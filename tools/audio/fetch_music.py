#!/usr/bin/env python3
"""Looks for CC0 recordings of GDD v1.4 App. E.6's candidate works on Wikimedia Commons.

    python3 tools/audio/fetch_music.py            # list what Commons has, with each file's own licence
    python3 tools/audio/fetch_music.py --fetch    # also download the CC0 ones into intake/_sources/music/

E.6's one rule: a CC0 1.0 recording of a public-domain composition. A public-domain *work* doesn't make a recording
public domain, and a "public domain" mark on an old recording isn't enough (it depends on the country). So this keeps
only files whose own Commons licence reads CC0, and saves, beside each download, the licence metadata as fetched and the
file's SHA-256: E.6's evidence. Downloads land in intake/ (gitignored, see intake/README.md), never in content/: a track
goes into content/audio/music only with a manifest entry, which `MusicManifestTests` holds to its hash.

From the cloud sessions this was written in, Commons (like Musopen, Freesound and the Internet Archive) is refused by the
network proxy, so the tracks in content/audio/music are E.6's fallback instead: our own recordings, made by
`dt audio opera` (src/DarkTerritory.Cli/OperaCommands.cs) and dedicated CC0.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import sys
import urllib.parse
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "intake" / "_sources" / "music"
API = "https://commons.wikimedia.org/w/api.php"

# E.6's candidate works (all public-domain compositions), as Commons would title them.
WORKS = [
    "Vesti la giubba", "O mio babbino caro", "Flower Duet Lakme", "Nessun dorma",
    "William Tell Overture", "Orpheus in the Underworld galop", "Ride of the Valkyries", "Largo al factotum",
    "Verdi Requiem Dies irae", "Der Holle Rache", "Anvil Chorus", "La donna e mobile", "Toreador Song", "Habanera Carmen",
]


def get(params: dict) -> dict:
    url = API + "?" + urllib.parse.urlencode({**params, "format": "json"})
    req = urllib.request.Request(url, headers={"User-Agent": "DarkTerritory-fetch-music/1.0 (asset licensing check)"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def search(work: str) -> list[dict]:
    found = get({"action": "query", "generator": "search", "gsrsearch": f"{work} filetype:audio", "gsrnamespace": 6,
                 "gsrlimit": 20, "prop": "imageinfo", "iiprop": "url|extmetadata|sha1|size|mime"})
    return list(found.get("query", {}).get("pages", {}).values())


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--fetch", action="store_true", help="download the CC0 files into intake/_sources/music/")
    args = ap.parse_args()
    report = []
    for work in WORKS:
        try:
            pages = search(work)
        except OSError as e:
            print(json.dumps({"blocked": True, "host": "commons.wikimedia.org", "error": str(e)}))
            print("Commons can't be reached from here: E.6's fallback applies (dt audio opera).", file=sys.stderr)
            return 2
        for p in pages:
            info = (p.get("imageinfo") or [{}])[0]
            meta = info.get("extmetadata", {})
            licence = meta.get("LicenseShortName", {}).get("value", "")
            entry = {"work": work, "title": p.get("title"), "licence": licence, "url": info.get("url"),
                     "descriptionUrl": info.get("descriptionurl"), "cc0": licence.strip().upper() in ("CC0", "CC0 1.0")}
            report.append(entry)
            if args.fetch and entry["cc0"] and entry["url"]:
                OUT.mkdir(parents=True, exist_ok=True)
                name = Path(urllib.parse.unquote(entry["url"])).name
                data = urllib.request.urlopen(urllib.request.Request(entry["url"], headers={"User-Agent": "DarkTerritory"}), timeout=60).read()
                (OUT / name).write_bytes(data)
                evidence = {**entry, "sha256": hashlib.sha256(data).hexdigest(), "extmetadata": meta}
                (OUT / (name + ".licence.json")).write_text(json.dumps(evidence, indent=2) + "\n")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
