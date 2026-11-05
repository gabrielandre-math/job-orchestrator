using System.Security.Cryptography;
using System.Text;

namespace JobOrchestrator.Automation.Contracts;

// Contratos públicos do módulo Automation: adapters, roteamento e navegador ficam atrás desta porta.

/// <summary>O que um adapter sabe fazer.</summary>
public enum Capability { DiscoverJobs, CheckPostingOpen, PrepareForm, SubmitApplication, ReconcileSubmission }

/// <summary>Como uma operação de adapter terminou. O roteador decide o fallback a partir daqui.</summary>
public enum AdapterResultKind { Completed, Unsupported, RetryableFailure, PermanentFailure, NeedsHuman, BlockedByPolicy, QuotaExceeded, SubmissionUnknown }

/// <summary>Uma vaga como veio da fonte, antes de normalizar. ListedAsOpen: a API oficial só lista vagas abertas, então não precisa conferir uma por uma.</summary>
public sealed record DiscoveredPosting(
    string Source, string ExternalId, string Company, string Title, string Location, string ApplyUrl, string Description, DateTime? UpdatedAt, string RawJson,
    string? Workplace = null, string? Compensation = null, string? Employment = null, string? ApplyEmail = null, bool ListedAsOpen = false);

/// <summary>Se a vaga segue aberta na fonte oficial.</summary>
public sealed record PostingStatus(bool Open, string Detail);

/// <summary>Onde a candidatura acontece.</summary>
public sealed record ApplyTarget(Guid ApplicationId, string ApplyUrl);

/// <summary>Tudo que vai no formulário: contato, PDF do currículo e respostas aprovadas. Nunca vai para log.</summary>
public sealed record MaterialBundle(string FirstName, string LastName, string Email, string Phone, string LinkedIn, string ResumePdfPath, IReadOnlyDictionary<string, string> Answers);

/// <summary>Um campo preenchido e de onde veio o valor.</summary>
public sealed record FieldValue(string Label, string Value, string Source);

/// <summary>O formulário preenchido e validado, sem envio.</summary>
public sealed record PreparedForm(string AdapterId, string AdapterVersion, string Domain, string PayloadHash, IReadOnlyList<FieldValue> Fields);

/// <summary>Um passo do roteamento: qual adapter, com que nota e por quê.</summary>
public sealed record RoutingStep(string AdapterId, double Score, string Outcome, string Why);

/// <summary>Resultado de uma operação de automação, com o caminho que o roteador fez.</summary>
public sealed record AutomationOutcome<T>(AdapterResultKind Kind, T? Value, string? ReasonCode, string Detail, IReadOnlyList<RoutingStep> Route)
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
public interface IAutomationApi
{
    Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct);
    Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct);
    Task<AutomationOutcome<PreparedForm>> PrepareAsync(ApplyTarget target, MaterialBundle materials, CancellationToken ct);
    Task<AutomationOutcome<SubmissionReceipt>> SubmitAsync(ApplyTarget target, MaterialBundle materials, SubmitToken token, string adapterId, CancellationToken ct);
    Task<AutomationOutcome<ReconcileResult>> ReconcileAsync(ApplyTarget target, MaterialBundle materials, string adapterId, CancellationToken ct);
    Task RenderPdfAsync(string html, string path, CancellationToken ct);
    Task<IReadOnlyList<AdapterView>> ListAdaptersAsync(CancellationToken ct);
    /// <summary>Boards de ATS citados numa página de carreira (respeita o robots.txt).</summary>
    Task<(IReadOnlyList<string> Boards, string Detail)> FindBoardsAsync(string pageUrl, CancellationToken ct);
}
