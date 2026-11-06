namespace JobOrchestrator.Mail.Data;

/// <summary>
/// Conta de envio e leitura. A senha de app fica no chaveiro do sistema (conta mail:{Id}), nunca aqui. Com várias contas, os envios
/// se revezam entre as que enviam; uma conta que bate o limite do provedor pausa até o dia seguinte.
/// </summary>
internal sealed class MailAccount
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Provider { get; set; } = "smtp";
    public string Address { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; }
    public bool SmtpSsl { get; set; }
    public string ImapHost { get; set; } = "";
    public int ImapPort { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CheckedAt { get; set; }
    public string? LastError { get; set; }
    public long LastUid { get; set; }
    public bool Sends { get; set; } = true;
    public DateTime? PausedUntil { get; set; }
    public string? PauseReason { get; set; }
}

/// <summary>
/// Como o e-mail anda: Draft (só rascunho), Review (cada envio espera o seu ok, padrão) ou Auto (envia sozinho dentro dos limites,
/// só depois do consentimento registrado). Teto por dia, janela de horário e dias até o lembrete.
/// </summary>
internal sealed class MailSettings
{
    public int Id { get; init; } = 1;
    public string Mode { get; set; } = "Review";
    public int DailyCap { get; set; } = 5;
    public int FollowUpDays { get; set; } = 5;
    public int WindowStart { get; set; } = 8;
    public int WindowEnd { get; set; } = 19;
    public bool AttachResume { get; set; } = true;
    public DateTime? AutoConsentAt { get; set; }
    public string? AutoConsentText { get; set; }
}

/// <summary>Esqueleto de e-mail com marcadores ({{empresa}}, {{vaga}}...): a IA escreve em cima dele, e sem IA ele é preenchido como está.</summary>
internal sealed class MailTemplate
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "first";
    public string Language { get; set; } = "pt";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Um e-mail para a vaga: rascunho, esperando aprovação, agendado, enviado, respondido, falhou ou cancelado.</summary>
internal sealed class OutgoingMail
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid JobId { get; init; }
    public string Company { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string To { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string Kind { get; set; } = "first";
    public Guid? FollowsId { get; set; }
    public Guid? AccountId { get; set; }
    public string Status { get; set; } = "Draft";
    public DateTime CreatedAt { get; init; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public string? MessageId { get; set; }
    public string? AttachmentPath { get; set; }
    public string? AttachmentName { get; set; }
    public string? Error { get; set; }
    public string DraftedBy { get; set; } = "";
    public string? CheckNotes { get; set; }
    public DateTime? RepliedAt { get; set; }
    public string? ReplyClass { get; set; }
    public Guid? ThreadId { get; set; }
}

/// <summary>Resposta lida da caixa (IMAP), ligada ao envio pelo Message-ID ou pelo domínio de quem respondeu. Classificada no modelo local.</summary>
internal sealed class IncomingMail
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string MessageId { get; set; } = "";
    public string From { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime Date { get; set; }
    public string Snippet { get; set; } = "";
    public string? Class { get; set; }
    public bool ActionNeeded { get; set; }
    public Guid? OutgoingId { get; set; }
    public DateTime ReceivedAt { get; init; }
}

/// <summary>Vaga lida num alerta de vaga da caixa de e-mail (uma por link, sem rastreio).</summary>
internal sealed class AlertPosting
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public string Sender { get; set; } = "";
    public string Title { get; set; } = "";
    public string Company { get; set; } = "";
    public string Location { get; set; } = "";
    public string Url { get; set; } = "";
    public string Snippet { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}

internal sealed class MailDbContext(DbContextOptions<MailDbContext> options) : DbContext(options)
{
    public DbSet<AlertPosting> AlertPostings => Set<AlertPosting>();
    public DbSet<MailAccount> Accounts => Set<MailAccount>();
    public DbSet<MailSettings> Settings => Set<MailSettings>();
    public DbSet<MailTemplate> Templates => Set<MailTemplate>();
    public DbSet<OutgoingMail> Outgoing => Set<OutgoingMail>();
    public DbSet<IncomingMail> Incoming => Set<IncomingMail>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("mail");
        b.Entity<MailAccount>(e => { e.ToTable("accounts"); e.HasKey(x => x.Id); e.Property(x => x.Address).HasMaxLength(200); e.Property(x => x.Sends).HasDefaultValue(true); });
        b.Entity<MailSettings>(e => { e.ToTable("settings"); e.HasKey(x => x.Id); e.Property(x => x.Id).ValueGeneratedNever(); });
        b.Entity<MailTemplate>(e => { e.ToTable("templates"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.Kind, x.Language }).IsUnique(); });
        b.Entity<OutgoingMail>(e =>
        {
            e.ToTable("outgoing");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.JobId, x.Kind });
            e.HasIndex(x => x.MessageId);
            e.HasIndex(x => new { x.AccountId, x.SentAt });
            e.Property(x => x.Status).HasMaxLength(20);
        });
        b.Entity<IncomingMail>(e => { e.ToTable("incoming"); e.HasKey(x => x.Id); e.HasIndex(x => x.MessageId).IsUnique(); });
        b.Entity<AlertPosting>(e => { e.ToTable("alert_postings"); e.HasKey(x => x.Id); e.HasIndex(x => x.Url).IsUnique(); e.HasIndex(x => x.ReceivedAt); });
    }
}
