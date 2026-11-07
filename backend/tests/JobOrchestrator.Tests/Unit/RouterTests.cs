using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Intelligence.Contracts;
using JobOrchestrator.Intelligence.Data;
using JobOrchestrator.Intelligence.Features;
using JobOrchestrator.Intelligence.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace JobOrchestrator.Tests.Unit;

/// <summary>
/// Roteador com provedores falsos e um banco novo no Postgres de teste: ordem de gasto, troca por cota, família evitada,
/// rascunho local marcado, teto diário, cache e IA desligada. Nenhum CLI de verdade roda aqui.
/// </summary>
[Collection("router")]
public sealed class RouterTests : IAsyncLifetime
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";
    private readonly string _db = $"jo_router_{Guid.NewGuid():N}"[..22];
    private string Connection => $"Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database={_db}";

    private sealed class Fake(string id, string family, bool available = true, Func<LlmRequest, LlmResult>? run = null) : IProvider
    {
        public List<LlmRequest> Calls { get; } = [];
        public string Id => id;
        public string Name => id;
        public string Family => family;
        public Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct) =>
            Task.FromResult(new ProviderView(id, id, family, order, true, "1", "assinatura", null, [], available, available ? "ready" : "no-login", "", lastTest));
        public Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
        {
            Calls.Add(r);
            return Task.FromResult(run?.Invoke(r) ?? new LlmResult(true, "{\"ok\":true}", id, $"{id}-model", "low", null, 0.1));
        }
    }

    public async Task InitializeAsync()
    {
        await using var c = new NpgsqlConnection(Admin);
        await c.OpenAsync();
        await new NpgsqlCommand($"create database {_db}", c).ExecuteNonQueryAsync();
        await using var db = Db();
        await db.Database.MigrateAsync();
        IntelligenceApi.Reset();
    }

    public async Task DisposeAsync()
    {
        IntelligenceApi.Reset();
        NpgsqlConnection.ClearAllPools();
        await using var c = new NpgsqlConnection(Admin);
        await c.OpenAsync();
        await new NpgsqlCommand($"drop database if exists {_db} with (force)", c).ExecuteNonQueryAsync();
    }

    private IntelligenceDbContext Db()
    {
        var b = new DbContextOptionsBuilder<IntelligenceDbContext>();
        b.UseModuleDatabase(Connection, "intelligence");
        return new IntelligenceDbContext(b.Options);
    }

    private IntelligenceApi Router(IntelligenceDbContext db, IEnumerable<IProvider> providers, Dictionary<string, string?>? extra = null)
    {
        var settings = new Dictionary<string, string?> { ["Llm:Order:0"] = "claude-pro", ["Llm:Order:1"] = "codex", ["Llm:Order:2"] = "claude-max" };
        foreach (var (k, v) in extra ?? []) settings[k] = v;
        IntelligenceApi.Reset();
        return new IntelligenceApi(new ProviderSet([.. providers], []), db, new ConfigurationBuilder().AddInMemoryCollection(settings).Build(), TimeProvider.System, NullLogger<IntelligenceApi>.Instance);
    }

    private static LlmRequest Tailor(string prompt = "fatos", string? avoid = null) =>
        LlmRequest.For(LlmTasks.ResumeTailor, "sistema", prompt, """{"type":"object"}""", avoid);

    [Fact]
    public async Task Claude_sem_login_cai_para_o_codex()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic", available: false);
        var codex = new Fake("codex", "openai");
        var r = await Router(db, [pro, codex, new Fake("claude-max", "anthropic"), new Fake("ollama", "local")]).CompleteAsync(Tailor(), default);
        Assert.Equal("codex", r.ProviderId);
        Assert.Empty(pro.Calls);
    }

    [Fact]
    public async Task Cota_esgotada_pausa_a_conta_e_tenta_a_proxima()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic", run: _ => new LlmResult(false, null, "claude-pro", "sonnet", "medium", "quota", 1));
        var codex = new Fake("codex", "openai");
        var api = Router(db, [pro, codex, new Fake("ollama", "local")]);
        Assert.Equal("codex", (await api.CompleteAsync(Tailor("um"), default)).ProviderId);
        Assert.Equal("codex", (await api.CompleteAsync(Tailor("dois"), default)).ProviderId);
        Assert.Single(pro.Calls);
    }

    [Fact]
    public async Task Revisor_nunca_e_da_familia_do_redator()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic");
        var r = await Router(db, [pro, new Fake("codex", "openai"), new Fake("ollama", "local")])
            .CompleteAsync(LlmRequest.For(LlmTasks.ResumeFactcheck, "s", "p", "{}", avoidFamily: "anthropic"), default);
        Assert.Equal("codex", r.ProviderId);
        Assert.Empty(pro.Calls);
    }

    [Fact]
    public async Task Nuvem_fora_vira_rascunho_local_marcado()
    {
        await using var db = Db();
        var local = new Fake("ollama", "local");
        var r = await Router(db, [new Fake("claude-pro", "anthropic", false), new Fake("codex", "openai", false), new Fake("claude-max", "anthropic", false), local])
            .CompleteAsync(Tailor(), default);
        Assert.True(r.Ok);
        Assert.True(r.Degraded);
        Assert.Equal(Rung.LocalStrong, local.Calls.Single().Rung);
    }

    [Fact]
    public async Task Tarefa_sem_rascunho_local_falha_sem_inventar()
    {
        await using var db = Db();
        var local = new Fake("ollama", "local");
        var r = await Router(db, [new Fake("claude-pro", "anthropic", false), new Fake("codex", "openai", false), local])
            .CompleteAsync(new LlmRequest("test.cloud-only", "s", "p", "{}", Rung.Medium), default);
        Assert.False(r.Ok);
        Assert.Empty(local.Calls);
    }

    [Fact]
    public async Task Tarefa_privada_permanece_local_mesmo_com_degrau_da_nuvem_configurado()
    {
        await using var db = Db();
        var cloud = new Fake("claude-pro", "anthropic");
        var local = new Fake("ollama", "local");
        var api = Router(db, [cloud, local], new()
        {
            ["Llm:Tasks:interview.prep:Rung"] = "Top", ["Llm:Tasks:interview.prep:Budget"] = "10"
        });
        var result = await api.CompleteAsync(LlmRequest.For(LlmTasks.InterviewPrep, "s", "p", "{}"), default);
        Assert.True(result.Ok);
        Assert.Empty(cloud.Calls);
        Assert.Equal(Rung.LocalStrong, Assert.Single(local.Calls).Rung);
    }

    [Fact]
    public async Task Classificar_email_nunca_vai_para_a_nuvem_mesmo_com_degrau_e_orcamento()
    {
        await using var db = Db();
        var cloud = new Fake("claude-pro", "anthropic");
        var local = new Fake("ollama", "local");
        var api = Router(db, [cloud, local], new() { ["Llm:Tasks:email.classify:Rung"] = "Top", ["Llm:Tasks:email.classify:Budget"] = "10" });
        var result = await api.CompleteAsync(LlmRequest.For(LlmTasks.EmailClassify, "s", "p", "{}"), default);
        Assert.True(result.Ok);
        Assert.Empty(cloud.Calls);
    }

    [Fact]
    public async Task Teto_diario_corta_a_nuvem()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic");
        var local = new Fake("ollama", "local");
        var api = Router(db, [pro, local], new() { ["Llm:Tasks:resume.tailor:Budget"] = "2" });
        await api.CompleteAsync(Tailor("a"), default);
        await api.CompleteAsync(Tailor("b"), default);
        var third = await api.CompleteAsync(Tailor("c"), default);
        Assert.Equal(2, pro.Calls.Count);
        Assert.True(third.Degraded);
    }

    [Fact]
    public async Task Mesmo_pedido_sai_do_cache_sem_gastar_cota()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic");
        var api = Router(db, [pro, new Fake("ollama", "local")]);
        await api.CompleteAsync(Tailor("igual"), default);
        var again = await api.CompleteAsync(Tailor("igual"), default);
        Assert.True(again.Cached);
        Assert.Single(pro.Calls);
    }

    [Fact]
    public async Task IA_desligada_nao_chama_ninguem()
    {
        await using var db = Db();
        var pro = new Fake("claude-pro", "anthropic");
        var r = await Router(db, [pro], new() { ["Llm:Disabled"] = "true" }).CompleteAsync(Tailor(), default);
        Assert.False(r.Ok);
        Assert.Empty(pro.Calls);
    }

    [Fact]
    public async Task Cada_chamada_fica_registrada_e_a_conversa_tambem()
    {
        await using var db = Db();
        var thread = AgentThread.New("resume", "teste");
        await Router(db, [new Fake("claude-pro", "anthropic"), new Fake("ollama", "local")])
            .CompleteAsync(LlmRequest.For(LlmTasks.ResumeTailor, "s", "p", "{}", thread: thread, agent: "Redator"), default);
        Assert.Equal(1, await db.Calls.CountAsync(c => c.Task == "resume.tailor"));
        var kinds = await db.Messages.Where(m => m.ThreadId == thread.Id).OrderBy(m => m.At).Select(m => m.Kind).ToListAsync();
        Assert.Equal(["ask", "working", "answer"], kinds);
    }
}
