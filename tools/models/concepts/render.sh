#!/usr/bin/env bash
# Renders the crew headgear concepts (tools/models/concepts/crew_headgear.py) to out/review/concept-<name>-high-*.png:
#   tools/models/concepts/render.sh            # all four
#   CONCEPT=diver tools/models/concepts/render.sh
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
blender="${BLENDER:-blender}"
concepts=("${CONCEPT:-beak diver welder sallet}")
for c in ${concepts[@]}; do
  log="$(mktemp)"
  if ! CONCEPT="$c" CONCEPT_PREVIEW=none PYTHONHASHSEED=0 PYTHONDONTWRITEBYTECODE=1 "$blender" -b --factory-startup \
      --python "$here/crew_headgear.py" >"$log" 2>&1 || grep -q "Traceback" "$log"; then
    cat "$log" >&2
    rm -f "$log"
    echo "render.sh: $c failed" >&2
    exit 1
  fi
  grep "^\[dt\]" "$log" || true
  rm -f "$log"
done
