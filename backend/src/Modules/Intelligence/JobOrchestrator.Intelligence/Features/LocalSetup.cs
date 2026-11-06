using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence.Features;

internal sealed record GpuInfo(string Vendor, string Name, int TotalMb, int UsedMb, int FreeMb);
internal sealed record Hardware(IReadOnlyList<GpuInfo> Gpus, string Cpu, int Threads, bool Avx2, bool Avx512, int RamTotalMb, int RamFreeMb, double DiskFreeGb, bool OllamaInstalled, string? OllamaVersion, bool OllamaRunning);

/// <summary>Um modelo do catálogo: tamanho no disco, VRAM para rodar inteiro na GPU, nota do bench (quando medido) e para quem serve.</summary>
internal sealed record CatalogModel(string Name, string Family, double DiskGb, double VramGb, int? Score, string Speed, string Note, bool Installed, int? BenchScore);
internal sealed record Recommendation(string Fast, string Strong, string Why, double BudgetGb);
internal sealed record LocalCatalog(Hardware Hardware, IReadOnlyList<CatalogModel> Models, Recommendation Recommendation, string? Fast, string? Strong);

internal sealed record InstallStep(string Name, string Status, string Detail, int Percent);
internal sealed record InstallView(bool Running, bool Done, bool Failed, IReadOnlyList<InstallStep> Steps);
internal sealed record InstallRequest(string[] Models, string? Fast, string? Strong);

/// <summary>
/// Modelo local sem sofrimento: detecta placa de vídeo, CPU, memória e disco; recomenda o modelo rápido e o forte que cabem;
/// instala o Ollama na pasta do usuário (sem sudo nem senha de administrador, com o SHA-256 conferido) e baixa os modelos com progresso.
/// Vale para Linux (NVIDIA, AMD ou só CPU), macOS (o chip da Apple usa a memória unificada pelo Metal) e Windows.
/// As escolhas vão para settings.json no diretório de configuração, que a API e o Worker leem sem reiniciar.
/// </summary>
internal sealed class LocalSetup(IHttpClientFactory http, IConfiguration config, ILogger<LocalSetup> log)
{
    private static readonly CatalogModel[] Known =
    [
        new("gemma4:e4b", "google", 6.6, 4.0, 99, "rápido", "Acerto máximo no bench e cabe inteiro numa placa de 6 GB. Melhor escolha para o modelo rápido.", false, null),
        new("qwen3.5:9b", "alibaba", 6.6, 7.5, 99, "médio", "Forte para textos longos e rascunhos com a nuvem fora. Com 8 GB divide com a CPU.", false, null),
        new("qwen3.5:4b", "alibaba", 3.3, 4.0, 97, "rápido", "Leve e preciso. Bom rápido para placas de 4 GB.", false, null),
        new("gemma4:e2b", "google", 4.6, 2.6, null, "rápido", "Para placas de 4 GB ou só CPU com 16 GB de RAM.", false, null),
        new("qwen3.5:2b", "alibaba", 2.7, 2.2, null, "rápido", "Roda até sem placa de vídeo, em CPU moderna.", false, null),
        new("qwen3.5:0.8b", "alibaba", 1.2, 1.0, null, "rápido", "Para máquina bem fraca. Só tarefas curtas.", false, null),
        new("gemma4:12b", "google", 7.7, 9.5, null, "médio", "Para placas de 12 GB ou mais.", false, null),
        new("gpt-oss:20b", "openai", 14, 15, null, "médio", "Para placas de 16 GB ou mais.", false, null),
        new("qwen3.5:27b", "alibaba", 17, 19, null, "lento", "Para placas de 24 GB.", false, null),
        new("qwen3:4b", "alibaba", 2.5, 3.0, 93, "rápido", "Versão anterior; os novos acertam mais.", false, null),
    ];

    private static readonly Lock Gate = new();
    private static List<InstallStep> steps = [];
    private static bool running, failed, done;

    private string Url => config["Llm:Ollama:Url"] ?? "http://127.0.0.1:11434";
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string OllamaDir => OllamaHost.Dir;

    public async Task<Hardware> HardwareAsync(CancellationToken ct)
    {
        var gpus = new List<GpuInfo>();
        if (CliRunner.Find("nvidia-smi") is { } smi)
        {
            var run = await CliRunner.RunAsync(smi, ["--query-gpu=name,memory.total,memory.used,memory.free", "--format=csv,noheader,nounits"], null, TimeSpan.FromSeconds(10), null, ct);
            foreach (var line in run.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var c = line.Split(',', StringSplitOptions.TrimEntries);
                if (c.Length == 4) gpus.Add(new GpuInfo("nvidia", c[0], Int(c[1]), Int(c[2]), Int(c[3])));
            }
        }
        // AMD no Linux: a VRAM vem do sysfs do amdgpu.
        foreach (var card in Directory.Exists("/sys/class/drm") ? Directory.GetDirectories("/sys/class/drm", "card?") : [])
        {
            var total = Path.Combine(card, "device", "mem_info_vram_total");
            var used = Path.Combine(card, "device", "mem_info_vram_used");
            if (!File.Exists(total)) continue;
            var t = (int)(long.Parse(File.ReadAllText(total).Trim()) / 1048576);
            var u = File.Exists(used) ? (int)(long.Parse(File.ReadAllText(used).Trim()) / 1048576) : 0;
            gpus.Add(new GpuInfo("amd", "Placa AMD", t, u, t - u));
        }
        var (model, ramTotal, ramFree) = Machine.Describe();
        // Chip da Apple: a GPU usa a memória unificada, e o Metal deixa uns dois terços dela para o modelo.
        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            gpus.Add(new GpuInfo("apple", $"{model} (GPU integrada)", ramTotal * 2 / 3, 0, ramTotal * 2 / 3));
        var models = Path.Combine(Home, ".ollama");
        var disk = new DriveInfo(Directory.Exists(models) ? models : Home).AvailableFreeSpace / 1e9;
        var binary = OllamaHost.Binary();
        string? version = null;
        try
        {
            using var doc = JsonDocument.Parse(await http.CreateClient("ollama").GetStringAsync($"{Url}/api/version", ct));
            version = doc.RootElement.GetProperty("version").GetString();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { }
        return new Hardware(gpus, model, Environment.ProcessorCount, System.Runtime.Intrinsics.X86.Avx2.IsSupported, System.Runtime.Intrinsics.X86.Avx512F.IsSupported,
            ramTotal, ramFree, Math.Round(disk, 1), binary is not null || OllamaHost.MacApp() is not null, version, version is not null);
    }

    public async Task<LocalCatalog> CatalogAsync(CancellationToken ct)
    {
        var hw = await HardwareAsync(ct);
        var installed = new HashSet<string>();
        var loadedVramMb = 0;
        try
        {
            var client = http.CreateClient("ollama");
            using var tags = JsonDocument.Parse(await client.GetStringAsync($"{Url}/api/tags", ct));
            foreach (var m in tags.RootElement.GetProperty("models").EnumerateArray()) installed.Add(m.GetProperty("name").GetString()!);
            using var ps = JsonDocument.Parse(await client.GetStringAsync($"{Url}/api/ps", ct));
            loadedVramMb = (int)(ps.RootElement.GetProperty("models").EnumerateArray().Sum(m => m.GetProperty("size_vram").GetInt64()) / 1048576);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { }
        var bench = Bench();
        var models = Known.Select(m => m with { Installed = installed.Contains(m.Name), BenchScore = bench.TryGetValue(m.Name, out var b) ? b : null }).ToList();
        return new LocalCatalog(hw, models, Recommend(hw, models, loadedVramMb), config["Llm:Ollama:Models:Local"], config["Llm:Ollama:Models:LocalStrong"]);
    }

    /// <summary>
    /// Rápido: o de melhor nota que cabe inteiro na VRAM que o desktop deixa livre. Forte: o melhor que cabe dividindo com a RAM.
    /// Sem placa de vídeo, modelos pequenos que rodam bem na CPU.
    /// </summary>
    internal static Recommendation Recommend(Hardware hw, IReadOnlyList<CatalogModel> models, int loadedVramMb)
    {
        var gpu = hw.Gpus.OrderByDescending(g => g.TotalMb).FirstOrDefault();
        int Rank(CatalogModel m) => m.BenchScore ?? m.Score ?? (int)(70 + Math.Min(25, m.VramGb * 2));
        if (gpu is null)
        {
            var fastCpu = hw.RamTotalMb >= 16_000 ? "qwen3.5:4b" : hw.RamTotalMb >= 8_000 ? "qwen3.5:2b" : "qwen3.5:0.8b";
            var strongCpu = hw.RamTotalMb >= 32_000 ? "qwen3.5:9b" : fastCpu;
            return new Recommendation(fastCpu, strongCpu, $"Sem placa de vídeo: modelos pequenos que rodam na CPU ({hw.Threads} threads, {hw.RamTotalMb / 1024} GB de RAM).", 0);
        }
        // O desktop fica com uma reserva estável (o uso agora, entre 1,5 GB e 30% da placa): streaming e navegador oscilam,
        // e a recomendação não pode trocar a cada minuto. O que o próprio Ollama ocupa agora não conta como desktop.
        var desktop = Math.Clamp(gpu.UsedMb - loadedVramMb, 1536, (int)(gpu.TotalMb * 0.3));
        var budget = Math.Round((gpu.TotalMb * 0.94 - desktop) / 1024.0, 1);
        var fits = models.Where(m => m.VramGb <= budget).OrderByDescending(Rank).ThenBy(m => m.VramGb).ToList();
        var fast = fits.FirstOrDefault(m => m.Speed == "rápido") ?? fits.FirstOrDefault() ?? models.OrderBy(m => m.VramGb).First();
        var offload = budget + Math.Min(hw.RamFreeMb / 1024.0 * 0.5, 8);
        var strong = models.Where(m => m.VramGb <= offload && m.Name != fast.Name && m.DiskGb >= fast.DiskGb).OrderByDescending(Rank).ThenByDescending(m => m.VramGb).FirstOrDefault() ?? fast;
        var why = $"{gpu.Name} com {gpu.TotalMb / 1024.0:0.0} GB, {budget:0.0} GB livres além do desktop. Rápido: {fast.Name}, inteiro na GPU. Forte: {strong.Name}"
            + (strong.VramGb > budget ? ", dividindo com a RAM quando precisa." : ".");
        return new Recommendation(fast.Name, strong.Name, why, budget);
    }

    private static Dictionary<string, int> Bench()
    {
        var path = Path.Combine(AppPaths.In(AppPaths.Data, "bench"), "latest.json");
        if (!File.Exists(path)) return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path))!.AsArray().Where(n => n is not null)
                .ToDictionary(n => (string)n!["model"]!, n => (int)Math.Round((double)n!["overall"]! * 100));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NullReferenceException) { return []; }
    }

    public InstallView Status()
    {
        lock (Gate) return new InstallView(running, done, failed, [.. steps]);
    }

    /// <summary>Começa a instalação em segundo plano. Uma por vez.</summary>
    public InstallView Start(InstallRequest r)
    {
        lock (Gate)
        {
            if (running) return Status();
            running = true; failed = false; done = false;
            steps = [new("Ollama", "waiting", "", 0), .. r.Models.Distinct().Select(m => new InstallStep(m, "waiting", "", 0)), new("Escolha dos modelos", "waiting", "", 0)];
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await EnsureOllamaAsync();
                foreach (var m in r.Models.Distinct()) await PullAsync(m);
                Update("Escolha dos modelos", "running", "Gravando a escolha.", 50);
                await SaveSlotsAsync(r.Fast, r.Strong);
                Update("Escolha dos modelos", "done", $"Rápido: {r.Fast ?? "sem mudança"}. Forte: {r.Strong ?? "sem mudança"}.", 100);
                lock (Gate) done = true;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Instalação local falhou");
                lock (Gate)
                {
                    failed = true;
                    var i = steps.FindIndex(s => s.Status == "running");
                    if (i >= 0) steps[i] = steps[i] with { Status = "failed", Detail = ex.Message.Length > 300 ? ex.Message[..300] : ex.Message };
                }
            }
            finally { lock (Gate) running = false; }
        });
        return Status();
    }

    private static void Update(string name, string status, string detail, int percent)
    {
        lock (Gate)
        {
            var i = steps.FindIndex(s => s.Name == name);
            if (i >= 0) steps[i] = new InstallStep(name, status, detail, percent);
        }
    }

    /// <summary>
    /// Ollama na pasta do usuário: baixa o pacote oficial da última versão no GitHub para este sistema (Linux, macOS ou Windows, x64 ou
    /// arm64), confere o SHA-256 publicado junto e extrai. No Linux com systemd, vira um serviço de usuário na fatia de recursos do
    /// orquestrador; no macOS e no Windows, o próprio app liga o Ollama quando sobe (OllamaHost). O app oficial do Ollama, se já
    /// estiver instalado no Mac, é aberto em vez de baixar outro.
    /// </summary>
    private async Task EnsureOllamaAsync()
    {
        var hw = await HardwareAsync(CancellationToken.None);
        var broken = OllamaHost.Incomplete();
        if (hw.OllamaRunning && !broken) { Update("Ollama", "done", $"Já instalado e no ar (versão {hw.OllamaVersion}).", 100); return; }
        var client = http.CreateClient("api");
        if ((OllamaHost.Binary() is null || broken) && OllamaHost.MacApp() is null)
        {
            OllamaHost.Busy = true;
            try
            {
                if (broken)
                {
                    Update("Ollama", "running", "A instalação anterior do Ollama ficou pela metade, sem o motor MLX. Instalando de novo.", 1);
                    log.LogWarning("Ollama install in {Dir} has no completion marker; reinstalling", OllamaDir);
                    OllamaHost.StopOwn();
                    Directory.Delete(OllamaDir, recursive: true);
                }
                Update("Ollama", "running", "Procurando a última versão oficial.", 2);
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ollama/ollama/releases/latest");
                req.Headers.UserAgent.ParseAdd("job-orchestrator");
                using var release = JsonDocument.Parse(await (await client.SendAsync(req)).Content.ReadAsStringAsync());
                var assets = release.RootElement.GetProperty("assets").EnumerateArray().Select(a => (Name: a.GetProperty("name").GetString()!, Url: a.GetProperty("browser_download_url").GetString()!)).ToList();
                var wanted = OllamaHost.Assets(assets.Select(a => a.Name).ToList(), hw.Gpus.Any(g => g.Vendor == "amd"));
                if (wanted.Count == 0) throw new InvalidOperationException($"O Ollama não publica pacote para {Platform.Os} {Platform.Arch}.");
                var sums = assets.First(a => a.Name == "sha256sum.txt");
                var checksums = (await client.GetStringAsync(sums.Url)).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length == 2).ToDictionary(p => p[1].Trim().TrimStart('.', '/'), p => p[0]);
                var downloads = AppPaths.In(AppPaths.Data, "downloads");
                Directory.CreateDirectory(OllamaDir);
                foreach (var name in wanted)
                {
                    var file = Path.Combine(downloads, name);
                    await DownloadAsync(client, assets.First(a => a.Name == name).Url, file, name);
                    Update("Ollama", "running", $"Conferindo o SHA-256 de {name}.", 80);
                    await using (var s = File.OpenRead(file))
                        if (!checksums.TryGetValue(name, out var expected) || !string.Equals(Convert.ToHexStringLower(await SHA256.HashDataAsync(s)), expected, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"O SHA-256 de {name} não confere com o publicado. Nada foi instalado.");
                    Update("Ollama", "running", $"Extraindo {name}.", 88);
                    await ExtractAsync(file, OllamaDir);
                    File.Delete(file);
                }
                await File.WriteAllTextAsync(OllamaHost.Marker, string.Join("\n", wanted) + "\n");
            }
            finally { OllamaHost.Busy = false; }
        }
        if (OperatingSystem.IsLinux() && OllamaHost.Systemd && OllamaHost.Binary() is { } bin && bin.StartsWith(OllamaDir, StringComparison.Ordinal))
        {
            Update("Ollama", "running", "Ligando o serviço de usuário.", 94);
            var units = Path.Combine(Home, ".config", "systemd", "user");
            Directory.CreateDirectory(units);
            await File.WriteAllTextAsync(Path.Combine(units, "jobs.slice"), "[Unit]\nDescription=Job Orchestrator (recursos limitados)\n\n[Slice]\nMemoryHigh=6G\nMemoryMax=8G\nCPUWeight=20\nIOWeight=20\n");
            await File.WriteAllTextAsync(Path.Combine(units, OllamaHost.Unit), $"""
                [Unit]
                Description=Ollama do Job Orchestrator (modelos locais)
                After=network-online.target

                [Service]
                ExecStart={bin} serve
                {string.Join("\n", OllamaHost.Env.Select(e => $"Environment={e.Key}={e.Value}"))}
                Slice=jobs.slice
                Restart=on-failure

                [Install]
                WantedBy=default.target

                """);
            await CliRunner.RunAsync("systemctl", ["--user", "daemon-reload"], null, TimeSpan.FromSeconds(30), null, CancellationToken.None);
            var start = await CliRunner.RunAsync("systemctl", ["--user", "enable", "--now", OllamaHost.Unit], null, TimeSpan.FromSeconds(60), null, CancellationToken.None);
            if (start.Exit != 0) throw new InvalidOperationException($"O serviço não subiu: {start.Stderr.Trim()}");
        }
        else
        {
            Update("Ollama", "running", "Ligando o Ollama.", 94);
            if (!OllamaHost.Start()) throw new InvalidOperationException("O Ollama foi instalado, mas não abriu. Abra o app do Ollama e tente de novo.");
        }
        for (var i = 0; i < 40 && !(await HardwareAsync(CancellationToken.None)).OllamaRunning; i++) await Task.Delay(1000);
        if (!(await HardwareAsync(CancellationToken.None)).OllamaRunning) throw new InvalidOperationException("O Ollama não respondeu em 127.0.0.1:11434.");
        Update("Ollama", "done", "Instalado na sua pasta, sem senha de administrador, e no ar.", 100);
    }

    /// <summary>
    /// .zip by .NET (the Windows package); tar packages by the system tar on macOS and Linux. The macOS package carries code-signature
    /// attributes in its tar headers, which .NET's TarFile rejects halfway ("The extended header contains invalid records"); .NET's
    /// reader stays only as the fallback when there is no tar.
    /// </summary>
    internal static async Task ExtractAsync(string file, string dir)
    {
        Directory.CreateDirectory(dir);
        if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { ZipFile.ExtractToDirectory(file, dir, overwriteFiles: true); return; }
        if (!OperatingSystem.IsWindows() && CliRunner.Find("tar") is { } system)
        {
            var run = await CliRunner.RunAsync(system, ["-C", dir, "-xf", file], null, TimeSpan.FromMinutes(10), null, CancellationToken.None);
            if (run.Exit != 0) throw new InvalidOperationException($"Não foi possível extrair {Path.GetFileName(file)}: {run.Stderr.Trim()}");
            return;
        }
        if (file.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await using var input = File.OpenRead(file);
            await using var gz = new GZipStream(input, CompressionMode.Decompress);
            await TarFile.ExtractToDirectoryAsync(gz, dir, overwriteFiles: true);
            return;
        }
        var tar = await CliRunner.RunAsync("tar", ["-C", dir, "-xf", file], null, TimeSpan.FromMinutes(10), null, CancellationToken.None);
        if (tar.Exit != 0) throw new InvalidOperationException($"Não foi possível extrair: {tar.Stderr.Trim()}");
    }

    private static async Task DownloadAsync(HttpClient client, string url, string file, string label)
    {
        using var res = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? 0;
        await using var input = await res.Content.ReadAsStreamAsync();
        await using var output = File.Create(file);
        var buffer = new byte[1 << 20];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total > 0) Update("Ollama", "running", $"Baixando {label}: {done / 1e6:0} de {total / 1e6:0} MB.", (int)(5 + 70.0 * done / total));
        }
    }

    /// <summary>Baixa o modelo pelo próprio Ollama, com o progresso que ele informa.</summary>
    private async Task PullAsync(string model)
    {
        Update(model, "running", "Começando.", 0);
        var body = new StringContent(JsonSerializer.Serialize(new { model, stream = true }), System.Text.Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/api/pull") { Content = body };
        using var res = await http.CreateClient("ollama").SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();
        using var reader = new StreamReader(await res.Content.ReadAsStreamAsync());
        while (await reader.ReadLineAsync() is { } line)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var e)) throw new InvalidOperationException(e.GetString());
            var status = root.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var percent = root.TryGetProperty("total", out var t) && t.GetInt64() > 0 && root.TryGetProperty("completed", out var c) ? (int)(100.0 * c.GetInt64() / t.GetInt64()) : 0;
            Update(model, "running", status.StartsWith("pulling", StringComparison.Ordinal) && percent > 0 ? $"Baixando: {percent}%" : status, Math.Min(99, percent));
            if (status == "success") break;
        }
        Update(model, "done", "Pronto.", 100);
    }

    /// <summary>Grava rápido e forte em settings.json (diretório de configuração); a configuração recarrega sozinha.</summary>
    public static async Task SaveSlotsAsync(string? fast, string? strong)
    {
        var path = Path.Combine(AppPaths.Config, "settings.json");
        var root = File.Exists(path) ? JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject() ?? [] : new JsonObject();
        var llm = root["Llm"] as JsonObject ?? new JsonObject();
        var ollama = llm["Ollama"] as JsonObject ?? new JsonObject();
        var models = ollama["Models"] as JsonObject ?? new JsonObject();
        if (fast is { Length: > 0 }) models["Local"] = fast;
        if (strong is { Length: > 0 }) models["LocalStrong"] = strong;
        ollama["Models"] = models;
        llm["Ollama"] = ollama;
        root["Llm"] = llm;
        await File.WriteAllTextAsync(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static int Int(string s) => int.TryParse(s.Trim(), out var n) ? n : 0;
}

/// <summary>Rotas HTTP (Carter) do modelo local.</summary>
internal sealed class LocalEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/local").WithTags("Intelligence");
        g.MapGet("catalog", async (LocalSetup setup, CancellationToken ct) => Results.Ok(await setup.CatalogAsync(ct)));
        g.MapGet("install", (LocalSetup setup) => Results.Ok(setup.Status()));
        g.MapPost("install", (InstallRequest body, LocalSetup setup) =>
            body.Models.All(m => System.Text.RegularExpressions.Regex.IsMatch(m, @"^[a-z0-9._-]+(:[a-z0-9._-]+)?$")) ? Results.Accepted(value: setup.Start(body)) : Results.BadRequest());
        g.MapPut("slots", async (InstallRequest body) =>
        {
            await LocalSetup.SaveSlotsAsync(body.Fast, body.Strong);
            return Results.NoContent();
        });
    }
}
