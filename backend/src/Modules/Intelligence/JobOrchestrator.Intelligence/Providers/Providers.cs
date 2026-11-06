namespace JobOrchestrator.Intelligence.Providers;

/// <summary>O que um provedor sabe dizer de si e como roda um pedido no degrau pedido.</summary>
internal interface IProvider
{
    string Id { get; }
    string Name { get; }
    string Family { get; }
    Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct);
    Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct);
}

/// <summary>
/// Claude Code pela assinatura. Uma instância por conta: a padrão (~/.claude, hoje o Max) e a Pro numa pasta própria
/// (CLAUDE_CONFIG_DIR). Nunca --bare: nesse modo o CLI aceita só API key.
/// </summary>
internal sealed class ClaudeCli(string id, string name, string? configDir, IConfiguration config) : IProvider
{
    public string Id => id;
    public string Name => name;
    public string Family => "anthropic";
    private Dictionary<string, string>? Env => configDir is null ? null : new() { ["CLAUDE_CONFIG_DIR"] = configDir };

    public async Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct)
    {
        var bin = CliRunner.Find("claude");
        if (bin is null) return View(order, false, null, "CLI não instalado", null, [], false, "missing", "Instale o Claude Code.", lastTest);
        var version = (await CliRunner.RunAsync(bin, ["--version"], null, TimeSpan.FromSeconds(10), Env, ct)).Stdout.Trim();
        var auth = await CliRunner.RunAsync(bin, ["auth", "status"], null, TimeSpan.FromSeconds(15), Env, ct);
        string login = "sem login"; string? plan = null; var apiKey = false;
        try
        {
            using var doc = JsonDocument.Parse(auth.Stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("loggedIn", out var l) && l.GetBoolean()) login = "assinatura";
            if (root.TryGetProperty("subscriptionType", out var s)) plan = s.GetString();
            // "none" é só sem login; cobrança por API só quando o método logado é chave de API.
            apiKey = login == "assinatura" ? false : root.TryGetProperty("authMethod", out var m) && (m.GetString() ?? "").Contains("api", StringComparison.OrdinalIgnoreCase);
            if (login != "assinatura" && root.TryGetProperty("authMethod", out var m2) && (m2.GetString() ?? "").Contains("api", StringComparison.OrdinalIgnoreCase)) apiKey = true;
        }
        catch (JsonException) { }
        if (apiKey) return View(order, true, version, "API key", plan, [], false, "blocked", "Login por API key detectado: bloqueado para não gerar cobrança.", lastTest);
        var usage = configDir is null ? [.. StatuslineUsage().Select(u => Reserves.Fresh(u)!)] : new List<UsageWindow>();
        var reserve = Reserves.Check(id, usage, config);
        var available = login == "assinatura" && reserve is null;
        var status = login != "assinatura" ? "no-login" : reserve is not null ? "reserve" : "ready";
        var detail = login != "assinatura" ? $"Conecte rodando: CLAUDE_CONFIG_DIR={configDir} claude auth login" : reserve ?? "Pronto para uso.";
        return View(order, true, version, login, plan, usage, available, status, detail, lastTest);
    }

    public async Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
    {
        var (model, effort) = r.Rung switch
        {
            Rung.Local or Rung.LocalStrong or Rung.Light => ("haiku", "low"),
            Rung.Medium => ("sonnet", "medium"),
            Rung.Strong => ("sonnet", "high"),
            _ => ("opus", "high")
        };
        var clock = Stopwatch.StartNew();
        var bin = CliRunner.Find("claude") ?? "claude";
        var output = await CliRunner.RunAsync(bin,
            ["-p", "--output-format", "json", "--json-schema", r.JsonSchema, "--model", model, "--effort", effort, "--tools", "",
             "--strict-mcp-config", "--no-session-persistence", "--system-prompt", r.System],
            r.Prompt, TimeSpan.FromSeconds(r.TimeoutSeconds), Env, ct);
        var seconds = clock.Elapsed.TotalSeconds;
        if (output.TimedOut) return new LlmResult(false, null, id, model, effort, "tempo esgotado", seconds);
        try
        {
            using var doc = JsonDocument.Parse(output.Stdout);
            var root = doc.RootElement;
            var error = root.TryGetProperty("is_error", out var e) && e.GetBoolean();
            var text = root.TryGetProperty("result", out var res) ? res.GetString() ?? "" : "";
            if (error) return new LlmResult(false, null, id, model, effort, CliRunner.LooksLikeQuota(text) ? "quota" : text, seconds);
            var json = root.TryGetProperty("structured_output", out var so) && so.ValueKind == JsonValueKind.Object ? so.GetRawText() : CliRunner.JsonOf(text);
            return new LlmResult(json is not null, json, id, model, effort, json is null ? "resposta fora do formato" : null, seconds);
        }
        catch (JsonException)
        {
            var all = output.Stdout + output.Stderr;
            return new LlmResult(false, null, id, model, effort, CliRunner.LooksLikeQuota(all) ? "quota" : Trim(all), seconds);
        }
    }

    /// <summary>Uso real gravado pela statusline do usuário em ~/.claude/state/statusline.json.</summary>
    private static IReadOnlyList<UsageWindow> StatuslineUsage()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "state", "statusline.json");
        if (!File.Exists(path)) return [];
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("rate_limits", out var rl)) return [];
            return [Window(rl, "five_hour", "5 h"), Window(rl, "seven_day", "Semana")];
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private static UsageWindow Window(JsonElement rl, string key, string name)
    {
        if (!rl.TryGetProperty(key, out var w)) return new UsageWindow(name, null, null);
        int? used = w.TryGetProperty("used_percentage", out var u) && u.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
        DateTime? reset = w.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
        return new UsageWindow(name, used, reset);
    }

    private ProviderView View(int order, bool installed, string? version, string login, string? plan, IReadOnlyList<UsageWindow> usage, bool available, string status, string detail, LlmResult? last) =>
        new(id, name, Family, order, installed, version, login, plan, usage, available, status, detail, last);

    internal static string Trim(string s) => s.Length > 300 ? s[..300] : s.Trim();
}

/// <summary>
/// Reserva para o seu uso: o orquestrador para de usar uma conta quando a janela passa do limite.
/// Padrão 60% na de 5 h e 75% na semanal; o Max, que é a conta do seu trabalho, só com folga (30% e 50%).
/// </summary>
internal static class Reserves
{
    public static string? Check(string id, IReadOnlyList<UsageWindow> usage, IConfiguration config)
    {
        var five = config.GetValue($"Llm:Reserve:{id}:FiveHour", id == "claude-max" ? 30 : 60);
        var week = config.GetValue($"Llm:Reserve:{id}:Week", id == "claude-max" ? 50 : 75);
        var h = Fresh(usage.FirstOrDefault(u => u.Name == "5 h"));
        var w = Fresh(usage.FirstOrDefault(u => u.Name == "Semana"));
        if (h?.UsedPercent >= five) return $"Reserva: janela de 5 h em {h.UsedPercent}% (limite {five}%){Until(h)}.";
        if (w?.UsedPercent >= week) return $"Reserva: semana em {w.UsedPercent}% (limite {week}%){Until(w)}.";
        return null;
    }

    /// <summary>Janela cujo reset já passou não diz nada sobre agora.</summary>
    public static UsageWindow? Fresh(UsageWindow? w) => w is { ResetsAt: { } r } && r < DateTime.UtcNow ? w with { UsedPercent = null } : w;

    private static string Until(UsageWindow w) => w.ResetsAt is { } r ? $", volta às {r.ToLocalTime():HH:mm} de {r.ToLocalTime():dd/MM}" : "";
}

/// <summary>
/// Codex pela assinatura do ChatGPT. Uma instância por conta: a padrão (~/.codex) e as outras com CODEX_HOME próprio.
/// Esforço sempre explícito: o padrão do seu config.toml é xhigh.
/// </summary>
internal sealed class CodexCli(string id, string name, string? home, IConfiguration config) : IProvider
{
    public string Id => id;
    public string Name => name;
    public string Family => "openai";
    private Dictionary<string, string>? Env => home is null ? null : new() { ["CODEX_HOME"] = home };
    private string Home => home ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

    public async Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct)
    {
        var bin = CliRunner.Find("codex");
        if (bin is null) return new(Id, Name, Family, order, false, null, "CLI não instalado", null, [], false, "missing", "Instale o Codex CLI.", lastTest);
        var version = (await CliRunner.RunAsync(bin, ["--version"], null, TimeSpan.FromSeconds(10), Env, ct)).Stdout.Trim();
        var status = await CliRunner.RunAsync(bin, ["login", "status"], null, TimeSpan.FromSeconds(15), Env, ct);
        var text = (status.Stdout + status.Stderr).Trim();
        var chatgpt = text.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase);
        var apiKey = text.Contains("API key", StringComparison.OrdinalIgnoreCase);
        var (raw, plan) = RolloutUsage(Home);
        IReadOnlyList<UsageWindow> usage = [.. raw.Select(u => Reserves.Fresh(u)!)];
        if (apiKey) return new(Id, Name, Family, order, true, version, "API key", plan, usage, false, "blocked", "Login por API key detectado: bloqueado para não gerar cobrança.", lastTest);
        var reserve = Reserves.Check(Id, usage, config);
        var available = chatgpt && reserve is null;
        return new(Id, Name, Family, order, true, version, chatgpt ? "assinatura" : "sem login", plan, usage, available,
            !chatgpt ? "no-login" : reserve is not null ? "reserve" : "ready", !chatgpt ? "Conecte rodando: codex login" : reserve ?? "Pronto para uso.", lastTest);
    }

    public async Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
    {
        var effort = r.Rung switch { Rung.Local or Rung.LocalStrong or Rung.Light => "low", Rung.Medium => "medium", Rung.Strong => "high", _ => "xhigh" };
        var model = config[$"Llm:Codex:Models:{r.Rung}"];
        var dir = Path.Combine(CliRunner.Sandbox, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var schema = Path.Combine(dir, "schema.json");
            var output = Path.Combine(dir, "out.json");
            await File.WriteAllTextAsync(schema, r.JsonSchema, ct);
            List<string> args = ["exec", "-c", $"model_reasoning_effort=\"{effort}\"", "-c", "mcp_servers={}", "--sandbox", "read-only", "--skip-git-repo-check", "--ephemeral", "--output-schema", schema, "-o", output];
            if (model is { Length: > 0 }) args.InsertRange(1, ["-m", model]);
            args.Add("-");
            var clock = Stopwatch.StartNew();
            var run = await CliRunner.RunAsync(CliRunner.Find("codex") ?? "codex", args, $"{r.System}\n\n{r.Prompt}", TimeSpan.FromSeconds(r.TimeoutSeconds), Env, ct);
            var seconds = clock.Elapsed.TotalSeconds;
            var used = model ?? "padrão do CLI";
            if (run.TimedOut) return new LlmResult(false, null, Id, used, effort, "tempo esgotado", seconds);
            var json = File.Exists(output) ? CliRunner.JsonOf(await File.ReadAllTextAsync(output, ct)) : null;
            if (json is not null) return new LlmResult(true, json, Id, used, effort, null, seconds);
            var all = run.Stdout + run.Stderr;
            return new LlmResult(false, null, Id, used, effort, CliRunner.LooksLikeQuota(all) ? "quota" : Failure(all, $"{r.System}\n\n{r.Prompt}"), seconds);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// O motivo da falha sem o que o codex exec ecoa: o cabeçalho (versão, workdir, modelo, sandbox, sessão) e o prompt inteiro, que é
    /// interno e não pode aparecer na tela. Fica a linha de erro do CLI; sem ela, as últimas linhas que sobraram.
    /// </summary>
    internal static string Failure(string output, string prompt)
    {
        var text = output.Replace(prompt, "", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal);
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        // Cabeçalho: da linha "OpenAI Codex v..." até o segundo separador "--------", e a linha "user" que abre o eco do prompt.
        var start = lines.FindIndex(l => l.StartsWith("OpenAI Codex", StringComparison.Ordinal));
        if (start >= 0)
        {
            var close = lines.FindIndex(start + 1, l => l.StartsWith("--------", StringComparison.Ordinal));
            close = close < 0 ? -1 : lines.FindIndex(close + 1, l => l.StartsWith("--------", StringComparison.Ordinal));
            lines.RemoveRange(start, (close < 0 ? lines.Count - 1 : close) - start + 1);
        }
        lines = [.. lines.Where(l => l.Length > 0 && l is not "user" and not "codex" and not "thinking")];
        var errors = lines.Where(l => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || l.StartsWith("error:", StringComparison.OrdinalIgnoreCase) || l.Contains("stream error", StringComparison.OrdinalIgnoreCase)).ToList();
        var picked = errors.Count > 0 ? errors.TakeLast(2) : lines.TakeLast(2);
        var reason = string.Join(" ", picked);
        return reason.Length == 0 ? "O Codex terminou sem resposta e sem mensagem de erro." : ClaudeCli.Trim(reason);
    }

    /// <summary>Uso real nos eventos rate_limits do arquivo de sessão mais recente do Codex (só leitura).</summary>
    private static (IReadOnlyList<UsageWindow>, string?) RolloutUsage(string home)
    {
        var root = Path.Combine(home, "sessions");
        if (!Directory.Exists(root)) return ([], null);
        var latest = new DirectoryInfo(root).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
        if (latest is null) return ([], null);
        string? plan = null;
        UsageWindow? five = null, week = null;
        foreach (var line in File.ReadLines(latest.FullName).Reverse().Take(400))
        {
            if (!line.Contains("\"rate_limits\"", StringComparison.Ordinal)) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var rl = Find(doc.RootElement, "rate_limits");
                if (rl is not { } r) continue;
                plan ??= r.TryGetProperty("plan_type", out var p) ? p.GetString() : null;
                five ??= Limit(r, "primary", "5 h");
                week ??= Limit(r, "secondary", "Semana");
                if (five is not null && week is not null) break;
            }
            catch (JsonException) { }
        }
        return ([five ?? new UsageWindow("5 h", null, null), week ?? new UsageWindow("Semana", null, null)], plan);
    }

    private static UsageWindow? Limit(JsonElement rl, string key, string name)
    {
        if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) return null;
        int? used = w.TryGetProperty("used_percent", out var u) && u.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
        DateTime? reset = w.TryGetProperty("resets_at", out var r) && r.TryGetInt64(out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : null;
        return used is null ? null : new UsageWindow(name, used, reset);
    }

    private static JsonElement? Find(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name == name) return p.Value;
                if (Find(p.Value, name) is { } inner) return inner;
            }
        return null;
    }
}

/// <summary>
/// Ollama local na RTX, com duas vagas: o modelo rápido (degrau Local) e o forte (LocalStrong), trocados pela configuração
/// Llm:Ollama:Models. Um modelo por vez na VRAM; a troca custa alguns segundos de carga.
/// </summary>
internal sealed class OllamaLocal(IHttpClientFactory http, IConfiguration config) : IProvider
{
    public string Id => "ollama";
    public string Name => "Llama local (Ollama)";
    public string Family => "local";
    private string Url => config["Llm:Ollama:Url"] ?? "http://127.0.0.1:11434";

    public string ModelFor(Rung rung) =>
        rung == Rung.LocalStrong && config["Llm:Ollama:Models:LocalStrong"] is { Length: > 0 } strong ? strong : config["Llm:Ollama:Models:Local"] ?? "gemma4:e4b";

    public async Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct)
    {
        var installed = CliRunner.Find("ollama") is not null;
        try
        {
            var client = http.CreateClient("ollama");
            using var version = JsonDocument.Parse(await client.GetStringAsync($"{Url}/api/version", ct));
            var models = await TagsAsync(ct);
            var wanted = new[] { ModelFor(Rung.Local), ModelFor(Rung.LocalStrong) }.Distinct().ToList();
            var missing = wanted.Where(m => !models.Any(x => x.Name == m)).ToList();
            var detail = missing.Count > 0 ? $"Serviço no ar, mas falta baixar: {string.Join(", ", missing)}." : $"Rápido: {wanted[0]}. Forte: {wanted[^1]}.";
            return new(Id, Name, Family, order, true, version.RootElement.GetProperty("version").GetString(), "local", null, [], missing.Count < wanted.Count, missing.Count > 0 ? "partial" : "ready", detail, lastTest);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new(Id, Name, Family, order, installed, null, "local", null, [], false, installed ? "stopped" : "missing",
                installed ? "Instalado, mas o serviço não está no ar." : "Não instalado.", lastTest);
        }
    }

    /// <summary>Modelos baixados (/api/tags) e os que estão na VRAM agora (/api/ps).</summary>
    public async Task<IReadOnlyList<LocalModelView>> TagsAsync(CancellationToken ct)
    {
        var client = http.CreateClient("ollama");
        using var tags = JsonDocument.Parse(await client.GetStringAsync($"{Url}/api/tags", ct));
        using var ps = JsonDocument.Parse(await client.GetStringAsync($"{Url}/api/ps", ct));
        var loaded = ps.RootElement.GetProperty("models").EnumerateArray()
            .ToDictionary(m => m.GetProperty("name").GetString()!, m => m.GetProperty("size").GetInt64() is > 0 and var size ? (int)Math.Round(100.0 * m.GetProperty("size_vram").GetInt64() / size) : 0);
        string? Slot(string name) => name == ModelFor(Rung.Local) ? (name == ModelFor(Rung.LocalStrong) ? "rápido e forte" : "rápido") : name == ModelFor(Rung.LocalStrong) ? "forte" : null;
        return [.. tags.RootElement.GetProperty("models").EnumerateArray().Select(m =>
        {
            var name = m.GetProperty("name").GetString()!;
            return new LocalModelView(name, Math.Round(m.GetProperty("size").GetInt64() / 1e9, 1), Slot(name), loaded.ContainsKey(name), loaded.TryGetValue(name, out var gpu) ? gpu : null);
        }).OrderBy(m => m.Slot is null).ThenBy(m => m.Name)];
    }

    public async Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
    {
        var model = ModelFor(r.Rung);
        var clock = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(r.TimeoutSeconds));
        try
        {
            var (content, error) = await ChatAsync(model, r, think: false, limit.Token);
            // Modelos sem modo de raciocínio recusam o campo think: repete sem ele.
            if (error is not null && error.Contains("think", StringComparison.OrdinalIgnoreCase)) (content, error) = await ChatAsync(model, r, think: null, limit.Token);
            if (error is not null) return new LlmResult(false, null, Id, model, "local", ClaudeCli.Trim(error), clock.Elapsed.TotalSeconds);
            var json = CliRunner.JsonOf(content ?? "");
            return new LlmResult(json is not null, json, Id, model, "local", json is null ? "resposta fora do formato" : null, clock.Elapsed.TotalSeconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return new LlmResult(false, null, Id, model, "local", limit.IsCancellationRequested ? "tempo esgotado" : "Ollama indisponível", clock.Elapsed.TotalSeconds);
        }
    }

    /// <summary>Uma chamada a /api/chat com o schema como formato e temperatura zero (extração, não criatividade).</summary>
    private async Task<(string? Content, string? Error)> ChatAsync(string model, LlmRequest r, bool? think, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = model, ["stream"] = false, ["format"] = JsonDocument.Parse(r.JsonSchema).RootElement,
            ["options"] = new { temperature = 0 },
            ["messages"] = new[] { new { role = "system", content = r.System }, new { role = "user", content = r.Prompt } },
            // Quanto o modelo fica carregado depois da resposta (documentação do Ollama): a conversa do Assistente segura 10 minutos,
            // as tarefas de fundo (Explorador, e-mail, páginas) soltam a placa de vídeo em 2 minutos parados.
            ["keep_alive"] = r.Task.StartsWith("assistant.", StringComparison.Ordinal) ? "10m" : "2m",
        };
        if (think is { } t) body["think"] = t;
        using var response = await http.CreateClient("ollama").PostAsync($"{Url}/api/chat", new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"), ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("error", out var e)) return (null, e.GetString() ?? "erro do Ollama");
        return (doc.RootElement.GetProperty("message").GetProperty("content").GetString(), null);
    }
}
