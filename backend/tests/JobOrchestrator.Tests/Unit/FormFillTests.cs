using System.Text.Json;
using JobOrchestrator.Applications.Features.Workflow;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Intelligence.Contracts;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>
/// Forms filled from the profile and the AI: the salary of the contract the question names, "at least N years" from the dates of
/// exactly that technology, and AI answers that must be an offered option or be sustained by the facts.
/// </summary>
public sealed class FormFillTests
{
    private static MaterialBundle Bundle(params (string Key, string Value)[] answers) =>
        new("Ana", "Lima", "ana@example.com", "+55 11 90000-0000", "https://linkedin.com/in/ana", "/tmp/cv.pdf", answers.ToDictionary(a => a.Key, a => a.Value));

    private static readonly MaterialBundle Years = Bundle(("years_experience:C#", "5"), ("years_experience:.NET", "6"), ("years_experience:Go", "1"), ("years_experience:React", "4"));

    [Theory]
    [InlineData("Qual é a sua expectativa salarial mensal em R$ para atuar como PJ?", "P")]
    [InlineData("Pretensão salarial para contratação CLT", "C")]
    [InlineData("What are your salary expectations?", "X")]
    [InlineData("Expectativa salarial", "X")]
    [InlineData("What is your minimum expected gross salary per month in USD (before taxes)?", "U")]
    [InlineData("What is your current gross salary per month in USD (before taxes)?", null)]
    [InlineData("Qual o seu salário atual?", null)]
    public void Salary_questions_take_the_amount_of_their_contract(string question, string? expected) =>
        Assert.Equal(expected, LabelForm.Resolve(question, "text", Bundle(("salary_expectation", "X"), ("salary_pj", "P"), ("salary_clt", "C"), ("salary_usd", "U"))).Value);

    [Theory]
    [InlineData("Do you have at least 3 years of experience with C#?", "Yes")]
    [InlineData("Você tem no mínimo 6 anos de experiência com C#?", "No")]
    [InlineData("2+ years in Go?", "No")]
    [InlineData("Pelo menos 2 anos com C# e .NET?", "Yes")]
    [InlineData("Você tem experiência prática de no mínimo 2 anos com .NET WebForms, incluindo a manutenção de componentes e ciclo de vida da página?", null)]
    [InlineData("At least 3 years with React Native?", null)]
    [InlineData("How many years of professional experience do you have with C#?", null)]
    public void At_least_N_years_comes_from_the_dates_of_exactly_that_technology(string question, string? expected) =>
        Assert.Equal(expected, LabelForm.AtLeastYears(question, Years)?.Value);

    [Fact]
    public void A_yes_or_no_written_in_a_text_box_follows_the_question_language()
    {
        Assert.Equal("Sim", LabelForm.Resolve("Você tem no mínimo 3 anos de experiência com C#?", "text", Years).Value);
        Assert.Equal("Yes", LabelForm.Resolve("Do you have at least 3 years of experience with C#?", "text", Years).Value);
        // The number question keeps its number; a minimum on a technology without dates waits for the AI instead of borrowing .NET's.
        Assert.Equal("5", LabelForm.Resolve("How many years of professional experience do you have with C#?", "text", Years).Value);
        Assert.Null(LabelForm.Resolve("Você tem experiência prática de no mínimo 2 anos com .NET WebForms?", "text", Years).Value);
    }

    [Fact]
    public void Your_answer_wins_over_the_ai_and_the_ai_over_nothing()
    {
        const string q = "Walk us through a feature you owned end-to-end.";
        var both = Bundle((CustomAnswer.Key(q), "mine"), (CustomAnswer.AiKey(q), "ai"));
        Assert.Equal(("mine", "sua resposta"), LabelForm.Resolve(q, "textarea", both));
        Assert.Equal(("ai", "IA, pelos fatos do seu perfil"), LabelForm.Resolve(q, "textarea", Bundle((CustomAnswer.AiKey(q), "ai"))));
    }

    private static readonly IReadOnlyList<(string Id, ProfileFact Fact)> Facts =
    [
        ("f1", new ProfileFact(Guid.NewGuid(), "experience", "Desenvolvedor .NET na Acme: migrou o faturamento para .NET 8 e PostgreSQL.", null, "Acme", "Desenvolvedor .NET", "2021-03", null, [".NET 8", "PostgreSQL", "C#"], null, "cv")),
        ("f2", new ProfileFact(Guid.NewGuid(), "skill", "Angular", null, null, null, null, null, ["Angular"], null, "cv")),
    ];

    private static readonly IReadOnlyList<string> Sources = [.. Facts.SelectMany(f => new[] { f.Fact.Statement, f.Fact.Organization ?? "", string.Join(", ", f.Fact.Technologies) }), "Invicti"];

    private static Answerer.Checked One(FormQuestion q, string answer, string[]? choices = null) =>
        Answerer.Check([q], [new Answerer.ModelAnswer(0, answer, choices ?? [], ["f1"])], Facts, Sources)[0];

    [Fact]
    public void A_choice_is_kept_only_as_an_option_the_form_offers()
    {
        var radio = new FormQuestion("Which framework do you know best?", "radio", ["React", "Angular", "Vue"]);
        Assert.Equal("Angular", One(radio, "angular").Value);
        Assert.Null(One(radio, "Svelte").Value);
        var yesNo = new FormQuestion("Você tem experiência com .NET WebForms?", "select", ["Sim", "Não"]);
        Assert.Equal("Não", One(yesNo, "No").Value);
        var boxes = new FormQuestion("Which JavaScript framework or library have you worked with extensively?", "checkboxes", ["React", "Angular", "Vue", "None"]);
        Assert.Equal("Angular", One(boxes, "", ["Angular", "Ember"]).Value);
        Assert.Null(One(boxes, "", ["Ember"]).Value);
    }

    [Fact]
    public void Picks_written_in_the_answer_instead_of_the_list_still_count()
    {
        var boxes = new FormQuestion("Which JavaScript framework or library have you worked with extensively?", "checkboxes", ["React", "Angular", "Vue", "None"]);
        Assert.Equal("React\nAngular", One(boxes, "React, Angular").Value);
        Assert.Equal("React", One(boxes, "", ["React, Next.js"]).Value);
        Assert.Equal("Yes", One(new FormQuestion("I confirm I am currently based in Latin America", "confirm", []), "I confirm").Value);
    }

    [Theory]
    [InlineData("Rio de Janeiro, RJ", "Brazil")]
    [InlineData("São Paulo - SP", "Brazil")]
    [InlineData("Curitiba, Brasil", "Brazil")]
    [InlineData("Lisbon, Portugal", "Portugal")]
    [InlineData(null, null)]
    public void The_ai_gets_your_country_never_your_city(string? location, string? country) => Assert.Equal(country, Answerer.Country(location));

    [Fact]
    public void An_english_question_answered_in_portuguese_is_refused()
    {
        var q = new FormQuestion("Walk us through a feature you owned end-to-end.", "textarea", []);
        Assert.Equal("escrita em outra língua", One(q, "Na Acme, migrei o faturamento para .NET 8 com PostgreSQL, da análise até a entrega.").Problem);
        Assert.NotNull(One(q, "At Acme I owned the billing migration to .NET 8 and PostgreSQL, from the analysis to the release.").Value);
    }

    [Fact]
    public void A_number_keeps_only_its_digits_and_a_confirmation_needs_a_yes()
    {
        Assert.Equal("15000", One(new FormQuestion("Expected monthly salary (BRL)", "number", []), "R$ 15.000").Value);
        Assert.Null(One(new FormQuestion("Expected monthly salary (BRL)", "number", []), "a combinar").Value);
        Assert.Equal("Yes", One(new FormQuestion("I confirm I am based in Latin America", "confirm", []), "Yes").Value);
        Assert.Null(One(new FormQuestion("I confirm I hold a US security clearance", "confirm", []), "No").Value);
    }

    [Fact]
    public void A_written_answer_must_be_sustained_by_the_facts()
    {
        var q = new FormQuestion("Conte sobre uma entrega que você conduziu do começo ao fim.", "textarea", []);
        Assert.NotNull(One(q, "Na Acme, migrei o faturamento para .NET 8 com PostgreSQL, da análise até a entrega.").Value);
        var invented = One(q, "Liderei a adoção de Kubernetes e Kafka em produção por 5 anos.");
        Assert.Null(invented.Value);
        Assert.StartsWith("fora dos fatos", invented.Problem);
    }

    [Fact]
    public void A_technology_the_question_names_may_appear_only_to_say_it_is_missing()
    {
        var q = new FormQuestion("How have you used Kubernetes in production?", "textarea", []);
        Assert.NotNull(One(q, "I have not used Kubernetes in production yet; at Acme I shipped the billing migration to .NET 8 and PostgreSQL.").Value);
        Assert.Null(One(q, "I run Kubernetes clusters at Acme.").Value);
    }

    private static LlmResult Reply(string model, params (int Index, string Answer)[] answers) =>
        new(true, JsonSerializer.Serialize(new { answers = answers.Select(a => new { index = a.Index, answer = a.Answer, choices = Array.Empty<string>(), facts = new[] { "f1" } }) }), model, model, "", null, 1);

    private static IReadOnlyList<Answerer.Checked> Checks(IReadOnlyList<FormQuestion> questions, IReadOnlyList<Answerer.ModelAnswer> answers) =>
        Answerer.Check(questions, answers, Facts, Sources);

    [Fact]
    public async Task What_one_model_gets_wrong_goes_up_the_ladder_with_the_reason()
    {
        var radio = new FormQuestion("Which framework do you know best?", "radio", ["React", "Angular", "Vue"]);
        var open = new FormQuestion("Walk us through a feature you owned end-to-end.", "textarea", []);
        var asked = new List<(string Model, string[] Questions, string[]? Problems)>();
        var replies = new Dictionary<string, LlmResult>
        {
            ["ollama"] = Reply("ollama", (0, "Angular"), (1, "I ran Kubernetes clusters at Acme.")),
            ["codex"] = Reply("codex", (0, "Na Acme migrei o faturamento para .NET 8 com PostgreSQL.")),
            ["claude-max"] = Reply("claude-max", (0, "At Acme I owned the billing migration to .NET 8 and PostgreSQL, from the analysis to the release.")),
        };
        var result = await Answerer.EscalateAsync(["ollama", "codex", "claude-max"], [radio, open],
            (model, questions, problems) => { asked.Add((model, [.. questions.Select(q => q.Label)], problems?.ToArray())); return Task.FromResult(replies[model]); }, Checks);

        Assert.Equal(("Angular", "ollama"), (result.Single(r => r.Checked.Question == radio).Checked.Value, result.Single(r => r.Checked.Question == radio).By.ProviderId));
        Assert.Equal("claude-max", result.Single(r => r.Checked.Question == open).By.ProviderId);
        Assert.Equal(["ollama", "codex", "claude-max"], asked.Select(a => a.Model));
        // Each step gets only what is still open, with why the last answer was refused.
        Assert.Equal([open.Label], asked[1].Questions);
        Assert.StartsWith("fora dos fatos", asked[1].Problems![0]);
        Assert.Equal("escrita em outra língua", asked[2].Problems![0]);
    }

    [Fact]
    public async Task A_no_about_you_is_final_and_a_model_that_fails_passes_its_turn()
    {
        var statement = new FormQuestion("I confirm I hold a US security clearance", "confirm", []);
        var calls = new List<string>();
        var result = await Answerer.EscalateAsync(["ollama", "codex", "claude-max"], [statement], (model, _, _) =>
        {
            calls.Add(model);
            return Task.FromResult(model == "ollama" ? new LlmResult(false, null, "ollama", "", "", "tempo esgotado", 60) : Reply(model, (0, "No")));
        }, Checks);
        // The local model timed out, ChatGPT said no; Claude is not asked to turn that into a yes.
        Assert.Equal(["ollama", "codex"], calls);
        Assert.True(result.Single().Checked.Final);
        Assert.Null(result.Single().Checked.Value);
        Assert.Empty(await Answerer.EscalateAsync(["ollama"], [statement], (_, _, _) => Task.FromResult(new LlmResult(false, null, "ollama", "", "", "fora do ar", 0)), Checks));
    }

    [Fact]
    public async Task A_single_model_gets_a_second_try_with_the_reason()
    {
        var open = new FormQuestion("Walk us through a feature you owned end-to-end.", "textarea", []);
        var tries = 0;
        var result = await Answerer.EscalateAsync(["claude-max"], [open], (_, _, problems) =>
            Task.FromResult(++tries == 1 ? Reply("claude-max", (0, "I don't know.")) : Reply("claude-max", (0, "At Acme I owned the billing migration to .NET 8 and PostgreSQL."))), Checks);
        Assert.Equal(2, tries);
        Assert.NotNull(result.Single().Checked.Value);
    }
}
