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

    /// <summary>An e-mail only moves the step forward: from none to any, from interview to offer or rejection. A step you marked past that stays.</summary>
    internal static bool Advances(string? current, string next) => current is null || current == "Entrevista" && next is "Oferta" or "Recusada";

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var e = message.Read<JobEmailRepliedV1>();
        if (await db.Applications.FirstOrDefaultAsync(a => a.JobId == e.JobId, ct) is { } app)
        {
            if (app.State is ApplicationState.SubmittedDeclared or ApplicationState.SubmittedConfirmed && Step(e.Class) is { } step && Advances(app.Outcome, step))
                app.SetOutcome(step, now, "Resposta da empresa por e-mail");
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

/// <summary>
/// A platform's notice about one of your applications (Gupy, Greenhouse, Lever, Indeed: received, next step, interview, closed), found by the
/// company and, when you have more than one there, by the role. It goes to the history. Any notice but "closed" proves the application arrived,
/// so one that was with you or unsure counts as confirmed; an interview, offer or rejection marks the step. Mail from before the application
/// existed is about something else and is left alone.
/// </summary>
internal sealed class HiringMailConsumer(ApplicationsDbContext db, TimeProvider time) : IEventConsumer
{
    public string EventType => HiringMailV1.EventType;

    private static readonly ApplicationState[] Open = [ApplicationState.ManualHandoff, ApplicationState.NeedsHuman, ApplicationState.SubmissionUnknown, ApplicationState.SubmittedDeclared, ApplicationState.SubmittedConfirmed];

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        var e = message.Read<HiringMailV1>();
        var apps = await db.Applications.Where(a => Open.Contains(a.State) && a.ArchivedAt == null && a.CreatedAt <= e.At).ToListAsync(ct);
        if (Match(apps.Select(a => (a.Id, a.Company, a.Title)), e) is { } id)
        {
            var app = apps.Single(a => a.Id == id);
            var proof = e.Class != "job_closed";
            if (proof && app.State is ApplicationState.ManualHandoff or ApplicationState.NeedsHuman or ApplicationState.SubmissionUnknown)
                await db.ResolveTasksAsync(app.Id, "Confirmada pela plataforma", now, ct);
            if (proof && app.State is ApplicationState.NeedsHuman)
                app.Move(ApplicationState.SubmittedDeclared, "A plataforma escreveu sobre esta candidatura: o envio foi feito pelo site.", "Correio", now);
            if (proof && app.State is ApplicationState.ManualHandoff or ApplicationState.SubmissionUnknown or ApplicationState.SubmittedDeclared)
                app.Confirmed(new SubmissionReceipt(e.MessageId, 3, $"A plataforma confirmou por e-mail: {e.Subject}"), "Correio", now);
            if (app.State is ApplicationState.SubmittedDeclared or ApplicationState.SubmittedConfirmed && JobEmailRepliedConsumer.Step(e.Class) is { } step && JobEmailRepliedConsumer.Advances(app.Outcome, step))
                app.SetOutcome(step, now, "Aviso da plataforma por e-mail");
            db.Evidence.Add(new Evidence
            {
                Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = proof ? 3 : 2, Kind = "platform_mail",
                Detail = $"E-mail de {e.Sender} em {e.At:dd/MM HH:mm} UTC: \"{e.Subject}\" ({e.Class}).", At = now
            });
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    /// <summary>
    /// The one application the e-mail is about: its company, as whole words, in what the model read, the subject, the sender or the text; with
    /// more than one application there, the role decides, and a tie is left alone rather than guessed. Platforms and hidden names never match.
    /// </summary>
    internal static Guid? Match(IEnumerable<(Guid Id, string Company, string Title)> apps, HiringMailV1 e)
    {
        var text = Fold($"{e.Company} {e.Subject} {e.Sender} {e.Snippet}");
        var model = e.Company is null ? "" : Key(e.Company);
        var hits = apps.Where(a => Key(a.Company) is { Length: > 4 } k && (text.Contains(k, StringComparison.Ordinal) || model.Length > 4 && model.Contains(k, StringComparison.Ordinal))).ToList();
        if (hits.Count <= 1) return hits.Count == 1 ? hits[0].Id : null;
        var scored = hits.Select(a => (a.Id, Score: Fold(a.Title).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2 && !Filler.Contains(w)).Distinct().Count(w => text.Contains($" {w} ", StringComparison.Ordinal))))
            .OrderByDescending(x => x.Score).ToList();
        return scored[0].Score > 0 && scored[0].Score > scored[1].Score ? scored[0].Id : null;
    }

    /// <summary>The company as whole words, padded with spaces: "Konsi Tecnologia Ltda." is " konsi ". Empty for a platform or a hidden name.</summary>
    internal static string Key(string company)
    {
        var words = Fold(company).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        while (words.Count > 1 && Suffix.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        var key = string.Join(' ', words);
        return key.Length < 3 || Hidden.Contains(key) || words.Any(Platforms.Contains) ? "" : $" {key} ";
    }

    private static string Fold(string s)
    {
        var b = new System.Text.StringBuilder(s.Length + 2).Append(' ');
        foreach (var c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                b.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return System.Text.RegularExpressions.Regex.Replace(b.Append(' ').ToString(), " {2,}", " ");
    }

    private static readonly HashSet<string> Suffix = ["ltda", "sa", "s", "a", "inc", "llc", "ltd", "gmbh", "corp", "corporation", "co", "me", "eireli", "plc", "ag", "bv",
        "tecnologia", "tecnologias", "technologies", "technology", "tech", "solucoes", "solutions", "servicos", "services", "group", "grupo", "brasil", "brazil", "do", "de"];
    private static readonly HashSet<string> Platforms = ["gupy", "indeed", "glassdoor", "linkedin", "greenhouse", "lever", "workable", "ashby", "catho", "infojobs", "solides", "kenoby",
        "recrutei", "pandape", "jobvite", "smartrecruiters", "workday", "teamtailor", "recruitee", "breezy", "factorial", "personio", "bamboohr", "icims"];
    private static readonly HashSet<string> Hidden = ["confidencial", "empresa confidencial", "sigilosa", "empresa sigilosa", "confidential", "confidential company", "empresa", "company"];
    private static readonly HashSet<string> Filler = ["para", "com", "and", "the", "for", "with", "das", "dos", "vaga", "job"];
}
