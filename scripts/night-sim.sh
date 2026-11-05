#!/usr/bin/env bash
# Noite simulada em minutos, no ambiente de desenvolvimento: pede a rodada da noite, espera a busca terminar e a fila assentar,
# e gera o resumo (o mesmo que o timer das 7h45 gera). Uso: scripts/night-sim.sh [api]
set -euo pipefail
A="${1:-http://127.0.0.1:7410}/api"
echo "Rodada da noite: $(curl -fsS -X POST "$A/night/run")"
for i in $(seq 1 120); do
  done=$(curl -fsS "$A/agents/threads?limit=10" | python3 -c "import json,sys; t=[x for x in json.load(sys.stdin) if x['kind']=='search']; print(1 if t and t[0]['messages']>0 else 0)")
  busy=$(curl -fsS "$A/applications" | python3 -c "import json,sys; print(sum(a['state'] in ('Shortlisted','MaterialsReady','FormPreparing','ReadyToSubmit','Submitting') for a in json.load(sys.stdin)))")
  [ "$done" = 1 ] && [ "$busy" = 0 ] && break
  sleep 5
done
curl -fsS -X POST "$A/night/report" | python3 -c "import json,sys; d=json.load(sys.stdin); print('Resumo:', d['name'])"
n=$(curl -fsS "$A/night/reports" | python3 -c "import json,sys; print(json.load(sys.stdin)[0]['name'])")
curl -fsS "$A/night/reports/$n" | python3 -c "import json,sys; print(json.load(sys.stdin)['markdown'])"
