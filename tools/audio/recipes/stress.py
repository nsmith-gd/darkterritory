"""A bend taken too fast (ARCHITECTURE note 265; the director, build 1121: "we'll want to telegraph using sound design that
the train is going under stress"). The stress telegraph builds in the game's order: the flanges squeal from the bend's board,
the frames creak faster, the slack lurches down the train, the flanges scream over the last half, and the cab's bell rings.
`dt audio render --scenario bend` plays it out on a real bend from the cab, a second at a time.

The first candidates (world_bed.py's squeal, world_state.py's curve) are modelled tones that come and go; heard in the cab,
the squeal was gone more than it was there, and the scream held one chord. These are made the other way: real struck metal
(the packs' bells and plates) frozen into a held ring, so the squeal is a wheel's own inharmonic partials singing,
moved in pitch and level by the stick-slip that drives it. The squeal never drops out while it's held (the game's level is
the warning's grade); the scream is several wheels at once, beating, with the contact chattering as it slips.
"""

import numpy as np

import dsp
from build import recipe
from dsp import samples
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, cfilter, croom

SR = dsp.SR
WHEEL = [380, 1050, 1880, 2750, 3640, 4550, 5450]
BELLS = [f"kenney_impact-sounds:impactBell_heavy_00{i}" for i in range(5)]
PLATES = [f"kenney_impact-sounds:impactPlate_light_00{i}" for i in range(5)]


def strongest(x, lo=500, hi=9000):
    """The frequency of a sound's loudest partial between lo and hi."""
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x))))
    f = np.fft.rfftfreq(len(x), 1 / SR)
    keep = (f > lo) & (f < hi)
    return f[keep][np.argmax(spec[keep])]


def freeze(x, n, rng, window=0.2):
    """One moment of a sound held forever (a spectral freeze): the magnitude spectrum of `window` seconds of `x`,
    resynthesised frame after frame with fresh random phases and overlap-added, n samples long and periodic over n."""
    w = samples(window) // 2 * 2
    mag = np.abs(np.fft.rfft(dsp.fit(x, w) * np.hanning(w)))
    hop = w // 4
    out = np.zeros(n + w, np.float32)
    win = np.hanning(w).astype(np.float32)
    for at in range(0, n, hop):
        frame = np.fft.irfft(mag * np.exp(2j * np.pi * rng.random(len(mag))), w).astype(np.float32)
        out[at:at + w] += frame * win
    out[:w] += out[n:n + w]                      # what runs past the end comes round to the start
    return norm(out[:n])


def held_ring(key, target, n, rng):
    """A struck piece of metal held out into a ring n samples long (and periodic over n): varispeeded so its loudest
    partial sits at `target` Hz, and frozen just after the strike, while every partial still sounds (a paulstretch of
    the whole strike would keep its decay, the high partials dying first, and the ring would darken across the loop)."""
    x = W.rec(key)
    x = dsp.vari(x, 12 * np.log2(target / strongest(x)))
    top = int(np.argmax(np.abs(x)))
    return freeze(x[top + samples(0.015):], n, rng)


def stick_slip(n, rng, rate, depth, bursts):
    """The contact catching and letting go: a fast flutter (rate Hz, wandering) that comes in `bursts` a second."""
    f = rate * (1 + 0.25 * slow(n, 0.8, rng))
    flutter = 0.5 + 0.5 * np.sin(2 * np.pi * np.cumsum(f) / SR)
    when = np.clip(0.5 + 0.9 * slow(n, bursts, rng), 0, 1)
    return (1 - depth * when * flutter ** 2).astype(np.float32)


@recipe("bed-wheel-rail", "flange", "sing",
        "Flange squeal that holds: a wheel singing all the way round the bend, wavering as the flange catches",
        """Real struck bell metal from the packs, varispeeded so its loudest partial sits where a car wheel squeals
        (about 3 kHz) and frozen into a held ring (its spectrum just after the strike, held): the squeal is the wheel's own inharmonic partials singing, not
        a sine. A second wheel's ring a little higher joins and leaves. Its pitch drifts and its level flutters with the
        stick-slip that drives it, but it never drops out: in the game its level is the warning's grade, from a third of
        the derailing pull to the bend's board. Under it the flange grinding on the rail head. 10 s exact cycle.""",
        sources=BELLS[:2], loop=True, takes=1, lufs=-22)
def flange_sing(rng, k):
    n = samples(10.0)
    a = held_ring(BELLS[0], 3050, n, rng)
    b = held_ring(BELLS[1], 4300, n, rng)
    a = dsp.wow(a, depth_cents=18, rate=0.5, rng=rng)
    b = dsp.wow(b, depth_cents=25, rate=0.7, rng=rng)
    lead = np.clip(0.8 + 0.2 * slow(n, 0.4, rng), 0.55, 1)
    second = np.clip(0.9 * slow(n, 0.3, rng) - 0.1, 0, 1)
    tone = a * lead * stick_slip(n, rng, 14, 0.45, 0.5) + 0.6 * b * second * stick_slip(n, rng, 19, 0.5, 0.6)
    tone = dsp.band(tone, 1800, 7500, order=2)
    grind = W.friction(n, rng, WHEEL, rough=1.2, grit=0.5, q=22, loop=True)
    # Above the bed's rumble: the wheel's low modes are the roll's, not the squeal's.
    grind = cfilter(grind, lambda f: (f / 1200) ** 2 / (1 + (f / 1200) ** 2) / (1 + (f / 6500) ** 2))
    y = norm(tone) * 0.75 + norm(grind) * 0.16
    return seamless(croom(y, "night", 0.12, rng))


@recipe("state-derail", "flange-scream", "shriek",
        "Every wheel on the bend shrieking at once, beating against each other, the flanges chattering as they slip",
        """Struck bell and plate metal from the packs frozen into held rings at four wheel pitches, two of
        them a few hertz apart so they beat, driven hard (saturated) so the shriek is harsh, not sweet. The contact
        chatters in bursts as the flanges catch and slip, the flanges grind on the rail heads, and the trucks hunt against
        the rails in heavy iron knocks. Steady by itself: the game raises its pitch and level as the bend's pull climbs to
        what derails the train, so it's heard climbing to the edge. 8 s exact cycle.""",
        sources=BELLS[2:4] + PLATES[:2] + W.PIECES["iron"][:5], loop=True, takes=1, lufs=-20)
def flange_shriek(rng, k):
    n = samples(8.0)
    rings = [held_ring(BELLS[2], 2140, n, rng), held_ring(BELLS[3], 2190, n, rng),
             held_ring(PLATES[0], 3420, n, rng), held_ring(PLATES[1], 5150, n, rng)]
    tone = sum(dsp.wow(r, depth_cents=30, rate=1.1, rng=rng) * g * np.clip(0.8 + 0.3 * slow(n, 1.2, rng), 0.4, 1)
               for r, g in zip(rings, (1.0, 0.85, 0.7, 0.45)))
    tone = dsp.saturate(norm(tone) * stick_slip(n, rng, 38, 0.6, 1.3), 9)
    tone = dsp.band(tone, 1500, 9000, order=2)
    grind = W.friction(n, rng, WHEEL, rough=2.2, grit=1.0, q=16, loop=True)
    grind = cfilter(grind, lambda f: (f / 800) ** 2 / (1 + (f / 800) ** 2))
    hunts = [(t, norm(W.piece(rng, "iron", (-9, -5), tau=0.12)), rng.uniform(0.3, 0.6)) for t in W.poisson(8.0, 2.4, rng)]
    y = norm(tone) * 0.55 + norm(grind) * 0.28 + norm(W.place(n, hunts)) * 0.32
    return seamless(croom(y, "night", 0.12, rng))
