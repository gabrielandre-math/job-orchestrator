using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>A deployable private test form. External widgets use their real verification endpoints; local images have no alphabet oracle.</summary>
internal sealed class ControlledCaptchaServer(ControlledCaptchaSetup setup, HttpClient http, TimeProvider time)
{
    private readonly ConcurrentDictionary<Guid, Challenge> challenges = new();
    private sealed class Challenge(string answer, DateTimeOffset expires)
    {
        public string Answer = answer;
        public DateTimeOffset Expires = expires;
        public string? Receipt;
        public readonly SemaphoreSlim Gate = new(1);
    }

    public void Map(WebApplication app)
    {
        app.MapGet("/controlled-test", Page);
        app.MapGet("/controlled-test/image/{id:guid}", (Guid id) =>
        {
            if (!challenges.TryGetValue(id, out var c) || c.Expires <= time.GetUtcNow()) return Results.NotFound();
            var svg = $"""<svg xmlns="http://www.w3.org/2000/svg" width="250" height="80"><rect width="250" height="80" fill="white"/><path d="M0 20 L250 65 M0 65 L250 15" stroke="#ddd"/><text x="22" y="56" font-family="monospace" font-size="46" letter-spacing="5" fill="#111">{c.Answer}</text></svg>""";
            return Results.Content(svg, "image/svg+xml");
        });
        app.MapPost("/controlled-test/submit", SubmitAsync);
    }

    private IResult Page(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        foreach (var c in challenges.Where(c => c.Value.Expires <= time.GetUtcNow()).ToArray()) challenges.TryRemove(c.Key, out _);
        if (challenges.Count >= 128) return Results.StatusCode(429);
        var id = Guid.NewGuid();
        var left = RandomNumberGenerator.GetInt32(10, 100); var right = RandomNumberGenerator.GetInt32(1, 10);
        var answer = setup.Provider == "math" ? (left + right).ToString(CultureInfo.InvariantCulture) : RandomNumberGenerator.GetInt32(10000, 100000).ToString(CultureInfo.InvariantCulture);
        challenges[id] = new(answer, time.GetUtcNow().AddMinutes(5));
        var widget = setup.Provider switch
        {
            "turnstile" => $"""<script src="https://challenges.cloudflare.com/turnstile/v0/api.js" async defer></script><div class="cf-turnstile" data-sitekey="{E(setup.SiteKey)}" data-action="job_orchestrator_test"></div>""",
            "recaptcha-v2" => $"""<script src="https://www.google.com/recaptcha/api.js" async defer></script><div class="g-recaptcha" data-sitekey="{E(setup.SiteKey)}"></div>""",
            "hcaptcha" => $"""<script src="https://js.hcaptcha.com/1/api.js" async defer></script><div class="h-captcha" data-sitekey="{E(setup.SiteKey)}"></div>""",
            "image" => $"""<img data-jo-captcha-image src="/controlled-test/image/{id}" alt="Digite os cinco dígitos da imagem" width="250" height="80"><label>Resposta <input name="answer" data-jo-captcha-answer autocomplete="off" required></label>""",
            _ => $"""<p data-jo-captcha-expression>{left} + {right}</p><label>Resposta <input name="answer" data-jo-captcha-answer autocomplete="off" required></label>"""
        };
        return Results.Content(Document($"""
            <h1>Teste controlado de CAPTCHA</h1><p>Provedor: {E(setup.Provider)}. Modo: {(setup.TestKeys ? "chaves oficiais de teste" : "validação real no servidor")}.</p>
            <form method="post" action="/controlled-test/submit" data-jo-test-form data-jo-provider="{E(setup.Provider)}">
              <input type="hidden" name="testId" value="{id}">
              <label>Nome de teste <input name="name" data-jo-test-field required value="Teste Job Orchestrator"></label>
              <label>E-mail de teste <input type="email" name="email" data-jo-test-field required value="candidate@example.test"></label>
              {widget}<button type="submit">Enviar teste</button>
            </form><p>Este formulário não envia e-mails nem candidaturas a terceiros. Guarda apenas o recibo temporário em memória.</p>
            """), "text/html; charset=utf-8");
    }

    private async Task<IResult> SubmitAsync(HttpRequest request, HttpResponse response, CancellationToken ct)
    {
        response.Headers.CacheControl = "no-store";
        if (!request.HasFormContentType || request.ContentLength > 32768) return Error("Formulário inválido.");
        var form = await request.ReadFormAsync(ct);
        if (!Guid.TryParse(form["testId"], out var id) || !challenges.TryGetValue(id, out var challenge)) return Error("Teste não encontrado. Abra uma sessão nova.");
        await challenge.Gate.WaitAsync(ct);
        try
        {
            if (challenge.Receipt is { } receipt) return Receipt(receipt);
            if (challenge.Expires <= time.GetUtcNow()) return Error("O desafio expirou. Abra um teste novo.");
            if (string.IsNullOrWhiteSpace(form["name"]) || string.IsNullOrWhiteSpace(form["email"])) return Error("Nome e e-mail de teste são obrigatórios.");
            bool success;
            if (setup.Provider is "image" or "math") success = form["answer"] == challenge.Answer;
            else
            {
                var field = setup.Provider == "turnstile" ? "cf-turnstile-response" : setup.Provider == "hcaptcha" ? "h-captcha-response" : "g-recaptcha-response";
                var result = await ControlledCaptchaVerifier.VerifyAsync(http, setup, form[field].ToString(), ct);
                success = result.Success;
            }
            if (!success) return Error("O servidor rejeitou a resposta: incorreta, expirada ou não validada pelo provedor.");
            challenge.Receipt = "JO-" + Guid.NewGuid().ToString("N");
            return Receipt(challenge.Receipt);
        }
        finally { challenge.Gate.Release(); }
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
    private static string Document(string body) => "<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><meta name=\"job-orchestrator-controlled\" content=\"v1\"><title>Job Orchestrator — servidor privado</title><style>body{max-width:680px;margin:40px auto;padding:20px;font:16px system-ui}form,label{display:grid;gap:12px}form{gap:20px}input,button{padding:10px;font:inherit}img{max-width:100%}</style></head><body>" + body + "</body></html>";
    private static IResult Error(string detail) => Results.Content(Document($"<h1>Teste rejeitado</h1><p data-jo-error>{E(detail)}</p>"), "text/html; charset=utf-8", statusCode: 400);
    private static IResult Receipt(string receipt) => Results.Content(Document($"<h1>CAPTCHA validado e teste recebido</h1><p data-jo-receipt=\"{receipt}\">Recibo: {receipt}</p>"), "text/html; charset=utf-8");
}

internal sealed record ControlledCaptchaSetup(string Provider, string SiteKey, string Secret, bool TestKeys, string? Hostname)
{
    public static ControlledCaptchaSetup FromEnvironment()
    {
        var provider = Environment.GetEnvironmentVariable("JO_CONTROLLED_CAPTCHA") ?? "math";
        if (provider is not ("math" or "image" or "turnstile" or "recaptcha-v2" or "hcaptcha")) throw new InvalidOperationException("JO_CONTROLLED_CAPTCHA: use math, image, turnstile, recaptcha-v2 ou hcaptcha.");
        var test = Environment.GetEnvironmentVariable("JO_CAPTCHA_TEST_KEYS") == "1";
        var site = Environment.GetEnvironmentVariable("JO_CAPTCHA_SITEKEY") ?? "";
        var secret = Environment.GetEnvironmentVariable("JO_CAPTCHA_SECRET") ?? "";
        if (test)
        {
            (site, secret) = provider switch
            {
                "turnstile" => ("1x00000000000000000000AA", "1x0000000000000000000000000000000AA"),
                "recaptcha-v2" => ("6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI", "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe"),
                "hcaptcha" => ("10000000-ffff-ffff-ffff-000000000001", "0x0000000000000000000000000000000000000000"),
                _ => ("", "")
            };
        }
        var hostname = Environment.GetEnvironmentVariable("JO_CAPTCHA_HOSTNAME");
        if (provider is not ("math" or "image") && (site.Length == 0 || secret.Length == 0 || !test && string.IsNullOrWhiteSpace(hostname)))
            throw new InvalidOperationException("Configure JO_CAPTCHA_SITEKEY, JO_CAPTCHA_SECRET e JO_CAPTCHA_HOSTNAME para o seu domínio, ou JO_CAPTCHA_TEST_KEYS=1 para chaves oficiais de teste.");
        return new(provider, site, secret, test, hostname);
    }
}

internal sealed record CaptchaVerified(bool Success, string? Error);
internal static class ControlledCaptchaVerifier
{
    public static async Task<CaptchaVerified> VerifyAsync(HttpClient http, ControlledCaptchaSetup setup, string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 8192) return new(false, "missing-token");
        var endpoint = setup.Provider switch
        {
            "turnstile" => "https://challenges.cloudflare.com/turnstile/v0/siteverify",
            "hcaptcha" => "https://api.hcaptcha.com/siteverify",
            "recaptcha-v2" => "https://www.google.com/recaptcha/api/siteverify",
            _ => throw new InvalidOperationException("Provedor não suportado.")
        };
        try
        {
            using var response = await http.PostAsync(endpoint, new FormUrlEncodedContent(new Dictionary<string, string> { ["secret"] = setup.Secret, ["response"] = token }), ct);
            if (!response.IsSuccessStatusCode) return new(false, "provider-http-error");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = json.RootElement;
            if (!data.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True) return new(false, "rejected");
            if (!setup.TestKeys)
            {
                if (!data.TryGetProperty("hostname", out var host) || host.ValueKind != JsonValueKind.String || !string.Equals(host.GetString(), setup.Hostname, StringComparison.OrdinalIgnoreCase)) return new(false, "hostname-mismatch");
                if (setup.Provider == "turnstile" && (!data.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String || action.GetString() != "job_orchestrator_test")) return new(false, "action-mismatch");
            }
            return new(true, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException) { return new(false, "provider-unavailable"); }
    }
}
