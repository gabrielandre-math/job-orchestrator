using JobOrchestrator.Automation.Adapters;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The approved answer is found in the form's own language: Portuguese job text, English ATS questions.</summary>
public sealed class FormAnswerTests
{
    [Theory]
    [InlineData("Yes", "Sim", true)]
    [InlineData("No", "Não", true)]
    [InlineData("Sim", "Yes", true)]
    [InlineData(" yes ", "Yes", true)]
    [InlineData("I don't wish to answer", "Prefiro não informar", true)]
    [InlineData("No", "Sim", false)]
    [InlineData("Yes", "Não", false)]
    [InlineData("Maybe", "Sim", false)]
    public void An_option_is_the_same_answer_in_another_language(string option, string answer, bool same) =>
        Assert.Equal(same, LabelForm.SameAnswer(option, answer));
}
