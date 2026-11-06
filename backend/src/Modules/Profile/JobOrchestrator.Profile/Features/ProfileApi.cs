using System.Globalization;

namespace JobOrchestrator.Profile.Features;

/// <summary>Contrato público do módulo: o perfil para compor materiais e o banco de respostas para os formulários.</summary>
internal sealed class ProfileApi(ProfileDbContext db, TimeProvider time) : IProfileApi
{
    public async Task<ProfileSnapshot?> CurrentAsync(CancellationToken ct)
    {
        var p = await db.Profiles.AsNoTracking().Include(x => x.Facts).Include(x => x.Documents).OrderBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (p is null) return null;
        var files = p.Documents.ToDictionary(d => d.Id, d => d.FileName);
        var facts = p.Facts.Where(f => f.Usable).OrderBy(f => f.Position).Select(f => new ProfileFact(
            f.Id, f.Kind, f.Statement, f.ParentId, f.Organization, f.Role, f.Start, f.End, f.Technologies, f.Level, Evidence(f, files))).ToList();
        return new ProfileSnapshot(p.Id, p.Version, p.Hash, p.Synthetic, !p.Pending, p.Identity, facts, p.Confirmations);
    }

    public async Task<IReadOnlyDictionary<string, string>> AnswersAsync(AnswerContext context, CancellationToken ct) =>
        await CurrentAsync(ct) is { } p ? AnswerBank.Build(p, context, DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)) : new Dictionary<string, string>();

    private static string Evidence(VerifiedFact f, IReadOnlyDictionary<Guid, string> files)
    {
        if (f.Status == FactStatus.Confirmed) return "confirmado por você";
        var file = f.DocumentId is { } d && files.TryGetValue(d, out var name) ? name : "perfil sintético de teste";
        if (f.Page is { } page) return $"{file}, página {page}";
        return f.Note is { Length: > 0 } note && note.Contains("linha", StringComparison.Ordinal) ? note : file;
    }
}

/// <summary>
/// Banco de respostas (docs/PLANO.md §3.2): só o que fatos ou confirmações sustentam. Anos com cada tecnologia saem das datas
/// das experiências sem contar sobreposição; pretensão sai do piso do vínculo e da moeda da vaga.
/// </summary>
internal static class AnswerBank
{
    public static Dictionary<string, string> Build(ProfileSnapshot p, AnswerContext c, DateOnly today)
    {
        var en = c.Language == "en";
        string T(string pt, string english) => en ? english : pt;
        var id = p.Identity;
        var conf = p.Confirmations;
        var a = new Dictionary<string, string>(StringComparer.Ordinal);
        void Put(string key, string? value) { if (!string.IsNullOrWhiteSpace(value)) a[key] = value.Trim(); }

        Put("full_name", id.FullName);
        Put("first_name", id.FirstName);
        Put("last_name", id.LastName);
        Put("email", id.Email);
        Put("phone", id.Phone);
        Put("location", id.Location);
        Put("linkedin_url", id.LinkedIn);
        Put("github_url", id.GitHub);
        Put("portfolio_url", id.Website ?? id.GitHub);

        if (conf.ConfirmedAt is not null)
        {
            var country = (c.Country ?? "BR").ToUpperInvariant();
            Put("work_authorization", conf.AuthorizedCountries.Contains(country, StringComparer.OrdinalIgnoreCase) ? T("Sim", "Yes") : T("Não", "No"));
            Put("needs_sponsorship", conf.NeedsSponsorship ? T("Sim", "Yes") : T("Não", "No"));
            Put("salary_expectation", Salary(conf, c, en));
            Put("start_date", conf.StartAvailability);
            Put("relocation", conf.Relocate ? T("Sim", "Yes") : T("Não", "No"));
            Put("work_modality", string.Join(", ", conf.Modalities.Select(m => m switch
            {
                "remote" => T("remoto", "remote"), "hybrid" => T("híbrido", "hybrid"), _ => T("presencial", "on-site")
            })));
            Put("self_id", conf.SelfIdDefault == "prefer_not" ? T("Prefiro não informar", "I don't wish to answer") : null);
        }

        var english = conf.EnglishLevel ?? p.Facts.FirstOrDefault(f => f.Kind == "language" && Claims.Fold(f.Statement).Contains("ingl") || f.Kind == "language" && Claims.Fold(f.Statement).Contains("english"))?.Level;
        Put("english_level", english switch
        {
            "native" => T("Nativo", "Native"), "fluent" => T("Fluente", "Fluent"), "advanced" => T("Avançado", "Advanced"),
            "intermediate" => T("Intermediário", "Intermediate"), "basic" => T("Básico", "Basic"), _ => null
        });

        Put("education", p.Facts.Where(f => f.Kind == "education").OrderByDescending(f => f.End ?? "9999").Select(f => f.Statement).FirstOrDefault());
        // Título e resumo saem dos fatos do currículo, como estão lá: alguns formulários (Workable) pedem os dois.
        Put("headline", p.Facts.FirstOrDefault(f => f.Kind == "headline")?.Statement);
        Put("summary", p.Facts.FirstOrDefault(f => f.Kind == "summary")?.Statement);
        Put("how_heard", c.Source switch
        {
            "hn" or "hackernews" => "Hacker News",
            "github" => "GitHub",
            "remotive" => "Remotive",
            _ => T("Site de carreiras da empresa", "Company careers page")
        });

        foreach (var tech in c.Technologies)
            if (Years(p, tech, today) is { } years) a[$"years_experience:{tech}"] = years.ToString(CultureInfo.InvariantCulture);
        return a;
    }

    /// <summary>Anos com a tecnologia: meses das experiências que a citam, sem contar duas vezes o mesmo mês. Nulo se nenhuma cita.</summary>
    public static int? Years(ProfileSnapshot p, string technology, DateOnly today)
    {
        var tech = Claims.Fold(technology);
        var jobs = p.Facts.Where(f => f.Kind == "experience" && f.Start is { Length: >= 4 } && f.Technologies.Any(t => Claims.Fold(t) == tech)).ToList();
        if (jobs.Count == 0) return null;
        var months = new HashSet<int>();
        foreach (var j in jobs)
        {
            var from = Parse(j.Start!) ?? today;
            var to = j.End is { Length: >= 4 } e ? Parse(e) ?? today : today;
            for (var d = from; d <= to; d = d.AddMonths(1)) months.Add(d.Year * 12 + d.Month);
        }
        return months.Count / 12;
    }

    private static DateOnly? Parse(string ym) =>
        ym.Length >= 7 && DateOnly.TryParseExact(ym[..7] + "-01", "yyyy-MM-dd", out var d) ? d
        : ym.Length >= 4 && int.TryParse(ym[..4], out var y) ? new DateOnly(y, 1, 1) : null;

    private static string? Salary(Confirmations conf, AnswerContext c, bool en)
    {
        var contract = c.Contract?.ToLowerInvariant();
        var currency = c.Currency?.ToUpperInvariant();
        var pt = CultureInfo.GetCultureInfo("pt-BR");
        var us = CultureInfo.GetCultureInfo("en-US");
        string Money(string symbol, int value) => en ? $"{symbol} {value.ToString("N0", us)}" : $"{symbol} {value.ToString("N0", pt)}";
        if (currency == "USD" || contract == "contractor")
            return conf.FloorUsdMonth is { } usd ? (en ? $"{Money("USD", usd)} per month" : $"{Money("US$", usd)} por mês") : null;
        if (contract == "pj")
            return conf.FloorPjBrl is { } pj ? (en ? $"{Money("BRL", pj)} per month (PJ contract)" : $"{Money("R$", pj)} por mês (PJ)") : null;
        return conf.FloorCltBrl is { } clt ? (en ? $"{Money("BRL", clt)} per month (CLT)" : $"{Money("R$", clt)} por mês (CLT)") : null;
    }
}
