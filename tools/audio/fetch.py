#!/usr/bin/env python3
"""Fetch the audio sources into out/audio/src (never committed): the four CC0 packs whole, and from the Sonniss GDC
bundles only the files the recipes pick.

The sources sit in the project's Google Drive folder (sources.json has the file ids). The Sonniss bundles are 0.9-1.9 GB
zips; rather than download them, this reads each zip's central directory with HTTP range requests and pulls single
members, so a pick of a few hundred files costs a few hundred MB of disk and none is left behind.

  python3 tools/audio/fetch.py packs                 # the CC0 packs, unzipped
  python3 tools/audio/fetch.py index                 # list every file in every Sonniss bundle -> out/audio/sonniss-index.json
  python3 tools/audio/fetch.py pick FILE             # pull the bundle paths listed in FILE (one per line, as the index has
                                                     # them) into out/audio/src/sonniss/<path>
Needs network access to drive.usercontent.google.com (the environment's allowed domains).
"""

import io
import json
import os
import sys
import urllib.request
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "out", "audio"))
SRC = os.environ.get("DT_AUDIO_SRC") or os.path.join(OUT, "src")
SOURCES = json.load(open(os.path.join(HERE, "sources.json")))
URL = "https://drive.usercontent.google.com/download?id={}&export=download&confirm=t"


class Remote(io.RawIOBase):
    """A read-only, seekable view of a remote file over HTTP range requests, with a small read-ahead cache: enough for
    zipfile to read a central directory and single members."""

    def __init__(self, url, block=1 << 20):
        self.url, self.pos, self.block, self.cache = url, 0, block, {}
        req = urllib.request.Request(url, headers={"Range": "bytes=0-0"})
        with urllib.request.urlopen(req) as r:
            rng = r.headers.get("Content-Range")
            if not rng:
                raise IOError(f"{url}: the server doesn't do range requests")
            self.size = int(rng.split("/")[1])

    def seekable(self):
        return True

    def readable(self):
        return True

    def tell(self):
        return self.pos

    def seek(self, off, whence=0):
        self.pos = off if whence == 0 else self.pos + off if whence == 1 else self.size + off
        return self.pos

    def _blk(self, i):
        if i not in self.cache:
            a = i * self.block
            b = min(self.size, a + self.block) - 1
            req = urllib.request.Request(self.url, headers={"Range": f"bytes={a}-{b}"})
            with urllib.request.urlopen(req) as r:
                self.cache[i] = r.read()
            if len(self.cache) > 64:
                self.cache.pop(next(iter(self.cache)))
        return self.cache[i]

    def read(self, n=-1):
        if n < 0:
            n = self.size - self.pos
        n = max(0, min(n, self.size - self.pos))
        out = bytearray()
        while n > 0:
            i, o = divmod(self.pos, self.block)
            chunk = self._blk(i)[o:o + n]
            out += chunk
            self.pos += len(chunk)
            n -= len(chunk)
        return bytes(out)

    def readinto(self, b):
        d = self.read(len(b))
        b[:len(d)] = d
        return len(d)


def packs():
    for name, p in SOURCES["packs"].items():
        dest = os.path.join(SRC, name)
        if os.path.isdir(dest):
            print("have", name)
            continue
        data = urllib.request.urlopen(URL.format(p["drive"])).read()
        zipfile.ZipFile(io.BytesIO(data)).extractall(dest)
        print("unpacked", name)


def index():
    out = {}
    for name, p in SOURCES["sonniss"].items():
        z = zipfile.ZipFile(Remote(URL.format(p["drive"])))
        out[name] = [{"path": i.filename, "bytes": i.file_size} for i in z.infolist() if not i.is_dir()]
        print(name, len(out[name]), "files")
    json.dump(out, open(os.path.join(OUT, "sonniss-index.json"), "w"), indent=0)


def pick(listfile):
    want = [l.strip() for l in open(listfile) if l.strip() and not l.startswith("#")]
    idx = json.load(open(os.path.join(OUT, "sonniss-index.json")))
    where = {f["path"]: b for b, files in idx.items() for f in files}
    by_bundle = {}
    for w in want:
        if w not in where:
            print("not in any bundle:", w)
            continue
        by_bundle.setdefault(where[w], []).append(w)
    for b, paths in by_bundle.items():
        z = zipfile.ZipFile(Remote(URL.format(SOURCES["sonniss"][b]["drive"])))
        for p in paths:
            dest = os.path.join(SRC, "sonniss", p)
            if os.path.exists(dest):
                continue
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            with z.open(p) as r, open(dest, "wb") as w:
                w.write(r.read())
            print("pulled", p)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "packs":
        packs()
    elif cmd == "index":
        index()
    elif cmd == "pick":
        pick(sys.argv[2])
    else:
        print(__doc__)
