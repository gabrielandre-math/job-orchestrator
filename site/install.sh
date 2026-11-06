#!/bin/sh
# Installs Job Orchestrator on macOS or Linux from the latest GitHub release, after checking the file's SHA-256 against
# the SHA256SUMS.txt published with it. Then it opens the app's own installer, the same one the downloads run.
#
#   curl -fsSL https://gabrielandre-math.github.io/job-orchestrator/install.sh | sh
#
# Read it before running it: https://github.com/gabrielandre-math/job-orchestrator/blob/main/site/install.sh
set -eu

RELEASE="https://github.com/gabrielandre-math/job-orchestrator/releases/latest/download"

case "$(uname -m)" in
  x86_64 | amd64) arch=x64 ;;
  arm64 | aarch64) arch=arm64 ;;
  *) echo "Job Orchestrator has no build for this processor ($(uname -m))." >&2; exit 1 ;;
esac
case "$(uname -s)" in
  Darwin) os=macos; file="job-orchestrator-macos-$arch.dmg" ;;
  Linux) os=linux; file="job-orchestrator-linux-$arch.run" ;;
  *) echo "This script is for macOS and Linux. On Windows, use install.ps1." >&2; exit 1 ;;
esac

tmp="$(mktemp -d "${TMPDIR:-/tmp}/job-orchestrator.XXXXXX")"
trap 'rm -rf "$tmp"' EXIT INT TERM

echo "Downloading $file"
curl -fL --progress-bar "$RELEASE/$file" -o "$tmp/$file"
curl -fsSL "$RELEASE/SHA256SUMS.txt" -o "$tmp/SHA256SUMS.txt"

expected="$(awk -v f="$file" '$2 == f { print $1 }' "$tmp/SHA256SUMS.txt")"
if command -v sha256sum >/dev/null 2>&1; then actual="$(sha256sum "$tmp/$file" | awk '{ print $1 }')"
else actual="$(shasum -a 256 "$tmp/$file" | awk '{ print $1 }')"; fi
if [ -z "$expected" ] || [ "$expected" != "$actual" ]; then
  echo "The SHA-256 of $file does not match SHA256SUMS.txt. Nothing was installed." >&2
  exit 1
fi
echo "SHA-256 checked"

if [ "${JO_INSTALL_DRY_RUN:-}" = 1 ]; then echo "Dry run: stopping before the installer"; exit 0; fi

if [ "$os" = macos ]; then
  mnt="$tmp/volume"
  mkdir -p "$mnt"
  hdiutil attach -quiet -nobrowse -readonly -mountpoint "$mnt" "$tmp/$file"
  ditto "$mnt/Job Orchestrator.app" "$tmp/Job Orchestrator.app"
  hdiutil detach -quiet "$mnt"
  "$tmp/Job Orchestrator.app/Contents/MacOS/job-orchestrator" --setup
else
  chmod +x "$tmp/$file"
  "$tmp/$file"
fi
