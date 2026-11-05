using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

/// <summary>jo sources coverage: lê /api/coverage e escreve docs/COVERAGE_GAPS.md, por continente e país, com o que existe e o que falta, sem métrica inventada.</summary>
internal static class CoverageReport
{
    private static readonly Dictionary<string, string> Status = new()
    {
        ["INTEGRATED"] = "integrado", ["CONNECTOR_PENDING"] = "conector pendente", ["JOBS_SEEN"] = "vagas vistas por fonte global",
        ["SOME_SEEDS_NO_LIVE_TEST"] = "pistas sem teste ao vivo", ["UNRESEARCHED"] = "não pesquisado",
    };

    public static async Task<int> WriteAsync(string api, string mdOut)
    {
        using var http = new HttpClient { BaseAddress = new Uri(api.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        var c = await http.GetFromJsonAsync<JsonObject>("api/coverage") ?? throw new InvalidOperationException("cobertura vazia");
        var t = c["totals"]!;
        int I(JsonNode? n) => n?.GetValue<int>() ?? 0;
        string S(JsonNode? n) => n?.GetValue<string>() ?? "";
        var md = new StringBuilder();
        md.AppendLine("# Lacunas de cobertura").AppendLine();
        md.AppendLine($"Gerado por `jo sources coverage` em {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC, contra a API local. O universo é a auditoria dos 249 países e territórios ISO 3166-1")
          .AppendLine("(`data/catalog/auditoria-249-paises-v2.csv`, do usuário). O estado de cada país sai só do que existe: conector do país sondado com vaga, fonte do país")
          .AppendLine("no registro ainda sem conector, vaga aberta vista com local nele, ou o estado da auditoria. **Isto não é cobertura mundial completa.**").AppendLine();
        md.AppendLine("## Números desta execução").AppendLine();
        md.AppendLine("| | Quantas |").AppendLine("|---|---|");
        foreach (var (label, key) in new[]
        {
            ("Fontes configuradas (plataformas no registro)", "configured"), ("Com conector no app", "withConnector"), ("Autorizadas (portão aberto)", "authorized"),
            ("Funcionando (última sondagem com vaga)", "working"), ("Quadros de empresa na campanha", "companyBoards"), ("Empresas com vaga aberta", "companiesWithOpenJobs"),
            ("Vagas únicas abertas", "openUnique"), ("Vagas abertas com data da fonte", "openWithDate"), ("Vagas confirmadas para quem mora no Brasil", "brazilConfirmed"),
            ("Vagas possíveis para quem mora no Brasil (remotas sem restrição clara)", "brazilPossible"), ("Vagas restritas (fora do alcance de quem mora no Brasil)", "brazilRestricted"),
            ("Vagas que se contradizem (revisão)", "review"), ("Vagas sem local nenhum", "unknown"), ("Países integrados", "countriesIntegrated"),
            ("Países com vaga aberta vista", "countriesWithJobs"), ("Países não pesquisados", "countriesUnresearched"), ("Registros no catálogo V2 (referência, não fonte)", "catalogRecords"),
        })
            md.AppendLine($"| {label} | {I(t[key])} |");
        md.AppendLine($"| Vagas abertas sem país no local (remotas, mundo todo) | {I(c["unmatchedOpen"])} |").AppendLine();
        md.AppendLine($"Fontes globais ou regionais ativas (cobrem qualquer país, mas só onde as empresas anunciam): {string.Join(", ", c["globalPlatforms"]!.AsArray().Select(x => $"`{S(x)}`"))}.").AppendLine();

        md.AppendLine("## Por continente").AppendLine();
        md.AppendLine("| Continente | Países | Integrados | Conector pendente | Vagas vistas | Pistas sem teste | Não pesquisados | Vagas abertas |").AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var k in c["continents"]!.AsArray()) md.AppendLine($"| {S(k!["name"])} | {I(k["countries"])} | {I(k["integrated"])} | {I(k["pending"])} | {I(k["jobsSeen"])} | {I(k["seeds"])} | {I(k["unresearched"])} | {I(k["open"])} |");
        md.AppendLine();

        var countries = c["countries"]!.AsArray().Select(x => x!.AsObject()).ToList();
        foreach (var group in countries.GroupBy(x => S(x["continent"])).OrderBy(g => g.Key))
        {
            var name = c["continents"]!.AsArray().FirstOrDefault(k => S(k!["code"]) == group.Key)?["name"]?.GetValue<string>() ?? group.Key;
            md.AppendLine($"## {name}").AppendLine();
            var known = group.Where(x => S(x["status"]) != "UNRESEARCHED").OrderBy(x => S(x["status"])).ThenBy(x => S(x["name"])).ToList();
            if (known.Count > 0)
            {
                md.AppendLine("| País | Estado | Conectores do país | Pendentes | Vagas abertas | Brasil (confirmadas/possíveis) | Pistas da auditoria |").AppendLine("|---|---|---|---|---|---|---|");
                foreach (var x in known)
                    md.AppendLine($"| {S(x["name"])} ({S(x["iso2"])}) | {Status.GetValueOrDefault(S(x["status"]), S(x["status"]))} | {Join(x["platforms"])} | {Join(x["pendingPlatforms"])} | {I(x["open"])} | {I(x["brazilOk"])}/{I(x["brazilPossible"])} | {I(x["portals"])} portais, {I(x["apiCandidates"])} APIs candidatas, {I(x["wapes"])} agências WAPES |");
                md.AppendLine();
            }
            var unknown = group.Where(x => S(x["status"]) == "UNRESEARCHED").Select(x => S(x["name"])).Order().ToList();
            if (unknown.Count > 0) md.AppendLine($"Não pesquisados ({unknown.Count}): {string.Join(", ", unknown)}.").AppendLine();
        }
        md.AppendLine("## Próximos passos").AppendLine();
        md.AppendLine("Para cada país: consultar a agência pública no diretório WAPES, achar o portal nacional com busca real, procurar API, feed, RSS ou download em lote na")
          .AppendLine("documentação oficial, conferir termos, licença e dados pessoais e só então criar a fonte (no registro, com sondagem). Portal com login, CAPTCHA ou")
          .AppendLine("antirrobô fica AUTH_REQUIRED ou POLICY_BLOCKED, sem contorno. A lista priorizada está em docs/NEXT_SOURCES.md.");
        await File.WriteAllTextAsync(mdOut, md.ToString());
        Console.WriteLine($"{countries.Count} países: {I(t["countriesIntegrated"])} integrados, {I(t["countriesWithJobs"])} com vaga vista, {I(t["countriesUnresearched"])} não pesquisados. Escrito: {mdOut}");
        return 0;
    }

    private static string Join(JsonNode? n) => n is JsonArray a && a.Count > 0 ? string.Join(", ", a.Select(x => $"`{x!.GetValue<string>()}`")) : "-";
}
