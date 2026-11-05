using Microsoft.Extensions.Http.Resilience;
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
        s.AddTransient<ResilienceErrors>();
        s.AddHttpClient("sources", c =>
        {
            // O tempo de cada tentativa fica com a resiliência; o HttpClient guarda só o teto total.
            c.Timeout = TimeSpan.FromSeconds(90);
            c.DefaultRequestHeaders.UserAgent.ParseAdd("JobOrchestrator/1.0 (uso pessoal)");
        })
        .AddHttpMessageHandler<ResilienceErrors>()
        // Fonte instável não derruba a rodada nem as outras: nova tentativa com espera (o Retry-After do 429 manda), tempo por tentativa
        // e circuito aberto por host quando o site insiste em falhar. Só GET se repete; 404 não é falha passageira e não se repete.
        .AddStandardResilienceHandler(o =>
        {
            o.Retry.MaxRetryAttempts = 2;
            o.Retry.Delay = TimeSpan.FromSeconds(2);
            o.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.Retry.DisableForUnsafeHttpMethods();
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(20);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(75);
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(60);
            o.CircuitBreaker.MinimumThroughput = 4;
            o.CircuitBreaker.BreakDuration = TimeSpan.FromMinutes(2);
        })
        .SelectPipelineByAuthority();
        s.TryAddSingleton<GuardKey>();
        s.AddSingleton<BrowserPool>();
        s.AddScoped<ISourceConnector, GreenhouseLikeSource>();
        s.AddScoped<ISourceConnector, LeverSource>();
        s.AddScoped<ISourceConnector, AshbySource>();
        s.AddScoped<ISourceConnector, SmartRecruitersSource>();
        s.AddScoped<ISourceConnector, GitHubIssuesSource>();
        s.AddScoped<ISourceConnector, HackerNewsSource>();
        s.AddScoped<ISourceConnector, RemotiveSource>();
        s.AddScoped<ISourceConnector, ArbeitnowSource>();
        s.AddScoped<ISourceConnector, HimalayasSource>();
        s.AddScoped<ISourceConnector, JobicySource>();
        s.AddScoped<ISourceConnector, TheMuseSource>();
        s.AddScoped<ISourceConnector, RemoteOkSource>();
        s.AddScoped<ISourceConnector, WeWorkRemotelySource>();
        s.AddScoped<ISourceConnector, RecruiteeSource>();
        s.AddScoped<ISourceConnector, BreezySource>();
        s.AddScoped<ISourceConnector, GupySource>();
        s.AddScoped<ISourceConnector, GetOnBoardSource>();
        s.AddScoped<ISourceConnector, PersonioSource>();
        s.AddScoped<ISourceConnector, WorkableSource>();
        s.AddScoped<ISourceConnector, TeamtailorSource>();
        s.AddSingleton<ScraperEnv>();
        s.AddScoped<ScrapyRunner>();
        s.AddScoped<Leads>();
        s.AddScoped<PageReader>();
        s.AddScoped<ISourceConnector, WebSource>();
        s.AddScoped<Features.ScraperApi>();
        s.AddScoped<CareerPageReader>();
        s.AddScoped<IFormAdapter, GreenhouseLikeForm>();
        s.AddScoped<IFormAdapter, LeverForm>();
        s.AddScoped<IFormAdapter, AshbyForm>();
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
