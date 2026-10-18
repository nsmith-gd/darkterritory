"""The human cannonball heard (queue #556, note 828; U1's #1106, note 746: the guard van's cannon loaded with a crewmate who
climbs in, fired at a landing point along its aim, "going for fun in this one": the landing never hurts). As
tools/audio/cues.py lists them and GameAudio.Cannonball plays them off the crewmate's InCannon and Fired flags.

- The climb (crew-cannonball.climb): into the barrel feet first (or back out): boots and knees scraping on the bore's iron,
  a knock that booms hollow down the tube, a grunt as they wriggle down.
- The whoop (crew-cannonball.whoop): as the shot throws them: a long delighted 'wheee!' (or a 'woo-HOO!'), each take a
  different crewmate, carried with them.
- The flight (crew-cannonball.flight, a loop): the air tearing past their ears and their coat flapping, held while they fly.
- The landing (crew-cannonball.land): a gag, as the hook's drop (note 802): a padded thump, a roll or two along the ground,
  and the wind knocked out of them in a comic 'oof!'.

The crew's modelled throat (recipes/rescue_finds.effort, the voice every crew sound uses) and the packs' real impacts, cloth
and leather.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.beasts import unit
from recipes.crane_hoist import SOFT, oof
from recipes.crew_kit import recipe
from recipes.rescue_finds import effort, VOICES, GASP, SWISH, DROP

L_ = "crew-cannonball"
IRON = [f"kenney_impact-sounds:impactMetal_heavy_{i:03d}" for i in range(5)]
LIGHT_IRON = [f"kenney_impact-sounds:impactMetal_light_{i:03d}" for i in range(5)]
# The bore's hollow: a cast-iron tube a body fits in, its low modes (pipe-like, a little inharmonic).
BORE = [190, 410, 640, 930]


def in_bore(x, rng, wet=0.5):
    """Heard down a cannon's barrel: the tube's modes ringing under it, the top dulled by the iron round it."""
    return unit(mix(lp(x, 4500, 2), dsp.resonate(lp(x, 2500), BORE, q=9) * wet))


def knocks(rng, keys, n, spread, semis, length):
    """Knees and boots and elbows on the iron, a few quick knocks as they wriggle in."""
    parts, t = [], rng.uniform(0.02, 0.08)
    for i in range(n):
        x = ck.align(dsp.vari(ck.get(keys[int(rng.integers(len(keys)))]), semis + rng.uniform(-2, 2)))
        parts.append((t, ck.choke(unit(x), 0.01, rng.uniform(0.04, 0.09)), rng.uniform(-8, 0)))
        t += rng.uniform(*spread)
    return ck.place(parts, length)


@recipe(L_, "climb", "barrel", "Climbing into the cannon: boots and knees on the bore's iron, booming hollow, and a grunt",
        """Feet first into the guard van's cannon: the packs' real heavy and light metal impacts (boots, knees and an elbow
        on the bore), pitched and choked short, rung through the barrel's hollow tube modes so each knock booms down it; the
        coat dragging on the iron (the packs' real cloth); a grunt as they wriggle down to the breech (the crew's modelled
        throat). Each take a different crewmate.""", sources=IRON + LIGHT_IRON + [SWISH], takes=3, lufs=-20)
def climb_barrel(rng, k):
    L = 1.2
    hits = knocks(rng, IRON[:3] + LIGHT_IRON[:3], int(rng.integers(3, 6)), (0.12, 0.26), -5, L)
    coat = unit(hp(dsp.vari(ck.get(SWISH), rng.uniform(-6, -2)), 250, 2))[:samples(0.5)]
    grunt = effort(rng, VOICES[k % 3], 0.3, [(0, 1.1), (0.05, 1.25), (0.3, 1.0)], [(0, "u"), (0.3, "h")],
                   [(0, 0), (0.02, 1), (0.3, 0)], press=0.4, rough=0.3, voiced=0.7)
    y = ck.place([(0, in_bore(hits, rng), 0), (0.15, coat, -10), (rng.uniform(0.35, 0.55), in_bore(grunt, rng, 0.3), -6)], L)
    return unit(y)


@recipe(L_, "climb", "squeeze", "Climbing into the cannon, modelled: a tight squeeze, scraping and thumping inside the tube",
        """Modelled: the scrape of a body sliding down an iron tube (filtered noise rubbing through the bore's modes), two or
        three soft thumps of knees and elbows (the bore's own ring struck), and a muffled 'hup' from inside it.""", takes=3, lufs=-20)
def climb_squeeze(rng, k):
    L = 1.1
    n = samples(L)
    scrape = unit(bp(synth.noise(L, rng, "pink"), 300, 2500)) * env([(0, 0), (0.1, 0.8), (0.6, 1), (0.9, 0.3), (L, 0)], L)
    thumps = ck.place([(t, ck.hollow(rng, BORE, 0.18, q=7), rng.uniform(-6, 0)) for t in sorted(rng.uniform(0.1, 0.8, int(rng.integers(2, 4))))], L)
    hup = effort(rng, VOICES[k % 3], 0.2, [(0, 1.2), (0.2, 1.05)], [(0, "u"), (0.2, "u")], [(0, 0), (0.02, 1), (0.2, 0)],
                 press=0.45, rough=0.2, voiced=0.75)
    y = mix(in_bore(scrape[:n], rng, 0.6) * 0.4, thumps, ck.place([(rng.uniform(0.5, 0.75), in_bore(hup, rng, 0.4), -4)], L))
    return unit(y)


@recipe(L_, "whoop", "whee", "Fired from the cannon: a long delighted 'wheee!' as the shot throws them",
        """The crew's modelled throat (each take a different crewmate: a man, a woman, a bigger man) letting out a long,
        delighted 'wheee!': up at once, held high as they climb, sliding down as they come over the top. Pure fun, never a
        scream of fright (the director: "we're going for fun in this one").""", sources=[GASP], takes=3, lufs=-18)
def whoop_whee(rng, k):
    L = rng.uniform(1.3, 1.7)
    return effort(rng, VOICES[k % 3], L, [(0, 1.4), (0.08, 2.1), (L * 0.5, 2.2), (L * 0.8, 1.7), (L, 1.3)],
                  [(0, "u"), (0.06, "i"), (L * 0.8, "i"), (L, "e")],
                  [(0, 0), (0.05, 1), (L * 0.6, 0.9), (L, 0)], press=0.55, rough=0.1, voiced=0.85, real=GASP)


@recipe(L_, "whoop", "woohoo", "Fired from the cannon: a 'woo-HOO!' as the shot throws them",
        """The same throat in two parts, a cheer: a short 'woo' and a big rising 'HOO!' held over the top of the flight, then
        a laugh of breath as it falls away. Each take a different crewmate.""", takes=3, lufs=-18)
def whoop_woohoo(rng, k):
    a = effort(rng, VOICES[k % 3], 0.3, [(0, 1.5), (0.3, 1.6)], [(0, "u"), (0.3, "u")], [(0, 0), (0.03, 1), (0.3, 0.2)],
               press=0.55, rough=0.1, voiced=0.85)
    L = rng.uniform(1.0, 1.3)
    b = effort(rng, VOICES[k % 3], L, [(0, 1.7), (0.15, 2.3), (L * 0.6, 2.1), (L, 1.4)], [(0, "u"), (0.1, "o"), (L, "o")],
               [(0, 0), (0.04, 1), (L * 0.55, 0.85), (L, 0)], press=0.55, rough=0.1, voiced=0.85)
    return unit(ck.place([(0, a, -2), (0.32, b, 0)], L + 0.4))


def air(rng, length, lo, hi, flap_rate):
    """The air tearing past a body in flight: broadband rush gusting with its tumble, the coat flapping in quick bursts."""
    n = samples(length)
    rush = unit(bp(synth.noise(length, rng, "pink"), lo, hi)) * np.clip(0.65 + 0.35 * W.norm(lp(rng.standard_normal(n).astype(np.float32), 0.8)), 0.2, 1.2)
    t = np.arange(n) / dsp.SR
    flap = unit(hp(synth.noise(length, rng), 900)) * np.clip(np.sin(2 * np.pi * flap_rate * t) ** 8, 0, 1) * \
        np.clip(W.norm(lp(rng.standard_normal(n).astype(np.float32), 1.5)), 0, 1)
    return rush, flap


@recipe(L_, "flight", "rush", "In flight from the cannon: the air tearing past their ears, the coat flapping",
        """Held while they fly: the air tearing past (pink noise, 200 Hz to 3 kHz, gusting as they turn over in it), and the
        coat's skirts and sleeves flapping in quick bursts (filtered noise chopped at a flag's 9 Hz). A 6 s loop.""",
        loop=True, takes=1, lufs=-22, seconds=6.0)
def flight_rush(rng, k):
    L = 6.6
    rush, flap = air(rng, L, 200, 3000, 9.0)
    y = mix(rush * 0.8, flap * 0.25)
    return lp(y, 7000)[:samples(6.3)]


@recipe(L_, "flight", "flutter", "In flight from the cannon, modelled: a lighter whistle of air and the coat cracking like a flag",
        """Lighter: the air thinner and higher past the ears (a whistle of it, 600 Hz to 5 kHz), the coat cracking like a
        flag in fast, sharp bursts (14 Hz). A 6 s loop.""", loop=True, takes=1, lufs=-22, seconds=6.0)
def flight_flutter(rng, k):
    L = 6.6
    rush, flap = air(rng, L, 600, 5000, 14.0)
    y = mix(rush * 0.6, flap * 0.45)
    return lp(y, 8000)[:samples(6.3)]


def rolled(rng, k, body_keys, n_rolls):
    """The landing as a gag: a padded thump, a roll or two along the ground (smaller thumps, the coat slapping), then the
    'oof!' a beat after (the comic timing, as the hook's: note 802)."""
    def thump(semis, db):
        x = unit(ck.align(dsp.vari(ck.get(body_keys[int(rng.integers(len(body_keys)))]), semis)))[:samples(0.3)]
        c = unit(ck.align(dsp.vari(ck.get(DROP), semis - 2)))[:samples(0.3)]
        return (unit(hp(mix(np.pad(x, (0, samples(0.3) - len(x))), np.pad(c, (0, samples(0.3) - len(c))) * 0.5), 180, 4)), db)
    parts, t = [(0.0, *thump(rng.uniform(-3, 0), 0))], 0.0
    for i in range(n_rolls):
        t += rng.uniform(0.18, 0.26)
        s, db = thump(rng.uniform(0, 3), -6 - 4 * i)
        parts.append((t, s, db))
    t += rng.uniform(0.12, 0.2)
    parts.append((t, oof(rng, VOICES[k % 3], GASP, rng.uniform(0.22, 0.3), rng.uniform(1.15, 1.3)), -4))
    return unit(ck.place(parts, t + 0.5))


@recipe(L_, "land", "roll", "Down from the cannon, a gag: a padded thump, a roll or two along the ground, and a comic 'oof!'",
        """Never hurt (note 746): the packs' real soft heavy impacts and heavy leather drop (a body and its coat on the
        ground), the boom taken out so it lands soft; two more smaller as they roll; then the crew's modelled throat's
        'oof!' a beat after, high and dropping away. Each take a different crewmate.""", sources=SOFT + [DROP, GASP], takes=3, lufs=-17)
def land_roll(rng, k):
    return rolled(rng, k, SOFT, 2)


@recipe(L_, "land", "tumble", "Down from the cannon: one big thump, a long tumble over and over, and an 'oof!'",
        """The same, landing faster: one big padded thump and three rolls tumbling away smaller each time, then the 'oof!'.""",
        sources=SOFT + [DROP, GASP], takes=3, lufs=-17)
def land_tumble(rng, k):
    return rolled(rng, k, SOFT, 3)
