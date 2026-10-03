"""The crew's mishaps where the world does the joke (crew-mishaps): a fouled gun's damp 'pfft', the extinguisher's last
dregs, the whistle blown on no steam, the livestock taking the slack run-in personally; and the run-end report's
paperwork for a death (ui-run-end): the rubber stamp, and the typewriter for a death the crew did to themselves.

The director wants these funny without being cartoons: real sounds, played straight, with comic timing doing the work
(a beat of silence before the payoff, a payoff smaller than the build-up, an effort that dies). So no stingers, no
slide-whistles, no music, and nothing that reads as a bottle being opened (no lone pop ahead of a hiss, no fizz, no
crackle riding on air). Steam and gas are the modelled jet (recipes/world_kit.py), the whistle is the cab's three-chime
model (recipes/crew_train.py) starved of pressure, the animals are the stock car's throats (recipes/world_out.py) made
startled, and the paperwork is the packs' real stamps, keys and bells, or a small model of the machine where the packs
have none.
"""

import numpy as np

import dsp
import synth
from build import recipe as build_recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, R, S, K
from recipes.world_bed import outdoors

SR = dsp.SR
M = "crew-mishaps"


def gate(x, pts):
    """x shaped by an envelope [(t, level), ...] over exactly its length."""
    return (x * dsp.fit(env(pts, len(x) / SR), len(x))).astype(np.float32)


# ---- A fouled gun ---------------------------------------------------------------------------------------------------------
# A muzzle-loader whose charge got wet: the priming in the vent flashes, but the charge under it only smoulders. What
# comes out of the vent is a feeble breath of smoke, not a shot. The joke is the size of it. Nothing tonal at the start
# (a ringing pop is a cork) and no crackle on the hiss (that's a fizz): a soft swell of breath and smoke, cut off short.

def vent_breath(rng, length, pressure, peak=900.0, low=0.9):
    """Gas out of the touch-hole: the jet model through a hole a few millimetres across, at the pressure a damp charge
    can manage (a curve), with the smoke's billow under it."""
    return W.jet(length, rng, pressure=pressure, opening=0.5, peak=peak, low=low, eddy=0.9, tilt=(1.2, 1.1))


def sputters(rng, times, size=1.0, peak=300.0):
    """The vent's last gasps: tiny puffs of smoke at uneven gaps, each a few hundredths of a second of soft, dark breath
    (no edge on it: an edge is a click)."""
    parts = []
    for i, t in enumerate(times):
        L = rng.uniform(0.035, 0.07) * size
        p = env([(0, 0.0), (0.012, 0.3), (L, 0.0)], L + 0.01)
        y = lp(vent_breath(rng, L + 0.01, p, peak * rng.uniform(0.8, 1.25), 0.6), 2200, 2)
        parts.append((t, ck.norm(y), -7 - 5 * i))
    return parts


def short_air(y, rng, length, wet=0.07):
    """Close by the gun in open air, the night's tail cut short: the take is the sound, not the landscape."""
    y = outdoors(y, rng, wet)
    return dsp.fade(dsp.fit(y, samples(length)), 0.001, 0.12)


@recipe(M, "foul-fizzle", "vent",
        "The fouled gun: a feeble 'pfft' of smoke out of the vent, a weak puff, a sputter or two, then nothing",
        """Modelled, not recorded: the damp priming burns as a soft breath of gas out of the touch-hole (the jet model
        through a hole a few millimetres across, swelling in over 20 ms and choking off, never a pop), the smoke's
        billow under it for the puff, then two or three tiny puffs at uneven gaps as it dies, each smaller than the
        last, and nothing after. No crackle and no bubbles on it, so it's smoke and not a fizz. On the gun car's roof.""",
        takes=2, lufs=-20)
def foul_vent(rng, k):
    L = (0.22, 0.28)[k]
    # 'pf': a soft swell; 'ft': the breath held a moment and choked off short, the way a damp charge gives up
    p = env([(0, 0.0), (0.022, 0.32), (0.07, 0.4), (L - 0.05, 0.3), (L - 0.012, 0.08), (L, 0.0)], L)
    pfft = ck.norm(lp(vent_breath(rng, L, p, peak=rng.uniform(700, 850), low=0.5), 6000, 2))
    # the puff: the little smoke that does come out, a soft low billow and nothing more
    puff = ck.norm(lp(synth.noise(0.2, rng, "pink"), 420, 2)) * env([(0, 0), (0.03, 1), (0.2, 0)], 0.2) ** 1.6
    gaps = ([L + 0.12, L + 0.24, L + 0.43], [L + 0.15, L + 0.5])[k]
    return short_air(ck.place([(0, pfft, 0), (0.012, puff, -10)] + sputters(rng, gaps)), rng, 0.95)


BREATH = S("misc_05")          # a short breathy burst of air, broadband, no tone in it
WHUMP = S("misc_32")           # a soft low whoomph of moving air (its head, before the knock at its end)


def air_of(key, start, length, semis, cut, rng):
    """A slice of a real breath of air, varispeeded down into a bigger, slower puff and darkened."""
    x = ck.get(key)
    x = ck.cut(x, samples(start), samples(start + length), 0.012, 0.03)
    return ck.norm(lp(dsp.vari(x, semis + rng.uniform(-0.6, 0.6)), cut, 2))


@recipe(M, "foul-fizzle", "smoke",
        "The fouled gun: a damp, dark 'pfff' of smoke from the vent, a sad little puff, two sputters, nothing",
        """From real air: a breathy burst from the packs (sfx_100 misc_05) varispeeded down most of an octave and
        darkened into a slower, smokier 'pfff' out of the touch-hole, with the soft whoomph of air at the head of
        misc_32 under it for the puff of smoke, then two tiny cut-down breaths of the same as the vent's last sputter,
        and nothing. Soft onsets throughout (no pop) and no crackle, so it's damp powder giving up rather than a fizz.""",
        sources=BREATH + WHUMP, takes=2, lufs=-20)
def foul_smoke(rng, k):
    pfff = air_of(BREATH[0], 0.03 + 0.04 * k, 0.2, -8 - 2 * k, 2600, rng)
    Lp = len(pfff) / SR
    pfff = gate(pfff, [(0, 0), (0.03, 1), (Lp * 0.75, 0.6), (Lp, 0)])        # held, then choked off: the 'ft'
    whump = air_of(WHUMP[0], 0.02, 0.14, -3, 500, rng)
    t, spits = Lp + rng.uniform(0.09, 0.14), []
    for i in range(2):
        s_ = air_of(BREATH[0], rng.uniform(0.05, 0.2), 0.045, -12, 1500, rng)
        w = air_of(WHUMP[0], 0.04, 0.05, -1, 400, rng)
        spits.append((t, gate(mix(s_, w * 0.7), [(0, 0), (0.012, 1), (0.07, 0)]), -3 - 7 * i))
        t += rng.uniform(0.14, 0.24) * (1 + i)
    return short_air(ck.place([(0, pfff, 0), (0.015, whump, -6)] + spits), rng, 0.95)


# ---- The extinguisher's dregs ---------------------------------------------------------------------------------------------
# The soda-acid extinguisher (recipes/crew_items.py: a copper cylinder, its gas driving water out of a hose) after the
# charge has run out: there's a little water left and a little gas, and they come out in turns. The joke is the effort
# going nowhere: a spit, a smaller spit, a gurgle from somewhere inside, a beat, and one last 'pt'.

def spit(rng, k, length, take=0):
    """A slug of water and gas forced out of the nozzle: the extinguisher's own spray (crew_items.jet) for a moment, with
    a hard front (the slug leaving) and a quick fall, its spatter landing just after."""
    from recipes.crew_items import jet
    j = jet(rng, length + 0.06, take + k)
    return gate(j, [(0, 0), (0.004, 1), (length * 0.35, 0.45), (length, 0.06), (length + 0.06, 0)])


def glugs(rng, n, gap=0.09, f=(150, 380), slow=0.18):
    """Gas bubbling back through the dregs: a run of big bubbles (each a low Minnaert ring rising as it nears the
    surface, with the wet suck of the slurp model round it), slowing as it goes, rung through the copper's hollow."""
    parts, t = [], 0.0
    for i in range(n):
        f0 = rng.uniform(*f) * (1 - 0.04 * i)
        b = synth.bubble(f0, length=0.16, rise=rng.uniform(0.5, 1.1))
        s = ck.slurp(rng, 0.09, 180, rng.uniform(500, 800), bubbles=0) * 0.35
        parts.append((t, ck.norm(mix(b, s)), -1.2 * i + rng.uniform(-2, 0)))
        t += gap * rng.uniform(0.75, 1.3) * (1 + slow * i)
    y = ck.place(parts)
    return ck.norm(lp(mix(y, dsp.resonate(y, [420, 830, 1250], q=8, gains=[1, 0.5, 0.3]) * 0.4), 2400, 2))


def gas_sigh(rng, length, level=0.3):
    """The last of the gas through the nozzle: the jet model through the small bore of the hose, weak and dying."""
    p = env([(0, 0), (0.04, level), (length * 0.5, level * 0.6), (length, 0)], length)
    return ck.norm(W.jet(length, rng, pressure=p, opening=0.35, peak=900, low=0.2, eddy=1.0))


def in_hands(y):
    """Held in front of you in a car: close, a breath of the car's room."""
    return ck.space(y, 0.18, 0.08, 6000, seed=13)


@recipe(M, "extinguisher-dregs", "spits",
        "Stand-in: the extinguisher run dry: two or three feeble spits from the nozzle, a gurgle inside, one last 'pt'",
        """Stand-in until the Sonniss library has a real extinguisher. The same copper extinguisher as the spray (its
        jet: pink jet noise, the packs' running water, the spatter), now only managing spits: a slug of water forced
        out with a hard front and a quick fall, each smaller than the one before; then gas bubbling back through the
        dregs (big bubbles' low rising rings with a wet suck round them, slowing, rung through the cylinder's hollow),
        a beat, and one last pathetic spit with the gas sighing out after it.""",
        sources=S("loop_water_02", "loop_water_03"), takes=2, lufs=-20)
def dregs_spits(rng, k):
    if k == 0:
        parts = [(0.0, spit(rng, 0, 0.13), 0), (0.24, spit(rng, 1, 0.07), -5), (0.36, spit(rng, 2, 0.05), -10),
                 (0.55, glugs(rng, 5), -4), (1.32, spit(rng, 3, 0.035), -12), (1.36, gas_sigh(rng, 0.3), -22)]
    else:
        parts = [(0.0, spit(rng, 0, 0.09), -2), (0.17, glugs(rng, 3, 0.11), -5), (0.62, spit(rng, 1, 0.06), -7),
                 (0.75, glugs(rng, 4, 0.08, (130, 300)), -6), (1.12, gas_sigh(rng, 0.45, 0.4), -18),
                 (1.5, spit(rng, 2, 0.03), -14)]
    return in_hands(ck.place(parts))
