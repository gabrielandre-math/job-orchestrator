namespace JobOrchestrator.Applications.Features;

/// <summary>Uma candidatura na lista.</summary>
internal sealed record ApplicationRow(
    Guid Id, Guid JobId, string Company, string Title, string Location, string State, int Score, DateTime CreatedAt, DateTime UpdatedAt,
    string? PreparedBy, int EvidenceLevel, string? ReceiptId, string? LastReason, string ApplyChannel, string? Outcome, DateTime? OutcomeAt);

internal sealed record HistoryRow(DateTime At, string? From, string To, string Reason, string Actor, string? Detail);
internal sealed record EvidenceRow(int Level, string Kind, string Detail, DateTime At);
internal sealed record TaskRow(Guid Id, Guid ApplicationId, string Kind, string Title, string Detail, DateTime CreatedAt, DateTime? ResolvedAt, string? Resolution);

/// <summary>A candidatura aberta: dados, payload preparado, histórico, evidências e pendências.</summary>
internal sealed record ApplicationDetail(
    ApplicationRow Application, string ApplyUrl, string? PayloadHash, IReadOnlyList<FieldValue> Fields, bool HasResume,
    IReadOnlyList<HistoryRow> History, IReadOnlyList<EvidenceRow> Evidence, IReadOnlyList<TaskRow> Tasks);

internal static class Reads
{
    public const string Columns = "id, job_id, company, title, location, state, score, created_at, updated_at, prepared_by, evidence_level, receipt_id, last_reason, apply_channel, outcome, outcome_at";
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

/// <summary>
/// Comando: você enviou pelo site original (LinkedIn, Gupy, site da empresa) ou confirmou que um envio incerto chegou.
/// Vira "declarada por você", com evidência nível 2; nunca conta como confirmada pelo ATS.
/// </summary>
internal sealed record DeclareCommand(Guid Id, string? Note) : ICommand<Done>, IApplicationsCommand;

internal sealed class DeclareHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<DeclareCommand, Done>
{
    public async Task<Result<Done>> Handle(DeclareCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        if (app.State is not (ApplicationState.ManualHandoff or ApplicationState.NeedsHuman))
            return Error.Conflict("cannot-declare", "Só dá para registrar envio feito por você numa candidatura que está com você.");
        var now = time.GetUtcNow().UtcDateTime;
        await db.ResolveTasksAsync(app.Id, "Enviada por você", now, ct);
        app.Move(ApplicationState.SubmittedDeclared, "Você registrou o envio pelo site original.", "Você", now, new { note = c.Note });
        db.Evidence.Add(new Evidence { Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = 2, Kind = "declared_by_you", Detail = c.Note is { Length: > 0 } n ? n : "Envio feito por você, fora do app.", At = now });
        return new Done();
    }
}

/// <summary>O que o copiloto mostra para você candidatar pelo site original: o link, o currículo da vaga e as respostas aprovadas, prontas para copiar.</summary>
internal sealed record CopilotView(string ApplyUrl, string Channel, bool HasResume, IReadOnlyList<CopilotAnswer> Answers, string FirstName, string LastName, string Email, string Phone, string LinkedIn);
internal sealed record CopilotAnswer(string Key, string Value);

internal sealed record DeclareBody(string? Note);

/// <summary>Comando: a etapa depois do envio (Entrevista, Oferta, Aceita, Recusada; vazio volta para Enviada), arrastada no quadro.</summary>
internal sealed record SetOutcomeCommand(Guid Id, string? Outcome) : ICommand<Done>, IApplicationsCommand;
internal sealed record OutcomeBody(string? Outcome);

internal sealed class SetOutcomeHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<SetOutcomeCommand, Done>
{
    public async Task<Result<Done>> Handle(SetOutcomeCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        try { app.SetOutcome(string.IsNullOrWhiteSpace(c.Outcome) ? null : c.Outcome, time.GetUtcNow().UtcDateTime); }
        catch (DomainRuleException ex) { return Error.Conflict("cannot-move", ex.Message); }
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
        group.MapPut("{id:guid}/outcome", async (Guid id, OutcomeBody body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new SetOutcomeCommand(id, body.Outcome), ct)).ToHttp(_ => Results.NoContent()));
        group.MapPost("{id:guid}/retry", async (Guid id, ISender sender, CancellationToken ct) => (await sender.Send(new RetryCommand(id), ct)).ToHttp(_ => Results.NoContent()));
        group.MapGet("{id:guid}/resume", async (Guid id, IReadDb<ApplicationsDbContext> db, CancellationToken ct) =>
        {
            var path = await db.FirstAsync<string>("select resume_path from applications.applications where id = @id", new { id }, ct);
            return path is not null && File.Exists(path) ? Results.File(path, "application/pdf", enableRangeProcessing: true) : Results.NotFound();
        });
        group.MapPost("{id:guid}/declare", async (Guid id, DeclareBody? body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new DeclareCommand(id, body?.Note), ct)).ToHttp(_ => Results.NoContent()));
        group.MapGet("{id:guid}/copilot", async (Guid id, ApplicationsDbContext db, Features.Workflow.Bundles bundles, CancellationToken ct) =>
        {
            var a = await db.Applications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var b = await bundles.ForAsync(a, ct);
            return Results.Ok(new CopilotView(a.ApplyUrl, a.ApplyChannel, a.ResumePath is not null && File.Exists(a.ResumePath),
                b is null ? [] : [.. b.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => new CopilotAnswer(x.Key, x.Value))],
                b?.FirstName ?? "", b?.LastName ?? "", b?.Email ?? "", b?.Phone ?? "", b?.LinkedIn ?? ""));
        });
        app.MapGet("/api/tasks", async (ISender sender, CancellationToken ct) => (await sender.Send(new ListTasksQuery(), ct)).ToHttp(Results.Ok)).WithTags("Applications");
        // Extensão do copiloto: acha a candidatura da página aberta (mesmo site, caminho mais parecido) e devolve o que preencher.
        app.MapGet("/api/copilot/match", async (string url, ApplicationsDbContext db, Features.Workflow.Bundles bundles, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var page)) return Results.BadRequest();
            var host = page.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase);
            var candidates = await db.Applications.AsNoTracking().Where(a => a.ApplyUrl.Contains(host)).OrderByDescending(a => a.UpdatedAt).Take(60).ToListAsync(ct);
            var best = candidates.Select(a => (App: a, Score: CopilotMatch.Score(a.ApplyUrl, page))).Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.App).FirstOrDefault();
            if (best is null) return Results.NotFound();
            var b = await bundles.ForAsync(best, ct);
            return Results.Ok(new
            {
                applicationId = best.Id, company = best.Company, title = best.Title,
                copilot = new CopilotView(best.ApplyUrl, best.ApplyChannel, best.ResumePath is not null && File.Exists(best.ResumePath),
                    b is null ? [] : [.. b.Answers.Where(x => !string.IsNullOrWhiteSpace(x.Value)).Select(x => new CopilotAnswer(x.Key, x.Value))],
                    b?.FirstName ?? "", b?.LastName ?? "", b?.Email ?? "", b?.Phone ?? "", b?.LinkedIn ?? ""),
            });
        }).WithTags("Applications");
    }
}

/// <summary>Quão parecida a página aberta é do link de candidatura guardado: mesmo site e o maior pedaço de caminho em comum.</summary>
internal static class CopilotMatch
{
    public static int Score(string applyUrl, Uri page)
    {
        if (!Uri.TryCreate(applyUrl, UriKind.Absolute, out var saved)) return 0;
        static string Bare(string h) => h.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? h[4..] : h;
        if (!Bare(saved.Host).Equals(Bare(page.Host), StringComparison.OrdinalIgnoreCase)) return 0;
        var a = saved.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var b = page.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var common = a.Zip(b).TakeWhile(x => string.Equals(x.First, x.Second, StringComparison.OrdinalIgnoreCase)).Count();
        // O id da vaga em qualquer parte do caminho da página conta muito: /jobs/123 e /jobs/123/apply são a mesma vaga.
        var id = a.LastOrDefault(s => s.Any(char.IsDigit));
        return 1 + common * 10 + (id is not null && b.Contains(id, StringComparer.OrdinalIgnoreCase) ? 100 : 0);
    }
}

