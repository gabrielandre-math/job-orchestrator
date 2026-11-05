using System.Collections.Concurrent;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence.Features;

/// <summary>
/// LLM routing: percorre a ordem de gasto que você definiu (Llm:Order), pula quem está sem login, sem instalação, na reserva
/// ou pausado por limite, e nunca usa a família que precisa ser evitada (o revisor é sempre de outra família).
/// Degrau Local vai só para o Ollama. Status dos provedores fica em cache por 60 s para não abrir processo a cada tela.
/// </summary>
internal sealed class IntelligenceApi(IEnumerable<IProvider> providers, IConfiguration config, TimeProvider time, ILogger<IntelligenceApi> log) : IIntelligenceApi
{
    private static readonly ConcurrentDictionary<string, LlmResult> LastTests = new();
    private static readonly ConcurrentDictionary<string, DateTime> PausedUntil = new();
    private static (DateTime At, IReadOnlyList<ProviderView> Views)? cache;
    private static readonly SemaphoreSlim Gate = new(1);

    private string[] Order => config.GetSection("Llm:Order").Get<string[]>() is { Length: > 0 } o ? o : ["claude-pro", "codex", "gemini", "claude-max"];

    public async Task<IReadOnlyList<ProviderView>> ProvidersAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (cache is { } c && now - c.At < TimeSpan.FromSeconds(60)) return Decorate(c.Views, now);
        await Gate.WaitAsync(ct);
        try
        {
            if (cache is { } again && now - again.At < TimeSpan.FromSeconds(60)) return Decorate(again.Views, now);
            var order = Order;
            var probes = providers.Select(async p => await p.ProbeAsync(p.Id == "ollama" ? 0 : Array.IndexOf(order, p.Id) + 1, LastTests.GetValueOrDefault(p.Id), ct));
            var views = (await Task.WhenAll(probes)).OrderBy(v => v.Order == 0 ? -1 : v.Order).ToList();
            cache = (now, views);
            return Decorate(views, now);
        }
        finally { Gate.Release(); }
    }

    /// <summary>Junta o último teste e a pausa por limite ao status em cache.</summary>
    private static IReadOnlyList<ProviderView> Decorate(IReadOnlyList<ProviderView> views, DateTime now) =>
        [.. views.Select(v =>
        {
            var last = LastTests.TryGetValue(v.Id, out var t) ? t : null;
            if (PausedUntil.TryGetValue(v.Id, out var until) && until > now)
                return v with { Available = false, Status = "paused", Detail = $"Pausado por limite de uso até {until.ToLocalTime():HH:mm}.", LastTest = last };
            return v with { LastTest = last };
        })];

    public async Task<LlmResult> CompleteAsync(LlmRequest r, CancellationToken ct)
    {
        var views = await ProvidersAsync(ct);
        var chain = r.Rung == Rung.Local
            ? views.Where(v => v.Id == "ollama")
            : Order.Select(id => views.FirstOrDefault(v => v.Id == id)).OfType<ProviderView>();
        LlmResult? last = null;
        foreach (var v in chain)
        {
            if (!v.Available || (r.AvoidFamily is not null && v.Family == r.AvoidFamily)) continue;
            var provider = providers.First(p => p.Id == v.Id);
            last = await provider.RunAsync(r, ct);
            log.LogInformation("LLM {Task} via {Provider} {Model}/{Effort}: {Ok} in {Seconds:0.0}s", r.Task, last.ProviderId, last.Model, last.Effort, last.Ok, last.Seconds);
            if (last.Ok) return last;
            if (last.Error == "quota")
            {
                // Limite de cota troca de conta no mesmo degrau; o provedor volta sozinho depois de uma hora (ou do reset informado).
                PausedUntil[v.Id] = time.GetUtcNow().UtcDateTime.AddHours(1);
            }
        }
        return last ?? new LlmResult(false, null, "nenhum", "", "", "Nenhum provedor disponível para este pedido.", 0);
    }

    /// <summary>Teste mínimo pedido por você na tela: degrau leve, resposta de uma linha, conta pouca cota.</summary>
    public async Task<LlmResult> TestAsync(string providerId, CancellationToken ct)
    {
        var provider = providers.FirstOrDefault(p => p.Id == providerId);
        if (provider is null) return new LlmResult(false, null, providerId, "", "", "Provedor desconhecido.", 0);
        var schema = """{"type":"object","properties":{"ok":{"type":"boolean"},"echo":{"type":"string"}},"required":["ok","echo"],"additionalProperties":false}""";
        var result = await provider.RunAsync(new LlmRequest("provider.test", "Você responde só com JSON válido no schema pedido.",
            "Responda com ok igual a true e echo igual a \"pronto\".", schema, providerId == "ollama" ? Rung.Local : Rung.Light, TimeoutSeconds: 120), ct);
        LastTests[providerId] = result;
        cache = null;
        return result;
    }
}

/// <summary>Rotas HTTP (Carter) dos provedores de IA.</summary>
internal sealed class ProviderEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/providers").WithTags("Intelligence");
        group.MapGet("", async (IIntelligenceApi api, CancellationToken ct) => Results.Ok(await api.ProvidersAsync(ct)));
        group.MapPost("{id}/test", async (string id, IIntelligenceApi api, CancellationToken ct) => Results.Ok(await api.TestAsync(id, ct)));
    }
}
