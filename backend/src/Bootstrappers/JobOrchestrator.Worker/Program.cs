using JobOrchestrator.Applications;
using JobOrchestrator.Automation;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Campaigns;
using JobOrchestrator.Jobs;

// Worker: outbox, consumidores entre módulos, fila de etapas e navegador. Roda sob systemd --user.
var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;
var modules = new[] { CampaignsModule.Assembly, JobsModule.Assembly, ApplicationsModule.Assembly, AutomationModule.Assembly };
builder.Services.AddSystemd();
builder.Services.AddJoBlocks(modules)
    .AddCampaignsModule(config).AddJobsModule(config).AddApplicationsModule(config).AddAutomationModule(config)
    .AddCampaignsWorker().AddJobsWorker().AddApplicationsWorker(config);
builder.Services.AddSingleton<IMessageBus, LocalBus>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));

var host = builder.Build();
await host.Services.MigrateCampaignsAsync(config);
await host.Services.MigrateJobsAsync();
await host.Services.MigrateApplicationsAsync();
await host.Services.MigrateAutomationAsync();
host.Run();
