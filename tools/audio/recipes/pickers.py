"""The Pickers heard (queue #501, note 771; G1's #315, note 592): the yard's drain scavengers, as their design doc's §4 asks
(docs/design/creatures/pickers.md, "a request to the audio chat"; src/DarkTerritory.Sim/Enemies/Pickers.cs).

What they are decides the build. Knee-high, lean and quick, a starved hare's hunched body on short hind legs, long arms
ending in big pale many-jointed hands, a small eyeless head that's mostly mouth and needle teeth. A while after a train
stands at a yard, three to eight come up out of the drains at the foot of the sheds and race the crew for the loose crates,
each running one back down its drain over its head (a heavy one takes two). Come right up to one and it bites (a hurt,
never a kill) and drops it; a blow kills one, and the rest near it bolt squealing for their drains. Small, quick and many,
a little comic but wrong. So:
- Their voices are small throats and teeth: a chitter is a run of quick, dry syllables (15-26 a second, each a throat's
  pulse a couple of hundredths long, its teeth meeting on the front of it), several of them at their own pitches answering
  one another. A squeal is hoarse and falls away, voices a hair apart so they beat: never a cartoon rat's clean 'eek'.
  Nothing wet anywhere (that's the Ribbits' hop and gulp), no growl, no embers and no big dog's chest (the Cinder Hounds'),
  and none of the Mourners' slow keening or their single clicks.
- Their feet are many, small and light on gravel and cinders: four to a stride, bunched fore and hind like a hare's bound,
  three strides a second, little stones ticking away after them. No heel, no sole, nothing under 600 Hz: never boots.
- Their things are the yard's: a cast-iron drain grating shoved over its stone kerb and dropped with a clank, rattling in
  its frame as one squeezes back down; a slatted crate knocked and dragged over the gravel; the drain's pipe under the
  ground, hollow.

The tell (emerge, feet, chitter) is heard across a yard over a standing train's idle, so it's held to 600 Hz-7 kHz, above
the engine's low beat. The carry sits in the mid band (300 Hz-5 kHz) under it. Every take keeps its weight out of the
squish under 150 Hz (the director's rule): the crate's knocks and the pipe's hollow are high-passed, nothing is a boom.

Each cue has two candidates. The first is kitbashed from the packs' real recordings: a real metal scrape dropped for
cast iron and real stones grinding under it, a real loose-metal rattle and real latches dropping, a real blow on steel;
real throats (the packs' wail, held out and pulled up to a small creature's pitch, and real hinge squeals for the
squeal's edge), real breath (the gasp), real ticks of a blade and of wood and bone cracking cut down into teeth, real
gravel cut into small feet, real wood knocks for the crate and real cracks and punches for the blow. The second is
modelled: a glottis through a small tract for the throats, struck modes for the iron, the crate and the pipe, stick-slip
friction for the scrapes and clicks and grains for the teeth and the stones. Loops are built periodic (events placed
round the cycle, the room and the band hold run round it), so build.py's seam gives the cycle back exactly.
"""

import functools

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.beasts import fry, main_hit, unit
from recipes.boarders import grain
from recipes.crew_items import hit_of
from recipes.g1_ground import air, chop_tick, far, gravel_grains, splinters, struck, wander
from recipes.horrors import finish
from recipes.house_beasts import rattle
from recipes.house_folk import drawn, held_cry
from recipes.moose_tells import burst, cycle, held

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

SCRAPE = "sfx_100_v2:misc_10"                    # a real metal scrape in a few strokes: the grating dragged
GRIT = "sfx_100_v2:stones_03"                    # real stones scraping and crackling: the kerb under it, little stones
GRAVEL = S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02")   # real stones and gravel steps
RATTLE = S("metal_03")                           # a real loose-metal rattle: five knocks in a seventh of a second
LATCHES = S("door_02", "door_04", "door_05")     # real latches dropping: iron dropped into iron
CLANK = "sfx_100_v2:metal_hit_01"                # a real blow on a steel sheet
RING = K("impactMetal_medium")                   # medium iron struck: a short ring, 600 Hz-2 kHz
DRAG = R("drawKnife1", "drawKnife2")             # something drawn fast across a surface: claws on iron
WAIL = "sfx_100_v2:misc_25"                      # a real groan and wail: a voiced throat, the wail near 780 Hz
HINGES = R("creak2", "creak3")                   # real hinge squeals: stick-slip singing near 1.1 kHz
GASP = "sfx_100_v2:misc_05"                      # a real gasp: air forced through a mouth
CHOP = "kenney_rpg-audio:chop"                   # a real blade chopped into a board: a dry, woody tick
BONE = "sfx_100_v2:misc_33"                      # real wood and bone cracking
CRACKS = S("misc_34", "misc_35")                 # real wood cracking and breaking (g1_ground.splinters cuts them)
WOOD_M = K("impactWood_medium")                  # a block of wood knocked: the crate's corner
WOOD_L = K("impactWood_light")                   # light wood knocks: the crate's loose slats
PLANK = K("impactPlank_medium")                  # a plank's hollow knock: the crate's side
CREAK = R("creak1")[0]                           # a real small, bright creak: the crate's nailed joints
PUNCH = K("impactPunch_medium")                  # a real punch landing: the blow's weight
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
WHIP = S("switch_01", "switch_02")               # a real switch cracked through the air: a snap's sharp front

# ---- Bands (spec A.4) and bodies -----------------------------------------------------------------------------------------

TELL = (600, 7000)          # the tell: above the standing engine's idle, across a yard
CARRY = (300, 5000)         # carrying: the mid band, under the tell
LOOP_L = 6.0                # the feet's and the carry's cycles (s)

WAIL_F = 780.0              # the packs' wail's pitch (Hz): its throat is pulled from here
HINGE_F = 1120.0            # the hinge squeals' (Hz)
GRATE = [760, 1190, 1820, 2540, 3450, 4510]      # a cast-iron drain grating's bars: short, dull rings (cast iron is damped)
CRATE = [250, 430, 720, 1100, 1650, 2450]        # a small slatted crate: its panels' and slats' modes
PIPE = [280, 690, 1130, 1580, 2210]              # a drain pipe a foot across: its cross modes, none under the squish line
TRACT_K = 1.5               # a Picker's mouth as a share of a child's (synth.TRACT): small, so its formants sit high


def semis(hz, base):
    """Semitones from a recording's own pitch `base` to `hz` (a constant or a curve)."""
    return (12 * np.log2(np.asarray(hz, np.float64) / base)).astype(np.float32)


# ---- Teeth and throats ---------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=1)
def _teeth():
    """Every hard little tick in the packs' real chop and their wood and bone cracking, cut to its first 12 ms: teeth."""
    out = [unit(ck.cut(chop_tick(), 0, samples(0.012), 0.0003, 0.006))]
    for key in [BONE] + CRACKS:
        x = ck.get(key)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.015, prom=5):
            out.append(unit(hp(ck.cut(x, s - samples(0.0005), s + samples(0.012), 0.0003, 0.006), 1200)))
    return out


def tooth(rng, up=(4.0, 10.0)):
    """One tooth meeting another: a real tick pitched up `up` semitones to a small jaw's."""
    g = _teeth()
    return unit(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*up)))


def syllables(rng, length, rate, decay=(0.006, 0.013), attack=0.001):
    """A chatter's syllables as a gate: a sharp swell every 1/`rate` s (a constant or a curve), uneven in time and size,
    each dying in `decay` s, so there's a breath of quiet between them; and their onsets (where the teeth meet). The last
    one starts in time to die away before the end."""
    n = samples(length)
    rc = synth.curve(rate, n)
    g = np.zeros(n, np.float32)
    t, at = rng.uniform(0, 0.008), []
    while t < length - 0.035:
        d = rng.uniform(*decay)
        k = np.arange(samples(min(d * 5, length - t))) / SR
        seg = ((1 - np.exp(-k / attack)) * np.exp(-k / d) * rng.uniform(0.45, 1.0)).astype(np.float32)
        a = samples(t)
        m = min(len(seg), n - a)
        g[a:a + m] = np.maximum(g[a:a + m], seg[:m])
        at.append(t)
        t += rng.uniform(0.75, 1.3) / max(float(rc[min(samples(t), n - 1)]), 1.0)
    return dsp.fade(g, 0.0, 0.008), at


def chirps(n, at, depth=1.5, tau=0.012):
    """Each syllable's own little fall in pitch (semitones per sample): up `depth` at its onset, settling in `tau` s, the
    way a small throat's pitch drops through each 'tchk'."""
    c = np.zeros(n, np.float32)
    k = np.arange(n) / SR
    for t in at:
        a = samples(t)
        c[a:] = np.maximum(c[a:], depth * np.exp(-k[:n - a] / tau))
    return c


def pitch_line(rng, L, hz, fall=2.0, lift=1.06):
    """A small throat's pitch over a run (Hz per sample): up a hair at the start, sagging `fall` semitones by the end,
    never still."""
    n = samples(L)
    c = env([(0, lift), (L * 0.3, 1.0), (L, 2 ** (-fall / 12))], L, "exp")
    return (hz * c * 2 ** (wander(rng, n, 9, 0.5) / 12)).astype(np.float32)


def chatter_rec(rng, L, hz, rate, squeak=0.35):
    """One Picker's chitter from real throats: the packs' wail held out (paulstretched, so its grain stays and its
    syllables go, its breathy top taken off) and pulled up to a small throat's pitch, a real hinge's squeal under it a
    hair apart for the edge, a real gasp's breath through it, all chopped into quick syllables, each dropping a little in
    pitch, and a real tick of teeth on the front of most."""
    g, at = syllables(rng, L, rate)
    c = semis(pitch_line(rng, L, hz, rng.uniform(1, 3)), WAIL_F) + chirps(samples(L), at)
    v = unit(lp(held_cry(rng, WAIL, L, c, 0.05), 5000))
    if squeak:
        h = held_cry(rng, HINGES[int(rng.integers(2))], L, c + semis(rng.uniform(1.03, 1.07) * WAIL_F, HINGE_F), 0.05)
        v = mix(v, unit(h) * squeak)
    br = unit(lp(hp(drawn(rng, GASP, L, rng.uniform(3, 6), 0.05), 1500), 6000))
    y = unit(hp(mix(unit(v), br * 0.25) * g, 700))
    teeth = [(t + rng.uniform(0, 0.002), tooth(rng), rng.uniform(-9, -2)) for t in at if rng.random() < 0.8]
    return unit(mix(y, ck.place(teeth, L + 0.02) * 0.55))


def chatter_model(rng, L, hz, rate):
    """One Picker's chitter, modelled: a glottis at a small throat's pitch, rough and a little period-doubled, through a
    small mouth flicking from 'ee' open to 'eh' or 'ah' on every syllable, breathy, chopped into quick syllables that
    each drop a little in pitch, and two hard little teeth clicking on the front of most."""
    n = samples(L)
    g, at = syllables(rng, L, rate)
    f = pitch_line(rng, L, hz, rng.uniform(1, 3)) * 2 ** (chirps(n, at) / 12)
    vow = [(0.0, "i")]
    for t in at:
        if t > vow[-1][0] + 0.004:
            vow += [(t, "i"), (t + 0.006, ("e", "a")[int(rng.integers(2))])]
    gl = synth.glottis(f, L, rng, jitter=0.02, shimmer=0.25, oq=0.45, sub=0.2, rough=0.3)
    v = synth.tract(gl, vow, TRACT_K, breath=0.25, rng=rng)
    breath = bp(synth.noise(L, rng), 2500, 7000)
    y = unit(hp(mix(unit(v), unit(breath) * 0.15) * g[:n], 700))
    teeth = []
    for t in at:
        if rng.random() < 0.8:
            fc = rng.uniform(2800, 5200)
            teeth += [(t, synth.click(fc, q=rng.uniform(6, 11), length=0.008, rng=rng), rng.uniform(-8, -2)),
                      (t + rng.uniform(0.0015, 0.004), synth.click(fc * rng.uniform(0.8, 0.9), q=8, length=0.006, rng=rng),
                       rng.uniform(-14, -8))]
    return unit(mix(y, unit(ck.place(teeth, L + 0.02)) * 0.5))


def voices(rng, plan, run):
    """A few of them chittering together: `plan` [(start, length, pitch Hz, rate, dB, metres), ...], each its own run."""
    end = max(t + L for t, L, *_ in plan)
    b = Bus(end + 0.1)
    for t, L, hz, rate, db, m in plan:
        b.at(t, far(run(rng, L, hz, rate), m), db)
    return b.x


def chitter_plan(rng, k):
    """Four ways a chitter goes: one alone, short; two answering; three at once, thickening and thinning; one quick run
    that tips up at the end as if asking."""
    hz = lambda: rng.uniform(950, 1500)     # noqa: E731
    rate = lambda: rng.uniform(16, 26)      # noqa: E731
    if k % 4 == 0:
        return [(0.0, rng.uniform(0.3, 0.4), hz(), rate(), 0, 4)]
    if k % 4 == 1:
        a = rng.uniform(0.24, 0.3)
        return [(0.0, a, hz(), rate(), 0, 4), (a * rng.uniform(0.7, 0.9), rng.uniform(0.28, 0.36), hz(), rate(), -3, 7)]
    if k % 4 == 2:
        return [(0.0, rng.uniform(0.4, 0.5), hz(), rate(), -1, 5), (rng.uniform(0.06, 0.12), rng.uniform(0.35, 0.45), hz(),
                rate(), -3, 6), (rng.uniform(0.2, 0.28), rng.uniform(0.35, 0.45), hz(), rate(), -5, 9)]
    return [(0.0, rng.uniform(0.3, 0.36), hz(), rng.uniform(22, 28), 0, 4)]


def asking(rng, run, hz):
    """The end of the fourth chitter: two quick syllables tipping up, as if it asked something."""
    L = 0.12
    return dsp.shaped(run(rng, L, hz * 2 ** (rng.uniform(3, 5) / 12), 18), [(0, 0.4), (L * 0.6, 1), (L, 0.8)])


def chitter(rng, k, run):
    plan = chitter_plan(rng, k)
    y = voices(rng, plan, run)
    if k % 4 == 3:
        t, L, hz = plan[0][0], plan[0][1], plan[0][2]
        y = ck.place([(0, y, 0), (t + L + rng.uniform(0.03, 0.06), far(asking(rng, run, hz), 4), -2)])
    return held(air(y, rng, 0.05, 0.15), *TELL, drive=3)


# ---- Squeals ----------------------------------------------------------------------------------------------------------------

def squeal_line(rng, L, top, bottom):
    """A squeal's pitch (Hz per sample): a scoop up into its top at once, then falling away to `bottom` as it runs off,
    wavering."""
    n = samples(L)
    c = env([(0, top * 0.86), (0.025, top), (L * 0.3, top * 0.93), (L, bottom)], L, "exp")
    return (c * 2 ** (wander(rng, n, 7, 0.45) / 12)).astype(np.float32)


def hoarse(rng, y, L, lo=85, hi=55):
    """A squeal's hoarseness: the voice flickering `lo` to `hi` times a second, unevenly (a small throat strained past
    what it can hold), so it rasps instead of whistling."""
    return y * (0.35 + 0.65 * fry(rng, env([(0, lo), (L, hi)], L), L, jitter=0.4, decay=0.006))


def strain(y, f, depth=0.25):
    """Period doubling (a throat pushed past its limit flapping every other cycle): the squeal's pitch `f` (Hz per
    sample) gated by a square at half of it, so a rough under-tone breaks in under it, as synth.glottis's `sub` does."""
    half = np.sign(np.sin(np.pi * np.cumsum(synth.curve(f, len(y))) / SR))
    return (y * (1 - depth + depth * (0.5 + 0.5 * half))).astype(np.float32)


def squeal_env(L):
    """In at once, held a moment, then fading as it falls."""
    return env([(0, 0), (0.01, 1), (L * 0.25, 0.85), (L * 0.75, 0.45), (L, 0)], L)


def squeal_rec(rng, L, top, bottom, take=0):
    """One shrill squeal from real throats: a real hinge's squeal and the packs' wail (its breathy top taken off) held
    out and pulled up to the squeal's top, falling away to its bottom, a few cents apart so they beat; strained into
    period doubling and hoarse; a real gasp's breath through the teeth over it."""
    f = squeal_line(rng, L, top, bottom)
    a = held_cry(rng, HINGES[take % 2], L, semis(f, HINGE_F), 0.06)
    b = lp(held_cry(rng, WAIL, L, semis(f * rng.uniform(1.01, 1.03), WAIL_F), 0.06), 4500)
    y = hoarse(rng, strain(mix(unit(a), unit(b) * 0.6), f, rng.uniform(0.2, 0.35)), L)
    br = unit(lp(hp(drawn(rng, GASP, L, rng.uniform(4, 7), 0.05), 2500), 7000))
    return unit(hp(mix(unit(y), br * 0.15), 800)) * squeal_env(L)


def squeal_model(rng, L, top, bottom):
    """One shrill squeal, modelled: a glottis at the squeal's pitch, falling, jittery and breaking into period doubling,
    through a small mouth on 'eh' opening to 'ah', a second throat a few cents off it in a mouth of its own size (they
    beat), breath through the teeth; hoarse."""
    f = squeal_line(rng, L, top, bottom)
    v = []
    for d in (1.0, rng.uniform(1.012, 1.03)):
        gl = synth.glottis(f * d, L, rng, jitter=0.025, shimmer=0.3, oq=0.35, sub=rng.uniform(0.2, 0.45), rough=0.4)
        k = TRACT_K * rng.uniform(0.85, 1.15)     # each its own mouth, so their formants never line up into one whistle
        v.append(unit(synth.tract(gl, [(0, "e"), (L * 0.4, "a"), (L, "a")], k, breath=0.2, rng=rng)))
    teeth = bp(synth.noise(L, rng), 3500, 8000)
    y = hoarse(rng, mix(v[0], v[1] * 0.7), L)
    return unit(lp(hp(mix(unit(y), unit(teeth) * 0.1), 800), 7000)) * squeal_env(L)


# ---- Feet ---------------------------------------------------------------------------------------------------------------------

def foot_rec(rng):
    """One small foot coming down on gravel and cinders, from real recordings: a crunch of real gravel cut to its first
    hundredth or two and pitched up for small stones under a small weight, a hard little claw (a real tick) on its front."""
    crunch = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(0.01, 0.022), rng.uniform(2, 6))
    return ck.place([(0, unit(hp(crunch, 500)), 0), (rng.uniform(0, 0.002), tooth(rng, (0, 5)), rng.uniform(-16, -8))])


def foot_model(rng):
    """One small foot on gravel and cinders, modelled: a claw's click, a handful of grit breaking under it, the pad's
    little give."""
    claw = synth.click(rng.uniform(2800, 5200), q=rng.uniform(5, 10), length=0.008, rng=rng)
    crunch = ck.grains(rng, int(rng.integers(4, 9)), 0.014, 900, 6000, q=(3, 8), length=(0.003, 0.01))
    return ck.place([(0, unit(claw), -7), (0.001, unit(crunch), 0), (0.0, ck.pad(rng, 0.018, 900), -16)])


def pebble_rec(rng):
    """A little stone kicked, ticking off the others: a grain of the real stones' crackle pitched up."""
    return unit(hp(grain(rng, GRIT, rng.uniform(0.006, 0.012), rng.uniform(5, 9)), 1500))


def pebble_model(rng):
    """A little stone kicked, modelled: a hard tick and a smaller one as it skips."""
    return ck.bounce(rng, unit(ck.tick(rng, rng.uniform(2500, 6000), q=rng.uniform(10, 25), length=0.012)), n=1, first=0.04)


def skid_rec(rng, L=0.12):
    """A turn taken short: the feet slewing over the gravel, real grit shoved and rolling."""
    v = env([(0, 0), (L * 0.2, 1), (L, 0)], L)
    return gravel_grains(rng, L, 80 + 300 * v, (1, 5), (0.006, 0.015)) * v


def skid_model(rng, L=0.12):
    """A turn taken short, modelled: claws dragged over gravel (stick-slip) and grit breaking under them."""
    v = env([(0, 0), (L * 0.2, 1), (L, 0)], L)
    grit = unit(dsp.fit(ck.grains(rng, 18, L, 1000, 6000), samples(L)))
    return mix(ck.friction(rng, L, 260, 900, 6000, 0.9) * v * 0.6, grit * v * 0.6)


def gait(rng, L, foot, pebble, skid, rate=3.2):
    """One of them scuttling on all fours for a loop of L s, periodic: bounding strides `rate` a second (the forefeet a
    hair apart, then the hind pair, unevenly bunched), so 12-13 footfalls a second; now and then a foot skipped; little
    stones kicked ticking after; one turn taken short; the level breathing as it weaves about the yard."""
    n = samples(L)
    strides = int(round(L * rate))
    T = L / strides
    sway = W.slow(n, 0.4, rng)
    ev = []
    for i in range(strides):
        t0 = i * T + rng.normal(0, 0.008)
        fore = rng.uniform(0.015, 0.035)
        hind = rng.uniform(0.08, 0.12)
        for j, o in enumerate((0.0, fore, hind, hind + rng.uniform(0.014, 0.03))):
            if rng.random() < 0.06:
                continue
            t = t0 + o + rng.normal(0, 0.004)
            db = 2.0 * float(sway[samples(t) % n]) + rng.uniform(-6, 0) - (1.5 if j >= 2 else 0.0)
            ev.append((t, foot(rng), dsp.db2a(db)))
        if rng.random() < 0.6:
            ev.append((t0 + rng.uniform(0.05, 0.25), pebble(rng), dsp.db2a(rng.uniform(-22, -12))))
    ev.append((rng.uniform(0, L), skid(rng), dsp.db2a(-8)))
    return W.place(n, ev)


# ---- The grating ----------------------------------------------------------------------------------------------------------------

def strokes(L, shoves):
    """How fast the grating's moving over L s: `shoves` [(start, length), ...], each a swell and a stop."""
    pts = [(0.0, 0.0)]
    for a, d in shoves:
        pts += [(a, 0.0), (a + d * 0.2, 1.0), (a + d * 0.75, 0.7), (a + d, 0.0)]
    pts.append((max(L, pts[-1][0] + 1e-3), 0.0))
    return env(pts, L)


def shove_rec(rng, L, shoves):
    """The grating shoved over its stone kerb, from real recordings: a real metal scrape dropped most of an octave for
    heavy cast iron and rung a little through a grating's bars, real stones grinding and crackling under it, moving only
    while it's shoved."""
    v = strokes(L, shoves)
    s = dsp.vari(ck.get(SCRAPE), rng.uniform(-8, -5))
    a = int(rng.integers(0, max(1, len(s) - samples(L))))
    s = dsp.fit(s[a:], samples(L))
    s = mix(unit(s), unit(dsp.resonate(s, [f * rng.uniform(0.95, 1.05) for f in GRATE], q=12)) * 0.35)
    g = dsp.vari(ck.get(GRIT), rng.uniform(-3, 0))
    a = int(rng.integers(0, max(1, len(g) - samples(L))))
    g = dsp.fit(g[a:], samples(L))
    return mix(unit(s) * v, unit(g) * v ** 0.7 * 0.5, gravel_grains(rng, L, 30 + 150 * v, (-3, 2)) * v * 0.3)


def clank_rec(rng, take, size=1.0):
    """The grating dropped back into its frame: a real blow on steel dropped for a cast-iron grating and choked short
    (cast iron rings dull), a real iron ring under it, a real latch dropping for its edge striking the frame, and its
    bars rattling as it settles (a real loose-metal rattle)."""
    sheet = ck.choke(dsp.vari(main_hit(CLANK, 0.3), rng.uniform(-3, -1)), 0.04, 0.05)
    ring = ck.choke(dsp.vari(hit_of(RING[take % 5], 0, 0.25), rng.uniform(-2, 1)), 0.03, 0.05)
    edge = dsp.vari(hit_of(LATCHES[take % 3], 0, 0.15), rng.uniform(-6, -3))
    settle = rattle(rng, 0.14, env([(0, 35), (0.14, 12)], 0.14), RATTLE + LATCHES, (-6, -2), band=(500, 5000))
    return ck.place([(0, unit(hp(sheet, 300)), 0), (0.001, unit(ring), -6), (0.002, unit(edge), -5),
                     (0.03, unit(settle), -10 + 4 * np.log2(size))])


def lift_rec(rng, L):
    """The grating lifted in its frame from underneath: its bars rattling against the frame (a real loose-metal rattle and
    real latches, cut to their first hundredths and choked)."""
    return rattle(rng, L, env([(0, 10), (L * 0.6, 32), (L, 18)], L), RATTLE + LATCHES, (-6, -2), band=(500, 5000))


def shove_model(rng, L, shoves):
    """The grating shoved over its kerb, modelled: the bars juddering over the stone as they catch and skip (stick-slip,
    40-90 slips a second, quicker as it's shoved harder, rung through a cast-iron grating's bars), iron rubbing between
    the slips (rough contact noise through the same bars, under it), grit breaking under it."""
    v = strokes(L, shoves)
    n = samples(L)
    judder = synth.creak(L, 40 + 50 * v, rng, body=[f * rng.uniform(0.95, 1.05) for f in GRATE], q=10, jitter=0.6,
                         grit=0.7)
    rub = W.friction(n, rng, [f * rng.uniform(0.95, 1.05) for f in GRATE], rough=1.5, grit=0.2, q=14)
    grit = ck.grains(rng, int(90 * L), L, 1200, 6000, q=(3, 8), length=(0.003, 0.012), shape=0.0)
    return mix(unit(hp(judder, 500)) * v, unit(hp(rub, 500)) * v * 0.3, unit(dsp.fit(grit, n)) * v ** 0.6 * 0.4)


def clank_model(rng, size=1.0):
    """The grating dropped back into its frame, modelled: a cast-iron grating struck hard (a few milliseconds of contact
    rung through its bars' modes, damped short as cast iron is), the crack of iron on iron at the front, its bars
    chattering as it settles, a second knock as it rocks."""
    def strike(tail, contact, at):
        x = struck(rng, [f * rng.uniform(0.96, 1.04) for f in GRATE], 60, tail, contact)
        return ck.choke(x, at, 0.07)

    crack = burst(rng, 0.004, 1800, 9000, attack=0.1)
    chat = [(0.03 + 0.012 * i + rng.uniform(0, 0.006), ck.tick(rng, rng.uniform(1600, 4200), q=rng.uniform(18, 35),
                                                                 length=0.02), -10 - 3 * i) for i in range(int(rng.integers(3, 6)))]
    return ck.place([(0, strike(0.35, 0.004, 0.05), 0), (0, unit(crack), -3),
                     (rng.uniform(0.07, 0.1), strike(0.2, 0.003, 0.02), -7 + 4 * np.log2(size))] + chat)


def lift_model(rng, L):
    """The grating lifted from underneath, modelled: its bars knocking against the frame, ticks through its modes."""
    b = Bus(L + 0.05)
    rc = env([(0, 10), (L * 0.6, 32), (L, 18)], L)
    t = rng.exponential(0.05)
    while t < L:
        x = ck.choke(struck(rng, [f * rng.uniform(0.9, 1.1) for f in GRATE[1:]], 20, 0.03, 0.0008), 0.004, 0.006)
        b.at(t, x, 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5))
        t += (0.5 + 0.5 * rng.exponential()) / max(float(rc[min(samples(t), len(rc) - 1)]), 1)
    return b.x


def emerge(rng, k, lift, shove, clank, run, foot):
    """Three ways out of a drain. One long shove and the grating dropped; a shove that jams (it's lifted and rattled
    impatiently) and a second that frees it; the grating lifted, tipped and let fall at once. Each with the chitter
    starting under the iron (two voices, three on the last) and a few quick feet out onto the gravel at the end."""
    hz = lambda: rng.uniform(1000, 1450)    # noqa: E731
    if k % 3 == 0:
        L = rng.uniform(0.45, 0.55)
        parts = [(0.0, lift(rng, 0.12), -6), (0.06, shove(rng, L, [(0.0, L)]), -3), (0.06 + L, clank(rng, k), 3)]
        plan = [(0.25, 0.45, hz(), rng.uniform(18, 24), -4, 5), (0.55, 0.4, hz(), rng.uniform(18, 24), -6, 7)]
        feet = 0.06 + L + 0.3
    elif k % 3 == 1:
        a, b = rng.uniform(0.14, 0.2), rng.uniform(0.3, 0.38)
        gap = rng.uniform(0.1, 0.15)
        parts = [(0.0, shove(rng, a, [(0.0, a)]), -5), (a, lift(rng, gap + 0.05), -4),
                 (a + gap, shove(rng, b, [(0.0, b)]), -3), (a + gap + b, clank(rng, k), 3)]
        plan = [(0.12, 0.35, hz(), rng.uniform(20, 26), -5, 5), (a + gap + b - 0.05, 0.45, hz(), rng.uniform(16, 22), -4, 6)]
        feet = a + gap + b + 0.32
    else:
        T = rng.uniform(0.18, 0.24)
        parts = [(0.0, lift(rng, T), -2), (T * 0.6, shove(rng, 0.16, [(0.0, 0.16)]), -6), (T + 0.12, clank(rng, k, 1.4), 3)]
        plan = [(T + 0.15, 0.4, hz(), rng.uniform(18, 24), -3, 4), (T + 0.22, 0.45, hz(), rng.uniform(18, 26), -5, 6),
                (T + 0.4, 0.4, hz(), rng.uniform(16, 22), -7, 9)]
        feet = T + 0.5
    parts.append((0.0, voices(rng, plan, run), 0))
    for i in range(int(rng.integers(4, 7))):
        parts.append((feet + i * rng.uniform(0.04, 0.09), foot(rng), -10 - 1.5 * i))
    return held(air(ck.place(parts), rng, 0.1, 0.3), *TELL, drive=3)


# ---- tell-pickers: emerge, feet, chitter (600 Hz-7 kHz) ----------------------------------------------------------------------

@recipe("tell-pickers", "emerge", "grate",
        "Out of a drain: a cast-iron grating shoved over its kerb and dropped with a clank, and a chitter of small teeth",
        """From real recordings: the grating shoved aside over its stone kerb (a real metal scrape dropped most of an octave
        for heavy cast iron, rung a little through a grating's bars, real stones grinding and crackling under it), its bars
        rattling in the frame as it's lifted (a real loose-metal rattle and real latches, cut to their first hundredths),
        then dropped with a clank (a real blow on steel dropped and choked short, a real iron ring, a real latch dropping
        for its edge on the frame, its bars settling). Under the iron the chitter starts: two or three small throats in
        quick dry syllables (the packs' wail held out and pulled up to a small throat, a real hinge's squeal for its edge,
        a real gasp's breath), real ticks of teeth on their fronts; a few quick feet out onto the gravel at the end. Takes:
        one long shove; a shove that jams and a second that frees it; lifted and let fall. Held to 600 Hz-7 kHz.""",
        sources=[SCRAPE, GRIT, CLANK, WAIL, GASP, CHOP, BONE] + RATTLE + LATCHES + RING + HINGES + CRACKS + GRAVEL,
        takes=3, band=TELL, lufs=-20)
def emerge_grate(rng, k):
    return emerge(rng, k, lift_rec, shove_rec, clank_rec, chatter_rec, foot_rec)


@recipe("tell-pickers", "emerge", "bars",
        "Out of a drain, modelled: iron dragged over stone, the grating's bars ringing as it drops, a chitter of throats",
        """Modelled: the grating shoved over its kerb (its bars juddering over the stone as they catch and skip, stick-slip
        rung through a cast-iron grating's modes, iron rubbing under it, grit breaking), its bars knocking in the frame as
        it's lifted, then dropped: the grating struck hard (its modes, 760 Hz-4.5 kHz, damped short as cast iron is), iron
        cracking on iron, the bars chattering and a second knock as it rocks. Under it the chitter: two or three small
        throats (a glottis through a small mouth flicking 'ee' to 'eh' or 'ah', rough, breathy) chopped into quick
        syllables with teeth clicking on their fronts; a few quick feet out onto the gravel. The same three takes as
        'grate'. Held to 600 Hz-7 kHz.""",
        takes=3, band=TELL, lufs=-20)
def emerge_bars(rng, k):
    return emerge(rng, k, lift_model, shove_model, lambda r, t, s=1.0: clank_model(r, s), chatter_model, foot_model)


@recipe("tell-pickers", "feet", "cinders",
        "One of them scuttling: many small quick feet on gravel and cinders, little stones ticking away after them",
        """From real recordings: each foot a crunch of real gravel cut to its first hundredth or two and pitched up for
        small stones under a small weight, with a hard little claw on its front (a real tick, pitched). On all fours in
        bounding strides, three a second (the forefeet a hair apart, then the hind pair, unevenly bunched): twelve or
        thirteen footfalls a second, now and then one skipped; little stones kicked and ticking off the others (grains of
        real stones crackling); once a loop a turn taken short, the gravel slewing. No heel, no sole: never boots. A 6 s
        loop, held to 600 Hz-7 kHz, low.""",
        sources=GRAVEL + [CHOP, BONE] + CRACKS, takes=1, loop=True, band=TELL, lufs=-25, seconds=LOOP_L)
def feet_cinders(rng, k):
    return cycle(gait(rng, LOOP_L, foot_rec, pebble_rec, skid_rec), LOOP_L, rng, TELL, drive=2, wet=0.08)


@recipe("tell-pickers", "feet", "claws",
        "One of them scuttling, modelled: hard little claws and grit breaking, many small feet, stones skipping",
        """Modelled: each foot a claw's hard click, a handful of grit breaking under it and the pad's little give; the same
        bounding gait as 'cinders' (four feet to a stride, bunched fore and hind, three strides a second, twelve or thirteen
        footfalls), little stones kicked skipping away (a hard tick and a smaller one), a turn taken short once a loop
        (claws dragged over gravel). A 6 s loop, held to 600 Hz-7 kHz, low.""",
        takes=1, loop=True, band=TELL, lufs=-25, seconds=LOOP_L)
def feet_claws(rng, k):
    return cycle(gait(rng, LOOP_L, foot_model, pebble_model, skid_model), LOOP_L, rng, TELL, drive=2, wet=0.08)


@recipe("tell-pickers", "chitter", "jaws",
        "A chitter now and then as they go about: quick, dry syllables of small throats and teeth, a few voices",
        """From real throats: the packs' wail held out (paulstretched, so its grain stays and its words go) and pulled up to
        a small creature's pitch (950 Hz-1.5 kHz), a real hinge's squeal a hair apart under it for the edge and a real gasp's
        breath through it, chopped into quick syllables, 16-28 a second, each a couple of hundredths long and dropping a
        little in pitch, a real tick of teeth (a blade's chop, wood and bone cracking, cut down and pitched up) on the front
        of most. Dry: nothing wet.
        Takes: one alone; two answering; three at once, thickening and thinning; one quick run that tips up at the end as
        if asking. Held to 600 Hz-7 kHz.""",
        sources=[WAIL, GASP, CHOP, BONE] + HINGES + CRACKS, takes=4, band=TELL, lufs=-22)
def chitter_jaws(rng, k):
    return chitter(rng, k, chatter_rec)


@recipe("tell-pickers", "chitter", "throats",
        "A chitter, modelled: small rough throats in quick dry syllables, teeth clicking on each, a few voices",
        """Modelled: a glottis at a small throat's pitch (950 Hz-1.5 kHz), rough and a little period-doubled, through a
        small mouth flicking from 'ee' open to 'eh' or 'ah' on every syllable, breathy, chopped into quick syllables
        (16-28 a second, each dropping a little in pitch) with two hard little teeth clicking on the front of most. The same
        four takes as 'jaws'. Held to 600 Hz-7 kHz.""",
        takes=4, band=TELL, lufs=-22)
def chitter_throats(rng, k):
    return chitter(rng, k, chatter_model)


# ---- cs-pickers: carrying (300 Hz-5 kHz) -------------------------------------------------------------------------------------

def jostles(rng, L):
    """When the crate's knocked about over a loop of L s: every 0.3-0.8 s, unevenly, the gaps scaled to come round."""
    gaps = []
    while sum(gaps) < L:
        gaps.append(rng.uniform(0.3, 0.8))
    gaps = np.array(gaps) * L / sum(gaps)
    return list(np.concatenate([[0.0], np.cumsum(gaps)[:-1]]) + rng.uniform(0, 0.2))


def knock_rec(rng, take):
    """The crate jostled in its long hands: a real wooden knock pitched up a little for a small crate and choked short,
    its loose slats chattering after it (real light knocks, pitched up)."""
    k = ck.choke(dsp.vari(hit_of(WOOD_M[take % 5], int(rng.integers(2)), 0.2), rng.uniform(1, 4)), 0.015, 0.03)
    slats = [(0.006 + 0.012 * i + rng.uniform(0, 0.006),
              ck.choke(dsp.vari(hit_of(WOOD_L[(take + i) % 5], 0, 0.1), rng.uniform(4, 8)), 0.005, 0.012), -8 - 4 * i)
             for i in range(int(rng.integers(1, 4)))]
    return hp(ck.place([(0, unit(k), 0)] + slats), 220, 4)


def drag_rec(rng, L):
    """A corner of the crate dragged over the gravel: the wood rasping on the stones (real stones scraping), real gravel
    shoved and rolling ahead of it, a real plank's hollow knock as the corner catches."""
    v = env([(0, 0), (L * 0.25, 1), (L * 0.7, 0.6), (L, 0)], L)
    g = dsp.vari(ck.get(GRIT), rng.uniform(-4, -1))
    a = int(rng.integers(0, max(1, len(g) - samples(L))))
    rasp = unit(dsp.fit(g[a:], samples(L))) * v
    roll = gravel_grains(rng, L, 40 + 220 * v, (-3, 2)) * v ** 0.7
    catch = ck.choke(dsp.vari(hit_of(PLANK[int(rng.integers(5))], 0, 0.2), rng.uniform(2, 5)), 0.012, 0.025)
    return ck.place([(0, mix(rasp * 0.6, roll * 0.8), 0), (0.0, unit(hp(catch, 250)), -6)])


def creak_rec(rng, L):
    """The crate's nailed joints working as it's carried: a real small creak dropped a few semitones and cut short."""
    c = dsp.vari(ck.align(ck.get(CREAK)), rng.uniform(-5, -2))[:samples(L)]
    n = len(c) / SR
    return unit(bp(c, 400, 4000)) * env([(0, 0), (0.02, 1), (n * 0.7, 0.7), (n, 0)], n)


def pant_rec(rng, L):
    """A chattering pant from a small mouth: quick shallow breaths out and in (a real gasp's breath pitched up into a
    small mouth and cut short), teeth chattering on the outbreaths, every second or so a few quick syllables of
    chitter."""
    ev, t, out = [], rng.uniform(0, 0.05), True
    while t < L:
        d = rng.uniform(0.04, 0.07) if out else rng.uniform(0.05, 0.08)
        x = drawn(rng, GASP, d + 0.02, rng.uniform(5, 9), 0.02)[:samples(d)]
        x = unit(lp(hp(x, 900), 5000)) * env([(0, 0), (d * (0.15 if out else 0.7), 1), (d, 0)], d)
        ev.append((t, x, dsp.db2a(0 if out else -5)))
        if out and rng.random() < 0.6:
            for j in range(int(rng.integers(2, 5))):
                ev.append((t + 0.005 + j * rng.uniform(0.03, 0.05), tooth(rng, (2, 8)), dsp.db2a(rng.uniform(-14, -6))))
        t += d + rng.uniform(0.04, 0.09)
        out = not out
    for c in np.sort(rng.uniform(0, L, int(round(L / 1.4)))):
        D = rng.uniform(0.14, 0.26)
        ev.append((float(c), chatter_rec(rng, D, rng.uniform(1000, 1400), rng.uniform(20, 26), 0.2), dsp.db2a(-4)))
    return W.place(samples(L), ev)


def knock_model(rng, take=0):
    """The crate jostled, modelled: a small slatted crate struck (its panels' modes, damped by what's in it), its loose
    slats knocking after."""
    k = ck.choke(struck(rng, [f * rng.uniform(0.92, 1.08) for f in CRATE], 6, 0.12, 0.002,
                        gains=[1.0, 0.8, 0.55, 0.35, 0.22, 0.14]), 0.012, 0.03)
    slats = [(0.006 + 0.012 * i + rng.uniform(0, 0.006),
              ck.choke(struck(rng, [f * rng.uniform(1.4, 1.9) for f in CRATE[2:]], 8, 0.04, 0.001), 0.004, 0.01), -8 - 4 * i)
             for i in range(int(rng.integers(1, 4)))]
    return hp(ck.place([(0, k, 0)] + slats), 220, 4)


def drag_model(rng, L):
    """A corner of the crate dragged over gravel, modelled: wood on stone juddering (stick-slip), stones breaking and
    rolling ahead of it, a knock as it catches."""
    v = env([(0, 0), (L * 0.25, 1), (L * 0.7, 0.6), (L, 0)], L)
    rub = ck.friction(rng, L, rng.uniform(140, 220), 400, 4000, 0.8)
    stones = ck.grains(rng, int(110 * L), L, 800, 5000, q=(3, 8), length=(0.004, 0.015), shape=0.0)
    catch = struck(rng, [f * rng.uniform(1.1, 1.3) for f in CRATE[1:]], 5, 0.06, 0.002)
    stones = unit(dsp.fit(stones, samples(L)))
    return ck.place([(0, mix(rub * v * 0.6, stones * v ** 0.7 * 0.7), 0), (0.0, catch, -7)])


def creak_model(rng, L):
    """The crate's joints working, modelled: slow stick-slip through its slats' modes."""
    s = synth.creak(L, env([(0, 30), (L, 60)], L, "exp"), rng, body=[f * rng.uniform(1.4, 1.8) for f in CRATE[1:5]], q=10,
                    jitter=0.4, grit=0.4)
    return unit(hp(s, 400)) * env([(0, 0), (0.02, 1), (L * 0.7, 0.7), (L, 0)], L)


def pant_model(rng, L):
    """A chattering pant, modelled: quick shallow breaths through a small mouth ('h' to 'ee' out, 'ee' in), two hard
    little teeth clicking on the outbreaths, a few quick syllables of chitter every second or so."""
    ev, t, out = [], rng.uniform(0, 0.05), True
    while t < L:
        d = rng.uniform(0.04, 0.07) if out else rng.uniform(0.05, 0.08)
        sh = env([(0, 0), (d * (0.15 if out else 0.7), 1), (d, 0)], d)
        x = synth.breath(d, [(0, "h"), (d, "i" if out else "e")], TRACT_K, rng, shape=sh)
        ev.append((t, unit(bp(x, 900, 6000)), dsp.db2a(0 if out else -5)))
        if out and rng.random() < 0.6:
            for j in range(int(rng.integers(2, 5))):
                c = synth.click(rng.uniform(2800, 5200), q=rng.uniform(6, 11), length=0.008, rng=rng)
                ev.append((t + 0.005 + j * rng.uniform(0.03, 0.05), unit(c), dsp.db2a(rng.uniform(-14, -6))))
        t += d + rng.uniform(0.04, 0.09)
        out = not out
    for c in np.sort(rng.uniform(0, L, int(round(L / 1.4)))):
        D = rng.uniform(0.14, 0.26)
        ev.append((float(c), chatter_model(rng, D, rng.uniform(1000, 1400), rng.uniform(20, 26)), dsp.db2a(-4)))
    return W.place(samples(L), ev)


def carry(rng, knock, drag, creak, pant):
    """A crate carried for a loop of LOOP_L s: knocked about every 0.3-0.8 s, unevenly (a knock in its hands, a corner
    dragged over the gravel, or both), its joints creaking now and then, the chattering pant under it all."""
    L = LOOP_L
    n = samples(L)
    ev = []
    for i, t in enumerate(jostles(rng, L)):
        kind = int(rng.integers(3))
        if kind != 1:
            ev.append((t, knock(rng, i), dsp.db2a(rng.uniform(-4, 0))))
        if kind != 0:
            D = rng.uniform(0.12, 0.32)
            ev.append((t + (0.04 if kind == 2 else 0.0), drag(rng, D), dsp.db2a(rng.uniform(-5, -1))))
        if rng.random() < 0.25:
            ev.append((t + rng.uniform(0.05, 0.2), creak(rng, rng.uniform(0.12, 0.25)), dsp.db2a(-14)))
    y = mix(W.place(n, ev), unit(pant(rng, L)) * dsp.db2a(-9))
    return cycle(y, L, rng, CARRY, drive=2, wet=0.08)


@recipe("cs-pickers", "carry", "crate",
        "Carrying: a crate knocked and dragged over the gravel as it's jostled, a chattering pant under it",
        """From real recordings: the crate jostled in its long hands every 0.3 to 0.8 s, unevenly: a real wooden knock
        pitched up a little for a small crate and choked short, its loose slats chattering after (real light knocks), or a
        corner dragged over the gravel (real stones scraping for the wood rasping on them, real gravel shoved and rolling
        ahead, a real plank's hollow knock as it catches), or both; its nailed joints creaking now and then (a real small
        creak, dropped). Under it a chattering pant: quick shallow breaths out and in (a real gasp pitched up into a small
        mouth and cut short), teeth chattering on the outbreaths (real ticks), and every second or so a few quick
        syllables of chitter. A 6 s loop, held to 300 Hz-5 kHz.""",
        sources=WOOD_M + WOOD_L + PLANK + [GRIT, CREAK, GASP, WAIL, CHOP, BONE] + GRAVEL + HINGES + CRACKS,
        takes=1, loop=True, band=CARRY, lufs=-24, seconds=LOOP_L)
def carry_crate(rng, k):
    return carry(rng, knock_rec, drag_rec, creak_rec, pant_rec)


@recipe("cs-pickers", "carry", "slats",
        "Carrying, modelled: a slatted crate knocking and scraping over gravel, a chattering pant from a small mouth",
        """Modelled: a small slatted crate struck (its panels' modes, 250 Hz-2.5 kHz, damped by what's in it) as it's
        jostled every 0.3 to 0.8 s, its loose slats knocking after, or a corner dragged over gravel (wood on stone juddering
        in stick-slip, stones breaking and rolling, a knock as it catches), or both; its joints creaking now and then
        (slow stick-slip through its slats). Under it a chattering pant: quick shallow breaths through a small mouth, teeth
        clicking on the outbreaths, a few quick syllables of chitter every second or so. A 6 s loop, held to 300 Hz-5 kHz.""",
        takes=1, loop=True, band=CARRY, lufs=-24, seconds=LOOP_L)
def carry_slats(rng, k):
    return carry(rng, knock_model, drag_model, creak_model, pant_model)


# ---- cs-pickers: the bite ------------------------------------------------------------------------------------------------------

def snap_rec(rng, take):
    """Small hard teeth closing, from real recordings: a real switch's crack pitched up small and cut to its front (the
    jaw snapping shut), two real ticks a hair apart (the rows meeting), a grain of real wood cracking under them (the
    snap's crack), and the small skull's dry knock behind them (a real light wood knock pitched up most of an octave)."""
    whip = unit(hp(dsp.vari(main_hit(WHIP[take % 2], 0.04), rng.uniform(5, 8)), 1200))
    g = splinters()
    crack = unit(dsp.fade(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(-1, 2))[:samples(0.03)], 0.0, 0.01))
    skull = ck.choke(dsp.vari(hit_of(WOOD_L[take % 5], 0, 0.06), rng.uniform(9, 12)), 0.006, 0.01)
    return ck.place([(0, whip, -3), (0.0005, tooth(rng, (2, 6)), 0), (rng.uniform(0.002, 0.004), tooth(rng, (1, 4)), -3),
                     (0.001, crack, -6), (0.001, unit(hp(skull, 600)), -7)])


def hiss_rec(rng, L):
    """Its hiss after: a real gasp's breath pitched up into a small mouth and drawn out, forced through the teeth (held to
    2.5-9 kHz), trembling."""
    br = bp(drawn(rng, GASP, L, rng.uniform(2, 5), 0.05), 2500, 9000)
    sh = env([(0, 0), (0.015, 1), (L * 0.35, 0.7), (L, 0)], L)
    return dsp.tremolo(unit(br) * sh, rng.uniform(16, 24), 0.35, rng, jitter=0.4)


def snap_model(rng):
    """Small hard teeth closing, modelled: a crack through hard little modes (the needle teeth), two clicks a hair apart
    (the rows meeting), the small skull's dry knock under them."""
    teeth = kit.snap(rng, 0.04, rng.uniform(3000, 4200))
    rows = [(0.0, synth.click(rng.uniform(3500, 5000), q=10, length=0.01, rng=rng), 0),
            (rng.uniform(0.002, 0.004), synth.click(rng.uniform(2800, 4000), q=8, length=0.008, rng=rng), -4)]
    skull = ck.hollow(rng, [rng.uniform(900, 1200), rng.uniform(2000, 2500)], 0.03, q=6, hit=0.001)
    return ck.place([(0, unit(teeth), -2), (0, skull, -8)] + [(t, unit(x), g) for t, x, g in rows])


def hiss_model(rng, L):
    """Its hiss, modelled: breath forced through a small mouth on 'ee' and the teeth's own hiss over it, trembling."""
    sh = env([(0, 0), (0.015, 1), (L * 0.35, 0.7), (L, 0)], L)
    mouth = synth.breath(L, [(0, "i"), (L, "h")], TRACT_K, rng, shape=sh)
    teeth = bp(synth.noise(L, rng), 3500, 9000) * sh
    return dsp.tremolo(unit(mix(unit(hp(mouth, 1500)), unit(teeth) * 0.6)), rng.uniform(16, 24), 0.35, rng, jitter=0.4)


def sleeve_rec(rng, k):
    """The sleeve caught in the teeth: a slice of the packs' real cloth handled."""
    return unit(hp(ck.cloth(rng, k, 0.08), 400))


def sleeve_model(rng, k):
    """The sleeve caught, modelled: a short breath of noise, cloth's broad band, snatched."""
    L = 0.08
    return unit(bp(synth.noise(L, rng, "pink"), 500, 4000) * env([(0, 0), (0.004, 1), (L, 0)], L))


def bite(rng, k, snap, hiss, sleeve):
    """Three bites: one snap and a hiss; a snap that misses and a second that takes hold; a snap through a sleeve (the
    cloth caught) and a longer hiss."""
    parts = [(0, snap(rng, k), 0)]
    t = 0.0
    if k % 3 == 1:
        t = rng.uniform(0.07, 0.1)
        parts.append((t, snap(rng, k + 1), 1))
    if k % 3 == 2:
        parts.append((0.002, sleeve(rng, k), -12))
    L = (0.26, 0.22, 0.32)[k % 3] * rng.uniform(0.9, 1.1)
    parts.append((t + rng.uniform(0.04, 0.07), hiss(rng, L), -10))
    return finish(lp(hp(air(ck.place(parts), rng, 0.06, 0.15), 300, 4), 10000), -19, -1.5)


@recipe("cs-pickers", "bite", "snap",
        "A snap of small hard teeth and a hiss: it hurts, but it's small",
        """From real recordings: the snap a real switch's crack pitched up small and cut to its front (the jaw shutting),
        two real ticks a hair apart for the rows of needle teeth meeting, a grain of real wood cracking under them and the
        small skull's dry knock behind (a real light wood knock pitched up most of an octave); then, quieter, a hiss: a
        real gasp's breath pitched up into a small mouth, drawn out and forced through the teeth (held to 2.5-9 kHz),
        trembling. Takes: one snap; a snap that misses and a second that takes hold; a snap through a sleeve (the packs'
        cloth caught) and a longer hiss. About half a second.""",
        sources=WHIP + [GASP, CHOP, BONE] + CRACKS + WOOD_L + CLOTH, takes=3, lufs=-19)
def bite_snap(rng, k):
    return bite(rng, k, snap_rec, hiss_rec, sleeve_rec)


@recipe("cs-pickers", "bite", "teeth",
        "A snap and a hiss, modelled: needle teeth cracking shut, a small skull's knock, breath hissed through the teeth",
        """Modelled: the snap a crack through hard little modes (3-4 kHz: the needle teeth), two clicks a hair apart for the
        rows meeting and the small skull's dry knock under them; then a hiss: breath forced through a small mouth on 'ee'
        with the teeth's own hiss over it, trembling. The same three takes as 'snap' (the sleeve a breath of cloth). About
        half a second.""",
        takes=3, lufs=-19)
def bite_teeth(rng, k):
    return bite(rng, k, lambda r, t: snap_model(r), hiss_model, sleeve_model)


# ---- cs-pickers: scattering ----------------------------------------------------------------------------------------------------

def scrabble(rng, length, rate, foot, away=True):
    """Several of them bolting over the gravel: feet at `rate` a second (a constant or a curve), uneven, going off
    duller and quieter as they run."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(float(rc[0]), 1))
    while t < length:
        b.at(t, foot(rng), rng.uniform(-9, 0))
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 1))
    y = b.x
    if away:
        L = len(y) / SR
        y = dsp.sweep_filter(y, "lp", dsp.fit(env([(0, 9000), (L, 2500)], L, "exp"), len(y)), q=0.7)
        y = dsp.shaped(y, [(0, 1), (L, 0.3)], "exp")
    return y


def scatter(rng, k, squeal, foot, drop):
    """A burst of squeals as they bolt: three to six of them going off within a few tenths of a second, each its own
    pitch and length, falling, some nearer and some already further off; their feet scrabbling away over the gravel; on
    the last take a crate dropped as they go."""
    count = (4, 3, 6)[k % 3]
    parts = []
    for i in range(count):
        t = 0.0 if i == 0 else rng.uniform(0.02, 0.4)
        L = rng.uniform(0.25, 0.55)
        top = rng.uniform(1600, 2500)
        m = 3.0 if i == 0 else rng.uniform(4, 12)
        parts.append((t, far(squeal(rng, L, top, top * rng.uniform(0.45, 0.62), i), m), 0 if i == 0 else rng.uniform(-6, -1)))
    end = max(t + len(x) / SR for t, x, _ in parts)
    parts.append((0.02, scrabble(rng, max(end, 0.9), env([(0, 20), (0.2, 45), (end, 25)], max(end, 0.9)), foot), -2))
    if k % 3 == 2:
        parts.append((0.0, drop(rng, k), -4))
    return finish(hp(air(ck.place(parts), rng, 0.1, 0.35), 300, 4), -19, -1.5)


@recipe("cs-pickers", "scatter", "squeals",
        "A burst of squeals as they bolt for their drains: several shrill, hoarse, falling squeals, feet scrabbling off",
        """From real throats: each squeal a real hinge's squeal and the packs' wail held out (paulstretched) and pulled up to
        1.6-2.5 kHz, falling away a half to most of an octave as it runs off, the two a few cents apart so they beat,
        strained into period doubling and made hoarse (the voice flickering 55-85 times a second), a real gasp's breath
        through the teeth: never a cartoon rat's clean 'eek'. Three to six go off within a few tenths of a second, each
        its own pitch and length, some near and some already further off, over their feet scrabbling away on the gravel
        (real gravel cut into small feet, duller and quieter as they go); on one take a crate dropped as they bolt (a real
        wooden knock).""",
        sources=HINGES + [WAIL, GASP, CHOP, BONE] + GRAVEL + CRACKS + WOOD_M + WOOD_L, takes=3, lufs=-19)
def scatter_squeals(rng, k):
    return scatter(rng, k, squeal_rec, foot_rec, knock_rec)


@recipe("cs-pickers", "scatter", "bolt",
        "A burst of squeals as they bolt, modelled: small strained throats squealing and falling, claws scrabbling off",
        """Modelled: each squeal a glottis at 1.6-2.5 kHz falling away a half to most of an octave, jittery and breaking
        into period doubling, through a small mouth on 'eh' opening to 'ah', a second throat a few cents off it in a mouth
        of its own size so they beat, breath hissing through the teeth, hoarse. Three to six within a few tenths of a
        second, each its own pitch, length and distance, over claws and grit scrabbling away; on one take a slatted crate
        dropped as they bolt.""",
        takes=3, lufs=-19)
def scatter_bolt(rng, k):
    return scatter(rng, k, lambda r, L, top, bot, i: squeal_model(r, L, top, bot), foot_model, knock_model)


# ---- cs-pickers: down the drain -----------------------------------------------------------------------------------------------

def pipe(rng, x):
    """Heard down the drain: rung through the pipe's hollow (its cross modes, from 280 Hz up), three quick echoes off its
    walls, muffled by the ground over it."""
    y = mix(x * 0.5, unit(dsp.resonate(x, [f * rng.uniform(0.95, 1.05) for f in PIPE], q=7)) * 0.8)
    b = Bus(len(y) / SR + 0.05)
    for d, db in ((0.0, 0.0), (0.006, -6), (0.013, -10), (0.021, -15)):
        b.at(d, y, db)
    return lp(hp(b.x, 200, 4), 2600)


def sunk_knock_rec(rng, take):
    """The crate knocking on the pipe's wall as it goes down: a real wooden knock and a real plank's hollow one."""
    k = ck.choke(dsp.vari(hit_of(WOOD_M[take % 5], 0, 0.2), rng.uniform(0, 2)), 0.02, 0.04)
    p = ck.choke(dsp.vari(hit_of(PLANK[(take + 2) % 5], 0, 0.25), rng.uniform(1, 3)), 0.03, 0.05)
    return mix(unit(hp(k, 200)), unit(hp(p, 220)) * 0.6)


def claws_iron_rec(rng, L):
    """Claws dragged over the grating's iron as it squeezes through: a real blade drawn fast, pitched up and cut short."""
    x = dsp.vari(ck.get(DRAG[int(rng.integers(2))]), rng.uniform(2, 5))
    a = int(rng.integers(0, max(1, len(x) - samples(L))))
    return unit(hp(dsp.fit(x[a:], samples(L)), 1500)) * env([(0, 0), (0.01, 1), (L, 0)], L)


def claws_iron_model(rng, L):
    """Claws dragged over iron, modelled: fast stick-slip rung through a bar's high modes."""
    return ck.friction(rng, L, rng.uniform(300, 450), 1500, 7000, 0.6, env([(0, 0), (0.01, 1), (L, 0)], L))


def down(rng, k, lift, claws, foot, clank, knock, run):
    """Three ways down: squeezed through, the grating rattling and settling, the crate knocking once on the pipe below; a
    longer squeeze, a mutter of chitter coming up the pipe after it; knocking twice as it goes."""
    R = (0.3, 0.4, 0.32)[k % 3] * rng.uniform(0.9, 1.1)
    parts = [(0.0, lift(rng, R), 0), (0.03, claws(rng, rng.uniform(0.08, 0.14)), -8),
             (0.02, scrabble(rng, R + 0.05, 30, foot, away=False), -5), (R + 0.02, clank(rng, k, 0.5), -9)]
    t = R + rng.uniform(0.12, 0.2)
    parts.append((t, pipe(rng, knock(rng, k)), -2))
    if k % 3 == 2:
        parts.append((t + rng.uniform(0.18, 0.25), pipe(rng, knock(rng, k + 1)), -6))
    if k % 3 == 1:
        parts.append((t + 0.06, pipe(rng, run(rng, 0.2, rng.uniform(1000, 1300), 22)), -10))
    return finish(hp(air(ck.place(parts), rng, 0.08, 0.2), 180, 4), -21, -1.5)


@recipe("cs-pickers", "down", "drain",
        "Down its drain: the grating rattling in its frame as it squeezes through, a scrabble, a hollow knock from the pipe",
        """From real recordings: the grating rattling in its frame as one squeezes through under it (a real loose-metal
        rattle and real latches dropping, cut to their first hundredths), its claws scraping the iron (a real blade drawn
        fast, pitched up) and its feet scrabbling on the stone, the grating settling back with a small clank (a real blow on
        steel, choked), then from below a hollow knock: the crate it took knocking on the pipe's wall (a real wooden knock
        and a plank's), rung through a foot-wide drain pipe and echoing off its walls, muffled by the ground. Takes: one
        knock; a longer squeeze and a mutter of chitter coming up the pipe; knocking twice as it goes.""",
        sources=RATTLE + LATCHES + DRAG + GRAVEL + [CLANK, CHOP, BONE, WAIL, GASP] + RING + WOOD_M + PLANK + CRACKS + HINGES,
        takes=3, lufs=-21)
def down_drain(rng, k):
    return down(rng, k, lift_rec, claws_iron_rec, foot_rec, clank_rec, sunk_knock_rec, chatter_rec)


@recipe("cs-pickers", "down", "pipe",
        "Down its drain, modelled: the grating's bars knocking in its frame, claws on iron, a hollow knock from the pipe",
        """Modelled: the grating's bars knocking against the frame as one squeezes through under it, its claws scraping the
        iron (fast stick-slip through a bar's high modes) and its feet scrabbling, the grating settling back (struck, damped
        short), then from below the crate knocking on the pipe's wall (a slatted crate struck), rung through a foot-wide
        pipe's cross modes, echoing off its walls, muffled by the ground. The same three takes as 'drain'.""",
        takes=3, lufs=-21)
def down_pipe(rng, k):
    return down(rng, k, lift_model, claws_iron_model, foot_model, lambda r, t, s=1.0: clank_model(r, s),
                knock_model, chatter_model)


# ---- cs-pickers: killed by a blow ----------------------------------------------------------------------------------------------

def blow_rec(rng, take):
    """The blow, small: a grain of real wood cracking (a little body's bones going), a real bone-and-wood crack under it,
    a real punch's landing thinned (no weight under 250 Hz)."""
    g = splinters()
    crack = unit(dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(-3, 0)))
    bone = unit(dsp.vari(main_hit(BONE, 0.06), rng.uniform(-3, -1)))
    thud = unit(hp(hit_of(PUNCH[take % 5], 0, 0.12), 250, 4))
    return ck.place([(0, crack, 0), (0.001, bone, -5), (0, thud, -5)])


def blow_model(rng, take):
    """The blow, modelled: a crack through small hard modes (thin bones), a short dull knock of a small body."""
    crack = kit.snap(rng, 0.06, rng.uniform(1800, 2600))
    body = ck.hollow(rng, [rng.uniform(320, 420), rng.uniform(700, 900)], 0.06, q=2.5, hit=0.002)
    return ck.place([(0, unit(crack), 0), (0.001, body, -6)])


def hit(rng, k, squeal, blow, foot):
    """A squeal cut short by the blow landing on it, and its claws twitching on the gravel after. Takes: cut at a
    tenth of a second; struck almost at once; a longer squeal, cut, and a last small one choked off."""
    cut = (0.12, 0.06, 0.17)[k % 3] * rng.uniform(0.9, 1.1)
    top = rng.uniform(2000, 2700)
    sq = ck.choke(squeal(rng, cut + 0.2, top, top * 0.55, k), cut, 0.006)
    parts = [(0.0, sq, 0), (cut - 0.002, blow(rng, k), 2)]
    for i in range(int(rng.integers(2, 5))):
        parts.append((cut + 0.04 + i * rng.uniform(0.03, 0.06), foot(rng), -12 - 2 * i))
    if k % 3 == 2:
        L = 0.07
        parts.append((cut + 0.13, ck.choke(squeal(rng, L, top * 0.7, top * 0.5, k + 1), 0.04, 0.008), -12))
    return finish(hp(air(ck.place(parts), rng, 0.08, 0.2), 300, 4), -19, -1.5)


@recipe("cs-pickers", "hit", "blow",
        "One killed by a blow: a squeal cut short, the blow's small crack on top of it",
        """From real recordings: a squeal starting (a real hinge's squeal and the packs' wail held out and pulled up to 2-2.7
        kHz, strained and hoarse) and cut off a tenth of a second in by the blow landing on it: small (a grain of real
        wood cracking, a real bone-and-wood crack, a real punch's landing with nothing under 250 Hz), its claws twitching
        on the gravel after (real gravel, pitched up). Takes: cut at a tenth of a second; struck almost at once; a longer
        squeal, cut, and a last small one choked off.""",
        sources=HINGES + [WAIL, GASP, BONE, CHOP] + CRACKS + PUNCH + GRAVEL, takes=3, lufs=-19)
def hit_blow(rng, k):
    return hit(rng, k, squeal_rec, blow_rec, foot_rec)


@recipe("cs-pickers", "hit", "cut",
        "One killed by a blow, modelled: a strained small squeal cut short by a small crack",
        """Modelled: a squeal starting (a glottis at 2-2.7 kHz through a small mouth, hoarse, two throats beating) cut off
        by the blow landing on it: a crack through small hard modes (thin bones) and a short dull knock of a small body,
        nothing under 300 Hz; claws twitching on the gravel after. The same three takes as 'blow'.""",
        takes=3, lufs=-19)
def hit_cut(rng, k):
    return hit(rng, k, lambda r, L, top, bot, i: squeal_model(r, L, top, bot), blow_model, foot_model)
