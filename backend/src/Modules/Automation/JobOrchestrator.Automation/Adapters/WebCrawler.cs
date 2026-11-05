using System.Net.Http.Json;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JobOrchestrator.Intelligence.Contracts;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Onde está o coletor: o ambiente virtual com o Scrapy (bin/ no Linux e no macOS, Scripts\ no Windows) e o projeto com os nossos
/// spiders. O instalador copia o projeto para a pasta de dados, que é gravável; o que vem com o app fica como está (no macOS, dentro
/// do pacote assinado).
/// </summary>
internal sealed class ScraperEnv(IConfiguration config)
{
    public string Venv => config["Scraper:Venv"] is { Length: > 0 } v ? v : DefaultVenv();
    public string Project => config["Scraper:Project"] is { Length: > 0 } p ? p
        : Directory.Exists(Path.Combine(AppPaths.Data, "scraper")) ? Path.Combine(AppPaths.Data, "scraper") : Path.Combine(AppPaths.Resources, "scraper");
    public string Scrapy => ScrapyIn(Venv);

    public static string ScrapyIn(string venv) => OperatingSystem.IsWindows() ? Path.Combine(venv, "Scripts", "scrapy.exe") : Path.Combine(venv, "bin", "scrapy");

    /// <summary>O ambiente da pasta de dados; no Linux, se só existir o do caminho XDG (modo de desenvolvimento com JO_DATA_DIR), ele.</summary>
    private static string DefaultVenv()
    {
        var own = Path.Combine(AppPaths.Data, "scraper-venv");
        if (File.Exists(ScrapyIn(own)) || !OperatingSystem.IsLinux()) return own;
        var xdg = Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Platform.Home, ".local", "share"), "job-orchestrator", "scraper-venv");
        return File.Exists(ScrapyIn(xdg)) ? xdg : own;
    }
    public bool Installed => File.Exists(Scrapy) && Directory.Exists(Project);
    public string Cache => AppPaths.In(AppPaths.Data, "scraper-cache");
}

/// <summary>O que uma varredura achou, para a tela e para o fio dos agentes.</summary>
internal sealed record CrawlSummary(int Pages, int FromJsonLd, int FromAi, int Rejected, int Boards, int Emails, int Blocked, IReadOnlyList<string> BoardSources, string? Error, double Seconds, IReadOnlyList<BoardHit>? Hits = null);

/// <summary>Quadro de ATS achado na varredura e a página onde estava o link.</summary>
internal sealed record BoardHit(string Source, string Page);

/// <summary>
/// Executor do Scrapy (por baixo dos panos): roda o spider careers com robots.txt obedecido, AutoThrottle e uma conexão por domínio,
/// lê cada item em JSON Lines conforme sai e conta a varredura no fio "crawl" (Semeador, Robots, Coletor, Extrator, Conferente).
/// Página com cara de vaga e sem dado estruturado vai para a IA (degrau da tarefa page.jobs: o Llama local por padrão);
/// o Conferente só aceita a vaga se o título estiver no trecho citado e o trecho estiver na página.
/// </summary>
internal sealed partial class ScrapyRunner(ScraperEnv env, IIntelligenceApi ai, IConfiguration config, IHttpClientFactory http, PageReader reader, ILogger<ScrapyRunner> log)
{
    private sealed record Candidate(string Url, string Title, string Text, DateTime? Posted = null, string? Salary = null);

    /// <summary>Firecrawl auto-hospedado (opcional, AGPL, serviço à parte): renderiza a página e devolve o texto em markdown. Sem modo furtivo nem proxy.</summary>
    private async Task<Candidate?> RenderAsync(string baseUrl, string url, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var res = await http.CreateClient("sources").PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/v1/scrape", new { url, formats = new[] { "markdown" }, onlyMainContent = true, timeout = 45000 }, timeout.Token);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(timeout.Token));
            var data = doc.RootElement.GetProperty("data");
            var text = data.Str("markdown");
            var title = data.TryGetProperty("metadata", out var m) ? m.Str("title") : "";
            return text.Length > 200 ? new Candidate(url, title, text.Length > 20000 ? text[..20000] : text) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { log.LogInformation("Firecrawl skipped for {Url}: {Error}", url, ex.Message); return null; }
    }
    private sealed record Email(string Address, string Url, string Context);

    private static (string? Url, DateTime At) _firecrawl;

    /// <summary>O Firecrawl configurado ou, sem configuração, o que estiver no ar em 127.0.0.1:3002 (scripts/firecrawl.sh); conferido a cada 5 minutos.</summary>
    private async Task<string?> FirecrawlAsync(CancellationToken ct)
    {
        if (config["Scraper:Firecrawl:Url"] is { Length: > 0 } configured) return configured;
        if (DateTime.UtcNow - _firecrawl.At < TimeSpan.FromMinutes(5)) return _firecrawl.Url;
        string? found = null;
        try
        {
            using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var r = await probe.GetAsync("http://127.0.0.1:3002/", ct);
            found = "http://127.0.0.1:3002";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* Firecrawl fora do ar: fica o navegador de leitura */ }
        _firecrawl = (found, DateTime.UtcNow);
        return found;
    }

    public async Task<(List<DiscoveredPosting> Postings, CrawlSummary Summary)> CrawlAsync(string source, IReadOnlyList<string> seeds, AgentThread thread, CancellationToken ct, int? pageBudget = null, int? aiPages = null)
    {
        var clock = Stopwatch.StartNew();
        if (!env.Installed)
        {
            await ai.NoteAsync(thread, "Semeador", "O coletor ainda não está instalado", "Instale pela tela Varredura (um clique): cria o ambiente do Scrapy na sua pasta.", "Você", false, ct);
            return ([], new CrawlSummary(0, 0, 0, 0, 0, 0, 0, [], "Coletor não instalado", 0));
        }
        var maxPages = pageBudget ?? config.GetValue("Scraper:MaxPages", 60);
        var maxSeconds = config.GetValue("Scraper:MaxSeconds", 240);
        await ai.NoteAsync(thread, "Semeador", $"{seeds.Count} {(seeds.Count == 1 ? "site" : "sites")} para varrer, até {maxPages} páginas",
            $"Sementes: {string.Join(", ", seeds)}.\nEntram também as rotas de carreira comuns (/carreiras, /vagas, /jobs) e o sitemap de cada site.", "Robots", true, ct);
        await ai.NoteAsync(thread, "Robots", "robots.txt obedecido em cada site", "Página proibida não é lida. Uma conexão por domínio, com o atraso ajustado sozinho (AutoThrottle). Sem disfarce de navegador.", "Coletor", true, ct);

        var info = new ProcessStartInfo(env.Scrapy) { WorkingDirectory = env.Project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "crawl", "careers", "-a", $"seeds={string.Join(',', seeds)}", "-o", "-:jsonlines" }) info.ArgumentList.Add(a);
        info.Environment.Clear();
        foreach (var key in Platform.BaseEnvironment) if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } v) info.Environment[key] = v;
        // O bytecode do Python vai para a pasta de dados: o projeto pode estar num lugar só de leitura (o pacote do macOS).
        info.Environment["PYTHONPYCACHEPREFIX"] = AppPaths.In(AppPaths.Data, "pycache");
        info.Environment["PYTHONUTF8"] = "1";
        info.Environment["JO_MAX_PAGES"] = maxPages.ToString(CultureInfo.InvariantCulture);
        info.Environment["JO_MAX_SECONDS"] = maxSeconds.ToString(CultureInfo.InvariantCulture);
        info.Environment["JO_SCRAPER_CACHE"] = env.Cache;
        info.Environment["JO_SCRAPER_LOG"] = "INFO";

        var postings = new List<DiscoveredPosting>();
        var candidates = new List<Candidate>();
        var emails = new List<Email>();
        var boards = new List<string>();
        var hits = new List<BoardHit>();
        var pages = 0;
        var stderr = new StringBuilder();
        using (var p = new Process { StartInfo = info })
        {
            p.ErrorDataReceived += (_, e) => { if (e.Data is { } line && stderr.Length < 200_000) stderr.AppendLine(line); };
            p.Start();
            p.BeginErrorReadLine();
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromSeconds(maxSeconds + 60));
            try
            {
                while (await p.StandardOutput.ReadLineAsync(limit.Token) is { } line)
                {
                    if (line.Length == 0) continue;
                    JsonElement item;
                    try { item = JsonDocument.Parse(line).RootElement.Clone(); } catch (JsonException) { continue; }
                    switch (item.Str("type"))
                    {
                        case "progress":
                            pages = item.TryGetProperty("pages", out var n) ? n.GetInt32() : pages;
                            if (pages % 20 == 0) await ai.NoteAsync(thread, "Coletor", $"{pages} páginas lidas", null, null, true, ct);
                            break;
                        case "posting": postings.Add(FromJsonLd(source, item)); break;
                        case "board": boards.Add(item.Str("source")); hits.Add(new BoardHit(item.Str("source"), item.Str("url"))); break;
                        case "email": emails.Add(new Email(item.Str("email"), item.Str("url"), item.Str("context"))); break;
                        case "candidate": candidates.Add(new Candidate(item.Str("url"), item.Str("title"), item.Str("text"), DateTime.TryParse(item.Str("posted"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var posted) ? posted : null, item.Str("salary") is { Length: > 0 } pay ? pay : null)); break;
                    }
                }
                await p.WaitForExitAsync(limit.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            }
        }
        var stats = stderr.ToString();
        pages = Math.Max(pages, Stat(stats, "response_received_count"));
        var blocked = Stat(stats, "robotstxt/forbidden") + Stat(stats, "downloader/response_status_count/403");
        await ai.NoteAsync(thread, "Coletor", $"{pages} páginas lidas em {clock.Elapsed.TotalSeconds:0} s",
            $"{postings.Count} vagas com dado estruturado (JSON-LD), {candidates.Count} páginas com cara de vaga sem esse dado, {boards.Distinct().Count()} quadros de ATS, {emails.Count} e-mails de candidatura."
            + (blocked > 0 ? $"\n{blocked} páginas não lidas: o robots.txt proíbe ou o site recusou (403). Não contornamos." : ""), "Extrator", true, ct);

        // Site que não deu nada no Scrapy (página que só monta com JavaScript): o Firecrawl, quando está no ar, lê primeiro; o navegador de
        // leitura fica com o que sobrar. Os dois conferem o robots.txt antes; o que o site recusa não é lido.
        bool Empty(string s) => !postings.Any(p => Leads.Under(Leads.PageOf(p), s)) && !candidates.Any(c => Leads.Under(c.Url, s)) && !hits.Any(h => Leads.Under(h.Page, s));
        if (seeds.Any(Empty) && await FirecrawlAsync(ct) is { } firecrawl)
            foreach (var seed in seeds.Where(Empty).Take(3).ToList())
            {
                var start = seed.StartsWith("http", StringComparison.Ordinal) ? seed : $"https://{seed}";
                if (!await reader.AllowedAsync(start, ct) || await RenderAsync(firecrawl, start, ct) is not { } page) continue;
                candidates.Add(page);
                await ai.NoteAsync(thread, "Coletor", $"Página com JavaScript lida pelo Firecrawl: {seed}", $"{page.Text.Length} caracteres para o Extrator.", "Extrator", true, ct);
            }

        // Site que não deu nada no Scrapy (página que só monta com JavaScript): o navegador de leitura abre, com o robots.txt conferido.
        // Quadro de ATS em link ou iframe, vaga em dado estruturado e o texto da página seguem o mesmo caminho do que o Scrapy leu.
        if (config.GetValue("Scraper:Browser:Enabled", true))
        {
            var empty = seeds.Where(Empty).Take(config.GetValue("Scraper:Browser:MaxSites", 4)).ToList();
            foreach (var seed in empty)
            {
                var start = seed.StartsWith("http", StringComparison.Ordinal) ? seed : $"https://{seed}";
                RenderedPage? page = null;
                try { page = await reader.ReadAsync(start, thread, ct); }
                catch (Exception ex) when (ex is Microsoft.Playwright.PlaywrightException or TimeoutException or InvalidOperationException) { log.LogInformation("Browser read failed for {Seed}: {Error}", seed, ex.Message); }
                if (page is null)
                {
                    await ai.NoteAsync(thread, "Navegador", $"{seed}: não abriu", "O robots.txt não deixa, o site recusou ou a página não respondeu a tempo. Não contornamos.", null, false, ct);
                    continue;
                }
                // Se o clique levou para o quadro de outra empresa ou plataforma, o achado continua contando para o site de onde saiu.
                var at = Leads.Under(page.Url, seed) ? page.Url : start;
                var found = page.Links.Append(page.Url).Select(SourceKeys.Board).OfType<string>().Distinct().ToList();
                foreach (var b in found) { boards.Add(b); hits.Add(new BoardHit(b, at)); }
                var structured = page.JsonLd.SelectMany(j => PageReader.JobPostings(source, at, j)).ToList();
                postings.AddRange(structured);
                if (structured.Count == 0 && found.Count == 0 && page.Text.Length > 200) candidates.Add(new Candidate(at, page.Title, page.Text));
                await ai.NoteAsync(thread, "Navegador", $"{seed}: lido pelo {reader.Engine}{(page.Steps.Count > 0 ? $" depois de {page.Steps.Count} {(page.Steps.Count == 1 ? "clique" : "cliques")}" : "")}",
                    $"{found.Count} {(found.Count == 1 ? "quadro de ATS" : "quadros de ATS")}, {structured.Count} vagas em dado estruturado{(structured.Count == 0 && found.Count == 0 ? ", o texto vai para o Extrator" : "")}." +
                    (page.Steps.Count > 0 ? $"\nCaminho: {string.Join(" > ", page.Steps)}" : ""), "Extrator", true, ct);
            }
        }

        // Extrator: a IA lê as páginas sem dado estruturado (até 8 por varredura) e o Conferente confere cada vaga contra a página.
        var fromAi = 0;
        var rejected = 0;
        // Página igual à da última leitura (mesmo texto) reaproveita a extração: a IA só lê o que mudou.
        var cache = ExtractCache.Load();
        var reused = 0;
        foreach (var c in candidates.Take(aiPages ?? config.GetValue("Scraper:MaxAiPages", 8)))
        {
            // Sem números ("há 2 minutos", contadores): a mesma lista de vagas dá a mesma chave mesmo com a página mudando o relógio.
            var key = Hash(Digits().Replace(c.Text.ToLowerInvariant(), "").ReplaceLineEndings(" "));
            string json;
            if (cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromDays(14)) { json = hit.Json; reused++; }
            else
            {
                var r = await ai.CompleteAsync(LlmRequest.For(LlmTasks.PageJobs, LocalTaskSpecs.PageJobsSystem, LocalTaskSpecs.PageJobsPrompt(c.Url, c.Title, c.Text),
                    LocalTaskSpecs.PageJobsSchema, timeoutSeconds: 240, thread: thread, agent: "Extrator"), ct);
                if (!r.Ok || r.Json is null) continue;
                json = r.Json;
                cache[key] = new ExtractCache.Entry(json, DateTime.UtcNow);
            }
            var jobs = Jobs(json).ToList();
            foreach (var job in jobs)
            {
                var page = Fold(c.Text);
                var quote = Fold(job.Quote);
                if (quote.Length < 6 || !page.Contains(quote, StringComparison.Ordinal) || !quote.Contains(Fold(job.Title), StringComparison.Ordinal)) { rejected++; continue; }
                // Rótulo de filtro, contagem por área e chamada para a lista não são vagas, mesmo escritos na página.
                if (NotAJob(job)) { rejected++; continue; }
                var email = job.ApplyEmail is { Length: > 3 } e && c.Text.Contains(e, StringComparison.OrdinalIgnoreCase) ? e : null;
                postings.Add(new DiscoveredPosting(source, Hash(c.Url + job.Title), job.Company is { Length: > 0 } co ? co : Host(c.Url), job.Title, job.Location,
                    Uri.TryCreate(job.ApplyUrl, UriKind.Absolute, out _) ? job.ApplyUrl : c.Url, job.Summary.Length > 0 ? job.Summary : c.Text[..Math.Min(c.Text.Length, 4000)],
                    // Data e salário escritos na página valem quando a página é de uma vaga só (página de vaga, não lista).
                    c.Posted, $"{{\"page\":{JsonSerializer.Serialize(c.Url)},\"extraction\":{json}}}", Workplace: job.Modality is "remote" or "hybrid" or "onsite" ? job.Modality : null,
                    Compensation: jobs.Count == 1 ? c.Salary : null, ApplyEmail: email, ListedAsOpen: true));
                fromAi++;
            }
        }

        // E-mail de candidatura publicado na página entra na vaga da mesma página.
        foreach (var group in emails.GroupBy(e => e.Url))
            for (var i = 0; i < postings.Count; i++)
                if (postings[i].ApplyEmail is null && (postings[i].ApplyUrl == group.Key || postings[i].RawJson.Contains(group.Key, StringComparison.Ordinal)))
                    postings[i] = postings[i] with { ApplyEmail = group.First().Address };

        // A mesma vaga pode aparecer em duas páginas do site: fica uma só.
        postings = [.. postings.GroupBy(p => (Claims: Fold(p.Title), p.ApplyUrl)).Select(g => g.First())];
        fromAi = Math.Min(fromAi, postings.Count);
        var jsonLd = postings.Count - fromAi;
        ExtractCache.Save(cache);
        await ai.NoteAsync(thread, "Conferente", $"{postings.Count} vagas aceitas ({jsonLd} do JSON-LD, {fromAi} pela IA){(rejected > 0 ? $"; {rejected} recusadas" : "")}{(reused > 0 ? $"; {reused} páginas sem mudança, sem IA de novo" : "")}",
            rejected > 0 ? "Recusada: o título ou o trecho citado não está escrito na página." : "Toda vaga aceita tem o título escrito na página.", "Analista", true, ct);
        log.LogInformation("Varredura {Source}: {Pages} páginas, {JsonLd} JSON-LD, {Ai} pela IA, {Boards} quadros", source, pages, jsonLd, fromAi, boards.Count);
        return (postings, new CrawlSummary(pages, jsonLd, fromAi, rejected, boards.Distinct().Count(), emails.Count, blocked, [.. boards.Distinct()], null, clock.Elapsed.TotalSeconds, hits));
    }

    private sealed record PageJob(string Title, string Company, string Location, string Modality, string Summary, string ApplyUrl, string ApplyEmail, string Quote);

    internal static bool NotAJob(string title, string location, string summary) =>
        CallToAction().IsMatch(title) || Counter().IsMatch(summary.Trim()) || Counter().IsMatch(title.Trim()) || Placeholder().IsMatch(location) || title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 14;
    private static bool NotAJob(PageJob j) => NotAJob(j.Title, j.Location, j.Summary);

    [GeneratedRegex(@"^\s*(to |see |view |visit |explore |browse |check |ver |veja |confira |conhe[cç]a |acesse |clique )", RegexOptions.IgnoreCase)] private static partial Regex CallToAction();
    [GeneratedRegex(@"^[\p{L}\s&/,.-]{2,60}\(\d+\)$")] private static partial Regex Counter();
    [GeneratedRegex(@"^\s*(select|selecione|choose|escolha|all locations|todas as localidades)", RegexOptions.IgnoreCase)] private static partial Regex Placeholder();

    private static IEnumerable<PageJob> Jobs(string json)
    {
        List<PageJob> list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            list = [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(j => new PageJob(j.Str("title"), j.Str("company"), j.Str("location"), j.Str("modality"),
                j.Str("summary"), j.Str("applyUrl"), j.Str("applyEmail"), j.Str("quote")))];
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return []; }
        return list.Where(j => j.Title.Length > 2);
    }

    private static DiscoveredPosting FromJsonLd(string source, JsonElement j) =>
        new(source, Hash(j.Str("key")), j.Str("company") is { Length: > 0 } c ? c : Host(j.Str("page")), j.Str("title"), j.Str("location"), j.Str("url"), j.Str("description"),
            DateTime.TryParse(j.Str("posted"), out var d) ? d.ToUniversalTime() : null, j.GetRawText(),
            Workplace: j.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True ? "remote" : null,
            Compensation: j.Str("salary") is { Length: > 0 } s ? s : null, Employment: j.Str("employment") is { Length: > 0 } e ? e : null, ListedAsOpen: true,
            AllowedCountries: j.TryGetProperty("allowed", out var a) && a.ValueKind == JsonValueKind.Array
                ? a.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).SelectMany(x => SourceCatalog.MatchCountries(x.GetString()!.Length == 2 ? Country(x.GetString()!) : x.GetString()!)).Distinct().ToList() is { Count: > 0 } list ? list : null
                : null);

    /// <summary>Código ISO de duas letras no JSON-LD vira o nome em inglês, para o mesmo casamento de país das outras fontes.</summary>
    private static string Country(string iso) { try { return new RegionInfo(iso).EnglishName; } catch (ArgumentException) { return iso; } }

    private static int Stat(string log, string name) =>
        Regex.Matches(log, $"'{Regex.Escape(name)}':\\s*(\\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Sum();

    internal static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24];
    internal static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? Json.Company(u.Host.Replace("www.", "").Split('.')[0]) : "";

    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
    [GeneratedRegex(@"[\d\s]+")] private static partial Regex Digits();
}

/// <summary>
/// Fonte "web:empresa.com" (várias separadas por vírgula): varre as páginas de carreira com o Scrapy. Quadro de ATS achado no caminho
/// (Greenhouse, Lever, Ashby, SmartRecruiters, Recruitee, Breezy, Personio) é lido pelo conector oficial dele na mesma rodada.
/// </summary>
internal sealed class WebSource(ScrapyRunner runner, IIntelligenceApi ai, IServiceProvider services, IHttpClientFactory http) : PublicSource(http, "web")
{
    public override string Id => "web-scrapy";
    public override string Name => "Varredura (Scrapy)";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var seeds = Key(source).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var thread = AgentThread.New("crawl", $"Varredura de {string.Join(", ", seeds)}");
        var (postings, summary) = await runner.CrawlAsync(source, seeds, thread, ct);
        var connectors = services.GetServices<ISourceConnector>().Where(c => c is not WebSource).ToList();
        var expanded = new List<string>();
        foreach (var board in summary.BoardSources.Take(6))
        {
            var connector = connectors.FirstOrDefault(c => c.Accepts(board));
            if (connector is null) continue;
            try
            {
                var found = await connector.DiscoverAsync(board, ct);
                postings.AddRange(found);
                expanded.Add($"{board}: {found.Count} vagas");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { expanded.Add($"{board}: não respondeu"); }
        }
        if (expanded.Count > 0) await ai.NoteAsync(thread, "Coletor", $"{expanded.Count} quadros de ATS lidos pelo conector oficial", string.Join("\n", expanded), "Analista", true, ct);
        return postings;
    }
}

/// <summary>Extrações da IA por texto de página (hash), por 14 dias: página que não mudou não volta para o modelo.</summary>
internal static class ExtractCache
{
    internal sealed record Entry(string Json, DateTime At);
    private static string File => Path.Combine(AppPaths.In(AppPaths.Data, "scraper"), "extractions.json");

    public static Dictionary<string, Entry> Load()
    {
        try { return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(System.IO.File.ReadAllText(File)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    public static void Save(Dictionary<string, Entry> cache) =>
        System.IO.File.WriteAllText(File, JsonSerializer.Serialize(cache.Where(c => DateTime.UtcNow - c.Value.At < TimeSpan.FromDays(14)).OrderByDescending(c => c.Value.At).Take(3000).ToDictionary()));
}

