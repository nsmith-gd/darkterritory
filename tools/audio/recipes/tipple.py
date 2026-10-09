"""The tipple heard (queue #216, note 480; A1's #458, note 423: "its sound (the clamp, the roll, the derail) AU1's"; spec
D.2 "Clamp the car, rotate it to load. 1 crew. Bad clamp derails the car on the spur"). At the mine head a cargo car is
stood in a cradle of two great iron hoops on rollers, clamped by a beam down on its roof and a platen at its side, and
rolled over 150 degrees toward the ore bin, whose chute tips ore into it at the top; then it rolls back by itself and lets
go. A car clamped off its mark comes off its rails a third of the way over and holds the train till the wrench puts it
back.

- Clamping (`clamping`): the lever held, the clamp's screw wound down by its ratchet, the beam's thread grinding.
- Clamped (`clamp`): the beam biting on the roof and the platen on the side, iron on the car's timber.
- Rolling (`roll`): the hoops turning on their rollers (steel on steel, a flat on one thudding round), the pinion clanking
  through the ring gear, the car's body twisting and creaking in the clamp, its load shifting.
- At the top (`pour`): the ore down the bin's steel chute into the car (the steam lift's pour, from a bin).
- Let go (`release`): rolled back, the beam lifting off with a clank and the car dropping onto its springs.
- A bad clamp (`derail`): the car wrenching off its rails in the cradle, its wheels dropping onto the sleepers with heavy
  iron bangs, its body slamming into the hoop, ore spilling over the side.
- The wrench (`rerail`): a jack's ratchet and a bar levering, the car's weight creaking up; and (`rerailed`) the wheels
  dropping back onto the rails, the springs settling.

Built from the packs' real iron, plate, wood and stones (pitched to the part's size, choked where it's bolted); the
rollers' rumble, the creaks and the pour are the kits' models.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter
from recipes.crew_kit import recipe, K, S
from recipes.jobs import strike, RATCHET, CHUTE, landing, METAL_H, METAL_M, PLATE_H, WOOD_H, STONES
from recipes.world_bed import outdoors, circ_outdoors

L = "place-tipple"
SR = dsp.SR
ROLL = 8.0


def yard(y, rng, wet=0.15):
    return outdoors(lp(y, 8000, 2), rng, wet)


@recipe(L, "clamping", "screw",
        "The lever held: the clamp's screw wound down by its ratchet, the beam's thread grinding, iron creaking",
        """At the lever while the clamp's wound down: its pawl clicking over the ratchet (the packs' real ratchet, pitched
        down, about five a second), the beam's screw thread grinding in its nut (modelled rubbing through iron), the beam
        creaking as it comes down. Held while the lever is; 3 s exact cycle.""",
        sources=[RATCHET], loop=True, takes=1, lufs=-24, seconds=3.0)
def clamping(rng, k):
    T = 3.0
    n = samples(T)
    clicks = [strike(RATCHET, -rng.uniform(4, 6), lo=300, at=0.01, tau=0.02) for _ in range(3)]
    count = 15
    pawl = W.place(n, [(j * T / count + rng.uniform(-0.005, 0.005), clicks[j % 3], rng.uniform(0.6, 1.0)) for j in range(count)])
    grind = W.friction(n, rng, [230, 520, 910, 1400], rough=1.6, grit=0.4, q=14, loop=True) \
        * np.clip(0.6 + 0.3 * slow(n, 2, rng), 0.2, 1.2).astype(np.float32)
    creaks = W.place(n, [(tt, norm(synth.creak(0.4, 30, rng, body=synth.IRON[:5], q=18)) * env([(0, 0), (0.05, 1), (0.4, 0)], 0.4)[:samples(0.4)], 0.4)
                         for tt in (0.5, 1.9)])
    y = norm(pawl) * 0.6 + norm(grind) * 0.25 + norm(creaks) * 0.2
    return seamless(circ_outdoors(cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 4)), rng, 0.12))


@recipe(L, "clamp", "bite",
        "Clamped: the beam biting down on the car's roof, the platen at its side, iron on timber",
        """The clamp shut: the beam coming down onto the car's roof with a heavy iron clank (real heavy metal, pitched
        down and choked by the car it's on), the side platen pressing in with a crunch of the car's timber (real heavy
        wood), the car's body creaking once under it. Under a second.""", sources=METAL_H + WOOD_H, takes=3, lufs=-20)
def clamp(rng, k):
    b = dsp.Bus(1.4)
    b.at(0, strike(METAL_H[k % 5], -rng.uniform(5, 7), lo=90, at=0.05, tau=0.05))
    b.at(rng.uniform(0.08, 0.14), strike(WOOD_H[(k + 1) % 5], -rng.uniform(3, 5), lo=100, at=0.04, tau=0.05) * 0.7)
    b.at(rng.uniform(0.1, 0.16), strike(METAL_M[(k + 2) % 5], -rng.uniform(4, 6), lo=150, at=0.03, tau=0.04) * 0.4)
    cr = synth.creak(0.5, 40, rng, body=[f * 0.8 for f in synth.WOOD[:4]], q=14, jitter=0.4)
    b.at(0.2, norm(cr * env([(0, 0), (0.06, 1), (0.5, 0)], 0.5)[:len(cr)]) * 0.25)
    return yard(b.x, rng)


@recipe(L, "roll", "hoops",
        "The cradle rolling the car over: its hoops grinding round on their rollers, the ring gear clanking, the car twisting",
        """The car rolled over in its cradle: the two iron hoops turning on their steel rollers (a heavy low rumble, a flat
        on one roller thudding round twice a second), the pinion clanking through the hoops' ring gear tooth by tooth (real
        medium iron, pitched down, choked), the car's timber body twisting and creaking in the clamp, and its load shifting
        and sliding inside. Loud: machinery. 8 s exact cycle.""", sources=METAL_M + STONES, loop=True, takes=2, lufs=-20,
        seconds=ROLL)
def roll(rng, k):
    n = samples(ROLL)
    rumble = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(110)) / 0.8) ** 2))
    flats = W.place(n, [(j * 0.5 + 0.13, norm(W.knock(rng, rng.uniform(70, 90), 0.12)), rng.uniform(0.6, 0.9)) for j in range(16)])
    teeth = [strike(METAL_M[j % 5], -rng.uniform(6, 8), lo=120, at=0.012, tau=0.02) for j in range(5)]
    gear = W.place(n, [(j * ROLL / 40 + rng.uniform(-0.005, 0.005), teeth[j % 5], rng.uniform(0.4, 0.8)) for j in range(40)])
    creaks = W.place(n, [(tt, norm(synth.creak(rng.uniform(0.4, 0.9), rng.uniform(18, 35), rng,
                                               body=[f * 0.7 for f in synth.WOOD[:5]], q=12, jitter=0.4)), rng.uniform(0.3, 0.6))
                         for tt in W.poisson(ROLL, 0.9, rng)])
    load = W.place(n, [(tt, norm(W.piece(rng, "brick", (-6, 0), tau=0.15)), rng.uniform(0.1, 0.3)) for tt in W.poisson(ROLL, 3, rng)])
    y = norm(rumble) * 0.45 + norm(flats) * 0.3 + norm(gear) * 0.3 + norm(creaks) * 0.25 + norm(load) * 0.12 * (1 + k)
    return seamless(circ_outdoors(cfilter(y, lambda f: 1 / (1 + (f / 5000) ** 4)), rng, 0.15))


@recipe(L, "pour", "chute",
        "At the top: ore roaring down the bin's steel chute into the rolled-over car",
        """At the top of the roll: the bin's gate knocked open (real heavy iron, choked) and half a car-load of ore down
        its steel chute into the car (the pour model: lumps cracking on the plates and each other, the riveted chute
        ringing under it), landing in the car while it still pours (heavy real wood, pitched down) and the last stones
        scattering. About 2.5 s.""", sources=METAL_H + WOOD_H, takes=2, lufs=-19)
def pour(rng, k):
    T = 2.6
    b = dsp.Bus(T + 1.0)
    b.at(0, strike(METAL_H[(k + 3) % 5], -rng.uniform(6, 8), lo=100, at=0.08, tau=0.08) * 0.7)
    m = samples(T - 0.2)
    flow = env([(0, 50), (0.15, 3000), (1.2, 2600), (T - 0.7, 300), (T - 0.2, 10)], T - 0.2)
    slide = W.pour(m, rng, flow, grain=(700, 4500), lump=14, thunder=0.4, ring=CHUTE, loop=False)
    b.at(0.12, norm(hp(slide, 120, 2)) * 0.8 * env([(0, 0.5), (0.1, 1), (T - 0.6, 0.8), (T - 0.2, 0)], T - 0.2)[:m])
    for tt in (0.6, 1.1, 1.6):
        b.at(tt + rng.uniform(-0.05, 0.05), landing(rng, rng.uniform(0.6, 1.0)) * 0.5)
    return yard(b.x, rng)


@recipe(L, "release", "springs",
        "Rolled back and let go: the beam lifting off with a clank, the car dropping onto its springs",
        """Rolled back upright, the clamp lets go: the beam lifts off the roof with a clank and a rattle (real medium iron,
        pitched down), the car settles down onto its springs with a heavy bounce (real heavy wood, a low knock), the
        hoops coming to rest on their rollers. About a second.""", sources=METAL_M + WOOD_H, takes=2, lufs=-21)
def release(rng, k):
    b = dsp.Bus(1.5)
    b.at(0, strike(METAL_M[k % 5], -rng.uniform(4, 6), lo=150, at=0.04, tau=0.05) * 0.8)
    for j in range(3):
        b.at(0.05 + 0.04 * j + rng.uniform(0, 0.02), strike(METAL_M[(k + j + 1) % 5], -rng.uniform(1, 3), lo=300, at=0.01, tau=0.02) * 0.2)
    b.at(rng.uniform(0.25, 0.32), strike(WOOD_H[(k + 2) % 5], -rng.uniform(4, 6), lo=70, at=0.05, tau=0.06) * 0.7)
    b.at(0.28, norm(W.knock(rng, 75, 0.25)) * 0.4)
    b.at(0.45, norm(W.knock(rng, 85, 0.15)) * 0.2)
    return yard(b.x, rng)


@recipe(L, "derail", "off",
        "A bad clamp: the car wrenching off its rails in the cradle, wheels crashing onto the sleepers, ore spilling",
        """A car clamped off its mark, a third of the way over: it wrenches sideways off its rails in the cradle. Its
        trucks drop off the rail heads onto the sleepers one after the other (real heavy iron and plate, pitched well
        down, with a low knock under each), its body slams into the hoop (real heavy wood and iron together), the timbers
        groan, and ore spills over the side onto the ballast (the packs' stones, a rush then a trickle). About 2.5 s.""",
        sources=METAL_H + PLATE_H + WOOD_H + STONES, takes=2, lufs=-17)
def derail(rng, k):
    T = 2.6
    b = dsp.Bus(T + 1.0)
    b.at(0, strike(METAL_H[k % 5], -rng.uniform(7, 9), lo=60, at=0.08, tau=0.08))
    b.at(0.01, norm(W.knock(rng, 55, 0.4, drop=0.4)) * 0.6)
    second = rng.uniform(0.18, 0.3)
    b.at(second, strike(PLATE_H[(k + 1) % 5], -rng.uniform(7, 9), lo=60, at=0.08, tau=0.08) * 0.9)
    b.at(second, norm(W.knock(rng, 60, 0.35, drop=0.4)) * 0.5)
    b.at(second + 0.08, strike(WOOD_H[(k + 2) % 5], -rng.uniform(5, 7), lo=70, at=0.08, tau=0.08) * 0.8)
    b.at(second + 0.09, strike(METAL_H[(k + 3) % 5], -rng.uniform(3, 5), lo=120, at=0.1, tau=0.1) * 0.5)
    g = synth.creak(1.2, env([(0, 30), (0.6, 15), (1.2, 8)], 1.2), rng, body=[f * 0.6 for f in synth.WOOD[:5]], q=10, jitter=0.5)
    b.at(second + 0.2, norm(g * env([(0, 0), (0.1, 1), (1.2, 0)], 1.2)[:len(g)]) * 0.35)
    m = samples(1.8)
    spill = W.pour(m, rng, env([(0, 100), (0.1, 4000), (0.6, 1500), (1.8, 30)], 1.8), grain=(500, 3500), lump=10, thunder=0.5, loop=False)
    b.at(second + 0.15, norm(hp(spill, 100, 2)) * 0.5)
    return yard(b.x, rng, 0.18)


@recipe(L, "rerail", "jack",
        "Putting it back on its rails: a jack's ratchet, a bar levering, the car's weight creaking up and over",
        """The wrench at a car off its rails: a rerailing jack wound by its ratchet (the packs' real ratchet, pitched
        down, in heaves), a bar levering under a wheel with iron scraping on iron, the car's weight creaking as it comes
        up and shifts over toward the rail. Held while the work goes on; 4 s exact cycle.""",
        sources=[RATCHET] + METAL_M, loop=True, takes=1, lufs=-24, seconds=4.0)
def rerail(rng, k):
    T = 4.0
    n = samples(T)
    clicks = [strike(RATCHET, -rng.uniform(5, 7), lo=300, at=0.01, tau=0.02) for _ in range(3)]
    ev = []
    for heave in (0.2, 2.1):
        for j in range(6):
            ev.append((heave + j * 0.14 + rng.uniform(-0.01, 0.01), clicks[j % 3], rng.uniform(0.6, 1.0)))
    pawl = W.place(n, ev)
    scrape = W.place(n, [(1.2, norm(W.friction(samples(0.5), rng, [300, 720, 1300], rough=1.8, grit=0.5, q=12)) * env([(0, 0), (0.05, 1), (0.5, 0)], 0.5)[:samples(0.5)], 0.5),
                         (3.3, norm(W.friction(samples(0.4), rng, [300, 720, 1300], rough=1.8, grit=0.5, q=12)) * env([(0, 0), (0.05, 1), (0.4, 0)], 0.4)[:samples(0.4)], 0.4)])
    creaks = W.place(n, [(tt, norm(synth.creak(0.8, 20, rng, body=[f * 0.6 for f in synth.WOOD[:5]], q=12, jitter=0.4)) * env([(0, 0), (0.1, 1), (0.8, 0)], 0.8)[:samples(0.8)], 0.5)
                         for tt in (0.5, 2.4)])
    y = norm(pawl) * 0.5 + norm(scrape) * 0.3 + norm(creaks) * 0.35
    return seamless(circ_outdoors(cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 4)), rng, 0.12))


@recipe(L, "rerailed", "back-on",
        "Back on its rails: the wheels dropping onto the rail heads with heavy clunks, the springs settling",
        """The last heave: the car's wheels drop back onto the rail heads, one truck then the other (real heavy iron,
        pitched down and choked, a low knock under each), and the car rocks and settles on its springs. About a second.""",
        sources=METAL_H, takes=2, lufs=-20)
def rerailed(rng, k):
    b = dsp.Bus(1.6)
    b.at(0, strike(METAL_H[k % 5], -rng.uniform(6, 8), lo=70, at=0.06, tau=0.06))
    b.at(0.01, norm(W.knock(rng, 70, 0.25)) * 0.5)
    t2 = rng.uniform(0.3, 0.45)
    b.at(t2, strike(METAL_H[(k + 2) % 5], -rng.uniform(6, 8), lo=70, at=0.06, tau=0.06) * 0.8)
    b.at(t2 + 0.01, norm(W.knock(rng, 75, 0.25)) * 0.4)
    cr = synth.creak(0.5, 30, rng, body=[f * 0.7 for f in synth.WOOD[:4]], q=12, jitter=0.4)
    b.at(t2 + 0.1, norm(cr * env([(0, 0), (0.06, 1), (0.5, 0)], 0.5)[:len(cr)]) * 0.2)
    return yard(b.x, rng)
