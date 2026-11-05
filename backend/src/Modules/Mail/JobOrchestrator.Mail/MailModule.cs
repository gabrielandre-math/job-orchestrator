using System.Reflection;

namespace JobOrchestrator.Mail;

/// <summary>Porta de entrada do módulo Mail: conta por senha de app, modelo com IA, Guardião de envio, modo automático com consentimento e leitura da caixa.</summary>
public static class MailModule
{
    public static Assembly Assembly => typeof(MailModule).Assembly;

    public static IServiceCollection AddMailModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddDbContext<MailDbContext>(o => o.UseModuleDatabase(config.ConnectionFor("Mail"), "mail"));
        s.AddScoped<Features.MailService>();
        s.AddScoped<JobOrchestrator.Automation.Contracts.IJobAlertFeed, Features.MailAlertFeed>();
        return s;
    }

    /// <summary>Só no Worker: o ciclo do automático, dos lembretes e da caixa.</summary>
    public static IServiceCollection AddMailWorker(this IServiceCollection s) => s.AddHostedService<Features.MailWorker>();

    public static async Task MigrateMailAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MailDbContext>().Database.MigrateAsync();
    }
}
