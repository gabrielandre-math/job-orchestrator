# Operação das fontes

Como instalar, rodar, agendar, vigiar e consertar a parte de fontes do Job Orchestrator no Omarchy. O runbook geral está em `docs/runbook.md` e o uso do
painel em `docs/guia.md`; aqui fica o que é próprio das fontes do mundo todo (especificação V2).

## Instalação e execução

| Jeito | Comando | O que sobe |
|---|---|---|
| Desenvolvimento | `scripts/dev.sh` | Postgres (127.0.0.1:5462), API e painel (127.0.0.1:7410), Worker e ATS falso (127.0.0.1:5470); dados em `.dev/` |
| Pacote | `scripts/package.sh` e depois `./dist/job-orchestrator-instalador.run` | binário único (API, painel e Worker), atalho no menu e serviço do usuário; a instalação real é um clique seu |
| Docker | `scripts/docker.sh up` | API e Worker em contêineres na rede do host, logins de assinatura montados, Ollama do host |
| Firecrawl (opcional) | `scripts/firecrawl.sh` | o compose oficial do Firecrawl (AGPL) em 127.0.0.1:3002, detectado sozinho para página que só monta com JavaScript |

Tudo escuta só em 127.0.0.1. A coleta não precisa de navegador: só as páginas que montam com JavaScript usam o navegador de leitura (Chromium do app
ou Obscura normal, nunca o modo furtivo).

## Agendamento

- **Busca contínua da campanha:** Configurações › Busca sozinha (intervalo em minutos). Cada rodada lê as fontes da campanha pela porta oficial.
- **Explorador:** a cada 10 minutos no Worker; acha fontes novas pelas palavras do currículo e só adiciona a que traz vaga nova, depois de testar.
- **Timers da noite** (noite, resumo e backup): ligados só pelo botão em Configurações, porque criam persistência na sua máquina.
- **Arquivo do DOL (SeasonalJobs):** baixado uma vez por dia; antes de sair o do dia, vale o de ontem.

## Fontes, permissão e portão

- **Registro:** `SourceRegistry` (código) e a aba Fontes › Registro (tela). Cada plataforma tem acesso (oficial pública, observada, com chave, parceria) e
  estado (ACTIVE, EXPERIMENTAL, CREDENTIALS_REQUIRED, PARTNER_ONLY, BLOCKED, DISCOVERY_ONLY).
- **Portão:** fonte bloqueada ou só com parceria nunca é chamada; a desligada em Fontes (`Sources:Disabled` no settings.json) também não; as experimentais
  saem todas com "Ler as experimentais" desligado (`Sources:Experimental`). O Recruitee fecha sozinho a partir de 10/02/2027, quando a rota anônima acaba.
- **Chaves:** Fontes › Chaves de API grava no gnome-keyring (Adzuna, Jooble, Brave, GitHub, USAJOBS, ReliefWeb). A plataforma passa de CREDENTIALS_REQUIRED
  para ACTIVE na sondagem seguinte.
- **Comandos** (rodar na raiz do repositório, com o app no ar):

| Comando | Gera |
|---|---|
| `jo sources probe` | sonda cada plataforma com permissão; `data/source-registry.json` e `docs/SOURCE_AUDIT.md` |
| `jo sources coverage` | a cobertura dos 249 países; `docs/COVERAGE_GAPS.md` |
| `jo sources boards --sample 40` | importa e valida os catálogos de quadros; `data/ats-board-catalog.json` |
| `jo sources next` | o inventário do que falta no catálogo V2; `docs/NEXT_SOURCES.md` |

O `jo` é `dotnet backend/src/Tools/JobOrchestrator.Cli/bin/Debug/net10.0/jo.dll` depois do build; `--api` aponta para outra porta quando preciso.

## Cotas e limites por fonte

| Fonte | Limite que o app respeita |
|---|---|
| Remotive | 4 leituras por dia somando todas as buscas, e a mesma busca no máximo a cada 6 h (termos da API) |
| Agregadores (Arbeitnow, Himalayas, Jobicy, The Muse, RemoteOK, WWR, JobTech, freehire...) | a mesma busca no máximo a cada 6 h |
| GitHub | 60 consultas por hora sem token, 5.000 com o token de leitura |
| Brave Search | teto diário configurado no assistente de chaves |
| Jooble | 500 chamadas no total da chave grátis |
| NAV | token público (gira sozinho; 401 busca o novo), 7 dias de feed, até 12 páginas e 40 detalhes por rodada |
| Gupy, Rippling, SmartRecruiters | até 40 descrições por rodada, uma de cada vez, com pausa |
| IA | teto por tarefa por dia (`Llm:Tasks:{tarefa}:Budget`); modelo local sem custo; API paga não é usada |

Todas as fontes passam pela resiliência por host (espera com Retry-After, circuito que abre e fecha) e por robots.txt onde há página.

## O que vigiar

- **Busca ao vivo:** cada fonte da rodada, quantas vagas, quantas da sua área, encerradas pela fonte, nota e fila.
- **Fontes › Na campanha:** a última rodada completa de cada fonte e o quadro morto (fonte que some várias rodadas sai sozinha).
- **Fontes › Registro:** estado, portão e a última sondagem (código HTTP, vagas, tempo). Sondar de novo pelo botão ou por `jo sources probe`.
- **Fontes › Cobertura:** os 249 países, por continente, com os números separados.
- **Provedores:** cotas das assinaturas, uso por tarefa e sensores da máquina.
- **Logs:** `.dev/logs/api.log`, `.dev/logs/worker.log` (desenvolvimento) ou `journalctl --user -u job-orchestrator` (instalado).

## Problemas comuns

| Sintoma | Causa | O que fazer |
|---|---|---|
| A sessão ou o VS Code morre no build | o systemd-oomd mata quem passa da memória | `free -h` antes; compilar com `MSBUILDDISABLENODEREUSE=1`, `-p:UseSharedCompilation=false`, `-m:2`, dentro de `systemd-run --user --scope -p MemoryMax=5G`; teste E2E um por vez |
| Fonte com "BLOCKED_BY_POLICY" ou "PARTNER_ONLY" | por desenho: a política da fonte proíbe ou pede contrato | nada a fazer; ver o caminho em `docs/NEXT_SOURCES.md` |
| "CREDENTIALS_REQUIRED" | falta a chave da pessoa | criar a conta na fonte e colar em Fontes › Chaves de API |
| NAV com 401 | o token público girou | o conector busca o novo sozinho; se persistir, conferir `https://pam-stilling-feed.nav.no/api/publicToken` |
| SeasonalJobs sem vaga de manhã | o arquivo do dia sai à meia-noite do horário do leste dos EUA | o app usa o de ontem até o novo sair |
| 403 de um site | Cloudflare ou antirrobô | não há contorno: escolher outra porta oficial para a empresa |
| Chave sumiu | gnome-keyring bloqueado | desbloquear a sessão e testar de novo em Fontes › Chaves de API |
| Assinatura sem resposta | CLI sem login | Provedores › Conectar conta (Claude ou ChatGPT) |
| Porta 7410 ocupada | outro processo do app | `job-orchestrator --status`; parar o antigo antes de subir o novo |
