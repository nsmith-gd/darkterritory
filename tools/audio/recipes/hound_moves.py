"""The hounds' patrol heard by its moves (queue #226, note 489; D1's #208, note 472, its mode replicated; E1's #213 clips).
On patrol a hound stops every 8-15 s to sniff for 3 s, and climbs back out of a car at an open door over 1.2 s; neither
had a sound of its own. Built from beasts.py's hound (its breath through a big dog's tract, its claws, its paws, the
ash and embers in its hide), so it's the same animal.

- Springing off (`cs-hounds.spring`): over a coupling gap to the next roof, or off the roof's edge down in at a door: both
  hind feet driving off the tin together, the sheet popping under them, a hard huff, its body through the air with the
  embers streaming off it. The landing is its paws (on the next roof's tin, or the car's boards). The kept `leap` is the
  arrival on the rear platform from the ballast, its landing on the boards; aboard it took off from nothing.
- Sniffing (`cs-hounds.sniff`): its nose down on the boards or the tin, quick hard sniffs in runs of four to six, a snort
  out between them, a low rumble in its chest, the embers in its hide crackling as it breathes. Held while it sniffs.
- Climbing out (`cs-hounds.climb`): timed to E1's clip (cinder_hound.py climb, 30 fps): a trot to the sill on the boards,
  the spring out and up (frame 10), its forelegs hooking over the roof's edge (frame 19), its hind legs scrabbling at the
  car's side (22-28), the heave up (33) and its weight onto the roof's tin (37).
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes.beasts import (done, unit, scrabble, huff, snort, embers, paw_roof, paw_wood, claws, throat,
                            HOUND_TRACT, takes_then, ash)
from recipes import world_kit as W
from recipes import kit

SNIFF = 3.0     # enemies.json cinderHounds.patrol.sniffSeconds
TIN = [f"kenney_impact-sounds:impactTin_medium_{i:03d}" for i in range(5)]       # the kept roof paw's sheet
BOARD = [f"kenney_impact-sounds:footstep_wood_{i:03d}" for i in range(5)]        # the kept board paw's step


def sniff_in(rng, L):
    """One short hard sniff: air drawn fast through the nose, a narrow hiss through the tract's nasal shape."""
    sh = env([(0, 0), (L * 0.2, 1), (L, 0)], L)
    br = synth.breath(L, "m", HOUND_TRACT, rng, shape=sh)
    return unit(bp(br, 900, 5000, 2))


@recipe("cs-hounds", "sniff", "nose",
        "A hound stopped to sniff: quick hard sniffs in runs, a snort out, a rumble in its chest, embers in its hide",
        """Its nose down on the boards or the tin: quick hard sniffs in runs of four to six (air drawn fast through a big
        dog's nose, modelled), a wet snort out between the runs, a low rumble in its chest (its throat, barely voiced),
        and the embers in the cracks of its hide crackling as it breathes. Held while it sniffs; 3 s exact cycle.""",
        loop=True, takes=2, lufs=-24, seconds=SNIFF)
def sniff(rng, k):
    n = samples(SNIFF)
    ev = []
    t = rng.uniform(0.05, 0.2)
    while t < SNIFF - 0.4:
        for _ in range(int(rng.integers(4, 7))):
            L = rng.uniform(0.06, 0.1)
            ev.append((t, sniff_in(rng, L), rng.uniform(0.6, 1.0)))
            t += L + rng.uniform(0.04, 0.08)
        t += rng.uniform(0.05, 0.12)
        ev.append((t, snort(rng, 1.2), rng.uniform(0.5, 0.8)))
        t += rng.uniform(0.35, 0.6)
    noses = W.place(n, ev)
    rumble = throat(rng, [(0, rng.uniform(60, 75)), (SNIFF, rng.uniform(55, 70))], SNIFF, "o",
                    [(0, 0.6), (SNIFF / 2, 1), (SNIFF, 0.6)], rasp=0.5, hiss=0.1, fire=0.2)
    rumble = W.cyclic(lambda z: lp(z, 600, 2), dsp.fit(rumble, n))
    crackle = embers(rng, SNIFF, 30, hi=1600)
    y = unit(noses) * 0.8 + unit(rumble) * 0.18 + unit(dsp.fit(crackle, n)) * 0.12
    return W.seamless(done(y))


@recipe("cs-hounds", "spring", "tin",
        "A hound springing off a roof: both hind feet driving off the tin, the sheet popping, a huff, its body through the air",
        """Over a coupling gap to the next car's roof, or off the roof's edge down in at an open door: a gathering forepaw,
        then both hind feet driving off together (the kept roof paw's tin and claws, a hind foot's weight, a hair apart),
        the roof sheet popping back under the push (its tin ring, low), a hard huff of effort that fans its embers, and
        its body through the air close by with sparks streaming off its hide. No landing (that's its paws, where it
        lands).""", sources=TIN, takes=3, lufs=-22, preview=takes_then(lambda takes, rng: np.concatenate(takes)))
def spring(rng, k):
    b = Bus(1.2)
    b.at(0, paw_roof(rng, 0), -9)
    push = rng.uniform(0.1, 0.14)
    b.at(push, paw_roof(rng, 1), 0)
    b.at(push + rng.uniform(0.012, 0.022), paw_roof(rng, 3), -1)
    imp = np.pad(lp(rng.standard_normal(samples(0.01)).astype(np.float32), 500), (0, samples(0.4)))
    pop = dsp.resonate(imp, [f * rng.uniform(0.55, 0.7) for f in synth.TIN[:4]], q=18)
    b.at(push + 0.03, dsp.shaped(unit(pop), [(0, 1), (0.4, 0.01)], "exp"), -14)
    b.at(push - 0.04, huff(rng, 0.2, rng.uniform(120, 150)), -9)
    air = rng.uniform(0.35, 0.45)
    b.at(push + 0.04, kit.whoosh(rng, air + 0.1, 220, 1600, 0.45), -15)
    b.at(push + 0.04, embers(rng, air, 220, hi=1600), -12)
    b.at(push, ash(rng, 0.3, 400, 4500), -16)
    return done(b.x)


@recipe("cs-hounds", "climb", "scrabble",
        "A hound climbing out at an open door: claws scrabbling up the car's side, a heave, its weight onto the roof",
        """Out at an open side door and up onto the roof, timed to E1's climb clip: a light trot to the sill on the
        boards, both hind feet springing it out and up with a grunt, its forelegs hooking over the roof's edge (claws on
        the tin's lip, a forepaw's weight), then hanging there with its hind legs scrabbling at the car's side (nails on
        the boards and the door's iron, short scrapes), a hard huff as it heaves, and its weight onto the roof's tin, two
        hind feet and settled. The kept paws' boards and tin, the same embers and ash.""", sources=BOARD + TIN, takes=3, lufs=-22, preview=takes_then(lambda takes, rng: np.concatenate(takes)))
def climb(rng, k):
    b = Bus(2.2)
    # the trot to the sill on the boards, light
    for t in (0.03, 0.13 + rng.uniform(-0.01, 0.01), 0.22):
        b.at(t, paw_wood(rng), rng.uniform(-12, -9))
    # the spring out and up off the sill (frame 10), both hind feet and a grunt
    up = 0.33
    b.at(up, paw_wood(rng), -4)
    b.at(up + rng.uniform(0.012, 0.02), paw_wood(rng), -5)
    b.at(up - 0.03, huff(rng, 0.16, rng.uniform(125, 150)), -9)
    # its forelegs hooking over the roof's edge (frame 19): claws on the tin's lip and a forepaw's weight on it
    hook = 0.63
    b.at(hook - 0.01, unit(claws(rng, 4, (2000, 3600), body=[1.0, 2.25, 3.6], spread=0.03)), -6)
    b.at(hook, paw_roof(rng, 0), -4)
    # hanging there, its hind legs scrabbling at the car's side (frames 22-28): nails on the boards and the door's iron
    L = rng.uniform(0.32, 0.4)
    b.at(hook + 0.07, unit(scrabble(rng, L, env([(0, 22), (L * 0.6, 34), (L, 14)], L), f=(1100, 2600), scratch=0.5)), -2)
    b.at(hook + 0.1, unit(claws(rng, 3, (1800, 3400), body=[1.0, 2.2, 3.1], spread=0.2)), -10)
    # the heave (frame 33) and its weight onto the roof (frame 37): hind feet onto the tin, then settled
    heave = rng.uniform(1.04, 1.1)
    b.at(heave - 0.06, huff(rng, 0.2, rng.uniform(110, 135)), -10)
    land = 1.2 + rng.uniform(-0.02, 0.02)
    b.at(land, paw_roof(rng, 1), -2)
    b.at(land + rng.uniform(0.04, 0.07), paw_roof(rng, 3), -4)
    b.at(land + rng.uniform(0.18, 0.26), paw_roof(rng, 2 + k % 2), -9)
    return done(b.x)
