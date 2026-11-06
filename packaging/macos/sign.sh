#!/bin/bash
# Runs on a Mac: signs Job Orchestrator.app and builds the .dmg. With a "Developer ID Application" certificate in the keychain
# and a notarytool profile (JO_NOTARY_PROFILE, default jo-notary), the app and the .dmg are signed with the hardened runtime,
# notarized by Apple and stapled, so Gatekeeper opens them without a warning. Without them, both are signed ad hoc.
set -euo pipefail
N="$1"; HERE="$(cd "$(dirname "$0")" && pwd)"; APP="Job Orchestrator.app"; PROFILE="${JO_NOTARY_PROFILE:-jo-notary}"
ID="${JO_MAC_IDENTITY:-$(security find-identity -v -p codesigning | sed -n 's/.*"\(Developer ID Application: [^"]*\)".*/\1/p' | head -1)}"
cd "$HERE" && ditto -x -k app.zip src && cd src
NOTARIZED=0
if [ -n "$ID" ]; then
  echo "signing with $ID"
  # Every Mach-O inside the bundle first (native libraries, the browser driver's node), then the bundle itself.
  find "$APP/Contents" -type f -print0 | while IFS= read -r -d '' f; do
    if file -b "$f" | grep -q "Mach-O"; then codesign --force --options runtime --timestamp --entitlements "$HERE/entitlements.plist" --sign "$ID" "$f"; fi
  done
  codesign --force --options runtime --timestamp --entitlements "$HERE/entitlements.plist" --sign "$ID" "$APP"
  codesign --verify --deep --strict --verbose=2 "$APP"
  if xcrun notarytool history --keychain-profile "$PROFILE" >/dev/null 2>&1; then
    ditto -c -k --keepParent "$APP" ../notarize.zip
    xcrun notarytool submit ../notarize.zip --keychain-profile "$PROFILE" --wait
    xcrun stapler staple "$APP"
    NOTARIZED=1
  else
    echo "no notarytool profile '$PROFILE': signed with Developer ID, not notarized"
  fi
else
  echo "no Developer ID certificate: signing ad hoc"
  codesign --force --deep --sign - "$APP"
  codesign --verify --deep --strict "$APP"
fi
mkdir ../dmg && ditto "$APP" "../dmg/$APP" && ln -s /Applications ../dmg/Aplicativos
hdiutil create -quiet -volname 'Job Orchestrator' -srcfolder ../dmg -ov -format UDZO "../$N.dmg"
if [ -n "$ID" ]; then
  codesign --force --timestamp --sign "$ID" "../$N.dmg"
  if [ "$NOTARIZED" = 1 ]; then
    xcrun notarytool submit "../$N.dmg" --keychain-profile "$PROFILE" --wait
    xcrun stapler staple "../$N.dmg"
    spctl --assess --type open --context context:primary-signature --verbose "../$N.dmg"
    spctl --assess --type execute --verbose "$APP"
  fi
fi
ditto -c -k --keepParent "$APP" "../$N-assinado.zip"
