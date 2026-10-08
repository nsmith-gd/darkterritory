"""The foundry's furnace heard (queue #161, note 425; C1's casting shed, note 420: "a furnace nobody tends"). The cupola
rises through the casting shed's roof, its door glowing, its 40 m stack behind; nobody charges it or taps it, but it
burns. Heard from the yard and loud inside the shed.

- The furnace (place-foundry.furnace): a banked cupola under its own draught. The stack pulls air through the coke bed,
  a deep, slow-breathing roar (pink noise through the stack's low column modes, lapping every few seconds as the draught
  gusts), the coke's fine crackle and ticks, the cupola's iron shell and the charging stage ticking as they take the heat,
  and a thin hiss at the glowing door. A loop.
- The charge slumping (place-foundry.slump): now and then the burnt-down charge settles in the shaft, a dull heavy
  rumble through the iron with a gout of sparks crackling up the stack and coke and slag knocking down after it.

Synthesised (there's no furnace in the packs: synth's fire and crackle, ck.hollow for the stack and the shell), with the
packs' real metal ticks for the hot iron.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of

L = "place-foundry"
SR = dsp.SR
TICKS = R("metalClick") + S("misc_01")
HEAVY = K("impactMetal_heavy")
STACK = [38, 61, 97, 140]          # a 40 m stack's low column: broad, never a tone
LOOP = 10.0


def tick(rng, semis, tau=0.025):
    x = dsp.vari(hit_of(TICKS[int(rng.integers(len(TICKS)))], int(rng.integers(3)), 0.1, -18), semis + rng.uniform(-1, 1))
    return ck.norm(ck.choke(hp(x, 500), 0.004, tau))


def roar(rng, length, lap=0.25, depth=0.45):
    """The draught up the stack: pink noise through the column's broad low modes and a warmer body, swelling as it gusts."""
    n = samples(length)
    # The roar's weight in the low mids where it's heard over the train, the stack's column only a little under it (a
    # furnace roars; a sub-heavy one just booms under the bed).
    base = hp(synth.noise(length, rng, "pink"), 90, 2)
    body = mix(bp(base, 180, 900) * 1.0, lp(base, 1600, 2) * 0.2, dsp.resonate(base, STACK, q=2.2) * 0.08)
    gust = np.clip(1 - depth + depth * (0.5 + 0.5 * W.norm(lp(rng.standard_normal(n).astype(np.float32), lap))), 0.2, 1.3)
    flutter = 1 + 0.08 * W.norm(lp(rng.standard_normal(n).astype(np.float32), 9))
    return ck.norm(body * gust * flutter)


@recipe(L, "furnace", "cupola", "The foundry's cupola banked and burning on its own: the stack's draught roaring, coke, hot iron ticking",
        """A cupola furnace nobody tends, burning under its own draught: the stack pulling air through the coke bed, a deep
        slow-breathing roar (pink noise through a 40 m stack's broad low column, gusting every few seconds), the coke's fine
        crackle and its odd pop (synth crackle), the furnace shell and the charging stage ticking as they take the heat (the
        packs' metal clicks, pitched down and choked), and a thin hiss at the glowing door. A 10 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-24, seconds=LOOP)
def furnace_cupola(rng, k):
    L_ = LOOP + 0.3
    n = samples(L_)
    draught = roar(rng, L_)
    coke = ck.norm(synth.crackle(L_, 45, rng, size=(0.0003, 0.002), hi=900))
    pops = ck.norm(synth.crackle(L_, 1.2, rng, size=(0.003, 0.01), hi=250))
    door = ck.norm(bp(synth.noise(L_, rng), 2500, 7000)) * (0.6 + 0.4 * W.norm(lp(rng.standard_normal(n).astype(np.float32), 0.5)))
    shell = ck.place([(float(t), tick(rng, rng.uniform(-9, -4)), rng.uniform(-20, -10)) for t in W.poisson(L_, 0.9, rng)]
                     + [(0.4, tick(rng, -6), -14)], L_ + 0.3)
    y = mix(draught, coke * 0.18, pops * 0.25, door * 0.05, shell * 0.7)
    return lp(y, 9000)[:samples(L_)]


@recipe(L, "slump", "charge", "The charge slumping in the cupola: a dull heavy rumble through the iron, sparks up the stack",
        """Now and then the burnt-down charge settles in the shaft: a dull, heavy rumble through the iron shell (the packs'
        heavy metal hit pitched far down and choked, the shell's low hollow under it), the draught catching with a whoomph,
        a gout of sparks crackling up the stack, and coke and slag knocking down after it.""",
        sources=HEAVY + TICKS, takes=3, lufs=-22)
def slump_charge(rng, k):
    thud = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(HEAVY[k % 5]), -14 + rng.uniform(-1, 1)))), 0.05, 0.12)
    shell = ck.hollow(rng, [rng.uniform(55, 70), rng.uniform(95, 115), rng.uniform(150, 180)], 0.9, q=4, hit=0.01)
    rumble = lp(ck.norm(synth.noise(1.1, rng, "brown")), 220) * env([(0, 0), (0.05, 1), (1.1, 0)], 1.1, "exp")
    whoomph = roar(rng, 1.4, lap=1.5, depth=0.2) * env([(0, 0), (0.15, 1), (1.4, 0)], 1.4)
    sparks = ck.norm(synth.crackle(1.2, env([(0, 260), (1.2, 10)], 1.2, "exp"), rng, size=(0.0002, 0.0012), hi=1500))
    knocks = ck.place([(0.25 + 0.6 * float(t), tick(rng, rng.uniform(-14, -8), 0.04), rng.uniform(-14, -6))
                       for t in np.sort(rng.random(int(rng.integers(3, 6))))], 1.2)
    return ck.place([(0, thud, 0), (0, ck.norm(shell), -6), (0.01, rumble, -4), (0.05, whoomph, -8), (0.08, sparks, -12),
                     (0, knocks, -6)])
