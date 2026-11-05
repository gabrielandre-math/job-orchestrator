#!/usr/bin/env bash
# Gera o executável do Job Orchestrator: um binário único (API, painel e Worker num processo, sem precisar do .NET instalado), com o
# painel, o coletor, o compose do Postgres e as units, em dist/job-orchestrator-<versão>-linux-x64/ e no .tar.gz; e o instalador num
# arquivo só, dist/job-orchestrator-instalador.run, que extrai o pacote numa pasta temporária e abre a janela de instalação.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
V="$(grep -oP '(?<=<Version>)[^<]+' "$R/backend/src/Bootstrappers/JobOrchestrator.App/JobOrchestrator.App.csproj")"
N="job-orchestrator-$V-linux-x64"
P="$R/dist/$N"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
# Um job-orchestrator rodando de dentro do pacote antigo não pode ficar: o binário de arquivo único relê o próprio arquivo para carregar
# assemblies sob demanda, e trocar o arquivo por baixo dele quebra o processo no meio (FileNotFound em rotas que ainda não tinham rodado).
for exe in /proc/[0-9]*/exe; do
  t="$(readlink "$exe" 2>/dev/null || true)"
  case "$t" in "$P"/*) pid="${exe#/proc/}"; pid="${pid%/exe}"; echo "parando o job-orchestrator $pid, que roda do pacote antigo (suba de novo depois)"; kill -TERM "$pid" 2>/dev/null || true
    for _ in $(seq 60); do kill -0 "$pid" 2>/dev/null || break; sleep 0.5; done ;;
  esac
done
mkdir -p "$R/dist" && rm -rf "$P" "$P.tar.gz" && mkdir -p "$P"

echo "1/5 binário (.NET autocontido, arquivo único)"
dotnet publish "$R/backend/src/Bootstrappers/JobOrchestrator.App" -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:GenerateDocumentationFile=false -o "$P" -v q -nologo -m:2 -nodeReuse:false
rm -f "$P/playwright.ps1" "$P"/*.staticwebassets.endpoints.json
echo "2/5 painel"
(cd "$R/web" && npx ng build >/dev/null) && cp -r "$R/web/dist/app/browser" "$P/wwwroot"
echo "3/5 coletor, banco e units"
mkdir -p "$P/scraper" && (cd "$R/scraper" && tar --exclude='__pycache__' --exclude='*.egg-info' -cf - .) | tar -xf - -C "$P/scraper"
cp "$R/infra/compose.yaml" "$P/compose.yaml"
mkdir -p "$P/infra/systemd" "$P/scripts" && cp "$R"/infra/systemd/* "$P/infra/systemd/" && cp "$R/scripts/backup.sh" "$P/scripts/backup.sh"
echo "4/5 abertura, ícone e extensão do copiloto"
cp -r "$R/packaging/splash" "$P/splash" && cp "$R/packaging/job-orchestrator.svg" "$P/job-orchestrator.svg"
cp -r "$R/extension" "$P/extension"
# O instalador oferece trazer os dados do modo de desenvolvimento desta máquina (em outra máquina a pasta não existe e nada acontece).
if [ -d "$R/.dev" ]; then echo "$R/.dev" > "$P/dev-data.txt"; fi
chmod +x "$P/job-orchestrator"
echo "5/5 pacote e instalador"
tar -C "$R/dist" -czf "$P.tar.gz" "$N"
I="$R/dist/job-orchestrator-instalador.run"
cat > "$I" <<'HEAD'
#!/bin/sh
# Instalador do Job Orchestrator: extrai o pacote numa pasta temporária e abre a janela de instalação. Nada pede sudo.
set -e
T="$(mktemp -d "${TMPDIR:-/tmp}/job-orchestrator-setup.XXXXXX")"
L=$(awk '/^__PACOTE__$/ { print NR + 1; exit 0 }' "$0")
tail -n +"$L" "$0" | tar -xz -C "$T"
# O instalador copia o app para ~/.local/opt; a pasta temporária (em /tmp, muitas vezes na RAM) sai quando ele termina.
set +e
"$T"/job-orchestrator-*/job-orchestrator --setup
S=$?
rm -rf "$T"
exit $S
HEAD
echo "__PACOTE__" >> "$I"
cat "$P.tar.gz" >> "$I"
chmod +x "$I"
echo "Pacote: $P ($(du -sh "$P" | cut -f1))"
echo "Instalador: $I ($(du -h "$I" | cut -f1))"
