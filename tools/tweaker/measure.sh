#!/bin/bash
# The Creature Tweaker's measurement (note 643; COORDINATION's "The Creature Tweaker"): nights on each tier as the app hosts
# them (dt harness --as-app, 4 bots, enemies), then each creature's count a night and the estimate's calibration as one
# ArtifactData batch for the Tweaker's store.
#
#   tools/tweaker/measure.sh [--seeds 8] [--jobs 2] [--uncapped 'dir/m-frontier:7-*.json'] OUT
#
# OUT gets the release build (OUT/dt), each night's report (OUT/<tier>:7-<seed>.json) and OUT/writes.json: an ArtifactData
# "batch" (update creatures/<name> measured, set model/calibration). Upload it with if_version on each document you read.
# A night is a few minutes of a core; one process per --jobs, as memory allows (the test suites want the machine alone).
set -euo pipefail
seeds=8; jobs=2; uncapped=""
while [ $# -gt 1 ]; do
  case "$1" in
    --seeds) seeds=$2; shift 2 ;;
    --jobs) jobs=$2; shift 2 ;;
    --uncapped) uncapped=$2; shift 2 ;;
    *) echo "unknown option $1" >&2; exit 2 ;;
  esac
done
out=$(realpath -m "${1:?OUT directory}")
root=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$out"
dotnet build "$root/src/DarkTerritory.Cli" -c Release -o "$out/dt" -v q >/dev/null
export OUT="$out" ROOT="$root"
for tier in local frontier deadLines deepTerritory; do
  for s in $(seq 1 "$seeds"); do echo "$tier:7 $s"; done
done | xargs -P "$jobs" -n 2 sh -c 'cd "$ROOT" && dotnet "$OUT/dt/dt.dll" harness --as-app --route "$0" --bots 4 --enemies --seconds 2000 --seed "$1" > "$OUT/$0-$1.json" 2> "$OUT/$0-$1.err"'
args=(--nights "$out/*:7-*.json" --at "$(date -u +'%d %b %Y')" --writes "$out/writes.json")
[ -n "$uncapped" ] && args+=(--uncapped "$uncapped")
python3 "$root/tools/tweaker/calibrate.py" "${args[@]}" > "$out/calibration.json"
echo "$(ls "$out"/*:7-*.json | wc -l) nights; the store's batch: $out/writes.json"
