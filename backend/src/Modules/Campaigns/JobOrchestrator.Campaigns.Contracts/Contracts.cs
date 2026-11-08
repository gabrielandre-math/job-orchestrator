namespace JobOrchestrator.Campaigns.Contracts;

// Contratos públicos do módulo Campaigns: outros módulos só conhecem o que está aqui, nunca as tabelas.

/// <summary>
/// Até onde a campanha vai: só buscar ou buscar e preparar. Quem envia é sempre você, com o copiloto; ReviewRequired e AutoSubmit
/// são valores antigos, guardados só porque o histórico os cita, e valem como PrepareOnly.
/// </summary>
public enum SubmissionMode { DiscoveryOnly, PrepareOnly, ReviewRequired, AutoSubmit }

/// <summary>O que os outros módulos precisam saber de uma campanha.</summary>
public sealed record CampaignSettings(
    Guid Id, string Name, bool Enabled, SubmissionMode Mode, int MinFitScore, int MaxSubmissionsPerDay,
    IReadOnlyList<string> Technologies, IReadOnlyList<string> Locations, IReadOnlyList<string> Sources, string TimeZone,
    IReadOnlyList<string>? Titles = null);

/// <summary>Evento de integração: alguém pediu uma rodada de descoberta. Only limita a rodada a essas fontes (as que o Explorador acabou de achar).</summary>
public sealed record RunRequestedV1(Guid RunId, Guid CampaignId, DateTime RequestedAt, IReadOnlyList<string>? Only = null)
{
    public const string EventType = "campaigns.run-requested.v1";
}

/// <summary>
/// Integration event: the campaign left "só buscar". Jobs found while it only searched never got an application; now the ones still in
/// the queue get theirs.
/// </summary>
public sealed record PreparationStartedV1(Guid CampaignId, DateTime At)
{
    public const string EventType = "campaigns.preparation-started.v1";
}

/// <summary>Contrato público do módulo Campaigns.</summary>
/// <summary>Pede rodadas sem o botão: o timer da noite pede uma para cada campanha ligada e com fontes.</summary>
public interface ICampaignRuns
{
    Task<int> RequestEnabledAsync(string trigger, CancellationToken ct);
    /// <summary>Busca contínua: pede rodada das campanhas ligadas cuja última rodada passou do intervalo.</summary>
    Task<int> RequestDueAsync(TimeSpan every, string trigger, CancellationToken ct);
    /// <summary>O Explorador acrescenta fontes descobertas (sem duplicar) e devolve quantas entraram.</summary>
    Task<int> AddSourcesAsync(Guid campaignId, IReadOnlyList<string> sources, CancellationToken ct);
    /// <summary>Rodada só com estas fontes, na hora: as vagas das fontes novas chegam sem esperar a próxima rodada completa.</summary>
    Task<bool> RequestSourcesAsync(Guid campaignId, IReadOnlyList<string> sources, string trigger, CancellationToken ct);
    /// <summary>Tira da campanha as fontes que saíram do ar (404 em várias rodadas seguidas).</summary>
    Task<int> RemoveSourcesAsync(Guid campaignId, IReadOnlyList<string> sources, CancellationToken ct);
}

public interface ICampaignsApi
{
    Task<CampaignSettings?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CampaignSettings>> ListAsync(CancellationToken ct);
}
