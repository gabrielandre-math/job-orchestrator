using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.App;

internal sealed record Check(string Id, string Label, string Icon, string State, string Detail, string? Fix, bool Blocking);
internal sealed record SetupStep(string Id, string Label, string Icon, string State, string Detail);
internal sealed record InstallInput(bool Menu, bool Autostart, bool Migrate);

/// <summary>
/// O instalador: um servidor pequeno em 127.0.0.1 que serve a mesma interface do app na rota /instalar e faz o trabalho de verdade:
/// confere a máquina, copia os arquivos para ~/.local/opt/job-orchestrator, traz os dados do modo de desenvolvimento, sobe o banco,
/// prepara o coletor, cria o atalho e o serviço do usuário. Nada exige sudo; o que falta na máquina vira um comando para você copiar.
/// </summary>
internal static class Setup
{
    public static string Target => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "opt", "job-orchestrator");

    public static async Task<int> RunAsync()
    {
        var port = FreePort(7420);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        var installer = new Installer();
        var quit = new CancellationTokenSource();

        app.MapGet("/api/setup/info", () => Results.Ok(new
        {
            version = Launcher.Version, source = Launcher.Home, target = Target, installed = File.Exists(Path.Combine(Target, "job-orchestrator")),
            devData = Installer.DevData() is not null, running = installer.Running, done = installer.Done,
        }));
        app.MapGet("/api/setup/checks", async () => Results.Ok(await Checks.RunAsync()));
        app.MapPost("/api/setup/install", (InstallInput body) =>
        {
            if (installer.Running) return Results.Conflict();
            _ = Task.Run(() => installer.RunAsync(body));
            return Results.Accepted();
        });
        app.MapGet("/api/setup/state", () => Results.Ok(installer.Snapshot()));
        app.MapPost("/api/setup/launch", () =>
        {
            // O app instalado sobe em segundo plano; a janela do instalador vira a janela do app quando ele responder.
            Launcher.Start(Path.Combine(Target, "job-orchestrator"));
            _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => quit.Cancel(), TaskScheduler.Default);
            return Results.Ok(new { url = Launcher.Url });
        });
        app.MapPost("/api/setup/quit", () => { _ = Task.Delay(1500).ContinueWith(_ => quit.Cancel(), TaskScheduler.Default); return Results.Ok(); });
        // O app no ar responde a /api/health em outra porta; o instalador pergunta por aqui para não depender de CORS.
        app.MapGet("/api/setup/app-up", async () => Results.Ok(new { up = await Launcher.HealthyAsync() }));
        Server.Panel(app, Path.Combine(AppContext.BaseDirectory, "wwwroot"));

        await app.StartAsync(quit.Token);
        Console.WriteLine($"Instalador em http://127.0.0.1:{port}/instalar");
        Launcher.Window($"http://127.0.0.1:{port}/instalar");
        try { await Task.Delay(Timeout.Infinite, quit.Token); } catch (TaskCanceledException) { }
        await app.StopAsync();
        return 0;
    }

    private static int FreePort(int preferred)
    {
        for (var p = preferred; p < preferred + 20; p++)
        {
            try { using var l = new TcpListener(IPAddress.Loopback, p); l.Start(); l.Stop(); return p; }
            catch (SocketException) { }
        }
        return 0;
    }
}

/// <summary>Conferência da máquina antes de instalar: cada item diz o que achou e, se faltar algo, o comando para resolver.</summary>
internal static class Checks
{
    public static async Task<IReadOnlyList<Check>> RunAsync()
    {
        var list = new List<Check>
        {
            OperatingSystem.IsLinux() && Environment.Is64BitOperatingSystem
                ? new("system", "Sistema", "router", "ok", $"Linux de 64 bits ({Environment.OSVersion.Version}).", null, true)
                : new("system", "Sistema", "router", "fail", "Este executável é para Linux de 64 bits.", null, true),
        };
        // Docker: o banco (Postgres) roda num contêiner do compose, preso em 127.0.0.1.
        if (Launcher.Which("docker") is null)
            list.Add(new("docker", "Docker", "inbox", "fail", "O banco roda num contêiner do Docker, e o Docker não está instalado.", "sudo pacman -S docker docker-compose && sudo systemctl enable --now docker && sudo usermod -aG docker $USER", true));
        else
        {
            var info = Launcher.Capture("docker", ["info", "--format", "{{.ServerVersion}}"]).Trim();
            if (info.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
                list.Add(new("docker", "Docker", "inbox", "fail", "O seu usuário ainda não pode usar o Docker.", "sudo usermod -aG docker $USER  # e entre de novo na sessão", true));
            else if (info.Length == 0 || info.Contains("Cannot connect", StringComparison.OrdinalIgnoreCase) || info.Contains("error", StringComparison.OrdinalIgnoreCase))
                list.Add(new("docker", "Docker", "inbox", "fail", "O Docker está instalado, mas o serviço está parado.", "sudo systemctl enable --now docker", true));
            else
            {
                var compose = Launcher.Capture("docker", ["compose", "version", "--short"]).Trim();
                list.Add(compose.Length > 0 && char.IsDigit(compose.TrimStart('v')[0])
                    ? new("docker", "Docker", "inbox", "ok", $"Docker {info}, compose {compose.TrimStart('v')}.", null, true)
                    : new("docker", "Docker", "inbox", "fail", "Falta o plugin compose do Docker.", "sudo pacman -S docker-compose", true));
            }
        }
        var python = Launcher.Which("python3") is null ? "" : Launcher.Capture("python3", ["-c", "import sys; print('%d.%d' % sys.version_info[:2])"]).Trim();
        list.Add(Version.TryParse(python, out var pv) && pv >= new Version(3, 11)
            ? new("python", "Coletor (Python)", "radar", "ok", $"Python {python}: o Scrapy ganha um ambiente só dele.", null, false)
            : new("python", "Coletor (Python)", "radar", "warn", "Sem Python 3.11 ou mais novo, a varredura de sites fica desligada; o resto funciona.", "sudo pacman -S python", false));
        var browser = new[] { "chromium", "google-chrome-stable", "google-chrome", "brave" }.Select(Launcher.Which).FirstOrDefault(b => b is not null);
        list.Add(browser is not null
            ? new("browser", "Navegador", "reader", "ok", $"{Path.GetFileName(browser)}: janela do app, formulários e páginas com JavaScript.", null, false)
            : new("browser", "Navegador", "reader", "warn", "Sem Chromium, a janela abre no navegador padrão e as páginas com JavaScript não são lidas.", "sudo pacman -S chromium", false));
        list.Add(await OllamaAsync());
        var gpu = Launcher.Which("nvidia-smi") is null ? "" : Launcher.Capture("nvidia-smi", ["--query-gpu=name,memory.total", "--format=csv,noheader"]).Trim().Split('\n')[0];
        list.Add(gpu.Length > 0 && !gpu.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? new("gpu", "Placa de vídeo", "chart", "ok", $"{gpu}: o modelo local roda na GPU.", null, false)
            : new("gpu", "Placa de vídeo", "chart", "warn", "Sem GPU NVIDIA: o modelo local roda no processador, mais devagar.", null, false));
        var free = new DriveInfo(Path.GetPathRoot(Setup.Target) ?? "/").AvailableFreeSpace / 1024d / 1024 / 1024;
        list.Add(free >= 3
            ? new("disk", "Espaço em disco", "receipt", "ok", $"{free:0} GB livres.", null, true)
            : new("disk", "Espaço em disco", "receipt", free >= 1 ? "warn" : "fail", $"Só {free:0.0} GB livres; o banco e o coletor precisam de uns 3 GB.", null, free < 1));
        list.Add(Launcher.Which("secret-tool") is not null
            ? new("keyring", "Chaveiro do sistema", "guardian", "ok", "Senhas de e-mail e chaves de API ficam no gnome-keyring.", null, false)
            : new("keyring", "Chaveiro do sistema", "guardian", "warn", "Sem o secret-tool, e-mail e chaves de API ficam indisponíveis.", "sudo pacman -S libsecret gnome-keyring", false));
        if (await Launcher.HealthyAsync())
            list.Add(new("port", "Porta 7410", "gate", "warn", "Já tem um Job Orchestrator no ar (talvez o modo de desenvolvimento). A instalação assume o lugar dele no fim.", null, false));
        return list;
    }

    private static async Task<Check> OllamaAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:11434/api/version"));
            return new("ollama", "Modelo local (Ollama)", "model", "ok", $"No ar, versão {doc.RootElement.GetProperty("version").GetString()}.", null, false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            var installed = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "opt", "ollama", "bin", "ollama")) || Launcher.Which("ollama") is not null;
            return installed
                ? new("ollama", "Modelo local (Ollama)", "model", "warn", "Instalado, mas parado. O app liga sozinho quando precisar.", "systemctl --user start job-orchestrator-ollama", false)
                : new("ollama", "Modelo local (Ollama)", "model", "warn", "Sem modelo local: o app usa a sua assinatura (Claude ou ChatGPT). Dá para instalar depois em Provedores.", null, false);
        }
    }
}

/// <summary>Os passos da instalação, um de cada vez, com o que cada um está fazendo para a tela mostrar ao vivo.</summary>
internal sealed class Installer
{
    private readonly object _gate = new();
    private readonly List<SetupStep> _steps =
    [
        new("files", "Arquivos", "receipt", "waiting", "Copiar o app para a sua pasta."),
        new("data", "Seus dados", "identity", "waiting", "Trazer perfil, campanha e configurações."),
        new("database", "Banco", "inbox", "waiting", "Subir o Postgres no Docker."),
        new("scraper", "Coletor", "radar", "waiting", "Preparar o Scrapy e as bibliotecas de extração."),
        new("model", "Modelo local", "model", "waiting", "Conferir o Ollama."),
        new("shortcut", "Atalho", "router", "waiting", "Pôr o app no menu, com ícone."),
        new("service", "Serviço", "gate", "waiting", "Ligar os serviços em segundo plano."),
    ];
    private readonly List<string> _log = [];
    public bool Running { get; private set; }
    public bool Done { get; private set; }
    private string? _error;

    public object Snapshot()
    {
        lock (_gate) return new { running = Running, done = Done, error = _error, steps = _steps.ToList(), log = _log.TakeLast(14).ToList() };
    }

    public async Task RunAsync(InstallInput input)
    {
        lock (_gate) { Running = true; Done = false; _error = null; for (var i = 0; i < _steps.Count; i++) _steps[i] = _steps[i] with { State = "waiting" }; }
        try
        {
            await StepAsync("files", CopyFiles);
            await StepAsync("data", () => Task.FromResult(input.Migrate ? Migrate() : "Nada para trazer."));
            await StepAsync("database", Database);
            await StepAsync("scraper", Scraper);
            await StepAsync("model", Model);
            await StepAsync("shortcut", () => Task.FromResult(input.Menu ? Shortcut() : "Sem atalho no menu (dá para criar depois rodando o instalador de novo)."));
            await StepAsync("service", () => Service(input.Autostart));
            lock (_gate) Done = true;
        }
        catch (InvalidOperationException ex) { lock (_gate) _error = ex.Message; }
        finally { lock (_gate) Running = false; }
    }

    private async Task StepAsync(string id, Func<Task<string>> work)
    {
        Set(id, "working", null);
        try { Set(id, "done", await work()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            Set(id, "failed", ex.Message);
            throw new InvalidOperationException($"{_steps.First(s => s.Id == id).Label}: {ex.Message}");
        }
    }

    private void Set(string id, string state, string? detail)
    {
        lock (_gate)
        {
            var i = _steps.FindIndex(s => s.Id == id);
            _steps[i] = _steps[i] with { State = state, Detail = detail ?? _steps[i].Detail };
        }
    }

    private void Log(string line) { lock (_gate) { _log.Add(line); if (_log.Count > 400) _log.RemoveRange(0, 100); } }

    private Task<string> CopyFiles()
    {
        var source = Path.GetFullPath(Launcher.Home).TrimEnd('/');
        var target = Setup.Target;
        if (source == target.TrimEnd('/')) return Task.FromResult("O app já roda da pasta instalada.");
        // Serviço antigo no ar segura o binário: para antes de copiar por cima.
        Launcher.Run("systemctl", ["--user", "stop", $"{Launcher.Unit}.service"]);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.StartsWith("..", StringComparison.Ordinal)) continue;
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
            if (++count % 200 == 0) Log($"{count} arquivos copiados");
        }
        var exe = Path.Combine(target, "job-orchestrator");
        if (File.Exists(exe) && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Log($"{count} arquivos em {target}");
        return Task.FromResult($"{count} arquivos em ~/.local/opt/job-orchestrator.");
    }

    /// <summary>A pasta .dev do modo de desenvolvimento que gerou este pacote (o empacotador anota onde fica).</summary>
    public static string? DevData()
    {
        var note = Path.Combine(Launcher.Home, "dev-data.txt");
        var dev = File.Exists(note) ? File.ReadAllText(note).Trim() : "";
        return dev.Length > 0 && Directory.Exists(dev) ? dev : null;
    }

    /// <summary>Configurações e arquivos do modo de desenvolvimento vão para as pastas do app, sem apagar nada que já esteja lá. O banco é o mesmo.</summary>
    private static readonly HashSet<string> Disposable = ["scraper-cache", "scraper-venv", "llm-sandbox", "run", "window", "httpcache", "logs"];

    private string Migrate()
    {
        if (DevData() is not { } dev) return "Nada para trazer.";
        var copied = 0;
        foreach (var (from, to) in new[] { (Path.Combine(dev, "config"), AppPaths.Config), (Path.Combine(dev, "data"), AppPaths.Data) })
        {
            if (!Directory.Exists(from)) continue;
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                // Cache, ambiente do Python, perfil da janela e arquivos de execução se refazem sozinhos: não vale copiar.
                var rel = Path.GetRelativePath(from, file);
                if (Disposable.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue;
                var dest = Path.Combine(to, rel);
                if (File.Exists(dest)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest);
                copied++;
            }
        }
        Log($"{copied} arquivos trazidos de {dev}");
        return copied > 0 ? $"{copied} arquivos trazidos (perfil, configurações, Explorador)." : "Tudo já estava no lugar.";
    }

    private async Task<string> Database()
    {
        if (await Launcher.PortOpenAsync(5462)) return "Postgres já no ar (127.0.0.1:5462).";
        var compose = Path.Combine(Setup.Target, "compose.yaml");
        if (!File.Exists(compose)) compose = Launcher.Compose() ?? throw new InvalidOperationException("compose.yaml não encontrado.");
        Log("docker compose up -d --wait postgres");
        var exit = await StreamAsync("docker", ["compose", "-p", "job-orchestrator", "-f", compose, "up", "-d", "--wait", "postgres"], TimeSpan.FromMinutes(8));
        if (exit != 0 || !await Launcher.PortOpenAsync(5462)) throw new InvalidOperationException("O Postgres não subiu. Confira o Docker e tente de novo.");
        return "Postgres no ar em 127.0.0.1:5462, com volume próprio.";
    }

    private async Task<string> Scraper()
    {
        var python = Launcher.Which("python3");
        if (python is null) return "Sem Python: a varredura de sites fica desligada.";
        var venv = Path.Combine(AppPaths.Data, "scraper-venv");
        if (!File.Exists(Path.Combine(venv, "bin", "pip")))
        {
            Log("python3 -m venv scraper-venv");
            if (await StreamAsync(python, ["-m", "venv", venv], TimeSpan.FromMinutes(3)) != 0) throw new InvalidOperationException("Não foi possível criar o ambiente do Python.");
        }
        Log("pip install (Scrapy, extruct, trafilatura, dateparser, price-parser)");
        var exit = await StreamAsync(Path.Combine(venv, "bin", "pip"), ["install", "-q", "--disable-pip-version-check", "-e", Path.Combine(Setup.Target, "scraper")], TimeSpan.FromMinutes(10));
        if (exit != 0) throw new InvalidOperationException("O pip não instalou o coletor. Confira a internet e tente de novo.");
        return "Scrapy e as bibliotecas de extração prontos.";
    }

    private static async Task<string> Model()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync("http://127.0.0.1:11434/api/tags"));
            var models = doc.RootElement.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).ToList();
            return models.Count > 0 ? $"Ollama no ar com {models.Count} {(models.Count == 1 ? "modelo" : "modelos")}: {string.Join(", ", models.Take(3))}." : "Ollama no ar, sem modelo: escolha um em Provedores.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException)
        {
            return "Sem Ollama no ar: o app usa a sua assinatura; o modelo local se instala depois em Provedores.";
        }
    }

    /// <summary>Atalho no menu de aplicativos, com o ícone do app, e o comando job-orchestrator em ~/.local/bin.</summary>
    private string Shortcut()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var exe = Path.Combine(Setup.Target, "job-orchestrator");
        var icons = Path.Combine(home, ".local", "share", "icons", "hicolor", "scalable", "apps");
        Directory.CreateDirectory(icons);
        var icon = Path.Combine(Setup.Target, "job-orchestrator.svg");
        if (File.Exists(icon)) File.Copy(icon, Path.Combine(icons, "job-orchestrator.svg"), overwrite: true);
        var apps = Path.Combine(home, ".local", "share", "applications");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "job-orchestrator.desktop"), $"""
            [Desktop Entry]
            Type=Application
            Name=Job Orchestrator
            GenericName=Busca de vagas
            Comment=Procura vagas de qualquer área, monta o currículo certo e cuida das candidaturas, na sua máquina
            Exec="{exe}"
            Icon=job-orchestrator
            Terminal=false
            Categories=Office;Network;
            Keywords=vagas;emprego;currículo;candidatura;jobs;
            StartupWMClass=JobOrchestrator
            Actions=Stop;Setup;

            [Desktop Action Stop]
            Name=Parar os serviços
            Exec="{exe}" --stop

            [Desktop Action Setup]
            Name=Abrir o instalador
            Exec="{exe}" --setup
            """);
        var bin = Path.Combine(home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var link = Path.Combine(bin, "job-orchestrator");
        if (File.Exists(link) || Directory.Exists(link)) File.Delete(link);
        File.CreateSymbolicLink(link, exe);
        if (Launcher.Which("update-desktop-database") is not null) Launcher.Run("update-desktop-database", [apps]);
        Log("atalho em ~/.local/share/applications e comando job-orchestrator");
        return "No menu de aplicativos, com ícone; no terminal, job-orchestrator.";
    }

    /// <summary>
    /// Serviço do usuário: com "abrir junto com o computador", uma unit em ~/.config/systemd/user ligada no login (a busca roda sozinha);
    /// sem, os serviços sobem agora e quando você abrir o app. O PATH da sua sessão fica guardado para as CLIs do Claude e do Codex.
    /// </summary>
    private async Task<string> Service(bool autostart)
    {
        File.WriteAllText(Path.Combine(AppPaths.Config, "env"), $"PATH={Environment.GetEnvironmentVariable("PATH")}\n");
        var exe = Path.Combine(Setup.Target, "job-orchestrator");
        var unit = Path.Combine(Launcher.UserUnits, $"{Launcher.Unit}.service");
        if (autostart)
        {
            Directory.CreateDirectory(Launcher.UserUnits);
            var env = Launcher.ServiceEnvironment();
            File.WriteAllText(unit, $"""
                [Unit]
                Description=Job Orchestrator (API, painel e Worker)
                After=network-online.target

                [Service]
                Type=notify
                ExecStart="{exe}" --serve
                Restart=on-failure
                RestartSec=5
                TimeoutStartSec=300
                Slice=jobs.slice
                MemoryMax=8G
                CPUWeight=50
                {string.Join("\n", env.Select(e => $"Environment=\"{e.Key}={e.Value}\""))}

                [Install]
                WantedBy=default.target
                """);
            Launcher.Run("systemctl", ["--user", "daemon-reload"]);
            Launcher.Run("systemctl", ["--user", "enable", $"{Launcher.Unit}.service"]);
        }
        else if (File.Exists(unit))
        {
            Launcher.Run("systemctl", ["--user", "disable", "--now", $"{Launcher.Unit}.service"]);
            File.Delete(unit);
            Launcher.Run("systemctl", ["--user", "daemon-reload"]);
        }
        // Um app no ar fora da pasta instalada (o modo de desenvolvimento ou um pacote antigo) sai antes: dois Workers no mesmo banco não.
        var taken = await TakeoverAsync(exe);
        if (taken > 0) Log($"{taken} {(taken == 1 ? "processo antigo encerrado" : "processos antigos encerrados")} para o instalado assumir");
        if (autostart) Launcher.Run("systemctl", ["--user", "start", $"{Launcher.Unit}.service"]);
        else Launcher.Start(exe);
        for (var i = 0; i < 90 && !await Launcher.HealthyAsync(); i++) await Task.Delay(1000);
        if (!await Launcher.HealthyAsync()) throw new InvalidOperationException("Os serviços não responderam em 90 segundos. Rode job-orchestrator --serve no terminal para ver o motivo.");
        return autostart ? "No ar, e liga sozinho quando você entrar no computador." : "No ar; sobe de novo sempre que você abrir o app.";
    }

    /// <summary>Encerra a API e o Worker do modo de desenvolvimento e qualquer job-orchestrator --serve de outra pasta; espera a porta 7410 ficar livre.</summary>
    private static async Task<int> TakeoverAsync(string installed)
    {
        var count = 0;
        foreach (var dir in Directory.Exists("/proc") ? Directory.EnumerateDirectories("/proc") : [])
        {
            if (!int.TryParse(Path.GetFileName(dir), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) || pid == Environment.ProcessId) continue;
            string cmd;
            try { cmd = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ').Trim(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            var dev = cmd.Contains("JobOrchestrator.Api.dll", StringComparison.Ordinal) || cmd.Contains("JobOrchestrator.Worker.dll", StringComparison.Ordinal);
            var other = cmd.Contains("job-orchestrator --serve", StringComparison.Ordinal) && !cmd.StartsWith(installed, StringComparison.Ordinal);
            if (!dev && !other) continue;
            // SIGTERM: a API e o Worker encerram com calma (o Worker termina a etapa e grava a intenção; nada é reenviado).
            if (Launcher.Run("kill", ["-TERM", pid.ToString(CultureInfo.InvariantCulture)]) == 0) count++;
        }
        for (var i = 0; i < 40 && count > 0 && await Launcher.HealthyAsync(); i++) await Task.Delay(500);
        return count;
    }

    private async Task<int> StreamAsync(string file, string[] args, TimeSpan limit)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = new Process { StartInfo = info };
        p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) Log(l.Length > 160 ? l[..160] : l); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) Log(l.Length > 160 ? l[..160] : l); };
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(limit);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); return -1; }
        return p.ExitCode;
    }
}
