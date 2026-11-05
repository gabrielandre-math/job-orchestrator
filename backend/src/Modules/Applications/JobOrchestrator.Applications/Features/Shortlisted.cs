namespace JobOrchestrator.Applications.Features;

/// <summary>Vaga entrou na fila: nasce a candidatura e a primeira etapa (currículo). Idempotente pela inbox e pelo índice único.</summary>
internal sealed class JobShortlistedConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => JobShortlistedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var job = message.Read<JobShortlistedV1>();
        if (!await db.Applications.AnyAsync(a => a.CampaignId == job.CampaignId && a.CanonicalKey == job.CanonicalKey, ct))
        {
            var app = JobApplication.Create(job, now);
            db.Applications.Add(app);
            db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Materials, now, now));
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
