namespace JobOrchestrator.Automation.Contracts;

/// <summary>Explicit data deletion also closes browsers and removes their local authentication state.</summary>
public interface ILocalBrowserSessions
{
    Task ClearAsync(CancellationToken ct);
}
