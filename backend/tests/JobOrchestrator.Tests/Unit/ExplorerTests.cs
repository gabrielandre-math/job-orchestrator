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
    [InlineData("https://olist.inhire.app/vagas/2e22/analista", "inhire:olist")]
    [InlineData("https://solides.vagas.solides.com.br/vaga/123", "solides:solides")]
    [InlineData("https://jobs.quickin.io/igma/jobs/6a98", "quickin:igma")]
    [InlineData("https://manpowergroup.pandape.infojobs.com.br/Detail/3901", "pandape:manpowergroup")]
    [InlineData("https://apply.workable.com/dextra/j/9D0C", "workable:dextra")]
    [InlineData("https://apply.workable.com/j/9D0C64C055", null)]
    [InlineData("https://combineglobalrecruitment.na.teamtailor.com/jobs/1-dev", "teamtailor:combineglobalrecruitment.na")]
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

    [Theory]
    [InlineData("Desenvolvedor(a) .NET Sênior | Pagamentos", "desenvolvedor-a-net-senior-pagamentos")]
    [InlineData("***", "vaga")]
    public void Endereco_da_vaga_da_inhire_tem_o_titulo(string title, string slug) => Assert.Equal(slug, InhireSource.Slug(title));

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

/// <summary>O Conferente recusa o que está escrito na página mas não é vaga: rótulo com contagem, chamada para a lista, filtro de local.</summary>
public sealed class PageJobTests
{
    [Theory]
    [InlineData("Business Development", "Select location", "Business Development (1)", true)]
    [InlineData("To explore open roles, visit the Job Search", "", "To explore open roles", true)]
    [InlineData("Engineering (12)", "", "", true)]
    [InlineData("Ver todas as vagas", "", "", true)]
    [InlineData("Desenvolvedor Backend .NET Sênior", "Rio de Janeiro", "C# e AWS, time de pagamentos.", false)]
    [InlineData("Senior Software Engineer, Payments", "Remote", "Build the ledger in C#.", false)]
    public void Rotulo_de_pagina_nao_vira_vaga(string title, string location, string summary, bool rejected) =>
        Assert.Equal(rejected, JobOrchestrator.Automation.Adapters.ScrapyRunner.NotAJob(title, location, summary));

    [Fact]
    public void Dado_estruturado_que_a_pagina_monta_com_javascript_vira_vaga()
    {
        const string json = """
            {"@context":"https://schema.org","@graph":[{"@type":"Organization","name":"Acme"},
             {"@type":"JobPosting","title":"Desenvolvedor .NET Pleno","hiringOrganization":{"@type":"Organization","name":"Acme Tecnologia"},
              "jobLocation":{"@type":"Place","address":{"addressLocality":"Rio de Janeiro","addressRegion":"RJ","addressCountry":"BR"}},
              "jobLocationType":"TELECOMMUTE","datePosted":"2026-10-30","employmentType":["FULL_TIME"],"description":"<p>C# e ASP.NET Core</p>","url":"https://acme.com.br/vagas/dotnet"}]}
            """;
        var p = Assert.Single(JobOrchestrator.Automation.Adapters.PageReader.JobPostings("web:acme.com.br", "https://acme.com.br/vagas", json));
        Assert.Equal("Acme Tecnologia", p.Company);
        Assert.Equal("Rio de Janeiro, RJ, BR", p.Location);
        Assert.Equal("remote", p.Workplace);
        Assert.Equal("https://acme.com.br/vagas/dotnet", p.ApplyUrl);
        Assert.Contains("ASP.NET Core", p.Description);
    }
}
