using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Gupy (gupy:empresa): a página pública de carreiras da empresa em empresa.gupy.io, que o robots.txt libera. A lista vem do JSON da
/// própria página; a descrição, da página de cada vaga, uma de cada vez e no máximo 40 por rodada. Cobre empresas de todas as áreas no Brasil.
/// </summary>
internal sealed partial class GupySource(IHttpClientFactory http) : PublicSource(http, "gupy", "gupy.io"), IHintedSource
{
    private const int MaxDetails = 40;
    public override string Id => "gupy-careers";
    public override string Name => "Gupy";

    public override Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) => DiscoverAsync(source, [], ct);

    /// <summary>
    /// Com dicas (palavras e cargos da campanha), as vagas cujo título ou área cita alguma delas ganham a descrição completa primeiro:
    /// empresa grande (bancos, varejo) tem centenas de vagas e só 40 descrições cabem por rodada.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var company = Key(source).Replace("https://", "").Replace("http://", "").Split('.', '/')[0].Trim().ToLowerInvariant();
        if (!Slug().IsMatch(company)) throw new InvalidOperationException("Nome de empresa inválido para a Gupy.");
        var root = $"https://{company}.gupy.io";
        var page = await PagePropsAsync($"{root}/", ct);
        var name = page.TryGetProperty("careerPage", out var cp) && cp.Str("name") is { Length: > 0 } n ? n : Json.Company(company);
        var list = new List<DiscoveredPosting>();
        if (!page.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array) return list;
        var words = hints.SelectMany(h => Fold(h).Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(w => w.Length >= 2).Distinct().ToList();
        int Match(JsonElement j) { var t = Fold($"{j.Str("title")} {j.Str("department")}"); return words.Count(w => t.Contains(w, StringComparison.Ordinal)); }
        var ordered = words.Count == 0 ? jobs.EnumerateArray().ToList() : [.. jobs.EnumerateArray().OrderByDescending(Match)];
        var i = 0;
        foreach (var j in ordered)
        {
            var id = j.GetProperty("id").ToString();
            var url = $"{root}/jobs/{id}";
            var title = j.Str("title");
            var place = j.TryGetProperty("workplace", out var w) ? w : default;
            var address = place.ValueKind == JsonValueKind.Object && place.TryGetProperty("address", out var a) ? a : default;
            var location = address.ValueKind == JsonValueKind.Object ? string.Join(", ", new[] { address.Str("city"), address.Str("stateShortName"), address.Str("country") }.Where(x => x.Length > 0)) : "";
            var type = place.ValueKind == JsonValueKind.Object ? place.Str("workplaceType") : "";
            var description = $"{j.Str("department")}. {title}. {location}.";
            string? markup = null;
            DateTime? published = null;
            if (i++ < MaxDetails)
            {
                try
                {
                    var job = (await PagePropsAsync(url, ct)).GetProperty("job");
                    markup = $"{job.Str("description")}\n{job.Str("responsibilities")}\n{job.Str("prerequisites")}\n{job.Str("relevantExperiences")}";
                    description = Json.Plain(markup);
                    published = job.Date("publishedAt");
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException) { /* fica a descrição curta da lista */ }
                await Task.Delay(250, ct);
            }
            list.Add(new DiscoveredPosting(source, id, name, title, location, url, description, published, j.GetRawText(),
                Workplace: type switch { "remote" => "remote", "hybrid" => "hybrid", "on-site" => "onsite", _ => null }, ListedAsOpen: true, DescriptionMarkup: markup));
        }
        return list;
    }

    /// <summary>
    /// Busca do portal (portal.gupy.io, robots.txt libera tudo): os subdomínios das empresas que aparecem no termo, página por página
    /// (12 vagas por página, até cinco páginas), com um segundo entre elas.
    /// </summary>
    public async Task<IReadOnlyList<string>> SearchAsync(string term, CancellationToken ct)
    {
        var companies = new List<string>();
        var pages = 1;
        for (var page = 1; page <= pages; page++)
        {
            var html = await Client.GetStringAsync($"https://portal.gupy.io/job-search/term={Uri.EscapeDataString(term)}{(page > 1 ? $"&page={page}" : "")}", ct);
            var found = Company().Matches(html).Select(m => m.Groups[1].Value.ToLowerInvariant()).Where(c => !Reserved.Contains(c)).Distinct().ToList();
            companies.AddRange(found);
            if (page == 1 && Total().Match(html) is { Success: true } t && int.TryParse(t.Groups[1].Value, out var total)) pages = Math.Clamp((total + 11) / 12, 1, 5);
            if (found.Count == 0) break;
            if (page < pages) await Task.Delay(1000, ct);
        }
        return [.. companies.Distinct()];
    }

    [GeneratedRegex(@"""total"":(\d+)")] private static partial Regex Total();

    private static readonly HashSet<string> Reserved = ["attachments", "suporte-candidatos", "www", "login", "portal", "help", "blog", "static", "cdn", "assets", "api", "employability-portal", "app"];
    private static string Fold(string s) => new string(s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());

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
    [GeneratedRegex(@"https://([a-z0-9-]{2,60})\.gupy\.io")] private static partial Regex Company();
}
