"""The sound-building kit: load sources, bend them, mix them, measure them, write takes and previews.

Everything is mono float32 at 48 kHz (the game's rate). Randomness always comes from the numpy Generator a recipe is
handed, seeded from the cue and take, so a rebuild is the same sound (tools/audio/build.py).

Pitch and time: `vari` is varispeed (pitch and length move together, like a tape), `shift` moves pitch alone and
`stretch` length alone (both through ffmpeg's rubberband), `smear` is a paulstretch for holding a moment out into a tone.
"""

import os
import subprocess
import tempfile
from fractions import Fraction

import numpy as np
import soundfile as sf
from scipy import signal

SR = 48000


# ---- Files --------------------------------------------------------------------------------------------------------------

def load(path, sr=SR):
    """Any audio file -> mono float32 at `sr`. Ogg/wav/flac through libsndfile, the rest (mp3) through ffmpeg."""
    try:
        x, r = sf.read(path, dtype="float32", always_2d=True)
        x = x.mean(axis=1)
    except Exception:
        out = subprocess.run(["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(sr), "-f", "f32le", "-"],
                             capture_output=True, check=True).stdout
        return np.frombuffer(out, dtype=np.float32).copy()
    if r != sr:
        x = resample(x, sr / r)
    return x.astype(np.float32)


def write_wav(path, x, sr=SR):
    """A take for the game: 16-bit PCM wav, clipped to full scale."""
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    sf.write(path, np.clip(x, -1, 1), sr, subtype="PCM_16")


def write_preview(path, x, sr=SR, kbps=96):
    """A preview for the checklist page: AAC in an MP4 container (the page's asset store takes video/mp4, and every browser
    plays an audio-only MP4 in an <audio>)."""
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as t:
        sf.write(t.name, np.clip(x, -1, 1), sr, subtype="PCM_16")
    try:
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", t.name, "-c:a", "aac", "-b:a", f"{kbps}k", "-movflags", "+faststart",
                        "-map_metadata", "-1", "-fflags", "+bitexact", "-flags:a", "+bitexact", path], check=True)
    finally:
        os.unlink(t.name)


def _ffmpeg_filter(x, filt, sr=SR):
    """Run x through an ffmpeg audio filter chain (rubberband and friends)."""
    p = subprocess.run(["ffmpeg", "-v", "error", "-f", "f32le", "-ar", str(sr), "-ac", "1", "-i", "-", "-af", filt,
                        "-f", "f32le", "-ar", str(sr), "-ac", "1", "-"], input=np.asarray(x, np.float32).tobytes(),
                       capture_output=True, check=True)
    return np.frombuffer(p.stdout, dtype=np.float32).copy()


# ---- Time and pitch -----------------------------------------------------------------------------------------------------

def resample(x, ratio):
    """Change the sample count by `ratio` (2 = twice as long)."""
    f = Fraction(ratio).limit_denominator(400)
    return signal.resample_poly(x, f.numerator, f.denominator).astype(np.float32)


def vari(x, semitones):
    """Varispeed: up an octave plays twice as fast, like a tape."""
    return resample(x, 2 ** (-semitones / 12))


def shift(x, semitones, formant=False):
    """Pitch without length (rubberband). `formant` keeps a voice's formants where they were."""
    if abs(semitones) < 1e-3:
        return x.copy()
    r = 2 ** (semitones / 12)
    return _ffmpeg_filter(x, f"rubberband=pitch={r:.6f}:formant={'preserved' if formant else 'shifted'}:transients=crisp")


def stretch(x, factor, smooth=False):
    """Length without pitch (rubberband); `smooth` for held tones, crisp for anything with attacks."""
    return _ffmpeg_filter(x, f"rubberband=tempo={1 / factor:.6f}:transients={'smooth' if smooth else 'crisp'}"
                             f":window={'long' if smooth else 'standard'}")


def smear(x, factor, window=0.25, rng=None):
    """Paulstretch: hold a sound out `factor` times longer by re-randomising its phases. Turns a cry into a held tone."""
    rng = rng or np.random.default_rng(0)
    w = int(window * SR) // 2 * 2
    win = np.hanning(w).astype(np.float32) ** 1.25
    hop_in, hop_out = w / 4 / factor, w / 4
    n_out = int(len(x) * factor) + w
    out = np.zeros(n_out, np.float32)
    xp = np.concatenate([x, np.zeros(w, np.float32)])
    pos, o = 0.0, 0
    while o + w < n_out and int(pos) + w <= len(xp):
        seg = xp[int(pos):int(pos) + w] * win
        S = np.abs(np.fft.rfft(seg))
        ph = rng.uniform(0, 2 * np.pi, len(S))
        out[o:o + w] += np.fft.irfft(S * np.exp(1j * ph)).astype(np.float32) * win
        pos += hop_in
        o += int(hop_out)
    return out[:int(len(x) * factor)]


def reverse(x):
    return x[::-1].copy()


# ---- Filters ------------------------------------------------------------------------------------------------------------

def _sos(kind, f, order=2):
    nyq = SR / 2
    if kind == "bp":
        lo, hi = f
        return signal.butter(order, [max(lo, 5) / nyq, min(hi, nyq * 0.98) / nyq], "bandpass", output="sos")
    return signal.butter(order, min(f, nyq * 0.98) / nyq, {"lp": "lowpass", "hp": "highpass"}[kind], output="sos")


def lp(x, f, order=2):
    return signal.sosfilt(_sos("lp", f, order), x).astype(np.float32)


def hp(x, f, order=2):
    return signal.sosfilt(_sos("hp", f, order), x).astype(np.float32)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos("bp", (lo, hi), order), x).astype(np.float32)


def band(x, lo, hi, order=4):
    """Hold a sound to its tell band (spec A.4): steep both sides, zero phase so attacks stay put."""
    sos = _sos("bp", (lo, hi), order)
    return signal.sosfiltfilt(sos, x).astype(np.float32)


def _biquad_peak(f, q, gain_db):
    A = 10 ** (gain_db / 40)
    w = 2 * np.pi * f / SR
    al = np.sin(w) / (2 * q)
    b = np.array([1 + al * A, -2 * np.cos(w), 1 - al * A])
    a = np.array([1 + al / A, -2 * np.cos(w), 1 - al / A])
    return b / a[0], a / a[0]


def peak(x, f, q, gain_db):
    """An RBJ peaking EQ: a resonance (a hollow head, a car's body) or a notch."""
    b, a = _biquad_peak(f, q, gain_db)
    return signal.lfilter(b, a, x).astype(np.float32)


def resonate(x, freqs, q=30, gains=None):
    """A bank of resonators: the body something rings through (porcelain, a bell, a boiler shell)."""
    out = np.zeros_like(x)
    for i, f in enumerate(freqs):
        if f >= SR / 2 * 0.95:
            continue
        g = 1.0 if gains is None else gains[i]
        w = 2 * np.pi * f / SR
        r = np.exp(-w / (2 * q))
        b, a = [1 - r], [1, -2 * r * np.cos(w), r * r]
        out += g * signal.lfilter(b, a, x).astype(np.float32)
    return out


def sweep_filter(x, kind, f_curve, q=0.707, block=256):
    """A filter whose frequency moves (a throat opening, a lowpass closing with distance). `f_curve` is per sample."""
    out = np.zeros_like(x)
    zi = np.zeros(2)
    for i in range(0, len(x), block):
        f = float(np.clip(f_curve[min(i, len(f_curve) - 1)], 20, SR * 0.45))
        w = 2 * np.pi * f / SR
        al = np.sin(w) / (2 * q)
        c = np.cos(w)
        if kind == "lp":
            b = np.array([(1 - c) / 2, 1 - c, (1 - c) / 2])
        elif kind == "hp":
            b = np.array([(1 + c) / 2, -(1 + c), (1 + c) / 2])
        else:  # band, constant peak gain
            b = np.array([al, 0, -al])
        a = np.array([1 + al, -2 * c, 1 - al])
        out[i:i + block], zi = signal.lfilter(b / a[0], a / a[0], x[i:i + block], zi=zi)
    return out


# ---- Envelopes, mixing --------------------------------------------------------------------------------------------------

def secs(n):
    return n / SR


def samples(t):
    return int(round(t * SR))


def silence(t):
    return np.zeros(samples(t), np.float32)


def env(points, length=None, curve="lin"):
    """[(t, value), ...] -> a per-sample curve. `curve="exp"` interpolates in dB-ish (for levels and frequencies)."""
    ts = np.array([p[0] for p in points], float)
    vs = np.array([p[1] for p in points], float)
    n = samples(length if length is not None else ts[-1])
    t = np.arange(n) / SR
    if curve == "exp":
        return np.exp(np.interp(t, ts, np.log(np.maximum(vs, 1e-6)))).astype(np.float32)
    return np.interp(t, ts, vs).astype(np.float32)


def fade(x, a=0.005, b=0.02):
    """Short fades so nothing clicks at its ends."""
    x = x.copy()
    na, nb = min(samples(a), len(x) // 2), min(samples(b), len(x) // 2)
    if na:
        x[:na] *= np.linspace(0, 1, na) ** 2
    if nb:
        x[-nb:] *= np.linspace(1, 0, nb) ** 2
    return x


def trim(x, start=0.0, length=None):
    a = samples(start)
    return x[a:a + samples(length)].copy() if length else x[a:].copy()


def trim_silence(x, db=-50, pad=0.01):
    """Cut the dead air off both ends."""
    if not len(x):
        return x
    thr = np.max(np.abs(x)) * 10 ** (db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    if not len(idx):
        return x
    a, b = max(0, idx[0] - samples(pad)), min(len(x), idx[-1] + samples(pad))
    return x[a:b].copy()


def fit(x, n):
    """Pad or cut to exactly n samples."""
    return x[:n] if len(x) >= n else np.concatenate([x, np.zeros(n - len(x), np.float32)])


def mix(*parts, length=None):
    """Sum signals of different lengths."""
    n = length if length is not None else max(len(p) for p in parts)
    out = np.zeros(n, np.float32)
    for p in parts:
        m = min(n, len(p))
        out[:m] += p[:m]
    return out


class Bus:
    """A timeline to place sounds on: bus.at(t, x, gain_db)."""

    def __init__(self, length):
        self.x = np.zeros(samples(length), np.float32)

    def at(self, t, x, db=0.0):
        a = samples(t)
        if a < 0:
            x, a = x[-a:], 0
        if a >= len(self.x):
            return self
        m = min(len(self.x) - a, len(x))
        self.x[a:a + m] += x[:m] * db2a(db)
        return self


def db2a(db):
    return 10 ** (db / 20)


def gain(x, db):
    return (x * db2a(db)).astype(np.float32)


def peak_norm(x, db=-1.0):
    m = np.max(np.abs(x)) if len(x) else 0
    return x if m == 0 else (x * (db2a(db) / m)).astype(np.float32)


# ---- Colour and grit ----------------------------------------------------------------------------------------------------

def saturate(x, drive_db=6.0):
    """Soft clip: warmth at low drive, a snarl at high."""
    g = db2a(drive_db)
    return (np.tanh(x * g) / np.tanh(g)).astype(np.float32)


def fold(x, amount=2.0):
    """Wavefolding: a harsh, metallic grit for throats that aren't throats."""
    y = x * amount
    return (np.abs(((y - 1) % 4) - 2) - 1).astype(np.float32) * -1


def ringmod(x, f, mix_=1.0):
    t = np.arange(len(x)) / SR
    m = np.sin(2 * np.pi * f * t) if np.isscalar(f) else np.sin(2 * np.pi * np.cumsum(f) / SR)
    return (x * (1 - mix_) + x * m * mix_).astype(np.float32)


def tremolo(x, rate, depth, rng=None, jitter=0.0):
    """Amplitude flutter; `jitter` makes the rate wander (an organic tremble, not an LFO)."""
    n = len(x)
    if jitter and rng is not None:
        wander = lp(rng.standard_normal(n).astype(np.float32), 2.0) * 20 * jitter
        ph = 2 * np.pi * np.cumsum(rate * (1 + wander)) / SR
    else:
        ph = 2 * np.pi * rate * np.arange(n) / SR
    return (x * (1 - depth * (0.5 + 0.5 * np.sin(ph)))).astype(np.float32)


def wow(x, depth_cents=20, rate=0.6, rng=None):
    """Slow pitch drift, like warped tape: a voice that can't hold its note."""
    n = len(x)
    rng = rng or np.random.default_rng(0)
    drift = lp(rng.standard_normal(n + SR).astype(np.float32), rate)[:n]
    drift = drift / (np.max(np.abs(drift)) + 1e-9) * depth_cents / 1200
    rate_curve = 2 ** drift
    pos = np.cumsum(rate_curve)
    pos = pos / pos[-1] * (n - 1)
    return np.interp(pos, np.arange(n), x).astype(np.float32)


def crush(x, bits=8, down=1):
    """Bit and rate reduction (a cheap, broken lamp's buzz)."""
    q = 2 ** (bits - 1)
    y = np.round(x * q) / q
    if down > 1:
        y = np.repeat(y[::down], down)[:len(x)]
    return y.astype(np.float32)


# ---- Space --------------------------------------------------------------------------------------------------------------

def ir(length=1.2, decay=0.6, damp=6000, rng=None, early=None, bright=0.0):
    """A synthetic impulse response: exponentially decaying noise, darker as it dies (air and walls soaking up the top)."""
    rng = rng or np.random.default_rng(7)
    n = samples(length)
    t = np.arange(n) / SR
    h = rng.standard_normal(n).astype(np.float32) * np.exp(-6.9 * t / decay).astype(np.float32)
    # darken over time: blend a lowpassed copy in as it decays
    dark = lp(h, damp, 2)
    k = np.clip(t / max(decay, 1e-3), 0, 1).astype(np.float32)
    h = h * (1 - k) * bright + dark * (1 - (1 - k) * bright)
    if early:
        for dt, g in early:
            i = samples(dt)
            if i < n:
                h[i] += g
    h[0] = 0
    return (h / (np.sqrt(np.sum(h ** 2)) + 1e-9)).astype(np.float32)


ROOMS = {
    # name: (length, RT60-ish decay, damping Hz, early reflections) — the places the game puts sounds
    "car": (0.5, 0.28, 5000, [(0.004, 0.5), (0.009, 0.35), (0.013, 0.25)]),       # inside a wooden car
    "cab": (0.35, 0.18, 6500, [(0.003, 0.6), (0.006, 0.4)]),                       # the engine's iron cab
    "tunnel": (3.5, 2.8, 3000, [(0.03, 0.4), (0.06, 0.3)]),
    "stone": (2.5, 1.9, 4000, [(0.02, 0.4), (0.045, 0.3)]),                        # a cold stone room (nest, mine)
    "hall": (2.2, 1.6, 5000, [(0.025, 0.3), (0.05, 0.25)]),
    "night": (2.8, 2.2, 2500, [(0.08, 0.15), (0.21, 0.1)]),                        # open country at night, far edges
    "box": (0.2, 0.08, 4000, [(0.002, 0.7), (0.004, 0.5)]),                        # a small hollow body
}


def room(x, name, wet=0.3, tail=True, rng=None):
    length, decay, damp, early = ROOMS[name]
    # str hashes change per process; a byte sum doesn't, so a room without an rng is the same room every build
    h = ir(length, decay, damp, rng=rng or np.random.default_rng(sum(name.encode()) * 7919), early=early)
    w = signal.fftconvolve(x, h).astype(np.float32)
    if not tail:
        w = w[:len(x)]
    return mix(x * (1 - wet) if len(w) else x, w * wet * 1.2)


def distance(x, metres, rng=None, space="night"):
    """Put a sound `metres` away: quieter (inverse distance from 1 m, softened), darker (air absorbs the top, ~ to 2 kHz at
    300 m), and more of it reverb."""
    m = max(metres, 1.0)
    cut = float(np.clip(18000 / (1 + m / 25), 1800, 18000))
    y = lp(x, cut, 2)
    wet = float(np.clip(0.15 + 0.6 * np.log10(m) / np.log10(300), 0.15, 0.8))
    y = room(y, space, wet=wet, rng=rng)
    return gain(y, -20 * np.log10(m) * 0.6)


def pan_free(x):
    """Mono stays mono: the game spatialises. Here for symmetry with recipes that once panned."""
    return x


# ---- Measuring ----------------------------------------------------------------------------------------------------------

def _k_weight(x):
    # ITU-R BS.1770 K-weighting at 48 kHz (high shelf + RLB highpass)
    b1, a1 = [1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585]
    b2, a2 = [1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621]
    return signal.lfilter(b2, a2, signal.lfilter(b1, a1, x))


def loudness(x):
    """Integrated loudness in LUFS (BS.1770, gated), so previews can be compared fairly."""
    y = _k_weight(np.asarray(x, float))
    blk, hop = samples(0.4), samples(0.1)
    if len(y) < blk:
        y = np.concatenate([y, np.zeros(blk - len(y))])
    ms = np.array([np.mean(y[i:i + blk] ** 2) for i in range(0, len(y) - blk + 1, hop)])
    lk = -0.691 + 10 * np.log10(ms + 1e-12)
    g = ms[lk > -70]
    if not len(g):
        return -70.0
    rel = -0.691 + 10 * np.log10(np.mean(g)) - 10
    g = ms[(lk > -70) & (lk > rel)]
    return float(-0.691 + 10 * np.log10(np.mean(g) + 1e-12))


def level(x, lufs=-20.0, ceiling=-1.0):
    """Set loudness, then keep the peak under the ceiling: only the peaks that cross a knee 3 dB under it are bent
    (a soft knee), so a spiky sound stays at its loudness instead of being pushed up into a saturator."""
    l = loudness(x)
    y = gain(x, lufs - l) if l > -70 else x
    return soft_limit(y, ceiling)


def soft_limit(x, ceiling=-1.0, knee_db=3.0):
    """Peaks over (ceiling - knee) are bent smoothly so none passes the ceiling; everything under the knee is untouched."""
    c = db2a(ceiling)
    t = db2a(ceiling - knee_db)
    a = np.abs(x)
    over = a > t
    if not np.any(over):
        return x.astype(np.float32)
    y = x.astype(np.float32).copy()
    y[over] = np.sign(x[over]) * (t + (c - t) * np.tanh((a[over] - t) / (c - t)))
    return y


def band_fraction(x, lo, hi):
    """How much of a sound's energy sits in [lo, hi] (the tell-band check the checklist shows)."""
    f, p = signal.welch(x, SR, nperseg=4096)
    tot = np.sum(p[(f >= 40)]) + 1e-20
    return float(np.sum(p[(f >= lo) & (f <= hi)]) / tot)


def centroid(x):
    f, p = signal.welch(x, SR, nperseg=4096)
    return float(np.sum(f * p) / (np.sum(p) + 1e-20))


def loop_seam(x, xfade=0.25):
    """Make a loop seamless: crossfade its tail into its head (equal power) and drop the tail."""
    n = samples(xfade)
    if len(x) <= 2 * n:
        return x
    a, b = x[:n], x[-n:]
    t = np.linspace(0, np.pi / 2, n)
    head = a * np.sin(t) + b * np.cos(t)
    return np.concatenate([head, x[n:-n]]).astype(np.float32)


def shaped(x, points, curve="lin"):
    """x times an envelope [(t, level), ...] that runs its whole length (held at the last point)."""
    e = env(points + [(max(points[-1][0], len(x) / SR) + 1e-3, points[-1][1])], len(x) / SR, curve)
    return (x * fit(e, len(x))).astype(np.float32)


def wrap(x, n):
    """Fold everything past n samples back onto the start: a loop's reverb tail rings on into its own head, so a loop of
    exactly one cycle stays one cycle long and seamless."""
    x = np.asarray(x, np.float32)
    out = fit(x[:n].copy(), n)
    for i in range(n, len(x), n):
        seg = x[i:i + n]
        out[:len(seg)] += seg
    return out


def compress(x, threshold_db=-18, ratio=3.0, attack=0.01, release=0.15, makeup=True):
    """A plain feed-forward compressor on the signal's envelope: evens out a performance without squashing its attacks."""
    a_att, a_rel = np.exp(-1 / (attack * SR)), np.exp(-1 / (release * SR))
    lvl = np.abs(x).astype(np.float64)
    e = np.empty_like(lvl)
    acc = 0.0
    for i, v in enumerate(lvl):
        acc = a_att * acc + (1 - a_att) * v if v > acc else a_rel * acc + (1 - a_rel) * v
        e[i] = acc
    db = 20 * np.log10(e + 1e-9)
    over = np.maximum(db - threshold_db, 0)
    g = 10 ** (-over * (1 - 1 / ratio) / 20)
    y = x * g
    if makeup:
        y = y * (np.max(np.abs(x)) / (np.max(np.abs(y)) + 1e-9))
    return y.astype(np.float32)
