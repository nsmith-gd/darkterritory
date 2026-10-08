#!/usr/bin/env bash
# Rebuilds every character and creature model from its script, headless:
#   tools/blender/build.sh            # all of them -> content/art/models/<name>.glb
#   tools/blender/build.sh crew hollow
# Needs Blender 4.x on PATH (or BLENDER=/path/to/blender), or Blender as a Python module: BLENDER_PYTHON=/path/to/python
# with `pip install "bpy<5"` in it (a Python 3.11 venv; bpy 5's actions have no fcurves for rig.bake). Deterministic: no randomness but fixed hashes (rig.noise3),
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
  # The crew, the Cinder Hound, the Weight, the Track Doll, the Car Hugger and the Dragger are built from crew.py,
  # cinder_hound.py, weight.py, track_doll.py, car_hugger.py and dragger.py by tools/models/recipes (their high copies
  # baked onto these game meshes, tools/models/overbake.py); the Hollow, the Switchman, the Sleepers, the Clinger and
  # the Soot children are sourced scans: tools/models/recipes. crew_clips is the crew's actions, merged into crew.glb on load.
  # The sheep and the Moose are these scripts' own meshes, on the shared tiling textures. The Gannet is built from gannet.py
  # by tools/models/recipes/gannet.py (its colour baked into an atlas, and a distance copy, gannet.lod1.glb).
  models=(crew_clips sheep moose)
fi
mkdir -p "$out"
for m in "${models[@]}"; do
  log="$(mktemp)"
  # Blender's exporter prints a lot; keep the one line each script prints ("[dt] ...") unless it fails.
  if [ -n "${BLENDER_PYTHON:-}" ]; then run=("$BLENDER_PYTHON" "$here/$m.py"); else run=("$blender" -b --factory-startup --python "$here/$m.py"); fi
  if ! PYTHONHASHSEED=0 PYTHONDONTWRITEBYTECODE=1 "${run[@]}" -- "$out/$m.glb" >"$log" 2>&1 \
      || grep -q "Traceback" "$log"; then
    cat "$log" >&2
    rm -f "$log"
    echo "build.sh: $m failed" >&2
    exit 1
  fi
  grep "^\[dt\]" "$log" || true
  rm -f "$log"
done
