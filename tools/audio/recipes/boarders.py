"""The things that take people off the train (GDD §21): the Whistler, the Car Hugger, Tippy Toesie, the Draggers, the
Passenger and the Track Doll taking its toy.

Each is built from what its body is made of:
- Whistler: a four-metre coil of ivory plates over grub-white flesh, twenty hooked legs, a fleshy siphon with flute stops.
  Its legs land on the ballast in waves (a rolling flam of hard tips on stone), its plates chatter dry, and its siphon is
  a breathy pipe that knows the train whistle's three notes (E, G#, B, the dome whistle it blows in its tell), an octave
  up, a smaller and wetter pipe: the chase follows it by those notes.
- Car Hugger: a ringed leech the size of a car. No voice (leeches don't have one): a blow sinks into wet rubber and
  sloshes, and it answers with its grip, on the guard van's own timbers. Its break-away is the van's real failure, iron
  and wood and air brakes, carrying the leech off with it.
- Tippy Toesie, the Draggers and the Passenger are mostly the victim's body: cloth, boots on boards and tin, a body
  sliding, with the creature's breath or sinews only where it's in contact.
- The Track Doll's toy-taking stays in the family it's been kept in: porcelain syllables rung through a hollow head,
  glaze ticks, a glass ding reversed through reverb for the vanish.

Loops are made exactly one cycle long and handed over with `cycle`, so a gait or a stride never stumbles at the seam.
"""

import functools

import numpy as np
from scipy import signal

import dsp
import src
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes.kit import hz, slap, squelch, snap, whoosh

SR = dsp.SR
SEAM = 0.3   # build.py's loop-seam crossfade


# ---- Shared parts ---------------------------------------------------------------------------------------------------------

def cycle(y):
    """Hand build.py one loop cycle so its equal-power seam crossfade gives the cycle back exactly: the head is repeated
    after the end and both copies are scaled by the inverse of the fade's sum (a crossfade of a sound with itself would
    otherwise swell 3 dB and drop 0.3 s out of the rhythm)."""
    n = samples(SEAM)
    t = np.linspace(0, np.pi / 2, n)
    head = (y[:n] / (np.sin(t) + np.cos(t))).astype(np.float32)
    return np.concatenate([head, y[n:], head])


def loop_bus(length, tail=3.0):
    """A bus for a loop: events may run past the end; `close` folds the overhang (and the room) back onto the head."""
    return Bus(length + tail)


def close(bus, length, space=None, wet=0.2, rng=None):
    y = bus.x
    if space:
        y = dsp.room(y, space, wet=wet, rng=rng)
    return cycle(dsp.wrap(y, samples(length)))


def rms(x):
    return float(np.sqrt(np.mean(np.square(x)) + 1e-12))


def norm(x, to=0.1):
    """Scale to an RMS, so parts can be balanced in dB against each other."""
    return (x * (to / rms(x))).astype(np.float32)


@functools.lru_cache(maxsize=256)
def _pitched(key, semis):
    x = src.get(key)
    return dsp.vari(x, semis) if semis else x


@functools.lru_cache(maxsize=256)
def _onsets(key, semis):
    """Where a recording's hits start (its envelope jumping), so grains cut from it land on an attack."""
    x = _pitched(key, semis)
    e = lp(np.abs(x), 120)
    d = np.diff(e, prepend=0)
    thr = np.max(d) * 0.15
    idx = np.where((d > thr) & (np.roll(d, 1) <= thr))[0]
    keep, last = [], -10 ** 9
    for i in idx:
        if i - last > samples(0.02):
            keep.append(i)
            last = i
    return np.array(keep or [0])


def grain(rng, key, length, semis=0.0, attack=True, decay=None):
    """A slice of a recording: from one of its attacks (a crunch, a knock) or anywhere (a texture), windowed."""
    x = _pitched(key, round(semis * 4) / 4)
    n = min(samples(length), len(x))
    if attack:
        on = _onsets(key, round(semis * 4) / 4)
        a = int(np.clip(on[rng.integers(len(on))] - samples(0.001), 0, len(x) - n))
        w = np.exp(-np.arange(n) / max(samples(decay or length / 3), 1)).astype(np.float32)
        w[:samples(0.001)] *= np.linspace(0, 1, samples(0.001))
    else:
        a = int(rng.integers(0, max(len(x) - n, 1)))
        w = np.hanning(n).astype(np.float32)
    return (x[a:a + n] * w).astype(np.float32)


def thud(rng, f=90, length=0.3, tone=0.3):
    """Weight landing on a body: mostly a dull bump of low noise (a sine alone rings like a cartoon), a little pitch drop."""
    n = synth.noise(length, rng)
    body = lp(lp(n, f * 2.5), f * 3) * env([(0, 1), (0.015, 0.7), (length, 0)], length, "exp")
    return mix(norm(body, 0.1), norm(synth.thump(f, length, drop=0.35), 0.1) * tone)


def bend(x, semis):
    """Varispeed along a curve (semitones per sample): a pitch that sags as a thing slows or falls behind."""
    rate = 2 ** (dsp.fit(np.asarray(semis, np.float32), len(x)) / 12)
    pos = np.cumsum(rate)
    pos = pos[pos < len(x) - 1]
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


def recede(x, rng, near, far, space="night", drop=0.0, curve=2.0):
    """Pull a sound away over its length, `near` to `far` metres: darker, quieter and wetter as it goes (as
    dsp.distance), its pitch sagging `drop` semitones. `curve` 2 is something falling behind a moving train: slow to open
    the gap, then quicker and quicker."""
    if drop:
        x = bend(x, -drop * np.linspace(0, 1, len(x)) ** 0.7)
    n = len(x)
    d = (near + (far - near) * (np.arange(n) / n) ** curve).astype(np.float32)
    y = dsp.sweep_filter(x, "lp", np.clip(18000 / (1 + d / 25), 900, 18000), q=0.6)
    g = d ** -0.6
    wet = np.clip(0.15 + 0.6 * np.log10(d) / np.log10(300), 0.15, 0.85)
    r = dsp.room(y, space, wet=1.0, rng=rng)
    out = r.copy() * g[-1] * wet[-1]
    out[:n] = y * g * (1 - wet) + r[:n] * g * wet
    return out.astype(np.float32)


def drift(rng, n, rate, depth):
    """A slow random wander, unit-scaled then set to `depth` (pitch drift, a breath's unevenness)."""
    w = lp(rng.standard_normal(n + SR).astype(np.float32), rate)[SR:]
    return (w / (np.std(w) + 1e-9) * depth).astype(np.float32)


def tame(x, pct=99.9):
    """Round off the few sharpest peaks (a crack's first millisecond), so the body of a long sound, not one spike, sets
    how loud it can be levelled."""
    ref = float(np.percentile(np.abs(x), pct)) + 1e-9
    return (np.tanh(x / ref) * ref).astype(np.float32)


def creak(length, rate, rng, **kw):
    """synth.creak with its DC taken out (its slips are one-signed pulses through resonators, which leaves an offset)."""
    return hp(synth.creak(length, rate, rng, **kw), 30)


def pad(x, before):
    return np.concatenate([np.zeros(samples(before), np.float32), x])


STONES = src.S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02")
CLOTH = src.R("cloth1", "cloth2", "cloth3", "cloth4")


def ballast(rng, weight=1.0, size=0.03):
    """A hard tip or a heel landing on ballast: a crunch cut from stones settling, with a little weight under it."""
    g = grain(rng, STONES[rng.integers(len(STONES))], size * rng.uniform(0.7, 1.4), rng.uniform(-5, 3))
    return mix(norm(g, 0.1), thud(rng, 120, 0.06, 0.1) * 0.5 * weight)


# ---- Whistler --------------------------------------------------------------------------------------------------------------

# The train whistle's chord (tell-whistler: E, G#, B), an octave up: the siphon is a smaller pipe that learned it.
NOTES = [hz(76), hz(80), hz(83)]


def steps(points, length, glide=0.03):
    """A stepped pitch curve [(t, Hz), ...] with a short glide between stops (a lid of flesh closing a hole)."""
    n = samples(length)
    tgt = np.zeros(n, np.float32)
    for i, (t, f) in enumerate(points):
        e = samples(points[i + 1][0]) if i + 1 < len(points) else n
        tgt[samples(t):e] = f
    k = 1 - np.exp(-1 / (glide * SR))
    y = signal.lfilter([k], [1, k - 1], tgt - tgt[0]) + tgt[0]
    return y.astype(np.float32)


def siphon(rng, f0, pressure, length, voiced=1.0, flutter=0.35, wet=0.4, q=22):
    """The Whistler's siphon: breath through a fleshy pipe. Air rings at the pipe's note (noise through a narrow band at
    the note and its octave); above a pressure the pipe speaks (a flute-like jet); its lips flutter; fluid gurgles in it."""
    n = samples(length)
    f = synth.curve(f0, n) * 2 ** (drift(rng, n, 5, 18) / 1200)
    p = np.clip(synth.curve(pressure, n), 0, 1.5)
    ph = np.cumsum(f) / SR
    jet = np.sin(2 * np.pi * ph) + 0.22 * np.sin(4 * np.pi * ph + 0.5) + 0.08 * np.sin(6 * np.pi * ph)
    speak = (np.clip((p - 0.35) / 0.5, 0, 1) ** 1.5) * voiced
    nz = rng.standard_normal(n).astype(np.float32)
    turb = np.clip(1 + drift(rng, n, 25, 0.25), 0.2, 2.5)
    pipe = norm(dsp.sweep_filter(nz, "bp", f, q=q, block=128), 0.1) + norm(dsp.sweep_filter(nz, "bp", f * 2, q=q * 0.6, block=128), 0.04)
    edge = norm(bp(nz, 1800, 7000), 0.025)
    y = norm(jet, 0.1) * speak * 1.2 + (pipe + edge) * turb * 0.9
    y = dsp.tremolo(y, rng.uniform(17, 26), flutter, rng, jitter=0.4)
    y = y * p
    if wet:
        gurgle = synth.bubbles(length, np.clip(p * 30, 1, None), 250, 1400, rng, rise=(0.2, 0.9))
        y = y + norm(gurgle, 0.03) * wet * p
    return lp(y, 6000).astype(np.float32)


def breath_env(t0, length, rise=0.12, fall=0.35, peak=1.0, total=None):
    """One push of air: up fast, a short hold, out slower."""
    return [(0, 0), (t0, 0), (t0 + rise, peak), (t0 + length - fall, peak * 0.8), (t0 + length, 0)] + \
        ([(total, 0)] if total else [])


def tick(rng, f=1100, wet=0.5):
    """A stop closing: a moist tick, a lid of flesh meeting the pipe."""
    c = synth.click(f * rng.uniform(0.85, 1.15), q=4, length=0.025, rng=rng)
    return mix(norm(c, 0.1), norm(synth.bubbles(0.04, 120, 600, 1800, rng), 0.05) * wet)


IVORY = [1.0, 1.58, 2.21, 2.95, 3.7]   # a curved plate of bone: its modes over the first


def plate_click(rng, f=None, q=None):
    """One ivory plate touching the next: a dry, damped tick of bone."""
    f = f or np.exp(rng.uniform(np.log(1700), np.log(3600)))
    return synth.click(f, q=q or rng.uniform(8, 16), length=0.018, rng=rng, body=[f * m for m in IVORY[:3]])


def leg_wave(rng, legs=10, span=0.2, weight=1.0):
    """One wave of legs down a side: tips landing front to back in a rolling flam, heaviest mid-body where the victim
    hangs."""
    out = Bus(span + 0.2)
    for k in range(legs):
        u = k / (legs - 1)
        t = span * u ** rng.uniform(0.85, 1.15) + rng.normal(0, 0.003)
        a = (0.45 + 0.55 * np.sin(np.pi * u) ** 0.7) * rng.uniform(0.6, 1.0) * weight
        out.at(max(t, 0), ballast(rng, a, 0.025), 20 * np.log10(a))
        if rng.random() < 0.5:
            out.at(max(t + rng.uniform(0, 0.004), 0), norm(plate_click(rng, rng.uniform(2500, 4500)), 0.05), 20 * np.log10(a) - 4)
    return out.x


def chatter(rng, length, rate, f=(1700, 3600)):
    """The plates chattering as the coil flexes: dry clicks, bunched where the body bends."""
    out = np.zeros(samples(length), np.float32)
    t = 0.0
    while t < length:
        t += rng.exponential(1 / rate)
        if t >= length:
            break
        burst = rng.integers(1, 4)
        for b in range(burst):
            c = plate_click(rng, np.exp(rng.uniform(np.log(f[0]), np.log(f[1]))))
            a = samples(t + b * rng.uniform(0.008, 0.02))
            m = min(len(c), len(out) - a)
            if m > 0:
                out[a:a + m] += c[:m] * rng.uniform(0.3, 1.0)
    return out


def victim_drag(rng, length):
    """The victim's heels dragging through the ballast for a stretch: a run of small crunches, and their clothes."""
    out = Bus(length + 0.2)
    t = 0.0
    while t < length:
        u = t / length
        out.at(t, ballast(rng, 0.3, 0.02), -6 + 6 * np.sin(np.pi * u))
        t += rng.uniform(0.008, 0.03)
    cl = grain(rng, CLOTH[rng.integers(len(CLOTH))], 0.4, rng.uniform(-6, -2), attack=False)
    out.at(length * 0.2, norm(cl, 0.05), -6)
    return lp(out.x, 5000)


def run_gait(rng, L, waves, weight=1.0):
    """The run's legs: `waves` waves of legs per loop, sides alternating, the spacing breathing a little (and coming back
    round so the loop holds)."""
    bus = loop_bus(L)
    period = L / waves
    wob = rng.uniform(0, 2 * np.pi)
    for i in range(waves):
        t = i * period + 0.012 * np.sin(2 * np.pi * i / waves * 3 + wob) + rng.normal(0, 0.004)
        side = i % 2
        bus.at(t % L, leg_wave(rng, 10, rng.uniform(0.16, 0.22) * (1.08 if side else 1.0), weight),
               rng.uniform(-1.5, 0) - 1.5 * side)
    return bus


@recipe("cs-whistler", "run", "waves",
        "Twenty legs on the ballast in rolling waves, plates chattering, the victim's heels dragging, the siphon panting",
        """Each wave of legs is ten hard tips landing front to back in a rolling flam (crunches cut from stones settling,
        a bone tick on half of them), the two sides alternating about two and a half times a second, so it rolls like
        no animal's gait. Over it the ivory plates chatter dry (the snatch's clicking chatter, kept), the victim's heels
        plough through the ballast now and then with their clothes, and the siphon pants in time: breath rung through a
        fleshy pipe at the train whistle's notes an octave up, which twice a loop speaks a half-note of whistle. Loops in
        one exact cycle.""",
        sources=STONES + CLOTH, loop=True, takes=1, seconds=12.0)
def whistler_run_waves(rng, take):
    L = 12.0
    bus = run_gait(rng, L, 30)
    bus.at(0, chatter(rng, L, 9), -13)
    for t0, d in [(1.3, 0.8), (5.9, 1.1), (9.4, 0.6)]:
        bus.at(t0, victim_drag(rng, d), -5)
    # the siphon pants every second wave; twice a loop the push is hard enough to speak a note
    f0, press, t, k = [], [(0, 0)], 0.0, 0
    period = L / 15
    for i in range(15):
        t0 = i * period + rng.uniform(0.0, 0.05)
        hard = i in (4, 11)
        press += [(t0, 0), (t0 + 0.1, 0.95 if hard else rng.uniform(0.35, 0.5)), (t0 + period * 0.55, 0.25), (t0 + period * 0.8, 0)]
        f0.append((t0, NOTES[(i * 2) % 3] if hard else NOTES[0] * rng.uniform(0.97, 1.03)))
    press.append((L, 0))
    s = siphon(rng, steps(f0, L), env(press, L), L, voiced=0.8, flutter=0.4)
    bus.at(0, norm(s, 0.1), -15)
    return close(bus, L, "night", 0.12, rng)


def inhale(rng, length=0.5):
    """The siphon drawing air back in: breath through the pipe, reversed so it swells and sucks shut, wet."""
    s = siphon(rng, NOTES[0] * rng.uniform(0.45, 0.55), env([(0, 0), (0.05, 0.6), (length, 0)], length), length,
               voiced=0, wet=1.0, flutter=0.5)
    return dsp.reverse(s)


def phrase(rng, t0, notes):
    """A run of the whistle's notes, each its own push of breath; the last sags as the push runs out."""
    f0, press, t = [], [], t0
    for i, (k, d) in enumerate(notes):
        f = NOTES[k] * rng.uniform(0.985, 1.01)
        f0.append((t, f))
        last = i == len(notes) - 1
        press += [(t, 0.15), (t + 0.04, rng.uniform(0.85, 1.1)), (t + d * 0.7, 0.8), (t + d, 0.2 if not last else 0)]
        if last:
            f0.append((t + d * 0.45, f * 2 ** (-rng.uniform(1.0, 2.5) / 12)))
        t += d
    return f0, press, t


@recipe("cs-whistler", "run", "piper",
        "Its siphon whistling broken bits of the train whistle's chord as it runs, the legs rolling under it",
        """The siphon carries this one: every few seconds it whistles two to four of the train whistle's notes (E, G#,
        B, an octave up), each its own push of breath through a fleshy pipe: air rung at the note, a jet that only speaks
        when the push is hard, lips fluttering, fluid gurgling, a wet tick as each stop closes, the last note sagging as
        the breath runs out. Between phrases it sucks air back in. Under it the same rolling waves of legs on ballast,
        lighter, so the chase follows the whistle.""",
        sources=STONES, loop=True, takes=1, seconds=12.0)
def whistler_run_piper(rng, take):
    L = 12.0
    bus = run_gait(rng, L, 30, 0.8)
    bus.at(0, chatter(rng, L, 6), -16)
    f0, press = [(0, NOTES[0])], [(0, 0)]
    for t0, notes in [(0.6, [(0, 0.32), (1, 0.22), (2, 0.55)]), (3.7, [(2, 0.3), (1, 0.6)]),
                      (6.2, [(0, 0.2), (0, 0.2), (2, 0.25), (1, 0.7)]), (9.5, [(1, 0.35), (0, 0.8)])]:
        f, p, t1 = phrase(rng, t0, notes)
        f0 += f
        press += [(t0 - 0.01, 0)] + p + [(t1 + 0.02, 0)]
        for t, _ in f[1:]:
            bus.at(t - 0.01, tick(rng), -24)
        bus.at(t1 + rng.uniform(0.3, 0.6), inhale(rng, rng.uniform(0.4, 0.6)), -16)
    press.append((L, 0))
    s = siphon(rng, steps(f0, L, 0.025), env(press, L), L, voiced=1.0, flutter=0.3, wet=0.3)
    bus.at(0, norm(s, 0.1), -6)
    return close(bus, L, "night", 0.15, rng)


def bone(rng, f1, length=0.3, q=None):
    """A struck plate of ivory: a short burst rung through the plate's modes, damped the way bone is."""
    x = np.zeros(samples(length), np.float32)
    k = samples(0.0015)
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    q = q or rng.uniform(10, 16)
    y = dsp.resonate(x, [f1 * m for m in IVORY], q=q, gains=[1, 0.7, 0.5, 0.35, 0.25])
    return (y * np.exp(-np.arange(len(y)) / samples(length / 5))).astype(np.float32)


def scrabble_legs(rng, length, rate=70, weight=0.6):
    """Legs flinching: hooked tips scrabbling on stone, quickening then easing."""
    out = Bus(length + 0.1)
    t = 0.0
    while t < length:
        u = t / length
        a = weight * (1 - u) ** 0.7 * rng.uniform(0.4, 1)
        out.at(t, ballast(rng, a, 0.02), 20 * np.log10(a + 1e-3))
        if rng.random() < 0.4:
            out.at(t, norm(plate_click(rng), 0.05), 20 * np.log10(a + 1e-3) - 3)
        t += rng.exponential(1 / rate)
    return out.x


WOODHIT = src.S("wood_hit_03", "misc_08")
CRACKS = src.S("misc_33", "misc_34", "misc_35")
WETS = src.R("handleCoins")


def squeal(rng, length, start, top, end, wet=0.6):
    """The siphon's pained squeal: a push of air that overblows the pipe upward, then sags as it runs out. Both sides of
    the siphon sound a few cents apart, so it beats and rasps like a reed rather than singing like a whistle."""
    f = env([(0, start), (length * 0.12, top), (length * 0.45, top * rng.uniform(0.9, 0.97)), (length, end)], length, "exp")
    p = env([(0, 0), (0.025, 1.25), (length * 0.4, 0.9), (length, 0)], length)
    a = siphon(rng, f, p, length, voiced=0.8, flutter=0.55, wet=wet)
    b = siphon(rng, f * 2 ** (rng.uniform(0.35, 0.6) / 12), p, length, voiced=0.8, flutter=0.55, wet=wet)
    return dsp.saturate(norm(a + 0.8 * b, 0.25), 6)


@recipe("cs-whistler", "hit", "plate",
        "The blow knocks on an ivory plate, the flesh gives under it, its legs flinch and its siphon squeals",
        """A short burst rung through a curved bone plate's modes (dry and damped, a different plate each take) over a
        bright wood knock, with the grub flesh giving under it (a dull low thud and a wet squelch); one take the plate
        cracks. The legs scrabble on the stones as it flinches, and the siphon squeals: a hard push of breath that
        overblows the pipe up past its notes and sags as the air runs out, its lips fluttering, wet in the pipe.""",
        sources=WOODHIT + CRACKS + STONES, takes=4)
def whistler_hit_plate(rng, take):
    b = Bus(1.3)
    b.at(0, norm(bone(rng, rng.uniform(1100, 1700)), 0.1), 0)
    b.at(0, norm(grain(rng, WOODHIT[take % 2], 0.2, rng.uniform(1, 5)), 0.1), -4)
    b.at(0.004, thud(rng, rng.uniform(75, 95), 0.3, 0.15), -3)
    b.at(0.01, norm(squelch(rng, 0.25, 280, 1300), 0.1), -12)
    if take == 2:
        b.at(0.002, norm(snap(rng, 0.09, 2400), 0.1), -3)
        b.at(0.01, norm(grain(rng, CRACKS[1], 0.25, -2), 0.1), -6)
    b.at(rng.uniform(0.03, 0.06), scrabble_legs(rng, rng.uniform(0.2, 0.32)), -6)
    L = rng.uniform(0.35, 0.55)
    top = NOTES[2] * 2 ** (rng.uniform(2, 6) / 12)
    b.at(rng.uniform(0.05, 0.1), norm(squeal(rng, L, NOTES[1], top, NOTES[0] * 2 ** (-rng.uniform(3, 7) / 12)), 0.1), -5)
    return b.x


def sputter(rng, length):
    """Air forced out of the siphon in a spasm: breath through the pipe that never quite speaks, fluid spraying in it."""
    p = env([(0, 0), (0.015, 1.3), (length * 0.3, 0.6), (length, 0)], length)
    p = p * np.clip(1 + drift(rng, len(p), 14, 0.5), 0.1, 2)
    return siphon(rng, NOTES[0] * rng.uniform(0.5, 0.7), p, length, voiced=0, flutter=0.7, wet=1.5, q=4)


@recipe("cs-whistler", "hit", "flesh",
        "The blow sinks between the plates into wet flesh; the plates rattle and air spits out of the siphon",
        """A duller, wetter blow: a slap and a low thud into grub flesh, a squelch and a chewing squish (a coin-handling
        squish pitched down) where it gives. The struck plates rattle against each other (a quick dry clatter of bone
        ticks), the legs scrabble on the stones, and the siphon spits a wet sputter of air that never becomes a note.
        No voice and no whistle: the body takes it.""",
        sources=WETS + STONES, takes=4)
def whistler_hit_flesh(rng, take):
    b = Bus(1.3)
    b.at(0, norm(slap(rng, 0.12, 1600, 0.9), 0.1), -3)
    b.at(0, thud(rng, rng.uniform(65, 85), 0.35, 0.15), 0)
    b.at(0.008, norm(squelch(rng, 0.32, 250, 1100), 0.1), -6)
    b.at(0.02, norm(grain(rng, WETS[0], 0.3, rng.uniform(-9, -5), attack=False), 0.1), -10)
    rattle = Bus(0.3)
    for i in range(rng.integers(7, 13)):
        rattle.at(0.012 * i + rng.uniform(0, 0.01), plate_click(rng, rng.uniform(1500, 3200)), -2 * i)
    b.at(0.01, norm(rattle.x, 0.1), -8)
    b.at(rng.uniform(0.04, 0.08), scrabble_legs(rng, rng.uniform(0.25, 0.4), 80, 0.8), -6)
    b.at(rng.uniform(0.07, 0.12), norm(sputter(rng, rng.uniform(0.3, 0.5)), 0.1), -8)
    return b.x


def collapse(rng, n=6, gap=(0.08, 0.16)):
    """A long body coming down on the stones: its segments landing one after another along its length."""
    out = Bus(n * gap[1] + 0.6)
    t = 0.0
    for i in range(n):
        a = 1 - 0.1 * i
        out.at(t, thud(rng, rng.uniform(70, 110), 0.3, 0.1), 20 * np.log10(a))
        for _ in range(3):
            out.at(t + rng.uniform(0, 0.03), ballast(rng, 0.6, 0.04), 20 * np.log10(a) - 4)
        t += rng.uniform(*gap)
    return out.x


def curl(rng, length, start_rate=50, weight=0.5):
    """Twenty legs folding as it dies: their ticks slowing like something winding down."""
    out = Bus(length + 0.1)
    t = 0.0
    while t < length:
        u = t / length
        out.at(t, norm(plate_click(rng, rng.uniform(2200, 4200)), 0.05), -3 - 10 * u + rng.uniform(-4, 0))
        if rng.random() < 0.3:
            out.at(t, ballast(rng, weight, 0.015), -8 - 8 * u)
        t += rng.exponential(1 / (start_rate * (1 - u) ** 2 + 2))
    return out.x


@recipe("cs-whistler", "death", "last-breath",
        "The killing blow cracks a plate; its siphon lets out a last whistle that sags into a wet rattle as it curls",
        """The blow cracks an ivory plate (a bone crack and wood splitting, into flesh). Then the siphon's last breath:
        one of the train whistle's notes held on a push that loses pressure, the stops fluttering between two notes
        faster and faster as it loses control of them, sagging in pitch and thinning to breath, ending in a gurgle of
        fluid in the pipe. Under it twenty legs curl, their ticks slowing like something winding down, and the coil
        comes down on the ballast segment by segment along its four metres.""",
        sources=CRACKS + STONES, takes=3)
def whistler_death_breath(rng, take):
    L = rng.uniform(4.2, 5.2)
    b = Bus(L + 1)
    b.at(0, norm(bone(rng, rng.uniform(900, 1300), 0.4), 0.1), 0)
    b.at(0, norm(snap(rng, 0.1, rng.uniform(1800, 2600)), 0.1), -2)
    b.at(0.005, norm(grain(rng, CRACKS[take], 0.4, -3), 0.1), -4)
    b.at(0.006, thud(rng, 70, 0.4, 0.15), -1)
    b.at(0.02, norm(squelch(rng, 0.3, 260, 1200), 0.1), -10)
    # the last whistle: a held note whose stops flutter faster as control goes, sagging into breath
    W = rng.uniform(2.6, 3.2)
    k = rng.integers(3)
    pts, t, d = [], 0.0, 0.5
    while t < W * 0.8:
        pts.append((t, NOTES[k] if len(pts) % 2 == 0 else NOTES[(k + 1) % 3]))
        t += d
        d = max(d * 0.72, 0.06)
    f = steps(pts, W, 0.015) * 2 ** (env([(0, 0), (W * 0.4, -0.5), (W, -rng.uniform(4, 7))], W) / 12)
    p = env([(0, 0), (0.06, 1.1), (W * 0.3, 0.85), (W * 0.75, 0.4), (W, 0)], W) * np.clip(1 + drift(rng, samples(W), 6, 0.15), 0.3, 2)
    b.at(0.12, norm(siphon(rng, f, p, W, voiced=1.0, flutter=0.45, wet=0.5), 0.1), -2)
    b.at(0.12 + W * 0.85, norm(sputter(rng, 0.6), 0.1), -14)
    b.at(0.25, curl(rng, L - 0.6), -9)
    b.at(rng.uniform(0.9, 1.4), collapse(rng, rng.integers(5, 8)), -4)
    return dsp.room(dsp.compress(b.x, -24, 4, 0.002, 0.15), "night", wet=0.1, rng=rng)


def grind(rng, length, rate=60):
    """Plates grinding over each other as the coil wrings: stick-slip through bone."""
    f1 = rng.uniform(600, 900)
    body = [f1 * m for m in IVORY] + [f1 * 4.6]
    r = env([(0, rate * 0.5), (length * 0.3, rate), (length, rate * 0.4)], length)
    c = creak(length, r, rng, body=body, q=9, jitter=0.5, grit=0.6)
    return c * env([(0, 0), (0.05, 1), (length * 0.7, 0.7), (length, 0)], length)


@recipe("cs-whistler", "death", "coil",
        "No whistle: the coil thrashes, plates grinding and legs clattering, then it slumps and breathes out its siphon",
        """Its death in its body rather than its pipe. The blow cracks into it (bone crack, wood splitting, a wet thud);
        the coil wrings in two or three spasms, the ivory plates grinding over each other (stick-slip through a plate's
        modes) with the legs clattering on the stones; it slumps down segment by segment, and its last breath goes out
        through the siphon as a long wet exhale that only just catches a note, then a few slow ticks of legs.""",
        sources=CRACKS + STONES, takes=3)
def whistler_death_coil(rng, take):
    L = rng.uniform(4.5, 5.5)
    b = Bus(L + 1)
    b.at(0, norm(bone(rng, rng.uniform(900, 1300), 0.4), 0.1), 0)
    b.at(0, norm(grain(rng, CRACKS[(take + 1) % 3], 0.4, -4), 0.1), -3)
    b.at(0.005, thud(rng, 70, 0.4, 0.15), -1)
    b.at(0.02, norm(squelch(rng, 0.35, 240, 1100), 0.1), -8)
    t = rng.uniform(0.15, 0.3)
    for i in range(rng.integers(2, 4)):
        d = rng.uniform(0.35, 0.6) * (1 - 0.2 * i)
        b.at(t, norm(grind(rng, d, rng.uniform(45, 80)), 0.1), -7 - 3 * i)
        b.at(t + 0.02, scrabble_legs(rng, d, 90, 0.8 - 0.2 * i), -5 - 3 * i)
        b.at(t + d * 0.3, norm(sputter(rng, 0.25), 0.1), -15 - 2 * i)
        t += d + rng.uniform(0.1, 0.25)
    b.at(t, collapse(rng, rng.integers(5, 8), (0.07, 0.13)), -4)
    W = rng.uniform(1.4, 1.9)
    p = env([(0, 0), (0.25, 0.5), (W * 0.5, 0.45), (W, 0)], W)
    b.at(t + 0.6, norm(siphon(rng, NOTES[0] * 2 ** (-rng.uniform(2, 5) / 12), p, W, voiced=0.25, flutter=0.5, wet=1.0, q=9), 0.1), -9)
    b.at(t + 0.9, curl(rng, L - t - 0.9, 8, 0.3), -12)
    return dsp.room(dsp.compress(b.x, -24, 4, 0.002, 0.15), "night", wet=0.1, rng=rng)


# ---- Car Hugger --------------------------------------------------------------------------------------------------------------

SQUISH = src.R("handleCoins")                      # the chewing squish the kept swallow and grind are made of
WATER = src.S("loop_water_01")
CREAKS = src.R("doorOpen_1", "doorOpen_2")
IRONHIT = src.S("metal_hit_01") + src.K("impactMetal_heavy", [0, 1, 3]) + src.K("impactPlate_heavy", [0, 2, 4])
SPLINTER = src.S("misc_34", "misc_35")
TIMBER = src.S("wood_hit_01", "wood_hit_02")


def hide(rng, size=1.0):
    """A blow sinking into a leech the size of a car: a dense low whump (noise, never a ringing sine), a slap on slime,
    the rubbery hide squishing round the blade."""
    L = 0.45
    whump = lp(lp(synth.noise(L, rng), 170 * size), 220 * size) * env([(0, 0), (0.004, 1), (0.03, 0.6), (L, 0)], L, "exp")
    y = mix(norm(whump, 0.1), norm(slap(rng, 0.14, 1200, 0.9), 0.1) * 0.7)
    y = mix(y, norm(grain(rng, SQUISH[0], 0.45, rng.uniform(-14, -11), attack=False), 0.1) * 0.6,
            norm(squelch(rng, 0.4, 150, 700), 0.1) * 0.5)
    return dsp.saturate(norm(y, 0.12), 5)


def slosh(rng, length):
    """The fluid it's full of slopping inside it: slow low bubbles and dark water, fading."""
    b = synth.bubbles(length, env([(0, 70), (length, 4)], length), 55, 260, rng, rise=(0.1, 0.5))
    w = lp(grain(rng, WATER[0], length, -12, attack=False), 700)
    y = mix(norm(b, 0.1), norm(w, 0.1) * 0.6)
    return y * env([(0, 0), (0.03, 1), (length, 0)], length, "exp")


def van_groan(rng, length, peak=55):
    """The guard van's timbers taking the strain as the rings clench on it: big boards slipping against each other
    (stick-slip through a heavy timber's modes, quickening to a groan) and a real door creak dragged down an octave."""
    r = env([(0, 12), (length * 0.55, peak), (length, 10)], length)
    c = creak(length, r, rng, body=[m * rng.uniform(0.5, 0.6) for m in synth.WOOD], q=16, jitter=0.35, grit=0.4)
    rec = lp(dsp.vari(src.get(CREAKS[rng.integers(2)]), -12), 1800)
    rec = dsp.fit(dsp.stretch(rec, length / (len(rec) / SR), smooth=True), samples(length))
    y = mix(norm(c, 0.1), norm(rec, 0.1) * 0.7)
    return y * env([(0, 0), (length * 0.15, 1), (length * 0.7, 0.9), (length, 0)], length)


@recipe("cs-car-hugger", "hit", "clench",
        "The blow sinks into wet rubbery hide and slops inside it; it clenches, and the guard van's timbers groan",
        """No voice: a leech hasn't got one. The blow lands as a dense low whump (filtered noise, not a ringing punch), a
        slap on slime and the chewing squish of its swallow two octaves down as the hide gives; the fluid it's full of
        slops inside it. Then it answers by clenching on the car: the van's own timbers groan under the rings (boards
        slipping against each other, quickening, and a door creak dragged down an octave), with an iron knock from the
        drawgear or a board popping.""",
        sources=SQUISH + WATER + CREAKS + IRONHIT + SPLINTER, takes=4)
def hugger_hit_clench(rng, take):
    b = Bus(2.8)
    b.at(0, hide(rng), 0)
    b.at(0.03, slosh(rng, rng.uniform(0.6, 0.9)), -8)
    t = rng.uniform(0.22, 0.38)
    g = rng.uniform(0.9, 1.6)
    b.at(t, van_groan(rng, g, rng.uniform(40, 70)), -5)
    if take % 2:
        b.at(t + g * rng.uniform(0.4, 0.6), norm(lp(grain(rng, IRONHIT[rng.integers(1, 4)], 0.3, -8), 2500), 0.1), -9)
    else:
        b.at(t + g * rng.uniform(0.3, 0.6), norm(grain(rng, SPLINTER[rng.integers(2)], 0.2, -5), 0.1), -10)
    return dsp.room(b.x, "car", wet=0.15, rng=rng)


def ripple(rng, length):
    """Its rings shuddering after the blow: wet flesh fluttering, dying away."""
    y = squelch(rng, length, 140, 520, 1.5)
    y = dsp.tremolo(y, rng.uniform(9, 14), 0.85, rng, jitter=0.3)
    return y * env([(0, 1), (length, 0)], length, "exp")


def suction(rng, length):
    """Its mouth re-gripping the car: a long seal of wet flesh dragging tight, ending in a sticky pop."""
    n = synth.noise(length, rng)
    fc = env([(0, 160), (length * 0.5, 480), (length * 0.9, 220), (length, 180)], length, "exp")
    y = dsp.sweep_filter(n, "bp", fc, q=6) * env([(0, 0), (length * 0.2, 1), (length * 0.85, 0.8), (length, 0)], length)
    b = synth.bubbles(length, env([(0, 15), (length, 60)], length), 180, 900, rng, rise=(0.3, 1.0))
    sq = grain(rng, SQUISH[0], length, -12, attack=False)
    out = Bus(length + 0.3)
    out.at(0, mix(norm(y, 0.1), norm(b, 0.1) * 0.5, norm(sq, 0.1) * 0.5))
    out.at(length * 0.95, norm(slap(rng, 0.08, 1500, 1.0), 0.1), -2)
    strings = bp(synth.crackle(0.3, 40, rng, size=(0.001, 0.004), hi=900), 900, 4500)   # mucus strings snapping
    out.at(length + 0.03, norm(strings * env([(0, 1), (0.3, 0)], 0.3), 0.1), -12)
    return out.x


@recipe("cs-car-hugger", "hit", "regrip",
        "The blow lands in wet rubber; its rings shudder, and its mouth sucks tighter on the car",
        """The same voiceless body, answering with its mouth instead of the car: a dense low whump, slap and squish as the
        blow sinks in, then its rings shudder (a wet squelch fluttering ten-odd times a second, dying away), and it
        re-grips: a long seal of flesh dragging tight on the car (noise through a sweeping wet band, slow bubbles, the
        chewing squish two octaves down) that ends in a sticky pop and strings of mucus snapping.""",
        sources=SQUISH, takes=4)
def hugger_hit_regrip(rng, take):
    b = Bus(2.4)
    b.at(0, hide(rng, rng.uniform(0.85, 1.15)), 0)
    b.at(0.04, norm(ripple(rng, rng.uniform(0.45, 0.7)), 0.1), -7)
    b.at(rng.uniform(0.35, 0.55), suction(rng, rng.uniform(0.6, 1.0)), -6)
    return dsp.room(b.x, "car", wet=0.15, rng=rng)


HISS = src.S("loop_ambient_01")
RAIL = src.S("loop_ambient_02")
RUMBLE = src.S("thunder_01")
IRON_RING = [96, 243, 470, 912, 1530, 2380]


def shear(rng):
    """The drawbar giving way: a crack of iron, the van's end splintering, a heavy drop of weight and the iron ringing."""
    b = Bus(2.5)
    b.at(0, norm(snap(rng, 0.12, 1100), 0.1), 0)
    b.at(0, norm(grain(rng, IRONHIT[0], 0.8, -4), 0.1), -1)
    b.at(0.01, norm(grain(rng, IRONHIT[2], 0.3, -7), 0.1), -3)
    b.at(0.005, thud(rng, 45, 0.7, 0.3), 0)
    x = np.zeros(samples(2.2), np.float32)
    x[:samples(0.003)] = rng.standard_normal(samples(0.003))
    ring = dsp.resonate(x, [f * rng.uniform(0.85, 0.95) for f in IRON_RING], q=90, gains=[0.6, 1, 0.8, 0.5, 0.35, 0.2])
    b.at(0.01, norm(ring * env([(0, 1), (2.2, 0)], 2.2, "exp"), 0.1), -10)
    b.at(0.03, norm(grain(rng, SPLINTER[1], 0.7, -3), 0.1), -4)
    return b.x


def hose(rng, length=1.6):
    """The brake hose between the cars tearing apart: the brake pipe dumping its air in one hard blast."""
    h = grain(rng, HISS[0], length, -2, attack=False)
    h = bp(h, 700, 9000) * env([(0, 0), (0.01, 1), (0.25, 0.7), (length, 0)], length, "exp")
    return norm(h, 0.1)


def shoe_squeal(rng, length, f=2600):
    """A cut car's brakes going on by themselves (the pipe's vented): iron shoes screaming on its wheels, the pitch
    wavering and jumping between the wheel's modes."""
    n = samples(length)
    jump = np.where(lp(rng.standard_normal(n).astype(np.float32), 0.8) > 0.004, 1.0, 1.18)
    fc = f * jump * 2 ** (drift(rng, n, 1.5, 25) / 1200)
    ph = np.cumsum(lp(fc, 30)) / SR
    tone = np.sin(2 * np.pi * ph) + 0.35 * np.sin(2 * np.pi * 2.03 * ph) + 0.15 * np.sin(2 * np.pi * 2.97 * ph)
    grit = bp(synth.noise(length, rng), f * 0.7, f * 1.5)
    am = np.clip(0.65 + drift(rng, n, 2.5, 0.3), 0.05, 1.3)
    return ((norm(tone, 0.1) + norm(grit, 0.06)) * am).astype(np.float32)


def clack(rng):
    """A wheel over a rail joint: a sharp iron knock (struck plate and a ringing iron hit) with the car's weight under."""
    k = lp(grain(rng, IRONHIT[rng.integers(4, 7)], 0.25, rng.uniform(-5, -2)), 5000)
    r = grain(rng, IRONHIT[rng.integers(1, 4)], 0.12, rng.uniform(-9, -6), decay=0.02)
    return mix(norm(k, 0.1), norm(r, 0.1) * 0.6, thud(rng, 70, 0.15, 0.2) * 0.6)


def falling_behind(rng, length, start_gap=0.5, end_gap=1.6):
    """The cut car rolling away behind: its two trucks clacking over the joints further and further apart as it slows,
    its wheels' roll, its brakes screaming, the Hugger still heaving in it."""
    b = Bus(length + 1)
    t = 0.0
    while t < length:
        u = t / length
        gap = start_gap * (end_gap / start_gap) ** u
        for d in (0.0, gap * 0.22):
            b.at(t + d, norm(clack(rng), 0.4), -4 * u)
        t += gap * rng.uniform(0.95, 1.05)
    roll = lp(grain(rng, RAIL[0], min(length, 2.9), -3, attack=False), 1500)
    roll = dsp.fit(dsp.stretch(roll, length / (len(roll) / SR)), samples(length))
    b.at(0, norm(roll * env([(0, 1), (length, 0.3)], length), 0.1), -11)
    b.at(0.3, shoe_squeal(rng, length - 0.3, rng.uniform(2300, 2900)) * env([(0, 0), (0.4, 1), (length, 0.6)], length - 0.3), -3)
    for i in range(2):
        b.at(rng.uniform(0.5, length - 1), hide(rng, 0.8), -12)
        b.at(rng.uniform(0.5, length - 1.5), van_groan(rng, rng.uniform(0.8, 1.3), 35), -12)
    return b.x


@recipe("cs-car-hugger", "break-away", "parting",
        "The drawbar shears with the van straining, the brake hose blasts, and the van falls away behind with its brakes screaming",
        """The car failing the way a car does: the van's timbers and drawgear straining under the Hugger (boards slipping
        faster into a groan, iron creaking, boards popping, quicker and quicker), then the drawbar shears (an iron crack,
        a heavy drop, the iron ringing, the end splintering) and the brake hose tears apart in one hard blast of air.
        Cut loose, the van's own brakes go on: it falls away behind, its wheels clacking over the joints further and
        further apart, its brake shoes screaming, the Hugger still heaving and the timbers still groaning in it, pulled
        back into the dark (darker, wetter, quieter, sagging in pitch).""",
        sources=CREAKS + IRONHIT + SPLINTER + HISS + RAIL + SQUISH + WATER, takes=1)
def hugger_away_parting(rng, take):
    b = Bus(12)
    # the strain: boards and iron giving, pops coming quicker
    b.at(0, van_groan(rng, 2.0, 70), -2)
    iron = creak(2.0, env([(0, 6), (2.0, 40)], 2.0), rng, body=synth.IRON, q=28, jitter=0.4)
    b.at(0, norm(iron * env([(0, 0), (1.8, 1), (2.0, 1)], 2.0), 0.1), -8)
    for t in (0.35, 0.9, 1.3, 1.55, 1.72, 1.85):
        b.at(t, norm(grain(rng, SPLINTER[rng.integers(2)], 0.15, rng.uniform(-6, -2)), 0.1), -10 + 4 * t)
    b.at(0.2, hide(rng, 0.7), -10)
    # it goes
    b.at(1.95, shear(rng), 2)
    b.at(2.0, hose(rng), -3)
    b.at(2.1, recede(norm(falling_behind(rng, 7.5), 0.1), rng, 3, 260, drop=2.5), 8)
    return tame(hp(dsp.compress(b.x, -22, 3, 0.003, 0.2), 25))


def crash(rng, size=1.0):
    """A car hitting the ground: timber smashing, iron banging, ballast and earth thrown."""
    b = Bus(1.2)
    b.at(0, norm(grain(rng, TIMBER[rng.integers(2)], 0.35, -6 * size), 0.1), 0)
    b.at(0.005, norm(grain(rng, IRONHIT[rng.integers(7)], 0.4, -5 - 3 * size), 0.1), -4)
    b.at(0.01, norm(grain(rng, SPLINTER[rng.integers(2)], 0.6, -2 - 2 * size), 0.1), -3)
    b.at(0, thud(rng, 50, 0.5, 0.3), 2)
    for _ in range(int(6 * size)):
        b.at(rng.uniform(0, 0.15), ballast(rng, 0.8, 0.05), -6)
    return b.x


@recipe("cs-car-hugger", "break-away", "tumble",
        "The Hugger tears the van off the coupling and it goes off the rails, tumbling down the bank into the dark",
        """The Hugger does it: its mouth heaves (a long wet suction and the chewing squish two octaves down) and wrenches
        the van's end off (timbers tearing, a door creak dragged down an octave, iron creaking, then the coupling tearing
        out with a crack of iron and splinters). The van drops off the rails in a heavy crash of timber, iron and thrown
        ballast over a sub-bass thump, then tumbles down the bank, each crash further off, the leech's wet bulk slapping
        down with it, until a last heavy thud far back in the dark.""",
        sources=SQUISH + CREAKS + IRONHIT + SPLINTER + TIMBER + RUMBLE + STONES, takes=1)
def hugger_away_tumble(rng, take):
    b = Bus(12)
    b.at(0, suction(rng, 1.4), -4)
    b.at(0.2, van_groan(rng, 1.6, 80), 0)
    iron = creak(1.6, env([(0, 10), (1.6, 50)], 1.6), rng, body=synth.IRON, q=25, jitter=0.5)
    b.at(0.3, norm(iron, 0.1), -9)
    b.at(1.75, shear(rng), 0)
    fall = Bus(9)
    fall.at(0, crash(rng, 1.3), 2)
    rum = lp(grain(rng, RUMBLE[0], 2.5, -5, attack=False), 300)
    fall.at(0, norm(rum * env([(0, 0), (0.05, 1), (2.5, 0)], 2.5, "exp"), 0.1), -4)
    t = 0.0
    for i, gap in enumerate([0.55, 0.5, 0.7, 0.9, 1.2]):
        t += gap * rng.uniform(0.9, 1.15)
        fall.at(t, crash(rng, 1.0 - 0.12 * i), -2 * i)
        if i in (1, 3):
            fall.at(t + 0.08, hide(rng, 0.7), -4 - 2 * i)
    fall.at(t + 1.0, thud(rng, 45, 0.8, 0.3), -8)
    b.at(2.05, recede(fall.x, rng, 6, 180, drop=1.0, curve=1.5), 0)
    return tame(hp(dsp.compress(b.x, -22, 3, 0.003, 0.2), 25))


# ---- Tippy Toesie: the struggle -------------------------------------------------------------------------------------------

BOOTS = [f"kenney_rpg-audio:footstep{i:02d}" for i in range(10)]
KNOCKS = src.K("impactWood_light") + src.S("footstep_wood_02", "footstep_wood_04")
BUMPS = src.K("impactWood_medium", [0, 2, 4]) + src.K("impactWood_heavy", [1, 3])
SCRAPE = src.S("misc_10")
GASP = src.S("misc_05")


def kick(rng):
    """A boot heel or toe hitting the boards: a real boot step, cut to its first knock."""
    key = BOOTS[rng.integers(len(BOOTS))] if rng.random() < 0.6 else KNOCKS[rng.integers(len(KNOCKS))]
    g = grain(rng, key, rng.uniform(0.06, 0.14), rng.uniform(-3, 2))
    return lp(g, rng.uniform(3000, 8000))


def heel_drag(rng, length):
    """A boot sole dragged across boards: friction noise roughened by the grain and rung lightly through the board, a real
    scrape slowed under it, and now and then the rubber catching in a short squeak."""
    n = samples(length)
    rough = np.abs(lp(rng.standard_normal(n).astype(np.float32), rng.uniform(40, 90)))
    fr = bp(synth.noise(length, rng), 250, 4000) * (rough / (np.max(rough) + 1e-9))
    fr = mix(fr, dsp.resonate(fr, [m * rng.uniform(1.0, 1.5) for m in synth.WOOD], q=8) * 0.3)
    sc = lp(grain(rng, SCRAPE[0], length / 1.5, -4, attack=False), 4000)
    sc = dsp.fit(dsp.stretch(sc, 1.5), n)
    y = mix(norm(fr, 0.1), norm(sc, 0.1) * 0.6)
    if rng.random() < 0.3:
        d = rng.uniform(0.06, 0.12)
        sq = creak(d, rng.uniform(600, 1100), rng, body=[1400, 2300, 3500], q=10, jitter=0.15)
        y = mix(y, pad(norm(sq * env([(0, 0), (0.01, 1), (d, 0)], d), 0.1) * 0.5, rng.uniform(0, length * 0.6)))[:n]
    return hp(y * env([(0, 0), (0.03, 1), (length * 0.6, 0.8), (length, 0)], length), 60)


def cloth(rng, length, pitch=0.0, stretch=1.0):
    """Clothes moving: real cloth handling, a few grains layered and smeared along the length."""
    out = Bus(length + 0.5)
    t = 0.0
    while t < length:
        g = grain(rng, CLOTH[rng.integers(len(CLOTH))], rng.uniform(0.12, 0.35), pitch + rng.uniform(-3, 2), attack=False)
        if stretch != 1.0:
            g = dsp.stretch(g, stretch)
        out.at(t, norm(g, 0.1), rng.uniform(-8, 0))
        t += rng.uniform(0.05, 0.2) * stretch
    return out.x[:samples(length + 0.3)]


def bump(rng):
    """A shoulder or knee hitting the floor."""
    g = lp(grain(rng, BUMPS[rng.integers(len(BUMPS))], 0.25, rng.uniform(-4, 0)), 1500)
    return mix(norm(g, 0.1), thud(rng, 90, 0.2, 0.1) * 0.6)


def pant(rng, length, rate, size=1.25):
    """Its excited breath: quick, shallow, thin (a tract smaller than a child's), each breath a short push in or out,
    with a gasp's own air pitched up in it."""
    n = samples(length)
    shape = np.zeros(n, np.float32)
    t = 0.0
    while t < length:
        d = rng.uniform(0.6, 1.0) / rate
        a = rng.uniform(0.5, 1.0)
        e = env([(0, 0), (d * 0.25, a), (d * 0.7, a * 0.4), (d, 0)], d)
        s0 = samples(t)
        m = min(len(e), n - s0)
        shape[s0:s0 + m] = np.maximum(shape[s0:s0 + m], e[:m])
        t += d * rng.uniform(1.0, 1.3) + (rng.exponential(0.35) if rng.random() < 0.15 else 0)
    vow = [(0, "h"), (length * 0.4, "i"), (length * 0.7, "h")]
    b = synth.breath(length, vow, size, rng, shape)
    g = dsp.fit(dsp.stretch(grain(rng, GASP[0], 0.3, 5, attack=False), length / 0.3 * 0.7), n) * shape
    return lp(hp(mix(norm(b, 0.1), norm(g, 0.1) * 0.5), 1400), 7500)


def struggle_bursts(rng, bus, spans, kicks=6.0):
    """The fight in bursts: boots kicking and dragging, clothes thrashing, a knee or shoulder on the boards."""
    for a, z in spans:
        L = z - a
        t = a + rng.uniform(0, 0.1)
        while t < z:
            bus.at(t, norm(kick(rng), 0.1), rng.uniform(-9, 0))
            if rng.random() < 0.3:
                d = rng.uniform(0.2, 0.5)
                bus.at(t + 0.03, heel_drag(rng, d), rng.uniform(-12, -6))
                t += d * 0.6
            t += rng.exponential(1 / kicks) + 0.04
        bus.at(a, cloth(rng, L, -2), -4)
        for _ in range(rng.integers(1, 3)):
            bus.at(rng.uniform(a, z), bump(rng), rng.uniform(-6, -2))


@recipe("cs-tippy", "struggle", "thrash",
        "Boots kicking and dragging on the boards in bursts, clothes thrashing, and its quick thin excited breath",
        """The victim's body, since their voice goes out muffled on voice chat: real boot steps cut into kicks on the
        boards, soles dragging (stick-slip through a board's modes over a slowed scrape), clothes thrashing (real cloth
        handling layered), a knee or shoulder thumping down, in bursts with short spent lulls between. Close by all
        through it, its excited breath: quick, shallow and thin, a tract smaller than a child's with a gasp pitched up in
        it, quickening in the lulls. Inside a wooden car. Loops in one exact cycle.""",
        sources=BOOTS + KNOCKS + BUMPS + SCRAPE + CLOTH + GASP, loop=True, takes=1, seconds=12.0)
def tippy_struggle_thrash(rng, take):
    L = 12.0
    bus = loop_bus(L)
    struggle_bursts(rng, bus, [(0.15, 2.3), (3.0, 4.4), (5.5, 8.0), (8.9, 10.2), (10.9, 11.9)], 6.5)
    for a, z in [(2.3, 3.0), (4.4, 5.5), (8.0, 8.9), (10.2, 10.9)]:
        bus.at(a, cloth(rng, z - a, -4, 1.8), -12)
        bus.at(a + 0.1, heel_drag(rng, rng.uniform(0.3, 0.5)), -14)
    bus.at(0, pant(rng, L, 4.2), -17)
    boards = creak(L, env([(0, 8), (L / 2, 14), (L, 8)], L), rng, body=synth.WOOD, q=14, jitter=0.5)
    bus.at(0, norm(boards * dsp.tremolo(np.ones(samples(L), np.float32), 0.25, 0.9, rng, 0.5), 0.1), -24)
    return close(bus, L, "car", 0.2, rng)


def lips(rng):
    """A wet lip parting: a soft low tick with a little moisture in it."""
    return mix(norm(synth.click(rng.uniform(900, 1600), q=3, length=0.02, rng=rng), 0.1),
               norm(synth.bubbles(0.03, 150, 1200, 3000, rng), 0.1) * 0.4)


@recipe("cs-tippy", "struggle", "pinned",
        "Pinned: heels dragging slowly on the boards, clothes twisting, a weak kick now and then, its breath savouring it",
        """Later in the twenty seconds, the fight going out of them: heels dragging long and slow across the boards
        (stick-slip soles over a slowed scrape), clothes twisting tight (cloth handling stretched out), boards creaking
        under shifting weight, two weak kicks a loop. Its breath is close and slower now, in through its teeth and out
        thin, with wet little lip sounds between: it's enjoying it. Inside a wooden car. Loops in one exact cycle.""",
        sources=BOOTS + KNOCKS + SCRAPE + CLOTH + GASP, loop=True, takes=1, seconds=12.0)
def tippy_struggle_pinned(rng, take):
    L = 12.0
    bus = loop_bus(L)
    for t0 in (0.4, 2.9, 4.6, 7.3, 9.6, 11.2):
        bus.at(t0, heel_drag(rng, rng.uniform(0.5, 1.0)), rng.uniform(-6, -2))
    for t0 in (3.8, 8.6):
        bus.at(t0, norm(kick(rng), 0.1), -6)
        bus.at(t0 + rng.uniform(0.1, 0.2), norm(kick(rng), 0.1), -12)
    for t0, d in ((1.2, 1.6), (5.4, 1.4), (8.9, 1.8)):
        bus.at(t0, cloth(rng, d, -5, 2.2), -6)
    bus.at(6.2, bump(rng), -8)
    boards = creak(L, env([(0, 6), (L * 0.3, 16), (L * 0.6, 7), (L, 6)], L), rng, body=synth.WOOD, q=14, jitter=0.6)
    bus.at(0, norm(boards * env([(0, 0.2), (2, 1), (4, 0.2), (7, 0.9), (9, 0.3), (L, 0.2)], L), 0.1), -18)
    bus.at(0, pant(rng, L, 2.4, 1.15), -14)
    for t0 in rng.uniform(0, L, 7):
        bus.at(t0, lips(rng), -22)
    return close(bus, L, "car", 0.2, rng)
