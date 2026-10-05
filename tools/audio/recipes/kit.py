"""Building blocks the creature recipes share: singing and crying throats, flesh, wet, gravel, wood and bone, air.

Each is a small model or a bent recording, never a finished sound: the recipes put several together (the director's
kitbash rule for enemies: interesting, organic, made by distorting sources together).
"""

import numpy as np

import dsp
import src
import synth
from dsp import samples, lp, hp, bp, env, gain, mix, fade, Bus

SR = dsp.SR


def hz(midi):
    return 440.0 * 2 ** ((midi - 69) / 12)


# ---- Throats --------------------------------------------------------------------------------------------------------------

def melody_f0(notes, beat, rng, scoop=60, glide=0.07, vib=(5.2, 0.006), detune=0.0, drift=12):
    """A sung line's pitch curve: [(midi, beats), ...] with a scoop up into each note, portamento between, a vibrato that
    blooms late in the note, a fixed detune and a slow wander (cents). Returns (f0 per sample, note onsets in s)."""
    total = sum(b for _, b in notes) * beat
    n = samples(total)
    tgt = np.zeros(n, np.float32)
    onsets, t = [], 0.0
    for m, b in notes:
        a, e = samples(t), samples(t + b * beat)
        tgt[a:e] = m
        onsets.append(t)
        t += b * beat
    # portamento: a one-pole glide on the target
    k = 1 - np.exp(-1 / (glide * SR))
    sm = np.empty_like(tgt)
    acc = tgt[0]
    for i in range(n):
        acc += k * (tgt[i] - acc)
        sm[i] = acc
    cents = np.zeros(n, np.float32)
    tt = np.arange(n) / SR
    for o in onsets:
        a = samples(o)
        d = np.arange(n - a) / SR
        cents[a:] += -scoop * np.exp(-d / 0.09) * (d < 0.6)
    vr, vd = vib
    vib_env = np.zeros(n, np.float32)
    for i, o in enumerate(onsets):
        a = samples(o)
        e = samples(onsets[i + 1]) if i + 1 < len(onsets) else n
        d = np.arange(e - a) / SR
        vib_env[a:e] = np.clip((d - 0.35) / 0.5, 0, 1)
    vibrato = vd * vib_env * np.sin(2 * np.pi * vr * tt + rng.uniform(0, 6.28)) * (1 + 0.15 * np.sin(2 * np.pi * 0.37 * tt))
    wander = lp(rng.standard_normal(n + SR).astype(np.float32), 0.4)[:n]
    wander = wander / (np.max(np.abs(wander)) + 1e-9) * drift
    f0 = 440 * 2 ** ((sm - 69 + (cents + detune + wander) / 100) / 12) * (1 + vibrato)
    return f0.astype(np.float32), onsets, total


def note_env(onsets, total, att=0.12, rel=0.18, dip=0.35, breathe=None):
    """Legato phrasing: each note swells in and eases off a little before the next."""
    n = samples(total)
    e = np.zeros(n, np.float32)
    bounds = onsets + [total]
    for i in range(len(onsets)):
        a, b = bounds[i], bounds[i + 1]
        L = b - a
        pts = [(0, dip), (min(att, L * 0.4), 1.0), (max(L - rel, L * 0.6), 0.85), (L, dip)]
        seg = env(pts, L)
        s = samples(a)
        e[s:s + len(seg)] = np.maximum(e[s:s + len(seg)], seg[:n - s])
    if breathe:
        for t in breathe:
            s = samples(t)
            w = samples(0.25)
            e[max(0, s - w // 2):s + w // 2] *= np.hanning(min(w, n - max(0, s - w // 2)))[:len(e[max(0, s - w // 2):s + w // 2])] * -0.9 + 1
    return e


BELL = [0.5, 1.0, 1.19, 1.51, 2.0, 2.52, 2.67, 3.01, 4.07, 5.2]   # a bell's partials (hum, prime, tierce, quint, ...)
MEMBRANE = [1.0, 1.59, 2.14, 2.30, 2.65, 2.92, 3.16, 3.50]      # a round drumhead's modes


def bell_body(x, f, q=80, wet=0.3):
    """Ring a voice through a thin bell of membrane (the Choir's bodies): its harmonics catch the bell's partials."""
    r = dsp.resonate(x, [f * p for p in BELL], q=q, gains=[0.5, 1, 0.8, 0.6, 0.6, 0.4, 0.4, 0.3, 0.2, 0.15])
    r = r / (np.max(np.abs(r)) + 1e-9)
    return (x * (1 - wet) + r * wet).astype(np.float32)


def cry(points, length, rng, tract="beast", vowels="a", rough=0.0, sub=0.0, breath=0.15, jitter=0.01, shimmer=0.1,
        shape=None, oq=0.6):
    """A creature's vocalisation: pitch from [(t, Hz), ...], through a tract (vowels a name or [(t, vowel)]), shaped by
    an envelope [(t, level)]. Rough and sub give a snarl's fry."""
    f0 = env(points, length, curve="exp")
    g = synth.glottis(f0, length, rng, jitter=jitter, shimmer=shimmer, sub=sub, rough=rough, oq=oq)
    y = synth.tract(g, vowels, tract, breath=breath, rng=rng)
    if shape is not None:
        y = y * dsp.fit(env(shape, length), len(y))
    return y


def howl_line(rng, length=3.2, base=420, peak=700, tract=0.55, start=0.0):
    """A canine howl's pitch: a scoop up to its peak, a long held top that wavers, a fall away at the end."""
    pts = [(0, base * 0.8), (0.35 * length, peak), (0.7 * length, peak * rng.uniform(0.92, 1.0)), (length, base * 0.6)]
    vow = [(0, "u"), (0.3 * length, "o"), (0.75 * length, "o"), (length, "u")]
    shape = [(0, 0), (0.15 * length, 0.8), (0.4 * length, 1), (0.8 * length, 0.85), (length, 0)]
    v = cry(pts, length, rng, tract=tract, vowels=vow, breath=0.25, jitter=0.006, shimmer=0.08, shape=shape)
    return dsp.wow(v, 25, 2.5, rng)


# ---- Bodies ---------------------------------------------------------------------------------------------------------------

def thud(rng, f=70, length=0.3, weight=1.0):
    """Weight landing: a dropping sub plus a dull bump of low noise."""
    t = synth.thump(f * rng.uniform(0.9, 1.1), length, drop=0.4)
    n = lp(synth.noise(length, rng), 300) * env([(0, 1), (0.04, 0.3), (length, 0)], length)
    return mix(t * weight, n * 0.6 * weight)


def slap(rng, length=0.12, bright=2500, wet=0.5):
    """Skin meeting something fast: a noise crack with a wet tail."""
    n = synth.noise(length, rng)
    y = bp(n, 300, bright) * env([(0, 1), (0.006, 0.6), (length, 0)], length, curve="exp")
    if wet:
        y = mix(y, synth.bubbles(length, 120, 600, 2500, rng) * wet * 0.4)
    return y


def squelch(rng, length=0.35, lo=350, hi=1600, depth=1.0):
    """Wet suction: noise through a resonant band that sweeps as the seal breaks, with bubbles popping in it."""
    n = synth.noise(length, rng)
    fc = env([(0, lo), (length * rng.uniform(0.3, 0.6), hi), (length, lo * 1.3)], length, curve="exp")
    y = dsp.sweep_filter(n, "bp", fc, q=rng.uniform(4, 9))
    y *= env([(0, 0), (0.02, 1), (length * 0.6, 0.5), (length, 0)], length)
    b = synth.bubbles(length, 60 * depth, lo, hi * 1.5, rng, rise=(0.1, 0.6))
    return mix(y, b * 0.5)


def flesh_hit(rng, weight=1.0, wet=0.6, bone=0.0, f=80):
    """A blow landing on a body: thud, slap, the flesh's ripple, wet if it's wet, a crack if bone takes it."""
    L = 0.5
    y = mix(thud(rng, f, 0.35, weight), slap(rng, 0.15, 2200, wet) * 0.8)
    rip = dsp.resonate(synth.noise(0.4, rng) * env([(0, 1), (0.4, 0)], 0.4, "exp"), [f * 2.1, f * 3.4], q=6) * 0.3
    y = mix(y, rip)
    if wet:
        y = mix(y, squelch(rng, 0.3, 300, 1300) * wet * 0.5)
    if bone:
        y = mix(y, snap(rng, 0.08, 1800) * bone)
    return dsp.fit(y, samples(L))


def snap(rng, length=0.1, f=2200):
    """A crack: a twig, a bone, a plate of bark."""
    x = np.zeros(samples(length), np.float32)
    k = samples(0.0015)
    x[:k] = rng.standard_normal(k)
    y = dsp.resonate(x, [f, f * 1.7, f * 2.6, f * 0.55], q=12) + hp(synth.noise(length, rng), 1000) * env([(0, 1), (0.01, 0.2), (length, 0)], length, "exp") * 0.5
    return y * env([(0, 1), (length, 0)], length, "exp")


def gravel(rng, n=12, length=0.09, lo=900, hi=5000, body=110):
    """Stones shifting under weight: a burst of small hard clicks over a pad's thump."""
    out = np.zeros(samples(length + 0.05), np.float32)
    for _ in range(n):
        c = synth.click(np.exp(rng.uniform(np.log(lo), np.log(hi))), q=rng.uniform(3, 8), length=0.015, rng=rng)
        a = samples(abs(rng.normal(0, length / 3)))
        m = min(len(c), len(out) - a)
        out[a:a + m] += c[:m] * rng.uniform(0.2, 1)
    if body:
        out = mix(out, synth.thump(body, 0.12, drop=0.3) * 0.8)
    return out


def creak(rng, length, rate, body=synth.WOOD, q=18, shape=None):
    return synth.creak(length, rate, rng, body=body, q=q, shape=shape)


def whoosh(rng, length=0.5, lo=200, hi=2500, peak=0.6):
    """Something big moving through the air close by: a band of noise that rises and falls with its speed."""
    n = synth.noise(length, rng, "pink")
    fc = env([(0, lo), (length * peak, hi), (length, lo)], length, curve="exp")
    y = dsp.sweep_filter(n, "bp", fc, q=1.2)
    return y * env([(0, 0), (length * peak, 1), (length, 0)], length) ** 1.5


def twigs(rng, length, density=40, lo=1200, hi=6000):
    """Dry branches rattling against each other (the Gaunt's body)."""
    return synth.skitter(length, density, rng, f=(lo, hi), q=(4, 9), legs=10)


# ---- Recordings, bent -----------------------------------------------------------------------------------------------------

def take(key, start=0.0, length=None):
    x = src.get(key)
    return dsp.trim(x, start, length) if (start or length) else x


def any_of(rng, keys):
    return src.get(keys[rng.integers(len(keys))])


# ---- Previews ---------------------------------------------------------------------------------------------------------------

def scatter(takes, rng, spacing=(0.25, 0.9), lead=0.2):
    """Takes fired the way the game fires them: one after another at uneven gaps."""
    t, parts = lead, []
    for x in takes:
        parts.append((t, x))
        t += len(x) / SR + rng.uniform(*spacing)
    b = Bus(t + 0.3)
    for at, x in parts:
        b.at(at, x)
    return b.x
