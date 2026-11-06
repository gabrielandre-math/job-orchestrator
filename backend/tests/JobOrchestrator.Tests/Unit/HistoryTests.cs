using System.Text.Json;
using JobOrchestrator.Jobs.Domain;
using Parquet;
using Parquet.Schema;

namespace JobOrchestrator.Tests.Unit;

/// <summary>Import histórico (Open Jobs, V2): o arquivo nunca abre vaga, só o quadro ao vivo abre ou fecha, e o leitor lê Parquet e CSV em streaming.</summary>
public sealed class HistoryTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static HistoryRowIn Row(string ats, string board, string id, bool? open = true, string? url = null, string? scope = null) =>
        new(ats, board, id, "Senior .NET Engineer", "Acme", "Remote", url ?? $"https://boards.greenhouse.io/{board}/jobs/{id}", Now.AddDays(-60), Now.AddDays(-32), open, scope, "remote");

    private static DiscoveredPosting Live(string id, bool tombstone = false) =>
        new("greenhouse:acme", id, "Acme", "Senior .NET Engineer", "Remote", $"https://boards.greenhouse.io/acme/jobs/{id}", "", Now, "{}", Tombstone: tombstone);

    [Fact]
    public void Vaga_do_snapshot_entra_nao_conferida_mesmo_marcada_aberta()
    {
        var h = HistoricalPosting.Import("openjobs:2026-10-05", Row("greenhouse", "acme", "4001", open: true, scope: "Brazil"), Now);
        Assert.Equal(HistoricalPosting.Unverified, h.Status);
        Assert.True(h.SnapshotOpen);
        Assert.Null(h.CheckedAt);
        Assert.Equal("greenhouse:acme", h.Source);
        Assert.Equal("BR_ELIGIBLE_CONFIRMED", h.Eligibility);
    }

    [Fact]
    public void So_o_quadro_ao_vivo_abre_ou_fecha()
    {
        var listed = HistoricalPosting.Import("s", Row("greenhouse", "acme", "4001"), Now);
        var gone = HistoricalPosting.Import("s", Row("greenhouse", "acme", "4002"), Now);
        var byUrl = HistoricalPosting.Import("s", Row("greenhouse", "acme", "x-9", url: "https://boards.greenhouse.io/acme/jobs/777/"), Now);
        var dead = HistoricalPosting.Import("s", Row("greenhouse", "acme", "4003"), Now);
        IReadOnlyCollection<DiscoveredPosting> live = [Live("4001"), Live("777"), Live("4003", tombstone: true)];
        foreach (var h in new[] { listed, gone, byUrl, dead }) h.Verify(live, Now.AddHours(1));
        Assert.Equal(HistoricalPosting.Open, listed.Status);
        Assert.Equal(HistoricalPosting.Closed, gone.Status);
        Assert.Contains("não está mais lá", gone.Note);
        Assert.Equal(HistoricalPosting.Open, byUrl.Status);
        Assert.Equal(HistoricalPosting.Closed, dead.Status);
        Assert.Contains("encerrada", dead.Note);
        Assert.All(new[] { listed, gone, byUrl, dead }, h => Assert.Equal(Now.AddHours(1), h.CheckedAt));
    }

    [Fact]
    public void Quadro_sem_resposta_nao_muda_o_estado_e_reimportar_mantem_o_conferido()
    {
        var h = HistoricalPosting.Import("s", Row("lever", "fechada", "a1"), Now);
        h.Fail("não conferida: BLOCKED_BY_POLICY: teste", Now);
        Assert.Equal(HistoricalPosting.Unverified, h.Status);
        Assert.Equal(1, h.Attempts);
        var open = HistoricalPosting.Import("s", Row("greenhouse", "acme", "4001"), Now);
        open.Verify([Live("4001")], Now);
        open.Refresh(Row("greenhouse", "acme", "4001", open: false) with { Title = "Staff .NET Engineer" });
        Assert.Equal(HistoricalPosting.Open, open.Status);
        Assert.Equal("Staff .NET Engineer", open.Title);
    }

    [Theory]
    [InlineData("greenhouse", "gitlab", "greenhouse:gitlab")]
    [InlineData("teamtailor", "acme.na.teamtailor.com", "teamtailor:acme.na")]
    [InlineData("personio", "1komma5grad", "personio:1komma5grad")]
    [InlineData("lever", "Spotify", "lever:Spotify")]
    [InlineData("workday", "3m.wd1.myworkdayjobs.com", null)]
    [InlineData("paylocity", "abc", null)]
    [InlineData("greenhouse", "http://127.0.0.1:7410/x", null)]
    public void Quadro_do_Open_Jobs_vira_a_fonte_do_app(string ats, string slug, string? source) => Assert.Equal(source, SourceKeys.OpenJobs(ats, slug));

    [Fact]
    public async Task Parquet_do_export_e_lido_por_grupo_e_nada_sai_aberto()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("jo-hist-").FullName, "greenhouse.parquet");
        var schema = new ParquetSchema(
            new DataField<string>("ats"), new DataField<string>("slug"), new DataField<string>("id"), new DataField<string>("title"), new DataField<string>("org"),
            new DataField<string>("location"), new DataField<string>("url"), new DataField<DateTime?>("first_seen_at"), new DataField<bool?>("is_open"),
            new DataField<string>("tier"), new DataField<string>("enrichment_json"), new DataField<string>("content"));
        await using (var file = File.Create(path))
        await using (var writer = await ParquetWriter.CreateAsync(schema, file, null, false, default))
        {
            // Dois grupos de linhas, como o export (lá são 20 mil por grupo).
            await GroupAsync(writer, schema,
            [
                ("acme", "4001", "Senior Backend Engineer", "Acme", "Remote - LATAM", "first_party", """{"work_arrangement":"remote","remote_scope":"LATAM","skills_required":["c#","postgresql"]}""", true),
                ("repost", "9", "Backend Engineer (.NET)", "Jobs Aggregator", "Remote", "aggregator", "", true),
            ]);
            await GroupAsync(writer, schema, [("acme", "4002", "Product Designer", "Acme", "New York, NY", "first_party", """{"skills_required":["figma"]}""", false)]);
        }

        var rows = new List<(HistoryRow Row, string Haystack, string? Tier)>();
        await foreach (var r in HistoryImport.ReadParquetAsync(path, "greenhouse")) rows.Add(r);
        Assert.Equal(3, rows.Count);
        var a = rows[0].Row;
        Assert.Equal(("greenhouse", "acme", "4001", "Acme"), (a.Ats, a.Board, a.Id, a.Company));
        Assert.Equal(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), a.FirstSeenAt);
        Assert.Equal((true, "LATAM", "remote"), (a.SnapshotOpen, a.RemoteScope, a.Arrangement));
        Assert.True(HistoryImport.Hit(rows[0].Haystack, "C#"));
        Assert.True(HistoryImport.Hit(rows[1].Haystack, ".NET"));
        Assert.False(HistoryImport.Hit(rows[2].Haystack, "C#"));
        Assert.Equal("aggregator", rows[1].Tier);

        // O que o importador manda é o que a API recebe, e nada sai aberto do arquivo.
        var imported = rows.Select(r => HistoricalPosting.Import("openjobs:2026-10-05", JsonSerializer.Deserialize<HistoryRowIn>(JsonSerializer.Serialize(r.Row, Web), Web)!, Now)).ToList();
        Assert.All(imported, h => Assert.Equal(HistoricalPosting.Unverified, h.Status));
        Assert.Equal("BR_ELIGIBLE_CONFIRMED", imported[0].Eligibility);
        Assert.Equal("RELOCATION_UNVERIFIED", imported[2].Eligibility);
    }

    private static async Task GroupAsync(ParquetWriter writer, ParquetSchema schema,
        (string Slug, string Id, string Title, string Org, string Location, string Tier, string Enrichment, bool Open)[] rows)
    {
        using var g = writer.CreateRowGroup();
        var f = schema.DataFields;
        await g.WriteAsync(f[0], rows.Select(_ => "greenhouse").ToArray());
        await g.WriteAsync(f[1], rows.Select(r => r.Slug).ToArray());
        await g.WriteAsync(f[2], rows.Select(r => r.Id).ToArray());
        await g.WriteAsync(f[3], rows.Select(r => r.Title).ToArray());
        await g.WriteAsync(f[4], rows.Select(r => r.Org).ToArray());
        await g.WriteAsync(f[5], rows.Select(r => r.Location).ToArray());
        await g.WriteAsync(f[6], rows.Select(r => $"https://boards.greenhouse.io/{r.Slug}/jobs/{r.Id}").ToArray());
        await g.WriteAsync<DateTime>(f[7], new ReadOnlyMemory<DateTime?>([.. rows.Select(_ => (DateTime?)new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc))]));
        await g.WriteAsync<bool>(f[8], new ReadOnlyMemory<bool?>([.. rows.Select(r => (bool?)r.Open)]));
        await g.WriteAsync(f[9], rows.Select(r => r.Tier).ToArray());
        await g.WriteAsync(f[10], rows.Select(r => r.Enrichment).ToArray());
        await g.WriteAsync(f[11], rows.Select(_ => new string('x', 4000)).ToArray());
    }

    [Fact]
    public async Task Csv_com_virgula_e_quebra_de_linha_entre_aspas_e_lido_registro_a_registro()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("jo-hist-").FullName, "lever.csv");
        await File.WriteAllTextAsync(path,
            "ats,slug,id,title,company,location,url,first_seen_at,is_open\n" +
            "lever,norte,abc-1,\"Backend Engineer, C#\",Norte,\"São Paulo,\nBrazil\",https://jobs.lever.co/norte/abc-1,2026-09-30T10:00:00Z,true\n" +
            "lever,norte,abc-2,\"Designer \"\"Sênior\"\"\",Norte,Remote,https://jobs.lever.co/norte/abc-2,1759226400000,false");
        var rows = new List<(HistoryRow Row, string Haystack, string? Tier)>();
        await foreach (var r in HistoryImport.ReadCsvAsync(path, "lever")) rows.Add(r);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("Backend Engineer, C#", "São Paulo,\nBrazil", "Norte"), (rows[0].Row.Title, rows[0].Row.Location, rows[0].Row.Company));
        Assert.Equal(new DateTime(2026, 9, 30, 10, 0, 0, DateTimeKind.Utc), rows[0].Row.FirstSeenAt);
        Assert.Equal("Designer \"Sênior\"", rows[1].Row.Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1759226400000).UtcDateTime, rows[1].Row.FirstSeenAt);
        Assert.False(rows[1].Row.SnapshotOpen);
        Assert.True(HistoryImport.Hit(rows[0].Haystack, "C#"));
        Assert.False(HistoryImport.Hit(rows[1].Haystack, "C#"));
    }
}
