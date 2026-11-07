#!/usr/bin/env bash
set -euo pipefail
export JO_TEST_REPO="${JO_TEST_REPO:-$(git rev-parse --show-toplevel)}"
export JO_TEST_TEMP="$(mktemp -d /tmp/jo-candidate-test.XXXXXX)"
trap 'if [ -n "${pg_test_pid:-}" ]; then kill "$pg_test_pid" 2>/dev/null || true; wait "$pg_test_pid" 2>/dev/null || true; fi; rm -rf -- "$JO_TEST_TEMP"' EXIT
pkg_path=io/zonky/test/postgres/embedded-postgres-binaries-linux-amd64/17.11.0/embedded-postgres-binaries-linux-amd64-17.11.0.jar
curl -fsSL --max-time 30 "https://repo.maven.apache.org/maven2/$pkg_path" -o "$JO_TEST_TEMP/postgres.jar"
printf '%s  %s\n' 0dd7b72b6f335b8ecfb355fa24c5781e8a93edd09880bb77eb52ebbf29b3e96d "$JO_TEST_TEMP/postgres.jar" | sha256sum -c -
unzip -p "$JO_TEST_TEMP/postgres.jar" '*.txz' | tar -xJ -C "$JO_TEST_TEMP"
export LD_LIBRARY_PATH="$JO_TEST_TEMP/lib"
printf 'jo-test\n' >"$JO_TEST_TEMP/password"
"$JO_TEST_TEMP/bin/initdb" -D "$JO_TEST_TEMP/db" -U jo --pwfile="$JO_TEST_TEMP/password" --auth=scram-sha-256 --locale-provider=builtin --builtin-locale=C --encoding=UTF8 >/dev/null
printf 'CREATE DATABASE jo_test;\n' | "$JO_TEST_TEMP/bin/postgres" --single -D "$JO_TEST_TEMP/db" postgres >"$JO_TEST_TEMP/setup.log" 2>&1
"$JO_TEST_TEMP/bin/postgres" -D "$JO_TEST_TEMP/db" -h 127.0.0.1 -p 5463 -c 'unix_socket_directories=' -c 'dynamic_shared_memory_type=mmap' >"$JO_TEST_TEMP/postgres.log" 2>&1 &
pg_test_pid=$!
for attempt in $(seq 1 30); do
  if (echo > /dev/tcp/127.0.0.1/5463) 2>/dev/null; then break; fi
  if ! kill -0 "$pg_test_pid" 2>/dev/null; then cat "$JO_TEST_TEMP/postgres.log"; exit 1; fi
  sleep 0.2
done
cd "$JO_TEST_REPO/backend"
"${JO_AUDIT_DOTNET:-dotnet}" build JobOrchestrator.slnx -m:1 -nodeReuse:false -p:UseSharedCompilation=false
python3 "$JO_TEST_REPO/docs/audit-evidence/reproduce-http.py"
