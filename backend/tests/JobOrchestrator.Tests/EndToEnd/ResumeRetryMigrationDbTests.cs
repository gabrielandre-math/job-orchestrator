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

/// <summary>The applications stopped by the ligature bug go back to the resume step at the update, once.</summary>
[Trait("Category", "E2E")]
public sealed class ResumeRetryMigrationDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task Stuck_resumes_are_retried_by_the_update()
    {
        var name = $"jo_lig_{Guid.NewGuid():N}"[..20];
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
            await db.GetService<IMigrator>().MigrateAsync("20261107003832_ResumeVetted");
            var now = DateTime.UtcNow;
            var stuck = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), "acme:1", "Acme", "Dev .NET", "Remoto", "https://boards.greenhouse.io/acme/jobs/1", 90), now);
            stuck.Move(ApplicationState.NeedsHuman, "O currículo não ficou pronto: Afirmações presentes no texto: 14 de 19", "Redator", now);
            var other = JobApplication.Create(new JobShortlistedV1(Guid.NewGuid(), Guid.NewGuid(), "beta:1", "Beta", "Dev .NET", "Remoto", "https://boards.greenhouse.io/beta/jobs/1", 90), now);
            other.Move(ApplicationState.NeedsHuman, "Sem perfil. Envie o seu currículo na tela Perfil.", "Guardião", now);
            db.Applications.AddRange(stuck, other);
            db.AddTask(stuck.Id, HumanTaskKind.Review, "Currículo não ficou pronto", "14 de 19", now);
            await db.SaveChangesAsync();

            await db.Database.MigrateAsync();

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            Assert.Equal("RetryScheduled", await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @Id", new { stuck.Id }));
            Assert.Equal("NeedsHuman", await c.ExecuteScalarAsync<string>("select state from applications.applications where id = @Id", new { other.Id }));
            Assert.Equal(1L, await c.ExecuteScalarAsync<long>("select count(*) from applications.work_items where application_id = @Id and step = 'Materials'", new { stuck.Id }));
            Assert.Equal(0L, await c.ExecuteScalarAsync<long>("select count(*) from applications.human_tasks where application_id = @Id and resolved_at is null", new { stuck.Id }));
            Assert.Equal(1L, await c.ExecuteScalarAsync<long>("""select count(*) from applications.events where application_id = @Id and "to" = 'RetryScheduled'""", new { stuck.Id }));
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
