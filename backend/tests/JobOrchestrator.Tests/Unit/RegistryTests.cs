using JobOrchestrator.Automation.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Registro de fontes (especificação V2): perfis coerentes e portão que impede coleta sem permissão.</summary>
public sealed class RegistryTests
{
    private static readonly DateTime Today = new(2026, 11, 5);

    [Fact]
    public void Perfis_sao_unicos_e_com_estado_conhecido()
    {
        Assert.Equal(SourceRegistry.All.Count, SourceRegistry.All.Select(p => p.Prefix).Distinct().Count());
        string[] lifecycles = ["ACTIVE", "EXPERIMENTAL", "CREDENTIALS_REQUIRED", "PARTNER_ONLY", "BLOCKED", "DISCOVERY_ONLY"];
        string[] access = ["OFFICIAL_PUBLIC", "OBSERVED", "CREDENTIALS_REQUIRED", "PARTNER_ONLY"];
        foreach (var p in SourceRegistry.All)
        {
            Assert.Contains(p.Lifecycle, lifecycles);
            Assert.Contains(p.Access, access);
            Assert.StartsWith("https://", p.DocsUrl);
            Assert.NotEmpty(p.Countries);
            if (p.Lifecycle == "CREDENTIALS_REQUIRED") Assert.NotNull(p.Credential);
        }
        Assert.All(SourceRegistry.Samples, s => Assert.StartsWith(s.Key + ":", s.Value));
        Assert.All(SourceRegistry.Samples.Keys, k => Assert.NotNull(SourceRegistry.Find(k + ":x")));
    }

    [Theory]
    [InlineData("linkedin:jobs", "BLOCKED_BY_POLICY")]
    [InlineData("francetravail:offres", "BLOCKED_BY_POLICY")]
    [InlineData("gupy-api:empresa", "PARTNER_ONLY")]
    public void Fonte_sem_permissao_nunca_passa_no_portao(string source, string reason) =>
        Assert.StartsWith(reason, SourceRegistry.Gate(source, [], true, Today));

    [Fact]
    public void Desligada_experimental_e_prazo_fecham_o_portao()
    {
        Assert.Null(SourceRegistry.Gate("greenhouse:gitlab", [], true, Today));
        Assert.StartsWith("DISABLED", SourceRegistry.Gate("greenhouse:gitlab", ["greenhouse"], true, Today));
        Assert.Null(SourceRegistry.Gate("personio:empresa", [], true, Today));
        Assert.StartsWith("EXPERIMENTAL_OFF", SourceRegistry.Gate("personio:empresa", [], false, Today));
        Assert.Null(SourceRegistry.Gate("recruitee:empresa", [], true, new DateTime(2027, 2, 9)));
        Assert.StartsWith("SUNSET", SourceRegistry.Gate("recruitee:empresa", [], true, new DateTime(2027, 2, 10)));
        // Fonte que o registro não conhece passa: quem decide é o conector.
        Assert.Null(SourceRegistry.Gate("qualquer:coisa", [], false, Today));
    }
}
