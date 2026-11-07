using Dapper;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Jobs.Data;
using JobOrchestrator.Jobs.Features;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>LinkedIn coverage counts what each way in really delivered, field by field, and leaves other sites out.</summary>
[Trait("Category", "E2E")]
public sealed class LinkedInCoverageDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task Coverage_counts_fields_per_way_in()
    {
        var name = $"jo_li_{Guid.NewGuid():N}"[..20];
        await using (var admin = new NpgsqlConnection(Admin))
        {
            await admin.OpenAsync();
            await admin.ExecuteAsync($"create database {name}");
        }
        var conn = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<JobsDbContext>();
            options.UseModuleDatabase(conn, "jobs");
            await using var db = new JobsDbContext(options.Options);
            await db.Database.MigrateAsync();
            await using (var c = new NpgsqlConnection(conn))
            {
                await c.OpenAsync();
                const string insert = """
                    insert into jobs.postings (id, campaign_id, source, external_id, canonical_key, company, title, location, apply_url, description, breakdown,
                      status, apply_channel, open, enriched, integrity_signals, first_seen_at, last_seen_at, apply_email)
                    values (gen_random_uuid(), gen_random_uuid(), @Source, gen_random_uuid()::text, gen_random_uuid()::text, @Company, 'Dev .NET', @Location, @Url,
                      @Description, '{}', 'New', 'manual', true, false, 0, now(), now(), null)
                    """;
                var longText = new string('x', 300);
                await c.ExecuteAsync(insert, new { Source = "alertas:email", Company = "Acme", Location = "Remoto", Url = "https://www.linkedin.com/jobs/view/1/", Description = "" });
                await c.ExecuteAsync(insert, new { Source = "alertas:email", Company = "Beta", Location = "", Url = "https://www.linkedin.com/jobs/view/2/", Description = "" });
                await c.ExecuteAsync(insert, new { Source = "manual:linkedin.com", Company = "Gama", Location = "São Paulo", Url = "https://www.linkedin.com/jobs/view/3/", Description = longText });
                await c.ExecuteAsync(insert, new { Source = "greenhouse:acme", Company = "Acme", Location = "Remoto", Url = "https://boards.greenhouse.io/acme/jobs/9", Description = longText });
            }

            var rows = await LinkedInCoverageEndpoints.QueryAsync(db, CancellationToken.None);

            Assert.Equal(["alertas:email", "manual:linkedin.com"], rows.Select(r => r.Source));
            Assert.Equal(new LinkedInCoverage("alertas:email", 2, 2, 2, 1, 0, 0, 0), rows[0]);
            Assert.Equal(new LinkedInCoverage("manual:linkedin.com", 1, 1, 1, 1, 1, 0, 0), rows[1]);
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
