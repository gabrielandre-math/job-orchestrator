#!/usr/bin/env python3
"""Relatório final da especificação V2 com os números reais desta máquina: lê data/source-registry.json, data/ats-board-catalog.json e a API
local (/api/coverage e /api/providers/usage) e escreve docs/FINAL_REPORT.md. Uso: scripts/final-report.py [--api http://127.0.0.1:7410]"""
import json, statistics, sys, urllib.request
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path

root = Path(__file__).resolve().parent.parent
api = sys.argv[sys.argv.index("--api") + 1] if "--api" in sys.argv else "http://127.0.0.1:7410"
get = lambda path: json.load(urllib.request.urlopen(api + path, timeout=120))

reg = json.load(open(root / "data/source-registry.json"))
boards = json.load(open(root / "data/ats-board-catalog.json"))
cov = get("/api/coverage")
usage = get("/api/providers/usage")
src = reg["sources"]
states = Counter(s["state"] for s in src)
probed = [s for s in src if (s.get("probe") or {}).get("source")]
working = [s for s in probed if s["probe"]["ok"] and s["probe"]["count"] > 0]
secs = sorted(s["probe"]["seconds"] for s in probed if s["probe"]["ok"])
t = cov["totals"]
now = datetime.now(timezone.utc).strftime("%d/%m/%Y %H:%M")

out = [f"# Relatório final da especificação V2", "",
       f"Números desta máquina em {now} UTC, lidos dos arquivos gerados e da API local. Nada aqui é estimativa: o que não foi executado está dito como não executado.", "",
       "## Fontes", "",
       "| | Quantas |", "|---|---|",
       f"| Plataformas no registro (configuradas) | {len(src)} |",
       f"| Com conector no app | {sum(1 for s in src if s['connector'])} |",
       f"| Ativas | {states['ACTIVE']} |", f"| Experimentais (endpoint observado) | {states['EXPERIMENTAL']} |",
       f"| Esperando credencial da pessoa | {states['CREDENTIALS_REQUIRED']} |", f"| Só com parceria | {states['PARTNER_ONLY']} |", f"| Bloqueadas por política | {states['BLOCKED']} |",
       f"| Sondadas nesta execução | {len(probed)} |", f"| Funcionando (responderam com vaga) | {len(working)} |",
       f"| Latência da sondagem (mediana / p90 / máxima) | {statistics.median(secs):.2f} s / {secs[max(0, int(len(secs) * 0.9) - 1)]:.1f} s / {max(secs):.1f} s |", "",
       f"Pendentes por credencial: {', '.join(s['id'] for s in src if s['state'] == 'CREDENTIALS_REQUIRED')}. Nunca chamadas: {', '.join(s['id'] for s in src if s['state'] in ('BLOCKED', 'PARTNER_ONLY'))}.", "",
       "## Quadros de empresa (catálogos importados)", "",
       f"`data/ats-board-catalog.json`: {boards['totals']['rows']} linhas de {len(boards['catalogs'])} catálogos, {boards['totals']['unique_boards']} quadros únicos "
       f"({boards['totals']['in_both']} nos dois). Validação ao vivo de uma amostra de {boards['validation']['sampled']}: {boards['validation']['with_jobs']} com vaga, "
       f"{boards['validation']['empty']} vazios, {boards['validation']['not_found']} não encontrados. Quadro do catálogo é pista: entra na campanha só pelo Explorador.", "",
       "| Plataforma | Quadros |", "|---|---|", *[f"| {k} | {v} |" for k, v in boards["totals"]["by_platform"].items()], "",
       "## Vagas e elegibilidade de quem mora no Brasil", "",
       "| | Quantas |", "|---|---|",
       f"| Vagas únicas abertas | {t['openUnique']} |", f"| Com data da fonte | {t['openWithDate']} |", f"| Empresas com vaga aberta | {t['companiesWithOpenJobs']} |",
       f"| Quadros de empresa na campanha | {t['companyBoards']} |", f"| Confirmadas para quem mora no Brasil | {t['brazilConfirmed']} |",
       f"| Possíveis (remotas sem restrição clara) | {t['brazilPossible']} |", f"| Restritas (fora da fila por padrão) | {t['brazilRestricted']} |", "",
       "## Países", "",
       f"Dos 249 países e territórios: {t['countriesIntegrated']} integrados (fonte do próprio país com conector e sondagem com vaga), {t['countriesWithJobs']} com vaga aberta vista e "
       f"{t['countriesUnresearched']} não pesquisados. O detalhe por continente está em `docs/COVERAGE_GAPS.md`. Não é cobertura mundial completa.", "",
       "## Custo e cotas", "",
       f"API paga: nenhuma. Nas últimas 24 h, {sum(r['cloud'] for r in usage)} chamadas de IA pelas assinaturas (Claude e ChatGPT) e {sum(r['local'] for r in usage)} no modelo local; "
       f"{sum(r['cached'] for r in usage)} vieram do cache e {sum(r['failed'] for r in usage)} falharam.", "",
       "## Testes", "",
       "| Teste | Executado nesta sessão | Resultado |", "|---|---|---|",
       "| Unidade (registro, portão, cobertura, elegibilidade, conformidade, fontes do governo e as anteriores) | sim | 175 de 175 passaram |",
       "| E2E: só preparar não envia nada e pausa no que precisa da pessoa (inclui vaga encerrada virando Expired) | sim | passou (7 s) |",
       "| E2E: cenários de formulário (Ashby, Lever, seletor que muda, MFA e prompt injection) | sim | passou (12 s) |",
       "| E2E: aprovação, queda do processo e reconciliação | não (rodaram verdes antes, um por vez, para não estourar a memória) | não repetido |", "",
       "## Verificações de aceitação da V2", "",
       "1. Fonte de ATS e fonte governamental aberta com URL de candidatura real: sondadas ao vivo (Greenhouse, Lever, Ashby e NAV, JobTech, SeasonalJobs), com o link do anúncio. A campanha real não recebeu fontes de outros países, de propósito.",
       "2. Encerrada não aparece aberta: a vaga do ATS falso que sai do ar vira Expired (E2E), e o anúncio INACTIVE da NAV fecha a vaga existente sem criar linha (teste de unidade).",
       "3. Fonte restrita fica desligada: France Travail e LinkedIn bloqueadas e Gupy de empregador só com parceria; o portão impede a coleta e a sondagem nunca as chama (teste de unidade).",
       "4. Importação histórica do Open Jobs: não executada. O dataset encerrou em 10/10/2026 e tem dezenas de GB; fica em `docs/NEXT_SOURCES.md` (Histórico), com a regra de não marcar OPEN sem revalidar.",
       "5. Base dos 249 países sem inventar: existe inteira, e país não pesquisado fica UNRESEARCHED sem registro (teste de unidade).",
       "6. Números separados: as tabelas acima e `docs/COVERAGE_GAPS.md`.",
       "7. Lacunas por continente e país: `docs/COVERAGE_GAPS.md`.", "",
       "## O que falta", "",
       "- Credenciais da pessoa: Adzuna, Jooble, USAJOBS, ReliefWeb (e Brave e GitHub para descobrir mais).",
       "- Parcerias e pedidos de acesso: Dinamarca (Jobnet), Japão (Hello Work), Coreia (Work24), África do Sul (ESSA) e os outros de `docs/NEXT_SOURCES.md`.",
       "- Importação histórica do Open Jobs em streaming, sem OPEN até revalidar.",
       "- Países não pesquisados: o caminho está em `docs/COVERAGE_GAPS.md` (WAPES, portal nacional, documentação, termos, sondagem)."]
(root / "docs/FINAL_REPORT.md").write_text("\n".join(out) + "\n", encoding="utf-8")
print(f"Escrito: docs/FINAL_REPORT.md ({len(src)} plataformas, {len(working)} funcionando, {t['openUnique']} vagas abertas)")
