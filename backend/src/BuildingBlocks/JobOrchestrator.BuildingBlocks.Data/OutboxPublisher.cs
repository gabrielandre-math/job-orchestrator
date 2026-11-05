using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Transactional Outbox (Richardson): publica uma mensagem por transação, com SKIP LOCKED e tentativas contadas.</summary>
public sealed class OutboxPublisher<TDb>(IServiceScopeFactory scopes, IMessageBus bus, TimeProvider time, ILogger<OutboxPublisher<TDb>> log) : BackgroundService
    where TDb : DbContext, IOutboxDbContext
{
    private const int BatchSize = 20;
    /// <summary>Com um só usuário de banco, o schema vai explícito (o search_path não aponta para o módulo).</summary>
    private static string NextMessageSql(string schema) => $"""
        SELECT * FROM {schema}.outbox
        WHERE published_at IS NULL AND attempts < 10 AND (next_attempt_at IS NULL OR next_attempt_at <= now())
        ORDER BY occurred_at LIMIT 1 FOR UPDATE SKIP LOCKED
        """;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                while (published < BatchSize && await PublishNextAsync(ct)) published++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Outbox failure in {Module}", typeof(TDb).Name);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
            if (published == 0) await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
    }

    private async Task<bool> PublishNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var message = (await db.Outbox.FromSqlRaw(NextMessageSql(db.Model.GetDefaultSchema()!)).ToListAsync(ct)).FirstOrDefault();
        if (message is null) return false;
        var now = time.GetUtcNow().UtcDateTime;
        try
        {
            await bus.PublishAsync(new Envelope(message.Id, message.EventType, message.Content), ct);
            message.MarkPublished(now);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message.RegisterFailure(ex.Message, now);
            log.LogWarning("Failed to publish {Type}: attempt {Attempt}", message.EventType, message.Attempts);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return message.PublishedAt is not null;
    }
}
