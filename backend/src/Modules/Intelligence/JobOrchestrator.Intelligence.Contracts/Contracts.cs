namespace JobOrchestrator.Intelligence.Contracts;

// Contratos públicos do módulo Intelligence: IA só local ou pelos CLIs oficiais das assinaturas. API paga não existe aqui.

/// <summary>Degrau de modelo e esforço (docs/integracao-ia.md §10). Top nunca é a primeira tentativa.</summary>
public enum Rung { Local, Light, Medium, Strong, Top }

/// <summary>Um pedido de IA: tarefa, instruções fixas, conteúdo (dado, não instrução) e o schema da resposta.</summary>
public sealed record LlmRequest(string Task, string System, string Prompt, string JsonSchema, Rung Rung, string? AvoidFamily = null, int TimeoutSeconds = 180);

/// <summary>A resposta, com quem fez, qual modelo, qual esforço e quanto tempo levou.</summary>
public sealed record LlmResult(bool Ok, string? Json, string ProviderId, string Model, string Effort, string? Error, double Seconds);

/// <summary>Janela de uso de uma assinatura. Sem dado, o percentual é nulo (aparece como "sem dado").</summary>
public sealed record UsageWindow(string Name, int? UsedPercent, DateTime? ResetsAt);

/// <summary>Um provedor como a tela mostra.</summary>
public sealed record ProviderView(
    string Id, string Name, string Family, int Order, bool Installed, string? Version, string Login, string? Plan,
    IReadOnlyList<UsageWindow> Usage, bool Available, string Status, string Detail, LlmResult? LastTest);

/// <summary>Contrato público do módulo Intelligence.</summary>
public interface IIntelligenceApi
{
    Task<IReadOnlyList<ProviderView>> ProvidersAsync(CancellationToken ct);
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
    Task<LlmResult> TestAsync(string providerId, CancellationToken ct);
}
