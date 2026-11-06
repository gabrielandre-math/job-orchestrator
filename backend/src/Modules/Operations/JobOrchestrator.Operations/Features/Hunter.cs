using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Campaigns.Contracts;
using JobOrchestrator.Intelligence.Contracts;
using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Operations.Features;

/// <summary>O plano da caçada: cargos em várias línguas, palavras de endereço de vaga, países, setores, empresas e buscas.</summary>
internal sealed record HuntPlan(string[] Roles, string[] Slugs, string[] Countries, string[] Industries, string[] Companies, string[] Queries);
internal sealed record HuntChannel(string Name, int Queries, int Found, int Leads, string? Note);
internal sealed record HuntSummary(DateTime At, string Depth, HuntPlan? Plan, string PlannedBy, IReadOnlyList<HuntChannel> Channels, int Leads,
    IReadOnlyList<string> Sample, double Seconds, string? Skipped);
internal sealed record HuntInput(string? Depth);
internal sealed record HuntSettings(bool Enabled, int IntervalHours, string Depth);

/// <summary>
/// Caçador de fontes com o Llama local: o modelo da máquina lê a campanha e o perfil e monta o plano (cargos em inglês, português e
/// espanhol, palavras que aparecem no endereço de páginas de vaga, países onde a pessoa pode disputar, setores e empresas). Depois três
/// canais oficiais e sem chave procuram no mundo todo: o índice público do Common Crawl (páginas de vaga dos ATS que o app lê, filtradas
/// pelas palavras), o Wikidata (empresas do setor em cada país, com o site oficial, e as empresas do plano conferidas, o que corta nome
/// inventado) e, com a chave do Brave, a busca na web. Tudo vira pista do Explorador, que testa ao vivo pela porta oficial antes de pôr
/// na campanha. Otimizado: respostas guardadas em disco (14 dias o Common Crawl, 30 o Wikidata), uma consulta por vez com espera,
/// orçamento por profundidade e só o modelo local (nada da campanha sai para a nuvem).
/// </summary>
internal sealed partial class Hunter(IServiceScopeFactory scopes, IHttpClientFactory http, [FromKeyedServices("operations")] NpgsqlDataSource data,
    IConfiguration config, ILogger<Hunter> log)
{
    private const long Lock = 4747031;
    private static string Folder => AppPaths.In(AppPaths.Data, "explorer", "hunter");
    internal static string LastFile => Path.Combine(Folder, "last.json");
    internal static string InboxFile => Path.Combine(Folder, "inbox.json");
    private static string Cache => AppPaths.In(AppPaths.Data, "explorer", "hunter", "cache");

    /// <summary>Os ATS que o app lê, na ordem da caçada: o domínio onde o Common Crawl guarda as páginas de vaga deles.</summary>
    internal static readonly string[] AtsDomains = ["gupy.io", "greenhouse.io", "lever.co", "ashbyhq.com", "workable.com", "recruitee.com", "teamtailor.com", "breezy.hr",
        "personio.de", "smartrecruiters.com", "pinpointhq.com", "bamboohr.com"];

    private HttpClient Client => http.CreateClient("hunter");

    /// <summary>Começa uma caçada em segundo plano; falso quando já tem uma rodando (aqui ou no Worker: a trava do Postgres é a mesma).</summary>
    public async Task<bool> StartAsync(string depth, CancellationToken ct)
    {
        if (await RunningAsync(ct)) return false;
        _ = Task.Run(async () =>
        {
            try { await HuntAsync(depth, CancellationToken.None); }
            catch (Exception ex) { log.LogWarning(ex, "Hunt failed"); Save(new(DateTime.UtcNow, depth, null, "", [], 0, [], 0, $"A caçada parou: {ex.Message}")); }
        }, CancellationToken.None);
        return true;
    }

    public async Task<bool> RunningAsync(CancellationToken ct)
    {
        await using var c = await data.OpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<bool>($"select exists(select 1 from pg_locks where locktype = 'advisory' and classid = 0 and objid = {Lock} and granted)");
    }

    public async Task<HuntSummary> HuntAsync(string depth, CancellationToken ct)
    {
        await using var gate = await data.OpenConnectionAsync(ct);
        if (!await gate.ExecuteScalarAsync<bool>($"select pg_try_advisory_lock({Lock})")) return Skip(depth, "Já tem uma caçada rodando.");
        try { return Save(await RunAsync(depth == "profundo" ? "profundo" : "rapido", ct)); }
        finally { await gate.ExecuteAsync($"select pg_advisory_unlock({Lock})"); }
    }

    private async Task<HuntSummary> RunAsync(string depth, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var campaign = (await sp.GetRequiredService<ICampaignsApi>().ListAsync(ct)).FirstOrDefault();
        if (campaign is null || campaign.Technologies.Count == 0) return Skip(depth, "Sem campanha com palavras-chave.");
        var profile = await sp.GetRequiredService<IProfileApi>().CurrentAsync(ct);
        var ai = sp.GetRequiredService<IIntelligenceApi>();
        var automation = sp.GetRequiredService<IAutomationApi>();
        var deep = depth == "profundo";
        var thread = AgentThread.New("hunt", "Caçador: fontes novas na internet");

        var (plan, by) = await PlanAsync(ai, campaign, profile, thread, ct);
        await Note(ai, thread, "Planejador", $"Plano de caça: {plan.Roles.Length} cargos, {plan.Slugs.Length} palavras de endereço, {plan.Countries.Length} países",
            $"Cargos: {string.Join(", ", plan.Roles)}.\nPalavras: {string.Join(", ", plan.Slugs)}.\nPaíses: {string.Join(", ", plan.Countries)}.\nSetores: {string.Join(", ", plan.Industries)}.",
            new { stage = "plan", plan, by }, ct);

        var leads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var channels = new List<HuntChannel> { await CommonCrawlAsync(plan, deep, leads, ai, thread, ct), await WikidataAsync(plan, deep, leads, ai, thread, ct) };
        if ((await automation.ApiKeysAsync(ct)).Contains("brave")) channels.Add(await WebAsync(automation, plan, deep, leads, ai, thread, ct));
        else channels.Add(new("Busca na web (Brave)", 0, 0, 0, "sem a chave do Brave Search: conecte em Fontes › Chaves de API para este canal entrar"));

        Enqueue(leads);
        var summary = new HuntSummary(DateTime.UtcNow, depth, plan, by, channels, leads.Count, [.. leads.Keys.Take(12)], Math.Round(clock.Elapsed.TotalSeconds, 1), null);
        await Note(ai, thread, "Caçador", $"{leads.Count} pistas novas para o Explorador testar",
            string.Join("\n", channels.Select(c => $"{c.Name}: {c.Queries} consultas, {c.Found} achados, {c.Leads} pistas{(c.Note is null ? "" : $" ({c.Note})")}")),
            new { stage = "done", leads = leads.Count, channels }, ct);
        // As pistas entram na fila com prioridade; um ciclo do Explorador já testa as primeiras pela porta oficial.
        try { await sp.GetRequiredService<Explorer>().CycleAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogInformation("Explorer after hunt skipped: {Error}", ex.Message); }
        return summary;
    }

    private const string PlanSystem =
        "Você planeja uma caçada de fontes de vagas na internet para uma pessoa. Responda só com o JSON do esquema, sem texto fora dele. " +
        "roles: de 6 a 10 nomes de cargo equivalentes aos da pessoa, em inglês, português e espanhol. " +
        "slugs: de 6 a 12 palavras curtas, minúsculas e sem acento que aparecem no endereço de páginas de vaga desse cargo (exemplos: dotnet, csharp, backend, golang, data-engineer). " +
        "countries: até 10 códigos ISO de duas letras onde há vagas que a pessoa pode disputar (remotas abertas a quem mora no país dela, ou presenciais se ela aceita mudança). " +
        "industries: de 3 a 6 setores em inglês. companies: até 20 empresas reais que costumam contratar esse cargo nesses países. queries: até 6 buscas para um buscador da web. " +
        "Use só o que está nos dados; não invente fatos sobre a pessoa.";
    private const string PlanSchema =
        """{"type":"object","properties":{"roles":{"type":"array","items":{"type":"string"}},"slugs":{"type":"array","items":{"type":"string"}},"countries":{"type":"array","items":{"type":"string"}},"industries":{"type":"array","items":{"type":"string"}},"companies":{"type":"array","items":{"type":"string"}},"queries":{"type":"array","items":{"type":"string"}}},"required":["roles","slugs","countries","industries","companies","queries"],"additionalProperties":false}""";

    /// <summary>O plano do Llama local, limpo; sem modelo ou com resposta ruim, o plano sai da campanha (cargos, palavras e países confirmados).</summary>
    private async Task<(HuntPlan Plan, string By)> PlanAsync(IIntelligenceApi ai, CampaignSettings c, ProfileSnapshot? p, AgentThread thread, CancellationToken ct)
    {
        var basic = Fallback(c, p);
        var skills = p?.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement).Take(20) ?? [];
        var languages = p?.Facts.Where(f => f.Kind == "language").Select(f => f.Statement) ?? [];
        var conf = p?.Confirmations;
        var prompt = $"Busca: {c.Name}. Cargos: {string.Join(", ", c.Titles ?? [])}. Palavras: {string.Join(", ", c.Technologies)}. Locais: {string.Join(", ", c.Locations)}.\n" +
            $"Competências do currículo: {string.Join(", ", skills)}. Idiomas: {string.Join(", ", languages)}.\n" +
            $"Mora em: {(conf?.AuthorizedCountries is { Count: > 0 } a ? string.Join(", ", a) : "Brasil")}. Aceita mudança: {(conf?.Relocate == true ? "sim" : "não")}. " +
            $"Modalidades: {(conf?.Modalities is { Count: > 0 } m ? string.Join(", ", m) : "remota")}.";
        try
        {
            var res = await ai.CompleteAsync(LlmRequest.For(LlmTasks.ExploreHunt, PlanSystem, prompt, PlanSchema, timeoutSeconds: 120, thread: thread, agent: "Planejador"), ct);
            if (res.Ok && res.Json is { } json) return (Merge(Parse(json), basic), $"{res.ProviderId} {res.Model}".Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogInformation("Hunt plan by the model failed: {Error}", ex.Message); }
        return (basic, "regras (sem o modelo local)");
    }

    internal static HuntPlan Parse(string json)
    {
        using var d = JsonDocument.Parse(json);
        string[] A(string name) => d.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? [.. v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length > 0)] : [];
        return new HuntPlan(A("roles"), A("slugs"), A("countries"), A("industries"), A("companies"), A("queries"));
    }

    /// <summary>O plano do modelo, limpo e somado ao das regras: palavras de endereço só [a-z0-9-], países que existem na base dos 249, tetos por lista.</summary>
    internal static HuntPlan Merge(HuntPlan model, HuntPlan basic)
    {
        var valid = SourceCatalog.Countries.Select(x => x.Iso2).ToHashSet(StringComparer.OrdinalIgnoreCase);
        static string[] Cap(IEnumerable<string> xs, int max, int len) => [.. xs.Select(x => x.Trim()).Where(x => x.Length is > 1 && x.Length <= len).Distinct(StringComparer.OrdinalIgnoreCase).Take(max)];
        return new HuntPlan(
            Cap(model.Roles.Concat(basic.Roles), 12, 80),
            Cap(basic.Slugs.Concat(model.Slugs.Select(Slug)), 14, 30),
            Cap(basic.Countries.Concat(model.Countries.Select(x => x.ToUpperInvariant())).Where(valid.Contains), 10, 2),
            Cap(model.Industries.Concat(basic.Industries), 6, 60),
            Cap(model.Companies, 25, 80),
            Cap(model.Queries.Concat(basic.Queries), 8, 160));
    }

    /// <summary>O plano sem modelo: os cargos e as palavras da campanha, o país da pessoa e os confirmados, e polos de vaga remota quando ela aceita remoto.</summary>
    internal static HuntPlan Fallback(CampaignSettings c, ProfileSnapshot? p)
    {
        var conf = p?.Confirmations;
        var remote = conf?.Modalities is not { Count: > 0 } m || m.Any(x => x.Contains("remot", StringComparison.OrdinalIgnoreCase));
        var countries = new List<string> { "BR" };
        countries.AddRange(conf?.AuthorizedCountries.SelectMany(SourceCatalog.MatchCountries) ?? []);
        if (remote || conf?.Relocate == true) countries.AddRange(["US", "PT", "ES", "DE", "NL", "GB", "IE", "CA"]);
        // Palavras de endereço: os termos inteiros da campanha (C# vira csharp) e os papéis que aparecem nos cargos (backend, full-stack...).
        var roles = (c.Titles ?? []).SelectMany(t => Roles.Where(r => r.Pattern.IsMatch(t)).Select(r => r.Slug));
        var slugs = roles.Concat(c.Technologies.Select(Slug)).Where(s => s.Length > 1 && !Common.Contains(s));
        return new HuntPlan([.. (c.Titles ?? []).Take(8)], [.. slugs.Distinct()], [.. countries.Distinct()], ["software", "information technology"], [],
            [.. (c.Titles ?? []).Take(4).Select(t => $"{t} remote")]);
    }

    private static readonly HashSet<string> Common = ["engineer", "developer", "software", "senior", "junior", "pleno", "desenvolvedor", "engenheiro", "de", "and", "the", "com",
        "aws", "ddd", "cqrs", "core", "full", "stack", "cloud", "docker"];

    private static readonly (Regex Pattern, string Slug)[] Roles =
    [
        (new(@"back.?end", RegexOptions.IgnoreCase), "backend"), (new(@"front.?end", RegexOptions.IgnoreCase), "frontend"),
        (new(@"full.?stack", RegexOptions.IgnoreCase), "full-stack"), (new(@"devops|\bsre\b|site reliability", RegexOptions.IgnoreCase), "devops"),
        (new(@"\bdata (engineer|scientist|analyst)|engenheir\w+ de dados|cientista de dados", RegexOptions.IgnoreCase), "data"),
        (new(@"mobile|android|\bios\b|flutter", RegexOptions.IgnoreCase), "mobile"), (new(@"\bqa\b|quality|test", RegexOptions.IgnoreCase), "qa"),
        (new(@"machine learning|\bml\b|\bai\b|\bia\b", RegexOptions.IgnoreCase), "machine-learning"), (new(@"security|seguran", RegexOptions.IgnoreCase), "security"),
    ];

    /// <summary>"C#" vira csharp, ".NET" vira dotnet, "ASP.NET Core" vira aspnet; o resto em minúsculas, sem acento, só letras, números e hífen.</summary>
    internal static string Slug(string term)
    {
        var t = term.Trim().ToLowerInvariant();
        if (t is "c#" or "c sharp") return "csharp";
        if (t is ".net" or "dotnet" or ".net core") return "dotnet";
        if (t.StartsWith("asp.net", StringComparison.Ordinal)) return "aspnet";
        if (t is "node.js" or "nodejs") return "node";
        if (t is "c++") return "cpp";
        var folded = new string(t.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(Regex.Replace(folded, "[^a-z0-9]+", "-"), "^-+|-+$", "");
    }

    /// <summary>Common Crawl: páginas de vaga de cada ATS com uma das palavras no endereço. Cada página vira o quadro da empresa (pista).</summary>
    private async Task<HuntChannel> CommonCrawlAsync(HuntPlan plan, bool deep, Dictionary<string, string> leads, IIntelligenceApi ai, AgentThread thread, CancellationToken ct)
    {
        if (plan.Slugs.Length == 0) return new("Common Crawl", 0, 0, 0, "sem palavras de endereço no plano");
        string crawl;
        try
        {
            crawl = await CachedAsync("cc-collinfo", TimeSpan.FromDays(1), async () =>
            {
                using var doc = JsonDocument.Parse(await GetAsync("https://index.commoncrawl.org/collinfo.json", ct));
                return doc.RootElement[0].GetProperty("id").GetString() ?? "";
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return new("Common Crawl", 1, 0, 0, $"o índice não respondeu: {ex.Message}"); }
        var pattern = string.Join("|", plan.Slugs.Take(8).Select(s => Regex.Escape(s).Replace("-", "-?")));
        var filter = Uri.EscapeDataString($"~url:.*({pattern}).*");
        int queries = 0, found = 0, added = 0;
        string? down = null;
        foreach (var domain in AtsDomains.Take(deep ? AtsDomains.Length : 4))
        {
            // O servidor do índice corta em ~10 s: a consulta vai por páginas (blocos limitados), e a primeira falha para o canal nesta caçada.
            var root = $"https://index.commoncrawl.org/{crawl}-index?url={domain}&matchType=domain&output=json";
            var before = added;
            try
            {
                var pages = await CachedAsync($"cc-pages-{crawl}-{domain}", TimeSpan.FromDays(14), async () =>
                {
                    using var d = JsonDocument.Parse(await GetAsync($"{root}&showNumPages=true", ct));
                    return d.RootElement.TryGetProperty("pages", out var n) ? n.GetInt32().ToString(CultureInfo.InvariantCulture) : "1";
                });
                for (var page = 0; page < Math.Min(int.Parse(pages, CultureInfo.InvariantCulture), deep ? 3 : 1); page++)
                {
                    var body = await CachedAsync($"cc-{crawl}-{domain}-{page}-{Hash(pattern)}", TimeSpan.FromDays(14),
                        () => GetAsync($"{root}&fl=url&limit=300&page={page}&filter={filter}", ct));
                    queries++;
                    var hits = Pages(body);
                    found += hits.Count;
                    foreach (var (board, origin) in FromPages(hits, plan.Slugs, domain)) if (leads.TryAdd(board, origin)) added++;
                    await Task.Delay(1500, ct);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
            {
                down = ex.Message;
                log.LogInformation("Common Crawl {Domain} skipped: {Error}", domain, ex.Message);
                break;
            }
            await Note(ai, thread, "Garimpeiro", $"Common Crawl, {domain}: {added - before} quadros novos", null, new { stage = "commoncrawl", domain, boards = added - before }, ct);
        }
        return new("Common Crawl", queries, found, added, down is null ? null : $"o índice do Common Crawl não respondeu agora ({down}); a próxima caçada tenta de novo");
    }

    /// <summary>
    /// Wikidata: empresas dos setores do plano em cada país, com o site oficial (cada site vai para a varredura do Explorador, que acha o quadro
    /// de vagas), e as empresas que o modelo citou, só as que existem lá com site (nome inventado cai aqui).
    /// </summary>
    private async Task<HuntChannel> WikidataAsync(HuntPlan plan, bool deep, Dictionary<string, string> leads, IIntelligenceApi ai, AgentThread thread, CancellationToken ct)
    {
        int queries = 0, found = 0, added = 0;
        string? note = null;
        try
        {
            var industries = new List<string>();
            foreach (var name in plan.Industries.Take(deep ? 4 : 2))
            {
                var id = await CachedAsync($"wd-search-{Hash(name)}", TimeSpan.FromDays(30), async () => (await SearchAsync(name, ct)).FirstOrDefault() ?? "");
                queries++;
                if (id.Length > 0) industries.Add(id);
            }
            foreach (var batch in plan.Countries.Chunk(4).Take(deep ? 3 : 1))
            {
                if (industries.Count == 0) break;
                var sparql = $"SELECT DISTINCT ?site ?code WHERE {{ VALUES ?code {{ {string.Join(" ", batch.Select(c => $"\"{c}\""))} }} VALUES ?ind {{ {string.Join(" ", industries.Select(i => $"wd:{i}"))} }} " +
                    "?country wdt:P297 ?code . ?c wdt:P17 ?country ; wdt:P452 ?ind ; wdt:P856 ?site . } LIMIT " + (deep ? 600 : 200);
                var body = await CachedAsync($"wd-sparql-{Hash(sparql)}", TimeSpan.FromDays(30),
                    () => GetAsync($"https://query.wikidata.org/sparql?format=json&query={Uri.EscapeDataString(sparql)}", ct));
                queries++;
                using var d = JsonDocument.Parse(body);
                foreach (var b in d.RootElement.GetProperty("results").GetProperty("bindings").EnumerateArray())
                {
                    found++;
                    if (Site(b.GetProperty("site").GetProperty("value").GetString()) is { } site &&
                        leads.TryAdd(site, $"achado pelo caçador no Wikidata ({string.Join(", ", plan.Industries.Take(2))}, {b.GetProperty("code").GetProperty("value").GetString()})")) added++;
                }
                await Task.Delay(1200, ct);
            }
            // As empresas do plano: só entram as que o Wikidata conhece e com site oficial.
            var ids = new List<(string Name, string Id)>();
            foreach (var name in plan.Companies.Take(deep ? 20 : 8))
            {
                var id = await CachedAsync($"wd-search-{Hash(name)}", TimeSpan.FromDays(30), async () => (await SearchAsync(name, ct)).FirstOrDefault() ?? "");
                queries++;
                if (id.Length > 0) ids.Add((name, id));
            }
            foreach (var chunk in ids.Chunk(20))
            {
                var body = await CachedAsync($"wd-sites-{Hash(string.Join(",", chunk.Select(c => c.Id)))}", TimeSpan.FromDays(30),
                    () => GetAsync($"https://www.wikidata.org/w/api.php?action=wbgetentities&props=claims&format=json&ids={string.Join("%7C", chunk.Select(c => c.Id))}", ct));
                queries++;
                using var d = JsonDocument.Parse(body);
                foreach (var (name, id) in chunk)
                    if (d.RootElement.GetProperty("entities").TryGetProperty(id, out var e) && e.TryGetProperty("claims", out var claims) && claims.TryGetProperty("P856", out var p856)
                        && p856.EnumerateArray().Select(x => x.GetProperty("mainsnak").TryGetProperty("datavalue", out var v) ? v.GetProperty("value").GetString() : null).FirstOrDefault(x => x is not null) is { } url
                        && Site(url) is { } site)
                    {
                        found++;
                        if (leads.TryAdd(site, $"achado pelo caçador: {name}, conferida no Wikidata")) added++;
                    }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            note = $"o Wikidata não respondeu tudo agora: {ex.Message}";
            log.LogInformation("Wikidata hunt partial: {Error}", ex.Message);
        }
        await Note(ai, thread, "Garimpeiro", $"Wikidata: {added} sites de empresa para a varredura", note, new { stage = "wikidata", sites = added }, ct);
        return new("Wikidata", queries, found, added, note);
    }

    /// <summary>A busca na web pela API oficial do Brave (chave da pessoa), com as buscas do plano, dentro do teto do dia.</summary>
    private async Task<HuntChannel> WebAsync(IAutomationApi automation, HuntPlan plan, bool deep, Dictionary<string, string> leads, IIntelligenceApi ai, AgentThread thread, CancellationToken ct)
    {
        int queries = 0, found = 0, added = 0;
        foreach (var q in plan.Queries.Take(deep ? 6 : 2))
        {
            if (!Explorer.SpendWebSearch(config.GetValue("Explorer:WebSearchPerDay", 60))) break;
            try
            {
                var results = await automation.WebSearchAsync(q, ct);
                queries++;
                found += results.Count;
                foreach (var l in results) if (leads.TryAdd(l.Source, $"achado pelo caçador na web (\"{q}\")")) added++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException) { log.LogInformation("Hunt web search skipped: {Error}", ex.Message); }
        }
        await Note(ai, thread, "Garimpeiro", $"Busca na web: {added} pistas", null, new { stage = "web", leads = added }, ct);
        return new("Busca na web (Brave)", queries, found, added, null);
    }

    private async Task<IReadOnlyList<string>> SearchAsync(string name, CancellationToken ct)
    {
        using var d = JsonDocument.Parse(await GetAsync($"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={Uri.EscapeDataString(name)}&language=en&type=item&limit=1&format=json", ct));
        return [.. d.RootElement.GetProperty("search").EnumerateArray().Select(x => x.GetProperty("id").GetString() ?? "").Where(x => x.Length > 0)];
    }

    /// <summary>GET com uma segunda tentativa quando o serviço pede calma (429, 503, 504), respeitando o Retry-After.</summary>
    private async Task<string> GetAsync(string url, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Accept.ParseAdd(url.Contains("sparql", StringComparison.Ordinal) ? "application/sparql-results+json" : "application/json");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(75));
            using var res = await Client.SendAsync(req, cts.Token);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
            if (attempt == 0 && (int)res.StatusCode is 429 or 503 or 504)
            {
                await Task.Delay(res.Headers.RetryAfter?.Delta is { } wait && wait < TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(4), ct);
                continue;
            }
            throw new HttpRequestException($"{(int)res.StatusCode} {res.ReasonPhrase}", null, res.StatusCode);
        }
    }

    private static async Task<string> CachedAsync(string key, TimeSpan life, Func<Task<string>> load)
    {
        var path = Path.Combine(Cache, Safe(key) + ".txt");
        if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < life) return await File.ReadAllTextAsync(path);
        var body = await load();
        await File.WriteAllTextAsync(path, body);
        return body;
    }

    /// <summary>As pistas vão para a caixa de entrada do Explorador, que as põe na fila com prioridade no próximo ciclo.</summary>
    internal static void Enqueue(IReadOnlyDictionary<string, string> leads)
    {
        var inbox = Explorer.Read<Dictionary<string, string>>(InboxFile) ?? [];
        foreach (var (source, origin) in leads) inbox.TryAdd(source, origin);
        File.WriteAllText(InboxFile, JsonSerializer.Serialize(inbox.Take(8000).ToDictionary(), JsonSerializerOptions.Web));
    }

    /// <summary>O site oficial vira a pista da varredura (web:dominio), ou o quadro, quando o site já é a página do ATS.</summary>
    internal static string? Site(string? url)
    {
        if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https")) return null;
        if (SourceKeys.Board(url) is { } board) return board;
        var host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        return host.Contains('.') ? $"web:{host.ToLowerInvariant()}" : null;
    }

    /// <summary>As páginas de uma resposta do índice (uma linha JSON por página).</summary>
    internal static IReadOnlyList<string> Pages(string body)
    {
        var pages = new List<string>();
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith('{')) continue;
            try { using var d = JsonDocument.Parse(line); if (d.RootElement.TryGetProperty("url", out var u) && u.GetString() is { } page) pages.Add(page); }
            catch (JsonException) { /* linha cortada: fica de fora */ }
        }
        return pages;
    }

    /// <summary>Cada página de vaga vira o quadro da empresa (uma pista por quadro), com a palavra que bateu no endereço.</summary>
    internal static IEnumerable<(string Board, string Origin)> FromPages(IEnumerable<string> pages, string[] slugs, string domain) =>
        pages.Select(p => (Board: SourceKeys.Board(p), Page: p)).Where(x => x.Board is not null).DistinctBy(x => x.Board, StringComparer.OrdinalIgnoreCase)
            .Select(x => (x.Board!, $"achado pelo caçador no Common Crawl ({Word(x.Page, slugs)} em {domain})"));

    private static string Word(string page, string[] slugs) => slugs.FirstOrDefault(s => page.Contains(s, StringComparison.OrdinalIgnoreCase)) ?? slugs[0];
    private static string Hash(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..16];
    private static string Safe(string s) => Regex.Replace(s, "[^A-Za-z0-9._-]", "_");

    private static HuntSummary Skip(string depth, string reason) => new(DateTime.UtcNow, depth, null, "", [], 0, [], 0, reason);
    private static HuntSummary Save(HuntSummary s) { File.WriteAllText(LastFile, JsonSerializer.Serialize(s, JsonSerializerOptions.Web)); return s; }

    internal static object Snapshot(IConfiguration config, bool running) => new
    {
        running,
        enabled = config.GetValue("Explorer:Hunter:Enabled", false),
        intervalHours = config.GetValue("Explorer:Hunter:IntervalHours", 12),
        depth = config.GetValue("Explorer:Hunter:Depth", "rapido"),
        last = Explorer.Read<HuntSummary>(LastFile),
        queued = Explorer.Read<Dictionary<string, string>>(InboxFile)?.Count ?? 0,
    };

    private async Task Note(IIntelligenceApi ai, AgentThread thread, string agent, string title, string? body, object detail, CancellationToken ct)
    {
        try { await ai.NoteAsync(thread, agent, title, body, detail, "Explorador", true, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogInformation("Hunter note failed: {Error}", ex.Message); }
    }
}

/// <summary>POST /api/explore/hunt (caçar agora, em segundo plano), GET /api/explore/hunt (estado e a última caçada), PUT /api/explore/hunt/settings.</summary>
internal sealed class HunterEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/explore/hunt").WithTags("Operations");
        g.MapGet("", async (IConfiguration config, Hunter hunter, CancellationToken ct) => Results.Ok(Hunter.Snapshot(config, await hunter.RunningAsync(ct))));
        g.MapPost("", async (HuntInput? body, Hunter hunter, CancellationToken ct) =>
            await hunter.StartAsync(body?.Depth ?? "rapido", ct) ? Results.Accepted() : Results.Problem(statusCode: 409, title: "running", detail: "Já tem uma caçada rodando."));
        g.MapPut("settings", async (HuntSettings body) =>
        {
            await UserSettings.SetAsync(new Dictionary<string, object?>
            {
                ["Explorer:Hunter:Enabled"] = body.Enabled,
                ["Explorer:Hunter:IntervalHours"] = Math.Clamp(body.IntervalHours, 1, 168),
                ["Explorer:Hunter:Depth"] = body.Depth == "profundo" ? "profundo" : "rapido",
            });
            return Results.NoContent();
        });
    }
}
