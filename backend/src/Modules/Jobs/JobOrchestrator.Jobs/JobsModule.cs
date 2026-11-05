using System.Reflection;

namespace JobOrchestrator.Jobs;

/// <summary>Porta de entrada do módulo Jobs: descoberta, identidade canônica, elegibilidade e nota.</summary>
public static class JobsModule
{
    public static Assembly Assembly => typeof(JobsModule).Assembly;

    public static IServiceCollection AddJobsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddModuleDatabase<JobsDbContext>(config, "Jobs");
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
        await scope.ServiceProvider.GetRequiredService<JobsDbContext>().Database.MigrateAsync();
    }
}
