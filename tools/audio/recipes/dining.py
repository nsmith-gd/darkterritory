"""The dining car's bar heard (queue #683, note 983; Hazel's dining car, note 959, upgrades.md §8.5: "order drinks at the
bar"), as tools/audio/cues.py lists it and GameAudio.Bbq plays it, off the car's drinks left (its record, every client's)
and the drink in a crewmate's hand.

What each is decides the build:
- A drink poured (up-dining.pour): at the counter, the moment the bar's drinks go down by one. The brass bell on the
  counter (a hotel desk bell: struck, it rings bright, high and long), then a bottle tipped over a tumbler (a few glugs,
  rising as the glass fills) and the glass slid across the panelled counter. Inside the car, its wood close round it.
- A drink drunk (up-dining.sip): where the drinker's head is, the moment it's gone from their hand. A glass, not the
  beer's can: its rim at the teeth (a small glassy tick), a sip and a swallow or two, and now and then the contented
  breath out the beer has.
- Glasses touched (up-dining.clink): two crewmates with drinks in hand close together. A tumbler rings where a can
  thuds: short, bright, and the two never quite the same pitch.

Two candidates each: the packs' real bell, glass and water (and the throats the beer's swig is made of), and one modelled.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, K, S
from recipes.beasts import unit
from recipes.crew_items import hit_of
from recipes.heard_foley import gulp
from recipes.house_folk import drawn, wet_grain
from recipes.up_forge_bbq import BELL, GASP, DRUM, WET, glug_rec, swig_rec

L_ = "up-dining"
GLASS = K("impactGlass_light") + S("glass_01", "glass_04")     # real glass knocked: a tumbler's ring
TRICKLE = "sfx_100_v2:loop_water_02"                             # real water pouring and trickling
WOOD = K("impactWood_light")                                     # the counter's panelled wood
TUMBLER = [1.0, 2.32, 3.89, 5.62]                                # a thick-walled tumbler's modes over its first


def inside(y, rng, wet=0.12, tail=0.3):
    """In the dining car: its wood and cloth close round it (the car's room, a short tail), nothing under 150 Hz."""
    z = dsp.room(y, "car", wet=wet, rng=rng)[:len(y) + samples(tail)]
    return dsp.fade(hp(z, 150, 2), 0.0, tail)


def desk_bell(rng, k, real):
    """The brass bell on the counter, struck once: real heavy bell metal pitched up to a hand bell's 2.4-2.9 kHz, ringing a
    second; or modelled, a small brass bell's modes."""
    if real:
        x = hit_of(BELL[k % len(BELL)], 0, 1.2)
        x = dsp.vari(x, 12 * np.log2(rng.uniform(2400, 2900) / 900))
        return unit(hp(ck.choke(x, 0.5, 0.35), 1200, 2))
    f = rng.uniform(2400, 2900)
    return unit(hp(W.modal([f, f * 2.32, f * 3.1], [0.9, 0.5, 0.3], [1, 0.4, 0.2], 1.3, rng, contact=0.0003), 1200, 2))


def pouring(rng, real, L=1.1):
    """A bottle tipped over a tumbler: a few glugs as the air gets in, rising as the glass fills, and the stream's
    trickle under them."""
    parts, t, n = [], 0.0, int(rng.integers(3, 6))
    for j in range(n):
        if real:
            g = dsp.shaped(glug_rec(rng, 0.13, rng.uniform(5, 7) + 1.5 * j), [(0, 0), (0.015, 1), (0.13, 0)])
        else:
            g = unit(synth.bubbles(0.13, 25, 500 + 150 * j, 1600 + 300 * j, rng, rise=(0.05, 0.2)))
        parts.append((t, unit(g), -2 - j))
        t += rng.uniform(0.14, 0.2)
    if real:
        s = ck.get(TRICKLE)
        a = int(rng.integers(0, len(s) - samples(L + 0.1)))
        stream = unit(lp(hp(s[a:a + samples(L)], 900), 7000))
    else:
        stream = unit(lp(hp(synth.noise(L, rng, "pink"), 1200), 6000))
    parts.append((0.02, dsp.shaped(stream, [(0, 0), (0.05, 1), (t, 0.8), (t + 0.15, 0)]), -14))
    return ck.place(parts)


def glass_down(rng, k, real):
    """The tumbler set down and slid a hand's breadth on the panelled counter: its thick base's knock and a short ring."""
    if real:
        g = ck.choke(dsp.vari(hit_of(GLASS[k % 5], 0, 0.3), -rng.uniform(3, 5)), 0.04, 0.05)
        wood = ck.choke(dsp.vari(hit_of(WOOD[k % 5], 0, 0.2), rng.uniform(0, 2)), 0.02, 0.03)
    else:
        f = rng.uniform(1900, 2300)
        g = W.modal([f * r for r in TUMBLER], [0.06, 0.04, 0.03, 0.02], [1, 0.5, 0.3, 0.15], 0.25, rng, contact=0.0006)
        wood = W.knock(rng, f=rng.uniform(260, 320), length=0.12, drop=0.3)
    slide = dsp.shaped(unit(lp(hp(synth.noise(0.18, rng, "pink"), 1500), 5000)), [(0, 0), (0.02, 1), (0.18, 0)])
    return ck.place([(0, unit(wood), -4), (0.001, unit(hp(g, 900)), -2), (0.03, slide, -20)])


def pour(rng, k, real):
    bell = desk_bell(rng, k, real)
    t = rng.uniform(0.55, 0.7)
    flow = pouring(rng, real)
    end = t + len(flow) / dsp.SR + rng.uniform(0.05, 0.15)
    return inside(ck.place([(0, bell, -4), (t, unit(flow), -6), (end, glass_down(rng, k, real), -6)]), rng)


@recipe(L_, "pour", "bell", "A drink poured at the bar: the counter's bell, a bottle's glugs into a tumbler, the glass slid over",
        """From real recordings: the packs' heavy bell metal struck and pitched up to a hand bell's 2.4-2.9 kHz, ringing half
        a second and dying; a bottle tipped over a tumbler (a real drum's sloshing water sliced into three to five glugs,
        pitched up and rising as the glass fills) with a real pour's trickle under them; the glass set down on the panelled
        counter (a real light glass knocked, pitched down for a thick base, and real light wood) and slid a hand's breadth.
        Inside the dining car.""", sources=BELL + [DRUM, TRICKLE] + GLASS + WOOD, takes=3, lufs=-22)
def pour_bell(rng, k):
    return pour(rng, k, True)


@recipe(L_, "pour", "brass", "A drink poured at the bar, modelled: a small brass bell, a pour's bubbles, a tumbler set down",
        """Modelled: a small brass bell's modes from 2.4-2.9 kHz, struck once; a bottle's glugs as bubbles rising as the glass
        fills, a pour's hiss under them; a tumbler's thick wall rung through a soft contact and a wooden counter's knock.
        Inside the dining car.""", takes=3, lufs=-22)
def pour_brass(rng, k):
    return pour(rng, k, False)


def rim(rng, k, real):
    """The tumbler's rim at the teeth: a small glassy tick, quiet."""
    if real:
        x = ck.choke(dsp.vari(hit_of(GLASS[(k + 2) % 5], 0, 0.1), rng.uniform(3, 6)), 0.008, 0.02)
    else:
        f = rng.uniform(3200, 3800)
        x = W.modal([f, f * 2.32], [0.02, 0.012], [1, 0.4], 0.06, rng, contact=0.0002)
    return unit(hp(x, 1800, 2))


@recipe(L_, "sip", "glass", "A drink drunk from a glass: its rim at the teeth, a sip, a swallow or two",
        """From real recordings: the tumbler's rim at the teeth (a real light glass knocked, pitched up and choked), the drink
        drawn in (a real wet grain for the lips and tongue), one or two swallows (the beer's own swig: a real drum's gurgle
        pitched and darkened going down), and on the last take the contented breath out (a real gasp drawn out and
        darkened). A glass's, not the can's: no tab, no fizz.""",
        sources=GLASS + WET + [DRUM, GASP], takes=3, lufs=-23)
def sip_glass(rng, k):
    parts = [(0, rim(rng, k, True), -10), (0.06, unit(hp(wet_grain(rng, (0, 3)), 900)), -16),
             (rng.uniform(0.2, 0.28), swig_rec(rng, (1, 2, 1)[k % 3]), -2)]
    if k % 3 == 2:
        Lb = 0.55
        ahh = dsp.shaped(unit(lp(hp(drawn(rng, GASP, Lb, -rng.uniform(2, 4)), 200), 3000)), [(0, 0), (0.06, 1), (Lb, 0)])
        parts.append((1.15, ahh, -12))
    return inside(ck.place(parts), rng, wet=0.08)


@recipe(L_, "sip", "throat", "A drink drunk from a glass, modelled: the rim's tick, a sip, a swallow",
        """Modelled: a tumbler's rim at the teeth (two small glass modes, a tick), the drink drawn in (a little wet noise), one
        or two swallows heard from inside the head (the beer's modelled gulp), the last take ending in a breath out.""",
        takes=3, lufs=-23)
def sip_throat(rng, k):
    parts = [(0, rim(rng, k, False), -10), (0.06, unit(lp(hp(synth.noise(0.12, rng, "pink"), 1500), 5000)) *
                                            env([(0, 0), (0.03, 1), (0.12, 0)], 0.12), -20)]
    t = rng.uniform(0.2, 0.28)
    for j in range((1, 2, 1)[k % 3]):
        parts.append((t, unit(hp(gulp(rng), 150)), -4 - 2 * j))
        t += rng.uniform(0.3, 0.38)
    if k % 3 == 2:
        Lb = 0.55
        ahh = synth.breath(Lb, [(0, "a"), (Lb, "h")], "man", rng, shape=env([(0, 0), (0.06, 1), (Lb, 0)], Lb))
        parts.append((t + 0.3, unit(lp(hp(ahh, 200), 3000)), -12))
    return inside(ck.place(parts), rng, wet=0.08)


def tumbler(rng, k, real, semis):
    if real:
        x = ck.choke(dsp.vari(hit_of(GLASS[k % 5], 0, 0.4), semis), 0.12, 0.08)
    else:
        f = 2100 * 2 ** (semis / 12)
        x = W.modal([f * r for r in TUMBLER], [0.25, 0.15, 0.1, 0.06], [1, 0.45, 0.25, 0.12], 0.4, rng, contact=0.0004)
    return unit(hp(x, 700, 2))


def clink(rng, k, real):
    """Two tumblers touched: two rings a few milliseconds apart and a little apart in pitch, so they beat."""
    s = rng.uniform(-1, 1)
    a = tumbler(rng, k, real, s)
    b = tumbler(rng, k + 1, real, s + rng.uniform(0.4, 0.9))
    return inside(ck.place([(0, a, 0), (rng.uniform(0.002, 0.006), b, -2)]), rng, wet=0.1)


@recipe(L_, "clink", "tumblers", "Glasses touched: two tumblers' bright clink, a glass's ring, never a can",
        """From real recordings: two real light glasses knocked a few milliseconds apart, one pitched a little higher, so their
        rings beat as two glasses' do; let ring a tenth of a second. Inside the dining car.""",
        sources=GLASS, takes=2, lufs=-22)
def clink_tumblers(rng, k):
    return clink(rng, k, True)


@recipe(L_, "clink", "rung", "Glasses touched, modelled: two tumblers' walls rung together",
        """Modelled: two thick-walled tumblers' modes from about 2.1 kHz, struck through a hard contact a few milliseconds
        apart, a little apart in pitch so they beat.""", takes=2, lufs=-22)
def clink_rung(rng, k):
    return clink(rng, k, False)
