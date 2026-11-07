using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>Runs only when asked: JO_REHEARSAL_HOURS (scripts/rehearsal.sh). Hours of real work have no place in the normal suite.</summary>
public sealed class RehearsalFactAttribute : FactAttribute
{
    public RehearsalFactAttribute() { if (Hours is null) Skip = "Ensaio noturno: rode com scripts/rehearsal.sh (JO_REHEARSAL_HOURS)."; }

    public static double? Hours => double.TryParse(Environment.GetEnvironmentVariable("JO_REHEARSAL_HOURS"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h) && h > 0 ? h : null;
}

/// <summary>
/// The night, rehearsed: the real app (API, Worker, browser, Postgres) works for hours in automatic mode against the test ATS and a local
/// mail catcher (GreenMail), with a synthetic profile, while faults are forced on it: processes killed, the site and the mail server down,
/// a resume deleted, the authorization revoked, the campaign moved to search only. At the end the promises are checked where they leave
/// marks (the ATS receipts, the mailboxes and the database), not in what the app says about itself.
/// </summary>
[Trait("Category", "Rehearsal")]
public sealed class NightRehearsal
{
    private const string Mail = "jo-rehearsal-mail";
    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(60);

    private sealed record Fault(DateTime At, string Name, string Detail);
    private sealed record Window(DateTime From, DateTime To, string Why);
    private sealed record Check(string Name, bool Ok, string Detail);

    [RehearsalFact]
    public async Task The_night_keeps_its_promises()
    {
        var hours = RehearsalFactAttribute.Hours!.Value;
        var cycle = TimeSpan.FromMinutes(double.TryParse(Environment.GetEnvironmentVariable("JO_REHEARSAL_CYCLE_MINUTES"), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : 15);
        var outage = TimeSpan.FromMinutes(Math.Min(2, cycle.TotalMinutes / 3));
        var pause = TimeSpan.FromMinutes(Math.Min(5, cycle.TotalMinutes / 2));
        var report = Environment.GetEnvironmentVariable("JO_REHEARSAL_REPORT") ?? Path.Combine(Path.GetTempPath(), $"ensaio-{DateTime.Now:yyyyMMdd-HHmm}.md");
        var started = DateTime.UtcNow;
        var faults = new List<Fault>();
        var windows = new List<Window>();
        var memory = new List<(DateTime At, long Api, long Worker)>();
        var deleted = new List<(DateTime At, string File)>();

        await StartMailAsync();
        await using var s = await Stack.StartAsync(extra: new() { ["JO_MAIL_PLAIN_LOOPBACK"] = "1" });
        Guid? account = null;
        try
        {
            var campaign = (await s.GetAsync("/campaigns"))[0].GetProperty("id").GetString();
            await AutoAsync(s, campaign!);
            account = await ConnectMailAsync(s);
            await MailAutoAsync(s);

            var deadline = started + TimeSpan.FromHours(hours);
            string[] rotation = ["worker-crash", "ats-down", "api-crash", "mail-down", "revoke", "resume-gone", "search-only"];
            var turn = 0;
            while (DateTime.UtcNow < deadline)
            {
                var cycleStart = DateTime.UtcNow;
                await s.Http.PostAsync($"{s.Ats}/lab/noite?count=3", null);
                try { await s.RunAsync(); } catch (Exception ex) when (ex is HttpRequestException or Xunit.Sdk.XunitException) { faults.Add(new(DateTime.UtcNow, "run", $"rodada não começou: {ex.Message}")); }
                await Task.Delay(cycle / 3);

                // Memory before the fault: right after a restart the numbers say nothing.
                var (api, worker) = s.Memory();
                memory.Add((DateTime.UtcNow, api, worker));
                var fault = rotation[turn++ % rotation.Length];
                var at = DateTime.UtcNow;
                switch (fault)
                {
                    case "worker-crash":
                        await s.KillAsync("worker");
                        await Task.Delay(TimeSpan.FromSeconds(20));
                        s.StartWorker();
                        faults.Add(new(at, fault, "Worker morto com kill -9 e religado 20 s depois."));
                        break;
                    case "api-crash":
                        await s.KillAsync("api");
                        await Task.Delay(TimeSpan.FromSeconds(10));
                        await s.StartApiAsync();
                        faults.Add(new(at, fault, "API morta com kill -9 e religada."));
                        break;
                    case "ats-down":
                        await s.KillAsync("ats");
                        await Task.Delay(outage);
                        s.StartAts();
                        faults.Add(new(at, fault, $"Site (ATS de teste) fora do ar por {outage.TotalMinutes:0.#} min."));
                        break;
                    case "mail-down":
                        await Docker("pause", Mail);
                        await Task.Delay(outage);
                        await Docker("unpause", Mail);
                        faults.Add(new(at, fault, $"Servidor de e-mail travado por {outage.TotalMinutes:0.#} min."));
                        break;
                    case "resume-gone":
                        var pdf = Directory.Exists(Path.Combine(s.DataDir, "materials"))
                            ? Directory.EnumerateFiles(Path.Combine(s.DataDir, "materials"), "*.pdf", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                            : null;
                        if (pdf is not null) { File.Delete(pdf); deleted.Add((at, pdf)); }
                        faults.Add(new(at, fault, pdf is null ? "Nenhum currículo gerado ainda para apagar." : $"Currículo apagado: {Path.GetFileName(Path.GetDirectoryName(pdf))}/{Path.GetFileName(pdf)}."));
                        break;
                    case "revoke":
                        await s.PostAsync("/settings/revoke");
                        await Task.Delay(pause);
                        windows.Add(new(at, DateTime.UtcNow, "revogação"));
                        await AutoAsync(s, campaign!);
                        await MailAutoAsync(s);
                        faults.Add(new(at, fault, $"Revogar tudo; o automático volta {pause.TotalMinutes:0.#} min depois."));
                        break;
                    case "search-only":
                        await s.SourcesAsync("DiscoveryOnly", $"greenhouse:{s.Ats}/boards/noite");
                        await Task.Delay(pause);
                        windows.Add(new(at, DateTime.UtcNow, "só buscar"));
                        await AutoAsync(s, campaign!);
                        faults.Add(new(at, fault, $"Campanha em só buscar por {pause.TotalMinutes:0.#} min."));
                        break;
                }
                var rest = cycle - (DateTime.UtcNow - cycleStart);
                if (rest > TimeSpan.Zero) await Task.Delay(rest);
            }

            // No more faults: the queues get the time of a mail cycle and a bit to drain before the checks.
            await Task.Delay(TimeSpan.FromMinutes(Math.Min(10, Math.Max(6, cycle.TotalMinutes))));
            var checks = await CheckAsync(s, windows, deleted);
            await WriteReportAsync(report, s, hours, cycle, started, faults, windows, memory, checks);
            Assert.True(checks.All(x => x.Ok), $"O ensaio reprovou em: {string.Join("; ", checks.Where(x => !x.Ok).Select(x => x.Name))}. Relatório: {report}");
        }
        finally
        {
            if (account is { } id) try { await s.Http.DeleteAsync($"{s.Api}/mail/accounts/{id}"); } catch (HttpRequestException) { }
            await Docker("rm", "-f", Mail);
        }
    }

    // ---------- Setup ----------

    private static async Task StartMailAsync()
    {
        await Docker("rm", "-f", Mail);
        var image = await DockerOk("image", "inspect", "greenmail/standalone:2.1.2") ? "greenmail/standalone:2.1.2" : "greenmail/standalone:latest";
        await Docker("run", "-d", "--name", Mail, "-p", "127.0.0.1:3025:3025", "-p", "127.0.0.1:3143:3143",
            "-e", "GREENMAIL_OPTS=-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.auth.disabled -Dgreenmail.hostname=0.0.0.0", image);
        for (var i = 0; i < 60; i++)
        {
            try { using var t = new TcpClient(); await t.ConnectAsync(IPAddress.Loopback, 3143); return; }
            catch (SocketException) { await Task.Delay(1000); }
        }
        throw new InvalidOperationException("O GreenMail não subiu.");
    }

    /// <summary>Only granting an authorization turns automatic sending on (a PUT of the campaign never does): you approve first, then authorize.</summary>
    private static async Task AutoAsync(Stack s, string campaign)
    {
        await s.SourcesAsync("ReviewRequired", $"greenhouse:{s.Ats}/boards/noite");
        await AuthorizeAsync(s, campaign);
    }

    private static async Task AuthorizeAsync(Stack s, string campaign) =>
        Assert.Equal(HttpStatusCode.OK, await s.PostAsync($"/campaigns/{campaign}/authorizations",
            new { days = 7, maxPerDay = MaxPerDay, minScore = 80, adapters = new[] { "greenhouse-form", "lever-form", "ashby-form" }, accepted = true }));

    /// <summary>The highest daily cap an authorization may give; the rehearsal also checks that no day goes past it.</summary>
    private const int MaxPerDay = 15;

    private static async Task<Guid> ConnectMailAsync(Stack s)
    {
        var response = await s.Http.PostAsJsonAsync($"{s.Api}/mail/accounts", new
        {
            provider = "smtp", address = "eu@ensaio.example", displayName = "Ensaio", password = "senha-do-ensaio",
            smtpHost = "127.0.0.1", smtpPort = 3025, smtpSsl = false, imapHost = "127.0.0.1", imapPort = 3143
        });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"A conta de e-mail do ensaio não conectou: {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task MailAutoAsync(Stack s) =>
        Assert.Equal(HttpStatusCode.OK, await s.PutAsync("/mail/settings", new { mode = "Auto", dailyCap = 50, followUpDays = 7, windowStart = 0, windowEnd = 24, attachResume = true, consent = true }));

    // ---------- Checks ----------

    private static async Task<List<Check>> CheckAsync(Stack s, IReadOnlyList<Window> windows, IReadOnlyList<(DateTime At, string File)> deleted)
    {
        var checks = new List<Check>();
        await using var db = new NpgsqlConnection(s.Connection);
        await db.OpenAsync();

        // 1. One application per job at the site: the ATS keeps every receipt.
        var receipts = (await s.Http.GetFromJsonAsync<JsonElement>($"{s.Ats}/api/submissions")).EnumerateArray()
            .Select(r => (Job: r.GetProperty("job").GetInt32(), Email: r.GetProperty("email").GetString()!, At: r.GetProperty("at").GetDateTime().ToUniversalTime())).ToList();
        var twice = receipts.GroupBy(r => (r.Job, r.Email)).Where(g => g.Count() > 1).Select(g => g.Key.Job).ToList();
        checks.Add(new("Nenhuma candidatura em dobro no site", twice.Count == 0, twice.Count == 0 ? $"{receipts.Count} candidaturas, uma por vaga." : $"Vagas com mais de uma: {string.Join(", ", twice)}."));

        // 1b. The daily cap of the authorization holds, whatever restarts and re-authorizations happened.
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var busiest = receipts.GroupBy(r => TimeZoneInfo.ConvertTimeFromUtc(r.At, zone).Date).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        checks.Add(new("Teto diário respeitado", busiest <= MaxPerDay, $"Dia com mais envios no site: {busiest} (teto {MaxPerDay})."));

        // 2. Nothing starts leaving inside a revocation or search-only window (a send already on the wire gets the grace).
        var submitting = (await db.QueryAsync<DateTime>("""select at from applications.events where "to" = 'Submitting'""")).Select(x => DateTime.SpecifyKind(x, DateTimeKind.Utc)).ToList();
        var sentMail = (await db.QueryAsync<DateTime>("select sent_at from mail.outgoing where sent_at is not null")).Select(x => DateTime.SpecifyKind(x, DateTimeKind.Utc)).ToList();
        var inside = windows.SelectMany(w => submitting.Concat(sentMail).Where(t => t > w.From + Grace && t < w.To).Select(t => $"{w.Why} às {t:HH:mm:ss}")).ToList();
        checks.Add(new("Nada sai com o automático desligado", inside.Count == 0, inside.Count == 0 ? $"{windows.Count} janelas de revogação ou só buscar, nenhum envio dentro." : string.Join("; ", inside)));

        // 3. Each e-mail reached its mailbox once, with the exact resume that was approved.
        var outgoing = (await db.QueryAsync<(string To, string Subject, string? Sha, string Status)>(
            "select \"to\", subject, attachment_sha256, status from mail.outgoing where kind = 'first' and status in ('Sent', 'Replied', 'Uncertain')")).ToList();
        var mailProblems = new List<string>();
        foreach (var o in outgoing)
        {
            var received = await MailboxAsync(o.To);
            var copies = received.Where(m => m.Subject == o.Subject).ToList();
            if (copies.Count > 1) mailProblems.Add($"{o.To}: {copies.Count} cópias");
            if (copies.Count == 0 && o.Status != "Uncertain") mailProblems.Add($"{o.To}: marcado como enviado e não chegou");
            foreach (var m in copies)
                if (o.Sha is { } sha && m.Attachments.All(a => a != sha)) mailProblems.Add($"{o.To}: anexo diferente do aprovado");
        }
        checks.Add(new("Cada e-mail chegou uma vez, com o currículo aprovado", mailProblems.Count == 0,
            mailProblems.Count == 0 ? $"{outgoing.Count} e-mails conferidos na caixa de quem recebeu." : string.Join("; ", mailProblems)));

        // 4. Nothing left half-way once the faults stop.
        var stuckApps = await db.ExecuteScalarAsync<long>("select count(*) from applications.applications where state in ('Submitting', 'FormPreparing')");
        var stuckMail = await db.ExecuteScalarAsync<long>("select count(*) from mail.outgoing where status = 'Sending'");
        checks.Add(new("Nada parado no meio", stuckApps == 0 && stuckMail == 0, $"{stuckApps} candidaturas e {stuckMail} e-mails parados no meio."));

        // 5. No silent failure: every stop carries its reason.
        var silentApps = await db.ExecuteScalarAsync<long>("select count(*) from applications.applications where state in ('Failed', 'NeedsHuman', 'DeadLetter') and coalesce(last_reason, '') = ''");
        var silentMail = await db.ExecuteScalarAsync<long>("select count(*) from mail.outgoing where status in ('Failed', 'Uncertain') and coalesce(error, '') = ''");
        checks.Add(new("Toda parada diz o motivo", silentApps == 0 && silentMail == 0, $"{silentApps} candidaturas e {silentMail} e-mails parados sem motivo."));

        // 6. A deleted resume never left: no e-mail or form after the deletion used it.
        var afterDeletion = deleted.Count == 0 ? 0 : await db.ExecuteScalarAsync<long>(
            "select count(*) from mail.outgoing where status in ('Sent', 'Replied') and attachment_path = any(@Files) and sent_at > @At",
            new { Files = deleted.Select(d => d.File).ToArray(), At = deleted.Min(d => d.At) });
        checks.Add(new("Currículo apagado não sai", afterDeletion == 0, deleted.Count == 0 ? "Nenhum currículo foi apagado nesta rodada." : $"{deleted.Count} apagados; {afterDeletion} e-mails saíram com eles depois."));

        var states = (await db.QueryAsync<(string State, long N)>("select state, count(*) from applications.applications group by state order by 2 desc")).ToList();
        var mailStates = (await db.QueryAsync<(string Status, long N)>("select status, count(*) from mail.outgoing group by status order by 2 desc")).ToList();
        checks.Add(new("Resumo", true, $"Candidaturas: {string.Join(", ", states.Select(x => $"{x.State} {x.N}"))}. E-mails: {string.Join(", ", mailStates.Select(x => $"{x.Status} {x.N}"))}."));
        return checks;
    }

    private sealed record Received(string Subject, IReadOnlyList<string> Attachments);

    /// <summary>What a recipient's mailbox at the catcher holds, with the SHA-256 of each attachment.</summary>
    private static async Task<List<Received>> MailboxAsync(string address)
    {
        using var imap = new ImapClient();
        await imap.ConnectAsync("127.0.0.1", 3143, SecureSocketOptions.None);
        await imap.AuthenticateAsync(address, "ensaio");
        await imap.Inbox.OpenAsync(FolderAccess.ReadOnly);
        var list = new List<Received>();
        for (var i = 0; i < imap.Inbox.Count; i++)
        {
            var m = await imap.Inbox.GetMessageAsync(i);
            var hashes = new List<string>();
            foreach (var part in m.Attachments.OfType<MimePart>())
            {
                if (part.Content is not { } content) continue;
                using var buffer = new MemoryStream();
                await content.DecodeToAsync(buffer);
                hashes.Add(Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray())));
            }
            list.Add(new(m.Subject ?? "", hashes));
        }
        await imap.DisconnectAsync(true);
        return list;
    }

    // ---------- Report ----------

    private static async Task WriteReportAsync(string path, Stack s, double hours, TimeSpan cycle, DateTime started, IReadOnlyList<Fault> faults,
        IReadOnlyList<Window> windows, IReadOnlyList<(DateTime At, long Api, long Worker)> memory, IReadOnlyList<Check> checks)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder();
        sb.AppendLine($"# Ensaio noturno, {started.ToLocalTime():dd/MM/yyyy HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"{hours.ToString("0.##", br)} h com uma rodada a cada {cycle.TotalMinutes.ToString("0.#", br)} min, {faults.Count} falhas provocadas. "
            + (checks.All(c => c.Ok) ? "**Passou em todos os critérios.**" : $"**Reprovou em {checks.Count(c => !c.Ok)} critério(s).**"));
        sb.AppendLine();
        sb.AppendLine("## Critérios");
        sb.AppendLine();
        foreach (var c in checks) sb.AppendLine($"- {(c.Ok ? "ok" : "REPROVADO")}: **{c.Name}.** {c.Detail}");
        sb.AppendLine();
        sb.AppendLine("## Falhas provocadas");
        sb.AppendLine();
        foreach (var f in faults) sb.AppendLine($"- {f.At.ToLocalTime():HH:mm:ss} {f.Name}: {f.Detail}");
        if (windows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Janelas sem envio");
            sb.AppendLine();
            foreach (var w in windows) sb.AppendLine($"- {w.Why}: {w.From.ToLocalTime():HH:mm:ss} a {w.To.ToLocalTime():HH:mm:ss}");
        }
        if (memory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Memória (MB)");
            sb.AppendLine();
            sb.AppendLine($"API de {memory[0].Api} a {memory[^1].Api} (pico {memory.Max(m => m.Api)}); Worker de {memory[0].Worker} a {memory[^1].Worker} (pico {memory.Max(m => m.Worker)}).");
        }
        sb.AppendLine();
        sb.AppendLine($"Logs da API, do Worker e do ATS de teste: {s.Folder}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, sb.ToString());
        Console.WriteLine(sb.ToString());
    }

    // ---------- Docker ----------

    private static async Task Docker(params string[] args) => await DockerOk(args);

    private static async Task<bool> DockerOk(params string[] args)
    {
        var info = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        await p.WaitForExitAsync();
        return p.ExitCode == 0;
    }
}
