#!/bin/bash
# Builds the game for players (roadmap M6): a self-contained folder per platform with its content and how to play,
# zipped into out/dist/. CI runs this on every push and keeps the zips as artifacts.
# Usage: tools/package.sh [--demo] [--dev] [win-x64] [linux-x64]   (both by default)
#   --demo   the demo as well (T79): the same build beside it, DarkTerritory-Demo-<rid>, with the demo edition
#            (editions/demo) baked into its content
#   --dev    the director's test build (note 514): the developer tools in, DarkTerritory-Dev-<rid> (never a store's: upload.sh
#            sends only DarkTerritory-<rid>, and checks it). Without it, a player's build: built with DevTools=false, so
#            none of the developer tools are in it, and `dt build check` fails the package if any are.
set -euo pipefail
cd "$(dirname "$0")/.."
demo=0 dev=0 rids=()
for a in "$@"; do
  case "$a" in
    --demo) demo=1 ;;
    --dev) dev=1 ;;
    *) rids+=("$a") ;;
  esac
done
if [ "$dev" = 1 ]; then name=DarkTerritory-Dev devtools=true check=--dev; else name=DarkTerritory devtools=false check=; fi
[ ${#rids[@]} -eq 0 ] && rids=(win-x64 linux-x64)
mkdir -p out/dist
for rid in "${rids[@]}"; do
  dir="out/dist/$name-$rid"
  rm -rf "$dir" "$dir.zip"
  # Debug info embedded in the assemblies (note 452): a player's crash report names the file and line of every frame.
  dotnet publish src/DarkTerritory.App -c Release -r "$rid" --self-contained true -o "$dir" -p:DebugType=embedded -p:DevTools=$devtools -nologo -v quiet
  test -f "$dir/content/tuning/train.json" || { echo "no content in $dir" >&2; exit 1; }
  # A player's build has none of the developer tools in it, a test build all of them (note 514).
  dotnet run --project src/DarkTerritory.Cli -c Release -- build check "$dir" $check > "out/dist/$name-$rid.check.json" \
    || { cat "out/dist/$name-$rid.check.json"; echo "$dir failed its build check" >&2; exit 1; }
  # Everyone whose work is in the game (note 390), at the top where a player looks; content/credits holds it and the texts.
  cp "$dir/content/credits/THIRD-PARTY-NOTICES.txt" "$dir/"
  (cd out/dist && zip -qr "$name-$rid.zip" "$name-$rid")
  echo "$dir.zip: $(du -h "$dir.zip" | cut -f1)"
  if [ "$demo" = 1 ]; then
    d="out/dist/${name/DarkTerritory/DarkTerritory-Demo}-$rid"
    rm -rf "$d" "$d.zip"
    cp -r "$dir" "$d"
    dotnet run --project src/DarkTerritory.Cli -c Release -- edition bake demo --into "$d/content" > /dev/null
    grep -q '"demo": true' "$d/content/tuning/edition.json" || { echo "the demo edition isn't in $d" >&2; exit 1; }
    (cd out/dist && zip -qr "$(basename "$d").zip" "$(basename "$d")")
    echo "$d.zip: $(du -h "$d.zip" | cut -f1)"
  fi
done
