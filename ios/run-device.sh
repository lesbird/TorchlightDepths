#!/usr/bin/env bash
# Build, sign, install and launch Torchlight Depths on a connected iPhone / iPad.
#
#   ios/run-device.sh                 first connected device
#   ios/run-device.sh "iPadAir4"      device whose name contains this text
#
# The device must be paired with this Mac (USB or same Wi-Fi) with Developer Mode on.
# Signing uses an Apple Development certificate plus a development provisioning
# profile covering this bundle id (a wildcard "*" team profile works) that lists the
# device. Override the defaults with CODESIGN_KEY / CODESIGN_PROVISION (profile UUID
# or name); `security find-identity -v -p codesigning` lists your certificates.
set -euo pipefail
cd "$(dirname "$0")"

MATCH="${1:-}"
BUNDLE_ID="com.lesbird.torchlightdepths"
CODESIGN_KEY="${CODESIGN_KEY:-Apple Development: Les Bird (3SN9RY3AUR)}"
CODESIGN_PROVISION="${CODESIGN_PROVISION:-9b02843d-d757-4a01-a5fa-8ba82ea71d34}" # team "Les Bird" wildcard

# pick a connected device (the identifier is the third column of `devicectl list devices`)
DEVICE=$(xcrun devicectl list devices 2>/dev/null | grep -E "connected|available \(paired\)" \
         | grep -F -- "$MATCH" | head -1 | grep -oE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -1)
if [[ -z "$DEVICE" ]]; then
  echo "error: no connected device${MATCH:+ matching '$MATCH'} (see: xcrun devicectl list devices)" >&2
  exit 1
fi

echo "Building and signing..."
dotnet build -c Release -r ios-arm64 -v quiet -nologo \
  -p:CodesignKey="$CODESIGN_KEY" -p:CodesignProvision="$CODESIGN_PROVISION"

echo "Installing on $DEVICE..."
xcrun devicectl device install app --device "$DEVICE" bin/Release/net10.0-ios/ios-arm64/TorchlightDepths.app >/dev/null
xcrun devicectl device process launch --device "$DEVICE" "$BUNDLE_ID"
