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
    // "que", "uma" and "não" are what tells Portuguese from Spanish: without them a question in Portuguese read as Spanish, and the
    // answer came out in English.
    private static readonly string[] Pt = ["de", "e", "com", "você", "voce", "nós", "nosso", "experiência", "experiencia", "time", "para", "em", "da", "do", "os", "as",
        "que", "não", "nao", "uma", "um", "é", "foi", "ou", "ao", "à", "pelo", "pela", "também", "já", "isso", "seu", "sua", "são", "está", "dos", "das", "nas", "qual", "quais", "quando"];

    public static string Detect(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).ToList();
        return words.Count(En.Contains) > words.Count(Pt.Contains) ? "en" : "pt";
    }

    /// <summary>Palavras comuns de cada língua das fontes (Suécia, Noruega, Dinamarca, Finlândia, Tchéquia, Polônia, Europa e América Latina).</summary>
    private static readonly Dictionary<string, string[]> Common = new()
    {
        ["es"] = ["el", "la", "los", "las", "y", "con", "usted", "nosotros", "experiencia", "equipo", "para", "en", "del", "una", "que", "por", "es", "de"],
        ["fr"] = ["le", "la", "les", "et", "avec", "vous", "nous", "notre", "expérience", "équipe", "pour", "dans", "des", "une", "est", "de"],
        ["de"] = ["der", "die", "das", "und", "mit", "sie", "wir", "unser", "erfahrung", "für", "ist", "eine", "von", "zu", "bei"],
        ["it"] = ["il", "gli", "con", "noi", "nostro", "esperienza", "squadra", "per", "del", "una", "che", "è", "di"],
        ["nl"] = ["het", "en", "met", "jij", "wij", "ons", "ervaring", "voor", "een", "van", "is", "je", "bij"],
        ["sv"] = ["och", "med", "vi", "vår", "erfarenhet", "för", "att", "är", "som", "på", "av", "till", "det", "har", "du"],
        ["da"] = ["og", "med", "vi", "vores", "erfaring", "for", "at", "er", "som", "på", "af", "til", "det", "har", "du"],
        ["no"] = ["og", "med", "vi", "vår", "erfaring", "for", "å", "er", "som", "på", "av", "til", "det", "har", "du"],
        ["fi"] = ["ja", "on", "kanssa", "sinä", "me", "meidän", "kokemus", "tiimi", "että", "tai", "olet"],
        ["pl"] = ["z", "w", "na", "jest", "dla", "się", "do", "oraz", "doświadczenie", "zespół", "nas"],
        ["cs"] = ["s", "v", "na", "je", "pro", "se", "do", "zkušenosti", "tým", "nebo", "práce"],
    };

    /// <summary>A língua do texto entre português, inglês e as outras das fontes; sem palavra conhecida, português (como Detect).</summary>
    public static string DetectAny(string text)
    {
        var words = System.Text.RegularExpressions.Regex.Matches(text.ToLowerInvariant(), @"\p{L}+").Select(m => m.Value).Take(600).ToList();
        var scores = Common.ToDictionary(kv => kv.Key, kv => words.Count(kv.Value.Contains));
        scores["en"] = words.Count(En.Contains);
        // ã, õ and ç exist only in Portuguese; ñ, ¿ and ¡ only in Spanish.
        scores["pt"] = words.Count(Pt.Contains) + text.Count(c => c is 'ã' or 'õ' or 'ç' or 'Ã' or 'Õ' or 'Ç');
        scores["es"] += text.Count(c => c is 'ñ' or '¿' or '¡' or 'Ñ');
        var best = scores.OrderByDescending(kv => kv.Value).First();
        return best.Value == 0 ? "pt" : best.Key;
    }

    /// <summary>Para o que só existe em português e inglês (currículo, respostas do formulário): português só para texto em português.</summary>
    public static string PtOrEn(string text) => DetectAny(text) == "pt" ? "pt" : "en";

    /// <summary>Nomes de cada língua no perfil, em português, em inglês e na própria língua.</summary>
    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["es"] = ["espanhol", "spanish", "español", "castellano"], ["fr"] = ["francês", "frances", "french", "français"],
        ["de"] = ["alemão", "alemao", "german", "deutsch"], ["it"] = ["italiano", "italian"], ["nl"] = ["holandês", "holandes", "neerlandês", "dutch", "nederlands"],
        ["sv"] = ["sueco", "swedish", "svenska"], ["da"] = ["dinamarquês", "dinamarques", "danish", "dansk"], ["no"] = ["norueguês", "noruegues", "norwegian", "norsk"],
        ["fi"] = ["finlandês", "finlandes", "finnish", "suomi"], ["pl"] = ["polonês", "polones", "polish", "polski"], ["cs"] = ["tcheco", "checo", "czech", "čeština"],
    };

    /// <summary>O nome da língua para o prompt ("Spanish", "German"...).</summary>
    public static string EnglishName(string code) => code switch
    {
        "pt" => "Portuguese", "en" => "English", "es" => "Spanish", "fr" => "French", "de" => "German", "it" => "Italian", "nl" => "Dutch",
        "sv" => "Swedish", "da" => "Danish", "no" => "Norwegian", "fi" => "Finnish", "pl" => "Polish", "cs" => "Czech", _ => "English",
    };

    /// <summary>
    /// Em que língua escrever a candidatura: a da vaga quando é português ou inglês; outra língua só se você a fala (pelos idiomas do
    /// perfil, de intermediário para cima). Senão, inglês: escrever numa língua que você não fala faria a empresa esperar que você fala.
    /// </summary>
    public static string Writing(string jobLanguage, IEnumerable<string> profileLanguages)
    {
        if (jobLanguage is "pt" or "en") return jobLanguage;
        if (!Names.TryGetValue(jobLanguage, out var names)) return "en";
        var speaks = profileLanguages.Select(l => l.ToLowerInvariant())
            .Any(l => names.Any(l.Contains) && !System.Text.RegularExpressions.Regex.IsMatch(l, @"b[aá]sico|basic|beginner|iniciante|elementar|elementary|\ba[12]\b"));
        return speaks ? jobLanguage : "en";
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
