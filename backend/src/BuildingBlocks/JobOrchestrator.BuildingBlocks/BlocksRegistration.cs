using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobOrchestrator.BuildingBlocks;

/// <summary>Registra MediatR, validadores e o pipeline.</summary>
public static class BlocksRegistration
{
    /// <summary>Chame antes dos módulos: a ordem de registro é a ordem do pipeline.</summary>
    public static IServiceCollection AddJoBlocks(this IServiceCollection s, params Assembly[] modules)
    {
        s.AddMediatR(c =>
        {
            c.RegisterServicesFromAssemblies(modules);
            c.AddOpenBehavior(typeof(LogBehavior<,>));
            c.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });
        s.AddValidatorsFromAssemblies(modules, includeInternalTypes: true);
        s.TryAddSingleton(TimeProvider.System);
        // The trail and the logger that feeds it: one per process, writing under the data folder.
        s.TryAddSingleton(sp => new Trail(sp.GetRequiredService<TimeProvider>()));
        s.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(sp => new TrailLoggerProvider(sp.GetRequiredService<Trail>()));
        return s;
    }

    public static IServiceCollection AddEventConsumer<T>(this IServiceCollection s) where T : class, IEventConsumer =>
        s.AddScoped<IEventConsumer, T>();
}
