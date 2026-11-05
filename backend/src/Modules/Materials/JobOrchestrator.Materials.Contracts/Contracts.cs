namespace JobOrchestrator.Materials.Contracts;

// Contratos públicos do módulo Materials: currículo por vaga, carta e guia de entrevista, sempre com fonte para cada afirmação.

/// <summary>Pedido de currículo para uma candidatura.</summary>
public sealed record ComposeRequest(Guid ApplicationId, Guid JobId, string Company, string Title);

/// <summary>
/// O currículo pronto, ou por que não ficou. Mode: ai (nuvem escreveu e outra família revisou), local (rascunho do modelo local),
/// plain (sem IA: os fatos como estão). Paused: nenhum provedor e a configuração manda esperar.
/// </summary>
public sealed record ComposeResult(bool Ok, Guid? VersionId, string? PdfPath, string? Hash, string Language, string Mode, bool Reviewed, string? Error, bool Paused, Guid? ThreadId);

/// <summary>Contrato público do módulo Materials.</summary>
public interface IMaterialsApi
{
    Task<ComposeResult> ComposeResumeAsync(ComposeRequest request, CancellationToken ct);
}
