#!/usr/bin/env bash
# Build a Mac App Store .pkg and optionally upload it.
#
#   macos/build-appstore.sh                  build; build number = timestamp
#   macos/build-appstore.sh 7                build with build number 7
#   macos/build-appstore.sh 7 --upload       build and upload to App Store Connect
#
# Same approach as ios/build-appstore.sh: `dotnet publish` makes a self-contained arm64
# single-file executable, the script wraps it in a sandboxed .app (signed for
# development) inside an Xcode archive, and `xcodebuild -exportArchive` re-signs it for
# the Mac App Store with automatic signing (Xcode's account, cloud-managed certificates,
# a Mac App Store profile created on demand) and produces the signed installer .pkg.
#
# Output: macos/bin/AppStore/export/Torchlight Depths.pkg (Transporter can upload it too).
# Apple Silicon only (arm64): the App Store lists it for M-series Macs.
set -euo pipefail
cd "$(dirname "$0")"

BUILD="${1:-$(date +%Y%m%d%H%M)}"
UPLOAD="${2:-}"
VERSION="${VERSION:-1.0.0}"
TEAM_ID="${TEAM_ID:-A75V2S2HM3}"
BUNDLE_ID=com.lesbird.torchlightdepths
CODESIGN_KEY="${CODESIGN_KEY:-Apple Development: Les Bird (3SN9RY3AUR)}"

OUT=bin/AppStore
PUB=$OUT/publish
ARCHIVE=$OUT/TorchlightDepths.xcarchive
APP="$ARCHIVE/Products/Applications/Torchlight Depths.app"

echo "Building $VERSION ($BUILD)..."
rm -rf "$OUT"
dotnet publish ../Dungeon2.csproj -c Release -r osx-arm64 --self-contained true -v quiet -nologo \
  -p:PublishSingleFile=true -p:DebugType=none -o "$PUB"

echo "Assembling the app..."
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$ARCHIVE/dSYMs"
cp "$PUB/Dungeon" "$PUB/libglfw.3.dylib" "$APP/Contents/MacOS/"
cp ../ios/PrivacyInfo.xcprivacy "$APP/Contents/Resources/"

# icon: the iOS 1024 artwork, cut into an .icns
ICONSET=$OUT/AppIcon.iconset; mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s ../ios/Assets.xcassets/AppIcon.appiconset/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) ../ios/Assets.xcassets/AppIcon.appiconset/AppIcon-1024.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key><string>Torchlight Depths</string>
	<key>CFBundleDisplayName</key><string>Torchlight Depths</string>
	<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
	<key>CFBundleExecutable</key><string>Dungeon</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleVersion</key><string>$BUILD</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundleSupportedPlatforms</key><array><string>MacOSX</string></array>
	<key>LSMinimumSystemVersion</key><string>11.0</string>
	<key>LSApplicationCategoryType</key><string>public.app-category.role-playing-games</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>NSHumanReadableCopyright</key><string>© $(date +%Y) Les Bird</string>
	<key>ITSAppUsesNonExemptEncryption</key><false/>
</dict>
</plist>
PLIST

echo "Signing for development..."
codesign --force --timestamp=none --sign "$CODESIGN_KEY" "$APP/Contents/MacOS/libglfw.3.dylib"
codesign --force --timestamp=none --sign "$CODESIGN_KEY" --entitlements Entitlements.plist "$APP"
codesign --verify --strict --deep "$APP"

cat > "$ARCHIVE/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>ApplicationProperties</key>
	<dict>
		<key>ApplicationPath</key><string>Applications/Torchlight Depths.app</string>
		<key>Architectures</key><array><string>arm64</string></array>
		<key>CFBundleIdentifier</key><string>$BUNDLE_ID</string>
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
PLIST

DESTINATION=export
[[ "$UPLOAD" == "--upload" ]] && DESTINATION=upload
cat > "$OUT/ExportOptions.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>method</key><string>app-store-connect</string>
	<key>destination</key><string>$DESTINATION</string>
	<key>signingStyle</key><string>automatic</string>
	<key>teamID</key><string>$TEAM_ID</string>
	<key>manageAppVersionAndBuildNumber</key><false/>
</dict>
</plist>
PLIST

echo "Signing for the Mac App Store${UPLOAD:+ and uploading}..."
xcodebuild -exportArchive -archivePath "$ARCHIVE" -exportOptionsPlist "$OUT/ExportOptions.plist" \
  -exportPath "$OUT/export" -allowProvisioningUpdates -quiet

if [[ "$DESTINATION" == upload ]]; then
  echo "Uploaded $VERSION ($BUILD). It shows up in App Store Connect / TestFlight after processing."
else
  echo "-> macos/$OUT/export/Torchlight Depths.pkg  ($VERSION build $BUILD)"
  echo "Upload it with Apple's Transporter app, or rerun with --upload."
fi
