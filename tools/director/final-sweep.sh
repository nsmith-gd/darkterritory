#!/bin/bash
# #613's final sweep (note 930; the director's brief §8): the four §7 sessions × 30 seeds on a tier's own routes, 2700 s
# nights, the shipping tuning or an edition laid over it (editions/f1-*: one director-pending ask each). Run from the repo
# root on main as it stands (or this branch); writes tools/director/final/<name>.json and prints its `dt director report`
# against the old director's baseline. Every run is the same command line, so runs in different containers compare.
#   tools/director/final-sweep.sh frontier                      # the shipping defaults
#   tools/director/final-sweep.sh frontier f1-dread             # an ask's edition
#   PARALLEL=4 SEEDS=1-30 tools/director/final-sweep.sh local   # (the defaults shown)
set -euo pipefail
tier=${1:?a tier: local, frontier, deadLines or deepTerritory}
edition=${2:-}
name=${edition:-default}-$tier
out=tools/director/final/$name.json
mkdir -p tools/director/final out/final-bin
dotnet build src/DarkTerritory.Cli -c Release -o out/final-bin > /dev/null
ed=(); [ -n "$edition" ] && ed=(--edition "$edition")
dotnet out/final-bin/dt.dll "${ed[@]}" director sweep --routes "$tier" --cells 1x3,2x5,4x8,8x12 --seeds "${SEEDS:-1-30}" \
  --seconds 2700 --parallel "${PARALLEL:-$(nproc)}" --out "$out" > /dev/null
dotnet out/final-bin/dt.dll director report "$out" --against tools/director/baseline-old.json
