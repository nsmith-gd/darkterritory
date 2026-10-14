"""The Track Doll's restlessness heard (queue #236, note 499; note 268's "not yet": "the doll has no recorded 'restless'
sound of her own (the faster giggle and the crew's brake handle stand in)"; GDD App. F.1). For warnSeconds (30) before each
of her stages she's restless: left alone too long, about to get worse. Her giggle came twice as often, and that was all;
restless at stage 2 she rattled the brake handle with the crew's own lever sound.

- Restless (`cs-track-doll.restless`): now and then while she is. Her porcelain heels drumming on the woodwork the way a
  child kicks a bench, impatient; a tuneless hum through her teeth; her head turning on its joint with a porcelain tick.
  The same doll as her giggle and her pleased 'heh': a child's throat played an octave up into a hollow porcelain head's
  3-6 kHz, glaze ticks, a door creak two octaves up for her joints.
- Her rattle (`cs-track-doll.rattle`): at stage 2, on the beat she'll take the brake, a small hand shaking the brake
  valve's brass handle in its detent without turning it: quick light squeaks and the detent clicking out and back, her
  fingertips ticking on the brass. The crew's handle (crew_train.py's brake-handle), shaken, not turned.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp
from recipes import crew_kit as ck
from recipes.boarders import porcelain as sung, glaze_tick, grain, settle, TOY_WOOD, GLAZE, JOINT as JOINTS
from recipes.tells import porcelain as head_modes, joints, ring, burst, held, DOLL, JOINT
from recipes.crew_train import latch, cab, LATCHES


def heel(rng, head):
    """A porcelain heel kicked back against a crate or a bench: a small hollow wooden knock and the glaze's tick on it."""
    b = Bus(0.4)
    b.at(0, ck.norm(grain(rng, TOY_WOOD[int(rng.integers(5))], 0.1, rng.uniform(2, 6))) * 0.8)
    b.at(0.002, ck.norm(ring(burst(rng, 0.0004, attack=0.05), head, rng.uniform(600, 1000), tail=0.25,
                             gains=[0.5, 1, 0.6, 0.7, 0.3])) * 0.35)
    return b.x


def drumming(rng, head, n, rate, accel=1.0):
    """Heels drumming in pairs, impatient: `n` kicks at about `rate` a second, quickening by `accel`."""
    b = Bus(n / rate + 0.6)
    t = 0.0
    for i in range(n):
        b.at(t, heel(rng, head), rng.uniform(-4, 0) - (2 if i % 2 else 0))
        t += rng.uniform(0.85, 1.15) / rate / (1 + (accel - 1) * i / max(n - 1, 1))
    return b.x


def hum(rng, notes, length):
    """A tuneless hum through her teeth: a few porcelain 'mm' notes that wander and don't resolve."""
    b = Bus(length + 0.4)
    t = 0.0
    for i, f in enumerate(notes):
        L = round(length / len(notes) * rng.uniform(0.85, 1.1), 2)    # whole hundredths: the sung syllable's halving stays exact
        b.at(t, sung(rng, f, L, [(0, "m"), (0.05, "m"), (L * 2, "m")], sing=True), rng.uniform(-4, 0))
        t += L * rng.uniform(0.95, 1.05)
    return b.x


@recipe("cs-track-doll", "restless", "heels",
        "The doll restless: porcelain heels drumming on the woodwork, a tuneless hum through her teeth, her head turning",
        """Left alone too long, her next stage coming (note 268): now and then while she's restless. Her heels drumming
        against a crate or a bench the way a child kicks, impatient, quickening (small hollow wooden knocks with the
        glaze's tick on each); a tuneless hum through her teeth, a few porcelain 'mm's that wander and don't resolve (her
        voice: a child's throat played an octave up and rung through the hollow head); her head turning on its joint (a
        door creak two octaves up) with a porcelain tick. In a wooden car's room. Four takes, different moods.""",
        sources=TOY_WOOD + GLAZE + JOINTS + [JOINT], takes=4, lufs=-23)
def restless(rng, k):
    head = head_modes(rng)
    b = Bus(4.2)
    if k in (0, 2, 3):
        b.at(0.0, drumming(rng, head, (8, 6, 12)[[0, 2, 3].index(k)], rng.uniform(5.5, 7.0), accel=1.4 if k == 3 else 1.0), -2)
    if k in (0, 1):
        f = rng.uniform(560, 620)
        notes = [f, f * 2 ** (rng.choice([-1, 1, 2]) / 12), f * 2 ** (rng.choice([-3, -2]) / 12)]
        b.at(0.4 if k == 0 else 0.1, hum(rng, notes, rng.uniform(1.2, 1.6)), -6)
    if k in (1, 2):
        t = 1.4 if k == 2 else 1.9
        b.at(t, joints(rng), -9)
        b.at(t + 0.12, ck.norm(ring(burst(rng, 0.0003, attack=0.05), head, rng.uniform(700, 1200), tail=0.4,
                                    gains=[0.4, 1, 0.6, 0.8, 0.3])), -10)
    if k == 3:     # quickening and stopping dead: she's had enough
        b.at(2.3, glaze_tick(rng), -12)
    y = dsp.room(b.x, "car", wet=0.28, rng=rng)
    return settle(y)


@recipe("cs-track-doll", "rattle", "brass",
        "Her small hand shaking the brake handle without turning it: quick brass squeaks, the detent clicking, her fingertips",
        """At stage 2, restless, on the beat she'll take the brake (note 268): the brake valve's brass handle shaken in its
        detent, not turned. Quick light squeaks of brass on brass (the crew's handle, a fast stick-slip through a small
        metal body, shorter and higher), the detent clicking out and back in (the packs' latch, a little brighter),
        five to eight in half a second, her porcelain fingertips ticking on the brass. Smaller and quicker than a hand
        that means it. In the cab's iron room. Three takes.""", sources=LATCHES + GLAZE, takes=3, lufs=-23)
def rattle(rng, k):
    events = []
    t = 0.0
    for i in range(int(rng.integers(5, 9))):
        L = rng.uniform(0.03, 0.05)
        sq = synth.creak(L, rng.uniform(500, 800), rng, body=[1700, 3300, 4900], q=25) * env([(0, 0), (0.01, 1), (L, 0)], L)
        events.append((t, sq, -12 - 2 * (i % 2)))
        if i % 2 == 1 or rng.random() < 0.3:
            events.append((t + L * 0.8, latch(rng, k + i, rng.uniform(2, 5)), -4 - rng.uniform(0, 4)))
        if rng.random() < 0.4:
            events.append((t + rng.uniform(0, 0.02), glaze_tick(rng), -16))
        t += rng.uniform(0.05, 0.09)
    return cab(ck.place(events, t + 0.4))
