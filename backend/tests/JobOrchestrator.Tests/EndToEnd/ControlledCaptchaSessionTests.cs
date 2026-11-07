using System.Net;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>HTTP against the private reference server; arithmetic challenges are controlled integration tests, not production-provider proof.</summary>
[Trait("Category", "E2E")]
public sealed class ControlledCaptchaSessionTests
{
    [Fact]
    public async Task Receipt_requires_valid_answer_same_session_same_payload_and_unexpired_challenge()
    {
        var time = new Clock();
        using var provider = new HttpClient();
        var server = new ControlledCaptchaServer(new("math", "", "", false, null), provider, time);
        await using var site = await TestSite.StartAsync(server.Map);
        using var owner = new HttpClient(new HttpClientHandler { UseProxy = false, CookieContainer = new() });
        using var stranger = new HttpClient(new HttpClientHandler { UseProxy = false, CookieContainer = new() });
        var page = await owner.GetAsync(site.Url + "/controlled-test");
        Assert.Contains("httponly", string.Join(';', page.Headers.GetValues("Set-Cookie")).ToLowerInvariant());
        var html = await page.Content.ReadAsStringAsync();
        var id = Regex.Match(html, "name=\"testId\" value=\"([^\"]+)\"").Groups[1].Value;
        var expression = Regex.Match(html, @"data-jo-captcha-expression>(\d+) \+ (\d+)");
        var answer = int.Parse(expression.Groups[1].Value) + int.Parse(expression.Groups[2].Value);
        var fields = new Dictionary<string, string> { ["testId"] = id, ["name"] = "Synthetic test", ["email"] = "candidate@example.test", ["answer"] = "wrong" };
        var endpoint = site.Url + "/controlled-test/submit";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(endpoint, new FormUrlEncodedContent(fields))).StatusCode);
        fields["answer"] = answer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.PostAsync(endpoint, new FormUrlEncodedContent(fields))).StatusCode);
        var accepted = await owner.PostAsync(endpoint, new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var receipt = await accepted.Content.ReadAsStringAsync();
        Assert.Contains("data-jo-receipt", receipt);
        Assert.Equal(receipt, await (await owner.PostAsync(endpoint, new FormUrlEncodedContent(fields))).Content.ReadAsStringAsync());
        fields["email"] = "another@example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(endpoint, new FormUrlEncodedContent(fields))).StatusCode);
        fields["email"] = "candidate@example.test";
        Assert.Equal(HttpStatusCode.BadRequest, (await stranger.PostAsync(endpoint, new FormUrlEncodedContent(fields))).StatusCode);
        time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync(endpoint, new FormUrlEncodedContent(fields))).StatusCode);
    }

    [Fact]
    public async Task Expired_unused_challenge_is_rejected_even_with_the_correct_answer()
    {
        var time = new Clock();
        using var provider = new HttpClient();
        var server = new ControlledCaptchaServer(new("math", "", "", false, null), provider, time);
        await using var site = await TestSite.StartAsync(server.Map);
        using var owner = new HttpClient(new HttpClientHandler { UseProxy = false, CookieContainer = new() });
        var html = await owner.GetStringAsync(site.Url + "/controlled-test");
        var id = Regex.Match(html, "name=\"testId\" value=\"([^\"]+)\"").Groups[1].Value;
        var expression = Regex.Match(html, @"data-jo-captcha-expression>(\d+) \+ (\d+)");
        time.Advance(TimeSpan.FromMinutes(6));
        var response = await owner.PostAsync(site.Url + "/controlled-test/submit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["testId"] = id, ["name"] = "Synthetic test", ["email"] = "candidate@example.test",
            ["answer"] = (int.Parse(expression.Groups[1].Value) + int.Parse(expression.Groups[2].Value)).ToString(System.Globalization.CultureInfo.InvariantCulture)
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("data-jo-receipt", await response.Content.ReadAsStringAsync());
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 7, 18, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan duration) => now += duration;
    }
}
