using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Applications.Features;

/// <summary>
/// The profile was confirmed: every application that stopped waiting for it ("Confirmar o perfil", "Enviar currículo") goes back to
/// writing its resume on its own, one every 20 seconds. Idempotent by the inbox.
/// </summary>
internal sealed class ProfileConfirmedConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    private static readonly string[] Waiting = ["Confirmar o perfil", "Enviar currículo"];

    public string EventType => ProfileConfirmedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var apps = await db.Applications.Where(a => a.State == ApplicationState.NeedsHuman && a.ArchivedAt == null && a.ResumePath == null
            && db.Tasks.Any(t => t.ApplicationId == a.Id && t.ResolvedAt == null && Waiting.Contains(t.Title))).OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var i = 0;
        foreach (var app in apps)
        {
            await db.ResolveTasksAsync(app.Id, "Perfil confirmado", now, ct);
            app.Move(ApplicationState.RetryScheduled, "O perfil foi confirmado: o currículo desta vaga é escrito agora.", "Supervisor", now);
            db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Materials, now.AddSeconds(20 * i++), now));
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
