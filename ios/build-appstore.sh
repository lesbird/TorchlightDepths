#!/usr/bin/env bash
# Build a signed App Store .ipa (no Xcode project needed) and optionally upload it.
#
#   ios/build-appstore.sh                  build; build number = timestamp
#   ios/build-appstore.sh 7                build with build number 7
#   ios/build-appstore.sh 7 --upload       build, validate, and upload to App Store Connect
#
# One-time setup (see README "Publishing to the App Store"):
#   * an "Apple Distribution" certificate with its private key in this Mac's keychain
#   * an App Store provisioning profile for com.lesbird.torchlightdepths, installed
#   * the app record created in App Store Connect
#   * for --upload: an App Store Connect API key, with
#       ASC_KEY_ID / ASC_ISSUER_ID set and AuthKey_<ASC_KEY_ID>.p8 in ~/.appstoreconnect/private_keys/
#
# CODESIGN_KEY / CODESIGN_PROVISION override the identity / profile (name or UUID).
# Each upload needs a build number higher than the last; the marketing version
# comes from ApplicationDisplayVersion in the .csproj.
set -euo pipefail
cd "$(dirname "$0")"

BUILD="${1:-$(date +%Y%m%d%H%M)}"
UPLOAD="${2:-}"
CODESIGN_KEY="${CODESIGN_KEY:-Apple Distribution}"
PROVISION_ARGS=()
[[ -n "${CODESIGN_PROVISION:-}" ]] && PROVISION_ARGS=(-p:CodesignProvision="$CODESIGN_PROVISION")

echo "Publishing build $BUILD..."
rm -rf bin/Release/net10.0-ios/ios-arm64/publish
dotnet publish -c Release -r ios-arm64 -v quiet -nologo \
  -p:ApplicationVersion="$BUILD" \
  -p:CodesignKey="$CODESIGN_KEY" "${PROVISION_ARGS[@]}"

IPA=$(ls bin/Release/net10.0-ios/ios-arm64/publish/*.ipa)
echo "-> $IPA"

if [[ "$UPLOAD" == "--upload" ]]; then
  : "${ASC_KEY_ID:?set ASC_KEY_ID (App Store Connect API key id)}"
  : "${ASC_ISSUER_ID:?set ASC_ISSUER_ID (App Store Connect API issuer id)}"
  echo "Validating..."
  xcrun altool --validate-app -f "$IPA" -t ios --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"
  echo "Uploading..."
  xcrun altool --upload-app -f "$IPA" -t ios --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"
  echo "Uploaded. The build shows up in App Store Connect / TestFlight after processing."
else
  echo "Upload it with Apple's Transporter app (drag the .ipa in), or rerun with --upload."
fi
