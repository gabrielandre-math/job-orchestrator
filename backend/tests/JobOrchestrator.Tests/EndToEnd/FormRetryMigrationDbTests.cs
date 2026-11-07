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

            await db.GetService<IMigrator>().MigrateAsync("20261107093000_RetryFormsWithAiAnswers");

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

    [Fact]
    public async Task Questions_lost_by_1_1_0_go_back_to_the_form_on_platforms_and_sites()
    {
        var name = $"jo_qfx_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107110000_TrySiteFormsOnce");
            var now = DateTime.UtcNow;
            JobApplication Stuck(string key, string channel, string reason)
            {
                var a = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), key, key, "Dev .NET", "Remoto", $"https://example.com/{key}", 80, channel), now);
                a.Move(ApplicationState.NeedsHuman, reason, "Preparador", now);
                db.Applications.Add(a);
                return a;
            }
            var platform = Stuck("p", "ats", "Perguntas obrigatórias sem resposta aprovada: \"Walk us through a feature you owned end-to-end.\".");
            var site = Stuck("s", "link", "Pergunta obrigatória sem resposta aprovada: \"Cargo pretendido\".");
            var captcha = Stuck("c", "link", "A página tem CAPTCHA ou verificação anti-robô: o app não passa por ela.");
            await db.SaveChangesAsync();

            await db.GetService<IMigrator>().MigrateAsync("20261107190000_RetryFormsAfterQuestionFix");

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            async Task<string?> State(Guid id) => await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @id", new { id });
            Assert.Equal("RetryScheduled", await State(platform.Id));
            Assert.Equal("RetryScheduled", await State(site.Id));
            Assert.Equal("NeedsHuman", await State(captcha.Id));
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
    public async Task Dead_letters_from_the_radio_bug_and_paused_workable_go_back_to_the_form()
    {
        var name = $"jo_fil_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107190000_RetryFormsAfterQuestionFix");
            var now = DateTime.UtcNow;
            JobApplication Stuck(string key, string reason)
            {
                var a = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), key, key, "Dev .NET", "Remoto", $"https://jobs.workable.com/view/{key}", 86), now);
                a.Move(ApplicationState.NeedsHuman, reason, "Preparador", now);
                db.Applications.Add(a);
                return a;
            }
            var paused = Stuck("w", "Nenhum adapter testado atende este domínio e esta etapa.");
            var captcha = Stuck("c", "A página tem verificação anti-robô (Cloudflare ou CAPTCHA): o app não passa por ela, o envio fica com você.");
            var dead = Stuck("d", "Preenchendo o formulário.");
            await db.SaveChangesAsync();
            var gaveUp = "A etapa Prepare falhou 4 vezes: A resposta de \"How many years?\" não ficou marcada.";
            await db.Database.ExecuteSqlInterpolatedAsync($"update applications.applications set state = 'DeadLetter', last_reason = {gaveUp} where id = {dead.Id}");

            await db.GetService<IMigrator>().MigrateAsync("20261107200000_RetryAfterFillFixes");

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            async Task<string?> State(Guid id) => await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @id", new { id });
            Assert.Equal("RetryScheduled", await State(paused.Id));
            Assert.Equal("RetryScheduled", await State(dead.Id));
            Assert.Equal("NeedsHuman", await State(captcha.Id));
            Assert.Equal("DeadLetter", await c.ExecuteScalarAsync<string>("""select "from" from applications.events where application_id = @Id and "to" = 'RetryScheduled'""", new { dead.Id }));
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
    public async Task The_cleanup_sends_back_what_the_app_can_now_do_and_hands_you_the_technical_stops()
    {
        var name = $"jo_clr_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107200000_RetryAfterFillFixes");
            var now = DateTime.UtcNow;
            JobApplication Stuck(string key, string url, string channel, ApplicationState state, string reason)
            {
                var a = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), key, key, "Dev .NET", "Remoto", url, 80, channel), now);
                a.MaterialsReady("/tmp/cv.pdf", new string('a', 64), now);
                if (state == ApplicationState.ManualHandoff) a.Move(ApplicationState.ManualHandoff, reason, "Supervisor", now);
                else { a.Move(ApplicationState.FormPreparing, "Preenchendo o formulário.", "Preparador", now); a.Move(state, reason, "Preparador", now); }
                db.Applications.Add(a);
                return a;
            }
            var refused = Stuck("i", "https://jobs.workable.com/view/i", "ats", ApplicationState.Failed, "O ATS recusou o formulário e mostrou erro de validação.");
            var expired = Stuck("w", "https://br.whatjobs.com/pub_api__cpl__1__7076", "link", ApplicationState.ManualHandoff, "A candidatura vai pelo site original. O currículo está pronto para você enviar.");
            var declaration = Stuck("d", "https://jobs.workable.com/view/d", "ats", ApplicationState.NeedsHuman, "O formulário pede uma declaração: \"I confirm...\". Só você marca.");
            var gupy = Stuck("g", "https://aliare.gupy.io/jobs/1", "manual", ApplicationState.ManualHandoff, "A candidatura vai pelo site original. O currículo está pronto para você enviar.");
            var captcha = Stuck("c", "https://jobs.smartrecruiters.com/x/1", "ats", ApplicationState.NeedsHuman, "O SmartRecruiters mostra uma verificação anti-robô para navegador automático: o app não passa por ela, a candidatura fica com você.");
            await db.SaveChangesAsync();

            await db.Database.MigrateAsync();

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            async Task<string?> State(Guid id) => await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @id", new { id });
            Assert.Equal("RetryScheduled", await State(refused.Id));
            Assert.Equal("RetryScheduled", await State(expired.Id));
            Assert.Equal("RetryScheduled", await State(declaration.Id));
            Assert.Equal("ManualHandoff", await State(gupy.Id));
            Assert.Equal("ManualHandoff", await State(captcha.Id));
            Assert.Equal(1L, await c.ExecuteScalarAsync<long>("select count(*) from applications.human_tasks where application_id = @Id and resolved_at is null and kind = 'Review'", new { captcha.Id }));
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
