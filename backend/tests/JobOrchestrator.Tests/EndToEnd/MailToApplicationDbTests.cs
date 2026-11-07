using System.Text.Json;
using Dapper;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Applications.Domain;
using JobOrchestrator.Applications.Features;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>The e-mail and the application tell one story: sent by e-mail moves the application, and the company's answer marks its step.</summary>
[Trait("Category", "E2E")]
public sealed class MailToApplicationDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    private static Envelope Send<T>(string type, T e) => new(Guid.NewGuid(), type, JsonSerializer.Serialize(e, JsonSerializerOptions.Web));

    [Fact]
    public async Task A_sent_email_sends_the_application_and_the_answer_marks_the_step()
    {
        var name = $"jo_m2a_{Guid.NewGuid():N}"[..20];
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
        }
        var conn = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationsDbContext>();
            options.UseModuleDatabase(conn, "applications");
            await using var db = new ApplicationsDbContext(options.Options);
            await db.Database.MigrateAsync();
            var now = DateTime.UtcNow;
            var job = new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), "acme:1", "Acme", "Dev .NET", "Remoto", "mailto:jobs@acme.example", 80, "email");
            var app = JobApplication.Create(job, now);
            app.MaterialsReady("/tmp/cv.pdf", new string('a', 64), now);
            app.Move(ApplicationState.ManualHandoff, "Kit pronto para você.", "Supervisor", now);
            db.Applications.Add(app);
            await db.SaveChangesAsync();

            await new JobEmailSentConsumer(db, TimeProvider.System).ConsumeAsync(
                Send(JobEmailSentV1.EventType, new JobEmailSentV1(job.JobId, "jobs@acme.example", "<a1@acme.example>", now, "first")), CancellationToken.None);
            await new JobEmailRepliedConsumer(db, TimeProvider.System).ConsumeAsync(
                Send(JobEmailRepliedV1.EventType, new JobEmailRepliedV1(job.JobId, "interview_invite", now)), CancellationToken.None);

            var after = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == app.Id);
            Assert.Equal(ApplicationState.SubmittedDeclared, after.State);
            Assert.Equal("Entrevista", after.Outcome);
            var kinds = await db.Evidence.AsNoTracking().Where(x => x.ApplicationId == app.Id).Select(x => x.Kind).ToListAsync();
            Assert.Contains("smtp_accepted", kinds);
            Assert.Contains("email_reply", kinds);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    [Theory]
    [InlineData("interview_invite", "Entrevista")]
    [InlineData("offer", "Oferta")]
    [InlineData("rejection", "Recusada")]
    [InlineData("application_received", null)]
    public void The_answer_marks_only_clear_steps(string replyClass, string? step) => Assert.Equal(step, JobEmailRepliedConsumer.Step(replyClass));
}
