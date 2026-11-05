using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Automation.Data;

/// <summary>Placar de um adapter num domínio e numa capacidade: base da nota do roteador e do circuit breaker.</summary>
internal sealed class AdapterStat
{
    public string AdapterId { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Capability { get; set; } = "";
    public string Version { get; set; } = "";
    public int Attempts { get; set; }
    public int Confirmed { get; set; }
    public int Failures { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? CircuitOpenUntil { get; set; }
    public DateTime? LastFailureAt { get; set; }
    public string? LastFailureCode { get; set; }
}

/// <summary>Registro de uma decisão do roteador: candidatos, notas, resultado e motivo.</summary>
internal sealed class RoutingRecord
{
    public Guid Id { get; set; }
    public Guid? ApplicationId { get; set; }
    public string Capability { get; set; } = "";
    public string? ChosenAdapter { get; set; }
    public string Outcome { get; set; } = "";
    public string Route { get; set; } = "[]";
    public DateTime At { get; set; }
}

/// <summary>Banco do módulo Automation: placar dos adapters e histórico de roteamento.</summary>
internal sealed class AutomationDbContext(DbContextOptions<AutomationDbContext> options) : DbContext(options)
{
    public DbSet<AdapterStat> Stats => Set<AdapterStat>();
    public DbSet<RoutingRecord> Routes => Set<RoutingRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("automation");
        b.Entity<AdapterStat>(e =>
        {
            e.ToTable("adapter_stats");
            e.HasKey(x => new { x.AdapterId, x.Domain, x.Capability });
            e.Property(x => x.AdapterId).HasMaxLength(80);
            e.Property(x => x.Domain).HasMaxLength(200);
            e.Property(x => x.Capability).HasMaxLength(40);
            e.Property(x => x.Version).HasMaxLength(20);
            e.Property(x => x.LastFailureCode).HasMaxLength(60);
        });
        b.Entity<RoutingRecord>(e =>
        {
            e.ToTable("routing_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Capability).HasMaxLength(40);
            e.Property(x => x.ChosenAdapter).HasMaxLength(80);
            e.Property(x => x.Outcome).HasMaxLength(40);
            e.Property(x => x.Route).HasColumnType("jsonb");
            e.HasIndex(x => x.ApplicationId);
        });
    }
}

internal sealed class AutomationDbFactory : IDesignTimeDbContextFactory<AutomationDbContext>
{
    public AutomationDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<AutomationDbContext>();
        b.UseModuleDatabase("Host=localhost", "automation");
        return new AutomationDbContext(b.Options);
    }
}
