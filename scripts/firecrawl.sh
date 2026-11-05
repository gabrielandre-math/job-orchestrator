#!/usr/bin/env bash
# Firecrawl auto-hospedado e opcional (AGPL, projeto à parte, sem modificação): só para páginas de carreira que montam com JavaScript.
# Clona o repositório oficial numa pasta de dados, sobe o compose deles preso em 127.0.0.1:3002 e diz como ligar no app.
set -euo pipefail
D="${XDG_DATA_HOME:-$HOME/.local/share}/job-orchestrator/firecrawl"
if [ ! -d "$D/.git" ]; then git clone --depth 1 https://github.com/firecrawl/firecrawl.git "$D"; else git -C "$D" pull --ff-only; fi
cd "$D"
[ -f .env ] || printf 'PORT=127.0.0.1:3002\nUSE_DB_AUTHENTICATION=false\nNUM_WORKERS_PER_QUEUE=2\n' >.env
docker compose up -d
echo "Firecrawl em http://127.0.0.1:3002. Para o app usar: Scraper__Firecrawl__Url=http://127.0.0.1:3002 (ou JO_FIRECRAWL_URL no Docker)."
