#!/usr/bin/env bash
# O app em contêineres: scripts/docker.sh up | down | logs | build | ps. Ollama em contêiner: JO_PROFILES=gpu. GPU visível (sensores): JO_GPU=1.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$HOME/.local/share/job-orchestrator" "$HOME/.config/job-orchestrator" "$HOME/.claude" "$HOME/.codex"
[ -f "$HOME/.claude.json" ] || echo '{}' >"$HOME/.claude.json"
export JO_UID="$(id -u)" JO_GID="$(id -g)"
profiles=(); for p in ${JO_PROFILES:-}; do profiles+=(--profile "$p"); done
files=(-f "$R/infra/compose.yaml" -f "$R/infra/compose.app.yaml"); [ "${JO_GPU:-}" = 1 ] && files+=(-f "$R/infra/compose.gpu.yaml")
dc() { docker compose "${files[@]}" "${profiles[@]}" "$@"; }
case "${1:-up}" in
  up) dc up -d --build --wait && echo "Painel: http://127.0.0.1:7410" ;;
  down) dc down ;;
  logs) dc logs -f --tail 100 api worker ;;
  build) dc build ;;
  ps) dc ps ;;
  *) echo "uso: $0 up|down|logs|build|ps"; exit 1 ;;
esac
