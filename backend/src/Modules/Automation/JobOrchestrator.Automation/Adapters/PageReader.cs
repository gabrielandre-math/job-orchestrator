using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using JobOrchestrator.Intelligence.Contracts;
using Microsoft.Playwright;

namespace JobOrchestrator.Automation.Adapters;

internal sealed record RenderedPage(string Url, string Title, string Text, IReadOnlyList<string> Links, IReadOnlyList<string> JsonLd, IReadOnlyList<string> Steps);

/// <summary>
/// Navegador de leitura para página de carreira que só monta com JavaScript. Motor: o Obscura na versão normal (sem --stealth, com
/// --obey-robots) quando está em ~/.local/opt/obscura, senão o Chromium do app. Antes de cada página o robots.txt é conferido; o
/// navegador se apresenta como robô, sem cookie guardado, sem login e sem preencher nada; 401, 403 ou 429 encerra (não contornamos).
/// Quando a lista de vagas fica atrás de um botão, o Llama local escolhe entre os rótulos da página, até 3 cliques, nunca em formulário.
/// </summary>
internal sealed partial class PageReader(IOptions<AutomationOptions> options, IHttpClientFactory http, IIntelligenceApi ai, IConfiguration config, ILogger<PageReader> log) : IAsyncDisposable
{
    private const string Bot = "JobOrchestratorBot/1.0 (uso pessoal; procura vagas em paginas de carreira)";
    private readonly Dictionary<string, string?> _robots = [];
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Process? _obscura;

    public string Engine { get; private set; } = "Chromium";

    public static string ObscuraBinary(IConfiguration c) => c["Scraper:Obscura:Path"] is { Length: > 0 } p ? p
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "opt", "obscura", "obscura");

    public async Task<RenderedPage?> ReadAsync(string url, AgentThread thread, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !await RobotsAllowAsync(uri, ct)) return null;
        IBrowserContext context;
        try { context = await ContextAsync(ct); }
        catch (PlaywrightException ex) when (Engine == "Obscura")
        {
            // O motor leve não abriu o contexto: o Chromium assume desta vez em diante.
            log.LogInformation("Obscura context failed, falling back to Chromium: {Error}", ex.Message);
            await CloseAsync();
            context = await ContextAsync(ct, chromium: true);
        }
        await using var _ = context;
        var page = await context.NewPageAsync();
        try
        {
            var response = await page.GotoAsync(uri.ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 25_000 });
            if (response is { Status: 401 or 403 or 429 }) return null;
        }
        catch (TimeoutException) { /* página que nunca fica quieta: lê o que já montou */ }
        var steps = new List<string>();
        var clicked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var snap = await SnapAsync(page);
        for (var i = 0; i < 3 && snap is not null && !Useful(snap); i++)
        {
            var pick = await PickAsync(snap, clicked, thread, ct);
            if (pick is not { } choice) break;
            if (Uri.TryCreate(choice.Href, UriKind.Absolute, out var next) && !await RobotsAllowAsync(next, ct)) break;
            try
            {
                await page.Locator($"[data-jo-ref=\"{choice.I}\"]").ClickAsync(new LocatorClickOptions { Timeout = 5000 });
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = 10_000 });
            }
            catch (Exception ex) when (ex is TimeoutException or PlaywrightException) { /* clique sem efeito: fica com o que já tem */ }
            steps.Add(choice.Text);
            clicked.Add($"{choice.Text}|{choice.Href}");
            snap = await SnapAsync(page) ?? snap;
        }
        if (snap is null) return null;
        // O texto principal (SmartReader, a leitura do Readability em .NET) tira menu, rodapé e banner antes do Extrator; sem leitura boa, fica o texto inteiro.
        var text = snap.Text;
        try
        {
            var article = SmartReader.Reader.ParseArticle(page.Url, await page.ContentAsync());
            if (article.IsReadable && article.TextContent is { Length: >= 300 } main) text = main.Trim();
        }
        catch (Exception ex) when (ex is PlaywrightException or InvalidOperationException or ArgumentException) { /* fica o texto inteiro */ }
        return new RenderedPage(page.Url, snap.Title, text, snap.Links, snap.JsonLd, steps);
    }

    private async Task<IBrowserContext> ContextAsync(CancellationToken ct, bool chromium = false)
    {
        var browser = await BrowserAsync(chromium, ct);
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = $"Mozilla/5.0 (X11; Linux x86_64) {Engine}/{browser.Version} {Bot}", Locale = "pt-BR", AcceptDownloads = false,
        });
        context.SetDefaultTimeout(20_000);
        // Rede privada nunca; imagem, vídeo e fonte não fazem falta para ler vaga.
        await context.RouteAsync("**/*", async route =>
        {
            if (route.Request.ResourceType is "image" or "media" or "font" || !Public(route.Request.Url)) await route.AbortAsync("blockedbyclient");
            else await route.ContinueAsync();
        });
        return context;
    }

    private async Task<IBrowser> BrowserAsync(bool chromium, CancellationToken ct)
    {
        if (_browser is { IsConnected: true }) return _browser;
        _playwright ??= await Playwright.CreateAsync();
        if (!chromium && File.Exists(ObscuraBinary(config)) && config.GetValue("Scraper:Obscura:Enabled", true))
        {
            try
            {
                var port = config.GetValue("Scraper:Obscura:Port", 9444);
                // A versão normal, sempre sem --stealth: o navegador se apresenta como é. O robots.txt também fica ligado no próprio motor.
                var info = new ProcessStartInfo(ObscuraBinary(config)) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "serve", "--port", port.ToString(CultureInfo.InvariantCulture), "--obey-robots" }) info.ArgumentList.Add(a);
                _obscura = Process.Start(info);
                if (_obscura is not null)
                {
                    _obscura.OutputDataReceived += (_, _) => { };
                    _obscura.ErrorDataReceived += (_, _) => { };
                    _obscura.BeginOutputReadLine();
                    _obscura.BeginErrorReadLine();
                }
                for (var i = 0; i < 25 && _browser is not { IsConnected: true }; i++)
                {
                    try { _browser = await _playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}"); }
                    catch (PlaywrightException) { await Task.Delay(200, ct); }
                }
                if (_browser is { IsConnected: true }) { Engine = "Obscura"; return _browser; }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { log.LogInformation("Obscura did not start: {Error}", ex.Message); }
            StopObscura();
        }
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true, ExecutablePath = options.Value.Browser.ExecutablePath, Args = ["--disable-extensions", "--disable-sync", "--no-first-run"],
        });
        Engine = "Chromium";
        return _browser;
    }

    private sealed record Item(int I, string Text, string Href);
    private sealed record Snap(string Title, string Text, IReadOnlyList<Item> Items, IReadOnlyList<string> Links, IReadOnlyList<string> JsonLd);

    // Marca cada link e botão visível com um número (como o snapshot do agent-browser) e junta links, iframes e scripts, onde moram os quadros de ATS.
    private const string SnapScript = """
        () => {
          const visible = (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
          const items = [...document.querySelectorAll('a[href], button, [role=button], [role=link], [role=tab]')].filter(visible).slice(0, 300).map((e, i) => {
            e.setAttribute('data-jo-ref', String(i));
            const form = !!e.closest('form') || e.getAttribute('type') === 'submit';
            return { i, text: (e.innerText || e.getAttribute('aria-label') || e.title || '').trim().replace(/\s+/g, ' ').slice(0, 80), href: e.href || '', form };
          }).filter((x) => x.text.length > 1 && !x.form);
          const links = [...document.querySelectorAll('a[href]')].map((a) => a.href)
            .concat([...document.querySelectorAll('iframe[src], script[src], [data-src], [data-url]')].map((x) => x.src || x.getAttribute('data-src') || x.getAttribute('data-url') || ''))
            .filter(Boolean);
          const jsonld = [...document.querySelectorAll('script[type="application/ld+json"]')].map((s) => s.textContent || '');
          return { title: document.title || '', text: ((document.body && document.body.innerText) || '').slice(0, 20000), items, links: [...new Set(links)].slice(0, 2000), jsonld };
        }
        """;

    private static async Task<Snap?> SnapAsync(IPage page)
    {
        try
        {
            var e = await page.EvaluateAsync<JsonElement>(SnapScript);
            return new Snap(e.Str("title"), e.Str("text"),
                [.. e.GetProperty("items").EnumerateArray().Select(x => new Item(x.GetProperty("i").GetInt32(), x.Str("text"), x.Str("href")))],
                [.. e.GetProperty("links").EnumerateArray().Select(x => x.GetString() ?? "")], [.. e.GetProperty("jsonld").EnumerateArray().Select(x => x.GetString() ?? "")]);
        }
        catch (Exception ex) when (ex is PlaywrightException or JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    private static bool Useful(Snap s) =>
        s.Links.Any(l => SourceKeys.Board(l) is not null) || s.JsonLd.Any(j => j.Contains("JobPosting", StringComparison.Ordinal)) || JobMarks().Matches(s.Text).Count >= 4;

    /// <summary>Qual link ou botão leva à lista de vagas: um só candidato vai direto; mais de um, o Llama local escolhe pelos rótulos.</summary>
    private async Task<Item?> PickAsync(Snap s, HashSet<string> clicked, AgentThread thread, CancellationToken ct)
    {
        // O que já foi clicado sai da lista: o mesmo botão duas vezes não leva a lugar novo.
        var options = s.Items.Where(x => (Nav().IsMatch(x.Text) || Nav().IsMatch(x.Href)) && !Avoid().IsMatch(x.Text) && !Avoid().IsMatch(x.Href) && !clicked.Contains($"{x.Text}|{x.Href}"))
            .DistinctBy(x => $"{x.Text}|{x.Href}").Take(25).ToList();
        if (options.Count <= 1) return options.FirstOrDefault();
        var list = string.Join("\n", options.Select(o => $"[{o.I}] {o.Text}{(o.Href.Length > 0 ? $" ({o.Href[..Math.Min(o.Href.Length, 90)]})" : "")}"));
        var res = await ai.CompleteAsync(LlmRequest.For(LlmTasks.PageNavigate, NavSystem, $"Página: {s.Title}\nOpções:\n{list}", NavSchema, timeoutSeconds: 45, thread: thread, agent: "Navegador"), ct);
        if (res.Ok && res.Json is { } json)
        {
            try
            {
                using var d = JsonDocument.Parse(json);
                var index = d.RootElement.GetProperty("index").GetInt32();
                if (index < 0) return null;
                if (options.FirstOrDefault(o => o.I == index) is { } chosen) return chosen;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { /* fica a regra abaixo */ }
        }
        return options.FirstOrDefault(o => Strong().IsMatch(o.Text)) ?? options[0];
    }

    private const string NavSystem = "Você ajuda a achar a lista de vagas abertas num site de carreiras. Escolha a opção que leva a ela (ver vagas, todas as vagas, " +
        "oportunidades, carregar mais, próxima página). Nunca escolha login, cadastro, candidatar-se, enviar currículo ou rede social. Se nenhuma servir, index = -1. Responda só o JSON.";
    private const string NavSchema = """{"type":"object","properties":{"index":{"type":"integer"}},"required":["index"],"additionalProperties":false}""";

    /// <summary>Vagas em dado estruturado (JSON-LD JobPosting) que a página só monta com JavaScript.</summary>
    internal static IEnumerable<DiscoveredPosting> JobPostings(string source, string pageUrl, string json)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(json).RootElement.Clone(); }
        catch (JsonException) { yield break; }
        foreach (var j in Flatten(root).Where(IsJob))
        {
            var title = j.Str("title");
            if (title.Length < 3) continue;
            var org = j.TryGetProperty("hiringOrganization", out var o) ? o.ValueKind == JsonValueKind.Object ? o.Str("name") : o.ValueKind == JsonValueKind.String ? o.GetString() ?? "" : "" : "";
            var url = j.Str("url") is { Length: > 0 } u ? u : pageUrl;
            var remote = j.Str("jobLocationType").Equals("TELECOMMUTE", StringComparison.OrdinalIgnoreCase);
            var employment = j.TryGetProperty("employmentType", out var e) ? e.ValueKind == JsonValueKind.Array ? string.Join(", ", e.EnumerateArray().Select(x => x.GetString())) : e.GetString() : null;
            yield return new DiscoveredPosting(source, ScrapyRunner.Hash(url + title), org.Length > 0 ? org : ScrapyRunner.Host(pageUrl), title, Place(j), url, Json.Plain(j.Str("description")),
                j.Date("datePosted"), $"{{\"page\":{JsonSerializer.Serialize(pageUrl)},\"jsonld\":{j.GetRawText()}}}", Workplace: remote ? "remote" : null,
                Employment: employment is { Length: > 0 } ? employment : null, ListedAsOpen: true, DescriptionMarkup: j.Str("description"), ValidThrough: j.Date("validThrough"));
        }
    }

    private static IEnumerable<JsonElement> Flatten(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) foreach (var y in Flatten(x)) yield return y; yield break; }
        if (e.ValueKind != JsonValueKind.Object) yield break;
        yield return e;
        if (e.TryGetProperty("@graph", out var g)) foreach (var y in Flatten(g)) yield return y;
    }

    private static bool IsJob(JsonElement e) => e.TryGetProperty("@type", out var t) &&
        (t.ValueKind == JsonValueKind.String ? t.GetString() == "JobPosting" : t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(x => x.GetString() == "JobPosting"));

    private static string Place(JsonElement j)
    {
        if (!j.TryGetProperty("jobLocation", out var loc)) return "";
        var first = loc.ValueKind == JsonValueKind.Array ? loc.EnumerateArray().FirstOrDefault() : loc;
        if (first.ValueKind != JsonValueKind.Object || !first.TryGetProperty("address", out var a)) return "";
        if (a.ValueKind == JsonValueKind.String) return a.GetString() ?? "";
        var country = a.TryGetProperty("addressCountry", out var c) ? c.ValueKind == JsonValueKind.Object ? c.Str("name") : c.GetString() ?? "" : "";
        return string.Join(", ", new[] { a.Str("addressLocality"), a.Str("addressRegion"), country }.Where(x => x.Length > 0));
    }

    /// <summary>Se o robots.txt do site deixa ler este endereço (o Firecrawl e o navegador perguntam antes).</summary>
    public Task<bool> AllowedAsync(string url, CancellationToken ct) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? RobotsAllowAsync(u, ct) : Task.FromResult(false);

    private async Task<bool> RobotsAllowAsync(Uri uri, CancellationToken ct)
    {
        var key = uri.GetLeftPart(UriPartial.Authority);
        if (!_robots.TryGetValue(key, out var text))
        {
            try
            {
                using var r = await http.CreateClient("sources").GetAsync(new Uri(uri, "/robots.txt"), ct);
                text = r.IsSuccessStatusCode ? await r.Content.ReadAsStringAsync(ct) : null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { text = null; }
            _robots[key] = text;
        }
        return text is null || CareerPageReader.Allowed(text, uri.AbsolutePath);
    }

    private static bool Public(string url)
    {
        if (url.StartsWith("data:", StringComparison.Ordinal) || url == "about:blank") return true;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || u.IsLoopback) return false;
        if (!IPAddress.TryParse(u.Host, out var ip)) return !u.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        var b = ip.MapToIPv4().GetAddressBytes();
        return !(b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127);
    }

    private async Task CloseAsync()
    {
        if (_browser is not null) { try { await _browser.CloseAsync(); } catch (PlaywrightException) { } _browser = null; }
        StopObscura();
    }

    private void StopObscura()
    {
        try { if (_obscura is { HasExited: false }) _obscura.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _obscura?.Dispose();
        _obscura = null;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _playwright?.Dispose();
    }

    [GeneratedRegex(@"requisitos|responsabilidades|qualifica[cç][oõ]es|requirements|responsibilities|candidatar|candidate-se|apply now", RegexOptions.IgnoreCase)] private static partial Regex JobMarks();
    [GeneratedRegex(@"vaga|carreira|trabalhe|oportunidade|\bjobs?\b|careers?|positions|openings|join|ver mais|ver todas|carregar mais|load more|show more|próxima|next|see all", RegexOptions.IgnoreCase)] private static partial Regex Nav();
    [GeneratedRegex(@"login|entrar|sign ?in|cadastr|candidatar|\bapply\b|enviar curr|linkedin|instagram|facebook|twitter|youtube|whatsapp|blog|privacidade|privacy", RegexOptions.IgnoreCase)] private static partial Regex Avoid();
    [GeneratedRegex(@"ver (todas )?(as )?vagas|todas as vagas|vagas abertas|nossas vagas|open positions|see all jobs|view all jobs|carregar mais|load more", RegexOptions.IgnoreCase)] private static partial Regex Strong();
}
