"""The radio as a device (its key, squelch and static, not the voices through it), and the banging of a prisoner on a
Holdout's walls (voice-callout's 'bang'; the shouts wait for voice recordings).

The radio's clicks are the packs' real switch and click recordings, pitched for a handset's heavier switch and its
transmit relay; squelch and static are noise as a receiver makes it: band-limited to the set's 300 Hz-3 kHz, bursts that
open and cut, a weak signal fading in and out under atmospheric crackle. The banging is real wood and metal hits heard
through a wall (darkened, the wall's boards booming), in a frantic, uneven rhythm.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter

SR = dsp.SR
RADIO = (300, 3000)


def speaker(x):
    """Through the set's little speaker: 300 Hz-3 kHz, a cone resonance, a touch of grit."""
    y = dsp.peak(bp(x, *RADIO, order=3), 1400, 2.0, 4.0)
    return dsp.saturate(y / (np.max(np.abs(y)) + 1e-9), 4)


def burst(rng, L, cut=0.008):
    """A squelch opening: band-limited noise that starts hard and stops hard."""
    n = samples(L)
    x = rng.standard_normal(n).astype(np.float32)
    x *= env([(0, 0), (0.003, 1), (L - cut, 0.9), (L, 0)], L)[:n]
    return speaker(x)


SWITCHES = [("kenney_ui-audio:switch2", "kenney_ui-audio:switch13"), ("kenney_ui-audio:click3", "kenney_ui-audio:click4"),
            ("kenney_ui-audio:switch27", "kenney_ui-audio:switch28")]


@recipe("voice-radio-sfx", "key-down", "handset",
        "Radio keyed: the handset's press-to-talk switch clicking down, the relay pulling in, the set opening with a hiss",
        """A real switch click from the packs pitched down for a chunky handset button, the transmit relay pulling in a few
        milliseconds behind it (a second, smaller click), and the receiver's squelch opening on the carrier: a short hard
        burst of band-limited noise that drops to a quiet hiss. Three takes, three different switches.""",
        sources=[k for pair in SWITCHES for k in pair], takes=3, lufs=-26)
def key_down(rng, k):
    a, b_ = SWITCHES[k]
    click = W.rec(a, semis=-rng.uniform(3, 5))
    relay = W.rec(b_, semis=-rng.uniform(6, 9)) * 0.5
    b = dsp.Bus(0.6)
    b.at(0, norm(click) * 0.9).at(rng.uniform(0.012, 0.02), norm(relay) * 0.5)
    b.at(0.03, burst(rng, rng.uniform(0.04, 0.07)) * 0.35)
    hiss = speaker(rng.standard_normal(samples(0.25)).astype(np.float32)) * env([(0, 1), (0.25, 0)], 0.25)[:samples(0.25)]
    b.at(0.08, hiss * 0.06)
    return b.x


@recipe("voice-radio-sfx", "key-up", "handset",
        "Radio released: the press-to-talk switch springing back, the relay dropping out",
        """The handset button let go: a real switch click from the packs (a lighter, springier one than the key-down, so
        the two are told apart by ear) and the relay dropping out just after. The squelch tail that follows on the other
        sets is its own cue. Three takes.""", sources=[k for pair in SWITCHES for k in pair], takes=3, lufs=-28)
def key_up(rng, k):
    a, b_ = SWITCHES[(k + 1) % 3]
    click = W.rec(a, semis=-rng.uniform(0, 2))
    relay = W.rec(b_, semis=-rng.uniform(6, 9))
    b = dsp.Bus(0.4)
    b.at(0, norm(click) * 0.8).at(rng.uniform(0.02, 0.035), norm(relay) * 0.35)
    return b.x


@recipe("voice-radio-sfx", "squelch", "tail",
        "Squelch tail after a transmission: a hard burst of radio noise, 'kssht', cut off clean",
        """What a receiver does when the carrier drops: the squelch can't close for a moment, so full noise through the
        set's speaker (band-limited to 300 Hz-3 kHz, a little gritty) for a fifth of a second, then cut. Takes differ in
        length and grain: a short clean one, a long one, one that crackles as it goes.""", takes=3, lufs=-24)
def squelch(rng, k):
    L = (0.14, 0.28, 0.2)[k]
    x = burst(rng, L)
    if k == 2:
        cr = synth.crackle(L, 400, rng, size=(0.0002, 0.001), hi=800)
        x = x + speaker(cr) * 0.4 * env([(0, 0.2), (L, 1)], L)[:len(cr)]
    return x


@recipe("voice-radio-sfx", "static", "weak",
        "Static under a weak signal: a hiss fading in and out, atmospheric crackle, a faint whistle drifting",
        """A receiver straining after a far transmitter: band-limited noise through the set's speaker that swells and sinks
        as the signal fades (slow, irregular), crackling with distant lightning (impulses of all sizes, a few big pops),
        and now and then a faint heterodyne whistle drifting in pitch from some other station. 10 s exact cycle.""",
        loop=True, takes=1, lufs=-26)
def static(rng, k):
    n = samples(10.0)
    t = np.arange(n) / SR
    fade = np.clip(0.6 + 0.35 * slow(n, 0.35, rng) + 0.1 * slow(n, 3, rng), 0.15, None)
    hiss = pnoise(n, rng) * fade
    cr = W.place(n, [(tt, W.body(rng, rng.uniform(800, 2500), W.PLATE, decay=0.002, length=0.01, count=4),
                      min(1.0, 0.05 * rng.pareto(1.5) + 0.05) * 6) for tt in W.poisson(10.0, 25, rng)])
    f = 1100 + 250 * slow(n, 0.1, rng)
    cyc = np.sum(f) / SR
    f *= round(cyc) / cyc
    whistle = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.clip(slow(n, 0.15, rng) - 0.3, 0, None) * 0.25
    y = hiss + cr + whistle
    y = cfilter(y, lambda fr: (fr / 300) ** 3 / (1 + (fr / 300) ** 3) / (1 + (fr / 3000) ** 6) * (1 + 0.6 * np.exp(-0.5 * ((fr - 1400) / 300) ** 2)))
    return seamless(dsp.crush(y / (np.max(np.abs(y)) + 1e-9), 9).astype(np.float32))


# ---- Banging on a Holdout's walls -----------------------------------------------------------------------------------------

def through_wall(x, rng, boom=140):
    """Heard outside a shut building: the boards boom at their low mode and the top is gone."""
    y = lp(x, 1600, 2) + 0.6 * dsp.resonate(lp(x, 600), [boom, boom * 1.6], q=6)
    return dsp.room(y, "box", 0.3, rng=rng)


def frantic(rng, count):
    """A desperate rhythm: blows in clumps of two to four, quick inside a clump, ragged pauses between."""
    ts, t = [], 0.0
    while len(ts) < count:
        for i in range(min(int(rng.integers(2, 5)), count - len(ts))):
            ts.append(t)
            t += rng.uniform(0.14, 0.26)
        t += rng.uniform(0.25, 0.6)
    return ts


@recipe("voice-callout", "bang", "walls",
        "Banging on a Holdout's walls from inside, heard outside: fists, kicks, a tin on iron, frantic and uneven",
        """Someone shut in, beating to be heard: the packs' real wood and metal hits in a desperate rhythm (clumps of two
        to four quick blows, ragged pauses), heard from outside the building so the top is gone and the wall's boards
        boom. Four takes: fists on a plank wall; kicks on a door with its latch rattling; something tin beaten on an iron
        door; palms slapping and fists. No voices (the calls for help need recordings).""",
        sources=W.PIECES["wood"] + ["kenney_rpg-audio:metalLatch", "sfx_100_v2:door_03"]
        + [f"kenney_impact-sounds:impactMetal_medium_00{i}" for i in range(5)]
        + [f"kenney_impact-sounds:impactSoft_medium_00{i}" for i in range(5)], takes=4, lufs=-24)
def bang(rng, k):
    b = dsp.Bus(5.0)
    for i, t in enumerate(frantic(rng, (6, 5, 7, 8)[k])):
        g = rng.uniform(0.6, 1.0)
        if k == 0:
            x = W.rec(f"kenney_impact-sounds:impactWood_medium_00{int(rng.integers(5))}", semis=-rng.uniform(1, 3))
        elif k == 1:
            x = mix(norm(W.rec("sfx_100_v2:door_03", semis=-rng.uniform(0, 2))) * 0.8, norm(W.piece(rng, "wood", (-3, 0))) * 0.6)
            x = mix(x, np.concatenate([np.zeros(samples(0.04)), norm(W.rec("kenney_rpg-audio:metalLatch", semis=-2)) * 0.25]))
        elif k == 2:
            x = W.rec(f"kenney_impact-sounds:impactMetal_medium_00{int(rng.integers(5))}", semis=-rng.uniform(2, 4), tau=0.3)
        else:
            x = (W.rec(f"kenney_impact-sounds:impactSoft_medium_00{int(rng.integers(5))}", semis=rng.uniform(0, 3)) if i % 2
                 else W.rec(f"kenney_impact-sounds:impactWood_medium_00{int(rng.integers(5))}", semis=-2))
        b.at(t + rng.uniform(-0.01, 0.01), norm(x) * g)
    return through_wall(b.x, rng, (140, 110, 180, 150)[k])
