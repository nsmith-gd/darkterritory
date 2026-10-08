"""The hounds heard on their patrol aboard (queue #214, note 478; D1's #208: hounds aboard "should either patrol between
cars that have doors open or patrol the roofs of the cars, jumping between them if they can make the jump"). Their paws
on the roof's tin and the running boards are beasts.py's; inside a car with its doors open a hound walks the boards, and
had no paw for them (the game fell back to its ballast paw).

- On the boards (`cs-hounds.paw` on wood): toe-first like the others. Its nails tick on the planks (dry and short, wood
  not steel), then the pad's soft weight through a plank floor's ring (a real heavy plank's decay, reached only in its
  low modes, choked by the dog standing on it), a board giving a little under the hind feet. The same puff of ash and
  embers as the kept roof paw.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import kit
from recipes.beasts import done, unit, paw_tail, ring_of, through, soft_pad, takes_then, gallop

PLANK = [f"kenney_impact-sounds:impactPlank_medium_{i:03d}" for i in range(5)]     # a car floor's planks


def wood_nail(rng, semis=0.0):
    """A nail on a plank: a dry, short tick through a board's upper modes (no steel's ring)."""
    x = synth.click(rng.uniform(1800, 2800) * 2 ** (semis / 12), q=rng.uniform(2.5, 4), length=0.012, rng=rng,
                    body=[f * rng.uniform(0.9, 1.1) for f in synth.WOOD[2:5]])
    return unit(x)


def paw_boards(rng, k):
    hind = k % 2 == 1
    w = rng.uniform(0.85, 1.0) * (1.25 if hind else 1.0)
    b = Bus(0.6)
    t0 = 0.03
    n = int(rng.integers(2, 4))
    spread = rng.uniform(0.008, 0.02)
    for i in range(n):
        b.at(t0 - spread + spread * i / max(n - 1, 1) + rng.uniform(-0.002, 0.002), wood_nail(rng, rng.uniform(-2, 2)),
             rng.uniform(-11, -6))
    ring = dsp.vari(ring_of(PLANK[k % len(PLANK)]), rng.uniform(-2, 0))
    pad = ck.choke(through(soft_pad(rng, rng.uniform(2.5, 4)), ring), 0.02, rng.uniform(0.03, 0.05))
    b.at(t0, unit(hp(pad, 90)) * w, -5)
    b.at(t0 + rng.uniform(0, 0.004), kit.thud(rng, rng.uniform(85, 115), 0.07, w), -15)
    if hind and rng.random() < 0.6:     # a board giving a little under the weight: a short dry creak
        cr = synth.creak(0.12, rng.uniform(60, 110), rng, body=[f * 0.9 for f in synth.WOOD[:4]], q=14, jitter=0.4)
        b.at(t0 + 0.02, unit(cr * env([(0, 0), (0.02, 1), (0.12, 0)], 0.12)[:len(cr)]), -20)
    b.at(t0 + 0.01, paw_tail(rng, 0.4, puff=0.5, sparks=rng.uniform(0.6, 1.0)), -2)
    return b.x


recipe("cs-hounds", "paw", "boards",
       "A hound's paw on a car's plank floor: its nails ticking on the boards, the pad's soft weight, ash and embers",
       """Inside a car with its doors open: toe-first, two or three nails tick dry and short on the planks (a click rung
       through a board's upper modes, no steel's ring), then the pad: a soft heavy blow convolved through a real heavy
       plank's decay so only the floor's low modes answer, choked as the dog stands on it, its weight a dull thud under
       it; a board creaks a little under a hind foot. The same puff of ash and embers as the kept roof paw. Odd takes
       are hind feet, heavier.""",
       sources=PLANK, takes=6, mat="wood", lufs=-22, gap=0.3, preview=takes_then(gallop))(lambda rng, k: done(paw_boards(rng, k)))
