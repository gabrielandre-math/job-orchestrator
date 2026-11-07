using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Repositórios de vagas no GitHub ("github:backend-br/vagas"): cada issue aberta é uma vaga.
/// Só a lista de issues abertas, uma chamada por rodada (a API sem token permite 60 por hora).
/// </summary>
internal sealed partial class GitHubIssuesSource(IHttpClientFactory http) : PublicSource(http, "github", "github.com", "api.github.com")
{
    public override string Id => "github-issues";
    public override string Name => "Vagas em issues do GitHub";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var repo = Key(source);
        var token = await ApiKeys.GetAsync("github", "token", ct);
        var result = new List<DiscoveredPosting>();
        // Page by page through the Link header, up to five pages (500 issues); what is left is reported as a partial list.
        string? next = $"https://api.github.com/repos/{repo}/issues?state=open&per_page=100";
        for (var page = 0; page < MaxPages && next is not null; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            if (token is { Length: > 0 }) request.Headers.Authorization = new("Bearer", token);
            using var response = await Client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await HttpBodies.JsonAsync(response.Content, ct);
            result.AddRange(doc.RootElement.EnumerateArray().Where(i => !i.TryGetProperty("pull_request", out _)).Select(i => Parse(source, repo, i)));
            next = NextPage(response.Headers.TryGetValues("Link", out var links) ? string.Join(",", links) : null);
        }
        SourceCoverage.Report(source, result.Count, next is null ? result.Count : result.Count + 1);
        return result;
    }

    /// <summary>A lista só traz issues abertas; conferir uma por uma gastaria o limite da API à toa.</summary>
    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Issue aberta no repositório."));

    private const int MaxPages = 5;

    /// <summary>The rel="next" address of a GitHub Link header, or null on the last page.</summary>
    internal static string? NextPage(string? link) =>
        link?.Split(',').Select(part => part.Split(';')).FirstOrDefault(p => p.Length > 1 && p[1].Contains("rel=\"next\"", StringComparison.Ordinal))?[0].Trim().Trim('<', '>');

    internal static DiscoveredPosting Parse(string source, string repo, JsonElement issue)
    {
        var title = issue.Str("title");
        var body = issue.Str("body");
        var labels = issue.TryGetProperty("labels", out var l) ? l.EnumerateArray().Select(x => x.Str("name")).Where(x => x.Length > 0).ToList() : [];
        var tags = Brackets().Matches(title).Select(m => m.Groups[1].Value.Trim()).ToList();
        var clean = Brackets().Replace(title, "").Trim(' ', '-', '|');
        var fromBody = CompanyLine().Match(body);
        var company = CompanyOf(clean) ?? (fromBody.Success ? fromBody.Groups[1].Value.Trim() : $"Vaga em {repo}");
        var role = CompanyOf(clean) is { } found ? RoleTail().Replace(clean[..clean.LastIndexOf(found, StringComparison.Ordinal)], "").Trim() : clean;
        var location = string.Join(", ", tags.Concat(labels).Where(t => Place().IsMatch(t)).Distinct(StringComparer.OrdinalIgnoreCase));
        var (email, link) = ApplyContact(body);
        return new DiscoveredPosting(source, issue.GetProperty("number").ToString(), company, role.Length > 0 ? role : clean, location,
            link ?? issue.Str("html_url"), $"{title}\n{string.Join(", ", labels)}\n{body}", issue.Date("updated_at"), issue.GetRawText(),
            Workplace: tags.Concat(labels).FirstOrDefault(t => Remote().IsMatch(t)), Employment: labels.FirstOrDefault(t => Contract().IsMatch(t)), ApplyEmail: email, ListedAsOpen: true);
    }

    /// <summary>
    /// The address and link to apply, taken from the part of the text that says how to apply ("Como se candidatar", "How to apply") or,
    /// without such a section, from lines that talk about applying. The first address or link of a posting is often privacy, support or
    /// the company page, so without that context there is no apply address (no automatic e-mail) and the link stays the issue itself.
    /// </summary>
    internal static (string? Email, string? Link) ApplyContact(string body)
    {
        var section = ApplySection(body);
        var text = section ?? string.Join("\n", body.Split('\n').Where(l => ApplyWords().IsMatch(l)));
        var email = Emails().Matches(text).Select(m => m.Value).FirstOrDefault(e => !NotForApplying().IsMatch(e));
        // Inside "how to apply" any link counts; on a loose line only one that looks like an application (an ATS, a form, a jobs page).
        var link = Links().Matches(text).Select(m => m.Value.TrimEnd(')', '.', ','))
            .FirstOrDefault(u => !u.Contains("github.com", StringComparison.OrdinalIgnoreCase) && (section is not null || ApplyLink().IsMatch(u)));
        return (email, link);
    }

    private static string? ApplySection(string body)
    {
        var heading = ApplyHeading().Match(body);
        if (!heading.Success) return null;
        var rest = body[heading.Index..];
        var next = NextHeading().Match(rest, heading.Length);
        return next.Success ? rest[..next.Index] : rest;
    }

    /// <summary>"Desenvolvedor .NET na Empresa X" ou "Backend @ Empresa X": o que vem depois do último separador.</summary>
    private static string? CompanyOf(string title) =>
        Separator().Match(title) is { Success: true } m ? m.Groups[1].Value.Trim()
        : Parenthesis().Match(title) is { Success: true } p ? p.Groups[1].Value.Trim()
        : Dash().Match(title) is { Success: true } d ? d.Groups[1].Value.Trim() : null;

    [GeneratedRegex(@"\[([^\]]+)\]")] private static partial Regex Brackets();
    [GeneratedRegex(@"(\s(na|no|at)|\s?@|\s?\||\s?\(|\s[-–])\s*$", RegexOptions.IgnoreCase)] private static partial Regex RoleTail();
    [GeneratedRegex(@"(?:\s(?:na|no|at)\s|\s@\s?|\s\|\s)([^|@]+)$", RegexOptions.IgnoreCase)] private static partial Regex Separator();
    [GeneratedRegex(@"\(([^()]{2,60})\)\s*$")] private static partial Regex Parenthesis();
    [GeneratedRegex(@"\s[-–]\s([^-–]{2,60})$")] private static partial Regex Dash();
    [GeneratedRegex(@"(?im)^\s*(?:\*\*)?empresa(?:\*\*)?\s*[:\-]\s*(.+)$")] private static partial Regex CompanyLine();
    [GeneratedRegex(@"remot|home office|h[ií]brid|presencial|s[aã]o paulo|rio de janeiro|belo horizonte|curitiba|porto alegre|florian|recife|brasil|brazil", RegexOptions.IgnoreCase)] private static partial Regex Place();
    [GeneratedRegex(@"remot|home office|h[ií]brid|presencial", RegexOptions.IgnoreCase)] private static partial Regex Remote();
    [GeneratedRegex(@"^(clt|pj|cooperado|contractor|freela)", RegexOptions.IgnoreCase)] private static partial Regex Contract();
    [GeneratedRegex(@"https?://[^\s<>()\]]+", RegexOptions.IgnoreCase)] private static partial Regex Links();
    [GeneratedRegex(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase)] private static partial Regex Emails();
    [GeneratedRegex(@"(?im)^[ \t]*(?:#{1,6}[ \t]*|\*\*|__)?[ \t]*(?:como (?:se |me )?candidatar|como aplicar|para se candidatar|candidatura|how to apply|to apply|application process)\b[^\n]*$")]
    private static partial Regex ApplyHeading();
    [GeneratedRegex(@"(?m)^[ \t]*(?:#{1,6}[ \t]+\S|\*\*[^*\n]+\*\*[ \t]*:?[ \t]*$)")] private static partial Regex NextHeading();
    [GeneratedRegex(@"candidat|curr[ií]culo|\bcv\b|apply|resume|envie|inscri", RegexOptions.IgnoreCase)] private static partial Regex ApplyWords();
    [GeneratedRegex(@"^(?:privacy|privacidade|dpo|lgpd|no-?reply|abuse|security|seguranca|legal|juridico|support|suporte|sac|ouvidoria|billing|financeiro)@", RegexOptions.IgnoreCase)]
    private static partial Regex NotForApplying();
    [GeneratedRegex(@"apply|candidat|/jobs?\b|/vagas?\b|careers|carreiras|trabalhe|gupy\.io|lever\.co|greenhouse\.io|workable\.com|ashbyhq\.com|smartrecruiters|recruitee|breezy|forms\.gle|/forms/|typeform", RegexOptions.IgnoreCase)]
    private static partial Regex ApplyLink();
}

/// <summary>
/// "Ask HN: Who is hiring?" do mês (API do Algolia): cada comentário de primeiro nível é uma vaga.
/// Só os que citam C# ou .NET entram, para não trazer centenas de vagas fora do perfil.
/// </summary>
internal sealed partial class HackerNewsSource(IHttpClientFactory http) : PublicSource(http, "hn", "news.ycombinator.com", "hn.algolia.com")
{
    public override string Id => "hn-who-is-hiring";
    public override string Name => "Hacker News: Who is hiring";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        using var stories = await Json.GetAsync(Client, "https://hn.algolia.com/api/v1/search_by_date?tags=story,author_whoishiring&hitsPerPage=10", ct);
        var story = stories.RootElement.GetProperty("hits").EnumerateArray().FirstOrDefault(h => h.Str("title").StartsWith("Ask HN: Who is hiring?", StringComparison.Ordinal));
        if (story.ValueKind != JsonValueKind.Object) return [];
        using var thread = await Json.GetAsync(Client, $"https://hn.algolia.com/api/v1/items/{story.Str("objectID")}", ct);
        return [.. thread.RootElement.GetProperty("children").EnumerateArray()
            // Só comentários no formato do fio ("Empresa | Cargo | ..."); perfis de quem procura vaga ficam de fora.
            .Where(c => c.Str("text") is { Length: > 0 } t && Stack().IsMatch(t) && Json.Plain(t.Split("<p>", 2)[0]).Count(ch => ch == '|') >= 2)
            .Select(c => Parse(source, c))];
    }

    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Comentário do mês corrente."));

    /// <summary>A primeira linha costuma ser "Empresa | Cargo | Local | REMOTE | Salário".</summary>
    internal static DiscoveredPosting Parse(string source, JsonElement c)
    {
        var html = c.Str("text");
        var first = Json.Plain(html.Split("<p>", 2)[0]);
        var parts = first.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var company = parts.ElementAtOrDefault(0) ?? "Empresa no HN";
        var role = parts.Skip(1).FirstOrDefault(p => !Place().IsMatch(p) && !Money().IsMatch(p)) ?? "Vaga no HN";
        var location = string.Join(", ", parts.Skip(1).Where(p => Place().IsMatch(p)));
        var id = c.TryGetProperty("id", out var i) ? i.ToString() : c.Str("objectID");
        var link = Href().Match(html) is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : $"https://news.ycombinator.com/item?id={id}";
        return new DiscoveredPosting(source, id, company, role, location, link, Json.Plain(html), c.Date("created_at"), c.GetRawText(),
            Workplace: parts.FirstOrDefault(p => Remote().IsMatch(p)), Compensation: parts.FirstOrDefault(p => Money().IsMatch(p)), ListedAsOpen: true);
    }

    [GeneratedRegex(@"c#|\.net\b|dotnet|asp\.net", RegexOptions.IgnoreCase)] private static partial Regex Stack();
    [GeneratedRegex(@"remote|onsite|on-site|hybrid|\b[A-Z][a-z]+,\s?[A-Z]{2}\b|london|berlin|new york|san francisco|anywhere|europe|latam|americas|brazil", RegexOptions.IgnoreCase)] private static partial Regex Place();
    [GeneratedRegex(@"remote|anywhere|hybrid", RegexOptions.IgnoreCase)] private static partial Regex Remote();
    [GeneratedRegex(@"[$€£]\s?\d|\d+\s?k\b|usd|eur", RegexOptions.IgnoreCase)] private static partial Regex Money();
    [GeneratedRegex(@"href=""([^""]+)""", RegexOptions.IgnoreCase)] private static partial Regex Href();
}
