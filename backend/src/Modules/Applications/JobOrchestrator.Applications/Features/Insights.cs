namespace JobOrchestrator.Applications.Features;

internal sealed record StateCounts(int Total, int InProgress, int Prepared, int AwaitingApproval, int Sending, int Confirmed, int Unknown, int NeedsHuman, int Blocked, int Failed, int Withdrawn);
internal sealed record ActivityRow(DateTime At, Guid ApplicationId, string Company, string Title, string? From, string To, string Reason, string Actor);
internal sealed record CampaignCard(Guid Id, string Name, string Mode, int MinFitScore, int MaxSubmissionsPerDay, int Sources);

/// <summary>A visão geral: só contagens provadas pelo banco, nunca estimadas.</summary>
internal sealed record OverviewView(CampaignCard? Campaign, JobCounts Jobs, StateCounts Applications, int PendingTasks, int ConfirmedToday, IReadOnlyList<ActivityRow> Activity, IReadOnlyList<AdapterView> Adapters);

internal sealed record GetOverviewQuery : IQuery<OverviewView>;

internal sealed class GetOverviewHandler(IReadDb<ApplicationsDbContext> db, ICampaignsApi campaigns, IJobsApi jobs, IAutomationApi automation, TimeProvider time)
    : IQueryHandler<GetOverviewQuery, OverviewView>
{
    internal const string CountsSql = """
        select count(*)::int as total,
          count(*) filter (where state in ('Shortlisted','MaterialsReady','FormPreparing','RetryScheduled','PausedProvider'))::int as in_progress,
          count(*) filter (where state = 'FormValidated')::int as prepared,
          count(*) filter (where state = 'AwaitingApproval')::int as awaiting_approval,
          count(*) filter (where state in ('ReadyToSubmit','Submitting'))::int as sending,
          count(*) filter (where state in ('SubmittedConfirmed','SubmittedDeclared'))::int as confirmed,
          count(*) filter (where state = 'SubmissionUnknown')::int as unknown,
          count(*) filter (where state = 'NeedsHuman')::int as needs_human,
          count(*) filter (where state = 'BlockedByPolicy')::int as blocked,
          count(*) filter (where state in ('Failed','DeadLetter'))::int as failed,
          count(*) filter (where state = 'Withdrawn')::int as withdrawn
        from applications.applications where archived_at is null
        """;

    public async Task<Result<OverviewView>> Handle(GetOverviewQuery q, CancellationToken ct)
    {
        var campaign = (await campaigns.ListAsync(ct)).FirstOrDefault();
        var counts = (await db.FirstAsync<StateCounts>(CountsSql, null, ct))!;
        var pending = await db.FirstAsync<int>("select count(*)::int from applications.human_tasks where resolved_at is null", null, ct);
        // Enviadas hoje pela hora do envio no histórico (confirmada pelo site ou registrada por você): marcar entrevista numa antiga não conta.
        var today = await db.FirstAsync<int>("""
            select count(distinct e.application_id)::int from applications.events e join applications.applications a on a.id = e.application_id
            where e."to" in ('SubmittedConfirmed', 'SubmittedDeclared') and e.at >= @Since and a.archived_at is null
            """, new { Since = Day.Start(time.GetUtcNow().UtcDateTime, campaign?.TimeZone) }, ct);
        var activity = await db.ListAsync<ActivityRow>("""
            select e.at, e.application_id, a.company, a.title, e."from", e."to", e.reason, e.actor
            from applications.events e join applications.applications a on a.id = e.application_id
            where a.archived_at is null
            order by e.id desc limit 15
            """, null, ct);
        return new OverviewView(
            campaign is null ? null : new CampaignCard(campaign.Id, campaign.Name, campaign.Mode.ToString(), campaign.MinFitScore, campaign.MaxSubmissionsPerDay, campaign.Sources.Count),
            await jobs.CountsAsync(ct), counts, pending, today, activity, await automation.ListAdaptersAsync(ct));
    }
}

/// <summary>Rotas HTTP (Carter) da visão geral.</summary>
internal sealed class InsightEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/overview", async (ISender sender, CancellationToken ct) => (await sender.Send(new GetOverviewQuery(), ct)).ToHttp(Results.Ok)).WithTags("Insights");
    }
}
