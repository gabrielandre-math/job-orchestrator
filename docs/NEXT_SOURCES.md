# Próximas fontes

Gerado por `jo sources next` em 06/11/2026 01:56 UTC. São os 283 registros do catálogo V2 (de 321) que ainda não
viraram plataforma no registro (`data/source-registry.json`), em ordem de prioridade do catálogo (P0 primeiro). Nenhum foi dado como funcionando: cada um
precisa de documentação, termos e sondagem antes de entrar. Agências do diretório WAPES ficam juntas no fim, por país.

## Pedir acesso (7)

Pedir pelo canal oficial (cadastro de desenvolvedor, appname, contrato de parceria) com nome, uso pessoal e não comercial; até a resposta, fica CREDENTIALS_REQUIRED ou PARTNER_ONLY.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P1 | Denmark Jobnet JobannonceService | DK | PARTNER_REGISTRATION | DOCUMENTED | https://star.dk/digital-service/saadan-arbejder-vi-med-it-i-styrelsen/oversigt-over-digitale-platforme-for-eksterne-brugere/styrelsen-for-arbejdsmarked-og-rekrutterings-webservices-og-wiki/jobnet-webservice |
| P1 | Japan Hello Work External Job Information API | JP | APPROVAL_REQUIRED | DOCUMENTED | https://api-catalog.e-gov.go.jp/info/en/apicatalog/view/57 |
| P1 | Korea Work24 Job Listing Endpoint Docs | KR | API_KEY_REGISTRATION | DOCUMENTED | https://www.work24.go.kr/cm/e/a/0110/selectOpenApiSvcInfo.do?apiSvcId=000000000000000000000000000060&fullApiSvcId=000000000000000000000000000000%5E000000000000000000000000000001&upprApiSvcId=000000000000000000000000000059 |
| P1 | Korea Work24 Job Postings API | KR | API_KEY_REGISTRATION | DOCUMENTED | https://www.work24.go.kr/cm/e/a/0110/selectOpenApiSvcInfo.do |
| P2 | Employment Services South Africa ESSA | ZA | ACCOUNT_REQUIRED | DOCUMENTED | https://essa.labour.gov.za/EssaOnline/WebBeans/ |
| P2 | Reed UK Jobs API | UK | API_KEY | REQUIRES_VALIDATION | https://www.reed.co.uk/developers |
| P2 | Workable SPI | GLOBAL | AUTHENTICATED_PARTNER_API | DOCUMENTED | https://help.workable.com/hc/en-us/articles/115012771647-Using-the-Workable-API-to-create-a-careers-page |

## Portal sem API conhecida (11)

Procurar feed, RSS, XML ou API na documentação; se não houver, conferir termos e robots.txt; pedir feed ou parceria ao órgão. Login, CAPTCHA ou antirrobô: AUTH_REQUIRED ou POLICY_BLOCKED, sem contorno.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P1 | Colombia SENA Agencia Pública de Empleo | CO | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://www.sena.edu.co/empleabilidad |
| P1 | EURAXESS Research Jobs Search | EU | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://euraxess.ec.europa.eu/jobs/search |
| P1 | Kenya National Employment Authority NEAIMS | KE | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://neaims.go.ke/ |
| P1 | Papua New Guinea Public Service Recruitment | PG | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://jobs.dpm.gov.pg/ |
| P1 | Samoa Ministry of Commerce Jobs Portal | WS | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://jobs.mcil.gov.ws/ |
| P1 | South Africa Government weekly vacancy circular | ZA | OFFICIAL_PDF_BULLETIN | DOCUMENTED | https://www.gov.za/government-jobs-week |
| P2 | EURAXESS Job Opportunities | EU | WEBSITE_ONLY | DOCUMENTED | https://euraxess.ec.europa.eu/jobs |
| P2 | Oman Government vacant positions | OM | OFFICIAL_JOB_PORTAL | DOCUMENTED | https://gov.om/en/w/apply-for-vacant-government-positions |
| P2 | Qualify Africa opportunity by eligible country | AFRICA | WEBSITE_ONLY | DOCUMENTED | https://qualifyafrica.com/hubs |
| P2 | Samoa Public Service Commission Employment Circular | WS | OFFICIAL_BULLETIN | DOCUMENTED | https://www.psc.gov.ws/ |
| P2 | SPTO Jobs Link Pacific Opportunities | OCEANIA | WEBSITE_ONLY | DOCUMENTED | https://sptojobslink.com/ |

## Histórico (6)

Importar em streaming, com teto de disco e RAM, como histórico: nenhuma vaga vira OPEN sem revalidação na fonte viva.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P0 | ATS Scrapers | GLOBAL | ATS_SCRAPER_AND_SNAPSHOTS | REQUIRES_VALIDATION | https://github.com/kalil0321/ats-scrapers |
| P0 | Open Jobs (Elliott Dehnbostel) frozen CC0 corpus | GLOBAL | SNAPSHOT_ONLY | DOCUMENTED | https://github.com/elliottdehn/open-jobs |
| P1 | ATS Scrapers Continuity Fork | GLOBAL | ATS_SCRAPER_AND_SNAPSHOTS | REQUIRES_VALIDATION | https://github.com/Continuity1/d1_ats_scrapers |
| P1 | ConorsCode Open Jobs Data | GLOBAL | PUBLIC_SNAPSHOT | DOCUMENTED | https://github.com/ConorsCode/open-jobs-data |
| P1 | Open Jobs archived public data | GLOBAL | SNAPSHOT_ONLY | DOCUMENTED | https://backend.dehnbostele.workers.dev/data/ |
| P1 | Open Jobs Data | GLOBAL | OPEN_DATA_SNAPSHOTS | REQUIRES_VALIDATION | https://github.com/ConorsCode/open-jobs-data |

## Pista do Explorador (168)

Serve para achar empresas, quadros e portais, não para coletar: o Explorador usa e cada quadro achado passa pela sondagem antes de entrar.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P0 | ATS Job Board APIs Guide | GLOBAL | ATS_API_GUIDE | REQUIRES_VALIDATION | https://github.com/Mersetti/ats-job-board-apis |
| P0 | Freehire Source Index | GLOBAL | SOURCE_INDEX | REQUIRES_VALIDATION | https://github.com/strelov1/freehire/blob/main/docs/sources.md |
| P0 | Job Board Directory 12k | GLOBAL | ATS_BOARD_CATALOG | REQUIRES_VALIDATION | https://github.com/kalebconfer-sys/job-board-directory |
| P0 | Job Board Directory CSV | GLOBAL | ATS_BOARD_CATALOG_CSV | REQUIRES_VALIDATION | https://kalebconfer-sys.github.io/job-board-directory/job-board-directory.csv |
| P0 | Job Board Directory JSON | GLOBAL | ATS_BOARD_CATALOG_JSON | REQUIRES_VALIDATION | https://kalebconfer-sys.github.io/job-board-directory/job-board-directory.json |
| P0 | LastroundAI ATS Directory | GLOBAL | COMPANY_ATS_DIRECTORY | REQUIRES_VALIDATION | https://github.com/fyrosofttech/lastroundai-hiring-data/tree/main/ats-directory |
| P0 | WAPES Public Employment Services global member directory | GLOBAL | DIRECTORY_ONLY | DOCUMENTED | https://wapes.org/members-2/?lang=en |
| P1 | African Development Bank Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.afdb.org/en/about-us/careers |
| P1 | Arc Dev | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://arc.dev/remote-jobs |
| P1 | Asian Development Bank Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.adb.org/work-with-us/careers |
| P1 | BairesDev | LATAM | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.bairesdev.com/careers/ |
| P1 | Brazil CIEE | BR | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://portal.ciee.org.br/ |
| P1 | Brazil SINE Emprega Brasil | BR | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://empregabrasil.mte.gov.br/ |
| P1 | Built In | USA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://builtin.com/jobs |
| P1 | CI&T | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://ciandt.com/ |
| P1 | CWJobs | UK | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.cwjobs.co.uk/ |
| P1 | Dice | USA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.dice.com/ |
| P1 | EBRD Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.ebrd.com/careers.html |
| P1 | EPAM | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.epam.com/careers |
| P1 | EU EPSO Careers | EUROPE | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://eu-careers.europa.eu/ |
| P1 | EU EURAXESS Jobs | EUROPE | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://euraxess.ec.europa.eu/jobs |
| P1 | EURES EU | EUROPE | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://eures.europa.eu/ |
| P1 | Globant | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://career.globant.com/ |
| P1 | Gupy Jobs | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://portal.gupy.io/ |
| P1 | IDB Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.iadb.org/en/who-we-are/careers |
| P1 | IMF Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.imf.org/en/About/Recruitment |
| P1 | Impactpool | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.impactpool.org/search |
| P1 | Indeed | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.indeed.com/ |
| P1 | ITJobs Portugal | PORTUGAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.itjobs.pt/ |
| P1 | JapanDev | JAPAN | DISCOVERY_ONLY | DISCOVERY_TARGET | https://japan-dev.com/jobs |
| P1 | Just Join IT | POLAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://justjoin.it/ |
| P1 | Landing Jobs | PORTUGAL/EUROPE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://landing.jobs/ |
| P1 | LinkedIn Jobs | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.linkedin.com/jobs/ |
| P1 | No Fluff Jobs | EUROPE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://nofluffjobs.com/ |
| P1 | NoDesk | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://nodesk.co/remote-jobs/ |
| P1 | OECD Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.oecd.org/careers/ |
| P1 | OfferZen | SOUTH_AFRICA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.offerzen.com/ |
| P1 | Otta / Welcome to the Jungle | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.welcometothejungle.com/ |
| P1 | Programathor | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://programathor.com.br/ |
| P1 | ReliefWeb Jobs | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://reliefweb.int/jobs |
| P1 | Remotar | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://remotar.com.br/ |
| P1 | Remote in Tech Companies | GLOBAL | REMOTE_COMPANY_DIRECTORY | REQUIRES_VALIDATION | https://github.com/remoteintech/remote-jobs |
| P1 | Remote.co | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://remote.co/remote-jobs/ |
| P1 | SwissDevJobs | SWITZERLAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://swissdevjobs.ch/ |
| P1 | Thoughtworks | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.thoughtworks.com/careers |
| P1 | TokyoDev | JAPAN | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.tokyodev.com/jobs |
| P1 | Torre | LATAM | DISCOVERY_ONLY | DISCOVERY_TARGET | https://torre.ai/ |
| P1 | Turing | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.turing.com/ |
| P1 | UAE Official Government Job Portals Directory | AE | OFFICIAL_DIRECTORY | DOCUMENTED | https://u.ae/en/information-and-services/jobs/working-in-uae-government-sector/search-for-a-job-in-government-sector |
| P1 | UN Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://careers.un.org/ |
| P1 | UNDP Jobs | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://jobs.undp.org/ |
| P1 | UNESCO Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://careers.unesco.org/ |
| P1 | UNHCR Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.unhcr.org/careers |
| P1 | UNICEF Jobs | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://jobs.unicef.org/ |
| P1 | UNOPS Jobs | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://jobs.unops.org/ |
| P1 | US USAJOBS | USA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.usajobs.gov/ |
| P1 | Vagas Com | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.vagas.com.br/ |
| P1 | Wellfound Brazil | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://wellfound.com/location/brazil |
| P1 | Wellfound Jobs | GLOBAL | DISCOVERY_ONLY | DOCUMENTED | https://wellfound.com/jobs |
| P1 | WHO Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://careers.who.int/ |
| P1 | Working Nomads | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.workingnomads.com/jobs |
| P1 | World Bank Careers | GLOBAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.worldbank.org/en/about/careers |
| P1 | YC Jobs | GLOBAL | DISCOVERY_ONLY | DOCUMENTED | https://www.ycombinator.com/jobs |
| P2 | 99jobs | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://99jobs.com/ |
| P2 | Abu Dhabi government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | Africa No Filter Opportunity Hub directory | AFRICA | DISCOVERY_DIRECTORY | DOCUMENTED | https://opportunity.africanofilter.org/directory |
| P2 | Ajman Kawader government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | Apec France | FRANCE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.apec.fr/ |
| P2 | Argentina Portal Empleo | ARGENTINA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.portalempleo.gob.ar/ |
| P2 | Australia Internet Vacancy Index data | AU | DISCOVERY_ONLY | DOCUMENTED | https://www.jobsandskills.gov.au/data/internet-vacancy-index |
| P2 | Australia Total New Vacancies | AU | DISCOVERY_ONLY | DOCUMENTED | https://www.jobsandskills.gov.au/data/total-new-vacancies |
| P2 | Australia Workforce Australia | AUSTRALIA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.workforceaustralia.gov.au/ |
| P2 | Bayt | MENA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.bayt.com/ |
| P2 | BDjobs Bangladesh | BANGLADESH | DISCOVERY_ONLY | DISCOVERY_TARGET | https://bdjobs.com/ |
| P2 | Braintrust | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.usebraintrust.com/ |
| P2 | BrighterMonday Kenya | KENYA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.brightermonday.co.ke/ |
| P2 | Bumeran Argentina | ARGENTINA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.bumeran.com.ar/ |
| P2 | Canada Job Bank | CANADA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.jobbank.gc.ca/ |
| P2 | Canada Job Bank international applicants filter | CA | DISCOVERY_ONLY | DOCUMENTED | https://www.jobbank.gc.ca/ |
| P2 | Catho | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.catho.com.br/ |
| P2 | Chile Bolsa Nacional de Empleo | CHILE | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.bne.gob.cl/ofertas |
| P2 | Computrabajo | LATAM | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.computrabajo.com/ |
| P2 | Contra | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://contra.com/ |
| P2 | Cutshort India | INDIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://cutshort.io/ |
| P2 | CV Library | UK | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.cv-library.co.uk/ |
| P2 | Denmark Jobnet | DENMARK | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://job.jobnet.dk/ |
| P2 | Dubai Careers government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://www.dubaicareers.ae/ |
| P2 | El Empleo Colombia | COLOMBIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.elempleo.com/ |
| P2 | EthicalJobs Australia | AUSTRALIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.ethicaljobs.com.au/ |
| P2 | Finland Tyomarkkinatori | FINLAND | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://tyomarkkinatori.fi/ |
| P2 | Finn Norway | NORWAY | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.finn.no/job/ |
| P2 | FlexJobs | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.flexjobs.com/ |
| P2 | Foundit India | INDIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.foundit.in/ |
| P2 | France Travail | FRANCE | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.francetravail.fr/ |
| P2 | Fujairah government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | Fuzu | AFRICA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.fuzu.com/ |
| P2 | GeekHunter | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.geekhunter.com.br/ |
| P2 | Germany Bundesagentur Jobsuche | GERMANY | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.arbeitsagentur.de/jobsuche/ |
| P2 | Glassdoor | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.glassdoor.com/ |
| P2 | Glints | ASIA_PACIFIC | DISCOVERY_ONLY | DISCOVERY_TARGET | https://glints.com/ |
| P2 | Google Jobs | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.google.com/search?q=jobs |
| P2 | GovJobs UK source coverage | GB | SOURCE_DIRECTORY | DOCUMENTED | https://govjobs.uk/coverage |
| P2 | GulfTalent | MENA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.gulftalent.com/ |
| P2 | Hark jobs source coverage index | GLOBAL | SOURCE_DIRECTORY | DOCUMENTED | https://www.harkjobs.com/sources |
| P2 | HelloWork France | FRANCE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.hellowork.com/ |
| P2 | Hong Kong Jobs Gov | HONG_KONG | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.jobs.gov.hk/ |
| P2 | India NCS | INDIA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.ncs.gov.in/ |
| P2 | InfoJobs Brazil | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.infojobs.com.br/ |
| P2 | Instahyre India | INDIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.instahyre.com/ |
| P2 | Ireland JobsIreland | IRELAND | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://jobsireland.ie/ |
| P2 | IrishJobs | IRELAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.irishjobs.ie/ |
| P2 | Japan HelloWork | JAPAN | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.hellowork.mhlw.go.jp/ |
| P2 | Jobberman Nigeria | NIGERIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.jobberman.com/ |
| P2 | Jobindex Denmark | DENMARK | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.jobindex.dk/ |
| P2 | JobKorea | SOUTH_KOREA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.jobkorea.co.kr/ |
| P2 | JobsDB | ASIA_PACIFIC | DISCOVERY_ONLY | DISCOVERY_TARGET | https://hk.jobsdb.com/ |
| P2 | JobStreet | ASIA_PACIFIC | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.jobstreet.com/ |
| P2 | Jora | MULTICOUNTRY | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.jora.com/ |
| P2 | Kalibrr | PHILIPPINES/SEA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.kalibrr.com/ |
| P2 | Kenya NEAIMS recruitment search | KE | DISCOVERY_ONLY | DOCUMENTED | https://recruitment.neaims.go.ke/ |
| P2 | Kenya Public Service Commission | KENYA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.publicservice.go.ke/ |
| P2 | Laborum Chile | CHILE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.laborum.cl/ |
| P2 | Malaysia MYFutureJobs | MALAYSIA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://myfuturejobs.gov.my/ |
| P2 | Morocco public recruitment competitions | MA | DISCOVERY_ONLY | DOCUMENTED | https://www.emploi-public.ma/ |
| P2 | Naukri India | INDIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.naukri.com/ |
| P2 | Naukrigulf | GULF | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.naukrigulf.com/ |
| P2 | Netherlands Werk NL | NETHERLANDS | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.werk.nl/ |
| P2 | New Zealand Public Service Jobs | NEW_ZEALAND | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://jobs.govt.nz/ |
| P2 | Norway NAV Jobs | NORWAY | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://arbeidsplassen.nav.no/ |
| P2 | OCC Mundial | MEXICO | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.occ.com.mx/ |
| P2 | Philippines PhilJobNet | PHILIPPINES | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://philjobnet.gov.ph/ |
| P2 | PNet | SOUTH_AFRICA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.pnet.co.za/ |
| P2 | Portugal IEFP | PORTUGAL | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://iefponline.iefp.pt/ |
| P2 | Pracuj Poland | POLAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.pracuj.pl/ |
| P2 | Ras Al Khaimah government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | Reed UK | UK | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.reed.co.uk/jobs |
| P2 | Rozee Pakistan | PAKISTAN | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.rozee.pk/ |
| P2 | Rwanda MIFOTRA Recruitment | RWANDA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://recruitment.mifotra.gov.rw/ |
| P2 | Saramin | SOUTH_KOREA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.saramin.co.kr/ |
| P2 | SEEK Australia | AUSTRALIA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.seek.com.au/ |
| P2 | SEEK New Zealand | NEW_ZEALAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.seek.co.nz/ |
| P2 | Sharjah government jobs | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | Singapore data.gov.sg open dataset catalog | SG | DISCOVERY_ONLY | DOCUMENTED | https://developers.data.gov.sg/ |
| P2 | South Africa DPSA | SOUTH_AFRICA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.dpsa.gov.za/ |
| P2 | South Africa Government Jobs Circular | SOUTH_AFRICA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.gov.za/government-jobs-week |
| P2 | South Africa Govt public service vacancy listing | ZA | DISCOVERY_ONLY | DOCUMENTED | https://www.gov.za/jobs |
| P2 | South Korea Work24 | SOUTH_KOREA | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.work24.go.kr/ |
| P2 | Spain Empleate SEPE | SPAIN | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://empleate.gob.es/ |
| P2 | SPTO Jobs Link for Pacific tourism | FJ | DISCOVERY_ONLY | DOCUMENTED | https://sptojobslink.com/ |
| P2 | StepStone | EUROPE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.stepstone.de/ |
| P2 | Sweden Platsbanken | SWEDEN | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://arbetsformedlingen.se/platsbanken/ |
| P2 | Switzerland Job Room | SWITZERLAND | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.job-room.ch/ |
| P2 | Taiwan Jobs | TAIWAN | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.taiwanjobs.gov.tw/ |
| P2 | Toptal | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.toptal.com/ |
| P2 | Totaljobs | UK | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.totaljobs.com/ |
| P2 | Trabajando Chile | CHILE | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.trabajando.cl/ |
| P2 | Trade Me Jobs | NEW_ZEALAND | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.trademe.co.nz/a/jobs |
| P2 | Trampos | BR | DISCOVERY_ONLY | DISCOVERY_TARGET | https://trampos.co/ |
| P2 | Trinidad and Tobago Ministry of Foreign Affairs careers | TT | DISCOVERY_ONLY | DOCUMENTED | https://foreign.gov.tt/about/ministry/vacancies/ |
| P2 | UAE Federal Government Careers | AE | DISCOVERY_ONLY | DOCUMENTED | https://u.ae/en/resources/government-jobs |
| P2 | UK API Catalogue | GB | DISCOVERY_ONLY | DOCUMENTED | https://www.api.gov.uk/ |
| P2 | UK Civil Service Jobs | UK | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://www.civilservicejobs.service.gov.uk/ |
| P2 | UK Find a Job | UK | DISCOVERY_ONLY | REQUIRES_VALIDATION | https://findajob.dwp.gov.uk/ |
| P2 | Upwork | GLOBAL | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.upwork.com/ |
| P2 | Wantedly Japan | JAPAN | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.wantedly.com/ |
| P2 | Wuzzuf | EGYPT | DISCOVERY_ONLY | DISCOVERY_TARGET | https://wuzzuf.net/ |
| P2 | XING Jobs | GERMANY | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.xing.com/jobs |
| P2 | ZipRecruiter | USA | DISCOVERY_ONLY | DISCOVERY_TARGET | https://www.ziprecruiter.com/ |

## Só referência (9)

Estudar o que faz e conferir a licença; nunca copiar código (regra do projeto). Endpoint não oficial só entra como EXPERIMENTAL depois de conferido.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P0 | Freehire Source Collectors | GLOBAL | JOB_AGGREGATOR_SOURCE_CODE | REQUIRES_VALIDATION | https://github.com/strelov1/freehire |
| P0 | Job Hunter Toolkit across ATS | GLOBAL | OPEN_SOURCE_CODE | DOCUMENTED | https://github.com/job-hunter-toolkit/job-hunter-toolkit |
| P1 | ATS Job Scraper 14 platforms | GLOBAL | OPEN_SOURCE_OR_ACTOR | DOCUMENTED | https://github.com/sukhhdipsingh/ats-job-scraper |
| P1 | GetAnything ATS Jobs Scraper | GLOBAL | OPEN_SOURCE_CODE | DOCUMENTED | https://github.com/GetAnything-1/ats-jobs-scraper |
| P1 | Job Forager multi-source CLI | GLOBAL | OPEN_SOURCE_CODE | DOCUMENTED | https://github.com/nipunsomani/job-forager |
| P2 | BundesAPI Job Search | GLOBAL | UNOFFICIAL_GOV_API_WRAPPER | REQUIRES_VALIDATION | https://github.com/bundesAPI/jobsuche-api |
| P2 | Busca Gupy InHire | GLOBAL | UNOFFICIAL_CONNECTOR_EXAMPLE | REQUIRES_VALIDATION | https://github.com/vonrondow/busca-vagas-gupy-inhire |
| P2 | JobSpy | GLOBAL | SCRAPER_SOURCE_CODE | REQUIRES_VALIDATION | https://github.com/speedyapply/JobSpy |
| P2 | JobSpy scraping dependency / risk review | GLOBAL | OPEN_SOURCE_REFERENCE | REQUIRES_VALIDATION | https://github.com/Bunsly/JobSpy |

## Fora: pago ou comercial (3)

O projeto não usa API paga.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P1 | Headcount Labs Jobs Dataset, API & MCP | GLOBAL | PARTIAL_DATA_OR_PAID_API | DOCUMENTED | https://github.com/HeadcountLabs/job-postings |
| P2 | ATSFeeds Commercial ATS Feed Directory | GLOBAL | COMMERCIAL_FEED_DIRECTORY | DOCUMENTED | https://www.atsfeeds.com/ |
| P2 | Hiver company jobs from 17 ATS | GLOBAL | EXTERNAL_PAID_ACTOR | DOCUMENTED | https://github.com/hiver-data/all-open-jobs-by-company |

## Fora: não usar (1)

O próprio catálogo marca como não usar.

| Prioridade | Fonte | Escopo | Método | Estado da verificação | Endereço |
|---|---|---|---|---|---|
| P3 | Norway NAV former API (deprecated) | NO | DO_NOT_USE | DOCUMENTED | https://github.com/navikt/pam-public-feed |

## Agências públicas de emprego do diretório WAPES (78)

Uma agência não implica API nem vaga aberta. Caminho: abrir o portal da agência, procurar busca real de vagas e documentação de API ou feed, conferir termos e só então criar a fonte.

| País | Agência | Endereço |
|---|---|---|
| AL | Albania — AKPA (National agency for employment and skills) | https://wapes.org/members-2/?lang=en |
| AT | Austria — AMS (Arbeitsmarktservice Oesterreich) | https://wapes.org/members-2/?lang=en |
| AZ | Azerbaijan — APEA (Azerbaijan Public Employment Agency) | https://wapes.org/members-2/?lang=en |
| BA | Bosnia and Herzegovina — LEA (Labour and Employment Agency) | https://wapes.org/members-2/?lang=en |
| BE | Belgium — VDAB (VDAB) | https://wapes.org/members-2/?lang=en |
| BE | Belgium — ACTIRIS (Actiris) | https://wapes.org/members-2/?lang=en |
| BE | Belgium — LE FOREM (Le Forem) | https://wapes.org/members-2/?lang=en |
| BE | Belgium — ADG (Arbeitsamt der Deutschsprachigen Gemeinschaft) | https://wapes.org/members-2/?lang=en |
| BF | Burkina Faso — ANPE (ANPE Burkina Faso) | https://wapes.org/members-2/?lang=en |
| BG | Bulgaria — NEA (National Employment Agency) | https://wapes.org/members-2/?lang=en |
| BJ | Benin — ANPE (Agence Nationale pour l'Emploi) | https://wapes.org/members-2/?lang=en |
| CA | Canada — Emploi Quebec (Emploi Quebec) | https://wapes.org/members-2/?lang=en |
| CA | Canada — ESDC (Employment and Social Development Canada) | https://wapes.org/members-2/?lang=en |
| CD | Democratic Republic of the Congo — ONEM (Office National de l'Emploi) | https://wapes.org/members-2/?lang=en |
| CF | Central African Republic — ACFPE (Agence Centrafricaine pour la Formation Professionelle et l'Emploi) | https://wapes.org/members-2/?lang=en |
| CG | Republic of the Congo — ACPE (Agence Congolaise Pour l'Emploi) | https://wapes.org/members-2/?lang=en |
| CH | Switzerland — SECO (Secretariat d'Etat a l'Economie) | https://wapes.org/members-2/?lang=en |
| CI | Côte d'Ivoire — Agence Emploi Jeunes (Agence Emploi Jeunes) | https://wapes.org/members-2/?lang=en |
| CM | Cameroon — FNE (Fonds National de l'Emploi) | https://wapes.org/members-2/?lang=en |
| CN | China — MOHRSS (Ministry of Human Resources and Social Security) | https://wapes.org/members-2/?lang=en |
| CY | Cyprus — DOL (Department of Labour) | https://wapes.org/members-2/?lang=en |
| DE | Germany — BA (Bundesagentur fuer Arbeit) | https://wapes.org/members-2/?lang=en |
| DJ | Djibouti — ANEFIP (Agence Nationale de l'Emploi, de la Formation et de l'Insertion Professionnelle) | https://wapes.org/members-2/?lang=en |
| DK | Denmark — STAR (Danish Agency for Labour Market and Recruitment) | https://wapes.org/members-2/?lang=en |
| DO | Dominican Republic — SET (Secretaria de Estado de Trabajo) | https://wapes.org/members-2/?lang=en |
| DZ | Algeria — ANEM (Agence nationale de l'emploi) | https://wapes.org/members-2/?lang=en |
| EE | Estonia — Eesti Tootukassa (Estonian Unemployment Insurance Fund) | https://wapes.org/members-2/?lang=en |
| ES | Spain — SEPE (Servicio Publico de Empleo Estatal) | https://wapes.org/members-2/?lang=en |
| FI | Finland — MINLA (Ministry of Economic Affairs and Employment) | https://wapes.org/members-2/?lang=en |
| FR | France — France Travail (France Travail) | https://wapes.org/members-2/?lang=en |
| GA | Gabon — PNPE (Pole National de Promotion de l'Emploi) | https://wapes.org/members-2/?lang=en |
| GB | United Kingdom — Jobcenter Plus (Jobcenter Plus) | https://wapes.org/members-2/?lang=en |
| GN | Guinea — AGUIPE (Agence Guineenne pour la Promotion de l'Emploi) | https://wapes.org/members-2/?lang=en |
| GR | Greece — DYPA (Greek Public Employment Service) | https://wapes.org/members-2/?lang=en |
| HN | Honduras — Direccion General de Empleo (Secretaria de Trabajo y Seguridad Social) | https://wapes.org/members-2/?lang=en |
| HR | Croatia — CES (Croatian Employment Service) | https://wapes.org/members-2/?lang=en |
| HU | Hungary — NFSZ (Nemzeti Foglalkoztatasi Szolgalat) | https://wapes.org/members-2/?lang=en |
| IE | Ireland — Intreo (Department of Social Protection) | https://wapes.org/members-2/?lang=en |
| IT | Italy — Ministry of Labour (Ministero del Lavoro e delle Politiche Sociali) | https://wapes.org/members-2/?lang=en |
| JP | Japan — MHLW (Ministry of Health Labour and Welfare) | https://wapes.org/members-2/?lang=en |
| KE | Kenya — NEA (National Employment Authority) | https://wapes.org/members-2/?lang=en |
| KH | Cambodia — NEA (National Employment Agency) | https://wapes.org/members-2/?lang=en |
| KR | South Korea — KEIS (Korea Employment Information Service) | https://wapes.org/members-2/?lang=en |
| KZ | Kazakhstan — ENBEK (Workforce Development Center) | https://wapes.org/members-2/?lang=en |
| LT | Lithuania — UZT (Uzimtumo tarnyba) | https://wapes.org/members-2/?lang=en |
| LU | Luxembourg — ADEM (Agence pour le developpement de l'emploi) | https://wapes.org/members-2/?lang=en |
| MA | Morocco — ANAPEC (ANAPEC) | https://wapes.org/members-2/?lang=en |
| MD | Moldova — ANOFM (Agentia Nationala pentru Ocuparea Fortei de Munca) | https://wapes.org/members-2/?lang=en |
| ME | Montenegro — Employment Agency of Montenegro (Employment Agency of Montenegro) | https://wapes.org/members-2/?lang=en |
| MK | North Macedonia — Employment Service Agency (Employment Service Agency of the Republic of North Macedonia) | https://wapes.org/members-2/?lang=en |
| ML | Mali — ANPE (Agence Nationale Pour l'Emploi) | https://wapes.org/members-2/?lang=en |
| MR | Mauritania — TECHGHIL (TECHGHIL) | https://wapes.org/members-2/?lang=en |
| MT | Malta — JOBSPLUS (Jobsplus) | https://wapes.org/members-2/?lang=en |
| MY | Malaysia — PERKESO (Perkeso) | https://wapes.org/members-2/?lang=en |
| NA | Namibia — Labour Relations (Department of Labour Relations) | https://wapes.org/members-2/?lang=en |
| NE | Niger — ANPE (Agence Nationale pour la Promotion de l'Emploi) | https://wapes.org/members-2/?lang=en |
| NG | Nigeria — FMLE (Federal Ministry of Labour and Employment) | https://wapes.org/members-2/?lang=en |
| NL | Netherlands — UWV (UWV) | https://wapes.org/members-2/?lang=en |
| NO | Norway — NAV (Norwegian Labour and Welfare Service) | https://wapes.org/members-2/?lang=en |
| NZ | New Zealand — Ministry of Social Development (Ministry of Social Development) | https://wapes.org/members-2/?lang=en |
| PA | Panama — MITRADEL (Ministerio de Trabajo y Desarrollo Laboral) | https://wapes.org/members-2/?lang=en |
| PE | Peru — MTPE (Ministerio de Trabajo y Promocion del Empleo) | https://wapes.org/members-2/?lang=en |
| PH | Philippines — DOLE (Department of Labor and Employment) | https://wapes.org/members-2/?lang=en |
| PS | Palestine — Ministry of Labour (Ministry of Labour) | https://wapes.org/members-2/?lang=en |
| PT | Portugal — IEFP (Instituto do Emprego e Formacao Profissional) | https://wapes.org/members-2/?lang=en |
| RO | Romania — ANOFM (Agentia Nationala pentru Ocupare Fortei de Munca) | https://wapes.org/members-2/?lang=en |
| RS | Serbia — NES (National Employment Service) | https://wapes.org/members-2/?lang=en |
| SE | Sweden — Arbetsformedlingen (Arbetsformedlingen) | https://wapes.org/members-2/?lang=en |
| SI | Slovenia — Employment Services of Slovenia (Employment Services of Slovenia) | https://wapes.org/members-2/?lang=en |
| SN | Senegal — ANPEJ (Agence Nationale Pour la Promotion de l'Emploi Jeunes) | https://wapes.org/members-2/?lang=en |
| TD | Chad — ONAPE (Office National pour la Promotion de l'Emploi) | https://wapes.org/members-2/?lang=en |
| TG | Togo — ANPE (Agence Nationale Pour l'Emploi) | https://wapes.org/members-2/?lang=en |
| TN | Tunisia — ANETI (Agence Nationale pour l'Emploi et le Travail Independant) | https://wapes.org/members-2/?lang=en |
| TR | Turkey — ISKUR (ISKUR) | https://wapes.org/members-2/?lang=en |
| UA | Ukraine — State Employment Center (State Employment Center) | https://wapes.org/members-2/?lang=en |
| UY | Uruguay — MTSS (Ministerio de Trabajo y Seguridad Social) | https://wapes.org/members-2/?lang=en |
| VN | Vietnam — MOLISA (Ministry of Labour Invalids and Social Affairs) | https://wapes.org/members-2/?lang=en |
| ZW | Zimbabwe — Ministry of Public Service (Ministry of Public Service, Labour and Social Welfare) | https://wapes.org/members-2/?lang=en |

## Como pedir parceria ou acesso

Escrever pelo canal oficial da fonte (formulário de desenvolvedor, e-mail de dados abertos ou de parcerias) dizendo: uso pessoal e não comercial, leitura só
dos anúncios públicos, frequência baixa (a cada poucas horas), crédito à fonte em cada vaga, nada de dado pessoal guardado e remoção imediata de anúncio encerrado.
A chave ou o appname recebido entra em Fontes › Chaves de API (gnome-keyring), e a plataforma passa de CREDENTIALS_REQUIRED para ACTIVE na próxima sondagem.
