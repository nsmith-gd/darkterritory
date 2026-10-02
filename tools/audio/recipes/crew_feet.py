"""The crew's footsteps (crew-footsteps): work boots on every surface the world is made of, walking, running, jumping,
landing and scuffing.

The director's note on the last pass: "These need to be loopable and consistent." So every surface is built the same way,
from the same pair of boots. Kenney's RPG footsteps are real hard-soled boots on boards, heel then toe; they're cut into
their heels and toes (crew_kit.split) and put back together evenly, so no take has a toe louder than its heel or a room
tail the others lack. A step is a heel, then the toe a walking roll later (70-90 ms; a run lands on the forefoot 25-35 ms
apart). Each contact then sounds the surface: on boards the boot recording is the surface; on everything else the boot's
sole click is kept (high-passed) and the surface's own real recording or model goes under it: Kenney's thin and heavy
plate for grating and the cab's footplate, a sheet's boom for the car roofs, stones and gravel crunch for ballast and coal,
grass and wet steps for grass and mud, concrete steps for setts and floors. Every take of a set is the same distance, the
same space and the same loudness, aligned to start on the heel so the game can fire them in time with the stride.
"""

import functools

import numpy as np

import dsp
import synth
from build import recipe
from dsp import SR, samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck

FEET = ["wood", "grate", "plate", "roof", "coal", "ballast", "dirt", "grass", "mud", "cobbles", "concrete"]
BOOTS = [f"kenney_rpg-audio:footstep0{i}" for i in range(10)]
CONCRETE = [f"kenney_impact-sounds:footstep_concrete_00{i}" for i in range(5)]
GRASS = [f"kenney_impact-sounds:footstep_grass_00{i}" for i in range(5)]
WOODSTEP = [f"kenney_impact-sounds:footstep_wood_00{i}" for i in range(5)]
PLATE_L = [f"kenney_impact-sounds:impactPlate_light_00{i}" for i in range(5)]
PLATE_M = [f"kenney_impact-sounds:impactPlate_medium_00{i}" for i in range(5)]
PLATE_H = [f"kenney_impact-sounds:impactPlate_heavy_00{i}" for i in range(5)]
METAL_L = [f"kenney_impact-sounds:impactMetal_light_00{i}" for i in range(5)]
TIN = [f"kenney_impact-sounds:impactTin_medium_00{i}" for i in range(5)]
STONES = ["sfx_100_v2:stones_01", "sfx_100_v2:stones_02", "sfx_100_v2:stones_03"]
CRUNCH = ["sfx_100_v2:footstep_01", "sfx_100_v2:footstep_02"]
WET = ["sfx_100_v2:footstep_wet_01", "sfx_100_v2:footstep_wet_02", "sfx_100_v2:footstep_wet_03"]


# ---- The boots ----------------------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=1)
def bank():
    """Every heel and toe in the boot recordings, each its own clip at full scale."""
    heels, toes = [], []
    for k in BOOTS:
        parts = ck.split(ck.get(k), max_hits=2, tail=0.13, floor_db=-14, gap=0.04)
        heels.append(ck.norm(parts[0][:samples(0.1)]))
        if len(parts) > 1:
            toes.append(ck.norm(parts[1][:samples(0.14)]))
    return ck.matched(heels), ck.matched(toes)


@functools.lru_cache(maxsize=1)
def concrete():
    """Kenney's concrete steps, matched: two of the five are all sub, the others all slap."""
    return ck.matched([ck.align(ck.get(k)) for k in CONCRETE])


def heel(rng, take):
    h, _ = bank()
    return h[(take * 3 + int(rng.integers(2))) % len(h)]


def toe(rng, take):
    _, t = bank()
    return t[(take * 2 + int(rng.integers(3))) % len(t)]


# Gaits: heel-to-toe roll, heel and toe levels (dB), how hard the surface is driven.
GAIT = {
    "walk": dict(gap=(0.07, 0.09), heel=0.0, toe=-5.0, drive=1.0),
    "run": dict(gap=(0.024, 0.036), heel=-3.0, toe=0.0, drive=1.35),
}


def sole(x, f=1600):
    """The boot's own click, the floor taken out of it: what a hard sole sounds like on anything."""
    return hp(x, f, 2)


# ---- Surfaces: what one contact (a heel or a toe) sounds like on each ----------------------------------------------------------

def on_wood(rng, boot, s, part):
    """Car boards: the boot recording is boots on boards; the car floor is planks over a hollow, so a little more of its
    low knock, and the floor's low mode."""
    y = ck.tilt(boot, lo_db=2.0, lo_f=220) * s
    return mix(y, ck.hollow(rng, [105, 170, 260], 0.12, q=4) * 0.12 * s)


def on_plate(rng, boot, s, part, take):
    """Solid iron plate: a dead, heavy 'tunk' (thick plate, the boot's weight on it chokes the ring at once), a short ring
    an octave down from the thin plate's, the sole's click, and the coal grit that's always on a footplate."""
    body = ck.get(ck.one(PLATE_M + PLATE_H, take + (part == "toe") * 3))
    body = ck.choke(ck.align(dsp.vari(body, -2 + rng.uniform(-0.4, 0.4))), 0.012, 0.022)
    body = ck.norm(hp(body, 70)) * 0.8
    ring = ck.choke(ck.align(dsp.vari(ck.get(ck.one(METAL_L, take + 1)), -12)), 0.004, 0.012)
    grit = ck.grains(rng, 6, 0.05, 2500, 8000, length=(0.003, 0.008)) * 0.06
    y = mix(body * s, ck.norm(ring) * 0.07 * s, sole(boot) * 0.45 * s, grit * s)
    return y


def on_grate(rng, boot, s, part, take):
    """An open steel grate (running boards, steps, the coupler plates): a thinner, brighter clank than plate, the bars
    chattering against their frame for a few ms after, and nothing booming under it (it's open)."""
    body = ck.get(ck.one(PLATE_L, take * 2 + (part == "toe")))
    body = ck.choke(ck.align(dsp.vari(body, rng.uniform(-0.5, 0.5))), 0.02, 0.03)
    body = ck.norm(hp(body, 160)) * 0.75
    ring = ck.choke(ck.align(dsp.vari(ck.get(ck.one(METAL_L, take + 2 * (part == "toe"))), -5)), 0.006, 0.03)
    chatter = np.zeros(samples(0.08), np.float32)
    for i in range(rng.integers(2, 5)):
        c = ck.tick(rng, rng.uniform(1800, 4200), q=rng.uniform(18, 35), length=0.02)
        a = samples(0.006 + 0.012 * i + rng.uniform(0, 0.006))
        chatter[a:a + len(c)] += c[:len(chatter) - a] * rng.uniform(0.3, 0.8)
    y = mix(body * s, ck.norm(ring) * 0.09 * s, sole(boot, 1300) * 0.5 * s, chatter * 0.12 * s)
    return y


def on_roof(rng, boot, s, part, take):
    """Car roof tin: sheet tin nailed over the car's roof boards with the car hollow below, so a step is a low hollow boom
    (the panel and the car under it), the sheet's thin crash damped where the boot is, a little of its tinny ring, and
    the sole's click on it."""
    sheet = ck.get(ck.one(PLATE_L, take + 2 * (part == "toe")))
    sheet = ck.choke(ck.align(dsp.vari(sheet, -1.5 + rng.uniform(-0.4, 0.4))), 0.03, 0.05)
    sheet = ck.norm(hp(sheet, 120))
    tin = ck.choke(ck.align(dsp.vari(ck.get(ck.one(TIN, take + (part == "toe"))), -7 + rng.uniform(-0.5, 0.5))), 0.01, 0.035)
    drum = ck.hollow(rng, [84, 132, 205, 310], 0.26, q=7)
    y = mix(sheet * 0.45 * s, ck.norm(tin) * 0.1 * s, drum * 1.1 * s ** 1.3, sole(boot, 1800) * 0.35 * s)
    return y


def crunch(rng, take, spread, lo, hi, n, rec_keys, rec_db, body_f, body_db, s, part):
    """Loose stuff under a boot: a dull pad of weight, the grains breaking and knocking as the weight comes on (front-
    loaded, then a few settling), and a slice of a real crunch under them."""
    g = ck.grains(rng, int(n * s), spread, lo, hi, q=(3, 10), length=(0.006, 0.02))
    rec = ck.get(rec_keys[(take + (part == "toe")) % len(rec_keys)])
    h = ck.hits(rec, floor_db=-12, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    rec = ck.cut(ck.denoise(rec), a - samples(0.003), a + samples(spread + 0.06), 0.002, 0.04)
    rec = ck.norm(hp(rec, 400))
    pad = ck.pad(rng, 0.08, body_f * 3)
    return mix(ck.norm(g) * s, rec * dsp.db2a(rec_db) * s, pad * dsp.db2a(body_db) * s)


def on_coal(rng, boot, s, part, take):
    """Coal: the boot sinks into lumps that are brittle and glassy, so they break and clink higher and brighter than stone,
    and slide a little down the pile after."""
    y = crunch(rng, take, 0.11, 1400, 7000, 26, STONES, -3, 80, -9, s, part)
    slide = ck.grains(rng, int(6 * s), 0.12, 1800, 6000) * 0.25
    y = mix(y, np.concatenate([np.zeros(samples(0.08), np.float32), slide]) * s, sole(boot, 2500) * 0.15 * s)
    return y


def on_ballast(rng, boot, s, part, take):
    """Ballast: fist-sized crushed rock that grinds and knocks under the weight, lower and heavier than gravel, with the
    packs' real gravel crunch under the stones."""
    y = crunch(rng, take, 0.13, 700, 4500, 22, CRUNCH, 0, 75, -6, s, part)
    return mix(y, sole(boot, 2500) * 0.1 * s)


def on_dirt(rng, boot, s, part, take):
    """Packed dirt and forest floor: a dull, soft thud that takes the click out of the boot, a fine grit of sand and needles
    under it."""
    pad = ck.get(ck.one(WOODSTEP, take + (part == "toe")))
    pad = ck.norm(lp(ck.align(pad), 900)) * 0.8
    thud = lp(boot, 1200) * 0.5
    grit = ck.grains(rng, int(12 * s), 0.07, 2500, 9000, q=(2, 5), length=(0.003, 0.01)) * 0.18
    return mix(pad * s, thud * s, grit * s)


def on_grass(rng, boot, s, part, take):
    """Grass and heath: the blades swishing aside round the boot (the packs' grass steps), the soft ground under it."""
    sw = ck.get(ck.one(GRASS, take * 2 + (part == "toe")))
    sw = ck.norm(ck.align(sw)[:samples(0.2)])
    rus = synth.rustle(0.14, 900, rng, f=(2500, 9000), ticks=0.4) * env([(0, 1), (0.14, 0)], 0.14)
    thud = lp(boot, 700) * 0.35
    return mix(sw * s, ck.norm(rus) * 0.15 * s, thud * s)


def on_mud(rng, boot, s, part, take):
    """Mud and bog: the boot sinking in (a heavy, low squelch, the packs' wet steps darkened), and on the toe the suck
    of the sole pulling out."""
    wet = ck.get(ck.one(WET, take + (part == "toe")))
    h = ck.hits(wet, floor_db=-10, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    wet = ck.norm(lp(ck.cut(ck.denoise(wet), a - samples(0.005), a + samples(0.16), 0.003, 0.05), 2600))
    sq = ck.slurp(rng, 0.2, 180, 900)
    y = mix(wet * 0.7 * s, sq * 0.45 * s, ck.pad(rng, 0.1, 220) * 0.6 * s, lp(boot, 500) * 0.2 * s)
    if part == "toe":
        suck = ck.slurp(rng, 0.11, 300, 1500, rise=True)
        y = mix(y, np.concatenate([np.zeros(samples(0.09), np.float32), suck * 0.3 * s]))
    return y


def on_cobbles(rng, boot, s, part, take):
    """Cobbles and setts, outdoors: a hard stone knock (the packs' concrete steps without their room), the hobnailed
    sole's click on stone, and a little sand between the setts."""
    st = ck.norm(hp(ck.one(concrete(), take + 2 * (part == "toe")), 140))
    nail = ck.tick(rng, rng.uniform(3500, 5500), q=6, length=0.008)
    grit = ck.grains(rng, 5, 0.04, 3000, 9000, q=(2, 5), length=(0.002, 0.006))
    return mix(st * s, sole(boot, 1800) * 0.5 * s, ck.norm(nail) * 0.12 * s, ck.norm(grit) * 0.08 * s)


def on_concrete(rng, boot, s, part, take):
    """Indoor concrete (the fortress, the facilities): the packs' concrete steps and the sole's slap on them; the room is
    added once per step."""
    st = ck.norm(hp(ck.one(concrete(), take * 2 + (part == "toe")), 90))
    return mix(st * s, sole(boot, 1500) * 0.55 * s)


SURF = dict(wood=on_wood, grate=on_grate, plate=on_plate, roof=on_roof, coal=on_coal, ballast=on_ballast, dirt=on_dirt,
            grass=on_grass, mud=on_mud, cobbles=on_cobbles, concrete=on_concrete)
ROOM = dict(wood=lambda y: dsp.room(y, "car", wet=0.14, rng=np.random.default_rng(11)),
            plate=lambda y: dsp.room(y, "cab", wet=0.12, rng=np.random.default_rng(11)),
            concrete=lambda y: ck.space(y, 0.45, 0.14, 5000, ((0.006, 0.5), (0.013, 0.35), (0.021, 0.2))))


def contact(rng, mat, boot, s, part, take):
    f = SURF[mat]
    return f(rng, boot, s, part) if mat == "wood" else f(rng, boot, s, part, take)


def finish(mat, y, rng):
    """The space the surface is in (inside a car, the cab, a building; outdoors stays dry: the game's world reverb takes it)."""
    if mat in ROOM:
        y = ROOM[mat](y)
    return ck.align(y, pre=0.002)


# ---- The cues -----------------------------------------------------------------------------------------------------------------

@functools.lru_cache(maxsize=None)
def target(mat, kind):
    """The set's average balance (from eight steps on fixed seeds), that every take is pulled toward."""
    fn = dict(walk=_step, run=_step, jump=_jump, land=_land, scuff=_scuff)[kind]
    args = (mat, kind) if kind in ("walk", "run") else (mat,)
    return np.mean([ck.profile(fn(np.random.default_rng(100 + i), i, *args)) for i in range(8)], axis=0)


def consistent(fn):
    """A cue's take, EQ-matched most of the way to its set's average: so cycling through them reads as one boot."""
    def take(rng, k, mat, *kind):
        return ck.match(fn(rng, k, mat, *kind), target(mat, kind[0] if kind else fn.__name__[1:]), 0.7)
    return take


def _step(rng, take, mat, kind):
    """One step: heel, then toe a roll later."""
    g = GAIT[kind]
    gap = rng.uniform(*g["gap"])
    s = g["drive"] * dsp.db2a(rng.uniform(-0.6, 0.6))
    h = contact(rng, mat, heel(rng, take), s * dsp.db2a(g["heel"]), "heel", take)
    t = contact(rng, mat, toe(rng, take), s * dsp.db2a(g["toe"]), "toe", take)
    return finish(mat, ck.place([(0.0, h, 0), (gap, t, 0)]), rng)


def _jump(rng, take, mat):
    """Push-off: both forefeet driven down and back, a short scrape as they leave."""
    a = contact(rng, mat, toe(rng, take), 1.2, "toe", take)
    b = contact(rng, mat, toe(rng, take + 3), 0.8, "toe", take + 1)
    sc = scrape(rng, mat, 0.09, 0.7)
    return finish(mat, ck.place([(0.0, a, 0), (rng.uniform(0.02, 0.04), b, 0), (0.01, sc, -2)]), rng)


def _land(rng, take, mat):
    """Both feet coming down hard a few ms apart, flat-footed, then the knees taking it and a foot shuffling to settle."""
    d = rng.uniform(0.012, 0.03)
    parts = [(0.0, contact(rng, mat, heel(rng, take), 1.6, "heel", take), 0),
             (0.004, contact(rng, mat, toe(rng, take), 1.3, "toe", take), -2),
             (d, contact(rng, mat, heel(rng, take + 2), 1.4, "heel", take + 1), 0),
             (d + 0.006, contact(rng, mat, toe(rng, take + 1), 1.1, "toe", take + 1), -3)]
    settle = rng.uniform(0.18, 0.26)
    parts += [(settle, contact(rng, mat, toe(rng, take + 4), 0.45, "toe", take + 2), -4),
              (settle, scrape(rng, mat, 0.1, 0.4), -6)]
    if mat in ("wood", "roof", "plate"):
        parts.append((0.0, ck.hollow(rng, [62, 98, 150], 0.25, q=3) * 0.3, -4 if mat != "roof" else 0))
    return finish(mat, ck.place(parts), rng)


def _scuff(rng, take, mat):
    """Turning on the spot or stopping short: the forefoot planted and the sole dragged a hand's width over the surface."""
    L = rng.uniform(0.16, 0.26)
    a = contact(rng, mat, toe(rng, take), 0.7, "toe", take)
    sc = scrape(rng, mat, L, 1.0)
    return finish(mat, ck.place([(0.0, a, -2), (0.015, sc, 0)]), rng)


step, jump, land, scuff = consistent(_step), consistent(_jump), consistent(_land), consistent(_scuff)


SCRAPE = {
    # band (Hz), stick-slip rate, roughness, grains (n per s, lo, hi) for the surface under a dragged sole
    "wood": ((300, 3500), 150, 0.6, (0, 0, 0)),
    "grate": ((800, 5000), 220, 0.5, (0, 0, 0)),
    "plate": ((600, 5000), 260, 0.5, (90, 2500, 8000)),
    "roof": ((400, 4000), 170, 0.6, (0, 0, 0)),
    "coal": ((400, 4000), 110, 0.8, (260, 1300, 6500)),
    "ballast": ((300, 3000), 80, 0.8, (220, 700, 4000)),
    "dirt": ((400, 4000), 210, 0.7, (160, 2500, 9000)),
    "grass": ((900, 6000), 0, 0.0, (0, 0, 0)),
    "mud": ((180, 1100), 0, 0.0, (0, 0, 0)),
    "cobbles": ((600, 5000), 280, 0.6, (140, 3000, 9000)),
    "concrete": ((600, 5000), 300, 0.6, (100, 3000, 9000)),
}


def scrape(rng, mat, L, s):
    """A sole dragged across a surface: friction in the surface's band, its grit, and what it does (bars rattling under it,
    tin flexing, grass swishing, mud sucking)."""
    (lo, hi), rate, rough, (gn, glo, ghi) = SCRAPE[mat]
    shape = env([(0, 0), (0.02, 1), (L * 0.6, 0.7), (L, 0)], L)
    if mat == "grass":
        sw = ck.norm(ck.align(ck.get(GRASS[int(rng.integers(len(GRASS)))]))[:samples(L)])
        y = mix(sw, ck.norm(synth.rustle(L, 1400, rng, f=(lo, hi), ticks=0.6) * shape) * 0.5)
    elif mat == "mud":
        y = ck.slurp(rng, L, lo, hi)
    else:
        y = ck.friction(rng, L, rate * rng.uniform(0.85, 1.15), lo, hi, rough, shape)
        if gn:
            y = mix(y, ck.grains(rng, int(gn * L), L, glo, ghi, q=(2, 8), length=(0.003, 0.012), shape=0.2) * 0.6)
        if mat == "grate":
            # sliding over the bars: a knock each bar
            n = int(L / 0.03)
            for i in range(n):
                c = ck.tick(rng, rng.uniform(1500, 3500), q=25, length=0.025)
                a = samples(i * 0.03 + rng.uniform(0, 0.004))
                y[a:a + len(c)] += c[:len(y) - a] * 0.4 * shape[min(a, len(shape) - 1)]
        if mat == "roof":
            y = mix(y, ck.hollow(rng, [88, 140, 215], L, q=8, hit=L * 0.5) * 0.15)
    return ck.norm(y) * s


# ---- Registration -------------------------------------------------------------------------------------------------------------

WHAT = {
    "wood": ("boots on a car's plank floor", BOOTS),
    "grate": ("boots on an open steel grate (running boards, steps, the coupler plates)", BOOTS + PLATE_L + METAL_L),
    "plate": ("boots on the cab's solid iron footplate and the tender deck", BOOTS + PLATE_M + PLATE_H + METAL_L),
    "roof": ("boots on a car's tin roof", BOOTS + PLATE_L + TIN),
    "coal": ("boots on the coal in the tender", BOOTS + STONES),
    "ballast": ("boots on the ballast of the track bed", BOOTS + CRUNCH),
    "dirt": ("boots on packed dirt and forest floor", BOOTS + WOODSTEP),
    "grass": ("boots in grass and heath", BOOTS + GRASS),
    "mud": ("boots in mud and bog", BOOTS + WET),
    "cobbles": ("boots on cobbles and stone setts, outdoors", BOOTS + CONCRETE),
    "concrete": ("boots on an indoor concrete floor", BOOTS + CONCRETE),
}

HOW = {
    "wood": """The packs' real boot-on-boards recordings (Kenney RPG footsteps) cut into heels and toes and recombined evenly:
        heel, then the toe 70-90 ms later and 5 dB under it. A little more of the boards' low knock and their low mode for a
        car floor over a hollow, and the car's small wooden room.""",
    "grate": """Each heel and toe is Kenney's thin plate hit, choked fast where the boot stops it, a short ring of light metal
        pitched down, and two to four tiny clicks of the bars chattering in their frame; the boot's sole click (the boot
        recordings high-passed) on top. Kept free of low boom: a grate is open.""",
    "plate": """Kenney's medium and heavy plate hits, pitched down a little and choked at once (thick plate under a boot's
        weight goes dead), a faint ring of metal an octave down, the boot's sole click and a pinch of coal grit, in the
        cab's small iron room.""",
    "roof": """A sheet of tin over a hollow car: Kenney's thin plate hit pitched down and only lightly damped, a tin ring
        pitched down a fifth and kept quiet, the panel's low boom (a 96 Hz drum mode), and the sole's click. Outdoors, dry.""",
    "coal": """Coal lumps are brittle and glassy: a burst of hard little grains breaking and clinking as the weight comes
        on (front-loaded), a slice of the packs' real stones knocking under them, a dull pad of weight, and a few lumps
        sliding down the pile after.""",
    "ballast": """Crushed rock grinding under the weight: the packs' real gravel-crunch steps (sfx_100 footstep_01/02, a
        slice from a different crunch each take), lower stone grains over them, and a dull pad of weight. Outdoors, dry.""",
    "dirt": """Soft ground takes the click out of a boot: Kenney's dull wooden step lowpassed as the earth's thud, the boot
        recording lowpassed under it, and a fine grit of sand and needles.""",
    "grass": """Kenney's grass steps (the blades swishing aside) as each contact, a thin rustle of stalks for variety, and
        the boot lowpassed as the soft ground under it.""",
    "mud": """The packs' wet steps darkened and cut to their squelch, a lower synthesised suction squelch and a dull heavy
        pad as the boot sinks, and on the toe the suck of the sole pulling out.""",
    "cobbles": """Kenney's concrete steps with their room taken out (outdoors), the boot's sole click and a hobnail tick on
        stone, and a little sand between the setts.""",
    "concrete": """Kenney's concrete steps, heel and toe, with the boot's sole slap, in a short hard indoor room.""",
}


def _register():
    for mat in FEET:
        what, srcs = WHAT[mat]
        srcs = sorted(set(srcs))
        how = " ".join(HOW[mat].split())
        recipe("crew-footsteps", "walk", "boots", f"Walking: {what}",
               how + " Six takes, each heel and toe from a different pair, walked in a steady gait in the preview.",
               sources=srcs, takes=6, mat=mat, lufs=-20,
               preview=lambda t, r: ck.gait(t, r, 0.56, 16))(lambda rng, k, m=mat: step(rng, k, m, "walk"))
        recipe("crew-footsteps", "run", "boots", f"Running: {what}",
               how + " Running lands on the forefoot: heel and toe 25-35 ms apart, the toe the louder, the surface driven "
                     "harder. Six takes, run in a steady stride in the preview.",
               sources=srcs, takes=6, mat=mat, lufs=-17,
               preview=lambda t, r: ck.gait(t, r, 0.34, 20))(lambda rng, k, m=mat: step(rng, k, m, "run"))
        recipe("crew-footsteps", "jump", "boots", f"Push-off into a jump: {what}",
               "Both forefeet driven down a few ms apart and the soles scraping as they leave, from the same boot and "
               "surface parts as the walk. The preview runs four steps into each jump.",
               sources=srcs, takes=3, mat=mat, lufs=-19,
               preview=lambda t, r, m=mat: _context(t, r, m, "jump"))(lambda rng, k, m=mat: jump(rng, k, m))
        recipe("crew-footsteps", "land", "boots", f"Landing from a jump or drop: {what}",
               "Both feet flat down 12-30 ms apart, driven hard (heavier on the surface, with the weight's low thump on "
               "hollow decks), then the knees taking it and one foot shuffling to settle. The preview jumps into each.",
               sources=srcs, takes=3, mat=mat, lufs=-15,
               preview=lambda t, r, m=mat: _context(t, r, m, "land"))(lambda rng, k, m=mat: land(rng, k, m))
        recipe("crew-footsteps", "scuff", "boots", f"Turning or stopping short: {what}",
               "The forefoot planted and the sole dragged over the surface: stick-slip friction in the surface's band "
               "with its grit (and on a grate the bars knocking under it, on grass a swish, in mud a suck). The preview "
               "walks into each stop.",
               sources=srcs, takes=3, mat=mat, lufs=-21,
               preview=lambda t, r, m=mat: _context(t, r, m, "scuff"))(lambda rng, k, m=mat: scuff(rng, k, m))


def _context(takes, rng, mat, cue):
    """Each take where it happens: run into a jump and land, or walk into a stop."""
    run = [ck.leveled(step(np.random.default_rng(i), i, mat, "run"), -17) for i in range(4)]
    walk = [ck.leveled(step(np.random.default_rng(10 + i), i, mat, "walk"), -20) for i in range(3)]
    other = {"jump": [ck.leveled(land(np.random.default_rng(20 + i), i, mat), -15) for i in range(3)],
             "land": [ck.leveled(jump(np.random.default_rng(30 + i), i, mat), -19) for i in range(3)]}
    parts, t = [], 0.3
    for i, x in enumerate(takes):
        if cue == "scuff":
            for k in range(3):
                parts.append((t, walk[k % 3], 0.0))
                t += 0.56
            parts.append((t - 0.2, x, 0.0))
            t += 1.2
            continue
        for k in range(4):
            parts.append((t, run[k], 0.0))
            t += 0.34
        jmp = x if cue == "jump" else other["land"][i % 3]
        lnd = x if cue == "land" else other["jump"][i % 3]
        parts.append((t, jmp, 0.0))
        t += 0.62
        parts.append((t, lnd, 0.0))
        t += 0.9
        for k in range(2):
            parts.append((t, walk[k], 0.0))
            t += 0.56
        t += 0.5
    return ck.place(parts, t + 0.5)


_register()
