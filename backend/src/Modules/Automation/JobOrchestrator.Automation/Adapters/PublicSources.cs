using System.Net;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>Leitura de JSON das APIs públicas, sem tolerar formato inesperado em silêncio.</summary>
internal static partial class Json
{
    public static async Task<JsonDocument> GetAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await HttpBodies.JsonAsync(response.Content, ct);
    }

    public static string Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static string Plain(string html) => Spaces().Replace(PostingIntegrity.Visible(html).Text, " ").Trim();

    /// <summary>"acme-labs" vira "Acme Labs": as APIs de Lever e Ashby não trazem o nome da empresa.</summary>
    public static string Company(string slug) =>
        string.Join(' ', slug.Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    public static DateTime? Epoch(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime : null;

    public static DateTime? Date(this JsonElement e, string name) => DateTime.TryParse(e.Str(name), out var d) ? d.ToUniversalTime() : null;

    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
}

/// <summary>Base das fontes de API pública: identidade, capacidades e conferência de abertura por status HTTP.</summary>
internal abstract class PublicSource(IHttpClientFactory http, string prefix, params string[] domains) : ISourceConnector
{
    protected HttpClient Client => http.CreateClient("sources");
    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Version => "1.0.0";
    public string Kind => "API pública";
    public double Risk => 0.05;
    public IReadOnlyList<Capability> Capabilities { get; } = [Capability.DiscoverJobs, Capability.CheckPostingOpen];
    public IReadOnlyList<string> Domains => domains;
    public double Fit(Uri url) => domains.Any(d => url.Host.Equals(d, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
    public bool Accepts(string source) => source.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase);
    protected string Key(string source) => source[(prefix.Length + 1)..].Trim();
    public abstract Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct);

    public virtual async Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct)
    {
        using var response = await Client.GetAsync(applyUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone
            ? new PostingStatus(false, "A vaga saiu do ar.")
            : new PostingStatus(true, "A página da vaga respondeu.");
    }
}

/// <summary>Lever Postings API: "lever:empresa".</summary>
internal sealed class LeverSource(IHttpClientFactory http) : PublicSource(http, "lever", "jobs.lever.co", "api.lever.co", "jobs.eu.lever.co", "api.eu.lever.co")
{
    public override string Id => "lever-postings-api";
    public override string Name => "Lever Postings API";

    /// <summary>"lever:empresa" lê a instância dos EUA; "lever:eu/empresa" lê a europeia (jobs.eu.lever.co), como a documentação do Lever separa.</summary>
    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var key = Key(source);
        var (api, company) = key.StartsWith("eu/", StringComparison.OrdinalIgnoreCase) ? ("api.eu.lever.co", key[3..]) : ("api.lever.co", key);
        using var doc = await Json.GetAsync(Client, $"https://{api}/v0/postings/{Uri.EscapeDataString(company)}?mode=json", ct);
        return [.. doc.RootElement.EnumerateArray().Select(j => Parse(source, company, j))];
    }

    internal static DiscoveredPosting Parse(string source, string company, JsonElement j)
    {
        var lists = j.TryGetProperty("lists", out var l) && l.ValueKind == JsonValueKind.Array
            ? string.Join("\n", l.EnumerateArray().Select(x => $"{x.Str("text")}: {Json.Plain(x.Str("content"))}")) : "";
        var cat = j.Obj("categories");
        var salary = j.Obj("salaryRange");
        var pay = salary.ValueKind == JsonValueKind.Object && salary.TryGetProperty("min", out var min)
            ? $"{min} - {salary.Obj("max")} {salary.Str("currency")} {salary.Str("interval")}".Trim() : null;
        return new DiscoveredPosting(source, j.Str("id"), Json.Company(company), j.Str("text"), cat.Str("location"),
            j.Str("hostedUrl") is { Length: > 0 } hosted ? hosted : j.Str("applyUrl"),
            $"{j.Str("descriptionPlain")}\n{lists}\n{j.Str("additionalPlain")}".Trim(), j.Epoch("createdAt"), j.GetRawText(),
            Workplace: j.Str("workplaceType"), Compensation: pay, Employment: cat.Str("commitment"), ListedAsOpen: true);
    }
}

/// <summary>Ashby Posting API: "ashby:board".</summary>
internal sealed class AshbySource(IHttpClientFactory http) : PublicSource(http, "ashby", "jobs.ashbyhq.com", "api.ashbyhq.com")
{
    public override string Id => "ashby-posting-api";
    public override string Name => "Ashby Posting API";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var board = Key(source);
        using var doc = await Json.GetAsync(Client, $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(board)}?includeCompensation=true", ct);
        // Vaga com isListed=false é da empresa e não foi publicada: a documentação da Ashby diz para não mostrar.
        return [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Where(Listed).Select(j => Parse(source, board, j))];
    }

    internal static bool Listed(JsonElement j) => !(j.TryGetProperty("isListed", out var l) && l.ValueKind == JsonValueKind.False);

    internal static DiscoveredPosting Parse(string source, string board, JsonElement j)
    {
        var remote = j.TryGetProperty("isRemote", out var r) && r.ValueKind == JsonValueKind.True;
        var workplace = j.Str("workplaceType") is { Length: > 0 } w ? w : remote ? "remote" : null;
        return new DiscoveredPosting(source, j.Str("id"), Json.Company(board), j.Str("title"), j.Str("location"), j.Str("jobUrl"),
            j.Str("descriptionPlain"), j.Date("publishedAt"), j.GetRawText(),
            Workplace: workplace, Compensation: j.Obj("compensation").Str("compensationTierSummary"), Employment: j.Str("employmentType"), ListedAsOpen: true,
            DescriptionMarkup: j.Str("descriptionHtml") is { Length: > 0 } html ? html : null);
    }
}

/// <summary>SmartRecruiters Posting API: "smartrecruiters:empresa". A lista não traz descrição; os detalhes vêm um por um, com pausa.</summary>
internal sealed class SmartRecruitersSource(IHttpClientFactory http) : PublicSource(http, "smartrecruiters", "jobs.smartrecruiters.com", "api.smartrecruiters.com")
{
    private const int MaxDetails = 40;
    public override string Id => "smartrecruiters-posting-api";
    public override string Name => "SmartRecruiters Posting API";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var company = Key(source);
        var api = $"https://api.smartrecruiters.com/v1/companies/{Uri.EscapeDataString(company)}/postings";
        using var list = await Json.GetAsync(Client, $"{api}?limit=100", ct);
        var result = new List<DiscoveredPosting>();
        foreach (var j in list.RootElement.GetProperty("content").EnumerateArray().Take(MaxDetails))
        {
            using var detail = await Json.GetAsync(Client, $"{api}/{j.Str("id")}", ct);
            var d = detail.RootElement;
            var sections = d.Obj("jobAd").Obj("sections");
            var text = string.Join("\n", new[] { "companyDescription", "jobDescription", "qualifications", "additionalInformation" }.Select(s => Json.Plain(sections.Obj(s).Str("text"))));
            var loc = d.Obj("location");
            var remote = loc.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True;
            result.Add(new DiscoveredPosting(source, j.Str("id"), d.Obj("company").Str("name") is { Length: > 0 } n ? n : Json.Company(company), d.Str("name"),
                string.Join(", ", new[] { loc.Str("city"), loc.Str("country") }.Where(x => x.Length > 0)),
                d.Str("postingUrl") is { Length: > 0 } url ? url : $"https://jobs.smartrecruiters.com/{company}/{j.Str("id")}",
                text, d.Date("releasedDate"), j.GetRawText(), Workplace: remote ? "remote" : null, Employment: d.Obj("typeOfEmployment").Str("label"), ListedAsOpen: true));
            await Task.Delay(150, ct);
        }
        return result;
    }
}
