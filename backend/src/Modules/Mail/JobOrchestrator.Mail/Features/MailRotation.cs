using System.Text.RegularExpressions;
using JobOrchestrator.Mail.Data;
using MailKit.Net.Smtp;

namespace JobOrchestrator.Mail.Features;

/// <summary>
/// Which account sends the next e-mail. A follow-up leaves from the account of its first e-mail, so it stays in the same thread;
/// anything else goes through the sending account with the fewest e-mails today, under the per-account daily cap.
/// </summary>
internal static partial class MailRotation
{
    public static MailAccount? Pick(IReadOnlyList<MailAccount> accounts, IReadOnlyDictionary<Guid, int> sentToday, int capPerAccount, Guid? sameAs, DateTime now)
    {
        bool Ready(MailAccount a) => a.Sends && (a.PausedUntil is null || a.PausedUntil <= now) && sentToday.GetValueOrDefault(a.Id) < capPerAccount;
        if (sameAs is { } id && accounts.FirstOrDefault(a => a.Id == id) is { } owner) return Ready(owner) ? owner : null;
        return accounts.Where(Ready).OrderBy(a => sentToday.GetValueOrDefault(a.Id)).ThenBy(a => a.CreatedAt).FirstOrDefault();
    }

    /// <summary>The provider says this account used up its sending for now (Gmail 550 5.4.5, 421 4.7.0): a limit, not a bad e-mail.</summary>
    public static bool QuotaHit(Exception ex) =>
        (ex is SmtpCommandException or SmtpProtocolException) && Quota().IsMatch(ex.Message) && !ex.Message.Contains("size", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"5\.4\.5|4\.7\.0|4\.7\.28|quota|limit exceeded|rate limit|too many", RegexOptions.IgnoreCase)]
    private static partial Regex Quota();
}
