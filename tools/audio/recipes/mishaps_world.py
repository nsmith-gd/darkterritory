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
from recipes.crew_items import hit_of, jet as spray_jet
from recipes.crew_kit import recipe, R, S, K
from recipes.ui import tidy
from recipes.world_bed import outdoors
from recipes.world_out import grunt

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
    j = spray_jet(rng, length + 0.06, take + k)
    return gate(j, [(0, 0), (0.004, 1), (length * 0.35, 0.45), (length, 0.06), (length + 0.06, 0)])


def glugs(rng, n, gap=0.09, f=(150, 380), slow=0.18):
    """Gas bubbling back through the dregs: a run of glugs slowing as it goes, rung through the copper's hollow. Each glug
    is a messy cluster, never one clean bubble (a lone bubble's rising ring is a cartoon 'bloop'): a big bubble's low
    ring that hardly rises, a few smaller ones breaking off it, and the wet suck of the water closing behind."""
    parts, t = [], 0.0
    for i in range(n):
        f0 = rng.uniform(*f) * (1 - 0.04 * i)
        c = [(0.0, synth.bubble(f0, length=0.09, rise=rng.uniform(0.04, 0.15)), 0)]
        for _ in range(int(rng.integers(2, 5))):
            c.append((rng.uniform(0.008, 0.045), synth.bubble(f0 * rng.uniform(1.8, 3.5), length=0.05,
                                                             rise=rng.uniform(0.05, 0.25)), rng.uniform(-14, -6)))
        c.append((0.0, ck.slurp(rng, rng.uniform(0.07, 0.11), 200, rng.uniform(600, 1000), bubbles=0), -5))
        parts.append((t, ck.norm(ck.place(c)), -1.2 * i + rng.uniform(-2, 0)))
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


WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")
GURGLE = S("loop_machine_02")            # a cistern's gurgling between its hiss: glugs at 200-700 Hz
GLUGS = [(5.25, 0.38), (6.3, 0.45), (7.82, 0.3), (1.65, 0.3)]     # where in it the gurgles are (start, length)


def wet_spit(rng, k, length):
    """A real splash cut down to its first wet burst and brightened into a spit of water out of a nozzle."""
    x = ck.get(WET[k % 3])
    h = ck.hits(x, floor_db=-14, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    y = ck.cut(x, a - samples(0.002), a + samples(length), 0.002, length * 0.5)
    return ck.norm(hp(dsp.vari(y, rng.uniform(1, 3)), 500))


def real_gurgle(rng, i, semis=-4.0):
    """A real gurgle, the cistern's hiss gated out of it, pitched down for the copper cylinder."""
    a, L = GLUGS[i % len(GLUGS)]
    x = ck.denoise(ck.get(GURGLE[0]), over=2.5, floor=0.03)
    y = ck.cut(x, samples(a), samples(a + L), 0.02, 0.06)
    return ck.norm(lp(dsp.vari(y, semis + rng.uniform(-1, 1)), 1800, 2))


@recipe(M, "extinguisher-dregs", "gurgle",
        "Stand-in: the extinguisher run dry, from real water: a few splashy spits, a gurgle in the cylinder, a last spit",
        """Stand-in until the Sonniss library has a real extinguisher, made from the packs' real water: the spits are
        splashes (sfx_100's wet footsteps) cut to their first wet burst and brightened into a spit from a nozzle, each
        smaller than the last; the gurgle is a cistern's real gurgling (loop_machine_02) with its hiss gated out,
        pitched down for the copper cylinder and rung through its hollow; then a beat and one last weak spit.""",
        sources=WET + GURGLE, takes=2, lufs=-20)
def dregs_gurgle(rng, k):
    g = real_gurgle(rng, k)
    g = ck.norm(mix(g, dsp.resonate(g, [420, 830, 1250], q=8, gains=[1, 0.5, 0.3]) * 0.3))
    if k == 0:
        parts = [(0.0, wet_spit(rng, 0, 0.12), 0), (0.22, wet_spit(rng, 1, 0.07), -6), (0.34, wet_spit(rng, 2, 0.05), -11),
                 (0.52, g, -3), (0.55 + len(g) / SR + 0.35, wet_spit(rng, 0, 0.04), -13)]
    else:
        g2 = real_gurgle(rng, 2, -6)
        parts = [(0.0, wet_spit(rng, 1, 0.09), 0), (0.2, g, -4), (0.2 + len(g) / SR + 0.1, wet_spit(rng, 2, 0.06), -7),
                 (0.35 + len(g) / SR + 0.12, g2, -8), (0.45 + (len(g) + len(g2)) / SR + 0.3, wet_spit(rng, 0, 0.035), -14)]
    return in_hands(ck.place(parts))


# ---- The whistle on no steam ----------------------------------------------------------------------------------------------
# The cab's three-chime whistle (E flat, G, B flat; recipes/crew_train.py) blown when the boiler has nothing to give it.
# A whistle's bells only speak when the jet across their mouths is fast enough to lock onto them; below that it's breath
# through the bells, coloured by them but not a note. On low pressure the pitch sits flat and sinks as the pressure goes,
# the harmonics thin out to almost a sine, and the biggest bell (it needs the most steam) gives up first. It's still the
# train's whistle and still heard far (a tell, held to 200-800 Hz), so it's played straight: the comedy is the effort.

CHIME = (311.1, 392.0, 466.2)


def starved_chord(rng, length, pressure, notes=CHIME, flat=0.45, need=(0.36, 0.3, 0.24)):
    """The three bells on too little steam (crew_train.whistle_tone's model with its pressure taken away): each bell
    speaks only while the pressure is over what it needs, its edge tone flickering in and out of lock near that
    threshold, its pitch flat by `flat` semitones and sinking further as the pressure falls, its harmonics thinning to
    almost a sine; under each, breath rung by the bell."""
    n = samples(length)
    p = synth.curve(pressure, n)
    out = np.zeros(n, np.float32)
    for i, f0 in enumerate(notes):
        speak = np.clip((p - need[i]) / 0.22, 0, 1) ** 1.3
        flicker = np.clip(0.55 + 0.9 * lp(rng.standard_normal(n).astype(np.float32), 7) * 14, 0, 1)
        bend = -flat - 2.2 * np.clip(0.6 - p, 0, None) / 0.6
        drift = lp(rng.standard_normal(n).astype(np.float32), 3) * 4 * 0.006
        f = f0 * 2 ** (bend / 12) * (1 + drift)
        ph = 2 * np.pi * np.cumsum(f) / SR + rng.uniform(0, 2 * np.pi)
        tone = sum(np.sin(h * ph) * np.clip(p, 0, 1) ** (h - 1) / h ** 2 for h in range(1, 4))
        breath = dsp.resonate(synth.noise(length, rng), [f0 * 2 ** (-flat / 12)], q=14)
        out += (ck.norm(tone) * 0.55 * speak * flicker + ck.norm(breath) * 0.5 * np.sqrt(np.clip(p, 0, 1))) * (1.0, 0.85, 0.75)[i]
    return out


def whistle_steam(rng, length, pressure):
    """The steam itself through the whistle's valve and slot: the jet model, kept down where the bells are (a wheeze,
    not a hiss)."""
    return lp(W.jet(length, rng, pressure=pressure, opening=0.8, peak=520, low=0.4, eddy=0.9, tilt=(1.4, 1.5)), 1600, 2)


def above_cab(y, tail=0.3):
    """Where the whistle is: on the boiler above the cab, out in the night (crew_train's whistle space), the night's
    long tail cut short so the take ends when the whistle does."""
    n = len(y) + samples(tail)
    return dsp.fade(dsp.fit(dsp.room(y, "night", wet=0.13, rng=np.random.default_rng(9)), n), 0.002, tail)


@recipe(M, "whistle-wheeze", "sag",
        "The whistle on low steam: a thin, flat chord trying to come up, sagging and dying into breath",
        """The cab's three-chime whistle model (E flat, G, B flat, as crew_train) with the pressure taken away: the
        cord pulled, the steam comes, the chord tries to form (the small bells first, thin, nearly sine tones, a
        quarter-tone flat and flickering in and out of lock), never gets there, and sags: the pitch sinking another
        semitone and a half as the pressure goes, the big bell dropping out first, until it's only breath through the
        bells and then nothing. The steam is the jet model kept low, so it wheezes rather than hisses. Above the cab,
        heard far; held to 200-800 Hz.""", takes=2, band=(200, 800), lufs=-20)
def wheeze_sag(rng, k):
    L = (1.9, 2.2)[k]
    if k == 0:
        p = env([(0, 0), (0.12, 0.38), (0.35, 0.56), (0.6, 0.52), (1.3, 0.3), (1.75, 0.12), (L, 0)], L)
    else:       # a hopeful second breath halfway through that doesn't get as far as the first
        p = env([(0, 0), (0.15, 0.4), (0.4, 0.52), (0.8, 0.34), (1.05, 0.44), (1.7, 0.18), (2.1, 0.06), (L, 0)], L)
    chord = starved_chord(rng, L, p)
    y = mix(ck.norm(chord), ck.norm(whistle_steam(rng, L, p)) * 0.55)
    return above_cab(gate(y, [(0, 0), (0.03, 1), (L - 0.1, 1), (L, 0)]))


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))) + 1e-9)


def bells_breath(rng, length, pressure, notes=CHIME, flat=0.4, need=(0.42, 0.34, 0.28)):
    """The three bells rung by the jet alone, no edge tone at all: the steam's noise through each bell's resonance. As
    the pressure nears what a bell needs, its resonance sharpens towards a note (a breathy, wavering near-tone, the way a
    flute sounds when it's under-blown); as it falls the resonance broadens back into breath. The pitch sags with it."""
    n = samples(length)
    p = synth.curve(pressure, n)
    jet = W.jet(length, rng, pressure=pressure, opening=0.8, peak=560, low=0.15, eddy=1.0, tilt=(1.4, 1.5))
    out = np.zeros(n, np.float32)
    for i, f0 in enumerate(notes):
        fc = f0 * 2 ** ((-flat - 2.0 * np.clip(0.6 - p, 0, None) / 0.6) / 12)
        broad = dsp.sweep_filter(jet, "bp", fc, q=4, block=128)
        sharp = dsp.sweep_filter(jet, "bp", fc, q=70, block=128)
        w = np.clip((p - need[i] + 0.12) / 0.2, 0, 1) ** 1.5
        out += ((broad / rms(broad)) * (1 - w) * 0.35 + (sharp / rms(sharp)) * w) * np.sqrt(np.clip(p, 0, 1)) * (1.0, 0.85, 0.75)[i]
    return out


@recipe(M, "whistle-wheeze", "breath",
        "The whistle on low steam: two pulls on the cord, a breathy near-chord that wavers, sags and gives up",
        """The three bells (E flat, G, B flat) blown by the jet model with nothing behind it, and no oscillators at all:
        the steam's own noise through each bell's resonance, which sharpens towards a note as the pressure comes up (a
        breathy, wavering near-tone, like an under-blown flute) and broadens back into breath as it falls, the pitch
        sagging with it. Two pulls on the cord: the first nearly gets a chord out, the second (a beat later, harder
        on the cord, less in the boiler) gets less, and dies. Above the cab, heard far; held to 200-800 Hz.""",
        takes=2, band=(200, 800), lufs=-20)
def wheeze_breath(rng, k):
    if k == 0:
        L = 2.1
        p = env([(0, 0), (0.12, 0.4), (0.32, 0.5), (0.6, 0.32), (0.78, 0.06), (0.92, 0.04), (1.02, 0.36), (1.25, 0.4),
                 (1.6, 0.16), (1.9, 0.03), (L, 0)], L)
    else:
        L = 2.15
        p = env([(0, 0), (0.18, 0.44), (0.5, 0.48), (0.9, 0.24), (1.05, 0.05), (1.22, 0.04), (1.34, 0.3), (1.5, 0.28),
                 (1.9, 0.05), (L, 0)], L)
    y = bells_breath(rng, L, p)
    y = mix(ck.norm(y), ck.norm(whistle_steam(rng, L, p)) * 0.3)
    return above_cab(gate(y, [(0, 0), (0.03, 1), (L - 0.1, 1), (L, 0)]))


# ---- Livestock taking the slack run-in personally -------------------------------------------------------------------------
# The stock car's animals (recipes/world_out.py: throats synthesised as a glottis through a vocal tract the animal's
# size, hooves from the packs' wood hits) when the slack runs in and the car is yanked under them. The bang itself is
# the train's (bed-slack); this is what the animals make of it: a body thrown against the boards, hooves scrambling for
# footing, a beat, and then the complaint. Inside the slatted car, heard from outside it.

HOOF_SRC = [f"kenney_impact-sounds:{n}_00{i}" for n in ("impactWood_light", "impactWood_medium", "footstep_wood")
            for i in range(5)]


def scramble(rng, length, n, semis, scuff=0.5):
    """Hooves scrabbling for footing on the boards: the stock car's hoof knocks (world_out's: the packs' wood hits and
    wooden footsteps), thick at the jolt and thinning out as the animals find their feet, with a hoof dragged across
    the boards here and there."""
    parts = []
    for i in range(n):
        t = length * (i / n) ** 1.6 + rng.uniform(0, 0.02)
        x = W.rec(HOOF_SRC[int(rng.integers(len(HOOF_SRC)))], semis=rng.uniform(*semis))
        parts.append((t, ck.norm(x), -2.5 * i / n * 6 + rng.uniform(-4, 0)))
        if rng.random() < scuff:
            d = rng.uniform(0.04, 0.09)
            parts.append((t + 0.01, ck.friction(rng, d, 120, 300, 3000, 0.9, env([(0, 0), (0.01, 1), (d, 0)], d)), -10))
    return ck.place(parts)


def against_boards(rng, semis=(-8, -5), weight=70):
    """A body thrown against the car's side: a real wood hit pitched for the bulk, the weight's knock under it."""
    return ck.norm(mix(W.piece(rng, "wood", semis), W.knock(rng, weight, 0.2) * 0.35))


def stock_car(y, rng, length):
    """Inside the slatted stock car, heard from beside it: the car's boxy room, the boards taking the top off, a little
    night, the take ending when the animals do."""
    y = lp(dsp.room(y, "car", wet=0.3, rng=np.random.default_rng(11)), 3800, 2)
    return dsp.fade(dsp.fit(outdoors(y, rng, 0.08), samples(length)), 0.002, 0.15)


def voice(rng, f0_pts, vowels, L, size, shape, **glot):
    """One call: a pitch line [(t, Hz)] through a tract `size` (world_out's sizes: a cow 0.42, a pig 0.55-0.6, a sheep
    0.62) on its vowels, under an envelope [(t, level)]."""
    breath = glot.pop("breath", 0.25)
    g = synth.glottis(env(f0_pts, L, "exp"), L, rng, **glot)
    return synth.tract(g, vowels, size, breath=breath, rng=rng) * dsp.fit(env(shape, L), samples(L))


def startled_moo(rng, L, f0, fall=0.62):
    """A cow's moo, startled: world_out's moo without its slow hum lead-in. It breaks out on the open mouth, jumps up in
    pitch as the jolt lands, then falls away complaining and closes back to the hum."""
    pts = [(0, f0 * 0.92), (0.07, f0 * 1.18), (0.22, f0 * 1.12), (L * 0.6, f0 * 0.85), (L, f0 * fall)]
    vow = [(0, "m"), (0.04, "o"), (0.12, "a"), (L * 0.55, "o"), (L * 0.85, "u"), (L, "m")]
    shape = [(0, 0), (0.035, 1), (0.25, 0.95), (L * 0.7, 0.6), (L, 0)]
    return voice(rng, pts, vow, L, 0.42, shape, jitter=0.012, shimmer=0.14, sub=0.3, rough=0.3)


def grumble(rng, L, f0):
    """A cow's closed-mouth grumble: a low hum through the nose that hardly opens, the afterthought of a complaint."""
    pts = [(0, f0), (L * 0.4, f0 * 1.05), (L, f0 * 0.85)]
    return voice(rng, pts, [(0, "m"), (L * 0.5, "u"), (L, "m")], L, 0.42, [(0, 0), (0.06, 1), (L * 0.6, 0.8), (L, 0)],
                 jitter=0.015, shimmer=0.15, sub=0.4, rough=0.35, breath=0.3)


def snort(rng, L=0.16, size=0.42):
    """A sharp breath out through the nostrils: noise through the tract, fluttering (the nostrils' wings)."""
    b = synth.breath(L, "h", size, rng=rng, shape=env([(0, 0), (0.012, 1), (L, 0)], L))
    return ck.norm(dsp.tremolo(b, rng.uniform(28, 40), 0.5))


@recipe(M, "startle-cattle", "moo",
        "Stand-in: a cow in the stock car jolted by the slack: thrown against the boards, a scramble, a beat, an indignant moo",
        """Stand-in until animal recordings (Sonniss). One cow, played for the timing: the jolt throws her against the
        boards (a real wood hit pitched for the bulk), her hooves scrabble for footing (the stock car's hooves from the
        packs), a beat of silence, then the complaint: world_out's synthesised moo (a glottis at cow pitch through a
        cow-sized tract) with the slow hum lead-in taken off, so it breaks out on the open mouth, jumps up and falls
        away indignant. One take adds a snort first, one a grumbled afterthought. Inside the car, heard from outside.""",
        sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def cattle_moo(rng, k):
    parts = [(0, against_boards(rng), -2), (0.03, scramble(rng, 0.35, 7, (-5, -2)), -3)]
    beat = (0.42, 0.3, 0.5)[k]
    if k == 1:
        parts.append((beat, snort(rng), -6))
        beat += 0.24
    moo = startled_moo(rng, (1.05, 0.95, 0.8)[k], rng.uniform(150, 175))
    parts.append((beat, moo, 0))
    if k == 2:
        parts.append((beat + len(moo) / SR + 0.22, grumble(rng, 0.35, rng.uniform(85, 100)), -7))
    return stock_car(ck.place(parts), rng, 2.0)


@recipe(M, "startle-cattle", "herd",
        "Stand-in: the whole cattle car jolted: bodies against the boards, a heavy scramble, two moos at once, grumbling",
        """Stand-in until animal recordings (Sonniss). The whole car rather than one cow: two or three beasts thrown
        into the boards (real wood hits pitched for the bulk), a heavy scramble of hooves (the packs' wood hits and
        steps), two startled moos breaking out over each other a moment later (the synthesised moo, open-mouthed and
        jumping up in pitch, each its own voice), and then the car settling into closed-mouth grumbles and a snort.
        Inside the car, heard from outside.""", sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def cattle_herd(rng, k):
    parts = [(0, against_boards(rng), -2), (0.07 + 0.03 * k, against_boards(rng, (-9, -6), 60), -5),
             (0.02, scramble(rng, 0.55, 12, (-6, -2)), -2)]
    t = (0.32, 0.25, 0.38)[k]
    parts.append((t, startled_moo(rng, rng.uniform(0.8, 1.0), rng.uniform(160, 185)), 0))
    parts.append((t + rng.uniform(0.12, 0.3), startled_moo(rng, rng.uniform(0.7, 0.9), rng.uniform(115, 135), 0.7), -4))
    end = t + 1.1
    parts.append((end, grumble(rng, rng.uniform(0.35, 0.5), rng.uniform(85, 105)), -8))
    if k != 1:
        parts.append((end + rng.uniform(0.15, 0.3), snort(rng, 0.14), -10))
    if k == 2:
        parts.append((end - 0.15, grumble(rng, 0.4, rng.uniform(105, 120)), -11))
    return stock_car(ck.place(parts), rng, 2.0)


def shriek(rng, L, f0):
    """A pig's scream, startled: world_out's squeal with a hard onset, its pitch leaping up as it breaks out, harsh
    and wavering (a fast irregular wobble in it), sagging as the breath runs out."""
    n = samples(L)
    t = np.arange(n) / SR
    wob = 1 + 0.035 * np.sin(2 * np.pi * rng.uniform(6, 9) * t + rng.uniform(0, 6)) * (1 + 0.5 * lp(rng.standard_normal(n).astype(np.float32), 5) * 10)
    pts = [(0, f0 * 0.75), (0.04, f0 * 1.15), (L * 0.35, f0 * 1.05), (L * 0.8, f0 * 0.95), (L, f0 * 0.7)]
    g = synth.glottis(env(pts, L, "exp") * wob, L, rng, jitter=0.03, shimmer=0.25, rough=0.7, sub=0.15)
    y = synth.tract(g, [(0, "a"), (0.05, "e"), (0.15, "i"), (L * 0.75, "e"), (L, "a")], 0.6, breath=0.4, rng=rng)
    return dsp.saturate(y * dsp.fit(env([(0, 0), (0.015, 1), (L * 0.75, 0.8), (L, 0)], L), n), 6)


def bark(rng):
    """A pig's alarm bark: the grunt made short, loud and harsh, mouth open, all breath and roughness."""
    L = rng.uniform(0.08, 0.13)
    f0 = rng.uniform(180, 260)
    return voice(rng, [(0, f0 * 1.1), (L, f0 * 0.8)], [(0, "a"), (L, "o")], L, 0.55, [(0, 0), (0.008, 1), (L, 0)],
                 jitter=0.04, shimmer=0.3, sub=0.5, rough=0.9, breath=0.8)


@recipe(M, "startle-pigs", "squeal",
        "Stand-in: a pig jolted by the slack: an outraged squeal that goes on too long, a beat, then indignant grunts",
        """Stand-in until animal recordings (Sonniss). One pig, played for the timing: the jolt and its trotters
        skittering on the boards (the packs' wood hits, lighter than the cattle's), an instant squeal (world_out's
        synthesised squeal made startled: a hard onset, the pitch leaping up, harsh and wavering) that goes on a little
        longer than it needs to, then a beat, and two or three short, offended grunts. Inside the car, heard from
        outside.""", sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def pigs_squeal(rng, k):
    parts = [(0, against_boards(rng, (-4, -1), 110), -6), (0.01, scramble(rng, 0.3, 9, (0, 3), 0.3), -4)]
    L = (0.75, 0.9, 0.6)[k]
    parts.append((0.05, shriek(rng, L, rng.uniform(800, 1000)), -6))
    t = 0.05 + L + (0.35, 0.28, 0.45)[k]
    for i in range((2, 3, 2)[k]):
        parts.append((t, grunt(rng), -1 - 2 * i))
        t += rng.uniform(0.18, 0.32)
    return stock_car(ck.place(parts), rng, 2.0)


@recipe(M, "startle-pigs", "herd",
        "Stand-in: the pig car jolted: trotters skittering, alarm barks and squeals from several at once, a grunting row",
        """Stand-in until animal recordings (Sonniss). The whole pen: trotters skittering on the boards (the packs' wood
        hits), several pigs going off at once with alarm barks (the grunt made short, harsh and open-mouthed) and two
        or three overlapping squeals of different pitches, then the row settling into fast, agitated grunting that slows
        as they calm down. Inside the car, heard from outside.""", sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def pigs_herd(rng, k):
    parts = [(0, against_boards(rng, (-5, -2), 100), -6), (0.0, scramble(rng, 0.6, 14, (0, 3), 0.4), -4)]
    for i in range(2 + k % 2):
        parts.append((0.03 + rng.uniform(0, 0.08), bark(rng), 0 - 2 * i))
    for i in range((2, 3, 2)[k]):
        parts.append((0.1 + 0.12 * i + rng.uniform(0, 0.06), shriek(rng, rng.uniform(0.4, 0.7), rng.uniform(650, 1150)), -6 - 3 * i))
    t, gap = 0.75, 0.12
    while t < 1.75:
        parts.append((t, grunt(rng), rng.uniform(-5, 0) - 4 * (t - 0.75)))
        t += gap * rng.uniform(0.6, 1.4)
        gap *= 1.15
    return stock_car(ck.place(parts), rng, 2.0)


def startled_bleat(rng, L, f0, q=None, size=0.62):
    """A sheep's bleat, startled: world_out's bleat (the glottis beating in pulses five to nine times a second, the
    quaver that makes a bleat a bleat) breaking out sharply near the top of its range, 'eh' opening to 'aa', falling."""
    q = q or rng.uniform(6, 9)
    t = np.arange(samples(L)) / SR
    f = env([(0, f0 * 0.9), (0.05, f0 * 1.12), (L * 0.4, f0 * 1.04), (L, f0 * 0.84)], L, "exp") * (1 + 0.035 * np.sin(2 * np.pi * q * t))
    g = synth.glottis(f, L, rng, jitter=0.02, shimmer=0.15, rough=0.25)
    g = g * (0.55 + 0.45 * np.sin(2 * np.pi * q * t) ** 2)
    v = synth.tract(g, [(0, "e"), (L * 0.15, "a"), (L, "a")], size, breath=0.25, rng=rng)
    return v * dsp.fit(env([(0, 0), (0.02, 1), (L * 0.7, 0.8), (L, 0)], L), len(v))


@recipe(M, "startle-sheep", "bleat",
        "Stand-in: a sheep jolted by the slack: small hooves pattering, a surprised 'meh!', a beat, a put-out 'meh-eh-eh'",
        """Stand-in until animal recordings (Sonniss). One sheep, played for the timing: the jolt and small hooves
        pattering on the boards (the packs' wood hits, light and quick), a short surprised bleat breaking out high
        (world_out's synthesised bleat, a glottis beating in pulses through a sheep-sized tract, made sudden), then a
        beat, and a longer, lower bleat with more quaver in it: the complaint. Inside the car, heard from outside.""",
        sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def sheep_bleat(rng, k):
    parts = [(0, against_boards(rng, (-3, 0), 130), -9), (0.0, scramble(rng, 0.35, 10, (1, 4), 0.2), -4)]
    f0 = rng.uniform(300, 360)
    L1 = (0.38, 0.3, 0.45)[k]
    parts.append(((0.06, 0.1, 0.05)[k], startled_bleat(rng, L1, f0), 0))
    t = L1 + (0.42, 0.55, 0.36)[k]
    parts.append((t, startled_bleat(rng, (0.75, 0.6, 0.85)[k], f0 * 0.82, q=rng.uniform(5, 6.5)), -2))
    return stock_car(ck.place(parts), rng, 1.95)


@recipe(M, "startle-sheep", "flock",
        "Stand-in: the sheep car jolted: a patter of hooves, the flock bleating over each other, a beat, one late bleat",
        """Stand-in until animal recordings (Sonniss). The whole flock: many small hooves pattering on the boards at the
        jolt (the packs' wood hits), three or four surprised bleats breaking out over each other at different pitches
        (world_out's synthesised bleat, made sudden), then a beat of quiet and one more bleat from the sheep that has
        only just noticed. Inside the car, heard from outside.""", sources=HOOF_SRC + W.PIECES["wood"], takes=3, lufs=-20)
def sheep_flock(rng, k):
    parts = [(0, against_boards(rng, (-3, 0), 130), -9), (0.0, scramble(rng, 0.5, 16, (1, 5), 0.3), -4)]
    t = 0.05
    for i in range(3 + (k == 1)):
        parts.append((t, startled_bleat(rng, rng.uniform(0.3, 0.55), rng.uniform(250, 400), size=rng.uniform(0.58, 0.68)),
                      -1 - 2.5 * i))
        t += rng.uniform(0.07, 0.16)
    late = (1.25, 1.15, 1.35)[k]
    parts.append((late, startled_bleat(rng, rng.uniform(0.4, 0.5), rng.uniform(270, 320), q=rng.uniform(7, 8.5)), -4))
    return stock_car(ck.place(parts), rng, 1.95)


# ---- The run-end report: a death entered -----------------------------------------------------------------------------------
# The report is railway paperwork (recipes/ui.py), so a death on it is clerical: a rubber stamp inked and brought down,
# dry and close, with no room and no music. The joke, where there is one, is how little ceremony it gets.

U = "ui-run-end"
PLACES = R("bookPlace1", "bookPlace2", "bookPlace3")
LIFT = "kenney_ui-audio:click2"
SOFT = K("impactSoft_heavy")


def ink_lift(rng, which=0):
    """The stamp lifted off the ink pad: the rubber unsticking from the inked felt (a small low tacky click from the
    packs, darkened), and the tin pad knocking the desk as it lets go (a tiny wood tap)."""
    tack = ck.norm(lp(dsp.vari(ck.get(LIFT), rng.uniform(-4, -2)), 2500, 2))
    tap = hit_of(PLACES[(which + 1) % 3], 0, 0.06)
    return ck.place([(0, tack, 0), (0.012, lp(tap, 3000), -16)])


def stamp_down(rng, which, weight=1.0):
    """The stamp brought down on the report: a book slapped on a desk from the packs (its paper and wood), the desk's
    weight under it (a soft heavy hit, choked so it thuds instead of booming)."""
    slap = hit_of(PLACES[which % 3], 0, 0.22, -5)        # the book's flat landing, not the edge that touches first
    thud = ck.choke(ck.norm(ck.get(SOFT[(which + 2) % 5])), 0.03, 0.04)
    return ck.place([(0, dsp.vari(slap, rng.uniform(-2.5, -1)), 0), (0.002, thud * weight, -6)])


@build_recipe(U, "death-stamp", "rubber",
              "A death entered: the stamp lifted off the ink pad, a beat, and thumped down on the report",
              """From the packs' real paperwork, dry and close like the rest of the report: the rubber unsticking from
              the ink pad (a small, low tacky click, darkened) with the tin pad knocking the desk, a clerk's beat, then
              the stamp thumped down (a book slapped on a desk, pitched down a little, with a soft heavy hit choked short
              under it for the desk's weight). One take rocks the stamp to be sure.""",
              sources=[LIFT] + PLACES + SOFT, takes=3, lufs=-24, gap=0.5)
def stamp_rubber(rng, k):
    gap = (0.34, 0.26, 0.42)[k]
    parts = [(0, ink_lift(rng, k), -4), (gap, stamp_down(rng, k), 0)]
    if k == 2:
        parts.append((gap + 0.13, lp(stamp_down(rng, k + 1, 0.4), 1800), -13))
    return tidy(ck.place(parts), 50)


def peel(rng, length=0.02, cut=2500):
    """Rubber pulled off an inked felt: the ink film letting go in a quick run of tiny tacky ticks, dark and soft."""
    n = samples(length)
    x = np.zeros(n + samples(0.01), np.float32)
    for _ in range(int(rng.integers(6, 12))):
        a = samples(length * rng.random() ** 0.7)
        x[a] += rng.uniform(-1, 1)
    x = lp(x, cut, 2) + lp(synth.noise(len(x) / SR, rng), 900, 2) * env([(0, 0.15), (len(x) / SR, 0)], len(x) / SR) * 0.6
    return ck.norm(x)


def desk_thump(rng, force=1.0):
    """A rubber stamp brought down on paper on a wooden desk, modelled: the rubber's soft contact (a few milliseconds,
    so only the desk's low modes speak), the desk top's boards, the paper's slap, the stamp's wooden handle knocking
    in its mount, and the weight of the hand behind it."""
    desk = W.body(rng, rng.uniform(115, 140), W.PLATE, decay=0.035, damp=1.5, contact=0.0035, tilt=0.9, length=0.2)
    handle = W.body(rng, rng.uniform(700, 880), W.BAR, decay=0.025, contact=0.0008, length=0.1)
    n = samples(0.05)
    paper = bp(rng.standard_normal(n).astype(np.float32), 900, 6500) * env([(0, 1), (0.006, 0.3), (0.05, 0)], 0.05, "exp")
    weight = W.knock(rng, rng.uniform(70, 85), 0.09, 0.3)
    return ck.place([(0, ck.norm(desk), 0), (0, ck.norm(paper), -4 + 3 * (force - 1)), (0.001, ck.norm(handle), -14),
                     (0, ck.norm(weight), -6 + 4 * (force - 1))])


@build_recipe(U, "death-stamp", "desk",
              "A death entered: a modelled rubber stamp peeled off its pad, a beat, thumped on the report on a wooden desk",
              """Modelled rather than recorded, so it's exactly a stamp: the rubber peeling off the inked felt (a quick
              run of tiny tacky ticks, dark and soft) and the tin pad's tick on the desk, a clerk's beat, then the thump:
              the rubber's soft contact rings only the desk top's low boards, with the paper's slap, the wooden
              handle's knock and the hand's weight. Dry and close. Three takes: a firm one, a quick one rocked to be
              sure, and one where the stamp sticks to the pad and comes down harder.""", takes=3, lufs=-24, gap=0.5)
def stamp_desk(rng, k):
    tin = W.body(rng, rng.uniform(1300, 1600), W.PLATE, decay=0.005, contact=0.0008, length=0.04, count=6)
    lift = ck.place([(0, peel(rng, (0.02, 0.015, 0.045)[k]), 0), ((0.016, 0.012, 0.04)[k], ck.norm(tin), -18)])
    gap = (0.36, 0.25, 0.5)[k]
    parts = [(0, lift, -6), (gap, desk_thump(rng, (1.0, 0.8, 1.3)[k]), 0)]
    if k == 1:
        parts.append((gap + 0.075, desk_thump(rng, 0.6), -9))
    return tidy(ck.place(parts), 50)


# ---- The run-end report: a death the crew did to themselves --------------------------------------------------------------
# Tunnels, curves, jumps, castings: nobody's fault but their own. The clerk doesn't stamp these, he types the cause in,
# slowly, two fingers, and the line ends where the margin bell rings. The bell is the punchline.

KEYS = S("misc_15", "misc_13", "metal_05", "misc_04")
BELL = S("glass_01")


def strikes():
    """Every key strike in the packs' typewriter-like recordings (sfx_100), cut to the strike and its little ring."""
    out = []
    for key in KEYS:
        x = ck.get(key)
        for a, _ in ck.hits(x, floor_db=-14, gap=0.025):
            out.append(ck.norm(ck.cut(x, a - samples(0.001), a + samples(0.07), 0.0005, 0.03)))
    return out


def typing(rng, times, pool, db=(-3, 0)):
    """Keys at the given times, each a different strike from the pool, never the same one twice running."""
    parts, last = [], -1
    for t in times:
        i = int(rng.integers(len(pool)))
        if i == last:
            i = (i + 1) % len(pool)
        last = i
        parts.append((t, dsp.vari(pool[i], rng.uniform(-1, 1)), rng.uniform(*db)))
    return parts


def margin_bell(rng):
    """The margin bell: the packs' small struck bell (sfx_100 glass_01, a bicycle-bell ding), its first strike only,
    let ring on at its own partials (2.73 kHz, a hair split so it beats, and 8.0 and 8.9 kHz) the way a bell on a
    typewriter's frame rings past the recording's short cut."""
    hit = hit_of(BELL[0], 0, 0.3, -6)
    ring = W.modal([2730, 2737, 8034, 8874], [0.5, 0.45, 0.12, 0.09], [1, 0.6, 0.25, 0.2], 0.9, rng, contact=0.0002)
    return ck.norm(mix(hit, ck.norm(ring) * 0.35))


@build_recipe(U, "own-goal", "keys",
              "A death they did to themselves: the cause typed in, two-fingered and weary, and the margin bell's ding",
              """The report's own typewriter (the strikes cut from sfx_100's typewriter-like mechanisms, as the tally's
              keys), typed slowly: a few keys, a weary pause, a couple more, a longer pause over the last one, and the
              margin bell ringing as it lands (sfx_100's small struck bell, a single ding). Nothing after it. Dry and
              close.""", sources=KEYS + BELL, takes=2, lufs=-24, gap=0.7)
def own_goal_keys(rng, k):
    pool = strikes()
    if k == 0:      # hunt and peck: three keys, a pause, two, a long pause, the last
        times = [0.0, 0.21, 0.36, 0.86, 1.0, 1.58]
    else:           # a brisk run that peters out, a beat, the full stop
        times = [0.0, 0.1, 0.19, 0.31, 0.42, 0.62, 1.22]
    parts = typing(rng, times, pool)
    parts.append((times[-1] + 0.012, margin_bell(rng), 0))
    return tidy(ck.place(parts), 300)


def keystroke(rng, weight=1.0):
    """One key of an office typewriter, modelled from its parts: the key lever bottoming (a small dull click), the
    typebar's slug striking the ribbon and paper on the rubber platen (a hard broadband snap with the platen's dull
    knock under it, and the cast frame ringing briefly), and the escapement letting the carriage step one letter along
    (a little steel tick just after)."""
    n = samples(0.012)
    snap = bp(rng.standard_normal(n).astype(np.float32), 1200, 9000) * env([(0, 1), (0.002, 0.4), (0.012, 0)], 0.012, "exp")
    platen = W.body(rng, rng.uniform(320, 420), W.PLATE, decay=0.008, contact=0.0006, length=0.05, count=8)
    clack = ck.hollow(rng, [rng.uniform(450, 600), 900, 1500, 2300, 3400], 0.035, q=3)
    frame = W.body(rng, rng.uniform(950, 1250), W.PLATE, decay=0.012, contact=0.0003, length=0.06, count=15)
    lever = W.body(rng, rng.uniform(1800, 2400), W.BAR, decay=0.005, contact=0.0008, length=0.02)
    tick = W.body(rng, rng.uniform(3200, 4200), W.BAR, decay=0.004, contact=0.0002, length=0.015)
    return ck.place([(0, ck.norm(lever), -16), (0.018, ck.norm(snap), 0 + 2 * (weight - 1)), (0.018, clack, -3),
                     (0.018, ck.norm(platen), -6), (0.019, ck.norm(frame), -18),
                     (0.018 + rng.uniform(0.016, 0.024), ck.norm(tick), -14)])


def dome_bell(rng, f1=2550.0):
    """A typewriter's margin bell, modelled: a small steel dome struck by a little hammer, its partials (a dome's, not a
    church bell's) each split a few hertz so it beats, the high ones dying fast."""
    fs, ds, am = [], [], []
    for r, d, a in ((1.0, 0.55, 1.0), (2.37, 0.22, 0.45), (4.18, 0.08, 0.25), (6.4, 0.04, 0.12)):
        for split in (0.0, rng.uniform(2.0, 4.0)):
            fs.append(f1 * r + split)
            ds.append(d)
            am.append(a * (0.7 if split else 1.0))
    return ck.norm(W.modal(fs, ds, am, 1.0, rng, contact=0.00015))


@build_recipe(U, "own-goal", "machine",
              "A death they did to themselves: a modelled typewriter, a weary line of keys, the margin bell, and one more key",
              """Modelled from the machine's parts rather than recorded: each key is the lever bottoming, the typebar's
              slug snapping onto the ribbon and paper on the rubber platen (a hard broadband snap and the platen's dull
              knock), the cast frame ringing briefly and the escapement's little tick as the carriage steps on. Typed
              slowly and unevenly, two-fingered, and on the last letter the margin bell (a small steel dome, its
              partials split so it beats) rings. In one take the clerk types one more letter past it, regardless.""",
              takes=2, lufs=-24, gap=0.7)
def own_goal_machine(rng, k):
    if k == 0:
        times = [0.0, 0.16, 0.42, 0.55, 1.12, 1.3]
    else:
        times = [0.0, 0.12, 0.2, 0.47, 0.88, 1.02]
    parts = [(t, keystroke(rng, rng.uniform(0.8, 1.1)), rng.uniform(-3, 0)) for t in times]
    parts.append((times[-1] + 0.02, dome_bell(rng, rng.uniform(2450, 2650)), -8))
    if k == 1:
        parts.append((times[-1] + 0.72, keystroke(rng, 0.8), -2))
    return tidy(ck.place(parts), 200)
