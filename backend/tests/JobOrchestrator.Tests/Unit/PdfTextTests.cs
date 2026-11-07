using JobOrchestrator.Materials.Compose;

namespace JobOrchestrator.Tests.Unit;

/// <summary>
/// PdfPig (macOS and Windows) hands ligatures over as one character, poppler (Linux) splits them. On the Mac every resume with "fi"
/// failed the inspector ("Afirmações presentes no texto: 14 de 19") and nothing was ever sent.
/// </summary>
public sealed class PdfTextTests
{
    [Fact]
    public void Ligatures_read_as_the_letters_they_stand_for() =>
        Assert.Equal("profissionais unifica definir fluxo", PdfCheck.Readable("proﬁssionais uniﬁca deﬁnir ﬂuxo"));

    [Fact]
    public void The_resume_asks_the_pdf_for_no_ligatures() =>
        Assert.Contains("font-variant-ligatures: none", Template.Render(
            new Draft(null, null, [], [], [], []), new Selection([], "pt", [], []), "pt", "classico"));
}
