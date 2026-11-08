using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Rio Vagas packs the role, the kind of business, the pay and the place in one title; the company opens the text.</summary>
public sealed class RssTitlePartsTests
{
    [Fact]
    public void A_rio_vagas_title_comes_apart()
    {
        var (title, sector, place, pay) = RssSource.Parts("Auxiliar de Loja – Ótica Loja – R$ 1.700,00 – Copacabana – 2 vagas – Sem Experiência", ["RIOVAGAS", "Copacabana", "Rio de Janeiro"]);
        Assert.Equal(("Auxiliar de Loja", "Ótica Loja", "Copacabana", "R$ 1.700,00"), (title, sector, place, pay));
        Assert.Equal("Copacabana, Rio de Janeiro", RssSource.Region(place, ["RIOVAGAS", "Copacabana", "Rio de Janeiro"]));
        Assert.Equal("Empresa de Bobinas e Etiquetas", RssSource.Lead("Empresa de Bobinas e Etiquetas Cargo: Auxiliar de E-commerce Regime de Contratação: CLT"));
        Assert.Equal("Riovagas", RssSource.Site("RIOVAGAS: O MAIOR SITE DE EMPREGOS DO RIO!"));
    }

    [Fact]
    public void Without_pay_or_tags_the_last_part_is_not_taken_for_a_place()
    {
        var (title, sector, place, pay) = RssSource.Parts("Senior Engineer - Platform - Acme");
        Assert.Equal(("Senior Engineer", "Platform", (string?)null, (string?)null), (title, sector, place, pay));
        Assert.Equal(("Dev .NET", (string?)null, (string?)null, (string?)null), RssSource.Parts("Dev .NET - Remoto - PJ"));
        Assert.Equal("Desenvolvedor Backend", RssSource.Parts("Desenvolvedor Backend").Title);
    }
}
