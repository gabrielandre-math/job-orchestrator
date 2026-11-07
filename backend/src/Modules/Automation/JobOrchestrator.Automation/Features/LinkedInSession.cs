using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Features;

internal sealed record LinkedInSessionSettings(bool Enabled = false, string Location = "Brasil", bool RemoteOnly = true,
    int MaxJobs = 10, int MaxDetails = 5, int IntervalMinutes = 60, int DailySearches = 12);
internal sealed record LinkedInSessionView(LinkedInSessionSettings Settings, string State, string? Detail, DateTimeOffset? LastSearch,
    DateTimeOffset? NextSearch, int LastJobs, bool BrowserOpen);
internal sealed record LinkedInSessionSearch(string Query);
internal sealed record LinkedInSessionJob(string Url, string Title, string? Company, string? Location, string? Description,
    string? Salary, string? PostedAt, string? ExternalApplyUrl, bool Closed = false);
internal sealed record LinkedInSessionPage(string PageUrl, DateTimeOffset CapturedAt, List<LinkedInSessionJob> Jobs);

/// <summary>The API owns one isolated local browser. The worker calls it over loopback, never opening the same Chromium profile twice.</summary>
internal sealed class LinkedInSession(IOptions<AutomationOptions> options, TimeProvider time, ILogger<LinkedInSession> log) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string SettingsPath => Path.Combine(AppPaths.Config, "linkedin-session.json");
    private static string StatePath => Path.Combine(AppPaths.Data, "linkedin-session-state.json");
    internal static string ProfilePath => Path.Combine(AppPaths.Data, "linkedin-browser");
    private static readonly string CaptureScript = LoadScript();
    private readonly SemaphoreSlim gate = new(1);
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<DiscoveredPosting> Jobs)> cache = new(StringComparer.OrdinalIgnoreCase);
    private IPlaywright? playwright;
    private IBrowserContext? browser;
    private IPage? page;
    private string state = "Disconnected";
    private string? detail;
    private int lastJobs;
    private DateTimeOffset? lastSearch, nextSearch;
    private int searches;
    private DateOnly day;
    private CancellationTokenSource? activeSearch;

    public static LinkedInSessionSettings Settings()
    {
        try { return File.Exists(SettingsPath) ? JsonSerializer.Deserialize<LinkedInSessionSettings>(File.ReadAllText(SettingsPath), Json) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException) { return new(); }
    }
    public async Task<LinkedInSessionView> ConfigureAsync(LinkedInSessionSettings value, CancellationToken ct)
    {
        if (value.Location is null || value.Location.Length > 120 || value.MaxJobs is < 1 or > 20 || value.MaxDetails < 0 || value.MaxDetails > value.MaxJobs
            || value.IntervalMinutes is < 15 or > 1440 || value.DailySearches is < 1 or > 24) throw new InvalidOperationException("Use 1–20 vagas, detalhes até o limite de vagas, intervalo de 15–1440 minutos e 1–24 buscas por dia.");
        if (!value.Enabled) CancelSearch();
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(AppPaths.Config);
            var temp = SettingsPath + ".tmp"; await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(value, Json), ct); File.Move(temp, SettingsPath, true);
            cache.Clear();
            if (!value.Enabled) { await CloseBrowserAsync(); state = "Disabled"; detail = "Busca do LinkedIn desligada."; }
            return View();
        }
        finally { gate.Release(); }
    }
    public async Task<LinkedInSessionView> OpenAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!Settings().Enabled) throw new InvalidOperationException("Ative a busca do LinkedIn antes de conectar sua conta.");
            await EnsureBrowserAsync();
            if (page!.Url == "about:blank") await page.GotoAsync("https://www.linkedin.com/login", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.BringToFrontAsync(); await StatusOfPageAsync(); return View();
        }
        finally { gate.Release(); }
    }
    public async Task<LinkedInSessionView> StatusAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { var previousDetail = detail; if (page is { IsClosed: false } && await StatusOfPageAsync()) detail ??= previousDetail; return View(); }
        finally { gate.Release(); }
    }
    private LinkedInSessionView View() => new(Settings(), state, detail, lastSearch, nextSearch, lastJobs, browser is not null && page is { IsClosed: false });

    private async Task EnsureBrowserAsync()
    {
        if (browser is not null && page is { IsClosed: false }) return;
        await CloseBrowserAsync();
        Directory.CreateDirectory(ProfilePath);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ProfilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        playwright = await Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchPersistentContextAsync(ProfilePath, new BrowserTypeLaunchPersistentContextOptions
        {
            Headless = false, ExecutablePath = options.Value.Browser.ExecutablePath, AcceptDownloads = false,
            Locale = "pt-BR", Args = ["--disable-sync", "--no-first-run"]
        });
        browser.SetDefaultTimeout(15000);
        page = browser.Pages.FirstOrDefault() ?? await browser.NewPageAsync();
        // No third-party browsing. LinkedIn's own identity/static hosts are needed for the normal login and job pages.
        await browser.RouteAsync("**/*", async route =>
        {
            if (route.Request.ResourceType is "media" or "font") { await route.AbortAsync(); return; }
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var u)) { await route.AbortAsync(); return; }
            if (u.Scheme == "data" || u.Scheme == "https" && (Domain(u.Host, "linkedin.com") || Domain(u.Host, "licdn.com") || Domain(u.Host, "arkoselabs.com") || Domain(u.Host, "funcaptcha.com")
                || u.Host is "accounts.google.com" or "appleid.apple.com")) await route.ContinueAsync();
            else await route.AbortAsync();
        });
        LoadState();
    }
    private static bool Domain(string host, string domain) => host.Equals(domain, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
    private async Task<bool> StatusOfPageAsync()
    {
        if (page is null || page.IsClosed) { state = "Disconnected"; return false; }
        var url = new Uri(page.Url);
        if (!Domain(url.Host, "linkedin.com") || url.AbsolutePath.Contains("login", StringComparison.OrdinalIgnoreCase) || url.AbsolutePath.Contains("authwall", StringComparison.OrdinalIgnoreCase)
            || url.AbsolutePath.Contains("checkpoint", StringComparison.OrdinalIgnoreCase))
        { state = "LoginRequired"; detail = "Conclua login ou verificação na janela do LinkedIn e clique em Conferir sessão."; return false; }
        var challenges = page.Locator("iframe[src*='captcha'], iframe[src*='arkoselabs'], [data-captcha], input[autocomplete='one-time-code']");
        for (var i = 0; i < await challenges.CountAsync(); i++)
            if (await challenges.Nth(i).IsVisibleAsync())
            { state = "VerificationRequired"; detail = "O LinkedIn pediu verificação. A busca está pausada; conclua na mesma janela e confira a sessão."; return false; }
        if (browser is null || !HasAuthentication(await browser.CookiesAsync(["https://www.linkedin.com"]), time.GetUtcNow()))
        { state = "LoginRequired"; detail = "A página pública abriu, mas não há sessão autenticada local. Entre na sua conta na mesma janela e confira a sessão."; return false; }
        state = "Ready"; detail = null; return true;
    }

    internal static bool HasAuthentication(IEnumerable<BrowserContextCookiesResult> cookies, DateTimeOffset now) =>
        cookies.Any(c => c.Name == "li_at" && !string.IsNullOrWhiteSpace(c.Value)
            && (c.Domain.TrimStart('.').Equals("linkedin.com", StringComparison.OrdinalIgnoreCase)
                || c.Domain.TrimStart('.').Equals("www.linkedin.com", StringComparison.OrdinalIgnoreCase))
            && (c.Expires == -1 || c.Expires > now.ToUnixTimeSeconds()));

    public async Task<IReadOnlyList<DiscoveredPosting>> SearchAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 160 || query.Any(char.IsControl)) throw new InvalidOperationException("Informe um termo de busca de até 160 caracteres.");
        await gate.WaitAsync(ct);
        using var running = CancellationTokenSource.CreateLinkedTokenSource(ct);
        activeSearch = running; ct = running.Token;
        try
        {
            var settings = Settings();
            if (!settings.Enabled) throw new InvalidOperationException("A busca pela sua sessão do LinkedIn está desligada. Conecte em Testes e busca pessoal.");
            await EnsureBrowserAsync();
            if (page!.Url == "about:blank") await page.GotoAsync("https://www.linkedin.com/feed/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            if (!await StatusOfPageAsync()) throw new InvalidOperationException(detail);
            var now = time.GetUtcNow();
            if (cache.TryGetValue(query, out var cached) && now - cached.At < TimeSpan.FromMinutes(settings.IntervalMinutes))
            { detail = "Resultado da última leitura; aguardando o intervalo configurado."; return cached.Jobs; }
            if (day != DateOnly.FromDateTime(now.UtcDateTime)) { day = DateOnly.FromDateTime(now.UtcDateTime); searches = 0; }
            if (searches >= settings.DailySearches || nextSearch > now)
            { state = "WaitingInterval"; detail = "Aguardando intervalo ou limite de buscas do dia. Nenhuma nova página foi consultada."; return []; }
            searches++; lastSearch = now; lastJobs = 0; nextSearch = now.AddMinutes(1); SaveState();
            var url = SearchUrl(query, settings);
            var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(ct);
            if (response?.Status == 429)
            { state = "Paused"; nextSearch = now.AddHours(1); detail = "O LinkedIn limitou as consultas (429). Aguarde antes de retomar."; SaveState(); throw new InvalidOperationException(detail); }
            if (!await StatusOfPageAsync()) throw new InvalidOperationException(detail);
            try { await page.Locator("a[href*='/jobs/view/']").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 }); }
            catch (TimeoutException) { /* A genuine empty result and a changed layout are distinguished below. */ }
            var listing = await ReadAsync();
            if (listing.Jobs.Count == 0)
            {
                if (!await StatusOfPageAsync()) throw new InvalidOperationException(detail);
                var body = await page.Locator("body").InnerTextAsync();
                if (!Regex.IsMatch(body, "no matching jobs|no results found|nenhuma vaga|nenhum resultado|não encontramos", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("A página abriu, mas nenhum cartão foi reconhecido. Confira a sessão e o layout; isso não foi registrado como busca bem-sucedida.");
            }
            var jobs = listing.Jobs.Take(settings.MaxJobs).ToList();
            for (var i = 0; i < Math.Min(jobs.Count, settings.MaxDetails); i++)
            {
                ct.ThrowIfCancellationRequested(); await Task.Delay(1500, ct);
                await page.GotoAsync(jobs[i].Url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }).WaitAsync(ct);
                if (!await StatusOfPageAsync()) break;
                try { await page.Locator(".jobs-description-content__text, .jobs-description__content, .show-more-less-html__markup").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 }); }
                catch (TimeoutException) { /* Preserve the card and explicitly leave its missing description empty. */ }
                var more = page.Locator("button.jobs-description__footer-button, button.show-more-less-html__button--more").First;
                if (await more.CountAsync() > 0 && await more.IsVisibleAsync())
                    try { await more.ClickAsync(new LocatorClickOptions { Timeout = 2000 }); } catch (PlaywrightException) { }
                var full = (await ReadAsync()).Jobs.FirstOrDefault(j => j.Url == jobs[i].Url);
                if (full is not null) jobs[i] = full with { Company = string.IsNullOrWhiteSpace(full.Company) ? jobs[i].Company : full.Company, Location = string.IsNullOrWhiteSpace(full.Location) ? jobs[i].Location : full.Location };
            }
            var result = jobs.Select(j => Posting(query, j, now)).ToArray();
            cache[query] = (now, result); lastJobs = result.Length;
            if (state == "Ready") detail = $"{result.Length} vagas lidas na sessão; {jobs.Count(j => !string.IsNullOrWhiteSpace(j.Description))} com descrição.";
            SaveState(); return result;
        }
        catch (OperationCanceledException) { state = "Paused"; detail = "A busca foi interrompida. Nenhuma leitura adicional foi iniciada."; throw new InvalidOperationException(detail); }
        catch (InvalidOperationException ex) { if (state is not ("LoginRequired" or "VerificationRequired" or "Paused")) state = "ReadError"; detail = ex.Message; throw; }
        catch (PlaywrightException ex) { state = "BrowserError"; detail = "O navegador do LinkedIn não concluiu a leitura. Reabra a sessão para continuar."; log.LogWarning("LinkedIn session browser error: {Type}", ex.GetType().Name); throw new InvalidOperationException(detail); }
        finally { activeSearch = null; gate.Release(); }
    }
    internal static string SearchUrl(string query, LinkedInSessionSettings settings) => "https://www.linkedin.com/jobs/search/?keywords=" + Uri.EscapeDataString(query)
        + "&location=" + Uri.EscapeDataString(settings.Location) + "&f_TPR=r86400&sortBy=DD" + (settings.RemoteOnly ? "&f_WT=2" : "");
    private async Task<LinkedInSessionPage> ReadAsync()
    {
        await page!.EvaluateAsync(CaptureScript);
        var json = await page.EvaluateAsync<string>("() => JSON.stringify(globalThis.__joCaptureLinkedIn())");
        return JsonSerializer.Deserialize<LinkedInSessionPage>(json, Json) ?? throw new InvalidOperationException("A página não retornou vagas reconhecíveis.");
    }
    internal static DiscoveredPosting Posting(string query, LinkedInSessionJob job, DateTimeOffset at)
    {
        var url = new Uri(job.Url); var id = url.AbsolutePath.Trim('/').Split('/').Last();
        var description = PostingIntegrity.Inspect(job.Description ?? "").Text;
        var contact = PublishedContactExtractor.From(description);
        var applyUrl = ExternalAts(job.ExternalApplyUrl) ?? job.Url;
        var raw = JsonSerializer.Serialize(new { kind = "linkedin.session", capturedAt = at, url = job.Url, externalApplyUrl = job.ExternalApplyUrl, job, contact }, Json);
        return new("linkedin-session:" + query, id, string.IsNullOrWhiteSpace(job.Company) ? "Empresa a confirmar" : job.Company, job.Title, job.Location ?? "", applyUrl, description, at.UtcDateTime, raw,
            // One submission channel: an explicitly published supported ATS takes priority. Contact evidence remains in the snapshot.
            Compensation: job.Salary, ApplyEmail: applyUrl == job.Url ? contact.Email : null, ListedAsOpen: false, Tombstone: job.Closed);
    }
    internal static string? ExternalAts(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https" && u.UserInfo.Length == 0 && u.IsDefaultPort
        && new[] { "boards.greenhouse.io", "job-boards.greenhouse.io", "jobs.lever.co", "jobs.ashbyhq.com", "jobs.smartrecruiters.com", "apply.workable.com", "jobs.workable.com" }.Contains(u.Host, StringComparer.OrdinalIgnoreCase) ? u.AbsoluteUri : null;
    private void SaveState()
    {
        Directory.CreateDirectory(AppPaths.Data);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new SavedState(day, searches, lastSearch, nextSearch), Json)); File.Move(temp, StatePath, true);
    }
    private void LoadState()
    {
        try { if (File.Exists(StatePath) && JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StatePath), Json) is { } s) { day = s.Day; searches = s.Searches; lastSearch = s.LastSearch; nextSearch = s.NextSearch; } }
        catch (Exception ex) when (ex is IOException or JsonException) { }
    }
    private sealed record SavedState(DateOnly Day, int Searches, DateTimeOffset? LastSearch, DateTimeOffset? NextSearch);
    private static string LoadScript()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("JobOrchestrator.LinkedInCapture.js")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public async Task DisconnectAsync(CancellationToken ct)
    {
        CancelSearch();
        await gate.WaitAsync(ct);
        try
        {
            await CloseBrowserAsync(); cache.Clear();
            Directory.CreateDirectory(AppPaths.Config);
            await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(Settings() with { Enabled = false }, Json), ct);
            if (Directory.Exists(ProfilePath)) Directory.Delete(ProfilePath, true);
            state = "Disconnected"; detail = "Sessão local apagada e busca desligada.";
        }
        finally { gate.Release(); }
    }
    private async Task CloseBrowserAsync() { if (browser is not null) try { await browser.CloseAsync(); } finally { browser = null; page = null; } playwright?.Dispose(); playwright = null; }
    private void CancelSearch() { try { activeSearch?.Cancel(); } catch (ObjectDisposedException) { } }
    public async ValueTask DisposeAsync() { await CloseBrowserAsync(); }
}

internal sealed class LinkedInSessionEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/automation/linkedin-session").WithTags("LinkedIn session");
        g.MapGet("", async (LinkedInSession session, CancellationToken ct) => await Run(() => session.StatusAsync(ct)));
        g.MapPut("", async (LinkedInSessionSettings settings, LinkedInSession session, CancellationToken ct) => await Run(() => session.ConfigureAsync(settings, ct)));
        g.MapPost("open", async (LinkedInSession session, CancellationToken ct) => await Run(() => session.OpenAsync(ct)));
        g.MapDelete("", async (LinkedInSession session, CancellationToken ct) => { await session.DisconnectAsync(ct); return Results.NoContent(); });
        g.MapPost("search", async (LinkedInSessionSearch search, LinkedInSession session, CancellationToken ct) => await Run(() => session.SearchAsync(search.Query, ct)));
    }
    private static async Task<IResult> Run<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (Exception ex) when (ex is InvalidOperationException or PlaywrightException or TimeoutException) { return Results.Problem(ex.Message, statusCode: 409); }
    }
}

/// <summary>Normal discovery entry point: API owns the browser, worker consumes its normalized postings through loopback.</summary>
internal sealed class LinkedInSessionSource(IHttpClientFactory http, IConfiguration config) : ISourceConnector
{
    public string Id => "linkedin-session";
    public string Version => "1.0.0";
    public string Name => "LinkedIn: minha sessão local";
    public string Kind => "Sessão local autorizada";
    public double Risk => 0.5;
    public IReadOnlyList<Capability> Capabilities => [Capability.DiscoverJobs, Capability.CheckPostingOpen];
    public IReadOnlyList<string> Domains => ["www.linkedin.com"];
    public double Fit(Uri url) => 0; // Open checks must not cause an extra authenticated navigation for every collected card.
    public bool Accepts(string source) => source.StartsWith("linkedin-session:", StringComparison.OrdinalIgnoreCase);
    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        if (!LinkedInSession.Settings().Enabled) throw new InvalidOperationException("Conecte sua sessão local do LinkedIn em Testes e busca pessoal.");
        var api = config["LinkedIn:ApiBase"] ?? "http://127.0.0.1:7410";
        if (!Uri.TryCreate(api, UriKind.Absolute, out var u) || u.Scheme != "http" || u.Host != "127.0.0.1" || u.UserInfo.Length > 0) throw new InvalidOperationException("LinkedIn:ApiBase deve apontar para a API local em http://127.0.0.1:porta.");
        using var response = await http.CreateClient("linkedin-session").PostAsJsonAsync(api.TrimEnd('/') + "/api/automation/linkedin-session/search", new LinkedInSessionSearch(source["linkedin-session:".Length..]), ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            try { using var body = JsonDocument.Parse(error); error = body.RootElement.GetProperty("detail").GetString() ?? "Sessão indisponível."; } catch (JsonException) { error = "A API local não concluiu a busca do LinkedIn."; }
            throw new InvalidOperationException(error);
        }
        return await response.Content.ReadFromJsonAsync<List<DiscoveredPosting>>(cancellationToken: ct) ?? [];
    }
    public Task<PostingStatus> CheckOpenAsync(string url, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Coletada pela sessão; abertura será conferida ao abrir a candidatura."));
}
