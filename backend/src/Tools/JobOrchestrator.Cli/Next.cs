using System.Text;
using System.Text.Json;

/// <summary>
/// jo sources next: o inventário priorizado do que falta (registros do catálogo V2 que ainda não viraram plataforma no registro), com o caminho
/// certo para cada tipo de acesso: conector, pedido de chave ou parceria, pista do Explorador, só referência, histórico ou fora (pago).
/// </summary>
internal static class NextSources
{
    private static (string Group, string How) Path(string method) => method switch
    {
        "PUBLIC_READ_API" or "PUBLIC_JSON_BY_BOARD" or "PUBLIC_XML_BY_BOARD" or "PUBLIC_RSS_BY_BOARD" or "PUBLIC_RSS" or "PUBLIC_EVENT_FEED" or "PUBLIC_DATA_FEED"
            => ("Conector a escrever", "Ler a documentação oficial, conferir termos, licença e dados pessoais, escrever o conector com teste de contrato e só ligar depois da sondagem."),
        "API_KEY" or "API_KEY_REGISTRATION" or "API_KEY_PER_COUNTRY" or "REGISTRATION_OR_KEY" or "APPNAME_APPROVAL" or "APPROVAL_REQUIRED" or "PARTNER_REGISTRATION"
            or "ACCOUNT_REQUIRED" or "AUTHENTICATED_PARTNER_API" or "RESTRICTED_ACCESS" or "TOKEN_REQUIRED_FROM_2027-02-10"
            => ("Pedir acesso", "Pedir pelo canal oficial (cadastro de desenvolvedor, appname, contrato de parceria) com nome, uso pessoal e não comercial; até a resposta, fica CREDENTIALS_REQUIRED ou PARTNER_ONLY."),
        "OFFICIAL_JOB_PORTAL" or "WEBSITE_ONLY" or "OFFICIAL_BULLETIN" or "OFFICIAL_PDF_BULLETIN"
            => ("Portal sem API conhecida", "Procurar feed, RSS, XML ou API na documentação; se não houver, conferir termos e robots.txt; pedir feed ou parceria ao órgão. Login, CAPTCHA ou antirrobô: AUTH_REQUIRED ou POLICY_BLOCKED, sem contorno."),
        "AGENCY_DIRECTORY_ONLY" or "DIRECTORY_ONLY" or "OFFICIAL_DIRECTORY" or "SOURCE_DIRECTORY" or "DISCOVERY_DIRECTORY" or "DISCOVERY_ONLY" or "COMPANY_ATS_DIRECTORY"
            or "REMOTE_COMPANY_DIRECTORY" or "ATS_BOARD_CATALOG" or "ATS_BOARD_CATALOG_CSV" or "ATS_BOARD_CATALOG_JSON" or "SOURCE_INDEX" or "HN_ALGOLIA_DISCOVERY" or "SEMANTIC_SCHEMA" or "ATS_API_GUIDE"
            => ("Pista do Explorador", "Serve para achar empresas, quadros e portais, não para coletar: o Explorador usa e cada quadro achado passa pela sondagem antes de entrar."),
        "OPEN_SOURCE_CODE" or "OPEN_SOURCE_REFERENCE" or "SCRAPER_SOURCE_CODE" or "JOB_AGGREGATOR_SOURCE_CODE" or "UNOFFICIAL_CONNECTOR_EXAMPLE" or "UNOFFICIAL_GOV_API_WRAPPER"
            or "UNOFFICIAL_WEB_BACKEND" or "UNOFFICIAL_PUBLIC_WIDGET" or "OPEN_SOURCE_OR_ACTOR" or "PUBLIC_API_CLAIM_REQUIRES_VALIDATION"
            => ("Só referência", "Estudar o que faz e conferir a licença; nunca copiar código (regra do projeto). Endpoint não oficial só entra como EXPERIMENTAL depois de conferido."),
        "SNAPSHOT_ONLY" or "OPEN_DATA_SNAPSHOTS" or "PUBLIC_SNAPSHOT" or "ATS_SCRAPER_AND_SNAPSHOTS"
            => ("Histórico", "Importar em streaming, com teto de disco e RAM, como histórico: nenhuma vaga vira OPEN sem revalidação na fonte viva."),
        "PARTIAL_DATA_OR_PAID_API" or "EXTERNAL_PAID_ACTOR" or "COMMERCIAL_FEED_DIRECTORY" => ("Fora: pago ou comercial", "O projeto não usa API paga."),
        "DO_NOT_USE" => ("Fora: não usar", "O próprio catálogo marca como não usar."),
        _ => ("Avaliar", "Ler a documentação e classificar."),
    };

    public static async Task<int> WriteAsync(string catalogPath, string registryPath, string mdOut)
    {
        using var catalog = JsonDocument.Parse(await File.ReadAllTextAsync(catalogPath));
        using var registry = JsonDocument.Parse(await File.ReadAllTextAsync(registryPath));
        var linked = registry.RootElement.GetProperty("sources").EnumerateArray().SelectMany(s => s.GetProperty("catalog_ids").EnumerateArray().Select(x => x.GetString())).ToHashSet();
        string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
        var pending = catalog.RootElement.GetProperty("sources").EnumerateArray().Where(r => !linked.Contains(S(r, "id"))).ToList();
        var md = new StringBuilder();
        md.AppendLine("# Próximas fontes").AppendLine();
        md.AppendLine($"Gerado por `jo sources next` em {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC. São os {pending.Count} registros do catálogo V2 (de {catalog.RootElement.GetProperty("sources").GetArrayLength()}) que ainda não")
          .AppendLine("viraram plataforma no registro (`data/source-registry.json`), em ordem de prioridade do catálogo (P0 primeiro). Nenhum foi dado como funcionando: cada um")
          .AppendLine("precisa de documentação, termos e sondagem antes de entrar. Agências do diretório WAPES ficam juntas no fim, por país.").AppendLine();
        foreach (var group in pending.Where(r => S(r, "method") != "AGENCY_DIRECTORY_ONLY").GroupBy(r => Path(S(r, "method")).Group)
                     .OrderBy(g => Array.IndexOf(new[] { "Conector a escrever", "Pedir acesso", "Portal sem API conhecida", "Histórico", "Pista do Explorador", "Só referência", "Avaliar", "Fora: pago ou comercial", "Fora: não usar" }, g.Key)))
        {
            md.AppendLine($"## {group.Key} ({group.Count()})").AppendLine().AppendLine(Path(S(group.First(), "method")).How).AppendLine();
            md.AppendLine("| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |").AppendLine("|---|---|---|---|---|---|");
            foreach (var r in group.OrderBy(r => S(r, "priority")).ThenBy(r => S(r, "name")))
                md.AppendLine($"| {S(r, "priority")} | {S(r, "name").Replace("|", "/")} | {S(r, "scope")} | {S(r, "method")} | {S(r, "verification_status")} | {S(r, "url")} |");
            md.AppendLine();
        }
        var agencies = pending.Where(r => S(r, "method") == "AGENCY_DIRECTORY_ONLY").OrderBy(r => S(r, "scope")).ToList();
        if (agencies.Count > 0)
        {
            md.AppendLine($"## Agências públicas de emprego do diretório WAPES ({agencies.Count})").AppendLine();
            md.AppendLine("Uma agência não implica API nem vaga aberta. Caminho: abrir o portal da agência, procurar busca real de vagas e documentação de API ou feed, conferir termos e só então criar a fonte.").AppendLine();
            md.AppendLine("| País | Agência | Endereço |").AppendLine("|---|---|---|");
            foreach (var r in agencies) md.AppendLine($"| {S(r, "scope")} | {S(r, "name").Replace("|", "/")} | {S(r, "url")} |");
            md.AppendLine();
        }
        md.AppendLine("## Como pedir parceria ou acesso").AppendLine();
        md.AppendLine("Escrever pelo canal oficial da fonte (formulário de desenvolvedor, e-mail de dados abertos ou de parcerias) dizendo: uso pessoal e não comercial, leitura só")
          .AppendLine("dos anúncios públicos, frequência baixa (a cada poucas horas), crédito à fonte em cada vaga, nada de dado pessoal guardado e remoção imediata de anúncio encerrado.")
          .AppendLine("A chave ou o appname recebido entra em Fontes › Chaves de API (gnome-keyring), e a plataforma passa de CREDENTIALS_REQUIRED para ACTIVE na próxima sondagem.");
        await File.WriteAllTextAsync(mdOut, md.ToString());
        Console.WriteLine($"{pending.Count} pendentes ({agencies.Count} agências WAPES). Escrito: {mdOut}");
        return 0;
    }
}
