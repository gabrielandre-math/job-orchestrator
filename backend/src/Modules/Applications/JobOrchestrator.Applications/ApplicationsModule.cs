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
        // O copiloto (API) e o fluxo (Worker) montam o mesmo pacote de candidatura.
        s.TryAddScoped<Bundles>();
        s.AddSingleton<Rehearsal>();
        s.AddScoped<Interviews>();
        return s;
    }

    public static IServiceCollection AddApplicationsWorker(this IServiceCollection s, IConfiguration config)
    {
        s.AddEventConsumer<JobShortlistedConsumer>();
        s.AddEventConsumer<AutoSubmitEndedConsumer>();
        s.AddHostedService<OutboxPublisher<ApplicationsDbContext>>();
        s.AddSingleton(new WorkQueue(NpgsqlDataSource.Create(config.ConnectionFor("Applications"))));
        s.TryAddSingleton<GuardKey>();
        s.AddSingleton<TokenIssuer>();
        s.TryAddScoped<Bundles>();
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
        // API e Worker sobem juntos: o lock serializa o DDL (as instruções rodam numa transação implícita só).
        await db.Database.ExecuteSqlRawAsync("""
            select pg_advisory_xact_lock(4747001);
            create or replace function applications.forbid_change() returns trigger language plpgsql as $$
            begin raise exception 'append-only: %', tg_table_name; end $$;
            create or replace trigger events_append_only before update or delete on applications.events
              for each row execute function applications.forbid_change();
            """);
    }
}
