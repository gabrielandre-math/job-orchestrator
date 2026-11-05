using System.Text.Json;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Jobs.Domain;
using JobOrchestrator.Jobs.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Fontes da fase 2 e normalização: formatos reais das APIs públicas, lidos sem chute.</summary>
public sealed class SourceTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Lever_le_cargo_local_modalidade_e_faixa()
    {
        var p = LeverSource.Parse("lever:acme-labs", "acme-labs", J("""
            {"id":"abc","text":"Senior Backend Engineer (.NET)","hostedUrl":"https://jobs.lever.co/acme-labs/abc","createdAt":1793000000000,
             "categories":{"location":"Remote, Brazil","commitment":"Full-time"},"workplaceType":"remote","descriptionPlain":"C# and .NET on AWS.",
             "lists":[{"text":"Requirements","content":"<li>5 years of C#</li>"}],"salaryRange":{"min":6000,"max":8000,"currency":"USD","interval":"per-month"}}
            """));
        Assert.Equal("Acme Labs", p.Company);
        Assert.Equal("remote", p.Workplace);
        Assert.Contains("5 years of C#", p.Description);
        Assert.Equal("6000 - 8000 USD per-month", p.Compensation);
    }

    [Fact]
    public void Ashby_usa_isRemote_quando_nao_ha_workplaceType()
    {
        var p = AshbySource.Parse("ashby:norte", "norte", J("""
            {"id":"1","title":"Software Engineer","location":"Brazil","isRemote":true,"jobUrl":"https://jobs.ashbyhq.com/norte/1","descriptionPlain":"C# .NET",
             "publishedAt":"2026-11-01T10:00:00Z","employmentType":"FullTime","compensation":{"compensationTierSummary":"R$ 15K – R$ 20K"}}
            """));
        Assert.Equal("remote", p.Workplace);
        Assert.Equal("R$ 15K – R$ 20K", p.Compensation);
    }

    [Fact]
    public void Issue_do_backend_br_vira_vaga_com_empresa_local_e_email()
    {
        var p = GitHubIssuesSource.Parse("github:backend-br/vagas", "backend-br/vagas", J("""
            {"number":42,"title":"[Remoto] Desenvolvedor Backend .NET Pleno na Empresa Ponte","html_url":"https://github.com/backend-br/vagas/issues/42",
             "body":"## Como se candidatar\nEnvie o currículo para vagas@ponte.com.br","labels":[{"name":"CLT"},{"name":"Pleno"},{"name":"Remoto"}],"updated_at":"2026-11-03T12:00:00Z"}
            """));
        Assert.Equal("Empresa Ponte", p.Company);
        Assert.Equal("Desenvolvedor Backend .NET Pleno", p.Title);
        Assert.Equal("vagas@ponte.com.br", p.ApplyEmail);
        Assert.Equal("CLT", p.Employment);
        Assert.Contains("Remoto", p.Location);
    }

    [Theory]
    [InlineData("Developer ASP.NET/C# Junior/Pleno - Dimensa", "Dimensa", "Developer ASP.NET/C# Junior/Pleno")]
    [InlineData("[Remoto] Back-end developer .NET (Strider)", "Strider", "Back-end developer .NET")]
    public void Issue_com_empresa_depois_de_traco_ou_entre_parenteses(string title, string company, string role)
    {
        var p = GitHubIssuesSource.Parse("github:backend-br/vagas", "backend-br/vagas", J($$"""{"number":1,"title":"{{title}}","html_url":"https://github.com/x","body":"","labels":[]}"""));
        Assert.Equal(company, p.Company);
        Assert.Equal(role, p.Title);
    }

    [Fact]
    public void Comentario_do_hn_vira_vaga_pela_primeira_linha()
    {
        var p = HackerNewsSource.Parse("hn:whoishiring", J("""
            {"id":123,"created_at":"2026-11-02T15:00:00Z","text":"Acme | Senior .NET Engineer | Remote (Americas) | $150k-$180k<p>We use C# and ASP.NET Core. Apply: <a href=\"https://boards.greenhouse.io/acme/jobs/9\">here</a>"}
            """));
        Assert.Equal("Acme", p.Company);
        Assert.Equal("Senior .NET Engineer", p.Title);
        Assert.Equal("https://boards.greenhouse.io/acme/jobs/9", p.ApplyUrl);
        Assert.Contains("Remote", p.Location);
    }

    [Theory]
    [InlineData("Salário: R$ 10.000,00 a R$ 14.000,00", 10000, 14000, "BRL")]
    [InlineData("$150k-$180k", 150000, 180000, "USD")]
    [InlineData("USD 5,000 - 7,500 per month", 5000, 7500, "USD")]
    [InlineData("R$ 15K – R$ 20K", 15000, 20000, "BRL")]
    public void Salario_em_faixa_e_moeda(string text, int min, int max, string currency)
    {
        var (_, a, b, c) = Normalizer.Salary(text);
        Assert.Equal(min, a);
        Assert.Equal(max, b);
        Assert.Equal(currency, c);
    }

    [Fact]
    public void Normalizacao_nao_inventa_o_que_a_vaga_nao_diz()
    {
        var n = Normalizer.From("Backend Developer", "Trabalho com APIs.", "", "https://example.com/vaga");
        Assert.Null(n.Seniority);
        Assert.Null(n.Modality);
        Assert.Null(n.SalaryText);
        Assert.Null(n.Contract);
        Assert.Equal("link", n.ApplyChannel);
    }

    [Fact]
    public void Normalizacao_le_senioridade_modalidade_contrato_e_ingles()
    {
        var n = Normalizer.From("Desenvolvedor .NET Sênior", "Vaga CLT, 100% remoto. Inglês avançado.", "Brasil", "https://jobs.lever.co/acme/1");
        Assert.Equal("Sênior", n.Seniority);
        Assert.Equal("Remoto", n.Modality);
        Assert.Equal("CLT", n.Contract);
        Assert.Equal("Inglês avançado", n.English);
        Assert.Equal("ats", n.ApplyChannel);
        Assert.Equal("manual", Normalizer.Channel("https://www.linkedin.com/jobs/view/123", null));
    }

    [Fact]
    public void Link_salvo_de_ats_vira_board_vigiado_e_titulo_do_linkedin_vira_empresa_e_cargo()
    {
        Assert.Equal("greenhouse:acme", CaptureJobHandler.AtsSource(new Uri("https://job-boards.greenhouse.io/acme/jobs/123")));
        Assert.Equal("lever:norte", CaptureJobHandler.AtsSource(new Uri("https://jobs.lever.co/norte/abc-123")));
        Assert.Equal("ashby:lume", CaptureJobHandler.AtsSource(new Uri("https://jobs.ashbyhq.com/lume/uuid")));
        Assert.Null(CaptureJobHandler.AtsSource(new Uri("https://www.linkedin.com/jobs/view/1")));
        Assert.Equal(("Atlas Saúde", "Senior .NET Engineer"), CaptureJobHandler.FromTitle("(3) Atlas Saúde hiring Senior .NET Engineer in Brazil | LinkedIn"));
        Assert.Equal(("Norte", "Backend Engineer"), CaptureJobHandler.FromTitle("Backend Engineer at Norte"));
    }

    [Fact]
    public void Remotive_aponta_para_o_link_do_remotive_e_e_remoto()
    {
        var p = RemotiveSource.Parse("remotive:.net", J("""
            {"id":7,"url":"https://remotive.com/remote-jobs/software-dev/net-engineer-7","title":".NET Engineer","company_name":"Kuiper","job_type":"full_time",
             "publication_date":"2026-11-03T10:00:00","candidate_required_location":"LATAM","salary":"$4k - $6k","description":"<p>C# and .NET</p>"}
            """));
        Assert.Equal("https://remotive.com/remote-jobs/software-dev/net-engineer-7", p.ApplyUrl);
        Assert.Equal("LATAM", p.Location);
        Assert.Equal("remote", p.Workplace);
        Assert.True(p.ListedAsOpen);
    }

    [Theory]
    [InlineData("User-agent: *\nDisallow: /", "/carreiras", false)]
    [InlineData("User-agent: *\nDisallow: /admin", "/carreiras", true)]
    [InlineData("User-agent: *\nDisallow: /carreiras\nAllow: /carreiras/vagas", "/carreiras/vagas", true)]
    [InlineData("User-agent: Googlebot\nDisallow: /\n\nUser-agent: *\nAllow: /", "/carreiras", true)]
    [InlineData("", "/qualquer", true)]
    public void Robots_txt_e_respeitado(string robots, string path, bool allowed) =>
        Assert.Equal(allowed, CareerPageReader.Allowed(robots.Replace("\\n", "\n"), path));

    [Fact]
    public void Pagina_de_carreira_revela_os_boards_de_ats()
    {
        var html = """
            <a href="https://boards.greenhouse.io/acme/jobs/1">Vaga</a>
            <iframe src="https://boards.greenhouse.io/embed/job_board?for=acme"></iframe>
            <a href="https://jobs.lever.co/norte">Lever</a> <a href="https://jobs.ashbyhq.com/lume/abc">Ashby</a>
            """;
        Assert.Equal(["greenhouse:acme", "lever:norte", "ashby:lume"], CareerPageReader.Boards(html));
    }
}
