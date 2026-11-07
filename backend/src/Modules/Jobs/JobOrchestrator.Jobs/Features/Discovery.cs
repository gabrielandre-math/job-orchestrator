using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using JobOrchestrator.Intelligence.Contracts;

namespace JobOrchestrator.Jobs.Features;

/// <summary>
/// Consumidor da rodada (agente Batedor + Normalizador + Juiz + Analista): busca em cada fonte, confere abertura,
/// deduplica pela identidade canônica, avalia e põe na fila o que passou. A rede vem antes da transação; a gravação é atômica.
/// Cada etapa é narrada no fio "search" com números (Data), para a Busca ao vivo desenhar o progresso.
/// </summary>
internal sealed class RunRequestedConsumer(
    JobsDbContext db, ICampaignsApi campaigns, ICampaignRuns runs, IAutomationApi automation, IIntelligenceApi ai, Profile.Contracts.IProfileApi profiles, TimeProvider time,
    ILogger<RunRequestedConsumer> log) : IEventConsumer
{
    public string EventType => RunRequestedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        var run = message.Read<RunRequestedV1>();
        var campaign = await campaigns.GetAsync(run.CampaignId, ct);
        var profile = await profiles.CurrentAsync(ct);
        var policy = Policy(profile);
        var prefs = profile?.Confirmations is { ConfirmedAt: not null } c
            ? new Preferences(c.Modalities, c.FloorCltBrl, c.FloorPjBrl, c.FloorUsdMonth) : Preferences.None;
        if (campaign is null) return;

        var found = new List<(DiscoveredPosting Posting, PostingStatus Status)>();
        var tombstones = new List<DiscoveredPosting>();
        var ignored = 0;
        var clock = Stopwatch.StartNew();
        var sources = run.Only is { Count: > 0 } only ? [.. campaign.Sources.Where(x => only.Contains(x, StringComparer.OrdinalIgnoreCase))] : campaign.Sources;
        var thread = AgentThread.New("search", run.Only is { Count: > 0 } ? $"Busca nas fontes novas: {campaign.Name}" : $"Busca: {campaign.Name}");
        await SayAsync(thread, "Batedor", $"{sources.Count} {(sources.Count == 1 ? "fonte" : "fontes")} para consultar{(run.Only is { Count: > 0 } ? " (achadas agora pelo Explorador)" : "")}", string.Join("\n", sources.Select(Label)),
            new { stage = "start", runId = run.RunId, sources, cut = campaign.MinFitScore, partial = run.Only is { Count: > 0 } }, "Normalizador", true, ct);
        var health = SourceHealth.Load();
        foreach (var source in sources)
        {
            var watch = Stopwatch.StartNew();
            await SayAsync(thread, "Batedor", $"Consultando {Label(source)}", null, new { stage = "source", source, state = "working" }, null, true, ct);
            int listed = 0, kept = 0;
            try
            {
                var postings = await automation.DiscoverAsync(source, [.. campaign.Technologies, .. campaign.Titles ?? []], ct);
                listed = postings.Count;
                foreach (var p in postings)
                {
                    // Encerramento declarado pela fonte: só fecha a vaga que já existe (depois, na transação), sem filtro de relevância.
                    if (p.Tombstone) { tombstones.Add(p); continue; }
                    // Sem nenhuma palavra essencial da campanha, a vaga nem entra: boards grandes têm centenas de vagas de outras áreas.
                    if (!Scoring.Relevant(campaign, p.Title, p.Description)) { ignored++; continue; }
                    kept++;
                    // A page that still shows an ad whose structured data says it ended does not keep it open.
                    found.Add((p, p.ValidThrough is { } until && until < time.GetUtcNow().UtcDateTime ? new PostingStatus(false, $"O anúncio venceu em {until:dd/MM/yyyy}, pelo dado estruturado da página.")
                        : p.ListedAsOpen ? new PostingStatus(true, "Listada agora pela API oficial.") : await automation.CheckOpenAsync(p.ApplyUrl, ct)));
                }
                health.Remove(source);
                await SayAsync(thread, "Batedor", $"{Label(source)}: {listed} {(listed == 1 ? "vaga" : "vagas")}, {kept} da sua área", null,
                    new { stage = "source", source, state = "done", listed, kept, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, "Normalizador", true, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Uma fonte fora do ar ou fora do formato não derruba a rodada. Só "não existe mais" (404, 410) conta para tirar a fonte; queda passageira não.
                if (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException) log.LogWarning("Source {Source} failed: {Error}", source, ex.Message);
                else log.LogWarning(ex, "Source {Source} answered in an unexpected format", source);
                if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone }) health[source] = health.GetValueOrDefault(source) + 1;
                await SayAsync(thread, "Batedor", $"{Label(source)} não respondeu", ex.Message,
                    new { stage = "source", source, state = "failed", listed, kept, error = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, "Normalizador", false, ct);
            }
        }

        // Quadro morto: a mesma fonte sumiu (404) em 5 rodadas seguidas. Sai da campanha e a tela diz qual.
        var dead = health.Where(h => h.Value >= 5).Select(h => h.Key).ToList();
        if (dead.Count > 0 && await runs.RemoveSourcesAsync(campaign.Id, dead, ct) > 0)
        {
            foreach (var d in dead) health.Remove(d);
            await SayAsync(thread, "Batedor", $"{dead.Count} {(dead.Count == 1 ? "fonte saiu do ar e foi tirada" : "fontes saíram do ar e foram tiradas")} da campanha",
                string.Join("\n", dead.Select(Label)), new { stage = "pruned", sources = dead }, null, false, ct);
        }
        SourceHealth.Save(health);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
        // Encerradas pela fonte: a vaga que existe vira Expired (o histórico fica); a que não existe é ignorada.
        var keys = tombstones.Select(t => t.ExternalId).Distinct().ToList();
        var known = keys.Count == 0 ? [] : await db.Postings.Where(x => x.CampaignId == campaign.Id && keys.Contains(x.ExternalId)).ToListAsync(ct);
        var closed = Close(known, tombstones, now);
        int shortlisted = 0, fresh = 0, again = 0, duplicates = 0, scored = 0;
        var top = new List<(string Company, string Title, int Score, string? Domain, Guid Id)>();
        var bands = new int[10];
        foreach (var (p, status) in found)
        {
            var posting = await db.Postings.FirstOrDefaultAsync(x => x.CampaignId == campaign.Id && x.Source == p.Source && x.ExternalId == p.ExternalId, ct);
            if (posting is null)
            {
                posting = JobPosting.Discover(campaign.Id, p, now);
                var original = await db.Postings.Where(x => x.CampaignId == campaign.Id && x.CanonicalKey == posting.CanonicalKey).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                    ?? db.Postings.Local.FirstOrDefault(x => x.CampaignId == campaign.Id && x.CanonicalKey == posting.CanonicalKey)?.Id;
                if (original is not null) { posting.MarkDuplicate(original.Value); duplicates++; } else fresh++;
                db.Postings.Add(posting);
            }
            else { posting.SeenAgain(p, now); again++; }

            await SnapshotAsync(posting.Id, p.RawJson, now, ct);
            posting.Verified(status, now);
            var passed = posting.Evaluate(Scoring.Evaluate(campaign, posting.Title, posting.Description, posting.Location, posting.Modality, prefs,
                new JobPay(posting.SalaryMin, posting.SalaryMax, posting.SalaryCurrency, posting.Contract)), campaign.MinFitScore, policy);
            if (posting.DuplicateOf is null && posting.Score is { } score) { scored++; bands[Math.Clamp(score / 10, 0, 9)]++; }
            if (passed)
            {
                shortlisted++;
                top.Add((posting.Company, posting.Title, posting.Score!.Value, posting.CompanyDomain, posting.Id));
                db.Publish(JobShortlistedV1.EventType, new JobShortlistedV1(posting.Id, campaign.Id, posting.CanonicalKey, posting.Company, posting.Title, posting.Location, posting.ApplyUrl, posting.Score!.Value, posting.ApplyChannel), now);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        log.LogInformation("Run {Run}: {Found} postings, {Ignored} ignored as off-profile, {Shortlisted} shortlisted", run.RunId, found.Count, ignored, shortlisted);

        if (closed > 0) await SayAsync(thread, "Normalizador", $"{closed} {(closed == 1 ? "vaga encerrada" : "vagas encerradas")} pela própria fonte", "Saem da fila e o histórico fica.",
            new { stage = "closed", closed }, "Juiz", true, ct);
        await SayAsync(thread, "Normalizador", $"{fresh} {(fresh == 1 ? "vaga nova" : "vagas novas")}, {again} já conhecidas, {duplicates} repetidas", null,
            new { stage = "dedupe", found = found.Count, fresh, again, duplicates, ignored }, "Juiz", true, ct);
        var above = bands.Skip(Math.Clamp(campaign.MinFitScore / 10, 0, 9)).Sum();
        await SayAsync(thread, "Juiz", $"{scored} avaliadas, {above} com nota {campaign.MinFitScore} ou mais", shortlisted > 0 ? $"{shortlisted} entram na fila agora." : "As que passaram do corte já estavam na fila.",
            new { stage = "score", scored, above, shortlisted, cut = campaign.MinFitScore, bands }, "Analista", true, ct);
        var best = top.OrderByDescending(t => t.Score).Take(8).Select(t => new { company = t.Company, title = t.Title, score = t.Score, domain = t.Domain, id = t.Id }).ToList();
        await SayAsync(thread, "Analista", shortlisted > 0 ? $"{shortlisted} {(shortlisted == 1 ? "vaga foi" : "vagas foram")} para a fila" : "Nenhuma vaga nova acima do corte",
            shortlisted > 0 ? string.Join("\n", best.Select(b => $"{b.score} · {b.company}: {b.title}")) : "A busca segue nas próximas rodadas.",
            new { stage = "done", found = found.Count, fresh, duplicates, ignored, scored, shortlisted, seconds = Math.Round(clock.Elapsed.TotalSeconds, 1), top = best }, "Você", true, ct);
    }

    /// <summary>
    /// A política de elegibilidade de quem procura, da confirmação do perfil: mora no Brasil quando o local do perfil é no Brasil ou o Brasil está
    /// entre os países autorizados (sem nada dito, vale o Brasil); aceita mudança só quando a pessoa disse que aceita.
    /// </summary>
    internal static EligibilityPolicy Policy(Profile.Contracts.ProfileSnapshot? p)
    {
        if (p is null) return EligibilityPolicy.Default;
        var c = p.Confirmations;
        var here = SourceCatalog.MatchCountries(p.Identity.Location ?? "");
        var authorized = c.AuthorizedCountries.SelectMany(SourceCatalog.MatchCountries).Concat(c.AuthorizedCountries.Where(x => x.Equals("BR", StringComparison.OrdinalIgnoreCase)).Select(_ => "BR")).ToList();
        var brazil = here.Contains("BR") || authorized.Contains("BR") || (here.Count == 0 && authorized.Count == 0);
        return new EligibilityPolicy(brazil, c.Relocate);
    }

    /// <summary>Fecha as vagas que a fonte declarou encerradas (mesma fonte e mesmo id externo). Devolve quantas fecharam agora.</summary>
    internal static int Close(IEnumerable<JobPosting> existing, IReadOnlyCollection<DiscoveredPosting> tombstones, DateTime now)
    {
        var dead = tombstones.Select(t => (t.Source, t.ExternalId)).ToHashSet();
        var count = 0;
        foreach (var posting in existing.Where(x => dead.Contains((x.Source, x.ExternalId)) && x.Status is not (JobStatus.Expired or JobStatus.Duplicate)))
        {
            posting.Verified(new PostingStatus(false, "Encerrada na própria fonte (anúncio inativo)."), now);
            count++;
        }
        return count;
    }

    /// <summary>A narração nunca derruba a rodada: falha ao gravar a nota só vira log.</summary>
    private async Task SayAsync(AgentThread thread, string agent, string title, string? body, object data, string? to, bool ok, CancellationToken ct)
    {
        try { await ai.NoteAsync(thread, agent, title, body, data, to, ok, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning("Search narration failed: {Error}", ex.Message); }
    }

    /// <summary>Nome legível da fonte: "greenhouse:nubank" vira "Greenhouse nubank", "web:empresa.com" vira "empresa.com".</summary>
    internal static string Label(string source)
    {
        var i = source.IndexOf(':');
        if (i < 0) return source;
        var (kind, rest) = (source[..i], source[(i + 1)..]);
        var name = kind switch
        {
            "web" => "", "hn" => "Hacker News", "github" => "GitHub", "wwr" => "We Work Remotely", "remoteok" => "RemoteOK", "themuse" => "The Muse",
            "smartrecruiters" => "SmartRecruiters", _ => char.ToUpperInvariant(kind[0]) + kind[1..],
        };
        rest = rest.Replace("https://", "").Replace("http://", "").TrimEnd('/');
        return name.Length == 0 ? rest : $"{name} {rest}";
    }

    private async Task SnapshotAsync(Guid jobId, string raw, DateTime now, CancellationToken ct)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        if (await db.Snapshots.AnyAsync(s => s.JobId == jobId && s.PayloadHash == hash, ct) || db.Snapshots.Local.Any(s => s.JobId == jobId && s.PayloadHash == hash)) return;
        db.Snapshots.Add(new JobSnapshot { Id = Guid.CreateVersion7(), JobId = jobId, FetchedAt = now, PayloadHash = hash, Payload = raw });
    }
}

/// <summary>Quantas rodadas seguidas cada fonte respondeu "não existe" (404 ou 410); uma resposta boa zera.</summary>
internal static class SourceHealth
{
    private static string File => Path.Combine(AppPaths.In(AppPaths.Data, "jobs"), "source-health.json");

    public static Dictionary<string, int> Load()
    {
        try { return System.IO.File.Exists(File) ? JsonSerializer.Deserialize<Dictionary<string, int>>(System.IO.File.ReadAllText(File)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    public static void Save(Dictionary<string, int> health) => System.IO.File.WriteAllText(File, JsonSerializer.Serialize(health));
}

