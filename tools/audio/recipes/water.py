"""The water heard (queue #165, note 429): the line plan's rivers, lakes and shores (maritime-rules.md §2-5), which lay
in silence (B1's #160 makes them move on screen).

- A river (world-water.river): a river running under its span and beside the line up its valley. Broad water over
  stones: the wash of it, gurgles and burbles where it folds over a rock, the fine fizz of its surface. A loop.
- A lake (world-water.lake): still water lapping at its shore, a wavelet every few seconds slapping the stones and
  sucking back, a plop now and then. A loop.
- The sea (world-water.surf): the Atlantic's surf on rock and shingle. A wave every 8-10 s: the swell rising, the break,
  the wash running up, and the shingle dragged rattling back down after it; the sea's low roar under all of it. A loop.
- Fundy's flats (world-water.tide): the tide far out over the red mud, its wash a long way off, and close by the mud
  seeping, ticking and popping and little channels trickling. A loop.

Synthesised (washes, bubbles, the shingle's stones knocking: there's no sea, lake or river in the packs bar one loop of
moving water), with the packs' water (sfx_100_v2 loop_water_01, loop_water_02) under the river's second candidate.
Every loop is built exactly periodic and handed to build.py through world_kit.seamless.
"""

import numpy as np

import dsp
import synth
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes import world_kit as W
from recipes.crew_kit import recipe, S

L = "world-water"
SR = dsp.SR
MASS = S("loop_water_01")           # a mass of water moving
TRICKLE = S("loop_water_02")        # water pouring and trickling


def stream(n, events):
    """[(t, x, gain), ...] summed onto a loop of n samples, what runs past the end coming round to the start (world_kit.place
    without its per-event buffer, for thousands of bubbles)."""
    longest = max((len(x) for _, x, _ in events), default=0)
    out = np.zeros(n + longest + 1, np.float32)
    for t, x, g in events:
        a = samples(t) % n
        out[a:a + len(x)] += x * g
    return dsp.wrap(out, n)


def fizz(rng, n, rate, fmin, fmax, rise=(0.05, 0.4), gain=(0.3, 1.0)):
    """A loop's stream of bubbles (Minnaert rings), `rate` a second, sizes spread between fmin and fmax."""
    length = n / SR
    ev = []
    for t in W.poisson(length, rate, rng):
        f = np.exp(rng.uniform(np.log(fmin), np.log(fmax)))
        ev.append((t, synth.bubble(f, rise=rng.uniform(*rise)), rng.uniform(*gain) * (fmin / f) ** 0.3))
    return stream(n, ev)


def burble(rng, fmin=250, fmax=900, count=(5, 14), spread=0.35):
    """Water folding over a stone: a knot of big bubbles in a third of a second (a one-shot: nothing comes round)."""
    m = int(rng.integers(*count))
    out = np.zeros(samples(spread + 0.3), np.float32)
    for t in np.sort(rng.uniform(0, spread, m)):
        f = np.exp(rng.uniform(np.log(fmin), np.log(fmax)))
        b = synth.bubble(f, rise=rng.uniform(0.2, 0.9)) * rng.uniform(0.4, 1.0)
        a = samples(float(t))
        k = min(len(b), len(out) - a)
        out[a:a + k] += b[:k]
    return out


def wash(rng, n, lo, hi, tilt=1.0):
    """Moving water's broadband rush: periodic noise between lo and hi, falling off above (`tilt` per octave-ish)."""
    return W.pnoise(n, rng, lambda f: (f / lo) / (1 + (f / lo) ** 2) ** 0.5 / (1 + (f / hi) ** (2 * tilt)) ** 0.5)


def swell(n, period, phase, attack, hold, decay, floor=0.0):
    """A periodic swell, once every `period` s: up over `attack`, held, then dying over `decay` (exponential) to `floor`."""
    t = (np.arange(n) / SR - phase) % period
    y = np.where(t < attack, (t / attack) ** 2,
                 np.where(t < attack + hold, 1.0, np.exp(-(t - attack - hold) / max(decay, 1e-3))))
    return (floor + (1 - floor) * y).astype(np.float32)


def stones(rng, length, rate, f=(1800, 7000), q=(4, 10), size=1.0):
    """Shingle dragged back by the water: stones knocking on stones, thick at first and thinning out."""
    return synth.skitter(length, env([(0, rate), (length * 0.3, rate * 0.7), (length, rate * 0.05)], length), rng,
                         f=(f[0] / size, f[1] / size), q=q, legs=10)


# ---- The river --------------------------------------------------------------------------------------------------------

RIVER = 12.0


@recipe(L, "river", "run", "A river running broad over stones: its wash, gurgles where it folds, the fizz of its surface",
        """A river running broad and quick over a stony bed, as heard from its bank or a span over it: the wash of moving
        water (periodic noise, its weight at 250 Hz-3 kHz, surging slowly), gurgles and burbles where it folds over a
        rock (knots of big Minnaert bubbles, 250-900 Hz, about one a second), a steady burble of mid bubbles and the
        fine fizz of its broken surface (thousands of small ones, 1.5-6 kHz). A 12 s loop.""",
        sources=[], loop=True, takes=1, lufs=-26, seconds=RIVER)
def river_run(rng, k):
    n = samples(RIVER)
    surge = np.clip(0.75 + 0.2 * W.slow(n, 0.35, rng), 0.3, 1.2)
    body = W.norm(wash(rng, n, 250, 3000)) * surge
    mid = W.norm(fizz(rng, n, 110, 350, 1500, rise=(0.1, 0.6)))
    top = W.norm(fizz(rng, n, 520, 1500, 6000, rise=(0.05, 0.3), gain=(0.2, 1.0)))
    folds = W.place(n, [(t, burble(rng), rng.uniform(0.5, 1.0)) for t in W.poisson(RIVER, 1.1, rng)])
    y = mix(body * 0.42, mid * 0.38, top * 0.2, W.norm(folds) * 0.5)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 9000) ** 4)))


@recipe(L, "river", "deep", "A deep, slower river: the packs' moving water slowed and darkened, its gurgles fewer",
        """A deeper, slower river, the water heavier and the stones under it further down: the packs' loop of moving
        water (sfx_100_v2 loop_water_01) slowed three semitones and darkened, a little of its trickle under it for the
        edges, fewer and lower gurgles (knots of 150-600 Hz bubbles) and a softer fizz. A 12 s loop.""",
        sources=MASS + TRICKLE, loop=True, takes=1, lufs=-26, seconds=RIVER)
def river_deep(rng, k):
    n = samples(RIVER)
    a = lp(dsp.vari(ck.get(MASS[0]), -3), 3500)
    b = lp(dsp.vari(ck.get(TRICKLE[0]), -2), 5000)
    # The packs' loops aren't 12 s: laid end to end and folded onto the cycle, each pass crossfaded into the next.
    def tile(x):
        k = samples(0.4)
        out = np.zeros(n + len(x), np.float32)
        fade = np.ones(len(x), np.float32)
        fade[:k] = np.sin(np.linspace(0, np.pi / 2, k)) ** 2
        fade[-k:] = np.cos(np.linspace(0, np.pi / 2, k)) ** 2
        step = len(x) - k
        for i in range(0, n + 1, step):
            out[i:i + len(x)] += x * fade
        return dsp.wrap(out, n)
    mass = W.norm(tile(a))
    edge = W.norm(tile(b))
    folds = W.place(n, [(t, burble(rng, 150, 600, (4, 9), 0.5), rng.uniform(0.5, 1.0)) for t in W.poisson(RIVER, 0.6, rng)])
    top = W.norm(fizz(rng, n, 260, 1200, 4500, gain=(0.2, 0.8)))
    y = mix(mass * 0.6, edge * 0.18, W.norm(folds) * 0.4, top * 0.12)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 7000) ** 4)))


# ---- A lake -----------------------------------------------------------------------------------------------------------

LAKE = 16.0


def lap(rng, size=1.0):
    """One wavelet at a stony shore: the slap of it, a plop or two, and the water sucking back between the stones."""
    length = 1.6
    n = samples(length)
    # A slosh, not a smack: up over 80 ms, then dying away.
    rise = dsp.fit(env([(0, 0), (0.08, 1), (1.6, 1)], length), n)
    fall = dsp.fit(env([(0, 1), (0.08, 1), (0.2, 0.45), (0.7, 0)], length, "exp"), n)
    slap = hp(bp(synth.noise(length, rng, "pink"), 300 / size, 2600), 200) * rise * fall
    plops = burble(rng, 350 / size, 1300 / size, (1, 4), 0.25)
    suck = W.norm(fizz(rng, n, 160, 1800, 6500, gain=(0.15, 0.6))) * dsp.fit(env([(0, 0), (0.25, 1), (1.4, 0)], length), n)
    return mix(W.norm(slap) * 0.7, dsp.fit(W.norm(plops), n) * 0.5, suck * 0.3, length=n)


@recipe(L, "lake", "lap", "Still water lapping at a lake's stony shore: a wavelet every few seconds, slapping and sucking back",
        """A lake on a still night lapping at its shore: a wavelet every 2-5 s slapping the stones (a soft burst of
        pink noise, 300 Hz-2.6 kHz), a plop or two (big bubbles), and the water sucking back between the stones (a
        short fizz of small ones); between them only a faint wash, the open water breathing. A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-30, seconds=LAKE)
def lake_lap(rng, k):
    n = samples(LAKE)
    times, t = [], rng.uniform(0, 1)
    while t < LAKE:
        times.append(t)
        t += rng.uniform(2.0, 5.0)
    laps = W.place(n, [(t, lap(rng, rng.uniform(0.85, 1.2)), rng.uniform(0.45, 1.0)) for t in times])
    air = W.norm(wash(rng, n, 300, 2000)) * np.clip(0.6 + 0.3 * W.slow(n, 0.2, rng), 0.1, 1)
    y = mix(W.norm(laps) * 0.8, air * 0.06)
    return W.seamless(W.cfilter(y, lambda f: 1 / (1 + (f / 8000) ** 4)))


# ---- The sea ----------------------------------------------------------------------------------------------------------

SURF = 27.0


def wave(rng, n, at, period, shingle=1.0, big=1.0):
    """One wave onto the shore, at `at` s into a loop of n: the swell rising, the break, the wash running up, and (on
    shingle) the stones dragged rattling back down."""
    rise, crash = 1.6, at + 1.6
    roar = W.norm(wash(rng, n, 120, 2500, tilt=0.8))
    shape = swell(n, period, at, rise, 0.25, 1.4)
    body = roar * shape
    # The break: a heavier, darker thump of water as the crest comes down.
    thud = lp(synth.noise(0.7, rng, "brown"), 180) * env([(0, 0), (0.03, 1), (0.7, 0)], 0.7, "exp")
    # The wash running up the beach: a hiss thinning as it spends itself.
    run = bp(synth.noise(2.6, rng, "white"), 1200, 9000) * env([(0, 0), (0.3, 1), (2.6, 0)], 2.6, "exp")
    parts = [(crash - 0.05, W.norm(thud) * big, 0.55), (crash + 0.15, W.norm(run), 0.32)]
    if shingle > 0:
        back = stones(rng, 3.2, 260 * shingle, size=1.0)
        parts.append((crash + 1.5, W.norm(back) * dsp.fit(env([(0, 0), (0.25, 1), (3.2, 0)], 3.2), len(back)), 0.8 * shingle))
    return body * big, W.place(n, parts)


@recipe(L, "surf", "shingle", "The Atlantic's surf on a shingle beach: each wave breaking and the stones dragged rattling back",
        """The open Atlantic on a shingle beach at night: a wave every 8-10 s, the swell rising (broad periodic noise,
        its weight at 120 Hz-2.5 kHz), the break coming down heavy (a brown-noise thud under 180 Hz), the wash hissing
        up the beach and then the shingle dragged rattling back down after it (thousands of stones knocking, 1.8-7 kHz,
        thick at first and thinning); under it all the sea's low roar further out. A 27 s loop of three waves.""",
        sources=[], loop=True, takes=1, lufs=-24, seconds=SURF)
def surf_shingle(rng, k):
    n = samples(SURF)
    period = SURF / 3
    bodies, hits = [], []
    for i in range(3):
        b, h = wave(rng, n, i * period + rng.uniform(-0.4, 0.4), SURF, shingle=1.0, big=rng.uniform(0.75, 1.0))
        bodies.append(b)
        hits.append(h)
    far = W.norm(wash(rng, n, 60, 700, tilt=1.2)) * np.clip(0.7 + 0.2 * W.slow(n, 0.15, rng), 0.3, 1)
    y = mix(W.norm(sum(bodies)) * 0.55, W.norm(sum(hits)) * 0.6, far * 0.22)
    return W.seamless(W.cfilter(y, lambda f: (f / 35) / (1 + (f / 35) ** 2) ** 0.5 / (1 + (f / 11000) ** 4)))


@recipe(L, "surf", "rock", "The Atlantic breaking on rock: deep booms in the gullies and spray hissing off, no shingle",
        """The sea on a rocky shore, the ria coast's ledges: each wave booming in the gullies (a deep thud with a hollow
        low ring, the water slamming into rock), spray hissing off the ledges after it, and the water pouring back off
        the rock in sheets (fizz and trickle); under it the sea's roar. No shingle. A 27 s loop of three waves.""",
        sources=[], loop=True, takes=1, lufs=-24, seconds=SURF)
def surf_rock(rng, k):
    n = samples(SURF)
    period = SURF / 3
    bodies, hits = [], []
    for i in range(3):
        at = i * period + rng.uniform(-0.6, 0.6)
        b, h = wave(rng, n, at, SURF, shingle=0.0, big=rng.uniform(0.8, 1.0))
        gully = ck.hollow(rng, [rng.uniform(45, 60), rng.uniform(80, 100), rng.uniform(130, 160)], 1.2, q=3, hit=0.02)
        pour = W.norm(fizz(rng, samples(3.0), 300, 900, 5000, gain=(0.2, 0.9))) * dsp.fit(env([(0, 1), (3.0, 0)], 3.0), samples(3.0))
        h = h + W.place(n, [(at + 1.55, W.norm(gully), 0.6), (at + 2.0, pour, 0.35)])
        bodies.append(b)
        hits.append(h)
    far = W.norm(wash(rng, n, 50, 600, tilt=1.2)) * np.clip(0.7 + 0.2 * W.slow(n, 0.15, rng), 0.3, 1)
    y = mix(W.norm(sum(bodies)) * 0.55, W.norm(sum(hits)) * 0.6, far * 0.25)
    return W.seamless(W.cfilter(y, lambda f: (f / 30) / (1 + (f / 30) ** 2) ** 0.5 / (1 + (f / 10000) ** 4)))


# ---- Fundy's flats ----------------------------------------------------------------------------------------------------

TIDE = 16.0


@recipe(L, "tide", "flats", "Fundy's tide far out over the red mud: its wash a long way off, the mud seeping and popping",
        """The Bay of Fundy's mudflats at night with the tide out: the water's wash a long way off over the flats (a
        low, slow-swelling rush), and close by the red mud seeping as it drains, popping (low bubbles, 80-400 Hz,
        bursting slowly), ticking (fine crackle) and little channels trickling through it (a thin stream of bright
        bubbles). A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-30, seconds=TIDE)
def tide_flats(rng, k):
    n = samples(TIDE)
    far = W.norm(wash(rng, n, 90, 900, tilt=1.1)) * np.clip(0.6 + 0.35 * W.slow(n, 0.12, rng), 0.1, 1.1)
    pops = W.norm(fizz(rng, n, 7, 90, 400, rise=(0.03, 0.2), gain=(0.4, 1.0)))
    seep = W.norm(fizz(rng, n, 60, 600, 2200, rise=(0.1, 0.5), gain=(0.2, 0.7)))
    channel = W.norm(fizz(rng, n, 240, 2000, 7000, rise=(0.05, 0.3), gain=(0.2, 0.8))) * np.clip(0.6 + 0.4 * W.slow(n, 0.3, rng), 0, 1.2)
    ticks = W.norm(stream(n, [(t, synth.click(rng.uniform(2500, 5000), q=6, length=0.008, rng=rng), rng.uniform(0.2, 1.0))
                              for t in W.poisson(TIDE, 9, rng)]))
    y = mix(far * 0.4, pops * 0.45, seep * 0.3, channel * 0.18, ticks * 0.12)
    return W.seamless(W.cfilter(y, lambda f: (f / 40) / (1 + (f / 40) ** 2) ** 0.5 / (1 + (f / 9000) ** 4)))


# ---- A bog beside its causeway, and the salt marsh past a dyke (queue #690, note 990) ---------------------------------
#
# The two marshes the line had in silence (the checklist's "Not yet: marshes and dykes"): a bog's standing water either
# side of a causeway (the plan's marsh water, PlanWater "marsh"/"contaminatedMarsh") and a dykeland's salt marsh out past
# its dyke (a Dyke shore). Neither may sound like the Marsh's own tell, "reeds rustling, surging" (tell-marsh): no grass
# or reeds at all, nothing that surges; nor the Ribbits' bubbling croak (no voiced, pitched bubbles in a run) nor the
# Debris' wet writhing (no slither). Still water, heard by its bubbles and drips; the salt marsh by its creeks running.

BOG = 16.0


def blup(rng, f):
    """Marsh gas come up through the peat: one big, slow bubble breaking the surface (a low Minnaert ring rising as it
    goes, and the film's tiny burst), alone."""
    b = synth.bubble(f, rise=rng.uniform(0.6, 1.2))
    film = synth.bubble(f * rng.uniform(5, 8), rise=0.1) * 0.25
    out = np.zeros(max(len(b), len(film) + samples(0.03)), np.float32)
    out[:len(b)] += b
    out[samples(0.03):samples(0.03) + len(film)] += film
    return out


@recipe(L, "marsh", "peat",
        "A bog's still water by its causeway: marsh gas blupping up through the peat, the moss seeping, a drip",
        """A bog's standing water at night either side of the causeway, all but still: marsh gas coming up through the
        peat now and then, one big slow bubble at a time breaking the surface (100-280 Hz, a second or three apart, never
        in a run), the sphagnum seeping as it holds the water (a sparse, faint fizz of small bubbles, 1.2-4 kHz), a drip
        off the causeway's edge (a small plop) and the open water's faint air. No reeds, nothing surging (the Marsh's tell
        is reeds rustling), no croak. A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-32, seconds=BOG)
def marsh_peat(rng, k):
    n = samples(BOG)
    gas = []
    t = rng.uniform(0, 1)
    while t < BOG:
        gas.append((t, blup(rng, np.exp(rng.uniform(np.log(100), np.log(280)))), rng.uniform(0.5, 1.0)))
        t += rng.uniform(1.2, 3.5)
    gas = W.norm(stream(n, gas))
    seep = W.norm(fizz(rng, n, 25, 1200, 4000, rise=(0.1, 0.5), gain=(0.15, 0.6)))
    drips = W.norm(stream(n, [(t, synth.bubble(rng.uniform(700, 1300), rise=rng.uniform(0.3, 0.7)), rng.uniform(0.4, 1.0))
                              for t in W.poisson(BOG, 0.4, rng)]))
    air = W.norm(wash(rng, n, 120, 900, tilt=1.3)) * np.clip(0.6 + 0.25 * W.slow(n, 0.1, rng), 0.2, 1)
    y = mix(gas * 0.7, seep * 0.22, drips * 0.3, air * 0.08)
    return W.seamless(W.cfilter(y, lambda f: (f / 60) / (1 + (f / 60) ** 2) ** 0.5 / (1 + (f / 7000) ** 4)))


@recipe(L, "marsh", "pools",
        "A bog's still water, its pools darker and deeper: slower marsh gas, the peat ticking, hardly a drip",
        """The same bog darker and deeper: the gas rising slower and lower (80-200 Hz, two to five seconds apart, now and
        then two together), the peat ticking as it settles (faint dry clicks, 2-4 kHz), the moss's seep thinner, hardly a
        drip, and the water's air low and close. No reeds, nothing surging, no croak. A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-32, seconds=BOG)
def marsh_pools(rng, k):
    n = samples(BOG)
    gas = []
    t = rng.uniform(0, 1)
    while t < BOG:
        gas.append((t, blup(rng, np.exp(rng.uniform(np.log(80), np.log(200)))), rng.uniform(0.6, 1.0)))
        if rng.uniform() < 0.25:
            gas.append((t + rng.uniform(0.15, 0.4), blup(rng, rng.uniform(140, 260)), rng.uniform(0.3, 0.6)))
        t += rng.uniform(2.0, 5.0)
    gas = W.norm(stream(n, gas))
    seep = W.norm(fizz(rng, n, 12, 1200, 3500, rise=(0.1, 0.5), gain=(0.15, 0.5)))
    ticks = W.norm(stream(n, [(t, synth.click(rng.uniform(2000, 4000), q=5, length=0.008, rng=rng), rng.uniform(0.2, 1.0))
                              for t in W.poisson(BOG, 3, rng)]))
    air = W.norm(wash(rng, n, 80, 600, tilt=1.4)) * np.clip(0.6 + 0.25 * W.slow(n, 0.1, rng), 0.2, 1)
    y = mix(gas * 0.75, seep * 0.15, ticks * 0.1, air * 0.1)
    return W.seamless(W.cfilter(y, lambda f: (f / 50) / (1 + (f / 50) ** 2) ** 0.5 / (1 + (f / 6000) ** 4)))


SALT = 16.0


@recipe(L, "saltmarsh", "creeks",
        "The salt marsh past a dyke: the tide running in its creeks, a drain gurgling at the dyke, the bay far out",
        """A dykeland's salt marsh at night, heard from the dyke: the tide running in and out of the marsh's creeks (thin
        streams of small bubbles, 1.5-6 kHz, swelling and easing slowly as the water moves), a creek gurgling where it
        drains toward the dyke's sluice (a knot of big bubbles every few seconds, 300-800 Hz), the red mud at the creeks'
        edges ticking and popping, and far out past the marsh the bay's wash over the flats (low, slow-swelling). No grass
        rustling, nothing surging (the Marsh's tell is reeds rustling). A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-30, seconds=SALT)
def saltmarsh_creeks(rng, k):
    n = samples(SALT)
    creeks = sum(W.norm(fizz(rng, n, 180, 1500, 6000, rise=(0.05, 0.3), gain=(0.2, 0.8)))
                 * np.clip(0.55 + 0.35 * W.slow(n, 0.08, rng), 0.1, 1.1) for _ in range(2))
    times, t = [], rng.uniform(0, 2)
    while t < SALT:
        times.append(t)
        t += rng.uniform(2.5, 5.5)
    gurgle = W.norm(W.place(n, [(t, burble(rng, 300, 800, (4, 10), 0.4), rng.uniform(0.5, 1.0)) for t in times]))
    mud = W.norm(fizz(rng, n, 5, 100, 350, rise=(0.03, 0.2), gain=(0.4, 1.0)))
    ticks = W.norm(stream(n, [(t, synth.click(rng.uniform(2500, 5000), q=6, length=0.008, rng=rng), rng.uniform(0.2, 1.0))
                              for t in W.poisson(SALT, 6, rng)]))
    far = W.norm(wash(rng, n, 70, 700, tilt=1.2)) * np.clip(0.6 + 0.35 * W.slow(n, 0.1, rng), 0.1, 1.1)
    y = mix(W.norm(creeks) * 0.35, gurgle * 0.4, mud * 0.25, ticks * 0.08, far * 0.3)
    return W.seamless(W.cfilter(y, lambda f: (f / 40) / (1 + (f / 40) ** 2) ** 0.5 / (1 + (f / 9000) ** 4)))


@recipe(L, "saltmarsh", "sluice",
        "The salt marsh past a dyke, the tide coming in: the creeks filling, water pushing at the sluice, the bay nearer",
        """The same marsh with the tide on the make: the creeks filling (their small-bubble streams thicker and lower,
        1-4 kHz, rising over the loop's length and easing back), the water pushing through the dyke's sluice in a steady
        low gurgle (big bubbles, 250-600 Hz, close together), the bay's wash nearer and fuller, hardly any mud showing.
        No grass rustling, nothing surging. A 16 s loop.""",
        sources=[], loop=True, takes=1, lufs=-30, seconds=SALT)
def saltmarsh_sluice(rng, k):
    n = samples(SALT)
    fill = np.clip(0.6 + 0.3 * np.sin(2 * np.pi * np.arange(n) / n), 0.2, 1).astype(np.float32)
    creeks = W.norm(fizz(rng, n, 260, 1000, 4000, rise=(0.05, 0.35), gain=(0.2, 0.8))) * fill
    sluice = W.norm(fizz(rng, n, 6, 250, 600, rise=(0.2, 0.8), gain=(0.4, 1.0))) * np.clip(0.7 + 0.2 * W.slow(n, 0.2, rng), 0.3, 1)
    far = W.norm(wash(rng, n, 80, 1000, tilt=1.1)) * np.clip(0.65 + 0.3 * W.slow(n, 0.1, rng), 0.2, 1.1)
    y = mix(creeks * 0.35, sluice * 0.35, far * 0.45)
    return W.seamless(W.cfilter(y, lambda f: (f / 40) / (1 + (f / 40) ** 2) ** 0.5 / (1 + (f / 8000) ** 4)))
