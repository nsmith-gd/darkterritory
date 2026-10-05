#!/usr/bin/env python3
"""Can every tell be told apart? (spec A.4: "every tell nameable in three words"; the playtest is the real test.)

Before players name them, this checks what an ear would have to go on. For each enemy tell's sound (the take the game
plays: content/audio/samples when installed, else the best built candidate), it measures where the energy sits (octave
bands), how bright it is, and its rhythm (the envelope's modulation spectrum: a steady hiss, a 4 Hz scrabble, a single
rasp), then compares every pair. Two tells close in both spectrum and rhythm get flagged: they'd be confused, so they
must never be staged together (spec A.4 rule 3) or one has to change. It also prints the three-word name each tell is
meant to be called out by, for the playtest script.

  python3 tools/audio/tellcheck.py            # table + flagged pairs, written to out/audio/tellcheck.md
"""

import glob
import json
import os
import sys

import numpy as np
from scipy import signal

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp  # noqa: E402

ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "out", "audio")

# The name a crew should shout for each tell: three words, what it sounds like, not what it is.
NAMES = {
    "tell-track-doll": "a glassy giggle",
    "tell-car-hugger": "grinding at the back",
    "tell-whistler": "whistle, nobody pulled",
    "tell-tippy": "tiptoes behind you",
    "tell-ribbits": "bubbling croak, ahead",
    "tell-choir": "children singing, gathering",
    "tell-car-fire": "crackling in the car",
    "tell-track-debris": "wet writhing, ahead",
    "tell-marsh": "reeds rustling, surging",
    "tell-grumbler": "gnawing on crates",
    "tell-hounds": "howling behind us",
    "tell-climbers": "scrabbling at the gap",
    "tell-draggers": "a scrape, the edge",
    "tell-stoker": "fire hissing wrong",
    "tell-fireflies": "buzzing round the lamp",
}
OCTAVES = [63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]


def sound_of(line):
    """The tell's audio: installed takes first, then the first built candidate's first take."""
    inst = sorted(glob.glob(os.path.join(ROOT, "content", "audio", "samples", line, "*", "*.opus")))
    if inst:
        return dsp.load(inst[0]), "installed"
    built = sorted(glob.glob(os.path.join(OUT, "takes", line, "*", "*_00.wav")))
    if built:
        return dsp.load(built[0]), "candidate"
    early = sorted(glob.glob(os.path.join(OUT, "earlier", "audio", line + "--*.mp3")))
    if early:
        return dsp.load(early[0]), "earlier"
    return None, None


def features(x):
    f, p = signal.welch(x, dsp.SR, nperseg=4096)
    bands = []
    for c in OCTAVES:
        m = (f >= c / np.sqrt(2)) & (f < c * np.sqrt(2))
        bands.append(np.sum(p[m]))
    bands = np.array(bands) / (np.sum(bands) + 1e-20)
    # rhythm: modulation spectrum of the envelope, 0.5-32 Hz in octave bins
    hop = dsp.samples(0.005)
    env = np.array([np.sqrt(np.mean(x[i:i + hop] ** 2)) for i in range(0, len(x) - hop, hop)])
    env = env - np.mean(env)
    if len(env) < 64:
        env = np.pad(env, (0, 64 - len(env)))
    fm, pm = signal.welch(env, 1 / 0.005, nperseg=min(len(env), 1024))
    mods = []
    for c in (0.5, 1, 2, 4, 8, 16, 32):
        m = (fm >= c / np.sqrt(2)) & (fm < c * np.sqrt(2))
        mods.append(np.sum(pm[m]))
    mods = np.array(mods) / (np.sum(mods) + 1e-20)
    return bands, mods, dsp.centroid(x)


def main():
    rows = {}
    for line in NAMES:
        x, where = sound_of(line)
        if x is None:
            continue
        rows[line] = (*features(x), where)
    lines = list(rows)
    out = ["# Tell distinctness", "", "| Tell | Called out as | Brightest octave | Centre | Rhythm peak | From |", "|---|---|---|---|---|---|"]
    for l in lines:
        b, m, c, w = rows[l]
        out.append(f"| {l} | {NAMES[l]} | {OCTAVES[int(np.argmax(b))]} Hz | {c:.0f} Hz | {[0.5, 1, 2, 4, 8, 16, 32][int(np.argmax(m))]} Hz | {w} |")
    out += ["", "## Pairs an ear could confuse (spectrum and rhythm both close)", ""]
    flagged = 0
    for i, a in enumerate(lines):
        for b_ in lines[i + 1:]:
            ds = 0.5 * np.sum(np.abs(rows[a][0] - rows[b_][0]))   # 0 same, 1 disjoint
            dr = 0.5 * np.sum(np.abs(rows[a][1] - rows[b_][1]))
            if ds < 0.35 and dr < 0.35:
                out.append(f"- {a} and {b_}: spectrum {ds:.2f}, rhythm {dr:.2f} apart. Never stage them together, or move one.")
                flagged += 1
    if not flagged:
        out.append("- None: every pair differs in where it sits or how it moves.")
    text = "\n".join(out) + "\n"
    os.makedirs(OUT, exist_ok=True)
    open(os.path.join(OUT, "tellcheck.md"), "w").write(text)
    print(text)


if __name__ == "__main__":
    main()
