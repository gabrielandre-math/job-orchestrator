using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Campaigns.Features;

/// <summary>Board descoberto por um link salvo: a campanha passa a vigiar e roda na hora. Idempotente pela inbox.</summary>
internal sealed class SourceDiscoveredConsumer(CampaignsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => SourceDiscoveredV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var e = message.Read<SourceDiscoveredV1>();
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == e.CampaignId, ct);
        if (campaign is not null)
        {
            campaign.Watch(e.Source, now);
            var run = campaign.RequestRun("saved-link", now);
            db.Runs.Add(run);
            db.Publish(RunRequestedV1.EventType, new RunRequestedV1(run.Id, campaign.Id, now), now);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
