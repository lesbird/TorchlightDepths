#!/usr/bin/env bash
# Mac App Store assets, rendered by the game itself (run on a Retina Mac):
#   appstore/screenshots/mac/   five 2880x1800 PNGs (--appstore)
#   appstore/preview/mac-1920x1080.mp4   29 s app preview, H.264 + AAC (--preview)
set -euo pipefail
cd "$(dirname "$0")/.."

TMP=$(mktemp -d); trap 'rm -rf "$TMP"' EXIT
dotnet build -c Release >/dev/null

dotnet run -c Release --no-build -- --appstore "$TMP/shots"
mkdir -p appstore/screenshots/mac
for f in "$TMP"/shots/*.bmp; do
  sips -s format png "$f" --out "appstore/screenshots/mac/$(basename "$f" .bmp).png" >/dev/null
done

dotnet run -c Release --no-build -- --preview "$TMP/preview"
swiftc -O tools/encode-preview.swift -o "$TMP/encode-preview" 2>/dev/null
mkdir -p appstore/preview
"$TMP/encode-preview" "$TMP/preview" appstore/preview/mac-1920x1080.mp4

sips -g pixelWidth -g pixelHeight appstore/screenshots/mac/*.png | grep -v '^ *$'
