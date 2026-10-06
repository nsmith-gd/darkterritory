"""Music while the crew is at work (decided 1 Oct): low, background, ambient, almost a drone. It never competes with the
soundscape: it sits under everything at its own bottom tier (T7), gives way to everything, and stays out of the voice
band (300 Hz-3.4 kHz) and the tell bands above it. So it lives in the sub and low bass, under 250 Hz, and moves slowly.

Every loop here is built from motions whose periods divide the loop's length, so it comes round seamlessly.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, env

L = 96.0     # a loop: long enough that nobody hears it come round


def _osc(f, n, phase=0.0, detune_hz=0.0):
    t = np.arange(n) / dsp.SR
    return np.sin(2 * np.pi * (f + detune_hz) * t + phase).astype(np.float32)


def _lfo(n, cycles, phase=0.0):
    """A slow swell that repeats a whole number of times in the loop (0..1)."""
    t = np.arange(n) / n
    return (0.5 - 0.5 * np.cos(2 * np.pi * cycles * t + phase)).astype(np.float32)


def drone_ground(rng, length=L):
    """D and A in the sub bass, each a pair a fraction of a hertz apart so they beat slowly, with a low E flat that
    swells in twice a loop and grinds against the D (a minor second: unease without a melody)."""
    n = samples(length)
    y = np.zeros(n, np.float32)
    k = 1.0 / length   # a beat rate that comes round in the loop
    for f, g in ((36.71, 1.0), (55.0, 0.55), (73.42, 0.35)):
        y += g * (_osc(f, n) + _osc(f, n, rng.uniform(0, 6.28), detune_hz=3 * k)) * 0.5
    eb = 77.78
    y += 0.3 * _osc(eb, n) * _lfo(n, 2, rng.uniform(0, 6.28)) ** 2
    # air: a faint rumble under it, breathing
    rum = lp(synth.noise(length, rng, "brown"), 90, 2) * (0.6 + 0.4 * _lfo(n, 3))
    y += 0.25 * rum / (np.max(np.abs(rum)) + 1e-9)
    y *= 0.75 + 0.25 * _lfo(n, 4)
    return lp(y, 220, 4)


@recipe("ui-music", "drone", "ground", "A sub-bass drone: D and A beating slowly, an E flat swelling in against them",
        """Sine pairs on D and A in the sub bass, each pair a fraction of a hertz apart so they beat slowly, a low E flat
        that swells in twice a loop against the D (a minor second, unease without a tune), over a faint breathing rumble.
        Nothing above 220 Hz, so it never touches a voice or a tell. A 96 s loop.""",
        takes=1, loop=True, lufs=-30)
def ground(rng, k):
    return drone_ground(rng)


@recipe("ui-music", "drone", "rails", "The rails humming at night: a low bowed-iron drone that breathes",
        """Noise bowed through the resonances of a long iron rail tuned low (55, 82 and 110 Hz and their bent overtones),
        each mode swelling at its own slow rate, so the drone shifts colour without moving pitch. Lowpassed at 250 Hz.
        A 96 s loop.""",
        takes=1, loop=True, lufs=-30)
def rails(rng, k):
    n = samples(L)
    exc = synth.noise(L, rng, "pink")
    modes = [55.0, 82.4, 110.0, 146.8, 164.8, 220.0]
    y = np.zeros(n, np.float32)
    for i, f in enumerate(modes):
        r = dsp.resonate(exc, [f * (1 + 0.003 * i)], q=180)
        r = r / (np.max(np.abs(r)) + 1e-9)
        y += r * (0.35 + 0.65 * _lfo(n, 1 + i, rng.uniform(0, 6.28))) / (1 + 0.4 * i)
    return lp(hp(y, 30, 2), 250, 4)


@recipe("ui-music", "drone", "descent", "A low bowed line falling D, C, B flat, A, so slowly it's almost a drone",
        """A bowed bass (a sawtooth's warmth through a soft lowpass, a slow vibrato, the bow's pressure swelling) on the
        old descending lament, D-C-B flat-A, one note every 24 s with long crossfades, over the sub D. The fall is there
        if you listen and gone if you don't. Under 250 Hz. A 96 s loop.""",
        takes=1, loop=True, lufs=-30)
def descent(rng, k):
    n = samples(L)
    notes = [73.42, 65.41, 58.27, 55.0]   # D2 C2 Bb1 A1
    seg = L / len(notes)
    y = np.zeros(n, np.float32)
    for i, f in enumerate(notes):
        t = np.arange(n) / dsp.SR
        # each note's window, round the loop, with long crossfades
        c = (t - i * seg) % L
        w = np.clip(np.minimum(c / 6.0, (seg + 6.0 - c) / 6.0), 0, 1).astype(np.float32)
        vib = 1 + 0.004 * np.sin(2 * np.pi * 4.6 * t + i)
        ph = np.cumsum(f * vib) / dsp.SR
        saw = (2 * (ph % 1.0) - 1).astype(np.float32)
        y += lp(saw, 180, 2) * w
    sub = _osc(36.71, n) * 0.6
    y = y / (np.max(np.abs(y)) + 1e-9) + sub
    return lp(y, 250, 4) * (0.8 + 0.2 * _lfo(n, 8)).astype(np.float32)
