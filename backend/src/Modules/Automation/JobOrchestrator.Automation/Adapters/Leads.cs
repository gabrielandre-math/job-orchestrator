using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using JobOrchestrator.Intelligence.Contracts;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Pistas para o Explorador: repositórios de vaga pela busca oficial de issues do GitHub, listas de empresas mantidas em READMEs e a
/// varredura de vários sites numa rodada só do coletor. Sem buscador de terceiros: só API oficial e o robots.txt de cada site.
/// </summary>
internal sealed partial class Leads(IHttpClientFactory http, ScrapyRunner runner)
{
    private HttpClient Client => http.CreateClient("sources");

    public async Task<IReadOnlyList<SourceLead>> JobReposAsync(string query, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var text = await GitHubAsync($"https://api.github.com/search/issues?q={Uri.EscapeDataString($"{query} is:issue is:open created:>{since}")}&sort=created&per_page=100", "application/vnd.github+json", ct);
        using var doc = JsonDocument.Parse(text);
        return [.. doc.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => i.Str("repository_url").Replace("https://api.github.com/repos/", "", StringComparison.Ordinal))
            .Where(r => r.Count(c => c == '/') == 1 && JobRepo().IsMatch(r) && !NotJobs().IsMatch(r))
            .GroupBy(r => r, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() >= 2).OrderByDescending(g => g.Count())
            .Select(g => new SourceLead($"github:{g.Key}", $"{g.Count()} issues de vaga abertas no último mês"))];
    }

    public async Task<IReadOnlyList<SourceLead>> CompanyListAsync(string repo, CancellationToken ct) =>
        ParseList(await GitHubAsync($"https://api.github.com/repos/{repo}/readme", "application/vnd.github.raw", ct), repo);

    /// <summary>Cada link do README: quadro de ATS vira a fonte oficial; site de empresa vai para a varredura; rede social e afins ficam de fora.</summary>
    internal static IReadOnlyList<SourceLead> ParseList(string text, string repo)
    {
        var leads = new List<SourceLead>();
        foreach (var raw in Urls().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')', ']', '|', '*', '_', ';')))
            if (LeadFor(raw, $"lista {repo}") is { } lead) leads.Add(lead);
        return [.. leads.DistinctBy(l => l.Source, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Um link vira pista: quadro de ATS vira a fonte oficial; site de empresa vai para a varredura; rede social e afins ficam de fora.</summary>
    internal static SourceLead? LeadFor(string raw, string origin)
    {
        if (SourceKeys.Board(raw) is { } board) return new(board, origin);
        if (raw.Contains(',') || !Uri.TryCreate(raw, UriKind.Absolute, out var u)) return null;
        var host = Bare(u.Host);
        if (host.Split('.').Length < 2 || Ignored().IsMatch(host)) return null;
        var path = u.AbsolutePath.TrimEnd('/');
        // Link que já é a página de carreiras vai inteiro; o resto vai pela raiz, onde o coletor tenta as rotas comuns e o sitemap.
        return new(CareerPath().IsMatch(path) ? $"web:{host}{path}" : $"web:{host}", origin);
    }

    /// <summary>Busca na web pela API oficial do Brave Search (chave do usuário): cada resultado vira pista. Sem chave, não busca.</summary>
    public async Task<IReadOnlyList<SourceLead>> WebSearchAsync(string query, CancellationToken ct)
    {
        var key = await ApiKeys.GetAsync("brave", "key", ct) ?? throw new InvalidOperationException("Conecte a chave do Brave Search em Fontes › Chaves de API.");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count=20&country=br&search_lang=pt-br");
        request.Headers.Add("X-Subscription-Token", key);
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var results = doc.RootElement.TryGetProperty("web", out var web) && web.TryGetProperty("results", out var r) ? r.EnumerateArray().Select(x => x.Str("url")).ToList() : [];
        return [.. results.Select(u => LeadFor(u, $"busca na web por \"{query}\"")).OfType<SourceLead>().DistinctBy(l => l.Source, StringComparer.OrdinalIgnoreCase)];
    }

    public async Task<IReadOnlyList<SourceLead>> PlatformLeadsAsync(string platform, CancellationToken ct)
    {
        if (platform == "job-board-directory") return await BoardDirectoryAsync(ct);
        if (platform == "lastroundai") return await LastRoundAsync(ct);
        if (platform == "openjobs") return await OpenJobsAsync(ct);
        if (platform != "pandape") return [];
        // O sitemap que o robots.txt do Pandapé indica: um índice .gz de pedaços .gz com as vagas abertas de todas as empresas.
        var index = await GunzipAsync("https://www.pandape.com.br/pandapeindexofertas.xml.gz", ct);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var shard in Loc().Matches(index).Select(m => m.Groups[1].Value).Take(2))
            foreach (var tenant in PandapeTenant().Matches(await GunzipAsync(shard, ct)).Select(m => m.Groups[1].Value.ToLowerInvariant()))
                counts[tenant] = counts.GetValueOrDefault(tenant) + 1;
        return [.. counts.OrderByDescending(c => c.Value).Take(500).Select(c => new SourceLead($"pandape:{c.Key}", $"lista sitemap do Pandapé ({c.Value} vagas)"))];
    }

    /// <summary>
    /// Diretório público de páginas de carreira (github.com/kalebconfer-sys/job-board-directory): plataforma, token e endereço de cada
    /// quadro, com o número de vagas que o ATS informou. Só para descobrir quadros, que depois passam pelo teste do Explorador; o
    /// diretório não é republicado. Os maiores primeiro, até 4 mil por leitura.
    /// </summary>
    private async Task<IReadOnlyList<SourceLead>> BoardDirectoryAsync(CancellationToken ct)
    {
        var csv = await Client.GetStringAsync("https://kalebconfer-sys.github.io/job-board-directory/job-board-directory.csv", ct);
        var rows = new List<(string Key, int Roles, string Company)>();
        foreach (var line in csv.Split('\n').Skip(1))
        {
            var c = CsvLine(line.TrimEnd('\r'));
            if (c.Count < 6 || SourceKeys.Board(c[3]) is not { } key) continue;
            rows.Add((key, int.TryParse(c[5], out var n) ? n : 0, c[2]));
        }
        return [.. rows.GroupBy(r => r.Key).Select(g => g.OrderByDescending(r => r.Roles).First()).OrderByDescending(r => r.Roles).Take(4000)
            .Select(r => new SourceLead(r.Key, $"diretório job-board-directory ({r.Company}, {r.Roles} vagas no ATS)"))];
    }

    /// <summary>Diretório da LastRoundAI (CC BY 4.0, github.com/fyrosofttech/lastroundai-hiring-data): empresa e slug de Greenhouse, Lever e Ashby.</summary>
    private async Task<IReadOnlyList<SourceLead>> LastRoundAsync(CancellationToken ct)
    {
        var csv = await Client.GetStringAsync("https://raw.githubusercontent.com/fyrosofttech/lastroundai-hiring-data/main/ats-directory/lastroundai-ats-company-directory-2026-08.csv", ct);
        var leads = new List<SourceLead>();
        foreach (var line in csv.Split('\n').Skip(1))
        {
            var c = CsvLine(line.TrimEnd('\r'));
            if (c.Count < 3 || c[0] is not ("greenhouse" or "lever" or "ashby") || !Slug().IsMatch(c[2])) continue;
            leads.Add(new SourceLead($"{c[0]}:{c[2].ToLowerInvariant()}", $"diretório LastRoundAI, CC BY 4.0 ({c[1]})"));
        }
        return [.. leads.DistinctBy(l => l.Source).Take(4000)];
    }

    /// <summary>
    /// Quadros que a frota do Open Jobs (CC0, github.com/elliottdehn/open-jobs) lia com sucesso na última lista publicada, por ATS. O
    /// projeto parou em 10/10/2026: a lista é histórica, e cada quadro só entra na campanha depois do teste ao vivo do Explorador. Cada
    /// leitura semanal traz uma fatia diferente de 4 mil, para a fila não ficar só com esta lista.
    /// </summary>
    private async Task<IReadOnlyList<SourceLead>> OpenJobsAsync(CancellationToken ct)
    {
        await using var stream = await Client.GetStreamAsync("https://raw.githubusercontent.com/elliottdehn/open-jobs/main/slugs.json", ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var at = doc.RootElement.Str("generated_at");
        var week = ISOWeek.GetWeekOfYear(DateTime.UtcNow);
        var leads = new List<SourceLead>();
        foreach (var ats in doc.RootElement.GetProperty("ats").EnumerateObject())
            foreach (var slug in ats.Value.EnumerateArray())
                if (SourceKeys.OpenJobs(ats.Name, slug.GetString() ?? "") is { } key) leads.Add(new SourceLead(key, $"lista Open Jobs, CC0 (quadro lido pela frota deles em {at})"));
        return [.. leads.DistinctBy(l => l.Source, StringComparer.OrdinalIgnoreCase).OrderBy(l => unchecked(l.Source.Aggregate(week, (h, c) => h * 31 + c))).Take(4000)];
    }

    /// <summary>Uma linha de CSV com aspas (nomes de empresa com vírgula vêm entre aspas, e aspas dobradas dentro delas).</summary>
    internal static List<string> CsvLine(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted && ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
            else if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(ch);
        }
        cells.Add(cell.ToString());
        return cells;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,80}$")] private static partial Regex Slug();

    private async Task<string> GunzipAsync(string url, CancellationToken ct)
    {
        await using var raw = await Client.GetStreamAsync(url, ct);
        await using var gzip = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return await reader.ReadToEndAsync(ct);
    }

    public async Task<IReadOnlyList<SiteFinding>> SweepSitesAsync(IReadOnlyList<string> sites, CancellationToken ct)
    {
        var thread = AgentThread.New("crawl", $"Explorador: varredura de {sites.Count} {(sites.Count == 1 ? "site" : "sites")}");
        var (postings, summary) = await runner.CrawlAsync($"web:{string.Join(',', sites)}", sites, thread, ct, Math.Clamp(30 * sites.Count, 60, 300), Math.Clamp(3 * sites.Count, 8, 24));
        return [.. sites.Select(site => new SiteFinding(site,
            [.. (summary.Hits ?? []).Where(h => Under(h.Page, site)).Select(h => h.Source).Distinct(StringComparer.OrdinalIgnoreCase)],
            [.. postings.Where(p => Under(PageOf(p), site)).Select(p => p with { Source = $"web:{site}" })]))];
    }

    /// <summary>A página onde a vaga foi lida (o JSON-LD e a extração da IA guardam "page"); sem ela, o link de candidatura.</summary>
    internal static string PageOf(DiscoveredPosting p)
    {
        try
        {
            using var d = JsonDocument.Parse(p.RawJson);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.Str("page") is { Length: > 0 } page ? page : p.ApplyUrl;
        }
        catch (JsonException) { return p.ApplyUrl; }
    }

    /// <summary>Se o link é do site (ou de um subdomínio dele): carreiras.empresa.com conta para empresa.com.</summary>
    internal static bool Under(string url, string site)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        var host = Bare(u.Host);
        var root = Bare(site.Split('/')[0]);
        return host == root || host.EndsWith("." + root, StringComparison.Ordinal);
    }

    private static string Bare(string host)
    {
        var h = host.ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }

    private async Task<string> GitHubAsync(string url, string accept, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd(accept);
        if (await ApiKeys.GetAsync("github", "token", ct) is { Length: > 0 } token) request.Headers.Authorization = new("Bearer", token);
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [GeneratedRegex(@"https?://[^\s)\]|>""'`<]+", RegexOptions.IgnoreCase)] private static partial Regex Urls();
    [GeneratedRegex(@"<loc>\s*([^<\s]+)\s*</loc>")] private static partial Regex Loc();
    [GeneratedRegex(@"https://([a-z0-9-]+)\.pandape\.infojobs\.com\.br/Detail/", RegexOptions.IgnoreCase)] private static partial Regex PandapeTenant();
    [GeneratedRegex(@"vaga|job|emprego|hiring|oportunidade|trampo|carreira|career|remot", RegexOptions.IgnoreCase)] private static partial Regex JobRepo();
    [GeneratedRegex(@"me-contrata|curriculo|resume|portfolio", RegexOptions.IgnoreCase)] private static partial Regex NotJobs();
    [GeneratedRegex(@"carreira|career|vaga|job|trabalhe|work-with|join|oportunidade", RegexOptions.IgnoreCase)] private static partial Regex CareerPath();
    // Redes sociais, buscadores e agregadores com termos que proíbem robô (o caminho deles é outro ou nenhum), encurtadores e imagens.
    [GeneratedRegex(@"(^|\.)(linkedin|github|githubusercontent|twitter|x|facebook|fb|instagram|youtube|youtu|medium|glassdoor|indeed|indeedjobs|travis-ci|shields|wikipedia|google|goo|gstatic|apple|angel|wellfound|stackoverflow|bit|t|discord|telegram|whatsapp|wa|notion|trello|typeform|forms|gitlab|bitbucket|amazon|reddit|tiktok|crunchbase|workable|rippling|recruiterbox|jobvite|myworkdayjobs|workday|icims|taleo|successfactors|kenoby|vagas|catho|infojobs|programathor|geekhunter|remoteok|weworkremotely|remotive|himalayas|ycombinator|producthunt|slack|zoom|calendly|imgur|gravatar|cloudflare|herokuapp|vercel|netlify|archive|web\.archive|theoutline|aaronsw|jes)\.", RegexOptions.IgnoreCase)]
    private static partial Regex Ignored();
}
