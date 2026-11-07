using Dapper;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Applications.Domain;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>Applications stopped by a form question go back to the form at the update, once, for the AI to answer from the profile.</summary>
[Trait("Category", "E2E")]
public sealed class FormRetryMigrationDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task Forms_stopped_by_a_question_are_tried_again_by_the_update()
    {
        var name = $"jo_frm_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107092253_FormAnswers");
            var now = DateTime.UtcNow;
            JobApplication Stuck(string key, string reason, string channel = "ats")
            {
                var a = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), key, key, "Dev .NET", "Remoto", $"https://apply.workable.com/j/{key}", 86, channel), now);
                a.Move(ApplicationState.NeedsHuman, reason, "Preparador", now);
                db.Applications.Add(a);
                return a;
            }
            var question = Stuck("q", "Perguntas obrigatórias sem resposta aprovada: \"Qual é a sua expectativa salarial mensal em R$ para atuar como PJ?\".");
            var boxes = Stuck("c", "O formulário pede para marcar opções em \"Which JavaScript framework?\". Fica com você.");
            var captcha = Stuck("x", "A página tem CAPTCHA ou verificação anti-robô: o app não passa por ela, o envio fica com você.");
            var link = Stuck("l", "Perguntas obrigatórias sem resposta aprovada: \"Pretensão\".", "link");
            db.AddTask(question.Id, HumanTaskKind.Question, "Responder pergunta nova", "PJ", now);
            await db.SaveChangesAsync();

            await db.Database.MigrateAsync();

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            async Task<string?> State(Guid id) => await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @id", new { id });
            Assert.Equal("RetryScheduled", await State(question.Id));
            Assert.Equal("RetryScheduled", await State(boxes.Id));
            Assert.Equal("NeedsHuman", await State(captcha.Id));
            Assert.Equal("NeedsHuman", await State(link.Id));
            Assert.Equal(1L, await c.ExecuteScalarAsync<long>("select count(*) from applications.work_items where application_id = @Id and step = 'Prepare' and completed_at is null", new { question.Id }));
            Assert.Equal(0L, await c.ExecuteScalarAsync<long>("select count(*) from applications.human_tasks where application_id = @Id and resolved_at is null", new { question.Id }));
            Assert.Equal(0L, await c.ExecuteScalarAsync<long>("select count(*) from applications.work_items where application_id = @Id", new { captcha.Id }));
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
    public async Task Site_links_handed_to_you_are_tried_once_by_the_update_but_not_accounts_or_aggregators()
    {
        var name = $"jo_site_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107093000_RetryFormsWithAiAnswers");
            var now = DateTime.UtcNow;
            JobApplication Handed(string key, string url, string channel = "link")
            {
                var a = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), key, key, "Dev .NET", "Remoto", url, 80, channel), now);
                a.MaterialsReady("/tmp/cv.pdf", new string('a', 64), now);
                a.Move(ApplicationState.ManualHandoff, "A candidatura vai pelo site original. O currículo está pronto para você enviar.", "Supervisor", now);
                db.Applications.Add(a);
                return a;
            }
            var site = Handed("s", "https://jobs.quickin.io/infovagas/jobs/6a208e72ca2c330013e4535b");
            var aggregator = Handed("h", "https://himalayas.app/companies/pixeon/jobs/analista-desenvolvedor-a-c-net-pleno?utm_source=api");
            var account = Handed("g", "https://aliare.gupy.io/jobs/12079294", "manual");
            var linkedin = Handed("l", "https://www.linkedin.com/jobs/view/123");
            db.AddTask(site.Id, HumanTaskKind.Review, "Candidatar-se pelo site da vaga", "Dev .NET, s", now);
            await db.SaveChangesAsync();

            await db.Database.MigrateAsync();

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            async Task<string?> State(Guid id) => await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @id", new { id });
            Assert.Equal("RetryScheduled", await State(site.Id));
            Assert.Equal("ManualHandoff", await State(aggregator.Id));
            Assert.Equal("ManualHandoff", await State(account.Id));
            Assert.Equal("ManualHandoff", await State(linkedin.Id));
            Assert.Equal(1L, await c.ExecuteScalarAsync<long>("select count(*) from applications.work_items where application_id = @Id and step = 'Prepare'", new { site.Id }));
            Assert.Equal(0L, await c.ExecuteScalarAsync<long>("select count(*) from applications.human_tasks where application_id = @Id and resolved_at is null", new { site.Id }));
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
