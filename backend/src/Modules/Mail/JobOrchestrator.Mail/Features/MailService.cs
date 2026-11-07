using System.Security.Cryptography;
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
        var lower = address.ToLowerInvariant();
        var existing = await db.Accounts.FirstOrDefaultAsync(x => x.Address.ToLower() == lower, ct);
        var id = existing?.Id ?? account.Id;
        if (!await Secrets.StoreAsync($"mail:{id}", password, ct)) throw new MailRuleException("O chaveiro do sistema não aceitou guardar a senha. Confira se ele está desbloqueado.");
        if (account.DisplayName.Length == 0) account.DisplayName = existing?.DisplayName is { Length: > 0 } kept ? kept : (await profile.CurrentAsync(ct))?.Identity.FullName ?? "";
        if (existing is not null)
        {
            existing.Provider = account.Provider; existing.DisplayName = account.DisplayName;
            existing.SmtpHost = account.SmtpHost; existing.SmtpPort = account.SmtpPort; existing.SmtpSsl = account.SmtpSsl;
            existing.ImapHost = account.ImapHost; existing.ImapPort = account.ImapPort;
            existing.CheckedAt = Now; existing.LastError = null; existing.PausedUntil = null; existing.PauseReason = null;
            await db.SaveChangesAsync(ct);
            return existing;
        }
        account.CheckedAt = Now;
        db.Accounts.Add(account);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>Liga ou desliga o envio por uma conta; desligada, ela continua lendo a caixa.</summary>
    public async Task SetSendsAsync(Guid id, bool sends, CancellationToken ct)
    {
        var a = await db.Accounts.FindAsync([id], ct) ?? throw new MailRuleException("Conta não encontrada.");
        a.Sends = sends;
        await db.SaveChangesAsync(ct);
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
        await SendAsync(a, msg, () => { }, ct);
    }

    private static async Task TestSmtpAsync(MailAccount a, string password, CancellationToken ct)
    {
        using var smtp = MailTls.Secure(new SmtpClient { Timeout = 20000 });
        try
        {
            await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, Socket(a.SmtpHost, a.SmtpSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls), ct);
            await smtp.AuthenticateAsync(a.Address, password, ct);
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (WebLoginRequired(ex)) { throw new MailRuleException(WebLoginMessage); }
        catch (Exception ex) when (ex is AuthenticationException || RefusedLogin(ex)) { throw new MailRuleException(LoginRefusedMessage(a)); }
        catch (SslHandshakeException) { throw new MailRuleException($"Não foi possível abrir uma conexão segura com {a.SmtpHost}:{a.SmtpPort}: o certificado do servidor não foi aceito."); }
        catch (Exception ex) when (ex is IOException or SmtpCommandException or SmtpProtocolException or System.Net.Sockets.SocketException)
        { throw new MailRuleException($"Não foi possível falar com {a.SmtpHost}:{a.SmtpPort}: {ex.Message}"); }
    }

    // Gmail answers a wrong password with 535 5.7.8 and drops the connection, which MailKit reports as a disconnect, not as a login error.
    internal static bool RefusedLogin(Exception ex) =>
        ex is SmtpCommandException or SmtpProtocolException
        && (ex.Message.Contains("5.7.8", StringComparison.Ordinal) || ex.Message.Contains("535", StringComparison.Ordinal)
            || ex.Message.Contains("Username and Password not accepted", StringComparison.OrdinalIgnoreCase));

    // Gmail answers 5.7.9 (SMTP) or a "log in via your web browser" alert (IMAP) when it wants the sign-in confirmed in a browser first.
    internal static bool WebLoginRequired(Exception ex) =>
        ex is SmtpCommandException or SmtpProtocolException or AuthenticationException
        && (ex.Message.Contains("5.7.9", StringComparison.Ordinal) || ex.Message.Contains("WebLoginRequired", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("web browser", StringComparison.OrdinalIgnoreCase));

    private const string WebLoginMessage = "O Gmail barrou este login por segurança e quer uma confirmação pelo navegador. Entre no gmail.com com esta conta neste computador, "
        + "confirme em myaccount.google.com/notifications que foi você e tente de novo daqui a um minuto.";

    private static string LoginRefusedMessage(MailAccount a) => a.Provider == "gmail"
        ? "O Gmail recusou o login. Use uma senha de app de 16 letras, criada em myaccount.google.com/apppasswords com a verificação em duas etapas ligada. A senha normal da conta não funciona aqui."
        : "O servidor recusou o login. Confira se é uma senha de app, não a senha normal da conta.";

    private static async Task TestImapAsync(MailAccount a, string password, CancellationToken ct)
    {
        using var imap = MailTls.Secure(new ImapClient { Timeout = 20000 });
        try
        {
            await imap.ConnectAsync(a.ImapHost, a.ImapPort, Socket(a.ImapHost, SecureSocketOptions.SslOnConnect), ct);
            await imap.AuthenticateAsync(a.Address, password, ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (WebLoginRequired(ex)) { throw new MailRuleException(WebLoginMessage); }
        catch (AuthenticationException) { throw new MailRuleException("O envio funcionou, mas a leitura (IMAP) recusou o login. No Gmail, confira se o IMAP está ligado nas configurações."); }
        catch (SslHandshakeException) { throw new MailRuleException($"Não foi possível abrir uma conexão segura com {a.ImapHost}:{a.ImapPort}: o certificado do servidor não foi aceito."); }
        catch (Exception ex) when (ex is IOException or ImapCommandException or ImapProtocolException or System.Net.Sockets.SocketException)
        { throw new MailRuleException($"Não foi possível ler a caixa em {a.ImapHost}:{a.ImapPort}: {ex.Message}"); }
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
        if (input.Mode != "Auto")
        {
            s.AutoConsentAt = null;
            await SuspendAutoAsync(ct);
        }
        s.Mode = input.Mode;
        // Your number: the provider is the one that stops an account (QuotaHit pauses it until the next day).
        s.DailyCap = Math.Max(1, input.DailyCap);
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
        // A língua do e-mail: a da vaga quando é português ou inglês, ou outra que você fala (perfil); senão, inglês.
        var writing = Languages.Writing(Languages.DetectAny($"{target.Title} {text?.Description}"), p.Facts.Where(f => f.Kind == "language").Select(f => $"{f.Statement} {f.Level}"));
        var language = writing == "pt" ? "pt" : "en";
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
            // Fora do português e do inglês, o esqueleto vai em inglês e a IA escreve na língua da vaga, que você fala.
            if (writing is not ("pt" or "en"))
                prompt += $"\n\nWrite the subject and the whole body in {Languages.EnglishName(writing)}, the language of the job posting. Keep every fact exactly as given.";
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
            AttachmentSha256 = pdf is null ? null : Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(pdf, ct))),
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
        await AttachmentAsync(m, ct);
        m.Status = "Scheduled";
        m.ApprovedAt = Now;
        m.ApprovedBy = "you";
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

    internal const string Unreleased = "O envio automático foi desligado depois que este e-mail foi liberado: ele espera a sua aprovação.";

    /// <summary>E-mails the automatic mode released and that have not left go back to your approval. Your own approvals stay.</summary>
    public async Task<int> SuspendAutoAsync(CancellationToken ct)
    {
        var released = await db.Outgoing.Where(o => o.Status == "Scheduled" && o.ApprovedBy == "auto").ToListAsync(ct);
        foreach (var m in released) Unrelease(m);
        await db.SaveChangesAsync(ct);
        return released.Count;
    }

    private static void Unrelease(OutgoingMail m)
    {
        m.Status = "AwaitingApproval";
        m.ApprovedAt = null;
        m.ApprovedBy = null;
        m.Error = Unreleased;
    }

    /// <summary>You checked the account's Sent folder: it left (Sent) or it did not (back to the queue, with the same Message-ID).</summary>
    public async Task<OutgoingMail> ResolveAsync(Guid id, bool sent, CancellationToken ct)
    {
        var m = await db.Outgoing.FindAsync([id], ct) ?? throw new MailRuleException("E-mail não encontrado.");
        if (m.Status != "Uncertain") throw new MailRuleException("Este e-mail não está em dúvida.");
        if (sent) { m.Status = "Sent"; m.SentAt ??= Now; }
        else m.Status = "Scheduled";
        m.Error = null;
        await db.SaveChangesAsync(ct);
        return m;
    }

    // ---------- Guardião e envio ----------

    /// <summary>
    /// Test sandboxes only (JO_MAIL_PLAIN_LOOPBACK=1, set by the night rehearsal): a local mail catcher on this machine, without TLS.
    /// Any other host keeps its TLS, whatever the variable says.
    /// </summary>
    internal static SecureSocketOptions Socket(string host, SecureSocketOptions normal) =>
        Environment.GetEnvironmentVariable("JO_MAIL_PLAIN_LOOPBACK") == "1" && host is "127.0.0.1" or "localhost" ? SecureSocketOptions.None : normal;

    internal const string UncertainNote = "O envio foi interrompido depois de começar, e o servidor pode ter aceitado o e-mail. Confira a pasta Enviados da conta antes de mandar de novo.";

    /// <summary>
    /// Envia o que está agendado e passa no Guardião. Um processo por vez (trava de sessão no Postgres), para API e Worker nunca mandarem em dobro.
    /// Each e-mail is marked Sending, with its Message-ID, in its own commit before the SMTP call. If the process dies after that, the e-mail
    /// turns Uncertain on the next run and waits for you instead of going out twice.
    /// </summary>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await db.Database.SqlQueryRaw<bool>("select pg_try_advisory_lock(4747010) as \"Value\"").SingleAsync(ct)) return 0;
            try { return await SendLockedAsync(ct); }
            finally { await db.Database.ExecuteSqlRawAsync("select pg_advisory_unlock(4747010)", CancellationToken.None); }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    private async Task<int> SendLockedAsync(CancellationToken ct)
    {
        // With the lock held nobody else is sending, so Sending here was left by a process that stopped mid-send.
        foreach (var stuck in await db.Outgoing.Where(o => o.Status == "Sending").ToListAsync(ct))
        {
            stuck.Status = "Uncertain";
            stuck.Error = UncertainNote;
            log.LogWarning("Mail {Id} was left sending by a stopped process; marked uncertain", stuck.Id);
        }
        await db.SaveChangesAsync(ct);
        var settings = await SettingsAsync(ct);
        // Automatic mode off (on the Mail screen or by "revogar tudo"), or a campaign that only searches or prepares: what the automatic
        // mode released goes back to you before anything is picked.
        if (!(settings.Mode == "Auto" && settings.AutoConsentAt is not null) || !await jobs.SendingAllowedAsync(ct)) await SuspendAutoAsync(ct);
        var accounts = await db.Accounts.OrderBy(a => a.CreatedAt).ToListAsync(ct);
        var sentToday = await SentTodayAsync(ct);
        var due = await db.Outgoing.Where(o => o.Status == "Scheduled").OrderBy(o => o.ApprovedAt).Take(10).ToListAsync(ct);
        var targets = due.Count == 0 ? [] : await jobs.EmailTargetsAsync(ct);
        var sent = 0;
        foreach (var m in due)
        {
            Guid? sameAs = m.FollowsId is { } f ? await db.Outgoing.AsNoTracking().Where(o => o.Id == f).Select(o => o.AccountId).FirstOrDefaultAsync(ct) : null;
            for (var tries = 0; tries <= accounts.Count; tries++)
            {
                var account = MailRotation.Pick(accounts, sentToday, settings.DailyCap, sameAs, Now);
                var (ok, wait, reason) = await GuardAsync(m, settings, account, targets, ct, Unavailable(accounts, sameAs, settings.DailyCap));
                if (!ok)
                {
                    m.Error = reason;
                    if (!wait) m.Status = "Failed";
                    break;
                }
                MimeMessage msg;
                try { msg = await BuildAsync(m, account!, ct); }
                catch (MailRuleException ex)
                {
                    m.Status = "Failed";
                    m.Error = ex.Message;
                    break;
                }
                m.Status = "Sending";
                m.AccountId = account!.Id;
                await db.SaveChangesAsync(ct);
                var transfer = false;
                try
                {
                    await SendAsync(account, msg, () => transfer = true, ct);
                    m.Status = "Sent";
                    m.SentAt = Now;
                    m.Error = null;
                    // Applications learns it in the same commit: the e-mail application is sent, with the server's acceptance as evidence.
                    db.Publish(JobEmailSentV1.EventType, new JobEmailSentV1(m.JobId, m.To, m.MessageId!, m.SentAt.Value, m.Kind), Now);
                    sentToday[account.Id] = sentToday.GetValueOrDefault(account.Id) + 1;
                    sent++;
                    await db.SaveChangesAsync(ct);
                    if (m.ThreadId is { } t) await ai.NoteAsync(new AgentThread(t, "mail", $"E-mail para {m.Company}: {m.JobTitle}"), "Guardião", $"Enviado para {m.To} por {account.Address}", $"Message-ID {m.MessageId}",
                        new { stage = "sent", mailId = m.Id }, "Você", true, ct);
                    break;
                }
                catch (Exception ex) when (MailRotation.QuotaHit(ex))
                {
                    account.PausedUntil = TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTimeFromUtc(Now, Zone).Date.AddDays(1), Zone);
                    account.PauseReason = "O provedor avisou que esta conta chegou ao limite de envio. Volta amanhã.";
                    m.Status = "Scheduled";
                    m.AccountId = null;
                    m.MessageId = null;
                    m.Error = $"{account.Address} chegou ao limite do provedor; o e-mail vai pela próxima conta.";
                    log.LogWarning("Mail account {Address} hit the provider limit: {Error}", account.Address, ex.Message);
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (transfer && ex is IOException or SmtpProtocolException or System.Net.Sockets.SocketException or OperationCanceledException)
                {
                    // The connection fell during the transfer: the server may have taken it. Nothing goes again on its own.
                    m.Status = "Uncertain";
                    m.Error = UncertainNote;
                    log.LogWarning("Mail {Id} transfer was cut ({Error}); marked uncertain", m.Id, ex.Message);
                    break;
                }
                catch (Exception ex) when (ex is MailRuleException or IOException or SmtpCommandException or SmtpProtocolException or AuthenticationException or System.Net.Sockets.SocketException)
                {
                    m.Status = "Failed";
                    m.Error = ex.Message;
                    log.LogWarning("Mail send failed: {Error}", ex.Message);
                    break;
                }
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return sent;
    }

    /// <summary>Quantos e-mails cada conta mandou hoje, no fuso da campanha.</summary>
    internal async Task<Dictionary<Guid, int>> SentTodayAsync(CancellationToken ct)
    {
        var start = TimeZoneInfo.ConvertTimeToUtc(TimeZoneInfo.ConvertTimeFromUtc(Now, Zone).Date, Zone);
        return await db.Outgoing.Where(o => o.SentAt >= start && o.AccountId != null).GroupBy(o => o.AccountId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
    }

    private static string Unavailable(IReadOnlyList<MailAccount> accounts, Guid? sameAs, int cap) =>
        accounts.Count == 0 ? "Conecte uma conta de e-mail para enviar."
        : sameAs is { } id && accounts.FirstOrDefault(a => a.Id == id) is { } owner ? $"O lembrete sai pela mesma conta do primeiro e-mail ({owner.Address}), que já chegou ao teto de hoje ou está pausada. Sai amanhã."
        : accounts.All(a => !a.Sends) ? "Nenhuma conta está marcada para enviar."
        : $"Todas as contas chegaram ao teto de {cap} e-mails por conta hoje ou estão pausadas. Sai amanhã.";

    /// <summary>As regras do envio. wait = true quando é só esperar (janela, teto do dia); false quando o envio não pode sair nunca.</summary>
    private async Task<(bool Ok, bool Wait, string? Reason)> GuardAsync(OutgoingMail m, MailSettings s, MailAccount? account, IReadOnlyList<EmailTarget> targets, CancellationToken ct, string unavailable)
    {
        if (account is null) return (false, true, unavailable);
        var target = targets.FirstOrDefault(t => t.Id == m.JobId);
        if (target is null || !string.Equals(target.ApplyEmail.Trim(), m.To, StringComparison.OrdinalIgnoreCase)) return (false, false, "O destino não é o e-mail que a vaga publica para candidatura.");
        if (m.Kind == "first" && await db.Outgoing.AnyAsync(o => o.JobId == m.JobId && o.Kind == "first" && o.Id != m.Id && (o.Status == "Sent" || o.Status == "Replied" || o.Status == "Sending" || o.Status == "Uncertain"), ct))
            return (false, false, "Esta vaga já recebeu um e-mail.");
        if (m.Kind == "followup")
        {
            var first = await db.Outgoing.AsNoTracking().FirstOrDefaultAsync(o => o.Id == m.FollowsId, ct);
            if (first is not { Status: "Sent", SentAt: { } at }) return (false, false, "O lembrete só sai depois do primeiro e-mail, e não sai se a empresa já respondeu.");
            if (Now - at < TimeSpan.FromDays(s.FollowUpDays)) return (false, true, $"O lembrete espera {s.FollowUpDays} dias desde o primeiro e-mail.");
        }
        var local = TimeZoneInfo.ConvertTimeFromUtc(Now, Zone);
        if (local.Hour < s.WindowStart || local.Hour >= s.WindowEnd) return (false, true, $"Fora da janela de envio ({s.WindowStart}h às {s.WindowEnd}h). Sai na próxima.");
        if (m.ApprovedAt is null && !(s.Mode == "Auto" && s.AutoConsentAt is not null)) return (false, true, "Esperando a sua aprovação.");
        return (true, false, null);
    }

    private async Task<MimeMessage> BuildAsync(OutgoingMail m, MailAccount a, CancellationToken ct)
    {
        m.MessageId ??= MimeUtils.GenerateMessageId(a.Address.Split('@')[1]);
        var msg = new MimeMessage { Subject = m.Subject, MessageId = m.MessageId };
        msg.From.Add(new MailboxAddress(a.DisplayName, a.Address));
        msg.To.Add(MailboxAddress.Parse(m.To));
        if (m.FollowsId is { } f && await db.Outgoing.AsNoTracking().FirstOrDefaultAsync(o => o.Id == f, ct) is { MessageId: { } original })
        {
            msg.InReplyTo = original;
            msg.References.Add(original);
        }
        var body = new BodyBuilder { TextBody = m.Body };
        if (await AttachmentAsync(m, ct) is { } pdf) body.Attachments.Add(m.AttachmentName ?? Path.GetFileName(m.AttachmentPath!), pdf, new ContentType("application", "pdf"));
        msg.Body = body.ToMessageBody();
        return msg;
    }

    /// <summary>
    /// The resume exactly as the draft was made with it. A missing or changed file stops the e-mail with the reason, instead of
    /// sending without the resume or with another version.
    /// </summary>
    internal static async Task<byte[]?> AttachmentAsync(OutgoingMail m, CancellationToken ct)
    {
        if (m.AttachmentPath is not { } path) return null;
        if (!File.Exists(path)) throw new MailRuleException($"O currículo deste e-mail não existe mais ({Path.GetFileName(path)}). Gere o rascunho de novo.");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (m.AttachmentSha256 is { } sha && !string.Equals(sha, Convert.ToHexStringLower(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase))
            throw new MailRuleException("O currículo mudou depois que este rascunho foi feito. Gere o rascunho de novo para mandar a versão atual.");
        return bytes;
    }

    private static async Task SendAsync(MailAccount a, MimeMessage msg, Action transfer, CancellationToken ct)
    {
        var password = await Secrets.GetAsync($"mail:{a.Id}", ct) ?? throw new MailRuleException("A senha da conta não está mais no chaveiro do sistema. Conecte a conta de novo.");
        using var smtp = MailTls.Secure(new SmtpClient { Timeout = 30000 });
        await smtp.ConnectAsync(a.SmtpHost, a.SmtpPort, Socket(a.SmtpHost, a.SmtpSsl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls), ct);
        await smtp.AuthenticateAsync(a.Address, password, ct);
        transfer();
        await smtp.SendAsync(msg, ct);
        // SendAsync returned, so the server took the message: a failure to say goodbye changes nothing.
        try { await smtp.DisconnectAsync(true, ct); }
        catch (Exception ex) when (ex is IOException or SmtpProtocolException or System.Net.Sockets.SocketException) { }
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
        if (m.CheckNotes is null)
        {
            try
            {
                await AttachmentAsync(m, ct);
                m.Status = "Scheduled";
                m.ApprovedAt = Now;
                m.ApprovedBy = "auto";
            }
            catch (MailRuleException ex) { m.Error = ex.Message; }
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task TickAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        var auto = s.Mode == "Auto" && s.AutoConsentAt is not null && await jobs.SendingAllowedAsync(ct);
        if (s.Mode != "Draft" && await db.Accounts.AnyAsync(ct))
        {
            if (auto)
            {
                // Só sai sozinho o e-mail de vaga que confirma aceitar quem mora no Brasil; o da remota "a confirmar" espera você.
                var targets = await jobs.EmailTargetsAsync(ct);
                var open = targets.Where(t => AutoEligibility.Allows(t.Eligibility)).Select(t => t.Id).ToHashSet();
                // Rascunho de antes do automático também sai, pelas mesmas regras.
                foreach (var old in await db.Outgoing.Where(o => o.Status == "AwaitingApproval").ToListAsync(ct))
                {
                    if (!open.Contains(old.JobId)) continue;
                    try { await ReleaseAsync(old, s.AttachResume, ct); }
                    catch (MailRuleException ex) { log.LogInformation("Auto release skipped: {Error}", ex.Message); }
                }
                var contacted = await db.Outgoing.Where(o => o.Status != "Cancelled").Select(o => o.JobId).Distinct().ToListAsync(ct);
                foreach (var t in targets.Where(t => !contacted.Contains(t.Id)).Take(2))
                {
                    try
                    {
                        var m = await DraftAsync(t.Id, "first", ct);
                        if (open.Contains(t.Id)) await ReleaseAsync(m, s.AttachResume, ct);
                    }
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
                    if (auto) { f.Status = "Scheduled"; f.ApprovedAt = Now; f.ApprovedBy = "auto"; await db.SaveChangesAsync(ct); }
                }
                catch (MailRuleException ex) { log.LogInformation("Follow-up skipped: {Error}", ex.Message); }
            }
        }
        await SendDueAsync(ct);
    }

    // ---------- Caixa de entrada (IMAP) ----------

    /// <summary>
    /// Which of your e-mails a message answers: the thread references first; then the sender's domain, only when one of your e-mails
    /// went there. Several jobs at that domain and no reference: ambiguous, for you to say.
    /// </summary>
    internal static (Guid? Link, bool Ambiguous) ReplyLink(IReadOnlyDictionary<string, Guid> byId, IReadOnlyDictionary<string, Guid?> domains, IEnumerable<string> refs, string from)
    {
        var threaded = refs.Select(r => byId.TryGetValue(r, out var g) ? g : (Guid?)null).FirstOrDefault(g => g is not null);
        if (threaded is not null) return (threaded, false);
        return domains.TryGetValue(from.Split('@').Last().ToLowerInvariant(), out var only) ? (only, only is null) : (null, false);
    }

    /// <summary>
    /// Lê só o que pode ser da busca: respostas aos e-mails enviados (pelo Message-ID), mensagens dos domínios para onde a pessoa escreveu
    /// e assuntos de processo seletivo. Classifica no modelo local (nunca vai para a nuvem) e para o lembrete quando a empresa respondeu.
    /// </summary>
    public async Task<int> PollAsync(CancellationToken ct)
    {
        await ReclassifyAsync(ct);
        var stored = 0;
        foreach (var a in await db.Accounts.ToListAsync(ct))
        {
            if (a.ImapHost.Length == 0) continue;
            var password = await Secrets.GetAsync($"mail:{a.Id}", ct);
            if (password is null) { a.LastError = "Senha fora do chaveiro do sistema."; continue; }
            try
            {
                using var imap = MailTls.Secure(new ImapClient { Timeout = 30000 });
                await imap.ConnectAsync(a.ImapHost, a.ImapPort, Socket(a.ImapHost, SecureSocketOptions.SslOnConnect), ct);
                await imap.AuthenticateAsync(a.Address, password, ct);
                var inbox = imap.Inbox;
                await inbox.OpenAsync(FolderAccess.ReadOnly, ct);
                if (inbox.UidValidity != a.UidValidity)
                {
                    // Renumbered box (or the first read): the cursor starts over; Message-IDs already stored keep it from counting twice.
                    a.LastUid = 0;
                    a.UidValidity = inbox.UidValidity;
                }
                SearchQuery query = a.LastUid > 0 ? SearchQuery.Uids(new UniqueIdRange(new UniqueId((uint)a.LastUid + 1), UniqueId.MaxValue)) : SearchQuery.DeliveredAfter(Now.AddDays(-14));
                var uids = await inbox.SearchAsync(query, ct);
                var sent = await db.Outgoing.Where(o => o.MessageId != null).Select(o => new { o.Id, o.MessageId, o.To, o.Status }).ToListAsync(ct);
                var byId = sent.ToDictionary(o => o.MessageId!.Trim('<', '>'), o => o.Id);
                // By domain only when one of your e-mails went there; with several jobs at the same company or agency, guessing would pick wrong.
                var domains = sent.GroupBy(o => o.To.Split('@').Last().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count() == 1 ? g.Single().Id : (Guid?)null);
                foreach (var summary in await inbox.FetchAsync(uids.Take(300).ToList(), MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.References | MessageSummaryItems.PreviewText, ct))
                {
                    a.LastUid = Math.Max(a.LastUid, summary.UniqueId.Id);
                    if (summary.Envelope is not { } env) continue;
                    var from = env.From.Mailboxes.FirstOrDefault()?.Address ?? "";
                    var refs = (summary.References ?? []).Append(env.InReplyTo ?? "").Select(r => r.Trim('<', '>')).Where(r => r.Length > 0);
                    var (link, ambiguous) = ReplyLink(byId, domains, refs, from);
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
                    if (link is null && !ambiguous && !Hiring().IsMatch(subject)) continue;
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
                    // Rules first (newsletters, promotions, work tools, closed jobs); the model decides the rest and its answer is checked.
                    (string? cls, bool action) = Rule(from, subject, preview) is { } ruled ? (ruled, false) : await ClassifyAsync(from, subject, preview, ct);
                    cls = Checked(cls, subject, preview);
                    // A recruiter writing first is a new opportunity, kept without a job; only unrelated mail is skipped.
                    if (link is null && !ambiguous && cls is "other" or "job_alert" or null) continue;
                    db.Incoming.Add(new IncomingMail { MessageId = messageId, From = from, Subject = Cut(subject, 300), Date = env.Date?.UtcDateTime ?? Now, Snippet = preview, Class = cls, ActionNeeded = action || ambiguous, OutgoingId = link, Ambiguous = ambiguous, ReceivedAt = Now });
                    if (link is { } id && await db.Outgoing.FindAsync([id], ct) is { } o && o.Status is "Sent")
                    {
                        o.Status = "Replied";
                        o.RepliedAt = Now;
                        o.ReplyClass = cls;
                        db.Publish(JobEmailRepliedV1.EventType, new JobEmailRepliedV1(o.JobId, cls, Now), Now);
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

    /// <summary>
    /// What a message is, by rules before the model: job-board newsletters, promotions and work tools are not replies to an application,
    /// and a closed job says so. Null when the rules leave it to the model.
    /// </summary>
    internal static string? Rule(string from, string subject, string text)
    {
        var all = $"{subject}\n{text}";
        if (WorkTool().IsMatch(from)) return "other";
        if (Closed().IsMatch(all)) return "job_closed";
        if (Newsletter().IsMatch(all)) return "job_alert";
        if (Promotion().IsMatch(subject)) return "other";
        return null;
    }

    /// <summary>The model's class must have its own words in the message: an invite talks about an interview, an offer about a job proposal or pay.</summary>
    internal static string? Checked(string? category, string subject, string text)
    {
        var all = $"{subject}\n{text}";
        return category switch
        {
            "interview_invite" when !InterviewWords().IsMatch(all) => "other",
            "offer" when !OfferWords().IsMatch(all) => "other",
            "assessment" when !AssessmentWords().IsMatch(all) => "other",
            _ => category
        };
    }

    [GeneratedRegex(@"@(?:[\w.-]+\.)?(?:atlassian\.net|atlassian\.com|github\.com|gitlab\.com|slack\.com|notion\.so|figma\.com|trello\.com|asana\.com|linear\.app)\b|^jira@", RegexOptions.IgnoreCase)]
    private static partial Regex WorkTool();
    [GeneratedRegex(@"vaga (?:foi )?(?:finalizada|encerrada|preenchida|cancelada|congelada)|processo (?:seletivo )?(?:foi )?(?:encerrado|finalizado|cancelado)|position (?:has been|was) (?:filled|closed)|no longer accepting|job (?:has been|was) closed", RegexOptions.IgnoreCase)]
    private static partial Regex Closed();
    [GeneratedRegex(@"vagas? que combina|vagas? recomendada|vagas? para voc[eê]|novas vagas|achamos que voc[eê] pode se interessar|jobs? (?:you may|you might|for you|recommended)|new jobs|job alert|alerta de vaga", RegexOptions.IgnoreCase)]
    private static partial Regex Newsletter();
    [GeneratedRegex(@"oferta especial|desconto|promo[cç][aã]o|[uú]ltimos? dias?|black friday|cupom|% ?off|aproveite|sale\b|special offer|discount", RegexOptions.IgnoreCase)]
    private static partial Regex Promotion();
    [GeneratedRegex(@"entrevista|interview|bate[- ]papo|conversa com|agendar|agendamento|schedule|call com|chamada de v[ií]deo", RegexOptions.IgnoreCase)]
    private static partial Regex InterviewWords();
    [GeneratedRegex(@"proposta (?:de trabalho|de emprego|formal|salarial)|carta[- ]oferta|offer letter|job offer|remunera[cç][aã]o|sal[aá]rio|admiss[aã]o|contrata[cç][aã]o|compensation|salary", RegexOptions.IgnoreCase)]
    private static partial Regex OfferWords();
    [GeneratedRegex(@"teste t[eé]cnico|desafio|case t[eé]cnico|assessment|challenge|take[- ]home|hackerrank|codility|avalia[cç][aã]o t[eé]cnica|prova", RegexOptions.IgnoreCase)]
    private static partial Regex AssessmentWords();

    /// <summary>Messages read before these rules existed get them now: an unlinked "invite" that is a newsletter stops pretending to be one.</summary>
    internal async Task<int> ReclassifyAsync(CancellationToken ct)
    {
        var loose = await db.Incoming.Where(i => i.OutgoingId == null && i.Class != "bounce" && i.Class != "job_alert" && i.Class != "other" && i.Class != "job_closed").ToListAsync(ct);
        var changed = 0;
        foreach (var i in loose)
        {
            var cls = Rule(i.From, i.Subject, i.Snippet) ?? Checked(i.Class, i.Subject, i.Snippet);
            if (cls == i.Class) continue;
            i.Class = cls;
            i.ActionNeeded = i.ActionNeeded && cls is not ("other" or "job_alert" or "job_closed");
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

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

    [GeneratedRegex(@"(?<=[.!?])\s+")] private static partial Regex Sentence();
    [GeneratedRegex(@"candidatura|processo seletivo|entrevista|vaga|application|interview|position|recruit|oferta|offer|teste t[eé]cnico|assessment", RegexOptions.IgnoreCase)] private static partial Regex Hiring();
}
