using System.Text.Json;
using JobOrchestrator.Jobs.Domain;
using JobOrchestrator.Jobs.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The snapshot column takes JSON only: an RSS item or a page goes in as a string, and the NUL that Postgres refuses comes out.</summary>
public sealed class SnapshotJsonTests
{
    [Theory]
    [InlineData("""{"id":1,"title":"Dev .NET"}""")]
    [InlineData("<item><title>Senior .NET Developer</title></item>")]
    [InlineData("Plain text from a careers page")]
    [InlineData("""{"title":"Dev\u0000 .NET"}""")]
    public void Any_payload_becomes_valid_json(string raw)
    {
        var json = RunRequestedConsumer.AsJson(raw);
        using var doc = JsonDocument.Parse(json);
        Assert.DoesNotContain("\\u0000", json, StringComparison.Ordinal);
        Assert.DoesNotContain('\0', json);
    }

    [Fact]
    public void Two_long_ids_that_share_their_start_stay_two_jobs()
    {
        // Himalayas, 07/11: two jobs of the same company whose URLs differ only after the 120th character.
        const string start = "https://himalayas.app/companies/waits-software-und-prozessberatungsgesellschaft-mbh/jobs/senior-ki-ai-c-net-asp-net-entw";
        var one = JobPosting.Key(start + "ickler-m-w-d");
        var two = JobPosting.Key(start + "icklerin-remote");
        Assert.NotEqual(one, two);
        Assert.All(new[] { one, two }, k => Assert.True(k.Length <= 120));
        Assert.Equal("gh-123", JobPosting.Key("gh-123"));
        Assert.Equal(one, JobPosting.Key(start + "ickler-m-w-d"));
    }

    [Fact]
    public void Valid_json_is_kept_as_it_came() =>
        Assert.Equal("""{"id":1}""", RunRequestedConsumer.AsJson("""{"id":1}"""));
}
