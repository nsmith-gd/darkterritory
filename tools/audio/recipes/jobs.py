"""The night's new jobs heard (queue #122, note 385): a coupling working loose and the hands that tighten it, the wrench's
other mends, powder carried to the guns, a Holdout's lock picked, and the steam lift at the mine head.

- The loose coupling (state-coupling-loose, for D1's synth: a 340 Hz band-passed clank with a ring at 1.75 kHz): the pin
  knocking in its knuckle, one knock a take, which the game fires faster and harder as it works out. Real iron kept to
  that character: a dull clank with a short ring over it, nothing under 120 Hz (the train's bed owns the bottom).
- The hands on it (crew-upkeep): a spanner worked on the pin's big nut in the gap, short pulls with the nut grinding
  round a flat at a time, and the pin seated home with a solid clunk that has to be heard.
- The wrench's mends (crew-repair): a dent beaten out of iron plate, heavy and dull with the plate's boom choked short;
  the headlamp put right (small bolts, glass shards brushed out, a new glass seated, the tin housing ticking).
- Powder (crew-powder): a canvas charge lifted out of the guard van's iron-bound locker, the gun's ready rack filled one
  charge after another, and its bar dropped across and latched.
- A Holdout's lock worked open quietly with the wrench (place-breach): the quiet alternative to the smash.
- The steam lift (place-mine-lift): a small winding engine on the locomotive's steam, its drum and rope and pawl, and a
  skip of ore tipped down a steel chute into a wooden car.

Gameplay foley, so the real thing first: the packs' iron, plate, latches, glass, tin, cloth and stone, cut to their hits,
pitched to the part's size and choked where a part is bolted or loaded (cast iron clanks, it doesn't sing). Where the
packs have nothing (a nut grinding on its thread, a plate's own boom, a pour of ore, a steam engine's exhaust, a wire
rope singing) the kit's models make it. Weight comes from the mids and a short low knock, never from a held sub.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, K, S, R
from recipes.crew_items import hit_of, tool_ring
from recipes.crew_train import iron, latch
from recipes.world_bed import exhaust, outdoors, circ_outdoors
from recipes.world_places import CHUTE
from recipes.stress import strongest

SR = dsp.SR
METAL_H, METAL_M, METAL_L = K("impactMetal_heavy"), K("impactMetal_medium"), K("impactMetal_light")
PLATE_H, PLATE_M, PLATE_L = K("impactPlate_heavy"), K("impactPlate_medium"), K("impactPlate_light")
GLASS_H = K("impactGlass_heavy")
TIN = K("impactTin_medium")
WOOD_H, PLANK = K("impactWood_heavy"), K("impactPlank_medium")
MINING = K("impactMining")
SLAM = S("metal_hit_01")[0]                         # a real metal door slammed: steel on steel, broad
KNOCKS = S("metal_hit_01", "misc_36")               # ... and a heavy knock-slam, for the coupling's clank
SHARDS = S("glass_01", "glass_03", "glass_04", "glass_05", "glass_06")
SCRAPE = S("misc_10")[0]                            # a real metal scrape in three strokes
LOCK = S("lock_open_01")[0]                         # a real lock's mechanism turned: five clicks
RATCHET = S("misc_20")[0]                           # a real ratchet and pawl
BUCKLE = R("clothBelt", "clothBelt2")
STONES = S("stones_01", "stones_02", "stones_03")
CASTING = [1.0, 1.48, 2.11, 2.63, 3.42, 4.27, 5.36]  # a thick casting's modes (the knuckle): close, inharmonic
LOOP = 12.0


def norm(x):
    return W.norm(x)


def strike(key, semis=0.0, lo=None, at=0.03, tau=0.04, i=0, length=0.4):
    """One real hit cut from a recording: pitched for the part's size, high-passed above its weight, and choked after
    `at` s (a bolted or loaded part stops ringing; a free one would sing)."""
    x = hit_of(key, i, length)
    if semis:
        x = dsp.vari(x, semis)
    if lo:
        x = hp(x, lo, 2)
    return norm(ck.choke(x, at, tau))


def between(y):
    """In the gap between two cars: the car ends a metre either side throw it straight back (two slaps, no tail)."""
    out = y.copy()
    for dt, g in ((0.0029, 0.32), (0.0077, 0.2)):
        k = samples(dt)
        out[k:] += y[:-k] * g
    return out


def oneshot(y, length, tail=0.04):
    """Cut to `length` with a short fade, so a take is exactly as long as it should be."""
    y = dsp.fit(y, samples(length))
    return dsp.fade(y, 0.001, tail)


def cycle(events, rng, wet=0.12, n=None):
    """A held job's events on one exact loop cycle, out in the open beside the train."""
    n = n or samples(LOOP)
    return W.seamless(circ_outdoors(W.place(n, events), rng, wet))


# ---- The loose coupling ------------------------------------------------------------------------------------------------

KNOCK_LEN = 0.3
RING = 1750                                         # the synth's ring, and the pin's: where each take's real ring is tuned


def ring(rng, key, at=0.05, tau=0.05, target=RING):
    """A real metal hit's ring, varispeeded so its loudest partial between 1 and 4 kHz sits at `target` (the pin's
    1.75 kHz, a few percent either way take to take), high-passed to just the ring and choked short."""
    x = hit_of(key, 0, 0.4)
    x = dsp.vari(x, 12 * np.log2(target * rng.uniform(0.96, 1.04) / strongest(x[:samples(0.2)], 1000, 4000)))
    return norm(ck.choke(hp(x, target * 0.6, 4), at, tau))


@recipe("state-coupling-loose", "knock", "clank",
        "The pin knocking in its knuckle: a dull iron clank with a short ring over it",
        """Kenney's real medium iron plate struck for the clank's body, high-passed above 120 Hz and lifted round 340 Hz (the
        synth's centre), choked at 25 ms like a loaded casting; over it the ring of a real light metal hit pitched down so
        its ringing partial sits at 1.75 kHz, dying in about a tenth of a second. The car ends either side throw it back.
        One knock a take; the game fires them faster and harder as the pin works out.""",
        sources=PLATE_M + METAL_L, takes=4, lufs=-24)
def knock_clank(rng, k):
    body = strike(PLATE_M[(k * 2) % 5], rng.uniform(-1.5, 0.5), lo=140, at=0.025, tau=0.04)
    body = dsp.peak(body, 340 * rng.uniform(0.95, 1.05), 1.3, 6)
    y = mix(norm(body), ring(rng, METAL_L[(0, 1, 3, 4)[k]], 0.03, 0.05) * rng.uniform(0.3, 0.4))
    return oneshot(hp(between(y), 120, 4), KNOCK_LEN)


@recipe("state-coupling-loose", "knock", "knuckle",
        "The pin knocking in its knuckle: a heavier slam of steel on steel, its ring short and bright",
        """A real metal slam (sfx_100's metal door hit, or its heavy knock-slam) cut to its first blow, pitched down two to
        four semitones for the knuckle's mass and choked at 40 ms; over it a real medium metal hit pitched up (about nine
        semitones) so its partial rings at 1.75 kHz, briefly. High-passed at 120 Hz. Heavier and more broadband than the
        clank. One knock a take.""",
        sources=KNOCKS + METAL_M, takes=4, lufs=-25)
def knock_knuckle(rng, k):
    body = strike(KNOCKS[k % 2], -rng.uniform(2, 4), lo=130, at=0.04, tau=0.04)
    body = lp(dsp.peak(body, 360, 1.0, 3), 6000)
    y = mix(body, ring(rng, METAL_M[(1, 3, 2, 4)[k]], 0.04, 0.05) * rng.uniform(0.35, 0.45))
    return oneshot(hp(between(y), 120, 4), KNOCK_LEN)


@recipe("state-coupling-loose", "knock", "casting",
        "The pin knocking in its knuckle: a real strike rung through the knuckle's casting, the pin ringing",
        """A real heavy metal strike (Kenney's, its first 30 ms) rung through the cast-steel knuckle's own modes from 330 Hz
        (a modelled casting, broad and close-packed so it clanks with no pitch), and the pin ringing over it (a real light
        metal ring pitched to 1.75 kHz). Rounder and duller than the clank; nearest the synth it replaces, made of iron.
        One knock a take.""",
        sources=METAL_H + METAL_L, takes=4, lufs=-24)
def knock_casting(rng, k):
    strike_ = hp(ck.align(ck.get(METAL_H[k % 5])), 150)[:samples(0.03)]
    strike_ = dsp.fade(strike_, 0.0005, 0.015)
    f1 = 330 * rng.uniform(0.9, 1.1)
    fs = [f1 * r * rng.uniform(0.96, 1.04) for r in CASTING]
    gains = [rng.uniform(0.35, 1.0) / (1 + 0.3 * i) for i in range(len(fs))]     # struck in a different place each time
    cast = dsp.resonate(dsp.fit(strike_, samples(KNOCK_LEN)), fs, q=rng.uniform(10, 14), gains=gains)
    cast = ck.choke(hp(cast, 200, 2), rng.uniform(0.06, 0.075), 0.06)
    y = mix(norm(cast), norm(strike_) * 0.3, ring(rng, METAL_L[(2, 0, 4, 1)[k]], 0.05, 0.055) * rng.uniform(0.18, 0.24))
    return oneshot(hp(between(y), 120, 4), KNOCK_LEN)


# ---- Tightening the coupling --------------------------------------------------------------------------------------------

NUT = [f * 1.4 for f in synth.IRON[1:6]]           # a big nut on its pin with a spanner on it: 340 Hz-3.3 kHz


def bite(rng, i):
    """The spanner's jaws closing on the nut's flats: a small hard clack of forged steel on iron."""
    return strike(METAL_M[i % 5], -rng.uniform(2, 4), lo=300, at=0.006, tau=0.012)


def grind(rng, length, top=None):
    """The nut ground round a flat: rusty thread and face slipping (stick-slip through the nut and spanner, quickening
    as the pull comes on), with the grit of it."""
    top = top or rng.uniform(70, 110)
    rate = env([(0, 30), (length * 0.6, top), (length, 35)], length)
    c = synth.creak(length, rate, rng, body=[f * rng.uniform(0.9, 1.1) for f in NUT], q=14, jitter=0.4, grit=0.5)
    g = ck.friction(rng, length, 220, 700, 5000, 0.8)
    e = env([(0, 0), (0.04, 1), (length * 0.7, 0.8), (length, 0)], length)
    return norm(hp(norm(c) + 0.5 * g, 250) * e)


@recipe("crew-upkeep", "tighten", "spanner",
        "Tightening a loose coupling: the spanner's jaws biting, short pulls, the nut grinding round a flat at a time",
        """Busy hands in the gap: the spanner seated on the nut (a real medium metal hit, pitched down, choked to a clack),
        a short pull with the nut grinding round (modelled stick-slip through the nut and spanner, quickening as the pull
        comes on, with its grit), the jaws lifted off for the next flat, and now and then the spanner knocking the coupler
        (real heavy iron, choked) and a glove's rustle. Out in the gap between the cars. 12 s exact cycle.""",
        sources=METAL_M + METAL_L + PLATE_H + METAL_H + PLATE_M + R("cloth1", "cloth2", "cloth3", "cloth4"), loop=True,
        takes=1, lufs=-22)
def tighten_spanner(rng, k):
    ev, t, i = [], 0.15, 0
    while t < LOOP - 0.4:
        force = rng.uniform(0.7, 1.0)
        ev.append((t, bite(rng, i), 0.45))
        pl = rng.uniform(0.25, 0.5)
        ev.append((t + 0.05, grind(rng, pl), 0.55 * force))
        if rng.random() < 0.55:
            ev.append((t + 0.05 + pl, strike(METAL_L[i % 5], -rng.uniform(4, 6), lo=800, at=0.004, tau=0.015), 0.18))
        if rng.random() < 0.18:
            ev.append((t + 0.05 + pl + 0.12, mix(iron(rng, i, -3, 0.03, 0.5), tool_ring(rng, "wrench", i) * 0.3), 0.6))
        if rng.random() < 0.3:
            ev.append((t + rng.uniform(0, pl), ck.cloth(rng, i, 0.25), 0.15))
        t += 0.05 + pl + rng.uniform(0.25, 0.6)
        i += 1
    return cycle(ev, rng, 0.1)


def scrape_pull(rng, length, semis=(6, 8)):
    """A pull heard through a real metal scrape: sfx_100's scrape from one of its strokes on, pitched down for a big nut,
    swelling and easing over the pull."""
    x = ck.get(SCRAPE)
    h = ck.hits(x, floor_db=-14, gap=0.03)
    a = h[int(rng.integers(len(h)))][0]
    x = dsp.vari(x[max(0, a - samples(0.01)):], -rng.uniform(*semis))
    x = dsp.fit(bp(x, 300, 6000), samples(length))
    x = norm(x) + 0.35 * norm(dsp.resonate(x, NUT, q=10))          # bent through the nut and spanner's iron
    return norm(x * env([(0, 0), (0.03, 1), (length * 0.7, 0.8), (length, 0)], length))


@recipe("crew-upkeep", "tighten", "rusted",
        "Tightening a loose coupling: every part a real recording, a metal scrape for each grinding pull",
        """Each pull is sfx_100's real metal scrape pitched down for the big rusted nut and rung a little through the nut
        and spanner's iron; the jaws biting are the packs' real latch clicks pitched down to a spanner's clack; the spanner
        knocking the coupler is the real metal slam choked short; small real clinks as the jaws come off the flats. Out in
        the gap between the cars. 12 s exact cycle.""",
        sources=[SCRAPE, SLAM] + S("door_04", "door_05", "metal_02", "metal_06"), loop=True, takes=1, lufs=-22)
def tighten_rusted(rng, k):
    jaws = S("door_04", "door_05")
    clinks = S("metal_02", "metal_06")
    ev, t, i = [], 0.15, 0
    while t < LOOP - 0.4:
        ev.append((t, strike(jaws[i % 2], -rng.uniform(5, 7), lo=300, at=0.01, tau=0.015), 0.4))
        pl = rng.uniform(0.25, 0.45)
        ev.append((t + 0.06, scrape_pull(rng, pl), rng.uniform(0.4, 0.55)))
        if rng.random() < 0.5:
            ev.append((t + 0.06 + pl, strike(clinks[i % 2], -rng.uniform(5, 7), lo=600, at=0.01, tau=0.02), 0.15))
        if rng.random() < 0.2:
            ev.append((t + 0.06 + pl + 0.1, strike(SLAM, -rng.uniform(1, 3), lo=150, at=0.03, tau=0.03), 0.5))
        t += 0.06 + pl + rng.uniform(0.25, 0.55)
        i += 1
    return cycle(ev, rng, 0.1)


@recipe("crew-upkeep", "tightened", "seat",
        "The pin seated home: the last grinding pull ending in a solid iron clunk, the knocking gone",
        """The last pull grinds the nut hard down (the modelled stick-slip, quickening and stopping dead) and the pin seats
        with a solid clunk: Kenney's heavy plate and metal hits pitched down and choked (crew_train's iron) with the real
        metal slam's first blow over it for the mids that carry, then the coupler's iron settling in two small ticks. Made
        to be heard: a confirmation.""",
        sources=PLATE_H + METAL_H + [SLAM] + METAL_L, takes=3, lufs=-21)
def tightened_seat(rng, k):
    g = grind(rng, rng.uniform(0.12, 0.16), top=rng.uniform(110, 140))
    clunk = mix(iron(rng, k, -5, 0.045, 0.6), strike(SLAM, -rng.uniform(2, 3), lo=90, at=0.05, tau=0.05) * 0.6)
    tick = [strike(METAL_L[(k + j) % 5], -rng.uniform(4, 6), lo=900, at=0.004, tau=0.015) for j in range(2)]
    t0 = len(g) / SR - 0.01
    y = ck.place([(0, g, -8), (t0, clunk, 0), (t0 + rng.uniform(0.09, 0.12), tick[0], -16),
                  (t0 + rng.uniform(0.17, 0.22), tick[1], -20)])
    return oneshot(hp(outdoors(between(y), rng, 0.06), 100, 4), 0.55)


@recipe("crew-upkeep", "tightened", "home",
        "The pin seated home: one heavy clunk with the pin's short ring, the spanner lifted off",
        """The pin driven home in one go: a heavy cast-iron clunk (crew_train's iron, pitched down and choked, with the
        real metal slam's blow for the mids that carry) and the pin's own short ring over it (a real light metal hit
        pitched so it rings at 1.2 kHz, choked by the knuckle), then the spanner lifted off the nut with a light tink
        (the wrench's own voice, crew_items). Quicker than the seat, with no grind before it.""",
        sources=PLATE_H + METAL_H + PLATE_M + [SLAM] + METAL_L, takes=3, lufs=-23)
def tightened_home(rng, k):
    clunk = mix(iron(rng, k + 1, -4, 0.05, 0.7), strike(SLAM, -rng.uniform(3, 4), lo=150, at=0.04, tau=0.04) * 0.5)
    pin = ring(rng, METAL_L[(k + 2) % 5], 0.05, 0.06, target=1200)
    off = tool_ring(rng, "wrench", k + 2, 0.6)
    y = ck.place([(0, norm(clunk), 0), (0.002, pin, -10), (rng.uniform(0.24, 0.3), off, -15)])
    return oneshot(hp(outdoors(between(y), rng, 0.06), 100, 4), 0.5)


# ---- Beating out a dent -------------------------------------------------------------------------------------------------

def plate_blow(rng, i, force):
    """A heavy hammer on a car's iron plate: real heavy plate struck, the real slam's steel-on-steel crack, the plate's own
    boom (a big sheet's modes) choked short by the hand behind it."""
    face = hp(strike(PLATE_H[i % 5], rng.uniform(-1, 1), at=0.03, tau=0.04), 120, 4)
    crack = strike(SLAM, -rng.uniform(3, 5), lo=180, at=0.02, tau=0.03)
    boom = ck.choke(W.sheet(rng, f1=rng.uniform(110, 140), fmax=4000, decay=0.4, contact=0.0012), 0.025, 0.04)
    y = mix(norm(face) * 0.8, crack * 0.6 * force, norm(boom) * 0.35)
    return norm(hp(y, 100, 2)) * force


def ease(rng):
    """The plate easing back: a sharp pop as a bulge snaps through (a sheet struck from inside, very short), or a slow
    iron creak."""
    if rng.random() < 0.55:
        p = ck.choke(W.sheet(rng, f1=rng.uniform(150, 220), fmax=3500, decay=0.2, contact=0.0003), 0.02, 0.03)
        return norm(hp(p, 120, 2)) * 0.5
    L = rng.uniform(0.3, 0.6)
    c = synth.creak(L, env([(0, 10), (L * 0.5, rng.uniform(25, 40)), (L, 12)], L), rng,
                    body=[f * 1.6 for f in synth.IRON[1:6]], q=12)
    return norm(hp(c, 150) * env([(0, 0), (0.08, 1), (L, 0)], L)) * 0.35


def beat(rng, blow, eases=0.35):
    """Blows 1-2 a second, uneven: a pause to look now and then, a lighter tap to feel the plate, and the plate easing
    between some of them."""
    ev, t, i = [], 0.1, 0
    while t < LOOP - 0.5:
        force = rng.uniform(0.75, 1.0) if rng.random() > 0.15 else rng.uniform(0.35, 0.5)
        ev.append((t, blow(rng, i, force), 1.0))
        if rng.random() < eases:
            ev.append((t + rng.uniform(0.25, 0.4), ease(rng), 1.0))
        t += rng.uniform(0.5, 0.95) if rng.random() > 0.12 else rng.uniform(1.3, 1.7)
        i += 1
    return ev


@recipe("crew-repair", "dent", "plate",
        "A dent beaten out of a car's iron wall: heavy hammer blows, dull, the plate's boom choked short, the plate easing",
        """Each blow is Kenney's real heavy plate struck, with the real metal slam's steel-on-steel crack and a modelled big
        sheet's boom choked inside 25 ms by the hand and the dent (dull and heavy, not a ringing gong), the weight in the
        low mids rather than a sub. One to two blows a second, uneven, now and then a lighter tap or a pause; between
        some the plate pops back or creaks as it eases. Outdoors beside the car. 12 s exact cycle.""",
        sources=PLATE_H + [SLAM], loop=True, takes=1, lufs=-22)
def dent_plate(rng, k):
    return cycle(beat(rng, plate_blow), rng, 0.12)


def cladding_blow(rng, i, force):
    """A hammer on the boiler's thin cladding over its lagging: the sheet's tunk (real light plate and tin, pitched down,
    dead on the lagging), the shell's thud behind it, and the cladding's band clips rattling."""
    sheet = strike(PLATE_L[i % 5], -rng.uniform(3, 5), lo=110, at=0.015, tau=0.03)
    tin = strike(TIN[(i + 2) % 5], -rng.uniform(4, 6), lo=200, at=0.01, tau=0.025)
    shell = W.body(rng, rng.uniform(210, 260), W.PLATE, decay=0.035, length=0.15, contact=0.002, count=8)
    clips = ck.grains(rng, int(rng.integers(3, 6)), 0.07, 2200, 5000, q=(15, 30), length=(0.01, 0.025), shape=0.5)
    y = ck.place([(0, sheet, 0), (0, tin, -6), (0, norm(shell), -7), (0.012, norm(clips), -20)])
    return norm(hp(y, 110, 2)) * force


@recipe("crew-repair", "dent", "cladding",
        "A dent beaten out of the boiler's cladding: the hammer's dead tunk on thin sheet over lagging, its clips rattling",
        """The engine's boiler flank: thin cladding sheet over the lagging, so a blow is a short dead tunk (Kenney's real
        light plate and tin, pitched down, choked in 15 ms) with the iron shell's thud behind it (modelled, short) and the
        cladding's band clips rattling after. One to two blows a second, uneven, with the sheet popping back or creaking
        between some. Outdoors beside the engine. 12 s exact cycle.""",
        sources=PLATE_L + TIN, loop=True, takes=1, lufs=-23)
def dent_cladding(rng, k):
    return cycle(beat(rng, cladding_blow, 0.3), rng, 0.12)


# ---- Mending the headlamp ------------------------------------------------------------------------------------------------

def bolt(rng, i):
    """A bracket bolt turned a flat: the small spanner's jaws ticking on it and the thread squeaking round."""
    tick = strike(METAL_L[i % 5], rng.uniform(1, 4), lo=1500, at=0.004, tau=0.01)
    L = rng.uniform(0.1, 0.2)
    sq = synth.creak(L, env([(0, 120), (L, rng.uniform(220, 300))], L), rng,
                     body=[f * rng.uniform(0.9, 1.1) for f in (1900, 3300, 5200)], q=22, jitter=0.3)
    sq = hp(sq, 1200) * env([(0, 0), (0.02, 1), (L, 0)], L)
    return ck.place([(0, tick, -4), (0.03, norm(sq), -12)])


def shards(rng, length, count):
    """Broken glass brushed out of the rim: fragments tinkling as they slide and drop, under a glove's brush."""
    parts = []
    for t in np.sort(rng.uniform(0, length, count)):
        g = strike(SHARDS[int(rng.integers(len(SHARDS)))], rng.uniform(2, 7), lo=1800, at=0.01, tau=0.025,
                   i=int(rng.integers(2)), length=0.12)
        parts.append((t, g, -rng.uniform(6, 20)))
    brush = hp(synth.rustle(length, 260, rng, f=(2500, 9000), ticks=0.4), 1500) * env([(0, 0), (0.1, 1), (length, 0)], length)
    parts.append((0, norm(brush), -16))
    return ck.place(parts)


def glass_in(rng, i):
    """The new glass set into the rim: glass meeting the tin with a dull tap, pressed home with a short squeak, the
    clip snapped over (a real latch, small)."""
    tap = strike(GLASS_H[i % 5], rng.uniform(1, 3), lo=600, at=0.02, tau=0.03)
    L = rng.uniform(0.12, 0.18)
    sq = synth.creak(L, rng.uniform(260, 360), rng, body=[2600, 4300, 6900], q=30) * env([(0, 0), (0.03, 1), (L, 0)], L)
    return ck.place([(0, tap, -2), (0.08, norm(hp(sq, 1500)), -14), (0.08 + L + 0.05, latch(rng, i, 5), -8)])


def housing(rng, i, rattle=False):
    """The lamp's tin housing knocked, a small tin can on its bracket; `rattle` a short run of it chattering."""
    t = strike(TIN[i % 5], rng.uniform(3, 6), lo=500, at=0.01, tau=0.02)
    if not rattle:
        return t
    return ck.place([(j * rng.uniform(0.035, 0.05), t, -3 * j - rng.uniform(0, 3)) for j in range(int(rng.integers(3, 6)))])


@recipe("crew-repair", "lamp", "mend",
        "Mending the headlamp: small bolts turned, shards brushed out, a new glass seated, the tin housing ticking",
        """The job in order round a 12 s cycle, small and close: the bracket's bolts turned a flat at a time (real light
        metal ticks and a small modelled thread squeak), the broken glass brushed out of the rim (sfx_100's real glass cut
        to its smallest tinkles, pitched up, under a glove's brush), the new glass set in (a real heavy glass tap, a
        squeak as it's pressed, a small latch for the clip), and the bolts nipped up again. All through it the lamp's tin
        housing (Kenney's tin, pitched up) ticks and rattles.""",
        sources=METAL_L + SHARDS + GLASS_H + TIN + S("door_02", "door_04", "door_05") + R("metalLatch"), loop=True,
        takes=1, lufs=-24)
def lamp_mend(rng, k):
    ev = []
    for j, t in enumerate(np.arange(0.2, 2.8, 0.5) + rng.uniform(-0.08, 0.08, 6)):
        ev.append((t, bolt(rng, j), rng.uniform(0.6, 0.9)))
    for t in (3.4, 4.6):
        ev.append((t + rng.uniform(0, 0.2), shards(rng, rng.uniform(0.6, 1.0), int(rng.integers(8, 14))), 0.8))
    ev.append((6.6, glass_in(rng, k), 0.9))
    for j, t in enumerate(np.arange(8.4, 11.2, 0.55) + rng.uniform(-0.08, 0.08, 6)):
        ev.append((t, bolt(rng, j + 3), rng.uniform(0.5, 0.8)))
    for j, t in enumerate(W.poisson(LOOP, 1.3, rng)):
        ev.append((t, housing(rng, j, rng.random() < 0.3), rng.uniform(0.2, 0.45)))
    return cycle(ev, rng, 0.08)


@recipe("crew-repair", "lamp", "tinker",
        "Mending the headlamp: fiddly hands all at once, the tin housing rattling on its bracket",
        """No order to it: two hands at the lamp at once, a small event every few tenths of a second from the job's parts
        (a bolt's tick and squeak, a sliver of glass, the glass tapped into its rim, a clip), the lamp's tin housing
        rattling on its loose bracket more than in the mend (Kenney's tin pitched up, in short chattering runs), and a
        glove's rub now and then. 12 s exact cycle.""",
        sources=METAL_L + SHARDS + GLASS_H + TIN + S("door_02", "door_04", "door_05") + R("metalLatch"), loop=True,
        takes=1, lufs=-24)
def lamp_tinker(rng, k):
    ev, t, j = [], 0.1, 0
    while t < LOOP - 0.3:
        r = rng.random()
        if r < 0.35:
            x = bolt(rng, j)
        elif r < 0.55:
            x = shards(rng, rng.uniform(0.2, 0.4), int(rng.integers(2, 5)))
        elif r < 0.65:
            x = strike(GLASS_H[j % 5], rng.uniform(2, 4), lo=800, at=0.015, tau=0.02)
        elif r < 0.72:
            x = latch(rng, j, 5)
        else:
            x = housing(rng, j, True)
        ev.append((t, x, rng.uniform(0.4, 0.8)))
        t += rng.uniform(0.18, 0.55)
        j += 1
    for t in W.poisson(LOOP, 0.5, rng):
        L = rng.uniform(0.2, 0.4)
        ev.append((t, norm(hp(synth.rustle(L, 400, rng, f=(1500, 7000), ticks=0.1), 1000) * env([(0, 0), (L / 2, 1), (L, 0)], L)), 0.15))
    return cycle(ev, rng, 0.08)


# ---- Powder ---------------------------------------------------------------------------------------------------------------

def powder(rng, length, flow=2500):
    """Black powder shifting in its canvas bag: a soft, short rush of fine grains, dark through the cloth."""
    n = samples(length)
    f = env([(0, flow * 0.1), (length * 0.3, flow), (length, flow * 0.05)], length)
    p = W.pour(n, rng, f, grain=(1500, 6000), lump=0, thunder=0.15, loop=False, voices=12, decay=0.002)
    return norm(lp(hp(p, 300), 3500) * env([(0, 0), (length * 0.25, 1), (length, 0)], length))


def bag(rng, k, length=0.35):
    """A canvas charge handled: the cloth (the packs' cloth handling), its powder shifting, the soft weight of it."""
    return ck.place([(0, ck.cloth(rng, k, length), -2), (0.05, powder(rng, length), -9),
                     (0.04, norm(hp(ck.pad(rng, 0.09, 400), 120, 2)), -10)])


@recipe("crew-powder", "take", "chest",
        "A charge taken from the powder locker: the iron-strapped lid lifted, the canvas bag hefted out, the lid let down",
        """In the guard van: the heavy lid lifted on its iron strap hinges (a modelled iron creak, the strap ticking against
        the hasp: real heavy metal choked), a canvas charge hefted out (the packs' cloth, the powder shifting soft and
        sandy inside it, the dull weight of it coming onto the arm), and the lid let down onto the chest (a real wooden
        knock with the chest's hollow and its iron bands).""",
        sources=METAL_H + ck.WOOD_KNOCKS + R("cloth1", "cloth2", "cloth3", "cloth4"), takes=3, lufs=-21)
def take_chest(rng, k):
    L = rng.uniform(0.3, 0.4)
    hinge = synth.creak(L, env([(0, 30), (L, rng.uniform(55, 75))], L), rng, body=[f * 1.6 for f in synth.IRON[1:6]],
                        q=16) * env([(0, 0), (0.05, 1), (L, 0.2)], L)
    strap = strike(METAL_H[k % 5], -rng.uniform(3, 5), lo=300, at=0.004, tau=0.02)
    down = mix(ck.floor(rng, "wood", k, 1.2, 0.6), strike(METAL_H[(k + 2) % 5], -5, lo=300, at=0.004, tau=0.02) * 0.2)
    t_bag = L + rng.uniform(0.0, 0.08)
    t_down = t_bag + rng.uniform(0.38, 0.5)
    y = ck.place([(0, strap, -14), (0.02, norm(hp(hinge, 200)), -10), (t_bag, bag(rng, k), 0), (t_down, down, -3)])
    return dsp.room(hp(y, 90, 2), "car", wet=0.12, rng=np.random.default_rng(11))


@recipe("crew-powder", "take", "heft",
        "A charge taken from the powder locker: a real hinge creak, the bag dragged up and out, the lid dropped to",
        """Built from the packs' real door: the lid's hinge is Kenney's door creak pitched down for a short heavy lid, the
        charge comes up and out over the chest's edge (the packs' cloth and a dragged canvas slide, the powder settling in
        it), and the lid drops to under its own weight (a real door slam pitched down, the chest's iron bands
        rattling).""", sources=R("creak1", "creak2", "doorClose_4", "cloth1", "cloth2", "cloth3", "cloth4") + METAL_H,
        takes=3, lufs=-21)
def take_heft(rng, k):
    hinge = dsp.vari(hit_of(R("creak1", "creak2")[k % 2], int(rng.integers(3)), 0.3), -rng.uniform(4, 6))
    hinge = dsp.fade(norm(hp(hinge, 300, 2)), 0.005, 0.12)
    L = rng.uniform(0.3, 0.38)
    drag = ck.friction(rng, L, 110, 200, 2500, 0.8, env([(0, 0), (0.05, 1), (L, 0)], L))
    shut = mix(dsp.vari(hit_of(R("doorClose_4")[0], 0, 0.35), -rng.uniform(2, 4)),
               strike(METAL_H[(k + 1) % 5], -4, lo=300, at=0.004, tau=0.03) * 0.25)
    t_bag = 0.22 + rng.uniform(0, 0.05)
    t_shut = t_bag + L + rng.uniform(0.12, 0.22)
    y = ck.place([(0, hinge, -10), (t_bag, drag, -10), (t_bag + 0.05, bag(rng, k + 1, 0.3), -2), (t_shut, norm(shut), -4)])
    return dsp.room(hp(y, 90, 2), "car", wet=0.12, rng=np.random.default_rng(11))


def charge(rng, i, rack):
    """One canvas charge pushed into the rack: dragged along its slot (canvas on wood, or on iron, brighter), the cloth,
    and the soft thump as it seats at the back."""
    L = rng.uniform(0.3, 0.45)
    lo, hi = (180, 2200) if rack == "wood" else (300, 3800)
    slide = ck.friction(rng, L, 100, lo, hi, 0.9, env([(0, 0), (0.06, 1), (L * 0.8, 0.8), (L, 0)], L))
    pad = norm(hp(ck.pad(rng, 0.08, 450), 150, 2))            # the bag's soft weight, in the low mids, not a sub
    if rack == "wood":
        stop = mix(pad, lp(ck.floor(rng, "wood", i, 0.8, 0.25), 2500) * 0.7)
    else:
        stop = mix(pad, lp(strike(PLATE_L[i % 5], -3, lo=150, at=0.01, tau=0.03), 3000) * 0.5)
    y = ck.place([(0, ck.cloth(rng, i, 0.3), -6), (0.05, slide, -6), (0.05 + L - 0.02, norm(stop), 0),
                  (0.05 + L, powder(rng, 0.2, 1500), -14)])
    return hp(y, 110, 2)


def fill(rng, rack):
    ev, t, i = [], 0.15, 0
    while t < LOOP - 0.5:
        ev.append((t, charge(rng, i, rack), rng.uniform(0.7, 1.0)))
        if rng.random() < 0.35:
            b = BUCKLE[i % 2]
            ev.append((t + rng.uniform(0.6, 0.8), strike(b, rng.uniform(-3, 0), lo=1500, at=0.03, tau=0.04,
                                                          i=(4 if b == BUCKLE[0] else 1), length=0.15), 0.25))
        t += rng.uniform(1.0, 1.45)
        i += 1
    return ev


@recipe("crew-powder", "fill", "wood",
        "Filling the ready rack: canvas charges pushed one after another into its wooden slots",
        """A charge every second or so: picked up (the packs' cloth), dragged along its wooden slot (a dark canvas-on-wood
        friction), thumping softly home against the back board (a pad and a real wooden knock, darkened), the powder
        settling in it; a strap's buckle clinking now and then (the packs' belt handling, cut to its buckle). On the gun
        car's roof. 12 s exact cycle.""",
        sources=ck.WOOD_KNOCKS + BUCKLE + R("cloth1", "cloth2", "cloth3", "cloth4"), loop=True, takes=1, lufs=-23)
def fill_wood(rng, k):
    return cycle(fill(rng, "wood"), rng, 0.08)


@recipe("crew-powder", "fill", "iron",
        "Filling the ready rack: canvas charges pushed one after another into its iron slots",
        """The same hands with an iron rack: each charge dragged along an iron slot (canvas on iron, a little brighter), its
        soft thump home waking the slot's iron for a moment (a real light plate hit, darkened and choked), the powder
        settling; a strap's buckle clinking now and then. On the gun car's roof. 12 s exact cycle.""",
        sources=PLATE_L + BUCKLE + R("cloth1", "cloth2", "cloth3", "cloth4"), loop=True, takes=1, lufs=-23)
def fill_iron(rng, k):
    return cycle(fill(rng, "iron"), rng, 0.08)


@recipe("crew-powder", "filled", "bar",
        "The rack full: its iron bar dropped across and the latch snapped over it",
        """The bar dropped into its brackets (crew_train's iron: Kenney's heavy plate and metal, pitched for a short bar,
        choked) with the bar's own ring cut short by the brackets (a real medium metal hit pitched to ring at 850 Hz),
        then the latch snapped over it (the packs' real latch). Clear and final.""",
        sources=PLATE_H + METAL_H + METAL_M + S("door_02", "door_04", "door_05") + R("metalLatch"), takes=3, lufs=-22)
def filled_bar(rng, k):
    clank = iron(rng, k + 3, -2, 0.05, 0.7)
    bar = ring(rng, METAL_M[(k + 1) % 5], 0.06, 0.06, target=850)
    y = ck.place([(0, clank, 0), (0.002, bar, -9), (rng.uniform(0.16, 0.22), latch(rng, k, -2), -3)])
    return oneshot(hp(outdoors(y, rng, 0.06), 110, 4), 0.5)


@recipe("crew-powder", "filled", "pin",
        "The rack full: the bar swung down onto its hook and a drop pin pushed through to hold it",
        """The bar swung down onto its hook (a heavy iron clank: the real metal slam's first blow, pitched down, choked),
        and the drop pin on its chain pushed through the hook (a short iron scrape and a small solid clank as it seats,
        the chain clinking: the packs' small metal).""",
        sources=[SLAM] + PLATE_H + METAL_H + S("metal_02", "metal_06"), takes=3, lufs=-22)
def filled_pin(rng, k):
    clank = mix(strike(SLAM, -rng.uniform(3, 4), lo=100, at=0.04, tau=0.04), iron(rng, k, -3, 0.03, 0.4) * 0.5)
    scr = ck.friction(rng, 0.07, 300, 800, 4500, 0.6, env([(0, 0), (0.02, 1), (0.07, 0.4)], 0.07))
    seat = iron(rng, k + 2, 1, 0.02, 0.6)
    chain = strike(S("metal_02", "metal_06")[k % 2], -rng.uniform(4, 6), lo=800, at=0.02, tau=0.03)
    t = rng.uniform(0.17, 0.22)
    y = ck.place([(0, norm(clank), 0), (t, scr, -12), (t + 0.06, seat, -5), (t + 0.11, chain, -16)])
    return oneshot(hp(outdoors(y, rng, 0.06), 120, 4), 0.55)


# ---- Picking a Holdout's lock ----------------------------------------------------------------------------------------------

def strain(rng, length, body, rate=(12, 35), q=20):
    """Iron under a lever, quietly: a slow stick-slip creak that tightens as the pull comes on."""
    c = synth.creak(length, env([(0, rate[0]), (length * 0.7, rate[1]), (length, rate[0])], length), rng, body=body, q=q,
                    jitter=0.45, grit=0.2)
    return norm(hp(c, 200) * env([(0, 0), (length * 0.4, 1), (length, 0)], length))


@recipe("place-breach", "pick", "lever",
        "A lock worked open quietly with a wrench: the hasp levered and straining, the shackle scraping, the wards ticking",
        """Careful, slow and quiet (the smash is heard as a cannon; this isn't): the wrench seated on the hasp (a light
        tick), the hasp straining under the lever (a slow modelled iron creak) and its screws creaking in the door's wood,
        the shackle scraping in its hole (a short iron friction), the wards inside the lock ticking and scraping (the real
        lock's mechanism clicks, pitched down and kept low), a small give now and then. Outdoors at a shed. 12 s exact
        cycle.""", sources=METAL_L + METAL_M + [LOCK], loop=True, takes=1, lufs=-29)
def pick_lever(rng, k):
    ev, t, i = [], 0.2, 0
    shackle = [f * 2.2 for f in synth.IRON[1:6]]
    screws = [f * 1.3 for f in synth.WOOD[:5]]
    while t < LOOP - 0.6:
        ev.append((t, strike(METAL_L[i % 5], -rng.uniform(2, 5), lo=900, at=0.004, tau=0.012), 0.25))
        L = rng.uniform(0.6, 1.1)
        ev.append((t + 0.08, strain(rng, L, shackle), 0.4))
        if rng.random() < 0.6:
            ev.append((t + 0.2, strain(rng, L * 0.8, screws, (6, 14), 10), 0.2))
        if rng.random() < 0.5:
            s = rng.uniform(0.15, 0.3)
            ev.append((t + 0.08 + L * 0.5, ck.friction(rng, s, 200, 900, 5000, 0.7, env([(0, 0), (0.03, 1), (s, 0)], s)), 0.2))
        for w in range(int(rng.integers(1, 4))):
            c = strike(LOCK, -rng.uniform(4, 6), lo=500, at=0.01, tau=0.02, i=int(rng.integers(5)), length=0.08)
            ev.append((t + 0.08 + L + 0.1 + w * rng.uniform(0.12, 0.22), c, rng.uniform(0.15, 0.3)))
        if rng.random() < 0.35:
            ev.append((t + 0.08 + L * 0.9, strike(METAL_M[i % 5], rng.uniform(0, 2), lo=800, at=0.004, tau=0.01), 0.18))
        t += 0.08 + L + rng.uniform(0.6, 1.1)
        i += 1
    return cycle(ev, rng, 0.1)


@recipe("place-breach", "pick", "wards",
        "A lock worked open quietly with a wrench: real lock clicks, a scraping shackle and a creaking hasp, all kept small",
        """The packs' real recordings, bent small and quiet: the lock's mechanism (sfx_100's lock turning, cut to its
        clicks, pitched down) for the wards; Kenney's door creak pitched down a fourth, cut short, for the hasp's iron
        squeaking as it's levered; sfx_100's metal scrape, pitched down and kept short, for the shackle; the packs'
        ratchet ticked once as something gives. Outdoors at a shed. 12 s exact cycle.""",
        sources=[LOCK, SCRAPE, RATCHET] + R("creak1", "creak2", "creak3"), loop=True, takes=1, lufs=-29)
def pick_wards(rng, k):
    creaks = R("creak1", "creak2", "creak3")
    ev, t, i = [], 0.2, 0
    while t < LOOP - 0.6:
        c = dsp.vari(hit_of(creaks[i % 3], int(rng.integers(3)), 0.3), -rng.uniform(4, 6))
        ev.append((t, dsp.fade(norm(hp(c, 500, 4)), 0.04, 0.12), 0.25))
        s = rng.uniform(0.18, 0.3)
        ev.append((t + rng.uniform(0.1, 0.3), scrape_pull(rng, s), 0.2))
        for w in range(int(rng.integers(2, 5))):
            cl = strike(LOCK, -rng.uniform(3, 6), lo=500, at=0.01, tau=0.02, i=int(rng.integers(5)), length=0.08)
            ev.append((t + 0.5 + w * rng.uniform(0.1, 0.2), cl, rng.uniform(0.15, 0.35)))
        if rng.random() < 0.3:
            ev.append((t + rng.uniform(0.9, 1.3), strike(RATCHET, -rng.uniform(5, 7), lo=600, at=0.01, tau=0.02), 0.2))
        t += rng.uniform(1.6, 2.4)
        i += 1
    return cycle(ev, rng, 0.1)


@recipe("place-breach", "pick-give", "click",
        "The lock giving: its mechanism clicking over, the hasp swinging free on its staple and knocking the door",
        """The lock's last ward giving (the loudest click of sfx_100's real lock, pitched down a little) with the shackle
        springing in it (a light metal tick), then the hasp swinging free on its staple (a short dry iron squeak) and
        knocking the planks of the door (a small real wooden knock) once, smaller again.""",
        sources=[LOCK] + METAL_L + ck.WOOD_KNOCKS[:2], takes=2, lufs=-25)
def give_click(rng, k):
    click = strike(LOCK, -rng.uniform(1, 2), lo=300, at=0.02, tau=0.03, i=3, length=0.1)
    spring = strike(METAL_L[(k + 1) % 5], rng.uniform(-2, 0), lo=1000, at=0.008, tau=0.02)
    L = rng.uniform(0.18, 0.26)
    sq = synth.creak(L, env([(0, 60), (L * 0.5, 140), (L, 70)], L), rng, body=[f * 3 for f in synth.IRON[1:5]], q=30)
    sq = norm(hp(sq, 400) * env([(0, 0), (0.03, 1), (L, 0)], L))
    knock = strike(ck.WOOD_KNOCKS[k % 2], rng.uniform(2, 4), lo=250, at=0.03, tau=0.03)
    t = 0.06 + L
    y = ck.place([(0, click, 0), (0.008, spring, -12), (0.06, sq, -14), (t, knock, -9), (t + rng.uniform(0.11, 0.16), knock, -18)])
    return oneshot(W.space(y, rng, "night", wet=0.12), 0.75)


@recipe("place-breach", "pick-give", "spring",
        "The lock giving: a real latch snapping open, the padlock dropping and swinging against the door",
        """A real latch's snap (sfx_100's door latch, pitched down) as the shackle springs open, then the opened padlock
        dropping on the swinging hasp and knocking against the door twice (the packs' small metal on a light wooden
        knock), its loose shackle rattling.""",
        sources=S("door_04", "door_05", "metal_02", "metal_06") + ck.WOOD_KNOCKS[:2], takes=2, lufs=-24)
def give_spring(rng, k):
    snap = strike(S("door_05", "door_04")[k], -rng.uniform(2, 3), lo=300, at=0.04, tau=0.03)
    hits = []
    t = rng.uniform(0.16, 0.2)
    for j in range(2):
        m = strike(S("metal_02", "metal_06")[(k + j) % 2], -rng.uniform(5, 7), lo=500, at=0.015, tau=0.02)
        w = strike(ck.WOOD_KNOCKS[(k + j) % 2], rng.uniform(3, 5), lo=250, at=0.025, tau=0.03)
        hits += [(t, m, -6 - 8 * j), (t + 0.003, w, -10 - 8 * j)]
        t += rng.uniform(0.14, 0.2)
    rattle = ck.grains(rng, 4, 0.06, 2500, 5500, q=(15, 30), length=(0.008, 0.02), shape=0.3)
    y = ck.place([(0, snap, 0)] + hits + [(t - 0.1, norm(rattle), -22)])
    return oneshot(W.space(y, rng, "night", wet=0.12), 0.7)


# ---- The steam lift at the mine head ------------------------------------------------------------------------------------

def rope(n, rng, freqs, strand=41.0, q=45):
    """A wire rope running over its sheave: its strands drumming over the groove (a buzz at the strand rate) and the
    rope singing (narrow, unsteady tones that come and go), with the hiss of it through the air. Periodic over n."""
    sing = W.howl(n, rng, freqs, 1.0, wander=0.02, rate=0.5, q=q)
    t = np.arange(n) / SR
    cyc = round(strand * n / SR) / (n / SR)                          # a whole number of strand passes in the cycle
    buzz = 1 + 0.6 * np.sin(2 * np.pi * cyc * t) ** 2
    hiss = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(3500)) / 0.8) ** 2))
    return norm(sing * buzz.astype(np.float32)) + norm(hiss) * 0.25


def drum(n, rng, turns):
    """The rope drum turning: a low-mid rumble that swells once a turn (the rope's lap crossing)."""
    t = np.arange(n) / SR
    r = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(220)) / 0.6) ** 2))
    swell = 0.7 + 0.3 * np.cos(np.pi * turns * t / (n / SR)) ** 2
    return norm(r * swell.astype(np.float32))


def pawl(rng, n, rate, semis):
    """The drum's pawl dragging over its ratchet wheel: a real ratchet click (sfx_100's) pitched for a big iron pawl."""
    count = int(round(rate * n / SR))
    clicks = [strike(RATCHET, semis + rng.uniform(-0.4, 0.4), lo=400, at=0.008, tau=0.02) for _ in range(3)]
    return W.place(n, [(j * (n / SR) / count, clicks[j % 3], rng.uniform(0.6, 1.0)) for j in range(count)])


BEAT = 4.0                                          # exhausts a second: a twin double-acting engine at one turn a second


@recipe("place-mine-lift", "winding", "engine",
        "The winding engine on the engine's steam: a small engine's quick double beat, the drum, the rope, the pawl",
        """At the engine house: a small twin engine on the locomotive's vented steam beating four exhausts a second (the
        blast-pipe exhaust model, pitched up seven semitones for a small engine and kept above its chimney's boom),
        accented as the cranks come round, its crossheads knocking (real medium iron, pitched down, choked), the rope drum
        rumbling with a swell once a turn, the wire rope singing over the sheave up the headframe, and the drum's pawl
        clicking over its ratchet (the packs' real ratchet, pitched down). Loud, outdoors at night. 12 s exact cycle, the
        beat locked to it.""",
        sources=METAL_M + [RATCHET], loop=True, takes=1, lufs=-19)
def winding_engine(rng, k):
    n = samples(LOOP)
    beats = [norm(hp(dsp.vari(exhaust(rng, False), 7), 160, 2)) for _ in range(4)]
    acc = (1.0, 0.6, 0.85, 0.55)
    count = int(BEAT * LOOP)
    eng = W.place(n, [(i / BEAT, beats[i % 4], acc[i % 4] * rng.uniform(0.9, 1.0)) for i in range(count)])
    cross = W.place(n, [(i / BEAT + 0.11, strike(METAL_M[i % 5], -rng.uniform(5, 7), lo=200, at=0.006, tau=0.02), 0.5)
                        for i in range(count)])
    y = norm(eng) * 0.75 + norm(cross) * 0.15 + drum(n, rng, 6) * 0.12 + rope(n, rng, [640, 960]) * 0.08 \
        + norm(pawl(rng, n, 8, -6)) * 0.12
    return W.seamless(circ_outdoors(W.cyclic(lambda z: hp(z, 90, 2), y), rng, 0.12))


@recipe("place-mine-lift", "winding", "headframe",
        "The winding engine heard at the shaft head: the rope singing over the sheave, the engine's beat from its house",
        """At the shaft head under the headframe: the wire rope running over the sheave above, singing and buzzing as its
        strands drum over the groove (modelled), the sheave's bearing grumbling, the drum's pawl clicking (the real
        ratchet); and the engine thirty metres off, its four exhausts a second out of an open pipe (modelled steam jet
        pulses, no chimney), darker and further. 12 s exact cycle, the beat locked to it.""",
        sources=[RATCHET], loop=True, takes=1, lufs=-19)
def winding_headframe(rng, k):
    n = samples(LOOP)
    t = np.arange(n) / SR
    ph = (t * BEAT) % 1.0
    acc = np.array([1.0, 0.65, 0.9, 0.6])[(np.floor(t * BEAT).astype(int)) % 4]
    p = (acc * (1 - np.exp(-ph / 0.008)) * np.exp(-ph / 0.06)).astype(np.float32)
    chuff = W.jet(LOOP, rng, n=n, pressure=p, peak=1100, low=0.2, eddy=0.6)
    chuff = W.cyclic(lambda z: lp(hp(z, 150, 2), 3000), chuff)
    bearing = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(320)) / 0.5) ** 2))
    bearing *= (0.7 + 0.3 * np.sin(2 * np.pi * 24 * t / LOOP) ** 2).astype(np.float32)
    y = norm(chuff) * 0.6 + rope(n, rng, [760, 1150], q=35) * 0.22 + norm(bearing) * 0.1 \
        + norm(pawl(rng, n, 8, -6)) * 0.1 + drum(n, rng, 6) * 0.06
    return W.seamless(circ_outdoors(W.cyclic(lambda z: hp(z, 90, 2), y), rng, 0.2))


def landing(rng, size=1.0):
    """Ore landing in a wooden car: the car's heavy wooden thump (real heavy wood, pitched down and kept above the
    car's boom) and the stones scattering over the load."""
    wood = mix(norm(hp(W.piece(rng, "wood", (-6, -3)), 110, 2)), norm(W.body(rng, rng.uniform(130, 160), W.PLATE,
                                                                          decay=0.06, contact=0.003)) * 0.3)
    return mix(norm(wood) * size, norm(W.spray(rng, 0.4, 30)) * 0.45 * size)


TIP = (1.8, 2.0, 1.6)                               # the takes' lengths: a skip's quarter load takes about two seconds


def tip_frame(rng, k, clank, slide, land, scatter):
    """A tip in its order: the skip's clank, the slide starting a moment after and running out, the ore landing in the
    car from 0.6 s while it still pours, the last stones scattering; cut to length with a short outdoor tail."""
    L = TIP[k]
    b = dsp.Bus(L + 1.0)
    b.at(0, clank)
    b.at(0.15, slide)
    for j, t in enumerate(np.sort(rng.uniform(0.6, min(1.4, L - 0.5), 3))):
        b.at(t, land(rng, j) * (0.9, 0.65, 0.5)[j])
    b.at(L - 0.6, scatter)
    return oneshot(outdoors(hp(b.x, 100, 2), rng, 0.12), L + 0.3, 0.25)


@recipe("place-mine-lift", "tip", "chute",
        "A skip tipped: a steel clank, the ore roaring and rattling down the steel chute, landing in the wooden car",
        """The skip tipping over in the headframe (a heavy real iron clank, choked, its door's chain rattling), then a
        quarter car-load of ore down the steel chute (the pour model: lumps cracking on the plates and each other, the
        big ones knocking, the mass of it a short rush, the riveted chute ringing under it), and the ore landing in the
        wooden car while it still pours (heavy real wood knocks pitched down, kept above the car's boom, stones
        scattering).""",
        sources=W.PIECES["iron"] + W.PIECES["scrap"] + W.PIECES["brick"] + W.PIECES["wood"], takes=3, lufs=-19)
def tip_chute(rng, k):
    L = TIP[k]
    clank = mix(norm(hp(W.piece(rng, "iron", (-7, -4), tau=0.25), 120, 2)),
                *[np.concatenate([np.zeros(samples(0.04 + j * 0.05)), norm(W.piece(rng, "scrap", (-5, -1), tau=0.06)) * 0.2])
                  for j in range(5)])
    m = samples(L - 0.15)
    flow = env([(0, 50), (0.12, 2600), (0.5 + 0.1 * k, 2200), (L - 0.65, 300), (L - 0.15, 10)], L - 0.15)
    slide = W.pour(m, rng, flow, grain=(700, 4500), lump=12, thunder=0.35, ring=CHUTE, loop=False)
    lumps = [(t, norm(W.piece(rng, "brick", (-8, -3))), rng.uniform(0.2, 0.5)) for t in np.sort(rng.uniform(0, L - 0.5, 10))]
    slide = norm(hp(slide, 120, 2)) + norm(W.place(m, lumps, loop=False)) * 0.35
    slide = norm(slide) * env([(0, 0.5), (0.1, 1), (L - 0.5, 0.8), (L - 0.15, 0)], L - 0.15)[:m]
    return tip_frame(rng, k, norm(clank) * 0.8, slide * 0.75, lambda r, j: landing(r, 1.0) * 0.6,
                     norm(W.spray(rng, 0.5, 20)) * 0.2)


@recipe("place-mine-lift", "tip", "rubble",
        "A skip tipped: the ore's slide built from real stone, a clatter of rock rushing down the chute into the car",
        """The slide made of the packs' real stone: sfx_100's stone cracks and Kenney's mining hits scattered thick and fast
        (a hundred-odd a second at the height of it, pitched about for sizes), rung lightly through the steel chute's
        plate modes; the skip's clank real heavy iron pitched down; the landing Kenney's real heavy wood and plank hits
        pitched down (kept above the car's boom), with the stones still scattering after.""",
        sources=STONES + MINING + METAL_H + PLATE_H + WOOD_H + PLANK, takes=3, lufs=-19)
def tip_rubble(rng, k):
    L = TIP[k]
    clank = mix(hp(iron(rng, k, -8, 0.08, 0.5), 120, 2), strike(METAL_H[(k + 3) % 5], -6, lo=150, at=0.02, tau=0.05) * 0.5)
    m = samples(L - 0.15)
    rate = env([(0, 30), (0.1, 110), (0.55, 90), (L - 0.6, 20), (L - 0.15, 2)], L - 0.15)
    ev, t = [], 0.0
    while t < L - 0.15:
        r = float(rate[min(samples(t), m - 1)])
        t += rng.exponential(1 / max(r, 1))
        key = STONES[int(rng.integers(3))] if rng.random() < 0.8 else MINING[int(rng.integers(5))]
        x = dsp.vari(hit_of(key, int(rng.integers(2)), 0.12), rng.uniform(-7, 3))
        ev.append((t, norm(hp(x, 200)), rng.uniform(0.15, 0.6) * min(1.0, r / 60)))
    stream = W.place(m, ev, loop=False)
    stream = mix(norm(stream), norm(hp(dsp.resonate(stream, CHUTE[2:], q=40), 300, 2)) * 0.25)

    def land(r, j):
        w = mix(strike(WOOD_H[(k + j) % 5], -r.uniform(2, 4), lo=170, at=0.04, tau=0.05),
                strike(PLANK[(k + j) % 5], -r.uniform(2, 4), lo=170, at=0.04, tau=0.05) * 0.6)
        return norm(w)

    tail = W.place(samples(0.6), [(rng.uniform(0, 0.5), norm(hp(dsp.vari(hit_of(STONES[j % 3], 0, 0.1), rng.uniform(-3, 3)), 300)),
                                   rng.uniform(0.2, 0.5)) for j in range(6)], loop=False)
    return tip_frame(rng, k, norm(clank) * 0.8, norm(stream) * 0.7, land, norm(tail) * 0.3)
