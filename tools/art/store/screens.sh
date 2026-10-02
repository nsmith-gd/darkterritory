#!/usr/bin/env bash
# The store's screenshots at L0 (GDD §32's test: "a steam train of desperate workers crossing a diseased frontier at
# night"), 1920x1080, rendered headless by dt into out/store/screens (not shipped), and the capsules over the first
# (tools/art/store/capsule.py). Each a moment the game has: the train in the fog, the crew at work on its roofs, a fire in a
# car, the cannon's shot, the Choir over the guard van, the stores by lamplight, the dawn coming.
#   tools/art/store/screens.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../../.."
out=out/store/screens
mkdir -p "$out" out/store/shots
dt() { dotnet run --no-build --project src/DarkTerritory.Cli -- "$@" >/dev/null; }
dotnet build src/DarkTerritory.Cli >/dev/null
shot() { local name=$1; shift; dt screenshot "$@" --width 1920 --height 1080 --out "$out/$name.png"; echo "[dt] $out/$name.png"; }
shot 01-train-in-the-fog --working --threats --view trackside
shot 02-crew-on-the-roofs --working --view crewside
shot 03-fire-in-the-car --threats --view fire
shot 04-the-cannon --muzzle --shot-age 0.033 --view cannonside
shot 05-the-choir --threats --view choir
shot 06-the-stores --stocked --lantern --view locker
shot 07-the-mail-crane --route frontier:7 --at 1620 --cam 1631,0.2,2.4 --target 1637,3.0,2.4
shot 08-dawn --working --view trackside --dawn 1
dt screenshot --working --threats --view trackside --width 1232 --height 706 --out out/store/shots/trackside.png
python3 tools/art/store/capsule.py out/store/shots/trackside.png
python3 tools/art/store/icon.py
