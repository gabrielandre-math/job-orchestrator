using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Automation.Routing;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A job page that never opened says why: a dead domain is final, a site that did not answer is tried later.</summary>
public sealed class RouterUnreachableTests
{
    [Fact]
    public void Dead_domain_is_final_and_a_silent_site_is_tried_later()
    {
        var dead = Router.Unreachable<PreparedForm>("net::ERR_NAME_NOT_RESOLVED at https://www.examplejobs.test/vagas/1");
        Assert.Equal((AdapterResultKind.PermanentFailure, "site-unreachable"), (dead!.Kind, dead.ReasonCode));
        var down = Router.Unreachable<PreparedForm>("net::ERR_CONNECTION_REFUSED at https://jobs.example.com");
        Assert.Equal((AdapterResultKind.RetryableFailure, "site-down"), (down!.Kind, down.ReasonCode));
        Assert.Null(Router.Unreachable<PreparedForm>("Timeout 15000ms exceeded."));
    }
}
