using System.Reflection;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence;

/// <summary>Porta de entrada do módulo Intelligence: provedores de IA pelas assinaturas, roteador, registro de chamadas, teto e cache.</summary>
public static class IntelligenceModule
{
    public static Assembly Assembly => typeof(IntelligenceModule).Assembly;

    public static IServiceCollection AddIntelligenceModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddModuleDatabase<IntelligenceDbContext>(config, "Intelligence");
        // O tempo de cada pedido é controlado no provedor; o modelo forte com parte na CPU passa de minutos.
        s.AddHttpClient("ollama", c => c.Timeout = TimeSpan.FromMinutes(15));
        // Caminho fixo na home: o login da conta Pro vale para o ambiente de desenvolvimento e para o uso real.
        var pro = config["Llm:Claude:ProConfigDir"] is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "job-orchestrator", "accounts", "claude-pro");
        Directory.CreateDirectory(pro);
        s.AddSingleton<IProvider>(sp => new ClaudeCli("claude-pro", "Claude Pro", pro, config));
        s.AddSingleton<IProvider>(sp => new ClaudeCli("claude-max", "Claude Max", null, config));
        s.AddSingleton<IProvider, CodexCli>();
        s.AddSingleton<OllamaLocal>();
        s.AddSingleton<IProvider>(sp => sp.GetRequiredService<OllamaLocal>());
        s.AddScoped<Features.IntelligenceApi>();
        s.AddScoped<IIntelligenceApi>(sp => sp.GetRequiredService<Features.IntelligenceApi>());
        return s;
    }

    public static async Task MigrateIntelligenceAsync(this IServiceProvider sp)
    {
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelligenceDbContext>();
        await db.Database.MigrateAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync(Features.LiveStream.TriggerSql);
        await tx.CommitAsync();
    }
}
