namespace JobOrchestrator.Jobs.Features;

/// <summary>
/// Leaving "só buscar" does not bring back what was already found: this sends the open jobs of the queue to Applications again,
/// which creates only the ones without an application (idempotent by job). Every enabled campaign that prepares takes part; the
/// first in the list could be a campaign that is off. Returns how many went.
/// </summary>
internal sealed class PrepareFoundEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/api/jobs/prepare-found", async (JobsDbContext db, ICampaignsApi campaigns, TimeProvider time, CancellationToken ct) =>
        {
            var all = await campaigns.ListAsync(ct);
            if (all.Count == 0) return Results.Problem("Crie uma campanha antes.", statusCode: 409);
            var preparing = all.Where(c => c.Enabled && c.Mode != SubmissionMode.DiscoveryOnly).ToList();
            if (preparing.Count == 0) return Results.Problem("A campanha está em só buscar: mude o modo para preparar as vagas.", statusCode: 409);
            var queued = 0;
            foreach (var campaign in preparing) queued += await PrepareFound.QueueAsync(db, campaign.Id, time.GetUtcNow().UtcDateTime, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { queued });
        }).WithTags("Jobs");
}

internal static class PrepareFound
{
    /// <summary>The open jobs in the campaign's queue go to Applications again; it creates only the applications that are missing.</summary>
    public static async Task<int> QueueAsync(JobsDbContext db, Guid campaignId, DateTime now, CancellationToken ct)
    {
        var queue = await db.Postings.Where(p => p.CampaignId == campaignId && p.Open && p.Status == JobStatus.Shortlisted).ToListAsync(ct);
        foreach (var p in queue)
            db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(p.Id, campaignId, p.CanonicalKey, p.Company, p.Title, p.Location, p.ApplyUrl, p.Score ?? 0, p.ApplyChannel), now);
        return queue.Count;
    }
}

/// <summary>The campaign left "só buscar": the jobs it found meanwhile get their applications without anyone pressing a button.</summary>
internal sealed class PreparationStartedConsumer(JobsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => PreparationStartedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        await PrepareFound.QueueAsync(db, message.Read<PreparationStartedV1>().CampaignId, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
