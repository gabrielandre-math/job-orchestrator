using System.Text.Json.Serialization;
using Carter;
using JobOrchestrator.Applications;
using JobOrchestrator.Assistant;
using JobOrchestrator.Automation;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Web;
using JobOrchestrator.Campaigns;
using JobOrchestrator.Intelligence;
using JobOrchestrator.Jobs;
using JobOrchestrator.Mail;
using JobOrchestrator.Operations;
using JobOrchestrator.Materials;
using JobOrchestrator.Profile;
using Microsoft.Extensions.FileProviders;

// API e painel. Só escuta em 127.0.0.1; o trabalho pesado fica no Worker.
var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;
// Escolhas feitas na tela (modelos locais e afins) ficam em settings.json no diretório de configuração e recarregam sozinhas.
config.AddJsonFile(Path.Combine(AppPaths.Config, "settings.json"), optional: true, reloadOnChange: true);
var modules = new[] { CampaignsModule.Assembly, JobsModule.Assembly, ApplicationsModule.Assembly, AutomationModule.Assembly, IntelligenceModule.Assembly, ProfileModule.Assembly, MaterialsModule.Assembly, AssistantModule.Assembly, MailModule.Assembly, OperationsModule.Assembly };
builder.Services.AddJoBlocks(modules).AddJoWeb(modules)
    .AddCampaignsModule(config).AddJobsModule(config).AddApplicationsModule(config).AddAutomationModule(config).AddIntelligenceModule(config).AddProfileModule(config).AddMaterialsModule(config).AddAssistantModule().AddMailModule(config).AddOperationsModule(config);
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
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).WithTags("Health");
app.MapCarter();
app.MapFallback("/api/{**rest}", () => Results.NotFound());

// O painel (Angular) compilado é servido pela própria API.
var web = config["Web:Root"] is { Length: > 0 } root ? root : Path.Combine(AppContext.BaseDirectory, "wwwroot");
if (Directory.Exists(web))
{
    var files = new PhysicalFileProvider(Path.GetFullPath(web));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = files });
}

app.Run();
