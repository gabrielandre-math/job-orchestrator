using JobOrchestrator.Materials.Compose;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A cross review only counts when every claim of the draft got exactly one verdict.</summary>
public sealed class ReviewCoverageTests
{
    private static readonly Draft Two = new(new Claim("Engenheiro de software .NET", ["F1"]), null,
        [new DraftExperience("F1", "Engenheiro de Software", [new Claim("APIs de pagamento em .NET", ["F1"])])], [], [], []);

    private static Prompts.Verdict V(string claim) => new(claim, true, "");

    [Fact]
    public void An_empty_list_is_no_review() => Assert.NotNull(Conferente.Gap(Two, []));

    [Fact]
    public void A_claim_left_without_verdict_is_no_review() => Assert.NotNull(Conferente.Gap(Two, [V("C1")]));

    [Fact]
    public void A_claim_judged_twice_is_no_review() => Assert.NotNull(Conferente.Gap(Two, [V("C1"), V("C1"), V("C2")]));

    [Fact]
    public void A_verdict_for_a_claim_that_does_not_exist_is_no_review() => Assert.NotNull(Conferente.Gap(Two, [V("C1"), V("C2"), V("C9")]));

    [Fact]
    public void One_verdict_per_claim_is_a_review() => Assert.Null(Conferente.Gap(Two, [V("C2"), V("C1")]));

    [Fact]
    public void Provenance_follows_the_draft_that_went_out()
    {
        var fact = new JobOrchestrator.Profile.Contracts.ProfileFact(Guid.NewGuid(), "experience", "Engenheiro de software na Acme, com APIs de pagamento em .NET.", null, "Acme",
            "Engenheiro de Software", "2024-01", null, [".NET"], null, "cv");
        var s = new Selection([new Picked("F1", fact, null)], "pt", [], []);
        var bullet = new Claim("APIs de pagamento em .NET na Acme.", ["F1"]);
        var first = new Draft(null, null, [new DraftExperience("F1", "Engenheiro de Software", [bullet, new Claim("Engenheiro de software na Acme.", ["F1"])])], [], [], []);
        var cut = first with { Experiences = [first.Experiences[0] with { Bullets = [bullet] }] };

        Assert.Equal(2, Conferente.Check(first, s).Lines.Count(l => l.Section == "bullet"));
        Assert.Equal(["APIs de pagamento em .NET na Acme."], Conferente.Check(cut, s).Lines.Where(l => l.Section == "bullet").Select(l => l.Text));
    }
}
