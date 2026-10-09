#!/bin/bash
# The screenshot gallery (ARCHITECTURE §8 note 517) into out/shots/, and the numbers a change can move (the train table,
# the art's triangle budgets) into out/shots/numbers.json: what main's CI keeps on every merge (the screenshots artifact)
# and what a pull request's review packet compares against it (tools/review/packet.sh).
# Usage: tools/review/gallery.sh [dt arguments before the command, e.g. "-c Release --no-build"]
set -euo pipefail
cd "$(dirname "$0")/../.."
DT="dotnet run --project src/DarkTerritory.Cli ${*:--c Release --no-build} --"
mkdir -p out/shots
# Every screenshot in one process, one dressed renderer for them all (tools/review/gallery.txt).
$DT screenshot --batch tools/review/gallery.txt > out/shots/gallery.json
# Generated lines (docs/design/linegen-plan.md): the map and profile.
$DT linegen generate --route frontier:7 --cars 6 --map out/shots/lg-map.png --profile out/shots/lg-profile.png > /dev/null
# The art pass (ARCHITECTURE §8 note 48): kit pieces on the turntable.
for p in engine car-steel guard portal viaduct-bay car-wrecked; do
  $DT art show $p --out out/shots/art-$p.png > /dev/null
done
# The numbers: the train table (spec B.4-B.6) and each kit piece against its triangle budget.
jq -n --argjson train "$($DT train table)" --argjson art "$($DT art check)" '{train: $train, art: $art}' > out/shots/numbers.json
jq -c '{shots: .shots, seconds: .seconds}' out/shots/gallery.json
