using System.Reflection;

namespace JobOrchestrator.Campaigns;

/// <summary>Porta de entrada do módulo Campaigns: critérios, limites e rodadas de busca.</summary>
public static class CampaignsModule
{
    public static Assembly Assembly => typeof(CampaignsModule).Assembly;

    public static IServiceCollection AddCampaignsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddModuleDatabase<CampaignsDbContext>(config, "Campaigns");
        s.AddTransient(typeof(IPipelineBehavior<,>), typeof(CampaignsTransaction<,>));
        s.AddScoped<ICampaignsApi, Features.CampaignsApi>();
        return s;
    }

    public static IServiceCollection AddCampaignsWorker(this IServiceCollection s) =>
        s.AddHostedService<OutboxPublisher<CampaignsDbContext>>();

    /// <summary>Migra e, na primeira vez, cria a campanha padrão (só preparando, desligada).</summary>
    public static async Task MigrateCampaignsAsync(this IServiceProvider sp, IConfiguration config)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
        await db.Database.MigrateAsync();
        if (await db.Campaigns.AnyAsync()) return;
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var sources = config.GetSection("Seed:Sources").Get<string[]>() ?? [];
        db.Campaigns.Add(Campaign.Create("Backend .NET", ["C#", ".NET", "ASP.NET Core", "AWS", "PostgreSQL"], ["Brasil", "Remoto"], sources, now));
        await db.SaveChangesAsync();
    }
}
