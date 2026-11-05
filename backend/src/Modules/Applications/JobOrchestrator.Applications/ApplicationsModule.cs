using System.Reflection;
using JobOrchestrator.Applications.Features;
using JobOrchestrator.Applications.Features.Workflow;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace JobOrchestrator.Applications;

/// <summary>Porta de entrada do módulo Applications: candidaturas, fila de etapas, Guardião, revisão e visão geral.</summary>
public static class ApplicationsModule
{
    public static Assembly Assembly => typeof(ApplicationsModule).Assembly;

    public static IServiceCollection AddApplicationsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddModuleDatabase<ApplicationsDbContext>(config, "Applications");
        s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ApplicationsTransaction<,>));
        return s;
    }

    public static IServiceCollection AddApplicationsWorker(this IServiceCollection s, IConfiguration config)
    {
        s.AddEventConsumer<JobShortlistedConsumer>();
        s.AddHostedService<OutboxPublisher<ApplicationsDbContext>>();
        s.AddSingleton(new WorkQueue(NpgsqlDataSource.Create(config.ConnectionFor("Applications"))));
        s.TryAddSingleton<GuardKey>();
        s.AddSingleton<TokenIssuer>();
        s.AddScoped<ProfileSource>();
        s.AddScoped<StepKit>();
        s.AddScoped<IStep, MaterialsStep>();
        s.AddScoped<IStep, PrepareStep>();
        s.AddScoped<IStep, SubmitStep>();
        s.AddScoped<IStep, ReconcileStep>();
        s.AddHostedService<WorkRunner>();
        return s;
    }

    /// <summary>Migra e garante que o histórico é append-only no próprio banco (invariante 9).</summary>
    public static async Task MigrateApplicationsAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationsDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            create or replace function applications.forbid_change() returns trigger language plpgsql as $$
            begin raise exception 'append-only: %', tg_table_name; end $$;
            drop trigger if exists events_append_only on applications.events;
            create trigger events_append_only before update or delete on applications.events
              for each row execute function applications.forbid_change();
            """);
    }
}
