using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Gupy (gupy:empresa): a página pública de carreiras da empresa em empresa.gupy.io, que o robots.txt libera. A lista vem do JSON da
/// própria página; a descrição, da página de cada vaga, uma de cada vez e no máximo 40 por rodada. Cobre empresas de todas as áreas no Brasil.
/// </summary>
internal sealed partial class GupySource(IHttpClientFactory http) : PublicSource(http, "gupy", "gupy.io")
{
    private const int MaxDetails = 40;
    public override string Id => "gupy-careers";
    public override string Name => "Gupy";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var company = Key(source).Replace("https://", "").Replace("http://", "").Split('.', '/')[0].Trim().ToLowerInvariant();
        if (!Slug().IsMatch(company)) throw new InvalidOperationException("Nome de empresa inválido para a Gupy.");
        var root = $"https://{company}.gupy.io";
        var page = await PagePropsAsync($"{root}/", ct);
        var name = page.TryGetProperty("careerPage", out var cp) && cp.Str("name") is { Length: > 0 } n ? n : Json.Company(company);
        var list = new List<DiscoveredPosting>();
        if (!page.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array) return list;
        var i = 0;
        foreach (var j in jobs.EnumerateArray())
        {
            var id = j.GetProperty("id").ToString();
            var url = $"{root}/jobs/{id}";
            var title = j.Str("title");
            var place = j.TryGetProperty("workplace", out var w) ? w : default;
            var address = place.ValueKind == JsonValueKind.Object && place.TryGetProperty("address", out var a) ? a : default;
            var location = address.ValueKind == JsonValueKind.Object ? string.Join(", ", new[] { address.Str("city"), address.Str("stateShortName"), address.Str("country") }.Where(x => x.Length > 0)) : "";
            var type = place.ValueKind == JsonValueKind.Object ? place.Str("workplaceType") : "";
            var description = $"{j.Str("department")}. {title}. {location}.";
            DateTime? published = null;
            if (i++ < MaxDetails)
            {
                try
                {
                    var job = (await PagePropsAsync(url, ct)).GetProperty("job");
                    description = Json.Plain($"{job.Str("description")}\n{job.Str("responsibilities")}\n{job.Str("prerequisites")}\n{job.Str("relevantExperiences")}");
                    published = job.Date("publishedAt");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException) { /* fica a descrição curta da lista */ }
                await Task.Delay(250, ct);
            }
            list.Add(new DiscoveredPosting(source, id, name, title, location, url, description, published, j.GetRawText(),
                Workplace: type switch { "remote" => "remote", "hybrid" => "hybrid", "on-site" => "onsite", _ => null }, ListedAsOpen: true));
        }
        return list;
    }

    /// <summary>O JSON que a página Next.js da Gupy traz embutido (props.pageProps).</summary>
    private async Task<JsonElement> PagePropsAsync(string url, CancellationToken ct)
    {
        var html = await Client.GetStringAsync(url, ct);
        var m = NextData().Match(html);
        if (!m.Success) throw new InvalidOperationException("A página da Gupy veio sem os dados das vagas.");
        using var doc = JsonDocument.Parse(m.Groups[1].Value);
        return doc.RootElement.GetProperty("props").GetProperty("pageProps").Clone();
    }

    [GeneratedRegex(@"<script id=""__NEXT_DATA__"" type=""application/json"">(.*?)</script>", RegexOptions.Singleline)] private static partial Regex NextData();
    [GeneratedRegex(@"^[a-z0-9-]{2,60}$")] private static partial Regex Slug();
}
