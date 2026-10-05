#!/usr/bin/env python3
"""Build the audio checklist's candidates: every recipe's takes for the game, and a preview of each for the page.

A recipe (tools/audio/recipes/*.py) makes one candidate for one cue of one checklist line: a function of a seeded rng and
the take number, registered with @recipe. Takes are what the game would pick between at random; the preview strings
them together (one-shots with a gap between, loops played round twice so the seam can be heard) for the director to judge
with Keep/Redo on the Dark Territory Audio Checklist.

  python3 tools/audio/build.py                        # everything
  python3 tools/audio/build.py tell-choir cs-gaunt.blow   # lines, or line.cue, or line.cue.key
  python3 tools/audio/build.py --list                 # what's registered

Writes out/audio/takes/<line>/<cue>/<key>_NN.wav (48 kHz 16-bit mono), out/audio/previews/<line>--<cue>--<key>.mp4
(AAC; the page's asset store takes video/mp4) and out/audio/manifest/<id>.json (what tools/audio/cues.py reads). Same
inputs, same output: each take's rng is seeded from its line, cue, key and number.
"""

import hashlib
import importlib
import json
import os
import pkgutil
import sys
import time

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dsp  # noqa: E402
import src  # noqa: E402

# Recipes import `recipe` from `build`; run as a script this module is __main__, so make both names the same module.
sys.modules.setdefault("build", sys.modules[__name__])

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "out", "audio")
RECIPES = []


def recipe(line, cue, key, label, how, sources=(), takes=3, loop=False, mat=None, band=None, lufs=-20.0, gap=0.6,
           seconds=None, preview=None):
    """Register a candidate. `band` (lo, hi) for a tell: the build reports how much of it sits there (spec A.4).
    `mat` the surface a per-surface cue's candidate is for. `seconds` a loop's length. `preview(takes, rng)` builds a
    preview of its own (the Choir gathering voice by voice) instead of the takes in a row."""

    def wrap(fn):
        RECIPES.append(dict(line=line, cue=cue, key=key, label=label, how=" ".join(how.split()), sources=list(sources),
                            takes=takes, loop=loop, mat=mat, band=band, lufs=lufs, gap=gap, seconds=seconds, fn=fn, preview=preview,
                            module=fn.__module__))
        return fn

    return wrap


def seed(*parts):
    return int.from_bytes(hashlib.sha256("/".join(map(str, parts)).encode()).digest()[:8], "little")


def load_recipes():
    import recipes
    for m in pkgutil.iter_modules(recipes.__path__):
        importlib.import_module(f"recipes.{m.name}")


def cid(r):
    return f"{r['line']}.{r['cue']}.{r['key']}" + (f"@{r['mat']}" if r["mat"] else "")


def build_one(r):
    takes = []
    for i in range(r["takes"]):
        rng = np.random.default_rng(seed(r["line"], r["cue"], r["key"], r["mat"] or "", i))
        x = np.asarray(r["fn"](rng, i), np.float32)
        if r["loop"]:
            x = dsp.loop_seam(x, 0.3)
        else:
            x = dsp.fade(dsp.trim_silence(x, -60), 0.002, 0.03)
        takes.append(dsp.level(x, r["lufs"]))
    stem = f"{r['line']}--{r['cue']}--{r['key']}" + (f"--{r['mat']}" if r["mat"] else "")
    tdir = os.path.join(OUT, "takes", r["line"], r["cue"])
    for i, x in enumerate(takes):
        dsp.write_wav(os.path.join(tdir, f"{r['key']}{'_' + r['mat'] if r['mat'] else ''}_{i:02d}.wav"), x)
    # the preview: takes in a row, or a loop played twice round
    if r["preview"]:
        pv = dsp.fade(r["preview"](takes, np.random.default_rng(seed(r["line"], r["cue"], r["key"], "preview"))), 0.05, 0.5)
    elif r["loop"]:
        reps = 2 if len(takes[0]) / dsp.SR < 14 else 1
        pv = np.concatenate([np.concatenate([t] * reps) for t in takes[:2]])
        pv = dsp.fade(pv, 0.3, 1.0)
    else:
        g = dsp.silence(r["gap"])
        pv = np.concatenate([dsp.silence(0.15)] + [p for t in takes for p in (t, g)])
    pv = dsp.level(pv, r["lufs"])
    ppath = os.path.join(OUT, "previews", stem + ".mp4")
    dsp.write_preview(ppath, pv)
    allx = np.concatenate(takes)
    entry = dict(id=cid(r), line=r["line"], cue=r["cue"], key=r["key"], mat=r["mat"], label=r["label"], how=r["how"],
                 sources=r["sources"], restricted=any(src.is_restricted(s) for s in r["sources"]),
                 takes=len(takes), loop=r["loop"], seconds=round(float(np.mean([len(t) for t in takes])) / dsp.SR, 2),
                 preview=os.path.relpath(ppath, OUT), previewSeconds=round(len(pv) / dsp.SR, 2),
                 sha=hashlib.sha256(open(ppath, "rb").read()).hexdigest()[:16],
                 centroid=round(dsp.centroid(allx)), lufs=round(dsp.loudness(allx), 1))
    if r["band"]:
        entry["band"] = list(r["band"])
        entry["inBand"] = round(dsp.band_fraction(allx, *r["band"]), 2)
    return entry


def main():
    load_recipes()
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if "--list" in sys.argv:
        for r in RECIPES:
            print(cid(r), "-", r["label"])
        print(len(RECIPES), "recipes")
        return
    sel = [r for r in RECIPES if not args or any(cid(r) == a or cid(r).startswith(a + ".") or cid(r).startswith(a + "@")
                                                    or r["line"] == a for a in args)]
    # One manifest file per candidate, so builds of different lines can run side by side.
    mdir = os.path.join(OUT, "manifest")
    os.makedirs(mdir, exist_ok=True)
    for r in sel:
        t0 = time.time()
        e = build_one(r)
        json.dump(e, open(os.path.join(mdir, e["id"] + ".json"), "w"), indent=1)
        band = f" {e['inBand']:.0%} in {e['band'][0]}-{e['band'][1]} Hz" if "band" in e else ""
        print(f"{e['id']:60s} {e['takes']}x {e['seconds']:5.2f}s centre {e['centroid']:5d} Hz{band}  ({time.time() - t0:.1f}s)")
    print(f"{len(sel)} built")


def manifest():
    """Every built candidate whose recipe still exists: {id: entry}."""
    load_recipes()
    known = {cid(r) for r in RECIPES}
    mdir = os.path.join(OUT, "manifest")
    out = {}
    for f in sorted(os.listdir(mdir)) if os.path.isdir(mdir) else []:
        e = json.load(open(os.path.join(mdir, f)))
        if e["id"] in known:
            out[e["id"]] = e
    return out


if __name__ == "__main__":
    main()
