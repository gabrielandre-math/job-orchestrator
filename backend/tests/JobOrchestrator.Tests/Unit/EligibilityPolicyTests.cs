using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Jobs.Domain;
using JobOrchestrator.Jobs.Features;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Elegibilidade no ranking (V2): restrita sai por padrão, presencial fora só com mudança aceita, e a lista de países da fonte é prova declarada.</summary>
public sealed class EligibilityPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Evaluation Good = new(92, 0.9, [], null);

    private static JobPosting Posting(string location, string description, IReadOnlyList<string>? allowed = null) =>
        JobPosting.Discover(Guid.NewGuid(), new DiscoveredPosting("test:x", Guid.NewGuid().ToString(), "Empresa", "Backend Engineer", location, "https://example.com/job",
            description, Now, "{}", AllowedCountries: allowed), Now);

    [Fact]
    public void Lista_de_paises_da_fonte_vale_como_prova_declarada()
    {
        var us = Eligibility.Classify("Backend Engineer", "Remote role.", "Remote", "Remoto", ["US"]);
        Assert.Equal("BR_RESTRICTED", us.Class);
        Assert.True(us.Declared);
        Assert.Contains("US", us.Evidence);
        Assert.Equal("BR_ELIGIBLE_CONFIRMED", Eligibility.Classify("Backend Engineer", "Remote role.", "Remote", "Remoto", ["AR", "BR"]).Class);
        Assert.Equal("BR_RESTRICTED", Posting("Remote", "Remote role.", ["US", "CA"]).Eligibility);
    }

    [Fact]
    public void Restrita_sai_da_fila_por_padrao_mas_nao_na_vaga_salva_a_mao()
    {
        var restricted = Posting("Remote (US only)", "Fully remote, US only.");
        Assert.Equal("BR_RESTRICTED", restricted.Eligibility);
        Assert.False(restricted.Evaluate(Good, 80));
        Assert.Equal(JobStatus.Rejected, restricted.Status);
        Assert.StartsWith("Restrita", restricted.Reason);

        var saved = Posting("Remote (US only)", "Fully remote, US only.");
        Assert.True(saved.Evaluate(Good, 80, EligibilityPolicy.Manual));
        Assert.Equal(JobStatus.Shortlisted, saved.Status);
    }

    [Fact]
    public void Presencial_fora_so_entra_quando_o_perfil_aceita_mudanca()
    {
        const string onsite = "On-site in Berlin. We offer relocation support and visa sponsorship.";
        var no = Posting("Berlin, Germany", onsite);
        Assert.Equal("RELOCATION_WITH_SPONSORSHIP", no.Eligibility);
        Assert.False(no.Evaluate(Good, 80, new EligibilityPolicy(true, false)));
        Assert.StartsWith("Presencial fora do Brasil", no.Reason);
        Assert.True(Posting("Berlin, Germany", onsite).Evaluate(Good, 80, new EligibilityPolicy(true, true)));
        // Confirmada (no Brasil) entra com qualquer política.
        Assert.True(Posting("São Paulo, Brazil", "Hybrid in São Paulo.").Evaluate(Good, 80));
    }

    [Fact]
    public void Politica_sai_da_confirmacao_do_perfil()
    {
        static ProfileSnapshot Profile(string? location, string[] authorized, bool relocate) => new(Guid.NewGuid(), 1, "h", false, true,
            new Identity("Ana Teste", null, null, location, null, null, null), [], Confirmations.Empty with { AuthorizedCountries = authorized, Relocate = relocate });
        Assert.Equal(EligibilityPolicy.Default, RunRequestedConsumer.Policy(null));
        Assert.Equal(new EligibilityPolicy(true, false), RunRequestedConsumer.Policy(Profile("Rio de Janeiro, Brasil", [], false)));
        Assert.Equal(new EligibilityPolicy(true, true), RunRequestedConsumer.Policy(Profile(null, ["BR"], true)));
        Assert.Equal(new EligibilityPolicy(false, false), RunRequestedConsumer.Policy(Profile("Lisboa, Portugal", ["PT"], false)));
    }

    [Fact]
    public void Hibrido_com_a_cidade_no_titulo_ou_no_anuncio_e_no_Brasil()
    {
        Assert.Equal("BR_ELIGIBLE_CONFIRMED", Eligibility.Classify("[Híbrido] Desenvolvedor - Rio de Janeiro - RJ", "Programa AI Stack.", "Híbrido", "Híbrido").Class);
        Assert.Equal("BR_ELIGIBLE_CONFIRMED", Eligibility.Classify("Back-end developer", "## Local\n\nPresencial em SP, na Faria Lima.", "Presencial", "Presencial").Class);
        Assert.Equal("RELOCATION_UNVERIFIED", Eligibility.Classify("Backend Engineer", "Onsite in our Berlin office.", "Berlin, Germany", "Presencial").Class);
    }
}
