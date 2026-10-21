"""The flare pistol heard (queue #675, note 975; note 871, docs/design/upgrades.md §4: "A flare lights a village for 60 s, and
the Fire Flies follow it away from the train"), as tools/audio/cues.py lists it and GameAudio.Flare plays it, off the
pistol's and the flare's own bodies, which every machine has (World.FireFlare, World.StepFlares).

What each is decides the build:
- The shot (up-flare.shot): a wide-bored signal pistol, not a gun. Its cartridge is a paper tube of black powder pushing a
  star out of a 26 mm barrel at walking-pace pressures, so no rifle's crack: a hollow "thoonk", the short barrel ringing
  at its quarter-wave (about 370 Hz for 23 cm, open at the muzzle), a puff of gas, and the flare's own hiss tearing away
  up the arc, darker and fainter as it goes.
- The flare burning (up-flare.burn, a loop held for its minute): magnesium and strontium burning on the ground, a fierce
  steady roar of hiss with a fizz in it and dry spits flying off it, fluttering a little as the burning face changes.
  Heard across a village (it's the light the Fire Flies leave the train for). Never a creature's tell (spec A.4): held
  under 5 kHz, under the car-fire tell's 6-9 kHz crackle, and steady where the Stoker's hiss comes and goes.
- The empty pistol (up-flare.click): Fire on no flares left, the hammer falling on an empty breech: a dry tick and the
  trigger's stop, close, in the hand.

Two candidates each: one from the packs' real things (their soft heavy blow and a book set down for the report, their air
releases for the hiss, their metal click for the hammer), one modelled.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, bp, mix, env
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, S, R, K
from recipes.crew_items import hit_of
from recipes.figures import at, loop, band_shape, once

L_ = "up-flare"
AIR = S("air_01", "air_02")                # the packs' real air releases: the flare's hiss
SOFT = K("impactSoft_heavy")               # a soft heavy blow: the report's body
SNAP = R("bookPlace2")                     # a book set down hard: the report's snap
CLICK = R("metalClick")                    # a real small mechanism: the hammer
BARREL = [370, 1110, 1850]                 # a 23 cm barrel open at the muzzle: quarter-wave and its odd harmonics
TOP = 5000                                 # the burn under the car-fire tell's 6-9 kHz band (spec A.4)
CYCLE = 8.0


def barrel(x, wet=0.4, q=14):
    """The short wide barrel ringing as the charge goes: its quarter-wave and odd harmonics, damped by the open muzzle."""
    return ck.norm(mix(x * (1 - wet), ck.norm(dsp.resonate(x, BARREL, q=q, gains=[1, 0.5, 0.25])) * wet))


def leaving(rng, hiss, length=1.8):
    """The flare tearing away up its arc: its hiss coming on in a few hundredths of a second, then fainter and darker as it
    flies off (a lowpass closing from 5 kHz to 900 Hz), a little more air under it for the plume."""
    n = samples(length)
    h = dsp.fit(hiss, n)
    h = dsp.sweep_filter(h, "lp", env([(0, 5000), (0.25, 3000), (length, 900)], length, "exp"), q=0.7)
    shape = env([(0, 0), (0.04, 1), (0.3, 0.7), (length, 0)], length) ** 1.6
    return ck.norm(hp(h, 300, 2)) * shape.astype(np.float32)


def report_real(rng, k):
    """The report from real things: the packs' soft heavy blow pitched down for the charge's thump, a book set down hard for
    the snap of the breech, both rung through the barrel, and a short puff of gas out of the muzzle."""
    thump = dsp.vari(hit_of(SOFT[k % len(SOFT)], 0, 0.3), rng.uniform(-5, -3))
    snap = dsp.vari(hit_of(SNAP[0], k % 2, 0.12), rng.uniform(-4, -2))
    puff = lp(hp(synth.noise(0.12, rng, "pink"), 250, 2), 2500, 2) * env([(0, 1), (0.12, 0)], 0.12, "exp")
    return barrel(ck.place([(0, thump, 0), (0.002, snap, -4), (0.004, puff, -10)]))


def report_model(rng, k):
    """The report modelled: a pressure thump falling from about 90 Hz, a short burst of noise (the shock, softened: low
    pressure, no crack), the gas's puff, all rung through the barrel."""
    L = 0.35
    t = np.arange(samples(L)) / dsp.SR
    thump = synth.thump(rng.uniform(80, 95), 0.3, drop=0.4)
    shock = bp(synth.noise(L, rng), 300, 3500, 2) * np.exp(-t / 0.008).astype(np.float32)
    puff = lp(synth.noise(L, rng, "pink"), 1800, 2) * np.exp(-t / 0.05).astype(np.float32)
    return barrel(mix(ck.norm(thump) * 0.8, ck.norm(shock) * 0.7, ck.norm(puff) * 0.4), wet=0.5)


def shot(rng, k, real):
    report = report_real(rng, k) if real else report_model(rng, k)
    if real:
        hiss = ck.get(AIR[k % 2])
        hiss = dsp.fit(dsp.vari(hiss, rng.uniform(-3, -1)), samples(1.8))
    else:
        hiss = W.jet(1.8, rng, pressure=1.0, peak=2200, low=0.0, rasp=0.2)
    y = ck.place([(0, report, 0), (0.03, leaving(rng, hiss), -9)])
    # Nothing under 140 Hz: a signal pistol's body is its barrel's ring, not a gun's boom (and no squish under it).
    return hp(once(y, rng, wet=0.12, tail=0.3, lo=140), 140, 4)


@recipe(L_, "shot", "pop", "The flare pistol fired: a hollow pop and the flare hissing away",
        """From real recordings: the packs' soft heavy blow pitched down for the charge's thump and a book set down hard for
        the breech's snap, rung through a 23 cm barrel open at the muzzle (370 Hz and its odd harmonics), a puff of gas;
        then their real air release for the flare tearing away up its arc, darker and fainter as it goes. A signal pistol,
        not a gun: no rifle's crack.""", sources=SOFT + SNAP + AIR, takes=3, lufs=-16)
def shot_pop(rng, k):
    return shot(rng, k, True)


@recipe(L_, "shot", "bore", "The flare pistol fired, modelled: a thump through a short wide barrel and a hiss away",
        """Modelled: a pressure thump falling from about 90 Hz and a softened shock rung through the barrel, a puff of gas,
        then a jet's hiss for the flare leaving, a lowpass closing on it as it flies off.""", takes=3, lufs=-16)
def shot_bore(rng, k):
    return shot(rng, k, False)


def spits(rng, n, rate, size=1.0):
    """Dry spits flying off the burning face: short bright bursts, a few a second, never even, and now and then a bigger
    pop as a lump of the composition goes."""
    events = []
    for t in W.poisson(CYCLE, rate, rng):
        L = rng.uniform(0.004, 0.015) * size
        b = bp(synth.noise(L + 0.01, rng), 900, 4500, 2)[:samples(L)] * np.exp(-np.linspace(0, 4, samples(L))).astype(np.float32)
        events.append((t, b, rng.uniform(0.3, 1.0)))
    for t in W.poisson(CYCLE, 0.5, rng):
        L = rng.uniform(0.03, 0.06)
        b = lp(synth.noise(L, rng, "pink"), 2500, 2) * env([(0, 1), (L, 0)], L, "exp")
        events.append((t, b, rng.uniform(1.0, 1.6)))
    return W.norm(W.place(n, events))


def burning(rng, real):
    """A flare burning on the ground: a steady fierce roar of hiss (the gas off the burning face), a fizz in it, dry spits
    off it, fluttering a little as the face changes. A seamless cycle, out in the open."""
    n = samples(CYCLE)
    flutter = np.clip(1 + 0.18 * W.slow(n, 3.5, rng) + 0.1 * W.slow(n, 9, rng), 0.5, 1.5).astype(np.float32)
    roar = W.norm(W.jet(CYCLE, rng, pressure=1.0, peak=1400, low=0.7, rasp=0.15, n=n))
    if real:
        from recipes.house_folk import drawn
        fizz = W.norm(bp(dsp.fit(drawn(rng, AIR[0], CYCLE + 0.5, semis=-2), n), 600, 4500))
    else:
        fizz = W.norm(bp(synth.noise(CYCLE, rng)[:n], 700, 4200))
    y = mix(at(roar * flutter, -22), at(fizz * flutter, -25), at(spits(rng, n, 7), -28))
    return loop(lp(y, TOP, 4), n, rng, 0.06, "night", band_shape(120, TOP, 4))


@recipe(L_, "burn", "fizz", "A flare burning where it came down: a fierce magnesium hiss and sputter",
        """From real recordings: the packs' real air release drawn out (its loudest stretch paulstretched) and held to 0.6-4.5
        kHz for the fizz off the burning face, over a steady jet's roar for the gas pouring off it, dry spits flying off a few
        times a second and now and then a bigger pop, all fluttering a little. Held under 5 kHz, so it's never the car-fire
        tell's crackle (6-9 kHz); steady, never the Stoker's coming and going. An 8 s cycle.""",
        sources=AIR[:1], loop=True, takes=1, lufs=-20, seconds=CYCLE)
def burn_fizz(rng, k):
    return burning(rng, True)


@recipe(L_, "burn", "torch", "A flare burning, modelled: a steady roaring hiss and dry spits",
        """Modelled: a jet's roar peaking about 1.4 kHz with its plume under it, a band of fizz over it, dry spits and the odd
        pop, fluttering a little. Under 5 kHz; an 8 s cycle.""", loop=True, takes=1, lufs=-20, seconds=CYCLE)
def burn_torch(rng, k):
    return burning(rng, False)


@recipe(L_, "click", "hammer", "The flare pistol empty: the hammer falling dry",
        """From real recordings: the packs' real metal click (its first part) pitched down a little for a bigger hammer,
        the trigger's stop a few milliseconds after, a little of the hand's grip. Close and dry.""",
        sources=CLICK, takes=2, lufs=-26)
def click_hammer(rng, k):
    c = hit_of(CLICK[0], 0, 0.06)
    stop = hit_of(CLICK[0], 1, 0.04)
    y = ck.place([(0, dsp.vari(c, rng.uniform(-4, -2)), 0), (rng.uniform(0.012, 0.02), dsp.vari(stop, -6), -10)])
    return dsp.fade(hp(y, 200, 2), 0.0005, 0.02)


@recipe(L_, "click", "tick", "The flare pistol empty, modelled: a dry tick and the trigger's stop",
        """Modelled: a short bright click for the hammer on the empty breech, a duller one a moment after for the trigger
        stopping, and a small knock of the grip.""", takes=2, lufs=-26)
def click_tick(rng, k):
    a = synth.click(f=rng.uniform(2600, 3200), q=10, length=0.02, rng=rng)
    b = synth.click(f=rng.uniform(1100, 1400), q=6, length=0.03, rng=rng)
    grip = W.knock(rng, f=rng.uniform(220, 280), length=0.06, drop=0.3)
    y = ck.place([(0, a, 0), (0.014, b, -8), (0.002, grip, -16)])
    return dsp.fade(hp(y, 200, 2), 0.0005, 0.02)
