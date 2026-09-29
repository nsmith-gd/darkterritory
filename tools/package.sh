#!/bin/bash
# Builds the game for players (roadmap M6): a self-contained folder per platform with its content and how to play,
# zipped into out/dist/. CI runs this on every push and keeps the zips as artifacts.
# Usage: tools/package.sh [win-x64] [linux-x64]   (both by default)
set -euo pipefail
cd "$(dirname "$0")/.."
rids=("$@")
[ ${#rids[@]} -eq 0 ] && rids=(win-x64 linux-x64)
mkdir -p out/dist
for rid in "${rids[@]}"; do
  dir="out/dist/DarkTerritory-$rid"
  rm -rf "$dir" "$dir.zip"
  dotnet publish src/DarkTerritory.App -c Release -r "$rid" --self-contained true -o "$dir" -p:DebugType=none -nologo -v quiet
  test -f "$dir/content/tuning/train.json" || { echo "no content in $dir" >&2; exit 1; }
  (cd out/dist && zip -qr "DarkTerritory-$rid.zip" "DarkTerritory-$rid")
  echo "$dir.zip: $(du -h "$dir.zip" | cut -f1)"
done
