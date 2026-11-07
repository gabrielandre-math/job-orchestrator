#!/usr/bin/env bash
# Linux x64 integration run. Everything below uses a disposable database and synthetic identities.
set -euo pipefail
JO_REPO="$(cd "$(dirname "$0")/.." && pwd)"
export JO_DOTNET="${JO_DOTNET:-dotnet}"
export JO_TEST_TEMP="$(mktemp -d /tmp/jo-controlled.XXXXXX)"
cleanup() {
  if [ -n "${jo_pg_pid:-}" ]; then kill "$jo_pg_pid" 2>/dev/null || true; wait "$jo_pg_pid" 2>/dev/null || true; fi
  rm -rf -- "$JO_TEST_TEMP"
}
trap cleanup EXIT
if (echo >/dev/tcp/127.0.0.1/5463) 2>/dev/null; then echo 'A porta 5463 já está ocupada. Este teste exige uma porta livre para não tocar no seu banco.' >&2; exit 1; fi
pkg=io/zonky/test/postgres/embedded-postgres-binaries-linux-amd64/17.11.0/embedded-postgres-binaries-linux-amd64-17.11.0.jar
curl -fsSL --max-time 60 "https://repo.maven.apache.org/maven2/$pkg" -o "$JO_TEST_TEMP/postgres.jar"
printf '%s  %s\n' 0dd7b72b6f335b8ecfb355fa24c5781e8a93edd09880bb77eb52ebbf29b3e96d "$JO_TEST_TEMP/postgres.jar" | sha256sum -c -
unzip -p "$JO_TEST_TEMP/postgres.jar" '*.txz' | tar -xJ -C "$JO_TEST_TEMP"
export LD_LIBRARY_PATH="$JO_TEST_TEMP/lib${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
printf 'jo-test\n' >"$JO_TEST_TEMP/password"
"$JO_TEST_TEMP/bin/initdb" -D "$JO_TEST_TEMP/db" -U jo --pwfile="$JO_TEST_TEMP/password" --auth=scram-sha-256 --locale-provider=builtin --builtin-locale=C --encoding=UTF8 >/dev/null
printf 'CREATE DATABASE jo_test;\n' | "$JO_TEST_TEMP/bin/postgres" --single -D "$JO_TEST_TEMP/db" postgres >"$JO_TEST_TEMP/setup.log" 2>&1
"$JO_TEST_TEMP/bin/postgres" -D "$JO_TEST_TEMP/db" -h 127.0.0.1 -p 5463 -c 'unix_socket_directories=' -c 'dynamic_shared_memory_type=mmap' >"$JO_TEST_TEMP/postgres.log" 2>&1 &
jo_pg_pid=$!
for _ in $(seq 1 50); do if (echo >/dev/tcp/127.0.0.1/5463) 2>/dev/null; then break; fi; sleep .2; done
cd "$JO_REPO/backend"
"$JO_DOTNET" build JobOrchestrator.slnx -m:1 -nodeReuse:false -p:UseSharedCompilation=false
"$JO_DOTNET" test tests/JobOrchestrator.Tests --no-build --filter 'Category!=E2E|FullyQualifiedName~LinkedInCoverageDbTests' -m:1 -nodeReuse:false
cd "$JO_REPO"
python3 scripts/controlled-smoke.py "$@"
