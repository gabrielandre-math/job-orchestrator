using JobOrchestrator.Materials.Compose;
using JobOrchestrator.Profile.Contracts;
using JobOrchestrator.Profile.Domain;
using JobOrchestrator.Profile.Features;
using JobOrchestrator.Profile.Import;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Aceite da fase 3: nada entra no currículo sem fato, e dado pessoal não chega ao modelo.</summary>
public sealed class TruthTests
{
    private static ProfileFact Fact(string label, string kind, string statement, Guid? parent = null, string? org = null, string? role = null, string? start = null, string? end = null, params string[] tech) =>
        new(Guid.NewGuid(), kind, statement, parent, org, role, start, end, tech, null, $"cv.pdf, {label}");

    private static (Selection Selection, Picked Job, Picked Win) Profile()
    {
        var job = Fact("F1", "experience", "Engenheiro de Software Pleno, Fintech Exemplo", null, "Fintech Exemplo", "Engenheiro de Software Pleno", "2022-03", null, "ASP.NET Core", "PostgreSQL", "RabbitMQ");
        var win = Fact("F2", "achievement", "Migrei filas de processamento de boletos para RabbitMQ, reduzindo o tempo de conciliação em 40%.", job.Id, tech: "RabbitMQ");
        var api = Fact("F3", "achievement", "Desenvolvi APIs em ASP.NET Core 8 com PostgreSQL atendendo 2 milhões de requisições por dia.", job.Id, tech: ["ASP.NET Core", "PostgreSQL"]);
        var skill = Fact("F4", "skill", "Docker");
        var picked = new List<Picked> { new("F1", job, null), new("F2", win, "F1"), new("F3", api, "F1"), new("F4", skill, null) };
        return (new Selection(picked, "en", ["RabbitMQ"], ["Kubernetes"]), picked[0], picked[1]);
    }

    [Fact]
    public void Fato_plantado_no_bullet_e_rejeitado()
    {
        var (s, _, _) = Profile();
        var draft = new Draft(null, null,
            [new DraftExperience("F1", "Mid-level Software Engineer", [
                new Claim("Migrated billing queues to RabbitMQ, cutting reconciliation time by 40%.", ["F2"]),
                new Claim("Led the migration to Kubernetes, cutting costs by 35%.", ["F2"])])], ["RabbitMQ", "Kubernetes"], [], []);
        var outcome = Conferente.Check(draft, s);
        var bullets = outcome.Draft.Experiences.Single().Bullets;
        Assert.Single(bullets);
        Assert.Contains("RabbitMQ", bullets[0].Text);
        Assert.Contains(outcome.Removed, r => r.Contains("Kubernetes") && r.Contains("35"));
        Assert.DoesNotContain("Kubernetes", outcome.Draft.Skills);
    }

    [Fact]
    public void Traducao_fiel_passa_e_senioridade_inflada_nao()
    {
        var (s, _, _) = Profile();
        Assert.Empty(Claims.Unsupported("Developed APIs in ASP.NET Core 8 with PostgreSQL serving 2 million requests per day.", [s.Get("F3")!.Fact.Statement]));
        Assert.Empty(Claims.Unsupported("Mid-level Software Engineer", ["Engenheiro de Software Pleno"]));
        Assert.Contains("Senior", Claims.Unsupported("Senior Software Engineer", ["Engenheiro de Software Pleno"]));
    }

    [Fact]
    public void Bullet_que_cita_fato_de_outra_experiencia_sai()
    {
        var (s, _, _) = Profile();
        var draft = new Draft(null, null, [new DraftExperience("F1", "Software Engineer", [new Claim("Docker", ["F4"])])], [], [], []);
        Assert.Empty(Conferente.Check(draft, s).Draft.Experiences.Single().Bullets);
    }

    [Fact]
    public void Revisor_recusa_e_o_bullet_sai()
    {
        var (s, _, _) = Profile();
        var draft = new Draft(null, null, [new DraftExperience("F1", "Software Engineer", [new Claim("Migrated queues to RabbitMQ.", ["F2"]), new Claim("Owned the billing domain.", ["F2"])])], [], [], []);
        var (clean, removed) = Conferente.Apply(draft, s, [new Prompts.Verdict("C1", true, ""), new Prompts.Verdict("C2", false, "ownership not stated")]);
        Assert.Single(clean.Experiences.Single().Bullets);
        Assert.Single(removed);
    }

    [Fact]
    public async Task Configuracao_da_tela_grava_e_le_de_volta()
    {
        var dir = Directory.CreateTempSubdirectory("jo-settings-").FullName;
        var before = Environment.GetEnvironmentVariable("JO_CONFIG_DIR");
        Environment.SetEnvironmentVariable("JO_CONFIG_DIR", dir);
        try
        {
            await JobOrchestrator.BuildingBlocks.UserSettings.SetAsync(new Dictionary<string, object?> { ["Onboarding:Done"] = true, ["Retention:Days"] = 90, ["Scraper:Assist"] = "Local", ["Llm:Order"] = new[] { "claude-pro", "codex" } });
            await JobOrchestrator.BuildingBlocks.UserSettings.SetAsync(new Dictionary<string, object?> { ["Scraper:Assist"] = null, ["Privacy:LocalOnly"] = false });
            var json = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(dir, "settings.json")))!;
            Assert.True(json["Onboarding"]!["Done"]!.GetValue<bool>());
            Assert.Equal(90, json["Retention"]!["Days"]!.GetValue<int>());
            Assert.Null(json["Scraper"]!["Assist"]);
            Assert.Equal("codex", json["Llm"]!["Order"]![1]!.GetValue<string>());
            Assert.False(json["Privacy"]!["LocalOnly"]!.GetValue<bool>());
        }
        finally { Environment.SetEnvironmentVariable("JO_CONFIG_DIR", before); Directory.Delete(dir, true); }
    }

    [Fact]
    public void Nome_no_pdf_do_linkedin_vem_antes_do_titulo()
    {
        const string cv = "Contact\n+55 (11) 98888-7777 (Mobile)\nmaria.teste@example.com\nTop Skills\nGestão de Projetos\nAtendimento ao Cliente\nLanguages\nInglês (Professional Working)\n\nMaria Souza Teste\n\nEnfermeira | UTI | Gestão de Leitos\nSão Paulo, São Paulo, Brazil\nSummary";
        Assert.Equal("Maria Souza Teste", Redactor.Detect(cv).FullName);
        const string broken = "Contact\nwww.linkedin.com/in/\nmaria-teste-123 (LinkedIn)\n\nMaria Souza Teste\nEnfermeira | UTI";
        var id = Redactor.Detect(broken);
        Assert.Equal("https://www.linkedin.com/in/maria-teste-123", id.LinkedIn);
        Assert.DoesNotContain("maria-teste-123", Redactor.Redact(broken, id));
    }

    [Fact]
    public void Dados_pessoais_nao_chegam_ao_modelo()
    {
        const string cv = "Bruno Teste Ficticio\nSão Paulo, SP | bruno.teste@example.com | (11) 98888-7777 | linkedin.com/in/bruno-teste | github.com/brunoteste\nCPF: 123.456.789-09\nEngenheiro de Software (03/2022 - 2024), salário 2019-2022";
        var id = Redactor.Detect(cv);
        Assert.Equal("Bruno Teste Ficticio", id.FullName);
        Assert.Equal("bruno.teste@example.com", id.Email);
        var redacted = Redactor.Redact(cv, id);
        foreach (var leak in new[] { "Bruno", "Ficticio", "bruno.teste", "98888", "linkedin.com", "github.com", "123.456" })
            Assert.DoesNotContain(leak, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2019-2022", redacted);
        Assert.Contains("03/2022 - 2024", redacted);
    }

    [Fact]
    public void Trecho_que_nao_existe_vira_fato_a_confirmar_e_tecnologia_inventada_sai()
    {
        const string text = "Engenheiro de Software, Fintech Exemplo (03/2022 - atual)\n- Migrei filas para RabbitMQ, reduzindo o tempo em 40%.\nCompetências: C#, RabbitMQ";
        var x = new FactExtractor.XProfile(null, null, null,
            [new FactExtractor.XExperience("Fintech Exemplo", "Engenheiro de Software", "2022-03", "", ["RabbitMQ", "Kafka"], "Engenheiro de Software, Fintech Exemplo (03/2022 - atual)",
                [new FactExtractor.TQ("Migrei filas para RabbitMQ, reduzindo o tempo em 60%.", "Migrei filas para RabbitMQ, reduzindo o tempo em 40%."),
                 new FactExtractor.TQ("Liderei um time de 12 pessoas.", "Liderei um time de 12 pessoas.")])],
            [], [new FactExtractor.XSkill("C#", "C#"), new FactExtractor.XSkill("Go", "Go")], [], [], []);
        var r = FactExtractor.Verify(x, text, Guid.NewGuid(), Guid.NewGuid(), new Identity("", null, null, null, null, null, null), "teste", DateTime.UtcNow);
        var job = r.Facts.Single(f => f.Kind == "experience");
        Assert.Equal(FactStatus.Verified, job.Status);
        Assert.Equal(["RabbitMQ"], job.Technologies);
        var wins = r.Facts.Where(f => f.Kind == "achievement").ToList();
        Assert.Contains(wins, f => f.Status == FactStatus.Verified && f.Statement.Contains("40%"));
        Assert.DoesNotContain(wins, f => f.Statement.Contains("60%"));
        Assert.Contains(wins, f => f.Status == FactStatus.Inferred && f.Statement.Contains("12 pessoas"));
        Assert.DoesNotContain(r.Facts, f => f.Kind == "skill" && f.Statement == "Go");
    }

    [Fact]
    public void Export_do_linkedin_vira_fato_sem_ia()
    {
        var tables = new Dictionary<string, IReadOnlyList<string[]>>
        {
            ["Profile.csv"] = [["First Name", "Last Name", "Headline"], ["Bruno", "Teste", "Backend .NET"]],
            ["Positions.csv"] = [["Company Name", "Title", "Description", "Location", "Started On", "Finished On"],
                ["Fintech Exemplo", "Engenheiro de Software", "- Migrei filas para RabbitMQ em produção\n- Mantive APIs em C# com PostgreSQL", "São Paulo", "Mar 2022", ""]],
            ["Skills.csv"] = [["Name"], ["RabbitMQ"], ["C#"], ["PostgreSQL"]],
            ["Languages.csv"] = [["Name", "Proficiency"], ["Inglês", "Professional working proficiency"]]
        };
        var (facts, identity) = LinkedInFacts.Map(tables, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        Assert.Equal("Bruno Teste", identity.FullName);
        var job = facts.Single(f => f.Kind == "experience");
        Assert.Equal("2022-03", job.Start);
        Assert.Null(job.End);
        Assert.Equal(2, facts.Count(f => f.Kind == "achievement"));
        Assert.Contains("RabbitMQ", job.Technologies);
        Assert.Equal("advanced", facts.Single(f => f.Kind == "language").Level);
        Assert.All(facts, f => Assert.Equal(FactStatus.Verified, f.Status));
    }

    [Fact]
    public void Anos_por_tecnologia_nao_contam_mes_duas_vezes_e_pretensao_segue_o_vinculo()
    {
        var a = Fact("", "experience", "A", null, "A", "Dev", "2020-01", "2021-12", "C#");
        var b = Fact("", "experience", "B", null, "B", "Dev", "2021-01", "2022-12", "C#");
        var conf = Confirmations.Empty with { FloorCltBrl = 12000, FloorPjBrl = 16000, FloorUsdMonth = 5000, ConfirmedAt = DateTime.UtcNow };
        var p = new ProfileSnapshot(Guid.NewGuid(), 1, "h", false, true, new Identity("Bruno Teste", "b@example.com", null, null, null, null, null), [a, b], conf);
        Assert.Equal(3, AnswerBank.Years(p, "C#", new DateOnly(2026, 11, 5)));
        Assert.Null(AnswerBank.Years(p, "Go", new DateOnly(2026, 11, 5)));
        var pj = AnswerBank.Build(p, new AnswerContext("pj", "BRL", "BR", ["C#"], "pt", "greenhouse"), new DateOnly(2026, 11, 5));
        Assert.Equal("R$ 16.000 por mês (PJ)", pj["salary_expectation"]);
        // A form may ask the PJ or the CLT amount whatever the job's contract: each from its own floor.
        Assert.Equal("R$ 16.000 por mês (PJ)", pj["salary_pj"]);
        Assert.Equal("R$ 12.000 por mês (CLT)", pj["salary_clt"]);
        Assert.Equal("3", pj["years_experience:C#"]);
        var usd = AnswerBank.Build(p, new AnswerContext("contractor", "USD", "US", [], "en", "hn"), new DateOnly(2026, 11, 5));
        Assert.Equal("USD 5,000 per month", usd["salary_expectation"]);
        Assert.Equal("BRL 16,000 per month (PJ contract)", usd["salary_pj"]);
        Assert.Equal("No", usd["work_authorization"]);
        Assert.Equal("Hacker News", usd["how_heard"]);
    }

    [Fact]
    public void Nome_do_arquivo_e_o_que_ats_e_recrutador_esperam() =>
        Assert.Equal("Joao-Da-Silva", ResumeComposer.FileName("João da Silva"));
}
