"""The train's fittings worked by hand: the car doors and hatches (crew-doors), the firebox door (crew-firebox-door), the
fireman's shovel (crew-shovel), the cab's levers, valves, brake and whistle (crew-cab-controls), the switch stand
(crew-switch) and the couplings (crew-coupling).

Everything here is iron, wood or steam, and big. The packs' real doors, latches and clanks are cut to their hits and
pitched to the size of the thing (a boxcar's sliding door is far heavier than any door in the packs, so its slam is
their slam a few semitones down with the car's hollow under it); iron is Kenney's heavy metal and plate hits choked
short (cast iron is dead: it clanks, it doesn't ring); latches are the packs' latch and lock clicks. Where the packs have
nothing, physics: a steam whistle is a jet of steam sounding three bells (a chord of breathy pitched tones that rises
as the pressure comes up and sags as it goes), steam itself is jet noise (a roar under the hiss), a fire is a roar with
coal crackling in it, sand is a stream of fine grains.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import R, S, K
from recipes.crew_items import hit_of, tool_ring

METAL_H, PLATE_H, PLATE_M, PLATE_L = K("impactMetal_heavy"), K("impactPlate_heavy"), K("impactPlate_medium"), K("impactPlate_light")
METAL_M, METAL_L = K("impactMetal_medium"), K("impactMetal_light")
LATCHES = S("door_02", "door_04", "door_05") + R("metalLatch")
OPENS = R("doorOpen_1", "doorOpen_2")
CLOSES = R("doorClose_1", "doorClose_2", "doorClose_3")
SLAM = S("door_03")
MINING = K("impactMining")


def iron(rng, take, size=-6.0, damp=0.04, ring=0.3):
    """A heavy cast-iron part knocked: Kenney's heavy plate for its mass and a heavy metal hit, both pitched down to
    the part's size and choked (cast iron clanks, it doesn't sing)."""
    body = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_H[take % 5]), size * 0.5 + rng.uniform(-0.5, 0.5)))), 0.02, damp * 1.5)
    clank = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[(take + 2) % 5]), size + rng.uniform(-0.5, 0.5)))), 0.004, damp)
    return ck.norm(mix(body, clank * ring * 2))


def latch(rng, take, semis=0.0, which=None):
    """A latch or a lock's click (the packs' real latches), cut to its first hit."""
    k = LATCHES[(take if which is None else which) % len(LATCHES)]
    return dsp.vari(hit_of(k, 0, 0.15, -16), semis + rng.uniform(-0.5, 0.5))


def car(y, wet=0.14):
    return dsp.room(y, "car", wet=wet, rng=np.random.default_rng(11))


def cab(y, wet=0.12):
    return dsp.room(y, "cab", wet=wet, rng=np.random.default_rng(3))


# ---- Doors ----------------------------------------------------------------------------------------------------------------

def doors():
    L = "crew-doors"

    # The big side door: a boxcar's sliding door, planks on an iron frame, hung on rollers from an iron track.
    @recipe(L, "slide-unlatch", "bar", "Big sliding side door: the iron latch bar lifted out of its keeper",
            """The heavy iron latch bar lifted out of its keeper (Kenney's heavy plate and metal hits pitched well down,
            choked: cast iron), the packs' latch click as it clears, and the door shifting on its rollers with a low knock
            from the car's boards.""", sources=PLATE_H + METAL_H + LATCHES + ck.WOOD_KNOCKS, takes=3, lufs=-21)
    def unlatch(rng, k):
        return car(ck.place([(0, latch(rng, k, -5), -4), (0.03, iron(rng, k, -8, 0.03), -2),
                             (0.12, ck.floor(rng, "wood", k, 1.2, 0.3), -14)]))

    @recipe(L, "slide-start", "rollers", "Big sliding door: breaking free and starting to roll",
            """The door heaved: its rollers breaking free on the iron track with a groan (an iron stick-slip creak), the
            planked door shuddering against its frame (a low wooden knock), and the rumble of the rollers starting.""",
            sources=ck.WOOD_KNOCKS, takes=2, lufs=-21)
    def start(rng, k):
        groan = synth.creak(0.5, env([(0, 12), (0.5, 60)], 0.5), rng, body=synth.IRON, q=10) * env([(0, 0), (0.06, 1), (0.5, 0.2)], 0.5)
        roll = rolling(rng, 0.7) * env([(0, 0), (0.7, 1)], 0.7)
        return car(ck.place([(0, ck.floor(rng, "wood", k, 2.0, 0.4), -6), (0.02, groan, -8), (0.15, roll, -6)]))

    @recipe(L, "slide-roll", "rollers", "Big sliding door rolling on its iron track, held",
            """Iron rollers grinding along an iron track (low stick-slip friction and the rumble of the wheels), the
            heavy door rattling in its guides, and a knock each time a roller crosses a joint in the track. One 8 s
            cycle.""", sources=ck.WOOD_KNOCKS, loop=True, takes=1, lufs=-21)
    def roll(rng, k):
        n = samples(8.0)
        y = rolling(rng, 8.6)
        for t in np.arange(0.4, 8.6, rng.uniform(0.9, 1.2)):
            y = mix(y, ck.place([(t, ck.floor(rng, "wood", int(t * 7), 0.9, 0.5), -10)], 8.6))
        return dsp.wrap(car(y), n)

    @recipe(L, "slide-open-stop", "stop", "Big sliding door hitting its open stop",
            """The door's weight arriving at its stop: a heavy wooden thud with the iron stop under it (the packs' door
            slam pitched down to a boxcar door, heavy plate), the door rattling in its guides and settling back.""",
            sources=SLAM + PLATE_H + ck.WOOD_KNOCKS, takes=2, lufs=-18)
    def open_stop(rng, k):
        slam = dsp.vari(hit_of(SLAM[0], k % 2, 0.5), -3 + rng.uniform(-0.5, 0.5))
        rattle = ck.grains(rng, 6, 0.15, 300, 1800, q=(6, 12), length=(0.02, 0.05))
        return car(ck.place([(0, slam, 0), (0, iron(rng, k, -7, 0.05, 0.15), -8), (0.02, ck.floor(rng, "wood", k, 2.5, 0.6), -4),
                             (0.05, rattle, -16)]))

    @recipe(L, "slide-shut", "slam", "Big sliding door slammed shut: the whole car booming with it",
            """Heard and trusted (the Choir's rule): the heavy planked door slammed into its frame (the packs' door slam,
            pitched down to a boxcar door's weight), the car's whole hollow body booming with it, the iron frame clanking,
            and the door rattling once in its guides as it settles.""",
            sources=SLAM + R("doorClose_4") + PLATE_H + ck.WOOD_KNOCKS, takes=3, lufs=-15)
    def shut(rng, k):
        slam = dsp.vari(hit_of(SLAM[0], 0, 0.6), -4 + rng.uniform(-0.6, 0.6))
        thump = dsp.vari(hit_of(R("doorClose_4")[0], 0, 0.4), -3)
        boom = ck.hollow(rng, [62, 95, 140, 210], 0.5, q=4)
        rattle = ck.grains(rng, 8, 0.2, 300, 2000, q=(6, 12), length=(0.02, 0.05))
        return car(ck.place([(0, slam, 0), (0, thump, -4), (0, boom, -4), (0.004, iron(rng, k, -6, 0.05, 0.2), -10),
                             (0.08, rattle, -14)]), 0.18)

    @recipe(L, "slide-latch", "bar", "Big sliding door latched: the iron bar dropped into its keeper",
            """The confirmation that it's shut: the iron latch bar dropped into its keeper, a bright latch click and then
            the solid clank of the bar seating (heavy plate and metal, choked), with the door's planks knocking once.""",
            sources=LATCHES + PLATE_H + METAL_H, takes=3, lufs=-18)
    def latch_home(rng, k):
        return car(ck.place([(0, latch(rng, k + 1, -3), -2), (0.035, iron(rng, k + 1, -5, 0.04, 0.35), 0),
                             (0.04, ck.floor(rng, "wood", k, 0.8, 0.5), -12)]))

    # The small end door: a hinged plank door with a thumb latch, into the next car across the coupling.
    @recipe(L, "end-open", "door", "Small end door opened: thumb latch, a short hinge creak, the door swinging in",
            """The thumb latch pressed and lifted (the packs' latch click), then the packs' real door opening (Kenney RPG
            doorOpen: latch, hinge creak) cut to its swing and kept short, with the car's small wooden room.""",
            sources=LATCHES + OPENS, takes=2, lufs=-21)
    def end_open(rng, k):
        o = ck.align(ck.get(OPENS[k]))
        o = ck.choke(o, 0.55, 0.12)
        return car(ck.place([(0, latch(rng, k + 2), -6), (0.04, o, 0)]))

    @recipe(L, "end-shut", "door", "Small end door shut: the door into its frame and the latch dropping",
            """The packs' real door shutting (Kenney RPG doorClose takes), given a little more body for a railway car's
            plank door (its low knock and the car's hollow), and the thumb latch dropping home.""",
            sources=CLOSES + LATCHES + ck.WOOD_KNOCKS, takes=3, lufs=-19)
    def end_shut(rng, k):
        c = ck.align(ck.get(CLOSES[k]))
        return car(ck.place([(0, ck.tilt(c, lo_db=3, lo_f=300), 0), (0.005, ck.hollow(rng, [90, 150, 230], 0.2, q=4), -12)]))

    # The roof hatch: a heavy plank hatch skinned with tin, on a strap hinge.
    @recipe(L, "hatch-open", "hatch", "Roof hatch thrown open: the hinge, then the hatch banging over onto the roof",
            """The big roof hatch shoved up and over: its strap hinge creaking (an iron stick-slip creak), a moment in the
            air, then the hatch banging down on the roof (a heavy wooden knock with the tin roof booming and buzzing under
            it: thin plate and the roof's hollow). Outdoors.""", sources=ck.WOOD_KNOCKS + PLATE_L, takes=3, lufs=-18)
    def hatch_open(rng, k):
        hinge = synth.creak(0.35, env([(0, 25), (0.35, 70)], 0.35), rng, body=synth.IRON, q=14) * env([(0, 0), (0.05, 1), (0.35, 0.3)], 0.35)
        bang = hatch(rng, k, 0.9)
        return ck.place([(0, hinge, -8), (0.5 + rng.uniform(0, 0.1), bang, 0)])

    @recipe(L, "hatch-shut", "hatch", "Roof hatch dropped shut into its frame",
            """The hatch dropped into its frame: a heavy wooden slam (the packs' door slam) with the roof tin booming and
            rattling round it and the car hollow under it.""", sources=SLAM + ck.WOOD_KNOCKS + PLATE_L, takes=3, lufs=-16)
    def hatch_shut(rng, k):
        slam = dsp.vari(hit_of(SLAM[0], 0, 0.5), -2 + rng.uniform(-0.5, 0.5))
        return ck.place([(0, slam, -2), (0, hatch(rng, k + 1, 1.0), 0), (0, ck.hollow(rng, [70, 110, 165], 0.45, q=4), -6)])

    # The shot locker: an iron-bound wooden chest.
    @recipe(L, "locker-open", "chest", "Locker opened: the hasp, the iron-bound lid lifted and propped",
            """The hasp flipped (the packs' latch), the heavy lid lifted on iron strap hinges (a short iron creak), and
            the lid knocked back against its stay (a wooden knock and an iron fitting's rattle).""",
            sources=LATCHES + ck.WOOD_KNOCKS + METAL_H, takes=3, lufs=-21)
    def locker_open(rng, k):
        hinge = synth.creak(0.3, rng.uniform(40, 70), rng, body=synth.IRON, q=16) * env([(0, 0), (0.05, 1), (0.3, 0.2)], 0.3)
        stay = mix(ck.floor(rng, "wood", k, 0.8, 0.8), ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[k]), -3))), 0.003, 0.02) * 0.3)
        return car(ck.place([(0, latch(rng, k), -4), (0.15, hinge, -10), (0.45, stay, 0)]))

    @recipe(L, "locker-shut", "chest", "Locker shut: the iron-bound lid dropped and the hasp snapped over",
            """The heavy lid dropped shut (a deep wooden knock, the chest's hollow and its iron bands rattling), then the
            hasp snapped over (the packs' latch).""", sources=ck.WOOD_KNOCKS + LATCHES + METAL_H, takes=3, lufs=-18)
    def locker_shut(rng, k):
        lid = mix(ck.floor(rng, "wood", k, 2.0, 0.9), dsp.room(ck.hollow(rng, [120, 190, 300], 0.15, q=5), "box", wet=0.3) * 0.4)
        bands = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[(k + 1) % 5]), -4))), 0.005, 0.03)
        return car(ck.place([(0, lid, 0), (0.01, bands, -14), (0.32 + rng.uniform(0, 0.06), latch(rng, k + 1, -1), -6)]))


def rolling(rng, length):
    """Iron rollers on an iron track: low stick-slip friction, the wheels' rumble, the door rattling in its guides."""
    n = samples(length)
    fr = ck.friction(rng, length, 70, 120, 1400, 0.8)
    rum = lp(synth.noise(length, rng, "brown"), 180) * (0.7 + 0.3 * lp(rng.standard_normal(n).astype(np.float32), 6) * 10)
    rattle = ck.grains(rng, int(10 * length), length, 400, 1600, q=(5, 10), length=(0.01, 0.03), shape=0.0)
    return ck.norm(mix(fr * 0.6, ck.norm(rum) * 0.7, ck.norm(rattle[:n]) * 0.25))


def hatch(rng, take, force):
    """The hatch's planks meeting the roof: a heavy wooden knock with the tin skin's crash and buzz and the roof's boom."""
    knock = ck.floor(rng, "wood", take, 2.2 * force, 0.9)
    tin = ck.choke(ck.norm(hp(ck.align(dsp.vari(ck.get(PLATE_L[take % 5]), -1)), 150)), 0.05, 0.08)
    buzz = ck.grains(rng, 10, 0.12, 400, 2500, q=(10, 20), length=(0.02, 0.04))
    return ck.norm(ck.place([(0, knock, 0), (0, tin, -4), (0.01, buzz, -18), (0, ck.hollow(rng, [84, 132, 205], 0.3, q=6), -4)]))


# ---- The firebox door, the fire, the shovel --------------------------------------------------------------------------------

def firebox_roar(rng, length, intensity=1.0):
    """A locomotive's coal fire under draught, heard through the firebox door: a deep turbulent roar (pink noise through
    the firebox's broad low resonances, lapping slowly), the white-hot bed's hiss, and coal cracking in it."""
    n = samples(length)
    r = hp(synth.noise(length, rng, "pink"), 70, 2)
    r = mix(lp(r, 1400, 2) * 0.8, dsp.resonate(r, [160, 260, 420, 650], q=3) * 0.2)
    lap = np.clip(0.75 + 0.25 * lp(rng.standard_normal(n).astype(np.float32), 3) * 14, 0.4, 1.3)
    flutter = 1 + 0.12 * lp(rng.standard_normal(n).astype(np.float32), 22) * 12
    roar = ck.norm(r * lap * flutter)
    hiss = ck.norm(bp(synth.noise(length, rng), 2500, 9000)) * np.clip(lap, 0.6, 1.2)
    crack = synth.crackle(length, 40 * intensity, rng, size=(0.0005, 0.004), hi=1200)
    pops = synth.crackle(length, 3 * intensity, rng, size=(0.004, 0.012), hi=400)
    return ck.norm(mix(roar, hiss * 0.12, ck.norm(crack) * 0.3, ck.norm(pops) * 0.35))


def fire():
    L = "crew-firebox-door"

    @recipe(L, "open", "door", "Firebox door opened: the iron latch thrown, the door swung back, the fire's roar let out",
            """The cast-iron door's latch handle thrown (a heavy iron clank, choked: cast iron is dead), its hinge grinding
            (a short iron creak), and the fire's roar rushing out as the door clears (the roar swelling over half a
            second). In the cab's iron room.""", sources=METAL_H + PLATE_H + LATCHES, takes=3, lufs=-17)
    def open_(rng, k):
        hinge = synth.creak(0.25, rng.uniform(30, 50), rng, body=synth.IRON, q=12) * env([(0, 0), (0.04, 1), (0.25, 0)], 0.25)
        roar = firebox_roar(rng, 0.9) * env([(0, 0), (0.35, 1), (0.9, 0)], 0.9)
        return cab(ck.place([(0, latch(rng, k, -6, 3), -6), (0.02, iron(rng, k, -4, 0.04, 0.5), -2), (0.1, hinge, -12),
                             (0.15, roar, -4)]))

    @recipe(L, "shut", "door", "Firebox door clanged shut: iron on iron, the roar cut off",
            """The heavy cast-iron door swung to and clanging into its iron frame (heavy plate and metal hits pitched down,
            a short ring of the backhead under it), the latch dropping, and the fire's roar cut off behind it.""",
            sources=METAL_H + PLATE_H + K("impactBell_heavy") + LATCHES, takes=3, lufs=-15)
    def shut(rng, k):
        ring = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(K("impactBell_heavy")[k % 5]), -7))), 0.02, 0.08)
        roar = firebox_roar(rng, 0.25) * env([(0, 1), (0.25, 0)], 0.25, "exp")
        return cab(ck.place([(0, roar, -12), (0.06, iron(rng, k + 1, -3, 0.06, 0.6), 0), (0.06, ring, -16),
                             (0.15 + rng.uniform(0, 0.05), latch(rng, k + 1, -5, 3), -8)]))

    @recipe(L, "fire-open", "roar", "The fire heard through the open firebox door, held",
            """A coal fire under draught: a deep, lapping roar (pink noise through the firebox's broad low resonances,
            swelling and flickering), the white-hot bed's thin hiss, and coal cracking and popping in it. Synthesised from
            the physics; one 10 s cycle.""", loop=True, takes=1, lufs=-20)
    def roar(rng, k):
        n = samples(10.0)
        return dsp.wrap(cab(firebox_roar(rng, 10.3), 0.1), n + samples(0.3))


def shovel():
    L = "crew-shovel"

    @recipe(L, "scoop", "coal", "The shovel driven along the shovelling plate into the coal",
            """The fireman's scoop: the steel blade driven along the tender's iron shovelling plate (a hard, fast steel-on-
            steel scrape), biting into the coal (lumps cracking and knocking against the blade, with a slice of the
            packs' real stones), the blade's thin ring choked by its load, and the coal sliding in behind it.""",
            sources=ck.STONES + PLATE_L + METAL_L, takes=4, lufs=-17)
    def scoop(rng, k):
        L_ = rng.uniform(0.14, 0.2)
        scr = ck.friction(rng, L_, 320, 800, 6000, 0.5, env([(0, 0.2), (0.02, 1), (L_, 0.6)], L_))
        bite = coal(rng, k, 0.18, 30)
        slide = ck.grains(rng, 18, 0.25, 900, 5000, q=(3, 9), shape=0.3)
        return cab(ck.place([(0, scr, -2), (L_ * 0.8, bite, 0), (L_ * 0.8, ck.choke(tool_ring(rng, "shovel", k), 0.004, 0.02), -14),
                             (L_, slide, -10)]))

    @recipe(L, "throw", "coal", "The coal thrown into the firebox, landing on the fire",
            """The shovel swung in (a short low whoof), its load of coal flung off the blade and landing on the firebed
            (a scatter of lumps knocking down on the hot coals, darkened: it lands inside the firebox), and the fire
            flaring up round it (a swell of the roar).""", sources=ck.STONES, takes=4, lufs=-17)
    def throw(rng, k):
        w = ck.whoosh(rng, 0.22, 120, 900, 0.6, 0.8)
        lumps = lp(coal(rng, k + 1, 0.22, 40), 4500)
        flare = firebox_roar(rng, 0.8) * env([(0, 0), (0.12, 1), (0.8, 0)], 0.8)
        return cab(ck.place([(0, w, -8), (0.16, lumps, 0), (0.18, flare, -8)]))

    @recipe(L, "knock", "blade", "The shovel blade knocked on the firebox door ring",
            """The steel blade struck on the cast-iron door ring to clear it: the blade's bright tang (thin plate and a light
            metal ring) on the dead iron (a heavy metal hit pitched down, choked), in the cab.""",
            sources=PLATE_L + METAL_L + METAL_H + PLATE_H, takes=3, lufs=-17)
    def knock(rng, k):
        return cab(ck.place([(0, tool_ring(rng, "shovel", k + 1), 0), (0, iron(rng, k, -5, 0.04, 0.4), -6)]))


def coal(rng, take, spread, n):
    """Coal lumps cracking and knocking: hard glassy grains, front-loaded, over a slice of the packs' real stones."""
    g = ck.grains(rng, n, spread, 1300, 6500, q=(4, 12))
    st = ck.get(ck.STONES[take % 3])
    h = ck.hits(st, floor_db=-12, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    st = ck.norm(ck.cut(st, a - samples(0.002), a + samples(spread + 0.05), 0.002, 0.04))
    return ck.norm(mix(ck.norm(g), st * 0.7, ck.pad(rng, 0.08, 300) * 0.4))


# ---- Cab controls ----------------------------------------------------------------------------------------------------------

def steam(rng, length, roar=0.6, f=(400, 7000)):
    """Steam escaping: jet noise. A broadband hiss with a roar under it (the pressure in it), wavering. Not a fizz: no
    crackle or bubbles riding on it."""
    n = samples(length)
    hiss = bp(synth.noise(length, rng), f[0], f[1], 2)
    body = lp(synth.noise(length, rng, "pink"), 500, 2)
    w = np.clip(1 + 0.15 * lp(rng.standard_normal(n).astype(np.float32), 4) * 12, 0.6, 1.4)
    return ck.norm(mix(ck.norm(hiss), ck.norm(body) * roar) * w)


def whistle_tone(rng, length, rise=0.0, sag=0.0, notes=(311.1, 392.0, 466.2)):
    """A three-chime steam whistle: three bells blown by one jet, each a breathy harmonic tone (its harmonics falling
    off, a turbulent flutter in its level, steam noise round each partial). `rise` / `sag` bend the pitch (semitones) as
    the pressure comes up at the start or drops at the end."""
    n = samples(length)
    t = np.arange(n) / dsp.SR
    bend = np.zeros(n, np.float32)
    if rise:
        bend += -rise * np.exp(-t / 0.12)
    if sag:
        bend += -sag * np.clip((t - (length - 0.5)) / 0.5, 0, 1) ** 2
    out = np.zeros(n, np.float32)
    for i, f0 in enumerate(notes):
        drift = lp(rng.standard_normal(n).astype(np.float32), 2) * 4 * 0.004
        f = f0 * 2 ** (bend / 12) * (1 + drift)
        ph = 2 * np.pi * np.cumsum(f) / dsp.SR
        tone = sum(np.sin(h * ph) / h ** 1.4 for h in range(1, 7))
        breath = dsp.resonate(synth.noise(length, rng), [f0 * h for h in (1, 2, 3)], q=25, gains=[1, 0.4, 0.2])
        flutter = 1 + 0.08 * lp(rng.standard_normal(n).astype(np.float32), 30) * 15
        out += (ck.norm(tone) + ck.norm(breath) * 0.35) * flutter * [1.0, 0.85, 0.75][i]
    return ck.norm(out)


def controls():
    L = "crew-cab-controls"

    @recipe(L, "regulator-notch", "quadrant", "The regulator handle moved one notch on its quadrant",
            """The regulator's long handle moved across the boiler backhead: the brass slide of the handle (a short metal
            friction), the spring catch clicking over and dropping into the next notch of the quadrant (the packs' ratchet
            and latch clicks), and the handle's heavy iron knock in the cab's iron room.""",
            sources=S("misc_20", "lock_open_01") + LATCHES + METAL_H + PLATE_H, takes=4, lufs=-20)
    def regulator(rng, k):
        slide = ck.friction(rng, 0.09, 260, 900, 5000, 0.6, env([(0, 0), (0.02, 1), (0.09, 0)], 0.09))
        catch = hit_of("sfx_100_v2:lock_open_01", k, 0.06, -14)
        return cab(ck.place([(0, slide, -12), (0.07, catch, -4), (0.075, iron(rng, k, -4, 0.025, 0.4), -6)]))

    @recipe(L, "reverser-notch", "bar", "The reverser (Johnson bar) moved one notch: the latch squeezed, the bar dropped in",
            """The big reverser lever: its latch squeezed out of the quadrant (a latch click), the bar's weight moved (a
            short heavy iron slide), and the latch dropping into the next notch with a heavy clack (heavy plate and metal
            hits pitched well down, choked).""", sources=LATCHES + PLATE_H + METAL_H, takes=3, lufs=-18)
    def reverser(rng, k):
        slide = ck.friction(rng, 0.14, 150, 400, 3000, 0.7, env([(0, 0), (0.03, 1), (0.14, 0)], 0.14))
        return cab(ck.place([(0, latch(rng, k, -4), -6), (0.06, slide, -12), (0.2, latch(rng, k + 1, -7), -4),
                             (0.205, iron(rng, k, -7, 0.04, 0.5), 0)]))

    @recipe(L, "brake-handle", "valve", "The brake valve handle moved: a brass handle turning to its next position",
            """The brass handle of the brake valve turned: a short squeak of brass on brass (a fast stick-slip through a
            small metal body) and the detent clicking into its position (the packs' latch click), small and close.""",
            sources=LATCHES, takes=3, lufs=-22)
    def brake_handle(rng, k):
        sq = synth.creak(0.12, rng.uniform(300, 500), rng, body=[1300, 2700, 4100], q=25) * env([(0, 0), (0.02, 1), (0.12, 0)], 0.12)
        return cab(ck.place([(0, sq, -10), (0.1, latch(rng, k + 1, 2), 0)]))

    @recipe(L, "brake-apply", "steam", "The steam brake applying, held: steam rushing to the brake cylinders",
            """Steam let into the brake: jet noise with a roar under its hiss (the pressure in it), heard through the
            valve and pipework (a little of the pipe's resonance), steady with a slow waver. Synthesised from the physics
            of a steam jet; one 8 s cycle.""", loop=True, takes=1, lufs=-22)
    def brake_apply(rng, k):
        s = steam(rng, 8.3, 0.7, (300, 6000))
        return cab(mix(s, dsp.resonate(s, [740, 1480], q=20) * 0.08), 0.08)

    @recipe(L, "handbrake-ratchet", "wheel", "A car's handbrake wheel turned one click",
            """The iron brake wheel heaved round a step: the staff creaking, the chain taking up round it (a couple of
            link clinks), and the pawl falling over one ratchet tooth with a heavy click (the packs' ratchet and a choked
            heavy metal hit under it). Outdoors on the car's end.""",
            sources=S("misc_20", "lock_open_01") + METAL_H + S("metal_02", "metal_06"), takes=4, lufs=-19)
    def hb_ratchet(rng, k):
        creak = synth.creak(0.2, rng.uniform(25, 45), rng, body=synth.IRON, q=14) * env([(0, 0), (0.04, 1), (0.2, 0.2)], 0.2)
        chain = mix(*[ck.place([(0.03 * i + rng.uniform(0, 0.02), dsp.vari(hit_of(S("metal_02", "metal_06")[(k + i) % 2], i, 0.08), -5), -3 * i)], 0.2)
                      for i in range(2)])
        pawl = mix(hit_of("sfx_100_v2:misc_20", 0, 0.12), iron(rng, k, -2, 0.02, 0.6) * 0.5)
        return ck.place([(0, creak, -10), (0.05, chain, -12), (0.17, pawl, 0)])

    @recipe(L, "handbrake-set", "wheel", "Handbrake wound tight: the chain taut, the last click, the rigging groaning",
            """The last hard heave: the chain creaking taut round the staff (a slow iron creak under strain), the pawl
            dropping into its last tooth (ratchet click and a heavy iron knock), and the brake rigging under the car
            groaning as the shoes bite.""", sources=S("misc_20") + METAL_H + PLATE_H, takes=3, lufs=-18)
    def hb_set(rng, k):
        strain = synth.creak(0.45, env([(0, 10), (0.45, 22)], 0.45), rng, body=synth.IRON, q=10) * env([(0, 0), (0.1, 1), (0.45, 0.4)], 0.45)
        groan = synth.creak(0.5, 8, rng, body=[70, 150, 260, 420], q=8) * env([(0, 0), (0.1, 1), (0.5, 0)], 0.5)
        pawl = mix(hit_of("sfx_100_v2:misc_20", 0, 0.12), iron(rng, k, -4, 0.03, 0.6))
        return ck.place([(0, strain, -6), (0.4, pawl, 0), (0.45, groan, -10)])

    @recipe(L, "handbrake-release", "wheel", "Handbrake knocked off: the pawl struck free, the wheel spinning, the chain running out",
            """The pawl knocked off with a boot (an iron clank), the wheel spinning back free with the ratchet chattering
            under it (the packs' ratchet clicks, fast and slowing), the chain running out round the staff, and the rigging
            dropping slack with a clunk.""", sources=S("misc_20", "lock_open_01") + METAL_H + PLATE_H, takes=3, lufs=-18)
    def hb_release(rng, k):
        teeth = [hit_of("sfx_100_v2:lock_open_01", i, 0.04, -14) for i in range(4)]
        parts = [(0, iron(rng, k, -3, 0.03, 0.6), 0)]
        t, gap = 0.08, 0.025
        while t < 0.75:
            parts.append((t, teeth[int(rng.integers(4))], -4 - 12 * t))
            t += gap
            gap *= 1.06
        chain = ck.grains(rng, 30, 0.6, 1500, 4500, q=(15, 30), length=(0.01, 0.03), shape=0.0)
        parts += [(0.1, chain, -14), (0.75, iron(rng, k + 2, -8, 0.05, 0.3), -6)]
        return ck.place(parts)

    @recipe(L, "sander-lever", "lever", "Sander lever pulled: a small lever and its valve",
            """A small lever pulled on the backhead: its catch clicking free (the packs' latch click), the lever's iron
            knock at the end of its travel, and the sand valve's dull thunk opening below.""",
            sources=LATCHES + METAL_H + PLATE_H, takes=3, lufs=-21)
    def sander(rng, k):
        return cab(ck.place([(0, latch(rng, k + 3), -6), (0.09, iron(rng, k, -1, 0.02, 0.5), -2),
                             (0.13, ck.pad(rng, 0.08, 250), -8)]))

    @recipe(L, "sand-flow", "sand", "Sand running down the sandpipe onto the rail, held",
            """Dry sand pouring through the sandpipe and onto the railhead: a dense stream of fine grains (thousands of tiny
            hard impacts a second, bright and dry) rung faintly through the iron pipe. Synthesised from the physics of
            grains; one 8 s cycle.""", loop=True, takes=1, lufs=-24)
    def sand(rng, k):
        L_ = 8.3
        n = samples(L_)
        fine = bp(synth.noise(L_, rng), 2000, 12000)
        grit = ck.grains(rng, int(900 * L_), L_, 2500, 9000, q=(1.5, 4), length=(0.003, 0.005), shape=0.0)[:n]
        w = np.clip(1 + 0.2 * lp(rng.standard_normal(n).astype(np.float32), 2) * 14, 0.6, 1.4)
        y = mix(ck.norm(fine) * 0.25, ck.norm(grit)) * w
        return ck.norm(mix(y, dsp.resonate(y, [1150, 2300, 3450], q=25) * 0.05))

    @recipe(L, "vent-handle", "handle", "The vent valve's handle thrown hard over",
            """A big valve handle thrown over hard: its spindle squealing a little (a fast iron stick-slip), the handle's
            weight thrown against its stop (a heavy iron clank, choked), in the cab.""",
            sources=METAL_H + PLATE_H, takes=3, lufs=-19)
    def vent(rng, k):
        sq = synth.creak(0.15, rng.uniform(150, 250), rng, body=[900, 1900, 3100], q=18) * env([(0, 0), (0.02, 1), (0.15, 0)], 0.15)
        return cab(ck.place([(0, sq, -10), (0.13, iron(rng, k + 1, -5, 0.035, 0.6), 0)]))

    @recipe(L, "whistle-cord", "cord", "The whistle cord pulled: the rope through its guides and the valve lever overhead",
            """The cord yanked down: the rope rubbing through its eye bolt (a short rough friction, cloth-dark), and the
            whistle valve's lever overhead knocking open (a small iron clack).""", sources=METAL_H + LATCHES, takes=3, lufs=-22)
    def cord(rng, k):
        rub = ck.friction(rng, 0.16, 140, 300, 2500, 0.8, env([(0, 0), (0.02, 1), (0.16, 0)], 0.16))
        return cab(ck.place([(0, rub, -2), (0.12, latch(rng, k, -6, 1), -8), (0.12, iron(rng, k, 0, 0.015, 0.6), -8)]))

    whistle_notes = (311.1, 392.0, 466.2)

    @recipe(L, "whistle-start", "chime", "The whistle speaking: the steam's rush and the three-chime chord rising into tune",
            """A three-chime steam whistle (E flat, G, B flat): the valve opening with a rush of steam (jet noise), then
            the three bells speaking, each a breathy harmonic tone with steam noise round it, scooping up a semitone into
            tune as the pressure comes up. Synthesised from the physics; outdoors above the cab. It ends at full voice for
            the held loop to take over.""", takes=3, lufs=-16)
    def w_start(rng, k):
        L_ = 0.9
        rush = steam(rng, L_, 0.3, (800, 8000)) * env([(0, 0), (0.04, 1), (0.15, 0.4), (L_, 0.3)], L_)
        tone = whistle_tone(rng, L_, rise=1.0 + 0.3 * k, notes=whistle_notes) * env([(0, 0), (0.06, 0), (0.25, 1), (L_, 1)], L_)
        # it hands over to the held loop, so it ends at full voice (the game crossfades) with only a short tail of its own
        y = dsp.room(mix(rush * 0.35, tone), "night", wet=0.12, rng=np.random.default_rng(9))
        return dsp.fade(y[:samples(L_ + 0.15)], 0.002, 0.15)

    @recipe(L, "whistle", "chime", "The three-chime whistle held",
            """The same three bells held: breathy harmonic tones fluttering with the turbulence of the jet, the steam's
            hiss under them, a slow waver in the pressure. One 8 s cycle, its echo folded round.""", loop=True, takes=1, lufs=-16)
    def w_hold(rng, k):
        L_ = 8.3
        y = mix(whistle_tone(rng, L_, notes=whistle_notes), steam(rng, L_, 0.2, (1000, 8000)) * 0.12)
        return dsp.wrap(dsp.room(y, "night", wet=0.12, rng=np.random.default_rng(9)), samples(L_))

    @recipe(L, "whistle-stop", "chime", "The whistle released: the chord sagging flat and dying into a hiss",
            """The cord let go: the three bells sagging flat as the pressure falls, the tone thinning to breath, and the
            last of the steam hissing off, with the country's echo after.""", takes=3, lufs=-18)
    def w_stop(rng, k):
        L_ = 0.8
        tone = whistle_tone(rng, L_, sag=1.2 + 0.3 * k, notes=whistle_notes) * env([(0, 1), (0.35, 0.7), (0.6, 0.1), (L_, 0)], L_)
        hiss = steam(rng, 1.0, 0.2, (1500, 8000)) * env([(0, 0.2), (0.4, 0.5), (1.0, 0)], 1.0)
        return dsp.room(mix(tone, hiss * 0.25), "night", wet=0.15, rng=np.random.default_rng(9))


# ---- The switch stand, the couplings ---------------------------------------------------------------------------------------

def switch():
    L = "crew-switch"

    @recipe(L, "lever-unlatch", "stand", "Switch stand lever unlatched: the iron latch lifted out of the keeper",
            """The lever's iron latch lifted out of its keeper at the stand's foot (a latch click and a heavy cast-iron
            clank, choked), the lever rocking a little in its socket. Outdoors.""",
            sources=LATCHES + PLATE_H + METAL_H, takes=3, lufs=-20)
    def unlatch(rng, k):
        return ck.place([(0, latch(rng, k, -6), -4), (0.03, iron(rng, k, -6, 0.03, 0.4), 0), (0.12, iron(rng, k + 1, -9, 0.02, 0.2), -14)])

    @recipe(L, "lever-throw", "stand", "The switch lever thrown over: the stand's crank grinding round, the weight landing",
            """The long lever heaved through its half-circle: the stand's crank and spindle grinding round (a slow iron
            stick-slip creak) and the lever's weight thudding down at the end of its travel (heavy plate pitched down,
            choked). Outdoors.""", sources=PLATE_H + METAL_H, takes=2, lufs=-18)
    def throw(rng, k):
        grind = synth.creak(0.7, env([(0, 15), (0.35, 35), (0.7, 20)], 0.7), rng, body=synth.IRON, q=8) * env([(0, 0), (0.1, 1), (0.7, 0.5)], 0.7)
        fr = ck.friction(rng, 0.7, 90, 200, 1800, 0.8, env([(0, 0), (0.1, 1), (0.7, 0.4)], 0.7))
        return ck.place([(0, grind, -6), (0, fr, -10), (0.7, iron(rng, k, -8, 0.05, 0.3), 0)])

    @recipe(L, "points-move", "blades", "The point blades sliding across their chairs and closing on the rail",
            """The two steel blades dragged across their iron slide chairs (a heavy, gritty steel-on-iron scrape, low and
            grinding, over most of a second) and the closing blade meeting the stock rail with a solid clunk (heavy plate
            and metal, choked). Outdoors.""", sources=PLATE_H + METAL_H, takes=2, lufs=-18)
    def points(rng, k):
        L_ = rng.uniform(0.75, 0.9)
        sh = env([(0, 0), (0.08, 1), (L_ * 0.8, 0.8), (L_, 0.3)], L_)
        scr = mix(ck.friction(rng, L_, 110, 200, 2500, 0.8, sh), ck.friction(rng, L_, 240, 1500, 6000, 0.6, sh) * 0.3)
        grit = ck.grains(rng, 40, L_, 1500, 6000, q=(3, 8), shape=0.0)
        return ck.place([(0, scr, 0), (0, grit, -16), (L_ - 0.02, iron(rng, k, -5, 0.05, 0.4), 0)])

    @recipe(L, "lever-latch", "stand", "The switch lever latched home: the latch dropping into its keeper",
            """The confirmation the road is set: the lever's iron latch dropped into its keeper (a bright latch click and
            a solid cast-iron clank on top of it). Outdoors.""", sources=LATCHES + PLATE_H + METAL_H, takes=3, lufs=-18)
    def latch_home(rng, k):
        return ck.place([(0, latch(rng, k + 1, -4), -2), (0.025, iron(rng, k + 2, -4, 0.04, 0.6), 0)])


def coupling():
    L = "crew-coupling"

    @recipe(L, "pin-lift", "pin", "The coupling pin lifted: the iron pin drawn up out of the coupler",
            """The lift lever heaved and the iron pin drawn up through its hole (a short iron-on-iron scrape), knocking
            against the side of the hole (two small iron knocks) and clinking free on its chain.""",
            sources=METAL_H + PLATE_H + S("metal_02", "metal_06"), takes=3, lufs=-20)
    def pin_lift(rng, k):
        scr = ck.friction(rng, 0.18, 200, 600, 4000, 0.7, env([(0, 0), (0.03, 1), (0.18, 0.2)], 0.18))
        clink = dsp.vari(hit_of(S("metal_02", "metal_06")[k % 2], 1, 0.1), -4)
        return ck.place([(0, iron(rng, k, -3, 0.02, 0.6), -6), (0.03, scr, -6), (0.15, iron(rng, k + 1, -2, 0.015, 0.6), -8),
                         (0.22, clink, -10)])

    @recipe(L, "knuckle-release", "knuckle", "The coupler knuckle swinging open as the cars part",
            """The heavy cast-steel knuckle swinging open on its pin (a heavy iron clank, pitched well down, choked), its
            lock rattling, and the slack coming out of the car with a low knock through the frame.""",
            sources=PLATE_H + METAL_H + MINING, takes=3, lufs=-17)
    def release(rng, k):
        sub = hit_of(MINING[k % 5], 0, 0.4)
        return ck.place([(0, iron(rng, k, -8, 0.06, 0.4), 0), (0.02, ck.grains(rng, 5, 0.1, 600, 2200, q=(8, 15)), -16),
                         (0.08, lp(sub, 300), -8)])

    @recipe(L, "hose-part", "hose", "Stand-in: the brake hoses pulled apart: rubber stretching, the glad hands parting",
            """Stand-in for a real rubber recording: the two hoses stretched (a low, damped rubbery creak: a slow
            stick-slip through a soft, heavily damped body), the iron glad hands twisting apart with a clank, and the freed
            hoses swinging down and slapping against the coupler (a soft heavy slap). No hiss, no pop.""",
            sources=METAL_H + PLATE_H + R("dropLeather"), takes=2, lufs=-19)
    def hose(rng, k):
        stretch = synth.creak(0.35, env([(0, 30), (0.35, 70)], 0.35), rng, body=[180, 340, 610], q=4, jitter=0.4) * env([(0, 0), (0.1, 1), (0.35, 0.6)], 0.35)
        slap = hit_of(R("dropLeather")[0], 0, 0.25)
        return ck.place([(0, lp(stretch, 1500), -6), (0.33, iron(rng, k, 2, 0.02, 0.8), -2), (0.5, slap, -8),
                         (0.56, slap * 0.5, -14)])

    @recipe(L, "knuckle-close", "knuckle", "Knuckles slamming together on a recouple: a massive iron bang, the slack running through",
            """Two couplers meeting at walking pace with a car's weight behind them: a massive iron bang (heavy plate and
            the packs' metal slam, pitched down; a mining hit's sub under it), the knuckle's lock dropping, and the slack
            running back through the next cars in two smaller clunks.""",
            sources=PLATE_H + S("metal_hit_01") + MINING + LATCHES, takes=3, lufs=-14)
    def close(rng, k):
        bang = mix(iron(rng, k, -7, 0.08, 0.5), dsp.vari(hit_of("sfx_100_v2:metal_hit_01", 0, 0.5), -4) * 0.6)
        sub = lp(hit_of(MINING[(k + 1) % 5], 0, 0.5), 250)
        return ck.place([(0, bang, 0), (0, sub, -4), (0.05, latch(rng, k, -5), -10),
                         (0.42, ck.norm(lp(iron(rng, k + 1, -9, 0.05, 0.3), 2500)), -12),
                         (0.78, ck.norm(lp(iron(rng, k + 2, -10, 0.05, 0.3), 1500)), -18)])

    @recipe(L, "pin-drop", "pin", "The coupling pin dropping home",
            """The iron pin let go and dropping down through the coupler (a short iron scrape), landing in its seat with a
            solid clank and a small rattle as it settles.""", sources=PLATE_H + METAL_H, takes=3, lufs=-19)
    def pin_drop(rng, k):
        scr = ck.friction(rng, 0.08, 300, 800, 4500, 0.6, env([(0, 0), (0.02, 1), (0.08, 0.5)], 0.08))
        return ck.place([(0, scr, -10), (0.07, iron(rng, k + 1, -3, 0.035, 0.6), 0), (0.16, iron(rng, k + 3, -1, 0.015, 0.6), -14),
                         (0.22, iron(rng, k + 4, 0, 0.01, 0.6), -20)])


doors()
fire()
shovel()
controls()
switch()
coupling()
