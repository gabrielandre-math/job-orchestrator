namespace JobOrchestrator.Materials.Compose;

/// <summary>
/// Agente Conferente (sem IA): cada afirmação do rascunho precisa citar fatos que existem, do lugar certo, e não pode trazer
/// número nem termo próprio que esses fatos não tenham (Claims.Unsupported). O que não passa sai do currículo, com o motivo.
/// </summary>
internal static class Conferente
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

        // Título e resumo podem citar qualquer skill ou tecnologia do perfil: listar o que você usa é verdade.
        var stack = s.Of("skill").Select(x => x.Fact.Statement).Concat(s.Of("experience").SelectMany(x => x.Fact.Technologies)).ToList();

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
            removed.Add($"{section}: \"{c.Text}\" (sem fato para {string.Join(", ", missing)})");
            return Fallback(section, fallbackKind);
        }

        Claim? Fallback(string section, string? kind)
        {
            if (kind is null || s.Of(kind).FirstOrDefault() is not { } f) return null;
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
            if (where == "headline") headline = s.Of("headline").Select(h => new Claim(h.Fact.Statement, [h.Label])).FirstOrDefault();
            else if (where == "summary") summary = s.Of("summary").Select(h => new Claim(h.Fact.Statement, [h.Label])).FirstOrDefault();
            else
            {
                var i = Array.FindIndex(experiences, x => x.Fact == where);
                if (i >= 0) experiences[i] = experiences[i] with { Bullets = [.. experiences[i].Bullets.Where(b => !ReferenceEquals(b, claim))] };
            }
        }
        return (d with { Headline = headline, Summary = summary, Experiences = experiences }, removed);
    }
}
