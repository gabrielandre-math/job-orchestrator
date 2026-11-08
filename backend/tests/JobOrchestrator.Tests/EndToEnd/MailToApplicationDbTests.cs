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

    [Fact]
    public async Task A_platform_notice_confirms_the_application_you_sent_yourself_and_marks_the_step()
    {
        var name = $"jo_m2p_{Guid.NewGuid():N}"[..20];
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
            JobApplication Handoff(string company, string title)
            {
                var app = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), $"{company}:{title}", company, title, "Remoto", "https://jobs.example/1", 80, "ats"), now);
                app.MaterialsReady("/tmp/cv.pdf", new string('a', 64), now);
                app.Move(ApplicationState.ManualHandoff, "Kit pronto para você.", "Supervisor", now);
                db.Applications.Add(app);
                return app;
            }
            var mine = Handoff("Konsi Tecnologia", "Desenvolvedor .NET");
            var other = Handoff("Acme", "Analista de Dados");
            await db.SaveChangesAsync();
            var consumer = new HiringMailConsumer(db, TimeProvider.System);

            // Mail from before the application existed is about something else.
            await consumer.ConsumeAsync(Send(HiringMailV1.EventType, new HiringMailV1("<old@lever>", "Konsi <no-reply@hire.lever.co>", "Thanks for applying to Konsi", "", "Konsi", "application_received", now.AddDays(-30))), CancellationToken.None);
            Assert.Equal(ApplicationState.ManualHandoff, (await db.Applications.AsNoTracking().SingleAsync(a => a.Id == mine.Id)).State);

            await consumer.ConsumeAsync(Send(HiringMailV1.EventType, new HiringMailV1("<r1@lever>", "Konsi <no-reply@hire.lever.co>", "Thanks for applying to Konsi", "", "Konsi", "application_received", now.AddMinutes(1))), CancellationToken.None);
            await consumer.ConsumeAsync(Send(HiringMailV1.EventType, new HiringMailV1("<r2@lever>", "Konsi <no-reply@hire.lever.co>", "Konsi: next steps", "We would like to schedule an interview", "Konsi", "interview_invite", now.AddMinutes(2))), CancellationToken.None);

            var after = await db.Applications.AsNoTracking().SingleAsync(a => a.Id == mine.Id);
            Assert.Equal(ApplicationState.SubmittedConfirmed, after.State);
            Assert.Equal(3, after.EvidenceLevel);
            Assert.Equal("Entrevista", after.Outcome);
            Assert.Equal(2, await db.Evidence.AsNoTracking().CountAsync(x => x.ApplicationId == mine.Id && x.Kind == "platform_mail"));
            // The history says the e-mail moved the step, not you.
            var step = await db.Events.AsNoTracking().SingleAsync(x => x.ApplicationId == mine.Id && x.To == "Entrevista");
            Assert.Equal(("Correio", "Aviso da plataforma por e-mail: de Enviada para Entrevista."), (step.Actor, step.Reason));
            Assert.Equal(ApplicationState.ManualHandoff, (await db.Applications.AsNoTracking().SingleAsync(a => a.Id == other.Id)).State);
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
