using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Features;

/// <summary>A porta do módulo para os outros: fontes, formulários pelo roteador, PDF e o registro de adapters.</summary>
internal sealed class AutomationApi(
    IEnumerable<ISourceConnector> sources, IEnumerable<IFormAdapter> forms, Router router, BrowserPool browsers,
    IReadDb<AutomationDbContext> read, CareerPageReader careers, Leads leads) : IAutomationApi
{
    public Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) =>
        (sources.FirstOrDefault(s => s.Accepts(source)) ?? throw new InvalidOperationException($"Fonte desconhecida: {source}")).DiscoverAsync(source, ct);

    public Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var connector = sources.FirstOrDefault(s => s.Accepts(source)) ?? throw new InvalidOperationException($"Fonte desconhecida: {source}");
        return connector is IHintedSource hinted && hints.Count > 0 ? hinted.DiscoverAsync(source, hints, ct) : connector.DiscoverAsync(source, ct);
    }

    public Task<IReadOnlyList<string>> SearchGupyAsync(string term, CancellationToken ct) =>
        sources.OfType<GupySource>().FirstOrDefault()?.SearchAsync(term, ct) ?? Task.FromResult<IReadOnlyList<string>>([]);

    public Task<IReadOnlyList<SourceLead>> JobReposAsync(string query, CancellationToken ct) => leads.JobReposAsync(query, ct);
    public Task<IReadOnlyList<SourceLead>> CompanyListAsync(string repo, CancellationToken ct) => leads.CompanyListAsync(repo, ct);
    public Task<IReadOnlyList<SiteFinding>> SweepSitesAsync(IReadOnlyList<string> domains, CancellationToken ct) => leads.SweepSitesAsync(domains, ct);

    public Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct)
    {
        var url = new Uri(applyUrl);
        var source = sources.OrderByDescending(s => s.Fit(url)).FirstOrDefault(s => s.Fit(url) > 0);
        return source is null ? Task.FromResult(new PostingStatus(true, "Sem conferência para este domínio.")) : source.CheckOpenAsync(applyUrl, ct);
    }

    public Task<AutomationOutcome<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct) =>
        router.RunAsync(target.ApplicationId, Capability.PrepareForm, new Uri(target.ApplyUrl), null, a => a.PrepareAsync(target, materials, ct), ct);

    public Task<AutomationOutcome<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, string adapterId, CancellationToken ct) =>
        router.RunAsync(target.ApplicationId, Capability.SubmitApplication, new Uri(target.ApplyUrl), adapterId, a => a.SubmitAsync(target, materials, token, ct), ct);

    public Task<AutomationOutcome<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, string adapterId, CancellationToken ct) =>
        router.RunAsync(target.ApplicationId, Capability.ReconcileSubmission, new Uri(target.ApplyUrl), adapterId, a => a.ReconcileAsync(target, materials, ct), ct);

    /// <summary>HTML para PDF no Chromium, sem rede: o template não busca nada de fora.</summary>
    public async Task RenderPdfAsync(string html, string path, CancellationToken ct)
    {
        await using var lease = await browsers.LeaseAsync([], ct);
        var page = await lease.Context.NewPageAsync();
        await page.SetContentAsync(html);
        await page.PdfAsync(new PagePdfOptions { Path = path, Format = "A4", PrintBackground = true, Margin = new Margin { Top = "14mm", Bottom = "14mm", Left = "14mm", Right = "14mm" } });
    }

    public Task<(IReadOnlyList<string> Boards, string Detail)> FindBoardsAsync(string pageUrl, CancellationToken ct) => careers.FindBoardsAsync(new Uri(pageUrl), ct);

    public async Task<IReadOnlyList<AdapterView>> ListAdaptersAsync(CancellationToken ct)
    {
        var stats = await read.ListAsync<AdapterStat>("select * from automation.adapter_stats", null, ct);
        IEnumerable<IAdapter> all = [.. sources, .. forms];
        return all.Select(a =>
        {
            var mine = stats.Where(s => s.AdapterId == a.Id).ToList();
            var open = mine.Select(s => s.CircuitOpenUntil).Where(t => t > DateTime.UtcNow).Max();
            return new AdapterView(a.Id, a.Version, a.Name, a.Kind, [.. a.Capabilities.Select(c => c.ToString())], a.Domains,
                mine.Sum(s => s.Attempts), mine.Sum(s => s.Confirmed), mine.Sum(s => s.Failures), open is not null, open);
        }).ToList();
    }
}

/// <summary>Rotas HTTP (Carter) do registro de adapters.</summary>
internal sealed class AdapterEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/adapters", async (IAutomationApi api, CancellationToken ct) => Results.Ok(await api.ListAdaptersAsync(ct))).WithTags("Automation");
}
