#!/usr/bin/env bash
# Build an App Store .ipa (no Xcode project needed) and optionally upload it.
#
#   ios/build-appstore.sh                  build; build number = timestamp
#   ios/build-appstore.sh 7                build with build number 7
#   ios/build-appstore.sh 7 --upload       build and upload to App Store Connect
#
# How it works: `dotnet publish` builds the app (signed for development), the script wraps
# it in an Xcode archive, and `xcodebuild -exportArchive` re-signs it for the App Store with
# automatic signing. That uses the Apple account signed in to Xcode (Settings → Accounts):
# Xcode's cloud-managed Apple Distribution certificate and an App Store profile, which it
# creates or downloads as needed. You don't need a distribution certificate in your
# keychain. --upload sends it the same way. You need the app record in App Store Connect first.
#
# Output: ios/bin/AppStore/export/TorchlightDepths.ipa (drag into Transporter to upload by hand).
# Each upload needs a build number higher than the last; the marketing version comes from
# ApplicationDisplayVersion in the .csproj.
set -euo pipefail
cd "$(dirname "$0")"

BUILD="${1:-$(date +%Y%m%d%H%M)}"
UPLOAD="${2:-}"
TEAM_ID="${TEAM_ID:-A75V2S2HM3}"
# development signing for the intermediate build (re-signed on export)
CODESIGN_KEY="${CODESIGN_KEY:-Apple Development: Les Bird (3SN9RY3AUR)}"
CODESIGN_PROVISION="${CODESIGN_PROVISION:-9b02843d-d757-4a01-a5fa-8ba82ea71d34}"

BIN=bin/Release/net10.0-ios/ios-arm64
OUT=bin/AppStore
ARCHIVE=$OUT/TorchlightDepths.xcarchive

echo "Building $BUILD..."
rm -rf "$BIN" "$OUT"
dotnet publish -c Release -r ios-arm64 -v quiet -nologo \
  -p:ApplicationVersion="$BUILD" \
  -p:CodesignKey="$CODESIGN_KEY" -p:CodesignProvision="$CODESIGN_PROVISION"

echo "Archiving..."
mkdir -p "$ARCHIVE/Products/Applications" "$ARCHIVE/dSYMs"
cp -R "$BIN/TorchlightDepths.app" "$ARCHIVE/Products/Applications/"
cp -R "$BIN/TorchlightDepths.app.dSYM" "$ARCHIVE/dSYMs/"
APP_PLIST="$ARCHIVE/Products/Applications/TorchlightDepths.app/Info.plist"
VERSION=$(plutil -extract CFBundleShortVersionString raw "$APP_PLIST")
cat > "$ARCHIVE/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>ApplicationProperties</key>
	<dict>
		<key>ApplicationPath</key><string>Applications/TorchlightDepths.app</string>
		<key>Architectures</key><array><string>arm64</string></array>
		<key>CFBundleIdentifier</key><string>com.lesbird.torchlightdepths</string>
		<key>CFBundleShortVersionString</key><string>$VERSION</string>
		<key>CFBundleVersion</key><string>$BUILD</string>
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

DESTINATION=export
[[ "$UPLOAD" == "--upload" ]] && DESTINATION=upload
cat > "$OUT/ExportOptions.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key><string>app-store-connect</string>
	<key>destination</key><string>$DESTINATION</string>
	<key>signingStyle</key><string>automatic</string>
	<key>teamID</key><string>$TEAM_ID</string>
	<key>uploadSymbols</key><true/>
	<key>manageAppVersionAndBuildNumber</key><false/>
</dict>
</plist>
EOF

echo "Signing for the App Store${UPLOAD:+ and uploading}..."
xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportOptionsPlist "$OUT/ExportOptions.plist" \
  -exportPath "$OUT/export" -allowProvisioningUpdates -quiet

if [[ "$DESTINATION" == upload ]]; then
  echo "Uploaded $VERSION ($BUILD). It shows up in App Store Connect / TestFlight after processing."
else
  echo "-> ios/$OUT/export/TorchlightDepths.ipa  ($VERSION build $BUILD)"
  echo "Upload it with Apple's Transporter app (drag the .ipa in), or rerun with --upload."
fi
