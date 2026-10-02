"""The beasts' creature sounds (GDD v1.2 §21, App. A.3-A.8): Cinder Hounds, Ribbits, the Grumbler, Followers, Climbers.

Each is built from what its body is made of (tools/blender/<creature>.py has the models):
- Cinder Hounds: lean lurchers starved to the frame, the hide gone to oily soot, slag crusted on the spine and the
  flanks split in cracks that glow like a banked fire. So: dog throats (a glottis through a big dog's tract, rough with
  fry) that are dry and hot, embers crackling in every breath and flaring on the inhale like a firebox drawing, a puff
  of ash off every paw. Their paws are the real surface first (ballast, iron, tin), with the claws and the ash on top.
- Ribbits: toads the size of a big dog, wet hairless skin, a throat sac, a row of human teeth. They eat like people
  with their mouths open, then gulp through the sac.
- The Grumbler: a dock labourer gone wrong, face-down like a spider on bare hands and feet, a split jaw of too many
  teeth, still muttering to himself. Skin slapping iron, not chitin; a man's voice breaking into rage.
- Followers: a hand-sized engorged tick, a leather sac of blood under a hard shield, ten hooked legs; its nest a
  pulsing mass of them over the loot. Taut membranes that burst, fluid, small shells, many little legs.
- Climbers: smooth wet-rubber skin over wire muscle, a spine row, a lamprey's sucker: blows land tight and dense, and
  it squeals and hisses through the sucker.

The packs have little that's alive, so the living parts are synthesised (tools/audio/synth.py) and the real recordings
are what they touch (stones, plate, tin, boards, cloth, crates) and the cracks of things breaking.
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


# ---- Shared parts
# ---------------------------------------------------------------------------------------------------------

def follow(x, ms=10.0):
    """A sound's amplitude envelope, normalised: what makes the embers crackle with the breath that fans them."""
    e = lp(np.abs(x).astype(np.float32), 1000 / ms / 2, 2)
    return np.clip(e / (np.max(e) + 1e-9), 0, 1).astype(np.float32)


def clip(key, start=None, length=0.2, semis=0.0, pre=0.004):
    """A slice of a recording from its first strong attack (or from `start` s), varispeeded, with short fades."""
    x = src.get(key)
    if start is None:
        a = int(np.argmax(np.abs(x) > 0.3 * np.max(np.abs(x))))
        start = max(0.0, a / SR - pre)
    y = dsp.trim(x, start, length)
    if semis:
        y = dsp.vari(y, semis)
    return dsp.fade(y, 0.002, min(0.03, len(y) / SR / 3))


def unit(x):
    return (x / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def done(x):
    """Every take's last step: a gentle 20 Hz highpass, so thumps and lopsided attacks leave no DC behind."""
    return hp(x, 20)


def embers(rng, length, rate, shape=None, hi=1800):
    """A banked fire's crackle: sharp ticks, many quiet and a few loud, `rate` a second (a constant or a curve)."""
    return unit(synth.crackle(length, rate, rng, shape=shape, hi=hi))


def ash(rng, length=0.25, lo=500, hi=5000):
    """A puff of dry ash kicked up: a soft breath of noise with grit in it, no wet."""
    n = synth.noise(length, rng, "pink")
    y = bp(n, lo, hi) * env([(0, 0), (0.012, 1), (length * 0.35, 0.4), (length, 0)], length)
    grit = synth.rustle(length, 400, rng, f=(1500, 7000), ticks=0) * env([(0, 1), (length, 0)], length)
    return unit(mix(unit(y), unit(grit) * 0.35))


def bellows(rng, length, shape):
    """The hound's chest drawing like a firebox: a low roar and a flare of crackle that follow the breath in."""
    n = samples(length)
    e = synth.curve(shape, n)
    roar = lp(synth.noise(length, rng, "brown"), 450) * e ** 1.5
    return mix(unit(roar) * 0.7, embers(rng, length, 30 + 500 * e, hi=1500) * 0.8)


def outdoors(x, rng, wet=0.1, tail=0.6):
    """Open air round a facility or the line: a little of the night's reverb, its tail cut short so retriggered
    one-shots don't pile up a rumble."""
    x = dsp.trim_silence(x, -70)
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def squish(rng, length=0.2, lo=400, hi=1800, wet=1.0):
    """Wet flesh giving: broad noise whose colour drifts half an octave as it gives (up or down), tissue ticking through
    it, and small quick bubbles. Broad and mostly ticks on purpose: a narrow sweep reads as a wah, big rising bubbles as
    a cartoon bloop."""
    c = float(np.sqrt(lo * hi))
    d = rng.uniform(1.3, 1.8) ** (1 if rng.random() < 0.5 else -1)
    fc = env([(0, c / d), (length * rng.uniform(0.3, 0.6), c), (length, c * d)], length, curve="exp")
    body = unit(dsp.sweep_filter(synth.noise(length, rng), "bp", fc, q=rng.uniform(1.2, 2.2)))
    body = body * env([(0, 0), (0.006, 1), (length * 0.4, 0.5), (length, 0)], length)
    tick = synth.crackle(length, env([(0, 1400), (length * 0.5, 500), (length, 60)], length), rng,
                         size=(0.0002, 0.0009), hi=lo)
    bub = synth.bubbles(length, env([(0, 200), (length, 20)], length), 1200, 4500, rng, rise=(0.02, 0.1))
    return unit(mix(body * 0.75, unit(lp(tick, hi * 3)) * 0.6 * wet, unit(bub) * 0.4 * wet))


HOUND_TRACT = 0.52     # a big dog's vocal tract, as a fraction of a child's (synth.TRACT)


def fry(rng, rate, length, jitter=0.35, decay=0.012):
    """A growl's rattle: an irregular train of pulses at `rate` a second (25-50; a constant or a curve), each a quick
    swell that dies. Real growls are this, not a steady buzz: it gates the throat and the breath noise together."""
    n = samples(length)
    rc = synth.curve(rate, n)
    e = np.zeros(n, np.float32)
    t = rng.uniform(0, 0.01)
    while True:
        a = samples(t)
        if a >= n:
            break
        e[a] = rng.uniform(0.4, 1.0)
        t += (1 / rc[a]) * max(0.3, 1 + jitter * rng.standard_normal() * 0.5)
    k = np.arange(samples(decay * 5)) / SR
    y = signal.fftconvolve(e, ((1 - np.exp(-k / 0.0015)) * np.exp(-k / decay)).astype(np.float32))[:n]
    return unit(y)


def throat(rng, pts, length, vowels="a", shape=None, rasp=0.75, fry_rate=(30, 48), sub=0.5, hiss=0.35, fire=0.45,
           tract=HOUND_TRACT, jitter=0.03):
    """One breath of a creature's voice: a glottis (pitch from `pts`) gated by a growl's fry and breath noise in the
    same pulses (`rasp` how much), through a tract (a big dog's by default); the teeth's hiss pulsing with it, and for a
    hound embers crackling in the breath (`fire`: hotter and denser the harder it blows). Dry: no spit, it's ash."""
    n = samples(length)
    sh = dsp.fit(env(shape or [(0, 1), (length, 1)], length), n)
    g = synth.glottis(env(pts, length, curve="exp"), length, rng, jitter=jitter, shimmer=0.3, sub=sub, oq=0.72)
    fr = fry(rng, fry_rate[0] + (fry_rate[1] - fry_rate[0]) * sh, length)
    x = g * (1 - rasp + rasp * fr) + synth.noise(length, rng) * fr * 0.6 * rasp
    v = unit(synth.tract(x, vowels, tract, breath=0.25, rng=rng) * sh)
    e = follow(v, 6)
    teeth = bp(synth.noise(length, rng), 2400, 6500) * e ** 1.3
    burn = embers(rng, length, 20 + 420 * lp(e, 8), hi=1700) * lp(e, 15)
    y = mix(v, unit(teeth) * hiss, burn * fire)
    return lp(dsp.saturate(unit(y) * 0.8, 7), 9000)


# ---- Cinder Hounds
# -----------------------------------------------------------------------------------------------------------
# A lean 0.55 m lurcher: a big dog's throat (tract 0.5-0.6 of a child's), growls round 80-130 Hz, a yelp near 700 Hz.

def cinder_voice(rng, pts, length, vowels="a", shape=None, fry_rate=(26, 44)):
    """A snarl from a throat burnt out: barely voiced, the growl's pulses made of ember crackle and ash-dry breath rung
    through the dog's tract, a fire's low roar under it that swells with each breath."""
    n = samples(length)
    sh = dsp.fit(env(shape or [(0, 1), (length, 1)], length), n)
    fr = fry(rng, fry_rate[0] + (fry_rate[1] - fry_rate[0]) * sh, length, decay=0.009)
    cr = unit(synth.crackle(length, 2500 + 4000 * sh, rng, size=(0.0002, 0.0015), hi=300))
    g = synth.glottis(env(pts, length, curve="exp"), length, rng, jitter=0.04, shimmer=0.3, sub=0.6, oq=0.72)
    x = cr * fr + synth.noise(length, rng, "pink") * fr * 0.5 + g * fr * 0.18
    v = unit(synth.tract(x, vowels, HOUND_TRACT, breath=0.1, rng=rng) * sh)
    y = mix(v, bellows(rng, length, sh ** 1.5) * 0.45, embers(rng, length, 40 + 500 * sh, hi=2200) * sh * 0.35)
    return lp(dsp.saturate(unit(y) * 0.8, 6), 9000)


def inhale(rng, length=0.3, growl=0.5):
    """A hound dragging breath in through its teeth: a creaky in-growl, the embers in its chest flaring as they draw."""
    sh = env([(0, 0), (length * 0.7, 1), (length, 0.2)], length)
    br = synth.breath(length, [(0, "h"), (length, "u")], 0.6, rng, shape=sh)
    g = throat(rng, [(0, 55), (length, 70)], length, "u", [(0, 0), (length * 0.7, 1), (length, 0)], rasp=0.9,
               fry_rate=(22, 30), hiss=0.1, fire=0.0)
    return mix(unit(br) * 0.6, g * growl * 0.6, bellows(rng, length, sh) * 0.7)


def jaw_snap(rng, teeth=1.0):
    """Jaws shutting on nothing: two rows of teeth clacking, dry, with the skull's hollow knock under it."""
    c = kit.snap(rng, 0.06, rng.uniform(1700, 2400))
    knock = dsp.resonate(np.pad(rng.standard_normal(samples(0.002)).astype(np.float32), (0, samples(0.08))),
                         [rng.uniform(380, 460), rng.uniform(900, 1100)], q=9)
    return mix(unit(c) * teeth, unit(knock) * 0.6)


# The paws. A gallop lands toe-first: the claws tick, then the pad takes the weight, then whatever's under it answers.
# Every fall kicks a little ash off the hide and a few embers with it.

def claws(rng, n, f=(2200, 4200), body=None, spread=0.015):
    """`n` claw ticks within `spread` s, each a hard little click (rung through `body`'s mode ratios if given)."""
    b = Bus(spread + 0.05)
    for _ in range(n):
        lf = rng.uniform(*f)
        c = synth.click(lf, q=rng.uniform(5, 10), length=0.012, rng=rng,
                        body=None if body is None else [lf * m for m in body])
        b.at(rng.uniform(0, spread), c, rng.uniform(-6, 0))
    return b.x


def paw_tail(rng, length=0.4, puff=1.0, sparks=1.0):
    """What a hound's footfall leaves: a breath of ash and a few embers ticking as they settle."""
    b = Bus(length)
    b.at(0.0, ash(rng, rng.uniform(0.14, 0.24)), -10 + 6 * np.log10(puff + 1e-3))
    b.at(0.01, embers(rng, length, env([(0, 110), (length, 0)], length), hi=2200), -13 + 6 * np.log10(sparks + 1e-3))
    return b.x


BALLAST = [f"kenney_impact-sounds:footstep_snow_{i:03d}" for i in range(5)] + ["sfx_100_v2:footstep_01",
                                                                               "sfx_100_v2:footstep_02"]


def paw_ground(rng, k):
    hind = k % 2 == 1     # the hind feet land heavier
    w = rng.uniform(0.8, 1.0) * (1.25 if hind else 1.0)
    b = Bus(0.55)
    t0 = 0.02
    b.at(t0 - 0.012, claws(rng, int(rng.integers(1, 4)), (1800, 3600)), -15)
    b.at(t0 + rng.uniform(0, 0.006), kit.thud(rng, rng.uniform(65, 115), rng.uniform(0.06, 0.11), w), -14)
    pmf = lp(synth.noise(0.06, rng, "pink"), 650) * env([(0, 1), (0.06, 0)], 0.06, "exp")
    b.at(t0, unit(pmf), -12)
    # the ballast giving under it: a real crunch, barely bent, and the bigger stones knocking
    key = BALLAST[int(rng.integers(len(BALLAST)))]
    b.at(t0 + 0.004, unit(hp(clip(key, length=rng.uniform(0.09, 0.14), semis=rng.uniform(-2.5, 0.5)), 180)), -4 + 2 * w)
    b.at(t0 + 0.006, unit(kit.gravel(rng, int(rng.integers(5, 10)), 0.05, 600, 3200, body=0)), -12)
    for _ in range(int(rng.integers(1, 4))):    # a stone or two kicked loose, rolling off
        stone = synth.click(rng.uniform(1400, 3500), q=6, length=0.015, rng=rng)
        b.at(t0 + rng.uniform(0.06, 0.22), stone, rng.uniform(-28, -20))
    b.at(t0 + 0.01, paw_tail(rng, 0.42, puff=1.0 * w, sparks=rng.uniform(0.6, 1.2)), 0)
    return b.x


def paw_grate(rng, k):
    hind = k % 2 == 1
    w = rng.uniform(0.8, 1.0) * (1.25 if hind else 1.0)
    b = Bus(0.6)
    t0 = 0.02
    # iron answers the claws: ticks rung through a bar's inharmonic modes
    b.at(t0 - 0.014, claws(rng, int(rng.integers(2, 4)), (2600, 4200), body=[1.0, 2.76, 5.4], spread=0.012), -12)
    if rng.random() < 0.5:     # a claw skidding on the chequer plate
        sk = kit.creak(rng, 0.05, rng.uniform(250, 400), body=[2900, 4300, 6100], q=25)
        b.at(t0, dsp.shaped(sk, [(0, 1), (0.05, 0)]), -22)
    # the pad on the plate: the real plate impact with its top taken off by the pad, and the step's weight
    weight = ["light", "medium"][int(rng.integers(2))]
    pl = clip(f"kenney_impact-sounds:impactPlate_{weight}_{int(rng.integers(5)):03d}", length=0.35,
              semis=rng.uniform(-3, -1))
    b.at(t0, dsp.shaped(unit(lp(pl, rng.uniform(1800, 2600))), [(0, 1), (0.35, 0.01)], "exp"), -3)
    b.at(t0 + rng.uniform(0, 0.006), kit.thud(rng, rng.uniform(70, 120), rng.uniform(0.07, 0.12), w), -13)
    # the loose board rattling in its frame
    for i in range(int(rng.integers(1, 3))):
        r = clip(f"kenney_impact-sounds:impactMetal_light_{int(rng.integers(5)):03d}", None, 0.03, rng.uniform(-9, -5))
        b.at(t0 + 0.03 + 0.025 * i + rng.uniform(0, 0.01), r, rng.uniform(-26, -20))
    b.at(t0 + 0.01, paw_tail(rng, 0.42, puff=0.6, sparks=rng.uniform(0.8, 1.4)), 0)
    return b.x


def paw_roof(rng, k):
    hind = k % 2 == 1
    w = rng.uniform(0.8, 1.0) * (1.25 if hind else 1.0)
    b = Bus(0.6)
    t0 = 0.02
    b.at(t0 - 0.012, claws(rng, int(rng.integers(1, 4)), (2000, 3600), body=[1.0, 2.25, 3.6], spread=0.012), -13)
    # the roof sheet: a real tin knock dropped for a bigger sheet, and the sheet's own hollow ring under the pad
    tn = clip(f"kenney_impact-sounds:impactTin_medium_{int(rng.integers(5)):03d}", length=0.2,
              semis=rng.uniform(-6, -3))
    b.at(t0, unit(lp(tn, 3000)), -5)
    imp = np.pad(lp(rng.standard_normal(samples(0.008)).astype(np.float32), 900), (0, samples(0.35)))
    ring = dsp.resonate(imp, [f * rng.uniform(0.75, 0.95) for f in synth.TIN], q=22)
    b.at(t0, dsp.shaped(unit(ring), [(0, 1), (0.35, 0.01)], "exp"), -10)
    b.at(t0 + rng.uniform(0, 0.006), kit.thud(rng, rng.uniform(60, 105), rng.uniform(0.08, 0.15), w), -13)
    if rng.random() < 0.6:     # claws dragging on the painted tin as the foot pushes off
        slip = dsp.fit(synth.creak(0.07, 180, rng, body=[3000], q=4), samples(0.07))
        sc = bp(synth.noise(0.07, rng), 2000, 5000) * slip
        b.at(t0 + rng.uniform(0.04, 0.08), dsp.shaped(unit(sc), [(0, 0), (0.01, 1), (0.07, 0)]), -20)
    b.at(t0 + 0.01, paw_tail(rng, 0.42, puff=0.7, sparks=rng.uniform(0.8, 1.3)), 0)
    return b.x


def gallop(takes, rng, seconds=3.0, stride=0.38):
    """Paws fired the way the game fires them: a rotary gallop (four falls a stride, then the flight), random takes."""
    b = Bus(seconds + 0.6)
    t = 0.1
    while t < seconds:
        for off in (0.0, 0.055, 0.165, 0.215):
            x = takes[int(rng.integers(len(takes)))]
            b.at(t + off + rng.uniform(-0.008, 0.008), x, rng.uniform(-3, 0))
        t += stride * rng.uniform(0.96, 1.04)
    return b.x


def takes_then(fn):
    """A preview of the takes one by one, then fn's montage of them in play."""
    def pv(takes, rng):
        return np.concatenate([kit.scatter(takes, rng, (0.35, 0.5)), dsp.silence(0.4), fn(takes, rng)])
    return pv


PAW_SOURCES = {
    "ground": BALLAST,
    "grate": [f"kenney_impact-sounds:impactPlate_{w}_{i:03d}" for w in ("light", "medium") for i in range(5)]
             + [f"kenney_impact-sounds:impactMetal_light_{i:03d}" for i in range(5)],
    "roof": [f"kenney_impact-sounds:impactTin_medium_{i:03d}" for i in range(5)],
}
PAW_LABEL = {
    "ground": "A hound's paw on the ballast: claws, the pad, the stones giving, a puff of ash and embers",
    "grate": "A hound's paw on an iron running board: claws ringing on the plate, the pad's dull clank, ash and embers",
    "roof": "A hound's paw on the car roof: the tin's hollow knock under the pad, claws on the paint, ash and embers",
}
PAW_HOW = {
    "ground": """Toe-first like a real gallop: two or three claw ticks, then the pad (a soft low thud), then real
        footstep crunches (Kenney snow steps and the sfx gravel steps) barely bent so the ballast still sounds like
        ballast, with synthesised stones knocking and one kicked loose. What makes it wrong is the tail: a dry puff of
        ash off the hide and a few embers ticking as they settle. Odd takes are hind feet, heavier.""",
    "grate": """Claw ticks rung through an iron bar's modes, sometimes a claw skidding on the chequer plate, then the
        pad on the plate: a real Kenney plate impact with its top cut off by the soft pad, the board's weight, and the
        plate rattling in its frame. A smaller puff of ash and embers ticking on the iron. Odd takes are hind feet.""",
    "roof": """A real Kenney tin knock dropped a few semitones for a whole roof sheet, the sheet's hollow ring under the
        pad (a soft hit through a tin roof's modes), the dog's weight, claw ticks on the paint and sometimes a scratch
        as the foot pushes off. Ash and embers after. Odd takes are hind feet.""",
}

for _mat, _fn in (("ground", paw_ground), ("grate", paw_grate), ("roof", paw_roof)):
    recipe("cs-hounds", "paw", "pad", PAW_LABEL[_mat], PAW_HOW[_mat], sources=PAW_SOURCES[_mat], takes=6, mat=_mat,
           lufs=-22, gap=0.3, preview=takes_then(gallop))(lambda rng, k, fn=_fn: done(fn(rng, k)))


def scrabble(rng, length, rate, f=(1200, 3000), scratch=0.45):
    """Claws scrabbling for grip on boards: ticks of nail on wood, and short scrapes as a foot slides."""
    b = Bus(length + 0.1)
    rc = synth.curve(rate, samples(length))
    t = 0.0
    while True:
        t += rng.exponential(1 / max(rc[min(samples(t), len(rc) - 1)], 1.0))
        if t >= length:
            break
        if rng.random() < scratch:
            L = rng.uniform(0.02, 0.07)
            slip = dsp.fit(synth.creak(L, rng.uniform(150, 320), rng, body=[2600], q=3), samples(L))
            s = bp(synth.noise(L, rng), 1400, 5000) * slip
            b.at(t, dsp.shaped(unit(s), [(0, 0), (0.004, 1), (L, 0)]), rng.uniform(-10, -4))
        else:
            lf = rng.uniform(*f)
            nail = synth.click(lf, q=rng.uniform(4, 7), length=0.016, rng=rng, body=[lf, lf * 1.6, lf * 2.7])
            b.at(t, nail, rng.uniform(-6, 0))
    return b.x


def paw_wood(rng):
    """A paw on the guard van's boards, running: the claws, a real board footstep made light, the pad's bump."""
    b = Bus(0.3)
    b.at(0, claws(rng, int(rng.integers(1, 3)), (1300, 2800), body=[1.0, 1.6, 2.7]), -8)
    st = clip(f"kenney_impact-sounds:footstep_wood_{int(rng.integers(5)):03d}", length=0.16, semis=rng.uniform(1, 3))
    b.at(0.008, unit(lp(st, 4000)), -2)
    b.at(0.008, kit.thud(rng, 110, 0.08, 0.6), -14)
    return b.x


def run_off(rng, length, start_db=0.0):
    """Galloping away over the boards: skids first, then paws in a gallop's fours, each stride quieter and darker."""
    b = Bus(length + 0.4)
    b.at(0, scrabble(rng, 0.25, 45, f=(1100, 2600), scratch=0.5), start_db - 4)
    t = 0.12
    while t < length:
        g = start_db - 22 * t / length
        for off in (0.0, 0.05, 0.16, 0.21):
            p = paw_wood(rng)
            b.at(t + off + rng.uniform(-0.01, 0.01), lp(p, 6000 - 4000 * t / length), g + rng.uniform(-3, 0))
        t += rng.uniform(0.34, 0.4)
    return b.x


def huff(rng, length=0.18, f0=130):
    """A hard breath out with effort in it: a grunt's throat under the air, and the embers flaring with the blast."""
    sh = env([(0, 0), (0.015, 1), (length, 0)], length)
    br = synth.breath(length, [(0, "a"), (length, "h")], HOUND_TRACT, rng, shape=sh)
    g = throat(rng, [(0, f0), (length, f0 * 0.7)], length, "a", [(0, 0), (0.02, 1), (length, 0)], hiss=0.2, fire=0.0)
    return mix(unit(br), g * 0.5, bellows(rng, length, sh) * 0.6)


def leap(rng, k):
    b = Bus(2.6)
    # the last strides on the ballast, then both hind feet driving off together and the stones sprayed back
    for i, t in enumerate((0.0, 0.055, 0.17)):
        b.at(t, paw_ground(rng, i), -4)
    push = 0.36
    b.at(push, paw_ground(rng, 1), 0)
    b.at(push + 0.018, paw_ground(rng, 3), -1)
    b.at(push + 0.01, unit(kit.gravel(rng, 26, 0.22, 600, 4200, body=0)), -12)
    b.at(push - 0.03, huff(rng, 0.2, rng.uniform(120, 150)), -3)
    # through the air: its body past you, sparks streaming off the cracks in its hide
    air = rng.uniform(0.38, 0.48)
    b.at(push + 0.04, kit.whoosh(rng, air + 0.1, 220, 1600, 0.55), -10)
    b.at(push + 0.04, embers(rng, air, 260, hi=1600), -11)
    # the rear platform takes it: real plank and heavy wood knocks under the weight, the railing ringing
    land = push + air
    plank = clip(f"kenney_impact-sounds:impactPlank_medium_{int(rng.integers(5)):03d}", length=0.6, semis=-2)
    b.at(land, unit(plank), 0)
    b.at(land + 0.012, unit(clip(f"kenney_impact-sounds:impactWood_heavy_{int(rng.integers(5)):03d}", length=0.3)), -4)
    b.at(land, kit.thud(rng, 58, 0.3, 1.3), -6)
    rail = clip(f"kenney_impact-sounds:impactMetal_heavy_{int(rng.integers(5)):03d}", length=0.4, semis=-7)
    b.at(land + 0.02, unit(lp(rail, 2500)), -18 if k == 0 else -9)
    # claws hunting for grip on the boards (the second take skids), a cloud of ash and a spray of embers off the deck
    grip = 0.35 if k == 0 else 0.7
    b.at(land + 0.01, scrabble(rng, grip, env([(0, 40), (grip, 10)], grip)), -8)
    b.at(land, ash(rng, 0.5, 400, 4500), -6)
    b.at(land + 0.01, embers(rng, 1.0, env([(0, 700), (0.15, 200), (1.0, 0)], 1.0), hi=1500), -6)
    # and it's up, snarling
    s = land + grip * 0.7 + rng.uniform(0.0, 0.06)
    L = rng.uniform(0.5, 0.7)
    b.at(s, throat(rng, [(0, 95), (L * 0.3, 135), (L, 100)], L, [(0, "u"), (0.15, "a"), (L, "e")],
                   [(0, 0), (0.06, 1), (L * 0.7, 0.8), (L, 0)]), -4)
    return b.x


@recipe("cs-hounds", "leap", "board",
        "A hound's last strides on the ballast, the jump, and its landing on the rear platform, claws hunting for grip",
        """Three gallop paws, then both hind feet driving off with the stones sprayed back and a hard huff of effort
        that fans its embers. Through the air, a body-sized whoosh with sparks streaming off it. It lands on the guard
        van's deck: real Kenney plank and heavy wood knocks under its weight, the iron railing ringing (a heavy metal
        knock dropped and dulled), claws scrabbling on the boards, a cloud of ash and a spray of embers, then a snarl.
        The second take skids further and hits the railing.""",
        sources=BALLAST + [f"kenney_impact-sounds:impactPlank_medium_{i:03d}" for i in range(5)]
        + [f"kenney_impact-sounds:impactWood_heavy_{i:03d}" for i in range(5)]
        + [f"kenney_impact-sounds:impactMetal_heavy_{i:03d}" for i in range(5)], takes=2)
def hound_leap(rng, k):
    return done(leap(rng, k))


# Snarls: four performances, the same throats. Each is a breath or two out with the teeth bared (the vowel pulled to
# 'e' as the lips draw back), an in-growl between, and the occasional snap or bark.

def snarl_takes(rng, k, voice):
    """The four snarls. `voice(rng, pts, L, vowels, shape)` is one exhaled snarl in the candidate's throat."""
    b = Bus(2.4)
    if k == 0:     # a long, low warning, the breath drawn in first
        b.at(0, inhale(rng, 0.36, 0.8), -5)
        L = 1.5
        vow = [(0, "u"), (0.25, "a"), (0.9, "e"), (L, "a")]
        b.at(0.33, voice(rng, [(0, 76), (0.3, 94), (1.0, 106), (L, 82)], L, vow,
                         [(0, 0), (0.12, 0.8), (0.5, 1), (1.2, 0.85), (L, 0)]))
    elif k == 1:   # rising into a snap of the jaws
        L = 0.95
        b.at(0, voice(rng, [(0, 88), (0.45, 128), (0.8, 210), (L, 170)], L, [(0, "a"), (0.5, "e"), (L, "a")],
                      [(0, 0), (0.1, 0.7), (0.8, 1), (L, 0.3)]))
        b.at(L - 0.03, jaw_snap(rng), -2)
        b.at(L + 0.09, inhale(rng, 0.25, 0.5), -10)
    elif k == 2:   # two short vicious bursts with a ragged breath between
        L1, L2 = rng.uniform(0.38, 0.46), rng.uniform(0.5, 0.6)
        b.at(0, voice(rng, [(0, 108), (L1 * 0.4, 150), (L1, 118)], L1, [(0, "a"), (L1, "e")],
                      [(0, 0), (0.04, 1), (L1, 0)]))
        b.at(L1 + 0.02, inhale(rng, 0.2, 0.7), -6)
        b.at(L1 + 0.22, voice(rng, [(0, 118), (L2 * 0.35, 172), (L2, 96)], L2, [(0, "a"), (0.2, "e"), (L2, "u")],
                              [(0, 0), (0.03, 1), (L2 * 0.6, 0.8), (L2, 0)]))
    else:          # a rumble that breaks into one bark, and settles back into the rumble
        L = 0.75
        b.at(0, voice(rng, [(0, 72), (L, 84)], L, [(0, "u"), (L, "o")], [(0, 0), (0.2, 0.7), (L, 0.9)]), -3)
        B = 0.24
        b.at(L - 0.02, voice(rng, [(0, 190), (0.04, 290), (B, 170)], B, [(0, "a"), (B, "o")],
                             [(0, 0), (0.012, 1), (B, 0)]), 2)
        L3 = 0.7
        b.at(L + B - 0.04, voice(rng, [(0, 92), (L3, 78)], L3, [(0, "a"), (0.3, "e"), (L3, "u")],
                                 [(0, 0.8), (L3, 0)]), -3)
    return b.x


@recipe("cs-hounds", "snarl", "throat",
        "A big dog's snarl gone dry and hot: a rough throat, bared teeth hissing, embers crackling in every breath",
        """Synthesised throats: a glottis at 75-130 Hz with fry and period doubling (the rattle of a growl) through a
        big dog's vocal tract, the vowel pulled toward 'e' as the lips draw back off the teeth, with the teeth's hiss
        pulsing in time with it. What's wrong is the breath: no spit, but embers crackling in it, denser the harder it
        blows, and on each in-growl the chest draws like a firebox (a low roar and a flare of crackle). Four
        performances: a long warning, a rise into a snap of the jaws, two vicious bursts, a rumble that breaks into one
        bark.""",
        takes=4)
def snarl_throat(rng, k):
    return done(snarl_takes(rng, k, throat))


@recipe("cs-hounds", "snarl", "cinder",
        "The same snarls from a throat burnt out: barely voiced, a growl whose rattle is made of ember crackle",
        """A wronger dog. The growl's pulses (an irregular 26-44 a second, like a real growl's fry) are bursts of dense
        ember crackle and dry breath rung through the hound's vocal tract, with only a trace of voice in them, so it
        snarls in ash rather than in a throat; a fire's low roar swells under each breath and loose embers crackle over
        it. The same four performances as 'throat', so the two can be compared like for like.""",
        takes=4)
def snarl_cinder(rng, k):
    return done(snarl_takes(rng, k, cinder_voice))


# The bite: the lunge's breath, teeth through a coat into the arm, and (in most takes) the hold and the shake.

def rip(rng, length, lo=500, hi=4000, wet=0.0, pull=None):
    """Something tearing: fibres snapping one after another, faster as it gives (`pull` shapes it); wet if flesh."""
    shape = pull or [(0, 0.3), (length * 0.7, 1), (length, 0)]
    e = dsp.fit(env(shape, length), samples(length))
    fib = synth.crackle(length, 300 + 2500 * e, rng, size=(0.0002, 0.0012), hi=lo)
    y = bp(fib, lo, hi) * (0.4 + 0.6 * e)
    if wet:
        y = mix(unit(y), unit(synth.bubbles(length, 90 * e + 10, 1100, 3500, rng, rise=(0.02, 0.12))) * wet * 0.6)
    return unit(y)


def sear(rng, length=0.3):
    """Ember-hot teeth in flesh: a short scorching hiss with the fat spitting in it."""
    h = bp(synth.noise(length, rng), 2500, 8000) * env([(0, 0), (0.02, 1), (length * 0.4, 0.4), (length, 0)], length)
    sp = synth.crackle(length, env([(0, 600), (length, 50)], length), rng, size=(0.0003, 0.002), hi=2500)
    return mix(unit(h) * 0.5, unit(sp))


CLOTH = ["kenney_rpg-audio:cloth1", "kenney_rpg-audio:cloth2", "kenney_rpg-audio:cloth3", "kenney_rpg-audio:cloth4",
         "kenney_rpg-audio:handleSmallLeather"]


def shake(rng, length):
    """Jaws locked in a sleeve and the head thrashing: a growl shut in its mouth, the cloth rucking, tearing in time."""
    rate = rng.uniform(5.0, 7.0)
    g = throat(rng, [(0, 95), (length, 110)], length, "u", [(0, 0), (0.05, 1), (length * 0.8, 0.9), (length, 0)],
               hiss=0.1, fire=0.3)
    g = dsp.tremolo(lp(g, 1100), rate, 0.55, rng, 0.3)
    tear = rip(rng, length, 600, 4500, wet=0.3, pull=[(0, 0.2), (length * 0.5, 0.6), (length * 0.9, 1), (length, 0)])
    tear = dsp.tremolo(tear, rate, 0.8, rng, 0.3)
    b = Bus(length + 0.2)
    b.at(0, g)
    b.at(0, tear, -6)
    for i in range(int(length * rate)):     # the coat rucked with each wrench
        c = clip(CLOTH[int(rng.integers(4))], length=0.12, semis=rng.uniform(-3, 0))
        b.at(i / rate + rng.uniform(0, 0.02), unit(c), rng.uniform(-14, -8))
    return b.x


def bite(rng, k):
    b = Bus(2.0)
    L = rng.uniform(0.14, 0.2)
    b.at(0, throat(rng, [(0, 120), (L, 175)], L, "a", [(0, 0), (0.04, 1), (L, 0.7)], hiss=0.6), -5)
    t = L - 0.01
    # the jaws: teeth through the coat (the clack muffled by the cloth), the puncture, the heat
    b.at(t, lp(jaw_snap(rng, 0.6), 3000), -4)
    b.at(t, unit(clip(CLOTH[int(rng.integers(len(CLOTH)))], length=0.25)), -3)
    b.at(t + 0.006, squish(rng, 0.18, 300, 1500), -7)
    b.at(t + 0.01, sear(rng, rng.uniform(0.25, 0.35)), -15)
    if k == 2:     # through to the bone of the forearm: a crunch
        b.at(t + 0.02, mix(kit.snap(rng, 0.08, rng.uniform(900, 1300)),
                           unit(clip("sfx_100_v2:misc_34", 0.44, 0.12, semis=-6)) * 0.7), -4)
    if k == 3:     # snapping twice, the first only finding cloth
        b.at(t + 0.28, throat(rng, [(0, 130), (0.12, 170)], 0.12, "a", [(0, 0), (0.02, 1), (0.12, 0.6)]), -6)
        t += 0.38
        b.at(t, jaw_snap(rng, 0.7), -3)
        b.at(t, unit(clip(CLOTH[int(rng.integers(4))], length=0.2)), -6)
        b.at(t + 0.005, squish(rng, 0.2, 300, 1500), -6)
        b.at(t + 0.01, sear(rng, 0.3), -15)
    if k in (1, 2):
        b.at(t + 0.12, shake(rng, rng.uniform(0.6, 0.85)), -4)
    return b.x


@recipe("cs-hounds", "bite", "jaws",
        "A hound's jaws closing through a crewman's coat into the arm: the clack, the cloth, the flesh, a scorch",
        """A short snarl going in, then the bite: the jaws' clack (a dry tooth crack over a skull's hollow knock)
        muffled by the cloth, a real Kenney cloth or leather grab, the teeth going in (a wet squelch) and a brief scorch
        of hot teeth in flesh, fat spitting. Two takes hold on and shake: a growl shut in its mouth, the coat rucking
        and tearing in time with the wrenches (fibres snapping). One crunches through to bone (a crack from an sfx
        break, dropped half an octave); one snaps twice, the first only finding cloth.""",
        sources=CLOTH + ["sfx_100_v2:misc_34"], takes=4)
def hound_bite(rng, k):
    return done(bite(rng, k))


def yelp(rng, k):
    b = Bus(2.4)
    # the blow knocks a cough of ash and a spray of sparks out of its hide
    b.at(0, ash(rng, 0.35, 350, 4000), -5)
    b.at(0, embers(rng, 0.7, env([(0, 1000), (0.1, 300), (0.7, 0)], 0.7), hi=1500), -5)
    t = 0.02
    for i in range((3, 2, 4)[k]):     # ki-yi-yi: each cry a scoop up to its top and a fall, lower and weaker
        top = rng.uniform(760, 920) * 0.9 ** i
        L = rng.uniform(0.15, 0.24) * (1 - 0.08 * i)
        v = throat(rng, [(0, top * 0.6), (L * 0.22, top), (L, top * 0.5)], L, [(0, "i"), (L * 0.3, "a"), (L, "u")],
                   [(0, 0), (0.012, 1), (L * 0.5, 0.75), (L, 0)], rasp=0.2, fry_rate=(40, 60), sub=0.15, hiss=0.12,
                   fire=0.4, tract=0.62, jitter=0.008)
        b.at(t, v, -2.5 * i)
        t += L + rng.uniform(0.06, 0.13)
    # scrambling off over the boards, going away: claws, then quieter and darker as it goes
    b.at(t - 0.08, run_off(rng, rng.uniform(1.0, 1.3)), -5)
    if k != 1:     # one last whimper from further off
        L = 0.35
        w = throat(rng, [(0, 620), (0.1, 700), (L, 480)], L, [(0, "i"), (L, "u")], [(0, 0), (0.05, 0.8), (L, 0)],
                   rasp=0.1, sub=0.0, hiss=0.05, fire=0.3, tract=0.62, jitter=0.01)
        far = lp(dsp.room(w, "night", wet=0.2, tail=False, rng=rng), 2500)
        b.at(t + 0.75, dsp.fade(far, 0.01, 0.08), -14)
    return b.x


@recipe("cs-hounds", "yelp", "kiyi",
        "A hound driven off: a cough of ash and sparks from the blow, a broken ki-yi, claws scrambling away",
        """The blow knocks a puff of ash and a spray of embers off its hide. Then a big dog's yelps (a synthesised
        throat scooping up to 750-900 Hz and falling, 'ee' opening to 'ah'), two to four in a run, each lower and
        weaker, with the same crackle in the breath as its snarls. Its claws skid, then it gallops off over the boards
        (real Kenney wood footsteps made light, under claw ticks), quieter and darker each stride; in two takes one last
        whimper further off.""",
        sources=[f"kenney_impact-sounds:footstep_wood_{i:03d}" for i in range(5)], takes=3)
def hound_yelp(rng, k):
    return done(yelp(rng, k))


# ---- Ribbits
# -------------------------------------------------------------------------------------------------------------
# Toads the size of a big dog: wet, hairless, a loose belly, a throat sac, a row of flat human teeth. They eat like
# people
# chewing with their mouths open, then gulp through the sac. `size` scales a mouth (1 the pack's average).

CRACKS = ["sfx_100_v2:misc_33", "sfx_100_v2:misc_34", "sfx_100_v2:misc_35", "sfx_100_v2:stones_01",
          "sfx_100_v2:stones_02"]


def chew(rng, size=1.0, teeth=0.5):
    """One champ of a mouthful: the jaw closing on wet meat, human teeth meeting through it, the lips coming apart."""
    L = rng.uniform(0.12, 0.2) * size
    b = Bus(L + 0.2)
    b.at(0, squish(rng, L, 260 / size, 1200 / size), 0)
    if rng.random() < teeth:     # flat teeth knocking through the food: a dull tock, not a click
        f = rng.uniform(1100, 1700) / size
        tock = synth.click(f, q=5, length=0.03, rng=rng, body=[f, f * 1.45, f * 2.2])
        b.at(L * rng.uniform(0.3, 0.6), lp(tock, 3500), -8)
    lips = mix(bp(synth.noise(0.04, rng), 1200, 5000) * env([(0, 1), (0.04, 0)], 0.04, "exp"),
               synth.bubbles(0.06, 120, 1800, 4500, rng, rise=(0.02, 0.1)) * 0.6)
    b.at(L * rng.uniform(0.8, 1.0), unit(lips), rng.uniform(-14, -8))
    return b.x


def gulp(rng, size=1.0):
    """A swallow through the throat sac: the gullet's click, a low glug sinking (down, never up), the sac's boom."""
    L = 0.4
    n = samples(L)
    t = np.arange(n) / SR
    f = rng.uniform(85, 120) / size
    glug = np.sin(2 * np.pi * np.cumsum(f * (1 - 0.35 * t / L)) / SR) * np.exp(-t / 0.07) * (1 - np.exp(-t / 0.006))
    imp = np.pad(rng.standard_normal(samples(0.01)).astype(np.float32), (0, n))
    sac = dsp.resonate(imp, [175 / size, 410 / size, 690 / size], q=7)
    b = Bus(L + 0.1)
    b.at(0, lp(synth.click(rng.uniform(500, 700), q=3, length=0.02, rng=rng), 1500), -10)
    b.at(0.02, unit(glug.astype(np.float32)), -2)
    b.at(0.03, dsp.shaped(unit(sac), [(0, 1), (0.25, 0.05)], "exp"), -6)
    b.at(0.0, squish(rng, 0.2, 200 / size, 700 / size, 0.5), -8)
    return b.x


def tear_meat(rng, length=0.5, size=1.0):
    """Meat pulled off the bone in the teeth: fibres giving faster and faster, wet, a grunt through the nose."""
    b = Bus(length + 0.3)
    b.at(0, rip(rng, length, 450 / size, 3200, wet=0.8, pull=[(0, 0.1), (length * 0.85, 1), (length, 0)]), -2)
    b.at(length * 0.85, squish(rng, 0.2, 300, 1400), -4)
    grunt = synth.breath(length, "m", 0.55 * size, rng, shape=env([(0, 0), (length * 0.7, 1), (length, 0)], length))
    b.at(0, lp(grunt, 1500), -12)
    return b.x


def crunch(rng, size=1.0):
    """Gristle or a small bone giving between the molars: a real crack slowed and darkened, with a wet squeeze."""
    c = clip(CRACKS[int(rng.integers(len(CRACKS)))], length=0.12, semis=-rng.uniform(5, 8))
    b = Bus(0.35)
    b.at(0, unit(lp(c, 4500)), 0)
    b.at(0, kit.snap(rng, 0.07, rng.uniform(800, 1300) / size), -6)
    b.at(0.01, squish(rng, 0.15, 300, 1300), -6)
    return b.x


def snort(rng, size=1.0):
    """Breath out through wet nostrils between mouthfuls."""
    L = rng.uniform(0.1, 0.18)
    br = synth.breath(L, "m", 0.6 * size, rng, shape=env([(0, 0), (0.015, 1), (L, 0)], L))
    return mix(unit(br), unit(synth.bubbles(L, 200, 2500, 5000, rng, rise=(0.02, 0.1))) * 0.3)


def croak(rng, length=0.4, f0=(80, 70), flutter=18.0, size=1.0, shape=None):
    """A toad's croak: a low throat pulsed by the sac `flutter` times a second (the pack's swell is 18), rung in it."""
    g = synth.glottis(env([(0, f0[0] / size), (length, f0[1] / size)], length, curve="exp"), length, rng, jitter=0.02,
                      shimmer=0.2, sub=0.3, oq=0.5)
    fr = fry(rng, flutter, length, jitter=0.12, decay=0.018)
    v = synth.tract(g * (0.2 + 0.8 * fr), "o", 0.42 * size ** -0.3, breath=0.2, rng=rng)
    v = mix(unit(v), unit(dsp.resonate(g * fr, [180 / size, 420 / size], q=6)) * 0.5)
    return unit(dsp.shaped(unit(v), shape or [(0, 0), (0.04, 1), (length * 0.7, 0.8), (length, 0)]))


def mouth(rng, length, size, tempo, frenzy=False):
    """One toad at the catch for `length` s: runs of chewing at its own tempo, broken by tearing, crunching, gulps,
    snorts and the odd croak. A frenzy tears more, chews faster and swallows less."""
    b = Bus(length + 1.0)
    t = rng.uniform(0, 0.6)
    while t < length:
        if rng.random() < (0.55 if frenzy else 0.3):
            L = rng.uniform(0.35, 0.7)
            b.at(t, tear_meat(rng, L, size), rng.uniform(-3, 0))
            t += L + rng.uniform(0.05, 0.15)
        for _ in range(int(rng.integers(3, 8))):
            b.at(t, chew(rng, size), rng.uniform(-4, 0))
            if rng.random() < (0.18 if frenzy else 0.1):
                b.at(t + rng.uniform(0.02, 0.08), crunch(rng, size), rng.uniform(-4, 0))
            t += tempo * rng.uniform(0.8, 1.25)
        r = rng.random()
        if r < (0.2 if frenzy else 0.45):
            b.at(t, gulp(rng, size), -1)
            t += 0.45
        elif r < 0.75:
            b.at(t, snort(rng, size), -8)
            t += 0.2
        if rng.random() < (0.08 if frenzy else 0.15):
            b.at(t, croak(rng, rng.uniform(0.3, 0.5), (rng.uniform(75, 90), 68), size=size), -6)
            t += 0.4
        t += rng.uniform(0.15, 1.1)
    return b.x


def squabble(rng):
    """Two of them at the same mouthful: a hiss through teeth, a snap, an angry rising croak, a tug that tears."""
    b = Bus(1.2)
    L = rng.uniform(0.25, 0.4)
    h = bp(synth.noise(L, rng), 1500, 6000) * env([(0, 0), (0.02, 1), (L, 0)], L)
    b.at(0, unit(mix(h, synth.bubbles(L, 200, 2000, 5000, rng, rise=(0.02, 0.1)) * 0.3)), -6)
    b.at(L * 0.8, lp(jaw_snap(rng, 0.8), 4000), -4)
    b.at(L * 0.7, croak(rng, 0.35, (rng.uniform(110, 130), 85), flutter=26, size=0.9), -3)
    b.at(L + 0.1, tear_meat(rng, rng.uniform(0.4, 0.6)), 0)
    return b.x


FEED_L = 12.3       # one cycle (build.py's seam crossfade takes 0.3 s of it)


def feed(rng, frenzy):
    n = samples(FEED_L)
    b = Bus(FEED_L + 2.0)
    pack = [(1.2, 0.5, 0.0, 12000), (1.0, 0.38, -2.5, 9000), (0.85, 0.3, -5.0, 7000)]     # size, chew tempo, dB, top
    for size, tempo, db, top in pack:
        m = mouth(rng, FEED_L, size, tempo * (0.8 if frenzy else 1.0), frenzy)
        b.at(0, lp(m, top), db)
    if frenzy:
        for t in np.sort(rng.uniform(0.5, FEED_L - 1, 3)):
            b.at(t, squabble(rng), -2)
    y = dsp.room(b.x, "night", wet=0.08, rng=rng)
    return dsp.wrap(y, n)


@recipe("cs-ribbits", "feed", "gorge",
        "Three of them eating a catch like people chewing with their mouths open, gulping through their throat sacs",
        """Not a grain cloud: three toads, each its own size and chewing tempo, doing what eating is. Runs of champing
        (a wet squish as the jaw closes, flat human teeth tocking dully through the food, the lips smacking apart), meat
        torn off in the teeth (fibres giving faster and faster, wet), gristle crunching (real sfx cracks slowed half an
        octave and darkened), then a swallow through the sac: a sinking glug and the sac's hollow boom. Snorts through
        wet nostrils between, and now and then a contented croak with the pack's 18 Hz flutter. A seamless 12 s loop.
        """,
        sources=CRACKS, loop=True, takes=1)
def feed_gorge(rng, k):
    return done(feed(rng, False))


@recipe("cs-ribbits", "feed", "frenzy",
        "The pack fighting over the catch: tearing and tugging, bones going, hissing and snapping at each other",
        """The same three mouths in a frenzy: more tearing than chewing, faster jaws, more bone and gristle crunching
        (real sfx cracks slowed and darkened), fewer swallows, and three squabbles over a mouthful: a hiss through the
        teeth, a snap, an angry croak fluttering faster than the swell, and a tug that tears the meat between them. A
        seamless 12 s loop.""",
        sources=CRACKS, loop=True, takes=1)
def feed_frenzy(rng, k):
    return done(feed(rng, True))


def belly(rng, length=0.5, size=1.0):
    """A fat loose body shaking after a blow: the belly's low wobble dying away at a few times a second."""
    imp = np.pad(lp(rng.standard_normal(samples(0.02)).astype(np.float32), 600), (0, samples(length)))
    r = dsp.resonate(imp, [120 / size, 190 / size, 300 / size], q=4)
    wob = dsp.tremolo(unit(r), rng.uniform(6.5, 9.0), 0.85)
    return dsp.shaped(unit(wob), [(0, 1), (length, 0.02)], "exp")


def ribbit_hit(rng, k):
    b = Bus(1.6)
    heavy = (1.0, 0.9, 1.3, 0.6)[k]
    # the blow: wet hairless skin smacked, the weight under it, the belly shaking, the wet in it squeezed
    b.at(0, kit.slap(rng, 0.09, 3200, wet=0), 1 + 3 * heavy)
    b.at(0, kit.thud(rng, 72, 0.18, heavy), 0)
    b.at(0, unit(clip("sfx_100_v2:wood_01", length=0.12, semis=-rng.uniform(4, 6))), -1)
    wet = clip(f"sfx_100_v2:footstep_wet_0{int(rng.integers(1, 4))}", length=0.18, semis=-rng.uniform(4, 7))
    b.at(0.004, unit(wet), -4)
    b.at(0.01, squish(rng, 0.25, 230, 1100), -3)
    b.at(0.02, belly(rng, 0.5), -9 + 4 * heavy)
    if k == 1:     # caught on the head: the jaw slammed shut, the human teeth clacking
        b.at(0.005, lp(jaw_snap(rng, 1.0), 5000), -2)
    # the sac squeezed: air driven out of it as a pulsed distress croak
    t = rng.uniform(0.06, 0.12)
    L = rng.uniform(0.3, 0.5) if k != 3 else 0.22
    distress = croak(rng, L, (rng.uniform(170, 210), rng.uniform(80, 100)), flutter=rng.uniform(24, 30))
    b.at(t, dsp.saturate(distress, 6), -7)
    if k == 2:     # the wind knocked out of it: the sac emptying with a wheeze through the throat
        W = 0.6
        wz = dsp.sweep_filter(synth.noise(W, rng), "bp", env([(0, 1800), (W, 700)], W, "exp"), q=9)
        b.at(t + L - 0.05, dsp.shaped(unit(wz), [(0, 0), (0.05, 1), (W, 0)]), -10)
        b.at(t + L + 0.4, croak(rng, 0.3, (85, 70), size=1.1), -10)
    if k == 3:     # a glancing blow: it hisses back
        H = 0.35
        b.at(t + L, unit(bp(synth.noise(H, rng), 1500, 6000) * env([(0, 0), (0.02, 1), (H, 0)], H)), -10)
    return b.x


@recipe("cs-ribbits", "hit", "clubbed",
        "A club landing on a fat wet toad-body: skin smacked, the belly wobbling, the throat sac squeezed into a croak",
        """The blow: a smack of wet hairless skin, the weight of the body under it, the attack of a real liquid squish
        and a real wet footstep slowed into the flesh giving, and the loose belly shaking after (low resonances wobbling
        7-9 times a second as they die). The throat sac squeezed by the blow forces out a pulsed distress croak, higher
        and faster than the pack's swell. One take catches the head (its human teeth clack shut), one knocks the wind
        out of it (the sac empties with a wheeze), one glances off and it hisses back.""",
        sources=["sfx_100_v2:wood_01", "sfx_100_v2:footstep_wet_01", "sfx_100_v2:footstep_wet_02",
                 "sfx_100_v2:footstep_wet_03"], takes=4)
def ribbit_clubbed(rng, k):
    return done(ribbit_hit(rng, k))


# ---- The Grumbler
# ------------------------------------------------------------------------------------------------------------
# A dock labourer gone wrong (tools/blender/grumbler.py): face-down like a spider on bare hands and feet, a second pair
# of arms out through the ribs, cheeks split back to the ears over too many teeth, still muttering to himself. What
# touches the crane is skin: flat palms and soles slapped on iron, nails dragging, the girder answering under him.

SLAPS = ["sfx_100_v2:switch_01", "sfx_100_v2:switch_02"]
PLATES = [f"kenney_impact-sounds:impactPlate_heavy_{i:03d}" for i in range(5)]
BEAM = [1.0, 2.756, 5.404, 8.933, 13.34]    # a free bar's bending modes: the crane's girders


def girder(rng, f1=None):
    """A crane girder's modes (an I-beam rings like a long bar, inharmonic)."""
    f1 = f1 or rng.uniform(150, 210)
    return [f1 * m for m in BEAM]


def palm(rng, beam, size=1.0, surface="iron"):
    """A bare hand or foot slapped flat on a girder: a real slap made broad, the weight, and the iron answering low and
    long; on the ground, the dirt instead of the ring."""
    b = Bus(0.6)
    s = clip(SLAPS[int(rng.integers(2))], length=0.12, semis=-rng.uniform(3, 6) * size)
    b.at(0, unit(lp(s, 5000)), 0)
    b.at(0, kit.thud(rng, 95 / size, 0.08, 0.7), -12)
    if surface == "iron":
        imp = np.pad(lp(rng.standard_normal(samples(0.004)).astype(np.float32), 2000), (0, samples(0.55)))
        ring = dsp.resonate(imp, [f * rng.uniform(0.995, 1.005) for f in beam], q=60, gains=[0.6, 0.8, 0.6, 0.4, 0.25])
        b.at(0, unit(ring), -25)
        knock = lp(clip(PLATES[int(rng.integers(5))], length=0.15, semis=-2), 1200)
        b.at(0, dsp.shaped(unit(knock), [(0, 1), (0.15, 0.01)], "exp"), -19)
    else:
        b.at(0.002, unit(hp(clip(BALLAST[int(rng.integers(len(BALLAST)))], length=0.1), 200)), -10)
    return b.x


def nails(rng, length=None):
    """Nails dragging on rusty iron as a hand slides: a short stick-slip scrape."""
    L = length or rng.uniform(0.04, 0.09)
    slip = dsp.fit(synth.creak(L, rng.uniform(180, 350), rng, body=[3800], q=3), samples(L))
    s = bp(synth.noise(L, rng), 2500, 7000) * slip
    return dsp.shaped(unit(s), [(0, 0), (0.005, 1), (L, 0)])


def mutter(rng, length, f0=100, rise=1.0, rate=5.0, open_=0.25):
    """A man grumbling to himself in no words: syllables at about `rate` a second on mostly shut vowels, a creaky low
    voice that drifts and sags at the end of each phrase. `rise` bends the pitch over the length (a temper rising)."""
    vow, pts, shape = [], [], []
    t = 0.0
    while t < length:
        v = rng.choice(["m", "u", "o", "m", "e", "a"] if rng.random() < open_ else ["m", "u", "o", "m"])
        vow.append((t, str(v)))
        p = f0 * (1 + (rise - 1) * t / length) * rng.uniform(0.92, 1.1)
        pts.append((t, p))
        d = rng.uniform(0.6, 1.4) / rate
        shape += [(t, 0.25), (t + d * 0.35, rng.uniform(0.6, 1.0)), (t + d * 0.9, 0.3)]
        t += d
    pts.append((length, pts[-1][1] * 0.85))
    shape.append((length + 0.01, 0.0))
    return throat(rng, pts, length, vow, shape, rasp=0.45, fry_rate=(28, 38), sub=0.3, hiss=0.12, fire=0.0, tract=0.7,
                  jitter=0.02)


def scuttle(rng, length, beat=0.13, beam=None, surface="iron", accel=1.0, db=0.0):
    """Very low and very fast, the limbs in diagonal pairs: each beat a hand and the opposite foot slapped down a few
    milliseconds apart, now and then a rib arm's smaller slap and a nail dragging; the pace pushing on by `accel`."""
    beam = beam or girder(rng)
    b = Bus(length + 0.8)
    t = 0.0
    while t < length:
        g = db + rng.uniform(-3, 0)
        b.at(t, palm(rng, beam, 1.0, surface), g)
        b.at(t + rng.uniform(0.008, 0.03), palm(rng, beam, 0.9, surface), g - rng.uniform(1, 4))
        if rng.random() < 0.25:
            b.at(t + rng.uniform(0.04, 0.07), palm(rng, beam, 0.7, surface), g - 8)
        if rng.random() < 0.3:
            b.at(t + rng.uniform(0.0, 0.03), nails(rng), g - 10)
        t += beat * rng.uniform(0.82, 1.18) / (1 + (accel - 1) * t / max(length, 1e-3))
    return b.x


def crane_creak(rng, length, beam):
    """The crane's members taking his weight: a slow iron stick-slip through the girder."""
    return synth.creak(length, rng.uniform(8, 16), rng, body=[f * 2 for f in beam], q=30,
                       shape=env([(0, 0), (length * 0.3, 1), (length, 0)], length))


def rust(rng, length=0.6):
    """Rust and grit shaken off the girder, pattering down."""
    return unit(synth.crackle(length, env([(0, 120), (length, 0)], length), rng, size=(0.0003, 0.002), hi=2500))


def grumbler_scuttle(rng, k):
    beam = girder(rng)
    b = Bus(3.0)
    runs = [[(0.0, 1.5, 0.12)], [(0.0, 0.55, 0.11), (0.95, 0.5, 0.11)], [(0.0, 1.4, 0.19)], [(0.0, 1.0, 0.125)]][k]
    end = 0.0
    for t0, L, beat in runs:
        b.at(t0, scuttle(rng, L, beat, beam), 0)
        b.at(t0 + 0.05, mutter(rng, L, rng.uniform(90, 110), rate=6), -12)
        b.at(t0 + L, rust(rng, 0.7), -18)
        end = t0 + L
    if k in (2, 3):     # his weight on the jib: the iron creaks
        b.at(0.3 if k == 2 else end - 0.1, crane_creak(rng, 0.9, beam), -14)
    if k in (0, 3):     # stopped, he grumbles on to himself
        b.at(end + 0.05, mutter(rng, rng.uniform(0.8, 1.1), rng.uniform(88, 100), rate=4.5), -6)
    return outdoors(b.x, rng)


@recipe("cs-grumbler", "scuttle", "palms",
        "Bare palms and soles slapped flat on the crane's iron in fast diagonal pairs, the girder ringing low under it",
        """The Grumbler is a man on hands and bare feet, so this is skin, not chitin: real slaps (sfx hand smacks
        dropped a few semitones into broad flat palms) in diagonal pairs a few milliseconds apart, now and then a rib
        arm's smaller slap and nails dragging on rust, each landing answered by the girder (a real heavy plate knock,
        darkened, and the long faint ring of a bar's modes). Under it he mutters to himself, a creaky man's voice on
        shut vowels; rust patters down after. Takes: a long run, two darts, a slower heavy climb with the jib creaking,
        a run and a grumble.""",
        sources=SLAPS + PLATES, takes=4)
def scuttle_palms(rng, k):
    return done(grumbler_scuttle(rng, k))


def gnash(rng, length=0.45, rate=18.0):
    """Too many teeth chattering in a rage: tooth on tooth, fast and uneven."""
    b = Bus(length + 0.1)
    t = 0.0
    while t < length:
        f = rng.uniform(1600, 2600)
        tooth = synth.click(f, q=rng.uniform(5, 9), length=0.02, rng=rng, body=[f, f * 1.5, f * 2.3])
        b.at(t, tooth, rng.uniform(-6, 0))
        t += rng.exponential(1 / rate) + 0.012
    return b.x


GROAN = "sfx_100_v2:misc_25"


def bellow(rng):
    """His muttering catching and climbing into a bellow through a jaw dropped too far: a man's scream gone ragged, an
    octave-down throat in it (the split cheeks make two mouths of one), a real groan under it for breath, torn by
    fold-over grit; the teeth gnashing as it runs out. Returns the sound and when the charge should start."""
    b = Bus(2.4)
    b.at(0, mutter(rng, 0.6, 105, rise=1.5, rate=8, open_=0.6), -4)
    L = rng.uniform(0.95, 1.2)
    top = rng.uniform(250, 290)
    brk = L * rng.uniform(0.45, 0.6)     # the voice cracks up a fifth and hangs there before it gives out
    pts = [(0, 150), (0.12, top), (brk, top * 0.93), (brk + 0.03, top * 1.4), (L * 0.8, top * 1.3), (L, top * 0.7)]
    vow = [(0, "o"), (0.12, "a"), (L * 0.7, "a"), (L, "o")]
    sh = [(0, 0), (0.06, 1), (L * 0.75, 0.9), (L, 0)]
    a = throat(rng, pts, L, vow, sh, rasp=0.6, fry_rate=(45, 70), sub=0.6, hiss=0.3, fire=0.0, tract=0.62)
    lo = throat(rng, [(t, p / 2) for t, p in pts], L, vow, sh, rasp=0.85, fry_rate=(30, 45), sub=0.7, hiss=0.0,
                fire=0.0, tract=0.55)
    groan = dsp.shift(dsp.stretch(dsp.trim_silence(src.get(GROAN), -40), L / 0.6, smooth=True), -rng.uniform(3, 5))
    sc = mix(a, lo * 0.6, dsp.shaped(unit(dsp.fit(groan, samples(L))), sh) * 0.5)
    sc = mix(dsp.saturate(sc, 8), dsp.fold(sc * 0.5, 2.2) * 0.12)
    b.at(0.5, unit(lp(sc, 7000)), 0)
    b.at(0.5 + L - 0.15, gnash(rng, 0.45), -6)
    return b.x, 0.5 + L + 0.15


def blubber(rng, length):
    """Air blasted out through cheeks split back to the ears: they flap (a deep, uneven 22-34 a second flutter on the
    breath) and spray spit with every flap."""
    fl = fry(rng, env([(0, 22), (length * 0.5, 34), (length, 26)], length), length, jitter=0.3, decay=0.011)
    air = unit(bp(synth.noise(length, rng), 250, 2500)) * fl
    spit = synth.crackle(length, 300 + 1500 * fl, rng, size=(0.0002, 0.001), hi=1800) * fl
    return unit(mix(air, unit(spit) * 0.5))


def gnashing(rng):
    """The other way he goes feral: no scream. Breath sucked in hard through the teeth, then out through the split
    cheeks so they flap and spray, too many teeth gnashing, a man's growl under it; twice. Returns the sound and when
    the charge should start."""
    b = Bus(2.6)
    t = 0.0
    for L, I in ((rng.uniform(0.8, 1.0), 0.32), (rng.uniform(0.5, 0.65), 0.22)):
        hs = dsp.shaped(unit(bp(synth.noise(I, rng), 2500, 8000)), [(0, 0), (I * 0.8, 1), (I, 0)])
        wh = dsp.sweep_filter(synth.noise(I, rng), "bp", env([(0, 3000), (I, 4200)], I, "exp"), q=20)
        b.at(t, mix(hs, dsp.shaped(unit(wh), [(0, 0), (I * 0.8, 1), (I, 0)]) * 0.3), -8)
        t += I
        sh = [(0, 0), (0.03, 1), (L * 0.7, 0.85), (L, 0)]
        b.at(t, dsp.shaped(blubber(rng, L), sh), 0)
        g = throat(rng, [(0, 95), (L * 0.5, 130), (L, 105)], L, [(0, "o"), (L * 0.3, "a"), (L, "a")], sh, rasp=0.9,
                   fry_rate=(26, 36), sub=0.7, hiss=0.2, fire=0.0, tract=0.62)
        b.at(t, g, -4)
        b.at(t + 0.05, gnash(rng, L - 0.1, 20), -3)
        t += L + 0.04
    return b.x, t + 0.05


def feral(rng, k, outburst):
    """`outburst` (bellow or gnashing), then the scuttle charging in at whoever hit him, quicker and nearer, breath
    hissing through the teeth. Take 0 charges along the crane's iron, take 1 over the yard."""
    b = Bus(3.8)
    x, c0 = outburst(rng)
    b.at(0, x)
    run = max(3.6 - c0, 1.2)
    ch = scuttle(rng, run, 0.13, surface="iron" if k == 0 else "ground", accel=1.6)
    b.at(c0, dsp.shaped(ch, [(0, 0.3), (run, 1.0)], "exp"), -2)
    for t in np.arange(c0 + 0.1, c0 + run - 0.1, rng.uniform(0.28, 0.36)):
        hb = bp(synth.noise(0.12, rng), 1500, 6000) * env([(0, 0), (0.02, 1), (0.12, 0)], 0.12)
        b.at(t, unit(hb), -16 + 6 * (t - c0) / run)
    return outdoors(b.x, rng)


@recipe("cs-grumbler", "feral", "bellow",
        "His grumbling catching into a ragged man's bellow through a split jaw, teeth gnashing, then the charge",
        """No roar or squeak samples this time. The Grumbler is a man gone wrong, so: his muttering quickens and climbs,
        breaks into a bellow (a synthesised man's throat torn by fry and period doubling, through a wide-open 'ah', with
        a second throat an octave down in it as if the split cheeks made two mouths, and a real human groan stretched
        and dropped a few semitones under it for breath; the voice cracks up a fifth before it gives out), saturated and
        folded for the tearing, too many teeth gnashing as it runs out, then the scuttle charging in at you, faster and
        nearer, breath hissing through the teeth. One take charges along the crane's iron, the other over the yard.""",
        sources=[GROAN] + SLAPS + PLATES + BALLAST, takes=2)
def feral_bellow(rng, k):
    return done(feral(rng, k, bellow))


@recipe("cs-grumbler", "feral", "gnash",
        "No scream: breath hissed in through his teeth and blasted out through flapping split cheeks, then the charge",
        """A quieter, nastier rage than 'bellow'. He sucks breath in hard through his teeth (a rising hiss with a
        whistle in it), then blasts it out through cheeks split back to the ears, so they flap (breath fluttered
        unevenly 22-34 times a second) and spray spit with each flap, over a man's growl with fry in it and too many
        teeth gnashing; twice, the second shorter. Then the same charge as 'bellow': along the crane's iron in one take,
        over the yard in the other.""",
        sources=SLAPS + PLATES + BALLAST, takes=2)
def feral_gnash(rng, k):
    return done(feral(rng, k, gnashing))


# Eating cargo aboard: in a wooden car, prising crates apart with the rib arms and eating what's in them.

SPLITS = ["sfx_100_v2:misc_35", "sfx_100_v2:misc_34", "kenney_rpg-audio:chop"]


def gnaw(rng, length):
    """Teeth rasping across timber in a run: fibres scraped in grains at 10-14 a second, the jaw's push in it."""
    b = Bus(length + 0.1)
    t = 0.0
    while t < length:
        L = rng.uniform(0.03, 0.05)
        s = synth.creak(L, rng.uniform(300, 600), rng, body=[f * rng.uniform(1.5, 2.5) for f in synth.WOOD], q=10)
        s = mix(unit(s), unit(bp(synth.noise(L, rng), 1200, 3500)) * 0.5)
        b.at(t, dsp.shaped(unit(s), [(0, 0), (0.004, 1), (L, 0)]), rng.uniform(-5, 0))
        t += 1 / rng.uniform(10, 14)
    return dsp.tremolo(b.x, rng.uniform(2, 3), 0.5)


def prise(rng):
    """A slat levered off a crate: the wood straining, a nail drawn out squealing, the split, the slat thrown down."""
    b = Bus(2.0)
    S = rng.uniform(0.35, 0.55)
    b.at(0, synth.creak(S, env([(0, 15), (S, 70)], S, "exp"), rng, body=synth.WOOD, q=18), -6)
    N = rng.uniform(0.2, 0.32)
    sq = synth.creak(N, env([(0, 700), (N, 1100)], N), rng, body=[2600, 3900, 5600], q=40, jitter=0.1)
    b.at(S * 0.6, dsp.shaped(sq, [(0, 0), (0.03, 1), (N, 0)]), -12)
    sp = SPLITS[int(rng.integers(len(SPLITS)))]
    b.at(S, unit(clip(sp, length=0.35, semis=-rng.uniform(1, 4))), 0)
    b.at(S + 0.02, unit(synth.crackle(0.3, env([(0, 500), (0.3, 0)], 0.3), rng, size=(0.0003, 0.002), hi=1200)), -10)
    slat = clip(f"kenney_impact-sounds:impactWood_light_{int(rng.integers(5)):03d}", length=0.25)
    b.at(S + rng.uniform(0.3, 0.5), unit(slat), -6)
    return b.x


def spill(rng, length=1.2):
    """A split sack of grain running out onto the boards: a hiss of thousands of grains, their patter on the wood."""
    r = synth.rustle(length, env([(0, 2500), (length * 0.3, 1800), (length, 0)], length), rng, f=(900, 6000), ticks=0)
    p = synth.crackle(length, env([(0, 400), (length, 0)], length), rng, size=(0.0004, 0.002), hi=700)
    return mix(unit(r) * 0.6, unit(p) * 0.6)


TINS = [f"kenney_impact-sounds:impactTin_medium_{i:03d}" for i in range(5)]


def tin(rng):
    """A tin knocked off the stack: it hits the boards and rolls a little, knocking."""
    b = Bus(1.2)
    b.at(0, unit(clip(TINS[int(rng.integers(5))], length=0.2, semis=rng.uniform(1, 4))), 0)
    t = 0.12
    for i in range(int(rng.integers(3, 6))):
        b.at(t, unit(clip(TINS[int(rng.integers(5))], length=0.08, semis=rng.uniform(3, 6))), -10 - 3 * i)
        t += rng.uniform(0.08, 0.2)
    return b.x


EAT_L = 12.3


def eat(rng):
    n = samples(EAT_L)
    b = Bus(EAT_L + 2.5)
    t = 0.2
    slots = ["prise", "gnaw", "chew", "gnaw", "prise", "chew", "spill", "gnaw", "chew", "tin", "gnaw", "chew"]
    for what in slots:
        if t >= EAT_L:
            break
        if what == "prise":
            b.at(t, prise(rng), 0)
            t += 1.0
        elif what == "gnaw":
            L = rng.uniform(0.7, 1.4)
            b.at(t, gnaw(rng, L), -5)
            t += L + rng.uniform(0.1, 0.3)
        elif what == "chew":     # a mouthful: crunching and chewing, grumbling through it
            L = rng.uniform(0.9, 1.5)
            for c in np.arange(0, L, rng.uniform(0.24, 0.32)):
                b.at(t + c, chew(rng, 1.1, teeth=0.8), -6)
                if rng.random() < 0.35:
                    b.at(t + c + 0.03, crunch(rng, 0.9), -6)
            b.at(t, lp(mutter(rng, L, rng.uniform(85, 100), rate=4), 1400), -10)
            t += L + rng.uniform(0.1, 0.3)
        elif what == "spill":
            b.at(t, spill(rng, 1.3), -8)
            t += 0.5
        elif what == "tin":
            b.at(t, tin(rng), -8)
            t += 0.4
        if rng.random() < 0.3:     # a hand slapped down on the boards as he shifts
            b.at(t - 0.1, palm(rng, None, 1.0, "ground"), -12)
    # under it all, never stopping: his heavy breath through the teeth, and the crates and the car creaking as he works
    t = 0.0
    while t < EAT_L:
        B = rng.uniform(0.7, 1.1)
        br = synth.breath(B, [(0, "h"), (B, "u")], 0.7, rng, shape=env([(0, 0), (B * 0.3, 1), (B, 0)], B))
        b.at(t, lp(br, 2500), -20)
        t += B + rng.uniform(0.3, 0.8)
    for t in rng.uniform(0, EAT_L, 5):
        C = rng.uniform(0.4, 0.8)
        cr = synth.creak(C, rng.uniform(10, 25), rng, body=synth.WOOD, q=14,
                         shape=env([(0, 0), (C * 0.5, 1), (C, 0)], C))
        b.at(t, cr, -20)
    y = dsp.room(b.x, "car", wet=0.25, rng=rng)
    return dsp.wrap(y, n)


@recipe("cs-grumbler", "eat", "crates",
        "Aboard in a wooden car: crate slats prised off and splitting, gnawing at the timber, chewing and grumbling",
        """In a cargo car's reverb. His rib arms lever slats off the crates: the wood straining (a stick-slip creak
        quickening), a nail drawn out squealing, the split (real sfx wood cracks and a Kenney chop), splinters, the slat
        thrown down (a real light wood knock). Between, runs of gnawing at the timber (the tell's stick-slip rasp at
        10-14 a second), mouthfuls crunched and chewed while he grumbles through them, a sack of grain running out onto
        the boards, a tin knocked rolling (real tin knocks); under it all his heavy breathing and the crates creaking,
        so it never drops dead. A seamless 12 s loop.""",
        sources=SPLITS + [f"kenney_impact-sounds:impactWood_light_{i:03d}" for i in range(5)]
        + TINS + CRACKS + SLAPS + BALLAST, loop=True, takes=1)
def eat_crates(rng, k):
    return done(eat(rng))


# ---- Followers
# ---------------------------------------------------------------------------------------------------------------
# A tick grown to the size of a hand and bloated (tools/blender/follower.py): a leather sac of blood under a hard
# shield,
# ten hooked legs, a barbed beak. Taut membranes that burst, what's in them, small shells, many little legs.

def sac_pop(rng, size=1.0):
    """A taut leather sac bursting: the skin's sharp 'pok', the blood squirting out, droplets spattering down."""
    b = Bus(0.6)
    imp = np.pad(rng.standard_normal(samples(0.0015)).astype(np.float32), (0, samples(0.05)))
    f = rng.uniform(1200, 2200) / size
    pok = dsp.resonate(imp, [f, f * 1.6], q=5)
    b.at(0, unit(pok), 0)
    b.at(0, kit.thud(rng, 170 / size, 0.06, 0.6), -10)
    b.at(0.004, squish(rng, rng.uniform(0.1, 0.18) * size, 500, 2600), -3)
    L = rng.uniform(0.25, 0.4)
    spray = synth.crackle(L, env([(0, 600), (L, 0)], L), rng, size=(0.0004, 0.002), hi=900)
    drops = synth.bubbles(L, env([(0, 150), (L, 0)], L), 1500, 5000, rng, rise=(0.02, 0.1))
    b.at(0.03, unit(mix(spray, drops * 0.5)), -10)
    return b.x


def shell(rng, size=1.0):
    """A small hard shield or egg-shell cracking: a chitin snap with a real crack's grit in it."""
    c = clip(CRACKS[int(rng.integers(len(CRACKS)))], length=0.05, semis=rng.uniform(1, 4))
    return mix(unit(kit.snap(rng, 0.05, rng.uniform(2400, 3600) / size)), unit(c) * 0.6)


def legs(rng, length, rate, lo=2500, hi=6500):
    """Ten little hooked legs: fine chitin ticks at `rate` a second (a constant or a curve)."""
    return unit(synth.skitter(length, rate, rng, f=(lo, hi), q=(6, 14), legs=10))


def drips(rng, length, n):
    """Blood dripping off the edges onto the boards: a drop is a soft wet tick on wood, not a plink into water."""
    b = Bus(length + 0.2)
    for t in np.sort(rng.uniform(0, length, n)):
        L = rng.uniform(0.012, 0.025)
        tick = bp(synth.noise(L, rng), rng.uniform(1200, 2000), rng.uniform(4000, 7000))
        tick = tick * env([(0, 1), (L, 0)], L, "exp")
        b.at(t, unit(tick), rng.uniform(-6, 0))
        b.at(t, lp(synth.click(rng.uniform(600, 900), q=4, length=0.02, rng=rng), 2000), -12)
        b.at(t + 0.002, synth.bubble(rng.uniform(2500, 4000), rise=0.03, amp=0.25), -6)
    return b.x


def coat_blow(rng, heavy=1.0):
    """The club landing on a friend's back: a real cloth thump through a heavy coat, the body under it."""
    b = Bus(0.5)
    b.at(0, unit(clip(CLOTH[int(rng.integers(3))], length=0.3)), 0)
    b.at(0, kit.thud(rng, 85, 0.18, heavy), -4)
    return b.x


def splat(rng):
    """It, or what's left of it, hitting the boards."""
    b = Bus(0.4)
    b.at(0, kit.slap(rng, 0.06, 2500, wet=0), -3)
    b.at(0, squish(rng, 0.14, 400, 2000), -3)
    b.at(0, unit(clip(f"kenney_impact-sounds:impactWood_light_{int(rng.integers(5)):03d}", length=0.12, semis=4)), -14)
    return b.x


def clubbed_off(rng, k):
    b = Bus(2.0)
    b.at(0, coat_blow(rng, 1.2 if k == 2 else 1.0), 0)
    if k == 0:     # it bursts on the back, twitches, slides off
        b.at(0.004, sac_pop(rng, 1.0), 0)
        b.at(0.06, legs(rng, 0.4, env([(0, 90), (0.4, 5)], 0.4)), -14)
        b.at(0.42, splat(rng), -6)
        b.at(0.5, drips(rng, 0.8, 4), -16)
    elif k == 1:   # knocked off whole: the barbs tear out of the coat, it lands and splits, the legs go on twitching
        b.at(0.01, rip(rng, 0.12, 1500, 6000, pull=[(0, 1), (0.12, 0)]), -6)
        b.at(0.3, splat(rng), -2)
        b.at(0.3, sac_pop(rng, 0.8), -4)
        b.at(0.31, shell(rng), -6)
        b.at(0.35, legs(rng, 0.9, env([(0, 70), (0.3, 30), (0.9, 0)], 0.9)), -10)
    else:          # crushed against the back: the shield cracks, the sac squeezed out, scraped off by the club
        b.at(0.0, shell(rng, 0.9), 0)
        b.at(0.01, squish(rng, 0.35, 300, 1800), -2)
        b.at(0.02, sac_pop(rng, 1.2), -4)
        b.at(0.18, rip(rng, 0.2, 1500, 6000, pull=[(0, 0.4), (0.15, 1), (0.2, 0)]), -8)
        b.at(0.5, splat(rng), -6)
        W = 0.4      # the last air out of the punctured sac
        wz = dsp.sweep_filter(synth.noise(W, rng), "bp", env([(0, 3500), (W, 1800)], W, "exp"), q=12)
        b.at(0.55, dsp.shaped(unit(wz), [(0, 0), (0.05, 1), (W, 0)]), -20)
    return dsp.room(b.x, "car", wet=0.15, rng=rng)


@recipe("cs-followers", "clubbed-off", "burst",
        "A club on a friend's back, and the engorged tick on it bursting or knocked off to split on the boards",
        """No punches or squeaks: the blow lands through a heavy coat (a real Kenney cloth thump and the body's weight),
        then the tick. It is a taut leather sac full of blood under a hard shield, so: the skin's sharp 'pok' as it
        goes, a squirt and droplets spattering; its shield cracking (a chitin snap with a real crack's grit); the hooked
        barbs tearing out of the coat; ten little legs twitching after. One take bursts it on the back, one knocks it
        off whole to split on the boards, one crushes it and the punctured sac sighs out the last of its air. Inside a
        car.""",
        sources=CLOTH[:3] + CRACKS + [f"kenney_impact-sounds:impactWood_light_{i:03d}" for i in range(5)], takes=3)
def followers_clubbed(rng, k):
    return done(clubbed_off(rng, k))


# The nest: a pulsing, kneading mass of them over the loot. Smashing it is a loop for as long as someone hacks at it.

CHOPS = ["kenney_rpg-audio:chop", "sfx_100_v2:wood_hit_01", "sfx_100_v2:wood_hit_02"]


def nest_bed(rng, length, agit):
    """The nest alive: a slow kneading pulse (a wet squeeze under the legs) and the brood squirming over each other,
    the squirm surging with `agit` (a per-sample curve, 0-1)."""
    b = Bus(length + 1.0)
    t, period = 0.0, rng.uniform(1.0, 1.25)
    while t < length:
        P = period * rng.uniform(0.9, 1.1)
        b.at(t, dsp.shaped(squish(rng, P * 0.7, 140, 700, 0.6), [(0, 0), (P * 0.3, 1), (P * 0.7, 0)]), -10)
        b.at(t, legs(rng, P * 0.6, env([(0, 4), (P * 0.3, 25), (P * 0.6, 4)], P * 0.6), 1800, 4000), -22)
        t += P
    n = samples(length)
    a = synth.curve(agit, n)
    b.at(0, legs(rng, length, 30 + 220 * a) * (0.25 + 0.75 * a), -14)
    wet = synth.rustle(length, 60 + 500 * a, rng, f=(900, 3500), ticks=0)
    b.at(0, unit(wet) * (0.3 + 0.7 * a), -20)
    return b.x


def hack(rng, strength=1.0):
    """One blow into the nest: the tool going into the wet fibrous mass (sometimes on through to the crate under it),
    sacs splitting, shells cracking, what was in them spattering."""
    b = Bus(1.4)
    b.at(0, kit.thud(rng, 80, 0.2, strength), -4)
    b.at(0, unit(clip(CHOPS[int(rng.integers(len(CHOPS)))], length=0.25, semis=-rng.uniform(3, 6))), -6 + 4 * strength)
    b.at(0.003, squish(rng, rng.uniform(0.2, 0.3), 250, 1400), -2)
    for i in range(int(rng.integers(1, 4))):
        b.at(rng.uniform(0.005, 0.07), sac_pop(rng, rng.uniform(0.7, 1.3)), -4 - 3 * i)
    for _ in range(int(rng.integers(0, 4))):
        b.at(rng.uniform(0.0, 0.1), shell(rng, rng.uniform(0.8, 1.4)), rng.uniform(-14, -6))
    b.at(0.25, drips(rng, 0.8, int(rng.integers(2, 6))), -14)
    return b.x


SMASH_L = 12.3


def nest_smash(rng):
    n = samples(SMASH_L)
    hits, t = [], rng.uniform(0.2, 0.5)
    while t < SMASH_L:
        hits.append((t, rng.uniform(0.75, 1.0)))
        t += rng.uniform(1.05, 1.55)
    agit = np.zeros(n + samples(1.0), np.float32)
    k = np.exp(-np.arange(samples(1.2)) / SR / 0.35).astype(np.float32)
    for t, s in hits:      # the brood surges after each blow and settles again (wrapping round the loop)
        a = samples(t)
        seg = min(len(k), len(agit) - a)
        agit[a:a + seg] = np.maximum(agit[a:a + seg], k[:seg] * s)
    agit = dsp.wrap(agit, n)
    b = Bus(SMASH_L + 1.5)
    b.at(0, nest_bed(rng, SMASH_L, 0.2 + 0.8 * agit))
    for t, s in hits:
        b.at(t, hack(rng, s), 0)
    y = dsp.room(b.x, "car", wet=0.22, rng=rng)
    return dsp.wrap(y, n)


@recipe("cs-followers", "nest-smash", "hacking",
        "Someone hacking at a pulsing, kneading nest in a loot car: sacs splitting, fluid, shells, the brood squirming",
        """A loop, as the director asked, for as long as a player keeps smashing. Under it the nest is alive: a slow wet
        kneading squeeze about once a second and the brood's tiny legs and wet bodies squirming over each other. Every
        1-1.5 s a blow goes in: the weight, the tool into the fibrous mass (a real Kenney chop or sfx wood knock,
        slowed, for when it goes through to the crate under it), one to three sacs splitting (a taut skin's 'pok', a
        squirt and spatter), small shells cracking, blood dripping off; and the brood surges after each blow and
        settles. In a wooden car's reverb. A seamless 12 s loop.""",
        sources=CHOPS + CRACKS, loop=True, takes=1)
def smash_hacking(rng, k):
    return done(nest_smash(rng))


GUSH = "sfx_100_v2:loop_water_02"


def nest_writhe(rng):
    n = samples(SMASH_L)
    # frantic and never settling: the agitation wanders high, with no blows in it to surge after
    wander = lp(rng.standard_normal(n + SR).astype(np.float32), 0.6)[:n]
    agit = 0.75 + 0.25 * wander / (np.max(np.abs(wander)) + 1e-9)
    b = Bus(SMASH_L + 1.5)
    b.at(0, nest_bed(rng, SMASH_L, agit))
    t = rng.uniform(0, 0.3)
    while t < SMASH_L:      # wounded sacs splitting on their own, shells giving under the squirming
        b.at(t, sac_pop(rng, rng.uniform(0.5, 1.0)), rng.uniform(-16, -7))
        if rng.random() < 0.35:
            b.at(t + rng.uniform(0, 0.2), shell(rng, rng.uniform(0.9, 1.5)), rng.uniform(-18, -10))
        t += rng.exponential(0.55) + 0.12
    run = lp(dsp.vari(src.get(GUSH), -7), 1800)      # what's in it running off over the loot
    b.at(0, unit(dsp.fit(run, n)), -20)
    b.at(0, drips(rng, SMASH_L, 30), -16)
    y = dsp.room(b.x, "car", wet=0.22, rng=rng)
    return dsp.wrap(y, n)


@recipe("cs-followers", "nest-smash", "writhe",
        "The nest writhing while it's attacked, without the blows: for the crew's own weapon hits to land on in time",
        """The other way to make the loop: everything but the tool. If the game plays each swing's own hit as the player
        lands it, the loop can't hit in time with them, so this is only the nest's side, frantic throughout: the
        kneading squeeze, the brood squirming hard and never settling, wounded sacs splitting on their own at uneven
        times, small shells giving, what's inside running off over the loot (a real pouring-water loop slowed and
        darkened) and dripping. In a wooden car's reverb. A seamless 12 s loop.""",
        sources=[GUSH] + CRACKS, loop=True, takes=1)
def smash_writhe(rng, k):
    return done(nest_writhe(rng))


def nest_burst(rng, k):
    b = Bus(4.5)
    # the last blow
    b.at(0, hack(rng, 1.2), 2)
    b.at(0, kit.thud(rng, 65, 0.3, 1.3), -2)
    # the nest's skin tears open and everything in it comes out
    R = rng.uniform(0.45, 0.6) if k == 0 else 0.25
    b.at(0.05, rip(rng, R, 400, 3000, wet=1.0, pull=[(0, 0.2), (R * 0.8, 1), (R, 0)]), -2)
    G = 1.6
    gush = lp(dsp.vari(dsp.trim(src.get(GUSH), rng.uniform(1, 5), 1.1), -rng.uniform(5, 7)), 2500)
    b.at(0.05 + R * 0.7, dsp.shaped(unit(gush), [(0, 0), (0.05, 1), (0.6, 0.6), (G, 0)]), -4 if k == 0 else 0)
    b.at(0.05 + R * 0.7, squish(rng, 0.5, 200, 900), -3)
    for i in range(int(rng.integers(3, 6))):     # gouts of it running out: short low wet gulps, not tones
        L = rng.uniform(0.08, 0.14)
        f = rng.uniform(150, 260)
        g = dsp.sweep_filter(synth.noise(L, rng), "bp", env([(0, f * 1.4), (L, f)], L, "exp"), q=3)
        b.at(0.2 + R + i * rng.uniform(0.12, 0.2), dsp.shaped(unit(g), [(0, 0), (0.008, 1), (L, 0)]), -10)
    # the brood's sacs going in a cascade, shells crunching under it
    for t in np.sort(0.1 + 0.9 * rng.random(int(rng.integers(14, 22))) ** 1.6):
        b.at(t, sac_pop(rng, rng.uniform(0.5, 1.0)), rng.uniform(-16, -8))
        if rng.random() < 0.4:
            b.at(t + 0.01, shell(rng, rng.uniform(0.9, 1.5)), rng.uniform(-18, -10))
    # what's left of the brood scattering in every direction, fainter and duller as they get away
    S = 1.8
    sc = legs(rng, S, env([(0, 260), (0.4, 160), (S, 10)], S), 1800, 6000)
    sc = dsp.sweep_filter(sc, "lp", env([(0, 9000), (S, 1500)], S, "exp"))
    b.at(0.15, dsp.shaped(sc, [(0, 1), (S, 0.05)], "exp"), -6)
    # the nest emptying: the last air bubbling out of the main sac, then drips
    W = 0.9
    wz = dsp.sweep_filter(synth.noise(W, rng), "bp", env([(0, 1400), (W, 380)], W, "exp"), q=8)
    wz = mix(unit(wz), unit(synth.bubbles(W, env([(0, 40), (W, 5)], W), 300, 1200, rng, rise=(0.02, 0.1))) * 0.5)
    b.at(0.6 + R, dsp.shaped(unit(wz), [(0, 0), (0.1, 1), (W, 0)]), -10)
    b.at(1.2, drips(rng, 1.6, 9), -12)
    b.at(2.2, legs(rng, 0.3, 20), -22)     # a last one twitching
    return dsp.room(b.x, "car", wet=0.22, rng=rng)


@recipe("cs-followers", "nest-burst", "collapse",
        "The nest destroyed: its skin tearing open, everything in it pouring out, the brood bursting and scattering",
        """The final blow, then the nest's skin tearing open (wet fibres giving faster and faster) and what it held
        pouring out over the loot (a real pouring-water loop slowed half an octave and darkened, with glugs and a big
        wet squeeze), the brood's sacs bursting in a cascade with shells crunching, the survivors scattering in every
        direction (tiny legs, fainter and duller as they get away), the main sac's last air bubbling out, drips, one
        last twitch. The second take tears open fast and gushes harder. In a wooden car.""",
        sources=[GUSH] + CHOPS + CRACKS, takes=2)
def burst_collapse(rng, k):
    return done(nest_burst(rng, k))


# ---- Climbers
# ------------------------------------------------------------------------------------------------------------
# Long, low and soot-black (tools/blender/climber.py): smooth wet-rubber skin over wire muscle, a row of short spines,
# and
# under the blank wedge of the head a lamprey's sucker ringed with hooked teeth. Blows land tight and dense, and it
# answers
# through the sucker: air forced out of a wet round mouth, hissing and squealing.

SQUEAKS = ["kenney_rpg-audio:creak2", "kenney_rpg-audio:creak3"]


def rubber_hit(rng, weight=1.0):
    """A blow on taut rubbery hide over hard muscle: a tight dense knock with a short body to it, the spines rattling
    and a little soot knocked off."""
    b = Bus(0.6)
    b.at(0, kit.slap(rng, 0.07, 2800, wet=0), 0)
    b.at(0, kit.thud(rng, 110, 0.08, weight), -7)
    imp = np.pad(lp(rng.standard_normal(samples(0.003)).astype(np.float32), 3000), (0, samples(0.15)))
    f = rng.uniform(200, 260)
    b.at(0, dsp.shaped(unit(dsp.resonate(imp, [f, f * 2.3, f * 3.9], q=9)), [(0, 1), (0.15, 0.01)], "exp"), -4)
    for _ in range(int(rng.integers(5, 10))):     # the spine row knocked against itself
        lf = rng.uniform(1500, 3800)
        spine = synth.click(lf, q=rng.uniform(4, 8), length=0.012, rng=rng)
        b.at(rng.uniform(0.004, 0.04), spine, rng.uniform(-18, -10))
    b.at(0.005, ash(rng, 0.2, 700, 6000), -20)
    return b.x


def sucker(rng, length, pitch, kind="squeal"):
    """Air forced out through a wet round mouth ringed with teeth: a hiss with a whistle in it where the lips tighten,
    the lips fluttering it (a rubbery buzz), a real squeak bent to the same contour for the squeal, wet clicks at the
    rim. `pitch` is the whistle's contour [(t, Hz), ...]."""
    n = samples(length)
    fc = dsp.fit(env(pitch, length, "exp"), n)
    sh = [(0, 0), (0.03, 1), (length * 0.6, 0.8), (length, 0)]
    flutter = fry(rng, rng.uniform(38, 55), length, jitter=0.2, decay=0.008)
    hiss = unit(bp(synth.noise(length, rng), 2200, 8000)) * (0.6 + 0.4 * flutter)
    whistle = unit(dsp.sweep_filter(synth.noise(length, rng), "bp", fc, q=28)) * (0.3 + 0.7 * flutter)
    parts = [hiss * (0.8 if kind == "hiss" else 0.45), whistle * (0.35 if kind == "hiss" else 0.8)]
    if kind != "hiss":
        sq = dsp.trim_silence(src.get(SQUEAKS[int(rng.integers(2))]), -35)
        sq = dsp.stretch(sq, length / (len(sq) / SR), smooth=True)
        sq = dsp.shift(sq, 12 * np.log2(float(np.median(fc)) / 1050))
        parts.append(unit(dsp.fit(sq, n)) * (0.3 + 0.7 * flutter) * 0.6)
    rim = synth.crackle(length, 250, rng, size=(0.0002, 0.0008), hi=1500)
    parts.append(unit(rim) * 0.25)
    y = dsp.shaped(unit(mix(*parts)), sh)
    return dsp.saturate(y * 0.8, 4)


def climber_hit(rng, k):
    b = Bus(1.6)
    b.at(0, rubber_hit(rng, (1.0, 1.2, 0.9, 1.1)[k]), 0)
    t = rng.uniform(0.03, 0.07)
    if k == 0:     # a hiss that tightens into a short squeal
        L = rng.uniform(0.4, 0.55)
        b.at(t, sucker(rng, L, [(0, 1600), (L * 0.5, 2300), (L, 1700)], "squeal"), -3)
    elif k == 1:   # a long squeal falling away
        L = rng.uniform(0.7, 0.9)
        b.at(t, sucker(rng, L, [(0, 2600), (0.08, 2900), (L, 1300)], "squeal"), -2)
    elif k == 2:   # two squeals, the second higher and shorter
        L1, L2 = rng.uniform(0.3, 0.38), rng.uniform(0.22, 0.28)
        b.at(t, sucker(rng, L1, [(0, 1800), (L1, 2200)], "squeal"), -3)
        b.at(t + L1 + 0.06, sucker(rng, L2, [(0, 2500), (L2 * 0.4, 3000), (L2, 2100)], "squeal"), -3)
    else:          # a wet hiss, the sucker working open and shut
        L = rng.uniform(0.6, 0.75)
        b.at(t, sucker(rng, L, [(0, 1900), (L, 1500)], "hiss"), -4)
        for tt in np.arange(t + 0.1, t + L, rng.uniform(0.14, 0.2)):
            b.at(tt, squish(rng, 0.08, 800, 3000), -14)
    return outdoors(b.x, rng, wet=0.08, tail=0.4)


@recipe("cs-climbers", "hit", "sucker",
        "A blow on a Climber's taut black hide, and it hissing and squealing through its lamprey sucker",
        """The blow lands tight and dense on rubbery skin over hard muscle (a skin smack, the body's weight, a short
        taut resonance), the spine row rattling and a little soot knocked off. Then it answers through the sucker: air
        forced out of a wet round mouth, a hiss with a whistle in it where the lips tighten (noise through a narrow band
        riding a pitch contour), fluttered by the lips into a rubbery buzz, a real Kenney squeak stretched and bent to
        the same contour for the squeal, and wet ticks at the toothed rim. Takes: hiss into a short squeal, a long
        falling squeal, two squeals, a wet hiss as the sucker works.""",
        sources=SQUEAKS, takes=4)
def climber_sucker(rng, k):
    return done(climber_hit(rng, k))
