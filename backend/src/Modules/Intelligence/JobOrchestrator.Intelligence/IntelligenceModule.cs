using System.Reflection;
using JobOrchestrator.Intelligence.Providers;

namespace JobOrchestrator.Intelligence;

/// <summary>Porta de entrada do módulo Intelligence: provedores de IA pelas assinaturas, roteador e status de cota.</summary>
public static class IntelligenceModule
{
    public static Assembly Assembly => typeof(IntelligenceModule).Assembly;

    public static IServiceCollection AddIntelligenceModule(this IServiceCollection s, IConfiguration config)
    {
        s.AddHttpClient("ollama", c => c.Timeout = TimeSpan.FromSeconds(120));
        // Caminho fixo na home: o login da conta Pro vale para o ambiente de desenvolvimento e para o uso real.
        var pro = config["Llm:Claude:ProConfigDir"] is { Length: > 0 } dir ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "job-orchestrator", "accounts", "claude-pro");
        Directory.CreateDirectory(pro);
        s.AddSingleton<IProvider>(sp => new ClaudeCli("claude-pro", "Claude Pro", pro, config));
        s.AddSingleton<IProvider>(sp => new ClaudeCli("claude-max", "Claude Max", null, config));
        s.AddSingleton<IProvider, CodexCli>();
        s.AddSingleton<IProvider, GeminiCli>();
        s.AddSingleton<IProvider, OllamaLocal>();
        s.AddScoped<IIntelligenceApi, Features.IntelligenceApi>();
        return s;
    }
}
