using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Profile.Contracts;

/// <summary>
/// Conferência de proveniência, sem IA: os números e os termos próprios (tecnologias, empresas, produtos) de uma afirmação
/// precisam aparecer nas fontes que ela cita. É a trava contra fato plantado; a revisão cruzada por outro modelo vem depois dela.
/// </summary>
public static partial class Claims
{
    // Termos genéricos que não afirmam nada sobre a pessoa.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "api", "apis", "rest", "http", "https", "json", "ci", "cd", "ci/cd", "ti", "it", "pt", "en", "ui", "ux", "ok", "br", "pt-br"
    };

    /// <summary>O que a afirmação diz e as fontes não sustentam. Lista vazia: tudo sustentado.</summary>
    public static IReadOnlyList<string> Unsupported(string claim, IEnumerable<string> sources)
    {
        var source = Fold(string.Join("\n", sources));
        var values = Numbers(source).ToHashSet();
        var missing = new List<string>();
        foreach (var n in Numbers(Fold(claim)).Distinct())
            if (!values.Contains(n)) missing.Add(n.ToString(CultureInfo.InvariantCulture));
        foreach (var term in Terms(claim).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!Generic.Contains(term) && !Present(Fold(term), source)) missing.Add(term);
        return missing;
    }

    // Senioridade, liderança e gestão são afirmação mesmo no começo da frase ("Senior Engineer", "Led the team").
    private static readonly HashSet<string> AlwaysChecked = new(StringComparer.Ordinal)
    {
        "senior", "sr", "lead", "led", "leading", "principal", "staff", "manager", "managed", "head", "director", "architect", "specialist", "chief", "cto",
        "coordinator", "mentored", "lider", "liderei", "liderou", "gerente", "gerenciei", "gerenciou", "diretor", "arquiteto", "especialista", "coordenador",
        "coordenei", "mentorei", "chefe"
    };

    /// <summary>Termos próprios: siglas, nomes com maiúscula no meio da frase, C#, C++, .NET, Node.js, PostgreSQL, gRPC, e palavras de senioridade.</summary>
    public static IEnumerable<string> Terms(string text)
    {
        foreach (Match m in Term().Matches(text))
        {
            var t = m.Value.TrimEnd('.', ',', ';', ':');
            if (t.Length < 2) continue;
            var symbolic = t.Any(c => c is '#' or '+') || t.StartsWith('.') || t.Skip(1).Any(char.IsUpper) || t.Any(char.IsDigit) && t.Any(char.IsLetter);
            var capitalized = char.IsUpper(t[0]) && !SentenceStart(text, m.Index);
            if (symbolic || capitalized || AlwaysChecked.Contains(Fold(t))) yield return t;
        }
    }

    private static bool SentenceStart(string text, int index)
    {
        var i = index - 1;
        while (i >= 0 && char.IsWhiteSpace(text[i])) i--;
        return i < 0 || text[i] is '.' or '!' or '?' or ':' or '\n' or '•' or '-' or '(' or '"';
    }

    // Equivalências PT/EN de cargo, senioridade, formação e idioma: o currículo em inglês sai de fatos em português.
    // Senioridade só passa se a fonte disser a mesma senioridade em alguma das línguas.
    private static readonly string[][] Equivalent =
    [
        ["developer", "desenvolvedor", "desenvolvedora", "dev"], ["engineer", "engenheiro", "engenheira"], ["engineering", "engenharia"],
        ["analyst", "analista"], ["scientist", "cientista"], ["specialist", "especialista"],
        ["consultant", "consultor", "consultora"], ["intern", "estagiario", "estagiaria", "estagio", "internship"], ["trainee"],
        ["senior", "sr"], ["junior", "jr"], ["mid-level", "mid", "pleno", "plena"],
        ["lead", "led", "leading", "lider", "liderei", "liderou", "liderando", "lideranca", "tech lead"],
        ["manager", "managed", "gerente", "gerenciei", "gerenciou", "gestor", "gestora", "gestao"], ["mentored", "mentorei", "mentoria", "mentor"],
        ["architect", "arquiteto", "arquiteta"], ["director", "diretor", "diretora"], ["coordinator", "coordenador", "coordenadora", "coordenei"],
        ["head", "head de"], ["staff"], ["principal"], ["chief"], ["cto"],
        ["software"], ["backend", "back-end", "back end"], ["frontend", "front-end", "front end"], ["full stack", "fullstack", "full-stack"],
        ["data", "dados"], ["systems", "sistemas"], ["information", "informacao"], ["computer", "computacao"], ["science", "ciencia", "ciencias"],
        ["bachelor", "bacharelado", "bacharel"], ["master", "mestrado", "mestre"], ["degree", "graduacao", "superior"], ["technologist", "tecnologo", "tecnologia em"],
        ["university", "universidade"], ["college", "faculdade"], ["institute", "instituto"], ["school", "escola"],
        ["english", "ingles"], ["spanish", "espanhol"], ["portuguese", "portugues"], ["french", "frances"], ["german", "alemao"],
        ["native", "nativo", "nativa"], ["fluent", "fluente"], ["advanced", "avancado"], ["intermediate", "intermediario"], ["basic", "basico"],
        ["brazil", "brasil"], ["present", "atual"]
    ];

    private static bool Present(string term, string source)
    {
        if (Contains(term, source)) return true;
        foreach (var group in Equivalent)
            if (group.Contains(term) && group.Any(e => e != term && Contains(e, source))) return true;
        // Plural simples: "APIs" sustentado por "API", "microsserviços" por "microsserviço".
        return term.Length > 3 && term.EndsWith('s') && Contains(term[..^1], source);
    }

    private static bool Contains(string term, string source)
    {
        var before = term.StartsWith('.') ? "" : @"(?<![\p{L}\p{N}])";
        return Regex.IsMatch(source, before + Regex.Escape(term) + @"(?![\p{L}\p{N}#+])");
    }

    /// <summary>Valores numéricos: 1.000 e 1000 e 1k e 1 mil são o mesmo número; 40% é 40.</summary>
    public static IEnumerable<decimal> Numbers(string folded)
    {
        foreach (Match m in Number().Matches(folded))
        {
            var raw = m.Groups["n"].Value;
            var digits = ThousandGroups().IsMatch(raw) ? raw.Replace(".", "").Replace(",", "") : raw.Replace(',', '.');
            if (!decimal.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
            var unit = m.Groups["u"].Value.Trim();
            value *= unit switch
            {
                "k" or "mil" or "thousand" => 1000,
                "mi" or "m" or "milhoes" or "milhao" or "million" or "millions" => 1_000_000,
                "bi" or "bilhoes" or "bilhao" or "billion" or "billions" => 1_000_000_000,
                _ => 1
            };
            yield return value;
        }
    }

    /// <summary>Minúsculas, sem acento, aspas e travessões unificados, espaços colapsados.</summary>
    public static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(c switch { '–' or '—' or '−' => '-', '“' or '”' => '"', '‘' or '’' => '\'', _ => char.ToLowerInvariant(c) });
        }
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\.?[\p{L}\p{N}][\p{L}\p{N}#+]*(?:[./-][\p{L}\p{N}#+]+)*")] private static partial Regex Term();
    [GeneratedRegex(@"(?<![\p{L}\p{N}.,])(?<n>\d+(?:[.,]\d+)*)(?<u>\s?(?:k|mil|mi|milhoes|milhao|million|millions|thousand|m|bi|bilhoes|bilhao|billion|billions)\b)?")] private static partial Regex Number();
    [GeneratedRegex(@"^\d{1,3}(?:[.,]\d{3})+$")] private static partial Regex ThousandGroups();
    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
}
