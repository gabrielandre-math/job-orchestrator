using JobOrchestrator.Applications.Features;
using JobOrchestrator.Automation.Adapters;
using JobOrchestrator.Profile.Contracts;
using JobOrchestrator.Profile.Domain;

namespace JobOrchestrator.Tests.Unit;
public sealed class FormOnboardingTests
{
    [Theory]
    [InlineData("Data de nascimento*")]
    [InlineData("Date of birth")]
    [InlineData("Birthdate")]
    [InlineData("DOB")]
    [InlineData("Fecha de nacimiento")]
    public void Birth_date_aliases_reuse_only_user_approved_answers(string label)
    {
        var b = new MaterialBundle("Ana", "Lima", "ana@example.test", "123", "", "cv.pdf",
            new Dictionary<string, string> { [CustomAnswer.Key("Data de nascimento")] = "2000-02-29", [CustomAnswer.AiKey(label)] = "1999-01-01" });
        Assert.Equal(("2000-02-29", "sua resposta pessoal"), LabelForm.Resolve(label, "date", b));
        Assert.Null(LabelForm.Resolve(label, "date", b with { Answers = new Dictionary<string, string> { [CustomAnswer.AiKey(label)] = "1999-01-01" } }).Value);
    }
    [Theory]
    [InlineData("Data de nascimento", "date", "", "2000-02-29")]
    [InlineData("Data de nascimento", "text", "DD/MM/AAAA", "29/02/2000")]
    [InlineData("Date of birth", "text", "MM/DD/YYYY", "02/29/2000")]
    [InlineData("Date of birth", "text", "YYYY-MM-DD", "2000-02-29")]
    public void Date_controls_receive_the_requested_format(string label, string type, string placeholder, string expected) =>
        Assert.Equal(expected, PersonalAnswer.FormatBirthDate("2000-02-29", label, type, placeholder));
    [Theory]
    [InlineData("2001-02-29")]
    [InlineData("29/02/2000")]
    [InlineData("2099-01-01")]
    [InlineData("1800-01-01")]
    public void Invalid_dates_are_not_approved(string value) =>
        Assert.False(new SaveFormAnswersValidator(TimeProvider.System).Validate(new SaveFormAnswersCommand([new("Data de nascimento", value)])).IsValid);
    [Fact]
    public void Form_preferences_cannot_confirm_inferred_cv_claims()
    {
        var now = DateTime.UtcNow;
        var p = CandidateProfile.Create(now);
        p.SetIdentity(new("Ana Lima", "ana@example.test", null, null, null, null, null), now);
        var doc = p.AddDocument("cv.txt", "txt", "test", "/tmp/cv.txt", "Teste", 1, now);
        p.ReplaceFacts(doc, [VerifiedFact.Create(p.Id, doc.Id, "skill", "Python", FactStatus.Inferred, 0, now)], p.Identity, "test", now);
        p.SetFormPreferences(Confirmations.Empty with { FloorCltBrl = 5000 }, now);
        Assert.Equal(5000, p.Confirmations.FloorCltBrl);
        Assert.Equal(now, p.Confirmations.ConfirmedAt);
        Assert.Equal(FactStatus.Inferred, Assert.Single(p.Facts).Status);
        Assert.True(p.Pending);
        Assert.Null(p.ConfirmedAt);
    }
}
