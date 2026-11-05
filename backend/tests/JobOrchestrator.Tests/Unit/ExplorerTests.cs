using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Operations.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Explorador: link vira quadro oficial, lista de empresas vira pista sem rede social, vaga conta para o site certo e as buscas giram.</summary>
public sealed class ExplorerTests
{
    [Theory]
    [InlineData("https://boards.greenhouse.io/nubank/jobs/123", "greenhouse:nubank")]
    [InlineData("https://job-boards.greenhouse.io/embed/job_board?for=vtex", "greenhouse:vtex")]
    [InlineData("https://jobs.lever.co/dlocal/abc-123", "lever:dlocal")]
    [InlineData("https://jobs.ashbyhq.com/ramp/123", "ashby:ramp")]
    [InlineData("https://Sicredi.gupy.io/job/eyJ", "gupy:sicredi")]
    [InlineData("https://portal.gupy.io/job-search/term=.net", null)]
    [InlineData("https://acme.recruitee.com/o/dev", "recruitee:acme")]
    [InlineData("https://www.empresa.com.br/carreiras", null)]
    public void Link_vira_quadro_oficial(string url, string? expected) => Assert.Equal(expected, SourceKeys.Board(url));

    [Fact]
    public void Lista_de_empresas_vira_quadros_e_sites_sem_rede_social()
    {
        const string readme = """
            | Name | Website |
            | Afya | https://afya.gupy.io/ |
            | 203pixels | https://www.linkedin.com/company/203-pixels/ |
            | Accenture | https://www.accenture.com/br-pt/careers |
            | Abacomm | http://www.abacomm.com.br/ |
            - [Acme](https://jobs.lever.co/acme) | Remote
            [![Build](https://travis-ci.org/x/y.svg)](https://travis-ci.org/x/y)
            """;
        var leads = Leads.ParseList(readme, "dono/lista").Select(l => l.Source).ToList();
        Assert.Equal(["gupy:afya", "web:accenture.com/br-pt/careers", "web:abacomm.com.br", "lever:acme"], leads);
    }

    [Theory]
    [InlineData("https://carreiras.empresa.com.br/vaga/1", "empresa.com.br", true)]
    [InlineData("https://www.empresa.com.br/jobs", "empresa.com.br/carreiras", true)]
    [InlineData("https://outraempresa.com.br/jobs", "empresa.com.br", false)]
    [InlineData("https://empresa.com.br.golpe.io/", "empresa.com.br", false)]
    public void Vaga_conta_para_o_site_certo(string url, string site, bool expected) => Assert.Equal(expected, Leads.Under(url, site));

    [Fact]
    public void Buscas_giram_entre_os_ciclos()
    {
        string[] terms = ["C#", ".NET", "AWS", "React", "SQL"];
        Assert.Equal(["C#", ".NET"], Explorer.Rotate(terms, 0, 2));
        Assert.Equal(["AWS", "React"], Explorer.Rotate(terms, 1, 2));
        Assert.Empty(Explorer.Rotate([], 3, 2));
    }

    [Theory]
    [InlineData("gupy:sicredi", "gupy")]
    [InlineData("github:backend-br/vagas", "github")]
    [InlineData("web:empresa.com", "web")]
    [InlineData("greenhouse:vtex", "board")]
    [InlineData("getonbrd:.net", "board")]
    public void Tipo_da_pista(string source, string kind) => Assert.Equal(kind, Explorer.Kind(source));
}
