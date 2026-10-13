#!/usr/bin/env bash
# Build, sign, install and launch Torchlight Depths on a connected iPhone / iPad.
#
#   ios/run-device.sh                 first connected device
#   ios/run-device.sh "iPadAir4"      device whose name contains this text
#
# The device must be paired with this Mac (USB or same Wi-Fi) with Developer Mode on.
#
# Signing: `dotnet build` signs with a development certificate and the team's wildcard
# profile, and the app is then re-signed through Xcode's automatic signing
# (`xcodebuild -exportArchive`, method "debugging"). That gets a development profile
# for com.lesbird.torchlightdepths itself, which a wildcard profile can't be: Game Center
# needs it. It uses the Apple account signed in to Xcode, and the device must be
# registered on the team. Override the intermediate signing with CODESIGN_KEY /
# CODESIGN_PROVISION, and the team with TEAM_ID.
set -euo pipefail
cd "$(dirname "$0")"

MATCH="${1:-}"
BUNDLE_ID="com.lesbird.torchlightdepths"
TEAM_ID="${TEAM_ID:-A75V2S2HM3}"
CODESIGN_KEY="${CODESIGN_KEY:-Apple Development: Les Bird (3SN9RY3AUR)}"
CODESIGN_PROVISION="${CODESIGN_PROVISION:-9b02843d-d757-4a01-a5fa-8ba82ea71d34}" # team "Les Bird" wildcard

# pick a connected device (the identifier is the third column of `devicectl list devices`)
DEVICE=$(xcrun devicectl list devices 2>/dev/null | grep -E "connected|available \(paired\)" \
         | grep -F -- "$MATCH" | head -1 | grep -oE '[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}' | head -1 || true)
if [[ -z "$DEVICE" ]]; then
  echo "error: no connected device${MATCH:+ matching '$MATCH'} (see: xcrun devicectl list devices)" >&2
  exit 1
fi

BIN=bin/Release/net10.0-ios/ios-arm64
OUT=bin/DevExport
ARCHIVE=$OUT/TorchlightDepths.xcarchive

echo "Building..."
dotnet build -c Release -r ios-arm64 -v quiet -nologo \
  -p:CodesignKey="$CODESIGN_KEY" -p:CodesignProvision="$CODESIGN_PROVISION"

echo "Signing..."
rm -rf "$OUT"
mkdir -p "$ARCHIVE/Products/Applications"
cp -R "$BIN/TorchlightDepths.app" "$ARCHIVE/Products/Applications/"
cat > "$ARCHIVE/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>ApplicationProperties</key>
	<dict>
		<key>ApplicationPath</key><string>Applications/TorchlightDepths.app</string>
		<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
		<key>SigningIdentity</key><string>$CODESIGN_KEY</string>
		<key>Team</key><string>$TEAM_ID</string>
	</dict>
	<key>ArchiveVersion</key><integer>2</integer>
	<key>CreationDate</key><date>$(date -u +%Y-%m-%dT%H:%M:%SZ)</date>
	<key>Name</key><string>TorchlightDepths</string>
	<key>SchemeName</key><string>TorchlightDepths</string>
</dict>
</plist>
EOF
cat > "$OUT/ExportOptions.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key><string>debugging</string>
	<key>signingStyle</key><string>automatic</string>
	<key>teamID</key><string>$TEAM_ID</string>
	<key>thinning</key><string>&lt;none&gt;</string>
</dict>
</plist>
EOF
xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportOptionsPlist "$OUT/ExportOptions.plist" \
  -exportPath "$OUT/export" -allowProvisioningUpdates -quiet 2>&1 | grep -v IDEDistributionLogging || true
unzip -q -o "$OUT/export/TorchlightDepths.ipa" -d "$OUT/export"

echo "Installing on $DEVICE..."
xcrun devicectl device install app --device "$DEVICE" "$OUT/export/Payload/TorchlightDepths.app" >/dev/null
xcrun devicectl device process launch --device "$DEVICE" "$BUNDLE_ID"
