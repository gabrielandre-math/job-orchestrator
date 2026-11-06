using JobOrchestrator.Profile.Import;

namespace JobOrchestrator.Profile.Features;

internal sealed record LinkedInStatusQuery : IQuery<LinkedInStatus>;

internal sealed class LinkedInStatusHandler(ProfileDbContext db) : IQueryHandler<LinkedInStatusQuery, LinkedInStatus>
{
    public async Task<Result<LinkedInStatus>> Handle(LinkedInStatusQuery q, CancellationToken ct)
    {
        var profile = await db.CurrentAsync(ct);
        var doc = profile?.Documents.Where(d => d.Kind == "linkedin").OrderByDescending(d => d.ImportedAt).FirstOrDefault();
        var ids = profile?.Documents.Where(d => d.Kind == "linkedin").Select(d => d.Id).ToHashSet() ?? [];
        var facts = profile?.Facts.Count(f => f.DocumentId is { } id && ids.Contains(id) && f.Status != FactStatus.Rejected) ?? 0;
        var folder = LinkedInExport.Downloads();
        return new LinkedInStatus(doc?.Status == DocumentStatus.Ready, doc?.FileName, doc?.ImportedAt, doc?.Status.ToString(), facts, LinkedInExport.Find(folder), folder);
    }
}

/// <summary>Importa um export achado na pasta de downloads: o nome tem de ser o de um export do LinkedIn, e o arquivo, estar lá.</summary>
internal sealed record ImportLinkedInInput(string Name);

internal sealed class LinkedInEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/profile/linkedin").WithTags("Profile");
        group.MapGet("", async (ISender sender, CancellationToken ct) => (await sender.Send(new LinkedInStatusQuery(), ct)).ToHttp(Results.Ok));
        group.MapPost("import", async (ImportLinkedInInput body, ISender sender, CancellationToken ct) =>
        {
            var name = Path.GetFileName(body.Name ?? "");
            var path = Path.Combine(LinkedInExport.Downloads(), name);
            if (name.Length == 0 || name != body.Name || !LinkedInExport.Looks(name) || !File.Exists(path))
                return Results.Problem(statusCode: 404, title: "not-found", detail: "Não achei esse export do LinkedIn na pasta de downloads.");
            if (new FileInfo(path).Length > LinkedInExport.MaxBytes)
                return Results.Problem(statusCode: 400, title: "too-big", detail: "O arquivo passa de 1 GB. Peça ao LinkedIn só as partes do perfil (a opção específica).");
            byte[] slim;
            try { await using var file = File.OpenRead(path); slim = LinkedInExport.Slim(file); }
            catch (InvalidDataException) { return Results.Problem(statusCode: 400, title: "unreadable", detail: "O ZIP está incompleto ou corrompido. Baixe de novo pelo link do e-mail do LinkedIn."); }
            return (await sender.Send(new ImportDocumentCommand(name, slim), ct)).ToHttp(v => Results.Accepted(value: v));
        });
    }
}
