using JobOrchestrator.Automation.Features;
using Microsoft.Playwright;

namespace JobOrchestrator.Tests.Unit;

public sealed class LinkedInAuthenticationTests
{
    [Theory]
    [InlineData("li_at", ".linkedin.com", "session-value", -1, true)]
    [InlineData("li_at", "www.linkedin.com", "session-value", 2000000000, true)]
    [InlineData("li_at", ".linkedin.com", "session-value", 1, false)]
    [InlineData("li_at", ".linkedin.com", "", -1, false)]
    [InlineData("li_at", "evil-linkedin.com", "session-value", -1, false)]
    [InlineData("JSESSIONID", ".linkedin.com", "guest-cookie", -1, false)]
    public void A_public_page_or_guest_cookie_does_not_establish_a_login(string name, string domain, string value, float expires, bool expected)
    {
        var cookies = new[] { new BrowserContextCookiesResult { Name = name, Domain = domain, Value = value, Expires = expires } };
        Assert.Equal(expected, LinkedInSession.HasAuthentication(cookies, DateTimeOffset.FromUnixTimeSeconds(1800000000)));
    }
}
