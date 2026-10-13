#!/bin/bash
# A stand-in for `blender -b --factory-startup --python X [-- args]` where there's no Blender, only its Python module
# (`pip install bpy`: Blender 5 as a module, for Python 3.13; the cloud sessions' way, note 694):
#   pip install bpy && BLENDER=tools/models/bpy-blender.sh tools/models/build.sh townsfolk_trapper
script=""; rest=()
while [ $# -gt 0 ]; do
  case "$1" in
    --python) script="$2"; shift 2;;
    --) shift; rest=("$@"); break;;
    *) shift;;
  esac
done
exec python3 -c "
import os, runpy, sys
import bpy
bpy.ops.wm.read_factory_settings(use_empty=False)
script = sys.argv[1]
sys.argv = ['blender', '-b', '--python', script, '--'] + sys.argv[2:]
sys.path.insert(0, os.path.dirname(script))
runpy.run_path(script, run_name='__main__')
" "$script" "${rest[@]}"
