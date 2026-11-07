namespace JobOrchestrator.Campaigns.Contracts;

// Contratos públicos do módulo Campaigns: outros módulos só conhecem o que está aqui, nunca as tabelas.

/// <summary>Até onde a campanha pode ir sozinha. O envio automático só existe com autorização dada no painel.</summary>
public enum SubmissionMode { DiscoveryOnly, PrepareOnly, ReviewRequired, AutoSubmit }

/// <summary>
/// Autorização de envio automático dada na tela: validade, teto por dia, nota mínima e quais adapters podem enviar (vazio = todos com
/// envio testado). AcceptDeclarations: you also accept, in your name, the privacy, data processing and terms boxes forms require.
/// </summary>
public sealed record SubmitAuthorizationView(Guid Id, DateTime GrantedAt, DateTime ExpiresAt, int MaxPerDay, int MinScore, IReadOnlyList<string> Adapters, bool AcceptDeclarations = false)
{
    public bool ValidAt(DateTime now) => now < ExpiresAt;
    public bool Allows(string? adapterId) => Adapters.Count == 0 || adapterId is not null && Adapters.Contains(adapterId);
}

/// <summary>O que os outros módulos precisam saber de uma campanha. Authorization é a autorização ativa, se houver.</summary>
public sealed record CampaignSettings(
    Guid Id, string Name, bool Enabled, SubmissionMode Mode, int MinFitScore, int MaxSubmissionsPerDay,
    IReadOnlyList<string> Technologies, IReadOnlyList<string> Locations, IReadOnlyList<string> Sources, string TimeZone,
    SubmitAuthorizationView? Authorization = null, IReadOnlyList<string>? Titles = null);

/// <summary>Evento de integração: alguém pediu uma rodada de descoberta. Only limita a rodada a essas fontes (as que o Explorador acabou de achar).</summary>
public sealed record RunRequestedV1(Guid RunId, Guid CampaignId, DateTime RequestedAt, IReadOnlyList<string>? Only = null)
{
    public const string EventType = "campaigns.run-requested.v1";
}

/// <summary>Evento de integração: o envio automático foi desligado (revogado ou trocado de modo). O que estava pronto pela autorização volta para você.</summary>
public sealed record AutoSubmitEndedV1(Guid CampaignId, DateTime At)
{
    public const string EventType = "campaigns.auto-submit-ended.v1";
}

/// <summary>
/// Integration event: a new authorization was granted. What waited for you under the old limits is checked again under the new
/// ones, and, when it accepts the declarations, what stopped on a privacy or terms box goes back to the form.
/// </summary>
public sealed record AutoSubmitGrantedV1(Guid CampaignId, bool AcceptDeclarations, DateTime At)
{
    public const string EventType = "campaigns.auto-submit-granted.v1";
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
