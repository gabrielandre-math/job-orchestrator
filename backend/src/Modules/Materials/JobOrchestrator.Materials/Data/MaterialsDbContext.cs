using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Materials.Data;

/// <summary>Unit of Work do módulo Materials. As consultas leem com Dapper.</summary>
internal sealed class MaterialsDbContext(DbContextOptions<MaterialsDbContext> options) : DbContext(options)
{
    public DbSet<ResumeVersion> Resumes => Set<ResumeVersion>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("materials");
        b.Entity<ResumeVersion>(e =>
        {
            e.ToTable("resume_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.ProfileHash).HasMaxLength(64);
            e.Property(x => x.Language).HasMaxLength(5);
            e.Property(x => x.Mode).HasMaxLength(10);
            e.Property(x => x.WrittenBy).HasMaxLength(120);
            e.Property(x => x.ReviewedBy).HasMaxLength(120);
            e.Property(x => x.PdfPath).HasMaxLength(500);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Provenance).HasColumnType("jsonb");
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
        });
    }
}

/// <summary>Usado só pelo comando dotnet ef, para gerar migrações.</summary>
internal sealed class MaterialsDbFactory : IDesignTimeDbContextFactory<MaterialsDbContext>
{
    public MaterialsDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<MaterialsDbContext>();
        b.UseModuleDatabase("Host=localhost", "materials");
        return new MaterialsDbContext(b.Options);
    }
}
