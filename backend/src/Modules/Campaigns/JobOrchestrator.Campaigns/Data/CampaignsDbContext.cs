using Microsoft.EntityFrameworkCore.Design;

namespace JobOrchestrator.Campaigns.Data;

/// <summary>Unit of Work do módulo Campaigns (EF Core): usado pelos comandos. As consultas leem com Dapper.</summary>
internal sealed partial class CampaignsDbContext(DbContextOptions<CampaignsDbContext> options) : DbContext(options), IOutboxDbContext
{
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("campaigns").MapOutbox().MapInbox();
        MapEntities(b);
    }

    partial void MapEntities(ModelBuilder b);

    /// <summary>Grava o evento na outbox: sai junto com a mudança de estado, ou não sai (Transactional Outbox).</summary>
    public void Publish<T>(string eventType, T evt, DateTime now) => Outbox.Add(OutboxMessage.Create(eventType, evt, now));
}

/// <summary>Usado só pelo comando dotnet ef, para gerar migrações.</summary>
internal sealed class CampaignsDbFactory : IDesignTimeDbContextFactory<CampaignsDbContext>
{
    public CampaignsDbContext CreateDbContext(string[] args)
    {
        var b = new DbContextOptionsBuilder<CampaignsDbContext>();
        b.UseModuleDatabase("Host=localhost", "campaigns");
        return new CampaignsDbContext(b.Options);
    }
}

/// <summary>Marca os comandos deste módulo que rodam em transação.</summary>
internal interface ICampaignsCommand;

/// <summary>Liga a transação ao banco deste módulo.</summary>
internal sealed class CampaignsTransaction<TReq, TRes>(CampaignsDbContext db) : TransactionBehavior<TReq, TRes, CampaignsDbContext>(db)
    where TReq : notnull, ICampaignsCommand where TRes : IResultOf<TRes>;
