using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.App;

/// <summary>
/// Abrir, subir, parar e conferir. Os serviços rodam em segundo plano como unidade do systemd do usuário (job-orchestrator, na fatia
/// jobs.slice e com teto de memória), então a busca segue com a janela fechada. A janela é o navegador do sistema em modo app, com perfil
/// próprio; sem navegador conhecido, abre no padrão.
/// </summary>
internal static class Launcher
{
    public const string Url = "http://127.0.0.1:7410";
    public const string Unit = "job-orchestrator";
    public static string Home => AppContext.BaseDirectory;
    public static string Exe => Environment.ProcessPath ?? Path.Combine(Home, "job-orchestrator");
    public static string Version => typeof(Launcher).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static string UserUnits => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "systemd", "user");
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public static int Print(string text) { Console.WriteLine(text); return 0; }

    public static async Task<int> OpenAsync()
    {
        var up = await HealthyAsync();
        if (!up) Start(Exe);
        // No ar: o painel direto. Subindo: a abertura animada, que entra no painel sozinha quando ele responder.
        Window(up ? Url : SplashUrl());
        return 0;
    }

    public static async Task<bool> HealthyAsync()
    {
        try { using var r = await Http.GetAsync($"{Url}/api/health"); return r.IsSuccessStatusCode; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return false; }
    }

    public static string SplashUrl() =>
        Path.Combine(Home, "splash", "index.html") is var splash && File.Exists(splash) ? new Uri(splash).AbsoluteUri : Url;

    /// <summary>Sobe o servidor em segundo plano: pelo serviço do usuário quando instalado, senão por uma unidade passageira do systemd, senão solto.</summary>
    public static void Start(string exe)
    {
        var env = ServiceEnvironment();
        if (Which("systemctl") is not null && File.Exists(Path.Combine(UserUnits, $"{Unit}.service")))
        {
            if (Run("systemctl", ["--user", "start", $"{Unit}.service"]) == 0) return;
        }
        if (Which("systemd-run") is not null)
        {
            Run("systemctl", ["--user", "reset-failed", $"{Unit}.service"]);
            var a = new List<string> { "--user", $"--unit={Unit}", "--collect", "--quiet", "--slice=jobs.slice", "-p", "MemoryMax=8G", "-p", "CPUWeight=50" };
            a.AddRange(env.Select(e => $"--setenv={e.Key}={e.Value}"));
            a.AddRange([exe, "--serve"]);
            if (Run("systemd-run", [.. a]) == 0) return;
        }
        Detach(exe, ["--serve"], env, Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.log"));
    }

    public static async Task<int> StopAsync()
    {
        if (Which("systemctl") is not null) Run("systemctl", ["--user", "stop", $"{Unit}.service"]);
        var pidFile = Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.pid");
        if (await HealthyAsync() && File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid))
        {
            try { Process.GetProcessById(pid).Kill(entireProcessTree: true); } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        }
        for (var i = 0; i < 30 && await HealthyAsync(); i++) await Task.Delay(500);
        return Print(await HealthyAsync() ? "Ainda respondendo em 127.0.0.1:7410 (modo de desenvolvimento?)." : "Serviços parados.");
    }

    public static async Task<int> StatusAsync()
    {
        var up = await HealthyAsync();
        Print(up ? $"No ar em {Url}." : "Parado.");
        if (Which("systemctl") is not null) Print($"Serviço: {Capture("systemctl", ["--user", "is-active", $"{Unit}.service"]).Trim()}");
        return up ? 0 : 1;
    }

    /// <summary>O Postgres do compose no ar: se a porta 5462 não responde, sobe o contêiner (mesmo projeto e volume do modo de desenvolvimento).</summary>
    public static async Task<bool> EnsureDatabaseAsync(TextWriter log)
    {
        if (await PortOpenAsync(5462)) return true;
        var compose = Compose();
        if (compose is null || Which("docker") is null)
        {
            await log.WriteLineAsync("Postgres fora do ar e sem Docker ou compose.yaml para subir. Rode o instalador (job-orchestrator --setup).");
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

    /// <summary>A janela do app: navegador do sistema em modo app, com perfil próprio (não mistura com o seu navegador) e classe para o gerenciador de janelas.</summary>
    public static void Window(string url)
    {
        // Teste e uso sem tela (JO_NO_WINDOW=1): só diz o endereço.
        if (System.Environment.GetEnvironmentVariable("JO_NO_WINDOW") is { Length: > 0 }) { Console.WriteLine($"Janela: {url}"); return; }
        var profile = AppPaths.In(AppPaths.Data, "window");
        foreach (var b in new[] { "chromium", "google-chrome-stable", "google-chrome", "brave", "brave-browser", "microsoft-edge-stable", "vivaldi-stable" })
            if (Which(b) is { } path)
            {
                var args = new List<string> { $"--app={url}", "--class=JobOrchestrator", "--name=JobOrchestrator", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check", "--window-size=1440,920" };
                // A extensão do copiloto já vem ligada na janela do app: a vaga aberta daqui tem o botão de preencher, sem instalar nada.
                var extension = Path.Combine(Home, "extension");
                if (File.Exists(Path.Combine(extension, "manifest.json"))) args.AddRange([$"--load-extension={extension}", "--disable-features=DisableLoadExtensionCommandLineSwitch"]);
                Detach(path, [.. args], null);
                return;
            }
        Detach("xdg-open", [url], null);
    }

    /// <summary>
    /// O ambiente dos serviços: o PATH da sua sessão (onde moram as CLIs do Claude e do Codex), guardado pelo instalador em
    /// ~/.config/job-orchestrator/env, mais ~/.local/bin e os atalhos do mise; e o IPv6 desligado no .NET, que trava sem rota.
    /// </summary>
    public static Dictionary<string, string> ServiceEnvironment()
    {
        var home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var paths = new List<string>();
        var saved = Path.Combine(AppPaths.Config, "env");
        if (File.Exists(saved))
            foreach (var line in File.ReadAllLines(saved))
                if (line.StartsWith("PATH=", StringComparison.Ordinal)) paths.AddRange(line[5..].Split(':'));
        paths.AddRange((System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(':'));
        paths.AddRange([Path.Combine(home, ".local", "bin"), Path.Combine(home, ".local", "share", "mise", "shims"), "/usr/local/bin", "/usr/bin", "/bin"]);
        var env = new Dictionary<string, string>
        {
            ["PATH"] = string.Join(':', paths.Where(p => p.Length > 0).Distinct()),
            ["DOTNET_SYSTEM_NET_DISABLEIPV6"] = "1",
        };
        foreach (var k in new[] { "LANG", "XDG_DATA_HOME", "XDG_CONFIG_HOME" })
            if (System.Environment.GetEnvironmentVariable(k) is { Length: > 0 } v) env[k] = v;
        return env;
    }

    public static string? Which(string name)
    {
        if (name.Contains('/')) return File.Exists(name) ? name : null;
        foreach (var dir in (System.Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin").Split(':').Append("/usr/bin"))
            if (dir.Length > 0 && File.Exists(Path.Combine(dir, name))) return Path.Combine(dir, name);
        return null;
    }

    public static int Run(string file, string[] args, TimeSpan? wait = null)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
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

    public static string Capture(string file, string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit(30_000);
            return output;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return ""; }
    }

    /// <summary>Processo solto da janela ou do terminal: sobrevive quando quem abriu fecha, com a saída num log (ou em lugar nenhum).</summary>
    public static void Detach(string file, string[] args, IReadOnlyDictionary<string, string>? env, string? log = null)
    {
        try
        {
            var info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add($"exec {(Which("setsid") is not null ? "setsid " : "")}\"$0\" \"$@\" >>'{(log ?? "/dev/null").Replace("'", "")}' 2>&1 </dev/null");
            info.ArgumentList.Add(file);
            foreach (var a in args) info.ArgumentList.Add(a);
            foreach (var (k, v) in env ?? new Dictionary<string, string>()) info.Environment[k] = v;
            using var p = Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Console.Error.WriteLine($"Não abriu {file}: {ex.Message}"); }
    }
}
