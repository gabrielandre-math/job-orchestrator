using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace JobOrchestrator.Automation.Adapters;

/// <summary>
/// NAV (nav:termo, Noruega): o feed oficial pam-stilling-feed, com o token público que a própria NAV publica (gira de vez em quando; 401
/// pega o novo). Lê as páginas por next_url a partir de 7 dias atrás (If-Modified-Since), guarda o estado mais novo de cada anúncio e busca o
/// detalhe das ativas que citam o termo (até 40). Anúncio INACTIVE vira encerramento explícito: fecha a vaga que já existe e nunca cria
/// linha nova, como os termos da NAV pedem. Os contatos pessoais do anúncio não são guardados. "nav:*" traz as mais novas de qualquer área.
/// </summary>
internal sealed class NavSource(IHttpClientFactory http, TimeProvider time) : PublicSource(http, "nav", "pam-stilling-feed.nav.no", "arbeidsplassen.nav.no")
{
    private const string Base = "https://pam-stilling-feed.nav.no";
    private const int MaxPages = 12, MaxDetails = 40, Days = 7;
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static (string Value, DateTime At)? token;
    public override string Id => "nav-stilling-feed";
    public override string Name => "NAV pam-stilling-feed";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var term = Fold(Key(source));
        var any = term is "" or "*" or "all" or "alle";
        var latest = new Dictionary<string, JsonElement>();
        var url = $"{Base}/api/v1/feed";
        var since = time.GetUtcNow().UtcDateTime.AddDays(-Days).ToString("R", CultureInfo.InvariantCulture);
        for (var page = 0; page < MaxPages; page++)
        {
            using var doc = await GetAsync(url, page == 0 ? since : null, ct);
            if (doc is null) break;
            var items = doc.RootElement.GetProperty("items");
            foreach (var item in items.EnumerateArray())
            {
                var entry = item.GetProperty("_feed_entry");
                if (any || Fold($"{entry.Str("title")} {entry.Str("businessName")}").Contains(term, StringComparison.Ordinal)) latest[entry.Str("uuid")] = entry.Clone();
            }
            var next = doc.RootElement.Str("next_url");
            if (items.GetArrayLength() == 0 || next.Length == 0) break;
            url = Base + next;
        }

        var result = new List<DiscoveredPosting>();
        foreach (var (uuid, entry) in latest.Where(kv => kv.Value.Str("status") == "INACTIVE"))
            result.Add(new DiscoveredPosting(source, uuid, entry.Str("businessName"), entry.Str("title"), Place(entry.Str("municipal")), Link(uuid), "", null, entry.GetRawText(), Tombstone: true));
        foreach (var (uuid, _) in latest.Where(kv => kv.Value.Str("status") == "ACTIVE").OrderByDescending(kv => kv.Value.Str("sistEndret")).Take(MaxDetails))
        {
            using var detail = await GetAsync($"{Base}/api/v1/feedentry/{uuid}", null, ct);
            if (detail is null || !detail.RootElement.TryGetProperty("ad_content", out var ad)) continue;
            var where = ad.TryGetProperty("workLocations", out var w) && w.ValueKind == JsonValueKind.Array
                ? string.Join("; ", w.EnumerateArray().Select(l => string.Join(", ", new[] { l.Str("city"), l.Str("county") }.Where(x => x.Length > 0))).Where(x => x.Length > 0).Distinct()) : "";
            var apply = ad.Str("applicationUrl") is { Length: > 8 } a && Uri.IsWellFormedUriString(a, UriKind.Absolute) ? a : ad.Str("link");
            var employer = ad.TryGetProperty("employer", out var e) ? e.Str("name") : "";
            var kind = string.Join(", ", new[] { ad.Str("engagementtype"), ad.Str("extent") }.Where(x => x.Length > 0));
            result.Add(new DiscoveredPosting(source, uuid, employer, ad.Str("title"), $"{(where.Length > 0 ? where : "Norge")}, Norway", apply, Json.Plain(ad.Str("description")),
                ad.Date("updated") ?? ad.Date("published"), Public(ad), Employment: kind.Length > 0 ? kind : null, ListedAsOpen: true));
            await Task.Delay(120, ct);
        }
        return result;
    }

    public override async Task<PostingStatus> CheckOpenAsync(string applyUrl, CancellationToken ct)
    {
        var i = applyUrl.IndexOf("/stillinger/stilling/", StringComparison.Ordinal);
        if (i < 0) return new PostingStatus(true, "Listada no feed da NAV.");
        using var doc = await GetAsync($"{Base}/api/v1/feedentry/{applyUrl[(i + 21)..].Trim('/')}", null, ct);
        return doc is not null && doc.RootElement.Str("status") == "INACTIVE" ? new PostingStatus(false, "Encerrada no feed da NAV.") : new PostingStatus(true, "Ativa no feed da NAV.");
    }

    /// <summary>GET com o token público; 401 busca o token novo uma vez. 304 (nada mudou) e 404 voltam null.</summary>
    private async Task<JsonDocument?> GetAsync(string url, string? since, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(attempt > 0, ct));
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (since is not null) req.Headers.TryAddWithoutValidation("If-Modified-Since", since);
            using var res = await Client.SendAsync(req, ct);
            if (res.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
            if (res.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound) return null;
            res.EnsureSuccessStatusCode();
            return await HttpBodies.JsonAsync(res.Content, ct);
        }
        return null;
    }

    private async Task<string> TokenAsync(bool fresh, CancellationToken ct)
    {
        await TokenGate.WaitAsync(ct);
        try
        {
            if (!fresh && token is { } t && DateTime.UtcNow - t.At < TimeSpan.FromMinutes(30)) return t.Value;
            var text = await Client.GetStringAsync($"{Base}/api/publicToken", ct);
            var value = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.StartsWith("ey", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("A NAV não devolveu o token público.");
            token = (value, DateTime.UtcNow);
            return value;
        }
        finally { TokenGate.Release(); }
    }

    /// <summary>O anúncio sem os contatos pessoais (nome, e-mail e telefone de quem atende ficam fora do que guardamos).</summary>
    private static string Public(JsonElement ad)
    {
        var copy = System.Text.Json.Nodes.JsonNode.Parse(ad.GetRawText())!.AsObject();
        copy.Remove("contactList");
        return copy.ToJsonString();
    }

    private static string Link(string uuid) => $"https://arbeidsplassen.nav.no/stillinger/stilling/{uuid}";
    private static string Place(string municipal) => municipal.Length > 0 ? $"{CultureInfo.GetCultureInfo("nb-NO").TextInfo.ToTitleCase(municipal.ToLowerInvariant())}, Norway" : "Norway";
    /// <summary>Sem acento e sem caixa; ø, æ, ß e afins não se desmontam no Unicode e são trocados à mão.</summary>
    internal static string Fold(string s)
    {
        var t = s.ToLowerInvariant().Replace("ø", "o").Replace("æ", "ae").Replace("œ", "oe").Replace("ß", "ss").Replace("ł", "l").Replace("đ", "d").Replace("þ", "th");
        return new string(t.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Trim();
    }
}

/// <summary>
/// SeasonalJobs.dol.gov (seasonaljobs:termo, EUA): o arquivo diário oficial para indexadores, com as ordens H-2A enviadas ou atualizadas nos
/// últimos 20 dias. É trabalho temporário agrícola com visto patrocinado pelo empregador, diferente de patrocínio de visto qualificado.
/// Baixa uma vez por dia (o do dia ou, se ainda não saiu, o de ontem) e filtra pelo termo no cargo, na ocupação e nas tarefas.
/// </summary>
internal sealed class SeasonalJobsSource(IHttpClientFactory http, TimeProvider time) : PublicSource(http, "seasonaljobs", "seasonaljobs.dol.gov", "api.seasonaljobs.dol.gov")
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static (DateOnly Day, List<JsonElement> Orders)? cache;
    public override string Id => "seasonaljobs-dol-feed";
    public override string Name => "SeasonalJobs.dol.gov";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var term = NavSource.Fold(Key(source));
        var any = term is "" or "*" or "all";
        var orders = await OrdersAsync(ct);
        return [.. orders.Where(o => any || NavSource.Fold($"{o.Str("jobTitle")} {o.Str("socTitle")} {o.Str("jobDuties")}").Contains(term, StringComparison.Ordinal))
            .OrderByDescending(o => o.Str("dateSubmitted")).Take(200).Select(o => Parse(source, o))];
    }

    internal static DiscoveredPosting Parse(string source, JsonElement o)
    {
        var id = o.Str("caseNumber");
        var apply = o.Str("recApplyUrl") is { Length: > 8 } u && Uri.TryCreate(u.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? u : $"https://{u}", UriKind.Absolute, out var uri) && uri.Host.Contains('.')
            ? uri.ToString() : $"https://seasonaljobs.dol.gov/jobs/{id}";
        var email = o.Str("recApplyEmail") is { } m && m.Contains('@') && !m.Contains(' ') ? m : null;
        var wage = o.Str("jobWageOffer") is { Length: > 0 } w ? $"US$ {w}/{o.Str("jobWagePer")}" : null;
        var text = $"{o.Str("jobDuties")}\n\nOrdem de trabalho H-2A do Departamento do Trabalho dos EUA: trabalho temporário agrícola; o empregador faz a petição do visto H-2A (visa sponsorship provided).\n"
            + $"Período: {o.Str("jobBeginDate")} a {o.Str("jobEndDate")}. {(wage is null ? "" : $"Salário: {wage}. ")}Experiência mínima: {o.Str("jobMinexpmonths")} meses.";
        return new DiscoveredPosting(source, id, o.Str("empBusinessName"), o.Str("jobTitle"), $"{o.Str("jobCity")}, {o.Str("jobState")}, United States", apply, text,
            DateTime.TryParse(o.Str("dateSubmitted"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d) ? d : null, o.GetRawText(),
            Compensation: wage, Employment: "Temporário (H-2A)", ApplyEmail: email, ListedAsOpen: true);
    }

    private async Task<List<JsonElement>> OrdersAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        await Gate.WaitAsync(ct);
        try
        {
            if (cache is { } c && c.Day == today) return c.Orders;
            foreach (var day in new[] { today, today.AddDays(-1) })
            {
                using var res = await Client.GetAsync($"https://api.seasonaljobs.dol.gov/datahub-search/sjCaseData/zip/jo/{day:yyyy-MM-dd}", HttpCompletionOption.ResponseHeadersRead, ct);
                if (res.StatusCode == HttpStatusCode.NotFound) continue;
                res.EnsureSuccessStatusCode();
                await using var zip = await res.Content.ReadAsStreamAsync(ct);
                using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
                var entry = archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException("O arquivo do DOL veio sem o JSON.");
                await using var json = entry.Open();
                using var doc = await JsonDocument.ParseAsync(json, cancellationToken: ct);
                var orders = doc.RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
                cache = (today, orders);
                return orders;
            }
            throw new InvalidOperationException("O arquivo diário do DOL ainda não saiu (nem o de hoje nem o de ontem).");
        }
        finally { Gate.Release(); }
    }
}

/// <summary>
/// USAJOBS (usajobs:termo, governo dos EUA): a API oficial de busca, com a chave da pessoa (Authorization-Key) e o e-mail da conta no
/// User-Agent, como a documentação pede. Quase todas pedem cidadania americana; "Who may apply" vai na descrição para a elegibilidade marcar.
/// </summary>
internal sealed class UsaJobsSource(IHttpClientFactory http) : PublicSource(http, "usajobs", "data.usajobs.gov", "www.usajobs.gov")
{
    public override string Id => "usajobs-api";
    public override string Name => "USAJOBS";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var key = await ApiKeys.GetAsync("usajobs", "key", ct);
        var email = await ApiKeys.GetAsync("usajobs", "email", ct);
        if (key is null || email is null) throw new InvalidOperationException("CREDENTIALS_REQUIRED: conecte a chave do USAJOBS em Fontes › Chaves de API.");
        var list = new List<DiscoveredPosting>();
        for (var page = 1; page <= 3; page++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://data.usajobs.gov/api/search?Keyword={Uri.EscapeDataString(Key(source))}&ResultsPerPage=100&Page={page}");
            req.Headers.TryAddWithoutValidation("Authorization-Key", key);
            req.Headers.UserAgent.Clear();
            req.Headers.TryAddWithoutValidation("User-Agent", email);
            using var res = await Client.SendAsync(req, ct);
            res.EnsureSuccessStatusCode();
            using var doc = await HttpBodies.JsonAsync(res.Content, ct);
            var items = doc.RootElement.GetProperty("SearchResult").GetProperty("SearchResultItems");
            foreach (var it in items.EnumerateArray())
            {
                var m = it.GetProperty("MatchedObjectDescriptor");
                var details = m.Obj("UserArea").Obj("Details");
                var who = details.Obj("WhoMayApply").Str("Name");
                var apply = m.TryGetProperty("ApplyURI", out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0 ? a[0].GetString() ?? m.Str("PositionURI") : m.Str("PositionURI");
                list.Add(new DiscoveredPosting(source, m.Str("PositionID"), m.Str("OrganizationName"), m.Str("PositionTitle"), $"{m.Str("PositionLocationDisplay")}, United States",
                    apply, $"{details.Str("JobSummary")}\n\n{m.Str("QualificationSummary")}\n\nWho may apply: {who}", m.Date("PublicationStartDate"), m.GetRawText(), ListedAsOpen: true));
            }
            if (items.GetArrayLength() < 100) break;
        }
        return list;
    }
}

/// <summary>
/// ReliefWeb (reliefweb:termo): vagas de ONGs e agências humanitárias do mundo todo pela API oficial v2, com o appname aprovado pela
/// ReliefWeb (desde 2025 a API pede appname pré-aprovado). Crédito à ReliefWeb e ao órgão de origem de cada vaga.
/// </summary>
internal sealed class ReliefWebSource(IHttpClientFactory http) : PublicSource(http, "reliefweb", "api.reliefweb.int", "reliefweb.int")
{
    public override string Id => "reliefweb-jobs-api";
    public override string Name => "ReliefWeb Jobs";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var app = await ApiKeys.GetAsync("reliefweb", "appname", ct) ?? throw new InvalidOperationException("CREDENTIALS_REQUIRED: conecte o appname aprovado da ReliefWeb em Fontes › Chaves de API.");
        var url = $"https://api.reliefweb.int/v2/jobs?appname={Uri.EscapeDataString(app)}&profile=full&preset=latest&limit=100&query%5Bvalue%5D={Uri.EscapeDataString(Key(source))}";
        using var doc = await Json.GetAsync(Client, url, ct);
        return [.. doc.RootElement.GetProperty("data").EnumerateArray().Select(d =>
        {
            var f = d.GetProperty("fields");
            var org = f.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0 ? s[0].Str("name") : "";
            var countries = f.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.Array ? string.Join(", ", c.EnumerateArray().Select(x => x.Str("name"))) : "";
            return new DiscoveredPosting(source, d.Str("id"), org, f.Str("title"), countries.Length > 0 ? countries : "Global", f.Str("url_alias") is { Length: > 0 } alias ? alias : f.Str("url"),
                Json.Plain($"{f.Str("body-html")}{f.Str("body")}\n\n{f.Str("how_to_apply")}"), f.Obj("date").Date("created"), d.GetRawText(), ListedAsOpen: true);
        })];
    }
}

/// <summary>
/// JobTech JobAd Links (joblinks:termo, Suécia): a API aberta que junta os anúncios de muitos sites suecos e aponta para o original (source_links),
/// sem chave. Complementa o JobSearch (só Platsbanken): a candidatura é sempre no link do site que publicou.
/// </summary>
internal sealed class JobLinksSource(IHttpClientFactory http) : PublicSource(http, "joblinks", "links.api.jobtechdev.se")
{
    public override string Id => "jobtech-joblinks";
    public override string Name => "JobTech JobAd Links";

    public override async Task<IReadOnlyList<DiscoveredPosting>> DiscoverAsync(string source, CancellationToken ct)
    {
        var list = new List<DiscoveredPosting>();
        for (var offset = 0; offset < 300; offset += 100)
        {
            using var doc = await Json.GetAsync(Client, $"https://links.api.jobtechdev.se/joblinks?q={Uri.EscapeDataString(Key(source))}&limit=100&offset={offset}", ct);
            var hits = doc.RootElement.GetProperty("hits");
            list.AddRange(hits.EnumerateArray().Select(h => Parse(source, h)).OfType<DiscoveredPosting>());
            if (hits.GetArrayLength() < 100) break;
        }
        return list;
    }

    internal static DiscoveredPosting? Parse(string source, JsonElement h)
    {
        var link = h.TryGetProperty("source_links", out var s) && s.ValueKind == JsonValueKind.Array && s.GetArrayLength() > 0 ? s[0].Str("url") : "";
        if (link.Length == 0) return null;
        var where = h.TryGetProperty("workplace_addresses", out var w) && w.ValueKind == JsonValueKind.Array
            ? string.Join("; ", w.EnumerateArray().Select(a => string.Join(", ", new[] { a.Str("municipality"), a.Str("region") }.Where(x => x.Length > 0))).Where(x => x.Length > 0).Distinct()) : "";
        return new DiscoveredPosting(source, h.Str("id"), h.Obj("employer").Str("name"), h.Str("headline"), $"{(where.Length > 0 ? where : "Sverige")}, Sweden", link,
            h.Str("brief"), h.Date("publication_date"), h.GetRawText(), ListedAsOpen: true);
    }
}
