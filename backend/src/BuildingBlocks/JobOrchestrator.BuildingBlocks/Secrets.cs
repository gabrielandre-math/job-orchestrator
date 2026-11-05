using System.Diagnostics;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>
/// Segredos (senhas de app de e-mail, chaves) no gnome-keyring pelo secret-tool: nunca em arquivo, banco ou log.
/// O segredo entra pelo stdin, então não aparece na lista de processos.
/// </summary>
public static class Secrets
{
    private const string Service = "job-orchestrator";

    public static async Task<bool> StoreAsync(string account, string secret, CancellationToken ct) =>
        (await RunAsync(["store", "--label", $"Job Orchestrator: {account}", "service", Service, "account", account], secret, ct)).Exit == 0;

    public static async Task<string?> GetAsync(string account, CancellationToken ct)
    {
        var (exit, output) = await RunAsync(["lookup", "service", Service, "account", account], null, ct);
        return exit == 0 && output.Trim().Length > 0 ? output.Trim() : null;
    }

    public static Task ClearAsync(string account, CancellationToken ct) => RunAsync(["clear", "service", Service, "account", account], null, ct);

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, string? stdin, CancellationToken ct)
    {
        var info = new ProcessStartInfo("secret-tool") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info) ?? throw new InvalidOperationException("secret-tool não iniciou.");
        if (stdin is not null) await p.StandardInput.WriteAsync(stdin.AsMemory(), ct);
        p.StandardInput.Close();
        var output = await p.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await p.WaitForExitAsync(timeout.Token);
        return (p.ExitCode, output);
    }
}
