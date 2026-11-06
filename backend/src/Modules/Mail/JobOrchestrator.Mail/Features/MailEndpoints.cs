namespace JobOrchestrator.Mail.Features;

internal sealed record AccountView(Guid Id, string Provider, string Address, string DisplayName, string SmtpHost, int SmtpPort, string ImapHost, DateTime? CheckedAt, string? LastError,
    bool Sends, DateTime? PausedUntil, string? PauseReason, int SentToday);
internal sealed record SendsInput(bool Sends);
internal sealed record TargetView(Guid JobId, string Company, string Title, string Location, string ApplyEmail, string ApplyUrl, string? CompanyDomain, int Score, string? MailStatus, Guid? MailId);
internal sealed record OverviewView(int SentToday, int DailyCap, int Awaiting, int Scheduled, int Sent, int Replied, int Interviews, string Mode, bool Consent, bool Connected);

/// <summary>Rotas do e-mail: contas, configuração (com o consentimento do automático), modelos, vagas com e-mail publicado, rascunhos, envio e respostas.</summary>
internal sealed class MailEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/mail").WithTags("Mail");
        g.MapGet("presets", () => Results.Ok(Presets.All));
        g.MapGet("consent", () => Results.Ok(new { text = MailService.ConsentText }));
        g.MapGet("alerts", async (MailDbContext db, CancellationToken ct) =>
        {
            var since = DateTime.UtcNow.AddDays(-14);
            var recent = await db.AlertPostings.AsNoTracking().Where(x => x.ReceivedAt >= since).OrderByDescending(x => x.ReceivedAt).Take(12)
                .Select(x => new { x.Sender, x.Title, x.Company, x.Location, x.Url, x.ReceivedAt }).ToListAsync(ct);
            return Results.Ok(new { count = await db.AlertPostings.CountAsync(x => x.ReceivedAt >= since, ct), connected = await db.Accounts.AnyAsync(a => a.ImapHost != "", ct), recent });
        });
        g.MapGet("overview", async (MailDbContext db, MailService mail, CancellationToken ct) =>
        {
            var s = await mail.SettingsAsync(ct);
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
            var start = TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date, zone);
            var all = await db.Outgoing.AsNoTracking().Select(o => new { o.Status, o.SentAt, o.ReplyClass }).ToListAsync(ct);
            var senders = await db.Accounts.CountAsync(a => a.Sends, ct);
            return Results.Ok(new OverviewView(all.Count(o => o.SentAt >= start), s.DailyCap * Math.Max(1, senders), all.Count(o => o.Status == "AwaitingApproval"), all.Count(o => o.Status == "Scheduled"),
                all.Count(o => o.Status is "Sent" or "Replied"), all.Count(o => o.Status == "Replied"), all.Count(o => o.ReplyClass is "interview_invite" or "assessment" or "offer"),
                s.Mode, s.AutoConsentAt is not null, await db.Accounts.AnyAsync(ct)));
        });

        g.MapGet("accounts", async (MailDbContext db, MailService mail, CancellationToken ct) =>
        {
            var today = await mail.SentTodayAsync(ct);
            var paused = DateTime.UtcNow;
            return Results.Ok((await db.Accounts.AsNoTracking().OrderBy(a => a.CreatedAt).ToListAsync(ct)).Select(a => new AccountView(a.Id, a.Provider, a.Address, a.DisplayName,
                a.SmtpHost, a.SmtpPort, a.ImapHost, a.CheckedAt, a.LastError, a.Sends, a.PausedUntil > paused ? a.PausedUntil : null, a.PausedUntil > paused ? a.PauseReason : null, today.GetValueOrDefault(a.Id))));
        });
        g.MapPut("accounts/{id:guid}", (Guid id, SendsInput body, MailService mail, CancellationToken ct) => Run(async () => { await mail.SetSendsAsync(id, body.Sends, ct); return new { ok = true }; }));
        g.MapPost("accounts", (AccountInput body, MailService mail, CancellationToken ct) => Run(async () => { var a = await mail.ConnectAsync(body, ct); return new { a.Id, a.Address }; }));
        g.MapDelete("accounts/{id:guid}", async (Guid id, MailService mail, CancellationToken ct) => { await mail.RemoveAsync(id, ct); return Results.NoContent(); });
        g.MapPost("accounts/{id:guid}/test", (Guid id, MailService mail, CancellationToken ct) => Run(async () => { await mail.SendTestAsync(id, ct); return new { ok = true }; }));

        g.MapGet("settings", async (MailService mail, CancellationToken ct) => Results.Ok(await mail.SettingsAsync(ct)));
        g.MapPut("settings", (SettingsInput body, MailService mail, CancellationToken ct) => Run(() => mail.SaveSettingsAsync(body, ct)));
        g.MapGet("templates", async (MailService mail, CancellationToken ct) => Results.Ok(await mail.TemplatesAsync(ct)));
        g.MapPut("templates/{id:guid}", (Guid id, TemplateInput body, MailService mail, CancellationToken ct) => Run(() => mail.SaveTemplateAsync(id, body, ct)));

        g.MapGet("targets", async (IJobsApi jobs, MailDbContext db, CancellationToken ct) =>
        {
            var mails = await db.Outgoing.AsNoTracking().Where(o => o.Kind == "first" && o.Status != "Cancelled").ToListAsync(ct);
            return Results.Ok((await jobs.EmailTargetsAsync(ct)).Select(t =>
            {
                var m = mails.Where(x => x.JobId == t.Id).OrderByDescending(x => x.CreatedAt).FirstOrDefault();
                return new TargetView(t.Id, t.Company, t.Title, t.Location, t.ApplyEmail, t.ApplyUrl, t.CompanyDomain, t.Score, m?.Status, m?.Id);
            }));
        });
        g.MapGet("outgoing", async (MailDbContext db, CancellationToken ct) => Results.Ok(await db.Outgoing.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync(ct)));
        g.MapPost("drafts", (DraftInput body, MailService mail, CancellationToken ct) => Run(() => mail.DraftAsync(body.JobId, body.Kind ?? "first", ct)));
        g.MapPut("outgoing/{id:guid}", (Guid id, EditInput body, MailService mail, CancellationToken ct) => Run(() => mail.EditAsync(id, body, ct)));
        g.MapPost("outgoing/{id:guid}/approve", (Guid id, MailService mail, CancellationToken ct) => Run(() => mail.ApproveAsync(id, ct)));
        g.MapPost("outgoing/{id:guid}/cancel", (Guid id, MailService mail, CancellationToken ct) => Run(async () => { await mail.CancelAsync(id, ct); return new { ok = true }; }));
        g.MapGet("inbox", async (MailDbContext db, CancellationToken ct) => Results.Ok(await db.Incoming.AsNoTracking().OrderByDescending(i => i.Date).Take(200).ToListAsync(ct)));
        g.MapPost("inbox/check", (MailService mail, CancellationToken ct) => Run(async () => new { stored = await mail.PollAsync(ct) }));
    }

    private static async Task<IResult> Run<T>(Func<Task<T>> work)
    {
        try { return Results.Ok(await work()); }
        catch (MailRuleException ex) { return Results.Problem(ex.Message, statusCode: 400, title: "mail"); }
    }
}
