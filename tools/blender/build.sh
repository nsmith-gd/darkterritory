#!/usr/bin/env bash
# Rebuilds every character and creature model from its script, headless:
#   tools/blender/build.sh            # all of them -> content/art/models/<name>.glb
#   tools/blender/build.sh crew hollow
# Needs Blender 4.x on PATH (or BLENDER=/path/to/blender). Deterministic: no randomness but fixed hashes (rig.noise3),
# so a rebuild with the same Blender gives the same files. Check the result with
#   dotnet test --project tests/DarkTerritory.Game.Tests -- --filter-class "*CreatureArtTests"
# which also renders the turntables into out/shots/creatures/ (look at them).
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../.." && pwd)"
out="$root/content/art/models"
blender="${BLENDER:-blender}"
models=("$@")
if [ ${#models[@]} -eq 0 ]; then
  models=(crew cinder_hound sleeper clinger hollow switchman soot_child dragger)
fi
mkdir -p "$out"
for m in "${models[@]}"; do
  log="$(mktemp)"
  # Blender's exporter prints a lot; keep the one line each script prints ("[dt] ...") unless it fails.
  if ! PYTHONHASHSEED=0 PYTHONDONTWRITEBYTECODE=1 "$blender" -b --factory-startup --python "$here/$m.py" -- "$out/$m.glb" >"$log" 2>&1 \
      || grep -q "Traceback" "$log"; then
    cat "$log" >&2
    rm -f "$log"
    echo "build.sh: $m failed" >&2
    exit 1
  fi
  grep "^\[dt\]" "$log" || true
  rm -f "$log"
done
