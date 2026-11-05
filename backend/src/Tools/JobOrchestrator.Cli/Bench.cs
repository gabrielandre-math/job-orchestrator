using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JobOrchestrator.Intelligence.Contracts;
using JobOrchestrator.Profile.Contracts;

/// <summary>
/// jo llm bench: roda os 50 casos de bench/fixtures.json em cada modelo, com os mesmos prompts e schemas do uso real
/// (LocalTaskSpecs), temperatura zero e sem raciocínio longo. Um modelo por vez; antes de cada um, a VRAM é esvaziada.
/// </summary>
internal static class Bench
{
    private sealed record Case(string Task, string System, string Schema, string Prompt, JsonObject Expect, string Label);
    private sealed record Outcome(string Task, string Label, bool Valid, double Score, double Seconds, string? Miss);
    private sealed record Row(string Model, double Cold, int? GpuPercent, int? VramMb, IReadOnlyList<Outcome> Outcomes);

    public static async Task<int> RunAsync(string[] models, string url, string? output, string? json)
    {
        var cases = Load();
        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromMinutes(10) };
        var rows = new List<Row>();
        foreach (var model in models)
        {
            await UnloadAllAsync(http);
            var baseline = Vram();
            Console.WriteLine($"\n{model}: carregando");
            var clock = Stopwatch.StartNew();
            var (_, warmError) = await ChatAsync(http, model, LocalTaskSpecs.QuestionMapSystem, LocalTaskSpecs.QuestionMapSchema(AnswerKeys.All.Select(k => k.Key)),
                LocalTaskSpecs.QuestionMapPrompt("Phone", null, AnswerKeys.All));
            if (warmError is not null) { Console.WriteLine($"  falhou: {warmError}"); continue; }
            var cold = clock.Elapsed.TotalSeconds;
            var (gpu, _) = await LoadedAsync(http, model);
            var vram = Vram() is { } v && baseline is { } b ? v - b : (int?)null;
            var outcomes = new List<Outcome>();
            foreach (var c in cases)
            {
                var t = Stopwatch.StartNew();
                var (content, error) = await ChatAsync(http, model, c.System, c.Schema, c.Prompt);
                var o = Grade(c, content, error, t.Elapsed.TotalSeconds);
                outcomes.Add(o);
                Console.Write(o.Score >= 0.999 ? "." : o.Valid ? "~" : "x");
            }
            Console.WriteLine();
            rows.Add(new Row(model, cold, gpu, vram, outcomes));
        }
        await UnloadAllAsync(http);
        var report = Report(rows, cases.Count);
        Console.WriteLine(report);
        if (output is { Length: > 0 }) await File.WriteAllTextAsync(output, report);
        if (json is { Length: > 0 })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(json))!);
            var summary = rows.Select(r => new
            {
                r.Model, Valid = r.Outcomes.Count(o => o.Valid), Total = r.Outcomes.Count,
                JobExtract = Math.Round(Acc(r, "job.extract"), 3), EmailClassify = Math.Round(Acc(r, "email.classify"), 3), QuestionMap = Math.Round(Acc(r, "question.map"), 3),
                Overall = Math.Round(r.Outcomes.Average(o => o.Score), 3), P50 = Math.Round(Pct(r.Outcomes.Select(o => o.Seconds), 0.5), 2),
                P95 = Math.Round(Pct(r.Outcomes.Select(o => o.Seconds), 0.95), 2), Cold = Math.Round(r.Cold, 1), r.GpuPercent, r.VramMb, At = DateTime.UtcNow
            });
            await File.WriteAllTextAsync(json, JsonSerializer.Serialize(summary, JsonSerializerOptions.Web));
        }
        return 0;
    }

    private static List<Case> Load()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bench", "fixtures.json")))!.AsObject();
        var keys = AnswerKeys.All;
        var list = new List<Case>();
        foreach (var f in root["job.extract"]!.AsArray())
            list.Add(new("job.extract", LocalTaskSpecs.JobExtractSystem, LocalTaskSpecs.JobExtractSchema,
                LocalTaskSpecs.JobExtractPrompt((string)f!["title"]!, (string)f["text"]!), f["expect"]!.AsObject(), (string)f["title"]!));
        foreach (var f in root["email.classify"]!.AsArray())
            list.Add(new("email.classify", LocalTaskSpecs.EmailClassifySystem, LocalTaskSpecs.EmailClassifySchema,
                LocalTaskSpecs.EmailClassifyPrompt((string)f!["from"]!, (string)f["subject"]!, (string)f["body"]!), f["expect"]!.AsObject(), (string)f["subject"]!));
        foreach (var f in root["question.map"]!.AsArray())
            list.Add(new("question.map", LocalTaskSpecs.QuestionMapSystem, LocalTaskSpecs.QuestionMapSchema(keys.Select(k => k.Key)),
                LocalTaskSpecs.QuestionMapPrompt((string)f!["question"]!, (string?)f["options"], keys), f["expect"]!.AsObject(), (string)f["question"]!));
        return list;
    }

    /// <summary>Nota do caso: fração dos campos esperados que vieram iguais. Texto compara sem caixa e sem espaço nas pontas.</summary>
    private static Outcome Grade(Case c, string? content, string? error, double seconds)
    {
        if (error is not null || content is null) return new(c.Task, c.Label, false, 0, seconds, error ?? "vazio");
        JsonObject got;
        try { got = JsonNode.Parse(content)!.AsObject(); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(c.Task, c.Label, false, 0, seconds, "JSON inválido"); }
        var misses = new List<string>();
        foreach (var (field, want) in c.Expect)
        {
            // A tecnologia só importa na pergunta de anos de experiência.
            if (c.Task == "question.map" && field == "technology" && (string?)c.Expect["key"] != "years_experience") continue;
            var have = got[field];
            var same = want is JsonValue wv && have is JsonValue hv && Normalize(wv) == Normalize(hv);
            if (!same) misses.Add($"{field}: {have?.ToJsonString() ?? "nada"} (esperado {want!.ToJsonString()})");
        }
        var graded = c.Task == "question.map" && (string?)c.Expect["key"] != "years_experience" ? c.Expect.Count - 1 : c.Expect.Count;
        return new(c.Task, c.Label, true, 1.0 - (double)misses.Count / graded, seconds, misses.Count == 0 ? null : string.Join("; ", misses));
    }

    private static string Normalize(JsonValue v) => v.GetValueKind() switch
    {
        JsonValueKind.String => v.GetValue<string>().Trim().ToLowerInvariant(),
        JsonValueKind.Number => v.GetValue<double>().ToString(CultureInfo.InvariantCulture),
        _ => v.ToJsonString()
    };

    private static async Task<(string? Content, string? Error)> ChatAsync(HttpClient http, string model, string system, string schema, string prompt)
    {
        var (content, error) = await PostAsync(http, model, system, schema, prompt, think: false);
        // Modelos sem modo de raciocínio recusam o campo think: repete sem ele, como o provedor faz.
        if (error is not null && error.Contains("think", StringComparison.OrdinalIgnoreCase)) (content, error) = await PostAsync(http, model, system, schema, prompt, think: null);
        return (content, error);
    }

    private static async Task<(string? Content, string? Error)> PostAsync(HttpClient http, string model, string system, string schema, string prompt, bool? think)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model, ["stream"] = false, ["format"] = JsonDocument.Parse(schema).RootElement, ["options"] = new { temperature = 0 },
            ["messages"] = new[] { new { role = "system", content = system }, new { role = "user", content = prompt } }
        };
        if (think is { } t) body["think"] = t;
        try
        {
            using var response = await http.PostAsync("/api/chat", new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"));
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var e)) return (null, e.GetString());
            return (doc.RootElement.GetProperty("message").GetProperty("content").GetString(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return (null, ex.Message); }
    }

    private static async Task<(int? Gpu, long Size)> LoadedAsync(HttpClient http, string model)
    {
        using var ps = JsonDocument.Parse(await http.GetStringAsync("/api/ps"));
        foreach (var m in ps.RootElement.GetProperty("models").EnumerateArray())
            if (m.GetProperty("name").GetString() == model && m.GetProperty("size").GetInt64() is > 0 and var size)
                return ((int)Math.Round(100.0 * m.GetProperty("size_vram").GetInt64() / size), size);
        return (null, 0);
    }

    private static async Task UnloadAllAsync(HttpClient http)
    {
        using var ps = JsonDocument.Parse(await http.GetStringAsync("/api/ps"));
        foreach (var m in ps.RootElement.GetProperty("models").EnumerateArray())
        {
            var body = JsonSerializer.Serialize(new { model = m.GetProperty("name").GetString(), keep_alive = 0 });
            using var _ = await http.PostAsync("/api/generate", new StringContent(body, Encoding.UTF8, "application/json"));
        }
        await Task.Delay(TimeSpan.FromSeconds(2));
    }

    /// <summary>VRAM usada agora, em MiB, pelo nvidia-smi.</summary>
    private static int? Vram()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("nvidia-smi", "--query-gpu=memory.used --format=csv,noheader,nounits") { RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return int.TryParse(text.Trim().Split('\n')[0], out var mb) ? mb : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static double Acc(Row r, string task) => r.Outcomes.Where(o => o.Task == task).Select(o => o.Score).DefaultIfEmpty(0).Average();
    private static double Pct(IEnumerable<double> xs, double q) { var s = xs.Order().ToList(); return s.Count == 0 ? 0 : s[(int)Math.Min(s.Count - 1, Math.Floor(q * s.Count))]; }

    private static string Report(List<Row> rows, int total)
    {
        string P(double x) => $"{x * 100:0}%";
        var sb = new StringBuilder();
        sb.AppendLine($"# Bench dos modelos locais ({DateTime.Now:dd/MM/yyyy HH:mm})");
        sb.AppendLine();
        sb.AppendLine($"`jo llm bench`: {total} casos (20 job.extract, 15 email.classify, 15 question.map), temperatura zero, sem raciocínio longo, um modelo por vez na RTX 4060 Ti 8 GB.");
        sb.AppendLine("Acerto é a fração dos campos certos em cada caso; VRAM é o que o modelo somou ao que o desktop já usava.");
        sb.AppendLine();
        sb.AppendLine("| Modelo | JSON válido | job.extract | email.classify | question.map | Geral | p50 | p95 | Carga fria | Na GPU | VRAM |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in rows)
        {
            var secs = r.Outcomes.Select(o => o.Seconds).ToList();
            sb.AppendLine($"| {r.Model} | {r.Outcomes.Count(o => o.Valid)}/{r.Outcomes.Count} | {P(Acc(r, "job.extract"))} | {P(Acc(r, "email.classify"))} | {P(Acc(r, "question.map"))} | " +
                $"{P(r.Outcomes.Average(o => o.Score))} | {Pct(secs, 0.5):0.0} s | {Pct(secs, 0.95):0.0} s | {r.Cold:0} s | {(r.GpuPercent is { } g ? $"{g}%" : "?")} | {(r.VramMb is { } v ? $"{v / 1024.0:0.0} GB" : "?")} |");
        }
        foreach (var r in rows)
        {
            var misses = r.Outcomes.Where(o => o.Miss is not null).ToList();
            if (misses.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"## Erros de {r.Model}");
            sb.AppendLine();
            foreach (var m in misses) sb.AppendLine($"- {m.Task}, \"{m.Label}\": {m.Miss}");
        }
        return sb.ToString();
    }
}
