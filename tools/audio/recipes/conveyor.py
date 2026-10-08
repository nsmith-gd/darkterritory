"""The conveyor line heard (queue #202, note 466; A1's #446, note 400: "its sound is AU1's: the belt, the drive and the
jam"). The grain elevator's belt runs low on its trestles from its drive house up a riser to its head over the track. It's
started at the drive house, jams every 30-60 s of carrying, and a jam left 20 s stalls it.

The drive house is drawn as an engine house (C1's drive_house: an exhaust stack through its roof, sooted, a flywheel and
its guard down the side), so the drive is a stationary oil engine. It has one big cylinder at 300 rpm, firing every other
turn, its flywheel carrying it between, and it's started off the yard's power: the starter is held while it cranks the
engine over, and when it catches the clutch goes in and the belt takes up.

- The starter (`cranking`): the starter motor whining against the engine's compression, slowing on each one, its pinion
  grinding in the flywheel's ring, the engine breathing out of its stack.
- It catches (`catch`): a first hard pop with the starter still in, the pinion thrown out and its whine running down, the
  pops coming quicker as it gathers, the clutch thrown in and the belt taking up with a groan.
- Running (`engine`): its slow pop-pop out of the stack, the crank's knock and the tappets with each one, the flywheel's
  swing and the drive belt's joint slapping round, the hut's tin buzzing with the beat.
- Labouring (`labour`): jammed, the belt held at the jam and slipping on the drive drum, squealing and juddering, the
  engine bogged down under it: slower, heavier pops.
- The belt (`belt`): the troughing idlers rumbling under the load, a dry bearing squeaking, the belt's splice ticking over
  each idler, the grain shifting on it.
- The head (`pour`): grain off the head pulley down the short chute and into the car, the pulley rumbling in its hood.
- A jam (`jam`): the grain heaping, the belt bunching and riding up onto the stringer with a bang, rubber dragged on
  steel, grain spilling over, the belt shuddering to a stop.
- Clearing it (`clearing`): grain dug out by the double handful and flung down, the bunched belt hauled at, the idler
  frame knocked.
- Free (`free`): the belt jerking free with a slap and running on, grain whooshing off the heap.
- The stall (`stall`): the engine dragged down by the slipping belt, its pops slower and weaker, a last cough, the
  flywheel coasting and the belt going slack.

The machinery's iron is the packs' real metal (pitched to its size, choked where it's bolted); the exhaust, the starter
and the belt's rubber are modelled. The loops stay low (the drive's under 2 kHz, the belt's grain soft): spec A.4 rule 1
keeps the tells' 2-6 kHz clear, and a stop's work goes on round the crew while they listen for what's coming.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter
from recipes.crew_kit import recipe, K
from recipes.jobs import strike, drum
from recipes.world_bed import outdoors, circ_outdoors

L = "place-conveyor"
SR = dsp.SR
METAL_H, METAL_M, METAL_L = K("impactMetal_heavy"), K("impactMetal_medium"), K("impactMetal_light")
PLATE_M = K("impactPlate_medium")
STACK = [50, 150, 250, 350]           # the exhaust's 1.7 m stack, closed at the engine: its odd quarter-wave modes
HUT = [310, 520, 870, 1240, 1710]     # the drive house's corrugated sheets
DRUM = [180, 410, 690, 1100]          # the tail drum and its frame, rung by the slipping belt
CHUTE = [330, 610, 980, 1450]         # the head's short discharge chute
REV = 5.0                             # the engine's turns a second (300 rpm)
FIRE = REV / 2                        # a four-stroke fires every other turn
ENGINE = 8.0                          # 20 firings, 40 turns


def under(y, f=2000, order=4):
    """Kept out of the tells' band (spec A.4 rule 1): a steep circular lowpass, so a loop stays a loop."""
    return cfilter(y, lambda x: 1 / (1 + (x / f) ** order))


def periodic_tone(n, freq, harm=(1.0,)):
    """A tone following `freq` (a per-sample curve) that makes a whole number of cycles over n, so it loops."""
    cycles = np.sum(freq) / SR
    freq = freq * (max(1, round(cycles)) / cycles)
    ph = 2 * np.pi * np.cumsum(freq) / SR
    return sum(a * np.sin((i + 1) * ph + i * 0.9) for i, a in enumerate(harm)).astype(np.float32)


def pop(rng, hard=0.6):
    """One firing out of the stack: the exhaust valve opening on the burnt charge, a pressure pulse rising in a couple of
    ms and gone in a few tens, its gas noise rung by the stack's quarter-wave modes, and the pulse leaving the stack's
    mouth as a thump. Harder under load: a sharper, longer pulse."""
    n = samples(0.35)
    t = np.arange(n) / SR
    rise = rng.uniform(0.0015, 0.003) / (0.6 + 0.4 * hard)
    decay = rng.uniform(0.022, 0.034) * (0.8 + 0.5 * hard)
    p = (1 - np.exp(-t / rise)) * np.exp(-t / decay)
    p = (p / p.max()).astype(np.float32)
    noise = rng.standard_normal(n).astype(np.float32)
    gas = lp(noise * p, 2000, 2)
    k = rng.uniform(0.95, 1.05)
    pipe = dsp.resonate(gas, [f * k for f in STACK], q=4, gains=[1, 0.7, 0.5, 0.35])
    dq = lp(np.diff(np.sqrt(p), prepend=0).astype(np.float32) * SR / 1000, 300, 2)
    thump = dsp.resonate(dq, [STACK[0] * k], q=2) * 0.6 + dq
    # The valve's crack as the charge gets out: the first few ms of the gas, in the mids where small speakers hear a pop.
    crack = bp(noise * np.exp(-t / rng.uniform(0.004, 0.007)).astype(np.float32), 400, 1800, 2)
    return norm(norm(gas) * 0.35 + norm(pipe) * 0.55 + norm(thump) * (0.55 + 0.25 * hard) + norm(crack) * (0.25 + 0.15 * hard))


def knock(rng, i, weight=1.0):
    """The crank and its big end taking the firing: a heavy iron knock pitched well down and choked (it's in oil)."""
    return strike(METAL_H[i % 5], -rng.uniform(8, 10), lo=70, at=0.008, tau=0.018) * weight


def tappet(rng, i):
    """A valve's tappet: a small dry tick of iron on iron, choked."""
    return lp(strike(METAL_L[i % 5], -rng.uniform(3, 5), lo=300, at=0.003, tau=0.006), 2400, 2)


def swing(n, rng, rate):
    """The flywheel going round: a low swish swelling once a turn (its rim's not true), and the air off its spokes."""
    t = np.arange(n) / SR
    cyc = max(1, round(rate * n / SR)) / (n / SR)
    band = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(260)) / 0.7) ** 2))
    return norm(band * (0.55 + 0.45 * np.cos(np.pi * cyc * t) ** 2).astype(np.float32))


def slaps(n, rng, count, level=1.0):
    """The flat drive belt's laced joint slapping the pulley once a round: a soft leathery thud."""
    hits = []
    for j in range(count):
        m = samples(0.08)
        x = rng.standard_normal(m).astype(np.float32) * env([(0, 0), (0.002, 1), (0.08, 0)], 0.08, "exp")[:m]
        hits.append((j * (n / SR) / count + rng.uniform(-0.01, 0.01), norm(lp(x, 500, 2)), rng.uniform(0.6, 1.0) * level))
    return W.place(n, hits)


def hut_rattle(x):
    """The hut's tin buzzing with what's in it (circular, for a loop)."""
    return W.cyclic(lambda z: dsp.resonate(z, HUT, q=22, gains=[1, 0.8, 0.6, 0.45, 0.3]), x)


def beat(n, rng, fires, hard, jitter=0.004):
    """The engine's firings over a loop of n samples, `fires` of them, the crank's knock and the tappets with each."""
    pops = [pop(rng, hard) for _ in range(5)]
    span = n / SR
    ev, mech = [], []
    for i in range(fires):
        t = i * span / fires + rng.uniform(-jitter, jitter)
        ev.append((t, pops[i % 5], rng.uniform(0.85, 1.0)))
        mech.append((t + 0.006, knock(rng, i, 0.5 + 0.5 * hard), rng.uniform(0.7, 1.0)))
        mech.append((t - 0.21 * span / fires, tappet(rng, i), rng.uniform(0.5, 0.8)))
        mech.append((t + 0.45 * span / fires, tappet(rng, i + 2), rng.uniform(0.4, 0.7)))
    return W.place(n, ev), W.place(n, mech)


@recipe(L, "engine", "oil-engine",
        "The belt's drive running: a big single-cylinder oil engine's slow pop-pop out of its stack, the flywheel swinging",
        """In the drive house at the belt's tail: one big cylinder at 300 rpm firing every other turn, a slow pop-pop out
        of the stack through its roof (a pressure pulse through the stack's quarter-wave modes, and the thump of it
        leaving the mouth), the crank knocking with each firing (real heavy iron, pitched well down and choked in its
        oil), the tappets ticking between, the flywheel's swing once a turn, the drive belt's laced joint slapping round,
        and the hut's corrugated tin buzzing with the beat. All under 2 kHz. 8 s exact cycle, 20 firings.""",
        sources=METAL_H + METAL_L, loop=True, takes=1, lufs=-21, seconds=ENGINE)
def engine(rng, k):
    n = samples(ENGINE)
    pops, mech = beat(n, rng, int(FIRE * ENGINE), 0.6)
    y = norm(pops) * 0.8 + norm(mech) * 0.22 + swing(n, rng, REV) * 0.12 + slaps(n, rng, 10) * 0.18 \
        + norm(hut_rattle(pops)) * 0.12
    return seamless(circ_outdoors(under(y), rng, 0.12))


@recipe(L, "labour", "slip",
        "Jammed: the belt slipping on the drive drum, squealing and juddering, the engine bogged down and labouring",
        """The belt held fast at a jam and the drum turning under it: the rubber slipping and catching on the drum (a
        self-excited squeal near 700 Hz coming and going with each catch, and a judder twenty times a second rung through
        the drum and its frame), a smell-of-it hiss of rubber burning; and the engine dragged down under it, firing every
        time and heavier, its beat slowed from 2.5 to 2.25 a second and the crank knocking hard. Kept under 2.2 kHz. 8 s
        exact cycle.""",
        sources=METAL_H + METAL_L, loop=True, takes=2, lufs=-19, seconds=ENGINE)
def labour(rng, k):
    n = samples(ENGINE)
    pops, mech = beat(n, rng, 18, 1.0, jitter=0.012)
    # The belt catching and letting go on the drum: bursts of squeal with the judder under them, a few a second.
    t = np.arange(n) / SR
    catch = np.clip(0.5 + 0.8 * slow(n, 3.0, rng) + 0.3 * np.sin(2 * np.pi * round(2.25 * ENGINE) / ENGINE * t), 0, 1)
    f0 = rng.uniform(640, 760) * (1 + 0.012 * slow(n, 4, rng))
    squeal = periodic_tone(n, f0, (1.0, 0.35, 0.15)) * catch ** 1.5
    jud = W.friction(n, rng, DRUM, rough=2.0, grit=0.2, q=12, loop=True)
    rate = round(20 * ENGINE) / ENGINE
    jud = jud * (0.4 + 0.6 * np.sin(np.pi * rate * t) ** 2).astype(np.float32) * (0.3 + 0.7 * catch)
    burn = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(1500)) / 0.5) ** 2)) * catch
    y = norm(pops) * 0.75 + norm(mech) * 0.3 + norm(squeal) * (0.35 + 0.1 * k) + norm(jud) * 0.3 + norm(burn) * 0.06 \
        + swing(n, rng, REV * 0.9) * 0.1 + norm(hut_rattle(pops)) * 0.15
    return seamless(circ_outdoors(under(y, 2200), rng, 0.12))


@recipe(L, "cranking", "starter",
        "The starter held: its motor whining against the engine's compression, slowing on each, the pinion grinding",
        """Cranking the oil engine over off the yard's power: the starter motor's hum and its pinion's whine in the
        flywheel's ring gear, slowing as each compression comes up and racing as it goes over (a pitch that dips 25 %),
        the pinion grinding under the load, and the engine breathing out of its stack once a compression, the flywheel's
        swing slow under it. Held as long as the starter is; 4 s exact cycle, five compressions.""",
        loop=True, takes=1, lufs=-22, seconds=4.0)
def cranking(rng, k):
    T = 4.0
    n = samples(T)
    t = np.arange(n) / SR
    comp = 5
    ph = (t * comp / T) % 1.0
    load = np.exp(-0.5 * ((ph - 0.55) / 0.12) ** 2)                 # each compression coming up, then over
    speed = (1 - 0.25 * load) * (1 + 0.01 * slow(n, 3, rng))
    hum = periodic_tone(n, 120 * speed, (1.0, 0.5, 0.3, 0.2))
    whine = periodic_tone(n, 860 * speed, (1.0, 0.25))
    grind = W.friction(n, rng, [480, 760, 1150], rough=1.5, grit=0.3, q=10, loop=True) * (0.3 + 0.7 * load).astype(np.float32)
    breaths = W.place(n, [(j * T / comp + 0.7 * T / comp, lp(pop(rng, 0.0), 600, 2), 0.5) for j in range(comp)])
    y = norm(hum) * 0.45 + norm(whine) * 0.22 + norm(grind) * 0.18 + norm(breaths) * 0.35 + swing(n, rng, 2.0) * 0.1
    return seamless(circ_outdoors(under(y, 2600), rng, 0.12))


@recipe(L, "catch", "fires",
        "The engine catching: a hard first pop, the starter thrown out, the pops quickening, the clutch in and the belt taking up",
        """The end of the starter's hold: the engine fires with the starter still in (a hard, ragged first pop), the
        pinion's thrown out of the ring with a clack and its whine runs down, the next pops come slow and uneven and then
        quicker as the flywheel gathers, until it's at its beat; then the clutch lever's thrown (heavy iron, pitched
        down) and the belt takes up with a long rubbery groan off the drum, the next pops heavier under the load. Loud:
        the yard hears it. About 3.5 s, into the running engine.""",
        sources=METAL_H + METAL_M + METAL_L, takes=3, lufs=-19)
def catch(rng, k):
    T = 3.8
    b = dsp.Bus(T + 1.2)
    # The starter still in, then out: its whine running down.
    m = samples(0.7)
    sp = env([(0, 1.0), (0.12, 1.0), (0.7, 0.35)], 0.7)[:m]
    b.at(0, norm(periodic_tone(m, 120 * sp, (1.0, 0.5, 0.3)) * 0.6 + periodic_tone(m, 860 * sp, (1.0,)) * 0.25)
         * env([(0, 1), (0.12, 1), (0.7, 0)], 0.7)[:m] * 0.35)
    b.at(0.12, strike(METAL_L[k % 5], -2, lo=300, at=0.01, tau=0.02) * 0.4)
    # The firings: ragged and slow, then quickening to its beat (2.5 a second).
    gaps = [0.55, 0.42, 0.36, 0.48 if k == 1 else 0.33, 0.6 if k == 1 else 0.31, 0.3, 0.32, 0.36, 0.4, 0.4, 0.4]
    t, i = 0.03, 0
    while t < T - 0.3:
        hard = 1.0 if i == 0 else (0.85 if t > 2.4 else 0.5)
        lvl = 1.0 if i == 0 else (0.6 if (k == 1 and i == 3) else 0.85)
        b.at(t, pop(rng, hard) * lvl)
        b.at(t + 0.006, knock(rng, i, 0.6 + 0.4 * hard) * 0.25)
        t += gaps[min(i, len(gaps) - 1)] * rng.uniform(0.95, 1.05)
        i += 1
    # The clutch thrown in, and the belt taking up off the drum.
    c = 2.2 + 0.15 * k
    b.at(c, strike(METAL_H[(k + 2) % 5], -5, lo=90, at=0.05, tau=0.06) * 0.45)
    g = synth.creak(1.1, env([(0, 18), (0.5, 45), (1.1, 70)], 1.1), rng, body=[f * 0.7 for f in DRUM], q=9, jitter=0.4)
    b.at(c + 0.08, norm(g * env([(0, 0), (0.1, 1), (0.8, 0.7), (1.1, 0)], 1.1)[:len(g)]) * 0.3)
    n = len(b.x)
    sw = swing(n, rng, REV) * fit(env([(0, 0.2), (2.0, 1), (T, 1), (T + 1.2, 0)], T + 1.2), n)
    b.at(0, sw * 0.12)
    y = lp(b.x, 2500, 2) * fit(env([(0, 1), (T - 0.3, 1), (T + 0.4, 0)], T + 1.2), n)
    return outdoors(y, rng, 0.12)


@recipe(L, "stall", "dies",
        "The drive stalling: the engine dragged down by the jammed belt, its pops slower and weaker, a last cough, the flywheel coasting",
        """A jam left too long: the engine can't turn the drum against the belt and is dragged down: its pops slower and
        heavier then weaker (the intervals stretching from 0.45 to over a second), the belt's squeal on the drum dying
        with it, a last cough out of the stack, the flywheel coasting a few turns on its own and the drive belt going
        slack with a slap. Then quiet, and someone has to go back and start it. About 4 s.""",
        sources=METAL_H + METAL_L, takes=2, lufs=-20)
def stall(rng, k):
    T = 4.2
    b = dsp.Bus(T + 1.2)
    t, i, gap = 0.0, 0, 0.45
    fires = 5 + k
    for i in range(fires):
        w = 1 - i / fires
        b.at(t, pop(rng, 0.9 * w + 0.1) * (0.5 + 0.5 * w))
        b.at(t + 0.008, knock(rng, i, 0.4 + 0.6 * w) * 0.3)
        t += gap
        gap *= rng.uniform(1.18, 1.3)
    # The last cough: a weak, broken firing.
    b.at(t, lp(pop(rng, 0.2), 700, 2) * 0.4)
    last = t
    # The squeal on the drum slowing with it.
    m = samples(min(last, T))
    sp = env([(0, 1.0), (m / SR, 0.55)], m / SR)[:m]
    sq = periodic_tone(m, rng.uniform(640, 720) * sp, (1.0, 0.3)) * env([(0, 0.8), (m / SR * 0.6, 0.5), (m / SR, 0)], m / SR)[:m]
    b.at(0, norm(sq) * 0.18)
    # The flywheel coasting down, and the belt going slack.
    n = len(b.x)
    sw = swing(n, rng, 3.0) * fit(env([(0, 1), (last, 0.7), (T + 0.8, 0)], T + 1.2), n)
    b.at(0, sw * 0.14)
    b.at(last + 0.5, norm(lp(rng.standard_normal(samples(0.1)).astype(np.float32)
                             * env([(0, 0), (0.002, 1), (0.1, 0)], 0.1, "exp")[:samples(0.1)], 450, 2)) * 0.4)
    b.at(last + 0.55, strike(METAL_L[(k + 1) % 5], -6, lo=200, at=0.005, tau=0.01) * 0.08)
    return outdoors(lp(b.x, 2500, 2), rng, 0.12)


@recipe(L, "belt", "idlers",
        "The belt running: its idlers rumbling under the load, a dry bearing squeaking, the splice ticking over, grain shifting",
        """Beside the low run: the troughed belt on its idlers a metre apart, their bearings rumbling under the load (low
        noise and a few faint roller tones wandering), one dry bearing chirping once a turn, the belt's laced splice
        ticking as it crosses an idler, the slack return rubbing its roller underneath, and the grain riding on it,
        shifting and hissing softly. Kept low. 6 s exact cycle.""",
        sources=METAL_L, loop=True, takes=2, lufs=-27, seconds=6.0)
def belt(rng, k):
    T = 6.0
    n = samples(T)
    t = np.arange(n) / SR
    rumble = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(170)) / 0.9) ** 2)) \
        * np.clip(1 + 0.25 * slow(n, 1.5, rng), 0.3, None)
    rollers = W.howl(n, rng, [rng.uniform(300, 420), rng.uniform(520, 680), rng.uniform(760, 900)], 1.0, wander=0.01, rate=0.4, q=25)
    rate = round(4.8 * T) / T                       # a 0.1 m roller at 1.5 m/s
    chirps = W.place(n, [(j / rate, synth.click(rng.uniform(1050, 1250), q=6, length=0.03, rng=rng), 1.0)
                         for j in range(int(rate * T))]) * np.clip(0.4 + 0.8 * slow(n, 0.5, rng), 0, 1).astype(np.float32)
    ticks = W.place(n, [(j / 1.5, tappet(rng, j), rng.uniform(0.5, 0.9)) for j in range(int(1.5 * T))])
    ret = W.friction(n, rng, [220, 470], rough=0.6, grit=0.1, q=8, loop=True)
    grain = W.pour(n, rng, 2500 + 400 * slow(n, 0.6, rng), grain=(1500, 5000), lump=0, thunder=0.1, voices=16, decay=0.003)
    y = norm(rumble) * 0.55 + norm(rollers) * 0.12 + norm(chirps) * 0.08 + norm(ticks) * 0.18 + norm(ret) * 0.08 \
        + norm(under(grain, 3500)) * (0.18 + 0.06 * k)
    return seamless(circ_outdoors(under(y, 3500), rng, 0.15))


@recipe(L, "pour", "head",
        "Grain off the belt's head: a steady stream down the short chute, drumming into the car, the head pulley rumbling",
        """At the head over the track: the grain thrown off the head pulley down a short steel chute into the car under
        it, a lighter stream than the spout's (a car in 25 s: the pour model's kernels, the chute's few modes ringing
        under it), landing on the heap and drumming the car's floor and sides low, and the head pulley rumbling in its
        hood above, swelling once a turn. 8 s exact cycle.""", loop=True, takes=1, lufs=-23, seconds=8.0)
def pour(rng, k):
    T = 8.0
    n = samples(T)
    flow = np.clip(14000 + 2000 * slow(n, 0.6, rng), 4000, None)
    p = W.pour(n, rng, flow, grain=(1200, 8000), lump=0, thunder=0.4, voices=24, decay=0.003)
    chute = W.cyclic(lambda z: dsp.resonate(z, CHUTE, q=12), p)
    car = W.cyclic(lambda z: dsp.resonate(z, [f * 0.35 for f in synth.WOOD[:4]], q=6), lp(p, 800))
    y = norm(p) * 0.6 + norm(chute) * 0.3 + norm(car) * 0.35 + drum(n, rng, 6) * 0.15
    return seamless(circ_outdoors(under(y, 7000), rng, 0.15))


@recipe(L, "jam", "bunch",
        "The belt jamming: grain heaping, the belt bunching and riding up onto the stringer with a bang, rubber dragged on steel",
        """Somewhere along the low run the load won't pass: the grain heaps up with a rush, the belt bunches behind it and
        rides up off its idlers onto the stringer with a bang (real medium plate and iron, pitched down for a 3 m
        section), the rubber dragged screeching over the steel edge, grain spilling over the side onto the ground, and
        the belt shuddering to a stop. About 2 s.""",
        sources=PLATE_M + METAL_M, takes=3, lufs=-21)
def jam(rng, k):
    T = 2.2
    b = dsp.Bus(T + 1)
    n = samples(T)
    heap = W.pour(n, rng, env([(0, 3000), (0.15, 16000), (0.6, 9000), (1.4, 1500), (T, 50)], T), grain=(1200, 6000),
                  lump=0, thunder=0.5, loop=False, voices=16, decay=0.003)
    b.at(0, norm(heap) * 0.4)
    bang = 0.25 + 0.1 * k
    b.at(bang, strike(PLATE_M[k % 5], -rng.uniform(4, 6), lo=120, at=0.08, tau=0.08) * 0.8)
    b.at(bang + 0.01, strike(METAL_M[(k + 1) % 5], -rng.uniform(3, 5), lo=150, at=0.04, tau=0.05) * 0.5)
    m = samples(0.9)
    drag = W.friction(m, rng, [260, 560, 980, 1500], rough=2.2, grit=0.3, q=16) * env([(0, 0), (0.05, 1), (0.5, 0.6), (0.9, 0)], 0.9)[:m]
    b.at(bang + 0.05, norm(lp(drag, 2500, 2)) * 0.35)
    spill = W.pour(samples(1.4), rng, env([(0, 200), (0.2, 5000), (1.4, 100)], 1.4), grain=(400, 2500), lump=0,
                   thunder=0.6, loop=False, voices=12, decay=0.004)
    b.at(bang + 0.2, norm(lp(spill, 2500, 2)) * 0.3)
    shudder = lp(rng.standard_normal(samples(0.8)).astype(np.float32), 200, 2) \
        * (0.5 + 0.5 * np.sin(2 * np.pi * 14 * np.arange(samples(0.8)) / SR)).astype(np.float32) \
        * env([(0, 1), (0.8, 0)], 0.8, "exp")[:samples(0.8)]
    b.at(bang + 0.1, norm(shudder) * 0.3)
    return outdoors(lp(b.x, 6000, 2), rng, 0.15)


@recipe(L, "clearing", "hands",
        "Clearing the jam by hand: grain dug out and flung down, the bunched belt hauled at, the idler frame knocked",
        """Someone at the jam: grain dug out of the heap by the double handful and flung down onto the ground (short
        bursts of kernels and a soft landing), the bunched belt's rubber hauled at and creaking as it stretches, a knee or a
        fist against the idler frame now and then. Held while they're at it; 5 s exact cycle.""",
        sources=METAL_L, loop=True, takes=1, lufs=-25, seconds=5.0)
def clearing(rng, k):
    T = 5.0
    n = samples(T)
    ev = []
    t = 0.2
    while t < T - 0.3:
        m = samples(0.5)
        scoop = W.pour(m, rng, env([(0, 500), (0.08, 9000), (0.3, 2000), (0.5, 0)], 0.5), grain=(1200, 5000), lump=0,
                       thunder=0.3, loop=False, voices=10, decay=0.003)
        ev.append((t, dsp.fade(norm(scoop), 0.04, 0.1) * rng.uniform(0.6, 0.9), 1.0))
        land = W.pour(samples(0.4), rng, env([(0, 0), (0.05, 6000), (0.4, 0)], 0.4), grain=(400, 1800), lump=0,
                      thunder=0.6, loop=False, voices=8, decay=0.004)
        ev.append((t + 0.35, dsp.fade(norm(land), 0.01, 0.08) * 0.5, 1.0))
        t += rng.uniform(0.7, 1.1)
    hauls = []
    for t in (1.1, 3.4):
        g = synth.creak(0.7, env([(0, 15), (0.35, 40), (0.7, 20)], 0.7), rng, body=[f * 0.55 for f in DRUM], q=7, jitter=0.5)
        hauls.append((t + rng.uniform(-0.1, 0.1), norm(g * env([(0, 0), (0.15, 1), (0.7, 0)], 0.7)[:len(g)]), 0.6))
    knocks = [(t, strike(METAL_L[j % 5], -7, lo=150, at=0.01, tau=0.02), 0.35) for j, t in enumerate((2.3, 4.4))]
    y = norm(W.place(n, ev)) * 0.7 + norm(W.place(n, hauls)) * 0.35 + norm(W.place(n, knocks)) * 0.25
    return seamless(circ_outdoors(under(y, 5000), rng, 0.15))


@recipe(L, "free", "slap",
        "The jam cleared: the belt jerking free with a slap, running on, the grain sliding off the heap",
        """The jam pulled clear and the drum's pull let go all at once: the belt jerks straight with a slap of rubber on
        the idlers, they spin up again with a rising whirr, and what's left of the heap slides off down the belt with a
        whoosh. About 1.5 s.""", sources=METAL_L, takes=3, lufs=-22)
def free(rng, k):
    T = 1.6
    b = dsp.Bus(T + 1)
    m = samples(0.12)
    whip = rng.standard_normal(m).astype(np.float32) * env([(0, 0), (0.001, 1), (0.12, 0)], 0.12, "exp")[:m]
    b.at(0, norm(mix(lp(whip, 3000, 2), lp(whip, 250, 2) * 2)) * 0.8)
    b.at(0.02, strike(METAL_L[k % 5], -5, lo=200, at=0.01, tau=0.03) * 0.3)
    n = samples(T)
    t = np.arange(n) / SR
    sp = np.clip(t / 0.8, 0, 1)
    whirr = cfilter(rng.standard_normal(n).astype(np.float32), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(300)) / 0.8) ** 2)) \
        * (sp * env([(0, 1), (T * 0.7, 1), (T, 0)], T)[:n]).astype(np.float32)
    b.at(0, norm(whirr) * 0.3)
    slide = W.pour(n, rng, env([(0, 200), (0.1, 12000), (0.7, 4000), (T, 0)], T), grain=(1200, 6000), lump=0,
                   thunder=0.4, loop=False, voices=12, decay=0.003)
    b.at(0.05, norm(slide) * 0.4)
    return outdoors(lp(b.x, 6000, 2), rng, 0.15)
