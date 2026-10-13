#!/usr/bin/env bash
# Build Torchlight Depths for the iOS Simulator and launch it.
#
#   ios/run-sim.sh                          iPhone 17 Pro (default)
#   ios/run-sim.sh "iPad Pro 13-inch (M5)"  any simulator name from `xcrun simctl list devices`
#   ios/run-sim.sh "iPhone 17" --capture    scripted touch walkthrough; screenshots land in
#                                           the app's Documents/capture folder (path printed)
#   ios/run-sim.sh "iPhone 17" --fakestore  gold shop with a stand-in store: the Simulator has
#                                           no App Store products, so purchases always succeed
#   ios/run-sim.sh "iPhone 17" --fakegc     leaderboards panel filled with made-up rivals
#
# Requires the .NET iOS workload (`dotnet workload install ios`) and Xcode.
# Note: the Simulator renders OpenGL ES on the CPU, so expect a low frame rate there —
# real iPhones/iPads run it on the GPU.
set -euo pipefail
cd "$(dirname "$0")"

DEVICE="${1:-iPhone 17 Pro}"
shift || true
BUNDLE_ID="com.lesbird.torchlightdepths"

UDID=$(xcrun simctl list devices available | grep -F "$DEVICE (" | head -1 | grep -oE '[0-9A-F-]{36}')
if [[ -z "$UDID" ]]; then
  echo "error: no available simulator named '$DEVICE'" >&2
  exit 1
fi

echo "Building for the simulator..."
dotnet build -c Debug -r iossimulator-arm64 -v quiet -nologo

xcrun simctl boot "$UDID" 2>/dev/null || true
open -a Simulator
xcrun simctl install "$UDID" bin/Debug/net10.0-ios/iossimulator-arm64/TorchlightDepths.app
xcrun simctl launch --terminate-running-process "$UDID" "$BUNDLE_ID" "$@"

if [[ " $* " == *" --capture "* ]]; then
  echo "screenshots: $(xcrun simctl get_app_container "$UDID" "$BUNDLE_ID" data)/Documents/capture"
fi
