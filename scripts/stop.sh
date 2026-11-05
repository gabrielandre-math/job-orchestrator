#!/usr/bin/env bash
# Para API, Worker e ATS falso do ambiente de desenvolvimento. O Postgres continua no ar.
R="$(cd "$(dirname "$0")/.." && pwd)"
for f in "$R"/.dev/pids/*; do [ -f "$f" ] && kill "$(cat "$f")" 2>/dev/null; rm -f "$f"; done
echo "parado"
