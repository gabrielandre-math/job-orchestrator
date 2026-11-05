#!/usr/bin/env bash
# Instala o Job Orchestrator como serviços de usuário: publica API e Worker em ~/.local/opt/job-orchestrator, copia o painel, o coletor,
# o compose e as units. Não liga nada sozinho: use --enable para ligar API, Worker e os timers da noite, do resumo e do backup.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"; O="$HOME/.local/opt/job-orchestrator"; U="$HOME/.config/systemd/user"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
mkdir -p "$O" "$U"
dotnet publish "$R/backend/src/Bootstrappers/JobOrchestrator.Api" -c Release -o "$O/api" -v q -nologo
dotnet publish "$R/backend/src/Bootstrappers/JobOrchestrator.Worker" -c Release -o "$O/worker" -v q -nologo
(cd "$R/web" && npx ng build >/dev/null) && rm -rf "$O/web" && cp -r "$R/web/dist/app/browser" "$O/web"
rm -rf "$O/scraper" && cp -r "$R/scraper" "$O/scraper"
cp "$R/infra/compose.yaml" "$O/compose.yaml" && cp "$R/scripts/backup.sh" "$O/backup.sh" && chmod +x "$O/backup.sh"
cp "$R"/infra/systemd/*.service "$R"/infra/systemd/*.timer "$R"/infra/systemd/jobs.slice "$U/"
systemctl --user daemon-reload
if [ "${1:-}" = "--enable" ]; then
  systemctl --user enable --now job-orchestrator-api.service job-orchestrator-worker.service
  systemctl --user enable --now job-orchestrator-night.timer job-orchestrator-report.timer job-orchestrator-backup.timer
  echo "Ligado. Painel: http://127.0.0.1:7410. Timers: systemctl --user list-timers 'job-orchestrator-*'"
else
  echo "Instalado sem ligar. Para ligar tudo: $0 --enable (ou systemctl --user enable --now job-orchestrator-api job-orchestrator-worker)."
fi
