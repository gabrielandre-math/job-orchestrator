using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence.Features;

/// <summary>
/// LLM routing: percorre a ordem de gasto que você definiu (Llm:Order), pula quem está sem login, sem instalação, na reserva
/// ou pausado por limite, e nunca usa a família que precisa ser evitada (o revisor é sempre de outra família).
/// Degraus Local e LocalStrong vão só para o Ollama. Com a nuvem fora (ou o teto do dia atingido), a tarefa que aceita
/// vira rascunho do modelo local forte, marcado como Degraded. Toda chamada fica registrada; respostas boas da nuvem ficam em cache.
/// </summary>
internal sealed class IntelligenceApi(IEnumerable<IProvider> providers, IntelligenceDbContext db, IConfiguration config, TimeProvider time, ILogger<IntelligenceApi> log) : IIntelligenceApi
{
    private static readonly ConcurrentDictionary<string, LlmResult> LastTests = new();
    private static readonly ConcurrentDictionary<string, DateTime> PausedUntil = new();
    private static (DateTime At, IReadOnlyList<ProviderView> Views)? cache;
    private static readonly SemaphoreSlim Gate = new(1);

    private string[] Order => config.GetSection("Llm:Order").Get<string[]>() is { Length: > 0 } o ? o : ["claude-pro", "codex", "claude-max"];
    private DateTime Now => time.GetUtcNow().UtcDateTime;
    private IProvider? Local => providers.FirstOrDefault(p => p.Id == "ollama");

    /// <summary>Só para testes: esquece status, pausas e testes guardados entre instâncias.</summary>
    internal static void Reset()
    {
        LastTests.Clear();
        PausedUntil.Clear();
        cache = null;
    }

    public async Task<IReadOnlyList<ProviderView>> ProvidersAsync(CancellationToken ct)
    {
        var now = Now;
        if (cache is { } c && now - c.At < TimeSpan.FromSeconds(60)) return Decorate(c.Views, now);
        await Gate.WaitAsync(ct);
        try
        {
            if (cache is { } again && now - again.At < TimeSpan.FromSeconds(60)) return Decorate(again.Views, now);
            var order = Order;
            // Quem saiu da ordem de gasto não aparece nem roda.
            var probes = providers.Where(p => p.Id == "ollama" || order.Contains(p.Id))
                .Select(async p => await p.ProbeAsync(p.Id == "ollama" ? 0 : Array.IndexOf(order, p.Id) + 1, LastTests.GetValueOrDefault(p.Id), ct));
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

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (config.GetValue("Llm:Disabled", false)) return new LlmResult(false, null, "nenhum", "", "", "IA desligada nesta instalação.", 0);
        var spec = LlmTasks.Find(request.Task);
        var r = config[$"Llm:Tasks:{request.Task}:Rung"] is { } rung && Enum.TryParse<Rung>(rung, out var forced) ? request with { Rung = forced } : request;
        var local = r.Rung is Rung.Local or Rung.LocalStrong;

        if (r.Thread is { } thread)
            await SayAsync(thread, r.Agent, "ask", $"Pede {Label(r.Task)}, degrau {RungPt(r.Rung)}", Excerpt(r.Prompt, 8000), null, null, null, 0, true, "Roteador", ct);

        var key = CacheKey(r);
        var hit = await db.Cache.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key && x.ExpiresAt > Now, ct);
        if (hit is not null)
        {
            var cached = new LlmResult(true, hit.Json, hit.ProviderId, hit.Model, hit.Effort, null, 0, Cached: true);
            await RecordAsync(r, cached, ct);
            return cached;
        }

        var views = await ProvidersAsync(ct);
        var budget = config.GetValue($"Llm:Tasks:{r.Task}:Budget", spec?.DailyBudget ?? 10);
        var spent = local ? 0 : await db.Calls.CountAsync(x => x.Task == r.Task && x.Ok && !x.Cached && x.ProviderId != "ollama" && x.At > Now.AddDays(-1), ct);
        var overBudget = !local && spent >= budget;
        var chain = local ? views.Where(v => v.Id == "ollama")
            : overBudget ? []
            : Order.Select(id => views.FirstOrDefault(v => v.Id == id)).OfType<ProviderView>();

        LlmResult? last = null;
        foreach (var v in chain)
        {
            if (!v.Available || (r.AvoidFamily is not null && v.Family == r.AvoidFamily)) continue;
            if (r.Thread is { } working) await SayAsync(working, Who(v.Id), "working", $"Pegou o pedido do {r.Agent}", null, v.Id, null, null, 0, true, r.Agent, ct);
            last = await providers.First(p => p.Id == v.Id).RunAsync(r, ct);
            await RecordAsync(r, last, ct);
            if (last.Ok)
            {
                if (!local) await StoreAsync(key, r.Task, last, ct);
                return last;
            }
            // Limite de cota troca de conta no mesmo degrau; o provedor volta sozinho depois de uma hora.
            if (last.Error == "quota") PausedUntil[v.Id] = Now.AddHours(1);
        }

        if (!local && r.LocalFallback && r.AvoidFamily != "local" && Local is { } ollama && views.FirstOrDefault(v => v.Id == "ollama") is { Available: true })
        {
            if (r.Thread is { } working) await SayAsync(working, Who("ollama"), "working", $"Nuvem fora: o modelo local forte faz um rascunho para o {r.Agent}", null, "ollama", null, null, 0, true, r.Agent, ct);
            var draft = await ollama.RunAsync(r with { Rung = Rung.LocalStrong, TimeoutSeconds = Math.Max(r.TimeoutSeconds, 600) }, ct) with { Degraded = true };
            await RecordAsync(r, draft, ct);
            if (draft.Ok) return draft;
            last = draft;
        }
        var none = last ?? new LlmResult(false, null, "nenhum", "", "", overBudget ? $"Teto de {budget} chamadas por dia de {r.Task} atingido." : "Nenhum provedor disponível para este pedido.", 0);
        if (last is null && r.Thread is { } t) await SayAsync(t, "Roteador", "fail", none.Error!, null, null, null, null, 0, false, r.Agent, ct);
        return none;
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
        await RecordAsync(new LlmRequest("provider.test", "", "", "", Rung.Light), result, ct);
        cache = null;
        return result;
    }

    public async Task<IReadOnlyList<LocalModelView>> LocalModelsAsync(CancellationToken ct)
    {
        if (providers.OfType<OllamaLocal>().FirstOrDefault() is not { } ollama) return [];
        try { return await ollama.TagsAsync(ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return []; }
    }

    /// <summary>Uso das últimas 24 h por tarefa do catálogo.</summary>
    public async Task<IReadOnlyList<TaskUsageView>> UsageAsync(CancellationToken ct)
    {
        var since = Now.AddDays(-1);
        var calls = await db.Calls.AsNoTracking().Where(x => x.At > since).Select(x => new { x.Task, x.ProviderId, x.Ok, x.Cached }).ToListAsync(ct);
        return [.. LlmTasks.All.Select(t =>
        {
            var mine = calls.Where(c => c.Task == t.Name).ToList();
            var rung = config[$"Llm:Tasks:{t.Name}:Rung"] ?? t.Rung.ToString();
            return new TaskUsageView(t.Name, rung, t.Privacy, mine.Count(c => c.Ok && !c.Cached && c.ProviderId != "ollama"),
                config.GetValue($"Llm:Tasks:{t.Name}:Budget", t.DailyBudget), mine.Count(c => c.Ok && c.ProviderId == "ollama"), mine.Count(c => c.Cached), mine.Count(c => !c.Ok));
        })];
    }

    public async Task<IReadOnlyList<LlmCallView>> CallsAsync(int limit, CancellationToken ct) =>
        await db.Calls.AsNoTracking().OrderByDescending(x => x.At).Take(Math.Clamp(limit, 1, 200))
            .Select(x => new LlmCallView(x.At, x.Task, x.ProviderId, x.Model, x.Effort, x.Ok, x.Error, x.Seconds, x.Degraded, x.Cached)).ToListAsync(ct);

    private async Task RecordAsync(LlmRequest request, LlmResult r, CancellationToken ct)
    {
        var task = request.Task;
        log.LogInformation("LLM {Task} via {Provider} {Model}/{Effort}: {Ok} in {Seconds:0.0}s{Flags}", task, r.ProviderId, r.Model, r.Effort, r.Ok, r.Seconds,
            r.Degraded ? " (rascunho local)" : r.Cached ? " (cache)" : "");
        db.Calls.Add(new LlmCall
        {
            At = Now, Task = task, ProviderId = Cut(r.ProviderId, 40), Model = Cut(r.Model, 80), Effort = Cut(r.Effort, 20), Ok = r.Ok,
            Error = r.Error is null ? null : Cut(r.Error, 300), Seconds = Math.Round(r.Seconds, 2), Degraded = r.Degraded, Cached = r.Cached
        });
        await db.SaveChangesAsync(ct);
        if (request.Thread is { } thread)
        {
            var title = r.Ok ? r.Cached ? "Respondeu do cache (mesmo pedido nos últimos 30 dias)" : r.Degraded ? "Respondeu como rascunho local (nuvem fora)" : "Respondeu"
                : r.Error == "quota" ? "Sem cota agora; o roteador tenta o próximo da ordem" : $"Falhou: {r.Error}";
            await SayAsync(thread, Who(r.ProviderId), r.Ok ? "answer" : "fail", title, r.Ok ? Excerpt(r.Json ?? "", 20000) : r.Error,
                r.ProviderId, r.Model, r.Effort, r.Seconds, r.Ok, request.Agent, ct);
        }
    }

    public Task NoteAsync(AgentThread thread, string agent, string title, string? body, string? to = null, bool ok = true, CancellationToken ct = default) =>
        SayAsync(thread, agent, "note", title, body is null ? null : Excerpt(body, 20000), null, null, null, 0, ok, to, ct);

    private async Task SayAsync(AgentThread t, string agent, string kind, string title, string? body, string? provider, string? model, string? effort, double seconds, bool ok, string? to, CancellationToken ct)
    {
        db.Messages.Add(new AgentMessage
        {
            ThreadId = t.Id, ThreadKind = Cut(t.Kind, 40), Subject = Cut(t.Subject, 200), At = Now, Agent = Cut(agent.Length > 0 ? agent : "Agente", 60), Kind = kind,
            ProviderId = provider, Model = model is null ? null : Cut(model, 80), Effort = effort is null ? null : Cut(effort, 20), Title = Cut(title, 300), Body = body,
            To = to is null ? null : Cut(to, 60), Seconds = Math.Round(seconds, 2), Ok = ok
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AgentThreadView>> ThreadsAsync(int limit, CancellationToken ct)
    {
        var recent = await db.Messages.AsNoTracking().GroupBy(m => m.ThreadId)
            .Select(g => new { Id = g.Key, Last = g.Max(m => m.At) }).OrderByDescending(x => x.Last).Take(Math.Clamp(limit, 1, 100)).ToListAsync(ct);
        var ids = recent.Select(x => x.Id).ToList();
        var messages = await db.Messages.AsNoTracking().Where(m => ids.Contains(m.ThreadId))
            .Select(m => new { m.ThreadId, m.ThreadKind, m.Subject, m.At, m.Agent, m.ProviderId, m.Ok, m.Kind }).ToListAsync(ct);
        return [.. recent.Select(r =>
        {
            var all = messages.Where(m => m.ThreadId == r.Id).OrderBy(m => m.At).ToList();
            return new AgentThreadView(r.Id, all[0].ThreadKind, all[0].Subject, all[0].At, r.Last, all.Count,
                [.. all.Select(m => m.Agent).Distinct()], [.. all.Where(m => m.ProviderId is not null).Select(m => m.ProviderId!).Distinct()], all.Any(m => m.Kind == "fail" && m.Agent == "Roteador"));
        })];
    }

    public async Task<IReadOnlyList<AgentMessageView>> ThreadAsync(Guid id, CancellationToken ct) =>
        await db.Messages.AsNoTracking().Where(m => m.ThreadId == id).OrderBy(m => m.At).ThenBy(m => m.Id)
            .Select(m => new AgentMessageView(m.Id, m.At, m.Agent, m.Kind, m.ProviderId, m.Model, m.Effort, m.Title, m.Body, m.Seconds, m.Ok, m.To)).ToListAsync(ct);

    /// <summary>Nome do agente que respondeu, pelo provedor.</summary>
    private static string Who(string providerId) => providerId switch
    {
        "claude-pro" => "Claude Pro", "claude-max" => "Claude Max", "codex" => "ChatGPT (Codex)", "ollama" => "Llama local", _ => providerId
    };

    private static string Label(string task) => task switch
    {
        "profile.extract" => "a leitura do currículo", "resume.tailor" => "o currículo para a vaga", "resume.factcheck" => "a revisão das afirmações",
        "cover.write" => "a carta", "question.draft" => "a resposta aberta", "interview.prep" => "o guia de entrevista", "question.map" => "a ligação da pergunta",
        "job.extract" => "a leitura da vaga", "email.classify" => "a classificação do e-mail", _ => task
    };

    private static string RungPt(Rung r) => r switch
    {
        Rung.Local => "local rápido", Rung.LocalStrong => "local forte", Rung.Light => "leve", Rung.Medium => "médio", Rung.Strong => "forte", _ => "topo"
    };

    private static string Excerpt(string s, int max) => s.Length > max ? s[..max] + "\n[…]" : s;

    private async Task StoreAsync(string key, string task, LlmResult r, CancellationToken ct)
    {
        if (await db.Cache.AnyAsync(x => x.Key == key, ct)) await db.Cache.Where(x => x.Key == key).ExecuteDeleteAsync(ct);
        db.Cache.Add(new CachedAnswer { Key = key, Task = task, Json = r.Json!, ProviderId = r.ProviderId, Model = r.Model, Effort = r.Effort, CreatedAt = Now, ExpiresAt = Now.AddDays(30) });
        await db.SaveChangesAsync(ct);
    }

    internal static string CacheKey(LlmRequest r) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', r.Task, r.Rung, r.AvoidFamily ?? "", r.System, r.JsonSchema, r.Prompt))));

    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
}

/// <summary>Rotas HTTP (Carter) dos provedores de IA.</summary>
internal sealed class ProviderEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/providers").WithTags("Intelligence");
        group.MapGet("", async (IIntelligenceApi api, CancellationToken ct) => Results.Ok(await api.ProvidersAsync(ct)));
        group.MapPost("{id}/test", async (string id, IIntelligenceApi api, CancellationToken ct) => Results.Ok(await api.TestAsync(id, ct)));
        group.MapGet("local-models", async (IIntelligenceApi api, CancellationToken ct) => Results.Ok(await api.LocalModelsAsync(ct)));
        group.MapGet("usage", async (IntelligenceApi api, CancellationToken ct) => Results.Ok(await api.UsageAsync(ct)));
        group.MapGet("calls", async (int? limit, IntelligenceApi api, CancellationToken ct) => Results.Ok(await api.CallsAsync(limit ?? 40, ct)));
        var agents = app.MapGroup("/api/agents").WithTags("Intelligence");
        agents.MapGet("threads", async (int? limit, IntelligenceApi api, CancellationToken ct) => Results.Ok(await api.ThreadsAsync(limit ?? 30, ct)));
        agents.MapGet("threads/{id:guid}", async (Guid id, IntelligenceApi api, CancellationToken ct) => Results.Ok(await api.ThreadAsync(id, ct)));
        // Ao vivo: cada mensagem nova dos agentes chega pela notificação do Postgres e sai por Server-Sent Events.
        agents.MapGet("stream", (IConfiguration config, IServiceProvider sp, CancellationToken ct) =>
            TypedResults.ServerSentEvents(LiveStream.ReadAsync(config.ConnectionFor("Intelligence"), sp, ct), eventType: "message"));
        // Resultado do último `jo llm bench` (bench/latest.json no diretório de dados), para a tela mostrar por que cada modelo está na vaga.
        group.MapGet("bench", (IConfiguration config) =>
        {
            var path = config["Llm:Bench:Path"] is { Length: > 0 } p ? p : Path.Combine(AppPaths.In(AppPaths.Data, "bench"), "latest.json");
            return File.Exists(path) ? Results.Text(File.ReadAllText(path), "application/json") : Results.Ok(Array.Empty<object>());
        });
    }
}
