"""A ping heard (queue #507, note 777; A1's #1024, note 739), as tools/audio/cues.py lists it and GameAudio.Interface plays
it: the crew's pings marked for everyone, once each, through the radio every uniform has (note 749).

What it is decides the build. A ping is an order passed down the train ("the fire, car 2"; "take the controls"): it has
to be heard over the engine by everyone, read at once as the crew's own and nothing out in the dark, and be short enough
to come several in a breath when the crew are busy. So: the radio's own voice, through the set's little speaker (300
Hz-3 kHz, world_voice.speaker), brief and bright, in the mids clear of every tell and the train's alarms (the overspeed
bell's 2 kHz strokes, the whistle).

Two candidates. The first is the handset keyed and a roger beep: the packs' real switch clicks for the key (as the
radio's own key-down, world_voice.SWITCHES), a quick rising two-note beep (900 then 1300 Hz, square-ish, sixty
milliseconds each) through the speaker, the key let go. The second is the railway's own: a telegraph sounder's armature
clacking down and up twice (the packs' real clicks, pitched for a brass bar on its anvil), heard through the set.
"""

import numpy as np

import dsp
from build import recipe
from dsp import samples, env
from recipes import world_kit as W
from recipes.world_kit import norm
from recipes.world_voice import SWITCHES, speaker


def beep(rng, f, L):
    """One note of the roger beep: a square-ish tone (odd harmonics, rounded), its edges softened, a hair unsteady."""
    n = samples(L)
    t = np.arange(n) / dsp.SR
    ph = 2 * np.pi * f * (1 + 0.002 * rng.standard_normal()) * t
    x = np.tanh(3 * np.sin(ph)).astype(np.float32)
    return x * env([(0, 0), (0.004, 1), (L - 0.006, 1), (L, 0)], L)[:n]


def clack(rng, k, semis):
    """A sounder's armature striking its anvil: a real click pitched down for a brass bar, short."""
    a, b_ = SWITCHES[k % len(SWITCHES)]
    return norm(W.rec(a if rng.random() < 0.5 else b_, semis=semis))


@recipe("ui-pings", "mark", "roger",
        "A ping: the radio keyed, a quick rising two-note beep through its speaker, the key let go",
        """The radio every uniform has (note 749) passing the ping down the train: the packs' real switch clicks for the
        handset's key down and up, between them a roger beep, two notes rising (900 then 1300 Hz, sixty milliseconds each,
        square-ish), all through the set's little speaker (300 Hz-3 kHz, a cone resonance, a touch of grit). Brief, bright
        and plainly the crew's: nothing alive sounds like it. Three takes, three switches.""",
        sources=[k for pair in SWITCHES for k in pair], takes=3, lufs=-20)
def mark_roger(rng, k):
    a, b_ = SWITCHES[k]
    down = norm(W.rec(a, semis=-rng.uniform(3, 5)))
    up = norm(W.rec(b_, semis=-rng.uniform(0, 2)))
    lo, hi = 900 * rng.uniform(0.98, 1.02), 1300 * rng.uniform(0.98, 1.02)
    b = dsp.Bus(0.4)
    b.at(0.015, down * 0.7)
    # The speaker's filters ring on past the last note: room for them, so the beep's end isn't a click.
    tone = np.concatenate([beep(rng, lo, 0.06), np.zeros(samples(0.012), np.float32), beep(rng, hi, 0.06),
                           np.zeros(samples(0.04), np.float32)])
    b.at(0.04, speaker(tone) * 0.9)
    b.at(0.19, up * 0.5)
    return b.x


@recipe("ui-pings", "mark", "sounder",
        "A ping, the railway's way: a telegraph sounder clacking down and up twice, heard through the radio",
        """A telegraph sounder's brass armature striking its anvil and springing back, twice, quick ("click-clack,
        click-clack"), the packs' real clicks pitched down a little for a brass bar; then the bar's faint ring. Heard
        through the radio's speaker so it is the crew's set, not a creature at the window. Three takes, spaced a little
        differently.""", sources=[k for pair in SWITCHES for k in pair], takes=3, lufs=-20)
def mark_sounder(rng, k):
    b = dsp.Bus(0.45)
    gap = rng.uniform(0.055, 0.07)
    at = 0.015
    for i in range(2):
        b.at(at, clack(rng, k + i, -rng.uniform(2, 4)) * 0.9)
        b.at(at + gap * 0.6, clack(rng, k + i + 1, -rng.uniform(5, 7)) * 0.55)
        at += gap * 1.6
    n = samples(0.18)
    t = np.arange(n) / dsp.SR
    ring = (np.sin(2 * np.pi * 1650 * t) + 0.4 * np.sin(2 * np.pi * 2470 * t)).astype(np.float32) * np.exp(-t / 0.05).astype(np.float32)
    b.at(at, ring * 0.12)
    y = speaker(np.concatenate([b.x, np.zeros(samples(0.04), np.float32)]))
    return y * env([(0, 1), (0.4, 1), (0.49, 0)], len(y) / dsp.SR)[:len(y)]
