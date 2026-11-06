using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Parquet;
using Parquet.Schema;

/// <summary>Uma vaga lida do export, como vai para POST /api/history/rows.</summary>
internal sealed record HistoryRow(string Ats, string Board, string Id, string Title, string? Company, string? Location, string? Url,
    DateTime? FirstSeenAt, DateTime? LastSeenAt, bool? SnapshotOpen, string? RemoteScope, string? Arrangement);

/// <summary>
/// jo history import: o import histórico resumido do Open Jobs (CC0, snapshot final de 05/10/2026) ou de outro export no mesmo formato.
/// Lê em streaming, com orçamento de disco e de memória: Parquet por grupo de linhas e só as colunas usadas (a descrição, o JSON bruto e
/// o vetor de 1536 dimensões ficam no disco sem ser lidos), CSV registro a registro, e o .tar do export (local ou pela URL) membro a
/// membro, cada Parquet extraído para a pasta de trabalho, lido e apagado antes do próximo. Por padrão só entram as vagas com uma
/// palavra-chave da campanha no título ou nas skills do enriquecimento, e só as do próprio empregador (tier first_party). Nada entra
/// aberto: o app guarda tudo como UNVERIFIED, e só a fonte ao vivo abre (jo history revalidate e o Explorador).
/// </summary>
internal static class HistoryImport
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static async Task<int> ImportAsync(string api, string from, string snapshot, bool all, int maxRows, long diskBytes, string work, int batch)
    {
        using var app = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
        IReadOnlyList<string> terms = [];
        if (!all)
        {
            var campaigns = await app.GetFromJsonAsync<JsonElement>("api/campaigns");
            terms = campaigns.ValueKind == JsonValueKind.Array && campaigns.GetArrayLength() > 0 && campaigns[0].TryGetProperty("technologies", out var t)
                ? [.. t.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => Fold(x).Length > 1)] : [];
            if (terms.Count == 0) { Console.Error.WriteLine("A campanha não tem palavras-chave. Crie a campanha ou use --all para importar tudo."); return 2; }
            Console.WriteLine($"Filtro: palavras da campanha ({string.Join(", ", terms)}) no título ou nas skills; só vagas do próprio empregador.");
        }
        var stats = new ImportStats();
        var pending = new List<HistoryRow>();

        async Task Flush()
        {
            if (pending.Count == 0) return;
            var r = await app.PostAsJsonAsync("api/history/rows", new { snapshot, rows = pending }, Web);
            if (!r.IsSuccessStatusCode) throw new InvalidOperationException($"a API recusou o lote ({(int)r.StatusCode}): {await r.Content.ReadAsStringAsync()}");
            var body = await r.Content.ReadFromJsonAsync<JsonElement>(Web);
            stats.Added += body.GetProperty("added").GetInt32();
            stats.Updated += body.GetProperty("updated").GetInt32();
            stats.WithSource += body.GetProperty("withSource").GetInt32();
            stats.Open += body.GetProperty("open").GetInt32();
            pending.Clear();
        }

        async Task Take(IAsyncEnumerable<(HistoryRow Row, string Haystack, string? Tier)> rows)
        {
            await foreach (var (row, haystack, tier) in rows)
            {
                stats.Read++;
                if (!all && (tier is "aggregator" || !terms.Any(t => Hit(haystack, t)))) continue;
                if (stats.Kept >= maxRows) { stats.Capped = true; break; }
                stats.Kept++;
                pending.Add(row);
                if (pending.Count >= batch) await Flush();
            }
        }

        Directory.CreateDirectory(work);
        var done = Path.Combine(work, $"feito-{Safe(snapshot)}.txt");
        var finished = File.Exists(done) ? File.ReadAllLines(done).ToHashSet(StringComparer.Ordinal) : [];
        foreach (var source in Inputs(from))
        {
            if (stats.Capped) break;
            if (source.EndsWith(".tar", StringComparison.OrdinalIgnoreCase))
            {
                await using var raw = source.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? await OpenRemoteAsync(source) : File.OpenRead(source);
                using var tar = new TarReader(raw);
                while (await tar.GetNextEntryAsync() is { } entry)
                {
                    if (stats.Capped) break;
                    var name = entry.Name;
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || !IsJobFile(name)) continue;
                    if (finished.Contains($"{source}#{name}")) { Console.WriteLine($"  {name}: já importado (pulando)"); continue; }
                    var free = new DriveInfo(Path.GetFullPath(work)).AvailableFreeSpace;
                    if (entry.Length > diskBytes || entry.Length > free - (2L << 30))
                    {
                        Console.WriteLine($"  {name}: {entry.Length / 1e9:F1} GB, acima do orçamento de disco ({diskBytes / 1e9:F1} GB, {free / 1e9:F0} GB livres): fica de fora");
                        stats.SkippedFiles.Add(name);
                        continue;
                    }
                    var temp = Path.Combine(work, "membro.parquet");
                    await using (var file = File.Create(temp)) await entry.DataStream!.CopyToAsync(file);
                    var before = stats.Read;
                    await Take(ReadFileAsync(temp, AtsOf(name)));
                    File.Delete(temp);
                    await Flush();
                    Console.WriteLine($"  {name}: {stats.Read - before} vagas lidas, {stats.Kept} guardadas até aqui");
                    await File.AppendAllTextAsync(done, $"{source}#{name}\n");
                }
            }
            else
            {
                var before = stats.Read;
                await Take(ReadFileAsync(source, AtsOf(source)));
                await Flush();
                Console.WriteLine($"  {Path.GetFileName(source)}: {stats.Read - before} vagas lidas, {stats.Kept} guardadas até aqui");
            }
        }
        await Flush();
        Console.WriteLine($"""

            {stats.Read} vagas lidas, {stats.Kept} guardadas ({stats.Added} novas, {stats.Updated} atualizadas, {stats.WithSource} com quadro que o app lê ao vivo).
            Abertas pelo import: {stats.Open}. Tudo entrou como UNVERIFIED; só a fonte ao vivo abre (jo history revalidate, ou o Explorador a cada ciclo).
            """);
        if (stats.Capped) Console.WriteLine($"Parou no teto de {maxRows} vagas (--max-rows).");
        if (stats.SkippedFiles.Count > 0) Console.WriteLine($"Fora do orçamento de disco: {string.Join(", ", stats.SkippedFiles)} (aumente --disk-mb).");
        return 0;
    }

    public static async Task<int> RevalidateAsync(string api, int boards)
    {
        using var app = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(20) };
        var r = await app.PostAsJsonAsync("api/history/revalidate", new { boards });
        var body = await r.Content.ReadAsStringAsync();
        if (!r.IsSuccessStatusCode) { Console.Error.WriteLine($"Falhou ({(int)r.StatusCode}): {body}"); return 1; }
        var c = JsonDocument.Parse(body).RootElement;
        Console.WriteLine($"{c.GetProperty("boards").GetInt32()} quadros conferidos ao vivo: {c.GetProperty("checked").GetInt32()} vagas, {c.GetProperty("open").GetInt32()} abertas hoje, " +
            $"{c.GetProperty("closed").GetInt32()} encerradas, {c.GetProperty("failed").GetInt32()} sem resposta (ficam como estavam).");
        return 0;
    }

    public static async Task<int> StatusAsync(string api)
    {
        using var app = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(1) };
        var s = (await app.GetFromJsonAsync<JsonElement>("api/history")).GetProperty("totals");
        int N(string k) => s.GetProperty(k).GetInt32();
        Console.WriteLine($"{N("total")} vagas no histórico: {N("unverified")} não conferidas, {N("open")} abertas ao vivo, {N("closed")} encerradas; " +
            $"{N("boards")} quadros que o app lê, {N("boardsChecked")} já conferidos; {N("brazilFit")} que quem mora no Brasil pode disputar.");
        return 0;
    }

    /// <summary>Os arquivos de entrada: um arquivo, uma pasta (os Parquet e CSV dela, jobs/ primeiro) ou a URL do .tar.</summary>
    private static IEnumerable<string> Inputs(string from)
    {
        if (from.StartsWith("http", StringComparison.OrdinalIgnoreCase) || File.Exists(from)) return [from];
        if (!Directory.Exists(from)) throw new FileNotFoundException($"Não achei {from}.");
        return Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).Where(f => IsJobFile(f) || f.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal);
    }

    // No export do Open Jobs as vagas ficam em jobs/<ats>.parquet; boards/<ats>.parquet é o cadastro dos quadros e não entra aqui.
    private static bool IsJobFile(string name) =>
        !name.Replace('\\', '/').Contains("boards/", StringComparison.OrdinalIgnoreCase) &&
        (name.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase));

    private static string AtsOf(string name) => Path.GetFileName(name).Split('.')[0].ToLowerInvariant();

    private static async Task<Stream> OpenRemoteAsync(string url)
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("JobOrchestrator/1.0 (+uso pessoal)");
        var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        if (r.IsSuccessStatusCode) return await r.Content.ReadAsStreamAsync();
        var text = (await r.Content.ReadAsStringAsync()).Trim();
        throw new HttpRequestException($"{url} respondeu {(int)r.StatusCode}: {text[..Math.Min(80, text.Length)]}");
    }

    internal static IAsyncEnumerable<(HistoryRow Row, string Haystack, string? Tier)> ReadFileAsync(string path, string atsHint, CancellationToken ct = default) =>
        path.EndsWith(".parquet", StringComparison.OrdinalIgnoreCase) ? ReadParquetAsync(path, atsHint, ct) : ReadCsvAsync(path, atsHint, ct);

    // As colunas do export que o importador lê. Os nomes seguem o export do Open Jobs; company_name, remote_scope e work_arrangement
    // cobrem exports achatados no mesmo formato.
    private static readonly string[] Wanted = ["ats", "slug", "id", "title", "org", "company_name", "company", "location", "url", "first_seen_at", "last_seen_at",
        "is_open", "tier", "enrichment_json", "remote_scope", "work_arrangement"];

    /// <summary>Um Parquet, grupo de linhas por grupo de linhas, lendo só as colunas de <see cref="Wanted"/>.</summary>
    internal static async IAsyncEnumerable<(HistoryRow Row, string Haystack, string? Tier)> ReadParquetAsync(string path, string atsHint, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        await using var reader = await ParquetReader.CreateAsync(stream, null, false, ct);
        var fields = reader.Schema.DataFields.Where(f => !f.IsArray && Wanted.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(f => f.Name.ToLowerInvariant());
        for (var g = 0; g < reader.RowGroupCount; g++)
        {
            using var group = reader.OpenRowGroupReader(g);
            var n = checked((int)group.RowCount);
            var cols = new Dictionary<string, object?[]>();
            foreach (var (name, f) in fields) cols[name] = await ColumnAsync(group, f, n, ct);
            for (var i = 0; i < n; i++)
            {
                string? S(string k) => cols.TryGetValue(k, out var c) ? c[i] as string : null;
                if (Row(S, k => cols.TryGetValue(k, out var c) ? c[i] : null, atsHint) is { } r) yield return r;
            }
        }
    }

    private static async Task<object?[]> ColumnAsync(ParquetRowGroupReader group, DataField f, int n, CancellationToken ct)
    {
        var t = Nullable.GetUnderlyingType(f.ClrType) ?? f.ClrType;
        // Texto: o Parquet.Net 6 anuncia a coluna como ReadOnlyMemory<char>; a leitura como string vale para as duas.
        if (t == typeof(string) || t == typeof(ReadOnlyMemory<char>)) { var a = new string?[n]; await group.ReadAsync(f, a.AsMemory(), null, ct); return [.. a]; }
        if (t == typeof(DateTime)) { var a = new DateTime?[n]; await group.ReadAsync(f, a.AsMemory(), null, ct); return [.. a.Select(x => (object?)x)]; }
        if (t == typeof(DateTimeOffset)) { var a = new DateTimeOffset?[n]; await group.ReadAsync(f, a.AsMemory(), null, ct); return [.. a.Select(x => (object?)x?.UtcDateTime)]; }
        if (t == typeof(bool)) { var a = new bool?[n]; await group.ReadAsync(f, a.AsMemory(), null, ct); return [.. a.Select(x => (object?)x)]; }
        if (t == typeof(long)) { var a = new long?[n]; await group.ReadAsync(f, a.AsMemory(), null, ct); return [.. a.Select(x => (object?)x)]; }
        return new object?[n];
    }

    /// <summary>Um CSV (ou .csv.gz) com cabeçalho, registro a registro (campo entre aspas pode ter vírgula e quebra de linha).</summary>
    internal static async IAsyncEnumerable<(HistoryRow Row, string Haystack, string? Tier)> ReadCsvAsync(string path, string atsHint, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var file = File.OpenRead(path);
        await using Stream input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(input, Encoding.UTF8);
        string[]? head = null;
        await foreach (var record in Records(reader, ct))
        {
            if (head is null) { head = [.. record.Select(h => h.Trim().ToLowerInvariant())]; continue; }
            string[] names = head;
            var cells = record;
            string? S(string k) => Array.IndexOf(names, k) is var i && i >= 0 && i < cells.Count && cells[i].Length > 0 ? cells[i] : null;
            if (Row(S, k => S(k), atsHint) is { } r) yield return r;
        }
    }

    private static async IAsyncEnumerable<List<string>> Records(TextReader reader, [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new char[1 << 16];
        var cell = new StringBuilder();
        var record = new List<string>();
        bool quoted = false, pendingQuote = false, any = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
            for (var i = 0; i < read; i++)
            {
                var ch = buffer[i];
                any = true;
                if (pendingQuote) { pendingQuote = false; if (ch == '"') { cell.Append('"'); continue; } quoted = false; }
                if (quoted) { if (ch == '"') pendingQuote = true; else cell.Append(ch); continue; }
                if (ch == '"') { quoted = true; continue; }
                if (ch == ',') { record.Add(cell.ToString()); cell.Clear(); continue; }
                if (ch == '\r') continue;
                if (ch == '\n') { record.Add(cell.ToString()); cell.Clear(); yield return record; record = []; any = false; continue; }
                cell.Append(ch);
            }
        if (any || cell.Length > 0 || record.Count > 0) { record.Add(cell.ToString()); yield return record; }
    }

    /// <summary>Uma linha do export vira vaga do histórico, e o texto onde as palavras da campanha são procuradas (título e skills).</summary>
    private static (HistoryRow, string, string?)? Row(Func<string, string?> s, Func<string, object?> v, string atsHint)
    {
        var id = s("id");
        var board = s("slug");
        var title = s("title");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(board) || title is null) return null;
        string? scope = s("remote_scope"), arrangement = s("work_arrangement");
        var haystack = new StringBuilder(Fold(title));
        if (s("enrichment_json") is { Length: > 2 } json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var e = doc.RootElement;
                if (e.ValueKind == JsonValueKind.Object)
                {
                    scope ??= Str(e, "remote_scope");
                    arrangement ??= Str(e, "work_arrangement");
                    foreach (var k in new[] { "alt_titles", "skills_required", "skills_preferred" })
                        if (e.TryGetProperty(k, out var list) && list.ValueKind == JsonValueKind.Array)
                            foreach (var x in list.EnumerateArray()) if (x.ValueKind == JsonValueKind.String) haystack.Append(" | ").Append(Fold(x.GetString()!));
                }
            }
            catch (JsonException) { /* enriquecimento quebrado: fica o título */ }
        }
        var row = new HistoryRow((s("ats") ?? atsHint).ToLowerInvariant(), board, id, title, s("org") ?? s("company_name") ?? s("company"), s("location"), s("url"),
            Date(v("first_seen_at")), Date(v("last_seen_at")), Bool(v("is_open")), scope, arrangement);
        return (row, haystack.ToString(), s("tier"));
    }

    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() : null;

    private static DateTime? Date(object? value) => value switch
    {
        DateTime d => DateTime.SpecifyKind(d, d.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : d.Kind).ToUniversalTime(),
        long ms => DateTimeOffset.FromUnixTimeMilliseconds(ms > 100_000_000_000_000 ? ms / 1000 : ms).UtcDateTime,
        string t when long.TryParse(t, out var ms) => Date(ms),
        string t when DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) => d.UtcDateTime,
        _ => null,
    };

    private static bool? Bool(object? value) => value switch { bool b => b, string t when bool.TryParse(t, out var b) => b, string t => t is "1" or "t" ? true : t is "0" or "f" ? false : null, _ => null };

    /// <summary>A palavra da campanha aparece inteira (C#, .NET, React...): sem letra ou número colado antes ou depois.</summary>
    internal static bool Hit(string haystack, string term)
    {
        var t = Fold(term);
        if (t.Length < 2) return false;
        for (var i = haystack.IndexOf(t, StringComparison.Ordinal); i >= 0; i = haystack.IndexOf(t, i + 1, StringComparison.Ordinal))
        {
            var end = i + t.Length;
            if ((i == 0 || !char.IsLetterOrDigit(haystack[i - 1])) && (end >= haystack.Length || !char.IsLetterOrDigit(haystack[end]))) return true;
        }
        return false;
    }

    internal static string Fold(string s)
    {
        var d = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString();
    }

    private static string Safe(string s) => string.Concat(s.Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-'));

    private sealed class ImportStats
    {
        public int Read, Kept, Added, Updated, WithSource, Open;
        public bool Capped;
        public List<string> SkippedFiles { get; } = [];
    }
}
