using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Intelligence.Data;

/// <summary>Cada chamada a um modelo, com quem respondeu e quanto levou. Serve de registro, de teto diário e de prova de que nada foi pago.</summary>
internal sealed class LlmCall
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTime At { get; init; }
    public string Task { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string Model { get; init; } = "";
    public string Effort { get; init; } = "";
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public double Seconds { get; init; }
    public bool Degraded { get; init; }
    public bool Cached { get; init; }
}

/// <summary>Resposta guardada por 30 dias: mesma tarefa, mesmas instruções e mesma entrada não gastam cota de novo.</summary>
internal sealed class CachedAnswer
{
    public string Key { get; init; } = "";
    public string Task { get; init; } = "";
    public string Json { get; init; } = "";
    public string ProviderId { get; init; } = "";
    public string Model { get; init; } = "";
    public string Effort { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
}

/// <summary>Mensagem de uma conversa entre agentes (ver AgentThread). Corpo limitado a 20 mil caracteres.</summary>
internal sealed class AgentMessage
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid ThreadId { get; init; }
    public string ThreadKind { get; init; } = "";
    public string Subject { get; init; } = "";
    public DateTime At { get; init; }
    public string Agent { get; init; } = "";
    public string Kind { get; init; } = "";
    public string? ProviderId { get; init; }
    public string? Model { get; init; }
    public string? Effort { get; init; }
    public string Title { get; init; } = "";
    public string? Body { get; init; }
    public string? To { get; init; }
    public double Seconds { get; init; }
    public bool Ok { get; init; }
}

internal sealed class IntelligenceDbContext(DbContextOptions<IntelligenceDbContext> options) : DbContext(options)
{
    public DbSet<LlmCall> Calls => Set<LlmCall>();
    public DbSet<CachedAnswer> Cache => Set<CachedAnswer>();
    public DbSet<AgentMessage> Messages => Set<AgentMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("intelligence");
        b.Entity<LlmCall>(e =>
        {
            e.ToTable("calls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Task).HasMaxLength(60);
            e.Property(x => x.ProviderId).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Effort).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(300);
            e.HasIndex(x => new { x.Task, x.At });
        });
        b.Entity<CachedAnswer>(e =>
        {
            e.ToTable("cache");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
            e.Property(x => x.Task).HasMaxLength(60);
            e.Property(x => x.ProviderId).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Effort).HasMaxLength(20);
            e.HasIndex(x => x.ExpiresAt);
        });
        b.Entity<AgentMessage>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.ThreadKind).HasMaxLength(40);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Agent).HasMaxLength(60);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.ProviderId).HasMaxLength(40);
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Effort).HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.To).HasMaxLength(60);
            e.HasIndex(x => new { x.ThreadId, x.At });
            e.HasIndex(x => x.At);
        });
    }
}

/// <summary>Usado só pelo comando dotnet ef, para gerar migrações.</summary>
internal sealed class IntelligenceDbFactory : IDesignTimeDbContextFactory<IntelligenceDbContext>
{
    public IntelligenceDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<IntelligenceDbContext>();
        b.UseModuleDatabase("Host=localhost", "intelligence");
        return new IntelligenceDbContext(b.Options);
    }
}
