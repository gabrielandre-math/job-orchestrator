using JobOrchestrator.Applications.Features;
using JobOrchestrator.Automation.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>
/// The questions window lists meanings, not form texts: every declaration is one consent, the same question in two languages is one
/// line, and one answer is written the way each form takes it.
/// </summary>
public sealed class FormQuestionGroupsTests
{
    [Fact]
    public void Declarations_birth_dates_and_the_same_question_in_two_languages_are_one_line()
    {
        Assert.Equal("consent", FormQuestions.GroupKey(new("Li e aceito a Política de Privacidade", "declaration", []), null));
        Assert.Equal("consent", FormQuestions.GroupKey(new("I consent to the processing of my personal data", "declaration", []), null));
        Assert.Equal("birth_date", FormQuestions.GroupKey(new("Date of birth", "text", []), null));
        Assert.Equal("current_pay", FormQuestions.GroupKey(new("Qual o seu salário atual?", "text", []), null));
        var en = new FormQuestion("Are you willing to travel?", "radio", ["Yes", "No"]);
        var pt = new FormQuestion("Você tem disponibilidade para viajar?", "select", ["Sim", "Não"]);
        Assert.Equal(FormQuestions.GroupKey(en, "Disponibilidade para viajar"), FormQuestions.GroupKey(pt, "Disponibilidade para viajar"));
        // Choices join only when they offer the same options.
        Assert.NotEqual(FormQuestions.GroupKey(new("Nível de inglês", "radio", ["Básico", "Avançado"]), "Nível de inglês"),
            FormQuestions.GroupKey(new("English level", "radio", ["B1", "C1"]), "Nível de inglês"));
    }

    [Fact]
    public void One_yes_becomes_each_forms_own_option_and_a_declaration_you_refuse_has_no_value()
    {
        static QuestionItem Item(FormQuestion q) => new("k", q.Label, q.Kind, q.Options, []);
        var yesno = new PendingGroup("yesno:x", "yesno", "x", "", ["Sim", "Não"], [], [], [], null, null);
        Assert.Equal("Yes", FormQuestions.Value(yesno, Item(new("Travel?", "radio", ["Yes", "No"])), "Sim"));
        Assert.Equal("Não", FormQuestions.Value(yesno, Item(new("Viajar?", "select", ["Sim", "Não"])), "Não"));
        Assert.Null(FormQuestions.Value(yesno, Item(new("I hold a clearance", "confirm", [])), "Não"));
        var consent = yesno with { Key = "consent", Kind = "consent" };
        Assert.Equal("Sim", FormQuestions.Value(consent, Item(new("Li e aceito", "declaration", [])), "Sim"));
        Assert.Null(FormQuestions.Value(consent, Item(new("Li e aceito", "declaration", [])), "Não"));
    }
}
