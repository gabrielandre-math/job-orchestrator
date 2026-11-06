using JobOrchestrator.Mail.Data;
using JobOrchestrator.Mail.Features;
using MailKit.Net.Smtp;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Several mail accounts take turns; follow-ups stay with the account of the first e-mail; a provider limit pauses an account.</summary>
public sealed class MailRotationTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);
    private static MailAccount Account(string address, int minutesOld, bool sends = true, DateTime? paused = null) =>
        new() { Address = address, CreatedAt = Now.AddMinutes(-minutesOld), Sends = sends, PausedUntil = paused };

    [Fact]
    public void The_account_with_fewer_sends_today_goes_next()
    {
        var a = Account("a@gmail.com", 30); var b = Account("b@gmail.com", 20);
        Assert.Same(b, MailRotation.Pick([a, b], new Dictionary<Guid, int> { [a.Id] = 3, [b.Id] = 1 }, 5, null, Now));
        Assert.Same(a, MailRotation.Pick([a, b], new Dictionary<Guid, int>(), 5, null, Now));
    }

    [Fact]
    public void Full_paused_or_read_only_accounts_are_skipped()
    {
        var full = Account("full@gmail.com", 40); var paused = Account("paused@gmail.com", 30, paused: Now.AddHours(3));
        var reader = Account("reader@gmail.com", 20, sends: false); var free = Account("free@gmail.com", 10);
        Assert.Same(free, MailRotation.Pick([full, paused, reader, free], new Dictionary<Guid, int> { [full.Id] = 5, [free.Id] = 4 }, 5, null, Now));
        Assert.Null(MailRotation.Pick([full, paused, reader], new Dictionary<Guid, int> { [full.Id] = 5 }, 5, null, Now));
        Assert.Same(paused, MailRotation.Pick([paused], new Dictionary<Guid, int>(), 5, null, Now.AddHours(4)));
    }

    [Fact]
    public void A_follow_up_waits_for_the_account_of_its_first_email()
    {
        var a = Account("a@gmail.com", 30); var b = Account("b@gmail.com", 20);
        Assert.Same(a, MailRotation.Pick([a, b], new Dictionary<Guid, int> { [a.Id] = 4 }, 5, a.Id, Now));
        Assert.Null(MailRotation.Pick([a, b], new Dictionary<Guid, int> { [a.Id] = 5 }, 5, a.Id, Now));
        Assert.Same(a, MailRotation.Pick([a, b], new Dictionary<Guid, int>(), 5, Guid.NewGuid(), Now));
    }

    [Fact]
    public void Provider_limits_are_told_apart_from_bad_emails()
    {
        Assert.True(MailRotation.QuotaHit(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.4.5 Daily user sending limit exceeded.")));
        Assert.True(MailRotation.QuotaHit(new SmtpProtocolException("The SMTP server has unexpectedly disconnected: 4.7.0 Try again later, closing connection.")));
        Assert.False(MailRotation.QuotaHit(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "5.1.1 The email account that you tried to reach does not exist.")));
        Assert.False(MailRotation.QuotaHit(new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ExceededStorageAllocation, "5.3.4 Message size exceeds fixed limit.")));
        Assert.False(MailRotation.QuotaHit(new IOException("Connection reset by peer")));
    }
}
