using System.Security.Cryptography;
using System.Text;

namespace JobOrchestrator.Jobs.Features;

/// <summary>
/// Consumidor da rodada (agente Batedor + Normalizador + Juiz + Analista): busca em cada fonte, confere abertura,
/// deduplica pela identidade canônica, avalia e põe na fila o que passou. A rede vem antes da transação; a gravação é atômica.
/// </summary>
internal sealed class RunRequestedConsumer(
    JobsDbContext db, ICampaignsApi campaigns, IAutomationApi automation, TimeProvider time, ILogger<RunRequestedConsumer> log) : IEventConsumer
{
    public string EventType => RunRequestedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        var run = message.Read<RunRequestedV1>();
        var campaign = await campaigns.GetAsync(run.CampaignId, ct);
        if (campaign is null) return;

        var found = new List<(DiscoveredPosting Posting, PostingStatus Status)>();
        foreach (var source in campaign.Sources)
        {
            try
            {
                foreach (var p in await automation.DiscoverAsync(source, ct))
                    found.Add((p, await automation.CheckOpenAsync(p.ApplyUrl, ct)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                // Uma fonte fora do ar não derruba a rodada.
                log.LogWarning("Source {Source} failed: {Error}", source, ex.Message);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var shortlisted = 0;
        foreach (var (p, status) in found)
        {
            var posting = await db.Postings.FirstOrDefaultAsync(x => x.CampaignId == campaign.Id && x.Source == p.Source && x.ExternalId == p.ExternalId, ct);
            if (posting is null)
            {
                posting = JobPosting.Discover(campaign.Id, p, now);
                var original = await db.Postings.Where(x => x.CampaignId == campaign.Id && x.CanonicalKey == posting.CanonicalKey).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                    ?? db.Postings.Local.FirstOrDefault(x => x.CampaignId == campaign.Id && x.CanonicalKey == posting.CanonicalKey)?.Id;
                if (original is not null) posting.MarkDuplicate(original.Value);
                db.Postings.Add(posting);
            }
            else posting.SeenAgain(p, now);

            await SnapshotAsync(posting.Id, p.RawJson, now, ct);
            posting.Verified(status, now);
            if (posting.Evaluate(Scoring.Evaluate(campaign, posting.Title, posting.Description, posting.Location), campaign.MinFitScore))
            {
                shortlisted++;
                db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(posting.Id, campaign.Id, posting.CanonicalKey, posting.Company, posting.Title, posting.Location, posting.ApplyUrl, posting.Score!.Value), now);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        log.LogInformation("Run {Run}: {Found} postings, {Shortlisted} shortlisted", run.RunId, found.Count, shortlisted);
    }

    private async Task SnapshotAsync(Guid jobId, string raw, DateTime now, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        if (await db.Snapshots.AnyAsync(s => s.JobId == jobId && s.PayloadHash == hash, ct) || db.Snapshots.Local.Any(s => s.JobId == jobId && s.PayloadHash == hash)) return;
        db.Snapshots.Add(new JobSnapshot { Id = Guid.CreateVersion7(), JobId = jobId, FetchedAt = now, PayloadHash = hash, Payload = raw });
    }
}
