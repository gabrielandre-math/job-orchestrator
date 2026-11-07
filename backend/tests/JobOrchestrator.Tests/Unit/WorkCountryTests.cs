using JobOrchestrator.Applications.Features.Workflow;
using JobOrchestrator.Profile.Contracts;
using JobOrchestrator.Profile.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The work authorization answer follows the job's own country; an unknown country is never answered as Brazil.</summary>
public sealed class WorkCountryTests
{
    [Theory]
    [InlineData("Toronto, Canada", null, "CA")]
    [InlineData("London, United Kingdom", null, "GB")]
    [InlineData("Berlin, Germany", null, "DE")]
    [InlineData("São Paulo, Brasil", null, "BR")]
    [InlineData("Jundiaí - SP", null, "BR")]
    [InlineData("Natal/RN", null, "BR")]
    [InlineData("", "BRL", "BR")]
    [InlineData("Remote (US)", null, "US")]
    [InlineData("New York, NY", null, "US")]
    [InlineData("Remote", null, null)]
    [InlineData("Remote - anywhere", "USD", null)]
    [InlineData("Remote - US or Canada", null, null)]
    [InlineData("Pittsburgh, PA", null, null)]
    [InlineData("Lisbon or Madrid", null, null)]
    public void The_job_country_is_one_country_or_unknown(string location, string? currency, string? country) =>
        Assert.Equal(country, Bundles.Country(location, currency));

    private static readonly ProfileSnapshot Profile = new(Guid.NewGuid(), 1, "h", false, true, new Identity("Bruno Teste", "b@example.com", null, null, null, null, null), [],
        Confirmations.Empty with { AuthorizedCountries = ["BR"], ConfirmedAt = DateTime.UtcNow });

    private static IReadOnlyDictionary<string, string> Answers(string? country) =>
        AnswerBank.Build(Profile, new AnswerContext(null, null, country, [], "en", "greenhouse"), new DateOnly(2026, 11, 7));

    [Fact] public void Brazil_is_answered_yes() => Assert.Equal("Yes", Answers("BR")["work_authorization"]);

    [Fact] public void A_foreign_country_is_answered_no() => Assert.Equal("No", Answers("CA")["work_authorization"]);

    [Fact]
    public void An_unknown_country_leaves_both_questions_for_you()
    {
        var a = Answers(null);
        Assert.False(a.ContainsKey("work_authorization"));
        Assert.False(a.ContainsKey("needs_sponsorship"));
    }
}
