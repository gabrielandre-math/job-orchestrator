using System.Reflection;

namespace JobOrchestrator.Assistant;

/// <summary>
/// Módulo Assistant: a conversa com os modelos da pessoa (sobe de degrau quando a pergunta pede, compara modelos lado a lado)
/// e o primeiro contato, que lê o currículo e propõe o ambiente inteiro. Não tem banco próprio: a conversa é um fio de agentes.
/// </summary>
public static class AssistantModule
{
    public static Assembly Assembly => typeof(AssistantModule).Assembly;

    public static IServiceCollection AddAssistantModule(this IServiceCollection s)
    {
        s.AddScoped<Features.AssistantService>();
        return s;
    }
}
