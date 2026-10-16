"""U1's second whimsies heard (queue #559, note 831; U1's #967, note 744): the hammock in the guard van, the hot tub on the
engine's hood, the fishing rod, and a sausage or a grilled fish eaten. As tools/audio/cues.py lists them and GameAudio.Lounge
plays them.

- The hammock (up-lounge.hammock): lain in or got out of: the canvas taking a body's weight, the ropes creaking at their
  hooks in the van's walls, a coat settling.
- Lying in it (up-lounge.sway, a loop): the van rocking it: the ropes' slow creak at each end of the swing, the canvas
  shifting. Held while anyone's in it.
- The hot tub (up-lounge.tub-in): got into or out of: a body lowered into hot water, a slosh over the rim, a contented 'ahh'.
- The tub itself (up-lounge.tub, a loop): hot water fed off the boiler, bubbling where the steam pipe comes in, lapping at
  the staves, a little steam hiss. Held while it's fitted and the boiler has steam.
- The cast (up-fishing.cast): the rod's swish, the clicker paying out, the weight's plop out on the water.
- The line in (up-fishing.line, a loop): the water lapping at the shore, the reel's clicker now and then as the line's
  twitched.
- The catch (up-fishing.catch): a bite: the reel screaming as the fish runs, a splash as it breaks the surface, wound in
  fast, and the trout landing flapping on the ground.
- Eating (up-bbq.eat): a bite of a sausage (its skin snapping) or of a grilled fish, and chewing.

Jacob's fishing (recipes/figures.py) gives the rod, reel and water; the BBQ deck's (recipes/up_forge_bbq.py) the beer's
sloshing drum; the crane's hook (recipes/crane_hoist.py) the rope's creaks; the crew's modelled throat (recipes/rescue_finds)
the voices. Each cue has a candidate built from the packs' real recordings and one modelled.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, bp, env, mix
from recipes import crew_kit as ck
from recipes import water as WA
from recipes import world_kit as W
from recipes.beasts import unit
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe
from recipes.figures import (WATER, AIR, TICKER, TEETH, at, loop, band_shape, lapping_rec, lapping_model, payout_rec,
                             payout_model, swish_rec, swish_model, plop, ratchet_rec, ratchet_model, _clicker)
from recipes.house_folk import bed, wet_grain, crack
from recipes.rescue_finds import effort, VOICES, GASP, CREAKS, SWISH, DROP
from recipes.up_forge_bbq import DRUM, STEAM, glug_rec

LOUNGE = "up-lounge"
FISHING = "up-fishing"
BBQ = "up-bbq"
SR = dsp.SR
CYCLE = 10.0
CLOTH = ck.R("cloth1", "cloth2", "cloth4")         # real cloth handled: canvas and a coat
SOFT = [f"kenney_impact-sounds:impactSoft_medium_00{i}" for i in range(5)]   # real soft blows: a body settling
WET = ck.S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")          # real wet squelches: a fish's slap
# Hemp rope round an iron hook in a wooden wall: a light, dry body (higher than the crane's iron swivel).
ROPE = [330, 610, 980, 1500]
# A banded cedar tub a couple of metres across, full: its staves' hollow, dull and low.
TUB = [160, 290, 450, 700]


def van(x, rng, wet=0.12, tail=0.3):
    """Inside the guard van: its wooden walls close round it, the tail cut short (`tail` s) so a one-shot again doesn't
    pile up."""
    y = dsp.room(x, "car", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def hood(x, rng, wet=0.1, tail=0.35):
    """Up on the engine's hood in the open: the night round it, the cab's back a few metres off."""
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(hp(y[:len(x) + samples(tail)], 120, 2), 0.0, tail)


# ---- The hammock ---------------------------------------------------------------------------------------------------------

def rope_rec(rng, L, semis):
    """A rope creaking on its hook, from the packs' real wooden creaks pitched down a little and cut to L s."""
    c = hit_of(CREAKS[int(rng.integers(len(CREAKS)))], int(rng.integers(2)), L)
    c = dsp.fit(dsp.vari(c, semis + rng.uniform(-1.5, 1.5)), samples(L))
    return unit(hp(c, 200, 2)) * env([(0, 0), (0.03, 1), (L * 0.7, 0.7), (L, 0)], L)


def rope_model(rng, L, rise):
    """A rope creaking on its hook, modelled: stick-slip of hemp round iron, rung through the rope and the wall's hook."""
    r0, r1 = (14, 30) if rise >= 0 else (28, 12)
    s = synth.creak(L, env([(0, r0), (L, r1)], L, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in ROPE], q=10,
                    jitter=0.45, grit=0.5, shape=env([(0, 0), (0.05, 1), (L * 0.7, 0.7), (L, 0)], L))
    return unit(hp(s, 200, 2))


def canvas(rng, L, take, real=True):
    """The canvas taking a body or shifting under it: real cloth handled, slowed into a heavy weave (or noise through
    one)."""
    if real:
        c = dsp.vari(ck.get(CLOTH[take % len(CLOTH)]), rng.uniform(-6, -3))
        a = int(rng.integers(0, max(1, len(c) - samples(L))))
        c = dsp.fit(c[a:], samples(L))
    else:
        c = bp(synth.noise(L, rng, "pink"), 400, 3500) * (0.6 + 0.4 * W.norm(lp(rng.standard_normal(samples(L)).astype(np.float32), 18)))
    return unit(hp(c, 250, 2)) * env([(0, 0), (L * 0.15, 1), (L * 0.6, 0.6), (L, 0)], L)


def lying(rng, k, rope, real):
    """Into the hammock (or out of it): the canvas swinging and taking the weight, the ropes creaking at both walls, a
    coat settling; out, the creaks lift as the weight comes off. The third take, a long contented breath out."""
    parts = [(0, canvas(rng, 0.7, k, real), -6)]
    if k % 3 == 1:
        # Out: a creak as they sit up, the canvas let go, the ropes springing back.
        parts += [(0.05, rope(rng, 0.45, 1.0), -2), (0.35, rope(rng, 0.3, 1.5), -6)]
    else:
        soft = unit(hp(hit_of(SOFT[k % 5], 0, 0.2), 150, 2)) if real else ck.choke(W.body(rng, rng.uniform(170, 210), W.PLATE,
                                                                                      decay=0.08, length=0.3, contact=0.006, count=8), 0.03, 0.05)
        parts += [(0.12, unit(soft), -8), (0.15, rope(rng, 0.55, -1.0), 0), (0.25, rope(rng, 0.5, -0.5), -4),
                  (0.8, rope(rng, 0.35, 0.0), -9)]
    if k % 3 == 2:
        sigh = effort(rng, VOICES[k % 3], 0.9, [(0, 1.0), (0.9, 0.85)], [(0, "a"), (0.9, "h")], [(0, 0), (0.08, 1), (0.9, 0)],
                      press=0.7, rough=0.1, voiced=0.35, real=GASP if real else None)
        parts.append((0.9, sigh, -12))
    return ck.place(parts)


@recipe(LOUNGE, "hammock", "ropes",
        "Lying down in the guard van's hammock or getting out: the canvas taking the weight, the ropes creaking at their hooks",
        """From real recordings: the canvas swinging and taking a body's weight (the packs' real cloth, slowed into a heavy
        weave), the two ropes creaking where they're tied to iron hooks in the van's walls (the packs' real wooden creaks,
        pitched a little down to hemp round iron), the body settling (a real soft blow); getting out, the creaks lift as
        the weight comes off; one take ends in a long, contented breath out (the crew's throat drawn through a real gasp).
        Inside the guard van's wooden walls.""", sources=CREAKS + SOFT + CLOTH + [GASP], takes=3, lufs=-23)
def hammock_ropes(rng, k):
    y = lying(rng, k, lambda r, L, rise: rope_rec(r, L, -3 + 2 * rise), True)
    return van(hp(y, 150, 2), rng)


@recipe(LOUNGE, "hammock", "hemp",
        "The hammock lain in or got out of, modelled: hemp stick-slipping round iron hooks, the canvas shifting",
        """Modelled: stick-slip of hemp round an iron hook, rung through the rope and the hook's wall (slowing as the weight
        comes on, quickening as it comes off), noise through a heavy weave for the canvas, a soft broad blow for the body,
        the crew's throat sighing on the third take. Inside the guard van.""", takes=3, lufs=-23)
def hammock_hemp(rng, k):
    y = lying(rng, k, rope_model, False)
    return van(hp(y, 150, 2), rng)


def swaying(rng, rope, real):
    """Lying in the hammock as the van rocks it: at each end of a slow swing (every 2.4-3.6 s, never even) a rope creaks
    at one hook and then the other, the canvas shifting now and then. A seamless 10 s cycle in the van."""
    n = samples(CYCLE)
    y = np.zeros(n + samples(2.0), np.float32)
    t = rng.uniform(0.1, 0.6)
    while t < CYCLE:
        for dt, x, db in ((0.0, rope(rng, rng.uniform(0.4, 0.7)), 0), (rng.uniform(0.08, 0.2), rope(rng, rng.uniform(0.3, 0.5)), -6)):
            i = samples(t + dt)
            m = min(len(x), len(y) - i)
            y[i:i + m] += x[:m] * 10 ** (db / 20) * rng.uniform(0.6, 1.0)
        if rng.random() < 0.4:
            c = canvas(rng, 0.5, int(rng.integers(3)), real)
            i = samples(t + rng.uniform(0.3, 1.0))
            m = min(len(c), len(y) - i)
            y[i:i + m] += c[:m] * 0.3
        t += rng.uniform(2.4, 3.6)
    return loop(y, n, rng, 0.12, "car", band_shape(180, 9000, 4))


@recipe(LOUNGE, "sway", "creak",
        "Lying in the hammock as the van rocks it: the ropes' slow creak at each end of the swing",
        """From real recordings: at each end of a slow, uneven swing (every 2.4-3.6 s), one rope creaks at its hook and
        then the other (the packs' real wooden creaks pitched a little down to hemp round iron), the canvas shifting now
        and then (real cloth, slowed). Quiet and lulling; inside the guard van. A 10 s cycle.""",
        sources=CREAKS + CLOTH, loop=True, takes=1, lufs=-28, seconds=CYCLE)
def sway_creak(rng, k):
    return swaying(rng, lambda r, L: rope_rec(r, L, -3), True)


@recipe(LOUNGE, "sway", "stickslip",
        "Lying in the hammock, modelled: hemp stick-slipping round its hooks at each end of a slow swing",
        """Modelled: hemp stick-slipping round an iron hook at each end of the same slow, uneven swing, a softer one at the
        other hook just after, noise through a heavy weave for the canvas now and then. Inside the guard van. A 10 s
        cycle.""", loop=True, takes=1, lufs=-28, seconds=CYCLE)
def sway_stickslip(rng, k):
    return swaying(rng, lambda r, L: rope_model(r, L, float(r.uniform(-1, 1))), False)


# ---- The hot tub ---------------------------------------------------------------------------------------------------------

def water_rec(rng, L, semis=0.0, key=None):
    """A slice of the packs' real moving water, pitched and cut to L s."""
    w = ck.get(key or WATER[0])
    w = dsp.vari(w, semis) if semis else w
    a = int(rng.integers(0, max(1, len(w) - samples(L))))
    return unit(dsp.fit(w[a:], samples(L)))


def slosh_model(rng, L, size=1.0):
    """Water moved in a tub, modelled: a band of noise swelling and falling, low bubbles rising in it, rung through the
    staves."""
    body = bp(synth.noise(L, rng, "pink"), 200, 2500) * env([(0, 0), (L * 0.2, 1), (L * 0.5, 0.5), (L, 0)], L)
    bub = synth.bubbles(L, 40 * size, 180, 900, rng, rise=(0.05, 0.4))
    return unit(mix(unit(body), unit(bub) * 0.6, unit(dsp.resonate(body, TUB, q=6)) * 0.3))


def bathing(rng, k, real):
    """Into the hot tub (or out): a body lowered into hot water, the water heaving and slopping over the rim onto the
    hood, the staves knocked by a heel; in, a long 'ahh' as the heat takes them; out, the water streaming off them and a
    shiver of a breath in the cold."""
    if real:
        heave = unit(lp(hp(water_rec(rng, 1.1, rng.uniform(-6, -3)), 150, 2), 3500))
        over = unit(hp(water_rec(rng, 0.6, rng.uniform(-2, 1), WATER[1]), 300, 2))
        gulp = unit(lp(glug_rec(rng, 0.5, rng.uniform(-4, -1)), 1500))
    else:
        heave, over, gulp = slosh_model(rng, 1.1, 1.0), unit(hp(slosh_model(rng, 0.6, 0.5), 600, 2)), slosh_model(rng, 0.5, 0.6)
    heel = ck.hollow(rng, TUB, 0.2, q=6)
    parts = [(0, heave * env([(0, 0), (0.1, 1), (0.6, 0.6), (1.1, 0)], 1.1), 0), (0.2, gulp, -6), (0.35, over, -8), (0.1, heel, -14)]
    v = VOICES[k % 3]
    if k % 3 == 1:
        # Out: streaming off them, and a sharp breath in at the cold.
        parts.append((0.5, effort(rng, v, 0.35, [(0, 1.1), (0.35, 1.2)], [(0, "i"), (0.35, "h")], [(0, 0), (0.05, 1), (0.35, 0)],
                                  press=0.6, rough=0.2, voiced=0.2, real=GASP if real else None), -10))
    else:
        parts.append((0.55, effort(rng, v, 1.1, [(0, 1.05), (0.3, 1.0), (1.1, 0.82)], [(0, "a"), (0.8, "a"), (1.1, "h")],
                                   [(0, 0), (0.1, 1), (0.7, 0.7), (1.1, 0)], press=0.65, rough=0.1, voiced=0.6,
                                   real=GASP if real else None), -6))
    return ck.place(parts)


@recipe(LOUNGE, "tub-in", "splash",
        "Getting into or out of the hot tub on the engine's hood: hot water heaving and slopping over the rim, a contented 'ahh'",
        """From real recordings: a body lowered into the tub (the packs' real moving water, dropped a few semitones and
        darkened as it heaves round them), the beer's sloshing drum pitched down for the water gulping under them, a slop
        over the rim onto the hood (the real trickle), a heel knocking the cedar staves; in, a long contented 'ahh' as the
        heat takes them (the crew's throat drawn through a real gasp); out, the water streaming off them and a sharp breath
        in at the cold. Up on the hood in the open.""", sources=WATER + [DRUM, GASP], takes=3, lufs=-21)
def tub_in_splash(rng, k):
    return hood(bathing(rng, k, True), rng)


@recipe(LOUNGE, "tub-in", "slosh",
        "Getting into or out of the hot tub, modelled: water heaving round a body and slopping over the staves, an 'ahh'",
        """Modelled: water heaving in a tub (noise swelling and falling with low bubbles rising in it, rung through the
        cedar staves), a smaller slop over the rim, a heel on the staves, the crew's throat: a long 'ahh' getting in, a
        breath in at the cold getting out. On the hood in the open.""", takes=3, lufs=-21)
def tub_in_slosh(rng, k):
    return hood(bathing(rng, k, False), rng)


def tub_loop(rng, real):
    """The hot tub: hot water fed off the boiler bubbling up where the steam pipe comes in (a steady seethe of small
    bubbles with bigger ones breaking through), lapping at the staves as the engine rocks, and a little steam hissing off
    the surface. A 10 s cycle."""
    n = samples(CYCLE)
    seethe = W.place(n, [(t, synth.bubble(float(np.exp(rng.uniform(np.log(500), np.log(2200)))), rise=rng.uniform(0.05, 0.3)),
                          rng.uniform(0.2, 1.0) ** 1.5) for t in W.poisson(CYCLE, 70, rng)])
    big = W.place(n, [(t, synth.bubble(float(rng.uniform(180, 400)), rise=rng.uniform(0.1, 0.4)), rng.uniform(0.5, 1.0))
                      for t in W.poisson(CYCLE, 1.5, rng)])
    if real:
        lap = W.norm(W.cfilter(W.norm(dsp.fit(dsp.vari(bed(rng, WATER[0], CYCLE * 1.4), -4), n)), band_shape(150, 1800)))
        lap = lap * (0.4 + 0.6 * W.norm(lp(rng.standard_normal(n).astype(np.float32), 0.5)))
        hiss = W.norm(W.cfilter(W.norm(dsp.fit(bed(rng, STEAM, CYCLE), n)), band_shape(1500, 7000)))
    else:
        lap = W.norm(W.cfilter(slosh_model(rng, CYCLE, 0.3)[:n], band_shape(150, 1800)))
        hiss = W.norm(W.cfilter(synth.noise(CYCLE, rng)[:n], band_shape(1500, 7000)))
    y = mix(at(seethe, -30), at(big, -33), at(lap, -31), at(hiss, -40))
    return loop(y, n, rng, 0.08, "night", band_shape(130, 9000))


@recipe(LOUNGE, "tub", "seethe",
        "The hot tub on the engine's hood: hot water bubbling where the steam pipe comes in, lapping at the staves",
        """From real recordings and modelled bubbles: the tub's water fed off the boiler seething where the steam pipe comes
        in (a steady stream of small bubbles' rings, a bigger one breaking through now and then), lapping at the cedar
        staves as the engine rocks (the packs' real moving water, dropped and held low) and a little steam hissing off the
        surface (the packs' real locomotive steam hiss, held high and quiet). Warm, homely, never a boil. A 10 s cycle.""",
        sources=WATER[:1] + [STEAM], loop=True, takes=1, lufs=-28, seconds=CYCLE)
def tub_seethe(rng, k):
    return tub_loop(rng, True)


@recipe(LOUNGE, "tub", "bubbles",
        "The hot tub, modelled: a seethe of small bubbles, the water lapping, a whisper of steam",
        """Modelled: the same seethe of small bubbles' rings and the odd big one, the water's lap (noise swelling and
        falling with low bubbles, through the staves) and a whisper of steam (noise held high). A 10 s cycle.""",
        loop=True, takes=1, lufs=-28, seconds=CYCLE)
def tub_bubbles(rng, k):
    return tub_loop(rng, False)


# ---- Fishing -------------------------------------------------------------------------------------------------------------

def casting(rng, k, swish, payout, real):
    """The cast: the rod's swish, the clicker paying out behind it, slowing, and the weight's plop out on the water."""
    pay = rng.uniform(1.0, 1.4)
    parts = [(0, unit(swish(rng, k)), 0), (0.12, unit(payout(rng, pay)), -6), (0.12 + pay + rng.uniform(0, 0.12), unit(plop(rng, real)), -14)]
    return ck.place(parts)


def shore(x, rng, wet=0.08, tail=0.3):
    """By the water at night: open air, the tail cut short."""
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(hp(y[:len(x) + samples(tail)], 150, 2), 0.0, tail)


@recipe(FISHING, "cast", "reel",
        "The fishing rod cast: the rod's swish, the reel's clicker paying out, the weight's plop out on the water",
        """From real recordings, as Jacob's (recipes/figures.py): the rod's swish (the packs' real burst of air dropped most
        of an octave and swept with the tip's speed), the reel's clicker ticking as the line pays out (a real fast
        mechanism's ticks, quick and slowing to a stop) with the line's thin hiss through the guides, and the weight's small
        plop far out (a bubble's ring and a little real water). By the water at night.""",
        sources=AIR + [TICKER] + WATER, takes=3, lufs=-24)
def cast_reel(rng, k):
    return shore(casting(rng, k, swish_rec, payout_rec, True), rng)


@recipe(FISHING, "cast", "pawl",
        "The fishing rod cast, modelled: a swish, a pawl ticking out over a gear, a plop",
        """Modelled: the rod's swish (a band of noise rising and falling with the tip), the clicker's pawl ticking over a
        gear in a brass housing as the line pays out, slowing, and the weight's plop (a bubble's ring). By the water.""",
        takes=3, lufs=-24)
def cast_pawl(rng, k):
    return shore(casting(rng, k, swish_model, payout_model, False), rng)


def line_in(rng, lapping, bank):
    """The line in the water: the shore's wavelets lapping, and now and then the line twitched, two or three of the reel's
    clicks as it's taken in a little. A 10 s cycle, close."""
    n = samples(CYCLE)
    ev, t = [], rng.uniform(1.5, 3.0)
    while t < CYCLE - 0.5:
        for j in range(int(rng.integers(2, 4))):
            ev.append((t + j * rng.uniform(0.08, 0.12), bank[int(rng.integers(len(bank)))], dsp.db2a(rng.uniform(-6, 0))))
        t += rng.uniform(2.8, 4.5)
    y = mix(at(lapping(rng, n), -29), at(W.place(n, ev), -33))
    return loop(y, n, rng, 0.08, "night", band_shape(120, 9000))


@recipe(FISHING, "line", "lap",
        "A line in the water: the shore's wavelets lapping, the reel's clicker now and then as it's twitched",
        """From real recordings, as Jacob's: still water lapping at a stony shore (the packs' moving water slowed and
        darkened into a wavelet every 2-4 s, its trickle sucking back between the stones), and every three or four seconds
        two or three of the reel's clicks as the line's twitched (single teeth cut from the packs' real ratchets). Calm.
        A 10 s cycle.""", sources=WATER + TEETH, loop=True, takes=1, lufs=-27, seconds=CYCLE)
def line_lap(rng, k):
    return line_in(rng, lapping_rec, ratchet_rec(rng))


@recipe(FISHING, "line", "ripple",
        "A line in the water, modelled: wavelets on stones, a pawl's clicks now and then",
        """Modelled: water.py's wavelets at a stony shore every 2-4 s over a faint wash, and two or three of a pawl's
        clicks in a brass housing now and then as the line's twitched. A 10 s cycle.""", loop=True, takes=1, lufs=-27, seconds=CYCLE)
def line_ripple(rng, k):
    return line_in(rng, lapping_model, ratchet_model(rng))


def scream(rng, L, real):
    """The reel screaming as the fish runs: the clicker at full tilt, fast and even, surging as the fish pulls."""
    n = samples(L)
    y = np.zeros(n + samples(0.05), np.float32)
    g = _clicker() if real else None
    t = 0.0
    while t < L:
        p = t / L
        r = 55 + 25 * np.sin(np.pi * p * rng.uniform(1.5, 2.5)) ** 2
        tick = dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(2, 4)) if real else \
            ck.tick(rng, rng.uniform(3200, 3800), q=14, length=0.012, body=[2900, 4700, 6900])
        i = samples(t)
        m = min(len(tick), len(y) - i)
        y[i:i + m] += tick[:m] * rng.uniform(0.6, 1.0)
        t += 1 / r
    return unit(hp(y[:n], 900)) * env([(0, 0), (0.02, 1), (L * 0.8, 0.8), (L, 0)], L)


def flap(rng, real):
    """The trout landing on the ground and flapping: a wet slap, then smaller ones as it flips."""
    parts, t = [], 0.0
    for j in range(int(rng.integers(3, 6))):
        if real:
            s = unit(hp(hit_of(WET[j % 3], 0, 0.12), 300, 2))
        else:
            s = unit(hp(bp(synth.noise(0.06, rng), 400, 4000) * env([(0, 1), (0.06, 0)], 0.06, "exp"), 300, 2))
        parts.append((t, s, -3 * j))
        t += rng.uniform(0.14, 0.3)
    return ck.place(parts)


def catching(rng, k, real):
    """A bite: the line snatched (a jerk of the rod, a twang), the reel screaming as the fish runs, a splash as it breaks
    the surface, wound in fast, and the trout landing flapping on the ground at their feet; on one take a pleased 'ha!'."""
    if real:
        splash = unit(hp(water_rec(rng, 0.45, rng.uniform(1, 4), WATER[1]), 300, 2)) * env([(0, 0), (0.01, 1), (0.45, 0)], 0.45, "exp")
        bank = ratchet_rec(rng)
        jerk = unit(swish_rec(rng, k))
    else:
        splash = unit(hp(slosh_model(rng, 0.45, 1.5), 500, 2)) * env([(0, 0), (0.01, 1), (0.45, 0)], 0.45, "exp")
        bank = ratchet_model(rng)
        jerk = unit(swish_model(rng, k))
    run = rng.uniform(0.7, 1.0)
    # Wound in hard straight after the run, the fish on the bank as the last click goes.
    w0 = run + rng.uniform(0.35, 0.5)
    count = int(rng.uniform(16, 22))
    wound = W.place(samples(1.5), [(j * 0.045 + rng.normal(0, 0.004) + 0.005, bank[int(rng.integers(len(bank)))],
                                    dsp.db2a(rng.uniform(-6, 0))) for j in range(count)])
    landed = w0 + count * 0.045 + 0.1
    parts = [(0, jerk, -6), (0.08, scream(rng, run, real), -4), (0.25 + run * 0.4, splash, -6), (w0, unit(wound), -8),
             (landed, flap(rng, real), -4)]
    if k % 3 == 2:
        parts.append((landed + 0.2, effort(rng, VOICES[k % 3], 0.3, [(0, 1.3), (0.3, 1.1)], [(0, "a"), (0.3, "h")],
                                        [(0, 0), (0.02, 1), (0.3, 0)], press=0.5, rough=0.1, voiced=0.6,
                                        real=GASP if real else None), -10))
    return ck.place(parts)


@recipe(FISHING, "catch", "trout",
        "A fish on: the reel screaming as it runs, a splash, wound in fast, and a trout flapping on the ground",
        """From real recordings: the line snatched (the rod's swish, the packs' real air), the reel's clicker at full tilt
        as the fish runs (a real fast mechanism's ticks, surging), a splash as it breaks the surface (the packs' real
        water, pitched up), the line wound in fast (single teeth cut from real ratchets), and the trout landing on the
        ground and flapping (real wet squelches); one take, a pleased 'ha!'. By the water at night.""",
        sources=AIR + [TICKER] + WATER + TEETH + WET + [GASP], takes=3, lufs=-21)
def catch_trout(rng, k):
    return shore(catching(rng, k, True), rng)


@recipe(FISHING, "catch", "run",
        "A fish on, modelled: a pawl screaming, a splash, wound in, a fish slapping the ground",
        """Modelled: the rod's swish, a pawl ticking at full tilt in a brass housing as the fish runs, a splash (noise and
        bubbles), the reel wound in fast, wet slaps of a fish on the ground. By the water.""", takes=3, lufs=-21)
def catch_run(rng, k):
    return shore(catching(rng, k, False), rng)


# ---- Eating --------------------------------------------------------------------------------------------------------------

def eating(rng, k, real):
    """A bite and chewing: a sausage's skin snapping (or a grilled fish's crisp skin flaking) as it's bitten, then a few
    chews (the sticky wet of a mouth), the last take with a happy 'mm'."""
    if real:
        snap = unit(hp(crack(rng, (2, 5)), 600, 2))
        grains = lambda: unit(hp(wet_grain(rng, (-2, 3)), 400, 2))
    else:
        snap = unit(hp(bp(synth.noise(0.02, rng), 1500, 6000) * env([(0, 1), (0.02, 0)], 0.02, "exp"), 600, 2))
        grains = lambda: unit(hp(bp(synth.noise(0.015, rng), 600, 3000) * env([(0, 1), (0.015, 0)], 0.015, "exp"), 400, 2))
    parts = [(0, snap, 0)]
    t = rng.uniform(0.15, 0.22)
    for j in range(int(rng.integers(3, 6))):
        for g in range(int(rng.integers(2, 4))):
            parts.append((t + g * rng.uniform(0.01, 0.03), grains(), rng.uniform(-14, -6)))
        t += rng.uniform(0.2, 0.28)
    if k % 4 == 3:
        parts.append((t + 0.05, effort(rng, VOICES[k % 3], 0.45, [(0, 1.0), (0.2, 1.1), (0.45, 0.9)], [(0, "m"), (0.45, "m")],
                                       [(0, 0), (0.05, 1), (0.45, 0)], press=0.6, rough=0.05, voiced=0.85), -8))
    return ck.place(parts)


@recipe(BBQ, "eat", "bite",
        "A sausage or a grilled fish eaten: a bite through the skin and a few chews",
        """From real recordings: a sausage's skin snapping as it's bitten (a grain of the packs' real wood cracking, pitched
        up and thinned), then a few chews (single wet contacts cut from the packs' real squelches, the sticky part of a
        mouth); the fourth take ends in a happy 'mm' (the crew's throat, humming). Close, at the mouth.""",
        sources=WET, takes=4, lufs=-25)
def eat_bite(rng, k):
    return hood(eating(rng, k, True), rng, 0.05, 0.2)


@recipe(BBQ, "eat", "chew",
        "A sausage or a grilled fish eaten, modelled: a crisp snap and a few chews",
        """Modelled: a short bright snap for the skin and a few soft, wet clicks for the chewing (noise grains in the
        mouth's band); the fourth take ends in a happy 'mm'.""", takes=4, lufs=-25)
def eat_chew(rng, k):
    return hood(eating(rng, k, False), rng, 0.05, 0.2)
