"""The townsfolk heard on their rounds (queue #182, note 446; B2's #389, note 353: towns that are lived in). A walled
town's people walk their rounds and breathe through their gear, and the watch walk the wall with their lanterns. Their
footsteps are the crew's own (crew-footsteps.walk by what they walk on). These are the rest:

- Their breathing gear, heard close to (place-town.gear-*), one loop a kind, two slow breaths:
  - respirator: a rubber half-mask with two filter cans. The breath in is drawn muffled through the filters, its valve
    ticking open; the breath out flaps the rubber exhalation valve, which buzzes and claps shut.
  - oxygen: an amber cup on a hose from a bottle on the back. A thin steady hiss of gas, and the breath hollow in the cup.
  - rebreather: a mine-rescue set, its bag on the chest. Breathed through a mouthpiece with a valve's click either way,
    the rubber bag crinkling as it fills and empties.
  - wrap: a wool wrap with a tin can sewn into it. The breath thick through the wool, the can ringing faintly hollow.
- The watch's lantern (place-town.lantern): carried on their rounds of the wall, the bail squeaking in its ears with
  each swing of a step, the glass chimney ticking in its frame, the flame fluttering.

Breaths are heard_foley's modelled breath (noise through a throat and mouth), the stand-in until recorded ones.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe
from recipes.heard_foley import breath

L = "place-town"
SR = dsp.SR
GEAR = 8.0       # two breaths of someone at rest, in and out
LANTERN = 4.8    # four steps of the watch's pace, a swing a step


def breaths(rng, n, inhale, exhale, gap=(0.5, 0.9)):
    """A loop's breaths: in, out, a rest, and again; `inhale` and `exhale` make one (length) -> sound."""
    parts = []
    t = rng.uniform(0.1, 0.4)
    while t < n / SR - 3.4:
        i_len, o_len = rng.uniform(1.0, 1.4), rng.uniform(1.3, 1.8)
        parts.append((t, swell(inhale(i_len), 0.5), 1.0))
        parts.append((t + i_len + rng.uniform(0.05, 0.2), swell(exhale(o_len), 0.35), 1.0))
        t += i_len + o_len + rng.uniform(*gap)
    return W.place(n, parts)


def swell(x, peak):
    """A breath's own rise and fall over it (air doesn't start or stop square): up to its peak `peak` of the way in, a
    sine's shoulders either side, so the edges fade rather than cut. Valve clicks ride over it."""
    n = len(x)
    u = np.arange(n) / max(n - 1, 1)
    w = np.where(u < peak, np.sin(np.pi / 2 * u / peak), np.cos(np.pi / 2 * (u - peak) / (1 - peak))) ** 1.5
    return (x * w).astype(np.float32)


def valve_click(rng, f=(1800, 2600), level=1.0):
    """A small rubber flapper valve seating or lifting: a tick through a little hollow."""
    return synth.click(rng.uniform(*f), q=rng.uniform(4, 8), length=0.02, rng=rng) * level


def in_mask(y, f=(700, 1500)):
    """A breath heard through a mask's rubber: its highs gone, a hollow cup's resonance on what's left."""
    return mix(lp(y, 2200), dsp.resonate(y, list(f), q=6) * 0.15)


@recipe(L, "gear-respirator", "valves", "Breathing through a rubber respirator: drawn through its filters, the valve flapping out",
        """Someone breathing at rest through a rubber half-mask with two filter cans: the breath in drawn muffled and a
        little laboured through the filters (a modelled breath through a mask's rubber cup), the inhalation valves
        ticking open; the breath out lifts the exhalation valve, whose rubber flap buzzes in the stream and claps shut at
        the end. Two breaths, an 8 s loop.""", loop=True, takes=1, lufs=-30, seconds=GEAR)
def gear_respirator(rng, k):
    n = samples(GEAR)

    def inhale(length):
        b = in_mask(breath(rng, length, [(0, "u"), (length, "u")], [(0, 0), (0.25, 0.8), (length * 0.8, 1), (length, 0)],
                           teeth=0.15, flutter=0.15))
        return mix(b, fit(valve_click(rng), len(b)) * 0.5, length=len(b))

    def exhale(length):
        m = samples(length)
        b = in_mask(breath(rng, length, [(0, "o"), (length, "u")], [(0, 0), (0.08, 1), (length * 0.6, 0.6), (length, 0)],
                           flutter=0.2))
        # The flap buzzing in the stream: a fast flutter on the breath, gone as the stream dies.
        rate = rng.uniform(55, 75)
        buzz = 1 + 0.6 * np.sin(2 * np.pi * rate * np.arange(m) / SR) * fit(env([(0, 1), (length * 0.6, 0.5), (length, 0)], length), m)
        shut = np.zeros(m, np.float32)
        c = valve_click(rng, (1200, 1800), 1.2)
        shut[m - len(c):] = c
        return mix(b * buzz.astype(np.float32), shut * 0.7, length=m)

    y = breaths(rng, n, inhale, exhale)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 4)))


@recipe(L, "gear-oxygen", "cup", "Breathing in an oxygen cup: the gas hissing thin and steady, the breath hollow in the cup",
        """Someone breathing through an amber cup on a hose from an oxygen bottle on their back: a thin, steady hiss of
        gas at the cup (high, narrow, never stopping), and their breath hollow inside it (a breath rung through a small
        cup's resonance), the hose's rubber creaking now and then as they move. Two breaths, an 8 s loop.""",
        loop=True, takes=1, lufs=-31, seconds=GEAR)
def gear_oxygen(rng, k):
    n = samples(GEAR)
    hiss = W.norm(W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * ((f - 5200) / 1400) ** 2))) * 0.12

    def inhale(length):
        return in_mask(breath(rng, length, [(0, "u"), (length, "o")], [(0, 0), (0.3, 1), (length, 0)], flutter=0.1), (900, 1900))

    def exhale(length):
        return in_mask(breath(rng, length, [(0, "o"), (length, "o")], [(0, 0), (0.1, 1), (length, 0)], flutter=0.15), (900, 1900))

    body = breaths(rng, n, inhale, exhale)
    creaks = W.place(n, [(t, synth.creak(0.25, 30, rng, body=synth.TIN, q=10) * 0.15, 1.0) for t in W.poisson(GEAR, 0.25, rng)])
    y = mix(W.norm(body) * 0.8, hiss, W.norm(creaks) * 0.1 if np.any(creaks) else creaks)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 9000) ** 4)))


@recipe(L, "gear-rebreather", "bag", "Breathing on a mine-rescue rebreather: the mouthpiece's valves clicking, the rubber bag crinkling",
        """Someone breathing on a mine-rescue set: through a mouthpiece, its valves clicking either way, the breath dry
        and close; the rubber breathing bag on their chest crinkling and creaking as it fills on the breath out and
        empties on the breath in. Two breaths, an 8 s loop.""", loop=True, takes=1, lufs=-30, seconds=GEAR)
def gear_rebreather(rng, k):
    n = samples(GEAR)

    def bag(length):
        m = samples(length)
        crinkle = synth.crackle(length, env([(0, 40), (length * 0.5, 90), (length, 20)], length), rng, size=(0.0004, 0.002), hi=2500)
        rub = synth.creak(length, rng.uniform(12, 20), rng, body=[f * 0.6 for f in synth.TIN], q=8, jitter=0.5)
        return fit(mix(ck.norm(crinkle) * 0.5, ck.norm(rub) * 0.25), m)

    def inhale(length):
        b = lp(breath(rng, length, [(0, "u"), (length, "u")], [(0, 0), (0.2, 1), (length, 0)], flutter=0.1), 3500)
        return mix(b, bag(length) * 0.6, fit(valve_click(rng, (2200, 3000)), len(b)) * 0.6, length=len(b))

    def exhale(length):
        b = lp(breath(rng, length, [(0, "o"), (length, "u")], [(0, 0), (0.1, 1), (length, 0)], flutter=0.12), 3500)
        return mix(b, bag(length) * 0.8, fit(valve_click(rng, (1600, 2200)), len(b)) * 0.6, length=len(b))

    y = breaths(rng, n, inhale, exhale)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 7500) ** 4)))


@recipe(L, "gear-wrap", "can", "Breathing through a wool wrap with a tin can sewn in: thick and muffled, the can ringing hollow",
        """Someone breathing through a wool wrap with a tin can sewn into it as a filter: the breath thick and muffled
        through the wool (only its lows left), and the can ringing faintly hollow on each breath (a can's low modes
        rung by the stream). Two breaths, an 8 s loop.""", loop=True, takes=1, lufs=-31, seconds=GEAR)
def gear_wrap(rng, k):
    n = samples(GEAR)
    can = [f * rng.uniform(0.9, 1.1) for f in (620, 1450, 2380)]

    def through(length, vowels, shape):
        b = breath(rng, length, vowels, shape, flutter=0.25)
        return mix(lp(b, 1100, 2), dsp.resonate(lp(b, 3000), can, q=14) * 0.25)

    def inhale(length):
        return through(length, [(0, "u"), (length, "u")], [(0, 0), (0.3, 1), (length, 0)])

    def exhale(length):
        return through(length, [(0, "o"), (length, "o")], [(0, 0), (0.1, 1), (length, 0)])

    y = breaths(rng, n, inhale, exhale)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 5000) ** 4)))


@recipe(L, "lantern", "bail", "The watch's lantern swinging as they walk: the bail squeaking in its ears, the glass ticking",
        """A hurricane lantern carried on the watch's round of the wall: the wire bail squeaking in its ears with each
        swing of a step (a little dry iron stick-slip, once each way), the glass chimney ticking in its frame at the end
        of each swing, and the flame fluttering behind it. Four steps, a 4.8 s loop.""",
        loop=True, takes=1, lufs=-32, seconds=LANTERN)
def lantern_bail(rng, k):
    n = samples(LANTERN)
    parts = []
    step = LANTERN / 4
    for i in range(4):
        t = i * step + rng.uniform(-0.03, 0.03)
        for half in (0.0, step / 2):
            sq = synth.creak(rng.uniform(0.12, 0.2), rng.uniform(140, 220), rng, body=[f * 2.2 for f in synth.TIN], q=22, jitter=0.15)
            parts.append((t + half, sq * dsp.fade(np.ones(len(sq), np.float32), 0.01, 0.06), rng.uniform(0.4, 0.8)))
            parts.append((t + half + rng.uniform(0.16, 0.22), valve_click(rng, (3000, 4200), 1.0), rng.uniform(0.15, 0.35)))
    swing = W.place(n, parts)
    flame = W.norm(W.cfilter(W.pnoise(n, rng), lambda f: (f / 200) / (1 + (f / 200) ** 2) / (1 + (f / 1800) ** 2))) \
        * np.clip(0.6 + 0.3 * W.slow(n, 3, rng), 0.1, 1.2)
    y = mix(W.norm(swing) * 0.8, flame * 0.12)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 9000) ** 4)))
