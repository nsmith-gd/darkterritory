"""The rail joints' clack (queue #167, note 431): new candidates for bed-wheel-rail.joint beside world_bed's `clack`.

The joint is fired per axle every 12 m under every car near the ear, the sound heard most in the game. world_bed's take is
a 1.6 s thunk centred at 280-420 Hz: a 70 Hz knock, the packs' plates pitched down and the night's tail on it. Fired
twenty times a rail length at speed, those pile into a low rumble instead of the click-clack.

- `crack`: the wheel's tread hitting the far rail's end. The contact itself is a crack (a fraction of a millisecond), with the rail's short ring from 800 Hz up and the wheel's damped tread modes. Under it are the
  packs' real heavy metal hit pitched only a little down, and a short, light thump of the truck. Most takes have the small tick
  of the wheel leaving the near rail end first. Half a second, and the night's tail kept short.
- `battered`: the same on worn, dipped rail ends (the Territory's track is run down). The wheel drops into the dip with
  a dull clunk, then cracks onto the far end a beat after: clunk-clack, heavier and a little looser.

Both are crisper than `clack` but kept under 2 kHz: spec A.4 rule 1 gives the tells 2-6 kHz, the bed the low mids, and
the joint is rhythmic, the other thing a tell is told by. Both preview as the game fires them (world_bed._clickclack: two axles a truck, truck after truck at 15 m/s).
"""

import numpy as np

import dsp
from build import recipe
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm
from recipes.world_bed import WHEEL, JOINT_CLANGS, _clickclack

SR = dsp.SR
CLANGS = ["kenney_impact-sounds:" + k for k in JOINT_CLANGS]


def crack(rng, bright=1.0):
    """The contact: a fraction of a millisecond of steel on steel, every band at once, gone in a few ms."""
    n = samples(0.03)
    k = samples(rng.uniform(0.00015, 0.0003))
    x = np.zeros(n, np.float32)
    x[:k] = rng.standard_normal(k) * np.hanning(k)
    x = bp(x, 700 * bright, 2400, 2) * env([(0, 1), (0.006, 0.2), (0.03, 0)], 0.03, "exp")[:n]
    return norm(x)


def ring(rng, length=0.5, rail=(820, 980), decay=(0.025, 0.04)):
    """The rail's short ring (a steel bar pinned between sleepers) and the wheel's tread modes, both damped by the load."""
    r = W.body(rng, rng.uniform(*rail), W.BAR, decay=rng.uniform(*decay), contact=0.0002, length=length)
    w = W.body(rng, WHEEL[1] * rng.uniform(0.96, 1.04), [f / WHEEL[1] for f in WHEEL[1:]], decay=0.02, damp=0.4,
                contact=0.0002, length=length)
    return norm(r) * 0.6 + norm(w) * 0.4


def tail(y, rng, length=0.5):
    """Close to the train in the open: two slaps off the cars' sides, a short night after, and out by `length`. Kept
    under 2 kHz (spec A.4 rule 1: the bed stays out of the tells' 2-6 kHz, and this one's rhythmic)."""
    y = lp(W.space(y, rng, "night", wet=0.05), 1900, 4)
    return fit(y, samples(length)) * fit(env([(0, 1), (length * 0.6, 1), (length, 0)], length), samples(length))


@recipe("bed-wheel-rail", "joint", "crack",
        "One axle over a rail joint: the wheel's tread cracking onto the next rail end, short and hard",
        """The wheel's tread hitting the far rail's end, built for the click-clack heard at speed. The contact is a
        crack, a fraction of a millisecond of steel on steel. The rail's short ring (a steel bar's
        modes from 800 Hz up) and the wheel's damped tread modes come with it, and under them the packs' heavy metal hit
        pitched only a little down and choked at 60 ms, with a short, light thump of the truck (the rolling loops carry the weight). Most takes have the tick
        of the wheel leaving the near rail end a few ms first. Half a second with a short tail, so twenty a rail length
        stay clicks; all of it under 2 kHz, out of the tells' band (spec A.4).""",
        sources=CLANGS, takes=6, lufs=-27, preview=lambda takes, rng: _clickclack(takes, rng))
def joint_crack(rng, k):
    L = 0.5
    hit = W.rec(CLANGS[k], semis=-rng.uniform(1, 2.5), lo=180, hi=7000, tau=0.06)
    y = mix(crack(rng) * 0.6, fit(ring(rng, L), samples(L)) * 0.5, fit(norm(hit), samples(L)) * 0.5,
            fit(W.knock(rng, 110, 0.08, drop=0.3), samples(L)) * 0.14, length=samples(L))
    if rng.random() < 0.6:
        lead = samples(rng.uniform(0.006, 0.014))
        tick = W.body(rng, rng.uniform(1100, 1400), W.BAR, decay=0.012, contact=0.0002, length=0.06) * 0.3
        y = mix(np.concatenate([np.zeros(lead, np.float32), y])[:samples(L)], tick, length=samples(L))
    return tail(hp(y, 90), rng, L)


@recipe("bed-wheel-rail", "joint", "battered",
        "One axle over a worn rail joint: the wheel dropping into the dip with a clunk, then cracking onto the far end",
        """The same joint on run-down track whose rail ends are battered into a dip. The wheel drops into it with a
        dull clunk: the packs' heavy hit pitched down and choked, and the truck's thump. A beat later (15-25 ms) it
        cracks onto the far end: the hard contact, the rail's short ring and the wheel's tread. Clunk-clack, heavier and
        looser than a sound joint, every take's dip a little different. Half a second with a short tail.""",
        sources=CLANGS, takes=6, lufs=-27, preview=lambda takes, rng: _clickclack(takes, rng))
def joint_battered(rng, k):
    L = 0.5
    clunk_hit = W.rec(CLANGS[(k + 2) % len(CLANGS)], semis=-rng.uniform(4, 6), lo=120, hi=3500, tau=0.035)
    clunk = mix(fit(norm(clunk_hit), samples(L)) * 0.6, fit(W.knock(rng, 95, 0.1, drop=0.4), samples(L)) * 0.2,
                length=samples(L))
    hit = W.rec(CLANGS[k], semis=-rng.uniform(1, 2.5), lo=180, hi=7000, tau=0.05)
    clack = mix(crack(rng, 0.9) * 0.6, fit(ring(rng, L), samples(L)) * 0.45, fit(norm(hit), samples(L)) * 0.5,
                length=samples(L))
    gap = samples(rng.uniform(0.015, 0.025))
    y = mix(clunk * 0.7, np.concatenate([np.zeros(gap, np.float32), clack])[:samples(L)], length=samples(L))
    return tail(hp(y, 70), rng, L)
