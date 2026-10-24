"""The big forests heard (note 1006; the director, 23 Oct 2026: "some unique sound design out there in the wilds for when
players stop in a forest to deal with an issue"): the deep woods round a train stood in them (spec A.3 tier 6, under every
tell). All of it modelled here (no recordings: CC0 by construction): the canopy's wind, trunks creaking, owls' throats as
the near-sine whistles they are, wood breaking, and the weight of something big walking in the duff.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter, croom

SR = dsp.SR


def far_off(x, rng, dist):
    """Put a sound `dist` m off in the trees: the air and the trunks eat the top, more of it the night's reverb."""
    y = lp(x, max(900.0, 9000.0 * 60.0 / dist))
    return W.space(norm(y) * (60.0 / dist) ** 0.5, rng, "night", wet=min(0.6, 0.15 + dist / 600))


# ---- The hush --------------------------------------------------------------------------------------------------------------

def hush_bed(rng, n):
    """Deep woods at night: the wind high up in the canopy (a soft swell far over your head, its top filtered by the
    needles), nothing of it down at the ground; a trunk creaking somewhere as the tops sway; needles and drips pattering
    down; the low weight of the land. Sparser and lower than the open country's bed: close, still, listening."""
    L = n / SR
    canopy = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(420)) / 1.0) ** 2))
    sway = np.clip(0.45 + 0.4 * slow(n, 0.05, rng) + 0.12 * slow(n, 0.3, rng), 0.05, None) ** 2
    hiss = W.cyclic(lambda z: lp(z, 3500), synth.rustle(L, 60, rng, f=(1800, 6000), ticks=0.05)) * sway
    low = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 60) ** 2) * (f / 20) / (1 + f / 20))
    creaks = []
    for t in W.poisson(L, 0.12, rng):
        cl = rng.uniform(1.2, 2.6)
        c = synth.creak(cl, env([(0, 5), (cl * 0.5, rng.uniform(12, 20)), (cl, 4)], cl), rng, body=synth.BARK, q=14, jitter=0.5)
        creaks.append((t, lp(c, 2200) * env([(0, 0), (cl * 0.3, 1), (cl, 0)], cl)[:len(c)], rng.uniform(0.3, 0.8)))
    drips = [(t, W.body(rng, rng.uniform(1800, 3500), W.PLATE, decay=0.006, length=0.03, count=4), rng.uniform(0.2, 0.7))
             for t in W.poisson(L, 0.8, rng)]
    y = (norm(canopy) * sway * 0.5 + norm(hiss) * 0.12 + norm(low) * 0.25 + norm(W.place(n, creaks)) * 0.1
         + norm(W.place(n, drips)) * 0.03)
    return croom(y, "night", 0.35, rng)


@recipe("world-woods", "hush", "canopy",
        "The deep woods round a stood train: wind high in the canopy, a trunk creaking as the tops sway, needles dripping",
        """Modelled: the wind far over your head in the spruce tops (a soft band round 400 Hz rising and falling over
        tens of seconds, none of it at the ground), a hiss of needles, now and then a trunk creaking as the tops sway
        (stick-slip through bark), drips and needles pattering down, the land's low weight. Quieter and closer than the
        open country's night: so whatever moves out there is heard. 20 s exact cycle.""", loop=True, takes=1, lufs=-31)
def hush(rng, k):
    return seamless(hush_bed(rng, samples(20.0)))


# ---- Far calls -------------------------------------------------------------------------------------------------------------

def hoot(rng, f0, length, glide=0.0, vib=0.0):
    """One owl's note: a near-sine whistle from the syrinx, a breathy onset, a little second harmonic, pitch gliding."""
    n = samples(length)
    t = np.arange(n) / SR
    f = f0 * (1 + glide * t / length) * (1 + vib * np.sin(2 * np.pi * 5.5 * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    y = np.sin(ph) + 0.12 * np.sin(2 * ph) + 0.04 * np.sin(3 * ph)
    y = y * env([(0, 0), (min(0.06, length * 0.3), 1), (length * 0.7, 0.85), (length, 0)], length)[:n]
    puff = bp(rng.standard_normal(n).astype(np.float32), f0 * 0.8, f0 * 3) * env([(0, 0), (0.03, 1), (0.12, 0)], length)[:n]
    return (y + 0.15 * puff).astype(np.float32)


@recipe("world-woods", "far", "calls",
        "A far call in the woods: a barred owl's 'who cooks for you', a great horned owl's hoots, an answer, a tree falling",
        """Owls as their throats make them (near-sine whistles with a breathy onset, gliding): a barred owl's eight-note
        'who cooks for you, who cooks for you-all', its last note falling away; a great horned owl's low five, 'hoo
        h'HOO hoo hoo'; two barred owls answering each other across the woods; a dead tree giving way far off and coming
        down through the others. 150-300 m off: quiet, dark, mostly reverb. Nothing a tell sounds like.""", takes=4, lufs=-30)
def far(rng, k):
    b = dsp.Bus(7.0)
    if k == 0 or k == 2:
        def barred(t0, f0):
            notes = [(0.0, 0.28, 0.0), (0.42, 0.22, 0.05), (0.75, 0.2, 0.0), (1.05, 0.55, -0.25)]
            for rep in range(2):
                for dt, ln, gl in notes:
                    b.at(t0 + rep * 1.9 + dt, hoot(rng, f0 * rng.uniform(0.98, 1.03), ln + (0.4 if rep == 1 and gl < 0 else 0), glide=gl, vib=0.01))
        barred(0.2, rng.uniform(380, 430))
        if k == 2:
            barred(3.9, rng.uniform(340, 370))
        d = 220
    elif k == 1:
        f0 = rng.uniform(250, 290)
        for dt, ln, g in [(0.0, 0.45, 0.8), (0.75, 0.15, 0.6), (0.95, 0.5, 1.0), (1.65, 0.35, 0.8), (2.2, 0.45, 0.7)]:
            b.at(0.3 + dt, hoot(rng, f0 * rng.uniform(0.97, 1.02), ln, glide=-0.04) * g)
        d = 260
    else:
        cl = 2.5
        c = synth.creak(cl, env([(0, 6), (1.8, 30), (cl, 60)], cl), rng, body=synth.BARK, q=10, jitter=0.6, grit=0.6)
        b.at(0, norm(c) * env([(0, 0), (1.0, 0.7), (cl, 1)], cl)[:len(c)])
        b.at(2.4, norm(W.splinter(0.5, rng, env([(0, 200), (0.5, 900)], 0.5))) * 0.9)
        b.at(2.6, norm(synth.rustle(1.4, 1200, rng, f=(800, 5000), ticks=0.8)) * env([(0, 0), (0.1, 1), (1.4, 0)], 1.4)[:samples(1.4)] * 0.7)
        b.at(3.5, norm(W.knock(rng, 38, 1.0)) * 1.0)
        d = 300
    return far_off(b.x, rng, d)


# ---- Branches --------------------------------------------------------------------------------------------------------------

@recipe("world-woods", "branch", "close",
        "A branch going somewhere close in the trees: a twig snapped, a limb cracking down, a cone dropping, needles shaken",
        """15-45 m off in the trees. A dry twig snapped underfoot of something you can't see (a sharp crack and the
        fibres after it); a dead limb letting go and cracking down through the branches to the duff; a cone dropping
        through the boughs; a bough shaken, its needles hissing, and settling; two twigs a beat apart. Short and real, the
        sort of thing that makes you turn round.""", takes=5, lufs=-29)
def branch(rng, k):
    b = dsp.Bus(3.5)
    if k == 0:
        b.at(0.1, norm(W.splinter(0.08, rng, env([(0, 400), (0.08, 1500)], 0.08), crack=1.2)))
        d = 20
    elif k == 1:
        b.at(0, norm(W.splinter(0.3, rng, env([(0, 100), (0.3, 900)], 0.3))) * 0.8)
        b.at(0.25, norm(synth.rustle(0.9, 900, rng, f=(1200, 7000), ticks=0.7)) * env([(0, 0), (0.08, 1), (0.9, 0)], 0.9)[:samples(0.9)] * 0.6)
        b.at(1.1, norm(W.knock(rng, 70, 0.25)) * 0.6)
        d = 35
    elif k == 2:
        t = 0.1
        for i in range(4):
            b.at(t, W.body(rng, rng.uniform(900, 2200), W.PLATE, decay=0.02, length=0.06, count=5) * (1 - i * 0.15))
            t += rng.uniform(0.08, 0.2)
        b.at(t + 0.1, norm(W.knock(rng, 110, 0.12)) * 0.4)
        d = 18
    elif k == 3:
        b.at(0, norm(synth.rustle(1.6, 1500, rng, f=(1500, 8000), ticks=0.2)) * env([(0, 0), (0.15, 1), (0.6, 0.5), (1.6, 0)], 1.6)[:samples(1.6)])
        d = 25
    else:
        for t in (0.1, 0.1 + rng.uniform(0.5, 0.9)):
            b.at(t, norm(W.splinter(0.06, rng, env([(0, 500), (0.06, 1500)], 0.06), crack=1.2)) * rng.uniform(0.6, 1.0))
        d = 30
    return far_off(b.x, rng, d)


# ---- Something big ---------------------------------------------------------------------------------------------------------

@recipe("world-woods", "heavy", "big",
        "Something big moving out in the trees: heavy steps in the duff, brush pushed aside, a long breath, then still",
        """50-80 m off. Not a creature's tell (no throat, no call): the weight of something big walking. Four to six
        heavy footfalls in the duff (a sub thump, the needles crushed under it), slow and uneven, a sapling bent and
        whipping back, brush pushed aside; it stops; a long low breath out; still. Four takes: walking past, stopping to
        listen, a stride breaking a dead limb, two steps and a long silence.""", takes=4, lufs=-30)
def heavy(rng, k):
    L = 7.0
    b = dsp.Bus(L)

    def step(t, g=1.0):
        b.at(t, norm(W.knock(rng, rng.uniform(32, 45), 0.5, drop=0.3)) * g)
        crush = synth.rustle(0.35, 1500, rng, f=(600, 4000), ticks=0.4) * env([(0, 1), (0.35, 0)], 0.35)[:samples(0.35)]
        b.at(t + 0.01, norm(crush) * 0.35 * g)

    steps = (5, 3, 4, 2)[k]
    t = 0.3
    for i in range(steps):
        step(t, rng.uniform(0.7, 1.0))
        t += rng.uniform(0.8, 1.3) * (1.4 if k == 1 and i == 1 else 1)
        if k == 0 and i == 2:
            b.at(t - 0.3, norm(synth.rustle(0.8, 700, rng, f=(900, 5000), ticks=0.5)) * env([(0, 0), (0.1, 1), (0.8, 0)], 0.8)[:samples(0.8)] * 0.5)
        if k == 2 and i == 2:
            b.at(t - 0.4, norm(W.splinter(0.25, rng, env([(0, 150), (0.25, 1000)], 0.25))) * 0.9)
    if k in (1, 3):
        bl = 2.2
        br = synth.breath(bl, "h", "man", rng=rng, shape=env([(0, 0), (0.4, 1), (bl, 0)], bl))
        b.at(t + 0.4, lp(dsp.shift(br, -9, formant=True), 700) * 0.5)
    return far_off(b.x, rng, (60, 70, 55, 80)[k])
