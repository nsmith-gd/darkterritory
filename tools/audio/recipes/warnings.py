"""The line's warnings and the gun crew's impacts: the cab's communication bell rung for overspeed (warn-overspeed), the
roof irons chattering on a bend taken too fast (warn-curve), the telltale cords before a tunnel (warn-low-clearance),
the roof gun's steam laying gear (crew-gun-lay), a cannonball coming down on earth, into water and through the Track
Doll (crew-cannon-impact), and a powder keg or a powder car going up (place-depot).

All of these replace synth definitions in content/audio/sounds/ at the same level, so each take is complete on its own.
They are gameplay foley and the train's own mechanics: the real thing, from the packs' real recordings where they have
it (a struck bell, light iron, whips and slaps, gravel, splinters, glass, thunder), cut, pitched to the size of the thing
and held in the band its warning needs; synthesis only where the packs have nothing (steam, the pressure of a blast,
water thrown up and falling back, the ring of a brass bell held longer than any recording here holds it).

The three warnings are tells (spec A.4): each holds its band (inBand >= 0.8 for the roof warnings) and is told from its
neighbours in that band by rhythm: the bell's double stroke, the irons' chatter rocking in two swells, the cords' sweep
of slaps.
"""

import numpy as np
from scipy.ndimage import minimum_filter1d

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit, Bus
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K

METAL_L, METAL_M, METAL_H = K("impactMetal_light"), K("impactMetal_medium"), K("impactMetal_heavy")


def stroke_tick(key, length=0.03, lo=1500):
    """The first instant of a real metal hit, its ring cut away: steel meeting steel (a clapper, a hammer, a pawl)."""
    x = ck.align(ck.get(key))
    x = ck.choke(x[:samples(length + 0.05)], 0.002, length / 5)[:samples(length)]
    return ck.norm(hp(x, lo, 2))


def peaks_under(y, lufs, ceiling=-5.0, look=0.003):
    """Level to `lufs` and hold the peaks under `ceiling` with a smooth look-ahead gain (no clipped edges), so build.py's
    levelling finds nothing to bend: a sparse rattle's loudest knock or a blast's first instant would otherwise cross its
    soft limiter and click."""
    w = samples(look)
    win = np.hanning(2 * w + 1)
    win /= win.sum()
    for _ in range(3):
        y = dsp.gain(y, lufs - dsp.loudness(y))
        need = np.minimum(1.0, dsp.db2a(ceiling) / (np.abs(y) + 1e-9))
        g = np.convolve(minimum_filter1d(need, 4 * w + 1), win, "same")
        y = (y * g).astype(np.float32)
    return dsp.gain(y, lufs - dsp.loudness(y))


# ---- warn-overspeed: the cab's communication bell ---------------------------------------------------------------------------

STRIKE = 1210.0           # the bell's strike note (warn-overspeed.json: 1.2 kHz, its bright pair at 2.6 and 4.1 kHz)
BELL_REC = "kenney_impact-sounds:impactBell_heavy_000"
BELL_REC_PRIME = 379.0    # that recording's loudest partial, which becomes the strike note


def double_stroke(rng, stroke, gap=(0.32, 0.36), second=(0.9, 1.0), length=1.3, ring_from=0.62, ring_tau=0.18):
    """Two strokes of one bell a beat apart, the first still ringing under the second, the whole dying away by `length`
    (the bell's own ring, hastened a little after `ring_from` so the warning is over before it's rung again)."""
    b = Bus(length + 0.2)
    b.at(0.0, stroke(rng, 1.0))
    b.at(rng.uniform(*gap), stroke(rng, rng.uniform(*second)))
    t = np.arange(len(b.x)) / dsp.SR
    tail = np.where(t < ring_from, 1.0, np.exp(-(t - ring_from) / ring_tau)).astype(np.float32)
    return b.x * tail


def bell_out(y):
    """The bell on the cab's roof plate, heard in the cab: held to its band (the bed is all below it) and the iron cab's
    short slap around it."""
    y = lp(hp(y, 850, 2), 5200, 2)
    return dsp.room(y, "cab", wet=0.12, rng=np.random.default_rng(3))


@recipe("warn-overspeed", "bell", "struck",
        "A real struck bell pitched to a small brass cab bell, rung twice, the clapper's steel tick on each stroke",
        """Kenney's heavy bell recording (a real struck bell with its beating partials) pitched up 20 semitones without
        shortening its ring, so its loudest partial sits at the bell's 1.2 kHz strike note and its cluster of upper
        partials at 2.1-2.4 and 3.5 kHz: a small brass bell. Struck twice a third of a second apart (the second a little
        softer or as hard, the first still ringing under it), each stroke with the clapper's tick (the first instant of a
        real light steel hit). Held to 0.85-5 kHz, the band the train's bed doesn't fill, in the iron cab's short slap.
        Takes differ in the spacing, the force of the second stroke and where the clapper meets the bell.""",
        sources=[BELL_REC] + METAL_L, takes=3, band=(1000, 4500))
def bell_struck(rng, k):
    ring = ck.norm(dsp.shift(ck.align(ck.get(BELL_REC)), 12 * np.log2(STRIKE / BELL_REC_PRIME)))
    tick = stroke_tick(METAL_L[(2 * k) % 5], 0.02)
    tilt = rng.uniform(-2, 2)        # struck nearer the lip rings brighter

    def stroke(rng, force):
        r = ck.tilt(ring, hi_db=tilt + 3 * (force - 1), hi_f=2000)
        return mix(r * force, tick * 0.45 * force ** 1.5)

    return bell_out(double_stroke(rng, stroke))


# The brass bell's modes as (ratio to the strike, decay s, level): the hum an octave under, the strike, the bright pair
# at 2.17 and 3.38 times it, and the minor partials between; then the strike's clang, a few partials that are gone in
# tens of milliseconds (what makes a stroke a stroke). Each is a doublet (a real bell is never quite round), so it
# shimmers as it rings.
BRASS = [(0.5, 1.1, 0.18), (1.0, 0.75, 1.0), (1.47, 0.42, 0.22), (2.17, 0.5, 0.55), (2.66, 0.3, 0.2), (3.38, 0.32, 0.4),
         (4.02, 0.16, 0.14), (4.91, 0.1, 0.08),
         (1.73, 0.05, 0.5), (2.93, 0.04, 0.45), (3.71, 0.03, 0.4), (1.29, 0.07, 0.3)]


def brass_modes(rng, f=STRIKE, lip=0.0):
    """One small brass bell's modes: (freqs, decays, levels), each partial a doublet, `lip` brightening the strike."""
    fs, ds, am = [], [], []
    for r, d, a in BRASS:
        split = rng.uniform(0.0008, 0.0018)
        for s, g in ((1 - split, 1.0), (1 + split, rng.uniform(0.5, 0.9))):
            fs.append(f * r * s)
            ds.append(d * rng.uniform(0.9, 1.1))
            am.append(a * g * (1 + lip * (r - 1) * 0.2))
    return fs, ds, am


@recipe("warn-overspeed", "bell", "brass",
        "A small brass bell's modes (1.2 kHz strike, 2.6 and 4.1 kHz bright pair) rung twice by a real steel hammer",
        """Where the packs have no small bell, its physics: the strike note at 1.2 kHz with its hum an octave under and its
        bright partials at 2.6 and 4.1 kHz (the synth's tuning), each a doublet a couple of hertz apart so it shimmers
        the way a real cast bell does, the high ones dying first. The hammer is real: the first strike of Kenney's medium
        metal hits (steel on steel, its own ring at 1 and 3 kHz) laid over each stroke, and its light metal ticks for the
        contact. Two strokes a third of a second apart, the iron cab's slap around them. Takes differ in the spacing,
        the force and where the hammer lands.""",
        sources=METAL_M + METAL_L, takes=3, band=(1000, 4500))
def bell_brass(rng, k):
    modes = brass_modes(rng, STRIKE, rng.uniform(0, 1))
    hammer = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(METAL_M[(k + 3) % 5]), 1.5)), 0.02, 0.04))
    tick = stroke_tick(METAL_L[(k + 1) % 5], 0.015)

    def stroke(rng, force):
        ring = ck.norm(W.modal(*modes, 1.6, rng, 0.00022 / force))
        return mix(ring * force, hammer * 0.5 * force, tick * 0.35 * force ** 1.5)

    return bell_out(double_stroke(rng, stroke, ring_tau=0.15))


# ---- warn-curve: the roof irons chattering ---------------------------------------------------------------------------------

CURVE_BAND = (2500, 4500)
RATTLE = S("metal_03")             # a real loose-metal rattle: five knocks in a seventh of a second
JANGLE = S("misc_09", "metal_05")  # keys and small iron jangling


def rocking(rng, length=1.8, rate=1.4):
    """The car rocking into the lean twice: two swells about `rate` Hz apart, the second the stronger, each rising slower
    than it falls (the car leans over, then snaps back against its springs)."""
    p1 = rng.uniform(0.36, 0.48)
    p2 = p1 + 1 / rate * rng.uniform(0.94, 1.06)
    dip = rng.uniform(0.08, 0.18)
    return env([(0, 0.3), (p1 - 0.28, 0.45), (p1, rng.uniform(0.75, 0.85)), (p1 + 0.24, dip), (p2 - 0.26, 0.45),
                (p2, 1.0), (p2 + 0.3, 0.25), (length, 0.0)], length)


def knocks(rng, swell, rate, bounce=0.5, gap=(0.012, 0.025), first=0.005):
    """When a loose fitting knocks in its socket: a Poisson run at up to `rate` a second, thinned by the swell (it knocks
    harder and more often as the car leans), a knock sometimes chattering once or twice more as it settles; the first
    knock at `first` s (the irons start as the warning goes up). Returns [(t, level), ...]."""
    L = len(swell) / dsp.SR
    out, t = [], first
    while t < L:
        s = float(swell[min(samples(t), len(swell) - 1)])
        if not out or rng.random() < s ** 1.3:
            g = s ** 0.8 * rng.uniform(0.35, 1.0)
            out.append((t, g))
            tb = t
            while rng.random() < bounce and g > 0.05:
                tb += rng.uniform(*gap)
                g *= rng.uniform(0.35, 0.6)
                out.append((tb, g))
        t += rng.exponential(1 / rate)
    return out


def clink(key, i, semis=0.0, tau=0.03):
    """One knock cut from a real metal recording: its i-th hit, pitched, its ring damped (the fitting is bolted down)."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-24, gap=0.015)
    a = h[i % len(h)][0] if h else 0
    c = ck.cut(x, a - samples(0.001), a + samples(5 * tau), 0.0005, 0.01)
    c = dsp.vari(c, semis) if semis else c
    return ck.norm(hp(ck.choke(c, 0.004, tau), 1800, 2))


def roof_air(y, band, length=None):
    """Up on the roof in the open: dry but for the slaps off the cupola and the next car's end, held to the tell's band
    and made safe to level (its loudest knock under the limiter), over by `length`."""
    for dt, g in ((0.0047, 0.3), (0.0123, 0.18)):
        k = samples(dt)
        y[k:] += y[:-k] * g
    y = dsp.band(y, *band)
    if length:
        y = fit(y, samples(length + 0.05)) * env([(0, 1), (length - 0.15, 1), (length + 0.05, 0)], length + 0.05)
    return peaks_under(y, -20)


@recipe("warn-curve", "chatter", "irons",
        "The roof irons' real rattle: loose stanchions and brackets knocking in their sockets, rocking in two swells",
        """Each roof fitting is a real recording: two handrail stanchions are Kenney's light metal hits (iron rods
        ringing at 2.4-4.6 kHz) damped short as a bolted part is, a lamp bracket is sfx_100's loose-metal rattle cut into
        its knocks, the ladder's top iron is its jangle of small iron. Each knocks in its socket at its own uneven rate,
        sometimes chattering once or twice more as it settles, harder and faster as the car leans: two slow swells about
        1.4 Hz apart, the second the stronger, nearly quiet between (the rhythm that tells it from a scrape or a
        scrabble). Held to 2.5-4.5 kHz.""",
        sources=METAL_L + RATTLE + JANGLE, takes=3, band=CURVE_BAND)
def chatter_irons(rng, k):
    L = 1.8
    swell = rocking(rng, L)
    fittings = [
        ([clink(METAL_L[(k + j) % 5], 0, rng.uniform(-1, 1), 0.025) for j in range(2)], 24, 0.55),
        ([clink(METAL_L[(k + 2 + j) % 5], 0, rng.uniform(1, 3), 0.02) for j in range(2)], 18, 0.5),
        ([clink(RATTLE[0], i, rng.uniform(-2, 0), 0.012) for i in range(5)], 28, 0.6),
        ([clink(JANGLE[(k + j) % 2], i, rng.uniform(-3, -1), 0.015) for j in range(2) for i in range(4)], 16, 0.4),
    ]
    b = Bus(L + 0.3)
    for (bank, rate, bounce), db in zip(fittings, (0, -2, -1, -5)):
        for t, g in knocks(rng, swell, rate, bounce):
            b.at(t, bank[int(rng.integers(len(bank)))] * g, db)
    return roof_air(b.x, CURVE_BAND, L)


def socket_modes(rng):
    """A bolted iron fitting's few ringing modes, all inside the tell's band (a short rod and its foot)."""
    f1 = rng.uniform(2650, 3200)
    return [f1, f1 * rng.uniform(1.18, 1.3), f1 * rng.uniform(1.38, 1.5)]


@recipe("warn-curve", "chatter", "sockets",
        "Three loose roof irons bouncing in their sockets: short settling chatters, faster and denser as the car leans",
        """The physics of a rod loose in a socket: each jolt lifts it and it chatters down, a run of knocks each sooner
        and softer than the last (a bouncing settle, 'trrk'). Three fittings, each its own few iron modes in 2.6-4.4 kHz
        damped short (bolted iron), struck through the first instant of a real light metal hit, so every knock is steel
        on steel. The jolts come as the car rocks into the lean: two slow swells about 1.4 Hz apart, the second the
        harder, where the chatters crowd into a rattle, nearly quiet between. Held to 2.5-4.5 kHz.""",
        sources=METAL_L, takes=3, band=CURVE_BAND)
def chatter_sockets(rng, k):
    L = 1.8
    swell = rocking(rng, L)
    b = Bus(L + 0.3)
    for j in range(3):
        modes = socket_modes(rng)
        tick = stroke_tick(METAL_L[(k + j) % 5], 0.004, 2000)
        rung = ck.norm(dsp.resonate(fit(tick, samples(0.06)), modes, q=rng.uniform(70, 110), gains=[1, 0.7, 0.5]))
        knock = ck.norm(mix(rung, tick * 0.3))
        t = 0.005 + 0.03 * j       # every fitting jolts as the lean starts
        while t < L:
            s = float(swell[min(samples(t), len(swell) - 1)])
            t0, t = t, t + rng.exponential(1 / 11.0)
            if t0 > 0.1 and rng.random() > s ** 1.2:
                continue
            # the settle: a bounce's gaps shrink geometrically as the rod loses its lift
            gap, g, tb = rng.uniform(0.028, 0.045) * (0.6 + 0.5 * s), s * rng.uniform(0.6, 1.0), t0
            for _ in range(int(3 + 5 * s)):
                b.at(tb, dsp.vari(knock, rng.uniform(-0.3, 0.3)) * g, -2 * j)
                tb += gap
                gap *= rng.uniform(0.66, 0.78)
                g *= rng.uniform(0.62, 0.8)
    return roof_air(b.x, CURVE_BAND, L)


# ---- warn-low-clearance: the telltales -------------------------------------------------------------------------------------

TELL_BAND = (4000, 7000)
WHIPS = S("switch_01", "switch_02")                                     # a real whip's crack and slap
FLAPS = R("bookFlip1", "bookFlip3", "clothBelt", "knifeSlice2")          # leather and paper slapping flat
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4", "dropLeather")         # a coat being struck and brushed
DRAG = R("drawKnife1", "drawKnife2")                                     # something drawn fast across a surface


def passing(rng, length=1.7):
    """The row of cords coming down the roof to you and away: a quick rise as they hit the roof ahead, loudest across
    your shoulders, thinning as they go over the back of the car. Returns (envelope, time you're hit)."""
    at = rng.uniform(0.45, 0.6)
    return env([(0, 0.25), (at * 0.5, 0.6), (at, 1.0), (at + 0.25, rng.uniform(0.7, 0.85)), (at + 0.6, 0.35),
                (length - 0.15, 0.1), (length, 0)], length), at


def run_of(rng, length, rate, uneven=0.8, first=0.004):
    """An uneven run of times at about `rate` a second: some cords nearly together, some gaps."""
    t, out = first, []
    while t < length:
        out.append(t)
        t += (1 / rate) * max(0.15, 1 + uneven * rng.standard_normal() * 0.6)
    return out


def tin_crack(rng, length=0.04):
    """A cord's end cracking on the roof sheet: a hard little burst ringing the tin's top modes."""
    x = synth.noise(0.004, rng) * np.hanning(samples(0.004)).astype(np.float32)
    fs = [f * rng.uniform(0.95, 1.05) for f in (4350, 4900, 5600, 6300)]
    y = dsp.resonate(fit(x, samples(length)), fs, q=rng.uniform(25, 45), gains=[1, 0.8, 0.6, 0.45])
    return ck.norm(mix(y, fit(x, samples(length)) * 0.5))


def flick(key, i, semis, length=0.09):
    """One slap cut from a real recording, sped up to a cord's weight and lightness."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-20, gap=0.03)
    a = h[i % len(h)][0] if h else 0
    c = ck.cut(x, a - samples(0.001), a + samples(length * 2 ** (semis / 12)), 0.0005, 0.02)
    return ck.norm(hp(dsp.vari(c, semis), 2500, 2))


@recipe("warn-low-clearance", "telltales", "whips",
        "The telltale cords whipping down the roof and across your shoulders: real whip cracks and slaps, sped up",
        """Each cord is a real slap: sfx_100's two whip cracks and Kenney's leather and paper slaps, sped up 2-6
        semitones to a light cord, in an uneven run (about 13 a second, some nearly together, some gaps) that comes down
        the roof at you, is loudest as it crosses your shoulders and thins out over the back of the car. Where a cord's end
        lands on the roof it cracks on the tin (a hard burst through the sheet's top modes); under them all, the cords
        dragging over the tin (a real knife drawn fast, sped up). Held to 4-7 kHz.""",
        sources=WHIPS + FLAPS + DRAG, takes=3, band=TELL_BAND)
def telltales_whips(rng, k):
    L = 1.7
    sweep, at = passing(rng, L)
    bank = [flick(w, 0, s) for w in WHIPS for s in (2, 5)] + [flick(f, i, rng.uniform(2, 6)) for f in FLAPS for i in range(2)]
    b = Bus(L + 0.3)
    for t in run_of(rng, L - 0.1, 13):
        s = float(sweep[min(samples(t), len(sweep) - 1)])
        b.at(t, bank[int(rng.integers(len(bank)))] * s * rng.uniform(0.45, 1.0))
        if abs(t - at) > 0.12 and rng.random() < 0.55:       # a cord's end on the roof, not on you
            b.at(t + rng.uniform(0.004, 0.02), tin_crack(rng) * s * rng.uniform(0.3, 0.7))
    drag = flick(DRAG[k % 2], 0, 7, 0.4)
    bed = fit(np.concatenate([drag, dsp.reverse(drag), drag, dsp.reverse(drag)]), samples(L)) * sweep
    b.at(0.0, bed * 0.25)
    return roof_air(b.x, TELL_BAND, L)


@recipe("warn-low-clearance", "telltales", "cords",
        "The telltales as soft cords: cloth and leather flaps sped up, brushing and slapping over the tin and your coat",
        """The cords as rope and cloth rather than whips: Kenney's cloth and leather handlings sped up an octave and more
        (a coat struck becomes a light cord's flap), in clusters of two or three nearly together, as a row of cords hits
        at once, about seven clusters a second, uneven; each cord's end cracking on the roof tin (a short bright burst
        through the sheet's top modes) as it lands ahead of you and again as it goes over the back. A sweep: rising as
        they come down the roof, loudest across your shoulders, gone over the back. Held to 4-7 kHz.""",
        sources=CLOTH, takes=3, band=TELL_BAND)
def telltales_cords(rng, k):
    L = 1.7
    sweep, at = passing(rng, L)
    bank = [flick(c, i, rng.uniform(11, 15), 0.12) for c in CLOTH for i in range(3)]
    b = Bus(L + 0.3)
    for t in run_of(rng, L - 0.1, 7, uneven=0.6):
        s = float(sweep[min(samples(t), len(sweep) - 1)])
        for j in range(int(rng.integers(1, 4))):
            tj = t + j * rng.uniform(0.012, 0.035)
            b.at(tj, bank[int(rng.integers(len(bank)))] * s * rng.uniform(0.5, 1.0))
            if rng.random() < (0.25 if abs(t - at) < 0.15 else 0.6):
                b.at(tj + rng.uniform(0.002, 0.01), tin_crack(rng) * s * rng.uniform(0.35, 0.8))
    return roof_air(b.x, TELL_BAND, L)


# ---- crew-gun-lay: the roof gun's steam laying gear --------------------------------------------------------------------------

LAY = 8.0                  # one loop (s)
REV = 2.5                  # the little engine's revolutions a second at a medium traverse (20 to the loop: it loops)
TEETH = S("misc_20", "lock_open_01") + R("handleCoins2")     # real ratchets and mechanisms, cut into single teeth


def strokes(rng, rev=REV, length=LAY):
    """A small twin double-acting engine's four exhausts a revolution, never quite even (the cranks aren't quite at
    90 degrees, one cylinder is a little stronger): [(t, strength), ...] over the loop."""
    lag = rng.uniform(0.02, 0.05)
    accent = [1.0, rng.uniform(0.6, 0.75), rng.uniform(0.85, 0.95), rng.uniform(0.55, 0.7)]
    out = []
    for r in range(int(round(length * rev))):
        for j, ph in enumerate((0, 0.25 + lag, 0.5, 0.75 + lag)):
            out.append(((r + ph) / rev, accent[j] * rng.uniform(0.9, 1.05)))
    return out


def pulses(n, beats, rise=0.003, decay=0.035, floor=0.0):
    """A periodic pressure curve: each beat a fast rise and an exponential fall, wrapped round the loop."""
    L = 0.3
    t = np.arange(samples(L)) / dsp.SR
    shape = ((1 - np.exp(-t / rise)) * np.exp(-t / decay)).astype(np.float32)
    shape /= shape.max()
    p = W.place(n, [(tb, shape, g) for tb, g in beats])
    return np.maximum(p, 0) + floor


def tooth(key, i, semis, tau=0.012):
    """One gear tooth knocking over: a single click cut from a real ratchet, pitched heavier and damped."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-24, gap=0.015)
    a = h[i % len(h)][0] if h else 0
    c = ck.cut(x, a - samples(0.001), a + samples(0.06), 0.0005, 0.01)
    return ck.norm(ck.choke(dsp.vari(c, semis), 0.003, tau))


def loop_of(key, n, semis=0.0, xf=0.4):
    """A recording made into an exact n-sample cycle: varispeeded, tiled with crossfades, its seam folded away."""
    x = W.rec(key, semis=semis)
    k = samples(xf)
    out = np.zeros(n + len(x) + k, np.float32)
    fade = np.sin(np.linspace(0, np.pi / 2, k)).astype(np.float32) ** 2
    pos = 0
    while pos < n + k:
        seg = x.copy()
        seg[:k] *= fade
        seg[-k:] *= fade[::-1]
        out[pos:pos + len(seg)] += seg
        pos += len(seg) - k
    return dsp.wrap(out[:n + k], n)


@recipe("crew-gun-lay", "lay", "engine",
        "The laying gear as a little steam engine: sharp exhaust chuffs, the valve gear's clack, worm teeth knocking",
        """A small twin steam engine like a ship's steering engine, at a medium traverse (2.5 turns a second): four
        exhaust chuffs a turn, uneven as a real twin's are (jet noise from a small pipe, each a fast rise and a quick
        fall, over a steady leak at the glands), the pistons' dull knock under each, the valve gear clacking twice a
        turn (Kenney's heavy metal hit, small and choked). It drives a worm: the gear's teeth knock over six times a
        turn (single clicks cut from the packs' real ratchets, pitched heavier, louder just after each chuff when the
        torque comes on) over the worm's grinding whirr. In its iron casing. One 8 s cycle.""",
        sources=TEETH + METAL_H, loop=True, takes=1, seconds=LAY)
def lay_engine(rng, k):
    n = samples(LAY)
    beats = strokes(rng)
    p = pulses(n, beats, decay=0.038, floor=0.05)
    chuff = W.jet(LAY, rng, pressure=p, peak=2300, low=0.3, eddy=0.6, n=n)
    leak = W.jet(LAY, rng, pressure=0.35, peak=5200, low=0.0, eddy=0.9, n=n)
    knock = [(t, W.knock(rng, 70, 0.1, 0.3), g) for t, g in beats]
    clack = ck.choke(ck.align(dsp.vari(ck.get(METAL_H[1]), -2)), 0.004, 0.012)
    clacks = [(r / (2 * REV) + 0.06, hp(dsp.vari(clack, rng.uniform(-0.5, 0.5)), 300), rng.uniform(0.6, 1.0))
              for r in range(int(LAY * REV * 2))]
    bank = [tooth(TEETH[j % 3], i, rng.uniform(-6, -4)) for j in range(3) for i in range(3)]
    teeth = []
    for r in range(int(LAY * REV * 6)):
        t = r / (6 * REV) + rng.normal(0, 0.002)
        ph = (t * 4 * REV) % 1          # where it falls between exhausts: the knock is hardest just after one
        teeth.append((t, bank[int(rng.integers(len(bank)))], (0.45 + 0.55 * np.exp(-ph * 4)) * rng.uniform(0.7, 1.0)))
    whirr = W.friction(n, rng, [210, 470, 820, 1300], rough=0.8, grit=0.15, q=10, loop=True)
    turn = 1 + 0.35 * np.sin(2 * np.pi * REV * np.arange(n) / dsp.SR)
    whirr = W.cfilter(whirr, lambda f: 1 / (1 + (f / 1600) ** 2)) * turn.astype(np.float32)
    y = (W.norm(chuff) * 1.0 + W.norm(leak) * 0.05 + W.norm(W.place(n, knock)) * 0.3 + W.norm(W.place(n, clacks)) * 0.22
         + W.norm(W.place(n, teeth)) * 0.4 + W.norm(whirr) * 0.12)
    return W.seamless(W.croom(y, "box", 0.15, rng))


@recipe("crew-gun-lay", "lay", "works",
        "The laying gear from real machinery: a running engine's knock, real steam hiss chopped by its strokes, a ratchet",
        """Recorded machinery throughout. The motor is the packs' running-engine loop (its knocking and rattle) pitched
        down to a heavier, slower machine and darkened; its steam is the packs' real steam-hiss loop chopped into the
        strokes (four a turn at 2.2 turns a second, each a quick rise and a fall, never quite to nothing: the glands
        leak); the worm gear is single teeth cut from the packs' real ratchets and lock, knocking over in step with the
        motor, over the packs' motor-hum loop pitched down to a low whirr. One 8 s cycle.""",
        sources=S("loop_machine_04", "loop_ambient_01", "loop_machine_01") + TEETH, loop=True, takes=1, seconds=LAY)
def lay_works(rng, k):
    n = samples(LAY)
    rev = 2.25
    beats = strokes(rng, rev)
    motor = W.cfilter(loop_of("sfx_100_v2:loop_machine_04", n, -5), lambda f: 1 / (1 + (f / 1400) ** 2))
    hiss = W.cfilter(loop_of("sfx_100_v2:loop_ambient_01", n, -2), lambda f: (f / 700) / (1 + f / 700) / (1 + (f / 7000) ** 2))
    gate = pulses(n, beats, rise=0.004, decay=0.05, floor=0.0) ** 1.5 + 0.05
    hum = W.cfilter(loop_of("sfx_100_v2:loop_machine_01", n, -6), lambda f: 1 / (1 + (f / 500) ** 2))
    bank = [tooth(TEETH[j % 3], i, rng.uniform(-4, -2), 0.015) for j in range(3) for i in range(4)]
    teeth = [(r / (5 * rev) + rng.normal(0, 0.003), bank[int(rng.integers(len(bank)))], rng.uniform(0.5, 1.0))
             for r in range(int(LAY * rev * 5))]
    y = (W.norm(motor) * 0.2 + W.norm(hiss * gate) * 1.0 + W.norm(hum) * 0.2 + W.norm(W.place(n, teeth)) * 0.45)
    return W.seamless(W.croom(y, "box", 0.12, rng))


# ---- crew-cannon-impact: where the ball comes down --------------------------------------------------------------------------

MINING = K("impactMining")
GRAVEL = S("footstep_01", "footstep_02")
STONES = S("stones_01", "stones_02", "stones_03")
SPLINTER = S("misc_35", "misc_34")       # real wood cracking and breaking
THUNDER = "sfx_100_v2:thunder_01"


def shaped(x, points, length=None):
    """x under an envelope [(t, level), ...], cut or padded to `length`."""
    L = length or len(x) / dsp.SR
    return fit(x, samples(L)) * env(points, L)


def over_by(y, length, tail=0.5):
    """Let a long tail (the land's echo, the reverb) die out so the take is over by `length` s."""
    y = fit(y, samples(length))
    return y * env([(0, 1), (length - tail, 1), (length, 0)], length) ** 2


def pebbles(rng, length, rate, decay=0.5, semis=(-2, 3)):
    """Stones coming down among the earth: single cracks cut from the packs' real stones and gravel, landing at a rate
    that dies away over `decay` s (W.debris's 'brick' would bring Kenney's mining thuds, too heavy for thrown stones)."""
    keys = STONES + GRAVEL
    b = Bus(length + 0.4)
    t = 0.0
    while True:
        t += rng.exponential(1 / (rate * np.exp(-t / decay) + 0.3))
        if t >= length:
            return b.x
        c = clink(keys[int(rng.integers(len(keys)))], int(rng.integers(4)), rng.uniform(*semis), rng.uniform(0.02, 0.05))
        b.at(t, c * rng.uniform(0.25, 1.0) * np.exp(-t / (decay * 2)))


def earth_fall(rng, length, start=0.25, peak=0.65, rate=160, size=1.0):
    """Earth thrown up coming back down: clods landing (rain's physics on the ground: each a soft splat, a few big, most
    small, `rate` a second at the thickest), from `start`, thickest at `peak`, thinning out, a finer shower of grit
    hissing among them, and real stones knocking on each other."""
    n = samples(length)
    shape = env([(0, 0), (start, 0), (peak, 1), (peak + (length - peak) * 0.4, 0.3), (length, 0)], length)
    clods = W.rain(n, rng, rate, "ground", loop=False) * shape
    grit = W.rain(n, rng, rate * 6, "ground", loop=False) * shape
    stones = pebbles(rng, length - start - 0.2, 9 * size, decay=0.5)
    stones = np.concatenate([np.zeros(samples(start + 0.05), np.float32), W.norm(lp(stones, 6000, 2))])
    return mix(W.norm(clods), W.norm(grit) * 0.2, stones * 0.35, length=n)


def burst(rng, f=38, T=0.012, decay=0.11, crack=0.7, roar=0.5):
    """A burst's pressure wave heard from off the train: a Friedlander pulse (an instant rise, the overpressure falling
    away over `T`, the shallow suction after), the gas's short roar, the sub of it dropping in pitch, the case's crack.
    Short: what carries on after it is the land's echo and what it threw up, not the blast."""
    L = 0.9
    n = samples(L)
    t = np.arange(n) / dsp.SR
    fr = lp(((1 - t / T) * np.exp(-1.3 * t / T) * np.exp(-t / decay)).astype(np.float32), 1200, 2)
    sub = synth.thump(f, 0.5, drop=0.45)
    cr = hp(synth.noise(L, rng), 500) * np.exp(-t / 0.007).astype(np.float32)
    ro = lp(synth.noise(L, rng, "pink"), 1800, 2) * np.exp(-t / (decay * 0.8)).astype(np.float32)
    return mix(W.norm(fr), W.norm(sub) * 0.6, W.norm(cr) * crack, W.norm(ro) * roar)


def out_there(y, rng, wet=0.22):
    """Out in the open a little way off the train: the slaps off the train's side, the night around it."""
    return W.space(y, rng, "night", wet=wet)


def finish(y, lufs=-16, lo=32):
    """A big impact made ready for the game: the rumble under `lo` cut (the weight is felt at 40-150 Hz; under that it
    only eats headroom), levelled, its first instant held under the limiter."""
    return peaks_under(hp(y, lo, 2), lufs, ceiling=-4)


@recipe("crew-cannon-impact", "ground", "burst",
        "The ball bursting on earth: a crack and a deep boom rolling off the land, then earth and stones raining down",
        """An exploding ball heard from the train: the burst's pressure wave (an instant rise and the long suction after
        it, with a sub falling under it and the crack of the case), driven a little into saturation the way a blast
        overloads the ear, and the land throwing it back (the packs' real thunder, from its peak on, tightened two
        semitones). The earth it throws up: real gravel crunched at the burst, then clods and grit coming back down for
        a second and a half (thousands of small soft impacts, a few big) with real stones knocking among them. Takes
        differ in the burst's size and the earth's fall.""",
        sources=[THUNDER] + GRAVEL + STONES, takes=3, lufs=-16)
def ground_burst(rng, k):
    L = 2.4
    blast = burst(rng, rng.uniform(34, 42), rng.uniform(0.008, 0.014), rng.uniform(0.16, 0.22), roar=0.7)
    blast = dsp.saturate(W.norm(ck.tilt(blast, lo_db=-8, lo_f=110)), 6)
    # the land's echo comes back a moment after, from the cuttings and the tree line
    roll = W.rec(THUNDER, semis=2 + rng.uniform(-0.5, 0.5), start=rng.uniform(0.78, 0.9), length=2.2)
    roll = shaped(roll, [(0, 0), (0.12, 0.3), (0.3, 1), (0.7, 0.45), (2.0, 0.05)], 2.0)
    crunch = mix(*[shaped(W.rec(GRAVEL[(k + j) % 2], semis=-5 - 2 * j), [(0, 1), (0.2, 0.3), (0.35, 0)], 0.35)
                   for j in range(2)])
    earth = earth_fall(rng, L, start=rng.uniform(0.22, 0.3), peak=rng.uniform(0.55, 0.75), rate=rng.uniform(140, 200))
    b = Bus(L + 0.5)
    b.at(0, blast, 0)
    b.at(0.02, W.norm(roll), -10)
    b.at(0.005, W.norm(crunch), -8)
    b.at(0, earth, -8)
    return finish(over_by(out_there(b.x, rng, 0.15), L))


@recipe("crew-cannon-impact", "ground", "shot",
        "A solid ball ploughing into earth: a deep heavy thud, earth thrown up, stones and splinters pattering down",
        """A solid iron ball landing rather than bursting: the ground's deep thud (Kenney's heavy mining impact, real
        iron into rock, pitched down an octave and more for the mass, over a sub thump and the dull weight of earth
        taking it), the earth thrown up in a dark whump with real gravel crunching, a sleeper or a fence rail splintering
        (the packs' real wood breaking), then earth, stones and splinters coming back down for a second and a half.
        The thud rolls off the land in the night's reverb. Takes differ in where it lands and what it throws up.""",
        sources=MINING + GRAVEL + STONES + SPLINTER + W.PIECES["wood"], takes=3, lufs=-16)
def ground_shot(rng, k):
    L = 2.3
    thud = ck.choke(W.rec(MINING[k % 5], semis=-7 + rng.uniform(-1, 1)), 0.12, 0.12)
    sub = W.knock(rng, 48, 0.4, 0.45)
    pad = ck.pad(rng, 0.35, 200)
    whump = shaped(lp(synth.noise(0.5, rng, "pink"), 700, 2), [(0, 0), (0.01, 1), (0.08, 0.4), (0.5, 0)], 0.5)
    crunch = shaped(W.rec(GRAVEL[k % 2], semis=-7), [(0, 1), (0.25, 0.4), (0.4, 0)], 0.4)
    split = W.rec(SPLINTER[k % 2], semis=-3 + rng.uniform(-1, 1))
    split = split[ck.hits(split, -12)[0][0]:] if ck.hits(split, -12) else split
    earth = earth_fall(rng, L, start=rng.uniform(0.18, 0.25), peak=rng.uniform(0.45, 0.6), rate=rng.uniform(110, 160))
    wood = W.debris(rng, 1.2, 6, {"wood": 1.0}, decay=0.5, semis=(-2, 3), bounce=0.4)
    b = Bus(L + 0.5)
    body = ck.tilt(mix(W.norm(thud), W.norm(sub) * 0.4, pad * 0.5), lo_db=-6, lo_f=110)
    b.at(0, dsp.saturate(W.norm(body), 6), 0)
    b.at(0.004, W.norm(whump), -4)
    b.at(0.01, W.norm(crunch), -6)
    b.at(0.03 + rng.uniform(0, 0.03), W.norm(split), -11 if k != 1 else -7)
    b.at(0, earth, -8)
    b.at(0.3 + rng.uniform(0, 0.1), W.norm(lp(wood, 5000, 2)), -14)
    return finish(over_by(out_there(b.x, rng, 0.2), L))


SPLASHES = S("footstep_wet_03", "footstep_wet_02") + ["kenney_impact-sounds:footstep_snow_000"]
SLOSH = "sfx_100_v2:loop_machine_02"      # water sloshing and splashing in a drum: its loudest splash, 5.2-5.7 s
STREAM = "sfx_100_v2:loop_water_02"       # a stream pouring and trickling
SURF = "sfx_100_v2:loop_water_01"         # broad moving water


def cavity(rng, f0=85, delay=0.1):
    """The air the ball drags down pinching off and collapsing: one huge bubble's Minnaert ring (a deep 'bloomp' that
    rises in pitch as it shrinks), a smaller one after it, and the dull push of the water closing."""
    big = synth.bubble(f0 * rng.uniform(0.9, 1.1), length=0.45, rise=rng.uniform(0.25, 0.45))
    small = synth.bubble(f0 * rng.uniform(2.1, 2.8), length=0.25, rise=0.6, amp=0.4)
    b = Bus(0.8)
    b.at(delay, big)
    b.at(delay + rng.uniform(0.05, 0.1), small)
    b.at(delay * 0.8, synth.thump(f0 * 0.6, 0.3, drop=0.3) * 0.5)
    return b.x


def fall_back(rng, length, start=0.35, peak=0.7, rate=500):
    """The column of spray coming back down onto the water: drops striking the surface (a hiss of tiny splashes) and
    some of them ringing a bubble each (every one its own size, so its own pitch), thickest a moment after the column
    tops out, thinning out."""
    n = samples(length)
    shape = env([(0, 0), (start, 0), (peak, 1), (peak + 0.35, 0.45), (length, 0)], length)
    hiss = bp(W.rain(n, rng, rate * 8, "ground", loop=False), 1500, 9000, 2)
    plinks = synth.bubbles(length, rate * shape + 1, 1200, 7000, rng, rise=(0.1, 0.5))
    return (W.norm(hiss) + W.norm(plinks) * 0.35) * shape


@recipe("crew-cannon-impact", "water", "plunge",
        "A ball into water: the smack on the surface, the deep gulp of the air it drags down, spray raining back",
        """The ball hits the surface flat (a real slap, sfx_100's whip-crack, pitched down an octave into a heavy smack)
        and throws up a sheet of water (the packs' real splash from a sloshing drum, pitched down for its size); the air
        it drags under pinches off and collapses in a deep 'bloomp' (one big bubble's ring, rising as it shrinks, and a
        smaller one after); then the column falls back: the packs' real pouring stream for its weight and thousands of
        drops landing (each a tiny bubble), thickest just after it tops out and thinning away. Out on open water.""",
        sources=[SLOSH, STREAM] + WHIPS, takes=3, lufs=-16)
def water_plunge(rng, k):
    L = 1.9
    smack = lp(dsp.vari(flick(WHIPS[k % 2], 0, 0, 0.2), -12 + rng.uniform(-1, 1)), 3500, 2)
    semis = -4 + rng.uniform(-1, 1)
    sheet = W.rec(SLOSH, semis=semis, start=5.2 + 0.08 * k, length=0.6)
    sheet = dsp.peak(sheet, 190 * 2 ** (semis / 12), 8, -15)      # the drum's motor hum under the splash
    sheet = shaped(sheet, [(0, 0), (0.02, 1), (0.25, 0.7), (0.6, 0)], 0.6)
    pour = W.rec(STREAM, semis=-3, start=rng.uniform(0.5, 6.0), length=L)
    pour = shaped(pour, [(0, 0), (0.35, 0), (0.6, 1), (1.0, 0.5), (L, 0)], L)
    b = Bus(L + 0.5)
    b.at(0, W.norm(smack), -3)
    b.at(0.01, W.norm(sheet), -4)
    b.at(0, W.norm(cavity(rng, rng.uniform(70, 95), rng.uniform(0.08, 0.14))), 0)
    b.at(0, W.norm(pour), -9)
    b.at(0, W.norm(fall_back(rng, L, rng.uniform(0.3, 0.4), rng.uniform(0.6, 0.8))), -8)
    return finish(over_by(out_there(b.x, rng, 0.15), L), lo=35)


@recipe("crew-cannon-impact", "water", "column",
        "A ball into water from small real splashes slowed down huge: the splash, its gulp, the water crashing back",
        """Real splashes made big the way film does it: the packs' wet footsteps and slush step, slowed an octave and
        more so a foot's splash becomes a ball's (slowing a splash makes it bigger, the drops fall slower), two layered
        for the sheet of water thrown up; the gulp of the air it dragged down collapsing under them (one big bubble's
        ring); then the column crashing back, the packs' broad moving-water recording swelling and dying away with a
        rain of drops in it. Out on open water.""",
        sources=SPLASHES + [SURF], takes=3, lufs=-16)
def water_column(rng, k):
    L = 1.8
    a = ck.align(W.rec(SPLASHES[k % 3], semis=-14 + rng.uniform(-1, 1)), floor_db=-12)
    c = ck.align(W.rec(SPLASHES[(k + 1) % 3], semis=-10 + rng.uniform(-1, 1)), floor_db=-20)
    crash = W.rec(SURF, semis=-2, start=rng.uniform(0.3, 4.0), length=L)
    crash = shaped(crash, [(0, 0), (0.3, 0.1), (0.55, 1), (0.9, 0.5), (L, 0)], L)
    drops = fall_back(rng, L, 0.35, rng.uniform(0.6, 0.75), 350)
    b = Bus(L + 0.5)
    b.at(0, W.norm(a), 0)
    b.at(rng.uniform(0.01, 0.03), W.norm(c), -4)
    b.at(0, W.norm(cavity(rng, rng.uniform(65, 90), rng.uniform(0.08, 0.13))), -2)
    b.at(0, W.norm(lp(crash, 5000, 2)), -9)
    b.at(0, W.norm(drops), -10)
    return finish(over_by(out_there(b.x, rng, 0.15), L), lo=35)


BREAKS = S("misc_26", "misc_27", "door_01", "glass_03", "items_02")    # real glass breaking and shattering
GLASS = S("glass_01", "glass_02", "glass_04", "glass_05", "glass_06")   # real glass knocks: the shards' ring
THICK = K("impactGlass_heavy")                                          # a thick glass knock: a porcelain body's thunk
HEAD = [3900, 4700, 5300]          # the doll's hollow porcelain head (its giggle rings there: tells.py, boarders.py)


def shard(rng, key, semis, tau):
    """One porcelain shard landing: a real glass knock, pitched to the piece and damped short (porcelain is thicker and
    duller than glass: it clacks more than it rings)."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=-18, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    c = ck.cut(x, a - samples(0.001), a + samples(0.15), 0.0005, 0.02)
    return ck.norm(hp(ck.choke(dsp.vari(c, semis), 0.003, tau), 700, 2))


def rain_of(rng, length, rate, piece, decay=0.45, start=0.06, bounce=0.45):
    """Pieces coming down on the ballast: `piece(rng)` each, Poisson at `rate` dying away over `decay` s, smaller and
    quieter as it goes on, some bouncing once off the stones."""
    b = Bus(length + 0.4)
    t = start
    while True:
        t += rng.exponential(1 / (rate * np.exp(-(t - start) / decay) + 0.5))
        if t >= length:
            return b.x
        g = rng.uniform(0.3, 1.0) * np.exp(-(t - start) / (decay * 1.5))
        x = piece(rng)
        b.at(t, x * g)
        if rng.random() < bounce:
            b.at(t + rng.uniform(0.04, 0.12), dsp.vari(x, rng.uniform(0.5, 2)) * g * rng.uniform(0.2, 0.45))


def last_piece(rng, ring=HEAD):
    """The last of her: a piece of the head rocking to rest on the stones, ringing its own modes a few times, each
    sooner and softer, then still."""
    b = Bus(0.8)
    t, g, gap = 0.0, 1.0, rng.uniform(0.11, 0.16)
    tick = stroke_tick(GLASS[int(rng.integers(len(GLASS)))], 0.003, 1500)
    for _ in range(int(rng.integers(3, 5))):
        r = dsp.resonate(fit(tick, samples(0.25)), [f * rng.uniform(0.98, 1.02) for f in ring], q=180, gains=[1, 0.7, 0.5])
        b.at(t, ck.norm(r) * g)
        t += gap
        gap *= 0.62
        g *= 0.55
    return b.x


@recipe("crew-cannon-impact", "doll", "shatter",
        "The Track Doll smashed by a ball: a hollow porcelain thunk, the shell bursting, shards raining on the ballast",
        """Real breakage throughout. The ball meets her: a thick glass knock pitched down into a hollow porcelain body's
        thunk (her body rings at 600 and 1200 Hz, as the earlier 'cornered' take had it) with the ball's thud into the
        ballast under it; she bursts: two of the packs' real glass breaks and shatters together, pitched down a little
        (porcelain is thicker and duller than glass); her pieces rain down on the ballast for a second (real glass
        knocks pitched to each piece and damped short, so they clack rather than ring, among real stones), and last a
        piece of her head rocks to rest, ringing at the head's own pitches (3.9, 4.7 and 5.3 kHz, where her giggle rang),
        and is still.""",
        sources=BREAKS + GLASS + THICK + STONES + GRAVEL, takes=2, lufs=-16)
def doll_shatter(rng, k):
    L = 1.6
    thunk = ck.choke(dsp.vari(ck.norm(ck.align(ck.get(THICK[k * 2]))), -5 + rng.uniform(-1, 1)), 0.03, 0.05)
    thunk = mix(thunk, ck.norm(dsp.resonate(fit(thunk, samples(0.3)), [600, 1200, 1830], q=12)) * 0.5)
    ballast = ck.floor(rng, "ground", k, 3.0, 0.6)
    bursts = [ck.align(W.rec(BREAKS[(k * 2 + j) % 5], semis=-2 - 2 * j + rng.uniform(-1, 1)), floor_db=-12) for j in range(2)]

    def piece(rng):
        if rng.random() < 0.2:
            return clink(STONES[int(rng.integers(3))], int(rng.integers(4)), rng.uniform(-3, 2), 0.02) * 0.6
        return shard(rng, GLASS[int(rng.integers(len(GLASS)))], rng.uniform(-3, 4), rng.uniform(0.012, 0.035))

    rain = rain_of(rng, 1.2, 40, piece, decay=0.35)
    at, last = rng.uniform(0.9, 1.05), last_piece(rng)
    b = Bus(L + 0.5)
    b.at(0, W.norm(thunk), -2)
    b.at(0.004, W.norm(ballast), -6)
    b.at(0.008, W.norm(bursts[0]), -1)
    b.at(0.02 + rng.uniform(0, 0.02), W.norm(bursts[1]), -5)
    b.at(0, W.norm(rain), -7)
    b.at(at, last, -19)
    return finish(over_by(out_there(b.x, rng, 0.1), L), lo=40)


def porcelain_shard(rng, f=None, decay=None):
    """A porcelain fragment struck: a small plate's modes (bright, a few kilohertz), dying in tens of milliseconds."""
    f = f or np.exp(rng.uniform(np.log(1800), np.log(5200)))
    return ck.norm(W.body(rng, f, W.PLATE, decay=decay or rng.uniform(0.015, 0.05), contact=0.00018, count=7, tilt=0.5))


@recipe("crew-cannon-impact", "doll", "porcelain",
        "The Track Doll smashed by a ball, porcelain modelled: her hollow body struck, the shell cracking apart, shards",
        """Porcelain's physics where the packs only have glass: the ball strikes her hollow body (its low modes at 600,
        1200 and 1830 Hz, struck hard, as the earlier 'cornered' take rang her) and drives into the ballast (real gravel
        and stones); her shell fractures in a few milliseconds (a fast run of cracks through the glaze's high modes), its
        fragments fly apart (dozens of small porcelain plates struck at once, each its own pitch, dying in tens of
        milliseconds) and come down on the stones for a second (each fragment's clack with a real stone's knock under
        some), and last a piece of her head rocks to rest ringing at the head's own pitches (3.9, 4.7 and 5.3 kHz, where
        her giggle rang), and is still.""",
        sources=STONES + GRAVEL + GLASS, takes=2, lufs=-16)
def doll_porcelain(rng, k):
    L = 1.6
    body = W.modal([600 * rng.uniform(0.97, 1.03), 1200, 1830, 2650, 3400], [0.09, 0.07, 0.05, 0.035, 0.025],
                   [1.0, 0.8, 0.6, 0.45, 0.3], 0.4, rng, contact=0.0006)
    ballast = ck.floor(rng, "ground", k + 1, 3.0, 0.6)
    crack = W.tear(0.22, rng, env([(0, 2500), (0.05, 900), (0.22, 60)], 0.22), (1500, 8000), q=22, crack=0.9)
    crack = crack * env([(0, 1), (0.22, 0)], 0.22)
    burst = Bus(0.5)
    for _ in range(int(rng.integers(26, 36))):
        burst.at(abs(rng.normal(0, 0.03)), porcelain_shard(rng) * rng.uniform(0.3, 1.0))

    def piece(rng):
        x = porcelain_shard(rng, decay=rng.uniform(0.012, 0.04))
        if rng.random() < 0.4:
            x = mix(x, clink(STONES[int(rng.integers(3))], int(rng.integers(4)), rng.uniform(-4, 0), 0.015) * 0.5)
        return x

    rain = rain_of(rng, 1.2, 45, piece, decay=0.32, start=0.1)
    at, last = rng.uniform(0.95, 1.1), last_piece(rng)
    b = Bus(L + 0.5)
    b.at(0, W.norm(body), -2)
    b.at(0.003, W.norm(ballast), -7)
    b.at(0.002, W.norm(crack), -4)
    b.at(0.01, W.norm(burst.x), -3)
    b.at(0, W.norm(rain), -8)
    b.at(at, last, -19)
    return finish(over_by(out_there(b.x, rng, 0.1), L), lo=40)


# ---- place-depot: a powder keg or a powder car going up ----------------------------------------------------------------------

def whump(rng, rise=0.025, decay=0.32, f=32, length=2.0):
    """Black powder going up is a deflagration, not a detonation: the pressure comes up over tens of milliseconds
    instead of instantly, so it shoves the air rather than cracking it. A slow pressure push, its sub dropping in
    pitch, the dark body of the gas expanding."""
    n = samples(length)
    t = np.arange(n) / dsp.SR
    push = lp(((1 - np.exp(-t / rise)) * np.exp(-t / decay)).astype(np.float32), 260, 2)
    push = push - lp(push, 18, 1)          # the suction after the push (no DC)
    sub = synth.thump(f * rng.uniform(0.9, 1.1), length * 0.7, drop=0.4)
    body = lp(synth.noise(length, rng, "brown"), 320, 2) * env([(0, 0), (rise * 1.5, 1), (length, 0)], length, "exp") ** 1.4
    return mix(W.norm(push), W.norm(sub) * 0.7, W.norm(body) * 0.8)


WRECK = {"wood": 0.5, "iron": 0.2, "tin": 0.2, "scrap": 0.1}


def wreckage(rng, length, start=0.5, rate=11, decay=1.3, floor=2.0):
    """The car's pieces coming back down for seconds: real planks, iron, roof tin and scrap (the packs' impacts pitched
    to their size), thick at first and thinning to a steady few a second (the last fell from highest, so they land as
    hard as the first), a little dark with the distance."""
    names = list(WRECK)
    w = np.array([WRECK[k] for k in names])
    b = Bus(length + 1.0)
    t = start
    while True:
        t += rng.exponential(1 / (rate * np.exp(-(t - start) / decay) + floor))
        if t >= length - 0.3:
            return lp(b.x, 7000, 2)
        x = W.norm(W.piece(rng, names[rng.choice(len(names), p=w / w.sum())], (-7, 1)))
        g = rng.uniform(0.3, 1.0) * (0.55 + 0.45 * np.exp(-(t - start) / decay))
        b.at(t, x * g)
        if rng.random() < 0.4:
            b.at(t + rng.uniform(0.08, 0.3), dsp.vari(x, rng.uniform(0.5, 2)) * g * rng.uniform(0.2, 0.5))


@recipe("place-depot", "powder-blast", "fireball",
        "Black powder going up: a slow shove of air, the fireball's roar burning down, the car torn apart, wreckage raining",
        """Not a cannonball's crack: black powder deflagrates, so the pressure rises over tens of milliseconds and shoves
        the air (a slow push, its sub sliding down, the dark body of the gas). Then the fireball: a huge jet of burning
        gas roaring bright at once and darkening as it burns down over a second, crackling as it goes (jet noise from a
        wide opening, its pressure falling). In it the car comes apart (real heavy iron and plank hits pitched down for
        the mass, boards splintering, tin crumpling, ballast thrown), the land throws the blast back (the packs' real
        thunder, late), and the wreckage rains down for three seconds and more: real planks, iron, roof tin and scrap,
        thinning out.""",
        sources=[THUNDER] + W.PIECES["wood"] + W.PIECES["iron"] + W.PIECES["tin"] + W.PIECES["scrap"] + ["sfx_100_v2:misc_34"],
        takes=2, lufs=-16)
def blast_fireball(rng, k):
    L = 4.6
    push = whump(rng, rng.uniform(0.02, 0.035), rng.uniform(0.28, 0.38))
    # the fireball swells a moment behind the shove (the whump, then the roar)
    p = env([(0, 0.02), (0.06, 0.3), (0.25, 1.0), (0.6, 0.55), (1.4, 0.12), (2.2, 0)], 2.2)
    roar = W.jet(2.2, rng, pressure=p, opening=1.0, peak=rng.uniform(650, 850), low=1.0, rasp=0.6, eddy=0.8)
    crash = W.crash(rng, size=1.4, iron=0.6, wood=1.0, tin=0.4, ground=0.4, crumple=0.3)
    echo = W.rec(THUNDER, semis=-1 + rng.uniform(-0.5, 0.5), start=rng.uniform(0.75, 0.85), length=3.4)
    echo = shaped(echo, [(0, 0), (0.25, 0.2), (0.6, 1), (1.5, 0.5), (3.4, 0)], 3.4)
    b = Bus(L + 0.5)
    b.at(0, ck.tilt(push, lo_db=-6, lo_f=90), 0)
    b.at(0.01, W.norm(roar), -3)
    b.at(0.03, W.norm(crash), -6)
    b.at(0.15, W.norm(echo), -9)
    b.at(0, W.norm(wreckage(rng, L, rng.uniform(0.5, 0.7))), -8)
    return finish(over_by(out_there(b.x, rng, 0.25), L, 0.8), lo=28)


@recipe("place-depot", "powder-blast", "car",
        "A powder car going up: the slow shove, the blast rolling like thunder, planks and iron torn, fire, wreckage raining",
        """The same slow black-powder shove of air (a deflagration's push, its sub sliding down), but its roar is the
        packs' real thunder from its first crack on, pitched down for the size of a whole car of powder: the blast rolling
        out and coming back off the land. In it the car is torn apart: the packs' real wood breaking and splintering
        pitched down, a stick-slip run of boards giving way, real heavy iron plates and frame hits slamming; what's left
        burns (a fire crackling up and dying back) while the wreckage rains down for three seconds and more, real planks,
        iron, tin and scrap, thinning out.""",
        sources=[THUNDER] + SPLINTER + W.PIECES["wood"] + W.PIECES["iron"] + W.PIECES["tin"] + W.PIECES["scrap"],
        takes=2, lufs=-16)
def blast_car(rng, k):
    L = 4.6
    push = whump(rng, rng.uniform(0.03, 0.045), rng.uniform(0.35, 0.45), f=28)
    roll = W.rec(THUNDER, semis=-3 + rng.uniform(-0.5, 0.5), start=rng.uniform(0.62, 0.7), length=4.0)
    roll = shaped(roll, [(0, 0), (0.04, 1), (0.8, 0.6), (2.0, 0.3), (4.0, 0)], 4.0)
    torn = Bus(1.5)
    for j, key in enumerate(SPLINTER):
        torn.at(0.01 + 0.04 * j, W.norm(W.rec(key, semis=-4 - 2 * j + rng.uniform(-1, 1))) * (1.0 - 0.3 * j))
    torn.at(0.0, W.norm(W.splinter(0.6, rng, env([(0, 600), (0.6, 60)], 0.6))) * env([(0, 1), (0.6, 0)], 0.6), -4)
    for _ in range(4):
        torn.at(abs(rng.normal(0, 0.05)), W.norm(W.piece(rng, "iron", (-9, -4), tau=0.3)) * rng.uniform(0.5, 0.9))
    fire = synth.fire(3.5, env([(0, 0.2), (0.4, 1.0), (1.5, 0.6), (3.5, 0.2)], 3.5), rng)
    fire = shaped(fire, [(0, 0), (0.3, 1), (2.5, 0.6), (3.5, 0)], 3.5)
    b = Bus(L + 0.5)
    b.at(0, ck.tilt(push, lo_db=-6, lo_f=90), 0)
    b.at(0.01, W.norm(roll), -2)
    b.at(0.02, W.norm(torn.x), -6)
    b.at(0.5, W.norm(fire), -16)
    b.at(0, W.norm(wreckage(rng, L, rng.uniform(0.45, 0.6), 12, 1.5)), -7)
    return finish(over_by(out_there(b.x, rng, 0.25), L, 0.8), lo=28)
