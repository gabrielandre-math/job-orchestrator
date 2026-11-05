#!/usr/bin/env bash
# Backup do banco do Job Orchestrator: pg_dump comprimido em ~/.local/share/job-orchestrator/backups, guarda os 14 mais novos.
set -euo pipefail
D="${JO_DATA_DIR:-${XDG_DATA_HOME:-$HOME/.local/share}/job-orchestrator}/backups"
mkdir -p "$D"
f="$D/$(date +%F-%H%M).sql.gz"
docker exec job-orchestrator-postgres-1 pg_dump -U jo -d job_orchestrator --no-owner | gzip -9 >"$f.tmp" && mv "$f.tmp" "$f"
ls -1t "$D"/*.sql.gz | tail -n +15 | xargs -r rm -f
echo "backup: $f ($(du -h "$f" | cut -f1))"
