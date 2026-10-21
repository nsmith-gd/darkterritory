"""A pin working loose heard the length of the train (queue #680, note 980; note 865, #592: "I want a pin to jump almost never
and when it does it should have hella telegraphing to the players"), as tools/audio/cues.py lists it and
GameAudio.Faults.LooseSounds plays it: one knock a take, fired three times a second at the warning's start and ten at its
end, at the pin, so the cab hears it from the engine and the roofs from the far end of the rake.

What it is decides the build: the same pin hammering in the same knuckle as the ordinary knock (recipes/jobs.py's
state-coupling-loose.knock), but driven by the train's slack at speed and about to go, so it's struck harder and the
knuckle rings out. Over a few hundred metres of wind and wheels a dull clank is lost: what carries is the ring, so each
take keeps the knock's body (a heavy iron slam round 360 Hz, choked: the casting's loaded) and lets the coupler head's
clang through, its partials at 1.15 and 1.75 kHz (the pin's, as the knock's own ring) dying over a tenth of a second.
Lifted through 0.7-2 kHz, where the train's bed is thinnest; nothing under 150 Hz. Never a creature's tell (spec A.4): not
the Knotter's wet rope, not the Brakeman's iron-shod limp (a quickening clang at a gap, not steps on the roofs).

Two candidates: the packs' real slams and metal rings, and the knuckle's casting modelled, struck hard and ringing longer.
"""

import numpy as np

import dsp
from dsp import samples, hp
from recipes import crew_kit as ck
from recipes.crew_kit import recipe
from recipes.jobs import KNOCKS, METAL_H, METAL_M, METAL_L, CASTING, RING, strike, ring, between, oneshot, norm

L_ = "state-coupling-loose"
WARN_LEN = 0.35
CLANG = 1150                                        # the coupler head's lower ringing partial, under the pin's 1.75 kHz


def carry(y):
    """Lift what carries over wind and wheels (0.7-2 kHz, where the bed is thinnest), take out what doesn't (under 150 Hz)."""
    y = dsp.peak(y, 1100, 0.8, 5)
    return hp(y, 150, 4)


@recipe(L_, "warn", "clang", "A pin working loose: struck hard in its knuckle, the coupler ringing out the length of the train",
        """From real recordings: the packs' heavy knock-slam and metal door hit (the knock's own body) pitched down one to three
        semitones and choked at 50 ms, lifted round 360 Hz; over it two real metal rings tuned to the coupler head's partials,
        1.15 kHz (a heavy hit's) and 1.75 kHz (a medium hit's, the pin's), let ring for a tenth of a second; lifted through
        0.7-2 kHz, where the train's bed is thinnest, so it carries. The car ends either side throw it back. One knock a take;
        the game fires them three to ten a second as the pin comes to jumping.""",
        sources=KNOCKS + METAL_H + METAL_M, takes=4, lufs=-18)
def warn_clang(rng, k):
    body = strike(KNOCKS[k % 2], -rng.uniform(1, 3), lo=150, at=0.05, tau=0.05)
    body = dsp.peak(body, 360 * rng.uniform(0.95, 1.05), 1.2, 5)
    low = ring(rng, METAL_H[(k + 1) % 5], 0.06, 0.1, target=CLANG)
    high = ring(rng, METAL_M[(1, 3, 2, 4)[k]], 0.05, 0.12)
    y = ck.place([(0, norm(body), 0), (0.002, low, -7), (0.001, high, -5)])
    return oneshot(carry(between(y)), WARN_LEN)


@recipe(L_, "warn", "casting", "A pin working loose, modelled: the knuckle's casting struck hard and ringing",
        """A real heavy metal strike (its first 50 ms, harder than the knock's 30) rung through the cast-steel knuckle's own modes
        from 330 Hz, sharper (q 18) so it rings longer than the knock's dead clank, the coupler head's 1.15 kHz and the pin's
        1.75 kHz over it from real light metal rings; lifted through 0.7-2 kHz. One knock a take.""",
        sources=METAL_H + METAL_L, takes=4, lufs=-18)
def warn_casting(rng, k):
    hit = hp(ck.align(ck.get(METAL_H[k % 5])), 150)[:samples(0.05)]
    hit = dsp.fade(hit, 0.0005, 0.02)
    f1 = 330 * rng.uniform(0.92, 1.08)
    fs = [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]
    gains = [rng.uniform(0.4, 1.0) / (1 + 0.25 * i) for i in range(len(fs))]
    cast = dsp.resonate(dsp.fit(hit, samples(WARN_LEN)), fs, q=rng.uniform(16, 20), gains=gains)
    cast = ck.choke(hp(cast, 180, 2), rng.uniform(0.1, 0.13), 0.07)
    low = ring(rng, METAL_L[(3, 1, 0, 2)[k]], 0.07, 0.09, target=CLANG)
    high = ring(rng, METAL_L[(2, 0, 4, 1)[k]], 0.06, 0.1)
    y = ck.place([(0, norm(cast), 0), (0, norm(hit), -9), (0.001, low, -9), (0.001, high, -8)])
    return oneshot(carry(between(y)), WARN_LEN)
