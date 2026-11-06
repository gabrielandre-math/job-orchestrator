# Relatório final da especificação V2

Números desta máquina em 06/11/2026 01:57 UTC, lidos dos arquivos gerados e da API local. Nada aqui é estimativa: o que não foi executado está dito como não executado.

## Fontes

| | Quantas |
|---|---|
| Plataformas no registro (configuradas) | 52 |
| Com conector no app | 48 |
| Ativas | 24 |
| Experimentais (endpoint observado) | 20 |
| Esperando credencial da pessoa | 4 |
| Só com parceria | 2 |
| Bloqueadas por política | 2 |
| Sondadas nesta execução | 44 |
| Funcionando (responderam com vaga) | 37 |
| Latência da sondagem (mediana / p90 / máxima) | 1.55 s / 12.8 s / 37.2 s |

Pendentes por credencial: adzuna, jooble, usajobs, reliefweb. Nunca chamadas: mcf, linkedin, francetravail, gupy-api.

## Quadros de empresa (catálogos importados)

`data/ats-board-catalog.json`: 97839 linhas de 3 catálogos, 34880 quadros únicos (12205 em mais de um catálogo). Validação ao vivo de uma amostra de 33: 27 com vaga, 6 vazios, 0 não encontrados. Quadro do catálogo é pista: entra na campanha só pelo Explorador.

| Plataforma | Quadros |
|---|---|
| greenhouse | 7223 |
| workable | 7051 |
| ashby | 3888 |
| personio | 3523 |
| breezy | 3286 |
| recruitee | 2733 |
| lever | 2670 |
| workday | 1371 |
| smartrecruiters | 1304 |
| teamtailor | 967 |
| rippling | 864 |

## Vagas e elegibilidade de quem mora no Brasil

| | Quantas |
|---|---|
| Vagas únicas abertas | 651 |
| Com data da fonte | 648 |
| Empresas com vaga aberta | 395 |
| Quadros de empresa na campanha | 58 |
| Confirmadas para quem mora no Brasil | 241 |
| Possíveis (remotas sem restrição clara) | 152 |
| Restritas (fora da fila por padrão) | 10 |

## Países

Dos 249 países e territórios: 6 integrados (fonte do próprio país com conector e sondagem com vaga), 48 com vaga aberta vista e 137 não pesquisados. O detalhe por continente está em `docs/COVERAGE_GAPS.md`. Não é cobertura mundial completa.

## Custo e cotas

API paga: nenhuma. Nas últimas 24 h, 30 chamadas de IA pelas assinaturas (Claude e ChatGPT) e 414 no modelo local; 0 vieram do cache e 2 falharam.

## Testes

| Teste | Executado nesta sessão | Resultado |
|---|---|---|
| Unidade (registro, portão, cobertura, elegibilidade, conformidade, fontes do governo e do mundo, histórico, LinkedIn, caçador, quadro e as anteriores) | sim | 218 de 218 passaram |
| Integração com o Postgres de teste: import histórico e revalidação (HistoryDbTests) | sim | passou |
| E2E: só preparar não envia nada e pausa no que precisa da pessoa (inclui vaga encerrada virando Expired) | sim | passou (7 s) |
| E2E: cenários de formulário (Ashby, Lever, seletor que muda, MFA e prompt injection) | sim | passou (12 s) |
| E2E: aprovação, queda do processo e reconciliação | não (rodaram verdes antes, um por vez, para não estourar a memória) | não repetido |

## Verificações de aceitação da V2

1. Fonte de ATS e fonte governamental aberta com URL de candidatura real: sondadas ao vivo (Greenhouse, Lever, Ashby e NAV, JobTech, SeasonalJobs), com o link do anúncio. A campanha real não recebeu fontes de outros países, de propósito.
2. Encerrada não aparece aberta: a vaga do ATS falso que sai do ar vira Expired (E2E), e o anúncio INACTIVE da NAV fecha a vaga existente sem criar linha (teste de unidade).
3. Fonte restrita fica desligada: France Travail e LinkedIn bloqueadas e Gupy de empregador só com parceria; o portão impede a coleta e a sondagem nunca as chama (teste de unidade).
4. Import histórico sem OPEN até revalidar: demonstrado. `jo history import` lê Parquet por grupo de linhas e CSV registro a registro, e o .tar membro a membro dentro do orçamento de disco; tudo entra como UNVERIFIED. Teste de unidade (Parquet e CSV no formato do export do Open Jobs) e de integração no Postgres (5 linhas importadas, nenhuma vaga na fila; a revalidação pelo quadro ao vivo abriu 1, fechou 1 e deixou 2 com o motivo). O snapshot real de 31 GB estava fora do ar (500, error code 1101, em 05/11 entre 21:07 e 21:09); a lista CC0 de quadros vivos do Open Jobs entrou no catálogo.
5. Base dos 249 países sem inventar: existe inteira, e país não pesquisado fica UNRESEARCHED sem registro (teste de unidade).
6. Números separados: as tabelas acima e `docs/COVERAGE_GAPS.md`.
7. Lacunas por continente e país: `docs/COVERAGE_GAPS.md`.

## Depois da V2 (05/11, noite)

- Fontes novas conferidas ao vivo: República Tcheca (MPSV, dados abertos oficiais, contatos fora, cidade pelo código oficial), Letônia (NVA, CC0), Pinpoint (feed oficial), BambooHR e Recruiterbox (experimentais), startups do YC e a lista de quadros vivos do Open Jobs como pistas. Singapura (MyCareersFuture) só com autorização: os termos da WSG proíbem raspar.
- Caçador com o Llama local: o modelo monta o plano e o Common Crawl, o Wikidata e o Brave (com chave) procuram; as pistas vão para o Explorador, que testa antes de pôr na campanha. Primeira caçada real: 83 sites de empresa pelo Wikidata; o índice do Common Crawl estava fora do ar (504 até em consulta simples).
- Conectar LinkedIn pela cópia oficial dos dados (o app acha o ZIP na pasta de downloads e lê só os CSVs do perfil), currículo e LinkedIn somados sem repetir, conector MCP (job-orchestrator --mcp), lacunas da vaga, força do perfil, tom e idioma da carta, quadro de candidaturas até a oferta e currículo base com quatro temas.

## O que falta

- Credenciais da pessoa: Adzuna, Jooble, USAJOBS, ReliefWeb (e Brave e GitHub para descobrir mais).
- Parcerias e pedidos de acesso: Dinamarca (Jobnet), Japão (Hello Work), Coreia (Work24), África do Sul (ESSA) e os outros de `docs/NEXT_SOURCES.md`.
- O snapshot de 31 GB do Open Jobs, quando o servidor deles voltar: `jo history import https://backend.dehnbostele.workers.dev/data/exports/open-jobs-latest.tar`.
- Autorização por escrito da Workforce Singapore para o MyCareersFuture; a API da agência de emprego alemã (BA) recusou (403).
- Países não pesquisados: o caminho está em `docs/COVERAGE_GAPS.md` (WAPES, portal nacional, documentação, termos, sondagem).
