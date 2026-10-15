"""Bringing them home and the big finds (crew-rescue, queue #512, note 723; loot-jackpot and loot-fragile, queue #513, note
719): a friend hauled into a moving car, carried on the shoulder, set down and thrown; the two-person jackpots (a safe, a
church bell, a ship's figurehead) lifted and set down; the fragile finds breaking a little more each time they're knocked.

Gameplay foley, so each is the real thing, trimmed and shaped: what it's made of and how heavy, in the space it's in.
- The crew's voices are the crew's own effort sounds (heard_foley.breath, crew_body.breath: noise through a modelled
  throat and mouth), voiced here because these are grunts: a pressed glottis (a tight, effortful voice, its pitch pushed
  up by the strain, rough with it) through the same modelled tract. The crew are men and women, so each take is a
  different crewmate: a man, a woman, a bigger man. The packs have no recorded grunt, so the voices are modelled in both
  candidates; the kitbashed one breathes through a real breath (the packs' gasp and sigh drawn out), the modelled one
  through the crew's modelled breath.
- Boots, boards, cloth and leather are the packs' real recordings in the kitbashed candidates (the crew's own boots, cut
  to heel and toe in crew_feet, on the car's boards), and small physical models in the modelled ones (a heel's blow rung
  through a plank's bending modes over the car's hollow, wool's stick-slip and crackle).
- The finds are what they're made of: a safe of thick cast iron that barely rings, its door loose in its frame; a
  bronze bell whose partials (hum, prime, tierce, quint, nominal) are cut short by the floor it lies on; oak; bottles in
  straw; lead crystal; a clock's wooden case, its glass, its brass works and its coiled gong.
Every impact, thud and set-down keeps its weight in the low mids: high-passed at 130-150 Hz (the director's squish rule:
under 45% of its energy below 150 Hz), so it lands heavy without a boom. The voices are laid over it whole, since a
grunt's pitch lives down there.
"""

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import SR, samples, lp, hp, env, mix
from recipes import crew_kit as ck
from recipes.beasts import main_hit, unit
from recipes.crew_feet import heel, toe, on_wood, land, scrape, BOOTS
from recipes.crew_items import hit_of, CLOTH, SOFT
from recipes.crew_kit import recipe, R, S, K
from recipes.heard_foley import breath as crew_breath
from recipes.house_folk import drawn
from recipes.k1_asks import lay
from recipes.kit import snap
from recipes.mishaps_body import flap
from recipes.moose_tells import burst

# ---- Sources ------------------------------------------------------------------------------------------------------------

GASP = "sfx_100_v2:misc_05"                      # a real gasp: a short burst of air through an open mouth
SIGH = "sfx_100_v2:misc_25"                      # a real sigh and groan: air through a throat, its formants in it
GRIPS = R("handleSmallLeather", "handleSmallLeather2")   # a real glove's leather taking hold
DROP = R("dropLeather")[0]                       # a real heavy leather drop: a body's weight on a coat
SWISH = R("cloth3")[0]                           # the packs' cloth handling that's a swish: a coat swung
CRACKLE = R("clothBelt", "clothBelt2")           # a real belt's cloth crackling: fibres, straw
CREAKS = R("creak1", "creak2", "creak3")         # real wooden creaks: boards and joints under weight
WOOD_H = K("impactWood_heavy")                   # real heavy wood hits: dull, big, all their weight under 150 Hz
PLANK = K("impactPlank_medium")                  # a plank's hollow knock
WOOD_M = K("impactWood_medium")
WOOD_L = K("impactWood_light")
KNOCKS = S("wood_hit_01", "wood_hit_02", "misc_08")      # real wood knocks: a car's door sill, a crate's side
THUNK = "sfx_100_v2:door_03"                     # a real heavy door's thunk: oak meeting oak
METAL_H, METAL_M = K("impactMetal_heavy"), K("impactMetal_medium")
PLATE_H = K("impactPlate_heavy")
BLOW = "sfx_100_v2:metal_hit_01"                 # a real blow on a steel sheet
LATCHES = S("door_02", "door_04", "door_05")     # real latches dropping: iron into iron
RATTLE = "sfx_100_v2:metal_03"                   # a real loose-metal rattle
BELLS = K("impactBell_heavy")                    # real bells struck (small, bronze): their strike and ring
BREAKS = S("misc_26", "misc_27", "glass_03")     # real glass breaking
CRACKS = S("items_02", "misc_33")                # real cracks and small breaks: glass and china giving
TINKS = S("glass_04", "glass_06") + K("impactGlass_light")[:1]   # real small glass knocked
GLASS_H = K("impactGlass_heavy")                 # real thick glass knocked
CRYSTAL = S("glass_01", "glass_05", "glass_02")  # real glasses ringing: almost pure, with doublets
WORKS = S("misc_20", "metal_04", "lock_open_01") # real mechanisms: a ratchet and pawl, a small mechanism, gears
JANGLE = S("misc_09")                            # real keys jangling: loose small metal

SQUISH = 140.0              # the director's line: an impact keeps its weight above it


def squish(x, f=SQUISH):
    """The squish rule: the boom under it taken out, steep, so the weight lands in the low mids."""
    return hp(x, f, 4)


def at(rng, x, semis=0.0, spread=0.4):
    """A source from its first hit, varispeeded `semis` (and a little either way), at full scale."""
    return ck.norm(ck.align(dsp.vari(x, semis + rng.uniform(-spread, spread))))


# ---- The crew's effort ----------------------------------------------------------------------------------------------------

# Each take is a different crewmate: tract length (as synth.TRACT) and the voice's resting pitch (Hz).
MAN, WOMAN, BIG = (0.72, 114.0), (0.86, 206.0), (0.66, 96.0)
VOICES = (MAN, WOMAN, BIG)


def effort(rng, voice, length, pitch, vowels, shape, press=0.5, rough=0.2, fry=0.0, voiced=0.65, real=None):
    """A crewmate's voice under effort: the crew's breath (heard_foley.breath, the modelled throat and mouth every crew
    breath uses) with a pressed glottis voiced through the same tract. `pitch` [(t, ratio)] on the voice's resting
    pitch (strain pushes it up), `press` the glottis's open quotient (low: a tight, effortful voice), `rough` and `fry`
    its rattle. `real` a recording whose air is drawn out under it instead of the modelled air (the packs' gasp or
    sigh)."""
    k, f0 = voice
    f = env([(t, f0 * r) for t, r in pitch], length, "exp")
    g = synth.glottis(f, length, rng, jitter=0.014, shimmer=0.16, oq=press, sub=fry, rough=rough)
    # synth.tract's formants are a parallel bank, which passes nothing under the first; a real tract passes the chest's
    # low harmonics, so a little of the glottis itself goes in under them
    v = unit(mix(unit(synth.tract(g, vowels, k, breath=0.35, rng=rng)), unit(lp(g, 420, 2)) * 0.3))
    air = crew_breath(rng, length, vowels, shape, tract=k, flutter=0.25)
    if real:
        # a real breath's grain, drawn to the length and coloured by the same mouth
        r = drawn(rng, real, length, rng.uniform(-3, 0) if k < 0.8 else rng.uniform(0, 3))
        air = mix(air * 0.4, unit(synth.tract(r, vowels, k)) * 0.35, unit(r) * 0.25)
    y = mix(v * voiced, unit(air) * (1 - voiced)) * dsp.fit(env(shape, length), samples(length))
    # a chest voice in a heavy coat: nothing buzzing up top, no rumble under the throat
    return unit(hp(lp(y, 5200, 2), 90, 2))


def grunt(rng, voice, real=None):
    """A short grunt of effort as the arms take a weight ('hngh'): a hard glottal start, mouth barely open, the pitch
    pushed up and falling as it goes, a little fry at its tail."""
    L = rng.uniform(0.2, 0.27)
    return effort(rng, voice, L, [(0, 1.32), (0.03, 1.22), (L * 0.6, 1.08), (L, 0.86)],
                  [(0, "m"), (0.035, "o"), (L * 0.7, "a"), (L, "h")],
                  [(0, 0), (0.008, 1), (L * 0.45, 0.8), (L * 0.8, 0.35), (L, 0)],
                  press=0.42, rough=0.3, fry=0.25, voiced=0.62, real=real)


def strain(rng, voice, real=None, length=None):
    """A strained grunt under a weight ('nnngh-hah'): a pressed, closed-mouth hold rising in pitch as the weight comes up,
    rough with the strain, then let go on an open breath as it settles."""
    L = length or rng.uniform(0.5, 0.6)
    rel = L * rng.uniform(0.62, 0.7)
    return effort(rng, voice, L, [(0, 1.1), (rel, 1.38), (rel + 0.05, 1.18), (L, 0.84)],
                  [(0, "m"), (rel * 0.5, "u"), (rel, "u"), (rel + 0.04, "a"), (L, "h")],
                  [(0, 0), (0.03, 0.75), (rel, 1.0), (rel + 0.06, 0.85), (L, 0)],
                  press=0.38, rough=0.45, fry=0.15, voiced=0.6, real=real)


def heave(rng, voice, real=None, big=1.0):
    """A big heave ('hup!'): breath shoved out through a pressed throat on an open vowel, high with the effort, cut off
    hard at its end by the glottis closing."""
    L = rng.uniform(0.22, 0.28) * (0.9 + 0.1 * big)
    return effort(rng, voice, L, [(0, 1.25 + 0.15 * big), (0.04, 1.4 + 0.12 * big), (L * 0.7, 1.15), (L, 0.95)],
                  [(0, "h"), (0.03, "a"), (L * 0.6, "a"), (L, "o")],
                  [(0, 0), (0.012, 0.7), (0.035, 1), (L * 0.65, 0.75), (L * 0.85, 0.2), (L, 0)],
                  press=0.45, rough=0.25, fry=0.1, voiced=0.7, real=real)


def let_out(rng, voice, real=None):
    """A breath let out as the weight comes off: an open 'hhah' with a little voice in its start, falling away."""
    L = rng.uniform(0.38, 0.48)
    return effort(rng, voice, L, [(0, 1.05), (L, 0.82)], [(0, "h"), (0.05, "a"), (L * 0.6, "o"), (L, "u")],
                  [(0, 0), (0.04, 1), (L * 0.4, 0.55), (L, 0)], press=0.6, rough=0.1, voiced=0.22, real=real)


def nose_in(rng, voice, length=0.12):
    """A quick breath in through the nose before the effort."""
    return unit(hp(crew_breath(rng, length, [(0, "m"), (length, "m")], [(0, 0), (length * 0.7, 1), (length, 0)],
                               tract=voice[0], flutter=0.2), 600))


# ---- Boots, boards and coats, modelled ------------------------------------------------------------------------------------

def plank(rng, force=1.0, toe_=False, f=1.0):
    """A boot coming down on a car's deal boards, modelled: a leather heel's short dull blow (a few ms of noise, dark) rung
    through a plank's bending modes (synth.WOOD), the sole's click on it and the car floor's hollow under it; a toe is
    lighter and quicker."""
    x = burst(rng, 0.0025 if toe_ else 0.004, hi=3500 if toe_ else 2200, attack=0.15)
    modes = [m * f * rng.uniform(0.96, 1.04) for m in synth.WOOD]
    b = dsp.resonate(np.concatenate([x, np.zeros(samples(0.12), np.float32)]), modes, q=14,
                     gains=[1, 0.85, 0.6, 0.45, 0.3, 0.18])
    click = hp(burst(rng, 0.0015, attack=0.05), 2500)
    y = mix(unit(b), unit(click) * (0.35 if toe_ else 0.22), ck.hollow(rng, [105, 170, 260], 0.12, q=4) * 0.35)
    return unit(y) * force


def wool(rng, length, density=1.0, shape=None):
    """Heavy wool coats rubbing and bunching, modelled: cloth on cloth stick-slipping (crew_kit.friction in the wool's
    band, rough and slow), the weave's fine dry crackle over it, swelling with the movement."""
    sh = shape if shape is not None else env([(0, 0), (length * 0.25, 1), (length * 0.7, 0.6), (length, 0)], length)
    fr = ck.friction(rng, length, 85 * density, 280, 2600, 0.95, sh)
    rs = synth.rustle(length, 500 * density, rng, f=(1500, 7000), shape=sh, ticks=0.0)
    return unit(mix(fr, unit(rs) * 0.3))


def snatch(rng, length=0.06):
    """A coat's cloth snatched taut in a fist, modelled: a short dull crack of cloth."""
    return unit(burst(rng, length, 250, 2800, attack=0.08))


# ---- crew-rescue ----------------------------------------------------------------------------------------------------------

RL = "crew-rescue"


def car(x, wet=0.12, seed=11):
    """Inside a car with its door open: the car's small wooden room, kept dry."""
    return dsp.room(x, "car", wet=wet, rng=np.random.default_rng(seed))


def scene(parts, voices=(), room=None, f=SQUISH):
    """A take: everything struck, dropped or rubbed with the squish under `f` taken out, the crew's voices laid over it
    whole (a grunt's pitch lives down there), all of it in `room` if there is one."""
    y = squish(ck.place(parts), f)
    if voices:
        y = mix(y, ck.place(list(voices)))
    return room(y) if room else y


@recipe(RL, "haul", "boards",
        "A friend hauled in through the door: a glove grabbing the coat, the hauler's grunt, boots landing hard on the boards",
        """The crewmate in the door grabs a fistful of coat (a real glove's leather taking hold, a real cloth handling
        snatched), grunts with the pull (the crew's modelled throat, pressed and pushed up in pitch, breathing through a
        real gasp), the friend's coat drags over the sill (real cloth, a short scrape on the wood) and a boot knocks it,
        then both boots land hard on the boards together (the crew's own boots, the footsteps' landing on a car's floor)
        and settle. Each take a different crewmate grunting: a man, a woman, a bigger man.""",
        sources=GRIPS + CLOTH + [GASP] + BOOTS + KNOCKS, takes=3, lufs=-18)
def haul_boards(rng, k):
    g = rng.uniform(0.0, 0.03)
    t = rng.uniform(0.34, 0.42)
    sill = squish(at(rng, ck.get(KNOCKS[k % 3]), -2), 160)
    parts = [(0, hit_of(GRIPS[k % 2], 0, 0.12), -10), (0.005, ck.cloth(rng, k, 0.12), -6),
             (0.12, ck.cloth(rng, k + 1, 0.22), -9), (0.14, scrape(rng, "wood", 0.14, 0.8), -16), (t - 0.07, sill, -12)]
    voice = [(0.03 + g, grunt(rng, VOICES[k], GASP), -4)]
    # the footsteps' landing brings the car's room with it
    return mix(scene(parts, voice, car), squish(ck.place([(t, land(rng, k, "wood"), 0)])))


@recipe(RL, "haul", "stumble",
        "Hauled in, modelled: the coat snatched, a grunt, a boot catching the sill and the friend stumbling in onto the boards",
        """Modelled from what happens: the coat snatched taut in a fist (a short dull crack of cloth), the hauler's grunt
        (the crew's modelled throat, pressed, on its own modelled breath), the friend's coats dragging (wool's stick-slip
        and crackle), one boot catching the sill and the other coming down flat, then a stumbling step and a scuff as
        they catch themselves. The boots are a heel's dull blow rung through a plank's bending modes over the car's
        hollow. Each take a different crewmate grunting.""",
        takes=3, lufs=-18)
def haul_stumble(rng, k):
    t = rng.uniform(0.26, 0.32)
    d = rng.uniform(0.15, 0.2)
    s = t + d + rng.uniform(0.15, 0.22)
    parts = [(0, snatch(rng), -6), (0.08, wool(rng, 0.32, 1.2), -10), (t, plank(rng, 0.7, True, 1.15), -6),
             (t + d, plank(rng, 1.0), 0), (t + d + 0.012, plank(rng, 0.6, True), -6),
             (s, plank(rng, 0.7, True, 0.95), -5),
             (s + 0.01, ck.friction(rng, 0.14, 150, 300, 3500, 0.6, env([(0, 0), (0.02, 1), (0.14, 0)], 0.14)), -14)]
    return scene(parts, [(0.02, grunt(rng, VOICES[k]), -4)], car)


@recipe(RL, "lift", "coats",
        "A friend heaved onto the shoulder: a breath in, a strained grunt, two heavy coats rustling and bunching, the weight settling",
        """A quick breath in through the nose, then the strained grunt of taking a grown body's weight (the crew's modelled
        throat, pressed and closed, rising as the weight comes up and let go on an open breath, through a real gasp's
        air), while two heavy coats rustle and bunch against each other (three of the packs' real cloth handlings
        overlapping, a belt's real crackle for the wool's fibres) and the body's weight settles across the shoulder (a
        real leather drop and a soft heavy hit, the boom taken out). Each take a different crewmate.""",
        sources=CLOTH + CRACKLE + [DROP, GASP] + SOFT[:3], takes=3, lufs=-19)
def lift_coats(rng, k):
    v = VOICES[(k + 1) % 3]
    w = rng.uniform(0.44, 0.52)
    crack = hit_of(CRACKLE[k % 2], int(rng.integers(3)), 0.3)
    parts = [(0.1, ck.cloth(rng, k, 0.22), -5), (0.16, ck.cloth(rng, k + 1, 0.35), -8), (0.3, ck.cloth(rng, k + 2, 0.3), -10),
             (0.22, hp(crack, 1200), -18),
             (w, squish(hit_of(DROP, 0, 0.25), 160), -7), (w, squish(hit_of(SOFT[k], 0, 0.25), 160), -11)]
    return scene(parts, [(0, nose_in(rng, v), -16), (0.12, strain(rng, v, GASP), -3)])


@recipe(RL, "lift", "wool",
        "Heaved onto the shoulder, modelled: a sharp 'hup' and a strained hold, wool bunching, the body's weight landing on the shoulder",
        """Modelled from the physics: a sharp short heave as the body leaves the floor and a strained, closed-mouth hold
        as it comes up onto the shoulder (the crew's modelled throat, pressed, on its own modelled breath), the coats
        bunching (wool's stick-slip and crackle swelling with the lift, then a second shove of it as the body's settled),
        and the weight landing across the shoulder (a dull blow through a torso's soft low modes). Each take a different
        crewmate.""",
        takes=3, lufs=-19)
def lift_wool(rng, k):
    v = VOICES[(k + 1) % 3]
    w = rng.uniform(0.42, 0.5)
    body = burst(rng, 0.012, hi=900, attack=0.2)
    torso = dsp.resonate(np.concatenate([body, np.zeros(samples(0.15), np.float32)]),
                         [rng.uniform(170, 200), rng.uniform(300, 340), rng.uniform(480, 540)], q=3)
    parts = [(0, snatch(rng, 0.05), -10), (0.05, wool(rng, 0.42, 1.3), -8), (w, unit(torso), -6),
             (w + 0.02, wool(rng, 0.25, 0.8), -13)]
    return scene(parts, [(0.02, heave(rng, v, big=0.4), -5), (0.24, strain(rng, v), -6)])


@recipe(RL, "set", "boards",
        "A friend put down off the shoulder: coats sliding, their boots coming down on the boards, a breath let out",
        """The coats sliding off the shoulder (real cloth), the friend's boots coming down on the car's boards one and then
        the other (the crew's own boots, heel and toe, on the boards with the floor's hollow), their weight sagging onto
        them (a soft heavy hit, the boom taken out), and the carrier's breath let out (the crew's modelled breath with a
        little voice in it, through a real sigh). Each take a different crewmate.""",
        sources=CLOTH[:3] + BOOTS + SOFT[2:] + [SIGH], takes=3, lufs=-20)
def set_boards(rng, k):
    v = VOICES[(k + 2) % 3]
    b = rng.uniform(0.12, 0.16)
    d = rng.uniform(0.04, 0.08)
    feet = [(b, on_wood(rng, heel(rng, k), 1.1, "heel"), 0), (b + 0.03, on_wood(rng, toe(rng, k), 0.8, "toe"), -3),
            (b + d, on_wood(rng, heel(rng, k + 2), 0.9, "heel"), -2), (b + d + 0.025, on_wood(rng, toe(rng, k + 1), 0.7, "toe"), -5)]
    parts = [(0, ck.cloth(rng, k, 0.25), -6)] + feet + [(b + d + 0.01, squish(hit_of(SOFT[(k + 2) % 5], 0, 0.2), 160), -14)]
    return scene(parts, [(b + d + 0.05, let_out(rng, v, SIGH), -7)], lambda x: car(x, 0.1))


@recipe(RL, "set", "plank",
        "Put down, modelled: the coat sliding, boots set on the boards, the knees giving a little, the breath let out",
        """Modelled: the coats sliding off the shoulder (wool's stick-slip), the friend's boots set down on the boards (a
        heel's blow through a plank's modes over the car's hollow, then the toe), the knees giving a little so a boot
        shifts (a short scuff), and the carrier's breath let out (the crew's modelled breath, a little voice at its
        start). Each take a different crewmate.""",
        takes=3, lufs=-20)
def set_plank(rng, k):
    v = VOICES[(k + 2) % 3]
    b = rng.uniform(0.14, 0.18)
    d = rng.uniform(0.05, 0.09)
    parts = [(0, wool(rng, 0.22, 1.0), -8), (b, plank(rng, 0.9), 0), (b + 0.028, plank(rng, 0.6, True), -4),
             (b + d, plank(rng, 0.8, False, 1.08), -2), (b + d + 0.025, plank(rng, 0.5, True, 1.08), -6),
             (b + d + 0.12, ck.friction(rng, 0.1, 140, 300, 3500, 0.6, env([(0, 0), (0.02, 1), (0.1, 0)], 0.1)), -16)]
    return scene(parts, [(b + d + 0.04, let_out(rng, v), -7)], lambda x: car(x, 0.1))


@recipe(RL, "throw", "swing",
        "A friend thrown off the shoulder: a big 'hup!', the coats whooshing round and flapping as they go",
        """Horseplay, so no hurt in it: a quick breath in, the grip shifting (real cloth snatched), a big heave (the crew's
        modelled throat shoving a short 'hup' out, high with the effort and cut off hard, through a real gasp's air), and
        the friend's coat swung round off the shoulder: the packs' real cloth swish and a heavy leather drop's swoosh,
        stretched to the swing, with the air the body moves under them. It ends in the air; the landing is crew-carry's.
        Each take a different crewmate.""",
        sources=CLOTH[1:] + [DROP, GASP], takes=3, lufs=-18)
def throw_swing(rng, k):
    v = VOICES[k]
    h = rng.uniform(0.08, 0.12)
    sw = ck.norm(dsp.stretch(ck.align(ck.get(SWISH)), rng.uniform(1.0, 1.25)))
    whoosh_ = ck.norm(main_hit(DROP, 0.3))
    parts = [(0.03, ck.cloth(rng, k + 1, 0.1), -8), (h + 0.06, hp(sw, 200), -5), (h + 0.12, hp(whoosh_, 250), -10),
             (h + 0.05, ck.whoosh(rng, 0.42, 180, 1100, 0.45, 0.9), -10)]
    return scene(parts, [(0, nose_in(rng, v, 0.08), -18), (h, heave(rng, v, GASP), -2)])


@recipe(RL, "throw", "flap",
        "Thrown, modelled: a big 'hyah!', the body swung round and the coat flapping off the shoulder in the air it moves",
        """Modelled: the grip shifting (a short snatch of cloth), a big heave (the crew's modelled throat, a higher, bigger
        'hyah' cut off hard), the body swung round off the shoulder (a broad band of air rising and falling with its
        speed) and its coat catching that air and flapping like a flag for a moment (noise in the cloth's band chopped at
        the flapping rate, mishaps_body.flap). It ends in the air. Each take a different crewmate.""",
        takes=3, lufs=-18)
def throw_flap(rng, k):
    v = VOICES[k]
    h = rng.uniform(0.04, 0.07)
    L = rng.uniform(0.4, 0.48)
    fl = flap(rng, L, env([(0, 12), (L, 22)], L), 300, 2400, 2.5) * env([(0, 0), (0.08, 1), (L * 0.6, 0.6), (L, 0)], L)
    parts = [(0, snatch(rng, 0.05), -8), (h + 0.08, ck.whoosh(rng, L, 160, 1300, 0.4, 0.9), -6), (h + 0.14, fl, -12),
             (h + 0.1, wool(rng, 0.2, 1.4), -14)]
    return scene(parts, [(h, heave(rng, v, big=1.6), -2)])


# ---- Struck bodies, modelled ----------------------------------------------------------------------------------------------

def body(rng, modes, q, length, hit=0.004, bright=2500, gains=None):
    """A solid body struck once, modelled: a short dull blow (`hit` s of noise, darkened to `bright`) rung through its
    modes (`q` low: a heavy, dead body; high: one that rings)."""
    x = burst(rng, hit, hi=bright, attack=0.15)
    y = dsp.resonate(np.concatenate([x, np.zeros(samples(length), np.float32)]),
                     [m * rng.uniform(0.98, 1.02) for m in modes], q=q, gains=gains)
    return unit(dsp.fade(y, 0.0, length * 0.3))


def ring(rng, freqs, gains, decays, length, split=0.0):
    """A ring as its partials: decaying sines at `freqs`, each a doublet `split` Hz apart (a round body's paired modes,
    slightly unequal, beating as a bell's do)."""
    n = samples(length)
    t = np.arange(n) / SR
    y = np.zeros(n)
    for f, g, d in zip(freqs, gains, decays):
        pair = ((f, 1.0), (f + split * rng.uniform(0.5, 1.5), 0.55)) if split else ((f, 1.0),)
        for ff, gg in pair:
            y += g * gg * np.sin(2 * np.pi * ff * t + rng.uniform(0, 2 * np.pi)) * np.exp(-t / d)
    a = samples(0.0015)
    y[:a] *= np.linspace(0, 1, a)
    return unit(dsp.fade(y.astype(np.float32), 0.0, min(0.1, length * 0.2)))


IRON_BOX = [205, 330, 470, 690, 980, 1420, 2100, 3100]   # a cast-iron safe's thick walls: low, close, dead modes
IRON_DOOR = [520, 940, 1500, 2300, 3400]                  # its door, knocking in its frame
LATCH = [2800, 4100, 6200]                                # its latch's tick
OAK = [150, 235, 350, 500, 720, 1000, 1400]               # a big piece of oak (a 2 m figurehead, a bell's headstock)
OAK_CREAK = [m * 0.55 for m in synth.WOOD]                # oak's grain creaking: a heavy beam, not a board
# A church bell's partials over its prime: hum, prime, tierce (a minor third), quint, nominal (the strike note), then
# the upper ones; their strengths at the strike, and how long each rings with the bell's lip lying on the boards.
BELL_P = [0.5, 1.0, 1.2, 1.5, 2.0, 2.51, 2.67, 3.01, 4.07, 5.2]
BELL_G = [0.55, 0.5, 0.8, 0.3, 1.0, 0.45, 0.4, 0.3, 0.25, 0.18]
BELL_D = [0.9, 0.6, 0.7, 0.35, 0.5, 0.25, 0.22, 0.15, 0.1, 0.07]
PRIME = 372.0               # the bell's prime: a church bell two can carry, its nominal near 745 Hz, its hum at 186


def oak_knock(rng, w):
    """A big piece of oak struck on the floor, modelled: a dull blow through its bending modes, low and well damped."""
    return body(rng, [m * w for m in OAK], 8, 0.2, 0.006, 1500, [1, 0.9, 0.75, 0.55, 0.4, 0.25, 0.15])


def oak_creak(rng, length):
    """Oak's grain creaking as it settles, modelled: a stick-slip through a heavy beam's modes, quick enough to groan
    rather than tick, darkened."""
    c = synth.creak(length, env([(0, 28), (length, 48)], length), rng, body=OAK_CREAK, q=18, grit=0.05,
                    shape=env([(0, 0), (0.05, 1), (length * 0.7, 0.55), (length, 0)], length))
    return unit(lp(c, 3000))


# ---- loot-jackpot ---------------------------------------------------------------------------------------------------------

JL = "loot-jackpot"
# Who's at each end of the find, and how far apart they heave (s): together, or a beat apart.
PAIRS = ((MAN, WOMAN, 0.0), (WOMAN, BIG, 0.16), (BIG, MAN, 0.1))


def hoist(rng, voice, real=None):
    """One end's heave on a two-person lift: a short strained grunt as the weight comes up."""
    return strain(rng, voice, real, rng.uniform(0.36, 0.44))


def hangs_rec(rng, k):
    """What the find does as it leaves the floor, from real recordings: the safe's door knocking once in its frame (a real
    latch pitched down), the bell's clapper knocking softly inside it (the packs' real bell, struck faintly and
    darkened), the figurehead's oak creaking as it takes its own weight (a real creak dropped well down)."""
    if k == 0:
        return mix(ck.choke(at(rng, hit_of(LATCHES[0], 0, 0.1), -6), 0.006, 0.02),
                   hp(dsp.vari(hit_of(RATTLE, 0, 0.12), -5), 500) * 0.3)
    if k == 1:
        return mix(lp(ck.choke(at(rng, main_hit(BELLS[1], 0.6), -2), 0.05, 0.15), 2500),
                   ck.choke(at(rng, ck.get(METAL_H[3]), -8), 0.003, 0.01) * 0.3)
    c = dsp.vari(hit_of(CREAKS[1], 1, 0.35), -9 + rng.uniform(-1, 1))
    return unit(hp(c * dsp.fit(env([(0, 0), (0.05, 1), (0.3, 0.5), (0.35, 0)], 0.35), len(c)), 160))


def jackpot_lift_boards(rng, k, find):
    """Two crewmates' lift (take k's pair and timing) ending on the find itself (0 the safe, 1 the bell, 2 the figurehead)."""
    a, b, d = PAIRS[k]
    up = rng.uniform(0.24, 0.3) + d * 0.5
    board = dsp.vari(hit_of(CREAKS[(k + 2) % 3], 0, 0.25), -6 + rng.uniform(-1, 1))
    parts = [(0, hit_of(GRIPS[0], 0, 0.12), -10), (0.04, hit_of(GRIPS[1], 0, 0.12), -12),
             (0.1, ck.cloth(rng, k, 0.3), -14), (0.08, scrape(rng, "wood", 0.1, 0.6), -18),
             (up, squish(at(rng, ck.get(KNOCKS[k % 3]), -3), 160), -6),
             (up + 0.02, hp(board, 250) * dsp.fit(env([(0, 1), (0.25, 0)], 0.25), len(board)), -10),
             (up + rng.uniform(0.1, 0.18), hangs_rec(rng, find), -5),
             # a boot shifting under the load once it's up
             (up + rng.uniform(0.42, 0.5), on_wood(rng, toe(rng, k + 2), 0.6, "toe"), -10),
             (up + rng.uniform(0.42, 0.5), scrape(rng, "wood", 0.1, 0.5), -18)]
    voices = [(0.06, hoist(rng, a, GASP), -5), (0.06 + d, hoist(rng, b, GASP), -7)]
    return scene(parts, voices, lambda x: car(x, 0.1))


LIFT_HOW = """Two crewmates at either end: their gloves taking hold (the packs' real leather), each one's short strained heave
(the crew's modelled throat, through a real gasp's air), together in one take and a beat apart in the others: a man and a
woman, a woman and a bigger man, the bigger man and the first. The find coming up off the boards: a dull knock as its base
leaves them (a real wood knock, the boom taken out), the boards creaking back up (a real creak, pitched down), boots
bracing, and then the find itself: """
LIFT_SOURCES = GRIPS + [GASP] + KNOCKS + CREAKS + CLOTH[:3] + BOOTS


@recipe(JL, "lift-iron", "boards", "The safe lifted between two: a heave at each end, the boards creaking, its door knocking once",
        LIFT_HOW + "the safe's door knocking once in its frame (a real latch pitched down).",
        sources=LIFT_SOURCES + [LATCHES[0], RATTLE], takes=3, lufs=-19)
def jackpot_lift_iron_boards(rng, k):
    return jackpot_lift_boards(rng, k, 0)


@recipe(JL, "lift-bell", "boards", "The bell lifted between two: a heave at each end, the boards creaking, its clapper knocking",
        LIFT_HOW + "the bell's clapper knocking softly inside it (the packs' real bell, struck faintly and darkened).",
        sources=LIFT_SOURCES + [BELLS[1], METAL_H[3]], takes=3, lufs=-19)
def jackpot_lift_bell_boards(rng, k):
    return jackpot_lift_boards(rng, k, 1)


@recipe(JL, "lift-wood", "boards", "The figurehead lifted between two: a heave at each end, the boards creaking, its oak creaking",
        LIFT_HOW + "the figurehead's oak creaking as it takes its own weight (a real creak dropped well down).",
        sources=LIFT_SOURCES, takes=3, lufs=-19)
def jackpot_lift_wood_boards(rng, k):
    return jackpot_lift_boards(rng, k, 2)


def hangs_model(rng, k):
    """The find leaving the floor, modelled: the safe's door knocking in its frame (its modes), the bell's clapper
    knocking softly (its partials, faint and dark), the figurehead's grain creaking (stick-slip through oak)."""
    if k == 0:
        return body(rng, IRON_DOOR, 14, 0.06, 0.0015, 6000)
    if k == 1:
        r = ring(rng, [PRIME * p for p in BELL_P[:6]], BELL_G[:6], [d * 0.4 for d in BELL_D[:6]], 0.6, 0.8)
        return mix(r * 0.6, body(rng, [1900, 3100, 4700], 20, 0.03, 0.001, 7000) * 0.3)
    return oak_creak(rng, 0.35)


def jackpot_lift_model(rng, k, find):
    """The modelled lift: take k's pair and timing, ending on the find (0 the safe, 1 the bell, 2 the figurehead)."""
    a, b, d = PAIRS[k]
    up = rng.uniform(0.24, 0.3) + d * 0.5
    spring = synth.creak(0.22, env([(0, 30), (0.22, 75)], 0.22), rng, body=synth.WOOD, q=16,
                         shape=env([(0, 0), (0.03, 1), (0.22, 0)], 0.22))
    parts = [(0, snatch(rng, 0.03), -12), (0.04, snatch(rng, 0.03), -14), (0.1, wool(rng, 0.35, 0.9), -14),
             (up, plank(rng, 0.5, True, 0.9), -6), (up + 0.02, unit(spring), -10),
             (up + rng.uniform(0.1, 0.18), hangs_model(rng, find), -5),
             (up + rng.uniform(0.42, 0.5), plank(rng, 0.5, True), -10),
             (up + rng.uniform(0.42, 0.5), ck.friction(rng, 0.1, 150, 300, 3500, 0.6, env([(0, 0), (0.02, 1), (0.1, 0)], 0.1)), -18)]
    return scene(parts, [(0.06, hoist(rng, a), -5), (0.06 + d, hoist(rng, b), -7)], lambda x: car(x, 0.1))


MODEL_HOW = """Modelled: the two crewmates' short strained heaves (the crew's modelled throat on its own modelled breath; the
same pairs and timings as the other candidate), gloves closing (short snatches of leather), the coats shifting (wool's
stick-slip), the boards springing back as the weight comes off them (a stick-slip creak through a plank's modes) with a
soft knock as the base leaves, and the find: """


@recipe(JL, "lift-iron", "model", "The safe lifted between two, modelled: a heave at each end, the boards springing, its door knocking",
        MODEL_HOW + "the safe's door knocking in its frame (an iron door's modes).", takes=3, lufs=-19)
def jackpot_lift_iron_model(rng, k):
    return jackpot_lift_model(rng, k, 0)


@recipe(JL, "lift-bell", "model", "The bell lifted between two, modelled: a heave at each end, the boards springing, its clapper",
        MODEL_HOW + "the bell's clapper knocking faintly (its partials).", takes=3, lufs=-19)
def jackpot_lift_bell_model(rng, k):
    return jackpot_lift_model(rng, k, 1)


@recipe(JL, "lift-wood", "model", "The figurehead lifted between two, modelled: a heave at each end, the boards springing, oak creaking",
        MODEL_HOW + "the figurehead's oak creaking (stick-slip through oak).", takes=3, lufs=-19)
def jackpot_lift_wood_model(rng, k):
    return jackpot_lift_model(rng, k, 2)


def safe_rec(rng, k):
    """The safe's weight meeting the floor, from real iron: a heavy plate's dead blow and a heavy iron hit dropped for
    thick cast iron and choked at once (it can't ring: it's a solid box of the stuff), a real blow on steel under them."""
    plate = ck.choke(at(rng, ck.get(PLATE_H[k % 5]), -5), 0.02, 0.04)
    iron = ck.choke(at(rng, ck.get(METAL_H[(k + 2) % 5]), -9), 0.004, 0.012)
    blow = ck.choke(at(rng, main_hit(BLOW, 0.3), -7), 0.015, 0.03)
    return unit(mix(plate, iron * 0.5, blow * 0.4))


def knocking(rng, n, knock, start=-8.0):
    """A loose door knocking in its frame after a blow, like anything bouncing: `n` knocks (`knock(i)` makes the i-th),
    each sooner and quieter than the last."""
    parts, t, g = [], 0.0, start
    gap = rng.uniform(0.07, 0.09)
    for i in range(n):
        parts.append((t, knock(i), g))
        t += gap
        gap *= rng.uniform(0.62, 0.75)
        g -= rng.uniform(3, 5)
    return ck.place(parts)


def settle_times(rng, k):
    """When the safe's weight comes down (after a corner touches first) and when it rocks back onto its last foot."""
    c = rng.uniform(0.03, 0.06)
    return c, c + rng.uniform(0.28, 0.34) + 0.04 * k


@recipe(JL, "set-iron", "boards",
        "The safe set down on the boards: a dead, heavy iron thud, its door rattling in its frame",
        """From real recordings: a corner touching first, then the safe's whole weight down (a heavy plate's dead blow and a
        heavy iron hit, dropped for thick cast iron and choked at once, with a real blow on steel under them; the car's
        boards answering with a real wood knock and the floor's hollow), its door knocking in its frame, quicker and
        dying (real latches pitched down for a heavy door, a real loose-metal rattle), then the safe rocking back onto
        its last foot with a smaller thud and the door knocking once or twice more, and the brass dial's small tick.
        The boom taken out so it lands heavy, not soft.""",
        sources=PLATE_H[:4] + METAL_H + [BLOW] + WOOD_H[1:4] + KNOCKS[1:] + [THUNK] + LATCHES +
        [RATTLE, "kenney_rpg-audio:metalClick"], takes=3, lufs=-17)
def set_iron_boards(rng, k):
    c, r = settle_times(rng, k)
    boards = squish(at(rng, ck.get(WOOD_H[(k + 1) % 5]), -2), 150)
    dial = dsp.vari(hit_of("kenney_rpg-audio:metalClick", 0, 0.05), 2 + rng.uniform(-1, 1))
    clack = lambda i: ck.choke(at(rng, hit_of(LATCHES[(k + i) % 3], 0, 0.1), -6), 0.006, 0.02)
    rattle = hp(dsp.vari(hit_of(RATTLE, 0, 0.15), -5), 500)
    parts = [(0, ck.choke(at(rng, ck.get(METAL_H[(k + 4) % 5]), -6), 0.003, 0.01), -14),
             (c, safe_rec(rng, k), 0), (c, ck.floor(rng, "wood", k, 3.5, 0.9), -3), (c, boards, -6),
             (c + 0.035, knocking(rng, 3, clack, -8), 0), (c + 0.04, rattle, -14),
             (r, safe_rec(rng, k + 1), -13), (r, ck.floor(rng, "wood", k + 1, 2.0, 0.9), -16),
             (r + 0.03, knocking(rng, 2, clack, -12), 0), (c + rng.uniform(0.06, 0.09), dial, -22)]
    return squish(car(ck.place(parts), 0.1))


def iron_box(rng, w=1.0):
    """The safe's weight on the floor, modelled: a dull blow rung through a thick cast-iron box's low, close modes."""
    return body(rng, [m * w for m in IRON_BOX], 7, 0.18, 0.006, 1800, [1, 0.9, 0.8, 0.6, 0.45, 0.3, 0.2, 0.12])


@recipe(JL, "set-iron", "model",
        "The safe set down, modelled: a thick iron box's dead blow on the boards, its door knocking in its frame",
        """Modelled from what it is: a corner touching first, then the weight down: a dull blow rung through a thick
        cast-iron box's low, close modes (so damped by their own mass they barely ring), the boards under it (a plank's
        modes over the car's hollow), the door knocking in its frame (an iron door's own modes, a latch's tick), the safe
        rocking back onto its last foot and the door knocking again, and the brass dial's tiny click.""",
        takes=3, lufs=-17)
def set_iron_model(rng, k):
    c, r = settle_times(rng, k)
    clack = lambda i: mix(body(rng, IRON_DOOR, 14, 0.06, 0.0015, 6000), body(rng, LATCH, 25, 0.02, 0.0008, 9000) * 0.4)
    parts = [(0, body(rng, IRON_BOX[3:], 10, 0.05, 0.0015, 5000), -16),
             (c, iron_box(rng), 0), (c, plank(rng, 1.0, False, 0.85), -4), (c, ck.hollow(rng, [95, 150, 240], 0.15, q=4), -8),
             (c + 0.035, knocking(rng, 3, clack, -10), 0),
             (r, iron_box(rng, 1.04), -12), (r, plank(rng, 0.6, False, 0.85), -15),
             (r + 0.03, knocking(rng, 2, clack, -12), 0),
             (c + rng.uniform(0.06, 0.09), body(rng, [3800, 6100], 30, 0.02, 0.0006, 9000), -24)]
    return squish(car(ck.place(parts), 0.1))


def oak_rec(rng, k):
    """Heavy oak meeting the floor, from real wood: a heavy wood hit, a plank's hollow knock and a heavy door's thunk,
    dropped for a big piece, their boom taken out: what's left is the oak's deep knock."""
    a = squish(at(rng, ck.get(WOOD_H[k % 5]), -2), 130)
    b = squish(at(rng, ck.get(PLANK[(k + 1) % 5]), -3), 130)
    c = squish(at(rng, main_hit(THUNK, 0.35), -4), 130)
    return unit(mix(a, b * 0.7, c * 0.6))


def creak_rec(rng, k, length=0.3, semis=-8.0):
    """Oak creaking as it settles, from a real creak dropped well down for a heavy beam."""
    c = dsp.vari(hit_of(CREAKS[k % 3], int(rng.integers(2)), length), semis + rng.uniform(-1, 1))
    return unit(hp(c, 160) * dsp.fit(env([(0, 0), (0.04, 1), (length * 0.7, 0.5), (length, 0)], length), len(c)))


@recipe(JL, "set-wood", "oak",
        "Heavy oak laid down: the figurehead's base and then its carved head, or the bell's headstock; a deep knock and a creak",
        """From real wood: the oak's deep knock (a heavy wood hit, a plank's hollow knock and a heavy door's thunk, dropped
        for a big piece and the boom taken out) with the car's boards answering, and the oak creaking as it settles (a
        real creak dropped well down). Takes 0 and 2 the figurehead: its base end down, then its carved head a beat
        later and lighter, and in take 2 rocking once on its carving. Take 1 the bell's headstock: one square, heavier
        knock, its iron straps ticking.""",
        sources=WOOD_H[:4] + PLANK[1:] + [THUNK] + CREAKS + KNOCKS[:2] + [METAL_H[1], METAL_H[4]], takes=3, lufs=-18)
def set_wood_oak(rng, k):
    if k == 1:
        straps = ck.choke(at(rng, ck.get(METAL_H[1]), -7), 0.003, 0.012)
        bolt = ck.choke(at(rng, ck.get(METAL_H[4]), -5), 0.002, 0.008)
        parts = [(0, oak_rec(rng, k), 0), (0, ck.floor(rng, "wood", k, 3.0, 0.5), -4), (0.004, straps, -12),
                 (0.07, oak_rec(rng, k + 2), -12), (0.14, creak_rec(rng, k, 0.42), -11), (0.5, bolt, -20)]
    else:
        h = rng.uniform(0.14, 0.2)
        parts = [(0, oak_rec(rng, k), 0), (0, ck.floor(rng, "wood", k, 2.5, 0.5), -4),
                 (h, oak_rec(rng, k + 3), -5), (h, ck.floor(rng, "wood", k + 1, 1.5, 0.5), -9),
                 (h + 0.08, creak_rec(rng, k), -10)]
        if k == 2:
            parts.append((h + rng.uniform(0.3, 0.36), oak_rec(rng, k + 1), -15))
    return squish(car(ck.place(parts), 0.1), 130)


@recipe(JL, "set-wood", "model",
        "Heavy oak laid down, modelled: a big oak piece's deep knock on the boards and its grain creaking as it settles",
        """Modelled: a dull blow rung through a big oak piece's bending modes (low and well damped), the boards under it
        (a plank's modes over the car's hollow), and the grain creaking as it settles (a stick-slip through a heavy
        beam's modes, quick enough to groan). The same three events as the other candidate: the figurehead's base then
        its head (and a rock on its carving), the headstock's single square knock with its iron straps ticking.""",
        takes=3, lufs=-18)
def set_wood_model(rng, k):
    if k == 1:
        parts = [(0, oak_knock(rng, 1.05), 0), (0, plank(rng, 1.0, False, 0.85), -5),
                 (0.004, body(rng, IRON_DOOR, 12, 0.03, 0.001, 6000), -14), (0.07, oak_knock(rng, 1.1), -12),
                 (0.14, oak_creak(rng, 0.42), -13), (0.5, body(rng, LATCH, 20, 0.02, 0.0008, 8000), -22)]
    else:
        h = rng.uniform(0.14, 0.2)
        parts = [(0, oak_knock(rng, 0.95), 0), (0, plank(rng, 0.9, False, 0.85), -5), (h, oak_knock(rng, 1.12), -5),
                 (h, plank(rng, 0.6, False, 0.9), -9), (h + 0.08, oak_creak(rng, 0.3), -12)]
        if k == 2:
            parts.append((h + rng.uniform(0.3, 0.36), oak_knock(rng, 1.2), -15))
    return squish(car(ck.place(parts), 0.1), 130)


def floor_damp(x, at_, tau):
    """A ring cut short by the floor: free for a moment, then dying with time constant `tau` (the lip on the boards)."""
    t = np.arange(len(x)) / SR
    return (x * np.where(t < at_, 1.0, np.exp(-(t - at_) / tau))).astype(np.float32)


@recipe(JL, "bell", "bronze",
        "The church bell knocked down on its side: a real bronze bell's ring cut short by the floor, the clapper knocking inside",
        """A real bronze bell struck (the packs' heavy bell: its hum, prime, tierce and nominal are all there, a little out
        of tune as an old bell's are), dropped two semitones for a bell two can carry. Its lip comes down on the car's
        boards (a real wood knock under it) and the floor damps it: it rings free for a moment and is cut short, dull. A
        beat later the clapper swings and knocks inside it (a real heavy iron hit, muffled by the bronze round it) and the
        bell answers again, quieter; in take 1 it knocks twice. Take 2 is knocked harder, brighter.""",
        sources=[BELLS[1]] + METAL_H[:3] + [KNOCKS[0], KNOCKS[2], THUNK], takes=3, lufs=-19)
def bell_bronze(rng, k):
    semis = -2 + rng.uniform(-0.15, 0.15)
    bell = dsp.vari(ck.get(BELLS[1]), semis)
    struck_ = floor_damp(bell, (0.08, 0.06, 0.12)[k], (0.6, 0.55, 0.7)[k])
    if k == 2:
        struck_ = mix(struck_, hp(struck_, 1500) * 0.6)
    parts = [(0, struck_, 0), (0, ck.floor(rng, "wood", k, 2.0, 0.8), -10)]
    t = rng.uniform(0.5, 0.62)
    for i in range(1 + (k == 1)):
        knock = lp(ck.choke(at(rng, ck.get(METAL_H[(k + i) % 5]), -6), 0.004, 0.015), 2200)
        again = floor_damp(dsp.vari(ck.get(BELLS[1]), semis), 0.02, 0.42)
        parts += [(t, knock, -12 - 4 * i), (t, lp(again, 3000), -9 - 5 * i)]
        t += rng.uniform(0.22, 0.3)
    return squish(car(ck.place(parts), 0.08))


@recipe(JL, "bell", "partials",
        "The church bell knocked down on its side, modelled: hum, prime, tierce, quint and nominal, cut short, the clapper inside",
        """Modelled as a church bell's partials over a 372 Hz prime: the hum an octave under it, the prime, the minor-third
        tierce, the quint and the nominal (the strike note, near 745 Hz), and the upper partials, each a pair of modes a
        hertz or so apart so it warbles as a real bell does. Its lip on the boards cuts it short: every partial dies in
        under a second, the high ones first. A short bright clank of bronze at the strike with the boards' knock under it,
        and the clapper's knock inside a beat later (a small iron knock, the bell answering faintly, darker). Take 1
        knocks twice; take 2 is struck harder.""",
        takes=3, lufs=-19)
def bell_partials(rng, k):
    p = PRIME * 2 ** (rng.uniform(-0.1, 0.1) / 12)
    hard = (1.0, 0.8, 1.4)[k]
    gains = [g * (1 + (hard - 1) * i / 6) for i, g in enumerate(BELL_G)]
    main = ring(rng, [p * r for r in BELL_P], gains, BELL_D, 2.2, 0.9)
    clank = hp(burst(rng, 0.003, attack=0.1), 1500)
    parts = [(0, main, 0), (0, unit(clank), -16 + 4 * (hard - 1)), (0, plank(rng, 0.9, False, 0.8), -10)]
    t = rng.uniform(0.5, 0.62)
    for i in range(1 + (k == 1)):
        again = ring(rng, [p * r for r in BELL_P[:6]], [g * (0.7 ** j) for j, g in enumerate(BELL_G[:6])],
                     [d * 0.6 for d in BELL_D[:6]], 1.2, 0.9)
        parts += [(t, body(rng, [1900, 3100, 4700], 20, 0.03, 0.001, 5000), -18 - 4 * i), (t, again, -10 - 5 * i)]
        t += rng.uniform(0.22, 0.3)
    return squish(car(ck.place(parts), 0.08))


# ---- loot-fragile ---------------------------------------------------------------------------------------------------------

FL = "loot-fragile"
BOTTLE = [1150, 2650, 4400, 6300]           # a wine bottle's shell
CRATE = [m * 1.25 for m in synth.WOOD]      # a crate's thin slats
BOX = [280, 450, 700]                       # the crate's hollow
CRYSTAL_M = [1450, 3400, 5900, 8700]        # a lead-crystal decanter's thick body: it rings long and bright
BASE = [900, 2100, 3800]                    # its heavy base, knocked
NECK = [2300, 5600, 9000]                   # its neck, snapped off and ringing on its own
CHIP = [6500, 9800]                         # a chip off its cut facets
CASE = [m * 1.6 for m in synth.WOOD]        # a mantel clock's small wooden case
CASE_HOLLOW = [320, 540, 830]
COVER = [2900, 6100, 9800]                  # the dial's thin glass cover
GONG = [1.0, 2.76, 5.40, 8.93]              # a coiled gong's partials: a stiff rod's, inharmonic
GONG_G = [1.0, 0.6, 0.35, 0.2]
GONG_D = [0.25, 0.16, 0.1, 0.06]            # it hums a moment, the high partials gone first
GONG_F = 236.0                              # a mantel clock's coil gong: low and humming


def straw(x, wet=0.3):
    """Inside the crate, packed in straw: the top soaked up and the crate's small box ringing round it."""
    return dsp.room(lp(x, 6500, 2), "box", wet=wet, rng=np.random.default_rng(5))


def crate_knock(rng, k):
    """The crate's slatted wood knocked, from real wood: a medium wood hit and a plank's knock, pitched up a little for
    thin slats, the boom out, and a loose slat knocking after."""
    a = squish(at(rng, ck.get(WOOD_M[k % 5]), 2), 160)
    b = squish(at(rng, ck.get(PLANK[(k + 2) % 5]), 3), 160)
    slat = hp(ck.choke(at(rng, ck.get(KNOCKS[2]), 5), 0.01, 0.02), 300)
    return ck.place([(0, unit(mix(a, b * 0.7)), 0), (rng.uniform(0.03, 0.05), slat, -12)])


def clinks_rec(rng, n, spread, semis=-3.0):
    """Bottles knocking against each other as they settle: the packs' real small glass knocks, pitched down for bottles."""
    return ck.place([(rng.uniform(0, spread), ck.choke(at(rng, ck.get(TINKS[int(rng.integers(3))]), semis), 0.02, 0.04),
                      rng.uniform(-10, 0)) for _ in range(n)], spread + 0.3)


@recipe(FL, "glass", "crate",
        "A crate of bottles in straw knocked: the crate's wood, bottles breaking and grinding inside, the straw rustling",
        """From real recordings: the crate's slats knocked (a medium wood hit and a plank's knock, pitched up a little for
        thin slats, a loose slat knocking after), one bottle and then another breaking inside it (the packs' real glass
        breaking), the broken glass grinding as the bottles settle (real glass breaks laid grain by grain, slowing), a
        couple of whole bottles knocking together, and the straw rustling round them (a belt's real dry crackle, laid
        the same way). The glass is heard through the straw and the crate: its top soaked up, the box ringing.""",
        sources=WOOD_M[:3] + PLANK[2:] + KNOCKS[2:] + BREAKS + TINKS + CRACKLE, takes=3, lufs=-18)
def glass_crate(rng, k):
    L = rng.uniform(0.8, 0.95)
    g = L - 0.1
    breaks = [(0.004, at(rng, ck.get(BREAKS[k % 3]), rng.uniform(-2, 0)), -2),
              (rng.uniform(0.04, 0.09), at(rng, ck.get(BREAKS[(k + 1) % 3]), rng.uniform(-4, -2)), -7)]
    grind = lay(rng, g, env([(0, 160), (g * 0.3, 70), (g, 10)], g), env([(0, -2), (g, -5)], g), BREAKS, [2000] * 3)
    grind = grind * dsp.fit(env([(0, 1), (g, 0.1)], g), len(grind))
    rustle = lay(rng, L * 0.8, env([(0, 120), (L * 0.8, 15)], L * 0.8), 0.0, CRACKLE, [1500] * 2)
    rustle = rustle * dsp.fit(env([(0, 1), (L * 0.8, 0)], L * 0.8), len(rustle))
    glass = straw(ck.place(breaks + [(0.06, grind, -7), (0.12, clinks_rec(rng, 2 + k % 2, 0.4), -12)]))
    parts = [(0, crate_knock(rng, k), 0), (0.002, glass, -1), (0.01, rustle, -15)]
    return squish(car(ck.place(parts), 0.08), 150)


def bottle_break(rng):
    """One bottle breaking, modelled: a crack (a hard impulse through glass's stiff modes), its shell ringing for an
    instant, and its shards: many small hard clicks, high-Q, front-loaded as the pieces fall in on each other."""
    crack = snap(rng, 0.04, rng.uniform(2400, 3600))
    shell = ring(rng, [m * rng.uniform(0.9, 1.1) for m in BOTTLE], [1, 0.7, 0.5, 0.35], [0.04, 0.03, 0.02, 0.015], 0.12)
    shards = ck.grains(rng, 40, 0.25, 2500, 9000, q=(20, 50), length=(0.01, 0.04), shape=0.6)
    return ck.place([(0, unit(crack), 0), (0, shell, -6), (0.004, unit(shards), -4)])


@recipe(FL, "glass", "shards",
        "A crate of bottles knocked, modelled: slats knocked, bottles cracking and their shards grinding in the straw",
        """Modelled from what's in it: the crate's thin slats struck (a short blow through their modes and the box's
        hollow), a bottle and then another breaking inside (a hard crack through glass's stiff modes, the shell ringing
        for an instant, a spray of small hard high-Q clicks as the pieces fall in), the broken glass grinding as it
        settles (stick-slip friction high up, glass grains in it, slowing), whole bottles knocking (their shells), and
        the straw rustling (thousands of tiny dry scrapes). Heard through the straw and the crate.""",
        takes=3, lufs=-18)
def glass_shards(rng, k):
    L = rng.uniform(0.8, 0.95)
    g = L - 0.1
    sh = env([(0, 0), (0.02, 1), (g * 0.3, 0.4), (g, 0)], g)
    bits = ck.grains(rng, int(90 * g), g, 2500, 9000, q=(15, 40), length=(0.005, 0.02), shape=0.3)
    grind = mix(ck.friction(rng, g, 140, 1800, 8000, 0.9, sh) * 0.35, dsp.fit(bits, len(sh)) * sh)
    knock = lambda: ring(rng, [m * rng.uniform(0.85, 0.95) for m in BOTTLE], [1, 0.6, 0.4, 0.3], [0.05, 0.03, 0.02, 0.015], 0.1)
    rustle = synth.rustle(L * 0.8, env([(0, 1500), (L * 0.8, 150)], L * 0.8), rng, f=(2500, 9000), ticks=0.5)
    glass = straw(ck.place([(0.004, bottle_break(rng), -2), (rng.uniform(0.04, 0.09), bottle_break(rng), -7),
                            (0.06, unit(grind), -10), (rng.uniform(0.15, 0.3), knock(), -14),
                            (rng.uniform(0.35, 0.5), knock(), -16)]))
    crate = mix(body(rng, CRATE, 12, 0.08, 0.004, 3000), ck.hollow(rng, BOX, 0.1, q=5) * 0.5)
    parts = [(0, unit(crate), 0), (0.002, glass, -1), (0.01, unit(rustle), -18),
             (rng.uniform(0.03, 0.05), body(rng, [m * 1.1 for m in CRATE], 14, 0.03, 0.002, 4000), -14)]
    return squish(car(ck.place(parts), 0.08), 150)


def partials_of(x, n=5, lo=500.0, apart=50.0):
    """A ringing recording's strongest partials: the peaks of its spectrum (Hz), at least `apart` Hz from each other, and
    their strengths."""
    N = 1 << 16
    X = np.abs(np.fft.rfft(x * np.hanning(len(x)), n=N))
    f = np.fft.rfftfreq(N, 1 / SR)
    X[f < lo] = 0
    pk, _ = signal.find_peaks(X, distance=int(apart / f[1]))
    top = pk[np.argsort(X[pk])[::-1][:n]]
    return f[top], X[top] / X[top].max()


def crystal_rec(rng, length, semis=-7.0, key=CRYSTAL[1]):
    """The decanter's own ring, from a real glass struck: the glass's strike (its first few milliseconds) rung through
    its own partials, measured off the recording and dropped for a heavy lead-crystal body, each a pair a couple of
    hertz apart so it warbles slowly as cut glass does, let ring a little. The recording's later knocks are left out:
    only the one strike rings."""
    x = ck.align(ck.get(key))
    freqs, amps = partials_of(x[:samples(0.25)])
    r = 2 ** ((semis + rng.uniform(-0.3, 0.3)) / 12)
    exc = np.concatenate([x[:samples(0.004)] * np.hanning(samples(0.008))[samples(0.004):], np.zeros(samples(length), np.float32)])
    tau = length * 0.3
    y = sum(a * g * unit(dsp.resonate(exc, [ff], q=tau * np.pi * ff))
            for f, a in zip(freqs * r, amps) for ff, g in ((f, 1.0), (f + rng.uniform(1.5, 3.0), 0.5)))
    return unit(dsp.fade(mix(unit(y), dsp.vari(x[:samples(0.004)], semis) * 0.3), 0.0, length * 0.3))


def crack_rec(rng, k, semis=0.0, length=0.025):
    """A bright crack, from the first instant of a real crack in glass, the body taken out of it."""
    x = ck.cut(main_hit(CRACKS[k % 2], 0.06), 0, samples(length), 0.0005, 0.01)
    return unit(hp(dsp.vari(x, semis), 2500))


@recipe(FL, "decanter", "cut",
        "A cut-glass decanter knocked: its heavy base's knock, a bright crack, a chip or its neck snapping off, a ring of crystal",
        """From real glass: the decanter's thick base knocking (the packs' real heavy glass knocked) and a bright crack (the
        first instant of a real crack, its body taken out), then the crystal ringing a little (a real glass's strike rung
        through that glass's own partials, dropped for a heavy lead-crystal body). Take 0 cracks; take 1 throws a chip
        off its cut facets, which lands with two small tinks; take 2 snaps its neck off (a slice of real glass breaking),
        the neck ringing on its own as it falls and clinking down.""",
        sources=GLASS_H[:3] + CRACKS + CRYSTAL[:2] + TINKS + BREAKS[1:2], takes=3, lufs=-19)
def decanter_cut(rng, k):
    knock = hp(ck.choke(at(rng, ck.get(GLASS_H[k % 5]), -1), 0.02, 0.04), 300)
    parts = [(0, knock, -3), (0.003, crack_rec(rng, k), -2), (0.004, crystal_rec(rng, 0.7), -6)]
    if k == 1:
        t = rng.uniform(0.14, 0.2)
        parts += [(0.006, crack_rec(rng, k + 1, 4, 0.012), -8),
                  (t, ck.choke(at(rng, ck.get(TINKS[0]), 5), 0.01, 0.03), -12),
                  (t + rng.uniform(0.07, 0.1), ck.choke(at(rng, ck.get(TINKS[1]), 6), 0.005, 0.02), -18)]
    elif k == 2:
        t = rng.uniform(0.22, 0.3)
        snap_ = hp(ck.cut(main_hit(BREAKS[1], 0.1), 0, samples(0.08), 0.0005, 0.03), 1500)
        parts += [(0.008, snap_, -4), (0.01, crystal_rec(rng, 0.3, -1, CRYSTAL[0]), -12),
                  (t, ck.choke(at(rng, ck.get(TINKS[2]), 2), 0.02, 0.04), -9),
                  (t + rng.uniform(0.08, 0.11), ck.choke(at(rng, ck.get(TINKS[2]), 3), 0.01, 0.03), -16)]
    return squish(car(ck.place(parts), 0.08), 300)


def crack_model(rng, n=10):
    """Glass cracking, modelled: a hard impulse, then the crack running through it in a quick burst of high-Q ticks."""
    y = np.zeros(samples(0.04), np.float32)
    y[:samples(0.0008)] = rng.standard_normal(samples(0.0008))
    for _ in range(n):
        c = synth.click(rng.uniform(3000, 10000), q=rng.uniform(25, 60), length=0.012, rng=rng)
        a = samples(abs(rng.normal(0, 0.006)))
        m = min(len(c), len(y) - a)
        y[a:a + m] += c[:m] * rng.uniform(0.2, 1.0)
    return unit(hp(y, 2000))


@recipe(FL, "decanter", "lead",
        "A lead-crystal decanter knocked, modelled: a thick glass knock, a crack running, a chip or the neck off, a bright ring",
        """Modelled: the heavy base knocked (a short blow through thick glass's modes), a crack running through it (a hard
        impulse and a quick burst of high, hard ticks), and the body ringing bright and clear for a moment (a lead
        crystal's modes, each a pair beating, dying fast at the top). The same three events as the other candidate: a
        crack; a chip off its facets (its own tiny ring, landing in two tinks); the neck snapping off and ringing higher
        on its own as it falls and clinks down.""",
        takes=3, lufs=-19)
def decanter_lead(rng, k):
    bright = ring(rng, CRYSTAL_M, [1, 0.6, 0.4, 0.25], [0.2, 0.13, 0.08, 0.05], 0.75, 1.5)
    parts = [(0, body(rng, BASE, 30, 0.05, 0.002, 6000), -3), (0.003, crack_model(rng), -2), (0.004, bright, -8)]
    tink = lambda f: ring(rng, [f, f * 1.51], [1, 0.4], [0.02, 0.012], 0.05)
    if k == 1:
        t = rng.uniform(0.14, 0.2)
        parts += [(0.006, ring(rng, CHIP, [1, 0.6], [0.03, 0.02], 0.08), -10), (t, tink(6800), -12),
                  (t + rng.uniform(0.07, 0.1), tink(7400), -18)]
    elif k == 2:
        t = rng.uniform(0.22, 0.3)
        parts += [(0.008, crack_model(rng, 16), -4), (0.01, ring(rng, NECK, [1, 0.5, 0.3], [0.12, 0.06, 0.03], 0.35, 2.0), -10),
                  (t, tink(3900), -9), (t + rng.uniform(0.08, 0.11), tink(4300), -16)]
    return squish(car(ck.place(parts), 0.08), 300)


def gong_rec(length, force=1.0):
    """The clock's coiled gong set humming, from a real iron strike: its first instant rung through a coiled rod's
    inharmonic partials (each its own resonance, the low one longest), a slice of the real ring dropped under them."""
    strike = main_hit(METAL_M[0], 0.012)
    exc = np.concatenate([strike, np.zeros(samples(length), np.float32)])
    y = np.zeros(len(exc), np.float32)
    for r, g, d in zip(GONG, GONG_G, GONG_D):
        f = GONG_F * r
        y += g * unit(dsp.resonate(exc, [f], q=d * np.pi * f))
    real = lp(dsp.vari(main_hit(METAL_M[0], 0.25), -12), 2500)
    return unit(mix(unit(y), unit(real) * 0.3 * force))


def works_rec(rng, length, rate=60.0):
    """The works jangling in the case, from real mechanisms: a ratchet and pawl, a small mechanism, gears and loose keys
    laid grain by grain and pitched up for small brass works, quick and dying."""
    y = lay(rng, length, env([(0, rate), (length, 8)], length), 3.0, WORKS + JANGLE, [1500] * 4)
    return y * dsp.fit(env([(0, 1), (length, 0.05)], length), len(y))


@recipe(FL, "clock", "mantel",
        "A mantel clock knocked: its wooden case, the dial's glass cracking, the works jangling, the gong humming a moment",
        """From real recordings: the clock's small wooden case knocked (a light wood hit pitched up, the case's hollow
        under it), the dial's glass cover cracking (the first instant of a real crack, pitched up for thin glass, a tiny
        real glass tinkle after), the works jangling inside (a real ratchet and pawl, a small mechanism, gears and loose
        keys laid grain by grain and pitched up for brass), and the coiled gong set humming a moment (a real iron strike
        rung through a coiled rod's inharmonic partials). Take 1 knocked harder, the case bouncing once; take 2 slid into
        something, a scrape first.""",
        sources=WOOD_L[:4] + CRACKS + TINKS + WORKS + JANGLE + METAL_M[:1], takes=3, lufs=-19)
def clock_mantel(rng, k):
    t0 = rng.uniform(0.12, 0.16) if k == 2 else 0.0
    case = squish(at(rng, ck.get(WOOD_L[k % 5]), 3), 200)
    parts = [(t0, case, -3), (t0, ck.hollow(rng, CASE_HOLLOW, 0.08, q=6), -10),
             (t0 + 0.004, crack_rec(rng, k, 4, 0.02), -6),
             (t0 + rng.uniform(0.03, 0.06), ck.choke(at(rng, ck.get(TINKS[k % 3]), 7), 0.008, 0.02), -16),
             (t0 + 0.01, works_rec(rng, 0.4 + 0.1 * (k == 1)), -6),
             (t0 + 0.006, gong_rec(1.2, 1.0 + 0.3 * (k == 1)), -16 + 2 * (k == 1))]
    if k == 1:
        parts.append((t0 + rng.uniform(0.14, 0.18), squish(at(rng, ck.get(WOOD_L[(k + 2) % 5]), 4), 200), -8))
    if k == 2:
        parts.append((0, ck.friction(rng, t0, 160, 400, 4000, 0.6, env([(0, 0), (0.03, 1), (t0, 0.8)], t0)), -14))
    y = car(ck.place(parts), 0.08)
    return squish(dsp.fade(dsp.fit(y, samples(1.4)), 0.0, 0.3), 180)


def works_model(rng, length):
    """The works jangling, modelled: wheels and pins knocking (small hard brass clicks, many and quick, dying) and the
    pendulum and its spring rattling (a quick run of small knocks slowing)."""
    w = ck.grains(rng, 30, length, 2500, 8000, q=(10, 30), length=(0.004, 0.015), shape=0.8)
    run = ck.place([(i * 0.03 * (1.1 ** i), synth.click(rng.uniform(1800, 2600), q=20, length=0.015, rng=rng), -2 * i)
                    for i in range(8)])
    return unit(mix(unit(w), unit(run) * 0.5))


@recipe(FL, "clock", "works",
        "A mantel clock knocked, modelled: the case's knock, the cover glass cracking, wheels and pendulum rattling, the gong",
        """Modelled from what's in it: the small wooden case struck (a short blow through its thin walls' modes and its
        hollow), the dial's thin glass cover cracking (a crack's impulse and high ticks, the cover's own modes ringing an
        instant), the works rattling (small hard brass clicks, the pendulum and its spring knocking in a quick run that
        slows), and the coiled gong humming a moment (a stiff rod's inharmonic partials over 236 Hz, each a pair that
        beats, the high ones gone first). The same three takes as the other candidate.""",
        takes=3, lufs=-19)
def clock_works(rng, k):
    t0 = rng.uniform(0.12, 0.16) if k == 2 else 0.0
    gong = ring(rng, [GONG_F * r for r in GONG], [g * (1 + 0.3 * (k == 1) * i) for i, g in enumerate(GONG_G)], GONG_D,
                1.2, 0.7)
    parts = [(t0, body(rng, CASE, 12, 0.06, 0.003, 4000), -3), (t0, ck.hollow(rng, CASE_HOLLOW, 0.08, q=6), -10),
             (t0 + 0.004, crack_model(rng, 8), -6), (t0 + 0.005, ring(rng, COVER, [1, 0.6, 0.4], [0.03, 0.02, 0.012], 0.06), -12),
             (t0 + 0.01, works_model(rng, 0.4 + 0.1 * (k == 1)), -6), (t0 + 0.006, gong, -16 + 2 * (k == 1))]
    if k == 1:
        parts.append((t0 + rng.uniform(0.14, 0.18), body(rng, [m * 1.1 for m in CASE], 12, 0.05, 0.003, 4000), -8))
    if k == 2:
        parts.append((0, ck.friction(rng, t0, 160, 400, 4000, 0.6, env([(0, 0), (0.03, 1), (t0, 0.8)], t0)), -14))
    y = car(ck.place(parts), 0.08)
    return squish(dsp.fade(dsp.fit(y, samples(1.4)), 0.0, 0.3), 180)
