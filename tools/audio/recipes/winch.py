"""The capstan winch heard wherever it stands (queue #204, note 468; spec D.2 "Capstan winch: two players hand-crank in
rhythm to drag cargo from distance. 2 mandatory. Desync stalls"; T43). A winch stands at the foundry, the mine head, the
military depot and the wreck yard: a drum on a frame with a crank at either end, hauling a sled of cargo 40 m in over the
ground to the track. Only the wreck yard's was heard (place-wreck.cargo-pull, its cargo dragged out of a wreck).

- The capstan turning (`capstan`): the drum going round at half a turn a second, its pawl clicking over the ratchet wheel
  (the packs' real ratchet, pitched down for a big iron one), the cranks creaking in their bushes once a turn, the rope
  winding on with the lap crossing and its strands creaking under the load, the frame groaning.
- The sled hauled in (`drag`): iron-shod skids grinding over the yard's gravel and earth in heaves as the cranks come round,
  stones crunching and kicked aside, the load rattling and shifting on the sled, the rope taut and humming.
- Out of rhythm (`stall`): D.2's desync. The drum snatched to a stop, the pawl catching hard on a tooth, the rope twanging
  taut and a crank jarring against the other's hands, the frame knocking.
- A sled in (`in`): hauled up to its stop by the track: the sled's nose bumping the stop timber, the load settling with a
  shift, the rope going slack with a slap.

Built from the packs' real iron, wood and stones (pitched to the part's size, choked where it's bolted); the rope and the
grinding are the kits' models.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter
from recipes.crew_kit import recipe, K, S
from recipes.jobs import strike, drum, rope, RATCHET
from recipes.world_bed import outdoors, circ_outdoors

L = "place-winch"
SR = dsp.SR
METAL_H, METAL_M = K("impactMetal_heavy"), K("impactMetal_medium")
WOOD_H = K("impactWood_heavy")
STONES = S("stones_01", "stones_02", "stones_03")
TURNS = 0.5            # the drum's turns a second at the cranks' pace (facilities.json winch.crank.revsPerSecond)
TEETH = 12             # the ratchet wheel's teeth
LOOP = 8.0             # four turns


def grind(m, rng, rough=2.2, loop=False):
    """Iron-shod skids on gravel and earth: rubbing rung through the sled's runners, with grit."""
    return W.friction(m, rng, [140, 310, 520, 860], rough=rough, grit=0.6, q=8, loop=loop)


@recipe(L, "capstan", "drum",
        "The capstan turning: the pawl clicking over its ratchet, the cranks creaking once a turn, the rope winding on",
        """At the winch while two crank it in rhythm: the drum going round half a turn a second, its pawl clicking over a
        twelve-tooth ratchet (the packs' real ratchet, pitched down for a big iron wheel), each crank creaking in its
        bush once a turn (modelled stick-slip through iron), the rope winding on with the lap crossing once a turn and its
        strands creaking under the load, and the frame groaning low. 8 s exact cycle, four turns.""",
        sources=[RATCHET] + METAL_M, loop=True, takes=1, lufs=-24, seconds=LOOP)
def capstan(rng, k):
    n = samples(LOOP)
    clicks = [strike(RATCHET, -rng.uniform(6, 8), lo=300, at=0.01, tau=0.02) for _ in range(3)]
    count = int(TURNS * TEETH * LOOP)
    pawl = W.place(n, [(j * LOOP / count + rng.uniform(-0.004, 0.004), clicks[j % 3], rng.uniform(0.6, 1.0)) for j in range(count)])
    creaks = []
    for j in range(int(TURNS * LOOP)):
        for h in range(2):
            cr = synth.creak(0.35, rng.uniform(25, 45), rng, body=[f * rng.uniform(0.9, 1.2) for f in synth.IRON[:5]], q=20, jitter=0.35)
            creaks.append(((j + 0.25 + 0.5 * h) / TURNS, cr * env([(0, 0), (0.08, 1), (0.35, 0)], 0.35)[:len(cr)], rng.uniform(0.4, 0.7)))
    groan = W.cyclic(lambda z: dsp.resonate(z, [f * 0.5 for f in synth.WOOD[:4]], q=12), lp(pnoise(n, rng), 300))
    y = norm(pawl) * 0.55 + norm(W.place(n, creaks)) * 0.3 + drum(n, rng, int(TURNS * LOOP)) * 0.25 \
        + rope(n, rng, [380, 610], strand=9.0, q=30) * 0.1 + norm(groan) * 0.12
    return seamless(circ_outdoors(cfilter(y, lambda f: 1 / (1 + (f / 6000) ** 4)), rng, 0.15))


@recipe(L, "drag", "skids",
        "The sled hauled in: its skids grinding over the gravel in heaves, stones crunching, the load rattling, the rope taut",
        """Out along the haul: a sled of cargo coming in over the yard's gravel and earth at a slow walk, its iron-shod
        skids grinding (modelled rubbing through its runners, with grit) and heaving forward as the cranks come round,
        stones crunching under it and kicked aside (the packs' stones), the load knocking and shifting on the sled (the
        packs' wood), the rope taut and humming ahead of it. 8 s exact cycle, a heave as each crank comes over (eight).""",
        sources=STONES + WOOD_H, loop=True, takes=2, lufs=-24, seconds=LOOP)
def drag(rng, k):
    n = samples(LOOP)
    t = np.arange(n) / SR
    heave = (0.6 + 0.4 * np.sin(np.pi * 2 * TURNS * t) ** 2).astype(np.float32)   # a heave as each crank comes over
    g = grind(n, rng, loop=True) * heave
    crunch = W.place(n, [(tt, norm(W.piece(rng, "brick", (-4, 2), tau=0.12)), rng.uniform(0.15, 0.5)) for tt in W.poisson(LOOP, 6, rng)])
    knocks = W.place(n, [(tt, strike(WOOD_H[j % 5], -rng.uniform(3, 6), lo=90, at=0.03, tau=0.04), rng.uniform(0.3, 0.6))
                         for j, tt in enumerate(W.poisson(LOOP, 1.2, rng))])
    hum = rope(n, rng, [220 + 40 * k, 330], strand=6.0, q=40)
    y = norm(g) * 0.6 + norm(crunch) * 0.3 + norm(knocks) * 0.25 + hum * 0.08
    return seamless(circ_outdoors(cfilter(y, lambda f: 1 / (1 + (f / 6000) ** 4)), rng, 0.15))


@recipe(L, "stall", "snatch",
        "Out of rhythm: the drum snatched to a stop, the pawl catching hard, the rope twanging, a crank jarring",
        """D.2's desync stall: one crank faster than the other, and the drum snatches to a stop. The pawl catches hard on
        a tooth (the real ratchet, a big hit), the rope twangs taut and shudders, a crank jars against the hands on the
        other (heavy iron, pitched down and choked), the frame knocks on its timbers. About a second.""",
        sources=[RATCHET] + METAL_H + WOOD_H, takes=3, lufs=-21)
def stall(rng, k):
    T = 1.2
    b = dsp.Bus(T + 0.8)
    b.at(0, strike(RATCHET, -rng.uniform(4, 6), lo=250, at=0.02, tau=0.03))
    b.at(0.01, strike(METAL_H[k % 5], -rng.uniform(5, 7), lo=90, at=0.04, tau=0.05) * 0.8)
    m = samples(0.7)
    tw = dsp.resonate(np.concatenate([rng.standard_normal(samples(0.004)), np.zeros(m - samples(0.004))]).astype(np.float32),
                      [rng.uniform(150, 190) * h for h in (1, 2, 3, 4)], q=60) * env([(0, 1), (0.7, 0)], 0.7, "exp")[:m]
    b.at(0.02, norm(lp(tw, 1500)) * 0.4)
    b.at(rng.uniform(0.08, 0.14), strike(WOOD_H[(k + 2) % 5], -rng.uniform(4, 6), lo=80, at=0.05, tau=0.05) * 0.5)
    cr = synth.creak(0.5, env([(0, 60), (0.5, 15)], 0.5), rng, body=[f * 0.5 for f in synth.WOOD[:4]], q=12, jitter=0.4)
    b.at(0.05, norm(cr * env([(0, 1), (0.5, 0)], 0.5)[:len(cr)]) * 0.25)
    return outdoors(lp(b.x, 7000, 2), rng, 0.15)


@recipe(L, "in", "stop",
        "A sled hauled in: its nose bumping the stop timber, the load settling with a shift, the rope going slack",
        """The sled hauled up to the track: its nose bumping the stop timber (the packs' heavy wood, pitched down), the
        load on it settling forward with a shift and a knock, a last grind of the skids, and the rope going slack with a
        slap on the ground. About 1.5 s.""", sources=WOOD_H + STONES, takes=2, lufs=-21)
def sled_in(rng, k):
    T = 1.6
    b = dsp.Bus(T + 0.8)
    m = samples(0.35)
    b.at(0, norm(grind(m, rng) * env([(0, 1), (0.35, 0)], 0.35)[:m]) * 0.4)
    b.at(0.3, strike(WOOD_H[k % 5], -rng.uniform(5, 7), lo=60, at=0.06, tau=0.06))
    b.at(0.3, norm(W.knock(rng, 80, 0.25)) * 0.4)
    b.at(rng.uniform(0.42, 0.5), strike(WOOD_H[(k + 3) % 5], -rng.uniform(2, 4), lo=100, at=0.03, tau=0.04) * 0.5)
    for _ in range(3):
        b.at(rng.uniform(0.35, 0.8), norm(W.piece(rng, "brick", (-3, 3), tau=0.1)) * rng.uniform(0.1, 0.25))
    m = samples(0.12)
    slap = lp(rng.standard_normal(m).astype(np.float32), 400, 2) * env([(0, 0), (0.003, 1), (0.12, 0)], 0.12, "exp")[:m]
    b.at(rng.uniform(0.75, 0.95), norm(slap) * 0.35)
    return outdoors(lp(b.x, 7000, 2), rng, 0.15)
