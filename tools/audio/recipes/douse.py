"""The extinguisher heard on the fire (queue #205, note 469; the director, 8 Oct 2026: "Holding fire extinguisher on fire
still doesnt feel like its doing anything"; D1's #203, note 467: the cell aimed at is out in a second). The soda-acid
extinguisher's own jet is crew_items.py's; the car fire's crackle is tells.py's. These are where the two meet:

- On the fire (`on-fire`): the jet striking burning boards. The water flashes to steam where it lands, a dense sizzle of
  tiny bursts and a rush of hiss over it; the flames' roar beaten down and guttering under the stream; water spitting off
  the hot wood. Held while the cell the jet's on is alight.
- A cell out (`cell-out`): the flames there collapsing. A soft whump as the roar's cut, a hissing gasp of steam that swells
  and dies in under a second, a last sizzle or two and an ember's tick. The answer to every second of spray on a cell.
- The fire out (`fire-out`): the last of it. A long sigh of steam off the boards, water dripping and running off the char,
  the boards ticking and creaking as they cool, then nothing.

The sizzle and the steam are modelled (the packs have no water on fire): many tiny steam bursts (a crackle of very short
noise grains, dense), a hiss shaped low and broad, so it reads as steam off wood rather than a spray. Wood's creak and the
drips are the kits'.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter
from recipes.crew_kit import recipe, K

L = "crew-extinguisher"
SR = dsp.SR
WOOD_L = K("impactWood_light")


def sizzle(n, rng, rate, loop=False):
    """Water flashing to steam on hot wood: a dense crackle of tiny bursts (each a few ms of noise), loud ones rare,
    centred in the low kilohertz so it's a fry, not a hiss."""
    out = np.zeros(n, np.float32)
    rc = W.curve(rate, n)
    t, length = 0.0, n / SR
    while t < length:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(0.001, 0.006)) + 2
        g = rng.standard_normal(k).astype(np.float32) * np.exp(-np.linspace(0, 5, k)).astype(np.float32)
        amp = min(1.0, 0.05 * rng.pareto(1.8) + 0.05)
        if loop:
            idx = (a + np.arange(k)) % n
            np.add.at(out, idx, g * amp)
        else:
            m = min(k, n - a)
            out[a:a + m] += g[:m] * amp
    return W.cyclic(lambda z: bp(z, 700, 5000, 2), out) if loop else bp(out, 700, 5000, 2)


def steam(n, rng, loop=True):
    """Steam rushing off the boards: broad noise centred near 1.5 kHz, rolling off above 5, its pressure surging."""
    x = pnoise(n, rng) if loop else rng.standard_normal(n).astype(np.float32)
    shape = lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(1500)) / 1.1) ** 2) / (1 + (f / 5000) ** 3)
    return cfilter(x, shape)


@recipe(L, "on-fire", "steam",
        "The jet on the flames: the water flashing to steam, a sizzle and a rush of hiss, the fire's roar beaten down",
        """Where the jet strikes burning boards: the water flashing to steam (a dense sizzle of tiny bursts, a fry in the
        low kilohertz), a rush of steam off the wood surging with the stream, the flames' roar beaten down and guttering
        under it (low and fluttering), and water spitting off the hot boards. Held while the cell the jet's on is alight;
        modelled, the packs have no water on fire. 6 s exact cycle.""", loop=True, takes=2, lufs=-22, seconds=6.0)
def on_fire(rng, k):
    T = 6.0
    n = samples(T)
    t = np.arange(n) / SR
    sz = sizzle(n, rng, 900 + 300 * slow(n, 1.5, rng), loop=True)
    st = steam(n, rng) * np.clip(0.75 + 0.3 * slow(n, 1.2, rng), 0.3, 1.2).astype(np.float32)
    # The roar under the stream, guttering: low noise, its level knocked about fast.
    roar = cfilter(pnoise(n, rng), lambda f: (f / 60) / (1 + (f / 60) ** 2) / (1 + (f / 350) ** 2))
    gutter = np.clip(0.5 + 0.5 * slow(n, 7, rng), 0.05, 1.2).astype(np.float32)
    spits = W.place(n, [(tt, synth.click(rng.uniform(900, 2200), q=3, length=0.02, rng=rng), rng.uniform(0.3, 1.0))
                        for tt in W.poisson(T, 9, rng)])
    y = norm(sz) * (0.55 + 0.1 * k) + norm(st) * 0.45 + norm(roar * gutter) * 0.4 + norm(spits) * 0.12
    return seamless(W.croom(W.cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 4)), "car", 0.12, rng))


@recipe(L, "cell-out", "gasp",
        "A patch of the fire put out: the flames collapsing with a soft whump and a hissing gasp of steam, an ember ticking",
        """The flames where the jet is give out all at once: a soft whump as their roar's cut (low noise snatched away in
        a tenth of a second), a hissing gasp of steam that swells and dies inside a second, a last sizzle or two, and an
        ember ticking in the wet char. Short and plain, so a second of spray on a cell has an answer.""",
        sources=WOOD_L, takes=3, lufs=-20)
def cell_out(rng, k):
    T = 1.3
    n = samples(T)
    b = dsp.Bus(T + 0.6)
    m = samples(0.18)
    whump = lp(rng.standard_normal(m).astype(np.float32), 220, 2) * env([(0, 0.4), (0.02, 1), (0.18, 0)], 0.18, "exp")[:m]
    b.at(0, norm(whump) * 0.7)
    rise, top = rng.uniform(0.04, 0.07), rng.uniform(0.5, 0.8)
    gasp = steam(n, rng, loop=False) * env([(0, 0), (rise, 1), (top, 0.25), (T, 0)], T)[:n]
    b.at(0.03, norm(gasp) * 0.75)
    sz = sizzle(n, rng, env([(0, 1500), (0.3, 500), (0.8, 60), (T, 0)], T))
    b.at(0.02, norm(sz) * 0.35)
    tick = hp(ck_hit(rng, k), 600)
    b.at(rng.uniform(0.7, 1.0), tick * 0.25)
    return W.space(lp(b.x, 8000, 2), rng, "car", wet=0.12)


def ck_hit(rng, k):
    """An ember ticking in the char: the packs' light wood tap, pitched up and choked to a tick."""
    from recipes import crew_kit as ck
    x = ck.get(WOOD_L[k % 5])
    h = ck.hits(x, floor_db=-14, gap=0.03)
    a = h[0][0] if h else 0
    x = dsp.vari(x[a:a + samples(0.12)], rng.uniform(5, 8))
    return norm(ck.choke(x, 0.006, 0.01))


@recipe(L, "fire-out", "cooling",
        "The fire out: a long sigh of steam off the boards, water dripping off the char, the boards ticking as they cool",
        """The last of the fire knocked down: a long sigh of steam off the wet boards (swelling, then thinning over a
        couple of seconds), a few last sizzles, water running and dripping off the char onto the floor, and the car's
        boards ticking and creaking as they cool. Then nothing. About 4 s.""", sources=WOOD_L, takes=2, lufs=-22)
def fire_out(rng, k):
    T = 4.2
    n = samples(T)
    b = dsp.Bus(T + 0.8)
    sigh = steam(n, rng, loop=False) * env([(0, 0), (0.15, 1), (1.2, 0.6), (2.8, 0.15), (T, 0)], T)[:n]
    b.at(0, norm(sigh) * 0.6)
    sz = sizzle(n, rng, env([(0, 600), (0.8, 200), (2.0, 30), (T, 0)], T))
    b.at(0, norm(sz) * 0.25)
    for tt in sorted(rng.uniform(0.6, T - 0.2, 7)):
        b.at(tt, norm(W.drip(rng, "water" if rng.random() < 0.5 else "stone", rng.uniform(0.6, 1.2))) * rng.uniform(0.15, 0.35))
    for j, tt in enumerate(sorted(rng.uniform(1.2, T - 0.3, 4))):
        b.at(tt, ck_hit(rng, k + j) * rng.uniform(0.12, 0.25))
    cr = synth.creak(0.6, 25, rng, body=[f * 0.8 for f in synth.WOOD[:4]], q=14, jitter=0.4)
    b.at(rng.uniform(2.0, 2.8), norm(cr * env([(0, 0), (0.1, 1), (0.6, 0)], 0.6)[:len(cr)]) * 0.18)
    return W.space(lp(b.x, 8000, 2), rng, "car", wet=0.15)
