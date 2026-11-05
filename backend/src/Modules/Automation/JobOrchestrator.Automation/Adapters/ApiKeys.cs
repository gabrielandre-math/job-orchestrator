using System.Collections.Concurrent;

namespace JobOrchestrator.Automation.Adapters;

internal sealed record ApiKeyInfo(string Id, string Name, IReadOnlyList<string> Fields, string Help, string Adds);

/// <summary>
/// Chaves de API das fontes (Adzuna, Jooble, Brave Search, GitHub) no gnome-keyring, como as senhas de e-mail: nunca em arquivo, banco ou log.
/// Lidas com cache de 5 minutos para não abrir o secret-tool a cada consulta.
/// </summary>
internal static class ApiKeys
{
    public static readonly ApiKeyInfo[] Catalog =
    [
        new("adzuna", "Adzuna", ["appId", "appKey"], "https://developer.adzuna.com/signup", "Agregador com vagas do Brasil inteiro, de todas as áreas, por API oficial."),
        new("jooble", "Jooble", ["key"], "https://jooble.org/api/about", "Agregador com vagas de muitos sites brasileiros, de todas as áreas."),
        new("brave", "Brave Search", ["key"], "https://api-dashboard.search.brave.com/register", "Busca na web inteira por API oficial: o Explorador acha quadros de vagas e sites de carreira pelas palavras do currículo."),
        new("github", "GitHub", ["token"], "https://github.com/settings/personal-access-tokens/new", "Token só de leitura pública: o limite sobe de 60 para 5.000 consultas por hora e o Explorador testa mais repositórios de vagas."),
    ];

    private static readonly ConcurrentDictionary<string, (string? Value, DateTime At)> Cache = new();

    public static async Task<string?> GetAsync(string id, string field, CancellationToken ct)
    {
        var account = $"source:{id}:{field}";
        if (Cache.TryGetValue(account, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromMinutes(5)) return hit.Value;
        string? value = null;
        try { value = await Secrets.GetAsync(account, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { /* sem keyring: a fonte fica sem chave */ }
        Cache[account] = (value, DateTime.UtcNow);
        return value;
    }

    public static async Task<bool> HasAsync(string id, CancellationToken ct)
    {
        var info = Catalog.FirstOrDefault(c => c.Id == id);
        if (info is null) return false;
        foreach (var f in info.Fields) if (await GetAsync(id, f, ct) is not { Length: > 0 }) return false;
        return true;
    }

    public static async Task<bool> StoreAsync(string id, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        foreach (var (field, value) in values)
        {
            if (!await Secrets.StoreAsync($"source:{id}:{field}", value.Trim(), ct)) return false;
            Cache.TryRemove($"source:{id}:{field}", out _);
        }
        return true;
    }

    public static async Task ClearAsync(string id, CancellationToken ct)
    {
        foreach (var f in Catalog.FirstOrDefault(c => c.Id == id)?.Fields ?? [])
        {
            await Secrets.ClearAsync($"source:{id}:{f}", ct);
            Cache.TryRemove($"source:{id}:{f}", out _);
        }
    }
}
