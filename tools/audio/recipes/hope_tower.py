"""The Hope Tower's Flame of Hope heard (queue #554, note 826; B2's #1091, note 819): a harbour town's light is a coal fire in
an iron basket on an 80 m stone tower, "Flames of Hope for the world to find in the darkness" (the director, 12 Oct). It
burnt in silence over the town.

- The Flame (place-town.flame): a great coal fire burning in the open on the tower's gallery, the wind off the sea at it.
  Heard from the town under it and from the line, so its roar is its weight: a big low breathing roar that swells as
  the gusts fan it, the bed of coals hissing white-hot, cinders cracking off and going up. Loops, one held at the top of
  the tower (GameAudio.Town).

Both candidates are kept under 4.5 kHz: it's high up and far off, and it must never be taken for a car alight (the car-fire
tell is 6-9 kHz, spec A.4). And none of it is a tell: the town's light, tier 6, under everything that is.

Synthesised: the packs have no fire (as the square's fires, recipes/town.py); the packs' real metal ticks for the basket's
iron.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe
from recipes.town import TICKS, lapping, seam, ticks

L = "place-town"
LOOP = 10.0
# Over this it reads as a car alight (car-fire's 6-9 kHz band); and far up, the air's taken the top off it anyway.
TOP = 4500


def gusts(rng, n, rate=0.25, depth=0.45):
    """The wind off the sea fanning it: slow swells a few seconds apart."""
    return np.clip(1 - depth + depth * W.norm(lp(rng.standard_normal(n).astype(np.float32), rate)), 0.2, 1.5)


def cinders(rng, length):
    """Cinders cracking off the coals and going up: a few big pops, many quiet ones, all dulled by the height."""
    big = synth.crackle(length, 1.5, rng, size=(0.002, 0.01), hi=400)
    fine = synth.crackle(length, 25, rng, size=(0.0005, 0.003), hi=900)
    return lp(W.norm(big) * 0.8 + W.norm(fine) * 0.25, 3500)


@recipe(L, "flame", "basket", "The Flame of Hope: a great coal fire in an iron basket high on its tower, roaring as the wind fans it",
        """A coal fire as big as a cart in an iron basket on the Hope Tower's gallery, open to the wind off the sea: a deep
        breathing roar (brown noise lapping at a couple of hertz, its weight from 120 Hz up) that swells and lifts as each
        gust fans it, the white-hot bed under it hissing, cinders cracking off and going up, and now and then the basket's
        iron ticking (the packs' real metal ticks, low and dull). Lowpassed at 4.5 kHz: heard from far below and far off,
        and never a car alight. A 10 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-22, seconds=LOOP)
def flame_basket(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    g = gusts(rng, n)
    roar = synth.fire(L_, 0.95, rng)
    breath = lp(hp(W.norm(synth.noise(L_, rng, "brown")), 120), 650) * lapping(rng, n, 1.8, 0.6)
    bed = ck.norm(bp(synth.noise(L_, rng), 1200, 3800)) * lapping(rng, n, 0.8, 0.3)
    # The bed's hiss and the cinders are what make it a fire and not a rumble: up, against the roar's weight.
    y = mix(roar * g * 0.55, W.norm(breath) * g ** 1.5 * 0.45, bed * (0.4 + 0.6 * g) * 0.1, cinders(rng, L_) * 0.7,
            ticks(rng, L_, TICKS, 0.25, -8, (-30, -22)) * 0.5)
    return seam(lp(hp(y, 90), TOP), LOOP)


@recipe(L, "flame", "tongues", "The Flame of Hope: tongues of fire three men high, torn and flapping in the wind over the basket",
        """The same fire heard by its flames rather than its bed: tongues taller than three men leaning with the wind,
        tearing and flapping like canvas (the roar fluttering at 6-12 Hz in bursts as a gust takes them, a flag's flap),
        a steady draught under them pulling through the basket's bars, cinders snapping up and away. Lowpassed at 4.5 kHz.
        A 10 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-22, seconds=LOOP)
def flame_tongues(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    g = gusts(rng, n, 0.3, 0.55)
    # The flap: a flutter that quickens and deepens with the gust, as canvas does.
    rate = 6 + 6 * np.clip(g - 0.5, 0, 1)
    phase = np.cumsum(rate) / dsp.SR
    flap = 1 - 0.45 * np.clip(g - 0.4, 0, 1) * (0.5 + 0.5 * np.sin(2 * np.pi * phase))
    tongues = lp(hp(W.norm(synth.noise(L_, rng, "pink")), 150), 1800) * flap * g
    draught = lp(hp(W.norm(synth.noise(L_, rng, "brown")), 110), 500) * lapping(rng, n, 1.2, 0.35)
    roar = synth.fire(L_, 0.7, rng)
    y = mix(W.norm(tongues) * 0.55, W.norm(draught) * 0.35, roar * g * 0.35, cinders(rng, L_) * 0.7)
    return seam(lp(hp(y, 90), TOP), LOOP)
