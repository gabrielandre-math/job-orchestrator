using System.Collections.Concurrent;

namespace JobOrchestrator.Automation.Contracts;

/// <summary>
/// Sources that stop early on purpose (pages and details per run are capped to go easy on the sites) say so here, so a short list
/// reads as partial and not as everything there is. A job missing from a partial list is never closed for that.
/// </summary>
public static class SourceCoverage
{
    private static readonly ConcurrentDictionary<string, string> Partial = new(StringComparer.OrdinalIgnoreCase);

    public static void Report(string source, int read, int total)
    {
        if (total > read) Partial[source] = $"lista parcial: {read} de {total}";
        else Partial.TryRemove(source, out _);
    }

    public static string? Of(string source) => Partial.TryGetValue(source, out var note) ? note : null;
}
