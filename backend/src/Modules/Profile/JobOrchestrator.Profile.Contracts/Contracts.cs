namespace JobOrchestrator.Profile.Contracts;

// Contratos públicos do módulo Profile: o perfil verdadeiro. Fatos com evidência, confirmações e o banco de respostas.

/// <summary>Dados pessoais. Nunca vão para modelo de IA: entram só na renderização (placeholders) e no formulário.</summary>
public sealed record Identity(string FullName, string? Email, string? Phone, string? Location, string? LinkedIn, string? GitHub, string? Website)
{
    public string FirstName => FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
    public string LastName => FullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) ?? "";
}

/// <summary>
/// Um fato do perfil, como os outros módulos veem: só os verificados (trecho achado no documento) e os confirmados por você.
/// Kind: experience, achievement (ligado a uma experiência por ParentId), education, skill, language, certification, project, headline, summary.
/// </summary>
public sealed record ProfileFact(
    Guid Id, string Kind, string Statement, Guid? ParentId, string? Organization, string? Role, string? Start, string? End,
    IReadOnlyList<string> Technologies, string? Level, string Evidence);

/// <summary>O que só você pode afirmar, respondido uma vez na tela única (docs/PLANO.md §3.1).</summary>
public sealed record Confirmations(
    IReadOnlyList<string> AuthorizedCountries, bool NeedsSponsorship,
    int? FloorCltBrl, int? FloorPjBrl, int? FloorUsdMonth,
    IReadOnlyList<string> Modalities, IReadOnlyList<string> Cities, bool Relocate,
    string? StartAvailability, string? EnglishLevel, string SelfIdDefault, string OpenAnswersPolicy, DateTime? ConfirmedAt)
{
    public static Confirmations Empty { get; } = new(["BR"], false, null, null, null, ["remote"], [], false, null, null, "prefer_not", "review", null);
}

/// <summary>O perfil inteiro para quem compõe materiais e preenche formulários. Hash muda a cada versão.</summary>
public sealed record ProfileSnapshot(
    Guid ProfileId, int Version, string Hash, bool Synthetic, bool Confirmed, Identity Identity,
    IReadOnlyList<ProfileFact> Facts, Confirmations Confirmations);

/// <summary>A vaga, para respostas que dependem dela: pretensão pelo vínculo e moeda, anos com as tecnologias que ela pede.</summary>
public sealed record AnswerContext(string? Contract, string? Currency, string? Country, IReadOnlyList<string> Technologies, string Language, string Source);

/// <summary>Idioma de um texto pelas palavras comuns: "en" quando o inglês ganha do português.</summary>
public static class Languages
{
    private static readonly string[] En = ["the", "and", "with", "you", "we", "our", "experience", "team", "will", "of", "to", "in", "for", "are"];
    private static readonly string[] Pt = ["de", "e", "com", "você", "voce", "nós", "nosso", "experiência", "experiencia", "time", "para", "em", "da", "do", "os", "as"];

    public static string Detect(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).ToList();
        return words.Count(En.Contains) > words.Count(Pt.Contains) ? "en" : "pt";
    }
}

/// <summary>Chaves do banco de respostas e o significado curto que vai no prompt do question.map.</summary>
public static class AnswerKeys
{
    public static IReadOnlyList<(string Key, string Meaning)> All { get; } =
    [
        ("full_name", "candidate's full name"),
        ("email", "e-mail address"),
        ("phone", "phone number"),
        ("location", "city, state or country where the candidate lives"),
        ("linkedin_url", "LinkedIn profile URL"),
        ("github_url", "GitHub profile URL"),
        ("portfolio_url", "personal website or portfolio URL"),
        ("work_authorization", "whether the candidate is legally authorized to work in the job's country"),
        ("needs_sponsorship", "whether the candidate needs visa sponsorship now or in the future"),
        ("salary_expectation", "expected salary or compensation"),
        ("start_date", "earliest start date or notice period"),
        ("english_level", "English proficiency level"),
        ("years_experience", "years of experience with a technology or in a role"),
        ("relocation", "willingness to relocate"),
        ("work_modality", "remote, hybrid or on-site preference or availability"),
        ("education", "highest degree or current studies"),
        ("how_heard", "how the candidate heard about the job"),
        ("cover_letter", "a cover letter or message to the hiring team"),
        ("self_id", "voluntary self-identification: gender, race, ethnicity, disability, veteran status, pronouns"),
        ("open_question", "an open question that needs a written answer"),
        ("assessment", "a technical test, coding exercise or knowledge question"),
        ("unknown", "nothing above fits")
    ];
}

/// <summary>Contrato público do módulo Profile.</summary>
public interface IProfileApi
{
    Task<ProfileSnapshot?> CurrentAsync(CancellationToken ct);

    /// <summary>Respostas objetivas para uma vaga, por chave do banco. Sem fato ou confirmação que sustente, a chave não aparece.</summary>
    Task<IReadOnlyDictionary<string, string>> AnswersAsync(AnswerContext context, CancellationToken ct);
}
