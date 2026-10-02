"""Physics the train and world lines share (world_*.py): steam jets, wind, rain and drips, things struck, rubbed, torn and
poured, thunder, and seamless loops. No recipes here, only the models.

The director's rule for gameplay sounds: the real thing, from real recordings where the packs have it, and synthesis only
where physics makes it convincing. These are the physics: each model is driven by the quantity that drives the real
event (a jet by the pressure behind it and the size of the hole, wind by its speed, a pour by its flow), so a sound moves
the way the real one does when that quantity moves.

Loops are built periodic from the start: their noise is made in the frequency domain (so it is exactly one cycle long),
slow modulations are periodic, events are placed modulo the cycle and their tails folded back (dsp.wrap), and reverb is
a circular convolution. `seamless` then hands build.py a cycle its seam crossfade turns back into exactly that cycle.
"""

import numpy as np
from scipy import signal

import dsp
import src
import synth
from dsp import SR, samples, lp, hp, bp, env, mix, fit

TAU = 2 * np.pi


# ---- Loops ----------------------------------------------------------------------------------------------------------------

def seamless(s, xfade=0.3):
    """A cycle `s` (already periodic) -> what build.py's equal-power seam crossfade turns back into exactly `s`: the cycle
    plus its own head, both pre-weighted so the crossfade of two identical copies sums to one. Rhythmic loops keep their
    beat across the seam and nothing dips."""
    k = samples(xfade)
    t = np.linspace(0, np.pi / 2, k)
    w = (1 / (np.sin(t) + np.cos(t))).astype(np.float32)
    s = s - np.mean(s)                                  # a cycle's mean is exactly its DC
    y = np.concatenate([s, s[:k]]).astype(np.float32)
    y[:k] *= w
    y[-k:] *= w
    return y


def pnoise(n, rng, shape=None):
    """Noise exactly n samples periodic, coloured by `shape(f) -> amplitude` (made in the frequency domain)."""
    X = rng.standard_normal(n // 2 + 1) + 1j * rng.standard_normal(n // 2 + 1)
    if shape is not None:
        f = np.fft.rfftfreq(n, 1 / SR)
        X = X * shape(np.maximum(f, 1e-3))
    X[0] = 0
    y = np.fft.irfft(X, n)
    return (y / (np.std(y) + 1e-12)).astype(np.float32)


def slow(n, rate, rng):
    """A slow random wander, unit std, nothing faster than `rate` Hz, and periodic over n (a loop's modulations loop too)."""
    m = n // 2 + 1
    f = np.fft.rfftfreq(n, 1 / SR)
    X = (rng.standard_normal(m) + 1j * rng.standard_normal(m)) * np.exp(-0.5 * (f / max(rate, 1e-3)) ** 2)
    X[0] = 0
    y = np.fft.irfft(X, n)
    return (y / (np.std(y) + 1e-12)).astype(np.float32)


def cfilter(x, shape):
    """Zero-phase, circular filtering by an amplitude response `shape(f)`: keeps a loop a loop."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    return np.fft.irfft(np.fft.rfft(x) * shape(np.maximum(f, 1e-3)), n).astype(np.float32)


def cyclic(fn, x):
    """Run a causal process (a filter, a compressor) over a loop so its result is periodic too: process two cycles, keep
    the second, whose filter state has come round from the first. `fn` gets the two cycles (any per-sample curve it
    uses must be given two cycles long too)."""
    n = len(x)
    return np.asarray(fn(np.concatenate([x, x])), np.float32)[n:2 * n]


def croom(x, name, wet, rng):
    """dsp.room as a circular convolution, for loops: the reverb tail rings on into the head."""
    length, decay, damp, early = dsp.ROOMS[name]
    h = dsp.ir(length, decay, damp, rng=rng, early=early)
    n = len(x)
    if len(h) > n:
        h = h[:n]
    w = np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(h, n), n).astype(np.float32)
    return (x * (1 - wet) + w * wet * 1.2).astype(np.float32)


def place(n, events, loop=True):
    """Sum [(t, sound, gain), ...] onto n samples; in a loop, events past the end come round to the start."""
    out = np.zeros(n, np.float32)
    for t, x, g in events:
        a = samples(t) % n if loop else samples(t)
        if not loop and a >= n:
            continue
        seg = x * g
        if loop:
            buf = np.zeros(a + len(seg), np.float32)
            buf[a:] = seg
            out += dsp.wrap(buf, n)
        else:
            m = min(len(seg), n - a)
            out[a:a + m] += seg[:m]
    return out


def poisson(length, rate, rng, jitter=None):
    """Event times at an average `rate` a second (a constant)."""
    t, out = 0.0, []
    while True:
        t += rng.exponential(1 / rate)
        if t >= length:
            return out
        out.append(t)


def norm(x):
    return (x / (np.max(np.abs(x)) + 1e-9)).astype(np.float32)


def curve(v, n):
    return synth.curve(v, n)


def space(x, rng, name="night", wet=0.2, early=((0.0045, 0.35), (0.011, 0.2))):
    """Open air beside the train: a couple of slaps off the train's own sides, then the night (one-shots)."""
    y = x.copy()
    for dt, g in early:
        k = samples(dt)
        y[k:] += x[:-k] * g
    return hp(dsp.room(y, name, wet=wet, rng=rng), 20)


# ---- Steam ----------------------------------------------------------------------------------------------------------------

BANDS = 2 ** np.arange(np.log2(30), np.log2(19000), 0.5)    # half-octave centres


def _bands(n, rng):
    """Noise split into half-octave bands (zero-phase Gaussian bands in log frequency), each at unit level: weighting them
    sets the spectrum per octave, the way the ear and a jet's spectrum are both measured."""
    X = rng.standard_normal(n // 2 + 1) + 1j * rng.standard_normal(n // 2 + 1)
    X[0] = 0
    f = np.maximum(np.fft.rfftfreq(n, 1 / SR), 1e-3)
    out = []
    for fc in BANDS:
        H = np.exp(-0.5 * ((np.log2(f) - np.log2(fc)) / 0.32) ** 2)
        b = np.fft.irfft(X * H, n).astype(np.float32)
        out.append(b / (np.std(b) + 1e-12))
    return out


def jet(length, rng, pressure=1.0, opening=1.0, peak=1500.0, low=1.0, rasp=0.0, eddy=0.7, tilt=(1.0, 0.75), n=None):
    """Turbulent jet noise: steam (or gas) out of a hole. The physics, kept to what the ear hears:
    - Velocity rises with the `pressure` behind the hole (U ~ sqrt(p)), and the noise peaks at a Strouhal frequency
      0.2 U / D: so `peak` (Hz at full pressure and full `opening`) is low for a big valve and high for a pinhole, and it
      climbs as a valve closes (the hole shrinks) and falls as the pressure dies.
    - Loudness rises steeply with velocity and with the hole's size (amplitude ~ opening^0.7 * pressure^1.5).
    - The spectrum is a broad hump (`tilt`: slopes below and above the peak, per octave of band amplitude): the roar
      under the hiss that makes steam steam rather than a fizz.
    - Turbulence: every band's level wanders, the big slow eddies (low bands) slowly, the fine fast ones quickly (`eddy`).
    - `low`: the plume billowing (large-scale structure near the ear, 25-250 Hz), the pressure you feel in it.
    - `rasp`: jet crackle, the steep positive shocklets of a choked high-pressure jet (a safety valve's tearing edge).
    `pressure`/`opening` are constants or per-sample curves. With `n` the result is exactly n samples and periodic."""
    n = n or samples(length)
    p = np.clip(curve(pressure, n), 0, None)
    o = np.clip(curve(opening, n), 0, None)
    fp = peak * np.sqrt(p + 1e-6) / np.maximum(o, 0.22)    # a feathering valve still sizzles audibly, not ultrasonically
    amp = o ** 0.7 * p ** 1.5      # gentler than the physics' D*p^2, as the ear hears it: a closing valve's last hiss stays audible
    a, b = tilt
    out = np.zeros(n, np.float32)
    bands = _bands(n, rng)
    # gains change slowly: compute them on a coarse grid
    step = 64
    idx = np.arange(0, n, step)
    for fc, x in zip(BANDS, bands):
        r = fc / fp[idx]
        w = 2 / (r ** -a + r ** b)
        rate = float(np.clip(fc / 60, 0.4, 60))
        m = 1 + eddy * 0.45 * slow(n, rate, rng)[idx]
        g = np.interp(np.arange(n), idx, w * np.clip(m, 0.05, None) * amp[idx]).astype(np.float32)
        out += x * g
    if rasp:
        z = cfilter(out, lambda f: (f / 900) ** 2 / (1 + (f / 900) ** 2))
        z = np.clip(z / (np.std(z) + 1e-9), -4, 3.2)
        c = np.exp(0.7 * z) - np.exp(0.245)        # log-normal: sharp positive spikes, the skew of real crackle
        c = cfilter(c.astype(np.float32), lambda f: (f / 700) ** 2 / (1 + (f / 700) ** 2))
        out += rasp * 0.25 * c / (np.std(c) + 1e-9) * np.std(out) * (amp / (np.max(amp) + 1e-9))
    if low:
        plume = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 260) ** 2) * (f / 35) ** 2 / (1 + (f / 35) ** 2))
        plume /= np.std(plume) + 1e-9
        bil = np.clip(1 + 0.5 * slow(n, 1.2, rng) + 0.3 * slow(n, 6, rng), 0.1, None)
        ref = np.std(out) / (np.sqrt(np.mean(amp ** 2)) + 1e-9)
        out += low * 0.55 * ref * plume * bil * amp * np.sqrt(o / (np.max(o) + 1e-9))
    return out.astype(np.float32)


def howl(n, rng, freqs, amount, wander=0.03, rate=0.5, q=40):
    """Narrow, unsteady tones a choked jet sings (screech, a valve's whistle): noise rung at `freqs` that drift and
    come and go, never a clean sine."""
    out = np.zeros(n, np.float32)
    for f in freqs:
        fc = f * (1 + wander * slow(n, rate, rng))
        x = cyclic(lambda z: dsp.sweep_filter(z, "bp", np.concatenate([fc, fc]), q=q, block=128),
                   rng.standard_normal(n).astype(np.float32))
        on = np.clip(0.6 + 0.7 * slow(n, rate * 1.5, rng), 0, 1.4)
        out += x * on
    return (amount * out / (np.std(out) + 1e-9)).astype(np.float32)


def sputter(length, rng, rate=12, size=1.0):
    """Condensate spat out with the first of the steam: wet coughs, each a short burst with bubbles and a slap."""
    n = samples(length)
    out = np.zeros(n, np.float32)
    for t in poisson(length, rate, rng):
        L = rng.uniform(0.02, 0.07) * size
        k = samples(L)
        e = np.exp(-np.linspace(0, 5, k)).astype(np.float32)
        b = bp(rng.standard_normal(k).astype(np.float32), 500, 6000) * e
        b = b + synth.bubbles(L, 300, 700, 3500, rng)[:k] * 0.5
        a = samples(t)
        m = min(k, n - a)
        out[a:a + m] += b[:m] * rng.uniform(0.3, 1.0)
    return out


# ---- Struck things ----------------------------------------------------------------------------------------------------

# Mode-frequency ratios: a free bar (a rod, a rail end), a plate (sheet iron, tin, boards), a thick casting.
BAR = [1.0, 2.756, 5.404, 8.933, 13.34]
PLATE = [1.0, 1.59, 2.14, 2.30, 2.65, 2.92, 3.16, 3.50, 4.15, 4.6, 5.2, 5.9, 6.6, 7.4, 8.3]


def modal(freqs, decays, amps, length, rng, contact=0.0004):
    """A struck body: decaying sinusoids (modes), excited through a contact pulse of `contact` seconds (hard metal on
    metal ~0.2 ms rings bright; wood ~1 ms; a soft blow ~5 ms only reaches the low modes)."""
    n = samples(length)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float32)
    for f, d, a in zip(freqs, decays, amps):
        if f >= SR * 0.45:
            continue
        y += (a * np.exp(-t / d) * np.sin(TAU * f * t + rng.uniform(0, TAU))).astype(np.float32)
    k = max(2, samples(contact))
    pulse = np.sin(np.linspace(0, np.pi, k)).astype(np.float32)
    y = signal.fftconvolve(y, pulse)[:n].astype(np.float32)
    att = samples(0.0006)
    y[:att] *= np.linspace(0, 1, att)
    return y


def body(rng, f1, ratios=PLATE, decay=0.4, damp=1.0, length=None, contact=0.0004, spread=0.04, tilt=0.7, count=None):
    """A modal body from a ratio family: f1 its lowest mode, higher modes dying faster (`damp`), a little mistuning."""
    rs = ratios[:count] if count else ratios
    fs = [f1 * r * (1 + spread * rng.standard_normal()) for r in rs]
    ds = [decay / (1 + damp * max(f / f1 - 1, 0) ** 0.7) for f in fs]
    am = [rng.uniform(0.4, 1.0) / (f / f1) ** tilt for f in fs]
    return modal(fs, ds, am, length or min(4.0, decay * 5 + 0.05), rng, contact)


def sheet(rng, f1=60, fmax=5000, decay=1.2, length=None, contact=0.0006, density=1.0, tilt=0.5):
    """A big sheet of iron (a car side, a boiler shell, a chute): dense modes from f1 up, the high ones dying fast."""
    k = int(np.clip(40 * density * np.log2(fmax / f1), 8, 160))
    fs = np.exp(rng.uniform(np.log(f1), np.log(fmax), k))
    fs = np.sort(fs)
    ds = decay * (f1 / fs) ** 0.45 * rng.uniform(0.6, 1.2, k)
    am = rng.uniform(0.3, 1.0, k) * (f1 / fs) ** tilt
    return modal(fs, ds, am, length or min(5.0, decay * 3 + 0.1), rng, contact)


def knock(rng, f=90, length=0.3, drop=0.35):
    """The weight behind a blow: a sub thump (synth.thump) with a dull bump of low noise."""
    t = synth.thump(f * rng.uniform(0.92, 1.08), length, drop=drop)
    nz = lp(rng.standard_normal(samples(length)).astype(np.float32), 260) * env([(0, 1), (0.03, 0.25), (length, 0)], length)
    return mix(t, nz * 0.35)


def rec(key, semis=0.0, start=0.0, length=None, lo=None, hi=None, tau=None):
    """A source recording, varispeeded (heavier when pitched down: bigger, slower), trimmed and filtered. `tau` damps its
    ring (seconds): a struck part that's bolted, oiled or loaded rings shorter than the free piece recorded."""
    x = src.get(key)
    if start or length:
        x = dsp.trim(x, start, length)
    if semis:
        x = dsp.vari(x, semis)
    if hi:
        x = lp(x, hi, 2)
    if lo:
        x = hp(x, lo, 2)
    x = dsp.trim_silence(x, -55, 0.002)
    if tau:
        x = (x * np.exp(-np.arange(len(x)) / SR / tau)).astype(np.float32)
    return x


# ---- Rubbed, torn, broken -------------------------------------------------------------------------------------------------

def friction(n, rng, modes, rough=1.0, grit=0.5, q=25, loop=False):
    """Rubbing (a brake block on a tyre, steel dragged on steel): broadband contact noise, its level roughened by the
    asperities passing (fast random AM), rung through the rubbed body's modes. `loop` makes it periodic over n."""
    x = rng.standard_normal(n).astype(np.float32)
    r = rng.standard_normal(n).astype(np.float32)

    def proc(x, r):
        x = x * np.abs(1 + rough * 3.6 * lp(r, 180))
        rung = dsp.resonate(x, modes, q=q, gains=[1 / (1 + 0.25 * i) for i in range(len(modes))])
        return rung / (np.std(rung) + 1e-9) + grit * bp(x, 1500, 9000) / (np.std(x) + 1e-9)

    y = proc(np.tile(x, 2), np.tile(r, 2))[n:] if loop else proc(x, r)
    return y.astype(np.float32)


def squeal(n, rng, freqs, on, wander=0.004, rate=3.0, harm=0.25):
    """Self-excited squeal (a flange on a curve, a glazed brake): a wheel mode locked into a limit cycle, so a near-pure
    tone with its harmonic, its pitch shivering and its level coming and going with `on` (a per-sample 0..1 curve).
    Each tone makes a whole number of cycles over n, so it loops."""
    out = np.zeros(n, np.float32)
    on = curve(on, n)
    for i, f in enumerate(freqs):
        fi = f * (1 + wander * slow(n, rate, rng) + 0.3 * wander * slow(n, 25, rng))
        cycles = np.sum(fi) / SR
        fi = fi * (max(1, round(cycles)) / cycles)
        ph = TAU * np.cumsum(fi) / SR
        lvl = np.clip(on * (0.6 + 0.6 * slow(n, rate * 0.7, rng)), 0, 1.2) ** 1.5
        tone = np.sin(ph) + harm * np.sin(2 * ph + 0.7) + 0.4 * harm * np.sin(3 * ph + 1.3)
        out += (tone * lvl / (1 + i * 0.5)).astype(np.float32)
    return out


def tear(length, rng, rate, body_f=(140, 4200), q=14, crack=0.5):
    """Metal under force (iron wrenched, a car side ripping, a rivet line letting go): plastic yield and fracture as
    stick-slip events whose `rate` (a curve) climbs as it tears, each event ringing a different patch of the sheet,
    with a fracture crackle on top."""
    n = samples(length)
    rc = curve(rate, n)
    x = np.zeros(n, np.float32)
    t = 0.0
    while True:
        r = max(rc[min(samples(t), n - 1)], 2.0)
        t += rng.exponential(1 / r) * 0.6 + 0.4 / r
        a = samples(t)
        if a >= n:
            break
        x[a] += rng.uniform(0.2, 1.0) * (1 if rng.random() < 0.8 else 2.5)
    fs = np.exp(rng.uniform(np.log(body_f[0]), np.log(body_f[1]), 14))
    y = dsp.resonate(x, list(fs), q=q, gains=list(rng.uniform(0.4, 1.0, 14)))
    y = y / (np.std(y) + 1e-9)
    if crack:
        c = synth.crackle(length, rc * 1.5, rng, size=(0.0002, 0.002), hi=1200)
        y = y + crack * c / (np.std(c) + 1e-9)
    return y.astype(np.float32)


def splinter(length, rng, rate, body=synth.WOOD, crack=0.8):
    """Wood giving way: fibres snapping in a run that speeds up (`rate` a curve), each a sharp tick rung through the board,
    over the board's own creak as it bends."""
    n = samples(length)
    s = synth.creak(length, rate, rng, body=[f * rng.uniform(0.8, 1.25) for f in body], q=9, jitter=0.6, grit=0.8)
    c = synth.crackle(length, curve(rate, n) * 3, rng, size=(0.0003, 0.004), hi=600)
    return (s + crack * c / (np.max(np.abs(c)) + 1e-9)).astype(np.float32)


# ---- Water ------------------------------------------------------------------------------------------------------------

def drip(rng, into="water", size=1.0):
    """One drop landing. Into water: the splash tick, then the entrained bubble's Minnaert ring rising in pitch (the
    'plip'); onto stone or iron: a tick and a tiny ring."""
    L = 0.25
    n = samples(L)
    tick = np.zeros(n, np.float32)
    k = samples(rng.uniform(0.0003, 0.0009))
    tick[:k] = rng.standard_normal(k) * np.hanning(k)
    tick = bp(tick, 900, 9000) * 0.6
    if into == "water":
        f0 = rng.uniform(700, 1800) / size
        b = synth.bubble(f0, length=L, rise=rng.uniform(0.4, 1.2), amp=1.0)
        b = np.concatenate([np.zeros(samples(rng.uniform(0.002, 0.006)), np.float32), b])[:n]
        return mix(tick * 0.5, fit(b, n) * rng.uniform(0.6, 1.0))
    ring = body(rng, rng.uniform(1800, 3200), PLATE, decay=0.03 if into == "stone" else 0.12, length=0.2, count=6)
    return mix(tick, fit(ring, n) * (0.15 if into == "stone" else 0.5))


def rain(n, rng, rate, surface="ground", loop=True):
    """Rain: thousands of drop impacts a second, each tiny, a few big (a power law), Poisson in time (`rate` drops/s).
    On tin each drop rings the roof's panel modes; on the ground each is a soft splat; on water a bubble."""
    events = []
    L = n / SR
    m = int(rate * L)
    ts = np.sort(rng.uniform(0, L, m))
    amps = np.minimum(1.0, 0.02 * rng.pareto(1.8, m) + 0.01)
    x = np.zeros(n, np.float32)
    idx = (ts * SR).astype(int) % n
    np.add.at(x, idx, amps.astype(np.float32) * rng.choice([-1, 1], m))
    if surface == "tin":
        # a few panel responses, chosen per drop: convolve each subset with its own ring
        out = np.zeros(n, np.float32)
        for k in range(4):
            mask = np.zeros(n, np.float32)
            sel = idx[k::4]
            np.add.at(mask, sel, x[sel])
            h = body(rng, rng.uniform(1100, 2400), PLATE, decay=0.012, length=0.06, contact=0.00012, count=12)
            out += _circconv(mask, h)
        return out
    if surface == "water":
        out = np.zeros(n, np.float32)
        for k in range(6):
            mask = np.zeros(n, np.float32)
            sel = idx[k::6]
            np.add.at(mask, sel, x[sel])
            out += _circconv(mask, synth.bubble(rng.uniform(2500, 9000), rise=0.3))
        return out
    # ground, leaves, ballast: a short soft splat
    h = rng.standard_normal(samples(0.004)).astype(np.float32) * np.exp(-np.linspace(0, 6, samples(0.004))).astype(np.float32)
    return bp(_circconv(x, h), 400, 7000) if not loop else cfilter(_circconv(x, h), lambda f: (f / 400) / (1 + f / 400) / (1 + (f / 7000) ** 2))


def _circconv(x, h):
    n = len(x)
    return np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(h, n), n).astype(np.float32)


# ---- Air --------------------------------------------------------------------------------------------------------------

FITTINGS = [0.006, 0.009, 0.014, 0.022, 0.032, 0.045]    # wire, rod, handrail, ladder rung, lamp bracket (diameters, m)


def wind(n, rng, speed, gust=0.25, gust_rate=0.15, buffet=0.6, whistle=0.35, flap=0.0, fittings=FITTINGS, hiss=1.0):
    """Moving air at `speed` m/s (a constant or curve), gusting by `gust` (fraction) at `gust_rate` Hz:
    - rush: turbulent noise whose brightness and loudness ride the instantaneous speed;
    - buffet: the wind pummelling the listener and the train's edges (20-120 Hz), lumpy and irregular;
    - whistle: aeolian tones off the fittings, each at 0.2 U / d, so they glide up and down with every gust;
    - flap: loose cloth and tarpaulin flapping (a fast irregular flutter of a mid band).
    Periodic over n, so loops loop."""
    U = curve(speed, n) * np.clip(1 + gust * slow(n, gust_rate, rng) + 0.35 * gust * slow(n, gust_rate * 4, rng), 0.15, None)
    Un = U / (np.mean(U) + 1e-9)
    base = pnoise(n, rng, lambda f: 1 / np.sqrt(f))                 # pink
    mean_u = float(np.mean(U))
    fc = 250 + 55 * mean_u
    rush = cfilter(base, lambda f: 1 / (1 + (f / fc) ** 2) * (f / 60) / (1 + f / 60))
    # brightness follows the gusts: a hiss layer that only comes up at the peaks
    air = cfilter(pnoise(n, rng), lambda f: (f / 2000) / (1 + (f / 2000)) / (1 + (f / 9000) ** 2))
    y = rush * Un ** 2.2 + hiss * 0.18 * air * np.clip(Un, 0, None) ** 4
    if buffet:
        bu = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(45)) / 0.9) ** 2))
        lump = np.clip(1 + 0.9 * slow(n, 3.0, rng), 0, None) ** 1.5
        y += buffet * 1.6 * bu * lump * Un ** 3
    if whistle:
        for d in fittings:
            f0 = 0.2 * U / d
            if np.max(f0) < 60:
                continue
            fc = np.clip(f0, 40, 12000)
            x = cyclic(lambda z: dsp.sweep_filter(z, "bp", np.concatenate([fc, fc]), q=35, block=64),
                       rng.standard_normal(n).astype(np.float32))
            lock = np.clip(0.2 + slow(n, 0.25, rng), 0, None) ** 2          # shedding locks in and out
            y += whistle * 0.5 * x / (np.std(x) + 1e-9) * lock * Un ** 2 * (0.6 + 0.4 * rng.random())
    if flap:
        fl = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(500)) / 1.2) ** 2))
        flut = np.clip(slow(n, 14, rng) + 0.4, 0, None) ** 2 * np.clip(Un - 0.6, 0, None)
        y += flap * 1.2 * fl * flut
    return y.astype(np.float32), U


# ---- Granular -----------------------------------------------------------------------------------------------------------

def pour(n, rng, rate, grain=(1500, 6000), lump=0.0, thunder=0.5, ring=None, loop=True):
    """A pour of lumps or grains: `rate` impacts a second (a curve: the flow), each a brittle click whose pitch says its
    size (`grain` Hz range), a few big `lump`s thudding among them, the mass of it a low roar (`thunder`), and the steel
    of a chute ringing (`ring`: mode frequencies) under the stream."""
    L = n / SR
    rc = curve(rate, n)
    mean_rate = float(np.mean(rc))
    m = int(mean_rate * L)
    # thin a uniform stream by the flow curve
    ts = np.sort(rng.uniform(0, L, int(m * 1.6)))
    keep = rng.random(len(ts)) < rc[(ts * SR).astype(int) % n] / (np.max(rc) + 1e-9)
    ts = ts[keep]
    amps = np.minimum(1.0, 0.04 * rng.pareto(1.5, len(ts)) + 0.02).astype(np.float32)
    out = np.zeros(n, np.float32)
    nb = 6
    for k in range(nb):
        sel = slice(k, None, nb)
        x = np.zeros(n, np.float32)
        np.add.at(x, (ts[sel] * SR).astype(int) % n, amps[sel] * rng.choice([-1, 1], len(amps[sel])))
        f = np.exp(np.log(grain[0]) + (np.log(grain[1]) - np.log(grain[0])) * k / (nb - 1))
        h = body(rng, f, PLATE, decay=0.006, length=0.03, contact=0.0002, count=8)
        out += _circconv(x, h)
    out = out / (np.std(out) + 1e-9)
    flow = rc / (np.max(rc) + 1e-9)
    if lump:
        for t in poisson(L, lump, rng):
            k = body(rng, rng.uniform(250, 700), PLATE, decay=0.03, length=0.12, contact=0.0015, count=8)
            k = mix(k, knock(rng, rng.uniform(70, 120), 0.12) * 0.6)
            out = place(n, [(t, norm(k), rng.uniform(1.5, 4.0) * flow[samples(t) % n])], loop) + out
    if thunder:
        roar = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 180) ** 2) * (f / 30) / (1 + f / 30))
        roar *= np.clip(1 + 0.4 * slow(n, 2.5, rng), 0, None)
        out += thunder * 1.5 * roar * flow
    if ring is not None:
        g = [1 / (1 + 0.3 * i) for i in range(len(ring))]
        rung = cyclic(lambda z: dsp.resonate(z, ring, q=60, gains=g), out) if loop else dsp.resonate(out, ring, q=60, gains=g)
        out += 0.6 * rung / (np.std(rung) + 1e-9) * np.std(out)
    return out.astype(np.float32)


# ---- Thunder ----------------------------------------------------------------------------------------------------------

def thunder(rng, dist=2000.0, length=None, channel=4000.0):
    """Thunder from a lightning channel (Ribner and Roy): a tortuous line of segments, each a short N-wave sent out
    broadside, so a segment square to you is loud and one pointing at you is faint. Arrival times spread by the
    channel's length make the crack and the long roll; the air eats the top with distance."""
    c = 343.0
    nseg = 1600
    step = channel / nseg
    # a random walk up from the ground strike point, wandering sideways
    d = np.cumsum(rng.standard_normal((nseg, 3)) * [0.6, 0.6, 1.0] + [0, 0, 0.9], axis=0)
    d = d / np.linalg.norm(np.diff(d, axis=0, prepend=d[:1]), axis=1).mean() * step
    ang = rng.uniform(0, TAU)
    ear = np.array([dist * np.cos(ang), dist * np.sin(ang), -2.0])
    seg = np.diff(d, axis=0, prepend=np.zeros((1, 3)))
    los = d - ear
    r = np.linalg.norm(los, axis=1)
    cosang = np.abs(np.sum(seg * los, axis=1)) / (np.linalg.norm(seg, axis=1) * r + 1e-9)
    w = (1 - cosang ** 2) ** 3                                     # broadside beaming
    t = r / c
    t -= t.min()
    L = length or float(t.max() + 3.0)
    n = samples(L)
    x = np.zeros(n, np.float32)
    for ti, wi, ri in zip(t, w, r):
        dur = float(np.clip(0.004 * (ri / 500) ** 0.25 * rng.uniform(0.6, 1.6), 0.002, 0.02))
        k = samples(dur)
        nw = np.linspace(1, -1, k).astype(np.float32)            # an N-wave
        a = samples(ti + 0.05)
        if a + k < n:
            x[a:a + k] += nw * wi * 1000 / ri
    # air absorption: the further, the darker; then the ground and the hills roll it out
    cut = float(np.clip(9000 / (1 + dist / 400), 120, 9000))
    y = lp(x, cut, 2)
    y = y + 0.6 * lp(rng.standard_normal(n).astype(np.float32) * np.convolve(np.abs(y), np.ones(samples(0.05)) / samples(0.05), "same"), cut * 0.6)
    return dsp.room(y, "night", wet=0.45, rng=rng)


# ---- Big events -------------------------------------------------------------------------------------------------------

def boom(rng, length=1.6, f=34, T=0.02, crack=0.6):
    """An explosion's pressure wave heard close: a Friedlander pulse (an instant rise, the overpressure decaying, then the
    long shallow suction after it), the sub of its body dropping in pitch, and the crack of whatever broke."""
    n = samples(length)
    t = np.arange(n) / SR
    fr = (1 - t / T) * np.exp(-1.3 * t / T)
    fr = fr * np.exp(-t / 0.35)
    fr = lp(fr.astype(np.float32), 900, 2)
    sub = synth.thump(f * rng.uniform(0.9, 1.1), length * 0.8, drop=0.45)
    cr = hp(rng.standard_normal(n).astype(np.float32), 400) * np.exp(-t / 0.012).astype(np.float32)
    return mix(norm(fr) * 1.0, norm(sub) * 0.8, norm(cr) * crack)


# Real impacts by what the piece is made of (the packs' keys), for debris and crashes.
PIECES = {
    "iron": [f"kenney_impact-sounds:impactPlate_heavy_00{i}" for i in range(5)] + [
        f"kenney_impact-sounds:impactMetal_heavy_00{i}" for i in range(5)] + ["sfx_100_v2:metal_hit_01"],
    "scrap": [f"kenney_impact-sounds:impactMetal_light_00{i}" for i in range(5)] + [
        f"kenney_impact-sounds:impactMetal_medium_00{i}" for i in range(5)] + [f"sfx_100_v2:metal_0{i}" for i in (1, 2, 6)],
    "brick": [f"kenney_impact-sounds:impactMining_00{i}" for i in range(5)] + [f"sfx_100_v2:stones_0{i}" for i in (1, 2, 3)],
    "wood": [f"kenney_impact-sounds:impactWood_heavy_00{i}" for i in range(5)] + [
        f"kenney_impact-sounds:impactPlank_medium_00{i}" for i in range(5)] + ["sfx_100_v2:wood_hit_01", "sfx_100_v2:wood_hit_02"],
    "tin": [f"kenney_impact-sounds:impactTin_medium_00{i}" for i in range(5)],
    "glass": [f"sfx_100_v2:glass_0{i}" for i in range(1, 7)],
}


def piece(rng, kind, semis=(-6, 0), tau=None):
    """One real impact of a `kind` of piece (PIECES), pitched for its size."""
    keys = PIECES[kind]
    return rec(keys[rng.integers(len(keys))], semis=rng.uniform(*semis), tau=tau)


def debris(rng, length, rate, kinds, decay=1.0, semis=(-6, 0), bounce=0.5):
    """Things coming down after a blow: pieces of each kind ({kind: weight}) landing at a rate that dies away over
    `decay` s, some bouncing once more, quieter and sooner; small ones brighter than big ones."""
    names = list(kinds)
    w = np.array([kinds[k] for k in names], float)
    w /= w.sum()
    b = dsp.Bus(length + 1.5)
    t = 0.0
    while True:
        t += rng.exponential(1 / (rate * np.exp(-t / decay) + 0.3))
        if t >= length:
            break
        kind = names[rng.choice(len(names), p=w)]
        x = norm(piece(rng, kind, semis))
        g = rng.uniform(0.25, 1.0) * np.exp(-t / (decay * 2))
        b.at(t, x * g)
        if rng.random() < bounce:
            b.at(t + rng.uniform(0.08, 0.3), dsp.vari(x, rng.uniform(0.5, 2)) * g * rng.uniform(0.2, 0.5))
    return b.x


def spray(rng, length=0.6, n=40, lo=1200, hi=6000):
    """Ballast thrown: real stone cracks pitched about and a shower of small hard clicks."""
    b = dsp.Bus(length + 0.6)
    for _ in range(4):
        b.at(abs(rng.normal(0, length / 4)), norm(piece(rng, "brick", (-3, 4))) * rng.uniform(0.3, 0.8))
    cl = synth.skitter(length, n / length, rng, f=(lo, hi), q=(3, 8), legs=8)
    b.at(0, norm(cl) * env([(0, 1), (length, 0)], length)[:len(cl)] * 0.6)
    return b.x


def crash(rng, size=1.0, iron=1.0, wood=0.6, tin=0.0, ground=0.0, crumple=0.15):
    """Something heavy smashing into something (a car into the ground, a car into a car): the weight's deep blow, the
    car's sheet iron ringing, crumpling for `crumple` s as many sub-impacts and tearing, real slams and clangs pitched
    down for the mass, boards splintering, tin clattering, ballast thrown. The crash itself sits in the mids, where
    the ear reads what broke; the weight is under it, not instead of it."""
    L = 3.0 + size
    b = dsp.Bus(L)
    b.at(0, norm(knock(rng, 42 / size ** 0.3, 0.7)) * 0.55)
    for i in range(int(3 + 4 * crumple / 0.15)):
        at = abs(rng.normal(0, crumple / 2))
        b.at(at, norm(sheet(rng, f1=rng.uniform(45, 90) / size ** 0.3, fmax=6000, decay=rng.uniform(0.5, 1.1),
                            contact=rng.uniform(0.0003, 0.001))) * rng.uniform(0.25, 0.45) * iron)
    for i in range(5):
        b.at(abs(rng.normal(0, crumple / 2 + 0.02)), norm(piece(rng, "iron", (-9, -4), tau=0.3)) * rng.uniform(0.5, 0.9) * iron)
    tr = tear(crumple + 0.25, rng, env([(0, 700), (crumple, 350), (crumple + 0.25, 40)], crumple + 0.25), (150, 4500))
    b.at(0.01, norm(tr) * env([(0, 1), (crumple + 0.25, 0)], crumple + 0.25)[:len(tr)] * 0.6 * iron)
    if wood:
        sp = splinter(0.3 + crumple, rng, env([(0, 500), (0.3 + crumple, 80)], 0.3 + crumple))
        b.at(0.0, norm(sp) * env([(0, 1), (0.3 + crumple, 0)], 0.3 + crumple)[:len(sp)] * 0.8 * wood)
        for i in range(4):
            b.at(abs(rng.normal(0.03, crumple)), norm(piece(rng, "wood", (-4, 1))) * rng.uniform(0.5, 1.0) * wood)
        b.at(0.02, norm(rec("sfx_100_v2:misc_34", semis=-rng.uniform(1, 4))) * 0.6 * wood)
    if tin:
        for i in range(6):
            b.at(abs(rng.normal(0.02, crumple)), norm(piece(rng, "tin", (-6, 0))) * rng.uniform(0.4, 0.8) * tin)
    if ground:
        b.at(0.0, norm(spray(rng, 0.5 + crumple, 70)) * 0.8 * ground)
        b.at(0.0, norm(lp(rng.standard_normal(samples(0.8)).astype(np.float32), 300)
                       * env([(0, 1), (0.05, 0.5), (0.8, 0)], 0.8)) * 0.45 * ground)
    return b.x
