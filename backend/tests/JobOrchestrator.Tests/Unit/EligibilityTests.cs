using JobOrchestrator.Jobs.Domain;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Elegibilidade de quem mora no Brasil: frases reais de anúncio e o que cada uma deve dar, com a prova guardada.</summary>
public sealed class EligibilityTests
{
    [Theory]
    [InlineData("Senior .NET Engineer", "Remote - LATAM", "We hire across Latin America.", "Remoto", "BR_ELIGIBLE_CONFIRMED")]
    [InlineData(".NET Developer", "São Paulo, SP", "Vaga híbrida, 3 dias no escritório.", "Híbrido", "BR_ELIGIBLE_CONFIRMED")]
    [InlineData("Backend Engineer", "Remote", "This is a fully remote role, work from anywhere in the world.", "Remoto", "BR_ELIGIBLE_CONFIRMED")]
    [InlineData("Backend Engineer", "Remote - US", "Join our team.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Software Development Engineer", "Remoto (India)", "Join our team.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Backend Engineer", "Remote, Germany", "Join our team.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Backend Engineer", "Remoto (Europa)", "Junte-se ao time.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Backend Engineer", "Remote - Brazil or Portugal", "Join our team.", "Remoto", "BR_ELIGIBLE_CONFIRMED")]
    [InlineData("Software Engineer", "Remote", "Candidates must be authorized to work in the United States.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Engineer", "Remote", "US citizens only. Security clearance required.", "Remoto", "BR_RESTRICTED")]
    [InlineData("Backend Engineer", "Remote", "Worldwide remote. Applicants must reside in the US.", "Remoto", "REVIEW")]
    [InlineData("Platform Engineer", "Berlin, Germany", "We offer visa sponsorship and a relocation package.", "Presencial", "RELOCATION_WITH_SPONSORSHIP")]
    [InlineData("Platform Engineer", "Amsterdam, Netherlands", "Office-based team.", "Presencial", "RELOCATION_UNVERIFIED")]
    [InlineData("C# Developer", "Remote", "Long-term engagement as an independent contractor via Deel.", "Remoto", "BR_ELIGIBLE_POSSIBLE")]
    [InlineData("C# Developer", "Remote", "Great team, great product.", "Remoto", "BR_ELIGIBLE_POSSIBLE")]
    [InlineData("C# Developer", "", "Great team, great product.", null, "UNKNOWN")]
    public void Classifies_with_evidence(string title, string location, string description, string? modality, string expected)
    {
        var r = Eligibility.Classify(title, description, location, modality);
        Assert.Equal(expected, r.Class);
        Assert.False(string.IsNullOrWhiteSpace(r.Evidence));
    }

    [Fact]
    public void Unknown_is_never_promoted_and_evidence_quotes_the_posting()
    {
        var r = Eligibility.Classify("Engineer", "Remote is possible, but you must be legally authorized to work in Canada to apply.", "Remote", "Remoto");
        Assert.Equal("BR_RESTRICTED", r.Class);
        Assert.Contains("authorized to work in Canada", r.Evidence);
        Assert.True(r.Declared);
    }
}
