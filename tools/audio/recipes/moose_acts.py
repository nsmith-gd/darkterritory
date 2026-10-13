"""The Moose's acts (docs/design/creatures/moose.md §4-5): snagged in something narrower than its rack, searching for
whoever hid, ramming the car they're in, and bellowing after a passing train. Its tells (grazing, listening, warning,
squaring up, the charge) are in moose_tells.py.

What it is decides the build. A bull moose 2.4 m at the shoulder, in a rut that never ended:
- One throat, used for every vocal sound here: a glottis far bigger than a man's (55-130 Hz, a slow growl's fry in it,
  period doubling when it rages) through a tract longer than a horse's (formants a third of a child's, the tract
  moose_tells.py uses), with a moose's long nose on it (a nasal resonance near 230 Hz), a swollen dewlap ringing low under
  the voice, and breath blasting from nostrils half a metre up the muzzle. Its vowels are a moose's hollow 'mm-oh-wah',
  not a dog's or a man's 'ah'.
- The rack: 3.2 m of antler mineralised into something like slag, so every contact is stone, not bone: hard, dense,
  gritty, with crumbs breaking off it. Fence wire is caught in it, and twangs with every blow: the Moose's own signature
  in its knocks, its grinding and its thrashing.
- What it touches is the real material, as the foley brief asks: the shed's planks, a brick wall, a boxcar's iron-framed
  body heard from inside, the verge's brush and turf.

Kept apart from the other creatures' tells (spec A.1, A.4): the grinding is dry stone in quick yanks, held above the Car
Hugger's wet low heaves; the scrape on the car is one heavy low drag with rivets bumping slowly under it, not a
Climber's clatter or a Dragger's high rasp; the voice's rattle runs faster than the Ribbits' 18 Hz flutter. And apart
from its own tells, so they stay readable: the breath sniffs and blows but leaves the sharp fluttering snort to the
square-up, and nothing here is a short explosive grunt, which is the warning's cough.
"""

import functools

import numpy as np

import dsp
import src
import synth
from build import recipe
from dsp import samples, env, mix, Bus, lp, hp, bp
from recipes import crew_kit as ck
from recipes import kit
from recipes.beasts import fry, follow, ring_of, through, main_hit, unit
from recipes.boarders import cycle
from recipes.horrors import finish

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

CREAKS = src.R("creak1", "creak2", "creak3", "doorOpen_1", "doorOpen_2")     # real wood creaks, a door's and a floor's
STONES = src.S("stones_01", "stones_02", "stones_03")                        # real stones knocked and crunched
SPLIT = "sfx_100_v2:misc_35"                                                 # a real board cracking
WOOD_HEAVY = src.K("impactWood_heavy")                                       # heavy blows on timber
PLANK = src.K("impactPlank_medium")                                          # a plank's hollow knock
WALL_WOOD = WOOD_HEAVY + PLANK + src.S("wood_hit_01", "wood_hit_02")
MINING = src.K("impactMining")                                               # a pick into rock: brick's dead weight
CONCRETE = src.K("footstep_concrete")                                        # hard, dead, gritty contacts
STEEL_DOOR = "sfx_100_v2:metal_hit_01"                                       # a real blow on a steel sheet
PLATE_H = src.K("impactPlate_heavy")                                         # heavy plate: iron's mass
METAL_M = src.K("impactMetal_medium")
GRASS = src.K("footstep_grass")                                              # real steps in long grass
WHIP = src.S("switch_01", "switch_02")                                       # a switch swished: a branch whipping
LATCH = src.R("metalLatch", "metalClick")
BREAKS = src.S("misc_34", "misc_35")                                         # real wood cracking and breaking
GASPS = src.S("misc_05", "misc_25") + src.R("handleSmallLeather")           # a gasp, a sigh and groan, a leather breath


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def done(x):
    """Every take's last step: a 25 Hz highpass, so the thumps and booms leave no DC or rumble behind."""
    return hp(np.asarray(x, np.float32), 25, 2)


def outdoors(x, rng, wet=0.1, tail=0.8):
    """Open ground by the line or a stop: a little of the night's reverb, its tail cut short so a one-shot fired again
    doesn't pile up a wash."""
    y = dsp.room(x, "night", wet=wet, rng=rng)
    return dsp.fade(y[:len(x) + samples(tail)], 0.0, tail)


def inside_car(x, wet=0.2):
    """Heard from inside a boxcar: the wooden car's room, the same room every take."""
    return dsp.room(x, "car", wet=wet, rng=np.random.default_rng(11))


def close_loop(b, length, rng, wet=0.08):
    """A loop's bus folded to exactly one cycle: the room's tail and anything that ran over rings on into the head."""
    y = dsp.room(hp(b.x, 25), "night", wet=wet, rng=rng) if wet else hp(b.x, 25)
    return cycle(dsp.wrap(y, samples(length)))


def rec(rng, keys, semis=0.0, length=0.4, pre=0.002):
    """A recording from its loudest hit, varispeeded to size, cut short."""
    k = keys[int(rng.integers(len(keys)))]
    return unit(dsp.vari(main_hit(k, length, pre), semis))


@functools.lru_cache(maxsize=8)
def stone_grains():
    """Every sharp contact in the real stone recordings, cut to 25 ms: stone striking stone, which is what the slag rack
    is against anything hard."""
    out = []
    for k in STONES:
        x = ck.get(k)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.012, prom=4):
            out.append(unit(ck.cut(x, s - samples(0.0005), s + samples(0.025), 0.0003, 0.012)))
    return out


def grit(rng, length, rate, semis=(-8, -2), shape=None):
    """Mineral crumbs breaking off the rack as it grinds: real stone contacts, dropped to the size of the rack's grit,
    `rate` a second (a constant or a curve)."""
    g = stone_grains()
    n = samples(length)
    rc = synth.curve(rate, n)
    b = Bus(length + 0.05)
    t = rng.exponential(1 / max(rc[0], 1))
    while t < length:
        x = dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(*semis))
        b.at(t, x, 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5))
        t += rng.exponential(1 / max(rc[min(samples(t), n - 1)], 1))
    y = dsp.fit(b.x, n)
    if shape is not None:
        y = y * synth.curve(shape, n)
    return unit(y)


def wire(rng, f=None, length=0.6, buzz=0.35):
    """Fence wire caught in the rack, struck when the rack is: a stiff steel strand's partials (each a little sharp of a
    string's, the stiffness), buzzing where the loose end lies against the antler."""
    f = f or rng.uniform(150, 260)
    n = samples(length)
    x = np.zeros(n, np.float32)
    k = samples(0.0015)
    x[:k] = rng.standard_normal(k) * np.linspace(1, 0, k)
    parts = [f * h * (1 + 0.0022 * h * h) for h in range(1, 14)]
    y = dsp.resonate(x, parts, q=rng.uniform(140, 220), gains=[1 / h ** 0.6 for h in range(1, 14)])
    y = unit(y)
    rattle = dsp.fold(y * 2.5, 1.2) * np.clip(follow(y, 5) * 1.5, 0, 1)
    return unit(mix(y, hp(rattle, 600) * buzz)) * env([(0, 1), (length, 0.003)], length, "exp")


def bend(x, semis):
    """Varispeed along a curve of semitones (per output sample): a creak's pitch climbing as the pressure comes on."""
    c = synth.curve(semis, len(x))
    n = int(len(x) * 2 ** (max(-float(np.min(c)), 0) / 12)) + 2
    r = 2 ** (synth.curve(c, n) / 12)
    pos = np.concatenate([[0.0], np.cumsum(r)[:-1]])
    pos = pos[pos < len(x) - 1]
    return np.interp(pos, np.arange(len(x)), x).astype(np.float32)


# ---- The throat ----------------------------------------------------------------------------------------------------------

TRACT = 0.34            # a bull moose's vocal tract as a share of a child's (synth.TRACT), as in moose_tells.py
NOSE = [(680, 4, 0.55)]  # its muzzle: a nasal resonance (680 x 0.34 = 230 Hz)


def voice(rng, pts, length, vowels, shape, rage=0.5, sub=0.35, quaver=0.0, crack=None):
    """One breath of the Moose's voice. Pitch from `pts` [(t, Hz)], vowels through its long tract and long nose,
    `shape` its envelope. `rage` 0-1: how much a slow growl's fry gates the voice and how hard it's driven. `quaver` a
    moose's wavering moan (a fraction of pitch). `crack` (t, length): the voice breaking up a fifth, the corruption."""
    n = samples(length)
    sh = dsp.fit(env(shape, length), n)
    f0 = env(pts, length, "exp")
    t = np.arange(n) / SR
    if quaver:
        rate = 5.2 + 1.5 * lp(rng.standard_normal(n).astype(np.float32), 1.0) * 30
        f0 = f0 * (1 + quaver * np.sin(2 * np.pi * np.cumsum(rate) / SR) * np.clip(t / 0.5, 0, 1))
    if crack:
        c0, cl = crack
        f0 = f0 * 2 ** (7 / 12 * env([(0, 0), (c0, 0), (c0 + 0.015, 1), (c0 + cl, 1), (c0 + cl + 0.02, 0), (length, 0)], length))
    g = synth.glottis(f0.astype(np.float32), length, rng, jitter=0.004 + 0.008 * rage, shimmer=0.1 + 0.15 * rage,
                      sub=sub, oq=0.75 - 0.2 * rage)      # pressed harder, brighter, the angrier it is
    # a huge larynx's rattle, 24-38 pulses a second: kept off the Ribbits' 18 Hz flutter (their tell)
    fr = fry(rng, 24 + 14 * sh, length, jitter=0.4, decay=0.02)
    a = 0.2 + 0.5 * rage
    rms = float(np.sqrt(np.mean(g ** 2)))
    x = g * (1 - a + a * fr) + synth.noise(length, rng) * fr * rms * 0.9 * rage
    v = unit(synth.tract(x, vowels, TRACT, breath=0.1, rng=rng, extra=NOSE) * sh)
    # the dewlap: a long sac of air and fluid under the throat, booming with the voice
    sac = dsp.resonate(v, [rng.uniform(70, 80), rng.uniform(150, 180)], q=7)
    # the nostrils: breath blasting through a muzzle half a metre long
    e = follow(v, 8)
    nose = dsp.resonate(bp(synth.noise(length, rng, "pink"), 300, 3000), [rng.uniform(700, 900), rng.uniform(1500, 1900)], q=3)
    y = mix(v, unit(sac) * 0.35, unit(nose) * e ** 1.3 * (0.06 + 0.1 * rage))
    return lp(dsp.saturate(unit(y) * 0.8, 2 + 6 * rage), 8000)


def nostrils(rng, length, out=True, flutter=0.5, wet=0.3, attack=0.03):
    """Air through the nostrils alone: drawn in (`out` False: rising, the passages narrowing) or blown out, the flaps
    fluttering in the blast (a growl's pulses gating the noise, slow and uneven), wet with what's in them."""
    n = samples(length)
    sh = env([(0, 0), (attack, 1), (max(length * 0.45, attack + 0.01), 0.7), (length, 0)] if out else
             [(0, 0), (length * 0.75, 1), (length, 0)], length)
    lo, hi = (380, 1100) if out else (700, 1600)
    fc = env([(0, lo), (length, hi * 0.7 if out else hi)], length, "exp")
    air = dsp.sweep_filter(synth.noise(length, rng, "pink"), "bp", fc, q=1.4)
    passages = dsp.resonate(synth.noise(length, rng), [rng.uniform(800, 1000), rng.uniform(1700, 2100)], q=4)
    air = mix(unit(air), unit(passages) * 0.4)
    if flutter:
        fl = fry(rng, rng.uniform(26, 36), length, jitter=0.5, decay=0.015)
        air = air * (1 - flutter + flutter * fl)
    mucus = lp(synth.crackle(length, 120 + 300 * sh, rng, size=(0.0002, 0.0008), hi=900), 3000) * wet
    return unit(lp(dsp.fit(mix(unit(air) * sh[:n], unit(mucus) * sh[:n] * 0.5), n), 5000))


# ---- cs-moose-snag: jammed in something narrower than itself for 4 s --------------------------------------------------------

def timber_groan(rng, k):
    """Real wood creaks (a door's and a floor's) dropped well over an octave to a door frame's size, pitch climbing with
    the pressure, three at once like the frame's joints; the frame's bulk flexing under them; fibres ticking."""
    L = rng.uniform(1.7, 2.4)
    b = Bus(L + 0.8)
    pk = L * rng.uniform(0.5, 0.7)
    press = env([(0, 0.15), (pk, 1.0), (L * 0.85, 0.75), (L, 0.0)], L)
    for i in range(3):
        key = CREAKS[(k + 2 * i) % len(CREAKS)]
        x = dsp.trim_silence(src.get(key), -40)
        x = dsp.vari(x, rng.uniform(-15, -11))
        x = dsp.stretch(x, L * rng.uniform(0.6, 0.9) / (len(x) / SR), smooth=True)
        x = bend(x, env([(0, -1.5), (len(x) / SR * 0.6, 1.0), (len(x) / SR, 0.2)], len(x) / SR))
        x = lp(x, 2200)
        at = rng.uniform(0, L * 0.35)
        b.at(at, unit(x) * dsp.fit(press[samples(at):], len(x)), -3 - 3 * i)
    flex = lp(synth.noise(L, rng, "brown"), 140) * press ** 2
    b.at(0, unit(flex), -14)
    fib = synth.crackle(L, 6 + 50 * press, rng, size=(0.0004, 0.002), hi=700)
    b.at(0, unit(lp(fib, 4000)), -20)
    if k != 1:   # the wood gives a little: one fibre-deep crack, not a break
        c = dsp.vari(main_hit(SPLIT, 0.25), rng.uniform(-7, -4))
        b.at(pk + rng.uniform(-0.1, 0.2), unit(lp(c, 3500)), -12)
    return done(outdoors(b.x, rng, 0.12))


@recipe("cs-moose-snag", "groan", "timber",
        "A door frame groaning under the rack: real wood creaks dropped to a frame's size, the pressure in them",
        """Three real wood creaks (Kenney's creaks and door-hinge groans) dropped 11-15 semitones to the size of a door
        frame's posts and lintel and stretched under the pressure: they groan at once, like the frame's joints, the pitch
        climbing as the rack bears on them and sagging as it lets off. Under them the frame's bulk flexing (a low brown
        rumble that swells with the pressure) and wood fibres ticking. Two takes end with one fibre-deep crack (a real
        board's crack, lowered): it gives, but nothing breaks. Outdoors, by a building.""",
        sources=CREAKS + [SPLIT], takes=3)
def snag_groan_timber(rng, k):
    return timber_groan(rng, k)


def strain_groan(rng, k):
    """Big timbers slipping against each other under the rack: stick-slip through a frame post's modes, catching and
    quickening into a groan; a second joint answering higher; the frame's joint knocking as it shifts."""
    L = rng.uniform(1.6, 2.3)
    b = Bus(L + 0.8)
    pk = L * rng.uniform(0.45, 0.7)
    rate = env([(0, 7), (pk * 0.5, 30), (pk, rng.uniform(90, 130)), (L * 0.85, 45), (L, 8)], L)
    post = synth.creak(L, rate, rng, body=[m * rng.uniform(0.5, 0.58) for m in synth.WOOD], q=9, jitter=0.45, grit=0.5)
    shape = env([(0, 0), (0.1, 0.6), (pk, 1), (L * 0.85, 0.7), (L, 0)], L)
    b.at(0, post * shape, -2)
    joint = synth.creak(L, rate * rng.uniform(1.5, 1.9), rng, body=[m * rng.uniform(0.7, 0.85) for m in synth.WOOD], q=14,
                        jitter=0.35, grit=0.3)
    b.at(rng.uniform(0.05, 0.25), joint * shape, -10)
    fib = synth.crackle(L, 10 + 60 * shape, rng, size=(0.0004, 0.002), hi=700)
    b.at(0, unit(lp(fib, 4000)), -19)
    b.at(0, unit(lp(synth.noise(L, rng, "brown"), 120) * shape ** 2), -19)
    knock = rec(rng, WALL_WOOD, rng.uniform(-6, -3), 0.3)
    b.at(pk * rng.uniform(0.8, 1.1), ck.choke(lp(knock, 3000), 0.03, 0.06), -6)
    return done(outdoors(b.x, rng, 0.12))


@recipe("cs-moose-snag", "groan", "strain",
        "A fence post or frame straining: big timbers slipping against each other, catching and quickening into a groan",
        """Modelled rather than recorded: stick-slip friction (a pulse each time the joint slips) rung through a heavy
        timber post's modes, catching slowly at first and quickening into a groan as the rack bears down, then easing; a
        second joint answers higher and smaller, and the frame's joint knocks once as it shifts (a real heavy wood blow,
        lowered and choked). Fibres tick through it and the bulk of the frame flexes underneath. Outdoors.""",
        sources=WALL_WOOD, takes=3)
def snag_groan_strain(rng, k):
    return strain_groan(rng, k)


def rasp_wood(rng, length, rate=(140, 220)):
    """Stone dragged across timber: stick-slip bursts through a board's modes, rough and dark."""
    fr = ck.friction(rng, length, rng.uniform(*rate), 350, 3200, rough=0.7)
    return unit(mix(fr * 0.6, unit(dsp.resonate(fr, [m * rng.uniform(0.7, 0.9) for m in synth.WOOD], q=8)) * 0.7))


def screech_iron(rng, length):
    """Stone dragged across an iron strap or hinge: the same stick-slip, ringing the iron's own recorded modes."""
    fr = ck.friction(rng, length, rng.uniform(80, 130), 300, 2600, rough=0.5)
    ring = dsp.vari(ring_of(STEEL_DOOR, 0.5), rng.uniform(-5, -2))
    return unit(mix(fr * 0.4, through(fr, ring)))


def hoof_dig(rng):
    """A hoof driven into the ground for purchase: the weight, real grass and turf giving, dirt kicked."""
    b = Bus(0.6)
    b.at(0, kit.thud(rng, rng.uniform(50, 65), 0.25, 1.0), -4)
    b.at(0.004, rec(rng, GRASS, rng.uniform(-6, -3), 0.35), -6)
    b.at(0.02, unit(kit.gravel(rng, 8, 0.12, 700, 3500, body=0)), -16)
    return b.x


def haul(rng, length):
    """One yank of the head against the jam: the timber creaking as the pressure comes on, the rack slipping a few
    centimetres with a burst of stone grinding on wood (and on an iron strap, some yanks), grit crumbling, a hoof dug in."""
    b = Bus(length + 1.0)
    pk = length * rng.uniform(0.4, 0.75)
    cr = synth.creak(length, env([(0, 10), (pk, 50), (length, 18)], length), rng,
                     body=[m * rng.uniform(0.42, 0.5) for m in synth.WOOD], q=9, jitter=0.45, grit=0.5)
    b.at(0, cr * env([(0, 0), (pk, 1), (length, 0.2)], length), -12)
    G = rng.uniform(0.22, 0.45)
    sh = [(0, 0), (0.015, 1), (G * 0.6, 0.7), (G, 0)]
    b.at(pk, dsp.shaped(rasp_wood(rng, G), sh), -2)
    b.at(pk, grit(rng, G + 0.1, env([(0, 90), (G, 40), (G + 0.1, 5)], G + 0.1)), -7)
    if rng.random() < 0.5:
        b.at(pk + rng.uniform(0.0, 0.05), dsp.shaped(screech_iron(rng, G * 0.8), sh), -8)
    if rng.random() < 0.35:
        b.at(pk + G * 0.5, wire(rng, length=0.5), -17)
    if rng.random() < 0.6:
        b.at(rng.uniform(0, 0.12), hoof_dig(rng), -9)
    return b.x


def grind_haul(rng):
    """Yanks of half a second to a second at uneven gaps, one 12 s cycle."""
    L = 12.0
    b = Bus(L + 2.0)
    t = 0.1
    while t < L:
        Y = rng.uniform(0.5, 1.1)
        b.at(t, haul(rng, Y), rng.uniform(-3, 0))
        t += Y + rng.uniform(0.2, 0.6)
    return close_loop(b, L, rng)


@recipe("cs-moose-snag", "grind", "haul",
        "The rack hauled against the jam in uneven yanks: stone grinding on timber and an iron strap, grit crumbling",
        """Each yank (half a second to a second, uneven pauses between) is the head hauling back: the timber creaking as
        the pressure comes on, then the rack slipping a few centimetres with a burst of stone grinding on wood (stick-slip
        rung through a board's modes) and, some yanks, on an iron strap (the same stick-slip rung through a real steel
        sheet's ring), real stone contacts crumbling off the slag rack, fence wire twanging, a hoof driven into the
        ground for purchase. Dry and mineral and quick, so it can't be the Car Hugger's wet low heaves. A 12 s loop.""",
        sources=STONES + [STEEL_DOOR] + GRASS, takes=1, loop=True, seconds=12)
def snag_grind_haul(rng, k):
    return grind_haul(rng)


def grind_wrench(rng):
    """Quick side-to-side tosses of the head, frantic: every toss a short scrape that ends with a tine banging the frame,
    the grit never stopping."""
    L = 10.0
    b = Bus(L + 2.0)
    t = 0.05
    side = 0
    while t < L:
        T = rng.uniform(0.32, 0.52)
        G = T * rng.uniform(0.55, 0.8)
        sh = [(0, 0), (0.03, 1), (G * 0.7, 0.8), (G, 0)]
        if side == 0 or rng.random() < 0.3:
            b.at(t, dsp.shaped(rasp_wood(rng, G, (180, 260)), sh), rng.uniform(-4, -1))
            b.at(t + G, ck.choke(rec(rng, WALL_WOOD, rng.uniform(-5, -2), 0.25), 0.02, 0.05), rng.uniform(-9, -5))
        else:
            b.at(t, dsp.shaped(screech_iron(rng, G), sh), rng.uniform(-8, -5))
            b.at(t + G, ck.choke(rec(rng, PLATE_H, rng.uniform(-6, -3), 0.25), 0.015, 0.05), rng.uniform(-13, -9))
        if rng.random() < 0.2:
            b.at(t + G, wire(rng, length=0.45), -18)
        side ^= 1
        t += T
    b.at(0, grit(rng, L, 60), -12)
    for _ in range(4):
        b.at(rng.uniform(0, L), hoof_dig(rng), -11)
    return close_loop(b, L, rng)


@recipe("cs-moose-snag", "grind", "wrench",
        "The rack wrenched side to side in the jam, frantic: short stone scrapes, a tine banging the frame each toss",
        """A different performance from 'haul': the head thrown side to side two or three times a second. Each toss is a
        short scrape of stone on timber or on an iron strap (stick-slip through the board's modes, or through a real
        steel sheet's ring) ending in a tine banging the frame (real heavy wood and plate blows, choked), the grit of the
        slag rack crumbling all the while, fence wire twanging now and then, hooves stamping for purchase. A 10 s loop.""",
        sources=STONES + [STEEL_DOOR] + WALL_WOOD + PLATE_H + GRASS, takes=1, loop=True, seconds=10)
def snag_grind_wrench(rng, k):
    return grind_wrench(rng)


def intake(rng, length=0.35):
    """The breath dragged in before it bellows: through the nostrils, hard."""
    return nostrils(rng, length, out=False, flutter=0.2, wet=0.4)


def roar(rng, k):
    """One long bellow of rage: a nasal 'mm' tearing open into 'oh-wah', the pitch climbing, wavering, the voice cracking
    up a fifth for a moment, then falling away into a fry."""
    L = rng.uniform(1.9, 2.6)
    top = rng.uniform(108, 125)
    pts = [(0, 60), (0.15, 88), (L * 0.4, top), (L * 0.65, top * rng.uniform(0.95, 1.05)), (L * 0.88, 88), (L, 52)]
    vow = [(0, "m"), (0.14, "o"), (L * 0.35, "a"), (L * 0.7, "o"), (L, "u")]
    shape = [(0, 0), (0.05, 0.6), (L * 0.3, 1), (L * 0.75, 0.9), (L * 0.93, 0.5), (L, 0)]
    crack = (L * rng.uniform(0.5, 0.62), rng.uniform(0.1, 0.18)) if k != 2 else None
    v = voice(rng, pts, L, vow, shape, rage=0.85, sub=0.5, quaver=0.025, crack=crack)
    b = Bus(L + 1.2)
    i = rng.uniform(0.3, 0.42)
    b.at(0, intake(rng, i), -10)
    b.at(i + 0.04, v, 0)
    return done(outdoors(b.x, rng, 0.12))


@recipe("cs-moose-snag", "bellow", "roar",
        "One long bellow of rage: a huge nasal throat tearing open, wavering, cracking up a fifth, falling into a fry",
        """The Moose's one throat, synthesised: a glottis far bigger than a man's (60-125 Hz, a slow fry rattling in it,
        period doubling for the rage) through a vocal tract longer than a horse's with a moose's long nose (a nasal
        resonance near 230 Hz) and a swollen dewlap booming under it, breath blasting from the nostrils. It drags a
        breath in, then bellows: a closed 'mm' tearing open into 'oh-wah', the pitch climbing and wavering like a moose's
        moan, the voice cracking up a fifth for a moment (the corruption), then falling away into a rattling 'uh'. Huge
        and close.""",
        takes=3)
def snag_bellow_roar(rng, k):
    return roar(rng, k)


def heaves(rng, k):
    """The rage in heaves, in time with the hauls: a bellow forced out, a rough groan dragged back in through the throat
    (voiced both ways, like a bray), two or three times, then one long bellow out. Each part swells in: never the short
    explosive grunt that is the warning's tell."""
    b = Bus(5.0)
    t = 0.0
    for i in range((2, 3, 2)[k]):
        L = rng.uniform(0.45, 0.65)
        f = rng.uniform(78, 92) * (1 + 0.05 * i)
        out = voice(rng, [(0, f * 0.85), (L * 0.4, f * 1.1), (L, f * 0.8)], L, [(0, "o"), (L * 0.5, "a"), (L, "o")],
                    [(0, 0), (0.07, 1), (L * 0.7, 0.8), (L, 0)], rage=0.8, sub=0.5)
        b.at(t, out, 0)
        t += L - 0.03
        Li = rng.uniform(0.3, 0.42)
        fi = f * rng.uniform(1.7, 2.0)
        gin = voice(rng, [(0, fi * 0.9), (Li, fi * 1.12)], Li, [(0, "u"), (Li, "o")],
                    [(0, 0), (Li * 0.7, 1), (Li, 0)], rage=1.0, sub=0.6)
        b.at(t, mix(gin, intake(rng, Li) * 0.5), -6)
        t += Li + rng.uniform(0.0, 0.05)
    L = rng.uniform(1.0, 1.4)
    top = rng.uniform(100, 118)
    v = voice(rng, [(0, 80), (L * 0.3, top), (L * 0.7, top * 0.9), (L, 55)], L,
              [(0, "o"), (L * 0.25, "a"), (L * 0.75, "o"), (L, "u")],
              [(0, 0), (0.06, 1), (L * 0.6, 0.85), (L, 0)], rage=0.9, sub=0.55, quaver=0.03)
    b.at(t, v, 0)
    return done(outdoors(dsp.trim_silence(b.x, -60), rng, 0.12))


@recipe("cs-moose-snag", "bellow", "heaves",
        "The rage in heaves, like a bray: a bellow forced out, a rough groan dragged back in, then one long bellow",
        """The same synthesised throat as 'roar', in a different performance: the bellow comes in heaves, in time with
        the head hauling at the jam. A bellow forced out on 'oh-ah', then a rough groan dragged back in through the
        throat an octave higher (voiced on the in-breath as well as the out, like a donkey's bray, with the nostrils
        drawing hard), two or three times, then one long bellow out that wavers and doubles. Every part swells in, so
        none of it is the short explosive grunt of the warning's tell. Huge and close, outdoors.""",
        takes=3)
def snag_bellow_heaves(rng, k):
    return heaves(rng, k)


# ---- cs-moose-search: a few metres from whoever hid, for up to 25 s -------------------------------------------------------

def sniffs(rng, count, rate=(4.0, 6.0)):
    """A run of sniffs: short sharp draws through the nostrils, each a little different."""
    b = Bus(count / rate[0] + 0.4)
    t = 0.0
    for i in range(count):
        L = rng.uniform(0.09, 0.15)
        b.at(t, nostrils(rng, L, out=False, flutter=0.15, wet=0.5), -1.5 * i * rng.uniform(0, 1) + rng.uniform(-2, 0))
        t += 1 / rng.uniform(*rate)
    return b.x


def blow(rng, length, voiced=0.0):
    """A heavy breath out through the nostrils, swelling in and the flaps barely fluttering (a sharp fluttering blast is
    the square-up's snort, a tell); a low hum of the throat under it if `voiced`."""
    y = nostrils(rng, length, out=True, flutter=0.22, wet=0.35, attack=rng.uniform(0.12, 0.2))
    if voiced:
        f = rng.uniform(52, 62)
        v = voice(rng, [(0, f), (length * 0.4, f * 1.08), (length, f * 0.85)], length, [(0, "m"), (length, "u")],
                  [(0, 0), (0.08, 1), (length * 0.6, 0.6), (length, 0)], rage=0.2, sub=0.2)
        y = mix(y, unit(v) * voiced)
    return y


def head_sweep(rng):
    """The rack swung as it sweeps its head: fence wire and the junk in the tines jingling faintly, a tine brushing
    something."""
    b = Bus(0.9)
    b.at(0, wire(rng, length=0.6, buzz=0.5), -6)
    b.at(rng.uniform(0.05, 0.2), wire(rng, length=0.4, buzz=0.6), -10)
    b.at(rng.uniform(0.0, 0.3), unit(lp(grit(rng, 0.2, 40, (-4, 0)), 3000)), -14)
    return b.x


def breath_nostrils(rng):
    """Sniffs, draws and blows in one 12 s breathing cycle, the rack's wire jingling three times as the head sweeps."""
    L = 12.0
    b = Bus(L + 2.0)
    seq = [("sniffs", 4), ("blow", 0.8), ("in", 0.95), ("blow", 1.1, 0.35), ("sniffs", 3), ("sniffs", 5),
           ("blow", 0.7), ("in", 1.05), ("blow", 1.3, 0.5), ("sniffs", 2)]
    t = 0.15
    for s in seq:
        if s[0] == "sniffs":
            x = sniffs(rng, s[1])
            b.at(t, x, -4)
            t += s[1] * 0.2 + rng.uniform(0.1, 0.3)
        elif s[0] == "in":
            b.at(t, nostrils(rng, s[1], out=False, flutter=0.25, wet=0.3), -7)
            t += s[1] + rng.uniform(0.05, 0.15)
        else:
            b.at(t, blow(rng, s[1], s[2] if len(s) > 2 else 0.0), 0)
            t += s[1] + rng.uniform(0.25, 0.45)
    for at in (2.4, 6.6, 10.2):
        b.at(at + rng.uniform(-0.3, 0.3), head_sweep(rng), -16)
    return close_loop(b, L, rng, 0.06)


@recipe("cs-moose-search", "breath", "nostrils",
        "Heavy breathing and sniffing through a huge muzzle, close: runs of sniffs, deep draws, long slow blows out",
        """Its breath alone, a few metres away, synthesised: air through nostrils half a metre up a moose's muzzle (noise
        through the nose's resonances, the flaps fluttering in the blast like a slow growl's pulses, wet crackle of what's
        in them). Runs of two to five quick sniffs, deep draws, and heavy blows out, two of them with the throat humming
        low under them; now and then the fence wire in its rack jingles as it swings its head. The blows swell in slowly
        with the flaps barely fluttering: the sharp fluttering snort stays the square-up's tell. A 12 s loop, one
        breathing cycle long.""",
        takes=1, loop=True, seconds=12)
def search_breath_nostrils(rng, k):
    return breath_nostrils(rng)


def gasp(rng, length, out=True, semis=(-17, -13)):
    """A real breath (a gasp, a sigh) dropped over an octave to a moose's lungs and stretched to `length`; drawn in or
    blown out."""
    key = GASPS[int(rng.integers(len(GASPS)))]
    x = dsp.trim_silence(src.get(key), -40)
    x = dsp.vari(x, rng.uniform(*semis))
    x = dsp.stretch(x, length / (len(x) / SR), smooth=True)
    x = dsp.fit(unit(lp(hp(x, 120), 3800)), samples(length))
    if not out:
        x = dsp.reverse(x)
    sh = [(0, 0), (0.02, 1), (length * 0.5, 0.7), (length, 0)] if out else [(0, 0), (length * 0.8, 1), (length, 0)]
    return dsp.shaped(x, sh)


def breath_lungs(rng):
    """The same breathing pattern built from real breath: gasps and sighs dropped to a moose's size, the sniffs cut short
    from them, the blows with the flaps fluttering."""
    L = 12.0
    b = Bus(L + 2.0)
    t = 0.15
    plan = [("s", 4), ("o", 0.9), ("i", 1.0), ("o", 1.2), ("s", 3), ("s", 5), ("o", 0.8), ("i", 1.1), ("o", 1.4), ("s", 2)]
    for kind, v in plan:
        if kind == "s":
            for i in range(v):
                Ls = rng.uniform(0.1, 0.16)
                x = gasp(rng, Ls, out=False, semis=(-9, -6))
                b.at(t, dsp.shaped(x, [(0, 0), (Ls * 0.7, 1), (Ls, 0)]), -3 - rng.uniform(0, 3))
                t += 1 / rng.uniform(4.0, 6.0)
            t += rng.uniform(0.15, 0.35)
        elif kind == "i":
            b.at(t, gasp(rng, v, out=False), -6)
            t += v + rng.uniform(0.05, 0.15)
        else:
            x = gasp(rng, v, out=True)
            fl = fry(rng, rng.uniform(26, 36), v, jitter=0.5, decay=0.015)
            x = dsp.shaped(x * (0.78 + 0.22 * fl), [(0, 0), (rng.uniform(0.12, 0.2), 1)])
            if v > 1.1:
                f = rng.uniform(52, 60)
                x = mix(unit(x), unit(voice(rng, [(0, f), (v * 0.4, f * 1.08), (v, f * 0.85)], v, [(0, "m"), (v, "u")],
                                            [(0, 0), (0.1, 1), (v * 0.6, 0.6), (v, 0)], rage=0.2, sub=0.2)) * 0.4)
            b.at(t, x, 0)
            t += v + rng.uniform(0.25, 0.45)
    for at in (2.4, 6.6, 10.2):
        b.at(at + rng.uniform(-0.3, 0.3), head_sweep(rng), -16)
    return close_loop(b, L, rng, 0.06)


@recipe("cs-moose-search", "breath", "lungs",
        "The same close breathing made from real breath: gasps and sighs dropped over an octave to a moose's lungs",
        """Real breath recordings (a gasp, a sigh and a groan, a leather creak like a breath) dropped 13-17 semitones and
        stretched to the size of a moose's lungs: drawn in reversed, blown out swelling in with the flaps barely fluttering,
        runs of sniffs cut short from the same gasps a little less lowered, the throat humming low under the two longest
        blows. The same pattern as 'nostrils', so the two compare as model against recording. No sharp snort (the
        square-up's tell). A 12 s loop.""",
        sources=GASPS, takes=1, loop=True, seconds=12)
def search_breath_lungs(rng, k):
    return breath_lungs(rng)


def tines(rng, n=None, spread=(0.008, 0.04)):
    """The rack meeting something hard: two to four tines landing in a ragged flam, each stone on stone (a real stone's
    contact, lowered to a tine's size) with a dense, short knock in it."""
    n = n or int(rng.integers(2, 5))
    g = stone_grains()
    b = Bus(0.3)
    t = 0.0
    for i in range(n):
        x = dsp.vari(g[int(rng.integers(len(g)))], rng.uniform(-9, -5))
        tok = synth.click(rng.uniform(700, 1200), q=rng.uniform(5, 9), length=0.04, rng=rng)
        b.at(t, mix(unit(x), unit(tok) * 0.6), -4 * i + rng.uniform(-2, 0))
        t += rng.uniform(*spread)
    return b.x


KNOCK_LUFS = -27.0    # all attack: a quieter integrated loudness leaves the knocks their peaks
PEAK = -1.5           # impacts are levelled with a look-ahead limiter (horrors.finish), not build.level's soft clip


def knock_plank(rng, k):
    """The rack swung into a shed's plank wall: the tines' stone flam, the boards' real heavy blow, the wall's hollow,
    the boards rattling on their nails, wire twanging in the rack."""
    b = Bus(1.4)
    b.at(0, tines(rng), -2)
    b.at(0.004, ck.choke(rec(rng, [WOOD_HEAVY[k % 5]], rng.uniform(-4, -1), 0.3), 0.06, 0.05), 0)
    b.at(0.006, ck.choke(rec(rng, [PLANK[(k + 2) % 5]], rng.uniform(-3, 0), 0.5), 0.08, 0.06), -6)
    b.at(0.004, ck.hollow(rng, [rng.uniform(62, 72), 104, 150, 228], 0.3, q=4), -9)
    b.at(0.004, kit.thud(rng, rng.uniform(50, 60), 0.18, 1.0), -12)
    rat = Bus(0.4)
    for j in range(int(rng.integers(3, 6))):
        rat.at(j / rng.uniform(18, 30), synth.click(rng.uniform(500, 900), q=6, length=0.03, rng=rng), -3 * j)
    b.at(0.05, unit(lp(rat.x, 2500)), -18)
    if k % 2 == 0:
        b.at(rng.uniform(0.01, 0.03), wire(rng), -15)
    return finish(outdoors(b.x, rng, 0.1, 0.5), KNOCK_LUFS, PEAK)


@recipe("cs-moose-search", "knock", "plank",
        "The rack knocking on a plank wall: stone-hard tines in a ragged flam, the boards' heavy blow, the shed booming",
        """Two to four tines landing at once in a ragged flam, each a real stone's contact dropped to a tine's size with a
        dense short knock in it (the rack is mineral, so it clacks like stone, not bone), on a shed's plank wall: a real
        heavy wood blow and a plank's hollow knock under it, the shed's hollow booming low, the boards rattling on their
        nails, the weight of the head. Fence wire caught in the rack twangs on two takes. Outdoors.""",
        sources=STONES + WOOD_HEAVY + PLANK, takes=4, lufs=KNOCK_LUFS,
        preview=lambda takes, rng: kit.scatter(takes, rng, (0.6, 1.6)))
def search_knock_plank(rng, k):
    return knock_plank(rng, k)


def knock_brick(rng, k):
    """The rack swung into a brick wall: the tines' stone flam, the brick's dead hard weight, mortar crumbling after."""
    b = Bus(1.4)
    b.at(0, tines(rng), -1)
    b.at(0.003, ck.choke(rec(rng, [MINING[k % 5]], rng.uniform(-4, 1), 0.4), 0.025, 0.035), -2)
    b.at(0.004, rec(rng, [CONCRETE[(k + 1) % 5]], rng.uniform(-6, -3), 0.15), -5)
    b.at(0.004, kit.thud(rng, rng.uniform(52, 62), 0.14, 1.0), -12)
    b.at(rng.uniform(0.04, 0.08), unit(lp(grit(rng, 0.6, env([(0, 60), (0.6, 4)], 0.6), (-2, 3)), 6000)), -17)
    if k % 2 == 1:
        b.at(rng.uniform(0.01, 0.03), wire(rng), -15)
    return finish(outdoors(b.x, rng, 0.1, 0.5), KNOCK_LUFS, PEAK)


@recipe("cs-moose-search", "knock", "brick",
        "The rack knocking on a brick wall: stone-hard tines in a ragged flam, the brick's dead weight, mortar crumbling",
        """The same flam of mineral tines as 'plank' (real stone contacts, lowered, with a dense short knock), on brick:
        a real pick-into-rock blow choked short for the wall's dead weight (brick doesn't boom), a hard gritty concrete
        contact, the weight of the head, and a trickle of mortar crumbs falling after (real stone grains, small). Fence
        wire twangs on two takes. Outdoors.""",
        sources=STONES + MINING + CONCRETE, takes=4, lufs=KNOCK_LUFS,
        preview=lambda takes, rng: kit.scatter(takes, rng, (0.6, 1.6)))
def search_knock_brick(rng, k):
    return knock_brick(rng, k)


# ---- cs-moose-ram: every 3 s for 15 s at the car it knows you're in ----------------------------------------------------------

BOOM_LUFS = -25.0     # most of a boom is under 100 Hz, which loudness barely counts: levelled any higher, its peak clips


def shudder(rng, length=1.6, rock=None):
    """The car taking the blow as a whole, heard from inside: the floor shuddering (boards chattering on the joists,
    dying fast), the body rocking on its springs (a low sway, two or three times, and the springs' iron creak), the
    lockers' doors and the big side door knocking in their frames."""
    b = Bus(length + 0.3)
    rock = rock or rng.uniform(2.4, 3.2)
    floor = bp(synth.noise(0.5, rng, "pink"), 90, 500) * env([(0, 1), (0.5, 0)], 0.5, "exp")
    floor = dsp.tremolo(floor, rng.uniform(18, 26), 0.8, rng, jitter=0.3)
    b.at(0.01, unit(floor), -10)
    t = np.arange(samples(length)) / SR
    sway = lp(synth.noise(length, rng, "brown"), 70) * (0.5 + 0.5 * np.sin(2 * np.pi * rock * t)) * np.exp(-t / 0.5)
    b.at(0, unit(sway), -12)
    for i in range(2):
        at = (i + 0.5) / rock
        L = rng.uniform(0.25, 0.4)
        c = synth.creak(L, rng.uniform(25, 40), rng, body=[m * rng.uniform(0.9, 1.1) for m in synth.IRON], q=14)
        b.at(at, lp(c, 2000) * env([(0, 0), (L * 0.3, 1), (L, 0)], L), -20 - 6 * i)
    rat = Bus(0.5)
    for j in range(int(rng.integers(4, 8))):   # the lockers' doors chattering in their iron frames, settling
        x = dsp.vari(main_hit(LATCH[j % 2], 0.05), rng.uniform(-10, -6))
        rat.at(j / rng.uniform(20, 32), unit(x), -2.5 * j)
    b.at(0.03, unit(lp(rat.x, 2500)), -17)
    b.at(rng.uniform(0.01, 0.03), door_jump(rng), -11)
    return b.x


def door_jump(rng):
    """The big sliding door jumping in its iron track with the blow: its rollers and latch knocking a few times, quickly
    smaller (real metal and heavy plate knocks, lowered to a door's weight and choked)."""
    b = Bus(0.6)
    t = 0.0
    for j in range(int(rng.integers(3, 6))):
        x = rec(rng, METAL_M if j % 2 else PLATE_H, rng.uniform(-7, -3), 0.15)
        b.at(t, ck.choke(lp(x, 4000), 0.01, 0.025), -4 * j)
        t += rng.uniform(0.04, 0.09)
    return b.x


def boom_hull(rng, k):
    """The rack driven into the car's side, heard from inside: the iron plates' real blow an octave down, rung on through
    a steel sheet's own recorded ring lowered to a car side's size, the car's hollow body booming, then the shudder."""
    b = Bus(2.8)
    ring = dsp.vari(ring_of(STEEL_DOOR, 0.8), rng.uniform(-11, -8))
    blow = mix(rec(rng, PLATE_H, rng.uniform(-6, -3), 0.4), kit.thud(rng, rng.uniform(38, 46), 0.5, 1.0) * 0.8)
    b.at(0, blow, -2)
    b.at(0.002, dsp.vari(main_hit(STEEL_DOOR, 0.7), rng.uniform(-12, -9)), -4)
    b.at(0.002, through(lp(blow, 800), ring), -1)
    b.at(0.003, ck.hollow(rng, [rng.uniform(36, 42), 57, 83, 121, 172], 1.4, q=9), -6)
    b.at(0.0, tines(rng, 2, (0.004, 0.012)), -16)
    b.at(0.02, shudder(rng), 0)
    return finish(hp(inside_car(lp(b.x, 7000)), 32), BOOM_LUFS, PEAK)


@recipe("cs-moose-ram", "boom", "hull",
        "A deep iron boom through the whole car, heard from inside: the side's plates ringing on, the floor shuddering",
        """The rack's whole weight driven into the car's side, heard from inside. Built from real steel: a heavy plate's
        blow and a steel sheet's, an octave and more down to the size of a car side, with the blow rung on through that
        sheet's own recorded ring lowered the same way, so the side's plates boom and sing for a second and a half. Under
        it the car's hollow body booms (a few broad low modes, 36-170 Hz) and the whole car answers: the floor's boards
        chattering on the joists, the big side door jumping in its iron track (real metal knocks, lowered and choked),
        the car rocking on its springs with their iron creak, the lockers' doors chattering once in their frames. A ram
        is just a ram: nothing falls, nothing breaks. Inside a wooden car's room.""",
        sources=PLATE_H + [STEEL_DOOR] + METAL_M + STONES + LATCH, takes=4, lufs=BOOM_LUFS,
        preview=lambda takes, rng: rams(takes, rng))
def ram_boom_hull(rng, k):
    return boom_hull(rng, k)


@functools.lru_cache(maxsize=1)
def car_modes():
    """The car's side as a big iron-framed plate (12 m by 2.7 m): its modes up to 1.4 kHz, f(m, n) going as m^2/a^2 +
    n^2/b^2, the lowest near 34 Hz."""
    a, b = 12.0, 2.7
    base = 34.0 / (1 / a ** 2 + 1 / b ** 2)
    out = sorted(base * (m * m / a ** 2 + n * n / b ** 2) for m in range(1, 40) for n in range(1, 12))
    return [f for f in out if f < 1400]


def boom_modal(rng, k):
    """The car's side modelled as a plate struck by something soft and enormous: a 12-20 ms push (the head's weight)
    rings the plate's own modes, mostly the low ones, and the stone tines' hard contact rings the higher ones; the real
    plate's blow on top; a heavy shudder."""
    b = Bus(2.8)
    L = 2.4
    push = np.zeros(samples(L), np.float32)
    w = samples(rng.uniform(0.012, 0.02))
    push[:w] = np.sin(np.pi * np.arange(w) / w)
    push = push + dsp.fit(hp(tines(rng, 2, (0.004, 0.012)), 150), len(push)) * 0.25
    modes = [f * rng.uniform(0.985, 1.015) for f in car_modes()]
    gains = [(40 / f) ** 0.6 * rng.uniform(0.5, 1.0) for f in modes]
    body = dsp.resonate(push, modes, q=rng.uniform(55, 75), gains=gains)
    body = unit(body) * env([(0, 1), (L, 0)], L, "exp") ** 0.5
    b.at(0, body, -2)
    b.at(0, ck.choke(rec(rng, PLATE_H, rng.uniform(-8, -5), 0.4), 0.03, 0.08), -7)
    b.at(0, tines(rng, 2, (0.004, 0.012)), -16)
    b.at(0, kit.thud(rng, rng.uniform(40, 48), 0.6, 1.0), -9)
    b.at(0.01, shudder(rng, 1.9), 3)
    return finish(hp(inside_car(lp(b.x, 6000), 0.24), 32), BOOM_LUFS, PEAK)


@recipe("cs-moose-ram", "boom", "plate",
        "The car's side struck like a huge iron plate: a deep, pitched boom ringing on, the floor shuddering hard",
        """Modelled from the car itself: its side as an iron-framed plate 12 m by 2.7 m, every mode of it up to 1.4 kHz
        (the lowest near 34 Hz), struck by a soft, enormous push (the head's weight, 12-20 ms long, which rings the low
        modes) with the stone tines' hard contact in it (which rings the higher ones). It booms like a struck gong of
        iron, low and pitched, with a real heavy plate's blow choked short on top for the contact. More shudder than
        'hull': the floor chattering, the big side door jumping in its track, the car rocking on its springs, the lockers
        rattling once. Nothing falls. Inside a wooden car.""",
        sources=PLATE_H + METAL_M + STONES + LATCH, takes=4, lufs=BOOM_LUFS,
        preview=lambda takes, rng: rams(takes, rng))
def ram_boom_plate(rng, k):
    return boom_modal(rng, k)


def rams(takes, rng, every=3.0, count=5):
    """The rams as the game plays them: one every 3 s."""
    b = Bus(every * count + 1.0)
    for i in range(count):
        b.at(0.2 + i * every, takes[i % len(takes)])
    return b.x


def scrape_plates(rng, k):
    """The rack dragged along the car's side after a ram, heard from inside: stone tines grinding on iron, the plate
    groaning under them, rivet heads bumping past slowly, the plate popping in under the weight once."""
    L = rng.uniform(1.1, 1.6)
    b = Bus(L + 1.2)
    ring = dsp.vari(ring_of(PLATE_H[k % 5], 0.45), rng.uniform(-7, -4))
    press = env([(0, 0), (0.08, 1), (L * 0.6, 0.85), (L, 0)], L)
    for j, (lo, hi, g) in enumerate(((180, 900, 0), (400, 1600, -5), (700, 2200, -11))):
        fr = ck.friction(rng, L, rng.uniform(60, 110), lo, hi, rough=0.6) * press
        b.at(rng.uniform(0, 0.04), mix(fr * 0.5, through(fr, ring)), g - 1)
    b.at(0, grit(rng, L, 25 * press + 2, (-10, -5)), -14)
    t = rng.uniform(0.12, 0.2)
    pitch = rng.uniform(0.17, 0.26)
    while t < L - 0.1:   # rivet heads under the tines: slow, even bumps, never a clatter
        x = mix(ck.choke(rec(rng, METAL_M, rng.uniform(-10, -7), 0.2), 0.008, 0.03), tines(rng, 1) * 0.5)
        b.at(t, mix(x, through(x, ring) * 0.6), -5 + 20 * np.log10(float(press[min(samples(t), len(press) - 1)]) + 0.05))
        t += pitch * rng.uniform(0.9, 1.1)
    if k != 1:   # the plate oil-canning in under the weight: a low pop through its ring
        at = L * rng.uniform(0.35, 0.6)
        pop = mix(synth.thump(rng.uniform(70, 85), 0.3, drop=0.2), rec(rng, PLATE_H, -8, 0.3) * 0.5)
        b.at(at, mix(pop, through(pop, ring) * 0.7), -4)
    return done(inside_car(lp(b.x, 3500)))


@recipe("cs-moose-ram", "scrape", "drag",
        "The rack dragged along the car's iron side, heard from inside: stone grinding on plate, rivets bumping slowly past",
        """After a ram the rack drags along the side. Three tines grinding on the iron at once (stick-slip friction, slow
        and heavy, each rung through a real heavy plate's ring lowered to the side's size, so the plate groans under the
        stone), the slag's grit crumbling, and the rivet heads bumping under the tines slowly and evenly (a real metal
        knock, lowered, every fifth of a second): one heavy low drag, not a Climber's clatter or a Dragger's high rasp.
        Two takes have the plate popping in under the weight (oil-canning) with a low boom. Heard through the wall
        from inside the car.""",
        sources=PLATE_H + METAL_M + STONES, takes=3)
def ram_scrape_drag(rng, k):
    return scrape_plates(rng, k)


# ---- cs-moose-train-pass: a moose beside the line takes the train for a rival ------------------------------------------

def rut_call(rng, k):
    """A rut challenge after the train: a long hollow moan that swells from a closed 'mm' and climbs, wavering like a
    moose's call, then the breath let out and the head tossed (the wire in the rack ringing): carrying, from beside the
    line. No short grunts after it: those would be the warning's cough."""
    L = rng.uniform(2.2, 2.9)
    top = rng.uniform(128, 150)
    pts = [(0, 66), (0.3, 82), (L * 0.45, top), (L * 0.7, top * rng.uniform(0.9, 0.97)), (L, 70)]
    vow = [(0, "m"), (0.3, "o"), (L * 0.45, "a"), (L * 0.8, "o"), (L, "u")]
    shape = [(0, 0), (0.25, 0.5), (L * 0.45, 1), (L * 0.8, 0.85), (L, 0)]
    v = voice(rng, pts, L, vow, shape, rage=0.45, sub=0.3, quaver=0.035,
              crack=(L * rng.uniform(0.55, 0.7), rng.uniform(0.08, 0.14)) if k == 1 else None)
    b = Bus(L + 2.6)
    i = rng.uniform(0.35, 0.5)
    b.at(0, intake(rng, i), -12)
    b.at(i + 0.03, v, 0)
    t = i + L - 0.05
    Lb = rng.uniform(0.8, 1.1)
    b.at(t, blow(rng, Lb, 0.3), -9)
    b.at(t + rng.uniform(0.1, 0.4), head_sweep(rng), -10)
    return done(dsp.distance(dsp.trim_silence(b.x, -60), rng.uniform(18, 24), rng))


@recipe("cs-moose-train-pass", "bellow", "call",
        "A rut challenge bellowed after the train: a long hollow moan swelling and wavering, carrying, the head tossed after",
        """The Moose's throat (as in the snag's bellow) calling, not raging: a breath dragged in, then a long moan
        swelling out of a closed 'mm' and climbing to 'wah', wavering like a cow moose's call but a bull's octave down,
        with less fry so it carries; then the breath let out and the head tossed, the fence wire in the rack ringing.
        One take cracks up a fifth in the middle. No short grunts after it, so it can't be taken for the warning's
        cough. Set 20 m off in the open night (darker, a long tail), as the train hears it going by.""",
        takes=3)
def pass_bellow_call(rng, k):
    return rut_call(rng, k)


@functools.lru_cache(maxsize=1)
def breaks():
    """Every crack in the real breaking-wood recordings, cut to 60 ms: stems and dead branches snapping."""
    out = []
    for k in BREAKS:
        x = ck.get(k)
        for s, _ in ck.hits(x, floor_db=-30, gap=0.02, prom=5):
            out.append(unit(ck.cut(x, s - samples(0.001), s + samples(0.06), 0.0005, 0.03)))
    return out


def brush_crash(rng, length=0.45):
    """The rack swept through verge brush: dry leaves thrashed (rustle at two sizes), stems and dead branches snapping
    (real cracks), a branch whipping back, the swing's air."""
    b = Bus(length + 0.4)
    b.at(0, ck.whoosh(rng, length, 120, 900, 0.45), -10)
    sh = env([(0, 0), (length * 0.3, 1), (length, 0)], length)
    b.at(length * 0.15, unit(synth.rustle(length, 900, rng, f=(900, 7000), shape=sh, ticks=1.0)), -5)
    b.at(length * 0.15, unit(lp(synth.rustle(length, 400, rng, f=(300, 1800), shape=sh, ticks=0), 2500)), -8)
    br = breaks()
    for _ in range(int(rng.integers(4, 9))):
        x = dsp.vari(br[int(rng.integers(len(br)))], rng.uniform(-3, 4))
        b.at(length * rng.uniform(0.15, 0.8), x, rng.uniform(-14, -5))
    if rng.random() < 0.6:
        w = dsp.vari(main_hit(WHIP[int(rng.integers(2))], 0.2), rng.uniform(-7, -4))
        b.at(length * rng.uniform(0.5, 0.8), unit(lp(w, 4500)), -12)
    return b.x


def stamp(rng):
    """A hoof driven down into the verge's turf: its weight, the grass, the turf tearing, dirt flung."""
    b = Bus(0.7)
    b.at(0, kit.thud(rng, rng.uniform(55, 70), 0.2, 1.0), -10)
    b.at(0.003, rec(rng, GRASS, rng.uniform(-5, -2), 0.4), -2)
    b.at(0.04, unit(ck.friction(rng, 0.12, 300, 300, 2500, rough=0.8)) * env([(0, 1), (0.12, 0)], 0.12), -14)
    b.at(0.05, unit(kit.gravel(rng, 10, 0.2, 600, 3000, body=0)), -16)
    return b.x


def thrash(rng, k):
    """Thrashing the verge as the train goes by: the rack swept through the brush two or three times, hooves stamped
    into the turf between, the wire in the rack jingling."""
    b = Bus(3.4)
    t = 0.05
    for i in range((3, 2, 3)[k]):
        L = rng.uniform(0.35, 0.55)
        b.at(t, brush_crash(rng, L), -1.5 * (i % 2))
        if rng.random() < 0.5:
            b.at(t + L * 0.5, wire(rng, length=0.5, buzz=0.5), -18)
        t += L + rng.uniform(0.05, 0.2)
        for _ in range(int(rng.integers(1, 3))):
            b.at(t, stamp(rng), -3)
            t += rng.uniform(0.18, 0.3)
    return done(dsp.distance(dsp.trim_silence(b.x, -60), rng.uniform(12, 16), rng))


@recipe("cs-moose-train-pass", "thrash", "verge",
        "The rack thrashed through the verge brush and hooves stamped into the turf, beside the line",
        """In place, not charging: the head thrown side to side two or three times through the brush beside the line,
        each sweep a burst of dry leaves thrashed (rustle at two sizes), stems and dead branches snapping (real cracks
        cut from breaking-wood recordings), a branch whipping back (a real switch swished, lowered) and the swing's air;
        between sweeps one or two hooves driven into the turf (the weight, real steps in long grass lowered, the turf
        tearing, dirt flung). The fence wire in the rack jingles. Set about 14 m off in the open.""",
        sources=WHIP + GRASS + BREAKS, takes=3)
def pass_thrash_verge(rng, k):
    return thrash(rng, k)
