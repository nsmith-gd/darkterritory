#!/usr/bin/env python3
"""Put the chosen sounds into the game: Opus takes under content/audio/samples, a sound definition per cue.

Which candidate a cue gets, from the checklist store (`--from DIR`, the `items` as an ArtifactData list saves them):
  1. what the director kept (Keep) on that cue, all of them as takes;
  2. otherwise, on a line still at L0 (nothing of its own in the game), the first candidate not marked Redo: built
     candidates first, then library files, so every cue has its own sound, rough or not (L1);
  3. otherwise nothing: a line already at L1 or better keeps what it plays until something is kept.

  python3 tools/audio/install.py --from DIR [--dry] [line ...]

For each cue it writes content/audio/samples/<line>/<cue>[/<surface>]/NN.opus (mono 48 kHz Opus; the engine picks a take
at random per instance) and content/audio/sounds/<line>.<cue>[.<surface>].json (one sample layer; tier, range and
level from the line's area). content/audio/samples/index.json records where every folder came from and its licence.
The engine side is Ballast.Audio's sample layer (`"source": "sample"`).
"""

import hashlib
import json
import os
import shutil
import subprocess
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cues as C  # noqa: E402
import dsp  # noqa: E402
import src  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "out", "audio")
SAMPLES = os.path.join(ROOT, "content", "audio", "samples")
SOUNDS = os.path.join(ROOT, "content", "audio", "sounds")

# Area -> (tier, minDistance, maxDistance, rolloff, gainDb) for a cue whose line has no tier of its own. Spec A.3's tiers:
# 1 enemy telegraphs, 2 proximity voice, 3 critical train state, 4 player actions, 5 train bed, 6 ambient world.
AREA = {
    "Enemy tells": (1, 4, 250, 0.8, 8),
    "Creature sounds": (3, 2, 80, 1.0, 6),
    "Train bed": (5, 4, 200, 0.8, 4),
    "Train state & alarms": (3, 4, 300, 0.8, 6),
    "Crew actions & foley": (4, 1, 40, 1.0, 2),
    "Voice & comms": (4, 1, 30, 1.0, 2),
    "Music & UI": (4, 1, 30, 1.0, 0),
    "World & hazards": (6, 10, 600, 0.6, 2),
    "Facilities & places": (6, 8, 300, 0.7, 2),
}
OPUS_KBPS = 64
# Earlier-pass keepers that string several of one event together (a gait, hops coming closer): cut into single takes.
# The rest are one designed event each, pauses and all (a giggle, a swallow, the Whistler's wrong whistle).
# Value: the gap (s) and level (dB under the loudest) that separate one event from the next in that file.
SEQUENCES = {"audio/cs-ribbits--hops.mp3": (0.06, -26), "audio/cs-passenger--boots.mp3": (0.12, -38),
             "audio/cs-climbers--roof.mp3": (0.08, -30)}


def encode(x, path):
    """A take as Ogg Opus. Byte-identical for identical audio, so rebuilds don't churn the repo."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    raw = np.clip(x, -1, 1).astype(np.float32).tobytes()
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(dsp.SR), "-ac", "1", "-i", "-",
                    "-c:a", "libopus", "-b:a", f"{OPUS_KBPS}k", "-application", "audio", "-map_metadata", "-1",
                    "-fflags", "+bitexact", "-flags:a", "+bitexact", path], input=raw, check=True)


def split_events(x, n, gap=0.12, db=-38):
    """An earlier-pass file that strings several events together (hops coming closer) cut back into single takes."""
    hop = dsp.samples(0.01)
    rms = np.array([np.sqrt(np.mean(x[i:i + hop] ** 2)) for i in range(0, len(x) - hop, hop)])
    on = rms > np.max(rms) * dsp.db2a(db)
    events, start, quiet = [], None, 0
    for i, v in enumerate(on):
        if v:
            if start is None:
                start = i
            quiet = 0
        elif start is not None:
            quiet += 1
            if quiet * 0.01 >= gap:
                events.append((start, i - quiet + 1))
                start, quiet = None, 0
    if start is not None:
        events.append((start, len(on)))
    takes = [dsp.fade(x[a * hop:min(len(x), (b + 3) * hop)], 0.002, 0.02) for a, b in events if (b - a) * 0.01 > 0.04]
    return takes if len(takes) >= 2 else [x]


def takes_of(cand, cue):
    """The audio of a candidate as a list of takes."""
    if cand.get("built"):
        d = os.path.join(OUT, "takes", cand["line"], cand["cue"])
        stem = cand["key"] + (f"_{cand['mat']}" if cand.get("mat") else "")
        files = sorted(f for f in os.listdir(d) if f.startswith(stem + "_") and f[len(stem) + 1:-4].isdigit())
        return [dsp.load(os.path.join(d, f)) for f in files]
    if cand.get("old"):
        x = dsp.load(os.path.join(OUT, "earlier", cand["src"]))
        if cue["kind"] == "loop":
            return [dsp.loop_seam(x, 0.3)]
        if cand["src"] in SEQUENCES:
            return [dsp.level(t, -20) for t in split_events(x, cue["vars"], *SEQUENCES[cand["src"]])]
        return [dsp.level(x, -20)]
    x = src.get(cand["libkey"])
    return [dsp.level(dsp.fade(dsp.trim_silence(x, -60), 0.002, 0.02), -20)]


def libkey(srcpath):
    """'library/pack__name.mp3' (the page's copy) -> 'pack:name' (the source file)."""
    base = os.path.basename(srcpath)[:-4]
    pack, name = base.split("__", 1)
    return f"{pack}:{name[len('sfx100v2_'):] if pack == 'sfx_100_v2' else name}"


def candidates(line, cue, stored):
    """The stored cue's candidates in pick order, each tagged with where its audio is."""
    import build
    man = build.manifest()
    by_url = {}
    import assets
    for e in man.values():
        u = assets.url_for(e["preview"])
        if u:
            by_url[u] = e
    out = []
    for k in stored.get("cands") or []:
        k = dict(k)
        if k["src"] in by_url:
            e = by_url[k["src"]]
            k.update(built=True, line=e["line"], cue=e["cue"], key=e["key"], mat=e["mat"])
        elif k.get("old") or k["src"].startswith("audio/"):
            k["old"] = True
        elif k["src"].startswith("library/"):
            k["libkey"] = libkey(k["src"])
        else:
            continue
        out.append(k)
    return out


def pick(cands, mat, line_level):
    here = [k for k in cands if k.get("mat") in (mat, None)]
    kept = [k for k in here if k.get("verdict") == "keep"]
    if kept:
        return kept, "kept"
    if (line_level or 0) >= 1:
        return [], None
    ok = [k for k in here if k.get("verdict") != "redo"]
    ok.sort(key=lambda k: (not k.get("built"), not k.get("old"), k.get("mat") is None))
    return ok[:1], "first"


# Lines whose sounds aren't the game's (the trailer's cut goes to the store tools, not content/).
NOT_IN_GAME = {"store-trailer"}
# A prisoner calling from a Holdout (D.7): heard to 60 m with normal falloff and occlusion, on the voice tier.
VOICE_LINES = {"voice-prisoner-sets", "voice-callout"}


def sound_def(item, cue, folder, line):
    tier, lo, hi, roll, g = AREA.get(item.get("area"), (4, 1, 40, 1.0, 2))
    if isinstance(item.get("tier"), int):
        tier = item["tier"]
    flat = line.startswith("ui-")
    if line == "ui-music":
        tier = 7            # music's own bottom tier, under the ambient world (decided 1 Oct)
    elif flat:
        tier = 4
    if line in VOICE_LINES:
        tier, lo, hi, roll = 2, 2, 60, 1.0
    d = {"tier": tier, "loop": cue["kind"] == "loop", "maxInstances": 8 if cue["kind"] == "loop" else 12,
         "minDistance": lo, "maxDistance": hi, "rolloff": roll, "gainDb": g, "flat": flat,
         "layers": [{"source": "sample", "sample": folder, "gain": 1,
                     "pitchJitter": 0 if cue["kind"] == "loop" or flat else 0.4,
                     "gainJitter": 0 if cue["kind"] == "loop" or flat else 1.0}]}
    return d


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    store = sys.argv[sys.argv.index("--from") + 1]
    args = [a for a in args if a != store]
    dry = "--dry" in sys.argv
    index_path = os.path.join(SAMPLES, "index.json")
    index = json.load(open(index_path)) if os.path.exists(index_path) else {}
    n_cues = n_files = 0
    for line, cues in C.CUES.items():
        if args and line not in args:
            continue
        p = os.path.join(store, line + ".json")
        if not os.path.exists(p):
            continue
        item = json.load(open(p))
        item = item.get("data", item)
        if item.get("status") == "cut" or line in NOT_IN_GAME:
            continue
        stored = {c["id"]: c for c in item.get("cues") or []}
        for cue in cues:
            if cue["silent"] or cue["id"] not in stored:
                continue
            cands = candidates(line, cue, stored[cue["id"]])
            for mat in (cue["mats"] or [None]):
                chosen, why = pick(cands, mat, item.get("level"))
                if not chosen:
                    continue
                rel = f"{line}/{cue['id']}" + (f"/{mat}" if mat else "")
                takes = [t for k in chosen for t in takes_of(k, cue)]
                if cue["kind"] == "loop":
                    takes = takes[:2]
                n_cues += 1
                n_files += len(takes)
                print(f"{rel:50s} {why:5s} {len(takes)} take(s) from {', '.join(k.get('key') or k.get('libkey') or k['src'] for k in chosen)}")
                if dry:
                    continue
                folder = os.path.join(SAMPLES, rel)
                shutil.rmtree(folder, ignore_errors=True)
                for i, x in enumerate(takes):
                    encode(x, os.path.join(folder, f"{i:02d}.opus"))
                name = f"{line}.{cue['id']}" + (f".{mat}" if mat else "")
                with open(os.path.join(SOUNDS, name + ".json"), "w") as f:
                    f.write(f"// {item['name']}: {cue['event']}" + (f" ({C.MATERIALS[mat]})" if mat else "") +
                            f". Written by tools/audio/install.py from the audio checklist ({why}); edit the cue, not this.\n")
                    json.dump(sound_def(item, cue, rel, line), f, indent=1)
                    f.write("\n")
                index[rel] = {"cue": f"{line}.{cue['id']}", "surface": mat, "picked": why,
                              "candidates": [{"label": k.get("label"), "key": k.get("key") or k.get("libkey") or k["src"],
                                              "sources": k.get("sources") or ([k["libkey"]] if k.get("libkey") else []),
                                              "licence": "Sonniss GDC" if k.get("restricted") else "CC0"} for k in chosen],
                              "takes": len(takes),
                              "sha": hashlib.sha256(b"".join(open(os.path.join(folder, f), "rb").read()
                                                             for f in sorted(os.listdir(folder)))).hexdigest()[:16]}
    if not dry:
        os.makedirs(SAMPLES, exist_ok=True)
        json.dump(dict(sorted(index.items())), open(index_path, "w"), indent=1)
    print(f"{n_cues} cue folders, {n_files} takes")


if __name__ == "__main__":
    main()
