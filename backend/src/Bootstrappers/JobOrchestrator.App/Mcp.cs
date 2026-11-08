using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// job-orchestrator --mcp: o conector MCP (Model Context Protocol) pela entrada e saída padrão, para usar o app de dentro do Claude Code,
/// do Claude Desktop ou de outro cliente MCP, no plano da pessoa. Conversa com o app que está no ar (127.0.0.1:7410, ou --api) e só lê,
/// menos dois rascunhos que ficam no app para revisão: o currículo e a carta de uma vaga. Nunca envia candidatura nem e-mail, nunca apaga.
/// Na saída padrão só vai JSON-RPC; o resto vai para a saída de erro.
/// </summary>
internal static class Mcp
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly string[] Versions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    public static async Task<int> RunAsync(string[] args)
    {
        var api = (args.SkipWhile(a => a != "--api").Skip(1).FirstOrDefault() ?? Environment.GetEnvironmentVariable("JO_API") ?? "http://127.0.0.1:7410").TrimEnd('/');
        using var http = new HttpClient { BaseAddress = new Uri(api + "/"), Timeout = TimeSpan.FromMinutes(10) };
        var stdout = Console.OpenStandardOutput();
        using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
        while (await input.ReadLineAsync() is { } line)
        {
            if (line.Trim().Length == 0) continue;
            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (JsonException) { await WriteAsync(stdout, Error(null, -32700, "JSON inválido")); continue; }
            var id = msg?["id"]?.DeepClone();
            var method = msg?["method"]?.GetValue<string>() ?? "";
            if (id is null) continue; // notificação (notifications/initialized e afins): não tem resposta
            JsonObject reply;
            try
            {
                reply = method switch
                {
                    "initialize" => Result(id, new JsonObject
                    {
                        ["protocolVersion"] = msg?["params"]?["protocolVersion"]?.GetValue<string>() is { } v && Versions.Contains(v) ? v : Versions[0],
                        ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                        ["serverInfo"] = new JsonObject { ["name"] = "job-orchestrator", ["version"] = JobOrchestrator.App.Launcher.Version },
                        ["instructions"] = "Job Orchestrator, o orquestrador local de busca de vagas da pessoa. Busque e leia vagas, o perfil e as candidaturas; " +
                            "gere o currículo ou a carta de uma vaga como rascunho. Nada aqui envia candidatura nem e-mail. Fatos da pessoa só vêm do perfil: não invente.",
                    }),
                    "ping" => Result(id, new JsonObject()),
                    "tools/list" => Result(id, new JsonObject { ["tools"] = Tools() }),
                    "tools/call" => Result(id, await CallAsync(http, msg?["params"]?["name"]?.GetValue<string>() ?? "", msg?["params"]?["arguments"] as JsonObject ?? [])),
                    _ => Error(id, -32601, $"Método desconhecido: {method}"),
                };
            }
            catch (Exception ex) { reply = Error(id, -32603, ex.Message); }
            await WriteAsync(stdout, reply);
        }
        return 0;
    }

    private static JsonArray Tools() =>
    [
        Tool("buscar_vagas", "Buscar vagas", "As vagas que o app achou, com nota, elegibilidade de quem mora no Brasil e link. Por padrão, a fila (as acima do corte).",
            new() { ["texto"] = Str("palavras no cargo ou na empresa"), ["situacao"] = Enum("fila", "fila", "todas", "abaixo_do_corte", "cortadas", "encerradas"),
                ["so_brasil"] = new JsonObject { ["type"] = "boolean", ["description"] = "só as abertas a quem mora no Brasil (confirmadas ou possíveis)" },
                ["limite"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["description"] = "padrão 20" } }, [], true),
        Tool("ver_vaga", "Ver uma vaga", "A vaga inteira: descrição, por que a nota, elegibilidade com o trecho do anúncio, e o que a pessoa tem e o que falta no perfil.",
            new() { ["id"] = Str("o id da vaga (de buscar_vagas)") }, ["id"], true),
        Tool("perfil", "Perfil da pessoa", "O perfil verificado: título, resumo, experiências com tecnologias, competências, idiomas, formação e certificados. Sem telefone nem e-mail.",
            [], [], true),
        Tool("candidaturas", "Candidaturas", "As candidaturas e a situação de cada uma.", [], [], true),
        Tool("fontes", "Fontes de vagas", "Quantas plataformas o app lê, quantas funcionam e a cobertura de países.", [], [], true),
        Tool("gerar_curriculo", "Gerar o currículo de uma vaga", "Os agentes do app escrevem o currículo da vaga só com fatos do perfil, conferem e geram o PDF (rascunho no app; nada é enviado).",
            new() { ["id"] = Str("o id da vaga") }, ["id"], false),
        Tool("escrever_carta", "Escrever a carta de uma vaga", "A carta de apresentação da vaga, só com fatos do perfil e conferida frase a frase (rascunho; nada é enviado).",
            new() { ["id"] = Str("o id da vaga"), ["tom"] = Enum("direto", "direto", "formal", "caloroso"), ["idioma"] = Enum("", "", "pt", "en", "es") }, ["id"], false),
    ];

    private static async Task<JsonObject> CallAsync(HttpClient http, string name, JsonObject a)
    {
        try
        {
            var text = name switch
            {
                "buscar_vagas" => await JobsAsync(http, a),
                "ver_vaga" => await JobAsync(http, Need(a, "id")),
                "perfil" => await ProfileAsync(http),
                "candidaturas" => await ApplicationsAsync(http),
                "fontes" => await SourcesAsync(http),
                "gerar_curriculo" => await ResumeAsync(http, Need(a, "id")),
                "escrever_carta" => await LetterAsync(http, Need(a, "id"), a["tom"]?.GetValue<string>(), a["idioma"]?.GetValue<string>()),
                _ => throw new ArgumentException($"Ferramenta desconhecida: {name}"),
            };
            return Content(text, false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null) { return Content("O Job Orchestrator não está no ar. Abra o app (ou rode job-orchestrator --serve) e tente de novo.", true); }
        catch (Exception ex) when (ex is HttpRequestException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException) { return Content(ex.Message, true); }
    }

    private static readonly Dictionary<string, string> Status = new() { ["fila"] = "Shortlisted", ["abaixo_do_corte"] = "Scored", ["cortadas"] = "Rejected", ["encerradas"] = "Expired" };

    private static async Task<string> JobsAsync(HttpClient http, JsonObject a)
    {
        var status = Status.GetValueOrDefault(a["situacao"]?.GetValue<string>() ?? "fila", a["situacao"]?.GetValue<string>() == "todas" ? "" : "Shortlisted");
        var q = a["texto"]?.GetValue<string>();
        var rows = await http.GetFromJsonAsync<JsonArray>($"api/jobs?{(status.Length > 0 ? $"status={status}&" : "")}{(string.IsNullOrWhiteSpace(q) ? "" : $"q={Uri.EscapeDataString(q)}")}", Web) ?? [];
        var brazil = a["so_brasil"]?.GetValue<bool>() == true;
        var limit = Math.Clamp(a["limite"]?.GetValue<int>() ?? 20, 1, 100);
        var list = rows.OfType<JsonObject>().Where(j => !brazil || S(j, "eligibility") is "BR_ELIGIBLE_CONFIRMED" or "BR_ELIGIBLE_POSSIBLE").Take(limit).ToList();
        if (list.Count == 0) return "Nenhuma vaga com esse filtro.";
        var sb = new StringBuilder($"{list.Count} vagas (id | empresa | cargo | local | nota | elegibilidade | link):\n");
        foreach (var j in list)
            sb.Append($"- {S(j, "id")} | {S(j, "company")} | {S(j, "title")} | {S(j, "location")} | {S(j, "score")} | {Eligibility(S(j, "eligibility"))} | {S(j, "applyUrl")}\n");
        return sb.ToString();
    }

    private static async Task<string> JobAsync(HttpClient http, string id)
    {
        var d = await http.GetFromJsonAsync<JsonObject>($"api/jobs/{Uri.EscapeDataString(id)}", Web) ?? throw new KeyNotFoundException("Vaga não encontrada.");
        var j = d["job"] as JsonObject ?? [];
        var stack = (j["stack"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList() ?? [];
        var mine = await MineAsync(http);
        var have = stack.Where(t => mine.Any(m => Same(m, t))).ToList();
        var sb = new StringBuilder();
        sb.Append($"{S(j, "title")}, {S(j, "company")} ({S(j, "location")})\nSituação: {S(j, "status")}. Nota: {S(j, "score")}. Modalidade: {S(j, "modality")}. Salário: {Or(S(j, "salaryText"))}.\n");
        sb.Append($"Elegibilidade: {Eligibility(S(j, "eligibility"))} ({S(j, "eligibilityEvidence")}).\nCandidatura: {S(j, "applyUrl")}\n");
        if (stack.Count > 0) sb.Append($"\nA pessoa tem: {Or(string.Join(", ", have))}.\nFalta no perfil (não entra no currículo): {Or(string.Join(", ", stack.Except(have)))}.\n");
        if (d["factors"] is JsonArray factors && factors.Count > 0)
            sb.Append("\nPor que essa nota:\n").Append(string.Join("\n", factors.OfType<JsonObject>().Select(f => $"- {S(f, "name")}: {S(f, "points")} de {S(f, "weight")} ({S(f, "note")})"))).Append('\n');
        var text = S(d, "description");
        sb.Append($"\nDescrição:\n{(text.Length > 4000 ? text[..4000] + "…" : text)}");
        return sb.ToString();
    }

    private static async Task<string> ProfileAsync(HttpClient http)
    {
        var p = await http.GetFromJsonAsync<JsonObject>("api/profile", Web) ?? [];
        var facts = (p["facts"] as JsonArray)?.OfType<JsonObject>().Where(f => S(f, "status") is "Verified" or "Confirmed").ToList() ?? [];
        string Of(string kind) => string.Join("\n", facts.Where(f => S(f, "kind") == kind).Select(f => $"- {S(f, "statement")}"));
        var sb = new StringBuilder($"{S(p["identity"] as JsonObject ?? [], "fullName")}\n");
        foreach (var (kind, title) in new[] { ("headline", "Título"), ("summary", "Resumo") })
            if (Of(kind) is { Length: > 0 } t) sb.Append($"\n{title}:\n{t}\n");
        var jobs = facts.Where(f => S(f, "kind") == "experience").ToList();
        if (jobs.Count > 0)
        {
            sb.Append("\nExperiências:\n");
            foreach (var e in jobs)
            {
                var tech = (e["technologies"] as JsonArray)?.Select(x => x?.GetValue<string>()).Where(x => x is not null).ToList() ?? [];
                sb.Append($"- {S(e, "role")} na {S(e, "organization")} ({Or(S(e, "start"))} a {(S(e, "end") is { Length: > 0 } end ? end : "hoje")}){(tech.Count > 0 ? $"; {string.Join(", ", tech)}" : "")}\n");
                foreach (var w in facts.Where(f => S(f, "kind") == "achievement" && S(f, "parentId") == S(e, "id"))) sb.Append($"  - {S(w, "statement")}\n");
            }
        }
        var skills = string.Join(", ", facts.Where(f => S(f, "kind") == "skill").Select(f => S(f, "statement")));
        if (skills.Length > 0) sb.Append($"\nCompetências: {skills}\n");
        foreach (var (kind, title) in new[] { ("language", "Idiomas"), ("education", "Formação"), ("certification", "Certificados"), ("project", "Projetos") })
            if (Of(kind) is { Length: > 0 } t) sb.Append($"\n{title}:\n{t}\n");
        return sb.ToString();
    }

    private static async Task<string> ApplicationsAsync(HttpClient http)
    {
        var rows = await http.GetFromJsonAsync<JsonArray>("api/applications", Web) ?? [];
        if (rows.Count == 0) return "Nenhuma candidatura ainda.";
        return $"{rows.Count} candidaturas (empresa | cargo | situação | atualizada):\n" + string.Join("\n", rows.OfType<JsonObject>().Take(100)
            .Select(r => $"- {S(r, "company")} | {S(r, "title")} | {S(r, "state")} | {S(r, "updatedAt")}"));
    }

    private static async Task<string> SourcesAsync(HttpClient http)
    {
        var r = await http.GetFromJsonAsync<JsonObject>("api/sources/registry", Web) ?? [];
        var c = await http.GetFromJsonAsync<JsonObject>("api/coverage", Web) ?? [];
        var list = (r["sources"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var t = c["totals"] as JsonObject ?? [];
        return $"{list.Count} plataformas no registro, {list.Count(s => s["connector"]?.GetValue<bool>() == true)} com conector. " +
            $"Países integrados: {S(t, "countriesIntegrated")}; com vaga vista: {S(t, "countriesWithJobs")}; não pesquisados: {S(t, "countriesUnresearched")} de 249. " +
            $"Vagas abertas: {S(t, "openUnique")}, confirmadas para quem mora no Brasil: {S(t, "brazilConfirmed")}.";
    }

    private static async Task<string> ResumeAsync(HttpClient http, string id)
    {
        using var res = await http.PostAsync($"api/materials/preview/{Uri.EscapeDataString(id)}", null);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(Web) ?? [];
        if (body["versionId"]?.GetValue<string>() is { Length: > 0 } version)
            return $"Currículo pronto (rascunho, nada foi enviado). PDF: {http.BaseAddress}api/materials/resumes/{version}/pdf";
        return $"O currículo não ficou pronto: {Or(S(body, "error"))}.";
    }

    private static async Task<string> LetterAsync(HttpClient http, string id, string? tone, string? language)
    {
        using var res = await http.PostAsJsonAsync($"api/assistant/cover/{Uri.EscapeDataString(id)}", new { tone, language = string.IsNullOrEmpty(language) ? null : language }, Web);
        var body = await res.Content.ReadFromJsonAsync<JsonObject>(Web) ?? [];
        if (body["ok"]?.GetValue<bool>() != true) return $"A carta não ficou pronta: {Or(S(body, "error"))}.";
        var removed = (body["removed"] as JsonArray)?.Count ?? 0;
        return $"{S(body, "text")}\n\n(rascunho, nada foi enviado{(removed > 0 ? $"; o Conferente tirou {removed} frase(s) sem fonte no perfil" : "")})";
    }

    /// <summary>O que a pessoa tem: competências e as tecnologias das experiências do perfil.</summary>
    private static async Task<List<string>> MineAsync(HttpClient http)
    {
        var p = await http.GetFromJsonAsync<JsonObject>("api/profile", Web) ?? [];
        return (p["facts"] as JsonArray)?.OfType<JsonObject>().Where(f => S(f, "status") is "Verified" or "Confirmed")
            .SelectMany(f => S(f, "kind") == "skill" ? [S(f, "statement")] : (f["technologies"] as JsonArray)?.Select(x => x?.GetValue<string>() ?? "") ?? [])
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
    }

    private static bool Same(string mine, string tech)
    {
        var a = mine.Trim().ToLowerInvariant();
        var b = tech.Trim().ToLowerInvariant();
        return a == b || a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    private static string Eligibility(string c) => c switch
    {
        "BR_ELIGIBLE_CONFIRMED" => "aberta a quem mora no Brasil", "BR_ELIGIBLE_POSSIBLE" => "remota, a confirmar", "BR_RESTRICTED" => "restrita a outro país",
        "RELOCATION_WITH_SPONSORSHIP" => "no exterior, com visto ou mudança", "RELOCATION_UNVERIFIED" => "no exterior, sem dizer de visto", "REVIEW" => "anúncio contraditório",
        _ => "sem local",
    };

    private static string Need(JsonObject a, string key) => a[key]?.GetValue<string>() is { Length: > 0 } v ? v : throw new ArgumentException($"Falta o argumento {key}.");
    private static string S(JsonObject o, string key) => o[key] is JsonValue v ? v.ToString() : "";
    private static string Or(string s) => s.Length > 0 ? s : "não informado";

    private static JsonObject Tool(string name, string title, string description, JsonObject properties, string[] required, bool readOnly) => new()
    {
        ["name"] = name, ["title"] = title, ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = new JsonArray([.. required.Select(r => (JsonNode)r)]) },
        ["annotations"] = new JsonObject { ["readOnlyHint"] = readOnly, ["destructiveHint"] = false, ["openWorldHint"] = false },
    };

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };
    private static JsonObject Enum(string fallback, params string[] values) =>
        new() { ["type"] = "string", ["enum"] = new JsonArray([.. values.Select(v => (JsonNode)v)]), ["description"] = $"padrão: {(fallback.Length > 0 ? fallback : "o da vaga")}" };

    private static JsonObject Content(string text, bool error) =>
        new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = error };
    private static JsonObject Result(JsonNode id, JsonObject result) => new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static async Task WriteAsync(Stream stdout, JsonObject reply)
    {
        var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString() + "\n");
        await stdout.WriteAsync(bytes);
        await stdout.FlushAsync();
    }
}
