using JobOrchestrator.Materials.Compose;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>
/// The sweep of 07/11: 8 of 16 resumes mixed two languages and 5 copied the CV's own summary. A draft like that goes to another model,
/// told what to fix; the CV's text stands in only in the resume's language; the years of experience come from the dates.
/// </summary>
public sealed class ResumeQualityTests
{
    private static readonly ProfileFact Job = new(Guid.NewGuid(), "experience", "Desenvolvedora .NET Sênior na Acme Pagamentos.", null, "Acme Pagamentos", "Desenvolvedora .NET Sênior", "2022-03", null, [".NET 8", "PostgreSQL"], null, "cv");
    private static readonly ProfileFact Old = new(Guid.NewGuid(), "experience", "Desenvolvedora Full Stack na Brisa Sistemas.", null, "Brisa Sistemas", "Desenvolvedora Full Stack", "2019-01", "2022-02", ["Angular", "C#"], null, "cv");
    private static readonly ProfileFact Summary = new(Guid.NewGuid(), "summary", "Desenvolvedora com 6 anos de experiência em C#, .NET e PostgreSQL. Inglês avançado.", null, null, null, null, null, [], null, "cv");
    private static Selection English => new([new Picked("F1", Job, null), new Picked("F2", Old, null), new Picked("F3", Summary, null)], "en", ["PostgreSQL"], []);

    [Fact]
    public void A_mixed_language_draft_is_sent_back()
    {
        var d = new Draft(new Claim("Desenvolvedora Full Stack .NET", ["F1"]), new Claim(Summary.Statement, ["F3"]),
            [new DraftExperience("F1", "Desenvolvedora .NET Sênior", [new Claim("Migrated the billing system to .NET 8 and PostgreSQL, cutting the monthly closing from 6 hours to 40 minutes.", ["F1"]),
                new Claim("Worked with .NET 8, PostgreSQL, RabbitMQ.", ["F1"])])], [], [], []);
        var problems = Quality.Problems(d, English);
        Assert.Contains(problems, p => p.StartsWith("the summary is not in English", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("role title \"Desenvolvedora .NET Sênior\" is not translated", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("the headline", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("the summary copies", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.StartsWith("this bullet only lists technologies", StringComparison.Ordinal));
    }

    [Fact]
    public void A_clean_english_draft_passes()
    {
        var d = new Draft(new Claim("Senior .NET Developer | C# | PostgreSQL", ["F1"]),
            new Claim("Senior .NET developer with 6 years building payment APIs in C# and PostgreSQL. Cut the monthly billing close from 6 hours to 40 minutes.", ["F1"]),
            [new DraftExperience("F1", "Senior .NET Developer", [new Claim("Migrated the billing system to .NET 8 and PostgreSQL, cutting the monthly closing from 6 hours to 40 minutes.", ["F1"])])], [], [], []);
        Assert.Empty(Quality.Problems(d, English));
    }

    [Fact]
    public void Years_come_from_the_dates_of_the_experiences() =>
        Assert.Equal(7, Prompts.Years([Job, Old], new DateTime(2026, 11, 7)));

    [Fact]
    public void The_cv_summary_stands_in_only_in_the_resume_language()
    {
        // The writer's summary claims a tool the facts do not have: it falls, and the Portuguese CV summary does not replace it in English.
        var d = new Draft(null, new Claim("Expert in Kubernetes and Terraform at Acme.", ["F1"]), [new DraftExperience("F1", "Senior .NET Developer", [])], [], [], []);
        var outcome = Conferente.Check(d, English);
        Assert.Null(outcome.Draft.Summary);
    }

    // The sweep's third round: half the resumes lost the whole summary for naming the hiring company or for one word, and "6 anos" came
    // from an old CV summary. The summary keeps what its facts back, names the company, and never says fewer years than the dates.
    private static Selection ForNorte => English with { Company = "Norte Pagamentos" };

    [Fact]
    public void Naming_the_hiring_company_and_saying_sql_based_keep_the_summary()
    {
        var d = new Draft(null, new Claim("Senior .NET Developer with 7 years of experience in C# and PostgreSQL. Brings PostgreSQL-based billing work to the Norte Pagamentos team.", ["F1", "F2"]),
            [new DraftExperience("F1", "Senior .NET Developer", [])], [], [], []);
        var outcome = Conferente.Check(d, ForNorte);
        Assert.Equal(d.Summary!.Text, outcome.Draft.Summary?.Text);
    }

    [Fact]
    public void One_sentence_without_facts_leaves_and_the_rest_of_the_summary_stays()
    {
        var d = new Draft(null, new Claim("Senior .NET Developer with 7 years of experience in C# and PostgreSQL, building payment systems. Expert in Kubernetes and Terraform.", ["F1", "F2"]),
            [new DraftExperience("F1", "Senior .NET Developer", [])], [], [], []);
        var outcome = Conferente.Check(d, English);
        Assert.Equal("Senior .NET Developer with 7 years of experience in C# and PostgreSQL, building payment systems.", outcome.Draft.Summary?.Text);
    }

    [Fact]
    public void With_no_summary_left_one_is_written_from_the_facts()
    {
        var d = new Draft(new Claim("Senior .NET Developer | C# | PostgreSQL", ["F1"]), null,
            [new DraftExperience("F1", "Senior .NET Developer", [new Claim("Migrated the billing system to .NET 8.", ["F1"])]), new DraftExperience("F2", "Full Stack Developer", [])], ["PostgreSQL", "C#", "Angular", "RabbitMQ"], [], []);
        var done = Tailor.Finish(d, English);
        Assert.StartsWith("Senior .NET Developer with ", done.Summary?.Text);
        Assert.Contains("years of experience in PostgreSQL, C# and Angular, across Acme Pagamentos and Brisa Sistemas.", done.Summary?.Text);
    }

    [Theory]
    [InlineData("Desenvolvedora com 6 anos de experiência em C# e .NET. Reduzi o fechamento de 6 horas para 40 minutos.", "Desenvolvedora com 7 anos de experiência em C# e .NET. Reduzi o fechamento de 6 horas para 40 minutos.")]
    [InlineData("Senior .NET Developer with 5+ years in C#. Led 2 years of work on billing.", "Senior .NET Developer with 7+ years in C#. Led 2 years of work on billing.")]
    [InlineData("Senior .NET Developer with 7 years in C#.", "Senior .NET Developer with 7 years in C#.")]
    public void The_opening_never_says_fewer_years_than_the_dates(string summary, string expected) => Assert.Equal(expected, Tailor.Counted(summary, 7));

    [Theory]
    [InlineData("Corrected calculations in 1.200 invoices", true, "Corrected calculations in 1,200 invoices")]
    [InlineData("Corrigi o cálculo em 1,200 faturas", false, "Corrigi o cálculo em 1.200 faturas")]
    [InlineData("Billing — payments squad, 2019–2022, .NET 8", true, "Billing, payments squad, 2019–2022, .NET 8")]
    public void No_dashes_and_thousands_the_way_the_language_writes_them(string text, bool en, string expected) => Assert.Equal(expected, Tailor.Plain(text, en));

    [Theory]
    [InlineData("Portuguese — Native", "Portuguese (Native)")]
    [InlineData("Inglês - Avançado", "Inglês (Avançado)")]
    [InlineData("Português (nativo)", "Português (nativo)")]
    public void A_language_level_goes_in_parentheses(string line, string expected) => Assert.Equal(expected, Tailor.Level(line));
}

