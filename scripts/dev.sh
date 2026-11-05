#!/usr/bin/env bash
# Sobe o ambiente de desenvolvimento: Postgres (compose), ATS falso, API e Worker, com dados isolados em .dev/.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
D="$R/.dev"; mkdir -p "$D/logs" "$D/data" "$D/config" "$D/pids"
export DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development DOTNET_SYSTEM_NET_DISABLEIPV6=1
export JO_DATA_DIR="$D/data" JO_CONFIG_DIR="$D/config"
export Profile__Seed="$R/config/profile.example.json" Web__Root="$R/web/dist/app/browser"
export FAKEATS_STATE="$D/data/fake-ats.json"

docker compose -f "$R/infra/compose.yaml" up -d --wait >/dev/null
dotnet build "$R/backend/JobOrchestrator.slnx" -v q -nologo | grep -E ' error |Warn' || true

start() { # start <nome> <projeto>
  local name="$1" dir="$R/backend/src/$2/bin/Debug/net10.0"
  [ -f "$D/pids/$name" ] && kill "$(cat "$D/pids/$name")" 2>/dev/null || true
  (cd "$dir" && exec nohup dotnet "$(basename "$2").dll" >"$D/logs/$name.log" 2>&1 </dev/null) &
  echo $! >"$D/pids/$name"
}
start fake-ats Tools/JobOrchestrator.FakeAts
start api Bootstrappers/JobOrchestrator.Api
start worker Bootstrappers/JobOrchestrator.Worker
for i in $(seq 1 40); do curl -fsS http://127.0.0.1:7410/api/health >/dev/null 2>&1 && break; sleep 0.5; done
echo "API e painel: http://127.0.0.1:7410   ATS falso: http://127.0.0.1:5470   logs: $D/logs"
