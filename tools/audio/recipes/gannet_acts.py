"""The Gannet's acts (docs/design/creatures/gannet.md §4-5): down on the train with the crew, close. The stab into a
walker who held their line, the beak buried in the roof planks on a miss (4 s, thrashing) and torn free, the landing on
its mark, the pin and its four pecks on the sim's 3 s beat (a 0.9 s wind-up each), being beaten off, being hit, and its
death across the roof. Its tells (the calls overhead, the fold, the bank) are in gannet_tells.py.

What it is decides the build:
- A seabird the size of a cart, the weight of two men: everything it does down here is that weight arriving through a
  metre of beak or a pair of plated feet, never a sub-bass boom. Its weight is in the 100-500 Hz of the wood and the body,
  and the low end is kept off it (the director's note on the last pass: nothing squishy, nothing boomy).
- The beak: a metre of serrated bone, so every stab and peck is a hard point first (a crack) and the mass behind it after.
- 7 m of wing: whatever it does on a roof, the wings go too: real heavy cloth whacked and dropped to their size, the
  primaries' rush and the shafts' creak from gannet_tells.py.
- Its voice is the tells' syrinx, close: the same rasp, a hiss through the throat, squawks and screeches that are the
  scream's short, hurt kin.
- What it hits is the real material, as the foley brief asks: a car roof's planks over a boxcar's hollow, a body and its
  coat, a steel helmet or a cloth cap.
"""

import functools

import numpy as np

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import kit
from recipes.beasts import fry, unit, main_hit, claws, rip
from recipes.boarders import cycle
from recipes.horrors import finish
from recipes.gannet_tells import (syrinx, roar_syllable, wingbeat, canvas_beat, quills, clap, bend, TRACT, BEAK, ROAR,
                                   LEATHER, RUSTLE, CREAK as SHAFTS)

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

DECK = src.S("wood_hit_01", "wood_hit_02", "door_03")       # heavy blows on planking and a door's slam: boards taking it
PLANK = src.K("impactPlank_medium")                          # a plank's hollow knock
BREAKS = src.S("misc_34", "misc_35")                         # real wood cracking and breaking
CREAKS = src.R("doorOpen_1", "doorOpen_2", "creak2")         # real wood creaks, a door's and a floor's
CLOTH = src.R("cloth1", "cloth2")                            # heavy cloth whacked: a wing's feathers, a coat
SNOW = src.K("footstep_snow")                                # a real crunch: cartilage giving
WET = src.S("footstep_wet_01", "footstep_wet_03")            # a real wet step: the wet in a wound
POT = "kenney_rpg-audio:metalPot1"                           # a real pot struck: a thin steel dome
TING = src.K("impactMetal_medium")                           # a light steel ring
SOFT = src.K("impactSoft_heavy")                             # a heavy soft blow: a feathered body

ROOF = [132, 188, 262, 370, 505]    # a boxcar's roof, planks over its hollow: broad low modes, kept off the sub (Hz)
HELMET = [610, 1340, 2080, 2870, 3790, 5150]   # a steel helmet's dome: its ring, damped by the head in it
KNOCK_LUFS = -24.0                  # the impacts: all attack, levelled under a look-ahead limiter
PEAK = -1.5


# ---- Shared parts --------------------------------------------------------------------------------------------------------

def done(x, length=None, fade=0.06):
    """Every take's last step: a steep highpass at 100 Hz, so a thud keeps its weight in the wood and the body (120-500
    Hz) and never turns to sub (the 'squishy, boomy' note), and no DC is left behind; cut to `length` s if given, the
    last of it faded, so a ring or a room's tail can't run a one-shot past its moment."""
    y = hp(np.asarray(x, np.float32), 100, 4)
    if length is not None and len(y) > samples(length):
        y = dsp.fade(y[:samples(length)], 0.0, fade)
    return y


def outdoors(x, rng, wet=0.08, tail=0.4):
    """On a car roof in the open: a little of the night's reverb, the tail cut short so a one-shot fired again doesn't
    pile up a wash."""
    x = dsp.trim_silence(x, -60)
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def rec(rng, keys, semis=0.0, length=0.4, pre=0.002):
    """A recording from its loudest hit, varispeeded to size, cut short."""
    return unit(dsp.vari(main_hit(keys[int(rng.integers(len(keys)))], length, pre), semis))


def loop_of(b, length, rng, wet=0.08):
    """A loop's bus folded to exactly one cycle (the room's tail and whatever ran over ring on into the head), handed to
    build.py so its seam gives the cycle back."""
    y = dsp.room(done(b.x), "night", wet=wet, rng=rng)
    return cycle(dsp.wrap(y, samples(length)))


@functools.lru_cache(maxsize=1)
def splinters():
    """Every crack in the real breaking-wood recordings, cut to 50 ms: fibres and splinters giving."""
    out = []
    for k in BREAKS:
        x = ck.get(k)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.015, prom=5):
            out.append(unit(ck.cut(x, s - samples(0.001), s + samples(0.05), 0.0005, 0.025)))
    return out


def splinter(rng, length, rate, semis=(-2, 4)):
    """Planks splintering: real cracks strewn at `rate` a second (a constant or a curve), a few loud and many quiet."""
    g = splinters()
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.06)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)), 20 * np.log10(rng.uniform(0.1, 1) ** 1.5))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return unit(b.x)


def roof_knock(rng, weight=1.0, semis=(-4, -1)):
    """Weight driven down onto the roof: a real heavy blow on planking, choked because the weight stays on it, the
    boxcar's hollow booming under it (broad low modes, no pitch), a plank's own knock."""
    b = Bus(0.8)
    b.at(0, ck.choke(rec(rng, DECK, rng.uniform(*semis), 0.4), 0.05, 0.06), 0)
    b.at(0.002, ck.choke(rec(rng, PLANK, rng.uniform(-2, 1), 0.3), 0.04, 0.05), -6)
    b.at(0.002, ck.hollow(rng, ROOF, 0.4, q=5), -7 + 6 * np.log2(weight))
    return b.x


def board_creak(rng, length, rate=(30, 90)):
    """The planks creaking under a weight: stick-slip through a roof plank's modes."""
    body = [m * rng.uniform(0.95, 1.15) for m in synth.WOOD]
    c = synth.creak(length, env([(0, rate[0]), (length, rate[1])], length), rng, body=body, q=14, jitter=0.4)
    return c * env([(0, 0), (length * 0.3, 1), (length, 0)], length)


def body_blow(rng, weight=1.0, wet=0.5, bone=0.0, f=None):
    """A blow landing in a body: a short thud with the weight in the chest (dying in a tenth of a second, never a boom),
    the flesh slapping, wet if it's opened, a crack if bone takes it."""
    f = f or rng.uniform(140, 170)
    b = Bus(0.4)
    b.at(0, synth.thump(f, 0.12, drop=0.3) * weight, -4)
    b.at(0, unit(bp(synth.noise(0.12, rng, "pink"), 150, 700)) * env([(0, 1), (0.03, 0.4), (0.12, 0)], 0.12) * weight, -2)
    b.at(0, kit.slap(rng, 0.1, 2200, wet), -2)
    if wet:
        b.at(0.004, kit.squelch(rng, 0.2, 300, 1300), -10 + 20 * np.log10(wet))
    if bone:
        b.at(0.002, kit.snap(rng, 0.06, rng.uniform(1500, 2000)), -8 + 20 * np.log10(bone))
    return b.x


def feathers(rng, length=0.25, rate=1200):
    """A puff of feathers ruffled by a blow or a landing: dry vanes rustling and the shafts ticking, dying away."""
    sh = env([(0, 1), (length, 0)], length, "exp")
    r = synth.rustle(length, rate, rng, f=(1500, 8000), shape=sh, ticks=0.6)
    return mix(unit(r), quills(rng, length, rng.uniform(120, 200), [(0, 1), (length, 0)]) * 0.3)


def cloth_whack(rng, semis=(-6, -3), length=0.25):
    """Feathers or a coat hit hard: a real heavy cloth whacked, dropped to size."""
    return unit(hp(rec(rng, CLOTH, rng.uniform(*semis), length), 150))


def mantle(rng, n=3):
    """The wings mantling over what it holds: quick partial flaps, smaller each time, then the feathers settling."""
    b = Bus(1.0)
    t = 0.0
    for i in range(n):
        L = rng.uniform(0.13, 0.18)
        b.at(t, wingbeat(rng, L, heavy=0.6, creak=0.5), -3 - 4 * i)
        b.at(t + L * 0.4, cloth_whack(rng, (-5, -2), 0.12), -10 - 4 * i)
        t += L * rng.uniform(0.85, 1.0)
    b.at(t, feathers(rng, 0.3, 700), -14)
    return b.x


def hiss(rng, length, f0=95):
    """An angry hiss through the throat, a goose's but huge: breath forced up the long neck past a rattling syrinx,
    a low growl voiced under it."""
    sh = [(0, 0), (0.08, 1), (length * 0.7, 0.8), (length, 0)]
    br = synth.breath(length, [(0, "h"), (length * 0.5, "a"), (length, "h")], TRACT, rng,
                      shape=env(sh, length))
    fl = fry(rng, rng.uniform(20, 28), length, jitter=0.4, decay=0.012)
    air = mix(unit(hp(br, 500)) * (0.6 + 0.4 * fl), unit(bp(synth.noise(length, rng), 2500, 6000)) * env(sh, length) * 0.4)
    growl = syrinx(rng, [(0, f0), (length, f0 * 0.9)], length, sh, "a", pulse=rng.uniform(20, 26), rasp=0.95,
                   sub=0.5, drive=6)
    return mix(unit(air), growl * 0.35)


def squawk(rng, L, f0, two=0.4):
    """A short hurt squawk: the call's rasp jumped up and cut off, the bill wide."""
    pts = [(0, f0 * 0.9), (L * 0.2, f0 * 1.15), (L, f0 * 0.75)]
    shape = [(0, 0), (0.008, 1), (L * 0.5, 0.8), (L * 0.85, 0.2), (L, 0)]
    v = syrinx(rng, pts, L, shape, [(0, "a"), (L, "o")], pulse=rng.uniform(32, 40), rasp=0.6, two=two, sub=0.4,
               tract=0.65, drive=10, gape=3)
    b = Bus(L + 0.05)
    b.at(0, v, 0)
    b.at(0, clap(rng, rng.uniform(2200, 2800)), -9)
    return b.x


def screech(rng, L, f0, top):
    """A short screech: the bank's scream torn off, up and back down."""
    pts = [(0, f0), (L * 0.35, top), (L, top * 0.8)]
    shape = [(0, 0), (0.015, 0.8), (L * 0.3, 1), (L * 0.75, 0.7), (L, 0)]
    return syrinx(rng, pts, L, shape, [(0, "a"), (L * 0.4, "e"), (L, "a")], pulse=env([(0, 35), (L, 55)], L),
                  rasp=0.4, two=0.5, ratio=(1.18, 1.42), sub=0.4, tract=0.72, drive=10, gape=3, oq=0.4)


def talons(rng, length=0.12):
    """Plated talons dragged on the planks: hard claw ticks and a short scrape of keratin on wood."""
    b = Bus(length + 0.05)
    b.at(0, claws(rng, int(rng.integers(3, 6)), (1300, 3200), body=[1.0, 1.6, 2.7], spread=0.03), -4)
    b.at(0.01, ck.friction(rng, length, rng.uniform(140, 220), 500, 3500, rough=0.6) * env([(0, 1), (length, 0)], length), -6)
    return b.x


def beak_tip(rng, f=None):
    """The beak's point meeting something: hard bone, a crack with almost no ring."""
    return unit(kit.snap(rng, 0.04, f or rng.uniform(2400, 3400)))


def mass(rng, f=None, length=0.15):
    """The weight behind a blow: a dull low knock, a short burst rung through a few broad low-mid modes (not a sub)."""
    f = f or rng.uniform(150, 200)
    x = rng.standard_normal(samples(0.004)).astype(np.float32) * np.hanning(samples(0.004)).astype(np.float32)
    y = dsp.resonate(np.concatenate([x, np.zeros(samples(length), np.float32)]), [f, f * 2.2, f * 3.9], q=4,
                     gains=[1, 0.6, 0.3])
    return unit(y) * env([(0, 1), (length + 0.004, 0.01)], length + 0.004, "exp")


# ---- The stab: a walker who held their line --------------------------------------------------------------------------------

@recipe("cs-gannet-strike", "stab", "pierce",
        "The beak driven into a body: a hard point through a coat, a heavy wet thud behind it, a crunch",
        """A metre of beak with the bird's whole dive behind it: the air it brings, the bone point punching through a
        coat (a real heavy cloth whacked, the weave tearing), the body taking it (a heavy thud with the weight in the
        chest, not a sub; the flesh slapping and going wet round the wound), a real crunch lowered for the cartilage
        giving, and the mass of the head behind it all. Short and heavy. On a car roof in the open.""",
        sources=CLOTH + SNOW, takes=3, lufs=KNOCK_LUFS)
def stab_pierce(rng, k):
    b = Bus(0.8)
    t0 = 0.06
    b.at(0, ck.whoosh(rng, 0.07, 400, 2600, 0.9), -12)
    b.at(t0, beak_tip(rng), -6)
    b.at(t0, cloth_whack(rng, (-4, -1), 0.2), -3)
    b.at(t0 + 0.002, rip(rng, 0.08, 800, 4000), -12)
    b.at(t0 + 0.003, body_blow(rng, 1.3, 0.7, 0.4), 0)
    cr = rec(rng, SNOW, rng.uniform(-6, -3), 0.12)
    b.at(t0 + 0.01, dsp.shaped(cr, [(0, 1), (0.12, 0)], "exp"), -1)
    b.at(t0, mass(rng), -4)
    return finish(done(outdoors(b.x, rng, 0.06, 0.15), 0.36), KNOCK_LUFS, PEAK)


@recipe("cs-gannet-strike", "stab", "gore",
        "The stab from real recordings: a heavy blow on a body lowered, a wet step for the wound, a crunch",
        """Kitbashed: a real heavy soft blow lowered to a body's weight (its sub taken out, so the weight is in the
        chest), a real coat whacked, a real wet step dropped half an octave for the wound opening, a real crunch for
        the cartilage, the beak's bone point cracking on top and the head's mass behind it. Wetter and duller than
        'pierce'. On a car roof in the open.""",
        sources=SOFT + CLOTH + WET + SNOW, takes=3, lufs=KNOCK_LUFS)
def stab_gore(rng, k):
    b = Bus(0.8)
    t0 = 0.04
    b.at(0, ck.whoosh(rng, 0.05, 400, 2200, 0.9), -14)
    b.at(t0, beak_tip(rng, rng.uniform(1900, 2600)), -9)
    b.at(t0, unit(hp(rec(rng, SOFT, rng.uniform(-3, 0), 0.3), 150, 4)), 0)
    b.at(t0, cloth_whack(rng, (-5, -2), 0.2), -4)
    w = rec(rng, WET, rng.uniform(-7, -5), 0.3)
    b.at(t0 + 0.008, dsp.shaped(w, [(0, 1), (0.25, 0)]), -4)
    b.at(t0 + 0.012, dsp.shaped(rec(rng, SNOW, rng.uniform(-5, -2), 0.12), [(0, 1), (0.12, 0)], "exp"), -7)
    b.at(t0, mass(rng, rng.uniform(130, 170)), -5)
    return finish(done(outdoors(b.x, rng, 0.06, 0.15), 0.36), KNOCK_LUFS, PEAK)


# ---- The miss: the beak buried in the roof planks, stuck 4 s, torn free ------------------------------------------------------

@recipe("cs-gannet-strike", "thunk", "plank",
        "The beak driven deep into the roof planks: a hollow heavy knock through the car, fibres splintering",
        """A metre of bone driven into a car roof at a dive's speed: the point cracking into the wood fibres, real heavy
        blows on planking and a plank's hollow knock (lowered, choked: the beak stays in it), the boxcar's hollow
        booming under the roof (broad low modes, no sub), splinters crackling round the wound in the wood (real cracks
        cut small), and the plank creaking once as it grips the beak. On a car roof in the open.""",
        sources=DECK + PLANK + BREAKS, takes=3, lufs=KNOCK_LUFS)
def thunk_plank(rng, k):
    b = Bus(0.9)
    t0 = 0.04
    b.at(0, ck.whoosh(rng, 0.05, 500, 3000, 0.9), -14)
    b.at(t0, beak_tip(rng, rng.uniform(1800, 2500)), -4)
    b.at(t0 + 0.002, roof_knock(rng, 1.3, (-5, -2)), 0)
    b.at(t0, mass(rng, rng.uniform(170, 220), 0.2), -6)
    b.at(t0 + 0.004, splinter(rng, 0.2, env([(0, 120), (0.2, 10)], 0.2)), -9)
    b.at(t0 + rng.uniform(0.12, 0.18), board_creak(rng, rng.uniform(0.12, 0.18), (60, 140)), -15)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.46), KNOCK_LUFS, PEAK)


def plank_modes(rng, f1):
    """A roof plank 3 m long, struck end-on by a point: its bending modes (a free bar's ratios), ringing short."""
    return [f1 * m * rng.uniform(0.97, 1.03) for m in (1.0, 2.756, 5.404, 8.933, 13.34, 19.9)]


@recipe("cs-gannet-strike", "thunk", "modal",
        "The beak into the planks, modelled: a point splitting the fibres, the plank and the car's hollow ringing dull",
        """Modelled rather than recorded: the beak's point splitting the plank's fibres (a dense crackle of them giving
        as it drives in, a split running a little way along the grain), the plank's own bending modes struck all at
        once and damped by the beak lodged in it, the boxcar's hollow under the roof booming dull, the head's mass, and
        the plank creaking as it grips. Drier and harder than 'plank'. On a car roof in the open.""",
        takes=3, lufs=KNOCK_LUFS)
def thunk_modal(rng, k):
    b = Bus(0.9)
    t0 = 0.04
    b.at(0, ck.whoosh(rng, 0.05, 500, 3000, 0.9), -14)
    b.at(t0, beak_tip(rng, rng.uniform(1800, 2500)), -3)
    x = np.zeros(samples(0.5), np.float32)
    x[:samples(0.003)] = rng.standard_normal(samples(0.003))
    pl = dsp.resonate(x, plank_modes(rng, rng.uniform(140, 175)), q=rng.uniform(14, 20),
                      gains=[1, 0.8, 0.6, 0.45, 0.3, 0.2])
    b.at(t0, ck.choke(unit(pl), 0.03, 0.05), -1)
    b.at(t0, ck.hollow(rng, ROOF, 0.4, q=5), -6)
    b.at(t0, mass(rng, rng.uniform(170, 220), 0.2), -4)
    fib = synth.crackle(0.18, env([(0, 3000), (0.05, 900), (0.18, 40)], 0.18), rng, size=(0.0002, 0.0015), hi=900)
    b.at(t0, unit(lp(fib, 6000)), -9)
    b.at(t0 + rng.uniform(0.04, 0.08), kit.snap(rng, 0.06, rng.uniform(1200, 1700)), -12)    # the split along the grain
    b.at(t0 + rng.uniform(0.12, 0.18), board_creak(rng, rng.uniform(0.12, 0.18), (60, 140)), -14)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.46), KNOCK_LUFS, PEAK)


THRASH_L = 10.0


def wing_slap(rng, wood=1.0):
    """A wing beating the roof: the stroke's air, the feathers slapping the planks (a real heavy cloth whacked), the
    wrist's bone knocking the wood, the shafts rattling."""
    b = Bus(0.6)
    L = rng.uniform(0.22, 0.32)
    b.at(0, wingbeat(rng, L, heavy=0.8, creak=0.6), -4)
    t = L * rng.uniform(0.45, 0.6)
    b.at(t, cloth_whack(rng, (-7, -4), 0.25), 0)
    b.at(t + 0.004, ck.choke(rec(rng, DECK, rng.uniform(-3, 0), 0.3), 0.02, 0.04), -8 + 20 * np.log10(wood + 1e-3))
    b.at(t, feathers(rng, 0.15, 900), -14)
    return b.x


def lever_creak(rng, length):
    """The beak levered in the wound in the wood: a real creak dropped to a plank's size and pulled up in pitch as the
    pressure comes on, a fibre or two cracking."""
    x = dsp.trim_silence(src.get(CREAKS[int(rng.integers(len(CREAKS)))]), -40)
    x = dsp.vari(x, rng.uniform(-11, -7))
    x = dsp.stretch(x, length / (len(x) / SR), smooth=True)
    d = len(x) / SR
    x = bend(x, env([(0, -2), (d * 0.6, 1.5), (d, 0.5)], d))
    x = lp(x, 3000) * env([(0, 0), (length * 0.6, 1), (length, 0)], len(x) / SR)[:len(x)]
    b = Bus(length + 0.1)
    b.at(0, unit(x), 0)
    if rng.random() < 0.6:
        b.at(length * rng.uniform(0.5, 0.9), dsp.vari(splinters()[int(rng.integers(len(splinters())))], rng.uniform(-3, 1)),
             -10)
    return b.x


@recipe("cs-gannet-strike", "thrash", "flail",
        "Stuck in the roof: its wings beating the planks in frantic bursts, the wood creaking on the beak, a hiss",
        """The beak buried in the roof, the bird heaving at it: the wings beat the planks in uneven bursts of two to
        four (each the stroke's air, the feathers slapping the wood as a real heavy cloth whacked and lowered, the
        wrist's bone knocking the planks), the wood creaking round the beak as it levers (real creaks dropped to a
        plank's size, the pitch climbing with the pressure, a fibre cracking), talons scraping for purchase, and an
        angry hiss forced up its throat now and then with a growl under it. A 10 s loop.""",
        sources=CLOTH + DECK + CREAKS + BREAKS, takes=1, loop=True, seconds=THRASH_L)
def thrash_flail(rng, k):
    L = THRASH_L
    b = Bus(L + 2.0)
    t = 0.1
    while t < L:
        for _ in range(int(rng.integers(2, 5))):     # a burst of beats
            b.at(t, wing_slap(rng, rng.uniform(0.5, 1.0)), rng.uniform(-3, 0))
            t += rng.uniform(0.2, 0.3)
        t += rng.uniform(0.3, 0.8)
    for at in np.sort(rng.uniform(0, L, 4)):
        b.at(at, lever_creak(rng, rng.uniform(0.5, 0.9)), -6)
    for at in (rng.uniform(1.0, 2.5), rng.uniform(5.5, 7.0)):
        b.at(at, hiss(rng, rng.uniform(0.7, 1.1)), -5)
    for at in rng.uniform(0, L, 5):
        b.at(at, talons(rng), -12)
    return loop_of(b, L, rng)


def heave(rng, length):
    """One heave at the beak: the planks groaning (stick-slip through a plank's modes, quickening as it hauls), the
    wings driving down hard twice to lift (the air, the feathers on the wood), the head jerking back."""
    b = Bus(length + 0.8)
    rate = env([(0, 15), (length * 0.7, 90), (length, 30)], length)
    body = [m * rng.uniform(0.8, 0.95) for m in synth.WOOD]
    gr = synth.creak(length, rate, rng, body=body, q=12, jitter=0.45, grit=0.5)
    b.at(0, gr * env([(0, 0), (length * 0.7, 1), (length, 0)], length), -3)
    for i in range(2):
        L = rng.uniform(0.3, 0.38)
        b.at(length * (0.25 + 0.35 * i), wingbeat(rng, L, heavy=1.0, creak=0.8), -2)
    b.at(length * 0.95, mass(rng, rng.uniform(180, 240), 0.1), -10)
    return b.x


@recipe("cs-gannet-strike", "thrash", "heave",
        "Stuck in the roof, heaving at it: long groans of the planks on the beak, wings driving down, a hiss between",
        """A different performance from 'flail', modelled: slow heaves instead of frantic bursts. Each heave the planks
        groan round the beak (stick-slip friction through a plank's modes, quickening as it hauls and easing), the wings
        drive down hard twice to lift (the air of a 7 m stroke, the primaries' rush and creak), and the head jerks back
        against it; between heaves an angry hiss forced up the throat with a growl under it, talons scraping the planks,
        a wing slapping the roof. A 10 s loop.""",
        sources=CLOTH + DECK, takes=1, loop=True, seconds=THRASH_L)
def thrash_heave(rng, k):
    L = THRASH_L
    b = Bus(L + 2.0)
    t = 0.1
    i = 0
    while t < L:
        H = rng.uniform(1.1, 1.6)
        b.at(t, heave(rng, H), 0)
        t += H + rng.uniform(0.15, 0.3)
        if i % 2 == 0:
            Lh = rng.uniform(0.6, 0.9)
            b.at(t, hiss(rng, Lh), -4)
            t += Lh * 0.7
        else:
            b.at(t, wing_slap(rng), -2)
            t += 0.3
        b.at(t - 0.1, talons(rng), -12)
        i += 1
    return loop_of(b, L, rng)


@recipe("cs-gannet-strike", "tear", "splinter",
        "Tearing free of the roof: the plank groaning, then splintering as the beak rips out, a flurry of wings",
        """The beak wrenched out of the planks: the wood groans as the pressure comes on (a real creak dropped to a
        plank's size), gives with a real crack and a spray of splinters (real breaking-wood recordings, cut to their
        cracks), the beak's serrations ripping the fibres on the way out, and the wings flurry as it lifts off (three
        quick heavy strokes, the air and the feathers). On a car roof in the open.""",
        sources=CREAKS + BREAKS, takes=3)
def tear_splinter(rng, k):
    b = Bus(1.4)
    pull = rng.uniform(0.15, 0.25)
    b.at(0, lever_creak(rng, pull + 0.05), -4)
    t = pull
    big = dsp.vari(main_hit(BREAKS[1], 0.15), rng.uniform(-4, -1))
    b.at(t, unit(big), 0)
    b.at(t, splinter(rng, 0.25, env([(0, 200), (0.25, 15)], 0.25)), -4)
    b.at(t + 0.01, rip(rng, 0.12, 900, 4500), -9)
    b.at(t, mass(rng, rng.uniform(190, 240), 0.1), -10)
    for i in range(3):
        Lw = rng.uniform(0.17, 0.22)
        b.at(t + 0.04 + i * Lw * 0.9, wingbeat(rng, Lw, heavy=1.0, creak=0.7), -2 - 2 * i)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.85, 0.12), -20, PEAK)


@recipe("cs-gannet-strike", "tear", "wrench",
        "Tearing free, modelled: the beak wrenched side to side, the planks squealing, then a split and a wing flurry",
        """Modelled: the beak twisted in the wood twice (the planks squealing on it, stick-slip through a plank's modes,
        the pitch climbing), then the plank splits along the grain (a hard snap and a run of fibres giving), the beak
        comes out, and the wings flurry as it lifts off: the stroke's air and real heavy cloth for the feathers. On a car
        roof in the open.""",
        sources=CLOTH, takes=3)
def tear_wrench(rng, k):
    b = Bus(1.4)
    t = 0.0
    body = [m * rng.uniform(0.7, 0.85) for m in synth.WOOD]
    for i in range(2):
        L = rng.uniform(0.12, 0.17)
        sq = synth.creak(L, env([(0, 80), (L, 260)], L), rng, body=body, q=16, jitter=0.3, grit=0.4)
        b.at(t, sq * env([(0, 0), (L * 0.5, 1), (L, 0.3)], L), -2 - 3 * i)
        t += L + rng.uniform(0.02, 0.05)
    b.at(t, kit.snap(rng, 0.08, rng.uniform(1100, 1500)), 0)
    fib = synth.crackle(0.22, env([(0, 2500), (0.22, 50)], 0.22), rng, size=(0.0002, 0.0015), hi=900)
    b.at(t, unit(lp(fib, 6000)), -6)
    b.at(t, mass(rng, rng.uniform(190, 240), 0.1), -9)
    for i in range(3):
        Lw = rng.uniform(0.17, 0.22)
        at = t + 0.03 + i * Lw * 0.9
        b.at(at, wingbeat(rng, Lw, heavy=1.0, creak=0.7), -2 - 2 * i)
        b.at(at + Lw * 0.4, cloth_whack(rng, (-6, -3), 0.15), -10 - 2 * i)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.85, 0.12), -20, PEAK)


# ---- The pin: landing on its mark, four pecks on a 3 s beat ------------------------------------------------------------------

@recipe("cs-gannet-strike", "land", "pin",
        "Its whole weight landing on its mark: talons, a heavy thud through the roof boards, wings mantling over",
        """The wings back-beating hard to brake (one great stroke's air), then the weight arrives: plated talons
        striking and dragging on the planks, a body driven down onto the roof (the thud of it, a coat crushed: a real
        heavy cloth whacked), the roof taking both (real heavy blows on planking choked, the boxcar's hollow booming,
        never a sub), the boards creaking under them; then the wings mantle over the catch, quick partial flaps
        smaller each time, the feathers settling. On a car roof in the open.""",
        sources=DECK + PLANK + CLOTH, takes=3, lufs=KNOCK_LUFS)
def land_pin(rng, k):
    b = Bus(1.4)
    brake = rng.uniform(0.15, 0.2)
    b.at(0, wingbeat(rng, brake + 0.05, heavy=1.0, creak=0.8), -10)
    t0 = brake
    b.at(t0, talons(rng, 0.1), -6)
    b.at(t0 + 0.01, roof_knock(rng, 1.5, (-6, -3)), 0)
    b.at(t0 + 0.01, body_blow(rng, 1.0, 0.15), -4)
    b.at(t0 + 0.012, cloth_whack(rng, (-5, -3), 0.2), -5)
    b.at(t0 + 0.05, board_creak(rng, rng.uniform(0.2, 0.3), (25, 70)), -14)
    b.at(t0 + rng.uniform(0.04, 0.07), mantle(rng, int(rng.integers(2, 4))), -4)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.78, 0.12), KNOCK_LUFS, PEAK)


@recipe("cs-gannet-strike", "land", "drop",
        "Its weight dropped onto its mark, modelled: wings braking, a body slammed onto a plank roof, feathers settling",
        """Modelled from what lands: a hard braking stroke (the air of a 7 m wing), then the drop, both feet at once: the
        plank roof's bending modes and the boxcar's hollow struck by a soft, enormous push (the weight of two men, so the
        low modes ring, kept off the sub), a body under it (a dull thud and the breath of a coat), talons ticking on the
        planks; then the wings mantle and the feathers settle. Softer at the edges than 'pin', heavier in the middle.
        On a car roof in the open.""",
        takes=3, lufs=KNOCK_LUFS)
def land_drop(rng, k):
    b = Bus(1.4)
    brake = rng.uniform(0.16, 0.21)
    b.at(0, wingbeat(rng, brake + 0.06, heavy=1.0, creak=1.0), -9)
    t0 = brake
    n = samples(0.5)
    push = np.zeros(n, np.float32)
    w = samples(rng.uniform(0.012, 0.018))
    push[:w] = np.sin(np.pi * np.arange(w) / w)
    pl = dsp.resonate(push, plank_modes(rng, rng.uniform(120, 150)) + ROOF, q=rng.uniform(8, 12))
    b.at(t0, unit(hp(pl, 80)) * env([(0, 1), (0.5, 0.01)], 0.5, "exp"), 0)
    b.at(t0, mass(rng, rng.uniform(130, 160), 0.25), -4)
    b.at(t0, claws(rng, int(rng.integers(3, 6)), (1300, 3000), body=[1.0, 1.6, 2.7], spread=0.02), -8)
    b.at(t0 + 0.005, bp(synth.noise(0.12, rng, "pink"), 300, 2500) * env([(0, 1), (0.12, 0)], 0.12, "exp"), -10)
    b.at(t0 + rng.uniform(0.05, 0.08), mantle(rng, int(rng.integers(2, 4))), -5)
    return finish(done(outdoors(b.x, rng, 0.07, 0.2), 0.78, 0.12), KNOCK_LUFS, PEAK)


WINDUP = 0.9


@recipe("cs-gannet-strike", "windup", "trill",
        "A peck's wind-up: a guttural trill in the throat, tightening and rising to the blow",
        """The head drawn back for the blow (the neck's feathers creaking) and a rattle building in the throat: the
        syrinx's rasp slowed to a trill, its pulses quickening from about 12 to 45 a second as it tightens, the pitch
        and the force rising, the bill opening from 'oo' to 'ah', the throat sacs throbbing with the pulses. It ends at
        its peak, as the peck lands. Exactly 0.9 s.""",
        takes=3)
def windup_trill(rng, k):
    L = WINDUP
    n = samples(L)
    f0 = rng.uniform(100, 125)
    pulse = env([(0, rng.uniform(11, 14)), (L * 0.6, 26), (L, rng.uniform(42, 48))], L, "exp")
    v = syrinx(rng, [(0, f0), (L * 0.7, f0 * 1.3), (L, f0 * 1.55)], L, [(0, 0.3), (L * 0.6, 0.6), (L, 1)],
               [(0, "u"), (L * 0.6, "o"), (L, "a")], pulse=pulse, rasp=0.95, two=0.3, sub=0.4, drive=8, gate=0.92)
    fr = fry(rng, pulse, L, jitter=0.25, decay=0.012)
    sacs = dsp.resonate(bp(synth.noise(L, rng, "pink"), 150, 900) * fr, [rng.uniform(230, 270), rng.uniform(480, 560)], q=5)
    b = Bus(L)
    b.at(0, v, 0)
    b.at(0, unit(sacs) * env([(0, 0.2), (L, 1)], L), -9)
    b.at(0, quills(rng, 0.2, rng.uniform(60, 90), [(0, 0), (0.05, 1), (0.2, 0)]), -14)
    return dsp.fit(done(b.x), n)


@functools.lru_cache(maxsize=1)
def slips():
    """Single slips of real door creaks, cut to 20 ms: old wood catching and letting go, once."""
    out = []
    for k in CREAKS[:2]:
        x = ck.get(k)
        for s0, _ in ck.hits(x, floor_db=-35, gap=0.006, prom=3):
            out.append(unit(ck.cut(x, s0 - samples(0.0005), s0 + samples(0.02), 0.0003, 0.008)))
    return out


@recipe("cs-gannet-strike", "windup", "rattle",
        "The wind-up from real creaks: a door's slips fired as a rattle that quickens, rung through its throat",
        """Kitbashed: single slips cut from real door creaks (old wood catching and letting go) fired one after another
        as a rattle that quickens from about 12 to 45 a second and climbs in pitch, rung through the Gannet's throat and
        bill (the same tract and bill as its call, opening from 'oo' to 'ah'), voiced a little under it, the throat's
        sacs throbbing dark with each pulse. Rising in force to its peak, where the peck lands. Exactly 0.9 s.""",
        sources=CREAKS[:2], takes=3)
def windup_rattle(rng, k):
    L = WINDUP
    n = samples(L)
    g = slips()
    rate = env([(0, rng.uniform(11, 14)), (L * 0.6, 26), (L, rng.uniform(42, 48))], L, "exp")
    x = Bus(L + 0.05)
    t = 0.0
    while t < L:
        u = t / L
        x.at(t, dsp.vari(g[int(rng.integers(len(g)))], -4 + 8 * u + rng.uniform(-1, 1)), rng.uniform(-3, 0))
        t += float(1 / rate[min(samples(t), n - 1)]) * rng.uniform(0.85, 1.15)
    v = synth.tract(dsp.fit(x.x, n), [(0, "u"), (L * 0.6, "o"), (L, "a")], TRACT,
                    extra=[(1900 / TRACT, 5, 0.5), (2700 / TRACT, 6, 0.45)])
    f0 = rng.uniform(100, 125)
    voiced = syrinx(rng, [(0, f0), (L, f0 * 1.5)], L, [(0, 0.3), (L, 1)], "o", pulse=rate, rasp=0.9, drive=6)
    fr = fry(rng, rate, L, jitter=0.25, decay=0.012)
    sacs = dsp.resonate(bp(synth.noise(L, rng, "pink"), 150, 900) * fr, [rng.uniform(230, 270), rng.uniform(480, 560)], q=5)
    shape = env([(0, 0.3), (L * 0.6, 0.6), (L, 1)], L)
    y = mix(unit(dsp.saturate(unit(v), 8)) * shape, voiced * 0.4, unit(sacs) * shape * 0.3)
    return dsp.fit(done(y), n)


def helmet_ring(rng):
    """A steel helmet struck on a head: the dome's ring (a real pot struck, its thin steel), a light steel ring over
    it, both choked by the head inside."""
    b = Bus(0.4)
    pot = dsp.vari(main_hit(POT, 0.3), rng.uniform(-1, 2))
    b.at(0, ck.choke(unit(hp(pot, 300)), 0.03, rng.uniform(0.05, 0.08)), 0)
    b.at(0, ck.choke(rec(rng, TING, rng.uniform(-5, -2), 0.25), 0.02, 0.06), -8)
    return b.x


def skull(rng):
    """The head under the blow: a short dull knock of bone through flesh."""
    return mass(rng, rng.uniform(230, 300), 0.08)


@recipe("cs-gannet-strike", "peck", "beak",
        "The peck landing: two ring on a steel helmet, two land wet and dull on a cloth cap",
        """A bone point the length of a forearm swung into a pinned head. On a helmet (two takes): the point cracks on
        the steel and skids, the dome rings (a real pot's thin steel and a light steel ring, both choked short by the
        head inside it) and the skull knocks under it. On a cloth cap (two takes): a real coat's cloth whacked, a wet
        heavy thud (flesh going wet, a real wet step lowered), a crack of bone and the skull's dull knock; no ring. Short:
        the next peck is 3 s off. On a car roof in the open.""",
        sources=[POT] + TING + CLOTH + WET, takes=4, lufs=KNOCK_LUFS,
        preview=lambda takes, rng: kit.scatter(takes, rng, (0.8, 1.4)))
def peck_beak(rng, k):
    b = Bus(0.6)
    t0 = 0.03
    b.at(0, ck.whoosh(rng, 0.04, 500, 2800, 0.9), -14)
    if k < 2:       # a steel helmet
        b.at(t0, beak_tip(rng, rng.uniform(2800, 3600)), -2)
        b.at(t0, helmet_ring(rng), 0)
        b.at(t0 + 0.004, ck.friction(rng, 0.05, 260, 1500, 5000, rough=0.5) * env([(0, 1), (0.05, 0)], 0.05), -12)
        b.at(t0 + 0.002, skull(rng), -8)
    else:           # a cloth cap
        b.at(t0, beak_tip(rng, rng.uniform(1600, 2100)), -12)
        b.at(t0, cloth_whack(rng, (-3, 0), 0.15), -3)
        b.at(t0 + 0.002, body_blow(rng, 0.8, 0.6, 0.5, rng.uniform(170, 200)), 0)
        b.at(t0 + 0.006, dsp.shaped(rec(rng, WET, rng.uniform(-6, -4), 0.2), [(0, 1), (0.15, 0)]), -6)
        b.at(t0 + 0.002, skull(rng), -4)
    return finish(done(outdoors(b.x, rng, 0.05, 0.1), 0.32), KNOCK_LUFS, PEAK)


@recipe("cs-gannet-strike", "peck", "strike",
        "The peck, modelled: a hard point ringing a steel dome (two takes) or thudding wet into a cap (two takes)",
        """Modelled rather than recorded. On a helmet: a sharp bone point strikes a thin steel dome, whose modes
        (600 Hz-5 kHz) ring and are choked within a tenth of a second by the head inside it, the point grating as it
        skids, the skull knocking under. On a cloth cap: a dull padded thud, the cap's wool muffling a wet slap and a
        split of skin, the skull's knock; no ring. Short. On a car roof in the open.""",
        takes=4, lufs=KNOCK_LUFS, preview=lambda takes, rng: kit.scatter(takes, rng, (0.8, 1.4)))
def peck_strike(rng, k):
    b = Bus(0.6)
    t0 = 0.03
    b.at(0, ck.whoosh(rng, 0.04, 500, 2800, 0.9), -14)
    if k < 2:
        x = np.zeros(samples(0.3), np.float32)
        x[:samples(0.0006)] = rng.standard_normal(samples(0.0006))
        dome = dsp.resonate(x, [f * rng.uniform(0.95, 1.05) for f in HELMET], q=rng.uniform(70, 110),
                            gains=[1, 0.9, 0.7, 0.55, 0.4, 0.3])
        b.at(t0, ck.choke(unit(dome), 0.015, rng.uniform(0.04, 0.07)), 0)
        b.at(t0, beak_tip(rng, rng.uniform(2800, 3600)), -4)
        b.at(t0 + 0.003, ck.friction(rng, 0.05, 300, 1500, 5000, rough=0.4) * env([(0, 1), (0.05, 0)], 0.05), -12)
        b.at(t0 + 0.002, skull(rng), -7)
    else:
        b.at(t0, kit.slap(rng, 0.08, 1800, 0.5), -6)
        b.at(t0, unit(lp(bp(synth.noise(0.06, rng, "pink"), 200, 3000), 2500)) * env([(0, 1), (0.06, 0)], 0.06, "exp"), -6)
        b.at(t0 + 0.002, body_blow(rng, 0.8, 0.7, 0.3, rng.uniform(170, 200)), 0)
        b.at(t0 + 0.004, kit.squelch(rng, 0.12, 400, 1500), -12)
        b.at(t0 + 0.002, skull(rng), -3)
    return finish(done(outdoors(b.x, rng, 0.05, 0.1), 0.32), KNOCK_LUFS, PEAK)


# ---- Beaten off, hit, killed ---------------------------------------------------------------------------------------------------

@recipe("cs-gannet-strike", "driven", "lurch",
        "Beaten off its pin: a screech as it lurches up, talons tearing off the planks, a burst of heavy wingbeats",
        """Three blows and it lets go: the talons rip off the planks (claws ticking, a scrape of keratin on wood), it
        screeches (the bank's scream torn off short, two-voiced and raw, up and back down) and lurches up on three or
        four quick heavy wingbeats (each a deep shove of air, the primaries' rush and the shafts' creak), the coat
        under it let go. On a car roof in the open.""",
        takes=3)
def driven_lurch(rng, k):
    b = Bus(1.6)
    b.at(0, talons(rng, 0.14), -4)
    b.at(0.02, cloth_whack(rng, (-4, -1), 0.15), -12)
    Ls = rng.uniform(0.42, 0.55)
    b.at(0.04, screech(rng, Ls, rng.uniform(420, 480), rng.uniform(640, 720)), 0)
    t = 0.08
    for i in range(int(rng.integers(3, 5))):
        Lw = rng.uniform(0.2, 0.25)
        b.at(t, wingbeat(rng, Lw, heavy=1.0, creak=0.7), -2 - 1.5 * i)
        t += Lw * rng.uniform(0.85, 1.0)
    return done(outdoors(b.x, rng, 0.08, 0.2), 1.15, 0.12)


def torn_cry(rng, L, semis):
    """A real roar's hardest sliver held out to `L` s (paulstretched), pitched up `semis`, pulled up and back down,
    broken into a rough flutter and rung through the bill: a screech from a recording."""
    x = dsp.trim(dsp.trim_silence(src.get(ROAR), -30), rng.uniform(0.04, 0.1), 0.22)
    y = dsp.shift(dsp.smear(x, (L + 0.1) / 0.22, 0.12, rng), semis)
    y = dsp.fit(bend(y, env([(0, -2), (L * 0.3, 3), (L, -3)], L + 0.1)), samples(L))
    y = y / (lp(np.abs(y), 6) + 0.05 * np.max(np.abs(y)))
    y = y * (0.5 + 0.5 * fry(rng, env([(0, 34), (L, 50)], L), L, decay=0.006))
    y = mix(unit(y), unit(dsp.resonate(y, [f for f, _, _ in BEAK], q=6)) * 0.6)
    return dsp.saturate(unit(dsp.peak(y, 2400, 1.0, 6)) * env([(0, 0), (0.015, 1), (L * 0.6, 0.8), (L, 0)], L), 8)


@recipe("cs-gannet-strike", "driven", "flurry",
        "Beaten off, kitbashed: a real roar torn up into a screech, real heavy cloth for the wings' burst, talons",
        """The same moment from recordings: a real roar's hardest sliver held out (paulstretched), pitched up most of an
        octave and pulled up and back down into a rough screech with the call's flutter in it, over a burst of
        wingbeats made of real heavy cloth whacked and lowered (the feathers), a leather drop's whoosh (the air) and a
        real creak cut short (the shafts); the talons ripping off the planks first. On a car roof in the open.""",
        sources=[ROAR] + CLOTH + [LEATHER] + SHAFTS + RUSTLE, takes=3)
def driven_flurry(rng, k):
    b = Bus(1.6)
    b.at(0, talons(rng, 0.14), -4)
    b.at(0.04, torn_cry(rng, rng.uniform(0.42, 0.52), rng.uniform(10, 12)), 0)
    t = 0.08
    for i in range(int(rng.integers(3, 5))):
        Lw = rng.uniform(0.2, 0.25)
        b.at(t, canvas_beat(rng, Lw, heavy=1.0), -2 - 1.5 * i)
        t += Lw * rng.uniform(0.85, 1.0)
    return done(outdoors(b.x, rng, 0.08, 0.2), 1.15, 0.12)


def feathered_blow(rng):
    """A blow landing on a body under thick feathers: a heavy, padded thud (a real soft blow, its sub taken out), the
    feathers puffing and the shafts rattling, the bone under them knocking once."""
    b = Bus(0.5)
    b.at(0, unit(hp(rec(rng, SOFT, rng.uniform(-2, 1), 0.3), 150, 4)), 0)
    b.at(0, cloth_whack(rng, (-6, -3), 0.15), -5)
    b.at(0.004, feathers(rng, 0.22, 1500), -7)
    b.at(0.002, mass(rng, rng.uniform(160, 210), 0.12), -6)
    return b.x


@recipe("cs-gannet-strike", "hit", "squawk",
        "A blow or a ball landing on it: a heavy feathered thud, feathers puffing, a short pained squawk",
        """A shovel or a cannon ball into a body under thick plumage: a padded heavy thud (a real soft blow with its sub
        taken out, so the weight is in the body), a real heavy cloth whacked for the feathers, a puff of them ruffling and
        the shafts rattling, the bone under knocking once; and its voice jumped up into a short hurt squawk (the call's
        rasp, higher and cut off, two-voiced). On a car roof in the open.""",
        sources=SOFT + CLOTH, takes=3, lufs=-21)
def hit_squawk(rng, k):
    b = Bus(0.8)
    b.at(0, feathered_blow(rng), 0)
    b.at(rng.uniform(0.02, 0.04), squawk(rng, rng.uniform(0.18, 0.26), rng.uniform(320, 400)), -3)
    return finish(done(outdoors(b.x, rng, 0.06, 0.12), 0.45), -21, PEAK)


@recipe("cs-gannet-strike", "hit", "croak",
        "Hit, kitbashed: the same feathered thud, a real roar pitched up and torn into a short hurt croak",
        """The same padded thud and puff of feathers as 'squawk', but the voice from a recording: a real roar's
        hardest sliver pitched up half an octave into a short, hurt 'kak', broken into the call's rasp and rung through
        the bill's hollow. Lower and more guttural than 'squawk'. On a car roof in the open.""",
        sources=SOFT + CLOTH + [ROAR], takes=3, lufs=-21)
def hit_croak(rng, k):
    b = Bus(0.8)
    b.at(0, feathered_blow(rng), 0)
    b.at(rng.uniform(0.02, 0.04), roar_syllable(rng, rng.uniform(0.18, 0.24), rng.uniform(4, 7), rng.uniform(30, 36)), -2)
    return finish(done(outdoors(b.x, rng, 0.06, 0.12), 0.45), -21, PEAK)


def last_croak(rng, L, f0):
    """Its last call: one syllable of the rasp, dragging lower, the pulses slowing apart as the breath goes."""
    pts = [(0, f0), (L * 0.2, f0 * 1.05), (L, f0 * 0.6)]
    shape = [(0, 0), (0.03, 1), (L * 0.4, 0.7), (L * 0.8, 0.3), (L, 0)]
    return syrinx(rng, pts, L, shape, [(0, "a"), (L * 0.6, "o"), (L, "u")], pulse=env([(0, 26), (L, 9)], L, "exp"),
                  rasp=0.95, two=0.2, sub=0.5, drive=7)


def drag(rng, length):
    """A body sliding across the planks: feathers and plates scraping the wood, the boards juddering under it."""
    fr = ck.friction(rng, length, rng.uniform(90, 140), 300, 3000, rough=0.7)
    ru = synth.rustle(length, 700, rng, f=(1200, 6000), ticks=0.4)
    sh = env([(0, 1), (length * 0.6, 0.6), (length, 0)], length)
    return mix(unit(fr) * sh, unit(ru) * sh * 0.5)


@recipe("cs-gannet-strike", "death", "crash",
        "Killed: crashing across the car roof, the body tumbling over the boards, a wing flailing, a last croak",
        """Its weight comes down on the roof all at once (real heavy blows on planking, the boxcar's hollow booming, a
        body's thud, a coat of feathers whacked), tumbles over the boards twice more, each bump smaller (the planks, the
        feathers, the bone), slides with its feathers and plates scraping the wood, one wing flailing three slowing
        beats against the roof, and lets out one last call: a single syllable of its rasp, dragging lower, the pulses
        slowing apart as the breath goes. On a car roof in the open.""",
        sources=DECK + PLANK + CLOTH, takes=2)
def death_crash(rng, k):
    b = Bus(3.0)
    b.at(0, roof_knock(rng, 1.6, (-6, -3)), 0)
    b.at(0.004, feathered_blow(rng), -2)
    t = 0.0
    for i in range(2):
        t += rng.uniform(0.25, 0.35)
        b.at(t, roof_knock(rng, 1.0, (-4, -1)), -5 - 4 * i)
        b.at(t + 0.004, feathered_blow(rng), -8 - 4 * i)
    sl = rng.uniform(0.35, 0.5)
    b.at(t + 0.06, drag(rng, sl), -10)
    t2 = rng.uniform(0.35, 0.5)
    for i in range(3):
        Lw = rng.uniform(0.25, 0.32) * (1 + 0.25 * i)
        b.at(t2, wing_slap(rng, 0.8 - 0.2 * i), -6 - 3 * i)
        t2 += Lw + 0.08 * i
    Lc = rng.uniform(0.55, 0.7)
    b.at(max(t + sl, t2) - 0.15, last_croak(rng, Lc, rng.uniform(150, 180)), -3)
    return finish(done(outdoors(b.x, rng, 0.08, 0.3), 2.4, 0.25), -20, PEAK)


@recipe("cs-gannet-strike", "death", "slump",
        "Killed, modelled: a dead weight slammed across the planks, a long slide, a wing beating twice, a rattling last breath",
        """A different death: it drops dead weight onto the roof (the plank roof's modes and the boxcar's hollow struck
        by an enormous soft push, a body's thud), slides a long way across the boards with its plates and feathers
        scraping and the boards juddering, one wing beats the roof twice, weakly, and its last breath rattles out of
        it: a hiss with the rasp slowing in it to nothing. On a car roof in the open.""",
        takes=2)
def death_slump(rng, k):
    b = Bus(3.0)
    n = samples(0.6)
    push = np.zeros(n, np.float32)
    w = samples(rng.uniform(0.015, 0.022))
    push[:w] = np.sin(np.pi * np.arange(w) / w)
    pl = dsp.resonate(push, plank_modes(rng, rng.uniform(110, 140)) + ROOF, q=rng.uniform(8, 12))
    b.at(0, unit(hp(pl, 80)) * env([(0, 1), (0.6, 0.01)], 0.6, "exp"), 0)
    b.at(0, mass(rng, rng.uniform(130, 160), 0.3), -3)
    b.at(0.004, feathered_blow(rng), -4)
    sl = rng.uniform(0.8, 1.0)
    b.at(0.08, drag(rng, sl), -6)
    for i in range(2):
        b.at(0.3 + i * rng.uniform(0.35, 0.45), wing_slap(rng, 0.7 - 0.3 * i), -6 - 4 * i)
    Lb = rng.uniform(0.7, 0.9)
    at = 0.08 + sl - 0.1
    br = hiss(rng, Lb, rng.uniform(80, 95))
    b.at(at, br * env([(0, 1), (Lb, 0.4)], Lb) * (0.4 + 0.6 * fry(rng, env([(0, 20), (Lb, 6)], Lb, "exp"), Lb,
                                                                   jitter=0.4, decay=0.02)), -4)
    return finish(done(outdoors(b.x, rng, 0.08, 0.3), 2.4, 0.25), -20, PEAK)
