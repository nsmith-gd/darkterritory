#!/bin/bash
# Builds the game for players (roadmap M6): a self-contained folder per platform with its content and how to play,
# zipped into out/dist/. CI runs this on every push and keeps the zips as artifacts.
# Usage: tools/package.sh [--demo] [win-x64] [linux-x64]   (both by default)
#   --demo   the demo as well (T79): the same build beside it, DarkTerritory-Demo-<rid>, with the demo edition
#            (editions/demo) baked into its content
set -euo pipefail
cd "$(dirname "$0")/.."
demo=0 rids=()
for a in "$@"; do
  case "$a" in
    --demo) demo=1 ;;
    *) rids+=("$a") ;;
  esac
done
[ ${#rids[@]} -eq 0 ] && rids=(win-x64 linux-x64)
mkdir -p out/dist
for rid in "${rids[@]}"; do
  dir="out/dist/DarkTerritory-$rid"
  rm -rf "$dir" "$dir.zip"
  dotnet publish src/DarkTerritory.App -c Release -r "$rid" --self-contained true -o "$dir" -p:DebugType=none -nologo -v quiet
  test -f "$dir/content/tuning/train.json" || { echo "no content in $dir" >&2; exit 1; }
  # Everyone whose work is in the game (note 390), at the top where a player looks; content/credits holds it and the texts.
  cp "$dir/content/credits/THIRD-PARTY-NOTICES.txt" "$dir/"
  (cd out/dist && zip -qr "DarkTerritory-$rid.zip" "DarkTerritory-$rid")
  echo "$dir.zip: $(du -h "$dir.zip" | cut -f1)"
  if [ "$demo" = 1 ]; then
    d="out/dist/DarkTerritory-Demo-$rid"
    rm -rf "$d" "$d.zip"
    cp -r "$dir" "$d"
    dotnet run --project src/DarkTerritory.Cli -c Release -- edition bake demo --into "$d/content" > /dev/null
    grep -q '"demo": true' "$d/content/tuning/edition.json" || { echo "the demo edition isn't in $d" >&2; exit 1; }
    (cd out/dist && zip -qr "DarkTerritory-Demo-$rid.zip" "DarkTerritory-Demo-$rid")
    echo "$d.zip: $(du -h "$d.zip" | cut -f1)"
  fi
done
