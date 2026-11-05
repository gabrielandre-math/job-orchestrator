using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Jobs.Data;

/// <summary>Unit of Work do módulo Jobs (EF Core). As consultas leem com Dapper.</summary>
internal sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<JobPosting> Postings => Set<JobPosting>();
    public DbSet<JobSnapshot> Snapshots => Set<JobSnapshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("jobs").MapOutbox().MapInbox();
        b.Entity<JobPosting>(e =>
        {
            e.ToTable("postings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(300);
            e.Property(x => x.ExternalId).HasMaxLength(120);
            e.Property(x => x.CanonicalKey).HasMaxLength(200);
            e.Property(x => x.Company).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.ApplyUrl).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Breakdown).HasColumnType("jsonb");
            e.Property(x => x.Reason).HasMaxLength(400);
            e.Property(x => x.Seniority).HasMaxLength(20);
            e.Property(x => x.Modality).HasMaxLength(20);
            e.Property(x => x.SalaryText).HasMaxLength(120);
            e.Property(x => x.SalaryCurrency).HasMaxLength(3);
            e.Property(x => x.Contract).HasMaxLength(20);
            e.Property(x => x.English).HasMaxLength(40);
            e.Property(x => x.Sponsorship).HasMaxLength(40);
            e.Property(x => x.ApplyChannel).HasMaxLength(10);
            e.Property(x => x.ApplyEmail).HasMaxLength(200);
            e.Property(x => x.CompanyDomain).HasMaxLength(120);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.CampaignId, x.Source, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.CampaignId, x.CanonicalKey });
            e.HasIndex(x => new { x.CampaignId, x.Status });
        });
        b.Entity<JobSnapshot>(e =>
        {
            e.ToTable("snapshots");
            e.HasKey(x => x.Id);
            e.Property(x => x.PayloadHash).HasMaxLength(64);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => new { x.JobId, x.FetchedAt });
        });
    }

    public void Publish<T>(string eventType, T evt, DateTime now) => Outbox.Add(OutboxMessage.Create(eventType, evt, now));
}

/// <summary>Marca os comandos deste módulo que rodam em transação.</summary>
internal interface IJobsCommand;

internal sealed class JobsTransaction<TReq, TRes>(JobsDbContext db) : TransactionBehavior<TReq, TRes, JobsDbContext>(db)
    where TReq : notnull, IJobsCommand where TRes : IResultOf<TRes>;

internal sealed class JobsDbFactory : IDesignTimeDbContextFactory<JobsDbContext>
{
    public JobsDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<JobsDbContext>();
        b.UseModuleDatabase("Host=localhost", "jobs");
        return new JobsDbContext(b.Options);
    }
}
