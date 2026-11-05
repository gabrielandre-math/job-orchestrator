using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>Um fator da nota: peso, valor de 0 a 1, pontos e a explicação em português.</summary>
internal sealed record Factor(string Name, int Weight, double Value, int Points, string Note, bool Unknown);

/// <summary>Resultado da avaliação: nota, confiança, fatores e o motivo do corte, quando houver.</summary>
internal sealed record Evaluation(int Score, double Confidence, IReadOnlyList<Factor> Factors, string? Rejection);

/// <summary>
/// Elegibilidade e aderência determinísticas (v0, sem IA). A nota é heurística de aderência, não probabilidade de contratação.
/// Regras eliminatórias vêm antes da nota e não podem ser compensadas por ela.
/// </summary>
internal static partial class Scoring
{
    private static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["C#"] = ["c#", "csharp", "c sharp"],
        [".NET"] = [".net", "dotnet", "net core", ".net core", "asp.net"],
        ["ASP.NET Core"] = ["asp.net core", "asp.net", "web api", "minimal api"],
        ["AWS"] = ["aws", "amazon web services", "lambda", "ecs", "sqs", "s3"],
        ["PostgreSQL"] = ["postgresql", "postgres"]
    };

    public static Evaluation Evaluate(CampaignSettings c, string title, string description, string location, string? modality = null)
    {
        var text = $" {Fingerprint.Normalize(title)} {Fingerprint.Normalize(description)} ";
        var factors = new List<Factor>();

        // Eliminatórios. Sentinela de golpe primeiro: vaga que cobra para entrar ou só conversa por mensageiro não vira candidatura.
        if (Scam().Match(text) is { Success: true } scam)
            return Reject(factors, $"Suspeita de golpe: a vaga fala em \"{scam.Value.Trim()}\". Vaga séria não cobra para você se candidatar.");
        var where = Fingerprint.Normalize(location);
        if (Restricted().IsMatch(where) || Restricted().IsMatch(text))
            return Reject(factors, "Restrita a residentes de outro país.");
        var remote = modality == "Remoto" || Remote().IsMatch(where) || Remote().IsMatch(text);
        var allowed = c.Locations.Count == 0 || remote && c.Locations.Any(l => Remote().IsMatch(Fingerprint.Normalize(l)))
            || c.Locations.Any(l => Places(l).Any(p => where.Contains(p, StringComparison.Ordinal)));
        var locationUnknown = where.Length == 0;
        if (!allowed && !locationUnknown) return Reject(factors, $"Local \"{location}\" fora da campanha.");

        var essential = c.Technologies.Take(2).ToList();
        var others = c.Technologies.Skip(2).ToList();
        var essentialFound = essential.Where(t => Mentions(text, t)).ToList();
        if (essential.Count > 0 && essentialFound.Count == 0)
            return Reject(factors, $"Stack essencial ausente ({string.Join(", ", essential)}).");

        // Aderência
        var othersFound = others.Where(t => Mentions(text, t)).ToList();
        var stack = 0.6 * (essential.Count == 0 ? 1 : (double)essentialFound.Count / essential.Count) + 0.4 * (others.Count == 0 ? 1 : (double)othersFound.Count / others.Count);
        factors.Add(F("Stack", 45, stack, $"Pede {string.Join(", ", essentialFound.Concat(othersFound))}" + (others.Count > othersFound.Count ? $"; não cita {string.Join(", ", others.Except(othersFound))}." : "."), false));

        var t = Fingerprint.Normalize(title);
        if (c.Titles is { Count: > 0 } targets)
        {
            // Qualquer área: o título da vaga contra os cargos-alvo da campanha, por palavras em comum.
            var words = Words(t);
            var best = targets.Select(x => Words(Fingerprint.Normalize(x))).Where(w => w.Count > 0).Select(w => (double)w.Count(words.Contains) / w.Count).DefaultIfEmpty(0).Max();
            var named = c.Technologies.Any(k => Mentions($" {t} ", k));
            var fit = best >= 0.6 ? 1.0 : best >= 0.3 || named ? 0.6 : 0.3;
            factors.Add(F("Cargo", 25, fit, fit == 1 ? "Cargo igual a um dos seus cargos-alvo." : fit == 0.6 ? "Cargo próximo dos seus cargos-alvo." : "Cargo diferente dos seus cargos-alvo.", false));
        }
        else
        {
            var role = BackendRole().IsMatch(t) ? 1.0 : OtherRole().IsMatch(t) ? 0.2 : 0.5;
            factors.Add(F("Cargo", 25, role, role == 1 ? "Cargo de backend ou engenharia de software." : role < 0.5 ? "Cargo de outra área." : "Cargo genérico.", role == 0.5));
        }

        var seniority = TooSenior().IsMatch(t) ? 0.3 : TooJunior().IsMatch(t) ? 0.2 : 0.8;
        factors.Add(F("Senioridade", 15, seniority, seniority == 0.8 ? "Nível compatível (estimado pelo título; o perfil ainda não define o seu)." : "Nível provavelmente distante do seu.", true));

        var mode = remote ? 1.0 : modality == "Híbrido" || Hybrid().IsMatch(text) ? 0.6 : modality == "Presencial" || Onsite().IsMatch(text) ? 0.3 : 0.5;
        factors.Add(F("Modalidade", 15, mode, remote ? "Remoto." : mode == 0.6 ? "Híbrido." : mode == 0.3 ? "Presencial." : "Modalidade não informada.", mode == 0.5 || locationUnknown));

        var score = factors.Sum(f => f.Points);
        var confidence = Math.Round(Math.Max(0.4, 1 - 0.15 * factors.Count(f => f.Unknown)), 2);
        return new Evaluation(score, confidence, factors, null);
    }

    /// <summary>Pré-filtro de relevância: cita ao menos uma tecnologia essencial da campanha.</summary>
    public static bool Relevant(CampaignSettings c, string title, string description)
    {
        var essential = c.Technologies.Take(2).ToList();
        if (essential.Count == 0) return true;
        var text = $" {Fingerprint.Normalize(title)} {Fingerprint.Normalize(description)} ";
        return essential.Any(t => Mentions(text, t));
    }

    private static bool Mentions(string text, string technology) =>
        (Synonyms.TryGetValue(technology, out var words) ? words : [technology.ToLowerInvariant()])
        .Any(w => text.Contains($" {Fingerprint.Normalize(w)} ", StringComparison.Ordinal) || text.Contains(Fingerprint.Normalize(w), StringComparison.Ordinal) && w.Length > 3);

    /// <summary>Grafias equivalentes de um local da campanha.</summary>
    private static IEnumerable<string> Places(string location)
    {
        var n = Fingerprint.Normalize(location);
        yield return n;
        if (n == "brasil") yield return "brazil";
        if (n == "brazil") yield return "brasil";
    }

    private static Factor F(string name, int weight, double value, string note, bool unknown) =>
        new(name, weight, Math.Round(value, 2), (int)Math.Round(weight * value), note, unknown);

    private static Evaluation Reject(List<Factor> factors, string reason) => new(0, 1, factors, reason);

    private static HashSet<string> Words(string normalized) =>
        [.. normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 3 && w is not ("para" or "com" or "and" or "the" or "with" or "dos" or "das"))];

    [GeneratedRegex(@"\b(taxa de (inscricao|cadastro|adesao|matricula)|pague (uma|a) taxa|pagamento antecipado|deposito (inicial|antecipado)|investimento inicial|compra do kit|kit de trabalho|renda extra garantida|registration fee|application fee|pay (a|the) fee|upfront (payment|fee)|western union|gift cards?|(so|somente|apenas) (pelo|por|no) (whatsapp|telegram)|only (via|on|through) (whatsapp|telegram))\b")] private static partial Regex Scam();
    [GeneratedRegex(@"\b(us only|usa only|united states only|eu only|uk only|must reside in the us|us based only)\b")] private static partial Regex Restricted();
    [GeneratedRegex(@"\b(remote|remoto|anywhere|worldwide|latam|home office|100 remoto)\b")] private static partial Regex Remote();
    [GeneratedRegex(@"\b(hybrid|hibrido)\b")] private static partial Regex Hybrid();
    [GeneratedRegex(@"\b(on site|onsite|presencial)\b")] private static partial Regex Onsite();
    [GeneratedRegex(@"\b(backend|back end|software engineer|engenheir[oa] de software|desenvolvedor[a]?|developer|full stack|fullstack|\.net|c#)\b")] private static partial Regex BackendRole();
    [GeneratedRegex(@"\b(frontend|front end|mobile|ios|android|data scientist|designer|qa)\b")] private static partial Regex OtherRole();
    [GeneratedRegex(@"\b(principal|staff|head|director|diretor|vp)\b")] private static partial Regex TooSenior();
    [GeneratedRegex(@"\b(intern|estagio|estagiario|trainee)\b")] private static partial Regex TooJunior();
}
