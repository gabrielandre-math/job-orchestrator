using JobOrchestrator.Mail.Features;
using MailKit.Security;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A mail server goes without TLS only in a test sandbox and only on this machine.</summary>
public sealed class MailSocketTests
{
    [Fact]
    public void Tls_stays_unless_the_sandbox_asks_and_the_host_is_local()
    {
        var before = Environment.GetEnvironmentVariable("JO_MAIL_PLAIN_LOOPBACK");
        try
        {
            Environment.SetEnvironmentVariable("JO_MAIL_PLAIN_LOOPBACK", null);
            Assert.Equal(SecureSocketOptions.StartTls, MailService.Socket("127.0.0.1", SecureSocketOptions.StartTls));
            Environment.SetEnvironmentVariable("JO_MAIL_PLAIN_LOOPBACK", "1");
            Assert.Equal(SecureSocketOptions.None, MailService.Socket("127.0.0.1", SecureSocketOptions.StartTls));
            Assert.Equal(SecureSocketOptions.SslOnConnect, MailService.Socket("smtp.gmail.com", SecureSocketOptions.SslOnConnect));
        }
        finally { Environment.SetEnvironmentVariable("JO_MAIL_PLAIN_LOOPBACK", before); }
    }
}
