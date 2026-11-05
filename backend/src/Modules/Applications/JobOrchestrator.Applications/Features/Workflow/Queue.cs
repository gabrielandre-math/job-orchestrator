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
          order by run_after limit 1 for update skip locked)
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

    /// <summary>No boot: leases de processos anteriores desta máquina estão mortos; liberar já, sem esperar o prazo.</summary>
    public async Task<int> ReleaseStaleAsync(CancellationToken ct)
    {
        await using var c = await source.OpenConnectionAsync(ct);
        return await c.ExecuteAsync(new CommandDefinition("""
            update applications.work_items set lease_expires_at = now() - interval '1 second'
            where completed_at is null and lease_owner like @Prefix and lease_owner <> @Owner
            """, new { Prefix = Environment.MachineName + ":%", Owner }, cancellationToken: ct));
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
