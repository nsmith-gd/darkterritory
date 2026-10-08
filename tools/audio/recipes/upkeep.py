"""The upkeep heard (queue #95, note 358): the jobs the train makes as it runs (orchestrator.md §5.1; D1's notes 331 and
346), the faults' own sounds and the hands' work on them.

- The hot box (state-hotbox, swapped in for D1's `hotbox` synth): a car's axle box running dry. A plain journal bearing
  with no oil is steel dragged on brass under the car's weight, so it squeals: a held, inharmonic ring (real struck metal
  frozen into a ring, as the bend's flange sing is) at 1.3-2.4 kHz, catching and letting go once a wheel turn (the
  waver the synth's tremolo was), with the grind of it under. As it heats the game lifts its pitch and level ("heat").
  Then it smokes: the grease in the box cooking off, crackling and spitting, and a thin hiss.
- The lamp guttering (state-gutter, for D1's `lamp-gutter`): the wick spitting as it runs short of oil, quick uneven
  cracks in clusters, and the flame fluttering low and tearing (the junction lamp's flame, recipes/horrors.py, turned
  down and torn). The game raises both with "gutter".
- The hands (crew-upkeep): a grease gun worked at a hot box, its lever's strokes and the grease spitting on hot iron,
  held while it's at it; the box greased, the last of it hissing off; a guttering lamp trimmed, the wick's brass screw
  clicking up and the flame catching steady.

All CC0: the packs' real metal (bell, plate and light metal hits for the squeal's ring and the gun's lever), their glass
and tin for the lamp; the rest built (the crackle, the hiss, the flame, the grease).
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, K
from recipes.horrors import flame
from recipes.stress import held_ring, stick_slip, BELLS, PLATES

SR = dsp.SR
METAL_L = K("impactMetal_light")
GLASS = K("impactGlass_light")
WHEEL_TURN = 3.2          # a 0.9 m wheel at about 9 m/s: the bearing catches once a turn


def norm(x):
    return W.norm(x)


# ---- The hot box ------------------------------------------------------------------------------------------------------------

@recipe("state-hotbox", "squeal", "journal",
        "A car's axle box running dry: the bearing's dry squeal, catching once a wheel turn",
        """Steel dragged on dry brass under the car's weight: real struck bell metal frozen into a held ring at 1.45 kHz with
        a second partial a fifth over it, catching and letting go once a wheel turn (3.2 times a second, wandering), the
        contact fluttering as it slips, and the grind of the journal under it. Steady by itself: the game lifts its pitch
        and level as the box heats ("heat"). 6 s exact cycle.""",
        sources=BELLS[:2], loop=True, takes=1, lufs=-24)
def journal(rng, k):
    n = samples(6.0)
    a = dsp.wow(held_ring(BELLS[0], 1450, n, rng), depth_cents=22, rate=0.6, rng=rng)
    b = dsp.wow(held_ring(BELLS[1], 2170, n, rng), depth_cents=30, rate=0.8, rng=rng)
    turn = WHEEL_TURN * (1 + 0.04 * W.slow(n, 0.3, rng))
    phase = np.cumsum(turn) / SR
    phase *= max(1, round(phase[-1])) / phase[-1]          # a whole number of turns: it loops
    catch = (0.35 + 0.65 * (0.5 + 0.5 * np.cos(2 * np.pi * phase)) ** 3).astype(np.float32)
    tone = (a + 0.45 * b) * catch * stick_slip(n, rng, 23, 0.4, 0.8)
    tone = dsp.band(tone, 1000, 6000, order=2)
    grind = W.friction(n, rng, [520, 1180, 1960, 2830], rough=1.4, grit=0.6, q=18, loop=True)
    grind = W.cfilter(grind, lambda f: (f / 700) ** 2 / (1 + (f / 700) ** 2) / (1 + (f / 7000) ** 2))
    y = norm(tone) * 0.8 + norm(grind) * catch * 0.2
    return W.seamless(W.croom(y, "night", 0.1, rng))


@recipe("state-hotbox", "squeal", "screech",
        "A car's axle box running dry: a thinner, harsher screech, the bearing seizing and slipping",
        """Thin plate metal frozen into a ring at 1.8 kHz, driven hard (saturated) so it screeches, chattering in short
        bursts as the journal seizes and slips once a wheel turn, and the dry grind under it. Harsher than the journal
        squeal: the box nearer to catching. The game lifts its pitch and level as it heats. 6 s exact cycle.""",
        sources=PLATES[:2], loop=True, takes=1, lufs=-24)
def screech(rng, k):
    n = samples(6.0)
    a = dsp.wow(held_ring(PLATES[0], 1800, n, rng), depth_cents=30, rate=0.9, rng=rng)
    b = dsp.wow(held_ring(PLATES[1], 2650, n, rng), depth_cents=35, rate=1.1, rng=rng)
    turn = WHEEL_TURN * (1 + 0.05 * W.slow(n, 0.4, rng))
    phase = np.cumsum(turn) / SR
    phase *= max(1, round(phase[-1])) / phase[-1]
    catch = np.clip(np.sin(2 * np.pi * phase) * 1.4, 0, 1).astype(np.float32) ** 0.7
    tone = dsp.saturate(norm(a + 0.6 * b) * (0.25 + 0.75 * catch) * stick_slip(n, rng, 31, 0.55, 1.2), 7)
    tone = dsp.band(tone, 1100, 7000, order=2)
    grind = W.friction(n, rng, [610, 1330, 2240], rough=2.0, grit=0.8, q=14, loop=True)
    grind = W.cfilter(grind, lambda f: (f / 800) ** 2 / (1 + (f / 800) ** 2))
    y = norm(tone) * 0.75 + norm(grind) * 0.25
    return W.seamless(W.croom(y, "night", 0.1, rng))


def cook(n, rng, rate, hiss):
    """Grease cooking on hot iron: crackles and spits in uneven clusters, a thin hiss under them (all periodic over n)."""
    length = n / SR
    out = np.zeros(n, np.float32)
    for t in W.poisson(length, rate, rng):
        size = rng.uniform(0.0004, 0.003)
        c = lp(synth.crackle(rng.uniform(0.02, 0.09), rng.uniform(80, 260), rng, size=(size * 0.3, size), hi=1200), 6000)
        out += W.place(n, [(t, c, rng.uniform(0.3, 1.0))])
    h = W.pnoise(n, rng, lambda f: ((f / 1200) ** 2 / (1 + (f / 1200) ** 2)) / (1 + (f / 3800) ** 2))
    h *= np.clip(0.6 + 0.4 * W.slow(n, 1.5, rng), 0.2, 1)
    return norm(out) + norm(h) * hiss


@recipe("state-hotbox", "smoke", "cooking",
        "The box smoking: the grease in it cooking off, crackling and spitting, a thin hiss",
        """Hot grease crackling and spitting on the box in uneven clusters, a thin steady hiss of it smoking, and now and
        then a fatter spit. Under the squeal from halfway hot ("heat"). 6 s exact cycle.""",
        loop=True, takes=1, lufs=-26)
def cooking(rng, k):
    n = samples(6.0)
    y = cook(n, rng, 9, 0.35)
    fat = [(t, synth.crackle(0.06, 400, rng, size=(0.002, 0.005), hi=600), rng.uniform(0.4, 0.8)) for t in W.poisson(6.0, 1.2, rng)]
    y = norm(y) + norm(W.place(n, fat)) * 0.4
    return W.seamless(W.croom(hp(y, 300), "night", 0.08, rng))


# ---- The lamp guttering -----------------------------------------------------------------------------------------------------

@recipe("state-gutter", "sputter", "wick",
        "A car's lamp guttering: the wick spitting as it runs short of oil, quick and uneven",
        """The wick spitting in clusters (a burst of tiny cracks, then nothing, then two), each crack a hard little tick of
        burning oil, with the glass chimney ticking back now and then. The game raises it with "gutter" (0 just started,
        1 about to go out). Small and near: a lamp in a car. 6 s exact cycle.""",
        sources=GLASS, loop=True, takes=1, lufs=-28)
def wick(rng, k):
    n = samples(6.0)
    events = []
    for t in W.poisson(6.0, 1.6, rng):                    # clusters
        for j in range(int(rng.integers(2, 7))):
            c = synth.crackle(0.03, 600, rng, size=(0.0002, 0.0012), hi=2500)
            events.append((t + j * rng.uniform(0.03, 0.09), c, rng.uniform(0.3, 1.0)))
    for t in W.poisson(6.0, 0.4, rng):                    # the chimney ticking as it heats and cools
        g = ck.norm(hp(ck.align(ck.get(GLASS[int(rng.integers(len(GLASS)))]))[:samples(0.06)], 2000))
        events.append((t, g * 0.25, 1.0))
    y = W.place(n, events)
    return W.seamless(W.croom(bp(y, 700, 9000), "car", 0.1, rng))


@recipe("state-gutter", "flutter", "flame",
        "The lamp's flame fluttering low and tearing as it starves",
        """An oil lamp's flame (the junction lamp's, recipes/horrors.py) turned down low and starved: it laps and tears in
        uneven gusts at 4-9 Hz, dips nearly out and catches again. Under the wick's spitting; the game raises it with
        "gutter". 6 s exact cycle.""",
        loop=True, takes=1, lufs=-30)
def starved(rng, k):
    n = samples(6.0)
    level = np.clip(0.45 + 0.35 * W.slow(n, 0.9, rng), 0.1, 0.9)
    y = flame(rng, 6.0, level)[:n]
    tear = np.clip(0.55 + 0.6 * W.slow(n, 7, rng), 0.05, 1.2)
    return W.seamless(W.croom(norm(y) * tear, "car", 0.12, rng))


# ---- The hands ---------------------------------------------------------------------------------------------------------------

def lever(rng, k):
    """The grease gun's lever: a light metal click-clack, the pump's two ends."""
    a = ck.norm(ck.choke(ck.align(ck.get(METAL_L[(k + int(rng.integers(5))) % 5])), 0.006, 0.02))
    return ck.norm(hp(dsp.vari(a, rng.uniform(4, 7)), 900))


@recipe("crew-upkeep", "grease", "gun",
        "Greasing a hot axle box: the grease gun's strokes, the grease spitting on hot iron",
        """A lever grease gun worked at the box: a stroke a second or so (the lever's click-clack, the plunger's wet
        push), the grease squirting into the box and spitting where it meets the hot iron, a rag's wipe now and then.
        Held while a crewmate's at it (3 s); the box goes quiet as it's done. 4 s exact cycle.""",
        sources=METAL_L, loop=True, takes=1, lufs=-22)
def gun(rng, k):
    n = samples(4.0)
    events = []
    for i, t in enumerate(np.arange(0.15, 4.0, 1.05) + rng.uniform(-0.05, 0.05, 4)):
        events += [(t, lever(rng, i), 0.7), (t + rng.uniform(0.18, 0.24), lever(rng, i + 2), 0.5)]
        push = ck.slurp(rng, rng.uniform(0.18, 0.26), 500, 2400)
        events.append((t + 0.05, push * 0.28, 1.0))
    sizzle = cook(n, rng, 14, 0.5)
    rag = [(t, W.norm(hp(synth.rustle(0.35, 900, rng, f=(1500, 6000), ticks=0.2), 800)) * 0.25, 1.0) for t in W.poisson(4.0, 0.5, rng)]
    y = norm(W.place(n, events)) + norm(sizzle) * 0.45 + W.place(n, rag)
    return W.seamless(W.croom(y, "night", 0.08, rng))


@recipe("crew-upkeep", "greased", "hiss",
        "The box greased: the last of the grease hissing off as the iron cools",
        """The grease gun's last stroke and the nozzle pulled off, and the hot iron hissing as the fresh grease takes the
        heat out of it, dying away over a second.""",
        sources=METAL_L, takes=3, lufs=-23)
def hissed(rng, k):
    L = 1.4
    n = samples(L)
    h = cook(n, rng, 20, 0.8) * env([(0, 0), (0.03, 1), (0.4, 0.6), (L, 0)], L)[:n]
    y = mix(lever(rng, k) * 0.6, np.concatenate([np.zeros(samples(0.08), np.float32), norm(h) * 0.7]))
    return dsp.room(y, "night", wet=0.08, rng=rng)


@recipe("crew-upkeep", "trim", "wick",
        "A guttering lamp trimmed: the wick's brass screw clicked up, the flame catching steady",
        """The lamp's wick wound up a few clicks on its little brass screw (small dry ticks of brass on brass), the glass
        chimney touched (the packs' light glass), and the flame catching and steadying with a soft breath as the fresh
        wick takes the oil.""",
        sources=GLASS, takes=3, lufs=-24)
def trimmed(rng, k):
    events = []
    t = 0.0
    for i in range(int(rng.integers(3, 6))):
        c = ck.norm(mix(ck.tick(rng, rng.uniform(3200, 4800), q=9, length=0.014), ck.tick(rng, rng.uniform(1400, 1900), q=6, length=0.01) * 0.4))
        events.append((t, c * rng.uniform(0.6, 0.9), 0))
        t += rng.uniform(0.07, 0.12)
    g = ck.norm(hp(ck.align(ck.get(GLASS[k % len(GLASS)]))[:samples(0.08)], 2000)) * 0.12
    events.append((t + 0.05, g, 0))
    catch = hp(flame(rng, 0.9, [0.2, 1.0, 0.7]), 200) * env([(0, 0), (0.08, 1), (0.9, 0)], 0.9)
    events.append((t + 0.12, ck.norm(catch) * 0.3, 0))
    return dsp.room(ck.place(events), "car", wet=0.12, rng=rng)
