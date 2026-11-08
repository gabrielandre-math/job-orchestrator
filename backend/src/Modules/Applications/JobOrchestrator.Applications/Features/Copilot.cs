using System.Collections.Concurrent;

namespace JobOrchestrator.Applications.Features;

/// <summary>
/// The browser copilot for the applications the app cannot send alone (login, CAPTCHA, anti-bot checks, boards without a form): you open
/// the job from the app, the extension fills it with everything prepared for that job and attaches its resume, you click send, and the
/// confirmation page marks the application as sent. Only the extension talks to these routes: its id is fixed by the key in its
/// manifest, so the browser's Origin header tells it apart from any page you have open. Nothing here goes to an AI model.
/// </summary>
internal static class CopilotExtension
{
    /// <summary>The extension's id, fixed by the public key in extension/manifest.json; Copilot:ExtensionIds adds others (a store build).</summary>
    public const string Id = "ghclhgdehejmhbcfejcgglhlkjbibmni";

    public static bool From(HttpContext http, IConfiguration config)
    {
        var origin = http.Request.Headers.Origin.ToString();
        if (!origin.StartsWith("chrome-extension://", StringComparison.Ordinal)) return false;
        var id = origin["chrome-extension://".Length..].TrimEnd('/');
        return id == Id || (config["Copilot:ExtensionIds"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(id);
    }
}

/// <summary>When the extension last answered, and the jobs opened from the app in the last 15 minutes: only those are filled on their own.</summary>
internal sealed class CopilotState(TimeProvider time)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private readonly ConcurrentDictionary<Guid, (string Url, DateTime Until)> expected = new();
    private (DateTime At, string Version)? seen;

    public void Seen(string version) => seen = (time.GetUtcNow().UtcDateTime, version);

    public CopilotStatus View()
    {
        var now = time.GetUtcNow().UtcDateTime;
        return new CopilotStatus(seen is { } s && now - s.At < TimeSpan.FromMinutes(3), seen?.At, seen?.Version, CopilotExtension.Id);
    }

    public void Expect(Guid id, string url) => expected[id] = (url, time.GetUtcNow().UtcDateTime + Window);

    /// <summary>The application opened from the app whose link best matches this page, while the 15 minutes last.</summary>
    public Guid? Match(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var page)) return null;
        var now = time.GetUtcNow().UtcDateTime;
        foreach (var (id, e) in expected) if (e.Until < now) expected.TryRemove(id, out _);
        // Same site is not enough: another job on apply.workable.com is not this one. The job's id, or two parts of the path in common.
        return expected.Select(e => (e.Key, Score: CopilotMatch.Score(e.Value.Url, page))).Where(x => x.Score >= 21).OrderByDescending(x => x.Score).Select(x => (Guid?)x.Key).FirstOrDefault();
    }

    public void Done(Guid id) => expected.TryRemove(id, out _);
}

internal sealed record CopilotStatus(bool Connected, DateTime? SeenAt, string? Version, string ExtensionId);
internal sealed record CopilotHello(string? Version);
internal sealed record CopilotQueueItem(Guid Id, string Company, string Title, string ApplyUrl, int Score, bool HasResume, DateTime UpdatedAt);
internal sealed record CopilotQuestion(string Question, string Answer);
internal sealed record CopilotFill(Guid ApplicationId, string Company, string Title, string ApplyUrl, bool HasResume, string FirstName, string LastName, string Email,
    string Phone, string LinkedIn, IReadOnlyDictionary<string, string> Answers, IReadOnlyList<CopilotQuestion> Questions, IReadOnlyList<CopilotQuestion> Prepared);
internal sealed record CopilotSentBody(Guid ApplicationId, string? Url, string? Text);

internal sealed class CopilotEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/copilot").WithTags("Copilot");
        g.MapGet("status", (CopilotState state) => Results.Ok(state.View()));
        g.MapPost("hello", (CopilotHello body, HttpContext http, IConfiguration config, CopilotState state) =>
        {
            if (!CopilotExtension.From(http, config)) return Results.StatusCode(403);
            state.Seen(body.Version ?? "");
            return Results.Ok(state.View());
        });
        // What goes by the site: handed to you because the robot cannot send it, best score first.
        g.MapGet("queue", async (ApplicationsDbContext db, CancellationToken ct) =>
        {
            var list = await db.Applications.AsNoTracking().Where(a => a.State == ApplicationState.ManualHandoff && a.ArchivedAt == null && a.ApplyUrl.StartsWith("http"))
                .OrderByDescending(a => a.Score).ThenByDescending(a => a.UpdatedAt).Take(300).ToListAsync(ct);
            return Results.Ok(list.Select(a => new CopilotQueueItem(a.Id, a.Company, a.Title, a.ApplyUrl, a.Score, a.ResumePath is not null && File.Exists(a.ResumePath), a.UpdatedAt)));
        });
        // The panel opens the link itself, in the browser it runs in; this only tells the extension to expect that page.
        g.MapPost("open/{id:guid}", async (Guid id, ApplicationsDbContext db, CopilotState state, CancellationToken ct) =>
        {
            var a = await db.Applications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            state.Expect(a.Id, a.ApplyUrl);
            return Results.Ok(new { url = a.ApplyUrl });
        });
        g.MapGet("expected", (string url, HttpContext http, IConfiguration config, CopilotState state) =>
            !CopilotExtension.From(http, config) ? Results.StatusCode(403) : state.Match(url) is { } id ? Results.Ok(new { applicationId = id }) : Results.NotFound());
        g.MapGet("fill/{id:guid}", async (Guid id, HttpContext http, IConfiguration config, ApplicationsDbContext db, Workflow.Bundles bundles, CancellationToken ct) =>
        {
            if (!CopilotExtension.From(http, config)) return Results.StatusCode(403);
            var a = await db.Applications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Results.NotFound();
            var b = await bundles.ForAsync(a, ct);
            return Results.Ok(new CopilotFill(a.Id, a.Company, a.Title, a.ApplyUrl, a.ResumePath is not null && File.Exists(a.ResumePath),
                b?.FirstName ?? "", b?.LastName ?? "", b?.Email ?? "", b?.Phone ?? "", b?.LinkedIn ?? "",
                b?.Answers.Where(x => !x.Key.StartsWith("custom:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(x.Value)).ToDictionary() ?? new Dictionary<string, string>(),
                await Copilot.QuestionsAsync(db, a.Id, ct), Copilot.Prepared(a.PreparedFields)));
        });
        g.MapPost("sent", async (CopilotSentBody body, HttpContext http, IConfiguration config, CopilotState state, ISender sender, CancellationToken ct) =>
        {
            if (!CopilotExtension.From(http, config)) return Results.StatusCode(403);
            state.Done(body.ApplicationId);
            return (await sender.Send(new CopilotSentCommand(body.ApplicationId, body.Url ?? "", body.Text ?? ""), ct)).ToHttp(_ => Results.NoContent());
        });
        g.MapPost("declare/{id:guid}", async (Guid id, HttpContext http, IConfiguration config, CopilotState state, ISender sender, CancellationToken ct) =>
        {
            if (!CopilotExtension.From(http, config)) return Results.StatusCode(403);
            state.Done(id);
            return (await sender.Send(new DeclareCommand(id, "Marcada como enviada no copiloto do navegador."), ct)).ToHttp(_ => Results.NoContent());
        });
        // Where the extension's folder is, to load it in Chrome, and a way to open that folder.
        g.MapGet("extension", () => Results.Ok(new { path = Copilot.Folder, exists = File.Exists(Path.Combine(Copilot.Folder, "manifest.json")), id = CopilotExtension.Id }));
        g.MapPost("extension/open", () =>
        {
            if (!Directory.Exists(Copilot.Folder)) return Results.NotFound();
            var opener = OperatingSystem.IsMacOS() ? "/usr/bin/open" : OperatingSystem.IsWindows() ? "explorer.exe" : "xdg-open";
            return Platform.Detach(opener, [Copilot.Folder]) ? Results.NoContent() : Results.Problem("Não foi possível abrir a pasta.", statusCode: 500);
        });
    }
}

internal static class Copilot
{
    public static string Folder => Path.Combine(AppPaths.Resources, "extension");

    /// <summary>The answers written for this job's questions, then yours from the questions window, each with the question as the form asked it.</summary>
    public static async Task<IReadOnlyList<CopilotQuestion>> QuestionsAsync(ApplicationsDbContext db, Guid applicationId, CancellationToken ct)
    {
        var mine = await db.FormAnswers.AsNoTracking().Where(f => f.ApplicationId == applicationId && f.Accepted && f.Answer != "").Select(f => new CopilotQuestion(f.Question, f.Answer)).ToListAsync(ct);
        var yours = AnswerStore.Load().Values.Where(s => !string.IsNullOrWhiteSpace(s.Answer)).Select(s => new CopilotQuestion(s.Question, s.Answer));
        return [.. mine.Concat(yours).DistinctBy(q => q.Question, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>What the robot already filled on this form, label by label; the resume line is a file, attached apart.</summary>
    public static IReadOnlyList<CopilotQuestion> Prepared(string json)
    {
        try
        {
            return [.. (JsonSerializer.Deserialize<List<FieldValue>>(json, JsonSerializerOptions.Web) ?? [])
                .Where(f => f.Label.Length > 0 && f.Value.Length > 0 && !f.Value.Contains("(sha256 ", StringComparison.Ordinal)).Select(f => new CopilotQuestion(f.Label, f.Value))];
        }
        catch (JsonException) { return []; }
    }
}

/// <summary>
/// The confirmation page showed up in your browser after you clicked send: the application is sent, with that page as the proof (the
/// same evidence the robot keeps when it sends). An application waiting for an answer of yours passes through "declared" first.
/// </summary>
internal sealed record CopilotSentCommand(Guid Id, string Url, string Text) : ICommand<Done>, IApplicationsCommand;

internal sealed class CopilotSentHandler(ApplicationsDbContext db, TimeProvider time) : ICommandHandler<CopilotSentCommand, Done>
{
    public async Task<Result<Done>> Handle(CopilotSentCommand c, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => a.Id == c.Id, ct);
        if (app is null) return Error.NotFound("application-not-found", "Candidatura não encontrada.");
        if (app.State == ApplicationState.SubmittedConfirmed) return new Done();
        if (app.State is not (ApplicationState.ManualHandoff or ApplicationState.NeedsHuman or ApplicationState.SubmissionUnknown or ApplicationState.SubmittedDeclared))
            return Error.Conflict("cannot-confirm", "Esta candidatura não estava com você para enviar pelo site.");
        var now = time.GetUtcNow().UtcDateTime;
        var text = c.Text.Length > 300 ? c.Text[..300] + "…" : c.Text;
        await db.ResolveTasksAsync(app.Id, "Enviada pelo site, com o copiloto", now, ct);
        if (app.State == ApplicationState.NeedsHuman)
            app.Move(ApplicationState.SubmittedDeclared, "Enviada por você pelo site, com o copiloto.", "Copiloto", now);
        app.Confirmed(new SubmissionReceipt(c.Url, 3, $"A página de confirmação apareceu no seu navegador: {text}"), "Copiloto", now);
        db.Evidence.Add(new Evidence { Id = Guid.CreateVersion7(), ApplicationId = app.Id, Level = 3, Kind = "copilot_confirmation", Detail = $"{c.Url} | {text}", At = now });
        return new Done();
    }
}
