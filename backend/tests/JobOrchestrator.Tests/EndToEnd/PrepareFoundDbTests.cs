using System.Text.Json;
using Dapper;
using JobOrchestrator.Applications.Data;
using JobOrchestrator.Applications.Features;
using JobOrchestrator.BuildingBlocks;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Campaigns.Contracts;
using JobOrchestrator.Jobs.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>
/// "Preparar as vagas já encontradas" sends the queue again: a job that already has an application stays with one, a job found
/// while the campaign only searched gets its first.
/// </summary>
[Trait("Category", "E2E")]
public sealed class PrepareFoundDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    private sealed class Campaigns(SubmissionMode mode, Guid id) : ICampaignsApi
    {
        public SubmissionMode Mode { get; set; } = mode;
        private CampaignSettings Settings => new(id, "Teste", true, Mode, 70, 5, [], [], [], "America/Sao_Paulo");
        public Task<CampaignSettings?> GetAsync(Guid campaign, CancellationToken ct) => Task.FromResult<CampaignSettings?>(Settings);
        public Task<IReadOnlyList<CampaignSettings>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<CampaignSettings>>([Settings]);
    }

    [Fact]
    public async Task Sending_the_queue_again_creates_only_missing_applications()
    {
        var name = $"jo_prep_{Guid.NewGuid():N}"[..20];
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
            var campaignId = Guid.NewGuid();
            var campaigns = new Campaigns(SubmissionMode.DiscoveryOnly, campaignId);
            var consumer = new JobShortlistedConsumer(db, campaigns, TimeProvider.System);
            JobShortlistedV1 Job(string key) => new(Guid.NewGuid(), campaignId, key, "Acme", "Dev .NET", "Remoto", $"https://boards.greenhouse.io/acme/jobs/{key}", 80);
            Envelope Send(JobShortlistedV1 job) => new(Guid.NewGuid(), JobShortlistedV1.EventType, JsonSerializer.Serialize(job, JsonSerializerOptions.Web));
            var a = Job("1");
            var b = Job("2");

            await consumer.ConsumeAsync(Send(a), CancellationToken.None);
            Assert.Equal(0, await db.Applications.CountAsync());

            campaigns.Mode = SubmissionMode.ReviewRequired;
            await consumer.ConsumeAsync(Send(a), CancellationToken.None);
            await consumer.ConsumeAsync(Send(b), CancellationToken.None);
            await consumer.ConsumeAsync(Send(a), CancellationToken.None);

            Assert.Equal(["1", "2"], (await db.Applications.Select(x => x.CanonicalKey).ToListAsync()).Order());
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
