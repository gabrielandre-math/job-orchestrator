using Dapper;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Applications.Domain;
using JobOrchestrator.Applications.Features;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>The confirmation page seen by the browser copilot marks the application you sent by the site as sent, with that page as proof.</summary>
[Trait("Category", "E2E")]
public sealed class CopilotDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task The_confirmation_page_sends_the_application_that_went_by_the_site()
    {
        var name = $"jo_cop_{Guid.NewGuid():N}"[..20];
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
        }
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationsDbContext>();
            options.UseModuleDatabase(new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString, "applications");
            await using var db = new ApplicationsDbContext(options.Options);
            await db.Database.MigrateAsync();
            var now = DateTime.UtcNow;
            JobApplication Make(string company, ApplicationState state)
            {
                var app = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), $"{company}:1", company, "Dev .NET", "Remoto", "https://jobs.example/1", 82, "ats"), now);
                app.MaterialsReady("/tmp/cv.pdf", new string('a', 64), now);
                app.Move(ApplicationState.ManualHandoff, "O site pede login.", "Preparador", now);
                if (state == ApplicationState.NeedsHuman) { app.Move(ApplicationState.FormPreparing, "De novo.", "Supervisor", now); app.Move(ApplicationState.NeedsHuman, "Pergunta nova.", "Preparador", now); }
                db.Applications.Add(app);
                return app;
            }
            var site = Make("Acme", ApplicationState.ManualHandoff);
            var asked = Make("Beta", ApplicationState.NeedsHuman);
            await db.SaveChangesAsync();
            var handler = new CopilotSentHandler(db, TimeProvider.System);

            Assert.True((await handler.Handle(new CopilotSentCommand(site.Id, "https://jobs.example/1/thanks", "Thank you for applying"), CancellationToken.None)).IsSuccess);
            Assert.True((await handler.Handle(new CopilotSentCommand(asked.Id, "https://jobs.example/1/thanks", "Candidatura enviada"), CancellationToken.None)).IsSuccess);
            await db.SaveChangesAsync();

            var a = await db.Applications.AsNoTracking().SingleAsync(x => x.Id == site.Id);
            var b = await db.Applications.AsNoTracking().SingleAsync(x => x.Id == asked.Id);
            Assert.Equal((ApplicationState.SubmittedConfirmed, 3), (a.State, a.EvidenceLevel));
            Assert.Equal(ApplicationState.SubmittedConfirmed, b.State);
            Assert.Contains(await db.Events.AsNoTracking().Where(e => e.ApplicationId == asked.Id).Select(e => e.To).ToListAsync(), t => t == "SubmittedDeclared");
            Assert.Equal(2, await db.Evidence.AsNoTracking().CountAsync(e => e.Kind == "copilot_confirmation"));
            // A second confirmation of the same application changes nothing.
            Assert.True((await new CopilotSentHandler(db, TimeProvider.System).Handle(new CopilotSentCommand(site.Id, "x", "y"), CancellationToken.None)).IsSuccess);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }
}
