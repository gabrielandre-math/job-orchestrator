using System.Reflection;
using JobOrchestrator.BuildingBlocks.Data;
using JobOrchestrator.Jobs.Contracts;
using JobOrchestrator.Jobs.Data;
using JobOrchestrator.Jobs.Domain;
using JobOrchestrator.Jobs.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace JobOrchestrator.Tests.EndToEnd;

/// <summary>
/// Import histórico contra o Postgres de teste (infra/compose.test.yaml), sem subir processos: o lote do importador entra como
/// UNVERIFIED e não cria vaga na fila; a conferência com a fonte ao vivo abre o que o quadro lista, fecha o que sumiu e deixa como
/// estava o que não deu para conferir (portão fechado, quadro fora do ar, ATS sem conector).
/// </summary>
[Trait("Category", "E2E")]
public sealed class HistoryDbTests
{
    private const string Admin = "Host=127.0.0.1;Port=5463;Username=jo;Password=jo-test;Database=jo_test";

    [Fact]
    public async Task Import_nao_abre_vaga_e_so_a_fonte_ao_vivo_abre_ou_fecha()
    {
        var name = $"jo_hist_{Guid.NewGuid():N}"[..20];
        await using (var c = new NpgsqlConnection(Admin))
        {
            await c.OpenAsync();
            await new NpgsqlCommand($"create database {name}", c).ExecuteNonQueryAsync();
        }
        var conn = new NpgsqlConnectionStringBuilder(Admin) { Database = name }.ConnectionString;
        try
        {
            var options = new DbContextOptionsBuilder<JobsDbContext>();
            options.UseModuleDatabase(conn, "jobs");
            await using var db = new JobsDbContext(options.Options);
            await db.Database.MigrateAsync();
            static HistoryRowIn Row(string ats, string board, string id) => new(ats, board, id, "Senior .NET Engineer", "Acme", "Remote - LATAM",
                $"https://example.com/{board}/{id}", DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-32), true, "LATAM", "remote");
            HistoryRowIn[] rows = [Row("greenhouse", "acme", "4001"), Row("greenhouse", "acme", "4002"), Row("lever", "fechada", "a1"), Row("ashby", "fora", "b1"), Row("paylocity", "semconector", "c1")];

            var first = (await new ImportHistoryHandler(db, TimeProvider.System).Handle(new ImportHistoryCommand("openjobs:2026-10-05", rows), default)).Value!;
            await db.SaveChangesAsync();
            Assert.Equal((5, 0, 4, 0), (first.Added, first.Updated, first.WithSource, first.Open));
            Assert.All(await db.History.ToListAsync(), h => Assert.Equal(HistoricalPosting.Unverified, h.Status));
            Assert.Equal(0, await db.Postings.CountAsync());
            // O mesmo lote de novo atualiza, não duplica.
            var again = (await new ImportHistoryHandler(db, TimeProvider.System).Handle(new ImportHistoryCommand("openjobs:2026-10-05", rows), default)).Value!;
            await db.SaveChangesAsync();
            Assert.Equal((0, 5), (again.Added, again.Updated));

            var automation = AutomationStub.Of(source => source switch
            {
                "greenhouse:acme" => [new DiscoveredPosting("greenhouse:acme", "4001", "Acme", "Senior .NET Engineer", "Remote", "https://example.com/acme/4001", "", DateTime.UtcNow, "{}")],
                "lever:fechada" => throw new InvalidOperationException("BLOCKED_BY_POLICY: teste"),
                _ => throw new HttpRequestException("503 Service Unavailable"),
            });
            var check = (await new RevalidateHistoryHandler(db, automation, TimeProvider.System, NullLogger<RevalidateHistoryHandler>.Instance)
                .Handle(new RevalidateHistoryCommand(10), default)).Value!;
            Assert.Equal(new HistoryCheck(3, 4, 1, 1, 2), check);

            db.ChangeTracker.Clear();
            var all = await db.History.ToDictionaryAsync(h => h.ExternalId);
            Assert.Equal(HistoricalPosting.Open, all["4001"].Status);
            Assert.Equal(HistoricalPosting.Closed, all["4002"].Status);
            Assert.Equal(HistoricalPosting.Unverified, all["a1"].Status);
            Assert.StartsWith("não conferida: BLOCKED_BY_POLICY", all["a1"].Note);
            Assert.Equal(HistoricalPosting.Unverified, all["b1"].Status);
            Assert.StartsWith("o quadro não respondeu", all["b1"].Note);
            Assert.Null(all["c1"].Source);
            Assert.Null(all["c1"].CheckedAt);
            Assert.Equal(0, await db.Postings.CountAsync());

            // O quadro com vaga aberta conferida vira pista do Explorador.
            Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            await using var read = new ReadDb<JobsDbContext>(NpgsqlDataSource.Create(conn));
            Assert.Equal(new[] { new HistoryLead("greenhouse:acme", 1) }, await new JobsApi(read, null!, null!).HistoryLeadsAsync(10, default));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var c = new NpgsqlConnection(Admin);
            await c.OpenAsync();
            await new NpgsqlCommand($"drop database if exists {name} with (force)", c).ExecuteNonQueryAsync();
        }
    }

    /// <summary>IAutomationApi de teste: só a rodada de uma fonte responde; o resto não é usado aqui.</summary>
    public class AutomationStub : DispatchProxy
    {
        private Func<string, IReadOnlyList<DiscoveredPosting>> _discover = _ => [];

        public static IAutomationApi Of(Func<string, IReadOnlyList<DiscoveredPosting>> discover)
        {
            var proxy = Create<IAutomationApi, AutomationStub>();
            ((AutomationStub)(object)proxy)._discover = discover;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != nameof(IAutomationApi.DiscoverAsync)) throw new NotSupportedException(method?.Name);
            try { return Task.FromResult(_discover((string)args![0]!)); }
            catch (Exception ex) { return Task.FromException<IReadOnlyList<DiscoveredPosting>>(ex); }
        }
    }
}
