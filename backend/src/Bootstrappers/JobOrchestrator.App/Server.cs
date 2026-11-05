using System.Text.Json.Serialization;
using Carter;
using JobOrchestrator.Applications;
using JobOrchestrator.Assistant;
using JobOrchestrator.Automation;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.BuildingBlocks.Web;
using JobOrchestrator.Campaigns;
using JobOrchestrator.Intelligence;
using JobOrchestrator.Jobs;
using JobOrchestrator.Mail;
using JobOrchestrator.Materials;
using JobOrchestrator.Operations;
using JobOrchestrator.Profile;
using Microsoft.Extensions.FileProviders;

namespace JobOrchestrator.App;

/// <summary>
/// API, painel e Worker num processo só: o que o executável roda em segundo plano, preso em 127.0.0.1:7410. Os mesmos módulos e o mesmo
/// banco do modo de desenvolvimento e dos serviços do systemd; muda só que tudo cabe num binário.
/// </summary>
internal static class Server
{
    public static async Task<int> RunAsync(string[] args)
    {
        // Binário de arquivo único: o Playwright acha o driver (.playwright) pela pasta do executável, não pela pasta de onde foi chamado.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        if (Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH") is not { Length: > 0 }) Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppContext.BaseDirectory);
        if (!await Launcher.EnsureDatabaseAsync(Console.Out)) return 2;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
        var config = builder.Configuration;
        config.AddJsonFile(Path.Combine(AppPaths.Config, "settings.json"), optional: true, reloadOnChange: true);
        var modules = new[] { CampaignsModule.Assembly, JobsModule.Assembly, ApplicationsModule.Assembly, AutomationModule.Assembly, IntelligenceModule.Assembly, ProfileModule.Assembly, MaterialsModule.Assembly, AssistantModule.Assembly, MailModule.Assembly, OperationsModule.Assembly };
        builder.Services.AddSystemd();
        builder.Services.AddJoBlocks(modules).AddJoWeb(modules)
            .AddCampaignsModule(config).AddJobsModule(config).AddApplicationsModule(config).AddAutomationModule(config).AddIntelligenceModule(config).AddProfileModule(config)
            .AddMaterialsModule(config).AddAssistantModule().AddMailModule(config).AddOperationsModule(config)
            .AddMailWorker().AddOperationsWorker().AddCampaignsWorker().AddJobsWorker().AddApplicationsWorker(config).AddProfileWorker();
        builder.Services.AddSingleton<IMessageBus, LocalBus>();
        builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        var app = builder.Build();
        await app.Services.MigrateCampaignsAsync(config);
        await app.Services.MigrateJobsAsync();
        await app.Services.MigrateApplicationsAsync();
        await app.Services.MigrateAutomationAsync();
        await app.Services.MigrateIntelligenceAsync();
        await app.Services.MigrateProfileAsync(config);
        await app.Services.MigrateMaterialsAsync();
        await app.Services.MigrateMailAsync();

        app.UseExceptionHandler();
        app.MapOpenApi("/api/openapi.json");
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "job-orchestrator", version = Launcher.Version })).WithTags("Health");
        app.MapCarter();
        app.MapFallback("/api/{**rest}", () => Results.NotFound());
        Panel(app, config["Web:Root"] is { Length: > 0 } root ? root : Path.Combine(AppContext.BaseDirectory, "wwwroot"));

        File.WriteAllText(Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.pid"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await app.RunAsync();
        return 0;
    }

    /// <summary>O painel (Angular) compilado, servido pelo próprio processo; rota desconhecida cai no index.html.</summary>
    internal static void Panel(WebApplication app, string web)
    {
        if (!Directory.Exists(web)) return;
        var files = new PhysicalFileProvider(Path.GetFullPath(web));
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
    }
}
