using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;

namespace JobOrchestrator.App;

/// <summary>
/// Abrir, subir, parar e conferir, igual nos três sistemas (o que muda fica em Desktop). Os serviços rodam em segundo plano, então a
/// busca segue com a janela fechada; a janela é o navegador do sistema em modo app, com perfil próprio.
/// </summary>
internal static class Launcher
{
    public const string Url = "http://127.0.0.1:7410";
    public const string Unit = "job-orchestrator";
    public static string Home => AppPaths.Resources;
    public static string Exe => Environment.ProcessPath ?? Desktop.Exe(Desktop.Source);
    public static string Version => typeof(Launcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static string SourceRevision => typeof(Launcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+').ElementAtOrDefault(1) ?? "unknown";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    /// <summary>Marca da instalação (versão, pasta e quando), gravada no fim do instalador.</summary>
    public static string Marker => Path.Combine(AppPaths.Config, "install.json");
    public static bool Installed => File.Exists(Marker) && File.Exists(Desktop.Exe(Desktop.Target));

    /// <summary>Roda da pasta instalada (no macOS, do pacote em ~/Applications).</summary>
    public static bool FromTarget => string.Equals(Path.GetFullPath(Desktop.Source), Path.GetFullPath(Desktop.Target).TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public static int Print(string text) { Console.WriteLine(text); return 0; }

    /// <summary>Read-only diagnosis of the running local instance; never includes profile facts, names, contacts or credentials.</summary>
    public static async Task<int> DiagnoseAsync()
    {
        try
        {
            using var diagnosticHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var health = System.Text.Json.JsonDocument.Parse(await diagnosticHttp.GetStringAsync($"{Url}/api/health"));
            using var overview = System.Text.Json.JsonDocument.Parse(await diagnosticHttp.GetStringAsync($"{Url}/api/overview"));
            using var applications = System.Text.Json.JsonDocument.Parse(await diagnosticHttp.GetStringAsync($"{Url}/api/applications"));
            var items = applications.RootElement.EnumerateArray().ToArray();
            var blocked = items.Where(a => a.GetProperty("state").GetString() is "NeedsHuman" or "AwaitingApproval" or "ManualHandoff" or "PausedProvider" or "Failed" or "DeadLetter");
            var reasons = blocked.GroupBy(a => new { State = a.GetProperty("state").GetString(), Reason = BlockReason(a.GetProperty("lastReason").GetString() ?? "") })
                .Select(g => new { g.Key.State, g.Key.Reason, Count = g.Count() }).OrderByDescending(g => g.Count).ToArray();
            var campaign = overview.RootElement.GetProperty("campaign");
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
            {
                Health = health.RootElement,
                Campaign = campaign.ValueKind == System.Text.Json.JsonValueKind.Object ? new
                {
                    Mode = campaign.GetProperty("mode"), MinFitScore = campaign.GetProperty("minFitScore"),
                    MaxSubmissionsPerDay = campaign.GetProperty("maxSubmissionsPerDay"), Sources = campaign.GetProperty("sources")
                } : null,
                Counts = overview.RootElement.GetProperty("applications"),
                ConfirmedToday = overview.RootElement.GetProperty("confirmedToday"),
                Blockers = reasons, SampledApplications = items.Length, SampleLimit = 500,
                Scope = "Leitura da instalação local. Não testa login, CAPTCHA ou envio externo e não envia dados."
            }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Console.Error.WriteLine("Não foi possível ler o diagnóstico. Abra o app e confira se a API local está disponível em 127.0.0.1:7410.");
            return 1;
        }
    }

    private static string BlockReason(string reason)
    {
        var text = reason.ToLowerInvariant();
        if (text.Contains("captcha") || text.Contains("anti-robô")) return "captcha";
        if (text.Contains("login") || text.Contains("código de verificação")) return "login";
        if (text.Contains("declaraç") || text.Contains("privacidade")) return "declaration";
        if (text.Contains("pergunta") || text.Contains("resposta aprovada") || text.Contains("marcar opções")) return "question";
        if (text.Contains("revisão cruzada") || text.Contains("revisado") || text.Contains("revisão do currículo")) return "resume-review";
        if (text.Contains("perfil") || text.Contains("sintético")) return "profile";
        if (text.Contains("provedor") || text.Contains("modelo")) return "provider";
        if (text.Contains("currículo")) return "resume";
        if (text.Contains("autorização") || text.Contains("aprovação")) return "authorization";
        if (text.Contains("limite") || text.Contains("teto")) return "limit";
        return "other";
    }

    public static async Task<int> OpenAsync()
    {
        var up = await HealthyAsync();
        if (!up) Desktop.Start(Exe, ServiceEnvironment());
        // No ar: o painel direto. Subindo: a abertura animada, que entra no painel sozinha quando ele responder.
        Desktop.Window(up ? Url : SplashUrl());
        return 0;
    }

    public static async Task<bool> HealthyAsync()
    {
        try { using var r = await Http.GetAsync($"{Url}/api/health"); return r.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public static string SplashUrl() =>
        Path.Combine(Home, "splash", "index.html") is var splash && File.Exists(splash) ? new Uri(splash).AbsoluteUri : Url;

    public static void Start(string exe) => Desktop.Start(exe, ServiceEnvironment());

    /// <summary>
    /// Para o serviço do sistema e o servidor: pelo pedido de parada com a chave desta sessão (o Windows não tem SIGTERM, e a parada
    /// com calma deixa o Worker terminar a etapa), e por fim pelo processo. O Postgres próprio para junto.
    /// </summary>
    public static async Task<int> StopAsync()
    {
        Desktop.StopService();
        var run = AppPaths.In(AppPaths.Data, "run");
        if (await HealthyAsync() && File.Exists(Path.Combine(run, "control.token")))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{Url}/api/app/stop");
                req.Headers.Add("X-Jo-Control", (await File.ReadAllTextAsync(Path.Combine(run, "control.token"))).Trim());
                using var _ = await Http.SendAsync(req);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
            for (var i = 0; i < 60 && await HealthyAsync(); i++) await Task.Delay(500);
        }
        var pidFile = Path.Combine(run, "server.pid");
        if (await HealthyAsync() && File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        }
        for (var i = 0; i < 30 && await HealthyAsync(); i++) await Task.Delay(500);
        if (PortablePostgres.Installed) await PortablePostgres.StopAsync(CancellationToken.None);
        return Print(await HealthyAsync() ? "Ainda respondendo em 127.0.0.1:7410 (modo de desenvolvimento?)." : "Serviços parados.");
    }

    public static async Task<int> StatusAsync()
    {
        var up = await HealthyAsync();
        Print(up ? $"No ar em {Url}." : "Parado.");
        Print($"Serviço: {Desktop.ServiceState()}");
        Print($"Banco: {(await PortOpenAsync(5462) ? "no ar em 127.0.0.1:5462" : "parado")}{(PortablePostgres.Installed ? " (Postgres próprio)" : "")}");
        Print($"Dados: {AppPaths.Data}");
        return up ? 0 : 1;
    }

    /// <summary>
    /// O Postgres no ar: se a porta 5462 não responde, liga o Postgres próprio (quando instalado) ou o contêiner do compose (mesmo
    /// projeto e volume do modo de desenvolvimento). Sem nenhum dos dois, manda rodar o instalador.
    /// </summary>
    public static async Task<bool> EnsureDatabaseAsync(TextWriter log)
    {
        if (await PortOpenAsync(5462)) return true;
        if (PortablePostgres.Installed)
        {
            await log.WriteLineAsync("Ligando o Postgres próprio...");
            if (await PortablePostgres.StartAsync(l => log.WriteLine(l), CancellationToken.None)) return true;
        }
        var compose = Compose();
        if (compose is null || Platform.Which("docker") is null)
        {
            await log.WriteLineAsync("Postgres fora do ar e sem banco instalado. Rode o instalador (job-orchestrator --setup).");
            return false;
        }
        await log.WriteLineAsync("Subindo o Postgres (docker compose)...");
        Run("docker", ["compose", "-p", "job-orchestrator", "-f", compose, "up", "-d", "--wait", "postgres"], wait: TimeSpan.FromMinutes(5));
        for (var i = 0; i < 60 && !await PortOpenAsync(5462); i++) await Task.Delay(1000);
        return await PortOpenAsync(5462);
    }

    public static string? Compose()
    {
        if (File.Exists(Path.Combine(Home, "compose.yaml"))) return Path.Combine(Home, "compose.yaml");
        for (var dir = new DirectoryInfo(Home); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "infra", "compose.yaml"))) return Path.Combine(dir.FullName, "infra", "compose.yaml");
        return null;
    }

    public static async Task<bool> PortOpenAsync(int port)
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await tcp.ConnectAsync("127.0.0.1", port, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
    }

    public static void Window(string url) => Desktop.Window(url);

    /// <summary>
    /// O ambiente dos serviços: o PATH da sua sessão (onde moram as CLIs do Claude e do Codex), guardado pelo instalador no arquivo env
    /// da pasta de configuração, mais as pastas de costume de cada sistema; e o IPv6 desligado no .NET, que trava sem rota.
    /// </summary>
    public static Dictionary<string, string> ServiceEnvironment()
    {
        var home = Platform.Home;
        var sep = Path.PathSeparator;
        var paths = new List<string>();
        var saved = Path.Combine(AppPaths.Config, "env");
        if (File.Exists(saved))
            foreach (var line in File.ReadAllLines(saved))
                if (line.StartsWith("PATH=", StringComparison.Ordinal)) paths.AddRange(line[5..].Split(sep));
        paths.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(sep));
        if (OperatingSystem.IsWindows())
            paths.AddRange([Path.Combine(home, ".local", "bin"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm")]);
        else
            paths.AddRange([Path.Combine(home, ".local", "bin"), Path.Combine(home, ".local", "share", "mise", "shims"), "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"]);
        var env = new Dictionary<string, string>
        {
            ["PATH"] = string.Join(sep, paths.Where(p => p.Length > 0).Distinct()),
            ["DOTNET_SYSTEM_NET_DISABLEIPV6"] = "1",
        };
        foreach (var k in new[] { "LANG", "XDG_DATA_HOME", "XDG_CONFIG_HOME", "JO_DATA_DIR", "JO_CONFIG_DIR" })
            if (Environment.GetEnvironmentVariable(k) is { Length: > 0 } v) env[k] = v;
        return env;
    }

    public static string? Which(string name) => Platform.Which(name);

    public static int Run(string file, string[] args, TimeSpan? wait = null)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            p.OutputDataReceived += (_, _) => { };
            p.ErrorDataReceived += (_, _) => { };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            if (!p.WaitForExit(wait ?? TimeSpan.FromSeconds(60))) { p.Kill(true); return -1; }
            return p.ExitCode;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return -1; }
    }

    public static string Capture(string file, string[] args) => Platform.Capture(file, args);
}
