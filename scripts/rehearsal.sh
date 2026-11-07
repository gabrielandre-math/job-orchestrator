#!/usr/bin/env bash
# Ensaio noturno: o app inteiro trabalhando horas no automático contra o ATS de teste e um coletor de e-mail local (GreenMail), com
# falhas provocadas (processos mortos, site e e-mail fora do ar, currículo apagado, revogação, só buscar) e o relatório no fim.
# Roda no desktop, nunca no Mac: precisa do Docker, do .NET 10 e de um Chrome ou Chromium. Nada sai para fora da máquina.
#   scripts/rehearsal.sh [horas] [minutos-por-rodada]     (padrão: 8 e 15; para conferir a montagem: 0.5 3)
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
HOURS="${1:-8}"; CYCLE="${2:-15}"
REPORT="${JO_REHEARSAL_REPORT:-$R/.dev/ensaios/ensaio-$(date +%Y%m%d-%H%M).md}"
mkdir -p "$(dirname "$REPORT")"
docker compose -f "$R/infra/compose.test.yaml" up -d --wait >/dev/null
docker pull -q greenmail/standalone:2.1.2 >/dev/null 2>&1 || true
cd "$R/backend"
status=0
JO_REHEARSAL_HOURS="$HOURS" JO_REHEARSAL_CYCLE_MINUTES="$CYCLE" JO_REHEARSAL_REPORT="$REPORT" DOTNET_SYSTEM_NET_DISABLEIPV6=1 \
  dotnet test tests/JobOrchestrator.Tests --filter "Category=Rehearsal" --logger "console;verbosity=normal" || status=$?
echo "Relatório: $REPORT"
exit "$status"
