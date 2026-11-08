using Dapper;
using Npgsql;

namespace JobOrchestrator.Applications.Features.Workflow;

/// <summary>Um item tomado da fila.</summary>
internal sealed record Claimed(Guid Id, Guid ApplicationId, string Step, int Attempts, int MaxAttempts)
{
    public WorkStep Kind => Enum.Parse<WorkStep>(Step);
}

/// <summary>Pedido de nova tentativa de uma etapa segura, com espera.</summary>
internal sealed class RetryLaterException(TimeSpan delay, string message) : Exception(message)
{
    public TimeSpan Delay => delay;
}

/// <summary>
/// Fila de trabalho em Postgres: FOR UPDATE SKIP LOCKED com lease de 2 minutos renovado enquanto a etapa roda.
/// Lease vencido volta para a fila; a etapa decide o que é seguro refazer olhando o estado gravado.
/// </summary>
internal sealed class WorkQueue(NpgsqlDataSource source)
{
    public static readonly string Owner = $"{Environment.MachineName}:{Environment.ProcessId}";

    private const string ClaimSql = """
        update applications.work_items w set lease_owner = @Owner, lease_expires_at = now() + interval '2 minutes', attempts = w.attempts + 1
        where w.id = (
          select id from applications.work_items
          where completed_at is null and run_after <= now() and (lease_expires_at is null or lease_expires_at < now())
          -- Later steps first: an application goes all the way to sending before the next resume starts, so a batch of 50 does not
          -- write 50 resumes before the first form is even opened.
          order by case step when 'Reconcile' then 0 when 'Submit' then 1 when 'Prepare' then 2 else 3 end, run_after limit 1 for update skip locked)
        returning w.id, w.application_id, w.step, w.attempts, w.max_attempts
        """;

    public async Task<Claimed?> ClaimAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await c.QueryFirstOrDefaultAsync<Claimed>(new CommandDefinition(ClaimSql, new { Owner }, cancellationToken: ct));
    }

    public async Task RenewAsync(Guid id, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("update applications.work_items set lease_expires_at = now() + interval '2 minutes' where id = @id and lease_owner = @Owner", new { id, Owner }, cancellationToken: ct));
    }

    public async Task RetryAsync(Guid id, TimeSpan delay, string error, CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        await c.ExecuteAsync(new CommandDefinition("""
            update applications.work_items set run_after = now() + make_interval(secs => @Seconds), lease_owner = null, lease_expires_at = null, last_error = left(@error, 500)
            where id = @id
            """, new { id, Seconds = delay.TotalSeconds, error }, cancellationToken: ct));
    }

    /// <summary>
    /// No boot: leases of processes of this machine that are gone are released at once, without waiting for the deadline. A lease of
    /// another live instance (the dev server next to the installed app) stays with it, so two workers never take the same item.
    /// </summary>
    public async Task<int> ReleaseStaleAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        var owners = await c.QueryAsync<string>(new CommandDefinition("""
            select distinct lease_owner from applications.work_items where completed_at is null and lease_owner like @Prefix and lease_owner <> @Owner
            """, new { Prefix = Environment.MachineName + ":%", Owner }, cancellationToken: ct));
        var dead = owners.Where(o => !Alive(o)).ToArray();
        if (dead.Length == 0) return 0;
        return await c.ExecuteAsync(new CommandDefinition("""
            update applications.work_items set lease_expires_at = now() - interval '1 second' where completed_at is null and lease_owner = any(@Dead)
            """, new { Dead = dead }, cancellationToken: ct));
    }

    /// <summary>"machine:pid" is alive while that process runs and is this app; a pid reused by another program counts as gone.</summary>
    internal static bool Alive(string owner)
    {
        if (!int.TryParse(owner[(owner.LastIndexOf(':') + 1)..], out var pid)) return false;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (process.HasExited) return false;
            using var self = System.Diagnostics.Process.GetCurrentProcess();
            return process.ProcessName == self.ProcessName || process.ProcessName.Contains("orchestrator", StringComparison.OrdinalIgnoreCase) || process.ProcessName == "dotnet";
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
    }
}

/// <summary>Assina a autorização de envio. Só o Guardião chama, depois de liberar.</summary>
internal sealed class TokenIssuer(GuardKey key, TimeProvider time)
{
    public SubmitToken Issue(SubmissionIntent intent)
    {
        var expires = time.GetUtcNow().UtcDateTime.AddMinutes(10);
        return new SubmitToken(intent.Id, intent.ApplicationId, intent.PayloadHash, expires,
            SubmitToken.Sign(key.Bytes, intent.Id, intent.ApplicationId, intent.PayloadHash, expires));
    }
}
