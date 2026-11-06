namespace JobOrchestrator.Jobs.Features;

internal sealed record HistoryImportResult(int Received, int Added, int Updated, int WithSource, int Open);

/// <summary>
/// Comando: um lote do importador histórico (`jo history import`, que lê o Parquet ou o CSV em streaming). Tudo entra como UNVERIFIED,
/// mesmo o que o export diz que estava aberto; a mesma vaga importada de novo atualiza os dados e mantém o estado já conferido.
/// </summary>
internal sealed record ImportHistoryCommand(string Snapshot, IReadOnlyList<HistoryRowIn> Rows) : ICommand<HistoryImportResult>, IJobsCommand;

internal sealed class ImportHistoryValidator : AbstractValidator<ImportHistoryCommand>
{
    public ImportHistoryValidator()
    {
        RuleFor(x => x.Snapshot).NotEmpty().MaximumLength(60).WithMessage("Diga de qual snapshot vem o lote (até 60 caracteres).");
        RuleFor(x => x.Rows).NotEmpty().Must(r => r.Count <= 5000).WithMessage("Mande de 1 a 5.000 linhas por lote.");
        RuleForEach(x => x.Rows).Must(r => !string.IsNullOrEmpty(r.Ats) && !string.IsNullOrEmpty(r.Board) && !string.IsNullOrEmpty(r.Id) && r.Title is not null).WithMessage("Cada linha precisa de ats, board e id.");
    }
}

internal sealed class ImportHistoryHandler(JobsDbContext db, TimeProvider time) : ICommandHandler<ImportHistoryCommand, HistoryImportResult>
{
    public async Task<Result<HistoryImportResult>> Handle(ImportHistoryCommand c, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var rows = c.Rows.DistinctBy(r => (r.Ats, r.Board, r.Id)).ToList();
        var ids = rows.Select(r => r.Id).Distinct().ToList();
        var known = (await db.History.Where(h => h.Snapshot == c.Snapshot && ids.Contains(h.ExternalId)).ToListAsync(ct))
            .ToDictionary(h => (h.Ats, h.Board, h.ExternalId));
        var added = 0;
        var batch = new List<HistoricalPosting>();
        foreach (var r in rows)
        {
            if (known.TryGetValue((r.Ats, r.Board, r.Id), out var h)) h.Refresh(r);
            else { h = HistoricalPosting.Import(c.Snapshot, r, now); db.History.Add(h); added++; }
            batch.Add(h);
        }
        return new HistoryImportResult(c.Rows.Count, added, rows.Count - added, batch.Count(h => h.Source is not null), batch.Count(h => h.Status == HistoricalPosting.Open));
    }
}

/// <summary>
/// Comando: confere o histórico com as fontes ao vivo, um quadro por vez (os nunca conferidos primeiro, depois os de vaga que quem mora
/// no Brasil pode disputar). A coleta passa pelo portão de permissão do registro: fonte bloqueada ou desligada não é chamada e a vaga
/// fica como estava, com o motivo. Sem transação longa: cada quadro salva quando termina.
/// </summary>
internal sealed record RevalidateHistoryCommand(int Boards) : ICommand<HistoryCheck>;

internal sealed class RevalidateHistoryHandler(JobsDbContext db, IAutomationApi automation, TimeProvider time, ILogger<RevalidateHistoryHandler> log)
    : ICommandHandler<RevalidateHistoryCommand, HistoryCheck>
{
    private static readonly string[] Brazil = ["BR_ELIGIBLE_CONFIRMED", "BR_ELIGIBLE_POSSIBLE", "RELOCATION_WITH_SPONSORSHIP"];

    public async Task<Result<HistoryCheck>> Handle(RevalidateHistoryCommand c, CancellationToken ct)
    {
        var due = await db.History.Where(h => h.Source != null && h.Status != HistoricalPosting.Closed)
            .GroupBy(h => h.Source!)
            .Select(g => new { Source = g.Key, Last = g.Max(h => h.CheckedAt), Fit = g.Count(h => Brazil.Contains(h.Eligibility!)) })
            .OrderBy(g => g.Last != null).ThenByDescending(g => g.Fit).ThenBy(g => g.Last)
            .Take(Math.Clamp(c.Boards, 1, 50)).ToListAsync(ct);
        int checkedRows = 0, open = 0, closed = 0, failed = 0;
        foreach (var board in due)
        {
            var rows = await db.History.Where(h => h.Source == board.Source && h.Status != HistoricalPosting.Closed).ToListAsync(ct);
            var now = time.GetUtcNow().UtcDateTime;
            try
            {
                var live = await automation.DiscoverAsync(board.Source, ct);
                foreach (var r in rows) r.Verify(live, now);
                open += rows.Count(r => r.Status == HistoricalPosting.Open);
                closed += rows.Count(r => r.Status == HistoricalPosting.Closed);
            }
            catch (InvalidOperationException ex)
            {
                foreach (var r in rows) r.Fail($"não conferida: {ex.Message}", now);
                failed += rows.Count;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
            {
                log.LogInformation("History check of {Source} failed: {Error}", board.Source, ex.Message);
                foreach (var r in rows) r.Fail($"o quadro não respondeu: {ex.Message}", now);
                failed += rows.Count;
            }
            checkedRows += rows.Count;
            await db.SaveChangesAsync(ct);
        }
        return new HistoryCheck(due.Count, checkedRows, open, closed, failed);
    }
}

internal sealed record HistorySnapshotRow(string Snapshot, int Rows, DateTime ImportedAt);
internal sealed record HistoryBoardRow(string Source, int Open, int Total, DateTime? CheckedAt);
internal sealed record HistoryTotals
{
    public int Total { get; init; }
    public int Unverified { get; init; }
    public int Open { get; init; }
    public int Closed { get; init; }
    public int WithSource { get; init; }
    public int Boards { get; init; }
    public int BoardsChecked { get; init; }
    public int BrazilFit { get; init; }
    public DateTime? LastCheck { get; init; }
}

/// <summary>O histórico importado: por estado, por snapshot e os quadros com vaga aberta conferida ao vivo.</summary>
internal sealed record HistorySummary(HistoryTotals Totals, IReadOnlyList<HistorySnapshotRow> Snapshots, IReadOnlyList<HistoryBoardRow> OpenBoards, IReadOnlyList<Count> Eligibility);

internal sealed record HistorySummaryQuery : IQuery<HistorySummary>;

internal sealed class HistorySummaryHandler(IReadDb<JobsDbContext> db) : IQueryHandler<HistorySummaryQuery, HistorySummary>
{
    public async Task<Result<HistorySummary>> Handle(HistorySummaryQuery q, CancellationToken ct)
    {
        var totals = await db.FirstAsync<HistoryTotals>("""
            select count(*)::int as total,
                   count(*) filter (where status = 'UNVERIFIED')::int as unverified,
                   count(*) filter (where status = 'OPEN')::int as open,
                   count(*) filter (where status = 'CLOSED')::int as closed,
                   count(*) filter (where source is not null)::int as with_source,
                   count(distinct source)::int as boards,
                   count(distinct source) filter (where checked_at is not null)::int as boards_checked,
                   count(*) filter (where eligibility in ('BR_ELIGIBLE_CONFIRMED', 'BR_ELIGIBLE_POSSIBLE', 'RELOCATION_WITH_SPONSORSHIP'))::int as brazil_fit,
                   max(checked_at) as last_check
            from jobs.history
            """, null, ct) ?? new HistoryTotals();
        var snapshots = await db.ListAsync<HistorySnapshotRow>(
            "select snapshot, count(*)::int as rows, max(imported_at) as imported_at from jobs.history group by snapshot order by max(imported_at) desc", null, ct);
        var boards = await db.ListAsync<HistoryBoardRow>("""
            select source, count(*) filter (where status = 'OPEN')::int as open, count(*)::int as total, max(checked_at) as checked_at
            from jobs.history where source is not null group by source having count(*) filter (where status = 'OPEN') > 0 order by open desc limit 12
            """, null, ct);
        var eligibility = await db.ListAsync<Count>(
            "select coalesce(eligibility, 'UNKNOWN') as key, count(*)::int as value from jobs.history group by 1 order by value desc", null, ct);
        return new HistorySummary(totals, snapshots, boards, eligibility);
    }
}

/// <summary>GET /api/history (o resumo), POST /api/history/rows (um lote do importador) e POST /api/history/revalidate (conferir N quadros agora).</summary>
internal sealed class HistoryEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/history").WithTags("Jobs");
        group.MapGet("", async (ISender sender, CancellationToken ct) => (await sender.Send(new HistorySummaryQuery(), ct)).ToHttp(Results.Ok));
        group.MapPost("rows", async (ImportHistoryCommand body, ISender sender, CancellationToken ct) => (await sender.Send(body, ct)).ToHttp(Results.Ok));
        group.MapPost("revalidate", async (RevalidateInput? body, ISender sender, CancellationToken ct) =>
            (await sender.Send(new RevalidateHistoryCommand(body?.Boards ?? 5), ct)).ToHttp(Results.Ok));
    }
}

internal sealed record RevalidateInput(int? Boards);
