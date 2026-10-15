"""The handcar heard (queue #514, note 729; cues.py's crew-handcar): the crew's two-person pump trolley, hung on hooks off
the back of the guard van, lifted down onto the rails and pumped along the open line at night, a lantern on its front.

What it is decides the build. A plank deck on wooden sills 0.55 m up, 3 m by 1.7 m, half a tonne, on four small
cast-iron wheels (0.53 m, so a tread goes round in 1.67 m) on two axles 1.5 m apart. A see-saw handle, a long wooden bar
with iron grip bars at its ends, rocks on a pivot pin in an iron post; its crank turns once in 5 m and drives the axle
through a 3:1 gear (45 teeth on the crank's wheel, 15 on the axle's pinion). So at the roll loop's 6 m/s the wheels turn
3.6 times a second, the crank 1.2 and the teeth pass 54 times a second, every one a whole number of times in the 10 s
cycle.
- creak: at the top and the bottom of each stroke the pull turns into a push. The pivot pin knocks across its worn bush
  and the gear's lash takes up with a tooth's tick (the iron knock is the event), and the bar creaks in its iron strap as
  the load comes back on. Heard twice a stroke for as long as anyone pumps, so it is mostly the knock, the creak short
  and low, and every take is built differently.
- joint: a small wheel over a rail joint, fired per axle. The same rail as the train's, but a quarter of the weight on a
  wheel half the size: a light, short click, the tread's crack and the rail's brief ring from 800 Hz up, the little
  wheel's own ring and the deck's boards ticking, a fifth of a second.
- roll: rolling at a moderate 6 m/s: the small iron wheels humming on the rail (contact noise ringing their modes and the
  rail's), a flat spot on each wheel ticking once a turn, the loose deck planks rattling on their sills when the bumps
  jolt them, the gears chattering faintly in mesh (most at the dead centres, twice a crank turn, where the load lets go),
  the lantern's bail clinking on its hook.
- brake: the brake shoe, an iron block on a lever, pressed on the treads at 5 m/s: a thin squeal where a wheel's mode
  locks in, coming and going, over the iron grinding on the iron, pulsing once a wheel turn.
- set: two of the crew set it down on the rails: one end's wheels then the other's land on the rail heads, a clank of
  iron on iron and the frame's thud, the planks knocking as the deck settles, the handle clunking in its lash.
- hang: two of the crew hang it back on the van: its iron eye dropped onto the hooks with a clank, the safety chains
  jangling, its frame swinging in and knocking the van's wooden end, then once more, smaller, as it settles.

Each cue has two candidates. The first is kitbashed from the packs' real recordings: real iron struck and choked short
(Kenney's metal and plate impacts, a door's metal hit), real wood creaking (Kenney's door creaks), real wood knocked,
real keys jangled and pitched down for chain, the packs' real train rumble pitched up for small wheels, a real
mechanism's ticking for the gears, real metal scrapes laid grain by grain and a real hinge's squeal held out. The second
is modelled: struck modes for the wheel, rail, post, planks and links, stick-slip for the creak, rolling and rubbing
contact noise through the wheel's modes, and a squeal locked onto a wheel mode. Every knock, thud and click keeps its
weight in the mids (the director's squish rule: under 45% of an impact's energy below 150 Hz; these keep under 10%).
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_car import sung
from recipes.crew_kit import recipe, K, S, R
from recipes.g1_train import cband
from recipes.house_beasts import cloud, knocks
from recipes.jobs import strike, CASTING
from recipes.k1_asks import lay
from recipes.stress import freeze, strongest
from recipes.world_bed import WHEEL as CAR_WHEEL, outdoors

L = "crew-handcar"
SR = dsp.SR

# ---- The car ------------------------------------------------------------------------------------------------------------

TREAD = 5 / 3              # a 0.53 m wheel's tread (m): three turns to the crank's one
CRANK = 5.0                # the crank turns once in this many metres
TEETH = 45                 # the crank's gear wheel (to the axle's 15-tooth pinion)
WHEELBASE = 1.5            # m between the axles
WHEEL = [f * 0.9 / 0.53 for f in CAR_WHEEL[:5]]   # a cast-iron wheel's modes, a car wheel's scaled to 0.53 m: 645 Hz up
PLANK = [1.0, 2.3, 4.1, 6.5, 10.0]                 # a deck board on its sill, knocked near an end: its local modes (ratios)
VAN_END = [150, 225, 330, 470]                     # the van's wooden end over its frame: broad, hollow low modes
ROLL_V = 6.0               # m/s: the roll loop's moderate speed
ROLL_L = 10.0              # 60 m: 36 wheel turns, 12 crank turns, 540 teeth through the mesh
BRAKE_V = 5.0              # m/s: braking
BRAKE_L = 8.0              # 40 m: 24 wheel turns
SQUISH = 150.0             # the director's line: an impact keeps most of its energy above it

# ---- Sources ------------------------------------------------------------------------------------------------------------

METAL_H, METAL_M, METAL_L = K("impactMetal_heavy"), K("impactMetal_medium"), K("impactMetal_light")
PLATE_M = K("impactPlate_medium")
WOOD_L = K("impactWood_light")
SNAP = "sfx_100_v2:metal_hit_02"                    # a real short, hard metal hit: steel on steel, all front
CLINK = "sfx_100_v2:metal_06"                       # a real small clink: a thin iron or tin part
DECK = S("wood_hit_02", "wood_hit_01")              # real heavy blows on planking
VAN = S("door_03", "misc_36")                       # a real door slammed; a real heavy knock on boards
KEYS = S("misc_09", "wood_03")                      # real keys jangled: small links knocking, pitched down for chain
JANGLES = [(KEYS[0], 0.03), (KEYS[1], 0.15)]        # ... each from its first jangle (s)
CREAKS = [  # a real door's wood creaking (Kenney's door openings, past the latch at their heads): (key, start s, length s)
    ("kenney_rpg-audio:doorOpen_2", 0.60, 0.11),
    ("kenney_rpg-audio:doorOpen_1", 0.57, 0.17),
    ("kenney_rpg-audio:doorOpen_2", 0.70, 0.13),
    ("kenney_rpg-audio:doorOpen_1", 0.72, 0.19),
]
RUMBLE = "sfx_100_v2:loop_ambient_04"               # a real train's rolling rumble (world_bed's rail loop)
RAILS = "sfx_100_v2:loop_ambient_02"                # a real rail vehicle's rattling run
TICKER = "sfx_100_v2:misc_28"                       # a real mechanism ticking fast and even, sixty a second
SCRAPES = [S("misc_10")[0], R("drawKnife2")[0]]     # a real metal scrape in three strokes; a blade drawn, steel on steel
SQUEAL = R("creak3")[0]                             # a real hinge's squeal: iron stick-slipping into one held tone


# ---- Shared parts -------------------------------------------------------------------------------------------------------

def stretch_of(key, start, length, semis=0.0, lo=None, hi=None, fade_in=0.02, fade_out=0.06):
    """A stretch of a recording (not a hit: a creak, a jangle), faded at its ends, varispeeded and filtered."""
    x = dsp.fade(dsp.trim(ck.get(key), start, length), fade_in, fade_out)
    if semis:
        x = dsp.vari(x, semis)
    if hi:
        x = lp(x, hi, 2)
    if lo:
        x = hp(x, lo, 2)
    return ck.norm(x)


def open_line(y, rng, length, wet=0.07):
    """Out on the open line at night, nothing near but the rails: a little of the night's reverb, cut to `length` with
    its last third faded, so a one-shot fired again and again leaves no wash. Every knock high-passed at the squish line."""
    y = W.space(hp(y, SQUISH, 4), rng, "night", wet=wet, early=())
    return tail_off(y, length)


def by_van(y, rng, length, wet=0.1):
    """At the guard van's back end: the slaps off the van and the train beside it, a little night, cut to `length`. Its
    thuds keep a little more weight than a click (high-passed at 100 Hz), still far under the squish line."""
    return tail_off(outdoors(hp(y, 100, 4), rng, wet), length)


def tail_off(y, length):
    """Cut to `length`, its last 40% faded out."""
    n = samples(length)
    return fit(y, n) * fit(env([(0, 1), (length * 0.6, 1), (length, 0)], length), n) ** 2


def struck(rng, f1, ratios, decay, contact, length=None, damp=1.0, count=None):
    """A modal part struck once (world_kit.body), normalised."""
    return W.norm(W.body(rng, f1, ratios, decay=decay, damp=damp, contact=contact, length=length, count=count))


def ringing(n, times, amps, h):
    """Impulses at `times` (s, wrapping round the cycle) with `amps`, each ringing `h`: a run of like events laid round a
    loop by one circular convolution."""
    x = np.zeros(n, np.float32)
    np.add.at(x, (np.asarray(times) * SR).astype(int) % n, np.asarray(amps, np.float32))
    return np.fft.irfft(np.fft.rfft(x) * np.fft.rfft(h, n), n).astype(np.float32)


def voiced(rng, n, times, amps, bodies):
    """The same, each event ringing one of several like-but-different `bodies` (a set of planks, of teeth), so no single
    ring stands out of the run."""
    times, amps = np.asarray(times), np.asarray(amps)
    pick = rng.integers(len(bodies), size=len(times))
    return sum(ringing(n, times[pick == i], amps[pick == i], h) for i, h in enumerate(bodies) if np.any(pick == i))


def rms(x):
    return float(np.sqrt(np.mean(np.square(x))) + 1e-12)


def squeal(n, rng, f, on, wander=0.005, rate=3.0, harm=0.2):
    """A squeal locked onto a wheel mode at `f` (world_kit.squeal's model): a near-pure tone with its harmonics, its pitch
    shivering, its level riding `on` (a 0-1 curve). Its phase is summed in double precision so it makes exactly a whole
    number of cycles round the loop (summed in float32 over a loop's samples it drifts by radians and clicks at the seam)."""
    fi = f * (1 + wander * W.slow(n, rate, rng).astype(np.float64) + 0.3 * wander * W.slow(n, 25, rng))
    fi = fi * max(1, round(np.sum(fi) / SR)) / (np.sum(fi) / SR)
    ph = 2 * np.pi * np.cumsum(fi) / SR
    lvl = np.clip(synth.curve(on, n) * (0.6 + 0.6 * W.slow(n, rate * 0.7, rng)), 0, 1.2) ** 1.5
    return ((np.sin(ph) + harm * np.sin(2 * ph + 0.7) + 0.4 * harm * np.sin(3 * ph + 1.3)) * lvl).astype(np.float32)


def fold(y, n, xfade=0.4):
    """A texture at least n + `xfade` long made into an exact n-sample cycle: the stretch past n crossfaded (equal power)
    into the head, so the last sample runs straight on into the first."""
    k = samples(xfade)
    up = np.sin(np.linspace(0, np.pi / 2, k)).astype(np.float32)
    return np.concatenate([y[:k] * up + y[n:n + k] * up[::-1], y[k:n]]).astype(np.float32)


def run_of(key, n, semis, xfade=0.4):
    """A recorded run (a rolling rumble) made into an exact n-sample cycle: varispeeded, laid end to end with equal-power
    crossfades, and folded."""
    x = W.rec(key, semis=semis)
    k = samples(xfade)
    up = np.sin(np.linspace(0, np.pi / 2, k)).astype(np.float32)
    out = x.copy()
    while len(out) < n + k:
        out = np.concatenate([out[:-k], out[-k:] * up[::-1] + x[:k] * up, x[k:]])
    return fold(out, n, xfade)


def loop_out(y, rng, wet=0.08, lo=90, hi=10000):
    """A loop's last step: the open night round the cycle (a circular reverb), held to lo-hi circularly, seamless."""
    return W.seamless(cband(W.croom(y, "night", wet, rng), lo, hi))


def turns(length, speed, rng, wheels=4, jitter=0.002):
    """Each wheel's flat spot coming round once a turn: [(times, level)] per wheel, every wheel at its own phase and wear."""
    period = TREAD / speed
    count = int(round(length / period))
    out = []
    for i in range(wheels):
        ph = rng.uniform(0, 1)
        t = (np.arange(count) + ph) * length / count + rng.normal(0, jitter, count)
        out.append((t, rng.uniform(0.35, 1.0) * (1.6 if i == 0 else 1.0)))
    return out


def chatter_lash(n, speed, phase=0.0):
    """How freely the gears rattle round a crank turn (`phase` its fraction at the cycle's start): most at the two dead
    centres, where the handle's load lets go."""
    t = np.arange(n) / SR
    return (0.15 + 0.85 * np.abs(np.cos(2 * np.pi * (speed / CRANK * t + phase))) ** 8).astype(np.float32)


# ---- creak: the handle at the top or bottom of its stroke ---------------------------------------------------------------

def creak_rec(rng, k):
    """A real door's wood creak pitched down for a thick bar, the pivot's knock (a real iron hit pitched down for a casting
    and choked: it's pinned and loaded), a gear tooth's tick (a real light iron hit choked at once) and the bar knocking
    in its strap (a real light wood knock). Even takes creak into the turn, odd ones knock first and creak as the load
    comes back on."""
    key, a, length = CREAKS[k]
    cr = stretch_of(key, a, length, -rng.uniform(2.5, 4.5), lo=220, hi=3200, fade_in=0.03, fade_out=0.08)
    knock = strike(METAL_M[(k + 1) % 5], -rng.uniform(6.5, 8.5), lo=200, at=0.02, tau=0.025)
    tooth = strike(METAL_L[(k + 3) % 5], -rng.uniform(1, 3), lo=700, at=0.004, tau=0.006)
    strap = strike(WOOD_L[k], rng.uniform(1, 3), lo=250, at=0.02, tau=0.02)
    turn = rng.uniform(0.07, 0.12) if k % 2 == 0 else 0.0
    creak_at = 0.0 if k % 2 == 0 else rng.uniform(0.03, 0.06)
    y = ck.place([(creak_at, cr, -rng.uniform(5, 8)), (turn, knock, 0), (turn + rng.uniform(0.006, 0.016), tooth, -10),
                  (turn + 0.002, strap, -rng.uniform(7, 10))], 0.7)
    return open_line(y, rng, 0.6, 0.06)


@recipe(L, "creak", "pivot",
        "The handle turning over: a real iron knock of the pivot pin, a gear tooth's tick, a short real wood creak",
        """From real recordings: at each end of the stroke the pivot pin knocks across its bush (a real iron hit pitched
        down 7-8 semitones for a cast post and choked short, as a pinned, loaded part is), a gear tooth ticks as the lash
        takes up (a real light iron hit choked at once) and the bar knocks in its strap (a real light wood knock). Round it
        the wooden bar creaks as the load comes on: a stretch of a real door's creak pitched down 3-4 semitones for a thick
        bar, kept short and low and well under the knock so it never grates when heard twice a stroke. Four takes from
        four different creaks, two creaking into the turn and two after it.""",
        sources=sorted({c[0] for c in CREAKS}) + METAL_M[1:5] + METAL_L[:2] + METAL_L[3:5] + WOOD_L[:4], takes=4,
        lufs=-21,
        preview=lambda takes, rng: ck.gait(takes, rng, 0.45, steps=16))
def creak_pivot(rng, k):
    return creak_rec(rng, k)


def creak_model(rng, k):
    """Stick-slip through the bar's modes, the pin knocking the post's casting, the tooth's tick, the strap's knock."""
    length = rng.uniform(0.18, 0.32)
    rate = env([(0, rng.uniform(40, 70)), (length * rng.uniform(0.25, 0.45), rng.uniform(170, 260)),
                (length, rng.uniform(70, 110))], length)
    scale = rng.uniform(1.1, 1.4)
    body = [f * scale * rng.uniform(0.95, 1.05) for f in synth.WOOD[1:]]
    cr = synth.creak(length, rate, rng, body=body, q=rng.uniform(10, 15), jitter=0.3, grit=0.2)
    cr = lp(cr * env([(0, 0), (length * 0.3, 1), (length * 0.6, 0.6), (length, 0)], length)[:len(cr)], 3000, 2)
    knock = mix(struck(rng, rng.uniform(470, 570), CASTING, 0.035, 0.0005, 0.25),
                W.norm(hp(W.knock(rng, 170, 0.06), 160)) * 0.4)
    tooth = struck(rng, rng.uniform(2200, 2900), CASTING, 0.007, 0.0002, 0.05)
    strap = struck(rng, rng.uniform(300, 380), PLANK, 0.025, 0.0012, 0.12)
    turn = rng.uniform(0.06, 0.12) if k % 2 == 0 else 0.0
    creak_at = 0.0 if k % 2 == 0 else rng.uniform(0.02, 0.05)
    y = ck.place([(creak_at, cr, -rng.uniform(4, 7)), (turn, knock, 0), (turn + rng.uniform(0.005, 0.015), tooth, -12),
                  (turn + 0.002, strap, -rng.uniform(6, 9))], 0.7)
    return open_line(y, rng, 0.55, 0.06)


@recipe(L, "creak", "lash",
        "The handle turning over, modelled: the pin knocking the iron post, the tooth's tick, a short stick-slip creak",
        """Modelled: the pivot pin knocking across its bush rings the post's casting (its close, inharmonic modes from
        about 500 Hz) with a little weight under it, a gear tooth ticks as the lash takes up, and the bar knocks in its
        iron strap (a board's modes from 300 Hz). The bar's creak is stick-slip through a thick wooden bar's modes, its
        slips speeding up as the load comes on and slowing as the stroke turns, held low and short under the knock. Four
        takes, each a different creak, pin and timing, two creaking into the turn and two after it.""",
        takes=4, lufs=-21, preview=lambda takes, rng: ck.gait(takes, rng, 0.45, steps=16))
def creak_lash(rng, k):
    return creak_model(rng, k)


# ---- joint: one small wheel over a rail joint ---------------------------------------------------------------------------

def ride(takes, rng, speed=7.0, rails=5):
    """The takes as the game fires them: per axle, the two axles a wheelbase apart, a rail length (12 m) between joints."""
    gap, rail = WHEELBASE / speed, 12.0 / speed
    b = dsp.Bus(0.3 + rails * rail + 0.5)
    i = 0
    for r in range(rails):
        for a in range(2):
            b.at(0.2 + r * rail + a * gap + rng.uniform(-0.004, 0.004), takes[i % len(takes)], -1.0 * a)
            i += 1
    return b.x


@recipe(L, "joint", "tick",
        "A small wheel over a rail joint: a light iron tick, a short ring, the deck's boards ticking with it",
        """From real recordings: the little wheel's tread cracking onto the next rail end (a real medium iron hit pitched
        down a semitone or two and choked at 12 ms, with a real hard metal hit's front for the contact), the rail's brief
        ring (a real light iron hit pitched down, choked at 20 ms) and the deck's boards ticking with the jolt (a real light
        wood knock pitched up). Most takes have the faint tick of the wheel leaving the near rail end a few ms first, and
        two bounce once, lightly, on the far end. A fifth of a second: lighter, shorter and higher than the train's
        clack.""",
        sources=METAL_M[:4] + METAL_L[:1] + METAL_L[2:5] + [SNAP] + WOOD_L[1:5], takes=4, lufs=-21,
        preview=lambda takes, rng: ride(takes, rng))
def joint_tick(rng, k):
    tread = strike(METAL_M[k], -rng.uniform(1, 2.5), lo=350, at=0.012, tau=0.012)
    front = strike(SNAP, rng.uniform(-3, 0), lo=900, at=0.003, tau=0.004)
    rail = strike(METAL_L[(k + 2) % 5], -rng.uniform(4, 6), lo=500, at=0.02, tau=0.025)
    board = strike(WOOD_L[(k + 1) % 5], rng.uniform(2, 4), lo=260, at=0.012, tau=0.012)
    lead = rng.uniform(0.006, 0.012) if k != 2 else 0.0
    parts = [(lead, front, -4), (lead, tread, 0), (lead + 0.001, rail, -7), (lead + rng.uniform(0.003, 0.008), board, -9)]
    if lead:
        parts.append((0, strike(METAL_L[(k + 4) % 5], rng.uniform(1, 3), lo=1200, at=0.003, tau=0.004), -15))
    if k % 2:   # the light wheel bouncing once on the far rail end
        parts.append((lead + rng.uniform(0.014, 0.024), strike(METAL_M[(k + 2) % 5], -rng.uniform(0, 1.5), lo=500, at=0.006,
                                                                  tau=0.008), -rng.uniform(8, 11)))
    return open_line(lp(ck.place(parts, 0.3), 9000, 2), rng, 0.2, 0.05)


def contact(rng, lo=1200, hi=6500):
    """Iron meeting steel: a tenth of a millisecond of every band at once, gone in a few ms."""
    k = samples(rng.uniform(0.0001, 0.0002))
    x = np.zeros(samples(0.02), np.float32)
    x[:k] = rng.standard_normal(k) * np.hanning(k)
    return W.norm(bp(x, lo, hi, 2) * env([(0, 1), (0.004, 0.15), (0.02, 0)], 0.02, "exp"))


@recipe(L, "joint", "click",
        "A small wheel over a rail joint, modelled: the tread's crack, the rail's quick ring, the little wheel's ring",
        """Modelled from the train's own joint (joints.py) and scaled down: the contact crack of the tread on the far rail
        end (a tenth of a millisecond, brighter than a car wheel's), the rail's quick ring (a steel bar's modes from 800
        Hz, damped fast), the 0.53 m wheel's own ring (a car wheel's modes scaled up to 645 Hz and over), and the deck
        boards ticking on their sills. Most takes have the faint tick of the wheel leaving the near rail end first, and
        two bounce once, lightly, on the far end. A fifth of a second, centred well above the train's clack.""",
        takes=4, lufs=-21,
        preview=lambda takes, rng: ride(takes, rng))
def joint_click(rng, k):
    rail = struck(rng, rng.uniform(820, 980), W.BAR, rng.uniform(0.012, 0.02), 0.00015, 0.2)
    wheel = struck(rng, WHEEL[0] * rng.uniform(0.96, 1.04), [f / WHEEL[0] for f in WHEEL], 0.014, 0.0002, 0.2, damp=0.3)
    board = struck(rng, rng.uniform(260, 330), PLANK, 0.018, 0.001, 0.12)
    lead = rng.uniform(0.006, 0.012) if k != 1 else 0.0
    parts = [(lead, contact(rng), -3), (lead, rail, -4), (lead, wheel, -6), (lead + rng.uniform(0.002, 0.007), board, -13)]
    if lead:
        parts.append((0, struck(rng, rng.uniform(1100, 1400), W.BAR, 0.008, 0.0002, 0.05), -16))
    if k % 2:   # the light wheel bouncing once on the far rail end
        bounce = mix(contact(rng), struck(rng, rng.uniform(820, 980), W.BAR, 0.01, 0.0002, 0.1) * 0.7)
        parts.append((lead + rng.uniform(0.014, 0.024), bounce, -rng.uniform(8, 11)))
    return open_line(lp(ck.place(parts, 0.3), 9000, 2), rng, 0.2, 0.05)


# ---- roll: rolling along at 6 m/s ----------------------------------------------------------------------------------------

def roll_parts(rng, n, speed, crank):
    """What every roll loop shares: the event times of the four wheels' flat spots, the deck planks' rattles (a few at
    random, more in short bursts after each big jolt), the gear mesh's teeth (the crank at `crank` of a turn at the
    start) and the lantern's clinks."""
    length = n / SR
    flats = turns(length, speed, rng)
    jolts = np.concatenate([t for t, _ in flats])
    big = np.concatenate([np.full(len(t), a) for t, a in flats])
    pt = list(W.poisson(length, 5.0, rng))
    pa = list(rng.uniform(0.1, 0.6, len(pt)) ** 1.5)
    for t, a in zip(jolts, big):
        if rng.random() < 0.45 * a:
            for j in range(int(rng.integers(1, 4))):
                pt.append(t + 0.008 + j * rng.uniform(0.014, 0.03))
                pa.append(a * rng.uniform(0.3, 1.0) * 0.6 ** j)
    mesh = speed / CRANK * TEETH
    teeth = (np.arange(int(round(length * mesh))) + (crank * TEETH) % 1) / mesh
    lash = chatter_lash(n, speed, crank)[(teeth * SR).astype(int) % n]
    ta = rng.random(len(teeth)) ** 3 * lash
    lt = list(W.poisson(length, 1.2, rng)) + [t + 0.02 for t, a in zip(jolts, big) if a > 1.2 and rng.random() < 0.4]
    la = rng.uniform(0.2, 1.0, len(lt)) ** 1.5
    return flats, (np.array(pt), np.array(pa)), (teeth, ta), (np.array(lt), la)


def roll_rec(rng, k):
    """Real recordings, bent round one cycle: the rumble pitched up for small, fast wheels and its car's weight taken off,
    a rail vehicle's rattling run under it, real iron ticks for the flats, real wood knocks for the planks, a real
    mechanism's ticking for the gears and a real clink for the lantern."""
    n = samples(ROLL_L)
    crank = rng.uniform()
    flats, planks, teeth, lamp = roll_parts(rng, n, ROLL_V, crank)
    hum = cband(run_of(RUMBLE, n, 6.0), 200, 9000)
    sing = W.cyclic(lambda z: dsp.resonate(z, WHEEL, q=45, gains=[1, 0.7, 0.45, 0.3, 0.2]), hum)
    run = cband(cloud(rng, RAILS, n, 4.0, grain_s=0.5), 250, 8000)
    ticks = [strike(METAL_L[i], -rng.uniform(2, 4), lo=500, at=0.004, tau=0.006) for i in range(4)]
    flat = sum(ringing(n, t, np.full(len(t), a) * rng.uniform(0.8, 1.2, len(t)), ticks[i]) for i, (t, a) in enumerate(flats))
    boards = [strike(WOOD_L[i], rng.uniform(1.5, 4), lo=260, at=0.012, tau=0.01) for i in range(5)]
    plank = voiced(rng, n, *planks, boards)
    gear = cband(cloud(rng, TICKER, n, -1.5, grain_s=0.3), 600, 6000) * chatter_lash(n, ROLL_V, crank)
    lantern = voiced(rng, n, *lamp, [strike(CLINK, s, lo=900, at=0.012, tau=0.02) for s in (-2, 0, 2)])
    y = (hum / rms(hum) + sing / rms(sing) * 0.35 + run / rms(run) * 0.3 + flat / rms(flat) * 0.18
         + plank / rms(plank) * 0.28 + gear / rms(gear) * 0.1 + lantern / rms(lantern) * 0.05)
    return loop_out(y, rng)


@recipe(L, "roll", "rumble",
        "Rolling at 6 m/s: real train rumble pitched up for small wheels, flats ticking, planks rattling, gears chattering",
        """From real recordings, laid round one 10 s cycle (60 m at 6 m/s: 36 wheel turns, 12 crank turns, all whole):
        the packs' real rolling train rumble pitched up six semitones for small wheels turning fast, its car's weight taken
        off and a little of it rung in a 0.53 m wheel's modes (the iron humming), a real rail vehicle's rattling run under
        it; each wheel's flat spot ticking once a turn at its own phase (real light iron hits, choked); the deck's loose
        planks rattling on their sills after the jolts (real light wood knocks, pitched up for narrow boards); the gears
        chattering faintly in mesh, most at the two dead centres of each crank turn (a real mechanism's even ticking,
        pitched to the 54 teeth a second); the lantern's bail clinking on its hook now and then.""",
        sources=[RUMBLE, RAILS, TICKER, CLINK] + METAL_L[:4] + WOOD_L[:5], takes=1, loop=True, lufs=-23, seconds=ROLL_L)
def roll_rumble(rng, k):
    return roll_rec(rng, k)


def resonance(f, q):
    """A resonance's amplitude response at f (Hz), for W.cfilter."""
    return lambda F: 1 / np.sqrt(1 + (q * (F / f - f / F)) ** 2)


def roll_model(rng, k):
    """Rolling contact noise (the rail's and the tread's roughness, the contact patch rolling off its top) rung through
    the wheel's modes and the rail's, the flats as small iron knocks, planks, teeth and the lantern as struck modes."""
    n = samples(ROLL_L)
    t = np.arange(n) / SR
    flats, planks, teeth, lamp = roll_parts(rng, n, ROLL_V, rng.uniform())
    force = W.pnoise(n, rng, lambda f: (f / 90) / (1 + f / 90) / (1 + (f / 1600) ** 2))
    wheel = W.cyclic(lambda z: dsp.resonate(z, [f * rng.uniform(0.98, 1.02) for f in WHEEL], q=55,
                                            gains=[1, 0.75, 0.5, 0.3, 0.2]), force)
    rail = W.cfilter(force, lambda F: resonance(950, 2.2)(F) + 0.5 * resonance(430, 1.4)(F) + 0.15)
    frame = W.cfilter(W.pnoise(n, rng), resonance(210, 2.5))
    out_of_round = (1 + 0.1 * np.sin(2 * np.pi * round(ROLL_L * ROLL_V / TREAD) * t / ROLL_L)).astype(np.float32)
    # the rail's long-wave roughness (a few cm to a foot) passing under the tread at 12-70 Hz: the rolling 'rrr'
    grain = W.cfilter(W.pnoise(n, rng), lambda f: np.exp(-0.5 * (np.log2(f / 30) / 0.9) ** 2))
    rough = np.clip(1 + 0.45 * grain / (np.std(grain) + 1e-9), 0.15, None).astype(np.float32)
    hum = (wheel / rms(wheel) * 1.1 + rail / rms(rail) + frame / rms(frame) * 0.25) * out_of_round * rough
    knocks_ = [mix(struck(rng, WHEEL[0] * rng.uniform(0.95, 1.05), [f / WHEEL[0] for f in WHEEL], 0.012, 0.0004, 0.08,
                          damp=0.3), struck(rng, rng.uniform(800, 950), W.BAR, 0.01, 0.0004, 0.08) * 0.5) for _ in range(4)]
    flat = sum(ringing(n, tt, np.full(len(tt), a) * rng.uniform(0.8, 1.2, len(tt)), knocks_[i])
               for i, (tt, a) in enumerate(flats))
    boards = [struck(rng, rng.uniform(240, 360), PLANK, rng.uniform(0.015, 0.03), 0.0012, 0.12) for _ in range(6)]
    plank = voiced(rng, n, *planks, boards)
    gear = voiced(rng, n, *teeth, [struck(rng, rng.uniform(1900, 2800), CASTING, 0.005, 0.0002, 0.03) for _ in range(3)])
    lantern = voiced(rng, n, *lamp, [struck(rng, rng.uniform(1300, 2200), W.PLATE, 0.05, 0.0003, 0.2, count=8)
                                     for _ in range(3)])
    y = (hum / rms(hum) + flat / rms(flat) * 0.2 + plank / rms(plank) * 0.3 + gear / rms(gear) * 0.08
         + lantern / rms(lantern) * 0.05)
    return loop_out(y, rng)


@recipe(L, "roll", "hum",
        "Rolling at 6 m/s, modelled: small iron wheels humming on the rail, flats ticking, planks rattling, a little gear",
        """Modelled, one 10 s cycle (60 m at 6 m/s: 36 wheel turns, 12 crank turns, 540 teeth through the mesh): rolling
        contact noise from the roughness of tread and rail, rung through a 0.53 m cast-iron wheel's modes (from 645 Hz, so
        the hum sings a little) and the rail's (a broad ring round 950 Hz), roughened by the rail's long-wave unevenness
        passing under the tread (the rolling 'rrr', 12-70 times a second) and swelling slightly once a turn; each wheel's
        flat spot knocking once a turn at its own phase; the deck's loose planks rattling on their sills after the jolts
        (board modes from 240 Hz); the gear teeth chattering faintly in mesh, most at the crank's two dead centres; the
        lantern's bail clinking now and then. Kept above 90 Hz: a light car, no boom.""",
        takes=1, loop=True, lufs=-23, seconds=ROLL_L)
def roll_hum(rng, k):
    return roll_model(rng, k)


# ---- brake: the shoe pressed on the wheels ------------------------------------------------------------------------------

def brake_model(rng, k):
    """Rubbing through the small wheel's modes (world_kit.friction), pulsing once a turn where the tread's out of round,
    with a squeal locked on the wheel's third mode coming and going (and sometimes jumping to its second), the shoe
    chattering on its lever and the deck humming with it."""
    n = samples(BRAKE_L)
    t = np.arange(n) / SR
    cyc = round(BRAKE_L * BRAKE_V / TREAD)
    turn = (1 + 0.22 * np.sin(2 * np.pi * cyc * t / BRAKE_L)).astype(np.float32)
    g = W.friction(n, rng, WHEEL, rough=1.3, grit=0.5, q=18, loop=True)
    g = W.cfilter(g, lambda f: (f / 300) ** 2 / (1 + (f / 300) ** 2) / (1 + (f / 7000) ** 2))
    on1 = np.clip(1.4 * W.slow(n, 0.45, rng) + 0.5, 0, 1)
    on2 = np.clip(1.4 * W.slow(n, 0.35, rng) - 0.4, 0, 1) * (1 - 0.7 * on1)
    sq = (squeal(n, rng, WHEEL[2] * rng.uniform(0.97, 1.0), on1 * turn, wander=0.006, harm=0.15)
          + squeal(n, rng, WHEEL[1] * rng.uniform(1.0, 1.03), on2, wander=0.005, harm=0.2) * 0.7)
    st = np.array(W.poisson(BRAKE_L, 7, rng))
    shoe = voiced(rng, n, st, rng.uniform(0.2, 1, len(st)) ** 2,
                  [struck(rng, rng.uniform(700, 950), CASTING, 0.02, 0.0004, 0.1) for _ in range(3)])
    deck = W.cfilter(W.pnoise(n, rng), resonance(260, 2.0)) * turn
    y = g / rms(g) * turn + sq / rms(sq) * 0.45 + shoe / rms(shoe) * 0.06 + deck / rms(deck) * 0.15
    return loop_out(y, rng, 0.07, 150, 11000)


@recipe(L, "brake", "shoe",
        "The brake shoe on the wheels, modelled: a thin squeal on the wheel's ring over iron grinding, pulsing each turn",
        """Modelled, one 8 s cycle (40 m at 5 m/s: 24 wheel turns): the iron block rubbing on the tread, contact noise
        roughened by the asperities and rung through the 0.53 m wheel's modes, swelling once a turn where the tread is out
        of round; a thin squeal where the rub locks the wheel's 3.2 kHz mode into a near-pure tone, shivering in pitch and
        coming and going (now and then jumping to its 1.8 kHz mode); the shoe chattering on its lever and the deck humming
        with it. Kept above 150 Hz: thin and iron, not a train's brake.""",
        takes=1, loop=True, lufs=-23, seconds=BRAKE_L)
def brake_shoe(rng, k):
    return brake_model(rng, k)


def squeal_rec(rng, n, f):
    """A real hinge's squeal (iron stick-slipping into one tone) varispeeded so its tone sits at `f`, held out over the
    loop from its steadiest stretch (a spectral freeze, each partial a narrow band, as a squeal never quite settles), its
    pitch shivering a few cents (periodic)."""
    x = dsp.trim(ck.get(SQUEAL), 0.07, 0.23)
    x = dsp.vari(x, 12 * np.log2(f / strongest(x, 800, 2000)))
    y = freeze(x, n, rng, window=0.1)
    return sung(y, 0.12 * W.slow(n, 3.0, rng) + 0.05 * W.slow(n, 20, rng))


def brake_rec(rng, k):
    """Real scrapes laid grain by grain (k1_asks.lay: short grains, densely overlapping, so the grind runs on without a
    beat), pitched down for iron on iron and rung a little through the wheel's modes, pulsing once a turn; a real squeal
    held out over it, coming and going."""
    n = samples(BRAKE_L)
    t = np.arange(n) / SR
    cyc = round(BRAKE_L * BRAKE_V / TREAD)
    turn = (1 + 0.22 * np.sin(2 * np.pi * cyc * t / BRAKE_L)).astype(np.float32)
    grind = fold(lay(rng, BRAKE_L + 0.5, 160, -7.0, SCRAPES, [300, 300]), n)
    grind = cband(dsp.saturate(grind / (2.5 * rms(grind)), 3.0), 300, 7000)    # the scrape's own clicks rounded off
    rung = W.cyclic(lambda z: dsp.resonate(z, WHEEL, q=20, gains=[0.6, 0.8, 0.6, 0.4, 0.3]), grind)
    on = np.clip(1.4 * W.slow(n, 0.45, rng) + 0.45, 0, 1)
    sq = squeal_rec(rng, n, WHEEL[1] * rng.uniform(1.0, 1.03)) * on * turn
    y = (grind / rms(grind) + rung / rms(rung) * 0.6) * turn + sq / rms(sq) * 0.45
    return loop_out(y, rng, 0.07, 150, 11000)


@recipe(L, "brake", "scrape",
        "The brake shoe on the wheels: real metal scrapes laid into a grind, a real hinge's squeal held out over it",
        """From real recordings, one 8 s cycle (24 wheel turns at 5 m/s): the iron block grinding on the treads, laid
        grain by grain from a real metal scrape and a blade drawn steel on steel, pitched down for heavier iron and rung a
        little in the 0.53 m wheel's modes, the scrape's own clicks rounded off so it runs on as a grind, swelling once a
        turn; over it a real hinge's squeal (the same stick-slip that makes a brake sing) varispeeded up to the wheel's
        1.8 kHz mode and held out, shivering in pitch and coming and going. Its squeal sits lower than the modelled one's
        3.2 kHz, out of the tells' 2-6 kHz. Kept above 150 Hz.""",
        sources=SCRAPES + [SQUEAL], takes=1, loop=True, lufs=-23, seconds=BRAKE_L)
def brake_scrape(rng, k):
    return brake_rec(rng, k)


# ---- set: set down on the rails --------------------------------------------------------------------------------------------

def axle_rec(rng, i):
    """One axle's two wheels landing on the rail heads a few ms apart: real heavy iron and plate hits pitched down a
    little for a cast wheel on a rail and choked (the weight is on them), the frame's thud from a real blow on planking."""
    w1 = strike(METAL_H[i % 5], -rng.uniform(3, 5), lo=200, at=0.04, tau=0.05)
    w2 = strike(METAL_H[(i + 2) % 5], -rng.uniform(3, 5), lo=200, at=0.04, tau=0.05)
    plate = strike(PLATE_M[i % 5], -rng.uniform(2, 4), lo=180, at=0.05, tau=0.05)
    thud = strike(DECK[i % 2], -rng.uniform(1, 2), lo=170, at=0.06, tau=0.05)
    return ck.place([(0, w1, 0), (rng.uniform(0.003, 0.009), w2, -2), (0.001, plate, -5), (0.004, thud, -3)])


@recipe(L, "set", "iron",
        "Set down on the rails: one end's wheels then the other's clanking on, the frame's thud, the planks knocking",
        """From real recordings: the two crew lower it the last few centimetres, one end a beat before the other (a
        sixth of a second in one take, nearly together in the other). Each axle's two wheels land on the rail heads a few
        ms apart (real heavy iron hits and a plate hit, pitched down a little and choked: the weight stays on them), with
        the frame's thud (a real blow on planking, its weight kept in the low mids); then the deck's planks knock as it
        settles (real light wood knocks), the handle clunks in its lash and the lantern clinks. Beside the van.""",
        sources=METAL_H + PLATE_M[:2] + PLATE_M[3:5] + DECK + WOOD_L + METAL_M[:2] + [CLINK], takes=2, lufs=-19)
def set_iron(rng, k):
    second = (0.16, 0.05)[k] * rng.uniform(0.9, 1.1)
    parts = [(0, axle_rec(rng, k), 0), (second, axle_rec(rng, k + 3), -1.5 - k)]
    for j in range(int(rng.integers(3, 5))):
        parts.append((second + rng.uniform(0.02, 0.2), strike(WOOD_L[j % 5], rng.uniform(0, 3), lo=230, at=0.02, tau=0.02),
                      -rng.uniform(9, 15)))
    parts.append((second + rng.uniform(0.06, 0.12), strike(METAL_M[k], -rng.uniform(6, 8), lo=200, at=0.02, tau=0.025), -9))
    parts.append((second + rng.uniform(0.03, 0.08), strike(CLINK, rng.uniform(-1, 1), lo=900, at=0.015, tau=0.02), -16))
    return by_van(ck.place(parts, 1.2), rng, 0.85)


def axle_model(rng):
    """One axle landing, modelled: each wheel's ring (its modes) and the rail's (a heavier bar ring than a joint's), a
    short knock of the weight (high-passed), and the frame's planks thudding (broad, hollow low modes and a board's)."""
    parts = []
    for w in range(2):
        wheel = struck(rng, WHEEL[0] * rng.uniform(0.94, 1.06), [f / WHEEL[0] for f in WHEEL], 0.05, 0.0004, 0.4, damp=0.4)
        rail = struck(rng, rng.uniform(420, 520), W.BAR, 0.04, 0.0004, 0.3)
        parts += [(w * rng.uniform(0.003, 0.008), wheel, -2 * w), (w * 0.005, rail, -4 - 2 * w)]
    parts.append((0.002, W.norm(hp(W.knock(rng, 180, 0.12), 160)), -6))
    parts.append((0.004, ck.hollow(rng, [175, 260, 390], 0.18, q=4), -4))
    parts.append((0.006, struck(rng, rng.uniform(230, 300), PLANK, 0.05, 0.002, 0.25), -5))
    return ck.place(parts)


@recipe(L, "set", "drop",
        "Set down on the rails, modelled: wheels ringing onto the rails one end then the other, a thud, planks rattling",
        """Modelled: one end's wheels land on the rail heads, then the other's (a sixth of a second later in one take,
        nearly together in the other); each wheel rings its own modes and the rail's, over a short knock of the weight
        and the deck thudding (its hollow low modes and a board's, kept in the low mids); then the loose planks rattle on
        their sills as it settles, the pivot clunks in the post and the lantern's bail clinks. Beside the van.""",
        takes=2, lufs=-19)
def set_drop(rng, k):
    second = (0.16, 0.05)[k] * rng.uniform(0.9, 1.1)
    rattle = knocks(rng, 0.25, env([(0, 30), (0.25, 4)], 0.25), [270, 620, 1100, 1750], q=6, tail=0.04)
    rattle = rattle * fit(env([(0, 1), (0.25, 0.2)], 0.25), len(rattle))
    parts = [(0, axle_model(rng), 0), (second, axle_model(rng), -1.5 - k), (second + 0.01, W.norm(rattle), -10),
             (second + rng.uniform(0.06, 0.12), struck(rng, rng.uniform(480, 560), CASTING, 0.03, 0.0005, 0.2), -10),
             (second + rng.uniform(0.03, 0.08), struck(rng, rng.uniform(1300, 2000), W.PLATE, 0.05, 0.0003, 0.2,
                                                       count=8), -18)]
    return by_van(ck.place(parts, 1.2), rng, 0.85)


# ---- hang: hung back on the van's hooks ------------------------------------------------------------------------------------

@recipe(L, "hang", "chains",
        "Hung back on the van: a real iron clank on the hooks, chains jangling, the frame knocking the van's wooden end",
        """From real recordings: the handcar's iron eye dropped onto the van's hooks (real heavy and medium iron hits,
        pitched down and choked), its safety chains jangling as they're hooked on and swing (real keys jangled, pitched
        down 6-8 semitones into heavy links), and the frame swinging in to knock the van's wooden end (a real door slam
        or a heavy knock on boards, pitched down a little, its weight kept in the low mids), then once more, smaller, as it
        settles, the chains easing after it. About a second, beside the van.""",
        sources=METAL_H[1:3] + METAL_M[2:4] + KEYS + VAN + WOOD_L[:2], takes=2, lufs=-19)
def hang_chains(rng, k):
    hook = strike(METAL_H[(k + 1) % 5], -rng.uniform(4, 6), lo=200, at=0.05, tau=0.06)
    eye = strike(METAL_M[(k + 2) % 5], -rng.uniform(2, 4), lo=300, at=0.03, tau=0.04)
    chain = stretch_of(*JANGLES[k], 0.45, -rng.uniform(6, 8), lo=250, hi=7000, fade_in=0.005, fade_out=0.15)
    chain2 = stretch_of(*JANGLES[1 - k], 0.3, -rng.uniform(5, 7), lo=250, hi=7000, fade_in=0.01, fade_out=0.12)
    knock_at = rng.uniform(0.18, 0.28)
    van = strike(VAN[k], -rng.uniform(1, 3), lo=170, at=0.08, tau=0.06)
    van2 = strike(VAN[1 - k], -rng.uniform(2, 4), lo=180, at=0.05, tau=0.05)
    board = strike(WOOD_L[k], rng.uniform(0, 2), lo=230, at=0.02, tau=0.02)
    back = knock_at + rng.uniform(0.3, 0.42)
    y = ck.place([(0, hook, 0), (0.004, eye, -5), (0.02, chain, -2), (knock_at, van, -1), (knock_at + 0.004, board, -9),
                  (knock_at + 0.05, chain2, -6), (back, van2, -9), (back + 0.01, chain2 * 0.5, -11)], 1.6)
    return by_van(y, rng, 1.15)


def links(rng, length, count, spread=0.5):
    """A short chain jangling: `count` link-on-link knocks, most as it first drops and swings, thinning out, every link a
    small iron ring (a plate's modes from 1.2-2.6 kHz, gone in a few tens of ms)."""
    b = dsp.Bus(length + 0.1)
    for _ in range(count):
        t = spread * rng.random() ** 1.8
        h = struck(rng, rng.uniform(1200, 2600), W.PLATE, rng.uniform(0.015, 0.035), 0.0002, 0.1, count=7)
        b.at(t, h, 20 * np.log10(rng.uniform(0.15, 1.0) ** 1.5 * (1 - 0.6 * t / spread)))
    return W.norm(b.x)


@recipe(L, "hang", "hooks",
        "Hung back on the van, modelled: the eye clanking on the hooks, chain links jangling, the frame knocking the van",
        """Modelled: the handcar's iron eye dropped onto the van's hooks (a forged hook's bar modes from 300 Hz and the
        eye's casting knocked), two safety chains jangling as they're hooked on and swing (each link-on-link knock a small
        iron ring, many as they drop, thinning out), the frame swinging in to knock the van's wooden end (its broad,
        hollow low modes over a board's, kept in the low mids), then once more, smaller, as it settles. About a second, beside
        the van.""", takes=2, lufs=-19)
def hang_hooks(rng, k):
    hook = struck(rng, rng.uniform(300, 420), W.BAR, 0.035, 0.0003, 0.3)   # loaded with the car's weight: it rings short
    eye = struck(rng, rng.uniform(600, 760), CASTING, 0.03, 0.0004, 0.2)
    knock_at = rng.uniform(0.18, 0.28)
    van = mix(ck.hollow(rng, VAN_END, 0.3, q=5), struck(rng, rng.uniform(210, 270), PLANK, 0.06, 0.0025, 0.3) * 0.6,
              W.norm(hp(W.knock(rng, 160, 0.12), 150)) * 0.4)
    van2 = mix(ck.hollow(rng, VAN_END, 0.2, q=5), struck(rng, rng.uniform(230, 290), PLANK, 0.04, 0.002, 0.2) * 0.6)
    back = knock_at + rng.uniform(0.3, 0.42)
    y = ck.place([(0, hook, 0), (0.003, eye, -4), (0.015, links(rng, 0.6, 18, 0.45), -1),
                  (0.06, links(rng, 0.5, 12, 0.35), -4), (knock_at, W.norm(van), -1), (back, W.norm(van2), -9),
                  (back + 0.01, links(rng, 0.3, 6, 0.2), -10)], 1.6)
    return by_van(y, rng, 1.15)
