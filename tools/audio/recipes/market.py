"""The night market heard (queue #555, note 827; B2's #1111, note 825's "not yet: the market heard: the stallholders' talk,
the tea boiler (AU1's, if wanted)"). A row of stalls lit and open in the square, a stallholder behind each; it stood there
in silence but for the townsfolk's murmur.

- The tea stall (place-town.stall-tea, a loop): "tea from a boiler on a brazier, a penny a cup, the cups on strings".
- The clock stall (place-town.stall-clocks, a loop): "clocks and watches, all of them going, all set a quarter of an hour
  fast": a dozen of them ticking out of step.
- The knitting stall (place-town.stall-knitting, a loop): the stallholder knitting at her counter, the needles clicking.
- A stallholder calling (place-town.stall-call): a wordless cry through a mask now and then, or a hand bell rung, to
  bring the crew over.

Synthesised voices (the town's mumble and mask, recipes/town.py), the packs' real metal ticks and wood knocks for the
clocks, the needles and the cups. Quiet, close things: a stall's counter heard across the square's width at most.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, K
from recipes.crew_items import hit_of
from recipes.town import TICKS, TIN, WOOD_L, lapping, mask, seam, ticks

L = "place-town"
SR = dsp.SR
LOOP = 10.0


# ---- The tea stall --------------------------------------------------------------------------------------------------------

def simmer(rng, length, rate, lo, hi, rise=(0.1, 0.4)):
    return ck.norm(synth.bubbles(length, rate, lo, hi, rng, rise=rise))


@recipe(L, "stall-tea", "boiler", "The tea stall: a boiler on a brazier on the simmer, its lid ticking, a cup knocking on its string",
        """A tin tea boiler on a brazier at the end of the counter, kept just off the boil all night: the water simmering
        inside it (fine bubbling through the tin, a rumble under it), the lid lifting and ticking on the steam now and then
        (the packs' real tin ticks, quick and light), the brazier's coals under it hissing low, and a cup on its string
        knocking against the boiler's side (a real tin knock). A 10 s loop.""",
        sources=TIN + TICKS, loop=True, takes=1, lufs=-28, seconds=LOOP)
def stall_tea_boiler(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    inside = lp(simmer(rng, L_, 30, 400, 1800), 2200) * lapping(rng, n, 1.5, 0.4)
    rumble = lp(hp(W.norm(synth.noise(L_, rng, "brown")), 120), 400) * lapping(rng, n, 2.0, 0.5)
    coals = ck.norm(bp(synth.noise(L_, rng), 2000, 5000)) * 0.04
    # The lid: a run of quick light ticks as the steam lifts it, a few seconds apart.
    lid = []
    for t in W.poisson(L_ - 1, 0.35, rng):
        for j in range(int(rng.integers(3, 7))):
            lid.append((float(t) + j * rng.uniform(0.05, 0.09), ck.choke(hp(dsp.vari(hit_of(TIN[j % len(TIN)], j % 3, 0.1, -18), 7), 900), 0.003, 0.015), rng.uniform(-24, -16)))
    cup = [(float(t), ck.choke(dsp.vari(hit_of(TIN[0], 1, 0.15, -18), -3), 0.004, 0.06), -14) for t in [rng.uniform(1, 8)]]
    y = mix(inside * 0.5, W.norm(rumble) * 0.25, coals, ck.place(lid, L_ + 0.3) * 0.7, ck.place(cup, L_ + 0.3) * 0.6)
    return seam(lp(hp(y, 80), 9000), LOOP)


@recipe(L, "stall-tea", "kettle", "The tea stall: a big copper kettle near the boil, breathing steam, the cups on their strings",
        """A big copper kettle on the brazier instead, closer to the boil: a deeper, rounder simmer (its belly's rumble
        and slow big bubbles), the spout breathing a thin thread of steam that rises and sinks without ever whistling, and
        the cups on their strings knocking together now and then when someone takes one down. A 10 s loop.""",
        sources=TIN, loop=True, takes=1, lufs=-28, seconds=LOOP)
def stall_tea_kettle(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    belly = lp(simmer(rng, L_, 14, 200, 900, (0.15, 0.5)), 1400) * lapping(rng, n, 1.0, 0.5)
    rumble = lp(hp(W.norm(synth.noise(L_, rng, "brown")), 100), 300) * lapping(rng, n, 1.2, 0.5)
    steam = ck.norm(bp(synth.noise(L_, rng), 2200, 4200)) * np.clip(lapping(rng, n, 0.3, 0.9) - 0.4, 0, None) * 0.08
    cups = []
    for t in W.poisson(L_ - 1, 0.25, rng) or [rng.uniform(2, 7)]:
        for j in range(int(rng.integers(2, 4))):
            cups.append((float(t) + j * rng.uniform(0.08, 0.2), ck.choke(dsp.vari(hit_of(TIN[j % len(TIN)], j % 3, 0.15, -18), 2 + j), 0.004, 0.05), -12 - 3 * j))
    y = mix(belly * 0.55, W.norm(rumble) * 0.3, steam, ck.place(cups, L_ + 0.3) * 0.6)
    return seam(lp(hp(y, 70), 9000), LOOP)


# ---- The clock stall ------------------------------------------------------------------------------------------------------

def clock(rng, length, period, key, semis, db, hollow=None):
    """One clock or watch ticking at its own rate, its tick and tock a hair apart in pitch, from a random phase."""
    parts = []
    t = rng.uniform(0, period)
    i = 0
    while t < length:
        x = ck.norm(ck.choke(hp(dsp.vari(hit_of(key, i % 2, 0.08, -18), semis + (0.8 if i % 2 else 0)), 500), 0.003, 0.012))
        parts.append((t, x, db + rng.uniform(-1, 1)))
        if hollow is not None:
            parts.append((t, ck.hollow(rng, hollow, 0.1, q=8), db - 12))
        t += period * rng.uniform(0.995, 1.005)
        i += 1
    return ck.place(parts, length + 0.3)


@recipe(L, "stall-clocks", "tables", "The clock stall: a dozen clocks and watches going on its counter, every one out of step",
        """Clocks and watches, all going, none together: a couple of mantel clocks' slow wooden tick-tock (a second and a
        bit), a carriage clock's quicker tick, a tray of pocket watches' fine fast ticking (five a second, high and small),
        an alarm clock's tinny tick; each on its own beat and phase so the whole counter shimmers and never lines up (the
        packs' real small clicks and a light wood knock, pitched). A 10 s loop.""",
        sources=TICKS + WOOD_L, loop=True, takes=1, lufs=-30, seconds=LOOP)
def stall_clocks_tables(rng, k):
    L_ = LOOP + 0.6
    clocks = [(1.0, WOOD_L[0], 2, -6, [210, 330, 520]), (1.1, WOOD_L[1], 4, -8, [240, 380, 600]), (0.5, TICKS[0], 4, -10, None),
              (0.4, TICKS[-1], 6, -12, None), (0.25, TICKS[0], 10, -18, None), (0.2, TICKS[-1], 12, -20, None),
              (0.33, TICKS[-1], 7, -14, [900, 1400])]
    parts = [clock(rng, L_, p, key, s, db, hol) for p, key, s, db, hol in clocks]
    # Small things' ticks, dulled a little by the counter's cloth: a shimmer, not a hail.
    return seam(lp(mix(*parts), 6500), LOOP)


@recipe(L, "stall-clocks", "chime", "The clock stall: the clocks ticking out of step, and one of them striking its quarter early",
        """The same counter of clocks going out of step, fewer and closer (two mantel clocks, a carriage clock, a few
        watches), and somewhere in the loop one small clock striking a quarter: two soft notes of a little gong, a
        quarter of an hour before any other clock in the world. A 10 s loop.""",
        sources=TICKS + WOOD_L, loop=True, takes=1, lufs=-30, seconds=LOOP)
def stall_clocks_chime(rng, k):
    L_ = LOOP + 0.6
    clocks = [(1.0, WOOD_L[0], 2, -6, [210, 330, 520]), (0.5, TICKS[0], 4, -9, None), (0.25, TICKS[-1], 10, -15, None),
              (0.2, TICKS[0], 12, -17, None)]
    parts = [clock(rng, L_, p, key, s, db, hol) for p, key, s, db, hol in clocks]
    at = rng.uniform(3, 6)
    gong = mix(*[ck.place([(at + i * 0.9, little_bell(rng, f, 1.6), -8 - 2 * i)], L_ + 0.3) for i, f in enumerate([1318.5, 1046.5])])
    return seam(lp(mix(*parts, gong), 6500), LOOP)


def little_bell(rng, f0, length, ratios=(1.0, 2.76, 5.40, 8.93), decays=(1.2, 0.5, 0.25, 0.12)):
    """A small struck bell or gong: a handful of inharmonic partials, each dying away at its own rate, a strike's tick."""
    n = samples(length)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float32)
    for i, (r, d) in enumerate(zip(ratios, decays)):
        y += (np.sin(2 * np.pi * f0 * r * t + rng.uniform(0, 6.28)) * np.exp(-t / d) / (1 + i)).astype(np.float32)
    strike = ck.norm(hp(synth.noise(0.01, rng), 2000)) * env([(0, 1), (0.01, 0)], 0.01) * 0.2
    return ck.norm(mix(y, strike))


# ---- The knitting stall ---------------------------------------------------------------------------------------------------

def stitches(rng, length, keys, semis, rate=3.2, run=(6, 14)):
    """Needles clicking in runs (a row's stitches, a little uneven), each run a pause apart while the work's turned."""
    parts = []
    t = rng.uniform(0, 0.6)
    while t < length:
        for j in range(int(rng.integers(*run))):
            x = ck.norm(ck.choke(hp(dsp.vari(hit_of(keys[j % len(keys)], j % 3, 0.06, -18), semis + rng.uniform(-1, 1)), 700), 0.002, 0.01))
            parts.append((t, x, rng.uniform(-6, 0) - (3 if j % 2 else 0)))
            t += rng.uniform(0.85, 1.15) / rate
        t += rng.uniform(0.6, 1.8)
    return ck.place(parts, length + 0.3)


@recipe(L, "stall-knitting", "steel", "The knitting stall: steel needles clicking, a row at a time, the wool drawn off the ball",
        """The stallholder knitting at her counter between customers: steel needles clicking together in runs of a row's
        stitches, quick and a little uneven (the packs' real small metal clicks, pitched up and choked), a pause while she
        turns the work, and the wool hissing softly off the ball as she draws it. A 10 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-31, seconds=LOOP)
def stall_knitting_steel(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    clicks = stitches(rng, L_, TICKS, 9)
    wool = ck.norm(bp(synth.noise(L_, rng), 1500, 4000)) * np.clip(lapping(rng, n, 0.6, 0.9) - 0.5, 0, None) * 0.008
    return seam(lp(mix(clicks, wool), 8000), LOOP)


@recipe(L, "stall-knitting", "wood", "The knitting stall: wooden needles knocking softly, a row at a time",
        """The same, on wooden needles: softer, duller knocks in runs (the packs' real light wood knocks, pitched up and
        choked short), slower, the pause longer while she counts. A 10 s loop.""",
        sources=WOOD_L, loop=True, takes=1, lufs=-31, seconds=LOOP)
def stall_knitting_wood(rng, k):
    L_ = LOOP + 0.6
    clicks = stitches(rng, L_, WOOD_L, 12, rate=2.6, run=(5, 11))
    return seam(lp(clicks, 8000), LOOP)


# ---- A stallholder calling ------------------------------------------------------------------------------------------------

def cry(rng, f0, tract):
    """A hawker's cry with no words: a long open 'aa' held and falling a third, then a short 'o' lower still, through a
    mask's cup (recipes/town.py's mask): the shape of 'tea, a penny!' or 'fresh bread!' without the words."""
    L1, gap, L2 = rng.uniform(0.45, 0.7), 0.06, rng.uniform(0.3, 0.45)
    L_ = L1 + gap + L2
    pts = [(0, f0), (L1 * 0.7, f0 * 1.03), (L1, f0 * 0.85), (L1 + gap, f0 * 0.76), (L_, f0 * 0.66)]
    g = synth.glottis(env(pts, L_, "exp"), L_, rng, jitter=0.02, shimmer=0.2, oq=0.6)
    v = synth.tract(g, [(0, "a"), (L1 * 0.8, "a"), (L1 + gap, "o"), (L_, "o")], tract, breath=0.35, rng=rng)
    shape = env([(0, 0), (0.04, 1), (L1 * 0.85, 0.9), (L1, 0.1), (L1 + gap, 0.05), (L1 + gap + 0.03, 0.8), (L_ - 0.08, 0.6), (L_, 0)], L_)
    return mask(ck.norm(v * dsp.fit(shape, len(v))))


@recipe(L, "stall-call", "cry", "A stallholder calling their wares through a mask: a wordless sing-song cry",
        """A stallholder calling across the square to bring the crew over: a hawker's two-part cry with no words (a long
        'aa' falling a third, a short 'o' lower), a man's or a woman's voice (synth glottis and tract), through their
        breathing mask's cup, with the square's night air round it.""",
        takes=3, lufs=-24)
def stall_call_cry(rng, k):
    f0, tract = [(150, "man"), (230, "woman"), (128, "man")][k % 3]
    v = cry(rng, f0 * rng.uniform(0.95, 1.05), tract)
    return dsp.room(v, "night", wet=0.15, rng=rng)[:samples(len(v) / SR + 0.6)]


@recipe(L, "stall-call", "handbell", "A stallholder ringing a hand bell to call trade: two or three shakes",
        """A brass hand bell rung at a stall to call the crew over: two or three quick shakes (a small bell's inharmonic
        partials, its clapper striking on each side), the last left to ring out over the square.""",
        takes=3, lufs=-24)
def stall_call_handbell(rng, k):
    f0 = rng.uniform(1050, 1250)
    strokes = 2 + k % 2 + (1 if k == 2 else 0)
    parts = [(i * rng.uniform(0.16, 0.22), little_bell(rng, f0 * rng.uniform(0.995, 1.005), 1.4), -2 * i) for i in range(strokes)]
    y = ck.place(parts, strokes * 0.22 + 1.4)
    return dsp.room(y, "night", wet=0.15, rng=rng)[:len(y)]
