namespace JobOrchestrator.Jobs.Features;

/// <summary>Uma vaga na caixa de entrada. Propriedades, não construtor: o Npgsql entrega text[] como Array.</summary>
internal record JobRow
{
    public Guid Id { get; init; }
    public string Company { get; init; } = "";
    public string Title { get; init; } = "";
    public string Location { get; init; } = "";
    public string Status { get; init; } = "";
    public int? Score { get; init; }
    public double? Confidence { get; init; }
    public string? Reason { get; init; }
    public string ApplyUrl { get; init; } = "";
    public string Source { get; init; } = "";
    public DateTime FirstSeenAt { get; init; }
    public DateTime? LastVerifiedAt { get; init; }
    public bool Open { get; init; }
    public Guid? DuplicateOf { get; init; }
    public string? Seniority { get; init; }
    public string? Modality { get; init; }
    public string? SalaryText { get; init; }
    public string? Contract { get; init; }
    public string? English { get; init; }
    public string? Sponsorship { get; init; }
    public string? Eligibility { get; init; }
    public string? EligibilityEvidence { get; init; }
    public string ApplyChannel { get; init; } = "";
    /// <summary>The platform the app fills and sends by itself for this job, or null when the sending is yours (copilot, e-mail, link).</summary>
    public string? SentBy { get; init; }
    public string? ApplyEmail { get; init; }
    public string[] Stack { get; init; } = [];
    public string? CompanyDomain { get; init; }
}

/// <summary>Consulta (CQRS): as vagas, com filtro por situação e busca por texto.</summary>
internal sealed record ListJobsQuery(string? Status, string? Search) : IQuery<IReadOnlyList<JobRow>>;

internal sealed class ListJobsHandler(IReadDb<JobsDbContext> db, IAutomationApi automation) : IQueryHandler<ListJobsQuery, IReadOnlyList<JobRow>>
{
    private const string Sql = """
        select id, company, title, location, status, score, confidence, reason, apply_url, source, first_seen_at, last_verified_at, open, duplicate_of,
               seniority, modality, salary_text, contract, english, sponsorship, eligibility, eligibility_evidence, apply_channel, apply_email, stack, company_domain
        from jobs.postings
        where (@Status is null or status = @Status)
          and (@Search is null or company ilike '%' || @Search || '%' or title ilike '%' || @Search || '%')
        order by case status when 'Shortlisted' then 0 when 'Scored' then 1 when 'Discovered' then 2 else 3 end, score desc nulls last, first_seen_at desc
        limit 500
        """;

    public async Task<Result<IReadOnlyList<JobRow>>> Handle(ListJobsQuery q, CancellationToken ct)
    {
        var rows = await db.ListAsync<JobRow>(Sql, new { q.Status, Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim() }, ct);
        // Collecting a job is not sending it: each row says if an adapter of the app sends it, before you trust the night with it.
        return Result<IReadOnlyList<JobRow>>.Ok([.. rows.Select(r => r with
        {
            SentBy = automation.CapabilityOf(r.ApplyUrl, r.ApplyChannel) switch
            {
                { Channel: "ats", Platform: { } p } => p.Replace("Formulário ", ""),
                { Channel: "site" } => "site da vaga (tentativa)",
                _ => null
            }
        })]);
    }
}

/// <summary>A vaga aberta: dados, fatores da nota e trecho da descrição.</summary>
internal sealed record JobDetail(JobRow Job, IReadOnlyList<Factor> Factors, string Description, int Snapshots, IReadOnlyList<string> IntegrityWarnings);

internal sealed record GetJobQuery(Guid Id) : IQuery<JobDetail>;

internal sealed class GetJobHandler(IReadDb<JobsDbContext> db) : IQueryHandler<GetJobQuery, JobDetail>
{
    private sealed record Row : JobRow
    {
        public string Breakdown { get; init; } = "[]";
        public string Description { get; init; } = "";
        public int Snapshots { get; init; }
        public int IntegritySignals { get; init; }
    }

    private const string Sql = """
        select p.id, p.company, p.title, p.location, p.status, p.score, p.confidence, p.reason, p.apply_url, p.source, p.first_seen_at,
               p.last_verified_at, p.open, p.duplicate_of, p.seniority, p.modality, p.salary_text, p.contract, p.english, p.sponsorship,
               p.apply_channel, p.apply_email, p.breakdown::text as breakdown, p.description, p.integrity_signals, p.stack, p.company_domain, p.eligibility, p.eligibility_evidence,
               (select count(*)::int from jobs.snapshots s where s.job_id = p.id) as snapshots
        from jobs.postings p where p.id = @Id
        """;

    public async Task<Result<JobDetail>> Handle(GetJobQuery q, CancellationToken ct)
    {
        var r = await db.FirstAsync<Row>(Sql, new { q.Id }, ct);
        if (r is null) return Error.NotFound("job-not-found", "Vaga não encontrada.");
        JobRow job = r with { };
        var factors = JsonSerializer.Deserialize<List<Factor>>(r.Breakdown, JsonSerializerOptions.Web) ?? [];
        var content = PostingIntegrity.Inspect(r.Description);
        var warnings = PostingIntegrity.Warnings((PostingSignals)r.IntegritySignals | content.Signals);
        return new JobDetail(job, factors, content.Text.Length > 12000 ? content.Text[..12000] + "…" : content.Text, r.Snapshots, warnings);
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
        group.MapPost("{id:guid}/compare-fit", async (Guid id, ISender sender, CancellationToken ct) =>
            (await sender.Send(new CompareFitQuery(id), ct)).ToHttp(Results.Ok));
        group.MapGet("stats", async (ISender sender, CancellationToken ct) => (await sender.Send(new JobStatsQuery(), ct)).ToHttp(Results.Ok));
    }
}

/// <summary>O que o módulo mostra aos outros.</summary>
internal sealed class JobsApi(IReadDb<JobsDbContext> db, ISender sender, ICampaignsApi campaigns) : IJobsApi
{
    public async Task<bool> SendingAllowedAsync(CancellationToken ct) =>
        (await campaigns.ListAsync(ct)).FirstOrDefault() is { Mode: not (SubmissionMode.DiscoveryOnly or SubmissionMode.PrepareOnly) };

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

    public async Task<JobText?> TextAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.FirstAsync<JobText>(
            "select id, title, company, location, description, source, seniority, modality, contract, english, salary_currency from jobs.postings where id = @jobId",
            new { jobId }, ct);
        return job is null ? null : job with
        {
            Title = PostingIntegrity.Inspect(job.Title).Text, Company = PostingIntegrity.Inspect(job.Company).Text,
            Location = PostingIntegrity.Inspect(job.Location).Text, Description = PostingIntegrity.Inspect(job.Description).Text
        };
    }

    // Aberta e única: não duplicada e não encerrada (a vaga que some da fonte vira Expired, sem apagar o histórico).
    private const string Open = "duplicate_of is null and status not in ('Expired', 'Duplicate')";

    public Task<IReadOnlyList<LocationCount>> OpenLocationsAsync(CancellationToken ct) => db.ListAsync<LocationCount>(
        $"""
        select coalesce(location, '') as location, count(*)::int as open,
               count(*) filter (where eligibility = 'BR_ELIGIBLE_CONFIRMED')::int as brazil_ok,
               count(*) filter (where eligibility = 'BR_ELIGIBLE_POSSIBLE')::int as brazil_possible,
               count(*) filter (where eligibility = 'BR_RESTRICTED')::int as restricted
        from jobs.postings where {Open} group by coalesce(location, '')
        """, null, ct);

    public async Task<OpenSummary> OpenSummaryAsync(CancellationToken ct) => await db.FirstAsync<OpenSummary>(
        $"""
        select count(*)::int as open, count(distinct lower(company))::int as companies,
               count(*) filter (where eligibility = 'BR_ELIGIBLE_CONFIRMED')::int as brazil_ok,
               count(*) filter (where eligibility = 'BR_ELIGIBLE_POSSIBLE')::int as brazil_possible,
               count(*) filter (where eligibility = 'BR_RESTRICTED')::int as restricted,
               count(*) filter (where eligibility = 'REVIEW')::int as review,
               count(*) filter (where eligibility is null or eligibility = 'UNKNOWN')::int as unknown,
               count(*) filter (where source_updated_at is not null)::int as with_date
        from jobs.postings where {Open}
        """, null, ct) ?? new OpenSummary();

    public Task<IReadOnlyList<EmailTarget>> EmailTargetsAsync(CancellationToken ct) => db.ListAsync<EmailTarget>(
        """
        select id, company, title, location, apply_email, apply_url, company_domain, coalesce(score, 0) as score, first_seen_at, eligibility
        from jobs.postings
        where status = 'Shortlisted' and duplicate_of is null and apply_email is not null and apply_email <> ''
        order by score desc nulls last, first_seen_at desc limit 200
        """, null, ct);

    public Task<IReadOnlyList<QueuedJob>> QueueAsync(string channel, int limit, CancellationToken ct) => db.ListAsync<QueuedJob>(
        """
        select id, company, title, location, apply_url, apply_channel, eligibility, coalesce(score, 0) as score
        from jobs.postings
        where status = 'Shortlisted' and duplicate_of is null and apply_channel = @channel and apply_url <> ''
        order by score desc nulls last, first_seen_at desc limit @limit
        """, new { channel, limit }, ct);

    public async Task<string?> EligibilityAsync(Guid jobId, CancellationToken ct) =>
        (await db.ListAsync<string?>("select eligibility from jobs.postings where id = @jobId", new { jobId }, ct)).FirstOrDefault();

    public Task<IReadOnlyList<string>> ApplyLinksAsync(int limit, CancellationToken ct) => db.ListAsync<string>(
        "select apply_url from (select apply_url, max(first_seen_at) as seen from jobs.postings where apply_url <> '' group by apply_url) t order by seen desc limit @limit", new { limit }, ct);

    public Task<IReadOnlyList<string>> CompanyDomainsAsync(int limit, CancellationToken ct) => db.ListAsync<string>(
        "select company_domain from (select company_domain, count(*) as n from jobs.postings where company_domain is not null and status <> 'Duplicate' group by company_domain) t order by n desc limit @limit", new { limit }, ct);

    public async Task<HistoryCheck> RevalidateHistoryAsync(int boards, CancellationToken ct) =>
        (await sender.Send(new RevalidateHistoryCommand(boards), ct)).Value ?? new HistoryCheck(0, 0, 0, 0, 0);

    public Task<IReadOnlyList<HistoryLead>> HistoryLeadsAsync(int limit, CancellationToken ct) => db.ListAsync<HistoryLead>(
        $"""
        select source, count(*)::int as open from jobs.history
        where status = '{HistoricalPosting.Open}' and source is not null and eligibility in ('BR_ELIGIBLE_CONFIRMED', 'BR_ELIGIBLE_POSSIBLE', 'RELOCATION_WITH_SPONSORSHIP')
        group by source order by open desc limit @limit
        """, new { limit }, ct);

    public Task<IReadOnlyList<SourceCount>> SourcesAsync(CancellationToken ct) => db.ListAsync<SourceCount>(
        "select source, count(*)::int as postings, count(*) filter (where status = 'Shortlisted')::int as shortlisted from jobs.postings group by source order by source",
        null, ct);
}
