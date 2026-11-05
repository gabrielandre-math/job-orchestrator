using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Alertas de vaga por e-mail: cada link de vaga vira uma vaga, sem rastreio, com empresa e local; link genérico e de gerenciar alerta ficam de fora.</summary>
public sealed class AlertTests
{
    [Fact]
    public void Alerta_do_linkedin_vira_vagas_sem_rastreio()
    {
        const string html = """
            <table><tr><td><a href="https://www.linkedin.com/comm/jobs/view/4012345678/?trackingId=abc&amp;refId=x">Desenvolvedor .NET Sênior</a></td></tr>
            <tr><td>Acme Tecnologia · Rio de Janeiro, RJ (Remoto)</td></tr>
            <tr><td><a href="https://www.linkedin.com/comm/jobs/view/4012345678/?x=1">Ver vaga</a></td></tr>
            <tr><td><a href="https://www.linkedin.com/comm/jobs/view/4099999999/">Engenheiro de Software Backend</a></td></tr><tr><td>Beta S.A. · São Paulo</td></tr>
            <a href="https://www.linkedin.com/comm/jobs/alerts?x=1">Gerenciar alertas</a></table>
            """;
        Assert.True(AlertParser.IsAlert("jobalerts-noreply@linkedin.com", "Desenvolvedor .NET: 12 novas vagas"));
        var jobs = AlertParser.Parse("jobalerts-noreply@linkedin.com", html, new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(2, jobs.Count);
        Assert.Equal(("Desenvolvedor .NET Sênior", "Acme Tecnologia", "Rio de Janeiro, RJ (Remoto)", "https://www.linkedin.com/jobs/view/4012345678/"), (jobs[0].Title, jobs[0].Company, jobs[0].Location, jobs[0].Url));
        Assert.Equal(("Engenheiro de Software Backend", "Beta S.A.", "São Paulo"), (jobs[1].Title, jobs[1].Company, jobs[1].Location));
    }

    [Fact]
    public void Alerta_do_indeed_tira_o_rastreio_e_separa_empresa_e_local()
    {
        const string html = """<div><a href="https://br.indeed.com/rc/clk/dl?jk=1a2b3c4d5e6f7a8b&amp;from=ja">Técnico de Enfermagem</a><div>Hospital Exemplo - Niterói, RJ</div></div>""";
        var job = Assert.Single(AlertParser.Parse("alert@indeed.com", html, DateTime.UtcNow));
        Assert.Equal(("Técnico de Enfermagem", "Hospital Exemplo", "Niterói, RJ", "https://br.indeed.com/viewjob?jk=1a2b3c4d5e6f7a8b"), (job.Title, job.Company, job.Location, job.Url));
    }

    [Fact]
    public void E_mail_comum_nao_e_alerta() => Assert.False(AlertParser.IsAlert("amigo@gmail.com", "Vagas para o churrasco"));
}
