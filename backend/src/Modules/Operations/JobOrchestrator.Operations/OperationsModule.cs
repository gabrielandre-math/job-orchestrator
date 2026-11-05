using System.Reflection;

namespace JobOrchestrator.Operations;

/// <summary>
/// Operação da casa: a busca da noite (rodadas pedidas pelo timer), o resumo da noite em Markdown e JSON, e configurações e segurança
/// (modo privado, retenção, exportar, apagar tudo, revogar). Lê o banco inteiro só para relatório; escreve só nos atalhos de segurança.
/// </summary>
public static class OperationsModule
{
    public static Assembly Assembly => typeof(OperationsModule).Assembly;

    public static IServiceCollection AddOperationsModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddKeyedSingleton("operations", (_, _) => NpgsqlDataSource.Create(config.ConnectionFor("Operations")));
        s.AddSingleton<Features.NightReports>();
        s.AddSingleton<Features.Explorer>();
        return s;
    }

    /// <summary>Só no Worker: limpeza pela retenção, busca contínua e o Explorador.</summary>
    public static IServiceCollection AddOperationsWorker(this IServiceCollection s) => s.AddHostedService<Features.Retention>().AddHostedService<Features.Continuous>();
}
