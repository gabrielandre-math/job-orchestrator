using System.Text.RegularExpressions;

namespace JobOrchestrator.Profile.Import;

/// <summary>
/// Export do LinkedIn sem IA: cada linha dos CSVs já é estruturada, então vira fato verificado com a evidência
/// "arquivo, linha N". Tecnologias de uma experiência são as skills do próprio export que aparecem no texto dela.
/// </summary>
internal static partial class LinkedInFacts
{
    public static (List<VerifiedFact> Facts, Identity Identity) Map(IReadOnlyDictionary<string, IReadOnlyList<string[]>> tables, Guid profileId, Guid documentId, DateTime now)
    {
        var facts = new List<VerifiedFact>();
        var position = 0;
        VerifiedFact Add(string kind, string statement, string file, int line, string quote, Guid? parent = null, string? org = null, string? role = null,
            string? start = null, string? end = null, IEnumerable<string>? tech = null, string? level = null)
        {
            var f = VerifiedFact.Create(profileId, documentId, kind, statement, FactStatus.Verified, position++, now, parent, org, role, start, end, tech, level,
                quote, null, $"{file}, linha {line}");
            facts.Add(f);
            return f;
        }

        var skills = Rows(tables, "Skills.csv").Select(r => r.Get("Name")).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var profile = Rows(tables, "Profile.csv").FirstOrDefault();
        var name = profile is null ? "" : $"{profile.Get("First Name")} {profile.Get("Last Name")}".Trim();
        if (profile?.Get("Headline") is { Length: > 0 } headline) Add("headline", headline, "Profile.csv", profile.Line, headline);
        if (profile?.Get("Summary") is { Length: > 0 } summary) Add("summary", summary, "Profile.csv", profile.Line, summary);
        var websites = profile?.Get("Websites") ?? "";
        var email = Rows(tables, "Email Addresses.csv").OrderByDescending(r => r.Get("Primary") == "Yes").Select(r => r.Get("Email Address")).FirstOrDefault(e => e.Length > 0);
        var phone = Rows(tables, "PhoneNumbers.csv").Select(r => r.Get("Number")).FirstOrDefault(p => p.Length > 0);
        var identity = new Identity(name, email, phone, Blank(profile?.Get("Geo Location")), null,
            Site().Matches(websites).Select(m => m.Value).FirstOrDefault(u => u.Contains("github.com", StringComparison.OrdinalIgnoreCase)),
            Site().Matches(websites).Select(m => m.Value).FirstOrDefault(u => !u.Contains("github.com", StringComparison.OrdinalIgnoreCase)));

        foreach (var r in Rows(tables, "Positions.csv"))
        {
            var company = r.Get("Company Name");
            var title = r.Get("Title");
            if (company.Length == 0 || title.Length == 0) continue;
            var description = r.Get("Description");
            var text = $"{title} {description}";
            var tech = skills.Where(s => Evidence.Fold(text).Contains(Evidence.Fold(s), StringComparison.Ordinal)).ToList();
            var job = Add("experience", $"{title}, {company}", "Positions.csv", r.Line, $"{company}, {title}, {r.Get("Started On")} – {r.Get("Finished On")}",
                org: company, role: title, start: Month(r.Get("Started On")), end: Month(r.Get("Finished On")), tech: tech);
            foreach (var bullet in Bullets(description))
                Add("achievement", bullet, "Positions.csv", r.Line, bullet, job.Id, tech: tech.Where(t => Evidence.Fold(bullet).Contains(Evidence.Fold(t), StringComparison.Ordinal)));
        }
        foreach (var r in Rows(tables, "Education.csv"))
        {
            var school = r.Get("School Name");
            if (school.Length == 0) continue;
            var degree = r.Get("Degree Name");
            Add("education", degree.Length > 0 ? $"{degree}, {school}" : school, "Education.csv", r.Line, string.Join(", ", new[] { school, degree, r.Get("End Date") }.Where(v => v.Length > 0)),
                org: school, role: Blank(degree), start: Month(r.Get("Start Date")), end: Month(r.Get("End Date")));
        }
        foreach (var s in skills) Add("skill", s, "Skills.csv", 0, s);
        foreach (var r in Rows(tables, "Languages.csv"))
        {
            var lang = r.Get("Name");
            if (lang.Length == 0) continue;
            var level = Level(r.Get("Proficiency"));
            Add("language", level == "unknown" ? lang : $"{lang} ({FactExtractor.LevelPt(level)})", "Languages.csv", r.Line, $"{lang}, {r.Get("Proficiency")}", level: level);
        }
        foreach (var r in Rows(tables, "Certifications.csv"))
        {
            var cert = r.Get("Name");
            if (cert.Length == 0) continue;
            var authority = r.Get("Authority");
            Add("certification", authority.Length > 0 ? $"{cert} ({authority})" : cert, "Certifications.csv", r.Line, cert, org: Blank(authority), end: Month(r.Get("Started On")));
        }
        foreach (var r in Rows(tables, "Projects.csv"))
        {
            var project = r.Get("Title");
            if (project.Length == 0) continue;
            var description = r.Get("Description");
            Add("project", description.Length > 0 ? $"{project}: {description}" : project, "Projects.csv", r.Line, project,
                tech: skills.Where(s => Evidence.Fold(description).Contains(Evidence.Fold(s), StringComparison.Ordinal)));
        }
        return (facts, identity);
    }

    private sealed record Row(string[] Header, string[] Cells, int Line)
    {
        public string Get(string column)
        {
            var i = Array.FindIndex(Header, h => string.Equals(h.Trim(), column, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i < Cells.Length ? Cells[i].Trim() : "";
        }
    }

    private static IEnumerable<Row> Rows(IReadOnlyDictionary<string, IReadOnlyList<string[]>> tables, string file)
    {
        if (!tables.TryGetValue(file, out var rows) || rows.Count < 2) yield break;
        for (var i = 1; i < rows.Count; i++) yield return new Row(rows[0], rows[i], i + 1);
    }

    /// <summary>"Mar 2022", "mar. de 2022", "2022" viram 2022-03 ou 2022.</summary>
    internal static string? Month(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var year = YearOf().Match(raw);
        if (!year.Success) return null;
        var folded = Claims.Fold(raw);
        string[][] names = [["jan"], ["feb", "fev"], ["mar"], ["apr", "abr"], ["may", "mai"], ["jun"], ["jul"], ["aug", "ago"], ["sep", "set"], ["oct", "out"], ["nov"], ["dec", "dez"]];
        for (var m = 0; m < 12; m++)
            if (names[m].Any(n => folded.StartsWith(n, StringComparison.Ordinal))) return $"{year.Value}-{m + 1:00}";
        return year.Value;
    }

    private static string Level(string proficiency)
    {
        var p = Claims.Fold(proficiency);
        return p.Contains("native") || p.Contains("nativ") || p.Contains("bilingu") ? "native"
            : p.Contains("full professional") || p.Contains("completa") ? "fluent"
            : p.Contains("professional working") || p.Contains("profissional") ? "advanced"
            : p.Contains("limited") || p.Contains("limitad") ? "intermediate"
            : p.Contains("elementary") || p.Contains("elementar") ? "basic" : "unknown";
    }

    private static IEnumerable<string> Bullets(string description) =>
        description.Split('\n').Select(l => l.Trim().TrimStart('-', '•', '*', '·', ' ').Trim()).Where(l => l.Length >= 15);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"\d{4}")] private static partial Regex YearOf();
    [GeneratedRegex(@"https?://[^\s\],]+")] private static partial Regex Site();
}
