using System.Diagnostics;
using System.Globalization;
using System.Text;
using JobOrchestrator.Automation.Contracts;
using JobOrchestrator.Campaigns.Contracts;
using JobOrchestrator.Intelligence.Contracts;
using JobOrchestrator.Jobs.Contracts;

namespace JobOrchestrator.Operations.Features;

internal sealed record Verdict(string Source, string Origin, int Total, int Relevant, IReadOnlyList<string> Sample, bool Fit, string Reason, DateTime At);
internal sealed record ExploreSummary(DateTime At, int Candidates, int Tested, IReadOnlyList<Verdict> Verdicts, IReadOnlyList<string> Added, string? Skipped,
    int Frontier = 0, IReadOnlyDictionary<string, int>? Origins = null, int Swept = 0, double Seconds = 0);
internal sealed record ExploreInput(string[] Sources);
internal sealed record ExploreSettings(bool Enabled, int IntervalMinutes, int SearchIntervalMinutes);

/// <summary>
/// Explorador: acha fontes e vagas novas sem você passar link, um ciclo a cada 10 minutos no Worker. As pistas vêm dos links de
/// candidatura das vagas que chegaram (cada agregador aponta para o quadro da empresa), da busca da Gupy e das APIs públicas pelas suas
/// palavras, dos repositórios do GitHub com issues de vaga recentes, das listas de empresas mantidas em READMEs e da varredura do
/// coletor (Scrapy, mais o Firecrawl quando ligado) nos sites das empresas. Toda pista entra numa fila e é lida pela porta oficial;
/// fica na campanha só a que tem vaga recente da sua área, confirmada pelo Llama local, e as vagas dela chegam na mesma hora.
/// Fonte vista só volta a ser testada depois de 7 dias.
/// </summary>
internal sealed class Explorer(IServiceScopeFactory scopes, [FromKeyedServices("operations")] NpgsqlDataSource data, IConfiguration config, ILogger<Explorer> log)
{
    private static string Folder => AppPaths.In(AppPaths.Data, "explorer");
    private static string SeenFile => Path.Combine(Folder, "explored.json");
    private static string FrontierFile => Path.Combine(Folder, "frontier.json");
    private static string ListsFile => Path.Combine(Folder, "lists.json");
    public static string LastFile => Path.Combine(Folder, "last.json");

    /// <summary>Listas de empresas lidas pelo Explorador (README no GitHub), relidas a cada 7 dias. Explorer:Lists troca a lista.</summary>
    internal static readonly string[] DefaultLists = ["lerrua/remote-jobs-brazil", "poteto/hiring-without-whiteboards", "yanirs/established-remote"];
    /// <summary>APIs públicas que buscam por texto: cada palavra (e cargo, onde a busca entende frase) vira uma fonte candidata. A Remotive pede
    /// poucas chamadas por dia: entra só com as duas primeiras palavras e só se a campanha ainda não tiver uma busca nela.</summary>
    private static readonly (string Api, bool Titles)[] SearchApis = [("getonbrd", true), ("himalayas", true), ("remotive", false)];

    public async Task<ExploreSummary> CycleAsync(CancellationToken ct)
    {
        await using var gate = await data.OpenConnectionAsync(ct);
        // API (botão) e Worker (ciclo) nunca exploram ao mesmo tempo: a trava fica com esta conexão até o fim.
        if (!await gate.ExecuteScalarAsync<bool>("select pg_try_advisory_lock(4747030)")) return new(DateTime.UtcNow, 0, 0, [], [], "Já tem uma exploração rodando.");
        try { return await RunAsync(ct); }
        finally { await gate.ExecuteAsync("select pg_advisory_unlock(4747030)"); }
    }

    /// <summary>Se um ciclo está rodando agora, na API ou no Worker: a trava do Postgres é a mesma.</summary>
    public async Task<bool> RunningAsync(CancellationToken ct)
    {
        await using var c = await data.OpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<bool>("select exists(select 1 from pg_locks where locktype = 'advisory' and classid = 0 and objid = 4747030 and granted)");
    }

    private async Task<ExploreSummary> RunAsync(CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var jobs = sp.GetRequiredService<IJobsApi>();
        var automation = sp.GetRequiredService<IAutomationApi>();
        var ai = sp.GetRequiredService<IIntelligenceApi>();
        var runs = sp.GetRequiredService<ICampaignRuns>();
        var campaign = (await sp.GetRequiredService<ICampaignsApi>().ListAsync(ct)).FirstOrDefault();
        if (campaign is null || campaign.Technologies.Count == 0) return Save(new(DateTime.UtcNow, 0, 0, [], [], "Sem campanha com palavras-chave."));
        var max = config.GetValue("Explorer:MaxSources", 300);
        if (campaign.Sources.Count >= max) return Save(new(DateTime.UtcNow, 0, 0, [], [], $"A campanha já tem {campaign.Sources.Count} fontes (teto {max})."));

        var thread = AgentThread.New("explore", "Explorador: fontes novas");
        var seen = Read<Dictionary<string, Verdict>>(SeenFile) ?? [];
        var frontier = Read<Dictionary<string, string>>(FrontierFile) ?? [];
        var known = campaign.Sources.ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Fresh(string s) => !known.Contains(s) && !(seen.TryGetValue(s, out var v) && DateTime.UtcNow - v.At < TimeSpan.FromDays(7));
        var origins = new Dictionary<string, int>();
        void Lead(string source, string origin, string kind)
        {
            if (!Fresh(source) || !frontier.TryAdd(source, origin)) return;
            origins[kind] = origins.GetValueOrDefault(kind) + 1;
        }
        // As buscas giram entre os ciclos: cada ciclo pergunta uma coisa diferente, sem repetir a mesma busca a cada 10 minutos.
        var turn = (int)(DateTime.UtcNow.Ticks / TimeSpan.FromMinutes(10).Ticks % 100_000);
        var terms = campaign.Technologies.Take(4).Concat((campaign.Titles ?? []).Take(3)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // 1. Links de candidatura das vagas que já chegaram (e o conjunto delas, para saber se uma fonte nova traz vaga nova de verdade).
        var links = await jobs.ApplyLinksAsync(5000, ct);
        var arrived = links.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var board in links.Take(800).Select(SourceKeys.Board).OfType<string>()) Lead(board, "links das vagas", "links das vagas");
        // 2. APIs públicas pelas palavras e cargos.
        foreach (var term in terms)
            foreach (var (api, titles) in SearchApis)
                if ((titles || campaign.Technologies.Take(2).Contains(term, StringComparer.OrdinalIgnoreCase)) && (titles || !campaign.Sources.Any(x => x.StartsWith($"{api}:", StringComparison.OrdinalIgnoreCase))))
                    Lead($"{api}:{term.ToLowerInvariant()}", $"API {api} por \"{term}\"", "APIs públicas");
        // 3. Busca da Gupy: duas palavras por ciclo.
        foreach (var term in Rotate(terms, turn, 2))
        {
            try { foreach (var c in await automation.SearchGupyAsync(term, ct)) Lead($"gupy:{c}", $"busca da Gupy por \"{term}\"", "busca da Gupy"); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { log.LogInformation("Gupy search skipped for {Term}: {Error}", term, ex.Message); }
            await Task.Delay(1500, ct);
        }
        // 4. Repositórios do GitHub com issues de vaga do último mês: uma busca por ciclo.
        var queries = new[] { "vaga in:title", "vagas in:title" }.Concat(campaign.Technologies.Take(2).Select(t => $"\"{t}\" vaga")).ToList();
        try { foreach (var l in await automation.JobReposAsync(queries[turn % queries.Count], ct)) Lead(l.Source, l.Origin, "repositórios do GitHub"); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { log.LogInformation("GitHub search skipped: {Error}", ex.Message); }
        // 5. Listas de empresas: uma por ciclo, relida a cada 7 dias.
        var lists = Read<Dictionary<string, DateTime>>(ListsFile) ?? [];
        var due = (config.GetSection("Explorer:Lists").Get<string[]>() is { Length: > 0 } custom ? custom : DefaultLists)
            .FirstOrDefault(r => !lists.TryGetValue(r, out var at) || DateTime.UtcNow - at > TimeSpan.FromDays(7));
        if (due is not null)
        {
            try
            {
                foreach (var l in await automation.CompanyListAsync(due, ct)) Lead(l.Source, l.Origin, "listas de empresas");
                lists[due] = DateTime.UtcNow;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { log.LogInformation("Company list {Repo} skipped: {Error}", due, ex.Message); }
        }
        // 6. Sites das empresas das vagas.
        foreach (var d in await jobs.CompanyDomainsAsync(60, ct)) Lead($"web:{d}", "site da empresa de uma vaga", "sites de empresa");
        foreach (var k in frontier.Keys.Where(k => !Fresh(k)).ToList()) frontier.Remove(k);
        // A fila vai para o disco antes dos testes: se o processo cair no meio do ciclo, as pistas ficam.
        Write(FrontierFile, frontier.Take(5000).ToDictionary());
        Write(ListsFile, lists);

        var batch = frontier.Where(f => Kind(f.Key) == "board").OrderBy(f => Priority(f.Value)).Take(config.GetValue("Explorer:BoardsPerCycle", 10))
            .Concat(frontier.Where(f => Kind(f.Key) == "github").Take(config.GetValue("Explorer:ReposPerCycle", 3)))
            .Concat(frontier.Where(f => Kind(f.Key) == "gupy").Take(config.GetValue("Explorer:GupyPerCycle", 4)))
            .Select(f => (Source: f.Key, Origin: f.Value)).ToList();
        var sites = frontier.Where(f => Kind(f.Key) == "web").OrderBy(f => Priority(f.Value)).Take(config.GetValue("Explorer:SitesPerCycle", 6))
            .Select(f => (Source: f.Key, Origin: f.Value)).ToList();
        await Note(ai, thread, "Garimpeiro", $"{frontier.Count} pistas na fila, {batch.Count + sites.Count} para testar agora",
            origins.Count > 0 ? "Novas neste ciclo: " + string.Join(", ", origins.Select(o => $"{o.Value} de {o.Key}")) + "." : "Nenhuma pista nova neste ciclo; seguem as da fila.",
            new { stage = "start", frontier = frontier.Count, batch = batch.Count, sites = sites.Count, origins }, ct);

        var verdicts = new List<Verdict>();
        async Task TestAsync(string source, string origin)
        {
            var (v, relevant) = await JudgeAsync(automation, ai, campaign, source, origin, thread, ct, arrived);
            verdicts.Add(v);
            seen[source] = v;
            frontier.Remove(source);
            // A vaga da sua área aponta para o quadro da empresa: esse quadro vira pista do próximo ciclo.
            foreach (var board in relevant.Select(p => SourceKeys.Board(p.ApplyUrl)).OfType<string>().Distinct()) Lead(board, $"link de vaga em {source}", "links das vagas");
        }
        foreach (var (source, origin) in batch) await TestAsync(source, origin);

        if (sites.Count > 0)
        {
            IReadOnlyList<SiteFinding> found = [];
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(config.GetValue("Scraper:MaxSeconds", 240) + 240));
                found = await automation.SweepSitesAsync([.. sites.Select(s => s.Source[4..])], timeout.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or IOException) { log.LogInformation("Sweep failed: {Error}", ex.Message); }
            var boards = new List<string>();
            foreach (var (source, origin) in sites)
            {
                frontier.Remove(source);
                var site = found.FirstOrDefault(f => $"web:{f.Domain}".Equals(source, StringComparison.OrdinalIgnoreCase));
                foreach (var b in site?.Boards ?? []) if (Fresh(b) && frontier.TryAdd(b, $"achado em {source[4..]}")) boards.Add(b);
                // Página que lista vaga de muitas empresas é agregador ou exemplo de documentação, não o site de carreira da empresa.
                var companies = site?.Postings.Select(p => Fold(p.Company).Trim()).Where(c => c.Length > 0).Distinct().Count() ?? 0;
                var (v, _) = site is null
                    ? (Miss(source, origin, "a varredura não terminou"), [])
                    : companies > 2
                        ? (Miss(source, origin, $"a página lista vagas de {companies} empresas diferentes (agregador ou exemplo), não as da própria empresa"), [])
                    : site.Postings.Count == 0
                        ? (Miss(source, origin, site.Boards.Count > 0 ? $"sem vaga no próprio site; o quadro {string.Join(", ", site.Boards.Take(2))} segue para o teste" : "nenhuma vaga publicada (ou o robots.txt não deixa ler)"), [])
                        : await WeighAsync(ai, campaign, source, origin, site.Postings, thread, ct, arrived);
                verdicts.Add(v);
                seen[source] = v;
            }
            await Note(ai, thread, "Coletor", $"{sites.Count} {(sites.Count == 1 ? "site varrido" : "sites varridos")}, {boards.Count} {(boards.Count == 1 ? "quadro de ATS achado" : "quadros de ATS achados")}",
                string.Join("\n", sites.Select(s => s.Source[4..])), new { stage = "sweep", sites = sites.Select(s => s.Source[4..]), boards }, ct);
            // Quadro achado no site é lido na hora pela API oficial dele (rápido); o resto fica para o próximo ciclo.
            foreach (var b in boards.Take(4)) await TestAsync(b, frontier.GetValueOrDefault(b, "achado na varredura"));
        }

        var fit = verdicts.Where(v => v.Fit).Select(v => v.Source).Take(Math.Max(0, max - campaign.Sources.Count)).ToList();
        var added = fit.Count == 0 ? 0 : await runs.AddSourcesAsync(campaign.Id, fit, ct);
        var fetching = added > 0 && await runs.RequestSourcesAsync(campaign.Id, fit, "explorer", ct);
        Write(SeenFile, seen.OrderByDescending(x => x.Value.At).Take(8000).ToDictionary());
        Write(FrontierFile, frontier.Take(5000).ToDictionary());
        var seconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
        await Note(ai, thread, "Curador", added > 0 ? $"{added} {(added == 1 ? "fonte nova entrou" : "fontes novas entraram")} na campanha" : "Nenhuma fonte nova com vaga da sua área neste ciclo",
            (fetching ? "As vagas delas já estão chegando na Busca ao vivo.\n" : "") +
            string.Join("\n", verdicts.Select(v => $"{(v.Fit ? "entra" : "fica de fora")}: {v.Source} ({v.Relevant} de {v.Total} da sua área). {v.Reason}")),
            new { stage = "done", tested = verdicts.Count, added = fit, fetching, frontier = frontier.Count, seconds, verdicts = verdicts.Select(v => new { v.Source, v.Origin, v.Total, v.Relevant, v.Fit, v.Reason }) }, ct);
        return Save(new(DateTime.UtcNow, frontier.Count + verdicts.Count, verdicts.Count, verdicts, fit, null, frontier.Count, origins, sites.Count, seconds));
    }

    /// <summary>Avaliação avulsa (fontes que você sugerir): o mesmo juiz, sem adicionar.</summary>
    public async Task<IReadOnlyList<Verdict>> EvaluateAsync(IReadOnlyList<string> sources, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var campaign = (await scope.ServiceProvider.GetRequiredService<ICampaignsApi>().ListAsync(ct)).FirstOrDefault();
        if (campaign is null) return [];
        var automation = scope.ServiceProvider.GetRequiredService<IAutomationApi>();
        var ai = scope.ServiceProvider.GetRequiredService<IIntelligenceApi>();
        var thread = AgentThread.New("explore", "Explorador: avaliação");
        var seen = Read<Dictionary<string, Verdict>>(SeenFile) ?? [];
        var list = new List<Verdict>();
        foreach (var s in sources.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().Take(40))
        {
            var (v, _) = await JudgeAsync(automation, ai, campaign, s, "pedido", thread, ct);
            list.Add(v);
            seen[v.Source] = v;
        }
        Write(SeenFile, seen);
        return list;
    }

    private async Task<(Verdict, IReadOnlyList<DiscoveredPosting>)> JudgeAsync(IAutomationApi automation, IIntelligenceApi ai, CampaignSettings c, string source, string origin, AgentThread thread, CancellationToken ct,
        IReadOnlySet<string>? arrived = null)
    {
        IReadOnlyList<DiscoveredPosting> postings;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(source.StartsWith("web:") ? 300 : 75));
            postings = await automation.DiscoverAsync(source, [.. c.Technologies, .. c.Titles ?? []], timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            var miss = Miss(source, origin, ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound } ? "não existe nesta plataforma" : "não respondeu");
            log.LogInformation("Explorer: {Source} failed: {Error}", source, ex.Message);
            await Note(ai, thread, "Juiz", $"{source}: {miss.Reason}", null, new { stage = "tested", source, origin, total = 0, relevant = 0, fit = false }, ct, ok: false);
            return (miss, []);
        }
        return await WeighAsync(ai, c, source, origin, postings, thread, ct, arrived);
    }

    /// <summary>Conta as vagas recentes da sua área e, se houver, pede ao Llama local para confirmar que a fonte contrata para ela.</summary>
    private async Task<(Verdict, IReadOnlyList<DiscoveredPosting>)> WeighAsync(IIntelligenceApi ai, CampaignSettings c, string source, string origin, IReadOnlyList<DiscoveredPosting> postings, AgentThread thread, CancellationToken ct,
        IReadOnlySet<string>? arrived = null)
    {
        var since = DateTime.UtcNow.AddDays(-config.GetValue("Explorer:FreshDays", 120));
        var essential = c.Technologies.Take(2).Select(Fold).ToList();
        var relevant = postings.Where(p => (p.UpdatedAt is null || p.UpdatedAt >= since) && essential.Any(k => Fold($" {p.Title} {p.Description} ").Contains(k, StringComparison.Ordinal))).ToList();
        var sample = relevant.Select(p => p.Title).Distinct().Take(5).ToList();
        // Fonte que só repete vaga que já chega por outra (a mesma API com outra palavra, o site que aponta para o quadro já lido) não entra.
        var novel = arrived is null ? relevant.Count : relevant.Count(p => !arrived.Contains(p.ApplyUrl));
        var fit = novel > 0;
        var reason = fit ? $"{novel} vagas recentes citam {string.Join(" ou ", c.Technologies.Take(2))}{(novel < relevant.Count ? $" ({relevant.Count - novel} já chegavam por outra fonte)" : "")}."
            : relevant.Count > 0 ? $"As {relevant.Count} vagas da sua área já chegam por outra fonte." : postings.Count > 0 ? "Nenhuma vaga recente cita as palavras essenciais." : "Sem vaga aberta agora.";
        if (fit)
        {
            // Só títulos públicos de vaga e os critérios da busca; nada sai da máquina.
            var prompt = $"A pessoa procura vagas de: {c.Name}. Cargos-alvo: {string.Join(", ", c.Titles ?? [])}. Palavras: {string.Join(", ", c.Technologies)}. Locais: {string.Join(", ", c.Locations)}.\n" +
                $"Fonte: {source} ({relevant.FirstOrDefault()?.Company}). Vagas que citam as palavras essenciais:\n- {string.Join("\n- ", sample)}";
            var res = await ai.CompleteAsync(LlmRequest.For(LlmTasks.ExploreJudge, JudgeSystem, prompt, JudgeSchema, timeoutSeconds: 60, thread: thread, agent: "Curador"), ct);
            if (res.Ok && res.Json is { } json)
            {
                try
                {
                    using var d = JsonDocument.Parse(json);
                    fit = d.RootElement.GetProperty("fit").GetBoolean();
                    reason = d.RootElement.TryGetProperty("reason", out var r) && r.GetString() is { Length: > 0 } why ? why : reason;
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { /* fica a regra das palavras */ }
            }
        }
        await Note(ai, thread, "Juiz", $"{source}: {relevant.Count} de {postings.Count} da sua área", reason,
            new { stage = "tested", source, origin, total = postings.Count, relevant = relevant.Count, fit }, ct, ok: fit);
        return (new Verdict(source, origin, postings.Count, relevant.Count, sample, fit, reason, DateTime.UtcNow), relevant);
    }

    private static Verdict Miss(string source, string origin, string reason) => new(source, origin, 0, 0, [], false, reason, DateTime.UtcNow);

    private const string JudgeSystem = "Você confere se uma fonte de vagas serve para a busca de uma pessoa. fit=true quando as vagas listadas são da área e do tipo de cargo que ela procura; " +
        "fit=false quando só citam a palavra de passagem (outra área, estágio sem relação, vaga presencial em outro país). reason: uma frase curta em português. Responda só o JSON.";
    private const string JudgeSchema = """{"type":"object","properties":{"fit":{"type":"boolean"},"reason":{"type":"string"}},"required":["fit","reason"],"additionalProperties":false}""";

    internal static string Kind(string source) => source.Split(':')[0].ToLowerInvariant() switch { "gupy" => "gupy", "github" => "github", "web" => "web", _ => "board" };

    /// <summary>Pista que veio de vaga real (link ou varredura) vai primeiro; depois as APIs; as listas por último.</summary>
    private static int Priority(string origin) =>
        origin.StartsWith("links", StringComparison.Ordinal) || origin.StartsWith("link de vaga", StringComparison.Ordinal) || origin.StartsWith("achado", StringComparison.Ordinal) ? 0
        : origin.StartsWith("lista", StringComparison.Ordinal) ? 2 : 1;

    internal static IEnumerable<string> Rotate(IReadOnlyList<string> items, int turn, int take) =>
        items.Count == 0 ? [] : Enumerable.Range(0, Math.Min(take, items.Count)).Select(i => items[(turn * take + i) % items.Count]).Distinct();

    private static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string([.. d.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)]);
    }

    internal static T? Read<T>(string file) where T : class
    {
        try { return System.IO.File.Exists(file) ? JsonSerializer.Deserialize<T>(System.IO.File.ReadAllText(file), JsonSerializerOptions.Web) : null; }
        catch (JsonException) { return null; }
    }
    private static void Write<T>(string file, T value) => System.IO.File.WriteAllText(file, JsonSerializer.Serialize(value, JsonSerializerOptions.Web));
    private static ExploreSummary Save(ExploreSummary s) { Write(LastFile, s); return s; }

    /// <summary>O que a tela mostra: o último ciclo, a fila por tipo e as descobertas recentes.</summary>
    internal static object Snapshot(IConfiguration config, bool running)
    {
        var frontier = Read<Dictionary<string, string>>(FrontierFile) ?? [];
        var seen = Read<Dictionary<string, Verdict>>(SeenFile) ?? [];
        var last = Read<ExploreSummary>(LastFile);
        var every = config.GetValue("Explorer:IntervalMinutes", 10);
        return new
        {
            enabled = config.GetValue("Explorer:Enabled", true), intervalMinutes = every, searchIntervalMinutes = config.GetValue("Search:IntervalMinutes", 60), running,
            last, next = last is null ? (DateTime?)null : last.At.AddMinutes(every),
            frontier = frontier.Count, queue = frontier.GroupBy(f => Kind(f.Key)).ToDictionary(g => g.Key, g => g.Count()),
            explored = seen.Count, fit = seen.Values.Count(v => v.Fit),
            recent = seen.Values.OrderByDescending(v => v.At).Take(40),
        };
    }

    private async Task Note(IIntelligenceApi ai, AgentThread thread, string agent, string title, string? body, object data, CancellationToken ct, bool ok = true)
    {
        try { await ai.NoteAsync(thread, agent, title, body, data, "Curador", ok, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogInformation("Explorer note failed: {Error}", ex.Message); }
    }
}

/// <summary>
/// No Worker: o Explorador roda um ciclo a cada 10 minutos (Explorer:Enabled, Explorer:IntervalMinutes) e a busca contínua pede rodada das
/// campanhas ligadas a cada hora (Search:IntervalMinutes). Nada disso envia candidatura: o modo da campanha continua valendo.
/// </summary>
internal sealed class Continuous(Explorer explorer, IServiceScopeFactory scopes, IConfiguration config, ILogger<Continuous> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stop);
        // Dois laços independentes: um ciclo longo do Explorador nunca atrasa a rodada da campanha.
        await Task.WhenAll(SearchAsync(stop), ExploreAsync(stop));
    }

    private async Task SearchAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var every = TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Search:IntervalMinutes", 60), 30, 1440));
                var asked = await scope.ServiceProvider.GetRequiredService<ICampaignRuns>().RequestDueAsync(every, "continuous", stop);
                if (asked > 0) log.LogInformation("Continuous search requested {Count} runs", asked);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Continuous search failed: {Error}", ex.Message); }
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }

    private async Task ExploreAsync(CancellationToken stop)
    {
        var last = Explorer.Read<ExploreSummary>(Explorer.LastFile)?.At ?? DateTime.MinValue;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var every = TimeSpan.FromMinutes(Math.Clamp(config.GetValue("Explorer:IntervalMinutes", 10), 5, 1440));
                if (config.GetValue("Explorer:Enabled", true) && DateTime.UtcNow - last >= every)
                {
                    last = DateTime.UtcNow;
                    var s = await explorer.CycleAsync(stop);
                    log.LogInformation("Explorer: {Tested} tested, {Added} added, {Frontier} in the queue", s.Tested, s.Added.Count, s.Frontier);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Explorer cycle failed: {Error}", ex.Message); }
            await Task.Delay(TimeSpan.FromMinutes(1), stop);
        }
    }
}

internal sealed class ExplorerEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/explore").WithTags("Operations");
        g.MapPost("run", async (Explorer explorer, CancellationToken ct) => Results.Ok(await explorer.CycleAsync(ct)));
        g.MapPost("evaluate", async (ExploreInput body, Explorer explorer, CancellationToken ct) => Results.Ok(await explorer.EvaluateAsync(body.Sources ?? [], ct)));
        g.MapGet("status", async (IConfiguration config, Explorer explorer, CancellationToken ct) => Results.Ok(Explorer.Snapshot(config, await explorer.RunningAsync(ct))));
        g.MapPut("settings", async (ExploreSettings body) =>
        {
            await UserSettings.SetAsync(new Dictionary<string, object?>
            {
                ["Explorer:Enabled"] = body.Enabled,
                ["Explorer:IntervalMinutes"] = Math.Clamp(body.IntervalMinutes, 5, 1440),
                ["Search:IntervalMinutes"] = Math.Clamp(body.SearchIntervalMinutes, 30, 1440),
            });
            return Results.NoContent();
        });
    }
}
