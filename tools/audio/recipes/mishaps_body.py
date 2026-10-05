"""The crew's own mishaps, the body half (crew-mishaps): a head bonked on a tunnel's portal and the tumble off the roof
after it, thrown off on a curve, the pockets emptying across the ballast, crushed under a casting from the crane and the
chain swinging in the quiet after, a punch with empty hands, and a dead crewmate's boots.

The director wants these funny the way Lethal Company's and R.E.P.O.'s physics deaths are funny: the crew doing it to
themselves, told with real sounds and comic timing, never a cartoon stinger, a sad trombone or music. So every sound here
is the real event, built like the rest of the crew's foley (crew_kit: the packs' recordings cut to their hits, matched and
layered, the car roof's tin, the ballast, the boards), and the comedy is in when things happen and in how little: the
beat before the grit falls off the brick, the thumps along the roof spreading out and then nothing for a long moment
before a small flop far below, the one tin lid still wobbling after everything else has stopped, an iron BONG and then
dead quiet. The one licence taken is the bonk: a hollow head knocked on brick is given the pitch that drops a few
semitones in its first tenth of a second (modelled as the modes of a hollow shell, and as a real hollow wooden knock
bent the same way), because that drop is what makes a knock read as a bonk.
"""

import numpy as np

import dsp
import synth
from dsp import SR, samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of, lantern, CLOTH, SOFT, TIN, GLASS, PLATE_L, PLATE_M, PLATE_H, METAL_M, METAL_H, WOOD_L
from recipes.crew_feet import heel, toe, sole, BOOTS, CONCRETE, STONES
from recipes.kit import snap

L = "crew-mishaps"
SOFT_M = K("impactSoft_medium")
BELL = K("impactBell_heavy")
MINING = K("impactMining")
JANGLE = S("misc_09", "misc_10")
COINS = R("handleCoins", "handleCoins2")
SMALL = S("metal_02", "metal_06")
SLAPS = R("bookPlace2", "bookPlace1")
JAMB = S("misc_08", "wood_hit_02", "wood_hit_01")
DROP_LEATHER = R("dropLeather")


# ---- Shared parts ---------------------------------------------------------------------------------------------------------

def modes(f0, ratios, gains, decays, length, bend=0.0, tau=0.05):
    """A struck body's ring as its modes: decaying sines at f0 x ratio. `bend` semitones of pitch at the strike, falling
    to rest with time constant `tau` (the bonk's drop)."""
    n = samples(length)
    t = np.arange(n) / SR
    glide = 2 ** (bend * np.exp(-t / tau) / 12)
    y = np.zeros(n)
    for r, g, d in zip(ratios, gains, decays):
        y += g * np.sin(2 * np.pi * np.cumsum(f0 * r * glide) / SR) * np.exp(-t / d)
    a = samples(0.0006)
    y[:a] *= np.linspace(0, 1, a)
    return ck.norm(dsp.fade(y.astype(np.float32), 0.0, length * 0.3))


def bend(x, semis, tau):
    """Varispeed whose speed falls from `semis` up to normal with time constant `tau`: a real knock's pitch dropping."""
    n = len(x)
    rate = 2 ** (semis * np.exp(-np.arange(n) / SR / tau) / 12)
    pos = np.cumsum(rate)
    pos = pos[pos < n - 1]
    return np.interp(pos, np.arange(n), x).astype(np.float32)


def burst(rng, length, lo, hi, att=0.0005):
    """A short band of noise with a fast rise and an exponential fall: a contact's crack, a cloth's snap."""
    y = bp(synth.noise(length, rng), lo, hi)
    return ck.norm(y * env([(0, 0), (att, 1), (length, 0.001)], length, "exp"))


def roof_thump(rng, take, s=1.0):
    """A body (not a boot) coming down on the car roof's tin: the panel and the car under it booming hollow, the sheet's
    thin crash damped flat under a coat, the tin's tick, and the soft weight of it."""
    drum = ck.hollow(rng, [84, 132, 205, 310], 0.32, q=7)
    sheet = ck.norm(hp(ck.choke(ck.align(dsp.vari(ck.get(PLATE_L[take % 5]), -2.5 + rng.uniform(-0.5, 0.5))), 0.02, 0.04), 120))
    tin = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(TIN[(take + 2) % 5]), -8 + rng.uniform(-0.5, 0.5))), 0.008, 0.03))
    body = hit_of(SOFT[(take + 1) % 5], 0, 0.3)
    return mix(drum * 1.1 * s ** 1.3, sheet * 0.28 * s, tin * 0.08 * s, body * 0.7 * s)


def far_flop(rng, take, cut=1300):
    """A body landing on the ballast well below and behind: the soft weight and the stones, darkened by the distance and
    the train between, with a short outdoor answer."""
    body = mix(hit_of(SOFT[take % 5], 0, 0.35), ck.pad(rng, 0.16, 220) * 0.9, hit_of(DROP_LEATHER[0], 0, 0.3) * 0.4)
    y = mix(body, ck.floor(rng, "ground", take, 2.2, 0.2) * 0.5)
    y = lp(lp(y, cut, 2), cut * 1.4, 1)
    return ck.space(y, 0.5, 0.25, 2500, ((0.03, 0.3), (0.07, 0.2)), seed=21)


def rate_times(rng, length, r0, r1, jitter=0.3, power=1.0):
    """Event times whose rate goes from r0 to r1 a second over `length` (shaped by `power`), with a little catch in each."""
    out, t = [], 0.0
    while t < length:
        r = r0 + (r1 - r0) * (t / length) ** power
        out.append(t)
        t += (1 / max(r, 0.5)) * (1 + jitter * rng.uniform(-0.6, 0.6))
    return out


# ---- 1. The tunnel's portal ------------------------------------------------------------------------------------------------

def bonk_core(rng, take, f0, bend_st, ring):
    """The bonk: a hollow head's modes (a strong first mode that knocks loud and dies fast into a short ring, three weak
    inharmonic ones above it) dropping `bend_st` semitones as it's struck, and a real hollow wooden knock bent the same
    way for the contact."""
    shell = modes(f0, [1, 1, 1.58, 2.31, 3.35], [1, 0.35, 0.3, 0.14, 0.06],
                  [ring * 0.35, ring, ring * 0.3, ring * 0.2, ring * 0.12], ring * 6, bend_st, 0.04)
    wood = ck.norm(ck.choke(bend(ck.align(ck.get(WOOD_L[take % 5])), bend_st * 0.8, 0.05), 0.03, 0.04))
    mass = dsp.fade(synth.thump(f0 * 0.34, 0.12, drop=0.35), 0.0, 0.05)
    return mix(shell, wood * 0.45, mass * 0.3)


@recipe(L, "tunnel-bonk", "coconut", "The head's BONK on the portal, clean: a hollow knock that drops in pitch and rings short",
        """The cleaner of the two: a hollow head knocked, modelled as a shell's modes (a strong first mode near 450 Hz,
        three weak inharmonic ones over it) whose pitch drops five to seven semitones in the first tenth of a second and
        rings for about a fifth of one, with a real hollow wooden knock (the packs' light wood impacts) bent down the same
        way for the contact and a little low weight under it. Only a pinch of brick grit after it. Three heads: each a
        slightly different pitch and drop.""",
        sources=WOOD_L, takes=3, lufs=-18)
def bonk_coconut(rng, k):
    f0 = [470, 420, 520][k] * rng.uniform(0.98, 1.02)
    core = bonk_core(rng, k, f0, [6.0, 7.0, 5.5][k], [0.11, 0.13, 0.1][k])
    click = burst(rng, 0.006, 1500, 7000)
    grit = ck.grains(rng, 7, 0.12, 3000, 8000, q=(3, 8), length=(0.003, 0.008))
    return ck.place([(0, core, 0), (0, click, -18), (0.06, grit, -30)])


@recipe(L, "tunnel-bonk", "brick", "The head's BONK on the portal's brick, grounded: the bonk, the brick, grit and a lamp or cap knocked loose",
        """The bonk with the world round it: the same hollow-head bonk but duller (shorter ring, a smaller drop) on a real
        hollow wooden knock bent down, the brick lip's own hard knock (the packs' concrete footsteps, the bottom taken
        out), a beat of nothing, then grit off the brick pattering onto the roof tin (the packs' stones, small grains). Then
        whatever was knocked loose: the cap flopping onto the tin, the hand lamp clattering (its tin base and glass), or the
        lamp's bail swinging. All in the portal's short brick echo, under a second.""",
        sources=WOOD_L + CONCRETE + STONES + CLOTH + TIN + GLASS + S("metal_02", "metal_06") + PLATE_L + SOFT, takes=3,
        lufs=-18)
def bonk_brick(rng, k):
    f0 = [360, 330, 390][k] * rng.uniform(0.98, 1.02)
    core = bonk_core(rng, k + 2, f0, [4.5, 5.0, 4.0][k], [0.07, 0.08, 0.065][k])
    brick = ck.norm(ck.choke(ck.align(hp(ck.get(CONCRETE[(k * 2 + 1) % 5]), 400)), 0.015, 0.03))
    stones = ck.get(STONES[k % 3])
    h = ck.hits(stones, floor_db=-14, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    trickle = ck.norm(hp(ck.cut(ck.denoise(stones), a, a + samples(0.18), 0.002, 0.08), 2500))
    patter = ck.grains(rng, 12, 0.25, 2500, 7500, q=(4, 12), length=(0.003, 0.01), shape=0.2)
    parts = [(0, core, 0), (0, brick, -5), (0.15, trickle, -15), (0.17, patter, -15)]
    if k == 0:   # the cap knocked off, landing on the tin
        parts += [(0.02, ck.cloth(rng, k, 0.18), -12), (0.46, roof_thump(rng, k, 0.3), -14)]
    elif k == 1:  # the hand lamp knocked out of the hand, clattering on the roof
        parts += [(0.36, lantern(rng, k, "base"), -9), (0.37, lantern(rng, k, "globe"), -13),
                  (0.38, roof_thump(rng, k + 1, 0.4), -15)]
    else:         # the lamp hung on the coat, its bail swinging
        parts += [(0.05, lantern(rng, k, "bail"), -12), (0.3, lantern(rng, k + 1, "bail"), -15)]
    y = ck.space(ck.place(parts), 0.45, 0.16, 3500, ((0.011, 0.45), (0.024, 0.3), (0.041, 0.2)), seed=9)
    return dsp.fade(dsp.fit(y, samples(0.95)), 0.0, 0.15)


# ---- 2. Off the end of the roof ------------------------------------------------------------------------------------------------

@recipe(L, "tunnel-tumble", "bounces", "The body thumping back along the roof tin, further apart each time, a long drop, a small flop far below",
        """Four or five hollow thumps of a body on the car roof (the roof's tin over the hollow car: its low boom, the
        sheet's crash damped flat under a coat, a soft heavy hit for the weight), each a limb's smaller thump after it,
        the gaps opening out and each a little softer, the coat flapping between. Then off the end: nothing for a long
        moment but a little cloth, and a soft flop onto the ballast well below (a soft hit and stones, dark with distance).""",
        sources=PLATE_L + TIN + SOFT + CLOTH + DROP_LEATHER + ck.FLOOR_SOURCES["ground"], takes=2, lufs=-21)
def tumble_bounces(rng, k):
    gaps = np.array([0.17, 0.24, 0.33, 0.46]) * rng.uniform(0.9, 1.1, 4)
    n = 4 + k
    parts, t = [], 0.0
    for i in range(n):
        s = 1.0 - 0.12 * i
        parts += [(t, roof_thump(rng, k + i, s), -2.0 * i), (t + 0.01, ck.cloth(rng, k + i, 0.2), -16 - i),
                  (t + rng.uniform(0.05, 0.08), roof_thump(rng, k + i + 3, 0.45 * s), -12 - 2 * i)]
        if i < n - 1:
            t += gaps[i]
    # the coat tails catching the air as it goes over the end, then the fall
    edge = t + rng.uniform(0.18, 0.24)
    fall = ck.whoosh(rng, 0.45, 150, 900, 0.4, 0.9)
    parts += [(edge, ck.cloth(rng, k + 5, 0.25), -18), (edge + 0.05, fall, -28),
              (edge + rng.uniform(0.55, 0.65), far_flop(rng, k), -17)]
    return ck.place(parts)


def tin_skid(rng, length, r0, r1):
    """A coat and its buttons skidding over sheet tin: stick-slip juddering in the tin's band, the sheet's own modes
    singing faintly in it, slowing as it goes."""
    times = rate_times(rng, length, r0, r1, 0.5)
    x = np.zeros(samples(length + 0.02), np.float32)
    for t in times:
        a = samples(t)
        kk = samples(rng.uniform(0.002, 0.006))
        x[a:a + kk] += (rng.standard_normal(kk) * np.hanning(kk) * rng.uniform(0.3, 1.0)).astype(np.float32)[:len(x) - a]
    x = mix(x, synth.noise(length + 0.02, rng, "pink") * 0.05)
    body = dsp.resonate(x, [f * rng.uniform(0.97, 1.03) for f in synth.TIN], q=14, gains=[1, 0.8, 0.6, 0.45, 0.3, 0.2])
    y = mix(lp(bp(x, 500, 5000), 3000, 1), ck.norm(body) * 0.35)
    return ck.norm(y * env([(0, 0), (0.03, 1), (length * 0.6, 0.7), (length, 0)], length + 0.02))


def oil_can(rng, take, s=1.0):
    """The roof's tin panel snapping through under the weight (oil-canning): a sharp tinny pop and the panel's low boom."""
    pop = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(TIN[take % 5]), -10 + rng.uniform(-0.5, 0.5))), 0.012, 0.05))
    boom = modes(rng.uniform(150, 175), [1, 1.6, 2.3], [1, 0.4, 0.2], [0.07, 0.04, 0.025], 0.35, 1.5, 0.02)
    return mix(pop * 0.7 * s, boom * 0.6 * s, ck.hollow(rng, [84, 132, 205], 0.2, q=6) * 0.4 * s)


@recipe(L, "tunnel-tumble", "skid", "The body rolling over twice and skidding along the roof tin, the panel popping, off the end, a flop far below",
        """A different telling: the body rolls over two or three times quickly (hollow thumps on the roof tin), then
        skids on its back along the tin (the coat and its buttons juddering over the sheet, slowing), the roof's thin
        panel popping in under the weight and springing back (oil-canning: a tin hit pitched down with the panel's boom),
        a last bump at the roof's end, a long empty drop, and a soft flop on the ballast well below.""",
        sources=PLATE_L + TIN + SOFT + CLOTH + DROP_LEATHER + ck.FLOOR_SOURCES["ground"], takes=2, lufs=-21)
def tumble_skid(rng, k):
    rolls = [0.0, rng.uniform(0.12, 0.15), rng.uniform(0.27, 0.32)][:2 + k]
    parts = []
    for i, t in enumerate(rolls):
        parts += [(t, roof_thump(rng, k + i, 1.0 - 0.15 * i), -1.5 * i), (t + 0.01, ck.cloth(rng, k + i, 0.15), -18)]
    s0 = rolls[-1] + 0.08
    skid = rng.uniform(0.75, 0.9)
    parts += [(s0, tin_skid(rng, skid, 140, 35), -9), (s0 + 0.03, ck.cloth(rng, k + 3, 0.4), -14),
              (s0 + skid * 0.3, oil_can(rng, k), -6), (s0 + skid * 0.3 + rng.uniform(0.28, 0.36), oil_can(rng, k + 2, 0.5), -12)]
    edge = s0 + skid + 0.1
    parts += [(edge, roof_thump(rng, k + 4, 0.5), -10), (edge + 0.03, ck.cloth(rng, k + 1, 0.25), -18),
              (edge + rng.uniform(0.6, 0.7), far_flop(rng, k + 1), -17)]
    return ck.place(parts)


# ---- 3. Thrown off on a curve ------------------------------------------------------------------------------------------------

def flap(rng, length, rate, lo=250, hi=2200, sharp=3.0, gusts=0.0):
    """Cloth flapping in a fast airflow like a flag: noise in the cloth's band, chopped at the flapping rate (a constant or
    a curve), each flap a sharpened crest, the rate wandering. `gusts` (a few a second) lets it catch and go slack in
    turn instead of flapping evenly."""
    n = samples(length)
    r = synth.curve(rate, n) * (1 + 0.25 * lp(rng.standard_normal(n).astype(np.float32), 6) * 10)
    ph = 2 * np.pi * np.cumsum(np.clip(r, 3, 60)) / SR
    wave = (0.5 + 0.5 * np.sin(ph)) ** sharp
    y = bp(synth.noise(length, rng, "pink"), lo, hi) * wave
    if gusts:
        g = lp(rng.standard_normal(n + SR).astype(np.float32), gusts)[:n]
        y = y * np.clip(g / (np.std(g) + 1e-9) * 0.6 + 0.4, 0.0, 1.5) ** 2
    return ck.norm(y.astype(np.float32))


@recipe(L, "thrown-flail", "flutter", "Thrown through the air: arms windmilling, the coat catching the air and flapping, the air rushing",
        """Physics, since the packs have no cloth in a gale: the arms windmilling, four or five round whooshes coming
        quicker each time (pink noise through a band that swells to about 1 kHz), the coat catching the air in gusts and
        flapping like a flag (noise in the cloth's band chopped 15-25 times a second), a crack as a coat tail whips, and
        the air rushing past under it all (its band rising with the speed). It ends in the air; the landing is its own
        sound.""",
        takes=2, lufs=-21)
def flail_flutter(rng, k):
    Ln = rng.uniform(1.2, 1.35)
    speed = env([(0, 0.25), (0.18, 1.0), (Ln * 0.7, 0.9), (Ln, 0.6)], Ln)
    air = dsp.sweep_filter(synth.noise(Ln, rng, "pink"), "lp", 300 + 1400 * speed, q=0.8)
    air = ck.norm(hp(air, 120)) * env([(0, 0), (0.15, 1), (Ln * 0.8, 0.85), (Ln, 0.5)], Ln)
    coat = flap(rng, Ln, env([(0, 14), (0.25, 22), (Ln, 18)], Ln), gusts=3.0) * env([(0, 0), (0.12, 1), (Ln, 0.8)], Ln)
    parts = [(0, air, -18), (0, coat, -11)]
    t, gap = rng.uniform(0.02, 0.06), rng.uniform(0.32, 0.38)
    while t < Ln - 0.2:
        Lw = min(0.28, gap * 0.8)
        parts.append((t, ck.whoosh(rng, Lw, 140, rng.uniform(850, 1200), 0.5, 1.6), rng.uniform(-2, 0)))
        t += gap
        gap *= rng.uniform(0.74, 0.82)
    parts.append((rng.uniform(0.35, Ln - 0.3), burst(rng, 0.03, 500, 3500, 0.002), -9))
    return dsp.fade(ck.place(parts, Ln), 0.04, 0.12)


@recipe(L, "thrown-flail", "cloth", "Thrown through the air: real coat and cloth snaps, the body turning over, a buckle rattling",
        """From the packs' real cloth: every snap and flap in the cloth and leather handlings cut out and fired off fast and
        uneven, a hair quicker than they were handled (the coat whipping in the wind of the fall), two broad slow
        whooshes as the body turns over, the belt buckle rattling once, and a low rush of air under it all. It ends in
        the air.""",
        sources=CLOTH + R("dropLeather", "clothBelt", "handleSmallLeather2"), takes=2, lufs=-21)
def flail_cloth(rng, k):
    Ln = rng.uniform(1.15, 1.3)
    pool = []
    for key in CLOTH + R("dropLeather", "handleSmallLeather2"):
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-20, gap=0.04)[:4]:
            pool.append(ck.norm(ck.cut(x, s - samples(0.004), s + samples(0.12), 0.002, 0.05)))
    parts, t = [], rng.uniform(0.0, 0.03)
    while t < Ln - 0.1:
        snapx = dsp.vari(pool[int(rng.integers(len(pool)))], rng.uniform(1.0, 3.5))
        parts.append((t, snapx, rng.uniform(-7, 0)))
        t += rng.uniform(0.045, 0.12)
    turn = [(rng.uniform(0.05, 0.15), ck.whoosh(rng, 0.55, 90, 650, 0.5, 0.8), -4),
            (rng.uniform(0.6, 0.7), ck.whoosh(rng, 0.5, 90, 600, 0.45, 0.8), -6)]
    buckle = hit_of(R("clothBelt")[0], k, 0.15)
    rush = ck.norm(lp(hp(synth.noise(Ln, rng, "pink"), 100), 900)) * env([(0, 0), (0.2, 1), (Ln, 0.7)], Ln)
    parts += turn + [(rng.uniform(0.3, 0.6), buckle, -16), (0, rush, -14)]
    return dsp.fade(ck.place(parts, Ln), 0.03, 0.12)


# ---- 4. The pockets emptying across the ballast --------------------------------------------------------------------------------

def on_stones(rng, x, take, weight=0.5):
    """An object hitting ballast: the object's own sound with the stones knocking under it."""
    return mix(x, ck.floor(rng, "ground", take, weight, 1.0) * 0.5)


def wrench(rng, take):
    """A forged-steel wrench hitting stone: a hard, short clank (a heavy metal hit choked: solid steel barely rings)."""
    t = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_H[take % 5]), -1 + rng.uniform(-0.5, 0.5))), 0.004, 0.03))
    return on_stones(rng, t, take, 0.8)


def tin(rng, take, semis=0.0, damp=0.05):
    """A tobacco tin or a snuff tin knocking on stone: a small hollow tin hit."""
    x = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(TIN[take % 5]), 2 + semis + rng.uniform(-0.6, 0.6))), 0.01, damp))
    return hp(x, 300)


def coin(rng, take):
    """A coin or a button chinking on stone: one of the packs' coin handlings' chinks, cut to itself."""
    return dsp.vari(hit_of(COINS[take % 2], int(rng.integers(4)), 0.09, -20), rng.uniform(-1.5, 1.5))


def tin_roll(rng, take, length, r0=22, r1=5):
    """A tin rolling away over the stones: its rim knocking on each stone it rolls over, slowing, softer as it goes."""
    times = rate_times(rng, length, r0, r1, 0.45, 0.7)
    b = dsp.Bus(length + 0.15)
    for i, t in enumerate(times):
        b.at(t, tin(rng, take + i, rng.uniform(1, 4), 0.025), -14 * (t / length) ** 1.5 + rng.uniform(-5, 0))
    return b.x


def clatter(rng, k, tight=False):
    """What was in hand and in the pockets hitting the ballast: the wrench (bouncing twice), a tin, coins and bits."""
    spread = 0.35 if tight else 0.55
    w = ck.bounce(rng, wrench(rng, k), 2, rng.uniform(0.13, 0.18), 0.5, 7)
    parts = [(0, w, 0), (rng.uniform(0.03, 0.07), ck.bounce(rng, on_stones(rng, tin(rng, k), k + 1, 0.3), 2, 0.11, 0.55, 6), -4)]
    for i in range(int(rng.integers(3, 6))):
        parts.append((rng.uniform(0.02, spread), on_stones(rng, coin(rng, k + i), k + i, 0.15), rng.uniform(-14, -8)))
    for i in range(2):
        bit = dsp.vari(hit_of(SMALL[(k + i) % 2], int(rng.integers(2)), 0.12), rng.uniform(1, 4))
        parts.append((rng.uniform(0.05, spread), bit, rng.uniform(-16, -11)))
    parts.append((0.01, ck.grains(rng, 40, spread, 800, 5000, q=(3, 9), shape=0.8), -12))
    return ck.place(parts)


@recipe(L, "pocket-scatter", "clatter", "Pockets emptying on the ballast: a wrench bouncing, a tin, coins and bits, the tin rolling off to rest",
        """Everything at once: the forged-steel wrench clanking on the stones and bouncing twice (a heavy metal hit choked
        short), a tobacco tin bouncing (the packs' tin hits, pitched small), coins and bits chinking (the packs' coin and
        small metal handlings), stones knocked loose under all of it (the ballast's crunch and grains), and then the tin
        rolling away over the stones, slowing, to rest.""",
        sources=METAL_H + TIN + COINS + SMALL + ck.FLOOR_SOURCES["ground"], takes=3, lufs=-21)
def scatter_clatter(rng, k):
    roll = tin_roll(rng, k + 2, rng.uniform(0.75, 1.0), rng.uniform(18, 26), 4)
    return ck.place([(0, clatter(rng, k), 0), (rng.uniform(0.35, 0.45), roll, -7)])


def wobble(rng, take, length):
    """A tin lid spinning down flat on a stone or a sleeper (Euler's disk): its rim rocking round faster and faster as it
    settles, each contact a small tin tick, then the clack of it going flat."""
    times = []
    t = 0.0
    while t < length:
        times.append(t)
        r = 8 + 75 * (t / length) ** 2.2
        t += 1 / r
    b = dsp.Bus(length + 0.2)
    lid = [rng.uniform(1700, 2100), rng.uniform(3900, 4400), rng.uniform(6500, 7300)]
    for i, t in enumerate(times):
        b.at(t, synth.click(lid[0], q=30, length=0.025, rng=rng, body=lid), -2 - 4 * t / length + rng.uniform(-1.5, 0))
        if i % 3 == 0:
            b.at(t, tin(rng, take + i, 4, 0.012), -14)
    b.at(times[-1] + 0.025, tin(rng, take, 3, 0.03), -2)
    b.at(times[-1] + 0.05, tin(rng, take + 1, 3, 0.02), -10)
    return b.x


@recipe(L, "pocket-scatter", "straggler", "Pockets emptying on the ballast: a quick clatter, then one last thing taking its time",
        """Comic timing: the clatter is over quickly (the wrench, a tin, coins and bits on the stones, as in 'clatter') and
        for a moment it's quiet, then one straggler: a tin lid spinning down flat on a sleeper, rocking faster and faster
        until it clacks flat (Euler's disk, the tin hits' ticks); a coin rolling away over the stones and dropping into a
        gap with a last tink; or the tin rolling off and stopping against the rail with a small steel tonk.""",
        sources=METAL_H + TIN + COINS + SMALL + METAL_M + ck.FLOOR_SOURCES["ground"], takes=3, lufs=-21)
def scatter_straggler(rng, k):
    gap = rng.uniform(0.65, 0.8)
    if k == 0:
        last = wobble(rng, k, rng.uniform(0.65, 0.8))
    elif k == 1:
        times = rate_times(rng, 0.6, 14, 6, 0.5)
        b = dsp.Bus(0.95)
        for i, t in enumerate(times):
            b.at(t, coin(rng, k + i), -4 - 8 * t / 0.6 + rng.uniform(-3, 0))
        b.at(0.6 + rng.uniform(0.12, 0.18), dsp.vari(coin(rng, k + 7), -2), -6)
        last = b.x
    else:
        roll = tin_roll(rng, k, 0.55, 16, 6)
        rail = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_M[k % 5]), -5)), 0.04, 0.25))
        last = ck.place([(0, roll, 0), (0.62, tin(rng, k, 0, 0.04), 0), (0.62, rail, -7)])
    return ck.place([(0, clatter(rng, k, tight=True), 0), (gap, last, -6)])


# ---- 5. Under the casting -------------------------------------------------------------------------------------------------------

def crunch_under(rng, take):
    """Something giving way under the iron, heard through it: a dull crack, a short dark squash, grit, all muffled."""
    c = lp(snap(rng, 0.07, rng.uniform(700, 1000)), 1400)
    squash = ck.slurp(rng, 0.13, 140, 650, bubbles=0.0)
    grit = lp(ck.grains(rng, 25, 0.06, 600, 3000, q=(3, 8)), 2200)
    return ck.place([(0, ck.norm(c), 0), (0.004, squash, -4), (0, ck.norm(grit), -8), (0, hit_of(SOFT[take % 5], 0, 0.2), -4)])


def clank_of(k):
    """A heavy iron part's short clank: a heavy metal hit pitched down to a casting's size, choked."""
    return ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_H[k % 5]), -9)), 0.01, 0.05))


def ground_whump(rng, take):
    """A big weight meeting the ground: the packs' deep mining impact (a low boom) and a dropping sub."""
    m = ck.norm(ck.align(ck.get(MINING[take % 5])))
    return mix(lp(m, 600), synth.thump(rng.uniform(40, 46), 0.45, drop=0.45) * 0.7, ck.pad(rng, 0.2, 160) * 0.5)


@recipe(L, "crushed", "bong", "Under the casting: a massive, dull iron BONG, a muffled crunch under it, then nothing",
        """The casting hitting the ground with a body under it: a big cast-iron part's modes (low, near 100 Hz, inharmonic,
        the top dulled, ringing under a second because it's lying on the ground) for the BONG, struck by the packs' heavy
        metal and plate hits pitched well down, the ground taking the weight (the deep mining impact and a sub), and a
        short muffled crunch under the iron (a dull crack and a dark squash, lowpassed). Then dead stillness.""",
        sources=METAL_H + PLATE_H + MINING + SOFT, takes=2, lufs=-18)
def crushed_bong(rng, k):
    f0 = [98, 112][k] * rng.uniform(0.98, 1.02)
    ring = modes(f0, [1, 1.47, 2.09, 2.76, 3.62, 5.1, 6.7], [1, 0.6, 0.5, 0.35, 0.25, 0.14, 0.07],
                 [0.5, 0.38, 0.28, 0.2, 0.14, 0.09, 0.05], 1.7, 0.7, 0.03)
    ring = lp(ring, 2200, 2)
    clank = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_H[(k + 1) % 5]), -10)), 0.01, 0.06))
    mass = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(PLATE_H[(k + 2) % 5]), -6)), 0.05, 0.1))
    return ck.place([(0, ring, 0), (0, clank, -8), (0, mass, -6), (0, ground_whump(rng, k), -3),
                     (rng.uniform(0.015, 0.025), crunch_under(rng, k), -6)])


@recipe(L, "crushed", "anvil", "Under the casting: a recorded iron clang pitched down to a casting's size, the ground's thump, a crunch",
        """From the packs' metal: a heavy struck bell-metal hit pitched down an octave and more (so its ring becomes a big
        casting's low bong), the slam of a heavy metal door and a heavy plate pitched down for the weight of the strike,
        the deep mining impact for the ground, and a muffled crunch under it (a dull crack, a squash, the ballast's stones
        lowpassed). The casting rocks once and settles, or a little grit trickles off it. Then nothing.""",
        sources=BELL[:2] + S("metal_hit_01") + PLATE_H + METAL_H + MINING + SOFT + STONES[1:], takes=2, lufs=-18)
def crushed_anvil(rng, k):
    bell = ck.norm(ck.align(dsp.vari(ck.get(BELL[k]), -13 + rng.uniform(-0.5, 0.5))))
    bell = ck.choke(lp(bell, 2500), 0.35, 0.3)
    slam = ck.norm(hp(ck.choke(ck.align(dsp.vari(hit_of("sfx_100_v2:metal_hit_01", 0, 0.8), -5)), 0.12, 0.12), 50))
    mass = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(PLATE_H[(k + 3) % 5]), -7)), 0.05, 0.1))
    stones = ck.norm(lp(ck.cut(ck.get(STONES[k + 1]), 0, samples(0.3), 0.002, 0.1), 1500))
    parts = [(0, bell, 0), (0, slam, -6), (0, mass, -6), (0, ground_whump(rng, k + 2), -3),
             (rng.uniform(0.015, 0.025), crunch_under(rng, k + 1), -6), (0.02, stones, -14)]
    if k == 0:   # it rocks back once on what's under it and settles
        parts += [(0.38, ck.norm(lp(mass, 900)), -16), (0.38, ck.norm(lp(clank_of(k), 1200)), -18)]
    else:        # a little grit trickling off it
        parts.append((0.25, lp(ck.grains(rng, 14, 0.5, 1500, 5000, q=(3, 9), shape=0.0), 4000), -28))
    return ck.place(parts)


# ---- 6. The crane's chain in the quiet after -----------------------------------------------------------------------------------

def jangle(rng, take, semis):
    """A bunch of the chain's links knocking: one of the jangling recordings' clusters, pitched down to heavy links."""
    x = ck.get(JANGLE[take % 2])
    h = ck.hits(x, floor_db=-18, gap=0.05)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    c = ck.cut(x, a - samples(0.003), a + samples(rng.uniform(0.12, 0.2)), 0.002, 0.05)
    return ck.norm(hp(dsp.vari(c, semis + rng.uniform(-0.7, 0.7)), 250))


def hook(rng, take, size=-6):
    """The empty hook knocking the jib or the crane's post: a cast hook's short iron clank."""
    clank = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_H[take % 5]), size + rng.uniform(-0.6, 0.6))), 0.01, 0.06))
    body = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(PLATE_M[(take + 2) % 5]), -4)), 0.02, 0.05))
    return mix(clank, body * 0.5)


@recipe(L, "crane-chain", "jangle", "The crane's empty chain swinging loose: link rattles at each end of the swing, the hook clanking, settling",
        """From the packs' jangling (keys and a chain-like rattle, pitched down to heavy links): the chain running loose
        as the casting falls away, then rattling at each end of its swing, less each time, the empty hook clanking once on
        the jib (a heavy metal hit, choked), and a few last single clinks as it hangs still. Quiet, in the hush after.""",
        sources=JANGLE + METAL_H + PLATE_M, takes=2, lufs=-23)
def chain_jangle(rng, k):
    parts = [(0, jangle(rng, k, -6), 0), (0.1, jangle(rng, k + 1, -7), -3), (0.22, jangle(rng, k, -6), -6)]
    t, g = rng.uniform(0.75, 0.85), -4
    for i in range(2):
        parts += [(t, jangle(rng, k + i, -6), g), (t + rng.uniform(0.07, 0.11), jangle(rng, k + i + 1, -7), g - 5)]
        if i == 0:
            parts.append((t + 0.02, hook(rng, k), g - 2))
        t += rng.uniform(0.8, 0.9)
        g -= 6
    for i in range(3):
        t += rng.uniform(0.18, 0.35)
        c = ck.cut(jangle(rng, k + i, -5), 0, samples(0.05), 0.001, 0.03)
        parts.append((t, c, -16 - 3 * i))
    return ck.place(parts)


LINKS = [("sfx_100_v2:metal_02", 0), ("sfx_100_v2:metal_02", 2), ("sfx_100_v2:metal_06", 0), ("sfx_100_v2:misc_19", 0),
         ("sfx_100_v2:misc_26", 0), ("sfx_100_v2:door_04", 0), ("sfx_100_v2:metal_05", 0), ("sfx_100_v2:hit_03", 0)]


def link(rng, size=-9.0):
    """One heavy iron link knocking the next: a single small real clink (the packs' metal and glassy chinks) pitched down
    most of an octave to a link's size, its ring cut short, with a second, softer knock a few ms later as the link
    behind it follows."""
    key, i = LINKS[int(rng.integers(len(LINKS)))]
    c = dsp.vari(hit_of(key, i, 0.1), size + rng.uniform(-1.5, 1.5))
    c = ck.norm(hp(ck.choke(c, 0.03, 0.04), 300))
    return ck.place([(0, c, 0), (rng.uniform(0.006, 0.02), lp(c, 3000), rng.uniform(-12, -6))])


def links(rng, times, db=(-8, 0), size=-9.0):
    b = dsp.Bus(max(times) + 0.3)
    for t in times:
        b.at(t, link(rng, size), rng.uniform(*db))
    return b.x


@recipe(L, "crane-chain", "links", "The crane's empty chain run loose and swinging: one link at a time, the hook's swivel creaking, settling",
        """Built a link at a time from single real clinks (the packs' small metal and glassy chinks pitched down most of an
        octave, each with the next link's softer knock behind it): the chain running out loose through the sheave as the
        casting falls away (a rattle slowing from forty knocks a second to a few), the links bunching at each end of the
        swing, less each time, the hook's swivel creaking faintly as it swings (a slow iron stick-slip creak), one dull
        clank of the hook on the jib, and two or three last clinks.""",
        sources=[k for k, _ in LINKS] + METAL_H + PLATE_M, takes=2, lufs=-23)
def chain_links(rng, k):
    run = links(rng, rate_times(rng, 0.65, 40, 6, 0.6), (-7, 0))
    parts = [(0, run, 0)]
    t, g = rng.uniform(0.8, 0.9), -3
    for i in range(2):
        bunch = [t + x for x in np.sort(rng.uniform(0, 0.14, int(rng.integers(5, 8))))]
        parts.append((0, links(rng, bunch, (-8, 0)), g))
        t += rng.uniform(0.8, 0.95)
        g -= 6
    # the swivel creaks as the hook passes the bottom of each swing, less the second time
    swivel = synth.creak(1.6, env([(0, 38), (1.6, 20)], 1.6), rng, body=[520, 1180, 2050, 3300], q=14, jitter=0.35)
    swivel = swivel * env([(0, 0), (0.25, 1), (0.5, 0), (0.95, 0), (1.15, 0.5), (1.4, 0)], 1.6)
    parts += [(0.85, ck.norm(swivel), -24), (rng.uniform(0.95, 1.05), hook(rng, k + 2, -8), -6)]
    last = [t + rng.uniform(0.1, 0.25), t + rng.uniform(0.4, 0.6), t + rng.uniform(0.8, 1.0)][:2 + k]
    parts.append((0, links(rng, last, (-14, -8), -8), 0))
    return ck.place(parts)


# ---- 7, 8. Fighting with empty hands --------------------------------------------------------------------------------------------

@recipe(L, "bare-swing", "whiff", "An empty-handed swing: a thin, quick whiff of air and the coat sleeve",
        """A fist moves very little air: a short, thin whiff (pink noise through a band that rises to about 1.5 kHz and
        drops away, a quarter of a second) with the coat sleeve rustling under it (the packs' cloth). Small and feeble
        next to the tools' swings.""",
        sources=CLOTH, takes=3, lufs=-24)
def swing_whiff(rng, k):
    Ln = rng.uniform(0.2, 0.27)
    w = ck.whoosh(rng, Ln, 280, rng.uniform(1300, 1700), rng.uniform(0.4, 0.55), 1.8)
    return ck.place([(0, hp(ck.cloth(rng, k, 0.22), 250), -9), (0.03, w, 0)])


@recipe(L, "bare-swing", "sleeve", "An empty-handed swing: a baggy coat sleeve flapping round, a limp fwup",
        """The sleeve, not the fist, makes the sound: a heavy coat sleeve swung round (the packs' cloth, its snap) on a
        slow, low whoosh (it peaks under 700 Hz: a big loose sleeve, not a fast arm), and the loose cuff flapping once or
        twice at the end of the swing as the arm stops short.""",
        sources=CLOTH, takes=3, lufs=-24)
def swing_sleeve(rng, k):
    Ln = rng.uniform(0.32, 0.4)
    w = ck.whoosh(rng, Ln, 110, rng.uniform(500, 680), 0.45, 0.8)
    cuff = flap(rng, 0.12, rng.uniform(20, 28), 300, 2000) * env([(0, 1), (0.12, 0)], 0.12)
    return ck.place([(0, w, 0), (0.02, hp(ck.cloth(rng, k + 1, 0.3), 140), -5), (Ln * 0.62, cuff, -12)])


@recipe(L, "bare-slap", "pat", "An empty hand landing on a hide: a limp, flat slap",
        """A flat thing slapped down (the packs' books slapped flat: the nearest the packs have to a palm), its crack
        taken off (lowpassed, the attack softened: no snap in the wrist), the hide's dull give under it (a soft hit) and a
        little dark squash. Limp.""",
        sources=SLAPS + SOFT_M, takes=3, lufs=-22)
def slap_pat(rng, k):
    key, i = [(SLAPS[0], 0), (SLAPS[0], 1), (SLAPS[1], 0)][k]
    pat = hit_of(key, i, 0.14, -20)
    pat = ck.norm(dsp.shaped(hp(lp(pat, rng.uniform(2800, 3400), 2), 160), [(0, 0.3), (0.003, 1)]))
    give = hit_of(SOFT_M[k], 0, 0.12)
    return ck.place([(0, pat, 0), (0.002, lp(give, 900), -10), (0.01, ck.slurp(rng, 0.08, 180, 800, bubbles=0.0), -18)])


@recipe(L, "bare-slap", "smack", "An empty hand landing on a hide: a soft palm smack and the hand peeling off the tacky hide",
        """Modelled: a palm meeting a hide (a burst of noise in skin's band with a slow, limp rise rather than a crack, and
        the small pocket of air under the cupped palm thumping low), then a beat later the hand peeling off the creature's
        tacky skin with a faint sticky tch. Pathetic.""",
        takes=3, lufs=-22)
def slap_smack(rng, k):
    palm = burst(rng, 0.07, 450, rng.uniform(1800, 2400), 0.003)
    cup = modes(rng.uniform(230, 290), [1, 1.9], [1, 0.3], [0.012, 0.007], 0.06)
    tch = ck.slurp(rng, 0.06, 900, 3000, rise=True, bubbles=0.0)
    tch2 = burst(rng, 0.012, 1500, 5000, 0.001)
    t = rng.uniform(0.12, 0.2)
    return ck.place([(0, palm, 0), (0.001, cup, -10), (0.004, ck.pad(rng, 0.05, 300), -10), (t, tch, -20),
                     (t + 0.03, tch2, -26)])


# ---- 9, 10. A dead crewmate's boots -------------------------------------------------------------------------------------------

BOOT_FLOOR = dict(wood="the car's boards (the packs' real wood knocks with the car floor's hollow under them)",
                  ground="ballast and dirt (a dull pad, stones knocking and a slice of real gravel crunch)")


for mat in ("wood", "ground"):
    @recipe(L, "body-boot", "heel", f"One heavy boot heel thudding down a beat after the body, on {'boards' if mat == 'wood' else 'ballast'}",
            f"""Just the boot: one of the real boot recordings' heels (crew_feet's bank of work-boot heels), driven down with
            the dead leg's weight behind it (a soft low pad), on {BOOT_FLOOR[mat]}. No toe after it: the leg doesn't
            walk, it drops.""",
            sources=BOOTS + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-19)
    def boot_heel(rng, k, m=mat):
        h = heel(rng, k)
        h = ck.tilt(h, lo_db=3, lo_f=250) if m == "wood" else sole(h, 1400)
        return ck.place([(0, h, 0 if m == "wood" else -4), (0, ck.floor(rng, m, k, 1.4, 0.7), -2),
                         (0.002, ck.pad(rng, 0.09, 260), -9)])

    @recipe(L, "body-boot", "dead-leg", f"A dead leg's boot dropping on {'boards' if mat == 'wood' else 'ballast'} and tipping over onto its side",
            f"""The leg's dead weight first (a soft heavy hit, the packs' leather drop), the heel's hard click on top of it,
            on {BOOT_FLOOR[mat]}; then the boot rolls over onto its side, a smaller second knock a tenth of a second
            later (the sole's edge), the leather creaking once.""",
            sources=SOFT + DROP_LEATHER + BOOTS + ck.FLOOR_SOURCES[mat] + JAMB[:1], takes=3, mat=mat, lufs=-19)
    def boot_dead(rng, k, m=mat):
        # the first impact choked short (the leg lies still on it) so the boot tipping over is heard after it
        weight = ck.choke(mix(hit_of(SOFT[(k + 2) % 5], 0, 0.3), hit_of(DROP_LEATHER[0], 0, 0.25) * 0.5), 0.05, 0.035)
        click = sole(heel(rng, k + 1), 1200)
        d = rng.uniform(0.1, 0.15)
        creak = synth.creak(0.12, rng.uniform(50, 80), rng, body=[260, 610, 1150], q=7) * env([(0, 0), (0.03, 1), (0.12, 0)], 0.12)
        # the boot's side on the floor: a short dry thunk on boards (the floor model's knocks run too long for it)
        side = (dsp.room(dsp.vari(hit_of(JAMB[0], 0, 0.12), rng.uniform(-2, 0)), "car", wet=0.12, rng=np.random.default_rng(11))
                if m == "wood" else ck.floor(rng, m, k + 1, 0.6, 0.6))
        tip = mix(ck.norm(side), sole(toe(rng, k), 1000) * 0.5, lp(weight, 600) * 0.3)
        return ck.place([(0, weight, 0), (0, click, -6), (0, ck.choke(ck.floor(rng, m, k, 1.8, 0.5), 0.06, 0.04), -1),
                         (d, tip, -5), (d - 0.04, creak, -30)])


def frame(rng, take, s=1.0):
    """A boot's heel knocking a wooden door frame: a solid post, so a dry, hard wooden knock (the packs' knocks and thunks,
    the post's few short modes) with the heel's click on it, nothing hollow."""
    k = ck.norm(ck.choke(hit_of(JAMB[take % 3], 0, 0.15), 0.02, 0.03))
    k = dsp.vari(k, rng.uniform(1.0, 3.0))
    post = ck.hollow(rng, [380, 690, 1120], 0.06, q=9)
    click = sole(heel(rng, take), 1800)
    return mix(ck.norm(hp(k, 150)) * s, post * 0.35 * s, click * 0.4 * s)


@recipe(L, "body-knock", "jamb", "A carried body's boots knocking the door frame: two or three quick knocks",
        """Two or three quick, dry knocks as the dangling boots catch the door frame one after the other (the packs' wooden
        knocks and thunks, a solid post's short ring, a heel's click on each), the second boot a little higher and softer
        than the first: tok-tok, or tok-tok-tok.""",
        sources=JAMB + BOOTS, takes=3, lufs=-21)
def knock_jamb(rng, k):
    gaps = [[0.1], [0.08, 0.1], [0.13, 0.07]][k]
    parts, t = [(0, frame(rng, k), 0)], 0.0
    for i, g in enumerate(gaps):
        t += g * rng.uniform(0.9, 1.1)
        parts.append((t, dsp.vari(frame(rng, k + i + 1, 1.0), 1.0 + i), -3 - 3 * i))
    return ck.space(ck.place(parts), 0.25, 0.1, 5000, ((0.004, 0.5), (0.009, 0.35)), seed=13)


@recipe(L, "body-knock", "scuff", "A carried body's boots knocking and scuffing the door frame, the door rattling in its track",
        """The boots catch the frame (two quick dry knocks, as in 'jamb'), one sole drags down the post after them (a
        short leather-on-wood scrape, stick-slip), the coat brushes the frame, and the car's sliding door rattles loose in
        its track from the knock (a few low, light wooden rattles).""",
        sources=JAMB + BOOTS + CLOTH, takes=3, lufs=-21)
def knock_scuff(rng, k):
    d = rng.uniform(0.08, 0.12)
    drag = ck.friction(rng, rng.uniform(0.12, 0.18), 160, 300, 2500, 0.7, env([(0, 0), (0.02, 1), (0.16, 0)], 0.16))
    rattle = ck.grains(rng, int(rng.integers(4, 7)), 0.12, 250, 900, q=(6, 12), length=(0.02, 0.04), shape=0.0)
    parts = [(0, frame(rng, k + 1), 0), (d, frame(rng, k + 2, 0.8), -3), (d + 0.03, drag, -12),
             (0.05, ck.cloth(rng, k, 0.25), -14), (0.04, ck.norm(rattle), -16)]
    return ck.space(ck.place(parts), 0.25, 0.1, 5000, ((0.004, 0.5), (0.009, 0.35)), seed=13)
