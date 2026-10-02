"""Shared parts for the crew's foley (crew_*.py): real recordings cut down to their events, cleaned and matched, and the
surfaces things land on.

The director's rule for gameplay foley is that it sounds like the real thing, built from real recordings, trimmed, levelled,
EQ'd to the object's size and layered from real parts. The packs' recordings are short and uneven (one take has its toe
louder than its heel, another a room tail, a third hiss), so most of this file is about cutting a recording into the
hits it holds (a boot's heel and toe, a latch's two clicks) and putting them back together evenly. The rest is the few
things the packs can't give and physics can: a sheet of tin booming over a car, a grate's bars chattering, stones
grinding under weight, friction.
"""

import numpy as np
from scipy import signal

import dsp
import src
import synth
from dsp import SR, samples, lp, hp, bp, env, mix, fit, Bus

# ---- Recordings -----------------------------------------------------------------------------------------------------------


def S(*names):
    """sfx_100_v2 keys."""
    return [f"sfx_100_v2:{n}" for n in names]


def R(*names):
    """Kenney RPG audio keys."""
    return [f"kenney_rpg-audio:{n}" for n in names]


def K(name, n=5):
    """Kenney impact takes: K('impactWood_medium') -> its five takes."""
    return [f"kenney_impact-sounds:{name}_{i:03d}" for i in range(n)]


def get(key):
    """A source, DC removed and rumble under 25 Hz cut (several of the packs' files sit on a little DC offset)."""
    x = src.get(key)
    return hp(x - np.mean(x), 25, 2)


def one(keys, take, rot=0):
    """The take-th of a set of sources, rotated so neighbouring cues don't start on the same file."""
    return keys[(take + rot) % len(keys)]


def envelope(x, cut=250):
    """A fast amplitude envelope (rectified, smoothed), for finding hits."""
    return np.maximum(lp(np.abs(x), cut, 2), 1e-9)


def hits(x, floor_db=-30, gap=0.02, prom=6):
    """The hits in a recording, as [(start, peak), ...] sample indices: each envelope peak at least `prom` dB above the
    trough before it and within `floor_db` of the loudest, its start where the envelope last rose out of that trough."""
    e = 20 * np.log10(envelope(x) / np.max(envelope(x)))
    pk, _ = signal.find_peaks(e, height=floor_db, distance=max(1, samples(gap)), prominence=prom)
    out, prev = [], 0
    for p in pk:
        seg = e[prev:p]
        if not len(seg):
            continue
        lo = prev + int(np.argmin(seg))
        # start: the last point before the peak still more than 20 dB under it (the attack's foot)
        rise = np.where(e[lo:p] < e[p] - 20)[0]
        s = lo + (int(rise[-1]) if len(rise) else 0)
        out.append((s, int(p)))
        prev = p
    return out


def cut(x, a, b, fade_in=0.0015, fade_out=0.012):
    """x[a:b] with short fades, so a cut-out hit doesn't click."""
    a, b = max(0, a), min(len(x), b)
    return dsp.fade(x[a:b].astype(np.float32), fade_in, fade_out)


def split(x, max_hits=3, tail=0.12, **kw):
    """Cut a recording into its hits: each from its start to the next hit's start (the last one `tail` long)."""
    h = hits(x, **kw)[:max_hits]
    if not h:
        return [x]
    starts = [s for s, _ in h] + [min(len(x), h[-1][1] + samples(tail))]
    return [cut(x, starts[i] - samples(0.001), starts[i + 1] + samples(0.004)) for i in range(len(h))]


def align(x, pre=0.002, floor_db=-30):
    """Start a sound at its first hit (less `pre`), so a step lands when the game fires it."""
    h = hits(x, floor_db=floor_db)
    a = max(0, (h[0][0] if h else 0) - samples(pre))
    return x[a:].copy()


def choke(x, at, tau=0.03):
    """Damp a ring after `at` seconds with time constant `tau`: a boot or a hand on the metal, stopping it."""
    t = np.arange(len(x)) / SR
    g = np.where(t < at, 1.0, np.exp(-(t - at) / tau))
    return (x * g).astype(np.float32)


def norm(x, db=0.0):
    return dsp.peak_norm(x, db)


def vari(rng, x, spread=0.5):
    """A small varispeed (+-`spread` semitones): the same object, struck a hair differently."""
    return dsp.vari(x, rng.uniform(-spread, spread))


def tilt(x, lo_db=0.0, hi_db=0.0, lo_f=250, hi_f=3000):
    """Shelf-ish EQ from two first-order bands: more or less bottom (size, weight), more or less top (distance, hardness)."""
    y = x.copy()
    if lo_db:
        y = y + (dsp.db2a(lo_db) - 1) * lp(x, lo_f, 1)
    if hi_db:
        y = y + (dsp.db2a(hi_db) - 1) * hp(x, hi_f, 1)
    return y.astype(np.float32)


OCTAVES = [31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000]


def profile(x):
    """A clip's spectrum in octave bands (dB), for matching takes to each other."""
    X = np.abs(np.fft.rfft(x, n=max(len(x), 4096))) ** 2
    f = np.fft.rfftfreq(max(len(x), 4096), 1 / SR)
    out = []
    for c in OCTAVES:
        m = (f >= c / 1.414) & (f < c * 1.414)
        out.append(10 * np.log10(np.sum(X[m]) + 1e-12))
    return np.array(out) - 10 * np.log10(np.sum(X) + 1e-12)


def match(x, target, amount=0.8, limit=8.0):
    """Matching EQ: pull a clip's octave balance `amount` of the way to `target` (a set's average profile), so takes cut
    from different recordings sound like one object in one place."""
    d = np.clip((np.asarray(target) - profile(x)) * amount, -limit, limit)
    n = max(len(x), 4096)
    f = np.fft.rfftfreq(n, 1 / SR)
    g = 10 ** (np.interp(np.log2(np.maximum(f, 20)), np.log2(OCTAVES), d) / 20)
    y = np.fft.irfft(np.fft.rfft(x, n=n) * g, n=n)[:len(x)]
    return y.astype(np.float32)


def matched(clips, amount=0.8):
    """A set of clips matched to their own average balance."""
    target = np.mean([profile(c) for c in clips], axis=0)
    return [norm(match(c, target, amount)) for c in clips]


def denoise(x, over=2.0, floor=0.08, pct=15):
    """Spectral gate: take out the steady hiss a recording sits on (its quietest frames give the noise's spectrum), so a
    cut-down crunch or squelch doesn't bring a bed of tape hiss with it."""
    nper = 1024
    f, t, X = signal.stft(x, SR, nperseg=nper, noverlap=nper * 3 // 4)
    mag = np.abs(X)
    noise = np.percentile(mag, pct, axis=1, keepdims=True)
    mask = np.clip((mag - over * noise) / (mag + 1e-12), floor, 1.0)
    mask = signal.convolve2d(mask, np.ones((3, 3)) / 9, mode="same", boundary="symm")
    _, y = signal.istft(X * mask, SR, nperseg=nper, noverlap=nper * 3 // 4)
    return fit(y.astype(np.float32), len(x))


def space(x, decay, wet, damp=5000, early=((0.004, 0.5), (0.009, 0.35)), seed=5):
    """A room of our own size (the kit's rooms are a car, a cab, or halls): `decay` the RT60-ish time."""
    h = dsp.ir(decay * 1.3, decay, damp, rng=np.random.default_rng(seed), early=list(early))
    w = signal.fftconvolve(x, h).astype(np.float32)
    return mix(x * (1 - wet), w * wet * 1.2)


def hollow(rng, freqs, length=0.15, q=5, hit=0.004):
    """A hollow body's low modes rung by a knock (a car floor over its frame, a tin roof over the car): a burst of noise
    through a few broad resonances, so it booms without a pitch."""
    n = samples(length)
    x = np.zeros(n, np.float32)
    k = samples(hit)
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    y = dsp.resonate(x, [f * rng.uniform(0.95, 1.05) for f in freqs], q=q, gains=[1 / (1 + 0.5 * i) for i in range(len(freqs))])
    y = y * env([(0, 1), (length, 0)], length, "exp")
    return norm(y)


def pad(rng, length=0.07, f=300):
    """The dull weight of a foot or a body coming down on soft or loose ground: low noise, a fast swell and fall."""
    y = lp(synth.noise(length, rng, "pink"), f, 2) * env([(0, 0), (0.006, 1), (length, 0)], length) ** 1.5
    return norm(y)


def place(parts, length=None):
    """[(t, x, db), ...] on one timeline."""
    end = max(t + len(x) / SR for t, x, _ in parts)
    b = Bus(length or end + 0.01)
    for t, x, db in parts:
        b.at(t, x, db)
    return b.x


# ---- Small physical parts ---------------------------------------------------------------------------------------------------


def tick(rng, f=4000, q=10, length=0.01, body=None):
    """A hard little click (a hobnail, a pawl, a stone on stone)."""
    return synth.click(f, q=q, length=length, rng=rng, body=body)


def grains(rng, n, spread, lo, hi, q=(3, 9), length=(0.006, 0.02), shape=1.0):
    """Hard grains knocking together (stones, coal, grit): `n` clicks over `spread` seconds, front-loaded (most break as
    the weight first comes on), sizes spread so the big ones are few."""
    out = np.zeros(samples(spread + 0.05), np.float32)
    for _ in range(n):
        t = spread * rng.random() ** (1 + shape)
        f = np.exp(rng.uniform(np.log(lo), np.log(hi)))
        c = synth.click(f, q=rng.uniform(*q), length=rng.uniform(*length), rng=rng)
        a = samples(t)
        m = min(len(c), len(out) - a)
        out[a:a + m] += c[:m] * rng.uniform(0.15, 1.0) ** 1.5
    return out


def friction(rng, length, rate=180, lo=600, hi=5000, rough=0.6, shape=None):
    """Something dragged across a surface: stick-slip, a short burst of noise each time it slips (`rate` a second, more
    regular as `rough` falls), tilted dark like a real scrape and held in the surface's band, so it judders instead of
    hissing."""
    n = samples(length)
    out = np.zeros(n + samples(0.01), np.float32)
    t = 0.0
    while t < length:
        t += (1 / rate) * (1 + rough * rng.uniform(-0.7, 0.9))
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(0.002, 0.007))
        g = rng.standard_normal(k).astype(np.float32) * np.hanning(k).astype(np.float32)
        out[a:a + k] += g * rng.uniform(0.25, 1.0) ** 1.5
    y = out[:n] + rng.standard_normal(n).astype(np.float32) * 0.04
    y = lp(bp(y, lo, hi, 2), lo * 2.5, 1)
    if shape is not None:
        y = y * synth.curve(shape, n)
    return norm(y)


def boom(rng, f=110, length=0.3, drop=0.15):
    """A panel's low mode: the drum of a tin roof over a car, a hollow floor."""
    return synth.thump(f * rng.uniform(0.94, 1.06), length, drop=drop)


def whoosh(rng, length, lo=150, hi=1800, peak=0.5, q=1.0):
    """Something swung through the air: pink noise through a band that rises and falls with its speed (no tone)."""
    n = synth.noise(length, rng, "pink")
    fc = env([(0, lo), (length * peak, hi), (length, lo * 1.2)], length, curve="exp")
    y = dsp.sweep_filter(n, "bp", fc, q=q)
    return (y * env([(0, 0), (length * peak, 1), (length, 0)], length) ** 2).astype(np.float32)


# ---- Previews ---------------------------------------------------------------------------------------------------------------


def gait(takes, rng, period, steps=16, lead=0.25, sway=0.012, order=None, lufs=None):
    """A steady walk (or run) through the takes: a step every `period` s, left and right a hair uneven like a real gait,
    the takes cycled in a shuffled order that never plays one twice running."""
    n = len(takes)
    seq = []
    while len(seq) < steps:
        p = list(rng.permutation(n))
        if seq and p[0] == seq[-1]:
            p = p[1:] + p[:1]
        seq += p
    seq = seq[:steps] if order is None else order
    b = Bus(lead + period * len(seq) + 0.6)
    for i, k in enumerate(seq):
        t = lead + i * period + (sway if i % 2 else 0) + rng.normal(0, 0.004)
        b.at(t, takes[k], -0.7 if i % 2 else 0.0)
    return b.x


def leveled(x, lufs):
    return dsp.level(dsp.fade(dsp.trim_silence(x, -60), 0.002, 0.03), lufs)


def slurp(rng, length, lo=200, hi=1000, rise=False, bubbles=0.25):
    """Mud: thick wet noise whose lowpass opens and closes as the seal forms and breaks (`rise`: the suck of something
    pulling out, opening late), with a few low, slow bubbles bursting in it. Broad and dark, not a chirp."""
    n = synth.noise(length, rng, "pink")
    pk = length * (0.75 if rise else rng.uniform(0.25, 0.45))
    fc = env([(0, lo), (pk, hi), (length, lo)], length, curve="exp")
    y = dsp.sweep_filter(n, "lp", fc, q=1.6)
    y = y * env([(0, 0), (0.008, 1), (length * 0.7, 0.6), (length, 0)], length)
    if not bubbles:
        return norm(y)
    b = synth.bubbles(length, 25, 120, 500, rng, rise=(0.05, 0.25))
    return norm(mix(norm(y), b * bubbles))


# ---- Floors: what the surface under a dropped thing does ---------------------------------------------------------------------

DROP = ["wood", "grate", "ground", "concrete"]
WOOD_KNOCKS = ["sfx_100_v2:wood_hit_01", "sfx_100_v2:wood_hit_02", "sfx_100_v2:misc_08", "sfx_100_v2:door_03"]
THIN_PLATE = [f"kenney_impact-sounds:impactPlate_light_00{i}" for i in range(5)]
LIGHT_METAL = [f"kenney_impact-sounds:impactMetal_light_00{i}" for i in range(5)]
CONCRETE_STEPS = [f"kenney_impact-sounds:footstep_concrete_00{i}" for i in range(5)]
STONES = ["sfx_100_v2:stones_01", "sfx_100_v2:stones_02", "sfx_100_v2:stones_03"]
GRAVEL = ["sfx_100_v2:footstep_01", "sfx_100_v2:footstep_02"]
FLOOR_SOURCES = {"wood": WOOD_KNOCKS, "grate": THIN_PLATE + LIGHT_METAL, "ground": STONES + GRAVEL,
                 "concrete": CONCRETE_STEPS}


def floor(rng, mat, take, weight=1.0, hard=1.0):
    """The floor's part in an impact: `weight` 0.2 (a toy) to 3 (a body, a loaded crate) sets how deep and long it sounds,
    `hard` 0 (cloth, flesh) to 1 (iron) how much of the floor's bright knock the thing brings out.
    - wood: a car floor, planks on the frame over a hollow: a real knock (the packs' wood hits), its hollow low modes.
    - grate: open steel grating: thin plate clatter choked by the weight on it, the bars chattering.
    - ground: ballast and dirt: a dull pad, stones knocking and a slice of real gravel crunch.
    - concrete: a hard floor indoors: the packs' concrete slaps, a low thud, a short hard room."""
    w = float(np.clip(weight, 0.1, 4.0))
    if mat == "wood":
        k = get(WOOD_KNOCKS[(take + int(rng.integers(2))) % len(WOOD_KNOCKS)])
        k = norm(tilt(align(k), hi_db=-10 * (1 - hard), lo_db=2 * (w - 1)))
        k = choke(dsp.vari(k, -1.5 * np.log2(w + 0.5) + rng.uniform(-0.4, 0.4)), 0.05 + 0.03 * w, 0.06)
        y = mix(k, hollow(rng, [92, 150, 235], 0.12 + 0.06 * w, q=4) * 0.25 * w)
        return dsp.room(y, "car", wet=0.12, rng=np.random.default_rng(11))
    if mat == "grate":
        p = norm(hp(align(get(THIN_PLATE[(take + int(rng.integers(3))) % 5])), 140))
        p = choke(dsp.vari(p, -2 * np.log2(w + 0.5) + rng.uniform(-0.5, 0.5)), 0.02 + 0.03 * hard, 0.04)
        ring = choke(align(dsp.vari(get(LIGHT_METAL[take % 5]), -6)), 0.005, 0.03) * 0.12 * hard
        chat = np.zeros(samples(0.12), np.float32)
        for i in range(int(rng.integers(3, 6))):
            c = tick(rng, rng.uniform(1600, 4200), q=rng.uniform(18, 35), length=0.02)
            a = samples(0.005 + 0.016 * i + rng.uniform(0, 0.008))
            chat[a:a + len(c)] += c[:len(chat) - a] * rng.uniform(0.3, 0.9)
        return mix(p * (0.5 + 0.5 * hard), norm(ring) * 0.12 * hard, chat * 0.15 * min(w, 2))
    if mat == "ground":
        g = grains(rng, int(14 + 10 * w), 0.06 + 0.04 * w, 700, 5000, q=(3, 9))
        rec = get(GRAVEL[take % 2])
        h = hits(rec, floor_db=-12, gap=0.03)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        rec = norm(hp(cut(denoise(rec), a - samples(0.003), a + samples(0.12 + 0.05 * w), 0.002, 0.04), 300))
        return mix(pad(rng, 0.06 + 0.05 * w, 260) * 0.8 * min(w, 2), norm(g) * 0.5, rec * 0.5)
    if mat == "concrete":
        sl = norm(hp(align(get(CONCRETE_STEPS[(take * 2 + int(rng.integers(2))) % 5])), 80))
        sl = tilt(sl, hi_db=-8 * (1 - hard))
        y = mix(sl, pad(rng, 0.05 + 0.04 * w, 180) * 0.5 * w)
        return space(y, 0.45, 0.14, 5000, ((0.006, 0.5), (0.013, 0.35), (0.021, 0.2)))
    raise KeyError(mat)


def bounce(rng, x, n=2, first=0.12, decay=0.45, drop_db=8):
    """An object bouncing: the impact again `n` times, each sooner and quieter (a dropped tool's clatter)."""
    parts, t, g = [(0.0, x, 0.0)], 0.0, 0.0
    gap = first * rng.uniform(0.85, 1.15)
    for _ in range(n):
        t += gap
        g -= drop_db * rng.uniform(0.8, 1.2)
        parts.append((t, lp(x, 9000 * (1 + g / 40)), g))
        gap *= decay
    return place(parts)


def cloth(rng, take, length=None, keys=("kenney_rpg-audio:cloth1", "kenney_rpg-audio:cloth2", "kenney_rpg-audio:cloth3",
                                        "kenney_rpg-audio:cloth4")):
    """A coat or a sleeve moving: one of the packs' cloth handlings, cut to its loudest part."""
    x = get(keys[take % len(keys)])
    h = hits(x, floor_db=-12, gap=0.05)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    L = length or 0.25
    return norm(cut(x, a - samples(0.02), a + samples(L), 0.01, 0.06))
