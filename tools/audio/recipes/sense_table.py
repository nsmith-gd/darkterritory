"""The sense table heard (queue #511, note 781; K1's #1026, note 728): the crew's things now speak to the creatures'
senses, and each is heard as the thing itself, as tools/audio/cues.py lists them.

What each is decides the build:
- A knock on the rail (`world-rail-knock.knock`): something heavy (a crate, a keg, a find, a lump of coal) thrown down and
  landing hard on a rail beside the line, outdoors at night. It's what the Car Hugger and the Tower Jaw hunt by: the
  vibration running down the steel. A real rail struck: the dull thunk of the blow on the railhead, then the rail ringing
  bright and long. A rail is a steel beam on sleepers, so its ring is inharmonic (the head bending between two sleepers,
  the web, the foot: 1-5 kHz), and each of its modes is a close pair (bending up and down, bending sideways) a few hertz
  apart, so the ring beats in a slow flutter. The ring runs off down the line: what's close dies first, what's far
  lingers darker in the night, 2-3 s in all. Three takes: square on the railhead; a bounce, the ring struck again; and
  one that tips off onto the ballast.
- A lamp broken (`crew-lamp-broken.smash`, `.burn`): a crewmate's kerosene hurricane lantern (a tin frame and cage, a
  glass globe, a font of oil) thrown and brought up short, on the ground or a car's floor. The globe shatters, the tin
  frame clatters and rolls a beat, and the oil spilling over the wick catches with a soft whump: a quick flare, a puff of
  flame, never a boom. Then the oil burns where it lies for 25 s: a small fire's crackle, the flutter of its flames, the
  odd spit of the oil. Small and close, outdoors: much smaller than a car alight (tells.py).
- A hound doused (`cs-hounds-doused.doused`): a Cinder Hound caught in the extinguisher's spray. The water flashes to steam
  off its ember-hot hide (a sharp spitting hiss that dies away, the embers in the cracks of its flanks quenched) and it
  yelps, hurt and startled, backing off. The jet itself is crew_items.py's, not here. The same animal as beasts.py's: its
  kept yelp ('kiyi': a big dog's throat with the embers crackling in the breath) and its kept snarl's burnt-out throat
  ('cinder').

Each cue has two candidates. For the gameplay sounds the first is kitbashed from the packs' real recordings, bent: the
packs' heavy iron struck, its clean ring paulstretched out into a rail's long one and beating against a twin a few hertz
off; real glass breaking, a real tin can knocked about, a real whoomph of air and real moving water slowed into a flame;
real cracks cut into a fire's crackle and a real steam hiss cut into the oil's spit. The second is modelled: a rail's
modes struck through a blunt contact; a globe's fracture and its shards' rings, a tin frame's plate modes, a flare as noise
through an opening filter; a fire as noise lapping through a lowpass with burst-sap crackle. The hound's two are both
kitbashes of its kept voice with real steam: one led by the steam's flash, one by its voice.

Every impact keeps its thump out of the squish: under 45% of its energy below 150 Hz (the director's rule), its weight in
the low mids.
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import ash, cinder_voice, embers, huff, throat
from recipes.crew_kit import recipe, R, S, K
from recipes.douse import sizzle
from recipes.g1_ground import CRACKS as CRACKS_WOOD, splinter
from recipes.house_folk import LEAD as BED_LEAD, SEAM, loop_of
from recipes.up_forge_bbq import blaze, breaths, breathing, roar_rec, sizzle_rec

SR = dsp.SR
SQUISH = 150.0              # the director's line: an impact keeps most of its energy above it
LEAD = 0.012                # silence before a one-shot's first part, so build.py's fade-in lands on nothing


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def unit(x):
    return (np.asarray(x, np.float32) / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def fshift(x, hz):
    """Move every partial up by the same `hz` (a single-sideband shift through the analytic signal): a twin made this way
    beats with its original at one slow rate across the whole ring, as a rail's paired modes do."""
    a = signal.hilbert(np.asarray(x, np.float64))
    t = np.arange(len(x)) / SR
    return np.real(a * np.exp(2j * np.pi * hz * t)).astype(np.float32)


def first_hit(key, length, pre=0.002, floor_db=-20):
    """A recording from its first hit (less `pre`), `length` s, with short fades."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=floor_db, gap=0.01)
    a = (h[0][0] if h else int(np.argmax(np.abs(x)))) - samples(pre)
    return ck.cut(x, a, a + samples(length), 0.0005, min(0.03, length / 3))


def beside_line(x, rng, wet, tail):
    """Outdoors beside the line at night: two quick slaps off the train's side, then the open night (world_kit.space),
    its reverb cut `tail` s after the sound so a one-shot fired again doesn't pile up."""
    y = W.space(np.concatenate([np.zeros(samples(LEAD), np.float32), x]), rng, "night", wet=wet)
    return dsp.fade(y[:len(x) + samples(LEAD + tail)], 0.0, tail)


# ---- world-rail-knock: a rail struck ------------------------------------------------------------------------------------

RINGS = K("impactMetal_heavy")              # heavy iron struck: a clean ring at ~1.05, 1.2-1.3, 2.8-3.6 and 4-4.5 kHz
BLOW = "sfx_100_v2:metal_hit_01"            # a heavy iron clang: dense and dark, the blow's body on the railhead
CARGO = S("wood_hit_02", "misc_08")         # a heavy wooden thing knocked: the crate or keg's own body
BALLAST = S("stones_01", "stones_02", "stones_03")   # real stones knocked: the ballast it tips off onto
RING_L = 3.2                                # how long a rail's ring runs (s)


@functools.lru_cache(maxsize=8)
def _ring_of(key):
    """A real iron ring from its strike: the recording's low lobe (the Kenney hits' thump) filtered off, and the steady
    part of its ring just after the strike, the grain a long ring is drawn out of."""
    x = ck.get(key)
    a = int(np.argmax(np.abs(x)))
    strike = hp(x[max(0, a - samples(0.002)):], 600, 4)
    return strike, strike[samples(0.03):samples(0.03) + samples(0.22)].copy()


def keep_partials(x, grain, floor_db=-36, width=10.0):
    """Only the grain's own partials in x: narrow bands (`width` Hz) round each peak of the grain's spectrum within
    `floor_db` of its loudest, applied zero-phase. Paulstretch smears a recording's noise floor into a steady wash
    along with its partials; this keeps the iron's ring and drops the wash."""
    N = 1 << 17
    G = np.abs(np.fft.rfft(grain * np.hanning(len(grain)), N))
    db = 20 * np.log10(G / (G.max() + 1e-12) + 1e-12)
    pk, _ = signal.find_peaks(db, height=floor_db, prominence=10, distance=int(40 * N / SR))
    fp = np.fft.rfftfreq(N, 1 / SR)[pk]
    F = np.fft.rfftfreq(len(x), 1 / SR)
    m = np.zeros(len(F))
    for f in fp:
        lo, hi = np.searchsorted(F, [f - 5 * width, f + 5 * width])
        m[lo:hi] = np.maximum(m[lo:hi], np.exp(-0.5 * ((F[lo:hi] - f) / width) ** 2))
    return np.fft.irfft(np.fft.rfft(x) * m, len(x)).astype(np.float32)


def drawn_ring(rng, key, length, semis=0.0, tau=0.5, beat=2.6, strike=True):
    """A real ring drawn out to a rail's: the iron's own strike (if `strike`), then its steady ring paulstretched to
    `length` s (its partials kept, its decay ours: `tau` s, the top dying faster as a lowpass closes), beating against a
    twin of itself `beat` Hz up (the rail's paired modes), so it flutters slowly as it dies."""
    hit, grain = _ring_of(key)
    hit, grain = dsp.vari(hit, semis), dsp.vari(grain, semis)
    n = samples(length)
    a = dsp.fit(dsp.smear(grain, length / (len(grain) / SR) + 0.1, 0.4, rng), n)
    b = dsp.fit(fshift(dsp.smear(grain, length / (len(grain) / SR) + 0.1, 0.4, rng), beat), n)
    held = keep_partials(unit(a) + unit(b) * rng.uniform(0.4, 0.55), grain)
    held = dsp.sweep_filter(held, "lp", env([(0, 10000), (length, 3500)], length, "exp"), q=0.6)
    decay = env([(0, 0), (0.025, 0.2), (0.12, 1.0), (length, 1.0)], length) * np.exp(-np.arange(n) / SR / tau)
    held = unit(held * decay[:n]) * 0.55
    if not strike:
        return held
    return mix(dsp.fit(dsp.shaped(unit(hit), [(0, 1), (0.08, 0.8), (0.25, 0.0)]), n), held)


def run_off(far, rng, cut=1800):
    """The ring running off down the line: the ring from further along the rail (one that dies slower), darker and deep
    in the night's reverb, so as the close ring fades the far one is what's left."""
    return dsp.room(lp(far, cut, 2), "night", wet=0.75, rng=rng)


def knock(rng, k, thunk, ring, ground):
    """Three landings on the same rail: square on the railhead; a bounce, the rail struck again a beat later and
    lighter; and one that lands on the rail's edge and tips off onto the ballast, the ring running on free. `thunk(rng,
    k, weight)` is the blow, `ring(rng, k, semis, tau, far)` the rail's ring (`far`: from down the line, no strike in it),
    `ground(rng, k)` the ballast taking it."""
    semis = rng.uniform(-0.6, 0.6)
    blows = Bus(0.8)
    rings = Bus(RING_L + 0.4)
    blows.at(0, thunk(rng, k, 1.0), 0)
    rings.at(0.001, ring(rng, k, semis, 0.5, False), -2)
    rings.at(0.03, run_off(ring(rng, k, semis, 0.95, True), rng), -12)
    if k % 3 == 1:
        tb = rng.uniform(0.13, 0.18)
        blows.at(tb, thunk(rng, k + 1, 0.6), -9)
        rings.at(tb + 0.001, ring(rng, k + 1, semis, 0.35, False), -11)
    if k % 3 == 2:
        blows.at(rng.uniform(0.22, 0.3), ground(rng, k), -6)
    # the blows get the open night's slaps and a short tail; the rings carry their own night (run_off)
    y = mix(beside_line(blows.x, rng, 0.04, tail=0.25), np.concatenate([np.zeros(samples(LEAD), np.float32), rings.x]))
    return dsp.fade(dsp.fit(hp(y, 140, 4), samples(RING_L + 0.2)), 0.0, 0.5)


RING_KEYS = [RINGS[1], RINGS[3], RINGS[0], RINGS[4]]


def thunk_rec(rng, k, weight):
    """The blow on the railhead from real recordings: the heavy iron clang's first instant dropped and darkened (the
    rail taking a blunt weight, not a hammer), and the thing's own body knocked (a heavy wooden knock, dropped)."""
    iron = ck.choke(lp(dsp.vari(first_hit(BLOW, 0.2), rng.uniform(-6, -4)), 2600), 0.03, 0.05)
    body = ck.choke(dsp.vari(first_hit(CARGO[k % 2], 0.25), rng.uniform(-3, -1)), 0.05, 0.06)
    return mix(unit(iron), unit(body) * 0.7 * weight)


def ring_rec(rng, k, semis, tau, far):
    """Two real rings drawn out, the second a few semitones up so its partials fall between the first's: one crowded,
    inharmonic ring from 1 to 5 kHz, as a rail's is."""
    main = drawn_ring(rng, RING_KEYS[k % len(RING_KEYS)], RING_L, semis, tau, rng.uniform(2.2, 3.4), not far)
    over = drawn_ring(rng, RING_KEYS[(k + 1) % len(RING_KEYS)], RING_L, semis + rng.uniform(4, 5), tau * 0.8,
                      rng.uniform(3.0, 4.2), False)
    return mix(main, over * 0.4)


def ballast_rec(rng, k):
    """It tips off onto the ballast: a slice of real stones knocked, dropped for a heavy thing, over a dull pad."""
    x = ck.get(BALLAST[k % 3])
    h = ck.hits(x, floor_db=-14, gap=0.03)
    a = h[0][0] if h else 0
    st = unit(lp(dsp.vari(ck.cut(x, a - samples(0.002), a + samples(0.22), 0.001, 0.08), rng.uniform(-6, -4)), 4000))
    return mix(st, ck.pad(rng, 0.09, 320) * 0.7)


@recipe("world-rail-knock", "knock", "clank",
        "Something heavy landing on a rail: a dull iron thunk and the rail's long bright ring beating as it runs off",
        """From real recordings: the blow is the packs' heavy iron clang cut to its first instant, dropped and darkened
        (a blunt weight, not a hammer), with a heavy wooden knock for the crate or keg itself. The ring is the clean ring
        of Kenney's heavy iron hits (a bar's inharmonic partials, 1-1.3 kHz and 3-4.5 kHz), two of them, the second a
        few semitones up so their partials crowd 1-5 kHz, paulstretched out to three seconds (only their partials kept)
        and each beating against a twin of itself a few hertz up, as a rail's paired modes do; a darker copy lingers
        in the night's reverb as the close ring dies, so it runs off down the line. Takes: square on the
        railhead; a bounce that strikes it again; one that tips off onto the ballast (real stones).""",
        sources=RINGS + [BLOW] + CARGO + BALLAST, takes=3, lufs=-18)
def rail_clank(rng, k):
    return knock(rng, k, thunk_rec, ring_rec, ballast_rec)


# A rail's modes on its sleepers (Hz): the head bending between two sleepers and its overtones, then the web and the foot
# flapping, inharmonic and crowded from 1 to 5 kHz.
RAIL = [1020, 1290, 1640, 1870, 2310, 2530, 2950, 3310, 3780, 4170, 4590, 4990]


def rail_modes(rng, length, semis=0.0, tau=0.5, contact=0.0003, top=True):
    """A rail struck, modelled: each mode a pair (bending up and down, bending sideways) 1.5-3.5 Hz apart, so they beat;
    the higher modes dying faster; with `top`, a few quick bright ones over 5 kHz from the railhead where it's struck;
    through a contact of `contact` s (a blunt thing is slow, so the top is softer)."""
    fs, ds, am = [], [], []
    for i, f0 in enumerate(RAIL):
        f = f0 * 2 ** (semis / 12) * (1 + 0.006 * rng.standard_normal())
        d = tau * (RAIL[0] / f0) ** 0.45 * rng.uniform(0.8, 1.2)
        a = rng.uniform(0.4, 1.0) / (1 + 0.15 * i)
        fs += [f, f + rng.uniform(1.5, 3.5)]
        ds += [d, d * rng.uniform(0.8, 1.1)]
        am += [a, a * rng.uniform(0.55, 0.9)]
    for f in rng.uniform(5600, 8800, 4 if top else 0):
        fs.append(f)
        ds.append(rng.uniform(0.01, 0.03))
        am.append(rng.uniform(0.15, 0.3))
    return W.modal(fs, ds, am, length, rng, contact)


def ring_model(rng, k, semis, tau, far):
    """The modelled ring, struck a little blunter or harder take by take (what landed differs)."""
    contact = (0.0003, 0.00045, 0.0002, 0.00035)[k % 4]
    return unit(rail_modes(rng, RING_L, semis, tau, contact, not far)) * 0.6


def thunk_model(rng, k, weight):
    """The blow modelled: the thing's mass stopped dead (a dull swell and fall of low-mid noise, no tone in it), its own
    body's dull modes (a wooden box's broad, quick ones) and the contact's short burst of noise."""
    mass = ck.pad(rng, rng.uniform(0.09, 0.13), rng.uniform(380, 480))
    box = W.body(rng, rng.uniform(210, 260), W.PLATE, decay=0.035, contact=0.0012, count=8, length=0.25)
    burst = bp(synth.noise(0.04, rng), 300, 3500) * env([(0, 1), (0.04, 0)], 0.04, "exp")
    return mix(unit(mass) * 0.8 * weight, unit(box) * 0.6, unit(burst) * 0.5)


def ballast_model(rng, k):
    """The ballast taking it, modelled: stones knocking under the weight (hard grains, front-loaded) over a dull pad."""
    return mix(unit(ck.grains(rng, 22, 0.12, 700, 4500, q=(3, 8))) * 0.8, ck.pad(rng, 0.09, 320) * 0.7)


@recipe("world-rail-knock", "knock", "modes",
        "Something heavy landing on a rail, modelled: a dull thunk and the rail's struck modes ringing and beating",
        """Modelled from what a rail is: twelve inharmonic modes from 1 to 5 kHz (the railhead bending between two
        sleepers, then its web and foot), each a pair a couple of hertz apart (bending up and down, bending sideways)
        so the ring beats slowly, the high ones dying first, a few quick bright ones over 5 kHz, all struck through a
        blunt contact. The blow under it is the thing's mass stopped dead and its own box's dull modes. A darker copy
        lingers in the night's reverb as the close ring dies. The same three landings as 'clank'.""",
        takes=3, lufs=-18)
def rail_modes_knock(rng, k):
    return knock(rng, k, thunk_model, ring_model, ballast_model)


# ---- crew-lamp-broken.smash: a lantern thrown down and broken -----------------------------------------------------------

GLOBE = S("misc_26", "misc_27", "glass_03")   # real glass breaking: a thin globe bursting
SHARDS = "sfx_100_v2:items_02"                  # real broken glass crunching and settling: the shards
TINKS = K("impactGlass_light")                  # small glass knocked (1.1-2.5 kHz): a shard landing, pitched up
TIN = K("impactTin_medium")                     # a tin can knocked (520-670 Hz): the lantern's frame and its font
CRUMPLE = K("impactPlate_light")                # thin plate struck: the font's sheet denting
WIRE = S("metal_02", "metal_06")                # small real metal clinks: the cage's wires and the bail
WHUMP = "sfx_100_v2:misc_32"                    # a soft whoomph of moving air (its head, before the knock at its end)
FLAME = "sfx_100_v2:loop_water_01"              # a mass of water moving: slowed and darkened into flame (roar_rec)
SMASH_L = 1.7


def tin_knock(rng, k, semis=0.0, hold=0.03):
    """The lantern's tin knocked on the ground: a real tin can's knock, a little lower for a lantern's frame, choked
    after `hold` s as its weight lies on it."""
    x = dsp.vari(first_hit(TIN[k % len(TIN)], 0.2), semis + rng.uniform(-2.5, -1.0))
    return unit(ck.choke(hp(x, 250, 2), hold, 0.04))


def clink(rng, k, semis=3.0):
    """A wire of the cage or the bail knocking: a small real metal clink, pitched up for thin wire and choked."""
    x = ck.get(WIRE[k % 2])
    h = ck.hits(x, floor_db=-18, gap=0.02)
    a = h[k % len(h)][0] if h else 0
    y = dsp.vari(ck.cut(x, a - samples(0.001), a + samples(0.08), 0.0005, 0.03), semis + rng.uniform(-1, 1))
    return unit(ck.choke(y, 0.004, 0.015))


def flutter(rng, length, depth=0.4, band=(5, 13)):
    """Flames fluttering: a level that tears at 5-13 Hz, unevenly, as a small flame does in the open."""
    n = samples(length)
    return np.clip(1 + depth * W.norm(bp(rng.standard_normal(n).astype(np.float32), *band, 2)), 0.1, 2.0)


def smash(rng, k, frame, globe, shards, clatter, whump):
    """The lantern thrown down: its tin frame and font brought up short and the globe bursting with it, shards
    settling, the frame clattering and rolling a beat; then the oil spilled over the wick catches with a soft whump and
    flares, settling to a small flame (the burn loop takes over). `whump(rng, k)` returns the flare from its start."""
    b = Bus(SMASH_L + 0.6)
    b.at(0, frame(rng, k), -2)
    b.at(0, ck.pad(rng, 0.05, 380), -12)            # its weight on the ground
    b.at(0.002, globe(rng, k), 0)
    b.at(0.03, shards(rng, k), -9)
    b.at(rng.uniform(0.1, 0.15), clatter(rng, k), -5)
    b.at(rng.uniform(0.34, 0.48), whump(rng, k), -8)
    y = hp(b.x, SQUISH, 4)
    return dsp.fade(dsp.fit(beside_line(y, rng, 0.06, tail=0.3), samples(SMASH_L)), 0.0, 0.25)


def frame_rec(rng, k):
    """The frame and the font hitting: a real tin can's knock and a thin plate denting with it (the font's sheet)."""
    dent = ck.choke(hp(dsp.vari(first_hit(CRUMPLE[k % 5], 0.25), rng.uniform(1.5, 3.0)), 300, 2), 0.02, 0.04)
    return mix(tin_knock(rng, k, 0.0, 0.04), unit(dent) * 0.6)


def globe_rec(rng, k):
    """The globe bursting: a real glass break from its first crack, its little low end cut (a thin globe has no
    weight in it)."""
    return unit(hp(ck.align(ck.get(GLOBE[k % len(GLOBE)])), 400, 2))


def shards_rec(rng, k):
    """The shards: a slice of real broken glass crunching and settling, and a few pieces landing apart (small real
    glass knocks pitched up into slivers and choked)."""
    x = ck.get(SHARDS)
    h = ck.hits(x, floor_db=-14, gap=0.02)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    b = Bus(0.7)
    b.at(0, unit(hp(dsp.vari(ck.cut(x, a, a + samples(0.22), 0.001, 0.08), rng.uniform(0, 2)), 1500, 2)), 0)
    for _ in range(int(rng.integers(4, 7))):
        t = rng.uniform(0.04, 0.55) ** 1.3
        g = dsp.vari(first_hit(TINKS[int(rng.integers(5))], 0.1), rng.uniform(7, 12))
        b.at(t, unit(ck.choke(g, 0.003, rng.uniform(0.008, 0.02))), rng.uniform(-24, -14))
    return b.x


def clatter_rec(rng, k):
    """The frame clattering and rolling a beat: a bounce, then the cage turning over on its wires, slowing, each knock
    quieter (real tin knocks, the wires' real clinks after them), and the tin scuffing the ground as it rolls."""
    b = Bus(0.9)
    b.at(0, tin_knock(rng, k + 1, 0.5, 0.02), 0)
    t, gap, db = 0.0, rng.uniform(0.08, 0.11), -4.0
    for i in range(int(rng.integers(4, 7))):
        t += gap
        gap *= rng.uniform(1.08, 1.22)
        db -= rng.uniform(2.0, 3.5)
        b.at(t, tin_knock(rng, k + 2 + i, 2.0, 0.008), db)
        b.at(t + rng.uniform(0.006, 0.02), clink(rng, i), db - 7)
    L = t + 0.05
    b.at(0.03, ck.friction(rng, L, 140, 900, 4500, 0.8, env([(0, 0.6), (L * 0.5, 1), (L, 0)], L)), -26)
    return b.x


def whump_rec(rng, k):
    """The oil catching, from real recordings: the head of a real whoomph of moving air, its front softened (a flame
    running over a pool in a few hundredths of a second, not a blast) and high-passed at 220 Hz (a puff, never a boom);
    real moving water slowed into the flame's rush, flaring and settling small; real cracks cut into its crackle."""
    w = ck.cut(ck.get(WHUMP), samples(0.02), samples(0.24), 0.002, 0.08)
    w = dsp.vari(w, rng.uniform(-1.5, 0.5))
    Lw = len(w) / SR
    w = lp(hp(w * dsp.fit(env([(0, 0), (0.035, 1), (Lw * 0.5, 0.6), (Lw, 0)], Lw), len(w)), 220, 4), 3500, 2)
    L = 1.3
    fl = hp(roar_rec(rng, L, rng.uniform(-8, -6), 1700), 250, 2) * flutter(rng, L)
    fl = dsp.shaped(fl, [(0, 0), (0.05, 1), (0.25, 0.45), (0.6, 0.25), (L, 0.18)])
    cr = hp(splinter(rng, L, env([(0, 70), (0.3, 25), (L, 10)], L), (2, 7)), 1500, 2)
    return mix(unit(w), unit(fl) * 0.55, unit(dsp.shaped(cr, [(0, 0), (0.04, 1), (L, 0.5)])) * 0.22)


@recipe("crew-lamp-broken", "smash", "shatter",
        "A thrown lantern smashing: the globe bursting, the tin frame clattering, the oil catching with a soft whump",
        """From real recordings: the lantern's tin frame and font hit the ground (a real tin can's knock, a thin plate
        denting under it) and the globe bursts (the packs' glass breaking), the shards crunching and a few slivers
        landing apart (real small glass knocks pitched up); the frame bounces and rolls a beat on its cage, slowing
        (real tin knocks and the wires' clinks). Then the spilled oil catches from the wick: the head of a real whoomph
        of air with a soft front, high-passed so it's a puff and never a boom, and a small rush of flame (real moving
        water slowed) flaring and settling with its crackle (real cracks cut into grains). 1.7 s, outdoors.""",
        sources=GLOBE + [SHARDS] + TINKS + TIN + CRUMPLE + WIRE + [WHUMP, FLAME] + CRACKS_WOOD, takes=3, lufs=-18)
def smash_shatter(rng, k):
    return smash(rng, k, frame_rec, globe_rec, shards_rec, clatter_rec, whump_rec)


GLASS_GLOBE = [1.0, 1.62, 2.31, 2.95, 3.7, 4.4]    # a thin glass globe's modes over its first


def frame_model(rng, k):
    """The frame and font hitting, modelled: a thin tin body's plate modes from about 500 Hz struck hard, and a dent's
    short buckle (a burst through its low modes)."""
    tin = W.body(rng, rng.uniform(470, 560), W.PLATE, decay=0.05, contact=0.0003, count=10, length=0.3)
    dent = W.body(rng, rng.uniform(300, 360), W.PLATE, decay=0.02, contact=0.0008, count=6, length=0.15)
    return mix(unit(tin) * 0.7, unit(dent) * 0.4)


def globe_model(rng, k):
    """The globe bursting, modelled: the fracture's crack (a couple of milliseconds of every band at once), the globe
    ringing for an instant as it goes, and its shards: forty-odd slivers ringing high and short, most breaking off at
    once, a few later, some bouncing."""
    b = Bus(0.8)
    crack = bp(synth.noise(0.006, rng), 1500, 14000) * env([(0, 1), (0.006, 0)], 0.006, "exp")
    b.at(0, unit(crack), 0)
    ring = W.body(rng, rng.uniform(1400, 1800), GLASS_GLOBE, decay=0.03, contact=0.0001, length=0.25)
    b.at(0.001, unit(ring), -3)
    for _ in range(int(rng.integers(36, 50))):
        f = np.exp(rng.uniform(np.log(2500), np.log(9500)))
        tau = rng.uniform(0.006, 0.03)
        s = W.modal([f, f * rng.uniform(1.4, 1.9)], [tau, tau * 0.6], [1.0, 0.5], tau * 6 + 0.01, rng, 0.00005)
        t = 0.002 + 0.3 * rng.random() ** 2.5
        g = 20 * np.log10(min(1.0, 0.15 * rng.pareto(1.5) + 0.08))
        b.at(t, unit(s), g + 2)
        if rng.random() < 0.25:
            b.at(t + rng.uniform(0.04, 0.12), unit(s), g - 8)
    return b.x


def shards_model(rng, k):
    """The shards settling, modelled: glass grains grinding (tiny high clicks, front-loaded) and a few slivers landing
    apart."""
    b = Bus(0.7)
    b.at(0, unit(ck.grains(rng, 30, 0.18, 3000, 9000, q=(15, 40), length=(0.004, 0.015))), 0)
    for _ in range(int(rng.integers(4, 7))):
        f = rng.uniform(3000, 7000)
        s = W.modal([f, f * 1.7], [0.012, 0.007], [1.0, 0.4], 0.08, rng, 0.00005)
        b.at(rng.uniform(0.04, 0.55) ** 1.3, unit(s), rng.uniform(-22, -12))
    return b.x


def clatter_model(rng, k):
    """The frame clattering and rolling a beat, modelled: the tin body knocked again as it bounces, then lighter as the
    cage turns over, slowing, a wire ticking after each knock, and the tin scuffing the ground."""
    b = Bus(0.9)
    f1 = rng.uniform(470, 560)
    b.at(0, unit(W.body(rng, f1, W.PLATE, decay=0.04, contact=0.0003, count=10, length=0.25)), 0)
    t, gap, db = 0.0, rng.uniform(0.08, 0.11), -4.0
    for i in range(int(rng.integers(4, 7))):
        t += gap
        gap *= rng.uniform(1.08, 1.22)
        db -= rng.uniform(2.0, 3.5)
        b.at(t, unit(W.body(rng, f1 * rng.uniform(1.1, 1.3), W.PLATE, decay=0.015, contact=0.0003, count=8,
                            length=0.12)), db)
        wire = W.body(rng, rng.uniform(2600, 3400), W.BAR, decay=0.03, contact=0.0001, count=3, length=0.12)
        b.at(t + rng.uniform(0.006, 0.02), unit(wire), db - 9)
    L = t + 0.05
    b.at(0.03, ck.friction(rng, L, 140, 900, 4500, 0.8, env([(0, 0.6), (L * 0.5, 1), (L, 0)], L)), -26)
    return b.x


def whump_model(rng, k):
    """The oil catching, modelled: noise through a lowpass that opens to 2.4 kHz in 50 ms as the flame runs over the
    pool and closes as it settles, swelling in 35 ms (soft, no blast front) and high-passed at 200 Hz (no boom); then
    the small flame lapping and fluttering (noise through a moving lowpass) with its crackle (burst sap ticks)."""
    L = 1.3
    n = samples(L)
    fc = env([(0, 300), (0.05, 2400), (0.18, 1400), (0.7, 800), (L, 700)], L, "exp")
    w = dsp.sweep_filter(synth.noise(L, rng, "pink"), "lp", fc, q=0.7)
    w = hp(w * env([(0, 0), (0.035, 1), (0.1, 0.55), (0.35, 0.2), (0.7, 0.05), (L, 0)], L)[:n], 200, 4)
    fl = blaze(rng, L, env([(0, 0), (0.06, 0.9), (0.3, 0.45), (L, 0.3)], L), (500, 1800)) * flutter(rng, L)
    cr = synth.crackle(L, env([(0, 0), (0.05, 160), (0.4, 50), (L, 20)], L), rng, size=(0.0002, 0.0015), hi=1500)
    return mix(unit(w) * 0.7, unit(hp(fl, 250, 2)) * 0.3, unit(cr) * 0.25)


@recipe("crew-lamp-broken", "smash", "struck",
        "A thrown lantern smashing, modelled: the globe's fracture and shards, the tin frame rolling, a soft whump",
        """Modelled from what breaks: the tin frame and font struck (a thin tin body's modes from about 500 Hz) with a
        dent's buckle; the globe's fracture (a crack of every band at once), its ring for an instant and forty-odd
        slivers ringing high and short, most at once and a few later, some bouncing; glass grains grinding as the shards
        settle. The frame bounces and rolls a beat on its cage, slowing, a wire ticking after each knock. Then the oil
        catches: noise through a lowpass that opens as the flame runs over the pool and closes as it settles, swelling
        softly (no blast front, nothing under 200 Hz), then a small flame lapping with its crackle. 1.7 s, outdoors.""",
        takes=3, lufs=-18)
def smash_struck(rng, k):
    return smash(rng, k, frame_model, globe_model, shards_model, clatter_model, whump_model)


# ---- crew-lamp-broken.burn: the oil burning where it lies ---------------------------------------------------------------

BURN_L = 10.0
CRACKLE = S("misc_33", "stones_01", "stones_02", "stones_03") + R("clothBelt2")   # real cracks and crunches
HISS = "sfx_100_v2:loop_water_03"           # a real steam hiss off a locomotive (sizzle_rec cuts it into grains)


@functools.lru_cache(maxsize=1)
def _ticks():
    """Every sharp contact in the packs' real cracks and crunches, cut to its first 5 ms: a fleck of a small fire's
    crackle (soot and drops of oil bursting)."""
    out = []
    for key in CRACKLE:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-36, gap=0.004, prom=4):
            out.append(unit(hp(ck.cut(x, s, s + samples(0.005), 0.0003, 0.002), 1200, 2)))
    return out


def crackle_rec(rng, length, rate, semis=(-1.0, 4.0)):
    """A small fire's crackle from real grains, `rate` a second (a constant or a curve), most faint and a few sharp."""
    g = _ticks()
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.02)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        amp = min(1.0, 0.06 * rng.pareto(1.6) + 0.04)
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)), 20 * np.log10(amp))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return lp(b.x, 9000, 2)


def spit_rec(rng):
    """The oil spitting: a short burst of the real hiss's grains (a bead of oil boiling off in the flame), sometimes with
    a pop in it (a real wood crack's grain, pitched up)."""
    L = rng.uniform(0.12, 0.35)
    y = unit(sizzle_rec(rng, L, env([(0, 1500), (L * 0.3, 700), (L, 0)], L)))
    if rng.random() < 0.5:
        y = mix(y, unit(hp(splinter(rng, 0.06, 20, (3, 7)), 1200, 2)) * 0.8)
    return y


def flame_bed(rng, length, swell):
    """The flames from a real recording: the packs' mass of moving water slowed and darkened into a small fire's rush,
    nothing under 220 Hz (it's a pool of oil, not a car), fluttering at 5-13 Hz and swelling with `swell`."""
    return hp(roar_rec(rng, length, -7, 1500), 220, 2) * flutter(rng, length, 0.45) * (0.55 + 0.45 * swell)


@recipe("crew-lamp-broken", "burn", "crackle",
        "A broken lantern's oil burning on the ground: small flames fluttering, a crackle, the odd spit of oil",
        """From real recordings: the flames are the packs' moving water slowed and darkened into a small fire's rush,
        kept above 220 Hz and fluttering as small flames do in the open, swelling three times a cycle as the pool
        flares; the crackle is grains cut from real cracks and stone crunches, most faint and a few sharp, quickening
        when it flares, now and then a fatter pop; and the oil spits (a real steam hiss cut into tiny grains, in short
        bursts). Small and close, outdoors. A 10 s loop, exactly one cycle.""",
        sources=[FLAME, HISS] + CRACKLE + CRACKS_WOOD, loop=True, takes=1, lufs=-22, seconds=BURN_L)
def burn_crackle(rng, k):
    T = BURN_L
    BL = T + BED_LEAD + SEAM + 0.3
    sw = breaths(rng, T, 3, (1.2, 2.2), (0.3, 0.7))
    bedx = W.space(flame_bed(rng, BL, breathing(sw, T, BL, BED_LEAD)), rng, "night", wet=0.06)
    E = T + 0.2
    e = breathing(sw, T, E)
    ev = Bus(E + 0.5)
    ev.at(0, crackle_rec(rng, E, 25 + 90 * e), 0)
    ev.at(0, hp(splinter(rng, E, 0.4 + 1.0 * e, (2, 6)), 1200, 2), -12)
    for t in W.poisson(T, 0.45, rng):
        ev.at(t, spit_rec(rng), rng.uniform(-14, -8))
    return loop_of(W.space(ev.x, rng, "night", wet=0.06), dsp.gain(bedx, -4), T)


def pop_model(rng):
    """A drop of oil bursting in the flame: a tick of noise rung through a tiny shell."""
    return unit(synth.click(rng.uniform(1500, 3500), q=5, length=0.025, rng=rng))


def spit_model(rng):
    """The oil spitting, modelled: a short burst of tiny steam bursts (douse.sizzle), fast and dying."""
    L = rng.uniform(0.12, 0.35)
    n = samples(L)
    return unit(sizzle(n, rng, env([(0, 1500), (L * 0.3, 700), (L, 0)], L)))


@recipe("crew-lamp-broken", "burn", "flame",
        "A broken lantern's oil burning on the ground, modelled: small flames lapping and fluttering, crackle, spits",
        """Modelled: the flames are noise lapping through a soft band (220-800 Hz, nothing under it), fluttering at
        5-13 Hz and swelling three times a cycle as the pool flares, with a brighter breath at the flames' tips riding
        the flutter; burst-sap crackle, most faint and a few sharp, quickening as it flares; now and then a drop of oil
        popping; and short spits of the oil boiling off (tiny steam bursts). Small and close, outdoors. A 10 s loop,
        built periodic.""", loop=True, takes=1, lufs=-22, seconds=BURN_L)
def burn_flame(rng, k):
    T = BURN_L
    n = samples(T)
    b = breathing(breaths(rng, T, 3, (1.2, 2.2), (0.3, 0.7)), T, T)
    flut = np.clip(1 + 0.45 * W.norm(W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((f - 9) / 3.5) ** 2))), 0.1, 2)
    roar = W.cfilter(W.pnoise(n, rng), lambda f: (f / 220) ** 2 / (1 + (f / 220) ** 2) / (1 + (f / 800) ** 2))
    tips = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(2500)) / 0.9) ** 2))
    cr = synth.crackle(T, 25 + 90 * b, rng, size=(0.0002, 0.0012), hi=1800)
    pops = W.place(n, [(t, pop_model(rng), rng.uniform(0.2, 0.6)) for t in W.poisson(T, 0.9, rng)])
    spits = W.place(n, [(t, spit_model(rng), rng.uniform(0.2, 0.5)) for t in W.poisson(T, 0.35, rng)])
    y = (W.norm(roar * flut * (0.55 + 0.45 * b)) * 0.22 + W.norm(tips * flut ** 2 * (0.4 + 0.6 * b)) * 0.04
         + W.norm(cr) * 0.6 + pops * 0.35 + spits * 0.45)
    return W.seamless(W.croom(y, "night", 0.04, rng))


# ---- cs-hounds-doused: a hound caught in the spray ----------------------------------------------------------------------

DOUSE_L = 1.4


def yip(rng, top, L, fire=0.4):
    """One cry of the hound's kept yelp ('kiyi', beasts.yelp): its throat scooping up to `top` Hz and falling, 'ee'
    opening to 'ah' and closing to 'oo', a little fry in it and the embers crackling in the breath."""
    return throat(rng, [(0, top * 0.6), (L * 0.22, top), (L, top * 0.5)], L, [(0, "i"), (L * 0.3, "a"), (L, "u")],
                  [(0, 0), (0.012, 1), (L * 0.5, 0.75), (L, 0)], rasp=0.2, fry_rate=(40, 60), sub=0.15, hiss=0.12,
                  fire=fire, tract=0.62, jitter=0.008)


def flash_rec(rng, L, spit=1.0):
    """Water flashing to steam off the ember-hot hide, from a real steam hiss: a slice of it with its rumble taken off,
    snapping on as the spray hits and dying away over `L` s, and the same hiss cut into tiny grains for its spitting
    (`spit` how much)."""
    h = ck.get(HISS)
    n = samples(L)
    a = int(rng.integers(0, len(h) - n - 1))
    x = dsp.shaped(unit(hp(h[a:a + n], 1500, 2)), [(0, 0), (0.006, 1), (0.08, 0.7), (0.3, 0.3), (L, 0)])
    sp = sizzle_rec(rng, L, env([(0, 3000), (0.1, 1200), (0.4, 200), (L, 0)], L))
    return mix(unit(x), unit(sp) * 0.8 * spit)


def quench(rng, L):
    """The embers in the cracks of its flanks put out: their crackle snapping fast as the water hits and dying, and a
    breath of ash and steam blown off the hide."""
    cr = embers(rng, L, env([(0, 600), (0.25, 150), (L, 0)], L), hi=1800)
    return mix(cr, ash(rng, 0.3, 600, 5000) * 0.5)


def doused(rng, k, flash, voice, flash_db, voice_db):
    """The spray catches it: the steam flashes off its hide at once and the embers die; it cries out a beat after
    (`voice(rng, k)`, startled), backing off."""
    b = Bus(DOUSE_L + 0.4)
    b.at(0, flash(rng, k), flash_db)
    b.at(0.005, quench(rng, 0.8), flash_db - 10)
    b.at(rng.uniform(0.04, 0.07), voice(rng, k), voice_db)
    return dsp.fade(dsp.fit(b.x, samples(DOUSE_L)), 0.0, 0.15)


def steam_flash(rng, k):
    return flash_rec(rng, rng.uniform(0.9, 1.1))


def steam_voice(rng, k):
    """Three ways it cries out under the steam, all the kept yelp: a sharp yelp and a smaller one as it turns away; one
    long hurt yelp falling into a whimper; a quick yi-yip and a huff as it backs off."""
    b = Bus(1.3)
    if k % 3 == 0:
        L = rng.uniform(0.18, 0.22)
        b.at(0, yip(rng, rng.uniform(900, 980), L), 0)
        b.at(L + rng.uniform(0.12, 0.18), lp(yip(rng, rng.uniform(720, 790), 0.16), 3500), -7)
    elif k % 3 == 1:
        L = rng.uniform(0.3, 0.36)
        b.at(0, yip(rng, rng.uniform(850, 900), L), 0)
        b.at(L + rng.uniform(0.1, 0.16), lp(yip(rng, rng.uniform(600, 660), 0.3, 0.3), 3000), -11)
    else:
        b.at(0, yip(rng, rng.uniform(920, 1000), 0.12), 0)
        b.at(rng.uniform(0.15, 0.18), yip(rng, rng.uniform(840, 900), 0.13), -2)
        b.at(rng.uniform(0.36, 0.42), huff(rng, 0.16, rng.uniform(120, 140)), -9)
    return b.x


@recipe("cs-hounds-doused", "doused", "steam",
        "A hound caught in the extinguisher's spray: steam flashing off its hot hide with a spitting hiss, and its yelp",
        """Led by the steam: the water flashes off its ember-hot hide at once (a real locomotive's steam hiss with its
        rumble taken off, snapping on and dying away in about a second, the same hiss cut into tiny grains for its
        spitting), the embers in the cracks of its flanks snapping and going out, a breath of ash and steam blown off
        it. A beat later it cries out in the hound's kept yelp (its own throat, embers crackling in the breath). Takes:
        a sharp yelp and a smaller one as it turns away; a long hurt yelp falling into a whimper; a quick yi-yip and a
        huff as it backs off. No spray: that's the extinguisher's own.""",
        sources=[HISS], takes=3, lufs=-18)
def doused_steam(rng, k):
    return doused(rng, k, steam_flash, steam_voice, -3, 0)


def cinder_yelp(rng, top, L):
    """A yelp from the kept snarl's burnt-out throat (beasts.cinder_voice): the same scoop and fall as the kept yelp,
    but its rattle made of ember crackle and dry breath, with the yelp's own throat under it for its pitch; the fire's
    low roar under a snarl taken out, since a yelp is thin."""
    pts = [(0, top * 0.55), (L * 0.25, top), (L, top * 0.5)]
    v = cinder_voice(rng, pts, L, [(0, "i"), (L * 0.3, "a"), (L, "u")], [(0, 0), (0.015, 1), (L * 0.6, 0.7), (L, 0)],
                     fry_rate=(40, 60))
    return hp(mix(unit(v), unit(yip(rng, top, L, 0.6)) * 0.55), 300, 2)


def hurt_snarl(rng, L, f0=150.0):
    """Backing off snarling: the kept snarl's burnt-out throat, a rising warning that sinks to a growl, turned away."""
    v = cinder_voice(rng, [(0, f0), (L * 0.3, f0 * 1.15), (L, f0 * 0.7)], L, [(0, "a"), (L * 0.5, "e"), (L, "u")],
                     [(0, 0), (0.04, 1), (L * 0.7, 0.7), (L, 0)])
    return hp(lp(unit(v), 3500), 120, 2)


def cinder_flash(rng, k):
    return flash_rec(rng, rng.uniform(0.5, 0.65), 1.4)


def cinder_cry(rng, k):
    """Three ways it cries out in its burnt-out voice: a yelp, then a snarl as it backs off; two yelps, the second
    breaking into a growl; a yelp cut off into a hiss through its teeth and a low growl."""
    b = Bus(1.3)
    if k % 3 == 0:
        L = rng.uniform(0.2, 0.24)
        b.at(0, cinder_yelp(rng, rng.uniform(880, 960), L), 0)
        b.at(L + rng.uniform(0.08, 0.12), hurt_snarl(rng, rng.uniform(0.45, 0.55)), -5)
    elif k % 3 == 1:
        b.at(0, cinder_yelp(rng, rng.uniform(900, 980), 0.15), 0)
        t = rng.uniform(0.24, 0.28)
        b.at(t, cinder_yelp(rng, rng.uniform(780, 840), 0.14), -2)
        b.at(t + 0.11, hurt_snarl(rng, rng.uniform(0.4, 0.5), 135), -6)
    else:
        L = rng.uniform(0.16, 0.2)
        b.at(0, cinder_yelp(rng, rng.uniform(860, 940), L), 0)
        H = 0.22
        teeth = bp(synth.noise(H, rng), 2400, 6500) * env([(0, 0), (0.02, 1), (H * 0.5, 0.5), (H, 0)], H)
        b.at(L - 0.02, unit(teeth), -12)
        b.at(L + 0.12, hurt_snarl(rng, rng.uniform(0.55, 0.65), 110), -6)
    return b.x


@recipe("cs-hounds-doused", "doused", "cinder",
        "A hound doused: a short spitting hiss off its hide, a yelp in its burnt-out voice, a snarl as it backs off",
        """Led by its voice: the yelp is the kept yelp's scoop and fall made in the kept snarl's burnt-out throat (its
        rattle ember crackle and dry breath, the yelp's own throat under it for pitch), so it's hurt and startled but
        still the thing that snarls; then it snarls as it backs off. The steam is shorter and spits more: a real steam
        hiss with its rumble off, mostly cut into tiny grains, the embers in its flanks snapping out and a breath of ash.
        Takes: a yelp then a snarl; two yelps, the second breaking into a growl; a yelp cut off into a hiss through its
        teeth and a low growl. No spray: that's the extinguisher's own.""",
        sources=[HISS], takes=3, lufs=-18)
def doused_cinder(rng, k):
    return doused(rng, k, cinder_flash, cinder_cry, -6, 0)
