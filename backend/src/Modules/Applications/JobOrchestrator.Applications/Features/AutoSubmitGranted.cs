namespace JobOrchestrator.Applications.Features;

/// <summary>
/// A new authorization: the applications that waited for your approval under the old limits (a higher score, another adapter) are
/// prepared again, so the Guardian decides under the new ones; with the declarations accepted, the ones stopped on a privacy or terms
/// box go back to the form too. One every minute, so the browser opens one site at a time. Idempotent through the inbox.
/// </summary>
internal sealed class AutoSubmitGrantedConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => AutoSubmitGrantedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var granted = message.Read<AutoSubmitGrantedV1>();
        var waiting = await db.Applications.Where(a => a.CampaignId == granted.CampaignId && (a.State == ApplicationState.AwaitingApproval
            || granted.AcceptDeclarations && a.State == ApplicationState.NeedsHuman && a.LastReason!.StartsWith("O formulário pede uma declaração"))).ToListAsync(ct);
        var ids = waiting.Select(a => a.Id).ToList();
        // A send left scheduled from before would block the new one: the form is prepared again and schedules its own.
        foreach (var w in await db.WorkItems.Where(w => ids.Contains(w.ApplicationId) && w.Step == WorkStep.Submit && w.CompletedAt == null).ToListAsync(ct)) w.Complete(now);
        var queued = (await db.WorkItems.Where(w => ids.Contains(w.ApplicationId) && w.Step == WorkStep.Prepare && w.CompletedAt == null).Select(w => w.ApplicationId).ToListAsync(ct)).ToHashSet();
        var i = 0;
        // The best matches go first.
        foreach (var app in waiting.OrderByDescending(a => a.Score))
        {
            app.Move(ApplicationState.FormPreparing, app.State == ApplicationState.AwaitingApproval
                ? "Nova autorização: o formulário é conferido de novo e o Guardião decide pelos limites novos."
                : "Nova autorização aceita as declarações: o formulário é preenchido de novo.", "Supervisor", now);
            await db.ResolveTasksAsync(app.Id, "Nova autorização", now, ct);
            if (!queued.Contains(app.Id)) db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Prepare, now.AddMinutes(i++), now));
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
