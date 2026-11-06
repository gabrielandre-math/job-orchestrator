using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Applications.Data;

/// <summary>Unit of Work do módulo Applications (EF Core). As consultas leem com Dapper.</summary>
internal sealed class ApplicationsDbContext(DbContextOptions<ApplicationsDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<ApplicationEvent> Events => Set<ApplicationEvent>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();
    public DbSet<SubmissionIntent> Intents => Set<SubmissionIntent>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<HumanTask> Tasks => Set<HumanTask>();
    public DbSet<InterviewRound> Interviews => Set<InterviewRound>();
    public DbSet<ApprovalDecision> Approvals => Set<ApprovalDecision>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("applications").MapOutbox().MapInbox();
        b.Entity<JobApplication>(e =>
        {
            e.ToTable("applications");
            e.HasKey(x => x.Id);
            e.Property(x => x.CanonicalKey).HasMaxLength(200);
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.ApplyUrl).HasMaxLength(1000);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Outcome).HasMaxLength(20);
            e.Property(x => x.ApplyChannel).HasMaxLength(10);
            e.Property(x => x.ResumePath).HasMaxLength(500);
            e.Property(x => x.ResumeHash).HasMaxLength(64);
            e.Property(x => x.PreparedBy).HasMaxLength(80);
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.PreparedFields).HasColumnType("jsonb");
            e.Property(x => x.ReceiptId).HasMaxLength(80);
            e.Property(x => x.LastReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            // Invariante 1: uma candidatura por vaga canônica e campanha.
            e.HasIndex(x => new { x.CampaignId, x.CanonicalKey }).IsUnique();
            e.HasIndex(x => x.State);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.ApplicationId);
            e.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<ApplicationEvent>(e =>
        {
            e.ToTable("events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.From).HasMaxLength(30);
            e.Property(x => x.To).HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Actor).HasMaxLength(40);
            e.Property(x => x.Detail).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.At });
        });
        b.Entity<WorkItem>(e =>
        {
            e.ToTable("work_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Step).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LeaseOwner).HasMaxLength(120);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasIndex(x => new { x.ApplicationId, x.Step }).IsUnique().HasFilter("completed_at IS NULL");
            e.HasIndex(x => x.RunAfter).HasFilter("completed_at IS NULL");
        });
        b.Entity<SubmissionIntent>(e =>
        {
            e.ToTable("submission_intents");
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.AdapterId).HasMaxLength(80);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.ApplicationId).IsUnique().HasFilter("state IN ('Open', 'SentUnverified')");
        });
        b.Entity<Evidence>(e =>
        {
            e.ToTable("evidence");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.HasIndex(x => x.ApplicationId);
        });
        b.Entity<HumanTask>(e =>
        {
            e.ToTable("human_tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.Property(x => x.Resolution).HasMaxLength(200);
            e.HasIndex(x => x.ApplicationId);
        });
        b.Entity<InterviewRound>(e =>
        {
            e.ToTable("interviews");
            e.HasKey(x => x.Id);
            e.Property(x => x.Stage).HasMaxLength(12);
            e.Property(x => x.Pack).HasColumnType("jsonb");
            e.Property(x => x.Feedback).HasMaxLength(3000);
            e.HasOne<JobApplication>().WithMany().HasForeignKey(x => x.ApplicationId);
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
        });
        b.Entity<ApprovalDecision>(e =>
        {
            e.ToTable("approval_decisions");
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.HasIndex(x => x.ApplicationId);
        });
    }

    public void Publish<T>(string eventType, T evt, DateTime now) => Outbox.Add(OutboxMessage.Create(eventType, evt, now));

    public HumanTask AddTask(Guid applicationId, HumanTaskKind kind, string title, string detail, DateTime now)
    {
        var task = new HumanTask { Id = Guid.CreateVersion7(), ApplicationId = applicationId, Kind = kind, Title = title, Detail = detail, CreatedAt = now };
        Tasks.Add(task);
        return task;
    }

    public async Task ResolveTasksAsync(Guid applicationId, string resolution, DateTime now, CancellationToken ct)
    {
        foreach (var t in await Tasks.Where(t => t.ApplicationId == applicationId && t.ResolvedAt == null).ToListAsync(ct))
        {
            t.ResolvedAt = now;
            t.Resolution = resolution;
        }
    }
}

internal sealed class ApplicationsDbFactory : IDesignTimeDbContextFactory<ApplicationsDbContext>
{
    public ApplicationsDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<ApplicationsDbContext>();
        b.UseModuleDatabase("Host=localhost", "applications");
        return new ApplicationsDbContext(b.Options);
    }
}

/// <summary>Marca os comandos deste módulo que rodam em transação.</summary>
internal interface IApplicationsCommand;

internal sealed class ApplicationsTransaction<TReq, TRes>(ApplicationsDbContext db) : TransactionBehavior<TReq, TRes, ApplicationsDbContext>(db)
    where TReq : notnull, IApplicationsCommand where TRes : IResultOf<TRes>;

/// <summary>Leva a mudança de estado para a outbox, na mesma transação.</summary>
internal sealed class ApplicationMovedHandler(ApplicationsDbContext db) : INotificationHandler<ApplicationMoved>
{
    public Task Handle(ApplicationMoved e, CancellationToken ct)
    {
        db.Publish(ApplicationStateChangedV1.EventType, new ApplicationStateChangedV1(e.ApplicationId, e.CampaignId, e.From.ToString(), e.To.ToString(), e.At), e.At);
        return Task.CompletedTask;
    }
}
