"""K1's odd outcomes heard (note 730): small strange things in the walled towns and the dead villages, there by design
and never explained. Nothing in them hurts anyone, so they're played as plain foley and plain places, and the oddness is
in what happens, not in how it's sounded (the far bark is the one creature-ish sound).

- The council house's bell (place-town.bell): a rope by the door; pulled, the cupola's bell rings and every townsperson
  within 90 m stops and looks up. An old town bell with a crack in it: near and loud, a bit wrong. A cracked bell rings
  short (the crack soaks up the ring), its partials split wide so they beat sour, and the crack's faces clap together
  each cycle while the bell swings wide enough to close them: a buzz in the first half second of every stroke that
  stops dead as the ring dies under it. The rope drawn through its guide, the gudgeons creaking in their bearings and
  the yoke knocking at the top of each swing; heard across the square at night with its stone fronts throwing it back.
- A well (world-odd.well-splash): something small dropped down a village's stone well, the splash 2.8 s later (about
  36 m down). Heard at the mouth: a small plop far down, rung up the shaft. A 1 m shaft rings across its width (a
  flutter between its walls, 6 ms a pass) and down its length (the sound comes back up, goes down off the open mouth
  and up again, 0.21 s a round trip, darker each time); a drip or two falls back after.
- The well gives back (world-odd.coin): an old coin out of the dark onto the stones beside it: a copper coin landing
  on stone, a bounce, spinning down on its edge faster and faster (Euler's disk) and settling flat. Thin and bright.
- Washing the soot off at the well (world-odd.wash): the bucket wound up (the windlass's axle creaking in its posts,
  the rope over the drum), tipped out over hands and face, the water splashing on the stones.
- A tin can on a village road (world-odd.can-kick): walked into and kicked 3-6 m: the kick's tinny clank, the can
  bouncing and rattling over dirt and stones, rolling to a stop.
- Something answers (world-odd.bark): a second or two after the can, from the dark 90 m off away from the line: a
  dog, or something like one. Almost a dog: too deep (a chest twice a farm dog's), too slow, a man's throat in it, a
  cough. Kitbashed under the enemy rules; kept well clear of the plain farm dog (world-dog.bark).

Each cue has two candidates, one from the packs' real recordings (bells, iron, coins, tin, water, creaks, a roar, a
groan) trimmed and shaped to the thing, one modelled (a cracked bell's partials, a plop's bubbles, a coin's plate
modes, a can's shell, a man's throat through a tract longer than his own), so the two compare; the well's shaft, the
square and the fields are the same places under both. Impacts keep under 45% of their energy below
150 Hz (the squish rule).
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.crew_kit import recipe, K, S, R
from recipes.crew_items import hit_of
from recipes.beasts import fry
from recipes.horrors import finish, bend
from recipes.town import cough_once

SR = dsp.SR
TOWN = "place-town"
ODD = "world-odd"


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def unit(x):
    return W.norm(x)


def cut_to(y, length, tail=0.25):
    """Exactly `length` s, the last `tail` faded: a one-shot that stops where it should, its reverb let go gently."""
    return dsp.fade(dsp.fit(np.asarray(y, np.float32), samples(length)), 0.001, tail)


def air(x, metres):
    """`metres` of open air on a damp night: what the air absorbs, more the higher (ISO 9613-1 at 10 C and 70% humidity:
    about 1.3 dB a km at 500 Hz, 3.5 at 1 kHz, 11 at 2 kHz, 37 at 4 kHz), as a zero-phase filter."""
    n = len(x) + samples(0.3)
    m = int(2 ** np.ceil(np.log2(n)))
    f = np.fft.rfftfreq(m, 1 / SR) / 1000
    db = metres / 1000 * (1.6 * f + 1.9 * f ** 2)
    return np.fft.irfft(np.fft.rfft(x, m) * 10 ** (-db / 20), m)[:len(x)].astype(np.float32)


def blur(x, rng, ms=12.0, amount=0.35):
    """Turbulent night air (or a rough wall) over a long path: a few ms of diffuse smear under the direct sound."""
    k = samples(ms / 1000)
    h = rng.standard_normal(k).astype(np.float32) * np.exp(-np.arange(k) / (k / 3)).astype(np.float32)
    w = signal.fftconvolve(x, h / np.sqrt(np.sum(h ** 2)))[:len(x)].astype(np.float32)
    return mix(x * (1 - amount), w * amount)


def village(y, rng, wet=0.12):
    """Out in a dead village at night: a slap off the wellhead's stones beside it and a house wall across the way, then
    the open night."""
    z = y.copy()
    for dt, g in ((0.0031, 0.3), (0.021, 0.15)):
        k = samples(dt)
        z[k:] += y[:-k] * g
    return hp(dsp.room(z, "night", wet=wet, rng=rng), 20)


def rms(x, a, b):
    """The level of x between a and b seconds."""
    return float(np.sqrt(np.mean(x[samples(a):samples(b)] ** 2)) + 1e-9)


def thrown_back(x, rng, taps, top=5000):
    """`x` and what the walls throw back of it: [(delay s, dB, metres further)], each darker and smeared."""
    b = Bus(len(x) / SR + max(d for d, _, _ in taps) + 0.05)
    b.at(0, x)
    for d, db, extra in taps:
        b.at(d, blur(air(lp(x, top, 1), extra), rng, 6, 0.5), db)
    return b.x


# ---- The council house's bell (place-town.bell) -------------------------------------------------------------------------

# The square's stone fronts: the council house's own under the cupola, the far side 40 m off, the two flanks, and the
# far side again (the flutter between the facing fronts).
SQUARE = [(0.031, -9, 10), (0.118, -10, 40), (0.152, -13, 52), (0.205, -14, 70), (0.351, -19, 120)]
BELL_L = (4.2, 3.4, 4.8)         # the takes: three strokes; two and the rope let go; four, the last one weak


def square(y, rng, metres=30.0, wet=0.28):
    """Across a walled town's square at night: `metres` of air up to the cupola, the square's stone fronts throwing it
    back, and the square's short stone reverb (open above, so it doesn't build like a hall)."""
    z = thrown_back(air(y, metres), rng, SQUARE)
    return ck.space(z, 1.2, wet, 3500, ((0.011, 0.35), (0.024, 0.25), (0.043, 0.2)), seed=int(rng.integers(1 << 16)))


def strokes(k):
    """When the bell strikes in each take, how hard, and which lip the clapper hits (the crack is nearer one side, so
    alternate strokes ring a little differently)."""
    return [[(0.32, 1.0, 0), (1.27, 0.92, 1), (2.22, 0.96, 0)],
            [(0.28, 1.0, 1), (1.31, 0.88, 0)],
            [(0.3, 1.0, 0), (1.12, 0.95, 1), (1.94, 0.9, 0), (2.8, 0.55, 1)]][k]


def closings(v, gap):
    """Where and how hard the crack's faces clap shut: each time the bell's motion `v` carries past the gap (`gap` a
    share of its swing just after the strike), a pulse as strong as the excess; none once the ring has died under it.
    The partials beat, so the closings come and go with the beat before they stop."""
    e = lp(np.abs(v), 40, 1) * (np.pi / 2)
    a0 = np.max(e[:samples(0.08)]) + 1e-9
    i = np.where((v[1:] >= gap * a0) & (v[:-1] < gap * a0))[0] + 1
    p = np.zeros(len(v), np.float32)
    p[i] = np.clip(e[i] / a0 - gap, 0, 1) / (1 - gap)
    return p


def buzz(v, grit, gap, modes=None):
    """The crack's buzz: a click at every closing (`grit` the texture each click is cut from: noise, or a real metal
    scrape), rung a little through the bell's upper modes."""
    p = closings(v, gap)
    h = np.exp(-np.arange(samples(0.0012)) / samples(0.0003)).astype(np.float32)
    gate = signal.fftconvolve(p, h)[:len(v)].astype(np.float32)
    g = dsp.fit(grit, len(v)) * gate
    if modes:
        g = mix(g, unit(dsp.resonate(g, modes, q=30)) * 0.4 * np.max(np.abs(g)))
    return hp(g, 1200, 2)


def ringing(rng, k, stroke, creak, knock):
    """A take: the rope drawn down, then each stroke (`stroke(force, side, length)` -> the bell), the gudgeons creaking
    through each swing and the yoke knocking at its top, and after the last the bell swinging to rest on creaks."""
    L = BELL_L[k]
    plan = strokes(k)
    b = Bus(L + 0.5)
    b.at(0.0, creak(0.3, "rope"), -15)
    for j, (t, force, side) in enumerate(plan):
        if j:
            b.at(t - 0.62, creak(0.42, "rope"), -19)
        b.at(t - 0.34, creak(0.3, "gudgeon"), -19)
        b.at(t - rng.uniform(0.015, 0.03), knock(), -16)
        b.at(t, stroke(force, side, L - t + 0.3), 20 * np.log10(force))
    end = plan[-1][0]
    for j in range(2):
        b.at(end + 0.4 + 0.55 * j, creak(0.35 - 0.08 * j, "gudgeon"), -19 - 5 * j)
    y = square(hp(b.x, 120, 2), rng)
    return finish(cut_to(y, L, 0.7), -16, -1.5)


BELL_REC = K("impactBell_heavy")[1]     # the packs' deepest real bell: partials from 240 Hz, two close (390, 414 Hz)
BELL_SEMIS = -3.5                       # down to a town bell's size: its lowest partial near 195 Hz
CLAPPER = K("impactMetal_heavy")        # real iron struck: the clapper's clang on the lip, which the bell's own strike lacks
SCRAPE = S("misc_10")[0]                # a real metal scrape: the grit the crack's faces clatter with
HINGES = R("creak1", "creak2", "creak3")    # real hinges squealing: iron gudgeons turning in their bearings
DOORS = R("doorOpen_1", "doorOpen_2")       # real doors' wood creaking: the rope drawn through its wooden guide
YOKE = K("impactWood_medium")               # a real block of wood knocked: the oak yoke against its stay


def bell_peaks(x, count=10, lo=150, hi=3000):
    """A real bell's strongest partials (Hz, level), measured from its ring past the strike's clang."""
    seg = x[samples(0.1):samples(0.9)]
    m = 2 ** 17
    S_ = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), m))
    f = np.fft.rfftfreq(m, 1 / SR)
    pk, _ = signal.find_peaks(S_, distance=int(6 / (SR / m)))
    pk = [p for p in pk if lo < f[p] < hi]
    top = sorted(pk, key=lambda p: -S_[p])[:count]
    return [(float(f[p]), float(S_[p] / S_[top[0]])) for p in sorted(top)]


@functools.lru_cache(maxsize=1)
def real_bell():
    """The real bell at the town bell's size, choked to a cracked bell's short ring (the crack soaks it up), and its
    partials measured, each given the crack's split (fixed: every take rings the same bell)."""
    x = unit(ck.tilt(ck.align(dsp.vari(ck.get(BELL_REC), BELL_SEMIS)), hi_db=6, hi_f=900))
    rng = np.random.default_rng(731)
    parts = [(f, f * (1 + rng.uniform(0.008, 0.02)), a) for f, a in bell_peaks(x)]
    return ck.choke(x, 0.04, 0.38), parts


def bell_rec(rng, force, side, length):
    """One stroke of the cracked bell from the real one: its recording struck (a harder stroke brighter), the real iron
    clang of the clapper over it, its own partials rung on through doublets of high-Q resonators a crack's split apart
    (so they beat sour, the clapper's side deciding which half sounds more), the lowest ringing longest; the crack's
    buzz is the real scrape's grit, let through each time the bell's own low partials swing past the crack's gap."""
    x, parts = real_bell()
    n = samples(length)
    dry = dsp.fit(lp(x, 2500 + 5000 * force, 1), n)
    clang = ck.choke(dsp.vari(hit_of(CLAPPER[int(rng.integers(5))], 0, 0.3), -rng.uniform(1.5, 3)), 0.012, 0.025)
    clang = dsp.fit(hp(clang, 500, 2), n)
    f0 = parts[0][0]
    ring = np.zeros(n, np.float32)
    low = np.zeros(n, np.float32)
    for i, (f1, f2, a) in enumerate(parts):
        T = float(np.clip(2.6 * (f0 / f1) ** 0.9, 0.35, 2.6))
        w = (1.0, rng.uniform(0.55, 0.8)) if side == 0 else (rng.uniform(0.55, 0.8), 1.0)
        for f_, w_ in ((f1, w[0]), (f2, w[1])):
            r = dsp.resonate(dry, [f_], q=np.pi * f_ * T / 6.91)
            r = (r / (np.max(np.abs(r)) + 1e-9) * a * w_).astype(np.float32)
            ring += r
            if i < 3:
                low += r
    ring = ring * (0.7 * rms(dry, 0.02, 0.25) / rms(ring, 0.3, 0.6))     # under the strike, carrying on past it
    grit = hp(dsp.fit(np.tile(ck.get(SCRAPE), int(length) + 2), n), 1500, 2)
    crack = buzz(low, grit, rng.uniform(0.17, 0.23), [f for f, _, _ in parts[4:]])
    return mix(dry, ring, unit(clang) * 0.45 * force, unit(crack) * 0.3 * force)


@functools.lru_cache(maxsize=8)
def _creak_src(key):
    x = ck.get(key)
    return x[samples(0.42):].copy() if key in DOORS else x     # a door's creak past its latch's clunk


def creak_slice(rng, keys, length, semis, lo=200):
    """A slice of one of the real creaks (a door's past its latch), dropped `semis`, swelling in and out."""
    x = dsp.vari(_creak_src(keys[int(rng.integers(len(keys)))]), semis)
    if len(x) < samples(length) * 1.2:
        x = dsp.stretch(x, samples(length) * 1.2 / len(x))
    a = int(rng.integers(0, len(x) - samples(length)))
    c = unit(hp(x[a:a + samples(length)], lo))
    return c * env([(0, 0), (length * 0.3, 1), (length * 0.7, 0.8), (length, 0)], length)


def creak_rec(rng, length, part):
    """The bell's hangings creaking, from real recordings: a real hinge's squeal dropped seven to ten semitones for the
    iron gudgeons turning in their bearings, or a real door's wood creak dropped a little for the rope drawn through its
    wooden guide."""
    if part == "gudgeon":
        return creak_slice(rng, HINGES, length, -rng.uniform(7, 10))
    return creak_slice(rng, DOORS, length, -rng.uniform(2, 4))


def knock_rec(rng):
    """The yoke knocking against its stay: a real block of wood knocked, dropped a little for oak, choked (it's loaded)."""
    k = ck.align(dsp.vari(ck.get(YOKE[int(rng.integers(5))]), -rng.uniform(0, 2)))
    return unit(ck.choke(hp(k, 160, 2), 0.03, 0.04))


@recipe(TOWN, "bell", "rung",
        "The council house's cracked bell, from a real bell: two to four sour, buzzing strokes across the square",
        """From the packs' real bell (Kenney's heavy bell, its deepest take) dropped three and a half semitones to a town
        bell's size and choked to a cracked bell's short ring; a real iron hit for the clapper's clang on the lip; the
        bell's own partials measured and rung on through pairs of resonators a crack's split apart, so every partial
        beats sour; the crack's buzz is a real metal scrape's grit let through each time the bell's low partials swing
        wide enough to close it, so each stroke starts with a rasp that stops dead as the ring dies. The rope through its
        guide and the gudgeons are real creaks (a door's wood, a hinge dropped for iron), the yoke a real wood knock.
        Heard across the square at night, the stone fronts throwing it back.""",
        sources=[BELL_REC] + CLAPPER + [SCRAPE] + HINGES + DOORS + YOKE, takes=3, lufs=-16, gap=1.0)
def bell_rung(rng, k):
    return ringing(rng, k, lambda f, s, L: bell_rec(rng, f, s, L), lambda L, p: creak_rec(rng, L, p),
                   lambda: knock_rec(rng))


# A cracked bell, modelled: a council house's cupola bell about 60 cm across. Its partials at a minor-third bell's ratios
# (kit.BELL: hum, prime, tierce, quint, nominal...), the tierce pushed sharp toward a sour neutral third and the nominal
# a little over the octave; each split wide by the crack (0.8-2%: it beats at a few hertz, a sour warble), and ringing
# a fifth as long as a sound bell's would.
PRIME = 352.0
CRACK_T60 = [3.0, 2.3, 1.5, 0.9, 1.2, 0.7, 0.6, 0.5, 0.35, 0.28]
CRACK_AMP = [0.55, 0.75, 0.7, 0.3, 1.0, 0.5, 0.4, 0.35, 0.25, 0.18]
SOUR = [1.0, 1.0, 1.03, 0.985, 1.022, 1.0, 1.01, 1.0, 0.99, 1.0]


@functools.lru_cache(maxsize=1)
def town_bell():
    """The one bell every take rings: its partials' exact tuning and split, fixed (the same casting, crack and all)."""
    rng = np.random.default_rng(730)
    parts = []
    for r, T, a, s in zip(kit.BELL, CRACK_T60, CRACK_AMP, SOUR):
        f = PRIME * r * s * (1 + rng.normal(0, 0.003))
        parts.append((f, f * (1 + rng.uniform(0.008, 0.02)), T, a))
    return parts


def bell_model(rng, force, side, length):
    """One stroke of the cracked bell, modelled: its partials (each a doublet, the clapper's side deciding which half
    sounds more), the clang of its high close partials for the first tenth of a second, the clapper's iron clack, and
    the crack's buzz driven by the bell's own low partials."""
    n = samples(length)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float64)
    low = np.zeros(n, np.float64)
    for i, (f1, f2, T, a) in enumerate(town_bell()):
        g = a * force ** (0.5 * kit.BELL[i])
        ph = rng.uniform(0, 2 * np.pi)
        w = (1.0, rng.uniform(0.55, 0.8)) if side == 0 else (rng.uniform(0.55, 0.8), 1.0)
        for f_, w_, T_ in ((f1, w[0], T), (f2, w[1], T * 0.8)):
            p = g * w_ * np.exp(-6.91 * t / T_) * np.sin(2 * np.pi * f_ * t + ph)
            y += p
            if i < 3:
                low += p
    for _ in range(16):
        f = PRIME * np.exp(rng.uniform(np.log(5.5), np.log(17)))
        y += 0.1 * force * np.exp(-t / rng.uniform(0.015, 0.08)) * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28))
    y[:samples(0.001)] *= np.linspace(0, 1, samples(0.001))
    clack = W.body(rng, rng.uniform(2200, 2700), W.PLATE, decay=0.012, contact=0.0003, length=0.06)
    ring = unit(y.astype(np.float32))
    crack = buzz(low.astype(np.float32), synth.noise(length, rng), rng.uniform(0.17, 0.23),
                 [f for f, _, _, _ in town_bell()[5:]])
    return mix(ring, unit(clack) * 0.3 * force, unit(crack) * 0.3 * force)


def creak_model(rng, length, part):
    """Stick-slip in the bell's hangings, modelled: the iron gudgeons turning in their bronze bearings (a dry, low iron
    groan), or the rope drawn through its wooden guide (hemp's quick catches through a board)."""
    if part == "gudgeon":
        rate = env([(0, rng.uniform(25, 35)), (length * 0.5, rng.uniform(60, 90)), (length, rng.uniform(20, 30))], length,
                   "exp")
        c = synth.creak(length, rate, rng, body=[f * rng.uniform(1.6, 2.0) for f in synth.IRON], q=16, jitter=0.35, grit=0.3)
    else:
        rate = env([(0, rng.uniform(40, 60)), (length * 0.4, rng.uniform(120, 160)), (length, rng.uniform(50, 70))], length,
                   "exp")
        c = mix(unit(synth.creak(length, rate, rng, body=[f * 1.3 for f in synth.WOOD], q=12, jitter=0.5, grit=0.5)),
                unit(ck.friction(rng, length, 260, 700, 4000, 0.8)) * 0.4)
    return unit(hp(c, 200)) * env([(0, 0), (length * 0.3, 1), (length * 0.7, 0.8), (length, 0)], length)


def knock_model(rng):
    """The oak yoke knocking against its stay at the top of the swing: a heavy beam's low modes through a soft contact."""
    return unit(hp(W.body(rng, rng.uniform(190, 230), W.BAR, decay=0.05, contact=0.002, length=0.15), 150))


@recipe(TOWN, "bell", "cast",
        "The council house's cracked bell, modelled: two to four sour, buzzing strokes across the square, the yoke creaking",
        """Modelled: a 60 cm town bell's partials at a minor-third bell's ratios (hum near 175 Hz, strike note near 720),
        its tierce pushed sharp toward a sour neutral third; the crack splits every partial wide so each beats at a few
        hertz, rings a fifth as long as a sound bell would, and buzzes: its faces clap together once a cycle while the
        bell swings wide enough to close them, so each stroke starts with a rasp that stops dead as the ring dies. The
        iron clapper's clack and the clang of the high partials; the rope drawn through its guide, the gudgeons creaking
        (stick-slip in iron) and the oak yoke knocking at the top of each swing. Heard across the square at night, the
        stone fronts throwing it back.""",
        takes=3, lufs=-16, gap=1.0)
def bell_cast(rng, k):
    return ringing(rng, k, lambda f, s, L: bell_model(rng, f, s, L), lambda L, p: creak_model(rng, L, p),
                   lambda: knock_model(rng))


# ---- A well (world-odd.well-splash) -------------------------------------------------------------------------------------

DEPTH = 36.0        # metres down to the water: a 2.8 s fall, less the tenth of a second the splash takes to come back up
CROSS = [1.841, 3.054, 3.832, 4.201, 5.318, 5.331, 6.416, 6.706, 7.016, 7.501, 8.015, 8.536]   # a round shaft's cross-modes
WELLS = (0.5, 0.56, 0.46)    # each take a different village's well: its shaft's radius (m)
SPLASH_L = (1.6, 1.4, 1.8)


def shaft(x, rng, radius, depth=DEPTH):
    """Heard at a stone well's mouth from the water `depth` m down. Across the shaft: its cross-modes (sound near each
    one's cutoff climbs the shaft slowly, so it lingers there: the well's hollow note, about 200, 330 and 420 Hz for a
    metre across) and the flutter between its walls (the width's round trip, 6 ms). Down its length: the open mouth
    throws the low end back down, off the water and up again, 0.21 s a trip, darker each time. Rough stone all the way
    down: a diffuse tail."""
    c = 343.0
    modes = [j * c / (2 * np.pi * radius) for j in CROSS]
    hollow = dsp.resonate(x, modes, q=30, gains=[1 / (1 + 0.2 * i) for i in range(len(modes))])
    p = samples(2 * radius / c)
    a = np.zeros(p + 1)
    a[0], a[p] = 1.0, -0.55
    flutter = signal.lfilter([1.0], a, lp(x, 5000, 1)).astype(np.float32)
    y = mix(unit(lp(x, 4000, 1)) * 0.3, unit(hollow) * 0.5, unit(flutter) * 0.5)
    trip = 2 * depth / c
    b = Bus(len(y) / SR + 3 * trip + 0.1)
    b.at(0, y)
    z = y
    for i in range(1, 4):
        z = blur(lp(z, 700, 2), rng, 10, 0.6)
        b.at(i * trip, z, -7 * i)
    return ck.space(b.x, 1.5, 0.4, 2500, ((0.0061, 0.4), (0.0127, 0.3), (0.019, 0.2)), seed=int(rng.integers(1 << 16)))


def plink(rng, size=1.0):
    """One drop into water: the splash's tick, then the small bubble it drags under ringing (700-1800 Hz) and rising
    only a little as it reaches the surface."""
    tick = bp(synth.noise(0.004, rng), 1500, 8000) * env([(0, 1), (0.004, 0)], 0.004)
    b = synth.bubble(rng.uniform(700, 1800) / size, length=0.12, rise=rng.uniform(0.08, 0.3))
    return mix(unit(tick) * 0.4, np.concatenate([np.zeros(samples(0.003), np.float32), b]))


def plop_model(rng, size=1.0):
    """A small thing hitting water at 27 m/s, modelled: the crack of the impact, the cavity it drags down pinching off
    into a big bubble that rings and hardly rises (Minnaert, 380-620 Hz) with smaller ones breaking off it, the water
    closing over with a wet suck, and the crown's droplets falling back a tenth to a third of a second later. A cluster,
    never one clean bubble (a lone rising ring is a cartoon bloop)."""
    f0 = rng.uniform(380, 620) / size
    crack = bp(synth.noise(0.012, rng), 1500, 9000) * env([(0, 1), (0.012, 0)], 0.012, "exp")
    slap = bp(synth.noise(0.07, rng), 300, 3500) * env([(0, 0), (0.002, 1), (0.07, 0)], 0.07, "exp")
    parts = [(0.0, unit(crack), -4), (0.001, unit(slap), -6),
             (0.012, synth.bubble(f0, length=0.14, rise=rng.uniform(0.04, 0.12)), 0),
             (0.01, ck.slurp(rng, rng.uniform(0.07, 0.1), 250, rng.uniform(900, 1300), bubbles=0), -8)]
    for _ in range(int(rng.integers(2, 5))):
        parts.append((rng.uniform(0.015, 0.06), synth.bubble(f0 * rng.uniform(1.8, 3.5), length=0.05,
                                                             rise=rng.uniform(0.05, 0.3)), rng.uniform(-14, -6)))
    for _ in range(int(rng.integers(2, 4))):
        parts.append((rng.uniform(0.1, 0.35), plink(rng, rng.uniform(0.5, 0.9)), rng.uniform(-18, -11)))
    return unit(ck.place(parts))


def drips_model(rng, t0, count):
    """A drip or two after: water off the shaft's wet stones into the well (a splash tick and a small bubble's plink)."""
    return [(t0 + rng.uniform(0, 0.5) + 0.35 * i, plink(rng, rng.uniform(0.7, 1.1)), rng.uniform(-16, -10))
            for i in range(count)]


WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")    # real splashes: a foot into a puddle
STREAM = S("loop_water_02")[0]       # real water pouring and trickling: its gloops and drips
GURGLE = S("loop_machine_02")[0]     # real water sloshing in a drum: its gurgles


@functools.lru_cache(maxsize=4)
def _water(key):
    """A real water recording with its steady bed gated out, and the hits in it."""
    x = ck.denoise(ck.get(key), over=2.5, floor=0.04)
    return x, ck.hits(x, floor_db=-16, gap=0.05)


def water_hit(rng, key, length, semis):
    """One of a real water recording's hits (a gloop, a drip, a splash), cut to `length` and varispeeded."""
    x, h = _water(key)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    return unit(dsp.vari(ck.cut(x, a - samples(0.002), a + samples(length), 0.001, length * 0.5), semis))


def plop_rec(rng, k):
    """A small thing into the water, from real water: a real splash's front (a foot into a puddle, cut to its first
    burst and brightened for something small and fast), a real gloop out of the packs' pouring water pitched up for the
    cavity's bubble, a slice of a real gurgle's smaller bubbles, and real drips for the crown's droplets falling back."""
    splash = hp(water_hit(rng, WET[k % 3], 0.05, rng.uniform(1, 3)), 700, 2)
    parts = [(0.0, splash, -3), (0.006, water_hit(rng, STREAM, 0.16, rng.uniform(5, 8)), 0),
             (0.01, lp(water_hit(rng, GURGLE, 0.14, rng.uniform(3, 5)), 4000), -9)]
    for _ in range(int(rng.integers(2, 4))):
        parts.append((rng.uniform(0.1, 0.35), water_hit(rng, STREAM, 0.05, rng.uniform(9, 12)), rng.uniform(-18, -11)))
    return unit(ck.place(parts))


def drips_rec(rng, t0, count):
    """A drip or two after, real: the pouring water's small drips, pitched up."""
    return [(t0 + rng.uniform(0, 0.5) + 0.35 * i, water_hit(rng, STREAM, 0.06, rng.uniform(8, 12)), rng.uniform(-16, -10))
            for i in range(count)]


def down_the_well(rng, k, plop, drips):
    """A take: the plop far down and a drip or two after it, all rung up the shaft of this take's well."""
    L = SPLASH_L[k]
    y = ck.place([(0.0, plop, 0)] + drips(rng, 0.55, (1, 2, 2)[k]), L + 0.2)
    return cut_to(shaft(y, rng, WELLS[k]), L, 0.4)


@recipe(ODD, "well-splash", "plunk",
        "Something dropped down a stone well, modelled: a small plop far down, rung hollow up the shaft, a drip after",
        """Modelled: a small thing hitting the water 36 m down at 27 m/s: the crack of it, the cavity pinching off into a
        big bubble ringing at 380-620 Hz with smaller ones breaking off it, the water closing over, the crown's droplets
        falling back; a drip or two off the wet stones after. Heard at the mouth, up the shaft: its cross-modes ring a
        hollow note (about 200, 330, 420 Hz for a metre-wide shaft), the walls flutter (6 ms a pass), and the low end
        comes back up two or three times, 0.21 s apart and darker each time, the open mouth throwing it back down.""",
        takes=3, lufs=-20)
def well_plunk(rng, k):
    return down_the_well(rng, k, plop_model(rng, rng.uniform(0.9, 1.15)), drips_model)


@recipe(ODD, "well-splash", "gloop",
        "Something dropped down a stone well, from real water: a small plop far down, rung hollow up the shaft, a drip",
        """From the packs' real water: a real splash's front (a foot into a puddle, cut to its first burst and brightened
        for something small and fast), a real gloop out of the pouring water pitched up for the bubble it drags down, a
        slice of a real gurgle's smaller bubbles, real drips for the droplets falling back and a drip or two after.
        Heard at the mouth, 36 m up the shaft: its cross-modes ring a hollow note, the walls flutter, and the low end
        comes back up two or three times, 0.21 s apart, darker each time.""",
        sources=WET + [STREAM, GURGLE], takes=3, lufs=-20)
def well_gloop(rng, k):
    return down_the_well(rng, k, plop_rec(rng, k), drips_rec)


# ---- The well gives back (world-odd.coin) -------------------------------------------------------------------------------

DISC = [1.0, 1.73, 2.33, 3.91, 4.11]      # a free thin disc's modes: (2,0), (0,1), (3,0), (1,1), (4,0)


def spin_times(rng, start, length, r0, r1):
    """The coin rolling round on its rim as it goes down (Euler's disk): its rim taps the stone faster and faster, the
    rate climbing steeply at the end (`r0` to `r1` a second), until it drops flat."""
    ts, t = [], start
    while t < start + length:
        u = (t - start) / length
        r = min(r1, r0 / np.sqrt(max(1 - u, 1e-3)))
        ts.append((t, u))
        t += (1 / r) * rng.uniform(0.85, 1.15)
    return ts


def coin_ring(f1, x, tau):
    """A copper coin's ring: `x` (its taps) through its first few modes, ringing `tau` s (a coin on stone, damped)."""
    fs = [f1 * r for r in DISC if f1 * r < SR * 0.45]
    y = dsp.resonate(x, fs, q=np.pi * f1 * tau, gains=[1.0, 0.7, 0.5, 0.35, 0.3][:len(fs)])
    return y.astype(np.float32)


def stone_tick(rng, hard=1.0):
    """The stone's part in a tap: a tick off granite's small hard grain."""
    return synth.click(rng.uniform(2500, 5000), q=rng.uniform(3, 6), length=0.006, rng=rng) * hard


def coin_model(rng, k):
    """The coin, modelled: an old copper coin (a big 3.5 cm penny, its first mode near 4.6 kHz, the disc's others over
    it, damped by the stone) landing, a bounce or two, rolling down on its rim faster and faster, and dropping flat (the
    ring choked). Returns the coin and when it lies flat."""
    f1 = rng.uniform(4200, 5000)
    taps = np.zeros(samples(1.5), np.float32)
    ticks = []

    def tap(t, a, hard=1.0):
        taps[samples(t)] += a
        ticks.append((t, stone_tick(rng, hard), 20 * np.log10(a + 1e-6) - 4))

    t, gap, a = 0.0, rng.uniform(0.07, 0.11), 1.0
    for _ in range((2, 1, 2)[k] + 1):
        tap(t, a)
        t += gap
        gap *= rng.uniform(0.45, 0.6)
        a *= rng.uniform(0.45, 0.6)
    spin = (0.55, 0.7, 0.35)[k]
    for ts, u in spin_times(rng, t, spin, rng.uniform(12, 18), rng.uniform(90, 140)):
        tap(ts, rng.uniform(0.6, 1.0) * (0.12 + 0.2 * u), 0.3)
    flat = t + spin
    tap(flat, 0.35, 0.8)
    ring = ck.choke(coin_ring(f1, taps, rng.uniform(0.06, 0.1)), flat + 0.004, 0.005)
    # rolling on the rim: a fine grinding hiss between the taps, quickening with them
    n = len(taps)
    whirr = bp(synth.noise(n / SR, rng), 2500, 9000) * dsp.fit(env([(0, 0), (t, 0), (t + spin * 0.8, 0.3), (flat, 0.5),
                                                                  (flat + 0.003, 0), (n / SR, 0)], n / SR), n)
    y = mix(unit(ring), unit(whirr) * 0.05, ck.place(ticks, n / SR) * 0.3)
    return hp(y, 400, 2), flat


DROP = S("items_01")[0]              # a real coin dropped on a hard floor: its strike and two bounces, ringing near 8 kHz
RATTLE = S("items_02")[0]            # a real coin let fall: small clinks, a strike and a rattle as it spins down
COINS = R("handleCoins")[0]          # real coins handled: single clean clinks, ringing near 5 and 8.6 kHz
STONES = S("stones_01", "stones_02", "stones_03")    # real stones knocked: the granite under it


def coin_bit(key, a, b, semis=0.0, lo=600):
    """A stretch of a real coin recording (seconds), varispeeded, high-passed clear of the hand and the floor's thump."""
    x = ck.get(key)
    return unit(hp(dsp.vari(ck.cut(x, samples(a), samples(b), 0.001, 0.01), semis), lo, 2))


def clinks(rng, key, count):
    """Single real coin clinks cut out of a recording, each choked short (a coin's rim tapping as it rolls)."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-30, gap=0.02)
    out = []
    for _ in range(count):
        s = h[int(rng.integers(len(h)))][0]
        c = dsp.vari(ck.cut(x, s - samples(0.001), s + samples(0.05), 0.0005, 0.02), rng.uniform(-1, 1.5))
        out.append(unit(ck.choke(hp(c, 1500, 2), 0.008, 0.012)))
    return out


def grain(rng):
    """A tick of real stone: one small hit out of the packs' knocked stones."""
    x = ck.get(STONES[int(rng.integers(3))])
    h = ck.hits(x, floor_db=-20, gap=0.02)
    s = h[int(rng.integers(len(h)))][0] if h else 0
    return unit(hp(ck.cut(x, s, s + samples(0.02), 0.0005, 0.01), 1500, 2))


def coin_rec(rng, k):
    """The coin, from real coins: a real coin's strike on a hard floor (its ring near 8 kHz) and its bounce, a real
    stone's tick under the strike; then it spins down, single real clinks fired at the quickening rate of a coin rolling
    round on its rim and the real coin's own rattle where the taps run together, drawn faster and higher; a last clink
    choked as it drops flat. Returns the coin and when it lies flat."""
    semis = rng.uniform(-1, 1)
    if k == 0:
        parts = [(0.0, coin_bit(DROP, 0.015, 0.16, semis), 0), (0.0, grain(rng), -12),
                 (0.15, coin_bit(DROP, 0.215, 0.3, semis), -5)]
        spin_at, taps = 0.25, RATTLE
    elif k == 1:
        parts = [(0.0, coin_bit(RATTLE, 0.175, 0.2, semis), 0), (0.0, grain(rng), -10),
                 (0.09, coin_bit(DROP, 0.3, 0.38, semis), -8)]
        spin_at, taps = 0.15, COINS
    else:
        parts = [(0.0, coin_bit(DROP, 0.015, 0.11, semis), 0), (0.0, grain(rng), -12)]
        spin_at, taps = 0.12, DROP
    spin = (0.5, 0.65, 0.35)[k]
    cs = clinks(rng, taps, 48)
    whirr_at = spin_at + spin * 0.7
    for i, (t, u) in enumerate(spin_times(rng, spin_at, spin, rng.uniform(12, 16), rng.uniform(90, 130))):
        if t < whirr_at:
            parts.append((t, cs[i % len(cs)], -13 + 6 * u + rng.uniform(-3, 0)))
    w = spin_at + spin - whirr_at
    r = coin_bit(RATTLE, 0.2, 0.2 + w * 1.3, semis + 1, lo=900)
    r = bend(r, env([(0, 0), (len(r) / SR, 4)], len(r) / SR))      # drawn faster and higher as it goes down
    parts.append((whirr_at, unit(dsp.fit(r, samples(w))) * env([(0, 0.5), (w * 0.7, 1), (w, 0.9)], w), -9))
    flat = spin_at + spin
    parts.append((flat, ck.choke(clinks(rng, COINS, 1)[0], 0.003, 0.004), -6))
    return hp(ck.place(parts, flat + 0.3), 400, 2), flat


def by_the_well(y, flat, rng):
    """Beside the wellhead (a slap off its stones, the night), cut a fifth of a second after the coin lies flat."""
    return cut_to(village(y, rng, 0.08), flat + 0.2, 0.12)


@recipe(ODD, "coin", "spin",
        "The well gives back, modelled: an old copper coin landing on stone, a bounce, spinning down on its rim, flat",
        """Modelled: an old copper coin (a big 3.5 cm penny, its first mode near 4.6 kHz and a thin disc's others over it,
        damped by the stone) landing on the wellhead's stones with a tick of the granite under it, a bounce or two each
        sooner and softer, then rolling round on its rim as it goes down (Euler's disk: its rim tapping the stone faster
        and faster, 12 a second climbing past 100, a fine grinding hiss between) until it drops flat and the ring is
        choked. Nothing under 400 Hz.""",
        takes=3, lufs=-20)
def coin_spin(rng, k):
    return by_the_well(*coin_model(rng, k), rng)


@recipe(ODD, "coin", "clink",
        "The well gives back, from real coins: a coin striking the stones, a bounce, rattling down on its rim, flat",
        """From the packs' real coins: a real coin's strike on a hard floor and its bounce (sfx_100 items_01, ringing near
        8 kHz), a real stone's tick under it; then it spins down: single real clinks (cut from the coin recordings)
        fired at the quickening rate of a coin rolling round on its rim, and where the taps run together a real coin's
        rattle (items_02) drawn faster and higher; a last clink choked as it drops flat. High-passed clear of the hand
        and the floor.""",
        sources=[DROP, RATTLE, COINS] + STONES, takes=3, lufs=-20)
def coin_clink(rng, k):
    return by_the_well(*coin_rec(rng, k), rng)


# ---- Washing the soot off at the well (world-odd.wash) ------------------------------------------------------------------

WASH_L = 2.0
# Two ways of it in two seconds: (wind until, bucket on the rim at, [(pour from, pour to)], set down at)
WASHES = [(0.55, 0.62, [(0.78, 1.6)], 1.72),
          (0.38, 0.45, [(0.58, 0.82), (0.98, 1.62)], 1.75)]


def wash_take(rng, k, wind, knock, slosh, pour, pat, drips):
    """A take: the bucket wound up the last of the way (the windlass turning once or twice), knocked up onto the rim and
    slopping, tipped out over hands and face (one long pour, or a splash into cupped hands and then the rest over the
    head), hands rubbing the face in the stream, the bucket set back down, dripping."""
    up, rim, pours, down = WASHES[k]
    parts = [(0.0, wind(up), -2), (rim, knock(1.0), -4), (rim + 0.02, slosh(0.35), -8)]
    for a, b in pours:
        parts.append((a, pour(b - a), 0))
        for t in np.sort(rng.uniform(a + 0.1, b - 0.05, int(rng.integers(2, 4)))):
            parts.append((float(t), pat(), rng.uniform(-12, -7)))
    parts.append((down, knock(0.6), -9))
    parts += drips(pours[-1][1])
    return cut_to(village(ck.place(parts, WASH_L + 0.3), rng, 0.1), WASH_L, 0.25)


def pour_shape(length):
    """A bucket tipped: the water gushing out over the lip, running, and thinning to a trickle as it empties."""
    return env([(0, 0), (0.06, 1), (length * 0.55, 0.85), (length * 0.85, 0.35), (length, 0)], length)


# From real recordings.
ROPE = R("handleSmallLeather2")[0]          # a real leather strap dragged: the rope rasping over the drum
BUCKET = K("impactWood_light")              # real light wood knocked: the stave bucket on the wellhead's stones


def wind_rec(rng, length):
    """The windlass wound, real: the axle creaking in its posts once a turn (a slice of a real door's wood creak, dropped
    a little for the bigger timber), the rope rasping over the drum (a real leather strap dragged, dropped for hemp)."""
    turn = rng.uniform(0.32, 0.4)
    parts = []
    t = 0.0
    while t < length:
        parts.append((t, creak_slice(rng, DOORS, min(turn * 0.7, length - t + 0.05), -rng.uniform(3, 6), 180), 0))
        t += turn * rng.uniform(0.9, 1.1)
    rope = dsp.vari(ck.get(ROPE), -rng.uniform(2, 4))
    rope = unit(hp(dsp.fit(dsp.stretch(rope, length / (len(rope) / SR) * 1.1), samples(length)), 300, 2))
    parts.append((0.0, rope * env([(0, 0), (0.05, 1), (length * 0.8, 0.8), (length, 0)], length), -8))
    return ck.place(parts, length + 0.1)


def knock_rec_bucket(rng, force):
    """The bucket set on the wellhead: a real light wood knock dropped a little for its staves and the water in it."""
    k = ck.align(dsp.vari(ck.get(BUCKET[int(rng.integers(5))]), -rng.uniform(1, 3)))
    return unit(ck.choke(hp(k, 180, 2), 0.02 + 0.02 * force, 0.03)) * force


def slosh_rec(rng, length):
    """The water slopping in the bucket: a real gurgle (the packs' water sloshing in a drum), its hiss gated out."""
    return lp(water_hit(rng, GURGLE, length, rng.uniform(-1, 2)), 5000)


def pour_rec(rng, length):
    """The bucket tipped over hands and face, real: the packs' pouring water dropped a little for a bucket's weight of
    it, and real splashes (a foot into a puddle) as it comes down on the stones."""
    x = ck.get(STREAM)
    a = int(rng.integers(0, len(x) - samples(length * 1.2) - 1))
    p = unit(dsp.vari(x[a:a + samples(length * 1.2)], -rng.uniform(1, 3)))
    p = dsp.fit(p, samples(length)) * pour_shape(length)
    parts = [(0.0, p, 0)]
    for t in np.sort(rng.uniform(0.02, length * 0.8, int(rng.integers(3, 6)))):
        parts.append((float(t), water_hit(rng, WET[int(rng.integers(3))], 0.12, rng.uniform(-3, 0)), rng.uniform(-9, -3)))
    return ck.place(parts, length + 0.15)


def pat_rec(rng):
    """A wet hand on a wet face: a real splash's first burst, pitched up and short."""
    return water_hit(rng, WET[int(rng.integers(3))], 0.06, rng.uniform(1, 4))


def drips_after_rec(rng, end):
    """Water running off and dripping onto the stones: real drips out of the packs' pouring water."""
    return [(end + rng.uniform(0.03, 0.4), water_hit(rng, STREAM, 0.05, rng.uniform(5, 10)), rng.uniform(-15, -9))
            for _ in range(int(rng.integers(3, 6)))]


@recipe(ODD, "wash", "bucket",
        "Washing the soot off at the well, from real recordings: the bucket wound up, knocked onto the rim, tipped out",
        """From the packs' real recordings: the windlass wound the last turn or two (a real door's wood creak, dropped a
        little, once a turn for the axle in its posts; a real leather strap dragged and dropped for the rope rasping
        over the drum), the stave bucket knocked onto the wellhead (a real light wood knock) and slopping (a real
        gurgle), tipped out over hands and face (the packs' real pouring water, gushing and thinning as it empties) with
        real splashes as it comes down on the stones, wet hands on a wet face, the bucket set back down, dripping. Two
        seconds, out in a dead village at night.""",
        sources=DOORS + [ROPE] + BUCKET + [GURGLE, STREAM] + WET, takes=2, lufs=-20)
def wash_bucket(rng, k):
    return wash_take(rng, k, lambda L: wind_rec(rng, L), lambda f: knock_rec_bucket(rng, f), lambda L: slosh_rec(rng, L),
                     lambda L: pour_rec(rng, L), lambda: pat_rec(rng), lambda e: drips_after_rec(rng, e))


# Modelled.
def wind_model(rng, length):
    """The windlass wound, modelled: stick-slip in the oak axle where it turns in its posts, swelling once a turn as the
    crank comes over the top and the load bears, and the rope's fibres catching over the drum."""
    turn = rng.uniform(0.32, 0.4)
    t = np.arange(samples(length)) / SR
    load = 0.35 + 0.65 * np.sin(np.pi * t / turn) ** 2
    rate = 40 + 90 * load
    c = synth.creak(length, rate, rng, body=[f * rng.uniform(0.8, 0.95) for f in synth.WOOD], q=16, jitter=0.4, grit=0.4)
    rope = ck.friction(rng, length, 320, 500, 4000, 0.8)
    sh = env([(0, 0), (0.05, 1), (length * 0.8, 0.8), (length, 0)], length)
    return mix(unit(hp(c, 150)) * load * sh, unit(rope) * 0.3 * sh)


def knock_model_bucket(rng, force):
    """The bucket on the wellhead, modelled: a stave bucket's hollow, heavy with water, knocked on stone."""
    return unit(hp(ck.hollow(rng, [rng.uniform(250, 300), rng.uniform(480, 560), rng.uniform(900, 1050)], 0.1, q=5,
                             hit=0.002), 160, 2)) * force


def slosh_model(rng, length):
    """The water slopping in the bucket, modelled: a wet suck opening and closing, a few low bubbles in it."""
    return unit(hp(ck.slurp(rng, length, 250, rng.uniform(900, 1400), bubbles=0.35), 150))


def pour_model(rng, length):
    """The bucket tipped over hands and face, modelled: a stream falling half a metre: the sheet of water's rush (pink
    noise, 300 Hz-7 kHz), the bubbles it drives into the pool on the stones (hundreds a second, 300 Hz-4 kHz), and its
    spatter flying off the hands and the stones (fine ticks)."""
    sh = pour_shape(length)
    n = samples(length)
    surge = np.clip(0.8 + 3 * lp(rng.standard_normal(n).astype(np.float32), 6), 0.3, 1.3)    # the stream's own unevenness
    rush = dsp.fit(bp(synth.noise(length, rng, "pink"), 300, 7000), n) * surge
    fizz = synth.bubbles(length, 380 * sh + 20, 300, 4000, rng, rise=(0.05, 0.4))
    spatter = synth.crackle(length, 300 * sh + 10, rng, size=(0.0003, 0.0015), hi=1800)
    return mix(unit(rush) * sh * 0.5, unit(fizz) * 0.6, unit(spatter) * sh * 0.3)


def pat_model(rng):
    """A wet hand on a wet face, modelled: skin meeting skin through water, a quick wet slap."""
    return unit(hp(kit.slap(rng, rng.uniform(0.05, 0.08), rng.uniform(1800, 2800), 0.7), 300))


def drips_after_model(rng, end):
    """Water running off and dripping onto the stones and into the puddle there (plinks and stone ticks)."""
    out = []
    for _ in range(int(rng.integers(3, 6))):
        d = plink(rng, rng.uniform(0.6, 1.0)) if rng.random() < 0.6 else W.drip(rng, "stone")
        out.append((end + rng.uniform(0.03, 0.4), d, rng.uniform(-16, -9)))
    return out


@recipe(ODD, "wash", "stream",
        "Washing the soot off at the well, modelled: the windlass creaking, the bucket on the rim, a stream over hands",
        """Modelled: the windlass wound the last turn or two (stick-slip in the oak axle in its posts, swelling once a
        turn as the load bears; the rope's fibres catching over the drum), the stave bucket knocked onto the wellhead
        (its hollow, heavy with water) and slopping (a wet suck, low bubbles), tipped out over hands and face (a stream
        falling half a metre: the sheet of water's rush, the bubbles it drives into the puddle on the stones, its spatter
        off the hands), wet hands on a wet face, the bucket set back down, plinks and drips after. Two seconds, out in a
        dead village at night.""",
        takes=2, lufs=-20)
def wash_stream(rng, k):
    return wash_take(rng, k, lambda L: wind_model(rng, L), lambda f: knock_model_bucket(rng, f),
                     lambda L: slosh_model(rng, L), lambda L: pour_model(rng, L), lambda: pat_model(rng),
                     lambda e: drips_after_model(rng, e))


# ---- A tin can kicked (world-odd.can-kick) ------------------------------------------------------------------------------

def can_path(rng, k):
    """Where the can comes down: kicked 3-6 m, it lands after a quarter of a second, bounces twice or three times (each
    lower and sooner), then rolls over the dirt slowing to a stop and rocks back once or twice. Returns the hits
    [(t, force)], when the roll starts and how long it runs."""
    land = rng.uniform(0.2, 0.3)
    hits, t, gap, f = [(land, 1.0)], land, rng.uniform(0.12, 0.17), 0.6
    for _ in range((2, 3, 2)[k]):
        t += gap
        hits.append((t, f))
        gap *= rng.uniform(0.5, 0.65)
        f *= rng.uniform(0.5, 0.65)
    roll = (0.75, 0.6, 1.0)[k] * rng.uniform(0.9, 1.1)
    return hits, t + 0.03, roll


def roll_times(rng, start, length, rev0):
    """A can rolling to a stop: its seam (or a dent) knocking the ground once a turn, the turns slowing from `rev0` a
    second to nothing."""
    ts, t = [], start
    while t < start + length:
        u = (t - start) / length
        ts.append((t, u))
        t += 1 / max(rev0 * (1 - u) ** 0.7, 2.5)
    return ts


def can_kick(rng, k, kick, clank, roll, rock):
    """A take: the boot into the can, its clanks as it lands and bounces, the roll over the road, a rock or two at the
    end; out on a village road at night."""
    hits, start, length = can_path(rng, k)
    parts = [(0.0, kick(), 0)]
    for t, f in hits:
        parts.append((t, clank(f), 20 * np.log10(f) - 1))
    parts.append((start, roll(length), -4))
    end = start + length
    for j in range((2, 1, 2)[k]):
        parts.append((end + 0.07 + 0.11 * j, rock(0.5 - 0.2 * j), -14 - 4 * j))
    L = end + 0.5
    return cut_to(village(hp(ck.place(parts, L + 0.3), 130, 2), rng, 0.1), L, 0.3)


# Modelled: a tinplate can, 7.5 cm across and 11 cm tall, empty and open at the top.
def can_body(rng, contact, force=1.0, decay=0.22):
    """The can struck: its thin shell's dense modes (from about 420 Hz up past 7 kHz), its bottom's 'ting' (a disc near
    1.7 kHz), and the air in it (open at one end: a quarter wave near 660 Hz)."""
    shell = W.sheet(rng, f1=rng.uniform(400, 450), fmax=7500, decay=decay, contact=contact, density=0.6, tilt=0.22)
    cap = W.body(rng, rng.uniform(1600, 1800), W.PLATE, decay=0.07, contact=contact, count=8)
    y = mix(unit(shell), unit(cap) * 0.45)
    y = mix(y, unit(dsp.resonate(y, [rng.uniform(630, 690)], q=8)) * 0.25)
    return unit(hp(y, 300, 2)) * force


def kick_model(rng):
    """The boot into the can: a leather toe (a soft 2 ms contact, so only the shell's lower modes) and the dull weight
    of the foot, a little grit scuffed off the road."""
    toe = ck.choke(can_body(rng, 0.0018, 1.0, 0.18), 0.03, 0.04)
    foot = W.knock(rng, rng.uniform(130, 170), 0.1, 0.3)
    scuff = ck.grains(rng, 10, 0.05, 900, 5000)
    return ck.place([(0.0, toe, 0), (0.0, unit(hp(foot, 110)), -12), (0.0, unit(scuff), -16)])


def clank_model(rng, force):
    """The can coming down on the road's stones: a hard tinny clank (steel on stone, a quarter-millisecond contact),
    choked a little by the ground it lies on."""
    return ck.choke(can_body(rng, 0.00025, force), 0.04, 0.08)


def roll_model(rng, length):
    """Rolling over dirt and stones, modelled: the shell grinding on grit (friction rung through its modes), its seam
    knocking once a turn as the turns slow, small stones flicked against it (bright ticks of the shell)."""
    n = samples(length)
    grind = ck.friction(rng, length, 220, 400, 5000, 0.9, env([(0, 1), (length * 0.7, 0.6), (length, 0)], length))
    modes = [rng.uniform(400, 450) * r for r in (1.0, 1.6, 2.3, 3.1, 4.4)]
    grind = mix(unit(grind) * 0.4, unit(dsp.resonate(grind, modes, q=12)) * 0.6)
    parts = [(0.0, grind, -6)]
    for t, u in roll_times(rng, 0.0, length, rng.uniform(9, 12)):
        parts.append((t, ck.choke(can_body(rng, 0.0004, 1.0, 0.1), 0.01, 0.02), -8 - 8 * u + rng.uniform(-3, 0)))
    for t in W.poisson(length * 0.8, 9, rng):
        parts.append((t, ck.choke(can_body(rng, 0.0002, 1.0, 0.08), 0.005, 0.01), rng.uniform(-20, -12)))
    return dsp.fit(ck.place(parts, length + 0.2), n + samples(0.2))


def rock_model(rng, force):
    """The can rocking back on the bump it stopped against: a small soft knock of the shell."""
    return ck.choke(can_body(rng, 0.0008, force, 0.1), 0.01, 0.03)


@recipe(ODD, "can-kick", "tin",
        "A tin can kicked along a village road, modelled: a tinny clank, bouncing, rattling over the stones, rolling still",
        """Modelled: an empty tinplate can (7.5 cm across): its thin shell's dense modes from about 420 Hz up, its
        bottom's ting near 1.7 kHz and the air in it (a quarter wave near 660 Hz). The boot's leather toe into it (a soft
        contact: a dull tonk with the foot's weight and a little grit), then it comes down on the road a quarter of a
        second later with a hard tinny clank, bounces two or three times lower and sooner, and rolls: grinding on the
        grit, its seam knocking once a turn as it slows, small stones flicked against it; it rocks back once or twice and
        stops. Out on a village road at night.""",
        takes=3, lufs=-18)
def can_tin(rng, k):
    return can_kick(rng, k, lambda: kick_model(rng), lambda f: clank_model(rng, f), lambda L: roll_model(rng, L),
                    lambda f: rock_model(rng, f))


# From real recordings.
TIN = K("impactTin_medium")                    # real tin struck: a tin box's dull knock
POTS = R("metalPot1", "metalPot2", "metalPot3")  # real metal pots dropped, clanging and bouncing
GRAVEL = S("footstep_01", "footstep_02")       # real boots on gravel


def tin_rec(rng, semis=(3, 5), tau=0.04):
    """A real tin knock pitched up for a small can, choked."""
    x = hit_of(TIN[int(rng.integers(5))], 0, 0.2)
    return unit(ck.choke(hp(dsp.vari(x, rng.uniform(*semis)), 300, 2), 0.01, tau))


def pot_rec(rng, tau=0.05):
    """One of the real pots' clangs, pitched up six to eight semitones from a pot to a tin can (thinner, smaller), choked
    as the can lies on the road."""
    j = int(rng.integers(3))
    x = hit_of(POTS[j], int(rng.integers(4)), 0.3, -20)
    return unit(ck.choke(hp(dsp.vari(x, rng.uniform(6, 8)), 400, 2), 0.02, tau))


def kick_rec(rng):
    """The boot into the can, real: a scuff of real gravel under the boot, a real tin knock for the toe denting the can,
    a real pot's clang pitched up for its ring."""
    g = ck.get(GRAVEL[int(rng.integers(2))])
    h = ck.hits(g, floor_db=-12, gap=0.03)
    a = h[0][0] if h else 0
    scuff = unit(hp(ck.cut(g, a, a + samples(0.08), 0.002, 0.03), 300, 2))
    return ck.place([(0.0, scuff, -10), (0.005, tin_rec(rng, (2, 4), 0.03), 0), (0.006, pot_rec(rng, 0.03), -6)])


def clank_rec(rng, force):
    """The can coming down on the road's stones, real: a real pot's clang pitched up to a can and a real tin knock."""
    return mix(pot_rec(rng, 0.04 + 0.04 * force), tin_rec(rng, (4, 6), 0.03) * 0.6) * force


def roll_rec(rng, length):
    """Rolling over dirt and stones, real: real gravel crunched under a boot drawn out under the roll (the grit under the
    can), a real tin knock for its seam once a turn as the turns slow, real stones' ticks flicked against it."""
    g = dsp.trim_silence(ck.denoise(ck.get(GRAVEL[int(rng.integers(2))])), -30)
    g = dsp.stretch(g, length * 1.1 / (len(g) / SR))
    grind = unit(hp(dsp.fit(g, samples(length)), 500, 2)) * env([(0, 1), (length * 0.7, 0.6), (length, 0)], length)
    parts = [(0.0, grind, -10)]
    for t, u in roll_times(rng, 0.0, length, rng.uniform(9, 12)):
        parts.append((t, tin_rec(rng, (5, 9), 0.015), -8 - 8 * u + rng.uniform(-3, 0)))
    for t in W.poisson(length * 0.8, 9, rng):
        parts.append((t, grain(rng), rng.uniform(-20, -12)))
    return ck.place(parts, length + 0.2)


def rock_rec(rng, force):
    """The can rocking back on the bump it stopped against: a small real tin knock."""
    return tin_rec(rng, (5, 7), 0.02) * force


@recipe(ODD, "can-kick", "clatter",
        "A tin can kicked along a village road, from real tin and pots: the clank, bounces, a rattle over the stones",
        """From the packs' real recordings: a boot scuffing real gravel into the can, a real tin knock pitched up for the
        toe denting it and the packs' real pots' clangs pitched up six to eight semitones from a pot to a small can for
        its ring; it comes down on the road a quarter of a second later with a real pot's clang and tin knock, bounces two
        or three times lower and sooner, and rolls: real gravel drawn out under it, a real tin knock for its seam once a
        turn as it slows, real stones' ticks flicked against it; it rocks back once or twice and stops. Out on a
        village road at night.""",
        sources=TIN + POTS + GRAVEL + STONES, takes=3, lufs=-18)
def can_clatter(rng, k):
    return can_kick(rng, k, lambda: kick_rec(rng), lambda f: clank_rec(rng, f), lambda L: roll_rec(rng, L),
                    lambda f: rock_rec(rng, f))


# ---- Something answers (world-odd.bark) ---------------------------------------------------------------------------------

FIELDS = [(0.17, -11, 40), (0.46, -16, 140)]    # a barn behind it, the treeline across the fields
ROAR = "sfx_100_v2:misc_24"     # a real roar: a big cat's rough throat, 130 Hz, loudest near 400
GROAN = "sfx_100_v2:misc_25"    # a real human groan: a breathy, sighing moan
GASP = "sfx_100_v2:misc_05"     # a real gasp: a burst of air through a mouth


def far_off(y, rng, metres=None):
    """90 m off over the fields in the dark: the air and the grass and hedges taking the top off, the barn and the
    treeline throwing it back, a little of the night."""
    m = metres or rng.uniform(80, 100)
    z = blur(air(y, m), rng)
    z = mix(z * 0.55, lp(z, 2500, 1) * 0.45)
    z = thrown_back(z, rng, FIELDS, 3000)
    return dsp.room(z, "night", wet=0.25, rng=rng)


def slow_env(L):
    """A bark that takes too long about it: snapped open like a dog's and loudest in its first 40 ms, but falling away
    over half a second instead of a dog's tenth."""
    return env([(0, 0), (0.006, 1), (0.04, 0.9), (L * 0.25, 0.6), (L * 0.6, 0.25), (L, 0)], L)


def flatten(x, floor=0.15):
    """Even out a recording's own swell (divide by its slow envelope), so a shape drawn on it is the one heard."""
    e = lp(np.abs(x), 15, 1)
    return (x / np.maximum(e, floor * np.max(e))).astype(np.float32)


@functools.lru_cache(maxsize=1)
def _roar():
    x = ck.get(ROAR)
    return x[samples(0.12):samples(0.42)].copy()    # the roar's full-throated middle


def throat_rec(rng, L, semis):
    """A deep bark's throat from the real roar: a slice sped up only `semis` (one to three and a half; a farm dog's is
    eight: so the throat stays a big chest's), held out to the bark's slow length, its pitch bent into a slow leap and a long
    fall, a man's 'wo-ah' drawn on it (formants near 650 and 1100 Hz) instead of a dog's."""
    x = _roar()
    a = int(rng.integers(0, len(x) - samples(0.24)))
    g = flatten(dsp.vari(x[a:a + samples(0.24)], semis))
    g = dsp.stretch(g, L * 1.4 / (len(g) / SR))
    g = dsp.fit(bend(g, env([(0, -3), (0.05, 0.5), (L * 0.45, -1), (L * 1.4, -8)], L * 1.4)), samples(L))
    return mix(g * 0.5, dsp.peak(dsp.peak(g, 650, 2.5, 8), 1100, 3, 6) * 0.6)


def groan_rec(L, semis=0.0):
    """A man's breath let out under it: the real groan, its breathy middle, held to `L`."""
    x = dsp.vari(ck.get(GROAN), semis)
    seg = x[samples(0.05):samples(0.45)]
    held = dsp.fit(dsp.stretch(seg, max(1.0, L / (len(seg) / SR))), samples(L))
    return unit(held) * env([(0, 0), (L * 0.3, 1), (L, 0)], L)


def burst_rec(length=0.04):
    """The air a bark is snapped open on: the real gasp's first burst, cut short and held to a throat's band."""
    g = ck.get(GASP)[samples(0.035):samples(0.035 + length)]
    return unit(lp(hp(g, 400, 2), 4000, 2)) * env([(0, 0), (0.002, 1), (length, 0)], length, "exp")


def cough_rec(rng, force=1.0):
    """A cough in a big chest, real: a rough voiced edge of the roar (dropped toward a man's voice) bursting open, the
    real gasp's air rushing after it through a big 'ah' and darkened (a chest, not a mouth), dying into breath."""
    g = dsp.vari(ck.get(GASP)[samples(0.03):samples(0.3)], -rng.uniform(3, 5))
    Lc = len(g) / SR
    air_ = lp(dsp.peak(dsp.peak(hp(g, 200), 600, 2, 9), 1150, 2.5, 6), 3000, 2)
    air_ = air_ * env([(0, 0), (0.004, 1), (0.05, 0.55), (Lc, 0)], Lc)
    Le = 0.11
    edge = flatten(dsp.fit(dsp.vari(_roar()[samples(0.05):samples(0.2)], rng.uniform(-1, 1)), samples(Le)))
    edge = dsp.saturate(dsp.peak(edge, 650, 2, 6), 6) * env([(0, 0), (0.003, 1), (0.04, 0.7), (Le, 0)], Le)
    return unit(ck.place([(0.0, unit(edge), 0), (0.006, unit(air_), -3)])) * force


def bark_rec(rng, L, f, cough=False, groan=False):
    """One bark that's almost a dog's, from real throats: snapped open on the real gasp's burst of air, the roar's deep
    throat in a slow bark's shape, rattling with a slow, uneven fry, a man's breath (the real groan) let out under it
    (more of it under some); sometimes it breaks into a cough."""
    semis = 3.5 + 12 * np.log2(f / 165)
    v = throat_rec(rng, L, semis) * (0.5 + 0.5 * fry(rng, rng.uniform(22, 30), L))
    v = dsp.saturate(unit(v), 5) * slow_env(L)
    parts = [(0.0, unit(v), 0), (0.0, burst_rec(), -6),
             (L * 0.3, groan_rec(L * 1.1, -rng.uniform(0, 2)), -7 if groan else -16)]
    if cough:
        parts.append((L * 0.8, cough_rec(rng), -2))
    return ck.place(parts)


def answers(rng, k, bark, cough):
    """Three ways it answers: two deep barks too slow, the second lower and breaking into a cough; one long bark whose
    tail is a man's breath; a cough, one deep bark, and half of another, cut off."""
    f = rng.uniform(140, 165)
    if k == 0:
        parts = [(0.0, bark(rng.uniform(0.38, 0.44), f, False, False), 0),
                 (rng.uniform(0.82, 0.95), bark(rng.uniform(0.4, 0.46), f * 0.92, True, False), -1)]
    elif k == 1:
        parts = [(0.0, bark(rng.uniform(0.5, 0.58), f * 0.95, False, True), 0)]
    else:
        parts = [(0.0, cough(0.8), -4), (rng.uniform(0.32, 0.38), bark(rng.uniform(0.4, 0.45), f, False, False), 0),
                 (rng.uniform(1.05, 1.15), bark(rng.uniform(0.16, 0.2), f * 1.04, False, False), -5)]
    y = far_off(ck.place(parts), rng)
    return cut_to(y, min(2.5, len(dsp.trim_silence(y, -45)) / SR), 0.5)


@recipe(ODD, "bark", "chest",
        "Something answers from the dark: a dog, or something like one: deep, slow barks with a man's breath and a cough",
        """Kitbashed from real throats: each bark a slice of the packs' roar sped up only one to three and a half
        semitones (a farm dog's would be eight), so it barks at 135-160 Hz from a chest twice a dog's, held out to a
        bark twice as slow as a dog's, its pitch a slow leap and a long fall, a man's 'wo-ah' drawn on it, rattling with
        an uneven fry, snapped open on a real gasp's burst of air; a real man's groan let out under its tail like
        breath; one breaks into a cough (the roar's rough edge bursting open, a real gasp's air rushing after it,
        dropped and darkened for a big chest). Two barks, the second lower and coughing; one long one ending in a
        breath; a cough, a bark and half of another. 90 m off over the fields: the air, the hedges, a barn and the
        treeline throwing it back.""",
        sources=[ROAR, GROAN, GASP], takes=3, lufs=-21, gap=1.0)
def bark_chest(rng, k):
    return answers(rng, k, lambda L, f, c, g: bark_rec(rng, L, f, c, g), lambda force: cough_rec(rng, force))


def throat_model(rng, L, f):
    """A man's throat made into something's bark, modelled: a voice at 140-165 Hz (a farm dog's is three times that)
    snapped open on a burst of air, its pitch leaping and falling as a bark's does but at half the speed, harsh (the folds
    doubling their period, a rough flutter, breath forced through every pulse), through a tract longer than a man's whose
    mouth goes 'h' to 'wo' to 'ah' and closes: a man's aspirate where a dog has none."""
    pts = [(0, f * 0.75), (0.04, f * 1.12), (0.09, f), (L * 0.6, f * 0.8), (L, f * 0.6)]
    g = synth.glottis(env(pts, L, "exp"), L, rng, jitter=0.03, shimmer=0.3, oq=0.5, sub=rng.uniform(0.35, 0.5),
                      rough=rng.uniform(0.3, 0.45))
    src_ = g + synth.noise(L, rng) * (0.3 + 0.7 * lp(np.abs(g), 300)) * 0.6
    v = synth.tract(src_, [(0, "h"), (0.015, "u"), (0.06, "o"), (0.14, "a"), (L * 0.65, "a"), (L, "u")], 0.62, breath=0.35,
                    rng=rng)
    pop = synth.tract(synth.noise(0.035, rng) * env([(0, 1), (0.035, 0)], 0.035, "exp"), "a", 0.62)
    return dsp.saturate(unit(mix(unit(v), unit(pop) * 0.45)), 6)


def bark_model(rng, L, f, cough=False, groan=False):
    """One bark that's almost a dog's, modelled: the man's throat made a bark, rattling with an uneven fry, a man's breath
    (the real groan) let out under it (more of it under some); sometimes it breaks into a man's cough."""
    v = throat_model(rng, L, f) * (0.55 + 0.45 * fry(rng, rng.uniform(22, 30), L)) * slow_env(L)
    parts = [(0.0, unit(v), 0), (L * 0.3, groan_rec(L * 1.1, -rng.uniform(1, 3)), -7 if groan else -17)]
    if cough:
        parts.append((L * 0.8, cough_model(rng), -2))
    return ck.place(parts)


def cough_model(rng, force=1.0):
    """A cough in a big chest, modelled: the town's human cough (the glottis bursting open, air rushing through 'ah' to
    'h') pitched into a chest bigger than a man's and darkened."""
    c = cough_once(rng, rng.uniform(110, 130), 1.2)
    return unit(lp(dsp.vari(c, -rng.uniform(2, 4)), 3000, 2)) * force


@recipe(ODD, "bark", "throat",
        "Something answers from the dark, modelled: a man's throat barking like a dog, too deep, too slow, a cough in it",
        """Modelled, with a real breath in it: each bark a voice at 140-165 Hz (a farm dog's is three times that) snapped
        open on a burst of air, its pitch leaping and falling as a bark's does but at half the speed, harsh with the
        folds doubling and a rough flutter, through a tract longer than a man's that opens 'h' to 'wo' to 'ah': a man's
        aspirate where a dog has none. A real man's groan (the packs' sigh) let out under the tail like breath; one bark
        breaks into a man's cough dropped into a bigger chest. Two barks, the second coughing; one long one ending in a
        breath; a cough, a bark and half of another. 90 m off over the fields, a barn and the treeline throwing it back.""",
        sources=[GROAN], takes=3, lufs=-21, gap=1.0)
def bark_throat(rng, k):
    return answers(rng, k, lambda L, f, c, g: bark_model(rng, L, f, c, g), lambda force: cough_model(rng, force))
