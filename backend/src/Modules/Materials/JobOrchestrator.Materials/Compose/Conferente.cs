namespace JobOrchestrator.Materials.Compose;

/// <summary>
/// Agente Conferente (sem IA): cada afirmação do rascunho precisa citar fatos que existem, do lugar certo, e não pode trazer
/// número nem termo próprio que esses fatos não tenham (Claims.Unsupported). O que não passa sai do currículo, com o motivo.
/// </summary>
internal static partial class Conferente
{
    public sealed record Outcome(Draft Draft, List<ProvenanceLine> Lines, List<string> Removed);

    public static Outcome Check(Draft d, Selection s)
    {
        var lines = new List<ProvenanceLine>();
        var removed = new List<string>();

        IReadOnlyList<string> Sources(IEnumerable<string> labels) =>
        [
            .. labels.Select(s.Get).OfType<Picked>().SelectMany(p => new[] { p.Fact.Statement, p.Fact.Organization ?? "", p.Fact.Role ?? "", string.Join(", ", p.Fact.Technologies) })
                .Where(x => x.Length > 0)
        ];

        // Título e resumo falam da pessoa inteira: skills, tecnologias, anos pelas datas, cargos que ela teve e a área que o trabalho mostra.
        // The hiring company may be named too ("to the Acme team"): it says where the person is going, not what they did.
        var stack = Profile(s);
        if (s.Company.Length > 0) stack.Add(s.Company);

        Claim? Single(Claim? c, string section, string? fallbackKind)
        {
            if (c is null || string.IsNullOrWhiteSpace(c.Text)) return Fallback(section, fallbackKind);
            var known = c.Facts.Where(l => s.Get(l) is not null).ToArray();
            var missing = known.Length == 0 ? ["nenhum fato citado"] : Claims.Unsupported(c.Text, [.. Sources(known), .. stack]);
            if (missing.Count == 0)
            {
                lines.Add(Line(section, null, c.Text, known, "ok"));
                return c with { Facts = known };
            }
            // A headline loses only the part without facts: "Full Stack Engineer (.NET + React)" with no React is "Full Stack Engineer",
            // followed by the job's keywords the person has, instead of a generic title that every resume would share.
            if (section == "headline" && known.Length > 0 && Trimmed(c.Text, missing, s) is { } trimmed
                && Claims.Unsupported(trimmed, [.. Sources(known), .. stack]).Count == 0)
            {
                removed.Add($"{section}: sem fato para {string.Join(", ", missing)}, saiu só esse pedaço");
                lines.Add(Line(section, null, trimmed, known, "ok, sem o pedaço recusado"));
                return c with { Text = trimmed, Facts = known };
            }
            // A summary keeps the sentences its facts support: one sentence with a word too many does not take the other two with it.
            if (section == "summary" && known.Length > 0)
            {
                var kept = Sentences(c.Text).Where(x => Claims.Unsupported(x, [.. Sources(known), .. stack]).Count == 0).ToList();
                if (kept.Count > 0 && string.Join(" ", kept).Split(' ').Length >= 8)
                {
                    var text = string.Join(" ", kept);
                    removed.Add($"{section}: frase sem fato para {string.Join(", ", missing)} saiu; o resto ficou");
                    lines.Add(Line(section, null, text, known, "ok, sem a frase recusada"));
                    return c with { Text = text, Facts = known };
                }
            }
            removed.Add($"{section}: \"{c.Text}\" (sem fato para {string.Join(", ", missing)})");
            return Fallback(section, fallbackKind);
        }

        // The CV's own headline or summary stands in only when it is in the resume's language: an English resume with a Portuguese summary
        // read as a mistake, and the same copied summary in every resume read as no tailoring at all. Otherwise the section stays out.
        Claim? Fallback(string section, string? kind)
        {
            if (kind is null || s.Of(kind).FirstOrDefault() is not { } f || Languages.PtOrEn(f.Fact.Statement) != (s.Language == "en" ? "en" : "pt")) return null;
            lines.Add(Line(section, null, f.Fact.Statement, [f.Label], "ok, texto original do fato"));
            return new Claim(f.Fact.Statement, [f.Label]);
        }

        var headline = Single(d.Headline, "headline", "headline");
        var summary = Single(d.Summary, "summary", "summary");

        var experiences = new List<DraftExperience>();
        foreach (var e in d.Experiences)
        {
            if (s.Get(e.Fact) is not { Fact.Kind: "experience" } job) { removed.Add($"experiência sem fato: \"{e.Title}\""); continue; }
            var allowed = s.Children(job.Label).Select(c => c.Label).Append(job.Label).ToHashSet();
            // O cargo vem sem a empresa: o template já põe a empresa ao lado.
            var proposed = job.Fact.Organization is { Length: > 0 } org ? e.Title.Replace($", {org}", "").Replace($" - {org}", "").Replace($" at {org}", "").Replace($" na {org}", "").Trim(' ', ',', '-') : e.Title;
            var title = proposed.Length > 0 && Claims.Unsupported(proposed, [job.Fact.Role ?? "", job.Fact.Statement]).Count == 0 ? proposed : job.Fact.Role ?? job.Fact.Statement;
            var bullets = new List<Claim>();
            foreach (var b in e.Bullets)
            {
                if (string.IsNullOrWhiteSpace(b.Text)) continue;
                var cited = b.Facts.Where(allowed.Contains).ToArray();
                var missing = cited.Length == 0 ? ["nenhum fato desta experiência citado"] : Claims.Unsupported(b.Text, Sources(cited.Append(job.Label)));
                if (missing.Count == 0)
                {
                    bullets.Add(b with { Facts = cited });
                    lines.Add(Line("bullet", job.Fact.Statement, b.Text, cited, "ok"));
                }
                else
                {
                    removed.Add($"{job.Fact.Statement}: \"{b.Text}\" (sem fato para {string.Join(", ", missing)})");
                    lines.Add(Line("bullet", job.Fact.Statement, b.Text, cited, $"saiu: sem fato para {string.Join(", ", missing)}"));
                }
            }
            experiences.Add(new DraftExperience(job.Label, title, [.. bullets]));
        }
        // Experiência que o Redator esqueceu volta com as conquistas originais: o currículo não perde emprego.
        foreach (var job in s.Of("experience").Where(j => experiences.All(x => x.Fact != j.Label)))
            experiences.Add(new DraftExperience(job.Label, job.Fact.Role ?? job.Fact.Statement, [.. s.Children(job.Label).Select(a => new Claim(a.Fact.Statement, [a.Label]))]));
        experiences = [.. experiences.OrderBy(x => s.Facts.ToList().FindIndex(p => p.Label == x.Fact))];

        var mine = s.Of("skill").Select(x => x.Fact.Statement).Concat(s.Of("experience").SelectMany(x => x.Fact.Technologies)).ToList();
        var skills = new List<string>();
        foreach (var skill in d.Skills.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            if (mine.FirstOrDefault(m => Claims.Fold(m) == Claims.Fold(skill)) is { } exact) skills.Add(exact);
            else removed.Add($"skill sem fato: {skill}");

        DraftLine[] Labels(DraftLine[] drafted, string kind) =>
        [
            .. s.Of(kind).Select(p =>
            {
                var mineLine = drafted.FirstOrDefault(x => x.Fact == p.Label);
                var ok = mineLine is not null && Claims.Numbers(Claims.Fold(mineLine.Text)).All(n => Claims.Numbers(Claims.Fold(p.Fact.Statement + " " + p.Fact.End + " " + p.Fact.Start)).Contains(n));
                return new DraftLine(p.Label, ok ? mineLine!.Text : p.Fact.Statement);
            })
        ];

        return new Outcome(new Draft(headline, summary, [.. experiences], [.. skills.Take(16)], Labels(d.Education, "education"), Labels(d.Languages, "language")), lines, removed);

        ProvenanceLine Line(string section, string? experience, string text, IEnumerable<string> labels, string check)
        {
            var picked = labels.Select(s.Get).OfType<Picked>().ToList();
            return new ProvenanceLine(section, experience, text, [.. picked.Select(p => p.Fact.Statement)], [.. picked.Select(p => p.Fact.Evidence)], check);
        }
    }

    /// <summary>
    /// Why the reviewer's verdicts do not count as a review: claims left without a verdict, judged twice or unknown. Null when every
    /// claim got exactly one verdict. An empty list on a draft with claims is no review at all.
    /// </summary>
    /// <summary>
    /// The reviewer's verdicts with the claim ids as the draft has them. Models write "C3", "c3", "3", "C3:" or the claim's own text:
    /// all name the same claim. Before 1.2.4 anything but "C3" made the whole review fall and every resume wait for your approval.
    /// </summary>
    public static IReadOnlyList<Prompts.Verdict> Normalize(Draft d, IReadOnlyList<Prompts.Verdict> verdicts)
    {
        var claims = d.Claims().ToList();
        var byText = claims.GroupBy(c => Same(c.Claim.Text)).ToDictionary(g => g.Key, g => g.First().Id);
        return [.. verdicts.Select(v =>
        {
            var key = v.Claim.Trim();
            if (ClaimId().Match(key) is { Success: true } m) return v with { Claim = $"C{int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)}" };
            return byText.TryGetValue(Same(key), out var id) ? v with { Claim = id } : v;
        })];
    }

    private static string Same(string text) => System.Text.RegularExpressions.Regex.Replace(Claims.Fold(text), @"[^\p{L}\p{N}]+", " ").Trim();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\W*c?\s*(\d{1,3})\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ClaimId();

    /// <summary>The CV's own headline or summary, only when it is in the resume's language.</summary>
    private static Claim? InLanguage(Selection s, string kind) =>
        s.Of(kind).FirstOrDefault() is { } f && Languages.PtOrEn(f.Fact.Statement) == (s.Language == "en" ? "en" : "pt") ? new Claim(f.Fact.Statement, [f.Label]) : null;

    /// <summary>
    /// What a headline or summary may say about the person as a whole, beyond the facts it cites: every skill and technology, the years
    /// counted from the dates, the roles held ("Sênior" is true when a role says it), "developer" and "engineer" as the same profession,
    /// and back end, front end or full stack when the person's technologies show that work. Bullets still answer only to their own facts.
    /// </summary>
    internal static List<string> Profile(Selection s)
    {
        var experiences = s.Of("experience").Select(x => x.Fact).ToList();
        var tools = s.Of("skill").Select(x => x.Fact.Statement).Concat(experiences.SelectMany(x => x.Technologies)).ToList();
        var words = new List<string>(tools) { $"{Prompts.Years(experiences)} anos years" };
        words.AddRange(experiences.Select(x => x.Role ?? "").Where(r => r.Length > 0));
        var folded = Claims.Fold(string.Join(" ", words));
        if (System.Text.RegularExpressions.Regex.IsMatch(folded, @"\b(desenvolvedor|desenvolvedora|developer|engenheir|engineer|programador|programmer)"))
            words.Add("developer engineer software engineer desenvolvedor desenvolvedora engenheiro engenheira programador programmer software");
        var back = System.Text.RegularExpressions.Regex.IsMatch(folded, @"asp\.net|\.net|c#|java|spring|node|django|rails|laravel|php|golang|\bgo\b|postgres|sql|rabbitmq|kafka|\bapi");
        var front = System.Text.RegularExpressions.Regex.IsMatch(folded, @"angular|react|vue|typescript|javascript|html|css|svelte");
        if (back) words.Add("backend back-end back end");
        if (front) words.Add("frontend front-end front end");
        if (back && front) words.Add("full stack fullstack full-stack");
        return words;
    }

    public static string? Gap(Draft d, IReadOnlyList<Prompts.Verdict> verdicts)
    {
        var ids = d.Claims().Select(c => c.Id).ToHashSet();
        var byClaim = verdicts.GroupBy(v => v.Claim).ToList();
        var missing = ids.Count(id => byClaim.All(g => g.Key != id));
        var twice = byClaim.Count(g => g.Count() > 1);
        var unknown = byClaim.Count(g => !ids.Contains(g.Key));
        return missing + twice + unknown == 0 ? null
            : $"o Revisor não avaliou cada afirmação uma vez ({missing} sem veredito, {twice} repetidas, {unknown} que não existem no rascunho)";
    }

    /// <summary>
    /// The headline without the parentheses and " | " parts that hold a word without facts, with the job's keywords the person has after it
    /// when nothing else is left to say. Null when the role itself is the problem.
    /// </summary>
    internal static string? Trimmed(string headline, IReadOnlyList<string> missing, Selection s)
    {
        bool Bad(string part) => missing.Any(m => part.Contains(m.Replace(" (negado no fato)", ""), StringComparison.OrdinalIgnoreCase));
        var parts = headline.Split(" | ").Select(p => Parenthesis().Replace(p, m => Bad(m.Value) ? "" : m.Value).Trim().TrimEnd(',', '-').Trim()).ToList();
        if (parts.Count == 0 || parts[0].Length < 3 || Bad(parts[0])) return null;
        var kept = parts.Where((p, i) => i == 0 || p.Length > 0 && !Bad(p)).ToList();
        if (kept.Count == 1) kept.AddRange(s.Matched.Where(k => !kept[0].Contains(k, StringComparison.OrdinalIgnoreCase)).Take(3));
        var text = Tailor.Fit(string.Join(" | ", kept), 64);
        return text == headline ? null : text;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"\s*\([^)]*\)")] private static partial System.Text.RegularExpressions.Regex Parenthesis();

    /// <summary>The sentences of a summary, each with its end mark; ".NET" and "Node.js" do not end a sentence.</summary>
    internal static IEnumerable<string> Sentences(string text) =>
        SentenceEnd().Split(text.Trim()).Select(x => x.Trim()).Where(x => x.Length > 0);

    [System.Text.RegularExpressions.GeneratedRegex(@"(?<=[.!?])\s+(?=[A-ZÀ-Ú])")] private static partial System.Text.RegularExpressions.Regex SentenceEnd();

    /// <summary>Tira do rascunho as afirmações que o Revisor recusou; título e resumo voltam ao texto original do fato.</summary>
    public static (Draft Draft, List<string> Removed) Apply(Draft d, Selection s, IReadOnlyList<Prompts.Verdict> verdicts)
    {
        var refused = verdicts.Where(v => !v.Supported).ToDictionary(v => v.Claim, v => v.Issue);
        var removed = new List<string>();
        Claim? headline = d.Headline, summary = d.Summary;
        var experiences = d.Experiences.Select(e => e with { Bullets = [.. e.Bullets] }).ToArray();
        foreach (var (id, where, claim) in d.Claims())
        {
            if (!refused.TryGetValue(id, out var issue)) continue;
            removed.Add($"\"{claim.Text}\": {issue}");
            if (where == "headline") headline = InLanguage(s, "headline");
            else if (where == "summary") summary = InLanguage(s, "summary");
            else
            {
                var i = Array.FindIndex(experiences, x => x.Fact == where);
                if (i >= 0) experiences[i] = experiences[i] with { Bullets = [.. experiences[i].Bullets.Where(b => !ReferenceEquals(b, claim))] };
            }
        }
        return (d with { Headline = headline, Summary = summary, Experiences = experiences }, removed);
    }
}
