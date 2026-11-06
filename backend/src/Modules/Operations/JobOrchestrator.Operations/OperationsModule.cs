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
        s.AddSingleton<Features.Hunter>();
        // O índice do Common Crawl e o SPARQL do Wikidata demoram mais que uma API de vaga: cliente próprio, sem o tempo curto por tentativa.
        s.AddHttpClient("hunter", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(100);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("JobOrchestrator/1.0 (personal job search tool; polite, cached)");
        });
        return s;
    }

    /// <summary>Só no Worker: limpeza pela retenção, busca contínua e o Explorador.</summary>
    public static IServiceCollection AddOperationsWorker(this IServiceCollection s) => s.AddHostedService<Features.Retention>().AddHostedService<Features.Continuous>().AddHostedService<Features.NightClock>();
}
