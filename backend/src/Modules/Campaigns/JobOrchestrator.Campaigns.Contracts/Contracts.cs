namespace JobOrchestrator.Campaigns.Contracts;

// Contratos públicos do módulo Campaigns: outros módulos só conhecem o que está aqui, nunca as tabelas.

/// <summary>Até onde a campanha pode ir sozinha. O envio automático só existe com autorização dada no painel.</summary>
public enum SubmissionMode { DiscoveryOnly, PrepareOnly, ReviewRequired, AutoSubmit }

/// <summary>Autorização de envio automático dada na tela: validade, teto por dia, nota mínima e quais adapters podem enviar (vazio = todos com envio testado).</summary>
public sealed record SubmitAuthorizationView(Guid Id, DateTime GrantedAt, DateTime ExpiresAt, int MaxPerDay, int MinScore, IReadOnlyList<string> Adapters)
{
    public bool ValidAt(DateTime now) => now < ExpiresAt;
    public bool Allows(string? adapterId) => Adapters.Count == 0 || adapterId is not null && Adapters.Contains(adapterId);
}

/// <summary>O que os outros módulos precisam saber de uma campanha. Authorization é a autorização ativa, se houver.</summary>
public sealed record CampaignSettings(
    Guid Id, string Name, bool Enabled, SubmissionMode Mode, int MinFitScore, int MaxSubmissionsPerDay,
    IReadOnlyList<string> Technologies, IReadOnlyList<string> Locations, IReadOnlyList<string> Sources, string TimeZone,
    SubmitAuthorizationView? Authorization = null, IReadOnlyList<string>? Titles = null);

/// <summary>Evento de integração: alguém pediu uma rodada de descoberta.</summary>
public sealed record RunRequestedV1(Guid RunId, Guid CampaignId, DateTime RequestedAt)
{
    public const string EventType = "campaigns.run-requested.v1";
}

/// <summary>Contrato público do módulo Campaigns.</summary>
public interface ICampaignsApi
{
    Task<CampaignSettings?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<CampaignSettings>> ListAsync(CancellationToken ct);
}
