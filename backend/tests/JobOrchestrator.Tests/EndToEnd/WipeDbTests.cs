using Dapper;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Automation.Data;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Campaigns.Data;
using JobOrchestrator.Intelligence.Data;
using JobOrchestrator.Jobs.Data;
using JobOrchestrator.Mail.Data;
using JobOrchestrator.Materials.Data;
using JobOrchestrator.Operations.Features;
using JobOrchestrator.Profile.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>"Apagar tudo" against the full schema of every module, on the test Postgres (infra/compose.test.yaml).</summary>
[Trait("Category", "E2E")]
public sealed class WipeDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task Wipe_empties_every_data_table_including_interviews_and_keeps_the_settings()
    {
        var name = $"jo_wipe_{Guid.NewGuid():N}"[..20];
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
        }
        var conn = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        try
        {
            await Migrate<JobsDbContext>(conn, "jobs", o => new JobsDbContext(o));
            await Migrate<CampaignsDbContext>(conn, "campaigns", o => new CampaignsDbContext(o));
            await Migrate<ApplicationsDbContext>(conn, "applications", o => new ApplicationsDbContext(o));
            await Migrate<AutomationDbContext>(conn, "automation", o => new AutomationDbContext(o));
            await Migrate<IntelligenceDbContext>(conn, "intelligence", o => new IntelligenceDbContext(o));
            await Migrate<ProfileDbContext>(conn, "profile", o => new ProfileDbContext(o));
            await Migrate<MaterialsDbContext>(conn, "materials", o => new MaterialsDbContext(o));
            await Migrate<MailDbContext>(conn, "mail", o => new MailDbContext(o));

            await using var c = new NpgsqlConnection(conn);
            await c.OpenAsync();
            var app = Guid.NewGuid();
            await c.ExecuteAsync("""
                insert into applications.applications (id, correlation_id, campaign_id, job_id, canonical_key, company, title, location, apply_url, score, state,
                  created_at, updated_at, prepared_fields, evidence_level)
                values (@app, gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 'acme:dev', 'Acme', 'Dev', 'Remote', 'https://example.com/apply', 80, 'Submitted',
                  now(), now(), '{}', 0);
                insert into applications.interviews (id, application_id, stage, created_at, pack) values (gen_random_uuid(), @app, 'screening', now(), '{}');
                """, new { app });

            var tables = await SettingsEndpoints.WipedTablesAsync(c);
            await SettingsEndpoints.WipeAsync(c);

            Assert.Contains("applications.interviews", tables);
            Assert.Contains("jobs.history", tables);
            Assert.Contains("mail.alert_postings", tables);
            Assert.DoesNotContain("mail.accounts", tables);
            Assert.DoesNotContain("intelligence.accounts", tables);
            Assert.DoesNotContain("campaigns.campaigns", tables);
            foreach (var table in tables) Assert.Equal(0L, await c.ExecuteScalarAsync<long>($"select count(*) from {table}"));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(Admin);
            await admin.OpenAsync();
            await admin.ExecuteAsync($"drop database if exists {name} with (force)");
        }
    }

    private static async Task Migrate<T>(string conn, string schema, Func<DbContextOptions<T>, T> create) where T : DbContext
    {
        var options = new DbContextOptionsBuilder<T>();
        options.UseModuleDatabase(conn, schema);
        await using var db = create(options.Options);
        await db.Database.MigrateAsync();
    }
}
