using System.Net.Http.Json;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JobOrchestrator.Intelligence.Contracts;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>Onde está o coletor: o ambiente virtual com o Scrapy e o projeto com os nossos spiders.</summary>
internal sealed class ScraperEnv(IConfiguration config)
{
    public string Venv => config["Scraper:Venv"] is { Length: > 0 } v ? v
        : Path.Combine(Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"), "job-orchestrator", "scraper-venv");
    public string Project => config["Scraper:Project"] is { Length: > 0 } p ? p : Path.Combine(AppContext.BaseDirectory, "scraper");
    public string Scrapy => Path.Combine(Venv, "bin", "scrapy");
    public bool Installed => File.Exists(Scrapy) && Directory.Exists(Project);
    public string Cache => AppPaths.In(AppPaths.Data, "scraper-cache");
}

/// <summary>O que uma varredura achou, para a tela e para o fio dos agentes.</summary>
internal sealed record CrawlSummary(int Pages, int FromJsonLd, int FromAi, int Rejected, int Boards, int Emails, int Blocked, IReadOnlyList<string> BoardSources, string? Error, double Seconds);

/// <summary>
/// Executor do Scrapy (por baixo dos panos): roda o spider careers com robots.txt obedecido, AutoThrottle e uma conexão por domínio,
/// lê cada item em JSON Lines conforme sai e conta a varredura no fio "crawl" (Semeador, Robots, Coletor, Extrator, Conferente).
/// Página com cara de vaga e sem dado estruturado vai para a IA (degrau da tarefa page.jobs: o Llama local por padrão);
/// o Conferente só aceita a vaga se o título estiver no trecho citado e o trecho estiver na página.
/// </summary>
internal sealed partial class ScrapyRunner(ScraperEnv env, IIntelligenceApi ai, IConfiguration config, IHttpClientFactory http, ILogger<ScrapyRunner> log)
{
    private sealed record Candidate(string Url, string Title, string Text);

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

    public async Task<(List<DiscoveredPosting> Postings, CrawlSummary Summary)> CrawlAsync(string source, IReadOnlyList<string> seeds, AgentThread thread, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        if (!env.Installed)
        {
            await ai.NoteAsync(thread, "Semeador", "O coletor ainda não está instalado", "Instale pela tela Varredura (um clique): cria o ambiente do Scrapy na sua pasta.", "Você", false, ct);
            return ([], new CrawlSummary(0, 0, 0, 0, 0, 0, 0, [], "Coletor não instalado", 0));
        }
        var maxPages = config.GetValue("Scraper:MaxPages", 60);
        var maxSeconds = config.GetValue("Scraper:MaxSeconds", 240);
        await ai.NoteAsync(thread, "Semeador", $"{seeds.Count} {(seeds.Count == 1 ? "site" : "sites")} para varrer, até {maxPages} páginas",
            $"Sementes: {string.Join(", ", seeds)}.\nEntram também as rotas de carreira comuns (/carreiras, /vagas, /jobs) e o sitemap de cada site.", "Robots", true, ct);
        await ai.NoteAsync(thread, "Robots", "robots.txt obedecido em cada site", "Página proibida não é lida. Uma conexão por domínio, com o atraso ajustado sozinho (AutoThrottle). Sem disfarce de navegador.", "Coletor", true, ct);

        var info = new ProcessStartInfo(env.Scrapy) { WorkingDirectory = env.Project, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "crawl", "careers", "-a", $"seeds={string.Join(',', seeds)}", "-o", "-:jsonlines" }) info.ArgumentList.Add(a);
        info.Environment.Clear();
        foreach (var key in new[] { "PATH", "HOME", "LANG", "LC_ALL" }) if (Environment.GetEnvironmentVariable(key) is { Length: > 0 } v) info.Environment[key] = v;
        info.Environment["JO_MAX_PAGES"] = maxPages.ToString(CultureInfo.InvariantCulture);
        info.Environment["JO_MAX_SECONDS"] = maxSeconds.ToString(CultureInfo.InvariantCulture);
        info.Environment["JO_SCRAPER_CACHE"] = env.Cache;
        info.Environment["JO_SCRAPER_LOG"] = "INFO";

        var postings = new List<DiscoveredPosting>();
        var candidates = new List<Candidate>();
        var emails = new List<Email>();
        var boards = new List<string>();
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
                        case "board": boards.Add(item.Str("source")); break;
                        case "email": emails.Add(new Email(item.Str("email"), item.Str("url"), item.Str("context"))); break;
                        case "candidate": candidates.Add(new Candidate(item.Str("url"), item.Str("title"), item.Str("text"))); break;
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

        // Página que só monta com JavaScript: o Scrapy leu (o robots.txt já foi respeitado) e não achou nada. Com o Firecrawl configurado,
        // ele renderiza a mesma página e o texto segue para o Extrator e o Conferente como os outros. Sem Firecrawl, nada muda.
        if (postings.Count == 0 && candidates.Count == 0 && boards.Count == 0 && blocked == 0 && pages > 0 && config["Scraper:Firecrawl:Url"] is { Length: > 0 } firecrawl)
            foreach (var seed in seeds.Take(3))
                if (await RenderAsync(firecrawl, seed.StartsWith("http") ? seed : $"https://{seed}", ct) is { } page)
                {
                    candidates.Add(page);
                    await ai.NoteAsync(thread, "Coletor", $"Página com JavaScript lida pelo Firecrawl: {seed}", $"{page.Text.Length} caracteres para o Extrator.", "Extrator", true, ct);
                }

        // Extrator: a IA lê as páginas sem dado estruturado (até 8 por varredura) e o Conferente confere cada vaga contra a página.
        var fromAi = 0;
        var rejected = 0;
        foreach (var c in candidates.Take(config.GetValue("Scraper:MaxAiPages", 8)))
        {
            var r = await ai.CompleteAsync(LlmRequest.For(LlmTasks.PageJobs, LocalTaskSpecs.PageJobsSystem, LocalTaskSpecs.PageJobsPrompt(c.Url, c.Title, c.Text),
                LocalTaskSpecs.PageJobsSchema, timeoutSeconds: 240, thread: thread, agent: "Extrator"), ct);
            if (!r.Ok) continue;
            foreach (var job in Jobs(r.Json!))
            {
                var page = Fold(c.Text);
                var quote = Fold(job.Quote);
                if (quote.Length < 6 || !page.Contains(quote, StringComparison.Ordinal) || !quote.Contains(Fold(job.Title), StringComparison.Ordinal)) { rejected++; continue; }
                var email = job.ApplyEmail is { Length: > 3 } e && c.Text.Contains(e, StringComparison.OrdinalIgnoreCase) ? e : null;
                postings.Add(new DiscoveredPosting(source, Hash(c.Url + job.Title), job.Company is { Length: > 0 } co ? co : Host(c.Url), job.Title, job.Location,
                    Uri.TryCreate(job.ApplyUrl, UriKind.Absolute, out _) ? job.ApplyUrl : c.Url, job.Summary.Length > 0 ? job.Summary : c.Text[..Math.Min(c.Text.Length, 4000)],
                    null, r.Json!, Workplace: job.Modality is "remote" or "hybrid" or "onsite" ? job.Modality : null, ApplyEmail: email, ListedAsOpen: true));
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
        await ai.NoteAsync(thread, "Conferente", $"{postings.Count} vagas aceitas ({jsonLd} do JSON-LD, {fromAi} pela IA){(rejected > 0 ? $"; {rejected} recusadas" : "")}",
            rejected > 0 ? "Recusada: o título ou o trecho citado não está escrito na página." : "Toda vaga aceita tem o título escrito na página.", "Analista", true, ct);
        log.LogInformation("Varredura {Source}: {Pages} páginas, {JsonLd} JSON-LD, {Ai} pela IA, {Boards} quadros", source, pages, jsonLd, fromAi, boards.Count);
        return (postings, new CrawlSummary(pages, jsonLd, fromAi, rejected, boards.Distinct().Count(), emails.Count, blocked, [.. boards.Distinct()], null, clock.Elapsed.TotalSeconds));
    }

    private sealed record PageJob(string Title, string Company, string Location, string Modality, string Summary, string ApplyUrl, string ApplyEmail, string Quote);

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
            Compensation: j.Str("salary") is { Length: > 0 } s ? s : null, Employment: j.Str("employment") is { Length: > 0 } e ? e : null, ListedAsOpen: true);

    private static int Stat(string log, string name) =>
        Regex.Matches(log, $"'{Regex.Escape(name)}':\\s*(\\d+)").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).Sum();

    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..24];
    private static string Host(string url) => Uri.TryCreate(url, UriKind.Absolute, out var u) ? Json.Company(u.Host.Replace("www.", "").Split('.')[0]) : "";

    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        return Spaces().Replace(sb.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+")] private static partial Regex Spaces();
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
