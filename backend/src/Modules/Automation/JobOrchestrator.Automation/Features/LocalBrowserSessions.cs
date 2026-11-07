namespace JobOrchestrator.Automation.Features;

internal sealed class LocalBrowserSessions(LinkedInSession linkedin, ControlledTests tests) : ILocalBrowserSessions
{
    public async Task ClearAsync(CancellationToken ct)
    {
        await linkedin.DisconnectAsync(ct);
        await tests.ConfigureAsync(new(false, []));
    }
}
