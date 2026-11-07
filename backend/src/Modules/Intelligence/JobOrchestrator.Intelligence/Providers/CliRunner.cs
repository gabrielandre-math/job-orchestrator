namespace JobOrchestrator.Intelligence.Providers;

/// <summary>Saída de um processo de CLI.</summary>
internal sealed record CliOutput(int Exit, string Stdout, string Stderr, bool TimedOut);

/// <summary>
/// Roda os CLIs de IA com ambiente limpo: só variáveis de sistema da lista, nenhuma chave de API, nada de AWS ou Vertex.
/// Pasta de trabalho própria e vazia, timeout com encerramento da árvore inteira de processos.
/// </summary>
internal static class CliRunner
{
    private static readonly string[] Keep = [.. Platform.BaseEnvironment, "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR", "DBUS_SESSION_BUS_ADDRESS"];

    public static string Sandbox => AppPaths.In(AppPaths.Data, "llm-sandbox");

    public static async Task<CliOutput> RunAsync(string file, IEnumerable<string> args, string? stdin, TimeSpan timeout, IReadOnlyDictionary<string, string>? extraEnv, CancellationToken ct)
    {
        var info = new ProcessStartInfo(file) { WorkingDirectory = Sandbox, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        info.Environment.Clear();
        foreach (var key in Keep)
            if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } v) info.Environment[key] = v;
        foreach (var (k, v) in extraEnv ?? new Dictionary<string, string>()) info.Environment[k] = v;
        info.Environment["NO_COLOR"] = "1";

        using var p = new Process { StartInfo = info };
        try { p.Start(); }
        catch (System.ComponentModel.Win32Exception) { return new CliOutput(-1, "", $"{file} não encontrado", false); }
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        if (stdin is not null) await p.StandardInput.WriteAsync(stdin);
        p.StandardInput.Close();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            // Whoever asked gave up (the page closed, the app is stopping): that is not the model running out of time, and the
            // provider must not be marked as failing for it.
            ct.ThrowIfCancellationRequested();
            return new CliOutput(-1, "", "tempo esgotado", true);
        }
        return new CliOutput(p.ExitCode, await stdout, await stderr, false);
    }

    /// <summary>
    /// Caminho do executável: o PATH do processo, depois as pastas onde os instaladores costumam pôr as CLIs (o mise, ~/.local/bin,
    /// o npm global do Windows em %APPDATA%\npm, o Homebrew no macOS). No Windows, acha claude.exe, codex.cmd e afins pelo PATHEXT.
    /// </summary>
    public static string? Find(string name)
    {
        var home = Platform.Home;
        var extra = new List<string> { Path.Combine(home, ".local", "share", "mise", "shims"), Path.Combine(home, ".local", "bin") };
        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            extra.AddRange([Path.Combine(appData, "npm"), Path.Combine(local, "Programs", "Ollama"), Path.Combine(local, "mise", "shims"), Path.Combine(home, "AppData", "Local", "Microsoft", "WinGet", "Links")]);
        }
        return Platform.Which(name, [.. extra]);
    }

    /// <summary>Sinais de limite de uso na saída dos CLIs.</summary>
    public static bool LooksLikeQuota(string text) =>
        text.Contains("usage limit", StringComparison.OrdinalIgnoreCase) || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
        || text.Contains("limit reached", StringComparison.OrdinalIgnoreCase) || text.Contains("quota", StringComparison.OrdinalIgnoreCase)
        || text.Contains("resets at", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tira cercas de código e espaço em volta: alguns modelos embrulham o JSON.</summary>
    public static string? JsonOf(string text)
    {
        var t = text.Trim();
        if (t.StartsWith("```", StringComparison.Ordinal)) t = t[(t.IndexOf('\n') + 1)..].TrimEnd('`', '\n', ' ');
        try { using var _ = JsonDocument.Parse(t); return t; } catch (JsonException) { return null; }
    }
}
