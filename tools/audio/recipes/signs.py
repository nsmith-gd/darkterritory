"""Signs off the train (queue #79, note 342): what a crewmate hears when a creature shows itself at a stop.

Walk away from the train into a village or a yard and every 12-24 s a pair of eyes shows at the hand lamp's edge
(14-22 m) for 3.5 s, toward where a creature lives at this stop. Its sound plays once as they appear, from where they
are. The design rule: "a sign means only that it's there; a tell means it's coming". So a sign is the creature moving
off, never its voice and never its tell: quieter and more ambiguous, a body shifting and going, but built from the same
body parts as its creature sounds so it's clearly the same animal:
- Ribbits (recipes/beasts.py): a dog-sized toad, wet hairless skin, a fat loose body. Its wet hops are kept, its croak
  is the tell, so: the skin and the weight in wet grass, one soft hop away, no throat at all.
- Soot Children (recipes/horrors.py): a child's small bare feet and blackened hands, soot sifting off it. Its call is
  the lure, so no voice here, not even a breath through a mouth: feet on cinders and soot in the air.
- Whistler (recipes/boarders.py): twenty hooked legs landing in rolling waves, ivory plates chattering, a four-metre
  coil. Its siphon is the whistle, so it stays shut: the legs and the body through brush.
- Grumbler (recipes/beasts.py): a man on bare palms and soles, heavy, muttering to himself. Its gnaw is the tell (dry
  teeth at 1.4-2.2 kHz in runs), so nothing rasps and the scuttle is kept low and heavy, behind a wall.
Every take is heard 15-22 m off in open night: the air taking a little off the top, a touch of the night's room with
its tail cut short (one-shot, every 12-24 s), and the movement away pulling it darker and quieter as it goes.
"""

import numpy as np

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import kit
from recipes.beasts import clip, unit, squish, palm, mutter, BALLAST
from recipes.boarders import grain, norm, plate_click, ballast, chatter, thud, STONES
from recipes.horrors import soot

SR = dsp.SR
LUFS = -24.0     # quiet: a little under full volume, under the tells


# ---- Out there -------------------------------------------------------------------------------------------------------

def cut_tail(y, n, tail):
    """Keep `tail` s of reverb after the dry sound's `n` samples, faded, so retriggered signs never pile up a wash."""
    y = y[:n + samples(tail)]
    return dsp.fade(y, 0.0, min(tail, len(y) / SR / 2))


def night(x, rng, wet, tail):
    """A touch of open night round a sound: the far edges' slow reverb, mostly dry."""
    return cut_tail(dsp.room(x, "night", wet=wet, rng=rng), len(x), tail)


def air(metres):
    """Where the air and the grass and the night's damp have taken the top off by `metres` (a little more than
    dsp.distance's open air: it's near the ground, among things)."""
    return 18000 / (1 + metres / 15)


def at(x, metres):
    """A sound standing `metres` off: darker with distance, the level falling as 1/distance (from 15 m)."""
    return dsp.gain(lp(x, air(metres), 2), -20 * np.log10(metres / 15))


def away(x, near, far, curve=1.0):
    """A sound going from `near` to `far` metres over its length: darker and quieter as it goes, a little faster than
    1/distance so the going reads at a glance (`curve` above 1 starts slow and picks up)."""
    n = len(x)
    d = (near + (far - near) * (np.arange(n) / max(n - 1, 1)) ** curve).astype(np.float32)
    y = dsp.sweep_filter(x, "lp", air(d), q=0.6)
    return (y * (15 / d) ** 1.5).astype(np.float32)


def even(x):
    """A fast compressor over a run of tiny hard grains (cinders, stems), so the sharpest few don't set the take's
    level and leave the rest too quiet once it's levelled."""
    return dsp.compress(x, -20, 3.0, attack=0.0005, release=0.05)


def finish(x, rng, wet=0.18, tail=0.45):
    """Every take's last step: into the night from where its last sound ends, and no DC left by a thud."""
    return hp(night(dsp.trim_silence(x, -60), rng, wet, tail), 25)


# ---- Ribbits ---------------------------------------------------------------------------------------------------------
# A toad the size of a big dog in the wet grass at the village's edge. Skin and weight only: its croak is the tell.

GRASS = [f"kenney_impact-sounds:footstep_grass_{i:03d}" for i in range(5)]
WET = src.S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")
LEATHER = src.R("handleSmallLeather", "handleSmallLeather2")
SLUSH = [f"kenney_impact-sounds:footstep_snow_{i:03d}" for i in range(5)]


def pick(rng, keys):
    return keys[int(rng.integers(len(keys)))]


def blades(rng, length, density=200, lo=1200, hi=5500, shape=None):
    """Wet grass brushed: the rustle's leaf grains kept sparse, low and soft (a dense rustle turns to hiss), no stalk
    ticks (wet stems bend, they don't snap)."""
    r = synth.rustle(length, density, rng, f=(lo, hi), ticks=0, shape=shape)
    return unit(lp(r, hi * 1.3))


def settle(rng, length):
    """The body shifting its weight where it squats: grass pressed flat, the skin creaking and unsticking from it."""
    b = Bus(length + 0.4)
    b.at(0, blades(rng, length, 160, shape=env([(0, 0), (length * 0.45, 1), (length, 0)], length)), -12)
    # hairless skin rubbing on itself as the haunches fold: real leather handling, slowed into something bigger
    sk = clip(pick(rng, LEATHER), length=0.3, semis=-rng.uniform(5, 8), pre=0.06)
    b.at(rng.uniform(0.0, length * 0.3), unit(lp(sk, 3000)), -6)
    # the belly coming off the wet ground: a real wet step slowed, and the flesh giving
    w = clip(pick(rng, WET), length=0.3, semis=-rng.uniform(4, 7))
    b.at(length * rng.uniform(0.3, 0.6), unit(lp(w, 4500)), -10)
    b.at(length * rng.uniform(0.35, 0.6), squish(rng, rng.uniform(0.16, 0.26), 230, 1000, wet=0.6), -5)
    return b.x


def push_off(rng):
    """The hind legs thrusting off: a soft sweep of grass and the feet's shove into the ground."""
    b = Bus(0.4)
    b.at(0, blades(rng, 0.16, 500, 1400, 6000, env([(0, 0), (0.015, 1), (0.16, 0)], 0.16, "exp")), -10)
    b.at(0, thud(rng, 70, 0.14, 0.2), -8)
    return b.x


def land(rng, weight=1.0):
    """A fat wet body coming down soft in grass: the weight (a dull bump, no ring), wet skin slapping, the flesh giving,
    grass flattened under it."""
    b = Bus(0.8)
    b.at(0, thud(rng, 62, 0.3, 0.25), -2 + 3 * (weight - 1))
    g = clip(pick(rng, GRASS), length=0.2, semis=-rng.uniform(4, 6))
    b.at(0, unit(lp(g, 2500)), -6)
    w = clip(pick(rng, WET), length=0.2, semis=-rng.uniform(3, 6))
    b.at(0.003, unit(lp(w, 5000)), -8)
    b.at(0.008, squish(rng, rng.uniform(0.2, 0.3), 220, 950, wet=0.8), -6)
    b.at(0.01, blades(rng, 0.3, 300, shape=env([(0, 1), (0.3, 0)], 0.3, "exp")), -14)
    return b.x


def ribbit_hop(rng, k):
    """Take 0: a shift, a pause, one hop. Take 1: two small shifts as it turns, then the hop. Take 2: off at once, a
    heavier landing, and it settles itself where it came down."""
    m = rng.uniform(15, 17)
    shifts = [[rng.uniform(0.35, 0.45)], [rng.uniform(0.2, 0.26), rng.uniform(0.25, 0.32)], []][k]
    b = Bus(2.6)
    t = 0.0
    for L in shifts:
        b.at(t, at(settle(rng, L), m), 0)
        t += L + rng.uniform(0.06, 0.14)
    t += [0.2, 0.05, 0.0][k]
    b.at(t, at(push_off(rng), m), -3)
    t += rng.uniform(0.28, 0.36)
    m2 = m + rng.uniform(2.5, 4)
    b.at(t, at(land(rng, 1.2 if k == 2 else 1.0), m2), 0)
    if k == 2:
        b.at(t + rng.uniform(0.35, 0.45), at(settle(rng, rng.uniform(0.3, 0.38)), m2), -4)
    return finish(b.x, rng)


@recipe("sign", "ribbits", "hop",
        "A Ribbit out in the dark: its fat wet body shifting in the grass, then one soft hop away and a soft landing",
        """No throat at all (the croak is the tell). Out at 15-17 m: the toad shifts its weight where it squats, wet
        grass pressed and brushed, its hairless skin rubbing as the haunches fold (real leather handling slowed half an
        octave) and the belly unsticking from the wet ground (a real wet footstep slowed, and flesh giving). Then one
        hop: the grass swept back by the thrust, a moment in the air, and the body coming down soft a few metres
        further off (a dull bump, wet skin, grass flattened), the same wet weight as its kept hops but small and far.
        Takes: shift and hop; two shifts as it turns, then the hop; off at once, landing heavier and settling there.""",
        sources=LEATHER + WET + GRASS, takes=3, lufs=LUFS, gap=1.0)
def ribbits_hop(rng, k):
    return ribbit_hop(rng, k)


def peel(rng, length):
    """Wet skin peeling off mud as it lifts its belly: the seal letting go in tacky little ticks that quicken, suction
    in it, the slime stretching (a low squelch)."""
    b = Bus(length + 0.3)
    rate = env([(0, 300), (length * 0.7, 1500), (length, 200)], length)
    tack = synth.crackle(length, rate, rng, size=(0.0003, 0.0015), hi=400)
    b.at(0, unit(lp(dsp.resonate(tack, [750, 1500, 2600], q=2) + tack * 0.3, 4000)), -6)
    b.at(length * 0.25, squish(rng, length * 0.75, 200, 900), -5)
    b.at(0, blades(rng, length, 120, shape=env([(0, 0.3), (length * 0.6, 1), (length, 0)], length)), -16)
    return b.x


def drips(rng, length=0.5, n=10):
    """Water shaken off its skin by the landing, pattering down onto the grass: small soft ticks, thinning out."""
    b = Bus(length + 0.1)
    for t in np.sort(rng.exponential(length / 3, n)):
        if t < length:
            f = rng.uniform(1500, 3500)
            b.at(t, lp(synth.click(f, q=3, length=0.015, rng=rng), 4500), rng.uniform(-12, -3) - 10 * t / length)
    return b.x


def ribbit_peel(rng, k):
    """Take 0: a long peel and a hop. Take 1: a short peel, a pause, the hop. Take 2: two quick peels, the hop with a
    heavier slush."""
    m = rng.uniform(15, 17)
    b = Bus(2.6)
    t = 0.0
    for L in [[rng.uniform(0.4, 0.5)], [rng.uniform(0.25, 0.3)], [0.18, 0.24]][k]:
        b.at(t, at(peel(rng, L), m), 0)
        t += L + rng.uniform(0.06, 0.12)
    t += [0.05, 0.25, 0.0][k]
    b.at(t, at(push_off(rng), m), -5)
    flight = rng.uniform(0.26, 0.34)
    m2 = m + rng.uniform(2.5, 4)
    lb = Bus(1.2)
    lb.at(0, thud(rng, 58, 0.3, 0.2), -3)
    sl = clip(pick(rng, SLUSH), length=0.3, semis=-rng.uniform(5, 7))     # slush squashed, slowed into wet mud
    lb.at(0, unit(lp(sl, 3500)), -4 + (3 if k == 2 else 0))
    lb.at(0.01, kit.slap(rng, 0.1, 1800, wet=0.6), -12)
    lb.at(0.06, drips(rng, 0.5), -6)
    b.at(t + flight, at(lb.x, m2), 0)
    return finish(b.x, rng)


@recipe("sign", "ribbits", "peel",
        "A Ribbit out in the dark: wet skin peeling off the mud as it lifts, one hop away, water shaken off it",
        """Wetter and more about the skin than 'hop', and mostly synthesised. At 15-17 m its belly comes up off the mud:
        the slime's seal letting go in tacky little ticks that quicken (a crackle rung through a soft body) with a low
        squelch of it stretching. Then one hop: grass swept back by the thrust, and a soft landing a few metres off
        (a dull bump and real slush footsteps slowed into wet mud, a wet slap), the water shaken off its skin
        pattering down on the grass after. No throat: the croak is the tell.""",
        sources=SLUSH + GRASS, takes=3, lufs=LUFS, gap=1.0)
def ribbits_peel(rng, k):
    return ribbit_peel(rng, k)


# ---- Soot Children ---------------------------------------------------------------------------------------------------
# A child's small bare feet on the cinders by the line, soot sifting off its hands and hair. No voice, no breath through
# a mouth: the call for help is its lure, and the sign must never sound like a child you could go and find.

CINDERS = src.S("stones_01", "stones_02", "stones_03")


def cinders(rng, n=5, spread=0.025, semis=(2.0, 6.0), size=0.02):
    """Cinders giving under a small weight: a few tiny crunches cut from stones settling, pitched up (clinker is light,
    porous and glassy, not ballast)."""
    b = Bus(spread + size * 2 + 0.05)
    for _ in range(n):
        g = grain(rng, pick(rng, CINDERS), size * rng.uniform(0.6, 1.4), rng.uniform(*semis))
        b.at(abs(rng.normal(0, spread / 2)), norm(g, 0.1), rng.uniform(-10, 0))
    return b.x


def bare_foot(rng, weight=1.0):
    """A child's bare foot at a run: the ball of the foot comes down (a soft little pad of skin, no shoe, cinders
    giving under it), rolls, and pushes off, flicking a few cinders back."""
    b = Bus(0.3)
    b.at(0, lp(kit.slap(rng, 0.04, 1600, wet=0), 2500), -10)
    b.at(0, thud(rng, 150, 0.06, 0.1), -12)
    b.at(0.002, cinders(rng, int(rng.integers(3, 6))), -2)
    roll = rng.uniform(0.05, 0.09)
    b.at(roll * 0.5, cinders(rng, 2, 0.02, (3.0, 7.0), 0.012), -14)
    b.at(roll, cinders(rng, int(rng.integers(2, 5)), 0.03, (4.0, 8.0), 0.012), -9)
    return dsp.gain(b.x, 20 * np.log10(weight))


def kicked(rng):
    """A cinder kicked loose, skittering on: two or three glassy little ticks."""
    b = Bus(0.3)
    t = 0.0
    for i in range(int(rng.integers(2, 4))):
        f = rng.uniform(2500, 4500)
        b.at(t, synth.click(f, q=rng.uniform(8, 14), length=0.012, rng=rng, body=[f, f * 1.7, f * 2.6]), -6 * i)
        t += rng.uniform(0.04, 0.09)
    return b.x


def ash_puff(rng, length=0.35):
    """A puff of soot off its hands and hair as it bolts: soft dust in the air, grit in it, no wet, no voice."""
    n = synth.noise(length, rng, "pink")
    y = bp(n, 400, 4000) * env([(0, 0), (0.03, 1), (length * 0.4, 0.4), (length, 0)], length)
    return unit(mix(unit(y), unit(soot(rng, length, env([(0, 1500), (length, 100)], length))) * 0.6))


def soot_run(rng, k):
    """Takes: a run of five, six, four; a child's sprint (four steps a second, quickening a little)."""
    steps = [5, 6, 4][k]
    m0, m1 = rng.uniform(14.5, 16), rng.uniform(21, 23)
    b = Bus(2.4)
    b.at(0, ash_puff(rng, rng.uniform(0.25, 0.35)), -18)
    t, gap = 0.04, rng.uniform(0.23, 0.26)
    for i in range(steps):
        b.at(t, bare_foot(rng, 1.0 if i % 2 == 0 else 0.8), rng.uniform(-2, 0))
        if rng.random() < 0.3:
            b.at(t + rng.uniform(0.02, 0.05), kicked(rng), -16)
        t += gap * rng.uniform(0.9, 1.1)
        gap *= 0.96
    L = t + 0.25
    b.at(0, soot(rng, L, env([(0, 600), (0.4, 120), (L, 10)], L)), -22)
    return finish(away(even(b.x[:samples(L)]), m0, m1, 1.3), rng)


@recipe("sign", "sootChildren", "barefoot",
        "A Soot Child out there: small bare feet running off over cinders, a puff of soot where it stood",
        """No voice and no breath through a mouth: its call is the lure and the eyes are enough. A child's run at four
        steps a second, quickening, heading off from 15 m to 22 m: each footfall the soft pad of a small bare foot (a
        skin slap, darkened) and cinders giving under it (tiny crunches cut from real stones settling, pitched up, since
        clinker is light and glassy), now and then a cinder kicked skittering. As it bolts a puff of soot comes off its
        hands and hair (soft dust and the finest dry crackle), sifting down behind it.""",
        sources=CINDERS, takes=3, lufs=LUFS, gap=1.0)
def soot_children_barefoot(rng, k):
    return soot_run(rng, k)


def scuff(rng, length=0.18):
    """A bare foot twisting in cinders as it turns, or skidding: a dense run of crunches with the skin dragging."""
    b = Bus(length + 0.1)
    t = 0.0
    while t < length:
        b.at(t, cinders(rng, 2, 0.01, (0.0, 4.0), 0.015), -4 - 8 * t / length)
        t += rng.uniform(0.008, 0.02)
    drag = bp(synth.noise(length, rng), 500, 3000) * env([(0, 0), (0.02, 1), (length, 0)], length)
    b.at(0, unit(drag), -14)
    return b.x


def soot_breath(rng, length):
    """Soot hanging in the air where it was and sinking: a slow soft swell of dust, no mouth in it, and the finest
    crackle settling."""
    n = synth.noise(length, rng, "pink")
    y = bp(n, 300, 3000) * env([(0, 0), (length * 0.4, 1), (length, 0)], length)
    return unit(mix(unit(y), unit(soot(rng, length, env([(0, 50), (length * 0.4, 400), (length, 20)], length))) * 0.5))


def soot_scamper(rng, k):
    """It turns where it stood, scampers off unevenly, skids, and is gone; the soot it shook off hangs and sinks.
    Takes vary where it skids and how many steps there are either side."""
    before, after = [(2, 2), (1, 3), (3, 1)][k]
    m0, m1 = rng.uniform(14.5, 16), rng.uniform(20, 22)
    b = Bus(2.6)
    b.at(0, scuff(rng, rng.uniform(0.14, 0.2)), -2)
    t = rng.uniform(0.18, 0.24)
    for i in range(before):
        b.at(t, bare_foot(rng, rng.uniform(0.7, 1.0)), 0)
        t += rng.uniform(0.14, 0.22)
    b.at(t, scuff(rng, rng.uniform(0.2, 0.28)), -3)
    b.at(t + 0.05, kicked(rng), -12)
    t += rng.uniform(0.22, 0.28)
    for i in range(after):
        b.at(t, bare_foot(rng, rng.uniform(0.6, 0.9)), -2)
        t += rng.uniform(0.16, 0.22)
    L = t + 0.15
    run = away(even(b.x[:samples(L)]), m0, m1, 1.6)
    out = Bus(L + 0.8)
    out.at(0, run, 0)
    out.at(0.1, at(soot_breath(rng, min(rng.uniform(0.9, 1.1), L)), m0), -24)
    return finish(out.x, rng)


@recipe("sign", "sootChildren", "scamper",
        "A Soot Child out there: a bare foot twisting in the cinders, an uneven scamper off with a skid, soot hanging",
        """Less a run than 'barefoot', more a startled thing: a small bare foot twisting in the cinders as it turns (a
        dense rush of tiny crunches from real stones, pitched up, the skin dragging), a few light uneven steps, a skid
        that kicks a cinder skittering, a step or two more and gone (15 m to 21 m). The soot it shook off hangs where it
        stood and sinks, a slow soft swell of dust with the finest crackle settling in it: a breath of soot, with no
        mouth in it. No voice: its call is the lure.""",
        sources=CINDERS, takes=3, lufs=LUFS, gap=1.0)
def soot_children_scamper(rng, k):
    return soot_scamper(rng, k)


# ---- Whistler --------------------------------------------------------------------------------------------------------
# Twenty hooked legs landing in rolling waves, ivory plates, four metres of coil, here going off through scrub. Its
# siphon stays shut: the whistle is the tell.

def tip(rng, a):
    """One hooked leg-tip going down through brush: a dry stem snapping, a leaf crushed, or the stony ground."""
    r = rng.random()
    if r < 0.4:
        x = norm(kit.snap(rng, rng.uniform(0.02, 0.04), rng.uniform(1800, 3600)), 0.1)
    elif r < 0.75:
        L = rng.uniform(0.015, 0.04)
        x = norm(synth.rustle(L, 3000, rng, f=(2000, 8000), ticks=0) * env([(0, 1), (L, 0)], L), 0.1)
    else:
        x = norm(ballast(rng, 0.4, 0.02), 0.1)
    return x * a


def brush_wave(rng, legs=10, span=0.16, weight=1.0):
    """One wave of legs down a side, front to back in a rolling flam (as its run's legs on ballast), through brush."""
    out = Bus(span + 0.15)
    for k in range(legs):
        u = k / (legs - 1)
        t = span * u ** rng.uniform(0.85, 1.15) + rng.normal(0, 0.003)
        a = (0.45 + 0.55 * np.sin(np.pi * u) ** 0.7) * rng.uniform(0.6, 1.0) * weight
        out.at(max(t, 0), tip(rng, a))
        if rng.random() < 0.35:
            out.at(max(t + rng.uniform(0, 0.004), 0), norm(plate_click(rng, rng.uniform(2500, 4500)), 0.05) * a, -6)
    return out.x


BOOK = src.R("bookFlip2", "bookFlip3")


def foliage(rng, length, rate, semis=(-5.0, -1.0)):
    """Leaves and scrub pushed about: grains of real paper riffling (pages are leaves), pitched down into bigger
    leaves, `rate` grains a second (a constant or a curve) so it moves in rushes, never a steady hiss."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.2)
    t = 0.0
    while t < length:
        r = max(float(rc[min(samples(t), n - 1)]), 1.0)
        t += rng.exponential(1 / r)
        if t >= length:
            break
        g = grain(rng, pick(rng, BOOK), rng.uniform(0.04, 0.12), rng.uniform(*semis), attack=False)
        b.at(t, norm(g, 0.1), rng.uniform(-9, 0))
    return unit(lp(b.x[:n], 9000))


def brush(rng, length, shape, lo=1800, hi=8000, density=700):
    """Scrub and dry grass pushed aside and springing back as something long goes through it: leaf grains sparse
    enough to crackle (a dense rustle is only hiss), stalks ticking."""
    return unit(synth.rustle(length, density, rng, f=(lo, hi), ticks=1.0, shape=shape))


def whistler_waves(rng, k):
    """Takes: four and five waves; the last a burst of two that stops, then two more further off."""
    m0, m1 = rng.uniform(14, 15.5), rng.uniform(23, 26)
    period = rng.uniform(0.27, 0.31)
    waves = [4, 5, 2][k]
    b = Bus(2.6)
    t = 0.0
    for i in range(waves):
        b.at(t + rng.normal(0, 0.004), brush_wave(rng, 10, rng.uniform(0.13, 0.18), 1.0 - 0.04 * i), -1.5 * (i % 2))
        t += period * rng.uniform(0.95, 1.05)
    if k == 2:     # a beat's stop, then on
        t += 0.12
        for i in range(2):
            b.at(t, brush_wave(rng, 10, 0.15, 0.7), -3)
            t += period
    L = t + 0.15
    sh = env([(0, 0), (0.08, 1), (L * 0.7, 0.6), (L, 0)], L)
    b.at(0, foliage(rng, L, 30 * sh), -16)
    b.at(0, brush(rng, L, sh, density=300), -22)
    b.at(0, chatter(rng, L, 7), -22)
    return finish(away(even(b.x[:samples(L)]), m0, m1, 1.4), rng)


@recipe("sign", "whistler", "waves",
        "The Whistler out there: twenty legs going off fast through the scrub in rolling waves, plates ticking",
        """Its run's gait, through brush instead of ballast, and no siphon (the whistle is the tell). Each wave is ten
        hooked tips landing front to back in a rolling flam, the two sides alternating about three and a half times a
        second, so it rolls like no animal: here every tip snaps a dry stem, crushes a leaf, or now and then finds stony
        ground (a crunch cut from stones settling), with a dry tick of ivory plate on some. Scrub rustles along its
        length (grains of real page-riffling pitched down into leaves) and the plates chatter faintly while it goes
        from 15 m to 25 m. One take stops for a beat, then goes on.""",
        sources=STONES + BOOK, takes=3, lufs=LUFS, gap=1.0)
def whistler_brush_waves(rng, k):
    return whistler_waves(rng, k)


def snaps(rng, length, n):
    """Stems giving one after another down its length as the coil goes through: a ripple of dry snaps."""
    b = Bus(length + 0.1)
    for t in np.sort(rng.uniform(0, length, n)):
        b.at(t, kit.snap(rng, rng.uniform(0.02, 0.05), rng.uniform(1500, 3200)), rng.uniform(-8, 0))
    return b.x


def spring_back(rng):
    """A branch it bent whipping back after it's gone through, and its leaves shaking themselves still."""
    b = Bus(0.9)
    b.at(0, kit.whoosh(rng, 0.14, 1500, 6000, 0.3), -6)
    sh = env([(0, 1), (0.6, 0)], 0.6, "exp")
    shake = dsp.tremolo(synth.rustle(0.6, 600, rng, f=(2500, 9000), ticks=0.5), rng.uniform(9, 13), 0.8) * sh
    b.at(0.08, unit(shake), -8)
    return b.x


def whistler_wake(rng, k):
    """Takes vary how long the coil takes to pass, and whether a branch springs back."""
    m0, m1 = rng.uniform(14, 15.5), rng.uniform(22, 25)
    L = [1.0, 1.15, 0.85][k] * rng.uniform(0.95, 1.05)
    b = Bus(L + 1.0)
    # four metres of coil parting the scrub: a swell as the head shoulders in, rough as the plates drag through, easing
    # as the tail goes by
    sh = env([(0, 0), (L * 0.18, 1), (L * 0.5, 0.8), (L, 0)], L)
    b.at(0, foliage(rng, L, 10 + 70 * sh), -3)
    b.at(0, brush(rng, L, sh, 1500, 9000, 400), -12)
    rough = np.abs(lp(rng.standard_normal(samples(L)).astype(np.float32), 30))
    drag = bp(synth.noise(L, rng), 400, 2000) * (rough / (np.max(rough) + 1e-9)) * sh
    b.at(0, unit(drag), -9)
    b.at(0.05, snaps(rng, L * 0.85, int(rng.integers(7, 12))), -2)
    # the legs only just heard under it, rolling
    t = 0.05
    while t < L - 0.1:
        b.at(t, brush_wave(rng, 10, 0.15, 0.5), -10)
        t += rng.uniform(0.27, 0.31)
    if k != 2:
        b.at(L - 0.05, spring_back(rng), -6)
    n = samples(L + (0.6 if k != 2 else 0.1))
    return finish(away(b.x[:n], m0, m1, 1.3), rng)


@recipe("sign", "whistler", "wake",
        "The Whistler out there: four metres of coil rushing off through the scrub, stems snapping down its length",
        """The body, not the legs: something long goes through the brush fast, so the rush of leaves swells as the
        head shoulders in and lasts the length of a four-metre coil (grains of real page-riffling pitched down into
        leaves, coming in rushes, with stalks ticking and a rough drag where the plates push through), dry stems
        snapping one after another down its length, its rolling waves of legs only just heard under it, and in two
        takes a branch it bent whipping back after it and shaking still. From 15 m to 24 m. No siphon: the whistle is
        the tell.""",
        sources=STONES + BOOK, takes=3, lufs=LUFS, gap=1.0)
def whistler_brush_wake(rng, k):
    return whistler_wake(rng, k)


# ---- Grumbler --------------------------------------------------------------------------------------------------------
# A man on bare palms and soles, a second pair of arms through the ribs, muttering to himself. Its gnaw is the tell (dry
# teeth rasping at 1.4-2.2 kHz in runs), so the sign is low: weight and skin behind a wall, and that voice.

def wall(x, f=1100):
    """Heard from the other side of a brick wall: what comes round its ends and over its top, the top end lost."""
    return mix(lp(x, f, 2) * 0.9, lp(x, f * 3, 2) * 0.12)


def grumble(rng, length, f0, rate):
    """His mutter (beasts.mutter: a man grumbling to himself on shut vowels), eased in and out so a phrase cut short
    doesn't click."""
    return dsp.fade(mutter(rng, length, f0, rate=rate), 0.01, 0.06)


def heavy_scuttle(rng, length, beat):
    """His run over the yard's ground: palms and soles slapped down in diagonal pairs (beasts.palm), heavier and slower
    than on the crane, the odd rib arm's slap."""
    b = Bus(length + 0.6)
    t = 0.0
    while t < length:
        g = rng.uniform(-3, 0)
        b.at(t, palm(rng, None, 1.15, "ground"), g)
        b.at(t + rng.uniform(0.015, 0.035), palm(rng, None, 1.0, "ground"), g - rng.uniform(1, 4))
        b.at(t, kit.thud(rng, 75, 0.12, 0.8), g - 6)
        if rng.random() < 0.2:
            b.at(t + rng.uniform(0.05, 0.08), palm(rng, None, 0.8, "ground"), g - 8)
        t += beat * rng.uniform(0.85, 1.15)
    return b.x


def grumbler_yard(rng, k):
    """Take 0: a scuttle, then he mutters. Take 1: a mutter, then a scuttle off. Take 2: a short burst, a stop, a
    grumble under his breath, a few more steps."""
    m = rng.uniform(16, 19)
    b = Bus(3.0)
    if k == 0:
        L = rng.uniform(0.6, 0.7)
        b.at(0, heavy_scuttle(rng, L, rng.uniform(0.16, 0.19)), 0)
        b.at(L + 0.08, grumble(rng, rng.uniform(0.6, 0.7), rng.uniform(82, 92), rate=4.5), -4)
    elif k == 1:
        M = rng.uniform(0.5, 0.6)
        b.at(0, grumble(rng, M, rng.uniform(85, 95), rate=5), -4)
        b.at(M + 0.05, heavy_scuttle(rng, rng.uniform(0.6, 0.7), rng.uniform(0.15, 0.18)), 0)
    else:
        b.at(0, heavy_scuttle(rng, 0.35, 0.17), 0)
        b.at(0.5, grumble(rng, rng.uniform(0.42, 0.5), rng.uniform(80, 88), rate=4), -5)
        b.at(1.05, heavy_scuttle(rng, 0.35, 0.19), -3)
    return finish(at(wall(dsp.trim_silence(b.x, -70)), m), rng)


@recipe("sign", "grumbler", "yard",
        "A Grumbler out there: a heavy scuttle on bare hands and feet behind a wall, and a low mutter",
        """His creature sounds' body, kept low: no teeth, nothing that rasps (the gnaw is the tell). Behind a wall
        16-19 m off, so only what comes round it and over the top is heard: bare palms and soles slapped down in
        diagonal pairs on the yard's ground (real hand slaps dropped into broad flat palms and ballast crunches, as his
        scuttle on the crane, but slower and heavier with a dull weight under each), and the man's voice grumbling to
        himself on shut vowels, the same mutter as his scuttle. Takes: scuttle then mutter; mutter then scuttle off; a
        burst, a grumble, a few more steps.""",
        sources=["sfx_100_v2:switch_01", "sfx_100_v2:switch_02"] + BALLAST, takes=3, lufs=LUFS, gap=1.0)
def grumbler_yard_sign(rng, k):
    return grumbler_yard(rng, k)


TIN = [f"kenney_impact-sounds:impactTin_medium_{i:03d}" for i in range(5)]


def wobble(x, rate, cents):
    """A sheet flexing back and forth: its pitch bent up and down `cents` at `rate` a second, the bend easing as it
    settles (varispeed along a sine)."""
    n = len(x)
    t = np.arange(n) / SR
    depth = cents * np.exp(-t / (t[-1] / 2 + 1e-3))
    r = 2 ** (depth * np.sin(2 * np.pi * rate * t) / 1200)
    pos = np.cumsum(r) - r[0]
    pos = pos[pos < n - 1]
    return np.interp(pos, np.arange(n), x).astype(np.float32)


def sheet(rng, weight=1.0):
    """A palm slapping a corrugated tin fence from the other side: the slap through the sheet, then the sheet itself
    booming and wobbling as it flexes (an oil-can's 'wub'), the corrugations shimmering, its loose nails rattling."""
    b = Bus(1.2)
    hit = clip(pick(rng, TIN), length=0.18, semis=-rng.uniform(7, 10))
    b.at(0, unit(lp(hit, 2200)), -3)
    L = rng.uniform(0.6, 0.8)
    exc = synth.noise(L, rng) * env([(0, 1), (0.03, 0.5), (L, 0)], L, "exp")
    f1 = rng.uniform(48, 60)
    modes = [f1 * m for m in (1.0, 1.52, 2.17, 2.91, 4.05, 5.6)]
    boom = dsp.resonate(lp(exc, 800), modes, q=25, gains=[1.0, 0.8, 0.6, 0.45, 0.3, 0.2])
    rate = rng.uniform(5, 7.5)
    boom = dsp.tremolo(wobble(unit(boom), rate, 60), rate, 0.5)
    b.at(0, dsp.shaped(unit(boom), [(0, 1), (L * 0.5, 0.4), (L, 0.02)], "exp"), -4)
    sh = dsp.resonate(exc, [rng.uniform(700, 900), rng.uniform(1300, 1600), rng.uniform(2300, 2700)], q=30)
    b.at(0, dsp.shaped(unit(wobble(sh, rate, 40)), [(0, 1), (L * 0.6, 0.02)], "exp"), -18)
    for t in np.sort(rng.uniform(0.01, 0.15, int(rng.integers(2, 5)))):
        f = rng.uniform(1100, 1900)
        b.at(t, lp(synth.click(f, q=10, length=0.02, rng=rng), 2500), -20)
    return dsp.gain(b.x, 20 * np.log10(weight))


def grumbler_fence(rng, k):
    """Take 0: along the fence, a hand catching it twice, then a mutter. Take 1: a mutter, a hand against the tin as he
    goes. Take 2: one heavy shove into the fence, the scuttle off, a grumble."""
    m = rng.uniform(16, 19)
    b = Bus(3.0)
    if k == 0:
        L = rng.uniform(0.75, 0.85)
        b.at(0, wall(heavy_scuttle(rng, L, rng.uniform(0.16, 0.19))), 0)
        for t in (rng.uniform(0.1, 0.2), rng.uniform(0.45, 0.6)):
            b.at(t, sheet(rng, rng.uniform(0.7, 1.0)), -3)
        b.at(L + 0.1, wall(grumble(rng, rng.uniform(0.5, 0.6), rng.uniform(82, 92), rate=4.5)), -5)
    elif k == 1:
        M = rng.uniform(0.5, 0.6)
        b.at(0, wall(grumble(rng, M, rng.uniform(85, 95), rate=5)), -5)
        L = rng.uniform(0.6, 0.7)
        b.at(M + 0.05, wall(heavy_scuttle(rng, L, rng.uniform(0.15, 0.18))), 0)
        b.at(M + 0.05 + L * rng.uniform(0.3, 0.6), sheet(rng, 0.8), -4)
    else:
        b.at(0, sheet(rng, 1.0), 0)
        b.at(0.02, kit.thud(rng, 70, 0.2, 1.0), -6)
        L = rng.uniform(0.55, 0.65)
        b.at(0.25, wall(heavy_scuttle(rng, L, rng.uniform(0.16, 0.19))), -1)
        b.at(0.3 + L, wall(grumble(rng, rng.uniform(0.45, 0.55), rng.uniform(80, 88), rate=4)), -6)
    return finish(at(dsp.trim_silence(b.x, -70), m), rng)


@recipe("sign", "grumbler", "fence",
        "A Grumbler out there: something heavy scuttling behind a tin fence, a hand booming on the sheets, a mutter",
        """The same heavy scuttle and mutter as 'yard', but on the far side of a corrugated tin fence it brushes as it
        goes: now and then a palm or a shoulder slaps the tin from behind (a real tin knock dropped most of an octave,
        darkened) and the sheet booms and wobbles low and its loose nails rattle. The fence makes it legible at 16-19 m
        without a single high, rasping sound in it (the gnaw is the tell). Takes: along the fence and a mutter; a
        mutter, then off with a hand on the tin; a heavy shove into the fence, the scuttle off, a grumble.""",
        sources=TIN + ["sfx_100_v2:switch_01", "sfx_100_v2:switch_02"] + BALLAST, takes=3, lufs=LUFS, gap=1.0)
def grumbler_fence_sign(rng, k):
    return grumbler_fence(rng, k)
