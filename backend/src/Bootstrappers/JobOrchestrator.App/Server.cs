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
/// banco do modo de desenvolvimento; muda só que tudo cabe num binário. --log grava a saída num arquivo (no Windows o processo de fundo
/// não tem console), e a parada com calma vem por /api/app/stop com a chave desta sessão, que só o seu usuário lê.
/// </summary>
internal static class Server
{
    public static async Task<int> RunAsync(string[] args)
    {
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--log" && i + 1 < args.Length) { Log(args[++i]); continue; }
            if (args[i] == "--background") continue;
            rest.Add(args[i]);
        }
        // Binário de arquivo único: o Playwright acha o driver (.playwright) pela pasta dos arquivos do app (no macOS, Contents/Resources/app).
        Directory.SetCurrentDirectory(AppPaths.Resources);
        if (Environment.GetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH") is not { Length: > 0 }) Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppPaths.Resources);
        if (!await Launcher.EnsureDatabaseAsync(Console.Out)) return 2;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [.. rest], ContentRootPath = AppPaths.Resources });
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
        app.UseTrail();
        app.MapOpenApi("/api/openapi.json");
        app.MapGet("/api/health", () => Results.Ok(new { status = "ok", app = "job-orchestrator", version = Launcher.Version, sourceRevision = Launcher.SourceRevision, platform = Platform.Os })).WithTags("Health");
        // The installer answers this while it runs (--setup); the installed app says there is nothing to install, so /instalar opens the panel.
        app.MapGet("/api/setup/info", () => Results.NoContent()).WithTags("Health");
        var token = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        var tokenFile = Path.Combine(AppPaths.In(AppPaths.Data, "run"), "control.token");
        File.WriteAllText(tokenFile, token);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tokenFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // Parada com calma pedida pelo job-orchestrator --stop: só com a chave do arquivo (um site aberto no navegador não manda esse cabeçalho).
        app.MapPost("/api/app/stop", (HttpRequest req, IHostApplicationLifetime life) =>
        {
            if (!string.Equals(req.Headers["X-Jo-Control"].ToString(), token, StringComparison.Ordinal)) return Results.StatusCode(403);
            _ = Task.Delay(300).ContinueWith(_ => life.StopApplication(), TaskScheduler.Default);
            return Results.Accepted();
        }).ExcludeFromDescription();
        // O botão Sair do painel: para o servidor, o Worker e o Postgres próprio. O cabeçalho próprio só sai do painel (outra origem
        // precisaria de preflight de CORS, que este servidor não libera), então um site aberto em outra aba não consegue pedir.
        app.MapPost("/api/app/quit", (HttpRequest req, IHostApplicationLifetime life) =>
        {
            if (req.Headers["X-Jo-App"] != "quit") return Results.StatusCode(403);
            life.ApplicationStopped.Register(() => { if (PortablePostgres.Installed) PortablePostgres.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); });
            _ = Task.Delay(400).ContinueWith(_ => life.StopApplication(), TaskScheduler.Default);
            return Results.Accepted();
        }).ExcludeFromDescription();
        app.MapCarter();
        app.MapFallback("/api/{**rest}", () => Results.NotFound());
        Panel(app, config["Web:Root"] is { Length: > 0 } root ? root : Path.Combine(AppPaths.Resources, "wwwroot"));

        File.WriteAllText(Path.Combine(AppPaths.In(AppPaths.Data, "run"), "server.pid"), Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await app.RunAsync();
        return 0;
    }

    /// <summary>A saída do processo num arquivo, junto com o console (que no Windows, em segundo plano, não existe).</summary>
    private static void Log(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        var sync = TextWriter.Synchronized(writer);
        Console.SetOut(sync);
        Console.SetError(sync);
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
