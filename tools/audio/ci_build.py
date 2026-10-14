#!/usr/bin/env python3
"""The audio loop for the lines a request names, for .github/workflows/audio-build.yml (note 667).

The recipes need the CC0 packs (fetch.py packs), which sit in the project's Google Drive, and the cloud sessions' proxy
refuses Drive. So an agent commits a request on its branch and the workflow fetches the packs, builds, installs, and
pushes what it installed back to that branch. The request, tools/audio/build_request.json:

    {"build": ["crew-footsteps"],                # build.py's arguments: lines, line.cue or recipe ids
     "install": ["crew-footsteps"],              # install.py's lines
     "surface": "wood",                          # optional: only these surfaces (install.py --surface)
     "store": "tools/audio/build_store/items",   # the checklist's items for those lines (ArtifactData get, out_dir)
     "why": "what it's for"}

The store is committed with the request, so the install sees the director's verdicts as they stood. Take both out of
the branch once the build's back (the workflow does nothing without a request).
"""

import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))


def run(*cmd):
    print("+", " ".join(cmd), flush=True)
    subprocess.run(cmd, check=True)


def main():
    req = json.load(open(sys.argv[1]))
    for target in req.get("build", []):
        run(sys.executable, os.path.join(HERE, "build.py"), target)
    if req.get("install"):
        cmd = [sys.executable, os.path.join(HERE, "install.py"), *req["install"], "--from", req["store"]]
        if req.get("surface"):
            cmd += ["--surface", req["surface"]]
        run(*cmd)


if __name__ == "__main__":
    main()
