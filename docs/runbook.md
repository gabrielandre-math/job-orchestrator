# Runbook

## Jeitos de rodar

| Jeito | Comando | Quando |
|---|---|---|
| Desenvolvimento | `scripts/dev.sh` | código local, logs em `.dev/logs`, dados em `.dev/data` |
| Serviços de usuário | `scripts/install-units.sh [--enable]` | uso diário com a busca da noite |
| Docker | `scripts/docker.sh up` (`JO_PROFILES=gpu` para Ollama em contêiner) | tudo em contêineres, rede do host, só 127.0.0.1 |
| Executável | `scripts/package.sh`, depois `dist/job-orchestrator-instalador.run` | uso diário: binário único (API, painel e Worker num processo), atalho no menu, serviço do usuário |

Todos usam o mesmo Postgres (`infra/compose.yaml`, porta 5462, projeto `job-orchestrator`). Rode um de cada vez: todos escutam em 7410. O instalador encerra a API e o Worker do modo de desenvolvimento antes de ligar o serviço instalado.

## Executável

- `job-orchestrator`: abre a janela (sobe os serviços se preciso, com a abertura animada); `--serve` roda os serviços no terminal; `--status`, `--stop`, `--setup`, `--version`.
- Os serviços rodam como `job-orchestrator.service` do usuário (com "abrir junto com o computador") ou como unidade passageira do `systemd-run`, sempre em `jobs.slice` com teto de 8 GB. Logs: `journalctl --user -u job-orchestrator -f`.
- Dados em `~/.local/share/job-orchestrator`, configurações em `~/.config/job-orchestrator` (o `env` guarda o PATH da sessão, onde moram as CLIs do Claude e do Codex). A janela usa um perfil próprio do navegador em `~/.local/share/job-orchestrator/window`.
- `JO_NO_WINDOW=1` faz o executável só dizer o endereço, sem abrir janela (testes e uso sem tela).

## Serviços de usuário

```bash
systemctl --user status job-orchestrator-api job-orchestrator-worker
systemctl --user list-timers 'job-orchestrator-*'
journalctl --user -u job-orchestrator-worker -f
systemctl --user stop job-orchestrator-worker    # seguro: nada é clicado pela metade
```

Parar o Worker no meio de um envio é seguro. A intenção é gravada antes do clique, e a próxima subida reconcilia o que ficou incerto, nunca reenvia. Os serviços ficam na fatia `jobs.slice`, com teto de 8 GB e prioridade baixa.

## Docker

`scripts/docker.sh up | down | logs | build | ps`. API e Worker usam a mesma base, com Chromium do Playwright, poppler, Scrapy, as CLIs do Claude e do Codex e o login de assinatura montado do host (`~/.claude`, `~/.claude.json`, `~/.codex`, `~/.config/job-orchestrator/accounts`). O gnome-keyring chega pelo D-Bus do usuário. O Firecrawl é opcional: rode `scripts/firecrawl.sh` e defina `JO_FIRECRAWL_URL=http://127.0.0.1:3002`. Os sensores da GPU no contêiner precisam do NVIDIA Container Toolkit (`nvidia-container-toolkit` e `sudo nvidia-ctk runtime configure --runtime=docker`); com ele, `JO_GPU=1 scripts/docker.sh up`.

## Busca sozinha e Explorador

- Campanha ligada: rodada completa a cada `Search:IntervalMinutes` (60 por padrão), pedida pelo Worker.
- Explorador: um ciclo a cada `Explorer:IntervalMinutes` (10), com `Explorer:Enabled`, `Explorer:BoardsPerCycle` (10), `Explorer:ReposPerCycle` (3), `Explorer:GupyPerCycle` (4), `Explorer:SitesPerCycle` (6), `Explorer:MaxSources` (300), `Explorer:FreshDays` (120) e `Explorer:Lists` (READMEs de listas de empresas).
- Estado em `$JO_DATA/explorer`: `frontier.json` (fila de pistas), `explored.json` (o que foi testado, revisto depois de 7 dias), `lists.json` (quando cada lista foi lida) e `last.json` (o último ciclo).
- Endpoints: `GET /api/explore/status`, `POST /api/explore/run`, `POST /api/explore/evaluate {"sources": [...]}`, `PUT /api/explore/settings`.
- Timers da noite pelo painel: `GET /api/units` e `PUT /api/units/night {"on": true}` (só os timers; API e Worker como serviço seguem no `install-units.sh`).

## Coletor e navegador de leitura

- `Scraper:Browser:Enabled` (true) e `Scraper:Browser:MaxSites` (4): site que não deu nada no Scrapy é aberto pelo navegador de leitura, com o robots.txt conferido e até 3 cliques escolhidos pelo Llama.
- `Scraper:Obscura:Enabled`, `Scraper:Obscura:Path`, `Scraper:Obscura:Port` (9444): o Obscura normal (nunca `--stealth`) entra no lugar do Chromium para ler quando instalado.
- `Scraper:Firecrawl:Url`: sem configuração, o app usa o Firecrawl que estiver em 127.0.0.1:3002.
- Extrações da IA guardadas por 14 dias em `scraper/extractions.json` (página igual não volta para o modelo); fontes que somem (404) em 5 rodadas seguidas saem da campanha (`jobs/source-health.json`).
- `Explorer:WebSearchPerDay` (60): teto diário da busca na web pelo Brave Search.

## Backup e restauração

- Backup diário às 7h55 (`job-orchestrator-backup.timer`) ou na hora: `scripts/backup.sh`. Os arquivos ficam em `~/.local/share/job-orchestrator/backups`, com os 14 mais novos.
- Restaurar: `gunzip -c ARQUIVO.sql.gz | docker exec -i job-orchestrator-postgres-1 psql -U jo -d job_orchestrator`.

## Testes

```bash
cd backend
dotnet test tests/JobOrchestrator.Tests --filter "Category!=E2E"                  # unidade
docker compose -f ../infra/compose.test.yaml up -d --wait                          # Postgres de teste (5463)
dotnet test tests/JobOrchestrator.Tests --filter "FullyQualifiedName~FlowTests.X"  # um E2E por vez
```

Rode os E2E um de cada vez e com teto de memória (`systemd-run --user --scope -p MemoryMax=5G`). Cada um sobe ATS falso, API, Worker e Chromium. Os 10 cenários do ATS falso estão descritos no topo de `backend/src/Tools/JobOrchestrator.FakeAts/Program.cs`.

## Problemas comuns

| Sintoma | Causa provável | O que fazer |
|---|---|---|
| Provedor "sem login" | sessão da CLI expirou | Provedores › Conectar, ou `claude auth login` na pasta da conta |
| Restore do .NET trava | IPv6 sem rota | `DOTNET_SYSTEM_NET_DISABLEIPV6=1` |
| Fonte "não respondeu" | site fora do ar ou 403 | não há contorno; troque a fonte |
| Explorador não acha repositório | limite da API do GitHub (60 por hora sem token) | espera a hora virar ou conecte o token do GitHub em Fontes |
| O app não abre | serviço parado ou banco fora do ar | `job-orchestrator --status`; `job-orchestrator --serve` no terminal mostra o motivo |
| Instalador avisa da porta 7410 | o modo de desenvolvimento está no ar | ele encerra o modo de desenvolvimento no passo Serviço; ou pare antes |
| Envio incerto | confirmação não apareceu a tempo | Atenção › "O envio chegou?" depois de conferir o e-mail |
| E-mail recusa login | senha normal em vez de senha de app | crie a senha de app no provedor |
| Modelo local lento | modelo maior que a VRAM | Provedores › use a recomendação para a sua placa |
