"""The Moose's tells (spec A.4; G1's design, docs/design/creatures/moose.md §4): a corrupted bull moose by the line.

What it is: 2.4 m at the shoulder and the better part of a tonne, a mineralized 3.2 m rack dragging its head low, a pale
hairless "ghost moose" hide crusted with engorged tick sacs from grape to plum size, a swollen dewlap, split black
hooves. Docile until bothered, then a straight-line charge. Everything it does is heard before it's seen, outdoors at
15-40 m, so each tell is built from what a real bull moose does plus what's wrong with this one, and held in a band the
train bed (heavy at 60 Hz-1 kHz) can't cover:

- Grazing (200 Hz-3 kHz): browse torn off willow, birch and sedge (leaves stripped, stems snapping, the branch springing
  back); a ruminant's slow chewing, molars grinding fibre at a steady 1.2 a second; a tick sac's wet rubbery creak as the
  hide shifts; now and then a content closed-mouth grunt. Calm and even on purpose: its stopping is the next tell.
- Warning: the bull's rut "cough", a short hollow explosive grunt (hunters liken it to an axe going into wood), 150 Hz-
  1.5 kHz; its molars clacking in slowing runs (2-4 kHz); a split hoof raked back through turf and grit, the clods thrown
  behind it (400 Hz-4 kHz).
- Squaring up: a hoof stamp per take (the code plays two), the two toes cracking down and the ground answering; a snort
  through big nostrils whose flaps flutter in the blast.
- The charge: a heavy transverse gallop, four falls a stride at 1.7 strides a second (slower and far heavier than a
  hound's), two hooves on the ballast shoulder and two on the verge; its breath locked to the stride (a grunt out as the
  forelegs land, a wheezing whistle in through an airway as sick as its hide); brush and saplings breaking as it comes.

The living parts are synthesised (throats and breath through a long tract, stick-slip, grains, flutter); the ground and
the wood are real recordings, bent and granulated. No howl, no fluttering croak, no slow breathing at rest, no rasping
gnaw, no clatter of claws: none of it may sound like another creature's tell (A.1).
"""

import numpy as np
from scipy import signal

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import kit

SR = dsp.SR

GRAZE = (200, 3000)
COUGH = (150, 1500)
CLACK = (2000, 4000)
DRAG = (400, 4000)
STAMP = (150, 3000)
SNORT = (300, 4000)
GALLOP = (150, 3500)
WHEEZE = (500, 4000)
BRUSH = (400, 5000)

TRACT = 0.34      # a bull moose's vocal tract as a share of a child's (synth.TRACT): longer than a horse's
STRIDE = 0.6      # its gallop at 11 m/s: 1.7 strides a second, a big ungulate's
LOOP = 12.0       # 20 strides: the hooves and the wheeze share it, so started together they stay in step
FALLS = (0.0, 0.085, 0.235, 0.305)   # a transverse gallop: hind, hind, fore, fore, then the flight

SNOW = [f"kenney_impact-sounds:footstep_snow_{i:03d}" for i in range(5)]     # a real crunch underfoot
STONES = ["sfx_100_v2:stones_01", "sfx_100_v2:stones_02", "sfx_100_v2:stones_03"]
PLANK = [f"kenney_impact-sounds:impactPlank_medium_{i:03d}" for i in range(5)]  # a hollow wooden knock
CRACK = "sfx_100_v2:misc_35"           # green wood breaking
DRY = ["sfx_100_v2:misc_34", "kenney_rpg-audio:clothBelt2"]                 # brittle cracks and crunches
ROAR = "sfx_100_v2:misc_24"
SIGH = "sfx_100_v2:misc_25"
SNEEZE = "sfx_100_v2:misc_05"
LEATHER = "kenney_rpg-audio:handleSmallLeather"
CHOP = "kenney_rpg-audio:chop"
WIRE = [f"kenney_impact-sounds:impactMetal_light_{i:03d}" for i in range(5)]
GLASS = [f"kenney_impact-sounds:impactGlass_light_{i:03d}" for i in range(5)]


# ---- Shared parts ----------------------------------------------------------------------------------------------------

def norm(x):
    return (x / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def burst(rng, length, lo=None, hi=None, attack=0.5):
    """A short noise burst with a raised-cosine envelope; `attack` its rise as a share of its length."""
    n = max(samples(length), 4)
    x = rng.standard_normal(n).astype(np.float32)
    a = max(1, int(n * attack))
    e = np.ones(n, np.float32)
    e[:a] = np.sin(np.linspace(0, np.pi / 2, a)) ** 2
    e[a:] = np.cos(np.linspace(0, np.pi / 2, n - a)) ** 2
    x = x * e
    if lo and hi:
        return bp(x, lo, hi)
    return lp(x, hi) if hi else (hp(x, lo) if lo else x)


def ring(x, modes, q, tail=0.0, gains=None):
    """An excitation rung through a body's modes, with room for the ring to die away."""
    return dsp.resonate(np.concatenate([x, np.zeros(samples(tail), np.float32)]), modes, q=q, gains=gains)


def clip(key, start=None, length=0.2, semis=0.0, pre=0.004):
    """A slice of a recording from its first strong attack (or from `start` s), varispeeded, with short fades."""
    x = src.get(key)
    if start is None:
        start = max(0.0, int(np.argmax(np.abs(x) > 0.3 * np.max(np.abs(x)))) / SR - pre)
    y = dsp.trim(x, start, length)
    if semis:
        y = dsp.vari(y, semis)
    return dsp.fade(y, 0.002, min(0.03, len(y) / SR / 3))


def held(y, lo, hi, drive=6):
    """Hold a tell to its band with its loudest peaks rounded off (a soft clip between two passes of the band filter),
    so it carries its level in its body, not in a few spikes."""
    return dsp.band(dsp.saturate(norm(dsp.band(y, lo, hi)), drive), lo, hi)


def air(x, rng, wet=0.12, tail=0.5):
    """Open country by the line at night: a little of its reverb, the tail cut short so a one-shot fired again doesn't
    pile up a wash."""
    x = dsp.trim_silence(x, -60)
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def cycle(x, length, rng, band, drive=6, wet=0.1):
    """A loop of exactly `length` s. Its reverb rings on into its own head (dsp.wrap); the band hold runs three times
    round and keeps the middle, so the filter's edges never land on the seam; then its head is appended, weighted so
    build.py's 0.3 s equal-power seam sums back to exactly that head. What comes out is the cycle itself at its full
    length, so a jaw or a gait keeps its beat across the seam."""
    n = samples(length)
    c = dsp.wrap(dsp.room(x, "night", wet=wet, rng=rng), n)
    c = held(np.concatenate([c, c, c]), *band, drive=drive)[n:2 * n]
    k = samples(0.3)
    w = np.tan(np.pi / 4 - np.linspace(0, np.pi / 2, k) / 2)    # sin t + w cos t = 1
    return np.concatenate([c, c[:k] * w]).astype(np.float32)


def flutter(rng, rate, length, jitter=0.3, decay=0.008):
    """Soft tissue flapping in a blast of air (a nostril's folds, a throat's fry): an uneven train of pulses at `rate` a
    second (a constant or a curve), each a quick swell that dies."""
    n = samples(length)
    rc = synth.curve(rate, n)
    e = np.zeros(n, np.float32)
    t = rng.uniform(0, 0.01)
    while samples(t) < n:
        a = samples(t)
        e[a] = rng.uniform(0.4, 1.0)
        t += (1 / rc[a]) * max(0.3, 1 + jitter * rng.standard_normal() * 0.5)
    k = np.arange(samples(decay * 5)) / SR
    return norm(signal.fftconvolve(e, ((1 - np.exp(-k / 0.0015)) * np.exp(-k / decay)).astype(np.float32))[:n])


def sac(x, f=170, q=5, wet=0.5):
    """The swollen dewlap and the chest above it: a big soft cavity the voice booms in."""
    r = dsp.resonate(x, [f, f * 2.3, f * 4.1], q=q, gains=[1, 0.6, 0.3])
    return (norm(x) * (1 - wet) + norm(r) * wet).astype(np.float32)


def leaves(rng, length, rate, lo=700, hi=3000, dur=(0.002, 0.02), alpha=2.0):
    """Green leaves torn, stripped or thrashed: grains with a sharp onset and a quick decay, Poisson in time at `rate` a
    second (a constant or a curve), loudness from a power law so a few crack out of many quiet ones. That spread is what
    keeps it a crackle of leaves rather than a hiss."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = synth.curve(rate, n)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(*dur)) + 8
        fc = np.exp(rng.uniform(np.log(lo), np.log(hi)))
        g = rng.standard_normal(k).astype(np.float32) * np.exp(-np.linspace(0, rng.uniform(3, 7), k)).astype(np.float32)
        g = bp(g, fc * 0.7, min(fc * 1.4, SR * 0.46))
        m = min(k, n - a)
        out[a:a + m] += g[:m] * min(0.5, 0.05 * rng.pareto(alpha) + 0.03)
    return norm(out)


def stems(rng, length, rate, f=(1000, 2600), dull=4000):
    """Leaf stalks and green twigs giving one after another: small snaps at `rate` a second (a constant or a curve), a
    few loud and many quiet, dulled because they're green."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.12)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        if t >= length:
            break
        s = kit.snap(rng, rng.uniform(0.025, 0.06), rng.uniform(*f))
        b.at(t, norm(lp(s, dull)), -16 * rng.random() ** 1.5)
    return b.x


def slosh(rng, length, lo=250, hi=900, rate=7.0):
    """Fluid shifting inside a taut sac: a dark noise that wobbles as it sloshes (6-9 a second), a few tissue ticks.
    No rising bubbles (those read as a cartoon bloop)."""
    n = synth.noise(length, rng, "pink")
    y = bp(n, lo, hi) * (0.55 + 0.45 * np.sin(2 * np.pi * rate * np.arange(samples(length)) / SR + rng.uniform(0, 6)))
    y = y * env([(0, 0), (length * 0.2, 1), (length, 0)], length)
    tick = synth.crackle(length, 90, rng, size=(0.0002, 0.0007), hi=lo) * env([(0, 1), (length, 0)], length)
    return norm(mix(norm(y), norm(lp(tick, hi * 2)) * 0.25))


def toes(rng, hard=1.0):
    """A split hoof landing: its two black toes strike a few milliseconds apart, keratin on ground (hard dull ticks, not
    claws)."""
    b = Bus(0.06)
    for i in range(2):
        f = rng.uniform(1300, 2400)
        x = burst(rng, rng.uniform(0.0004, 0.001), attack=0.1)
        y = ring(x, [f, f * rng.uniform(1.5, 1.8), f * 0.6], q=rng.uniform(5, 9), tail=0.025)
        b.at(i * rng.uniform(0.004, 0.012), norm(y), -i * rng.uniform(2, 5))
    return b.x * hard


def thock(rng, f=200, length=0.12):
    """The earth taking a tonne through a hoof: a dull low knock, a short burst rung through a few low damped modes."""
    x = burst(rng, 0.004, 80, 1500, attack=0.1)
    y = ring(x, [f, f * rng.uniform(2.0, 2.3), f * rng.uniform(3.6, 4.1)], q=4, tail=length, gains=[1, 0.6, 0.3])
    return dsp.shaped(norm(y), [(0, 1), (length, 0.01)], "exp")


def grains(rng, keys, length, rate, size=(0.008, 0.03), semis=(-3, 3)):
    """Real ground (crunches, stones) cut into grains and strewn along `length` at `rate` a second (a constant or a
    curve): a scrape made of the ground itself."""
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1.0))
        if t >= length:
            break
        x = src.get(keys[int(rng.integers(len(keys)))])
        gl = rng.uniform(*size)
        a = rng.uniform(0, max(0.0, len(x) / SR - gl))
        g = dsp.vari(dsp.trim(x, a, gl), rng.uniform(*semis))
        b.at(t, g * np.hanning(len(g)).astype(np.float32), rng.uniform(-10, 0))
    return b.x


# ---- Grazing: its presence beside the line (200 Hz-3 kHz) ------------------------------------------------------------

def browse_willow(rng):
    """Lips close round a willow stem and the head pulls back along it, stripping the leaves (a crackling rip, the
    stalks snapping off one after another, slowing); the bare stem springs back and its last leaves shake."""
    b = Bus(2.0)
    b.at(0.0, dsp.shaped(leaves(rng, 0.22, 250), [(0, 0), (0.1, 1), (0.22, 0)]), -12)
    t, pull = 0.18, rng.uniform(0.35, 0.55)
    b.at(t, dsp.shaped(leaves(rng, pull, env([(0, 1200), (pull, 300)], pull), 600, 2800, (0.002, 0.03)),
                       [(0, 0), (0.03, 1), (pull * 0.7, 0.7), (pull, 0)]), -5)
    b.at(t, stems(rng, pull, env([(0, 70), (pull, 12)], pull)), 0)
    b.at(t + pull, kit.whoosh(rng, 0.3, 300, 1600, 0.25), -14)
    b.at(t + pull + 0.04, dsp.shaped(leaves(rng, 0.5, 200), [(0, 1), (0.5, 0)], "exp"), -10)
    return b.x


def browse_sapling(rng):
    """A birch sapling bitten and wrenched: the stem bends (a green-wood creak), gives with a real crack, the leaves torn
    with it; the top whips back."""
    b = Bus(2.0)
    b.at(0.0, dsp.shaped(leaves(rng, 0.25, 300), [(0, 0), (0.12, 1), (0.25, 0.3)]), -12)
    bend = rng.uniform(0.18, 0.3)
    body = [f * rng.uniform(2.2, 2.8) for f in synth.BARK]
    cr = synth.creak(bend, env([(0, 25), (bend, 160)], bend), rng, body=body, q=12, jitter=0.4,
                     shape=env([(0, 0), (bend * 0.6, 1), (bend, 0.7)], bend))
    b.at(0.1, cr, -7)
    t = 0.1 + bend
    b.at(t, clip(CRACK, rng.uniform(0.3, 0.36), rng.uniform(0.08, 0.13), rng.uniform(-6, -3)), 0)
    b.at(t, stems(rng, 0.3, env([(0, 50), (0.3, 8)], 0.3)), -4)
    b.at(t, dsp.shaped(leaves(rng, 0.4, 900, 600, 2800, (0.002, 0.03)), [(0, 1), (0.4, 0)]), -6)
    b.at(t + 0.05, kit.whoosh(rng, 0.35, 250, 1500, 0.3), -13)
    return b.x


def browse_sedge(rng):
    """Sedge pulled out of the bog: a wet sucking pull, the fibrous blades tearing in a rip that slows as they part,
    water dripping off the roots."""
    b = Bus(2.0)
    pull = rng.uniform(0.3, 0.45)
    suck = dsp.sweep_filter(synth.noise(pull, rng, "pink"), "bp", env([(0, 280), (pull, 750)], pull, "exp"), q=1.6)
    b.at(0.0, norm(suck) * env([(0, 0), (0.05, 1), (pull, 0.2)], pull), -10)
    b.at(0.0, slosh(rng, pull, 250, 800, rate=rng.uniform(8, 11)), -12)
    rip = rng.uniform(0.25, 0.4)
    b.at(pull * 0.6, stems(rng, rip, env([(0, 120), (rip, 12)], rip), f=(800, 2000), dull=3000), 0)
    b.at(pull * 0.6, dsp.shaped(leaves(rng, rip, 700, 500, 2200, (0.003, 0.03)), [(0, 1), (rip, 0)]), -8)
    t = pull * 0.6 + rip
    for _ in range(int(rng.integers(3, 6))):      # drips off the roots back into the water
        t += rng.uniform(0.06, 0.25)
        d = ring(burst(rng, 0.002, 600, 2000, 0.2), [rng.uniform(700, 1400)], q=6, tail=0.03)
        b.at(t, norm(d), rng.uniform(-26, -18))
    return b.x


@recipe("tell-moose-grazing", "browse", "tear",
        "A bite of browse torn off: willow leaves stripped down a stem, a sapling wrenched till it cracks, sedge pulled "
        "from the bog",
        """Three bites, three plants. Willow: the lips close on a stem and the head pulls back along it, the leaves
        stripped off in a crackling rip (leaf grains with sharp onsets, a few loud and many quiet, the stalks snapping off
        one after another and slowing), the bare stem springing back. Birch sapling: the stem bends (a green-wood
        creak), gives with a real crack bent down, the leaves torn away with it. Sedge: a wet sucking pull out of the
        bog, fibrous blades tearing, water dripping off the roots. Held to 200 Hz-3 kHz.""",
        sources=[CRACK], takes=3, band=GRAZE)
def grazing_browse(rng, k):
    y = (browse_willow, browse_sapling, browse_sedge)[k % 3](rng)
    return held(air(y, rng, 0.1), *GRAZE, drive=3)


HEAD = [280, 640, 1150, 1900]     # the long skull and its nasal passages: the box the molars grind in


def molar_stroke(rng, length, head, crunch=1.0):
    """One sideways sweep of the lower molars across the upper with browse between: fibre crushing (a dense crackle) and
    enamel grinding (a fast stick-slip), both rung through the head; strongest mid-sweep, as the jaw bears down."""
    shape = [(0, 0), (length * 0.35, 1), (length * 0.7, 0.55), (length, 0)]
    cr = synth.crackle(length, rng.uniform(500, 900), rng, size=(0.0003, 0.0018), hi=700)
    cr = norm(mix(norm(dsp.resonate(cr, head, q=4, gains=[0.3, 0.7, 1.0, 0.9])), norm(cr) * 0.6))
    gr = synth.creak(length, env([(0, 120), (length, 200)], length), rng, body=[f * 1.6 for f in head], q=7,
                     jitter=0.5, grit=0.6)
    return dsp.shaped(mix(cr * crunch, gr * 0.3), shape)


def gulp(rng):
    """The bolus swallowed: a soft low knock in the gullet with the throat working round it."""
    k = ring(burst(rng, 0.006, 150, 900, 0.3), [rng.uniform(230, 280), rng.uniform(520, 600)], q=6, tail=0.18)
    return norm(mix(norm(k), slosh(rng, 0.2, 250, 700, 9) * 0.4))


def cud_up(rng, length=0.55):
    """The cud coming back up the gullet: a rolling gurgle rising through the throat, felt more than voiced."""
    g = kit.cry([(0, 60), (length, 75)], length, rng, tract=TRACT, vowels=[(0, "u"), (length, "o")], rough=0.8,
                sub=0.7, breath=0.8, jitter=0.05, shape=[(0, 0), (length * 0.6, 1), (length, 0)], oq=0.7)
    return norm(mix(norm(g) * 0.6, slosh(rng, length, 220, 650, rng.uniform(10, 14))))


@recipe("tell-moose-grazing", "chew", "cud",
        "Chewing the cud: big molars grinding fibre at a slow steady 1.2 a second, a swallow and the cud coming back up",
        """A ruminant's chew, never a gnaw: each stroke is the lower molars sweeping across the upper with browse between
        (a dense crackle of fibre crushing, enamel grinding in a fast stick-slip under it), rung through a long skull's
        hollows, swelling as the jaw bears down. Left and right sides alternate, a little different. Once a cycle the
        bolus is swallowed (a soft knock in the gullet) and the cud rolls back up (a low gurgle through the throat) without
        a break in the rhythm. Dry, short strokes, so the loop stops dead. Held to 200 Hz-3 kHz.""",
        takes=1, loop=True, band=GRAZE, lufs=-22, seconds=LOOP)
def grazing_chew_cud(rng, k):
    b = Bus(LOOP + 0.5)
    sides = [[f * rng.uniform(0.94, 1.0) for f in HEAD], [f * rng.uniform(1.0, 1.07) for f in HEAD]]
    beat = LOOP / 15                 # 15 chews a cycle: 0.8 s apart
    swallow = int(rng.integers(8, 11))
    for i in range(15):
        t = 0.05 + i * beat + rng.uniform(-0.02, 0.02)     # a lead, so no stroke starts before the loop does
        if i == swallow:
            b.at(t, gulp(rng), -8)
            b.at(t + 0.25, cud_up(rng), -9)
            continue
        sl = rng.uniform(0.3, 0.38)
        b.at(t, molar_stroke(rng, sl, sides[i % 2], rng.uniform(0.8, 1.0)), (0 if i % 2 == 0 else -2) + rng.uniform(-1, 1))
        if rng.random() < 0.35:      # the lips working the browse back in as the jaw opens
            b.at(t + sl, leaves(rng, 0.12, 300, 500, 1800) * env([(0, 1), (0.12, 0)], 0.12), -20)
    return cycle(dsp.peak(b.x, 1800, 0.7, 4), LOOP, rng, GRAZE, drive=4, wet=0.06)


def twig_crunch(rng, length, head):
    """A woody mouthful crushed: a real crunch bent down into the jaw, twigs breaking in it, the molars grinding."""
    c = clip(SNOW[int(rng.integers(len(SNOW)))], rng.uniform(0.0, 0.05), length * 0.6, rng.uniform(-4, -1))
    c = norm(mix(norm(hp(c, 500)), norm(dsp.resonate(c, head, q=5, gains=[0.3, 0.7, 1.0, 0.9])) * 0.6))
    y = Bus(length + 0.1)
    y.at(0.0, dsp.shaped(c, [(0, 0), (0.02, 1), (len(c) / SR, 0.2)]), -2)
    y.at(0.02, stems(rng, length * 0.6, rng.uniform(15, 35), f=(900, 2600), dull=3500), -5)
    y.at(0.0, molar_stroke(rng, length, head, 0.6), -6)
    return y.x


@recipe("tell-moose-grazing", "chew", "twigs",
        "Chewing a mouthful of woody browse: real crunches bent down into a big jaw, twigs cracking in it, slow and even",
        """Fresh browse instead of cud: each chew crushes a woody mouthful, a real underfoot crunch slowed and dropped a
        fourth or more so it's a jaw's size, rung through a long skull, with small twigs breaking in it and the molars
        grinding under it. About 1.3 a second, the odd stroke softer as the mouthful wears down, never a pause long
        enough to be mistaken for its stopping. Dry and short, so the loop stops dead. Held to 200 Hz-3 kHz.""",
        sources=SNOW, takes=1, loop=True, band=GRAZE, lufs=-22, seconds=LOOP)
def grazing_chew_twigs(rng, k):
    b = Bus(LOOP + 0.5)
    sides = [[f * rng.uniform(0.94, 1.0) for f in HEAD], [f * rng.uniform(1.0, 1.07) for f in HEAD]]
    beat = LOOP / 16                 # 16 chews a cycle: 0.75 s apart
    for i in range(16):
        t = 0.05 + i * beat + rng.uniform(-0.025, 0.025)
        soft = rng.random() < 0.25
        b.at(t, twig_crunch(rng, rng.uniform(0.28, 0.36), sides[i % 2]), (-5 if soft else 0) + rng.uniform(-1.5, 0.5))
    return cycle(dsp.peak(b.x, 1800, 0.7, 4), LOOP, rng, GRAZE, drive=4, wet=0.06)


def sac_rub(rng, length, f, rate):
    """Two engorged sacs rubbing: taut wet membrane on membrane. Rubber sticks and slips evenly, so unlike wood's
    catching creak the slips come regularly, at `rate` a second (a curve, 100-450), and the pitch glides with the
    pressure; rung through a drum skin's modes it's a rubbery squeak-creak, like a water balloon squeezed. Wet ticks of
    the skin peeling apart run through it."""
    n = samples(length)
    rc = synth.curve(rate, n)
    press = np.clip(0.6 + 15 * lp(rng.standard_normal(n + SR).astype(np.float32), 12)[:n], 0.1, 1.5)
    x = np.zeros(n, np.float32)
    t = 0.0
    while samples(t) < n:
        a = samples(t)
        x[a] = press[a] * (1 + 0.1 * rng.standard_normal())
        t += (1 / rc[a]) * (1 + 0.06 * rng.standard_normal())
    body = [f * m for m in kit.MEMBRANE[:6]]
    y = dsp.resonate(x, body, q=rng.uniform(12, 16), gains=[1 / (1 + 0.3 * i) for i in range(len(body))])
    wet = synth.crackle(length, 140, rng, size=(0.0002, 0.0008), hi=600)
    y = mix(norm(hp(y, 120)), norm(lp(wet, 3000)) * 0.2)
    return y * env([(0, 0), (length * 0.25, 1), (length * 0.8, 0.7), (length, 0)], length)


@recipe("tell-moose-grazing", "creak", "sac",
        "A tick sac's wet rubbery creak as the hide shifts: taut membranes rubbing, the blood inside sloshing",
        """The engorged ticks on its hide, grape to plum size, rubbing against each other as it shifts its weight: taut
        wet membrane on membrane, stick-slip through a drum skin's modes (a rubbery squeak-creak like a water balloon
        squeezed), the fluid inside wobbling as it sloshes, and the bare hide stretching (a real leather creak slowed).
        Four takes: one long creak, two sacs squeaking past each other, a creak that ends in a wet slosh, a short tight
        squeal. Held to 200 Hz-3 kHz.""",
        sources=[LEATHER], takes=4, band=GRAZE)
def grazing_creak(rng, k):
    b = Bus(1.4)
    f = rng.uniform(420, 560)
    kind = k % 4
    if kind == 0:          # one long creak as the hide stretches
        L = rng.uniform(0.5, 0.7)
        b.at(0.0, sac_rub(rng, L, f, env([(0, 130), (L * 0.5, 300), (L, 160)], L)), 0)
        b.at(0.1, slosh(rng, L, 220, 700), -12)
    elif kind == 1:        # two sacs squeaking past each other, back and forth
        for i, L in enumerate((rng.uniform(0.18, 0.26), rng.uniform(0.22, 0.32))):
            t = i * rng.uniform(0.28, 0.36)
            b.at(t, sac_rub(rng, L, f * (1.0 if i == 0 else rng.uniform(1.15, 1.3)),
                            env([(0, 200), (L, 380 if i == 0 else 240)], L)), -i * 2)
        b.at(0.15, slosh(rng, 0.4, 220, 650), -14)
    elif kind == 2:        # a creak that ends as the sac rolls over, its blood sloshing
        L = rng.uniform(0.3, 0.42)
        b.at(0.0, sac_rub(rng, L, f, env([(0, 150), (L, 320)], L)), 0)
        b.at(L * 0.8, slosh(rng, 0.45, 200, 750, rate=rng.uniform(6, 8)), -4)
    else:                  # one short tight squeal, the sac at its fullest
        L = rng.uniform(0.15, 0.22)
        b.at(0.0, sac_rub(rng, L, f * 1.35, env([(0, 340), (L, 450)], L)), 0)
        b.at(0.05, slosh(rng, 0.25, 250, 800, 10), -15)
    hide = clip(LEATHER, 0.1, 0.12, rng.uniform(-7, -4))
    b.at(rng.uniform(0.0, 0.08), norm(lp(hide, 2500)), -14)
    return held(air(b.x, rng, 0.1), *GRAZE, drive=3)


def content_grunt(rng, length, f0):
    """A content bull's grunt: mouth shut, a low pressed hum through the long nose, the dewlap booming with it."""
    shape = [(0, 0), (0.03, 1), (length * 0.5, 0.8), (length, 0)]
    v = kit.cry([(0, f0), (length * 0.3, f0 * 1.06), (length, f0 * 0.88)], length, rng, tract=0.42,
                vowels=[(0, "m"), (length * 0.35, "u"), (length, "m")], rough=0.2, sub=0.3, breath=0.25, jitter=0.02,
                shimmer=0.12, shape=shape, oq=0.42)
    return sac(v, rng.uniform(150, 180), wet=0.45)


def nostrils(rng, length, flap=0.4):
    """Air let out through the big nostrils: a soft breath, the nostril folds fluttering a little."""
    br = synth.breath(length, [(0, "h"), (length, "m")], TRACT * 1.3, rng,
                      shape=env([(0, 0), (0.04, 1), (length, 0)], length))
    fl = flutter(rng, rng.uniform(26, 34), length)
    return norm(bp(br, 250, 2500) * (1 - flap + flap * fl))


@recipe("tell-moose-grazing", "grunt", "content",
        "The occasional low content grunt: mouth shut, a hum through the long nose, a soft breath out after",
        """What a bull at peace sounds like: a short low grunt with its mouth shut (a slow pressed glottis at 70-90 Hz
        through a long tract on a closed 'mm-oo-mm'), the swollen dewlap booming with it, then a little air let out
        through big nostrils. One, a double 'hm-hmh', and one that trails into a longer breath. Soft and closed, never a
        cough: the warning's grunt is open and explosive. Held to 200 Hz-3 kHz.""",
        takes=3, band=GRAZE)
def grazing_grunt(rng, k):
    b = Bus(1.6)
    f0 = rng.uniform(72, 88)
    if k % 3 == 1:
        b.at(0.0, content_grunt(rng, rng.uniform(0.18, 0.24), f0), -2)
        t = rng.uniform(0.3, 0.38)
        L = rng.uniform(0.28, 0.36)
        b.at(t, content_grunt(rng, L, f0 * 0.93), 0)
        b.at(t + L * 0.85, nostrils(rng, 0.25), -18)
    else:
        L = rng.uniform(0.3, 0.42)
        b.at(0.0, content_grunt(rng, L, f0), 0)
        b.at(L * 0.85, nostrils(rng, 0.25 if k % 3 == 0 else 0.5, 0.5), -18 if k % 3 == 0 else -14)
    return held(air(b.x, rng, 0.1), *GRAZE, drive=4)


# ---- Warning: its meter at 50 (150 Hz-4 kHz, by part) ----------------------------------------------------------------

def cough(rng, length, f0, force=1.0):
    """The bull's cough: a glottal stop bursting open (a puff of air, the chest and dewlap knocking hollow under it, an
    axe going into wood) into a short pressed grunt on 'ah' that dies fast toward 'oh'."""
    L = length
    v = kit.cry([(0, f0 * 1.15), (0.025, f0 * 1.2), (L * 0.5, f0 * 0.9), (L, f0 * 0.7)], L, rng, tract=TRACT,
                vowels=[(0, "a"), (L, "o")], rough=0.6, sub=0.6, breath=0.4, jitter=0.03, shimmer=0.25,
                shape=[(0, 0), (0.004, 1), (0.03, 0.9), (L * 0.45, 0.35), (L, 0)], oq=0.32)
    puff = burst(rng, 0.025, 300, 1500, attack=0.05)
    chop = ring(burst(rng, 0.003, 200, 1500, attack=0.1),
                [rng.uniform(190, 230), rng.uniform(430, 500), rng.uniform(850, 980)], q=7, tail=0.08)
    b = Bus(L + 0.15)
    b.at(0.0, sac(v, rng.uniform(160, 190), wet=0.4), 0)
    b.at(0.0, norm(chop), -3 + 3 * np.log2(force))
    b.at(0.0, norm(puff), -8)
    return dsp.saturate(norm(b.x), 8)


@recipe("tell-moose-warning", "grunt", "cough",
        "The bull's cough: a short hollow explosive grunt, like an axe going into wood",
        """A real bull moose's warning, made from a throat: a glottal stop bursting open (a puff of air, the chest and
        the swollen dewlap knocking hollow under it) into a short pressed grunt (a slow glottis at 90-120 Hz, rough and
        doubled, through a very long tract on 'ah') that dies within a fifth of a second. Saturated: it's forced out.
        One cough, a lower one, and a double 'uh-UHK'. Held to 150 Hz-1.5 kHz.""",
        takes=3, band=COUGH)
def warning_cough(rng, k):
    b = Bus(1.0)
    if k % 3 == 2:
        f0 = rng.uniform(95, 110)
        b.at(0.0, cough(rng, rng.uniform(0.11, 0.14), f0, 0.7), -5)
        b.at(rng.uniform(0.22, 0.27), cough(rng, rng.uniform(0.17, 0.22), f0 * 1.05, 1.3), 0)
    else:
        f0 = rng.uniform(100, 115) if k % 3 == 0 else rng.uniform(85, 95)
        b.at(0.0, cough(rng, rng.uniform(0.16, 0.22), f0, 1.0), 0)
    return held(air(b.x, rng, 0.1, 0.35), *COUGH, drive=5)


@recipe("tell-moose-warning", "grunt", "chop",
        "The cough kitbashed: a real roar dropped an octave and a half and chopped short over a hollow plank knock",
        """The same warning built from recordings: the hardest sliver of a roar, slowed an octave and a half into a
        bull's chest and cut off short and explosive, rung through the dewlap's cavity; under it a real hollow plank
        knock dropped a few semitones (the 'axe into wood' everyone who's heard one describes), and a sigh slowed into
        the breath forced out with it. Saturated hard. One, a lower one, a double. Held to 150 Hz-1.5 kHz.""",
        sources=[ROAR, SIGH] + PLANK, takes=3, band=COUGH)
def warning_chop(rng, k):
    def one(semis, force=1.0):
        r = clip(ROAR, rng.uniform(0.25, 0.28), rng.uniform(0.06, 0.08), semis)
        L = len(r) / SR
        r = dsp.shaped(sac(r, rng.uniform(150, 180), wet=0.5), [(0, 0), (0.003, 1), (0.03, 0.9), (L * 0.5, 0.35), (L, 0)])
        knock = clip(PLANK[int(rng.integers(5))], length=0.2, semis=rng.uniform(-5, -2))
        knock = dsp.shaped(norm(lp(knock, 1500)), [(0, 1), (0.15, 0)], "exp")
        sigh = clip(SIGH, 0.06, 0.1, rng.uniform(-9, -7))
        b = Bus(L + 0.3)
        b.at(0.0, norm(r), 0)
        b.at(0.0, knock, -2 + 3 * np.log2(force))
        b.at(0.01, norm(sigh), -12)
        return dsp.saturate(norm(b.x), 9)
    b = Bus(1.0)
    if k % 3 == 2:
        b.at(0.0, one(-17, 0.7), -5)
        b.at(rng.uniform(0.22, 0.27), one(-18, 1.3), 0)
    else:
        b.at(0.0, one(-17 if k % 3 == 0 else -19.5), 0)
    return held(air(b.x, rng, 0.1, 0.35), *COUGH, drive=4)


def clack(rng):
    """Molars clashing: big enamel on enamel, a hard tick that rings briefly, the jawbone knocking under it; left and
    right rows meet a few milliseconds apart. A real wooden chop gives it its hardness."""
    b = Bus(0.1)
    for i in range(2):
        f = rng.uniform(2300, 3200)
        x = burst(rng, rng.uniform(0.0002, 0.0005), attack=0.05)
        en = ring(x, [f, f * rng.uniform(1.22, 1.38), f * rng.uniform(0.72, 0.8)], q=rng.uniform(12, 20), tail=0.035,
                  gains=[1, 0.5, 0.5])
        bone = ring(x, [rng.uniform(1300, 1600), rng.uniform(1900, 2200)], q=5, tail=0.02)
        b.at(i * rng.uniform(0.005, 0.014), norm(mix(norm(en), norm(bone) * 0.6, hp(x, 2500) * 0.6)),
             -i * rng.uniform(3, 7))
    b.at(0.0, norm(clip(CHOP, length=0.06, semis=rng.uniform(2, 5))), -5)
    return b.x


def lick(rng):
    """The tongue run over the lips between clacks: a short wet smack."""
    L = rng.uniform(0.04, 0.07)
    s = burst(rng, L, 1800, 4500, attack=0.15)
    t = synth.crackle(L, 300, rng, size=(0.0002, 0.0006), hi=2000)
    return norm(mix(norm(s), norm(t) * 0.5))


@recipe("tell-moose-warning", "clack", "molars",
        "Teeth clacking: big molars snapped together in a short run that slows, a lick of the lips between",
        """Its jaw snapped shut on nothing, again and again: each clack is big enamel on enamel (a hard tick ringing
        briefly in the molars, the jawbone knocking under it, the left and right rows meeting a few milliseconds apart,
        a real wooden chop pitched up for the hardness). Runs of three to six, about five a second and slowing, the
        tongue sometimes running over the lips between. Few, big, even knocks: not a claw clatter, not a gnaw's rasp.
        Held to 2-4 kHz.""",
        sources=[CHOP], takes=4, band=CLACK)
def warning_clack(rng, k):
    n = 3 + k % 4
    b = Bus(2.0)
    t, gap = 0.0, rng.uniform(0.15, 0.19)
    for i in range(n):
        b.at(t, clack(rng), rng.uniform(-2, 0) - (2 if i == n - 1 else 0))
        if rng.random() < 0.3 and i < n - 1:
            b.at(t + gap * 0.55, lick(rng), -16)
        t += gap
        gap *= rng.uniform(1.05, 1.2)
    return held(air(b.x, rng, 0.04, 0.2), *CLACK, drive=2)


def rake(rng, length):
    """A split hoof raked back through turf and grit: real crunches and stones slowed and cut into grains, strewn densely
    along the pull (earthy, not leafy), the soil's hiss under them, a root or two tearing."""
    shape = [(0, 0), (length * 0.1, 1), (length * 0.6, 0.85), (length, 0)]
    g = grains(rng, SNOW + STONES, length, env([(0, 120), (length * 0.3, 320), (length, 60)], length),
               size=(0.01, 0.04), semis=(-7, -2))
    hiss = burst(rng, length, 500, 2500, attack=0.1)
    roots = stems(rng, length * 0.6, env([(0, 20), (length * 0.6, 5)], length * 0.6), f=(700, 1500), dull=2000)
    return mix(dsp.shaped(norm(g), shape), dsp.shaped(norm(hiss), shape) * 0.25, roots * 0.3)


def clods(rng, start, spread=0.4, n=(4, 9)):
    """What the hoof threw back landing behind it: soft clods thudding and grit pattering."""
    b = Bus(start + spread + 0.3)
    for _ in range(int(rng.integers(*n))):
        t = start + rng.uniform(0, spread)
        c = burst(rng, rng.uniform(0.012, 0.03), 300, 1400, attack=0.08)
        b.at(t, norm(c * np.exp(-np.linspace(0, 5, len(c))).astype(np.float32)), rng.uniform(-22, -12))
        b.at(t + rng.uniform(0, 0.03), kit.gravel(rng, int(rng.integers(2, 6)), 0.04, 900, 3500, body=0),
             rng.uniform(-26, -18))
    return b.x


@recipe("tell-moose-warning", "hoof-drag", "paw",
        "A split hoof raked back through the ground like a bull pawing it, the clods thrown behind",
        """A forehoof planted hard and dragged back through turf and grit, the way a bull paws before it fights: a dull
        knock as the toes dig in, then the ground itself scrapes (real crunches and stones slowed a few semitones and cut
        into grains strewn densely along the pull, earthy rather than leafy, the soil's hiss, a root tearing), and what
        it threw back lands behind it in soft clods and pattering grit. One long drag, a drag with a hitch in it, a short
        hard rake. Held to 400 Hz-4 kHz.""",
        sources=SNOW + STONES, takes=3, band=DRAG)
def warning_drag(rng, k):
    b = Bus(2.0)
    b.at(0.0, toes(rng), -6)
    b.at(0.004, thock(rng, rng.uniform(220, 280), 0.1), -3)     # the hoof planted hard before it rakes
    kind = k % 3
    if kind == 0:
        L = rng.uniform(0.6, 0.8)
        b.at(0.02, rake(rng, L), 0)
        b.at(0.02, clods(rng, L * 0.6, 0.5), 0)
    elif kind == 1:
        L1, L2 = rng.uniform(0.25, 0.32), rng.uniform(0.35, 0.45)
        b.at(0.02, rake(rng, L1), -2)
        t = 0.02 + L1 + rng.uniform(0.08, 0.14)
        b.at(t - 0.02, toes(rng, 0.7), -10)
        b.at(t - 0.016, thock(rng, rng.uniform(220, 280), 0.08), -8)
        b.at(t, rake(rng, L2), 0)
        b.at(t, clods(rng, L2 * 0.6, 0.45), 0)
    else:
        L = rng.uniform(0.3, 0.4)
        b.at(0.02, rake(rng, L), 0)
        b.at(0.02, clods(rng, L * 0.5, 0.35, (7, 12)), 2)
    return held(air(b.x, rng, 0.1), *DRAG, drive=5)


# ---- Squaring up: 2.5 s before the charge (two stamps and a snort) ---------------------------------------------------

GROUNDS = ["gravel", "turf", "peat", "ballast"]


def stamp_take(rng, ground):
    """A forehoof slammed down from high: the toes crack onto the ground, the earth takes a tonne (a dull knock, a real
    plank knock dropped well down, a weight under it), then the ground answers; the sacs on its chest shudder."""
    b = Bus(1.0)
    t0 = 0.03
    b.at(t0 - 0.004, toes(rng, 1.0), -2)
    b.at(t0, thock(rng, rng.uniform(180, 230), 0.12), 0)
    pk = clip(PLANK[int(rng.integers(5))], length=0.2, semis=rng.uniform(-8, -5))
    b.at(t0, dsp.shaped(norm(lp(pk, 1600)), [(0, 1), (0.2, 0.01)], "exp"), -5)
    b.at(t0, kit.thud(rng, rng.uniform(55, 70), 0.3, 1.0), -8)
    if ground in ("gravel", "ballast"):
        cr = clip(SNOW[int(rng.integers(5))], length=rng.uniform(0.08, 0.12), semis=rng.uniform(-3, -1))
        b.at(t0 + 0.003, dsp.shaped(norm(hp(cr, 200)), [(0, 1), (0.12, 0.1)], "exp"), -4)
        b.at(t0 + 0.004, kit.gravel(rng, int(rng.integers(14, 24)), 0.06, 600, 3500, body=0), -9)
        if ground == "ballast":
            st = clip(STONES[int(rng.integers(3))], length=0.2, semis=rng.uniform(-5, -2))
            b.at(t0 + 0.01, norm(st), -11)
        for _ in range(int(rng.integers(3, 7))):    # stones kicked loose, settling
            b.at(t0 + rng.uniform(0.06, 0.35), synth.click(rng.uniform(1200, 3000), q=6, length=0.015, rng=rng),
                 rng.uniform(-28, -18))
    elif ground == "turf":
        cr = clip(SNOW[int(rng.integers(5))], length=rng.uniform(0.08, 0.12), semis=rng.uniform(-7, -4))
        b.at(t0 + 0.003, dsp.shaped(norm(lp(cr, 2200)), [(0, 1), (0.12, 0.1)], "exp"), -5)
        b.at(t0 + 0.01, stems(rng, 0.06, 60, f=(800, 1800), dull=2500), -12)
        b.at(t0, clods(rng, 0.06, 0.25, (2, 5)), 0)
    else:   # peat: the hoof driven into wet ground, the suck as it settles, water spat out round it
        sq = dsp.sweep_filter(synth.noise(0.2, rng, "pink"), "bp", env([(0, 900), (0.2, 300)], 0.2, "exp"), q=1.4)
        b.at(t0, norm(sq) * env([(0, 1), (0.2, 0)], 0.2), -5)
        b.at(t0 + 0.02, slosh(rng, 0.25, 250, 900, 11), -10)
        for _ in range(int(rng.integers(4, 8))):
            d = burst(rng, rng.uniform(0.004, 0.01), 800, 2500, attack=0.1)
            b.at(t0 + rng.uniform(0.04, 0.25), norm(d), rng.uniform(-26, -18))
    b.at(t0 + 0.03, slosh(rng, 0.25, 220, 700, 12), -18)
    return b.x


def _pairs(takes, rng):
    """Stamps in twos, as the square-up plays them (about 0.75 s apart, a pause between squares)."""
    b = Bus(len(takes) * 2.2 + 1.0)
    t = 0.2
    for i, x in enumerate(takes):
        b.at(t, x)
        b.at(t + rng.uniform(0.7, 0.8), takes[(i + 1) % len(takes)])
        t += 2.2
    return b.x


@recipe("tell-moose-square-up", "stamp", "hoof",
        "One heavy hoof stamp: split toes cracking down, the earth taking a tonne, the ground answering (gravel, turf, "
        "peat, ballast)",
        """A forehoof slammed down from high: its two black toes crack onto the ground a few milliseconds apart, the
        earth takes the weight (a dull low knock, a real plank knock dropped half an octave under it, a sub), then
        the ground answers: gravel spraying, turf and roots giving, peat sucking and spitting water, ballast stones
        knocking and settling. The sacs on its chest shudder with it. Four grounds, one per take; the preview plays them
        in twos as the square-up does. Held to 150 Hz-3 kHz.""",
        sources=PLANK + SNOW + STONES, takes=4, band=STAMP, lufs=-22, preview=_pairs)
def squareup_stamp(rng, k):
    return held(air(stamp_take(rng, GROUNDS[k % 4]), rng, 0.12), *STAMP, drive=3)


@recipe("tell-moose-square-up", "snort", "blow",
        "A heavy snort: air blasted out through big nostrils, their folds fluttering, wet in it",
        """A forced blast out through nostrils the size of a fist: breath noise through a long nose, the nostril folds
        flapping in it (a rough flutter that slows as the blast runs out), a real sneeze slowed half an octave under it
        for the air's texture, mucus crackling at the peak. One sharp and short, one long and rattling, one with a
        quick hard sniff in first. Held to 300 Hz-4 kHz.""",
        sources=[SNEEZE], takes=3, band=SNORT)
def squareup_snort(rng, k):
    b = Bus(1.6)
    t = 0.0
    kind = k % 3
    if kind == 2:       # a quick hard sniff in first
        L = rng.uniform(0.1, 0.14)
        sn = synth.breath(L, [(0, "h"), (L, "m")], TRACT * 1.3, rng, shape=env([(0, 0), (L * 0.8, 1), (L, 0)], L))
        b.at(0.0, norm(bp(sn, 500, 3500)), -10)
        t = L + rng.uniform(0.05, 0.08)
    L = rng.uniform(0.3, 0.4) if kind == 0 else rng.uniform(0.5, 0.65)
    # the nostril folds flapping in the blast: a pulse train that slows as the air runs out, strongest early
    fl = flutter(rng, env([(0, rng.uniform(30, 36)), (L, rng.uniform(15, 19))], L), L, jitter=0.35, decay=0.01)
    depth = (0.85 if kind == 1 else 0.65) * env([(0, 1), (L * 0.6, 1), (L, 0.3)], L)
    gate = 1 - depth * (1 - fl)
    nose = synth.tract(synth.noise(L, rng, "pink") * gate, [(0, "m"), (L * 0.5, "h")], TRACT * 1.3)
    jet = bp(synth.noise(L, rng) * gate, 1200, 3500)
    blast = mix(norm(nose), norm(jet) * 0.3, norm(bp(fl, 300, 1200)) * 0.25)
    b.at(t, dsp.shaped(norm(blast), [(0, 0), (0.01, 1), (0.05, 0.9), (L * 0.5, 0.4), (L, 0)]), 0)
    real = clip(SNEEZE, length=0.35, semis=rng.uniform(-7, -5))
    b.at(t, norm(lp(real, 4000)), -9)
    mucus = synth.crackle(0.25, 120, rng, size=(0.0002, 0.0008), hi=1200) * env([(0, 1), (0.25, 0)], 0.25)
    b.at(t + 0.02, norm(lp(mucus, 4000)), -16)
    return held(air(b.x, rng, 0.1, 0.35), *SNORT, drive=4)


# ---- The charge: a straight line at 11 m/s ---------------------------------------------------------------------------

def hoof_fall(rng, ground, w=1.0):
    """One hoof of the charge: the toes, the earth taking the weight, then the ground (ballast shoulder or verge)."""
    b = Bus(0.6)
    t0 = 0.012
    b.at(t0 - 0.004, toes(rng, 0.8), -9)
    b.at(t0, thock(rng, rng.uniform(150, 200), 0.1), -5 + 6 * np.log10(w))
    pk = clip(PLANK[int(rng.integers(5))], length=0.15, semis=rng.uniform(-7, -4))
    b.at(t0, dsp.shaped(norm(lp(pk, 1300)), [(0, 1), (0.15, 0.01)], "exp"), -8 + 6 * np.log10(w))
    b.at(t0, kit.thud(rng, rng.uniform(60, 85), 0.2, w), -9)
    if ground == "ballast":
        cr = clip(SNOW[int(rng.integers(5))], length=rng.uniform(0.09, 0.14), semis=rng.uniform(-4, -1))
        b.at(t0 + 0.003, norm(hp(cr, 400)), -1 + 6 * np.log10(w))
        b.at(t0 + 0.005, kit.gravel(rng, int(rng.integers(8, 16)), 0.06, 700, 3500, body=0), -8)
        for _ in range(int(rng.integers(1, 4))):     # stones flung from under it
            b.at(t0 + rng.uniform(0.05, 0.25), synth.click(rng.uniform(1300, 3200), q=6, length=0.015, rng=rng),
                 rng.uniform(-28, -19))
    else:
        cr = clip(SNOW[int(rng.integers(5))], length=rng.uniform(0.1, 0.14), semis=rng.uniform(-7, -4))
        b.at(t0 + 0.003, norm(lp(cr, 2000)), -5 + 6 * np.log10(w))
        b.at(t0 + 0.01, clods(rng, 0.03, 0.15, (1, 3)), 3)
    return b.x


def rack_rattle(rng):
    """What's caught in its rack jolting as the forelegs land: fence wire (a real light metal knock slowed, buzzing),
    now and then the insulator's glass."""
    b = Bus(0.3)
    w = clip(WIRE[int(rng.integers(5))], length=0.1, semis=rng.uniform(-9, -6))
    b.at(0.0, dsp.shaped(norm(lp(w, 3000)), [(0, 1), (0.1, 0.01)], "exp"), 0)
    for i in range(int(rng.integers(1, 3))):
        b.at(0.02 + 0.03 * i, norm(lp(clip(WIRE[int(rng.integers(5))], None, 0.03, rng.uniform(-6, -3)), 3500)), -8)
    if rng.random() < 0.3:
        b.at(rng.uniform(0.02, 0.06), norm(clip(GLASS[int(rng.integers(5))], length=0.1, semis=rng.uniform(-8, -5))),
             -10)
    return b.x


def gallop(rng, laden=False):
    """Twenty strides of the gallop on the ballast shoulder (near feet) and the verge (off feet), each foot its own
    weight, each stride a few milliseconds loose; laden, the rack's junk rattles and the sacs slap as it lands."""
    b = Bus(LOOP + 1.0)
    for s in range(int(LOOP / STRIDE)):
        t = 0.05 + s * STRIDE        # a lead, so no fall starts before the loop does
        for i, off in enumerate(FALLS):
            near = i % 2 == 0
            w = rng.uniform(0.8, 1.0) * (1.15 if i >= 2 else 1.0)       # the forelegs take the landing
            b.at(t + off + rng.uniform(-0.008, 0.008), hoof_fall(rng, "ballast" if near else "verge", w),
                 rng.uniform(-2.5, 0))
        if laden:
            b.at(t + FALLS[2] + rng.uniform(0.0, 0.02), rack_rattle(rng), rng.uniform(-22, -18))
            sl = kit.slap(rng, 0.1, 1200, wet=0.3)
            b.at(t + FALLS[0] + 0.03, mix(norm(lp(sl, 1500)), slosh(rng, 0.2, 220, 800, 12) * 0.7), -15)
    return b.x


@recipe("tell-moose-charge", "hooves", "gallop",
        "A heavy four-beat gallop of split hooves at a charge, two on the ballast shoulder and two on the verge",
        """A tonne at 11 m/s: a transverse gallop at 1.7 strides a second (hind, hind, fore, fore, then the flight),
        slower and far heavier than a hound's. Each fall is the two toes striking, the earth taking the weight (a dull
        low knock, a real plank knock dropped half an octave, a sub), then the ground: the near feet crunch the ballast
        shoulder (a real crunch, gravel spraying, stones flung), the off feet thud into the verge (turf giving, clods
        thrown); tilted up where the bed is thin. Exactly 20 strides, seamless; the wheeze loops at the same length so they stay in step. Held to
        150 Hz-3.5 kHz.""",
        sources=PLANK + SNOW, takes=1, loop=True, band=GALLOP, lufs=-20, seconds=LOOP)
def charge_hooves(rng, k):
    return cycle(dsp.peak(gallop(rng), 2000, 0.7, 5), LOOP, rng, GALLOP, drive=4, wet=0.08)


@recipe("tell-moose-charge", "hooves", "laden",
        "The same gallop with what hangs off it: wire and glass caught in the rack jolting, the sacs slapping",
        """The gallop above, with what's wrong with it: the fence wire caught in its rack jolting on every landing of
        the forelegs (a real light metal knock slowed into a buzz), now and then the telegraph insulator's glass, and
        the engorged sacs on its flanks slapping and sloshing as the hind feet land. Kept low under the hooves, so it
        reads as something big with junk on it rather than a jingle. Same 20 strides, seamless. Held to
        150 Hz-3.5 kHz.""",
        sources=PLANK + SNOW + WIRE + GLASS, takes=1, loop=True, band=GALLOP, lufs=-20, seconds=LOOP)
def charge_hooves_laden(rng, k):
    return cycle(dsp.peak(gallop(rng, laden=True), 2000, 0.7, 5), LOOP, rng, GALLOP, drive=4, wet=0.08)


def blow_out(rng, length):
    """Breath driven out of it as the forelegs land: a grunt more air than voice, rough, through the long tract."""
    v = kit.cry([(0, 95), (length, 75)], length, rng, tract=TRACT, vowels=[(0, "a"), (length, "h")], rough=0.6,
                sub=0.5, breath=0.9, jitter=0.04, shape=[(0, 0), (0.01, 1), (length * 0.4, 0.6), (length, 0)], oq=0.4)
    br = synth.breath(length, [(0, "a"), (length, "h")], TRACT, rng,
                      shape=env([(0, 0), (0.01, 1), (length, 0)], length))
    return norm(mix(norm(v) * 0.5, norm(hp(br, 400))))


def wheeze_in(rng, length, f):
    """Air dragged in through an airway swollen half shut: a reedy squeal that can't hold its pitch (stridor), rising
    as the pull peaks and now and then cracking up, rough with the swollen tissue fluttering, over the rush of breath,
    with fine wet crackles."""
    n = samples(length)
    wander = lp(rng.standard_normal(n + SR).astype(np.float32), 12)[:n]
    wander = wander / (np.max(np.abs(wander)) + 1e-9) * 0.04
    fc = f * (1 + wander) * env([(0, 0.85), (length * 0.5, 1.03), (length, 0.95)], length)
    if rng.random() < 0.35:     # the whistle cracks up for a moment
        a = samples(rng.uniform(0.3, 0.6) * length)
        fc[a:a + samples(0.05)] *= rng.uniform(1.35, 1.55)
    tone = np.sin(2 * np.pi * np.cumsum(fc) / SR).astype(np.float32)
    tone = tone + 0.3 * np.sin(2 * np.pi * np.cumsum(fc * 2.0) / SR).astype(np.float32)
    reed = dsp.sweep_filter(synth.noise(length, rng), "bp", fc, q=14, block=64)
    squeal = mix(norm(tone) * 0.35, norm(reed)) * (0.55 + 0.45 * flutter(rng, rng.uniform(45, 70), length, 0.5, 0.006))
    shape = env([(0, 0), (length * 0.35, 1), (length * 0.8, 0.8), (length, 0)], length)
    rush = bp(synth.noise(length, rng, "pink"), 600, 3500)
    rales = synth.crackle(length, 70, rng, size=(0.0002, 0.0008), hi=1200)
    return mix(norm(squeal) * shape, norm(rush) * shape * 0.4, norm(rales) * shape * 0.3)


@recipe("tell-moose-charge", "wheeze", "stride",
        "Its laboured breath at a run, locked to the gallop: a grunt out as it lands, a wheezing whistle in",
        """A galloping animal breathes once a stride, so this does too, at the hooves' 1.7 a second: a rough grunt
        of air driven out as the forelegs land (more breath than voice, through its long tract) and a wheeze dragged in
        during the flight, a reedy whistle through an airway swollen half shut that can't hold its pitch and now and
        then cracks up, with fine wet crackles in it. Exactly 20 breaths, the same length as the hooves, so started
        together they stay in step. Fast and rhythmic, nothing like a slow hiss. Held to 500 Hz-4 kHz.""",
        takes=1, loop=True, band=WHEEZE, lufs=-22, seconds=LOOP)
def charge_wheeze(rng, k):
    b = Bus(LOOP + 1.0)
    f = rng.uniform(1150, 1400)
    for s in range(int(LOOP / STRIDE)):
        t = s * STRIDE
        bl = rng.uniform(0.15, 0.2)
        b.at(t + FALLS[2] + rng.uniform(0.0, 0.015), blow_out(rng, bl), rng.uniform(-3, 0))
        wl = rng.uniform(0.2, 0.26)
        b.at(t + FALLS[2] + bl + rng.uniform(0.02, 0.05), wheeze_in(rng, wl, f * rng.uniform(0.94, 1.06)),
             rng.uniform(-4, -1))
    return cycle(b.x, LOOP, rng, WHEEZE, drive=3, wet=0.04)


def rack_knock(rng):
    """The mineral rack striking a trunk: a hard stony knock, slag-dense, no ring to speak of."""
    x = burst(rng, 0.002, attack=0.1)
    return norm(ring(x, [rng.uniform(600, 800), rng.uniform(1500, 1900), rng.uniform(2600, 3100)], q=10, tail=0.06))


@recipe("tell-moose-charge", "brush", "through",
        "Brush and saplings breaking as it comes through: leaves thrashing, twigs snapping, a trunk cracking",
        """Something wider than a doorway going through a thicket at speed: leaves and twigs thrashed against its hide
        and rack (crackling leaf grains swelling as it hits and dying behind), branches swept aside, a run of twigs
        snapping, the rack knocking a trunk like stone. One through green alder (many dull snaps, a thicker stem or two
        going with a real crack), one through a sapling that bends, cracks and splits (a real crack bent down) with its
        top crashing after, one through dead deadfall (real brittle cracks and crunches, crisp). Held to
        400 Hz-5 kHz.""",
        sources=[CRACK] + DRY, takes=3, band=BRUSH)
def charge_brush(rng, k):
    L = rng.uniform(1.0, 1.3)
    b = Bus(L + 1.0)
    kind = k % 3
    thrash = leaves(rng, L, env([(0, 200), (0.1, 1400), (L * 0.6, 900), (L, 80)], L), 600, 5000, (0.002, 0.025))
    b.at(0.0, dsp.shaped(thrash, [(0, 0), (0.06, 1), (L * 0.6, 0.7), (L, 0)]), -6)
    for _ in range(int(rng.integers(2, 4))):
        b.at(rng.uniform(0, L * 0.6), kit.whoosh(rng, rng.uniform(0.25, 0.4), 400, 2500, 0.4), -14)
    snaps = {0: (16, 24), 1: (7, 11), 2: (10, 16)}[kind]
    b.at(0.03, stems(rng, L * 0.75, rng.uniform(*snaps), f=(900, 3200) if kind != 2 else (1500, 4000),
                     dull=4000 if kind == 0 else 7000), 0)
    for _ in range(int(rng.integers(1, 3))):
        b.at(rng.uniform(0.05, L * 0.5), rack_knock(rng), rng.uniform(-8, -4))
    if kind == 0:      # green alder: a couple of thicker stems going with real cracks, dulled
        for _ in range(int(rng.integers(1, 3))):
            b.at(rng.uniform(0.1, L * 0.6), lp(clip(CRACK, rng.uniform(0.3, 0.36), 0.1, rng.uniform(-6, -3)), 3500), -2)
    elif kind == 1:    # a sapling bends, cracks and splits; its top crashes down after
        t = rng.uniform(0.15, 0.3)
        bend = rng.uniform(0.15, 0.22)
        body = [f * rng.uniform(1.8, 2.3) for f in synth.BARK]
        b.at(t, synth.creak(bend, env([(0, 30), (bend, 220)], bend), rng, body=body, q=12, jitter=0.4,
                            shape=env([(0, 0), (bend, 1)], bend)), -4)
        b.at(t + bend, clip(CRACK, 0.3, 0.3, rng.uniform(-5, -3)), 3)
        b.at(t + bend + 0.3, dsp.shaped(leaves(rng, 0.5, 900, 600, 4000, (0.002, 0.025)), [(0, 0), (0.05, 1), (0.5, 0)]),
             -6)
        b.at(t + bend + 0.5, kit.thud(rng, 120, 0.15, 0.6), -12)
    else:              # dead deadfall: real brittle cracks and crunches
        for _ in range(int(rng.integers(2, 4))):
            key = DRY[int(rng.integers(len(DRY)))]
            a = 0.42 if key.endswith("misc_34") else rng.uniform(0.08, 0.4)
            b.at(rng.uniform(0.05, L * 0.7), clip(key, a, rng.uniform(0.08, 0.15), rng.uniform(-4, -1)), -1)
    return held(air(b.x, rng, 0.12, 0.4), *BRUSH, drive=4)
