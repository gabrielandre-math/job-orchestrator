using System.Diagnostics;
using System.Reflection;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace JobOrchestrator.BuildingBlocks.Web;

/// <summary>Converte Result em resposta HTTP.</summary>
public static class ResultHttpExtensions
{
    public static IResult ToHttp<T>(this Result<T> r, Func<T, IResult> ok) => r.IsSuccess ? ok(r.Value!) : r.Error!.ToProblem();

    /// <summary>RFC 9457: toda falha esperada sai como Problem Details.</summary>
    public static IResult ToProblem(this Error e)
    {
        var extras = new Dictionary<string, object?> { ["code"] = e.Code };
        if (e.Fields is not null) return Results.ValidationProblem(e.Fields, e.Message, title: "Dados inválidos", extensions: extras);
        var status = e.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return Results.Problem(detail: e.Message, statusCode: status, title: e.Code, extensions: extras);
    }
}

/// <summary>Encontra as rotas Carter de todos os módulos.</summary>
public static class WebRegistration
{
    public static IServiceCollection AddJoWeb(this IServiceCollection s, params Assembly[] assemblies)
    {
        var modules = assemblies.SelectMany(a => a.GetTypes()).Where(t => !t.IsAbstract && typeof(ICarterModule).IsAssignableFrom(t)).ToArray();
        // Catálogo explícito: o binário de arquivo único não tem .deps.json, e a procura padrão do Carter depende dele.
        s.AddCarter(new DependencyContextAssemblyCatalog(assemblies), c => c.WithModules(modules));
        s.AddExceptionHandler<TrailExceptions>();
        return s;
    }
}

/// <summary>An exception no handler caught goes to the trail with its stack; the answer is still the usual Problem Details.</summary>
internal sealed class TrailExceptions(Trail trail) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        trail.Add("error", "error", $"{ctx.Request.Method} {ctx.Request.Path}", $"{ex.GetType().Name}: {ex.Message}", code: "unhandled",
            data: new Dictionary<string, string?> { ["stack"] = ex.StackTrace is { Length: > 2000 } st ? st[..2000] : ex.StackTrace });
        return ValueTask.FromResult(false);
    }
}

/// <summary>One event the screen sends: a click, a page, an error, an API call that failed.</summary>
public sealed record ClientEvent(string? Name, string? Level, string? Detail, int? Ms, string? Code, string? Page, string? Session);
public sealed record ClientEvents(IReadOnlyList<ClientEvent>? Events);

/// <summary>The trail on the web side: the requests that change something or fail, and the endpoints of the Diagnóstico screen.</summary>
public static class TrailWeb
{
    public static WebApplication UseTrail(this WebApplication app)
    {
        var trail = app.Services.GetRequiredService<Trail>();
        app.Use(async (ctx, next) =>
        {
            var path = ctx.Request.Path;
            if (!path.StartsWithSegments("/api") || path.StartsWithSegments("/api/diagnostics")) { await next(ctx); return; }
            Trail.Trace = ctx.TraceIdentifier;
            var start = Stopwatch.GetTimestamp();
            var failed = false;
            try { await next(ctx); }
            catch { failed = true; throw; }
            finally
            {
                var ms = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                // A request the page gave up on (it moved to another page) is the client's choice, not a server error: 499, as nginx says.
                var status = ctx.RequestAborted.IsCancellationRequested ? 499 : failed ? 500 : ctx.Response.StatusCode;
                var method = ctx.Request.Method;
                // Polling reads that went well are not events; everything that changes something, fails or is slow is. A stream (the
                // live map, the search) stays open by design: its length is not slowness.
                var stream = ctx.Response.ContentType?.StartsWith("text/event-stream", StringComparison.Ordinal) == true;
                // Only a read is slow at 1.5 s: an action that tests a source or asks the AI takes what it takes.
                var slow = method == "GET" && ms > 1500 && !stream && !path.Value!.EndsWith("/logo", StringComparison.Ordinal);
                if (method != "GET" || status >= 400 || slow)
                    trail.Add("http", status == 499 ? "info" : status >= 500 ? "error" : status >= 400 || slow ? "warn" : "info", $"{method} {path}",
                        status == 499 ? "cancelada pela página" : slow && status < 400 ? "lenta" : null, ms,
                        status.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ctx.Request.Headers["X-Jo-Page"].ToString() is { Length: > 0 } page ? new Dictionary<string, string?> { ["page"] = page.Length > 200 ? page[..200] : page } : null);
            }
        });
        var g = app.MapGroup("/api/diagnostics").WithTags("Diagnostics");
        g.MapGet("trail", (string? kind, string? level, string? q, string? trace, long? after, int? limit) => trail.Recent(kind, level, q, trace, after ?? 0, limit ?? 300));
        g.MapGet("days", () => trail.Days().Select(d => new { day = d.Day, bytes = d.Bytes }));
        g.MapGet("days/{day}", (string day) => trail.FileOf(day) is { } file
            ? Results.Stream(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite), "application/x-ndjson", $"rastro-{day}.jsonl")
            : Results.NotFound());
        g.MapPost("client", (ClientEvents body) =>
        {
            foreach (var e in (body.Events ?? []).Take(100))
                trail.Add("ui", e.Level is "error" or "warn" ? e.Level : "info", e.Name ?? "?", e.Detail, e.Ms, e.Code,
                    e.Page is null ? null : new Dictionary<string, string?> { ["page"] = e.Page.Length > 200 ? e.Page[..200] : e.Page }, e.Session is null ? null : $"ui:{e.Session}");
            return Results.NoContent();
        });
        return app;
    }
}
