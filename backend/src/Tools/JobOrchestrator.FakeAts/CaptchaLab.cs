using System.Globalization;
using System.Net;
using System.Security.Cryptography;

internal sealed class CaptchaLab
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, Challenge> _challenges = [];
    private sealed record Challenge(string Code, DateTimeOffset ExpiresAt);

    public void Map(WebApplication app)
    {
        var lab = app.MapGroup("/lab/captcha");
        lab.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (http.Request.Host.Host != "127.0.0.1" || http.Connection.RemoteIpAddress is not { } ip
                || !IPAddress.IsLoopback(ip) || http.Request.Headers["Sec-Fetch-Site"] == "cross-site")
                return Results.NotFound();

            http.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        lab.MapGet("", Issue);
        lab.MapGet("/alphabet", () => Results.File(CaptchaImage.Render("0123456789"), "image/bmp"));
        lab.MapGet("/{id:guid}/image", Image);
        lab.MapPost("", Verify);
    }

    private IResult Issue()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        lock (_gate)
        {
            foreach (var expired in _challenges.Where(item => item.Value.ExpiresAt <= now).Select(item => item.Key).ToArray())
                _challenges.Remove(expired);

            if (_challenges.Count >= 128) return Results.StatusCode(StatusCodes.Status429TooManyRequests);

            var code = RandomNumberGenerator.GetInt32(10000, 100000).ToString(CultureInfo.InvariantCulture);
            _challenges.Add(id, new(code, now.AddMinutes(2)));
        }

        return Results.Content(Html.Page($$"""
            <h1>Laboratório de CAPTCHA</h1>
            <p>Leia o código da imagem para consultar uma vaga fictícia.</p>
            <form method="post" action="/lab/captcha">
              <input type="hidden" name="challenge" value="{{id}}">
              <img src="/lab/captcha/{{id}}/image" alt="Desafio visual com cinco dígitos" width="204" height="72">
              <label for="answer">Código</label>
              <input id="answer" name="answer" inputmode="numeric" pattern="[0-9]{5}" required>
              <button type="submit">Ver vaga fictícia</button>
            </form>
            """), "text/html; charset=utf-8");
    }

    private IResult Image(Guid id)
    {
        Challenge? challenge;
        lock (_gate) _challenges.TryGetValue(id, out challenge);
        return challenge is not null && challenge.ExpiresAt > DateTimeOffset.UtcNow
            ? Results.File(CaptchaImage.Render(challenge.Code), "image/bmp")
            : Results.NotFound();
    }

    private async Task<IResult> Verify(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType) return Results.BadRequest();
        var form = await request.ReadFormAsync(ct);
        if (!Guid.TryParse(form["challenge"], out var id)) return Results.BadRequest();

        lock (_gate)
        {
            // Uma tentativa por desafio, inclusive quando a resposta está errada.
            if (!_challenges.Remove(id, out var challenge) || challenge.ExpiresAt <= DateTimeOffset.UtcNow
                || form["answer"] != challenge.Code)
                return Results.BadRequest(new { error = "Código incorreto, expirado ou já usado. Abra um desafio novo." });
        }

        return Results.Ok(new { jobs = new[] { new { id = "lab-1", title = "Backend Engineer (.NET)", company = "Empresa fictícia" } } });
    }
}
