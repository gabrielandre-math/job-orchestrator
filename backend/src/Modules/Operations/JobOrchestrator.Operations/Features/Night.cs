using System.Diagnostics;
using System.Text;

namespace JobOrchestrator.Operations.Features;

internal sealed record NightCount(string Key, long Value);
internal sealed record NightJob(string Company, string Title, int Score);
internal sealed record NightReport(DateTime From, DateTime To, long Runs, long Found, long Shortlisted, long Rejected, long Duplicates,
    IReadOnlyList<NightCount> Applications, long MailsSent, IReadOnlyList<NightCount> Replies, long AiCalls, long AiFailures, long Tokens,
    IReadOnlyList<NightCount> Providers, long Failures, IReadOnlyList<NightJob> Top);
internal sealed record ReportFile(string Name, DateTime At, string Summary);

/// <summary>
/// A noite: o timer pede rodadas das campanhas ligadas e, no fim da janela, o resumo do período (descoberta, nota, candidaturas por estado,
/// e-mails, uso de IA e falhas) vira um arquivo Markdown e um JSON na pasta de dados, com aviso na área de trabalho.
/// </summary>
internal sealed class NightReports([FromKeyedServices("operations")] NpgsqlDataSource data, ILogger<NightReports> log)
{
    public static string Folder => AppPaths.In(AppPaths.Data, "reports");

    public async Task<(NightReport Report, string Name)> WriteAsync(DateTime? since)
    {
        Directory.CreateDirectory(Folder);
        var to = DateTime.UtcNow;
        var from = since ?? Last() ?? to.AddHours(-24);
        await using var c = await data.OpenConnectionAsync();
        var p = new { from, to };
        long N(string sql) => c.ExecuteScalar<long>(sql, p);
        var report = new NightReport(from, to,
            N("select count(*) from campaigns.runs where requested_at between @from and @to"),
            N("select count(*) from jobs.postings where first_seen_at between @from and @to"),
            N("select count(*) from jobs.postings where first_seen_at between @from and @to and status = 'Shortlisted'"),
            N("select count(*) from jobs.postings where first_seen_at between @from and @to and status = 'Rejected'"),
            N("select count(*) from jobs.postings where first_seen_at between @from and @to and status = 'Duplicate'"),
            [.. await c.QueryAsync<NightCount>("select \"to\" as key, count(*) as value from applications.events where at between @from and @to group by \"to\" order by 2 desc", p)],
            N("select count(*) from mail.outgoing where sent_at between @from and @to"),
            [.. await c.QueryAsync<NightCount>("select coalesce(class, 'sem classe') as key, count(*) as value from mail.incoming where received_at between @from and @to group by 1 order by 2 desc", p)],
            N("select count(*) from intelligence.calls where at between @from and @to"),
            N("select count(*) from intelligence.calls where at between @from and @to and not ok"),
            N("select coalesce(sum(tokens), 0) from intelligence.calls where at between @from and @to"),
            [.. await c.QueryAsync<NightCount>("select provider_id as key, count(*) as value from intelligence.calls where at between @from and @to group by 1 order by 2 desc", p)],
            N("select count(*) from intelligence.messages where at between @from and @to and not ok"),
            [.. await c.QueryAsync<NightJob>("select company, title, coalesce(score, 0) as score from jobs.postings where first_seen_at between @from and @to and status = 'Shortlisted' order by score desc limit 10", p)]);
        var name = $"{TimeZoneInfo.ConvertTimeFromUtc(to, Zone):yyyy-MM-dd-HHmm}";
        await File.WriteAllTextAsync(Path.Combine(Folder, $"{name}.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerOptions.Web) { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(Folder, $"{name}.md"), Markdown(report));
        Notify($"Resumo da noite: {report.Found} vagas lidas, {report.Shortlisted} na fila, {report.MailsSent} e-mails.");
        return (report, name);
    }

    public IReadOnlyList<ReportFile> List() => !Directory.Exists(Folder) ? [] :
        [.. Directory.GetFiles(Folder, "*.md").OrderDescending().Take(60).Select(f => new ReportFile(Path.GetFileNameWithoutExtension(f), File.GetLastWriteTimeUtc(f), File.ReadLines(f).Skip(2).FirstOrDefault() ?? ""))];

    private static DateTime? Last() =>
        Directory.Exists(Folder) && Directory.GetFiles(Folder, "*.json").OrderDescending().FirstOrDefault() is { } f && JsonSerializer.Deserialize<NightReport>(File.ReadAllText(f), JsonSerializerOptions.Web) is { } r ? r.To : null;

    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static string Markdown(NightReport r)
    {
        var at = (DateTime t) => TimeZoneInfo.ConvertTimeFromUtc(t, Zone).ToString("dd/MM HH:mm");
        var sb = new StringBuilder($"# Resumo da noite, {at(r.From)} a {at(r.To)}\n\n");
        sb.AppendLine($"{r.Found} vagas lidas, {r.Shortlisted} na fila, {r.Rejected} cortadas e {r.Duplicates} repetidas em {r.Runs} {(r.Runs == 1 ? "rodada" : "rodadas")}.\n");
        sb.AppendLine("## Candidaturas\n");
        foreach (var a in r.Applications) sb.AppendLine($"- {State(a.Key)}: {a.Value}");
        if (r.Applications.Count == 0) sb.AppendLine("- Nenhuma mudança no período.");
        sb.AppendLine($"\n## E-mail\n\n- Enviados: {r.MailsSent}");
        foreach (var x in r.Replies) sb.AppendLine($"- Respostas ({x.Key}): {x.Value}");
        sb.AppendLine($"\n## IA\n\n- Chamadas: {r.AiCalls} ({r.AiFailures} com falha), {r.Tokens} tokens");
        foreach (var x in r.Providers) sb.AppendLine($"- {x.Key}: {x.Value}");
        sb.AppendLine($"\n## Falhas\n\n- {r.Failures} {(r.Failures == 1 ? "passo de agente falhou" : "passos de agente falharam")} no período.");
        if (r.Top.Count > 0)
        {
            sb.AppendLine("\n## Entraram na fila\n");
            foreach (var j in r.Top) sb.AppendLine($"- {j.Score} · {j.Company}: {j.Title}");
        }
        return sb.ToString();
    }

    private static string State(string s) => s switch
    {
        "FormValidated" => "Preparadas", "AwaitingApproval" => "Esperando você", "SubmittedConfirmed" => "Confirmadas pelo ATS", "SubmittedDeclared" => "Enviadas por você",
        "SubmissionUnknown" => "Envio incerto", "BlockedByPolicy" => "Bloqueadas pelo Guardião", "NeedsHuman" => "Paradas com você", "ManualHandoff" => "Pelo site original",
        "Failed" => "Falharam", "Shortlisted" => "Entraram na fila", "FormPreparing" => "Preparando o formulário", "MaterialsReady" => "Currículo pronto",
        "ReadyToSubmit" => "Prontas para enviar", "Submitting" => "Enviando", "Withdrawn" => "Retiradas", "RetryScheduled" => "Nova tentativa agendada",
        "PausedProvider" => "Pausadas sem IA", "DeadLetter" => "Paradas por erro", _ => s,
    };

    private void Notify(string text)
    {
        Platform.Notify("Job Orchestrator", text);
        log.LogInformation("Notified: {Text}", text);
    }
}

internal sealed class NightEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/night").WithTags("Operations");
        g.MapPost("run", async (JobOrchestrator.Campaigns.Contracts.ICampaignRuns runs, CancellationToken ct) => Results.Ok(new { campaigns = await runs.RequestEnabledAsync("night", ct) }));
        g.MapPost("report", async (NightReports night) => { var (r, name) = await night.WriteAsync(null); return Results.Ok(new { name, report = r }); });
        g.MapGet("reports", (NightReports night) => Results.Ok(night.List()));
        g.MapGet("reports/{name}", (string name) =>
        {
            if (name.Any(ch => !char.IsLetterOrDigit(ch) && ch != '-')) return Results.BadRequest();
            var md = Path.Combine(NightReports.Folder, $"{name}.md");
            return File.Exists(md) ? Results.Ok(new { name, markdown = File.ReadAllText(md) }) : Results.NotFound();
        });
    }
}
