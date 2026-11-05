# Relatório final da especificação V2

Números desta máquina em 05/11/2026 23:56 UTC, lidos dos arquivos gerados e da API local. Nada aqui é estimativa: o que não foi executado está dito como não executado.

## Fontes

| | Quantas |
|---|---|
| Plataformas no registro (configuradas) | 46 |
| Com conector no app | 43 |
| Ativas | 21 |
| Experimentais (endpoint observado) | 18 |
| Esperando credencial da pessoa | 4 |
| Só com parceria | 1 |
| Bloqueadas por política | 2 |
| Sondadas nesta execução | 38 |
| Funcionando (responderam com vaga) | 32 |
| Latência da sondagem (mediana / p90 / máxima) | 1.35 s / 10.4 s / 36.4 s |

Pendentes por credencial: adzuna, jooble, usajobs, reliefweb. Nunca chamadas: linkedin, francetravail, gupy-api.

## Quadros de empresa (catálogos importados)

`data/ats-board-catalog.json`: 22179 linhas de 2 catálogos, 17763 quadros únicos (4416 nos dois). Validação ao vivo de uma amostra de 40: 38 com vaga, 2 vazios, 0 não encontrados. Quadro do catálogo é pista: entra na campanha só pelo Explorador.

| Plataforma | Quadros |
|---|---|
| greenhouse | 5992 |
| ashby | 3560 |
| lever | 2192 |
| workable | 1875 |
| workday | 1371 |
| recruitee | 1043 |
| personio | 866 |
| rippling | 864 |

## Vagas e elegibilidade de quem mora no Brasil

| | Quantas |
|---|---|
| Vagas únicas abertas | 143 |
| Com data da fonte | 143 |
| Empresas com vaga aberta | 88 |
| Quadros de empresa na campanha | 25 |
| Confirmadas para quem mora no Brasil | 77 |
| Possíveis (remotas sem restrição clara) | 46 |
| Restritas (fora da fila por padrão) | 6 |

## Países

Dos 249 países e territórios: 4 integrados (fonte do próprio país com conector e sondagem com vaga), 26 com vaga aberta vista e 147 não pesquisados. O detalhe por continente está em `docs/COVERAGE_GAPS.md`. Não é cobertura mundial completa.

## Custo e cotas

API paga: nenhuma. Nas últimas 24 h, 26 chamadas de IA pelas assinaturas (Claude e ChatGPT) e 229 no modelo local; 0 vieram do cache e 2 falharam.

## Testes

| Teste | Executado nesta sessão | Resultado |
|---|---|---|
| Unidade (registro, portão, cobertura, elegibilidade, conformidade, fontes do governo e as anteriores) | sim | 175 de 175 passaram |
| E2E: só preparar não envia nada e pausa no que precisa da pessoa (inclui vaga encerrada virando Expired) | sim | passou (7 s) |
| E2E: cenários de formulário (Ashby, Lever, seletor que muda, MFA e prompt injection) | sim | passou (12 s) |
| E2E: aprovação, queda do processo e reconciliação | não (rodaram verdes antes, um por vez, para não estourar a memória) | não repetido |

## Verificações de aceitação da V2

1. Fonte de ATS e fonte governamental aberta com URL de candidatura real: sondadas ao vivo (Greenhouse, Lever, Ashby e NAV, JobTech, SeasonalJobs), com o link do anúncio. A campanha real não recebeu fontes de outros países, de propósito.
2. Encerrada não aparece aberta: a vaga do ATS falso que sai do ar vira Expired (E2E), e o anúncio INACTIVE da NAV fecha a vaga existente sem criar linha (teste de unidade).
3. Fonte restrita fica desligada: France Travail e LinkedIn bloqueadas e Gupy de empregador só com parceria; o portão impede a coleta e a sondagem nunca as chama (teste de unidade).
4. Importação histórica do Open Jobs: não executada. O dataset encerrou em 10/10/2026 e tem dezenas de GB; fica em `docs/NEXT_SOURCES.md` (Histórico), com a regra de não marcar OPEN sem revalidar.
5. Base dos 249 países sem inventar: existe inteira, e país não pesquisado fica UNRESEARCHED sem registro (teste de unidade).
6. Números separados: as tabelas acima e `docs/COVERAGE_GAPS.md`.
7. Lacunas por continente e país: `docs/COVERAGE_GAPS.md`.

## O que falta

- Credenciais da pessoa: Adzuna, Jooble, USAJOBS, ReliefWeb (e Brave e GitHub para descobrir mais).
- Parcerias e pedidos de acesso: Dinamarca (Jobnet), Japão (Hello Work), Coreia (Work24), África do Sul (ESSA) e os outros de `docs/NEXT_SOURCES.md`.
- Importação histórica do Open Jobs em streaming, sem OPEN até revalidar.
- Países não pesquisados: o caminho está em `docs/COVERAGE_GAPS.md` (WAPES, portal nacional, documentação, termos, sondagem).
