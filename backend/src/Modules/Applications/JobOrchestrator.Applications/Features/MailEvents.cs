namespace JobOrchestrator.Applications.Features;

/// <summary>
/// The application e-mail of a job left your account: the e-mail application stops waiting with you and is sent, with the server's
/// acceptance as evidence. A follow-up only adds evidence. SMTP acceptance is not delivery, and the evidence says so.
/// </summary>
internal sealed class JobEmailSentConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => JobEmailSentV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var e = message.Read<JobEmailSentV1>();
        if (await db.Applications.FirstOrDefaultAsync(a => a.JobId == e.JobId && a.ApplyChannel == "email", ct) is { } app)
        {
            if (e.Kind == "first" && app.State is ApplicationState.ManualHandoff or ApplicationState.NeedsHuman)
            {
                await db.ResolveTasksAsync(app.Id, "Enviada por e-mail", now, ct);
                app.Move(ApplicationState.SubmittedDeclared, $"E-mail de candidatura enviado pela sua conta para {e.To}; o servidor aceitou.", "Correio", now, new { e.MessageId });
            }
            db.Evidence.Add(new Evidence
            {
                Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = 1, Kind = e.Kind == "first" ? "smtp_accepted" : "smtp_followup",
                Detail = $"{(e.Kind == "first" ? "E-mail" : "Lembrete")} aceito pelo servidor em {e.SentAt:dd/MM HH:mm} UTC (Message-ID {e.MessageId}). Não prova entrega nem leitura.", At = now
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}

/// <summary>The company answered the job's e-mail: the answer goes to the history, and an interview, offer or rejection marks the step you had not marked.</summary>
internal sealed class JobEmailRepliedConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => JobEmailRepliedV1.EventType;

    internal static string? Step(string? replyClass) => replyClass switch
    {
        "interview_invite" => "Entrevista",
        "offer" => "Oferta",
        "rejection" => "Recusada",
        _ => null
    };

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var e = message.Read<JobEmailRepliedV1>();
        if (await db.Applications.FirstOrDefaultAsync(a => a.JobId == e.JobId, ct) is { } app)
        {
            if (app.State is ApplicationState.SubmittedDeclared or ApplicationState.SubmittedConfirmed && app.Outcome is null && Step(e.Class) is { } step)
                app.SetOutcome(step, now);
            db.Evidence.Add(new Evidence
            {
                Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = 2, Kind = "email_reply",
                Detail = $"A empresa respondeu por e-mail em {e.At:dd/MM HH:mm} UTC{(e.Class is { } c ? $" ({c})" : "")}.", At = now
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
