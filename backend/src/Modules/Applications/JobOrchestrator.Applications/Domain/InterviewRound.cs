namespace JobOrchestrator.Applications.Domain;

internal sealed class InterviewRound
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string Stage { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string Pack { get; init; } = "";
    public string? Feedback { get; set; }
}
