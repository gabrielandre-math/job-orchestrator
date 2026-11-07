using System.Net;
using System.Net.Http.Headers;
using System.Text;
using JobOrchestrator.Intelligence.Data;

namespace JobOrchestrator.Intelligence.Providers;

/// <summary>Conta com chave de API: cobra por uso, então tem teto de tokens por mês e o roteador pula a conta quando passa.</summary>
internal interface IMetered
{
    long MonthlyTokens { get; }
}

/// <summary>Modelos padrão por degrau de cada tipo de conta. Você troca por conta na tela (a lista vem da própria API).</summary>
internal static class ApiDefaults
{
    public static string BaseUrl(string kind) => kind switch
    {
        "anthropic-api" => "https://api.anthropic.com/v1",
        "openai-api" => "https://api.openai.com/v1",
        "gemini-api" => "https://generativelanguage.googleapis.com/v1beta/openai",
        "openrouter-api" => "https://openrouter.ai/api/v1",
        "deepseek-api" => "https://api.deepseek.com/v1",
        "mistral-api" => "https://api.mistral.ai/v1",
        "groq-api" => "https://api.groq.com/openai/v1",
        "xai-api" => "https://api.x.ai/v1",
        _ => ""
    };

    /// <summary>Todos os tipos de conta por chave que falam o formato da OpenAI.</summary>
    public static readonly string[] Compatible = ["openai-api", "gemini-api", "openrouter-api", "deepseek-api", "mistral-api", "groq-api", "xai-api", "openai-compatible"];

    /// <summary>
    /// Escolha automática na lista que a própria chave devolve: o leve é o menor da família (mini, flash, small, haiku),
    /// o principal é o de linha (sonnet, pro, large, chat). Você troca depois, se quiser.
    /// </summary>
    public static Dictionary<string, string> Pick(IReadOnlyList<string> models)
    {
        var usable = models.Where(m => !System.Text.RegularExpressions.Regex.IsMatch(m, "embed|whisper|tts|audio|image|vision-preview|moderation|dall|realtime|transcribe|search", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToList();
        if (usable.Count == 0) return [];
        string First(params string[] hints) => hints.Select(h => usable.FirstOrDefault(m => m.Contains(h, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(x => x is not null) ?? usable[0];
        var light = First("haiku", "flash-lite", "mini", "flash", "small", "lite", "nano", "8b", "chat");
        var main = First("sonnet", "-pro", "large", "gpt-5", "grok-4", "deepseek-chat", "medium", "70b", "chat");
        return new() { ["Light"] = light, ["Medium"] = main, ["Strong"] = main, ["Top"] = main };
    }

    public static string Model(string kind, Rung rung) => (kind, rung) switch
    {
        ("anthropic-api", Rung.Light) => "claude-haiku-4-5-20251001",
        ("anthropic-api", Rung.Top) => "claude-opus-5-5",
        ("anthropic-api", _) => "claude-sonnet-5",
        ("openai-api", Rung.Light) => "gpt-5-mini",
        ("openai-api", _) => "gpt-5",
        ("gemini-api", Rung.Light) => "gemini-2.5-flash",
        ("gemini-api", _) => "gemini-2.5-pro",
        _ => ""
    };

    public static string Family(string kind, string model) => kind switch
    {
        "anthropic-api" => "anthropic",
        "openai-api" => "openai",
        "gemini-api" => "google",
        "deepseek-api" => "deepseek",
        "mistral-api" => "mistral",
        "xai-api" => "xai",
        // OpenRouter e afins: a família vem do nome do modelo (anthropic/claude…, openai/gpt…, google/gemini…).
        _ => model.Split('/')[0].ToLowerInvariant() switch
        {
            var p when p.Contains("anthropic") || p.Contains("claude") => "anthropic",
            var p when p.Contains("openai") || p.Contains("gpt") => "openai",
            var p when p.Contains("google") || p.Contains("gemini") => "google",
            var p => p
        }
    };
}

/// <summary>API da Anthropic (Messages). Resposta no schema por uma ferramenta obrigatória: o modelo só pode responder preenchendo-a.</summary>
internal sealed class AnthropicApi(Account account, IHttpClientFactory http) : IProvider, IMetered
{
    public string Id => account.Id;
    public string Name => account.Name;
    public string Family => "anthropic";
    public long MonthlyTokens => account.MonthlyTokens ?? 0;
    private string Model(Rung r) => account.ModelMap.GetValueOrDefault(r.ToString()) ?? ApiDefaults.Model(account.Kind, r);

    public async Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct) =>
        await ApiProbe.ViewAsync(account, Family, order, lastTest, ct);

    public async Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
    {
        var model = Model(r.Rung);
        var key = await Keyring.GetAsync(account.Id, ct);
        if (key is null) return new LlmResult(false, null, Id, model, "api", "chave não encontrada no keyring", 0, ProviderFamily: Family);
        var body = JsonSerializer.Serialize(new
        {
            model, max_tokens = 8192, system = r.System,
            messages = new[] { new { role = "user", content = r.Prompt } },
            tools = new[] { new { name = "answer", description = "Responda preenchendo exatamente este schema.", input_schema = JsonDocument.Parse(r.JsonSchema).RootElement } },
            tool_choice = new { type = "tool", name = "answer" }
        });
        var clock = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(r.TimeoutSeconds));
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiDefaults.BaseUrl(account.Kind)}/messages") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.Add("x-api-key", key);
            req.Headers.Add("anthropic-version", "2023-06-01");
            using var res = await http.CreateClient("api").SendAsync(req, limit.Token);
            var text = await res.Content.ReadAsStringAsync(limit.Token);
            if (!res.IsSuccessStatusCode) return new LlmResult(false, null, Id, model, "api", ApiProbe.Error(res.StatusCode, text), clock.Elapsed.TotalSeconds, ProviderFamily: Family);
            using var doc = JsonDocument.Parse(text);
            var tool = doc.RootElement.GetProperty("content").EnumerateArray().FirstOrDefault(c => c.GetProperty("type").GetString() == "tool_use");
            var usage = doc.RootElement.TryGetProperty("usage", out var u) ? u.GetProperty("input_tokens").GetInt32() + u.GetProperty("output_tokens").GetInt32() : 0;
            var json = tool.ValueKind == JsonValueKind.Object ? tool.GetProperty("input").GetRawText() : null;
            return new LlmResult(json is not null, json, Id, model, "api", json is null ? "resposta fora do formato" : null, clock.Elapsed.TotalSeconds, Tokens: usage, ProviderFamily: Family);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Whoever asked gave up: not a timeout of this provider.
            ct.ThrowIfCancellationRequested();
            return new LlmResult(false, null, Id, model, "api", limit.IsCancellationRequested ? "tempo esgotado" : "API indisponível", clock.Elapsed.TotalSeconds, ProviderFamily: Family);
        }
    }
}

/// <summary>
/// APIs no formato da OpenAI (chat/completions): OpenAI, Gemini pelo endpoint compatível do Google, OpenRouter, Groq, DeepSeek e afins.
/// Pede json_schema estrito; quem não aceita recebe json_object com o schema no prompt.
/// </summary>
internal sealed class OpenAiCompatible(Account account, IHttpClientFactory http) : IProvider, IMetered
{
    public string Id => account.Id;
    public string Name => account.Name;
    public string Family => ApiDefaults.Family(account.Kind, Model(Rung.Medium));
    public long MonthlyTokens => account.MonthlyTokens ?? 0;
    private string Base => (account.BaseUrl is { Length: > 0 } b ? b : ApiDefaults.BaseUrl(account.Kind)).TrimEnd('/');
    private string Model(Rung r) => account.ModelMap.GetValueOrDefault(r.ToString()) ?? ApiDefaults.Model(account.Kind, r);

    public async Task<ProviderView> ProbeAsync(int order, LlmResult? lastTest, CancellationToken ct) =>
        await ApiProbe.ViewAsync(account, Family, order, lastTest, ct);

    public async Task<LlmResult> RunAsync(LlmRequest r, CancellationToken ct)
    {
        var model = Model(r.Rung);
        var key = await Keyring.GetAsync(account.Id, ct);
        if (key is null) return new LlmResult(false, null, Id, model, "api", "chave não encontrada no keyring", 0, ProviderFamily: Family);
        var clock = Stopwatch.StartNew();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(r.TimeoutSeconds));
        try
        {
            var (ok, json, tokens, error, status) = await ChatAsync(key, model, r, strict: true, limit.Token);
            if (!ok && status == HttpStatusCode.BadRequest && (error ?? "").Contains("response_format", StringComparison.OrdinalIgnoreCase))
                (ok, json, tokens, error, _) = await ChatAsync(key, model, r, strict: false, limit.Token);
            return new LlmResult(ok, json, Id, model, "api", error, clock.Elapsed.TotalSeconds, Tokens: tokens, ProviderFamily: Family);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Whoever asked gave up: not a timeout of this provider.
            ct.ThrowIfCancellationRequested();
            return new LlmResult(false, null, Id, model, "api", limit.IsCancellationRequested ? "tempo esgotado" : "API indisponível", clock.Elapsed.TotalSeconds, ProviderFamily: Family);
        }
    }

    private async Task<(bool Ok, string? Json, int Tokens, string? Error, HttpStatusCode Status)> ChatAsync(string key, string model, LlmRequest r, bool strict, CancellationToken ct)
    {
        object format = strict
            ? new { type = "json_schema", json_schema = new { name = "answer", strict = true, schema = JsonDocument.Parse(r.JsonSchema).RootElement } }
            : new { type = "json_object" };
        var system = strict ? r.System : $"{r.System}\n\nResponda só com JSON neste schema: {r.JsonSchema}";
        var body = JsonSerializer.Serialize(new { model, messages = new[] { new { role = "system", content = system }, new { role = "user", content = r.Prompt } }, response_format = format });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}/chat/completions") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        using var res = await http.CreateClient("api").SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode) return (false, null, 0, ApiProbe.Error(res.StatusCode, text), res.StatusCode);
        using var doc = JsonDocument.Parse(text);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        var tokens = doc.RootElement.TryGetProperty("usage", out var u) && u.TryGetProperty("total_tokens", out var t) ? t.GetInt32() : 0;
        var json = CliRunner.JsonOf(content);
        return (json is not null, json, tokens, json is null ? "resposta fora do formato" : null, res.StatusCode);
    }
}

/// <summary>Status comum das contas por chave de API e a lista de modelos que a própria API oferece.</summary>
internal static class ApiProbe
{
    public static async Task<ProviderView> ViewAsync(Account a, string family, int order, LlmResult? lastTest, CancellationToken ct)
    {
        var key = await Keyring.GetAsync(a.Id, ct);
        var cap = a.MonthlyTokens is { } m ? $"Teto de {m:N0} tokens por mês." : "Sem teto: a conta fica parada até você definir um.";
        if (key is null) return new(a.Id, a.Name, family, order, true, null, "sem chave", "cobrança por uso", [], false, "no-key", "A chave não está no keyring. Conecte de novo.", lastTest);
        if (a.MonthlyTokens is null or <= 0) return new(a.Id, a.Name, family, order, true, null, "chave de API", "cobrança por uso", [], false, "blocked", cap, lastTest);
        return new(a.Id, a.Name, family, order, true, null, "chave de API", "cobrança por uso", [], a.Enabled, a.Enabled ? "ready" : "off", $"Pronto. {cap}", lastTest);
    }

    public static async Task<IReadOnlyList<string>> ModelsAsync(Account a, IHttpClientFactory http, CancellationToken ct)
    {
        var key = await Keyring.GetAsync(a.Id, ct);
        if (key is null) return [];
        var url = a.Kind == "anthropic-api" ? "https://api.anthropic.com/v1/models" : $"{(a.BaseUrl is { Length: > 0 } b ? b : ApiDefaults.BaseUrl(a.Kind)).TrimEnd('/')}/models";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (a.Kind == "anthropic-api") { req.Headers.Add("x-api-key", key); req.Headers.Add("anthropic-version", "2023-06-01"); }
        else req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        try
        {
            using var res = await http.CreateClient("api").SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return [];
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return [.. doc.RootElement.GetProperty("data").EnumerateArray().Select(m => (m.GetProperty("id").GetString() ?? "").Replace("models/", "")).Where(x => x.Length > 0).Order()];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException) { return []; }
    }

    public static string Error(HttpStatusCode status, string body) => status switch
    {
        HttpStatusCode.TooManyRequests => "quota",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "chave recusada pelo provedor",
        HttpStatusCode.PaymentRequired => "sem crédito na conta do provedor",
        _ => ClaudeCli.Trim($"{(int)status}: {body}")
    };
}
