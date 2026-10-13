"""G1's new creatures on the ground at a stop (queue #125, note 388): the Mourners, Tower Jaw and the Freight Beetle, heard
ahead of their sims (docs/design/creatures/*.md as G1 lands them). The train's own (the Brakeman, the Knotter, Hotbox) are
in g1_train.py.

What each is decides the build:
- The Mourners: small ash-pale scavengers that come only after a crewmate dies, a nervous group that never touches the
  living and drags the body off the line. Their keening is several thin throats out in the dark, each on its own slow,
  wavering line that falls away in sobs: heterophony, never a chord and never in unison. Nothing sung on a scale (that's
  the Choir), no words and no rising call (the Soot Children's). Small throats, so a short tract and a high, breathy
  voice; ash-dry, so no wet in them. Startled, they click: tongues off the palate or mandibles, dry.
- Tower Jaw: a corrupted beaver the size of a bear, gnawing a stop's water tower, coaling tower or crane gantry down
  across the line. Its gnaw is deep, a chisel going into a thick timber leg in steady runs, chunks splitting off, held
  low (200 Hz-1.3 kHz) and percussive so it can't be the Grumbler's high rasp on crates (1.4-2.2 kHz). The tower it
  works on is real timber: its creak, its groan as it goes, the leg's crack like a shot, and the fall: timbers breaking,
  a tank bursting, water, planks on the rails.
- The Freight Beetle: a beetle the size of a pony that shoves a loose crate away from whoever's nearest. The crate's
  scrape comes in surges, one with each tripod of legs, its shell creaking under the load; startled, its plates clatter
  and it hisses through its spiracles.

The living parts are synthesised (throats through a tract, stick-slip, clicks); the wood, ground, cloth and water are
real recordings, bent and cut into grains. Nothing creature-made is left in the sub: the weight is kept in the 120-500 Hz
of wood and body (the director's note against boomy and squishy).
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
from recipes.beasts import fry, unit, main_hit, throat
from recipes.boarders import cycle as seam, grain
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.moose_tells import burst, ring, held, cycle

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

HINGES = src.R("creak2", "creak3")                     # real hinge squeals: a steady, wavering tone near 1.1 kHz
CLOTH = src.R("cloth1", "cloth2", "cloth3", "cloth4")   # real cloth handled, whacked and dragged
GRAVEL = src.S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02")   # real stones and gravel steps
CHOP = "kenney_rpg-audio:chop"                          # a real blade chopped into a board
SLAP = "kenney_rpg-audio:bookPlace2"                    # a real book slapped down flat
LEATHER = "kenney_rpg-audio:dropLeather"                # a real heavy leather drop
WOOD_HEAVY = src.K("impactWood_heavy")                  # heavy blows on timber
DECK = src.S("wood_hit_01", "wood_hit_02")              # heavy blows on planking
PLANK = src.K("impactPlank_medium")                     # a plank's hollow knock
KNOCKS = src.K("impactWood_medium")                     # a block of wood knocked
CRACKS = src.S("misc_34", "misc_35")                    # real wood cracking and breaking
DOORS = src.R("doorOpen_1", "doorOpen_2")               # real door creaks: big, slow wood
ROAR = "sfx_100_v2:misc_24"                             # a real roar: a throat round 200 Hz
GASP = "sfx_100_v2:misc_05"                             # a real sneeze and gasp: air forced through a mouth
WHIP = src.S("switch_01", "switch_02")                  # a real switch cracked through the air
SLAMS = src.S("door_03", "misc_36")                     # a real door slammed: big boards taking a blow
IRON = src.K("impactMetal_heavy")                       # heavy iron struck: a rail, a tank's hoop
SPLASH = "sfx_100_v2:loop_machine_02"                   # water rushing and splashing
SEA = "sfx_100_v2:loop_water_01"                        # a mass of water moving
TRICKLE = "sfx_100_v2:loop_water_02"                    # water pouring and trickling
RIFFLE = "kenney_rpg-audio:bookFlip2"                   # a real book's pages riffled: stiff leaves flicking past each other


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def done(x, length=None, fade=0.06, low=90):
    """Every take's last step: a steep highpass at `low` Hz, so a thud keeps its weight in the wood and the body and
    never turns to sub, and no DC is left; cut to `length` s if given, the last of it faded."""
    y = hp(np.asarray(x, np.float32), low, 4)
    if length is not None and len(y) > samples(length):
        y = dsp.fade(y[:samples(length)], 0.0, fade)
    return y


def air(x, rng, wet=0.12, tail=0.5):
    """Open ground at a stop at night: a little of its reverb, the tail cut short so a one-shot fired again doesn't pile
    up a wash."""
    x = dsp.trim_silence(x, -60)
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def loop_of(b, length, rng, wet=0.1):
    """A loop's bus (not a tell's) folded to exactly one cycle, the room's tail and whatever ran over ringing on into the
    head, handed to build.py so its seam gives the cycle back."""
    y = dsp.room(done(b.x), "night", wet=wet, rng=rng)
    return seam(dsp.wrap(y, samples(length)))


def wander(rng, n, rate, depth):
    """A slow uneven drift (a pitch in cents, a level), unit-peaked then scaled to `depth`."""
    w = lp(rng.standard_normal(n + SR).astype(np.float32), rate)[SR:]
    return (w / (np.max(np.abs(w)) + 1e-9) * depth).astype(np.float32)


def far(x, metres):
    """Out in the dark `metres` away: quieter and darker (the room comes later, once, on the whole mix)."""
    return dsp.gain(lp(x, float(np.clip(18000 / (1 + metres / 25), 2500, 18000))), -12 * np.log10(max(metres, 1.0) / 5))


@functools.lru_cache(maxsize=4)
def splinters():
    """Every crack in the real breaking-wood recordings, cut to 50 ms: fibres and splinters giving."""
    out = []
    for k in CRACKS:
        x = ck.get(k)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.015, prom=5):
            out.append(unit(ck.cut(x, s - samples(0.001), s + samples(0.05), 0.0005, 0.025)))
    return out


def splinter(rng, length, rate, semis=(-2, 4)):
    """Wood splintering: real cracks strewn at `rate` a second (a constant or a curve), a few loud and many quiet."""
    g = splinters()
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.06)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        b.at(t, dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis)), 20 * np.log10(rng.uniform(0.1, 1) ** 1.5))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return unit(b.x)


def rec(rng, keys, semis=0.0, length=0.4, pre=0.002):
    """A recording from its loudest hit, varispeeded to size, cut short."""
    return unit(dsp.vari(main_hit(keys[int(rng.integers(len(keys)))], length, pre), semis))


# ---- The Mourners: keening (600 Hz-2.5 kHz), startled clicks, the drag ---------------------------------------------------

KEEN = (600, 2500)
CLICK = (600, 4000)
CLICK_LUFS = -23.0     # all attack: a quieter integrated loudness leaves the clicks their peaks
KEEN_L = 12.0
MTRACT = 1.25          # a Mourner's throat as a share of a child's (synth.TRACT): shorter, so its formants sit higher
PITCHES = (640, 735, 815, 905, 1010)   # each voice's home pitch (Hz): no two a simple ratio apart, so they never agree


def plan(rng, length, voices, lead=(0.0, 3.0), phrase=(1.6, 3.8), gap=(0.8, 2.6), hole=0.3):
    """Who keens when: per voice, its phrases [(start, length, home Hz, fall in semitones, sob times)], each voice starting
    at its own moment and breathing at its own gaps, so the lines overlap without ever lining up. Drawn again until no
    moment of the cycle (its end wrapping round to its head) is left without a voice for more than `hole` s."""
    while True:
        homes = [PITCHES[i] * rng.uniform(0.97, 1.03) for i in rng.permutation(len(PITCHES))[:voices]]
        out = []
        for f in homes:
            t, ps = rng.uniform(*lead), []
            while t < length:
                L = rng.uniform(*phrase)
                sobs = sorted(rng.uniform(0.35, 0.75, int(rng.integers(0, 3))) * L)
                ps.append((t, L, f * 2 ** (rng.uniform(-1.5, 1.5) / 12), rng.uniform(4, 8), sobs))
                t += L + rng.uniform(*gap)
            out.append(ps)
        on = np.zeros(int(length * 20), bool)       # 50 ms steps; a phrase's faded last fifth doesn't count
        for ps in out:
            for t, L, *_ in ps:
                i = np.arange(int(t * 20), int((t + L * 0.8) * 20)) % len(on)
                on[i] = True
        runs = np.diff(np.flatnonzero(np.concatenate([on, on])))
        if on.any() and (not len(runs) or runs.max() <= hole * 20 + 1):
            return out


def keen_f0(rng, L, f, fall, sobs):
    """One phrase's pitch: a scoop up into the note, a held line that wanders and trembles (no two alike), sobs that
    break it upward for an instant, and a long fall away at the end."""
    n = samples(L)
    pts = [(0, f * 2 ** (-2 / 12)), (0.22, f * 2 ** (0.6 / 12)), (L * rng.uniform(0.4, 0.5), f * 2 ** (-0.8 / 12)),
           (L * 0.9, f * 2 ** (-fall * 0.8 / 12)), (L, f * 2 ** (-fall / 12))]
    c = wander(rng, n, rng.uniform(0.5, 1.2), rng.uniform(45, 75))    # never still, so two voices never settle into a chord
    t = np.arange(n) / SR
    rate = rng.uniform(5.2, 7.2) * (1 + wander(rng, n, 1.5, 0.12))
    c += np.sin(2 * np.pi * np.cumsum(rate) / SR) * rng.uniform(12, 24) * np.clip(t / (L * 0.5), 0.2, 1)
    for s in sobs:          # the voice breaking up a couple of semitones for a moment
        c += 250 * np.exp(-((t - s) / 0.03) ** 2)
    return (env(pts, L, "exp")[:n] * 2 ** (c / 1200)).astype(np.float32)


def keen_shape(rng, L, sobs):
    """A phrase's level: a soft swell in, the line's own unevenness, a catch at each sob, and dying with the fall."""
    e = env([(0, 0), (rng.uniform(0.18, 0.35), 0.8), (L * 0.45, 1), (L * 0.85, 0.6), (L, 0)], L)
    t = np.arange(len(e)) / SR
    e = e * (1 + wander(rng, len(e), 3, 0.25))
    for s in sobs:
        e = e * (1 - 0.75 * np.exp(-((t - s + 0.02) / 0.035) ** 2))
    return np.clip(e, 0, None).astype(np.float32)


VOWELS = [[(0, "u"), (0.6, "e"), (1.6, "i")], [(0, "o"), (0.5, "u"), (1.4, "e")], [(0, "e"), (0.8, "i"), (1.8, "u")]]


def keen_phrase(rng, L, f, fall, sobs, breathy=0.72):
    """One phrase of one Mourner: a small throat, breathy and dry, through a short tract on a thin 'oo-eh-ee'."""
    f0 = keen_f0(rng, L, f, fall, sobs)
    g = synth.glottis(f0, L, rng, jitter=0.008, shimmer=0.18, oq=breathy)
    v = synth.tract(g, VOWELS[int(rng.integers(3))], MTRACT, breath=0.4, rng=rng)
    return hp(v, 450) * keen_shape(rng, L, sobs)


def sob_in(rng, length=0.3):
    """The breath caught before a phrase: a ragged inhale in two or three shudders, through the small mouth."""
    sh = env([(0, 0), (length * 0.6, 1), (length, 0)], length)
    br = synth.breath(length, [(0, "h"), (length, "i")], MTRACT, rng, shape=sh)
    shudder = 0.45 + 0.55 * fry(rng, rng.uniform(7, 11), length, jitter=0.2, decay=0.03)
    return unit(bp(br, 900, 4000) * shudder)


@recipe("tell-mourners", "keening", "throats",
        "Several small, thin throats keening out in the dark, each on its own slow wavering line, never together",
        """Four Mourners at 6-20 m in the dark, synthesised: each a small, breathy throat (650-1000 Hz, through a tract
        shorter than a child's on a thin 'oo-eh-ee') keening in phrases of two to four seconds: a scoop up into the note,
        a line that sinks, wanders and trembles on its own, a sob that breaks it upward for an instant, and a long fall away of
        four to eight semitones. Each voice starts and breathes (a ragged, shuddering inhale) at its own moments and
        keeps its own home pitch, none a simple ratio from another, so the lines overlap but never line up: heterophony,
        never a chord or a unison. No scale and no words. A 12 s cycle, seamless. Held to 600 Hz-2.5 kHz.""",
        takes=1, loop=True, band=KEEN, lufs=-22, seconds=KEEN_L)
def keening_throats(rng, k):
    L = KEEN_L
    b = Bus(L + 5.0)
    for ps in plan(rng, L, 4):
        m = rng.uniform(6, 20)
        for t, pl, f, fall, sobs in ps:
            b.at(t, far(keen_phrase(rng, pl, f, fall, sobs), m), rng.uniform(-3, 0))
            b.at(max(0.0, t - 0.32), far(sob_in(rng, rng.uniform(0.22, 0.32)), m), -17)
    return cycle(b.x, L, rng, KEEN, drive=2, wet=0.22)


@functools.lru_cache(maxsize=4)
def hinge_core(key):
    """The steadiest 0.15 s of a real hinge's squeal: where most of its energy sits round its 1.1 kHz tone."""
    x = ck.get(key)
    e = lp(np.abs(bp(x, 900, 1400)), 20)
    w = samples(0.15)
    a = int(np.argmax(np.convolve(e, np.ones(w), "valid")))
    return unit(x[a:a + w])


def hinge_phrase(rng, L, f, fall, sobs):
    """One phrase from a hinge: its squeal held out into a tone (paulstretched, so it keeps its grain without its
    jerks), dropped to the voice's pitch, bent along the same keening line as a throat's, and coloured by a small
    mouth's formants."""
    key = HINGES[int(rng.integers(len(HINGES)))]
    seg = dsp.vari(hinge_core(key), 12 * np.log2(f / 1100))
    tone = dsp.smear(seg, (L * 1.6 + 0.3) / (len(seg) / SR), 0.12, rng)
    c = 12 * np.log2(keen_f0(rng, L, f, fall, sobs) / f)
    y = dsp.fit(bend(tone, c), samples(L))
    mouth = synth.tract(y, VOWELS[int(rng.integers(3))], MTRACT, breath=0.0)
    y = mix(unit(y) * 0.5, unit(mouth))
    return hp(y, 450) * keen_shape(rng, L, sobs)


@recipe("tell-mourners", "keening", "hinges",
        "The keening from real hinge squeals: thin wavering tones that sob and fall, several out of step, never together",
        """Kitbashed: the steadiest moment of two real door-hinge squeals (a thin, wavering tone near 1.1 kHz) held out
        into long tones (paulstretched, so they keep the hinge's grain), dropped to each Mourner's pitch (650-1000 Hz),
        bent along the same keening lines as 'throats' (a scoop up, a wandering, trembling hold, a sob, a fall of four
        to eight semitones) and coloured by a small mouth's formants. Four voices at 6-20 m, each with its own home pitch
        and its own breaths (a ragged, shuddering inhale), so they overlap but never agree. Thinner and less human than
        'throats'. A 12 s cycle, seamless. Held to 600 Hz-2.5 kHz.""",
        sources=HINGES, takes=1, loop=True, band=KEEN, lufs=-22, seconds=KEEN_L)
def keening_hinges(rng, k):
    L = KEEN_L
    b = Bus(L + 5.0)
    for ps in plan(rng, L, 4):
        m = rng.uniform(6, 20)
        for t, pl, f, fall, sobs in ps:
            b.at(t, far(hinge_phrase(rng, pl, f, fall, sobs), m), rng.uniform(-3, 0))
            b.at(max(0.0, t - 0.32), far(sob_in(rng, rng.uniform(0.22, 0.32)), m), -17)
    return cycle(b.x, L, rng, KEEN, drive=2, wet=0.22)


def tongue(rng, f):
    """One click of a small throat: the tongue snapped off the palate (a sharp suck of pressure), the little mouth ringing
    for a few milliseconds. Dry: nothing wet in it."""
    x = burst(rng, rng.uniform(0.0004, 0.0009), attack=0.1)
    y = ring(x, [f, f * rng.uniform(1.55, 1.8), f * rng.uniform(0.5, 0.6)], q=rng.uniform(5, 9), tail=0.02,
             gains=[1, 0.45, 0.6])
    return unit(y) * env([(0, 1), (len(y) / SR, 0.02)], len(y) / SR, "exp")


@functools.lru_cache(maxsize=1)
def chop_tick():
    """The first 25 ms of a real blade chopped into a board: a dry, woody tick."""
    return unit(main_hit(CHOP, 0.025, 0.001))


def mandible(rng, f):
    """A pair of mandibles snapped shut: two hard little plates meeting a few milliseconds apart, a real dry tick under
    them pitched up to the size of the jaw."""
    b = Bus(0.05)
    b.at(0, synth.click(f, q=rng.uniform(10, 16), length=0.015, rng=rng), 0)
    b.at(rng.uniform(0.002, 0.005), synth.click(f * rng.uniform(0.8, 0.9), q=rng.uniform(8, 14), length=0.012, rng=rng),
         -rng.uniform(3, 7))
    b.at(0, dsp.vari(chop_tick(), rng.uniform(4, 8)), -9)
    return unit(b.x)


def alarm(rng, kind, click, rate=(9, 16), run=(2, 6), f=(1300, 2600)):
    """A startled group: one Mourner clicks and the rest answer, each in its own run (`rate` a second, slowing as it
    trails off) at its own pitch and distance. `kind` 0: all at once; 1: one alarm, then the others spreading; 2: a
    flurry, a pause, and a second, farther and quieter as they scatter."""
    b = Bus(2.0)
    flurries = [(0.0, 0.0, 0.0)] if kind != 2 else [(0.0, 0.0, 0.0), (rng.uniform(0.45, 0.6), -6, 6)]
    for t0, db, further in flurries:
        for i in range(int(rng.integers(4, 7))):
            if kind == 1:
                t = t0 if i == 0 else t0 + rng.uniform(0.15, 0.5)
            else:
                t = t0 + (0.0 if i == 0 else rng.uniform(0.02, 0.22))
            pitch, m = rng.uniform(*f), rng.uniform(4, 12) + further
            gap = 1 / rng.uniform(*rate)
            for j in range(int(rng.integers(*run)) if not (kind == 1 and i == 0) else int(rng.integers(3, 5))):
                b.at(t, far(click(rng, pitch * rng.uniform(0.97, 1.03)), m), db + rng.uniform(-4, 0) - 1.5 * j)
                t += gap * rng.uniform(0.8, 1.2)
                gap *= rng.uniform(1.05, 1.25)
    return b.x


@recipe("tell-mourners", "clicks", "tongues",
        "A startled group clicking in alarm: small, dry tongue clicks from several throats, answering each other",
        """Synthesised: each click is a small tongue snapped off the palate (a sharp suck of pressure) and the little
        mouth ringing for a few milliseconds at 1.3-2.6 kHz, dry. Four to six Mourners click in short runs that slow as
        they trail off, each at its own pitch and distance. Three takes: the whole group at once; one alarm click and
        the rest answering, spreading out; a flurry, a pause, and a second flurry farther off as they scatter. Held to
        600 Hz-4 kHz.""",
        takes=3, band=CLICK, lufs=CLICK_LUFS)
def clicks_tongues(rng, k):
    return held(air(alarm(rng, k, tongue), rng, 0.1, 0.25), *CLICK, drive=3)


@recipe("tell-mourners", "clicks", "mandibles",
        "A startled group's mandibles clattering: pairs of hard little plates snapping shut, quick and dry",
        """Each click is a pair of mandibles snapping shut: two hard plates meeting a few milliseconds apart (chitin
        clicks at 1.8-3.4 kHz) over a real blade's dry tick on a board, pitched up to the size of a small jaw. Faster
        and harder than 'tongues': runs of three to seven at 14-24 a second, slowing, four to six of them answering one
        another. The same three takes (all at once; one alarm then the others; two flurries as they scatter). Held to
        600 Hz-4 kHz.""",
        sources=[CHOP], takes=3, band=CLICK, lufs=CLICK_LUFS)
def clicks_mandibles(rng, k):
    return held(air(alarm(rng, k, mandible, rate=(14, 24), run=(3, 8), f=(1800, 3400)), rng, 0.1, 0.25), *CLICK, drive=3)


# The drag: the body hauled off the line in tugs of half a metre, the group heaving together then catching its breath.

DRAG_L = 12.0


def gravel_grains(rng, length, rate, semis=(-4, 1), size=(0.008, 0.03)):
    """Real gravel cut into grains and strewn at `rate` a second (a constant or a curve): stones shoved and rolling."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        g = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(*size), rng.uniform(*semis))
        b.at(t, unit(g), 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return unit(dsp.fit(b.x, n))


def haul(rng, length, weight=1.0):
    """One tug: the dead weight dragged half a metre through gravel. Its speed swells and dies; the coat's cloth rasps on
    the stones (stick-slip friction, judders with the weave), gravel is shoved and rolls ahead of it, a boot heel
    furrows through; a dull jolt as the weight is taken, a soft settle as it stops."""
    v = env([(0, 0), (length * 0.3, 1), (length * 0.65, 0.7), (length, 0)], length) ** 1.3
    b = Bus(length + 0.4)
    coat = ck.friction(rng, length, rng.uniform(110, 160), 250, 3500, rough=0.7)
    b.at(0, coat * v, -4)
    b.at(0, lp(gravel_grains(rng, length, 30 + 220 * v, (-5, 0)), 6000) * v ** 0.7, -3)
    b.at(0, lp(gravel_grains(rng, length, 40 + 260 * v, (-11, -7), (0.02, 0.05)), 1200) * v, -3)   # the weight grinding them
    b.at(0, ck.grains(rng, int(rng.integers(5, 12)), length * 0.8, 1200, 4000) , -16)     # pebbles skittering ahead
    b.at(0.02, kit.thud(rng, rng.uniform(150, 190), 0.12, 0.6 * weight), -12)
    b.at(length * 0.95, ck.pad(rng, 0.09, 350), -10 + 6 * np.log10(weight))
    return dsp.fit(b.x, samples(length + 0.4))


def feet(rng, length, rate=26, f=(1500, 4000)):
    """Small feet scrabbling for purchase on cinders: quick light steps from several of them, a crunch of grit and a
    little pad under each, uneven."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.1)
    t = rng.exponential(1 / rc[0])
    while t < length:
        g = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(0.006, 0.014), rng.uniform(2, 6))
        b.at(t, unit(g), rng.uniform(-10, 0))
        b.at(t, ck.tick(rng, rng.uniform(*f), q=6, length=0.008), rng.uniform(-18, -10))
        b.at(t, ck.pad(rng, 0.03, 500), rng.uniform(-20, -12))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return b.x


def pant(rng, length, rate=5.0):
    """A small creature's breathless panting: quick huffs out and in through the small mouth, uneven."""
    b = Bus(length + 0.2)
    t, out = rng.uniform(0, 0.05), True
    while t < length:
        L = rng.uniform(0.05, 0.1) if out else rng.uniform(0.07, 0.12)
        sh = env([(0, 0), (L * (0.2 if out else 0.7), 1), (L, 0)], L)
        h = synth.breath(L, [(0, "h"), (L, "e" if out else "i")], MTRACT, rng, shape=sh)
        b.at(t, unit(bp(h, 700, 4500)), 0 if out else -4)
        t += L + rng.uniform(0.02, 0.06) + (1 / rate - 0.15) * rng.uniform(0.6, 1.4)
        out = not out
    return b.x


@recipe("cs-mourners-drag", "drag", "tugs",
        "The body hauled off in quick nervous tugs: a coat dragged over gravel, small feet scrabbling, panting keening",
        """Eight tugs in a 12 s cycle, every 1.5 s: small feet scrabble for purchase on the cinders (light, quick, several
        of them), then the dead weight lurches half a metre: a dull jolt as it's taken, the coat rasping on the stones
        (stick-slip friction that judders with the weave), real gravel shoved and rolling ahead of it and ground down
        under the weight (the same gravel dropped half an octave), a soft settle as it stops. Between tugs the group pants, breathless (quick huffs through small
        mouths), and keens under the effort in short, strained snatches: the same thin throats as the tell. A few metres
        off on open ground; seamless.""",
        sources=GRAVEL, takes=1, loop=True, seconds=DRAG_L)
def drag_tugs(rng, k):
    L = DRAG_L
    b = Bus(L + 3.0)
    period = L / 8
    for i in range(8):
        t = 0.3 + i * period + rng.uniform(-0.06, 0.06)
        H = rng.uniform(0.45, 0.65)
        b.at(t - 0.25, feet(rng, 0.25 + H * 0.7, env([(0, 15), (0.25, 40), (0.25 + H * 0.7, 10)], 0.25 + H * 0.7)), -8)
        b.at(t, haul(rng, H, rng.uniform(0.8, 1.2)), 0)
        b.at(t + H + 0.05, pant(rng, period - H - 0.35, rng.uniform(4, 6)), -15)
        if rng.random() < 0.6:          # a strained snatch of keening under the effort
            pl = rng.uniform(0.5, 0.9)
            f = PITCHES[int(rng.integers(len(PITCHES)))] * 2 ** (rng.uniform(1, 3) / 12)
            b.at(t + rng.uniform(-0.1, H), far(keen_phrase(rng, pl, f, rng.uniform(3, 6), [pl * 0.5], 0.8), 5), -10)
    return loop_of(b, L, rng, wet=0.12)


def coat(rng, length, semis=-4.0):
    """A coat dragged: real cloth handling cut into overlapping grains along the slide, dropped for a heavy wool coat."""
    b = Bus(length + 0.4)
    t = 0.0
    while t < length:
        g = grain(rng, CLOTH[int(rng.integers(len(CLOTH)))], rng.uniform(0.12, 0.3), semis + rng.uniform(-2, 1), attack=False)
        b.at(t, unit(g), rng.uniform(-8, 0))
        t += rng.uniform(0.04, 0.12)
    return unit(dsp.fit(b.x, samples(length)))


@functools.lru_cache(maxsize=1)
def gasp_core():
    """The breath in a real gasp: its first 0.2 s, without the voice."""
    return unit(hp(dsp.trim(dsp.trim_silence(ck.get(GASP), -30), 0.0, 0.2), 500))


def gasps(rng, length, rate=4.0):
    """Breathless panting from a real gasp, pitched up into small mouths, cut short and fired unevenly."""
    b = Bus(length + 0.3)
    t = rng.uniform(0, 0.08)
    while t < length:
        g = dsp.vari(gasp_core(), rng.uniform(5, 9))[:samples(rng.uniform(0.06, 0.12))]
        b.at(t, dsp.fade(g, 0.004, 0.03), rng.uniform(-6, 0))
        t += (1 / rate) * rng.uniform(0.6, 1.4)
    return b.x


@recipe("cs-mourners-drag", "drag", "heaves",
        "The body hauled off in slow double heaves: a real coat rasping over gravel, feet scrabbling, gasps and a thin keen",
        """Kitbashed, and a different performance from 'tugs': five hauls in a 12 s cycle, each a double heave (the group
        jerks it, then pulls it on half a metre, the slide nearly a second long). Real cloth cut into grains and dropped
        for a heavy coat, real gravel crunched and rolling under it and ground down by the weight, small feet scrabbling
        hard (real grit pitched up), a dull jolt with each heave and a soft settle. Between hauls they gasp (a real gasp
        pitched up into small mouths and cut into quick pants) and a thin keen rises and falls under the effort, made
        from the hinge squeals of the tell's 'hinges'. A few metres off on open ground; seamless.""",
        sources=CLOTH + GRAVEL + [GASP] + HINGES, takes=1, loop=True, seconds=DRAG_L)
def drag_heaves(rng, k):
    L = DRAG_L
    b = Bus(L + 3.0)
    period = L / 5
    for i in range(5):
        t = 0.35 + i * period + rng.uniform(-0.08, 0.08)
        jerk, H = rng.uniform(0.18, 0.25), rng.uniform(0.75, 1.0)
        b.at(t - 0.3, feet(rng, 0.3 + jerk + H * 0.8, rng.uniform(30, 45)), -6)
        for at, Lh, w in ((t, jerk, 0.7), (t + jerk + rng.uniform(0.08, 0.14), H, 1.1)):
            v = env([(0, 0), (Lh * 0.25, 1), (Lh * 0.7, 0.6), (Lh, 0)], Lh)
            b.at(at, coat(rng, Lh) * v, -3)
            b.at(at, lp(gravel_grains(rng, Lh, 40 + 260 * v, (-4, 1)), 6000) * v ** 0.7, -4)
            b.at(at, lp(gravel_grains(rng, Lh, 50 + 280 * v, (-11, -7), (0.02, 0.05)), 1200) * v, -3)
            b.at(at + 0.02, kit.thud(rng, rng.uniform(150, 190), 0.12, 0.6 * w), -12)
            b.at(at + Lh * 0.95, ck.pad(rng, 0.09, 350), -11)
        end = t + jerk + 0.1 + H
        b.at(end + 0.05, gasps(rng, period - jerk - H - 0.45, rng.uniform(3.5, 5)), -13)
        pl = rng.uniform(0.8, 1.3)
        f = PITCHES[int(rng.integers(len(PITCHES)))] * 2 ** (rng.uniform(1, 3) / 12)
        b.at(t + rng.uniform(0.0, H), far(hinge_phrase(rng, pl, f, rng.uniform(3, 6), [pl * 0.5]), 5), -11)
    return loop_of(b, L, rng, wet=0.12)


# ---- Tower Jaw: the gnaw (200 Hz-1.3 kHz), the tower's creak, groan and crack ----------------------------------------------

JAW = (200, 1300)
SHOT = (300, 5000)
GNAW_L = 12.0
LEG = [205, 430, 660, 950, 1280]      # a 30 cm timber leg bitten into: its local modes (Hz), low and dull


def fibres(rng, length, rate=1500, hi=1500):
    """Wood fibres crushing under a blow: a dense, dark crackle that dies as the bite stops."""
    c = synth.crackle(length, env([(0, rate), (length, rate * 0.05)], length), rng, size=(0.0003, 0.002), hi=150)
    return unit(lp(c, hi))


def bite(rng, depth=1.0):
    """One stroke of the incisors into the leg: two chisels the width of a hand driven in (the leg's dull knock, a real
    blade chopped into a board dropped well down), the fibres crushing, then the lower teeth gouging up through the
    grain (stick-slip friction held low, so it's a chisel, not a rasp)."""
    b = Bus(0.6)
    x = burst(rng, 0.0015, attack=0.1)
    b.at(0, unit(ring(x, [f * rng.uniform(0.95, 1.05) for f in LEG], q=rng.uniform(6, 9), tail=0.15,
                      gains=[1, 0.8, 0.6, 0.4, 0.3])), 0)
    b.at(0, unit(lp(dsp.vari(main_hit(CHOP, 0.2), rng.uniform(-10, -7)), 1800)), -3)
    b.at(0.002, fibres(rng, rng.uniform(0.06, 0.1)), -8)
    g = rng.uniform(0.1, 0.16) * depth
    sc = ck.friction(rng, g, rng.uniform(55, 85), 220, 1300, rough=0.5)
    b.at(0.05, sc * env([(0, 0), (g * 0.3, 1), (g, 0)], g), -9)
    return b.x


def chunk(rng):
    """A chunk prised off: the jaw wrenches (the fibres creaking as they hold), the chunk splits away with a real crack
    dropped to the leg's size, splinters with it; then it falls a metre and thuds onto the ground."""
    b = Bus(1.4)
    w = rng.uniform(0.18, 0.28)
    cr = synth.creak(w, env([(0, 30), (w, 110)], w), rng, body=[f * 0.9 for f in LEG], q=12, jitter=0.4)
    b.at(0, hp(cr, 60) * env([(0, 0), (w * 0.7, 1), (w, 0.6)], w), -10)
    b.at(w, unit(lp(rec(rng, CRACKS, rng.uniform(-9, -6), 0.25), 2500)), 0)
    b.at(w, splinter(rng, 0.2, env([(0, 140), (0.2, 10)], 0.2), (-9, -5)), -8)
    land = w + rng.uniform(0.38, 0.48)
    b.at(land, unit(lp(rec(rng, KNOCKS, rng.uniform(-6, -3), 0.2), 1500)), -6)
    b.at(land, ck.pad(rng, 0.08, 400), -8)
    return b.x


def runs(rng, length, beat, counts, extra):
    """Split a cycle into runs of bites: each run `counts[i]` bites `beat` apart plus `extra` s for its chunk, and the
    rest of the cycle shared out as pauses (no two alike), so the runs fill exactly one cycle and the seam keeps time."""
    used = sum(counts) * beat + len(counts) * extra
    pauses = rng.dirichlet(np.full(len(counts), 4.0)) * (length - used)
    t, out = 0.05, []
    for c, p in zip(counts, pauses):
        out.append((t, c))
        t += c * beat + extra + p
    return out


@recipe("tell-tower-jaw", "gnaw", "chisel",
        "Huge incisors chiselling into a thick timber leg in steady runs, a chunk prised off and thudding down after each",
        """A beaver's gnaw at a bear's size: each stroke drives two chisels the width of a hand into a 30 cm timber leg
        (the leg's dull, low knock; a real blade chopped into a board dropped most of an octave; the fibres crushing),
        then the lower teeth gouge up through the grain (friction held low, so it's a chisel, never a rasp). Runs of
        five to seven strokes at 2.7 a second; at the end of each the jaw wrenches, the fibres creak, and a chunk splits
        away (a real crack dropped to the leg's size, splinters) and thuds onto the ground a moment later. Three runs in
        a 12 s cycle, seamless. Deep and percussive, nothing like the Grumbler's high rasp. Held to 200 Hz-1.3 kHz.""",
        sources=[CHOP] + CRACKS + KNOCKS, takes=1, loop=True, band=JAW, lufs=-21, seconds=GNAW_L)
def gnaw_chisel(rng, k):
    L = GNAW_L
    b = Bus(L + 3.0)
    beat = 0.37
    for t, c in runs(rng, L, beat, [int(n) for n in rng.permutation([5, 6, 7])], 0.95):
        for i in range(c):
            b.at(t + i * beat + rng.uniform(-0.012, 0.012), bite(rng, rng.uniform(0.8, 1.1)),
                 rng.uniform(-2, 0) + (1.5 if i == 0 else 0))
        b.at(t + c * beat, chunk(rng), 0)
    return cycle(b.x, L, rng, JAW, drive=4, wet=0.15)


def blow(rng):
    """A heavier, slower stroke for 'adze': a real heavy blow on timber and a plank's knock, dropped and choked (the
    teeth stay in the wood), the fibres crushing, the jaw's mass behind it."""
    b = Bus(0.6)
    b.at(0, ck.choke(rec(rng, WOOD_HEAVY + DECK, rng.uniform(-4, -1), 0.4), 0.06, 0.05), 0)
    b.at(0.002, ck.choke(rec(rng, PLANK, rng.uniform(-3, 0), 0.3), 0.05, 0.05), -5)
    b.at(0, unit(lp(dsp.vari(main_hit(CHOP, 0.2), rng.uniform(-8, -5)), 2000)), -5)
    b.at(0.003, fibres(rng, rng.uniform(0.1, 0.14), 1200), -7)
    return b.x


def tear(rng, length):
    """The jaw worked side to side with the teeth sunk in: fibres tearing in a slow, creaking rip."""
    cr = synth.creak(length, env([(0, 20), (length * 0.6, 70), (length, 35)], length), rng,
                     body=[f * rng.uniform(0.8, 0.95) for f in LEG], q=10, jitter=0.5, grit=0.6)
    rip = splinter(rng, length, env([(0, 15), (length * 0.7, 60), (length, 5)], length), (-10, -6))
    return dsp.fit(mix(unit(hp(cr, 60)) * 0.8, rip * 0.5), samples(length)) * env([(0, 0), (length * 0.4, 1), (length, 0)], length)


@recipe("tell-tower-jaw", "gnaw", "adze",
        "Slower, heavier bites like an adze into the leg: each sunk in, worked side to side, a chunk torn off every other",
        """Kitbashed from real wood, a different performance from 'chisel': every 0.8 s a heavy bite (real heavy blows on
        timber and a plank's knock, dropped and choked because the teeth stay in, a real blade's chop under them, the
        fibres crushing), then the jaw worked side to side with the teeth sunk in (the fibres tearing in a slow,
        creaking rip), and every other bite a chunk splitting away (a real crack dropped down) and thudding onto the
        ground. Fifteen bites in a 12 s cycle, seamless. Deep and percussive. Held to 200 Hz-1.3 kHz.""",
        sources=WOOD_HEAVY + DECK + PLANK + [CHOP] + CRACKS + KNOCKS, takes=1, loop=True, band=JAW, lufs=-21,
        seconds=GNAW_L)
def gnaw_adze(rng, k):
    L = GNAW_L
    b = Bus(L + 3.0)
    beat = L / 15
    for i in range(15):
        t = 0.05 + i * beat + rng.uniform(-0.02, 0.02)
        b.at(t, blow(rng), rng.uniform(-2, 0))
        tl = rng.uniform(0.3, 0.42)
        b.at(t + 0.12, tear(rng, tl), -7)
        if i % 2 == 1:
            b.at(t + 0.12 + tl * 0.6, chunk(rng), -2)
    return cycle(b.x, L, rng, JAW, drive=4, wet=0.15)


TOWER = [150, 228, 335, 480, 690, 960, 1250]   # the tower's timbers, legs and cross-braces under load: their modes (Hz)


def joint(rng, length, rate, f=1.0, q=16, shape=None):
    """One joint of the tower creaking: stick-slip where a brace rubs a leg (`rate` slips a second, a constant or a
    curve), rung through the timbers' modes scaled by `f`; DC taken out."""
    body = [m * f * rng.uniform(0.94, 1.06) for m in TOWER]
    c = synth.creak(length, rate, rng, body=body, q=q, jitter=0.35, grit=0.4)
    c = hp(c, 80)
    return c * (synth.curve(shape, len(c)) if shape is not None else 1)


def grit_fall(rng, length=0.6):
    """Dust and splinters shaken loose, pattering down the leg."""
    return lp(ck.grains(rng, int(rng.integers(8, 16)), length, 500, 2500, length=(0.004, 0.012)), 2500)


@recipe("tell-tower-jaw", "creak", "timber",
        "The tower creaking on its weakened legs: big timbers rubbing under the load, slow and deep",
        """Modelled from what's moving: where a gnawed leg is weakest the braces rub against it as the tower sways,
        stick-slip friction (25-90 slips a second, slow, so it's a creak, not a squeal) rung through big timbers' modes
        at 150 Hz-1.25 kHz. Three takes: one long creak that rises and eases; a sway out and back, two creaks, the
        second lower; a creak that ends in a peg shifting with a small crack, dust and splinters pattering down. Held to
        200 Hz-1.3 kHz.""",
        takes=3, band=JAW)
def creak_timber(rng, k):
    b = Bus(3.0)
    kind = k % 3
    if kind == 0:
        L = rng.uniform(1.4, 1.9)
        b.at(0, joint(rng, L, env([(0, 25), (L * 0.6, 80), (L, 40)], L), 1.0,
                      shape=env([(0, 0), (L * 0.3, 0.8), (L * 0.6, 1), (L, 0)], L)), 0)
        b.at(L * 0.25, joint(rng, L * 0.6, env([(0, 40), (L * 0.6, 70)], L * 0.6), 1.5,
                             shape=env([(0, 0), (L * 0.3, 1), (L * 0.6, 0)], L * 0.6)), -9)
    elif kind == 1:
        t = 0.0
        for i, f in enumerate((1.0, 0.8)):
            L = rng.uniform(0.6, 0.85)
            b.at(t, joint(rng, L, env([(0, 30), (L * 0.5, 75), (L, 35)], L), f,
                          shape=env([(0, 0), (L * 0.4, 1), (L, 0)], L)), -i * 2)
            t += L + rng.uniform(0.25, 0.45)
    else:
        L = rng.uniform(0.9, 1.2)
        b.at(0, joint(rng, L, env([(0, 30), (L, 90)], L), 1.1, shape=env([(0, 0), (L * 0.5, 0.8), (L, 1)], L)), 0)
        b.at(L, unit(lp(rec(rng, CRACKS, rng.uniform(-7, -4), 0.12), 3000)), -6)
        b.at(L + 0.05, grit_fall(rng), -14)
    return held(air(b.x, rng, 0.15, 0.6), *JAW, drive=3)


@recipe("tell-tower-jaw", "creak", "doors",
        "The tower creaking, from real door creaks dropped an octave and more to the size of a tower's timbers",
        """Kitbashed: real door creaks (big, slow wood on a hinge) varispeeded down 10-14 semitones so a door's creak
        becomes a tower leg's, two at once a little apart in pitch and time (two joints going), the second softer. Three
        takes: one long; a sway out and back; one ending in a peg shifting with a small crack (a real crack dropped down)
        and dust pattering. Held to 200 Hz-1.3 kHz.""",
        sources=DOORS + CRACKS, takes=3, band=JAW)
def creak_doors(rng, k):
    b = Bus(4.0)
    kind = k % 3

    def door(semis, length):
        x = dsp.trim_silence(src.get(DOORS[int(rng.integers(len(DOORS)))]), -40)
        a = rng.uniform(0, max(0.0, len(x) / SR - 0.5))
        y = dsp.vari(dsp.trim(x, a, length / 2 ** (-semis / 12)), semis)
        return dsp.fade(unit(hp(y, 80)), 0.08, length * 0.35)

    if kind == 1:
        t = 0.0
        for i in range(2):
            L = rng.uniform(0.7, 0.9)
            b.at(t, door(rng.uniform(-12, -10) - 2 * i, L), -2 * i)
            t += L + rng.uniform(0.25, 0.45)
    else:
        L = rng.uniform(1.3, 1.8) if kind == 0 else rng.uniform(0.9, 1.2)
        b.at(0, door(rng.uniform(-13, -11), L), 0)
        b.at(rng.uniform(0.15, 0.35), door(rng.uniform(-11, -9), L * 0.7), -8)
        if kind == 2:
            b.at(L, unit(lp(rec(rng, CRACKS, rng.uniform(-7, -4), 0.12), 3000)), -5)
            b.at(L + 0.05, grit_fall(rng), -14)
    return held(air(b.x, rng, 0.15, 0.6), *JAW, drive=3)


GROAN_L = 10.0


@recipe("tell-tower-jaw", "groan", "fibres",
        "The tower's long deep groan as it goes: timbers straining in one long stick-slip moan, fibres popping",
        """Modelled from the last seconds: the gnawed leg taking more than it can, so the braces grind against it in one
        long groan (stick-slip at 60-150 slips a second, fast enough to moan, rung through the timbers' modes, sagging
        and recovering in two slow swells a cycle, louder as it sags), other joints creaking against it at their own pitches, and the leg's
        fibres popping one after another (real cracks cut small and dropped down), dust shaken loose. Ominous, deep and
        never still. A 10 s cycle, seamless. Held to 200 Hz-1.3 kHz.""",
        sources=CRACKS, takes=1, loop=True, band=JAW, lufs=-21, seconds=GROAN_L)
def groan_fibres(rng, k):
    L = GROAN_L
    n = samples(L)
    b = Bus(L + 3.0)
    t = np.arange(n) / SR
    swell = np.sin(2 * np.pi * t / L + rng.uniform(0, 6.28)) ** 2      # two swells a cycle, periodic in L
    rate = 70 + 70 * swell + wander(rng, n, 0.8, 12)
    g = joint(rng, L, rate, 0.85, q=14) * (0.25 + 0.75 * swell)
    b.at(0, unit(g), 0)
    for _ in range(5):          # other joints answering at their own pitches
        Lj = rng.uniform(0.6, 1.4)
        b.at(rng.uniform(0, L), joint(rng, Lj, env([(0, 25), (Lj * 0.6, 70), (Lj, 30)], Lj), rng.uniform(1.1, 1.6),
                                      shape=env([(0, 0), (Lj * 0.4, 1), (Lj, 0)], Lj)), rng.uniform(-12, -7))
    b.at(0, splinter(rng, L, 3.5, (-9, -5)), -9)        # fibres popping
    for at in rng.uniform(0, L, 3):
        b.at(at, grit_fall(rng, 0.8), -16)
    return cycle(b.x, L, rng, JAW, drive=1.5, wet=0.18)


@functools.lru_cache(maxsize=2)
def door_core(key):
    """A real door creak's body without its silence."""
    return unit(hp(dsp.trim_silence(ck.get(key), -40), 80))


@recipe("tell-tower-jaw", "groan", "strain",
        "The tower's groan from real door creaks held out and dropped deep, sagging, fibres popping, its iron straps straining",
        """Kitbashed: two real door creaks held out into one long groan each (paulstretched, so the creak's grain stays
        but its jerks go), dropped about an octave and bent so the pitch sags as the load comes on and
        recovers as it eases, twice a cycle, louder as it sags, laid together a little apart in pitch; the leg's fibres popping (real cracks cut small and dropped
        down), and the iron straps round the timbers straining (a slow stick-slip through a heavy iron part's modes,
        low under it all). A 10 s cycle, seamless. Held to 200 Hz-1.3 kHz.""",
        sources=DOORS + CRACKS, takes=1, loop=True, band=JAW, lufs=-21, seconds=GROAN_L)
def groan_strain(rng, k):
    L = GROAN_L
    n = samples(L)
    b = Bus(L + 3.0)
    ph = rng.uniform(0, 6.28)
    load = np.sin(2 * np.pi * np.arange(n) / n + ph) ** 2      # the strain coming and going, twice a cycle
    for i, key in enumerate(DOORS):
        x = door_core(key)
        a = rng.uniform(0, len(x) / SR - 0.3)
        semis = rng.uniform(-12, -10) + 2 * i
        seg = dsp.vari(dsp.trim(x, a, 0.3), semis)
        held_ = dsp.smear(seg, (L * 1.3 + 1.0) / (len(seg) / SR), 0.25, rng)
        sag = -1.5 * load + wander(rng, n, 0.6, 0.4)
        y = dsp.band(dsp.fit(bend(held_, sag), n), *JAW)     # flatten what the tell keeps, not what the band takes off
        e = lp(np.abs(y), 3)
        y = y / (e + 0.1 * np.median(e))      # its own swells flattened (the creak's quiet stretches too): the load sets the shape
        b.at(0, unit(y) * (0.25 + 0.75 * load), -3 * i)
    b.at(0, splinter(rng, L, 3.0, (-9, -5)), -8)
    iron = synth.creak(L, 40 + 20 * wander(rng, n, 0.5, 1.0) ** 2, rng, body=[f * 1.2 for f in synth.IRON], q=25)
    b.at(0, hp(iron, 150) * (0.3 + 0.7 * load), -14)
    return cycle(b.x, L, rng, JAW, drive=1.5, wet=0.18)


def echoes(x, rng, delays=((0.11, -11), (0.23, -16), (0.4, -21))):
    """A shot's report coming back off the stop's buildings: a few late copies, each darker and quieter."""
    b = Bus(len(x) / SR + 0.6)
    b.at(0, x, 0)
    for d, db in delays:
        b.at(d * rng.uniform(0.9, 1.1), lp(x, 2500 - 3000 * d), db)
    return b.x


@recipe("tell-tower-jaw", "crack", "shot",
        "The leg giving with a crack like a gunshot: real wood breaking, a whip's report, echoing off the stop",
        """Kitbashed: a real piece of wood breaking, cut to its sharpest crack and dropped a few semitones for a timber
        30 cm thick, on a real switch's whip-crack dropped half an octave (the report), with the leg's own dull boom and
        a short tearing run of fibres after it as the split runs up the grain. The report comes back off the stop's
        buildings in three late, darker echoes: that's what makes a crack read as a shot. Held to 300 Hz-5 kHz.""",
        sources=CRACKS + WHIP, takes=2, band=SHOT, lufs=-23)
def crack_shot(rng, k):
    b = Bus(1.4)
    cr = dsp.vari(main_hit(CRACKS[1], 0.2), rng.uniform(-4, -2))
    b.at(0, unit(cr) * env([(0, 1), (0.03, 0.35), (len(cr) / SR, 0.05)], len(cr) / SR, "exp")[:len(cr)], 0)
    b.at(0, unit(dsp.vari(main_hit(WHIP[k % 2], 0.08), rng.uniform(-7, -5))), -1)
    b.at(0, unit(hp(burst(rng, 0.0015, attack=0.05), 400)), -1)              # the report itself
    x = burst(rng, 0.002, attack=0.1)
    b.at(0, unit(ring(x, [f * 0.8 for f in LEG], q=5, tail=0.3)), -8)
    b.at(0.03, splinter(rng, 0.35, env([(0, 250), (0.35, 10)], 0.35), (-5, -1)), -14)
    y = echoes(dsp.trim_silence(b.x, -60), rng)
    return finish(dsp.band(air(y, rng, 0.1, 0.6), *SHOT), -23)


@recipe("tell-tower-jaw", "crack", "split",
        "The leg giving, modelled: fibres popping faster and faster, then one great split like a shot, echoing",
        """Modelled from how a timber fails: the last fibres go in a quick crescendo of pops (a fifth of a second,
        accelerating), then the leg breaks at once: one hard impulse through a 30 cm timber's bending modes (a free
        bar's, struck all together, ringing a moment), the air's crack on top, and the split tearing up the grain in a
        falling rip. Its report comes back off the stop's buildings, darker each time. Held to 300 Hz-5 kHz.""",
        takes=2, band=SHOT, lufs=-23)
def crack_split(rng, k):
    b = Bus(1.4)
    pre = rng.uniform(0.15, 0.22)
    pops = synth.crackle(pre, env([(0, 15), (pre, 220)], pre), rng, size=(0.0004, 0.002), hi=400)
    b.at(0, unit(lp(pops, 4000)) * env([(0, 0.3), (pre, 1)], pre), -10)
    x = burst(rng, 0.0012, attack=0.05)
    f1 = rng.uniform(170, 210)
    bar = ring(x, [f1 * m * rng.uniform(0.97, 1.03) for m in (1.0, 2.756, 5.404, 8.933, 13.34)], q=rng.uniform(12, 18),
               tail=0.4, gains=[1, 0.8, 0.6, 0.4, 0.25])
    b.at(pre, dsp.shaped(unit(bar), [(0, 1), (0.4, 0.01)], "exp"), 0)
    b.at(pre, unit(hp(burst(rng, 0.006, attack=0.05), 1500)), -2)
    rip = synth.crackle(0.4, env([(0, 3000), (0.4, 60)], 0.4), rng, size=(0.0003, 0.002), hi=500)
    b.at(pre + 0.01, unit(lp(rip, 5000)) * env([(0, 1), (0.4, 0.1)], 0.4)[:samples(0.4)], -9)
    y = echoes(dsp.trim_silence(b.x, -60), rng)
    return finish(dsp.band(air(y, rng, 0.1, 0.6), *SHOT), -23)


# ---- Tower Jaw's acts: the tail slap, the lunge, the tower's fall ----------------------------------------------------------

KNOCK_LUFS = -23.0     # all attack: a quieter integrated loudness leaves the impacts their peaks
PEAK = -1.5
GROUNDS = ["earth", "cinders", "mud"]
PADDLE = [175, 285, 410, 560, 760]     # the tail's flat paddle, 30 by 60 cm of scaled hide over gristle: its modes (Hz)


def dust(rng, length=0.3):
    """A puff of dry dust and grit thrown up off packed ground."""
    n = synth.noise(length, rng, "pink")
    y = bp(n, 400, 3000) * env([(0, 0), (0.01, 1), (length * 0.3, 0.35), (length, 0)], length)
    return unit(y)


def answer(rng, ground):
    """The ground taking a blow: packed earth (a dull thud and a puff of dust), cinders (grit sprayed, stones knocking),
    mud (a wet, sucking smack)."""
    b = Bus(0.8)
    b.at(0, ck.pad(rng, 0.12, 380), -2)
    if ground == "earth":
        b.at(0.004, dust(rng, rng.uniform(0.25, 0.35)), -8)
    elif ground == "cinders":
        b.at(0.002, lp(gravel_grains(rng, 0.25, env([(0, 600), (0.25, 20)], 0.25), (-3, 2)), 6000), -5)
        b.at(0.004, kit.gravel(rng, int(rng.integers(10, 18)), 0.08, 700, 3500, body=0), -10)
    else:
        b.at(0.002, kit.squelch(rng, 0.25, 300, 1200), -7)
        b.at(0.0, kit.slap(rng, 0.1, 1800, wet=0.6), -6)
    return b.x


def scales(rng):
    """The tail's scales clicking against each other as it lands flat."""
    return ck.grains(rng, int(rng.integers(6, 12)), 0.04, 1500, 4000, length=(0.004, 0.01))


@recipe("cs-tower-jaw", "tail-slap", "flat",
        "A heavy flat tail whacked down on the ground: a broad leathery smack, the ground thudding, grit or dust thrown",
        """Kitbashed: the tail swung up and over (its air), then brought down flat: a real book slapped down flat dropped
        four to seven semitones for a paddle 60 cm long, a real heavy leather drop and a cloth whack under it for the scaled
        hide, the scales clicking; the ground takes its weight (a dull low thud, never sub) and answers: packed earth
        and a puff of dust, cinders sprayed, or mud smacked. One ground a take. Out at a stop at night.""",
        sources=[SLAP, LEATHER] + CLOTH[:2] + GRAVEL, takes=3, lufs=KNOCK_LUFS)
def tail_slap_flat(rng, k):
    b = Bus(1.5)
    up = rng.uniform(0.2, 0.28)
    b.at(0, kit.whoosh(rng, up, 200, 1400, 0.85), -14)
    t0 = up
    b.at(t0, rec(rng, [SLAP], rng.uniform(-7, -4), 0.3), 0)
    b.at(t0, unit(hp(rec(rng, [LEATHER], rng.uniform(-3, -1), 0.3), 150)), -4)
    b.at(t0 + 0.002, unit(hp(rec(rng, CLOTH[:2], rng.uniform(-6, -3), 0.2), 200)), -8)
    b.at(t0 + 0.003, scales(rng), -18)
    b.at(t0 + 0.002, answer(rng, GROUNDS[k % 3]), -2)
    return finish(done(air(b.x, rng, 0.1, 0.5), 1.2, low=120), KNOCK_LUFS, PEAK)


def paddle(rng, weight=1.0):
    """The paddle meeting the ground flat: the air trapped under it punched out (a short pressure pulse), the hide's
    smack, and the flat of it ringing dull for a moment in its own modes."""
    b = Bus(0.4)
    w = samples(rng.uniform(0.003, 0.005))
    pulse = np.sin(np.pi * np.arange(w) / w).astype(np.float32)
    b.at(0, unit(hp(np.concatenate([pulse, -0.4 * pulse[::2]]), 120)), -3)
    b.at(0, kit.slap(rng, 0.09, 2600, wet=0.0), -2)
    x = burst(rng, 0.002, attack=0.1)
    b.at(0, unit(ring(x, [f * rng.uniform(0.95, 1.05) for f in PADDLE], q=rng.uniform(3, 5), tail=0.12)), -4)
    return b.x * weight


@recipe("cs-tower-jaw", "tail-slap", "paddle",
        "The tail slap modelled: a broad paddle punched flat onto the ground, base then tip, the ground thudding",
        """Modelled from a long flat paddle swung down: the air trapped under it punched out (a short pressure pulse), the
        hide's smack, the flat of it ringing dull in its own modes (175-760 Hz, broad); a long tail lands base first and
        its tip slaps a moment later, so each whack is a heavy flam. The scales click, the ground takes the weight (a dull
        thud, never sub) and answers: packed earth and dust, cinders sprayed, or mud. One ground a take. Out at a stop
        at night.""",
        takes=3, lufs=KNOCK_LUFS)
def tail_slap_paddle(rng, k):
    b = Bus(1.5)
    up = rng.uniform(0.2, 0.28)
    b.at(0, kit.whoosh(rng, up, 250, 1600, 0.85), -13)
    t0 = up
    b.at(t0, paddle(rng), 0)
    b.at(t0 + rng.uniform(0.025, 0.045), paddle(rng, 0.6), -2)
    b.at(t0 + 0.003, scales(rng), -16)
    b.at(t0 + 0.002, answer(rng, GROUNDS[k % 3]), 0)
    return finish(done(air(b.x, rng, 0.1, 0.5), 1.2, low=120), KNOCK_LUFS, PEAK)


JTRACT = 0.55      # a bear-sized rodent's vocal tract as a share of a child's (synth.TRACT)


def incisors(rng, hard=1.0):
    """Two pairs of chisel teeth the width of a hand snapping together: hard enamel clacks a few milliseconds apart,
    long teeth so they ring a little lower than a dog's, the jaw's knock under them."""
    b = Bus(0.2)
    for i in range(2):
        f = rng.uniform(1300, 1900)
        t = i * rng.uniform(0.003, 0.008)
        b.at(t, synth.click(f, q=rng.uniform(8, 12), length=0.03, rng=rng, body=[f, f * 1.47, f * 2.3]), -i * 3)
        b.at(t, unit(hp(burst(rng, 0.0006, attack=0.05), 1200)), -4 - i * 3)       # the enamel's hard edge
    x = burst(rng, 0.002, attack=0.1)
    b.at(0, unit(ring(x, [rng.uniform(320, 380), rng.uniform(640, 720)], q=6, tail=0.08)), -5)
    return unit(b.x) * hard


def paws(rng):
    """Two heavy forepaws landing as it lunges: claws digging in, the pads' weight, grit kicked."""
    b = Bus(0.4)
    for i in range(2):
        t = i * rng.uniform(0.03, 0.06)
        b.at(t, kit.gravel(rng, int(rng.integers(5, 10)), 0.05, 800, 3500, body=0), -10)
        b.at(t, ck.pad(rng, 0.08, 350), -4)
    return b.x


@recipe("cs-tower-jaw", "lunge", "snarl",
        "Its lunge: a hissing, rattling snarl through big teeth, the weight thrown forward, the incisors clacking shut",
        """Synthesised from a beaver's threat at a bear's size: a hissing snarl (a growl's rattle at 28-40 pulses a
        second through a big rodent's tract, rising as it comes, with the hiss of air forced past huge front teeth over
        it), the weight thrown forward (its air, two forepaws landing heavy in grit), then the bite: two pairs of chisel
        teeth the width of a hand clacking shut, enamel on enamel, the jaw knocking under them. One clean bite, one
        snap-snap, one long rising hiss into the bite. Out at a stop at night.""",
        takes=3, lufs=-21)
def lunge_snarl(rng, k):
    b = Bus(1.8)
    L = (0.55, 0.4, 0.85)[k % 3] * rng.uniform(0.9, 1.1)
    f0 = rng.uniform(105, 130)
    sh = [(0, 0), (0.04, 0.7), (L * 0.6, 1), (L * 0.9, 0.7), (L, 0.3)]
    v = throat(rng, [(0, f0), (L * 0.7, f0 * 1.25), (L, f0 * 1.15)], L, [(0, "h"), (L * 0.3, "a"), (L, "e")], sh,
               rasp=0.85, fry_rate=(28, 40), sub=0.5, hiss=0.7, fire=0.0, tract=JTRACT)
    b.at(0, v, 0)
    b.at(L - 0.18, kit.whoosh(rng, 0.22, 250, 1600, 0.7), -10)
    b.at(L - 0.02, paws(rng), -4)
    clacks = [L + 0.01] if k % 3 != 1 else [L + 0.01, L + rng.uniform(0.14, 0.18)]
    for i, t in enumerate(clacks):
        b.at(t, incisors(rng), 6 - 3 * i)
    b.at(clacks[-1] + 0.06, throat(rng, [(0, f0 * 0.9), (0.18, f0 * 0.75)], 0.18, [(0, "a"), (0.18, "h")],
                                   [(0, 0), (0.02, 1), (0.18, 0)], rasp=0.9, fry_rate=(26, 32), hiss=0.3, fire=0.0,
                                   tract=JTRACT), -9)
    return finish(done(air(b.x, rng, 0.1, 0.5), 1.6), -21, PEAK)


def roar_snarl(rng, L, semis):
    """A real roar's hardest stretch, dropped a little to a bear-sized rodent and broken by a growl's rattle, with a
    hiss of air through the teeth from a real gasp."""
    x = dsp.vari(dsp.trim(dsp.trim_silence(src.get(ROAR), -30), rng.uniform(0.08, 0.14), 0.25), semis)
    y = dsp.fit(dsp.stretch(x, (L + 0.05) / (len(x) / SR)), samples(L))      # held out to the snarl's length
    n = len(y)
    fr = fry(rng, env([(0, 28), (L, 40)], L), L, jitter=0.35, decay=0.012)[:n]
    y = y * (0.3 + 0.7 * fr)
    hiss = dsp.vari(gasp_core(), -3)
    hiss = dsp.fit(dsp.stretch(hiss, L / (len(hiss) / SR)), n)
    sh = env([(0, 0), (0.03, 0.8), (L * 0.6, 1), (L * 0.9, 0.7), (L, 0.3)], L)[:n]
    return dsp.saturate(unit(mix(unit(y), unit(bp(hiss, 2000, 7000)) * 0.6)) * sh, 6)


@recipe("cs-tower-jaw", "lunge", "roar",
        "The lunge kitbashed: a real roar broken into a rattling snarl with a hiss through the teeth, a hard wooden clack",
        """Kitbashed: the hardest quarter-second of a real big-cat roar, dropped a couple of semitones, held out to the
        snarl's length and broken by a growl's rattle (28-40 pulses a second) so it snarls rather than roars, a real gasp stretched under it for the air hissing past the front teeth;
        the weight thrown forward, two forepaws landing in grit; then the incisors clacking shut, a real blade's chop on a
        board for the enamel's hardness under two hard clicks and the jaw's knock. Kept low and close: no shriek. One
        clean bite, one snap-snap, one long snarl into the bite. Out at a stop at night.""",
        sources=[ROAR, GASP, CHOP], takes=3, lufs=-21)
def lunge_roar(rng, k):
    b = Bus(1.8)
    L = (0.55, 0.4, 0.85)[k % 3] * rng.uniform(0.9, 1.1)
    b.at(0, roar_snarl(rng, L, rng.uniform(-3, -1)), 0)
    b.at(L - 0.18, kit.whoosh(rng, 0.22, 250, 1600, 0.7), -10)
    b.at(L - 0.02, paws(rng), -4)
    clacks = [L + 0.01] if k % 3 != 1 else [L + 0.01, L + rng.uniform(0.14, 0.18)]
    for i, t in enumerate(clacks):
        b.at(t, incisors(rng), 5 - 3 * i)
        b.at(t, unit(dsp.vari(main_hit(CHOP, 0.08), rng.uniform(-4, -1))), 1 - 3 * i)
    return finish(done(air(b.x, rng, 0.1, 0.5), 1.6), -21, PEAK)


FALL_L = 3.6
HOOP = [1.0, 2.83, 5.42, 8.9]          # an iron hoop's ring modes (in-plane bending), over its first
RAIL = [430, 1180, 2280, 3700]         # a steel rail struck: its modes (Hz)


def water(rng, key, length, semis, start=None):
    """A stretch of a real water recording, varispeeded, from `start` s (or anywhere in it)."""
    x = src.get(key)
    need = length * 2 ** (semis / 12)
    a = start if start is not None else rng.uniform(0, max(0.0, len(x) / SR - need - 0.1))
    return unit(dsp.fit(dsp.vari(dsp.trim(x, a, need), semis), samples(length)))


def deluge(rng, length):
    """The tank's water let go all at once: a slap of it hitting the ground, then a mass of it rushing out and
    spreading, thinning to a run and drips."""
    b = Bus(length + 0.2)
    sh = env([(0, 0), (0.02, 1), (0.4, 0.6), (length * 0.5, 0.2), (length, 0)], length)
    b.at(0, hp(water(rng, SPLASH, length, -2), 150) * sh, 0)
    b.at(0, hp(water(rng, SEA, length, -3), 150) * env([(0, 0), (0.05, 1), (length * 0.6, 0.3), (length, 0)], length), -3)
    b.at(length * 0.35, hp(water(rng, TRICKLE, length * 0.65, 0), 300)
         * env([(0, 0), (0.3, 1), (length * 0.65, 0)], length * 0.65), -10)
    b.at(0, bp(synth.noise(0.3, rng, "pink"), 300, 6000) * env([(0, 1), (0.3, 0)], 0.3, "exp"), -4)    # the slap of it
    return b.x


def debris(rng, t0, length, rail=0.4):
    """Planks scattering after the crash: knocks thinning out, some landing on a rail (wood on steel, a short ring),
    a few bouncing once."""
    b = Bus(t0 + length + 0.5)
    t = t0
    gap = 0.04
    while t < t0 + length:
        b.at(t, unit(lp(rec(rng, KNOCKS + PLANK, rng.uniform(-5, 1), 0.2), 3500)), rng.uniform(-14, -4) - 6 * (t - t0))
        if rng.random() < rail:
            b.at(t + 0.002, ck.choke(rec(rng, IRON, rng.uniform(-4, -1), 0.4), 0.05, 0.12), rng.uniform(-18, -10) - 5 * (t - t0))
        if rng.random() < 0.3:
            b.at(t + rng.uniform(0.08, 0.14), unit(lp(rec(rng, KNOCKS, rng.uniform(-2, 2), 0.12), 3500)), -16 - 6 * (t - t0))
        t += gap * rng.uniform(0.6, 1.6)
        gap *= rng.uniform(1.15, 1.35)
    return b.x


@recipe("cs-tower-jaw", "fall", "crash",
        "A wooden water tower crashing across the line: legs splitting, timbers smashing, the tank bursting, planks on rails",
        """Kitbashed from real wood, iron and water: the last leg splits (a real crack) and the whole tower groans over
        (a real door creak held and dropped an octave, fibres popping, its mass shoving the air), then it comes down:
        real heavy blows on timber and door slams dropped and piled into one crash, beams cracking, splinters spraying,
        the tank's staves bursting (plank knocks), its iron hoops springing loose and ringing; the tank's water let go
        at once (real splashing and rushing water, slowed a little, thinning to a trickle); planks scattering across
        the line, some landing on the rails with a short ring of steel. 3.5 s, out at a stop.""",
        sources=CRACKS + DOORS + WOOD_HEAVY + DECK + SLAMS + PLANK + KNOCKS + IRON + [SPLASH, SEA, TRICKLE],
        takes=2, lufs=-20)
def fall_crash(rng, k):
    b = Bus(FALL_L + 1.0)
    tip = rng.uniform(0.75, 0.95)
    b.at(0, unit(dsp.vari(main_hit(CRACKS[k % 2], 0.25), rng.uniform(-5, -3))), -3)
    x = door_core(DOORS[k % 2])
    g = dsp.vari(dsp.trim(x, rng.uniform(0.0, 0.3), 0.45), rng.uniform(-11, -9))
    g = dsp.fit(dsp.stretch(g, (tip + 0.1) / (len(g) / SR)), samples(tip + 0.1))
    b.at(0.05, unit(g) * env([(0, 0), (tip * 0.5, 0.8), (tip, 1), (tip + 0.1, 0)], tip + 0.1), -3)
    b.at(0.05, splinter(rng, tip, env([(0, 8), (tip, 60)], tip), (-6, -2)), -12)
    b.at(tip * 0.35, kit.whoosh(rng, tip * 0.65 + 0.05, 150, 900, 0.95), -9)
    t0 = tip
    for i in range(5):                                   # the crash: timbers and boards piling down in a flam
        b.at(t0 + i * rng.uniform(0.015, 0.05), rec(rng, WOOD_HEAVY + DECK + SLAMS, rng.uniform(-6, -2), 0.5), -i * 1.5)
    b.at(t0, kit.thud(rng, rng.uniform(120, 150), 0.3, 1.0), -6)
    for i in range(2):
        b.at(t0 + rng.uniform(0.0, 0.12), unit(dsp.vari(main_hit(CRACKS[i], 0.3), rng.uniform(-4, -1))), -2 - 2 * i)
    b.at(t0, splinter(rng, 0.7, env([(0, 400), (0.7, 20)], 0.7), (-4, 2)), -5)
    for i in range(int(rng.integers(6, 10))):            # the tank's staves bursting apart
        b.at(t0 + rng.uniform(0.02, 0.3), unit(lp(rec(rng, PLANK + KNOCKS, rng.uniform(-4, 0), 0.25), 3000)),
             rng.uniform(-12, -5))
    b.at(t0 + 0.04, rec(rng, IRON, rng.uniform(-6, -4), 0.6), -9)        # the iron hoops springing loose
    b.at(t0 + rng.uniform(0.3, 0.5), rec(rng, IRON, rng.uniform(-3, -1), 0.6), -15)
    b.at(t0 + 0.02, deluge(rng, FALL_L - t0), -6)
    b.at(0, debris(rng, t0 + 0.15, FALL_L - t0 - 0.6), 3)
    return finish(done(air(b.x, rng, 0.12, 0.6), FALL_L, 0.3, low=110), -20, PEAK)


def struck(rng, modes, q, tail, hit=0.002, gains=None):
    """A body struck once: a short burst rung through its modes."""
    return unit(ring(burst(rng, hit, attack=0.1), modes, q=q, tail=tail, gains=gains))


@recipe("cs-tower-jaw", "fall", "timbers",
        "The water tower's fall modelled: a groan over, then beams, staves and hoops struck all at once, water, planks on rails",
        """Modelled from what falls: the tower groans over (the braces' stick-slip quickening into a moan as it tips,
        fibres giving faster and faster, its mass shoving the air), then the crash: the big timbers struck all at once
        (their bending modes, heavy and dull), beams snapping (hard impulses through a bar's modes), the tank's staves
        bursting (a scatter of short plank knocks), its iron hoops springing loose (a hoop's ring, wobbling as it rolls);
        the water let go (a broad rush full of bubbles, thinning to drips); planks scattering across the line, some
        ringing on the rails (a rail's steel modes). Drier and clearer than 'crash'. 3.5 s, out at a stop.""",
        takes=2, lufs=-20)
def fall_timbers(rng, k):
    b = Bus(FALL_L + 1.0)
    tip = rng.uniform(0.75, 0.95)
    b.at(0, joint(rng, tip, env([(0, 25), (tip, 170)], tip), 0.8, shape=env([(0, 0.3), (tip, 1)], tip)), -4)
    b.at(0, unit(lp(synth.crackle(tip, env([(0, 10), (tip, 300)], tip), rng, size=(0.0004, 0.002), hi=300), 4000)), -12)
    b.at(tip * 0.35, kit.whoosh(rng, tip * 0.65 + 0.05, 150, 900, 0.95), -8)
    t0 = tip
    for i in range(4):          # the big timbers coming down one on another
        f = rng.uniform(0.75, 1.0)
        b.at(t0 + i * rng.uniform(0.02, 0.06), struck(rng, [m * f for m in TOWER], 6, 0.4, 0.004), -i * 2)
    for i in range(3):          # beams snapping
        f1 = rng.uniform(150, 230)
        b.at(t0 + rng.uniform(0.0, 0.15), struck(rng, [f1 * m for m in (1.0, 2.756, 5.404, 8.933)], 14, 0.3, 0.001), -3 - i)
        b.at(t0 + rng.uniform(0.0, 0.15), unit(hp(burst(rng, 0.004, attack=0.05), 1200)), -6)
    b.at(t0, unit(lp(synth.crackle(0.6, env([(0, 3000), (0.6, 50)], 0.6), rng, size=(0.0003, 0.002), hi=400), 6000)), -6)
    for i in range(int(rng.integers(6, 10))):            # the staves
        f1 = rng.uniform(250, 420)
        b.at(t0 + rng.uniform(0.02, 0.3), struck(rng, [f1 * m for m in (1.0, 2.756, 5.404)], 10, 0.12), rng.uniform(-14, -7))
    hf = rng.uniform(140, 190)
    hoop = struck(rng, [hf * m for m in HOOP], 60, 1.2, gains=[1, 0.7, 0.5, 0.3])
    b.at(t0 + 0.05, hoop * (1 - 0.4 * (0.5 + 0.5 * np.sin(2 * np.pi * 6 * np.arange(len(hoop)) / SR))), -10)
    L = FALL_L - t0
    wet = mix(bp(synth.noise(L, rng, "pink"), 200, 5000) * env([(0, 0), (0.02, 1), (0.5, 0.4), (L, 0)], L),
              synth.bubbles(L, env([(0, 900), (0.6, 300), (L, 15)], L), 300, 3000, rng, rise=(0.05, 0.3)) * 0.7)
    b.at(t0 + 0.02, unit(hp(wet, 150)), -4)
    t, gap = t0 + 0.15, 0.04
    while t < FALL_L - 0.4:     # planks scattering, some on the rails
        b.at(t, struck(rng, [rng.uniform(300, 520) * m for m in (1.0, 2.756, 5.404)], 9, 0.1), rng.uniform(-14, -5) - 6 * (t - t0))
        if rng.random() < 0.4:
            b.at(t, struck(rng, [f * rng.uniform(0.97, 1.03) for f in RAIL], 70, 0.5), rng.uniform(-22, -15) - 5 * (t - t0))
        t += gap * rng.uniform(0.6, 1.6)
        gap *= rng.uniform(1.15, 1.35)
    return finish(done(air(b.x, rng, 0.12, 0.6), FALL_L, 0.3, low=110), -20, PEAK)


# ---- The Freight Beetle: the push (a crate shoved in surges with its strides), the startle -----------------------------------

PUSH_L = 12.0
STEP = 0.5                                # one tripod of legs every half second: 24 a cycle
CHITIN = [640, 1080, 1720, 2550, 3800]    # the leg joints' and the pronotum's shell plates: their modes (Hz)
CRATE = [210, 470, 820, 1330, 2050]       # a freight crate's slats and frame: their modes (Hz)
BOARDS = [140, 310, 560, 905, 1480]       # a platform's planks over the void under them: their modes (Hz)


def tripod(rng, ground, weight=1.0):
    """Three of its six feet coming down together: hooked chitin tips striking a few milliseconds apart, the weight
    of a pony on them, and the ground answering (grit, or a board's knock)."""
    b = Bus(0.3)
    for i in range(3):
        t = rng.uniform(0, 0.025)
        b.at(t, synth.click(rng.uniform(1300, 2600), q=rng.uniform(5, 8), length=0.012, rng=rng), rng.uniform(-15, -8))
        if ground == "gravel":
            b.at(t, unit(grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(0.01, 0.025),
                               rng.uniform(-3, 1))), rng.uniform(-10, -4))
        else:
            b.at(t, ck.choke(unit(lp(rec(rng, KNOCKS, rng.uniform(2, 5), 0.1), 3000)), 0.02, 0.02), rng.uniform(-14, -8))
    b.at(0.01, ck.pad(rng, 0.07, 400), -6 + 6 * np.log10(weight))
    return b.x


def chitin_creak(rng, length):
    """Its shell under the load: plates and leg joints creaking against each other, stick-slip through hard chitin
    modes, quick and dry."""
    body = [f * rng.uniform(0.9, 1.1) for f in CHITIN]
    c = synth.creak(length, env([(0, 90), (length * 0.5, 200), (length, 120)], length), rng, body=body, q=14,
                    jitter=0.3, grit=0.3)
    return hp(c, 300) * env([(0, 0), (length * 0.3, 1), (length, 0)], length)


def shove(rng, length, ground):
    """The crate lurching on a few inches: its edge scraping (stick-slip friction on the ground: gravel ploughed and
    rolling, or wood juddering on wood through the planks' modes), the crate's own joints creaking now and then."""
    v = env([(0, 0), (length * 0.25, 1), (length * 0.6, 0.6), (length, 0)], length) ** 1.2
    b = Bus(length + 0.2)
    if ground == "gravel":
        b.at(0, ck.friction(rng, length, rng.uniform(140, 200), 200, 3000, rough=0.7) * v, -5)
        b.at(0, lp(gravel_grains(rng, length, 30 + 250 * v, (-4, 1)), 6000) * v ** 0.7, -3)
        b.at(0, lp(gravel_grains(rng, length, 40 + 250 * v, (-10, -6), (0.02, 0.05)), 1200) * v, -4)
    else:
        sc = ck.friction(rng, length, rng.uniform(45, 75), 150, 2500, rough=0.45)
        sc = mix(sc, unit(dsp.resonate(sc, [f * rng.uniform(0.95, 1.05) for f in BOARDS], q=10)) * 0.8)
        b.at(0, unit(sc) * v, -2)
        if rng.random() < 0.25:     # a nail head catching the crate's runner
            L = rng.uniform(0.06, 0.1)
            sq = synth.creak(L, rng.uniform(500, 800), rng, body=[1200, 1900, 2900], q=12, jitter=0.15)
            b.at(length * rng.uniform(0.2, 0.5), hp(sq, 300) * env([(0, 0), (0.01, 1), (L, 0)], L), -12)
    if rng.random() < 0.4:
        c = synth.creak(length * 0.6, rng.uniform(30, 60), rng, body=[f * rng.uniform(0.95, 1.05) for f in CRATE], q=16)
        b.at(length * 0.1, hp(c, 120) * env([(0, 0), (length * 0.3, 1), (length * 0.6, 0)], length * 0.6), -12)
    return b.x


def push(rng, ground):
    """A cycle of the push: a tripod of legs every half second, each driving the crate on in a surge as it lands, the
    shell creaking on the hardest shoves, the crate's load knocking about inside it now and then."""
    L = PUSH_L
    b = Bus(L + 2.0)
    for i in range(int(L / STEP)):
        t = 0.05 + i * STEP + rng.uniform(-0.03, 0.03)
        w = rng.uniform(0.85, 1.0) * (1.1 if i % 2 == 0 else 0.95)     # its two tripods not quite alike
        b.at(t, tripod(rng, ground, w), 0)
        H = rng.uniform(0.26, 0.36)
        b.at(t + 0.03, shove(rng, H, ground), rng.uniform(-2, 0) + 3 * np.log10(w))
        if rng.random() < 0.35:
            b.at(t + 0.05, chitin_creak(rng, rng.uniform(0.15, 0.25)), rng.uniform(-14, -9))
        if rng.random() < 0.15:     # what's in the crate shifting
            b.at(t + H * 0.8, ck.choke(unit(lp(rec(rng, KNOCKS, rng.uniform(-2, 2), 0.15), 2500)), 0.03, 0.03), -16)
    return b


@recipe("cs-freight-beetle", "push", "gravel",
        "A crate shoved over gravel in surges, one with each tripod of a big beetle's legs, its shell creaking",
        """A beetle the size of a pony walking a crate along: every half second three of its feet come down together
        (hooked chitin tips striking, a pony's weight, grit crunching under them; the two tripods not quite alike) and
        the crate lurches on a few inches: its edge ploughing the gravel (stick-slip friction, real gravel shoved and
        rolling ahead of it and ground down under the weight), the crate's joints creaking now and then and what's in it
        knocking about. On the hardest shoves its shell creaks (plates and leg joints rubbing, a dry stick-slip through
        hard chitin modes). 24 steps in a 12 s cycle, seamless, at a facility in the open.""",
        sources=GRAVEL + KNOCKS, takes=1, loop=True, seconds=PUSH_L, mat="ground")
def push_gravel(rng, k):
    return loop_of(push(rng, "gravel"), PUSH_L, rng, wet=0.1)


@recipe("cs-freight-beetle", "push", "boards",
        "A crate shoved over a platform's boards in surges with a big beetle's strides: wood juddering on wood, its shell creaking",
        """The same walk on a loading platform: every half second a tripod of chitin feet on the planks (hard tips
        striking, a small real wood knock under each, the weight), and the crate lurches on: its runners juddering over
        the boards (wood on wood, stick-slip at 45-75 slips a second rung through the planks over the void under them),
        a nail head catching now and then in a short squeak, the crate's joints creaking, its load knocking. The shell
        creaks on the hardest shoves. 24 steps in a 12 s cycle, seamless.""",
        sources=KNOCKS, takes=1, loop=True, mat="wood", seconds=PUSH_L)
def push_boards(rng, k):
    return loop_of(push(rng, "boards"), PUSH_L, rng, wet=0.1)


def plates(rng, length, rate, f=(700, 2200)):
    """Its shell plates clattering: the wing cases and the plates of its back knocking edge on edge, bigger and duller
    than a claw's tick (`rate` a second, a constant or a curve)."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        pf = rng.uniform(*f)
        b.at(t, synth.click(pf, q=rng.uniform(4, 7), length=rng.uniform(0.015, 0.03), rng=rng,
                            body=[pf, pf * rng.uniform(1.4, 1.7), pf * rng.uniform(2.2, 2.7)]),
             20 * np.log10(rng.uniform(0.2, 1.0) ** 1.3))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    return unit(dsp.fit(b.x, n))


def spiracles(rng, length, pumps):
    """A beetle's hiss: air driven out through the spiracles along its sides, many small tubes at once (a broad hiss
    with a few soft resonances in it), the abdomen pumping it out in `pumps` pushes, each sharp at the start and
    trailing off."""
    n = samples(length)
    air = synth.noise(length, rng)
    tubes = dsp.resonate(air, [rng.uniform(2600, 3000), rng.uniform(3900, 4400), rng.uniform(5400, 6000)], q=4)
    y = mix(unit(bp(air, 1500, 9000)), unit(tubes) * 0.7)
    e = np.zeros(n, np.float32)
    t = 0.0
    for i in range(pumps):
        L = length / pumps * rng.uniform(0.85, 1.1) * (1.3 if i == pumps - 1 else 1.0)
        seg = env([(0, 0), (0.012, 1), (L * 0.35, 0.6), (L, 0)], L) * (0.7 ** i)
        a = samples(t)
        m = min(len(seg), n - a)
        if m > 0:
            e[a:a + m] = np.maximum(e[a:a + m], seg[:m])
        t += L * rng.uniform(0.7, 0.85)
    flutter = 0.75 + 0.25 * fry(rng, rng.uniform(22, 32), length, jitter=0.4, decay=0.01)
    return unit(y * e * flutter)


def scuttle(rng, length=0.22):
    """Its six feet scrabbling back a step as it startles: hooked tips ticking on the ground, fast and uneven."""
    return synth.skitter(length, rng.uniform(70, 110), rng, f=(1300, 3000), q=(5, 9), legs=6,
                         shape=env([(0, 1), (length, 0.2)], length))


@recipe("cs-freight-beetle", "startle", "plates",
        "Startled: a clatter of shell plates as the wing cases flare, its feet scrabbling back, a hiss from its sides",
        """Modelled: the wing cases snap up off its back (a stiff double clack) and its plates clatter edge on edge
        (many dull chitin knocks at 0.7-2.2 kHz, thick at first and thinning), its six feet scrabble back a step on hooked
        tips; then it hisses, air driven out through the spiracles along its sides (a broad hiss with a few soft tube
        resonances in it, a rough flutter), pumped out in two or three pushes like a hissing cockroach's, the last one
        longest. One short, one with a longer hiss.""",
        takes=2, lufs=-21)
def startle_plates(rng, k):
    b = Bus(2.0)
    for i in range(2):
        b.at(i * rng.uniform(0.006, 0.012), synth.click(rng.uniform(900, 1300), q=6, length=0.03, rng=rng), -i * 3)
    cl = rng.uniform(0.25, 0.35)
    b.at(0.005, plates(rng, cl, env([(0, 400), (cl * 0.5, 120), (cl, 20)], cl)), 2)
    b.at(0.02, scuttle(rng), -8)
    hl = (0.7, 1.1)[k % 2] * rng.uniform(0.9, 1.1)
    b.at(cl * rng.uniform(0.55, 0.7), lp(spiracles(rng, hl, 2 + k % 2), 8000), -8)
    return finish(done(air(b.x, rng, 0.08, 0.4), low=150), -21, PEAK)


@functools.lru_cache(maxsize=1)
def riffle():
    """A real book's pages riffled: the run of leaves flicking past, without the silence round it."""
    return unit(dsp.trim_silence(ck.get(RIFFLE), -30))


@recipe("cs-freight-beetle", "startle", "riffle",
        "Startled, kitbashed: stiff plates rattling like riffled pages dropped down, a real breath torn into a hiss",
        """Kitbashed: a real book's pages riffled, dropped half an octave and more so the leaves become shell plates
        flicking stiffly past each other as the wing cases flare, with real light wood knocks cut small and pitched up
        strewn through it, thick then thinning, for the plates knocking edge on edge; its feet scrabble back; then a hiss made from a real gasp's breath, stretched and pitched down, rung
        through the spiracles' small tubes and pumped out in two or three pushes. One short, one with a longer hiss.""",
        sources=[RIFFLE, GASP] + src.K("impactWood_light"), takes=2, lufs=-21)
def startle_riffle(rng, k):
    b = Bus(2.0)
    b.at(0, unit(lp(rec(rng, src.K("impactWood_light"), rng.uniform(3, 6), 0.06), 5000)), -2)
    b.at(rng.uniform(0.008, 0.015), unit(lp(rec(rng, src.K("impactWood_light"), rng.uniform(2, 5), 0.06), 5000)), -6)
    r = dsp.vari(riffle(), rng.uniform(-9, -6))
    b.at(0.01, unit(hp(r, 300)) * env([(0, 1), (len(r) / SR, 0.1)], len(r) / SR, "exp")[:len(r)], -1)
    cl = rng.uniform(0.25, 0.35)
    t = 0.0
    while t < cl:               # the plates knocking edge on edge through it: real light knocks cut small, pitched up
        g = ck.choke(rec(rng, src.K("impactWood_light"), rng.uniform(4, 9), 0.03), 0.008, 0.008)
        b.at(0.01 + t, unit(bp(g, 400, 6000)), 20 * np.log10(rng.uniform(0.2, 1.0) ** 1.3) - 1)
        t += rng.exponential(1 / (300 * (1 - t / cl) + 20))
    b.at(0.02, scuttle(rng), -8)
    hl = (0.7, 1.1)[k % 2] * rng.uniform(0.9, 1.1)
    g = dsp.vari(gasp_core(), rng.uniform(-5, -3))
    g = dsp.fit(dsp.stretch(g, hl / (len(g) / SR)), samples(hl))
    g = mix(unit(g), unit(dsp.resonate(g, [2800, 4200, 5700], q=4)) * 0.6)
    pumps = spiracles(rng, hl, 2 + k % 2)
    shape = lp(np.abs(pumps), 30)
    b.at(rng.uniform(0.2, 0.26), lp(unit(dsp.fit(g, len(shape)) * shape / (np.max(shape) + 1e-9)), 8000), -8)
    return finish(done(air(b.x, rng, 0.08, 0.4), low=150), -21, PEAK)
