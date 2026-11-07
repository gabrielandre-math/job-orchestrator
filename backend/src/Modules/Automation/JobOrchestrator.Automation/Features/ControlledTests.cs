using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Features;

internal sealed record ControlledTestSettings(bool Enabled, string[] Origins, bool Headless = false, int LifetimeMinutes = 10);
internal sealed record ControlledStart(string Url, bool OwnServer, Dictionary<string, string>? Fields = null);
internal sealed record ControlledSubmit(string PayloadHash);
internal sealed record ControlledView(Guid Id, string Url, string State, string Provider, string? PayloadHash, string? Receipt,
    string? Detail, DateTimeOffset ExpiresAt, bool OcrAvailable);

/// <summary>Private-server tests use their own form contract and exact origins. Tokens and cookies stay in the leased page.</summary>
internal sealed partial class ControlledTests(BrowserPool browsers, TimeProvider time, ILogger<ControlledTests> log) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string SettingsPath => Path.Combine(AppPaths.Config, "controlled-tests.json");
    private readonly ConcurrentDictionary<Guid, Session> sessions = new();
    private readonly SemaphoreSlim starts = new(1);
    private Timer? cleanup;
    internal static readonly string[] ProviderHosts = ["challenges.cloudflare.com", "www.google.com", "www.recaptcha.net", "www.gstatic.com", "hcaptcha.com"];
    private sealed class Session(Guid id, string url, BrowserLease lease, IPage page, DateTimeOffset expires)
    {
        public readonly SemaphoreSlim Gate = new(1);
        public Guid Id = id;
        public string Url = url, State = "Waiting", Provider = "unknown";
        public string? Hash, Receipt, Detail;
        public BrowserLease Lease = lease;
        public IPage Page = page;
        public DateTimeOffset Expires = expires;
        public bool Closed, OcrTried;
    }

    public ControlledTestSettings Settings()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<ControlledTestSettings>(File.ReadAllText(SettingsPath), Json) ?? new(false, []) : new(false, []); }
        catch (Exception ex) when (ex is IOException or JsonException) { return new(false, []); }
    }

    internal static string? Origin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var u) || u.UserInfo.Length > 0 || u.Fragment.Length > 0 || u.Query.Length > 0 || u.AbsolutePath != "/") return null;
        if (u.Host.Equals("linkedin.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".linkedin.com", StringComparison.OrdinalIgnoreCase)) return null;
        var local = u.Host == "localhost" || IPAddress.TryParse(u.Host, out var ip) && (IPAddress.IsLoopback(ip) || Private(ip));
        return u.Scheme == "https" || u.Scheme == "http" && local ? u.GetLeftPart(UriPartial.Authority) : null;
    }

    internal static bool Allows(ControlledTestSettings settings, string value) => settings.Enabled && Uri.TryCreate(value, UriKind.Absolute, out var u)
        && u.UserInfo.Length == 0 && settings.Origins.Any(s => Origin(s) is { } origin && origin == u.GetLeftPart(UriPartial.Authority));

    private static bool Private(IPAddress address)
    {
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal;
        var b = address.GetAddressBytes(); return b[0] == 10 || b[0] == 192 && b[1] == 168 || b[0] == 172 && b[1] is >= 16 and <= 31;
    }

    public async Task ConfigureAsync(ControlledTestSettings settings)
    {
        if (settings.Origins is null || settings.Origins.Length > 5 || settings.Origins.Any(x => Origin(x) is null)) throw new InvalidOperationException("Informe até cinco origens exatas do seu servidor (https://dominio:porta), sem caminho. HTTP é aceito só em rede local.");
        if (settings.LifetimeMinutes is < 1 or > 30) throw new InvalidOperationException("A sessão deve durar de 1 a 30 minutos.");
        await starts.WaitAsync();
        try
        {
            foreach (var id in sessions.Keys) await CloseAsync(id);
            Directory.CreateDirectory(AppPaths.Config);
            var temp = SettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(settings with { Origins = settings.Origins.Select(x => Origin(x)!).Distinct().ToArray() }, Json));
            File.Move(temp, SettingsPath, true);
        }
        finally { starts.Release(); }
    }

    public async Task<ControlledView> StartAsync(ControlledStart input, CancellationToken ct)
    {
        var settings = Settings();
        if (!input.OwnServer || !Allows(settings, input.Url)) throw new InvalidOperationException("Ative os testes, cadastre a origem exata e confirme que este é o seu servidor.");
        if (input.Fields is { Count: > 20 } || input.Fields?.Any(x => x.Key.Length > 100 || x.Value is null || x.Value.Length > 2000) == true) throw new InvalidOperationException("Campos de teste grandes demais.");
        await starts.WaitAsync(ct);
        BrowserLease? lease = null;
        Guid? created = null;
        try
        {
            await ExpireAsync();
            if (sessions.Values.Count(s => !s.Closed) >= 1) throw new InvalidOperationException("Feche a sessão de teste anterior antes de abrir outra.");
            var uri = new Uri(input.Url);
            lease = await browsers.LeaseAsync([uri.Authority, .. ProviderHosts], ct, challengeResources: true,
                navigationOrigin: uri.GetLeftPart(UriPartial.Authority), headless: settings.Headless);
            var page = await lease.Context.NewPageAsync();
            await page.GotoAsync(input.Url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            if (await page.Locator("meta[name='job-orchestrator-controlled'][content='v1']").CountAsync() != 1
                || await page.Locator("form[data-jo-test-form]").CountAsync() != 1) throw new InvalidOperationException("A página não publica o contrato de teste controlado v1. Use o servidor de referência ou adicione o contrato ao seu formulário.");
            await ValidateFormAsync(page, input.Url);
            foreach (var (name, value) in input.Fields ?? new() { ["name"] = "Teste Job Orchestrator", ["email"] = "candidate@example.test" })
            {
                var fields = page.Locator("form[data-jo-test-form] [data-jo-test-field]");
                for (var i = 0; i < await fields.CountAsync(); i++)
                {
                    var field = fields.Nth(i);
                    if (await field.GetAttributeAsync("name") == name) await field.FillAsync(value);
                }
            }
            var session = new Session(Guid.CreateVersion7(), input.Url, lease, page, time.GetUtcNow().AddMinutes(settings.LifetimeMinutes));
            sessions[session.Id] = session; created = session.Id; lease = null;
            cleanup ??= new Timer(_ => _ = CleanupAsync(), null, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20));
            return await InspectAsync(session.Id, ct);
        }
        catch { if (created is { } id) await CloseAsync(id); throw; }
        finally { if (lease is not null) await lease.DisposeAsync(); starts.Release(); }
    }

    public async Task<ControlledView> InspectAsync(Guid id, CancellationToken ct)
    {
        var s = Find(id); await s.Gate.WaitAsync(ct);
        try
        {
            if (s.Closed || s.State is "Confirmed" or "Unknown" or "Rejected") return View(s);
            if (time.GetUtcNow() >= s.Expires) { await DisposeSessionAsync(s, "Expired"); return View(s); }
            await InspectPageAsync(s, ct); return View(s);
        }
        finally { s.Gate.Release(); }
    }

    private async Task InspectPageAsync(Session s, CancellationToken ct)
    {
        var type = await s.Page.Locator("form[data-jo-test-form]").GetAttributeAsync("data-jo-provider");
        s.Provider = type ?? "unknown";
        if (s.Provider == "math")
        {
            var expression = await s.Page.Locator("[data-jo-captcha-expression]").TextContentAsync();
            if (MathAnswer(expression) is { } answer) await s.Page.Locator("[data-jo-captcha-answer]").FillAsync(answer);
        }
        if (s.Provider == "image" && !s.OcrTried)
        {
            s.OcrTried = true;
            if (OcrPath() is { } executable)
            {
                var image = await s.Page.Locator("[data-jo-captcha-image]").ScreenshotAsync();
                var alphabet = await s.Page.Locator("[data-jo-captcha-image]").GetAttributeAsync("data-jo-captcha-alphabet") ?? "0123456789";
                if (!Regex.IsMatch(alphabet, "^[A-Z0-9]{2,36}$")) alphabet = "0123456789";
                try
                {
                    var answer = await OcrAsync(executable, image, alphabet, ct);
                    if (answer is not null) await s.Page.Locator("[data-jo-captcha-answer]").FillAsync(answer);
                    else s.Detail = "O OCR não reconheceu uma resposta. Digite a resposta na janela de teste.";
                }
                catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or OperationCanceledException)
                { s.Detail = "O OCR local não concluiu a leitura. Você pode digitar a resposta na janela de teste."; }
            }
            else s.Detail = "Tesseract não encontrado. Instale-o para testar OCR local ou digite a resposta na janela.";
        }
        // Observe only. Official widgets generate their own tokens; the app never fabricates them.
        var ready = await s.Page.Locator("form[data-jo-test-form]").EvaluateAsync<bool>("""
            form => {
              const p = form.dataset.joProvider;
              const name = p === 'turnstile' ? 'cf-turnstile-response' : p === 'hcaptcha' ? 'h-captcha-response' : p === 'recaptcha-v2' ? 'g-recaptcha-response' : null;
              const value = name ? form.querySelector('[name="' + name + '"]')?.value : form.querySelector('[data-jo-captcha-answer]')?.value;
              return typeof value === 'string' && value.trim().length > 0;
            }
            """);
        s.State = ready ? "Ready" : "Waiting";
        s.Hash = ready ? await HashAsync(s) : null;
    }

    public async Task<ControlledView> SubmitAsync(Guid id, ControlledSubmit input, CancellationToken ct)
    {
        var s = Find(id); await s.Gate.WaitAsync(ct);
        try
        {
            if (s.Closed || s.State is "Confirmed" or "Unknown" or "Rejected") return View(s);
            if (!Allows(Settings(), s.Url) || time.GetUtcNow() >= s.Expires) { await DisposeSessionAsync(s, "Expired"); return View(s); }
            await ValidateFormAsync(s.Page, s.Url);
            await InspectPageAsync(s, ct);
            if (s.State != "Ready" || input.PayloadHash != s.Hash) throw new InvalidOperationException("A resposta não está pronta ou os campos mudaram. Atualize e confira o teste antes de enviar.");
            s.State = "Submitting";
            try
            {
                await s.Page.Locator("form[data-jo-test-form]").EvaluateAsync("form => form.requestSubmit()");
                await s.Page.Locator("[data-jo-receipt], [data-jo-error]").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
                if (await s.Page.Locator("[data-jo-receipt]").CountAsync() > 0)
                {
                    s.Receipt = await s.Page.Locator("[data-jo-receipt]").First.GetAttributeAsync("data-jo-receipt");
                    s.State = string.IsNullOrWhiteSpace(s.Receipt) ? "Unknown" : "Confirmed";
                    s.Detail = "O servidor retornou um recibo após validar o CAPTCHA.";
                }
                else { s.State = "Rejected"; var error = await s.Page.Locator("[data-jo-error]").First.InnerTextAsync(); s.Detail = error[..Math.Min(500, error.Length)]; }
            }
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException or OperationCanceledException)
            {
                s.State = "Unknown"; s.Detail = "O envio começou, mas não houve recibo verificável. Confira no servidor; esta sessão não será reenviada.";
            }
            return View(s);
        }
        finally { s.Gate.Release(); }
    }

    private static async Task<string> HashAsync(Session s)
    {
        var fields = await s.Page.Locator("form[data-jo-test-form]").EvaluateAsync<string>("form => JSON.stringify({ url: location.href, action: form.action, method: form.method, fields: [...form.querySelectorAll('[data-jo-test-field]')].map(f => [f.name, f.value]) })");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s.Url + "\n" + fields)));
    }
    private static async Task ValidateFormAsync(IPage page, string target)
    {
        var form = page.Locator("form[data-jo-test-form]");
        var action = await form.EvaluateAsync<string>("form => form.action");
        var method = await form.EvaluateAsync<string>("form => form.method");
        if (method != "post" || !Uri.TryCreate(action, UriKind.Absolute, out var destination)
            || destination.GetLeftPart(UriPartial.Authority) != new Uri(target).GetLeftPart(UriPartial.Authority))
            throw new InvalidOperationException("O contrato exige formulário POST para a mesma origem do seu servidor.");
    }

    internal static string? MathAnswer(string? expression)
    {
        var match = Arithmetic().Match(expression ?? "");
        if (!match.Success) return null;
        var left = int.Parse(match.Groups[1].Value); var right = int.Parse(match.Groups[3].Value);
        return (match.Groups[2].Value == "+" ? left + right : left - right).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    [GeneratedRegex(@"^\s*([0-9]{1,3})\s*([+-])\s*([0-9]{1,3})\s*$")] private static partial Regex Arithmetic();
    private static string? OcrPath() => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract")).FirstOrDefault(File.Exists);
    internal static async Task<string?> OcrAsync(string executable, byte[] image, string alphabet, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false } };
        foreach (var argument in new[] { "stdin", "stdout", "--psm", "7", "-c", "tessedit_char_whitelist=" + alphabet }) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token); var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardInput.BaseStream.WriteAsync(image, timeout.Token); process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token); var text = Regex.Replace(await output, @"\s+", ""); await error;
            return process.ExitCode == 0 && OcrAnswer().IsMatch(text) ? text : null;
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
    [GeneratedRegex(@"^[A-Z0-9]{4,8}$")] private static partial Regex OcrAnswer();
    private Session Find(Guid id) => sessions.TryGetValue(id, out var s) ? s : throw new KeyNotFoundException("Sessão não encontrada. Abra um teste novo.");
    private static ControlledView View(Session s) => new(s.Id, s.Url, s.State, s.Provider, s.Hash, s.Receipt, s.Detail, s.Expires, OcrPath() is not null);
    public async Task CloseAsync(Guid id)
    {
        if (!sessions.TryGetValue(id, out var s)) return;
        await s.Gate.WaitAsync();
        try { await DisposeSessionAsync(s, "Closed"); }
        finally { s.Gate.Release(); }
        sessions.TryRemove(id, out _);
    }
    private static async Task DisposeSessionAsync(Session s, string state)
    {
        if (s.Closed) return;
        s.Closed = true; s.State = state; s.Hash = null;
        await s.Lease.DisposeAsync();
    }
    private async Task ExpireAsync() { foreach (var s in sessions.Values.Where(s => time.GetUtcNow() >= s.Expires).ToArray()) await CloseAsync(s.Id); }
    private async Task CleanupAsync() { try { await ExpireAsync(); } catch (Exception ex) { log.LogWarning("Controlled test cleanup: {Error}", ex.Message); } }
    public async ValueTask DisposeAsync() { if (cleanup is not null) await cleanup.DisposeAsync(); foreach (var id in sessions.Keys) await CloseAsync(id); }
}

internal sealed class ControlledTestEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/automation/controlled-tests").WithTags("Controlled tests");
        group.MapGet("settings", (ControlledTests tests) => Results.Ok(tests.Settings()));
        group.MapPut("settings", async (ControlledTestSettings settings, ControlledTests tests) => await Run(async () => { await tests.ConfigureAsync(settings); return tests.Settings(); }));
        group.MapPost("sessions", async (ControlledStart input, ControlledTests tests, CancellationToken ct) => await Run(() => tests.StartAsync(input, ct)));
        group.MapGet("sessions/{id:guid}", async (Guid id, ControlledTests tests, CancellationToken ct) => await Run(() => tests.InspectAsync(id, ct)));
        group.MapPost("sessions/{id:guid}/submit", async (Guid id, ControlledSubmit input, ControlledTests tests, CancellationToken ct) => await Run(() => tests.SubmitAsync(id, input, ct)));
        group.MapDelete("sessions/{id:guid}", async (Guid id, ControlledTests tests) => { await tests.CloseAsync(id); return Results.NoContent(); });
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (KeyNotFoundException ex) { return Results.Problem(ex.Message, statusCode: 404); }
        catch (Exception ex) when (ex is InvalidOperationException or PlaywrightException or TimeoutException) { return Results.Problem(ex.Message, statusCode: 400); }
    }
}
