namespace JobOrchestrator.Applications.Contracts;

// Contratos públicos do módulo Applications.

/// <summary>Evento de integração: uma candidatura mudou de estado. O acompanhamento (CRM) escuta este evento.</summary>
public sealed record ApplicationStateChangedV1(Guid ApplicationId, Guid CampaignId, string From, string To, DateTime At)
{
    public const string EventType = "applications.state-changed.v1";
}

/// <summary>Where the applications are now, for what other modules show or say (the assistant answers with these numbers).</summary>
public sealed record ApplicationCounts(int AwaitingApproval, int WaitingForAnswer, int ToSendYourself, int InProgress, int Sent, int SentToday, int Failed, int Withdrawn);

public interface IApplicationsApi
{
    Task<ApplicationCounts> CountsAsync(CancellationToken ct);
}
