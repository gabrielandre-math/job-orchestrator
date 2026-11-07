namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>
/// Supervisor da fila: toma um item, renova o lease enquanto a etapa roda e trata falha com backoff e jitter.
/// Etapa que esgota as tentativas vai para DeadLetter. Uma etapa por vez nesta fase (um envio por vez).
/// </summary>
internal sealed class WorkRunner(IServiceScopeFactory scopes, WorkQueue queue, ILogger<WorkRunner> log, Trail trail) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var released = await queue.ReleaseStaleAsync(ct);
        if (released > 0) log.LogWarning("Released {Count} leases from a previous process", released);
        while (!ct.IsCancellationRequested)
        {
            Claimed? item = null;
            try
            {
                item = await queue.ClaimAsync(ct);
                if (item is null) { await Task.Delay(TimeSpan.FromMilliseconds(500), ct); continue; }
                await RunAsync(item, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Work loop failure");
                if (item is not null) await FailAsync(item, ex.Message, null, ct);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
        }
    }

    private async Task RunAsync(Claimed item, CancellationToken ct)
    {
        using var renew = new CancellationTokenSource();
        var renewing = RenewAsync(item.Id, renew.Token);
        // Every event of this step, from the form to the AI calls, carries the application in the trail.
        Trail.Trace = $"app:{item.ApplicationId}";
        var data = new Dictionary<string, string?> { ["application"] = item.ApplicationId.ToString(), ["attempt"] = item.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        int Ms() => (int)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var step = scope.ServiceProvider.GetServices<IStep>().First(s => s.Step == item.Kind);
            trail.Info("step", $"{item.Step} começou", data: data);
            await step.RunAsync(item, ct);
            trail.Info("step", $"{item.Step} terminou", ms: Ms(), data: data);
        }
        catch (RetryLaterException ex) { trail.Warn("step", $"{item.Step} espera", ex.Message, Ms(), "retry-later", data); await FailAsync(item, ex.Message, ex.Delay, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Step {Step} failed for {Application}", item.Step, item.ApplicationId);
            trail.Error("step", $"{item.Step} falhou", $"{ex.GetType().Name}: {ex.Message}", Ms(), "exception", data);
            await FailAsync(item, ex.Message, null, ct);
        }
        finally
        {
            await renew.CancelAsync();
            try { await renewing; } catch (OperationCanceledException) { }
        }
    }

    private async Task RenewAsync(Guid id, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            await queue.RenewAsync(id, ct);
        }
    }

    /// <summary>Repete com backoff exponencial e jitter; esgotou, vai para DeadLetter. Submit nunca é repetido às cegas: a etapa olha o estado.</summary>
    private async Task FailAsync(Claimed item, string error, TimeSpan? delay, CancellationToken ct)
    {
        if (item.Attempts < item.MaxAttempts || item.Kind == WorkStep.Submit)
        {
            var wait = delay ?? TimeSpan.FromSeconds(Math.Min(600, 5 * Math.Pow(2, item.Attempts)) * (0.8 + Random.Shared.NextDouble() * 0.4));
            await queue.RetryAsync(item.Id, wait, error, ct);
            return;
        }
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var app = await db.Applications.FirstAsync(a => a.Id == item.ApplicationId, ct);
        (await db.WorkItems.FirstAsync(w => w.Id == item.Id, ct)).Complete(now);
        if (Transitions.Allowed(app.State, ApplicationState.DeadLetter))
            app.Move(ApplicationState.DeadLetter, $"A etapa {item.Step} falhou {item.Attempts} vezes: {error}", "Supervisor", now);
        else if (Transitions.Allowed(app.State, ApplicationState.NeedsHuman))
        {
            app.Move(ApplicationState.NeedsHuman, $"A etapa {item.Step} falhou {item.Attempts} vezes: {error}", "Supervisor", now);
            db.AddTask(app.Id, HumanTaskKind.Review, "Etapa travada", error, now);
        }
        await db.SaveChangesAsync(ct);
    }
}
