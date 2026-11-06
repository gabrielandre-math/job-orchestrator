namespace JobOrchestrator.Materials.Contracts;

// Contratos públicos do módulo Materials: currículo por vaga, carta e guia de entrevista, sempre com fonte para cada afirmação.

/// <summary>Pedido de currículo para uma candidatura.</summary>
public sealed record ComposeRequest(Guid ApplicationId, Guid JobId, string Company, string Title);

/// <summary>
/// O currículo pronto, ou por que não ficou. Mode: ai (nuvem escreveu e outra família revisou), local (rascunho do modelo local),
/// plain (sem IA: os fatos como estão). Paused: nenhum provedor e a configuração manda esperar.
/// </summary>
public sealed record ComposeResult(bool Ok, Guid? VersionId, string? PdfPath, string? Hash, string Language, string Mode, bool Reviewed, string? Error, bool Paused, Guid? ThreadId);

/// <summary>Texto profissional do currículo final, encontrado pelo hash da candidatura.</summary>
public sealed record ResumeInterviewContext(Guid VersionId, string Sha256, string Text);

/// <summary>Contrato público do módulo Materials.</summary>
public interface IMaterialsApi
{
    Task<ComposeResult> ComposeResumeAsync(ComposeRequest request, CancellationToken ct);
    Task<ResumeInterviewContext?> ResumeForInterviewAsync(Guid applicationId, string sha256, CancellationToken ct);
    /// <summary>Currículo base, sem vaga e sem IA: só os fatos do perfil (o ensaio dos formulários anexa este).</summary>
    Task<ComposeResult> ComposeBaseAsync(string language, string theme, CancellationToken ct);
}
