using System.Text.Json;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>
/// Os termos de uso aceitos na primeira vez: sem o aceite, nada roda sozinho na máquina (busca contínua, Explorador, rodadas da noite,
/// Ollama ligado pelo app). O aceite fica em terms.json na pasta de configuração, com a versão; termos novos pedem aceite de novo.
/// </summary>
public static class Consent
{
    public const string Version = "2026-11-05";
    private static string FilePath => Path.Combine(AppPaths.Config, "terms.json");

    public sealed record Record(string Version, DateTime AcceptedAt);

    public static Record? Current()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Record>(File.ReadAllText(FilePath), JsonSerializerOptions.Web) : null; }
        catch (Exception ex) when (ex is JsonException or IOException) { return null; }
    }

    public static bool Accepted => Current()?.Version == Version;

    public static Record Accept()
    {
        var record = new Record(Version, DateTime.UtcNow);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(record, JsonSerializerOptions.Web));
        return record;
    }
}
