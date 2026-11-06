using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace JobOrchestrator.Mail.Features;

/// <summary>
/// Certificate check for SMTP and IMAP. MailKit rejects a certificate whose revocation check did not finish, and macOS often
/// cannot finish it, so Gmail's valid certificate was refused. Like browsers, only an unknown revocation status is tolerated:
/// revoked, expired, untrusted or wrong-host certificates still fail.
/// </summary>
internal static class MailTls
{
    private const X509ChainStatusFlags Unknown = X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

    public static T Secure<T>(T client) where T : MailKit.MailService
    {
        client.ServerCertificateValidationCallback = (_, _, chain, errors) => Accept(errors, chain?.ChainStatus.Select(s => s.Status) ?? []);
        return client;
    }

    internal static bool Accept(SslPolicyErrors errors, IEnumerable<X509ChainStatusFlags> chain)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors) return false;
        var statuses = chain.ToList();
        return statuses.Count > 0 && statuses.All(s => (s & ~Unknown) == 0);
    }
}
