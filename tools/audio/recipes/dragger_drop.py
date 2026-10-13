"""The truss Dragger's drop heard (queue #180, note 444; D1's #171, note 435): a Dragger perched on a through-truss's top
chord scrapes (dragger-scrape, the tell), then drops 7 m onto the roof of the car passing under with someone on it,
straight into the grab; with nobody up there it falls onto the ballast behind the train and is gone. Neither fall made a
sound.

- The landing (cs-draggers.drop): its weight coming down on the car's tin roof from the chord. The whole sheet booms and
  flexes under it, a dull body thud under that, its limbs coming down a beat apart, and its claws biting and skating on
  the tin as it gathers itself toward whoever's up there. The grab follows (cs-draggers.grab).
- The fall (cs-draggers.fall): the same weight into the ballast behind the train, heard from the train as it draws away.
  A deep thud and a crunch of stones, stones scattering, its limbs scrabbling for a moment, then nothing.

Built from boarders.py's Dragger pieces (its claws on tin, the tin lip, the thud, the ballast crunch, joints popping), so
it's the same creature as its grab.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, env, mix, Bus
from recipes.boarders import (TINHIT, STONES, grain, thud, norm, settle, claws_tin, tin_lip, pops, ballast,
                              scrabble_legs, recede, plate_click)


def roof_boom(rng, weight=1.0):
    """A big weight on a car roof's tin: the packs' tin pitched well down for the mass, and the sheet's own modes flexing."""
    g = grain(rng, TINHIT[rng.integers(len(TINHIT))], 0.6, rng.uniform(-11, -7))
    x = np.zeros(samples(0.9), np.float32)
    x[:samples(0.006)] = rng.standard_normal(samples(0.006))
    flex = dsp.resonate(x, [f * rng.uniform(0.55, 0.7) for f in synth.TIN], q=24) * env([(0, 1), (0.9, 0)], 0.9, "exp")
    return mix(norm(lp(g, 5000), 0.1), norm(flex, 0.1) * 0.6, thud(rng, 65, 0.45, 0.25) * weight)


@recipe("cs-draggers", "drop", "slam",
        "The Dragger dropping off the truss onto the car's roof: its weight booming the tin, claws biting as it gathers",
        """Its whole weight coming down 7 m onto the tin roof: the sheet booms and flexes under it (the packs' tin hit
        pitched far down, the sheet's modes rung), a dull body thud under that, a second limb landing a beat after, and
        then its claws catch and skate on the tin and a few joints pop as it gathers itself toward the edge, about a
        second in all. The grab comes straight after it in the game.""",
        sources=TINHIT, takes=3)
def drop_slam(rng, take):
    b = Bus(1.4)
    b.at(0, roof_boom(rng, 1.3), 0)
    b.at(rng.uniform(0.05, 0.09), tin_lip(rng, 0.6), -5)
    b.at(rng.uniform(0.25, 0.35), claws_tin(rng, rng.uniform(0.25, 0.35)), -7)
    b.at(rng.uniform(0.3, 0.45), pops(rng, int(rng.integers(3, 5)), rng.uniform(0.12, 0.2)), -9)
    return settle(lp(b.x, 9000))


@recipe("cs-draggers", "drop", "limbs",
        "The Dragger landing on the roof on all its limbs at once: a rattle of hard tips on the tin, then its weight",
        """It lands spread on its limbs: their hard tips rattle down on the tin one after another in a fraction of a
        second (bone on sheet iron), then its body settles onto the roof with a dull boom and the sheet flexing, and
        its claws drag a little on the tin. Lighter than slam, and more of a creature.""",
        sources=TINHIT, takes=3)
def drop_limbs(rng, take):
    b = Bus(1.3)
    t = 0.0
    for _ in range(int(rng.integers(5, 8))):
        b.at(t, norm(tin_lip(rng, 0.15), 0.1) * rng.uniform(0.4, 0.9), rng.uniform(-10, -4))
        b.at(t, norm(plate_click(rng), 0.05), -8)
        t += rng.uniform(0.012, 0.035)
    b.at(t + 0.03, roof_boom(rng, 0.8), -2)
    b.at(t + 0.25, claws_tin(rng, rng.uniform(0.2, 0.3)), -9)
    return settle(lp(b.x, 9000))


def into_ballast(rng):
    """A big weight landing in the ballast: a deep thud, a heavy crunch of stones, and stones thrown and settling."""
    b = Bus(1.6)
    b.at(0, thud(rng, 55, 0.5, 0.3), 0)
    for _ in range(4):
        b.at(rng.uniform(0, 0.03), norm(grain(rng, STONES[rng.integers(len(STONES))], rng.uniform(0.15, 0.3),
                                              rng.uniform(-6, -2)), 0.1), rng.uniform(-4, 0))
    for _ in range(int(rng.integers(8, 14))):
        b.at(rng.uniform(0.08, 0.7), ballast(rng, rng.uniform(0.2, 0.6), 0.02), rng.uniform(-18, -9))
    b.at(rng.uniform(0.5, 0.7), scrabble_legs(rng, rng.uniform(0.4, 0.6), rate=55, weight=0.5), -8)
    return b.x


@recipe("cs-draggers", "fall", "ballast",
        "The Dragger dropping off the truss onto the ballast behind the train: a heavy thud and a crunch of stones",
        """With nobody on the roofs it drops anyway, onto the track behind the train: a deep thud and a heavy crunch of
        the ballast (the packs' stones pitched down, several at once), stones thrown and settling, its limbs scrabbling
        for a moment, and nothing more. Heard from the train as it draws away (darker and further as it goes), so it
        sits behind you.""",
        sources=STONES, takes=3)
def fall_ballast(rng, take):
    x = into_ballast(rng)
    return settle(recede(x, rng, 12, 40, drop=0.0, curve=1.5))
