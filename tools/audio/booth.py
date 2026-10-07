#!/usr/bin/env python3
"""The voice booth (queue #42): the director's own recordings, disguised so they aren't the director's voice.

The Dark Territory Voice Booth artifact (https://claude.ai/artifact/3VvuCkMwP3hJ6iDb1PZaoe) holds the script (`lines`), the
director's takes (`takes`: an asset each, a phone voice memo as video/mp4, or any other file wrapped in a JSON envelope) and
the disguised versions (`versions`, each with the director's Keep or Redo). The loop, as the audio chat runs it:

  1. ArtifactData list `takes` and `lines` with out_dir=DIR; for each take whose status is "new", Artifact read with
     path=<its asset id> saves the file locally
  2. python3 tools/audio/booth.py make DIR/lines/<line>.json FILE --out OUT
         -> OUT/<key>.wav (the take for the game) and OUT/<key>.mp4 (the page's preview), and OUT/versions.json
  3. upload the .mp4s to the booth's asset store; a `versions` doc per entry (line, take, asset, label, how, order); the
     take's status to "processed"

The page's source is tools/audio/booth/voice-booth.html (republish it to the same artifact with the Artifact tool and its
url) and the draft script it was seeded with is tools/audio/booth/script.json (ArtifactData `set` writes, one per line).

Every disguise moves the voice's size (its formants) as well as its pitch, so it's another throat and not a sped-up
recording of the same one: a listener knows a voice by its formants and the way it moves, which pitch alone doesn't hide.
Kept versions become candidates on the audio checklist like any recipe's (voice-prisoner-sets, tell-soot-children, ...).
"""

import base64
import json
import os
import sys
import tempfile

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import dsp  # noqa: E402

SR = dsp.SR


def unwrap(path):
    """A take as the booth stored it -> a playable file: the page wraps anything that isn't MP4 or WebM in JSON."""
    with open(path, "rb") as f:
        head = f.read(64).lstrip()
    if not head.startswith(b"{"):
        return path
    w = json.load(open(path))
    ext = os.path.splitext(w.get("name") or "")[1] or ".wav"
    fd, out = tempfile.mkstemp(suffix=ext)
    with os.fdopen(fd, "wb") as f:
        f.write(base64.b64decode(w["data"]))
    return out


def clean(x):
    """A phone take, tidied: rumble off, the dead air at each end gone, long pauses closed to half a second, levelled."""
    x = dsp.hp(x, 80, 2)
    x = dsp.trim_silence(x, db=-45, pad=0.05)
    frame = dsp.samples(0.02)
    loud = np.array([np.sqrt(np.mean(x[i:i + frame] ** 2)) for i in range(0, len(x), frame)]) + 1e-9
    voiced = 20 * np.log10(loud / loud.max()) > -40
    keep, quiet = [], 0
    for i, v in enumerate(voiced):
        quiet = 0 if v else quiet + 1
        if quiet * 0.02 <= 0.5:
            keep.append(x[i * frame:(i + 1) * frame])
    return dsp.level(np.concatenate(keep) if keep else x, -20)


def person(x, pitch, size):
    """Another throat: formants moved `size` semitones (a bigger or smaller tract), the pitch to `pitch` in all."""
    y = dsp.shift(x, size, formant=False)
    return dsp.shift(y, pitch - size, formant=True)


def layered(x, rng, voices):
    """Many voices out of one: each (pitch, size, gain) a person of its own, a little late and out of step."""
    out = np.zeros(len(x) + dsp.samples(0.4), np.float32)
    for pitch, size, gain in voices:
        y = person(x, pitch + rng.uniform(-0.3, 0.3), size + rng.uniform(-1, 1))
        at = dsp.samples(rng.uniform(0, 0.3))
        out[at:at + len(y)] += gain * y[:len(out) - at]
    return out


# ---- the disguises, by group (and for the creatures, by the cue the line is for): [(key, label, how, fn(x, rng))]

def prisoners():
    wall = lambda y, rng: dsp.room(dsp.lp(y, 3200, 2), "box", wet=0.12, rng=rng)
    return [
        ("deep", "A deep, heavy-set man", "Down 4 semitones with a throat 2.5 bigger; a little gravel; through a door.",
         lambda x, rng: wall(dsp.saturate(person(x, -4, -2.5), 3), rng)),
        ("old", "An older man, rough", "Down 2 with a throat 1 bigger, roughened and shaky; through a door.",
         lambda x, rng: wall(dsp.tremolo(dsp.saturate(person(x, -2, -1), 7), 6, 0.15, rng, jitter=0.6), rng)),
        ("thin", "A higher, thinner man", "Up 3 with a throat 1.5 smaller; through a door.",
         lambda x, rng: wall(person(x, 3, 1.5), rng)),
        ("woman", "A woman", "Up 6 with a throat 3 smaller; through a door.",
         lambda x, rng: wall(person(x, 6, 3), rng)),
    ]


def children():
    from recipes.children import child
    return [
        ("girl", "A young girl", "A throat 4.5 semitones smaller, then 4 more of pitch; a raised voice's edge; open air.",
         lambda x, rng: child(x, rng, 4.5, 4.0)),
        ("boy", "A young boy", "A throat 4 smaller, then 3 more of pitch; open air.",
         lambda x, rng: child(x, rng, 4.0, 3.0)),
        ("small", "A small child", "A throat 6 smaller, then 5 more of pitch; open air.",
         lambda x, rng: child(x, rng, 6.0, 5.0)),
    ]


def radio(y):
    """How the game's radio set will colour it (ClerkVoice crushes it itself): for the preview only."""
    return dsp.crush(dsp.saturate(dsp.bp(y, 300, 3000, 2), 4), bits=10)


def clerk():
    return [
        ("flat", "An older, flatter man", "Down 3 with a throat 1.5 bigger, heard through the yard's radio set as the game plays it.",
         lambda x, rng: radio(person(x, -3, -1.5))),
        ("deep", "A deeper man", "Down 5 with a throat 3 bigger, through the radio set.",
         lambda x, rng: radio(person(x, -5, -3))),
    ]


def crew():
    return [
        ("a", "A bigger crewmate", "Down 1 with a throat 1 bigger: breath carries little voice, so only a nudge.",
         lambda x, rng: person(x, -1, -1)),
        ("b", "A smaller crewmate", "Up 1 with a throat 1 smaller.",
         lambda x, rng: person(x, 1, 1)),
    ]


CREATURES = {
    "tell-track-doll.giggle": [
        ("porcelain", "Porcelain", "Up 8 with a throat 6 smaller, lifted at 4.2 kHz where her giggle rings, a hollow head's ring under it.",
         lambda x, rng: dsp.peak(dsp.hp(person(x, 8, 6), 500, 2), 4200, 1.2, 8) + 0.15 * dsp.resonate(person(x, 8, 6), [3900, 4700, 5300], q=40)),
        ("flutter", "A fluttering doll", "Up 10 with a throat 4 smaller, fluttering, lifted at 4.2 kHz.",
         lambda x, rng: dsp.peak(dsp.tremolo(person(x, 10, 4), 14, 0.35, rng, jitter=0.4), 4200, 1.2, 6)),
    ],
    "tell-choir.voices": [
        ("gather", "The Choir gathering", "Twelve voices from yours: octaves and fifths below and above, each its own throat, out of step; a hall.",
         lambda x, rng: dsp.room(layered(x, rng, [(p, p * 0.6, g) for p, g in [(-12, .7), (-12, .5), (-5, .8), (-5, .6), (0, .9), (0, .6),
                                                                               (3, .5), (7, .6), (7, .4), (12, .35), (-17, .4), (-7, .5)]]), "hall", wet=0.4, rng=rng)),
        ("closing", "The Choir closing in", "Slowed by half again, darker, the low voices to the front; a hall.",
         lambda x, rng: dsp.room(dsp.lp(layered(dsp.stretch(x, 1.5, smooth=True), rng,
                                                [(p, p * 0.6, g) for p, g in [(-12, .9), (-12, .7), (-17, .6), (-5, .7), (0, .5), (-24, .3)]]), 3000, 2), "hall", wet=0.45, rng=rng)),
    ],
    "voice-dead": [
        ("deadair", "From under the static", "Slowed 1.4 times, down 3 with a throat 2 bigger, wavering, through a dead radio with its hiss.",
         lambda x, rng: (lambda y: dsp.crush(dsp.saturate(dsp.bp(dsp.wow(y, 35, 0.4, rng), 400, 2500, 2), 8), bits=6)
                         + 0.03 * dsp.bp(rng.standard_normal(len(y)).astype(np.float32), 1500, 6000, 2))(person(dsp.stretch(x, 1.4), -3, -2))),
    ],
    "tell-hounds.howl-far": [
        ("hound", "A hound across the valley", "Down 9 with a throat 7 bigger, an octave below it, roughened; far off in the night.",
         lambda x, rng: dsp.room(dsp.lp(dsp.saturate(person(x, -9, -7) + 0.4 * person(x, -21, -12), 6), 2500, 2), "night", wet=0.45, rng=rng)),
        ("pair", "Two answering", "Two hounds from one howl, a fifth apart and out of step; far off.",
         lambda x, rng: dsp.room(dsp.lp(layered(x, rng, [(-9, -7, .9), (-14, -10, .7)]), 2500, 2), "night", wet=0.5, rng=rng)),
    ],
    "cs-hounds.snarl": [
        ("snarl", "A hound's snarl", "Down 7 with a throat 6 bigger, an octave below, driven hard.",
         lambda x, rng: dsp.saturate(person(x, -7, -6) + 0.5 * person(x, -19, -10), 12)),
    ],
    "cs-grumbler.feral": [
        ("torn", "Torn and doubled", "Two throats from yours (down 5 and down 9), driven and ring-modulated for grit.",
         lambda x, rng: dsp.saturate(dsp.ringmod(person(x, -5, -4), 30) * 0.5 + person(x, -9, -7) * 0.8, 10)),
    ],
    "cs-grumbler.eat": [
        ("wet", "Wetter and slower", "Slowed by a quarter, down 3 with a throat 3 bigger, the mouth's wet low-mids lifted; in a crate.",
         lambda x, rng: dsp.room(dsp.peak(person(dsp.stretch(x, 1.25), -3, -3), 400, 1.0, 6), "box", wet=0.15, rng=rng)),
    ],
    "cs-gaunt.death": [
        ("large", "Something large", "Slowed 1.3 times, down 6 with a throat 5 bigger, a long tail.",
         lambda x, rng: dsp.room(person(dsp.stretch(x, 1.3), -6, -5), "hall", wet=0.3, rng=rng)),
        ("small", "Something small", "Up 4 with a throat 6 smaller, thinned.",
         lambda x, rng: dsp.hp(person(x, 4, 6), 300, 2)),
    ],
}
ANY = [
    ("man", "Another man", "Down 3 with a throat 2 bigger.", lambda x, rng: person(x, -3, -2)),
    ("woman", "A woman", "Up 5 with a throat 3 smaller.", lambda x, rng: person(x, 5, 3)),
    ("creature", "Something else", "Down 7 with a throat 6 bigger, driven.", lambda x, rng: dsp.saturate(person(x, -7, -6), 8)),
]


def disguises(line):
    group, cue = line.get("group"), line.get("forCue") or ""
    if group == "prisoners" and cue.startswith("voice-prisoner"):
        return prisoners()
    if group == "children" and cue == "tell-soot-children.call":
        return children()
    if group == "clerk":
        return clerk()
    if group == "crew":
        return crew()
    return CREATURES.get(cue, ANY)


def preview(path, y):
    """The page's .mp4, never clipping: AAC drops a driven take's top harmonics and its peaks come back up to 4 dB higher
    than they went in (the roughened prisoners clipped), and not in step with the level (2 dB down came out 0.4 dB under
    full scale), so it's decoded and measured, and made again lower till it's under -0.5 dB."""
    g = 1.0
    for _ in range(8):
        dsp.write_preview(path, y * g)
        peak = 20 * np.log10(np.abs(dsp.load(path)).max() + 1e-9)
        if peak <= -0.5:
            return
        g *= 10 ** (-(peak + 1.0) / 20)
    raise SystemExit(f"{path}: the preview still peaks at {peak:.1f} dB")


def make(line_path, take_path, out):
    line = json.load(open(line_path))
    line = line.get("data", line)
    x = clean(dsp.load(unwrap(take_path)))
    os.makedirs(out, exist_ok=True)
    rng = np.random.default_rng(int.from_bytes(os.path.basename(take_path).encode()[:8].ljust(8, b"\0"), "little"))
    made = []
    for i, (key, label, how, fn) in enumerate(disguises(line)):
        # The rooms' tails trimmed where they fall under -50 dB: a take is the voice, not seconds of night after it.
        y = dsp.level(dsp.fade(dsp.trim_silence(np.asarray(fn(x, rng), np.float32), db=-50, pad=0.05)), -18)
        wav, mp4 = os.path.join(out, key + ".wav"), os.path.join(out, key + ".mp4")
        dsp.write_wav(wav, y)
        preview(mp4, y)
        made.append({"key": key, "label": label, "how": how, "order": i + 1, "wav": wav, "mp4": mp4,
                     "seconds": round(len(y) / SR, 2)})
        print(f"{key:10s} {label} ({len(y) / SR:.1f} s)")
    json.dump(made, open(os.path.join(out, "versions.json"), "w"), indent=1)


if __name__ == "__main__":
    if len(sys.argv) >= 4 and sys.argv[1] == "make":
        out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else "out/audio/booth"
        make(sys.argv[2], sys.argv[3], out)
    else:
        print(__doc__)
