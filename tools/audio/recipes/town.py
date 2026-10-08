"""The walled town heard (queue #151, note 415): the departure town's fires, its lived-in houses and its people, which
burnt, ticked and stood about in silence (the town was the fortress's loop and the night).

- Fires (place-town.fire): the square's fire barrels and braziers. An oil drum's wood fire, flames lapping out of its
  mouth, sap popping, the drum's thin steel ticking as it heats; a brazier's iron basket of coals, a hotter, quieter bed
  with more hiss than flame. Loops, the nearest few held where they burn.
- The range (place-town.range): a lived-in house's kitchen range, the fire shut in cast iron with its draught murmuring
  through the door's vents, the iron ticking, a kettle on the hob just on the simmer.
- The custom's things in a house: a clock ticking (a longcase's slow wooden tick-tock) and a wireless left on (static
  and a far station fading in and out: no words to make out).
- The townsfolk (place-town.murmur, .cough): the director's town is walled in against foul air, and its people talk low
  through their breathing gear (B2's #389 gives them respirators, oxygen cups, rebreathers and wraps): wordless murmur on
  shut vowels, three or four voices, muffled by a mask's cup; and now and then one coughs into it.

Synthesised where the packs have no such thing (fire, a voice, a cough, static: synth's fire and crackle, glottis and
tract, world_voice's static); the packs' real metal ticks and wood knocks for the iron and the clock.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of
from recipes.world_voice import static as radio_static

L = "place-town"
SR = dsp.SR
TICKS = R("metalClick") + S("misc_01")
TIN = K("impactTin_medium")
WOOD_L = K("impactWood_light")
LOOP = 8.0


def seam(y, length):
    """A loop's cycle and the 0.3 s after it: build.py crossfades that tail into the head (dsp.loop_seam), so the loop
    comes out exactly `length` long and a beat keeps its time across the seam."""
    return y[:samples(length + 0.3)]


def lapping(rng, n, rate=3.0, depth=0.5):
    """A flame's slow swell and sag."""
    return np.clip(1 - depth + depth * W.norm(lp(rng.standard_normal(n).astype(np.float32), rate)) * 0.5 + depth * 0.5, 0.05, 1.5)


def tick(rng, key, semis, tau=0.02):
    """One small real metal tick (hot iron or tin creaking as it heats), pitched and choked."""
    x = dsp.vari(hit_of(key, int(rng.integers(3)), 0.12, -18), semis + rng.uniform(-1, 1))
    return ck.norm(ck.choke(hp(x, 600), 0.004, tau))


def ticks(rng, length, keys, rate, semis, db=(-22, -12)):
    """Heating metal ticking now and then: Poisson in time."""
    parts = []
    for t in W.poisson(length, rate, rng):
        parts.append((float(t), tick(rng, keys[int(rng.integers(len(keys)))], semis), rng.uniform(*db)))
    return ck.place(parts, length + 0.3) if parts else np.zeros(samples(length + 0.3), np.float32)


# ---- Fires ----------------------------------------------------------------------------------------------------------------

@recipe(L, "fire", "drum", "A fire barrel: wood burning in an oil drum, flames lapping out of its mouth, the drum ticking",
        """An oil drum with a wood fire in it, the town's warmth on its square: the flames' lapping roar out of the drum's
        mouth (brown noise lapping at a few hertz, a little of the drum's hollow ring under it), sap cracking and popping
        (synth crackle, a few big pops), and the drum's thin steel ticking now and then as it heats (the packs' real tin
        and metal ticks, pitched up and choked). An 8 s loop.""",
        sources=TIN + TICKS, loop=True, takes=1, lufs=-24, seconds=LOOP)
def fire_drum(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    f = synth.fire(L_, 0.55, rng)
    # The drum's hollow under the flames: broad (a low q), lapping with them, so it colours the roar and never hums.
    drum = ck.norm(dsp.resonate(hp(synth.noise(L_, rng, "pink"), 80), [180, 410, 690], q=2.5)) * lapping(rng, n, 2.5, 0.8)
    y = mix(f, drum * 0.05, ticks(rng, L_, TIN + TICKS, 0.5, 5) * 0.8)
    return seam(lp(y, 9000), LOOP)


@recipe(L, "fire", "coals", "A brazier: an iron basket of coals, glowing more than flaming, hissing and ticking",
        """A brazier's iron basket of coal on its legs: a bed hotter than it is bright, so less roar and more of the coals'
        thin hiss and fine crackle (synth fire at a low flame, its hiss up), a lump settling now and then with a soft
        clink against the bars (the packs' metal clicks, choked low), the basket's iron ticking. An 8 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-26, seconds=LOOP)
def fire_coals(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    f = synth.fire(L_, 0.3, rng)
    hiss = ck.norm(bp(synth.noise(L_, rng), 2000, 6000)) * lapping(rng, n, 1.5, 0.4)
    fine = ck.norm(synth.crackle(L_, 60, rng, size=(0.0004, 0.002), hi=1000))
    # A lump settling against the bars now and then (at least once a cycle).
    settle = ck.place([(float(t), tick(rng, TICKS[int(rng.integers(len(TICKS)))], -6, 0.04), -10)
                       for t in [rng.uniform(0.5, 3.5)] + W.poisson(L_, 0.3, rng)], L_ + 0.3)
    glow = lp(ck.norm(synth.noise(L_, rng, "brown")), 300) * lapping(rng, n, 1.0, 0.3)
    y = mix(f, ck.norm(glow) * 0.35, hiss * 0.05, fine * 0.2, settle * 0.6, ticks(rng, L_, TICKS, 0.4, 3) * 0.6)
    return seam(lp(y, 7000), LOOP)


# ---- The range ------------------------------------------------------------------------------------------------------------

@recipe(L, "range", "stove", "A kitchen range lit: the fire shut in cast iron, its draught murmuring, the iron ticking, a kettle",
        """A lived-in house's cast-iron range: the fire inside it heard through the iron, a low muffled roar breathing
        through the door's vents (synth fire, lowpassed hard: you hear it through iron), the iron ticking as it takes the
        heat (the packs' real metal ticks, low and dull), and a kettle on the hob just on the simmer (fine bubbling and a
        faint breathy whistle that never quite comes). An 8 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-28, seconds=LOOP)
def range_stove(rng, k):
    L_ = LOOP + 0.6
    n = samples(L_)
    inside = lp(synth.fire(L_, 0.6, rng), 700, 2) * lapping(rng, n, 2, 0.4)
    vents = ck.norm(bp(synth.noise(L_, rng, "pink"), 300, 1500)) * lapping(rng, n, 3, 0.5) * 0.15
    simmer = ck.norm(synth.bubbles(L_, 18, 900, 3200, rng, rise=(0.1, 0.4))) * 0.12
    steam = ck.norm(bp(synth.noise(L_, rng), 1800, 2600)) * np.clip(lapping(rng, n, 0.5, 0.8) - 0.5, 0, None) * 0.05
    y = mix(ck.norm(inside), vents, simmer, steam, ticks(rng, L_, TICKS, 0.35, -4, (-24, -14)) * 0.7)
    return seam(lp(y, 7000), LOOP)


# ---- The custom's things in a house ---------------------------------------------------------------------------------------

@recipe(L, "clock", "case", "A clock ticking in a house: a longcase's slow tick-tock in its wooden case",
        """A longcase clock in a dead-quiet room: the escapement's tick and its slightly different tock, a second apart
        (the packs' real small clicks and a light wood knock, pitched for brass on steel), each rung through the tall wooden
        case (its hollow low modes), the pendulum's faint swish under it. An 8 s loop, eight beats.""",
        sources=TICKS + WOOD_L, loop=True, takes=1, lufs=-30, seconds=LOOP)
def clock_case(rng, k):
    parts = []
    tick_k, tock_k = TICKS[0], TICKS[-1]
    for i in range(9):
        key = tick_k if i % 2 == 0 else tock_k
        x = dsp.vari(hit_of(key, 0, 0.08, -18), (2 if i % 2 == 0 else -1))
        x = ck.norm(ck.choke(hp(x, 400), 0.003, 0.012))
        body = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(WOOD_L[i % 2]), 4))), 0.004, 0.02)
        parts += [(i * 1.0, x, -2), (i * 1.0, body, -14), (i * 1.0, ck.hollow(rng, [210, 330, 520], 0.12, q=8), -20)]
    y = ck.place(parts, LOOP + 0.6)
    swish = ck.norm(bp(synth.noise(LOOP + 0.6, rng), 400, 1400)) * (0.5 + 0.5 * np.abs(np.sin(np.pi * np.arange(len(y)) / SR))) * 0.02
    return seam(mix(y, swish), LOOP)


@recipe(L, "radio", "wireless", "A wireless left on in a house: static, and a far station fading in and out",
        """A wireless set left on, tuned to nothing that's there any more: the receiver's static through its speaker
        (world_voice's weak-signal static), and under it now and then a far station swelling up out of the hiss and away
        again, a voice too far to make out (wordless syllables through the set's 300 Hz-3 kHz) or a few notes of a tune.
        A 10 s loop.""",
        loop=True, takes=1, lufs=-30, seconds=10.0)
def radio_wireless(rng, k):
    st = radio_static(rng, 0)
    n = len(st)
    L_ = n / SR
    far = mumble(rng, L_, 120, "man", rate=4.5)
    fade = np.clip(W.norm(lp(rng.standard_normal(n).astype(np.float32), 0.25)) * 0.5 + 0.1, 0, None)
    far = bp(far, 300, 3000) * fade * 0.5
    return dsp.crush(mix(st, far) / 1.2, 9).astype(np.float32)


# ---- The townsfolk --------------------------------------------------------------------------------------------------------

def mumble(rng, length, f0, tract="man", rate=4.0):
    """One person talking low in no words: syllables at about `rate` a second on mostly shut vowels, the pitch drifting
    and sagging at each phrase's end, pauses between phrases."""
    vow, pts, shape = [], [], []
    t = 0.0
    while t < length:
        phrase = rng.uniform(0.8, 2.4)
        end = min(length, t + phrase)
        while t < end:
            vow.append((t, str(rng.choice(["m", "u", "o", "e", "a", "m", "u"]))))
            pts.append((t, f0 * rng.uniform(0.92, 1.12) * (1 - 0.12 * (t - (end - phrase)) / phrase)))
            d = rng.uniform(0.6, 1.4) / rate
            shape += [(t, 0.2), (t + d * 0.35, rng.uniform(0.55, 1.0)), (t + d * 0.9, 0.25)]
            t += d
        shape += [(t, 0.0), (t + rng.uniform(0.4, 1.6), 0.0)]
        t = shape[-1][0]
    pts.append((length, pts[-1][1]))
    shape.append((length + 0.01, 0.0))
    g = synth.glottis(env(pts, length, "exp"), length, rng, jitter=0.015, shimmer=0.15, oq=0.65)
    v = synth.tract(g, vow, tract, breath=0.3, rng=rng) * dsp.fit(env(shape, length), samples(length))
    return ck.norm(v)


def mask(x, cup=750):
    """Through a breathing mask: the top gone (rubber and filters), a cup's hollow ring in the low mids."""
    y = lp(x, 1900, 2)
    return ck.norm(mix(y, dsp.resonate(y, [cup, cup * 2.3], q=3) * 0.15))


def breaths(rng, length, rate, valve=True):
    """Breathing through a respirator: each breath's air rasping through the filter, the exhale valve's small click."""
    parts = []
    t = rng.uniform(0, 1)
    while t < length:
        L_ = rng.uniform(0.7, 1.1)
        air = ck.norm(bp(synth.noise(L_, rng, "pink"), 500, 2600)) * env([(0, 0), (L_ * 0.4, 1), (L_, 0)], L_)
        parts.append((t, air, -12))
        if valve:
            parts.append((t + L_ * 0.55, tick(rng, TICKS[0], -2, 0.01), -18))
        t += rate * rng.uniform(0.8, 1.2)
    return ck.place(parts, length + 1.5)


@recipe(L, "murmur", "masks", "Townsfolk talking low among themselves through their masks: three or four voices, no words",
        """The square's people standing about in the cold, talking low: three or four voices (men and a woman, 95-210 Hz,
        synth glottis and tract) on mostly shut vowels with pauses between phrases, so it never resolves into words; each
        through a breathing mask's rubber cup (the top gone, a hollow ring at 700-800 Hz), a few metres apart. A 10 s loop.""",
        loop=True, takes=1, lufs=-27, seconds=10.0)
def murmur_masks(rng, k):
    L_ = 10.6
    voices = [(105, "man", -3), (128, "man", -6), (196, "woman", -5), (92, "man", -9)]
    parts = []
    for i, (f0, tract, db) in enumerate(voices):
        v = mask(mumble(rng, L_, f0, tract, rate=rng.uniform(3.4, 4.6)), cup=rng.uniform(680, 820))
        v = dsp.room(v, "night", wet=0.12 + 0.05 * i, rng=rng)[:samples(L_)]
        parts.append((0, v, db))
    y = ck.place(parts, L_)
    return seam(y, 10.0)


@recipe(L, "murmur", "close", "Two townsfolk close by, talking low through respirators, their breathing rasping through the filters",
        """Closer: two people a couple of metres off, one talking low and the other answering now and then (synth voices on
        shut vowels through the masks' cups), and between their words their breath rasping in and out through the
        respirators' filters, the exhale valve clicking. A 10 s loop.""",
        sources=TICKS, loop=True, takes=1, lufs=-27, seconds=10.0)
def murmur_close(rng, k):
    L_ = 10.6
    a = mask(mumble(rng, L_, 112, "man", rate=4.0), 740)
    b = mask(mumble(rng, L_, 180, "woman", rate=3.6), 800)
    b = b * np.clip(W.norm(lp(rng.standard_normal(len(b)).astype(np.float32), 0.3)) * 0.5 + 0.3, 0, 1)
    y = ck.place([(0, a, -3), (0, b, -6), (0, breaths(rng, L_, 3.2), -6), (0.8, breaths(rng, L_, 3.6), -9)], L_)
    return seam(y, 10.0)


def cough_once(rng, f0, force=1.0):
    """One human cough: the glottis bursting open (a hard puff of air), a rush of turbulent air through the throat on 'ah'
    falling to 'h', with only a short rough voiced edge at its start (a cough is mostly air, never a vowel sung)."""
    Lc = rng.uniform(0.2, 0.3)
    puff = ck.norm(bp(synth.noise(0.05, rng), 400, 3500)) * env([(0, 0), (0.003, 1), (0.05, 0)], 0.05)
    # Mostly plain air through a soft hump, the tract only colouring it: a tract on noise alone whistles.
    plain = ck.norm(lp(hp(synth.noise(Lc, rng), 350, 1), 2800, 2))
    shaped = ck.norm(synth.breath(Lc, [(0, "a"), (Lc * 0.5, "o"), (Lc, "h")], "man", rng))
    air = ck.norm(mix(plain * 0.7, shaped * 0.4)) * env([(0, 0), (0.006, 1), (Lc * 0.35, 0.55), (Lc, 0)], Lc)
    Lv = Lc * 0.4
    g = synth.glottis(env([(0, f0 * 1.3), (Lv, f0 * 0.85)], Lv, "exp"), Lv, rng, jitter=0.05, shimmer=0.4, rough=0.7, oq=0.45)
    edge = synth.tract(g, "a", "man", breath=0.9, rng=rng) * env([(0, 0), (0.006, 1), (Lv, 0)], Lv)
    return ck.norm(ck.place([(0, puff, -2 + 3 * np.log2(force)), (0.004, air, 0), (0.004, ck.norm(edge), -7)]))


@recipe(L, "cough", "mask", "A townsperson coughing into their mask: a dry cough, a fit of two or three, a wet one",
        """The foul air in their chests: a human cough (the glottis bursting open, a hard puff of air into a short rough
        'ah' that dies into breath; synth glottis and tract), through a breathing mask's cup. One dry cough; a fit of two
        or three, the last weaker; and a wet, chesty one with a rattle in it.""",
        takes=3, lufs=-22)
def cough_mask(rng, k):
    f0 = rng.uniform(140, 190)
    if k % 3 == 0:
        parts = [(0, cough_once(rng, f0), 0)]
    elif k % 3 == 1:
        parts = [(0, cough_once(rng, f0), 0), (0.32, cough_once(rng, f0 * 0.95), -2), (0.62, cough_once(rng, f0 * 0.9, 0.7), -6)]
    else:
        c = cough_once(rng, f0 * 0.85, 1.2)
        rattle = ck.norm(bp(synth.noise(0.3, rng), 150, 900)) * dsp.tremolo(np.ones(samples(0.3), np.float32), 22, 0.9) * \
            env([(0, 0), (0.03, 1), (0.3, 0)], 0.3)
        parts = [(0, c, 0), (0.02, rattle, -10), (0.45, cough_once(rng, f0 * 0.8, 0.8), -4)]
    return mask(ck.place(parts), 760)
