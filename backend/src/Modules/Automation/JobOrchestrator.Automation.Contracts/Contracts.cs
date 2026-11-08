using System.Security.Cryptography;
using System.Text;

namespace JobOrchestrator.Automation.Contracts;

// Contratos públicos do módulo Automation: adapters, roteamento e navegador ficam atrás desta porta.

/// <summary>O que um adapter sabe fazer.</summary>
public enum Capability { DiscoverJobs, CheckPostingOpen, PrepareForm, SubmitApplication, ReconcileSubmission }

/// <summary>Como uma operação de adapter terminou. O roteador decide o fallback a partir daqui.</summary>
public enum AdapterResultKind { Completed, Unsupported, RetryableFailure, PermanentFailure, NeedsHuman, BlockedByPolicy, QuotaExceeded, SubmissionUnknown }

/// <summary>Uma vaga como veio da fonte, antes de normalizar. ListedAsOpen: a API oficial só lista vagas abertas, então não precisa conferir uma por uma.</summary>
/// <summary>Vaga lida de uma fonte. Tombstone: a fonte declarou a vaga encerrada (ex.: INACTIVE na NAV); fecha a que existe e nunca cria linha nova.</summary>
public sealed record DiscoveredPosting(
    string Source, string ExternalId, string Company, string Title, string Location, string ApplyUrl, string Description, DateTime? UpdatedAt, string RawJson,
    string? Workplace = null, string? Compensation = null, string? Employment = null, string? ApplyEmail = null, bool ListedAsOpen = false,
    IReadOnlyList<string>? AllowedCountries = null, bool Tombstone = false, string? DescriptionMarkup = null, DateTime? ValidThrough = null);

/// <summary>Pista de fonte achada pelo Explorador (greenhouse:x, gupy:y, github:dono/repo, web:empresa.com) e de onde ela veio.</summary>
public sealed record SourceLead(string Source, string Origin);

/// <summary>O que a varredura achou num site: quadros de ATS linkados e vagas publicadas no próprio site.</summary>
public sealed record SiteFinding(string Domain, IReadOnlyList<string> Boards, IReadOnlyList<DiscoveredPosting> Postings);

/// <summary>Se a vaga segue aberta na fonte oficial.</summary>
public sealed record PostingStatus(bool Open, string Detail);

/// <summary>Onde a candidatura acontece.</summary>
public sealed record ApplyTarget(Guid ApplicationId, string ApplyUrl);

/// <summary>
/// Tudo que vai no formulário: contato, PDF do currículo e respostas aprovadas. Nunca vai para log.
/// AcceptDeclarations: your authorization lets the app tick, in your name, the privacy, data processing and terms boxes a form requires.
/// </summary>
public sealed record MaterialBundle(string FirstName, string LastName, string Email, string Phone, string LinkedIn, string ResumePdfPath, IReadOnlyDictionary<string, string> Answers,
    bool AcceptDeclarations = false);

/// <summary>
/// A required form question the app had no answer for. Kind: text, textarea, number, select, radio, checkboxes (several options may
/// be ticked), confirm (one box that states a fact about you) or declaration (privacy, terms). Options: what the form offers to pick.
/// </summary>
public sealed record FormQuestion(string Label, string Kind, IReadOnlyList<string> Options)
{
    /// <summary>Several ticked options travel as one answer, one option per line.</summary>
    public const char ChoiceSeparator = '\n';
}

/// <summary>Resposta sua a uma pergunta de formulário que o perfil não responde: a chave é o texto da pergunta sem acento, caixa ou pontuação.</summary>
public static class CustomAnswer
{
    public static string Key(string question) => "custom:" + (PersonalAnswer.IsBirthDate(question) ? "birth_date" : Normalize(question));

    /// <summary>The answer the AI wrote from your profile for this application's form, kept apart from yours: yours always wins.</summary>
    public static string AiKey(string question) => "ai:" + Normalize(question);

    public static string Normalize(string question)
    {
        var sb = new StringBuilder();
        foreach (var ch in question.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        var words = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return words.Length > 200 ? words[..200] : words;
    }
}

/// <summary>Um campo preenchido e de onde veio o valor.</summary>
public sealed record FieldValue(string Label, string Value, string Source);

/// <summary>
/// O formulário preenchido e validado, sem envio. HumanCheck: o site confirma no envio que é uma pessoa (reCAPTCHA invisível, código por
/// e-mail) ou recusou todos os envios do robô; o robô não passa por isso, então o clique final é seu, com o copiloto.
/// </summary>
public sealed record PreparedForm(string AdapterId, string AdapterVersion, string Domain, string PayloadHash, IReadOnlyList<FieldValue> Fields, string? HumanCheck = null);

/// <summary>Um passo do roteamento: qual adapter, com que nota e por quê.</summary>
public sealed record RoutingStep(string AdapterId, double Score, string Outcome, string Why);

/// <summary>
/// Resultado de uma operação de automação, com o caminho que o roteador fez. Questions: the required questions left without an
/// answer, when that is why the form stopped.
/// </summary>
public sealed record AutomationOutcome<T>(AdapterResultKind Kind, T? Value, string? ReasonCode, string Detail, IReadOnlyList<RoutingStep> Route, IReadOnlyList<FormQuestion>? Questions = null)
{
    public bool Completed => Kind == AdapterResultKind.Completed;
}

/// <summary>Prova de envio. Nível 3 é a página de confirmação; nível 4, o número do recibo.</summary>
public sealed record SubmissionReceipt(string ReceiptId, int EvidenceLevel, string Evidence);

/// <summary>Reconciliação só de leitura depois de um envio incerto.</summary>
public sealed record ReconcileResult(bool Found, string? ReceiptId, string Detail);

/// <summary>Um adapter como aparece no painel.</summary>
public sealed record AdapterView(
    string Id, string Version, string Name, string Kind, IReadOnlyList<string> Capabilities, IReadOnlyList<string> Domains,
    int Attempts, int Confirmed, int Failures, bool CircuitOpen, DateTime? CircuitOpenUntil);

/// <summary>
/// Autorização de um envio, emitida só pelo Guardião (módulo Applications) e conferida pelo adapter antes do clique.
/// Amarrada à intenção, ao payload aprovado e a um prazo curto.
/// </summary>
public sealed record SubmitToken(Guid IntentId, Guid ApplicationId, string PayloadHash, DateTime ExpiresAt, string Signature)
{
    public static string Sign(byte[] key, Guid intentId, Guid applicationId, string payloadHash, DateTime expiresAt)
    {
        var data = Encoding.UTF8.GetBytes($"{intentId:N}|{applicationId:N}|{payloadHash}|{expiresAt.Ticks}");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, data));
    }

    public bool IsValid(byte[] key, DateTime now) =>
        ExpiresAt > now && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Sign(key, IntentId, ApplicationId, PayloadHash, ExpiresAt)), Encoding.ASCII.GetBytes(Signature));
}

/// <summary>Contrato público do módulo Automation.</summary>
/// <summary>Uma plataforma do registro como está agora: estado, países, se há conector, se o portão está aberto e a última sondagem.</summary>
public sealed record RegistryEntry(string Prefix, string Name, string Lifecycle, string[] Countries, bool Connector, bool GateOpen, bool? ProbeOk, int ProbeCount);

/// <summary>
/// What the app does with one application, before you trust it with the night. Confirm: receipt (the ATS confirmation page), smtp
/// (accepted by the mail server) or declared (you say it was sent). Recover: portal (the site is asked), email (you check the
/// confirmation e-mail), uncertain (an interrupted send waits for you) or you.
/// </summary>
public sealed record ApplyCapability(string Channel, string? Platform, bool Prepare, bool Submit, string Confirm, string Recover);

/// <summary>One platform the app fills and sends by itself, on its real sites (the test ATS left out).</summary>
public sealed record PlatformCapability(string Id, string Name, IReadOnlyList<string> Domains, bool Prepare, bool Submit, bool Confirm, bool Recover);

public interface IAutomationApi
{
    ApplyCapability CapabilityOf(string applyUrl, string channel);
    IReadOnlyList<PlatformCapability> Platforms();
    /// <summary>O registro de fontes ao vivo, para a cobertura por país.</summary>
    Task<IReadOnlyList<RegistryEntry>> RegistryAsync(CancellationToken ct);
    Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct);
    /// <summary>Mesma descoberta com as palavras da campanha: fonte que só lê parte das descrições (Gupy) lê primeiro as da sua área.</summary>
    Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct);
    /// <summary>Busca pública do portal da Gupy (o robots.txt libera): os nomes das empresas com vagas do termo, para o Explorador.</summary>
    Task<IReadOnlyList<string>> SearchGupyAsync(string term, CancellationToken ct);
    /// <summary>Repositórios do GitHub com issues de vaga abertas no último mês (busca oficial de issues).</summary>
    Task<IReadOnlyList<SourceLead>> JobReposAsync(string query, CancellationToken ct);
    /// <summary>Lista de empresas mantida num README do GitHub: cada link vira quadro de ATS (gupy:x, lever:y) ou site para a varredura.</summary>
    Task<IReadOnlyList<SourceLead>> CompanyListAsync(string repo, CancellationToken ct);
    /// <summary>Empresas de uma plataforma pelo sitemap que o robots.txt dela indica (hoje o Pandapé), mais vagas primeiro.</summary>
    Task<IReadOnlyList<SourceLead>> PlatformLeadsAsync(string platform, CancellationToken ct);
    /// <summary>Busca na web pela API oficial do buscador (Brave Search, com a chave do usuário); cada resultado vira pista.</summary>
    Task<IReadOnlyList<SourceLead>> WebSearchAsync(string query, CancellationToken ct);
    /// <summary>Quais chaves de API das fontes estão conectadas (adzuna, jooble, brave, github).</summary>
    Task<IReadOnlySet<string>> ApiKeysAsync(CancellationToken ct);
    /// <summary>Varre vários sites numa rodada só do coletor (robots.txt, uma conexão por domínio) e separa o que achou por site.</summary>
    Task<IReadOnlyList<SiteFinding>> SweepSitesAsync(IReadOnlyList<string> domains, CancellationToken ct);
    Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct);
    Task<AutomationOutcome<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct);
    Task<AutomationOutcome<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, string adapterId, CancellationToken ct);
    Task<AutomationOutcome<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, string adapterId, CancellationToken ct);
    Task RenderPdfAsync(string html, string path, CancellationToken ct);
    Task<IReadOnlyList<AdapterView>> ListAdaptersAsync(CancellationToken ct);
    /// <summary>Boards de ATS citados numa página de carreira (respeita o robots.txt).</summary>
    Task<(IReadOnlyList<string> Boards, string Detail)> FindBoardsAsync(string pageUrl, CancellationToken ct);
}

/// <summary>Vaga que chegou num alerta de vaga no e-mail da pessoa (LinkedIn, Indeed, Catho, InfoJobs, Glassdoor, Vagas.com e outros).</summary>
public sealed record AlertJob(string Sender, string Title, string Company, string Location, string Url, string Snippet, DateTime ReceivedAt);

/// <summary>Quem lê a caixa de e-mail entrega aqui as vagas dos alertas; a fonte "alertas:email" as leva para a campanha.</summary>
public interface IJobAlertFeed
{
    Task<IReadOnlyList<AlertJob>> RecentAsync(DateTime since, CancellationToken ct);
}

/// <summary>Personal answers come only from the local, user-approved bank. Never ask a model to invent them.</summary>
public static class PersonalAnswer
{
    public static bool IsBirthDate(string? label) => !string.IsNullOrWhiteSpace(label) && System.Text.RegularExpressions.Regex.IsMatch(CustomAnswer.Normalize(label),
        @"\b(data (de )?nascimento|date of birth|birth ?date|birthday|dob|fecha de nacimiento)\b");

    public static bool TryBirthDate(string value, DateOnly today, out DateOnly date) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date)
        && date <= today && date >= today.AddYears(-120);

    public static string FormatBirthDate(string iso, string label, string type, string? placeholder)
    {
        if (!DateOnly.TryParseExact(iso, "yyyy-MM-dd", out var date)) return "";
        if (type == "date") return iso;
        var hint = (placeholder ?? "").ToLowerInvariant();
        var separator = hint.Contains('-') ? "-" : "/";
        var format = hint.StartsWith("yyyy", StringComparison.Ordinal) || hint.StartsWith("aaaa", StringComparison.Ordinal) ? $"yyyy{separator}MM{separator}dd"
            : hint.StartsWith("mm", StringComparison.Ordinal) ? $"MM{separator}dd{separator}yyyy"
            : hint.StartsWith("dd", StringComparison.Ordinal) || CustomAnswer.Normalize(label).Contains("nascimento", StringComparison.Ordinal)
                || CustomAnswer.Normalize(label).Contains("nacimiento", StringComparison.Ordinal) ? $"dd{separator}MM{separator}yyyy" : $"MM{separator}dd{separator}yyyy";
        return date.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
    }
}
