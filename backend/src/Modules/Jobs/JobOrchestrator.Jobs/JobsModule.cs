using System.Reflection;

namespace JobOrchestrator.Jobs;

/// <summary>Porta de entrada do módulo Jobs: descoberta, identidade canônica, elegibilidade e nota.</summary>
public static class JobsModule
{
    public static Assembly Assembly => typeof(JobsModule).Assembly;

    public static IServiceCollection AddJobsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddHttpClient("logos", c => { c.Timeout = TimeSpan.FromSeconds(8); c.DefaultRequestHeaders.UserAgent.ParseAdd("job-orchestrator"); });
        s.AddSingleton<Features.CompanyLogos>();
        s.AddModuleDatabase<JobsDbContext>(config, "Jobs");
        s.AddTransient(typeof(IPipelineBehavior<,>), typeof(JobsTransaction<,>));
        s.AddScoped<IJobsApi, Features.JobsApi>();
        return s;
    }

    public static IServiceCollection AddJobsWorker(this IServiceCollection s)
    {
        s.AddEventConsumer<Features.RunRequestedConsumer>();
        s.AddHostedService<OutboxPublisher<JobsDbContext>>();
        return s;
    }

    public static async Task MigrateJobsAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JobsDbContext>();
        await db.Database.MigrateAsync();
        // Vagas guardadas antes da stack existir ganham stack e domínio da empresa uma vez. API e Worker sobem juntos:
        // o lock faz um esperar o outro, e o segundo não acha mais nada para preencher.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("select pg_advisory_xact_lock(4747003)");
        var pending = await db.Postings.Where(p => p.Stack.Count == 0 && p.CompanyDomain == null && !p.Enriched).Take(5000).ToListAsync();
        foreach (var p in pending) p.Enrich();
        if (pending.Count > 0) await db.SaveChangesAsync();
        await tx.CommitAsync();
    }
}
