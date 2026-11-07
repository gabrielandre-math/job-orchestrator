using System.Net;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Fonte no formato da Job Board API do Greenhouse. Aceita "greenhouse:&lt;board&gt;" (API pública) ou
/// "greenhouse:http://host/boards/&lt;board&gt;" (ATS de teste com o mesmo formato).
/// </summary>
internal sealed partial class GreenhouseLikeSource(IHttpClientFactory http, IOptions<AutomationOptions> options) : ISourceConnector
{
    private const string Prefix = "greenhouse:";
    public string Id => "greenhouse-board-api";
    public string Version => "1.0.0";
    public string Name => "Greenhouse Job Board API";
    public string Kind => "API pública";
    public double Risk => 0.05;
    public IReadOnlyList<Capability> Capabilities { get; } = [Capability.DiscoverJobs, Capability.CheckPostingOpen];
    public IReadOnlyList<string> Domains => ["boards-api.greenhouse.io", .. options.Value.FakeAtsHosts];
    public double Fit(Uri url) => Domains.Any(d => d.Equals(url.Authority, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    public bool Accepts(string source) => source.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var board = source[Prefix.Length..].Trim();
        var baseUrl = board.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? board.TrimEnd('/') : $"https://boards-api.greenhouse.io/v1/boards/{board}";
        var client = http.CreateClient("sources");
        using var doc = await HttpBodies.JsonAsync(client, $"{baseUrl}/jobs?content=true", ct);
        // Alguns boards se chamam "Empresa - Linkedin"; o sufixo não é parte do nome.
        var company = BoardSuffix().Replace(await BoardNameAsync(client, baseUrl, ct), "");
        var result = new List<DiscoveredPosting>();
        foreach (var job in doc.RootElement.GetProperty("jobs").EnumerateArray())
        {
            var html = job.TryGetProperty("content", out var c) ? WebUtility.HtmlDecode(c.GetString() ?? "") : "";
            result.Add(new DiscoveredPosting(
                Source: source,
                ExternalId: job.GetProperty("id").ToString(),
                Company: BoardSuffix().Replace(job.TryGetProperty("company_name", out var cn) && cn.GetString() is { Length: > 0 } name ? name : company, ""),
                Title: job.GetProperty("title").GetString() ?? "",
                Location: job.TryGetProperty("location", out var l) && l.TryGetProperty("name", out var ln) ? ln.GetString() ?? "" : "",
                ApplyUrl: job.GetProperty("absolute_url").GetString() ?? "",
                Description: PostingIntegrity.Visible(html).Text,
                UpdatedAt: job.TryGetProperty("updated_at", out var u) && u.TryGetDateTime(out var at) ? at.ToUniversalTime() : null,
                RawJson: job.GetRawText(),
                // A board that asks for the resume by e-mail says so under "how to apply"; a job with a form stays a form job.
                ApplyEmail: GitHubIssuesSource.ApplyContact(PostingIntegrity.Visible(html).Text).Email,
                // A API pública só lista vagas abertas; boards por URL (como o ATS de teste) são conferidos um por um.
                ListedAsOpen: !board.StartsWith("http", StringComparison.OrdinalIgnoreCase), DescriptionMarkup: html));
        }
        return result;
    }

    public async Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct)
    {
        using var response = await http.CreateClient("sources").GetAsync(applyUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.StatusCode switch
        {
            HttpStatusCode.OK => new PostingStatus(true, "A página de candidatura respondeu."),
            HttpStatusCode.NotFound or HttpStatusCode.Gone => new PostingStatus(false, "A vaga saiu do ar."),
            _ => new PostingStatus(true, $"Resposta {(int)response.StatusCode}; mantida como aberta até a próxima conferência.")
        };
    }

    private static async Task<string> BoardNameAsync(HttpClient client, string baseUrl, CancellationToken ct)
    {
        try
        {
            using var board = await HttpBodies.JsonAsync(client, baseUrl, ct);
            return board.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        }
        catch (HttpRequestException)
        {
            return "";
        }
    }


    [GeneratedRegex(@"\s*[-|]\s*linked\s*in\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex BoardSuffix();
}
