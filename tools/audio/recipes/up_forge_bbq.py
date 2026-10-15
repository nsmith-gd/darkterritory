"""U1's upgrades heard: the town blacksmith's forge and the BBQ deck (queue #505, note 775; U1's #967, notes 678, 697, 698;
the checklist's up-forge and up-bbq rows, U1's requests of 13 Oct). Built ahead of #967 for the director to judge; hooked
when it lands.

What each is decides the build:
- The forge (up-forge): in the walled fortress towns the crew buy their upgrades from a blacksmith working a lit forge
  beside the engine. It's heard from the cab across the town's yard, and it's how the crew find it by ear, so it has to
  carry: the hearth's low roar breathing as the draught gusts, the coals ticking and settling (a loop, warm, held to
  100 Hz-6 kHz); now and then the hammer on the anvil, the hard bright ring of hammered steel and the hammer's bounce off
  the face (one to three blows a take, its ring in the 1-6 kHz that carries furthest); the bellows worked, the leather's
  long wheeze and the fire roaring up with it; hot iron plunged in the quench barrel, a sharp hiss and a boil of water.
  The yard's walls throw a blow back once, a few tens of metres off.
- The BBQ deck (up-bbq): a railed deck on the engine's front, two Muskoka chairs, a cooler of beers and a charcoal grill
  cooking sausages. The end of the world, and the BBQ Dads having a good time anyway: warm, homely, a little comic, never
  a creature. Outdoors at night on the front of the engine (the smokebox door and the rail throw it back, then the open
  night): the grill lit, a whump of lighter fluid catching the charcoal; the grill burning, sausages sizzling, charcoal
  crackling and tinkling, fat spitting (it draws the Cinder Hounds, so it's a warning as well as a comfort: it carries in
  the mids, a sizzle and not a roar); a sausage turned, the tongs' clack on the grate and a spit of fat; the cooler's lid
  lifted, ice shifting and clinking; a can cracked open, its tab, its fizz and a swig; two cans clinked, aluminium and
  full, so dull, never glass; a wooden Muskoka chair creaking as someone sits down or gets up.

Each cue has two candidates. The first is kitbashed from the packs' real recordings, bent: a mass of moving water slowed
and darkened into a fire's roar, real wood cracking cut into a fire's crackle and real glass knocks into clinker's tinkle,
real stones for coals settling and ice shifting; the packs' hammer on metal and their heavy bell metal pitched up into an
anvil's ring; a real gasp drawn out into the bellows' breath and a real hinge's squeal held out into its leather's wheeze;
a real locomotive's steam hiss for the quench and cut into tiny grains for the sizzle of fat, water sloshing in a real drum
for the barrel's boil and a can's beer; a real whoomph of air for the lighter fluid catching; the packs' bottle fizz for
the can (the director's note of 2 Oct: a fizz only where a fizz belongs, and here it does), a tin knocked for the cans,
real creaks and boards for the chair. The second is modelled: struck modes for the anvil, the tongs and grate, the cans and
the cooler; noise lapping through a moving lowpass for the fire; turbulent jet noise for the steam and the bellows' nozzle;
bubbles' Minnaert rings for the boil, the fizz and the swig; stick-slip for the leather and the chair.

Loops are built on their exact cycle (events folded round onto the head, beds running on past it for build.py's seam), so
the seam gives the cycle back. Every take keeps its weight out of the squish under 150 Hz: impacts are high-passed and
their weight comes from the low mids, nothing is a sub.
"""

import functools

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import town
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.beasts import unit
from recipes.crew_items import hit_of
from recipes.douse import sizzle as flash
from recipes.g1_ground import splinter, splinters
from recipes.heard_foley import gulp
from recipes.house_folk import LEAD, SEAM, bed, chair, drawn, held_cry, loop_of, pings, wet_grain
from recipes.upkeep import cook

SR = dsp.SR
FORGE, BBQ = "up-forge", "up-bbq"

# ---- Sources -------------------------------------------------------------------------------------------------------------

ROAR = "sfx_100_v2:loop_water_01"          # a mass of water moving: slowed most of an octave and darkened, a fire's roar
STEAM = "sfx_100_v2:loop_water_03"         # a real steam hiss off a locomotive, with its own puffs
HISS = "sfx_100_v2:misc_22"                # a steam vehicle's hiss: the quench's first flash
DRUM = "sfx_100_v2:loop_machine_02"        # water sloshing and gurgling in a drum: its loudest splash 5.2-5.7 s
TRICKLE = "sfx_100_v2:loop_water_02"       # water pouring and trickling
WHOOMPH = "sfx_100_v2:misc_32"             # a soft low whoomph of moving air (its head, before the knock at its end)
GASP = "sfx_100_v2:misc_05"                # a real gasp: air forced through a mouth
HAMMER = "sfx_100_v2:misc_19"              # the packs' hammer on metal: a hard, bright steel tak
BELL = K("impactBell_heavy")               # heavy bell metal struck: a long ring, 380 Hz-1.6 kHz
METAL_L, METAL_H = K("impactMetal_light"), K("impactMetal_heavy")
PLATE_L = K("impactPlate_light")           # thin plate: the grill's grate and its bowl
TICKS = R("metalClick") + S("misc_01")     # small real metal ticks: iron taking the heat
STONES = S("stones_01", "stones_02", "stones_03")              # real stones knocked and crunched
CRACKS = S("misc_34", "misc_35")           # real wood cracking and breaking (g1_ground.splinters cuts them): crackle
LEATHER = R("handleSmallLeather", "handleSmallLeather2")       # real leather handled: the bellows' folds
FLAP = R("dropLeather")[0]                 # a leather drop: a heavy flap of air (the bellows' valve slapping shut)
CREAKS = R("creak1", "creak2", "creak3")   # real wooden creaks (creak2's squeal sings near 1.1 kHz)
WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")   # real wet squelches (house_folk.wet_grain cuts them)
SCRATCH = R("drawKnife1", "drawKnife2")    # a blade drawn fast across a surface: a match struck, tongs over the bars
CLINKS = S("metal_02", "metal_06")         # small real metal clinks: the tongs' arms, a can's rim
LATCH = R("metalLatch")[0]                 # a real latch: the cooler's catch, a lighter's trigger
GLASS = S("glass_01", "glass_04", "glass_06") + K("impactGlass_light")   # real glass knocked: ice, charcoal's tinkle
FIZZ = S("air_02", "air_03")               # a bottle opened, its fizz (only where a fizz belongs: the can)
TIN = K("impactTin_medium")                # a tin knocked: a can of beer, full
SOFT = K("impactSoft_medium")              # real soft blows: a body settling into a chair
WOOD_M, PLANK = K("impactWood_medium"), K("impactPlank_medium")
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
BOOK = R("bookOpen", "bookClose")          # a book opened and shut: a stiff lid unsticking, a lid dropped shut

# ---- Bands, lengths and bodies -------------------------------------------------------------------------------------------

HEARTH = (100, 6000)        # the hearth: warm, from the cab across the yard
RING = (1000, 6000)         # the anvil: its ring is what carries
HEARTH_L = 10.0
GRILL_L = 8.0

ANVIL = [1.0, 1.47, 2.18, 2.94, 3.71, 4.6]      # a London-pattern anvil's modes over its first: face and horn
HOOD = [150, 240, 380, 610]                     # the forge's brick hood and chimney: broad, never a tone
BARREL = [190, 340, 560, 820]                   # a quench barrel half full of water: its hollow, wooden and dull
CAN = [1.0, 1.52, 2.11, 2.87, 3.64, 4.48]       # a beer can's wall, full: close modes, damped by what's in it
TONGS = [1.0, 2.756, 5.404, 8.933]              # a pair of steel tongs' arm, a thin bar (world_kit.BAR)
COOLER = [210, 330, 470, 690, 980, 1350]        # a plastic cooler's box and lid: hollow, dull
SLATS = [190, 340, 560, 880, 1300]              # a Muskoka chair's wide slats and its joints: a big, dry wooden body


# ---- Spaces --------------------------------------------------------------------------------------------------------------

def yard(x, rng, wet=0.16, tail=None):
    """Across a walled town's yard at night: a little air, and the yard's walls throwing a blow back twice a few tens of
    metres off (47 and 94 ms), so the forge carries and comes back once. `tail` (s) cuts a one-shot's reverb short so one
    fired again doesn't pile up; a loop keeps it all, to fold round onto its head."""
    y = ck.space(x, 0.9, wet, 4500, ((0.047, 0.32), (0.094, 0.2), (0.15, 0.1)), seed=int(rng.integers(1 << 16)))
    if tail is None:
        return y
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def deck(x, rng, wet=0.08, tail=0.35):
    """Out on the engine's front deck at night: the smokebox door and the deck's rail a hand's breadth off throwing it back
    (two quick slaps), then the open night, its tail cut short (`tail` s; None for a loop's events, whose tail folds
    round onto the head) so a one-shot fired again doesn't pile up."""
    y = x.copy()
    for dt, g in ((0.0037, 0.25), (0.0093, 0.14)):
        k = samples(dt)
        y[k:] += x[:-k] * g
    y = dsp.room(y, "night", wet=wet, rng=rng)
    if tail is None:
        return y
    L = len(x) / SR
    return dsp.fade(y[:samples(L + tail)], 0.0, tail)


# ---- Fire ----------------------------------------------------------------------------------------------------------------

def breaths(rng, T, count, width=(1.6, 2.6), depth=(0.55, 1.0)):
    """The fire breathing: `count` slow swells of the draught round a cycle of T s, at uneven times:
    [(top, width, depth)]."""
    return [((i + rng.uniform(0.15, 0.85)) * T / count, rng.uniform(*width), rng.uniform(*depth)) for i in range(count)]


def breathing(swells, T, length, lead=0.0):
    """Those swells as a curve (0 resting, up to 1 at the top of a breath) over `length` s whose 0 is `lead` s before the
    cycle's: read round the cycle, so it's the same either side of the seam. Each rises quickly and sinks slowly, as a
    draught gusts."""
    t = (np.arange(samples(length)) / SR - lead) % T
    c = np.zeros(len(t), np.float32)
    for top, w, d in swells:
        x = (t - top + T / 2) % T - T / 2
        s = np.where(x < 0, np.clip(1 + x / (0.3 * w), 0, 1), np.clip(1 - x / (0.7 * w), 0, 1))
        c = np.maximum(c, (d * (0.5 - 0.5 * np.cos(np.pi * s))).astype(np.float32))
    return c


def roar_rec(rng, length, semis=-9.0, top=1300.0):
    """A fire's roar from a real recording: the packs' mass of moving water laid end to end, slowed `semis` and darkened
    till its rush roars, held to the low mids where a fire's roar sits."""
    need = length * 2 ** (semis / 12) + 0.3
    y = dsp.vari(bed(rng, ROAR, need), semis)
    return unit(lp(hp(dsp.fit(y, samples(length)), 90, 2), top, 2))


def blaze(rng, length, level, top=(450.0, 1900.0)):
    """A fire's roar, modelled: pink noise lapping as the flames tear (a quick 5-12 Hz flutter over a slow sway), its
    lowpass opening and its level rising as it flares (`level` 0..1, a constant or a curve)."""
    n = samples(length)
    lv = np.clip(synth.curve(level, n), 0, 1)
    x = hp(synth.noise(length, rng, "pink"), 90, 2)
    y = dsp.sweep_filter(x, "lp", top[0] + (top[1] - top[0]) * lv, q=0.7)
    tear = np.clip(1 + 0.3 * W.norm(bp(rng.standard_normal(n).astype(np.float32), 5, 12)), 0.3, 1.5)
    sway = town.lapping(rng, n, 1.5, 0.3)
    return unit(y * tear * sway * (0.15 + 0.85 * lv) ** 1.5)


def pop_rec(rng, semis=(-3.0, 3.0)):
    """One spark of sap or charcoal popping: a grain of the packs' real wood cracking."""
    g = splinters()
    return unit(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)))


def tinkle_rec(rng, length, rate, semis=(-3.0, 3.0)):
    """Lump charcoal and clinker tinkling as they burn and shift: small real glass knocks, pitched and choked to ticks,
    `rate` a second."""
    parts = [(float(t), ck.choke(dsp.vari(hit_of(GLASS[int(rng.integers(len(GLASS)))], int(rng.integers(2)), 0.08, -18),
                                          rng.uniform(*semis)), 0.002, 0.008), 20 * np.log10(rng.uniform(0.2, 1) ** 1.5))
             for t in W.poisson(length, rate, rng)]
    return unit(hp(ck.place(parts, length + 0.1), 1500)) if parts else np.zeros(samples(length + 0.1), np.float32)


def settle_rec(rng, take):
    """Coals settling: a slice of real stones crunching, dropped and darkened (a lump turning over in the fire), and the
    clinker under it tinkling."""
    x = ck.get(STONES[take % 3])
    h = ck.hits(x, floor_db=-16, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    L = rng.uniform(0.18, 0.3)
    c = ck.cut(x, a - samples(0.003), a + samples(L), 0.002, L / 2)
    c = unit(lp(hp(dsp.vari(c, -rng.uniform(5, 8)), 150), 3500))
    return ck.place([(0, c, 0), (0.01, tinkle_rec(rng, L, 14, (-4, 0)), -12)])


def settle_model(rng):
    """Coals settling, modelled: lumps knocking as they turn over (hard grains rung low and loose), the hollow of the
    bed under them, a clinker's tink or two."""
    L = rng.uniform(0.2, 0.32)
    lumps = lp(ck.grains(rng, int(rng.integers(10, 20)), L, 500, 3000, q=(3, 7), length=(0.01, 0.03), shape=0.6), 3500)
    bed_ = ck.hollow(rng, [rng.uniform(170, 210), rng.uniform(280, 330), rng.uniform(440, 520)], 0.15, q=4, hit=0.006)
    tinks = [(rng.uniform(0, L), synth.click(rng.uniform(2600, 4200), q=25, length=0.05, rng=rng), rng.uniform(-18, -10))
             for _ in range(int(rng.integers(1, 3)))]
    return ck.place([(0, unit(lumps), 0), (0, bed_, -8)] + tinks)


def crackle_model(rng, length, rate, big):
    """A fire's crackle, modelled: sharp ticks of burst sap (many quiet, a few loud) and, `big` a second, fatter pops."""
    return mix(unit(synth.crackle(length, rate, rng, size=(0.0003, 0.002), hi=1500)),
               unit(synth.crackle(length, big, rng, size=(0.003, 0.01), hi=300)) * 0.8)


# ---- Steam and fat -------------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=1)
def _hiss():
    """The packs' real steam hiss with its rumble taken off: what a sizzle's grains are cut from."""
    return unit(hp(ck.get(STEAM), 1500, 2))


def sizzle_rec(rng, length, rate, size=(0.0008, 0.004), loop=False):
    """Fat frying, from a real hiss: tiny grains of the packs' steam hiss (1-4 ms, each snapping on and dying at once, as
    a droplet bursts in hot fat) strewn `rate` a second (a constant or a curve), a few loud and many quiet; `loop` folds
    the last ones round onto the head, so a loop's fry has no seam."""
    h = _hiss()
    n = samples(length)
    rc = synth.curve(rate, n)
    out = np.zeros(n + samples(0.01), np.float32)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 1.0))
        a = samples(t)
        if a >= n:
            break
        m = samples(rng.uniform(*size)) + 2
        o = int(rng.integers(0, len(h) - m))
        g = h[o:o + m] * np.exp(-np.linspace(0, 5, m)).astype(np.float32) * min(1.0, 0.05 * rng.pareto(1.8) + 0.05)
        if loop:
            np.add.at(out, (a + np.arange(m)) % n, g)
        else:
            out[a:a + m] += g
    return out[:n]


# ---- up-forge: the hearth ------------------------------------------------------------------------------------------------

@recipe(FORGE, "hearth", "coals",
        "The blacksmith's hearth from across the yard: its low roar breathing, coals ticking and settling",
        """The forge's fire heard from the cab across the yard, from real recordings: the roar is the packs' mass of
        moving water laid end to end, slowed most of an octave and darkened into the low mids, with a second, brighter
        pass of it flaring as the draught comes up; the fire breathes three times a cycle, a quick swell and a slow
        sink, and the coals' crackle quickens with each breath (real wood cracking cut into grains and pitched up), a
        fatter pop now and then; lumps of coke settling (real stones crunching, dropped and darkened) with the clinker
        tinkling under them (real glass knocks choked to ticks), the hood's iron ticking as it takes the heat (the
        packs' metal ticks), and air hissing thin through the coals (the real steam hiss, high and quiet). The yard's
        walls throw it back once. A 10 s loop, held to 100 Hz-6 kHz.""",
        sources=[ROAR, STEAM] + CRACKS + STONES + GLASS + TICKS, takes=1, loop=True, band=HEARTH, lufs=-25,
        seconds=HEARTH_L)
def hearth_coals(rng, k):
    T = HEARTH_L
    BL = T + LEAD + SEAM + 0.3
    sw = breaths(rng, T, 3)
    b = breathing(sw, T, BL, LEAD)
    n = samples(BL)
    roar = roar_rec(rng, BL) * (0.4 + 0.6 * b) * town.lapping(rng, n, 4, 0.3)
    flare = unit(hp(roar_rec(rng, BL, -6, 2600), 400)) * b ** 1.5
    draught = unit(bp(bed(rng, STEAM, BL), 1800, 5500)) * (0.15 + 0.85 * b)
    bedx = yard(mix(unit(roar), flare * 0.55, draught * 0.04), rng)
    E = T + 0.2
    e = breathing(sw, T, E)
    crackle = unit(hp(splinter(rng, E, 8 + 30 * e, (3, 9)), 1500))
    pops = splinter(rng, E, 0.6 + 1.6 * e, (-4, 0))
    settle = ck.place([(float(t), settle_rec(rng, i), rng.uniform(-3, 0))
                       for i, t in enumerate((np.arange(3) + rng.uniform(0.1, 0.9, 3)) * T / 3)], E)
    iron = town.ticks(rng, E, TICKS, 0.35, -5, (-24, -14))
    ev = mix(crackle * 0.12, pops * 0.22, unit(settle) * 0.35, tinkle_rec(rng, E, 0.8) * 0.05, iron * 0.4)
    return loop_of(yard(ev, rng), bedx, T, band=HEARTH)


@recipe(FORGE, "hearth", "draught",
        "The blacksmith's hearth from across the yard, modelled: a breathing roar, coals ticking and settling",
        """Modelled: the fire's roar is pink noise lapping as the flames tear, its lowpass opening and its level rising
        each time the draught comes up (three breaths a cycle, a quick swell and a slow sink), rung a little through the
        forge's brick hood (broad low modes, never a tone); the coals' crackle (synthesised burst sap, many quiet and a
        few loud, quickening with each breath) and fatter pops; lumps settling (hard grains knocking low and loose over
        the bed's hollow, a clinker's tink); the hood's iron pinging as it heats; air hissing thin through the coals.
        The yard's walls throw it back once. A 10 s loop, held to 100 Hz-6 kHz.""",
        takes=1, loop=True, band=HEARTH, lufs=-25, seconds=HEARTH_L)
def hearth_draught(rng, k):
    T = HEARTH_L
    BL = T + LEAD + SEAM + 0.3
    sw = breaths(rng, T, 3)
    b = breathing(sw, T, BL, LEAD)
    roar = blaze(rng, BL, 0.35 + 0.65 * b)
    hood = unit(dsp.resonate(roar, HOOD, q=3, gains=[1, 0.8, 0.6, 0.4]))
    air = unit(bp(synth.noise(BL, rng), 1800, 5500)) * (0.1 + 0.9 * b)
    bedx = yard(mix(roar, hood * 0.15, air * 0.03), rng)
    E = T + 0.2
    e = breathing(sw, T, E)
    crackle = crackle_model(rng, E, 10 + 40 * e, 0.6 + 1.6 * e)
    settle = ck.place([(float(t), settle_model(rng), rng.uniform(-3, 0))
                       for t in (np.arange(3) + rng.uniform(0.1, 0.9, 3)) * T / 3], E)
    ev = mix(unit(crackle) * 0.2, unit(settle) * 0.35, pings(rng, E, 0.35) * 0.4)
    return loop_of(yard(ev, rng), bedx, T, band=HEARTH)


# ---- up-forge: the anvil -------------------------------------------------------------------------------------------------

def blows(rng, k):
    """A take's blows: one, two, three, then two (a smith's working rhythm, a blow every 0.55-0.7 s, each a little
    lighter than the last as the iron cools), as [(time, force)]."""
    t, out = 0.0, []
    for i in range((1, 2, 3, 2)[k % 4]):
        out.append((t, rng.uniform(0.85, 1.0) * (1 - 0.08 * i)))
        t += rng.uniform(0.55, 0.7)
    return out


@functools.lru_cache(maxsize=8)
def _bell(i, semis):
    x = ck.align(ck.get(BELL[(0, 1, 3)[i % 3]]))
    return unit(hp(dsp.shift(x, semis), 1000, 4))


def anvil_rec(take, semis):
    """The anvil's ring from real bell metal: the packs' heavy bell struck, pitched up an octave and more without
    shortening it (so its hum becomes a 150 kg steel block's long, bright ring from 1 kHz up), its strike's thud taken
    off. One bell to a take: the same anvil rings the same at every blow."""
    return _bell(take % 3, round(semis * 2) / 2).copy()


def blow_rec(rng, take, i, force):
    """One blow on hot iron, from real recordings: the packs' hammer on metal (its hard tak, its body under 500 Hz taken
    off), a heavy metal hit pitched down and choked under it (the hot iron giving: a dull weight in the mids), the anvil
    ringing under both, and the hammer's own steel ringing bright over it (a light metal hit)."""
    tak = hp(ck.choke(hit_of(HAMMER, 0, 0.3), 0.006, 0.03), 500, 2)
    thud = hp(ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[i % 5]), -rng.uniform(2, 4)))), 0.012, 0.03), 400, 2)
    ring = anvil_rec(take, 15.5)
    steel = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_L[(take + 2) % 5]), rng.uniform(-0.5, 0.5)))), 0.04, 0.3)
    return ck.place([(0, unit(tak), 0), (0, thud, -12), (0.001, ring, -6 + 4 * (force - 1)), (0, steel, -8)])


def bounce_rec(rng, take, i):
    """The hammer's bounce off the bare face: a lighter, brighter ting (a light metal hit pitched up, the anvil rung
    again, less), and now and then a second, smaller one."""
    ting = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_L[(take + 2) % 5]), rng.uniform(1.5, 2.5)))), 0.03, 0.25)
    parts = [(0, ting, 0), (0, anvil_rec(take, 15.5), -9)]
    if rng.random() < 0.45:
        parts.append((rng.uniform(0.05, 0.07), ck.choke(ting, 0.01, 0.1), -9))
    return ck.place(parts)


def hammering(rng, k, anvil, blow, bounce):
    """A take: its blows on the one anvil (`anvil`, what blow and bounce strike), each with the hammer's bounce 90-140 ms
    after, heard across the yard."""
    parts = []
    for i, (t, force) in enumerate(blows(rng, k)):
        parts += [(t, blow(rng, anvil, i, force), 20 * np.log10(force)),
                  (t + rng.uniform(0.09, 0.14), bounce(rng, anvil, i), -9 + 20 * np.log10(force))]
    return yard(lp(ck.place(parts), 9000), rng, 0.14, 0.4)


@recipe(FORGE, "anvil", "ring",
        "A hammer on the anvil: a hard steel tak, the anvil's long bright ring, the hammer's bounce",
        """Each blow from real recordings: the packs' hammer on metal for the hard tak of the face meeting the iron, a
        heavy metal hit pitched down and choked under it for the hot iron giving (a dull weight in the low mids), the
        anvil ringing long and bright under both (the packs' heavy bell metal pitched up an octave and more without
        shortening it, its thud taken off), the hammer's own steel ringing over it (a light metal hit); 90-140 ms after,
        the hammer's bounce off the bare face, a lighter, brighter ting (sometimes two). One blow, two, three, then two,
        a blow every 0.55-0.7 s, lighter as the iron cools. The yard's walls throw each back once. Held to 1-6 kHz,
        where it carries.""",
        sources=[HAMMER] + BELL[:2] + BELL[3:4] + METAL_H + METAL_L, takes=4, band=RING, lufs=-21)
def anvil_ring(rng, k):
    return hammering(rng, k, k, blow_rec, bounce_rec)


ANVIL_DECAY = [0.9, 0.75, 0.6, 0.45, 0.35, 0.27, 0.2, 0.14]
ANVIL_AMP = [1.0, 0.8, 0.9, 0.7, 0.6, 0.5, 0.35, 0.25]


def anvil_modes(rng):
    """One anvil's modes for a take: a steel block from about 1 kHz (face and horn), the same at every blow (struck
    again, the same anvil rings the same: no beating between blows)."""
    f1 = 1020 * rng.uniform(0.96, 1.04)
    return [f1 * x * rng.uniform(0.995, 1.005) for x in ANVIL + [5.62, 6.71]]


def anvil_model(rng, modes, contact):
    """The anvil rung, modelled: its modes (1-7 kHz, the low ones ringing over a second) struck through a contact
    `contact` s long (bare steel on steel ~0.3 ms rings bright; through hot iron ~1 ms, only the low modes), the face
    struck a little off centre each time (each mode's share varies), faded out under its own ring."""
    am = [a * rng.uniform(0.7, 1.0) for a in ANVIL_AMP]
    y = W.modal(modes, ANVIL_DECAY, am, 2.0, rng, contact)
    return unit(dsp.fade(y, 0.0, 0.6))


def crack_model(rng, length=0.004, lo=1500):
    """Steel meeting steel: the contact's own crack, a few milliseconds of hard broadband noise."""
    return dsp.shaped(unit(hp(synth.noise(length, rng), lo, 2)), [(0, 1), (length, 0)])


def blow_model(rng, modes, i, force):
    """One blow on hot iron, modelled: the hammer's face meeting the work (a hard crack), the soft hot iron giving (a
    short, dull thunk of a thick bar, damped), the hammer head's own bright ring (a bar's modes, brief), and the anvil
    rung through the iron (duller and less: the hot iron under the hammer damps it)."""
    work = ck.choke(W.body(rng, rng.uniform(420, 480), W.BAR, decay=0.03, length=0.2, contact=0.0015), 0.015, 0.03)
    head = W.body(rng, rng.uniform(2500, 2900), W.BAR, decay=0.12, length=0.5, contact=0.0003, count=3)
    return ck.place([(0, crack_model(rng), -2), (0, unit(hp(work, 400)), -6), (0, unit(head), -6),
                     (0, anvil_model(rng, modes, 0.0009), -9)])


def bounce_model(rng, modes, i):
    """The hammer's bounce off the bare face, modelled: steel on steel, a hard 0.3 ms contact, the anvil's ring bright."""
    parts = [(0, anvil_model(rng, modes, 0.0003), -2), (0, crack_model(rng, 0.003, 2500), -8),
             (0, unit(W.body(rng, rng.uniform(2500, 2900), W.BAR, decay=0.08, length=0.4, contact=0.0002, count=3)), -8)]
    if rng.random() < 0.45:
        parts.append((rng.uniform(0.05, 0.07), anvil_model(rng, modes, 0.0004), -10))
    return ck.place(parts)


@recipe(FORGE, "anvil", "modal",
        "A hammer on the anvil, modelled: a dull blow on hot iron, the anvil's bright ring, the bounce",
        """Modelled: each blow a hard crack of steel meeting steel, the work's own thick-bar thunk as the hot iron gives
        and the hammer head's brief bright ring, the anvil under it rung through the hot iron (a soft, millisecond
        contact, so duller and damped); 90-140 ms after, the hammer's bounce off the bare face, steel on steel in a
        third of a millisecond, ringing the anvil bright (sometimes twice): a London-pattern anvil's modes from about
        1 kHz (face and horn, ratios 1 : 1.47 : 2.18 : 2.94 : 3.71 : 4.6 and two above, the lowest ringing near a
        second). One blow, two, three, then two, a blow every 0.55-0.7 s. The yard's walls throw each back once. Held to
        1-6 kHz, where it carries.""",
        takes=4, band=RING, lufs=-21)
def anvil_modal(rng, k):
    return hammering(rng, k, anvil_modes(rng), blow_model, bounce_model)


# ---- up-forge: the bellows -----------------------------------------------------------------------------------------------

def stroke(L, push):
    """A bellows' stroke over L s: the board pressed down over `push` s, held hard, let up."""
    return [(0, 0), (push, 1), (L * 0.72, 0.8), (L, 0)]


def leather_rec(rng, L, shape):
    """The bellows' leather folds rubbing as they're squeezed: real leather handled cut into grains, dropped a few
    semitones, strewn as fast as the board moves (`shape`, an envelope)."""
    n = samples(L)
    rate = 6 + 40 * synth.curve(env(shape, L), n)
    b = dsp.Bus(L + 0.3)
    t = rng.exponential(1 / rate[0])
    while t < L:
        x = ck.get(LEATHER[int(rng.integers(2))])
        h = ck.hits(x, floor_db=-24, gap=0.02)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        g = ck.cut(x, a - samples(0.002), a + samples(rng.uniform(0.04, 0.1)), 0.002, 0.03)
        b.at(t, unit(dsp.vari(g, -rng.uniform(2, 5))), 20 * np.log10(rng.uniform(0.25, 1) ** 1.5))
        t += rng.exponential(1 / rate[min(samples(t), n - 1)])
    return unit(hp(b.x, 200))


@recipe(FORGE, "bellows", "leather",
        "The bellows worked: the leather's long wheeze, the board's creak, the fire roaring up",
        """From real recordings: the handle's pivot creaking as the board goes down (a real wooden creak, dropped) and
        the leather valve slapping shut (a real leather drop, a heavy flap of air); the air forced out of the nozzle
        into the coals, a long breath (the packs' real gasp drawn out and dropped most of an octave, a big bellows'
        throat), swelling and easing over the stroke; the leather wheezing as it's squeezed (a real hinge's squeal held
        out and dropped, sagging as the stroke runs out) and its folds rubbing (real leather handled, cut into grains);
        the fire roaring up after it (the hearth's roar, slowed water darkened, swelling and dying), a whoomph as it
        catches (the packs' real whoomph of air) and the crackle flaring (real wood cracking cut into grains). A 1.5 to
        2 s stroke and the fire's after-roar, heard across the yard.""",
        sources=[GASP, ROAR, WHOOMPH, FLAP] + CREAKS + LEATHER + CRACKS, takes=3, lufs=-21)
def bellows_leather(rng, k):
    L = (1.5, 1.75, 2.0)[k % 3] * rng.uniform(0.96, 1.04)
    push = rng.uniform(0.25, 0.35) * L
    sh = stroke(L, push)
    air = dsp.shaped(unit(bp(drawn(rng, GASP, L, -rng.uniform(7, 10)), 250, 3500)), sh)
    Lw = L * 0.85
    sing = held_cry(rng, CREAKS[1], Lw, env([(0, -8), (Lw, -10.5)], Lw), 0.08)
    sing = dsp.shaped(unit(bp(sing, 400, 2600)), [(0, 0), (push, 1), (Lw * 0.6, 0.6), (Lw, 0)])
    pivot = unit(hp(dsp.vari(ck.align(ck.get(CREAKS[2])), -rng.uniform(3, 5)), 300))
    valve = unit(hp(dsp.vari(hit_of(FLAP, 0, 0.2), -rng.uniform(1, 3)), 250, 2))
    Lf = L + 0.4
    fire = dsp.shaped(roar_rec(rng, Lf, -8, 1800), [(0, 0.15), (push + 0.25, 1), (L, 0.7), (Lf, 0)])
    whoomph = unit(lp(dsp.vari(ck.cut(ck.get(WHOOMPH), samples(0.02), samples(0.16), 0.012, 0.03), -3), 1200))
    crackle = unit(hp(splinter(rng, Lf, env([(0, 6), (push + 0.3, 60), (L, 25), (Lf, 4)], Lf), (2, 8)), 1200))
    y = ck.place([(0, pivot, -10), (0.02, valve, -12), (0.03, air, 0), (0.06, sing, -12),
                  (0.03, leather_rec(rng, L, sh), -16), (0.15, hp(fire, 150), -5), (push * 0.9, hp(whoomph, 150), -9),
                  (0.15, crackle, -17), (L - 0.05, ck.choke(pivot, 0.08, 0.05), -16)])
    return yard(y, rng, 0.12, 0.3)


@recipe(FORGE, "bellows", "wheeze",
        "The bellows worked, modelled: air forced through the nozzle, the leather wheezing, the fire roaring",
        """Modelled: the air out of the nozzle is turbulent jet noise (its pitch and level rising with the pressure as
        the board goes down, a broad hiss-and-roar, not a whistle), the leather wheezing as it's squeezed (slow
        stick-slip through soft, broad leather modes, quickening with the stroke), the board's pivot creaking at each
        end (stick-slip through a wooden lever) and the leather valve slapping shut (a soft flap of air); the fire
        roaring up after it (lapping noise, its lowpass opening as it flares), a soft whoomph as it catches and the
        crackle flaring. A 1.5 to 2 s stroke and the fire's after-roar, heard across the yard.""",
        takes=3, lufs=-21)
def bellows_wheeze(rng, k):
    L = (1.5, 1.75, 2.0)[k % 3] * rng.uniform(0.96, 1.04)
    push = rng.uniform(0.25, 0.35) * L
    sh = stroke(L, push)
    p = env(sh, L)
    air = unit(bp(W.jet(L, rng, pressure=p, opening=0.7, peak=1100, low=0.3, eddy=0.9), 200, 4000))
    leather = synth.creak(L, 8 + 70 * p, rng, body=[f * rng.uniform(0.9, 1.1) for f in (420, 760, 1150, 1700)], q=4,
                          jitter=0.5, grit=0.5)
    leather = unit(hp(leather, 250)) * np.clip(p * 1.2, 0, 1)
    pivot = synth.creak(0.25, env([(0, 20), (0.25, 45)], 0.25), rng, body=synth.WOOD[:5], q=14)
    pivot = unit(hp(pivot, 300)) * env([(0, 0), (0.04, 1), (0.25, 0)], 0.25)
    valve = dsp.shaped(unit(bp(synth.noise(0.06, rng, "pink"), 250, 1500)), [(0, 0), (0.003, 1), (0.06, 0)])
    Lf = L + 0.4
    lvl = env([(0, 0.15), (push + 0.25, 1), (L, 0.7), (Lf, 0)], Lf)
    fire = blaze(rng, Lf, lvl) * lvl
    whoomph = lp(synth.noise(0.3, rng, "pink"), 700) * env([(0, 0), (0.04, 1), (0.3, 0)], 0.3)
    crackle = unit(crackle_model(rng, Lf, env([(0, 8), (push + 0.3, 90), (L, 30), (Lf, 5)], Lf), 2.0))
    y = ck.place([(0, pivot, -10), (0.02, valve, -12), (0.03, air, 0), (0.03, leather, -12),
                  (0.15, hp(unit(fire), 150), -5), (push * 0.9, hp(unit(whoomph), 150), -9), (0.15, crackle, -17),
                  (L - 0.05, pivot, -16)])
    return yard(y, rng, 0.12, 0.3)


# ---- up-forge: the quench ------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=1)
def _splash():
    """The packs' drum of sloshing water at its loudest splash (5.2-5.5 s): the iron going in."""
    x = ck.get(DRUM)
    return unit(ck.cut(x, samples(5.2), samples(5.5), 0.002, 0.08))


def quench_shape(L):
    """The quench over L s: the steam flashes off at once and dies back, the boil swells up after and settles."""
    return [(0, 0), (0.006, 1), (0.12, 0.65), (0.4, 0.25), (L, 0)], [(0, 0), (0.12, 0.3), (0.35, 1), (0.75, 0.45), (L, 0)]


@recipe(FORGE, "quench", "hiss",
        "Hot iron in the quench barrel: a sharp hiss of steam and a boil of water",
        """From real recordings: the iron plunged in (the packs' real drum of water at its loudest splash, pitched up
        for a barrel), the steam flashing off it at once (a real steam vehicle's hiss and a locomotive's steam hiss,
        pitched up for the violence of it and high-passed, a sharp hiss that dies back), the iron's skin crackling as
        the water flashes on it (the real steam hiss cut into tiny grains, thick and thinning), and the water boiling up
        round it (the drum's real gurgle, pitched up into a churning boil, and real water trickling, rung a little
        through the barrel's wooden hollow), settling. 1.1 to 1.6 s and its tail, heard across the yard.""",
        sources=[STEAM, HISS, DRUM, TRICKLE], takes=3, lufs=-21)
def quench_hiss(rng, k):
    L = (1.1, 1.35, 1.6)[k % 3] * rng.uniform(0.95, 1.05)
    hs, bs = quench_shape(L)
    plunge = unit(hp(dsp.vari(_splash(), rng.uniform(1, 3)), 250))
    st = ck.get(STEAM)
    a = int(rng.integers(0, len(st) - samples(L * 1.4)))
    hiss = dsp.shaped(unit(lp(hp(dsp.vari(st[a:a + samples(L * 1.4)], rng.uniform(3, 5)), 1800, 2), 9000)), hs)
    first = ck.get(HISS)
    first = unit(hp(dsp.vari(first[:samples(0.5)], rng.uniform(4, 6)), 2500, 2))
    first = dsp.shaped(first, [(0, 0), (0.005, 1), (0.35, 0)])
    skin = unit(bp(sizzle_rec(rng, 0.9, env([(0, 3500), (0.25, 900), (0.9, 30)], 0.9)), 1500, 8000))
    d = ck.get(DRUM)
    b0 = int(rng.integers(0, samples(4.5)))
    gurgle = unit(bp(dsp.vari(d[b0:b0 + samples(L * 1.8)], rng.uniform(4, 6)), 250, 2600))
    trickle = unit(lp(hp(bed(rng, TRICKLE, L), 600), 3500))
    boil = dsp.shaped(mix(gurgle, trickle * 0.4), bs)
    boil = mix(unit(boil), unit(dsp.resonate(boil, BARREL, q=4)) * 0.25)
    y = ck.place([(0, plunge, -6), (0.004, hiss, 0), (0.002, first, -2), (0.01, skin, -9), (0.03, unit(boil), -8)])
    return yard(y, rng, 0.12, 0.3)


@recipe(FORGE, "quench", "boil",
        "Hot iron in the quench barrel, modelled: a sharp jet of steam, the water flashing and boiling",
        """Modelled: the iron going in (a short slap of water and a big bubble's ring), the steam flashing off it
        (turbulent jet noise from a hole as big as the iron, high and sharp, its pressure spiking and dying), the water
        flashing to steam on the iron's skin (a dense sizzle of tiny bursts, thinning), and the boil round it: bubbles
        by the hundred, their Minnaert rings from 220 Hz to 1.4 kHz, swelling up and settling, rung through the barrel's
        wooden hollow. 1.1 to 1.6 s and its tail, heard across the yard.""",
        takes=3, lufs=-21)
def quench_boil(rng, k):
    L = (1.1, 1.35, 1.6)[k % 3] * rng.uniform(0.95, 1.05)
    hs, bs = quench_shape(L)
    n = samples(L)
    jet = unit(hp(W.jet(L, rng, pressure=env(hs, L), opening=0.35, peak=4200, low=0.1, rasp=0.3), 1200, 2))
    skin = unit(flash(samples(0.9), rng, env([(0, 3500), (0.25, 900), (0.9, 30)], 0.9)))
    bub = synth.bubbles(L, env([(0, 30), (0.08, 600), (0.4, 220), (L, 8)], L), 220, 1400, rng, rise=(0.1, 0.5))
    churn = unit(lp(hp(synth.noise(L, rng, "pink"), 250), 1800)) * env(bs, L)[:n]
    boil = mix(unit(bub), churn * 0.25)
    boil = mix(boil, unit(dsp.resonate(boil, BARREL, q=4)) * 0.3)
    splash = dsp.shaped(unit(bp(synth.noise(0.15, rng), 400, 4000)), [(0, 0), (0.004, 1), (0.15, 0)])
    plunge = mix(splash, synth.bubble(rng.uniform(260, 340), 0.15, 0.6) * 0.6)
    y = ck.place([(0, unit(hp(plunge, 200)), -6), (0.004, jet, 0), (0.01, skin, -9), (0.03, unit(boil), -8)])
    return yard(y, rng, 0.12, 0.3)


# ---- up-bbq: the grill lit -----------------------------------------------------------------------------------------------

def lick_rec(rng, L):
    """A lick of flame, from a real recording: a short slice of the slowed-water roar, brighter, swelling and gone (the
    'fff' of fat or fluid catching)."""
    return dsp.shaped(unit(hp(roar_rec(rng, L, -5, 2500), 250)), [(0, 0), (L * 0.2, 1), (L, 0)])


@recipe(BBQ, "light", "whump",
        "The grill lit: a match struck and tossed in, the lighter fluid catching with a whump",
        """From real recordings: a match struck (a blade drawn fast over a surface, cut short and pitched up, its head
        flaring in a burst of real steam hiss cut into grains) or the long BBQ lighter's trigger clicked (a real latch,
        dropped); then the soaked charcoal catching all at once: a whump (the packs' real whoomph of air, its head
        before its knock, dropped a few semitones), the fluid's flames roaring up and settling (the slowed water
        darkened, licking brighter as it catches), the kettle grill's thin steel bowl ticking with the heat (a real
        light plate, choked) and the first crackle (real wood cracking cut into grains). Out on the engine's front at
        night. The whump's weight is in the low mids, not the squish.""",
        sources=SCRATCH + [LATCH, STEAM, WHOOMPH, ROAR] + CRACKS + PLATE_L, takes=2, lufs=-21)
def light_whump(rng, k):
    parts = []
    if k % 2 == 0:
        s = ck.get(SCRATCH[0])
        h = ck.hits(s, floor_db=-14, gap=0.03)
        a = h[0][0] if h else 0
        strike = unit(hp(dsp.vari(ck.cut(s, a, a + samples(0.07), 0.002, 0.02), rng.uniform(1, 3)), 1500))
        flare = unit(bp(sizzle_rec(rng, 0.35, env([(0, 4000), (0.08, 2500), (0.35, 200)], 0.35)), 1500, 8000))
        parts += [(0, strike, -6), (0.05, flare, -8), (0.06, lick_rec(rng, 0.3), -18)]
        t0 = rng.uniform(0.55, 0.7)
    else:
        click = ck.choke(dsp.vari(hit_of(LATCH, 0, 0.12), -rng.uniform(2, 4)), 0.006, 0.02)
        parts += [(0, unit(hp(click, 800)), -8), (0.12, unit(hp(click, 1500)), -14), (0.14, lick_rec(rng, 0.25), -16)]
        t0 = rng.uniform(0.32, 0.4)
    whump = unit(lp(dsp.vari(ck.cut(ck.get(WHOOMPH), samples(0.02), samples(0.16), 0.008, 0.03), -4), 2200))
    Lr = 1.5
    roar = dsp.shaped(roar_rec(rng, Lr, -7, 2200), [(0, 0), (0.03, 1), (0.25, 0.45), (0.8, 0.22), (Lr, 0)])
    licks = lick_rec(rng, 0.6)
    crackle = unit(hp(splinter(rng, Lr, env([(0, 35), (0.3, 18), (Lr, 6)], Lr), (2, 8)), 1200))
    bowl = ck.choke(dsp.vari(hit_of(PLATE_L[k % 5], 0, 0.2), -rng.uniform(3, 5)), 0.01, 0.04)
    parts += [(t0, hp(whump, 140), 0), (t0 + 0.01, hp(roar, 140), -4), (t0 + 0.05, licks, -8),
              (t0 + 0.08, crackle, -15), (t0 + 0.02, unit(hp(bowl, 300)), -20)]
    return deck(hp(ck.place(parts), 120, 2), rng, 0.08, 0.5)


@recipe(BBQ, "light", "flare",
        "The grill lit, modelled: a match or the lighter, the fluid's vapour catching in a soft whump, the fire up",
        """Modelled: a match struck (a quick stick-slip scratch and the head's flare, a short sizzle) or the BBQ
        lighter's trigger clicked (two small clicks and a little flame); then the lighter fluid's vapour over the
        charcoal igniting all at once: a pressure front (a burst of low-mid noise whose band sweeps up as it catches and
        falls as it spreads, the body of it a soft 170 Hz thump, never a sub), the flames roaring up and settling
        (lapping noise, its lowpass opening and closing), and the first crackle. Out on the engine's front at night.""",
        takes=2, lufs=-21)
def light_flare(rng, k):
    parts = []
    if k % 2 == 0:
        strike = ck.friction(rng, 0.07, 320, 1800, 7000, 0.9, env([(0, 0), (0.01, 1), (0.07, 0)], 0.07))
        flare = unit(flash(samples(0.35), rng, env([(0, 3000), (0.08, 2000), (0.35, 150)], 0.35)))
        parts += [(0, strike, -6), (0.05, flare, -8)]
        t0 = rng.uniform(0.55, 0.7)
    else:
        c = synth.click(rng.uniform(2200, 2800), q=10, length=0.03, rng=rng, body=[2400, 3900, 5600])
        flame = dsp.shaped(unit(bp(synth.noise(0.25, rng, "pink"), 300, 2500)), [(0, 0), (0.05, 1), (0.25, 0)])
        parts += [(0, unit(c), -8), (0.12, unit(c), -14), (0.14, flame, -18)]
        t0 = rng.uniform(0.32, 0.4)
    L = 0.45
    front = dsp.sweep_filter(synth.noise(L, rng, "pink"), "bp", env([(0, 250), (0.04, 900), (L, 300)], L, "exp"), q=0.9)
    front = dsp.shaped(unit(hp(front, 140)), [(0, 0), (0.02, 1), (0.12, 0.45), (L, 0)])
    thump = synth.thump(rng.uniform(160, 185), 0.25, drop=0.3)
    Lr = 1.5
    lvl = env([(0, 0), (0.03, 1), (0.3, 0.45), (0.8, 0.22), (Lr, 0)], Lr)
    fire = blaze(rng, Lr, lvl) * lvl
    crackle = unit(crackle_model(rng, Lr, env([(0, 50), (0.3, 25), (Lr, 8)], Lr), 1.5))
    parts += [(t0, front, 0), (t0, thump, -9), (t0 + 0.01, hp(unit(fire), 140), -4), (t0 + 0.08, crackle, -15)]
    return deck(hp(ck.place(parts), 120, 2), rng, 0.08, 0.5)


# ---- up-bbq: the grill burning -------------------------------------------------------------------------------------------

def spit_rec(rng):
    """Fat spitting into the coals, from real recordings: a wet pop as it bursts off the sausage (a real squelch cut to
    a grain, pitched up), a spark of it cracking (a grain of real wood cracking), and a lick of flame where it lands
    with a burst of sizzle."""
    burst = sizzle_rec(rng, 0.25, env([(0, 2500), (0.25, 0)], 0.25))
    return ck.place([(0, unit(hp(wet_grain(rng, (3, 8)), 900)), -2), (0.003, pop_rec(rng, (0, 4)), -6),
                     (0.01, unit(bp(burst, 1500, 8000)), -8), (0.02, lick_rec(rng, rng.uniform(0.2, 0.35)), -12)])


@recipe(BBQ, "grill", "sausages",
        "The grill burning: sausages sizzling, charcoal crackling and tinkling, fat spitting",
        """From real recordings: the sausages frying, a dense sizzle made of the packs' real steam hiss cut into tiny
        grains (1-4 ms, each snapping on and dying at once, as a droplet bursts in the fat), thicker and thinner as the
        fat runs, over a thin bed of the same hiss; the charcoal crackling (real wood cracking cut into grains, pitched
        up) and tinkling as the lumps burn (real glass knocks choked to ticks); the coals' low glow (the slowed water,
        quiet and dark); and every second or so fat spitting into the coals: a wet pop (a real squelch cut to a grain),
        a spark of it cracking, a lick of flame and a burst of sizzle where it lands. Outdoors on the engine's front at
        night. An 8 s loop; it carries in the mids, a sizzle and not a roar.""",
        sources=[STEAM, ROAR] + CRACKS + GLASS + WET, takes=1, loop=True, lufs=-25, seconds=GRILL_L)
def grill_sausages(rng, k):
    T = GRILL_L
    n = samples(T)
    BL = T + LEAD + SEAM + 0.3
    run = np.clip(1 + 0.3 * W.slow(n, 0.6, rng), 0.5, 1.5)
    fry = unit(bp(sizzle_rec(rng, T, 650 * run, loop=True), 1200, 9000))
    ev = np.zeros(samples(T + 1.0), np.float32)
    ev[:n] += fry * 0.5
    crackle = unit(hp(splinter(rng, T, 5, (4, 10)), 1500))
    pops = splinter(rng, T, 0.5, (-2, 2))
    spits = ck.place([(float(t), spit_rec(rng), rng.uniform(-6, 0)) for t in W.poisson(T, 1.1, rng)], T + 0.6)
    ev = mix(ev, crackle * 0.12, pops * 0.18, unit(spits) * 0.35, tinkle_rec(rng, T, 1.5, (0, 5)) * 0.06)
    hiss = unit(lp(hp(bed(rng, STEAM, BL), 3000), 8000)) * town.lapping(rng, samples(BL), 1.0, 0.3)
    glow = roar_rec(rng, BL, -10, 700) * town.lapping(rng, samples(BL), 2.0, 0.4)
    bedx = deck(mix(hiss * 0.05, hp(glow, 150) * 0.12), rng, 0.06, None)
    return loop_of(deck(ev, rng, 0.06, None), bedx, T)


def spit_model(rng):
    """Fat spitting, modelled: a wet pop (a hard click and a tiny bubble's ring), a burst of sizzle and a lick of flame
    where it lands."""
    pop = mix(synth.click(rng.uniform(1500, 2600), q=4, length=0.015, rng=rng),
              synth.bubble(rng.uniform(1800, 3200), 0.04, 0.6) * 0.5)
    burst = flash(samples(0.25), rng, env([(0, 2500), (0.25, 0)], 0.25))
    lick = dsp.shaped(unit(bp(synth.noise(0.3, rng, "pink"), 300, 2500)), [(0, 0), (0.06, 1), (0.3, 0)])
    return ck.place([(0, unit(pop), -2), (0.01, unit(burst), -8), (0.02, lick, -12)])


@recipe(BBQ, "grill", "embers",
        "The grill burning, modelled: fat sizzling on hot iron, charcoal crackling, fat spitting",
        """Modelled: the sausages frying on the grate, fat crackling and spitting in uneven clusters over a thin hiss
        (the hot box's grease model, upkeep's) and a dense fine sizzle of tiny bursts (the extinguisher's, douse's:
        water in hot fat), thickening and thinning as the fat runs; the charcoal crackling (synthesised burst sap) and
        its glow (a low, lapping murmur); every second or so fat spitting into the coals: a wet pop, a burst of sizzle
        and a lick of flame. Outdoors on the engine's front at night. An 8 s loop, built periodic; a sizzle and not a
        roar.""",
        takes=1, loop=True, lufs=-25, seconds=GRILL_L)
def grill_embers(rng, k):
    T = GRILL_L
    n = samples(T)
    grease = cook(n, rng, 9, 0.3)
    fine = flash(n, rng, 600 * np.clip(1 + 0.3 * W.slow(n, 0.6, rng), 0.5, 1.5), loop=True)
    crackle = dsp.wrap(crackle_model(rng, T + 0.1, 6, 0.6), n)
    spits = W.place(n, [(t, spit_model(rng), rng.uniform(0.5, 1.0)) for t in W.poisson(T, 1.1, rng)])
    glow = W.cfilter(W.pnoise(n, rng), lambda f: (f / 150) ** 2 / (1 + (f / 150) ** 2) / (1 + (f / 600) ** 2))
    glow = glow * np.clip(0.7 + 0.3 * W.slow(n, 2.0, rng), 0.2, 1.3)
    y = mix(W.norm(grease) * 0.3, W.norm(fine) * 0.45, W.norm(crackle) * 0.15, W.norm(spits) * 0.35, W.norm(glow) * 0.08)
    for dt, g in ((0.0037, 0.25), (0.0093, 0.14)):
        y = y + np.roll(y, samples(dt)) * g
    return W.seamless(W.croom(y, "night", 0.06, rng))


# ---- up-bbq: a sausage turned --------------------------------------------------------------------------------------------

def clack_rec(rng, take):
    """The tongs' clack on the grate, from real recordings: the arms' steel tips meeting the bars (small real metal
    clinks, cut short) and the grate's thin bars knocked (a real light plate, pitched up and choked, two or three real
    light metal ticks for the bars chattering)."""
    tips = hit_of(CLINKS[take % 2], int(rng.integers(2)), 0.12, -18)
    tips = ck.choke(dsp.vari(tips, rng.uniform(-1, 1)), 0.01, 0.03)
    plate = ck.choke(dsp.vari(hit_of(PLATE_L[take % 5], 0, 0.2), rng.uniform(2, 4)), 0.008, 0.025)
    bars = [(0.004 + 0.011 * i + rng.uniform(0, 0.005),
             ck.choke(dsp.vari(hit_of(METAL_L[(take + i) % 5], 0, 0.1), rng.uniform(2, 5)), 0.003, 0.015), -10 - 3 * i)
            for i in range(int(rng.integers(2, 4)))]
    return unit(hp(ck.place([(0, unit(hp(tips, 1200)), 0), (0, unit(plate), -4)] + bars), 300, 2))


@recipe(BBQ, "turn", "tongs",
        "A sausage turned: the tongs' clack on the grate, the sausage rolled over, a spit of fat",
        """From real recordings: the steel tongs clacking down on the grate (small real metal clinks for the arms' tips,
        a real light plate pitched up and choked for the grate, real light metal ticks for its bars chattering), the
        sausage's skin unsticking from the bars as it's rolled (real squelches cut to wet grains, pitched up) and the
        tongs scraping a bar (a blade drawn fast, cut short), then the raw side meeting the heat: a spit of fat, a burst
        of sizzle (the real steam hiss in tiny grains) and a pop or two (real wood cracking cut to grains), and the
        tongs' arms ticking together as they let go. Out on the engine's front at night.""",
        sources=CLINKS + PLATE_L + METAL_L + WET + SCRATCH + [STEAM, ROAR] + CRACKS, takes=3, lufs=-21)
def turn_tongs(rng, k):
    roll = ck.place([(0.012 * j + rng.uniform(0, 0.006), unit(hp(wet_grain(rng, (2, 6)), 700)), -4 - 3 * j)
                     for j in range(int(rng.integers(3, 6)))])
    s = ck.get(SCRATCH[(k + 1) % 2])
    h = ck.hits(s, floor_db=-14, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    scrape = unit(hp(dsp.vari(ck.cut(s, a, a + samples(0.06), 0.003, 0.03), -rng.uniform(1, 3)), 1500))
    Ls = rng.uniform(0.45, 0.65)
    sizzle = unit(bp(sizzle_rec(rng, Ls, env([(0, 200), (0.03, 3500), (Ls * 0.4, 1200), (Ls, 50)], Ls)), 1200, 8000))
    t1 = rng.uniform(0.09, 0.14)
    parts = [(0, clack_rec(rng, k), 0), (t1, roll, -8), (t1 + 0.01, scrape, -14), (t1 + 0.04, sizzle, -6),
             (t1 + 0.05, spit_rec(rng), -8), (t1 + rng.uniform(0.15, 0.3), pop_rec(rng, (1, 5)), -14)]
    let = ck.choke(dsp.vari(hit_of(CLINKS[(k + 1) % 2], 0, 0.08, -18), rng.uniform(2, 4)), 0.004, 0.012)
    parts.append((t1 + Ls * rng.uniform(0.6, 0.8), unit(hp(let, 1500)), -16))
    return deck(hp(ck.place(parts), 250, 2), rng, 0.07)


def tongs_model(rng, contact=0.0003):
    """The tongs' arm struck, modelled: a thin steel bar's modes, damped by the hand on it."""
    return ck.choke(unit(W.body(rng, rng.uniform(1100, 1400), TONGS, decay=0.15, length=0.3, contact=contact)), 0.01, 0.04)


def grate_model(rng):
    """The grate's thin bars knocked, modelled: two or three short bar rings a hair apart, chattering."""
    return ck.place([(0.004 + 0.01 * i + rng.uniform(0, 0.005),
                      unit(W.body(rng, rng.uniform(1700, 2600), W.BAR, decay=0.03, length=0.1, contact=0.0003, count=3)),
                      -3 * i) for i in range(int(rng.integers(2, 4)))])


@recipe(BBQ, "turn", "flip",
        "A sausage turned, modelled: the tongs' steel clacking on the grate's bars, a spit of fat",
        """Modelled: the tongs' arms (thin steel bars' modes, damped by the hand) clacking down on the grate (its thin
        bars ringing short and chattering), the sausage's skin unsticking as it rolls (small wet clicks and bubbles),
        then the raw side meeting the heat: a burst of sizzle (tiny bursts, swelling and thinning), a wet pop and a lick
        of flame, and the tongs' arms ticking as they let go. Out on the engine's front at night.""",
        takes=3, lufs=-21)
def turn_flip(rng, k):
    clack = ck.place([(0, tongs_model(rng), 0), (0.002, tongs_model(rng), -5), (0.001, grate_model(rng), -4)])
    roll = ck.place([(0.012 * j + rng.uniform(0, 0.006),
                      mix(synth.click(rng.uniform(900, 1600), q=3, length=0.012, rng=rng),
                          synth.bubble(rng.uniform(1500, 2500), 0.03, 0.4) * 0.4), -4 - 3 * j)
                     for j in range(int(rng.integers(3, 6)))])
    Ls = rng.uniform(0.45, 0.65)
    sizzle = unit(flash(samples(Ls), rng, env([(0, 200), (0.03, 3500), (Ls * 0.4, 1200), (Ls, 50)], Ls)))
    t1 = rng.uniform(0.09, 0.14)
    parts = [(0, unit(hp(clack, 300)), 0), (t1, unit(hp(roll, 500)), -8), (t1 + 0.04, sizzle, -6),
             (t1 + 0.05, spit_model(rng), -8), (t1 + Ls * rng.uniform(0.6, 0.8), tongs_model(rng, 0.0002), -16)]
    return deck(hp(ck.place(parts), 250, 2), rng, 0.07)


# ---- up-bbq: the cooler --------------------------------------------------------------------------------------------------

def ice_rec(rng, length, count, semis=(-5.0, -1.0)):
    """Ice shifting and clinking, from real recordings: small real glass knocks pitched down (ice is duller than glass)
    and choked short, bunched as the hand digs in, with real stones crunched under them, pitched up (the cubes grinding)."""
    parts = []
    for i in range(count):
        t = length * rng.random() ** 1.4
        x = hit_of(GLASS[int(rng.integers(len(GLASS)))], int(rng.integers(2)), 0.1, -18)
        x = ck.choke(dsp.vari(x, rng.uniform(*semis)), rng.uniform(0.004, 0.012), rng.uniform(0.008, 0.02))
        parts.append((t, unit(hp(x, 900)), 20 * np.log10(rng.uniform(0.2, 1) ** 1.3)))
    for _ in range(int(rng.integers(2, 4))):
        s = ck.get(STONES[int(rng.integers(3))])
        h = ck.hits(s, floor_db=-14, gap=0.03)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        g = ck.cut(s, a, a + samples(rng.uniform(0.06, 0.12)), 0.002, 0.03)
        parts.append((rng.uniform(0, length * 0.8), unit(hp(dsp.vari(g, rng.uniform(2, 5)), 1500)), -10))
    return ck.place(parts, length + 0.2)


@recipe(BBQ, "cooler", "lid",
        "The cooler opened: its catch, the lid unsticking and lifted, ice shifting and clinking",
        """From real recordings: the cooler's catch flipped (a real latch, dropped), the lid unsticking from its seal
        and lifting (a real book opened, its stiff cover's catch and puff, and a small real creak pitched up for the
        plastic hinge), then a hand in the ice: cubes shifting and clinking (small real glass knocks pitched down and
        choked, real stones crunched and pitched up for the grinding), the meltwater sloshing (real water trickling,
        darkened), and a can knocked against the ice as it's pulled out (a real tin, pitched up, choked), or the lid let
        fall shut again (a real book shut, dropped). Out on the engine's front at night.""",
        sources=[LATCH] + BOOK + CREAKS[2:] + GLASS + STONES + [TRICKLE] + TIN, takes=2, lufs=-21)
def cooler_lid(rng, k):
    catch = unit(hp(ck.choke(dsp.vari(hit_of(LATCH, 0, 0.15), -rng.uniform(2, 4)), 0.01, 0.03), 500))
    unstick = unit(hp(dsp.vari(hit_of(BOOK[0], 0, 0.14, -24), -rng.uniform(1, 3)), 220))
    hinge = unit(hp(dsp.vari(ck.align(ck.get(CREAKS[2])), rng.uniform(4, 6))[:samples(0.2)], 700))
    hinge = dsp.shaped(hinge, [(0, 0), (0.03, 1), (0.2, 0)])
    Li = rng.uniform(0.7, 0.9)
    ice = ice_rec(rng, Li, int(rng.integers(14, 22)))
    melt = dsp.shaped(unit(lp(hp(bed(rng, TRICKLE, Li), 500), 3000)), [(0, 0), (0.1, 1), (Li * 0.7, 0.6), (Li, 0)])
    t1 = rng.uniform(0.22, 0.28)
    parts = [(0, catch, -4), (t1, unstick, -2), (t1 + 0.03, hinge, -16), (t1 + 0.3, unit(ice), 0), (t1 + 0.3, melt, -20)]
    if k % 2 == 0:
        can = ck.choke(dsp.vari(hit_of(TIN[k % 5], 0, 0.15), rng.uniform(5, 7)), 0.006, 0.02)
        parts += [(t1 + 0.3 + Li * 0.75, unit(hp(can, 500)), -6), (t1 + 0.35 + Li * 0.75, unit(ice_rec(rng, 0.25, 5)), -6)]
    else:
        shut = unit(hp(dsp.vari(hit_of(BOOK[1], 0, 0.2, -18), -rng.uniform(2, 4)), 180))
        parts += [(t1 + 0.35 + Li, shut, -2), (t1 + 0.37 + Li, unit(ice_rec(rng, 0.15, 4)), -14)]
    return deck(hp(ck.place(parts), 150, 2), rng, 0.07)


def ice_model(rng, length, count):
    """Ice shifting and clinking, modelled: hard little cubes struck (clicks rung through a cube's few high modes), bunched
    as the hand digs in, and crushed grains grinding under them."""
    parts = []
    for _ in range(count):
        f = rng.uniform(1800, 4200)
        c = synth.click(f, q=rng.uniform(6, 11), length=0.025, rng=rng, body=[f, f * 1.58, f * 2.31])
        parts.append((length * rng.random() ** 1.4, unit(c), 20 * np.log10(rng.uniform(0.2, 1) ** 1.3)))
    grind = ck.grains(rng, int(rng.integers(15, 30)), length * 0.8, 1500, 6000, q=(2, 6), length=(0.003, 0.01), shape=0.0)
    return ck.place(parts + [(0.02, unit(grind), -8)], length + 0.2)


@recipe(BBQ, "cooler", "ice",
        "The cooler opened, modelled: the catch, the hollow plastic lid lifted, ice shifting and clinking",
        """Modelled: the cooler's catch (a hard plastic click), the lid lifting off its seal (a soft suck of air) and
        its hinge, the hollow plastic box knocked as it opens (broad, dull modes from 210 Hz), then a hand in the ice:
        cubes clinking (clicks rung through a cube's few high modes) and grinding (fine grains), meltwater lapping
        (small bubbles and a dark slosh), and a can knocked as it's pulled out (a full can's damped wall), or the lid
        dropped shut (the box knocked harder). Out on the engine's front at night.""",
        takes=2, lufs=-21)
def cooler_ice(rng, k):
    catch = unit(synth.click(rng.uniform(1800, 2400), q=6, length=0.03, rng=rng, body=[2100, 3300, 4700]))
    seal = dsp.shaped(unit(bp(synth.noise(0.12, rng, "pink"), 300, 2500)), [(0, 0), (0.02, 1), (0.12, 0)])
    box = ck.choke(unit(W.body(rng, rng.uniform(200, 230), [r / COOLER[0] for r in COOLER], decay=0.06, length=0.3,
                               contact=0.002)), 0.02, 0.04)
    hinge = synth.creak(0.18, env([(0, 40), (0.18, 80)], 0.18), rng, body=[900, 1500, 2300], q=10)
    hinge = unit(hp(hinge, 600)) * env([(0, 0), (0.03, 1), (0.18, 0)], 0.18)
    Li = rng.uniform(0.7, 0.9)
    ice = ice_model(rng, Li, int(rng.integers(14, 22)))
    melt = mix(unit(synth.bubbles(Li, 25, 600, 2000, rng, rise=(0.1, 0.4))) * 0.5,
               dsp.shaped(unit(lp(hp(synth.noise(Li, rng, "pink"), 400), 1500)), [(0, 0), (0.1, 1), (Li, 0)]) * 0.5)
    t1 = rng.uniform(0.22, 0.28)
    parts = [(0, catch, -4), (t1, seal, -14), (t1 + 0.02, unit(hp(box, 180)), -10), (t1 + 0.03, hinge, -16),
             (t1 + 0.3, unit(ice), 0), (t1 + 0.3, unit(melt), -18)]
    if k % 2 == 0:
        can = ck.choke(unit(W.body(rng, rng.uniform(1100, 1300), CAN, decay=0.04, length=0.2, contact=0.0008)), 0.008, 0.02)
        t2 = t1 + 0.3 + Li * 0.75
        parts += [(t2, unit(hp(can, 500)), -6), (t2 + 0.05, unit(ice_model(rng, 0.25, 5)), -6)]
    else:
        shut = ck.choke(unit(W.body(rng, rng.uniform(190, 220), [r / COOLER[0] for r in COOLER], decay=0.08, length=0.3,
                                    contact=0.0015)), 0.03, 0.05)
        parts += [(t1 + 0.35 + Li, unit(hp(shut, 160)), -6), (t1 + 0.37 + Li, unit(ice_model(rng, 0.15, 4)), -12)]
    return deck(hp(ck.place(parts), 150, 2), rng, 0.07)


# ---- up-bbq: a can cracked -----------------------------------------------------------------------------------------------

def glug_rec(rng, L, semis):
    """Beer moving in a can or a mouth, from a real recording: a slice of the packs' drum of sloshing water, pitched up
    into a small vessel and darkened."""
    d = ck.get(DRUM)
    a = int(rng.integers(0, len(d) - samples(L * 3)))
    return unit(lp(hp(dsp.vari(d[a:a + samples(L * 3)], semis), 250), 2500)[:samples(L)])


def swig_rec(rng, gulps):
    """A swig, from real recordings: the beer tipped (a slosh in the can), then `gulps` swallows, each the tongue
    lifting (a real wet grain) and the beer going down (a slice of the drum's real gurgle pitched up and darkened, a
    throat's dull knock)."""
    parts = [(0, dsp.shaped(glug_rec(rng, 0.25, rng.uniform(5, 7)), [(0, 0), (0.05, 1), (0.25, 0)]), -10)]
    t = rng.uniform(0.2, 0.28)
    for j in range(gulps):
        down = dsp.shaped(glug_rec(rng, 0.14, rng.uniform(1, 3)), [(0, 0), (0.02, 1), (0.14, 0)])
        parts += [(t, unit(hp(wet_grain(rng, (0, 3)), 700)), -14), (t + 0.02, unit(lp(down, 1200)), -4 - 2 * j)]
        t += rng.uniform(0.3, 0.38)
    return ck.place(parts)


@recipe(BBQ, "can", "crack",
        "A can cracked open: the tab's click and crack, its fizz, a swig",
        """From real recordings: the tab levered up (a real metal click, pitched up and choked), the seal giving with a
        sharp little crack (a real tin knocked, pitched far up and cut to its front) and the beer's fizz rushing out of
        the hole (the packs' bottle fizz, its head, high-passed: the director's note of 2 Oct, a fizz only where a fizz
        belongs, and here one does), the tab pressed back (the click again, smaller); then a swig: the beer tipped (a
        slice of a real drum's sloshing water pitched up into a can) and one, two or three swallows (a real wet grain
        for the tongue, the drum's gurgle pitched and darkened going down), the last take ending in a contented breath
        out (a real gasp drawn out, dropped and darkened). Out on the engine's front at night.""",
        sources=TICKS[:1] + TIN + FIZZ + [DRUM, GASP] + WET, takes=3, lufs=-21)
def can_crack(rng, k):
    click = ck.choke(dsp.vari(hit_of(TICKS[0], 0, 0.08, -18), rng.uniform(2, 4)), 0.004, 0.012)
    crack = ck.choke(dsp.vari(hit_of(TIN[(k + 1) % 5], 0, 0.1), rng.uniform(10, 13)), 0.003, 0.006)
    f = ck.get(FIZZ[k % 2])
    Lf = rng.uniform(0.3, 0.45)
    fizz = dsp.shaped(unit(lp(hp(f[:samples(Lf)], 1800), 10000)), [(0, 0), (0.004, 1), (0.06, 0.6), (Lf, 0)])
    parts = [(0, unit(hp(click, 1200)), -6), (0.012, unit(hp(crack, 1500)), -2), (0.014, fizz, -4),
             (0.12 + Lf * 0.5, unit(hp(click, 1500)), -16)]
    t = rng.uniform(0.75, 0.9)
    parts.append((t, swig_rec(rng, (1, 2, 3)[k % 3]), -2))
    if k % 3 == 2:
        Lb = 0.55
        ahh = dsp.shaped(unit(lp(hp(drawn(rng, GASP, Lb, -rng.uniform(2, 4)), 200), 3000)), [(0, 0), (0.06, 1), (Lb, 0)])
        parts.append((t + 1.25, ahh, -12))
    return deck(hp(ck.place(parts), 150, 2), rng, 0.06)


@recipe(BBQ, "can", "pop",
        "A can cracked open, modelled: the tab's click, the seal's crack and fizz, a swig",
        """Modelled: the tab levered up (a hard little click), the seal giving (a sharp crack rung through the can's
        thin, full wall: its close modes, damped by the beer), the gas rushing out (a short burst of high noise) and the
        beer fizzing up in the neck (bubbles by the hundred, small and high, thinning), the tab pressed back; then a
        swig: the beer tipped (a slosh of low bubbles) and one, two or three swallows heard from inside the head
        (heard_foley's gulp: the tongue's wet click, the swallow's squelch and bubble, the larynx dropping back), the
        last take ending in a contented breath out. Out on the engine's front at night.""",
        takes=3, lufs=-21)
def can_pop(rng, k):
    click = unit(synth.click(rng.uniform(3200, 3800), q=12, length=0.02, rng=rng))
    wall = W.body(rng, rng.uniform(1100, 1300), CAN, decay=0.03, length=0.15, contact=0.0002)
    crack = ck.choke(unit(mix(unit(wall), hp(synth.noise(0.01, rng), 2500) * 0.4)), 0.003, 0.008)
    Lf = rng.uniform(0.3, 0.45)
    gas = dsp.shaped(unit(bp(synth.noise(Lf, rng), 2500, 10000)), [(0, 0), (0.003, 1), (0.05, 0.4), (Lf, 0)])
    foam = synth.bubbles(Lf + 0.3, env([(0, 1500), (Lf + 0.3, 60)], Lf + 0.3), 2500, 7000, rng, rise=(0.05, 0.2))
    parts = [(0, click, -6), (0.012, unit(hp(crack, 1500)), -2), (0.014, gas, -4), (0.02, unit(foam), -12),
             (0.12 + Lf * 0.5, click, -16)]
    t = rng.uniform(0.75, 0.9)
    tip = mix(unit(synth.bubbles(0.25, 40, 250, 900, rng, rise=(0.1, 0.5))),
              dsp.shaped(unit(lp(synth.noise(0.25, rng, "pink"), 1200)), [(0, 0), (0.05, 1), (0.25, 0)]) * 0.5)
    parts.append((t, unit(hp(tip, 200)), -12))
    t += rng.uniform(0.2, 0.28)
    for j in range((1, 2, 3)[k % 3]):
        parts.append((t, unit(hp(gulp(rng), 150)), -4 - 2 * j))
        t += rng.uniform(0.3, 0.38)
    if k % 3 == 2:
        Lb = 0.55
        sh = env([(0, 0), (0.06, 1), (Lb, 0)], Lb)
        ahh = synth.breath(Lb, [(0, "a"), (Lb, "h")], "man", rng, shape=sh)
        parts.append((t + 0.3, unit(lp(hp(ahh, 200), 3000)), -12))
    return deck(hp(ck.place(parts), 150, 2), rng, 0.06)


# ---- up-bbq: cheers ------------------------------------------------------------------------------------------------------

def can_rec(rng, take, semis):
    """A full can knocked, from a real recording: the packs' tin knocked, pitched to a can's size and choked hard (the
    beer in it stops the wall ringing), its thump high-passed off."""
    x = ck.choke(dsp.vari(hit_of(TIN[take % 5], 0, 0.15), semis), 0.005, 0.02)
    return unit(hp(x, 250, 2))


@recipe(BBQ, "cheers", "clink",
        "Cans clinked: cheers. Two full cans of beer tapped together, dull aluminium, never glass",
        """From real recordings: two full cans tapped together, a few milliseconds apart (each the packs' real tin
        knocked, pitched to a can's size and choked hard, since the beer in them stops the wall ringing, at slightly
        different pitches), the rims' faint edge (a small real metal clink, high and quiet), and the beer sloshing in
        both (a slice of a real drum's sloshing water, pitched up and darkened). Dull, short and friendly: aluminium,
        never glass. One take a single 'cheers', one two taps. Out on the engine's front at night.""",
        sources=TIN + CLINKS[:1] + [DRUM], takes=2, lufs=-21)
def cheers_clink(rng, k):
    parts = []
    for i, t in enumerate((0.0,) if k % 2 == 0 else (0.0, rng.uniform(0.2, 0.26))):
        g = -6 * i
        a = can_rec(rng, k + 2 * i, rng.uniform(2, 4))
        b = can_rec(rng, k + 2 * i + 1, rng.uniform(4, 6))
        rim = ck.choke(dsp.vari(hit_of(CLINKS[0], 0, 0.08, -18), rng.uniform(-2, 0)), 0.003, 0.01)
        parts += [(t, a, g), (t + rng.uniform(0.003, 0.007), b, g - 3), (t, unit(hp(rim, 2500)), g - 22)]
    slosh = dsp.shaped(glug_rec(rng, 0.22, rng.uniform(6, 8)), [(0, 0), (0.04, 1), (0.22, 0)])
    parts.append((0.03, unit(lp(slosh, 1800)), -22))
    return deck(hp(ck.place(parts), 200, 2), rng, 0.06)


def can_model(rng, f1, contact=0.0012):
    """A full can knocked, modelled: its thin aluminium wall's close modes, struck through a soft contact (can on can,
    a millisecond) and damped fast by the beer in it."""
    return ck.choke(unit(W.body(rng, f1, CAN, decay=0.035, length=0.2, contact=contact, tilt=0.9)), 0.008, 0.02)


@recipe(BBQ, "cheers", "tunk",
        "Cans clinked, modelled: two full aluminium cans tapped together, a dull tunk",
        """Modelled: two full cans tapped together a few milliseconds apart, each its thin aluminium wall's close modes
        (from 600-750 Hz, a little apart so the two don't fuse) struck through a soft millisecond contact and damped
        fast by the beer in it; the rims' faint edge (a tiny high click), and the beer sloshing in both (low bubbles).
        Dull, short and friendly, never glass. One take a single 'cheers', one two taps. Out on the engine's front at
        night.""",
        takes=2, lufs=-21)
def cheers_tunk(rng, k):
    parts = []
    for i, t in enumerate((0.0,) if k % 2 == 0 else (0.0, rng.uniform(0.2, 0.26))):
        g = -6 * i
        f = rng.uniform(600, 750)
        rim = synth.click(rng.uniform(4800, 5600), q=20, length=0.02, rng=rng)
        b = can_model(rng, f * rng.uniform(1.07, 1.12))
        parts += [(t, can_model(rng, f), g), (t + rng.uniform(0.003, 0.007), b, g - 3), (t, unit(rim), g - 24)]
    slosh = mix(unit(synth.bubbles(0.3, 50, 300, 1000, rng, rise=(0.1, 0.5))),
                dsp.shaped(unit(lp(synth.noise(0.3, rng, "pink"), 1200)), [(0, 0), (0.04, 1), (0.3, 0)]) * 0.5)
    parts.append((0.03, unit(hp(slosh, 250)), -22))
    return deck(hp(ck.place(parts), 200, 2), rng, 0.06)


# ---- up-bbq: the chair ---------------------------------------------------------------------------------------------------

def sitting(rng, k, creak, weight, legs, cloth):
    """Three ways with a Muskoka chair: sat down in (the body coming down on the slats, the chair creaking under the
    weight as it settles, its feet shifting on the deck), got up out of (hands on the wide arms, a long creak as the
    weight comes off, the slats springing back), and leant back in (two creaks as it settles deeper)."""
    if k % 3 == 0:
        parts = [(0, cloth(rng, k), -10), (0.08, weight(rng, k), 0), (0.1, creak(rng, k, 0.55, -1.0), -2),
                 (0.12, legs(rng, k), -8), (0.55, creak(rng, k + 1, 0.3, 0.5), -8)]
    elif k % 3 == 1:
        parts = [(0, creak(rng, k, 0.25, 0.0), -6), (0.18, cloth(rng, k), -10), (0.25, creak(rng, k + 1, 0.6, 1.0), 0),
                 (0.7, legs(rng, k), -10), (0.82, weight(rng, k + 1) * 0.3, -14)]
    else:
        parts = [(0, cloth(rng, k), -12), (0.05, creak(rng, k, 0.4, -0.5), -2), (0.5, creak(rng, k + 2, 0.35, -1.5), -5),
                 (0.55, legs(rng, k), -12)]
    return ck.place(parts)


def creak_rec(rng, take, L, rise):
    """The chair's slats and joints creaking: one of the packs' real wooden creaks, dropped a few semitones for a big
    slatted chair (house_folk.chair) and cut to L s, a little higher as the weight comes off (`rise` > 0) and lower as
    it comes on, wavering as the joints catch."""
    c = chair(rng, take, L, -rng.uniform(3, 5) + 1.5 * rise)
    return unit(dsp.wow(c, 15 + 20 * abs(rise), 2.0, rng))


def weight_rec(rng, take):
    """A body coming down on the slats: a real soft blow and a real wooden knock, dropped and choked (the weight in
    the low mids)."""
    soft = hit_of(SOFT[take % 5], 0, 0.2)
    knock = ck.choke(dsp.vari(hit_of(WOOD_M[take % 5], 0, 0.2), -rng.uniform(2, 4)), 0.02, 0.04)
    return unit(hp(ck.place([(0, unit(soft), 0), (0.004, unit(knock), -4)]), 150, 2))


def legs_rec(rng, take):
    """The chair's feet shifting on the deck boards: a real plank's knock, choked, and a short scrape."""
    k = ck.choke(dsp.vari(hit_of(PLANK[take % 5], 0, 0.2), rng.uniform(-1, 2)), 0.015, 0.03)
    sl = ck.friction(rng, 0.12, 180, 300, 2500, 0.7, env([(0, 0), (0.02, 1), (0.12, 0)], 0.12))
    return unit(hp(ck.place([(0, unit(k), 0), (0.01, sl, -10)]), 200, 2))


@recipe(BBQ, "chair", "creak",
        "A Muskoka chair creaking as someone sits down in it, gets up out of it, or leans back",
        """From real recordings: a big wooden Adirondack chair's wide slats and joints creaking under the weight (the
        packs' real wooden creaks, dropped a few semitones for its size, a little lower as the weight comes on and
        higher as it comes off), the body coming down on the slats (a real soft blow and a real wooden knock, dropped
        and choked), its feet shifting on the deck boards (a real plank's knock and a short scrape), a coat moving (real
        cloth). One take sitting down, one getting up (hands on the wide arms, a long creak as the weight comes off),
        one leaning back deeper. Out on the engine's front at night.""",
        sources=CREAKS + SOFT + WOOD_M + PLANK + CLOTH, takes=3, lufs=-21)
def chair_creak(rng, k):
    y = sitting(rng, k, creak_rec, weight_rec, legs_rec, lambda r, t: ck.cloth(r, t, 0.25))
    return deck(hp(y, 150, 2), rng, 0.07)


def creak_model(rng, take, L, rise):
    """The chair's slats and joints creaking, modelled: stick-slip in its joints through the wide slats' modes, slowing
    as the weight comes on (`rise` < 0) or quickening as it comes off."""
    r0, r1 = (25, 55) if rise >= 0 else (50, 22)
    s = synth.creak(L, env([(0, r0), (L, r1)], L, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in SLATS], q=12,
                    jitter=0.4, grit=0.35)
    return unit(hp(s, 200)) * env([(0, 0), (0.03, 1), (L * 0.7, 0.85), (L, 0)], L)


def weight_model(rng, take):
    """A body coming down on the slats, modelled: a soft, broad blow on a wooden body (its low modes, a soft contact)."""
    return unit(hp(ck.choke(W.body(rng, rng.uniform(180, 220), W.PLATE, decay=0.08, length=0.3, contact=0.005, count=8),
                            0.03, 0.05), 150, 2))


def legs_model(rng, take):
    """The chair's feet shifting on the deck, modelled: a wooden knock (a plank's modes) and a short stick-slip scrape."""
    k = W.body(rng, rng.uniform(240, 300), W.PLATE, decay=0.05, length=0.2, contact=0.0015, count=8)
    k = ck.choke(unit(k), 0.012, 0.03)
    sl = ck.friction(rng, 0.12, 180, 300, 2500, 0.7, env([(0, 0), (0.02, 1), (0.12, 0)], 0.12))
    return unit(hp(ck.place([(0, k, 0), (0.01, sl, -10)]), 200, 2))


def cloth_model(rng, take):
    """A coat moving, modelled: a short breath of noise in cloth's broad band."""
    L = 0.25
    return dsp.shaped(unit(bp(synth.noise(L, rng, "pink"), 400, 4000)), [(0, 0), (0.05, 1), (L, 0)])


@recipe(BBQ, "chair", "slats",
        "A Muskoka chair creaking, modelled: its slats and joints under the weight, its feet on the deck",
        """Modelled: a big wooden Adirondack chair's joints in stick-slip, rung through its wide slats' modes
        (190 Hz-1.3 kHz, dry), slowing as the weight comes on and quickening as it comes off; the body coming down on
        the slats (a soft, broad blow on a wooden body), its feet shifting on the deck (a plank's knock and a short
        scrape), a coat moving. One take sitting down, one getting up, one leaning back deeper. Out on the engine's
        front at night.""",
        takes=3, lufs=-21)
def chair_slats(rng, k):
    y = sitting(rng, k, creak_model, weight_model, legs_model, cloth_model)
    return deck(hp(y, 150, 2), rng, 0.07)
