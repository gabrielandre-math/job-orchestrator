# Auditoria das fontes

Gerado por `jo sources probe` em 05/11/2026 23:21 UTC, contra a API local (http://127.0.0.1:7420). Cada plataforma com permissão foi chamada uma vez pela porta oficial,
com a amostra da campanha ou uma amostra pública; bloqueadas e só com parceria nunca são chamadas. Os números abaixo são desta execução, sem estimativa.

| | Quantas |
|---|---|
| Plataformas no registro | 45 |
| Com conector no app | 37 |
| Autorizadas agora (portão aberto) | 38 |
| Sondadas | 32 |
| Funcionando (responderam com vaga) | 28 |
| Esperando credencial da pessoa | 4 |
| Bloqueadas por política ou só com parceria | 3 |

## ACTIVE

| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |
|---|---|---|---|---|---|---|
| Greenhouse Job Board API (`greenhouse`) | OFFICIAL_PUBLIC | GLOBAL | `greenhouse:vtex` HTTP 200, 33 vagas, 1.2 s | [link](https://docs.greenhouse.io/job-board) | [link](https://www.greenhouse.com/legal) | GET de anúncios públicos sem chave; candidatura por API exige credencial do empregador e não é implementada. |
| Lever Postings API (`lever`) | OFFICIAL_PUBLIC | GLOBAL | `lever:spotify` HTTP 200, 86 vagas, 24 s | [link](https://github.com/lever/postings-api) | [link](https://www.lever.co/terms-of-service/) | api.lever.co e api.eu.lever.co; diferente da API privada do Lever. |
| Ashby Public Job Posting API (`ashby`) | OFFICIAL_PUBLIC | GLOBAL | `ashby:ramp` HTTP 200, 170 vagas, 0.2 s | [link](https://developers.ashbyhq.com/docs/public-job-posting-api) | [link](https://www.ashbyhq.com/terms) | Vaga com isListed=false não aparece. |
| SmartRecruiters Posting API (`smartrecruiters`) | OFFICIAL_PUBLIC | GLOBAL | `smartrecruiters:BoschGroup` HTTP 200, 40 vagas, 36.7 s | [link](https://developers.smartrecruiters.com/docs/endpoints) | [link](https://www.smartrecruiters.com/terms-of-service/) | Paginado (limit e offset). |
| InHire páginas públicas (`inhire`) | OFFICIAL_PUBLIC | BR | sem amostra pública segura | [link](https://docs.inhire.com.br/guides/guias/pagina-de-divulgacao/listar/) | - | Por tenant (X-Tenant), sem busca unificada. |
| Teamtailor RSS (`teamtailor`) | OFFICIAL_PUBLIC | GLOBAL | `teamtailor:polestar` HTTP 200, 12 vagas, 1.5 s | [link](https://docs.teamtailor.com/) | - | Só o RSS público; a API central exige token da conta e não é chamada. |
| Himalayas (`himalayas`) | OFFICIAL_PUBLIC | GLOBAL | `himalayas:c#` HTTP 200, 20 vagas, 0.1 s | [link](https://himalayas.app/docs/remote-jobs-api) | [link](https://himalayas.app/docs/remote-jobs-api) | locationRestrictions e timezoneRestrictions entram na elegibilidade. |
| Remote OK (`remoteok`) | OFFICIAL_PUBLIC | GLOBAL | `remoteok:dev` HTTP 200, 99 vagas, 1.2 s | [link](https://remoteok.com/api) | [link](https://remoteok.com/faq) |  |
| We Work Remotely (`wwr`) | OFFICIAL_PUBLIC | GLOBAL | `wwr:remote-back-end-programming-jobs` HTTP 200, 13 vagas, 0.5 s | [link](https://weworkremotely.com/remote-job-rss-feed) | [link](https://weworkremotely.com/remote-job-rss-feed) | RSS oficial por categoria. |
| Remotive (`remotive`) | OFFICIAL_PUBLIC | GLOBAL | `remotive:dotnet` HTTP 200, 10 vagas, 0.1 s | [link](https://remotive.com/remote-jobs/api) | [link](https://remotive.com/remote-jobs/api) | Vagas chegam com 24 h de atraso. |
| Jobicy (`jobicy`) | OFFICIAL_PUBLIC | GLOBAL | `jobicy:dev` HTTP 200, 50 vagas, 1.4 s | [link](https://jobicy.com/jobs-rss-feed) | - | Janela recente de 7 dias. |
| Get on Board (`getonbrd`) | OFFICIAL_PUBLIC | LATAM | `getonbrd:developer` HTTP 200, 150 vagas, 3 s | [link](https://www.getonbrd.com/user-manual/get-on-board-s-api) | [link](https://www.getonbrd.com/terms) | Tecnologia na América Latina. |
| Arbeitnow (`arbeitnow`) | OFFICIAL_PUBLIC | EUROPE | `arbeitnow:dotnet` HTTP 200, 0 vagas, 0.4 s | [link](https://www.arbeitnow.com/blog/job-board-api) | - | Sobretudo Europa. |
| The Muse (`themuse`) | OFFICIAL_PUBLIC | GLOBAL | `themuse:Software Engineering` HTTP 200, 20 vagas, 1.1 s | [link](https://www.themuse.com/developers/api/v2) | [link](https://www.themuse.com/developers/api/v2) | Registro do app recomendado; sem chave funciona com cota menor. |
| JobTech JobSearch (Suécia) (`jobtech`) | OFFICIAL_PUBLIC | SE | `jobtech:developer` HTTP 200, 200 vagas, 10.5 s | [link](https://jobsearch.api.jobtechdev.se/) | [link](https://data.arbetsformedlingen.se/dataset/job-ads/) | Serviço público de emprego da Suécia. |
| NAV pam-stilling-feed (Noruega) (`nav`) | OFFICIAL_PUBLIC | NO | `nav:utvikler` HTTP -, 0 vagas, 0 s, Não reconheci esta fonte. Confira a plataforma e o nome da empresa. | [link](https://navikt.github.io/pam-stilling-feed/) | [link](https://navikt.github.io/pam-stilling-feed/) | Feed de eventos com token público; a API antiga pam-public-feed está descontinuada. |
| SeasonalJobs.dol.gov (EUA) (`seasonaljobs`) | OFFICIAL_PUBLIC | US | `seasonaljobs:all` HTTP -, 0 vagas, 0 s, Não reconheci esta fonte. Confira a plataforma e o nome da empresa. | [link](https://seasonaljobs.dol.gov/feeds) | [link](https://seasonaljobs.dol.gov/feeds) | Vagas H-2A e H-2B: visto temporário, não patrocínio de visto qualificado. |
| Hacker News Who is hiring (`hn`) | OFFICIAL_PUBLIC | GLOBAL | `hn:whoishiring` HTTP 200, 3 vagas, 1.3 s | [link](https://hn.algolia.com/api) | [link](https://news.ycombinator.com/) | Busca da Algolia; e-mail só quando o post o publica para candidatura. |
| GitHub issues de vagas (`github`) | OFFICIAL_PUBLIC | BR | `github:backend-br/vagas` HTTP 200, 44 vagas, 0.7 s | [link](https://docs.github.com/rest/issues) | [link](https://docs.github.com/site-policy/github-terms/github-terms-of-service) | 60 chamadas por hora sem token. |
| Alertas de vaga por e-mail (`alertas`) | OFFICIAL_PUBLIC | GLOBAL | sem amostra pública segura | [link](https://support.google.com/mail/answer/7126229) | - | Da caixa da própria pessoa (IMAP); o link do alerta nunca é aberto por robô. |

## EXPERIMENTAL

| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |
|---|---|---|---|---|---|---|
| Personio XML (`personio`) | OBSERVED | EUROPE | `personio:personio` HTTP 200, 0 vagas, 1.1 s | [link](https://www.personio.com/) | - | Feed XML público por tenant; confirmar por empresa. |
| Breezy HR JSON (`breezy`) | OBSERVED | GLOBAL | `breezy:breezy` HTTP 200, 3 vagas, 0.3 s | [link](https://breezy.hr/) | - | JSON por empresa divulgado em referência técnica comunitária. |
| Recruitee Careers Site (`recruitee`) | OBSERVED | GLOBAL | `recruitee:bunq` HTTP 200, 23 vagas, 1.3 s | [link](https://docs.recruitee.com/reference/authentication-1) | - | Rota anônima legada: a partir de 10/02/2027 exige X-Careers-Sites-Token e para sozinha. |
| Workable widget (`workable`) | OBSERVED | GLOBAL | `workable:huggingface` HTTP 200, 6 vagas, 0.2 s | [link](https://apply.workable.com/) | [link](https://www.workable.com/terms) | Widget de carreiras observado; a SPI oficial é autenticada e não é usada. |
| Workable busca de vagas (`workable-search`) | OBSERVED | GLOBAL | `workable-search:asp.net core` HTTP 200, 60 vagas, 3.4 s | [link](https://jobs.workable.com/) | [link](https://www.workable.com/terms) | Busca pública de todas as empresas no Workable. |
| Workday CXS (`workday`) | OBSERVED | GLOBAL | `workday:accenture.wd103.myworkdayjobs.com/AvanadeCareers` HTTP 200, 20 vagas, 14.6 s | [link](https://www.workday.com/) | - | Endpoint dos sites públicos; tenant, host e site exatos; páginas de 20. |
| Rippling ATS board (`rippling`) | OBSERVED | GLOBAL | sem amostra pública segura | [link](https://ats.rippling.com/) | - | Endpoint público observado em referência comunitária. |
| Oracle Recruiting Cloud (`oracle`) | OBSERVED | GLOBAL | `oracle:eeho.fa.us2.oraclecloud.com/CX_1` HTTP 200, 20 vagas, 17.7 s | [link](https://www.oracle.com/human-capital-management/recruiting/) | - | API REST pública dos sites de carreira (requisitionList); robots.txt conferido. |
| SAP SuccessFactors (`successfactors`) | OBSERVED | GLOBAL | sem amostra pública segura | [link](https://www.sap.com/products/hcm/recruiting-software.html) | - | Página pública de vagas; robots.txt conferido. |
| Gupy páginas de carreira e portal (`gupy`) | OBSERVED | BR | `gupy:softplan` HTTP 200, 5 vagas, 2.8 s | [link](https://portal.gupy.io/) | [link](https://www.gupy.io/termos-de-uso) | Dados embutidos na página pública de carreiras (robots.txt libera /) e busca do portal; a API de empregador (Bearer) nunca é usada. |
| Sólides vagas (`solides`) | OBSERVED | BR | `solides:solides` HTTP 200, 18 vagas, 2.1 s | [link](https://solides.com.br/) | - | API pública da página de vagas. |
| Quickin vagas (`quickin`) | OBSERVED | BR | `quickin:igma` HTTP 200, 3 vagas, 0.2 s | [link](https://quickin.io/) | - | API pública da página de vagas. |
| Pandapé (`pandape`) | OBSERVED | BR, LATAM | `pandape:infojobs` HTTP 200, 3 vagas, 2.7 s | [link](https://www.pandape.com.br/) | - | Página da vaga com JSON-LD; empresas pelo sitemap oficial. |
| freehire (`freehire`) | OBSERVED | GLOBAL | `freehire:developer` HTTP 200, 720 vagas, 13.4 s | [link](https://freehire.me/docs/api) | [link](https://freehire.me/docs/api) | Agregador aberto de ~420 fontes; números do projeto são declarados por ele. |
| 4 Day Week (`4dayweek`) | OBSERVED | GLOBAL | `4dayweek:engineering` HTTP 200, 59 vagas, 3.1 s | [link](https://4dayweek.io/) | - |  |
| Working Nomads (`workingnomads`) | OBSERVED | GLOBAL | `workingnomads:development` HTTP 200, 23 vagas, 1 s | [link](https://www.workingnomads.com/) | - |  |
| RSS de vagas (`rss`) | OBSERVED | GLOBAL | sem amostra pública segura | [link](https://www.rssboard.org/rss-specification) | - | Feed público informado por fonte. |
| Site de carreiras (coletor) (`web`) | OBSERVED | GLOBAL | sem amostra pública segura | [link](https://developers.google.com/search/docs/appearance/structured-data/job-posting) | - | JSON-LD JobPosting e links de ATS; robots.txt sempre obedecido. |

## CREDENTIALS_REQUIRED

| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |
|---|---|---|---|---|---|---|
| Adzuna (`adzuna`) | CREDENTIALS_REQUIRED | GLOBAL | sem amostra pública segura | [link](https://developer.adzuna.com/overview) | [link](https://developer.adzuna.com/overview) | app_id e app_key próprios. |
| Jooble (`jooble`) | CREDENTIALS_REQUIRED | GLOBAL | sem amostra pública segura | [link](https://jooblehelpcenter.freshdesk.com/en/support/solutions/articles/60001448238-rest-api-documentation) | - | Chave por país; o plano grátis tem 500 chamadas no total da chave. |
| USAJOBS (`usajobs`) | CREDENTIALS_REQUIRED | US | sem amostra pública segura | [link](https://developer.usajobs.gov/guides/authentication) | [link](https://developer.usajobs.gov/) | Authorization-Key e o e-mail da conta no User-Agent; quase sempre pede cidadania americana. |
| ReliefWeb Jobs (`reliefweb`) | CREDENTIALS_REQUIRED | GLOBAL | sem amostra pública segura | [link](https://apidoc.reliefweb.int/read/resources/jobs/) | [link](https://apidoc.reliefweb.int/) | appname aprovado pela ReliefWeb. |

## PARTNER_ONLY

| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |
|---|---|---|---|---|---|---|
| Gupy API de empregador (`gupy-api`) | PARTNER_ONLY | BR | não sondada: PARTNER_ONLY | [link](https://developers.gupy.io/reference/authentication) | - | Bearer de cliente da Gupy; nunca usada como busca global. |

## BLOCKED

| Fonte | Acesso | Países | Sondagem | Documentação | Termos | Notas |
|---|---|---|---|---|---|---|
| LinkedIn Jobs (`linkedin`) | PARTNER_ONLY | GLOBAL | não sondada: BLOCKED | [link](https://www.linkedin.com/help/linkedin/answer/a1341387/prohibited-software-and-extensions) | [link](https://www.linkedin.com/legal/user-agreement) | Robôs e extensões de automação proibidos: só export de dados, link salvo e alertas por e-mail. |
| France Travail Offres d'emploi (`francetravail`) | PARTNER_ONLY | FR | não sondada: BLOCKED | [link](https://www.data.gouv.fr/dataservices/api-offres-demploi) | - | Acesso restrito em 2026; pedir pelo formulário oficial. |

## Catálogo V2 do usuário

`data/catalog/catalogo-fontes-v2-2026.json` (Global Job Source Seed Registry v2 (with agency discovery and audit tracker), compilado em 2026-11-05): 321 registros, dos quais 35 ligados a perfis do registro.
Registro do catálogo não é fonte funcionando: os que não têm perfil aqui seguem como pista ou pendência em docs/NEXT_SOURCES.md.

| Método | Registros |
|---|---|
| DISCOVERY_ONLY | 157 |
| AGENCY_DIRECTORY_ONLY | 79 |
| PUBLIC_READ_API | 14 |
| OFFICIAL_JOB_PORTAL | 6 |
| UNOFFICIAL_WEB_BACKEND | 3 |
| API_KEY | 3 |
| API_KEY_REGISTRATION | 3 |
| OPEN_SOURCE_CODE | 3 |
| WEBSITE_ONLY | 3 |
| AUTHENTICATED_PARTNER_API | 2 |
| ATS_SCRAPER_AND_SNAPSHOTS | 2 |
| SNAPSHOT_ONLY | 2 |
| SOURCE_DIRECTORY | 2 |
| PUBLIC_RSS_BY_BOARD | 1 |
| PUBLIC_XML_BY_BOARD | 1 |
| PUBLIC_JSON_BY_BOARD | 1 |
| TOKEN_REQUIRED_FROM_2027-02-10 | 1 |
| UNOFFICIAL_PUBLIC_WIDGET | 1 |
| PUBLIC_RSS | 1 |
| REGISTRATION_OR_KEY | 1 |
| API_KEY_PER_COUNTRY | 1 |
| APPNAME_APPROVAL | 1 |
| RESTRICTED_ACCESS | 1 |
| HN_ALGOLIA_DISCOVERY | 1 |
| PUBLIC_API_CLAIM_REQUIRES_VALIDATION | 1 |
| ATS_BOARD_CATALOG | 1 |
| ATS_BOARD_CATALOG_CSV | 1 |
| ATS_BOARD_CATALOG_JSON | 1 |
| COMPANY_ATS_DIRECTORY | 1 |
| JOB_AGGREGATOR_SOURCE_CODE | 1 |
| SOURCE_INDEX | 1 |
| OPEN_DATA_SNAPSHOTS | 1 |
| ATS_API_GUIDE | 1 |
| SCRAPER_SOURCE_CODE | 1 |
| UNOFFICIAL_CONNECTOR_EXAMPLE | 1 |
| REMOTE_COMPANY_DIRECTORY | 1 |
| UNOFFICIAL_GOV_API_WRAPPER | 1 |
| SEMANTIC_SCHEMA | 1 |
| PUBLIC_EVENT_FEED | 1 |
| DO_NOT_USE | 1 |
| PUBLIC_DATA_FEED | 1 |
| APPROVAL_REQUIRED | 1 |
| PARTNER_REGISTRATION | 1 |
| ACCOUNT_REQUIRED | 1 |
| OFFICIAL_PDF_BULLETIN | 1 |
| OFFICIAL_BULLETIN | 1 |
| OFFICIAL_DIRECTORY | 1 |
| DIRECTORY_ONLY | 1 |
| PARTIAL_DATA_OR_PAID_API | 1 |
| EXTERNAL_PAID_ACTOR | 1 |
| OPEN_SOURCE_OR_ACTOR | 1 |
| PUBLIC_SNAPSHOT | 1 |
| COMMERCIAL_FEED_DIRECTORY | 1 |
| DISCOVERY_DIRECTORY | 1 |
| OPEN_SOURCE_REFERENCE | 1 |
