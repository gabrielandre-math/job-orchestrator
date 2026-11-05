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
