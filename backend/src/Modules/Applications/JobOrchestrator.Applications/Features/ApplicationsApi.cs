namespace JobOrchestrator.Applications.Features;

/// <summary>The applications by where they are: waiting for your approval, for an answer of yours, for you to send by the site, and so on.</summary>
internal sealed class ApplicationsApi(ApplicationsDbContext db, ICampaignsApi campaigns, TimeProvider time) : IApplicationsApi
{
    private static readonly ApplicationState[] Sent = [ApplicationState.SubmittedConfirmed, ApplicationState.SubmittedDeclared];
    private static readonly ApplicationState[] Moving = [ApplicationState.Shortlisted, ApplicationState.MaterialsReady, ApplicationState.FormPreparing, ApplicationState.FormValidated,
        ApplicationState.RetryScheduled, ApplicationState.ReadyToSubmit, ApplicationState.Submitting, ApplicationState.SubmissionUnknown, ApplicationState.PausedProvider];

    public async Task<ApplicationCounts> CountsAsync(CancellationToken ct)
    {
        var by = await db.Applications.AsNoTracking().Where(a => a.ArchivedAt == null).GroupBy(a => a.State).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        int N(params ApplicationState[] states) => states.Sum(s => by.GetValueOrDefault(s));
        // "Today" is the campaign's day, in its time zone, as the daily limit counts it.
        var zone = (await campaigns.ListAsync(ct)).FirstOrDefault()?.TimeZone;
        var today = SubmissionGuard.StartOfDay(time.GetUtcNow().UtcDateTime, zone);
        // Sent today by the time of the send in the history, as the home page counts it: an outcome marked today on an old one is not a send.
        var sentToday = await db.Events.AsNoTracking().Where(e => (e.To == "SubmittedConfirmed" || e.To == "SubmittedDeclared") && e.At >= today
                && db.Applications.Any(a => a.Id == e.ApplicationId && a.ArchivedAt == null))
            .Select(e => e.ApplicationId).Distinct().CountAsync(ct);
        return new(N(ApplicationState.AwaitingApproval), N(ApplicationState.NeedsHuman), N(ApplicationState.ManualHandoff), N(Moving), N(Sent), sentToday,
            N(ApplicationState.Failed, ApplicationState.DeadLetter, ApplicationState.BlockedByPolicy), N(ApplicationState.Withdrawn));
    }
}
