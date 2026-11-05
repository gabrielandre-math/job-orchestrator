using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JobOrchestrator.Automation;

/// <summary>Porta de entrada do módulo Automation: fontes, adapters de formulário, roteador e navegador.</summary>
public static class AutomationModule
{
    public static Assembly Assembly => typeof(AutomationModule).Assembly;

    public static IServiceCollection AddAutomationModule(this IServiceCollection s, IConfiguration config)
    {
        s.Configure<AutomationOptions>(config.GetSection("Automation"));
        s.AddDbContext<AutomationDbContext>(o => o.UseModuleDatabase(config.ConnectionFor("Automation"), "automation"));
        s.AddModuleReadDb<AutomationDbContext>(config, "Automation");
        s.AddHttpClient("sources", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(20);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("JobOrchestrator/1.0 (uso pessoal)");
        });
        s.TryAddSingleton<GuardKey>();
        s.AddSingleton<BrowserPool>();
        s.AddScoped<ISourceConnector, GreenhouseLikeSource>();
        s.AddScoped<IFormAdapter, GreenhouseLikeForm>();
        s.AddScoped<Router>();
        s.AddScoped<IAutomationApi, Features.AutomationApi>();
        return s;
    }

    public static async Task MigrateAutomationAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AutomationDbContext>().Database.MigrateAsync();
    }
}
