"""Heard foley: the crew healing (crew-heal) and emoting (crew-emotes), the fireman's shovel on its rack
(crew-melee), the Car Hugger giving a swallowed crewmate back (cs-car-hugger.spit-out), a seized derelict shunted
(place-derelict), an engine starved of steam dragging its train (state-starved), and a cannonball meeting flesh, a
fort's stone and the train's own iron (crew-cannon-impact).

Gameplay foley first: each is the real thing, built from what it is made of. Cloth, boots, iron, glass, wood and stone
come from the packs' real recordings, cut to their hits and pitched to the object's size; where the packs have no such
recording the part is a small physical model (linen threads breaking in a run, a cork stick-slipping in a glass neck and
the phial's air ringing as it leaves, a tin tube crumpling, a dry journal grinding in its box, steam too weak to bark).
The breaths are a modelled man's throat and mouth: stand-ins until the library has recorded breath. The Hugger giving
back its meal is a creature sound, kitbashed from the same wet chewing, stretched creak and dropped roar its kept
swallow and grind are made of, but the body it lets go lands on the boards as real foley. The cannonball impacts
match the ground, water and doll set in warnings.py: the same distance, level and outdoor tail.
"""

import numpy as np

import dsp
import synth
from build import recipe as loop_recipe
from dsp import SR, samples, lp, hp, bp, env, mix, fit, Bus
from recipes import boarders as B
from recipes import crew_kit as ck
from recipes import warnings as WR
from recipes import world_kit as W
from recipes.crew_feet import heel, toe, on_wood, BOOTS
from recipes.crew_items import hit_of, limbs, tool_ring, CLOTH, LEATHER, SOFT
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_train import cab, car, iron
from recipes.kit import flesh_hit, slap, snap, squelch
from recipes.toys_outro import seamless
from recipes.world_bed import CHIMNEY, circ_outdoors, src_loop

METAL_L, METAL_M, METAL_H = K("impactMetal_light"), K("impactMetal_medium"), K("impactMetal_heavy")
PLATE_L, PLATE_H = K("impactPlate_light"), K("impactPlate_heavy")
SOFT_M = K("impactSoft_medium")
WET = S("footstep_wet_01", "footstep_wet_02", "footstep_wet_03")


def unit(x):
    """RMS to 0.1, so parts are balanced by how loud they are rather than by their peaks."""
    return B.norm(x, 0.1)


def bubble(f, length, rise, amp=1.0):
    """synth.bubble faded out at its end, so a low one cut short doesn't click."""
    return dsp.fade(synth.bubble(f, length=length, rise=rise, amp=amp), 0.0005, 0.03)


# ---- Breath (stand-in) ---------------------------------------------------------------------------------------------------

def breath(rng, length, vowels, shape, voice=0.0, f0=105.0, teeth=0.0, flutter=0.2, tract="man"):
    """A man's breath (a stand-in for a recorded one): noise through a modelled throat and mouth on `vowels`, under the
    envelope `shape`. `voice` mixes in a breathy glottis (a sigh barely voiced), `teeth` the hiss of air through teeth,
    `flutter` the unevenness of real airflow."""
    n = samples(length)
    # the tract's formants on noise alone ring like a whistle; real aspiration is broader, so most of it is plain air
    # through a soft hump and the tract only colours it
    air = ck.norm(lp(hp(synth.noise(length, rng), 350, 1), 2800, 2))
    y = mix(ck.norm(synth.breath(length, vowels, tract, rng)) * 0.55, air * 0.6)
    if voice:
        g = synth.glottis(env([(0, f0), (length, f0 * 0.82)], length, "exp"), length, rng, jitter=0.012, shimmer=0.2, oq=0.75)
        y = mix(y, ck.norm(synth.tract(g, vowels, tract, breath=0.8, rng=rng)) * voice)
    if teeth:
        y = mix(y, ck.norm(bp(synth.noise(length, rng), 3800, 9500)) * teeth)
    fl = 1 + flutter * W.norm(lp(rng.standard_normal(n).astype(np.float32), 14))
    return ck.norm(lp(y, 9000) * fit(env(shape, length), n) * fl)


def sharp_in(rng, length=0.36):
    """A sharp breath in through the teeth (a needle going in): a hiss on 'ee', rising fast, caught short."""
    return breath(rng, length, [(0, "i"), (length, "e")], [(0, 0), (0.05, 1), (length * 0.75, 0.85), (length, 0)],
                  teeth=0.9, flutter=0.3)


def nose_out(rng, length=0.32):
    """A short breath out through the nose, the way one follows a swallow."""
    y = breath(rng, length, [(0, "m"), (length, "m")], [(0, 0), (0.04, 1), (length, 0)], flutter=0.15)
    return ck.norm(hp(y, 700))


# ---- crew-heal.apply: bandages ---------------------------------------------------------------------------------------------

HEAL = 10.0          # one heal loop: four whole uses of 2.5 s, so wherever the hold starts or is cut it's the act
DOSE = HEAL / 4


def heal_loop(rng, y, n):
    """Close on the player's own body: a breath of the space round them, the tail folded round the cycle."""
    return seamless(W.croom(dsp.wrap(y, n), "car", 0.06, rng), n)


def rip(rng, length, rate=450.0):
    """Linen torn across. The physics: threads break one after another as the tear runs, so fast and so evenly that the
    breaks fuse into a rasp at the weave's rate (a few hundred a second, quicker as the hands pull harder); each break a
    tick of noise in the fibres' band, the strip flapping under it."""
    n = samples(length)
    pull = env([(0, 0.25), (min(0.06, length * 0.2), 1.0), (length * 0.65, rng.uniform(0.7, 0.95)), (length, 0.15)], length)
    x = np.zeros(n, np.float32)
    t = 0.0
    while True:
        p = float(pull[min(samples(t), n - 1)])
        t += max(0.3, 1 + 0.3 * rng.standard_normal()) / (rate * (0.35 + 0.9 * p))
        a = samples(t)
        if a >= n:
            break
        x[a] = rng.uniform(0.25, 1.0) * p * (2.5 if rng.random() < 0.03 else 1.0)    # now and then a thicker thread
    k = samples(0.0015)
    tick = (rng.standard_normal(k) * np.exp(-np.linspace(0, 6, k))).astype(np.float32)
    threads = bp(np.convolve(x, tick)[:n].astype(np.float32), 900, 8000)
    flap = synth.rustle(length, 250 * pull + 40, rng, f=(600, 3500), ticks=0.0) * pull
    return ck.norm(mix(ck.norm(threads), ck.norm(flap) * 0.25))


def turn(rng, length):
    """One turn of the bandage round the arm: linen drawn over linen and the sleeve (fabric friction: a soft grainy brush,
    loudest as the roll passes over the top of the arm) and the roll peeling a little off itself."""
    sh = env([(0, 0), (length * 0.45, 1), (length, 0)], length)
    rub = ck.friction(rng, length, 380, 1100, 7500, 0.9, sh)
    brush = synth.rustle(length, 500 * sh + 20, rng, f=(1800, 8000), ticks=0.0) * sh
    peel = synth.crackle(length, 60 * sh + 5, rng, size=(0.0002, 0.001), hi=2500)
    return ck.norm(mix(rub, ck.norm(brush) * 0.5, ck.norm(peel) * 0.25))


def tug(rng, take):
    """The turn pulled tight: the linen snapping taut (the sharp start of a real cloth handling) and the arm taking it."""
    return ck.norm(mix(hp(ck.cloth(rng, take, 0.16), 400), ck.pad(rng, 0.05, 220) * 0.3))


def dose_plan(rng, first, then, n_turns=(3, 5)):
    """One use of a bandage in a 2.5 s slot: a strip torn, then turns wound round and pulled tight every second turn.
    `first(rng, length)` makes the tear, `then(rng, length, i)` a turn; returns [(t, sound, gain), ...] from 0."""
    ev = []
    L = rng.uniform(0.42, 0.6)
    ev.append((0.02, unit(first(rng, L)), 1.0))
    t = 0.02 + L + rng.uniform(0.08, 0.16)
    i = 0
    while t < DOSE - 0.4:
        d = rng.uniform(0.34, 0.46)
        ev.append((t, unit(then(rng, d, i)), rng.uniform(0.3, 0.4)))
        t += d + rng.uniform(0.02, 0.07)
        if i % 2:
            ev.append((t - 0.07, unit(tug(rng, i + int(t * 10))), rng.uniform(0.5, 0.65)))
            t += 0.07
        i += 1
    return ev


@loop_recipe("crew-heal", "apply", "linen",
             "A linen bandage torn and wound tight round an arm: the strip ripped, the turns brushed on and pulled taut",
             """Each 2.5 s of the loop is one bandaging: a strip of linen torn across (modelled from the physics of a
             tear: threads breaking one after another so fast they fuse into a rasp at the weave's rate, a few hundred a
             second, quicker as the hands pull), then three or four turns wound round the arm (linen drawn over linen:
             a soft grainy brush loudest over the top of the arm, the roll peeling off itself) and pulled tight every
             second turn (the sharp start of the packs' real cloth handlings). Close and dry. One 10 s cycle.""",
             sources=CLOTH, loop=True, takes=1, mat="bandages", lufs=-24, seconds=HEAL)
def bandage_linen(rng, k):
    n = samples(HEAL)
    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in dose_plan(rng, lambda r, L: rip(r, L, r.uniform(380, 560)),
                                                              lambda r, L, i: turn(r, L))]
    return heal_loop(rng, W.place(n, ev), n)


TEARS = R("handleSmallLeather2", "drawKnife2")      # real tearing drags (a leather strap, a blade drawn through a sheath)
CRINKLE = R("clothBelt2")                           # a belt's cloth crackling: real fibres crackling


def rip_rec(rng, length, take):
    """A tear from real recordings: a leather strap's tearing drag and a blade drawn out (their zipper-like rasp), the
    ring of the blade cut away, stretched to the strip's length, with the real crackle of cloth fibres in it."""
    a = hp(ck.get(TEARS[take % 2]), 1600 if take % 2 else 900)
    a = dsp.trim_silence(a, -40)
    a = dsp.stretch(a, length / (len(a) / SR))
    c = dsp.trim_silence(hp(ck.get(CRINKLE[0]), 1500), -40)
    s = int(rng.integers(0, max(1, len(c) - samples(length))))
    c = fit(c[s:], samples(length))
    y = mix(ck.norm(a), ck.norm(c) * 0.35, length=samples(length))
    return ck.norm(dsp.fade(y * env([(0, 0.4), (0.02, 1), (length * 0.7, 0.8), (length, 0)], length), 0.003, 0.03))


def turn_rec(rng, length, i):
    """One turn from a real cloth handling, slowed and softened into the long brush of a strip drawn round an arm."""
    c = dsp.vari(ck.cloth(rng, i + int(rng.integers(4)), length * 0.7), -rng.uniform(3, 5))
    c = fit(lp(hp(c, 350), 6000), samples(length))     # its thump slowed is a thud, not a brush: cut away
    return ck.norm(c * np.hanning(len(c)).astype(np.float32) ** 0.7)


@loop_recipe("crew-heal", "apply", "torn",
             "The bandage from real recordings: a tearing drag for the strip ripped, slowed cloth handlings for the turns",
             """Each 2.5 s is one bandaging, from the packs' real recordings: the strip torn (a leather strap's tearing drag
             and a blade drawn through a sheath, their zipper-like rasp kept and the blade's ring cut away, stretched to
             half a second, with a belt's real cloth crackle in it), then the turns wound round (the packs' cloth
             handlings slowed a few semitones and softened into long brushes) and pulled tight every second turn (their
             sharp starts). Close and dry. One 10 s cycle.""",
             sources=TEARS + CRINKLE + CLOTH, loop=True, takes=1, mat="bandages", lufs=-24, seconds=HEAL)
def bandage_torn(rng, k):
    n = samples(HEAL)
    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in dose_plan(rng, lambda r, L, d=d: rip_rec(r, L, d), turn_rec)]
    return heal_loop(rng, W.place(n, ev), n)


# ---- crew-heal.apply: medicine -------------------------------------------------------------------------------------------

def squeak(rng, length, out=True, f=1250.0):
    """A cork twisted in a glass neck: stick-slip (a few hundred slips a second, so it sings) rung through the cork and
    the neck, quickening as it's worked loose, slowing as it's pushed home."""
    r = env([(0, 90), (length * 0.6, 340), (length, 230)], length) if out else env([(0, 260), (length * 0.5, 180), (length, 90)], length)
    body = [f * m * rng.uniform(0.97, 1.03) for m in (1.0, 1.93, 2.81, 4.1)]
    c = synth.creak(length, r, rng, body=body, q=16, jitter=0.3, grit=0.25)
    return ck.norm(hp(c, 500) * env([(0, 0), (0.02, 1), (length * 0.8, 0.8), (length, 0)], length))


def pop(rng, f=None, length=0.1):
    """The cork leaving the neck: the phial's air ringing once at its Helmholtz pitch (a 50 ml bottle: about 400 Hz) as
    the pressure evens out, with the tick of the cork's lip clearing the glass."""
    f = f or rng.uniform(380, 480)
    t = np.arange(samples(length)) / SR
    tone = (np.sin(2 * np.pi * f * t * (1 + 0.6 * t)) * np.exp(-t / 0.012)).astype(np.float32)
    tick = np.zeros_like(tone)
    k = samples(0.0008)
    tick[:k] = rng.standard_normal(k)
    return ck.norm(mix(tone, hp(tick, 2000) * 0.25))


def glugs(rng, count=2):
    """The phial tipped up: liquid leaving the narrow neck lets air in a bubble at a time (a glug each, rising as it
    climbs into the bottle), and the trickle of it onto the tongue (the packs' real trickling water, dark)."""
    b = Bus(0.6)
    t = 0.0
    for _ in range(count):
        b.at(t, bubble(rng.uniform(420, 620), 0.09, rng.uniform(0.3, 0.7)) * rng.uniform(0.6, 1.0))
        t += rng.uniform(0.07, 0.12)
    w = ck.get("sfx_100_v2:loop_water_02")
    a = int(rng.integers(0, len(w) - samples(0.4)))
    trickle = lp(hp(w[a:a + samples(0.35)], 300), 3000) * env([(0, 0), (0.05, 1), (0.35, 0)], 0.35)
    b.at(0.02, ck.norm(trickle) * 0.25)
    return b.x


def gulp(rng):
    """A swallow heard from inside the head: the tongue lifting off the palate (a small wet click), the gulp (a short
    low wet squelch as the liquid's pushed down, one low bubble in it) and the larynx dropping back (a soft knock)."""
    click = synth.click(rng.uniform(1300, 1900), q=5, length=0.015, rng=rng)
    g = ck.norm(lp(squelch(rng, 0.13, 140, 600, 0.8), 1400))
    bub = bubble(rng.uniform(170, 240), 0.16, 0.5)
    knock = ck.norm(W.knock(rng, 120, 0.07, 0.3))
    return ck.place([(0, click, -14), (0.02, g, 0), (0.04, bub, -6), (0.1, knock, -12)])


GLASS = S("glass_01", "glass_04", "glass_06")


def glass_tick(rng, take, semis=4.0):
    """The phial's glass knocked lightly (on a ring, on the teeth): a real glass knock pitched to a small bottle, damped
    by the hand round it."""
    x = hit_of(GLASS[take % 3], int(rng.integers(2)), 0.12)
    return ck.norm(ck.choke(dsp.vari(x, semis + rng.uniform(-1, 1)), 0.004, 0.025))


def palm(rng, take):
    """A palm pressing the cork home: a soft hand tap (the packs' soft impact, small)."""
    return ck.norm(lp(hit_of(SOFT_M[take % 5], 0, 0.08), 2500))


def medicine_dose(rng, d, squeak_out, popper, squeak_in, seat, drink):
    """One dose in a 2.5 s slot: uncorked, drunk in a swallow or two, corked again."""
    so = rng.uniform(0.16, 0.24)
    ev = [(0.02, squeak_out(rng, so, d), 0.5), (0.02 + so, popper(rng, d), 0.9), (0.42, glass_tick(rng, d), 0.25)]
    t = 0.5 + rng.uniform(0, 0.05)
    ev.append((t, drink(rng, d), 0.6))
    t += 0.28
    for j in range(1 if rng.random() < 0.3 else 2):
        ev.append((t, gulp(rng), 0.6))
        t += rng.uniform(0.38, 0.48)
    ev.append((t, nose_out(rng), 0.3))
    t = max(t + 0.15, 1.75)
    si = rng.uniform(0.14, 0.2)
    ev += [(t, squeak_in(rng, si, d), 0.45), (t + si, seat(rng, d), 0.6)]
    return ev


@loop_recipe("crew-heal", "apply", "cork",
             "A small medicine bottle uncorked, a swallow or two, the cork squeaked back in: modelled cork, real glass",
             """Each 2.5 s of the loop is one dose. The cork is modelled: twisted out of the glass neck it squeaks (stick-
             slip a few hundred times a second, rung through cork and neck, quickening as it comes loose) and leaves with
             the phial's air ringing once at its Helmholtz pitch (about 400 Hz for a 50 ml bottle: the 'pok'). The
             bottle's glass taps the teeth (a real glass knock pitched small), it's tipped (air glugging in at the neck,
             the packs' real trickle under it), one or two swallows (a wet click, a low gulp with a bubble, the larynx
             dropping back), a breath out through the nose, and the cork squeaked home and pressed in with the palm.
             Close and dry. One 10 s cycle.""",
             sources=GLASS + ["sfx_100_v2:loop_water_02"] + SOFT_M, loop=True, takes=1, mat="medicine", lufs=-24,
             seconds=HEAL)
def medicine_cork(rng, k):
    n = samples(HEAL)
    f = rng.uniform(1150, 1350)
    fh = rng.uniform(390, 450)

    def seat(r, d):
        return mix(palm(r, d), lp(pop(r, fh * 0.8), 1500) * 0.4)

    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in medicine_dose(
            rng, d, lambda r, L, d: squeak(r, L, True, f), lambda r, d: pop(r, fh * r.uniform(0.97, 1.03)),
            lambda r, L, d: squeak(r, L, False, f), seat, lambda r, d: glugs(r, 2 + d % 2))]
    return heal_loop(rng, W.place(n, ev), n)


SQUEALS = R("creak1", "creak2", "creak3")            # real hinge squeals: stick-slip singing, as a cork does in glass
PLOP = "kenney_impact-sounds:impactGeneric_light_001"   # a real 'plop': a low click and a tone dying in 60 ms
WOOD_L = K("impactWood_light")


def squeal_rec(rng, length, take, up=True):
    """A cork squeaking from a real hinge squeal: a slice of its steady singing pitched up into a small neck's range and
    cut to a twist's length."""
    x = dsp.trim_silence(ck.get(SQUEALS[take % 3]), -30)
    a = int(rng.integers(0, max(1, len(x) - samples(length * 2))))
    x = dsp.vari(x[a:], rng.uniform(5, 7) if up else rng.uniform(3, 5))
    x = fit(x, samples(length))
    return ck.norm(hp(x, 600) * env([(0, 0), (0.015, 1), (length * 0.7, 0.8), (length, 0)], length))


@loop_recipe("crew-heal", "apply", "phial",
             "The medicine bottle from real recordings: a hinge squeal for the cork, a real plop, glass, water, a palm",
             """Each 2.5 s is one dose, as many parts as the packs have real: the cork's squeak is a real hinge squeal's
             steady singing pitched up into a small glass neck's range and cut to a twist; it leaves with a real plop (a
             low click and a tone dying in sixty milliseconds), pitched a little apart each time; the glass taps the
             teeth (a real glass knock pitched small), the medicine trickles (real trickling water) and glugs; going
             home, the cork squeaks lower and is tapped down with the palm over a real light wood knock. The swallows and
             the breath through the nose are modelled. Close and dry. One 10 s cycle.""",
             sources=SQUEALS + [PLOP] + GLASS + ["sfx_100_v2:loop_water_02"] + SOFT_M + WOOD_L, loop=True, takes=1,
             mat="medicine", lufs=-24, seconds=HEAL)
def medicine_phial(rng, k):
    n = samples(HEAL)

    def plop(r, d):
        return ck.norm(hp(dsp.vari(ck.align(ck.get(PLOP)), r.uniform(-2.5, -0.5)), 250))

    def seat(r, d):
        knock = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(WOOD_L[d % 5]), 5))), 0.004, 0.02)
        return mix(palm(r, d), knock * 0.35)

    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in medicine_dose(
            rng, d, lambda r, L, d: squeal_rec(r, L, d, True), plop, lambda r, L, d: squeal_rec(r, L, d + 1, False),
            seat, lambda r, d: glugs(r, 1 + d % 2))]
    return heal_loop(rng, W.place(n, ev), n)


# ---- crew-heal.apply: morphine -------------------------------------------------------------------------------------------

def crumple(rng, length, grain):
    """A collapsible tin tube squeezed flat: thin tin buckling in many small snaps as the thumb presses (crumpling's
    crackle: a few loud, many quiet, a power law), each ringing the tube's tiny modes (`grain(rng)` makes one), over the
    soft creak of the tin bending."""
    n = samples(length)
    press = env([(0, 0), (0.08, 1), (length * 0.6, 0.7), (length, 0)], length)
    b = Bus(length + 0.1)
    t = 0.0
    while True:
        t += rng.exponential(1 / (15 + 120 * float(press[min(samples(t), n - 1)])))
        if t >= length:
            break
        b.at(t, grain(rng) * min(1.0, 0.06 * rng.pareto(1.5) + 0.03))
    bend = synth.creak(length, 30 + 60 * press, rng, body=[f * 2.2 for f in synth.TIN], q=10, grit=0.5) * press
    return ck.norm(mix(ck.norm(b.x), ck.norm(hp(bend, 600)) * 0.06))


def tin_ping(rng):
    """One buckle of the tube's thin tin: a tiny plate's modes, a few kilohertz, gone in milliseconds."""
    f1 = np.exp(rng.uniform(np.log(2200), np.log(6500)))
    return W.body(rng, f1, W.PLATE, decay=rng.uniform(0.004, 0.012), length=0.03, contact=0.00015, count=6)


def cap_off(rng, take):
    """The syrette's little cap worked off the needle: a tight fit giving (a short squeak) and its click coming free (the
    packs' real small metal click, pitched small)."""
    tight = synth.creak(0.05, 600, rng, body=[3100, 4700, 6900], q=12) * env([(0, 0), (0.01, 1), (0.05, 0)], 0.05)
    click = dsp.vari(hit_of("kenney_rpg-audio:metalClick", take % 2, 0.06), 3 + rng.uniform(-1, 1))
    return ck.place([(0, ck.norm(hp(tight, 1500)), -10), (0.045, click, 0)])


def exhale_shaky(rng, length=0.72):
    """A breath let out slowly through the mouth, shaking: the pain and the drug."""
    return breath(rng, length, [(0, "h"), (length * 0.4, "o"), (length, "u")], [(0, 0), (0.06, 1), (length * 0.5, 0.5),
                  (length, 0)], voice=0.06, flutter=0.4)


def syrette_dose(rng, d, cap, squeeze):
    """One syrette in a 2.5 s slot: the cap off, the sleeve shoved back, a sharp breath as the needle goes in, the tube
    squeezed flat, the breath let out."""
    L = rng.uniform(0.6, 0.8)
    return [(0.02, cap(rng, d), 0.6), (0.2, ck.norm(lp(ck.cloth(rng, d, 0.2), 6000)), 0.3),
            (0.48, sharp_in(rng, rng.uniform(0.3, 0.4)), 0.55), (0.78, squeeze(rng, L), 0.7),
            (0.82 + L, exhale_shaky(rng, rng.uniform(0.6, 0.8)), 0.45)]


@loop_recipe("crew-heal", "apply", "syrette",
             "A morphine syrette: the little cap off, a sharp breath through the teeth, the tin tube squeezed flat",
             """Each 2.5 s is one syrette. The cap is worked off the needle (a short squeak of a tight fit and the packs'
             real small metal click, pitched small), the sleeve shoved back (real cloth), a sharp breath in through the
             teeth as the needle goes in (a modelled throat and mouth: a stand-in), then the collapsible tin tube
             squeezed flat between finger and thumb: modelled crumpling, thin tin buckling in many small snaps (a few
             loud, most faint) each ringing the tube's tiny modes, over the soft creak of the tin bending; and the breath
             let out slowly, shaking. Close and dry. One 10 s cycle.""",
             sources=R("metalClick") + CLOTH, loop=True, takes=1, mat="morphine", lufs=-24, seconds=HEAL)
def morphine_syrette(rng, k):
    n = samples(HEAL)
    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in syrette_dose(rng, d, cap_off, lambda r, L: crumple(r, L, tin_ping))]
    return heal_loop(rng, W.place(n, ev), n)


TIN_REC = K("impactTin_medium")


def tin_grain(rng):
    """One buckle of the tube from a real tin hit: its first instant pitched up an octave and more to a thin tube's
    size, choked at once."""
    x = ck.align(ck.get(TIN_REC[int(rng.integers(5))]))
    x = dsp.vari(x[:samples(0.06)], rng.uniform(9, 15))
    return ck.norm(hp(ck.choke(x, 0.001, rng.uniform(0.002, 0.006)), 1500))


def cap_latch(rng, take):
    """The cap off from a real latch: its click pitched up to a tiny cap's."""
    return ck.norm(dsp.vari(hit_of("kenney_rpg-audio:metalLatch", 0, 0.08), 5 + rng.uniform(-1, 1)))


@loop_recipe("crew-heal", "apply", "tin",
             "The syrette from real metal: a latch's click for the cap, the tube's crumple from real tin and a belt's crackle",
             """Each 2.5 s is one syrette, its metal from the packs' real recordings: the cap is a real latch's click
             pitched up to a tiny cap; the tube squeezed flat is crumpling's statistics (many small buckles, a few loud,
             as the thumb presses) played on grains of real tin (Kenney's tin hits, their first instant pitched up an
             octave and more and choked at once) with a belt's real cloth crackle under them. The sleeve is real cloth;
             the sharp breath in and the shaking breath out are the modelled throat (a stand-in). Close and dry. One
             10 s cycle.""",
             sources=R("metalLatch", "clothBelt2") + TIN_REC + CLOTH, loop=True, takes=1, mat="morphine", lufs=-24,
             seconds=HEAL)
def morphine_tin(rng, k):
    n = samples(HEAL)

    def squeeze(r, L):
        c = dsp.trim_silence(hp(ck.get(CRINKLE[0]), 2000), -40)
        c = fit(dsp.stretch(c, L / (len(c) / SR) * 1.05), samples(L))
        return ck.norm(mix(crumple(r, L, tin_grain), ck.norm(c) * 0.3))

    ev = []
    for d in range(4):
        ev += [(d * DOSE + t, x, g) for t, x, g in syrette_dose(rng, d, cap_latch, squeeze)]
    return heal_loop(rng, W.place(n, ev), n)


# ---- crew-heal.done: the breath let out ----------------------------------------------------------------------------------

@recipe("crew-heal", "done", "exhale",
        "Stand-in: a long breath let out through the mouth, unvoiced or barely, the hurt eased",
        """Stand-in until a recorded breath: a man's long exhale through an open mouth (noise through a modelled throat and
        mouth, its formants closing from 'ah' to 'oo' as the jaw rises), quick to come and slow to go, its airflow
        uneven like a real breath's. One take unvoiced, two with a trace of voice in the first of it. Close and dry.""",
        takes=3, lufs=-24)
def done_exhale(rng, k):
    L = rng.uniform(1.6, 2.2)
    vow = [(0, "h"), (0.12, "a"), (L * 0.55, "o"), (L, "u")]
    shape = [(0, 0), (0.08, 1.0), (0.3, 0.75), (L * 0.55, 0.35), (L * 0.85, 0.12), (L, 0)]
    return breath(rng, L, vow, shape, voice=(0.0, 0.07, 0.12)[k], f0=rng.uniform(95, 115), flutter=0.18)


@recipe("crew-heal", "done", "sigh",
        "Stand-in: a quick breath in through the nose, then a long sigh out, barely voiced, the coat settling",
        """Stand-in until a recorded breath: relief as a sigh, in two parts: a short breath drawn in through the nose, then
        a long one let out through the mouth, a little voice in its start ('hhah'), stepping down once as the chest lets
        go and trailing off through nearly closed lips. The shoulders drop with it: the coat settling (the packs' real
        cloth, soft). Close and dry.""", sources=CLOTH, takes=3, lufs=-24)
def done_sigh(rng, k):
    Li = rng.uniform(0.35, 0.5)
    inhale = breath(rng, Li, [(0, "m"), (Li, "m")], [(0, 0), (Li * 0.75, 1), (Li, 0)], flutter=0.2)
    L = rng.uniform(1.5, 2.0)
    step = rng.uniform(0.45, 0.6)
    out = breath(rng, L, [(0, "a"), (step, "a"), (L * 0.7, "o"), (L, "u")],
                 [(0, 0), (0.06, 1.0), (step, 0.8), (step + 0.08, 0.55), (L * 0.75, 0.25), (L, 0)],
                 voice=rng.uniform(0.12, 0.2), f0=rng.uniform(92, 108), flutter=0.22)
    lips = ck.norm(bp(synth.noise(L, rng), 2500, 7000)) * env([(0, 0), (L * 0.6, 0), (L * 0.85, 0.12), (L, 0)], L)
    coat = ck.norm(lp(ck.cloth(rng, k, 0.3), 4000))
    return ck.place([(0, hp(inhale, 600), -8), (Li + 0.05, mix(out, lips), 0), (Li + 0.1, coat, -20)])


# ---- crew-emotes ---------------------------------------------------------------------------------------------------------

def stamp(rng, take, force=1.0):
    """A boot stamped flat on the car's boards: heel and toe down nearly together (a flam), the floor's hollow booming
    under the weight."""
    h = on_wood(rng, heel(rng, take), 1.0, "heel")
    t = on_wood(rng, toe(rng, take + 1), 0.8, "toe")
    return ck.place([(0, h, 0), (rng.uniform(0.01, 0.02), t, -3),
                     (0, ck.hollow(rng, [88, 140, 225], 0.2, q=4), -12 + 4 * (force - 1))])


def step(rng, take):
    """A light step on the spot: heel, then the toe a quick roll later."""
    return ck.place([(0, on_wood(rng, heel(rng, take), 1.0, "heel"), 0),
                     (rng.uniform(0.03, 0.045), on_wood(rng, toe(rng, take + 2), 1.0, "toe"), -4)])


def tap(rng, take):
    """A toe tapped on the boards."""
    return on_wood(rng, toe(rng, take), 1.0, "toe")


SLAP = "kenney_rpg-audio:knifeSlice2"     # its first instant is hands slapped together


def clap(rng, take):
    """Two hands clapped: air squeezed out from between the palms in two or three bursts as they meet, a few ms apart,
    rung by the cupped hollow between them (about 1 kHz), the skin's slap, and a real hand slap's first instant."""
    L = 0.09
    n = samples(L)
    x = np.zeros(n, np.float32)
    t = 0.0
    for _ in range(int(rng.integers(2, 4))):
        a, k = samples(t), samples(rng.uniform(0.001, 0.002))
        x[a:a + k] += rng.standard_normal(k) * rng.uniform(0.5, 1.0) * np.hanning(k)
        t += rng.uniform(0.0015, 0.004)
    cav = dsp.resonate(x, [rng.uniform(850, 1300), rng.uniform(1900, 2600)], q=5, gains=[1, 0.4])
    y = mix(ck.norm(cav), ck.norm(hp(x, 1800)) * 0.5) * env([(0, 1), (L, 0)], L, "exp")
    real = ck.choke(hp(hit_of(SLAP, 0, 0.06), 600), 0.004, 0.012)
    return ck.norm(mix(ck.norm(y), real * 0.4))


# Two bars of a jig in 6/8 and a closing bar, as (eighth, what, dB); claps as (eighth, dB). Eighths count from the first
# bar's downbeat; each take its own figure.
JIGS = [
    dict(steps=[(0, "stamp", 0), (2, "step", -5), (3, "stamp", -2), (5, "step", -5),
                (6, "stamp", 0), (7, "tap", -9), (8, "step", -5), (9, "stamp", -1), (10, "tap", -9), (11, "step", -5),
                (12, "stamp", 0), (13, "stamp", -3), (14, "stamp", 0)],
         claps=[(6, -3), (9, -5), (12, -2)]),
    dict(steps=[(0, "stamp", 0), (1, "tap", -8), (2, "tap", -9), (3, "stamp", -1), (4, "step", -6), (5, "step", -6),
                (6, "stamp", 0), (7, "tap", -8), (8, "tap", -9), (9, "stamp", -1), (10, "step", -6), (11, "step", -6),
                (12, "stamp", 0), (15, "stamp", 1)],
         claps=[(12, -3), (15, -1)]),
]
CLINKS = S("metal_02", "metal_06") + R("beltHandle1")


@recipe("crew-emotes", "dance", "jig",
        "A jig on the spot on the car's boards: boots stamping and tapping in 6/8, hands clapping on the beat",
        """Real boots on boards (Kenney's RPG footsteps cut into heels and toes, as the footsteps are built): stamps (heel
        and toe slammed down together, the car floor's hollow booming under them), toe taps and light steps, in two bars
        of a jig in 6/8 at about 115 to the dotted crotchet and a closing bar of stamps, a hair loose in time like a man
        dancing. Hands clap on the beat: two or three bursts of air between the palms rung by the cupped hollow, with a
        real hand slap's first instant. The coat swings and the kit on the belt clinks now and then. In the car.""",
        sources=BOOTS + [SLAP] + CLOTH + CLINKS, takes=2, lufs=-18)
def dance_jig(rng, k):
    fig = JIGS[k]
    e8 = 60 / rng.uniform(110, 120) / 3
    parts = []
    for i, (q, what, db) in enumerate(fig["steps"]):
        t = 0.02 + q * e8 * (1.04 if q % 3 == 0 else 0.98) + rng.normal(0, 0.006)
        x = {"stamp": lambda: stamp(rng, i + k, 1.0 + 0.3 * (db >= 0)), "step": lambda: step(rng, i + k),
             "tap": lambda: tap(rng, i + k)}[what]()
        parts.append((t, x, db))
        if what == "stamp" and rng.random() < 0.6:
            parts.append((t, ck.norm(lp(ck.cloth(rng, i, 0.2), 5000)), -20))
        if what == "stamp" and rng.random() < 0.3:
            parts.append((t + 0.02, ck.norm(hit_of(CLINKS[i % 3], 0, 0.1)), -28))
    for q, db in fig["claps"]:
        parts.append((0.02 + q * e8 + rng.normal(0, 0.008), clap(rng, q), db + 2))
    return car(ck.place(parts))


def swish(rng, length, lo=130, hi=1000, peak=0.55):
    """A heavy wool sleeve swung through the air: the low whoosh of its bulk and the cloth's own rub at the elbow and
    shoulder as the arm moves."""
    sh = env([(0, 0), (length * peak, 1), (length, 0)], length)
    return mix(unit(ck.whoosh(rng, length, lo, hi, peak, 0.9)), unit(ck.friction(rng, length, 260, 900, 5500, 0.9, sh)) * 0.45)


def dry(y):
    """Close, with just the car's walls round it."""
    return dsp.room(y, "car", wet=0.08, rng=np.random.default_rng(11))


@recipe("crew-emotes", "wave", "sleeve",
        "An arm raised and waved in a heavy coat: the sleeve's rustle going up, then two or three swishes side to side",
        """The arm going up (the packs' real cloth handling, the coat's shoulder bunching, with a slow low whoosh), then
        waved two to four times side to side, each swing a heavy wool sleeve moving air (a low band of noise that rises
        and falls with the arm's speed, never a tone) with the wool rubbing at the elbow (stick-slip friction, soft), the
        later swings a little lazier; the coat settling as it comes down. Takes differ in the number and speed of the
        swings.""", sources=CLOTH, takes=3, lufs=-24)
def wave_sleeve(rng, k):
    parts = [(0, unit(ck.cloth(rng, k, 0.3)), -3), (0.05, swish(rng, 0.42, 100, 700, 0.7), -5)]
    t = 0.42
    swings = (2, 3, 4)[k]
    for i in range(swings):
        d = rng.uniform(0.25, 0.32) * (1 + 0.08 * i)
        parts.append((t, swish(rng, d, 150, rng.uniform(900, 1200), rng.uniform(0.45, 0.6)), -2 * (i % 2) - i))
        t += d * rng.uniform(0.85, 0.95)
    parts.append((t + 0.05, unit(lp(ck.cloth(rng, k + 1, 0.25), 4000)), -12))
    return dry(ck.place(parts))


@recipe("crew-emotes", "point", "sleeve",
        "An arm thrown out to point: a quick swish and the coat sleeve snapping taut, the coat following",
        """The arm thrown out fast (a short swish, quicker and brighter than a wave's), the sleeve snapping taut at the end
        of it (the sharp start of the packs' real cloth handlings and the fabric's crack), then the coat's body
        following round a moment later (a softer real cloth rustle). Takes differ in the speed of the throw and the
        cloth.""", sources=CLOTH, takes=3, lufs=-22)
def point_sleeve(rng, k):
    L = rng.uniform(0.15, 0.2)
    throw = ck.whoosh(rng, L, 200, rng.uniform(1600, 2200), 0.75, 1.1)
    snap_at = L * 0.85
    taut = hp(ck.cloth(rng, k, 0.12), 300)
    crack = ck.norm(bp(synth.noise(0.012, rng), 900, 5000) * env([(0, 1), (0.012, 0)], 0.012, "exp"))
    follow = lp(ck.cloth(rng, k + 2, 0.25), 5000)
    return dry(ck.place([(0, unit(throw), -2), (snap_at, unit(taut), 0), (snap_at, unit(crack), -10),
                         (L + rng.uniform(0.05, 0.09), unit(follow), -9)]))


def sleeve_in(rng, length):
    """An arm pushed down a coat sleeve: the lining sliding over the shirt (a long smooth friction, brighter than wool)
    and the wool bunching round it."""
    sh = env([(0, 0), (0.06, 1), (length * 0.7, 0.8), (length, 0)], length)
    lining = ck.friction(rng, length, 520, 1600, 9000, 0.6, sh)
    bunch = synth.rustle(length, 300 * sh + 20, rng, f=(900, 4500), ticks=0.0) * sh
    return mix(unit(lining), unit(bunch) * 0.6)


BUTTONS = ["kenney_ui-audio:click4", "kenney_ui-audio:click5", "kenney_ui-audio:click3"]


def button(rng, take):
    """One button done up: the buttonhole's cloth dragged over it (a short friction), the fingers tugging the front, and
    the small hard tick of the horn button clearing the hole (a real tiny click pitched down to a coat button)."""
    rub = ck.friction(rng, 0.07, 400, 1200, 7000, 0.7, env([(0, 0), (0.02, 1), (0.07, 0)], 0.07))
    tick = ck.norm(lp(dsp.vari(hit_of(BUTTONS[take % 3], 0, 0.04), -rng.uniform(3, 5)), 7000))
    return ck.place([(0, unit(rub), -4), (0, unit(hp(ck.cloth(rng, take, 0.08), 500)), -8), (0.06, unit(tick), 0)])


@recipe("crew-emotes", "outfit", "coat",
        "A heavy coat shrugged on: swung round the shoulders, both arms down the sleeves, settled, buttons done up",
        """The coat swung round onto the shoulders (a big low whoosh of heavy cloth and the packs' real cloth handling,
        slowed for the weight), an arm pushed down each sleeve (the lining sliding over the shirt, the wool bunching
        round it, the hand flicking out of the cuff), a shrug to settle it (the heavy cloth dropping onto the shoulders
        with a dull weight), the fronts pulled together, and three or four buttons done up a beat apart (the buttonhole's
        cloth dragged over each and a horn button's small hard tick, a real tiny click pitched down).""",
        sources=CLOTH + BUTTONS, takes=2, lufs=-23)
def outfit_coat(rng, k):
    parts = [(0, unit(ck.whoosh(rng, 0.45, 80, 600, 0.6, 0.8)), 0),
             (0.05, unit(dsp.vari(ck.cloth(rng, k, 0.4), -3)), -1)]
    t = 0.55 + rng.uniform(0, 0.08)
    for arm in range(2):
        L = rng.uniform(0.42, 0.52)
        parts += [(t, sleeve_in(rng, L), -4), (t + L - 0.04, unit(hp(ck.cloth(rng, k + arm + 1, 0.12), 300)), -6)]
        t += L + rng.uniform(0.12, 0.2)
    parts += [(t, unit(dsp.vari(ck.cloth(rng, k + 2, 0.3), -4)), -3), (t + 0.02, unit(ck.pad(rng, 0.08, 200)), -8)]
    t += rng.uniform(0.35, 0.45)
    parts.append((t, unit(hp(ck.cloth(rng, k + 3, 0.15), 300)), -4))
    t += rng.uniform(0.35, 0.45)
    for b in range(3 + k):
        parts.append((t, button(rng, b + k), -2))
        t += rng.uniform(0.36, 0.5)
    return dry(ck.place(parts))


# ---- crew-melee.shovel-rack-off / -on --------------------------------------------------------------------------------------

def bracket(rng, take, semis=-4.0, tau=0.05):
    """The rack's iron bracket struck by the blade: a light iron hit pitched to a small bolted bracket, its ring cut short
    by the cab wall it's bolted to."""
    x = ck.align(dsp.vari(ck.get(METAL_L[take % 5]), semis + rng.uniform(-0.5, 0.5)))
    return ck.norm(ck.choke(x, 0.006, tau))


def blade_knock(rng, take, tau=0.03):
    """The flat of the blade knocking the bracket: thin plate, a little lower than the tang."""
    x = ck.align(dsp.vari(ck.get(PLATE_L[take % 5]), -1.5 + rng.uniform(-0.5, 0.5)))
    return ck.norm(hp(ck.choke(x, 0.008, tau), 200))


def scrape(rng, length):
    """Steel drawn over iron: the blade's back edge sliding on the bracket."""
    return ck.friction(rng, length, 420, 900, 7000, 0.5, env([(0, 0), (0.015, 1), (length * 0.7, 0.8), (length, 0)], length))


def bracket_ring(rng):
    """The empty bracket ringing on a moment once the weight's off it: a short iron bar's modes, quickly damped."""
    return ck.norm(W.body(rng, rng.uniform(1300, 1900), W.BAR, decay=0.1, contact=0.0003, length=0.3, count=3))


@recipe("crew-melee", "shovel-rack-off", "hooks",
        "The coal shovel lifted off its iron rack: a glove on the shaft, the blade scraping up the bracket, a clack free",
        """The gloved hand closing on the ash shaft (the packs' small leather handling), the blade's back edge scraping up
        over the iron bracket (steel on iron, stick-slip friction, a tenth of a second), and the clack as it clears the
        hook: the shovel's thin blade and its tang (the same plate and light metal as the shovel's other sounds) on the
        bracket's iron (a light metal hit pitched down, its ring choked by the cab wall it's bolted to), the empty bracket
        ringing on a moment. A coat sleeve. In the iron cab.""",
        sources=LEATHER + PLATE_L + METAL_L + CLOTH, takes=2, lufs=-21)
def rack_off(rng, k):
    L = rng.uniform(0.08, 0.13)
    t = 0.1 + rng.uniform(0, 0.03)
    c = t + L
    return cab(ck.place([(0, hit_of(LEATHER[k % 2], 0, 0.2), -10), (t, scrape(rng, L), -10),
                         (c, tool_ring(rng, "shovel", k, 0.6), -3), (c, bracket(rng, k), -4),
                         (c + 0.004, blade_knock(rng, k), -7), (c + 0.01, bracket_ring(rng), -22),
                         (c + 0.12, ck.norm(lp(ck.cloth(rng, k, 0.25), 5000)), -16)]))


@recipe("crew-melee", "shovel-rack-on", "hooks",
        "The coal shovel hung back on its rack: the blade clacked onto the iron hooks, a short scrape down, settled",
        """The blade brought round onto the rack (a little air), then iron on iron: the shovel's blade and tang clacking
        onto the bracket (thin plate and light metal, the bracket a light iron hit pitched down and choked), a short
        scrape as it slides down into its seat, and a smaller clack as it settles, the blade left ringing a little
        longer since it hangs free; the glove letting go. In the iron cab.""",
        sources=LEATHER + PLATE_L + METAL_L, takes=2, lufs=-21)
def rack_on(rng, k):
    c = 0.12 + rng.uniform(0, 0.03)
    L = rng.uniform(0.05, 0.09)
    return cab(ck.place([(0, unit(ck.whoosh(rng, 0.16, 150, 900, 0.7)), -18),
                         (c, tool_ring(rng, "shovel", k + 2, 1.0), 0), (c, bracket(rng, k + 2), -2),
                         (c + 0.003, blade_knock(rng, k + 1), -4), (c + 0.012, scrape(rng, L), -11),
                         (c + 0.012 + L, tool_ring(rng, "shovel", k + 3, 0.5), -9),
                         (c + 0.012 + L, bracket(rng, k + 3, -5, 0.03), -10),
                         (c + 0.3 + rng.uniform(0, 0.08), hit_of(LEATHER[(k + 1) % 2], 0, 0.2), -14)]))


# ---- cs-car-hugger.spit-out ------------------------------------------------------------------------------------------------

ROAR = "sfx_100_v2:misc_24"
SLOSH = "sfx_100_v2:loop_machine_02"      # water sloshing in a drum: its loudest splash, 5.2-5.7 s
STREAM = "sfx_100_v2:loop_water_02"


def prise(rng, length):
    """The mouth forced open: its lip-ring wrenched apart, a seal of slime breaking (a wet band sweeping down as the gap
    widens), mucus strings snapping across it, the chewing squish of its flesh (the kept swallow's, an octave down)."""
    seal = dsp.sweep_filter(synth.noise(length, rng), "bp", env([(0, 900), (length * 0.4, 420), (length, 220)], length, "exp"), q=5)
    seal = seal * env([(0, 0), (0.03, 1), (length * 0.6, 0.7), (length, 0)], length)
    strings = bp(synth.crackle(length, env([(0, 10), (length * 0.3, 70), (length, 20)], length), rng, size=(0.001, 0.004),
                               hi=900), 900, 4500)
    chew = B.grain(rng, B.SQUISH[0], length, -12, attack=False)
    bub = synth.bubbles(length, 40, 200, 900, rng, rise=(0.2, 0.8))
    return mix(unit(seal), unit(strings) * 0.5, unit(chew) * 0.7, unit(bub) * 0.35)


def heave(rng, length, take):
    """The heave: its throat pushing the meal back out, the kept swallow run the other way: a roar played forward (out,
    where the swallow's was reversed, in) an octave down and ring-modulated at 38 Hz so it shudders, the throat
    stretching (a door creak two octaves down), the fluid it's full of slopping."""
    roar = dsp.vari(ck.get(ROAR), -12 + rng.uniform(-1, 1))
    roar = lp(dsp.ringmod(fit(dsp.stretch(roar, length / (len(roar) / SR), smooth=True), samples(length)), 38, 0.6), 1200)
    roar = roar * env([(0, 0), (length * 0.55, 1), (length * 0.8, 0.6), (length, 0)], length)
    creak = lp(dsp.vari(ck.get(B.CREAKS[take % 2]), -20), 1500)
    creak = fit(dsp.stretch(creak, length / (len(creak) / SR), smooth=True), samples(length)) * env([(0, 0), (length * 0.3, 1), (length, 0)], length)
    return mix(unit(roar), unit(creak) * 0.6, unit(B.slosh(rng, length)) * 0.6)


def slide_out(rng, length):
    """A body slithering out of the throat on its slime: a long wet slurp opening as it comes free, the coat dragging
    through the slime (a wet, squelching friction)."""
    sh = env([(0, 0), (0.08, 1), (length * 0.8, 0.8), (length, 0)], length)
    drag = ck.friction(rng, length, 120, 300, 2500, 1.0, sh)
    return mix(unit(ck.slurp(rng, length, 120, 900, rise=True, bubbles=0.4)), unit(drag) * 0.5,
               unit(squelch(rng, length, 150, 800, 1.2)) * 0.5)


def splashes(rng, take, n=2, semis=(-6, -3)):
    """The slime it comes out in hitting the boards: real wet steps (splashes) pitched down for the volume."""
    b = Bus(0.8)
    for j in range(n):
        x = ck.align(dsp.vari(ck.get(WET[(take + j) % 3]), rng.uniform(*semis)))
        b.at(j * rng.uniform(0.02, 0.06), unit(x) * rng.uniform(0.6, 1.0))
    return b.x


def drips(rng, length, rate=8, start=0.0):
    """Slime dripping and spattering off the body and the lip onto the boards: small wet ticks, thinning out."""
    b = Bus(length + 0.3)
    t = start
    while True:
        t += rng.exponential(1 / (rate * np.exp(-(t - start) / (length * 0.5)) + 0.5))
        if t >= length:
            return b.x
        d = mix(slap(rng, rng.uniform(0.02, 0.05), rng.uniform(1500, 3000), 0.8) * 0.6,
                bubble(rng.uniform(500, 1400), 0.05, 0.5) * 0.4)
        b.at(t, unit(d) * rng.uniform(0.2, 1.0))


@recipe("cs-car-hugger", "spit-out", "heave",
        "Its mouth forced open, a heaving roar run outward, the crewmate slithering out and landing on the boards",
        """The kept swallow's own parts run the other way. The mouth wrenched open: a seal of slime breaking (a wet band
        sweeping down as the gap widens), mucus strings snapping, the chewing squish its swallow and grind are made of.
        Then the heave: the roar the swallow played reversed, here forward and an octave down, ring-modulated at 38 Hz so
        it shudders, with the throat stretching (a door creak two octaves down) and its fluid slopping inside. The body
        slithers out on its slime (a long wet slurp opening as it comes free, the coat dragging) and lands on the boards
        as real foley (the crew's body-fall: a soft heavy hit and the leather drop, limbs after, the car floor's knock),
        slime splashing round it (real wet steps pitched down) and dripping; the lips slap back shut.""",
        sources=B.SQUISH + [ROAR] + B.CREAKS + SOFT + R("dropLeather") + CLOTH + ck.FLOOR_SOURCES["wood"] + WET,
        takes=2, lufs=-19)
def spit_heave(rng, k):
    b = Bus(3.8)
    Lp = rng.uniform(0.5, 0.65)
    b.at(0, prise(rng, Lp), -2)
    th = Lp * 0.6
    Lh = rng.uniform(0.95, 1.2)
    b.at(th, heave(rng, Lh, k), 2)
    ts = th + Lh * 0.55
    Ls = rng.uniform(0.5, 0.65)
    b.at(ts, slide_out(rng, Ls), -3)
    tl = ts + Ls - 0.05
    b.at(tl, unit(limbs(rng, k, "wood", 1.6)), 1)
    b.at(tl, splashes(rng, k), -5)
    b.at(tl + 0.05, drips(rng, 0.9, 14), -12)
    b.at(tl + rng.uniform(0.35, 0.5), unit(mix(slap(rng, 0.14, 1200, 0.9), B.thud(rng, 70, 0.3, 0.2))), -8)
    return B.settle(dsp.room(b.x, "car", wet=0.15, rng=rng))


def wet_pops(rng, length, rate0=8, rate1=40):
    """Its lip-ring tearing free of the van's end: the sticky seal letting go in a run of wet pops that quickens."""
    b = Bus(length + 0.2)
    t = 0.0
    while True:
        r = rate0 + (rate1 - rate0) * min(1.0, t / length)
        t += rng.exponential(1 / r)
        if t >= length:
            return b.x
        b.at(t, unit(slap(rng, rng.uniform(0.03, 0.07), rng.uniform(900, 1800), 1.0)) * rng.uniform(0.3, 1.0))


def gush(rng, length):
    """The disgorge: a sheet of its fluid thrown out with the body (the packs' real splash from a sloshing drum, its motor
    hum notched out, pitched down for the volume), a throatful of big low bubbles bursting in it, and the wet slurp of
    the throat emptying."""
    semis = -6 + rng.uniform(-1, 1)
    sheet = dsp.peak(W.rec(SLOSH, semis=semis, start=5.2 + rng.uniform(0, 0.1), length=0.6), 190 * 2 ** (semis / 12), 8, -15)
    sheet = WR.shaped(sheet, [(0, 0), (0.02, 1), (0.3, 0.7), (length, 0)], length)
    bub = synth.bubbles(length, env([(0, 90), (length, 10)], length), 60, 300, rng, rise=(0.1, 0.5))
    return mix(unit(sheet), unit(bub) * 0.6, unit(ck.slurp(rng, length, 90, 700, bubbles=0.3)) * 0.7)


@recipe("cs-car-hugger", "spit-out", "gush",
        "The guard van's timbers groaning as its lips are levered off, a gush of slime and the crewmate in it, landing",
        """The crew lever its mouth off the van's end: the van's timbers groaning under the bar (boards slipping against
        each other, a real door creak dragged down an octave, as in its hits) and its lip-ring tearing free in a run of
        sticky wet pops that quickens. Then it disgorges: a sheet of its fluid thrown out (the packs' real splash from a
        sloshing drum, pitched down for the volume), big low bubbles bursting, the throat emptying, the body sliding out
        on it and landing on the boards as real foley (the crew's body-fall on wood), real wet splashes round it, and the
        fluid running off over the boards and between them (the packs' real trickling water, darkened) with drips
        thinning out.""",
        sources=B.CREAKS + [SLOSH, STREAM] + SOFT + R("dropLeather") + CLOTH + ck.FLOOR_SOURCES["wood"] + WET,
        takes=2, lufs=-19)
def spit_gush(rng, k):
    b = Bus(3.8)
    Lg = rng.uniform(0.7, 0.9)
    b.at(0, unit(B.van_groan(rng, Lg, rng.uniform(40, 60))), -6)
    b.at(0.08, wet_pops(rng, Lg - 0.1), -6)
    tg = Lg - 0.05
    b.at(tg, gush(rng, 0.8), 0)
    b.at(tg + 0.12, slide_out(rng, 0.45), -5)
    tl = tg + rng.uniform(0.5, 0.6)
    b.at(tl, unit(limbs(rng, k + 1, "wood", 1.7)), 3)
    b.at(tl, splashes(rng, k + 1, 3, (-5, -2)), -3)
    w = ck.get(STREAM)
    a = int(rng.integers(0, len(w) - samples(1.6)))
    run = lp(hp(dsp.vari(w[a:a + samples(1.6)], -3), 150), 2500)
    b.at(tl + 0.1, unit(WR.shaped(run, [(0, 0), (0.15, 1), (0.9, 0.4), (1.6, 0)], 1.6)), -11)
    b.at(tl + 0.1, drips(rng, 1.6, 12), -13)
    return B.settle(dsp.room(b.x, "car", wet=0.15, rng=rng))


# ---- place-derelict.roll: a seized car shunted --------------------------------------------------------------------------------

DERELICT = 12.0
TURN = 1.2           # one turn of a flat-spotted wheel (the brief: a thump about every 1.2 s); ten turns a cycle
IRON_BOX = [f * 0.75 for f in synth.IRON]          # an axle box and its guides: a heavy iron casting's modes, lower
JOURNAL = [210, 430, 760, 1150, 1700, 2400]         # a dry journal rubbing in its brass: where the grinding sings


def flat(rng, take):
    """A flat spot coming round onto the rail: the wheel drops onto the flat and slams (a heavy dull clonk: real heavy
    metal and plate hits pitched down for a wheel's weight, choked, since a flat doesn't ring), the axle box knocking in
    its guides a moment after, and the rotten body jolting on its springs (its boards' hollow thud)."""
    clonk = W.rec(METAL_H[take % 5], semis=-rng.uniform(6, 9), hi=3000, tau=0.05)
    plate = W.rec(PLATE_H[(take + 2) % 5], semis=-rng.uniform(3, 5), hi=2200, tau=0.1)
    box = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_M[take % 5]), -7))), 0.01, 0.03)
    return ck.place([(0, unit(clonk), 0), (0, unit(plate), -1), (0, unit(W.knock(rng, 58, 0.3)), -3),
                     (0.006, unit(ck.hollow(rng, [74, 118, 182, 260], 0.35, q=5)), -3),
                     (rng.uniform(0.02, 0.04), unit(box), -9)])


def rattle(rng, keys, count, tau=0.015):
    """A loose hasp or a hanging chain shaken by the jolt: a few iron knocks settling, each sooner and softer."""
    b = Bus(0.6)
    t, g = 0.0, 1.0
    for i in range(count):
        b.at(t, WR.clink(keys[(i + int(rng.integers(len(keys)))) % len(keys)], i, rng.uniform(-6, -3), tau) * g)
        t += rng.uniform(0.04, 0.09) * (1 - 0.1 * i)
        g *= rng.uniform(0.5, 0.75)
    return b.x


def groan(rng, length, body, peak=40.0, q=12):
    """Stick-slip quickening into a groan and slowing out of it, rung through `body` (an axle box, a car's timbers)."""
    r = env([(0, peak * 0.25), (length * 0.45, peak), (length, peak * 0.3)], length)
    c = B.creak(length, r, rng, body=body, q=q, jitter=0.45, grit=0.4)
    return c * env([(0, 0), (length * 0.25, 1), (length * 0.75, 0.8), (length, 0)], length)


def yard(rng, y, wet=0.25):
    """Twenty metres off in a yard at night: the top dulled by the air, the slaps off the cars round it, the night."""
    return circ_outdoors(W.cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 2)), rng, wet)


def derelict_beats(rng):
    """Which wheels are flat and when they come round: two flats on different axles, each once a turn."""
    phases = [rng.uniform(0, 0.15), rng.uniform(0.4, 0.6)]
    return [((r + ph) * TURN + rng.normal(0, 0.006), w) for r in range(int(round(DERELICT / TURN))) for w, ph in enumerate(phases)]


@loop_recipe("place-derelict", "roll", "seized",
             "A seized, rusted car shunted at walking pace: dry axle boxes grinding, flat wheels clonking, the body groaning",
             """Two flat-spotted wheels on different axles, each slamming once a turn (1.2 s): real heavy metal and plate
             hits pitched down for a wheel's weight and choked, the axle box knocking in its guides, the rotten body
             jolting with a hollow thud, a loose hasp or chain rattling after some. The dry journals grind in their boxes
             (modelled: rubbing noise roughened by the rust, rung through an iron casting's modes, swelling once a turn
             where the journal's worst spot drags, groaning into stick-slip there and now and then catching into a dry
             squeal). The body groans as it rocks (stick-slip through big timbers), the wheels rumble faintly on the rail.
             Heard about 20 m off in a yard. One 12 s cycle of ten turns.""",
             sources=METAL_H + PLATE_H + METAL_M + METAL_L, loop=True, takes=1, lufs=-22, seconds=DERELICT)
def derelict_seized(rng, k):
    n = samples(DERELICT)
    t = np.arange(n) / SR
    ev = []
    for i, (tb, w) in enumerate(derelict_beats(rng)):
        ev.append((tb, flat(rng, i), rng.uniform(0.8, 1.0) * (1.0 if w == 0 else 0.8)))
        if rng.random() < 0.35:
            ev.append((tb + 0.03, rattle(rng, METAL_L, int(rng.integers(3, 6))), 0.18))
        if rng.random() < 0.4:
            L = rng.uniform(0.7, 1.3)
            ev.append((tb + 0.05, unit(groan(rng, L, [f * rng.uniform(0.6, 0.75) for f in synth.WOOD], rng.uniform(25, 45), 10)), 0.25))
    grind, sq = np.zeros(n, np.float32), np.zeros(n, np.float32)
    for axle, ph in enumerate((rng.uniform(0, 1), rng.uniform(0, 1))):
        turn_ = (0.5 + 0.5 * np.cos(2 * np.pi * (t / TURN + ph))) ** 3
        drag = W.cfilter(W.friction(n, rng, JOURNAL, rough=1.6, grit=0.3, q=14, loop=True), lambda f: 1 / (1 + (f / 3000) ** 2))
        grind += W.norm(drag) * (0.4 + 0.6 * turn_).astype(np.float32)
        on = np.clip(0.3 + 0.9 * W.slow(n, 0.25, rng), 0, 1) * turn_ ** 1.5
        sq += W.squeal(n, rng, [rng.uniform(560, 680), rng.uniform(1050, 1250)], on, wander=0.012, rate=2.0, harm=0.4)
        for r in range(int(round(DERELICT / TURN))):
            if rng.random() < 0.6:
                ev.append(((r - ph) * TURN % DERELICT - 0.25, unit(groan(rng, 0.5, IRON_BOX, rng.uniform(35, 60))), 0.22))
    rumble = W.cfilter(W.pnoise(n, rng), lambda f: 1 / (1 + (f / 180) ** 2) * (f / 40) / (1 + f / 40))
    y = unit(W.place(n, ev)) + unit(grind) * 0.5 + unit(sq) * 0.12 + unit(rumble) * 0.08
    return seamless(yard(rng, y), n)


SCRAPE = "sfx_100_v2:misc_10"                       # a real metal scrape, a second long
TIMBER = R("doorOpen_1", "doorOpen_2")


def scrape_grain(rng, length, semis):
    """A slice of the real scrape, slowed into a dry iron grind, windowed so slices overlap into a running grind."""
    x = dsp.vari(ck.get(SCRAPE), semis)
    a = int(rng.integers(0, max(1, len(x) - samples(length))))
    return unit(lp(fit(x[a:], samples(length)), 3500) * np.hanning(samples(length)).astype(np.float32))


@loop_recipe("place-derelict", "roll", "rust",
             "The derelict from real recordings: a scrape slowed into the dry axles' grind, door creaks for the body",
             """The same seized car, its moving parts from the packs' real recordings: the dry journals' grind is the
             packs' metal scrape slowed an octave and more, laid in overlapping slices that swell where each journal's
             worst spot comes round once a turn; the dry squeal is a real hinge squeal pitched down into an axle box's
             range; the rotten body's groan is the packs' door creaks dragged down most of an octave and stretched; the
             wheels roll on the real train-running loop slowed right down. The flats are the same real heavy metal and plate
             hits pitched for a wheel's weight, once a turn on two axles. About 20 m off in a yard. One 12 s cycle.""",
             sources=[SCRAPE] + SQUEALS + TIMBER + ["sfx_100_v2:loop_ambient_04"] + METAL_H + PLATE_H + METAL_M + METAL_L,
             loop=True, takes=1, lufs=-22, seconds=DERELICT)
def derelict_rust(rng, k):
    n = samples(DERELICT)
    ev = []
    for i, (tb, w) in enumerate(derelict_beats(rng)):
        ev.append((tb, flat(rng, i + 1), rng.uniform(0.8, 1.0) * (1.0 if w == 0 else 0.8)))
        if rng.random() < 0.35:
            ev.append((tb + 0.03, rattle(rng, METAL_L, int(rng.integers(3, 6))), 0.18))
    grind = []
    for axle, ph in enumerate((rng.uniform(0, 1), rng.uniform(0, 1))):
        for r in range(int(round(DERELICT / TURN)) * 2):
            tg = (r / 2 + ph) * TURN
            grind.append((tg, scrape_grain(rng, 0.9, -rng.uniform(12, 14)), 1.0 if r % 2 == 0 else 0.55))
    for r in range(0, int(round(DERELICT / TURN)), 3):
        L = rng.uniform(1.0, 1.5)
        x = lp(dsp.vari(ck.get(TIMBER[r % 2]), -rng.uniform(8, 11)), 1800)
        x = fit(dsp.stretch(x, L / (len(x) / SR), smooth=True), samples(L)) * env([(0, 0), (L * 0.2, 1), (L * 0.8, 0.8), (L, 0)], L)
        ev.append((r * TURN + rng.uniform(0.05, 0.2), unit(x), 0.25))
    sq_ev = []
    for r in range(int(round(DERELICT / TURN))):
        if rng.random() < 0.35:
            x = dsp.trim_silence(ck.get(SQUEALS[r % 3]), -30)
            x = dsp.vari(x, -rng.uniform(7, 9))
            L = min(len(x) / SR, 0.7)
            sq_ev.append((r * TURN + rng.uniform(0.3, 0.6), unit(fit(x, samples(L)) * env([(0, 0), (0.05, 1), (L, 0)], L)), 1.0))
    roll = W.cfilter(src_loop("sfx_100_v2:loop_ambient_04", n, -9, rng), lambda f: 1 / (1 + (f / 1200) ** 2))
    y = unit(W.place(n, ev)) + unit(W.place(n, grind)) * 0.3 + unit(W.place(n, sq_ev)) * 0.05 + unit(roll) * 0.12
    return seamless(yard(rng, y), n)


# ---- state-starved.labour: an engine short of steam -----------------------------------------------------------------------------

STARVE = 12.0
REV = 2.4            # one turn of the drivers, labouring (a walking pace on 5 ft drivers): four beats a turn, five turns


def weak_beats(rng):
    """Four exhausts a turn, uneven (the cranks aren't quite square, one cylinder weaker), and now and then a beat that
    hardly comes: the steam isn't there to make it."""
    lag = rng.uniform(0.02, 0.05)
    accent = [1.0, rng.uniform(0.55, 0.7), rng.uniform(0.8, 0.95), rng.uniform(0.45, 0.6)]
    out = []
    for r in range(int(round(STARVE / REV))):
        for j, ph in enumerate((0, 0.25 + lag, 0.5, 0.75 + lag)):
            g = accent[j] * rng.uniform(0.75, 1.05) * (0.35 if rng.random() < 0.12 else 1.0)
            out.append(((r + ph) * REV + rng.normal(0, 0.008), g))
    return out


def gasps(rng, n, beats):
    """The exhaust's pressure with too little steam behind it: each beat opens slowly and dies slowly (tens of ms up, a
    fifth of a second down: a sigh up the chimney, not a bark), and after it the smokebox's draw pulls a shallow breath of
    air back in through the firebox (a soft rising draw, cut off by the next beat). Returns (out, in) pressure curves."""
    L = 0.7
    t = np.arange(samples(L)) / SR
    out, draw = [], []
    for tb, g in beats:
        rise, decay = rng.uniform(0.025, 0.045), rng.uniform(0.16, 0.24)
        sh = (1 - np.exp(-t / rise)) * np.exp(-t / decay)
        out.append((tb, (sh / sh.max()).astype(np.float32), g))
        d = REV / 4 * 0.6
        dr = env([(0, 0), (d * 0.8, 1), (d, 0)], d)
        draw.append((tb + REV / 4 * 0.3, dr, g * 0.5))
    return W.place(n, out), W.place(n, draw)


def leaks(rng, n, p):
    """Steam escaping where it shouldn't, at low pressure: the glands and drain cocks hissing thin, harder while a
    cylinder's under steam, and a wheezing whistle of a leak coming and going."""
    held = W.cyclic(lambda z: lp(z, 6), p)
    hiss = W.jet(n / SR, rng, n=n, pressure=np.clip(0.25 + 0.6 * held, 0, None), peak=4200, low=0.0, eddy=1.1)
    whistle = W.howl(n, rng, [rng.uniform(1700, 2000), rng.uniform(2500, 2900)], 1.0, wander=0.04, rate=0.4, q=30)
    return unit(hiss) + unit(whistle) * 0.35 * np.clip(0.3 + held, 0, 1.5)


def couplings(rng, n, count, take):
    """The train dragging on the drawbar: the drawgear's iron groaning under the strain in long surges, the slack
    snatching now and then (a real cast-iron clank), the wooden cars behind creaking."""
    ev = []
    for i in range(count):
        L = rng.uniform(1.4, 2.6)
        ev.append((rng.uniform(0, STARVE), unit(groan(rng, L, IRON_BOX, rng.uniform(14, 30), 14)), 1.0))
        ev.append((rng.uniform(0, STARVE), unit(lp(groan(rng, L, [f * 0.65 for f in synth.WOOD], rng.uniform(10, 25), 10), 2500)), 0.45))
    for i in range(2):
        ev.append((rng.uniform(0, STARVE), unit(iron(rng, take + i, -8, 0.05, 0.3)), 0.9))
    return W.place(n, ev)


@loop_recipe("state-starved", "labour", "gasp",
             "Short of steam: weak, gasping exhaust beats, leaks wheezing, the rods knocking under the load, couplings straining",
             """The struggling part of an engine dragging its train with too little steam, to lie under the chuff. Its
             exhaust is the bed's blast-pipe model with the pressure gone: four beats a turn at a labouring walking pace,
             each opening slowly and dying slowly (a sigh up the chimney, darker and softer than a chuff), uneven, some
             hardly there, and after each the smokebox's draw pulling a shallow breath of air back in. Between them the
             glands and drain cocks hiss thin at low pressure and a leak wheezes. The motion labours: the rods knocking
             in their brasses at each dead centre (real heavy plate and metal hits pitched down), the crossheads rubbing
             on their slides, and behind it the drawgear groaning under the strain in long surges, the slack snatching
             (a real cast-iron clank) and the cars creaking. One 12 s cycle (five turns).""",
             sources=PLATE_H + METAL_M + METAL_H, loop=True, takes=1, lufs=-24, seconds=STARVE)
def starved_gasp(rng, k):
    n = samples(STARVE)
    beats = weak_beats(rng)
    p_out, p_in = gasps(rng, n, beats)
    chuff = W.jet(STARVE, rng, n=n, pressure=p_out * 0.8 + 0.02, peak=rng.uniform(380, 460), low=0.5, eddy=0.9)
    pipe = W.cyclic(lambda z: dsp.resonate(z, CHIMNEY[1:], q=3, gains=[1, 0.7, 0.45]), chuff)
    draw = W.jet(STARVE, rng, n=n, pressure=p_in * 0.5 + 0.01, peak=rng.uniform(1000, 1300), low=0.0, eddy=1.0)
    knocks, slides = [], []
    for r in range(int(round(STARVE / REV))):
        for j, ph in enumerate((0.12, 0.62)):
            x = mix(unit(W.rec(PLATE_H[(r + j) % 5], semis=-rng.uniform(2.5, 4), hi=4000)),
                    unit(W.rec(METAL_M[(r + 2 * j) % 5], semis=-rng.uniform(6, 8), hi=3000, tau=0.08)) * 0.5,
                    unit(W.knock(rng, 72, 0.18)) * 0.7)
            knocks.append(((r + ph) * REV + rng.normal(0, 0.01), x, rng.uniform(0.6, 1.0)))
        for j in range(4):
            Ls = REV / 4 * 0.7
            f = W.friction(samples(Ls), rng, [240, 520, 910, 1500], rough=1.2, grit=0.3, q=12)
            slides.append(((r + j / 4 + 0.05) * REV, unit(f * env([(0, 0), (Ls * 0.5, 1), (Ls, 0)], Ls)), rng.uniform(0.6, 1.0)))
    y = (unit(mix(W.norm(chuff), W.norm(pipe) * 0.5)) + unit(draw) * 0.22 + leaks(rng, n, p_out) * 0.28
         + unit(W.place(n, knocks)) * 0.45 + unit(W.place(n, slides)) * 0.12 + unit(couplings(rng, n, 3, k)) * 0.35)
    return seamless(circ_outdoors(y, rng, 0.12), n)


@loop_recipe("state-starved", "labour", "drag",
             "Short of steam, with no beat of its own: steam sighing in slow surges, wheezing leaks, the motion grinding",
             """The same struggle with nothing on a beat, so it can lie under the chuff at any speed without fighting the
             engine's own rhythm: the weak steam's breath sagging and swelling in slow surges as the engine nearly
             stalls and recovers (the bed's jet model at low pressure, dark and breathy), the glands and drain cocks hissing
             thin and a leak wheezing, the motion grinding under the load (rubbing noise through the rods' and axle boxes'
             iron, heavier in each surge), and the drawgear groaning in long strains, the slack snatching at uneven
             moments (a real cast-iron clank), the cars behind creaking. One 12 s cycle.""",
             sources=PLATE_H + METAL_H, loop=True, takes=1, lufs=-24, seconds=STARVE)
def starved_drag(rng, k):
    n = samples(STARVE)
    surge = np.clip(0.55 + 0.35 * W.slow(n, 0.3, rng) + 0.15 * W.slow(n, 1.2, rng), 0.1, None).astype(np.float32)
    sigh = W.jet(STARVE, rng, n=n, pressure=0.3 * surge, peak=rng.uniform(420, 520), low=0.7, eddy=1.0)
    pipe = W.cyclic(lambda z: dsp.resonate(z, CHIMNEY[1:], q=3, gains=[1, 0.7, 0.45]), sigh)
    grind = W.cfilter(W.friction(n, rng, [130, 290, 520, 860, 1350], rough=1.4, grit=0.25, q=10, loop=True),
                      lambda f: 1 / (1 + (f / 2000) ** 2)) * surge ** 1.5
    y = (unit(mix(W.norm(sigh), W.norm(pipe) * 0.4)) + leaks(rng, n, surge - surge.min()) * 0.3 + unit(grind) * 0.3
         + unit(couplings(rng, n, 4, k + 1)) * 0.45)
    return seamless(circ_outdoors(y, rng, 0.12), n)


# ---- crew-cannon-impact: flesh, a fort's stone, the train's iron ----------------------------------------------------------------
# The same distance, space and finish as the ground, water and doll impacts (warnings.py): out in the open 10-80 m off,
# levelled to -16 LUFS with the first instant held under the limiter, the land's tail over by the take's end.

MEAT = SOFT + R("dropLeather", "bookPlace2")
CRACK = S("misc_35", "misc_33")                    # real wood and bone cracking


def gore(rng, length, rate=24, decay=0.35):
    """What the ball throws out of a body coming down round it: wet splats on the ground (the packs' real wet steps cut
    short, pitched about) and small wet slaps, thick at first and thinning fast."""
    def piece(rng):
        if rng.random() < 0.5:
            x = ck.align(dsp.vari(ck.get(WET[int(rng.integers(3))]), rng.uniform(-3, 2)))
            return unit(ck.choke(x[:samples(0.12)], 0.02, 0.03))
        return unit(slap(rng, rng.uniform(0.03, 0.06), rng.uniform(1200, 2500), 1.0))
    return WR.rain_of(rng, length, rate, piece, decay=decay, start=0.05, bounce=0.1)


@recipe("crew-cannon-impact", "flesh", "meat",
        "A ball landing in a large creature: a deep wet thud into a mass of meat, a bone giving, gore spattering down",
        """No earth in it: the ball sinks into a body. Its weight is real soft heavy impacts and a leather drop pitched
        down five and six semitones for the mass, a book slapped flat pitched down an octave for the hide splitting, over a
        sub thump, driven a little into saturation as the others are; a bone gives under it (the packs' real crack,
        pitched down and kept small); the flesh closes round it wet (a deep squelch); then what it threw out comes down
        round it for half a second, wet splats on the ground (real wet steps cut short) thinning fast. Out in the open,
        heard from the gun.""", sources=MEAT + CRACK + WET, takes=3, lufs=-16)
def flesh_meat(rng, k):
    L = 1.5
    body = mix(unit(W.rec(SOFT[k], semis=-5 + rng.uniform(-1, 1))), unit(W.rec(R("dropLeather")[0], semis=-6 + rng.uniform(-1, 1))) * 0.8,
               unit(W.knock(rng, 48, 0.5, 0.45)) * 0.9)
    body = dsp.saturate(W.norm(ck.tilt(body, lo_db=-4, lo_f=110)), 5)
    hide = W.rec("kenney_rpg-audio:bookPlace2", semis=-12 + rng.uniform(-1, 1), hi=5000)
    crack = hp(W.rec(CRACK[k % 2], semis=-4 + rng.uniform(-1, 1), tau=0.05), 500)
    wet = lp(squelch(rng, 0.4, 110, 650, 1.4), 2500)
    b = Bus(L + 0.5)
    b.at(0, body, 0)
    b.at(0.002, unit(hide), -8)
    b.at(0.01 + rng.uniform(0, 0.02), unit(crack), -16)
    b.at(0.015, unit(wet), -8)
    b.at(0.06, unit(gore(rng, 0.9)), -15)
    return WR.finish(WR.over_by(WR.out_there(b.x, rng, 0.18), L), lo=35)


@recipe("crew-cannon-impact", "flesh", "mass",
        "A ball into a large creature, modelled: a dense low whump, the hide slapping, the body's fluid gulping round it",
        """Built from the flesh models the creatures share, scaled up to something the size of a cow or bigger: a dense
        low whump of noise (never a ringing sine), a slap on wet hide, the meat giving (the crack of bone inside, small),
        the ball's hole gulping and bubbling as fluid closes round it, and the whole body quivering after (a wet squelch
        fluttering a few times a second, dying); a few wet splats coming down round it. Saturated a little for the weight,
        out in the open like the other impacts.""", sources=WET, takes=3, lufs=-16)
def flesh_mass(rng, k):
    L = 1.5
    whump = B.thud(rng, rng.uniform(55, 70), 0.6, 0.3)
    hit = synth.thump(rng.uniform(40, 48), 0.5, drop=0.4)
    blow = flesh_hit(rng, weight=2.0, wet=1.0, bone=0.4, f=55)
    gulp_ = synth.bubbles(0.5, env([(0, 60), (0.5, 5)], 0.5), 70, 320, rng, rise=(0.1, 0.5))
    quiver = B.ripple(rng, rng.uniform(0.5, 0.7))
    b = Bus(L + 0.5)
    b.at(0, dsp.saturate(W.norm(mix(unit(whump), unit(hit) * 0.7, unit(blow) * 0.8)), 5), 0)
    b.at(0.003, unit(slap(rng, 0.15, 1400, 1.0)), -9)
    b.at(0.03, unit(gulp_), -12)
    b.at(0.05, unit(quiver), -11)
    b.at(0.08, unit(gore(rng, 0.8, 18)), -16)
    return WR.finish(WR.over_by(WR.out_there(b.x, rng, 0.18), L), lo=35)


MINING = K("impactMining")


@recipe("crew-cannon-impact", "structure", "masonry",
        "A ball striking a fort's stone wall: a hard crack over the wall's dead thud, the crack slapping back, rubble falling",
        """Iron on stone. The crack is real stone cracking (the packs' stones breaking, pitched down for a block's size)
        with the fracture running through the block (a short bright crack rung through its modes); under it the dead
        weight of the wall taking the blow (Kenney's heavy rock impacts, a pick into rock, pitched down, over a sub
        thump), driven a little into saturation like the other impacts. The fort's other walls slap the crack back a tenth
        of a second later. Then the face it broke comes down: grit and dust hissing down the wall (a pour of small hard
        grains), chips and stones (the packs' real stones cracking) and a few blocks thudding onto the ground (dull
        knocks under real stones' cracks pitched down), thinning out over a second and a half. Out in the open, heard
        from the gun.""",
        sources=WR.STONES + MINING + WR.GRAVEL, takes=3, lufs=-16)
def structure_masonry(rng, k):
    L = 2.2
    crack = hp(W.rec(WR.STONES[k % 3], semis=-5 + rng.uniform(-1, 1), tau=0.08), 300)
    frac = snap(rng, 0.1, rng.uniform(1800, 2600))
    thud = W.rec(MINING[(k * 2) % 5], semis=-2 + rng.uniform(-1, 1), tau=0.2)
    mass = mix(unit(thud), unit(W.knock(rng, 52, 0.45, 0.4)) * 0.7)
    hit = dsp.saturate(W.norm(mix(unit(crack), unit(frac) * 0.7, mass * 0.7)), 5)
    echo = lp(mix(unit(crack), unit(frac) * 0.7), 3000)
    n = samples(L)
    rate = env([(0, 0), (0.05, 1), (0.5, 0.45), (L, 0)], L) * 900
    dust = W.pour(n, rng, rate + 1, grain=(2500, 7500), thunder=0.0, loop=False, voices=12, decay=0.004)
    dust = dust * env([(0, 0), (0.05, 1), (0.6, 0.4), (L, 0)], L)
    stones = WR.pebbles(rng, 1.4, 16, decay=0.5, semis=(-4, 2))
    blocks = Bus(1.6)
    for _ in range(int(rng.integers(3, 6))):
        # a block or a brick landing: a short dull knock of its weight and a big stone's crack (Kenney's rock hits ring
        # on for a second at this pitch: a boom, not a block)
        t = rng.uniform(0.15, 1.1)
        blocks.at(t, unit(W.knock(rng, rng.uniform(80, 130), 0.12, 0.3)) * rng.uniform(0.5, 1.0))
        blocks.at(t, WR.clink(WR.STONES[int(rng.integers(3))], int(rng.integers(4)), rng.uniform(-9, -6), 0.04) * 0.6)
    blocks = blocks.x
    b = Bus(L + 0.5)
    b.at(0, hit, 0)
    b.at(rng.uniform(0.08, 0.12), unit(echo), -13)
    b.at(0.02, unit(dust), -19)
    b.at(0.06, unit(lp(stones, 7000)), -14)
    b.at(0.0, unit(lp(blocks, 5000)), -14)
    return WR.finish(WR.over_by(WR.out_there(b.x, rng, 0.2), L), lo=35)


SLAM = "sfx_100_v2:metal_hit_01"
RIVETS = S("metal_02", "metal_06")


def rivet(rng, f=None):
    """A rivet head ringing in the plate it's set in: a short steel bar's modes, high and quickly gone."""
    return W.body(rng, f or rng.uniform(2200, 4800), W.BAR, decay=rng.uniform(0.12, 0.3), contact=0.0002, count=3, length=0.6)


@recipe("crew-cannon-impact", "train", "iron",
        "A ball striking the train's own iron: a deep clang through a big plate, rivets ringing, scale and a rivet falling",
        """Iron on the train's iron. The blow is the packs' real metal slam and heavy plate and metal hits, pitched down
        for a ball's weight and a car side's mass, over a sub thump, driven a little into saturation; the plate it hit
        clangs on (its few low modes ringing clear for a second and more, near 100 Hz and up, its dense upper modes a
        shimmer dying first) and its rivets ring in it (a scatter of short bright steel rings, high). Then a little debris: rust and scale pattering down off the plate
        and a sheared rivet head bouncing away (real small iron knocks). Out in the open, heard from the gun.""",
        sources=[SLAM] + PLATE_H + METAL_H + RIVETS, takes=2, lufs=-16)
def train_iron(rng, k):
    L = 2.0
    slam = W.rec(SLAM, semis=-4 + rng.uniform(-1, 1), tau=0.25)
    plate = W.rec(PLATE_H[(k * 2 + 1) % 5], semis=-5 + rng.uniform(-1, 1), hi=3500)
    clang = W.rec(METAL_H[(k * 3) % 5], semis=-8 + rng.uniform(-1, 1), tau=0.2)
    blow = dsp.saturate(W.norm(mix(unit(slam), unit(plate) * 0.9, unit(clang) * 0.7, unit(W.knock(rng, 44, 0.4)) * 0.8)), 5)
    # the clang: the struck plate's few low modes ringing clear (the pitch you hear), its dense upper modes a shimmer
    bong = W.body(rng, rng.uniform(85, 110), W.PLATE, decay=rng.uniform(1.0, 1.3), damp=0.6, contact=0.0008, count=9,
                  tilt=0.4, length=L)
    shimmer = W.sheet(rng, f1=rng.uniform(180, 240), fmax=5000, decay=0.7, contact=0.0004)
    rv = Bus(0.8)
    for _ in range(int(rng.integers(5, 9))):
        rv.at(abs(rng.normal(0, 0.03)), rivet(rng) * rng.uniform(0.3, 1.0))
    scale = WR.rain_of(rng, 0.9, 30, lambda r: unit(r.standard_normal(samples(0.004)).astype(np.float32)
                                                      * np.exp(-np.linspace(0, 6, samples(0.004))).astype(np.float32)),
                       decay=0.3, start=0.05, bounce=0.0)
    bounce = Bus(1.0)
    t, g = 0.0, 1.0
    for i in range(int(rng.integers(3, 5))):
        bounce.at(t, WR.clink(RIVETS[i % 2], i, rng.uniform(-2, 2), 0.03) * g)
        t += rng.uniform(0.12, 0.2) * 0.7 ** i
        g *= 0.6
    b = Bus(L + 0.5)
    b.at(0, blow, 0)
    b.at(0, unit(bong), -8)
    b.at(0, unit(shimmer), -16)
    b.at(0.002, unit(rv.x), -15)
    b.at(0.05, unit(hp(scale, 2000)), -24)
    b.at(rng.uniform(0.3, 0.45), unit(bounce.x), -20)
    return WR.finish(WR.over_by(WR.out_there(b.x, rng, 0.18), L), lo=32)
