namespace JobOrchestrator.Applications.Features;

/// <summary>
/// O automático foi desligado: candidatura pronta pela autorização volta já para a sua aprovação, sem esperar a próxima tentativa
/// de envio. A que você mesmo aprovou continua pronta. Idempotente pela inbox.
/// </summary>
internal sealed class AutoSubmitEndedConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => AutoSubmitEndedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var ended = message.Read<AutoSubmitEndedV1>();
        var ready = await db.Applications.Where(a => a.CampaignId == ended.CampaignId && a.State == ApplicationState.ReadyToSubmit
            && db.Approvals.Where(p => p.ApplicationId == a.Id).OrderByDescending(p => p.At).Select(p => p.ByAuthorization).FirstOrDefault()).ToListAsync(ct);
        foreach (var app in ready) app.Move(ApplicationState.AwaitingApproval, "O envio automático foi desligado: esta vaga espera a sua aprovação.", "Guardião", now);
        // The send that was waiting for tomorrow under the authorization is dropped: approving or a new authorization schedules its own.
        var ids = ready.Select(a => a.Id).ToList();
        foreach (var w in await db.WorkItems.Where(w => ids.Contains(w.ApplicationId) && w.Step == WorkStep.Submit && w.CompletedAt == null).ToListAsync(ct)) w.Complete(now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
