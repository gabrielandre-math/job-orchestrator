using JobOrchestrator.Applications.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace JobOrchestrator.Tests.Unit;

/// <summary>The browser copilot: only the extension talks to its routes, and only the jobs opened from the app are filled on their own.</summary>
public sealed class CopilotTests
{
    private static HttpContext From(string? origin)
    {
        var http = new DefaultHttpContext();
        if (origin is not null) http.Request.Headers.Origin = origin;
        return http;
    }

    private static readonly IConfiguration None = new ConfigurationBuilder().Build();

    [Fact]
    public void Only_the_extension_with_the_fixed_id_is_trusted()
    {
        Assert.True(CopilotExtension.From(From($"chrome-extension://{CopilotExtension.Id}"), None));
        Assert.False(CopilotExtension.From(From("chrome-extension://abcdefghijklmnopabcdefghijklmnop"), None));
        Assert.False(CopilotExtension.From(From("https://evil.example"), None));
        Assert.False(CopilotExtension.From(From(null), None));
        var store = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Copilot:ExtensionIds"] = "storebuildidstorebuildidstorebui" }).Build();
        Assert.True(CopilotExtension.From(From("chrome-extension://storebuildidstorebuildidstorebui"), store));
    }

    [Fact]
    public void A_page_matches_the_job_opened_from_the_app_for_fifteen_minutes()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero));
        var state = new CopilotState(clock);
        var job = Guid.NewGuid();
        state.Expect(job, "https://apply.workable.com/acme/j/ABC123/");
        Assert.Equal(job, state.Match("https://apply.workable.com/acme/j/ABC123/apply/"));
        Assert.Null(state.Match("https://apply.workable.com/other/j/XYZ/"));
        Assert.Null(state.Match("https://evil.example/acme/j/ABC123/"));
        clock.Advance(TimeSpan.FromMinutes(16));
        Assert.Null(state.Match("https://apply.workable.com/acme/j/ABC123/apply/"));
    }

    [Fact]
    public void The_extension_that_answered_lately_counts_as_connected()
    {
        var clock = new ManualClock(new DateTimeOffset(2026, 11, 8, 3, 0, 0, TimeSpan.Zero));
        var state = new CopilotState(clock);
        Assert.False(state.View().Connected);
        state.Seen("1.2.0");
        Assert.True(state.View().Connected);
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(state.View().Connected);
    }

    [Fact]
    public void What_the_robot_filled_comes_back_label_by_label_without_the_resume_file()
    {
        var json = """[{"label":"Phone","value":"+55 21 90000-0000","source":"perfil"},{"label":"Resume","value":"cv.pdf (sha256 abcdef123456)","source":"currículo"},{"label":"","value":"x","source":""}]""";
        var prepared = Copilot.Prepared(json);
        Assert.Single(prepared);
        Assert.Equal(("Phone", "+55 21 90000-0000"), (prepared[0].Question, prepared[0].Answer));
        Assert.Empty(Copilot.Prepared("not json"));
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;
        public override DateTimeOffset GetUtcNow() => current;
        public void Advance(TimeSpan by) => current += by;
    }
}
