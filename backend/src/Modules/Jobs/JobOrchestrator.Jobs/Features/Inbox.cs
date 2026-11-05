namespace JobOrchestrator.Jobs.Features;

/// <summary>Uma vaga na caixa de entrada.</summary>
internal sealed record JobRow(
    Guid Id, string Company, string Title, string Location, string Status, int? Score, double? Confidence, string? Reason,
    string ApplyUrl, string Source, DateTime FirstSeenAt, DateTime? LastVerifiedAt, bool Open, Guid? DuplicateOf,
    string? Seniority, string? Modality, string? SalaryText, string? Contract, string? English, string? Sponsorship, string ApplyChannel, string? ApplyEmail);

/// <summary>Consulta (CQRS): as vagas, com filtro por situação e busca por texto.</summary>
internal sealed record ListJobsQuery(string? Status, string? Search) : IQuery<IReadOnlyList<JobRow>>;

internal sealed class ListJobsHandler(IReadDb<JobsDbContext> db) : IQueryHandler<ListJobsQuery, IReadOnlyList<JobRow>>
{
    private const string Sql = """
        select id, company, title, location, status, score, confidence, reason, apply_url, source, first_seen_at, last_verified_at, open, duplicate_of,
               seniority, modality, salary_text, contract, english, sponsorship, apply_channel, apply_email
        from jobs.postings
        where (@Status is null or status = @Status)
          and (@Search is null or company ilike '%' || @Search || '%' or title ilike '%' || @Search || '%')
        order by case status when 'Shortlisted' then 0 when 'Scored' then 1 when 'Discovered' then 2 else 3 end, score desc nulls last, first_seen_at desc
        limit 500
        """;

    public async Task<Result<IReadOnlyList<JobRow>>> Handle(ListJobsQuery q, CancellationToken ct) =>
        Result<IReadOnlyList<JobRow>>.Ok(await db.ListAsync<JobRow>(Sql, new { q.Status, Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim() }, ct));
}

/// <summary>A vaga aberta: dados, fatores da nota e trecho da descrição.</summary>
internal sealed record JobDetail(JobRow Job, IReadOnlyList<Factor> Factors, string Description, int Snapshots);

internal sealed record GetJobQuery(Guid Id) : IQuery<JobDetail>;

internal sealed class GetJobHandler(IReadDb<JobsDbContext> db) : IQueryHandler<GetJobQuery, JobDetail>
{
    private sealed record Row(Guid Id, string Company, string Title, string Location, string Status, int? Score, double? Confidence, string? Reason,
        string ApplyUrl, string Source, DateTime FirstSeenAt, DateTime? LastVerifiedAt, bool Open, Guid? DuplicateOf,
        string? Seniority, string? Modality, string? SalaryText, string? Contract, string? English, string? Sponsorship, string ApplyChannel, string? ApplyEmail,
        string Breakdown, string Description, int Snapshots);

    private const string Sql = """
        select p.id, p.company, p.title, p.location, p.status, p.score, p.confidence, p.reason, p.apply_url, p.source, p.first_seen_at,
               p.last_verified_at, p.open, p.duplicate_of, p.seniority, p.modality, p.salary_text, p.contract, p.english, p.sponsorship,
               p.apply_channel, p.apply_email, p.breakdown::text as breakdown, p.description,
               (select count(*)::int from jobs.snapshots s where s.job_id = p.id) as snapshots
        from jobs.postings p where p.id = @Id
        """;

    public async Task<Result<JobDetail>> Handle(GetJobQuery q, CancellationToken ct)
    {
        var r = await db.FirstAsync<Row>(Sql, new { q.Id }, ct);
        if (r is null) return Error.NotFound("job-not-found", "Vaga não encontrada.");
        var job = new JobRow(r.Id, r.Company, r.Title, r.Location, r.Status, r.Score, r.Confidence, r.Reason, r.ApplyUrl, r.Source, r.FirstSeenAt, r.LastVerifiedAt, r.Open, r.DuplicateOf,
            r.Seniority, r.Modality, r.SalaryText, r.Contract, r.English, r.Sponsorship, r.ApplyChannel, r.ApplyEmail);
        var factors = JsonSerializer.Deserialize<List<Factor>>(r.Breakdown, JsonSerializerOptions.Web) ?? [];
        return new JobDetail(job, factors, r.Description.Length > 4000 ? r.Description[..4000] + "…" : r.Description, r.Snapshots);
    }
}

/// <summary>Rotas HTTP (Carter) das vagas.</summary>
internal sealed class JobEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs");
        group.MapGet("", async (string? status, string? q, ISender sender, CancellationToken ct) => (await sender.Send(new ListJobsQuery(status, q), ct)).ToHttp(Results.Ok));
        group.MapGet("{id:guid}", async (Guid id, ISender sender, CancellationToken ct) => (await sender.Send(new GetJobQuery(id), ct)).ToHttp(Results.Ok));
    }
}

/// <summary>O que o módulo mostra aos outros.</summary>
internal sealed class JobsApi(IReadDb<JobsDbContext> db) : IJobsApi
{
    private const string Sql = """
        select count(*)::int as total,
               count(*) filter (where status in ('Discovered','Scored'))::int as discovered,
               count(*) filter (where status = 'Shortlisted')::int as shortlisted,
               count(*) filter (where status = 'Rejected')::int as rejected,
               count(*) filter (where status = 'Duplicate')::int as duplicates,
               count(*) filter (where status = 'Expired')::int as expired
        from jobs.postings
        """;

    public async Task<JobCounts> CountsAsync(CancellationToken ct) => await db.FirstAsync<JobCounts>(Sql, null, ct) ?? new JobCounts(0, 0, 0, 0, 0, 0);

    public Task<IReadOnlyList<SourceCount>> SourcesAsync(CancellationToken ct) => db.ListAsync<SourceCount>(
        "select source, count(*)::int as postings, count(*) filter (where status = 'Shortlisted')::int as shortlisted from jobs.postings group by source order by source",
        null, ct);
}
