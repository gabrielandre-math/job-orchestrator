# Runbook

## Jeitos de rodar

| Jeito | Comando | Quando |
|---|---|---|
| Desenvolvimento | `scripts/dev.sh` | código local, logs em `.dev/logs`, dados em `.dev/data` |
| Serviços de usuário | `scripts/install-units.sh [--enable]` | uso diário com a busca da noite |
| Docker | `scripts/docker.sh up` (`JO_PROFILES=gpu` para Ollama em contêiner) | tudo em contêineres, rede do host, só 127.0.0.1 |

Os três usam o mesmo Postgres (`infra/compose.yaml`, porta 5462). Rode um de cada vez: os três escutam em 7410.

## Serviços de usuário

```bash
systemctl --user status job-orchestrator-api job-orchestrator-worker
systemctl --user list-timers 'job-orchestrator-*'
journalctl --user -u job-orchestrator-worker -f
systemctl --user stop job-orchestrator-worker    # seguro: nada é clicado pela metade
```

Parar o Worker no meio de um envio é seguro. A intenção é gravada antes do clique, e a próxima subida reconcilia o que ficou incerto, nunca reenvia. Os serviços ficam na fatia `jobs.slice`, com teto de 8 GB e prioridade baixa.

## Docker

`scripts/docker.sh up | down | logs | build | ps`. API e Worker usam a mesma base, com Chromium do Playwright, poppler, Scrapy, as CLIs do Claude e do Codex e o login de assinatura montado do host (`~/.claude`, `~/.claude.json`, `~/.codex`, `~/.config/job-orchestrator/accounts`). O gnome-keyring chega pelo D-Bus do usuário. O Firecrawl é opcional: rode `scripts/firecrawl.sh` e defina `JO_FIRECRAWL_URL=http://127.0.0.1:3002`.

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
| Envio incerto | confirmação não apareceu a tempo | Atenção › "O envio chegou?" depois de conferir o e-mail |
| E-mail recusa login | senha normal em vez de senha de app | crie a senha de app no provedor |
| Modelo local lento | modelo maior que a VRAM | Provedores › use a recomendação para a sua placa |
