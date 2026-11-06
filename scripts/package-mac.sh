#!/usr/bin/env bash
# Signs the macOS build and builds its .dmg on a Mac over SSH (packaging/macos/sign.sh). With a Developer ID certificate and a
# notarytool profile on that Mac the result is notarized by Apple; without them it is signed ad hoc.
# Usage: scripts/package-mac.sh dist/job-orchestrator-<v>-osx-<arch>.zip [host]   (default host: mac)
# Writes the .dmg and the signed zip next to the input zip. Nothing is left on the Mac.
set -euo pipefail
R="$(cd "$(dirname "$0")/.." && pwd)"
Z="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"; H="${2:-mac}"; B="${Z%.zip}"; N="$(basename "$B")"
W="jo-build-$N"
ssh "$H" "rm -rf ~/$W && mkdir -p ~/$W"
scp -q "$Z" "$H:$W/app.zip"
scp -q "$R/packaging/macos/sign.sh" "$R/packaging/macos/entitlements.plist" "$H:$W/"
ssh "$H" "bash ~/$W/sign.sh '$N'"
scp -q "$H:$W/$N.dmg" "$B.dmg" && scp -q "$H:$W/$N-assinado.zip" "$B-assinado.zip"
ssh "$H" "rm -rf ~/$W"
echo "DMG: $B.dmg ($(du -h "$B.dmg" | cut -f1)); zip assinado: $B-assinado.zip"
