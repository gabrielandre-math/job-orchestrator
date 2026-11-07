using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A reply is tied to one of your e-mails only when that is certain.</summary>
public sealed class MailReplyLinkTests
{
    private static readonly Guid First = Guid.NewGuid(), Second = Guid.NewGuid(), Agency = Guid.NewGuid();
    private static readonly Dictionary<string, Guid> ById = new() { ["a1@acme.example"] = First, ["a2@acme.example"] = Second };
    private static readonly Dictionary<string, Guid?> Domains = new() { ["acme.example"] = null, ["talento.example"] = Agency };

    [Fact] public void The_thread_reference_wins() => Assert.Equal((Second, false), MailService.ReplyLink(ById, Domains, ["a2@acme.example"], "rh@acme.example"));
    [Fact] public void One_email_to_the_domain_is_the_link() => Assert.Equal((Agency, false), MailService.ReplyLink(ById, Domains, [], "ana@talento.example"));
    [Fact] public void Several_jobs_at_the_domain_are_ambiguous() => Assert.Equal((null, true), MailService.ReplyLink(ById, Domains, [], "rh@acme.example"));
    [Fact] public void An_unknown_sender_is_neither() => Assert.Equal((null, false), MailService.ReplyLink(ById, Domains, [], "oi@outra.example"));
}
