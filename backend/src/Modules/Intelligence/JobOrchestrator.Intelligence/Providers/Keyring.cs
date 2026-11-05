namespace JobOrchestrator.Intelligence.Providers;

/// <summary>
/// Chaves de API no gnome-keyring (Secret Service) pelo secret-tool: nunca em arquivo, banco ou log.
/// A chave entra pelo stdin do secret-tool, então não aparece na lista de processos.
/// </summary>
internal static class Keyring
{
    private const string Service = "job-orchestrator";

    public static async Task<bool> StoreAsync(string account, string secret, CancellationToken ct)
    {
        var run = await CliRunner.RunAsync("secret-tool", ["store", "--label", $"Job Orchestrator: {account}", "service", Service, "account", account], secret, TimeSpan.FromSeconds(20), null, ct);
        return run.Exit == 0;
    }

    public static async Task<string?> GetAsync(string account, CancellationToken ct)
    {
        var run = await CliRunner.RunAsync("secret-tool", ["lookup", "service", Service, "account", account], null, TimeSpan.FromSeconds(10), null, ct);
        return run.Exit == 0 && run.Stdout.Trim().Length > 0 ? run.Stdout.Trim() : null;
    }

    public static async Task ClearAsync(string account, CancellationToken ct) =>
        await CliRunner.RunAsync("secret-tool", ["clear", "service", Service, "account", account], null, TimeSpan.FromSeconds(10), null, ct);
}
