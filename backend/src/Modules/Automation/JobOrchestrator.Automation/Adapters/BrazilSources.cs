using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Sólides (solides:empresa): a API pública que o próprio site de vagas usa (apigw.solides.com.br), com a descrição na lista. O robots.txt de
/// vagas.solides.com.br libera tudo. Só entra vaga aberta ("em_andamento"); até 4 páginas de 25.
/// </summary>
internal sealed class SolidesSource(IHttpClientFactory http) : PublicSource(http, "solides", "vagas.solides.com.br")
{
    public override string Id => "solides-api";
    public override string Name => "Sólides";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var slug = Key(source).ToLowerInvariant();
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 4; page++)
        {
            using var doc = await Json.GetAsync(Client, $"https://apigw.solides.com.br/jobs/v3/home/vacancy?slug={Uri.EscapeDataString(slug)}&take=25&page={page}", ct);
            var (postings, pages) = ParsePage(source, slug, doc.RootElement);
            list.AddRange(postings);
            if (page >= pages) break;
            await Task.Delay(400, ct);
        }
        return list;
    }

    /// <summary>
    /// Uma página da API. Empresa sem vaga (ou slug que não existe) responde "data": {} sem a lista: isso é zero vagas, não erro
    /// (antes o GetProperty quebrava ali e uma empresa assim travava o Explorador inteiro).
    /// </summary>
    internal static (IReadOnlyList<DiscoveredPosting> Postings, int Pages) ParsePage(string source, string slug, JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array) return ([], 0);
        var list = new List<DiscoveredPosting>();
        foreach (var j in items.EnumerateArray())
        {
            if (j.Str("currentState") is { Length: > 0 } state && state != "em_andamento") continue;
            if (!j.TryGetProperty("id", out var idValue)) continue;
            var id = idValue.ToString();
            var type = j.Str("jobType").ToLowerInvariant();
            var remote = j.TryGetProperty("homeOffice", out var h) && h.ValueKind == JsonValueKind.True || type == "remoto";
            var place = string.Join(", ", new[] { j.Obj("city").Str("name"), j.Obj("state").Str("code") }.Where(x => x.Length > 0));
            list.Add(new DiscoveredPosting(source, id, Json.Company(slug), j.Str("title").Trim(), place.Length > 0 ? place : remote ? "Remoto" : "Brasil",
                $"https://{slug}.vagas.solides.com.br/vaga/{id}", Json.Plain(j.Str("description")), j.Date("createdAt"), j.GetRawText(),
                Workplace: remote ? "remote" : type == "hibrido" ? "hybrid" : type == "presencial" ? "onsite" : null, ListedAsOpen: true));
        }
        return (list, data.TryGetProperty("totalPages", out var t) && t.TryGetInt32(out var pages) ? pages : 1);
    }
}

/// <summary>
/// InHire (inhire:empresa): a API pública dos sites de carreira (api.inhire.app, cabeçalho X-Tenant), lista e detalhe. O robots.txt de
/// inhire.app libera o agente geral. A lista não traz descrição: as vagas que batem com as palavras da busca têm o detalhe lido primeiro.
/// </summary>
internal sealed class InhireSource(IHttpClientFactory http) : PublicSource(http, "inhire", "inhire.app"), IHintedSource
{
    public override string Id => "inhire-api";
    public override string Name => "InHire";

    public override Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) => DiscoverAsync(source, [], ct);

    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var tenant = Key(source).ToLowerInvariant();
        using var doc = await GetAsync("https://api.inhire.app/job-posts/public/pages", tenant, ct);
        var company = doc.RootElement.Str("tenantName") is { Length: > 0 } n ? n : Json.Company(tenant);
        var posts = doc.RootElement.GetProperty("jobsPage").EnumerateArray().Where(p => p.Str("status") is "" or "published")
            .Select(p => (Id: p.Str("jobId"), Title: p.Str("displayName"), Place: p.Str("location"), Mode: p.Str("workplaceType"))).Where(p => p.Id.Length > 0).ToList();
        var words = hints.Select(Fold).Where(w => w.Length > 1).ToList();
        var ordered = posts.OrderByDescending(p => words.Count(w => Fold(p.Title).Contains(w, StringComparison.Ordinal))).ToList();
        var list = new List<DiscoveredPosting>();
        foreach (var (p, i) in ordered.Select((p, i) => (p, i)))
        {
            string description = "";
            DateTime? published = null;
            if (i < 30)
            {
                try
                {
                    using var detail = await GetAsync($"https://api.inhire.app/job-posts/public/pages/{p.Id}", tenant, ct);
                    description = Json.Plain(detail.RootElement.Str("description"));
                    published = detail.RootElement.Date("publishedAt") ?? detail.RootElement.Date("createdAt");
                }
                catch (HttpRequestException) { /* sem o detalhe, a vaga entra só com o título */ }
                await Task.Delay(250, ct);
            }
            list.Add(new DiscoveredPosting(source, p.Id, company, p.Title, p.Place, $"https://{tenant}.inhire.app/vagas/{p.Id}/{Slug(p.Title)}",
                description.Length > 0 ? description : p.Title, published, $"{{\"jobId\":{JsonSerializer.Serialize(p.Id)}}}",
                Workplace: p.Mode.ToLowerInvariant() switch { "remote" => "remote", "hybrid" => "hybrid", "onsite" or "on-site" => "onsite", _ => null }, ListedAsOpen: true));
        }
        return list;
    }

    private async Task<JsonDocument> GetAsync(string url, string tenant, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant", tenant);
        using var response = await Client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await HttpBodies.JsonAsync(response.Content, ct);
    }

    internal static string Slug(string title)
    {
        var folded = Fold(title);
        var slug = Regex.Replace(folded, "[^a-z0-9]+", "-").Trim('-');
        return slug.Length > 0 ? slug[..Math.Min(slug.Length, 80)] : "vaga";
    }

    private static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string([.. d.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)]);
    }
}

/// <summary>
/// Quickin (quickin:empresa): a API pública dos sites de carreira (api.quickin.io), conta e vagas com descrição, requisitos e benefícios.
/// O robots.txt de jobs.quickin.io libera tudo. Só vaga publicada; até 3 páginas de 50.
/// </summary>
internal sealed class QuickinSource(IHttpClientFactory http) : PublicSource(http, "quickin", "jobs.quickin.io")
{
    public override string Id => "quickin-api";
    public override string Name => "Quickin";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var slug = Key(source);
        using var account = await Json.GetAsync(Client, $"https://api.quickin.io/public/accounts/{Uri.EscapeDataString(slug)}", ct);
        var id = account.RootElement.Str("_id");
        var company = account.RootElement.Str("name") is { Length: > 0 } n ? n : Json.Company(slug);
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 3; page++)
        {
            using var doc = await Json.GetAsync(Client, $"https://api.quickin.io/public/{id}/jobs?page={page}&limit=50", ct);
            foreach (var j in doc.RootElement.GetProperty("docs").EnumerateArray())
            {
                if (j.Str("publicate") is { Length: > 0 } state && state != "published") continue;
                var place = string.Join(", ", new[] { j.Str("city"), j.Str("region"), j.Str("country") }.Where(x => x.Length > 0).Distinct());
                var mode = j.Str("workplace_type");
                list.Add(new DiscoveredPosting(source, j.Str("_id"), j.Obj("company_id").Str("name") is { Length: > 0 } c ? c : company, j.Str("title"), place,
                    j.Str("career_url") is { Length: > 0 } url ? url : $"https://jobs.quickin.io/{slug}/jobs/{j.Str("_id")}",
                    Json.Plain($"{j.Str("description")}\n{j.Str("requirements")}\n{j.Str("benefits")}"), j.Date("created_at"), j.GetRawText(),
                    Workplace: mode is "remote" or "hybrid" or "onsite" ? mode : null, Employment: j.Obj("contract_id").Str("name") is { Length: > 0 } e ? e : null, ListedAsOpen: true));
            }
            if (page >= (doc.RootElement.TryGetProperty("pages", out var p) && p.TryGetInt32(out var pages) ? pages : 1)) break;
            await Task.Delay(400, ct);
        }
        return list;
    }
}

/// <summary>
/// Pandapé (pandape:empresa): o ATS do InfoJobs. A página de carreira da empresa mostra as 20 vagas mais novas; cada vaga tem a própria página
/// com o dado estruturado (JobPosting). O robots.txt libera tudo menos a página de candidatura (/Apply/), que nunca é lida.
/// </summary>
internal sealed partial class PandapeSource(IHttpClientFactory http) : PublicSource(http, "pandape", "pandape.infojobs.com.br")
{
    public override string Id => "pandape-site";
    public override string Name => "Pandapé (InfoJobs)";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var tenant = Key(source).ToLowerInvariant();
        var site = $"https://{tenant}.pandape.infojobs.com.br";
        var html = await Client.GetStringAsync(site + "/", ct);
        var list = new List<DiscoveredPosting>();
        foreach (var id in Detail().Matches(html).Select(m => m.Groups[1].Value).Distinct().Take(20))
        {
            var url = $"{site}/Detail/{id}";
            try
            {
                var page = await Client.GetStringAsync(url, ct);
                foreach (var json in JsonLd().Matches(page).Select(m => m.Groups[1].Value))
                    list.AddRange(PageReader.JobPostings(source, url, json).Select(p => p with { ExternalId = id, ApplyUrl = url }));
            }
            catch (HttpRequestException) { /* vaga que saiu do ar entre a lista e o detalhe */ }
            await Task.Delay(300, ct);
        }
        return list;
    }

    [GeneratedRegex(@"href=""/Detail/(\d+)""")] private static partial Regex Detail();
    [GeneratedRegex(@"<script[^>]*application/ld\+json[^>]*>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)] private static partial Regex JsonLd();
}

/// <summary>
/// Workday (workday:empresa.wdN.myworkdayjobs.com/Site): a busca pública que o próprio site de carreira usa (/wday/cxs), pelas palavras da
/// campanha, e o detalhe de cada vaga com a descrição. O robots.txt dos sites Workday só fecha /refreshFacet/. Usado por multinacionais
/// (Accenture, Avanade e outras) que contratam no Brasil.
/// </summary>
internal sealed partial class WorkdaySource(IHttpClientFactory http) : PublicSource(http, "workday", "myworkdayjobs.com"), IHintedSource
{
    public override string Id => "workday-cxs";
    public override string Name => "Workday";

    public override Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) => DiscoverAsync(source, [], ct);

    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var key = Key(source);
        var host = key.Split('/')[0];
        var site = key.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var tenant = host.Split('.')[0];
        var api = $"https://{host}/wday/cxs/{tenant}/{site}";
        // Uma busca por palavra essencial (no máximo duas) e uma puxando para o Brasil; sem palavra, as mais novas.
        var found = new Dictionary<string, JsonElement>();
        var total = 0;
        foreach (var text in hints.Count > 0 ? hints.Take(2).Append($"{hints[0]} Brazil") : [""])
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { appliedFacets = new { }, limit = 20, offset = 0, searchText = text }), Encoding.UTF8, "application/json");
            using var response = await Client.PostAsync($"{api}/jobs", content, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await HttpBodies.JsonAsync(response.Content, ct);
            if (doc.RootElement.TryGetProperty("total", out var t) && t.TryGetInt32(out var n)) total = Math.Max(total, n);
            foreach (var j in doc.RootElement.GetProperty("jobPostings").EnumerateArray())
                if (j.Str("externalPath") is { Length: > 0 } path) found.TryAdd(path, j.Clone());
            await Task.Delay(400, ct);
        }
        // Details are capped at 25 per run: the board says how many it has, and the rest is reported as a partial list.
        SourceCoverage.Report(source, Math.Min(found.Count, 25), Math.Max(total, found.Count));
        var list = new List<DiscoveredPosting>();
        foreach (var (path, card) in found.Take(25))
        {
            try
            {
                using var doc = await Json.GetAsync(Client, api + path, ct);
                var info = doc.RootElement.GetProperty("jobPostingInfo");
                var country = info.Obj("country").Str("descriptor");
                var place = string.Join(", ", new[] { info.Str("location"), country }.Where(x => x.Length > 0));
                var title = info.Str("title");
                var remote = Remote().IsMatch(title + " " + info.Str("remoteType"));
                list.Add(new DiscoveredPosting(source, info.Str("jobReqId") is { Length: > 0 } req ? req : path, doc.RootElement.Obj("hiringOrganization").Str("name") is { Length: > 0 } org ? org : Json.Company(tenant),
                    title, place, info.Str("externalUrl") is { Length: > 0 } url ? url : $"https://{host}/{site}{path}", Json.Plain(info.Str("jobDescription")),
                    info.Date("startDate") ?? Posted(card.Str("postedOn")), info.GetRawText(), Workplace: remote ? "remote" : Hybrid().IsMatch(title) ? "hybrid" : null,
                    Employment: info.Str("timeType") is { Length: > 0 } t ? t : null, ListedAsOpen: true));
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException) { /* vaga que saiu do ar entre a busca e o detalhe */ }
            await Task.Delay(250, ct);
        }
        return list;
    }

    /// <summary>"Posted Today", "Posted Yesterday", "Posted 3 Days Ago", "Posted 30+ Days Ago".</summary>
    internal static DateTime? Posted(string text)
    {
        var today = DateTime.UtcNow.Date;
        if (text.Contains("Today", StringComparison.OrdinalIgnoreCase)) return today;
        if (text.Contains("Yesterday", StringComparison.OrdinalIgnoreCase)) return today.AddDays(-1);
        return Days().Match(text) is { Success: true } m ? today.AddDays(-int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) : null;
    }

    [GeneratedRegex(@"(\d+)\+?\s+Days?\s+Ago", RegexOptions.IgnoreCase)] private static partial Regex Days();
    [GeneratedRegex(@"remot[oa]|remote|home ?office|100% remoto", RegexOptions.IgnoreCase)] private static partial Regex Remote();
    [GeneratedRegex(@"h[íi]brid[oa]|hybrid", RegexOptions.IgnoreCase)] private static partial Regex Hybrid();
}

/// <summary>
/// Oracle Recruiting Cloud (oracle:empresa.fa.região.oraclecloud.com/Site): a API pública que o próprio site de carreira usa, com busca pela
/// palavra e o detalhe de cada vaga. Usado por bancos e multinacionais; sem robots.txt que feche a API.
/// </summary>
internal sealed partial class OracleSource(IHttpClientFactory http) : PublicSource(http, "oracle", "oraclecloud.com"), IHintedSource
{
    public override string Id => "oracle-rc";
    public override string Name => "Oracle Recruiting Cloud";

    public override Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) => DiscoverAsync(source, [], ct);

    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var key = Key(source);
        var host = key.Split('/')[0];
        var site = key.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var api = $"https://{host}/hcmRestApi/resources/latest";
        var found = new Dictionary<string, JsonElement>();
        foreach (var text in hints.Count > 0 ? hints.Take(2) : [""])
        {
            var keyword = text.Length > 0 ? $",keyword={Uri.EscapeDataString(text)}" : "";
            using var doc = await Json.GetAsync(Client, $"{api}/recruitingCEJobRequisitions?onlyData=true&expand=requisitionList.secondaryLocations&finder=findReqs;siteNumber={site}{keyword},limit=25,offset=0,sortBy=POSTING_DATES_DESC", ct);
            foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
                foreach (var r in item.GetProperty("requisitionList").EnumerateArray())
                    if (r.Str("Id") is { Length: > 0 } id) found.TryAdd(id, r.Clone());
            await Task.Delay(400, ct);
        }
        var company = Json.Company(host.Split('.')[0]);
        var list = new List<DiscoveredPosting>();
        foreach (var (id, r) in found.Take(20))
        {
            var description = "";
            try
            {
                using var detail = await Json.GetAsync(Client, $"{api}/recruitingCEJobRequisitionDetails?onlyData=true&expand=all&finder=ById;Id=%22{id}%22,siteNumber={site}", ct);
                var d = detail.RootElement.GetProperty("items").EnumerateArray().FirstOrDefault();
                if (d.ValueKind == JsonValueKind.Object)
                    description = Json.Plain($"{d.Str("ExternalDescriptionStr")}\n{d.Str("ExternalResponsibilitiesStr")}\n{d.Str("ExternalQualificationsStr")}");
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException) { /* sem detalhe: fica o título */ }
            var mode = r.Str("WorkplaceTypeCode").ToUpperInvariant();
            list.Add(new DiscoveredPosting(source, id, company, r.Str("Title"), r.Str("PrimaryLocation"), $"https://{host}/hcmUI/CandidateExperience/en/sites/{site}/job/{id}",
                description.Length > 0 ? description : r.Str("Title"), r.Date("PostedDate"), r.GetRawText(),
                Workplace: mode.Contains("REMOTE") ? "remote" : mode.Contains("HYBRID") ? "hybrid" : mode.Contains("ON_SITE") ? "onsite" : null, ListedAsOpen: true));
            await Task.Delay(250, ct);
        }
        return list;
    }
}

/// <summary>
/// SAP SuccessFactors (successfactors:site.da.empresa): a busca pública dos sites de carreira (/search/?q=) e a página de cada vaga, que
/// marca título, data, local e descrição em microdata. O robots.txt desses sites fecha só candidatura e cadastro, que nunca são abertos.
/// </summary>
internal sealed partial class SuccessFactorsSource(IHttpClientFactory http) : PublicSource(http, "successfactors"), IHintedSource
{
    public override string Id => "successfactors-rmk";
    public override string Name => "SAP SuccessFactors";

    public override Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct) => DiscoverAsync(source, [], ct);

    public async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, IReadOnlyList<string> hints, CancellationToken ct)
    {
        var host = Key(source).Split('/')[0];
        var cards = new Dictionary<string, (string Title, string Place)>();
        foreach (var text in hints.Count > 0 ? hints.Take(2) : [""])
        {
            var html = await Client.GetStringAsync($"https://{host}/search/?q={Uri.EscapeDataString(text)}&startrow=0", ct);
            // O link da vaga aparece com href antes ou depois da classe, conforme o tema do site.
            foreach (var m in HrefFirst().Matches(html).Concat(ClassFirst().Matches(html)))
                cards.TryAdd(m.Groups[1].Value, (System.Net.WebUtility.HtmlDecode(m.Groups[2].Value).Trim(), ""));
            await Task.Delay(400, ct);
        }
        var list = new List<DiscoveredPosting>();
        foreach (var (path, card) in cards.Take(20))
        {
            var url = $"https://{host}{path}";
            try
            {
                var page = await Client.GetStringAsync(url, ct);
                var title = ItemText(page, "title") is { Length: > 0 } t ? t : card.Title;
                var at = page.IndexOf("itemprop=\"description\"", StringComparison.Ordinal);
                var description = at >= 0 ? Json.Plain(page[at..Math.Min(page.Length, at + 20000)]) : title;
                var place = Geo().Match(page) is { Success: true } g ? System.Net.WebUtility.HtmlDecode(g.Groups[1].Value).Trim() : card.Place;
                DateTime? posted = Posted().Match(page) is { Success: true } p && DateTime.TryParseExact(p.Groups[1].Value, "ddd MMM dd HH:mm:ss 'UTC' yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
                list.Add(new DiscoveredPosting(source, path.TrimEnd('/').Split('/').Last(), Json.Company(host.Split('.').Reverse().Skip(1).FirstOrDefault() ?? host), title, place, url,
                    description[..Math.Min(description.Length, 8000)], posted, $"{{\"url\":{JsonSerializer.Serialize(url)}}}", ListedAsOpen: true));
            }
            catch (HttpRequestException) { /* vaga que saiu entre a busca e a página */ }
            await Task.Delay(300, ct);
        }
        return list;
    }

    private static string ItemText(string html, string prop) =>
        Regex.Match(html, $"itemprop=\"{prop}\"[^>]*>([^<]+)<") is { Success: true } m ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim() : "";

    [GeneratedRegex(@"<a[^>]*href=""(/job/[^""]+/\d+/)""[^>]*class=""jobTitle-link[^""]*""[^>]*>([^<]+)</a>")] private static partial Regex HrefFirst();
    [GeneratedRegex(@"<a[^>]*class=""jobTitle-link[^""]*""[^>]*href=""(/job/[^""]+/\d+/)""[^>]*>([^<]+)</a>")] private static partial Regex ClassFirst();
    [GeneratedRegex(@"class=""jobGeoLocation""[^>]*>([^<]+)<")] private static partial Regex Geo();
    [GeneratedRegex(@"itemprop=""datePosted""[^>]*content=""([^""]+)""")] private static partial Regex Posted();
}
