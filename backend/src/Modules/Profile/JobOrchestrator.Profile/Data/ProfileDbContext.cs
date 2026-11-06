using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Profile.Data;

/// <summary>Unit of Work do módulo Profile (EF Core): usado pelos comandos e pelo Extrator. As consultas leem com Dapper.</summary>
internal sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<CandidateProfile> Profiles => Set<CandidateProfile>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("profile").MapOutbox().MapInbox();
        b.Entity<CandidateProfile>(e =>
        {
            e.ToTable("profiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Hash).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.FullName).HasMaxLength(160);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Location).HasMaxLength(160);
            e.Property(x => x.LinkedIn).HasMaxLength(300);
            e.Property(x => x.GitHub).HasMaxLength(300);
            e.Property(x => x.Website).HasMaxLength(300);
            e.Property(x => x.ConfirmationsJson).HasColumnName("confirmations").HasColumnType("jsonb");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.Identity);
            e.Ignore(x => x.Confirmations);
            e.Ignore(x => x.Pending);
            e.HasMany(x => x.Facts).WithOne().HasForeignKey(f => f.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Documents).WithOne().HasForeignKey(d => d.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Facts).HasField("_facts").UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Documents).HasField("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        b.Entity<VerifiedFact>(e =>
        {
            e.ToTable("facts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(30);
            e.Property(x => x.Statement).HasMaxLength(600);
            e.Property(x => x.Organization).HasMaxLength(200);
            e.Property(x => x.Role).HasMaxLength(200);
            e.Property(x => x.Start).HasMaxLength(10);
            e.Property(x => x.End).HasMaxLength(10);
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.Quote).HasMaxLength(600);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.AlsoIn).HasDefaultValueSql("'{}'::uuid[]");
            e.Ignore(x => x.Usable);
            e.HasIndex(x => new { x.ProfileId, x.Kind, x.Position });
        });
        b.Entity<SourceDocument>(e =>
        {
            e.ToTable("documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StoredPath).HasMaxLength(500);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(300);
            e.Property(x => x.ExtractedBy).HasMaxLength(120);
            e.Ignore(x => x.PageTexts);
            e.HasIndex(x => new { x.ProfileId, x.Sha256 }).IsUnique();
        });
    }

    /// <summary>Grava o evento na outbox: sai junto com a mudança de estado, ou não sai (Transactional Outbox).</summary>
    public void Publish<T>(string eventType, T evt, DateTime now) => Outbox.Add(OutboxMessage.Create(eventType, evt, now));

    /// <summary>O perfil único, com fatos e documentos.</summary>
    public Task<CandidateProfile?> CurrentAsync(CancellationToken ct) =>
        Profiles.Include(p => p.Facts).Include(p => p.Documents).OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(ct);
}

/// <summary>Usado só pelo comando dotnet ef, para gerar migrações.</summary>
internal sealed class ProfileDbFactory : IDesignTimeDbContextFactory<ProfileDbContext>
{
    public ProfileDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<ProfileDbContext>();
        b.UseModuleDatabase("Host=localhost", "profile");
        return new ProfileDbContext(b.Options);
    }
}

/// <summary>Marca os comandos deste módulo que rodam em transação.</summary>
internal interface IProfileCommand;

/// <summary>Liga a transação ao banco deste módulo.</summary>
internal sealed class ProfileTransaction<TReq, TRes>(ProfileDbContext db) : TransactionBehavior<TReq, TRes, ProfileDbContext>(db)
    where TReq : notnull, IProfileCommand where TRes : IResultOf<TRes>;

/// <summary>Evento interno: um documento chegou e precisa ser lido. O Worker faz a extração.</summary>
internal sealed record DocumentImportedV1(Guid ProfileId, Guid DocumentId)
{
    public const string EventType = "profile.document-imported.v1";
}
