namespace JobOrchestrator.Applications.Domain;

/// <summary>
/// What the AI answered, from your profile's facts, to a required question of one application's form. It stays with the application:
/// the send fills the same form the preparation saw, the screen shows what went in your name, and a question already tried is not
/// sent to the AI again. Accepted false: the answer did not pass the check (Note says why) and the question waits for you.
/// </summary>
internal sealed class FormAnswer
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    /// <summary>The question without accents, case or punctuation (CustomAnswer.Normalize): the same question is the same row.</summary>
    public string QuestionKey { get; init; } = "";
    public string Question { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Answer { get; init; } = "";
    public bool Accepted { get; init; }
    public string? Note { get; init; }
    /// <summary>The facts the answer rests on, as the AI cited them.</summary>
    public string Basis { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string Model { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
