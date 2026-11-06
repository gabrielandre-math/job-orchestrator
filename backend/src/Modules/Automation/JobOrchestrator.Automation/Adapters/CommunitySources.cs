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
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/issues?state=open&per_page=100");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        if (await ApiKeys.GetAsync("github", "token", ct) is { Length: > 0 } token) request.Headers.Authorization = new("Bearer", token);
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = await HttpBodies.JsonAsync(response.Content, ct);
        return [.. doc.RootElement.EnumerateArray().Where(i => !i.TryGetProperty("pull_request", out _)).Select(i => Parse(source, repo, i))];
    }

    /// <summary>A lista só traz issues abertas; conferir uma por uma gastaria o limite da API à toa.</summary>
    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Issue aberta no repositório."));

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
        var link = Links().Matches(body).Select(m => m.Value.TrimEnd(')', '.', ',')).FirstOrDefault(u => !u.Contains("github.com", StringComparison.OrdinalIgnoreCase));
        var email = Emails().Match(body) is { Success: true } e ? e.Value : null;
        return new DiscoveredPosting(source, issue.GetProperty("number").ToString(), company, role.Length > 0 ? role : clean, location,
            link ?? issue.Str("html_url"), $"{title}\n{string.Join(", ", labels)}\n{body}", issue.Date("updated_at"), issue.GetRawText(),
            Workplace: tags.Concat(labels).FirstOrDefault(t => Remote().IsMatch(t)), Employment: labels.FirstOrDefault(t => Contract().IsMatch(t)), ApplyEmail: email, ListedAsOpen: true);
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
