namespace JobOrchestrator.BuildingBlocks;

/// <summary>Pastas do usuário (XDG). Nada de dado pessoal dentro do repositório.</summary>
public static class AppPaths
{
    private static string Xdg(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } dir ? dir : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), fallback);

    public static string Data => Ensure(Environment.GetEnvironmentVariable("JO_DATA_DIR") is { Length: > 0 } d ? d : Path.Combine(Xdg("XDG_DATA_HOME", ".local/share"), "job-orchestrator"));
    public static string Config => Ensure(Environment.GetEnvironmentVariable("JO_CONFIG_DIR") is { Length: > 0 } d ? d : Path.Combine(Xdg("XDG_CONFIG_HOME", ".config"), "job-orchestrator"));
    public static string In(string root, params string[] parts) => Ensure(Path.Combine([root, .. parts]));

    private static string Ensure(string dir)
    {
        if (Directory.Exists(dir)) return dir;
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
        else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return dir;
    }
}

/// <summary>
/// Pontos de queda para os testes de caos: com JO_CRASH_AT igual ao nome do ponto, o processo morre na hora,
/// sem finally nem desligamento limpo, como num kill -9.
/// </summary>
public static class Chaos
{
    private static readonly string? Target = Environment.GetEnvironmentVariable("JO_CRASH_AT");
    public static void Point(string name)
    {
        if (string.Equals(Target, name, StringComparison.Ordinal)) Environment.FailFast($"chaos: {name}");
    }
}
