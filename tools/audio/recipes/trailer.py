"""The store trailer's soundtrack (§36): cut from the game's own sounds, as the line asks: the bed, slack action, a tell,
the crew shouting. Built last, from whatever the other recipes and the kept earlier takes have made (out/audio/takes and
out/audio/earlier): a rough cut for the editor to time pictures to, not a finished mix.
"""

import glob
import os

import numpy as np

import dsp
from build import recipe
from dsp import Bus

OUT = os.path.normpath(os.path.join(os.path.dirname(__file__), "..", "..", "..", "out", "audio"))


def _take(pattern):
    hits = sorted(glob.glob(os.path.join(OUT, pattern)))
    return dsp.load(hits[0]) if hits else None


def _norm(x, db):
    return dsp.gain(x / (np.max(np.abs(x)) + 1e-9), db) if x is not None else None


@recipe("store-trailer", "mix", "night-run", "A 60 s rough cut: night, the train coming on, the slack, the Choir, the crew, then silence and a giggle",
        """Built from the game's own sounds. 0-10 s: wind, the work drone and a far howl. 10-24 s: the train comes on (wheels,
        the boiler's roar, the conductor's whistle). 24-30 s: slack action running down the cars. 30-44 s: the Choir's
        round gathering over it. 44-52 s: a prisoner shouting for help, a cannon far off, the Car Hugger's grinding. 52 s:
        everything cuts; a porcelain giggle in the dark; the end card's stamp.""",
        takes=1, lufs=-16)
def night_run(rng, k):
    L = 60.0
    b = Bus(L + 4)
    put = lambda t, x, db: x is not None and b.at(t, _norm(x, db))  # noqa: E731
    drone = _take("takes/ui-music/drone/ground_00.wav")
    if drone is not None:
        b.at(0, dsp.shaped(_norm(np.tile(drone, 2)[:dsp.samples(52)], -8), [(0, 0), (3, 1), (50, 1), (52, 0)]))
    title = _take("takes/ui-menus/title/far-train_00.wav")
    put(0, title[:dsp.samples(12)] if title is not None else None, -10)
    put(4.0, _take("earlier/audio/tell-hounds--far.mp3"), -12)
    wheels = _take("earlier/audio/bed-wheel-rail--library.mp3")
    roar = _take("earlier/audio/bed-boiler-roar--roar.mp3")
    for x, db in ((wheels, -8), (roar, -10)):
        if x is not None:
            seg = np.tile(x, int(np.ceil(42 * dsp.SR / len(x))))[:dsp.samples(42)]
            b.at(10, dsp.shaped(_norm(seg, db), [(0, 0), (8, 1), (40, 1), (42, 0)]))
    put(16.0, _take("earlier/audio/tell-whistler--conductor.mp3"), -6)
    put(24.0, _take("earlier/audio/bed-slack--run.mp3"), -4)
    choir = [_take(f"takes/tell-choir/voices/round_0{i}.wav") for i in range(4)]
    for i, c in enumerate(c for c in choir if c is not None):
        b.at(30 + i * 2.5, dsp.shaped(_norm(c, -10 + i), [(0, 0), (2, 1), (20 - i * 2.5, 1), (22 - i * 2.5, 0)]))
    put(44.0, _take("takes/voice-prisoner-sets/call/set1_00.wav"), -8)
    put(45.5, _take("takes/voice-prisoner-sets/call/set1_04.wav"), -8)
    put(46.5, _take("earlier/audio/crew-cannon-fire--far.mp3"), -4)
    put(47.0, _take("earlier/audio/tell-car-hugger--grind.mp3"), -8)
    x = b.x
    cut = dsp.samples(52)
    x[cut:] = 0
    tail = Bus(L + 4)
    tail.at(53.5, _norm(_take("earlier/audio/tell-track-doll--heh.mp3"), -10))
    tail.at(57.0, _norm(_take("takes/ui-menus/end-card/stamp_00.wav"), -6))
    return (x + tail.x)[:dsp.samples(L + 2)]
