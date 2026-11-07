using System.Globalization;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>Campos que o texto da vaga revela. Nulo quando a vaga não diz: desconhecido não vira afirmação.</summary>
internal sealed record Normalized(
    string? Seniority, string? Modality, string? SalaryText, decimal? SalaryMin, decimal? SalaryMax, string? Currency,
    string? Contract, string? English, string? Sponsorship, string ApplyChannel, string? ApplyEmail);

/// <summary>Extração determinística (regex), em português e inglês. Dicas estruturadas da fonte (Lever, Ashby) têm prioridade.</summary>
internal static partial class Normalizer
{
    private static readonly string[] AtsHosts = ["greenhouse.io", "lever.co", "ashbyhq.com", "smartrecruiters.com", "workable.com"];
    private static readonly string[] ManualHosts = ["linkedin.com", "gupy.io", "indeed.com", "glassdoor.com", "vagas.com.br", "catho.com.br", "infojobs.com.br"];

    public static Normalized From(string title, string description, string location, string applyUrl,
        string? workplace = null, string? compensation = null, string? employment = null, string? applyEmail = null)
    {
        var text = $"{title}\n{location}\n{description}";
        var (salaryText, min, max, currency) = Salary(compensation is { Length: > 0 } c ? c : text);
        return new Normalized(Seniority(title) ?? Seniority(description[..Math.Min(description.Length, 600)]),
            Modality(workplace) ?? Modality(location) ?? Modality(text), salaryText, min, max, currency,
            Contract(employment) ?? Contract(text), English(text), Sponsorship(text), Channel(applyUrl, applyEmail), applyEmail);
    }

    internal static string? Seniority(string s) =>
        Intern().IsMatch(s) ? "Estágio" : Junior().IsMatch(s) ? "Júnior" : Lead().IsMatch(s) ? "Especialista" : Senior().IsMatch(s) ? "Sênior" : Mid().IsMatch(s) ? "Pleno" : null;

    internal static string? Modality(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : Hybrid().IsMatch(s) ? "Híbrido" : RemoteWord().IsMatch(s) ? "Remoto" : Onsite().IsMatch(s) ? "Presencial" : null;

    internal static string? Contract(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : Clt().IsMatch(s) ? "CLT" : Pj().IsMatch(s) ? "PJ" : Contractor().IsMatch(s) ? "Contractor" : null;

    internal static string? English(string s) => EnglishLevel().Match(s) is { Success: true } m
        ? $"Inglês {Level(m.Groups.Values.Skip(1).First(g => g.Success).Value)}" : null;

    internal static string? Sponsorship(string s) =>
        NoSponsor().IsMatch(s) ? "Sem patrocínio de visto" : Sponsor().IsMatch(s) ? "Patrocina visto" : null;

    /// <summary>"ats" quando o link é de um ATS com formulário aberto; "manual" em sites com login; senão e-mail ou link.</summary>
    internal static string Channel(string applyUrl, string? applyEmail)
    {
        if (Uri.TryCreate(applyUrl, UriKind.Absolute, out var u))
        {
            if (AtsHosts.Any(h => u.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)) || FakeAts().IsMatch(u.AbsolutePath)) return "ats";
            if (ManualHosts.Any(h => u.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase))) return "manual";
            if (u.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("ycombinator.com", StringComparison.OrdinalIgnoreCase))
                return applyEmail is null ? "link" : "email";
        }
        return applyEmail is null ? "link" : "email";
    }

    /// <summary>
    /// The pay the job states, monthly: a range or a single value, with currency. "10.000,00" and "10,000.00" read right; "k" is a thousand.
    /// Every amount in the text is weighed by the words around it: a benefit ("PicPay de R$ 800", "auxílio de R$ 2.000 anual") or a
    /// company figure ("paid out $11M") is not pay, and words like "salário", "faixa" or "per month" make an amount the pay. An hourly
    /// rate becomes monthly (160 hours) and a yearly one is divided by 12. With no such words, an amount too low to be a monthly salary
    /// stays unknown: an unknown pay never rejects a job, a wrong one did.
    /// </summary>
    internal static (string? Text, decimal? Min, decimal? Max, string? Currency) Salary(string s)
    {
        (string Text, decimal? Min, decimal? Max, string Currency, int Score)? best = null;
        void Weigh(Match m, bool range)
        {
            var before = s[Math.Max(0, m.Index - 80)..m.Index];
            var after = s[(m.Index + m.Length)..Math.Min(s.Length, m.Index + m.Length + 30)];
            if (NotPay().IsMatch(before) || Magnitude().IsMatch(after)) return;
            var currency = Currency(m.Groups["c"].Value);
            var min = Amount(m.Groups["a"].Value, m.Groups["ka"].Success);
            var max = range ? Amount(m.Groups["b"].Value, m.Groups["kb"].Success || m.Groups["ka"].Success) : null;
            if (min is null) return;
            var near = before[Math.Max(0, before.Length - 40)..] + after;
            var per = Hourly().IsMatch(near) ? 160m : Yearly().IsMatch(after) ? 1m / 12 : 1m;
            min = Math.Round(min.Value * per); max = max is { } x ? Math.Round(x * per) : null;
            var score = (PayWord().IsMatch(before) ? 3 : 0) + (Period().IsMatch(after) || Hourly().IsMatch(near) ? 2 : 0) + (range ? 1 : 0);
            // A bare amount, with no pay word or period around it, counts only when it is a plausible monthly salary.
            var top = max ?? min.Value;
            if (score == 0 && (currency == "BRL" && top < 1500 || currency != "BRL" && top < 1000)) return;
            if (best is null || score > best.Value.Score) best = (m.Value.Trim(), min, max, currency, score);
        }
        foreach (Match m in Range().Matches(s)) Weigh(m, true);
        foreach (Match m in Single().Matches(s)) Weigh(m, false);
        return best is { } b ? (b.Text, b.Min, b.Max, b.Currency) : (null, null, null, null);
    }

    private static decimal? Amount(string raw, bool thousands)
    {
        if (raw.Length == 0) return null;
        var lastDot = raw.LastIndexOf('.');
        var lastComma = raw.LastIndexOf(',');
        var decimalSep = lastDot > lastComma ? '.' : ',';
        var groups = raw.Split(decimalSep);
        var normalized = groups.Length > 1 && groups[^1].Length != 3 ? string.Join("", groups[..^1]).Replace(".", "").Replace(",", "") + "." + groups[^1] : raw.Replace(".", "").Replace(",", "");
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? (thousands ? v * 1000 : v) : null;
    }

    private static string Currency(string symbol) => symbol.Trim().ToUpperInvariant() switch
    {
        "R$" or "BRL" => "BRL",
        "€" or "EUR" => "EUR",
        "£" or "GBP" => "GBP",
        _ => "USD"
    };

    private static string Level(string word) => word.ToLowerInvariant() switch
    {
        "fluent" or "fluente" => "fluente",
        "advanced" or "avançado" or "avancado" => "avançado",
        _ => "intermediário"
    };

    [GeneratedRegex(@"\b(intern|internship|estagi[aá]ri[oa]|est[aá]gio|trainee)\b", RegexOptions.IgnoreCase)] private static partial Regex Intern();
    [GeneratedRegex(@"\b(j[uú]nior|jr\.?)\b", RegexOptions.IgnoreCase)] private static partial Regex Junior();
    [GeneratedRegex(@"\b(pleno|mid[- ]level|mid)\b", RegexOptions.IgnoreCase)] private static partial Regex Mid();
    [GeneratedRegex(@"\b(s[eê]nior|sr\.?)\b", RegexOptions.IgnoreCase)] private static partial Regex Senior();
    [GeneratedRegex(@"\b(staff|principal|tech lead|lead|especialista)\b", RegexOptions.IgnoreCase)] private static partial Regex Lead();
    [GeneratedRegex(@"\b(remote|remoto|home[- ]office|anywhere|100% remoto|work from home|wfh)\b", RegexOptions.IgnoreCase)] private static partial Regex RemoteWord();
    [GeneratedRegex(@"\b(hybrid|h[ií]brido)\b", RegexOptions.IgnoreCase)] private static partial Regex Hybrid();
    [GeneratedRegex(@"\b(on-?site|in office|presencial)\b", RegexOptions.IgnoreCase)] private static partial Regex Onsite();
    [GeneratedRegex(@"\bCLT\b")] private static partial Regex Clt();
    [GeneratedRegex(@"\bPJ\b|pessoa jur[ií]dica")] private static partial Regex Pj();
    [GeneratedRegex(@"\b(contractor|contract|freelance|freelancer|b2b)\b", RegexOptions.IgnoreCase)] private static partial Regex Contractor();
    [GeneratedRegex(@"\b(fluent|advanced|intermediate)\s+english\b|\benglish\s*\(?\s*(fluent|advanced|intermediate)|ingl[eê]s\s+(fluente|avan[cç]ado|intermedi[aá]rio)", RegexOptions.IgnoreCase)] private static partial Regex EnglishLevel();
    [GeneratedRegex(@"(no|not|unable to|cannot|can't|won't)\s+(provide\s+|offer\s+)?(visa\s+)?sponsor", RegexOptions.IgnoreCase)] private static partial Regex NoSponsor();
    [GeneratedRegex(@"(visa sponsorship (is )?available|will sponsor|we sponsor|offer visa sponsorship)", RegexOptions.IgnoreCase)] private static partial Regex Sponsor();
    [GeneratedRegex(@"/jobs/\d+/apply|^/lever/[^/]+/\d+/apply|^/ashby/[^/]+/\d+/application")] private static partial Regex FakeAts();
    [GeneratedRegex(@"(?<c>R\$|US\$|USD|EUR|BRL|€|£|\$)\s?(?<a>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+)\s?(?<ka>[kK])?\s*(?:-|–|a|to|até)\s*(?:R\$|US\$|USD|EUR|BRL|€|£|\$)?\s?(?<b>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+)\s?(?<kb>[kK])?")] private static partial Regex Range();
    [GeneratedRegex(@"(?<c>R\$|US\$|USD|EUR|BRL|€|£|\$)\s?(?<a>\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,2})?|\d{2,})\s?(?<ka>[kK])?")] private static partial Regex Single();
    // Words right before an amount that make it something other than pay: benefits, allowances, budgets and company figures.
    [GeneratedRegex(@"(vale|aux[ií]lio|benef[ií]cio|b[oô]nus|picpay|caju|flash|alelo|ifood|swile|vr|va|plano|reembolso|ajuda de custo|allowance|stipend|budget|equipamento|equipment|gympass|wellhub|cart[aã]o|pr[eê]mio|plr|ppr|paid out|raised|funding|investimento|invested|revenue|fatur|valuation|series [a-e]|rodada|capta)[^\n.]{0,60}$", RegexOptions.IgnoreCase)] private static partial Regex NotPay();
    [GeneratedRegex(@"^\s?(mm?|mi|bi|b|bn|million|billion|milh|bilh)\b", RegexOptions.IgnoreCase)] private static partial Regex Magnitude();
    [GeneratedRegex(@"(sal[aá]ri|remunera|faixa|pretens|compensation|salary|pay range|base pay|\bote\b|pagamento|bolsa|ganhos|rate)[^\n]{0,60}$", RegexOptions.IgnoreCase)] private static partial Regex PayWord();
    [GeneratedRegex(@"(/\s?h(ou)?r?\b|por hora|/hora|per hour|an hour|hourly|\bhr\b)", RegexOptions.IgnoreCase)] private static partial Regex Hourly();
    [GeneratedRegex(@"^[^\n]{0,20}(/\s?(ano|year|yr)\b|por ano|anual|per year|a year|annual|yearly|p\.\s?a\.)", RegexOptions.IgnoreCase)] private static partial Regex Yearly();
    [GeneratedRegex(@"^[^\n]{0,20}(/\s?m[eê]s|por m[eê]s|mensal|per month|/\s?month|a month|monthly|/\s?(ano|year|yr)\b|por ano|anual|per year|a year|annual|yearly)", RegexOptions.IgnoreCase)] private static partial Regex Period();
}
