#!/usr/bin/env python3
"""The checklist page's asset store, as far as this repo knows it: which preview went up under which asset id.

Previews are uploaded to the Dark Territory Audio Checklist artifact's asset store by the Artifact tool (publish with
asset: true, up to 25 files a call); the page plays them from "/_blob/<id>". This keeps tools/audio/assets.json in step,
keyed by the preview's path under out/audio and its content hash, so a rebuild that changes a sound gets a new upload
(and a fresh verdict) and one that doesn't keeps its id.

  python3 tools/audio/assets.py pending [--limit 25]     # previews not uploaded yet (or changed since), absolute paths
  python3 tools/audio/assets.py record < upload-result.txt   # read the Artifact tool's upload result, remember the ids
                                                         # (its lines as printed, or "<preview>.mp4 <asset id>" per line)
"""

import hashlib
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "out", "audio"))
FILE = os.path.join(HERE, "assets.json")


def load():
    return json.load(open(FILE)) if os.path.exists(FILE) else {}


def sha(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()[:16]


def url_for(preview):
    """The page's url for a built preview (its path under out/audio), if that exact file went up."""
    a = load().get(preview)
    p = os.path.join(OUT, preview)
    if a and os.path.exists(p) and a["sha"] == sha(p):
        return a["url"]
    return None


def pending():
    reg = load()
    out = []
    pdir = os.path.join(OUT, "previews")
    for f in sorted(os.listdir(pdir)) if os.path.isdir(pdir) else []:
        rel = f"previews/{f}"
        p = os.path.join(pdir, f)
        if rel not in reg or reg[rel]["sha"] != sha(p):
            out.append(p)
    return out


def record(text):
    reg = load()
    n = 0
    found = [m.groups() for m in re.finditer(r'"([^"]+\.mp4)" \(\d+ bytes[^)]*\) → "(/_blob/([0-9a-f]{32}))"', text)]
    # or the short form: "<preview file name> <asset id>" per line
    found += [(os.path.join(OUT, "previews", m.group(1)), "/_blob/" + m.group(2), m.group(2))
              for m in re.finditer(r"^\s*([\w.-]+\.mp4)\s+([0-9a-f]{32})\s*$", text, re.M)]
    for path, url, aid in found:
        rel = os.path.relpath(path, OUT)
        reg[rel] = {"id": aid, "url": url, "sha": sha(path)}
        n += 1
    json.dump(dict(sorted(reg.items())), open(FILE, "w"), indent=1)
    print(f"recorded {n}, {len(reg)} in all")


if __name__ == "__main__":
    if sys.argv[1:2] == ["pending"]:
        lim = int(sys.argv[sys.argv.index("--limit") + 1]) if "--limit" in sys.argv else None
        p = pending()
        print(json.dumps(p[:lim] if lim else p))
    elif sys.argv[1:2] == ["record"]:
        record(sys.stdin.read())
    else:
        print(__doc__)
