namespace JobOrchestrator.Automation.Contracts;

/// <summary>
/// Perfil de uma fonte de vagas (especificação V2, §8.3 Source): como se lê (Access), em que estado está (Lifecycle), onde cobre (Countries,
/// ISO 3166-1 alpha-2 ou GLOBAL/LATAM/EUROPE), onde estão a documentação e os termos, o crédito que a fonte exige e a política de atualização.
/// CatalogIds liga o perfil aos registros do catálogo V2 do usuário (data/catalog).
/// </summary>
public sealed record SourceProfile(
    string Prefix, string Name, string Kind, string Access, string Lifecycle, string[] Countries, string DocsUrl, string? TermsUrl,
    string? Attribution, string Refresh, string[] CatalogIds, string Notes, string? Credential = null);

/// <summary>
/// O registro das fontes que o app sabe ler, com o estado de cada uma. Access: OFFICIAL_PUBLIC (API ou feed público documentado pela própria
/// fonte), OBSERVED (endpoint que o site público usa, sem contrato; pode mudar), CREDENTIALS_REQUIRED (chave da pessoa), PARTNER_ONLY.
/// Lifecycle: ACTIVE, EXPERIMENTAL (observado, atrás da chave Sources:Experimental), CREDENTIALS_REQUIRED, PARTNER_ONLY, BLOCKED (a política
/// proíbe), DISCOVERY_ONLY (só para achar empresas e links). Fonte sem permissão não é coletada: o portão fica em <see cref="Gate"/>.
/// </summary>
public static class SourceRegistry
{
    private const string Global = "GLOBAL";

    public static IReadOnlyList<SourceProfile> All { get; } =
    [
        // ATS com leitura pública oficial.
        new("greenhouse", "Greenhouse Job Board API", "ats", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://docs.greenhouse.io/job-board", "https://www.greenhouse.com/legal",
            null, "2-6 h por quadro", ["greenhouse-job-board-api"], "GET de anúncios públicos sem chave; candidatura por API exige credencial do empregador e não é implementada."),
        new("lever", "Lever Postings API", "ats", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://github.com/lever/postings-api", "https://www.lever.co/terms-of-service/",
            null, "2-6 h por quadro", ["lever-postings-api"], "api.lever.co e api.eu.lever.co; diferente da API privada do Lever."),
        new("ashby", "Ashby Public Job Posting API", "ats", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://developers.ashbyhq.com/docs/public-job-posting-api", "https://www.ashbyhq.com/terms",
            null, "2-6 h por quadro", ["ashby-public-posting-api"], "Vaga com isListed=false não aparece."),
        new("smartrecruiters", "SmartRecruiters Posting API", "ats", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://developers.smartrecruiters.com/docs/endpoints", "https://www.smartrecruiters.com/terms-of-service/",
            null, "2-6 h por empresa", ["smartrecruiters-posting-api"], "Paginado (limit e offset)."),
        new("inhire", "InHire páginas públicas", "ats", "OFFICIAL_PUBLIC", "ACTIVE", ["BR"], "https://docs.inhire.com.br/guides/guias/pagina-de-divulgacao/listar/", null,
            null, "2-6 h por empresa", ["inhire-public-pages-api"], "Por tenant (X-Tenant), sem busca unificada."),
        new("teamtailor", "Teamtailor RSS", "ats", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://docs.teamtailor.com/", null,
            null, "2-6 h por site", ["teamtailor-job-rss"], "Só o RSS público; a API central exige token da conta e não é chamada."),
        // ATS com endpoint observado: funciona como o site público, sem contrato.
        new("personio", "Personio XML", "ats", "OBSERVED", "EXPERIMENTAL", ["EUROPE"], "https://www.personio.com/", null,
            null, "6 h por empresa", ["personio-careers-xml"], "Feed XML público por tenant; confirmar por empresa."),
        new("breezy", "Breezy HR JSON", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://breezy.hr/", null,
            null, "6 h por empresa", ["breezy-hr-jobs-json"], "JSON por empresa divulgado em referência técnica comunitária."),
        new("recruitee", "Recruitee Careers Site", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://docs.recruitee.com/reference/authentication-1", null,
            null, "6 h por empresa", ["recruitee-careers-site"], "Rota anônima legada: a partir de 10/02/2027 exige X-Careers-Sites-Token e para sozinha.", "recruitee"),
        new("workable", "Workable widget", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://apply.workable.com/", "https://www.workable.com/terms",
            null, "6 h por empresa", ["workable-careers-widget"], "Widget de carreiras observado; a SPI oficial é autenticada e não é usada."),
        new("workable-search", "Workable busca de vagas", "api", "OBSERVED", "EXPERIMENTAL", [Global], "https://jobs.workable.com/", "https://www.workable.com/terms",
            null, "6 h por termo", ["workable-careers-widget"], "Busca pública de todas as empresas no Workable."),
        new("workday", "Workday CXS", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://www.workday.com/", null,
            null, "6 h por site", ["workday-careers-cxs"], "Endpoint dos sites públicos; tenant, host e site exatos; páginas de 20."),
        new("rippling", "Rippling ATS board", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://ats.rippling.com/", null,
            null, "6 h por empresa", ["rippling-careers-board"],
            "Sem conector: o endpoint público não foi confirmado em 05/11/2026 (o quadro do próprio Rippling redireciona para rippling.com/careers). Pendente em docs/NEXT_SOURCES.md."),
        new("oracle", "Oracle Recruiting Cloud", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://www.oracle.com/human-capital-management/recruiting/", null,
            null, "6 h por site", [], "API REST pública dos sites de carreira (requisitionList); robots.txt conferido."),
        new("successfactors", "SAP SuccessFactors", "ats", "OBSERVED", "EXPERIMENTAL", [Global], "https://www.sap.com/products/hcm/recruiting-software.html", null,
            null, "6 h por site", [], "Página pública de vagas; robots.txt conferido."),
        new("gupy", "Gupy páginas de carreira e portal", "ats", "OBSERVED", "EXPERIMENTAL", ["BR"], "https://portal.gupy.io/", "https://www.gupy.io/termos-de-uso",
            null, "6 h por empresa", ["gupy-employability-portal"], "Dados embutidos na página pública de carreiras (robots.txt libera /) e busca do portal; a API de empregador (Bearer) nunca é usada."),
        new("solides", "Sólides vagas", "ats", "OBSERVED", "EXPERIMENTAL", ["BR"], "https://solides.com.br/", null,
            null, "6 h por empresa", [], "API pública da página de vagas."),
        new("quickin", "Quickin vagas", "ats", "OBSERVED", "EXPERIMENTAL", ["BR"], "https://quickin.io/", null,
            null, "6 h por empresa", [], "API pública da página de vagas."),
        new("pandape", "Pandapé", "ats", "OBSERVED", "EXPERIMENTAL", ["BR", "LATAM"], "https://www.pandape.com.br/", null,
            null, "6 h por empresa", [], "Página da vaga com JSON-LD; empresas pelo sitemap oficial."),
        // APIs e feeds de vagas.
        new("himalayas", "Himalayas", "api", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://himalayas.app/docs/remote-jobs-api", "https://himalayas.app/docs/remote-jobs-api",
            "Crédito à Himalayas; candidatura pelo applicationLink da API; não redistribuir a outros agregadores.", "1 h", ["himalayas-api"], "locationRestrictions e timezoneRestrictions entram na elegibilidade."),
        new("remoteok", "Remote OK", "api", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://remoteok.com/api", "https://remoteok.com/faq",
            "Crédito e link para a vaga no Remote OK.", "1 h", ["remote-ok-api"], ""),
        new("wwr", "We Work Remotely", "feed", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://weworkremotely.com/remote-job-rss-feed", "https://weworkremotely.com/remote-job-rss-feed",
            "Atribuir os links à origem.", "30 min", ["we-work-remotely-rss"], "RSS oficial por categoria."),
        new("remotive", "Remotive", "api", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://remotive.com/remote-jobs/api", "https://remotive.com/remote-jobs/api",
            "Link e crédito à Remotive; não repassar a outros boards.", "6 h (até 4 leituras por dia)", ["remotive-api"], "Vagas chegam com 24 h de atraso."),
        new("jobicy", "Jobicy", "api", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://jobicy.com/jobs-rss-feed", null,
            null, "2 h", ["jobicy-api"], "Janela recente de 7 dias."),
        new("getonbrd", "Get on Board", "api", "OFFICIAL_PUBLIC", "ACTIVE", ["LATAM"], "https://www.getonbrd.com/user-manual/get-on-board-s-api", "https://www.getonbrd.com/terms",
            null, "2 h", ["get-on-board-api"], "Tecnologia na América Latina."),
        new("arbeitnow", "Arbeitnow", "api", "OFFICIAL_PUBLIC", "ACTIVE", ["EUROPE"], "https://www.arbeitnow.com/blog/job-board-api", null,
            null, "2 h", ["arbeitnow-api"], "Sobretudo Europa."),
        new("themuse", "The Muse", "api", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://www.themuse.com/developers/api/v2", "https://www.themuse.com/developers/api/v2",
            null, "6 h", ["the-muse-jobs-api"], "Registro do app recomendado; sem chave funciona com cota menor."),
        new("jobtech", "JobTech JobSearch (Suécia)", "gov", "OFFICIAL_PUBLIC", "ACTIVE", ["SE"], "https://jobsearch.api.jobtechdev.se/", "https://data.arbetsformedlingen.se/dataset/job-ads/",
            "Dados CC0 da Arbetsförmedlingen.", "3 h", ["sweden-jobtech-jobsearch-api"], "Serviço público de emprego da Suécia."),
        new("nav", "NAV pam-stilling-feed (Noruega)", "gov", "OFFICIAL_PUBLIC", "ACTIVE", ["NO"], "https://navikt.github.io/pam-stilling-feed/", "https://arbeidsplassen.nav.no/vilkar-api",
            "Dados abertos da NAV.", "3 h", ["norway-nav-stilling-feed", "norway-nav-feed-openapi"],
            "Token público rotativo (/api/publicToken); anúncio INACTIVE fecha a vaga, como os termos pedem; contatos pessoais não são guardados; a API antiga pam-public-feed saiu em maio de 2025."),
        new("seasonaljobs", "SeasonalJobs.dol.gov (EUA)", "gov", "OFFICIAL_PUBLIC", "ACTIVE", ["US"], "https://seasonaljobs.dol.gov/feeds", "https://seasonaljobs.dol.gov/feeds",
            "Dados do U.S. Department of Labor.", "24 h", ["us-dol-seasonal-jobs-feeds"],
            "Arquivo diário oficial com as ordens H-2A (790/790A) dos últimos 20 dias: visto temporário agrícola, não patrocínio de visto qualificado. Os arquivos 9142A e 9142B são pedidos de certificação, não vagas."),
        new("freehire", "freehire", "api", "OBSERVED", "EXPERIMENTAL", [Global], "https://freehire.me/docs/api", "https://freehire.me/docs/api",
            "Link de origem preservado; uso pessoal e não comercial.", "2 h", ["freehire-api"], "Agregador aberto de ~420 fontes; números do projeto são declarados por ele."),
        new("4dayweek", "4 Day Week", "api", "OBSERVED", "EXPERIMENTAL", [Global], "https://4dayweek.io/", null, null, "6 h", [], ""),
        new("workingnomads", "Working Nomads", "api", "OBSERVED", "EXPERIMENTAL", [Global], "https://www.workingnomads.com/", null, null, "6 h", [], ""),
        new("hn", "Hacker News Who is hiring", "community", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://hn.algolia.com/api", "https://news.ycombinator.com/",
            null, "24 h", ["hacker-news-hiring-threads"], "Busca da Algolia; e-mail só quando o post o publica para candidatura."),
        new("github", "GitHub issues de vagas", "community", "OFFICIAL_PUBLIC", "ACTIVE", ["BR"], "https://docs.github.com/rest/issues", "https://docs.github.com/site-policy/github-terms/github-terms-of-service",
            null, "6 h", [], "60 chamadas por hora sem token."),
        new("rss", "RSS de vagas", "feed", "OBSERVED", "EXPERIMENTAL", [Global], "https://www.rssboard.org/rss-specification", null, null, "6 h por feed", [], "Feed público informado por fonte."),
        new("web", "Site de carreiras (coletor)", "site", "OBSERVED", "EXPERIMENTAL", [Global], "https://developers.google.com/search/docs/appearance/structured-data/job-posting", null,
            null, "6 h por site", ["google-jobposting-structured-data"], "JSON-LD JobPosting e links de ATS; robots.txt sempre obedecido."),
        new("alertas", "Alertas de vaga por e-mail", "email", "OFFICIAL_PUBLIC", "ACTIVE", [Global], "https://support.google.com/mail/answer/7126229", null,
            null, "10 min", [], "Da caixa da própria pessoa (IMAP); o link do alerta nunca é aberto por robô."),
        // Com chave da pessoa.
        new("adzuna", "Adzuna", "api", "CREDENTIALS_REQUIRED", "CREDENTIALS_REQUIRED", [Global], "https://developer.adzuna.com/overview", "https://developer.adzuna.com/overview",
            null, "6 h", ["adzuna-api"], "app_id e app_key próprios.", "adzuna"),
        new("jooble", "Jooble", "api", "CREDENTIALS_REQUIRED", "CREDENTIALS_REQUIRED", [Global], "https://jooblehelpcenter.freshdesk.com/en/support/solutions/articles/60001448238-rest-api-documentation", null,
            null, "24 h", ["jooble-rest-api"], "Chave por país; o plano grátis tem 500 chamadas no total da chave.", "jooble"),
        new("usajobs", "USAJOBS", "gov", "CREDENTIALS_REQUIRED", "CREDENTIALS_REQUIRED", ["US"], "https://developer.usajobs.gov/guides/authentication", "https://developer.usajobs.gov/",
            null, "24 h", ["usajobs-api", "us-usajobs-api-spec"], "Authorization-Key e o e-mail da conta no User-Agent; quase sempre pede cidadania americana.", "usajobs"),
        new("reliefweb", "ReliefWeb Jobs", "gov", "CREDENTIALS_REQUIRED", "CREDENTIALS_REQUIRED", [Global], "https://apidoc.reliefweb.int/read/resources/jobs/", "https://apidoc.reliefweb.int/",
            null, "24 h", ["reliefweb-jobs-api"], "appname aprovado pela ReliefWeb.", "reliefweb"),
        // Bloqueadas por política ou só com parceria: não são coletadas.
        new("linkedin", "LinkedIn Jobs", "board", "PARTNER_ONLY", "BLOCKED", [Global], "https://www.linkedin.com/help/linkedin/answer/a1341387/prohibited-software-and-extensions", "https://www.linkedin.com/legal/user-agreement",
            null, "-", [], "Robôs e extensões de automação proibidos: só export de dados, link salvo e alertas por e-mail."),
        new("francetravail", "France Travail Offres d'emploi", "gov", "PARTNER_ONLY", "BLOCKED", ["FR"], "https://www.data.gouv.fr/dataservices/api-offres-demploi", null,
            null, "-", ["france-travail-offres-api"], "Acesso restrito em 2026; pedir pelo formulário oficial."),
        new("gupy-api", "Gupy API de empregador", "ats", "PARTNER_ONLY", "PARTNER_ONLY", ["BR"], "https://developers.gupy.io/reference/authentication", null,
            null, "-", ["gupy-employer-api"], "Bearer de cliente da Gupy; nunca usada como busca global."),
    ];

    /// <summary>
    /// Amostras públicas para a sondagem do registro, usadas quando a campanha não tem uma fonte daquela plataforma. Só tenants e termos que a
    /// própria plataforma expõe publicamente; plataforma sem amostra segura fica "sem amostra" na auditoria, sem chute.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Samples { get; } = new Dictionary<string, string>
    {
        ["greenhouse"] = "greenhouse:gitlab", ["lever"] = "lever:spotify", ["ashby"] = "ashby:notion", ["gupy"] = "gupy:ambev",
        ["remotive"] = "remotive:developer", ["arbeitnow"] = "arbeitnow:developer", ["himalayas"] = "himalayas:engineer", ["jobicy"] = "jobicy:dev",
        ["themuse"] = "themuse:Software Engineering", ["remoteok"] = "remoteok:dev", ["wwr"] = "wwr:remote-programming-jobs", ["github"] = "github:backend-br/vagas",
        ["hn"] = "hn:whoishiring", ["getonbrd"] = "getonbrd:developer", ["jobtech"] = "jobtech:developer", ["nav"] = "nav:utvikler", ["seasonaljobs"] = "seasonaljobs:all",
        // Conferidas em 05/11/2026 pela sondagem real (responderam com vaga, ou a própria plataforma quando a empresa não tem vaga aberta).
        ["smartrecruiters"] = "smartrecruiters:BoschGroup", ["teamtailor"] = "teamtailor:polestar", ["breezy"] = "breezy:breezy", ["workable"] = "workable:huggingface",
        ["recruitee"] = "recruitee:bunq", ["solides"] = "solides:solides", ["pandape"] = "pandape:infojobs", ["oracle"] = "oracle:eeho.fa.us2.oraclecloud.com/CX_1",
        ["4dayweek"] = "4dayweek:engineering", ["workingnomads"] = "workingnomads:development", ["freehire"] = "freehire:developer", ["personio"] = "personio:personio",
        // Com chave da pessoa: a sondagem mostra "pede credencial" até a chave existir.
        ["usajobs"] = "usajobs:developer", ["reliefweb"] = "reliefweb:officer", ["adzuna"] = "adzuna:developer", ["jooble"] = "jooble:developer",
    };

    public static SourceProfile? Find(string source)
    {
        var i = source.IndexOf(':');
        var prefix = (i < 0 ? source : source[..i]).Trim().ToLowerInvariant();
        return All.FirstOrDefault(p => p.Prefix == prefix);
    }

    /// <summary>
    /// O portão da coleta: null quando a fonte pode ser lida agora, ou o motivo (BLOCKED_BY_POLICY, PARTNER_ONLY, DISABLED, EXPERIMENTAL_OFF,
    /// SUNSET) quando não pode. Fonte desconhecida passa: o conector decide se aceita.
    /// </summary>
    public static string? Gate(string source, IReadOnlyCollection<string> disabled, bool experimental, DateTime today)
    {
        if (Find(source) is not { } p) return null;
        if (p.Lifecycle == "BLOCKED") return $"BLOCKED_BY_POLICY: {p.Name}. {p.Notes}";
        if (p.Lifecycle == "PARTNER_ONLY") return $"PARTNER_ONLY: {p.Name} só com parceria. {p.Notes}";
        if (disabled.Contains(p.Prefix, StringComparer.OrdinalIgnoreCase)) return $"DISABLED: {p.Name} desligada em Fontes.";
        if (p.Lifecycle == "EXPERIMENTAL" && !experimental) return $"EXPERIMENTAL_OFF: {p.Name} é endpoint observado e as fontes experimentais estão desligadas.";
        if (p.Prefix == "recruitee" && today >= new DateTime(2027, 2, 10)) return "SUNSET: a rota anônima do Recruitee passou a exigir X-Careers-Sites-Token em 10/02/2027.";
        return null;
    }
}
