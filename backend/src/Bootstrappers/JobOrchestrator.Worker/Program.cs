using JobOrchestrator.Applications;
using JobOrchestrator.Automation;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Campaigns;
using JobOrchestrator.Intelligence;
using JobOrchestrator.Jobs;
using JobOrchestrator.Materials;
using JobOrchestrator.Profile;

// Worker: outbox, consumidores entre módulos, fila de etapas e navegador. Roda sob systemd --user.
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;
config.AddJsonFile(Path.Combine(AppPaths.Config, "settings.json"), optional: true, reloadOnChange: true);
var modules = new[] { CampaignsModule.Assembly, JobsModule.Assembly, ApplicationsModule.Assembly, AutomationModule.Assembly, IntelligenceModule.Assembly, ProfileModule.Assembly, MaterialsModule.Assembly };
builder.Services.AddSystemd();
builder.Services.AddJoBlocks(modules)
    .AddCampaignsModule(config).AddJobsModule(config).AddApplicationsModule(config).AddAutomationModule(config).AddIntelligenceModule(config).AddProfileModule(config).AddMaterialsModule(config)
    .AddCampaignsWorker().AddJobsWorker().AddApplicationsWorker(config).AddProfileWorker();
builder.Services.AddSingleton<IMessageBus, LocalBus>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

var host = builder.Build();
await host.Services.MigrateCampaignsAsync(config);
await host.Services.MigrateJobsAsync();
await host.Services.MigrateApplicationsAsync();
await host.Services.MigrateAutomationAsync();
await host.Services.MigrateIntelligenceAsync();
await host.Services.MigrateProfileAsync(config);
await host.Services.MigrateMaterialsAsync();
host.Run();
