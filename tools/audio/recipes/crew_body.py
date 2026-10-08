"""The crew's bodies: climbing the iron ladders and landing across a coupling gap (crew-ladder), being hit, grabbed,
burned and falling (crew-hurt), jumping off a moving train (crew-jump-off), and the cold (crew-cold).

The ladders on a car's end are round iron bar rungs riveted to iron stiles that are bolted to the car body: a rung under a
boot or a gloved hand doesn't sing (the director on the last pass: "What ladder sounds like glass bottles clinging
together?" That was Kenney's ringing light-metal hits). It gives a short, dead iron clank, and the car body behind it
thuds. So a rung here is the boot recordings' sole (crew_feet's heels and toes) on a choked heavy iron hit, a glove's
leather slap, and the car's hollow; never the ringing metal. The body sounds are the packs' cloth, leather and soft heavy
hits; the breath in the cold is a stand-in (a throat and mouth modelled, crew_kit has no recorded breath).
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import recipe, R, K, DROP
from recipes.crew_items import hit_of, limbs, FLOOR, FLOOR_HOW, LEATHER, CLOTH, SOFT
from recipes.crew_train import iron
from recipes.crew_feet import heel, toe, land, BOOTS
from recipes.kit import snap

PLATE_H, METAL_H = K("impactPlate_heavy"), K("impactMetal_heavy")


def rung(rng, take, force=1.0):
    """A round iron rung taking a weight: a dead, short iron clank (heavy metal and plate hits choked within a few ms:
    the rung is riveted to the car and can't ring) and the car body's hollow thud behind it."""
    clank = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[take % 5]), -5 + rng.uniform(-0.8, 0.8)))), 0.003, 0.012)
    body = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_H[(take + 1) % 5]), -2))), 0.01, 0.03)
    return ck.norm(mix(body * 0.8, clank * 0.5, ck.hollow(rng, [95, 150, 240], 0.12, q=4) * 0.4 * force))


def glove(rng, take):
    """A leather glove slapping onto and closing round a bar."""
    return hit_of(LEATHER[take % 2], 0, 0.15)


def ladder():
    L = "crew-ladder"

    @recipe(L, "grab", "rungs", "Both hands taking the iron ladder: gloves slapping onto the rungs",
            """Two gloved hands slapping onto round iron rungs a beat apart: the leather's slap and grip (the packs' small
            leather handlings) on a dead iron clank (a heavy metal hit choked at once: the rung is riveted to the car and
            doesn't ring), the car body thudding faintly behind.""",
            sources=LEATHER + METAL_H + PLATE_H, takes=3, lufs=-22)
    def grab(rng, k):
        d = rng.uniform(0.09, 0.16)
        return ck.place([(0, glove(rng, k), 0), (0, rung(rng, k, 0.5), -10),
                         (d, glove(rng, k + 1), -2), (d, rung(rng, k + 2, 0.5), -12)])

    @recipe(L, "rung-up", "rungs", "One rung climbed: a boot set on the iron rung, a hand taking the next",
            """A boot set down on a round iron rung: the boots' own sole (the footsteps' heel and toe recordings, high-passed)
            on a dead iron clank with the car body's thud behind it, then a gloved hand slapping onto the next rung up.
            Six takes from different boot hits and iron.""", sources=BOOTS + LEATHER + METAL_H + PLATE_H, takes=6, lufs=-21,
            preview=lambda t, r: ck.gait(t, r, 0.6, 12))
    def up(rng, k):
        sole = hp(toe(rng, k), 900)
        return ck.place([(0, sole, -6), (0, rung(rng, k, 1.0), 0), (rng.uniform(0.18, 0.26), glove(rng, k), -6),
                         (rng.uniform(0.18, 0.26), rung(rng, k + 3, 0.3), -16)])

    @recipe(L, "rung-down", "rungs", "One rung down: a hand slid down, the boot dropped onto the rung below",
            """Climbing down: a gloved hand let go and taking the rung below first (a leather slap on dead iron), then the
            boot dropped onto its rung, heavier and toe-first (the boot's sole on the iron clank, the car body's thud).""",
            sources=BOOTS + LEATHER + METAL_H + PLATE_H, takes=6, lufs=-21,
            preview=lambda t, r: ck.gait(t, r, 0.6, 12))
    def down(rng, k):
        sole = hp(heel(rng, k), 900)
        return ck.place([(0, glove(rng, k + 1), -6), (0, rung(rng, k + 2, 0.3), -16),
                         (rng.uniform(0.14, 0.2), sole, -5), (rng.uniform(0.14, 0.2), rung(rng, k, 1.2), 0)])

    @recipe(L, "let-go", "rungs", "Letting go of the ladder at the top or bottom",
            """The gloves leaving the iron (a short leather scuff off the rungs and one last small clank as a rung is
            pushed off), and a coat sleeve swinging free.""", sources=LEATHER + METAL_H + CLOTH, takes=3, lufs=-24)
    def let_go(rng, k):
        rub = ck.friction(rng, 0.1, 150, 400, 3000, 0.7, env([(0, 0), (0.02, 1), (0.1, 0)], 0.1))
        return ck.place([(0, rub, -8), (0.04, rung(rng, k + 1, 0.4), -10), (0.06, ck.cloth(rng, k, 0.25), -6)])

    @recipe(L, "gap-land", "plate", "Landing across a coupling gap onto the coupler plate",
            """The footsteps' landing on a steel grate (the same family: both boots down hard, the plate's clatter, the
            knees taking it), with the coupler's iron under it clanking once and the slack in the couplings knocking.""",
            sources=BOOTS + K("impactPlate_light") + K("impactMetal_light") + PLATE_H + METAL_H, takes=3, lufs=-16)
    def gap(rng, k):
        return ck.place([(0, land(rng, k, "grate"), 0), (0.005, iron(rng, k, -6, 0.04, 0.3), -8)])


def hurt():
    L = "crew-hurt"

    @recipe(L, "hit", "body", "Struck: a blow landing on the body through a heavy coat",
            """A blow landing on you: the coat's heavy cloth slapped (the packs' leather drop and cloth), the dull thud of
            it in the chest (a soft heavy hit, the low end of it as you'd feel it), and the coat's cloth jerking with it.
            No punch: a body under wool.""", sources=R("dropLeather") + SOFT + CLOTH, takes=4, lufs=-18)
    def hit(rng, k):
        return ck.place([(0, hit_of(R("dropLeather")[0], 0, 0.3), -2), (0, hit_of(SOFT[k], 0, 0.3), -2),
                         (0.01, ck.cloth(rng, k, 0.25), -10), (0.004, ck.pad(rng, 0.12, 180), -6)])

    @recipe(L, "grabbed", "coat", "Seized: the coat grabbed in a fist and yanked",
            """The coat grabbed and yanked: the cloth bunching hard in a grip (two of the packs' cloth handlings, the sharp
            starts of them), a strap or belt creaking under the pull, a button's tick.""",
            sources=CLOTH + R("clothBelt", "beltHandle2"), takes=3, lufs=-20)
    def grabbed(rng, k):
        strap = synth.creak(0.2, rng.uniform(40, 70), rng, body=[300, 700, 1300], q=6) * env([(0, 0), (0.04, 1), (0.2, 0)], 0.2)
        return ck.place([(0, ck.cloth(rng, k, 0.25), 0), (0.08, ck.cloth(rng, k + 2, 0.3), -2), (0.1, strap, -14),
                         (0.12, hit_of(R("clothBelt", "beltHandle2")[k % 2], 0, 0.1), -18)])

    @recipe(L, "burned", "sear", "Burned: a quick sear",
            """A flare of flame catching the coat (a short rush of air and fire) and a quick sear: fat-in-a-pan sizzle
            (dense, sharp crackle, not a fizz) that dies away in under half a second.""", takes=2, lufs=-20)
    def burned(rng, k):
        L_ = 0.5
        flare = bp(synth.noise(L_, rng, "pink"), 300, 4000) * env([(0, 0), (0.04, 1), (L_, 0)], L_, "exp")
        sizzle = synth.crackle(L_, 900, rng, size=(0.0002, 0.0012), hi=2500) * env([(0, 0), (0.03, 1), (L_, 0)], L_)
        return ck.place([(0, ck.norm(flare), -6), (0.02, ck.norm(sizzle), 0)])

    for mat in DROP:
        @recipe(L, "body-fall", "collapse", f"The body falling on the {FLOOR[mat]}",
                f"""Collapsing: the knees hitting first, then the torso's dull weight (a soft heavy hit with the packs'
                leather drop), an arm and the head after it, the coat settling. The floor answers dully: {FLOOR_HOW[mat]}.""",
                sources=SOFT + R("dropLeather") + CLOTH + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-18)
        def fall(rng, k, m=mat):
            knees = [(0, hit_of(SOFT[(k + 2) % 5], 0, 0.2), -6), (0, ck.floor(rng, m, k + 3, 1.0, 0.3), -8),
                     (0.05, hit_of(SOFT[(k + 3) % 5], 0, 0.2), -8), (0.05, ck.floor(rng, m, k + 4, 0.8, 0.3), -10)]
            return ck.place(knees + [(rng.uniform(0.25, 0.32), limbs(rng, k, m, 1.3), 0)])


def jump_off():
    L = "crew-jump-off"

    @recipe(L, "rush", "air", "Air rushing past at speed (off a moving train), held",
            """Twenty metres a second of air past the ears: a broad roar (pink and brown noise) buffeting at a few times a
            second, the turbulence round the head rumbling low, and a thin edge of whistle that wavers. Wind, from the
            physics; one 8 s cycle.""", loop=True, takes=1, lufs=-20)
    def rush(rng, k):
        L_ = 8.3
        n = samples(L_)
        roar = lp(bp(synth.noise(L_, rng, "pink"), 80, 5000), 1800, 1)
        buf = np.clip(1 + 0.6 * lp(rng.standard_normal(n).astype(np.float32), 4) * 16, 0.15, 2.0)
        low = lp(synth.noise(L_, rng, "brown"), 160) * np.clip(1 + 0.8 * lp(rng.standard_normal(n).astype(np.float32), 9) * 16, 0.1, 2.4)
        fc = 1800 * np.exp(lp(rng.standard_normal(n).astype(np.float32), 0.7) * 8)
        whistle = dsp.sweep_filter(synth.noise(L_, rng), "bp", np.clip(fc, 900, 4000), q=6)
        return ck.norm(mix(ck.norm(roar) * buf, ck.norm(low) * 1.0, ck.norm(whistle) * 0.05))

    for mat in ("ground", "grate"):
        @recipe(L, "impact", "body", f"Hitting the {'ballast' if mat == 'ground' else 'steel grating'} at speed",
                {"ground": """The fatal impact on the ballast: the body slammed down (a soft heavy hit and the packs' leather
                    drop, driven very hard), something breaking inside it (a dull crack), and a spray of stones thrown out
                    and rattling down after (stone grains and the packs' real gravel and stones over half a second).""",
                 "grate": """The fatal impact on steel grating: the body slammed down on it (soft heavy hits and leather),
                    the grating crashing and buzzing under the weight (thin and heavy plate, bars chattering), and a dull
                    crack inside."""}[mat],
                sources=SOFT + R("dropLeather") + ck.FLOOR_SOURCES[mat] + PLATE_H, takes=3, mat=mat, lufs=-14)
        def impact(rng, k, m=mat):
            body = mix(hit_of(SOFT[k], 0, 0.4), hit_of(R("dropLeather")[0], 0, 0.35) * 0.7, ck.pad(rng, 0.2, 200) * 0.8)
            parts = [(0, body, 0), (0.008, lp(snap(rng, 0.08, rng.uniform(700, 1100)), 3000), -12),
                     (0, ck.floor(rng, m, k, 4.0, 0.5), 0)]
            if m == "ground":
                spray = ck.grains(rng, 70, 0.6, 700, 5000, q=(3, 9), shape=0.6)
                parts += [(0.02, spray, -6), (0.05, ck.floor(rng, "ground", k + 1, 2.0, 0.6), -6)]
            else:
                # Queue #240 (note 503): the steel was choked inside 0.05 s under the body's thump and it read as earth
                # (its takes centred at 214-263 Hz, the ballast's 254). The grating crashes and buzzes under the weight
                # for a good part of a second, its bars chattering, the thin plate ringing on as it settles.
                crash = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_H[k]), -3))), 0.18, 0.14)
                thin = ck.choke(ck.norm(hp(ck.align(dsp.vari(ck.get(ck.THIN_PLATE[(k + 2) % 5]), -2)), 300)), 0.25, 0.18)
                buzz = ck.norm(ck.grains(rng, 70, 0.22, 1200, 5500, q=(14, 30), length=(0.004, 0.012), shape=0.35))
                parts = [(t, x, g - 7 if x is body else g) for t, x, g in parts]
                parts += [(0, crash, -3), (0.004, thin, 0), (0.008, buzz, -6), (0.05, ck.floor(rng, "grate", k + 1, 2.0, 0.9), -5),
                          (0.3, ck.floor(rng, "grate", k + 2, 0.6, 0.8), -16)]
            return ck.place(parts)

    @recipe(L, "tumble", "ballast", "Rolling over and over on the ballast after the fall, held",
            """Tumbling along the track bed: the body thudding down every third of a second or so (soft heavy hits, an
            arm, a shoulder), cloth dragging, and stones spraying and grinding under it the whole time. One 8 s cycle.""",
            sources=SOFT + CLOTH + ck.FLOOR_SOURCES["ground"], loop=True, takes=1, mat="ground", lufs=-18)
    def tumble(rng, k):
        L_ = 8.3
        parts, t = [], 0.0
        while t < L_:
            parts += [(t, hit_of(SOFT[int(rng.integers(5))], 0, 0.3), rng.uniform(-6, 0)),
                      (t, ck.floor(rng, "ground", int(t * 10), rng.uniform(1.5, 3.0), 0.4), -4),
                      (t + 0.02, ck.cloth(rng, int(t * 7), 0.3), -10)]
            t += rng.uniform(0.25, 0.5)
        stones = ck.grains(rng, int(120 * L_), L_, 700, 5000, q=(3, 9), shape=0.0)
        return dsp.wrap(mix(ck.place(parts, L_ + 0.6), ck.place([(0, stones, -12)], L_ + 0.6)), samples(L_))


def breath(rng, length, inhale, shudder=0.0, tract="man"):
    """A breath (stand-in): noise through a modelled mouth and throat, shaped as an inhale (rising, through the teeth)
    or an exhale (falling, open), with a shudder in it when it's cold."""
    n = samples(length)
    y = synth.breath(length, [(0, "h"), (length * 0.5, "u" if inhale else "o"), (length, "h")], tract, rng)
    if inhale:
        y = mix(y, hp(synth.noise(length, rng), 3500) * 0.25)
        shape = env([(0, 0), (length * 0.7, 1), (length, 0)], length)
    else:
        shape = env([(0, 0), (0.06, 1), (length * 0.6, 0.6), (length, 0)], length)
    if shudder:
        sh = 1 - shudder * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * np.cumsum(np.full(n, rng.uniform(7, 10))) / dsp.SR)))
        shape = shape * lp(sh.astype(np.float32), 60)
    return ck.norm(y * shape)


def cold():
    L = "crew-cold"

    @recipe(L, "breath-in", "breath", "Stand-in: a sharp cold breath in through the teeth, catching",
            """Stand-in until a recorded breath: air drawn in sharply through teeth and a tight throat (noise through a
            modelled mouth and throat, a hiss of teeth over it), rising, catching two or three times as the cold makes
            the chest jump.""", takes=4, lufs=-24)
    def b_in(rng, k):
        return breath(rng, rng.uniform(0.45, 0.65), True, 0.35 + 0.1 * (k % 2))

    @recipe(L, "breath-out", "breath", "Stand-in: a breath out, long and shaking",
            """Stand-in: a long breath out through an open mouth (noise through a modelled throat on 'oh', falling away),
            shaking with the cold.""", takes=4, lufs=-25)
    def b_out(rng, k):
        return breath(rng, rng.uniform(0.8, 1.1), False, 0.3 + 0.1 * (k % 2))

    @recipe(L, "shiver", "shiver", "Stand-in: shivering, held: teeth chattering, the breath shaking, the coat trembling",
            """Stand-in: teeth chattering in bursts (tiny hard clicks, a dozen a second), short shaky breaths in and out
            (the modelled breath), and the coat's cloth trembling with the body. One 8 s cycle.""",
            sources=CLOTH, loop=True, takes=1, lufs=-26)
    def shiver(rng, k):
        L_ = 8.3
        parts, t = [], 0.0
        while t < L_:
            for j in range(int(rng.integers(5, 12))):
                parts.append((t + j * rng.uniform(0.06, 0.09), ck.tick(rng, rng.uniform(2200, 3500), q=6, length=0.008),
                              rng.uniform(-14, -8)))
            t += rng.uniform(0.9, 1.6)
        t = 0.2
        while t < L_:
            parts.append((t, breath(rng, 0.5, True, 0.4), -10))
            parts.append((t + 0.7, breath(rng, 0.8, False, 0.4), -10))
            t += rng.uniform(1.8, 2.4)
        tremble = mix(*[ck.place([(t0, ck.cloth(rng, i, 0.4), 0)], L_ + 1) for i, t0 in enumerate(np.arange(0, L_, 0.9))])
        tremble = dsp.tremolo(tremble, 9, 0.6, rng, 0.2)
        return dsp.wrap(mix(ck.place(parts, L_ + 1), tremble * 0.15), samples(L_))


ladder()
hurt()
jump_off()
cold()
