#!/usr/bin/env bash
# Cooks the sourced props (tools/models/recipes/<name>.py) into content/art/models/props/ and their layers into
# content/art/textures/models/, headless in Blender:
#   python3 tools/models/fetch.py      # the sources first (intake/_sources/models, never committed)
#   tools/models/build.sh              # every recipe
#   tools/models/build.sh skull_lantern
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
blender="${BLENDER:-blender}"
recipes=("$@")
if [ ${#recipes[@]} -eq 0 ]; then
  recipes=()
  for f in "$here"/recipes/*.py; do recipes+=("$(basename "$f" .py)"); done
fi
for r in "${recipes[@]}"; do
  log="$(mktemp)"
  if ! PYTHONHASHSEED=0 PYTHONDONTWRITEBYTECODE=1 "$blender" -b --factory-startup --python "$here/recipes/$r.py" >"$log" 2>&1 \
      || grep -q "Traceback" "$log"; then
    cat "$log" >&2
    rm -f "$log"
    echo "build.sh: $r failed" >&2
    exit 1
  fi
  grep "^\[dt\]" "$log" || true
  rm -f "$log"
done
