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
        {
            if (SourceKeys.Board(raw) is { } board) { leads.Add(new(board, $"lista {repo}")); continue; }
            if (raw.Contains(',') || !Uri.TryCreate(raw, UriKind.Absolute, out var u)) continue;
            var host = Bare(u.Host);
            if (host.Split('.').Length < 2 || Ignored().IsMatch(host)) continue;
            var path = u.AbsolutePath.TrimEnd('/');
            // Link que já é a página de carreiras vai inteiro; o resto vai pela raiz, onde o coletor tenta as rotas comuns e o sitemap.
            leads.Add(new(CareerPath().IsMatch(path) ? $"web:{host}{path}" : $"web:{host}", $"lista {repo}"));
        }
        return [.. leads.DistinctBy(l => l.Source, StringComparer.OrdinalIgnoreCase)];
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
    private static string PageOf(DiscoveredPosting p)
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
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    [GeneratedRegex(@"https?://[^\s)\]|>""'`<]+", RegexOptions.IgnoreCase)] private static partial Regex Urls();
    [GeneratedRegex(@"vaga|job|emprego|hiring|oportunidade|trampo|carreira|career|remot", RegexOptions.IgnoreCase)] private static partial Regex JobRepo();
    [GeneratedRegex(@"me-contrata|curriculo|resume|portfolio", RegexOptions.IgnoreCase)] private static partial Regex NotJobs();
    [GeneratedRegex(@"carreira|career|vaga|job|trabalhe|work-with|join|oportunidade", RegexOptions.IgnoreCase)] private static partial Regex CareerPath();
    // Redes sociais, buscadores e agregadores com termos que proíbem robô (o caminho deles é outro ou nenhum), encurtadores e imagens.
    [GeneratedRegex(@"(^|\.)(linkedin|github|githubusercontent|twitter|x|facebook|fb|instagram|youtube|youtu|medium|glassdoor|indeed|indeedjobs|travis-ci|shields|wikipedia|google|goo|gstatic|apple|angel|wellfound|stackoverflow|bit|t|discord|telegram|whatsapp|wa|notion|trello|typeform|forms|gitlab|bitbucket|amazon|reddit|tiktok|crunchbase|workable|rippling|recruiterbox|jobvite|myworkdayjobs|workday|icims|taleo|successfactors|kenoby|vagas|catho|infojobs|programathor|geekhunter|remoteok|weworkremotely|remotive|himalayas|ycombinator|producthunt|slack|zoom|calendly|imgur|gravatar|cloudflare|herokuapp|vercel|netlify|archive|web\.archive|theoutline|aaronsw|jes)\.", RegexOptions.IgnoreCase)]
    private static partial Regex Ignored();
}
