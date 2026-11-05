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
        s.AddHttpClient("api", c => c.Timeout = TimeSpan.FromMinutes(10));
        s.AddSingleton<OllamaLocal>();
        s.AddSingleton<ProviderCatalog>();
        s.AddScoped(sp => sp.GetRequiredService<ProviderCatalog>().Current(sp.GetRequiredService<IntelligenceDbContext>()));
        s.AddScoped<Features.IntelligenceApi>();
        s.AddScoped<Features.AccountsApi>();
        s.AddSingleton<Features.LocalSetup>();
        s.AddSingleton<Features.Sensors>();
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
        // Primeira vez: as contas que já existiam (Claude Pro na pasta própria, ChatGPT e Claude Max nas pastas padrão dos CLIs).
        if (!await db.Accounts.AnyAsync())
        {
            var now = DateTime.UtcNow;
            var pro = Path.Combine(ProviderCatalog.AccountsRoot, "claude-pro");
            Directory.CreateDirectory(pro);
            db.Accounts.AddRange(
                new Account { Id = "claude-pro", Kind = "claude-cli", Name = "Claude Pro", Position = 1, ConfigDir = pro, CreatedAt = now },
                new Account { Id = "codex", Kind = "codex-cli", Name = "ChatGPT (Codex)", Position = 2, CreatedAt = now },
                new Account { Id = "claude-max", Kind = "claude-cli", Name = "Claude Max", Position = 3, CreatedAt = now });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }
}
