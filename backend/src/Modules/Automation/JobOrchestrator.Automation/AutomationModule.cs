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
        s.AddSingleton<Features.ControlledTests>();
        s.AddSingleton<Features.LinkedInSession>();
        s.AddSingleton<ILocalBrowserSessions, Features.LocalBrowserSessions>();
        s.AddHttpClient("linkedin-session", c => c.Timeout = TimeSpan.FromMinutes(3));
        s.AddScoped<ISourceConnector, Features.LinkedInSessionSource>();
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
        s.AddScoped<ISourceConnector, WorkableSearchSource>();
        s.AddScoped<ISourceConnector, FreehireSource>();
        s.AddScoped<ISourceConnector, JobTechSource>();
        s.AddScoped<ISourceConnector, WeWorkRemotelySource>();
        s.AddScoped<ISourceConnector, RecruiteeSource>();
        s.AddScoped<ISourceConnector, BreezySource>();
        s.AddScoped<ISourceConnector, GupySource>();
        s.AddScoped<ISourceConnector, NavSource>();
        s.AddScoped<ISourceConnector, SeasonalJobsSource>();
        s.AddScoped<ISourceConnector, UsaJobsSource>();
        s.AddScoped<ISourceConnector, ReliefWebSource>();
        s.AddScoped<ISourceConnector, RipplingSource>();
        s.AddScoped<ISourceConnector, JobLinksSource>();
        s.AddScoped<ISourceConnector, MpsvSource>();
        s.AddScoped<ISourceConnector, NvaSource>();
        s.AddScoped<ISourceConnector, PinpointSource>();
        s.AddScoped<ISourceConnector, BambooHrSource>();
        s.AddScoped<ISourceConnector, RecruiterboxSource>();
        s.AddScoped<ISourceConnector, GetOnBoardSource>();
        s.AddScoped<ISourceConnector, PersonioSource>();
        s.AddScoped<ISourceConnector, WorkableSource>();
        s.AddScoped<ISourceConnector, TeamtailorSource>();
        s.AddScoped<ISourceConnector, SolidesSource>();
        s.AddScoped<ISourceConnector, InhireSource>();
        s.AddScoped<ISourceConnector, QuickinSource>();
        s.AddScoped<ISourceConnector, PandapeSource>();
        s.AddScoped<ISourceConnector, WorkdaySource>();
        s.AddScoped<ISourceConnector, OracleSource>();
        s.AddScoped<ISourceConnector, SuccessFactorsSource>();
        s.AddScoped<ISourceConnector, RssSource>();
        s.AddScoped<ISourceConnector, FourDayWeekSource>();
        s.AddScoped<ISourceConnector, WorkingNomadsSource>();
        s.AddScoped<ISourceConnector, AdzunaSource>();
        s.AddScoped<ISourceConnector, JoobleSource>();
        s.AddScoped<ISourceConnector, EmailAlertsSource>();
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
        s.AddScoped<IFormAdapter, WorkableForm>();
        s.AddScoped<IFormAdapter, SmartRecruitersForm>();
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
