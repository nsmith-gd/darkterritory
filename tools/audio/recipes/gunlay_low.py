"""crew-gun-lay, low and slow: the roof cannon's steam laying gear as the seated gunner hears it turning the gun. The
director on the first two (warnings.py's `engine` and `works`), GDD App. F.3, 7 Oct: "a weird high repeated sound ...
make it something that's a lot more low and slow." Those were a quick little engine (ten bright chuffs and fifteen
ratchet teeth a second); these two follow the game's synth after that note (content/audio/sounds/gun-lay.json): a deep
gear clunk two or three times a second, the steam a low breath, a motor's hum carrying it.

The real thing: a small steam motor in an iron casing on the car roof drives a worm, and the worm turns the gun's big
toothed ring. A worm drive is slow by nature (a whole turn of the worm moves the ring one tooth), so what the gunner hears
is not a whirr but the load: each stroke of the motor takes up the slack between worm and wheel and the wheel's teeth
come home with a heavy, dull clunk through the casing; the motor hums and labours under it; its exhaust breathes.

Everything is the packs' real recordings, cut, pitched down and darkened: heavy plate thumps, an iron door slam, a pick
on rock and struck iron for the clunks, their machine loops for the motor, their steam for the breath, a scraped iron
pot for the worm grinding under load. Almost nothing passes 800 Hz. Both loop one exact cycle (events placed modulo the
cycle, the recordings tiled into it with crossfades, reverb circular); the game plays them at 0.8-1.2x with the turn's
speed, so a clunk rate of 2-2.4 a second becomes 1.6-2.9.
"""

import numpy as np
from scipy.ndimage import minimum_filter1d

import dsp
from build import recipe
from dsp import samples, lp
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import S, K

PLATE = K("impactPlate_heavy")                   # heavy plate thumps: the weight of a clunk
PLATE_M = K("impactPlate_medium")
SLAM = S("metal_hit_01", "door_03")             # a real iron door slam and a heavy thunk: its metal
MINING = K("impactMining")                       # a pick on rock: a deep thud with a ring in it
RING = K("impactMetal_heavy")                    # struck heavy iron, its ring pitched far down: the casting
HUM = "sfx_100_v2:loop_machine_01"               # a running motor's hum
ENGINE = "sfx_100_v2:loop_machine_04"            # an engine's knocking idle
STEAM = "sfx_100_v2:loop_water_03"               # a real steam hiss with its own puffs
EXHAUST = "sfx_100_v2:misc_22"                   # a steam vehicle's hiss
POT = "kenney_rpg-audio:metalPot1"               # an iron pot scraped and struck


# ---- Parts ------------------------------------------------------------------------------------------------------------------

def under(f, order=2):
    """A low-pass as an amplitude response for W.cfilter (circular, so a loop stays a loop)."""
    return lambda x: 1 / np.sqrt(1 + (x / f) ** (2 * order))


def casing(f=160, gain=0.6, top=750):
    """The iron casing the gear runs in: a broad lift at its hollow's pitch, and nothing bright gets out."""
    return lambda x: under(top, 2)(x) * (1 + gain * np.exp(-0.5 * (np.log2(x / f) / 0.4) ** 2))


def cycle(key, n, semis, start=0.0, length=None, xf=0.5, top=None):
    """A recording made one exact n-sample cycle: varispeeded (lower is bigger and slower), tiled end to end with
    equal-power crossfades (each tile entering at a different point, so the joins don't fall in step), and the stream's end
    crossfaded round into its start."""
    x = ck.get(key)
    if start or length:
        x = dsp.trim(x, start, length)
    x = dsp.vari(x, semis)
    if top:
        x = lp(x, top, 2)
    k = samples(xf)
    th = np.linspace(0, np.pi / 2, k)
    up, down = np.sin(th).astype(np.float32), np.cos(th).astype(np.float32)
    out = np.zeros(n + len(x) + 2 * k, np.float32)
    pos, i = 0, 0
    while pos < n + k:
        seg = x[int(len(x) * (0.23 * i % 0.4)):].copy()
        seg[:k] *= up
        seg[-k:] *= down
        out[pos:pos + len(seg)] += seg
        pos += len(seg) - k
        i += 1
    y = out[:n].copy()
    y[:k] = y[:k] * up + out[n:n + k] * down
    return y


def pulses(n, beats, rise, decay):
    """A periodic swell: each beat a rise and an exponential fall, wrapped round the loop. [(t, strength)] -> curve."""
    L = decay * 7
    t = np.arange(samples(L)) / dsp.SR
    shape = ((1 - np.exp(-t / rise)) * np.exp(-t / decay)).astype(np.float32)
    shape /= shape.max()
    return W.place(n, [(tb, shape, g) for tb, g in beats])


def hit(key, semis, top, at, tau):
    """A real hit, started at its attack, pitched down, darkened and choked (the oil and the load damp it)."""
    x = dsp.vari(ck.align(ck.get(key)), semis)
    return ck.choke(lp(x, top, 2), at, tau)


def clunk(rng, i):
    """One of the worm wheel's teeth coming home under load: a heavy plate's thump for the weight, an iron slam an octave
    down for the metal, a trace of struck iron's ring nearly two octaves down for the casting."""
    body = hit(PLATE[i % 5], rng.uniform(-7, -5), 700, 0.05, 0.1)
    metal = hit(SLAM[i % 2], rng.uniform(-13, -11), 600, 0.04, 0.07)
    ring = hit(RING[(i + 2) % 5], rng.uniform(-22, -19), 500, 0.02, 0.15)
    y = dsp.mix(ck.norm(body), ck.norm(metal) * 0.55, ck.norm(ring) * 0.2)
    return ck.norm(dsp.fade(y, 0.0005, 0.05))


def thud(rng, i):
    """A deeper, duller tooth: a pick's thud on rock, a heavy door's thunk an octave down and a medium plate's boom, all
    darkened. More felt than heard."""
    rock = hit(MINING[i % 5], rng.uniform(-5, -3), 500, 0.06, 0.12)
    door = hit(SLAM[1], rng.uniform(-12, -10), 450, 0.05, 0.09)
    boom = hit(PLATE_M[(i + 1) % 5], rng.uniform(-9, -7), 400, 0.04, 0.1)
    y = dsp.mix(ck.norm(rock) * 0.8, ck.norm(door) * 0.7, ck.norm(boom) * 0.6)
    return ck.norm(dsp.fade(y, 0.0005, 0.05))


def held(y, lufs=-20.0, ceiling=-4.5):
    """Finish a loop: rumble under 25 Hz out (zero-phase and circular, where crew_kit's recipe would run a causal
    high-pass over the finished loop and shift the clunks' low bodies into higher peaks), levelled, and its loudest
    clunks held under `ceiling` by a smooth gain that wraps round the cycle, so build.py's limiter finds nothing to bend
    (a bent clunk would add the brightness this cue mustn't have)."""
    y = W.cfilter(y, lambda f: 1 / np.sqrt(1 + (25 / f) ** 4))
    w = samples(0.004)
    win = np.hanning(2 * w + 1)
    win /= win.sum()
    for _ in range(3):
        y = dsp.gain(y, lufs - dsp.loudness(y))
        need = np.minimum(1.0, dsp.db2a(ceiling) / (np.abs(y) + 1e-9))
        g = minimum_filter1d(need, 4 * w + 1, mode="wrap")
        g = np.convolve(np.concatenate([g[-w:], g, g[:w]]), win, "valid")
        y = (y * g).astype(np.float32)
    return dsp.gain(y, lufs - dsp.loudness(y))


# ---- slow: the worm wheel's clunk on each stroke --------------------------------------------------------------------------

SLOW = 10.0          # one loop (s)
TURNS = 1.2          # the motor's turns a second at a medium traverse: a double-acting cylinder, two strokes a turn


@recipe("crew-gun-lay", "lay", "slow",
        "The worm wheel's heavy clunk on each stroke of the motor, slow, over its low hum and a soft breath of steam",
        """A single-cylinder steam motor turning 1.2 times a second, double-acting: two strokes a turn, the back stroke
        weaker and a hair late (the piston rod takes up part of that side). Each stroke takes up the slack in the worm
        and a tooth of the gun's ring comes home: a heavy clunk built from real heavy plate thumps, an iron door slam an
        octave down and a trace of struck iron's ring nearly two octaves down, darkened and choked short. Under it the packs'
        motor-hum loop pitched down and swelling a little with each stroke, and their real steam loop pitched down and
        low-passed to a soft breath on each stroke. In its iron casing; nothing bright. One 10 s cycle.""",
        sources=PLATE + SLAM + RING + [HUM, STEAM], loop=True, takes=1, seconds=SLOW)
def lay_slow(rng, k):
    n = samples(SLOW)
    lag = rng.uniform(0.01, 0.025)
    beats = []
    for r in range(int(round(SLOW * TURNS))):
        beats.append((r / TURNS + rng.normal(0, 0.004), rng.uniform(0.9, 1.0)))
        beats.append(((r + 0.5) / TURNS + lag + rng.normal(0, 0.004), rng.uniform(0.55, 0.7)))
    bank = [clunk(rng, i) for i in range(8)]
    teeth = W.place(n, [(t + 0.03, bank[int(rng.integers(len(bank)))], g) for t, g in beats])
    labour = pulses(n, beats, 0.06, 0.2)
    hum = W.cfilter(cycle(HUM, n, -9), under(260, 2)) * (0.8 + 0.25 * labour / labour.max())
    breath = pulses(n, beats, 0.07, 0.22)
    steam = W.cfilter(cycle(STEAM, n, -7), under(420, 2)) * (0.3 + 0.7 * breath / breath.max())
    y = W.norm(teeth) * 1.0 + W.norm(hum) * 0.3 + W.norm(steam) * 0.45
    y = W.cfilter(W.croom(y, "box", 0.12, rng), casing())
    return W.seamless(held(y))


# ---- deep: the worm grinding round under load ------------------------------------------------------------------------------

DEEP = 12.0          # one loop (s)
BEAT = 2.0           # teeth a second: the motor's twin strokes, one turn of it every two


@recipe("crew-gun-lay", "lay", "deep",
        "The worm grinding round under load and each tooth giving with a deep thud, over a labouring motor and slow exhaust",
        """Led by the load rather than the knock. The worm's thread drags on the wheel and the iron groans: the packs'
        scraped iron pot pitched down an octave and a half into a low grind that builds into each tooth and lets go as
        it comes home, two a second, with a deep, dull thud (a pick's thud on rock, a heavy door's thunk and a plate's
        boom, pitched down and darkened). Under it a bigger, slower motor: the packs' knocking engine loop pitched
        down an octave into a low labouring throb, and its exhaust once a turn, a steam vehicle's real hiss pitched down
        into a long low sigh. In its iron casing; nothing bright. One 12 s cycle.""",
        sources=MINING + [SLAM[1]] + PLATE_M + [POT, ENGINE, EXHAUST], loop=True, takes=1, seconds=DEEP)
def lay_deep(rng, k):
    n = samples(DEEP)
    swing = rng.uniform(0.02, 0.04)
    beats = [((i + (swing if i % 2 else 0)) / BEAT + rng.normal(0, 0.006), rng.uniform(0.85, 1.0) if i % 2 == 0
              else rng.uniform(0.6, 0.75)) for i in range(int(DEEP * BEAT))]
    bank = [thud(rng, i) for i in range(8)]
    teeth = W.place(n, [(t, bank[int(rng.integers(len(bank)))], g) for t, g in beats])
    # the grind: the load builds over each beat and lets go at the tooth
    load = np.concatenate([np.linspace(0, 1, samples(0.3)) ** 2, np.linspace(1, 0, samples(0.04))]).astype(np.float32)
    strain = W.place(n, [(t - 0.3, load, g) for t, g in beats])
    grind = W.cfilter(cycle(POT, n, -17, start=0.08, length=0.4, xf=0.25), under(380, 2))
    grind = grind * (0.12 + strain)
    motor = W.cfilter(cycle(ENGINE, n, -12), under(320, 2))
    turn = pulses(n, [(t, g) for t, g in beats[::2]], 0.15, 0.3)
    motor = motor * (0.75 + 0.3 * turn / turn.max())
    sigh = W.cfilter(cycle(EXHAUST, n, -10, length=2.8), under(380, 2)) * (0.25 + 0.75 * turn / turn.max())
    y = W.norm(teeth) * 1.0 + W.norm(grind) * 0.3 + W.norm(motor) * 0.27 + W.norm(sigh) * 0.25
    y = W.cfilter(W.croom(y, "box", 0.14, rng), casing(130, 0.7, 650))
    return W.seamless(held(y))
