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
          count(*) filter (where state = 'SubmittedConfirmed')::int as confirmed,
          count(*) filter (where state = 'SubmissionUnknown')::int as unknown,
          count(*) filter (where state = 'NeedsHuman')::int as needs_human,
          count(*) filter (where state = 'BlockedByPolicy')::int as blocked,
          count(*) filter (where state in ('Failed','DeadLetter'))::int as failed,
          count(*) filter (where state = 'Withdrawn')::int as withdrawn
        from applications.applications
        """;

    public async Task<Result<OverviewView>> Handle(GetOverviewQuery q, CancellationToken ct)
    {
        var campaign = (await campaigns.ListAsync(ct)).FirstOrDefault();
        var counts = (await db.FirstAsync<StateCounts>(CountsSql, null, ct))!;
        var pending = await db.FirstAsync<int>("select count(*)::int from applications.human_tasks where resolved_at is null", null, ct);
        var today = await db.FirstAsync<int>("select count(*)::int from applications.applications where state = 'SubmittedConfirmed' and updated_at >= @Since", new { Since = time.GetUtcNow().UtcDateTime.Date }, ct);
        var activity = await db.ListAsync<ActivityRow>("""
            select e.at, e.application_id, a.company, a.title, e."from", e."to", e.reason, e.actor
            from applications.events e join applications.applications a on a.id = e.application_id
            order by e.id desc limit 15
            """, null, ct);
        return new OverviewView(
            campaign is null ? null : new CampaignCard(campaign.Id, campaign.Name, campaign.Mode.ToString(), campaign.MinFitScore, campaign.MaxSubmissionsPerDay, campaign.Sources.Count),
            await jobs.CountsAsync(ct), counts, pending, today, activity, await automation.ListAdaptersAsync(ct));
    }
}

/// <summary>Um nó do fluxo: agente, papel, situação e quantos itens passaram por ele.</summary>
internal sealed record FlowNode(string Id, string Label, string Role, string Status, int Count, string Detail, string? Phase);
internal sealed record FlowEdge(string From, string To, int Count, string Kind);
internal sealed record FlowView(IReadOnlyList<FlowNode> Nodes, IReadOnlyList<FlowEdge> Edges, DateTime At);

internal sealed record GetFlowQuery : IQuery<FlowView>;

/// <summary>O fluxo dos agentes para o canvas. A topologia é fixa: o Guardião sempre fica no caminho do envio.</summary>
internal sealed class GetFlowHandler(IReadDb<ApplicationsDbContext> db, IJobsApi jobs, TimeProvider time) : IQueryHandler<GetFlowQuery, FlowView>
{
    private sealed record Extra(int Materials, int Prepared, int Intents, int Tasks);

    public async Task<Result<FlowView>> Handle(GetFlowQuery q, CancellationToken ct)
    {
        var j = await jobs.CountsAsync(ct);
        var sources = await jobs.SourcesAsync(ct);
        var a = (await db.FirstAsync<StateCounts>(GetOverviewHandler.CountsSql, null, ct))!;
        var x = (await db.FirstAsync<Extra>("""
            select (select count(*) from applications.applications where resume_hash is not null)::int as materials,
                   (select count(*) from applications.applications where payload_hash is not null)::int as prepared,
                   (select count(*) from applications.submission_intents)::int as intents,
                   (select count(*) from applications.human_tasks where resolved_at is null)::int as tasks
            """, null, ct))!;
        var eligible = j.Total - j.Duplicates - j.Rejected - j.Expired;
        var guarded = a.AwaitingApproval + a.Sending + a.Confirmed + a.Unknown + a.Blocked;
        string S(int n) => n > 0 ? "active" : "idle";
        var nodes = new List<FlowNode>();
        var edges = new List<FlowEdge>();
        // Uma fonte por plataforma no canvas; o detalhe por board fica na tela de vagas.
        foreach (var g in sources.GroupBy(x => Platform(x.Source)).OrderBy(g => g.Key.Order))
        {
            var postings = g.Sum(x => x.Postings);
            var boards = g.Count();
            nodes.Add(new($"source-{g.Key.Id}", g.Key.Name, boards == 1 ? "1 fonte" : $"{boards} fontes", S(postings), postings, $"{g.Sum(x => x.Shortlisted)} na fila", null));
            edges.Add(new($"source-{g.Key.Id}", "scout", postings, "flow"));
        }
        nodes.AddRange(
        [
            new("scout", "Batedor", "Descoberta", S(j.Total), j.Total, "Busca nas fontes permitidas", null),
            new("normalizer", "Normalizador", "Identidade e duplicadas", S(j.Total), j.Total - j.Duplicates, $"{j.Duplicates} duplicadas", null),
            new("judge", "Juiz", "Filtros eliminatórios", S(eligible), eligible, $"{j.Rejected} cortadas, {j.Expired} encerradas", null),
            new("analyst", "Analista", "Nota explicada", S(j.Shortlisted), j.Shortlisted, "Acima do corte da campanha", null),
            new("writer", "Redator", "Currículo", S(x.Materials), x.Materials, "Só fatos do perfil", null),
            new("reviewer", "Revisor", "Checagem de fatos", "planned", 0, "Outra família de modelo confere", "Fase 3"),
            new("preparer", "Preparador", "Formulário", S(x.Prepared), x.Prepared, "Preenche sem enviar", null),
            new("guard", "Guardião", "Política de envio", a.AwaitingApproval > 0 ? "attention" : S(guarded), guarded, $"{a.AwaitingApproval} esperando você", null),
            new("sender", "Remetente", "Envio", S(x.Intents), x.Intents, "Um por vez, com token", null),
            new("verifier", "Verificador", "Confirmação", a.Unknown > 0 ? "attention" : S(a.Confirmed), a.Confirmed, $"{a.Unknown} incertas", null),
            new("tracker", "Rastreador", "E-mail e funil", "planned", 0, "Lê só e-mails de ATS", "Fase 4"),
            new("attention", "Atenção", "Fila humana", x.Tasks > 0 ? "attention" : "idle", x.Tasks, "CAPTCHA, login, perguntas", null)
        ]);
        edges.AddRange(
        [
            new("scout", "normalizer", j.Total, "flow"),
            new("normalizer", "judge", j.Total - j.Duplicates, "flow"),
            new("judge", "analyst", eligible, "flow"),
            new("analyst", "writer", j.Shortlisted, "flow"),
            new("writer", "reviewer", 0, "planned"),
            new("writer", "preparer", x.Materials, "flow"),
            new("preparer", "guard", x.Prepared, "flow"),
            new("guard", "sender", x.Intents, "flow"),
            new("sender", "verifier", x.Intents, "flow"),
            new("verifier", "tracker", 0, "planned"),
            new("preparer", "attention", a.NeedsHuman, "fallback"),
            new("guard", "attention", a.AwaitingApproval, "fallback"),
            new("verifier", "attention", a.Unknown, "fallback")
        ]);
        return new FlowView(nodes, edges, time.GetUtcNow().UtcDateTime);
    }

    /// <summary>Plataforma de uma fonte ("greenhouse:acme" vira Greenhouse), com a ordem de exibição.</summary>
    internal static (string Id, string Name, int Order) Platform(string source) => source.Split(':', 2)[0].ToLowerInvariant() switch
    {
        "greenhouse" => ("greenhouse", "Greenhouse", 0),
        "lever" => ("lever", "Lever", 1),
        "ashby" => ("ashby", "Ashby", 2),
        "smartrecruiters" => ("smartrecruiters", "SmartRecruiters", 3),
        "github" => ("github", "GitHub", 4),
        "hn" => ("hackernews", "Hacker News", 5),
        "remotive" => ("remotive", "Remotive", 6),
        "manual" => ("manual", "Salvas por você", 7),
        var other => (other, other, 9)
    };
}

/// <summary>Rotas HTTP (Carter) da visão geral e do fluxo.</summary>
internal sealed class InsightEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/overview", async (ISender sender, CancellationToken ct) => (await sender.Send(new GetOverviewQuery(), ct)).ToHttp(Results.Ok)).WithTags("Insights");
        app.MapGet("/api/flow", async (ISender sender, CancellationToken ct) => (await sender.Send(new GetFlowQuery(), ct)).ToHttp(Results.Ok)).WithTags("Insights");
    }
}
