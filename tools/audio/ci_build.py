#!/usr/bin/env python3
"""The audio loop for the lines a request names, for .github/workflows/audio-build.yml (note 667).

The recipes need the CC0 packs (fetch.py packs), which sit in the project's Google Drive, and the cloud sessions' proxy
refuses Drive. So an agent commits a request on its branch and the workflow fetches the packs, builds, installs, and
pushes what it installed back to that branch. The request, tools/audio/build_request.json:

    {"build": ["crew-footsteps"],                # build.py's arguments: lines, line.cue or recipe ids
     "install": ["crew-footsteps"],              # install.py's lines
     "surface": "wood",                          # optional: only these surfaces (install.py --surface)
     "store": "tools/audio/build_store/items",   # the checklist's items for those lines (ArtifactData get, out_dir)
     "why": "what it's for",
     "fresh": ["crew-horseplay"],                # optional: lines with no checklist item yet (their store items are made
                                                 # here from what was built, each candidate "built:<id>", the first one
                                                 # installed), in install.py's AREA ("areas": {line: area}; crew foley if not)
     "publish": ["crew-horseplay"]}              # optional: these lines' previews and manifest entries come back too,
                                                 # in tools/audio/build_out/, for the agent to put on the checklist

The store is committed with the request, so the install sees the director's verdicts as they stood. Take both out of
the branch once the build's back (the workflow does nothing without a request), and build_out once its previews are up.
"""

import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def run(*cmd):
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True)


OUT = os.path.join(HERE, "..", "..", "out", "audio")


def built(lines):
    """The manifest's entries for these lines (what build.py just wrote), in recipe order."""
    sys.path.insert(0, HERE)
    import build
    return [e for e in build.manifest().values() if e["line"] in lines]


def fresh_items(lines, store, areas):
    """A store item for each line the checklist doesn't have yet: its cues from cues.py, each with every candidate built
    for it ("built:<id>", in recipe order), nothing judged."""
    import cues as C
    entries = built(lines)
    os.makedirs(store, exist_ok=True)
    for line in lines:
        cues = [{"id": c["id"], "event": c["event"],
                 "cands": [{"src": "built:" + e["id"], "key": e["key"], "mat": e["mat"], "label": e["label"]}
                           for e in entries if e["line"] == line and e["cue"] == c["id"]]}
                for c in C.CUES[line]]
        item = {"name": line, "area": areas.get(line, "Crew actions & foley"), "level": 0, "status": "review", "cues": cues}
        json.dump(item, open(os.path.join(store, line + ".json"), "w"), indent=1)


def publish(lines):
    """These lines' previews and manifest entries into tools/audio/build_out/, which the workflow pushes back."""
    dest = os.path.join(HERE, "build_out")
    for e in built(lines):
        for src, sub in ((os.path.join(OUT, e["preview"]), "previews"), (os.path.join(OUT, "manifest", e["id"] + ".json"), "manifest")):
            os.makedirs(os.path.join(dest, sub), exist_ok=True)
            shutil.copy(src, os.path.join(dest, sub, os.path.basename(src)))


def main():
    req = json.load(open(sys.argv[1]))
    for target in req.get("build", []):
        run(sys.executable, os.path.join(HERE, "build.py"), target)
    if req.get("fresh"):
        fresh_items(req["fresh"], req["store"], req.get("areas", {}))
    if req.get("publish"):
        publish(req["publish"])
    if req.get("install"):
        cmd = [sys.executable, os.path.join(HERE, "install.py"), *req["install"], "--from", req["store"]]
        if req.get("surface"):
            cmd += ["--surface", req["surface"]]
        run(*cmd)


if __name__ == "__main__":
    main()
