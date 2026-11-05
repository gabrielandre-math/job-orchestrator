using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace JobOrchestrator.BuildingBlocks.Data;

/// <summary>Linha da outbox: o evento gravado na mesma transação do negócio, para publicar depois.</summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Content { get; private set; } = null!;
    public DateTime OccurredAt { get; private set; }
    public DateTime? PublishedAt { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create<T>(string eventType, T evt, DateTime now) => new()
    {
        Id = Guid.CreateVersion7(),
        EventType = eventType,
        Content = JsonSerializer.Serialize(evt, JsonSerializerOptions.Web),
        OccurredAt = now
    };

    public void MarkPublished(DateTime now) => PublishedAt = now;

    public void RegisterFailure(string error, DateTime now)
    {
        Attempts++;
        NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Attempts)));
        LastError = error.Length > 500 ? error[..500] : error;
    }
}

/// <summary>Contexto de banco que tem uma tabela de outbox.</summary>
public interface IOutboxDbContext
{
    DbSet<OutboxMessage> Outbox { get; }
}

/// <summary>Linha da inbox: anota a mensagem já processada, para não repetir o efeito.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public DateTime ProcessedAt { get; set; }
}

/// <summary>Mapeia as tabelas de outbox e inbox em cada módulo.</summary>
public static class MessagingExtensions
{
    public static ModelBuilder MapOutbox(this ModelBuilder b)
    {
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(120);
            e.Property(x => x.Content).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt).HasFilter("published_at IS NULL");
        });
        return b;
    }

    public static ModelBuilder MapInbox(this ModelBuilder b)
    {
        b.Entity<InboxMessage>(e => { e.ToTable("inbox"); e.HasKey(x => x.MessageId); });
        return b;
    }

    /// <summary>Roda na mesma transação do efeito. Devolve false se a mensagem já foi processada (Receptor Idempotente).</summary>
    public static async Task<bool> RegisterInInboxAsync(this DbContext db, Guid messageId, DateTime now, CancellationToken ct)
    {
        // O schema vem do modelo do EF (nunca de entrada externa); os valores vão como parâmetro.
        var sql = string.Concat("INSERT INTO ", db.Model.GetDefaultSchema(), ".inbox (message_id, processed_at) VALUES ({0}, {1}) ON CONFLICT DO NOTHING");
        return await db.Database.ExecuteSqlRawAsync(sql, [messageId, now], ct) == 1;
    }

    public static T Read<T>(this Envelope message) => JsonSerializer.Deserialize<T>(message.Content, JsonSerializerOptions.Web)!;
}
