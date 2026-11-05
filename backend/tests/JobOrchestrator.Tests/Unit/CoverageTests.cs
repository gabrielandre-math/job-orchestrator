using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Operations.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Cobertura dos 249 países (V2.2): a base existe inteira, sem país inventado, e o local da vaga vira país sem confundir nomes.</summary>
public sealed class CoverageTests
{
    [Fact]
    public void Os_249_paises_existem_com_continente_e_sem_registro_inventado()
    {
        var all = SourceCatalog.Countries;
        Assert.Equal(249, all.Count);
        Assert.Equal(249, all.Select(c => c.Iso2).Distinct().Count());
        Assert.All(all, c => Assert.NotEqual("", c.Continent));
        var ids = SourceCatalog.Records.Select(r => r.GetProperty("id").GetString()).ToHashSet();
        Assert.All(all.SelectMany(c => c.RecordIds), id => Assert.Contains(id, ids));
        // País sem pesquisa continua UNRESEARCHED e sem registro: nada de URL inventada.
        Assert.All(all.Where(c => c.Research == "UNRESEARCHED"), c => Assert.Empty(c.RecordIds));
    }

    [Theory]
    [InlineData("São Paulo, Brazil", "BR")]
    [InlineData("Remote (US only)", "US")]
    [InlineData("New Mexico, USA", "US")]
    [InlineData("Port Moresby, Papua New Guinea", "PG")]
    [InlineData("Stockholm", "SE")]
    [InlineData("Atlanta, Georgia", "US")]
    [InlineData("Seoul, South Korea", "KR")]
    [InlineData("Kinshasa, Democratic Republic of the Congo", "CD")]
    public void Local_vira_o_pais_certo(string location, string iso) => Assert.Equal([iso], Coverage.Match(location));

    [Fact]
    public void Local_com_dois_paises_e_sem_pais()
    {
        Assert.Equal(["DE", "PT"], Coverage.Match("Berlin, Germany or Lisbon, Portugal"));
        Assert.Empty(Coverage.Match("Remote"));
        Assert.Empty(Coverage.Match("Anywhere in the world"));
        Assert.Empty(Coverage.Match(""));
    }

    [Fact]
    public void Escopo_regional_expande_sem_inventar()
    {
        Assert.Equal(249, SourceCatalog.Expand("GLOBAL").Count);
        Assert.Contains("BR", SourceCatalog.Expand("LATAM"));
        Assert.DoesNotContain("US", SourceCatalog.Expand("LATAM"));
        Assert.Equal(["GB"], SourceCatalog.Expand("UK"));
        Assert.Empty(SourceCatalog.Expand("MARTE"));
    }
}
