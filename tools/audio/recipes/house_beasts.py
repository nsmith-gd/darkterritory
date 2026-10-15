"""The house creatures that are the house, or live in its roof (queue #497, note 767; G1's #318 and #319, notes 585-586):
the Hollow House and the Hanger, as their design docs' §4 asks (docs/design/creatures/hollow-house.md, hanger.md). The
village's other two, the Lodger and the Householder, are in house_folk.py.

What each is decides the build:
- The Hollow House: one of the village's houses (TownKit: peeling plaster on a timber frame, boarded floors over a void, a
  slate roof, a brick chimney stack) that is itself the creature, a Venus flytrap. Its tell is the ground under it: a low
  groan coming up through the floor, the frame working, everything loose in the house rattling in its seat (sashes, the
  doors against their stops, the panes in their putty) and grit and plaster crumbs sifting down off the ceiling. It's felt
  as much as heard, but its weight is a groan and a rattle in the low mids, never only sub: held to 40 Hz-2 kHz with
  real content over 80 Hz, so a laptop carries it, low and slow where Tower Jaw's gnaw is percussive. Then its doors slam,
  one every 0.35 s, by nobody: each the house's own heavy plank door thrown shut from wide open as hard as it will go. No
  hand on it, no sneck lifted, no swollen foot dragged over the sill (the crew's own door, crew-house-door, is all of
  those): only the air of the swing and the hinges yelping at the speed of it, a bang with no give in it, the sneck
  slammed down and bouncing in its keeper, the leaf shuddering against its stops, the frame booming and dust shaken
  down. Then it sinks 2.4 m in 3 s: timber screaming as the frame racks, nails shrieking out of the joists, fibres
  popping, the earth heaving and grinding round its base. Then it comes down on whoever's inside: the frame's last
  scream and a beam going, the roof falling in (timbers, slates smashing, the windows bursting, the chimney's bricks),
  the walls folding in after it and the heap settling.
- The Hanger: a pale, bloated, spider-like thing clinging upside down to the rafters of a house's roof space, paying its
  strands out of a mouth underneath. Its house is quiet: a faint creak of the rafters as it shifts its weight and a soft
  wet clicking overhead, its mouthparts working, all of it heard through the ceiling (the laths take the top off, the
  roof space's hollow rings under it). Its telegraph is the strands: six thin, glistening threads quivering in the
  draught with a thin, taut hum like a wire in wind, held high (1.5-5 kHz). Never one tone and never a tune, so it can't
  be a whistle (the Whistler's, the engine's) or the Choir: six strands of different lengths, each a narrow band of noise
  that wavers and trembles, beating against the others, the draught locking them in and out. The grab is a sticky snap
  and the strand pulled tight with the rafters taking the weight; the haul a ratcheting creak of the rafters, catch by
  catch, and a wet reel; letting go a wet hiss and a scuttle of many thin legs back up into the roof.

Each cue has a candidate built from the packs' real recordings (wood, doors, latches, creaks, cracks, gravel, glass, a
real thunder roll slowed into the ground's rumble, wet steps cut into wet clicks) and one modelled (stick-slip through the
frame's and the rafters' modes, struck bodies, narrow bands of noise for the strands), so the two compare. Loops are
built periodic (events placed round the cycle, textures crossfaded into their own heads, circular filters and room), so
build.py's seam gives the cycle back exactly. The house's own room is a whisker of its walls: the game's room
(spaces.json `room`) does the rest.
"""

import numpy as np
from scipy import signal

import dsp
import src
import synth
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.beasts import unit, main_hit
from recipes.boarders import grain
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, R, S, K
from recipes.g1_ground import air, door_core, gasp_core, spiracles, splinter, struck
from recipes.g1_ground import rec as hit_rec
from recipes.g1_train import cband, creak_rec, ext, loopify, squish, tack, tighten
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.moose_tells import burst
from recipes.stress import freeze, strongest

SR = dsp.SR
PEAK = -1.5

# ---- Bands (spec A.4: each tell held where it can be named, apart from the others) ------------------------------------------

RUMBLE = (40, 2000)       # the house's tell: the ground's weight under 80 Hz is felt; the groan, the rattle and the grit carry
SLAM = (80, 4000)         # its doors: the leaf's boom and the frame's shudder, the sneck's iron at the top
SINK = (40, 4000)         # timber screaming and nails shrieking over the earth heaving
FALL = (40, 6000)         # the roof and walls coming down: timbers, slates, glass, brick
RAFTERS = (200, 5000)     # the Hanger's house: the rafters' creak and the wet clicks, through the ceiling
HUM = (1500, 5000)        # its strands: a wire's whine, thin and high

# ---- Sources ---------------------------------------------------------------------------------------------------------------

THUNDER = S("thunder_01")[0]                          # a real thunder roll: slowed, the ground working under the house
CREAKS = R("creak1", "creak2", "creak3")              # real creaks: a bright squeak; two hinges' squeals near 1.1 kHz
DOORS = R("doorOpen_1", "doorOpen_2")                 # real door creaks: big, slow wood
SLAMS = S("door_03", "misc_36")                       # a real door slammed; a heavy knock-slam
LATCHES = S("door_02", "door_04", "door_05") + R("metalLatch")   # real latches dropping
WOOD_H = K("impactWood_heavy")                        # heavy blows on timber
WOOD_M = K("impactWood_medium")                       # a block of wood knocked
WOOD_L = K("impactWood_light")                        # light wood knocks: a sash, a door against its stop
DECK = S("wood_hit_01", "wood_hit_02")                # heavy blows on planking
PLANK = K("impactPlank_medium")                       # a plank's hollow knock
CRACKS = S("misc_34", "misc_35")                      # real wood cracking and breaking
STONES = S("stones_01", "stones_02", "stones_03")     # real stones knocked and scraped
GRAVEL = STONES + S("footstep_01", "footstep_02")     # stones and gravel steps: cut into grit, clods and soil
MINING = K("impactMining")                            # a pick into rock: a heavy dull strike (bricks, a chimney's stones)
GLASS = S("glass_01", "glass_04", "glass_06")         # real glass knocks: a pane's ring
SHATTER = S("misc_26", "misc_27", "glass_03")         # real glass breaking and shattering
SCRAPE = "sfx_100_v2:misc_10"                         # a real metal scrape, a few strokes
WET = S("footstep_wet_01", "footstep_wet_03") + R("handleCoins")   # wet steps; a chewing squish
STRAP = R("handleSmallLeather2")[0]                   # a real leather strap dragged taut
WHIP = S("switch_01", "switch_02")                    # a real switch cracked through the air
GASP = "sfx_100_v2:misc_05"                           # a real gasp: air forced through a mouth (g1_ground.gasp_core)
STEPS = K("footstep_wood")                            # real steps on boards
METAL_L = K("impactMetal_light")                      # light metal struck: thin, ringing
RINGING = S("glass_05", "glass_02")                   # struck glass that rings on

# ---- Bodies (Hz) -----------------------------------------------------------------------------------------------------------

HOUSE = [78, 118, 172, 248, 355, 505, 720, 1030]      # a timber-framed cottage: sills, joists and studs over the floor's void
LEDGED = [128, 225, 370, 590, 900, 1380, 2050]        # its front door: ledged deal planks 4 cm thick, heavier than the crew's
SASH = [410, 720, 1150, 1700, 2450]                   # a sash or a door rattling against its stop: small, light deal
PANE = [1150, 1830, 2700, 3900]                       # a small window pane loose in old putty
SNECK = [2450, 3720, 5800]                            # a thumb latch's iron bar
HINGE = [1300, 2100, 3200, 4600]                      # a strap hinge's iron, turned fast
NAIL = [1500, 2350, 3400, 4900]                       # a cut nail's shank shrieking in its hole, a size up from a floorboard's
TIMBER = [96, 150, 228, 335, 480, 690, 960]           # the roof's purlins and tie beams coming down: their bending modes
RAFTER = [190, 340, 560, 830, 1200, 1700]             # a 4 by 6 inch rafter, overhead
ROOF = [230, 410, 690]                                # the roof space over the ceiling: its hollow

# Which real creak makes which timber, and how far it's dropped: the door creaks are big slow wood (their strongest
# partials 140-260 Hz), creak2 and creak3 hinges' squeals near 1.1 kHz; creak1, a bright squeak, only yelps as a hinge.
SCREAMS = [(CREAKS[1], (-8.0, -4.0)), (CREAKS[2], (-8.0, -4.0)), (DOORS[0], (-3.0, 0.0)), (DOORS[1], (-3.0, 0.0))]
BEAMS = [(DOORS[0], (-8.0, -4.0)), (DOORS[1], (-8.0, -4.0)), (CREAKS[2], (-13.0, -10.0))]


# ---- Shared parts ------------------------------------------------------------------------------------------------------------

def dry(y):
    """A whisker of the house's own walls and floor close by: the room's reverb is the game's (spaces.json `room`)."""
    return ck.space(y, 0.14, 0.1, 5500, ((0.003, 0.4), (0.008, 0.25)))


def hold(x, band, order=2):
    """A one-shot held to its tell band, gently and zero phase, so its attacks stay put."""
    return dsp.band(np.asarray(x, np.float32), *band, order=order)


def clp(x, fc, order=2):
    """A circular lowpass (zero phase), for a loop's parts: it keeps a loop a loop."""
    return W.cfilter(x, lambda f: 1 / np.sqrt(1 + (f / fc) ** (2 * order)))


def close(y, rng, band=None, wet=0.12, drive=3.0, lufs=-20.0, order=4, room="car"):
    """A loop's last step (g1_train.close, indoors): a small wooden room round the cycle (a circular reverb), held to its
    band with its loudest peaks rounded off between two passes of a circular band filter, limited round three cycles
    (keeping the middle, so the limiter's gain loops too), and handed to build.py seamless."""
    y = W.croom(np.asarray(y, np.float32), room, wet, rng)
    if band:
        y = cband(dsp.saturate(W.norm(cband(y, *band, order)), drive), *band, order)
    else:
        y = W.cfilter(y, lambda f: (f / 40) ** 4 / (1 + (f / 40) ** 4))
    n = len(y)
    return W.seamless(finish(np.tile(y, 3), lufs, -4.5)[n:2 * n])


def heave(n, rng, rate=0.4, floor=0.15):
    """The ground working under the house over a loop, `floor`-1: slow swells, nothing faster than `rate` Hz, periodic."""
    h = W.slow(n, rate, rng)
    h = (h - np.min(h)) / (np.max(h) - np.min(h) + 1e-9)
    return (floor + (1 - floor) * h).astype(np.float32)


def summits(c, k, gap=1.2):
    """The `k` highest swells of a loop's curve, as times (s) at least `gap` apart (spread evenly if it has fewer)."""
    d = c[::480]
    pk, _ = signal.find_peaks(np.concatenate([d, d[:1]]), distance=max(1, int(gap * 100)))
    pk = sorted({int(p) % len(d) for p in pk})
    top = sorted(pk, key=lambda p: -d[p])[:k]
    while len(top) < k:
        top.append(int(len(d) * (len(top) + 0.5) / k))
    return sorted(p / 100 for p in top)


def cloud(rng, key, n, semis, grain_s=1.0):
    """A recording held out over a loop as overlapping grains (Hann windows half a grain apart, so they sum flat), each
    cut from a loud stretch of it somewhere else and set to the same level: its texture and its lumps kept, its length
    and its own fades gone; periodic over n."""
    x = src.get(key)
    x = dsp.vari(x, semis) if semis else x
    count = max(2, int(round(n / samples(grain_s / 2))))
    hop = n / count
    g = int(round(2 * hop))
    win = (0.5 - 0.5 * np.cos(2 * np.pi * np.arange(g) / g)).astype(np.float32)
    e = lp(np.abs(x), 4)
    ok = np.nonzero(e[:max(1, len(x) - g)] > 0.3 * np.max(e))[0]
    ev = []
    for i in range(count):
        a = int(ok[int(rng.integers(len(ok)))]) if len(ok) else 0
        seg = dsp.fit(x[a:a + g], g)
        ev.append((round(i * hop) / SR, seg / (np.sqrt(np.mean(seg ** 2)) + 1e-9) * win, 1.0))
    return W.place(n, ev)


def rattle(rng, length, rate, keys, semis=(0.0, 4.0), piece=0.02, tau=0.006, band=(250, 3500)):
    """Something loose shaking in its seat (a sash in its frame, a door against its stop, a pane in its putty): real
    knocks cut to their first few milliseconds and choked, fired at `rate` a second (a constant or a curve; a shudder
    runs 10-30 a second), uneven in time and size, a few loud and many quiet. `length` s and the last one's tail."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + piece + 0.05)
    t = rng.exponential(1 / max(float(rc[0]), 0.5))
    while t < length:
        x = grain(rng, keys[int(rng.integers(len(keys)))], piece, rng.uniform(*semis), decay=tau)
        b.at(t, unit(bp(x, *band)), 20 * np.log10(rng.uniform(0.12, 1.0) ** 1.5))
        t += (0.5 + 0.5 * rng.exponential()) / max(float(rc[min(samples(t), n - 1)]), 0.5)
    return b.x


def knocks(rng, length, rate, modes, q=5.0, tail=0.03, spread=0.08, hit=0.0015):
    """The same, modelled: each knock a tick rung through the loose part's own modes (a little different each time, as
    it sits differently in its seat), at `rate` a second, a few loud and many quiet."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + tail + 0.05)
    t = rng.exponential(1 / max(float(rc[0]), 0.5))
    while t < length:
        x = struck(rng, [m * rng.uniform(1 - spread, 1 + spread) for m in modes], q, tail, hit)
        b.at(t, x, 20 * np.log10(rng.uniform(0.12, 1.0) ** 1.5))
        t += (0.5 + 0.5 * rng.exponential()) / max(float(rc[min(samples(t), n - 1)]), 0.5)
    return b.x


def strew(rng, keys, length, rate, semis=(-4.0, 1.0), size=(0.008, 0.03)):
    """Real recordings cut into grains at their attacks (stones knocked, gravel crunched) and strewn at `rate` a second
    (a constant or a curve), a few loud and many quiet: `length` s and the last grain's whole tail, so a loop can fold it
    round instead of cutting it off at the seam."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + size[1] + 0.01)
    t = rng.exponential(1 / max(float(rc[0]), 1))
    while t < length:
        g = grain(rng, keys[int(rng.integers(len(keys)))], rng.uniform(*size), rng.uniform(*semis))
        b.at(t, unit(g), 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5))
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 1))
    return b.x


def grit(rng, length, rate, semis=(2.0, 7.0)):
    """Grit and plaster crumbs pattering down onto the boards and the furniture: real gravel cut to its tiniest crunches
    and pitched up for grains a few millimetres across, strewn at `rate` a second (a constant or a curve)."""
    return unit(strew(rng, GRAVEL, length, rate, semis, size=(0.004, 0.012)))


def sift(rng, length, rate, f=(900, 4500)):
    """Grit and plaster dust sifting down, modelled: hard little grains clicking on the boards at `rate` a second (a
    constant or a curve), over the fine dry hiss of the dust."""
    n = samples(length)
    rc = synth.curve(rate, n)
    out = np.zeros(n + samples(0.03), np.float32)
    t = rng.exponential(1 / max(float(rc[0]), 0.5))
    while t < length:
        c = synth.click(np.exp(rng.uniform(np.log(f[0]), np.log(f[1]))), q=rng.uniform(3, 8),
                        length=rng.uniform(0.003, 0.01), rng=rng)
        a = samples(t)
        out[a:a + len(c)] += c * rng.uniform(0.1, 1.0) ** 2
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 0.5))
    dust = synth.rustle(length, rc * 4 + 5, rng, f=(1500, 6000), ticks=0)
    return mix(unit(out), unit(dust) * 0.3)


def trickles(rng, n, times, make, length=(0.6, 1.4), rate=(60, 140), gain=1.0):
    """Grit let down off the ceiling after each swell at `times` (s): a trickle that starts thick and thins, placed
    round the loop."""
    ev = []
    for t in times:
        D = rng.uniform(*length)
        r = rng.uniform(*rate)
        ev.append((t + rng.uniform(0.05, 0.25), make(rng, D, env([(0, 0), (0.06, r), (D * 0.4, r * 0.5), (D, 0)], D)),
                   gain * rng.uniform(0.6, 1.0)))
    return W.place(n, ev)


def groan_rec(rng, key, n, semis, sag):
    """A real door creak held out into one long groan over a loop (paulstretched: its grain kept, its jerks gone),
    dropped to a house frame's size and bent by `sag` (semitones, a periodic per-sample curve) as the load comes and
    goes; crossfaded round into its own head."""
    x = door_core(key)
    a = rng.uniform(0, max(0.0, len(x) / SR - 0.3))
    seg = dsp.vari(dsp.trim(x, a, 0.3), semis)
    m = n + samples(0.5)
    held = dsp.smear(seg, (m / SR * 1.3 + 0.5) / (len(seg) / SR), 0.25, rng)
    return loopify(dsp.fit(bend(held, ext(sag, n, 0.5)), m), n)


def boom(rng, length=0.4):
    """The whole frame taking a blow: the house's low modes rung once (its sills and joists over the void under the
    floor), dull, a boom without a pitch."""
    return ck.hollow(rng, HOUSE[1:5], length, q=4)


def scream_rec(rng, length, i):
    """One timber screaming as the frame racks: a real creak held out (g1_train.creak_rec) and dropped (SCREAMS), bent up
    as the strain builds and easing as it gives, swelling in."""
    key, semis = SCREAMS[i % len(SCREAMS)]
    c = creak_rec(rng, length * 1.35, rng.uniform(*semis), key)
    up = rng.uniform(2.0, 4.0)
    rise = env([(0, 0), (length * 0.75, up), (length * 1.35, up - rng.uniform(1, 2))], length * 1.35)
    y = dsp.fit(bend(c, rise), samples(length))
    return y * env([(0, 0), (length * 0.3, 0.75), (length * 0.75, 1), (length, 0)], length)


def beam_rec(rng, length, i):
    """A rafter or a joist creaking under a weight that shifts: a real door's creak dropped a few semitones, or a hinge's
    dropped an octave (BEAMS), held out to `length` (g1_train.creak_rec, crisp, so the slips stay slips)."""
    key, semis = BEAMS[i % len(BEAMS)]
    return creak_rec(rng, length, rng.uniform(*semis), key)


def scream(rng, length, scale=1.0):
    """One timber screaming as the frame racks, modelled: stick-slip so fast it has a pitch (100-400 slips a second,
    gliding up as the strain builds and falling as it gives), steady enough to sing, rung through a board's modes scaled
    to the member."""
    rate = env([(0, rng.uniform(90, 140)), (length * 0.7, rng.uniform(260, 420)), (length, rng.uniform(150, 220))], length)
    c = synth.creak(length, rate, rng, body=[f * scale * rng.uniform(0.95, 1.05) for f in synth.WOOD], q=22, jitter=0.12,
                    grit=0.2)
    return hp(c, 150) * env([(0, 0), (length * 0.3, 0.7), (length * 0.75, 1), (length, 0)], length)


def nail(rng, length):
    """A cut nail shrieking out of its joist as the frame racks: iron stick-slip rising as it comes, rung through its
    shank (house_search's, a size bigger)."""
    s = synth.creak(length, env([(0, 70), (length, 220)], length, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in NAIL],
                    q=30, jitter=0.2, grit=0.25)
    return unit(hp(s, 900)) * env([(0, 0), (0.05, 0.7), (length * 0.7, 1), (length, 0)], length)


def overhead(x):
    """Heard through the ceiling: the laths and boards take the top off, and the roof space's hollow rings under it."""
    y = lp(np.asarray(x, np.float32), 3500, 2)
    r = dsp.resonate(y, ROOF, q=4, gains=[1, 0.6, 0.4])
    return mix(y, r * 0.3 * float(np.std(y) / (np.std(r) + 1e-9)))


def wet_tick(rng, semis=(2.0, 6.0), length=(0.008, 0.02)):
    """One soft wet click: a real wet sound cut to a few milliseconds of its attack and pitched up small."""
    return unit(grain(rng, WET[int(rng.integers(len(WET)))], rng.uniform(*length), rng.uniform(*semis), decay=0.004))


def wet_click(rng, f=(900, 3200)):
    """One soft wet click, modelled: mouthparts parting in slime (a short burst of noise through a resonance that jumps
    up as it parts) and the little bubble of air let in behind it."""
    L = rng.uniform(0.006, 0.018)
    f0 = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
    y = dsp.sweep_filter(synth.noise(L, rng), "bp", env([(0, f0 * 0.6), (L, f0 * 1.8)], L, "exp"), q=rng.uniform(5, 9),
                         block=32) * env([(0, 0), (0.0008, 1), (L, 0)], L)
    b = synth.bubble(f0 * rng.uniform(0.7, 1.1), rise=rng.uniform(0.1, 0.3), amp=0.35)
    return unit(mix(unit(y), b))


def clicking(rng, length, runs, make, rate=(7, 14), count=(3, 9)):
    """Its mouthparts working the strands overhead: `runs` short runs of soft wet clicks (`make(rng)` each, 3-9 in a
    run at 7-14 a second, uneven), spread round `length` s (each somewhere in its own share, so no long silence)."""
    ev = []
    for i in range(runs):
        t = (i + rng.uniform(0.05, 0.8)) * length / runs
        r = rng.uniform(*rate)
        for j in range(int(rng.integers(*count))):
            ev.append((t + j / r * rng.uniform(0.8, 1.25), make(rng), rng.uniform(0.3, 1.0) * (0.8 if j % 2 else 1.0)))
    return ev


# ---- The Hollow House: the rumble (the tell) ----------------------------------------------------------------------------------

RUMBLE_L = 8.0


@recipe("tell-hollow-house", "rumble", "ground",
        "The ground rumbling under the house: a low groan up through the floor, the house rattling, grit sifting down",
        """Real recordings, bent: a real thunder roll slowed an octave and held out in overlapping grains is the ground
        working under the house, swelling and easing three or four times a cycle; two real door creaks held out
        (paulstretched) and dropped more than an octave are its frame groaning through the floor, sagging in pitch as
        each swell comes on. Everything loose in the house rattles with the swells (real light wood knocks for the sashes
        and the doors against their stops, a latch chattering, the panes buzzing in their putty: real glass knocks cut
        short), the floor booms dully at each peak, and after it grit and plaster crumbs patter down off the ceiling
        (real gravel cut to its tiniest crunches). 8 s exact cycle. Held to 40 Hz-2 kHz: the weight under 80 Hz is felt,
        the groan, the rattle and the grit are what a laptop carries.""",
        sources=[THUNDER] + DOORS + WOOD_L + LATCHES[:3] + GLASS + GRAVEL, takes=1, loop=True, band=RUMBLE, lufs=-20,
        seconds=RUMBLE_L)
def rumble_ground(rng, k):
    L = RUMBLE_L
    n = samples(L)
    h = heave(n, rng, 0.45, 0.45)
    earth = clp(cloud(rng, THUNDER, n, -12, 1.2), 240) * h ** 1.3
    groan = sum(groan_rec(rng, key, n, rng.uniform(-14, -12) + 3 * i, -2.0 * h + 0.5 * i) * (0.15 + 0.85 * h ** 1.5) * g
                for i, (key, g) in enumerate(zip(DOORS, (1.0, 0.6))))
    sash = dsp.wrap(rattle(rng, L, 2 + 24 * h ** 2.5, WOOD_L, (-3, 1), 0.02, 0.006, (180, 2200)), n)
    latch = dsp.wrap(rattle(rng, L, 0.5 + 10 * h ** 3, LATCHES[:3], (-4, -1), 0.02, 0.004, (500, 3000)), n)
    panes = dsp.wrap(rattle(rng, L, 0.5 + 40 * np.clip(h - 0.55, 0, None) ** 1.5, GLASS, (-7, -3), 0.012, 0.003,
                            (600, 3000)), n)
    tops = summits(h, 3)
    dust = trickles(rng, n, tops, grit)
    booms = W.place(n, [(t, boom(rng), rng.uniform(0.7, 1.0)) for t in tops])
    y = (W.norm(earth) * 0.6 + W.norm(groan) * 0.9 + W.norm(sash) * 0.4 + W.norm(latch) * 0.16 + W.norm(panes) * 0.14
         + W.norm(dust) * 0.3 + W.norm(booms) * 0.35)
    return close(y, rng, RUMBLE, wet=0.12, drive=3, lufs=-20)


@recipe("tell-hollow-house", "rumble", "frame",
        "The ground rumbling under the house, modelled: the frame groaning in slow swells, loose things rattling, grit",
        """Modelled from what's moving: a lumpy band of low noise (35-110 Hz) is the ground heaving under the house in
        three or four swells a cycle; its frame groans with it, stick-slip through a cottage's sills, joists and studs
        (120 Hz-1 kHz, tilted up so it's heard in the mids) quickening from a creak into a groan as each swell comes on,
        other joints creaking against it at their own pitches. The loose parts of the house shudder in their seats
        (sashes and doors knocking against their stops, the panes ringing in their putty, the sneck chattering), the
        floor booms at each peak, and grit clicks down off the ceiling over a fine sift of plaster dust. 8 s exact cycle.
        Held to 40 Hz-2 kHz.""",
        takes=1, loop=True, band=RUMBLE, lufs=-20, seconds=RUMBLE_L)
def rumble_frame(rng, k):
    L = RUMBLE_L
    n = samples(L)
    m = n + samples(0.5)
    h = heave(n, rng, 0.45, 0.45)
    lumps = np.clip(1 + 0.5 * W.slow(n, 5.0, rng), 0.2, None)
    earth = W.cfilter(W.pnoise(n, rng), lambda f: (f / 35) ** 2 / (1 + (f / 35) ** 2) / (1 + (f / 110) ** 3)) * h ** 1.3 * lumps
    body = [f * rng.uniform(0.95, 1.05) for f in HOUSE[1:]]
    groan = loopify(synth.creak(m / SR, ext(22 + 70 * h ** 1.5, n, 0.5), rng, body=body, q=12, jitter=0.4, grit=0.4), n)
    # its low modes take most of a slip's energy: tilted up, so the groan is heard in the frame's mids, not felt in the sub
    groan = W.cfilter(groan, lambda f: np.clip((f / 160) ** 0.8, 0.3, 3.0)) * (0.12 + 0.88 * h ** 1.5)
    joints = []
    for t in rng.uniform(0, L, 4):
        D = rng.uniform(0.6, 1.4)
        c = synth.creak(D, env([(0, 25), (D * 0.6, 70), (D, 30)], D), rng, body=[f * rng.uniform(1.4, 2.2) for f in HOUSE[:6]],
                        q=14, jitter=0.35, grit=0.4)
        joints.append((t, hp(c, 90) * env([(0, 0), (D * 0.4, 1), (D, 0)], D), rng.uniform(0.3, 0.6)))
    sash = dsp.wrap(knocks(rng, L, 2 + 24 * h ** 2.5, SASH, q=6, tail=0.03), n)
    latch = dsp.wrap(knocks(rng, L, 0.5 + 10 * h ** 3, SNECK, q=20, tail=0.02, hit=0.0005), n)
    panes = dsp.wrap(knocks(rng, L, 0.5 + 40 * np.clip(h - 0.55, 0, None) ** 1.5, PANE, q=16, tail=0.05, hit=0.0006), n)
    tops = summits(h, 3)
    dust = trickles(rng, n, tops, sift)
    booms = W.place(n, [(t, boom(rng), rng.uniform(0.7, 1.0)) for t in tops])
    y = (W.norm(earth) * 0.5 + W.norm(groan) * 0.9 + W.norm(W.place(n, joints)) * 0.45 + W.norm(sash) * 0.45
         + W.norm(latch) * 0.16 + W.norm(panes) * 0.14 + W.norm(dust) * 0.3 + W.norm(booms) * 0.3)
    return close(y, rng, RUMBLE, wet=0.12, drive=3, lufs=-20)


# ---- The Hollow House: its doors slammed by nobody ------------------------------------------------------------------------------

SLAM_LUFS = -17.0


def swing(rng, k, real=True):
    """The leaf thrown round from wide open by nothing: the air it shoves ahead of it and its strap hinges yelping at the
    speed of it (a real creak cut to a yelp and pitched up, or iron stick-slip). No hand on the latch, no foot dragged
    over the sill: the crew's door has those."""
    b = Bus(0.2)
    b.at(0, ck.whoosh(rng, 0.11, 160, 1100, 0.85, q=0.8), -8)
    if real:
        c = dsp.vari(ck.align(ck.get(CREAKS[k % 3])), rng.uniform(2, 4))[:samples(0.07)]
        b.at(0.03, ck.norm(hp(dsp.fade(c, 0.005, 0.02), 500)), -16)
    else:
        L = 0.07
        y = synth.creak(L, env([(0, 220), (L, 520)], L), rng, body=[f * rng.uniform(0.9, 1.1) for f in HINGE], q=25, jitter=0.2)
        b.at(0.03, unit(hp(y, 600)) * env([(0, 0), (0.01, 1), (L, 0)], L), -17)
    return b.x


def bang_rec(rng, k):
    """The leaf into its frame as hard as it will go: the packs' real door slam (a first hit), a heavy knock-slam and
    heavy blows on planking under it, all dropped a few semitones for a heavy plank door and laid on each other so the
    blow has no give in it, driven a little."""
    b = Bus(0.8)
    b.at(0, dsp.vari(hit_of(SLAMS[0], k % 2, 0.6), rng.uniform(-4, -2)), 0)
    b.at(0.0015, dsp.vari(hit_of(SLAMS[1], 0, 0.5), rng.uniform(-3, -1)), -3)
    b.at(0.001, ck.choke(dsp.vari(hit_of(DECK[k % 2], 0, 0.5), rng.uniform(-4, -2)), 0.08, 0.06), -4)
    b.at(0.002, ck.choke(dsp.vari(hit_of(WOOD_H[(k + 2) % 5], 0, 0.4), rng.uniform(-3, -1)), 0.06, 0.05), -5)
    return dsp.saturate(ck.norm(b.x), 4)


def bang_model(rng):
    """The leaf into its frame, modelled: the door's ledged planks struck all over at once (a heavy wooden plate's modes,
    a wooden contact), the jamb post struck with it (a bar's modes), the hard edge of the meeting, driven a little."""
    b = Bus(0.8)
    leaf = W.body(rng, rng.uniform(105, 125), W.PLATE, decay=0.07, damp=2.5, contact=0.0012, tilt=0.6, count=12)
    b.at(0, unit(leaf), -8)            # under the planks: at -2 its 105-125 Hz fundamental was half the slam, a squish
    b.at(0, struck(rng, [f * rng.uniform(0.95, 1.05) for f in LEDGED], 7, 0.15, 0.0012), -3)
    f1 = rng.uniform(220, 270)
    b.at(0.001, struck(rng, [f1 * r for r in W.BAR[:4]], 10, 0.12, 0.001), -3)
    b.at(0, unit(hp(burst(rng, 0.003, attack=0.05), 900)), -1)
    b.at(0.0005, unit(bp(burst(rng, 0.012, attack=0.03), 1500, 5000)), -8)
    b.at(0, kit.thud(rng, rng.uniform(85, 100), 0.12, 1.0), -12)
    return dsp.saturate(ck.norm(b.x), 4)


def sneck(rng, k, real=True):
    """The thumb latch's bar slammed down over its keeper and bouncing: one hard click of iron (the packs' latch, or a
    bar's modes), then two or three smaller ones closer and closer together, choked."""
    if real:
        c = ck.choke(dsp.vari(hit_of(LATCHES[k % len(LATCHES)], 0, 0.12, -16), rng.uniform(-2.6, -1.4)), 0.02, 0.02)
    else:
        f = rng.uniform(0.95, 1.05)
        c = synth.click(SNECK[0] * f, q=18, length=0.04, rng=rng, body=[s * f for s in SNECK])
    parts, t, gap, g = [(0, c, 0)], 0.0, rng.uniform(0.035, 0.05), 0.0
    for _ in range(int(rng.integers(2, 4))):
        t += gap
        g -= rng.uniform(4, 7)
        gap *= rng.uniform(0.55, 0.7)
        parts.append((t, dsp.vari(c, rng.uniform(-0.5, 1.0)), g))
    return ck.place(parts)


def judder(rng, real=True, length=0.22, rate=30, glass=False):
    """The leaf shuddering against its stops after the blow (or a pane in it rattling in its putty): a fast run of
    small knocks, dying away."""
    r = env([(0, rate), (length, rate * 0.6)], length)
    if glass:
        x = rattle(rng, length, r, GLASS, (-6, -2), 0.012, 0.004, (600, 3500)) if real else \
            knocks(rng, length, r, PANE, q=16, tail=0.04, hit=0.0006)
    else:
        x = rattle(rng, length, r, WOOD_L, (0, 3), 0.02, 0.006, (200, 3000)) if real else knocks(rng, length, r, SASH, q=6)
    return dsp.shaped(unit(x), [(0, 1), (length, 0.05)], "exp")


def slam(rng, k, real):
    """One door of the house slammed shut: the swing, the bang (for take 2 a flam: the leaf comes off its stop a hair
    and is yanked back harder), the sneck dropping and bouncing, the leaf shuddering, the frame's boom, dust."""
    t0 = 0.1
    parts = [(0, swing(rng, k, real), 0)]
    bang = (lambda: bang_rec(rng, k)) if real else (lambda: bang_model(rng))
    if k % 3 == 2:
        parts.append((t0, bang(), -7))
        t0 += rng.uniform(0.06, 0.08)
    parts += [(t0, bang(), 0), (t0, boom(rng, 0.35), -8), (t0 + 0.004, sneck(rng, k, real), -6),
              (t0 + 0.015, judder(rng, real), -10)]
    if k % 3 == 1:
        parts.append((t0 + 0.01, judder(rng, real, 0.18, 34, glass=True), -13))
    D = rng.uniform(0.35, 0.5)
    dust = grit(rng, D, env([(0, 0), (0.03, 150), (D, 0)], D)) if real else sift(rng, D, env([(0, 0), (0.03, 150), (D, 0)], D))
    parts.append((t0 + 0.05, dust, -20))
    y = dry(ck.place(parts))
    if not real:                        # the modelled bang and the frame's boom: up to 62% under 150 Hz, a squish of a slam
        y = hp(y, 150, 4)
    return finish(hold(y, SLAM), SLAM_LUFS, PEAK)


@recipe("cs-hollow-house", "slam", "thrown",
        "A heavy house door thrown shut by nobody: no hand, a bang with no give, the sneck slammed down, the frame shaking",
        """Real recordings: the leaf swung from wide open by nothing, the air shoved ahead of it and its hinges yelping at
        the speed of it (a real creak cut short and pitched up), then the bang (the packs' real door slam, a heavy
        knock-slam and heavy blows on planking, dropped for a heavy plank door, laid on each other and driven), the sneck
        slammed down over its keeper and bouncing (a real latch), the leaf shuddering against its stops (real light wood
        knocks in a fast dying run), the frame's dull boom and grit shaken down. Nothing of the crew's door: no hinge
        creaked open, no sill dragged, no hand lifting the sneck. One plain; one with a pane rattling in its putty; one
        where the leaf comes off its stop a hair and is yanked back harder. Held to 80 Hz-4 kHz.""",
        sources=CREAKS + SLAMS + DECK + WOOD_H + LATCHES + WOOD_L + GLASS + GRAVEL, takes=3, band=SLAM, lufs=SLAM_LUFS)
def slam_thrown(rng, k):
    return slam(rng, k, True)


@recipe("cs-hollow-house", "slam", "leaf",
        "A heavy house door thrown shut by nobody, modelled: a ledged plank leaf struck into its jamb, the latch bouncing",
        """Modelled: the air of the swing and the strap hinges' iron yelping (stick-slip, fast), then the leaf into its
        frame (a heavy ledged deal door struck all over at once: a wooden plate's modes, its planks' own,
        the jamb post's bar modes, the hard edge of the meeting and the weight behind it, driven), the sneck's iron bar
        slammed down and bouncing in its keeper, the leaf shuddering against its stops (small deal knocks dying away),
        the frame's boom and grit clicking down. Same three performances as 'thrown'. Cut below 150 Hz, the leaf's
        fundamental kept under its planks: whole, it was half the slam, a squish.""",
        takes=3, band=SLAM, lufs=SLAM_LUFS)
def slam_leaf(rng, k):
    return slam(rng, k, False)


# ---- The Hollow House: sinking ---------------------------------------------------------------------------------------------

SINK_L = 8.0


@recipe("cs-hollow-house", "sink", "timber",
        "The house sinking: its timbers screaming as the frame racks, fibres popping, the earth heaving round its base",
        """Real recordings: six real wooden creaks a cycle held out and bent up as the strain builds (a timber screaming,
        never a hinge's squeak: hinges' squeals dropped half an octave, door creaks, overlapping), the whole frame groaning
        under them (a real door creak held out and dropped an octave, sagging with the load), fibres popping in between
        (real cracks cut small), a nail drawn shrieking out of a joist now and then (a real metal scrape pitched up); and
        under it the earth: a real thunder roll slowed further still for the ground heaving, real gravel dropped half an
        octave into clods grinding and tumbling round the base and pitched up into soil running into the gap, a foundation
        stone knocking (a pick on rock, dropped), and once a pane cracking in its racked frame. 8 s exact cycle. Held to
        40 Hz-4 kHz.""",
        sources=CREAKS + DOORS + CRACKS + [SCRAPE, THUNDER] + GRAVEL + MINING + GLASS, takes=1, loop=True, band=SINK,
        lufs=-20, seconds=SINK_L)
def sink_timber(rng, k):
    L = SINK_L
    n = samples(L)
    h = heave(n, rng, 0.6, 0.35)
    screams = []
    for i in range(6):
        D = rng.uniform(0.9, 1.8)
        screams.append(((i + rng.uniform(0, 0.6)) * L / 6, scream_rec(rng, D, i + k), rng.uniform(0.55, 1.0)))
    groan = groan_rec(rng, DOORS[k % 2], n, rng.uniform(-13, -11), -2.0 * h) * (0.3 + 0.7 * h)
    for t in rng.uniform(0, L, 2):
        x = dsp.vari(dsp.trim(src.get(SCRAPE), rng.uniform(0, 0.2), 0.3), rng.uniform(2, 4))
        screams.append((t, dsp.shaped(unit(hp(x, 900)), [(0, 0), (0.05, 1), (len(x) / SR, 0)]), 0.3))
    fibres = dsp.wrap(splinter(rng, L, 3 + 7 * h, (-6, -2)), n)
    ground = clp(cloud(rng, THUNDER, n, -14, 1.5), 200) * h
    clods = clp(dsp.wrap(strew(rng, GRAVEL, L, 40 + 160 * h, (-8, -3), (0.02, 0.06)), n), 2200)
    soil = clp(dsp.wrap(strew(rng, GRAVEL, L, 120 + 200 * h, (-2, 2), (0.008, 0.02)), n), 4000)
    stones = [(t, ck.choke(hit_rec(rng, MINING, rng.uniform(-7, -4), 0.4), 0.05, 0.08), rng.uniform(0.4, 0.8))
              for t in W.poisson(L, 1.0, rng)]
    pane = [(rng.uniform(0, L), ck.choke(hit_rec(rng, GLASS, rng.uniform(-3, 0), 0.2), 0.01, 0.03), 0.5)]
    y = (W.norm(W.place(n, screams)) + W.norm(groan) * 0.85 + W.norm(fibres) * 0.3 + W.norm(ground) * 0.55
         + W.norm(clods) * 0.6 + W.norm(soil) * 0.15 + W.norm(W.place(n, stones)) * 0.35 + W.norm(W.place(n, pane)) * 0.12)
    return close(y, rng, SINK, wet=0.1, drive=2.5, lufs=-20)


@recipe("cs-hollow-house", "sink", "racking",
        "The house sinking, modelled: the frame racking in long screams of stick-slip, nails shrieking, the earth grinding",
        """Modelled from how a frame fails as it's pulled down: its members screaming (stick-slip so fast it has a pitch,
        100-400 slips a second gliding up as the strain builds, rung through boards' modes from 270-550 Hz up, seven
        overlapping a cycle), the whole frame groaning under them (slow stick-slip through the cottage's low modes), nails
        shrieking out of the joists (iron stick-slip, rising), fibres crackling; and the earth round its base: a heaving
        band of low noise, clods knocking and rolling, the footings grinding down past the soil (rough low friction) and
        the wet ground folding in over them in slow sucks. 8 s exact cycle. Held to 40 Hz-4 kHz.""",
        takes=1, loop=True, band=SINK, lufs=-20, seconds=SINK_L)
def sink_racking(rng, k):
    L = SINK_L
    n = samples(L)
    m = n + samples(0.5)
    h = heave(n, rng, 0.6, 0.35)
    ev = []
    for i in range(7):
        D = rng.uniform(0.8, 1.7)
        ev.append(((i + rng.uniform(0, 0.6)) * L / 7, scream(rng, D, rng.uniform(1.5, 3.0)), rng.uniform(0.5, 1.0)))
    for t in rng.uniform(0, L, 3):
        ev.append((t, nail(rng, rng.uniform(0.25, 0.5)), rng.uniform(0.2, 0.35)))
    under = loopify(synth.creak(m / SR, ext(30 + 60 * h, n, 0.5), rng, body=[f * rng.uniform(0.95, 1.05) for f in HOUSE],
                                q=12, jitter=0.4, grit=0.4), n) * (0.3 + 0.7 * h)
    fibres = dsp.wrap(synth.crackle(L, 15 + 60 * h, rng, size=(0.0003, 0.003), hi=500), n)
    earth = W.cfilter(W.pnoise(n, rng), lambda f: (f / 35) ** 2 / (1 + (f / 35) ** 2) / (1 + (f / 140) ** 3))
    earth = earth * h * np.clip(1 + 0.5 * W.slow(n, 4.0, rng), 0.2, None)
    clods = dsp.wrap(knocks(rng, L, 20 + 60 * h, [260, 520, 900], q=3, tail=0.04, hit=0.004), n)
    grind = loopify(ck.friction(rng, m / SR, 60, 150, 1200, rough=0.9), n) * h
    sucks = [(t, ck.slurp(rng, rng.uniform(0.4, 0.7), 150, 600, bubbles=0.15), rng.uniform(0.4, 0.7)) for t in rng.uniform(0, L, 3)]
    y = (W.norm(W.place(n, ev)) + W.norm(under) * 0.4 + W.norm(fibres) * 0.2 + W.norm(earth) * 0.8 + W.norm(clods) * 0.3
         + W.norm(grind) * 0.3 + W.norm(W.place(n, sucks)) * 0.25)
    return close(y, rng, SINK, wet=0.1, drive=2.5, lufs=-20)


# ---- The Hollow House: the collapse --------------------------------------------------------------------------------------------

FALL_L = (3.2, 3.8)
FALL_LUFS = -16.0


def slates(rng, length, rate=90, real=True):
    """The roof's slates coming off and smashing: hard, ringing little slabs clattering down over each other and breaking,
    thick at first and thinning (real stones knocked and glass knocks dropped most of an octave, since a slate rings
    thicker and duller than glass; or a slab's modes struck)."""
    n = samples(length)
    rc = env([(0, rate), (length, rate * 0.05)], length)
    b = Bus(length + 0.2)
    t = 0.0
    while t < length:
        if real:
            key = (STONES + GLASS)[int(rng.integers(len(STONES) + len(GLASS)))]
            semis = rng.uniform(-8, -4) if key in GLASS else rng.uniform(-2, 3)
            x = unit(hp(grain(rng, key, rng.uniform(0.03, 0.08), semis, decay=rng.uniform(0.01, 0.03)), 300))
        else:
            f = rng.uniform(1100, 2400)
            x = struck(rng, [f, f * 1.62, f * 2.31, f * 3.1], rng.uniform(12, 20), 0.07, 0.0006)
        b.at(t, x, 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.3) - 8 * t / length)
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 1))
    return b.x


def bricks(rng, t0, span, count, real=True):
    """The chimney stack's bricks tumbling onto the heap one after another (a pick on rock dropped and choked, a crunch
    of grit with each; or a brick's dull modes struck), as [(t, x, db), ...]."""
    out = []
    for _ in range(count):
        t = t0 + span * rng.random() ** 1.3
        if real:
            x = ck.choke(hit_rec(rng, MINING, rng.uniform(-5, -1), 0.4), 0.05, 0.06)
        else:
            f = rng.uniform(350, 650)
            x = mix(struck(rng, [f, f * 2.1, f * 3.4], 4, 0.06, 0.002), ck.pad(rng, 0.06, 400) * 0.5)
        out += [(t, x, rng.uniform(-12, -4))]
    return out


def plaster(rng, length):
    """The plaster coming off the laths in sheets: a dry rushing crumble, dust and grit, thick and dying."""
    y = bp(synth.noise(length, rng, "pink"), 300, 3000) * env([(0, 0), (0.02, 1), (length * 0.3, 0.5), (length, 0)], length)
    c = synth.crackle(length, env([(0, 900), (length, 20)], length), rng, size=(0.0004, 0.004), hi=300)
    return mix(unit(y), unit(lp(c, 4000)) * 0.6)


@recipe("cs-hollow-house", "collapse", "crash",
        "The house coming down: a last scream of the frame, a beam going, the roof falling in, slates, glass, brick, walls",
        """Real recordings: the frame's last scream (a real creak bent up) and a beam going with a crack (real wood breaking,
        dropped), then the roof falls in: the air shoved out of the house in a deep whump (the head of a real thunder roll,
        slowed), real heavy blows on timber, door slams and blows on planking dropped and piled into one crash, beams
        cracking and splinters spraying, the slates smashing down in a shower (real stones and glass knocks dropped most of
        an octave), the windows bursting (real glass breaking), the chimney's bricks tumbling (a pick on rock, dropped) and
        the plaster coming off the laths in a dry rush; a moment later the walls fold in after it (more heavy blows and
        planks), and the heap settles (real wood, brick and glass pieces thinning out, grit trickling, a last beam
        slumping). Two takes, 3.2 and 3.8 s. Held to 40 Hz-6 kHz.""",
        sources=CREAKS + DOORS + CRACKS + [THUNDER] + WOOD_H + DECK + SLAMS + PLANK + STONES + GLASS + SHATTER + MINING + GRAVEL
        + WOOD_M + W.PIECES["wood"] + W.PIECES["brick"] + W.PIECES["glass"], takes=2, band=FALL, lufs=FALL_LUFS)
def collapse_crash(rng, k):
    L = FALL_L[k % 2] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    give = rng.uniform(0.4, 0.55)
    b.at(0, scream_rec(rng, give + 0.1, k), -6)
    b.at(0, splinter(rng, give, env([(0, 10), (give, 120)], give), (-6, -2)), -12)
    b.at(give - 0.03, unit(dsp.vari(main_hit(CRACKS[k % 2], 0.3), rng.uniform(-5, -3))), -2)
    t0 = give
    whump = unit(lp(dsp.vari(main_hit(THUNDER, 0.9), rng.uniform(-7, -5)), 250))
    b.at(t0 - 0.04, dsp.shaped(whump, [(0, 0), (0.03, 1), (0.9, 0)]), -3)
    b.at(t0, ck.pad(rng, 0.3, 180), -6)
    for i in range(6):                  # the roof's timbers and boards piling down in a flam
        b.at(t0 + i * rng.uniform(0.02, 0.06), hit_rec(rng, WOOD_H + DECK + SLAMS, rng.uniform(-7, -3), 0.5), -i * 1.2)
    for i in range(2):
        b.at(t0 + rng.uniform(0.0, 0.15), unit(dsp.vari(main_hit(CRACKS[i], 0.3), rng.uniform(-4, -1))), -3 - 2 * i)
    b.at(t0, splinter(rng, 0.8, env([(0, 400), (0.8, 20)], 0.8), (-4, 2)), -7)
    b.at(t0 + 0.03, slates(rng, 1.1, 90, True), -4)
    b.at(t0 + rng.uniform(0.06, 0.12), unit(dsp.vari(main_hit(SHATTER[k % 3], 0.6), rng.uniform(-2, 0))), -6)
    b.at(t0 + rng.uniform(0.25, 0.4), unit(dsp.vari(main_hit(SHATTER[(k + 1) % 3], 0.6), rng.uniform(-3, -1))), -11)
    b.at(t0 + 0.05, unit(mix(plaster(rng, 1.2), grit(rng, 1.2, env([(0, 300), (1.2, 10)], 1.2)) * 0.5)), -9)
    for t, x, db in bricks(rng, t0 + 0.2, 1.1, int(rng.integers(6, 10))):
        b.at(t, x, db)
    t1 = t0 + rng.uniform(0.65, 0.85)   # the walls folding in after it
    for i in range(3):
        b.at(t1 + i * rng.uniform(0.04, 0.09), hit_rec(rng, WOOD_H + DECK + PLANK, rng.uniform(-6, -2), 0.5), -3 - i * 1.5)
    b.at(t1 + 0.02, unit(dsp.vari(main_hit(CRACKS[(k + 1) % 2], 0.3), rng.uniform(-5, -2))), -6)
    b.at(t1, ck.pad(rng, 0.25, 200), -8)
    rest = max(0.4, L - t1 - 0.7)
    b.at(t1 + 0.15, W.debris(rng, rest, 14, {"wood": 3, "brick": 2, "glass": 0.6}, decay=0.6, semis=(-5, 1)), -5)
    b.at(t1 + 0.3, grit(rng, L - t1 - 0.3, env([(0, 80), (L - t1 - 0.3, 5)], L - t1 - 0.3)), -16)
    b.at(L - 0.75, mix(unit(beam_rec(rng, 0.35, k)) * dsp.fit(env([(0, 0), (0.1, 1), (0.35, 0)], 0.35), samples(0.35)) * 0.5,
                       ck.choke(hit_rec(rng, WOOD_M, -4, 0.3), 0.05, 0.06)), -11)
    y = hp(air(b.x, rng, 0.1, 0.5), 80, 2)  # the dropped thunder's whump put 55-58% under 150 Hz: weight, not a squelch
    y = dsp.fade(dsp.fit(y, samples(L)), 0.0, 0.3)
    return finish(hold(y, FALL), FALL_LUFS, -1.0)


@recipe("cs-hollow-house", "collapse", "fall",
        "The house coming down, modelled: the frame giving, the air shoved out, timbers, slates, glass and brick, settling",
        """Modelled from what falls: the frame's last seconds (its members' stick-slip quickening into a scream, fibres
        going faster and faster), then the roof falls in: the air in the house shoved out in one deep pressure pulse, the
        purlins and tie beams struck all at once (their bending modes, heavy and dull), rafters snapping (hard impulses
        through a bar's modes), splinters crackling, the slates smashing down in a shower (small hard slabs' modes, thick
        and thinning), the windows bursting (glass shards' bright modes over a hiss of breaking), the chimney's bricks
        knocking down one after another and the plaster rushing off the laths; the walls fold in a moment later, and the
        heap settles (boards, slates and bricks thinning out, grit, a last beam groaning down). Drier and clearer than
        'crash'. Two takes, 3.2 and 3.8 s. Held to 40 Hz-6 kHz.""",
        takes=2, band=FALL, lufs=FALL_LUFS)
def collapse_fall(rng, k):
    L = FALL_L[k % 2] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    give = rng.uniform(0.4, 0.55)
    b.at(0, scream(rng, give + 0.05, rng.uniform(0.6, 0.9)) * env([(0, 0.4), (give, 1)], give + 0.05), -6)
    b.at(0, unit(lp(synth.crackle(give, env([(0, 10), (give, 300)], give), rng, size=(0.0004, 0.002), hi=300), 4000)), -12)
    t0 = give
    b.at(t0 - 0.02, unit(lp(W.boom(rng, 1.0, f=48, T=0.03, crack=0.15), 500)), -3)
    for i in range(5):                  # the purlins and tie beams coming down one on another
        f = rng.uniform(0.75, 1.0)
        b.at(t0 + i * rng.uniform(0.02, 0.06), struck(rng, [m * f for m in TIMBER], 6, 0.4, 0.004), -i * 1.5)
    for i in range(3):                  # rafters snapping
        f1 = rng.uniform(160, 240)
        b.at(t0 + rng.uniform(0.0, 0.18), struck(rng, [f1 * m for m in W.BAR[:4]], 14, 0.3, 0.001), -3 - i)
        b.at(t0 + rng.uniform(0.0, 0.18), unit(hp(burst(rng, 0.004, attack=0.05), 1200)), -6)
    b.at(t0, unit(lp(synth.crackle(0.7, env([(0, 3000), (0.7, 50)], 0.7), rng, size=(0.0003, 0.002), hi=400), 6000)), -7)
    b.at(t0 + 0.03, slates(rng, 1.1, 90, False), -4)
    for w, at in enumerate((rng.uniform(0.06, 0.12), rng.uniform(0.25, 0.4))):     # the windows bursting
        g = Bus(0.8)
        g.at(0, unit(hp(burst(rng, 0.03, attack=0.02), 2500)) * 0.7, 0)
        for _ in range(int(rng.integers(8, 14))):
            g.at(rng.uniform(0, 0.25) ** 1.5, unit(W.body(rng, rng.uniform(2200, 4200), W.PLATE, decay=0.08, contact=0.0002, count=8)),
                 rng.uniform(-14, -3))
        b.at(t0 + at, g.x, -6 - 5 * w)
    b.at(t0 + 0.05, plaster(rng, 1.2), -9)
    for t, x, db in bricks(rng, t0 + 0.2, 1.1, int(rng.integers(6, 10)), real=False):
        b.at(t, x, db)
    t1 = t0 + rng.uniform(0.65, 0.85)   # the walls folding in after it
    for i in range(3):
        f = rng.uniform(0.9, 1.3)
        b.at(t1 + i * rng.uniform(0.04, 0.09), struck(rng, [m * f for m in TIMBER], 6, 0.3, 0.004), -3 - i * 1.5)
    b.at(t1, unit(lp(synth.crackle(0.4, env([(0, 1500), (0.4, 30)], 0.4), rng, size=(0.0003, 0.002), hi=400), 5000)), -9)
    t = t1 + 0.15
    gap = 0.04
    while t < L - 0.5:                  # boards, slates and bricks coming to rest, thinning
        kind = rng.random()
        if kind < 0.5:
            x = struck(rng, [rng.uniform(300, 520) * m for m in (1.0, 2.756, 5.404)], 9, 0.1)
        elif kind < 0.8:
            f = rng.uniform(1100, 2400)
            x = struck(rng, [f, f * 1.62, f * 2.31], 16, 0.06, 0.0006)
        else:
            f = rng.uniform(350, 650)
            x = struck(rng, [f, f * 2.1, f * 3.4], 4, 0.06, 0.002)
        b.at(t, x, rng.uniform(-16, -6) - 6 * (t - t1))
        t += gap * rng.uniform(0.6, 1.6)
        gap *= rng.uniform(1.12, 1.3)
    b.at(t1 + 0.3, sift(rng, L - t1 - 0.3, env([(0, 80), (L - t1 - 0.3, 5)], L - t1 - 0.3)), -16)
    D = 0.45
    b.at(L - 0.75, unit(synth.creak(D, env([(0, 15), (D, 40)], D), rng, body=HOUSE[2:], q=12, jitter=0.4))
         * env([(0, 0), (0.1, 1), (D, 0)], D), -12)
    y = air(b.x, rng, 0.1, 0.5)
    y = dsp.fade(dsp.fit(y, samples(L)), 0.0, 0.3)
    return finish(hold(y, FALL), FALL_LUFS, -1.0)


# ---- The Hanger: its house (the tell) and its strands (the telegraph) ----------------------------------------------------------

RAFTERS_L = 10.0


@recipe("tell-hanger", "rafters", "beams",
        "Overhead in the house: a faint creak of the rafters as something shifts its weight, a soft wet clicking",
        """Real recordings, heard through the ceiling: four real wooden creaks a cycle, cut at their loudest and held out
        (crisp, so the slips stay slips), door creaks dropped four to eight semitones and a hinge's an octave, for a rafter
        taking a weight that shifts along it; between them, never more than a second or so apart, short runs of soft wet
        clicks (real wet steps and a chewing squish cut to a few milliseconds and pitched up small, three to nine in a
        run): its mouthparts working the strands; once a cycle a little grit sifting down. All of it through the laths
        (the top taken off) with the roof space's hollow ringing under it. Faint: a house that's too quiet. 10 s exact
        cycle. Held to 200 Hz-5 kHz.""",
        sources=DOORS + CREAKS[2:] + WET + GRAVEL, takes=1, loop=True, band=RAFTERS, lufs=-26, seconds=RAFTERS_L)
def rafters_beams(rng, k):
    L = RAFTERS_L
    n = samples(L)
    ev = []
    for i in range(4):
        D = rng.uniform(0.5, 1.3)
        c = beam_rec(rng, D, i + k)
        ev.append(((i + rng.uniform(0.1, 0.7)) * L / 4, c * env([(0, 0), (D * 0.35, 1), (D, 0)], D), rng.uniform(0.4, 1.0)))
    clicks = clicking(rng, L, int(rng.integers(7, 11)), wet_tick)
    t = rng.uniform(0, L)
    dust = [(t, grit(rng, 0.8, env([(0, 0), (0.05, 60), (0.8, 0)], 0.8)), 0.2)]
    y = W.norm(W.place(n, ev)) * 0.8 + W.norm(W.place(n, clicks)) * 0.55 + W.norm(W.place(n, dust)) * 0.12
    return close(W.cyclic(overhead, y), rng, RAFTERS, wet=0.15, drive=1.5, lufs=-26, order=2)


@recipe("tell-hanger", "rafters", "sac",
        "Overhead in the house, modelled: rafters creaking under a shifting weight, soft wet clicks, a wet shift of its body",
        """Modelled: slow stick-slip through a rafter's modes (190 Hz-1.7 kHz, 12-40 slips a second, so a creak, never a
        groan), four times a cycle as the weight on the beam shifts; short runs of soft wet clicks (a burst of noise
        through a resonance that jumps up as the mouthparts part in slime, the little bubble let in behind it); once or
        twice its soft bulk shifting against the boards (wet noise opening and closing). Through the ceiling, faint.
        10 s exact cycle. Held to 200 Hz-5 kHz.""",
        takes=1, loop=True, band=RAFTERS, lufs=-26, seconds=RAFTERS_L)
def rafters_sac(rng, k):
    L = RAFTERS_L
    n = samples(L)
    ev = []
    for i in range(4):
        D = rng.uniform(0.5, 1.3)
        c = synth.creak(D, env([(0, 12), (D * 0.5, rng.uniform(25, 40)), (D, 15)], D), rng,
                        body=[f * rng.uniform(1.0, 1.3) for f in RAFTER], q=14, jitter=0.45, grit=0.3)
        ev.append(((i + rng.uniform(0.1, 0.7)) * L / 4, hp(c, 150) * env([(0, 0), (D * 0.35, 1), (D, 0)], D), rng.uniform(0.4, 1.0)))
    clicks = clicking(rng, L, int(rng.integers(7, 11)), wet_click)
    shifts = [(t, squish(rng, rng.uniform(0.3, 0.5), 300, 1200), 0.25) for t in rng.uniform(0, L, int(rng.integers(1, 3)))]
    y = W.norm(W.place(n, ev)) * 0.8 + W.norm(W.place(n, clicks)) * 0.55 + W.norm(W.place(n, shifts)) * 0.3
    return close(W.cyclic(overhead, y), rng, RAFTERS, wet=0.15, drive=1.5, lufs=-26, order=2)


HUM_L = 8.0


def pitches(rng, count=6, lo=1700, hi=4300, apart=1.05):
    """The strands' pitches: `count` across lo-hi Hz, none within 5% of another (so no two in unison) and drawn at
    random, so they make no chord and no tune."""
    out = []
    while len(out) < count:
        f = float(np.exp(rng.uniform(np.log(lo), np.log(hi))))
        if all(max(f, g) / min(f, g) > apart for g in out):
            out.append(f)
    return sorted(out)


def quiver(n, rng, rate=10.0):
    """A strand trembling, and the draught on it: a fast uneven tremble (`rate` Hz) under slow swells that lock it in
    and out; periodic over n."""
    trem = np.clip(1 + 0.45 * W.slow(n, rate, rng), 0.1, None)
    lock = np.clip(0.35 + 0.65 * W.slow(n, 0.25, rng), 0.05, None) ** 1.5
    return (trem * lock).astype(np.float32)


def strand(n, rng, f, q):
    """One strand singing in the draught, modelled: the wake shed off it locked to its own vibration, so a narrow band of
    noise at its pitch (a whine with breath in it, not a tone), its second mode faint above; the pitch wavering between
    two close bands as the draught shifts, trembling as it quivers; periodic over n."""
    def band_at(fc, qq):
        return W.cfilter(W.pnoise(n, rng), lambda F: 1 / np.sqrt(1 + (qq * (F / fc - fc / F)) ** 2))
    sway = 0.5 + 0.5 * np.tanh(1.5 * W.slow(n, 0.6, rng))
    y = band_at(f * 0.994, q) * sway + band_at(f * 1.006, q) * (1 - sway)
    if 2 * f < 6000:
        y = y + 0.25 * band_at(2 * f, q * 1.5)
    return W.norm(y * quiver(n, rng, rng.uniform(7, 13)))


@recipe("tell-hanger-strands", "hum", "wire",
        "Its strands quivering: a thin, taut hum like a wire in the wind, six strands beating, trembling, never a tone",
        """Modelled from a wire singing in a draught: each of six strands of different lengths sheds a wake that locks to
        its own vibration, so a narrow band of noise at its pitch (1.7-4.3 kHz, drawn at random and none in unison, so no
        chord and no tune), a whine with breath in it rather than a tone, its second mode faint above; its pitch wavers
        between two close bands as the draught shifts, it trembles at 7-13 Hz as it quivers, and the draught locks each
        strand in and out on its own slow swells, so they beat against each other and the hum is never still. A faint
        breath of the draught round them. Thin and taut, nothing like a whistle or the Choir. 8 s exact cycle. Held to
        1.5-5 kHz.""",
        takes=1, loop=True, band=HUM, lufs=-24, seconds=HUM_L)
def hum_wire(rng, k):
    n = samples(HUM_L)
    y = sum(strand(n, rng, f, rng.uniform(70, 140)) * rng.uniform(0.5, 1.0) / (1 + 0.15 * i)
            for i, f in enumerate(pitches(rng)))
    draught = W.cfilter(W.pnoise(n, rng), lambda f: (f / 1500) ** 2 / (1 + (f / 1500) ** 2) / (1 + (f / 6000) ** 2))
    draught = draught * np.clip(0.5 + 0.5 * W.slow(n, 0.3, rng), 0.05, None)
    y = W.norm(y) + W.norm(draught) * 0.05
    return close(y, rng, HUM, wet=0.1, drive=2, lufs=-24)


def held(rng, key, f, n, window=0.06):
    """A struck piece of thin metal or glass held out into a whine n samples long (periodic over n): varispeeded so its
    loudest partial sits at `f`, then frozen just after the strike over a short window (stress.freeze), so each partial
    is a narrow band of noise, airy, not a pure ring."""
    x = W.rec(key)
    x = dsp.vari(x, 12 * np.log2(f / strongest(x[:samples(0.2)], 500, 9000)))
    top = int(np.argmax(np.abs(x)))
    return freeze(x[top + samples(0.01):], n, rng, window)


@recipe("tell-hanger-strands", "hum", "held",
        "Its strands quivering, from real struck metal and glass frozen into a thin whine: six strands, trembling, beating",
        """Real recordings, bent: six struck pieces of thin metal and ringing glass from the packs, each varispeeded so its
        loudest partial sits at one strand's pitch (1.7-4.3 kHz, at random, none in unison) and frozen into a held whine
        over a short window, so each partial is a narrow band of noise (airy, like a wire's, not a bell's ring); each
        strand's pitch wavers a little as the draught shifts, it trembles at 7-13 Hz and the draught locks it in and out
        on its own slow swells, so the six beat against each other and are never still. Thin and taut, never a tone or a
        tune. 8 s exact cycle. Held to 1.5-5 kHz.""",
        sources=METAL_L[:4] + RINGING, takes=1, loop=True, band=HUM, lufs=-24, seconds=HUM_L)
def hum_held(rng, k):
    n = samples(HUM_L)
    keys = METAL_L[:4] + RINGING
    y = np.zeros(n, np.float32)
    for i, f in enumerate(pitches(rng)):
        a, b = held(rng, keys[(i + k) % 6], f * 0.996, n), held(rng, keys[(i + k) % 6], f * 1.004, n)
        sway = 0.5 + 0.5 * np.tanh(1.5 * W.slow(n, 0.6, rng))
        y += W.norm(a * sway + b * (1 - sway)) * quiver(n, rng, rng.uniform(7, 13)) * rng.uniform(0.5, 1.0) / (1 + 0.15 * i)
    return close(y, rng, HUM, wet=0.1, drive=2, lufs=-24)


# ---- The Hanger: the grab, the haul, letting go ----------------------------------------------------------------------------------

def snap_rec(rng, k):
    """The strand taking hold: a real switch's crack pitched up small (a thread snapping taut against a coat), a real
    wet step's first instant and a squish under it (tacky: it sticks)."""
    b = Bus(0.4)
    b.at(0, unit(hp(dsp.vari(main_hit(WHIP[k % 2], 0.05), rng.uniform(4, 7)), 800)), -3)
    b.at(0.002, wet_tick(rng, (0, 3), (0.03, 0.05)), -4)
    b.at(0.01, unit(grain(rng, WET[2], 0.08, rng.uniform(1, 4), decay=0.03)), -10)
    return b.x


def snap_model(rng):
    """The strand taking hold, modelled: the slime on it tacking onto the coat in a burst of wet little peels, a pop as
    a seal of it parts (noise through a resonance swept up fast), and the hard tick of the thread snapping taut."""
    b = Bus(0.3)
    b.at(0, unit(tack(rng, 0.05, 900, (900, 3500))), -4)
    L = 0.014
    pop = dsp.sweep_filter(synth.noise(L, rng), "bp", env([(0, 900), (L, 4000)], L, "exp"), q=6, block=16)
    b.at(0.004, unit(pop * env([(0, 0), (0.001, 1), (L, 0)], L)), -3)
    b.at(0.006, unit(synth.click(rng.uniform(2800, 3600), q=6, length=0.01, rng=rng)), -6)
    return b.x


def taut_rec(rng, length, key):
    """The strand pulled tight: a real hinge's squeal (steady, near 1.1 kHz) held out and bent up as the tension comes on,
    thinned to a thread's band; a real leather strap dragged taut under it."""
    c = creak_rec(rng, length * 1.4, rng.uniform(1, 3), key)
    rise = env([(0, 0), (length * 1.4, rng.uniform(6, 9))], length * 1.4)
    y = bp(dsp.fit(bend(c, rise), samples(length)), 700, 6000)
    y = unit(y) * env([(0, 0), (0.04, 0.7), (length * 0.8, 1), (length, 0)], length)
    s = grain(rng, STRAP, length, rng.uniform(-1, 2), attack=False)
    s = unit(bp(dsp.fit(s, samples(length)), 600, 4000)) * env([(0, 0), (0.05, 1), (length, 0.3)], length)
    return mix(y * 0.6, s * 0.8)


def rafter_rec(rng, length, k):
    """The rafter overhead taking the weight: a real creak dropped for a beam, and its dull knock, through the ceiling."""
    c = beam_rec(rng, length, k) * env([(0, 0), (length * 0.3, 1), (length, 0)], length)
    kn = ck.choke(hit_rec(rng, WOOD_M, rng.uniform(-7, -4), 0.3), 0.04, 0.05)
    return overhead(mix(unit(c), unit(kn) * 0.5))


def rafter_model(rng, length):
    """The rafter overhead taking the weight, modelled: slow stick-slip through its modes and its dull knock, through
    the ceiling."""
    c = synth.creak(length, env([(0, 15), (length * 0.5, 40), (length, 20)], length), rng,
                    body=[f * rng.uniform(0.9, 1.1) for f in RAFTER], q=14, jitter=0.4)
    c = hp(c, 150) * env([(0, 0), (length * 0.3, 1), (length, 0)], length)
    kn = struck(rng, [f * rng.uniform(0.9, 1.1) for f in RAFTER[:4]], 6, 0.12, 0.003)
    return overhead(mix(unit(c), kn * 0.5))


@recipe("cs-hanger", "grab", "snare",
        "The grab: a coat walks into a strand, a sticky snap, the strand pulled tight and the rafters taking the weight",
        """Real recordings: a coat brushing into the strand (the packs' cloth), the sticky snap (a real switch's crack
        pitched up small for a thread snapping taut, a real wet step's first instant and a chewing squish under it), then
        the strand pulled tight (a real hinge's squeal held out, thinned to a thread's band and bent up most of an octave
        as the tension comes on, a real leather strap dragged taut under it) and, overhead, the rafter taking the weight
        (a real creak dropped and a dull knock, through the ceiling). One plain; one quicker and tighter; one where two
        strands catch.""",
        sources=R("cloth1", "cloth2", "cloth3", "cloth4") + WHIP + WET + CREAKS + DOORS + [STRAP] + WOOD_M, takes=3, lufs=-20)
def grab_snare(rng, k):
    b = Bus(1.8)
    b.at(0, ck.cloth(rng, k, 0.14), -12)
    t = 0.06
    snaps = [t] if k % 3 != 2 else [t, t + rng.uniform(0.08, 0.11)]
    for i, s in enumerate(snaps):
        b.at(s, snap_rec(rng, k + i), -3 * i)
    D = (0.55, 0.4, 0.65)[k % 3] * rng.uniform(0.9, 1.1)
    b.at(snaps[-1] + 0.02, taut_rec(rng, D, CREAKS[1 + k % 2]), -5)
    b.at(snaps[-1] + D * 0.5, rafter_rec(rng, 0.6, k), -19)
    return finish(hp(dry(b.x), 120, 4), -20, PEAK)


@recipe("cs-hanger", "grab", "tack",
        "The grab, modelled: a sticky snap of wet peels and a pop, the strand tightening in a rising creak, a thrum",
        """Modelled: a coat brushing the strand (a breath of noise), the sticky snap (the slime tacking onto the cloth in a
        burst of wet little peels, a pop as a seal of it parts, the hard tick of the thread snapping taut), then the strand
        pulled tight (stick-slip as a steady train gliding up with the load, rung through a thin thread's resonances,
        1.3-4.6 kHz, that climb with the tension), a short thrum as it takes the whole pull, and overhead the rafter
        creaking under the weight. Same three performances as 'snare'.""",
        takes=3, lufs=-20)
def grab_tack(rng, k):
    b = Bus(1.8)
    L = 0.12
    b.at(0, unit(bp(synth.noise(L, rng, "pink"), 800, 5000)) * env([(0, 0), (0.03, 1), (L, 0)], L), -14)
    t = 0.06
    snaps = [t] if k % 3 != 2 else [t, t + rng.uniform(0.08, 0.11)]
    for i, s in enumerate(snaps):
        b.at(s, snap_model(rng), -3 * i)
    D = (0.55, 0.4, 0.65)[k % 3] * rng.uniform(0.9, 1.1)
    load = env([(0, 0.1), (D * 0.8, 1.0), (D, 0.8)], D)
    pull = tighten(rng, D, load, 60, 300, modes=(1300, 1850, 2550, 3400, 4600), q=20)
    b.at(snaps[-1] + 0.02, pull * env([(0, 0), (0.03, 1), (D * 0.85, 1), (D, 0)], D), -4)
    f0 = rng.uniform(330, 420)
    hs = range(2, 9)
    thrum = W.modal([f0 * h * rng.uniform(0.99, 1.01) for h in hs], [0.15 / h ** 0.6 for h in hs], [1 / h ** 0.8 for h in hs],
                    0.4, rng, contact=0.0008)
    b.at(snaps[-1] + 0.02 + D * 0.8, unit(hp(thrum, 400)), -12)
    b.at(snaps[-1] + D * 0.5, rafter_model(rng, 0.6), -19)
    return finish(hp(dry(b.x), 120, 4), -20, PEAK)


HAUL_L = 8.0
PULLS = 11      # a heave every 0.73 s: up 0.7 m in its 2.5 s haul takes three or four


def teeth(rng, length, rate):
    """A ratchet's catches as a gate: a short swell every 1/`rate` s, a little uneven, each catch a tooth; and their
    times."""
    n = samples(length)
    g = np.zeros(n, np.float32)
    t, at = rng.uniform(0, 0.02), []
    while t < length:
        w = samples(rng.uniform(0.02, 0.035))
        a = samples(t)
        seg = np.hanning(w).astype(np.float32)[:max(0, n - a)] * rng.uniform(0.5, 1.0)
        g[a:a + len(seg)] = np.maximum(g[a:a + len(seg)], seg)
        at.append(t)
        t += rng.uniform(0.85, 1.15) / rate
    return g, at


def reel_rec(rng, length):
    """The strand reeled in wet through its mouthparts: real wet clicks thick as it pulls (a reel spinning in slime)."""
    rc = env([(0, 20), (length * 0.3, 80), (length, 10)], length)
    b = Bus(length + 0.1)
    t = rng.exponential(1 / 20)
    while t < length:
        b.at(t, wet_tick(rng, (2, 7)), rng.uniform(-14, 0))
        t += rng.exponential(1 / max(float(rc[min(samples(t), len(rc) - 1)]), 1))
    return b.x


def pull_rec(rng, i, length):
    """One heave up the strand: the rafter's creak let through catch by catch (a real creak, dropped, gated by a ratchet's
    teeth, a real small wood knock on each catch), the wet reel, the strand drawn taut (a real leather strap)."""
    b = Bus(length + 0.4)
    c = beam_rec(rng, length, i)
    g, at = teeth(rng, length, rng.uniform(14, 19))
    b.at(0, unit(c * g), 0)
    for t in at:
        b.at(t, unit(grain(rng, WOOD_L[int(rng.integers(5))], 0.015, rng.uniform(2, 5), decay=0.004)), rng.uniform(-18, -12))
    b.at(0, reel_rec(rng, length), -9)
    s = grain(rng, STRAP, length, rng.uniform(-2, 1), attack=False)
    b.at(0, unit(bp(dsp.fit(s, samples(length)), 700, 4000)) * env([(0, 0), (0.05, 1), (length, 0)], length), -15)
    return b.x


@recipe("cs-hanger", "haul", "reel",
        "The haul: the rafters creaking catch by catch like a ratchet, a wet reel, the strand drawn taut, heave on heave",
        """Real recordings: eleven heaves a cycle, each the rafter's creak let through catch by catch (a real door's
        creak dropped a few semitones, or a hinge's an octave, gated into 14-19 teeth a second, a real small wood knock
        on every catch: a ratchet made of wood), the strand reeled in wet through its mouthparts (real wet clicks thick
        as it pulls), the strand drawn taut (a real leather strap), and every third heave the rafter knocking as the
        weight on it swings. Overhead, through the ceiling. 8 s exact cycle.""",
        sources=DOORS + CREAKS[2:] + WOOD_L + WET + [STRAP] + WOOD_M, takes=1, loop=True, lufs=-22, seconds=HAUL_L)
def haul_reel(rng, k):
    n = samples(HAUL_L)
    ev = []
    for i in range(PULLS):
        t = i * HAUL_L / PULLS + rng.uniform(-0.03, 0.03)
        D = rng.uniform(0.38, 0.5)
        ev.append((t, pull_rec(rng, i, D), rng.uniform(0.75, 1.0)))
        if i % 3 == 0:
            ev.append((t + D * 0.6, ck.choke(hit_rec(rng, WOOD_M, rng.uniform(-7, -4), 0.3), 0.04, 0.05), 0.3))
    y = W.cyclic(lambda z: lp(z, 6000, 2), W.place(n, ev))
    return close(y, rng, None, wet=0.12, lufs=-22)


def pull_model(rng, length):
    """One heave up the strand, modelled: the rafter's stick-slip as a ratchet (slips a steady 14-19 a second, each a
    catch, rung through a rafter's modes), the wet reel (wet little peels and small bubbles, thick as it pulls), the
    strand tightening under the load."""
    b = Bus(length + 0.3)
    c = synth.creak(length, rng.uniform(14, 19), rng, body=[f * rng.uniform(0.9, 1.1) for f in RAFTER], q=16, jitter=0.08,
                    grit=0.6)
    b.at(0, unit(hp(c, 150)) * env([(0, 0), (0.03, 1), (length * 0.8, 0.9), (length, 0)], length), 0)
    rc = env([(0, 40), (length * 0.3, 260), (length, 30)], length)
    b.at(0, unit(tack(rng, length, rc, (800, 3000))), -9)
    b.at(0, unit(synth.bubbles(length, rc * 0.25, 1500, 4000, rng, rise=(0.1, 0.3))), -16)
    load = env([(0, 0.3), (length * 0.6, 1.0), (length, 0.5)], length)
    b.at(0, tighten(rng, length, load, 50, 140, modes=(1300, 1850, 2550, 3400), q=18) * env([(0, 0), (0.04, 1), (length, 0)], length),
         -14)
    return b.x


@recipe("cs-hanger", "haul", "winch",
        "The haul, modelled: the rafter's stick-slip catching like a ratchet, a wet reel of peels and bubbles, heave on heave",
        """Modelled: eleven heaves a cycle, each the rafter's stick-slip as a ratchet (a steady 14-19 slips a second, so
        every slip is a catch, rung through a rafter's modes), the strand reeled in wet (slime peeling off it in wet little
        ticks and small bubbles, thick as it pulls), the strand tightening under the load (a creak with a pitch that
        rises), and every third heave the rafter knocking as the weight swings. Overhead, through the ceiling. 8 s exact
        cycle.""",
        takes=1, loop=True, lufs=-22, seconds=HAUL_L)
def haul_winch(rng, k):
    n = samples(HAUL_L)
    ev = []
    for i in range(PULLS):
        t = i * HAUL_L / PULLS + rng.uniform(-0.03, 0.03)
        D = rng.uniform(0.38, 0.5)
        ev.append((t, pull_model(rng, D), rng.uniform(0.75, 1.0)))
        if i % 3 == 0:
            ev.append((t + D * 0.6, struck(rng, [f * rng.uniform(0.9, 1.1) for f in RAFTER[:4]], 6, 0.12, 0.003), 0.3))
    y = W.cyclic(lambda z: lp(z, 6000, 2), W.place(n, ev))
    return close(y, rng, None, wet=0.12, lufs=-22)


def zip_up(rng, real=True, k=0):
    """The strand let go and drawn up into the roof: a fast thin rising whizz (a real switch's swish reversed and pitched
    up, or noise through a band swept up)."""
    if real:
        x = dsp.reverse(dsp.vari(main_hit(WHIP[k % 2], 0.18), rng.uniform(3, 5)))
        return unit(hp(x, 1500))
    L = 0.18
    y = dsp.sweep_filter(synth.noise(L, rng), "bp", env([(0, 1500), (L, 6000)], L, "exp"), q=3)
    return unit(y * env([(0, 0), (L * 0.8, 1), (L, 0)], L))


def hiss_rec(rng, length, pumps):
    """Its hiss: a real gasp's breath stretched out and dropped for a sac that size, rung through its mouthparts'
    small cavities, pushed out in `pumps` pushes, wet grains in it."""
    g = dsp.vari(gasp_core(), rng.uniform(-4, -2))
    g = dsp.fit(dsp.stretch(g, length / (len(g) / SR)), samples(length))
    g = mix(unit(g), unit(dsp.resonate(g, [1800, 2900, 4300], q=4)) * 0.5)
    shape = np.zeros(samples(length), np.float32)
    t = 0.0
    for i in range(pumps):
        P = length / pumps * rng.uniform(0.9, 1.2) * (1.3 if i == pumps - 1 else 1.0)
        seg = dsp.fit(env([(0, 0), (0.015, 1), (P * 0.35, 0.6), (P, 0)], P), samples(P)) * 0.75 ** i
        a = samples(t)
        m = min(len(seg), len(shape) - a)
        if m > 0:
            shape[a:a + m] = np.maximum(shape[a:a + m], seg[:m])
        t += P * rng.uniform(0.7, 0.85)
    wet = Bus(length + 0.1)
    t = rng.exponential(1 / 50)
    while t < length:
        wet.at(t, wet_tick(rng, (0, 4)), rng.uniform(-16, -4))
        t += rng.exponential(1 / 50)
    return mix(unit(bp(g, 900, 8000)) * shape, dsp.fit(wet.x, len(shape)) * shape * 0.4)


def scuttle_rec(rng, length, rate=32):
    """Many thin jointed legs scuttling up the rafters and away into the roof: real small wood knocks and steps on boards
    cut to their tips and pitched up, quick and uneven, going duller and quieter as it climbs away."""
    b = Bus(length + 0.1)
    t = rng.exponential(1 / rate)
    keys = WOOD_L + STEPS
    while t < length:
        x = grain(rng, keys[int(rng.integers(len(keys)))], rng.uniform(0.008, 0.015), rng.uniform(5, 9), decay=0.004)
        b.at(t, unit(hp(x, 600)), rng.uniform(-12, 0))
        t += rng.exponential(1 / rate) * 0.6 + 0.4 / rate
    return away(b.x)


def away(x):
    """Something climbing away up into the roof: duller and quieter as it goes."""
    L = len(x) / SR
    y = dsp.sweep_filter(np.asarray(x, np.float32), "lp", dsp.fit(env([(0, 7000), (L, 1200)], L, "exp"), len(x)), q=0.7)
    return dsp.shaped(y, [(0, 1), (L, 0.25)], "exp")


@recipe("cs-hanger", "retreat", "scuttle",
        "Letting go: the strand whipped up into the roof, a wet hiss, many thin legs scuttling away up the rafters",
        """Real recordings: the strand let go and drawn up into the roof (a real switch's swish reversed and pitched up),
        then its hiss (a real gasp's breath stretched and dropped for a sac that size, rung through its mouthparts' small
        cavities, pushed out in two or three pushes with wet clicks in it), and many thin jointed legs scuttling away up
        the rafters (real small wood knocks and steps on boards cut to their tips and pitched up, quick and uneven,
        duller and quieter as it climbs away into the roof), a rafter creaking as it settles up there and a little grit
        sifting down. One short; one with a longer hiss and a pause in the scuttle.""",
        sources=WHIP + [GASP] + WET + WOOD_L + STEPS + DOORS + CREAKS[2:] + WOOD_M + GRAVEL, takes=2, lufs=-21)
def retreat_scuttle(rng, k):
    b = Bus(2.6)
    b.at(0, zip_up(rng, True, k), -8)
    H = (0.5, 0.8)[k % 2] * rng.uniform(0.9, 1.1)
    b.at(0.08, hiss_rec(rng, H, 2 + k % 2), -5)
    s0 = 0.08 + H * 0.4
    if k % 2:
        b.at(s0, scuttle_rec(rng, 0.45), 2)
        b.at(s0 + 0.6, scuttle_rec(rng, 0.35), -3)
        end = s0 + 0.95
    else:
        b.at(s0, scuttle_rec(rng, 0.7), 2)
        end = s0 + 0.7
    b.at(end, rafter_rec(rng, 0.5, k), -14)
    b.at(end + 0.1, grit(rng, 0.5, env([(0, 0), (0.05, 50), (0.5, 0)], 0.5)), -22)
    return finish(hp(dry(b.x), 150, 4), -21, PEAK)


@recipe("cs-hanger", "retreat", "skitter",
        "Letting go, modelled: a whizz of strand drawn up, a wet hiss pumped out of its sides, legs skittering into the roof",
        """Modelled: the strand drawn up into the roof (noise through a band swept up fast), its hiss (air driven out
        through many small openings, a broad hiss with soft resonances and a rough flutter, pumped out in two or three
        pushes, slime peeling in it), and many thin legs skittering away up the rafters (hard little tips clicking, eight
        legs uneven, on wood that knocks dull under them, duller and quieter as it climbs), a rafter creaking as it settles
        and grit clicking down. Same two performances as 'scuttle'.""",
        takes=2, lufs=-21)
def retreat_skitter(rng, k):
    b = Bus(2.6)
    b.at(0, zip_up(rng, False), -8)
    H = (0.5, 0.8)[k % 2] * rng.uniform(0.9, 1.1)
    hs = spiracles(rng, H, 2 + k % 2)
    b.at(0.08, mix(unit(lp(hs, 8000)), unit(tack(rng, H, 120, (900, 3200))) * 0.3 * np.abs(hs) / (np.max(np.abs(hs)) + 1e-9)), -5)
    s0 = 0.08 + H * 0.4

    def legs(L):
        sk = synth.skitter(L, rng.uniform(45, 60), rng, f=(1800, 4500), q=(5, 10), legs=8)
        wood = knocks(rng, L, 20, SASH, q=5, tail=0.02)
        return away(mix(unit(sk), unit(dsp.fit(wood, len(sk))) * 0.4))

    if k % 2:
        b.at(s0, legs(0.45), 9)
        b.at(s0 + 0.6, legs(0.35), 4)
        end = s0 + 0.95
    else:
        b.at(s0, legs(0.7), 9)
        end = s0 + 0.7
    b.at(end, rafter_model(rng, 0.5), -14)
    b.at(end + 0.1, sift(rng, 0.5, env([(0, 0), (0.05, 50), (0.5, 0)], 0.5)), -22)
    return finish(hp(dry(b.x), 150, 4), -21, PEAK)
