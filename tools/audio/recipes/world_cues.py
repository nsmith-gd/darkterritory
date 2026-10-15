"""Sound cues in the world, and what jumped a coupling's pin (queue #518, note 785; K1's #1034, note 732; GDD §32): the
director wants the world to sound before it bites, sound-only cues in the environment, and a loose coupling's cause heard.

What each is decides the build:
- The pin jumping (state-coupling-loose): heard at the gap between two cars the moment its pin jumps up in the knuckle,
  with the line's knock (jobs.py: a dull iron clank round 340 Hz, the pin ringing at 1.75 kHz) straight after. So every
  jump ends in that same pin: a sharp iron clank as it's thrown up in its hole and a duller one as it drops back crooked,
  ringing at the knock's 1.75 kHz so the ear ties the two. What threw it is the cause, and each is its own: a run-in (the
  train braked hard and the slack slamming in down it, couplers and buffers clashing one gap after another, further and
  further off), a hard coupling (one big clash of the knuckles meeting too fast, the cars shoved), a bend taken too fast
  (the car lurching onto its side bearings and its frame groaning as it twists, wood and iron). The weight is in the mids
  and a short knock round 100 Hz, never a held sub: each keeps under 45% of its energy below 150 Hz (the squish rule).
- A house where something waits (tell-house.creak): the Lodger at its corner, the Hanger up in the roof, heard from
  outside or at the door. A floorboard or a rafter taking a slow weight in the dark: old pine's stick-slip swelling in as
  the load comes on and easing off as it settles, sometimes a second board after the first. Never a step: no heel, no
  knock to start it, a weight shifting. The game puts the walls round it (OccludedInHouse); here it's the empty room.
- The Householder shifting (tell-householder.shift): one of its things lifted, it shifts in its chair, its own chair
  (house_folk.py's, cs-householder.creak's): the chair's joints creaking, its legs moved a little on the boards, the
  table jolted so the cutlery rattles once, then stilled.
- A far bell (world-bell.far): something of the crew's lost out there. A stop's small bronze bell rung once or twice a
  long way off across open land: the strike softened by a kilometre of night air, the hum and the minor-third tierce
  ringing on, the land's echoes and reverb. Mournful, never an alarm.
- A far dog (world-dog.bark): something at the lamps' edge, and a farm dog barking at it across the fields: two to four
  urgent barks, sometimes a growl between, a couple of hundred metres off, the barn and the treeline throwing them back.
  There are no dogs in the packs.

Each cue has two candidates: one from the packs' real recordings (iron slams and hits, a door's creak, the chair's
creaks and cutlery, a real struck bell, a real roar's throat) bent to the thing, and one modelled (struck modes,
stick-slip, a bell's partials, a dog's throat through its tract), so the two compare.
"""

import functools

import numpy as np
from scipy import signal

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, Bus
from recipes import crew_kit as ck
from recipes import kit
from recipes import world_kit as W
from recipes.crew_kit import recipe, K, S, R
from recipes.beasts import fry
from recipes.crew_items import hit_of
from recipes.crew_train import latch
from recipes.gannet_tells import bend
from recipes.horrors import finish
from recipes.house_beasts import overhead, grit, RAFTER
from recipes.house_folk import (chair, chair_model, scrape, spoon, plate, kitchen, back_room, knock,
                                CUTLERY, RATTLE, CHINA, CREAKS, WOOD_M, RISE)
from recipes.jobs import strike, between, CASTING
from recipes.jobs import ring as pin_ring
from recipes.world_bed import frame_body, outdoors

SR = dsp.SR

# ---- Sources ------------------------------------------------------------------------------------------------------------

SLAMS = S("metal_hit_01", "misc_36", "misc_30")      # real metal slams: a steel door, a heavy knock-slam, a hard knock
PLATE_H = K("impactPlate_heavy")                     # heavy iron plate struck: a headstock's mass
METAL_H, METAL_L = K("impactMetal_heavy"), K("impactMetal_light")
WOOD_H, WOOD_L = K("impactWood_heavy"), K("impactWood_light")
DOORS = R("doorOpen_1", "doorOpen_2")                # real doors opening: a latch's clunk, then big slow wood creaking
SCRAPE = S("misc_10")[0]                             # a real metal scrape in three strokes
LEATHER = R("dropLeather")[0]                        # a real leather drop: a long hand laid flat
BELLS = K("impactBell_heavy")                        # real bells struck: bronze ringing on for a second and a half
ROAR = "sfx_100_v2:misc_24"                          # a real roar: a big cat's rough throat, 130 Hz, loudest near 400
GASP = "sfx_100_v2:misc_05"                          # a real gasp: a burst of air through a mouth

# ---- Shared parts -------------------------------------------------------------------------------------------------------

JUMP_LUFS = -18.0
JUMP_PEAK = -2.0                 # a smooth look-ahead limit on the blow (build.py's soft knee then bends only a hair)
PIN = 640                        # the pin modelled: a short thick bar, its second mode (x2.756, 1764 Hz) on the knock's ring


def cut_to(y, length, tail=0.25):
    """Exactly `length` s, the last `tail` faded: a one-shot that stops where it should, its reverb let go gently."""
    return dsp.fade(dsp.fit(np.asarray(y, np.float32), samples(length)), 0.001, tail)


def air(x, metres):
    """`metres` of open air on a damp night: what the air itself absorbs, more the higher (ISO 9613-1's curve at 10 C and
    70% humidity: about 1.3 dB a km at 500 Hz, 3.5 at 1 kHz, 11 at 2 kHz, 37 at 4 kHz), as a zero-phase filter."""
    n = len(x) + samples(0.3)
    m = int(2 ** np.ceil(np.log2(n)))
    f = np.fft.rfftfreq(m, 1 / SR) / 1000
    db = metres / 1000 * (1.6 * f + 1.9 * f ** 2)
    return np.fft.irfft(np.fft.rfft(x, m) * 10 ** (-db / 20), m)[:len(x)].astype(np.float32)


def blur(x, rng, ms=12.0, amount=0.35):
    """The night air's turbulence over a long path: the wavefront scattered a little, so edges soften (a few ms of diffuse
    smear mixed under the direct sound)."""
    k = samples(ms / 1000)
    h = rng.standard_normal(k).astype(np.float32) * np.exp(-np.arange(k) / (k / 3)).astype(np.float32)
    w = signal.fftconvolve(x, h / np.sqrt(np.sum(h ** 2)))[:len(x)].astype(np.float32)
    return mix(x * (1 - amount), w * amount)


def across(x, rng, metres, echoes=(), wet=0.45, ground=1.0):
    """A sound heard `metres` off across open land at night: the air's absorption, its edges blurred by the turbulence, a
    soft loss through the grass and hedges (`ground`, a broad shelf off the top), echoes back off what stands out there
    [(delay s, dB, metres further)] each darker and smeared, and the night's long reverb."""
    y = blur(air(x, metres), rng)
    if ground:
        y = mix(y * (1 - 0.5 * ground), lp(y, 2500, 1) * 0.5 * ground)
    b = Bus(len(y) / SR + max([d for d, _, _ in echoes], default=0) + 0.1)
    b.at(0, y)
    for d, db, extra in echoes:
        b.at(d, blur(air(lp(y, 3000, 1), extra), rng, 30, 0.7), db)
    return dsp.room(b.x, "night", wet=wet, rng=rng)


# ---- The pin jumping (state-coupling-loose) ----------------------------------------------------------------------------

def pin_rec(rng, k):
    """The pin thrown up in its knuckle and dropping back crooked, from real iron: a sharp clank as it's knocked up (a real
    heavy metal hit, high-passed above the knuckle's weight and choked, a real light metal hit's ring tuned to the knock's
    1.75 kHz over it), a duller one 60-110 ms later as it drops back, and two or three small knocks as it settles."""
    up = mix(strike(METAL_H[k % 5], rng.uniform(-1, 1), lo=250, at=0.012, tau=0.03),
             pin_ring(rng, METAL_L[(k + 1) % 5], 0.03, 0.06) * 0.6)
    down = mix(strike(METAL_H[(k + 2) % 5], -rng.uniform(2, 3.5), lo=200, at=0.02, tau=0.03),
               pin_ring(rng, METAL_L[(k + 3) % 5], 0.02, 0.04) * 0.35)
    t = rng.uniform(0.06, 0.11)
    parts = [(0, up, 0), (t, down, -5)]
    for j in range(int(rng.integers(2, 4))):
        t += rng.uniform(0.025, 0.05)
        parts.append((t, pin_ring(rng, METAL_L[(k + j) % 5], 0.004, 0.012), -14 - 4 * j))
    return between(ck.place(parts))


def pin_model(rng, k):
    """The pin thrown up and dropping back, modelled: a short thick iron bar struck hard (its bending modes, the second on
    the knock's 1.75 kHz) against the knuckle's casting (close, inharmonic modes from 330 Hz, choked: it clanks), up and
    then down softer and lower, and two or three small knocks of the bar settling crooked."""
    def hit(contact, f1, cast_db):
        bar = ck.choke(W.body(rng, f1 * rng.uniform(0.97, 1.03), W.BAR, decay=0.12, contact=contact, length=0.3), 0.02, 0.05)
        c0 = 330 * rng.uniform(0.9, 1.1)
        cast = W.modal([c0 * r for r in CASTING], [0.05] * len(CASTING), [1 / (1 + 0.4 * i) for i in range(len(CASTING))], 0.3,
                       rng, contact * 3)
        cast = ck.choke(cast, 0.015, 0.03)
        return W.norm(mix(W.norm(bar), W.norm(hp(cast, 200)) * dsp.db2a(cast_db)))
    t = rng.uniform(0.06, 0.11)
    parts = [(0, hit(0.00025, PIN, -4), 0), (t, hit(0.0005, PIN * 0.97, -2), -6)]
    for j in range(int(rng.integers(2, 4))):
        t += rng.uniform(0.025, 0.05)
        parts.append((t, ck.choke(W.body(rng, PIN * rng.uniform(0.98, 1.02), W.BAR, decay=0.04, contact=0.0003,
                                         length=0.08), 0.004, 0.012), -15 - 4 * j))
    return between(ck.place(parts))


def clash_rec(rng, k, weight=1.0):
    """Couplers and buffers slammed together, from real iron: a real metal slam pitched down for a coupler's mass, Kenney's
    heavy plate for the headstock's iron (high-passed: its own boom sits under the squish line), a real heavy hit's iron
    edge, the weight in a short knock round 100 Hz, the wooden car's end knocked, loose gear rattling in the car after."""
    slam = strike(SLAMS[k % 3], -rng.uniform(3, 5), lo=110, at=0.03, tau=0.04, length=0.4)
    plate = strike(PLATE_H[k % 5], -rng.uniform(1, 3), lo=160, at=0.03, tau=0.05, length=0.4)
    edge = strike(METAL_H[(k + 1) % 5], -rng.uniform(3, 5), lo=200, at=0.01, tau=0.03)
    car = ck.hollow(rng, [150, 235, 380], 0.12, q=4)
    y = mix(slam, plate * 0.6, edge * 0.5, W.knock(rng, 105, 0.14, 0.3) * 0.3 * weight, car * 0.3)
    return W.norm(mix(W.norm(hp(y, 70, 2)), gear(rng) * 0.1 * weight))


def clash_model(rng, k, weight=1.0):
    """Couplers and buffers slammed together, modelled: the coupler heads' cast steel struck (close inharmonic modes from
    about 200 Hz, steel on steel for half a millisecond), the headstock's iron plate ringing over its dense modes, the crack
    of the contact, the weight in a short knock round 100 Hz, the car's wooden end knocked, its loose gear rattling after."""
    c0 = rng.uniform(190, 240)
    cast = W.modal([c0 * r * rng.uniform(0.97, 1.03) for r in CASTING], [0.06 / (1 + 0.3 * i) for i in range(len(CASTING))],
                   [rng.uniform(0.5, 1.0) / (1 + 0.15 * i) for i in range(len(CASTING))], 0.4, rng, 0.0005)
    sheet = W.sheet(rng, f1=rng.uniform(150, 190), fmax=6000, decay=0.12, contact=0.0006, tilt=0.3)
    crack = bp(synth.noise(0.03, rng), 700, 8000) * env([(0, 1), (0.03, 0.01)], 0.03, "exp")
    car = ck.hollow(rng, [150, 235, 380], 0.12, q=4)
    y = mix(W.norm(cast), W.norm(sheet) * 0.7, W.norm(crack) * 0.6, W.knock(rng, 105, 0.14, 0.3) * 0.3 * weight, car * 0.3)
    return W.norm(mix(W.norm(hp(y, 70, 2)), gear(rng) * 0.1 * weight))


def gear(rng):
    """The car's loose gear rattling a moment after the jolt (world_bed's slack, a little shorter)."""
    rat = synth.skitter(0.3, 40, rng, f=(700, 2600), q=(6, 14), legs=5) * env([(0, 0), (0.03, 1), (0.3, 0)], 0.3)
    return np.concatenate([np.zeros(samples(rng.uniform(0.05, 0.1)), np.float32), W.norm(rat)])


def further(x, rng, metres):
    """A clash `metres` down the train: its top dulled by the distance and the cars' bodies between, quieter (a little
    faster than 1/distance, the cars shadowing it), thrown back off the train's sides and the cutting (a short space,
    not the night's long one, so each stays its own blow)."""
    y = lp(x, 16000 / (1 + metres / 10), 2)
    y = ck.space(y, 0.5, min(0.15 + metres / 250, 0.4), 3500, ((0.006, 0.4), (0.017, 0.25)), seed=int(metres))
    return dsp.gain(y, -24 * np.log10(metres / 4))


def jumped(rng, b, pin, at):
    """The pin thrown up a beat after the blow that threw it (the shock has to reach it), its clank on top."""
    b.at(at + rng.uniform(0.035, 0.06), W.norm(pin), -1)


def done(y, rng, L, tail):
    """Out in the open beside the train (a touch of the night: the game's world adds its own), cut to `L` s, its blow's
    peaks taken down smoothly so it sits at its loudness."""
    return finish(cut_to(outdoors(y, rng, 0.06), L, tail), JUMP_LUFS, JUMP_PEAK)


def run_in(rng, k, clash, pin):
    """A run-in: this gap's couplers slamming in with the pin thrown up in it, then the slack running on down the train,
    the next gaps clashing a car (12-14 m) further off each, 0.15-0.21 s apart, quieter and darker."""
    L = (1.3, 1.1, 1.45)[k]
    b = Bus(L + 0.5)
    b.at(0, clash(rng, k, 1.0), -1)
    jumped(rng, b, pin(rng, k), 0)
    t, d = 0.0, 0.0
    for i in range((3, 2, 4)[k]):
        t += rng.uniform(0.15, 0.21)
        d += rng.uniform(12, 14)
        b.at(t, further(clash(rng, k + i + 1, 0.8), rng, d))
    return done(b.x, rng, L, 0.3)


def hard_coupling(rng, k, clash, pin, bang):
    """A hard coupling: the knuckles meeting too fast (a bigger clash, the lock dropping in its knuckle) with the pin
    knocked up in it, the car shoved (its loose gear rattling) and the slack thrown into the next car with a clunk
    further off."""
    L = (0.8, 0.95, 0.7)[k]
    b = Bus(L + 0.5)
    b.at(0, bang(rng, k), -1)
    b.at(0, clash(rng, k + 1, 1.2), -5)
    b.at(rng.uniform(0.02, 0.03), latch(rng, k, -5), -14)
    jumped(rng, b, pin(rng, k), 0.01)
    b.at(rng.uniform(0.32, 0.45), further(clash(rng, k + 2, 0.7), rng, 14), 2)
    return done(b.x, rng, L, 0.2)


def bang_rec(rng, k):
    """The knuckles' own bang, real: the packs' metal door slam pitched down and its heavy plate, little choked."""
    return W.norm(mix(strike(SLAMS[0], -rng.uniform(4, 6), lo=120, at=0.06, tau=0.06, length=0.5),
                      strike(PLATE_H[(k + 2) % 5], -rng.uniform(2, 4), lo=170, at=0.05, tau=0.07, length=0.5) * 0.7))


def bang_model(rng, k):
    """The knuckles' own bang, modelled: a thick casting struck hard and ringing a moment before the mass kills it."""
    c0 = rng.uniform(260, 320)
    y = W.modal([c0 * r * rng.uniform(0.97, 1.03) for r in CASTING], [0.12 / (1 + 0.25 * i) for i in range(len(CASTING))],
                [rng.uniform(0.5, 1.0) / (1 + 0.12 * i) for i in range(len(CASTING))], 0.5, rng, 0.0004)
    crack = bp(synth.noise(0.03, rng), 700, 8000) * env([(0, 1), (0.03, 0.01)], 0.03, "exp")
    return ck.choke(W.norm(mix(W.norm(y), W.norm(crack) * 0.5)), 0.05, 0.06)


def lurch_rec(rng, k, soft=1.0):
    """The car lurching onto its side bearings: a real heavy wood knock (the body) dropped for its size and Kenney's heavy
    plate (the bearing's iron), high-passed, a short weight under them."""
    body = strike(WOOD_H[k % 5], -rng.uniform(3, 5), lo=110, at=0.07, tau=0.08, length=0.4)
    iron = strike(PLATE_H[(k + 3) % 5], -rng.uniform(2, 4), lo=170, at=0.03, tau=0.05, length=0.4)
    return hp(mix(body, iron * 0.5, W.knock(rng, 100, 0.2, 0.3) * 0.3 * soft), 90)


def lurch_model(rng, k, soft=1.0):
    """The car lurching onto its side bearings, modelled: the wooden body's low modes knocked through a soft, heavy contact,
    the bearing's iron plates under it, a short weight."""
    body = W.body(rng, rng.uniform(150, 180), W.PLATE, decay=0.04, contact=0.003, count=10, spread=0.08)
    thud = lp(synth.noise(0.12, rng), 900) * env([(0, 1), (0.12, 0.01)], 0.12, "exp")
    iron = W.body(rng, rng.uniform(300, 360), W.PLATE, decay=0.03, contact=0.0008, count=10)
    return hp(mix(W.norm(body), W.norm(thud) * 0.6, W.norm(iron) * 0.4, W.knock(rng, 100, 0.2, 0.3) * 0.3 * soft), 90)


@functools.lru_cache(maxsize=2)
def _door_creak(key):
    x = ck.get(key)
    return x[samples(0.42):].copy()     # past the latch's clunk: only the wood creaking


def groan_rec(rng, k, length, hard=1.0):
    """The frame twisting, from real wood: a real door's creak (past its latch) dropped eight to eleven semitones for a car
    body's sills and posts (less for the smaller joint that creaks as it eases back, `hard` under 1), held out to `length`
    (rubberband, crisp, so the slips stay slips) and bent up as the twist builds; a real metal scrape dropped an octave and
    held out under it for the iron truss rods and body bolts working."""
    semis = -rng.uniform(8, 11) + 3 * (1 - hard)
    x = _door_creak(DOORS[k % 2])
    a = int(rng.integers(0, max(1, len(x) - samples(0.4))))
    c = dsp.vari(ck.cut(x[a:], 0, samples(0.4), 0.01, 0.05), semis)
    c = dsp.fit(dsp.stretch(c, max(1.0, length * 1.3 / (len(c) / SR))), samples(length * 1.3))
    c = W.norm(dsp.fit(bend(c, env([(0, 0), (length * 1.3, rng.uniform(2, 3.5))], length * 1.3)), samples(length)))
    s = dsp.vari(ck.get(SCRAPE), -12 + rng.uniform(-1, 1))
    s = W.norm(bp(dsp.fit(dsp.stretch(s[:samples(0.6)], max(1.0, length / 0.6 * 0.5)), samples(length)), 300, 3000))
    return hp(mix(c, s * 0.3), 120) * env([(0, 0), (length * 0.4, 0.8), (length * 0.75, 1), (length, 0)], length)


def groan_model(rng, k, length, hard=1.0):
    """The frame twisting, modelled (world_bed's frame): stick-slip through a loaded car's wooden sills and its iron truss
    rods, quickening as the twist builds and slowing as it gives, and a timber's higher creak over it (a smaller joint as
    it eases back, `hard` under 1)."""
    scale = rng.uniform(0.32, 0.42) * (1 + 0.4 * (1 - hard))
    rate = env([(0, rng.uniform(12, 18)), (length * 0.7, rng.uniform(45, 65)), (length, rng.uniform(15, 22))], length, "exp")
    g = synth.creak(length, rate, rng, body=frame_body(rng, scale), q=20, jitter=0.35, grit=0.25)
    t = synth.creak(length, rate * 1.6, rng, body=frame_body(rng, scale * 2.2, iron=False), q=14, jitter=0.45, grit=0.3)
    y = hp(mix(W.norm(g), W.norm(t) * 0.35), 120)
    return y * env([(0, 0), (length * 0.4, 0.8), (length * 0.75, 1), (length, 0)], length)


def on_bend(rng, k, lurch, groan, pin):
    """A bend taken too fast: the car lurching onto its side bearings (its loose gear thrown about), its frame groaning as
    it twists, the pin jumping at the twist's height, the frame easing back with a shorter creak as the body rocks back
    onto its bearings."""
    L = (1.3, 1.5, 1.1)[k]
    G = L * rng.uniform(0.55, 0.65)
    jump = rng.uniform(0.45, 0.55) * L
    b = Bus(L + 0.5)
    b.at(0, W.norm(lurch(rng, k)), 0)
    b.at(0, gear(rng), -12)
    b.at(0.04, W.norm(groan(rng, k, G, 1)), -2)
    jumped(rng, b, pin(rng, k), jump)
    b.at(G * 0.9, W.norm(groan(rng, k + 1, L - G * 0.9 - 0.05, 0.6)), -7)
    b.at(L * rng.uniform(0.72, 0.8), W.norm(lurch(rng, k + 2, 0.5)), -11)
    return done(b.x, rng, L, 0.2)


@recipe("state-coupling-loose", "jump-brake", "slack",
        "Jumped by a run-in: couplers slamming in down the train, one gap after another, the pin jumping in its knuckle",
        """From real iron: this gap's couplers and buffers slamming together (a real metal slam pitched down for a
        coupler's mass, Kenney's heavy plate for the headstock, a real heavy hit's edge, a short knock round 100 Hz, the
        car's end and its loose gear), and a beat after it the pin thrown up in its knuckle: a sharp iron clank and its
        ring tuned to the knock's 1.75 kHz, a duller one as it drops back crooked, small knocks as it settles. Then the
        slack runs on down the train: two to four more gaps clashing a car further off each, darker and quieter.""",
        sources=SLAMS + PLATE_H + METAL_H + METAL_L, takes=3, lufs=JUMP_LUFS)
def jump_brake_slack(rng, k):
    return run_in(rng, k, clash_rec, pin_rec)


@recipe("state-coupling-loose", "jump-brake", "cast",
        "Jumped by a run-in, modelled: cast couplers clashing down the train, the pin bar jumping in its knuckle",
        """Modelled: this gap's coupler heads struck (cast steel's close modes from about 200 Hz, steel on steel), the
        headstock's iron plate ringing, a crack of the contact, a short knock round 100 Hz, the car's end and its loose
        gear; the pin a short thick iron bar struck up against the knuckle's casting (its second mode on the knock's
        1.75 kHz) a beat after, dropping back softer, settling with small knocks. Then two to four more gaps clashing down
        the train, a car further off each.""",
        takes=3, lufs=JUMP_LUFS)
def jump_brake_cast(rng, k):
    return run_in(rng, k, clash_model, pin_model)


@recipe("state-coupling-loose", "jump-coupled", "clash",
        "Jumped by a hard coupling: the knuckles' big iron clash, the pin knocked up in its knuckle, the cars shoved",
        """From real iron: the knuckles meeting too fast (the packs' metal door slam pitched down four to six semitones,
        Kenney's heavy plate, little choked so it rings a moment), the couplers' clash under it, the knuckle's lock dropping
        (a real latch, dropped), the pin knocked up in its knuckle (a sharp real iron clank with the knock's 1.75 kHz ring,
        dropping back crooked), the car's loose gear rattling, and the slack shoved into the next car with a clunk
        further off.""",
        sources=SLAMS + PLATE_H + METAL_H + METAL_L + R("metalLatch") + S("door_02", "door_04", "door_05"), takes=3,
        lufs=JUMP_LUFS)
def jump_coupled_clash(rng, k):
    return hard_coupling(rng, k, clash_rec, pin_rec, bang_rec)


@recipe("state-coupling-loose", "jump-coupled", "cast",
        "Jumped by a hard coupling, modelled: cast knuckles banging together, the pin bar knocked up, the cars shoved",
        """Modelled: the knuckles meeting too fast (a thick casting struck hard, ringing a moment before its mass kills
        it), the coupler heads' clash under it, the knuckle's lock dropping (a real latch), the pin a short iron bar
        knocked up against the casting (its second mode on the knock's 1.75 kHz) and dropping back, the car's loose gear
        rattling, and a clunk further off as the slack goes into the next car.""",
        sources=R("metalLatch") + S("door_02", "door_04", "door_05"), takes=3, lufs=JUMP_LUFS)
def jump_coupled_cast(rng, k):
    return hard_coupling(rng, k, clash_model, pin_model, bang_model)


@recipe("state-coupling-loose", "jump-bend", "groan",
        "Jumped on a bend: the car lurching, its wooden frame groaning as it twists, the pin jumping at the height of it",
        """From real wood and iron: the car lurching onto its side bearings (a real heavy wood knock dropped for the body,
        Kenney's heavy plate for the bearing), its frame groaning as it twists (a real door's creak dropped eight to eleven
        semitones for a car's sills and posts, held out and bent up as the twist builds, a real metal scrape an octave
        down under it for the truss rods and bolts), the pin jumping at the height of it (a sharp real iron clank with
        the knock's 1.75 kHz ring, dropping back), then the frame easing back and the body rocking onto its bearings.""",
        sources=WOOD_H + PLATE_H + DOORS + [SCRAPE] + METAL_H + METAL_L, takes=3, lufs=JUMP_LUFS)
def jump_bend_groan(rng, k):
    return on_bend(rng, k, lurch_rec, groan_rec, pin_rec)


@recipe("state-coupling-loose", "jump-bend", "frame",
        "Jumped on a bend, modelled: the car lurching, stick-slip groaning through its sills and truss rods, the pin jumping",
        """Modelled: the car lurching onto its side bearings (the wooden body's low modes through a soft, heavy contact,
        the bearing's iron plates under it), its frame groaning as it twists (world_bed's frame: stick-slip through a
        loaded car's wooden sills and iron truss rods, quickening as the twist builds, a timber's higher creak over it),
        the pin a short iron bar jumping against the knuckle's casting at the height of it, then the frame easing back
        and the body rocking onto its bearings.""",
        takes=3, lufs=JUMP_LUFS)
def jump_bend_frame(rng, k):
    return on_bend(rng, k, lurch_model, groan_model, pin_model)


# ---- A house where something waits (tell-house.creak) -------------------------------------------------------------------

CREAK_BAND = (250, 3000)     # old pine under a weight, in the dark: its creak, with nothing of a step's thump in it
FLOOR = [260, 417, 760, 1290, 2110]          # a floorboard's modes (synth.WOOD's, its first held a little stiffer)


def board_rec(rng, k, length, semis):
    """A board taking a slow weight, from real wood: a real door's creak (past its latch) dropped `semis` (a board or a
    rafter is bigger and slower than a door), held out to `length` (crisp, so the slips stay slips) and bent down as
    the load comes on and eases, swelling in with the weight and easing off as it settles."""
    x = _door_creak(DOORS[k % 2])
    a = int(rng.integers(0, max(1, len(x) - samples(0.35))))
    c = dsp.vari(ck.cut(x[a:], 0, samples(0.35), 0.01, 0.05), semis)
    c = dsp.fit(dsp.stretch(c, max(1.0, length * 1.2 / (len(c) / SR))), samples(length * 1.2))
    c = dsp.fit(bend(c, env([(0, 0), (length * 0.5, -rng.uniform(1, 2)), (length * 1.2, -rng.uniform(0, 1))], length * 1.2)),
                samples(length))
    return W.norm(hp(c, 150)) * swell(rng, length)


def board_model(rng, length, modes):
    """A board taking a slow weight, modelled: stick-slip in old pine (a slip at every catch, 10 a second as the weight
    starts to come on, quickening to 60-110 as the board bends under it, slowing as it settles), rung through the board's
    or the rafter's modes, swelling in with the load."""
    rate = env([(0, rng.uniform(8, 12)), (length * rng.uniform(0.35, 0.5), rng.uniform(60, 110)),
                (length * 0.8, rng.uniform(30, 45)), (length, rng.uniform(10, 15))], length, "exp")
    c = synth.creak(length, rate, rng, body=[f * rng.uniform(0.92, 1.08) for f in modes], q=rng.uniform(16, 24), jitter=0.4,
                    grit=0.35)
    return W.norm(hp(c, 150)) * swell(rng, length)


def swell(rng, length):
    """A weight coming on: no attack, the load building over a fifth of a second or more (slowly at first: the first
    slips are small), held, easing off."""
    e = env([(0, 0), (length * rng.uniform(0.2, 0.35), 0.85), (length * 0.6, 1), (length * 0.85, 0.6), (length, 0)], length)
    return e ** 1.5


def seat(rng, real):
    """The board seating on its joist as the weight settles: a small dull tock (a real light wood knock dropped and
    choked, or a deal board's low modes knocked softly), nothing like a heel."""
    if real:
        return ck.choke(strike(WOOD_L[int(rng.integers(5))], -rng.uniform(5, 7), lo=200, at=0.01, tau=0.02), 0.01, 0.02)
    return ck.hollow(rng, [310, 520, 870], 0.06, q=5, hit=0.003)


def waits(rng, k, creak_fn, real):
    """Four ways of a weight in the dark: one long board; a board then the next as it shifts across; a rafter overhead,
    grit let down off it; a short creak, a pause, and a long slow one. Each ends with the board seating, quietly."""
    if k == 0:
        L = rng.uniform(1.2, 1.5)
        parts = [(0, creak_fn(L, False), 0), (L * 0.85, seat(rng, real), -18)]
    elif k == 1:
        a, gap, c = rng.uniform(0.55, 0.7), rng.uniform(0.25, 0.4), rng.uniform(0.6, 0.8)
        parts = [(0, creak_fn(a, False), 0), (a * 0.8, seat(rng, real), -20), (a + gap, creak_fn(c, False), -2),
                 (a + gap + c * 0.85, seat(rng, real), -18)]
    elif k == 2:
        L = rng.uniform(1.3, 1.6)
        dust = grit(rng, 0.7, env([(0, 0), (0.05, 50), (0.7, 0)], 0.7))
        parts = [(0, overhead(creak_fn(L, True)), 0), (L * 0.55, overhead(dust), -20)]
    else:
        a, gap, c = rng.uniform(0.3, 0.4), rng.uniform(0.3, 0.4), rng.uniform(0.85, 1.0)
        parts = [(0, creak_fn(a, False), -4), (a + gap, creak_fn(c, False), 0), (a + gap + c * 0.85, seat(rng, real), -18)]
    return dsp.band(back_room(ck.place(parts), 0.2), *CREAK_BAND, order=3)


@recipe("tell-house", "creak", "board",
        "A house where something waits: old boards creaking under a slow weight in the dark, sometimes a rafter overhead",
        """From real wood: the packs' door creaks (past their latch's clunk) dropped three to six semitones for a
        floorboard, nine to eleven for a rafter, held out (crisp, so the slips stay slips) and bent down as the weight
        comes on, swelling in with no knock to start it and easing off, the board seating on its joist with a small dull
        tock. One long board; one board then the next as the weight shifts across; one rafter overhead with grit let down
        off it; one short creak, a pause, and a long slow one. In a bare room: the game puts the walls round it.""",
        sources=DOORS + WOOD_L + S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02"), takes=4,
        band=CREAK_BAND, lufs=-20)
def creak_board(rng, k):
    return waits(rng, k, lambda L, rafter: board_rec(rng, k, L, -rng.uniform(9, 11) if rafter else -rng.uniform(3, 6)), True)


@recipe("tell-house", "creak", "slip",
        "A house where something waits, modelled: stick-slip in old pine under a slow weight, a board, two, or a rafter",
        """Modelled: stick-slip in old pine, a slip at every catch, ten a second as the weight starts to come on,
        quickening to 60-110 as the board bends under it and slowing as it settles, rung through a floorboard's modes or
        a rafter's lower ones; it swells in with no knock to start it, and the board seats on its joist with a soft low
        tock. The same four takes as 'board': one long board, a board then the next, a rafter overhead with grit falling,
        a short creak then a long one. In a bare room.""",
        sources=S("stones_01", "stones_02", "stones_03", "footstep_01", "footstep_02"), takes=4, band=CREAK_BAND, lufs=-20)
def creak_slip(rng, k):
    return waits(rng, k, lambda L, rafter: board_model(rng, L, [f * 1.3 for f in RAFTER] if rafter else FLOOR), False)


# ---- The Householder shifting in its chair (tell-householder.shift) -----------------------------------------------------

def rattle_once(rng, k, real):
    """The table jolted once: knife, fork and spoon jumping on the plate and the boards and the cup chattering in its
    saucer, then stilled at once (choked within a few hundredths of a second, as if a hand were laid on them). Pitched to
    sit in its band (the clinks' ring at 1.5-5 kHz): real, the packs' loose-metal rattle and small clinks and a light china
    knock; modelled, spoons as thin plated bars bouncing and the plate's porcelain rim."""
    L = rng.uniform(0.12, 0.18)
    if real:
        parts = [(0.0, ck.norm(hp(dsp.vari(hit_of(RATTLE, 0, 0.2, -20), rng.uniform(-3, -1)), 700)), 0)]
        for t in np.sort(L * rng.random(int(rng.integers(3, 6))) ** 1.5):
            x = hit_of(CUTLERY[int(rng.integers(len(CUTLERY)))], int(rng.integers(3)), 0.15, -18)
            x = dsp.vari(x, rng.uniform(-2, 1))
            parts.append((float(t), ck.norm(hp(ck.choke(x, 0.01, 0.03), 700)), rng.uniform(-10, -3)))
        cup = dsp.vari(hit_of(CHINA[int(rng.integers(len(CHINA)))], int(rng.integers(3)), 0.15, -18), rng.uniform(-2, 0))
        parts.append((rng.uniform(0, L * 0.6), ck.norm(ck.choke(cup, 0.008, 0.04)), rng.uniform(-12, -6)))
    else:
        times = np.sort(L * rng.random(int(rng.integers(5, 8))) ** 1.5)
        parts = [(float(t), lp(spoon(rng), 6000), rng.uniform(-8, 0)) for t in times]
        parts.append((rng.uniform(0, L * 0.5), plate(rng), rng.uniform(-10, -4)))
    return ck.choke(ck.place(parts, L + 0.3), L, 0.015)


def shifts(rng, k, real):
    """Three shifts in the chair: the chair creaking under it and its legs moved an inch on the boards, the cutlery
    rattling once, the joints giving a last small creak as it settles; the legs first, a short hitch over the boards,
    the cutlery, then the creak; two small creaks as it leans, the legs and the cutlery, and (real) a long hand laid
    flat on the cloth to still it."""
    creak = (lambda L, t: chair(rng, t, L)) if real else (lambda L, t: chair_model(rng, L))

    def leg():
        """A chair leg set down square on the boards: a real block of wood knocked, or a deal leg's modes knocked."""
        return knock(rng, WOOD_M, k, -3, 0.03) if real else ck.hollow(rng, [520, 980, 1650], 0.05, q=3.5, hit=0.002)

    drag = rng.uniform(0.16, 0.24)
    if k == 0:
        parts = [(0, creak(0.45, k), 0), (0.12, scrape(rng, drag, 0.4), -4), (0.12 + drag, leg(), -7),
                 (0.14, rattle_once(rng, k, real), 0), (0.62, creak(0.25, k + 2), -7)]
    elif k == 1:
        parts = [(0, scrape(rng, drag, 0.6), -2), (drag, leg(), -6), (0.03, rattle_once(rng, k, real), 0),
                 (drag + 0.1, creak(0.45, k + 1), -1)]
    else:
        parts = [(0, creak(0.3, k), -2), (0.45, creak(0.35, k + 1), 0), (0.5, scrape(rng, drag * 0.7, 0.3), -8),
                 (0.52, rattle_once(rng, k, real), 0)]
        if real:
            parts.append((0.52 + 0.15, ck.choke(lp(knock(rng, [LEATHER], 0, -4, 0.03), 2500), 0.02, 0.03), -14))
    return dsp.band(kitchen(ck.place(parts, 1.3), 0.15), *RISE, order=2)


@recipe("tell-householder", "shift", "chair",
        "It shifts in its chair: its chair's joints creaking, the legs moved an inch on the boards, the cutlery rattling once",
        """The same chair as its watching creak (the packs' real wooden creaks pitched up for a kitchen chair's joints), its
        legs moved an inch over the floorboards (stick-slip judder through deal: the packs have no chair) and knocking
        down (a real block of wood knocked), the table jolted so the cutlery rattles once on the plate (the packs' real
        loose-metal rattle and small clinks pitched up for plated steel, a real cup) and is stilled at once; in one take
        a long hand laid flat on the cloth to still it (a real leather drop, dulled). In its small furnished kitchen. Held
        to 300 Hz-5 kHz, like its rising.""",
        sources=CREAKS + WOOD_M + CUTLERY + [RATTLE] + CHINA + [LEATHER], takes=3, band=RISE, lufs=-20)
def shift_chair(rng, k):
    return shifts(rng, k, True)


@recipe("tell-householder", "shift", "joints",
        "It shifts in its chair, modelled: the chair's joints slipping, its legs juddering an inch, spoons jumping once",
        """Modelled, the same chair as its 'sinew' creak: slow stick-slip in the chair's joints through a small seat's dry
        modes, its legs moved an inch over the boards (stick-slip judder through deal) and knocking down square, the table
        jolted so the spoons (thin plated bars) and the plate's porcelain rim jump once, stilled at once. Three ways: a creak
        and a nudge, a nudge and a creak, two creaks as it leans. In its small furnished kitchen. Held to 300 Hz-5 kHz.""",
        takes=3, band=RISE, lufs=-20)
def shift_joints(rng, k):
    return shifts(rng, k, False)


# ---- A far bell (world-bell.far) ----------------------------------------------------------------------------------------

BELL_T60 = [14.0, 9.0, 7.0, 3.5, 5.0, 2.4, 2.0, 1.6, 1.1, 0.8]   # a small bronze bell's partials, how long each rings (s)
BELL_AMP = [0.55, 0.7, 0.65, 0.25, 1.0, 0.45, 0.35, 0.3, 0.22, 0.15]
FAR_BELL = [(0.31, -11, 60), (0.74, -15, 250), (1.35, -19, 500)]   # a treeline, a barn, a far hill's edge


def strikes(k):
    """When the bell is struck in each take, and how hard: once; twice as the bell swings back; twice, the second
    sooner and softer, as if whoever rang it let the rope go."""
    return [[(0.0, 1.0)], [(0.0, 1.0), (1.9, 0.85)], [(0.0, 1.0), (1.55, 0.65)]][k]


def bell_partials(rng, prime, length, force=1.0):
    """One strike of a small bronze bell, modelled: its partials at a minor-third bell's ratios (hum, prime, tierce,
    quint, nominal, and the upper ones; kit.BELL) a little mistuned as a cheap casting is, each a doublet a hair apart
    (no bell is quite round, so each beats), the lowest ringing longest; a harder strike brings out the upper ones; the
    clapper's iron clack on the bronze for the first few milliseconds."""
    n = samples(length)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float64)
    for r, T, a in zip(kit.BELL, BELL_T60, BELL_AMP):
        f = prime * r * (1 + rng.normal(0, 0.006))
        g = a * force ** (0.5 * r)
        ph = rng.uniform(0, 2 * np.pi)       # the strike sets both halves of a doublet going together: it beats from full
        for f_, w in ((f, 1.0), (f + rng.uniform(0.4, 2.2), rng.uniform(0.4, 0.9))):
            y += g * w * np.exp(-6.91 * t / T) * np.sin(2 * np.pi * f_ * t + ph)
    y[:samples(0.0015)] *= np.linspace(0, 1, samples(0.0015))
    clack = W.body(rng, rng.uniform(2300, 2800), W.PLATE, decay=0.02, contact=0.0004, length=0.08)
    return mix(W.norm(y.astype(np.float32)), W.norm(clack) * 0.25 * force)


def bell_peaks(x, count=10, lo=150, hi=2500):
    """A real bell's strongest partials (Hz, level), measured from a stretch of its ring past the strike's clang."""
    seg = x[samples(0.12):samples(0.9)]
    m = 2 ** 17
    S_ = np.abs(np.fft.rfft(seg * np.hanning(len(seg)), m))
    f = np.fft.rfftfreq(m, 1 / SR)
    pk, _ = signal.find_peaks(S_, distance=int(6 / (SR / m)))
    pk = [p for p in pk if lo < f[p] < hi]
    top = sorted(pk, key=lambda p: -S_[p])[:count]
    return [(float(f[p]), float(S_[p] / S_[top[0]])) for p in sorted(top)]


def bell_rung(rng, key, semis, hits, length):
    """A real bell struck and rung on: the packs' recording varispeeded `semis` for a stop's small bell, placed once per
    strike, and its own strongest partials (measured) rung on through high-Q resonators at exactly those frequencies,
    two to each a hair apart so they beat, the lowest ringing longest (nine seconds down to two), swelling up out of the
    recording's own ring as it dies so the real strike and the real metal stay what's heard."""
    x = W.norm(ck.align(dsp.vari(ck.get(key), semis)))
    dry = Bus(length)
    for at, force in hits:
        dry.at(at, lp(x, 2500 + 4000 * force, 1) * force)
    ps = bell_peaks(x)
    f0 = ps[0][0]
    ring = np.zeros(samples(length), np.float32)
    for f, a in ps:
        T = max(9.0 * (f0 / f) ** 0.8, 2.0)
        for f_, w in ((f, 1.0), (f + rng.uniform(0.4, 1.8), rng.uniform(0.4, 0.8))):
            r = dsp.resonate(dry.x, [f_], q=np.pi * f_ * T / 6.91)
            ring += (r / (np.max(np.abs(r)) + 1e-9) * a * w).astype(np.float32)
    # the ring a few dB under the recording's own at the strike, so the strike stays the attack and the ring carries on
    rms = lambda z, a, b: float(np.sqrt(np.mean(z[samples(a):samples(b)] ** 2)) + 1e-9)
    return mix(dry.x, ring * (0.3 * rms(dry.x, 0.02, 0.25) / rms(ring, 0.5, 0.9)))


def far_bell(rng, y, metres):
    """Heard `metres` off across open land at night, the last of the hum faded under the land's reverb by the take's end."""
    z = across(y, rng, metres, FAR_BELL, wet=0.4)
    L = len(y) / SR
    return cut_to(z, L, min(1.4, L * 0.3))


@recipe("world-bell", "far", "rung",
        "Something lost out there: a real bronze bell rung once or twice a long way off, its ring held on over the land",
        """The packs' real struck bell (Kenney's heavy bell, three of its strikes), varispeeded down a little for a stop's
        small bell, rung once, or twice as the bell swings back; its own strongest partials measured and rung on through
        high-Q resonators at exactly those frequencies (two to each a hair apart, so they beat as a bell does, the lowest
        ringing longest), so the real strike and metal stay what's heard. Then 800-1100 m of damp night air takes the
        clang's top off, its edges blur, a treeline, a barn and a far hill throw it back, and the land's reverb.""",
        sources=[BELLS[0], BELLS[1], BELLS[3]], takes=3, lufs=-21, gap=1.0)
def bell_far_rung(rng, k):
    L = (3.6, 4.8, 4.6)[k]
    y = bell_rung(rng, (BELLS[0], BELLS[1], BELLS[3])[k], (-2.0, -1.0, -3.0)[k], strikes(k), L)
    return far_bell(rng, y, rng.uniform(800, 1100))


@recipe("world-bell", "far", "partials",
        "Something lost out there, modelled: a small bronze bell's hum and minor-third tierce ringing a long way off",
        """Modelled: a small bronze bell's partials at a minor-third bell's ratios (the hum an octave under the prime, the
        tierce a minor third over it, the quint, the nominal and the upper ones), a little mistuned as a cheap casting is,
        each a doublet a hair apart so it beats, the hum ringing longest; the clapper's iron clack on the bronze. Rung
        once, or twice as it swings back. Then 800-1100 m of damp night air (the strike softened, the tierce and the hum
        ringing on), a treeline, a barn and a far hill throwing it back, and the land's reverb.""",
        takes=3, lufs=-21, gap=1.0)
def bell_far_partials(rng, k):
    L = (3.6, 4.8, 4.6)[k]
    prime = (470.0, 438.0, 505.0)[k] * rng.uniform(0.98, 1.02)
    b = Bus(L)
    for at, force in strikes(k):
        b.at(at, bell_partials(rng, prime, L - at, force), 20 * np.log10(force))
    return far_bell(rng, b.x, rng.uniform(800, 1100))


# ---- A far dog (world-dog.bark) -----------------------------------------------------------------------------------------

DOG = 0.8                    # a 20 kg farm dog's vocal tract as a share of a child's (synth.TRACT): a collie's, a cur's
FAR_DOG = [(0.21, -10, 40), (0.58, -16, 150)]     # the barn across the yard, the treeline behind


def bark_voice(rng, f0, L):
    """One bark, modelled: the throat snapping open on a burst of air, its pitch leaping up in the first 20-30 ms and
    falling away (a bark's inverted V), harsh (the folds' period doubling and a rough flutter, breath forced through
    each pulse), through a dog-sized tract whose jaw opens 'u' to 'a' and closes 'o' at the end."""
    pts = [(0, f0 * 0.7), (0.025, f0 * 1.1), (0.06, f0), (L * 0.75, f0 * 0.82), (L, f0 * 0.62)]
    g = synth.glottis(env(pts, L, "exp"), L, rng, jitter=0.03, shimmer=0.3, oq=0.45, sub=rng.uniform(0.25, 0.5),
                      rough=rng.uniform(0.35, 0.55))
    src_ = g + synth.noise(L, rng) * (0.25 + 0.75 * lp(np.abs(g), 300)) * 0.5
    v = synth.tract(src_, [(0, "u"), (0.012, "a"), (L * 0.65, "a"), (L, "o")], DOG, breath=0.3, rng=rng)
    pop = synth.tract(synth.noise(0.03, rng) * env([(0, 1), (0.03, 0)], 0.03, "exp"), "a", DOG)
    y = dsp.saturate(W.norm(mix(W.norm(v), W.norm(pop) * 0.4)), 6)
    return y * bark_env(L)


def bark_env(L):
    """A bark's shape: in at once, loudest in its first 30 ms, falling away through the rest."""
    return env([(0, 0), (0.003, 1), (0.03, 1), (L * 0.5, 0.55), (L * 0.85, 0.3), (L, 0)], L)


def growl_voice(rng, L):
    """A growl between barks, modelled: a low throat (120-150 Hz) rattling in a growl's irregular pulses, closed to 'u'."""
    f = rng.uniform(120, 150)
    g = synth.glottis(env([(0, f * 0.9), (L * 0.4, f), (L, f * 0.85)], L, "exp"), L, rng, jitter=0.04, shimmer=0.3, sub=0.5)
    x = g * (0.3 + 0.7 * fry(rng, rng.uniform(28, 38), L)) + synth.noise(L, rng) * fry(rng, 34, L) * 0.3
    v = synth.tract(x, "u", DOG, breath=0.2, rng=rng)
    return dsp.saturate(W.norm(v) * env([(0, 0), (0.08, 1), (L * 0.7, 0.8), (L, 0)], L), 4)


@functools.lru_cache(maxsize=1)
def _roar():
    x = ck.get(ROAR)
    return x[samples(0.12):samples(0.42)].copy()    # the roar's full-throated middle


def bark_rec(rng, f0, L):
    """One bark from a real throat: a slice of the packs' roar sped up (varispeed, so its throat shrinks with its pitch:
    about eight semitones, more or less with the bark's own pitch, takes a big cat's 130 Hz roar to a big farm dog's
    200 Hz woof), its pitch bent into a bark's leap and fall, snapped open on the real gasp's burst of air, the jaw's
    opening drawn on it ('a' resonances swelling in)."""
    semis = 8 + 12 * np.log2(f0 / 480)
    x = _roar()
    a = int(rng.integers(0, len(x) - samples(0.28)))
    g = dsp.vari(x[a:a + samples(0.28)], semis)
    g = dsp.fit(bend(g, env([(0, -5), (0.025, 1.5), (0.06, 0), (L, -6)], L * 1.5)), samples(L))
    g = mix(g, dsp.peak(dsp.peak(g, 750, 2.5, 7), 1500, 3, 4) * 0.6)
    gasp = dsp.vari(ck.cut(ck.get(GASP), 0, samples(0.04), 0.001, 0.02), rng.uniform(-3, 0))
    y = dsp.saturate(W.norm(mix(W.norm(g), dsp.fit(W.norm(hp(gasp, 600)), samples(L)) * 0.3)), 4)
    return y * bark_env(L)


def growl_rec(rng, L):
    """A growl between barks from the real roar: a slice held out (paulstretched) and dropped near a dog's growl, gated
    in a growl's irregular pulses and closed down (lowpassed)."""
    x = dsp.vari(_roar(), -3)
    y = dsp.fit(dsp.smear(x, (L + 0.2) / (len(x) / SR), 0.06, rng), samples(L))
    y = lp(y, 1400) * (0.3 + 0.7 * fry(rng, rng.uniform(28, 38), L))
    return W.norm(y) * env([(0, 0), (0.08, 1), (L * 0.7, 0.8), (L, 0)], L)


def bout(rng, k, bark, growl):
    """Three ways a farm dog barks at something at the edge of its yard: three urgent barks, the last a little lower;
    two barks, a low growl, two more quick; four barks fast and uneven. Each bark its own pitch and length."""
    f0 = rng.uniform(430, 520)
    if k == 0:
        plan = [(0.0, 1.0, 0), (0.36, 1.05, -1), (0.7, 0.9, -2)]
        gr = None
    elif k == 1:
        plan = [(0.0, 1.05, 0), (0.33, 1.0, -1), (1.25, 1.1, 0), (1.5, 1.0, -2)]
        gr = (0.62, 0.5)
    else:
        plan = [(0.0, 1.0, 0), (0.26, 1.08, 0), (0.6, 0.95, -1), (0.84, 1.0, -3)]
        gr = None
    b = Bus(3.0)
    for t, p, db in plan:
        b.at(t + rng.uniform(-0.02, 0.02), bark(rng, f0 * p * rng.uniform(0.97, 1.03), rng.uniform(0.13, 0.19)), db)
    if gr:
        b.at(gr[0], growl(rng, gr[1]), -9)
    return b.x


def far_dog(rng, y):
    """A couple of hundred metres off across the fields: the air, the hedges, the barn and the treeline throwing it
    back, a little of the night (open land rings little: the echoes are its space)."""
    z = across(y, rng, rng.uniform(170, 240), FAR_DOG, wet=0.2)
    return cut_to(z, min(3.0, len(dsp.trim_silence(z, -45)) / SR), 0.5)


@recipe("world-dog", "bark", "collie",
        "Something at the lamps' edge: a farm dog barking at it across the fields at night, two to four urgent barks",
        """Modelled, no dog in the packs: each bark a throat (a 430-520 Hz voice) snapping open on a burst of air, its pitch
        leaping up in the first 25 ms and falling away, harsh with the folds' period doubling and breath forced
        through each pulse, through a 20 kg dog's tract with the jaw opening 'u' to 'a' and closing. Three urgent barks;
        two, a low growl, two more; four fast and uneven. Then 170-240 m of night air and hedges, the barn and the
        treeline throwing it back. A dog at a distance; close up the model would show.""",
        takes=3, lufs=-21, gap=1.0)
def dog_bark_collie(rng, k):
    return far_dog(rng, bout(rng, k, bark_voice, growl_voice))


@recipe("world-dog", "bark", "roar",
        "Something at the lamps' edge: a big farm dog woofing far off, its barks a real roar sped up to a dog's size",
        """Kitbashed from a real throat, no dog in the packs: each bark a slice of the packs' roar sped up about eight
        semitones (varispeed, so the throat shrinks with the pitch: a big cat's 130 Hz roar becomes a big farm dog's 200 Hz
        woof, deeper than 'collie'), its pitch bent into a bark's leap and fall, the jaw's opening drawn on it, snapped
        open on a real gasp's burst of air; the growl the same roar held out and gated into a growl's rattle. Three
        urgent barks; two, a growl, two more; four fast. Then 170-240 m of night air and hedges, the barn and the
        treeline throwing it back.""",
        sources=[ROAR, GASP], takes=3, lufs=-21, gap=1.0)
def dog_bark_roar(rng, k):
    return far_dog(rng, bout(rng, k, bark_rec, growl_rec))
