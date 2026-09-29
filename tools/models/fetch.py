#!/usr/bin/env python3
"""Fetches the sourced models listed in tools/models/sources.json into intake/_sources/models/<id>/ (never committed).

    python3 tools/models/fetch.py              # everything
    python3 tools/models/fetch.py lantern      # ids containing "lantern"

Each repository is cloned once, blob-less and without a checkout, into intake/_sources/git/, and only the listed files
(or every file under a listed "dir") are pulled out of the pinned commit: a 9 MB model out of a multi-gigabyte repo.
Git LFS objects are not served here; an LFS pointer is reported and skipped.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GIT = ROOT / "intake" / "_sources" / "git"
OUT = ROOT / "intake" / "_sources" / "models"


def load_manifest():
    text = (ROOT / "tools" / "models" / "sources.json").read_text()
    return json.loads(re.sub(r"^\s*//.*$", "", text, flags=re.M))


def git(*args, cwd=None, capture=False):
    env = {"GIT_LFS_SKIP_SMUDGE": "1", "PATH": "/usr/bin:/bin:/usr/local/bin"}
    import os
    env = {**os.environ, **env}
    r = subprocess.run(["git", *args], cwd=cwd, env=env, check=True, capture_output=capture)
    return r.stdout if capture else None


def clone(repo, commit):
    name = repo.rstrip("/").split("github.com/")[1].replace("/", "__")
    d = GIT / name
    if not (d / ".git").exists() and not (d / "HEAD").exists():
        d.parent.mkdir(parents=True, exist_ok=True)
        git("clone", "--filter=blob:none", "--no-checkout", "--depth", "1", repo, str(d))
    have = subprocess.run(["git", "cat-file", "-e", f"{commit}^{{commit}}"], cwd=d, capture_output=True).returncode == 0
    if not have:
        git("fetch", "--filter=blob:none", "--depth", "1", "origin", commit, cwd=d)
    return d


def main():
    want = sys.argv[1:]
    for src in load_manifest():
        if want and not any(w in src["id"] for w in want):
            continue
        d = clone(src["repo"], src["commit"])
        files = list(src.get("files", []))
        if "dir" in src:
            listing = git("ls-tree", "-r", "--name-only", src["commit"], src["dir"], cwd=d, capture=True).decode().split()
            files += listing
        out = OUT / src["id"]
        for f in files:
            dest = out / f
            if dest.exists():
                continue
            data = git("show", f"{src['commit']}:{f}", cwd=d, capture=True)
            if data.startswith(b"version https://git-lfs"):
                print(f"  {src['id']}: {f} is a Git LFS pointer (not served here); skipped")
                continue
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
        (out / "SOURCE.json").write_text(json.dumps(src, indent=2) + "\n")
        size = sum(p.stat().st_size for p in out.rglob("*") if p.is_file())
        print(f"{src['id']:28s} {len(files):3d} files {size / 1e6:6.1f} MB  {src['license']}")


if __name__ == "__main__":
    main()
