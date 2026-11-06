using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;

namespace JobOrchestrator.Intelligence.Providers;

/// <summary>
/// O Ollama desta máquina: onde está o binário (o nosso, o do PATH ou o app oficial do Mac), que pacote baixar para este sistema e
/// como ligar sem o systemd. No Linux com systemd quem liga é o serviço de usuário; no macOS e no Windows, o próprio app.
/// </summary>
internal static class OllamaHost
{
    public const string Unit = "job-orchestrator-ollama.service";

    public static string Dir => OperatingSystem.IsLinux() ? Path.Combine(Platform.Home, ".local", "opt", "ollama") : Path.Combine(AppPaths.Data, "ollama");

    public static bool Systemd => OperatingSystem.IsLinux() && Platform.Which("systemctl") is not null && Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 };

    /// <summary>Um modelo carregado por vez, contexto de 8 mil tokens e preso em 127.0.0.1: cabe ao lado do resto da máquina.</summary>
    public static IReadOnlyDictionary<string, string> Env { get; } = new Dictionary<string, string>
    {
        ["OLLAMA_HOST"] = "127.0.0.1:11434", ["OLLAMA_KEEP_ALIVE"] = "5m", ["OLLAMA_MAX_LOADED_MODELS"] = "1", ["OLLAMA_NUM_PARALLEL"] = "1", ["OLLAMA_CONTEXT_LENGTH"] = "8192",
    };

    /// <summary>Written once every file of the package is out. Without it, an install of ours stopped halfway.</summary>
    public static string Marker => Path.Combine(Dir, ".job-orchestrator-complete");

    /// <summary>True while LocalSetup downloads or extracts Ollama, so the keeper does not start a half-written binary.</summary>
    public static volatile bool Busy;

    /// <summary>
    /// Our own macOS install without the marker: before 1.0.4 the macOS package was extracted with .NET's TarFile, which stops at the
    /// first code-signature header and left the MLX runtime out ("this model requires MLX support"). Linux and Windows packages extract whole.
    /// </summary>
    public static bool Incomplete() => OperatingSystem.IsMacOS() && Binary() is { } b && b.StartsWith(Dir, StringComparison.Ordinal) && !File.Exists(Marker);

    /// <summary>Stops the "ollama serve" this app runs from its own folder, before replacing its files.</summary>
    public static void StopOwn()
    {
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ollama"))
            using (p)
                try
                {
                    if (p.MainModule?.FileName is { } f && f.StartsWith(Dir, StringComparison.Ordinal)) { p.Kill(entireProcessTree: true); p.WaitForExit(5000); }
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }

    public static string? Binary()
    {
        var exe = OperatingSystem.IsWindows() ? "ollama.exe" : "ollama";
        foreach (var path in new[] { Path.Combine(Dir, "bin", exe), Path.Combine(Dir, exe) })
            if (File.Exists(path)) return path;
        return CliRunner.Find("ollama");
    }

    /// <summary>O app oficial do Ollama no Mac: fica na barra de menus e cuida do próprio servidor.</summary>
    public static string? MacApp() =>
        OperatingSystem.IsMacOS() ? new[] { "/Applications/Ollama.app", Path.Combine(Platform.Home, "Applications", "Ollama.app") }.FirstOrDefault(Directory.Exists) : null;

    /// <summary>O pacote oficial deste sistema na lista da release: .tgz antes de .tar.zst (o .NET extrai sozinho), e o ROCm junto para placa AMD no Linux.</summary>
    public static List<string> Assets(IReadOnlyList<string> names, bool amd)
    {
        string[] Pick(params string[] options) => [.. options.Where(names.Contains).Take(1)];
        var arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "amd64";
        if (OperatingSystem.IsMacOS()) return [.. Pick("ollama-darwin.tgz")];
        if (OperatingSystem.IsWindows()) return [.. Pick($"ollama-windows-{arch}.zip")];
        var list = Pick($"ollama-linux-{arch}.tgz", $"ollama-linux-{arch}.tar.zst").ToList();
        if (amd && arch == "amd64") list.AddRange(Pick("ollama-linux-amd64-rocm.tgz", "ollama-linux-amd64-rocm.tar.zst"));
        return list;
    }

    /// <summary>Liga o Ollama: o app oficial do Mac, se for o que existe; senão "ollama serve" solto, com o log na pasta de dados.</summary>
    public static bool Start()
    {
        var own = Binary();
        if (MacApp() is { } app && (own is null || !own.StartsWith(Dir, StringComparison.Ordinal)))
            return Platform.Detach("/usr/bin/open", ["-g", "-a", app]);
        return own is not null && Platform.Detach(own, ["serve"], Env, Path.Combine(AppPaths.In(AppPaths.Data, "run"), "ollama.log"));
    }

    public static async Task<bool> UpAsync()
    {
        try
        {
            using var tcp = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await tcp.ConnectAsync("127.0.0.1", 11434, timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException) { return false; }
    }
}

/// <summary>
/// Sem systemd (macOS, Windows ou Linux sem sessão de usuário), mantém o Ollama ligado enquanto o app roda, se você escolheu um modelo
/// local e o Ollama está instalado. Confere a cada três minutos; nunca instala nada sozinho.
/// </summary>
internal sealed class OllamaKeeper(IConfiguration config, ILogger<OllamaKeeper> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (OllamaHost.Systemd) return;
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            var local = (config["Llm:Ollama:Url"] ?? "http://127.0.0.1:11434").Contains("127.0.0.1", StringComparison.Ordinal) || (config["Llm:Ollama:Url"] ?? "").Contains("localhost", StringComparison.Ordinal);
            if (local && Consent.Accepted && !OllamaHost.Busy && config["Llm:Ollama:Models:Local"] is { Length: > 0 } && !await OllamaHost.UpAsync() && (OllamaHost.Binary() is not null || OllamaHost.MacApp() is not null))
                log.LogInformation("Ollama parado; ligando: {Started}", OllamaHost.Start());
            try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
