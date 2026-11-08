using JobOrchestrator.Campaigns.Contracts;
using JobOrchestrator.Operations.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Caçador com o Llama: o plano do modelo é limpo e somado ao da campanha, e as páginas do Common Crawl e os sites viram pistas certas.</summary>
public sealed class HunterTests
{
    [Theory]
    [InlineData("C#", "csharp")]
    [InlineData(".NET", "dotnet")]
    [InlineData("ASP.NET Core", "aspnet")]
    [InlineData("Node.js", "node")]
    [InlineData("Engenharia de Dados", "engenharia-de-dados")]
    [InlineData("Ação Social", "acao-social")]
    public void Palavra_vira_pedaco_de_endereco(string term, string slug) => Assert.Equal(slug, Hunter.Slug(term));

    [Fact]
    public void Plano_do_modelo_e_limpo_e_somado_ao_da_campanha()
    {
        var basic = new HuntPlan(["Backend Engineer .NET"], ["csharp", "dotnet"], ["BR"], ["software"], [], ["Backend Engineer .NET remote"]);
        var model = Hunter.Parse("""
            {"roles":["Backend Developer","Desenvolvedor Back-end","Desarrollador Backend"],"slugs":["Back End!","dotnet","golang","x"],
             "countries":["pt","DE","XX","Brasil"],"industries":["fintech"],"companies":["Nubank","  "],"queries":["dotnet remote jobs"]}
            """);
        var plan = Hunter.Merge(model, basic);
        Assert.Equal(["csharp", "dotnet", "back-end", "golang"], plan.Slugs);
        Assert.Equal(["BR", "PT", "DE"], plan.Countries);
        Assert.Contains("Desenvolvedor Back-end", plan.Roles);
        Assert.Contains("Backend Engineer .NET", plan.Roles);
        Assert.Equal(["fintech", "software"], plan.Industries);
        Assert.Equal(["Nubank"], plan.Companies);
    }

    [Fact]
    public void Sem_modelo_o_plano_sai_da_campanha()
    {
        var c = new CampaignSettings(Guid.NewGuid(), "Backend .NET", true, default, 80, 10, ["C#", ".NET", "AWS"], ["Remoto"], [], "America/Sao_Paulo", ["Backend Software Engineer .NET"]);
        var plan = Hunter.Fallback(c, null);
        Assert.Contains("csharp", plan.Slugs);
        Assert.Contains("dotnet", plan.Slugs);
        Assert.Contains("backend", plan.Slugs);
        Assert.DoesNotContain("engineer", plan.Slugs);
        Assert.DoesNotContain("core", plan.Slugs);
        Assert.DoesNotContain("aws", plan.Slugs);
        Assert.Contains("BR", plan.Countries);
        Assert.Contains("PT", plan.Countries);
    }

    [Fact]
    public void Pagina_de_vaga_do_indice_vira_um_quadro_por_empresa()
    {
        var body = """
            {"url": "https://anchr.recruitee.com/o/junior-backend-developer"}
            {"url": "https://hardrockdigital.recruitee.com/o/principal-backend-engineer-casino"}
            {"url": "https://hardrockdigital.recruitee.com/o/principal-backend-engineer-core"}
            {"url": "https://boards.greenhouse.io/acme/jobs/123?gh_src=x"}
            {"url": "https://exemplo.com/blog/backend"}
            {"url": "https://cortad
            """;
        var pages = Hunter.Pages(body);
        Assert.Equal(5, pages.Count);
        var leads = Hunter.FromPages(pages, ["dotnet", "backend"], "recruitee.com").ToList();
        Assert.Equal(["recruitee:anchr", "recruitee:hardrockdigital", "greenhouse:acme"], leads.Select(l => l.Board));
        Assert.Contains("backend em recruitee.com", leads[0].Origin);
        Assert.StartsWith("achado", leads[0].Origin);
    }

    [Theory]
    [InlineData("https://www.caixamagica.pt", "web:caixamagica.pt")]
    [InlineData("https://jobs.lever.co/spotify", "lever:spotify")]
    [InlineData("ftp://x.org", null)]
    public void Site_oficial_vira_varredura_ou_quadro(string url, string? lead) => Assert.Equal(lead, Hunter.Site(url));
}
