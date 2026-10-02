#!/usr/bin/env python3
"""Where each checklist line stands in the game: how many of its cues have a sound installed, and how many the game's
code plays (GameAudio's hooks name them as "line.cue" strings). A line whose every cue (bar the silent ones) is installed
and hooked has its own sound in the game: L1 on the checklist's scale, rough until the director keeps its candidates.

  python3 tools/audio/levels.py            # a table, and out/audio/levels.json for the store update
"""

import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cues as C  # noqa: E402
from install import TELL_SOUNDS  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
SOUNDS = os.path.join(ROOT, "content", "audio", "sounds")


def code_text():
    out = []
    for d, _, fs in os.walk(os.path.join(ROOT, "src")):
        for f in fs:
            if f.endswith(".cs"):
                out.append(open(os.path.join(d, f)).read())
    return "\n".join(out)


def main():
    code = code_text()
    installed = {f[:-5] for f in os.listdir(SOUNDS) if f.endswith(".json")}
    rows = {}
    for line, cues in C.CUES.items():
        # silent by design, or waiting on an action the game doesn't have yet (cues.py says so in the cue's need)
        live = [c for c in cues if not c["silent"] and not c["need"].startswith("Not in the game yet")]
        if not live:
            continue
        inst = [c for c in live if any(n == f"{line}.{c['id']}" or n.startswith(f"{line}.{c['id']}.") for n in installed)
                or line in TELL_SOUNDS and TELL_SOUNDS[line][0] in installed]
        # hooked: named in code literally, or the line's name in code with the cue's id as a literal (composed names)
        # or a tell the game plays under its own sound's name, which install.py points at the kept takes
        tell = line in TELL_SOUNDS and f'"{TELL_SOUNDS[line][0]}"' in code
        hooked = [c for c in live if tell or f'"{line}.{c["id"]}' in code
                  or (f'"{line}' in code and re.search(r'["\.]' + re.escape(c["id"]) + r'["\.]', code))]
        rows[line] = {"cues": len(live), "installed": len(inst), "hooked": len(hooked),
                      "missing_install": [c["id"] for c in live if c not in inst],
                      "missing_hook": [c["id"] for c in live if c not in hooked]}
    for line, r in rows.items():
        flag = "L1" if r["installed"] == r["cues"] and r["hooked"] == r["cues"] else "  "
        print(f"{flag} {line:26s} {r['installed']:3d}/{r['cues']:<3d} installed  {r['hooked']:3d}/{r['cues']:<3d} hooked"
              + (f"  no sound: {','.join(r['missing_install'])}" if r["missing_install"] else "")
              + (f"  not hooked: {','.join(r['missing_hook'])}" if r["missing_hook"] else ""))
    json.dump(rows, open(os.path.join(ROOT, "out", "audio", "levels.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
