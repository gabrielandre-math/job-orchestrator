using System.Text.RegularExpressions;

namespace JobOrchestrator.Jobs.Domain;

/// <summary>A elegibilidade de quem mora no Brasil para uma vaga, com o trecho que sustenta e se foi dito pelo anúncio ou deduzido do local.</summary>
internal sealed record EligibilityResult(string Class, string Evidence, bool Declared);

/// <summary>Quem procura: mora no Brasil (vaga restrita sai da fila) e aceita mudança (vaga presencial fora entra). Sem perfil, mora no Brasil e não aceita.</summary>
internal sealed record EligibilityPolicy(bool ResidesInBrazil, bool AcceptsRelocation)
{
    public static readonly EligibilityPolicy Default = new(true, false);
    /// <summary>Vaga salva à mão: nada é cortado pela elegibilidade.</summary>
    public static readonly EligibilityPolicy Manual = new(false, true);
}

/// <summary>
/// Quem mora no Brasil pode se candidatar? O HiringEligibilityEngine da especificação V2, com regras determinísticas em português,
/// inglês e espanhol (sem IA) e a frase do anúncio guardada como prova:
/// BR_ELIGIBLE_CONFIRMED (no Brasil, ou remota com Brasil, LATAM, América do Sul, Américas ou mundo todo escritos), BR_ELIGIBLE_POSSIBLE
/// (remota sem restrição clara, ou contractor/B2B/EOR remoto), BR_RESTRICTED (US only, autorização ou residência local, cidadania),
/// RELOCATION_WITH_SPONSORSHIP (presencial fora com visto ou mudança escritos), RELOCATION_UNVERIFIED (presencial fora, sem dizer de visto),
/// REVIEW (o anúncio se contradiz, como "worldwide" e "US only") e UNKNOWN (sem local nenhum). UNKNOWN nunca vira permitido por palpite.
/// </summary>
internal static partial class Eligibility
{
    public static EligibilityResult Classify(string title, string description, string location, string? modality, IReadOnlyCollection<string>? allowed = null)
    {
        // A fonte disse quais países aceita, de forma estruturada: é prova declarada e vale mais que o texto.
        if (allowed is { Count: > 0 })
            return allowed.Contains("BR")
                ? new("BR_ELIGIBLE_CONFIRMED", $"a fonte lista os países aceitos: {string.Join(", ", allowed.Take(12))}", true)
                : new("BR_RESTRICTED", $"a fonte aceita só: {string.Join(", ", allowed.Take(12))}", true);
        var text = $"{title}\n{location}\n{description}";
        var remote = modality == "Remoto" || RemoteWord().IsMatch(location) || RemoteWord().IsMatch(title);
        if (Citizenship().Match(text) is { Success: true } citizen) return new("BR_RESTRICTED", Snippet(text, citizen), true);
        var local = LocalAuth().Match(text);
        var restricted = local.Success ? local : remote ? RemoteRestricted().Match($"{title}\n{location}") : Match.Empty;
        // Sigla de estado e cidade ambígua (SC é Carolina do Sul, Salvador pode ser El Salvador): se o local cita outro país e não o Brasil, não é no Brasil.
        var countries = SourceCatalog.MatchCountries(location);
        var abroad = countries.Count > 0 && !countries.Contains("BR");
        if (!abroad && InBrazil().IsMatch(location)) return new("BR_ELIGIBLE_CONFIRMED", $"local da vaga: {location.Trim()}{(remote ? " (remota)" : "")}", true);
        // The place is often in the title only ("[Híbrido] Desenvolvedor... - Rio de Janeiro - RJ", as the Brazilian GitHub boards write).
        if (!abroad && InBrazil().Match(title) is { Success: true } inTitle) return new("BR_ELIGIBLE_CONFIRMED", Snippet(title, inTitle), true);
        var open = remote ? OpenToBrazil().Match(text) is { Success: true } latam ? latam : Worldwide().Match(text) : Match.Empty;
        if (open.Success && restricted.Success) return new("REVIEW", $"{Snippet(text, open)} / {Snippet(restricted == local ? text : $"{title}\n{location}", restricted)}", true);
        if (open.Success) return new("BR_ELIGIBLE_CONFIRMED", Snippet(text, open), true);
        if (restricted.Success) return new("BR_RESTRICTED", Snippet(restricted == local ? text : $"{title}\n{location}", restricted), true);
        // Remota com o local citando só países que não são o Brasil ("Remoto (India)", "Remote, Germany"): na prática, só para quem mora lá.
        if (remote && abroad)
            return new("BR_RESTRICTED", $"remota, mas o local cita só {string.Join(", ", countries.Select(c => SourceCatalog.Countries.FirstOrDefault(x => x.Iso2 == c)?.Name ?? c))}: {location.Trim()}", false);
        if (!remote && Relocation().Match(text) is { Success: true } move && !NoSponsor().IsMatch(text)) return new("RELOCATION_WITH_SPONSORSHIP", Snippet(text, move), true);
        if (remote && Contractor().Match(text) is { Success: true } b2b) return new("BR_ELIGIBLE_POSSIBLE", Snippet(text, b2b), true);
        if (remote) return new("BR_ELIGIBLE_POSSIBLE", "remota, sem dizer de onde se pode trabalhar: confirmar no anúncio", false);
        // "Híbrido" or "Presencial" with no country is not abroad: a Brazilian city or state in the ad says where.
        if (countries.Count == 0 && BrazilPlace().Match(description) is { Success: true } here) return new("BR_ELIGIBLE_CONFIRMED", Snippet(description, here), true);
        if (location.Trim().Length > 0) return new("RELOCATION_UNVERIFIED", $"presencial ou híbrida fora do Brasil, sem dizer de visto: {location.Trim()}", false);
        return new("UNKNOWN", "o anúncio não diz o local nem se é remota", false);
    }

    /// <summary>O trecho do anúncio em volta do que casou, numa linha, até 200 caracteres.</summary>
    private static string Snippet(string text, Match m)
    {
        var start = Math.Max(0, m.Index - 70);
        var end = Math.Min(text.Length, m.Index + m.Length + 70);
        var s = Regex.Replace(text[start..end], @"\s+", " ").Trim();
        return (start > 0 ? "…" : "") + (s.Length > 200 ? s[..200] : s) + (end < text.Length ? "…" : "");
    }

    [GeneratedRegex(@"\b(remot[eoa]s?|remote-first|home ?office|teletrabajo|trabajo remoto|anywhere|work from home|wfh)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"\b(brasil|brazil|br)\b|são paulo|sao paulo|rio de janeiro|belo horizonte|curitiba|porto alegre|florian[oó]polis|recife|salvador|bras[ií]lia|campinas|fortaleza|goi[aâ]nia|\b(sp|rj|mg|pr|sc|rs|df)\b\s*(,|$)", RegexOptions.IgnoreCase)]
    private static partial Regex InBrazil();
    // A Brazilian city, or a state after "em", "-" or "/", in the body of an ad: the strict version of InBrazil for long text.
    [GeneratedRegex(@"(?i:são paulo|sao paulo|rio de janeiro|belo horizonte|curitiba|porto alegre|florian[oó]polis|recife|bras[ií]lia|campinas|fortaleza|goi[aâ]nia)|(?:\b(?:em|no|na)|[-/])\s+(?:SP|RJ|MG|PR|SC|RS|DF|PE|BA|CE|GO|ES)\b")]
    private static partial Regex BrazilPlace();

    [GeneratedRegex(@"\b(brasil|brazil|latam|latin america|am[eé]rica latina|south america|am[eé]rica do sul|sudam[eé]rica|americas? timezones?|brazilian)\b", RegexOptions.IgnoreCase)]
    private static partial Regex OpenToBrazil();

    [GeneratedRegex(@"\b(worldwide|anywhere in the world|work from anywhere|from anywhere|global(ly)? remote|remote[ -]+global|fully distributed|any (country|location|timezone)|qualquer lugar do mundo|de qualquer lugar|todo el mundo|cualquier lugar)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Worldwide();

    [GeneratedRegex(@"\b(visa sponsorship (is )?(available|provided|offered)|we (will |can |do )?sponsor (your )?(work )?visas?|sponsor(s|ing)? (work )?visas?|relocation (package|support|assistance|bonus) (is )?(available|provided|offered|included)?|relocation (package|support|assistance)|blue card|skilled worker visa|lmia|zam[eě]stnaneck[aá] karta|employee card|cart[aã]o de funcion[aá]rio tcheco|patroc[ií]nio de visto|ajuda (de|com) (mudan[cç]a|realoca[cç][aã]o)|apoyo (de|con) reubicaci[oó]n)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Relocation();

    [GeneratedRegex(@"\b(no|not|unable to|cannot|can't|won't|will not|do not|does not) (provide |offer )?(visa )?sponsor(ship)?|sem patroc[ií]nio", RegexOptions.IgnoreCase)]
    private static partial Regex NoSponsor();

    [GeneratedRegex(@"\b(independent contractor|contractor|b2b|freelanc(e|er)|deel|remote\.com|oyster hr|employer of record|\beor\b|\bpj\b|pessoa jur[ií]dica)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Contractor();

    [GeneratedRegex(@"\b(us|u\.s\.|united states|american|canadian|uk|british|eu|saudi|emirati|qatari) (citizens?|citizenship|nationals?|nationality)( (only|required|is required))?\b|\b(security|secret|top secret|public trust) clearance\b|\bmust (be|hold) (a )?(us|u\.s\.) citizen|\bcidadania (americana|europeia|brit[aâ]nica) (obrigat[oó]ria|exigida)|\bclearance (required|eligible)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Citizenship();

    [GeneratedRegex(@"\b(must|need to|required to|should) (be )?(legally )?(authori[sz]ed|eligible|permitted) to work in (the )?(us|u\.s\.|usa|united states|canada|uk|united kingdom|eu|europe|european union|australia|germany|netherlands|ireland|sweden|norway|india|israel)\b|\b(right to work|work (permit|authori[sz]ation)) in (the )?(us|usa|uk|eu|canada|australia|europe)\b|\bmust (reside|live|be (located|based)) in (the )?(us|usa|united states|canada|uk|eu|europe|australia|india)\b|\b(us|usa|u\.s\.|uk|eu|canada)[ -]only\b|\bonly (candidates|applicants) (based|located|residing) in (the )?(us|usa|uk|eu|canada|europe)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LocalAuth();

    [GeneratedRegex(@"\bremot[eoa]s?\b[^\n]{0,12}\b(us|usa|u\.s\.|united states|eua|estados unidos|canada|canadá|uk|united kingdom|reino unido|eu|europe|europa|emea|apac|india|índia|germany|alemanha|australia|austrália)\b|\b(us|usa|eua|canada|canadá|uk|europe|europa|emea|apac|india|índia)\b[^\n]{0,6}\bremot[eoa]s?\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteRestricted();
}
