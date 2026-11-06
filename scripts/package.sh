#!/usr/bin/env bash
# Gera o Job Orchestrator para cada sistema: um binário único autocontido (não precisa do .NET instalado) com o painel, o coletor, a
# abertura e a extensão do copiloto.
#   Linux:   dist/job-orchestrator-<v>-linux-<arq>/, o .tar.gz e o instalador .run (extrai e abre a janela de instalação).
#   macOS:   Job Orchestrator.app (Contents/MacOS com o binário, Contents/Resources com o resto) num .zip; scripts/package-mac.sh assina
#            e monta o .dmg no Mac.
#   Windows: a pasta, o .zip e JobOrchestrator-Setup-<v>-<arq>.exe (o próprio app com o pacote colado no fim).
# Uso: scripts/package.sh [linux-x64 linux-arm64 osx-arm64 osx-x64 win-x64 win-arm64 | all]   (sem argumento: linux-x64)
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
V="$(grep -oP '(?<=<Version>)[^<]+' "$R/backend/src/Bootstrappers/JobOrchestrator.App/JobOrchestrator.App.csproj")"
D="$R/dist"
export DOTNET_SYSTEM_NET_DISABLEIPV6=1
if [ $# -eq 0 ]; then set -- linux-x64; fi
if [ "$1" = all ]; then set -- linux-x64 linux-arm64 osx-arm64 osx-x64 win-x64 win-arm64; fi
mkdir -p "$D"

# Um job-orchestrator rodando de dentro de um pacote antigo não pode ficar: o binário de arquivo único relê o próprio arquivo para carregar
# assemblies sob demanda, e trocar o arquivo por baixo dele quebra o processo no meio (FileNotFound em rotas que ainda não tinham rodado).
for rid in "$@"; do
  for exe in /proc/[0-9]*/exe; do
    t="$(readlink "$exe" 2>/dev/null || true)"
    case "$t" in "$D/job-orchestrator-$V-$rid"/*) pid="${exe#/proc/}"; pid="${pid%/exe}"; echo "parando o job-orchestrator $pid, que roda do pacote antigo (suba de novo depois)"; kill -TERM "$pid" 2>/dev/null || true
      for _ in $(seq 60); do kill -0 "$pid" 2>/dev/null || break; sleep 0.5; done ;;
    esac
  done
done

echo "painel e ícones"
(cd "$R/web" && npx ng build >/dev/null)
python3 "$R/scripts/icons.py" "$D/icons" >/dev/null

# Os arquivos do app que não são o binário: painel, coletor, compose, abertura, ícone e extensão.
resources() {
  local to="$1" rid="$2"
  cp -r "$R/web/dist/app/browser" "$to/wwwroot"
  mkdir -p "$to/scraper" && (cd "$R/scraper" && tar --exclude='__pycache__' --exclude='*.egg-info' -cf - .) | tar -xf - -C "$to/scraper"
  cp "$R/infra/compose.yaml" "$to/compose.yaml"
  cp -r "$R/packaging/splash" "$to/splash" && cp "$R/packaging/job-orchestrator.svg" "$to/job-orchestrator.svg"
  cp -r "$R/extension" "$to/extension"
  case "$rid" in linux-*)
    mkdir -p "$to/infra/systemd" "$to/scripts" && cp "$R"/infra/systemd/* "$to/infra/systemd/" && cp "$R/scripts/backup.sh" "$to/scripts/backup.sh"
    # O instalador oferece trazer os dados do modo de desenvolvimento desta máquina (em outra máquina a pasta não existe e nada acontece).
    if [ -d "$R/.dev" ]; then echo "$R/.dev" > "$to/dev-data.txt"; fi ;;
  esac
}

publish() {
  local rid="$1" out="$2" extra=()
  case "$rid" in win-*) extra+=("-p:ApplicationIcon=$D/icons/job-orchestrator.ico") ;; esac
  rm -rf "$out" && mkdir -p "$out"
  dotnet publish "$R/backend/src/Bootstrappers/JobOrchestrator.App" -c Release -r "$rid" --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:EnableCompressionInSingleFile=true -p:DebugType=none \
    -p:GenerateDocumentationFile=false -p:UseSharedCompilation=false "${extra[@]}" -o "$out" -v q -nologo -m:2 -nodeReuse:false
  rm -f "$out/playwright.ps1" "$out"/*.staticwebassets.endpoints.json
  # O driver do Playwright (Node) só da plataforma do pacote: o publish traz também o da máquina que compila (uns 120 MB a mais).
  local plat
  case "$rid" in linux-x64) plat=linux-x64 ;; linux-arm64) plat=linux-arm64 ;; osx-arm64) plat=darwin-arm64 ;; osx-x64) plat=darwin-x64 ;; win-*) plat=win32_x64 ;; esac
  local node="$out/.playwright/node"
  if [ ! -d "$node/$plat" ]; then
    local pkg; pkg="$(ls -d "$HOME"/.nuget/packages/microsoft.playwright/*/.playwright/node 2>/dev/null | sort -V | tail -1)"
    mkdir -p "$node" && cp -r "$pkg/$plat" "$node/$plat"
  fi
  for d in "$node"/*/; do [ "$(basename "$d")" = "$plat" ] || rm -rf "$d"; done
}

linux() {
  local rid="$1" n="job-orchestrator-$V-$1" p="$D/job-orchestrator-$V-$1"
  publish "$rid" "$p" && resources "$p" "$rid" && chmod +x "$p/job-orchestrator"
  rm -f "$p.tar.gz" && tar -C "$D" -czf "$p.tar.gz" "$n"
  local i="$D/job-orchestrator-instalador$([ "$rid" = linux-x64 ] || echo "-${rid#linux-}").run"
  cat > "$i" <<'HEAD'
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
  echo "__PACOTE__" >> "$i" && cat "$p.tar.gz" >> "$i" && chmod +x "$i"
  echo "  $rid: $p ($(du -sh "$p" | cut -f1)), $(basename "$i") ($(du -h "$i" | cut -f1))"
}

mac() {
  local rid="$1" b="$D/build/$1" o="$D/job-orchestrator-$V-$1"
  publish "$rid" "$b"
  rm -rf "$o" && local app="$o/Job Orchestrator.app" && mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/app"
  # O binário (e as bibliotecas nativas, se houver) em Contents/MacOS; o que não é código em Contents/Resources/app, onde a assinatura espera.
  mv "$b/job-orchestrator" "$app/Contents/MacOS/" && find "$b" -maxdepth 1 -name '*.dylib' -exec mv {} "$app/Contents/MacOS/" \;
  (shopt -s dotglob; mv "$b"/* "$app/Contents/Resources/app/")
  resources "$app/Contents/Resources/app" "$rid"
  cp "$D/icons/AppIcon.icns" "$app/Contents/Resources/AppIcon.icns"
  cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Job Orchestrator</string>
  <key>CFBundleDisplayName</key><string>Job Orchestrator</string>
  <key>CFBundleIdentifier</key><string>app.joborchestrator</string>
  <key>CFBundleVersion</key><string>$V</string>
  <key>CFBundleShortVersionString</key><string>$V</string>
  <key>CFBundleExecutable</key><string>job-orchestrator</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.productivity</string>
  <key>LSUIElement</key><true/>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSHumanReadableCopyright</key><string>Job Orchestrator, uso local.</string>
</dict>
</plist>
PLIST
  chmod +x "$app/Contents/MacOS/job-orchestrator"
  rm -rf "$b" && rm -f "$o.zip" && (cd "$o" && zip -qry -X "$o.zip" "Job Orchestrator.app")
  echo "  $rid: $o.zip ($(du -h "$o.zip" | cut -f1)); assine e monte o .dmg com scripts/package-mac.sh $o.zip"
}

win() {
  local rid="$1" n="job-orchestrator-$V-$1" p="$D/job-orchestrator-$V-$1"
  publish "$rid" "$p" && resources "$p" "$rid"
  # Sobras do modelo de hospedagem no IIS (o app sobe o próprio Kestrel): não servem para nada aqui.
  rm -f "$p/web.config" "$p/aspnetcorev2_inprocess.dll"
  rm -f "$p.zip" && (cd "$D" && zip -qr "$p.zip" "$n")
  # Setup.exe: o executável, o resto do pacote num zip, o tamanho do zip (8 bytes, little-endian) e a marca JOSETUP1.
  local s="$D/JobOrchestrator-Setup-$V-${rid#win-}.exe" z="$D/build/payload-$rid.zip"
  mkdir -p "$D/build" && rm -f "$z" && (cd "$p" && zip -qr "$z" . -x job-orchestrator.exe)
  cat "$p/job-orchestrator.exe" "$z" > "$s"
  python3 -c "import struct,sys,os; sys.stdout.buffer.write(struct.pack('<q', os.path.getsize(sys.argv[1])) + b'JOSETUP1')" "$z" >> "$s"
  rm -f "$z"
  echo "  $rid: $(basename "$p.zip") ($(du -h "$p.zip" | cut -f1)), $(basename "$s") ($(du -h "$s" | cut -f1))"
}

for rid in "$@"; do
  echo "$rid"
  case "$rid" in linux-*) linux "$rid" ;; osx-*) mac "$rid" ;; win-*) win "$rid" ;; *) echo "alvo desconhecido: $rid" >&2; exit 2 ;; esac
done
rm -rf "$D/build"
(cd "$D" && sha256sum *.tar.gz *.run *.zip *.exe *.dmg 2>/dev/null > SHA256SUMS.txt || true)
echo "Somas em $D/SHA256SUMS.txt"
