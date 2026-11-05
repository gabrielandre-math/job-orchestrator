using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using JobOrchestrator.BuildingBlocks;

namespace JobOrchestrator.App;

/// <summary>
/// O que muda de um sistema para outro no app instalado: onde ele mora, como liga sozinho e em segundo plano (systemd no Linux,
/// launchd no macOS, a chave Run do registro no Windows), o atalho (menu do Linux, o próprio pacote .app no macOS, menu Iniciar no
/// Windows), a janela e a lista de processos para assumir o lugar de uma versão antiga. Nada pede senha de administrador.
/// </summary>
internal static class Desktop
{
    public const string Label = "app.joborchestrator.server";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\JobOrchestrator";
    private static string Home => Platform.Home;

    /// <summary>Onde o app fica instalado: ~/.local/opt no Linux, ~/Applications/Job Orchestrator.app no macOS, %LOCALAPPDATA%\Programs no Windows.</summary>
    public static string Target =>
        // Pacote arrastado para /Applications (o jeito de costume no Mac) já é a instalação; aberto do .dmg ou de Downloads, vai para ~/Applications.
        OperatingSystem.IsMacOS() ? (Bundle is { } b && b.StartsWith("/Applications/", StringComparison.Ordinal) ? b : Path.Combine(Home, "Applications", "Job Orchestrator.app"))
        : OperatingSystem.IsWindows() ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "JobOrchestrator")
        : Path.Combine(Home, ".local", "opt", "job-orchestrator");

    /// <summary>O executável dentro de uma instalação (no macOS, dentro do pacote).</summary>
    public static string Exe(string root) =>
        OperatingSystem.IsMacOS() ? Path.Combine(root, "Contents", "MacOS", "job-orchestrator")
        : Path.Combine(root, OperatingSystem.IsWindows() ? "job-orchestrator.exe" : "job-orchestrator");

    /// <summary>O pacote .app de onde este processo roda (macOS), ou null fora de um pacote.</summary>
    public static string? Bundle
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return null;
            var app = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
            return app.EndsWith(".app", StringComparison.OrdinalIgnoreCase) ? app : null;
        }
    }

    /// <summary>O que o instalador copia: o pacote inteiro no macOS, a pasta do executável nos outros.</summary>
    public static string Source => Bundle ?? Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);

    /// <summary>Quando o HOME do processo não é o do usuário de verdade (teste com HOME isolado), nada é registrado no systemd nem no launchd.</summary>
    public static bool Sandboxed => Environment.GetEnvironmentVariable("JO_SANDBOX") is { Length: > 0 };

    private static string UserUnits => Path.Combine(Home, ".config", "systemd", "user");
    private static string UnitFile => Path.Combine(UserUnits, $"{Launcher.Unit}.service");
    private static string Agent => Path.Combine(Home, "Library", "LaunchAgents", $"{Label}.plist");
    private static string Uid => Platform.Capture("/usr/bin/id", "-u").Trim();
    private static bool Systemd => OperatingSystem.IsLinux() && Platform.Which("systemctl") is not null && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } && !Sandboxed;

    // ---- ligar sozinho -------------------------------------------------------------------------------------------------------

    public static bool AutostartInstalled =>
        OperatingSystem.IsMacOS() ? File.Exists(Agent)
        : OperatingSystem.IsWindows() ? WindowsAutostart()
        : File.Exists(UnitFile);

    /// <summary>Liga o app junto com o login do usuário. Devolve o que fez, para a tela.</summary>
    public static string InstallAutostart(string exe, IReadOnlyDictionary<string, string> env)
    {
        if (OperatingSystem.IsMacOS())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Agent)!);
            File.WriteAllText(Agent, Plist(exe, env, runAtLoad: true));
            return "No launchd do seu usuário: liga quando você entrar no Mac.";
        }
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue("JobOrchestrator", $"\"{exe}\" --serve --background");
            return "Na inicialização do Windows: liga quando você entrar.";
        }
        Directory.CreateDirectory(UserUnits);
        File.WriteAllText(UnitFile, $"""
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
        if (Systemd)
        {
            Launcher.Run("systemctl", ["--user", "daemon-reload"]);
            Launcher.Run("systemctl", ["--user", "enable", $"{Launcher.Unit}.service"]);
            return "Serviço do seu usuário no systemd: liga quando você entrar.";
        }
        return Sandboxed ? "Unit gravada (teste com HOME isolado: o systemd não foi chamado)." : "Unit gravada; sem o systemd do usuário aqui, o app sobe quando você abrir.";
    }

    public static void RemoveAutostart()
    {
        if (OperatingSystem.IsMacOS())
        {
            if (!Sandboxed) Launcher.Run("/bin/launchctl", ["bootout", $"gui/{Uid}/{Label}"]);
            if (File.Exists(Agent)) File.Delete(Agent);
            return;
        }
        if (OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue("JobOrchestrator", throwOnMissingValue: false);
            return;
        }
        if (!File.Exists(UnitFile)) return;
        if (Systemd) Launcher.Run("systemctl", ["--user", "disable", "--now", $"{Launcher.Unit}.service"]);
        File.Delete(UnitFile);
        if (Systemd) Launcher.Run("systemctl", ["--user", "daemon-reload"]);
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsAutostart()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("JobOrchestrator") is string;
    }

    /// <summary>
    /// O job do launchd: o servidor em segundo plano, com o log na pasta de dados, religado se cair. Com runAtLoad e em
    /// ~/Library/LaunchAgents, liga no login; numa pasta de execução, só nesta sessão (como a unidade passageira do systemd).
    /// </summary>
    internal static string Plist(string exe, IReadOnlyDictionary<string, string> env, bool runAtLoad)
    {
        static string X(string s) => SecurityElement.Escape(s) ?? "";
        var log = Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.log");
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>Label</key><string>{Label}</string>
              <key>ProgramArguments</key><array><string>{X(exe)}</string><string>--serve</string></array>
              <key>RunAtLoad</key><{(runAtLoad ? "true" : "false")}/>
              <key>KeepAlive</key><dict><key>SuccessfulExit</key><false/></dict>
              <key>ThrottleInterval</key><integer>10</integer>
              <key>Nice</key><integer>5</integer>
              <key>ProcessType</key><string>Standard</string>
              <key>AssociatedBundleIdentifiers</key><string>app.joborchestrator</string>
              <key>StandardOutPath</key><string>{X(log)}</string>
              <key>StandardErrorPath</key><string>{X(log)}</string>
              <key>EnvironmentVariables</key><dict>{string.Concat(env.Select(e => $"<key>{X(e.Key)}</key><string>{X(e.Value)}</string>"))}</dict>
            </dict>
            </plist>
            """;
    }

    // ---- subir, parar, conferir ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sobe o servidor em segundo plano agora. Linux: o serviço do usuário quando instalado, senão uma unidade passageira do systemd,
    /// senão solto. macOS: o job do launchd (o instalado, ou um só desta sessão). Windows: processo sem janela, com o log em arquivo.
    /// </summary>
    public static void Start(string exe, IReadOnlyDictionary<string, string> env)
    {
        var log = Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.log");
        if (OperatingSystem.IsMacOS() && !Sandboxed)
        {
            var domain = $"gui/{Uid}";
            var plist = File.Exists(Agent) ? Agent : Path.Combine(AppPaths.In(AppPaths.Data, "run"), $"{Label}.plist");
            if (plist != Agent) File.WriteAllText(plist, Plist(exe, env, runAtLoad: true));
            // Já carregado (outra sessão do app): só reinicia; senão carrega, e o RunAtLoad sobe o servidor.
            if (Launcher.Run("/bin/launchctl", ["print", $"{domain}/{Label}"]) == 0 && Launcher.Run("/bin/launchctl", ["kickstart", $"{domain}/{Label}"]) == 0) return;
            if (Launcher.Run("/bin/launchctl", ["bootstrap", domain, plist]) == 0) return;
        }
        if (OperatingSystem.IsWindows())
        {
            Platform.Detach(exe, ["--serve", "--background", "--log", log], env);
            return;
        }
        if (OperatingSystem.IsLinux() && Systemd)
        {
            if (File.Exists(UnitFile) && Launcher.Run("systemctl", ["--user", "start", $"{Launcher.Unit}.service"]) == 0) return;
            if (Platform.Which("systemd-run") is not null)
            {
                Launcher.Run("systemctl", ["--user", "reset-failed", $"{Launcher.Unit}.service"]);
                var a = new List<string> { "--user", $"--unit={Launcher.Unit}", "--collect", "--quiet", "--slice=jobs.slice", "-p", "MemoryMax=8G", "-p", "CPUWeight=50" };
                a.AddRange(env.Select(e => $"--setenv={e.Key}={e.Value}"));
                a.AddRange([exe, "--serve"]);
                if (Launcher.Run("systemd-run", [.. a]) == 0) return;
            }
        }
        Platform.Detach(exe, ["--serve"], env, log);
    }

    /// <summary>Para o serviço do sistema (o processo em si o Launcher encerra depois, se ainda responder).</summary>
    public static void StopService()
    {
        if (Sandboxed) return;
        if (OperatingSystem.IsMacOS()) Launcher.Run("/bin/launchctl", ["bootout", $"gui/{Uid}/{Label}"]);
        else if (Systemd) Launcher.Run("systemctl", ["--user", "stop", $"{Launcher.Unit}.service"]);
    }

    public static string ServiceState()
    {
        if (OperatingSystem.IsMacOS())
        {
            var print = Platform.Capture("/bin/launchctl", "print", $"gui/{Uid}/{Label}");
            var state = print.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("state = ", StringComparison.Ordinal))?[8..] ?? "não carregado";
            return $"launchd: {state}{(File.Exists(Agent) ? ", liga no login" : "")}";
        }
        if (OperatingSystem.IsWindows()) return AutostartInstalled ? "liga com o Windows" : "sobe quando você abre o app";
        return Systemd ? $"systemd: {Platform.Capture("systemctl", "--user", "is-active", $"{Launcher.Unit}.service").Trim()}" : "sem systemd do usuário";
    }

    // ---- atalho ---------------------------------------------------------------------------------------------------------------

    /// <summary>O atalho de cada sistema. Devolve o que fez, para a tela.</summary>
    public static string CreateShortcut(string target)
    {
        var exe = Exe(target);
        if (OperatingSystem.IsMacOS())
            return "Em Aplicativos (~/Applications), com ícone: abre pelo Launchpad, pelo Spotlight ou pelo Dock.";
        if (OperatingSystem.IsWindows())
        {
            var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Job Orchestrator.lnk");
            var desk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Job Orchestrator.lnk");
            foreach (var lnk in new[] { menu, desk }) Link(lnk, exe, target);
            RegisterUninstall(target, exe);
            return "No menu Iniciar e na área de trabalho, com ícone; desinstala por Configurações > Aplicativos.";
        }
        var icons = Path.Combine(Home, ".local", "share", "icons", "hicolor", "scalable", "apps");
        Directory.CreateDirectory(icons);
        var icon = Path.Combine(target, "job-orchestrator.svg");
        if (File.Exists(icon)) File.Copy(icon, Path.Combine(icons, "job-orchestrator.svg"), overwrite: true);
        var apps = Path.Combine(Home, ".local", "share", "applications");
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
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var link = Path.Combine(bin, "job-orchestrator");
        if (File.Exists(link) || Directory.Exists(link)) File.Delete(link);
        File.CreateSymbolicLink(link, exe);
        if (Platform.Which("update-desktop-database") is not null) Launcher.Run("update-desktop-database", [apps]);
        return "No menu de aplicativos, com ícone; no terminal, job-orchestrator.";
    }

    public static void RemoveShortcut()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var lnk in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Job Orchestrator.lnk"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Job Orchestrator.lnk") })
                if (File.Exists(lnk)) File.Delete(lnk);
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            return;
        }
        if (OperatingSystem.IsMacOS()) return;
        foreach (var f in new[] { Path.Combine(Home, ".local", "share", "applications", "job-orchestrator.desktop"), Path.Combine(Home, ".local", "share", "icons", "hicolor", "scalable", "apps", "job-orchestrator.svg"), Path.Combine(Home, ".local", "bin", "job-orchestrator") })
            if (File.Exists(f) || Directory.Exists(f)) File.Delete(f);
    }

    /// <summary>Atalho .lnk pelo WScript.Shell do próprio Windows, chamado pelo PowerShell; os caminhos vão por variável de ambiente.</summary>
    [SupportedOSPlatform("windows")]
    private static void Link(string lnk, string exe, string dir)
    {
        const string script = "$s = (New-Object -ComObject WScript.Shell).CreateShortcut($env:JO_LNK); $s.TargetPath = $env:JO_EXE; $s.WorkingDirectory = $env:JO_DIR;"
            + " $s.IconLocation = \"$env:JO_EXE,0\"; $s.Description = 'Busca de vagas e candidaturas, na sua máquina'; $s.Save()";
        var info = new System.Diagnostics.ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script]) { UseShellExecute = false, CreateNoWindow = true };
        info.Environment["JO_LNK"] = lnk;
        info.Environment["JO_EXE"] = exe;
        info.Environment["JO_DIR"] = dir;
        using var p = System.Diagnostics.Process.Start(info);
        p?.WaitForExit(30_000);
    }

    /// <summary>A entrada em Configurações > Aplicativos (por usuário, sem administrador): desinstalar chama job-orchestrator --uninstall.</summary>
    [SupportedOSPlatform("windows")]
    private static void RegisterUninstall(string dir, string exe)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "Job Orchestrator");
        key.SetValue("DisplayVersion", Launcher.Version);
        key.SetValue("Publisher", "Job Orchestrator");
        key.SetValue("DisplayIcon", $"\"{exe}\",0");
        key.SetValue("InstallLocation", dir);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, Microsoft.Win32.RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, Microsoft.Win32.RegistryValueKind.DWord);
        var size = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024;
        key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, size), Microsoft.Win32.RegistryValueKind.DWord);
    }

    // ---- janela ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A janela do app: Chrome, Edge, Chromium ou Brave em modo app, com perfil próprio (não mistura com o seu navegador) e a extensão
    /// do copiloto. Sem nenhum deles, abre no navegador padrão do sistema.
    /// </summary>
    public static void Window(string url)
    {
        if (Environment.GetEnvironmentVariable("JO_NO_WINDOW") is { Length: > 0 }) { Console.WriteLine($"Janela: {url}"); return; }
        var profile = AppPaths.In(AppPaths.Data, "window");
        if (Platform.Browser() is { } browser)
        {
            var args = new List<string> { $"--app={url}", $"--user-data-dir={profile}", "--no-first-run", "--no-default-browser-check", "--window-size=1440,920" };
            if (OperatingSystem.IsLinux()) args.AddRange(["--class=JobOrchestrator", "--name=JobOrchestrator"]);
            var extension = Path.Combine(AppPaths.Resources, "extension");
            if (File.Exists(Path.Combine(extension, "manifest.json"))) args.AddRange([$"--load-extension={extension}", "--disable-features=DisableLoadExtensionCommandLineSwitch"]);
            if (Platform.Detach(browser, args)) return;
        }
        if (OperatingSystem.IsMacOS()) Platform.Detach("/usr/bin/open", [url]);
        else if (OperatingSystem.IsWindows())
        {
            try { using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception ex) { Console.Error.WriteLine($"Não abriu o navegador: {ex.Message}"); }
        }
        else Platform.Detach("xdg-open", [url]);
    }

    // ---- processos ------------------------------------------------------------------------------------------------------------

    /// <summary>Os processos do usuário com a linha de comando: /proc no Linux, ps no macOS, o CIM pelo PowerShell no Windows.</summary>
    public static IEnumerable<(int Pid, string Command)> Processes()
    {
        if (OperatingSystem.IsLinux())
        {
            foreach (var dir in Directory.Exists("/proc") ? Directory.EnumerateDirectories("/proc") : [])
            {
                if (!int.TryParse(Path.GetFileName(dir), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) continue;
                string cmd;
                try { cmd = File.ReadAllText(Path.Combine(dir, "cmdline")).Replace('\0', ' ').Trim(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                yield return (pid, cmd);
            }
            yield break;
        }
        var output = OperatingSystem.IsWindows()
            ? Platform.Capture("powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name='job-orchestrator.exe' or Name='dotnet.exe'\" | ForEach-Object { \"$($_.ProcessId) $($_.CommandLine)\" }")
            : Platform.Capture("/bin/ps", "-axww", "-o", "pid=,command=");
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            var space = t.IndexOf(' ');
            if (space > 0 && int.TryParse(t[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)) yield return (pid, t[(space + 1)..].Trim());
        }
    }
}
