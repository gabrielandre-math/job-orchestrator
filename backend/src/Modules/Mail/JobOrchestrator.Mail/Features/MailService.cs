using System.Text;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace JobOrchestrator.Mail.Features;

public sealed record AccountInput(string Provider, string Address, string? DisplayName, string Password, string? SmtpHost, int? SmtpPort, bool? SmtpSsl, string? ImapHost, int? ImapPort);
public sealed record SettingsInput(string Mode, int DailyCap, int FollowUpDays, int WindowStart, int WindowEnd, bool AttachResume, bool Consent);
public sealed record TemplateInput(string Subject, string Body);
public sealed record DraftInput(Guid JobId, string? Kind);
public sealed record EditInput(string Subject, string Body);

/// <summary>Erro que vira frase na tela (400), nunca exceção crua.</summary>
internal sealed class MailRuleException(string message) : Exception(message);

/// <summary>
/// E-mail para recrutadores, com o Guardião no meio: só para o endereço que a vaga publica para candidatura, um e-mail por vaga e um lembrete,
/// teto por dia, janela de horário e, no modo automático, só depois do consentimento. A IA escreve em cima do esqueleto, o Conferente
/// tira frase sem fonte no currículo e os dados de contato entram só na máquina, depois da IA.
/// </summary>
internal sealed partial class MailService(
    MailDbContext db, IJobsApi jobs, IProfileApi profile, IMaterialsApi materials, IIntelligenceApi ai, TimeProvider time, ILogger<MailService> log)
{
    public const string ConsentText = "Autorizo o Job Orchestrator a enviar, sem me perguntar a cada vez, e-mails de candidatura escritos a partir dos fatos verificados do meu currículo, " +
        "apenas para o endereço que cada vaga publica para candidatura, no máximo um por vaga e um lembrete, dentro do teto diário e da janela de horário que configurei. Posso desligar a qualquer momento.";
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
    private DateTime Now => time.GetUtcNow().UtcDateTime;

    // ---------- Contas ----------

    public async Task<MailAccount> ConnectAsync(AccountInput input, CancellationToken ct)
    {
        var preset = Presets.Find(input.Provider);
        var address = input.Address.Trim();
        if (!MailboxAddress.TryParse(address, out _) || !address.Contains('@')) throw new MailRuleException("Endereço de e-mail inválido.");
        if (input.Password.Trim().Length < 6) throw new MailRuleException("Cole a senha de app inteira.");
        var account = new MailAccount
        {
            Provider = preset?.Id ?? "smtp", Address = address, DisplayName = (input.DisplayName ?? "").Trim(), CreatedAt = Now,
            SmtpHost = input.SmtpHost?.Trim() is { Length: > 0 } sh ? sh : preset?.Smtp ?? "", SmtpPort = input.SmtpPort ?? preset?.SmtpPort ?? 587,
            SmtpSsl = input.SmtpSsl ?? preset?.SmtpSsl ?? false, ImapHost = input.ImapHost?.Trim() is { Length: > 0 } ih ? ih : preset?.Imap ?? "", ImapPort = input.ImapPort ?? preset?.ImapPort ?? 993,
        };
        if (account.SmtpHost.Length == 0) throw new MailRuleException("Informe o servidor SMTP.");
        var password = input.Password.Replace(" ", "").Trim();
        await TestSmtpAsync(account, password, ct);
        if (account.ImapHost.Length > 0) await TestImapAsync(account, password, ct);
        if (!await Secrets.StoreAsync($"mail:{account.Id}", password, ct)) throw new MailRuleException("O gnome-keyring não aceitou guardar a senha. Confira se o chaveiro está desbloqueado.");
        if (account.DisplayName.Length == 0 && await profile.CurrentAsync(ct) is { } p) account.DisplayName = p.Identity.FullName;
        account.CheckedAt = Now;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task RemoveAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Accounts.FindAsync([id], ct);
        if (a is null) return;
        await Secrets.ClearAsync($"mail:{id}", ct);
        db.Accounts.Remove(a);
        await db.SaveChangesAsync(ct);
    }

    public async Task SendTestAsync(Guid id, CancellationToken ct)
    {
        var a = await db.Accounts.FindAsync([id], ct) ?? throw new MailRuleException("Conta não encontrada.");
        var msg = new MimeMessage { Subject = "Teste do Job Orchestrator" };
        msg.From.Add(new MailboxAddress(a.DisplayName, a.Address));
        msg.To.Add(new MailboxAddress(a.DisplayName, a.Address));
        msg.Body = new TextPart("plain") { Text = "Se este e-mail chegou, o envio está pronto. Nada foi enviado para empresa nenhuma." };
        await SendAsync(a, msg, ct);
    }

    private static async Task TestSmtpAsync(MailAccount a, string password, CancellationToken ct)
    {
        using var smtp = new SmtpClient { Timeout = 20000 };
        try
        {
            await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, a.SmtpSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
            await smtp.AuthenticateAsync(a.Address, password, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException) { throw new MailRuleException("O servidor recusou o login. Confira se é uma senha de app, não a senha normal da conta."); }
        catch (Exception ex) when (ex is IOException or SmtpCommandException or SmtpProtocolException or System.Net.Sockets.SocketException or SslHandshakeException)
        { throw new MailRuleException($"Não deu para falar com {a.SmtpHost}:{a.SmtpPort}: {ex.Message}"); }
    }

    private static async Task TestImapAsync(MailAccount a, string password, CancellationToken ct)
    {
        using var imap = new ImapClient { Timeout = 20000 };
        try
        {
            await imap.ConnectAsync(a.ImapHost, a.ImapPort, SecureSocketOptions.SslOnConnect, ct);
            await imap.AuthenticateAsync(a.Address, password, ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (AuthenticationException) { throw new MailRuleException("O envio funcionou, mas a leitura (IMAP) recusou o login. No Gmail, confira se o IMAP está ligado nas configurações."); }
        catch (Exception ex) when (ex is IOException or ImapCommandException or ImapProtocolException or System.Net.Sockets.SocketException or SslHandshakeException)
        { throw new MailRuleException($"Não deu para ler a caixa em {a.ImapHost}:{a.ImapPort}: {ex.Message}"); }
    }

    // ---------- Configuração e modelo ----------

    public async Task<MailSettings> SettingsAsync(CancellationToken ct)
    {
        var s = await db.Settings.FindAsync([1], ct);
        if (s is not null) return s;
        s = new MailSettings();
        db.Settings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<MailSettings> SaveSettingsAsync(SettingsInput input, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        if (input.Mode is not ("Draft" or "Review" or "Auto")) throw new MailRuleException("Modo desconhecido.");
        if (input.Mode == "Auto" && !input.Consent && s.AutoConsentAt is null) throw new MailRuleException("O modo automático precisa do seu consentimento marcado na tela.");
        if (input.Mode == "Auto" && input.Consent) { s.AutoConsentAt = Now; s.AutoConsentText = ConsentText; }
        if (input.Mode != "Auto") s.AutoConsentAt = null;
        s.Mode = input.Mode;
        s.DailyCap = Math.Clamp(input.DailyCap, 1, 15);
        s.FollowUpDays = Math.Clamp(input.FollowUpDays, 3, 21);
        s.WindowStart = Math.Clamp(input.WindowStart, 0, 23);
        s.WindowEnd = Math.Clamp(input.WindowEnd, s.WindowStart + 1, 24);
        s.AttachResume = input.AttachResume;
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<IReadOnlyList<MailTemplate>> TemplatesAsync(CancellationToken ct)
    {
        if (!await db.Templates.AnyAsync(ct))
        {
            db.Templates.AddRange(Defaults().Select(t => { t.UpdatedAt = Now; return t; }));
            await db.SaveChangesAsync(ct);
        }
        return await db.Templates.OrderBy(t => t.Kind).ThenBy(t => t.Language).ToListAsync(ct);
    }

    public async Task<MailTemplate> SaveTemplateAsync(Guid id, TemplateInput input, CancellationToken ct)
    {
        var t = await db.Templates.FindAsync([id], ct) ?? throw new MailRuleException("Modelo não encontrado.");
        if (!input.Body.Contains("{{assinatura}}")) throw new MailRuleException("Mantenha o marcador {{assinatura}}: é por ele que o seu contato entra, sem passar pela IA.");
        t.Subject = input.Subject.Trim();
        t.Body = input.Body.Trim();
        t.UpdatedAt = Now;
        await db.SaveChangesAsync(ct);
        return t;
    }

    private static IEnumerable<MailTemplate> Defaults() =>
    [
        new() { Kind = "first", Language = "pt", Subject = "Candidatura: {{vaga}}",
            Body = "Olá, equipe de recrutamento da {{empresa}},\n\nVi a vaga de {{vaga}} e gostaria de me candidatar. {{experiencia}}\n\n{{competencias}}\n\nSegue o meu currículo em anexo. Fico à disposição para uma conversa.\n\n{{assinatura}}" },
        new() { Kind = "first", Language = "en", Subject = "Application: {{vaga}}",
            Body = "Hello {{empresa}} recruiting team,\n\nI came across the {{vaga}} opening and would like to apply. {{experiencia}}\n\n{{competencias}}\n\nMy resume is attached. I would be glad to talk.\n\n{{assinatura}}" },
        new() { Kind = "followup", Language = "pt", Subject = "Re: Candidatura: {{vaga}}",
            Body = "Olá,\n\nEscrevo para saber se a minha candidatura para {{vaga}} segue em análise. Continuo com interesse na vaga e à disposição para conversar.\n\n{{assinatura}}" },
        new() { Kind = "followup", Language = "en", Subject = "Re: Application: {{vaga}}",
            Body = "Hello,\n\nI am following up on my application for {{vaga}}. I am still very interested and available to talk.\n\n{{assinatura}}" },
    ];

    // ---------- Rascunho ----------

    /// <param name="plain">Só o esqueleto com dados locais, sem IA: o automático usa quando o Conferente teve de cortar frase do texto da IA.</param>
    public async Task<OutgoingMail> DraftAsync(Guid jobId, string kind, CancellationToken ct, bool plain = false)
    {
        var target = (await jobs.EmailTargetsAsync(ct)).FirstOrDefault(t => t.Id == jobId) ?? throw new MailRuleException("Esta vaga não publica um e-mail de candidatura.");
        var mine = await db.Outgoing.Where(o => o.JobId == jobId && o.Status != "Cancelled" && o.Status != "Failed").ToListAsync(ct);
        var first = mine.FirstOrDefault(o => o.Kind == "first");
        if (kind == "first" && first is not null) throw new MailRuleException("Já existe um e-mail para esta vaga. Um por vaga, mais um lembrete.");
        if (kind == "followup" && (first is not { Status: "Sent" } || mine.Any(o => o.Kind == "followup"))) throw new MailRuleException("O lembrete vem só depois do primeiro e-mail enviado, uma vez.");

        var text = await jobs.TextAsync(jobId, ct);
        var p = await profile.CurrentAsync(ct) ?? throw new MailRuleException("Envie o currículo antes: o e-mail sai dos fatos dele.");
        var settings = await SettingsAsync(ct);
        var language = English(text?.Description ?? "") ? "en" : "pt";
        var template = (await TemplatesAsync(ct)).First(t => t.Kind == kind && t.Language == language);
        var thread = AgentThread.New("mail", $"E-mail para {target.Company}: {target.Title}");
        await ai.NoteAsync(thread, "Seletor", $"E-mail {(kind == "first" ? "de candidatura" : "de lembrete")} para {target.Company}", $"Destino: o e-mail que a vaga publica ({target.ApplyEmail}). Idioma: {language}.",
            new { stage = "start", jobId, company = target.Company }, "Redator", true, ct);

        var (subject, body) = Fill(template, target, p, text, language);
        var by = "modelo";
        if (kind == "first" && !plain)
        {
            var facts = string.Join("\n", p.Facts.Where(f => f.Kind is "experience" or "skill" or "achievement" or "education" or "certification" or "language").Take(50).Select(f => $"- {Line(f)}"));
            var prompt = $"Esqueleto (tom e estrutura):\nAssunto: {template.Subject}\n{template.Body}\n\nVaga: {target.Title} na {target.Company} ({target.Location}).\n{Cut(text?.Description ?? "", 3500)}\n\nFatos verificados do currículo:\n{facts}";
            var res = await ai.CompleteAsync(LlmRequest.For(LlmTasks.CoverWrite, language == "en" ? SystemEn : SystemPt, prompt, Schema, thread: thread, agent: "Redator"), ct);
            if (res.Ok && res.Json is { } json)
            {
                try
                {
                    using var d = JsonDocument.Parse(json);
                    var s = d.RootElement.GetProperty("subject").GetString() ?? "";
                    var b = d.RootElement.GetProperty("body").GetString() ?? "";
                    if (b.Length > 40) { subject = s.Length > 0 ? s : subject; body = b; by = res.Model; }
                }
                catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { log.LogInformation("Mail draft out of format: {Error}", ex.Message); }
            }
        }

        // Conferente: frase da IA com termo ou número sem fonte no currículo, na vaga ou no esqueleto sai do e-mail.
        var notes = new List<string>();
        if (by != "modelo")
        {
            var sources = p.Facts.Select(f => $"{f.Statement} {f.Role} {f.Organization} {string.Join(' ', f.Technologies)} {f.Level}").Append(text?.Description ?? "")
                .Append($"{target.Company} {target.Title} {target.Location}").Append(template.Body).ToList();
            body = string.Join("\n", body.Split('\n').Select(line =>
            {
                if (line.Contains("{{")) return line;
                var kept = new List<string>();
                foreach (var sentence in Sentence().Split(line).Where(x => x.Trim().Length > 0))
                {
                    var bad = Claims.Unsupported(sentence, sources);
                    if (bad.Count > 0) notes.Add($"\"{sentence.Trim()}\" ({string.Join(", ", bad)})"); else kept.Add(sentence.Trim());
                }
                return string.Join(" ", kept);
            }));
            body = Regex.Replace(body, @"\n{3,}", "\n\n").Trim();
            if (!body.Contains("{{assinatura}}")) body += "\n\n{{assinatura}}";
        }
        await ai.NoteAsync(thread, "Conferente", notes.Count == 0 ? "Cada afirmação tem fonte no currículo ou na vaga" : $"{notes.Count} {(notes.Count == 1 ? "frase sem fonte saiu" : "frases sem fonte saíram")}",
            notes.Count == 0 ? null : string.Join("\n", notes), new { stage = "check", removed = notes.Count }, "Guardião", notes.Count == 0, ct);

        // Contato só agora, na máquina: a IA viu marcadores, nunca o nome, o telefone ou o LinkedIn.
        body = Personal(body, p.Identity);
        subject = Personal(subject, p.Identity);

        string? pdf = null, file = null;
        if (settings.AttachResume && kind == "first")
        {
            var r = await materials.ComposeResumeAsync(new ComposeRequest(jobId, jobId, target.Company, target.Title), ct);
            if (r is { Ok: true, PdfPath: { } path }) { pdf = path; file = $"{Slug(p.Identity.FirstName)}-{Slug(p.Identity.LastName)}-{(language == "en" ? "Resume" : "Curriculo")}.pdf"; }
            else notes.Add($"currículo não anexado: {r.Error ?? "não ficou pronto"}");
        }

        var mail = new OutgoingMail
        {
            JobId = jobId, Company = target.Company, JobTitle = target.Title, To = target.ApplyEmail.Trim(), Subject = Cut(subject, 200), Body = body, Kind = kind,
            FollowsId = kind == "followup" ? first!.Id : null, Status = "AwaitingApproval", CreatedAt = Now, AttachmentPath = pdf, AttachmentName = file, DraftedBy = by,
            CheckNotes = notes.Count == 0 ? null : string.Join("\n", notes), ThreadId = thread.Id,
        };
        db.Outgoing.Add(mail);
        await db.SaveChangesAsync(ct);
        await ai.NoteAsync(thread, "Guardião", "Rascunho pronto, esperando a sua aprovação", null, new { stage = "draft", mailId = mail.Id }, "Você", true, ct);
        return mail;
    }

    public async Task<OutgoingMail> EditAsync(Guid id, EditInput input, CancellationToken ct)
    {
        var m = await db.Outgoing.FindAsync([id], ct) ?? throw new MailRuleException("E-mail não encontrado.");
        if (m.Status is not ("Draft" or "AwaitingApproval")) throw new MailRuleException("Só dá para editar antes da aprovação.");
        m.Subject = Cut(input.Subject.Trim(), 200);
        m.Body = input.Body.Trim();
        await db.SaveChangesAsync(ct);
        return m;
    }

    public async Task<OutgoingMail> ApproveAsync(Guid id, CancellationToken ct)
    {
        var m = await db.Outgoing.FindAsync([id], ct) ?? throw new MailRuleException("E-mail não encontrado.");
        if (m.Status is not ("Draft" or "AwaitingApproval")) throw new MailRuleException("Este e-mail já foi decidido.");
        m.Status = "Scheduled";
        m.ApprovedAt = Now;
        m.Error = null;
        await db.SaveChangesAsync(ct);
        await SendDueAsync(ct);
        return await db.Outgoing.AsNoTracking().FirstAsync(x => x.Id == id, ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct)
    {
        var m = await db.Outgoing.FindAsync([id], ct) ?? throw new MailRuleException("E-mail não encontrado.");
        if (m.Status is "Sent" or "Replied" or "Sending") throw new MailRuleException("Este e-mail já saiu.");
        m.Status = "Cancelled";
        await db.SaveChangesAsync(ct);
    }

    // ---------- Guardião e envio ----------

    /// <summary>Envia o que está agendado e passa no Guardião. Um processo por vez (trava no Postgres), para API e Worker nunca mandarem em dobro.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Database.SqlQueryRaw<bool>("select pg_try_advisory_xact_lock(4747010) as \"Value\"").SingleAsync(ct)) return 0;
        var settings = await SettingsAsync(ct);
        var account = await db.Accounts.OrderBy(a => a.CreatedAt).FirstOrDefaultAsync(ct);
        var due = await db.Outgoing.Where(o => o.Status == "Scheduled").OrderBy(o => o.ApprovedAt).Take(10).ToListAsync(ct);
        var targets = due.Count == 0 ? [] : await jobs.EmailTargetsAsync(ct);
        var sent = 0;
        foreach (var m in due)
        {
            var (ok, wait, reason) = await GuardAsync(m, settings, account, targets, ct);
            if (!ok)
            {
                m.Error = reason;
                if (!wait) m.Status = "Failed";
                continue;
            }
            m.Status = "Sending";
            await db.SaveChangesAsync(ct);
            try
            {
                var msg = await BuildAsync(m, account!, ct);
                await SendAsync(account!, msg, ct);
                m.MessageId = msg.MessageId;
                m.Status = "Sent";
                m.SentAt = Now;
                m.Error = null;
                sent++;
                if (m.ThreadId is { } t) await ai.NoteAsync(new AgentThread(t, "mail", $"E-mail para {m.Company}: {m.JobTitle}"), "Guardião", $"Enviado para {m.To}", $"Message-ID {m.MessageId}",
                    new { stage = "sent", mailId = m.Id }, "Você", true, ct);
            }
            catch (Exception ex) when (ex is MailRuleException or IOException or SmtpCommandException or SmtpProtocolException or AuthenticationException or System.Net.Sockets.SocketException)
            {
                m.Status = "Failed";
                m.Error = ex.Message;
                log.LogWarning("Mail send failed: {Error}", ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return sent;
    }

    /// <summary>As regras do envio. wait = true quando é só esperar (janela, teto do dia); false quando o envio não pode sair nunca.</summary>
    private async Task<(bool Ok, bool Wait, string? Reason)> GuardAsync(OutgoingMail m, MailSettings s, MailAccount? account, IReadOnlyList<EmailTarget> targets, CancellationToken ct)
    {
        if (account is null) return (false, true, "Conecte uma conta de e-mail para enviar.");
        var target = targets.FirstOrDefault(t => t.Id == m.JobId);
        if (target is null || !string.Equals(target.ApplyEmail.Trim(), m.To, StringComparison.OrdinalIgnoreCase)) return (false, false, "O destino não é o e-mail que a vaga publica para candidatura.");
        if (m.Kind == "first" && await db.Outgoing.AnyAsync(o => o.JobId == m.JobId && o.Kind == "first" && o.Id != m.Id && (o.Status == "Sent" || o.Status == "Replied" || o.Status == "Sending"), ct))
            return (false, false, "Esta vaga já recebeu um e-mail.");
        if (m.Kind == "followup")
        {
            var first = await db.Outgoing.AsNoTracking().FirstOrDefaultAsync(o => o.Id == m.FollowsId, ct);
            if (first is not { Status: "Sent", SentAt: { } at }) return (false, false, "O lembrete só sai depois do primeiro e-mail, e não sai se a empresa já respondeu.");
            if (Now - at < TimeSpan.FromDays(s.FollowUpDays)) return (false, true, $"O lembrete espera {s.FollowUpDays} dias desde o primeiro e-mail.");
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(Now, Zone);
        if (local.Hour < s.WindowStart || local.Hour >= s.WindowEnd) return (false, true, $"Fora da janela de envio ({s.WindowStart}h às {s.WindowEnd}h). Sai na próxima.");
        var startOfDay = TimeZoneInfo.ConvertTimeToUtc(local.Date, Zone);
        var today = await db.Outgoing.CountAsync(o => o.SentAt >= startOfDay, ct);
        if (today >= s.DailyCap) return (false, true, $"Teto de {s.DailyCap} e-mails por dia atingido. Sai amanhã.");
        if (m.ApprovedAt is null && !(s.Mode == "Auto" && s.AutoConsentAt is not null)) return (false, true, "Esperando a sua aprovação.");
        return (true, false, null);
    }

    private async Task<MimeMessage> BuildAsync(OutgoingMail m, MailAccount a, CancellationToken ct)
    {
        var msg = new MimeMessage { Subject = m.Subject, MessageId = MimeUtils.GenerateMessageId(a.Address.Split('@')[1]) };
        msg.From.Add(new MailboxAddress(a.DisplayName, a.Address));
        msg.To.Add(MailboxAddress.Parse(m.To));
        if (m.FollowsId is { } f && await db.Outgoing.AsNoTracking().FirstOrDefaultAsync(o => o.Id == f, ct) is { MessageId: { } original })
        {
            msg.InReplyTo = original;
            msg.References.Add(original);
        }
        var body = new BodyBuilder { TextBody = m.Body };
        if (m.AttachmentPath is { } path && File.Exists(path)) await body.Attachments.AddAsync(m.AttachmentName ?? Path.GetFileName(path), File.OpenRead(path), new ContentType("application", "pdf"), ct);
        msg.Body = body.ToMessageBody();
        return msg;
    }

    private static async Task SendAsync(MailAccount a, MimeMessage msg, CancellationToken ct)
    {
        var password = await Secrets.GetAsync($"mail:{a.Id}", ct) ?? throw new MailRuleException("A senha da conta sumiu do gnome-keyring. Conecte a conta de novo.");
        using var smtp = new SmtpClient { Timeout = 30000 };
        await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, a.SmtpSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(a.Address, password, ct);
        await smtp.SendAsync(msg, ct);
        await smtp.DisconnectAsync(true, ct);
    }

    // ---------- Modo automático e lembretes (Worker) ----------

    /// <summary>
    /// No automático nada espera você: o rascunho limpo sai; o que o Conferente teve de cortar troca pelo esqueleto, só com fatos do perfil.
    /// Fica parado só o que nem o esqueleto resolve, como o currículo que não ficou pronto para o anexo.
    /// </summary>
    private async Task ReleaseAsync(OutgoingMail m, bool attach, CancellationToken ct)
    {
        if (m.CheckNotes is not null && m.Kind == "first" && m.DraftedBy != "modelo" && (m.AttachmentPath is not null || !attach))
        {
            m.Status = "Cancelled";
            await db.SaveChangesAsync(ct);
            m = await DraftAsync(m.JobId, "first", ct, plain: true);
        }
        if (m.CheckNotes is null) { m.Status = "Scheduled"; m.ApprovedAt = Now; }
        await db.SaveChangesAsync(ct);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var auto = s.Mode == "Auto" && s.AutoConsentAt is not null;
        if (s.Mode != "Draft" && await db.Accounts.AnyAsync(ct))
        {
            if (auto)
            {
                // Rascunho de antes do automático também sai, pelas mesmas regras.
                foreach (var old in await db.Outgoing.Where(o => o.Status == "AwaitingApproval").ToListAsync(ct))
                {
                    try { await ReleaseAsync(old, s.AttachResume, ct); }
                    catch (MailRuleException ex) { log.LogInformation("Auto release skipped: {Error}", ex.Message); }
                }
                var contacted = await db.Outgoing.Where(o => o.Status != "Cancelled").Select(o => o.JobId).Distinct().ToListAsync(ct);
                foreach (var t in (await jobs.EmailTargetsAsync(ct)).Where(t => !contacted.Contains(t.Id)).Take(2))
                {
                    try { await ReleaseAsync(await DraftAsync(t.Id, "first", ct), s.AttachResume, ct); }
                    catch (MailRuleException ex) { log.LogInformation("Auto draft skipped: {Error}", ex.Message); }
                }
            }
            var since = Now.AddDays(-s.FollowUpDays);
            var pending = await db.Outgoing.Where(o => o.Kind == "first" && o.Status == "Sent" && o.SentAt <= since).ToListAsync(ct);
            foreach (var first in pending)
            {
                if (await db.Outgoing.AnyAsync(o => o.FollowsId == first.Id, ct)) continue;
                try
                {
                    var f = await DraftAsync(first.JobId, "followup", ct);
                    if (auto) { f.Status = "Scheduled"; f.ApprovedAt = Now; await db.SaveChangesAsync(ct); }
                }
                catch (MailRuleException ex) { log.LogInformation("Follow-up skipped: {Error}", ex.Message); }
            }
        }
        await SendDueAsync(ct);
    }

    // ---------- Caixa de entrada (IMAP) ----------

    /// <summary>
    /// Lê só o que pode ser da busca: respostas aos e-mails enviados (pelo Message-ID), mensagens dos domínios para onde a pessoa escreveu
    /// e assuntos de processo seletivo. Classifica no modelo local (nunca vai para a nuvem) e para o lembrete quando a empresa respondeu.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        var stored = 0;
        foreach (var a in await db.Accounts.ToListAsync(ct))
        {
            if (a.ImapHost.Length == 0) continue;
            var password = await Secrets.GetAsync($"mail:{a.Id}", ct);
            if (password is null) { a.LastError = "Senha fora do gnome-keyring."; continue; }
            try
            {
                using var imap = new ImapClient { Timeout = 30000 };
                await imap.ConnectAsync(a.ImapHost, a.ImapPort, SecureSocketOptions.SslOnConnect, ct);
                await imap.AuthenticateAsync(a.Address, password, ct);
                var inbox = imap.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                SearchQuery query = a.LastUid > 0 ? SearchQuery.Uids(new UniqueIdRange(new UniqueId((uint)a.LastUid + 1), UniqueId.MaxValue)) : SearchQuery.DeliveredAfter(Now.AddDays(-14));
                var uids = await inbox.SearchAsync(query, ct);
                var sent = await db.Outgoing.Where(o => o.MessageId != null).Select(o => new { o.Id, o.MessageId, o.To, o.Status }).ToListAsync(ct);
                var byId = sent.ToDictionary(o => o.MessageId!.Trim('<', '>'), o => o.Id);
                var domains = sent.GroupBy(o => o.To.Split('@').Last().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Last().Id);
                foreach (var summary in await inbox.FetchAsync(uids.Take(300).ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.References | MessageSummaryItems.PreviewText, ct))
                {
                    a.LastUid = Math.Max(a.LastUid, summary.UniqueId.Id);
                    if (summary.Envelope is not { } env) continue;
                    var from = env.From.Mailboxes.FirstOrDefault()?.Address ?? "";
                    var refs = (summary.References ?? []).Append(env.InReplyTo ?? "").Select(r => r.Trim('<', '>')).Where(r => r.Length > 0);
                    Guid? link = refs.Select(r => byId.TryGetValue(r, out var g) ? g : (Guid?)null).FirstOrDefault(g => g is not null)
                        ?? (domains.TryGetValue(from.Split('@').Last().ToLowerInvariant(), out var d) ? d : null);
                    var subject = env.Subject ?? "";
                    // Alerta de vaga (LinkedIn, Indeed, Catho, InfoJobs...): as vagas do corpo vão para a fonte "alertas:email".
                    if (link is null && AlertParser.IsAlert(from, subject))
                    {
                        var message = await inbox.GetMessageAsync(summary.UniqueId, ct);
                        foreach (var job in AlertParser.Parse(from, message.HtmlBody ?? System.Net.WebUtility.HtmlEncode(message.TextBody ?? ""), env.Date?.UtcDateTime ?? Now))
                            if (!await db.AlertPostings.AnyAsync(x => x.Url == job.Url, ct) && !db.AlertPostings.Local.Any(x => x.Url == job.Url))
                            {
                                db.AlertPostings.Add(new AlertPosting { Sender = job.Sender, Title = job.Title, Company = job.Company, Location = job.Location, Url = job.Url, Snippet = job.Snippet, ReceivedAt = job.ReceivedAt });
                                stored++;
                            }
                        continue;
                    }
                    if (link is null && !Hiring().IsMatch(subject)) continue;
                    var messageId = env.MessageId ?? $"{a.Id}:{summary.UniqueId.Id}";
                    if (await db.Incoming.AnyAsync(x => x.MessageId == messageId, ct)) continue;
                    var preview = Cut(summary.PreviewText ?? "", 600);
                    // Aviso de falha na entrega volta no mesmo fio, mas não é resposta: o e-mail não chegou na empresa.
                    if (link is { } lost && Bounce(from, env.From.Mailboxes.FirstOrDefault()?.Name, subject))
                    {
                        db.Incoming.Add(new IncomingMail { MessageId = messageId, From = from, Subject = Cut(subject, 300), Date = env.Date?.UtcDateTime ?? Now, Snippet = preview, Class = "bounce", OutgoingId = lost, ReceivedAt = Now });
                        if (await db.Outgoing.FindAsync([lost], ct) is { Status: "Sent" or "Replied" } back) Bounced(back, preview);
                        foreach (var f in await db.Outgoing.Where(x => x.FollowsId == lost && (x.Status == "AwaitingApproval" || x.Status == "Scheduled")).ToListAsync(ct)) f.Status = "Cancelled";
                        stored++;
                        continue;
                    }
                    (string? cls, bool action) = await ClassifyAsync(from, subject, preview, ct);
                    if (link is null && cls is "other" or "recruiter_outreach" or null) continue;
                    db.Incoming.Add(new IncomingMail { MessageId = messageId, From = from, Subject = Cut(subject, 300), Date = env.Date?.UtcDateTime ?? Now, Snippet = preview, Class = cls, ActionNeeded = action, OutgoingId = link, ReceivedAt = Now });
                    if (link is { } id && await db.Outgoing.FindAsync([id], ct) is { } o && o.Status is "Sent")
                    {
                        o.Status = "Replied";
                        o.RepliedAt = Now;
                        o.ReplyClass = cls;
                        // A empresa respondeu: o lembrete agendado não sai mais.
                        foreach (var f in await db.Outgoing.Where(x => x.FollowsId == id && (x.Status == "AwaitingApproval" || x.Status == "Scheduled")).ToListAsync(ct)) f.Status = "Cancelled";
                    }
                    stored++;
                }
                await imap.DisconnectAsync(true, ct);
                a.CheckedAt = Now;
                a.LastError = null;
            }
            catch (Exception ex) when (ex is IOException or ImapCommandException or ImapProtocolException or AuthenticationException or System.Net.Sockets.SocketException)
            {
                a.LastError = ex.Message;
                log.LogWarning("IMAP poll failed: {Error}", ex.Message);
            }
            await db.SaveChangesAsync(ct);
        }
        return stored;
    }

    /// <summary>Remetente de aviso de entrega (mailer-daemon, postmaster) ou assunto de falha na entrega, em inglês ou português.</summary>
    internal static bool Bounce(string from, string? name, string subject) =>
        from.Split('@')[0].ToLowerInvariant() is "mailer-daemon" or "postmaster" or "mail-daemon"
        || name is { } n && n.Contains("Mail Delivery", StringComparison.OrdinalIgnoreCase)
        || BounceSubject().IsMatch(subject);

    /// <summary>O e-mail voltou: fica como não entregue, com o motivo do aviso, e não conta como resposta.</summary>
    internal static void Bounced(OutgoingMail m, string preview)
    {
        m.Status = "Bounced";
        m.RepliedAt = null;
        m.ReplyClass = null;
        var why = preview.Replace("*", "").Trim();
        m.Error = Cut($"Não chegou na empresa: {(why.Length > 0 ? why : "o servidor de lá recusou a mensagem.")}", 300);
    }

    [GeneratedRegex(@"delivery status notification|undeliver|returned to sender|delivery (has )?failed|mail delivery failed|failure notice|não (foi possível )?entreg|mensagem (não entregue|devolvida)", RegexOptions.IgnoreCase)]
    private static partial Regex BounceSubject();

    private async Task<(string?, bool)> ClassifyAsync(string from, string subject, string text, CancellationToken ct)
    {
        var res = await ai.CompleteAsync(LlmRequest.For(LlmTasks.EmailClassify, LocalTaskSpecs.EmailClassifySystem, LocalTaskSpecs.EmailClassifyPrompt(from, subject, text), LocalTaskSpecs.EmailClassifySchema, timeoutSeconds: 60), ct);
        if (!res.Ok || res.Json is null) return (null, false);
        try
        {
            using var d = JsonDocument.Parse(res.Json);
            return (d.RootElement.GetProperty("category").GetString(), d.RootElement.TryGetProperty("action_needed", out var a) && a.ValueKind == JsonValueKind.True);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { return (null, false); }
    }

    // ---------- Texto ----------

    private const string SystemPt = """
        Você escreve um e-mail curto de candidatura em português do Brasil, em nome de quem procura emprego, para o e-mail que a vaga publicou.
        Até 140 palavras, tom profissional e direto, sem exagero e sem clichê. Siga a estrutura do esqueleto. Use só os fatos verificados do currículo:
        não invente número, empresa, cargo, tempo de experiência nem resultado. Ligue de 2 a 3 fatos ao que a vaga pede. Não use travessão.
        Mantenha literais os marcadores {{nome}} e {{assinatura}} (o contato entra depois, fora da IA). Diga que o currículo vai em anexo.
        """;
    private const string SystemEn = """
        You write a short job application e-mail in English on behalf of the job seeker, to the address the job posting published.
        Up to 140 words, professional and direct, no hype or clichés. Follow the skeleton structure. Use only the verified resume facts: never invent numbers,
        companies, titles, years of experience or results. Tie 2 to 3 facts to what the job asks for. Do not use em dashes.
        Keep the {{nome}} and {{assinatura}} markers literally (contact details are added later, outside the AI). Mention the resume is attached.
        """;
    private const string Schema = """{"type":"object","properties":{"subject":{"type":"string"},"body":{"type":"string"}},"required":["subject","body"],"additionalProperties":false}""";

    /// <summary>O esqueleto preenchido só com dados locais: experiência atual e as competências do perfil que a vaga cita. Serve também de rascunho sem IA.</summary>
    private static (string Subject, string Body) Fill(MailTemplate t, EmailTarget target, ProfileSnapshot p, JobText? job, string language)
    {
        var current = p.Facts.FirstOrDefault(f => f.Kind == "experience" && f.End is null or "" or "atual" or "present") ?? p.Facts.FirstOrDefault(f => f.Kind == "experience");
        var text = Claims.Fold($"{job?.Title} {job?.Description}");
        var skills = p.Facts.Where(f => f.Kind == "skill").Select(f => f.Statement).Concat(p.Facts.SelectMany(f => f.Technologies))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(s => s.Length > 1 && text.Contains(Claims.Fold(s))).Take(5).ToList();
        var exp = current is null ? "" : language == "en" ? $"I currently work as {current.Role} at {current.Organization}." : $"Hoje atuo como {current.Role} na {current.Organization}.";
        var comp = skills.Count == 0 ? "" : language == "en" ? $"I work with {Join(skills, "and")}, which the role asks for." : $"Trabalho com {Join(skills, "e")}, que a vaga pede.";
        string Apply(string s) => s.Replace("{{empresa}}", target.Company).Replace("{{vaga}}", target.Title).Replace("{{experiencia}}", exp).Replace("{{competencias}}", comp)
            .Replace("{{link_vaga}}", target.ApplyUrl);
        return (Apply(t.Subject), Regex.Replace(Apply(t.Body), @"\n{3,}", "\n\n").Trim());
    }

    private static string Personal(string text, Identity id)
    {
        var signature = string.Join("\n", new[] { id.FullName, id.Phone, id.LinkedIn, id.Email }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return text.Replace("{{assinatura}}", signature).Replace("{{nome}}", id.FullName).Replace("{{primeiro_nome}}", id.FirstName);
    }

    private static string Line(ProfileFact f) => f.Kind == "experience" ? $"{f.Role} em {f.Organization} ({f.Start ?? "?"} a {f.End ?? "atual"}): {f.Statement}" : $"{f.Kind}: {f.Statement}";
    private static string Join(IReadOnlyList<string> items, string and) => items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} {and} {items[^1]}";
    private static string Cut(string s, int max) => s.Length > max ? s[..max] : s;
    private static string Slug(string s) => new string(s.Normalize(NormalizationForm.FormD).Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
    private static bool English(string text)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\b[a-zà-ú]+\b").Select(m => m.Value).Take(400).ToList();
        int en = words.Count(w => w is "the" or "and" or "with" or "you" or "will" or "our" or "for" or "experience"), pt = words.Count(w => w is "de" or "com" or "para" or "você" or "experiência" or "vaga" or "nossa" or "e");
        return en > pt;
    }

    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex Sentence();
    [GeneratedRegex(@"candidatura|processo seletivo|entrevista|vaga|application|interview|position|recruit|oferta|offer|teste t[eé]cnico|assessment", RegexOptions.IgnoreCase)] private static partial Regex Hiring();
}
