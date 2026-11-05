using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// jo sources boards: importa os catálogos públicos de quadros de ATS (job-board-directory e LastRound AI), converte cada linha para a fonte
/// do app (greenhouse:token, lever:eu/slug, workday:host/site...), deduplica, guarda de onde veio e a licença, valida uma amostra de verdade
/// pela API do app e grava data/ats-board-catalog.json. Quadro do catálogo é pista: entra na campanha só pelo Explorador, depois de testado.
/// </summary>
internal static class Boards
{
    private sealed record Catalog(string Id, string Url, string License, string Credit);
    private sealed class Board { public string Source = ""; public string Platform = ""; public string Company = ""; public int? Roles; public SortedSet<string> From = []; public string? Seen; }

    private static readonly Catalog[] Catalogs =
    [
        new("job-board-directory", "https://kalebconfer-sys.github.io/job-board-directory/job-board-directory.csv",
            "sem licença declarada no GitHub (conferido em 05/11/2026): pista de uso pessoal, não redistribuir", "github.com/kalebconfer-sys/job-board-directory"),
        new("lastroundai", "https://raw.githubusercontent.com/fyrosofttech/lastroundai-hiring-data/main/ats-directory/lastroundai-ats-company-directory-2026-08.csv",
            "CC BY 4.0", "LastRound AI hiring data (lastroundai.com/blog/salary-transparency-study-2026)"),
    ];

    public static async Task<int> ImportAsync(string api, string output, int sample)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("JobOrchestrator/1.0 (+uso pessoal)");
        var boards = new Dictionary<string, Board>(StringComparer.OrdinalIgnoreCase);
        var rows = new Dictionary<string, int>();
        foreach (var c in Catalogs)
        {
            var lines = (await http.GetStringAsync(c.Url)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var head = Split(lines[0]);
            int Col(string n) => Array.IndexOf(head, n);
            rows[c.Id] = lines.Length - 1;
            foreach (var line in lines.Skip(1))
            {
                var f = Split(line);
                string Get(string n) => Col(n) is var i && i >= 0 && i < f.Length ? f[i].Trim() : "";
                var platform = (c.Id == "lastroundai" ? Get("ats_vendor") : Get("platform")).ToLowerInvariant();
                var source = c.Id == "lastroundai" ? $"{platform}:{Get("board_slug")}" : Source(platform, Get("token"), Get("board_url"));
                if (source is null || source.EndsWith(':')) continue;
                if (!boards.TryGetValue(source, out var b)) boards[source] = b = new Board { Source = source, Platform = platform };
                b.Company = b.Company.Length > 0 ? b.Company : c.Id == "lastroundai" ? Get("company_name") : Get("company");
                if (int.TryParse(Get("open_roles"), out var roles)) b.Roles = roles;
                if (Get("last_crawled") is { Length: > 0 } seen) b.Seen = seen;
                b.From.Add(c.Id);
            }
        }

        // Validação de verdade numa amostra espalhada pelas plataformas, uma de cada vez, pela mesma porta oficial da rodada.
        var picked = boards.Values.GroupBy(b => b.Platform).SelectMany(g => g.OrderBy(b => Stable(b.Source)).Take(Math.Max(1, sample / Math.Max(1, boards.Values.Select(x => x.Platform).Distinct().Count())))).Take(sample).ToList();
        var checks = new Dictionary<string, JsonObject>();
        using var app = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        foreach (var b in picked)
        {
            try
            {
                var r = await (await app.PostAsJsonAsync("api/sources/test", new { source = b.Source })).Content.ReadFromJsonAsync<JsonObject>();
                checks[b.Source] = new JsonObject { ["ok"] = r?["ok"]?.GetValue<bool>(), ["count"] = r?["count"]?.GetValue<int>(), ["http"] = r?["http"]?.DeepClone(), ["error"] = r?["error"]?.DeepClone(), ["at"] = DateTime.UtcNow.ToString("O") };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { checks[b.Source] = new JsonObject { ["ok"] = false, ["error"] = ex.Message, ["at"] = DateTime.UtcNow.ToString("O") }; }
            Console.Write('.');
        }
        Console.WriteLine();

        var ok = checks.Values.Count(x => x["ok"]?.GetValue<bool>() == true && (x["count"]?.GetValue<int>() ?? 0) > 0);
        var empty = checks.Values.Count(x => x["ok"]?.GetValue<bool>() == true && (x["count"]?.GetValue<int>() ?? 0) == 0);
        var notFound = checks.Values.Count(x => x["http"]?.GetValue<int>() == 404);
        var doc = new JsonObject
        {
            ["generated_at"] = DateTime.UtcNow.ToString("O"), ["generated_by"] = "jo sources boards",
            ["note"] = "Quadro do catálogo é pista, não fonte funcionando: entra na campanha só pelo Explorador, depois de testado pela porta oficial.",
            ["catalogs"] = new JsonArray([.. Catalogs.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["url"] = c.Url, ["license"] = c.License, ["credit"] = c.Credit, ["rows"] = rows.GetValueOrDefault(c.Id) })]),
            ["totals"] = new JsonObject
            {
                ["rows"] = rows.Values.Sum(), ["unique_boards"] = boards.Count, ["in_both"] = boards.Values.Count(b => b.From.Count > 1),
                ["by_platform"] = new JsonObject([.. boards.Values.GroupBy(b => b.Platform).OrderByDescending(g => g.Count()).Select(g => new KeyValuePair<string, JsonNode?>(g.Key, g.Count()))]),
            },
            ["validation"] = new JsonObject { ["sampled"] = checks.Count, ["with_jobs"] = ok, ["empty"] = empty, ["not_found"] = notFound, ["failed"] = checks.Count - ok - empty, ["at"] = DateTime.UtcNow.ToString("O") },
            ["boards"] = new JsonArray([.. boards.Values.OrderBy(b => b.Source, StringComparer.OrdinalIgnoreCase).Select(b =>
            {
                var o = new JsonObject { ["source"] = b.Source, ["company"] = b.Company, ["from"] = new JsonArray([.. b.From.Select(x => (JsonNode)x)]) };
                if (b.Roles is { } n) o["open_roles"] = n;
                if (b.Seen is { } s) o["last_crawled"] = s;
                if (checks.TryGetValue(b.Source, out var c)) o["check"] = c.DeepClone();
                return (JsonNode)o;
            })]),
        };
        await File.WriteAllTextAsync(output, doc.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");
        Console.WriteLine($"{rows.Values.Sum()} linhas, {boards.Count} quadros únicos ({boards.Values.Count(b => b.From.Count > 1)} nos dois catálogos); amostra de {checks.Count}: {ok} com vaga, {empty} vazios, {notFound} não encontrados. Escrito: {output}");
        return 0;
    }

    /// <summary>A fonte do app para cada linha do job-board-directory; plataforma que o app não lê fica de fora.</summary>
    private static string? Source(string platform, string token, string url)
    {
        Uri.TryCreate(url, UriKind.Absolute, out var u);
        return platform switch
        {
            "greenhouse" or "ashby" or "workable" or "recruitee" or "personio" or "rippling" or "smartrecruiters" or "teamtailor" or "breezy" => $"{platform}:{token}",
            "lever" => u?.Host.Contains(".eu.", StringComparison.OrdinalIgnoreCase) == true ? $"lever:eu/{token}" : $"lever:{token}",
            "workday" when u is not null => $"workday:{u.Host}{u.AbsolutePath.TrimEnd('/')}",
            _ => null,
        };
    }

    private static int Stable(string s) { unchecked { var h = 17; foreach (var ch in s) h = h * 31 + ch; return h; } }

    private static string[] Split(string line)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"') { if (quoted && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == ',' && !quoted) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(ch);
        }
        fields.Add(sb.ToString());
        return [.. fields];
    }
}
