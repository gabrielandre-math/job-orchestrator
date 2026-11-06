using System.Globalization;
using System.Net;
using System.Security.Cryptography;

internal sealed class CaptchaLab
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Challenge> _challenges = [];
    private sealed record Challenge(string Kind, string Answer, string Scope, DateTimeOffset ExpiresAt);

    public void Map(WebApplication app)
    {
        var lab = app.MapGroup("/lab");
        lab.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Host.Host != "127.0.0.1" || http.Connection.RemoteIpAddress is not { } ip
                || !IPAddress.IsLoopback(ip) || http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                return Results.NotFound();

            http.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        var captcha = lab.MapGroup("/captcha");
        captcha.MapGet("", (string? kind) => Issue(kind ?? "image", "/lab/captcha"));
        captcha.MapGet("/alphabet", () => Results.File(CaptchaImage.Render("0123456789"), "image/bmp"));
        captcha.MapGet("/{id:guid}/image", Image);
        captcha.MapPost("", async (HttpRequest request, CancellationToken ct) =>
            await ValidateAsync(request, "/lab/captcha", ct)
                ? Results.Ok(new { jobs = new[] { new { id = "lab-1", title = "Backend Engineer (.NET)", company = "Empresa fictícia" } } })
                : Rejected());
        LinkedInLab.Map(lab, this);
    }

    public IResult Issue(string kind, string scope, int status = StatusCodes.Status200OK)
    {
        if (kind == "unsupported")
            return Results.Content(Html.Page("""
                <h1>Desafio interativo simulado</h1>
                <div data-captcha-kind="interactive">Este desafio requer intervenção manual.</div>
                """), "text/html; charset=utf-8", statusCode: status);
        if (kind is not ("image" or "math")) return Results.NotFound();

        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var left = RandomNumberGenerator.GetInt32(1, 100);
        var right = RandomNumberGenerator.GetInt32(1, 100);
        var answer = (kind == "math" ? left + right : RandomNumberGenerator.GetInt32(10000, 100000))
            .ToString(CultureInfo.InvariantCulture);
        lock (_gate)
        {
            foreach (var expired in _challenges.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
                _challenges.Remove(expired);

            if (_challenges.Count >= 128) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            _challenges.Add(id, new(kind, answer, scope, now.AddMinutes(2)));
        }

        var prompt = kind == "math"
            ? $"""<p id="challenge-prompt" data-captcha-expression>{left} + {right}</p>"""
            : $"""<img id="challenge-prompt" src="/lab/captcha/{id}/image" alt="Desafio visual com cinco dígitos" width="204" height="72">""";
        return Results.Content(Html.Page($"""
            <h1>Laboratório de CAPTCHA</h1>
            <form data-captcha-kind="{kind}" method="post" action="{WebUtility.HtmlEncode(scope)}">
              <input type="hidden" name="challenge" value="{id}">
              {prompt}
              <label for="answer">Resposta</label>
              <input id="answer" name="answer" inputmode="numeric" aria-describedby="challenge-prompt" required>
              <button type="submit">Ver vaga fictícia</button>
            </form>
            """), "text/html; charset=utf-8", statusCode: status);
    }

    private IResult Image(Guid id)
    {
        Challenge? challenge;
        lock (_gate) _challenges.TryGetValue(id, out challenge);
        return challenge is { Kind: "image" } && challenge.ExpiresAt > DateTimeOffset.UtcNow
            ? Results.File(CaptchaImage.Render(challenge.Answer), "image/bmp")
            : Results.NotFound();
    }

    public async Task<bool> ValidateAsync(HttpRequest request, string scope, CancellationToken ct)
    {
        if (!request.HasFormContentType) return false;
        var form = await request.ReadFormAsync(ct);
        if (!Guid.TryParse(form["challenge"], out var id)) return false;

        lock (_gate)
        {
            if (!_challenges.TryGetValue(id, out var challenge) || challenge.Scope != scope) return false;
            // Uma tentativa por desafio, inclusive quando a resposta está errada.
            _challenges.Remove(id);
            return challenge.ExpiresAt > DateTimeOffset.UtcNow && form["answer"] == challenge.Answer;
        }
    }

    public static IResult Rejected() => Results.BadRequest(new { error = "Código incorreto, expirado ou já usado. Abra um desafio novo." });
}
