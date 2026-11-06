using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using JobOrchestrator.Mail.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Certificate check for SMTP and IMAP: an unfinished revocation check passes, every real problem still fails.</summary>
public sealed class MailTlsTests
{
    private const SslPolicyErrors Chain = SslPolicyErrors.RemoteCertificateChainErrors;

    [Fact]
    public void Valid_certificate_passes() => Assert.True(MailTls.Accept(SslPolicyErrors.None, []));

    [Theory]
    [InlineData(X509ChainStatusFlags.RevocationStatusUnknown)]
    [InlineData(X509ChainStatusFlags.OfflineRevocation)]
    [InlineData(X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation)]
    public void Unknown_revocation_status_passes(X509ChainStatusFlags status) => Assert.True(MailTls.Accept(Chain, [status]));

    [Theory]
    [InlineData(X509ChainStatusFlags.Revoked)]
    [InlineData(X509ChainStatusFlags.NotTimeValid)]
    [InlineData(X509ChainStatusFlags.UntrustedRoot)]
    [InlineData(X509ChainStatusFlags.PartialChain)]
    [InlineData(X509ChainStatusFlags.NotSignatureValid)]
    public void Real_chain_problems_fail(X509ChainStatusFlags status)
    {
        Assert.False(MailTls.Accept(Chain, [status]));
        Assert.False(MailTls.Accept(Chain, [status, X509ChainStatusFlags.RevocationStatusUnknown]));
    }

    [Fact]
    public void Wrong_host_or_missing_certificate_fails()
    {
        Assert.False(MailTls.Accept(SslPolicyErrors.RemoteCertificateNameMismatch, []));
        Assert.False(MailTls.Accept(SslPolicyErrors.RemoteCertificateNameMismatch | Chain, [X509ChainStatusFlags.RevocationStatusUnknown]));
        Assert.False(MailTls.Accept(SslPolicyErrors.RemoteCertificateNotAvailable, []));
    }

    [Fact]
    public void Chain_error_without_details_fails() => Assert.False(MailTls.Accept(Chain, []));
}

/// <summary>A refused Gmail login reads as a login problem, not as a broken connection.</summary>
public sealed class MailLoginTests
{
    [Fact]
    public void Gmail_disconnect_after_bad_password_is_a_refused_login() =>
        Assert.True(MailService.RefusedLogin(new MailKit.Net.Smtp.SmtpProtocolException("The SMTP server has unexpectedly disconnected: 5.7.8 Username and Password not accepted.")));

    [Fact]
    public void Network_errors_are_not_refused_logins()
    {
        Assert.False(MailService.RefusedLogin(new IOException("Connection reset by peer")));
        Assert.False(MailService.RefusedLogin(new MailKit.Net.Smtp.SmtpProtocolException("The SMTP server has unexpectedly disconnected.")));
    }
}
