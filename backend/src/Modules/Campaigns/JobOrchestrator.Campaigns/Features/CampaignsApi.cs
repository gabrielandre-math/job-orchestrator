namespace JobOrchestrator.Campaigns.Features;

/// <summary>O que o módulo mostra aos outros. Leitura com Dapper.</summary>
internal sealed class CampaignsApi(IReadDb<CampaignsDbContext> db) : ICampaignsApi
{
    public async Task<CampaignSettings?> GetAsync(Guid id, CancellationToken ct) =>
        (await ListAsync(ct)).FirstOrDefault(c => c.Id == id);

    public async Task<IReadOnlyList<CampaignSettings>> ListAsync(CancellationToken ct) =>
        (await db.ListAsync<CampaignView>(ListCampaignsHandler.Sql, null, ct))
            .Select(c => new CampaignSettings(c.Id, c.Name, c.Enabled, Enum.Parse<SubmissionMode>(c.Mode), c.MinFitScore, c.MaxSubmissionsPerDay, c.Technologies, c.Locations, c.Sources, c.TimeZone))
            .ToList();
}
