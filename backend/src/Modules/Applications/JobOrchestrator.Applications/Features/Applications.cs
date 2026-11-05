namespace JobOrchestrator.Applications.Features;

/// <summary>Uma candidatura na lista.</summary>
internal sealed record ApplicationRow(
    Guid Id, Guid JobId, string Company, string Title, string Location, string State, int Score, DateTime CreatedAt, DateTime UpdatedAt,
    string? PreparedBy, int EvidenceLevel, string? ReceiptId, string? LastReason, string ApplyChannel);

internal sealed record HistoryRow(DateTime At, string? From, string To, string Reason, string Actor, string? Detail);
internal sealed record EvidenceRow(int Level, string Kind, string Detail, DateTime At);
internal sealed record TaskRow(Guid Id, Guid ApplicationId, string Kind, string Title, string Detail, DateTime CreatedAt, DateTime? ResolvedAt, string? Resolution);

/// <summary>A candidatura aberta: dados, payload preparado, histórico, evidências e pendências.</summary>
internal sealed record ApplicationDetail(
    ApplicationRow Application, string ApplyUrl, string? PayloadHash, IReadOnlyList<FieldValue> Fields, bool HasResume,
    IReadOnlyList<HistoryRow> History, IReadOnlyList<EvidenceRow> Evidence, IReadOnlyList<TaskRow> Tasks);

internal static class Reads
{
    public const string Columns = "id, job_id, company, title, location, state, score, created_at, updated_at, prepared_by, evidence_level, receipt_id, last_reason, apply_channel";
}

/// <summary>Consulta (CQRS): candidaturas, com filtro por estado.</summary>
internal sealed record ListApplicationsQuery(string? State) : IQuery<IReadOnlyList<ApplicationRow>>;

internal sealed class ListApplicationsHandler(IReadDb<ApplicationsDbContext> db) : IQueryHandler<ListApplicationsQuery, IReadOnlyList<ApplicationRow>>
{
    public async Task<Result<IReadOnlyList<ApplicationRow>>> Handle(ListApplicationsQuery q, CancellationToken ct) =>
        Result<IReadOnlyList<ApplicationRow>>.Ok(await db.ListAsync<ApplicationRow>(
            $"select {Reads.Columns} from applications.applications where (@State is null or state = @State) order by updated_at desc limit 500", new { q.State }, ct));
}

internal sealed record GetApplicationQuery(Guid Id) : IQuery<ApplicationDetail>;

internal sealed class GetApplicationHandler(IReadDb<ApplicationsDbContext> db) : IQueryHandler<GetApplicationQuery, ApplicationDetail>
{
    private sealed record Extra(string ApplyUrl, string? PayloadHash, string PreparedFields, string? ResumePath);

    public async Task<Result<ApplicationDetail>> Handle(GetApplicationQuery q, CancellationToken ct)
    {
        var row = await db.FirstAsync<ApplicationRow>($"select {Reads.Columns} from applications.applications where id = @Id", new { q.Id }, ct);
        if (row is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        var extra = (await db.FirstAsync<Extra>("select apply_url, payload_hash, prepared_fields::text as prepared_fields, resume_path from applications.applications where id = @Id", new { q.Id }, ct))!;
        var history = await db.ListAsync<HistoryRow>("select at, \"from\", \"to\", reason, actor, detail::text as detail from applications.events where application_id = @Id order by id", new { q.Id }, ct);
        var evidence = await db.ListAsync<EvidenceRow>("select level, kind, detail, at from applications.evidence where application_id = @Id order by at", new { q.Id }, ct);
        var tasks = await db.ListAsync<TaskRow>("select id, application_id, kind, title, detail, created_at, resolved_at, resolution from applications.human_tasks where application_id = @Id order by created_at", new { q.Id }, ct);
        var fields = JsonSerializer.Deserialize<List<FieldValue>>(extra.PreparedFields, JsonSerializerOptions.Web) ?? [];
        return new ApplicationDetail(row, extra.ApplyUrl, extra.PayloadHash, fields, extra.ResumePath is not null && File.Exists(extra.ResumePath), history, evidence, tasks);
    }
}

/// <summary>Consulta (CQRS): pendências suas, mais antigas primeiro.</summary>
internal sealed record ListTasksQuery : IQuery<IReadOnlyList<TaskRow>>;

internal sealed class ListTasksHandler(IReadDb<ApplicationsDbContext> db) : IQueryHandler<ListTasksQuery, IReadOnlyList<TaskRow>>
{
    public async Task<Result<IReadOnlyList<TaskRow>>> Handle(ListTasksQuery q, CancellationToken ct) =>
        Result<IReadOnlyList<TaskRow>>.Ok(await db.ListAsync<TaskRow>(
            "select id, application_id, kind, title, detail, created_at, resolved_at, resolution from applications.human_tasks where resolved_at is null order by created_at", null, ct));
}

/// <summary>Comando: aprova o payload exato que você viu. Outro payload não passa.</summary>
internal sealed record ApproveCommand(Guid Id, string PayloadHash) : ICommand<Done>, IApplicationsCommand;

internal sealed class ApproveValidator : AbstractValidator<ApproveCommand>
{
    public ApproveValidator() => RuleFor(x => x.PayloadHash).Length(64).WithMessage("Hash do formulário inválido.");
}

internal sealed class ApproveHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<ApproveCommand, Done>
{
    public async Task<Result<Done>> Handle(ApproveCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        if (app.State != ApplicationState.AwaitingApproval) return Error.Conflict("not-awaiting", "Esta candidatura não está esperando aprovação.");
        if (app.PayloadHash != c.PayloadHash) return Error.Conflict("payload-changed", "O formulário mudou desde que você abriu. Revise de novo.");
        var now = time.GetUtcNow().UtcDateTime;
        db.Approvals.Add(new ApprovalDecision { Id = Guid.CreateVersion7(), ApplicationId = app.Id, PayloadHash = c.PayloadHash, Approved = true, At = now });
        await db.ResolveTasksAsync(app.Id, "Aprovada", now, ct);
        app.Approve(c.PayloadHash, now);
        db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Submit, now, now));
        return new Done();
    }
}

/// <summary>Comando: recusa. A candidatura sai da fila e fica registrada.</summary>
internal sealed record WithdrawCommand(Guid Id) : ICommand<Done>, IApplicationsCommand;

internal sealed class WithdrawHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<WithdrawCommand, Done>
{
    public async Task<Result<Done>> Handle(WithdrawCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        if (!Transitions.Allowed(app.State, ApplicationState.Withdrawn)) return Error.Conflict("cannot-withdraw", "Esta candidatura não pode mais ser retirada.");
        var now = time.GetUtcNow().UtcDateTime;
        if (app.PayloadHash is not null && app.State == ApplicationState.AwaitingApproval)
            db.Approvals.Add(new ApprovalDecision { Id = Guid.CreateVersion7(), ApplicationId = app.Id, PayloadHash = app.PayloadHash, Approved = false, At = now });
        await db.ResolveTasksAsync(app.Id, "Recusada", now, ct);
        app.Move(ApplicationState.Withdrawn, "Recusada por você.", "Você", now);
        return new Done();
    }
}

/// <summary>Comando: depois de resolver a pendência, prepara de novo. Nunca reenvia: volta ao preparo.</summary>
internal sealed record RetryCommand(Guid Id) : ICommand<Done>, IApplicationsCommand;

internal sealed class RetryHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<RetryCommand, Done>
{
    public async Task<Result<Done>> Handle(RetryCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        if (app.State != ApplicationState.NeedsHuman || app.ResumePath is null) return Error.Conflict("cannot-retry", "Só dá para refazer o preparo de uma candidatura parada com você.");
        var now = time.GetUtcNow().UtcDateTime;
        await db.ResolveTasksAsync(app.Id, "Refeito o preparo", now, ct);
        db.WorkItems.Add(WorkItem.Create(app.Id, WorkStep.Prepare, now, now));
        return new Done();
    }
}

/// <summary>Rotas HTTP (Carter) das candidaturas e da revisão.</summary>
internal sealed class ApplicationEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/applications").WithTags("Applications");
        group.MapGet("", async (string? state, ISender sender, CancellationToken ct) => (await sender.Send(new ListApplicationsQuery(state), ct)).ToHttp(Results.Ok));
        group.MapGet("{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => (await sender.Send(new GetApplicationQuery(id), ct)).ToHttp(Results.Ok));
        group.MapPost("{id:guid}/approve", async (Guid id, ApproveCommand body, ISender sender, CancellationToken ct) => (await sender.Send(body with { Id = id }, ct)).ToHttp(_ => Results.NoContent()));
        group.MapPost("{id:guid}/withdraw", async (Guid id, ISender sender, CancellationToken ct) => (await sender.Send(new WithdrawCommand(id), ct)).ToHttp(_ => Results.NoContent()));
        group.MapPost("{id:guid}/retry", async (Guid id, ISender sender, CancellationToken ct) => (await sender.Send(new RetryCommand(id), ct)).ToHttp(_ => Results.NoContent()));
        group.MapGet("{id:guid}/resume", async (Guid id, IReadDb<ApplicationsDbContext> db, CancellationToken ct) =>
        {
            var path = await db.FirstAsync<string>("select resume_path from applications.applications where id = @id", new { id }, ct);
            return path is not null && File.Exists(path) ? Results.File(path, "application/pdf", enableRangeProcessing: true) : Results.NotFound();
        });
        app.MapGet("/api/tasks", async (ISender sender, CancellationToken ct) => (await sender.Send(new ListTasksQuery(), ct)).ToHttp(Results.Ok)).WithTags("Applications");
    }
}
