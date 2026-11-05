using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>
/// O que muda entre Linux, macOS e Windows num lugar só: o nome do sistema, como achar um programa (PATH e, no Windows, as extensões
/// do PATHEXT), o ambiente mínimo de um processo filho, o navegador da família Chromium instalado e o aviso na área de notificações.
/// </summary>
public static class Platform
{
    public static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string Os => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
    public static string Arch => RuntimeInformation.OSArchitecture switch { Architecture.Arm64 => "arm64", Architecture.X64 => "x64", var a => a.ToString().ToLowerInvariant() };

    /// <summary>O sistema por extenso, para a tela: "macOS 26.6.2", "Windows 11 (build 26100)", "Ubuntu 24.04.1 LTS".</summary>
    public static string Describe()
    {
        if (OperatingSystem.IsMacOS()) return $"macOS {Capture("/usr/bin/sw_vers", "-productVersion").Trim()}".TrimEnd();
        if (OperatingSystem.IsWindows())
        {
            var build = Environment.OSVersion.Version.Build;
            return $"Windows {(build >= 22000 ? "11" : "10")} (build {build})";
        }
        foreach (var line in File.Exists("/etc/os-release") ? File.ReadLines("/etc/os-release") : [])
            if (line.StartsWith("PRETTY_NAME=", StringComparison.Ordinal)) return line[12..].Trim('"');
        return "Linux";
    }

    /// <summary>Caminho de um programa: o PATH do processo, as pastas extras e as de costume do sistema. No Windows, com .exe, .cmd e o resto do PATHEXT.</summary>
    public static string? Which(string name, params string[] extra)
    {
        if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar)) return File.Exists(name) ? name : null;
        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).Concat(extra).Concat(UsualDirs());
        string[] exts = OperatingSystem.IsWindows() && !Path.HasExtension(name)
            ? (Environment.GetEnvironmentVariable("PATHEXT") is { Length: > 0 } pe ? pe : ".COM;.EXE;.BAT;.CMD").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [""];
        foreach (var dir in dirs)
            foreach (var ext in exts)
            {
                var path = Path.Combine(dir.Trim('"'), name + ext.ToLowerInvariant());
                if (File.Exists(path)) return path;
            }
        return null;
    }

    private static string[] UsualDirs() =>
        OperatingSystem.IsWindows() ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System))]
        : OperatingSystem.IsMacOS() ? ["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"]
        : ["/usr/local/bin", "/usr/bin", "/bin", "/usr/sbin", "/sbin"];

    /// <summary>
    /// As variáveis que um processo filho precisa para funcionar neste sistema, sem chave nem token. No Windows entram as pastas do
    /// perfil e do sistema (o Python e o Node não sobem sem SystemRoot e APPDATA).
    /// </summary>
    public static IReadOnlyList<string> BaseEnvironment { get; } = OperatingSystem.IsWindows()
        ? ["PATH", "PATHEXT", "SystemRoot", "SystemDrive", "windir", "ComSpec", "USERPROFILE", "USERNAME", "USERDOMAIN", "HOMEDRIVE", "HOMEPATH", "APPDATA", "LOCALAPPDATA",
           "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "ProgramW6432", "CommonProgramFiles", "TEMP", "TMP", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS", "LANG"]
        : ["PATH", "HOME", "USER", "LOGNAME", "SHELL", "LANG", "LC_ALL", "TERM", "TMPDIR"];

    /// <summary>Os navegadores da família Chromium que o app sabe usar, na ordem de preferência de cada sistema.</summary>
    public static IEnumerable<string> BrowserCandidates()
    {
        if (OperatingSystem.IsMacOS())
        {
            foreach (var root in new[] { "/Applications", Path.Combine(Home, "Applications") })
                foreach (var app in new[] { "Google Chrome", "Chromium", "Microsoft Edge", "Brave Browser", "Vivaldi" })
                    yield return Path.Combine(root, $"{app}.app", "Contents", "MacOS", app);
            yield break;
        }
        if (OperatingSystem.IsWindows())
        {
            var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            foreach (var rel in new[] { @"Google\Chrome\Application\chrome.exe", @"Microsoft\Edge\Application\msedge.exe", @"BraveSoftware\Brave-Browser\Application\brave.exe", @"Chromium\Application\chrome.exe", @"Vivaldi\Application\vivaldi.exe" })
                foreach (var root in roots.Where(r => r.Length > 0))
                    yield return Path.Combine(root, rel);
            yield break;
        }
        foreach (var name in new[] { "chromium", "chromium-browser", "google-chrome-stable", "google-chrome", "brave", "brave-browser", "microsoft-edge-stable", "microsoft-edge", "vivaldi-stable" })
            if (Which(name) is { } path) yield return path;
        yield return "/snap/bin/chromium";
    }

    /// <summary>O navegador para a janela do app e para as páginas com JavaScript: o configurado, se existir; senão o primeiro instalado.</summary>
    public static string? Browser(string? configured = null) =>
        configured is { Length: > 0 } && File.Exists(configured) ? configured : BrowserCandidates().FirstOrDefault(File.Exists);

    /// <summary>Aviso na área de notificações do sistema. Sem área de notificações (servidor, teste), não faz nada.</summary>
    public static void Notify(string title, string text)
    {
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsMacOS())
            {
                static string Quote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
                info = new ProcessStartInfo("/usr/bin/osascript", ["-e", $"display notification {Quote(text)} with title {Quote(title)}"]);
            }
            else if (OperatingSystem.IsWindows())
            {
                // Toast do Windows pelo PowerShell que vem no sistema; título e texto vão por variável de ambiente, sem montar código com eles.
                const string script = "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;"
                    + "$t = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent([Windows.UI.Notifications.ToastTemplateType]::ToastText02);"
                    + "$x = $t.GetElementsByTagName('text'); $x.Item(0).AppendChild($t.CreateTextNode($env:JO_TITLE)) > $null; $x.Item(1).AppendChild($t.CreateTextNode($env:JO_TEXT)) > $null;"
                    + "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\\WindowsPowerShell\\v1.0\\powershell.exe').Show([Windows.UI.Notifications.ToastNotification]::new($t))";
                info = new ProcessStartInfo("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-Command", script]) { CreateNoWindow = true };
                info.Environment["JO_TITLE"] = title;
                info.Environment["JO_TEXT"] = text;
            }
            else if (Which("notify-send") is { } send) info = new ProcessStartInfo(send, ["-a", "Job Orchestrator", title, text]);
            else return;
            info.UseShellExecute = false;
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>
    /// Processo solto: sobrevive a quem abriu e não prende o terminal. No Linux e no macOS vai por /bin/sh, numa sessão própria (setsid)
    /// ou imune ao fim do terminal (nohup), com a saída num log; no Windows, sem janela de console, e o log pelo cmd quando pedido.
    /// </summary>
    public static bool Detach(string file, IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env = null, string? log = null)
    {
        try
        {
            ProcessStartInfo info;
            if (OperatingSystem.IsWindows())
            {
                if (log is null)
                {
                    info = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var a in args) info.ArgumentList.Add(a);
                }
                else
                {
                    static string Q(string s) => "\"" + s.Replace("\"", "", StringComparison.Ordinal) + "\"";
                    info = new ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                    info.Arguments = $"/d /s /c \"{Q(file)} {string.Join(' ', args.Select(Q))} >>{Q(log)} 2>&1\"";
                }
            }
            else
            {
                info = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add($"exec {(Which("setsid") is not null ? "setsid" : "nohup")} \"$0\" \"$@\" >>'{(log ?? "/dev/null").Replace("'", "", StringComparison.Ordinal)}' 2>&1 </dev/null");
                info.ArgumentList.Add(file);
                foreach (var a in args) info.ArgumentList.Add(a);
            }
            foreach (var (k, v) in env ?? new Dictionary<string, string>()) info.Environment[k] = v;
            using var p = Process.Start(info);
            return p is not null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return false; }
    }

    /// <summary>Saída de um comando curto do sistema; vazio se não rodar.</summary>
    public static string Capture(string file, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info)!;
            var stderr = p.StandardError.ReadToEndAsync();
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(30_000);
            return output + stderr.Result;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return ""; }
    }
}
