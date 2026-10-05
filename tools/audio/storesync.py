#!/usr/bin/env python3
"""Push cue changes to the checklist's store without trampling a review.

The store is the Dark Territory Audio Checklist artifact's `items` collection, read and written by the ArtifactData tool.
Every write is pinned to the version last read, so a verdict the director gives while this runs makes the write fail
rather than vanish. The loop:

  1. ArtifactData list items with out_dir=DIR           (a fresh copy; the tool prints each doc's version)
  2. python3 tools/audio/cues.py --store NEW --from DIR  (rebuild the cues, carrying every verdict in DIR over)
  3. python3 tools/audio/storesync.py changed NEW DIR     (the lines whose cues differ)
  4. python3 tools/audio/storesync.py writes NEW id:version ...   -> the `writes` for one ArtifactData batch (50 at most)
"""

import json
import os
import sys


def cues_of(path):
    d = json.load(open(path))
    return d.get("data", d).get("cues")


def changed(new, cur):
    out = []
    for f in sorted(os.listdir(new)):
        line = f[:-5]
        old = os.path.join(cur, f)
        if not os.path.exists(old) or cues_of(os.path.join(new, f)) != cues_of(old):
            out.append(line)
    return out


def writes(new, pins):
    out = []
    for pin in pins:
        line, ver = pin.split(":")
        out.append({"op": "update", "collection": "items", "doc_id": line, "if_version": int(ver),
                    "file_path": os.path.abspath(os.path.join(new, line + ".json"))})
    return out


if __name__ == "__main__":
    if sys.argv[1] == "changed":
        print(" ".join(changed(sys.argv[2], sys.argv[3])))
    elif sys.argv[1] == "writes":
        print(json.dumps(writes(sys.argv[2], sys.argv[3:])))
    else:
        print(__doc__)
