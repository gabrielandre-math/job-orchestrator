namespace JobOrchestrator.Jobs.Features;

/// <summary>
/// Leaving "só buscar" does not bring back what was already found: this sends the open jobs of the queue to Applications again,
/// which creates only the ones without an application (idempotent by job). Returns how many went.
/// </summary>
internal sealed class PrepareFoundEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapPost("/api/jobs/prepare-found", async (JobsDbContext db, ICampaignsApi campaigns, TimeProvider time, CancellationToken ct) =>
        {
            var campaign = (await campaigns.ListAsync(ct)).FirstOrDefault();
            if (campaign is null) return Results.Problem("Crie uma campanha antes.", statusCode: 409);
            if (campaign.Mode == SubmissionMode.DiscoveryOnly) return Results.Problem("A campanha está em só buscar: mude o modo para preparar as vagas.", statusCode: 409);
            var now = time.GetUtcNow().UtcDateTime;
            var queue = await db.Postings.Where(p => p.CampaignId == campaign.Id && p.Open && p.Status == JobStatus.Shortlisted).ToListAsync(ct);
            foreach (var p in queue)
                db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(p.Id, campaign.Id, p.CanonicalKey, p.Company, p.Title, p.Location, p.ApplyUrl, p.Score ?? 0, p.ApplyChannel), now);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { queued = queue.Count });
        }).WithTags("Jobs");
}
