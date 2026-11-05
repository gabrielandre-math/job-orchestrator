using System.Globalization;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Profile.Import;

/// <summary>
/// Dados pessoais saem do texto antes de qualquer modelo ver: nome, e-mail, telefone, links pessoais, documentos, endereço,
/// nascimento e estado civil viram placeholders. Os valores reais voltam só na renderização do CV e no formulário.
/// </summary>
internal static partial class Redactor
{
    private static readonly HashSet<string> Particles = new(StringComparer.OrdinalIgnoreCase) { "de", "da", "do", "dos", "das", "e", "van", "von", "del", "di" };
    private static readonly string[] Headings = ["curriculo", "curriculum", "resume", "cv", "perfil", "profile", "sobre", "about", "contato", "contact"];

    /// <summary>O que dá para achar sem IA: nome (primeira linha que parece um nome), e-mail, telefone e links.</summary>
    public static Identity Detect(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var name = lines.Take(12).Select(l => l.Trim('•', '-', ' ', '\t')).FirstOrDefault(LooksLikeName) ?? "";
        if (name.Length > 0 && name == name.ToUpperInvariant()) name = CultureInfo.GetCultureInfo("pt-BR").TextInfo.ToTitleCase(name.ToLowerInvariant());
        var urls = Url().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ';', ')')).ToList();
        return new Identity(
            name,
            Email().Match(text) is { Success: true } e ? e.Value : null,
            Phones(text).FirstOrDefault(),
            null,
            urls.FirstOrDefault(u => u.Contains("linkedin.com/", StringComparison.OrdinalIgnoreCase)) is { } li ? Absolute(li) : null,
            urls.FirstOrDefault(u => u.Contains("github.com/", StringComparison.OrdinalIgnoreCase)) is { } gh ? Absolute(gh) : null,
            urls.FirstOrDefault(u => !u.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) && !u.Contains("github.com", StringComparison.OrdinalIgnoreCase)) is { } site ? Absolute(site) : null);
    }

    public static string Redact(string text, Identity id)
    {
        var t = Email().Replace(text, "{{EMAIL}}");
        t = Url().Replace(t, m =>
            m.Value.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase) ? "{{LINKEDIN}}"
            : m.Value.Contains("github.com", StringComparison.OrdinalIgnoreCase) ? "{{GITHUB}}" : "{{SITE}}");
        t = Phone().Replace(t, m => Digits(m.Value) is >= 10 and <= 13 ? "{{TELEFONE}}" : m.Value);
        t = Cpf().Replace(t, "{{DOCUMENTO}}");
        t = Rg().Replace(t, "{{DOCUMENTO}}");
        t = Cep().Replace(t, "{{CEP}}");
        t = PrivateLine().Replace(t, m => m.Groups["label"].Value + "{{PRIVADO}}");
        t = Street().Replace(t, "{{ENDERECO}}");
        if (id.FullName.Length > 0)
        {
            t = Regex.Replace(t, Regex.Escape(id.FullName), "{{NOME}}", RegexOptions.IgnoreCase);
            foreach (var part in id.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.Length >= 3 && !Particles.Contains(p)))
                t = Regex.Replace(t, $@"(?<![\p{{L}}]){Regex.Escape(part)}(?![\p{{L}}])", "{{NOME}}", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, @"\{\{NOME\}\}(\s+(?:de|da|do|dos|das|e)?\s*\{\{NOME\}\})+", "{{NOME}}");
        }
        return t;
    }

    public static IEnumerable<string> Phones(string text) =>
        Phone().Matches(text).Select(m => m.Value.Trim()).Where(v => Digits(v) is >= 10 and <= 13);

    private static bool LooksLikeName(string line)
    {
        if (line.Length is < 5 or > 60 || line.Any(char.IsDigit) || line.Contains('@') || line.Contains("http", StringComparison.OrdinalIgnoreCase) || line.Contains('|')) return false;
        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length is < 2 or > 6) return false;
        if (Headings.Contains(Claims.Fold(words[0]))) return false;
        return words.All(w => Particles.Contains(w) || (char.IsUpper(w[0]) && w.All(c => char.IsLetter(c) || c is '\'' or '-' or '.')));
    }

    private static int Digits(string s) => s.Count(char.IsDigit);
    private static string Absolute(string url) => url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : $"https://{url}";

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")] private static partial Regex Email();
    [GeneratedRegex(@"(?:https?://|www\.)[^\s<>()""]+|(?<![\w@./])(?:[a-z]{2,3}\.)?(?:linkedin\.com/in|github\.com)/[^\s<>()""]+", RegexOptions.IgnoreCase)] private static partial Regex Url();
    [GeneratedRegex(@"(?<![\d/.])(?:\+\d{1,3}[\s.-]?)?(?:\(\d{2,3}\)|\d{2,3})[\s.-]?\d{4,5}[\s.-]?\d{4}(?![\d/])")] private static partial Regex Phone();
    [GeneratedRegex(@"\b\d{3}\.\d{3}\.\d{3}-\d{2}\b")] private static partial Regex Cpf();
    [GeneratedRegex(@"\b\d{1,2}\.\d{3}\.\d{3}-[\dxX]\b")] private static partial Regex Rg();
    [GeneratedRegex(@"\b\d{5}-\d{3}\b")] private static partial Regex Cep();
    [GeneratedRegex(@"(?im)^(?<label>[ \t•-]*(?:data de nascimento|nascimento|idade|estado civil|endere[cç]o|cpf|rg|date of birth|birth ?date|address|marital status)[ \t]*[:\-–][ \t]*)(?<value>\S.*)$")] private static partial Regex PrivateLine();
    [GeneratedRegex(@"(?im)^[ \t•-]*(?:rua|r\.|av\.|avenida|travessa|alameda|rodovia|estrada)\s+[^\n]+$")] private static partial Regex Street();
}
