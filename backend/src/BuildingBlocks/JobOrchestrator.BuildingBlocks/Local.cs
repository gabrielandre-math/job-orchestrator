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

/// <summary>
/// Escolhas feitas na tela (modelos locais, varredura, e-mail) em settings.json no diretório de configuração.
/// API e Worker leem o arquivo com recarga automática, então a mudança vale sem reiniciar.
/// </summary>
public static class UserSettings
{
    private static readonly SemaphoreSlim Gate = new(1);

    /// <summary>Grava valores por caminho com ":" (ex.: "Scraper:MaxPages"); null remove a chave.</summary>
    public static async Task SetAsync(IReadOnlyDictionary<string, object?> values)
    {
        await Gate.WaitAsync();
        try
        {
            var path = Path.Combine(AppPaths.Config, "settings.json");
            var root = File.Exists(path) ? System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))?.AsObject() ?? [] : new System.Text.Json.Nodes.JsonObject();
            foreach (var (key, value) in values)
            {
                var parts = key.Split(':');
                var node = root;
                foreach (var part in parts[..^1])
                {
                    if (node[part] is not System.Text.Json.Nodes.JsonObject next) { next = []; node[part] = next; }
                    node = next;
                }
                if (value is null) node.Remove(parts[^1]);
                // Pelo tipo real (bool, int, string, lista): JsonValue.Create(object) vira um valor que o escritor não serializa sem resolvedor.
                else node[parts[^1]] = System.Text.Json.JsonSerializer.SerializeToNode(value, value.GetType(), System.Text.Json.JsonSerializerOptions.Web);
            }
            await File.WriteAllTextAsync(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerOptions.Web) { WriteIndented = true }));
        }
        finally { Gate.Release(); }
    }
}
