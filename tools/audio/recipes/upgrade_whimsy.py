"""U1's whimsies heard (#967, note 715; queue #534, note 806; U1's ask on #1040): the canary in car one, the steam
calliope's note on the whistle's pull, and the gramophone wound in the guard van, as tools/audio/cues.py lists them
(up-whimsy). Each plays over U1's own made sound only where it's installed.

What each is decides the build:
- The canary (song): a domestic canary in a small brass-wire cage hung in car one, singing while all's well. When it
  stops, something is aboard: the silence is the tell, so the song has to be pleasant, alive and unmistakably a small
  songbird. Real canary song is a run of "tours": one syllable repeated at an even rate, then another. Rolls (fast
  down-sweeps 15-25 ms long, 20-30 a second), slower tours of up-sweeps, arches and two-part 'tsee-ew's (7-16 a
  second), warbles (a syllable with a fast wobble in it), a bubbling 'water roll' (a short tone 40-50 times a second),
  a few clear whistled notes, and sharp chips: 2-7 kHz, almost pure tones, the pitch always moving. Each take is a
  different phrase of three to five tours, 2-3 s. The car's wooden room a little round it; nothing under a kilohertz.
- The calliope (calliope): a steam calliope on the roof whose pipes the whistle cord now plays, one note a pull, which
  the game pitches to the tune's notes. So every take is the same note, C5 (523 Hz, the middle of #967's tune played an
  octave down, G4-G5, in the whistle's band; no note is moved more than five semitones): the valve
  snapping open with a chiff of steam, the pipe speaking (its pitch scooping up into tune as the pressure comes on, its
  twelfth sounding for an instant first), a steady breathy, reedy tone wavering a little with the steam, and the valve
  closing: the pitch sagging and the tone thinning into breath. A steam whistle's bell is a stopped pipe, so its odd
  harmonics are strong. The steam's noise is kept modest and mostly rung in the pipe's own resonances, so it moves with
  the note when the game pitches it: no loud fixed hiss band. About 2.5 s.
- The gramophone (wind): a portable gramophone in the guard van wound by its crank, a few turns: the click (a pawl over
  the ratchet wheel on the winding arbor, twelve teeth a turn) ticking evenly, the gears' purr under it, the crank
  slowing a little turn by turn as the mainspring tightens and the effort rises (the handle's knob creaking on its pin
  at the hard part of each turn, a little more each time), and when the hand stops, the spring taking up against the
  pawl with a last click. A small wooden cabinet rings round it; the van round that. 1.5-2.4 s.

Each cue has two candidates. The packs hold no birds, so both canaries are modelled: one draws each syllable's pitch as
an FM sweep and strings the syllables into tours; the other models the bird's syrinx, its two sides two voices, each a
vibration that starts and dies with the air pressure behind it, driven by simple breathing and muscle gestures (a
sinusoid of pressure and one of tension, whose phase against each other shapes the syllable: Gardner, Cecchi, Magnasco,
Laje and Mindlin 2001), through the trachea and the beak. The calliope's first is modelled (an additive stopped pipe,
the jet's turbulence rung through the pipe's resonances); its second is blown by the packs' real locomotive steam (the
tone locked onto the steam's noise, as up_horns_tells' gold whistle is) with a real burst of air and a real valve's rush
for its chiff. The crank's first is the packs' real ratchet, lock and clockwork clicks cut to single teeth, with a real
creak for the knob; its second is modelled.
"""

import functools
import math

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S
from recipes.crew_items import hit_of
from recipes.up_horns_tells import PRE, STEAM, lock, smooth, steam_rec

SR = dsp.SR
L_ = "up-whimsy"


def unit(x):
    return W.norm(np.asarray(x, np.float32))


# ---- song: the canary in its cage ----------------------------------------------------------------------------------------

CAR_ROOM = 17         # the car's room (its seed): the same wooden car under both canaries
SONG_FLOOR = 1200.0   # nothing of the bird is under this: everything below is cut


def in_cage(y):
    """The cage hung in the car: the bird close, the car's wooden room a little round it, nothing under a kilohertz."""
    y = dsp.room(y, "car", wet=0.13, rng=np.random.default_rng(CAR_ROOM))
    return hp(y, SONG_FLOOR, 4)


def ease(u):
    return u * u * (3 - 2 * u)


def glide(u, pts):
    """A pitch path through [(u, Hz), ...] over a syllable (u 0-1), each leg eased: a bird's sweep leaves and lands
    softly, fastest in the middle."""
    out = np.empty_like(u)
    for (u0, f0), (u1, f1) in zip(pts[:-1], pts[1:]):
        m = (u >= u0) & (u <= u1)
        out[m] = f0 + (f1 - f0) * ease((u[m] - u0) / (u1 - u0))
    return out


# One canary syllable kind per tour. Each: its length (s), how many a second, and its pitch path drawn once per tour
# (`p` the tour's register): the bird repeats one syllable almost exactly, which is what makes a tour.
def proto(kind, rng, p):
    u = rng.uniform
    if kind == "roll":      # the roll: fast down-sweeps, 20-30 a second
        hi, lo = u(4700, 5400) * p, u(2800, 3200) * p
        return dict(L=u(0.016, 0.022), rate=u(23, 28), path=[(0, hi), (0.45, lo + 0.3 * (hi - lo)), (1, lo)],
                    att=0.15, hold=0.45, db=0)
    if kind == "chip":      # a sharp chip: a steep fall from high
        return dict(L=u(0.013, 0.018), rate=u(8, 11), path=[(0, u(6400, 7200) * p), (0.35, 3900 * p), (1, u(2500, 2900) * p)],
                    att=0.06, hold=0.25, db=2)
    if kind == "rise":      # up-sweeps, 12-15 a second
        return dict(L=u(0.03, 0.04), rate=u(12, 15), path=[(0, u(2400, 2800) * p), (0.7, u(4400, 4900) * p), (1, u(4700, 5200) * p)],
                    att=0.3, hold=0.6, db=-1)
    if kind == "arch":      # up and over: 'wheeo'
        return dict(L=u(0.05, 0.068), rate=u(9, 12), path=[(0, u(3000, 3300) * p), (0.4, u(5000, 5500) * p), (1, u(3400, 3800) * p)],
                    att=0.2, hold=0.55, db=0)
    if kind == "tew":       # two-part: 'tsee-ew'
        return dict(L=u(0.07, 0.088), rate=u(7, 9), path=[(0, u(2800, 3100) * p), (0.35, u(4600, 5000) * p), (0.5, 4200 * p),
                                                          (1, u(2200, 2600) * p)], att=0.15, hold=0.6, db=0)
    if kind == "warble":    # a syllable with a fast wobble through it
        fc = u(3200, 3800) * p
        return dict(L=u(0.055, 0.072), rate=u(9, 12), path=[(0, fc + 200), (1, fc - 250)], att=0.2, hold=0.6, db=-1,
                    fm=(u(55, 80), u(220, 380)))
    if kind == "glock":     # the water roll: a short round tone 40-50 times a second, bubbling
        fc = u(2900, 3400) * p
        return dict(L=u(0.017, 0.02), rate=u(42, 50), path=[(0, fc * 1.02), (1, fc * 0.97)], att=0.45, hold=0.5, db=-3)
    if kind == "whistle":   # a clear whistled note, a slight vibrato
        f = u(2500, 2900) * p
        return dict(L=u(0.16, 0.26), rate=u(3.2, 4.0), path=[(0, f * 0.97), (0.2, f), (1, f * 1.03)], att=0.12, hold=0.7,
                    db=-2, vib=(u(10, 14), 0.005))
    raise KeyError(kind)


def syllable(rng, q):
    """One repeat of a tour's syllable: its pitch path a hair different each time (1%), its length (4%); a nearly pure
    tone with a faint second and third harmonic, the level rising quickly and falling away."""
    L = q["L"] * rng.uniform(0.96, 1.04)
    n = samples(L)
    u = np.linspace(0, 1, n)
    f = glide(u, q["path"]) * rng.uniform(0.99, 1.01)
    t = np.arange(n) / SR
    if "fm" in q:
        rate, depth = q["fm"]
        f = f + depth * np.sin(2 * np.pi * rate * t + rng.uniform(0, 2 * np.pi))
    if "vib" in q:
        rate, depth = q["vib"]
        f = f * (1 + depth * np.sin(2 * np.pi * rate * t))
    ph = 2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 2 * np.pi)
    a = np.sin(np.pi / 2 * np.clip(u / q["att"], 0, 1)) * np.cos(np.pi / 2 * np.clip((u - q["hold"]) / (1 - q["hold"]), 0, 1))
    x = np.sin(ph) + 0.09 * np.sin(2 * ph + 0.7) + 0.025 * np.sin(3 * ph + 1.3)
    return (a ** 1.2 * x).astype(np.float32)


def tour(rng, kind, amount, p, t0, parts):
    """A tour from t0: `amount` repeats (an int) or seconds of them (a float), each at the tour's rate, a hair uneven;
    the first two coming in softer, the last falling off. Returns when the last one ends."""
    q = proto(kind, rng, p)
    n = amount if isinstance(amount, int) else max(2, round(amount * q["rate"]))
    t = t0
    for i in range(n):
        db = q["db"]
        if n > 2:
            db += (-5.0, -2.5)[i] if i < 2 else (-1.5 if i == n - 1 else 0.0)
        parts.append((t, syllable(rng, q), db + rng.uniform(-1, 1)))
        end = t + q["L"] * 1.04
        t += rng.uniform(0.98, 1.02) / q["rate"]
    return end


# Each take a different phrase: (kind, repeats or seconds[, register]).
PHRASES = [
    [("chip", 2), ("roll", 0.6), ("arch", 6), ("whistle", 2), ("chip", 1)],
    [("rise", 8), ("glock", 0.5), ("tew", 5), ("roll", 0.4)],
    [("chip", 3), ("warble", 8), ("roll", 0.8, 0.92), ("chip", 1)],
    [("whistle", 3), ("arch", 5), ("glock", 0.4), ("rise", 6), ("roll", 0.35)],
    [("roll", 0.5, 1.05), ("roll", 0.5, 0.86), ("tew", 4), ("warble", 6), ("chip", 2)],
    [("arch", 5), ("chip", 4), ("glock", 0.6), ("whistle", 1), ("roll", 0.6)],
]


@recipe(L_, "song", "tours", "The canary singing in its cage: tours of trills, rolls, warbles, whistles and chips",
        """Modelled (the packs have no birds): a canary's song as it's sung, in tours, one syllable repeated at an even
        rate and then another. Each syllable is a near-pure tone whose pitch is drawn as a sweep (FM): rolls of fast
        down-sweeps 25 a second, up-sweeps, arches and two-part 'tsee-ew's 7-15 a second, warbles with a fast wobble
        through them, a bubbling water roll, clear whistled notes with a slight vibrato, and sharp chips falling from 7
        kHz; 2-7 kHz, a faint second harmonic, each repeat a hair different from the last, the first of a tour coming
        in softer. Each take is a different phrase of four or five tours, 2-3 s. The car's wooden room a little round
        it; nothing under a kilohertz.""", takes=6, lufs=-24, gap=0.8)
def song_tours(rng, k):
    parts, t = [], 0.02
    for spec in PHRASES[k % len(PHRASES)]:
        kind, amount = spec[:2]
        p = (spec[2] if len(spec) > 2 else 1.0) * rng.uniform(0.95, 1.05)
        t = tour(rng, kind, amount, p, t, parts) + rng.uniform(0.03, 0.07)
    return in_cage(ck.place(parts, t + 0.05))


# The syrinx: two sides (the left lower, the right higher), each a pair of labia set vibrating by the air pressure
# behind them once it's over a threshold, their pitch set by the muscles' tension and raised a little by the pressure.
# A tour is a gesture: pressure and tension each a sinusoid at the syllable rate (or the tension at twice it), the
# pressure dipping under the threshold between syllables (a mini-breath) or, in a roll, never. The phase between the
# two shapes the syllable: tension peaking with the pressure makes an arch, ahead of it a down-sweep, behind it an
# up-sweep, against it a 'U'; tension at twice the rate a two-part syllable.

PTH = 0.12            # the pressure at which the labia start to vibrate
MU = 5000.0           # how fast a side's vibration grows (or dies) for its pressure over (or under) the threshold (1/s)
KAPPA = 0.05          # the pitch raised by the pressure (5% at full)
CTRL = 8000           # the rate the labia's amplitude is followed at
SPLIT = 3600.0        # tours centred above this are the right side's, under it the left's
REST = -0.4           # the pressure between tours: a breath in


def gesture(kind, rng, p):
    """A tour's gesture: syllables a second (r), the pressure's mean and swing (P0, P1), the tension's centre pitch
    (fc, Hz), its swing (semitones peak to peak), its phase against the pressure's, and its cycles per syllable (m)."""
    u = rng.uniform
    G = {
        "trill": dict(r=u(22, 27), P0=0.0, P1=1.0, fc=u(3800, 4200), depth=u(7, 9), phi=np.pi / 2, m=1),
        "rise": dict(r=u(12, 15), P0=-0.1, P1=1.1, fc=u(3300, 3700), depth=u(8, 10), phi=-np.pi / 2, m=1),
        "arch": dict(r=u(9, 11), P0=-0.1, P1=1.1, fc=u(3900, 4300), depth=u(6, 8), phi=0.0, m=1),
        "u": dict(r=u(9, 11), P0=-0.1, P1=1.1, fc=u(3300, 3600), depth=u(5, 7), phi=np.pi, m=1),
        "double": dict(r=u(7, 8.5), P0=-0.1, P1=1.1, fc=u(3500, 3900), depth=u(8, 10), phi=rng.choice([0.0, np.pi / 2]), m=2),
        "roll": dict(r=u(40, 48), P0=0.55, P1=0.5, fc=u(2900, 3300), depth=u(1.2, 2.0), phi=np.pi / 2, m=1),
        "whistle": dict(r=u(3.2, 4.0), P0=0.15, P1=0.85, fc=u(2600, 2900), depth=u(0.8, 1.2), phi=-np.pi / 2, m=1),
        "chip": dict(r=u(9, 10), P0=-0.35, P1=1.4, fc=u(4300, 4700), depth=u(12, 15), phi=np.pi / 2, m=1),
    }[kind]
    G["fc"] *= p
    return G


def gesture_into(g, t0, dur, P, F):
    """Lay a gesture into a side's pressure and pitch from t0 for a whole number of syllables near `dur` s (an int: that
    many). Its pressure comes in and goes out over 20 ms. Returns its end."""
    n = dur if isinstance(dur, int) else max(1, round(dur * g["r"]))
    a, b = samples(t0), samples(t0 + n / g["r"])
    b = min(b, len(P))
    t = np.arange(b - a) / SR
    th = 2 * np.pi * g["r"] * t
    edge = np.clip(np.minimum(t, t[-1] - t) / 0.02, 0, 1)
    P[a:b] = REST + (g["P0"] + g["P1"] * np.sin(th) - REST) * edge
    F[a:b] = g["fc"] * 2 ** (g["depth"] / 2 * np.sin(g["m"] * th + g["phi"]) / 12)
    return t0 + n / g["r"]


def labia(P):
    """A side's vibration (its amplitude squared) per sample, under the pressure P: it grows while P is over the
    threshold, towards P less the threshold, and dies under it (the averaged van der Pol oscillator's amplitude, a
    logistic, stepped exactly at CTRL a second so the fast onsets stay stable)."""
    step = SR // CTRL
    dt = 1.0 / CTRL
    pc = P[::step].astype(np.float64)
    out = np.empty(len(pc))
    s = 1e-6
    for i, p in enumerate(pc):
        pe = p - PTH
        if abs(pe) < 1e-6:
            pe = 1e-6
        e = math.exp(MU * pe * dt)
        s = max(pe * s * e / (pe + s * (e - 1)), 1e-7)
        out[i] = s
    return np.interp(np.arange(len(P)), np.arange(len(pc)) * step, out).astype(np.float32)


def side(rng, P, F):
    """One side's voice: the labia's vibration at the tension's pitch (raised by the pressure), its wave rounder when
    soft and fuller when pushed hard (a soft clip that grows with the amplitude, a touch lopsided for a faint even
    harmonic), and the air through it, a breath of turbulence."""
    s = labia(P)
    a = np.sqrt(s)
    f = F * (1 + KAPPA * np.clip(P, 0, None))
    ph = 2 * np.pi * np.cumsum(f.astype(np.float64)) / SR + rng.uniform(0, 2 * np.pi)
    drive = 0.4 + 1.1 * s
    w = (np.tanh(drive * (np.sin(ph) + 0.08)) - np.tanh(drive * 0.08)) / np.tanh(drive * 1.08)
    air = bp(rng.standard_normal(len(P)).astype(np.float32), 2000, 9000) * np.sqrt(np.clip(P, 0, None)) * a * 0.025
    return (a * w + air).astype(np.float32), s, f


# Each take a different phrase of gestures: (kind, syllables or seconds[, register]); ("two", (low, high), seconds) both
# sides at once, the canary's two voices.
GESTURES = [
    [("chip", 2), ("trill", 0.7), ("arch", 5), ("roll", 0.45)],
    [("rise", 7), ("two", ("whistle", "trill"), 0.8), ("chip", 2)],
    [("arch", 4), ("double", 5), ("trill", 0.6, 0.9), ("chip", 1)],
    [("whistle", 2), ("roll", 0.5), ("u", 6), ("trill", 0.5)],
    [("trill", 0.5, 1.04), ("trill", 0.5, 0.88), ("two", ("roll", "rise"), 0.6), ("chip", 2)],
    [("double", 4), ("rise", 6), ("whistle", 1), ("trill", 0.7)],
]


@recipe(L_, "song", "syrinx", "The canary singing, its voice box modelled: two voices, trills and rolls from breath and muscle",
        """Modelled from how a canary sings: its syrinx has two sides, two voices (the left lower, the right higher),
        each a pair of membranes set vibrating by the air pressure behind them once it passes a threshold, so every
        note starts and dies with the breath instead of being switched; their pitch is set by the muscles' tension and
        pushed a little by the pressure. Each tour is one simple gesture, a pulse of pressure and a swing of tension
        repeated at the syllable rate: their timing against each other makes the syllable a down-sweep, an up-sweep, an
        arch, a 'U' or a two-part note, and where the pressure never drops between them, a bubbling roll. Some tours
        use both voices at once, a whistle under a trill. Through the bird's trachea and its beak, which opens with
        the pitch and keeps the tone pure. Each take a different phrase, 2-3 s, in the car's wooden room a little;
        nothing under a kilohertz.""", takes=6, lufs=-24, gap=0.8)
def song_syrinx(rng, k):
    plan = GESTURES[k % len(GESTURES)]
    n = samples(4.5)
    P = {s_: np.full(n, REST, np.float32) for s_ in "LR"}
    F = {s_: np.full(n, 3000.0, np.float32) for s_ in "LR"}
    t = 0.02
    for spec in plan:
        if spec[0] == "two":            # both sides at once: the low one under the threshold's split, the high over it
            ends = []
            for kd, sd in zip(spec[1], "LR"):
                g = gesture(kd, rng, rng.uniform(0.95, 1.05))
                g["fc"] = min(g["fc"], SPLIT * 0.92) if sd == "L" else max(g["fc"], SPLIT * 1.08)
                ends.append(gesture_into(g, t, spec[2], P[sd], F[sd]))
            t = max(ends)
        else:
            kind, amount = spec[:2]
            g = gesture(kind, rng, (spec[2] if len(spec) > 2 else 1.0) * rng.uniform(0.95, 1.05))
            sd = "R" if g["fc"] >= SPLIT else "L"
            t = gesture_into(g, t, amount, P[sd], F[sd])
        t += rng.uniform(0.03, 0.07)
    n = samples(t + 0.05)
    voices = [side(rng, P[s_][:n], F[s_][:n]) for s_ in "LR"]
    y = voices[0][0] + voices[1][0]
    # the beak opens with the pitch of whichever side is louder, keeping the tone pure; the trachea's two resonances
    s_l, s_r = voices[0][1], voices[1][1]
    f_dom = (voices[0][2] * s_l + voices[1][2] * s_r) / (s_l + s_r + 1e-9)
    f_dom = lp(f_dom, 40, 1)
    y = dsp.sweep_filter(y, "lp", np.clip(1.3 * f_dom, 2500, 12000), q=0.8, block=64)
    y = mix(y, dsp.resonate(y, [2900, 8600], q=4, gains=[0.5, 0.3]))
    return in_cage(y)



# ---- calliope: the steam calliope's note ---------------------------------------------------------------------------------

# C5: the middle of #967's calliope tune played an octave down (MIDI 67-79, G4-G5), so the game's pitching moves no note
# more than five semitones. An octave down because the calliope is the whistle, and the whistle's band (spec A.4,
# AudioBench.TellBands) is 200-800 Hz: at #967's own octave (G5-G6, 784-1568 Hz) it would sit in Tippy's, the Stoker's and
# the Grumbler's bands.
NOTE = 523.25
CALL_L = 2.5          # the note (s), its release's breath included
OFF = 2.08            # when the valve starts to close
# A steam whistle's bell is a stopped pipe: its odd harmonics strong, the even ones faint; a little upper reach for a reedy,
# calliope edge. Brightness grows with the pressure (an upper harmonic h scales as speak ** (BRIGHT * (h - 1))).
PIPE = {1: 1.0, 2: 0.14, 3: 0.5, 4: 0.07, 5: 0.28, 6: 0.04, 7: 0.15, 8: 0.025, 9: 0.08, 11: 0.045, 13: 0.025, 15: 0.012}
BRIGHT = 0.25
BEND = 1.2            # semitones the pitch falls for the whole pressure gone (a whistle sounds flat under-blown)
SPACE = ((0.004, 0.4), (0.011, 0.25))   # the car roof and the tender beside it, out in the night


def pressure(L, off, wander):
    """The steam behind the pipe over the note: the valve snapping open (full in 45 ms, the line's pressure surging a
    little past it), held but never still (`wander`, a curve round 1), and the valve closing from `off` over 0.3 s."""
    p = env([(0, 0), (0.012, 0.45), (0.045, 1.08), (0.12, 1.0), (off, 1.0), (off + 0.1, 0.55), (off + 0.3, 0.0), (L, 0.0)], L)
    return (p * wander).astype(np.float32)


def speaking(p):
    """How fully the pipe sounds on pressure p: nothing under 15%, full at full."""
    return np.clip((p - 0.15) / 0.85, 0, None) ** 1.3


def pipe_tone(ph, p, t, weights=PIPE):
    """The pipe's tone on a phase: its harmonics thinning as the pressure falls, and at the very start its twelfth (the
    3rd harmonic, the overblown note) jumping out for an instant before the fundamental takes over (the chiff's pitch)."""
    sp = speaking(p)
    twelfth = 1 + 2.5 * np.exp(-t / 0.018)
    out = np.zeros(len(ph), np.float64)
    for h, w in weights.items():
        g = w * sp ** (BRIGHT * (h - 1) + 1)
        if h == 3:
            g = g * twelfth
        out += g * np.sin(h * ph + 0.4 * h)
    return out.astype(np.float32)


def comb(x, f0=NOTE, q=30, even=0.35):
    """The pipe's resonances on noise: a stopped pipe's comb (peaks at its harmonics, the even ones weaker), the jet's
    turbulence rung in the pipe as a waveguide rings it. A zero-phase filter in the frequency domain, so its peaks sit
    exactly on the note."""
    m = int(2 ** np.ceil(np.log2(len(x) + samples(0.2))))
    f = np.maximum(np.fft.rfftfreq(m, 1 / SR), 1.0)
    H = np.full(len(f), 0.06)
    for h in range(1, 24):
        fh = f0 * h
        H += (1.0 if h % 2 else even) / np.sqrt(h) / np.sqrt(1 + (q * (f / fh - fh / f)) ** 2)
    return np.fft.irfft(np.fft.rfft(x, m) * H, m)[:len(x)].astype(np.float32)


def on_roof(y):
    """Up on the car roof, out in the night: a couple of near reflections, a short tail. Nothing is kept under 300 Hz,
    below the note: the jet's rumble there would turn to a roar under the note when the game pitches it down."""
    y = ck.space(y, 0.3, 0.08, 5000, SPACE)
    return hp(y, 300, 4)


def chiff_model(rng, L):
    """The valve snapping open, modelled: a spike of jet noise (5 ms up, 80 ms down), part of it rung in the pipe."""
    p = env([(0, 0), (0.005, 1), (0.08, 0.15), (0.18, 0), (L, 0)], L)
    j = W.jet(L, rng, pressure=p, peak=3500, low=0.0, eddy=0.8)
    return unit(mix(unit(comb(j, q=12)), unit(hp(j, 600, 2)) * 0.6))


@recipe(L_, "calliope", "pipe", "The steam calliope's note (C5), modelled: a chiff of steam, a breathy stopped pipe, a sag",
        """Modelled: one steam calliope pipe sounding C5 (523 Hz; the game pitches it to the tune's notes). A steam
        whistle's bell is a stopped pipe, so its tone is built from its harmonics with the odd ones strong (the third
        at half the fundamental, the fifth and seventh after it, the even ones faint), a little upper reach for a reedy
        edge, brighter with more pressure. The valve snaps open with a chiff (a spike of steam rung in the pipe) and the
        pipe's twelfth sounds for an instant before the note takes over, scooping up into tune as the pressure comes
        on; held, the steam's pressure wanders (slow surges and a faster flutter), so the pitch wavers a few cents and
        the level breathes; the jet's turbulence is rung in the pipe's own resonances (a waveguide comb on the note), so
        the breath moves with the note when it's pitched, with only a little plain hiss. The valve closes over 0.3 s:
        the pitch sags, the harmonics thin, the breath goes. 2.5 s, on the car roof in the night.""",
        takes=2, lufs=-16)
def calliope_pipe(rng, k):
    L = CALL_L
    n = samples(L)
    t = np.arange(n) / SR
    wander = 1 + 0.025 * W.slow(n, 2.5, rng) + 0.012 * W.slow(n, 8.0, rng)
    p = pressure(L, OFF + 0.06 * k, wander)
    drift = 0.06 * W.slow(n, 0.8, rng)                      # an old calliope never quite in tune: a few cents' drift
    semis = BEND * (np.maximum(p, 0.3) - 1) + drift
    ph = 2 * np.pi * np.cumsum(NOTE * 2 ** (semis.astype(np.float64) / 12)) / SR + rng.uniform(0, 2 * np.pi)
    tone = pipe_tone(ph, p, t)
    jet = W.jet(L, rng, pressure=np.maximum(p, 0), peak=2600, low=0.0, eddy=0.8)
    breath = unit(comb(jet)) * 0.16 + unit(hp(jet, 1500, 2)) * 0.03
    y = mix(unit(tone), breath * np.clip(p, 0, None) ** 0.5, chiff_model(rng, L) * 0.75)
    return on_roof(y)


# The real-steam candidate: the packs' real locomotive steam blowing the pipe, the tone locked onto the steam's noise at
# the note (up_horns_tells.lock: rung down to a band a couple of hertz wide, the tone taking that band's frequency, held
# within a few cents of the note), so the steam's own turbulence wavers its pitch and its own surges its level.

PUFF = "sfx_100_v2:air_03"     # a real short burst of air: the valve snapping open
VALVE = "sfx_100_v2:misc_22"   # a real steam vehicle's hiss: a valve's rush
# The real-steam pipe a touch reedier: its even harmonics up a little, as a pipe blown hard by wet steam sounds.
REEDY = {1: 1.0, 2: 0.22, 3: 0.48, 4: 0.12, 5: 0.27, 6: 0.07, 7: 0.15, 8: 0.04, 9: 0.08, 10: 0.025, 11: 0.045, 13: 0.025}


@functools.lru_cache(maxsize=1)
def _puff():
    """The real burst of air from its first strong attack, 0.2 s."""
    return ck.align(ck.get(PUFF), floor_db=-20)[:samples(0.2)]


def chiff_real(rng, L):
    """The valve snapping open, from real recordings: the real burst of air, part of it rung in the pipe, over a slice
    of the real valve's rush that dies in 0.12 s."""
    puff = _puff()
    v = ck.get(VALVE)
    a = samples(0.5 + 0.4 * rng.random())
    rush = v[a:a + samples(0.2)] * env([(0, 0), (0.004, 1), (0.12, 0.1), (0.2, 0)], 0.2)
    burst = mix(unit(hp(puff, 500, 2)), unit(hp(rush, 400, 2)) * 0.6)
    return dsp.fit(unit(mix(unit(comb(burst, q=12)), unit(burst) * 0.7)), samples(L))


@recipe(L_, "calliope", "steam", "The steam calliope's note (C5), blown by real steam: a real valve's chiff, a wavering pipe",
        """From real recordings: the packs' real locomotive steam (sfx_100 loop_water_03, its puffs evened out) blowing
        one calliope pipe at C5 (523 Hz; the game pitches it to the tune's notes): the steam's noise rung down at the
        note to a band a couple of hertz wide and the pipe's tone locked onto it, so the real turbulence wavers its
        pitch a few cents and its surges move the level and nudge the pitch, as a real calliope's do; the tone a
        stopped pipe's (odd harmonics strong) with its even ones up a little, reedy; the real steam rung round the
        partials breathing under it, moving with the note when it's pitched. The valve snaps open with a real burst
        of air and a slice of a real valve's rush (sfx_100 air_03, misc_22), part of it rung in the pipe, the twelfth
        sounding for an instant and the note scooping up into tune; the valve closes over 0.3 s, the pitch sagging and
        the tone thinning into the steam's breath. 2.5 s, on the car roof in the night.""",
        sources=[STEAM, PUFF, VALVE], takes=2, lufs=-16)
def calliope_steam(rng, k):
    L = CALL_L
    n = samples(L)
    m = samples(PRE)
    at = (1.6, 4.7)[k % 2]                                 # where in the real steam each take is blown from
    jet = steam_rec(PRE + L, at - PRE)
    # the real steam's surges: its level smoothed under 5 Hz, as a pressure round 1
    e = smooth(np.abs(jet), 5)
    surge = 1 + 0.06 * np.clip((e - np.mean(e)) / (np.std(e) + 1e-9), -2.5, 2.5)
    p = np.concatenate([np.full(m, 1.0, np.float32), pressure(L, OFF + 0.06 * k, 1.0)]) * surge
    semis = BEND * (np.maximum(p, 0.3) - 1)
    semis[:m] = semis[m]                                   # the run-in held at the first pitch: the lock settles unheard
    ph = lock(jet, NOTE * 2 ** (semis / 12)) + rng.uniform(0, 2 * np.pi)
    tone = pipe_tone(ph[m:], p[m:], np.arange(n) / SR, REEDY)
    breath = dsp.resonate(hp(jet, 300, 4), [NOTE * h for h in (1, 2, 3, 5, 7)], q=25, gains=[1, 0.35, 0.5, 0.25, 0.12])[m:]
    hiss = hp(jet, 1500, 2)[m:]
    pr = np.clip(p[m:], 0, None)
    y = mix(unit(tone), (unit(breath) * 0.18 + unit(hiss) * 0.03) * pr ** 0.5, chiff_real(rng, L) * 0.3)
    return on_roof(y)


# ---- wind: the gramophone wound in the guard van -------------------------------------------------------------------------

TEETH = 12                                            # the click's ratchet wheel: teeth a turn of the crank
CABINET = [230, 390, 610, 940, 1420, 2150]            # a small wooden cabinet's panels: the case the motor sits in
CLICKS = S("misc_20", "lock_open_01")                 # a real ratchet and pawl; a real lock's ratchet clicks
CLOCKWORK = S("metal_04")                             # a real small mechanism's ticks (a knurled wheel turning)
METAL_CLICK = R("metalClick")                         # a small real metal click
GEARS = "sfx_100_v2:misc_28"                          # a real mechanism ticking fast and even: the gears' purr
KNOB = R("creak1")[0]                                 # a real wooden creak: the handle's knob on its pin
WIND_ROOM = 29


def crank(rng, k):
    """When each tooth drops: the crank turned a few times (3 on one take, 2 and a quarter on the other), slowing a
    little turn by turn as the spring tightens (rising effort), the hand a touch quicker on the downstroke of each
    turn. [(t, turn), ...] and the take's length."""
    turns, r0, r1 = ((3.0, 1.45, 1.2), (2.25, 1.6, 1.38))[k % 2]
    ph0 = rng.uniform(0, 2 * np.pi)
    out, t, th = [], 0.03, 0.0
    while th < turns - 1e-6:
        r = (r0 + (r1 - r0) * th / turns) * (1 + 0.07 * np.sin(2 * np.pi * th + ph0))
        t += (1.0 / TEETH) / r * (1 + rng.normal(0, 0.01))
        th += 1.0 / TEETH
        out.append((t, th))
    return out, t


def in_cabinet(y):
    """In the gramophone's small wooden cabinet (its panels ringing a little round the works), then the guard van round
    that; the squish under 150 Hz cut."""
    y = mix(y, dsp.resonate(y, CABINET, q=7, gains=[0.5, 0.45, 0.4, 0.35, 0.25, 0.2]) * 0.3)
    y = ck.space(y, 0.09, 0.12, 6000, ((0.0012, 0.5), (0.0027, 0.35)))
    y = dsp.room(y, "car", wet=0.08, rng=np.random.default_rng(WIND_ROOM))
    return hp(y, 150, 4)


def winding(rng, k, tooth, purr, knob, settle):
    """The crank turned: a tooth at each drop (even, a hair of level spread), the gears' purr under it at the crank's
    speed, the knob creaking at the hard part of each turn (a little louder each turn: the effort rising), and once the
    hand stops, the spring taking up against the pawl."""
    drops, end = crank(rng, k)
    parts = [(t, tooth(rng, k, i), rng.uniform(-1.0, 1.0) + 1.5 * (th / drops[-1][1])) for i, (t, th) in enumerate(drops)]
    parts.append((0.0, purr(rng, end + 0.02), -20))
    whole = int(round(drops[-1][1]))
    for j in range(max(1, whole)):
        hard = [t for t, th in drops if abs(th - (j + 0.6)) < 0.5 / TEETH]
        if hard:
            parts.append((hard[0], knob(rng), -20 + 3 * j))
    parts.append((end + rng.uniform(0.07, 0.1), settle(rng), 1))
    return in_cabinet(ck.place(parts, end + 0.3))


# The real candidate's parts: single teeth cut from the packs' real ratchet, lock and clockwork.

@functools.lru_cache(maxsize=1)
def _teeth():
    """A bank of single teeth: the real ratchet's (sfx_100 misc_20), three of the lock's ratchet clicks (lock_open_01)
    and a small real metal click, each cut to its hit, the rumble cut, choked at 25 ms so they land evenly, and matched
    to one another's balance (crew_kit.matched) so they sound like one pawl on one wheel."""
    bank = [hit_of(CLICKS[0], 0, 0.04, -20)] + [hit_of(CLICKS[1], i, 0.04, -20) for i in (1, 2, 3)]
    bank += [hit_of(METAL_CLICK[0], 0, 0.04, -20)]
    return ck.matched([unit(ck.choke(hp(x, 400, 2), 0.012, 0.01)) for x in bank])


TRIOS = ((0, 2, 3), (1, 2, 4))      # each take's three real teeth: one ratchet, never the same click twice running


def tooth_rec(rng, k, i):
    """One tooth, from real recordings: the pawl dropping over it, one of the take's three real teeth (so it sounds
    like one ratchet without a machine gun's sameness), a hair of pitch spread."""
    trio = TRIOS[k % 2]
    x = _teeth()[trio[(i + int(rng.integers(1, 3))) % 3]]
    return dsp.vari(x, rng.uniform(-0.5, 0.0))


def purr_rec(rng, L):
    """The gears under the click (the crank's bevel driving the spring's arbor): a slice of the real mechanism's fast
    ticking (sfx_100 misc_28) pitched down for bigger wheels, its top rolled off."""
    n = samples(L)
    x = ck.get(GEARS)
    g = dsp.vari(x[samples(0.05 + 0.2 * rng.random()):samples(1.7)], -3.0)
    while len(g) < n:              # the even stretch run on again (a 50 ms crossfade) for a long take
        j = samples(0.05)
        w = np.linspace(0, 1, j, dtype=np.float32)
        g = np.concatenate([g[:-j], g[-j:] * (1 - w) + g[:j] * w, g[j:]])
    g = lp(hp(g[:n], 300, 2), 2500, 2) * env([(0, 0), (0.08, 1), (L - 0.08, 1), (L, 0)], L)
    return unit(g)


@functools.lru_cache(maxsize=1)
def _knob():
    """The real wooden creak's first squeak, 0.14 s."""
    return hit_of(KNOB, 0, 0.14, -20)


def knob_rec(rng):
    """The handle's knob creaking on its pin at the hard part of a turn: the real creak's squeak, pitched up for a small
    turned-wood knob."""
    return unit(hp(dsp.vari(_knob(), rng.uniform(2, 4)), 500, 2))


def settle_rec(rng):
    """The spring taking up against the pawl once the hand lets go: the real ratchet's heavier second tooth, a little
    lower, and a tiny real tick after."""
    x = dsp.vari(hit_of(CLICKS[0], 1, 0.05, -26), -2.0)
    y = dsp.vari(hit_of(CLOCKWORK[0], 0, 0.03, -20), 1.0)
    return ck.place([(0, unit(hp(x, 300, 2)), 0), (0.018, unit(hp(y, 600, 2)), -10)])


@recipe(L_, "wind", "ratchet", "The gramophone wound: the crank's real ratchet clicking evenly, the spring tightening",
        """From real recordings: a portable gramophone in the guard van wound by its crank, a few turns (three on one
        take, two and a quarter on the other). Its click, the pawl over the twelve-tooth ratchet wheel on the winding
        arbor, ticks evenly at each tooth: single teeth cut from the packs' real ratchet and pawl (sfx_100 misc_20),
        a real lock's ratchet clicks (lock_open_01) and a small real metal click, choked so they land even. Under them
        the gears' purr (a real fast mechanism's ticking, misc_28, pitched down for bigger wheels). The crank slows a
        little turn by turn as the mainspring tightens, the hand quicker on each downstroke, and the handle's knob
        creaks on its pin at the hard part of each turn, a little more every time (a real wooden creak pitched up);
        when the hand stops, the spring takes up against the pawl with a last, heavier click. In the gramophone's small
        wooden cabinet (its panels ringing round the works), the van round it. 1.5-2.4 s, nothing under 150 Hz.""",
        sources=CLICKS + METAL_CLICK + CLOCKWORK + [GEARS, KNOB], takes=2, lufs=-22)
def wind_ratchet(rng, k):
    return winding(rng, k, tooth_rec, purr_rec, knob_rec, settle_rec)


# The modelled candidate's parts.

PAWL = [2900, 4700, 6900]        # a small steel pawl and wheel tooth: its modes
GEAR = [1700, 2600, 3900]        # a bevel gear's tooth knocked in mesh
SPRING = [620, 1340, 2280, 3500] # the mainspring's coils sliding in their barrel


def snap(rng, body, q, length, raw=0.08, top=9000):
    """A hard contact rung through small steel modes: half a millisecond of noise into the modes (a little of the raw
    contact left), dying over `length`, the very top taken off so it ticks rather than spits."""
    n = samples(length)
    x = np.zeros(n, np.float32)
    j = samples(0.0005)
    x[:j] = rng.standard_normal(j) * np.linspace(1, 0, j)
    y = dsp.resonate(x, body, q=q, gains=[1.0, 0.55, 0.3][:len(body)]) + x * raw
    return lp(y * np.exp(-np.arange(n) / SR / (length / 5)), top, 2)


def tooth_model(rng, k, i):
    """One tooth, modelled: the pawl riding up the tooth's flank (a faint scrape rising), then snapping off its edge
    onto the next (a hard tick through small steel modes, and the wheel's arbor knocked under it)."""
    body = [m * rng.uniform(0.985, 1.015) for m in PAWL]
    tick = snap(rng, body, 18, 0.025)
    knock = snap(rng, [1100 * rng.uniform(0.97, 1.03), 1750], 6, 0.02, raw=0.0, top=6000)
    ride = bp(synth.noise(0.03, rng), 2500, 7000) * env([(0, 0), (0.026, 1), (0.03, 0)], 0.03) ** 2
    return ck.place([(0, unit(ride), -24), (0.03, unit(tick), 0), (0.0305, unit(knock), -9)])


def purr_model(rng, L):
    """The gears under the click, modelled: a bevel gear's teeth in mesh, five to a ratchet tooth, each a small knock
    through its modes, a little uneven."""
    n = samples(L)
    out = np.zeros(n + samples(0.03), np.float32)
    t = 0.0
    while t < L:
        body = [m * rng.uniform(0.95, 1.05) for m in GEAR]
        c = synth.click(body[0], q=10, length=0.008, rng=rng, body=body) * rng.uniform(0.4, 1.0)
        a = samples(t)
        out[a:a + len(c)] += c[:len(out) - a]
        t += rng.uniform(0.85, 1.15) / 80
    return unit(out[:n] * env([(0, 0), (0.08, 1), (L - 0.08, 1), (L, 0)], L))


def knob_model(rng):
    """The handle's knob creaking on its pin, modelled: a short stick-slip through a small turned-wood knob's modes."""
    L = rng.uniform(0.1, 0.14)
    body = [m * 2.1 for m in synth.WOOD[:4]]
    return unit(hp(synth.creak(L, env([(0, 140), (L, 220)], L), rng, body=body, q=14, jitter=0.3,
                               shape=env([(0, 0), (0.02, 1), (L, 0)], L)), 500, 2))


def settle_model(rng):
    """The spring taking up against the pawl: a heavier tick, the coils shifting in the barrel after it."""
    tick = synth.click(2300, q=12, length=0.03, rng=rng, body=[2300, 3700, 5600])
    coils = synth.creak(0.08, 90, rng, body=SPRING, q=10, jitter=0.4, shape=env([(0, 1), (0.08, 0)], 0.08))
    return ck.place([(0, unit(tick), 0), (0.006, unit(coils), -14)])


@recipe(L_, "wind", "pawl", "The gramophone wound, modelled: a steel pawl over a ratchet wheel, gears in mesh, a wooden case",
        """Modelled: the gramophone's crank turned a few times (three turns, and two and a quarter), its click a small
        steel pawl over a twelve-tooth ratchet wheel: at each tooth the pawl rides up the flank (a faint rising
        scrape) and snaps off its edge onto the next (a hard tick through small steel modes, the arbor knocked under
        it), evenly; under it a bevel gear's teeth in mesh, five to a click. The crank slows a little turn by turn as
        the mainspring tightens, the hand quicker on each downstroke, the handle's knob creaking on its pin at the hard
        part of each turn, louder as the effort rises (stick-slip through a small turned-wood knob); the hand stops and
        the spring takes up against the pawl with a heavier tick, its coils shifting in the barrel. The same small
        wooden cabinet and guard van as 'ratchet'. 1.5-2.4 s, nothing under 150 Hz.""", takes=2, lufs=-22)
def wind_pawl(rng, k):
    return winding(rng, k, tooth_model, purr_model, knob_model, settle_model)
