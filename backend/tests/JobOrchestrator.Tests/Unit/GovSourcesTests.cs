using System.Text.Json;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Jobs.Domain;
using JobOrchestrator.Jobs.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Fontes governamentais (V2): encerramento declarado pela fonte fecha a vaga sem apagar histórico, e o formato do DOL vira vaga honesta.</summary>
public sealed class GovSourcesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Anuncio_inativo_fecha_so_a_vaga_que_existe()
    {
        var open = JobPosting.Discover(Guid.NewGuid(), new DiscoveredPosting("nav:utvikler", "u1", "Kommune", "Utvikler", "Oslo, Norway", "https://example.com/1", "Utvikler.", Now, "{}", ListedAsOpen: true), Now);
        var other = JobPosting.Discover(Guid.NewGuid(), new DiscoveredPosting("nav:utvikler", "u2", "Kommune", "Utvikler", "Oslo, Norway", "https://example.com/2", "Utvikler.", Now, "{}", ListedAsOpen: true), Now);
        var dead = new DiscoveredPosting("nav:utvikler", "u1", "Kommune", "Utvikler", "Oslo, Norway", "https://arbeidsplassen.nav.no/stillinger/stilling/u1", "", null, "{}", Tombstone: true);
        Assert.Equal(1, RunRequestedConsumer.Close([open, other], [dead], Now.AddHours(1)));
        Assert.Equal(JobStatus.Expired, open.Status);
        Assert.False(open.Open);
        Assert.NotEqual(JobStatus.Expired, other.Status);
        // A mesma marcação de novo não conta duas vezes.
        Assert.Equal(0, RunRequestedConsumer.Close([open], [dead], Now.AddHours(2)));
    }

    [Fact]
    public void Ordem_H2A_do_DOL_vira_vaga_com_link_oficial_e_visto_temporario()
    {
        using var doc = JsonDocument.Parse("""
            {"caseNumber":"JO-A-300-1","jobTitle":"Farm Worker","socTitle":"Farmworkers","jobDuties":"Planting and harvesting.","empBusinessName":"Fazenda Teste LLP",
             "jobCity":"Loris","jobState":"SC","jobBeginDate":"15-Jan-2027","jobEndDate":"31-Oct-2027","dateSubmitted":"2026-11-04T04:40:06.880Z",
             "jobWageOffer":"16.37","jobWagePer":"Hour","recApplyEmail":"vagas@example.com","recApplyUrl":"N/A","jobMinexpmonths":"0"}
            """);
        var p = SeasonalJobsSource.Parse("seasonaljobs:farm", doc.RootElement);
        Assert.Equal("https://seasonaljobs.dol.gov/jobs/JO-A-300-1", p.ApplyUrl);
        Assert.Equal("vagas@example.com", p.ApplyEmail);
        Assert.Equal("Loris, SC, United States", p.Location);
        Assert.Contains("H-2A", p.Description);
        Assert.Equal("US$ 16.37/Hour", p.Compensation);
        var job = JobPosting.Discover(Guid.NewGuid(), p, Now);
        Assert.Equal("RELOCATION_WITH_SPONSORSHIP", job.Eligibility);
    }

    [Fact]
    public void JobTech_Links_aponta_para_o_anuncio_original_e_ignora_sem_link()
    {
        using var doc = JsonDocument.Parse("""
            [{"id":"a1","headline":"Developer","brief":"Backend role.","employer":{"name":"Empresa AB"},"publication_date":"2026-10-22T00:00:00",
              "workplace_addresses":[{"municipality":"Stockholm","region":"Stockholms län"}],"source_links":[{"label":"arbetsformedlingen.se","url":"https://arbetsformedlingen.se/platsbanken/annonser/1"}]},
             {"id":"a2","headline":"Sem link","source_links":[]}]
            """);
        var items = doc.RootElement.EnumerateArray().Select(h => JobLinksSource.Parse("joblinks:developer", h)).ToList();
        Assert.Null(items[1]);
        Assert.Equal("https://arbetsformedlingen.se/platsbanken/annonser/1", items[0]!.ApplyUrl);
        Assert.Equal("Stockholm, Stockholms län, Sweden", items[0]!.Location);
        Assert.Equal(["SE"], SourceCatalog.MatchCountries(items[0]!.Location));
    }

    [Fact]
    public void Termo_da_NAV_ignora_acento_e_caixa() => Assert.Equal("sjafor og montor", NavSource.Fold("Sjåfør og Montør"));
}
