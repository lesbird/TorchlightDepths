#!/usr/bin/env bash
#
# Build & package Torchlight Depths for macOS (arm64 + x64) and Windows (x64).
#   - each target is a single self-contained executable (runtime + native libs embedded)
#   - Windows is packaged as a .zip (Dungeon.exe + OpenAL-Soft license)
#   - macOS is wrapped in a .app bundle and packaged as a drag-to-install .dmg
#
# Usage:  ./build.sh [version]     (version defaults to 1.0.0)
# Requires: .NET SDK; macOS host for the .dmg step (hdiutil).
#
set -euo pipefail
cd "$(dirname "$0")"

VERSION="${1:-1.0.0}"
VOL="Torchlight Depths"
APP="TorchlightDepths"
BUNDLE_ID="com.lesbird.torchlightdepths"
DIST="dist"

SF=(-c Release --self-contained true
    -p:PublishSingleFile=true
    -p:IncludeNativeLibrariesForSelfExtract=true
    -p:EnableCompressionInSingleFile=true
    -p:DebugType=none)

rm -rf "$DIST"
mkdir -p "$DIST"

publish() { echo "== publish $1"; dotnet publish "${SF[@]}" -r "$1" -o "$DIST/$2" >/dev/null; }

# ---- Windows: single exe -> zip ------------------------------------------
package_windows() {
  publish win-x64 windows-x64
  local stage="$DIST/.stage-win"
  rm -rf "$stage"; mkdir -p "$stage"
  cp "$DIST/windows-x64/Dungeon.exe" "$stage/"
  cp native/win-x64/OpenAL-Soft-LICENSE.txt "$stage/"
  ( cd "$stage" && zip -q -X "../$APP-windows-x64.zip" Dungeon.exe OpenAL-Soft-LICENSE.txt )
  rm -rf "$stage"
  echo "-> $DIST/$APP-windows-x64.zip"
}

# ---- macOS: single exe -> .app -> .dmg -----------------------------------
package_macos() { # arch  min-macos-version
  local arch="$1" minver="$2"
  publish "osx-$arch" "macos-$arch"

  local stage="$DIST/.stage-$arch"
  local appdir="$stage/$APP.app"
  rm -rf "$stage"; mkdir -p "$appdir/Contents/MacOS" "$appdir/Contents/Resources"

  cp "$DIST/macos-$arch/Dungeon" "$appdir/Contents/MacOS/Dungeon"
  chmod +x "$appdir/Contents/MacOS/Dungeon"
  [ -f build/AppIcon.icns ] && cp build/AppIcon.icns "$appdir/Contents/Resources/AppIcon.icns"

  cat > "$appdir/Contents/Info.plist" <<PLIST
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
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>$minver</string>
  <key>NSHighResolutionCapable</key><true/>
$( [ -f build/AppIcon.icns ] && echo '  <key>CFBundleIconFile</key><string>AppIcon</string>' )
</dict>
</plist>
PLIST

  ln -s /Applications "$stage/Applications"
  rm -f "$DIST/$APP-macos-$arch.dmg"
  hdiutil create -quiet -volname "$VOL" -srcfolder "$stage" -ov -format UDZO "$DIST/$APP-macos-$arch.dmg"
  rm -rf "$stage"
  echo "-> $DIST/$APP-macos-$arch.dmg"
}

package_windows
if [[ "$(uname)" == "Darwin" ]]; then
  package_macos arm64 11.0
  package_macos x64 10.15
else
  echo "!! skipping .dmg (needs a macOS host); macOS single-file binaries are in $DIST/macos-*"
fi

echo
echo "artifacts:"
ls -1 "$DIST"/*.zip "$DIST"/*.dmg 2>/dev/null
