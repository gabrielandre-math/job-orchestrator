namespace JobOrchestrator.Intelligence.Contracts;

// Contratos públicos do módulo Intelligence: IA só local ou pelos CLIs oficiais das assinaturas. API paga não existe aqui.

/// <summary>
/// Degrau de modelo e esforço (docs/integracao-ia.md §10). Local e LocalStrong rodam só na RTX (modelo rápido e modelo forte);
/// Top nunca é a primeira tentativa.
/// </summary>
public enum Rung { Local, LocalStrong, Light, Medium, Strong, Top }

/// <summary>
/// Um pedido de IA: tarefa, instruções fixas, conteúdo (dado, não instrução) e o schema da resposta.
/// LocalFallback: com a nuvem fora, o modelo local forte faz um rascunho marcado.
/// </summary>
public sealed record LlmRequest(string Task, string System, string Prompt, string JsonSchema, Rung Rung, string? AvoidFamily = null, int TimeoutSeconds = 180, bool LocalFallback = false,
    AgentThread? Thread = null, string Agent = "")
{
    public static LlmRequest For(LlmTask task, string system, string prompt, string schema, string? avoidFamily = null, int timeoutSeconds = 180, AgentThread? thread = null, string agent = "") =>
        new(task.Name, system, prompt, schema, task.Rung, avoidFamily, timeoutSeconds, task.LocalFallback, thread, agent);
}

/// <summary>
/// Conversa entre agentes: um fio por trabalho (ler um currículo, compor o currículo de uma vaga). Cada pedido a um modelo,
/// cada resposta e cada nota dos agentes sem IA (conferência, renderização, Guardião) vira uma mensagem do fio.
/// </summary>
public sealed record AgentThread(Guid Id, string Kind, string Subject)
{
    public static AgentThread New(string kind, string subject) => new(Guid.CreateVersion7(), kind, subject);
}

/// <summary>Um fio como a lista mostra.</summary>
public sealed record AgentThreadView(Guid Id, string Kind, string Subject, DateTime StartedAt, DateTime LastAt, int Messages, IReadOnlyList<string> Agents, IReadOnlyList<string> Providers, bool Failed);

/// <summary>Uma mensagem do fio. Kind: ask (pedido ao modelo), answer, note (agente sem IA), fail.</summary>
/// <summary>Data: JSON opcional com números da etapa (fonte, vagas, nota), para as telas ao vivo desenharem sem interpretar texto.</summary>
public sealed record AgentMessageView(Guid Id, DateTime At, string Agent, string Kind, string? ProviderId, string? Model, string? Effort, string Title, string? Body, double Seconds, bool Ok, string? To, string? Data = null);

/// <summary>
/// A resposta, com quem fez, qual modelo, qual esforço e quanto tempo levou.
/// Degraded: o modelo local respondeu no lugar da nuvem. Cached: mesma tarefa e mesma entrada, reaproveitada.
/// </summary>
public sealed record LlmResult(bool Ok, string? Json, string ProviderId, string Model, string Effort, string? Error, double Seconds, bool Degraded = false, bool Cached = false,
    int Tokens = 0, string? ProviderFamily = null)
{
    /// <summary>Família do modelo que respondeu: o revisor precisa ser de outra.</summary>
    public string Family => ProviderFamily ?? ProviderId switch { "ollama" => "local", _ when ProviderId.StartsWith("claude", StringComparison.Ordinal) => "anthropic", _ when ProviderId.StartsWith("codex", StringComparison.Ordinal) => "openai", _ => "" };
}

/// <summary>Uma tarefa do catálogo: degrau padrão, se aceita rascunho local com a nuvem fora e o teto de chamadas na nuvem por dia (0 = só local).</summary>
public sealed record LlmTask(string Name, Rung Rung, bool LocalFallback, int DailyBudget, string Privacy, bool LocalOnly = false);

/// <summary>Catálogo de tarefas (docs/architecture.md §7). Degrau e teto mudam por Llm:Tasks:{nome}:Rung e Llm:Tasks:{nome}:Budget.</summary>
public static class LlmTasks
{
    public static readonly LlmTask JobFit = new("job.fit", Rung.Local, false, 0, "fatos do perfil e vaga, no modelo local", LocalOnly: true);
    public static readonly LlmTask JobExtract = new("job.extract", Rung.Local, false, 0, "pública");
    public static readonly LlmTask QuestionMap = new("question.map", Rung.Local, false, 0, "perfil minimizado");
    public static readonly LlmTask EmailClassify = new("email.classify", Rung.Local, false, 0, "privada, nunca vai para a nuvem");
    public static readonly LlmTask ProfileExtract = new("profile.extract", Rung.Medium, true, 6, "currículo sem dados pessoais");
    public static readonly LlmTask ResumeTailor = new("resume.tailor", Rung.Medium, true, 15, "perfil minimizado + vaga");
    public static readonly LlmTask ResumeFactcheck = new("resume.factcheck", Rung.Light, true, 15, "perfil minimizado");
    public static readonly LlmTask CoverWrite = new("cover.write", Rung.Medium, true, 10, "perfil minimizado + vaga");
    public static readonly LlmTask QuestionDraft = new("question.draft", Rung.Light, true, 30, "perfil minimizado + pergunta");
    public static readonly LlmTask InterviewPrep = new("interview.prep", Rung.LocalStrong, false, 0, "currículo da candidatura e feedback, no modelo local", LocalOnly: true);
    public static readonly LlmTask InterviewPractice = new("interview.practice", Rung.LocalStrong, false, 0, "resposta de treino, no modelo local", LocalOnly: true);
    public static readonly LlmTask PageJobs = new("page.jobs", Rung.Local, false, 0, "página pública de carreira");
    public static readonly LlmTask AssistantTriage = new("assistant.triage", Rung.Local, false, 0, "pergunta, fica na máquina");
    public static readonly LlmTask AssistantChat = new("assistant.chat", Rung.Light, true, 80, "perfil minimizado + pergunta");
    public static readonly LlmTask AssistantSetup = new("assistant.setup", Rung.Medium, true, 8, "currículo sem dados pessoais");
    public static readonly LlmTask ExploreJudge = new("explore.judge", Rung.Local, false, 0, "títulos públicos de vagas, fica na máquina");
    public static readonly LlmTask PageNavigate = new("page.navigate", Rung.Local, false, 0, "rótulos de botões e links de página pública de carreira");
    public static readonly LlmTask ExploreHunt = new("explore.hunt", Rung.Local, false, 0, "palavras da campanha e do currículo, fica na máquina");

    public static IReadOnlyList<LlmTask> All { get; } = [JobExtract, JobFit, QuestionMap, EmailClassify, ProfileExtract, ResumeTailor, ResumeFactcheck, CoverWrite, QuestionDraft, InterviewPrep, InterviewPractice, PageJobs,
        AssistantTriage, AssistantChat, AssistantSetup, ExploreJudge, ExploreHunt];

    public static LlmTask? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}

/// <summary>Janela de uso de uma assinatura. Sem dado, o percentual é nulo (aparece como "sem dado").</summary>
public sealed record UsageWindow(string Name, int? UsedPercent, DateTime? ResetsAt);

/// <summary>Um provedor como a tela mostra.</summary>
public sealed record ProviderView(
    string Id, string Name, string Family, int Order, bool Installed, string? Version, string Login, string? Plan,
    IReadOnlyList<UsageWindow> Usage, bool Available, string Status, string Detail, LlmResult? LastTest);

/// <summary>Um modelo baixado no Ollama: tamanho, se está na VRAM agora e em qual vaga (rápido ou forte) está configurado.</summary>
public sealed record LocalModelView(string Name, double SizeGb, string? Slot, bool Loaded, int? GpuPercent);

/// <summary>Uso de uma tarefa nas últimas 24 h: chamadas na nuvem contra o teto, e quantas foram locais ou do cache.</summary>
public sealed record TaskUsageView(string Task, string Rung, string Privacy, int Cloud, int Budget, int Local, int Cached, int Failed);

/// <summary>Uma chamada registrada.</summary>
public sealed record LlmCallView(DateTime At, string Task, string ProviderId, string Model, string Effort, bool Ok, string? Error, double Seconds, bool Degraded, bool Cached);

/// <summary>Contrato público do módulo Intelligence.</summary>
public interface IIntelligenceApi
{
    Task<IReadOnlyList<ProviderView>> ProvidersAsync(CancellationToken ct);
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
    Task<LlmResult> TestAsync(string providerId, CancellationToken ct);
    Task<IReadOnlyList<LocalModelView>> LocalModelsAsync(CancellationToken ct);
    Task<IReadOnlyList<AgentThreadView>> ThreadsAsync(int limit, CancellationToken ct);
    Task<IReadOnlyList<AgentMessageView>> ThreadAsync(Guid id, CancellationToken ct);

    /// <summary>Nota de um agente sem IA no fio: o que conferiu, o que passou adiante e para quem.</summary>
    Task NoteAsync(AgentThread thread, string agent, string title, string? body, string? to = null, bool ok = true, CancellationToken ct = default);
    /// <summary>Nota com dados estruturados (serializados como JSON) para as telas ao vivo.</summary>
    Task NoteAsync(AgentThread thread, string agent, string title, string? body, object data, string? to = null, bool ok = true, CancellationToken ct = default);
}
