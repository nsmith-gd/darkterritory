"""Burned at a fire's edge, heard (queue #561, note 833; C1's note 821, the director's 14 Oct playtest, line 25: "I just died
because I wasn't getting any feedback that I was being burned"). As tools/audio/cues.py lists it and GameAudio.Scorch plays
it, off the fire's heat on each crewmate (CarFire.HeatOn, the burn's own measure, which every machine reckons alike).

- Scorched (crew-scorch.sizzle, a loop): held on whoever the fire's heat reaches, from its first tick, louder the hotter
  (the HUD's heat glow's curve: never under 0.4 at the edge, full at 0.7 heat). Their coat smouldering on them: cloth and
  hair singeing, a dry fizzing hiss and a fine crackle of fibres catching, close, on the body. Not the fire's own crackle:
  it's held under 5 kHz, under the car-fire tell's 6-9 kHz (spec A.4), and dry, at the body, where the fire is the room's.

Two candidates: the packs' real cloth crackle and fizz, and one modelled.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, bp, mix
from recipes import world_kit as W
from recipes.crew_kit import recipe, S
from recipes.figures import FIBRES, at, loop, band_shape
from recipes.house_folk import bed, drawn

L_ = "crew-scorch"
CYCLE = 8.0
FIZZ = S("air_02", "air_03")               # a bottle opened, its fizz: the fabric's smoke hissing out of the weave
TOP = 5000                                 # under the car-fire tell's 6-9 kHz band (spec A.4)


def flicker(rng, n, rate=3.0, depth=0.5):
    """The smoulder catching and dying back: slow swells a few times a second, never even."""
    return np.clip(1 - depth + depth * W.norm(lp(rng.standard_normal(n).astype(np.float32), rate)), 0.15, 1.5)


def smouldering(rng, real):
    """A coat smouldering on someone: a dry fizzing hiss out of the weave, swelling as it catches, and a fine crackle of
    fibres and hair singeing over it, now and then a sharper snap. A seamless cycle, close and dry."""
    n = samples(CYCLE)
    f = flicker(rng, n)
    if real:
        # The fizz drawn out (its loudest quarter second stretched), not laid end to end: the recording has silences.
        hiss = W.norm(bp(dsp.fit(drawn(rng, FIZZ[0], CYCLE + 0.5), n), 900, 4200))
        fibres = W.norm(lp(hp(dsp.fit(dsp.vari(bed(rng, FIBRES, CYCLE * 1.6), 4), n), 1200, 2), 4000, 4))
    else:
        hiss = W.norm(bp(synth.noise(CYCLE, rng)[:n], 900, 4200))
        fibres = W.norm(hp(synth.crackle(CYCLE, 60, rng, size=(0.0003, 0.002), hi=4000)[:n], 1200, 2))
    snaps = W.norm(lp(synth.crackle(CYCLE, 1.2, rng, size=(0.001, 0.004), hi=2500)[:n], 3500, 4))
    y = mix(at(hiss * f, -26), at(fibres * (0.5 + 0.5 * f), -27), at(snaps, -31))
    return loop(lp(y, TOP, 4), n, rng, 0.04, "car", band_shape(500, TOP, 4))


@recipe(L_, "sizzle", "smoulder", "Burned at a fire's edge: a coat smouldering on them, cloth and hair singeing and hissing",
        """From real recordings: the packs' real fizz drawn out (its loudest quarter second stretched) and held to 0.9-4.2 kHz (the smoke hissing out of a
        wool coat's weave), swelling and dying back a few times a second as it catches; a real cloth belt's crackle sped up
        and thinned over it (fibres and hair singeing), and now and then a sharper snap. Close and dry, on the body; under 5
        kHz, so it's never the car-fire tell (6-9 kHz). Held on whoever the fire's heat reaches, louder the hotter. An 8 s
        cycle.""", sources=FIZZ + [FIBRES], loop=True, takes=1, lufs=-22, seconds=CYCLE)
def sizzle_smoulder(rng, k):
    return smouldering(rng, True)


@recipe(L_, "sizzle", "singe", "Burned at a fire's edge, modelled: a hiss and a fine crackle of a coat singeing",
        """Modelled: noise held to 0.9-4.2 kHz for the smoke out of the weave, swelling and dying back as it catches, a fine
        crackle of tiny grains over it and a sharper snap now and then. Close, dry, under 5 kHz. An 8 s cycle.""",
        loop=True, takes=1, lufs=-22, seconds=CYCLE)
def sizzle_singe(rng, k):
    return smouldering(rng, False)
