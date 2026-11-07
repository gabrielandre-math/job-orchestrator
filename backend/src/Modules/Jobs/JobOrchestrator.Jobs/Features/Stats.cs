namespace JobOrchestrator.Jobs.Features;

internal sealed record Count(string Key, int Value);
internal sealed record DayCount(DateOnly Day, int Value);

/// <summary>As abertas e únicas (não duplicadas nem encerradas), as abertas a quem mora no Brasil, as restritas e as que chegaram nas últimas 24 h.</summary>
internal sealed record OpenCounts
{
    public int Open { get; init; }
    public int BrazilOk { get; init; }
    public int Restricted { get; init; }
    public int Fresh { get; init; }
}

/// <summary>Os números da área de vagas: situação, fonte, nota, modalidade, vínculo, senioridade, stack mais pedida e vagas novas por dia.</summary>
internal sealed record JobStats(
    IReadOnlyList<Count> ByStatus, IReadOnlyList<Count> BySource, IReadOnlyList<Count> Scores, IReadOnlyList<Count> ByModality,
    IReadOnlyList<Count> ByContract, IReadOnlyList<Count> BySeniority, IReadOnlyList<Count> TopStack, IReadOnlyList<DayCount> PerDay, int Total, OpenCounts Open);

internal sealed record JobStatsQuery : IQuery<JobStats>;

internal sealed class JobStatsHandler(IReadDb<JobsDbContext> db) : IQueryHandler<JobStatsQuery, JobStats>
{
    public async Task<Result<JobStats>> Handle(JobStatsQuery q, CancellationToken ct)
    {
        Task<IReadOnlyList<Count>> By(string sql) => db.ListAsync<Count>(sql, null, ct);
        var status = await By("select status as key, count(*)::int as value from jobs.postings group by status order by value desc");
        // Fonte: a plataforma (greenhouse:empresa vira greenhouse), como no canvas.
        var source = await By("select split_part(source, ':', 1) as key, count(*)::int as value from jobs.postings group by 1 order by value desc");
        var scores = await By("""
            select (least(score, 99) / 10 * 10)::text as key, count(*)::int as value from jobs.postings where score is not null group by 1 order by min(score)
            """);
        var modality = await By("select coalesce(modality, 'unknown') as key, count(*)::int as value from jobs.postings where status <> 'Duplicate' group by 1 order by value desc");
        var contract = await By("select coalesce(contract, 'unknown') as key, count(*)::int as value from jobs.postings where status <> 'Duplicate' group by 1 order by value desc");
        var seniority = await By("select coalesce(seniority, 'unknown') as key, count(*)::int as value from jobs.postings where status <> 'Duplicate' group by 1 order by value desc");
        var stack = await By("select t as key, count(*)::int as value from jobs.postings, unnest(stack) t where status <> 'Duplicate' group by t order by value desc, t limit 16");
        var days = await db.ListAsync<DayCount>("""
            select d::date as day, (select count(*)::int from jobs.postings p where p.first_seen_at >= d and p.first_seen_at < d + interval '1 day') as value
            from generate_series(date_trunc('day', now()) - interval '13 days', date_trunc('day', now()), interval '1 day') d order by d
            """, null, ct);
        var open = await db.FirstAsync<OpenCounts>("""
            select count(*)::int as open,
                   count(*) filter (where eligibility = 'BR_ELIGIBLE_CONFIRMED')::int as brazil_ok,
                   count(*) filter (where eligibility = 'BR_RESTRICTED')::int as restricted,
                   count(*) filter (where first_seen_at >= now() - interval '24 hours')::int as fresh
            from jobs.postings where duplicate_of is null and status not in ('Expired', 'Duplicate')
            """, null, ct) ?? new OpenCounts();
        return new JobStats(status, source, scores, modality, contract, seniority, stack, days, status.Sum(s => s.Value), open);
    }
}

/// <summary>
/// Logo da empresa pelo domínio: o ícone do site (Google, depois DuckDuckGo), guardado no diretório de dados para não buscar de novo.
/// Sem ícone, 404 e a tela mostra o monograma. Só o domínio sai da máquina, nunca dado seu.
/// </summary>
internal sealed class CompanyLogos(IHttpClientFactory http)
{
    private static string Dir => AppPaths.In(AppPaths.Data, "logos");

    public async Task<string?> PathAsync(string domain, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(domain, @"^[a-z0-9.-]{3,120}$")) return null;
        var file = Path.Combine(Dir, domain + ".png");
        var none = Path.Combine(Dir, domain + ".none");
        if (File.Exists(file)) return file;
        if (File.Exists(none) && File.GetLastWriteTimeUtc(none) > DateTime.UtcNow.AddDays(-7)) return null;
        var client = http.CreateClient("logos");
        foreach (var url in new[] { $"https://www.google.com/s2/favicons?domain={domain}&sz=128", $"https://icons.duckduckgo.com/ip3/{domain}.ico" })
        {
            try
            {
                using var res = await client.GetAsync(url, ct);
                var type = res.Content.Headers.ContentType?.MediaType ?? "";
                if (!res.IsSuccessStatusCode || !type.StartsWith("image/", StringComparison.Ordinal)) continue;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct);
                // Ícone de 16 px ou vazio não serve como logo.
                if (bytes.Length < 600) continue;
                await File.WriteAllBytesAsync(file, bytes, ct);
                return file;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { }
        }
        await File.WriteAllTextAsync(none, "", ct);
        return null;
    }
}

internal sealed class CompanyEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app) =>
        app.MapGet("/api/companies/{domain}/logo", async (string domain, CompanyLogos logos, HttpContext http, CancellationToken ct) =>
        {
            var path = await logos.PathAsync(domain.ToLowerInvariant(), ct);
            // No icon for this site: 204, so the page draws the initials without a failed load in the console. Asked again tomorrow.
            if (path is null) { http.Response.Headers.CacheControl = "public, max-age=86400"; return Results.NoContent(); }
            http.Response.Headers.CacheControl = "public, max-age=604800";
            return Results.File(path, "image/png");
        }).WithTags("Jobs");
}
