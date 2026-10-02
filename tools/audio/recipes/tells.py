"""The enemy tells (spec A.4): the warning sounds, each held to its band over the train bed and known by its rhythm.

Each is built from what the thing is made of and what it's doing, mostly from the synth models (the packs have little
that's organic) with real recordings bent in where they carry the material:

- Tippy Toesie, tiptoeing: a child-sized bare foot set down with care. What you hear is the surface taking the weight (a
  board's give and its nail, a roof sheet oil-canning) and the skin (toenails, a pad, a squeak, the sole peeling off).
- The Marsh: dry reeds. Thousands of crisp leaf-on-leaf grains (sharp onsets, a few loud and many quiet, so it crackles
  dry instead of hissing), stalks clacking, blades fluttering after they spring back; the surge is something parting
  them in pushes that come faster and nearer, then stop dead.
- Track debris, the writhe: something wet and muscular twisting on itself on the line: lubricated skin sliding (a cavity
  of slime squeezed shut, gliding in pitch), air and fluid popping out of the folds, suction letting go, strands of mucus
  breaking; or the same thing thrashing against the rail, which rings.
- The Grumbler, gnawing: dry teeth or mandibles on a wooden crate, never wet. Teeth rasping across the grain in runs, a
  splinter torn off, the chip dropping; or slow mandible bites that press (the wood creaks), crunch (its fibres go) and
  tear. The crate's hollow body rings under all of it.
- The Climbers, scrabbling at the gap: six hooked hands of three fingers each on iron and wood. Every hand lands as a
  flam of three claw tips; the hands ripple in uneven bursts, a claw skids now and then. Many small hard attacks in a row,
  which is what tells it from the Dragger's one long rasp.
- The car fire, through the boards: a real wood fire, not hiss. Smouldering, it's charring wood ticking in little
  flurries over a faint frying seethe, the odd sap pop and a pocket of sap singing as it boils off; alight, it's dense
  crackle that rides the flames' lapping, pops, boards splitting, embers collapsing, and the roar's flutter.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, Bus
import src
from recipes.kit import scatter, take as kit_take, slap as synth_slap, gravel as kit_gravel

SR = dsp.SR


# ---- Shared parts ---------------------------------------------------------------------------------------------------------

def burst(rng, length, lo=None, hi=None, attack=0.5):
    """A short noise burst with a raised-cosine envelope; `attack` is the rise as a share of its length (0 = a hard tick,
    0.5 = a soft press)."""
    n = max(samples(length), 4)
    x = rng.standard_normal(n).astype(np.float32)
    a = max(1, int(n * attack))
    e = np.ones(n, np.float32)
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    e[a:] = np.cos(np.linspace(0, np.pi / 2, n - a)) ** 2
    x *= e
    if lo and hi:
        x = dsp.bp(x, lo, hi)
    elif hi:
        x = dsp.lp(x, hi)
    elif lo:
        x = dsp.hp(x, lo)
    return x


def ring(x, modes, q, tail=0.0, gains=None):
    """An excitation rung through a body's modes, with room for the ring to die away."""
    x = np.concatenate([x, np.zeros(samples(tail), np.float32)])
    return dsp.resonate(x, modes, q=q, gains=gains)


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))) + 1e-12)


def norm(x):
    return (x / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def held(y, lo, hi, drive=9):
    """Round off the loudest peaks (a soft clip) and hold the result to the tell's band, which also filters off the clip's
    grit: a texture of sharp grains then carries its level in its body, not in a few spikes."""
    return dsp.band(dsp.saturate(norm(y), drive), lo, hi)


def glide_ring(rng, modes, q, length, glide=-0.05, amp=None, soft=0.0004):
    """A thin sheet snapping through its buckle (a tin roof oil-canning): an impulse into modes whose pitch slides by
    `glide` (a share) as the sheet's stiffness changes."""
    n = samples(length)
    x = np.zeros(n, np.float32)
    k = max(samples(soft), 2)
    x[:k] = rng.standard_normal(k) * np.hanning(k)
    out = np.zeros(n, np.float32)
    for i, f in enumerate(modes):
        fc = env([(0, f), (length * 0.25, f * (1 + glide)), (length, f * (1 + glide * 1.2))], length, curve="exp")
        out += dsp.sweep_filter(x, "bp", dsp.fit(fc, n), q=q, block=64) * (amp[i] if amp else 1.0 / (1 + i * 0.4))
    return norm(out)


# ---- Tippy Toesie: tiptoeing (1-2 kHz, one step per take, per surface) -----------------------------------------------------

TIPPY = (1000, 2000)


def _pace(takes, rng):
    """Steps at its slow, uneven stalking pace, creeping nearer (the game fires one per step)."""
    order = list(range(len(takes))) * 2
    t, parts = 0.3, []
    for i, k in enumerate(order):
        parts.append((t, takes[k], -9 + 9 * i / (len(order) - 1)))
        t += rng.uniform(0.8, 1.7) * (1.6 if rng.random() < 0.2 else 1.0)   # now and then it stops to listen
    b = Bus(t + 1.0)
    for at, x, db in parts:
        b.at(at, x, db)
    return b.x


def _pad(rng, body, q=7, length=0.006):
    """The ball of a bare toe pressed down, not struck: a soft rise into the surface's modes."""
    return norm(ring(burst(rng, length, hi=3500, attack=0.45), body, q, tail=0.05))


def _settle(rng, body, q):
    return norm(ring(burst(rng, 0.0015, hi=6000, attack=0.1), body, q, tail=0.04))


def tiptoe_give_wood(rng, k):
    """A floorboard taking a careful weight: the pad, the board's give (a rough creak or its nail's squeak), the settle."""
    board = [f * rng.uniform(0.9, 1.25) for f in synth.WOOD]
    b = Bus(1.0)
    b.at(0.01, _pad(rng, board), -12)
    # how the weight comes on: some steps creak (board rubbing board), some squeak (a nail in its joist), one only ticks
    kind = ["creak", "squeak", "creak", "ticks", "squeak", "creak"][k]
    t = rng.uniform(0.04, 0.09)
    if kind == "creak":
        cl = rng.uniform(0.22, 0.45)
        rate = env([(0, rng.uniform(18, 30)), (cl * 0.6, rng.uniform(60, 90)), (cl, rng.uniform(110, 160))], cl)
        c = synth.creak(cl, rate, rng, body=board, q=rng.uniform(16, 26), jitter=0.4, grit=0.25,
                        shape=env([(0, 0), (cl * 0.25, 0.7), (cl * 0.7, 1), (cl, 0)], cl))
        b.at(t, c, rng.uniform(-2, 0))
    elif kind == "squeak":
        cl = rng.uniform(0.12, 0.26)
        f0 = rng.uniform(430, 620)
        rate = env([(0, f0), (cl * 0.5, f0 * rng.uniform(1.08, 1.2)), (cl, f0 * rng.uniform(0.95, 1.05))], cl, "exp")
        c = synth.creak(cl, rate, rng, body=[f0 * 2.6, f0 * 3.1, board[3]], q=6, jitter=0.06, grit=0.0,
                        shape=env([(0, 0), (cl * 0.3, 1), (cl * 0.8, 0.7), (cl, 0)], cl))
        b.at(t, c, rng.uniform(-3, 0))
    else:   # the board shifting on its nails without a creak
        for i in range(3):
            b.at(t + 0.02 + i * rng.uniform(0.06, 0.14), _settle(rng, board, 9), -3 - 4 * i)
    # the board settling as the weight comes fully onto it
    b.at(rng.uniform(0.35, 0.6), _settle(rng, board, 12), rng.uniform(-14, -9))
    y = dsp.room(b.x, "car", wet=0.18, rng=rng)
    return dsp.band(y, *TIPPY)


def tiptoe_give_roof(rng, k):
    """A roof sheet under a careful weight: the pad's dull tup, the sheet oil-canning through its buckle, a tick at a nail."""
    sheet = [f * rng.uniform(0.85, 1.2) for f in synth.TIN]
    modes = [m for m in sheet if 800 < m < 2400]
    b = Bus(1.0)
    b.at(0.01, _pad(rng, sheet, q=14, length=0.007), -11)
    t = rng.uniform(0.07, 0.2)
    if k != 2:
        # a snap-through: the panel's modes rung at once, sliding down as it gives
        b.at(t, glide_ring(rng, modes, rng.uniform(70, 120), 0.35, glide=-rng.uniform(0.03, 0.08)), 0)
        if rng.random() < 0.5:   # and back, smaller and higher, as the weight eases
            b.at(t + rng.uniform(0.25, 0.4), glide_ring(rng, modes, 90, 0.25, glide=rng.uniform(0.02, 0.05)), -9)
    else:   # this one doesn't buckle: the sheet grinds a little on its purlin instead
        cl = rng.uniform(0.18, 0.3)
        b.at(t, synth.creak(cl, env([(0, 25), (cl, 70)], cl), rng, body=sheet, q=40, jitter=0.4,
                            shape=env([(0, 0), (cl * 0.3, 1), (cl, 0)], cl)), -2)
    for _ in range(rng.integers(1, 3)):   # the sheet ticking against a nail head
        b.at(t + rng.uniform(0.08, 0.5), _settle(rng, [sheet[2], sheet[3]], 40), rng.uniform(-14, -8))
    y = dsp.fade(dsp.fit(dsp.room(b.x, "night", wet=0.06, rng=rng), samples(1.1)), 0, 0.3)
    return dsp.band(y, *TIPPY)


def _squeak(rng, f0, length, body_q=2.0):
    """Skin stick-slipping on a smooth surface: a short squeal whose pitch rides up as the grip tightens and lets go, with
    the breathy hiss of the skin sliding under it."""
    rate = env([(0, f0), (length * 0.55, f0 * rng.uniform(1.1, 1.25)), (length, f0 * rng.uniform(0.92, 1.02))], length,
               "exp")
    shape = env([(0, 0), (length * 0.2, 1), (length * 0.75, 0.8), (length, 0)], length)
    sq = synth.creak(length, rate, rng, body=[f0 * 1.15], q=body_q, jitter=0.05, grit=0.0, shape=shape)
    hiss = burst(rng, length, 900, 2200, attack=0.3)
    return norm(mix(sq, norm(hiss) * 0.25))


def _peel(rng, body, q, length):
    """The sole coming unstuck: a dense run of tiny ticks that thins out (tacky skin parting from varnish or paint)."""
    n = samples(length)
    x = np.zeros(n, np.float32)
    t = 0.0
    while True:
        t += rng.exponential(1 / (1500 * (1 - t / length) + 120))
        if t >= length:
            break
        x[min(samples(t), len(x) - 1)] += rng.uniform(0.2, 1.0) * (1 - t / length)
    return norm(ring(x, body, q, tail=0.02))


def tiptoe_skin(rng, k, mat):
    """A bare child-sized foot, close: its toenails, the pad, the skin squeaking as it grips, the sole peeling off."""
    if mat == "wood":
        body = [f * rng.uniform(0.95, 1.2) for f in synth.WOOD]
        bq, f0, peel_len = 9, rng.uniform(1100, 1350), rng.uniform(0.025, 0.05)
    else:
        body = [f * rng.uniform(0.85, 1.15) for f in synth.TIN]
        bq, f0, peel_len = 45, rng.uniform(1300, 1600), rng.uniform(0.04, 0.08)
    b = Bus(1.0)
    # the nails touch first, one toe after another: a tiny dry run, never a knock
    t = 0.01
    for i in range(rng.integers(2, 5)):
        nail = synth.click(rng.uniform(1300, 1900), q=10, length=0.01, rng=rng)
        b.at(t, norm(mix(nail, ring(nail, body, bq, tail=0.03) * 0.5)), -6 - 3 * i + rng.uniform(-2, 2))
        t += rng.uniform(0.008, 0.026)
    b.at(t + rng.uniform(0.005, 0.02), _pad(rng, body, q=bq * 0.8), -9)
    # what the skin does once the weight's on it: some steps squeak, some peel off, one does both
    squeak, peel = [(True, False), (False, True), (True, True), (False, True), (True, False), (False, True)][k]
    if squeak:
        b.at(t + rng.uniform(0.05, 0.14), _squeak(rng, f0, rng.uniform(0.05, 0.13)), rng.uniform(-6, -3))
    if peel:
        b.at(rng.uniform(0.3, 0.5), _peel(rng, body, bq, peel_len), rng.uniform(-6, -3))
    y = dsp.room(b.x, "car" if mat == "wood" else "night", wet=0.15 if mat == "wood" else 0.05, rng=rng)
    return dsp.band(dsp.fade(dsp.fit(y, samples(1.1)), 0, 0.3), *TIPPY)


for _mat, _fn, _how in (
        ("wood", tiptoe_give_wood,
         """A bare child-sized foot put down with care on a floorboard. The toe pad is pressed, not struck (a soft rise of
         noise rung through a board's modes, so no knock), then the board takes the weight: a rough short creak of board
         on board, or its nail squeaking in the joist, or just a settling tick. No breath, no doubles: only the board's
         give, which is what sneaking on boards sounds like. Small wooden car's reverb."""),
        ("roof", tiptoe_give_roof,
         """The same careful bare foot on the car roof's tin: a dull 'tup' of the pad on the sheet, then the sheet
         oil-canning (a snap through its buckle, its ring sliding down in pitch as it gives) and sometimes snapping back
         as the weight lifts; a sheet ticking at a nail head after. Synthesised from the roof tin's modes.""")):
    recipe("tell-tippy", "tiptoe", "give", "One careful bare step: the surface giving under it (a board creak or nail "
           "squeak on wood; the tin oil-canning on the roof)", _how, takes=6, mat=_mat, band=TIPPY, lufs=-26,
           preview=_pace)(_fn)


for _mat, _how in (
        ("wood", """The bare skin of a child-sized foot, close, on a floorboard: its toenails touch first, a tiny dry run of
         clicks one toe after another; then the pad presses (soft, no knock); then either the skin squeaks on the varnish
         as it grips (a short stick-slip squeal) or the sole peels off as the foot lifts (a dense run of tiny ticks that
         thins out). Synthesised, rung through a board's modes, in a wooden car's reverb."""),
        ("roof", """The same bare foot on the roof tin: toenails ticking on the sheet (its modes ring a little longer than
         a board's), the pad, the skin squeaking higher on the painted metal or peeling off it slowly, tacky on the
         paint. Synthesised from the tin's modes, almost dry.""")):
    recipe("tell-tippy", "tiptoe", "skin", "One bare step, close: toenails, the pad, the skin squeaking or peeling off the "
           "surface", _how, takes=6, mat=_mat, band=TIPPY, lufs=-26, preview=_pace)(
        lambda rng, k, _m=_mat: tiptoe_skin(rng, k, _m))


# ---- The Marsh: reeds rustling (12-15 kHz, a loop) ------------------------------------------------------------------------

MARSH = (12000, 15000)


def periodic(rng, length, harmonics=4, lo=0.0, hi=1.0):
    """A slow wandering curve that comes back to where it started after `length` (for a loop's swells)."""
    n = samples(length)
    t = np.arange(n) / SR
    c = np.zeros(n)
    for h in range(1, harmonics + 1):
        c += rng.uniform(0.3, 1.0) / h * np.sin(2 * np.pi * h * t / length + rng.uniform(0, 2 * np.pi))
    c = (c - c.min()) / (c.max() - c.min() + 1e-9)
    return (lo + (hi - lo) * c).astype(np.float32)


def leaves(rng, length, rate, f=(9000, 17000), dur=(0.002, 0.014), alpha=2.4):
    """Dry blades rubbing: grains with a sharp onset and a quick decay, Poisson in time at `rate` a second (a constant or
    a curve), loudness drawn from a power law so a few crack out of many quiet ones. That spread is what makes it dry."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = synth.curve(rate, n)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(*dur)) + 8
        fc = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
        g = rng.standard_normal(k).astype(np.float32) * np.exp(-np.linspace(0, rng.uniform(3, 7), k)).astype(np.float32)
        g = dsp.bp(g, fc * 0.75, min(fc * 1.3, SR * 0.46))
        m = min(k, n - a)
        out[a:a + m] += g[:m] * min(0.4, 0.05 * rng.pareto(alpha) + 0.03)
    return out


def stalk(rng):
    """A dry hollow stalk knocking another: a tick rung through a thin tube's modes (most of it lands up high)."""
    f = rng.uniform(2200, 4800)
    return synth.click(f, q=7, length=0.01, rng=rng, body=[f, f * 2.76, f * 5.4])


def push(rng, length, bright=1.0):
    """Something pushing through a clump: blades dragged along it (a dense swell of grains that brightens as it moves
    faster), the stalks clacking as they spring back, the blades fluttering after."""
    L = length + 0.6
    b = Bus(L)
    dense = leaves(rng, length, rng.uniform(1800, 3500), f=(8000 * bright, 17000), dur=(0.002, 0.02))
    b.at(0, dsp.shaped(dense, [(0, 0), (length * rng.uniform(0.35, 0.6), 1), (length, 0)]))
    for _ in range(rng.integers(2, 6)):
        b.at(length * rng.uniform(0.5, 1.1), stalk(rng), rng.uniform(-14, -4))
    fl = rng.uniform(16, 30)   # blades still shivering once it's through
    tail = leaves(rng, 0.5, 900, f=(10000, 17000))
    tail *= (0.5 + 0.5 * np.sin(2 * np.pi * fl * np.arange(len(tail)) / SR)) * env([(0, 1), (0.5, 0)], 0.5, "exp")[:len(tail)]
    b.at(length * 0.85, tail, -6)
    return b.x


def surge(rng, b, start, pushes, gap0, gap1, db0, db1):
    """A run of pushes at gaps shrinking from gap0 to gap1 and levels rising (it's coming), then nothing (it lost you)."""
    t = start
    for i in range(pushes):
        k = i / max(pushes - 1, 1)
        L = rng.uniform(0.16, 0.34) * (1.2 - 0.4 * k)
        b.at(t, push(rng, L, bright=1.2 - 0.3 * k), db0 + (db1 - db0) * k + rng.uniform(-1.5, 1.5))
        t += (gap0 + (gap1 - gap0) * k) * rng.uniform(0.8, 1.25)
    return t


@recipe("tell-marsh", "reeds", "parting",
        "Dry reeds stirring in slow swells; something parts them in pushes that come faster and nearer, then stops dead",
        """Synthesised dry reeds: thousands of leaf-on-leaf grains with sharp onsets and a power-law spread of loudness (a
        few crack out, most are tiny), so it rustles dry instead of hissing; hollow stalks clacking; blades shivering after
        they spring back. The bed swells slowly and unevenly. Twice a loop something parts them: a small run of three
        pushes, then a long creeping one of seven that comes faster, brighter and nearer and stops dead (it lost you), the
        reeds settling after. Held to 12-15 kHz, above everything the train makes.""",
        takes=1, loop=True, band=MARSH, lufs=-26, seconds=12)
def reeds_parting(rng, k):
    L = 12.0
    n = samples(L)
    b = Bus(L)
    # the bed: breathing slowly, stirred up round each surge
    stir = periodic(rng, L, 4, 0.0, 1.0) * 0.5 + env([(0, 0.2), (1.2, 0.3), (2.5, 1.0), (4.2, 0.4), (5.6, 0.5),
                                                         (8.2, 1.3), (10.0, 0.6), (11.2, 0.3), (12, 0.2)], L)
    b.at(0, leaves(rng, L, 40 + 260 * stir), -11)
    end1 = surge(rng, b, 1.6, 3, 0.62, 0.48, -12, -8)
    end2 = surge(rng, b, 5.9, 7, 0.55, 0.17, -11, 0)
    for t0 in (end1, end2):   # the reeds settling: a few late clacks
        for _ in range(rng.integers(3, 6)):
            b.at(t0 + rng.uniform(0.1, 1.2), stalk(rng), rng.uniform(-22, -12))
    y = dsp.wrap(dsp.room(b.x, "night", wet=0.15, rng=rng), n)
    return held(y, *MARSH)


PAPER = [("kenney_rpg-audio:bookFlip1", 0.27, 0.56), ("kenney_rpg-audio:bookFlip2", 0.0, 0.42),
         ("kenney_rpg-audio:bookFlip3", 0.06, 0.21)]


def blades(rng, length, rate):
    """Dry blades from real paper: slivers of book pages riffling, an octave up, sharp at the front, scattered like leaves
    (Poisson at `rate` a second, power-law loud)."""
    srcs = [kit_take(k, a, b - a) for k, a, b in PAPER]
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = synth.curve(rate, n)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        x = srcs[rng.integers(len(srcs))]
        d = samples(rng.uniform(0.015, 0.07))
        s0 = rng.integers(0, max(1, len(x) - d))
        g = dsp.vari(x[s0:s0 + d], rng.uniform(10, 15))
        g = g * np.exp(-np.linspace(0, rng.uniform(2, 6), len(g))).astype(np.float32)
        g = g / (np.std(g) + 1e-9)
        m = min(len(g), n - a)
        out[a:a + m] += g[:m] * min(0.4, 0.05 * rng.pareto(2.4) + 0.03)
    return out


def snap_stem(rng):
    """A dry stem breaking: its fibres going in a quick uneven run of ticks, then the hollow knock of the two halves."""
    b = Bus(0.08)
    t = 0.0
    for i in range(rng.integers(5, 14)):
        b.at(t, synth.click(rng.uniform(5000, 11000), q=5, length=0.004, rng=rng), -i * 0.8 + rng.uniform(-4, 0))
        t += rng.exponential(0.0025)
    b.at(t + 0.004, stalk(rng), -6)
    return b.x


@recipe("tell-marsh", "reeds", "wading",
        "Reeds combed by slow gusts; something wades through them, its strides quickening, stems snapping, then still",
        """The reeds made from real paper: slivers of riffled book pages an octave and more up, scattered like dry blades
        and combed by two slow gusts. Inside each gust something wades: a stride of paired swishes (one leg, the other),
        careful in the first gust, quickening to a lunge in the second; dry stems snap under it (a run of fibre ticks and
        the halves knocking) and the bent reeds spring back after it stops. Held to 12-15 kHz.""",
        sources=[k for k, _, _ in PAPER], takes=1, loop=True, band=MARSH, lufs=-26, seconds=12)
def reeds_wading(rng, k):
    L = 12.0
    n = samples(L)
    b = Bus(L)
    gust = env([(0, 0.15), (1.0, 0.25), (2.6, 1.0), (4.6, 0.3), (6.0, 0.35), (8.4, 1.2), (10.4, 0.4), (12, 0.15)], L)
    b.at(0, blades(rng, L, 30 + 220 * (gust + 0.2 * periodic(rng, L, 3))), -10)
    for start, steps, gap0, gap1, db0, db1 in ((1.5, 4, 0.7, 0.6, -14, -10), (6.3, 9, 0.55, 0.22, -12, 0)):
        t = start
        for i in range(steps):
            q = i / (steps - 1)
            sl = rng.uniform(0.12, 0.22)
            sw = dsp.shaped(blades(rng, sl, rng.uniform(1500, 2800)), [(0, 0), (sl * 0.4, 1), (sl, 0)])
            b.at(t, sw, db0 + (db1 - db0) * q)
            if rng.random() < 0.35 + 0.3 * q:
                b.at(t + sl * rng.uniform(0.3, 0.8), snap_stem(rng), db0 + (db1 - db0) * q - 6)
            # strides come in pairs: a long gap, then a short one
            t += (gap0 + (gap1 - gap0) * q) * (1.25 if i % 2 else 0.75) * rng.uniform(0.9, 1.1)
        for _ in range(rng.integers(3, 6)):   # the bent reeds springing back once it stops
            sl = rng.uniform(0.08, 0.16)
            b.at(t + rng.uniform(0.0, 0.9), dsp.shaped(blades(rng, sl, 900), [(0, 1), (sl, 0)]), db1 - rng.uniform(8, 14))
            b.at(t + rng.uniform(0.1, 1.3), stalk(rng), db1 - rng.uniform(14, 22))
    y = dsp.wrap(dsp.room(b.x, "night", wet=0.15, rng=rng), n)
    return held(y, *MARSH)


# ---- Track debris: the wet writhe (400 Hz-2 kHz, one-shots fired at uneven gaps) -------------------------------------------

DEBRIS = (400, 2000)


def slide(rng, length, f0, f1, q=None):
    """Wet skin sliding on wet skin: the slime between them squeezed through a gap that opens or closes, so its resonance
    glides; fluttering where the film breaks and re-forms."""
    n = synth.noise(length, rng, "pink")
    fc = env([(0, f0), (length * rng.uniform(0.4, 0.7), np.sqrt(f0 * f1)), (length, f1)], length, "exp")
    y = dsp.sweep_filter(n, "bp", fc, q=q or rng.uniform(5, 9), block=64)
    film = np.clip(0.55 + dsp.lp(rng.standard_normal(len(y)).astype(np.float32), 35) * 14, 0.05, 1.4)
    return norm(y * film * env([(0, 0), (length * 0.25, 1), (length * 0.7, 0.8), (length, 0)], length))


def suction(rng, f=None):
    """Two wet surfaces pulled apart: a soft click as the seal breaks and the bubble of air that rushes in."""
    f = f or rng.uniform(420, 800)
    b = Bus(0.2)
    b.at(0, burst(rng, 0.002, 600, 3000, attack=0.1), -8)
    b.at(0.001, synth.bubble(f, rise=rng.uniform(0.3, 0.8)))
    return norm(b.x)


def tack(rng, length):
    """Strands of slime stretching and snapping: a thinning run of tiny wet ticks."""
    x = np.zeros(samples(length), np.float32)
    t = 0.0
    while True:
        t += rng.exponential(1 / (700 * (1 - t / length) + 60))
        if t >= length:
            break
        x[min(samples(t), len(x) - 1)] += rng.uniform(0.2, 1.0)
    y = dsp.resonate(x, [rng.uniform(700, 1100), rng.uniform(1300, 1900)], q=4) + dsp.bp(x, 500, 2500) * 0.5
    return norm(y)


def contraction(rng, length):
    """One squirm: a slide gliding up or down, air popping from the folds through it, the fold letting go at the end."""
    b = Bus(length + 0.25)
    up = rng.random() < 0.6
    f0, f1 = (rng.uniform(380, 560), rng.uniform(1100, 1700)) if up else (rng.uniform(1200, 1700), rng.uniform(400, 600))
    b.at(0, slide(rng, length, f0, f1))
    pops = synth.bubbles(length, rng.uniform(25, 70), 450, 1900, rng, rise=(0.1, 0.7))
    b.at(0, norm(pops) * env([(0, 0.3), (length * 0.5, 1), (length, 0.4)], length)[:len(pops)], rng.uniform(-8, -4))
    b.at(length * rng.uniform(0.75, 0.95), suction(rng), rng.uniform(-7, -2))
    if rng.random() < 0.7:
        b.at(length * rng.uniform(0.8, 1.0), tack(rng, rng.uniform(0.04, 0.09)), rng.uniform(-13, -8))
    return b.x


@recipe("tell-track-debris", "writhe", "coils",
        "A brief wet writhe: slimy coils squirming over each other in two to four contractions, popping and letting go",
        """Something wet and muscular twisting on itself, made from models of what's wet: skin sliding on skin through a
        film of slime (pink noise squeezed through a gap whose resonance glides up or down, fluttering as the film breaks),
        air popping out of the folds (bubbles), the fold's suction letting go (a soft click and the bubble rushing in), and
        strands of slime snapping. Two to four contractions per take, each a little weaker, so it reads as a squirm, never a
        footstep. Held to 400 Hz-2 kHz; the game fires it at uneven gaps.""",
        takes=4, band=DEBRIS, lufs=-20, preview=lambda takes, rng: scatter(takes + takes[:2], rng, (0.9, 2.6)))
def writhe_coils(rng, k):
    n_c = [3, 2, 4, 3][k]
    b = Bus(2.2)
    t = 0.0
    for i in range(n_c):
        L = rng.uniform(0.22, 0.42) * (1 - 0.12 * i)
        b.at(t, contraction(rng, L), -3 * i + rng.uniform(-1.5, 1.5))
        t += L * rng.uniform(0.55, 0.9)
    y = dsp.fade(dsp.fit(dsp.room(b.x, "night", wet=0.1, rng=rng), samples(t + 0.7)), 0, 0.4)
    return dsp.band(y, *DEBRIS)


WET = ["kenney_impact-sounds:footstep_snow_000", "kenney_impact-sounds:footstep_grass_001", "sfx_100_v2:footstep_wet_01",
       "sfx_100_v2:wood_01"]
RAIL = [340, 940, 1840, 3040]   # a rail's bending modes (they go as (2n+1)^2): heavy, so it only rings this high


def rail_slap(rng):
    """A wet length of it thrown down on the rail: a slap of skin, real slush and squish under it, the rail ringing and
    choked at once by the weight lying on it, the ballast shifting."""
    b = Bus(0.6)
    b.at(0, synth_slap(rng), -4)
    wet = dsp.vari(src.get(WET[rng.integers(len(WET))]), -rng.uniform(3, 8))
    b.at(0.002, norm(wet), rng.uniform(-6, -2))
    modes = [m * rng.uniform(0.95, 1.05) for m in RAIL]
    b.at(0.001, norm(ring(burst(rng, 0.0015, attack=0.1), modes, rng.uniform(250, 400), tail=0.5,
                          gains=[0.5, 1, 0.8, 0.4])), rng.uniform(-6, -3))
    b.at(0.01, kit_gravel(rng, n=8, length=0.12, lo=700, hi=2500, body=None), -14)
    return b.x


@recipe("tell-track-debris", "writhe", "rail",
        "A brief wet thrash on the line: slaps on the rail (which rings, choked), the body dragging across it in between",
        """The same wet thing, thrashing against the rail ahead: each slap is a skin slap over real slush, squish and a
        wet footstep (pitched down), with the rail ringing at its bending modes and choked at once by the weight lying on
        it, and the ballast shifting; between slaps, the body drags wetly across the rail head (a gliding slime squeeze with
        bubbles). Slap, drag, a quick double slap, a last squirm. The rail's ring is what says it's on the line. Held to
        400 Hz-2 kHz.""",
        sources=WET, takes=4, band=DEBRIS, lufs=-20,
        preview=lambda takes, rng: scatter(takes + takes[:2], rng, (0.9, 2.6)))
def writhe_rail(rng, k):
    b = Bus(2.0)
    t = 0.0
    pattern = [["slap", "drag", "slap", "slap", "squirm"], ["drag", "slap", "squirm"],
               ["slap", "slap", "drag", "squirm"], ["slap", "drag", "slap", "drag"]][k]
    for i, what in enumerate(pattern):
        if what == "slap":
            b.at(t, rail_slap(rng), -2 * i * 0.5)
            t += rng.uniform(0.09, 0.16) if i + 1 < len(pattern) and pattern[i + 1] == "slap" else rng.uniform(0.12, 0.2)
        elif what == "drag":
            L = rng.uniform(0.25, 0.4)
            d = mix(slide(rng, L, rng.uniform(500, 700), rng.uniform(1000, 1500), q=5),
                    norm(synth.bubbles(L, 50, 450, 1800, rng)) * 0.4)
            b.at(t, d, -5)
            t += L * rng.uniform(0.7, 0.9)
        else:
            L = rng.uniform(0.2, 0.3)
            b.at(t, contraction(rng, L), -8)
            t += L
    y = dsp.fade(dsp.fit(dsp.room(b.x, "night", wet=0.1, rng=rng), samples(t + 0.6)), 0, 0.4)
    return dsp.band(y, *DEBRIS)


# ---- The Grumbler: gnawing on the crates (1.4-2.2 kHz, a loop) ---------------------------------------------------------------

GNAW = (1400, 2200)


def crate(rng):
    """A food crate's slats: a thin board's modes, a few of them where the band is."""
    k = rng.uniform(0.95, 1.08)
    return [f * k for f in (420, 980, 1480, 1730, 2040, 2650)]


def fibres(rng, length, rate, body, q=9):
    """Wood fibres giving way: an uneven train of tiny breaks at `rate` a second (a constant or a curve), rung through the
    crate. Fast, it's a rasp; slower, a crackle; decelerating, a tear."""
    n = samples(length)
    x = np.zeros(n, np.float32)
    rc = synth.curve(rate, n)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        if t >= length:
            break
        x[min(samples(t), len(x) - 1)] += rng.uniform(0.25, 1.0)
    return norm(dsp.resonate(x, body, q=q, gains=[0.3, 0.6, 1, 0.9, 0.8, 0.4]) + dsp.bp(x, 1200, 2600) * 0.4)


def stroke(rng, body, length):
    """One pass of the incisors across the grain: a rasp of breaking fibres with the scrape's hiss under it."""
    shape = [(0, 0), (length * 0.15, 1), (length * 0.6, 0.6), (length, 0)]
    r = dsp.shaped(fibres(rng, length, rng.uniform(450, 900), body), shape)
    h = dsp.shaped(burst(rng, length, 1300, 2400, attack=0.2), shape)
    return mix(r, norm(h) * 0.3)


def chip_drop(rng, body):
    """A splinter falling on the crate lid: a tick, then quicker smaller bounces."""
    b = Bus(0.4)
    t, g, dt = 0.0, 0.0, rng.uniform(0.07, 0.11)
    for _ in range(rng.integers(2, 5)):
        b.at(t, norm(ring(burst(rng, 0.001, attack=0.1), body, 20, tail=0.03)), g)
        t += dt
        dt *= rng.uniform(0.5, 0.7)
        g -= rng.uniform(4, 7)
    return b.x


CRACK = "sfx_100_v2:misc_35"


def splinter(rng):
    """A chunk torn off: a real piece of wood breaking, cut short and bent up into the crate's band, over a quick tear."""
    x = src.get(CRACK)
    a = rng.uniform(0.1, 0.32)
    c = dsp.vari(dsp.trim(x, a, rng.uniform(0.07, 0.12)), rng.uniform(-2, 3))
    tear = fibres(rng, 0.09, env([(0, 1400), (0.09, 120)], 0.09), crate(rng), q=7)
    return mix(norm(dsp.fade(c, 0.001, 0.02)), tear * 0.6)


@recipe("tell-grumbler", "gnaw", "incisors",
        "Teeth rasping across a crate's grain in quick runs; a splinter torn off and dropped, the crate creaking between",
        """Dry gnawing, never wet: each stroke of the teeth across the grain is a rasp of breaking fibres (a fast uneven
        train of tiny breaks rung through a crate slat's modes) with the scrape's hiss under it, strong and weak in turn as
        the jaw works, at 7-11 a second in runs of half a second to two seconds. Between runs it tears a splinter off (a
        real piece of wood breaking, bent into the band, over a quick tear), the chip bounces on the lid, the crate creaks
        as it shifts its weight. Held to 1.4-2.2 kHz.""",
        sources=[CRACK], takes=1, loop=True, band=GNAW, lufs=-22, seconds=12)
def gnaw_incisors(rng, k):
    L = 12.0
    b = Bus(L)
    body = crate(rng)
    t = 0.2
    while t < L - 0.8:
        rate = rng.uniform(7, 11)
        run = min(rng.uniform(0.5, 2.0), L - 0.6 - t)
        i, t0 = 0, t
        while t < t0 + run:
            sl = rng.uniform(0.04, 0.075)
            b.at(t, stroke(rng, body, sl), (0 if i % 2 == 0 else -5) + rng.uniform(-2, 1))
            t += (1 / rate) * rng.uniform(0.85, 1.15)
            i += 1
        gap = rng.uniform(0.35, 1.1)
        roll = rng.random()
        if roll < 0.45:
            b.at(t + 0.05, splinter(rng), rng.uniform(-3, 0))
            b.at(t + rng.uniform(0.15, 0.3), chip_drop(rng, body), -8)
        elif roll < 0.75:
            cl = rng.uniform(0.2, 0.4)
            b.at(t + 0.1, synth.creak(cl, env([(0, 30), (cl, 80)], cl), rng, body=body, q=22, jitter=0.4,
                                      shape=env([(0, 0), (cl * 0.4, 1), (cl, 0)], cl)), -9)
        t += gap
    y = dsp.wrap(dsp.room(b.x, "night", wet=0.12, rng=rng), samples(L))
    return held(y, *GNAW, drive=6)


def bite(rng, body):
    """One mandible bite: it presses in (the wood creaks as it compresses), the fibres crush (a dense crackle), and it
    tears the splinter away (a rip whose breaks slow down as it comes free)."""
    b = Bus(0.6)
    pl = rng.uniform(0.07, 0.14)
    b.at(0, synth.creak(pl, env([(0, 40), (pl, 220)], pl), rng, body=body, q=18, jitter=0.35,
                        shape=env([(0, 0), (pl, 1)], pl)), -8)
    cl = rng.uniform(0.03, 0.06)
    b.at(pl, dsp.shaped(fibres(rng, cl, 2200, body), [(0, 1), (cl, 0.2)]), 0)
    tl = rng.uniform(0.07, 0.15)
    tear = fibres(rng, tl, env([(0, rng.uniform(500, 800)), (tl, 50)], tl), body, q=12)
    b.at(pl + cl + rng.uniform(0.0, 0.03), dsp.shaped(tear, [(0, 0.8), (tl * 0.7, 0.5), (tl, 0)]), -4)
    return b.x


@recipe("tell-grumbler", "gnaw", "mandibles",
        "Slow mandible bites into a crate, left and right: press, crunch, tear; mouthparts clicking as it chews between",
        """A spider's jaws, not a rodent's: slower bites, two or three a second in sets of three to six, each one a press
        (the slat creaking as it compresses), a crunch (a dense burst of fibres crushing) and a tear (a rip whose breaks slow
        as the splinter comes free), the left and right mandibles biting into slightly different wood. Between sets its
        mouthparts work the chip with quick chitin clicks and spit it onto the lid. All dry, synthesised and rung through a
        crate slat's modes. Held to 1.4-2.2 kHz.""",
        takes=1, loop=True, band=GNAW, lufs=-22, seconds=12)
def gnaw_mandibles(rng, k):
    L = 12.0
    b = Bus(L)
    sides = [crate(rng), crate(rng)]
    t = 0.3
    while t < L - 1.0:
        for i in range(rng.integers(3, 7)):
            if t > L - 0.7:
                break
            b.at(t, bite(rng, sides[i % 2]), (0 if i % 2 == 0 else -3) + rng.uniform(-2, 1))
            t += rng.uniform(0.32, 0.55)
        # chewing it over: soft quick chitin clicks, then the chip spat out
        cl = rng.uniform(0.4, 0.9)
        b.at(t, synth.skitter(cl, rng.uniform(12, 18), rng, f=(1450, 2100), q=(8, 14), legs=2), -12)
        b.at(t + cl + 0.05, chip_drop(rng, sides[0]), -9)
        t += cl + rng.uniform(0.4, 0.9)
    y = dsp.wrap(dsp.room(b.x, "night", wet=0.12, rng=rng), samples(L))
    return held(y, *GNAW, drive=6)


# ---- The Climbers: scrabbling at the gap (2-5 kHz, one-shots) ----------------------------------------------------------------

CLIMB = (2000, 5000)


def claw(rng, iron):
    """One hooked claw tip striking: a hard tick into iron (a short bright ring, it's thick) or into wood (dull, at once)."""
    x = burst(rng, rng.uniform(0.0001, 0.0003), attack=0.05)
    if iron:
        f = rng.uniform(2400, 4600)
        y = ring(x, [f, f * rng.uniform(1.3, 1.6), f * rng.uniform(1.9, 2.3)], rng.uniform(25, 50), tail=0.03,
                 gains=[1, 0.6, 0.3])
    else:
        k = rng.uniform(1.6, 2.4)
        y = ring(x, [m * k for m in synth.WOOD[2:]], 8, tail=0.015)
    return norm(mix(norm(y), dsp.hp(x, 3000) * 0.5))


def hand(rng, iron):
    """Three fingers landing a few milliseconds apart: a flam, not one hit."""
    b = Bus(0.06)
    t = 0.0
    for i in range(3):
        b.at(t, claw(rng, iron), -i * rng.uniform(1, 4))
        t += rng.uniform(0.002, 0.008)
    return b.x


def skid(rng, length, iron):
    """A claw losing its grip and dragging: a short fast stick-slip, rung through the surface."""
    body = [m * rng.uniform(2.0, 2.6) for m in (synth.IRON[4:] if iron else synth.WOOD[3:])]
    return synth.creak(length, env([(0, rng.uniform(700, 1100)), (length, rng.uniform(300, 600))], length), rng,
                       body=body, q=20 if iron else 8, jitter=0.5, grit=0.6,
                       shape=env([(0, 0), (length * 0.2, 1), (length, 0)], length))


def sucker(rng):
    """The lamprey mouth under its head kissing onto the iron: a wet tick and a small bubble."""
    return norm(mix(burst(rng, 0.001, 2000, 6000, attack=0.1) * 0.6, synth.bubble(rng.uniform(2300, 3300), rise=0.6)))


@recipe("tell-climbers", "scrabble", "claws",
        "Hooked three-fingered hands scrambling for a grip on the coupler and the car end: uneven clattering bursts",
        """Six hooked hands, three claws each, scrambling up the gap: every hand lands as a flam of three claw tips (hard
        ticks into iron with a short bright ring, or dull into the wood of the car end), four to seven hands rippling in
        each uneven burst, a claw skidding now and then (a short fast stick-slip), and once in a while the lamprey mouth
        under its head kissing onto the iron. Two to four bursts per take at uneven gaps: a clatter of many small attacks,
        never one rasp. Synthesised. Held to 2-5 kHz.""",
        takes=4, band=CLIMB, lufs=-20, preview=lambda takes, rng: scatter(takes, rng, (0.5, 1.4)))
def scrabble_claws(rng, k):
    b = Bus(2.0)
    t = 0.0
    for burst_i in range([3, 2, 4, 3][k]):
        n_h = rng.integers(4, 8)
        for h in range(n_h):
            b.at(t, hand(rng, rng.random() < 0.6), rng.uniform(-6, 0))
            t += rng.uniform(0.018, 0.06)
        if rng.random() < 0.6:
            sl = rng.uniform(0.05, 0.11)
            b.at(t, skid(rng, sl, rng.random() < 0.6), rng.uniform(-8, -3))
            t += sl
        if rng.random() < 0.25:
            b.at(t + 0.02, sucker(rng), -10)
        t += rng.uniform(0.08, 0.3)
    y = dsp.fit(dsp.room(b.x, "car", wet=0.12, rng=rng), samples(t + 0.3))
    return held(dsp.fade(y, 0, 0.2), *CLIMB, drive=4)


IRON_TIPS = [f"kenney_impact-sounds:impactMetal_light_{i:03d}" for i in range(5)] + ["kenney_rpg-audio:metalClick"]
WOOD_TIPS = [f"kenney_impact-sounds:impactWood_light_{i:03d}" for i in range(5)]
SCRAPE = "sfx_100_v2:misc_10"


def tip(rng, keys, up):
    """A real strike cut to its first few milliseconds (the claw's contact, before the object rings) and pitched up."""
    x = src.get(keys[rng.integers(len(keys))])
    a = int(np.argmax(np.abs(x) > 0.2 * np.max(np.abs(x))))
    x = x[max(0, a - 24):a + samples(rng.uniform(0.008, 0.02))]
    return norm(dsp.bp(dsp.fade(dsp.vari(x, up + rng.uniform(-2, 2)), 0.0002, 0.006), *CLIMB))


def tripod(rng, iron):
    """Three hands landing together (the six-limbed gait moves three at once), each a flam of three claw tips."""
    b = Bus(0.08)
    keys, up = (IRON_TIPS, 5) if iron else (WOOD_TIPS, 19)
    for h in range(3):
        t = rng.uniform(0, 0.018)
        for c in range(3):
            b.at(t, tip(rng, keys, up), -h * 2 - c * 3 + rng.uniform(-2, 0))
            t += rng.uniform(0.002, 0.007)
    return b.x


@recipe("tell-climbers", "scrabble", "tripod",
        "Climbing the gap three hands at a time: quick clattering steps up the iron, then the wood, a claw skidding",
        """Six-limbed things move three limbs at a time, so this one climbs in tripods: each step is three hands landing
        together, each hand three claw tips a few milliseconds apart, cut from real strikes (light metal and wood impacts
        trimmed to their first few milliseconds, the claw's contact before the object rings, and pitched up). Steps come
        quick (6-9 a second, left tripod then right) in runs that start on the coupler's iron and go up onto the car end's
        wood, and one run ends with a claw skidding (a real scrape, bent up). A clatter with a gait in it. Held to
        2-5 kHz.""",
        sources=IRON_TIPS + WOOD_TIPS + [SCRAPE], takes=4, band=CLIMB, lufs=-20,
        preview=lambda takes, rng: scatter(takes, rng, (0.5, 1.4)))
def scrabble_tripod(rng, k):
    b = Bus(2.2)
    t = 0.0
    runs = [2, 3, 2, 2][k]
    for r in range(runs):
        steps = rng.integers(4, 8)
        rate = rng.uniform(6, 9)
        for s_ in range(steps):
            iron = (r * steps + s_) < rng.integers(3, 7)    # up off the coupler and onto the wood
            b.at(t, tripod(rng, iron), (0 if s_ % 2 == 0 else -2) + rng.uniform(-2, 1))
            t += 1 / (rate * (1 + 0.04 * s_)) * rng.uniform(0.85, 1.15)
        if r == runs - 1 or rng.random() < 0.4:
            x = src.get(SCRAPE)
            sl = rng.uniform(0.06, 0.12)
            sc = dsp.vari(dsp.trim(x, rng.uniform(0.04, 0.25), sl), rng.uniform(-3, 2))
            b.at(t - 0.04, dsp.fade(norm(dsp.bp(sc, *CLIMB)), 0.005, 0.03), -5)
            t += sl
        t += rng.uniform(0.1, 0.3)
    y = dsp.fit(dsp.room(b.x, "car", wet=0.12, rng=rng), samples(t + 0.25))
    return held(dsp.fade(y, 0, 0.2), *CLIMB, drive=9)


# ---- The car fire: crackle through the boards (6-9 kHz, two loops) ------------------------------------------------------------

FIRE = (6000, 9000)


def sap_pop(rng, big=False):
    """A pocket of sap or water bursting: a sharp tick, and if it's big the wood chunk around it rings for a moment."""
    k = samples(rng.uniform(0.0001, 0.0005 if not big else 0.0012)) + 2
    x = rng.standard_normal(k).astype(np.float32) * np.exp(-np.linspace(0, 4, k)).astype(np.float32)
    if not big:
        return x
    f = rng.uniform(3500, 9500)
    body = ring(x, [f, f * rng.uniform(1.4, 1.8)], rng.uniform(6, 14), tail=rng.uniform(0.004, 0.015), gains=[1, 0.5])
    return mix(x, norm(body) * np.max(np.abs(x)) * rng.uniform(0.6, 1.2))


def crackling(rng, length, rate, big=0.05, alpha=1.6):
    """Wood crackling: sap pops Poisson in time at `rate` a second (a constant or a curve), loudness from a power law
    (a few loud, a mass of tiny ones), a share `big` of them ringing their wood."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = synth.curve(rate, n)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 0.2))
        a = samples(t)
        if a >= n:
            break
        is_big = rng.random() < big
        p = sap_pop(rng, is_big)
        amp = min(1.0, 0.04 * rng.pareto(alpha) + 0.02) * (2.5 if is_big else 1.0)
        m = min(len(p), n - a)
        out[a:a + m] += p[:m] * amp
    return out


def split(rng):
    """A board splitting in the heat: the crack running along the grain (a run of ticks that slows as it goes), then the
    board's knock."""
    b = Bus(0.12)
    t, dt = 0.0, rng.uniform(0.0006, 0.0015)
    for i in range(rng.integers(10, 35)):
        b.at(t, sap_pop(rng, rng.random() < 0.2), rng.uniform(-6, 0) - i * 0.25)
        t += dt * rng.uniform(0.5, 1.5)
        dt *= rng.uniform(1.03, 1.12)
    b.at(t, norm(ring(burst(rng, 0.001, attack=0.1), [m * rng.uniform(2.5, 3.5) for m in synth.WOOD[2:]], 10,
                      tail=0.02)), -8)
    return b.x


def sap_sing(rng, length):
    """Sap boiling out of the end grain through a pinhole: a thin wavering whistle that rises a little and dies."""
    f = env([(0, rng.uniform(6300, 6900)), (length * 0.6, rng.uniform(7200, 7900)), (length, rng.uniform(6800, 7600))],
            length, "exp")
    f = f * (1 + dsp.lp(rng.standard_normal(len(f)).astype(np.float32), 12) * 0.15)
    tone = np.sin(2 * np.pi * np.cumsum(f) / SR).astype(np.float32)
    hiss = dsp.sweep_filter(rng.standard_normal(len(f)).astype(np.float32), "bp", f, q=25, block=64)
    flutter = np.clip(0.6 + dsp.lp(rng.standard_normal(len(f)).astype(np.float32), 15) * 12, 0, 1.3)
    y = (tone * 0.5 + norm(hiss)) * flutter
    return norm(y * env([(0, 0), (length * 0.15, 1), (length * 0.7, 0.7), (length, 0)], length))


def ember_fall(rng):
    """Charcoal collapsing: a spill of small glassy ticks thinning out, a few bigger ones in it."""
    L = rng.uniform(0.4, 0.8)
    return dsp.shaped(crackling(rng, L, env([(0, 220), (L, 15)], L), big=0.3, alpha=2.0), [(0, 1), (L, 0.2)])


def through_boards(y, rng, L, n):
    """Heard from outside the burning car: the fire's own box of reverb, folded round so the loop stays one cycle."""
    return dsp.wrap(dsp.room(y, "car", wet=0.3, rng=rng), n)


@recipe("tell-car-fire", "smoulder", "char",
        "Wood smouldering in a shut car: charring ticks in little flurries, a sap pop now and then, a pocket of sap singing",
        """A real wood fire's small sounds, synthesised from what they are: charring wood ticking as it cracks (tiny sharp
        sap ticks, a few loud and most faint, coming in little flurries and lulls), the odd bigger pop whose wood chunk
        rings for a moment, a board checking with a short crack, a pocket of sap boiling out through the end grain and
        singing thinly as it goes, and under it all a faint frying seethe of embers. No steam, no hiss bed. Inside a
        wooden car, heard through its boards. Held to 6-9 kHz.""",
        takes=1, loop=True, band=FIRE, lufs=-24, seconds=12)
def fire_smoulder(rng, k):
    L = 12.0
    n = samples(L)
    b = Bus(L)
    flurry = periodic(rng, L, 6, 0.0, 1.0) ** 3
    b.at(0, crackling(rng, L, 3 + 30 * flurry, big=0.08), 0)
    b.at(0, crackling(rng, L, 220 + 120 * periodic(rng, L, 3), big=0.0, alpha=3.0), -10)    # the seethe
    for _ in range(rng.integers(7, 12)):
        b.at(rng.uniform(0, L - 0.2), sap_pop(rng, True), rng.uniform(-3, 2))
    b.at(rng.uniform(1, L - 1), split(rng), -6)
    for t0 in (rng.uniform(1.0, 4.0), rng.uniform(6.5, 9.5)):
        b.at(t0, sap_sing(rng, rng.uniform(0.8, 1.6)), -16)
    y = through_boards(b.x, rng, L, n)
    return held(y, *FIRE, drive=9)


@recipe("tell-car-fire", "alight", "blaze",
        "The car alight: dense crackle riding the flames' lapping, pops, boards splitting, embers collapsing, a roar",
        """The same wood fire, gone: crackle thick enough to be a texture, its density riding the flames' lapping (flutters
        of 3-10 a second inside slow surges), big sap pops ringing their wood, a board splitting every couple of seconds
        (the crack running along the grain, then the board's knock), a spill of embers collapsing, sap singing in two places,
        and the roar itself as fluttering turbulence. Heard through the burning car's boards. Held to 6-9 kHz, with a
        little of the roar's low body left under it.""",
        takes=1, loop=True, band=FIRE, lufs=-20, seconds=12)
def fire_alight(rng, k):
    L = 12.0
    n = samples(L)
    b = Bus(L)
    surge = periodic(rng, L, 3, 0.45, 1.0)
    lap = np.clip(dsp.lp(rng.standard_normal(n).astype(np.float32), 9) * 18 + 0.55, 0.05, 1.6)
    flame = surge * lap
    b.at(0, crackling(rng, L, 120 + 480 * flame, big=0.04), 0)
    b.at(0, crackling(rng, L, 4 + 10 * surge, big=0.6, alpha=1.4), -1)                    # the pops
    roar = dsp.bp(synth.noise(L, rng, "pink"), 3000, 12000) * flame
    b.at(0, norm(roar), -17)
    t = rng.uniform(0.3, 1.0)
    while t < L - 0.3:
        b.at(t, split(rng), rng.uniform(-4, 0))
        t += rng.uniform(1.4, 3.0)
    b.at(rng.uniform(2, L - 2), ember_fall(rng), -3)
    for _ in range(2):
        b.at(rng.uniform(0.5, L - 2), sap_sing(rng, rng.uniform(0.7, 1.4)), -18)
    y = held(through_boards(b.x, rng, L, n), *FIRE, drive=9)
    # a little of the roar's low body, lapping with the flames, so it's a fire and not a hiss
    body = dsp.lp(synth.noise(L, rng, "brown"), 350) * flame
    return mix(y, body * 0.4 * rms(y) / rms(body))
