#!/usr/bin/env bash
# Assina e monta o .dmg do Job Orchestrator num Mac (por SSH): ad-hoc, porque assinatura com Developer ID e notarização exigem a conta
# paga da Apple. Uso: scripts/package-mac.sh dist/job-orchestrator-<v>-osx-<arq>.zip [host]   (host padrão: mac)
# Gera, ao lado do zip, o .dmg (com o atalho para Aplicativos) e o zip assinado. Nada fica no Mac depois.
set -euo pipefail
Z="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"; H="${2:-mac}"; B="${Z%.zip}"; N="$(basename "$B")"
W="jo-build-$N"
ssh "$H" "rm -rf ~/$W && mkdir -p ~/$W"
scp -q "$Z" "$H:$W/app.zip"
ssh "$H" "set -e; cd ~/$W && ditto -x -k app.zip src && cd src \
  && codesign --force --deep --sign - 'Job Orchestrator.app' && codesign --verify --deep --strict 'Job Orchestrator.app' \
  && mkdir ../dmg && ditto 'Job Orchestrator.app' '../dmg/Job Orchestrator.app' && ln -s /Applications ../dmg/Aplicativos \
  && hdiutil create -quiet -volname 'Job Orchestrator' -srcfolder ../dmg -ov -format UDZO '../$N.dmg' \
  && ditto -c -k --keepParent 'Job Orchestrator.app' '../$N-assinado.zip'"
scp -q "$H:$W/$N.dmg" "$B.dmg" && scp -q "$H:$W/$N-assinado.zip" "$B-assinado.zip"
ssh "$H" "rm -rf ~/$W"
echo "DMG: $B.dmg ($(du -h "$B.dmg" | cut -f1)); zip assinado: $B-assinado.zip"
