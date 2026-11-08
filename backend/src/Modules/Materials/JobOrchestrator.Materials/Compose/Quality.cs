using System.Text.RegularExpressions;

namespace JobOrchestrator.Materials.Compose;

/// <summary>
/// What makes a draft read badly before any fact check: parts in another language than the resume (an English resume with a Portuguese
/// summary or role titles), the candidate's own summary copied instead of one for this job, and bullets that only list technologies.
/// A draft with any of these goes to another model of the ladder, told what to fix.
/// </summary>
internal static partial class Quality
{
    public static IReadOnlyList<string> Problems(Draft d, Selection s)
    {
        var en = s.Language == "en";
        var problems = new List<string>();
        void Text(string where, string? text)
        {
            if (text is { } t && Words(t) >= 5 && Languages.PtOrEn(t) != (en ? "en" : "pt"))
                problems.Add($"the {where} is not in {(en ? "English" : "Portuguese")}: \"{Cut(t, 90)}\"");
        }
        Text("summary", d.Summary?.Text);
        foreach (var e in d.Experiences)
        {
            if ((en ? PortugueseTitle() : EnglishTitle()).IsMatch(e.Title)) problems.Add($"the role title \"{e.Title}\" is not translated");
            foreach (var b in e.Bullets)
            {
                Text("bullet", b.Text);
                if (OnlyTools().IsMatch(b.Text)) problems.Add($"this bullet only lists technologies; drop it or turn it into a result: \"{Cut(b.Text, 70)}\"");
            }
        }
        if (d.Headline is { } h && (en ? PortugueseTitle() : EnglishTitle()).IsMatch(h.Text)) problems.Add($"the headline \"{h.Text}\" is not translated");
        var own = s.Of("summary").Concat(s.Of("headline")).Select(f => Same(f.Fact.Statement)).ToHashSet();
        if (d.Summary is { } sum && own.Contains(Same(sum.Text))) problems.Add("the summary copies the candidate's own summary: write one for this job");
        return problems;
    }

    /// <summary>A role title or headline still in the other language ("Desenvolvedora .NET Sênior" in an English resume).</summary>
    public static bool Untranslated(string text, string language) => (language == "en" ? PortugueseTitle() : EnglishTitle()).IsMatch(text);

    /// <summary>The problems, for the next model's prompt.</summary>
    public static string Fix(IReadOnlyList<string> problems) =>
        $"\n\n<fix>\nThe last draft for this job had these problems. Write it again with every one fixed:\n{string.Join("\n", problems.Select(p => $"- {p}"))}\n</fix>";

    private static int Words(string t) => t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    private static string Cut(string t, int max) => t.Length <= max ? t : t[..max] + "…";
    private static string Same(string t) => Regex.Replace(Claims.Fold(t), @"[^\p{L}\p{N}]+", " ").Trim();

    [GeneratedRegex(@"(?i)\b(desenvolvedor|desenvolvedora|engenheir[oa]|analista|sênior|pleno|plena|júnior|estagi[aá]ri[oa]|coordenador|gerente)\b")] private static partial Regex PortugueseTitle();
    [GeneratedRegex(@"(?i)\b(developer|engineer|senior|junior|analyst|manager|intern|lead)\b")] private static partial Regex EnglishTitle();
    [GeneratedRegex(@"(?i)^\s*(trabalhei com|atuei com|utilizei|usei|worked with|used|experience with|experiência com)\b.{0,140},.{0,140}$")] private static partial Regex OnlyTools();
}
