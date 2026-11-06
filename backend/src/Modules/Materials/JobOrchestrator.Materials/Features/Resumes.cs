namespace JobOrchestrator.Materials.Features;

/// <summary>Uma versão de currículo como a tela mostra (sem o HTML).</summary>
internal sealed record ResumeView
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public int ProfileVersion { get; init; }
    public string Language { get; init; } = "";
    public string Mode { get; init; } = "";
    public string? WrittenBy { get; init; }
    public string? ReviewedBy { get; init; }
    public bool Reviewed { get; init; }
    public string Sha256 { get; init; } = "";
    public int Pages { get; init; }
    public string Provenance { get; init; } = "[]";
    public Guid ThreadId { get; init; }
    public DateTime CreatedAt { get; init; }
    public string FileName { get; init; } = "";
}

/// <summary>Consulta (CQRS): as versões de currículo de uma candidatura, mais recente primeiro.</summary>
internal sealed record ListResumesQuery(Guid ApplicationId) : IQuery<IReadOnlyList<ResumeView>>;

internal sealed class ListResumesHandler(IReadDb<MaterialsDbContext> db) : IQueryHandler<ListResumesQuery, IReadOnlyList<ResumeView>>
{
    public async Task<Result<IReadOnlyList<ResumeView>>> Handle(ListResumesQuery q, CancellationToken ct) =>
        Result<IReadOnlyList<ResumeView>>.Ok(await db.ListAsync<ResumeView>("""
            select id, application_id, profile_version, language, mode, written_by, reviewed_by, reviewed, sha256, pages, provenance::text as provenance, thread_id, created_at,
                   regexp_replace(pdf_path, '^.*/', '') as file_name
            from materials.resume_versions where application_id = @ApplicationId order by created_at desc
            """, new { q.ApplicationId }, ct));
}

/// <summary>Rotas HTTP (Carter) dos materiais.</summary>
internal sealed class ResumeEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/materials").WithTags("Materials");
        group.MapGet("resumes", async (Guid applicationId, ISender sender, CancellationToken ct) =>
            (await sender.Send(new ListResumesQuery(applicationId), ct)).ToHttp(Results.Ok));
        // Prévia: o currículo da vaga sem abrir candidatura (a pasta e o vínculo ficam com o id da vaga). Nada é enviado.
        group.MapPost("preview/{jobId:guid}", async (Guid jobId, IJobsApi jobs, IMaterialsApi materials, CancellationToken ct) =>
        {
            var job = await jobs.TextAsync(jobId, ct);
            if (job is null) return Results.NotFound();
            var result = await materials.ComposeResumeAsync(new ComposeRequest(jobId, jobId, job.Company, job.Title), ct);
            return result.Ok ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });
        // Currículo base (sem vaga, sem IA) no tema e no idioma escolhidos; o tema escolhido vale também para os currículos das vagas.
        group.MapPost("base", async (BaseInput body, Compose.ResumeComposer composer, CancellationToken ct) =>
        {
            if (Compose.Template.Themes.Any(t => t.Key == body.Theme)) await UserSettings.SetAsync(new Dictionary<string, object?> { ["Materials:Theme"] = body.Theme });
            var result = await composer.ComposeBaseAsync(body.Language ?? "pt", body.Theme ?? "classico", ct);
            return result.Ok ? Results.Ok(result) : Results.UnprocessableEntity(result);
        });
        group.MapGet("base", async (MaterialsDbContext db, IConfiguration config, CancellationToken ct) => Results.Ok(new
        {
            theme = config.GetValue("Materials:Theme", "classico"),
            themes = Compose.Template.Themes.Select(t => new { key = t.Key, name = t.Name, note = t.Note }),
            versions = await db.Resumes.AsNoTracking().Where(v => v.JobId == Guid.Empty).OrderByDescending(v => v.CreatedAt).Take(8)
                .Select(v => new { v.Id, v.Language, v.Pages, v.CreatedAt, v.WrittenBy }).ToListAsync(ct),
        }));
        // O PDF só sai do diretório de materiais e pelo id da versão: nunca por caminho vindo de fora.
        group.MapGet("resumes/{id:guid}/pdf", async (Guid id, MaterialsDbContext db, CancellationToken ct) =>
        {
            var v = await db.Resumes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (v is null || !File.Exists(v.PdfPath) || !Path.GetFullPath(v.PdfPath).StartsWith(Path.GetFullPath(AppPaths.In(AppPaths.Data, "materials")), StringComparison.Ordinal))
                return Results.NotFound();
            return Results.File(v.PdfPath, "application/pdf", Path.GetFileName(v.PdfPath), enableRangeProcessing: true);
        });
    }
}

/// <summary>O idioma (pt ou en) e o tema do currículo base.</summary>
internal sealed record BaseInput(string? Language, string? Theme);
