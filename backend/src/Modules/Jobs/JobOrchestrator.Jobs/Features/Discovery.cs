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
    JobsDbContext db, ICampaignsApi campaigns, IAutomationApi automation, IIntelligenceApi ai, TimeProvider time, ILogger<RunRequestedConsumer> log) : IEventConsumer
{
    public string EventType => RunRequestedV1.EventType;

    public async Task ConsumeAsync(Envelope message, CancellationToken ct)
    {
        var run = message.Read<RunRequestedV1>();
        var campaign = await campaigns.GetAsync(run.CampaignId, ct);
        if (campaign is null) return;

        var found = new List<(DiscoveredPosting Posting, PostingStatus Status)>();
        var ignored = 0;
        var clock = Stopwatch.StartNew();
        var thread = AgentThread.New("search", $"Busca: {campaign.Name}");
        await SayAsync(thread, "Batedor", $"{campaign.Sources.Count} {(campaign.Sources.Count == 1 ? "fonte" : "fontes")} para consultar", string.Join("\n", campaign.Sources.Select(Label)),
            new { stage = "start", runId = run.RunId, sources = campaign.Sources, cut = campaign.MinFitScore }, "Normalizador", true, ct);
        foreach (var source in campaign.Sources)
        {
            var watch = Stopwatch.StartNew();
            await SayAsync(thread, "Batedor", $"Consultando {Label(source)}", null, new { stage = "source", source, state = "working" }, null, true, ct);
            int listed = 0, kept = 0;
            try
            {
                var postings = await automation.DiscoverAsync(source, ct);
                listed = postings.Count;
                foreach (var p in postings)
                {
                    // Sem nenhuma palavra essencial da campanha, a vaga nem entra: boards grandes têm centenas de vagas de outras áreas.
                    if (!Scoring.Relevant(campaign, p.Title, p.Description)) { ignored++; continue; }
                    kept++;
                    found.Add((p, p.ListedAsOpen ? new PostingStatus(true, "Listada agora pela API oficial.") : await automation.CheckOpenAsync(p.ApplyUrl, ct)));
                }
                await SayAsync(thread, "Batedor", $"{Label(source)}: {listed} {(listed == 1 ? "vaga" : "vagas")}, {kept} da sua área", null,
                    new { stage = "source", source, state = "done", listed, kept, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, "Normalizador", true, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
            {
                // Uma fonte fora do ar não derruba a rodada.
                log.LogWarning("Source {Source} failed: {Error}", source, ex.Message);
                await SayAsync(thread, "Batedor", $"{Label(source)} não respondeu", ex.Message,
                    new { stage = "source", source, state = "failed", listed, kept, error = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1) }, "Normalizador", false, ct);
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        if (!await db.RegisterInInboxAsync(message.MessageId, now, ct)) return;
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
            var passed = posting.Evaluate(Scoring.Evaluate(campaign, posting.Title, posting.Description, posting.Location, posting.Modality), campaign.MinFitScore);
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
