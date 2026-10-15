"""The Mouse Moose heard (queue #537, note 809; A1's #1042, note 790; docs/design/creatures/mouse-moose.md;
src/DarkTerritory.Sim/Enemies/MouseMoose.cs), as tools/audio/cues.py lists it.

What it is decides the build. A mutant rat the size of a dog, with little moose antlers, working a yard at night: it
creeps in out of the dark to a crane's stacked iron casting, fastens its teeth on it and drags it off, very slowly, out of
the hook's reach and then down its hole. A crewmate within 6 m, or a blow, scares it off: it lets go, bolts for its hole,
hides and comes back later. It never hurts anyone and nothing kills it: a pest, eerie but a little comic. So:
- It's a rat, only too big. Its voice is a rat's squeak pitched down for a throat that size (1-2.5 kHz, still a squeak,
  never a dog's yelp or a monster's roar), its teeth chatter after it like a rat's do, and when it strains it grunts
  through its nose with its teeth shut on the iron. Nothing roars, nothing growls, nothing booms.
- Its feet are heavy and slow for a rat: big claws ticking on gravel and cinders a step at a time, uneven, stopping to
  sniff (a wet snuffle). Bolting, the same claws scrabble off fast and are gone.
- Its work is iron and earth: incisors scraping and clicking on a casting; the casting ground over gravel a few
  centimetres at a tug; earth dug and kicked back at its hole, its antlers knocking the rim; the casting tipped down the
  hole into the earth.
- It's distinct from the Pickers (note 771): they're many, quick and dry, chittering in runs and squealing hoarsely in a
  burst; this is one, slow and heavy, a single rat's squeak at a time, wet in the nose, and its loot is cast iron.

Each cue has two candidates. The first is built from the packs' real recordings: real gravel, stones and a real crunch
underfoot for the ground and the earth; real ticks (a blade's chop, wood and bone cracking) cut down for claws and teeth;
a real hinge's squeal stretched and pulled along a rat's squeak, over the packs' real throat (the big-cat roar's single
pulses laid down again at the squeak's pitch, shrunk to a smaller throat) for its voice; a real gasp for its breath and
real wet squelches for its nose; a real metal scrape dropped for heavy iron, a blade drawn across steel for teeth on iron,
real iron struck for the casting, real wood knocks pitched up for its antlers. The second is modelled: a glottis through
a rat's mouth, stick-slip friction, struck modes, grains of grit. Loops are built periodic (events placed round the
cycle, the room and the band hold circular), so build.py's seam gives the cycle back exactly. Every one-shot keeps its
weight out of the squish under 150 Hz (the director's rule); the voices have almost nothing there.
"""

import functools

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import fry, unit
from recipes.boarders import grain
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, R, S, K
from recipes.figures import _pulses, shut, loop as ring_round, band_shape
from recipes.g1_ground import air, far, gravel_grains, struck, wander
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.house_folk import drawn, wet_grain
from recipes.moose_tells import burst
from recipes.pickers import tooth, semis, strain

SR = dsp.SR
TELL = "tell-mouse-moose"
ACTS = "cs-mouse-moose"

# ---- Sources ------------------------------------------------------------------------------------------------------------

GRAVEL = S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02")   # real stones and gravel steps
GRIT = "sfx_100_v2:stones_03"                    # real stones scraping and crackling
EARTH = K("footstep_snow")                       # a real crunch underfoot: packed earth giving
SCRAPE = "sfx_100_v2:misc_10"                    # a real metal scrape in a few strokes: dropped, the casting ground along
KNIVES = R("drawKnife1", "drawKnife2")           # a real blade drawn across steel: incisors scraping on iron
HINGE = R("creak3")[0]                           # a real hinge's squeal: a near-pure stick-slip tone at 1.09 kHz
GASP = "sfx_100_v2:misc_05"                      # a real gasp: air forced through a mouth
ROAR = "sfx_100_v2:misc_24"                      # a real big cat's roar: a real larynx (figures._pulses cuts it)
WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")   # real wet squelches (house_folk.wet_grain cuts them)
TICKS = [R("chop")[0]] + S("misc_33", "misc_34", "misc_35")        # a blade's chop, wood and bone cracking (pickers.tooth)
IRON_L = K("impactMetal_light")                  # light iron struck: a tooth's tick on the casting
IRON_M = K("impactMetal_medium")                 # medium iron struck: the casting knocked
IRON_H = K("impactMetal_heavy")                  # heavy iron struck: the casting landing
WOOD_L = K("impactWood_light")                   # light wood knocks: pitched up, small bone

# ---- Bodies and sizes ------------------------------------------------------------------------------------------------------

HINGE_F = 1090.0            # the hinge's squeal (Hz)
MOUTH = 1.15                # a dog-sized rat's mouth as a share of a child's (synth.TRACT): a little over a child's formants
SHRINK = 3.4                # the roar's pulses shrunk this much: a throat a third the big cat's, its resonances up with it
CASTING = [1.0, 1.48, 2.11, 2.63, 3.42, 4.27, 5.36]   # a thick casting's modes as ratios (jobs.py): close, inharmonic
ANTLER = [1150, 1900, 2850, 4100]                     # a small antler tine knocked: short, dry, bony
TELL_BAND = (400, 7000)     # the tells sit above a standing engine's idle
LOOP_L = 8.0                # the creep's and the drag's cycles (s)


def lead(x, t=0.01):
    """A one-shot with a little silence before it, so build.py's 2 ms fade-in lands before the first sound."""
    return np.concatenate([np.zeros(samples(t), np.float32), np.asarray(x, np.float32)])


def casting(rng, f1=None):
    """The casting's modes, from its lowest (a heavy cast block: 330-420 Hz), struck in a different place each time."""
    f1 = f1 or rng.uniform(330, 420)
    return [f1 * r * rng.uniform(0.97, 1.03) for r in CASTING]


# ---- A real throat, shrunk -------------------------------------------------------------------------------------------------

def pulse_throat(rng, f, shrink, amp=None, avg=10, banks=3, sub=0.0, jit=0.004, over=4):
    """The packs' real throat (the big-cat roar's single pulses, figures._pulses) laid down one per period of `f` (Hz per
    sample): each the average of `avg` real ones, resampled `shrink` times shorter (a smaller throat: its resonances rise
    with it), every other one `sub` quieter (period doubling: a strained throat), the periods a little uneven like a real
    voice's. `amp` a per-sample level. Laid on a grid `over` times finer than the output's and brought down after: at a
    squeak's pitch a period is only 30 samples, and rounding each pulse to a whole sample would rasp."""
    pulses, P = _pulses()
    n = len(f)
    bank = []
    for _ in range(banks):
        idx = rng.choice(len(pulses), min(avg, len(pulses)), replace=False)
        p = np.mean(pulses[idx], axis=0) * np.hanning(2 * P)
        bank.append(dsp.resample(p.astype(np.float32), over / shrink))
    m = max(len(b) for b in bank)
    out = np.zeros(n * over + m, np.float32)
    t, i = float(rng.uniform(0, 4)) * over, 0
    while True:
        a = int(round(t))
        if a >= n * over:
            break
        k = a // over
        g = 1.0 if amp is None else float(amp[k])
        if sub and i % 2:
            g *= 1 - sub
        if g > 1e-4:
            b = bank[int(rng.integers(len(bank)))]
            out[a:a + len(b)] += b * g * rng.uniform(0.95, 1.05)
        t += over * SR / max(float(f[k]), 40.0) * (1 + rng.normal(0, jit))
        i += 1
    return dsp.resample(out, 1 / over)[:n]


@functools.lru_cache(maxsize=1)
def _hinge_core():
    """The real hinge's squeal itself: its tone from the swell to where it's died (75-300 ms)."""
    x = ck.get(HINGE)
    return x[samples(0.075):samples(0.3)].copy()


def hinge_tone(rng, f):
    """The real hinge's squeal pulled along `f` (Hz per sample): stretched (pitch kept) to as much as the pitch line will
    read, its own swell and fade flattened, then varispeeded along the line from its own 1.09 kHz. Not paulstretched: the
    stick-slip's own regular grain is what makes it a squeak."""
    s = semis(f, HINGE_F)
    core = _hinge_core()
    need = float(np.sum(2 ** (s / 12))) / SR + 0.04
    y = dsp.stretch(core, need / (len(core) / SR), smooth=True)
    a = np.abs(y)
    e = np.maximum(lp(a, 8), lp(a[::-1], 8)[::-1])     # both ways, so neither edge lags and overshoots
    y = y / (e + 0.05 * np.max(e) + 1e-9)
    return unit(dsp.fit(bend(y, s), len(f)))


def mouth(x, vowels, k=MOUTH, q=(4.0, 5.0, 5.0), gains=(1.0, 0.8, 0.45)):
    """An animal's mouth: synth.tract's formants (a vowel or [(t, vowel)], gliding) at `k` times a child's, but broad: a
    high voice has few harmonics, and through narrow formants each one swells and dies as it crosses them."""
    n = len(x)
    if isinstance(vowels, str):
        vowels = [(0.0, vowels)]
    ts = [t for t, _ in vowels] + [n / SR]
    out = np.zeros(n, np.float32)
    for i in range(3):
        fs = [synth.VOWELS[v][0][i] * k for _, v in vowels]
        fc = np.interp(np.arange(n) / SR, ts, fs + [fs[-1]]).astype(np.float32)
        out += gains[i] * dsp.sweep_filter(x, "bp", fc, q=q[i], block=128)
    return unit(out)


# ---- Its squeak ------------------------------------------------------------------------------------------------------------

def squeak_line(rng, L, top, kind):
    """A squeak's pitch (Hz per sample). 'arch': up into its top at once, held, sagging away; 'fall': in at the top and
    falling; 'rise': climbing to the end and tipping over, as if it asked. A quick flutter in it, never still."""
    if kind == "rise":
        pts = [(0, top * 0.8), (L * 0.75, top), (L, top * 0.9)]
    elif kind == "fall":
        pts = [(0, top * 0.92), (0.015, top), (L * 0.35, top * 0.93), (L, top * 0.7)]
    else:
        pts = [(0, top * 0.84), (min(0.04, L * 0.25), top), (L * 0.55, top * 0.97), (L, top * 0.78)]
    c = env(pts, L, "exp")
    return (c * 2 ** (wander(rng, len(c), 16, 0.35) / 12)).astype(np.float32)


def squeak_env(L):
    """In at once, held a moment, fading as it sags."""
    return env([(0, 0), (0.008, 1), (L * 0.3, 0.9), (L * 0.75, 0.55), (L, 0)], L)


def squeak_rec(rng, L, top, kind="arch", rough=0.15, mouth_open=1.0):
    """A big rat's squeak from real recordings: a real hinge's squeal stretched and pulled along the squeak's pitch for
    its tone; under it the packs' real throat laid down at that pitch, shrunk to a dog-sized rat's and shaped by its
    mouth going 'ee' to 'eh' (or held near shut, `mouth_open` 0); a real gasp's breath through it; strained a little into
    period doubling (`rough`), so it's an animal's and not a hinge's."""
    f = squeak_line(rng, L, top, kind)
    n = samples(L)
    th = pulse_throat(rng, f, SHRINK * rng.uniform(0.9, 1.1), sub=rough)
    th = mouth(th, [(0, "i"), (L * 0.45, "e" if mouth_open > 0.5 else "i"), (L, "e")])
    br = unit(lp(hp(drawn(rng, GASP, L, rng.uniform(2, 5), 0.05), 2500), 8000))
    y = mix(hinge_tone(rng, f) * 0.85, unit(th) * 0.45, br * 0.08)
    y = strain(dsp.fit(y, n), f, rough * 0.7)
    return unit(lp(hp(y, 700, 4), 9000)) * squeak_env(L)


def squeak_model(rng, L, top, kind="arch", rough=0.15, mouth_open=1.0):
    """A big rat's squeak, modelled: a glottis at the squeak's pitch (a smooth, near-round pulse, so the first harmonics
    carry it, a little jitter, period doubling `rough`), half through a dog-sized rat's mouth going 'ee' to 'eh' and half
    straight out, breathy."""
    f = squeak_line(rng, L, top, kind)
    gl = synth.glottis(f, L, rng, jitter=0.01, shimmer=0.12, oq=0.75, sub=rough * 0.7, rough=rough * 0.4)
    v = mouth(gl, [(0, "i"), (L * 0.45, "e" if mouth_open > 0.5 else "i"), (L, "e")])
    br = bp(synth.noise(L, rng), 2500, 7000)
    y = mix(v * 0.55, unit(lp(gl, 4500)) * 0.85, unit(br) * 0.05)
    return unit(lp(hp(y, 700, 4), 9000)) * squeak_env(L)


# ---- Its teeth --------------------------------------------------------------------------------------------------------------

def incisor_rec(rng):
    """Its incisors meeting: two real ticks a hair apart (a blade's chop, bone and wood cracking, cut to their first
    hundredth), barely pitched, since they're big teeth for a rat, and the long jaw's small dry knock behind them."""
    jaw = ck.hollow(rng, [rng.uniform(700, 900), rng.uniform(1600, 1900)], 0.025, q=4, hit=0.001)
    return ck.place([(0, tooth(rng, (-2, 3)), 0), (rng.uniform(0.002, 0.005), tooth(rng, (-3, 1)), -6), (0, jaw, -14)])


def incisor_model(rng):
    """Its incisors meeting, modelled: two hard enamel clicks a hair apart, the jaw's small dry knock behind them."""
    f = rng.uniform(2200, 3300)
    jaw = ck.hollow(rng, [rng.uniform(700, 900), rng.uniform(1600, 1900)], 0.025, q=4, hit=0.001)
    return ck.place([(0, unit(synth.click(f, q=rng.uniform(8, 12), length=0.014, rng=rng)), 0),
                     (rng.uniform(0.002, 0.005), unit(synth.click(f * rng.uniform(0.8, 0.9), q=8, length=0.01, rng=rng)), -6),
                     (0, jaw, -14)])


def chatter(rng, incisor, count, rate):
    """A rat's teeth chattering: `count` quick meetings `rate` a second, uneven, trailing off."""
    parts, t = [], 0.0
    for i in range(count):
        parts.append((t, incisor(rng), -1.5 * i * rng.uniform(0.6, 1.2) + rng.uniform(-3, 0)))
        t += rng.uniform(0.8, 1.25) / rate
    return ck.place(parts)


# ---- Its feet ---------------------------------------------------------------------------------------------------------------

def foot_rec(rng, w=1.0):
    """One heavy paw set down on gravel and cinders, from real recordings: a crunch of real gravel cut to a few
    hundredths (barely pitched: a dog's weight on it, not a rat's), the stones settling under the weight a moment after
    (a second, smaller crunch, dropped), its big claws catching the stones (real ticks, one to three a few milliseconds
    apart), the pad's dull weight under it; now and then a claw dragged a little over the stones as the foot's placed
    (real stones scraping)."""
    crunch = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(0.025, 0.05), rng.uniform(-2, 2))
    settle = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], rng.uniform(0.02, 0.035), rng.uniform(-5, -2))
    parts = [(0, unit(hp(crunch, 350)), 0), (rng.uniform(0.015, 0.03), unit(hp(settle, 350)), -9 + 3 * np.log2(w)),
             (0, ck.pad(rng, 0.045, 600), -11 + 4 * np.log2(w))]
    t = rng.uniform(0, 0.004)
    for i in range(int(rng.integers(1, 4))):
        parts.append((t, tooth(rng, (-4, 1)), rng.uniform(-9, -3) - 3 * i))
        t += rng.uniform(0.003, 0.012)
    if rng.random() < 0.3:
        L = rng.uniform(0.03, 0.06)
        s = grain(rng, GRIT, L, rng.uniform(-1, 3), attack=False)
        parts.append((rng.uniform(0.0, 0.01), unit(hp(s, 1500)), -12))
    return ck.place(parts)


def foot_model(rng, w=1.0):
    """One heavy paw on gravel, modelled: its big claws' clicks catching the stones, a handful of grit breaking under it
    and a few more settling after, the pad's dull weight."""
    claws = [(rng.uniform(0, 0.012), unit(synth.click(rng.uniform(1800, 3500), q=rng.uniform(6, 10), length=0.012, rng=rng)),
              rng.uniform(-9, -3)) for _ in range(int(rng.integers(1, 4)))]
    crunch = ck.grains(rng, int(rng.integers(6, 13)), 0.03, 800, 5000, q=(3, 8), length=(0.004, 0.014))
    settle = ck.grains(rng, int(rng.integers(3, 7)), 0.03, 600, 3000, q=(3, 8), length=(0.004, 0.014))
    return ck.place([(0, unit(crunch), 0), (rng.uniform(0.015, 0.03), unit(settle), -9 + 3 * np.log2(w)),
                     (0, ck.pad(rng, 0.045, 600), -11 + 4 * np.log2(w))] + claws)


# ---- Its nose ----------------------------------------------------------------------------------------------------------------

def sniff_rec(rng):
    """A wet snuffling sniff, from real recordings: three to six quick sniffs in through the nose (a real gasp drawn
    short and pitched up into a small nose, swelling as it draws and cut off), a real wet squelch's tick at the end of
    most (the wet of the nose), sometimes a short snort out to finish."""
    parts, t = [], 0.0
    for i in range(int(rng.integers(3, 7))):
        d = rng.uniform(0.045, 0.085)
        x = drawn(rng, GASP, d + 0.02, rng.uniform(2, 5), 0.02)[:samples(d)]
        x = dsp.peak(unit(bp(x, 1100, 7000)), rng.uniform(2200, 2900), 2.0, 6)
        parts.append((t, unit(x) * env([(0, 0), (d * 0.7, 1), (d, 0)], d), rng.uniform(-4, 0) - 0.8 * i))
        if rng.random() < 0.75:
            parts.append((t + d * rng.uniform(0.5, 0.95), unit(hp(wet_grain(rng, (3, 9)), 900)), rng.uniform(-14, -7)))
        t += d + rng.uniform(0.04, 0.09)
    if rng.random() < 0.5:
        d = rng.uniform(0.07, 0.11)
        x = drawn(rng, GASP, d + 0.02, rng.uniform(-1, 2), 0.02)[:samples(d)]
        parts.append((t + 0.03, unit(bp(x, 700, 5000)) * env([(0, 0), (0.01, 1), (d, 0)], d), -5))
        parts.append((t + 0.04, unit(hp(wet_grain(rng, (0, 4)), 700)), -10))
    return ck.place(parts)


def sniff_model(rng):
    """A wet snuffling sniff, modelled: quick sniffs in through a small nose ('h' to 'ee'), a few small bubbles bursting
    in the wet of it, sometimes a short snort out."""
    parts, t = [], 0.0
    for i in range(int(rng.integers(3, 7))):
        d = rng.uniform(0.045, 0.085)
        sh = env([(0, 0), (d * 0.7, 1), (d, 0)], d)
        x = synth.breath(d, [(0, "h"), (d, "i")], 1.4, rng, shape=sh)
        parts.append((t, unit(bp(x, 1100, 7000)), rng.uniform(-4, 0) - 0.8 * i))
        if rng.random() < 0.75:
            b = synth.bubbles(0.03, 120, 1200, 3500, rng, rise=(0.1, 0.3))
            parts.append((t + d * rng.uniform(0.5, 0.9), unit(b), rng.uniform(-16, -9)))
        t += d + rng.uniform(0.04, 0.09)
    if rng.random() < 0.5:
        d = rng.uniform(0.07, 0.11)
        sh = env([(0, 0), (0.01, 1), (d, 0)], d)
        parts.append((t + 0.03, unit(bp(synth.breath(d, [(0, "e"), (d, "h")], 1.4, rng, shape=sh), 700, 5000)), -5))
    return ck.place(parts)


# ---- Its grunt ---------------------------------------------------------------------------------------------------------------

def grunt_env(L):
    """A grunt's shape: snatched in, pushed, let go."""
    return env([(0, 0), (0.006, 1), (L * 0.35, 0.8), (L, 0)], L)


def grunt_rec(rng, L, f0):
    """A short grunt as it braces, its teeth shut on the iron, from real recordings: the real throat (the roar's pulses)
    laid down at a small creature's grunt (270-360 Hz), shrunk less than the squeak's and rattling with effort (the
    pulses gated 32-44 times a second, unevenly), half through closed lips and half out round its teeth; the breath
    forced out of its nose with it (a real gasp, darkened). Nothing under 180 Hz."""
    f = env([(0, f0 * 1.15), (L * 0.2, f0), (L, f0 * 0.85)], L, "exp")
    a = grunt_env(L)
    v = pulse_throat(rng, f, 1.9, amp=a, avg=4, sub=0.35)
    v = v * (0.35 + 0.65 * fry(rng, rng.uniform(32, 44), L, jitter=0.45, decay=0.008)[:len(v)])
    huff = unit(bp(drawn(rng, GASP, L, rng.uniform(-2, 1), 0.04), 300, 2500)) * a
    y = mix(unit(shut(v)) * 0.7, unit(lp(hp(v, 500), 2200)) * 0.45, huff * 0.2)
    return unit(hp(lp(y, 3000), 180, 4))


def grunt_model(rng, L, f0):
    """A short grunt as it braces, modelled: a rough glottis (period doubling, a growl's flutter of effort) half through a
    closed mouth and half out round its teeth, the breath forced out of its nose with it. Nothing under 180 Hz."""
    f = env([(0, f0 * 1.15), (L * 0.2, f0), (L, f0 * 0.85)], L, "exp")
    a = grunt_env(L)
    g = synth.glottis(f, L, rng, jitter=0.025, shimmer=0.25, oq=0.6, sub=0.4, rough=0.8)
    g = g * (0.35 + 0.65 * fry(rng, rng.uniform(32, 44), L, jitter=0.45, decay=0.008)[:len(g)])
    v = synth.tract(g, [(0, "m"), (L * 0.5, "u")], 1.0, breath=0.3, rng=rng) * a
    huff = unit(synth.breath(L, [(0, "h"), (L, "u")], 1.2, rng, shape=a))
    y = mix(unit(shut(v)) * 0.7, unit(lp(hp(v, 500), 2200)) * 0.45, unit(bp(huff, 300, 2500)) * 0.2)
    return unit(hp(lp(y, 3000), 180, 4))


# ---- tell-mouse-moose: creep (a loop) --------------------------------------------------------------------------------------

def creep_plan(rng, L):
    """How it creeps for a cycle of L s: three or four goes of a step at a time, each a second or two, with a pause
    between (most of them with a sniff). Every gap a little different; the lot scaled to fill exactly one cycle."""
    count = int(rng.integers(3, 5))
    moves = rng.uniform(1.0, 1.9, count)
    pauses = rng.uniform(0.5, 1.3, count)
    k = L / (np.sum(moves) + np.sum(pauses))
    out, t = [], rng.uniform(0.1, 0.3)
    for m, p in zip(moves * k, pauses * k):
        out.append((t, m, p))
        t += m + p
    return out


def creep(rng, foot, sniff):
    """The creep, one cycle: in each go, a paw set down every 0.25-0.5 s (a heavy, cautious walk: the hind feet heavier,
    a pair now and then close together), uneven and louder and quieter as it noses about; in the pauses a sniff (or a
    paw shifted where it stands)."""
    L = LOOP_L
    n = samples(L)
    sway = W.slow(n, 0.5, rng)
    ev = []
    for t0, m, p in creep_plan(rng, L):
        t, i = t0, 0
        while t < t0 + m:
            w = 1.3 if i % 2 else 1.0
            db = 2.5 * float(sway[samples(t) % n]) + rng.uniform(-5, 0) + (1.5 if i % 2 else 0.0)
            ev.append((t, foot(rng, w), dsp.db2a(db)))
            t += rng.uniform(0.07, 0.14) if rng.random() < 0.25 else rng.uniform(0.25, 0.5)
            i += 1
        if rng.random() < 0.75:
            ev.append((t0 + m + rng.uniform(0.1, 0.25), sniff(rng), dsp.db2a(rng.uniform(-4, -1))))
        else:
            ev.append((t0 + m + p * rng.uniform(0.3, 0.6), foot(rng, 0.7), dsp.db2a(-8)))
    return ring_round(W.place(n, ev), n, rng, wet=0.1, shape=band_shape(*TELL_BAND))


@recipe(TELL, "creep", "cinders",
        "Creeping in toward a casting: big claws ticking slowly over gravel and cinders, stopping to sniff, wet",
        """From real recordings: each paw a crunch of real gravel cut to a few hundredths and barely pitched (a dog's
        weight on it, not a rat's) and the stones settling under it a moment after, its big claws catching the stones
        (real ticks, a blade's chop and bone cracking cut down, one to three a few milliseconds apart), the pad's dull
        weight under it, now and then a claw dragged over the stones (real stones scraping). It creeps a step at a time,
        every quarter to half a second, uneven, in three or four goes of a second or two; between them it stops and
        sniffs: three to six quick wet sniffs in through the nose (a real gasp drawn short and pitched up into a small
        nose, real wet squelches' ticks for the wet of it), sometimes a short snort out. Heavy and cautious, a few
        metres off in the yard. An 8 s loop, held to 400 Hz-7 kHz, low.""",
        sources=GRAVEL + TICKS + [GASP] + WET, takes=1, loop=True, band=TELL_BAND, lufs=-24, seconds=LOOP_L)
def creep_cinders(rng, k):
    return creep(rng, foot_rec, sniff_rec)


@recipe(TELL, "creep", "claws",
        "Creeping in, modelled: big claws clicking on grit a step at a time, a pause, a wet snuffle",
        """Modelled: each paw its big claws' clicks catching the stones (one to three), a handful of grit breaking under it
        and the pad's dull weight; the same creep as 'cinders' (a step every quarter to half a second, uneven, three or
        four goes with pauses between), and in the pauses a sniff: quick breaths in through a small nose with small
        bubbles bursting in the wet of it. An 8 s loop, held to 400 Hz-7 kHz, low.""",
        takes=1, loop=True, band=TELL_BAND, lufs=-24, seconds=LOOP_L)
def creep_claws(rng, k):
    return creep(rng, foot_model, sniff_model)


# ---- tell-mouse-moose: squeak ------------------------------------------------------------------------------------------------

def squeak_take(rng, k, voice, incisor):
    """Four ways it squeaks: one arched 'eek'; a quick double 'ee-eek'; a longer one falling away, plaintive; a short one
    rising as if it asked. Each a big rat's squeak (1.2-1.75 kHz at its top: a real rat's is twice that and more), its
    teeth chattering after it: three to six quick meetings, 11-16 a second."""
    kind = k % 4
    top = rng.uniform(1200, 1750)
    if kind == 0:
        L = rng.uniform(0.26, 0.34)
        parts = [(0, voice(rng, L, top, "arch", 0.15), 0)]
    elif kind == 1:
        a, b = rng.uniform(0.09, 0.12), rng.uniform(0.2, 0.26)
        gap = rng.uniform(0.03, 0.05)
        parts = [(0, voice(rng, a, top * 0.92, "arch", 0.1, 0.0), -3), (a + gap, voice(rng, b, top, "arch", 0.15), 0)]
        L = a + gap + b
    elif kind == 2:
        L = rng.uniform(0.42, 0.5)
        parts = [(0, voice(rng, L, top * 1.05, "fall", 0.22), 0)]
    else:
        L = rng.uniform(0.2, 0.25)
        parts = [(0, voice(rng, L, top, "rise", 0.12), 0)]
    t = L + rng.uniform(0.03, 0.09)
    parts.append((t, chatter(rng, incisor, int(rng.integers(3, 7)), rng.uniform(11, 16)), -9))
    return finish(lead(hp(air(ck.place(parts), rng, 0.06, 0.2), 400, 4)), -22, -1.5)


@recipe(TELL, "squeak", "hinge",
        "A rat's squeak, too big a rat: a single 'eek' pitched low for a dog-sized throat, its teeth chattering after",
        """From real recordings: the squeak's tone a real hinge's squeal (a stick-slip whistle at 1.1 kHz) stretched
        and pulled along a rat's squeak: up into its top at once (1.2-1.75 kHz, about half a real rat's) and sagging away, a
        quick flutter in it; under it the packs' real throat (the big-cat roar's single pulses laid down again at the
        squeak's pitch and shrunk to a throat a third the size) through a rat's mouth going 'ee' to 'eh', a real gasp's
        breath in it, strained a little so it's an animal's and not a hinge's. After it its teeth chatter: three to six
        quick meetings of its incisors (real ticks, a blade's chop and bone cracking cut down), 11-16 a second. Takes: one
        arched 'eek'; a quick double 'ee-eek'; a longer one falling away, plaintive; a short one rising as if it asked.
        Held to 400 Hz-7 kHz.""",
        sources=[HINGE, ROAR, GASP] + TICKS, takes=4, band=TELL_BAND, lufs=-22)
def squeak_hinge(rng, k):
    return squeak_take(rng, k, squeak_rec, incisor_rec)


@recipe(TELL, "squeak", "throat",
        "A big rat's squeak, modelled: a small tight voice through a rat's mouth, 'eek', then its teeth chattering",
        """Modelled: a glottis at the squeak's pitch (1.2-1.75 kHz at its top, a small tight voice, a little jitter and a
        touch of period doubling) through a dog-sized rat's mouth going 'ee' to 'eh', breathy; then its teeth chattering,
        hard enamel clicks in pairs, 11-16 a second, trailing off. The same four takes as 'hinge'. Held to 400 Hz-7 kHz.""",
        takes=4, band=TELL_BAND, lufs=-22)
def squeak_throat(rng, k):
    return squeak_take(rng, k, squeak_model, incisor_model)


# ---- cs-mouse-moose: bite ----------------------------------------------------------------------------------------------------

def tink_rec(rng, take):
    """An incisor meeting the casting, from real recordings: a real light iron hit pitched up and choked at once (the
    tooth's tick on the iron; the casting's too heavy to ring), a real tick of enamel on it, a little of the casting's own
    dull ring under it (a real medium iron hit, dropped and choked)."""
    t = ck.choke(dsp.vari(hit_of(IRON_L[take % 5], 0, 0.08), rng.uniform(2, 6)), 0.005, 0.012)
    body = ck.choke(dsp.vari(hit_of(IRON_M[(take + 2) % 5], 0, 0.15), rng.uniform(-4, -2)), 0.02, 0.04)
    return ck.place([(0, unit(hp(t, 1000)), 0), (0, tooth(rng, (-2, 2)), -4), (0, unit(hp(body, 300, 4)), -14)])


def tink_model(rng, take=0):
    """An incisor meeting the casting, modelled: a hard click through the iron's high modes, choked, and the casting's
    close, dull modes barely rung under it."""
    t = ck.choke(struck(rng, [f * rng.uniform(0.95, 1.05) for f in (2600, 3900, 5600)], 30, 0.04, 0.0004), 0.004, 0.01)
    body = ck.choke(struck(rng, casting(rng), 20, 0.12, 0.001), 0.02, 0.04)
    return ck.place([(0, t, 0), (0, unit(synth.click(rng.uniform(2400, 3400), q=10, length=0.012, rng=rng)), -5),
                     (0, unit(hp(body, 300, 4)), -14)])


def gnash_rec(rng, L):
    """Its incisors scraping over the iron for a grip: a real blade drawn across steel, a slice of it pitched down a
    little for bigger, blunter teeth, swelling and dying."""
    x = dsp.vari(ck.get(KNIVES[int(rng.integers(2))]), rng.uniform(-4, -1))
    a = int(rng.integers(0, max(1, len(x) - samples(L) - samples(0.05))))
    y = unit(hp(dsp.fit(x[a:], samples(L)), 1200))
    return y * env([(0, 0), (L * 0.2, 1), (L * 0.7, 0.7), (L, 0)], L)


def gnash_model(rng, L):
    """Its incisors scraping over the iron, modelled: fast stick-slip (300-450 slips a second) rung through the iron's
    high modes, swelling and dying."""
    sh = env([(0, 0), (L * 0.2, 1), (L * 0.7, 0.7), (L, 0)], L)
    y = ck.friction(rng, L, rng.uniform(300, 450), 1400, 7000, 0.6)
    y = mix(y, unit(dsp.resonate(y, [f * rng.uniform(0.95, 1.05) for f in (2600, 3900, 5600)], q=25)) * 0.5)
    return unit(y) * sh


def bite(rng, k, tink, gnash, grunt, foot):
    """Three bites on a casting: teeth on the iron, click-click, a scrape for a grip and a grunt as it braces; a scrape
    first (the teeth slipping) and then the clamp; a click, a scrape, a click again as it takes a better grip and two
    short grunts. Its paws shuffle on the gravel as it sets its weight back."""
    f0 = rng.uniform(270, 360)
    kind = k % 3
    if kind == 0:
        g = rng.uniform(0.1, 0.16)
        parts = [(0, tink(rng, k), 0), (rng.uniform(0.02, 0.04), tink(rng, k + 1), -3),
                 (0.05, gnash(rng, g), -8)]
        tg = 0.05 + g + rng.uniform(0.0, 0.04)
        parts.append((tg, grunt(rng, rng.uniform(0.14, 0.2), f0), -2))
    elif kind == 1:
        g = rng.uniform(0.16, 0.24)
        parts = [(0, gnash(rng, g), -6), (g - 0.02, tink(rng, k), 0), (g + rng.uniform(0.015, 0.03), tink(rng, k + 2), -4)]
        tg = g + rng.uniform(0.05, 0.08)
        parts.append((tg, grunt(rng, rng.uniform(0.16, 0.24), f0), -2))
    else:
        g = rng.uniform(0.12, 0.18)
        parts = [(0, tink(rng, k), -1), (0.03, gnash(rng, g), -7), (0.03 + g, tink(rng, k + 3), 0)]
        tg = 0.03 + g + rng.uniform(0.03, 0.05)
        parts += [(tg, grunt(rng, rng.uniform(0.1, 0.13), f0), -3),
                  (tg + rng.uniform(0.16, 0.2), grunt(rng, rng.uniform(0.12, 0.16), f0 * rng.uniform(1.03, 1.1)), -2)]
    for i in range(int(rng.integers(1, 3))):
        parts.append((tg + rng.uniform(0.0, 0.12), foot(rng, 1.2), rng.uniform(-14, -10)))
    return finish(lead(hp(air(ck.place(parts), rng, 0.08, 0.2), 200, 4)), -20, -1.5)


@recipe(ACTS, "bite", "iron",
        "Its teeth fastened on a casting: incisors clicking and scraping on iron, a short grunt as it braces",
        """From real recordings: each incisor meeting the casting a real light iron hit pitched up and choked at once (the
        casting's far too heavy to ring) with a real tick of enamel on it and a little of the casting's dull ring under it
        (a real iron hit dropped and choked); the teeth scraping for a grip a real blade drawn across steel, pitched down a
        little for bigger, blunter teeth. Then a short grunt as it braces, through its nose because its teeth are shut on
        the iron: the packs' real throat (the roar's single pulses) laid down at 270-360 Hz, rattling with effort, through
        closed lips, the air hissing past its teeth (a real gasp); its paws shuffle on the gravel as it sets its weight
        back (real gravel). Takes: click-click, a scrape and a grunt; a scrape that slips and then the clamp; click, scrape,
        click again for a better grip and two short grunts. About half a second.""",
        sources=IRON_L + IRON_M + KNIVES + [ROAR, GASP] + TICKS + GRAVEL, takes=3, lufs=-20)
def bite_iron(rng, k):
    return bite(rng, k, tink_rec, gnash_rec, grunt_rec, foot_rec)


@recipe(ACTS, "bite", "grip",
        "Its teeth on a casting, modelled: hard clicks and a scrape of enamel on iron, a grunt through the nose",
        """Modelled: each incisor meeting the casting a hard click through iron's high modes, choked, the casting's close,
        dull modes barely rung under it; the teeth scraping for a grip fast stick-slip through the same iron; the grunt a
        rough little glottis (period doubling, a flutter of effort) through closed lips, air hissing past the teeth; its
        paws shuffling on grit. The same three takes as 'iron'.""",
        takes=3, lufs=-20)
def bite_grip(rng, k):
    return bite(rng, k, tink_model, gnash_model, grunt_model, foot_model)


# ---- cs-mouse-moose: drag (a loop) -------------------------------------------------------------------------------------------

def tugs(rng, L):
    """When it tugs over a cycle of L s: every 0.6-1.2 s, unevenly, the gaps scaled to come round. The cycle starts just
    before a tug's claws, so its seam falls in a pause."""
    gaps = []
    while sum(gaps) < L - 0.6:
        gaps.append(rng.uniform(0.6, 1.2))
    gaps = np.array(gaps) * L / sum(gaps)
    return list(np.concatenate([[0.0], np.cumsum(gaps)[:-1]]) + 0.17), list(gaps)


def speed(L):
    """How fast the casting moves through one tug: snatched into motion, slowing, stopped."""
    return env([(0, 0), (L * 0.15, 1), (L * 0.55, 0.75), (L, 0)], L) ** 1.2


def window(rng, key, L, semis, active=None):
    """A slice of a real recording L s long, varispeeded by `semis`, from somewhere in its first `active` s."""
    x = dsp.vari(ck.get(key), semis)
    end = len(x) if active is None else min(len(x), samples(active * 2 ** (-semis / 12)))
    a = int(rng.integers(0, max(1, end - samples(L))))
    return dsp.fit(x[a:], samples(L))


def grind_rec(rng, L):
    """One tug of the casting over the gravel, from real recordings: a real metal scrape dropped most of an octave for
    heavy cast iron and rung a little through the casting, real stones grinding and crackling under it, real gravel
    ground down under the weight (dropped half an octave) and shoved ahead of it (as it is); a dull knock as the weight
    is snatched into motion (a real iron hit, dropped and choked), a soft settle as it stops."""
    v = speed(L)
    modes = casting(rng)
    s = window(rng, SCRAPE, L, rng.uniform(-9, -6), 0.45)
    s = mix(unit(s), unit(dsp.resonate(s, modes, q=7)) * 0.25)
    g = window(rng, GRIT, L, rng.uniform(-4, -1), 0.5)
    crush = lp(gravel_grains(rng, L, 40 + 220 * v, (-10, -6), (0.02, 0.05)), 2500)
    roll = gravel_grains(rng, L, 30 + 160 * v, (-3, 2))
    jolt = ck.choke(dsp.vari(hit_of(IRON_M[int(rng.integers(5))], 0, 0.2), rng.uniform(-7, -4)), 0.02, 0.04)
    b = Bus(L + 0.2)
    b.at(0, unit(s) * v, 0)
    b.at(0, unit(g) * v ** 0.7, -5)
    b.at(0, crush * v, -4)
    b.at(0, roll * v ** 0.7, -9)
    b.at(0, unit(hp(jolt, 250, 4)), -9)
    b.at(L * 0.92, ck.pad(rng, 0.07, 500), -14)
    return b.x


def grind_model(rng, L):
    """One tug of the casting over gravel, modelled: iron juddering over the stones as it catches and skips (stick-slip,
    60-110 slips a second, quicker as it moves faster, rung through the casting's close modes), iron rubbing between the
    slips, stones breaking and rolling under it; a knock of the casting's modes as it's snatched, a soft settle."""
    v = speed(L)
    n = samples(L)
    modes = casting(rng)
    judder = synth.creak(L, 60 + 50 * v, rng, body=modes, q=10, jitter=0.6, grit=0.7)
    rub = W.friction(n, rng, modes, rough=1.5, grit=0.3, q=12)
    stones = ck.grains(rng, int(120 * L), L, 700, 5000, q=(3, 8), length=(0.004, 0.015), shape=0.0)
    knock = ck.choke(struck(rng, modes, 12, 0.1, 0.002), 0.02, 0.04)
    b = Bus(L + 0.2)
    b.at(0, unit(hp(judder, 250)) * v, 0)
    b.at(0, unit(hp(rub, 250)) * v, -8)
    b.at(0, unit(dsp.fit(stones, n)) * v ** 0.7, -5)
    b.at(0, unit(hp(knock, 250, 4)), -9)
    b.at(L * 0.92, ck.pad(rng, 0.07, 500), -14)
    return b.x


def huff_rec(rng):
    """A quick breath out through its nose between tugs: a real gasp, short, darkened."""
    d = rng.uniform(0.07, 0.12)
    x = drawn(rng, GASP, d + 0.02, rng.uniform(0, 3), 0.02)[:samples(d)]
    return unit(bp(x, 800, 5000)) * env([(0, 0), (0.012, 1), (d, 0)], d)


def huff_model(rng):
    """A quick breath out through its nose, modelled."""
    d = rng.uniform(0.07, 0.12)
    sh = env([(0, 0), (0.012, 1), (d, 0)], d)
    return unit(bp(synth.breath(d, [(0, "e"), (d, "h")], 1.4, rng, shape=sh), 800, 5000))


def drag(rng, grind, squeak, grunt, foot, huff):
    """The drag, one cycle: a tug every 0.6-1.2 s. Before each its claws scrabble for purchase; then the casting grinds
    a little way over the gravel (0.25-0.45 s) and stops, and with most tugs the rat strains, a squeak through its
    clenched teeth or a grunt; then a pause, a breath out through its nose in some."""
    L = LOOP_L
    n = samples(L)
    starts, gaps = tugs(rng, L)
    ev = []
    f0 = rng.uniform(270, 340)
    for t, gap in zip(starts, gaps):
        room = L - 0.04 - t           # what's left of the cycle: the last tug and its voice end before the seam
        D = min(rng.uniform(0.25, 0.45), gap * 0.6, room)
        for j in range(int(rng.integers(2, 4))):
            ev.append((t - 0.13 + j * rng.uniform(0.03, 0.06), foot(rng, 1.3), dsp.db2a(rng.uniform(-11, -6))))
        ev.append((t, grind(rng, D), dsp.db2a(rng.uniform(-2, 0))))
        r = rng.random()
        if r < 0.55:
            sl = min(rng.uniform(0.14, 0.26), room - 0.08)
            sq = lp(squeak(rng, sl, rng.uniform(950, 1300), ("arch", "fall")[int(rng.integers(2))], 0.35, 0.0), 4500)
            ev.append((t + rng.uniform(0.02, 0.08), sq, dsp.db2a(rng.uniform(0, 3))))
        elif r < 0.9:
            gl = min(rng.uniform(0.12, 0.2), room - 0.05)
            ev.append((t + rng.uniform(0.0, 0.05), grunt(rng, gl, f0 * rng.uniform(0.95, 1.08)), dsp.db2a(rng.uniform(0, 2))))
        if rng.random() < 0.5 and gap - D > 0.4 and D + 0.3 < room:
            ev.append((t + D + rng.uniform(0.08, 0.15), huff(rng), dsp.db2a(-16)))
    return ring_round(W.place(n, ev), n, rng, wet=0.08, shape=band_shape(170, 9000))


@recipe(ACTS, "drag", "gravel",
        "Dragging a casting off, very slowly: iron grinding over gravel in short tugs, the rat straining between",
        """From real recordings: a tug every 0.6-1.2 s, unevenly. Before each its claws scrabble for purchase in the gravel
        (real gravel and ticks); then the casting grinds a little way over the stones and stops (a real metal scrape
        dropped most of an octave for heavy cast iron and rung a little through a casting's modes, real stones grinding
        and crackling under it, real gravel ground down under its weight and shoved ahead of it, a dull knock as it's
        snatched into motion, a soft settle as it stops). With most tugs the rat strains: a short squeak through its
        clenched teeth (the squeak's real hinge and real throat, pitched lower, strained) or a grunt through its nose (the
        real throat, rattling with effort). Between tugs a pause, now and then a breath out through its nose (a real gasp).
        An 8 s loop, nothing much under 170 Hz.""",
        sources=[SCRAPE, GRIT, HINGE, ROAR, GASP] + GRAVEL + IRON_M + TICKS, takes=1, loop=True, lufs=-20, seconds=LOOP_L)
def drag_gravel(rng, k):
    return drag(rng, grind_rec, squeak_rec, grunt_rec, foot_rec, huff_rec)


@recipe(ACTS, "drag", "judder",
        "Dragging a casting, modelled: iron juddering over stones in short tugs, a strained squeak or grunt with each",
        """Modelled: the same tugs as 'gravel' (every 0.6-1.2 s, claws scrabbling for purchase first): the casting juddering
        over the stones as it catches and skips (stick-slip rung through a casting's close, inharmonic modes), iron rubbing
        between the slips, stones breaking and rolling under it, a knock as it's snatched and a soft settle; the rat's
        strained squeak (a tight glottis through its mouth, nearly shut) or its grunt (a rough glottis through closed lips)
        with most tugs; a breath out between some. An 8 s loop, nothing much under 170 Hz.""",
        takes=1, loop=True, lufs=-20, seconds=LOOP_L)
def drag_judder(rng, k):
    return drag(rng, grind_model, squeak_model, grunt_model, foot_model, huff_model)


# ---- cs-mouse-moose: bolt ----------------------------------------------------------------------------------------------------

def gallop(rng, D, foot):
    """Bolting off over the gravel: bounding strides 5-6 a second, four paws to a stride (the forefeet a hair apart, the
    hind pair landing ahead of them), little stones kicked ticking after, going off: quieter and duller as it goes."""
    b = Bus(D + 0.1)
    rate = rng.uniform(5.0, 6.2)
    t = 0.0
    while t < D:
        fore = rng.uniform(0.012, 0.03)
        hind = rng.uniform(0.06, 0.09)
        for o in (0.0, fore, hind, hind + rng.uniform(0.01, 0.025)):
            if rng.random() < 0.08:
                continue
            b.at(t + o + rng.normal(0, 0.003), foot(rng, 1.0), rng.uniform(-5, 0))
        if rng.random() < 0.6:
            b.at(t + rng.uniform(0.04, 0.15), unit(hp(gravel_grains(rng, 0.05, 120, (2, 6), (0.006, 0.012)), 1200)),
                 rng.uniform(-20, -12))
        t += (1 / rate) * rng.uniform(0.9, 1.1)
    y = b.x
    T = len(y) / SR
    y = dsp.sweep_filter(y, "lp", dsp.fit(env([(0, 9000), (T, 2000)], T, "exp"), len(y)), q=0.7)
    return dsp.shaped(y, [(0, 1), (T, 0.15)], "exp")


def launch(rng, foot, L=0.14):
    """Its claws spinning on the gravel as it whips round to go: a quick scrabble, the stones sprayed."""
    b = Bus(L + 0.1)
    for _ in range(int(rng.integers(7, 12))):
        b.at(rng.uniform(0, L), foot(rng, 0.9), rng.uniform(-8, 0))
    b.at(0, unit(hp(gravel_grains(rng, L, 220, (0, 4), (0.006, 0.015)), 900)), -8)
    return b.x


def let_go_rec(rng, take):
    """The casting let go: its teeth sliding off the iron (a real blade drawn, a flick of it) and the casting rocking
    back onto the gravel (a real medium iron hit, dropped and choked, real gravel under it)."""
    flick = gnash_rec(rng, 0.06)
    rock = ck.choke(dsp.vari(hit_of(IRON_M[take % 5], 0, 0.2), rng.uniform(-6, -3)), 0.025, 0.04)
    crunch = grain(rng, GRAVEL[int(rng.integers(len(GRAVEL)))], 0.05, rng.uniform(-6, -3))
    return ck.place([(0, flick, -6), (0.03, unit(hp(rock, 250, 4)), 0), (0.03, unit(hp(crunch, 300)), -6)])


def let_go_model(rng, take=0):
    """The casting let go, modelled: a flick of stick-slip off the iron, the casting's modes knocked as it rocks back,
    grit under it."""
    rock = ck.choke(struck(rng, casting(rng), 12, 0.12, 0.002), 0.025, 0.04)
    grit = ck.grains(rng, 10, 0.04, 700, 4000)
    return ck.place([(0, gnash_model(rng, 0.06), -6), (0.03, unit(hp(rock, 250, 4)), 0), (0.03, unit(grit), -8)])


def bolt(rng, k, squeal, foot, let_go):
    """Three ways it bolts: a squeal and off; a squeal as it goes and a last smaller one further off; the casting let go
    first (when it's scared off one it's holding), then the squeal and off. Each a shrill squeal (the squeak's own voice
    driven up to 1.9-2.5 kHz, falling), its claws spinning on the gravel as it whips round, then a scrabble of claws
    bolting fast and going off."""
    kind = k % 3
    parts = []
    t = 0.0
    if kind == 2:
        parts.append((0.0, let_go(rng, k), -3))
        t = rng.uniform(0.04, 0.07)
    L = rng.uniform(0.3, 0.42)
    top = rng.uniform(1900, 2500)
    parts.append((t, squeal(rng, L, top, "fall", rng.uniform(0.25, 0.35)), -3))
    t0 = t + rng.uniform(0.03, 0.07)
    parts.append((t0, launch(rng, foot), 1))
    D = rng.uniform(0.55, 0.75)
    parts.append((t0 + 0.1, gallop(rng, D, foot), 2))
    if kind == 1:
        L2 = rng.uniform(0.12, 0.16)
        parts.append((t0 + rng.uniform(0.35, 0.45), far(squeal(rng, L2, top * 0.9, "fall", 0.3), 10), -6))
    return finish(lead(hp(air(ck.place(parts), rng, 0.1, 0.3), 250, 4)), -18, -1.5)


@recipe(ACTS, "bolt", "squeal",
        "Scared off: a shrill rat's squeal and a scrabble of claws bolting away over the gravel, gone",
        """From real recordings: a shrill squeal (the squeak's real hinge and real throat driven up to 1.9-2.5 kHz, falling
        away, strained), its claws spinning on the gravel as it whips round (real gravel and ticks in a quick scrabble,
        stones sprayed), then bolting: bounding strides five or six a second, four paws to a stride, little stones kicked
        ticking after, quieter and duller as it goes off into the dark. Takes: a squeal and off; a squeal and a last small
        one further off; the casting let go first (its teeth sliding off the iron, a real blade's flick, and the casting
        rocking back onto the gravel, a real iron hit choked), then the squeal and off. About a second.""",
        sources=[HINGE, ROAR, GASP] + GRAVEL + TICKS + KNIVES + IRON_M, takes=3, lufs=-18)
def bolt_squeal(rng, k):
    return bolt(rng, k, squeak_rec, foot_rec, let_go_rec)


@recipe(ACTS, "bolt", "scrabble",
        "Scared off, modelled: a strained squeal, claws scrabbling on grit and running off, receding",
        """Modelled: a shrill squeal (the squeak's glottis driven up to 1.9-2.5 kHz, falling, strained) through its mouth,
        claws and grit scrabbling as it whips round, then bounding off, four paws a stride, five or six strides a second,
        duller and quieter as it goes. The same three takes as 'squeal' (the casting let go a flick of stick-slip and the
        casting's modes knocked as it rocks back).""",
        takes=3, lufs=-18)
def bolt_scrabble(rng, k):
    return bolt(rng, k, squeak_model, foot_model, let_go_model)


# ---- cs-mouse-moose: dig ------------------------------------------------------------------------------------------------------

def earth_grains(rng, length, rate, size=(0.008, 0.025), semis=(-6, -1)):
    """Real earth and stones (a real crunch underfoot, real stones) cut into grains and strewn at `rate` a second (a
    constant or a curve): soil raked and falling."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(float(rc[0]), 1))
    keys = EARTH + GRAVEL[:3]
    while t < length:
        g = grain(rng, keys[int(rng.integers(len(keys)))], rng.uniform(*size), rng.uniform(*semis), attack=False)
        b.at(t, unit(g), 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5))
        t += rng.exponential(1 / max(float(rc[min(samples(t), n - 1)]), 1))
    return unit(dsp.fit(b.x, n))


def stroke_rec(rng):
    """One stroke of a forepaw into the earth, from real recordings: its claws raking through soil and stones (a real
    crunch underfoot and real stones cut into grains, dropped), a claw's tick on a stone (a real tick)."""
    L = rng.uniform(0.04, 0.07)
    sh = env([(0, 0), (L * 0.15, 1), (L, 0)], L)
    r = earth_grains(rng, L, 500, (0.006, 0.02), (-5, 0)) * sh
    return ck.place([(0, unit(hp(r, 300)), 0), (rng.uniform(0, 0.01), tooth(rng, (-5, -1)), -12)])


def stroke_model(rng):
    """One stroke of a forepaw into the earth, modelled: soil's dry hiss through a moving band, grit breaking, a claw's
    click on a stone."""
    L = rng.uniform(0.04, 0.07)
    sh = env([(0, 0), (L * 0.15, 1), (L, 0)], L)
    soil = dsp.sweep_filter(synth.noise(L, rng, "pink"), "bp", env([(0, 600), (L, 1800)], L, "exp"), q=1.2) * sh
    grit = dsp.fit(ck.grains(rng, int(rng.integers(6, 12)), L, 600, 4000, q=(2, 6)), samples(L)) * sh
    claw = synth.click(rng.uniform(1800, 3000), q=8, length=0.01, rng=rng)
    return ck.place([(0, unit(soil), -2), (0, unit(grit), 0), (rng.uniform(0, 0.01), unit(claw), -12)])


def clod(rng):
    """A clod of what it kicked back landing: a soft dull knock and a few grains patter."""
    c = burst(rng, rng.uniform(0.012, 0.03), 300, 1500, attack=0.08)
    c = unit(c * np.exp(-np.linspace(0, 5, len(c))).astype(np.float32))
    grit = ck.grains(rng, int(rng.integers(2, 6)), 0.04, 900, 3500)
    return lp(ck.place([(0, c, 0), (rng.uniform(0, 0.02), unit(grit), -8)]), 2500)


def antler_rec(rng, take):
    """An antler knocking the hole's rim, from real recordings: a real light wood knock pitched up most of an octave and
    more, choked (a small tine of bone), a real tick on its front (bone's hardness), a pinch of earth knocked off the rim."""
    k = ck.choke(dsp.vari(hit_of(WOOD_L[take % 5], 0, 0.1), rng.uniform(8, 11)), 0.01, 0.015)
    return ck.place([(0, unit(hp(k, 500, 4)), 0), (0, tooth(rng, (-6, -2)), -5),
                     (0.004, unit(hp(earth_grains(rng, 0.05, 150, (0.004, 0.012), (-2, 2)), 600)), -14)])


def antler_model(rng, take=0):
    """An antler knocking the rim, modelled: a short burst through a small tine's dry modes, a hard tick on its front."""
    k = struck(rng, [f * rng.uniform(0.9, 1.1) for f in ANTLER], 9, 0.05, 0.0008)
    tick = unit(synth.click(rng.uniform(2500, 3500), q=8, length=0.008, rng=rng))
    return ck.place([(0, ck.choke(k, 0.01, 0.015), 0), (0, tick, -6),
                     (0.004, unit(ck.grains(rng, 4, 0.03, 900, 3500)), -14)])


def dig(rng, k, stroke, antler):
    """Three ways in: a run of digging, the antlers knock-knock on the rim as it squeezes its head in, and the last of it
    muffled in the hole; a short dig, a knock, more digging, a second knock and gone; a longer dig with a pause, then two
    knocks close together and a little earth trickling in after it. Each run is forepaw strokes 9-13 a second, the earth
    kicked back landing behind it in clods."""
    kind = k % 3
    b = Bus(2.0)

    def run(t, D, db=0.0, muffled=False):
        rate = rng.uniform(9, 13)
        s = t
        while s < t + D:
            x = stroke(rng)
            b.at(s, lp(x, 1400 if muffled else 5000), db - 3 + rng.uniform(-4, 0))
            if not muffled and rng.random() < 0.5:
                b.at(s + rng.uniform(0.12, 0.25), clod(rng), db - rng.uniform(10, 16))
            s += rng.uniform(0.8, 1.2) / rate
        return t + D

    if kind == 0:
        e = run(0.0, rng.uniform(0.45, 0.6))
        k1 = e + rng.uniform(0.02, 0.05)
        b.at(k1, antler(rng, k), 2)
        b.at(k1 + rng.uniform(0.11, 0.16), antler(rng, k + 1), 0)
        run(k1 + 0.22, rng.uniform(0.2, 0.3), -8, True)
    elif kind == 1:
        e = run(0.0, rng.uniform(0.25, 0.35))
        b.at(e + 0.02, antler(rng, k), 2)
        e = run(e + 0.08, rng.uniform(0.25, 0.35), -2)
        b.at(e + 0.02, antler(rng, k + 2), 1)
        run(e + 0.1, rng.uniform(0.15, 0.22), -9, True)
    else:
        e = run(0.0, rng.uniform(0.3, 0.38))
        e = run(e + rng.uniform(0.1, 0.15), rng.uniform(0.22, 0.28))
        b.at(e + 0.03, antler(rng, k), 2)
        b.at(e + 0.03 + rng.uniform(0.06, 0.09), antler(rng, k + 3), -1)
        T = 0.3
        b.at(e + 0.15, unit(hp(earth_grains(rng, T, env([(0, 80), (T, 4)], T), (0.006, 0.02), (-3, 1)), 400)), -16)
    return finish(lead(hp(air(b.x, rng, 0.08, 0.25), 200, 4)), -22, -1.5)


@recipe(ACTS, "dig", "earth",
        "Into its hole: earth scrabbled and kicked back, its antlers knocking twice on the rim, gone",
        """From real recordings: its forepaws dig, 9-13 strokes a second, each its claws raking soil and stones (a real
        crunch underfoot and real stones cut into grains and dropped) with a claw's tick on a stone, the earth it kicks
        back landing behind it in soft clods and pattering grit. As it squeezes its head in its little antlers knock twice
        on the hole's rim: small dry bony knocks (a real light wood knock pitched up most of an octave and more, choked, a
        real tick on its front, a pinch of earth knocked off). Takes: a run of digging, knock-knock, the last of it muffled in
        the hole; a short dig, a knock, more digging, a second knock; a longer dig with a pause, two knocks close together
        and a little earth trickling in after. About a second.""",
        sources=EARTH + GRAVEL[:3] + TICKS + WOOD_L, takes=3, lufs=-22)
def dig_earth(rng, k):
    return dig(rng, k, stroke_rec, antler_rec)


@recipe(ACTS, "dig", "rake",
        "Into its hole, modelled: claws raking dry soil, clods kicked back, two small bony knocks on the rim",
        """Modelled: each stroke of a forepaw soil's dry hiss through a moving band with grit breaking in it and a claw's
        click, 9-13 a second, the earth kicked back landing in clods; the antlers' two knocks a short burst through a small
        tine's dry modes (1.1-4 kHz), a hard tick on each. The same three takes as 'earth'.""",
        takes=3, lufs=-22)
def dig_rake(rng, k):
    return dig(rng, k, stroke_model, antler_model)


# ---- cs-mouse-moose: lost ------------------------------------------------------------------------------------------------------

def tip_rec(rng, L):
    """The casting dragged over the hole's edge: the iron grinding over the rim (a real metal scrape dropped for heavy
    iron, real stones crackling), the rim's earth crumbling under it."""
    v = env([(0, 0), (L * 0.3, 1), (L * 0.8, 0.8), (L, 0)], L)
    s = window(rng, SCRAPE, L, rng.uniform(-9, -6), 0.45)
    g = window(rng, GRIT, L, rng.uniform(-4, -1), 0.5)
    crumble = earth_grains(rng, L, 60 + 160 * v, (0.008, 0.025), (-5, -1))
    return mix(unit(s) * v * 0.8, unit(g) * v ** 0.7 * 0.5, crumble * v * 0.5)


def tip_model(rng, L):
    """The casting dragged over the edge, modelled: stick-slip through its modes, grit and earth breaking under it."""
    v = env([(0, 0), (L * 0.3, 1), (L * 0.8, 0.8), (L, 0)], L)
    j = synth.creak(L, 50 + 50 * v, rng, body=casting(rng), q=10, jitter=0.6, grit=0.7)
    g = ck.grains(rng, int(140 * L), L, 600, 4000, q=(2, 6), length=(0.004, 0.015), shape=0.0)
    return mix(unit(hp(j, 250)) * v, unit(dsp.fit(g, samples(L))) * v * 0.5)


def thud_rec(rng, take, w=1.0):
    """The casting landing in the earth at the bottom of the hole, from real recordings: a real heavy iron hit, dropped
    and choked almost at once (earth doesn't let iron ring), a real crunch underfoot dropped for the earth taking the
    weight, the dull knock of the mass (kept over 150 Hz)."""
    iron = ck.choke(dsp.vari(hit_of(IRON_H[take % 5], 0, 0.25), rng.uniform(-3, -1)), 0.02, 0.03)
    earth = dsp.vari(hit_of(EARTH[(take + 1) % 5], 0, 0.25), rng.uniform(-5, -3))
    mass = ck.hollow(rng, [rng.uniform(150, 180), rng.uniform(310, 380)], 0.12, q=2.5, hit=0.004)
    return ck.place([(0, unit(hp(iron, 180, 4)), -3 + 2 * np.log2(w)), (0, unit(hp(earth, 90)), 0),
                     (0, unit(hp(mass, 90)), -3 + 3 * np.log2(w))])


def thud_model(rng, take=0, w=1.0):
    """The casting landing in the earth, modelled: its modes struck and damped at once, a burst of soil breaking (grit
    and low grains), the mass's dull knock."""
    iron = ck.choke(struck(rng, casting(rng, rng.uniform(280, 340)), 10, 0.2, 0.004), 0.02, 0.03)
    soil = ck.grains(rng, 40, 0.06, 250, 2500, q=(1.5, 4), length=(0.006, 0.02))
    mass = ck.hollow(rng, [rng.uniform(150, 180), rng.uniform(310, 380)], 0.12, q=2.5, hit=0.004)
    return ck.place([(0, unit(hp(iron, 180, 4)), -3 + 2 * np.log2(w)), (0, unit(hp(soil, 120)), -2),
                     (0, unit(hp(mass, 90)), -2 + 3 * np.log2(w))])


def trickle(rng, T, grains_fn):
    """Loose earth running down after it: grains thinning out, a little clod or two."""
    y = grains_fn(rng, T)
    parts = [(0, unit(lp(hp(y, 400), 3500)), 0)]
    for _ in range(int(rng.integers(1, 3))):
        parts.append((rng.uniform(0.05, T * 0.6), clod(rng), -6))
    return ck.place(parts)


def trickle_rec(rng, T):
    return earth_grains(rng, T, env([(0, 110), (T * 0.4, 30), (T, 3)], T), (0.006, 0.02), (-3, 2))


def trickle_model(rng, T):
    return dsp.fit(ck.grains(rng, int(60 * T), T, 700, 4500, q=(2, 6), length=(0.004, 0.012), shape=1.5), samples(T))


def lost(rng, k, tip, thud, grains_fn):
    """Two ways down: the casting ground over the edge, it tips and drops into the earth, loose earth running in after
    it; tipped over quicker, landing hard and rocking once (a second, smaller knock), earth after it."""
    T = rng.uniform(0.2, 0.3) if k % 2 == 0 else rng.uniform(0.12, 0.18)
    drop = T + rng.uniform(0.07, 0.12)
    parts = [(0, tip(rng, T), -2), (drop, thud(rng, k, 1.0), 0)]
    if k % 2 == 1:
        parts.append((drop + rng.uniform(0.1, 0.15), thud(rng, k + 1, 0.5), -9))
    tr = rng.uniform(0.4, 0.55)
    parts.append((drop + 0.03, trickle(rng, tr, grains_fn), -11))
    return finish(lead(hp(air(ck.place(parts), rng, 0.08, 0.25), 80, 4)), -18, -1.5)


@recipe(ACTS, "lost", "thud",
        "A casting dragged down its hole: the iron tipping over the edge and thudding into the earth, earth after it",
        """From real recordings: the casting dragged over the hole's edge (a real metal scrape dropped for heavy cast iron,
        real stones crackling, the rim's earth crumbling: a real crunch underfoot cut into grains), a moment's drop, then
        it lands in the earth at the bottom: a real heavy iron hit, dropped and choked almost at once (earth doesn't let
        iron ring), a real crunch underfoot dropped for the earth taking the weight, the dull knock of the mass, its weight
        kept over 150 Hz; then loose earth running in after it, thinning out, a little clod or two. Takes: ground over the
        edge and down; tipped over quicker, landing hard and rocking once.""",
        sources=[SCRAPE, GRIT] + EARTH + GRAVEL[:3] + IRON_H, takes=2, lufs=-18)
def lost_thud(rng, k):
    return lost(rng, k, tip_rec, thud_rec, trickle_rec)


@recipe(ACTS, "lost", "drop",
        "A casting down its hole, modelled: iron juddering over the edge, a dull damped knock into earth, grains after",
        """Modelled: the casting juddering over the rim (stick-slip through its close, inharmonic modes, earth and grit
        breaking under it), then landing in the earth: its modes struck and damped at once, a burst of soil breaking, the
        mass's dull knock over 150 Hz; loose earth running in after it as grains thinning out. The same two takes as
        'thud'.""",
        takes=2, lufs=-18)
def lost_drop(rng, k):
    return lost(rng, k, tip_model, thud_model, trickle_model)
