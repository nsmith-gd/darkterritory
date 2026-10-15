"""Odd outcomes in the dead villages (world-odd, K1's note 730: small strange things that happen when nobody's looking)
and emotes for two (crew-emotes, note 722): a scarecrow found turned to face the line, a letter nobody collected taken
out of a pillar box and opened, Dave's small painting set down at your feet, a crewmate come out black from under the
coaling chute, a switch thrown under someone standing on its blades; and the crew's high five, fist bump, sitting down
on an edge and leaning out of a door.

Gameplay foley, so each is the real thing, trimmed and shaped: what it's made of, how big, the space it's in. The crew
are grown men and women in heavy work coats, boots and leather work gloves, so a hand is a glove, a slap is leather on
leather and the coat is in everything they do.
- Each cue has two candidates. The first is kitbashed from the packs' real recordings: real creaks dropped for a thick
  post or kept for a hinge, real latches, iron and plate for a cast-iron door and a rail, real pages and a belt's
  crackle for paper and straw, real wood knocks for a frame and a brush handle, a real book slapped flat for a gloved
  palm, the crew's own boots (crew_feet) on boards and ballast, a real gasp and sigh for the air in a voice. The second
  is modelled: stick-slip through a body's modes for creaks, struck modes for iron, wood, knuckles and a rail, paper's
  fibres breaking for a tear, friction and rustle for coats, straw and grass.
- The crew's voices are the crew's own (rescue_finds.effort: a glottis through the modelled tract, over the crew's
  modelled breath), so each take is a different crewmate: a man, a woman, a bigger man. A cough is the same throat
  shut and blown open: a glottal burst, a rush of rough air on an open vowel, a little voice in it, quickly gone.
- Every impact, slap and thud keeps its weight in the low mids (the director's squish rule: under 45% of its energy
  below 150 Hz), high-passed at 140-160 Hz; a voice is laid over it whole.
"""

import numpy as np

import dsp
import synth
from dsp import SR, samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes.beasts import main_hit, unit
from recipes.crew_car import lead_in, seg, FLIPS, PLACES, BOOK_CLOSE
from recipes.crew_feet import heel, toe, on_wood, on_ballast, scrape, tup, BOOTS, CRUNCH, GRASS, STONES
from recipes.crew_items import hit_of, CLOTH, SOFT
from recipes.crew_kit import recipe, R, S, K
from recipes.heard_foley import breath as crew_breath
from recipes.k1_asks import lay
from recipes.moose_tells import burst
from recipes.rescue_finds import (effort, let_out, plank, wool, snatch, body, ring, squish, at, VOICES, GASP, SIGH,
                                  GRIPS, CRACKLE, CREAKS, WOOD_L, METAL_H, METAL_M, IRON_DOOR, LATCH)

# ---- Sources --------------------------------------------------------------------------------------------------------------

LOCKS = S("door_05", "door_04")                  # real latches and locks: a bolt drawn, a catch lifted (two clicks each)
PLATE_M, PLATE_L = K("impactPlate_medium"), K("impactPlate_light")
TIN = K("impactTin_medium")
SLAP = R("bookPlace2")[0]                        # a real book slapped down flat: a padded, leathery smack
CRACK = S("switch_01", "switch_02")              # a real switch cracking through the air: a slap's air snapping out
HANDS = R("knifeSlice2")[0]                      # its first instant is two hands slapped together
DROP = R("dropLeather")[0]                       # a real heavy leather drop
TICKS = ["kenney_ui-audio:switch21", "kenney_ui-audio:switch20"]   # small real wooden ticks: a brush handle on a ledge
SOFT_M = K("impactSoft_medium")


def jit(rng, x, semis=0.0, spread=0.4):
    """A part varispeeded `semis` and a hair either way: the same thing done a little differently each take."""
    return dsp.vari(x, semis + rng.uniform(-spread, spread))


def motion(x, rate=8.0):
    """How hard something's moving, from its own loudness: a slow envelope, peak 1."""
    e = lp(np.abs(x), rate, 2)
    return np.clip(e / (np.max(e) + 1e-9), 0, 1).astype(np.float32)


# ---- world-odd.scarecrow --------------------------------------------------------------------------------------------------

WO = "world-odd"
POST = synth.BARK                                # an old weathered pole, thick and dry: a hollow trunk's modes
CROSS = [m * 1.6 for m in synth.WOOD]            # the crosspiece nailed across it: a lighter board


def straw(rng, length, shape, real=True):
    """Straw stuffing and a sacking coat shifting on the frame as it turns: dry stalks crackling and the sacking's coarse
    weave rubbing, swelling with `shape` (per sample, 0-1). `real`: the packs' belt crackle laid grain by grain and a
    real cloth handling slowed for coarse sacking; otherwise a dry rustle of stalks and the weave's stick-slip."""
    n = samples(length)
    sh = dsp.fit(shape, n)
    if real:
        stalks = lay(rng, length, 30 + 220 * sh, 2.0, CRACKLE, (1500, 1500)) * sh
        sack = dsp.fit(lp(dsp.vari(ck.cloth(rng, int(rng.integers(4)), length * 0.6), -5), 2600), n)
        return unit(mix(unit(hp(stalks, 1200)), unit(sack) * 0.4 * (0.3 + 0.7 * sh)))
    stalks = synth.rustle(length, 60 + 900 * sh, rng, f=(2500, 9000), ticks=0.5) * sh
    sack = ck.friction(rng, length, 110, 350, 2800, 0.9, 0.2 + 0.8 * sh)
    return unit(mix(unit(stalks), unit(sack) * 0.45))


def soil(rng, length, shape):
    """The post grinding round in its hole: wood dragged on packed earth (slow, rough stick-slip, dark) and crumbs of it
    ticking down the post."""
    n = samples(length)
    sh = dsp.fit(shape, n)
    drag = ck.friction(rng, length, 70, 160, 1600, 0.95, sh)
    crumbs = dsp.fit(ck.grains(rng, int(14 * length), length, 900, 3500, q=(2, 5), length=(0.004, 0.012), shape=0.0), n)
    return unit(mix(unit(drag), unit(crumbs) * 0.2 * (0.4 + 0.6 * sh)))


SCARE_TAKES = [
    # the turn's length (s), and its catches as (start, length, peak slips a second)
    dict(L=1.3, catches=[(0.0, 0.42, 34), (0.5, 0.36, 40), (0.92, 0.3, 26)]),
    dict(L=1.15, catches=[(0.0, 0.62, 30), (0.78, 0.3, 44)]),
]


@recipe(WO, "scarecrow", "post",
        "A scarecrow's post turning to face the line: a slow dry creak of old wood in the ground, its straw and sacking rustling",
        """From real recordings: a real wooden creak (a hinge's squeak) dropped an octave by tape so its catches come
        slow, and a few semitones more by pitch alone so its squeak sits low, the dry groan of a thick old post twisting
        in its hole, its top rolled off; the post's foot grinding round in the
        packed earth under it (a slow, dark drag and a few crumbs); and its stuffing and coat shifting with each catch (a
        real belt's crackle laid grain by grain for dry straw, a real cloth handling slowed for coarse sacking). One take
        turns in three catches, the other in a long one and a short one. Close, dry, very quiet (built at -26 LUFS).""",
        sources=CREAKS[:2] + CRACKLE + CLOTH, takes=2, lufs=-26)
def scarecrow_post(rng, k):
    L = SCARE_TAKES[k]["L"]
    # an octave down by tape (slow catches), and further by pitch alone, so the squeak's line sits low like a groan
    c = dsp.vari(ck.align(ck.get(CREAKS[(1, 0)[k]])), -12 + rng.uniform(-0.4, 0.4))
    c = dsp.fit(lp(hp(dsp.shift(c, (-4, -6)[k]), 160, 2), 3200, 2), samples(L))
    m = motion(c)
    parts = [(0, unit(c), 0), (0, soil(rng, L, m), -9), (0.02, straw(rng, L, m), -11)]
    return lead_in(ck.place(parts, L + 0.1))


@recipe(WO, "scarecrow", "twist",
        "A scarecrow's post turning, modelled: dry wood stick-slipping in its hole, the crosspiece ticking, straw rustling",
        """Modelled from what turns: the post twisting in its hole, a slow stick-slip (25-45 slips a second at each catch,
        uneven) rung through a thick, weathered pole's low modes, so it creaks dry and close, never a squeal; the
        crosspiece's nail ticking against it as the arms swing round (a lighter board's modes, a few quick slips); the
        foot grinding in packed earth (slow dark friction, crumbs); dry straw and sacking rustling with each catch. The
        same two turns as 'post'. Close, dry, very quiet (built at -26 LUFS).""",
        takes=2, lufs=-26)
def scarecrow_twist(rng, k):
    d = SCARE_TAKES[k]
    L = d["L"]
    n = samples(L)
    creak = np.zeros(n, np.float32)
    for t0, Lc, r in d["catches"]:
        r *= rng.uniform(0.9, 1.1)
        rate = env([(0, 8), (Lc * 0.3, r), (Lc * 0.75, r * 0.8), (Lc, 6)], Lc)
        sh = env([(0, 0), (Lc * 0.15, 1), (Lc * 0.7, 0.7), (Lc, 0)], Lc)
        c = synth.creak(Lc, rate, rng, body=POST, q=10, jitter=0.45, shape=sh, grit=0.4)
        a = samples(t0)
        creak[a:a + len(c)] += c[:n - a] * rng.uniform(0.75, 1.0)
    m = motion(creak)
    t_nail = d["catches"][-1][0] + 0.05
    nail = synth.creak(0.12, 70, rng, body=CROSS, q=14, jitter=0.5, shape=env([(0, 0), (0.02, 1), (0.12, 0)], 0.12))
    parts = [(0, unit(lp(hp(creak, 180, 4), 3500, 2)), 0), (t_nail, unit(hp(nail, 500)), -16), (0, soil(rng, L, m), -9),
             (0.02, straw(rng, L, m, real=False), -12)]
    return lead_in(ck.place(parts, L + 0.1))


# ---- world-odd.letter -----------------------------------------------------------------------------------------------------

LETTER_TAKES = [
    # the door: its lock (clicks), its hinge's squeak (s, 0 for none), the door's knock on its stop (dB, None for none);
    # the tear: its pulls as (start, length) after the letter's out; the unfolding: how many creases snap open
    dict(clicks=1, squeak=0.2, knock=-6, pulls=[(0.0, 0.26)], folds=2),
    dict(clicks=1, squeak=0.0, knock=0, pulls=[(0.0, 0.14), (0.2, 0.16)], folds=1),
    dict(clicks=2, squeak=0.32, knock=None, pulls=[(0.0, 0.2)], folds=2),
]


def postbox_rec(rng, k):
    """A pillar box's cast-iron door opened, from real recordings: the lock's bolt drawn (a real latch's clicks pitched
    down for a heavy lock), the door's hinge squeaking (a real creak's pure squeak, kept short), the door swung back and
    knocking its stop (a real medium iron hit and a plate, pitched down for thick cast iron, choked: it barely rings),
    the hollow box answering. Returns the take and when the door's open."""
    d = LETTER_TAKES[k]
    parts, t = [], 0.0
    for i in range(d["clicks"]):
        parts.append((t, unit(jit(rng, hit_of(LOCKS[(k + i) % 2], i, 0.12), -4)), -4 - 3 * i))
        t += rng.uniform(0.07, 0.1)
    if d["squeak"]:
        Ls = d["squeak"]
        sq = dsp.fit(jit(rng, seg(CREAKS[2], 0.08, 0.24), -1.5), samples(Ls))
        parts.append((t + 0.02, unit(hp(sq, 500)) * env([(0, 0), (0.03, 1), (Ls * 0.6, 0.7), (Ls, 0)], Ls), -15))
        t += Ls * 0.8
    else:
        t += 0.12
    if d["knock"] is not None:
        clank = ck.choke(at(rng, ck.get(METAL_M[k % 5]), -5), 0.008, 0.03)
        plate = ck.choke(at(rng, ck.get(PLATE_M[(k + 2) % 5]), -3), 0.01, 0.03)
        box = ck.hollow(rng, [190, 300, 450], 0.12, q=5)
        parts += [(t, unit(mix(clank, plate * 0.7)), d["knock"]), (t, box, d["knock"] - 12)]
        if k == 1:      # swung wide, it rattles once on its hinge pins
            parts.append((t + 0.06, unit(ck.choke(at(rng, hit_of(LOCKS[1], 1, 0.08), -6), 0.004, 0.015)), -16))
    return squish(ck.place(parts), 150), t


def tear_rec(rng, length):
    """An old envelope torn open along its top, from real recordings: real pages' crisp snap and a real belt's fibres
    crackling, laid grain by grain fast and pitched up for thin dry paper, the rate following the finger pulling
    through."""
    rate = env([(0, 120), (length * 0.25, 380), (length * 0.8, 300), (length, 80)], length)
    y = lay(rng, length, rate, env([(0, 2), (length, 4.5)], length), [FLIPS[0], CRACKLE[1]], (1500, 1800))
    return unit(hp(y, 900) * env([(0, 0.3), (0.02, 1), (length * 0.85, 0.8), (length, 0)], length))


def unfold_rec(rng, k, folds):
    """The letter slid out of its envelope and unfolded, from real recordings: paper sliding on paper (a real page's
    drag), each crease snapping open (a real page's snap) with the sheet swishing over (a real page's sweep, slowed)."""
    parts = [(0, unit(hp(jit(rng, seg(FLIPS[1], 0.1, 0.1), -1), 700)), -6)]
    t = rng.uniform(0.1, 0.14)
    for i in range(folds):
        sw = seg(FLIPS[0], 0.27, 0.22, -2 + rng.uniform(-0.7, 0.7), 0.02, 0.04)
        Lw = len(sw) / SR
        snap_ = seg(FLIPS[0], 0.565, 0.07, rng.uniform(-1.5, 0.5), 0.001, 0.03)
        parts += [(t, unit(hp(sw, 500)) * env([(0, 0), (0.05, 1), (Lw * 0.6, 0.7), (Lw, 0)], Lw), -6),
                  (t + rng.uniform(0.07, 0.1), unit(lp(snap_, 8000)), -1 - 3 * i)]
        t += rng.uniform(0.16, 0.2)
    return ck.place(parts)


def letter(rng, k, door, tear, unfold, pickup):
    """The whole of it: the door, the letter lifted off the box's iron floor, the envelope torn open, the letter
    unfolded. Close and dry."""
    d = LETTER_TAKES[k]
    y, t = door(rng, k)
    t += rng.uniform(0.08, 0.12)
    parts = [(0, y, 0), (t, pickup(rng), -12)]
    t += rng.uniform(0.1, 0.13)
    for t0, Lp in d["pulls"]:
        parts.append((t + t0, tear(rng, Lp), -2))
    t += d["pulls"][-1][0] + d["pulls"][-1][1] + rng.uniform(0.04, 0.07)
    parts.append((t, unfold(rng, k, d["folds"]), -2))
    return lead_in(ck.space(ck.place(parts), 0.1, 0.05, 6000, ((0.003, 0.3), (0.006, 0.2))))


def pickup_rec(rng):
    """The letter lifted off the box's floor: a real page's drag, short."""
    return unit(hp(jit(rng, seg(FLIPS[1], 0.1, 0.07), 1), 900))


@recipe(WO, "letter", "pillar",
        "A letter taken from a pillar box and opened: the iron door unlocked and swung open, the envelope torn, the letter unfolded",
        """From real recordings, close and dry: the box's lock drawn (a real latch's clicks pitched down for a heavy lock),
        the cast-iron door swung open on a squeaking hinge (a real creak's pure squeak, short) and knocking its stop (a
        real iron hit and a plate pitched down for thick cast iron and choked, the hollow box behind it), the letter
        lifted off the iron floor (a real page's drag), the old envelope torn open along its top (real pages' snap and a
        belt's fibres laid grain by grain fast, pitched up for thin dry paper) and the letter slid out and unfolded (paper
        on paper, each crease snapping open as the sheet swishes over). Takes: the door creaking open; flung open with
        a rattle, the envelope torn in two pulls; a stiff lock and a slow creak, the door eased open without a
        knock.""",
        sources=LOCKS + [CREAKS[2]] + METAL_M[:2] + PLATE_M[2:4] + FLIPS[:2] + [CRACKLE[1]], takes=3, lufs=-21)
def letter_pillar(rng, k):
    return letter(rng, k, postbox_rec, tear_rec, unfold_rec, pickup_rec)


def postbox_model(rng, k):
    """The pillar box's door, modelled: the lock's bolt (a small hard tick through a latch's modes), the hinge squeaking
    (stick-slip quick enough to sing, through a pin's small iron modes), the door's knock on its stop (a small
    cast-iron door's own modes, dead) and the hollow box behind it."""
    d = LETTER_TAKES[k]
    parts, t = [], 0.0
    for i in range(d["clicks"]):
        parts += [(t, body(rng, LATCH, 25, 0.02, 0.0008, 9000), -6 - 3 * i),
                  (t + 0.006, body(rng, [m * 0.5 for m in IRON_DOOR[2:]], 12, 0.03, 0.001, 6000), -10 - 3 * i)]
        t += rng.uniform(0.07, 0.1)
    if d["squeak"]:
        Ls = d["squeak"]
        rate = env([(0, 380), (Ls * 0.4, 520), (Ls, 430)], Ls) * rng.uniform(0.9, 1.1)
        sq = synth.creak(Ls, rate, rng, body=[1050, 2200, 3400], q=30, jitter=0.08, grit=0.1,
                         shape=env([(0, 0), (0.03, 1), (Ls * 0.6, 0.7), (Ls, 0)], Ls))
        parts.append((t + 0.02, unit(sq), -16))
        t += Ls * 0.8
    else:
        t += 0.12
    if d["knock"] is not None:
        parts += [(t, body(rng, IRON_DOOR, 12, 0.07, 0.002, 5000), d["knock"]),
                  (t, ck.hollow(rng, [190, 300, 450], 0.12, q=5), d["knock"] - 12)]
        if k == 1:
            parts.append((t + 0.06, body(rng, LATCH, 20, 0.02, 0.0008, 8000), -18))
    return squish(ck.place(parts), 150), t


def tear_model(rng, length):
    """An old envelope torn open, modelled: dry paper's fibres breaking one after another as the tear runs (a crisp tick
    each, a few hundred a second and uneven, quicker as the finger pulls through), the sheet crackling as it buckles,
    the flap fluttering under it."""
    n = samples(length)
    pull = env([(0, 0.3), (min(0.04, length * 0.2), 1.0), (length * 0.7, rng.uniform(0.75, 0.95)), (length, 0.1)], length)
    x = np.zeros(n, np.float32)
    t = 0.0
    while True:
        p = float(pull[min(samples(t), n - 1)])
        t += max(0.2, 1 + 0.5 * rng.standard_normal()) / (520 * (0.3 + 0.9 * p))
        a = samples(t)
        if a >= n:
            break
        x[a] = rng.uniform(0.2, 1.0) * p * (3.0 if rng.random() < 0.05 else 1.0)
    k_ = samples(0.0008)
    tick = (rng.standard_normal(k_) * np.exp(-np.linspace(0, 6, k_))).astype(np.float32)
    fibres = bp(np.convolve(x, tick)[:n].astype(np.float32), 1800, 11000)
    crackle = bp(synth.crackle(length, 90 * pull + 10, rng, size=(0.0003, 0.002), hi=1200), 1200, 8000)
    flutter = synth.rustle(length, 200 * pull + 30, rng, f=(900, 4000), ticks=0.0) * pull
    return unit(mix(unit(fibres), unit(crackle) * 0.45, unit(flutter) * 0.25))


def flick(rng, semis=(-3.0, 1.0)):
    """A crease snapping open, modelled: a crisp burst in paper's band (crew_car's page flick)."""
    s = 2 ** (rng.uniform(*semis) / 12)
    return unit(burst(rng, rng.uniform(0.0015, 0.004), 1800 * s, 9000, attack=0.15))


def unfold_model(rng, k, folds):
    """The letter slid out and unfolded, modelled: paper sliding on paper (fine bright stick-slip), each crease opening
    with a little crackle of its fibres and a flick as it snaps flat, the sheet's air swishing over."""
    Ls = 0.09
    slide = ck.friction(rng, Ls, rng.uniform(550, 750), 1500, 8000, 0.5, env([(0, 0.2), (Ls * 0.7, 1), (Ls, 0)], Ls))
    parts = [(0, slide, -7)]
    t = rng.uniform(0.1, 0.14)
    for i in range(folds):
        Lc = rng.uniform(0.06, 0.09)
        rate = env([(0, 40), (Lc * 0.6, 260), (Lc, 30)], Lc)
        cr = bp(synth.crackle(Lc, rate, rng, size=(0.0003, 0.0025), hi=900), 900, 7000)
        Lw = rng.uniform(0.14, 0.18)
        parts += [(t, unit(cr), -8), (t + 0.02, unit(ck.whoosh(rng, Lw, 700, 3200, 0.5)), -10),
                  (t + Lc + rng.uniform(0.01, 0.03), flick(rng, (-2.0, 1.0)), -1 - 3 * i)]
        t += rng.uniform(0.16, 0.2)
    return ck.place(parts)


def pickup_model(rng):
    """The letter lifted off the box's floor, modelled: paper slid a few centimetres on iron."""
    L = 0.07
    return ck.friction(rng, L, rng.uniform(450, 650), 1500, 9000, 0.6, env([(0, 0.3), (L * 0.5, 1), (L, 0)], L))


@recipe(WO, "letter", "paper",
        "A letter taken from a pillar box and opened, modelled: the iron door, old paper tearing, the letter unfolded",
        """Modelled, close and dry: the lock's bolt drawn (a small hard tick through a latch's modes), the hinge squeaking
        (stick-slip quick enough to sing, through a pin's small iron modes), the cast-iron door knocking its stop (a
        small iron door's own dead modes, the hollow box behind it), the letter slid off the iron floor, the old envelope
        torn open (dry paper's fibres breaking one after another, a few hundred a second, crackling as it buckles, the
        flap fluttering) and the letter unfolded (paper sliding on paper, each crease crackling open and flicking
        flat). The same three takes as 'pillar'.""",
        takes=3, lufs=-21)
def letter_paper(rng, k):
    return letter(rng, k, postbox_model, tear_model, unfold_model, pickup_model)


# ---- world-odd.painting ---------------------------------------------------------------------------------------------------

FRAME = [760, 1380, 2150, 3300]                  # a small picture frame's moulding: light, thin wood
CANVAS = [230, 380, 560]                         # the canvas stretched over it: a soft papery drum
HANDLE = [1700, 2900, 4600]                      # a brush's thin wooden handle
LEDGE = [420, 880, 1500]                         # the easel's wooden ledge it's tapped on


def brush_taps(rng, n, tap):
    """A brush tapped clean on the easel's ledge `n` times, quick and lighter each time (`tap(i)` makes one)."""
    parts, t, g = [], 0.0, 0.0
    for i in range(n):
        parts.append((t, tap(i), g))
        t += rng.uniform(0.1, 0.13) * (0.92 ** i)
        g -= rng.uniform(2, 4)
    return ck.place(parts)


@recipe(WO, "painting", "frame",
        "Dave's small painting set down on the grass at your feet: the frame's soft knock, then his brush tapped clean on the easel",
        """From real recordings: a small framed canvas set down on the grass (the packs' real grass crushed under it, its
        low wobble taken out), the frame's soft knock as it settles (a real light wood knock, choked by the grass and
        darkened), the canvas's flat papery pat under it (a real book set down, softened); then a beat later Dave's brush
        tapped clean on the easel's wooden ledge, two or three quick light taps (small real wooden ticks). One take is set
        down flat; the other touches on a corner first and is laid down.""",
        sources=GRASS[1:3] + WOOD_L[:2] + WOOD_L[3:4] + PLACES[:1] + TICKS, takes=2, lufs=-21)
def painting_frame(rng, k):
    corner = k == 1
    grass = unit(hp(ck.align(ck.get(GRASS[1 + k]))[:samples(0.18)], 350, 4))
    knock = unit(lp(ck.choke(at(rng, ck.get(WOOD_L[k]), 3), 0.006, 0.02), 3500))
    pat = unit(lp(ck.choke(at(rng, hit_of(PLACES[0], 0, 0.1), 2), 0.012, 0.02), 2500))
    t = 0.0
    parts = []
    if corner:
        parts.append((0, unit(lp(ck.choke(at(rng, ck.get(WOOD_L[k + 2]), 5), 0.004, 0.012), 4000)), -8))
        t = rng.uniform(0.07, 0.1)
    parts += [(t, grass, -8), (t + 0.004, knock, -2), (t + 0.008, pat, -6)]
    tap = lambda i: unit(jit(rng, hit_of(TICKS[(k + i) % 2], 0, 0.09), 3 + i * 0.5))
    tb = t + rng.uniform(0.6, 0.7)
    parts.append((tb, brush_taps(rng, 2 + k, tap), -4))
    return lead_in(squish(ck.place(parts), 150))


@recipe(WO, "painting", "canvas",
        "Dave's small painting set down, modelled: grass crushed, the frame's light wooden knock, brush taps on the easel",
        """Modelled: the grass crushed under a small framed canvas (a dry rustle of blades and a short dead tup of soft
        ground), the frame's soft knock (a light moulding's modes, damped at once by the grass), the canvas's papery drum
        under it (a few broad low modes), then the brush's thin wooden handle tapped on the easel's ledge two or three
        times (the handle's bright modes over the ledge's lower knock). The same two takes as 'frame'.""",
        takes=2, lufs=-21)
def painting_canvas(rng, k):
    corner = k == 1
    Lg = 0.12
    grass = synth.rustle(Lg, 1100, rng, f=(2500, 9000), ticks=0.4) * env([(0, 1), (Lg, 0)], Lg)
    t = 0.0
    parts = []
    if corner:
        parts.append((0, body(rng, [m * 1.15 for m in FRAME], 10, 0.03, 0.0015, 5000), -9))
        t = rng.uniform(0.07, 0.1)
    parts += [(t, unit(grass), -16), (t, tup(rng, 0.6, "toe", (200, 280)), -12),
              (t + 0.003, ck.choke(body(rng, FRAME, 8, 0.06, 0.003, 3000), 0.006, 0.015), -2),
              (t + 0.006, ck.hollow(rng, CANVAS, 0.07, q=4, hit=0.006), -8)]
    tap = lambda i: mix(body(rng, HANDLE, 14, 0.025, 0.0008, 9000), body(rng, LEDGE, 9, 0.04, 0.0012, 5000) * 0.6)
    parts.append((t + rng.uniform(0.6, 0.7), brush_taps(rng, 2 + k, tap), -4))
    return lead_in(squish(ck.place(parts), 150))


# ---- world-odd.sooted -----------------------------------------------------------------------------------------------------

def cough(rng, voice, force=1.0, real=None, length=None):
    """One cough, in a crewmate's own throat: the glottis shut and blown open (a sharp burst), a rush of rough air on an
    open 'ah' with a little strained voice in its start (pitched up by the force of it, rattling with the dust), falling
    away to breath."""
    L = length or rng.uniform(0.2, 0.25)
    air = effort(rng, voice, L, [(0, 1.5 + 0.2 * force), (0.03, 1.35), (L * 0.5, 1.08), (L, 0.9)],
                 [(0, "a"), (0.05, "a"), (L * 0.6, "o"), (L, "h")],
                 [(0, 0), (0.004, 1), (0.03, 0.7), (L * 0.4, 0.3), (L, 0)],
                 press=0.35, rough=0.6, fry=0.35, voiced=0.38, real=real)
    pop = burst(rng, 0.005, 400, 6000, attack=0.05)
    return unit(mix(unit(air), unit(pop) * 0.5 * force))


def clear(rng, voice, real=None):
    """A throat cleared ('hhrm'): a rough, closed-mouth rasp, low, pushed and let go."""
    L = rng.uniform(0.22, 0.28)
    return effort(rng, voice, L, [(0, 1.0), (L * 0.4, 1.15), (L, 0.85)], [(0, "h"), (0.04, "m"), (L * 0.7, "u"), (L, "m")],
                  [(0, 0), (0.03, 1), (L * 0.6, 0.7), (L, 0)], press=0.4, rough=0.75, fry=0.5, voiced=0.45, real=real)


def spit(rng, wet=1.0):
    """Spitting the grit out ('ptuh'), modelled: the lips' soft pop, the tongue's tick, a short spray of air through
    pursed lips with a little wet crackle in it."""
    L = rng.uniform(0.09, 0.12)
    lips = burst(rng, 0.003, 300, 2500, attack=0.1)
    tongue = burst(rng, 0.0015, 3000, 8000, attack=0.05)
    air = bp(synth.noise(L, rng), 1500, 8000) * env([(0, 0), (0.005, 1), (L * 0.4, 0.5), (L, 0)], L)
    drops = bp(synth.crackle(L, 120 * wet, rng, size=(0.0002, 0.001), hi=2500), 2000, 9000)
    return ck.place([(0, unit(lips), -4), (0.008, unit(tongue), -12), (0.006, unit(air), 0), (0.012, unit(drops), -14)])


SOOT_TAKES = [
    # coughs as (gap before, force); then a breath caught or the throat cleared; the pause before the spit
    dict(coughs=[(0.08, 1.0), (0.3, 0.85), (0.28, 0.6)], spit=0.16),
    dict(coughs=[(0.06, 1.0), (0.3, 0.7)], gasp=True, spit=0.2),
    dict(coughs=[(0.1, 1.0), (0.32, 0.75)], clear=True, spit=0.1),
]


def sooted(rng, k, real, puff, grit):
    """Out from under the chute: the soot coming off them, grit pattering down, two or three coughs (take k's crewmate),
    a breath caught or a throat cleared, and the grit spat out."""
    d = SOOT_TAKES[k]
    v = VOICES[k]
    voices, t = [], 0.0
    for gap, force in d["coughs"]:
        t += gap
        voices.append((t, cough(rng, v, force, real), -2 - 4 * (1 - force)))
    t += 0.26
    if d.get("gasp"):
        Lg = 0.14
        voices.append((t, unit(crew_breath(rng, Lg, [(0, "h"), (Lg, "u")], [(0, 0), (Lg * 0.7, 1), (Lg, 0)], tract=v[0],
                                           teeth=0.3)), -14))
        t += Lg + 0.06
    if d.get("clear"):
        voices.append((t, clear(rng, v, real), -7))
        t += 0.3
    t += d["spit"]
    voices.append((t, spit(rng), -6))
    parts = [(0, puff(rng, k), -8), (0.01, grit(rng, k, t + 0.15), -8)]
    return lead_in(mix(hp(ck.place(parts), 150), ck.place(voices)))


def puff_rec(rng, k):
    """Soot shaken off a coat, from real recordings: a real cloth handling (the coat shrugged), darkened, and the dust
    off it."""
    return unit(mix(unit(lp(ck.cloth(rng, k, 0.25), 3000)), unit(burst(rng, 0.3, 250, 1800, attack=0.05)) * 0.4))


def patter(rng, length, grain):
    """Grit falling off a coat: grains (`grain(rng)` makes one) striking at first thick and fast, then thinning out."""
    b = Bus(length + 0.05)
    t = 0.0
    while t < length:
        b.at(t, grain(rng), 20 * np.log10(rng.uniform(0.15, 1.0)) - 10 * t / length)
        t += rng.exponential(1 / (70 * np.exp(-3 * t / length) + 4))
    return unit(hp(b.x, 1500))


def stone_hits():
    """Every small hit in the packs' real stones, cut out one by one."""
    out = []
    for key in STONES:
        x = ck.get(key)
        out += [ck.cut(x, a - samples(0.001), a + samples(0.03), 0.0005, 0.015)
                for a, _ in ck.hits(x, floor_db=-30, gap=0.01)]
    return out


def grit_rec(rng, k, length):
    """Coal grit pattering off them onto the ballast, from real recordings: the packs' real stones cut into their
    small hits and pitched up for grit, falling thick and then thinning out."""
    hits = stone_hits()
    return patter(rng, length, lambda r: ck.norm(dsp.vari(hits[int(r.integers(len(hits)))], r.uniform(4, 9))))


@recipe(WO, "sooted", "dust",
        "Out from under the coaling chute black with soot: grit pattering off, two or three dust-choked coughs and a spit",
        """The soot coming off them (a real cloth handling, the coat shrugged, darkened, and its dust) and coal grit
        pattering down off them (real stones' small hits laid one by one, pitched up for grit, thinning out), then two or
        three coughs in the crewmate's own throat (the crew's modelled voice: the glottis shut and blown open, a rush of
        rough air on an open 'ah' with a little strained voice in it, rattling with the dust, its air drawn from a real
        gasp), a breath caught or a throat cleared, and the grit spat out (lips, tongue and a short wet spray,
        modelled). Each take a different crewmate: a man, a woman, a bigger man.""",
        sources=CLOTH[:3] + STONES + [GASP], takes=3, lufs=-19)
def sooted_dust(rng, k):
    return sooted(rng, k, GASP, puff_rec, grit_rec)


def puff_model(rng, k):
    """Soot off a coat, modelled: the heavy cloth shrugged (wool's stick-slip) and a soft dark puff of dust."""
    return unit(mix(wool(rng, 0.25, 1.2), unit(burst(rng, 0.3, 250, 1800, attack=0.05)) * 0.5))


def grit_model(rng, k, length):
    """Coal grit pattering down, modelled: hard little grains clicking on the coat and the ballast, thinning out."""
    return patter(rng, length,
                  lambda r: ck.tick(r, r.uniform(2000, 7000), q=r.uniform(3, 9), length=r.uniform(0.003, 0.01)))


@recipe(WO, "sooted", "lungs",
        "Out from under the chute black with soot, modelled: dust puffing off, grit pattering, coughing and spitting",
        """Modelled through: a soft dark puff of dust off a heavy coat as it's shrugged (wool's stick-slip and a band of
        dark air), coal grit pattering down on the coat and the ballast (hard little grains, many at first, thinning),
        two or three coughs in the crewmate's own modelled throat on its own modelled breath (shut, blown open, a rush of
        rough air with a little voice in it), a breath caught or a throat cleared, and the grit spat out. The same three
        takes and crewmates as 'dust'.""",
        takes=3, lufs=-19)
def sooted_lungs(rng, k):
    return sooted(rng, k, None, puff_model, grit_model)


# ---- world-odd.lurch ------------------------------------------------------------------------------------------------------

RAIL = [640, 1210, 1790, 2680, 3920]             # a rail's head ringing when struck: bright, close, slow to die
RAIL_G = [1.0, 0.8, 0.6, 0.4, 0.25]
RAIL_D = [0.32, 0.26, 0.2, 0.14, 0.09]


def startle(rng, voice, real=None):
    """A startled grunt ('hoh!'): air shoved out by the jolt through a throat that tightens on it, the pitch jumped up and
    falling, short and cut off."""
    L = rng.uniform(0.15, 0.2)
    return effort(rng, voice, L, [(0, 1.6), (0.03, 1.8), (L * 0.6, 1.4), (L, 1.1)],
                  [(0, "h"), (0.015, "o"), (L * 0.6, "a"), (L, "h")],
                  [(0, 0), (0.01, 1), (L * 0.5, 0.7), (L * 0.8, 0.2), (L, 0)],
                  press=0.45, rough=0.25, fry=0.1, voiced=0.7, real=real)


SCRABBLE = [
    # boots scrabbling on the ballast after the jolt: (time after the slam, heel or toe, force), and a scrape's (time, s)
    dict(steps=[(0.16, "toe", 1.0), (0.3, "heel", 1.2), (0.42, "toe", 0.8), (0.6, "toe", 0.9), (0.78, "heel", 0.7)],
         scrapes=[(0.17, 0.12), (0.61, 0.2)]),
    dict(steps=[(0.13, "toe", 1.1), (0.22, "toe", 0.9), (0.36, "heel", 1.2), (0.47, "toe", 0.7), (0.7, "heel", 0.8)],
         scrapes=[(0.22, 0.14), (0.71, 0.16)]),
    dict(steps=[(0.18, "heel", 1.2), (0.34, "toe", 1.0), (0.5, "heel", 1.3), (0.68, "toe", 0.7), (0.86, "toe", 0.5)],
         scrapes=[(0.35, 0.12), (0.69, 0.18)]),
]


def lurch(rng, k, slam, sole, step, real):
    """The switch thrown under them: the blades slamming over under their boots (`slam`), the soles jerked across the
    steel, their startled grunt (take k's crewmate), and their boots scrabbling on the ballast as they're thrown off
    it."""
    d = SCRABBLE[k]
    parts = [(0, slam(rng, k), 0), (0.004, sole(rng), -10)]
    for i, (t, part, s) in enumerate(d["steps"]):
        parts.append((t + rng.uniform(-0.01, 0.01), step(rng, k + i, part, s), -8))
    for t, Ls in d["scrapes"]:
        parts.append((t, scrape(rng, "ballast", Ls, 0.8), -13))
    voice = [(rng.uniform(0.05, 0.08), startle(rng, VOICES[k], real), -8)]
    return lead_in(mix(squish(ck.place(parts), 150), ck.place(voice)))


def slam_rec(rng, k):
    """The blades slamming over, from real iron: a heavy iron hit and a medium plate pitched down and choked (the boots
    are on the blade: it clanks, it can't sing) with a real blow on a steel sheet under them, and the rail's head
    ringing after it (a real heavy iron hit, pitched down, let ring)."""
    clank = ck.choke(at(rng, ck.get(METAL_H[k % 5]), -3), 0.01, 0.04)
    plate = ck.choke(at(rng, ck.get(PLATE_M[(k + 1) % 5]), -2), 0.02, 0.05)
    blow = ck.choke(ck.norm(main_hit("sfx_100_v2:metal_hit_01", 0.3)), 0.03, 0.06)
    rail = at(rng, ck.get(METAL_H[(k + 1) % 5]), -3)
    return unit(mix(unit(clank), unit(plate) * 0.8, unit(blow) * 0.6, unit(hp(rail, 400)) * 0.5))


def sole_rec(rng):
    """The boots jerked across the blade's steel: a short bright scrape of soles on iron."""
    L = 0.06
    return ck.friction(rng, L, 260, 800, 6000, 0.5, env([(0, 1), (L, 0)], L))


def step_rec(rng, take, part, s):
    """A boot coming down on the ballast: the crew's own heel or toe (crew_feet) on its real gravel crunch."""
    b = heel(rng, take) if part == "heel" else toe(rng, take)
    return on_ballast(rng, b, s, part, take)


@recipe(WO, "lurch", "rail",
        "A switch thrown under a crewmate on its blades: the iron blades slam over, a startled grunt, boots scrabbling on the ballast",
        """The point blades slamming over under their boots (a real heavy iron hit and a medium plate pitched down and
        choked, so it clanks dead under the boots, a real blow on a steel sheet under them, and the rail's head ringing
        after it, a real iron hit pitched down and let ring), the soles jerked across the steel (a short bright
        scrape), a startled grunt (the crew's modelled throat, jumped up in pitch and cut off, its air from a real
        gasp), and their boots scrabbling on the ballast as they're thrown off (the crew's own boots on real gravel
        crunch, a couple of dragging scrapes). Each take a different crewmate: a man, a woman, a bigger man.""",
        sources=METAL_H[:4] + PLATE_M[1:4] + ["sfx_100_v2:metal_hit_01", GASP] + BOOTS + CRUNCH,
        takes=3, lufs=-19)
def lurch_rail(rng, k):
    return lurch(rng, k, slam_rec, sole_rec, step_rec, GASP)


def slam_model(rng, k):
    """The blades slamming over, modelled: a dull, heavy blow through a big iron part's modes (dead: the boots are on
    it) and the rail's head ringing bright after it, each mode a slightly split pair."""
    clank = body(rng, [m * rng.uniform(0.95, 1.05) for m in synth.IRON[1:]], 7, 0.15, 0.004, 3500)
    rail = ring(rng, [f * rng.uniform(0.97, 1.03) for f in RAIL], RAIL_G, RAIL_D, 0.6, 3.0)
    crack = hp(burst(rng, 0.003, attack=0.05), 1500)
    return unit(mix(unit(clank), rail * 0.3, unit(crack) * 0.25))


def step_model(rng, take, part, s):
    """A boot on the ballast, modelled: the heel's dead knock, rocks grinding and knocking under it, its short tup."""
    g = ck.grains(rng, int(22 * s), 0.12, 600, 3800, q=(3, 10), length=(0.006, 0.02))
    knock = ck.hollow(rng, [rng.uniform(380, 520), rng.uniform(900, 1300)], 0.04, q=2.5, hit=0.002)
    knock = ck.norm(ck.choke(knock, 0.004, 0.008))
    return mix(ck.norm(g) * 0.7 * s, knock * (0.35 if part == "toe" else 0.55) * s, tup(rng, s, part, (140, 200)) * 0.4)


def sole_model(rng):
    """The boots jerked across the blade's steel, modelled: a short bright stick-slip scrape."""
    return ck.friction(rng, 0.06, 300, 900, 6500, 0.4, env([(0, 1), (0.06, 0)], 0.06))


@recipe(WO, "lurch", "points",
        "A switch thrown under a crewmate, modelled: a heavy iron blow and the rail ringing, a startled 'hoh!', boots scrabbling",
        """Modelled: the blades slamming over under their boots (a dull heavy blow through a big iron part's modes, dead
        under the boots, and the rail's head ringing bright after it), the soles jerked across the steel (a short
        scrape), a startled grunt (the crew's modelled throat on its own modelled breath, jumped up in pitch and cut
        short), and their boots scrabbling on the ballast (heels' dead knocks, rocks grinding and knocking, dragged
        scrapes). The same three takes and crewmates as 'rail'.""",
        takes=3, lufs=-19)
def lurch_points(rng, k):
    return lurch(rng, k, slam_model, sole_model, step_model, None)


# ---- crew-emotes.high-five ------------------------------------------------------------------------------------------------

CE = "crew-emotes"


def whoop(rng, voice, real=None):
    """A little whoop of breath ('hoo!'): a pleased breath out with a little voice in it, rising and let go."""
    L = rng.uniform(0.18, 0.24)
    return effort(rng, voice, L, [(0, 1.2), (L * 0.55, 1.65), (L, 1.4)], [(0, "h"), (0.03, "u"), (L * 0.6, "o"), (L, "h")],
                  [(0, 0), (0.03, 0.7), (L * 0.5, 1), (L, 0)], press=0.6, rough=0.05, fry=0.0, voiced=0.45, real=real)


def emote(parts, voices=(), f=150):
    """An emote: what's struck or rubbed with the squish under `f` taken out, the voice laid over it whole, all of it
    close with a breath of the car's walls."""
    y = squish(ck.place(parts), f)
    if voices:
        y = mix(y, ck.place(list(voices)))
    return lead_in(dsp.room(y, "car", wet=0.06, rng=np.random.default_rng(11)))


@recipe(CE, "high-five", "leather",
        "Two gloved hands meeting in a high five: a solid leather slap and a little whoop of breath",
        """Two leather work gloves slapped together: a real book slapped down flat (the packs' nearest thing to a padded
        leather palm, its flat smack; another book's flatter thunk in the second take, pitched down for the bigger man's
        hands in the third), the first
        instant of two real hands slapping and a real switch's crack for the air snapped out between the palms, both
        darkened for the gloves, the gloves' leather creaking as they part (a real glove taking hold), and a little
        whoop of breath (the crew's modelled throat, rising and let go, through a real gasp's air). Each take a
        different crewmate whooping: a man, a woman, a bigger man.""",
        sources=[SLAP, PLACES[0], HANDS] + CRACK + GRIPS[:1] + [GASP], takes=3, lufs=-19)
def high_five_leather(rng, k):
    key, i, semis = [(SLAP, 1, 0.0), (PLACES[0], 0, 1.5), (SLAP, 1, -1.5)][k]
    smack = unit(ck.choke(at(rng, hit_of(key, i, 0.1), semis), 0.03, 0.03))
    hands = unit(lp(ck.choke(hit_of(HANDS, 0, 0.05), 0.004, 0.01), 4500))
    snap_ = unit(lp(ck.choke(at(rng, hit_of(CRACK[k % 2], 0, 0.05), -2), 0.003, 0.008), 5000))
    creak = unit(hp(hit_of(GRIPS[0], 0, 0.1), 300))
    parts = [(0, smack, 0), (0.0005, hands, -6), (0, snap_, -9), (0.03, creak, -20)]
    return emote(parts, [(rng.uniform(0.05, 0.08), whoop(rng, VOICES[k], GASP), -9)])


def palms(rng, size=1.0):
    """Two gloved palms meeting flat, modelled: the air squeezed out between them in two or three bursts a few ms apart,
    rung by the hollow between the cupped palms (lower than bare hands': gloves make a bigger, softer cup), the
    leather's dull slap and a tick of the stiff hide, the hands' own weight behind it."""
    L = 0.1
    n = samples(L)
    x = np.zeros(n, np.float32)
    t = 0.0
    for _ in range(int(rng.integers(2, 4))):
        a, k_ = samples(t), samples(rng.uniform(0.0012, 0.0025))
        x[a:a + k_] += rng.standard_normal(k_) * rng.uniform(0.5, 1.0) * np.hanning(k_)
        t += rng.uniform(0.0015, 0.0035)
    cup = dsp.resonate(x, [rng.uniform(600, 820) / size, rng.uniform(1350, 1700) / size], q=4, gains=[1, 0.4])
    slap_ = burst(rng, 0.03, 250, 3000, attack=0.05)
    hide = burst(rng, 0.0015, 2500, 7000, attack=0.05)
    hands = ck.choke(ck.hollow(rng, [220, 360, 540], 0.08, q=3, hit=0.004), 0.012, 0.02)
    y = mix(unit(cup), unit(slap_) * 0.8, unit(hide) * 0.18, hands * 0.5) * env([(0, 1), (L, 0)], L, "exp")
    return unit(lp(y, 5000))


@recipe(CE, "high-five", "palms",
        "A gloved high five, modelled: air clapped out between two leather palms, the leather's slap, a little whoop",
        """Modelled from what two gloved hands do: the air squeezed out between the palms in two or three bursts a few
        milliseconds apart, rung by the hollow of the cupped gloves (lower and softer than bare hands), the leather's
        dull slap and a tick of its stiff hide, the gloves creaking as they part (a short stick-slip of leather), and a
        little whoop of breath (the crew's modelled throat on its own modelled breath). Each take a different crewmate.""",
        takes=3, lufs=-19)
def high_five_palms(rng, k):
    Lc = 0.05
    creak = ck.friction(rng, Lc, 220, 500, 3500, 0.6, env([(0, 0), (0.01, 1), (Lc, 0)], Lc))
    parts = [(0, palms(rng, rng.uniform(0.95, 1.1)), 0), (0.025, creak, -18)]
    return emote(parts, [(rng.uniform(0.05, 0.08), whoop(rng, VOICES[k]), -9)])


# ---- crew-emotes.fist-bump ------------------------------------------------------------------------------------------------

KNUCKLES = [270, 520, 860, 1350]                 # a gloved fist's knuckles: bone under leather and padding, dull


# Each take its own pair of fists: the knock (a real recording's hit, pitched) and the leather on it.
BUMPS = [(BOOK_CLOSE, 0, 2.5, DROP), (PLACES[0], 1, 3.0, GRIPS[1]), (BOOK_CLOSE, 0, -0.5, SOFT_M[2])]


@recipe(CE, "fist-bump", "gloves",
        "Two gloved fists bumping: a dull, soft knuckle knock, leather on leather",
        """Two fists in leather work gloves knocked together: the fists clenching first (a real glove's leather creaking as
        it takes hold), then the knock, choked short and its boom taken out, dull and soft with no crack: a real book
        shut (its cover's soft slap, a 260 Hz body) pitched up a little for two fists, or a real book set down (its first
        thunk), or the book shut pitched down for the bigger man's fists; with leather meeting leather on it (a real heavy
        leather drop's first instant, a glove's leather, or a real soft hit's skin).""",
        sources=[BOOK_CLOSE, PLACES[0], DROP, GRIPS[1], SOFT_M[2], GRIPS[0]], takes=3, lufs=-22)
def fist_bump_gloves(rng, k):
    key, i, semis, skin = BUMPS[k]
    knock = unit(ck.choke(at(rng, hit_of(key, i, 0.12), semis), 0.025, 0.035))
    leather = unit(ck.choke(at(rng, main_hit(skin, 0.1), rng.uniform(1, 3)), 0.01, 0.02))
    clench = dsp.shaped(unit(hp(hit_of(GRIPS[0], 0, 0.1), 300)), [(0, 0), (0.02, 1), (0.1, 0)])
    t = rng.uniform(0.06, 0.09)
    parts = [(0, clench, -16), (t, knock, 0), (t + 0.001, leather, -6)]
    return emote(parts)


@recipe(CE, "fist-bump", "knuckles",
        "Two gloved fists bumping, modelled: knuckles under leather knocked together, dull and soft",
        """Modelled: the gloves' leather creaking as the fists clench (a short stick-slip), then the knuckles meeting
        through leather and padding (a soft, wide contact rung through a fist's few dull, low-q modes, 270 Hz-1.4 kHz) and
        the leather's soft pat over it; high-passed at 150 Hz so it stays a knock, not a thud. The third take is the
        bigger man's fists, a little lower.""",
        takes=3, lufs=-22)
def fist_bump_knuckles(rng, k):
    Lc = 0.07
    clench = ck.friction(rng, Lc, 180, 450, 3000, 0.7, env([(0, 0), (0.02, 1), (Lc, 0)], Lc))
    size = (1.0, 1.15, 0.85)[k]          # two pairs of fists, then the bigger man's
    knock = body(rng, [m * size * rng.uniform(0.94, 1.06) for m in KNUCKLES], 4, 0.12, 0.006, 1800)
    pat = burst(rng, 0.01, 300, 2500, attack=0.15)
    t = rng.uniform(0.06, 0.09)
    parts = [(0, clench, -18), (t, knock, 0), (t, unit(pat), -6)]
    return emote(parts)


# ---- crew-emotes.sit ------------------------------------------------------------------------------------------------------

# Take 0 sits on a car roof's edge (tin), take 1 on an open door's sill (wood), take 2 on a running board's iron step.
SEATS = ("roof", "sill", "board")
SEAT_HOW = """Take 0 on a car roof's edge (the tin), take 1 on an open door's sill (wood), take 2 on a running board's
iron step; each a different crewmate breathing out: a man, a woman, a bigger man."""


def sit(rng, k, coat, seat, side, sigh):
    """Sitting down on an edge, legs over: the coat bunching as they lower, the weight coming onto the edge (`seat`),
    the coat settling on it, one boot knocking the car's side (`side`) and a breath let out."""
    t = rng.uniform(0.16, 0.22)
    b = t + rng.uniform(0.16, 0.22)
    parts = [(0, coat(rng, k, t + 0.08), -6), (t, seat(rng, k), 0), (t + 0.01, coat(rng, k + 1, 0.22), -10),
             (b, side(rng, k), -6)]
    return emote(parts, [(t + rng.uniform(0.05, 0.09), sigh(rng, VOICES[k]), -6)])


def coat_rec(rng, k, length):
    """The coat bunching or settling: a real cloth handling, its top rolled off for heavy wool."""
    return unit(lp(ck.cloth(rng, k, length), 4500))


def seat_rec(rng, k):
    """The weight onto the edge, from real recordings: a real soft heavy hit for the body, and the edge itself: the
    roof's tin (a real tin knock and a light plate, choked and dull under a coat), the sill's wood (a real wood knock),
    the running board's iron (a real light plate, choked)."""
    soft = unit(squish(hit_of(SOFT[k], 0, 0.2), 160))
    if SEATS[k] == "roof":
        tin = ck.choke(at(rng, ck.get(TIN[k]), -4), 0.008, 0.03)
        edge = mix(unit(tin), unit(ck.choke(at(rng, ck.get(PLATE_L[k]), -2), 0.01, 0.03)) * 0.6)
    elif SEATS[k] == "sill":
        edge = ck.choke(at(rng, ck.get(WOOD_L[(k + 2) % 5]), -2), 0.01, 0.03)
    else:
        edge = ck.choke(at(rng, ck.get(PLATE_L[(k + 2) % 5]), -3), 0.006, 0.025)
    return unit(mix(soft, unit(lp(edge, 4000)) * 0.6))


def side_rec(rng, k):
    """A boot heel knocking the car's wooden side once: the crew's own heel on boards with the side's hollow."""
    return unit(on_wood(rng, heel(rng, k + 1), 0.8, "heel"))


def sigh_rec(rng, voice):
    """The breath let out once they're sat: the crew's modelled breath through a real sigh."""
    return let_out(rng, voice, SIGH)


@recipe(CE, "sit", "coat",
        "Sitting down on an edge, legs over: the coat settling on tin or wood, a boot knocking the car's side, a breath let out",
        """From real recordings: the coat bunching as they lower themselves (a real cloth handling), the weight coming onto
        the edge (a real soft heavy hit, the boom taken out, and the edge itself: a real tin knock and a light plate for
        the roof's tin, a real wood knock for the sill, a real light plate for the iron step, all dull under a coat), the
        coat settling on it, one boot heel knocking the car's wooden side as the legs swing over (the crew's own heel on
        boards), and a breath let out (the crew's modelled breath with a little voice in it, through a real sigh). """
        + SEAT_HOW,
        sources=CLOTH + SOFT[:3] + TIN[:1] + PLATE_L[:1] + PLATE_L[4:5] + WOOD_L[3:4] + BOOTS + [SIGH],
        takes=3, lufs=-19)
def sit_coat(rng, k):
    return sit(rng, k, coat_rec, seat_rec, side_rec, sigh_rec)


TIN_M = [m * 0.9 for m in synth.TIN]
GRATE = [460, 1020, 1750, 2600]                  # a running board's iron step: small, stiff, damped by the weight on it


def seat_model(rng, k):
    """The weight onto the edge, modelled: a soft wide blow (a body in a coat) through the edge's own modes, dull: the
    roof's tin sheet, the sill's plank, the iron step."""
    soft = ck.choke(ck.hollow(rng, [170, 260, 380], 0.12, q=2, hit=0.01), 0.02, 0.04)
    if SEATS[k] == "roof":
        edge = body(rng, TIN_M, 8, 0.08, 0.006, 2500)
    elif SEATS[k] == "sill":
        edge = plank(rng, 0.8, False, 1.1)
    else:
        edge = body(rng, GRATE, 10, 0.06, 0.004, 3000)
    return unit(mix(unit(soft), unit(edge) * 0.7))


def coat_model(rng, k, length):
    """The coat bunching or settling, modelled: heavy wool's stick-slip and crackle."""
    return wool(rng, length, 1.1)


def side_model(rng, k):
    """A heel knocking the car's wooden side once, modelled: a plank's modes over the car's hollow."""
    return plank(rng, 0.8, False, 0.95)


def sigh_model(rng, voice):
    """The breath let out, modelled: the crew's own breath, a little voice at its start."""
    return let_out(rng, voice)


@recipe(CE, "sit", "wool",
        "Sitting down on an edge, modelled: the coat's wool bunching and settling, the weight on the edge, a heel on the side, a breath",
        """Modelled: heavy wool bunching as they lower themselves and settling as they sit (wool's stick-slip and crackle),
        the weight coming onto the edge (a soft, wide blow through the edge's own modes: the roof's tin sheet, the sill's
        plank, the iron step, dull under a coat), one heel knocking the car's wooden side (a plank's modes over the car's
        hollow), and a breath let out (the crew's modelled breath, a little voice at its start). """ + SEAT_HOW,
        takes=3, lufs=-19)
def sit_wool(rng, k):
    return sit(rng, k, coat_model, seat_model, side_model, sigh_model)


# ---- crew-emotes.lean -----------------------------------------------------------------------------------------------------

# Take 0 leans on a car's wooden door frame, take 1 on the car's grab iron, take 2 on the cab's iron doorway.
FRAMES = ("door", "grab", "cab")
LEAN_HOW = """Take 0 a car's wooden door frame, take 1 the grab iron beside the door, take 2 the cab's iron doorway."""


def lean(rng, k, grip, frame, creak, brush):
    """Leaning out of a doorway: the gloved hand taking hold of the frame (`grip` and the frame's knock under it), the
    frame's small creak as the weight goes on it, the coat brushing the frame."""
    c = rng.uniform(0.05, 0.09)
    Lb = rng.uniform(0.26, 0.34)
    parts = [(0, grip(rng, k), -4), (0.002, frame(rng, k), -3), (c, creak(rng, k), -8), (0.04, brush(rng, k, Lb), -12)]
    return emote(parts)


def grip_rec(rng, k):
    """A gloved hand taking hold: a real glove's leather closing on something."""
    return unit(hit_of(GRIPS[k % 2], 0, 0.12))


def frame_rec(rng, k):
    """What the hand lands on, from real recordings: a real light wood knock for the door frame, a real iron hit pitched
    down and choked by the glove for the grab iron and the cab's doorway."""
    if FRAMES[k] == "door":
        return unit(lp(ck.choke(at(rng, ck.get(WOOD_L[k + 1]), 1), 0.006, 0.02), 4000))
    x = ck.choke(at(rng, ck.get(METAL_M[(k + 1) % 5]), -6 if FRAMES[k] == "grab" else -8), 0.003, 0.012)
    return unit(lp(x, 4000))


def creak_rec(rng, k):
    """The frame's small creak, from a real creak: its squeak dropped for the wooden frame, its grainy low catch for the
    car's boards flexing behind the grab iron, its squeak kept short for the cab door's iron hinge."""
    L = (0.28, 0.16, 0.22)[k]
    semis = (-7, -3, -4)[k] + rng.uniform(-0.5, 0.5)
    c = dsp.fit(dsp.vari(seg(CREAKS[(1, 1, 2)[k]], (0.32, 0.22, 0.06)[k], (0.2, 0.14, 0.2)[k]), semis), samples(L))
    return unit(lp(hp(c, 250), 3500) * env([(0, 0), (0.03, 1), (L * 0.6, 0.6), (L, 0)], L))


def brush_rec(rng, k, length):
    """The coat brushing the frame: a real cloth handling, its rumble and its top taken off."""
    return unit(lp(hp(ck.cloth(rng, k + 2, length), 300), 4500))


@recipe(CE, "lean", "frame",
        "Leaning out of a doorway: a gloved hand taking the frame or the grab iron, the frame's small creak, the coat brushing it",
        """From real recordings: a gloved hand slapping onto and closing round the frame (a real glove's leather taking
        hold, with the frame's own knock under it: a real light wood knock for the door frame, a real iron hit pitched down
        and choked by the glove for the iron), the frame's small creak as the weight goes on it (a real creak: its squeak
        dropped for the wooden frame, its grainy low catch for the car's boards behind the grab iron, its squeak kept
        short for the cab door's hinge), and the coat brushing the frame (a real cloth handling). """ + LEAN_HOW,
        sources=GRIPS + WOOD_L[1:2] + METAL_M[2:4] + CREAKS[1:] + [CLOTH[0], CLOTH[2], CLOTH[3]], takes=3, lufs=-19)
def lean_frame(rng, k):
    return lean(rng, k, grip_rec, frame_rec, creak_rec, brush_rec)


def grip_model(rng, k):
    """A glove closing on the frame, modelled: a short dull crack of leather."""
    return snatch(rng, 0.04)


def frame_model(rng, k):
    """What the hand lands on, modelled: a light board's modes for the door frame, a round iron bar's or the cab's iron
    frame's modes, damped at once by the glove."""
    if FRAMES[k] == "door":
        return body(rng, [m * 1.4 for m in synth.WOOD[:4]], 8, 0.04, 0.003, 3000)
    modes = [m * (1.6 if FRAMES[k] == "grab" else 1.0) for m in synth.IRON[2:6]]
    return ck.choke(body(rng, modes, 14, 0.06, 0.002, 3500), 0.004, 0.012)


def creak_model(rng, k):
    """The frame's small creak, modelled: a few slips of stick-slip through the frame's body (a board's modes for the
    wood, iron's for the grab iron's bracket and the cab door's hinge), slow enough to creak."""
    L = (0.28, 0.16, 0.22)[k]
    b = synth.WOOD if FRAMES[k] == "door" else [m * 1.5 for m in synth.IRON]
    rate = env([(0, 20), (L * 0.4, (45, 60, 90)[k]), (L, 25)], L)
    c = synth.creak(L, rate, rng, body=b, q=14, jitter=0.4, shape=env([(0, 0), (0.03, 1), (L * 0.6, 0.6), (L, 0)], L))
    return unit(lp(hp(c, 250), 4000))


def brush_model(rng, k, length):
    """The coat brushing the frame, modelled: wool dragged over the edge (friction, soft and dark)."""
    return ck.friction(rng, length, 200, 400, 3500, 0.8, env([(0, 0), (length * 0.3, 1), (length, 0)], length))


@recipe(CE, "lean", "grip",
        "Leaning out of a doorway, modelled: a glove closing on the frame, a small stick-slip creak, the coat brushing it",
        """Modelled: the glove closing on the frame (a short dull crack of leather) over the frame's own knock (a light
        board's modes for the door frame, a round iron bar's or the cab frame's modes damped at once by the glove), the
        frame's small creak as the weight goes on (a few slips of stick-slip through the wood, or through the grab iron's
        bracket and the cab door's hinge), and the coat brushing it (wool dragged over the edge). """ + LEAN_HOW,
        takes=3, lufs=-19)
def lean_grip(rng, k):
    return lean(rng, k, grip_model, frame_model, creak_model, brush_model)
