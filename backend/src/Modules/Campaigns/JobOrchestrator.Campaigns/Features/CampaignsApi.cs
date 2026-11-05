namespace JobOrchestrator.Campaigns.Features;

/// <summary>O que o módulo mostra aos outros. Leitura com Dapper.</summary>
internal sealed class CampaignsApi(IReadDb<CampaignsDbContext> db) : ICampaignsApi
{
    public async Task<CampaignSettings?> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(c => c.Id == id);

    public async Task<IReadOnlyList<CampaignSettings>> ListAsync(CancellationToken ct)
    {
        var active = (await db.ListAsync<AuthorizationRow>(
            "select id, campaign_id, granted_at, expires_at, max_per_day, min_score, adapters from campaigns.authorizations where revoked_at is null and expires_at > now()", null, ct))
            .GroupBy(a => a.CampaignId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.GrantedAt).First());
        return (await db.ListAsync<CampaignView>(ListCampaignsHandler.Sql, null, ct))
            .Select(c => new CampaignSettings(c.Id, c.Name, c.Enabled, Enum.Parse<SubmissionMode>(c.Mode), c.MinFitScore, c.MaxSubmissionsPerDay, c.Technologies, c.Locations, c.Sources, c.TimeZone,
                active.TryGetValue(c.Id, out var a) ? new SubmitAuthorizationView(a.Id, a.GrantedAt, a.ExpiresAt, a.MaxPerDay, a.MinScore, a.Adapters) : null, c.Titles))
            .ToList();
    }
}

internal sealed record AuthorizationRow
{
    public Guid Id { get; init; }
    public Guid CampaignId { get; init; }
    public DateTime GrantedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int MaxPerDay { get; init; }
    public int MinScore { get; init; }
    public string[] Adapters { get; init; } = [];
}

/// <summary>Rodada pedida pelo timer da noite: mesmo caminho do botão (registro da rodada e evento no outbox, numa transação).</summary>
internal sealed class CampaignRuns(CampaignsDbContext db, TimeProvider time) : ICampaignRuns
{
    public async Task<int> RequestEnabledAsync(string trigger, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var campaigns = await db.Campaigns.Where(c => c.Enabled).ToListAsync(ct);
        var count = 0;
        foreach (var campaign in campaigns.Where(c => c.Sources.Count > 0))
        {
            var run = campaign.RequestRun(trigger, now);
            db.Runs.Add(run);
            db.Publish(RunRequestedV1.EventType, new RunRequestedV1(run.Id, campaign.Id, now), now);
            count++;
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return count;
    }

    public async Task<int> RequestDueAsync(TimeSpan every, string trigger, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var limit = now - every;
        var due = await db.Campaigns.Where(c => c.Enabled && c.Sources.Count > 0)
            // A rodada parcial do Explorador (só as fontes novas) não conta: a completa segue no horário dela.
            .Where(c => !db.Runs.Any(r => r.CampaignId == c.Id && r.RequestedAt > limit && r.Trigger != "explorer")).ToListAsync(ct);
        if (due.Count == 0) return 0;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        foreach (var campaign in due)
        {
            var run = campaign.RequestRun(trigger, now);
            db.Runs.Add(run);
            db.Publish(RunRequestedV1.EventType, new RunRequestedV1(run.Id, campaign.Id, now), now);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return due.Count;
    }

    public async Task<int> AddSourcesAsync(Guid campaignId, IReadOnlyList<string> sources, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null) return 0;
        var added = campaign.AddSources(sources, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<bool> RequestSourcesAsync(Guid campaignId, IReadOnlyList<string> sources, string trigger, CancellationToken ct)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == campaignId, ct);
        var only = sources.Where(s => campaign?.Sources.Contains(s, StringComparer.OrdinalIgnoreCase) == true).ToList();
        if (campaign is null || only.Count == 0) return false;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var run = campaign.RequestRun(trigger, now);
        db.Runs.Add(run);
        db.Publish(RunRequestedV1.EventType, new RunRequestedV1(run.Id, campaign.Id, now, only), now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}
