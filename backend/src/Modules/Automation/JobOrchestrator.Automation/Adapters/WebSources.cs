using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Remotive ("remotive:.net"): vagas remotas por busca. Termos da API: citar o Remotive como fonte, apontar para o link
/// dele e não consultar mais de 4 vezes por dia. O link de candidatura é sempre o do Remotive, e a consulta tem intervalo mínimo.
/// </summary>
internal sealed class RemotiveSource(IHttpClientFactory http, TimeProvider time) : PublicSource(http, "remotive", "remotive.com")
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(6);
    private static readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<DiscoveredPosting> Items)> LastCall = new();
    public override string Id => "remotive-api";
    public override string Name => "Remotive API";

    /// <summary>A Remotive pede poucas chamadas: dentro de 6 horas, a mesma busca devolve a última resposta, sem chamar de novo.</summary>
    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var search = Key(source);
        var now = time.GetUtcNow().UtcDateTime;
        if (LastCall.TryGetValue(search, out var last) && now - last.At < MinInterval) return last.Items;
        using var doc = await Json.GetAsync(Client, $"https://remotive.com/api/remote-jobs?search={Uri.EscapeDataString(search)}&limit=100", ct);
        IReadOnlyList<DiscoveredPosting> items = [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(j => Parse(source, j))];
        LastCall[search] = (now, items);
        return items;
    }

    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Listada agora pelo Remotive."));

    internal static DiscoveredPosting Parse(string source, JsonElement j) =>
        new(source, j.GetProperty("id").ToString(), j.Str("company_name"), j.Str("title"), j.Str("candidate_required_location"), j.Str("url"),
            Json.Plain(j.Str("description")), j.Date("publication_date"), j.GetRawText(),
            Workplace: "remote", Compensation: j.Str("salary") is { Length: > 0 } s ? s : null, Employment: j.Str("job_type"), ListedAsOpen: true);
}

/// <summary>
/// Leitor de página de carreira: acha links de boards de ATS (Greenhouse, Lever, Ashby, SmartRecruiters) numa página que você salvou.
/// Uma requisição para o robots.txt e uma para a página; se o robots.txt proíbe o caminho, não lê.
/// </summary>
internal sealed partial class CareerPageReader(IHttpClientFactory http)
{
    private const int MaxBytes = 2_000_000;

    public async Task<(IReadOnlyList<string> Boards, string Detail)> FindBoardsAsync(Uri page, CancellationToken ct)
    {
        var client = http.CreateClient("sources");
        try
        {
            using var robots = await client.GetAsync(new Uri(page, "/robots.txt"), ct);
            if (robots.IsSuccessStatusCode && !Allowed(await robots.Content.ReadAsStringAsync(ct), page.AbsolutePath))
                return ([], "O robots.txt do site não permite ler esta página.");
        }
        catch (HttpRequestException) { /* sem robots.txt acessível: segue as regras padrão */ }

        using var response = await client.GetAsync(page, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return ([], $"A página respondeu {(int)response.StatusCode}.");
        var html = await ReadLimitedAsync(response, ct);
        var boards = Boards(html);
        return (boards, boards.Count == 0 ? "Nenhum board de ATS conhecido na página." : $"{boards.Count} board(s) encontrado(s).");
    }

    /// <summary>Regras do grupo "User-agent: *": vale o Allow ou Disallow de prefixo mais longo.</summary>
    internal static bool Allowed(string robots, string path)
    {
        var rules = new List<(bool Allow, string Prefix)>();
        var inGroup = false;
        var groupHasRules = false;
        foreach (var raw in robots.Split('\n'))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            var (key, value) = line.IndexOf(':') is var i and > 0 ? (line[..i].Trim().ToLowerInvariant(), line[(i + 1)..].Trim()) : ("", "");
            if (key == "user-agent")
            {
                if (groupHasRules) { inGroup = false; groupHasRules = false; }
                inGroup |= value == "*";
            }
            else if (inGroup && key is "allow" or "disallow")
            {
                groupHasRules = true;
                if (value.Length > 0) rules.Add((key == "allow", value.TrimEnd('*')));
            }
        }
        var match = rules.Where(r => path.StartsWith(r.Prefix, StringComparison.Ordinal)).OrderByDescending(r => r.Prefix.Length).FirstOrDefault();
        return match.Prefix is null || match.Allow;
    }

    internal static IReadOnlyList<string> Boards(string html) =>
        [.. AtsLinks().Matches(html).Select(m => m.Groups["host"].Value.ToLowerInvariant() switch
        {
            var h when h.Contains("greenhouse") => $"greenhouse:{m.Groups["slug"].Value}",
            "jobs.lever.co" => $"lever:{m.Groups["slug"].Value}",
            "jobs.ashbyhq.com" => $"ashby:{m.Groups["slug"].Value}",
            _ => $"smartrecruiters:{m.Groups["slug"].Value}"
        }).Where(s => !s.EndsWith(":embed", StringComparison.Ordinal)).Concat(Embed().Matches(html).Select(m => $"greenhouse:{m.Groups[1].Value}"))
        .Distinct(StringComparer.OrdinalIgnoreCase)];

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[MaxBytes];
        var total = 0;
        int read;
        while (total < MaxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, MaxBytes - total), ct)) > 0) total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    [GeneratedRegex(@"https?://(?<host>(?:job-)?boards\.greenhouse\.io|jobs\.lever\.co|jobs\.ashbyhq\.com|jobs\.smartrecruiters\.com)/(?<slug>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AtsLinks();
    [GeneratedRegex(@"greenhouse\.io/embed/job_board(?:/js)?\?for=([A-Za-z0-9_-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Embed();
}
