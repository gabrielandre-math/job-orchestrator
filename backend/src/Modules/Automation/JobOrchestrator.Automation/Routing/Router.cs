namespace JobOrchestrator.Automation.Routing;

/// <summary>
/// Capability routing: elimina quem não pode (capacidade, domínio, circuito), dá nota a quem sobra e tenta em ordem.
/// Fallback só quando é seguro: campo não suportado ou falha de preparo. Envio e reconciliação nunca trocam de adapter.
/// </summary>
internal sealed class Router(IEnumerable<IFormAdapter> adapters, AutomationDbContext db, TimeProvider time, IOptions<AutomationOptions> options, ILogger<Router> log)
{
    private const int CircuitThreshold = 3;
    private static readonly TimeSpan CircuitPause = TimeSpan.FromMinutes(10);

    public async Task<AutomationOutcome<T>> RunAsync<T>(
        Guid applicationId, Capability capability, Uri url, string? pinnedAdapter,
        Func<IFormAdapter, Task<AdapterResult<T>>> operation, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var route = new List<RoutingStep>();
        var stats = await db.Stats.AsNoTracking().Where(s => s.Domain == url.Authority && s.Capability == capability.ToString()).ToListAsync(ct);
        var ranked = new List<(IFormAdapter Adapter, double Score, string Why)>();
        foreach (var a in adapters)
        {
            if (pinnedAdapter is not null && a.Id != pinnedAdapter) continue;
            if (!a.Capabilities.Contains(capability)) { route.Add(new(a.Id, 0, "skipped", $"não faz {capability}")); continue; }
            var fit = a.Fit(url);
            if (fit <= 0) { route.Add(new(a.Id, 0, "skipped", $"não atende {url.Authority}")); continue; }
            var s = stats.FirstOrDefault(x => x.AdapterId == a.Id);
            if (s?.CircuitOpenUntil > now) { route.Add(new(a.Id, 0, "skipped", $"circuito aberto até {s.CircuitOpenUntil:HH:mm}")); continue; }
            ranked.Add((a, Score(a, s, fit, out var why), why));
        }

        // Only a pause by repeated failures stood in the way: the step waits for it to end instead of handing the job to you.
        AutomationOutcome<T> outcome = ranked.Count == 0 && route.Any(r => r.Why.StartsWith("circuito aberto", StringComparison.Ordinal))
            ? new(AdapterResultKind.RetryableFailure, default, "circuit-open", "O adapter deste site está em pausa por falhas seguidas; tenta de novo quando a pausa acabar.", route)
            : new(AdapterResultKind.Unsupported, default, "no-adapter", "Nenhum adapter testado atende este domínio e esta etapa.", route);
        foreach (var (adapter, score, why) in ranked.OrderByDescending(r => r.Score))
        {
            AdapterResult<T> result;
            try
            {
                result = await operation(adapter);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Adapter {Adapter} failed on {Capability}", adapter.Id, capability);
                // Depois de começar um envio, exceção não prova nada (o erro pode vir do redirecionamento depois do clique): só a
                // reconciliação decide. Fora do envio, uma página que nem abriu diz o que houve.
                result = (capability == Capability.SubmitApplication ? null : Unreachable<T>(ex.Message))
                    ?? (capability == Capability.SubmitApplication
                        ? AdapterResult<T>.Fail(AdapterResultKind.SubmissionUnknown, "exception", "O adapter caiu durante o envio.")
                        : AdapterResult<T>.Fail(AdapterResultKind.RetryableFailure, "exception", "O adapter caiu; a etapa pode ser repetida."));
            }
            route.Add(new(adapter.Id, Math.Round(score, 3), result.Kind.ToString(), $"{why}; {result.Detail}"));
            await RecordAsync(adapter, url.Authority, capability, result, ct);
            outcome = new(result.Kind, result.Value, result.ReasonCode, result.Detail, route, result.Questions);
            // A form that opened and only lacks answers is not the adapter's fault: another adapter would ask the same questions.
            var fallback = pinnedAdapter is null && capability == Capability.PrepareForm && result.Questions is null
                && result.Kind is AdapterResultKind.Unsupported or AdapterResultKind.PermanentFailure;
            if (!fallback || result.ReasonCode is "posting-closed" or "site-unreachable") break;
        }

        db.Routes.Add(new RoutingRecord
        {
            Id = Guid.CreateVersion7(), ApplicationId = applicationId, Capability = capability.ToString(),
            ChosenAdapter = route.LastOrDefault(r => r.Outcome != "skipped")?.AdapterId, Outcome = outcome.Kind.ToString(),
            Route = JsonSerializer.Serialize(route, JsonSerializerOptions.Web), At = now
        });
        await db.SaveChangesAsync(ct);
        return outcome;
    }

    /// <summary>
    /// The job's page never loaded while preparing: a domain that does not exist is gone for good, a site that refused or did not answer
    /// is tried again later. Any other exception stays the adapter's.
    /// </summary>
    internal static AdapterResult<T>? Unreachable<T>(string error)
    {
        if (error.Contains("net::ERR_NAME_NOT_RESOLVED", StringComparison.Ordinal))
            return AdapterResult<T>.Fail(AdapterResultKind.PermanentFailure, "site-unreachable", "O endereço da vaga não existe mais: o domínio não responde. Nada foi enviado.");
        if (error.Contains("net::ERR_CONNECTION_REFUSED", StringComparison.Ordinal) || error.Contains("net::ERR_CONNECTION_TIMED_OUT", StringComparison.Ordinal)
            || error.Contains("net::ERR_CONNECTION_RESET", StringComparison.Ordinal) || error.Contains("net::ERR_ADDRESS_UNREACHABLE", StringComparison.Ordinal))
            return AdapterResult<T>.Fail(AdapterResultKind.RetryableFailure, "site-down", "O site da vaga não respondeu agora. Nada foi enviado; o app tenta de novo mais tarde.");
        return null;
    }

    /// <summary>Nota de 0 a 1 (docs/PLANO.md §5). Sucesso suavizado: (confirmados + 1) / (tentativas + 2).</summary>
    private double Score(IFormAdapter a, AdapterStat? s, double fit, out string why)
    {
        var w = options.Value.Weights;
        var success = ((s?.Confirmed ?? 0) + 1.0) / ((s?.Attempts ?? 0) + 2.0);
        var health = s is null || s.Attempts == 0 ? 1 : 1 - Math.Min(1, (double)s.ConsecutiveFailures / CircuitThreshold);
        var score = w.Success * success + w.Compat * fit + w.Risk * (1 - a.Risk) + w.Health * health;
        why = $"sucesso {success:P0}, compatibilidade {fit:0.#}, risco {a.Risk:0.##}, saúde {health:P0}";
        return score;
    }

    /// <summary>Atualiza o placar com incremento atômico no banco, para dois workers não se atropelarem.</summary>
    private Task RecordAsync<T>(IFormAdapter a, string domain, Capability capability, AdapterResult<T> r, CancellationToken ct)
    {
        var ok = r.Kind == AdapterResultKind.Completed;
        // A send the robot could not confirm is not a failure of the adapter (the site may just say "thank you" in other words): it does
        // not pause the adapter, or one unrecognized confirmation page would hold every approved application behind it.
        var failure = r.Kind is AdapterResultKind.RetryableFailure or AdapterResultKind.PermanentFailure;
        var openUntil = time.GetUtcNow().UtcDateTime.Add(CircuitPause);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO automation.adapter_stats (adapter_id, domain, capability, version, attempts, confirmed, failures, consecutive_failures, last_failure_at, last_failure_code)
            VALUES ({a.Id}, {domain}, {capability.ToString()}, {a.Version}, 1, {(ok ? 1 : 0)}, {(failure ? 1 : 0)}, {(failure ? 1 : 0)},
                    {(failure ? time.GetUtcNow().UtcDateTime : null)}, {(failure ? r.ReasonCode : null)})
            ON CONFLICT (adapter_id, domain, capability) DO UPDATE SET
              version = excluded.version,
              attempts = adapter_stats.attempts + 1,
              confirmed = adapter_stats.confirmed + excluded.confirmed,
              failures = adapter_stats.failures + excluded.failures,
              consecutive_failures = CASE WHEN {failure} THEN adapter_stats.consecutive_failures + 1 ELSE 0 END,
              last_failure_at = COALESCE(excluded.last_failure_at, adapter_stats.last_failure_at),
              last_failure_code = COALESCE(excluded.last_failure_code, adapter_stats.last_failure_code),
              circuit_open_until = CASE WHEN {failure} AND adapter_stats.consecutive_failures + 1 >= {CircuitThreshold} THEN {openUntil} ELSE adapter_stats.circuit_open_until END
            """, ct);
    }
}
