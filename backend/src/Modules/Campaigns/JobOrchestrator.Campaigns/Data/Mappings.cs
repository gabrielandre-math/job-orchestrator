namespace JobOrchestrator.Campaigns.Data;

internal sealed partial class CampaignsDbContext
{
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<CampaignRun> Runs => Set<CampaignRun>();

    partial void MapEntities(ModelBuilder b)
    {
        b.Entity<Campaign>(e =>
        {
            e.ToTable("campaigns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.TimeZone).HasMaxLength(60);
            e.Property(x => x.Window).HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
        });
        b.Entity<CampaignRun>(e =>
        {
            e.ToTable("runs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Trigger).HasMaxLength(30);
            e.HasIndex(x => new { x.CampaignId, x.RequestedAt });
        });
    }
}
