using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;

namespace JobOrchestrator.App;

internal sealed record Check(string Id, string Label, string Icon, string State, string Detail, string? Fix, bool Blocking);
internal sealed record SetupStep(string Id, string Label, string Icon, string State, string Detail);
internal sealed record InstallInput(bool Menu, bool Autostart, bool Migrate);
internal sealed record SetupOption(string Label, string Hint);

/// <summary>
/// O instalador: um servidor pequeno em 127.0.0.1 que serve a mesma interface do app na rota /instalar e faz o trabalho de verdade nos
/// três sistemas: confere a máquina, copia o app para a pasta do usuário, traz os dados do modo de desenvolvimento, liga o banco
/// (o Postgres próprio, sem Docker), prepara o coletor (o Python dele vem pelo uv), cria o atalho e o serviço. Nada pede senha de
/// administrador; o que falta na máquina vira um comando ou um link para você.
/// </summary>
internal static class Setup
{
    public static string Target => Desktop.Target;

    public static async Task<int> RunAsync()
    {
        var port = FreePort(7420);
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppPaths.Resources });
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        var app = builder.Build();
        var installer = new Installer();
        var quit = new CancellationTokenSource();

        app.MapGet("/api/setup/info", () => Results.Ok(new
        {
            version = Launcher.Version, os = Platform.Os, osLabel = $"{Platform.Describe()}, {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}",
            source = Desktop.Source, target = Target, installed = Launcher.Installed, devData = Installer.DevData() is not null, running = installer.Running, done = installer.Done,
            options = Options(),
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
            Launcher.Start(Desktop.Exe(Target));
            _ = Task.Delay(TimeSpan.FromSeconds(90)).ContinueWith(_ => quit.Cancel(), TaskScheduler.Default);
            return Results.Ok(new { url = Launcher.Url });
        });
        app.MapPost("/api/setup/quit", () => { _ = Task.Delay(1500).ContinueWith(_ => quit.Cancel(), TaskScheduler.Default); return Results.Ok(); });
        // O app no ar responde a /api/health em outra porta; o instalador pergunta por aqui para não depender de CORS.
        app.MapGet("/api/setup/app-up", async () => Results.Ok(new { up = await Launcher.HealthyAsync() }));
        Server.Panel(app, Path.Combine(AppPaths.Resources, "wwwroot"));

        await app.StartAsync(quit.Token);
        Console.WriteLine($"Instalador em http://127.0.0.1:{port}/instalar");
        Launcher.Window($"http://127.0.0.1:{port}/instalar");
        try { await Task.Delay(Timeout.Infinite, quit.Token); } catch (TaskCanceledException) { }
        await app.StopAsync();
        Payload.CleanupLater();
        return 0;
    }

    /// <summary>Os textos das opções no nome de cada sistema.</summary>
    private static object Options() => new
    {
        menu = OperatingSystem.IsMacOS() ? new SetupOption("App em Aplicativos", "O pacote vai para ~/Applications: abre pelo Launchpad, pelo Spotlight ou pelo Dock.")
            : OperatingSystem.IsWindows() ? new SetupOption("Atalho no menu Iniciar", "Com ícone, também na área de trabalho; desinstala por Configurações > Aplicativos.")
            : new SetupOption("Atalho no menu de aplicativos", "Com o ícone do app; no terminal, o comando job-orchestrator."),
        autostart = OperatingSystem.IsMacOS() ? new SetupOption("Abrir junto com o Mac", "A busca roda sozinha mesmo com a janela fechada, pelo launchd do seu usuário.")
            : OperatingSystem.IsWindows() ? new SetupOption("Abrir junto com o Windows", "A busca roda sozinha mesmo com a janela fechada, sem janela de console.")
            : new SetupOption("Abrir junto com o computador", "A busca roda sozinha mesmo com a janela fechada. Os serviços ficam na fatia jobs.slice, com teto de memória."),
        scan = "Conferindo o sistema, o banco, o navegador, o coletor, o modelo local, a placa de vídeo, o disco e o chaveiro…",
    };

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

/// <summary>Conferência da máquina antes de instalar: cada item diz o que achou e, se faltar algo, o comando ou o link para resolver.</summary>
internal static class Checks
{
    public static async Task<IReadOnlyList<Check>> RunAsync()
    {
        var list = new List<Check> { SystemCheck() };
        list.Add(await DatabaseAsync());
        list.Add(Scraper());
        var browser = Platform.Browser();
        list.Add(browser is not null
            ? new("browser", "Navegador", "reader", "ok", $"{BrowserName(browser)}: janela do app, formulários e páginas com JavaScript.", null, false)
            : new("browser", "Navegador", "reader", "warn", "Sem Chrome, Edge, Chromium ou Brave: a janela abre no navegador padrão e as páginas com JavaScript não são lidas.",
                OperatingSystem.IsMacOS() ? "https://www.google.com/chrome/" : OperatingSystem.IsWindows() ? "winget install Google.Chrome" : Install("chromium", "chromium", "chromium", "chromium"), false));
        list.Add(await OllamaAsync());
        list.Add(Gpu());
        var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(AppPaths.Data)) ?? "/").AvailableFreeSpace / 1024d / 1024 / 1024;
        list.Add(free >= 3
            ? new("disk", "Espaço em disco", "receipt", "ok", $"{free:0} GB livres.", null, true)
            : new("disk", "Espaço em disco", "receipt", free >= 1.5 ? "warn" : "fail", $"Só {free:0.0} GB livres; o banco, o coletor e o Python dele precisam de uns 3 GB.", null, free < 1.5));
        list.Add(Secrets.Available
            ? new("keyring", "Chaveiro do sistema", "guardian", "ok", $"Senhas de e-mail e chaves de API ficam no {Secrets.StoreName}.", null, false)
            : new("keyring", "Chaveiro do sistema", "guardian", "warn", "Sem o secret-tool, e-mail e chaves de API ficam indisponíveis.", Install("libsecret-tools gnome-keyring", "libsecret gnome-keyring", "libsecret gnome-keyring", "libsecret-tools gnome-keyring"), false));
        if (await Launcher.HealthyAsync())
            list.Add(new("port", "Porta 7410", "gate", "warn", "Já tem um Job Orchestrator no ar (talvez uma versão anterior ou o modo de desenvolvimento). A instalação assume o lugar dele no fim.", null, false));
        return list;
    }

    private static Check SystemCheck()
    {
        var label = $"{Platform.Describe()}, {RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()}";
        if (!Environment.Is64BitOperatingSystem) return new("system", "Sistema", "router", "fail", $"{label}: o app precisa de um sistema de 64 bits.", null, true);
        if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return new("system", "Sistema", "router", "fail", $"{label}: o app precisa do Windows 10 versão 1809 ou mais novo.", null, true);
        return new("system", "Sistema", "router", "ok", $"{label}.", null, true);
    }

    /// <summary>O banco: o que já está no ar, o Postgres próprio, o contêiner com os seus dados ou, sem nada disso, o Postgres que o instalador baixa.</summary>
    private static async Task<Check> DatabaseAsync()
    {
        if (await Launcher.PortOpenAsync(5462)) return new("database", "Banco", "inbox", "ok", "Postgres já no ar em 127.0.0.1:5462: a instalação usa esse.", null, true);
        if (PortablePostgres.Installed) return new("database", "Banco", "inbox", "ok", "Postgres próprio já instalado, com os seus dados.", null, true);
        if (Installer.DockerVolume()) return new("database", "Banco", "inbox", "ok", "Postgres do Docker com os seus dados (volume job-orchestrator_jo-pg): a instalação sobe esse.", null, true);
        return PortablePostgres.Platform() is not null
            ? new("database", "Banco", "inbox", "ok", $"O PostgreSQL {PortablePostgres.Version[..2]} próprio (uns 60 MB) é baixado e conferido na instalação. Não precisa de Docker.", null, true)
            : new("database", "Banco", "inbox", "fail", $"Sem Postgres pronto para {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}.", null, true);
    }

    private static Check Scraper()
    {
        var venv = Path.Combine(AppPaths.Data, "scraper-venv");
        if (File.Exists(Installer.Scrapy(venv)))
            return new("python", "Coletor (Python)", "radar", "ok", "Scrapy já pronto num ambiente só dele; a instalação só atualiza as bibliotecas.", null, false);
        return Uv.Asset() is not null
            ? new("python", "Coletor (Python)", "radar", "ok", "O Python 3.12 do coletor vem pelo uv, numa pasta só dele; o Python do sistema fica como está.", null, false)
            : new("python", "Coletor (Python)", "radar", "warn", "Sem uv para este sistema: o coletor usa o Python do sistema, se houver um 3.11 ou mais novo.", null, false);
    }

    private static Check Gpu()
    {
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64
                ? new("gpu", "Placa de vídeo", "chart", "ok", "GPU do chip da Apple (Metal), com a memória unificada: o modelo local roda nela.", null, false)
                : new("gpu", "Placa de vídeo", "chart", "warn", "Mac com Intel: o modelo local roda no processador, mais devagar.", null, false);
        var smi = Platform.Which("nvidia-smi");
        var gpu = smi is null ? "" : Launcher.Capture(smi, ["--query-gpu=name,memory.total", "--format=csv,noheader"]).Trim().Split('\n')[0];
        return gpu.Length > 0 && !gpu.Contains("failed", StringComparison.OrdinalIgnoreCase)
            ? new("gpu", "Placa de vídeo", "chart", "ok", $"{gpu}: o modelo local roda na GPU.", null, false)
            : new("gpu", "Placa de vídeo", "chart", "warn", "Sem GPU NVIDIA: o modelo local roda no processador, mais devagar (ou use a sua assinatura do Claude ou do ChatGPT).", null, false);
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
            var installed = Platform.Which("ollama") is not null || File.Exists(Path.Combine(Platform.Home, ".local", "opt", "ollama", "bin", "ollama"))
                || Directory.Exists("/Applications/Ollama.app") || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"));
            return installed
                ? new("ollama", "Modelo local (Ollama)", "model", "warn", "Instalado, mas parado. O app liga sozinho quando precisar.", null, false)
                : new("ollama", "Modelo local (Ollama)", "model", "warn", "Sem modelo local: o app usa a sua assinatura (Claude ou ChatGPT). O Ollama se instala depois, num clique, em Provedores.", null, false);
        }
    }

    private static string BrowserName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name switch { "msedge" => "Microsoft Edge", "chrome" => "Google Chrome", "brave" => "Brave", _ => name };
    }

    /// <summary>O comando de instalação no gerenciador de pacotes desta distribuição (apt, dnf, pacman ou zypper).</summary>
    internal static string Install(string apt, string dnf, string pacman, string zypper) =>
        Platform.Which("apt-get") is not null ? $"sudo apt-get install -y {apt}"
        : Platform.Which("dnf") is not null ? $"sudo dnf install -y {dnf}"
        : Platform.Which("pacman") is not null ? $"sudo pacman -S --needed {pacman}"
        : Platform.Which("zypper") is not null ? $"sudo zypper install -y {zypper}"
        : $"instale pelo gerenciador de pacotes: {apt}";
}

/// <summary>Os passos da instalação, um de cada vez, com o que cada um está fazendo para a tela mostrar ao vivo.</summary>
internal sealed class Installer
{
    private readonly Lock _gate = new();
    private readonly List<SetupStep> _steps =
    [
        new("files", "Arquivos", "receipt", "waiting", "Copiar o app para a sua pasta."),
        new("data", "Seus dados", "identity", "waiting", "Trazer perfil, campanha e configurações."),
        new("database", "Banco", "inbox", "waiting", "Ligar o Postgres."),
        new("scraper", "Coletor", "radar", "waiting", "Preparar o Scrapy e as bibliotecas de extração."),
        new("model", "Modelo local", "model", "waiting", "Conferir o Ollama."),
        new("shortcut", "Atalho", "router", "waiting", "Pôr o app no menu, com ícone."),
        new("service", "Serviço", "gate", "waiting", "Ligar os serviços em segundo plano."),
    ];
    private readonly List<string> _log = [];
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(15) };
    public bool Running { get; private set; }
    public bool Done { get; private set; }
    private string? _error;

    static Installer() => Http.DefaultRequestHeaders.UserAgent.ParseAdd($"JobOrchestrator/{Launcher.Version}");

    public object Snapshot()
    {
        lock (_gate) return new { running = Running, done = Done, error = _error, steps = _steps.ToList(), log = _log.TakeLast(14).ToList() };
    }

    public async Task RunAsync(InstallInput input)
    {
        lock (_gate) { Running = true; Done = false; _error = null; for (var i = 0; i < _steps.Count; i++) _steps[i] = _steps[i] with { State = "waiting" }; }
        try
        {
            await StepAsync("files", CopyFilesAsync);
            await StepAsync("data", () => Task.FromResult(input.Migrate ? Migrate() : "Nada para trazer."));
            await StepAsync("database", DatabaseAsync);
            await StepAsync("scraper", ScraperAsync);
            await StepAsync("model", Model);
            await StepAsync("shortcut", () => Task.FromResult(input.Menu ? Desktop.CreateShortcut(Setup.Target) : "Sem atalho (dá para criar depois rodando o instalador de novo)."));
            await StepAsync("service", () => ServiceAsync(input.Autostart));
            lock (_gate) Done = true;
        }
        catch (InvalidOperationException ex) { lock (_gate) _error = ex.Message; }
        finally { lock (_gate) Running = false; }
    }

    private async Task StepAsync(string id, Func<Task<string>> work)
    {
        Set(id, "working", null);
        try { Set(id, "done", await work()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or TaskCanceledException or Npgsql.NpgsqlException)
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

    /// <summary>
    /// Copia o app para a pasta do usuário. No macOS vai o pacote inteiro (ditto guarda a assinatura e os atributos); no Windows e no
    /// Linux, a pasta. Uma versão anterior no ar segura os arquivos: para antes de copiar por cima.
    /// </summary>
    private async Task<string> CopyFilesAsync()
    {
        var source = Desktop.Source;
        var target = Setup.Target;
        if (Launcher.FromTarget) return "O app já roda da pasta instalada.";
        if (await Launcher.HealthyAsync() || Desktop.AutostartInstalled)
        {
            Log("parando a versão anterior");
            Desktop.StopService();
            if (await Launcher.HealthyAsync()) await Launcher.StopAsync();
        }
        if (OperatingSystem.IsMacOS())
        {
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            Log($"ditto {Path.GetFileName(source)} ~/Applications");
            if (await StreamAsync("/usr/bin/ditto", [source, target], TimeSpan.FromMinutes(5)) != 0) throw new InvalidOperationException("O ditto não copiou o pacote do app.");
            return "Pacote em ~/Applications/Job Orchestrator.app.";
        }
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            if (rel.StartsWith("..", StringComparison.Ordinal)) continue;
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            for (var attempt = 0; ; attempt++)
            {
                // No Windows, o executável antigo pode demorar uns segundos para soltar o arquivo depois de parar.
                try { File.Copy(file, dest, overwrite: true); break; }
                catch (IOException) when (attempt < 10 && OperatingSystem.IsWindows()) { await Task.Delay(1000); }
            }
            if (++count % 200 == 0) Log($"{count} arquivos copiados");
        }
        var exe = Desktop.Exe(target);
        if (File.Exists(exe) && !OperatingSystem.IsWindows())
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Log($"{count} arquivos em {target}");
        return $"{count} arquivos em {target.Replace(Platform.Home, "~", StringComparison.Ordinal)}.";
    }

    /// <summary>A pasta .dev do modo de desenvolvimento que gerou este pacote (o empacotador anota onde fica).</summary>
    public static string? DevData()
    {
        var note = Path.Combine(Launcher.Home, "dev-data.txt");
        var dev = File.Exists(note) ? File.ReadAllText(note).Trim() : "";
        return dev.Length > 0 && Directory.Exists(dev) ? dev : null;
    }

    /// <summary>Configurações e arquivos do modo de desenvolvimento vão para as pastas do app, sem apagar nada que já esteja lá. O banco é o mesmo.</summary>
    private static readonly HashSet<string> Disposable = ["scraper-cache", "scraper-venv", "llm-sandbox", "run", "window", "httpcache", "logs", "postgres", "tools", "pycache", "downloads"];

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

    /// <summary>O volume do compose com os dados de uma instalação anterior pelo Docker (o modo de desenvolvimento usa o mesmo).</summary>
    internal static bool DockerVolume() =>
        Platform.Which("docker") is { } docker && Launcher.Run(docker, ["volume", "inspect", "job-orchestrator_jo-pg"], TimeSpan.FromSeconds(15)) == 0;

    /// <summary>
    /// O banco: o que já responde na 5462; senão o Postgres próprio (instalado, ou baixado agora, conferido e criado); o Docker só quando
    /// ele já guarda os seus dados de antes. Assim nenhum sistema precisa de Docker.
    /// </summary>
    private async Task<string> DatabaseAsync()
    {
        if (await Launcher.PortOpenAsync(5462)) return "Postgres já no ar (127.0.0.1:5462).";
        var connection = Connection();
        if (PortablePostgres.Installed)
        {
            if (!await PortablePostgres.StartAsync(Log, CancellationToken.None)) throw new InvalidOperationException($"O Postgres próprio não subiu. Veja {PortablePostgres.Log}.");
            return "Postgres próprio no ar em 127.0.0.1:5462.";
        }
        if (DockerVolume() && Launcher.Compose() is { } compose)
        {
            Log("docker compose up -d --wait postgres (os seus dados de antes)");
            var exit = await StreamAsync("docker", ["compose", "-p", "job-orchestrator", "-f", compose, "up", "-d", "--wait", "postgres"], TimeSpan.FromMinutes(8));
            if (exit == 0 && await Launcher.PortOpenAsync(5462)) return "Postgres do Docker no ar, com os seus dados.";
            Log("o Docker não subiu o banco; seguindo com o Postgres próprio");
        }
        await PortablePostgres.InstallAsync(Http, Log, CancellationToken.None);
        await PortablePostgres.InitAsync(connection, Log, CancellationToken.None);
        return $"PostgreSQL {PortablePostgres.Version[..2]} próprio no ar em 127.0.0.1:5462, com os dados em {PortablePostgres.Cluster.Replace(Platform.Home, "~", StringComparison.Ordinal)}.";
    }

    /// <summary>A conexão que o app usa (appsettings mais settings.json), para o banco nascer com o mesmo usuário, senha e nome.</summary>
    private static string Connection()
    {
        var config = new ConfigurationBuilder().SetBasePath(AppPaths.Resources).AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile(Path.Combine(AppPaths.Config, "settings.json"), optional: true).AddEnvironmentVariables().Build();
        return config.GetConnectionString("Default") ?? "Host=127.0.0.1;Port=5462;Database=job_orchestrator;Username=jo;Password=jo-local";
    }

    /// <summary>
    /// O coletor: o projeto copiado para a pasta de dados (gravável em qualquer sistema) e o ambiente do Python com o Scrapy. O uv traz
    /// um Python 3.12 só do app, igual em qualquer máquina; sem uv, vale o Python do sistema (3.11 ou mais novo).
    /// </summary>
    private async Task<string> ScraperAsync()
    {
        var project = Path.Combine(AppPaths.Data, "scraper");
        var bundled = Path.Combine(AppPaths.Resources, "scraper");
        if (Directory.Exists(bundled) && !string.Equals(Path.GetFullPath(bundled), Path.GetFullPath(project), StringComparison.Ordinal))
        {
            if (Directory.Exists(project)) Directory.Delete(project, recursive: true);
            foreach (var file in Directory.EnumerateFiles(bundled, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(bundled, file);
                if (rel.Contains("__pycache__", StringComparison.Ordinal) || rel.Contains(".egg-info", StringComparison.Ordinal)) continue;
                var dest = Path.Combine(project, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, overwrite: true);
            }
        }
        if (!File.Exists(Path.Combine(project, "pyproject.toml"))) return "Sem o projeto do coletor neste pacote: a varredura de sites fica desligada.";
        var venv = Path.Combine(AppPaths.Data, "scraper-venv");
        var python = OperatingSystem.IsWindows() ? Path.Combine(venv, "Scripts", "python.exe") : Path.Combine(venv, "bin", "python");
        string? uv = null;
        try { uv = await Uv.EnsureAsync(Http, Log, CancellationToken.None); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException or TaskCanceledException) { Log($"uv indisponível ({ex.Message}); tentando o Python do sistema"); }
        if (uv is not null)
        {
            if (!File.Exists(python))
            {
                Log("uv venv --python 3.12 (o Python vem junto, só para o coletor)");
                if (await StreamAsync(uv, ["venv", venv, "--python", "3.12"], TimeSpan.FromMinutes(10), Uv.Environment) != 0) throw new InvalidOperationException("O uv não criou o ambiente do Python.");
            }
            Log("uv pip install (Scrapy, extruct, trafilatura, dateparser, price-parser)");
            if (await StreamAsync(uv, ["pip", "install", "--python", python, "-e", project], TimeSpan.FromMinutes(15), Uv.Environment) != 0)
                throw new InvalidOperationException("O uv não instalou o coletor. Confira a internet e tente de novo.");
        }
        else
        {
            var system = Platform.Which(OperatingSystem.IsWindows() ? "python" : "python3");
            if (system is null) return "Sem Python e sem uv: a varredura de sites fica desligada; o resto funciona.";
            if (!File.Exists(python))
            {
                Log("python -m venv scraper-venv");
                if (await StreamAsync(system, ["-m", "venv", venv], TimeSpan.FromMinutes(3)) != 0) throw new InvalidOperationException("Não foi possível criar o ambiente do Python.");
            }
            Log("pip install (Scrapy, extruct, trafilatura, dateparser, price-parser)");
            if (await StreamAsync(python, ["-m", "pip", "install", "-q", "--disable-pip-version-check", "-e", project], TimeSpan.FromMinutes(15)) != 0)
                throw new InvalidOperationException("O pip não instalou o coletor. Confira a internet e tente de novo.");
        }
        return File.Exists(Scrapy(venv)) ? "Scrapy e as bibliotecas de extração prontos." : throw new InvalidOperationException("O Scrapy não ficou no ambiente do coletor.");
    }

    /// <summary>O executável do Scrapy dentro do ambiente: bin/ no Linux e no macOS, Scripts\ no Windows.</summary>
    internal static string Scrapy(string venv) => OperatingSystem.IsWindows() ? Path.Combine(venv, "Scripts", "scrapy.exe") : Path.Combine(venv, "bin", "scrapy");

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

    /// <summary>
    /// Os serviços: com "abrir junto com o computador", ligam no login (systemd, launchd ou a inicialização do Windows); sem, sobem agora
    /// e quando você abrir o app. O PATH da sua sessão fica guardado para as CLIs do Claude e do Codex.
    /// </summary>
    private async Task<string> ServiceAsync(bool autostart)
    {
        File.WriteAllText(Path.Combine(AppPaths.Config, "env"), $"PATH={Environment.GetEnvironmentVariable("PATH")}\n");
        var exe = Desktop.Exe(Setup.Target);
        var env = Launcher.ServiceEnvironment();
        var how = autostart ? Desktop.InstallAutostart(exe, env) : null;
        if (!autostart && Desktop.AutostartInstalled) Desktop.RemoveAutostart();
        if (how is not null) Log(how);
        // Um app no ar fora da pasta instalada (o modo de desenvolvimento ou um pacote antigo) sai antes: dois Workers no mesmo banco não.
        var taken = await TakeoverAsync(exe);
        if (taken > 0) Log($"{taken} {(taken == 1 ? "processo antigo encerrado" : "processos antigos encerrados")} para o instalado assumir");
        Desktop.Start(exe, env);
        for (var i = 0; i < 120 && !await Launcher.HealthyAsync(); i++) await Task.Delay(1000);
        if (!await Launcher.HealthyAsync()) throw new InvalidOperationException("Os serviços não responderam em 2 minutos. Rode job-orchestrator --serve no terminal para ver o motivo.");
        await File.WriteAllTextAsync(Launcher.Marker, System.Text.Json.JsonSerializer.Serialize(new { version = Launcher.Version, target = Setup.Target, os = Platform.Os, at = DateTime.UtcNow }));
        return how ?? "No ar; sobe de novo sempre que você abrir o app.";
    }

    /// <summary>Encerra a API e o Worker do modo de desenvolvimento e qualquer job-orchestrator --serve de outra pasta; espera a porta 7410 ficar livre.</summary>
    private static async Task<int> TakeoverAsync(string installed)
    {
        var count = 0;
        foreach (var (pid, cmd) in Desktop.Processes())
        {
            if (pid == Environment.ProcessId) continue;
            var dev = cmd.Contains("JobOrchestrator.Api.dll", StringComparison.Ordinal) || cmd.Contains("JobOrchestrator.Worker.dll", StringComparison.Ordinal);
            var other = cmd.Contains("job-orchestrator", StringComparison.OrdinalIgnoreCase) && cmd.Contains("--serve", StringComparison.Ordinal)
                && !cmd.Contains(installed, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            if (!dev && !other) continue;
            // Linux e macOS: SIGTERM, e a API e o Worker encerram com calma (o Worker termina a etapa e grava a intenção; nada é reenviado).
            if (!OperatingSystem.IsWindows()) { if (Launcher.Run("/bin/kill", ["-TERM", pid.ToString(System.Globalization.CultureInfo.InvariantCulture)]) == 0) count++; continue; }
            try { using var p = Process.GetProcessById(pid); p.Kill(entireProcessTree: true); count++; } catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }
        }
        for (var i = 0; i < 40 && count > 0 && await Launcher.HealthyAsync(); i++) await Task.Delay(500);
        return count;
    }

    private async Task<int> StreamAsync(string file, string[] args, TimeSpan limit, IReadOnlyDictionary<string, string>? env = null)
    {
        var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) info.Environment[k] = v;
        using var p = new Process { StartInfo = info };
        p.OutputDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) Log(l.Length > 160 ? l[..160] : l); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } l) Log(l.Length > 160 ? l[..160] : l); };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { Log($"{Path.GetFileName(file)}: {ex.Message}"); return -1; }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(limit);
        try { await p.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { p.Kill(true); return -1; }
        return p.ExitCode;
    }
}
