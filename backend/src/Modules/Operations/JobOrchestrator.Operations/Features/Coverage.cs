using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Campaigns.Contracts;
using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Operations.Features;

internal sealed record CountryCoverage(string Iso2, string Iso3, string Name, string Continent, string Status, string Research, int Portals, int ApiCandidates, int Wapes,
    string[] RecordIds, string[] Platforms, string[] PendingPlatforms, int Open, int BrazilOk, int BrazilPossible, int Restricted);
internal sealed record ContinentCoverage(string Code, string Name, int Countries, int Integrated, int Pending, int JobsSeen, int Seeds, int Unresearched, int Open);
internal sealed record CoverageTotals(int Configured, int WithConnector, int Authorized, int Working, int CompanyBoards, int CompaniesWithOpenJobs, int OpenUnique, int OpenWithDate,
    int BrazilConfirmed, int BrazilPossible, int BrazilRestricted, int Review, int Unknown, int CountriesIntegrated, int CountriesWithJobs, int CountriesUnresearched, int CatalogRecords);
internal sealed record CoverageView(DateTime At, CoverageTotals Totals, IReadOnlyList<ContinentCoverage> Continents, IReadOnlyList<CountryCoverage> Countries,
    string[] GlobalPlatforms, int UnmatchedOpen);

/// <summary>
/// Cobertura dos 249 países e territórios (especificação V2.2), sem inventar: o estado de cada país sai do que existe de verdade.
/// INTEGRATED (plataforma do próprio país com conector, portão aberto e sondagem com vaga), CONNECTOR_PENDING (fonte do país no registro sem
/// conector ou sem sondagem boa), JOBS_SEEN (vaga aberta com local nele vinda de fonte global), e senão o estado da auditoria do usuário
/// (SOME_SEEDS_NO_LIVE_TEST ou UNRESEARCHED). Os totais vêm separados, como a V2 pede, e nunca somam o catálogo como se fosse fonte.
/// </summary>
internal static class Coverage
{
    private static readonly string[] Regions = ["GLOBAL", "LATAM", "EUROPE", "WORLDWIDE"];

    public static async Task<CoverageView> BuildAsync(IAutomationApi automation, IJobsApi jobs, ICampaignsApi campaigns, TimeProvider time, CancellationToken ct)
    {
        var registry = await automation.RegistryAsync(ct);
        var locations = await jobs.OpenLocationsAsync(ct);
        var summary = await jobs.OpenSummaryAsync(ct);
        var sources = (await campaigns.ListAsync(ct)).SelectMany(c => c.Sources).Distinct().ToList();

        var perCountry = new Dictionary<string, (int Open, int Ok, int Possible, int Restricted)>();
        var unmatched = 0;
        foreach (var l in locations)
        {
            var found = Match(l.Location);
            if (found.Count == 0) { unmatched += l.Open; continue; }
            foreach (var iso in found)
            {
                var c = perCountry.GetValueOrDefault(iso);
                perCountry[iso] = (c.Open + l.Open, c.Ok + l.BrazilOk, c.Possible + l.BrazilPossible, c.Restricted + l.Restricted);
            }
        }

        var countries = SourceCatalog.Countries.Select(a =>
        {
            var specific = registry.Where(r => !r.Countries.Any(Regions.Contains) && r.Countries.Contains(a.Iso2)).ToList();
            var working = specific.Where(r => r.Connector && r.GateOpen && r.ProbeOk == true).Select(r => r.Prefix).ToArray();
            var pending = specific.Where(r => !working.Contains(r.Prefix) && r.Lifecycle is not ("BLOCKED" or "PARTNER_ONLY")).Select(r => r.Prefix).ToArray();
            var j = perCountry.GetValueOrDefault(a.Iso2);
            var status = working.Length > 0 ? "INTEGRATED" : pending.Length > 0 ? "CONNECTOR_PENDING" : j.Open > 0 ? "JOBS_SEEN" : a.Research;
            return new CountryCoverage(a.Iso2, a.Iso3, a.Name, a.Continent, status, a.Research, a.Portals, a.ApiCandidates, a.Wapes, a.RecordIds, working, pending,
                j.Open, j.Ok, j.Possible, j.Restricted);
        }).ToList();

        var continents = countries.GroupBy(c => c.Continent).Select(g => new ContinentCoverage(g.Key, SourceCatalog.ContinentNames.GetValueOrDefault(g.Key, g.Key), g.Count(),
            g.Count(c => c.Status == "INTEGRATED"), g.Count(c => c.Status == "CONNECTOR_PENDING"), g.Count(c => c.Status == "JOBS_SEEN"),
            g.Count(c => c.Status == "SOME_SEEDS_NO_LIVE_TEST"), g.Count(c => c.Status == "UNRESEARCHED"), g.Sum(c => c.Open)))
            .OrderByDescending(c => c.Integrated).ThenByDescending(c => c.Open).ToList();

        var totals = new CoverageTotals(registry.Count, registry.Count(r => r.Connector), registry.Count(r => r.GateOpen), registry.Count(r => r.ProbeOk == true),
            sources.Count(s => SourceRegistry.Find(s) is { Kind: "ats" }), summary.Companies, summary.Open, summary.WithDate, summary.BrazilOk, summary.BrazilPossible, summary.Restricted,
            summary.Review, summary.Unknown, countries.Count(c => c.Status == "INTEGRATED"), countries.Count(c => c.Open > 0), countries.Count(c => c.Status == "UNRESEARCHED"),
            SourceCatalog.Records.Count());
        var global = registry.Where(r => r.Countries.Any(Regions.Contains) && r.Connector && r.GateOpen).Select(r => r.Prefix).ToArray();
        return new CoverageView(time.GetUtcNow().UtcDateTime, totals, continents, countries, global, unmatched);
    }

    /// <summary>Os países que um texto de local cita (o casamento mora no contrato comum, o mesmo das vagas).</summary>
    internal static IReadOnlyList<string> Match(string location) => SourceCatalog.MatchCountries(location);

}

/// <summary>GET /api/coverage: a cobertura por país e continente, com os totais separados.</summary>
internal sealed class CoverageEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/coverage", async (IAutomationApi automation, IJobsApi jobs, ICampaignsApi campaigns, TimeProvider time, CancellationToken ct) =>
            Results.Ok(await Coverage.BuildAsync(automation, jobs, campaigns, time, ct))).WithTags("Operations");
}
