"""Train state and alarms (spec A.3 tier 3): the safety valve, the boiler straining and bursting, brake fade, derailment,
a car breached, the cannon fouling, engine damage. The crew must hear each of these and know what it is without UI.

Steam is the modelled jet (recipes/world_kit.py): its pressure and the size of its hole drive it, so a valve's lift, its
chatter as it reseats and a boiler emptying through a torn shell all move the way the real ones do; none of it starts
with a lone transient before the hiss (the earlier takes' bottle opening). Metal and wood under force are modelled
stick-slip and fracture rung through their bodies, laid over the packs' real impacts pitched for size. A derailment is
not one sound: each cue is one physical event the sim can fire (wheel climbing, car going over, hitting the ground,
grinding, settling, and the proposed collide, rail-scrape and tear), each a set of different takes.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, env, mix, fit
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter, croom
from recipes.world_bed import WHEEL, outdoors, circ_outdoors, rolling

SR = dsp.SR
SAFETY = dict(peak=1900, low=0.9, rasp=0.5, eddy=0.8)


def valve_howl(n, rng, amount=0.08):
    """The pop valve's whistle: a choked jet singing two unsteady tones through the valve's annulus."""
    return W.howl(n, rng, [1420 * rng.uniform(0.97, 1.03), 2870 * rng.uniform(0.97, 1.03)], amount, wander=0.02)


# ---- Safety valve ---------------------------------------------------------------------------------------------------------

@recipe("state-valve", "lift", "pop",
        "The safety valve lifting: a thin, spitting simmer, then the valve snaps open into a deafening roar",
        """The modelled steam jet, as a pop valve behaves: first the valve feathers, barely open and fluttering, a thin
        sizzling hiss that comes and goes; then it snaps to full lift and the roar arrives in a few hundredths of a
        second, the hole suddenly wide so the hiss drops into a huge broad roar with the choked jet's crackle and the
        valve's unsteady whistle in it. The second take lifts with almost no warning. Ends at full blow, fading so the
        held loop takes over.""", takes=2, lufs=-20)
def valve_lift(rng, k):
    sim = (1.1, 0.25)[k]
    L = sim + 2.6
    n = samples(L)
    flutter = np.clip(0.07 + 0.05 * slow(n, 9, rng), 0.01, None)
    o = env([(0, 1), (sim - 0.02, 1), (sim + 0.07, 0), (L, 0)], L)             # 1 while simmering
    opening = flutter * o + (1 - o) * env([(0, 0.15), (sim, 0.15), (sim + 0.07, 1.0), (L, 1.0)], L)
    opening *= env([(0, 0.0), (min(0.25, sim * 0.8), 1), (L, 1)], L)
    y = W.jet(L, rng, opening=opening, **SAFETY) + valve_howl(n, rng) * (1 - o)
    # the feathering itself: a high sizzle spitting on and off as the disc flutters on its seat
    fizz = W.jet(L, rng, peak=6500, low=0.0, eddy=1.3) * np.clip(flutter / 0.07 - 0.3, 0, 1.5) * o
    y += norm(fizz) * np.std(y[samples(sim + 0.2):]) * 0.9
    stop = W.body(rng, 160, W.BAR, decay=0.05, contact=0.003, length=0.3)          # the disc hitting its lift stop
    y = mix(y, np.concatenate([np.zeros(samples(sim + 0.02)), norm(stop) * np.std(y) * 1.5]))
    y = dsp.shaped(y, [(0, 1), (L - 0.8, 1), (L, 0)])
    return outdoors(y, rng, 0.12)


@recipe("state-valve", "blow", "pop",
        "The safety valve blowing: a deafening, tearing roar of steam straight up off the boiler, the valve whistling",
        """The modelled jet at full lift and full boiler pressure: louder and brighter than the vent (a choked jet, so its
        hump sits higher and it crackles with shocklets: the tearing edge), the plume's low billow heavy under it, and
        the valve's two unsteady whistle tones drifting in and out. Every band churns at its own eddy rate. 10 s exact
        cycle.""", loop=True, takes=1, lufs=-18)
def valve_blow(rng, k):
    n = samples(10.0)
    y = W.jet(10.0, rng, n=n, **SAFETY) + valve_howl(n, rng)
    return seamless(circ_outdoors(y, rng, 0.14))


@recipe("state-valve", "reseat", "chatter",
        "The valve reseating: the roar sagging, the valve chattering as it closes, a clunk, a last weeping hiss",
        """What a real safety valve does as the pressure drops back: the roar sags, then the disc starts to chatter on its
        seat (opening and closing twenty-odd times a second, a hard rattling roar), snaps shut with a heavy clunk, and a
        thin hiss weeps from the seat while the plume in the air dies away. All the steam is the modelled jet, its hole
        driven through those motions.""", takes=2, lufs=-23)
def valve_reseat(rng, k):
    L = 2.6
    n = samples(L)
    a, b = (0.45, 0.95) if k == 0 else (0.3, 0.6)          # chatter from a to b, shut at b
    t = np.arange(n) / SR
    rate = (22, 31)[k]
    chat = 0.2 + 0.18 * np.sign(np.sin(2 * np.pi * rate * t + 2.0 * slow(n, 6, rng)))
    o = np.where(t < a, np.interp(t, [0, a], [1.0, 0.45]), np.where(t < b, chat * np.interp(t, [a, b], [1.2, 0.5]), 0.0))
    o = lp(o.astype(np.float32), 300, 1)
    weep = np.where(t > b + 0.03, 0.035 * np.exp(-(t - b) / 0.5), 0.0).astype(np.float32)
    y = W.jet(L, rng, opening=np.clip(o, 0, None) + weep, pressure=env([(0, 1), (L, 0.85)], L), **SAFETY)
    y += valve_howl(n, rng) * np.clip(np.interp(t, [0, a, b], [1, 0.6, 0]), 0, 1)
    seat = mix(W.body(rng, 150, W.BAR, decay=0.06, contact=0.002, length=0.4), W.knock(rng, 90, 0.2) * 0.5)
    y = mix(y, np.concatenate([np.zeros(samples(b)), norm(seat) * np.std(y[:samples(a)]) * 2.5]))
    plume = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 250) ** 2) * (f / 35) / (1 + f / 35))
    y += norm(plume) * np.std(y[:samples(a)]) * 0.5 * np.interp(t, [0, b, L], [0.2, 0.8, 0])
    return outdoors(y, rng, 0.12)


# ---- Boiler straining -----------------------------------------------------------------------------------------------------

SHELL = [58, 96, 141, 197, 263, 352, 470]      # a locomotive boiler shell's low ring modes (1.6 m barrel, 16 mm plate)


@recipe("state-strain", "tick", "plate",
        "A boiler plate ticking under pressure: a dry tick or tock as the plate shifts, with the shell's faint bong",
        """The plates of an overpressured boiler shifting against their stays and seams: a short hard contact through a
        thick plate's modes (dry, quickly damped: 'tick', or lower: 'tock'), and the shell ringing faintly behind it.
        Six takes, from small ticks to a heavier shift; one is a double tick as the plate snaps over and back. Heard from
        the cab.""", takes=6, lufs=-30)
def strain_tick(rng, k):
    f1 = (1500, 1150, 900, 1800, 700, 1300)[k]
    tick = W.body(rng, f1 * rng.uniform(0.9, 1.1), W.PLATE, decay=rng.uniform(0.012, 0.03), contact=0.00012, length=0.2,
                  count=10)
    bong = W.modal([f * rng.uniform(0.97, 1.03) for f in SHELL[2:]], [0.35, 0.3, 0.25, 0.2, 0.15], [1, 0.8, 0.6, 0.4, 0.3],
                   0.8, rng, contact=0.002)
    y = mix(norm(tick), norm(bong) * (0.15 + 0.05 * k))
    if k == 2:
        y = mix(y, np.concatenate([np.zeros(samples(0.09)), norm(tick) * 0.6]))
    return dsp.room(y, "cab", 0.2, rng=rng)


@recipe("state-strain", "rivet", "ping",
        "A rivet pinging: a sharp bright ping as a rivet head shears, sometimes skittering off iron or starting a hiss",
        """A rivet's head letting go is a short steel shank ringing high (a little bar's modes, 3-7 kHz) struck by the
        snap of the shear, with the shell's knock behind it. Of four takes, one is a plain ping, one rings longer, one
        skitters off the cab roof after (the head flying), and one leaves a thin new jet of steam whistling from the
        hole, which is the worst news.""", takes=4, lufs=-28)
def strain_rivet(rng, k):
    L = 1.6
    ring = W.body(rng, rng.uniform(3100, 3900), W.BAR, decay=(0.25, 0.6, 0.3, 0.3)[k], contact=0.00008, length=1.2, damp=0.4)
    snap = W.body(rng, rng.uniform(800, 1100), W.PLATE, decay=0.02, contact=0.0001, length=0.15, count=10)
    y = mix(norm(ring) * 0.8, norm(snap) * 0.6, W.knock(rng, 120, 0.12) * 0.25)
    if k == 2:
        for i, dt in enumerate((0.22, 0.37, 0.46)):
            y = mix(y, np.concatenate([np.zeros(samples(dt)), norm(W.body(rng, rng.uniform(4000, 5500), W.BAR, decay=0.05,
                                                                              length=0.2)) * 0.35 / (i + 1)]))
    if k == 3:
        jet = W.jet(L, rng, opening=env([(0, 0), (0.05, 0.08), (L, 0.1)], L), peak=900, low=0.0, eddy=1.2)
        jet = jet + W.howl(samples(L), rng, [5200], 0.15, wander=0.01) * env([(0, 0), (0.1, 1), (L, 1)], L)
        y = mix(y, norm(jet) * 0.25 * env([(0, 0), (0.06, 1), (L - 0.3, 1), (L, 0)], L))
    return dsp.room(y, "cab", 0.2, rng=rng)


def boiler_groan(rng, n, intensity=1.0):
    """The shell flexing under overpressure: deep stick-slip groans where the plates work against their stays, slow
    swells of strain, seams weeping steam."""
    L = n / SR
    ev = []
    for i in range(6):
        gl = rng.uniform(1.5, 3.5)
        rate = env([(0, rng.uniform(8, 14)), (gl * 0.6, rng.uniform(18, 32)), (gl, rng.uniform(8, 12))], gl)
        g = synth.creak(gl, rate, rng, body=[f * rng.uniform(0.95, 1.05) for f in SHELL], q=30, jitter=0.3, grit=0.15)
        ev.append((i * L / 6 + rng.uniform(0, 0.8), g * env([(0, 0), (gl * 0.4, 1), (gl, 0)], gl), rng.uniform(0.6, 1.0)))
    y = norm(W.place(n, ev))
    weep = W.jet(L, rng, n=n, peak=3800, opening=0.2, low=0.0, eddy=1.4)
    weep = norm(weep) * np.clip(0.5 + 0.5 * slow(n, 0.4, rng), 0.05, None)
    hum = W.cyclic(lambda z: dsp.resonate(z, SHELL[:3], q=40), pnoise(n, rng))
    return y + weep * 0.04 * intensity + norm(hum) * 0.15


@recipe("state-strain", "groan", "shell",
        "The boiler groaning in the red: deep iron groans as the shell flexes, swelling, seams weeping steam",
        """Modelled stick-slip through a locomotive boiler shell's low modes (58-470 Hz): the plates working against
        their stays eight to thirty times a second in slow swells, which is what a pressure vessel past its limit
        sounds like, with seams weeping thin unsteady hisses and the shell's own hum under it. The preview ends the way
        the director asked, in the burst (state-rupture), since the code plays this until the boiler goes.
        12 s exact cycle.""", loop=True, takes=1, lufs=-25, preview=lambda takes, rng: _strain_to_burst(takes, rng))
def strain_groan(rng, k):
    n = samples(12.0)
    y = croom(boiler_groan(rng, n), "cab", 0.25, rng)
    return seamless(W.cyclic(lambda z: dsp.compress(z, -16, 2.5, 0.01, 0.3), norm(y)))


def _strain_to_burst(takes, rng):
    """The groan held with ticks and pings landing on it, getting louder, and the very end of it the burst."""
    g = np.concatenate([takes[0], takes[0]])
    L = len(g) / SR
    g = dsp.shaped(g, [(0, 0.4), (L, 1.0)])
    b = dsp.Bus(L + 5.5)
    b.at(0, g)
    for t in np.sort(rng.uniform(1, L - 0.5, 14)):
        b.at(t, norm(strain_tick(rng, int(rng.integers(6)))) * 0.25 * (0.4 + t / L), 0)
    for t in (L * 0.6, L * 0.85):
        b.at(t, norm(strain_rivet(rng, 0)) * 0.3)
    b.at(L - 0.05, norm(rupture_burst(np.random.default_rng(5), 0)) * 1.2)
    return b.x


# ---- Boiler rupture -------------------------------------------------------------------------------------------------------

@recipe("state-rupture", "burst", "shell",
        "Stand-in: the boiler bursting: a huge bang as the shell tears, iron ringing, the steam erupting in a roar",
        """The moment is a blast, not a pop: an explosion's pressure wave (a Friedlander pulse: instant rise, decaying
        overpressure, long suction after) with its deep body, the shell tearing (modelled fracture through iron) and
        ringing low and long as a torn sheet, the packs' heavy metal impacts pitched an octave down for its mass, and in
        the same instant the boiler's whole charge erupting through a torn hole as the modelled jet at its biggest
        (a low, enormous roar, flashing water seething in it) dying over four seconds as the boiler empties. Echoes come
        back off the land. Stand-in until a recorded explosion and big metal (Sonniss).""",
        sources=W.PIECES["iron"], takes=2, lufs=-24)
def rupture_burst(rng, k):
    L = 6.0
    b = dsp.Bus(L)
    bang = W.boom(rng, 2.0, f=(30, 38)[k], T=(0.025, 0.018)[k], crack=0.8)
    b.at(0, norm(bang) * 1.4)
    sh = W.sheet(rng, f1=38, fmax=3500, decay=1.6, contact=0.002, density=1.2)
    b.at(0.004, norm(sh) * 0.45)
    tr = W.tear(0.35, rng, env([(0, 900), (0.35, 120)], 0.35), (150, 4000))
    b.at(0.0, norm(tr) * env([(0, 1), (0.35, 0)], 0.35)[:len(tr)] * 0.5)
    for i in range(3):
        b.at(abs(rng.normal(0, 0.03)), norm(W.piece(rng, "iron", (-13, -8), tau=0.5)) * 0.5)
    # the steam erupts as the tear opens, just behind the bang: the bang leads, the roar follows it out
    p = env([(0, 0.001), (0.03, 0.15), (0.09, 1), (0.6, 0.75), (L, 0.02)], L, curve="exp")
    steam = W.jet(L, rng, pressure=p, opening=1.0, peak=520, low=1.6, rasp=0.6, eddy=1.2)
    seethe = synth.bubbles(L, 2500, 120, 600, rng) * env([(0, 0), (0.05, 1), (L, 0.05)], L, curve="exp")
    b.at(0.008, norm(steam) * 0.5)
    b.at(0.05, norm(lp(seethe, 1500)) * 0.12)
    if k == 1:
        # the firebox following the shell a moment later: a second, duller blow
        b.at(0.42, norm(W.boom(rng, 1.4, f=42, T=0.012, crack=0.3)) * 0.45)
    y = b.x
    for dt, g in ((0.38, 0.22), (0.71, 0.14), (1.25, 0.08)):         # echoes off cuttings and trees
        k_ = samples(dt)
        y[k_:] += lp(y[:-k_], 1200) * g
    return W.space(y, rng, "night", wet=0.3)


@recipe("state-rupture", "debris", "rain",
        "Debris coming down after the burst: iron plates and fittings clanging, firebrick and coal pattering, tinkling",
        """Real impacts of what a boiler is made of, falling back onto the train and the ground: the packs' heavy plate
        and metal hits for torn plates and fittings, Kenney's mining hits and the stone cracks for firebrick, light
        metal chinks for rivets and small fittings, a few wood hits for the cab. Dense at first and thinning out over
        four seconds, some pieces bouncing once.""",
        sources=W.PIECES["iron"] + W.PIECES["scrap"] + W.PIECES["brick"], takes=2, lufs=-24)
def rupture_debris(rng, k):
    y = W.debris(rng, 4.5, 14, {"iron": 3, "scrap": 4, "brick": 3, "wood": 1}, decay=1.4, semis=(-7, 1))
    return W.space(y, rng, "night", wet=0.22)


@recipe("state-rupture", "steam-out", "wreck",
        "Steam pouring from the wreck of the boiler: a ragged low roar surging, water boiling and spitting in it",
        """The modelled jet through a torn, ragged opening at falling pressure: lower and rougher than any valve (a big
        hole sings low), its turbulence exaggerated, surging slowly as water flashing to steam keeps the pressure up in
        gulps, with the boiling seethe and spits of water in it. 10 s exact cycle.""", loop=True, takes=1, lufs=-20)
def rupture_steam(rng, k):
    n = samples(10.0)
    surge = np.clip(0.75 + 0.25 * slow(n, 0.35, rng), 0.3, None)
    y = W.jet(10.0, rng, n=n, pressure=surge, peak=620, low=1.3, rasp=0.25, eddy=1.3)
    seethe = W.cyclic(lambda z: lp(z, 1500), synth.bubbles(10.0, 1500, 120, 700, rng))
    spits = [(t, W.sputter(0.15, rng, rate=40), 1.0) for t in W.poisson(10.0, 1.2, rng)]
    y = y + norm(seethe) * np.std(y) * 0.6 + W.place(n, spits) * np.std(y) * 1.5
    return seamless(circ_outdoors(y, rng, 0.18))


# ---- Brake fade -----------------------------------------------------------------------------------------------------------

@recipe("state-brake-fade", "fade", "glazed",
        "Hot brakes glazing: the bite going thin and glassy, a wavering whine, the blocks grabbing and slipping",
        """The brake friction model (a car wheel's modes rung by rubbing) as the blocks glaze: the grind loses its
        coarse low bite and goes thin, a glassy whine rises out of it and wavers as the contact jumps between stick and
        slip, and the blocks grab and let go in irregular judders (the brakes catching and failing to hold). The shape
        a crew can hear coming: grip turning into a slide. 10 s exact cycle.""", loop=True, takes=1, lufs=-20)
def brake_fade(rng, k):
    n = samples(10.0)
    g = W.friction(n, rng, WHEEL, rough=0.5, grit=1.0, q=30, loop=True)
    g = cfilter(g, lambda f: (f / 600) ** 2 / (1 + (f / 600) ** 2) / (1 + (f / 9000) ** 2))
    # grab and slip: short surges of bite at uneven intervals
    grabs = W.place(n, [(tt, env([(0, 0), (0.04, 1), (rng.uniform(0.15, 0.4), 0)], 0.4), 1.0) for tt in W.poisson(10.0, 1.3, rng)])
    bite = W.friction(n, rng, WHEEL[:4], rough=1.6, grit=0.3, q=12, loop=True)
    whine = W.squeal(n, rng, [1240, 1690], np.clip(0.75 + 0.4 * slow(n, 0.3, rng), 0, 1), wander=0.02, rate=5.0, harm=0.5)
    sizzle = synth.crackle(10.0, 300, rng, size=(0.0001, 0.0006), hi=3000)
    y = norm(g) * 0.3 + norm(bite) * np.clip(grabs, 0, 1) * 0.6 + whine * 0.32 + norm(W.cyclic(lambda z: z, sizzle)) * 0.04
    return seamless(circ_outdoors(y, rng, 0.1))


# ---- Derailment -----------------------------------------------------------------------------------------------------------

@recipe("state-derail", "flange-scream", "curve",
        "Flanges screaming on a curve taken too fast: several wheels shrieking at once, grinding, the trucks hammering",
        """The warning, made the way it happens: every wheel on the curve squealing at once (wheel modes at 2.1, 3.4 and
        5.2 kHz locked into tones that shiver, beat against each other and never let go), the flanges grinding hard on
        the rail heads (rubbing through the wheel's modes), and the trucks hunting against the rails in heavy knocks.
        Continuous and alarming, unlike the bed's flange squeal that comes and goes. 8 s exact cycle.""",
        sources=W.PIECES["iron"][:5], loop=True, takes=1, lufs=-20)
def derail_scream(rng, k):
    n = samples(8.0)
    tones = [2110, 2160, 3380, 5240]
    sq = sum(W.squeal(n, rng, [f], np.clip(0.8 + 0.3 * slow(n, 1.5, rng), 0.3, 1), wander=0.012, rate=6, harm=0.35)
             * (1.0, 0.7, 0.8, 0.5)[i] for i, f in enumerate(tones))
    grind = W.friction(n, rng, WHEEL, rough=2.0, grit=1.0, q=18, loop=True)
    hunts = [(t, norm(W.piece(rng, "iron", (-9, -5), tau=0.12)), rng.uniform(0.3, 0.6)) for t in W.poisson(8.0, 2.2, rng)]
    y = sq * 0.5 + norm(grind) * 0.3 + norm(W.place(n, hunts)) * 0.35
    return seamless(circ_outdoors(y, rng, 0.12))


@recipe("state-derail", "climb", "flange",
        "A wheel climbing the rail: a rising screech and scrape as the flange rides up, then the drop onto the sleepers",
        """One wheel lifting over the rail head: the flange's squeal climbing in pitch and the grind getting harsher as it
        rides up (modelled), a heavy iron knock as it drops off the far side, then the wheel running on the sleepers, a
        drumroll of wooden blows twenty-odd a second (the packs' heavy wood impacts) with ballast flying. The two takes
        differ in how long it rides the rail and how hard it drops.""", sources=W.PIECES["wood"] + W.PIECES["iron"],
        takes=2, lufs=-20)
def derail_climb(rng, k):
    ride = (0.8, 1.4)[k]
    L = ride + 2.2
    n = samples(L)
    t = np.arange(n) / SR
    f = np.interp(t, [0, ride], [2100, 2600 + 300 * k]).astype(np.float32)
    on = np.interp(t, [0, 0.1, ride, ride + 0.05], [0, 1, 1, 0]).astype(np.float32)
    ph = 2 * np.pi * np.cumsum(f * (1 + 0.01 * slow(n, 8, rng))) / SR
    sq = (np.sin(ph) + 0.4 * np.sin(2 * ph)) * on
    grind = W.friction(n, rng, WHEEL, rough=2.0, grit=1.0, q=16) * np.interp(t, [0, ride, ride + 0.1], [0.3, 1, 0])
    b = dsp.Bus(L + 1)
    b.at(0, norm(sq) * 0.4 + norm(grind) * 0.35)
    b.at(ride, norm(W.piece(rng, "iron", (-9, -6), tau=0.3)) * 0.9)
    b.at(ride, W.knock(rng, 55, 0.4) * 0.8)
    sl = 1 / 22.0
    tt = ride + 0.08
    while tt < L - 0.3:
        g = np.interp(tt, [ride, L], [0.8, 0.4])
        b.at(tt + rng.uniform(-0.004, 0.004), norm(W.piece(rng, "wood", (-6, -2))) * g * rng.uniform(0.6, 1.0))
        tt += sl * rng.uniform(0.9, 1.1)
    b.at(ride + 0.05, norm(W.spray(rng, 1.2, 80)) * 0.3)
    return outdoors(b.x, rng, 0.15)


@recipe("state-derail", "tip", "car",
        "A car going over: its frame groaning and splitting as it leans, the load inside sliding and slamming",
        """The moment before the impact: a loaded wooden car leaning past its balance. Modelled stick-slip groans through the
        car's frame (wood and iron) rising as the load comes on them, boards splitting under it, the cargo inside
        sliding across the floor and slamming into the low wall (the packs' heavy wood and plank hits), a truss rod or
        chain snapping. The two takes go over at different speeds.""", sources=W.PIECES["wood"], takes=2, lufs=-20)
def derail_tip(rng, k):
    L = (2.2, 1.5)[k]
    b = dsp.Bus(L + 1.5)
    gr = synth.creak(L, env([(0, 10), (L * 0.8, 45), (L, 70)], L), rng,
                     body=[f * 0.4 for f in synth.WOOD[:5]] + [f * 0.7 for f in synth.IRON[:3]], q=20, jitter=0.4)
    b.at(0, norm(gr) * env([(0, 0.2), (L * 0.8, 1), (L, 0.6)], L)[:len(gr)] * 0.8)
    sp = W.splinter(L * 0.5, rng, env([(0, 20), (L * 0.5, 250)], L * 0.5))
    b.at(L * 0.45, norm(sp) * 0.45)
    slide = W.friction(samples(0.6), rng, synth.WOOD[:4], rough=1.5, grit=0.4, q=8)
    b.at(L * 0.55, norm(slide) * env([(0, 0), (0.1, 1), (0.6, 0)], 0.6)[:len(slide)] * 0.35)
    for i in range(3):
        b.at(L * rng.uniform(0.7, 0.98), norm(W.piece(rng, "wood", (-7, -3))) * rng.uniform(0.5, 0.9))
    b.at(L * 0.85, norm(W.body(rng, rng.uniform(500, 700), W.BAR, decay=0.3, contact=0.0002)) * 0.3)      # a rod snapping
    return outdoors(b.x, rng, 0.15)


@recipe("state-derail", "impact", "ground", "Stand-in: a car hitting the ground: three different crashes of wood, iron and ballast",
        """Three physically different landings, so each derailment is its own: a car slammed down on its side into the
        ballast (body boom, boards splintering, stones thrown), one digging in at an end (the iron frame ploughing and
        crumpling for longer), one landing on its roof (the tin crushing and clattering, then the frame). Each is the
        weight's deep blow, the car's sheet iron ringing low (modelled), tearing, real slams and wood and tin hits from
        the packs pitched for a car's mass, and the ground. Stand-in until recorded big metal crashes (Sonniss).""",
        sources=W.PIECES["iron"] + W.PIECES["wood"] + W.PIECES["tin"] + W.PIECES["brick"] + W.PIECES["scrap"]
        + ["sfx_100_v2:misc_34"], mat="ground", takes=3, lufs=-22)
def derail_impact(rng, k):
    b = dsp.Bus(6.0)
    if k == 0:          # slammed down on its side: one huge blow, boards bursting, ballast thrown
        b.at(0, W.crash(rng, 1.2, iron=0.7, wood=1.0, ground=1.0, crumple=0.12))
        b.at(0.05, W.debris(rng, 2.0, 10, {"wood": 3, "scrap": 1, "brick": 2}, decay=0.5, semis=(-5, 1)) * 0.5)
    elif k == 1:        # dug in at one end: the frame ploughs and crumples, a long crunching stop
        b.at(0, W.crash(rng, 1.4, iron=1.0, wood=0.5, ground=0.6, crumple=0.5))
        n = samples(1.2)
        plough = W.pour(n, rng, env([(0, 1500), (1.2, 100)], 1.2), grain=(800, 4500), lump=8, thunder=1.0, loop=False)
        b.at(0.1, norm(plough) * env([(0, 1), (1.2, 0)], 1.2)[:n] * 0.6)
        b.at(0.9, W.crash(rng, 0.8, iron=0.6, wood=0.3, ground=0.3, crumple=0.08) * 0.5)
    else:               # on its roof: the tin crushing first, then the frame coming down on it
        for i in range(10):
            b.at(abs(rng.normal(0.05, 0.05)), norm(W.piece(rng, "tin", (-7, -1))) * rng.uniform(0.5, 1.0))
        b.at(0.0, norm(W.tear(0.3, rng, env([(0, 900), (0.3, 200)], 0.3), (500, 6000))) * 0.5)
        b.at(0.16, W.crash(rng, 1.1, iron=0.8, wood=0.7, tin=0.6, ground=0.4, crumple=0.2))
        b.at(0.3, W.debris(rng, 1.8, 10, {"tin": 2, "scrap": 1, "wood": 2}, decay=0.6, semis=(-4, 2)) * 0.45)
    return W.space(b.x, rng, "night", wet=0.2)


@recipe("state-derail", "grind", "ballast",
        "A wreck grinding along the ballast: iron dragging over stones, crunching and thumping, a screech off a rail",
        """A car on its side ploughing the track bed: rough iron-on-stone friction (modelled rubbing through the car's
        sheet), a continuous crunch of ballast (thousands of stone impacts from the pour model, with the packs' real
        stone cracks thrown in), heavy irregular thumps as it rides over sleepers, and now and then the iron screeching
        across a rail head. 8 s exact cycle.""", sources=W.PIECES["brick"] + W.PIECES["wood"], loop=True, takes=1,
        lufs=-20)
def derail_grind(rng, k):
    n = samples(8.0)
    drag = W.friction(n, rng, [70, 133, 210, 340, 520, 890, 1400], rough=2.5, grit=0.6, q=10, loop=True)
    crunch = W.pour(n, rng, np.clip(900 + 500 * slow(n, 1.0, rng), 200, None), grain=(1000, 5000), lump=4, thunder=0.8)
    thumps = [(t, norm(mix(W.piece(rng, "wood", (-8, -4)), W.knock(rng, 50, 0.25))), rng.uniform(0.4, 0.9))
              for t in W.poisson(8.0, 3.0, rng)]
    stones = [(t, norm(W.piece(rng, "brick", (-2, 3))), rng.uniform(0.15, 0.4)) for t in W.poisson(8.0, 5.0, rng)]
    on = np.clip(2.0 * slow(n, 0.5, rng) - 1.2, 0, 1)
    screech = W.squeal(n, rng, [1650, 2480], on, wander=0.02, rate=4, harm=0.5)
    y = (norm(drag) * 0.55 + norm(crunch) * 0.5 + norm(W.place(n, thumps)) * 0.6 + norm(W.place(n, stones)) * 0.3
         + screech * 0.2)
    return seamless(circ_outdoors(y, rng, 0.15))


@recipe("state-derail", "settle", "wreck",
        "Wreckage settling: four different moments of a wreck coming to rest, creaks, slides and late falls",
        """After the crash, things the physics would still do: a bent car frame creaking and dropping an inch with a clunk;
        iron groaning as the weight shifts, then a sheet slumping; loose ballast and coal trickling down the heap; a
        hanging piece finally tearing free and clanging down. Modelled creak and tear with the packs' real impacts.""",
        sources=W.PIECES["iron"] + W.PIECES["brick"] + W.PIECES["scrap"] + W.PIECES["wood"], takes=4, lufs=-24)
def derail_settle(rng, k):
    b = dsp.Bus(4.0)
    if k == 0:
        c = synth.creak(1.2, env([(0, 15), (0.9, 40), (1.2, 20)], 1.2), rng, body=[f * 0.5 for f in synth.WOOD[:5]], q=18)
        b.at(0, norm(c) * env([(0, 0), (0.2, 1), (1.2, 0.3)], 1.2)[:len(c)] * 0.6)
        b.at(1.15, norm(W.piece(rng, "iron", (-8, -5), tau=0.3)) * 0.8)
        b.at(1.15, W.knock(rng, 60, 0.3) * 0.5)
    elif k == 1:
        g = synth.creak(1.6, env([(0, 8), (1.6, 25)], 1.6), rng, body=[f * 0.6 for f in synth.IRON], q=35)
        b.at(0, norm(g) * env([(0, 0), (0.5, 1), (1.6, 0.5)], 1.6)[:len(g)] * 0.6)
        b.at(1.55, norm(W.sheet(rng, f1=55, fmax=3000, decay=0.8, contact=0.004)) * 0.6)
        b.at(1.55, norm(W.tear(0.3, rng, env([(0, 300), (0.3, 30)], 0.3), (150, 3000))) * 0.25)
    elif k == 2:
        tr = W.pour(samples(2.2), rng, env([(0, 50), (0.4, 300), (2.2, 10)], 2.2), grain=(1500, 6000), lump=2.5, thunder=0.2,
                    loop=False)
        b.at(0, norm(tr) * 0.6)
        for t in np.sort(rng.uniform(0.1, 1.8, 5)):
            b.at(t, norm(W.piece(rng, "brick", (-4, 2))) * rng.uniform(0.2, 0.5))
    else:
        b.at(0, norm(W.tear(0.5, rng, env([(0, 20), (0.45, 200), (0.5, 400)], 0.5), (300, 4000))) * 0.4)
        b.at(0.55, norm(W.piece(rng, "iron", (-6, -2), tau=0.6)) * 0.9)
        b.at(0.8, norm(W.piece(rng, "scrap", (-5, 0))) * 0.4)
        b.at(0.95, norm(W.piece(rng, "scrap", (-5, 0))) * 0.25)
    return W.space(b.x, rng, "night", wet=0.25)


# Proposed cues (not in cues.py yet; see the report): what the director asked for that the six cues don't cover.

@recipe("state-derail", "collide", "cars",
        "Stand-in (proposed cue): one car slamming into another: iron frames crashing and crumpling, couplers buckling",
        """Proposed cue 'collide' (a car running into the car ahead as the train piles up). Iron on iron rather than iron
        on ground: the crash model with heavy crumpling (the end frames buckling into each other for a third of a
        second), heavier iron and less wood, the coupler yanked out of its pocket (a deep slam), no ballast. Four takes
        of different weight. Stand-in until recorded big metal crashes (Sonniss).""",
        sources=W.PIECES["iron"] + W.PIECES["wood"], takes=4, lufs=-22)
def derail_collide(rng, k):
    y = W.crash(rng, (1.0, 1.3, 0.9, 1.5)[k], iron=1.0, wood=(0.3, 0.5, 0.2, 0.6)[k], ground=0.0,
                crumple=(0.3, 0.4, 0.2, 0.5)[k])
    slam = W.rec("sfx_100_v2:metal_hit_01", semis=-rng.uniform(9, 12))
    y = mix(y, norm(slam) * 0.6)
    return W.space(y, rng, "night", wet=0.2)


@recipe("state-derail", "rail-scrape", "steel",
        "Proposed cue: steel dragged along a rail head: a long harsh screech and grind, juddering",
        """Proposed loop 'rail-scrape' (a car body or a truck sliding along a rail, apart from the grind through ballast).
        Steel on steel: modelled rubbing through a rail's ring and the car's sheet, a shrill unsteady screech coming and
        going as the contact skips, and a judder as it jumps the rail joints. 8 s exact cycle.""", loop=True, takes=1,
        lufs=-20)
def derail_rail_scrape(rng, k):
    n = samples(8.0)
    rail = [f * 430 for f in W.BAR] + [700, 1150, 1900]
    g = W.friction(n, rng, rail, rough=2.2, grit=1.2, q=22, loop=True)
    on = np.clip(1.2 * slow(n, 1.2, rng) + 0.4, 0, 1)
    sq = W.squeal(n, rng, [2950, 4400], on, wander=0.03, rate=7, harm=0.4)
    jud = W.place(n, [(t, norm(W.piece(rng, "iron", (-6, -2), tau=0.08)), 0.5) for t in np.arange(0.4, 8.0, 0.8)])
    y = norm(g) * 0.55 + sq * 0.3 + norm(jud) * 0.35
    return seamless(circ_outdoors(y, rng, 0.12))


@recipe("state-derail", "tear", "body",
        "Proposed cue: metal and wood ripping apart under force: a car body tearing open, iron shrieking, boards bursting",
        """Proposed cue 'tear' (a car's body or frame being torn apart by the forces in a wreck). Modelled fracture: iron
        yielding in stick-slip that speeds up into a tearing shriek, rivets letting go in a rattling run, boards
        splintering and bursting with the packs' real wood cracks and breaks. Four takes: mostly iron, mostly wood, a
        roof peeling off, a frame wrenched apart.""", sources=["sfx_100_v2:misc_35", "sfx_100_v2:misc_34"] + W.PIECES["wood"],
        takes=4, lufs=-22)
def derail_tear(rng, k):
    L = (1.2, 0.9, 1.6, 1.4)[k]
    b = dsp.Bus(L + 1.5)
    iron = (1.0, 0.3, 0.6, 1.0)[k]
    wood = (0.3, 1.0, 0.4, 0.7)[k]
    tr = W.tear(L, rng, env([(0, 30), (L * 0.7, 500), (L, 1200)], L), (110, 4500), q=18)
    b.at(0, norm(tr) * env([(0, 0.2), (L * 0.8, 1), (L, 0)], L)[:len(tr)] * 0.6 * iron)
    rivets = [(t, W.body(rng, rng.uniform(2500, 4000), W.BAR, decay=0.06, contact=0.0001, length=0.2), 1.0)
              for t in np.sort(rng.uniform(L * 0.4, L, 7))]
    b.at(0, norm(W.place(samples(L + 0.3), rivets, loop=False)) * 0.25 * iron)
    sp = W.splinter(L, rng, env([(0, 15), (L * 0.8, 300), (L, 600)], L))
    b.at(0, norm(sp) * env([(0, 0.1), (L * 0.8, 1), (L, 0)], L)[:len(sp)] * 0.5 * wood)
    b.at(L * 0.75, norm(W.rec(("sfx_100_v2:misc_35", "sfx_100_v2:misc_34")[k % 2], semis=-rng.uniform(2, 6))) * 0.6 * wood)
    if k == 2:
        for t in np.sort(rng.uniform(0.2, L, 6)):
            b.at(t, norm(W.piece(rng, "tin", (-6, -2))) * 0.35)
    b.at(L, norm(W.piece(rng, "iron", (-8, -4), tau=0.4)) * 0.6 * iron)
    return W.space(b.x, rng, "night", wet=0.2)


# ---- Car breached ---------------------------------------------------------------------------------------------------------

@recipe("state-breach", "breach", "shell",
        "A car's shell giving way: iron wrenched and boards splintering, loud enough to carry two cars",
        """Three ways a car's shell goes: a side door forced (its iron latch and hinges wrenched out in a groaning tear,
        the boards round it splitting, the door slamming back), a roof hatch torn off (sheet iron tearing, rivets pinging,
        the hatch clattering across the roof tin), an end wall chewed through (boards splintering one after another, a
        plank cracking out). Modelled iron stick-slip and fracture rung through the car's sheet, with real wood cracks,
        breaks and slams from the packs.""",
        sources=["sfx_100_v2:misc_35", "sfx_100_v2:misc_34", "sfx_100_v2:door_03", "kenney_rpg-audio:chop"]
        + W.PIECES["wood"] + W.PIECES["tin"], takes=3, lufs=-25)
def breach(rng, k):
    b = dsp.Bus(4.0)
    if k == 0:          # door forced
        gr = W.tear(0.9, rng, env([(0, 15), (0.7, 120), (0.9, 700)], 0.9), (130, 3000), q=20, crack=0.3)
        b.at(0, norm(gr) * env([(0, 0.2), (0.8, 1), (0.9, 0.3)], 0.9)[:len(gr)] * 0.6)
        b.at(0.85, norm(W.splinter(0.35, rng, env([(0, 300), (0.35, 50)], 0.35))) * 0.6)
        b.at(0.88, norm(W.rec("sfx_100_v2:misc_35", semis=-3)) * 0.6)
        b.at(0.9, norm(W.rec("sfx_100_v2:door_03", semis=-2)) * 0.9)
        b.at(0.9, W.knock(rng, 70, 0.3) * 0.5)
        b.at(1.15, norm(W.piece(rng, "scrap", (-4, 0))) * 0.3)
    elif k == 1:        # hatch torn off
        tr = W.tear(0.7, rng, env([(0, 40), (0.6, 600), (0.7, 900)], 0.7), (300, 5000), q=16)
        b.at(0, norm(tr) * env([(0, 0.3), (0.65, 1), (0.7, 0)], 0.7)[:len(tr)] * 0.6)
        for t in np.sort(rng.uniform(0.3, 0.7, 4)):
            b.at(t, norm(W.body(rng, rng.uniform(2800, 3800), W.BAR, decay=0.15, contact=0.0001, length=0.4)) * 0.2)
        for i, t in enumerate((0.85, 1.05, 1.2, 1.32)):
            b.at(t, norm(W.piece(rng, "tin", (-8, -4))) * (0.9, 0.6, 0.4, 0.25)[i])
        b.at(0.85, W.knock(rng, 110, 0.15) * 0.4)
    else:               # end wall chewed through
        for i, t in enumerate((0.0, 0.35, 0.6, 0.95)):
            sp = W.splinter(0.3, rng, env([(0, 60), (0.25, 400), (0.3, 100)], 0.3))
            b.at(t, norm(sp) * (0.5 + 0.15 * i))
            b.at(t + 0.2, norm(W.rec("kenney_rpg-audio:chop", semis=-rng.uniform(3, 6))) * 0.4)
        b.at(1.1, norm(W.rec("sfx_100_v2:misc_34", semis=-3)) * 0.7)
        b.at(1.15, norm(W.piece(rng, "wood", (-5, -2))) * 0.8)
    return hp(dsp.room(b.x, "car", 0.25, rng=rng), 25)


@recipe("state-breach", "open-to-outside", "hole",
        "Inside a breached car: the wind tearing past the hole and throbbing in the car, the wheels loud and unmuffled",
        """The state, heard from inside: the outside no longer shut out. The wheels and rail come in bright and close (the
        real train rumble, unmuffled, with the rail's roar), the wind rushes past the hole at train speed (the wind
        model: rush, fitting whistles, an edge tone off the torn opening), the whole car throbs as the air over the
        hole pumps it like a bottle (a Helmholtz buffeting at about 7 Hz), and the broken boards at the edge rattle and
        flap. 10 s exact cycle.""", sources=["sfx_100_v2:loop_ambient_04"], loop=True, takes=1, lufs=-20)
def breach_open(rng, k):
    n = samples(10.0)
    t = np.arange(n) / SR
    roll = rolling(rng, n, 15.0, 0)
    wind, U = W.wind(n, rng, 15.0, gust=0.15, gust_rate=0.3, buffet=0.8, whistle=0.4, flap=0.7)
    cyc = round(7.0 * 10.0)
    throb = 1 + 0.35 * np.sin(2 * np.pi * cyc * t / 10.0 + 0.4 * slow(n, 0.5, rng))
    boom = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(70)) / 0.6) ** 2))
    rattle = W.place(n, [(tt, norm(W.piece(rng, "wood", (-2, 4))), rng.uniform(0.1, 0.4)) for tt in W.poisson(10.0, 6, rng)])
    y = norm(roll) * 0.7 + norm(wind) * 0.6 * throb + norm(boom) * 0.4 * throb + norm(rattle) * 0.15
    return seamless(croom(y, "car", 0.2, rng))


# ---- Cannon fouls ---------------------------------------------------------------------------------------------------------

BORE = [47, 141, 235, 329, 423, 517]      # a 1.8 m bore, closed at the breech: odd quarter-wave resonances
BARREL = [380, 1040, 1960, 3150]          # the iron barrel's ring when struck


@recipe("state-cannon-foul", "misfire", "lock",
        "The gun failing to fire: the lock snaps down on the vent with a hard dead click, and nothing",
        """The firing lock's hammer falling onto the vent of an iron gun with no shot behind it: a real metal click from
        the packs for the snap, the heavy barrel ringing faintly where it was struck, and silence where the boom
        should be. One take snaps twice (tried again), one has the faint dull puff of priming that flashed and died
        (no fizz).""", sources=["kenney_rpg-audio:metalClick", "kenney_rpg-audio:metalLatch"], takes=3, lufs=-30)
def misfire(rng, k):
    click = W.rec("kenney_rpg-audio:metalClick", semis=-rng.uniform(1, 3), tau=0.06)
    barrel = W.modal([f * rng.uniform(0.97, 1.03) for f in BARREL], [0.1, 0.07, 0.05, 0.03], [0.6, 1, 0.6, 0.3], 0.5, rng,
                     contact=0.0002)
    hit = mix(norm(click), norm(barrel) * 0.15)
    if k == 1:          # the lock re-cocked and snapped again
        cock = norm(W.rec("kenney_rpg-audio:metalLatch", semis=-3, tau=0.05)) * 0.4
        b = dsp.Bus(2.0)
        b.at(0, hit).at(0.55, cock).at(0.95, hit * 0.9)
        hit = b.x
    if k == 2:          # the priming flashed and died: a dull puff, no fizz
        n = samples(0.4)
        puff = lp(rng.standard_normal(n).astype(np.float32), 700) * env([(0, 0), (0.01, 1), (0.4, 0)], 0.4, curve="exp")
        hit = mix(hit, np.concatenate([np.zeros(samples(0.03)), norm(puff) * 0.25]))
    return outdoors(hit, rng, 0.1)


@recipe("state-cannon-foul", "clear", "worm",
        "Clearing the bore by hand: a worm screwed in and dragged out, iron scraping hollow inside the barrel",
        """A worm (a corkscrew on a pole) worked down the bore to pull the fouled charge: each stroke is iron scraping
        on iron inside a tube (modelled rubbing rung through the bore's quarter-wave resonances, so it's hollow), a
        twist that grinds and catches, and the head knocking the barrel on the way out (real metal hits pitched for an
        iron gun). Five uneven strokes in an exact 8 s cycle.""",
        sources=[f"kenney_impact-sounds:impactMetal_medium_00{i}" for i in range(5)], loop=True, takes=1, lufs=-22)
def clear_bore(rng, k):
    n = samples(8.0)
    ev = []
    t0 = 0.0
    for i in range(5):
        L = rng.uniform(1.3, 1.7)
        m = samples(L)
        scr = W.friction(m, rng, BORE + [700, 1100], rough=1.8, grit=0.4, q=12)
        e = env([(0, 0), (0.08, 1), (0.45, 0.8), (0.5, 0.2), (0.75, 1.0), (0.8, 0.3), (1.05, 1), (L - 0.1, 0.8), (L, 0)], L)
        ev.append((t0, scr * fit(e, m), 0.6))
        ev.append((t0 + 0.47, norm(synth.creak(0.3, 120, rng, body=BARREL, q=20)), 0.2))      # the twist catching
        ev.append((t0 + L - 0.15, norm(W.rec(f"kenney_impact-sounds:impactMetal_medium_00{i}", semis=-6, tau=0.12)), 0.5))
        t0 += 8.0 / 5
    y = W.place(n, ev)
    return seamless(circ_outdoors(y, rng, 0.1))


@recipe("state-cannon-foul", "cleared", "out",
        "Cleared: the fouled charge drawn out and dropped, the rammer knocked twice on the muzzle",
        """The worm comes out of the muzzle with a last hollow scrape, the fouled wad and powder bag drop on the deck (a
        soft, crumbly thud: the packs' soft and leather hits), and the gunner knocks the rammer on the muzzle twice, the
        crew's 'clear' (real metal hits rung through the barrel's modes). Three takes.""",
        sources=["kenney_rpg-audio:dropLeather"] + [f"kenney_impact-sounds:impactSoft_heavy_00{i}" for i in range(3)]
        + [f"kenney_impact-sounds:impactMetal_heavy_00{i}" for i in range(3)], takes=3, lufs=-24)
def cleared(rng, k):
    b = dsp.Bus(2.5)
    m = samples(0.5)
    scr = W.friction(m, rng, BORE + [700], rough=1.8, grit=0.3, q=12) * env([(0, 0), (0.05, 1), (0.5, 0)], 0.5)[:m]
    b.at(0, norm(scr) * 0.4)
    b.at(0.55, norm(mix(W.rec("kenney_rpg-audio:dropLeather", semis=-3), W.rec(f"kenney_impact-sounds:impactSoft_heavy_00{k}")))
         * 0.6)
    for i, t in enumerate((1.0, 1.28)):
        h = W.rec(f"kenney_impact-sounds:impactMetal_heavy_00{(k + i) % 3}", semis=-5, tau=0.2)
        ring = W.modal(BARREL, [0.3, 0.2, 0.12, 0.08], [0.6, 1, 0.5, 0.3], 0.8, rng, contact=0.0003)
        b.at(t + 0.03 * k, norm(mix(norm(h), norm(ring) * 0.3)) * (0.8, 0.65)[i])
    return outdoors(b.x, rng, 0.1)


# ---- Engine damage --------------------------------------------------------------------------------------------------------

@recipe("state-engine-damage", "leak-small", "gland",
        "A small steam leak: a thin, high jet hissing from a gland, pulsing and spitting, now and then whistling",
        """The modelled jet through a small hole (so its hump sits high, near 4 kHz, with little roar), its pressure
        pulsing unevenly as the gland packing lets go and catches, a few spits of water, and a faint whistle when the
        jet lines up with the gap. 10 s exact cycle.""", loop=True, takes=1, lufs=-24)
def leak_small(rng, k):
    n = samples(10.0)
    pulse = np.clip(0.8 + 0.18 * slow(n, 1.8, rng) + 0.08 * slow(n, 8, rng), 0.45, None)
    y = W.jet(10.0, rng, n=n, pressure=pulse, peak=3600, low=0.25, eddy=1.1)
    y = y + W.howl(n, rng, [5600], 0.05, wander=0.01, rate=0.3) * np.clip(slow(n, 0.2, rng), 0, None)
    spits = [(t, W.sputter(0.08, rng, rate=60, size=0.6), 1.0) for t in W.poisson(10.0, 0.5, rng)]
    y = y + W.place(n, spits) * np.std(y) * 0.8
    return seamless(circ_outdoors(y, rng, 0.1))


@recipe("state-engine-damage", "leak-large", "pipe",
        "A big steam leak: a burst pipe roaring, ragged and surging, water spitting in it",
        """The modelled jet through a split steam pipe: a wide hole, so a low heavy roar with the hiss over it, choked
        enough to crackle, surging slowly with the boiler's pressure and spitting water. Louder and lower than the small
        leak, quieter and steadier than the boiler's wreck. 10 s exact cycle.""", loop=True, takes=1, lufs=-20)
def leak_large(rng, k):
    n = samples(10.0)
    surge = np.clip(0.85 + 0.15 * slow(n, 0.5, rng), 0.4, None)
    y = W.jet(10.0, rng, n=n, pressure=surge, peak=950, low=1.1, rasp=0.3, eddy=1.0)
    spits = [(t, W.sputter(0.12, rng, rate=50), 1.0) for t in W.poisson(10.0, 1.0, rng)]
    y = y + W.place(n, spits) * np.std(y) * 1.0
    return seamless(circ_outdoors(y, rng, 0.12))


@recipe("state-engine-damage", "knock", "big-end",
        "Damaged machinery knocking: a loose big end clanking twice a turn, a dry bearing scraping, the motion rattling",
        """A worn big-end bearing with play in it: a heavy knock where the rod takes up the slack at each dead centre,
        uneven in weight, from the packs' heavy plate and metal hits pitched for a locomotive's rods, a dry scrape of
        the bearing between knocks, and the valve gear rattling. Turning at 1.5 turns a second (a train coasting),
        an exact 8 s cycle so the beat never slips at the seam.""",
        sources=W.PIECES["iron"][:10], loop=True, takes=1, lufs=-29)
def knock(rng, k):
    n = samples(8.0)
    turns = 12                                      # 1.5 turns a second over 8 s, whole turns so it loops
    per = 8.0 / turns
    ev = []
    for i in range(turns):
        for half, g in ((0.0, 1.0), (0.5, 0.6)):
            t = (i + half) * per + rng.normal(0, 0.006)
            h = mix(norm(W.piece(rng, "iron", (-6, -3), tau=0.12)), W.knock(rng, 80, 0.15) * 0.5)
            ev.append((t, norm(h), g * rng.uniform(0.75, 1.0)))
    scrape = W.friction(n, rng, [180, 420, 760, 1300], rough=1.4, grit=0.3, q=10, loop=True)
    t = np.arange(n) / SR
    sc_env = np.clip(np.sin(2 * np.pi * turns * t / 8.0), 0, None) ** 2
    rattle = W.place(n, [(tt, W.body(rng, rng.uniform(900, 2000), W.PLATE, decay=0.02, length=0.06, count=6), 0.6)
                         for tt in W.poisson(8.0, 9, rng)])
    y = norm(W.place(n, ev)) + norm(scrape) * sc_env * 0.15 + norm(rattle) * 0.1
    return seamless(circ_outdoors(y, rng, 0.1))
