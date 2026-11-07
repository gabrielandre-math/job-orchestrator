#!/usr/bin/env bash
# Runs the local CAPTCHA and job-extraction labs end to end with one command: a lab Fake ATS on its own port (the dev one on
# 5470 is left alone), both solvers, the paginated collection, the unsupported scenario and the 11 lab tests. Everything stays
# on 127.0.0.1 and the server stops at the end. Needs the .NET 10 SDK and Python 3.11+; lxml goes into .dev/labvenv.
#   scripts/labs.sh            (port 5471, or FAKEATS_LAB_PORT)
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
PORT="${FAKEATS_LAB_PORT:-5471}"
VENV="$R/.dev/labvenv"
OUT="$R/.dev/labs-fakeats"
PY="$VENV/bin/python"

[ -x "$PY" ] || { python3 -m venv "$VENV" && "$VENV/bin/pip" install -q 'lxml>=5,<7'; }
dotnet build -c Release -v quiet -nologo "$R/backend/src/Tools/JobOrchestrator.FakeAts" -o "$OUT" >/dev/null

tmp="$(mktemp -d)"; state="$tmp/fake-ats.json"; log="$tmp/fake-ats.log"
FAKEATS_CAPTCHA_LAB=1 FAKEATS_URL="http://127.0.0.1:$PORT" FAKEATS_STATE="$state" dotnet "$OUT/JobOrchestrator.FakeAts.dll" >"$log" 2>&1 &
server=$!
trap 'code=$?; kill "$server" 2>/dev/null || true; [ "$code" -eq 0 ] || { echo "--- Fake ATS log"; tail -20 "$log"; }; rm -rf "$tmp"' EXIT
for _ in $(seq 1 60); do curl -s -o /dev/null "http://127.0.0.1:$PORT/lab/linkedin" && break; sleep 1; done

cd "$R"
echo "== CAPTCHA de imagem"; "$PY" scripts/captcha-lab.py --kind image --port "$PORT"
echo "== CAPTCHA de soma"; "$PY" scripts/captcha-lab.py --kind math --port "$PORT"
echo "== Coleta paginada"; "$PY" scripts/linkedin_lab.py --port "$PORT"
echo "== Desafio sem resolvedor (termina com 2 e pede uma pessoa)"; "$PY" scripts/linkedin_lab.py --port "$PORT" --scenario unsupported || [ $? -eq 2 ]
echo "== Testes do laboratório"
PYTHONPATH=scripts PYTHONDONTWRITEBYTECODE=1 FAKEATS_LAB_TEST_PORT="$PORT" "$PY" -m unittest discover -s scripts/tests -v
