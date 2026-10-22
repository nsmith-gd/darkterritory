"""The big cab's benches heard sat on and got up from (queue #684, note 984; Tess's note 905: "Bench sitting has no sound
yet. `GameAudio.Lounge` leaves benches out (AU1's to give one)"), as tools/audio/cues.py lists it and GameAudio.Lounge
plays it, off the Lounging flag at one of the engine's bench seats (`LoungeKind.Bench`), every client's.

What it is decides the build: not the Muskoka chair's big slatted body. A bench in the cab's gallery is one thin plank
(0.175 m deep) on slim iron brackets bolted to the cab's steel side, open underneath, a rail on the wall to sit back
against. Sat on: the body coming down on the plank (a short, light wooden knock, higher than a chair's: a narrow board
over a short span), the plank flexing a little on its brackets (a small dry creak), the brackets' bolts ticking in the
steel, and the back meeting the wall's rail (a soft thump on iron). Got up from: the hands pushing off, the plank
springing back (a short creak rising), a bracket's tick. In the iron cab, its walls close round it; nothing under 150 Hz.
Never a creature's tell (spec A.4): no rope's wet creak, no iron-shod step.

Two candidates: the packs' real creaks, planks, soft blows and light metal; and the plank, brackets and rail modelled.
"""

import dsp
import synth
from dsp import samples, hp, bp, env
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import unit
from recipes.crew_items import hit_of
from recipes.crew_kit import recipe, K, R

L_ = "up-lounge"
CREAKS = R("creak1", "creak3")                   # real wooden creaks (not creak2: its squeal sings): the plank flexing
PLANK = K("impactPlank_medium")                  # a plank's hollow knock: the seat taking the body
SOFT = K("impactSoft_medium")                    # real soft blows: a body sitting, a back on the rail
METAL = K("impactMetal_light")                   # real light metal hits: the brackets' bolts in the steel
CLOTH = R("cloth1", "cloth2", "cloth4")          # a coat moving
BOARD = [1.0, 2.6, 4.9, 7.8]                     # a narrow board over a short span, knocked: its modes as ratios


def cab(x, rng, wet=0.1, tail=0.25):
    """In the iron cab: its steel walls close round it, a short tail, nothing under 150 Hz."""
    y = dsp.room(x, "cab", wet=wet, rng=rng)[:len(x) + samples(tail)]
    return dsp.fade(hp(y, 150, 2), 0.0, tail)


def flex_rec(rng, take, L, rise):
    """The plank flexing on its brackets: a real wooden creak, a little up for a narrow board (higher still as it springs
    back, `rise` > 0), cut short."""
    c = dsp.vari(ck.align(ck.get(CREAKS[take % 2])), rng.uniform(0, 2) + rise)[:samples(L)]
    return unit(hp(c, 300, 2)) * env([(0, 0), (0.02, 1), (L * 0.6, 0.7), (L, 0)], L)


def flex_model(rng, take, L, rise):
    """The plank flexing, modelled: a short stick-slip at a bracket through the board's modes, quicker as it springs back."""
    r0, r1 = (35, 70) if rise > 0 else (60, 30)
    f = rng.uniform(320, 380)
    s = synth.creak(L, env([(0, r0), (L, r1)], L, "exp"), rng, body=[f * r for r in BOARD], q=12, jitter=0.4, grit=0.3)
    return unit(hp(s, 300)) * env([(0, 0), (0.02, 1), (L * 0.6, 0.7), (L, 0)], L)


def seat_rec(rng, take):
    """The body coming down on the plank: a real soft blow and a real plank's knock, a little up for a narrow board."""
    soft = hit_of(SOFT[take % 5], 0, 0.2)
    knock = ck.choke(dsp.vari(hit_of(PLANK[take % 5], 0, 0.2), rng.uniform(1, 3)), 0.02, 0.04)
    return unit(hp(ck.place([(0, unit(soft), -2), (0.003, unit(knock), 0)]), 150, 2))


def seat_model(rng, take):
    """The body coming down on the plank, modelled: a broad soft blow on a narrow board's modes."""
    f = rng.uniform(300, 360)
    k = W.modal([f * r for r in BOARD], [0.06, 0.04, 0.03, 0.02], [1, 0.5, 0.3, 0.15], 0.25, rng, contact=0.004)
    return unit(hp(ck.choke(unit(k), 0.03, 0.05), 150, 2))


def bolt_rec(rng, take):
    """A bracket's bolt ticking in the cab's steel side: a real light metal hit, choked to a tick."""
    return unit(hp(ck.choke(dsp.vari(hit_of(METAL[take % 5], 0, 0.1), rng.uniform(-2, 1)), 0.008, 0.02), 600, 2))


def bolt_model(rng, take):
    """A bracket's bolt ticking, modelled: a small iron part's bright modes through a hard contact, short."""
    f = rng.uniform(1700, 2100)
    return unit(hp(W.modal([f, f * 2.7, f * 4.4], [0.03, 0.02, 0.012], [1, 0.4, 0.2], 0.06, rng, contact=0.0003), 600, 2))


def rail_rec(rng, take):
    """The back meeting the wall's rail: a real soft blow, dull, and the rail's faint ring under the coat."""
    soft = dsp.vari(hit_of(SOFT[(take + 2) % 5], 0, 0.2), -rng.uniform(1, 3))
    ring = ck.choke(dsp.vari(hit_of(METAL[(take + 1) % 5], 0, 0.15), -rng.uniform(4, 6)), 0.02, 0.05)
    return unit(hp(ck.place([(0, unit(soft), 0), (0.004, unit(ring), -14)]), 150, 2))


def rail_model(rng, take):
    """The back meeting the rail, modelled: a soft blow on an iron tube (its low modes), mostly coat."""
    f = rng.uniform(420, 520)
    t = W.modal([f, f * 2.76, f * 5.4], [0.04, 0.03, 0.02], [1, 0.3, 0.15], 0.2, rng, contact=0.006)
    return unit(hp(ck.choke(unit(t), 0.03, 0.05), 150, 2))


def cloth_rec(rng, take):
    return unit(hp(ck.cloth(rng, take, 0.25), 250, 2))


def cloth_model(rng, take):
    L = 0.25
    return dsp.shaped(unit(bp(synth.noise(L, rng, "pink"), 400, 4000)), [(0, 0), (0.05, 1), (L, 0)])


def bench(rng, k, flex, seat, bolt, rail, cloth):
    """Sat down on (takes 0 and 2: the coat, the body on the plank, its flex and a bolt's tick, the back on the rail) or
    got up from (take 1: hands pushing off, the plank springing back, a bolt's tick, the coat)."""
    if k % 3 == 1:
        parts = [(0, flex(rng, k, 0.12, 0.0), -10), (0.1, cloth(rng, k), -12), (0.16, flex(rng, k + 1, 0.22, 1.0), -2),
                 (0.2, bolt(rng, k), -12), (0.3, seat(rng, k + 1) * 0.25, -16)]
    else:
        parts = [(0, cloth(rng, k), -12), (0.06, seat(rng, k), 0), (0.075, flex(rng, k, 0.25, -1.0), -6),
                 (0.08, bolt(rng, k), -12), (0.32, rail(rng, k), -6)]
        if k % 3 == 2:
            parts.append((0.6, flex(rng, k + 2, 0.15, -0.5), -12))
    return ck.place(parts)


@recipe(L_, "bench", "plank",
        "A cab bench sat on or got up from: a thin plank on iron brackets taking the weight, the back on the wall's rail",
        """From real recordings: the body coming down on the plank (a real soft blow and a real plank's knock, a little up
        for a narrow board), the plank flexing on its brackets (the packs' real wooden creaks, a little up and short), a
        bracket's bolt ticking in the steel (a real light metal hit, choked), the back meeting the wall's rail (a real
        soft blow with the rail's faint ring under it), a coat moving (real cloth). Two takes sitting down, one getting up
        (the plank springing back). In the iron cab.""",
        sources=CREAKS + PLANK + SOFT + METAL + CLOTH, takes=3, lufs=-24)
def bench_plank(rng, k):
    return cab(bench(rng, k, flex_rec, seat_rec, bolt_rec, rail_rec, cloth_rec), rng)


@recipe(L_, "bench", "board",
        "A cab bench sat on or got up from, modelled: a narrow board's knock and flex, a bolt's tick, the rail",
        """Modelled: a soft blow on a narrow board's modes from about 330 Hz, stick-slip at a bracket through them, a small
        iron bolt's tick, a soft blow on the wall rail's iron tube, a coat's breath of cloth. Two takes sitting down, one
        getting up. In the iron cab.""", takes=3, lufs=-24)
def bench_board(rng, k):
    return cab(bench(rng, k, flex_model, seat_model, bolt_model, rail_model, cloth_model), rng)
