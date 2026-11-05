namespace JobOrchestrator.Applications.Contracts;

// Contratos públicos do módulo Applications.

/// <summary>Evento de integração: uma candidatura mudou de estado. O acompanhamento (CRM) escuta este evento.</summary>
public sealed record ApplicationStateChangedV1(Guid ApplicationId, Guid CampaignId, string From, string To, DateTime At)
{
    public const string EventType = "applications.state-changed.v1";
}
