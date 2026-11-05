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

    /// <summary>Faixa ou valor único, com moeda. "10.000,00" e "10,000.00" são lidos certo; "k" multiplica por mil.</summary>
    internal static (string? Text, decimal? Min, decimal? Max, string? Currency) Salary(string s)
    {
        var m = Range().Match(s);
        if (m.Success)
            return (m.Value.Trim(), Amount(m.Groups["a"].Value, m.Groups["ka"].Success), Amount(m.Groups["b"].Value, m.Groups["kb"].Success || m.Groups["ka"].Success), Currency(m.Groups["c"].Value));
        m = Single().Match(s);
        return m.Success ? (m.Value.Trim(), Amount(m.Groups["a"].Value, m.Groups["ka"].Success), null, Currency(m.Groups["c"].Value)) : (null, null, null, null);
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
    [GeneratedRegex(@"/jobs/\d+/apply")] private static partial Regex FakeAts();
    [GeneratedRegex(@"(?<c>R\$|US\$|USD|EUR|BRL|€|£|\$)\s?(?<a>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+)\s?(?<ka>[kK])?\s*(?:-|–|a|to|até)\s*(?:R\$|US\$|USD|EUR|BRL|€|£|\$)?\s?(?<b>\d{1,3}(?:[.,]\d{3})*(?:[.,]\d{1,2})?|\d+)\s?(?<kb>[kK])?")] private static partial Regex Range();
    [GeneratedRegex(@"(?<c>R\$|US\$|USD|EUR|BRL|€|£|\$)\s?(?<a>\d{1,3}(?:[.,]\d{3})+(?:[.,]\d{1,2})?|\d{2,})\s?(?<ka>[kK])?")] private static partial Regex Single();
}
