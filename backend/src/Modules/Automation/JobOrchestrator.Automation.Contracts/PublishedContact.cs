using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Contracts;

public sealed record PublishedContact(string? Email, string Status, string? Evidence);

public static partial class PublishedContactExtractor
{
    public static PublishedContact From(string text)
    {
        var found = new List<(string Email, string Evidence)>();
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var context = (i > 0 ? lines[i - 1] + "\n" : "") + lines[i];
            if (!ApplyIntent().IsMatch(context) || Negative().IsMatch(context)) continue;
            foreach (Match match in Email().Matches(lines[i]))
                if (!Excluded().IsMatch(match.Value)) found.Add((match.Value.ToLowerInvariant(), context.Trim()));
        }
        var distinct = found.DistinctBy(x => x.Email).ToArray();
        return distinct.Length switch
        {
            1 => new(distinct[0].Email, "published_for_application", distinct[0].Evidence[..Math.Min(600, distinct[0].Evidence.Length)]),
            > 1 => new(null, "ambiguous", null),
            _ => new(null, "not_published", null)
        };
    }
    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)] private static partial Regex Email();
    [GeneratedRegex(@"candidat|curr[ií]culo|\bcv\b|\bresume\b|\bto apply\b|\bhow to apply\b|\bapply (?:at|via|by|to)\b|send (?:your )?application|recrutamento|recruitment", RegexOptions.IgnoreCase)] private static partial Regex ApplyIntent();
    [GeneratedRegex(@"n[aã]o (?:envie|enviar|mande|aceitamos)|do not (?:send|email)|don.t (?:send|email)", RegexOptions.IgnoreCase)] private static partial Regex Negative();
    [GeneratedRegex(@"^(?:privacy|privacidade|dpo|lgpd|no-?reply|support|suporte|abuse|security|legal|billing|sac)@", RegexOptions.IgnoreCase)] private static partial Regex Excluded();
}
