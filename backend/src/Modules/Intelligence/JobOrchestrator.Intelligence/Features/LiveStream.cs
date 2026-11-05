using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Npgsql;

namespace JobOrchestrator.Intelligence.Features;

/// <summary>
/// Conversas ao vivo: uma conexão com LISTEN agent_messages; o gatilho da tabela de mensagens avisa o id de cada mensagem nova,
/// que é lida e entregue na hora. Sem polling; a conexão fecha quando a tela fecha.
/// </summary>
internal static class LiveStream
{
    public const string Channel = "agent_messages";

    public static async IAsyncEnumerable<AgentMessageLive> ReadAsync(string connection, IServiceProvider sp, [EnumeratorCancellation] CancellationToken ct)
    {
        var ids = System.Threading.Channels.Channel.CreateUnbounded<Guid>();
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync(ct);
        conn.Notification += (_, e) => { if (Guid.TryParse(e.Payload, out var id)) ids.Writer.TryWrite(id); };
        await using (var listen = new NpgsqlCommand($"LISTEN {Channel}", conn)) await listen.ExecuteNonQueryAsync(ct);
        var pump = Task.Run(async () =>
        {
            try { while (!ct.IsCancellationRequested) await conn.WaitAsync(ct); }
            catch (OperationCanceledException) { }
            finally { ids.Writer.TryComplete(); }
        }, CancellationToken.None);

        await foreach (var id in ids.Reader.ReadAllAsync(ct))
        {
            await using var scope = sp.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IntelligenceDbContext>();
            var m = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (m is null) continue;
            yield return new AgentMessageLive(m.ThreadId, m.ThreadKind, m.Subject,
                new AgentMessageView(m.Id, m.At, m.Agent, m.Kind, m.ProviderId, m.Model, m.Effort, m.Title, m.Body, m.Seconds, m.Ok, m.To));
        }
        await pump;
    }

    /// <summary>Gatilho que avisa cada mensagem nova. Idempotente e protegido por lock para API e Worker subirem juntos.</summary>
    public const string TriggerSql = """
        select pg_advisory_xact_lock(4747002);
        create or replace function intelligence.notify_message() returns trigger language plpgsql as $$
        begin perform pg_notify('agent_messages', NEW.id::text); return NEW; end $$;
        create or replace trigger messages_notify after insert on intelligence.messages for each row execute function intelligence.notify_message();
        """;
}

/// <summary>Uma mensagem ao vivo, com o fio a que pertence.</summary>
internal sealed record AgentMessageLive(Guid ThreadId, string Kind, string Subject, AgentMessageView Message);
