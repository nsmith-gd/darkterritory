"""Sounds made from nothing, for the kitbashes: throats, wet bubbles, chitin clicks, creaking wood, fire, reeds.

The packs are short on anything organic, so the enemies' builds lean on these for their living parts and bend real
recordings around them. Each is a small physical model (a glottis through a vocal tract, a bubble's Minnaert ring, a
stick-slip creak through a body's resonances), so it moves like the real thing rather than like an oscillator.
All take a numpy Generator `rng` so a take is repeatable.
"""

import numpy as np
from scipy import signal

from dsp import SR, samples, lp, hp, bp, resonate, sweep_filter, saturate, env, db2a

# Vowel formants (F1-F3 in Hz, bandwidth in Hz) for a child's tract; adults scale them down (tract length).
VOWELS = {
    "u": ((450, 1100, 2650), (70, 110, 160)),
    "o": ((560, 1000, 2900), (80, 110, 170)),
    "a": ((1000, 1600, 3200), (110, 130, 200)),
    "e": ((620, 2550, 3500), (80, 140, 220)),
    "i": ((380, 3000, 3700), (60, 160, 250)),
    "m": ((320, 1350, 2700), (90, 300, 300)),   # a closed-mouth hum
    "h": ((800, 1800, 3000), (300, 400, 500)),  # breath
}
TRACT = {"child": 1.0, "woman": 0.86, "man": 0.72, "beast": 0.5, "giant": 0.36}


def curve(v, n):
    """A constant or a per-sample array, as a per-sample array of n."""
    if np.isscalar(v):
        return np.full(n, float(v), np.float32)
    v = np.asarray(v, np.float32)
    return v[:n] if len(v) >= n else np.concatenate([v, np.full(n - len(v), v[-1], np.float32)])


def glottis(f0, length, rng, jitter=0.004, shimmer=0.06, oq=0.6, sub=0.0, rough=0.0):
    """Rosenberg glottal flow derivative (what a voice is before the mouth shapes it). `f0` a constant or per-sample curve.
    `jitter`/`shimmer` are the period and level wobble of a real throat; `sub` adds period doubling (a growl's fry);
    `rough` amplitude-modulates at 30-70 Hz (a snarl)."""
    os_ = 4
    n = samples(length)
    f = curve(f0, n)
    # cycle-to-cycle jitter as a slowly wandering multiplier
    wander = lp(rng.standard_normal(n).astype(np.float32), 30) * 40 * jitter
    f = f * (1 + wander)
    fo = np.repeat(f, os_)
    ph = np.cumsum(fo / (SR * os_)) % 1.0
    tp, tn = oq * 0.66, oq * 0.34
    flow = np.where(ph < tp, 0.5 * (1 - np.cos(np.pi * ph / tp)),
                    np.where(ph < tp + tn, np.cos(0.5 * np.pi * (ph - tp) / tn), 0.0))
    d = np.diff(flow, prepend=0) * SR * os_ / 1000
    y = signal.resample_poly(d, 1, os_).astype(np.float32)[:n]
    if shimmer:
        y *= 1 + lp(rng.standard_normal(n).astype(np.float32), 40) * 30 * shimmer
    if sub:
        half = np.sign(np.sin(np.pi * np.cumsum(f) / SR))
        y = y * (1 - sub + sub * (0.5 + 0.5 * half))
    if rough:
        rr = 30 + 40 * rng.random()
        y = y * (1 - rough * (0.5 + 0.5 * np.sin(2 * np.pi * np.cumsum(np.full(n, rr) * (1 + 0.2 * wander)) / SR)))
    return (y / (np.max(np.abs(y)) + 1e-9)).astype(np.float32)


def tract(src, vowels, tract_len="child", breath=0.0, rng=None, extra=None):
    """Shape a source through a moving vocal tract. `vowels` is a vowel name or [(t, vowel), ...]; formants glide between
    them. `breath` mixes aspiration noise through the same tract."""
    n = len(src)
    k = TRACT.get(tract_len, tract_len) if isinstance(tract_len, str) else tract_len
    if isinstance(vowels, str):
        vowels = [(0.0, vowels)]
    ts = [t for t, _ in vowels] + [n / SR]
    fcs, bws = [], []
    for i in range(3):
        fs = [VOWELS[v][0][i] * k for _, v in vowels]
        bs = [VOWELS[v][1][i] * max(k, 0.6) for _, v in vowels]
        fcs.append(np.interp(np.arange(n) / SR, ts, fs + [fs[-1]]).astype(np.float32))
        bws.append(np.interp(np.arange(n) / SR, ts, bs + [bs[-1]]).astype(np.float32))
    x = src.copy()
    if breath:
        rng = rng or np.random.default_rng(1)
        x = x + breath * hp(rng.standard_normal(n).astype(np.float32), 300) * 0.3
    out = np.zeros(n, np.float32)
    for i, g in enumerate((1.0, 0.7, 0.35)):
        q = float(np.median(fcs[i] / bws[i]))
        out += g * sweep_filter(x, "bp", fcs[i], q=q, block=128)
    if extra:
        for f, q, g in extra:
            out += g * sweep_filter(x, "bp", np.full(n, f * k, np.float32), q=q)
    return (out / (np.max(np.abs(out)) + 1e-9)).astype(np.float32)


def voice(f0, length, vowels="u", tract_len="child", rng=None, breath=0.1, **glot):
    """A sung or cried voice: glottis through a tract."""
    rng = rng or np.random.default_rng(0)
    return tract(glottis(f0, length, rng, **glot), vowels, tract_len, breath=breath, rng=rng)


def breath(length, vowels="h", tract_len="man", rng=None, shape=None):
    """Breath through a mouth: noise through the tract. `shape` an envelope (inhale/exhale)."""
    rng = rng or np.random.default_rng(0)
    n = samples(length)
    x = rng.standard_normal(n).astype(np.float32)
    y = tract(x, vowels, tract_len)
    if shape is not None:
        y = y * curve(shape, n)
    return y


def bubble(f0, length=None, rise=0.1, rng=None, amp=1.0):
    """One bubble's ring (Minnaert): a sine that dies fast and rises in pitch as the bubble nears the surface."""
    tau = 0.004 + 6.0 / f0
    length = length or min(0.25, tau * 6)
    n = samples(length)
    t = np.arange(n) / SR
    f = f0 * (1 + rise * t / tau)
    y = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / tau)
    att = min(n, samples(0.0008))
    y[:att] *= np.linspace(0, 1, att)
    return (amp * y).astype(np.float32)


def bubbles(length, rate, fmin, fmax, rng, rise=(0.05, 0.3), shape=None):
    """A stream of bubbles: `rate` a second (a constant or a curve over the length), radii spread so sizes vary."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = curve(rate, n)
    t = 0.0
    while t < length:
        r = max(rc[min(samples(t), n - 1)], 0.1)
        t += rng.exponential(1 / r)
        if t >= length:
            break
        f = np.exp(rng.uniform(np.log(fmin), np.log(fmax)))
        b = bubble(f, rise=rng.uniform(*rise), amp=rng.uniform(0.3, 1.0) * (fmin / f) ** 0.3)
        a = samples(t)
        m = min(len(b), n - a)
        out[a:a + m] += b[:m]
    if shape is not None:
        out *= curve(shape, n)
    return out


def click(f=3500, q=12, length=0.012, rng=None, body=None):
    """A chitin click: a tick of noise rung through a hard little shell."""
    rng = rng or np.random.default_rng(0)
    n = samples(length)
    x = np.zeros(n, np.float32)
    k = samples(rng.uniform(0.0003, 0.0012))
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    fs = body or [f, f * rng.uniform(1.4, 1.8), f * rng.uniform(2.3, 2.9)]
    y = resonate(x, fs, q=q, gains=[1.0, 0.6, 0.35][:len(fs)]) + x * 0.4
    return (y * np.exp(-np.arange(n) / SR / (length / 4))).astype(np.float32)


def skitter(length, rate, rng, f=(2500, 6000), q=(6, 18), legs=6, shape=None):
    """Many hard little feet: clicks in bursts at a gait (each leg a slightly different shell)."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = curve(rate, n)
    leg_f = [np.exp(rng.uniform(np.log(f[0]), np.log(f[1]))) for _ in range(legs)]
    t = 0.0
    while t < length:
        r = max(rc[min(samples(t), n - 1)], 0.5)
        t += rng.exponential(1 / r)
        if t >= length:
            break
        lf = leg_f[rng.integers(legs)] * rng.uniform(0.9, 1.1)
        c = click(lf, q=rng.uniform(*q), length=rng.uniform(0.006, 0.02), rng=rng) * rng.uniform(0.3, 1.0)
        a = samples(t)
        m = min(len(c), n - a)
        out[a:a + m] += c[:m]
    if shape is not None:
        out *= curve(shape, n)
    return out


WOOD = [182, 417, 760, 1290, 2110, 3300]        # a board's modes
IRON = [96, 243, 470, 912, 1530, 2380, 3700]     # a heavy iron part
TIN = [390, 880, 1460, 2240, 3350, 4900]         # a car roof's tin
BARK = [140, 330, 610, 1050, 1720]               # a dry, hollow trunk


def creak(length, rate, rng, body=WOOD, q=18, jitter=0.25, shape=None, grit=0.3):
    """Stick-slip: a pulse each time the joint slips, `rate` a second (a constant or a curve), rung through a body. A slow
    rate is a creak, a fast one a groan; jitter makes it catch."""
    n = samples(length)
    rc = curve(rate, n)
    x = np.zeros(n, np.float32)
    t = 0.0
    while t < length:
        r = max(rc[min(samples(t), n - 1)], 1.0)
        t += (1 / r) * (1 + jitter * rng.standard_normal() * 0.5)
        a = samples(t)
        if a >= n:
            break
        x[a] = rng.uniform(0.4, 1.0)
        if grit and rng.random() < grit:
            k = min(samples(0.002), n - a)
            x[a:a + k] += rng.standard_normal(k) * 0.3
    y = resonate(x, body, q=q, gains=[1 / (1 + 0.35 * i) for i in range(len(body))])
    if shape is not None:
        y *= curve(shape, n)
    return (y / (np.max(np.abs(y)) + 1e-9)).astype(np.float32)


def crackle(length, rate, rng, shape=None, size=(0.0002, 0.003), hi=1500):
    """Fire's crackle: sharp ticks of burst sap, a few loud and many quiet (a power law), Poisson in time."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    rc = curve(rate, n)
    t = 0.0
    while t < length:
        r = max(rc[min(samples(t), n - 1)], 0.2)
        t += rng.exponential(1 / r)
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(*size)) + 2
        amp = min(1.0, 0.05 * rng.pareto(1.6) + 0.04)
        g = rng.standard_normal(k) * np.exp(-np.linspace(0, 6, k))
        m = min(k, n - a)
        out[a:a + m] += (amp * g[:m]).astype(np.float32)
    out = hp(out, hi, 2)
    if shape is not None:
        out *= curve(shape, n)
    return out


def fire(length, intensity, rng):
    """A fire: crackle and pops over hiss and a lapping roar. `intensity` 0 (smouldering) to 1 (a car alight), or a curve."""
    n = samples(length)
    I = curve(intensity, n)
    # roar: brown noise, lowpassed, lapping at 2-6 Hz
    br = np.cumsum(rng.standard_normal(n)).astype(np.float32)
    br = hp(br, 30, 2)
    br /= np.max(np.abs(br)) + 1e-9
    lap = 0.6 + 0.4 * lp(rng.standard_normal(n).astype(np.float32), 4) * 8
    roar = lp(br, 400 + 900 * float(np.mean(I)), 2) * np.clip(lap, 0.1, 1.5) * (I ** 1.5)
    # hiss: gas escaping the wood
    hiss = bp(rng.standard_normal(n).astype(np.float32), 2500, 9000) * 0.08 * (0.3 + I)
    hiss *= np.clip(0.6 + lp(rng.standard_normal(n).astype(np.float32), 3) * 10, 0.1, 1.5)
    # crackle and pops
    cr = crackle(length, 6 + 120 * I, rng) * 1.0
    pops = crackle(length, 0.5 + 5 * I, rng, size=(0.003, 0.012), hi=300) * 1.8
    y = roar * 0.6 + hiss + cr + pops
    return (y / (np.max(np.abs(y)) + 1e-9)).astype(np.float32)


def rustle(length, density, rng, f=(5000, 15000), shape=None, ticks=0.3):
    """Dry leaves and reeds: thousands of tiny scrapes (short bandpassed noise grains), with brittle stalk ticks."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    dc = curve(density, n)
    t = 0.0
    while t < length:
        d = max(dc[min(samples(t), n - 1)], 1.0)
        t += rng.exponential(1 / d)
        a = samples(t)
        if a >= n:
            break
        k = samples(rng.uniform(0.003, 0.04))
        fc = np.exp(rng.uniform(np.log(f[0]), np.log(f[1])))
        g = rng.standard_normal(k).astype(np.float32) * np.hanning(k).astype(np.float32)
        g = bp(g, fc * 0.7, min(fc * 1.4, SR * 0.45)) * rng.uniform(0.2, 1.0)
        m = min(k, n - a)
        out[a:a + m] += g[:m]
        if ticks and rng.random() < ticks * 0.05:
            c = click(rng.uniform(3000, 9000), q=8, length=0.006, rng=rng) * rng.uniform(0.3, 0.8)
            m = min(len(c), n - a)
            out[a:a + m] += c[:m]
    if shape is not None:
        out *= curve(shape, n)
    return out


def thump(f=55, length=0.25, rng=None, drop=0.5):
    """A sub thump: a sine that drops in pitch as it dies (weight landing)."""
    n = samples(length)
    t = np.arange(n) / SR
    fr = f * (1 - drop * t / length)
    y = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / (length / 4))
    y[:samples(0.002)] *= np.linspace(0, 1, samples(0.002))
    return y.astype(np.float32)


def noise(length, rng, colour="white"):
    n = samples(length)
    x = rng.standard_normal(n).astype(np.float32)
    if colour == "pink":
        b, a = [0.049922035, -0.095993537, 0.050612699, -0.004408786], [1, -2.494956002, 2.017265875, -0.522189400]
        x = signal.lfilter(b, a, x).astype(np.float32)
    elif colour == "brown":
        x = hp(np.cumsum(x).astype(np.float32), 20, 2)
    return x / (np.max(np.abs(x)) + 1e-9)
