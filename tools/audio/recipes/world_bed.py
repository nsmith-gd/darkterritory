"""The train bed (spec A.2): boiler roar, exhaust chuff and rod clank, wheel on rail, slack action, brakes, wind, the
frame's groan, the vent. The floor everything sits on, heard from the cab, the roofs and the cars.

Approach: steam, air and friction are modelled (recipes/world_kit.py: a turbulent jet driven by pressure and hole size, wind
driven by its speed, a rubbed body's modes); every blow of metal on metal starts from the packs' real impacts, varispeeded
to the part's size and layered with the body it rings (a rod, a knuckle, a wooden car) and the weight behind it. Bed
sounds keep their weight in the low-mids (the tells live above them, spec A.4).
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter, croom

SR = dsp.SR
# A 0.9 m car wheel's axial modes and a 12 m rail's ring (pinned between sleepers); the stuff a train is made of.
WHEEL = [380, 1050, 1880, 2750, 3640, 4550, 5450]
CHIMNEY = [62, 195, 420, 655]   # smokebox and chimney as a Helmholtz resonator, then the chimney's pipe modes in hot gas


def loop_cycle(seconds):
    return samples(seconds)


def outdoors(y, rng, wet=0.12):
    """Close to the train in open country: two slaps off the train's sides and a little night."""
    return W.space(y, rng, "night", wet=wet)


def circ_outdoors(y, rng, wet=0.12):
    """The same for a loop: the slaps and the night tail go round the cycle."""
    z = y + 0.35 * np.roll(y, samples(0.0045)) + 0.2 * np.roll(y, samples(0.011))
    return croom(z, "night", wet, rng)


# ---- Vent blow-off --------------------------------------------------------------------------------------------------------

VENT = dict(peak=1000, low=0.75, rasp=0.15, eddy=0.75)


@recipe("bed-vent", "open", "jet",
        "The vent valve opening: a thin spit of water and steam that widens into the full roar",
        """A turbulent steam jet modelled from the physics, not a recording: broadband noise in half-octave bands, peaking
        where a jet of that speed through that size of hole peaks, every band's level churning like real turbulence, with
        the plume's low billow under it. As the valve opens the hole grows, so the hiss starts thin and high and drops into
        a wide roar within half a second, with condensate from the pipe spitting in it. It ends at full blow, fading so
        the held loop takes over.""", takes=2, lufs=-18)
def vent_open(rng, k):
    L = 2.4
    if k == 0:      # cracked open: a thin hiss for a moment, then the valve comes round
        o = env([(0, 0.04), (0.3, 0.12), (0.55, 0.6), (0.85, 1.0), (L, 1.0)], L, curve="exp")
        snap = (0.3, 0.85)
    else:           # thrown open
        o = env([(0, 0.05), (0.06, 0.4), (0.18, 0.85), (0.3, 1.0), (L, 1.0)], L, curve="exp")
        snap = (0.06, 0.3)
    y = W.jet(L, rng, opening=o, **VENT)
    # condensate in the pipe spits out with the first of it: wet crackle inside the roar, never ahead of it (a lone
    # transient before the hiss is what made the earlier takes read as a bottle opening)
    a, b = snap[1] * 0.6, snap[1] + 0.5
    spit = W.sputter(b - a, rng, rate=18 if k == 0 else 10) * env([(0, 0), (0.1, 1), (b - a, 0)], b - a)
    lvl = np.sqrt(np.convolve(y ** 2, np.ones(2400) / 2400, "same"))
    y = mix(y, fit(np.concatenate([np.zeros(samples(a)), spit]), len(y)) * lvl * 2.0)
    y = dsp.shaped(y, [(0, 1), (L - 0.7, 1), (L, 0)])
    return outdoors(y, rng)


@recipe("bed-vent", "blow", "jet",
        "Steam blowing off, held: a broad roar under the hiss, churning, with the odd spit of water",
        """The same modelled jet at full bore through the blow-off pipe: a roar centred near 1 kHz with the hiss above and
        the plume's billow below, every band wandering at the rate its eddies turn over (big ones slowly, small ones fast),
        so it lives like real steam instead of sitting still like noise. A few spits of water ride in it. Built as an
        exact 10 s cycle (noise, wander and reverb all periodic), so the loop has no seam.""", loop=True, takes=1, lufs=-18)
def vent_blow(rng, k):
    n = loop_cycle(10.0)
    y = W.jet(10.0, rng, n=n, **VENT)
    spits = [(t, W.sputter(0.12, rng, rate=40, size=0.8), 1.0) for t in W.poisson(10.0, 0.7, rng)]
    y = y + W.place(n, spits) * np.std(y) * 1.2
    return seamless(circ_outdoors(y, rng))


@recipe("bed-vent", "close", "jet",
        "The vent shut: the roar narrowing into a rising hiss that cuts off, a last spit and the plume dying",
        """The modelled jet with its hole closing: as the valve shuts the noise climbs in pitch and thins (a smaller hole
        sings higher) while it drops away, ending in a short high hiss and a soft knock of the valve seating. The plume
        already in the air keeps billowing for a moment after, and a last drop of condensate spits out.""", takes=2,
        lufs=-22)
def vent_close(rng, k):
    L = 1.8
    shut = 0.55 if k == 0 else 0.35
    o = env([(0, 1.0), (shut * 0.35, 0.65), (shut * 0.75, 0.18), (shut, 0.04), (shut + 0.04, 0.0), (L, 0.0)], L)
    y = W.jet(L, rng, opening=o, **VENT)
    plume = cfilter(pnoise(samples(L), rng), lambda f: 1 / (1 + (f / 300) ** 2) * (f / 40) / (1 + f / 40))
    plume *= env([(0, 0.0), (shut * 0.5, 0.5), (shut + 0.1, 0.35), (L, 0)], L, curve="lin") * np.std(y) * 0.6
    seat = W.body(rng, 340, W.BAR, decay=0.04, contact=0.0012, length=0.2) * 0.08
    y = mix(y, plume, np.concatenate([np.zeros(samples(shut)), seat]))
    y = mix(y, np.concatenate([np.zeros(samples(shut + 0.08)), W.sputter(0.1, rng, rate=30) * np.std(y) * 1.5]))
    return outdoors(y, rng)


# ---- Boiler roar ----------------------------------------------------------------------------------------------------------

def boiler(rng, n, heat):
    """The boiler and fire, from the footplate: the draught roaring through the firebed (lapping, faster and harder as
    it's forced), the water seething against the firebox plates, the shell's low hum, gland leaks hissing. `heat` 0..1."""
    L = n / SR
    roar = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / (160 + 160 * heat)) ** 2) * (f / 40) ** 2 / (1 + (f / 40) ** 2))
    lap = np.clip(1 + (0.16 + 0.08 * heat) * slow(n, 3 + 4 * heat, rng) + 0.12 * slow(n, 0.6, rng), 0.3, None)
    roar = norm(roar * lap)
    # the seethe: thousands of small steam bubbles collapsing on the hot plates, heard through the shell
    seethe = synth.bubbles(L, 900 + 2500 * heat, 180, 900, rng, rise=(0.0, 0.1))
    seethe = W.cyclic(lambda z: lp(z, 1400, 2), seethe)
    seethe = norm(seethe) * (1 + 0.3 * slow(n, 0.8, rng))
    shell = W.cyclic(lambda z: dsp.resonate(z, [78, 141, 233, 352], q=18), norm(roar))
    leak = W.jet(L, rng, n=n, peak=4200, low=0.0, eddy=1.0)
    leak = norm(leak) * np.clip(0.5 + 0.5 * slow(n, 0.5, rng), 0.05, None)
    y = roar * 1.0 + seethe * (0.10 + 0.14 * heat) + norm(shell) * 0.25 + leak * (0.025 + 0.05 * heat)
    if heat > 0.7:
        # the safety valves feathering just short of the lift: a thin, unsteady hiss coming and going
        sim = W.jet(L, rng, n=n, peak=5200, opening=0.25, low=0.0, eddy=1.2)
        on = np.clip(slow(n, 0.35, rng) + 0.2, 0, None) ** 2
        y += norm(sim) * on / (np.max(on) + 1e-9) * 0.07
    return y


@recipe("bed-boiler-roar", "roar-low", "draught",
        "The boiler at low pressure: the fire's draught roaring softly, the water seething, the shell humming",
        """A matched pair with roar-high (the code crossfades them by pressure): the fire's roar as lapping low noise, the
        boiling water as thousands of small bubbles collapsing against the plates (heard through the shell, so dark),
        the shell's own low hum, and a faint gland leak. At low pressure the roar is slow and soft. 12 s exact cycle.""",
        loop=True, takes=1, lufs=-20)
def roar_low(rng, k):
    n = loop_cycle(12.0)
    return seamless(croom(boiler(rng, n, 0.2), "cab", 0.25, rng))


@recipe("bed-boiler-roar", "roar-high", "draught",
        "The boiler near the redline: the fire forced and roaring hard, the water boiling violently, valves feathering",
        """The same boiler as roar-low, worked hard: the draught's roar louder, brighter and lapping faster, the boiling
        dense and loud, the leaks hissing harder, and the safety valves just starting to feather (a thin hiss that comes
        and goes) as a warning before they lift. Same parts at the same distance as roar-low, so the code's crossfade by
        pressure is one boiler getting angrier. 12 s exact cycle.""", loop=True, takes=1, lufs=-20)
def roar_high(rng, k):
    n = loop_cycle(12.0)
    return seamless(croom(boiler(rng, n, 0.9), "cab", 0.25, rng))


# ---- Exhaust chuff and rod clank --------------------------------------------------------------------------------------------

def exhaust(rng, heavy):
    """One exhaust beat: the cylinder's steam released through the blast nozzle up the chimney. The pressure pulse rises
    in a few ms and dies over a tenth of a second; its jet noise (the 'ch') sweeps down as the pressure falls; the
    chimney rings it (the 'uff'); and the pulse itself leaving the chimney top radiates as a thump (a monopole: the
    rate of change of the flow). Working hard, the pulse is sharper, longer and choked enough to crackle (the bark)."""
    L = 0.8 if heavy else 0.6
    n = samples(L)
    t = np.arange(n) / SR
    rise = rng.uniform(0.003, 0.006) if heavy else rng.uniform(0.007, 0.013)
    decay = rng.uniform(0.11, 0.16) if heavy else rng.uniform(0.065, 0.1)
    p = (1 - np.exp(-t / rise)) * np.exp(-t / decay)
    if rng.random() < 0.45:
        # the far end of the cylinder exhausting a moment later: an uneven, two-step beat
        d = samples(rng.uniform(0.018, 0.04))
        p[d:] += rng.uniform(0.2, 0.45) * p[:-d]
    p = (p / p.max()).astype(np.float32)
    jet = W.jet(L, rng, pressure=p, peak=rng.uniform(560, 760) * (1.25 if heavy else 1.0), low=0.35,
                rasp=0.45 if heavy else 0.0, eddy=0.5)
    k = rng.uniform(0.94, 1.06)
    pipe = dsp.resonate(jet, [f * k for f in CHIMNEY[1:]], q=3, gains=[1, 0.7, 0.45])
    flow = np.sqrt(p)
    dq = lp(np.diff(flow, prepend=0).astype(np.float32) * SR / 1000, 500, 2)
    thump = dsp.resonate(dq, [CHIMNEY[0] * k], q=2.2) * 0.5 + dq
    y = norm(jet) * 0.7 + norm(pipe) * 0.45 + norm(thump) * (1.0 if heavy else 0.7)
    return outdoors(y, rng, wet=0.08)


@recipe("bed-chuff", "chuff", "blast",
        "One exhaust beat: a steam pulse up the chimney, the 'ch' of the jet falling into the chimney's hollow 'uff'",
        """Modelled from the blast pipe: a pressure pulse that rises in about 10 ms and dies over a tenth of a second
        drives a steam jet whose hiss sweeps down as the pressure drops; the chimney rings that noise at its pipe modes
        (the vowel in a chuff), and the pulse leaving the chimney top thumps (it's a monopole, so you hear the flow's
        rate of change). Takes vary the pulse, and some come in two steps as the cylinder's far end exhausts a moment
        later, so a run of them is uneven like a real engine.""", takes=6, lufs=-30)
def chuff(rng, k):
    return exhaust(rng, heavy=False)


@recipe("bed-chuff", "chuff-heavy", "blast",
        "One exhaust beat working hard: a sharper, longer pulse that barks and crackles up the chimney",
        """The same blast-pipe model with the regulator wide: the pulse rises in 3-6 ms and lasts half again as long, the
        jet runs fast enough to crackle (the steep shocklets of a choked jet: the bark), and the chimney thump is
        heavier. Six takes, uneven like a real engine.""", takes=6, lufs=-30)
def chuff_heavy(rng, k):
    return exhaust(rng, heavy=True)


@recipe("bed-chuff", "rod-clank", "knock",
        "The side rod knocking once a turn: a heavy, oily clonk of steel on its crank pin",
        """Kenney's heavy plate impacts varispeeded down for the mass of a 2.5 m coupling rod, with a medium metal hit
        pitched lower and darkened for the steel ring, a modelled steel bar's modes for the rod itself (short: it's oiled
        and held at both ends), and a low knock of weight through the frame. Each take a different plate and tuning.""",
        sources=[f"kenney_impact-sounds:impactPlate_heavy_00{i}" for i in range(4)]
        + [f"kenney_impact-sounds:impactMetal_medium_00{i}" for i in (0, 2, 3, 1)], takes=4, lufs=-28)
def rod_clank(rng, k):
    plate = W.rec(f"kenney_impact-sounds:impactPlate_heavy_00{k}", semis=-rng.uniform(1.5, 3.5), hi=5000)
    ring = W.rec(f"kenney_impact-sounds:impactMetal_medium_00{(0, 2, 3, 1)[k]}", semis=-rng.uniform(5, 8), hi=3500, tau=0.1)
    rod = W.body(rng, rng.uniform(170, 230), W.BAR, decay=0.09, contact=0.0005)
    y = mix(norm(plate), norm(ring) * 0.35, norm(rod) * 0.4, W.knock(rng, 75, 0.16) * 0.45)
    return outdoors(y, rng, wet=0.1)


# ---- Wheel on rail ----------------------------------------------------------------------------------------------------------

def joint(rng, k):
    """One axle over a rail joint: the wheel drops off the battered end and hits the next rail (a hard contact that rings
    the rail and the wheel), with the truck's weight thumping into the sleepers and the car's frame."""
    L = 0.45
    rail = W.body(rng, rng.uniform(380, 460), W.BAR, decay=rng.uniform(0.04, 0.07), contact=0.00035, length=L)
    wheel = W.body(rng, WHEEL[0] * rng.uniform(0.95, 1.05), [f / WHEEL[0] for f in WHEEL], decay=0.03, damp=0.3,
                   contact=0.0003, length=L)
    clang = W.rec(f"kenney_impact-sounds:impactMetal_heavy_00{(0, 2, 4, 3, 0, 2)[k]}", semis=-rng.uniform(3, 6), hi=4500,
                  tau=0.05)
    plate = W.rec(f"kenney_impact-sounds:impactPlate_heavy_00{(4, 2, 0, 1, 3, 4)[k]}", semis=-rng.uniform(0, 2), hi=2500)
    y = mix(norm(rail) * 0.5, norm(wheel) * 0.35, norm(clang) * 0.45, norm(plate) * 0.6, W.knock(rng, 70, 0.2) * 0.55)
    if rng.random() < 0.6:
        # the drop: a lighter tick as the wheel leaves the near rail end, a few ms before the hit
        tick = W.body(rng, rng.uniform(500, 650), W.BAR, decay=0.02, contact=0.0004, length=0.1) * 0.35
        y = mix(np.concatenate([np.zeros(samples(rng.uniform(0.006, 0.014))), y]), tick)
    return outdoors(lp(y, 6000, 2), rng, wet=0.1)


@recipe("bed-wheel-rail", "joint", "clack",
        "One axle over a rail joint: the wheel dropping off one rail end and clacking onto the next",
        """The packs' heavy metal and plate impacts varispeeded down for a wheel's mass, layered with a modelled rail
        ring (a steel bar's modes, the rail pinned between sleepers) and a wheel's damped ring, over the thump of the
        truck's weight into the sleepers. Most takes have the small tick of the wheel leaving the near rail end a few ms
        before the hit. Fired per axle, two axles a truck, it's the click-clack.""",
        sources=[f"kenney_impact-sounds:impactMetal_heavy_00{i}" for i in (0, 2, 3, 4)]
        + [f"kenney_impact-sounds:impactPlate_heavy_00{i}" for i in range(5)], takes=6, lufs=-29,
        preview=lambda takes, rng: _clickclack(takes, rng))
def wheel_joint(rng, k):
    return joint(rng, k)


def _clickclack(takes, rng):
    """The takes as the game fires them at 15 m/s on 12 m rails: two axles a truck, two trucks a car, car after car."""
    b = dsp.Bus(9.0)
    t, i = 0.2, 0
    while t < 8.4:
        for gap in (0.0, 0.12, 0.85, 0.97):                 # truck A axles, then truck B (the next car's leading truck)
            b.at(t + gap + rng.uniform(-0.004, 0.004), takes[i % len(takes)], -1.5 * (gap > 0.5))
            i += 1
        t += 0.8
    return b.x


@recipe("bed-wheel-rail", "flange", "squeal",
        "Flange squeal on a curve: a wheel ringing at its own mode, shrill and unsteady, over the grind of the flange",
        """Modelled the way squeal happens: a wheel on a curve slips sideways, stick-slip locks one of its axial modes into
        a near-pure tone (here 2.3 and 3.6 kHz, with harmonics) that shivers in pitch and comes and goes in bursts as
        the slip catches and lets go, sometimes jumping to the other mode. Under it the flange grinds on the rail head
        (rubbing noise rung through the wheel's modes). 10 s exact cycle.""", loop=True, takes=1, lufs=-22)
def flange(rng, k):
    n = loop_cycle(10.0)
    on1 = np.clip(1.6 * slow(n, 0.6, rng) + 0.5, 0, 1)
    on2 = np.clip(1.6 * slow(n, 0.5, rng) - 0.3, 0, 1) * (1 - on1 * 0.7)
    sq = W.squeal(n, rng, [2310], on1, wander=0.005) + W.squeal(n, rng, [3620], on2, wander=0.006) * 0.8
    grind = W.friction(n, rng, WHEEL, rough=1.4, grit=0.6, q=20, loop=True)
    grind = cfilter(grind, lambda f: 1 / (1 + (f / 7000) ** 2)) * np.clip(0.7 + 0.3 * slow(n, 2, rng), 0.2, None)
    y = sq * 0.6 + norm(grind) * 0.18
    return seamless(circ_outdoors(y, rng, 0.15))


def rolling(rng, n, speed, base_semis):
    """Wheels rolling: a real train's interior rumble (the pack's rail-transport loop), varispeeded to the speed, with
    each wheel's once-a-turn thump from a tyre worn out of round, loose gear rattling and the rail's roar rising with
    speed. Periodic over n."""
    x = src_loop("sfx_100_v2:loop_ambient_04", n, base_semis, rng)
    rev = speed / (np.pi * 0.9)                                 # wheel turns a second
    cyc = max(1, round(rev * n / SR))
    t = np.arange(n) / SR
    turn = 1 + 0.12 * np.sin(2 * np.pi * cyc * t / (n / SR)) ** 8
    roar = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(300 + 30 * speed)) / 1.1) ** 2))
    roar *= np.clip(1 + 0.25 * slow(n, 1.5, rng), 0.2, None)
    rattle = W.place(n, [(tt, W.body(rng, rng.uniform(900, 2400), W.PLATE, decay=0.015, length=0.05, count=6),
                          rng.uniform(0.2, 1.0)) for tt in W.poisson(n / SR, 4 + speed * 0.6, rng)])
    y = norm(x) * turn + norm(roar) * (0.15 + speed * 0.012) + norm(rattle) * 0.06
    return y


def src_loop(key, n, semis, rng):
    """A recorded loop made into an exact n-sample cycle: varispeeded, tiled, and its own seam crossfaded away."""
    x = W.rec(key, semis=semis)
    k = samples(0.5)
    reps = int(np.ceil((n + k) / (len(x) - k))) + 1
    out = np.zeros(n + k, np.float32)
    pos = 0
    fade_in = np.sin(np.linspace(0, np.pi / 2, k)) ** 2
    for _ in range(reps):
        seg = x.copy()
        seg[:k] *= fade_in
        seg[-k:] *= fade_in[::-1]
        m = min(len(seg), len(out) - pos)
        if m <= 0:
            break
        out[pos:pos + m] += seg[:m]
        pos += len(seg) - k
    return dsp.wrap(out, n)


@recipe("bed-wheel-rail", "roll-slow", "worn",
        "Wheels rolling slowly: the real train rumble pitched down, with worn tyres thumping once a turn and loose gear",
        """The pack's rail-transport loop (the one kept from the earlier pass) varispeeded down four semitones for a slow
        train, made into an exact 10 s cycle, with what speed changes added on top: a faint thump once per wheel turn
        from a tyre worn out of round (1.8 turns a second at 5 m/s), the roar of the rail band, and loose gear ticking
        now and then.""", sources=["sfx_100_v2:loop_ambient_04"], loop=True, takes=1, lufs=-22)
def roll_slow(rng, k):
    return seamless(rolling(rng, loop_cycle(10.0), 5.0, -4))


@recipe("bed-wheel-rail", "roll-fast", "worn",
        "Wheels rolling fast: the real train rumble pitched up, the turns blurring, the rail roaring",
        """The same as roll-slow at 20 m/s: the rail-transport loop pitched up two semitones, the once-a-turn thump now
        seven a second (a flutter in the roar), the rail band louder and brighter, and the loose gear rattling more.""",
        sources=["sfx_100_v2:loop_ambient_04"], loop=True, takes=1, lufs=-22)
def roll_fast(rng, k):
    return seamless(rolling(rng, loop_cycle(10.0), 20.0, 2))


# ---- Slack action -----------------------------------------------------------------------------------------------------------

def coupling(rng, k, out_):
    """One coupling taking up its slack. Running in, the couplers and buffers slam together: a heavy, dull blow that
    booms the wooden car. Running out, the drawgear yanks taut: the knuckles clank and ring, the draft spring twangs,
    loose gear rattles after."""
    if not out_:
        key, semis = SLAMS[k]
        slam = W.rec(key, semis=semis - rng.uniform(0, 1.5), hi=3500, start=0.125 if key.endswith("36") else 0.0)
        plate = W.rec(f"kenney_impact-sounds:impactPlate_heavy_00{k % 5}", semis=-rng.uniform(2, 4), hi=3000)
        car = W.body(rng, rng.uniform(65, 85), W.PLATE, decay=0.22, contact=0.003, damp=1.5)
        y = mix(norm(slam) * 0.8, norm(plate) * 0.8, norm(car) * 0.45, W.knock(rng, 44 + 4 * k, 0.35) * 0.8)
    else:
        clank = W.rec(f"kenney_impact-sounds:impactMetal_heavy_00{(1, 3, 0, 2, 4, 1)[k]}", semis=-rng.uniform(2, 4), hi=6000,
                      tau=0.15)
        slam = W.rec(("sfx_100_v2:metal_hit_01", "sfx_100_v2:misc_30")[k % 2], semis=-rng.uniform(1, 3))
        knuckle = W.body(rng, rng.uniform(380, 520), W.BAR, decay=0.14, contact=0.0003)
        spring = W.body(rng, rng.uniform(105, 135), W.BAR, decay=0.25, contact=0.002, count=2)
        y = mix(norm(clank) * 0.7, norm(slam) * 0.55, norm(knuckle) * 0.4, norm(spring) * 0.3, W.knock(rng, 70, 0.2) * 0.45)
    # loose gear in the car rattling a moment after the jolt
    rat = synth.skitter(0.35, 40, rng, f=(700, 2600), q=(6, 14), legs=5) * env([(0, 0), (0.03, 1), (0.35, 0)], 0.35)
    y = mix(y, np.concatenate([np.zeros(samples(rng.uniform(0.05, 0.12))), norm(rat) * rng.uniform(0.08, 0.16)]))
    return outdoors(y, rng, wet=0.12)


# a different real slam for each run-in take, each pitched for a car's mass
SLAMS = [("sfx_100_v2:door_03", -4), ("sfx_100_v2:metal_hit_01", -5), ("sfx_100_v2:misc_36", -4),
         ("sfx_100_v2:misc_30", -6), ("sfx_100_v2:wood_hit_02", -2), ("sfx_100_v2:door_03", -7)]
SLACK_SRC = sorted({k for k, _ in SLAMS}) + [f"kenney_impact-sounds:impactPlate_heavy_00{i}" for i in range(5)]


@recipe("bed-slack", "run-in", "buffers",
        "One coupling closing up: couplers slamming together, a dull heavy blow that booms the wooden car",
        """Real slams (the pack's heavy door slam, metal door hit, knocks and a wooden thud, a different one per take)
        varispeeded down 2-8 semitones for a car's mass,
        layered with Kenney's heavy plate impacts, a modelled wooden car body booming at 65-85 Hz and a deep knock of
        weight, then the car's loose gear rattling a moment after the jolt. Six takes from different slams, so a run
        down the train never repeats.""", sources=SLACK_SRC, takes=6, lufs=-28)
def slack_in(rng, k):
    return coupling(rng, k, False)


@recipe("bed-slack", "run-out", "drawgear",
        "One coupling stretching out: the drawgear yanked taut, knuckles clanking and ringing, the spring twanging",
        """Sharper and more metallic than run-in, because it is iron pulling on iron rather than a car's body slamming:
        Kenney's heavy metal hits and the pack's metal door hit varispeeded down a little, a modelled cast-steel knuckle
        ringing at 380-520 Hz, the draft gear's spring twanging low, less boom, and loose gear rattling after.""",
        sources=[f"kenney_impact-sounds:impactMetal_heavy_00{i}" for i in range(5)]
        + ["sfx_100_v2:metal_hit_01", "sfx_100_v2:misc_30"], takes=6, lufs=-28)
def slack_out(rng, k):
    return coupling(rng, k, True)


# ---- Brakes -----------------------------------------------------------------------------------------------------------------

def brake_drag(rng, n, hot, loop=True):
    """Cast-iron blocks dragging on steel tyres: contact noise roughened by the asperities, rung through the wheel's modes,
    pulsing once a wheel turn where the tyre is out of round. Hot, the blocks grind harsher and brighter, squeal in
    bursts, and judder."""
    g = W.friction(n, rng, WHEEL, rough=1.0 + 0.8 * hot, grit=0.35 + 0.6 * hot, q=14 + 10 * hot, loop=loop)
    g = cfilter(g, lambda f: (f / 120) / (1 + f / 120) / (1 + (f / (5000 + 3000 * hot)) ** 2))
    L = n / SR
    t = np.arange(n) / SR
    cyc = max(1, round(5.3 * L))                                # 15 m/s on 0.9 m wheels
    turn = 1 + (0.15 + 0.2 * hot) * np.sin(2 * np.pi * cyc * t / L)
    body = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(160)) / 0.7) ** 2))
    y = norm(g) * turn * np.clip(1 + 0.2 * slow(n, 1.0, rng), 0.3, None) + norm(body) * 0.25
    on = np.clip(1.5 * slow(n, 0.4, rng) + (-0.6 + 0.9 * hot), 0, 1)
    y += W.squeal(n, rng, [1880 * rng.uniform(0.97, 1.03)], on, wander=0.008 + 0.01 * hot) * (0.12 + 0.2 * hot)
    return y


@recipe("bed-brake", "drag", "blocks",
        "Brakes dragging, cold: iron blocks grinding on the tyres, pulsing once a wheel turn, a squeal now and then",
        """Modelled friction: broadband contact noise roughened by the passing asperities and rung through a car wheel's
        axial modes, with a low hum of the block vibrating in its hanger. It swells once a wheel turn (a tyre worn out of
        round), and a faint squeal catches and lets go. 10 s exact cycle.""", loop=True, takes=1, lufs=-22)
def brake_drag_cold(rng, k):
    n = loop_cycle(10.0)
    return seamless(circ_outdoors(brake_drag(rng, n, 0.0), rng, 0.1))


@recipe("bed-brake", "drag-hot", "blocks",
        "Brakes dragging, hot: harsher and brighter, squealing in bursts, juddering",
        """The same friction model with the blocks hot: rougher contact and more hiss in the grind, the wheel modes ringing
        harder, the once-a-turn pulse deepened into a judder, and the squeal coming in long bursts. Same distance and
        parts as the cold drag so the code can crossfade them by heat. 10 s exact cycle.""", loop=True, takes=1, lufs=-22)
def brake_drag_hot(rng, k):
    n = loop_cycle(10.0)
    return seamless(circ_outdoors(brake_drag(rng, n, 1.0), rng, 0.1))


def rigging(rng, count, spread):
    """The brake rigging moving: rods and levers knocking in their hangers (small real metal hits, pitched for heavy iron)."""
    b = dsp.Bus(spread + 0.4)
    for i in range(count):
        h = W.rec(f"kenney_impact-sounds:impactMetal_light_00{rng.integers(5)}", semis=-rng.uniform(6, 10), hi=4000)
        b.at(rng.uniform(0, spread), norm(h) * rng.uniform(0.3, 0.7))
    return b.x


@recipe("bed-brake", "apply", "shoes",
        "Shoes going onto the tyres: the rigging clanking, the blocks clacking on, the grind rising",
        """The brake rigging's rods and levers knocking in their hangers (Kenney's light metal hits pitched down 6-10
        semitones for heavy iron), then the iron blocks meeting the tyres (a heavy plate impact) and the modelled
        block-on-tyre friction rising into the drag. Ends at full drag, fading so the drag loop takes over.""",
        sources=[f"kenney_impact-sounds:impactMetal_light_00{i}" for i in range(5)]
        + [f"kenney_impact-sounds:impactPlate_medium_00{i}" for i in range(3)], takes=3, lufs=-20)
def brake_apply(rng, k):
    L = 2.0
    n = samples(L)
    rig = rigging(rng, 3 + k, 0.18)
    contact = 0.16 + 0.04 * k
    shoe = W.rec(f"kenney_impact-sounds:impactPlate_medium_00{k}", semis=-rng.uniform(3, 5), hi=4000)
    drag = brake_drag(rng, n, 0.1 * k, loop=False)
    drag *= env([(0, 0), (contact, 0), (contact + 0.3, 1), (L - 0.6, 1), (L, 0)], L)
    y = mix(norm(rig) * 0.5, np.concatenate([np.zeros(samples(contact)), norm(shoe) * 0.7]), norm(drag) * 0.55)
    return outdoors(y, rng, 0.1)


@recipe("bed-brake", "release", "shoes",
        "Brakes coming off: the grind dying with a last squeal as the blocks lift, the rigging knocking back",
        """The modelled drag falling away in a quarter of a second, a brief squeal as the blocks barely touch, then the
        rigging's rods and levers knocking back against their stops (Kenney's light metal hits pitched down for heavy
        iron). No air: on this train the brake is the blocks and the iron that moves them.""",
        sources=[f"kenney_impact-sounds:impactMetal_light_00{i}" for i in range(5)], takes=2, lufs=-20)
def brake_release(rng, k):
    L = 1.6
    n = samples(L)
    off = 0.25 + 0.1 * k
    drag = brake_drag(rng, n, 0.2, loop=False) * env([(0, 1), (off, 0.08), (off + 0.15, 0), (L, 0)], L)
    chirp = W.squeal(n, rng, [1880 * rng.uniform(1.0, 1.15)], env([(0, 0), (off * 0.6, 0), (off, 1), (off + 0.2, 0), (L, 0)], L)) * 0.25
    rig = rigging(rng, 3, 0.25)
    y = mix(norm(drag) * 0.7, chirp, np.concatenate([np.zeros(samples(off + 0.05)), norm(rig) * 0.45]))
    return outdoors(y, rng, 0.1)


# ---- Wind -------------------------------------------------------------------------------------------------------------------

@recipe("bed-wind", "wind-slow", "rush",
        "Wind past you at low speed: a soft rush that breathes, fittings whistling faintly when it picks up",
        """Modelled from the wind's speed (8 m/s, gusting about 15%): turbulent rush whose brightness and loudness follow the
        instantaneous speed, a little buffeting on the body, and aeolian tones off the train's fittings (handrails,
        ladder rungs, lamp brackets, wire) each at 0.2 x speed / diameter, so they slide up and down together with every
        gust. 12 s exact cycle.""", loop=True, takes=1, lufs=-24)
def wind_slow(rng, k):
    n = loop_cycle(12.0)
    y, _ = W.wind(n, rng, 8.0, gust=0.14, gust_rate=0.2, buffet=0.3, whistle=0.3, hiss=0.6)
    return seamless(y)


@recipe("bed-wind", "wind-fast", "rush",
        "Wind past you at speed: a hard rush and buffeting, the fittings whistling, your coat flapping",
        """The same model at 22 m/s: the rush louder and brighter, buffeting pummelling the body and the train's edges, the
        fittings' aeolian whistles higher and stronger (gliding with each gust), and loose cloth flapping in the
        peaks. 12 s exact cycle.""", loop=True, takes=1, lufs=-20)
def wind_fast(rng, k):
    n = loop_cycle(12.0)
    y, _ = W.wind(n, rng, 22.0, gust=0.13, gust_rate=0.25, buffet=0.7, whistle=0.35, flap=0.6)
    return seamless(W.cyclic(lambda z: dsp.compress(z, -14, 2.5, 0.02, 0.3), norm(y)))


@recipe("bed-wind", "gust", "swell",
        "A gust: the wind surging and falling away, the fittings' whistles sliding up and back down with it",
        """The wind model driven through one gust: speed swelling from 10 to 20-28 m/s and back over two to four seconds,
        so the rush brightens, the buffeting thumps, and every fitting's aeolian tone slides up and down with the speed
        (which is what makes a gust read as a gust). Four takes of different strength and shape.""", takes=4, lufs=-20)
def wind_gust(rng, k):
    L = (2.6, 3.4, 4.0, 3.0)[k]
    n = samples(L)
    peak = (20, 24, 28, 22)[k]
    at = L * (0.35, 0.5, 0.4, 0.3)[k]
    sp = env([(0, 10), (at, peak), (at + 0.25 * L, peak * 0.85), (L, 9)], L)
    y, _ = W.wind(n, rng, sp, gust=0.08, gust_rate=1.0, buffet=0.7, whistle=0.4, flap=0.3)
    return dsp.shaped(y, [(0, 0), (at * 0.6, 0.6), (at, 1), (L - 0.4, 0.5), (L, 0)])


# ---- Structural groan -----------------------------------------------------------------------------------------------------

def frame_body(rng, scale, iron=True):
    """A loaded car frame: wood sills (synth's board modes scaled to the size) and, for a groan, the iron truss rods."""
    wood = [f * scale * rng.uniform(0.93, 1.07) for f in synth.WOOD[:5]]
    return wood + ([f * scale for f in synth.IRON[:3]] if iron else [])


@recipe("bed-groan", "groan", "frames",
        "A long heavy train labouring up a grade: frames groaning under the pull in slow swells, timbers creaking",
        """Modelled stick-slip, which is what a groan is: the joints of loaded wooden frames and iron truss rods slipping
        twelve to forty times a second, rung through the frames' low modes, two or three groans overlapping and swelling
        with the pull. Over it single timber creaks and, now and then, the faint ting of a drawbar under tension; under it
        the low rumble of the weight. 12 s exact cycle.""", loop=True, takes=1, lufs=-22)
def groan(rng, k):
    n = loop_cycle(12.0)
    L = n / SR
    ev = []
    for i in range(5):
        gl = rng.uniform(2.5, 5.0)
        rate = env([(0, rng.uniform(10, 16)), (gl * 0.5, rng.uniform(25, 40)), (gl, rng.uniform(12, 18))], gl)
        g = synth.creak(gl, rate, rng, body=frame_body(rng, rng.uniform(0.32, 0.45)), q=22, jitter=0.35, grit=0.2)
        g = g * env([(0, 0), (gl * 0.35, 1), (gl * 0.7, 0.8), (gl, 0)], gl)
        ev.append((i * L / 5 + rng.uniform(0, 1.0), g, rng.uniform(0.5, 1.0)))
    for t in W.poisson(L, 0.6, rng):
        c = synth.creak(rng.uniform(0.2, 0.5), rng.uniform(20, 60), rng, body=frame_body(rng, rng.uniform(0.6, 0.9)), q=16)
        ev.append((t, c * dsp.fit(env([(0, 0), (0.05, 1), (0.5, 0)], 0.5), len(c)), rng.uniform(0.15, 0.35)))
    for t in W.poisson(L, 0.15, rng):
        ev.append((t, W.body(rng, rng.uniform(700, 1100), W.BAR, decay=0.08, contact=0.0004, count=3), 0.025))
    y = W.place(n, ev)
    rumble = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 90) ** 4) * (f / 25) / (1 + f / 25))
    y = norm(y) + norm(rumble) * 0.25 * np.clip(1 + 0.3 * slow(n, 0.3, rng), 0.2, None)
    y = croom(y, "car", 0.3, rng)
    return seamless(W.cyclic(lambda z: dsp.compress(z, -16, 2.5, 0.01, 0.25), norm(y)))


@recipe("bed-groan", "creak", "frame",
        "A single frame creak: a loaded wooden car frame slipping at a joint, low and woody, iron in some",
        """Modelled stick-slip through a car frame's modes (wood sills, a little iron truss rod): a run of slips that
        catches and speeds up and lets go, a third to one second long, each take a different joint (size, speed). Low and
        dry so it sits in the train's own body rather than sounding like a door.""",
        takes=6, lufs=-22)
def frame_creak(rng, k):
    L = rng.uniform(0.35, 1.0)
    rate = env([(0, rng.uniform(15, 30)), (L * rng.uniform(0.3, 0.6), rng.uniform(40, 90)), (L, rng.uniform(15, 30))], L)
    c = synth.creak(L, rate, rng, body=frame_body(rng, rng.uniform(0.6, 1.0), iron=False), q=rng.uniform(12, 20),
                    jitter=0.45, grit=0.35)
    c = c * env([(0, 0), (0.04, 1), (L * 0.7, 0.8), (L, 0)], L)
    return hp(dsp.room(c, "car", 0.25, rng=rng), 70)
