"""The Wakers heard (queue #502, note 772; note 588, docs/design/creatures/wakers.md §1-§5, §8.4 "the sounds to AU1"):
what gets up at dawn. GameAudio.Wakers.cs fires them, tools/audio/cues.py names them.

What they are decides the build. Hill-sized (about 60 m stood) and earth-coloured, they sleep under the land all night;
at dawn they rise behind the train and come down the line after it, slow, then faster than any engine (26 m/s). One that
reaches the last car seizes it, drags the train to a stand, lifts the cars from the rear one at a time with the crew in
them and eats them back to the engine; whoever ran off the train is taken by another rising beside them. So their sound
is the land's and the iron's: geological, enormous and slow, heard from a kilometre and more. Never a film's dinosaur
roar, never a crewmate's step made big, never the Moose's bellow.
- The stir (tell-wakers thud, call), the last three minutes before dawn, out where they lie (the game puts it 700 m off,
  toward the sky's first light). A thud through the land, felt more than heard: a short, deep ground blow (its body 40-150
  Hz) with a faint crack of rock in it, so it's never a sine's boom, and the land rumbling on after it. A long, low call
  from behind the hills: an enormous slow voice moaning and bending down, breath and a rough grain in it, the hills
  answering. Both sit under the night creatures' tells so they never mask one (spec A.3): mostly 40-400 Hz with a little
  grit above, darkened by the air and given a long distant tail of our own (the game's mixer only makes a far sound
  quieter, it doesn't darken it).
- The rise: the land getting up over 8 s in three surges, each bigger than the last: earth and rock torn open, roots
  and stones popping, a long groan rising under it, debris pattering down after each.
- Its stride, as it comes down the line: the ground struck by a foot the size of a barn, a crack of rock, stones thrown
  up and landing again a moment later (they fly: 2v/g), the earth falling back. The director's squish rule (8 Oct): a
  footfall with most of its energy under 150 Hz reads as a squish, so every take keeps under 45% there and carries its
  weight in the rock's crack, the stones and the debris as well as in the low body.
- Its roar, running the train down or stood under a town's guns: huge, harsh and slow, from a throat the size of a
  house, ragged with a slow rattle, the air roaring through it.
- At the train (cs-wakers), close: the grab (a blow through the whole train, steel shrieking as a hand closes on it, the
  couplings snatching taut car after car toward the engine); the drag (the wheels locked and screaming on the rails, steel
  grinding, flanges shrieking: a 6 s loop); the lift (a car's frame groaning and twisting, its trucks scraping off the
  rail, a coupling snapping with a crack of iron); eating (steel and timber crushed in its jaws, planks splintering,
  glass, a deep crunch); the take (the ground struck as a hand comes down, a great grip closing, earth spilling through
  its fingers); the swallow (a crunch overhead and a long wet swallow). The grab, the eating and the take keep the squish
  rule too.

Each cue has two candidates. The first is the packs' real recordings bent to the size: a real thunder roll slowed one
and two octaves for the land's weight and its rumbling, a pick into rock, real wood breaking dropped into rock splitting,
stones and gravel thrown and falling, a real roar, groan and gasp dropped and held out for the throat, real steel and
plate blows, a metal scrape and bell metal held into a shriek for the iron, wood, tin and glass for the cars, wet
squelches and water for the swallow. The second is modelled: pressure pulses and the land's broad low modes, its coda,
stick-slip and fracture, struck bodies, stones in flight, a glottis through a tract the size of a cave, a jet of air.
The drag is built periodic (events round the cycle, circular filters and room), so build.py's seam gives the cycle back
exactly.
"""

import numpy as np
from scipy import signal

import build
import dsp
import src
import synth
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import fry, main_hit, unit
from recipes.boarders import grain
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, R, S, K
from recipes.g1_ground import air, splinter, struck
from recipes.g1_train import clank, close, creak_rec, judder, run_down, scrape_grains
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.house_beasts import cloud, grit, sift, strew
from recipes.house_folk import core
from recipes.moose_tells import burst
from recipes.stress import held_ring

SR = dsp.SR
PEAK = -1.5

# ---- Bands (spec A.3/A.4) and levels -----------------------------------------------------------------------------------------

STIR = (40, 400)          # the stir's thud and call: under every night creature's tell, a little grit above
RISE = (40, 5000)         # the land tearing open: its weight low, the rock, roots and stones above
STRIDE = (60, 6000)       # a footfall: the blow, the rock's crack, stones flying
ROAR = (50, 3000)         # a throat the size of a house: low, harsh, nothing shrill
TRAIN = (80, 8000)        # the grab, the lift, the eating: iron, timber and glass at the car
DRAG = (250, 8000)        # the skid: wheels and flanges screaming, steel grinding
TAKE = (60, 6000)         # the hand coming down and closing, the earth spilling
GULP = (50, 4000)         # the swallow overhead: a crunch and the wet of a throat

STIR_LUFS = -20.0
RISE_LUFS = -18.0
STRIDE_LUFS = -18.0
ROAR_LUFS = -17.0
GRAB_LUFS = -18.0
DRAG_LUFS = -21.0
LIFT_LUFS = -19.0
EAT_LUFS = -18.0
TAKE_LUFS = -19.0
GULP_LUFS = -19.0

# ---- Sources -----------------------------------------------------------------------------------------------------------------

THUNDER = S("thunder_01")[0]                          # a real thunder roll: slowed, the land's own weight and rumble
ROAR_REC = "sfx_100_v2:misc_24"                       # a real roar: a throat round 200 Hz
GROAN = "sfx_100_v2:misc_25"                          # a real groan and wail: a voiced throat
GASP = "sfx_100_v2:misc_05"                           # a real gasp: air forced through a mouth
DOORS = R("doorOpen_1", "doorOpen_2")                 # real door creaks: big, slow wood
CRACKS = S("misc_34", "misc_35")                      # real wood cracking and breaking: dropped, rock splitting
STONES = S("stones_01", "stones_02", "stones_03")     # real stones knocked and scraped
GRAVEL = STONES + S("footstep_01", "footstep_02")     # stones and gravel crunched: cut into clods, grit and soil
MINING = K("impactMining")                            # a pick into rock: a heavy dull strike
DECK = S("wood_hit_01", "wood_hit_02")                # heavy blows on planking: a car's floor and sides
PLANK = K("impactPlank_medium")                       # a plank's hollow knock: a sleeper, a car's boards
PLATE_H = K("impactPlate_heavy")                      # heavy plate struck: iron's mass
METAL_H = K("impactMetal_heavy")                      # heavy metal struck: its clank
SLAM = "sfx_100_v2:metal_hit_01"                      # a real blow on a steel sheet
KNOCK = "sfx_100_v2:misc_36"                          # a heavy knock-slam
SNAP = "sfx_100_v2:metal_hit_02"                      # a sharp, bright hit of metal: iron cracking
SCRAPE = "sfx_100_v2:misc_10"                         # a real metal scrape, a few strokes
RATTLE = S("metal_03")[0]                             # a real loose-metal rattle: a pin and chain
BELLS = K("impactBell_heavy")                         # struck bell metal: held, a wheel's scream
PLATES_L = K("impactPlate_light")                     # light plate struck: held, a flange's shriek
TIN = K("impactTin_medium")                           # tin struck: a car's roof crushed
SHATTER = S("misc_26", "misc_27", "glass_03")         # real glass breaking and shattering
WHIP = S("switch_01", "switch_02")                    # a real switch cracked through the air: the report of a snap
WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03") + R("handleCoins")   # wet squelches, a chewing squish
WATER = "sfx_100_v2:loop_water_01"                    # a mass of water moving
LEATHER = R("handleSmallLeather", "handleSmallLeather2")   # real leather handled: a hide flexing
PALM = R("dropLeather")[0]                            # a real heavy leather drop: a hand landing on a coat

# ---- Bodies (Hz) -------------------------------------------------------------------------------------------------------------

LAND = [44, 63, 88, 121, 167, 230]                    # the land's low modes under a blow: soil over rock, broad
CLOD = [190, 330, 560, 900]                           # a slab of soil and rock thrown loose: dull
HILL = [38, 61, 97, 150, 230, 350, 520]               # the land's groan as it lifts: a hill's frame, stick-slip
WHEEL = [380, 1050, 1880, 2750, 3640, 4550, 5450]     # a car wheel's modes (stress.py, world_bed.py)
HIDE = [140, 260, 430, 700, 1100]                     # a hand's hide, a hall wide: soft, heavily damped
CHEST = [70, 130, 240, 410]                           # a throat a house wide: its hollow


# ---- Shared parts ------------------------------------------------------------------------------------------------------------

def hold(x, band, order=2):
    """A one-shot held to its band, gently and zero phase, so its attacks stay put."""
    return dsp.band(np.asarray(x, np.float32), *band, order=order)


def trimmed(y, length, fade=0.3):
    """Cut to `length` s, its last `fade` s faded out."""
    return dsp.fade(dsp.fit(np.asarray(y, np.float32), samples(length)), 0.0, fade)


def roll(rng, length, semis, start=None, cut=None):
    """A stretch of the real thunder roll slowed `semis` (one and two octaves: the land's own rumble, not the sky's),
    from `start` s into it or anywhere past its crack, `length` s long."""
    x = src.get(THUNDER)
    need = length * 2 ** (semis / 12)
    a = start if start is not None else rng.uniform(0.9, max(0.95, len(x) / SR - need - 0.05))
    y = dsp.fit(dsp.vari(dsp.trim(x, a, need), semis), samples(length))
    return unit(lp(y, cut) if cut else y)


def strike(semis, length=0.4, cut=None):
    """The real thunder's crack slowed into a blow on the land: its loudest hit, `semis` down."""
    y = dsp.vari(main_hit(THUNDER, length), semis)
    return unit(lp(y, cut) if cut else y)


def rock(rng, semis, length=0.35):
    """Rock splitting: a real piece of wood breaking, cut at its loudest crack and dropped (a slab of rock is denser and
    duller than a board), real stones knocking in it as the pieces shift."""
    c = unit(dsp.vari(main_hit(CRACKS[int(rng.integers(2))], length), semis))
    b = Bus(len(c) / SR + 0.1)
    b.at(0, c, 0)
    for _ in range(3):
        g = grain(rng, STONES[int(rng.integers(3))], rng.uniform(0.02, 0.05), semis + rng.uniform(-1, 3))
        b.at(rng.uniform(0, 0.05), unit(g), rng.uniform(-14, -7))
    return unit(b.x)


def rock_model(rng, length=0.3, f1=None):
    """Rock splitting, modelled: fracture running through a slab (stick-slip quickening and dying, rung through rock's
    close modes), the two pieces' own modes struck once as it breaks (dense and quickly damped: rock clacks, it never
    rings to a pitch), and the hard edge of the break."""
    f1 = f1 or rng.uniform(260, 420)
    tr = W.tear(length, rng, env([(0, 900), (length * 0.3, 300), (length, 20)], length), (f1, 3200), q=8, crack=0.6)
    tr = unit(tr) * env([(0, 1), (length, 0)], length, "exp")[:len(tr)]
    pieces = mix(*[unit(W.body(rng, f1 * r, W.PLATE, decay=0.035, damp=2.0, contact=0.0006, count=10))
                   for r in (1.0, rng.uniform(1.3, 1.6))])
    edge = unit(hp(burst(rng, 0.004, attack=0.05), 900))
    return unit(mix(tr * 0.8, unit(pieces) * 0.4, edge * 0.6))


def blow(rng, length=0.9, f=40, size=1.0, sub=0.8):
    """The land struck, modelled: a pressure pulse through the ground (a Friedlander pulse, world_kit.boom's, without the
    explosion's click), its sub dropping in pitch as it dies (`sub` of it), and the land's own broad low modes rung once,
    soil over rock: a boom with no pitch. `size` scales the patch of ground that rings (smaller: its modes higher)."""
    n = samples(length)
    t = np.arange(n) / SR
    T = rng.uniform(0.03, 0.05)
    fr = ((1 - t / T) * np.exp(-1.3 * t / T) * np.exp(-t / 0.35)).astype(np.float32)
    thump = synth.thump(f * rng.uniform(0.9, 1.1), length * 0.8, drop=0.45)
    p = unit(lp(mix(unit(fr), unit(thump) * sub), 280))
    m = struck(rng, [x * rng.uniform(0.92, 1.08) / size for x in LAND], 2.5, length, hit=0.012)
    return unit(mix(p, m * 0.8))


def coda(rng, length, rate=14.0, f=(45, 170)):
    """The land ringing on after a blow, modelled: the blow arriving again by every longer path through the ground, many
    soft low knocks thinning out (a seismogram's coda), over a low rumble that dies with them."""
    b = Bus(length + 0.6)
    t = rng.exponential(1 / rate)
    while t < length:
        fc = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
        x = struck(rng, [fc, fc * 1.47, fc * 2.1], 2.0, 0.3, hit=0.01)
        b.at(t, x, -4 - 24 * t / length + 20 * np.log10(rng.uniform(0.3, 1.0)))
        t += rng.exponential(1 / rate) * (1 + 1.5 * t / length)
    rum = lp(synth.noise(length, rng, "pink"), f[1], 2)
    lumps = np.clip(1 + 0.8 * lp(rng.standard_normal(samples(length)).astype(np.float32), 5) * 20, 0.1, None)
    rum = unit(rum * lumps) * env([(0, 0), (0.06, 1), (length * 0.35, 0.35), (length, 0)], length, "exp")[:len(rum)]
    return unit(mix(unit(b.x), rum * 0.8))


def far(x, rng, cut=1400, wet=0.45, decay=3.0, echoes=((0.45, -10), (1.05, -15))):
    """Out where they lie, behind the hills: the air takes the top off (darker the further), and the land answers with a
    long dark tail and an echo or two off the hills. A reverb of our own: the game's mixer only makes a far sound quieter."""
    y = lp(np.asarray(x, np.float32), cut, 2)
    h = dsp.ir(decay * 1.3, decay, min(cut, 700), rng=rng)
    w = signal.fftconvolve(y, h).astype(np.float32)
    b = Bus(len(w) / SR + 0.1)
    b.at(0, y * (1 - wet))
    b.at(0, w * wet * 1.2)
    for d, db in echoes:
        b.at(d * rng.uniform(0.85, 1.15), lp(y, cut * 0.5, 2), db)
    return b.x


def flight(rng, count, make, v=(1.0, 3.5), loud=-14.0):
    """Stones thrown up by a blow landing again: each comes down after its own time in the air (2v/g, thrown at 1-3.5
    m/s: 0.2-0.7 s), most of them small, a few big and louder, some bouncing once more, smaller and sooner.
    `make(rng, size)` one stone landing; returns [(t, x, db), ...]."""
    out = []
    for _ in range(count):
        s = rng.random() ** 2
        t = 2 * rng.uniform(*v) / 9.81
        out.append((t, make(rng, s), loud - 12 + 14 * s))
        if rng.random() < 0.4:
            out.append((t + rng.uniform(0.05, 0.14), make(rng, s * 0.5), loud - 20 + 10 * s))
    return out


def stone_rec(rng, s):
    """A stone landing: a real stone knocked, the bigger the lower."""
    g = grain(rng, STONES[int(rng.integers(3))], 0.025 + 0.05 * s, rng.uniform(-2, 3) - 6 * s)
    return unit(hp(g, 350))


def stone_model(rng, s):
    """A stone landing, modelled: a small hard body's modes struck, the bigger the lower and the duller."""
    f = (2800 - 1800 * s) * rng.uniform(0.85, 1.15)
    return struck(rng, [f, f * 1.62, f * 2.31, f * 3.1], rng.uniform(7, 13), 0.05 + 0.05 * s, hit=0.0006 + 0.001 * s)


def iron(rng, i, size=-6.0, damp=0.04, ring=0.3):
    """A heavy cast-iron or steel part struck (crew_train's): the packs' heavy plate for its mass and a heavy metal hit,
    both pitched down to the part's size and choked (it clanks, it doesn't sing)."""
    body = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_H[i % 5]), size * 0.5 + rng.uniform(-0.5, 0.5)))), 0.02,
                    damp * 1.5)
    knock = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[(i + 2) % 5]), size + rng.uniform(-0.5, 0.5)))), 0.004, damp)
    return ck.norm(mix(body, knock * ring * 2))


def held(rng, key, length, semis, sag, window=0.25, piece=0.3):
    """A real throat (or creak) held out: its loudest `piece` s dropped `semis` and paulstretched to `length` (its grain
    and colour kept, its own jerks and envelope gone), bent along `sag` (semitones, a constant or per-sample curve)."""
    seg = dsp.vari(core(key, piece), semis)
    up = float(np.max(synth.curve(sag, samples(length))))
    m = length * 2 ** (max(up, 0) / 12) + 0.4
    long_ = dsp.smear(seg, m / (len(seg) / SR), window, rng)
    return unit(dsp.fit(bend(long_, sag), samples(length)))


def giant(rng, pts, length, vowels, shape, rattle=(9.0, 13.0), rasp=0.45, sub=0.5, k=0.18, breath=0.5):
    """A throat the size of a cave: a glottis at 30-70 Hz (pitch from `pts`) with period doubling in it (`sub`), gated by
    a slow rattle (`rattle` pulses a second, slower as it's bigger: a flap of throat a metre long), breath noise through
    the same tract (`k` of a child's: a fifth to a third puts its formants at 100-1000 Hz, under any crewmate's)."""
    n = samples(length)
    sh = dsp.fit(env(shape, length), n)
    g = synth.glottis(env(pts, length, curve="exp"), length, rng, jitter=0.03, shimmer=0.3, sub=sub, oq=0.7)
    fr = fry(rng, rattle[0] + (rattle[1] - rattle[0]) * sh, length, jitter=0.4, decay=0.03)
    x = g * (1 - rasp + rasp * fr) + synth.noise(length, rng, "pink") * (0.15 + 0.5 * fr) * breath
    v = synth.tract(x, vowels, k, breath=0.3, rng=rng)
    return unit(v * sh)


def chest(x, amount=0.4):
    """Rung through a throat a house wide: its hollow under the voice."""
    r = dsp.resonate(x, CHEST, q=3, gains=[1, 0.7, 0.45, 0.3])
    return unit(mix(unit(x), unit(r) * amount))


def lumpy(rng, length, rate=4.0, depth=0.6):
    """A slow uneven swell (1 +- depth), nothing faster than `rate` Hz."""
    w = lp(rng.standard_normal(samples(length) + SR).astype(np.float32), rate)[SR:]
    return np.clip(1 + depth * w / (np.max(np.abs(w)) + 1e-9), 0.05, None).astype(np.float32)


# ---- The stir: a thud through the land ---------------------------------------------------------------------------------------

THUD_L = (2.2, 1.8, 2.8, 2.0)          # one a take: further and closer, stronger as dawn comes (the game scales it)
THUD_AIR = (1100, 1400, 1800, 950)     # the air's lowpass for each: the further, the darker


@recipe("tell-wakers", "thud", "ground",
        "The stir: a far-off thud through the land, felt more than heard, a crack of rock in it, the land rumbling on",
        """Real recordings, bent: the real thunder roll's crack slowed two octaves into one short, deep blow on the land
        (its body 40-150 Hz), a pick into rock dropped under it for the rock below the soil, and in it, faint, rock
        splitting (real wood breaking, dropped an octave, real stones knocking) and a little gravel shaken. The
        land rumbles on after it: the thunder's own roll slowed an octave and a half and dying away. All of it 700 m
        off: the air takes the top off, the land answers with a long dark tail and an echo off the hills. Four takes at
        four distances, 1.6-2.6 s. Held to 40-400 Hz with a little grit above.""",
        sources=[THUNDER] + MINING + CRACKS + GRAVEL, takes=4, band=STIR, lufs=STIR_LUFS)
def thud_ground(rng, k):
    L = THUD_L[k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 1.0)
    hit = strike(rng.uniform(-24, -21), 0.3, cut=260)
    b.at(0, dsp.shaped(hit, [(0, 1), (0.12, 0.5), (0.5, 0.08), (1.0, 0.0)], "exp"), 0)
    rockbed = unit(lp(dsp.vari(main_hit(MINING[k % 5], 0.6), rng.uniform(-7, -5)), 400))
    b.at(0.004, rockbed, -3)
    tail = roll(rng, L, rng.uniform(-19, -16), cut=220)
    b.at(0.05, tail * env([(0, 0), (0.15, 1), (L * 0.45, 0.4), (L, 0)], L, "exp")[:len(tail)], -5)
    b.at(0.01, rock(rng, rng.uniform(-14, -12)), 2)
    shake = strew(rng, GRAVEL, 0.5, env([(0, 0), (0.02, 80), (0.5, 0)], 0.5), (-12, -9), (0.02, 0.05))
    b.at(0.03, unit(shake), -4)
    y = far(b.x, rng, cut=THUD_AIR[k], wet=0.42, decay=2.6)
    return finish(hold(trimmed(y, L, 0.5), (30, 2500)), STIR_LUFS, PEAK)


@recipe("tell-wakers", "thud", "blow",
        "The stir, modelled: a far-off ground blow, the land's low modes booming, a faint crack of rock, its long coda",
        """Modelled from what the land does when something that size shifts under it: a pressure pulse through the
        ground (a Friedlander pulse whose sub drops in pitch as it dies) and the land's own broad low modes rung once
        (44-230 Hz, soil over rock, too damped to have a pitch), rock fracturing faintly in it (stick-slip quickening and
        dying through a slab's close modes, its pieces struck as it breaks, grit crackling), then the coda: the blow
        arriving again by every longer path through the ground, many soft low knocks thinning out over a rumble. 700 m
        off, the air darkening it, the hills answering. Same four distances as 'ground'. Held to 40-400 Hz.""",
        takes=4, band=STIR, lufs=STIR_LUFS)
def thud_blow(rng, k):
    L = THUD_L[k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 1.0)
    b.at(0, blow(rng, 1.0, rng.uniform(36, 44)), 0)
    b.at(0.006, lp(rock_model(rng, 0.25, rng.uniform(200, 260)), 2000), -4)
    b.at(0.006, unit(lp(synth.crackle(0.4, env([(0, 600), (0.4, 10)], 0.4), rng, size=(0.0005, 0.004), hi=300), 1500)), -9)
    b.at(0.06, coda(rng, L, rng.uniform(8, 12)), -12)
    y = far(b.x, rng, cut=THUD_AIR[k], wet=0.42, decay=2.6)
    return finish(hold(trimmed(y, L, 0.5), (30, 2500)), STIR_LUFS, PEAK)


# ---- The stir: a long, low call from behind the hills ------------------------------------------------------------------------

CALL_L = (6.0, 7.5, 5.2)               # the whole call with the hills' answer: the voice itself 1.6 s shorter


@recipe("tell-wakers", "call", "groan",
        "The stir: a long, low call from behind the hills, an enormous slow voice moaning and bending down",
        """Real throats, held out and dropped: a real roar's hardest stretch dropped an octave and a half and
        paulstretched into one long held voice (its grain kept, its animal jerks gone), a real groan-and-wail dropped two
        octaves held under it a little apart in pitch so the two beat as one throat, a real door creak dropped nearly
        two octaves for the land's own grain in it; all of it bending slowly down as it ends, a moan. A slow rattle
        gates it (ten-odd flaps a second, a throat a metre wide), a real gasp dropped an octave and held is the breath
        going out under it, and a breath drawn first. Far behind the hills: the air takes the top off, a long distant
        tail and the hills answering twice. 4.5-7.5 s. Held to 40-400 Hz, a little breath above.""",
        sources=[ROAR_REC, GROAN, GASP] + DOORS, takes=3, band=STIR, lufs=STIR_LUFS)
def call_groan(rng, k):
    L = CALL_L[k] * rng.uniform(0.95, 1.05)
    V = L - 1.6
    n = samples(V)
    sag = env([(0, rng.uniform(0.5, 1.5)), (V * 0.35, rng.uniform(1.5, 2.5)), (V * 0.75, 0), (V, -rng.uniform(4, 6))], V)
    v1 = held(rng, ROAR_REC, V, rng.uniform(-20, -18), sag)
    v2 = held(rng, GROAN, V, rng.uniform(-26, -25), sag + 0.3)
    land = held(rng, DOORS[k % 2], V, rng.uniform(-23, -21), sag * 0.6, piece=0.4)
    rat = fry(rng, env([(0, 9), (V * 0.5, 13), (V, 10)], V), V, jitter=0.4, decay=0.03)[:n]
    voice = unit(v1 * (0.6 + 0.4 * rat) + v2 * 0.55 * (0.7 + 0.3 * rat) + land * 0.35)
    sh = env([(0, 0), (V * 0.18, 0.75), (V * 0.4, 1), (V * 0.7, 0.85), (V * 0.9, 0.45), (V, 0)], V)[:n]
    sh = sh * lumpy(rng, V, 0.6, 0.25)
    exhale = held(rng, GASP, V, rng.uniform(-14, -12), sag * 0.5)
    b = Bus(L + 1.0)
    b.at(0, lp(held(rng, GASP, 0.9, rng.uniform(-16, -14), 0.0), 900) * env([(0, 0), (0.6, 1), (0.9, 0)], 0.9), -16)
    b.at(0.75, chest(voice * sh, 0.35), 0)
    b.at(0.75, lp(exhale, 1100) * sh, -15)
    y = far(b.x, rng, cut=1000, wet=0.5, decay=3.6, echoes=((0.7, -9), (1.5, -14)))
    return finish(hold(trimmed(y, L, 0.8), (30, 2000)), STIR_LUFS, PEAK)


@recipe("tell-wakers", "call", "throat",
        "The stir, modelled: a long, low call from behind the hills, a throat the size of a cave moaning down",
        """Modelled: a glottis at 35-70 Hz with period doubling in it, through a vocal tract the size of a cave (its
        formants at 150-1000 Hz, a third of a child's), gated by a slow rattle (ten-odd flaps a second) and blowing
        breath through the same tract; it swells in on 'oo', opens to 'oh' as it rises a little, and moans down nearly an
        octave as it ends, ringing a chest a house wide under it. A breath drawn first. Far behind the hills: the air
        takes the top off, a long distant tail and the hills answering twice. Not the Moose: no nasal 'mm', no climb, no
        crack; an octave under him and twice as long. 4.5-7.5 s. Held to 40-400 Hz.""",
        takes=3, band=STIR, lufs=STIR_LUFS)
def call_throat(rng, k):
    L = CALL_L[k] * rng.uniform(0.95, 1.05)
    V = L - 1.6
    f = rng.uniform(58, 64)
    pts = [(0, f * 0.85), (V * 0.3, f * 1.08), (V * 0.6, f), (V, f * rng.uniform(0.55, 0.62))]
    vow = [(0, "u"), (V * 0.3, "o"), (V * 0.65, "o"), (V, "u")]
    shape = [(0, 0), (V * 0.18, 0.75), (V * 0.4, 1), (V * 0.7, 0.85), (V * 0.9, 0.45), (V, 0)]
    v = giant(rng, pts, V, vow, shape, rattle=(9, 13), rasp=0.45, sub=0.5, k=rng.uniform(0.32, 0.36), breath=0.7)
    b = Bus(L + 1.0)
    br = synth.breath(0.9, "h", 0.22, rng, env([(0, 0), (0.6, 1), (0.9, 0)], 0.9))
    b.at(0, lp(br, 900), -18)
    b.at(0.75, hp(chest(v, 0.3), 70, 2), 0)
    y = far(b.x, rng, cut=1000, wet=0.5, decay=3.6, echoes=((0.7, -9), (1.5, -14)))
    return finish(hold(trimmed(y, L, 0.8), (30, 2000)), STIR_LUFS, PEAK)


# ---- The rise: the land getting up -------------------------------------------------------------------------------------------

RISE_L = 8.0


def surges(rng, length, count=3):
    """The land getting up in `count` heaves over `length` s, each swelling slower than it eases and each bigger than the
    last, over a low stir between: the per-sample curve (0-1) and each heave's peak (s)."""
    t = np.arange(samples(length)) / SR
    tops = [(i + rng.uniform(0.5, 0.65)) * (length - 1.0) / count for i in range(count)]
    c = np.zeros_like(t)
    for i, p in enumerate(tops):
        a = 0.65 + 0.35 * i / max(count - 1, 1)
        w = np.where(t < p, rng.uniform(0.7, 1.0), rng.uniform(0.45, 0.7))
        c = np.maximum(c, a * np.exp(-((t - p) / w) ** 2))
    c = np.maximum(c, 0.12 * np.clip(t / 0.5, 0, 1) * np.clip((length - t) / 0.8, 0, 1))
    return c.astype(np.float32), tops


def later(c, by):
    """A curve `by` s later: what's thrown up at a heave comes down after it."""
    k = samples(by)
    return np.concatenate([np.zeros(k, np.float32), c[:-k]]).astype(np.float32)


@recipe("tell-wakers", "rise", "earth",
        "The land getting up: earth and rock torn open in three surges, roots and stones popping, a groan rising, debris",
        """Real recordings, bent: the real thunder roll slowed an octave and held out in overlapping grains (and two
        octaves, under it) is the earth heaving, three surges each bigger than the last; at each one the land tears open
        (the thunder's crack slowed into a blow, a pick into rock dropped, real wood breaking dropped into rock splitting).
        Roots snap (real cracks strewn faster as each surge comes on), stones pop out of the ground (real stones knocked)
        and clods tumble (real gravel dropped half an octave); under it all a long groan rises (a real door creak dropped
        nearly two octaves and a real groan dropped two, held out and bending up as it gets up), and after each surge the
        debris patters down (real stones, rock and wood landing, grit trickling). 8 s, out in the country. Held to 40
        Hz-5 kHz.""",
        sources=[THUNDER, GROAN] + MINING + CRACKS + GRAVEL + DOORS + W.PIECES["brick"] + W.PIECES["wood"], takes=2,
        band=RISE, lufs=RISE_LUFS)
def rise_earth(rng, k):
    L = RISE_L
    n = samples(L)
    c, tops = surges(rng, L)
    b = Bus(L + 1.5)
    earth = lp(cloud(rng, THUNDER, n, -12, 1.2), 350) * c ** 1.3
    deep = lp(cloud(rng, THUNDER, n, -20, 1.5), 120) * c
    b.at(0, unit(earth), -6)
    b.at(0, unit(deep), -9)
    up = env([(0, -3), (L, 6)], L)
    groan = held(rng, DOORS[k % 2], L, rng.uniform(-22, -20), up, piece=0.4)
    voice = held(rng, GROAN, L, rng.uniform(-25, -24), up + 0.4)
    swell = np.clip(lp(c, 1.0) * 1.3, 0, 1) * env([(0, 0.3), (L, 1)], L)[:n]
    b.at(0, unit(groan * 0.8 + voice * 0.5) * (0.15 + 0.85 * swell), -8)
    b.at(0, splinter(rng, L, 2 + 70 * c ** 2, (-7, -2)), -9)
    b.at(0, unit(strew(rng, STONES, L, 2 + 40 * c ** 2, (-5, 2), (0.015, 0.05))), -10)
    b.at(0, unit(lp(strew(rng, GRAVEL, L, 15 + 160 * c, (-9, -4), (0.02, 0.06)), 2500)), -8)
    for i, p in enumerate(tops):
        g = -4 + 2 * i
        b.at(p - 0.05, strike(rng.uniform(-11, -9), 0.4, cut=600), g - 2)
        b.at(p - 0.04, unit(lp(dsp.vari(main_hit(MINING[(k + i) % 5], 0.5), rng.uniform(-5, -3)), 900)), g - 6)
        b.at(p - 0.03, rock(rng, rng.uniform(-6, -3), 0.45), g - 3)
        b.at(p + 0.25, W.debris(rng, 1.6, 14, {"brick": 3, "wood": 1}, decay=0.5, semis=(-6, -1)) * 0.5, g - 3)
        D = rng.uniform(1.2, 1.8)
        b.at(p + 0.4, grit(rng, D, env([(0, 0), (0.1, 140), (D, 0)], D)), g - 10)
    y = air(b.x, rng, 0.25, 1.0)
    y = trimmed(lp(y, 6000), L + 0.4, 0.6)
    return finish(hold(y, (30, 7000)), RISE_LUFS, PEAK)


@recipe("tell-wakers", "rise", "heave",
        "The land getting up, modelled: the ground heaving and tearing in three surges, roots, stones, a hill's groan",
        """Modelled from what the land does as something under it gets up: a band of low noise heaving in three surges,
        each bigger than the last; fracture running through earth and rock (stick-slip quickening to a roar as each surge
        comes on, rung through soil's and rock's broad modes, its crackle on top); roots stretched and snapping (slow
        stick-slip through thick wet wood, fibres popping); stones popping out of the ground and thrown, landing again
        a moment later; at each surge's top the land torn open (a ground blow and a slab of rock breaking); under it all
        the land's own groan rising (slow stick-slip through a hill's low modes, quickening as it lifts); and after
        each surge the earth and stones pour back down in a pattering shower. 8 s. Held to 40 Hz-5 kHz.""",
        takes=2, band=RISE, lufs=RISE_LUFS)
def rise_heave(rng, k):
    L = RISE_L
    n = samples(L)
    c, tops = surges(rng, L)
    b = Bus(L + 1.5)
    earth = lp(synth.noise(L, rng, "pink"), 150, 2) * c ** 1.3 * lumpy(rng, L, 5, 0.5)
    b.at(0, unit(hp(earth, 30)), -6)
    tear = W.tear(L, rng, 6 + 260 * c ** 2, (110, 2200), q=9, crack=0.5) * c
    b.at(0, unit(tear), -6)
    roots = synth.creak(L, 6 + 50 * c, rng, body=[f * rng.uniform(0.5, 0.7) for f in synth.WOOD], q=9, jitter=0.6, grit=0.6)
    fibres = lp(synth.crackle(L, 20 + 300 * c ** 2, rng, size=(0.0003, 0.003), hi=500), 4000)
    b.at(0, unit(roots * c), -11)
    b.at(0, unit(fibres), -10)
    rate = env([(0, 12), (L, 55)], L)
    groan = synth.creak(L, rate, rng, body=[f * rng.uniform(0.95, 1.05) for f in HILL], q=10, jitter=0.4, grit=0.4)
    groan = hp(groan, 70) * (0.2 + 0.8 * np.clip(lp(c, 1.0) * 1.3, 0, 1))
    b.at(0, unit(groan), -9)
    for t in W.poisson(L, 6.0, rng):
        s = rng.random() ** 2 * c[min(samples(t), n - 1)]
        b.at(t, stone_model(rng, s), -16 + 12 * s)
    for i, p in enumerate(tops):
        g = -4 + 2 * i
        b.at(p - 0.05, blow(rng, 0.9, rng.uniform(40, 50)), g - 2)
        b.at(p - 0.03, rock_model(rng, 0.4), g - 4)
        for t, x, db in flight(rng, 18, stone_model, loud=-12):
            b.at(p + t, x, db + g)
    fall = W.pour(n, rng, 20 + 700 * later(c, 0.35) ** 2, grain=(800, 4000), lump=3, thunder=0.4, loop=False)
    b.at(0, unit(fall), -7)
    b.at(0, sift(rng, L, 10 + 120 * later(c, 0.5)), -14)
    y = air(b.x, rng, 0.25, 1.0)
    y = trimmed(lp(y, 6000), L + 0.4, 0.6)
    return finish(hold(y, (30, 7000)), RISE_LUFS, PEAK)


# ---- Its stride: a footfall as it comes down the line ------------------------------------------------------------------------

STRIDE_L = (1.9, 2.2, 2.4, 1.7)


@recipe("tell-wakers", "stride", "ground",
        "Its footfall as it comes down the line: the ground struck, a crack of rock, stones jumping and landing, earth",
        """Real recordings, bent: the ground struck by a foot the size of a barn (the real thunder's crack slowed an octave
        into the blow, a pick into rock and heavy blows on timber dropped under it for the mass of the earth), and in the
        same instant the rock under it splitting (real wood breaking dropped half an octave, real stones knocking). The
        stones it throws up come down again a moment later, each after its own flight (real stones knocked, a few big,
        many small, some bouncing), clods and gravel fall back (real gravel dropped half an octave) and grit trickles
        after. One crosses the line's ballast with a sleeper splitting under it, one comes down in a cutting with the rock
        cracking louder, one through trees with a trunk snapping. No step: no heel, no sole, nothing a crewmate's made big.
        Under 45% of it below 150 Hz (the squish rule): its weight is in the rock, the stones and the earth as much as
        the blow. 1.5-2.4 s, out in the country. Held to 60 Hz-6 kHz.""",
        sources=[THUNDER] + MINING + DECK + CRACKS + GRAVEL + PLANK + DOORS, takes=4, band=STRIDE, lufs=STRIDE_LUFS)
def stride_ground(rng, k):
    L = STRIDE_L[k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 1.0)
    hit = strike(rng.uniform(-14, -11), 0.35, cut=700)
    b.at(0, dsp.shaped(hit, [(0, 1), (0.08, 0.55), (0.5, 0.08), (1.0, 0.0)], "exp"), -7)
    b.at(0.002, unit(lp(dsp.vari(main_hit(MINING[k % 5], 0.5), rng.uniform(-3, -1)), 1200)), -9)
    b.at(0.002, unit(dsp.vari(main_hit(DECK[k % 2], 0.4), rng.uniform(-9, -7))), -5)
    b.at(0.003, rock(rng, rng.uniform(-7, -4), 0.4), 2 if k == 2 else 0)
    b.at(0.0, unit(strew(rng, STONES, 0.12, 300, (-3, 3), (0.01, 0.04))), -9 + (4 if k == 0 else 0))
    for t, x, db in flight(rng, 26, stone_rec, loud=-8):
        b.at(t, x, db)
    D = L * 0.6
    clods = strew(rng, GRAVEL, D, env([(0, 220), (D, 0)], D), (-9, -4), (0.02, 0.06))
    b.at(0.05, unit(lp(clods, 3500)), -11 + (4 if k == 0 else 0))
    b.at(0.15, grit(rng, L - 0.2, env([(0, 0), (0.1, 120), (L - 0.2, 0)], L - 0.2)), -20)
    if k == 1:          # a sleeper splitting under it, the ballast thrown
        b.at(0.004, unit(dsp.vari(main_hit(PLANK[int(rng.integers(5))], 0.4), rng.uniform(-6, -4))), -7)
        b.at(0.01, splinter(rng, 0.3, env([(0, 300), (0.3, 10)], 0.3), (-5, -1)), -9)
    elif k == 3:        # a trunk snapped underfoot
        b.at(0.006, unit(dsp.vari(main_hit(CRACKS[1], 0.4), rng.uniform(-5, -3))), -4)
        b.at(0.02, creak_rec(rng, 0.35, rng.uniform(-8, -6), DOORS[0]) * env([(0, 1), (0.35, 0)], 0.35), -14)
    y = air(b.x, rng, 0.18, 0.8)
    return finish(hold(trimmed(hp(y, 60, 2), L, 0.4), STRIDE), STRIDE_LUFS, PEAK)


@recipe("tell-wakers", "stride", "blow",
        "Its footfall, modelled: the ground struck, a slab of rock breaking, stones flying and landing, earth falling",
        """Modelled: the ground struck (a pressure pulse through it, the land's broad low modes rung once, a chunk of soil
        and rock struck dull in the mids), a slab of rock breaking under it (fracture through its close modes, its bending
        modes, the hard edge of the break), a clatter of grit flung at the blow, then the stones it throws coming down
        again, each after its own flight (small hard bodies struck, the big ones lower and duller, some bouncing), and the
        earth pouring back in a shower thinning out. Same four grounds as 'ground', by their weight and their rock. Under
        45% below 150 Hz (the squish rule). 1.5-2.4 s. Held to 60 Hz-6 kHz.""",
        takes=4, band=STRIDE, lufs=STRIDE_LUFS)
def stride_blow(rng, k):
    L = STRIDE_L[k] * rng.uniform(0.95, 1.05)
    n = samples(L)
    b = Bus(L + 1.0)
    b.at(0, blow(rng, 0.6, rng.uniform(55, 65), 0.6, sub=0.3), 2)
    b.at(0, struck(rng, [f * rng.uniform(0.9, 1.1) for f in CLOD], 3, 0.3, hit=0.004), -8)
    b.at(0.002, rock_model(rng, 0.35, rng.uniform(220, 360) * (0.8 if k == 2 else 1.0)), -2 + (2 if k == 2 else 0))
    b.at(0, unit(synth.skitter(0.12, 300, rng, f=(1200, 4500), q=(4, 9), legs=10)), -12)
    for t, x, db in flight(rng, 26, stone_model, loud=-8):
        b.at(t, x, db)
    rate = env([(0, 0), (0.15, 900), (L * 0.6, 60), (L, 0)], L)
    b.at(0.05, unit(W.pour(n, rng, rate, grain=(700, 3500), lump=2, thunder=0.25, loop=False)), -12)
    if k == 1:          # a sleeper splitting under it
        f1 = rng.uniform(170, 220)
        b.at(0.004, struck(rng, [f1 * r for r in W.BAR[:4]], 9, 0.25, 0.002), -8)
        b.at(0.01, unit(lp(synth.crackle(0.3, env([(0, 1500), (0.3, 30)], 0.3), rng, size=(0.0003, 0.003), hi=500), 5000)),
             -11)
    elif k == 3:        # a trunk snapped underfoot
        f1 = rng.uniform(120, 160)
        b.at(0.006, struck(rng, [f1 * r for r in W.BAR[:4]], 12, 0.35, 0.001), -5)
        b.at(0.006, unit(hp(burst(rng, 0.004, attack=0.05), 1200)), -6)
    y = air(b.x, rng, 0.18, 0.8)
    return finish(hold(trimmed(hp(y, 60, 2), L, 0.4), STRIDE), STRIDE_LUFS, PEAK)


# ---- Its roar ----------------------------------------------------------------------------------------------------------------

ROAR_L = (3.0, 3.6, 2.6)               # the roar itself: its breath in before it and the land's answer after


@recipe("tell-wakers", "roar", "throat",
        "A roar as it runs the train down, or stood at the walls: huge, harsh and slow, a throat the size of a house",
        """Real throats, bent to the size: a real roar's hardest stretch dropped more than an octave and drawn out to the
        roar's length (its rasp kept), the same roar dropped two octaves and smeared under it (a chest the size of a hall,
        beating against it), a real groan-and-wail dropped an octave and a half held in it for its ragged, inhuman edge.
        A slow rattle tears through all of it (11-17 flaps a second), a real gasp dropped and held is the air roaring out
        of a mouth a cave wide, and the real thunder roll slowed an octave is the ground under it shaking with it. It drags
        a breath in first; the roar heaves twice, climbs a little and sags as it ends. Driven hard, out in the open with
        the land answering. 3.5-4.6 s. Held to 50 Hz-3 kHz: huge, never shrill.""",
        sources=[ROAR_REC, GROAN, GASP, THUNDER], takes=3, band=ROAR, lufs=ROAR_LUFS)
def roar_throat(rng, k):
    L = ROAR_L[k] * rng.uniform(0.95, 1.05)
    n = samples(L)
    sag = env([(0, -1.5), (L * 0.25, 1.0), (L * 0.6, 0.5), (L, -4)], L)
    top = dsp.vari(core(ROAR_REC, 0.35), rng.uniform(-15, -13))
    top = unit(dsp.fit(bend(dsp.stretch(top, (L * 1.2 + 0.2) / (len(top) / SR)), sag), n))
    low = held(rng, ROAR_REC, L, rng.uniform(-25, -23), sag + 0.25)
    edge = held(rng, GROAN, L, rng.uniform(-18, -16), sag)
    rat = fry(rng, env([(0, 11), (L * 0.5, 17), (L, 12)], L), L, jitter=0.45, decay=0.02)[:n]
    breath = held(rng, GASP, L, rng.uniform(-9, -7), sag * 0.5)
    v = (top * (0.35 + 0.65 * rat) + low * 0.7 * (0.5 + 0.5 * rat) + edge * 0.35 * (0.4 + 0.6 * rat)
         + unit(bp(breath, 300, 3000)) * 0.4 * (0.6 + 0.4 * rat))
    heave = env([(0, 0), (0.12, 0.8), (L * 0.3, 1), (L * 0.45, 0.75), (L * 0.62, 1), (L * 0.85, 0.7), (L, 0)], L)[:n]
    v = dsp.saturate(chest(unit(v) * heave, 0.4), 8)
    ground = lp(cloud(rng, THUNDER, n, -12, 1.0), 220) * heave
    i = rng.uniform(0.35, 0.5)
    b = Bus(L + i + 1.5)
    b.at(0, lp(held(rng, GASP, i, rng.uniform(-12, -10), 1.0), 1500) * env([(0, 0), (i * 0.8, 1), (i, 0.3)], i), -12)
    b.at(i, v, 0)
    b.at(i, unit(ground), -12)
    y = W.space(dsp.trim_silence(b.x, -60), rng, "night", wet=0.25)
    y = mix(y, lp(dsp.fit(np.concatenate([np.zeros(samples(0.55), np.float32), y]), len(y)), 1200) * 0.18)
    return finish(hold(trimmed(y, L + i + 0.7, 0.5), ROAR), ROAR_LUFS, PEAK)


@recipe("tell-wakers", "roar", "bellows",
        "A roar, modelled: a glottis the size of a cave, a slow tearing rattle, the air roaring out of it like a furnace",
        """Modelled: a glottis at 35-70 Hz with period doubling in it, through a tract a fifth of a child's (formants at
        100-750 Hz) opening from 'oh' to 'ah', gated by a slow tearing rattle (11-18 flaps a second); the air driven out
        of it a jet's worth, roaring broad and low (a turbulent jet whose noise peaks near 700 Hz, its plume billowing,
        its crackle), the rattle tearing the jet too; a chest a house wide ringing under it. It drags a breath in, heaves
        twice and sags as it ends. Driven hard and folded a little for the inhuman grit, out in the open with the land
        answering. Not the Moose: no nasal 'mm', no crack up a fifth, an octave under his bellow and its air louder than
        its voice. 3.5-4.6 s. Held to 50 Hz-3 kHz.""",
        takes=3, band=ROAR, lufs=ROAR_LUFS)
def roar_bellows(rng, k):
    L = ROAR_L[k] * rng.uniform(0.95, 1.05)
    n = samples(L)
    f = rng.uniform(52, 60)
    pts = [(0, f * 0.7), (0.3, f), (L * 0.35, f * 1.18), (L * 0.6, f * 1.1), (L * 0.85, f * 0.9), (L, f * 0.65)]
    vow = [(0, "o"), (0.35, "a"), (L * 0.7, "a"), (L, "o")]
    shape = [(0, 0), (0.12, 0.8), (L * 0.3, 1), (L * 0.45, 0.75), (L * 0.62, 1), (L * 0.85, 0.7), (L, 0)]
    v = giant(rng, pts, L, vow, shape, rattle=(11, 18), rasp=0.75, sub=0.6, k=rng.uniform(0.2, 0.23), breath=0.9)
    heave = dsp.fit(env(shape, L), n)
    jet = W.jet(L, rng, pressure=0.15 + 0.85 * heave, opening=1.0, peak=rng.uniform(600, 800), low=0.8, rasp=0.6, eddy=0.9,
                n=n)
    rat = fry(rng, env([(0, 11), (L * 0.5, 18), (L, 12)], L), L, jitter=0.45, decay=0.02)[:n]
    jet = unit(lp(jet, 3000)) * heave * (0.4 + 0.6 * rat)
    y = chest(mix(v, jet * 0.75), 0.4)
    y = dsp.saturate(mix(y, dsp.fold(y, 1.5) * 0.25), 9)
    i = rng.uniform(0.35, 0.5)
    b = Bus(L + i + 1.5)
    br = synth.breath(i, "h", 0.25, rng, env([(0, 0), (i * 0.8, 1), (i, 0.3)], i))
    b.at(0, lp(br, 1500), -12)
    b.at(i, unit(y), 0)
    y = W.space(dsp.trim_silence(b.x, -60), rng, "night", wet=0.25)
    y = mix(y, lp(dsp.fit(np.concatenate([np.zeros(samples(0.55), np.float32), y]), len(y)), 1200) * 0.18)
    return finish(hold(trimmed(y, L + i + 0.7, 0.5), ROAR), ROAR_LUFS, PEAK)


# ---- cs-wakers: the grab -----------------------------------------------------------------------------------------------------

GRAB_L = (2.5, 2.9)


def shriek_rec(rng, length, k):
    """Steel shrieking as the hand closes: a real metal scrape held endlessly (its strokes cut into grains laid end over
    end) and pitched down for a car's steel, rung through its modes so the scrape itself shrieks; bell metal frozen into
    a held ring under it, bending up as the steel yields."""
    n = samples(length)
    drag = scrape_grains(n, rng, 70, semis=(-6, -3), size=(0.08, 0.2), gain=(0.6, 1.0))
    rung = dsp.resonate(drag, [rng.uniform(1500, 1800), rng.uniform(2300, 2600), rng.uniform(3200, 3600)], q=45,
                        gains=[1, 0.7, 0.5])
    ring = held_ring(BELLS[k % 5], rng.uniform(1700, 2000), n, rng)
    ring = dsp.fit(bend(np.concatenate([ring, ring]), env([(0, 0), (length * 0.7, 4), (length, 5)], length)), n)
    y = unit(drag) * 0.5 + unit(dsp.saturate(unit(rung), 4)) * 0.6 + unit(ring) * 0.45
    return y * env([(0, 0), (0.08, 0.7), (length * 0.35, 1), (length * 0.8, 0.55), (length, 0)], length)


def snatch_rec(rng, i):
    """One coupling jerked taut: its knuckle's cast steel clanking (real heavy plate and metal, choked), the drawgear's
    springs knocking."""
    return ck.norm(hp(mix(iron(rng, i, rng.uniform(-5, -3), 0.06, 0.6),
                          unit(dsp.vari(hit_of(KNOCK, 0, 0.2), rng.uniform(-4, -2))) * 0.4), 150))


@recipe("cs-wakers", "grab", "seized",
        "The last car seized: a blow through the whole train, steel shrieking as a hand closes, couplings snatching taut",
        """Real recordings: the blow of a hand the size of a barn landing on the car, every part of it struck at once (a
        real blow on a steel sheet, heavy plate and metal dropped for the car's frame, a heavy knock-slam, heavy blows
        on planking for its body, the thunder's crack slowed for the weight behind it, held off the sub), boards
        cracking; then the steel shrieking as the hand closes on it (a real metal scrape held without end, pitched down
        and rung through the car's steel, bell metal frozen into a held ring bending up as the steel yields) and the
        couplings snatching taut down the train, a clank at each car in turn toward the engine (real heavy plate and
        metal, choked), each a car further off. Under 45% below 150 Hz (the squish rule). Held to 80 Hz-8 kHz.""",
        sources=[SLAM, KNOCK, SCRAPE, THUNDER] + PLATE_H + METAL_H + DECK + BELLS[:2] + CRACKS, takes=2, band=TRAIN,
        lufs=GRAB_LUFS)
def grab_seized(rng, k):
    L = GRAB_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    b.at(0, unit(dsp.vari(hit_of(SLAM, 0, 0.8), rng.uniform(-7, -5))), 0)
    b.at(0.002, iron(rng, k, -6, 0.12, 0.8), -3)
    b.at(0.001, unit(dsp.vari(hit_of(KNOCK, 0, 0.4), rng.uniform(-6, -4))), -4)
    b.at(0.003, unit(dsp.vari(main_hit(DECK[k % 2], 0.5), rng.uniform(-8, -6))), -4)
    b.at(0, strike(rng.uniform(-11, -9), 0.3, cut=500), -3)
    b.at(0.02, splinter(rng, 0.6, env([(0, 250), (0.6, 10)], 0.6), (-6, -2)), -12)
    D = rng.uniform(1.4, 1.8)
    b.at(0.06, shriek_rec(rng, D, k), -5)
    b.at(rng.uniform(0.15, 0.22), run_down(rng, snatch_rec, (0.14, 0.2), 7), -3)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 90, 2), L, 0.4), TRAIN), GRAB_LUFS, PEAK)


def shriek_model(rng, length):
    """Steel shrieking as the hand closes, modelled: the car's sheet tearing (stick-slip quickening into a scream through
    its patches' modes) and its frame's modes locked into a squeal that bends up as the steel yields and judders as the
    grip slips and catches."""
    n = samples(length)
    tr = W.tear(length, rng, env([(0, 80), (length * 0.4, 900), (length, 250)], length), (500, 5000), q=18, crack=0.4)
    t = np.arange(n) / SR
    out = np.zeros(n, np.float32)
    for f0, g in ((rng.uniform(1500, 1700), 1.0), (rng.uniform(2300, 2500), 0.6), (rng.uniform(3300, 3500), 0.4)):
        f = f0 * 2 ** (np.interp(t, [0, length * 0.7, length], [0, 4, 5]) / 12)
        ph = 2 * np.pi * np.cumsum(f) / SR
        out += g * (np.sin(ph) + 0.4 * np.sin(2 * ph + 0.5)).astype(np.float32)
    chatter = 1 - 0.6 * judder(n, rng, 25, 70)
    y = unit(tr) * 0.6 + unit(out * chatter) * 0.5
    return y * env([(0, 0), (0.08, 0.7), (length * 0.35, 1), (length * 0.8, 0.55), (length, 0)], length)


@recipe("cs-wakers", "grab", "hand",
        "The last car seized, modelled: the car struck all over at once, its steel screaming, the couplings snatching",
        """Modelled: the car struck all over at once by a hand that size (its sheet steel's dense modes rung from the
        frame up, crumpling for a quarter of a second, iron struck and torn, its boards splintering), then its steel
        shrieking as the hand closes (the sheet tearing in stick-slip that quickens into a scream, its frame's modes
        locked into a squeal that bends up as the steel yields and judders as the grip slips and catches), and the jolt
        running down the train: a heavy cast-steel knuckle clanking at each coupling in turn toward the engine, each a
        car further off. Under 45% below 150 Hz (the squish rule). Held to 80 Hz-8 kHz.""",
        takes=2, band=TRAIN, lufs=GRAB_LUFS)
def grab_hand(rng, k):
    L = GRAB_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    b.at(0, hp(W.crash(rng, 1.3, iron=1.0, wood=0.7, crumple=0.25), 70, 2), 0)
    D = rng.uniform(1.4, 1.8)
    b.at(0.06, shriek_model(rng, D), -4)
    snatch = run_down(rng, lambda r, i: hp(clank(r, r.uniform(280, 380), 1.0), 150), (0.14, 0.2), 7)
    b.at(rng.uniform(0.15, 0.22), snatch, -3)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 90, 2), L, 0.4), TRAIN), GRAB_LUFS, PEAK)


# ---- cs-wakers: the drag -----------------------------------------------------------------------------------------------------

DRAG_CYCLE = 6.0      # registered with build.recipe itself: crew_kit's 25 Hz highpass would start the cycle from rest


@build.recipe("cs-wakers", "drag", "skid",
        "The train dragged to a stand: wheels locked and screaming on the rails, steel grinding, flanges shrieking",
        """Real recordings, held: every locked wheel's tread sliding on the rail head is a real metal scrape cut into
        grains laid end over end so it never stops, rung through a wheel's modes so the slide itself screams; struck bell
        metal frozen into held rings at three wheel pitches (two a few hertz apart, so they beat) is the scream's tone,
        its level chattering as the contact sticks and slips; the same scrape dropped most of an octave is the steel
        grinding under it; held plate metal high over it is a flange shrieking now and then; and the slack snatches in
        the couplings twice a cycle (real heavy plate and metal, choked). 6 s exact cycle. Held to 250 Hz-8 kHz.""",
        sources=[SCRAPE] + BELLS[:3] + PLATES_L[:1] + PLATE_H + METAL_H, takes=1, loop=True, band=DRAG, lufs=DRAG_LUFS,
        seconds=DRAG_CYCLE)
def drag_skid(rng, k):
    n = samples(DRAG_CYCLE)
    slide = scrape_grains(n, rng, 110, semis=(-3, 0), size=(0.06, 0.16), gain=(0.7, 1.0))
    slide = W.norm(slide / (W.cfilter(np.abs(slide), lambda f: 1 / (1 + (f / 3) ** 2)) + 0.05 * np.max(np.abs(slide))))
    rung = W.cyclic(lambda x: dsp.resonate(x, [1880, 2750, 3640], q=60, gains=[1, 0.7, 0.5]), slide)
    rings = [held_ring(BELLS[0], 2150, n, rng), held_ring(BELLS[1], 2185, n, rng), held_ring(BELLS[2], 3380, n, rng)]
    chat = [1 - 0.55 * judder(n, rng, 18 + 6 * i, 60) for i in range(3)]
    lvl = [np.clip(0.75 + 0.35 * W.slow(n, 0.4, rng), 0.3, 1) for _ in range(3)]
    tone = sum(r * c * v * g for r, c, v, g in zip(rings, chat, lvl, (1.0, 0.85, 0.6)))
    grind = scrape_grains(n, rng, 80, semis=(-10, -7), size=(0.08, 0.2), gain=(0.6, 1.0))
    flange = held_ring(PLATES_L[0], rng.uniform(4800, 5200), n, rng) * np.clip(1.4 * W.slow(n, 0.5, rng) - 0.4, 0, 1)
    snaps = W.place(n, [(t, iron(rng, i, -5, 0.08, 0.6), rng.uniform(0.6, 0.9))
                        for i, t in enumerate((rng.uniform(0.3, 1.0), rng.uniform(3.2, 4.0)))])
    y = (W.norm(slide) * 0.35 + W.norm(rung) * 0.4 + dsp.saturate(W.norm(tone), 6) * 0.55 + W.norm(grind) * 0.35
         + W.norm(flange) * 0.18 + W.norm(snaps) * 0.4)
    return close(y, rng, DRAG, wet=0.1, drive=4, lufs=DRAG_LUFS)


@build.recipe("cs-wakers", "drag", "locked",
        "The train dragged to a stand, modelled: locked wheels sliding and screaming, the steel grinding, flanges",
        """Modelled: steel sliding on steel under a car's weight (contact noise roughened by the asperities, rung through a
        wheel's modes), every locked wheel's modes locked into a scream (2.1-3.4 kHz, the close two beating, shivering
        and never letting go), the contact chattering as it sticks and slips (a fast uneven flutter, heard as roughness),
        a flange shrieking higher now and then, the cars' frames groaning against the hold (slow stick-slip through iron)
        and the slack snatching in the couplings twice a cycle (a cast-steel knuckle's clank). 6 s exact cycle. Held to
        250 Hz-8 kHz.""",
        takes=1, loop=True, band=DRAG, lufs=DRAG_LUFS, seconds=DRAG_CYCLE)
def drag_locked(rng, k):
    n = samples(DRAG_CYCLE)
    slide = W.friction(n, rng, WHEEL, rough=2.5, grit=1.0, q=14, loop=True)
    on = np.clip(0.85 + 0.25 * W.slow(n, 0.8, rng), 0.4, 1)
    scream = W.squeal(n, rng, [2140, 2178, 3380], on, wander=0.012, rate=4.0, harm=0.4)
    flange = W.squeal(n, rng, [5150, 6020], np.clip(1.4 * W.slow(n, 0.5, rng) - 0.4, 0, 1), wander=0.02, rate=5, harm=0.3)
    j = judder(n, rng, 30, 90)
    frame = W.friction(n, rng, [f * 0.8 for f in synth.IRON[:5]], rough=1.5, grit=0.2, q=25, loop=True)
    frame = W.cfilter(frame, lambda f: (f / 250) ** 2 / (1 + (f / 250) ** 2))
    frame = frame * np.clip(0.6 + 0.4 * W.slow(n, 0.3, rng), 0.2, 1)
    snaps = W.place(n, [(t, clank(rng, rng.uniform(280, 360)), rng.uniform(0.6, 0.9))
                        for t in (rng.uniform(0.3, 1.0), rng.uniform(3.2, 4.0))])
    y = (W.norm(slide) * 0.45 * (1 - 0.4 * j) + dsp.saturate(W.norm(scream) * (1 - 0.5 * j), 6) * 0.5
         + W.norm(flange) * 0.18 + W.norm(frame) * 0.2 + W.norm(snaps) * 0.4)
    return close(y, rng, DRAG, wet=0.1, drive=4, lufs=DRAG_LUFS)


# ---- cs-wakers: a car lifted -------------------------------------------------------------------------------------------------

LIFT_L = (2.4, 2.8, 2.2)
SNAP_AT = (0.05, 0.9, 0.5)          # when the coupling goes: first, after the groan, or between the scrape and the groan


def snap_rec(rng, k):
    """A coupling snapping: a sharp crack of iron (a bright real metal hit and a heavy one, a real board breaking for the
    fracture's edge, a switch's crack for its report), the knuckle swinging free and clanking, the pin and chain
    rattling."""
    b = Bus(1.0)
    b.at(0, unit(dsp.vari(hit_of(SNAP, 0, 0.2), rng.uniform(-3, -1))), 0)
    b.at(0, unit(dsp.vari(main_hit(METAL_H[k % 5], 0.3), rng.uniform(-4, -2))), -3)
    b.at(0, unit(dsp.vari(main_hit(CRACKS[k % 2], 0.25), rng.uniform(-3, -1))), -6)
    b.at(0, unit(dsp.vari(main_hit(WHIP[k % 2], 0.08), rng.uniform(-6, -4))), -6)
    b.at(rng.uniform(0.12, 0.2), iron(rng, k + 1, -3, 0.08, 0.7), -6)
    b.at(rng.uniform(0.2, 0.3), unit(dsp.vari(hit_of(RATTLE, 0, 0.25), rng.uniform(-3, 0))), -14)
    return b.x


@recipe("cs-wakers", "lift", "torn",
        "A car torn up off the rails: its frame groaning and twisting, its trucks scraping off the rail, couplings snapping",
        """Real recordings: the car's frame groaning and twisting as it's hauled up nose-first (real door creaks held out
        and dropped an octave, bending up as the frame racks, a real metal scrape slowed for its iron straps), its trucks
        scraping off the rail (the scrape's strokes held and laid end over end, pitched for a wheel's flange on the rail
        head) and dropping off it with a heavy clank of iron as they swing free, and the coupling snapping (a sharp
        real metal hit, a heavy one, a board breaking and a switch's crack for the report), its knuckle swinging free and
        clanking, the pin and chain rattling; ballast and loose things falling off the car. Three orders: the coupling
        first, after the groan, between the scrape and the groan. 2.2-2.8 s. Held to 80 Hz-8 kHz.""",
        sources=[SNAP, SCRAPE, RATTLE] + METAL_H + PLATE_H + CRACKS + WHIP + DOORS + W.PIECES["brick"] + W.PIECES["scrap"],
        takes=3, band=TRAIN, lufs=LIFT_LUFS)
def lift_torn(rng, k):
    L = LIFT_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    G = L - 0.2
    g = creak_rec(rng, G, rng.uniform(-12, -9), DOORS[k % 2])
    g = dsp.fit(bend(g, env([(0, 0), (G, 3)], G)), samples(G)) * env([(0, 0), (G * 0.3, 0.8), (G * 0.7, 1), (G, 0)], G)
    b.at(0.05, unit(g), -4)
    strap = held(rng, SCRAPE, G, rng.uniform(-13, -11), env([(0, 0), (G, 2)], G))
    b.at(0.05, lp(strap, 3000) * env([(0, 0), (G * 0.4, 1), (G, 0)], G), -14)
    t1 = (0.4, 0.2, 0.0)[k] + rng.uniform(0.0, 0.1)
    D = rng.uniform(0.5, 0.7)
    sc = scrape_grains(samples(D), rng, 60, semis=(-3, 0), size=(0.06, 0.14), gain=(0.6, 1.0))
    b.at(t1, unit(sc) * env([(0, 0), (0.05, 1), (D * 0.8, 0.8), (D, 0)], D), -7)
    b.at(t1 + D - 0.02, iron(rng, k + 2, -5, 0.12, 0.7), -4)
    b.at(t1 + D + rng.uniform(0.15, 0.3), iron(rng, k + 3, -4, 0.06, 0.5), -13)
    b.at(SNAP_AT[k] + rng.uniform(0, 0.08), snap_rec(rng, k), -1)
    b.at(L * 0.45, W.debris(rng, L * 0.5, 10, {"brick": 2, "scrap": 1}, decay=0.4, semis=(-4, 2)) * 0.5, -10)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 80, 2), L, 0.4), TRAIN), LIFT_LUFS, PEAK)


def snap_model(rng):
    """A coupling snapping, modelled: one hard impulse through a forged bar's bending modes (a crack, not a clang: it dies
    fast), the edge of the fracture, then the knuckle swinging free and clanking."""
    b = Bus(1.0)
    f1 = rng.uniform(420, 560)
    b.at(0, struck(rng, [f1 * r for r in W.BAR], 30, 0.25, hit=0.0002), 0)
    b.at(0, unit(hp(burst(rng, 0.002, attack=0.05), 1500)), -2)
    b.at(0.001, unit(lp(W.tear(0.06, rng, 1500, (800, 6000), q=20, crack=0.8), 9000)), -6)
    b.at(rng.uniform(0.12, 0.2), clank(rng, rng.uniform(300, 380)), -6)
    b.at(rng.uniform(0.2, 0.3), ck.grains(rng, int(rng.integers(5, 9)), 0.18, 900, 3200, q=(10, 18)), -14)
    return b.x


@recipe("cs-wakers", "lift", "frame",
        "A car torn up off the rails, modelled: the frame racking, the trucks grinding off the rail, a coupling cracking",
        """Modelled: the car's frame racking as it's hauled up nose-first (slow stick-slip through its timbers' and its iron
        straps' modes, quickening and bending up as it twists, rivets letting go), its trucks grinding off the rail (steel
        sliding on steel through a wheel's modes, a flange squealing) and dropping off it with a cast-iron clank as they
        swing free, and the coupling snapping (one hard impulse through a forged bar's modes, the edge of the fracture),
        its knuckle swinging free and clanking, the pin rattling; grit and stones falling off the car. Same three orders
        as 'torn'. 2.2-2.8 s. Held to 80 Hz-8 kHz.""",
        takes=3, band=TRAIN, lufs=LIFT_LUFS)
def lift_frame(rng, k):
    L = LIFT_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    G = L - 0.2
    body = [f * 0.6 for f in synth.WOOD[:5]] + [f * 0.8 for f in synth.IRON[:4]]
    g = synth.creak(G, env([(0, 10), (G * 0.7, 45), (G, 30)], G), rng, body=body, q=9, jitter=0.5, grit=0.5)
    g = dsp.fit(bend(g, env([(0, 0), (G, 3)], G)), samples(G)) * env([(0, 0), (G * 0.3, 0.8), (G * 0.7, 1), (G, 0)], G)
    b.at(0.05, unit(hp(g, 100)), -4)
    rivets = [(t, W.body(rng, rng.uniform(2500, 4000), W.BAR, decay=0.05, contact=0.0001, length=0.15), 1.0)
              for t in np.sort(rng.uniform(G * 0.3, G, 5))]
    b.at(0.05, unit(W.place(samples(G + 0.2), rivets, loop=False)), -16)
    t1 = (0.4, 0.2, 0.0)[k] + rng.uniform(0.0, 0.1)
    D = rng.uniform(0.5, 0.7)
    m = samples(D)
    sc = W.friction(m, rng, WHEEL, rough=2.2, grit=1.0, q=16) * env([(0, 0), (0.05, 1), (D * 0.8, 0.8), (D, 0)], D)[:m]
    sq = W.squeal(m, rng, [rng.uniform(2300, 2700)], env([(0, 0), (0.1, 1), (D * 0.7, 0.8), (D, 0)], D)[:m], harm=0.3)
    b.at(t1, unit(sc) + unit(sq) * 0.3, -7)
    b.at(t1 + D - 0.02, clank(rng, rng.uniform(260, 320)), -4)
    b.at(t1 + D + rng.uniform(0.15, 0.3), clank(rng, rng.uniform(300, 360), 0.5), -12)
    b.at(SNAP_AT[k] + rng.uniform(0, 0.08), snap_model(rng), -1)
    b.at(L * 0.45, unit(W.pour(samples(L * 0.5), rng, env([(0, 300), (L * 0.5, 0)], L * 0.5), grain=(900, 4500), lump=1.5,
                               thunder=0.1, loop=False)), -16)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 80, 2), L, 0.4), TRAIN), LIFT_LUFS, PEAK)


# ---- cs-wakers: a car eaten --------------------------------------------------------------------------------------------------

EAT_L = (2.8, 3.6, 2.4)
BITES = ((0.0, 1.0), (0.0, 0.9, 1.9), (0.0, 0.75))     # each take's jaws closing (s)


def bite_rec(rng, i, glass):
    """One bite through a car: the jaws meeting through it (heavy timber and iron struck at once, dropped for the mass,
    a jaw's weight behind it held off the sub), its timbers crushed (real cracks dropped and piled, a run of splinters),
    its steel and tin crumpling (real tin and plate struck in a flurry, a scrape torn short), its teeth grinding on the
    ballast of it (real stones) and, once, its windows bursting (real glass breaking)."""
    b = Bus(1.8)
    b.at(0, unit(dsp.vari(main_hit(DECK[i % 2], 0.5), rng.uniform(-10, -8))), 0)
    b.at(0.003, iron(rng, i, -7, 0.1, 0.6), -3)
    b.at(0, strike(rng.uniform(-10, -8), 0.25, cut=400), -3)
    for j in range(3):
        b.at(rng.uniform(0.0, 0.1), unit(dsp.vari(main_hit(CRACKS[j % 2], 0.3), rng.uniform(-6, -2))), -3 - 2 * j)
    b.at(0.01, splinter(rng, 0.6, env([(0, 500), (0.6, 15)], 0.6), (-5, 1)), -6)
    for _ in range(6):
        b.at(abs(rng.normal(0.03, 0.05)), unit(dsp.vari(main_hit(TIN[int(rng.integers(5))], 0.2), rng.uniform(-8, -3))),
             rng.uniform(-13, -6))
    sc = dsp.vari(dsp.trim(src.get(SCRAPE), rng.uniform(0, 0.4), 0.25), rng.uniform(-7, -4))
    b.at(0.03, unit(dsp.fade(sc, 0.01, 0.08)), -9)
    b.at(0.05, unit(strew(rng, STONES, 0.4, env([(0, 200), (0.4, 0)], 0.4), (-6, -1), (0.015, 0.04))), -12)
    if glass:
        b.at(rng.uniform(0.03, 0.1), unit(dsp.vari(main_hit(SHATTER[i % 3], 0.6), rng.uniform(-3, 0))), -5)
    return b.x


@recipe("cs-wakers", "eat", "crunch",
        "A car eaten: steel and timber crushed in its jaws, planks splintering, glass bursting, a deep crunch",
        """Real recordings: two or three bites through the car, each the jaws meeting through it (heavy blows on planking
        and heavy iron dropped for the mass, the thunder's crack slowed under it for the weight of a jaw), its timbers
        crushed (real wood breaking dropped and piled, a run of splinters), its sheet steel and roof tin crumpling (real
        tin and plate struck in a flurry, a real scrape torn short), stones grinding in the teeth, and once the windows
        bursting (real glass breaking); between the bites the crushing goes on (splinters and grit). After the last bite
        the wreck's pieces fall from its jaws (real wood, iron and glass pieces landing, thinning). Under 45% below 150 Hz
        (the squish rule): the crunch is in the timber and the steel. 2.4-3.6 s. Held to 80 Hz-8 kHz.""",
        sources=[THUNDER, SCRAPE] + DECK + PLATE_H + METAL_H + CRACKS + TIN + STONES + SHATTER + W.PIECES["wood"]
        + W.PIECES["scrap"] + W.PIECES["glass"], takes=3, band=TRAIN, lufs=EAT_LUFS)
def eat_crunch(rng, k):
    L = EAT_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    bites = BITES[k]
    g = int(rng.integers(len(bites)))
    for i, t in enumerate(bites):
        b.at(t + rng.uniform(0, 0.05), bite_rec(rng, k + i, i == g), -1.5 * i)
    last = bites[-1] + 0.3
    chew = env([(0, 5), (last, 40), (L, 0)], L)
    b.at(0.1, splinter(rng, L, chew, (-8, -3)), -14)
    b.at(0.1, unit(strew(rng, STONES, L, chew, (-7, -2), (0.015, 0.04))), -18)
    b.at(last, W.debris(rng, L - last, 12, {"wood": 3, "scrap": 2, "glass": 0.6}, decay=0.6, semis=(-4, 2)) * 0.5, -8)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 100, 2), L, 0.4), TRAIN), EAT_LUFS, PEAK)


def bite_model(rng, glass):
    """One bite through a car, modelled: the crash of the car's steel, iron and boards crumpling together (the jaws
    meeting through it), fracture tearing its sheet, fibres splintering, teeth like boulders grinding (dull rock modes
    struck) and, once, its windows bursting (glass's bright modes over a hiss of breaking)."""
    b = Bus(1.8)
    b.at(0, hp(W.crash(rng, 1.1, iron=0.8, wood=1.0, tin=0.6, crumple=rng.uniform(0.3, 0.45)), 70, 2), 0)
    b.at(0.02, unit(W.tear(0.4, rng, env([(0, 600), (0.4, 40)], 0.4), (300, 5000), q=14)), -8)
    b.at(0.0, unit(W.splinter(0.5, rng, env([(0, 600), (0.5, 20)], 0.5))), -8)
    for _ in range(4):
        f = rng.uniform(350, 700)
        b.at(rng.uniform(0.02, 0.25), struck(rng, [f, f * 2.1, f * 3.4], 4, 0.08, 0.002), rng.uniform(-14, -8))
    if glass:
        g = Bus(0.8)
        g.at(0, unit(hp(burst(rng, 0.03, attack=0.02), 2500)) * 0.7, 0)
        for _ in range(int(rng.integers(8, 14))):
            g.at(rng.uniform(0, 0.25) ** 1.5, unit(W.body(rng, rng.uniform(2200, 4200), W.PLATE, decay=0.08, contact=0.0002,
                                                          count=8)), rng.uniform(-14, -3))
        b.at(rng.uniform(0.03, 0.1), g.x, -5)
    return b.x


@recipe("cs-wakers", "eat", "jaws",
        "A car eaten, modelled: the car's steel, iron and boards crushed together in its jaws, splintering, glass",
        """Modelled: two or three bites through the car, each its steel sheet, iron frame and boards crushed together (the
        sheet's dense modes rung and crumpling for a third of a second, iron torn, boards splintering, tin clattering),
        fracture tearing the steel, teeth like boulders grinding (dull rock modes struck), and once the windows bursting
        (glass's bright modes over a hiss of breaking), fibres splintering between the bites; after the last the pieces
        fall from its jaws, boards, iron and glass coming to rest. Under 45% below 150 Hz (the squish rule). Same three
        meals as 'crunch'. Held to 80 Hz-8 kHz.""",
        takes=3, band=TRAIN, lufs=EAT_LUFS)
def eat_jaws(rng, k):
    L = EAT_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    bites = BITES[k]
    g = int(rng.integers(len(bites)))
    for i, t in enumerate(bites):
        b.at(t + rng.uniform(0, 0.05), bite_model(rng, i == g), -1.5 * i)
    chew = env([(0, 5), (bites[-1] + 0.3, 40), (L, 0)], L)
    b.at(0.1, unit(W.splinter(L, rng, chew)) * env([(0, 1), (L, 0.2)], L), -16)
    t = bites[-1] + 0.35
    gap = 0.05
    while t < L - 0.3:
        kind = rng.random()
        if kind < 0.5:
            x = struck(rng, [rng.uniform(300, 520) * m for m in (1.0, 2.756, 5.404)], 9, 0.1)
        elif kind < 0.8:
            x = struck(rng, [rng.uniform(500, 900) * m for m in W.BAR[:4]], 20, 0.15, 0.0005)
        else:
            f = rng.uniform(2200, 3800)
            x = struck(rng, [f, f * 1.62, f * 2.31], 16, 0.06, 0.0003)
        b.at(t, x, rng.uniform(-18, -9) - 5 * (t - bites[-1]))
        t += gap * rng.uniform(0.6, 1.6)
        gap *= rng.uniform(1.12, 1.3)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 100, 2), L, 0.4), TRAIN), EAT_LUFS, PEAK)


# ---- cs-wakers: one who ran, taken -------------------------------------------------------------------------------------------

TAKE_L = (2.0, 2.4)


@recipe("cs-wakers", "take", "grip",
        "One who ran, taken in its hand: the ground struck as the hand comes down, a great grip closing, earth spilling",
        """Real recordings, close: the air shoved down ahead of a hand a barn wide, then the ground struck round you (the
        thunder's crack slowed for the weight, a pick into rock dropped, the ground cracking: real wood breaking dropped
        into rock, real stones thrown), then the grip closing on you and the earth under you (real gravel crushed slow and
        dropped into clods, a real heavy leather drop for the palm landing on your coat, real leather handled, dropped and
        held for the hide of its fingers creaking as they close, a knuckle cracking) and the earth spilling out between
        its fingers as it lifts (real gravel and grit pattering down, thinning). Under 45% below 150 Hz (the squish
        rule). 2.0-2.4 s. Held to 60 Hz-6 kHz.""",
        sources=[THUNDER, PALM] + MINING + CRACKS + GRAVEL + LEATHER, takes=2, band=TAKE, lufs=TAKE_LUFS)
def take_grip(rng, k):
    L = TAKE_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    b.at(0, ck.whoosh(rng, 0.28, 90, 700, 0.9, q=0.7), -8)
    t0 = 0.24
    hit = strike(rng.uniform(-13, -11), 0.3, cut=800)
    b.at(t0, dsp.shaped(hit, [(0, 1), (0.08, 0.5), (0.4, 0.05), (0.8, 0.0)], "exp"), -6)
    b.at(t0 + 0.002, unit(lp(dsp.vari(main_hit(MINING[k % 5], 0.4), rng.uniform(-3, -1)), 1500)), -10)
    b.at(t0 + 0.003, rock(rng, rng.uniform(-8, -5), 0.35), -4)
    for t, x, db in flight(rng, 14, stone_rec, loud=-10):
        b.at(t0 + t, x, db)
    G = rng.uniform(0.7, 0.9)
    t1 = t0 + 0.12
    b.at(t1, unit(dsp.vari(hit_of(PALM, 0, 0.3), rng.uniform(-7, -5))), -6)
    crush = strew(rng, GRAVEL, G, env([(0, 30), (G * 0.5, 160), (G, 20)], G), (-10, -6), (0.03, 0.07))
    b.at(t1, unit(lp(crush, 3000)), -9)
    hide = held(rng, LEATHER[k % 2], G, rng.uniform(-11, -9), env([(0, 0), (G, 2)], G), window=0.15, piece=0.25)
    b.at(t1 + 0.05, unit(bp(hide, 120, 3000)) * env([(0, 0), (G * 0.4, 1), (G, 0.2)], G), -10)
    b.at(t1 + G * 0.7, unit(dsp.vari(main_hit(CRACKS[k % 2], 0.2), rng.uniform(-9, -7))), -12)
    D = L - t1 - 0.4
    b.at(t1 + 0.4, unit(lp(strew(rng, GRAVEL, D, env([(0, 0), (0.1, 90), (D, 0)], D), (-6, 0), (0.01, 0.03)), 5000)), -15)
    b.at(t1 + 0.4, grit(rng, D, env([(0, 0), (0.1, 160), (D, 0)], D)), -17)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 80, 2), L, 0.4), TAKE), TAKE_LUFS, PEAK)


@recipe("cs-wakers", "take", "hand",
        "One who ran, taken, modelled: a hand coming down and striking the ground, the grip closing, earth pouring off",
        """Modelled, close: the air shoved down ahead of a hand a barn wide, the ground struck round you (a ground pulse and
        a chunk of soil and rock struck dull, a slab breaking, stones thrown and landing again), then the grip closing on
        you (the hide of its fingers creaking: slow stick-slip through a soft, heavily damped body a hall wide, the soil
        squeezed in its fist grinding, roots tearing out with it) and the earth pouring off between its fingers as it
        lifts, thinning to a trickle. Under 45% below 150 Hz (the squish rule). 2.0-2.4 s. Held to 60 Hz-6 kHz.""",
        takes=2, band=TAKE, lufs=TAKE_LUFS)
def take_hand(rng, k):
    L = TAKE_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    b.at(0, ck.whoosh(rng, 0.28, 90, 700, 0.9, q=0.7), -8)
    t0 = 0.24
    b.at(t0, blow(rng, 0.6, rng.uniform(50, 60), 0.7, sub=0.3), -2)
    b.at(t0, struck(rng, [f * rng.uniform(0.9, 1.1) for f in CLOD], 3, 0.25, hit=0.004), -6)
    b.at(t0 + 0.003, rock_model(rng, 0.3), -5)
    for t, x, db in flight(rng, 14, stone_model, loud=-14):
        b.at(t0 + t, x, db)
    G = rng.uniform(0.7, 0.9)
    t1 = t0 + 0.12
    c = synth.creak(G, env([(0, 15), (G * 0.6, 45), (G, 25)], G), rng, body=[f * rng.uniform(0.9, 1.1) for f in HIDE], q=5,
                    jitter=0.5, grit=0.5)
    b.at(t1 + 0.05, unit(hp(c, 120)) * env([(0, 0), (G * 0.4, 1), (G, 0.2)], G), -12)
    b.at(t1, ck.friction(rng, G, 90, 250, 2500, 0.9, env([(0, 0), (G * 0.4, 1), (G, 0.3)], G)), -11)
    b.at(t1 + 0.1, unit(lp(synth.crackle(G, 120, rng, size=(0.0004, 0.004), hi=400), 3500)), -15)
    D = L - t1 - 0.4
    pour = W.pour(samples(D), rng, env([(0, 0), (0.1, 500), (D, 0)], D), grain=(800, 4000), lump=0.7, thunder=0.2,
                  loop=False)
    b.at(t1 + 0.4, unit(pour), -13)
    y = air(b.x, rng, 0.1, 0.6)
    return finish(hold(trimmed(hp(y, 80, 2), L, 0.4), TAKE), TAKE_LUFS, PEAK)


# ---- cs-wakers: the swallow --------------------------------------------------------------------------------------------------

GULP_L = (2.4, 2.9)


def gullet(x, length, top=1400, bottom=120):
    """Going down: a lowpass closing from `top` to `bottom` Hz over `length` s, as what's swallowed goes down the throat
    away from the mouth."""
    n = len(x)
    fc = dsp.fit(env([(0, top), (length, bottom)], length, "exp"), n)
    return dsp.sweep_filter(np.asarray(x, np.float32), "lp", fc, q=1.2)


@recipe("cs-wakers", "swallow", "gulp",
        "Eaten: a crunch overhead and a long wet swallow, enormous",
        """Real recordings, overhead: the jaws closing (real wood breaking dropped, a heavy timber blow, stones grinding in
        the teeth), then the swallow: real wet squelches and a chewing squish dropped two octaves and more, so a throat's
        wet is a cave's, a mass of real water slowed under it, the gulp itself (the squelches' loudest moment, dropped
        further), then the long wet slide down, all of it swept darker as it goes down the gullet away from the mouth, the
        throat's hollow ringing round it and a real groan dropped two octaves deep in it. 2.4-2.9 s. Held to 50 Hz-4
        kHz.""",
        sources=[WATER, GROAN] + CRACKS + DECK + STONES + WET, takes=2, band=GULP, lufs=GULP_LUFS)
def swallow_gulp(rng, k):
    L = GULP_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    b.at(0, unit(dsp.vari(main_hit(DECK[k % 2], 0.4), rng.uniform(-8, -6))), -4)
    b.at(0.005, unit(dsp.vari(main_hit(CRACKS[k % 2], 0.3), rng.uniform(-6, -4))), -4)
    b.at(0.02, unit(strew(rng, STONES, 0.3, env([(0, 150), (0.3, 0)], 0.3), (-6, -2), (0.015, 0.04))), -12)
    t0 = rng.uniform(0.45, 0.6)
    S1 = t0 - 0.12
    sq = dsp.fit(strew(rng, WET, S1, env([(0, 8), (S1, 70)], S1), (-16, -12), (0.05, 0.14)), samples(S1))
    b.at(0.12, unit(sq) * env([(0, 0.2), (S1, 1)], S1)[:len(sq)], -8)
    gl = unit(dsp.vari(main_hit(WET[k % 3], 0.35), rng.uniform(-18, -16)))
    b.at(t0, gl, -3)
    D = L - t0 - 0.1
    slide = dsp.fit(strew(rng, WET, D, env([(0, 60), (D, 4)], D), (-18, -14), (0.06, 0.16)), samples(D))
    wet = mix(unit(slide), unit(grain(rng, WATER, D, rng.uniform(-11, -9), attack=False)) * 0.8)
    wet = gullet(wet, D, 1400, 140) * env([(0, 0), (0.08, 1), (D * 0.6, 0.6), (D, 0)], D)[:len(wet)]
    b.at(t0 + 0.05, unit(wet), -5)
    thr = held(rng, GROAN, D, rng.uniform(-26, -24), env([(0, 0), (D, -3)], D))
    b.at(t0, lp(thr, 500) * env([(0, 0), (0.2, 1), (D, 0)], D), -16)
    y = dsp.resonate(b.x, CHEST, q=3, gains=[0.5, 0.4, 0.3, 0.2])
    y = mix(b.x, unit(y) * 0.3 * float(np.max(np.abs(b.x))))
    y = ck.space(y, 0.5, 0.15, 3500)
    return finish(hold(trimmed(hp(y, 50, 2), L, 0.4), GULP), GULP_LUFS, PEAK)


@recipe("cs-wakers", "swallow", "throat",
        "Eaten, modelled: a crunch overhead, then a long wet swallow down a throat the size of a tunnel",
        """Modelled, overhead: the jaws closing (dull rock-hard teeth struck through bone and timber, a crackle of things
        breaking), then the swallow: a seal of wet flesh pulled tight (noise through a sweeping wet band), the gulp (a
        bubble of air the size of a barrel forced down: its ring at 40-90 Hz, a slap of wet over it), then the long
        slide down (wet noise and slow low bubbles swept darker as they go down the gullet), the throat's hollow ringing
        round it. 2.4-2.9 s. Held to 50 Hz-4 kHz.""",
        takes=2, band=GULP, lufs=GULP_LUFS)
def swallow_throat(rng, k):
    L = GULP_L[k] * rng.uniform(0.97, 1.03)
    b = Bus(L + 1.0)
    for i in range(3):
        f = rng.uniform(300, 600)
        b.at(rng.uniform(0, 0.06), struck(rng, [f, f * 2.1, f * 3.4], 4, 0.1, 0.002), -4 - 3 * i)
    b.at(0.005, unit(lp(synth.crackle(0.35, env([(0, 1200), (0.35, 20)], 0.35), rng, size=(0.0004, 0.004), hi=400), 4000)),
         -8)
    t0 = rng.uniform(0.45, 0.6)
    S1 = rng.uniform(0.4, 0.55)
    b.at(t0 - S1 * 0.7, ck.slurp(rng, S1, 150, 700, rise=True, bubbles=0.2), -8)
    gl = synth.bubble(rng.uniform(45, 70), length=0.5, rise=0.4, amp=1.0)
    b.at(t0, unit(mix(gl, unit(bp(burst(rng, 0.05, attack=0.1), 200, 1200)) * 0.4)), -3)
    D = L - t0 - 0.1
    n = samples(D)
    wet = mix(unit(lp(synth.noise(D, rng, "pink"), 900)),
              unit(synth.bubbles(D, env([(0, 30), (D, 4)], D), 50, 260, rng, rise=(0.1, 0.4))) * 0.8)
    wet = gullet(wet, D, 1200, 120) * env([(0, 0), (0.08, 1), (D * 0.6, 0.6), (D, 0)], D)[:n]
    b.at(t0 + 0.05, unit(wet), -5)
    y = dsp.resonate(b.x, CHEST, q=3, gains=[0.5, 0.4, 0.3, 0.2])
    y = mix(b.x, unit(y) * 0.3 * float(np.max(np.abs(b.x))))
    y = ck.space(y, 0.5, 0.15, 3500)
    return finish(hold(trimmed(hp(y, 50, 2), L, 0.4), GULP), GULP_LUFS, PEAK)
