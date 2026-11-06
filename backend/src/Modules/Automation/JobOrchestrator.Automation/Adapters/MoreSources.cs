using System.Collections.Concurrent;
using System.Xml.Linq;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// Agregadores com API pública e regra de uso: cada um no intervalo mínimo que pede (6 h por busca), crédito com link para a
/// vaga original no próprio agregador e nada de raspar página: só o endpoint oficial.
/// </summary>
internal abstract class Aggregator(IHttpClientFactory http, TimeProvider time, string prefix, string domain) : PublicSource(http, prefix, domain)
{
    private static readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<DiscoveredPosting> Items)> LastCall = new();
    protected virtual TimeSpan MinInterval => TimeSpan.FromHours(6);

    /// <summary>
    /// Dentro do intervalo mínimo, devolve a resposta da última chamada em vez de chamar de novo: o teste do Explorador e a rodada que
    /// vem logo depois dele enxergam as mesmas vagas, e a API não recebe a mesma pergunta duas vezes.
    /// </summary>
    public sealed override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        if (LastCall.TryGetValue(source, out var last) && now - last.At < MinInterval) return last.Items;
        var items = await FetchAsync(source, Key(source), ct);
        LastCall[source] = (now, items);
        return items;
    }

    protected abstract Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct);
    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, $"Listada agora em {Name}."));
}

/// <summary>Arbeitnow: API pública de vagas (Europa e remoto), sem chave.</summary>
internal sealed class ArbeitnowSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "arbeitnow", "arbeitnow.com")
{
    public override string Id => "arbeitnow-api";
    public override string Name => "Arbeitnow API";

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        using var doc = await Json.GetAsync(Client, "https://www.arbeitnow.com/api/job-board-api", ct);
        return [.. doc.RootElement.GetProperty("data").EnumerateArray()
            .Where(j => key.Length == 0 || $"{j.Str("title")} {j.Str("description")}".Contains(key, StringComparison.OrdinalIgnoreCase))
            .Select(j => new DiscoveredPosting(source, j.Str("slug"), j.Str("company_name"), j.Str("title"), j.Str("location"), j.Str("url"), Json.Plain(j.Str("description")),
                j.TryGetProperty("created_at", out var c) && c.TryGetInt64(out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null, j.GetRawText(),
                Workplace: j.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True ? "remote" : null, ListedAsOpen: true))];
    }
}

/// <summary>Himalayas: vagas remotas, API pública sem chave (pede link de volta para a vaga deles).</summary>
internal sealed class HimalayasSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "himalayas", "himalayas.app")
{
    public override string Id => "himalayas-api";
    public override string Name => "Himalayas API";

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var url = key.Length > 0 ? $"https://himalayas.app/jobs/api/search?q={Uri.EscapeDataString(key)}&limit=20" : "https://himalayas.app/jobs/api?limit=20";
        using var doc = await Json.GetAsync(Client, url, ct);
        return [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(j =>
        {
            var salary = j.TryGetProperty("minSalary", out var min) && min.ValueKind == JsonValueKind.Number ? $"{j.Str("currency")} {min.GetDouble():0}-{(j.TryGetProperty("maxSalary", out var max) && max.ValueKind == JsonValueKind.Number ? max.GetDouble().ToString("0") : "")}" : null;
            var where = j.TryGetProperty("locationRestrictions", out var lr) && lr.ValueKind == JsonValueKind.Array ? string.Join(", ", lr.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.Str("name"))) : "Remoto";
            // Os países aceitos vêm estruturados da Himalayas: a elegibilidade usa a lista como prova declarada, não o texto.
            var allowed = lr.ValueKind == JsonValueKind.Array ? lr.EnumerateArray().SelectMany(x => SourceCatalog.MatchCountries(x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : x.Str("name"))).Distinct().ToList() : null;
            return new DiscoveredPosting(source, j.Str("guid") is { Length: > 0 } g ? g : j.Str("applicationLink"), j.Str("companyName"), j.Str("title"), where, j.Str("applicationLink"),
                Json.Plain(j.Str("description")), j.Date("pubDate"), j.GetRawText(), Workplace: "remote", Compensation: salary, Employment: j.Str("employmentType"), ListedAsOpen: true,
                AllowedCountries: allowed is { Count: > 0 } ? allowed : null);
        })];
    }
}

/// <summary>Jobicy: vagas remotas, API pública sem chave (pede crédito e não mais que algumas chamadas por hora).</summary>
internal sealed class JobicySource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "jobicy", "jobicy.com")
{
    public override string Id => "jobicy-api";
    public override string Name => "Jobicy API";

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var tag = key.Length > 0 ? $"&tag={Uri.EscapeDataString(key)}" : "";
        using var doc = await Json.GetAsync(Client, $"https://jobicy.com/api/v2/remote-jobs?count=50{tag}", ct);
        return [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(j =>
        {
            var min = j.TryGetProperty("annualSalaryMin", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetDouble() : (double?)null;
            var max = j.TryGetProperty("annualSalaryMax", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : (double?)null;
            var geo = j.TryGetProperty("jobGeo", out var g) ? g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "" : "";
            return new DiscoveredPosting(source, j.GetProperty("id").ToString(), j.Str("companyName"), j.Str("jobTitle"), geo, j.Str("url"), Json.Plain(j.Str("jobDescription")),
                j.Date("pubDate"), j.GetRawText(), Workplace: "remote", Compensation: min is null ? null : $"{j.Str("salaryCurrency")} {min:0}-{max:0} por ano", ListedAsOpen: true);
        })];
    }
}

/// <summary>The Muse: API pública (sem chave, 500 chamadas por hora); a categoria vem da fonte (ex.: themuse:Software Engineering).</summary>
internal sealed class TheMuseSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "themuse", "themuse.com")
{
    public override string Id => "themuse-api";
    public override string Name => "The Muse API";
    protected override TimeSpan MinInterval => TimeSpan.FromHours(3);

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var category = key.Length > 0 ? key : "Software Engineering";
        using var doc = await Json.GetAsync(Client, $"https://www.themuse.com/api/public/jobs?page=1&category={Uri.EscapeDataString(category)}", ct);
        return [.. doc.RootElement.GetProperty("results").EnumerateArray().Select(j =>
        {
            var locations = j.TryGetProperty("locations", out var l) ? string.Join("; ", l.EnumerateArray().Select(x => x.Str("name"))) : "";
            return new DiscoveredPosting(source, j.GetProperty("id").ToString(), j.Obj("company").Str("name"), j.Str("name"), locations, j.Obj("refs").Str("landing_page"),
                Json.Plain(j.Str("contents")), j.Date("publication_date"), j.GetRawText(), Workplace: locations.Contains("Remote", StringComparison.OrdinalIgnoreCase) ? "remote" : null, ListedAsOpen: true);
        })];
    }
}

/// <summary>
/// Busca global do Workable (workable-search:termo): a busca pública do jobs.workable.com, que cobre todas as empresas que usam o
/// Workable. Duas consultas por termo, no Brasil e remota, até três páginas cada; o robots.txt libera e a vaga guarda o link dela lá.
/// </summary>
internal sealed class WorkableSearchSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "workable-search", "jobs.workable.com")
{
    public override string Id => "workable-search-api";
    public override string Name => "Workable (todas as empresas)";
    protected override TimeSpan MinInterval => TimeSpan.FromHours(1);

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var list = new List<DiscoveredPosting>();
        var ids = new HashSet<string>();
        foreach (var filter in new[] { "&location=Brazil", "&workplace=remote" })
        {
            string? token = null;
            for (var page = 0; page < 3; page++)
            {
                var url = $"https://jobs.workable.com/api/v1/jobs?query={Uri.EscapeDataString(key)}{filter}{(token is null ? "" : $"&pageToken={Uri.EscapeDataString(token)}")}";
                using var doc = await Json.GetAsync(Client, url, ct);
                var added = 0;
                if (!doc.RootElement.TryGetProperty("jobs", out var jobs) || jobs.ValueKind != JsonValueKind.Array) break;
                foreach (var j in jobs.EnumerateArray())
                {
                    if (!ids.Add(j.Str("id"))) continue;
                    added++;
                    var l = j.Obj("location");
                    var place = string.Join(", ", new[] { l.Str("city"), l.Str("subregion"), l.Str("countryName") }.Where(v => v.Length > 0));
                    var remote = j.TryGetProperty("locations", out var ls) && ls.ValueKind == JsonValueKind.Array && ls.EnumerateArray().Any(x => x.GetString() == "TELECOMMUTE");
                    list.Add(new DiscoveredPosting(source, j.Str("id"), j.Obj("company").Str("title"), j.Str("title"), remote ? (place.Length > 0 ? $"Remoto ({place})" : "Remoto") : place, j.Str("url"),
                        Json.Plain($"{j.Str("description")}\n{j.Str("requirementsSection")}\n{j.Str("benefitsSection")}"), j.Date("created"), j.GetRawText(),
                        Workplace: remote ? "remote" : null, ListedAsOpen: true));
                }
                token = doc.RootElement.Str("nextPageToken");
                if (token.Length == 0 || added == 0) break;
                await Task.Delay(700, ct);
            }
        }
        return list;
    }
}

/// <summary>
/// freehire (freehire:termo): busca pública sobre ~420 fontes (ATS, boards e governos) já normalizadas, com região, país e modo de
/// trabalho. Sem chave para uso pessoal e não comercial (os termos deles: https://freehire.me/docs/api). Três consultas por termo:
/// remoto na América Latina, remoto no mundo todo e qualquer vaga no Brasil, até 300 por consulta. A vaga guarda o link de origem.
/// </summary>
internal sealed class FreehireSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "freehire", "freehire.me")
{
    public override string Id => "freehire-api";
    public override string Name => "freehire (busca em 420 fontes)";
    protected override TimeSpan MinInterval => TimeSpan.FromHours(2);

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var list = new List<DiscoveredPosting>();
        var ids = new HashSet<string>();
        foreach (var (filter, pages) in new[] { ("&work_mode=remote&regions=latam", 3), ("&work_mode=remote&regions=global", 2), ("&countries=BR", 3) })
            for (var page = 0; page < pages; page++)
            {
                using var doc = await Json.GetAsync(Client, $"https://freehire.me/api/v1/jobs/search?q={Uri.EscapeDataString(key)}{filter}&limit=100&offset={page * 100}", ct);
                if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) break;
                var count = 0;
                foreach (var j in data.EnumerateArray())
                {
                    count++;
                    var id = j.Str("external_id") is { Length: > 0 } e ? e : j.Str("public_slug");
                    if (!ids.Add(id) || j.Str("closed_at").Length > 0) continue;
                    var mode = j.Str("work_mode");
                    list.Add(new DiscoveredPosting(source, id, j.Str("company"), j.Str("title"), j.Str("location"), Untracked(j.Str("url")), Json.Plain(j.Str("description")),
                        j.Date("posted_at"), j.GetRawText(), Workplace: mode is "remote" or "hybrid" or "onsite" ? mode : null, ListedAsOpen: true));
                }
                if (count < 100) break;
                await Task.Delay(600, ct);
            }
        return list;
    }

    /// <summary>O link da vaga sem o utm_source que o freehire acrescenta: é o mesmo endereço do ATS, e a deduplicação casa com a fonte direta.</summary>
    internal static string Untracked(string url) =>
        System.Text.RegularExpressions.Regex.Replace(url, @"([?&])utm_source=freehire\.me(&|$)", m => m.Groups[2].Value == "&" ? m.Groups[1].Value : "").TrimEnd('?', '&');
}

/// <summary>
/// JobTech JobSearch da Suécia (jobtech:termo): a API oficial da Arbetsförmedlingen, dados CC0, sem chave (https://jobsearch.api.jobtechdev.se).
/// Até 200 vagas por termo; em geral pedem estar na Suécia, o que a elegibilidade marca pelo texto de cada uma.
/// </summary>
internal sealed class JobTechSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "jobtech", "jobsearch.api.jobtechdev.se")
{
    public override string Id => "jobtech-api";
    public override string Name => "JobTech (Suécia, oficial)";
    protected override TimeSpan MinInterval => TimeSpan.FromHours(3);

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var list = new List<DiscoveredPosting>();
        for (var offset = 0; offset < 200; offset += 100)
        {
            using var doc = await Json.GetAsync(Client, $"https://jobsearch.api.jobtechdev.se/search?q={Uri.EscapeDataString(key)}&limit=100&offset={offset}", ct);
            if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array) break;
            var count = 0;
            foreach (var j in hits.EnumerateArray())
            {
                count++;
                var place = j.Obj("workplace_address");
                var where = string.Join(", ", new[] { place.Str("municipality"), place.Str("region"), place.Str("country") }.Where(v => v.Length > 0));
                var apply = j.Obj("application_details").Str("url") is { Length: > 0 } u ? u : j.Str("webpage_url");
                list.Add(new DiscoveredPosting(source, j.Str("id"), j.Obj("employer").Str("name"), j.Str("headline"), where.Length > 0 ? where : "Suécia", apply,
                    Json.Plain(j.Obj("description").Str("text")), j.Date("publication_date"), j.GetRawText(), ListedAsOpen: true));
            }
            if (count < 100) break;
            await Task.Delay(400, ct);
        }
        return list;
    }
}

/// <summary>RemoteOK: API pública; a regra deles pede crédito e link para a vaga no RemoteOK, que é o ApplyUrl guardado.</summary>
internal sealed class RemoteOkSource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "remoteok", "remoteok.com")
{
    public override string Id => "remoteok-api";
    public override string Name => "RemoteOK API";

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var url = key.Length > 0 ? $"https://remoteok.com/api?tag={Uri.EscapeDataString(key)}" : "https://remoteok.com/api";
        using var doc = await Json.GetAsync(Client, url, ct);
        // O primeiro item é o aviso legal deles, não uma vaga.
        return [.. doc.RootElement.EnumerateArray().Skip(1).Where(j => j.TryGetProperty("id", out _)).Select(j =>
        {
            var min = j.TryGetProperty("salary_min", out var a) && a.ValueKind == JsonValueKind.Number && a.GetDouble() > 0 ? a.GetDouble() : (double?)null;
            var max = j.TryGetProperty("salary_max", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : (double?)null;
            return new DiscoveredPosting(source, j.GetProperty("id").ToString(), j.Str("company"), j.Str("position"), j.Str("location") is { Length: > 0 } loc ? loc : "Remoto", j.Str("url"),
                Json.Plain(j.Str("description")), j.Date("date"), j.GetRawText(), Workplace: "remote", Compensation: min is null ? null : $"USD {min:0}-{max:0} por ano", ListedAsOpen: true);
        })];
    }
}

/// <summary>We Work Remotely: feed RSS público por categoria (ex.: wwr:remote-back-end-programming-jobs).</summary>
internal sealed class WeWorkRemotelySource(IHttpClientFactory http, TimeProvider time) : Aggregator(http, time, "wwr", "weworkremotely.com")
{
    public override string Id => "wwr-rss";
    public override string Name => "We Work Remotely (RSS)";

    protected override async Task<IReadOnlyList<DiscoveredPosting>> FetchAsync(string source, string key, CancellationToken ct)
    {
        var category = key.Length > 0 ? key : "remote-back-end-programming-jobs";
        var xml = await HttpBodies.XmlAsync(Client, $"https://weworkremotely.com/categories/{Uri.EscapeDataString(category)}.rss", ct);
        return [.. xml.Descendants("item").Select(i =>
        {
            // Título vem como "Empresa: Cargo".
            var title = (string?)i.Element("title") ?? "";
            var parts = title.Split(':', 2, StringSplitOptions.TrimEntries);
            var link = (string?)i.Element("link") ?? "";
            return new DiscoveredPosting(source, link, parts.Length == 2 ? parts[0] : "", parts.Length == 2 ? parts[1] : title, (string?)i.Element("region") ?? "Remoto", link,
                Json.Plain((string?)i.Element("description") ?? ""), DateTime.TryParse((string?)i.Element("pubDate"), out var d) ? d.ToUniversalTime() : null, title, Workplace: "remote", ListedAsOpen: true);
        })];
    }
}

/// <summary>Recruitee: API pública do quadro de cada empresa (recruitee:empresa).</summary>
internal sealed class RecruiteeSource(IHttpClientFactory http) : PublicSource(http, "recruitee", "recruitee.com")
{
    public override string Id => "recruitee-api";
    public override string Name => "Recruitee";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var company = Key(source);
        using var doc = await Json.GetAsync(Client, $"https://{company}.recruitee.com/api/offers/", ct);
        return [.. doc.RootElement.GetProperty("offers").EnumerateArray().Select(j => new DiscoveredPosting(source, j.GetProperty("id").ToString(),
            j.Str("company_name") is { Length: > 0 } n ? n : Json.Company(company), j.Str("title"), j.Str("location"), j.Str("careers_url"),
            Json.Plain(j.Str("description") + "\n" + j.Str("requirements")), j.Date("updated_at"), j.GetRawText(),
            Workplace: j.TryGetProperty("remote", out var r) && r.ValueKind == JsonValueKind.True ? "remote" : null, Employment: j.Str("employment_type_code"), ListedAsOpen: true))];
    }
}

/// <summary>Breezy HR: lista pública de vagas de cada empresa em JSON (breezy:empresa).</summary>
internal sealed class BreezySource(IHttpClientFactory http) : PublicSource(http, "breezy", "breezy.hr")
{
    public override string Id => "breezy-api";
    public override string Name => "Breezy HR";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var company = Key(source);
        using var doc = await Json.GetAsync(Client, $"https://{company}.breezy.hr/json", ct);
        return [.. doc.RootElement.EnumerateArray().Select(j => new DiscoveredPosting(source, j.Str("id"), j.Obj("company").Str("name") is { Length: > 0 } n ? n : Json.Company(company),
            j.Str("name"), j.Obj("location").Str("name"), j.Str("url"), j.Str("description"), j.Date("published_date"), j.GetRawText(),
            Workplace: j.Obj("location").TryGetProperty("is_remote", out var r) && r.ValueKind == JsonValueKind.True ? "remote" : null, Employment: j.Obj("type").Str("name"), ListedAsOpen: true))];
    }
}

/// <summary>Personio: feed XML público das vagas de cada empresa (personio:empresa).</summary>
internal sealed class PersonioSource(IHttpClientFactory http) : PublicSource(http, "personio", "jobs.personio.de", "jobs.personio.com")
{
    public override string Id => "personio-xml";
    public override string Name => "Personio";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var company = Key(source);
        var xml = await HttpBodies.XmlAsync(Client, $"https://{company}.jobs.personio.de/xml", ct);
        return [.. xml.Descendants("position").Select(p =>
        {
            var id = (string?)p.Element("id") ?? "";
            var text = string.Join("\n", p.Descendants("jobDescription").Select(d => $"{(string?)d.Element("name")}\n{(string?)d.Element("value")}"));
            return new DiscoveredPosting(source, id, (string?)p.Element("subcompany") is { Length: > 0 } sub ? sub : Json.Company(company), (string?)p.Element("name") ?? "",
                (string?)p.Element("office") ?? "", $"https://{company}.jobs.personio.de/job/{id}", Json.Plain(text), DateTime.TryParse((string?)p.Element("createdAt"), out var d) ? d.ToUniversalTime() : null,
                id, Employment: (string?)p.Element("employmentType"), ListedAsOpen: true);
        })];
    }
}

/// <summary>
/// GetOnBoard (getonbrd:termo): API pública de vagas de tecnologia da América Latina, com a empresa expandida. O robots.txt libera o
/// agente geral (search=yes). Até 3 páginas de 50 por rodada, com pausa entre elas.
/// </summary>
internal sealed class GetOnBoardSource(IHttpClientFactory http) : PublicSource(http, "getonbrd", "getonbrd.com", "www.getonbrd.com")
{
    public override string Id => "getonbrd-api";
    public override string Name => "GetOnBoard";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var term = Key(source);
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 3; page++)
        {
            using var doc = await Json.GetAsync(Client, $"https://www.getonbrd.com/api/v0/search/jobs?query={Uri.EscapeDataString(term)}&per_page=50&page={page}&expand=%5B%22company%22%5D", ct);
            foreach (var j in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var a = j.GetProperty("attributes");
                var company = a.TryGetProperty("company", out var c) && c.TryGetProperty("data", out var cd) && cd.TryGetProperty("attributes", out var ca) ? ca.Str("name") : "";
                var url = j.TryGetProperty("links", out var l) ? l.Str("public_url") : "";
                var countries = a.TryGetProperty("countries", out var co) && co.ValueKind == JsonValueKind.Array ? co.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0) : [];
                DateTime? published = Num(a, "published_at") is { } ts and > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime : null;
                var min = Num(a, "min_salary") ?? 0;
                var max = Num(a, "max_salary") ?? 0;
                list.Add(new DiscoveredPosting(source, j.GetProperty("id").GetString() ?? url, company.Length > 0 ? company : "GetOnBoard", a.Str("title"), string.Join(", ", countries), url,
                    Json.Plain($"{a.Str("description")}\n{a.Str("functions")}\n{a.Str("desirable")}\n{a.Str("benefits")}"), published, j.GetRawText(),
                    Workplace: a.Str("remote_modality") is "fully_remote" or "remote_local" ? "remote" : null, Compensation: min > 0 ? $"USD {min} - {max} por mês" : null, ListedAsOpen: true));
            }
            var pages = doc.RootElement.TryGetProperty("meta", out var m) ? Num(m, "total_pages") ?? 1 : 1;
            if (page >= pages) break;
            await Task.Delay(500, ct);
        }
        return list;
    }

    /// <summary>Número do JSON ou nada: salário e datas vêm null quando a empresa não informa.</summary>
    private static long? Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
}

/// <summary>
/// Workable (workable:conta): o mesmo endereço que o widget oficial de vagas usa nos sites das empresas, com a descrição. O robots.txt
/// de apply.workable.com libera tudo.
/// </summary>
internal sealed class WorkableSource(IHttpClientFactory http) : PublicSource(http, "workable", "apply.workable.com")
{
    public override string Id => "workable-widget";
    public override string Name => "Workable";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var account = Key(source);
        using var doc = await Json.GetAsync(Client, $"https://apply.workable.com/api/v1/widget/accounts/{Uri.EscapeDataString(account)}?details=true", ct);
        var company = doc.RootElement.Str("name") is { Length: > 0 } n ? n : Json.Company(account);
        return [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(j =>
        {
            var place = string.Join(", ", new[] { j.Str("city"), j.Str("state"), j.Str("country") }.Where(x => x.Length > 0));
            var remote = j.TryGetProperty("telecommuting", out var t) && (t.ValueKind == JsonValueKind.True || (t.ValueKind == JsonValueKind.String && t.GetString() == "true"));
            return new DiscoveredPosting(source, j.Str("shortcode"), company, j.Str("title"), place.Length > 0 ? place : remote ? "Remoto" : "", j.Str("url"),
                Json.Plain($"{j.Str("department")}\n{j.Str("experience")}\n{j.Str("description")}"), j.Date("published_on") ?? j.Date("created_at"), j.GetRawText(),
                Workplace: remote ? "remote" : null, Employment: j.Str("employment_type") is { Length: > 0 } e ? e : null, ListedAsOpen: true);
        })];
    }
}

/// <summary>Teamtailor (teamtailor:subdominio): o feed RSS oficial das vagas do site de carreira; o robots.txt só fecha as áreas internas.</summary>
internal sealed class TeamtailorSource(IHttpClientFactory http) : PublicSource(http, "teamtailor", "teamtailor.com")
{
    public override string Id => "teamtailor-rss";
    public override string Name => "Teamtailor";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var site = Key(source);
        var xml = await HttpBodies.XmlAsync(Client, $"https://{site}.teamtailor.com/jobs.rss", ct);
        XNamespace tt = "https://teamtailor.com/locations";
        var company = (string?)xml.Root?.Element("channel")?.Element("title") is { Length: > 0 } c ? c : Json.Company(site.Split('.')[0]);
        return [.. xml.Descendants("item").Select(i =>
        {
            var link = (string?)i.Element("link") ?? "";
            var place = string.Join(", ", (i.Element(tt + "locations")?.Descendants() ?? []).Where(e => !e.HasElements).Select(e => e.Value.Trim()).Where(v => v.Length > 0).Distinct());
            var remote = (string?)i.Element("remoteStatus");
            return new DiscoveredPosting(source, (string?)i.Element("guid") ?? link, company, (string?)i.Element("title") ?? "", place.Length > 0 ? place : remote == "fully" ? "Remoto" : "", link,
                Json.Plain($"{(string?)i.Element(tt + "department")}\n{(string?)i.Element("description")}"), DateTime.TryParse((string?)i.Element("pubDate"), out var d) ? d.ToUniversalTime() : null, link,
                Workplace: remote switch { "fully" => "remote", "hybrid" => "hybrid", _ => null }, ListedAsOpen: true);
        })];
    }
}

/// <summary>
/// Feed RSS ou Atom de qualquer quadro de vagas (rss:https://site/feed): RSS 2.0, Atom e os campos do WP Job Manager (empresa, local, tipo).
/// O robots.txt do site é conferido antes de ler o feed.
/// </summary>
internal sealed class RssSource(IHttpClientFactory http) : PublicSource(http, "rss")
{
    public override string Id => "rss-feed";
    public override string Name => "Feed RSS";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var url = new Uri(Key(source));
        try
        {
            using var robots = await Client.GetAsync(new Uri(url, "/robots.txt"), ct);
            if (robots.IsSuccessStatusCode && !CareerPageReader.Allowed(await robots.Content.ReadAsStringAsync(ct), url.AbsolutePath))
                throw new InvalidOperationException("O robots.txt do site não permite ler este feed.");
        }
        catch (HttpRequestException) { /* sem robots.txt acessível: segue as regras padrão */ }
        var xml = await HttpBodies.XmlAsync(Client, url, ct);
        XNamespace atom = "http://www.w3.org/2005/Atom", wp = "https://wpjobmanager.com/";
        var site = (string?)xml.Root?.Element("channel")?.Element("title") ?? (string?)xml.Root?.Element(atom + "title") ?? url.Host;
        var items = xml.Descendants("item").Select(i => (
            Title: (string?)i.Element("title") ?? "", Link: (string?)i.Element("link") ?? "", Body: (string?)i.Elements().FirstOrDefault(e => e.Name.LocalName == "encoded")?.Value ?? (string?)i.Element("description") ?? "",
            Date: (string?)i.Element("pubDate"), Company: Field(i, "company"), Place: Field(i, "location"), Type: Field(i, "job_type")))
            .Concat(xml.Descendants(atom + "entry").Select(e => (
                Title: (string?)e.Element(atom + "title") ?? "", Link: (string?)e.Elements(atom + "link").FirstOrDefault()?.Attribute("href") ?? "", Body: (string?)e.Element(atom + "content") ?? (string?)e.Element(atom + "summary") ?? "",
                Date: (string?)e.Element(atom + "updated") ?? (string?)e.Element(atom + "published"), Company: (string?)e.Element(atom + "author")?.Element(atom + "name") ?? "", Place: "", Type: "")));
        return [.. items.Where(i => i.Title.Length > 2 && i.Link.Length > 0).Select(i =>
        {
            // "Empresa: Cargo" e "Cargo at Empresa" são os formatos mais comuns quando o feed não tem o campo da empresa.
            var (company, title) = i.Company.Length > 0 ? (i.Company, i.Title) : SplitTitle(i.Title, site);
            return new DiscoveredPosting(source, i.Link, company, title, i.Place, i.Link, Json.Plain(i.Body), DateTime.TryParse(i.Date, out var d) ? d.ToUniversalTime() : null, i.Link,
                Workplace: (i.Place + i.Title).Contains("remot", StringComparison.OrdinalIgnoreCase) ? "remote" : null, Employment: i.Type.Length > 0 ? i.Type : null, ListedAsOpen: true);
        })];
    }

    private static string Field(XElement item, string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() ?? "";

    internal static (string Company, string Title) SplitTitle(string text, string site)
    {
        var colon = text.IndexOf(": ", StringComparison.Ordinal);
        if (colon is > 1 and < 60) return (text[..colon].Trim(), text[(colon + 2)..].Trim());
        var at = text.LastIndexOf(" at ", StringComparison.OrdinalIgnoreCase);
        if (at > 3) return (text[(at + 4)..].Trim(), text[..at].Trim());
        return (site, text.Trim());
    }
}

/// <summary>4 Day Week (4dayweek:categoria): a API pública de vagas com semana de 4 dias ou jornada reduzida; o robots.txt libera /api/v2.</summary>
internal sealed class FourDayWeekSource(IHttpClientFactory http) : PublicSource(http, "4dayweek", "4dayweek.io")
{
    public override string Id => "4dayweek-api";
    public override string Name => "4 Day Week";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var category = Key(source).ToLowerInvariant();
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 3; page++)
        {
            using var doc = await Json.GetAsync(Client, $"https://4dayweek.io/api/v2/jobs?page={page}&limit=100", ct);
            foreach (var j in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                if (category is not ("" or "todas" or "all") && !j.Str("category").Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
                var places = j.TryGetProperty("locations", out var l) && l.ValueKind == JsonValueKind.Array
                    ? string.Join("; ", l.EnumerateArray().Select(x => string.Join(", ", new[] { x.Str("city"), x.Str("country") }.Where(v => v.Length > 0))).Where(v => v.Length > 0).Distinct()) : "";
                var mode = j.Str("work_arrangement");
                var tools = j.TryGetProperty("tools", out var t) && t.ValueKind == JsonValueKind.Array ? string.Join(", ", t.EnumerateArray().Select(x => x.Str("name"))) : "";
                var min = j.TryGetProperty("salary_min", out var mn) && mn.ValueKind == JsonValueKind.Number ? mn.GetInt64() / 100 : 0;
                var max = j.TryGetProperty("salary_max", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetInt64() / 100 : 0;
                list.Add(new DiscoveredPosting(source, j.Str("id"), j.Obj("company").Str("name") is { Length: > 0 } c ? c : "4 Day Week", j.Str("title"), places.Length > 0 ? places : mode == "remote" ? "Remoto" : "",
                    j.Str("url"), Json.Plain($"{j.Str("description")}\nFerramentas: {tools}"), j.Date("posted_at"), j.GetRawText(),
                    Workplace: mode is "remote" or "hybrid" ? mode : mode == "onsite" ? "onsite" : null,
                    Compensation: min > 0 ? $"{j.Str("salary_currency")} {min}-{max} por {(j.Str("salary_period") == "year" ? "ano" : j.Str("salary_period"))}" : null, ListedAsOpen: true));
            }
            if (!(doc.RootElement.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True)) break;
            await Task.Delay(500, ct);
        }
        return list;
    }
}

/// <summary>Working Nomads (workingnomads:categoria): a lista pública das vagas remotas mais novas; o robots.txt libera tudo.</summary>
internal sealed class WorkingNomadsSource(IHttpClientFactory http) : PublicSource(http, "workingnomads", "www.workingnomads.com")
{
    public override string Id => "workingnomads-api";
    public override string Name => "Working Nomads";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var category = Key(source);
        using var doc = await Json.GetAsync(Client, "https://www.workingnomads.com/api/exposed_jobs/", ct);
        return [.. doc.RootElement.EnumerateArray()
            .Where(j => category is "" or "todas" || j.Str("category_name").Contains(category, StringComparison.OrdinalIgnoreCase))
            .Select(j => new DiscoveredPosting(source, j.Str("url"), j.Str("company_name"), j.Str("title"), j.Str("location") is { Length: > 0 } p ? p : "Remoto", j.Str("url"),
                Json.Plain($"{j.Str("description")}\n{j.Str("tags")}"), j.Date("pub_date"), j.GetRawText(), Workplace: "remote", ListedAsOpen: true))];
    }
}

/// <summary>Adzuna (adzuna:termo): a API oficial com as vagas do Brasil, de todas as áreas; precisa da chave gratuita (Fontes › Chaves de API).</summary>
internal sealed class AdzunaSource(IHttpClientFactory http) : PublicSource(http, "adzuna", "www.adzuna.com.br", "api.adzuna.com")
{
    public override string Id => "adzuna-api";
    public override string Name => "Adzuna";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var id = await ApiKeys.GetAsync("adzuna", "appId", ct);
        var key = await ApiKeys.GetAsync("adzuna", "appKey", ct);
        if (id is null || key is null) throw new InvalidOperationException("Conecte a chave da Adzuna em Fontes › Chaves de API.");
        var term = Key(source);
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 2; page++)
        {
            using var doc = await Json.GetAsync(Client, $"https://api.adzuna.com/v1/api/jobs/br/search/{page}?app_id={Uri.EscapeDataString(id)}&app_key={Uri.EscapeDataString(key)}" +
                $"&results_per_page=50&what={Uri.EscapeDataString(term)}&max_days_old=30&sort_by=date&content-type=application/json", ct);
            var results = doc.RootElement.GetProperty("results").EnumerateArray().ToList();
            foreach (var j in results)
            {
                var min = j.TryGetProperty("salary_min", out var mn) && mn.ValueKind == JsonValueKind.Number ? mn.GetDouble() : 0;
                var max = j.TryGetProperty("salary_max", out var mx) && mx.ValueKind == JsonValueKind.Number ? mx.GetDouble() : 0;
                list.Add(new DiscoveredPosting(source, j.GetProperty("id").ToString(), j.Obj("company").Str("display_name"), j.Str("title"), j.Obj("location").Str("display_name"),
                    j.Str("redirect_url"), Json.Plain(j.Str("description")), j.Date("created"), j.GetRawText(),
                    Compensation: min > 0 ? $"BRL {min:0}-{max:0} por ano" : null, Employment: j.Str("contract_type") is { Length: > 0 } t ? t : null, ListedAsOpen: true));
            }
            if (results.Count < 50) break;
            await Task.Delay(600, ct);
        }
        return list;
    }
}

/// <summary>Jooble (jooble:termo): a API oficial com vagas de muitos sites brasileiros; precisa da chave gratuita (Fontes › Chaves de API).</summary>
internal sealed class JoobleSource(IHttpClientFactory http) : PublicSource(http, "jooble", "jooble.org", "br.jooble.org")
{
    public override string Id => "jooble-api";
    public override string Name => "Jooble";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var key = await ApiKeys.GetAsync("jooble", "key", ct) ?? throw new InvalidOperationException("Conecte a chave do Jooble em Fontes › Chaves de API.");
        var term = Key(source);
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 2; page++)
        {
            using var content = new StringContent(JsonSerializer.Serialize(new { keywords = term, location = "Brasil", page }), System.Text.Encoding.UTF8, "application/json");
            using var response = await Client.PostAsync($"https://jooble.org/api/{Uri.EscapeDataString(key)}", content, ct);
            response.EnsureSuccessStatusCode();
            using var doc = await HttpBodies.JsonAsync(response.Content, ct);
            var jobs = doc.RootElement.GetProperty("jobs").EnumerateArray().ToList();
            foreach (var j in jobs)
                list.Add(new DiscoveredPosting(source, j.GetProperty("id").ToString(), j.Str("company"), j.Str("title"), j.Str("location"), j.Str("link"),
                    Json.Plain(j.Str("snippet")), j.Date("updated"), j.GetRawText(), Compensation: j.Str("salary") is { Length: > 0 } s ? s : null,
                    Employment: j.Str("type") is { Length: > 0 } t ? t : null, ListedAsOpen: true));
            if (jobs.Count == 0) break;
            await Task.Delay(600, ct);
        }
        return list;
    }
}

/// <summary>
/// Alertas de vaga do e-mail (alertas:email): as vagas que a leitura da caixa achou nos alertas do LinkedIn, Indeed, Catho, InfoJobs,
/// Glassdoor, Vagas.com e outros nos últimos 14 dias. Nenhum desses sites é aberto por robô; a candidatura segue pelo copiloto.
/// </summary>
internal sealed class EmailAlertsSource(IHttpClientFactory http, IEnumerable<IJobAlertFeed> feeds) : PublicSource(http, "alertas")
{
    public override string Id => "email-alerts";
    public override string Name => "Alertas de vaga por e-mail";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var jobs = new List<AlertJob>();
        foreach (var feed in feeds) jobs.AddRange(await feed.RecentAsync(DateTime.UtcNow.AddDays(-14), ct));
        return [.. jobs.Select(j => new DiscoveredPosting(source, j.Url, j.Company.Length > 0 ? j.Company : Json.Company(j.Sender.Split('.')[0]), j.Title, j.Location, j.Url,
            $"{j.Title}\n{j.Company}\n{j.Location}\n{j.Snippet}\nRecebida no alerta de {j.Sender} em {j.ReceivedAt:dd/MM}.", j.ReceivedAt, JsonSerializer.Serialize(j), ListedAsOpen: true))];
    }

    /// <summary>O alerta é recente e o site não é aberto por robô: a vaga conta como aberta até sair do alerta.</summary>
    public override Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct) => Task.FromResult(new PostingStatus(true, "Vaga de um alerta recente do seu e-mail."));
}
