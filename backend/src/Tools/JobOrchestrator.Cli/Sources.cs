using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// jo sources probe: sonda de verdade cada plataforma do registro pela API do app (amostra da campanha ou pública), grava o resultado e escreve
/// data/source-registry.json (o registro tipado, com o estado e a prova) e docs/SOURCE_AUDIT.md (a auditoria legível, com data e código HTTP).
/// </summary>
internal static class Sources
{
    public static async Task<int> ProbeAsync(string api, string jsonOut, string mdOut)
    {
        using var http = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(15) };
        var campaigns = await http.GetFromJsonAsync<JsonArray>("api/campaigns") ?? [];
        var samples = campaigns.FirstOrDefault()?["sources"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray() ?? [];
        Console.WriteLine($"sondando o registro com {samples.Length} fontes da campanha como amostra...");
        var probe = await http.PostAsJsonAsync("api/sources/registry/probe", new { samples });
        probe.EnsureSuccessStatusCode();
        var registry = await http.GetFromJsonAsync<JsonObject>("api/sources/registry") ?? throw new InvalidOperationException("registro vazio");
        var at = DateTime.UtcNow;
        var experimental = registry["experimentalOn"]?.GetValue<bool>() ?? true;
        var list = registry["sources"]!.AsArray().Select(x => x!.AsObject()).ToList();

        bool Enabled(JsonObject s) => s["gate"] is null && s["lifecycle"]!.GetValue<string>() is "ACTIVE" or "EXPERIMENTAL" && (experimental || s["lifecycle"]!.GetValue<string>() != "EXPERIMENTAL");
        var output = new JsonObject
        {
            ["generated_at"] = at.ToString("O"),
            ["generated_by"] = "jo sources probe",
            ["experimental_on"] = experimental,
            ["catalog"] = registry["catalog"]!.DeepClone(),
            ["sources"] = new JsonArray([.. list.Select(s => (JsonNode)new JsonObject
            {
                ["id"] = s["prefix"]!.DeepClone(), ["name"] = s["name"]!.DeepClone(), ["category"] = s["kind"]!.DeepClone(),
                ["integration_method"] = s["access"]!.DeepClone(), ["state"] = s["lifecycle"]!.DeepClone(), ["country_codes"] = s["countries"]!.DeepClone(),
                ["api_doc_url"] = s["docsUrl"]!.DeepClone(), ["terms_url"] = s["termsUrl"]?.DeepClone(), ["attribution"] = s["attribution"]?.DeepClone(),
                ["update_frequency"] = s["refresh"]!.DeepClone(), ["catalog_ids"] = s["catalogIds"]!.DeepClone(), ["notes"] = s["notes"]!.DeepClone(),
                ["authorization_kind"] = s["credential"] is null ? "none" : "user_key", ["credential_present"] = s["credentialPresent"]!.DeepClone(),
                ["connector"] = s["connector"]!.DeepClone(), ["ingestion_enabled"] = Enabled(s) && s["connector"]!.GetValue<bool>(),
                ["gate"] = s["gate"]?.DeepClone(), ["disabled"] = s["disabled"]!.DeepClone(), ["probe"] = s["probe"]?.DeepClone(),
            })]),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(jsonOut))!);
        await File.WriteAllTextAsync(jsonOut, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n");

        var probed = list.Where(s => s["probe"] is JsonObject p && p["source"] is not null).ToList();
        var working = probed.Count(s => s["probe"]!["ok"]!.GetValue<bool>() && s["probe"]!["count"]!.GetValue<int>() > 0);
        var md = new StringBuilder();
        md.AppendLine("# Auditoria das fontes").AppendLine();
        md.AppendLine($"Gerado por `jo sources probe` em {at:dd/MM/yyyy HH:mm} UTC, contra a API local ({api}). Cada plataforma com permissão foi chamada uma vez pela porta oficial,")
          .AppendLine("com a amostra da campanha ou uma amostra pública; bloqueadas e só com parceria nunca são chamadas. Os números abaixo são desta execução, sem estimativa.").AppendLine();
        md.AppendLine("| | Quantas |").AppendLine("|---|---|");
        md.AppendLine($"| Plataformas no registro | {list.Count} |");
        md.AppendLine($"| Com conector no app | {list.Count(s => s["connector"]!.GetValue<bool>())} |");
        md.AppendLine($"| Autorizadas agora (portão aberto) | {list.Count(s => s["gate"] is null && s["lifecycle"]!.GetValue<string>() is "ACTIVE" or "EXPERIMENTAL")} |");
        md.AppendLine($"| Sondadas | {probed.Count} |");
        md.AppendLine($"| Funcionando (responderam com vaga) | {working} |");
        md.AppendLine($"| Esperando credencial da pessoa | {list.Count(s => s["lifecycle"]!.GetValue<string>() == "CREDENTIALS_REQUIRED")} |");
        md.AppendLine($"| Bloqueadas por política ou só com parceria | {list.Count(s => s["lifecycle"]!.GetValue<string>() is "BLOCKED" or "PARTNER_ONLY")} |").AppendLine();
        foreach (var group in list.GroupBy(s => s["lifecycle"]!.GetValue<string>()).OrderBy(g => Array.IndexOf(new[] { "ACTIVE", "EXPERIMENTAL", "CREDENTIALS_REQUIRED", "DISCOVERY_ONLY", "PARTNER_ONLY", "BLOCKED" }, g.Key)))
        {
            md.AppendLine($"## {group.Key}").AppendLine();
            md.AppendLine("| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |").AppendLine("|---|---|---|---|---|---|---|");
            foreach (var s in group)
            {
                var p = s["probe"] as JsonObject;
                var result = p is null ? "não sondada" : p["source"] is null ? Text(p["error"]) : $"`{Text(p["source"])}` HTTP {Text(p["http"], "-")}, {Text(p["count"])} vagas, {Text(p["seconds"])} s{(p["ok"]!.GetValue<bool>() ? "" : $", {Text(p["error"])}")}";
                md.AppendLine($"| {Text(s["name"])} (`{Text(s["prefix"])}`) | {Text(s["access"])} | {string.Join(", ", s["countries"]!.AsArray().Select(c => Text(c)))} | {result.Replace("|", "/")} | {Link(s["docsUrl"])} | {Link(s["termsUrl"])} | {Text(s["notes"]).Replace("|", "/")} |");
            }
            md.AppendLine();
        }
        var cat = registry["catalog"]!;
        md.AppendLine("## Catálogo V2 do usuário").AppendLine();
        md.AppendLine($"`data/catalog/catalogo-fontes-v2-2026.json` ({Text(cat["name"])}, compilado em {Text(cat["compiledOn"])}): {Text(cat["records"])} registros, dos quais {Text(cat["matchedToConnectors"])} ligados a perfis do registro.")
          .AppendLine("Registro do catálogo não é fonte funcionando: os que não têm perfil aqui seguem como pista ou pendência em docs/NEXT_SOURCES.md.").AppendLine();
        md.AppendLine("| Método | Registros |").AppendLine("|---|---|");
        foreach (var (k, v) in cat["byMethod"]!.AsObject().OrderByDescending(x => x.Value!.GetValue<int>())) md.AppendLine($"| {k} | {v} |");
        await File.WriteAllTextAsync(mdOut, md.ToString());
        Console.WriteLine($"{list.Count} plataformas, {probed.Count} sondadas, {working} funcionando. Escrito: {jsonOut} e {mdOut}");
        return 0;
    }

    private static string Text(JsonNode? n, string empty = "") => n is null ? empty : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString();
    private static string Link(JsonNode? n) => n is null ? "-" : $"[link]({n.GetValue<string>()})";
}
