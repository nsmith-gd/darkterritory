#!/usr/bin/env bash
# The store's screenshots at L0 (GDD §32's test: "a steam train of desperate workers crossing a diseased frontier at
# night"), 1920x1080, rendered headless by dt into out/store/screens (not shipped), and the capsules over the first
# (tools/art/store/capsule.py). Each a moment the game has: the train in the fog, the crew at work on its roofs, a fire in a
# car, a Car Hugger eating the rear car, the Choir over the guard van, the Switchman by the line with his lantern, an iron
# truss over the water, the dawn.
#   tools/art/store/screens.sh
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/../../.."
out=out/store/screens
mkdir -p "$out" out/store/shots
dt() { dotnet run --no-build --project src/DarkTerritory.Cli -- "$@" >/dev/null; }
dotnet build src/DarkTerritory.Cli >/dev/null
shot() { local name=$1; shift; dt screenshot "$@" --width 1920 --height 1080 --out "$out/$name.png"; echo "[dt] $out/$name.png"; }
shot 01-train-in-the-fog --working --cam 1207,3.6,1.6 --target 1180,-0.8,3.2 --fov 56
shot 02-crew-on-the-roofs --working --view crewside
shot 03-fire-in-the-car --threats --view fire
# (E1, 7 Oct: the empty cannon and the locker didn't pass the test; the Car Hugger eating the rear car and the Switchman by
# the line with his lantern do.)
shot 04-the-car-hugger --threats --view board
shot 05-the-choir --threats --view choir
shot 06-the-switchman --threats --view switchman
shot 07-the-iron-bridge --route frontier:7 --structure truss
shot 08-dawn --working --view trackside --dawn 1
# The key frame: the engine off its front quarter, its lamp blazing, the train behind it into the fog (capsule.py).
dt screenshot --working --cam 1207,3.6,1.6 --target 1180,-0.8,3.2 --fov 56 --width 1232 --height 706 --out out/store/shots/key.png
python3 tools/art/store/capsule.py out/store/shots/key.png
# The icon's frame: the engine head on in the fog, its lamp lit (tools/art/store/icon.py).
dt screenshot --cam 1211,0.4,1.9 --target 1200,0,2.4 --fov 34 --width 1024 --height 1024 --out out/store/shots/icon.png
python3 tools/art/store/icon.py out/store/shots/icon.png
