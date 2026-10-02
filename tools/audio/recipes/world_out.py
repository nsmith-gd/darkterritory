"""The world past the train (spec A.2, A.3 tier 6): the Territory at night, tunnels, bridges, rain and thunder, brass
growth, debris on the line, gales, and livestock aboard.

Weather and air are modelled (recipes/world_kit.py: wind driven by its speed with aeolian tones off whatever it passes,
rain as thousands of drop impacts on tin, ground and water, thunder from a lightning channel's N-waves); places the train
runs through are the train's own sound put through them (a tunnel's bore, a bridge's girders or timbers). Anything struck
starts from the packs' real impacts. Livestock are synthesised throats (glottis and tract) and are stand-ins until there
are animal recordings.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter, croom
from recipes.world_bed import rolling, exhaust, coupling, outdoors, circ_outdoors, frame_body

SR = dsp.SR


def ccomb(x, delay, g):
    """A circular feedback comb (sound bouncing between two parallel walls `delay` s apart in round trip)."""
    n = len(x)
    f = np.fft.rfftfreq(n, 1 / SR)
    H = 1 / (1 - g * np.exp(-2j * np.pi * f * delay))
    return np.fft.irfft(np.fft.rfft(x) * H, n).astype(np.float32)


# ---- The Territory at night ---------------------------------------------------------------------------------------------

def night_bed(rng, n, crickets=False):
    """Open country at night: wind moving through trees in slow swells, leaves stirring far off, a twig or a seed pod
    ticking now and then, the low breath of the land."""
    L = n / SR
    trees = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(700)) / 1.3) ** 2))
    swell = np.clip(0.55 + 0.45 * slow(n, 0.07, rng) + 0.15 * slow(n, 0.4, rng), 0.08, None) ** 1.5
    leaves = W.cyclic(lambda z: lp(z, 5000), synth.rustle(L, 220, rng, f=(2500, 9000), ticks=0.2)) * swell
    low = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 90) ** 2) * (f / 25) / (1 + f / 25))
    ticks = [(t, W.body(rng, rng.uniform(1500, 4000), W.PLATE, decay=0.008, length=0.04, count=5), rng.uniform(0.2, 1.0))
             for t in W.poisson(L, 0.5, rng)]
    y = norm(trees) * swell * 0.6 + norm(leaves) * 0.25 + norm(low) * 0.2 + norm(W.place(n, ticks)) * 0.05
    if crickets:
        y += norm(cricket_field(rng, n)) * 0.12
    return croom(y, "night", 0.3, rng)


def cricket_field(rng, n):
    """A few field crickets far off: each a ~4.6 kHz carrier rasped in syllables of 15 ms, three to five a chirp, two
    or three chirps a second, each insect its own pitch and pace, stopping and starting."""
    L = n / SR
    out = np.zeros(n, np.float32)
    for c in range(3):
        fc = rng.uniform(4200, 5100)
        syl, per = rng.uniform(0.012, 0.018), rng.uniform(0.028, 0.036)
        rate = rng.uniform(1.8, 2.8)
        ev = []
        on = slow(n, 0.05, rng) + 0.3
        t = rng.uniform(0, 1)
        while t < L:
            if on[samples(t) % n] > 0:
                for s_ in range(rng.integers(3, 6)):
                    m = samples(syl)
                    tt = np.arange(m) / SR
                    ch = np.sin(2 * np.pi * fc * (1 - 0.03 * tt / syl) * tt) * np.hanning(m)
                    ev.append((t + s_ * per, ch.astype(np.float32), 1.0))
            t += 1 / rate * rng.uniform(0.9, 1.1)
        out += W.place(n, ev) * rng.uniform(0.4, 1.0)
    return W.cyclic(lambda z: lp(z, 6000), out)


@recipe("world-night", "night", "still",
        "The wilderness at night past the train: wind moving through trees in slow swells, leaves, a twig ticking",
        """Modelled open country with nothing calling: broad wind noise through trees rising and falling over tens of
        seconds, dry leaves stirring far off (thousands of tiny scrapes), the low breath of the land, and now and then a
        twig or seed pod ticking somewhere close. Quiet enough that whatever is out there can be heard over it (the
        'far' one-shots, and the enemies). 16 s exact cycle.""", loop=True, takes=1, lufs=-28)
def night(rng, k):
    return seamless(night_bed(rng, samples(16.0)))


@recipe("world-night", "night", "crickets",
        "The wilderness at night with a few crickets far off: the same trees and wind, insects chirping and falling silent",
        """The same night as 'still', with three field crickets far off (each a 4-5 kHz carrier rasped in short syllables,
        three to five to a chirp, each at its own pitch and pace, stopping and starting). Ordinary country noise, so the
        moments they all stop mean something. 16 s exact cycle.""", loop=True, takes=1, lufs=-28)
def night_crickets(rng, k):
    return seamless(night_bed(rng, samples(16.0), crickets=True))


@recipe("world-night", "far", "out-there",
        "Something far off out there: six night sounds at 150-500 m (a tree creaking, a branch falling, stones, tin, water)",
        """Not enemies: the land itself, far enough off to make you listen. A dead tree creaking in the wind (modelled
        stick-slip through a hollow trunk), a branch cracking and crashing down through the others, stones rattling down
        a slope (the packs' stone cracks), a loose sheet of tin banging on some empty building, a heavy thud of something
        falling, something going into water. Each put at its distance: quieter, darker, more of it the night's
        reverb.""", sources=W.PIECES["brick"] + W.PIECES["tin"] + W.PIECES["wood"], takes=6, lufs=-30)
def far(rng, k):
    b = dsp.Bus(5.0)
    if k == 0:
        c = synth.creak(2.2, env([(0, 8), (1.0, 22), (2.2, 9)], 2.2), rng, body=synth.BARK, q=16, jitter=0.4)
        b.at(0, norm(c) * env([(0, 0), (0.5, 1), (1.6, 0.8), (2.2, 0)], 2.2)[:len(c)])
        d = 200
    elif k == 1:
        b.at(0, norm(W.splinter(0.25, rng, env([(0, 100), (0.25, 800)], 0.25))) * 0.7)
        b.at(0.25, norm(W.rec("sfx_100_v2:misc_35", semis=-4)) * 0.6)
        b.at(0.35, norm(synth.rustle(1.0, 900, rng, f=(1500, 7000), ticks=0.8)) * env([(0, 0), (0.1, 1), (1.0, 0)], 1.0)[:samples(1.0)] * 0.5)
        b.at(1.25, norm(W.piece(rng, "wood", (-6, -3))) * 0.7)
        d = 250
    elif k == 2:
        t = 0.0
        for i in range(14):
            b.at(t, norm(W.piece(rng, "brick", (-4, 3))) * rng.uniform(0.3, 1.0) * (1 - i / 18))
            t += rng.uniform(0.03, 0.18) * (1 + i / 6)
        d = 180
    elif k == 3:
        for i, t in enumerate((0.0, 0.35, 1.6)):
            b.at(t, norm(mix(W.piece(rng, "tin", (-6, -3)), W.sheet(rng, f1=90, fmax=2500, decay=0.4, contact=0.002) * 0.3))
                 * (1.0, 0.5, 0.8)[i])
        d = 300
    elif k == 4:
        b.at(0, norm(mix(W.knock(rng, 45, 0.8), W.piece(rng, "wood", (-10, -7)) * 0.6)))
        b.at(0.02, norm(synth.rustle(0.6, 600, rng, f=(1000, 5000))) * env([(0, 1), (0.6, 0)], 0.6)[:samples(0.6)] * 0.3)
        d = 400
    else:
        n = samples(0.6)
        splash = bp(rng.standard_normal(n).astype(np.float32), 300, 5000) * env([(0, 1), (0.02, 0.6), (0.6, 0)], 0.6, "exp")
        bub = synth.bubbles(0.8, 120, 250, 1500, rng) * env([(0, 1), (0.8, 0)], 0.8)
        b.at(0, norm(splash) * 0.8).at(0.01, norm(bub) * 0.4)
        b.at(0.0, norm(W.knock(rng, 70, 0.3)) * 0.4)
        d = 220
    return dsp.distance(b.x, d, rng=rng)


# ---- Tunnels ------------------------------------------------------------------------------------------------------------

def tunnel_wash(rng, n, loop=True):
    """The train's own noise filling a brick bore: only the reflections (the bed itself is already playing), long and
    dark, fluttering between the walls (8 m apart, a 47 ms round trip), over the air column's low drone."""
    src_ = rolling(rng, n, 15.0, 0)
    wet = croom(src_, "tunnel", 1.0, rng) if loop else dsp.room(src_, "tunnel", 1.0, rng=rng)[:n]
    fl = ccomb(lp(wet, 2500), 0.047, 0.55)
    drone = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(36)) / 0.5) ** 2))
    return norm(wet) * 0.7 + norm(fl) * 0.35 + norm(drone) * 0.3 * np.clip(1 + 0.3 * slow(n, 0.2, rng), 0.3, None)


@recipe("world-tunnels", "inside", "bore",
        "Inside the tunnel: the train's roar folded back on itself off the brick, fluttering, the air column droning",
        """What a tunnel adds to a train already playing: the bed's rumble (the real rail-transport recording) heard only as
        its reflections in a long brick bore, dark and dense, the flutter of sound bouncing wall to wall, the low drone of
        the air column being pushed, and a drip now and then. Layered on the bed (which gets the tunnel reverb itself).
        12 s exact cycle.""", sources=["sfx_100_v2:loop_ambient_04"], loop=True, takes=1, lufs=-22)
def tunnel_inside(rng, k):
    n = samples(12.0)
    y = tunnel_wash(rng, n)
    drips = [(t, dsp.room(W.drip(rng), "tunnel", 0.6, rng=rng), rng.uniform(0.05, 0.15)) for t in W.poisson(12.0, 0.4, rng)]
    return seamless(y + norm(W.place(n, drips)) * 0.25)


def portal(rng, L, t0, entering):
    """The moment at a tunnel mouth: the pressure wave thumping the ears, the air shoved, the bore's roar swelling in
    (or cut off behind), the portal's face slapping the exhaust back."""
    n = samples(L)
    t = np.arange(n) / SR
    wash = tunnel_wash(rng, n, loop=False)
    if entering:
        e = np.interp(t, [0, t0, t0 + 0.12, t0 + 1.2, L], [0, 0, 0.5, 1.0, 1.0])
    else:
        e = np.interp(t, [0, t0, t0 + 0.15, t0 + 0.6, L], [1.0, 1.0, 0.35, 0.05, 0])
    whump = mix(W.knock(rng, 26 if entering else 32, 0.6, drop=0.2), lp(rng.standard_normal(samples(0.4)).astype(np.float32), 150)
                * env([(0, 1), (0.4, 0)], 0.4) * 0.5)
    gust_u = env([(0, 14), (t0, 14), (t0 + 0.15, 24 if entering else 20), (t0 + 0.8, 16), (L, 15)], L)
    air, _ = W.wind(n, rng, gust_u, gust=0.05, buffet=0.8, whistle=0.2)
    air *= np.interp(t, [0, t0 - 0.2, t0, t0 + 0.9, L], [0.2, 0.3, 1.0, 0.4, 0.3])
    slap = dsp.room(norm(exhaust(rng, True)), "tunnel", 0.3, rng=rng)
    b = dsp.Bus(L)
    b.at(0, norm(wash) * e * 0.7)
    b.at(t0, norm(whump) * (0.9 if entering else 0.6))
    b.at(0, norm(air) * 0.45)
    if entering:
        b.at(t0 + 0.05, norm(slap) * 0.3)
    else:
        night_air, _ = W.wind(n, rng, 6.0, gust=0.2, buffet=0.1, whistle=0.0)
        b.at(0, norm(night_air) * np.interp(t, [0, t0, t0 + 1.0, L], [0, 0, 0.25, 0.25]))
    return dsp.shaped(b.x, [(0, 0), (0.15, 1), (L - 0.6, 1), (L, 0)])


@recipe("world-tunnels", "enter", "mouth",
        "The train entering a tunnel mouth: a thump of pressure in the ears, a shove of air, the roar closing in",
        """The pressure wave at the portal (a deep thump, as felt as heard), the air shoved past the train in a gust (the
        wind model), the portal face slapping an exhaust beat back, and the train's own roar swelling in off the brick as
        the bore closes round (the tunnel wash rising to full in about a second). Ends fading into the inside loop.""",
        sources=["sfx_100_v2:loop_ambient_04"], takes=2, lufs=-22)
def tunnel_enter(rng, k):
    return portal(rng, 3.4, (0.5, 0.7)[k], True)


@recipe("world-tunnels", "exit", "mouth",
        "Coming out of the tunnel: the roar cut off behind, a softer pressure thump, the night air opening out",
        """The reverse of entering: the bore's reflections fall away in a fraction of a second as the train breaks out,
        a softer thump as the pressure lets go, a gust as the air spills out with you, and the open night's wind coming
        up in place of the brick.""", sources=["sfx_100_v2:loop_ambient_04"], takes=2, lufs=-22)
def tunnel_exit(rng, k):
    return portal(rng, 3.0, (0.6, 0.8)[k], False)


@recipe("world-tunnels", "drip", "puddle",
        "Water dripping in the tunnel: drops into puddles on the ballast, ringing off the brick",
        """Each drop modelled as it lands in water: the splash's tick, then the entrained bubble's ring rising in pitch
        (the 'plip'), sizes varying so no two are the same note; one lands on wet stone with no puddle. Rung through
        the tunnel's long brick reverb.""", takes=4, lufs=-30)
def tunnel_drip(rng, k):
    d = W.drip(rng, "stone" if k == 3 else "water", size=(1.0, 0.7, 1.4, 1.0)[k])
    return dsp.room(d, "tunnel", 0.55, rng=rng)


# ---- Bridges ------------------------------------------------------------------------------------------------------------

GIRDER = [142, 183, 262, 344, 411, 560, 735]      # a riveted iron girder span's deck and web modes


@recipe("world-bridges", "iron-drum", "girders",
        "Wheels drumming on an iron bridge: the rumble booming hollow through the girders, each joint clanging the span",
        """The train's rolling (the real rail-transport recording) rung through a riveted girder span's modes, so the
        whole bridge drums and booms; at each rail joint the axles' blows ring the deck plates (the packs' heavy plate
        hits through the girders), a low boom of the span under it, and a faint shimmer of the steelwork singing.
        At 15 m/s on 12 m rails; 12 s exact cycle.""",
        sources=["sfx_100_v2:loop_ambient_04"] + W.PIECES["iron"][:5], loop=True, takes=1, lufs=-20)
def iron_drum(rng, k):
    n = samples(12.0)
    roll = rolling(rng, n, 15.0, 0)
    drum = W.cyclic(lambda z: dsp.resonate(z, GIRDER, q=14, gains=[1, 0.9, 0.8, 0.6, 0.5, 0.4, 0.3]), roll)
    ev = []
    for c in range(15):
        t0 = c * 0.8
        for gap in (0.0, 0.12):
            h = W.piece(rng, "iron", (-7, -4), tau=0.2)
            ev.append((t0 + gap + rng.uniform(-0.005, 0.005), norm(h), rng.uniform(0.5, 0.8)))
    hits = W.place(n, ev)
    hits = hits + 0.6 * W.cyclic(lambda z: dsp.resonate(z, GIRDER, q=40), hits)
    boom = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(55)) / 0.6) ** 2))
    shimmer = W.cyclic(lambda z: dsp.resonate(z, list(rng.uniform(900, 2600, 6)), q=200), pnoise(n, rng))
    y = norm(roll) * 0.45 + norm(drum) * 0.6 + norm(hits) * 0.45 + norm(boom) * 0.3 + norm(shimmer) * 0.04
    return seamless(circ_outdoors(y, rng, 0.2))


@recipe("world-bridges", "timber", "trestle",
        "A timber trestle under the train: a hollow wooden rumble, deck timbers knocking, the bents creaking",
        """The train's rolling (the real rail-transport recording, duller over timber) rung through the trestle's big wooden
        members, so it rumbles hollow and woody; the ties knock under each axle (the packs' wood hits), loose deck
        planks rattle, and the bents creak and groan under the load (modelled stick-slip through timber). 12 s exact
        cycle.""", sources=["sfx_100_v2:loop_ambient_04"] + W.PIECES["wood"], loop=True, takes=1, lufs=-20)
def timber(rng, k):
    n = samples(12.0)
    L = 12.0
    roll = cfilter(rolling(rng, n, 12.0, -1), lambda f: 1 / (1 + (f / 1800) ** 2))
    wood = [f * 0.35 for f in synth.WOOD[:5]]
    drum = W.cyclic(lambda z: dsp.resonate(z, wood, q=8), roll)
    ties = [(t, norm(W.piece(rng, "wood", (-6, -2))), rng.uniform(0.15, 0.4)) for t in np.arange(0, L, 0.05) if rng.random() < 0.35]
    creaks = []
    for t in W.poisson(L, 0.5, rng):
        cl = rng.uniform(0.6, 1.6)
        c = synth.creak(cl, env([(0, 10), (cl * 0.6, 35), (cl, 12)], cl), rng, body=frame_body(rng, rng.uniform(0.3, 0.5), False), q=14)
        creaks.append((t, c * env([(0, 0), (0.2, 1), (cl, 0)], cl)[:len(c)], rng.uniform(0.3, 0.6)))
    y = norm(roll) * 0.4 + norm(drum) * 0.5 + norm(W.place(n, ties)) * 0.35 + norm(W.place(n, creaks)) * 0.35
    return seamless(circ_outdoors(hp(y, 30), rng, 0.2))


@recipe("world-bridges", "groan", "load",
        "A weak bridge groaning under the load: two timber trestles creaking and cracking, two iron spans groaning",
        """Modelled stick-slip, which is what a structure groaning is: two takes of a timber trestle (big wooden members
        slipping at their joints, low and woody, with a split cracking somewhere in it), two of an iron span (girders
        flexing against their pins, deep and ringing, a rivet pinging). Two to four seconds each, slow and heavy.""",
        sources=["sfx_100_v2:misc_35"], takes=4, lufs=-24)
def bridge_groan(rng, k):
    L = rng.uniform(2.2, 3.6)
    b = dsp.Bus(L + 2)
    if k < 2:
        c = synth.creak(L, env([(0, 7), (L * 0.6, 26), (L, 9)], L), rng, body=frame_body(rng, rng.uniform(0.28, 0.36), False),
                        q=14, jitter=0.5)
        b.at(0, norm(c) * env([(0, 0), (L * 0.4, 1), (L, 0)], L)[:len(c)])
        b.at(L * rng.uniform(0.5, 0.7), norm(W.rec("sfx_100_v2:misc_35", semis=-rng.uniform(4, 7))) * 0.35)
    else:
        c = synth.creak(L, env([(0, 6), (L * 0.5, 18), (L, 7)], L), rng, body=[f * rng.uniform(0.95, 1.05) for f in GIRDER], q=45,
                        jitter=0.3)
        b.at(0, norm(c) * env([(0, 0), (L * 0.4, 1), (L, 0)], L)[:len(c)])
        b.at(L * 0.6, norm(W.body(rng, rng.uniform(2500, 3500), W.BAR, decay=0.2, contact=0.0001)) * 0.12)
    b.at(0, W.knock(rng, 40, 1.2, drop=0.1) * 0.25)
    return W.space(b.x, rng, "night", wet=0.25)


# ---- Rain -----------------------------------------------------------------------------------------------------------------

@recipe("world-rain", "rain-roof", "tin",
        "Rain on the roof tin heard from inside a car: thousands of drops pattering and drumming, bigger drips off the edge",
        """Modelled rain: about 3000 drops a second, sizes on a power law (a few big, most tiny), each a short hard contact
        that rings the roof sheet's panel modes, which is the patter; the roof's low modes drum under the whole of it;
        every few seconds a fat drip off the eaves lands with a louder tonk. Heard inside a wooden car. 12 s exact
        cycle.""", loop=True, takes=1, lufs=-22)
def rain_roof(rng, k):
    n = samples(12.0)
    pat = W.rain(n, rng, 3000, "tin")
    drum = W.cyclic(lambda z: dsp.resonate(z, [95, 150, 220, 310], q=12), pat)
    eaves = [(t, W.body(rng, rng.uniform(600, 1100), W.PLATE, decay=0.05, length=0.2, contact=0.0003, count=10), rng.uniform(0.4, 1.0))
             for t in W.poisson(12.0, 1.0, rng)]
    swell = np.clip(1 + 0.15 * slow(n, 0.15, rng), 0.5, None)
    y = (norm(pat) * 0.6 + norm(drum) * 0.4) * swell + norm(W.place(n, eaves)) * 0.25
    y = cfilter(y, lambda f: 1 / (1 + (f / 9000) ** 2))
    return seamless(croom(y, "car", 0.35, rng))


@recipe("world-rain", "rain-out", "ground",
        "Rain outside: a broad hiss of drops on ballast, earth and puddles, pattering on the cars, runoff trickling",
        """Modelled rain heard in the open: thousands of soft splats on ballast and earth (the broad hiss), drops in puddles
        each with its tiny bubble ring (the fine high fizz of rain on water), the tin roofs of the cars pattering a little
        way off, leaves shivering, and runoff trickling from somewhere (a slow stream of low bubbles). 12 s exact
        cycle.""", loop=True, takes=1, lufs=-22)
def rain_out(rng, k):
    n = samples(12.0)
    ground = W.rain(n, rng, 7000, "ground")
    puddle = W.rain(n, rng, 1600, "water")
    tin = cfilter(W.rain(n, rng, 900, "tin"), lambda f: 1 / (1 + (f / 4000) ** 2))
    leaves = W.cyclic(lambda z: z, synth.rustle(12.0, 500, rng, f=(3000, 10000), ticks=0.0))
    trickle = W.cyclic(lambda z: lp(z, 2500), synth.bubbles(12.0, 70, 300, 1400, rng, rise=(0.1, 0.5)))
    thuds = cfilter(ground, lambda f: 1 / (1 + (f / 350) ** 2))         # the big drops landing: a soft low patter
    y = (norm(ground) * 0.55 + norm(thuds) * 0.3 + norm(puddle) * 0.3 + norm(tin) * 0.18 + norm(leaves) * 0.12
         + norm(trickle) * 0.12)
    y *= np.clip(1 + 0.12 * slow(n, 0.12, rng), 0.5, None)
    return seamless(croom(y, "night", 0.2, rng))


@recipe("world-rain", "thunder", "channel",
        "Thunder: a close crack and roll, a mid-distance rumble, a far low roll, from a modelled lightning channel",
        """Thunder made the way the air makes it: a lightning channel kilometres long, tortuous, each metre of it sending
        out a short N-wave broadside; the arrivals spread over seconds because the channel is at many distances at once,
        which is the roll. The close take (800 m) starts with a ripping crack; the middle (2.5 km) is a rumble; the far
        one (7 km) only a low roll, the air having eaten the top. Rolls out in the night's reverb.""", takes=3, lufs=-22)
def thunder(rng, k):
    return W.thunder(rng, dist=(800.0, 2500.0, 7000.0)[k], channel=(4000.0, 5000.0, 6000.0)[k])


@recipe("world-rain", "slip", "drivers",
        "Wheels slipping on wet rail: the engine's beats suddenly racing, the drivers spinning and grinding, then caught",
        """What a crew hears when the drivers lose their grip: the exhaust beats (the blast-pipe model) suddenly racing as
        the wheels spin up, a steel-on-steel grind and whine from the tyres slipping on the rail head, the rods
        clattering, then the regulator shut and the beats collapsing back as the wheels bite. One short slip and one
        long, violent one.""", takes=2, lufs=-22)
def slip(rng, k):
    L = (2.4, 3.6)[k]
    n = samples(L)
    spin_at = 0.4
    peak_t = spin_at + (0.5, 0.9)[k]
    shut = peak_t + (0.3, 0.8)[k]
    rate = env([(0, 3.0), (spin_at, 3.2), (peak_t, (11.0, 15.0)[k]), (shut, (11.0, 15.0)[k]), (shut + 0.5, 3.0), (L, 2.8)], L)
    b = dsp.Bus(L + 1)
    t = 0.0
    while t < L:
        r = rate[min(samples(t), n - 1)]
        g = 1.0 if t < shut else np.interp(t, [shut, shut + 0.4], [1.0, 0.35])
        b.at(t, norm(exhaust(rng, True)) * g * rng.uniform(0.75, 1.0))
        t += 1 / r
    tt = np.arange(n) / SR
    spin = np.interp(tt, [0, spin_at, peak_t, shut, shut + 0.4], [0, 0, 1, 1, 0])
    grind = W.friction(n, rng, [380, 1050, 1880, 2750], rough=1.6, grit=0.8, q=16) * spin
    ph = 2 * np.pi * np.cumsum(220 + 500 * spin) / SR          # the tyres' whine climbing with the spin
    whine = (np.sin(ph) + 0.4 * np.sin(2 * ph)) * spin
    b.at(0, norm(grind) * 0.35 + norm(whine) * 0.08)
    return outdoors(b.x, rng, 0.12)


# ---- Brass growth -------------------------------------------------------------------------------------------------------

def brass_rod(rng, length=None, f1=None, decay=None):
    """One brass rod of the growth struck or snapped: a free bar's modes in brass (bright, long ringing)."""
    f1 = f1 or np.exp(rng.uniform(np.log(180), np.log(900)))
    d = decay or rng.uniform(0.8, 2.0)
    return W.body(rng, f1, W.BAR, decay=d, damp=0.25, contact=0.00015, length=length or min(3.0, d * 3), spread=0.02, tilt=0.4)


@recipe("world-brass", "cut", "pilot",
        "Cutting slowly through brass growth: rods bending and snapping against the pilot, ringing, grinding, tinkling down",
        """Brass rods of the growth pressed by the engine's pilot at walking pace: each rod bends with a ringing creak
        (stick-slip through a brass bar's modes, bright and long-sustaining) and snaps with a ping, the pilot grinds
        along the stems (modelled rubbing), the broken pieces tinkle down onto iron and ballast (the packs' light metal
        hits), and the uncut growth around keeps ringing faintly like chimes. 12 s exact cycle.""",
        sources=W.PIECES["scrap"], loop=True, takes=1, lufs=-22)
def brass_cut(rng, k):
    n = samples(12.0)
    L = 12.0
    ev = []
    for t in W.poisson(L, 1.4, rng):
        f1 = np.exp(rng.uniform(np.log(200), np.log(800)))
        bl = rng.uniform(0.3, 0.9)
        bend = synth.creak(bl, env([(0, 30), (bl, 160)], bl), rng, body=[f1 * r for r in W.BAR[:4]], q=60, jitter=0.2)
        ev.append((t, norm(bend) * env([(0, 0), (bl * 0.7, 1), (bl, 0.3)], bl)[:len(bend)], 0.35))
        ev.append((t + bl, norm(brass_rod(rng, f1=f1 * rng.uniform(1.2, 1.6))), 0.6))
        for j in range(rng.integers(1, 4)):
            ev.append((t + bl + rng.uniform(0.2, 0.6), norm(W.piece(rng, "scrap", (-1, 5))), rng.uniform(0.1, 0.3)))
    grind = W.friction(n, rng, [430, 1180, 2320, 3840], rough=1.5, grit=0.4, q=40, loop=True)
    grind *= np.clip(0.5 + 0.5 * slow(n, 0.6, rng), 0.05, None)
    chimes = W.place(n, [(t, norm(brass_rod(rng, decay=2.5)), rng.uniform(0.03, 0.08)) for t in W.poisson(L, 2.0, rng)])
    y = norm(W.place(n, ev)) * 0.8 + norm(grind) * 0.2 + norm(chimes) * 0.25
    return seamless(circ_outdoors(y, rng, 0.2))


@recipe("world-brass", "ram", "pilot",
        "Ramming through brass growth: the pilot smashing into it, dozens of rods snapping at once, a shower of brass",
        """The engine taking a thicket of brass at speed: the pilot's heavy clang (real plate and metal hits pitched for its
        mass), a burst of rods snapping and ringing all at once (modelled brass bars, dozens in half a second), a long
        screech of brass dragging under the engine, and pieces raining down and skittering off the train. The growth
        rings on after. Two takes of different size.""", sources=W.PIECES["iron"] + W.PIECES["scrap"], takes=2, lufs=-22)
def brass_ram(rng, k):
    L = 4.0
    b = dsp.Bus(L + 2)
    b.at(0, norm(mix(W.piece(rng, "iron", (-8, -5), tau=0.4), W.knock(rng, 55, 0.4) * 0.6)) * 0.9)
    for i in range((25, 40)[k]):
        b.at(abs(rng.normal(0.08, 0.15)), norm(brass_rod(rng, decay=rng.uniform(0.5, 1.5))) * rng.uniform(0.2, 0.6))
    n = samples(1.6)
    scr = W.friction(n, rng, [430, 1180, 2320, 3840], rough=2.0, grit=0.6, q=40)
    scr += W.squeal(n, rng, [2950], env([(0, 0.2), (0.4, 1), (1.6, 0)], 1.6), wander=0.03, harm=0.4) * 3
    b.at(0.1, norm(scr) * env([(0, 0), (0.1, 1), (1.6, 0)], 1.6)[:n] * 0.4)
    b.at(0.3, W.debris(rng, 2.5, 18, {"scrap": 4, "iron": 1}, decay=0.8, semis=(-2, 5)) * 0.5)
    return W.space(b.x, rng, "night", wet=0.22)


# ---- Track debris ---------------------------------------------------------------------------------------------------------

def slack_run(rng, cars=4, hard=False):
    """The jolt running back down the train: one coupling closing up after another, 0.11 s apart, quieter and further."""
    b = dsp.Bus(cars * 0.11 + 2.5)
    for i in range(cars):
        x = coupling(rng, int(rng.integers(6)), False)
        b.at(i * 0.11, dsp.distance(norm(x), 4 + 12 * i, rng=rng) * (1.0 if hard else 0.7))
    return b.x


@recipe("world-debris-hit", "jolt", "pilot",
        "Debris taken slowly: the pilot thumping into it and shoving it aside, the jolt running back down the train",
        """At walking pace: the engine's pilot meets something on the line (a log, a fall of rocks, a wrecked cart) with a
        heavy dull blow (the packs' wood and plate hits pitched for the mass), shoves and grinds it aside (modelled
        scraping, stones crunching), and the jolt runs back down the train as couplings close up one after another
        (the slack model). Three takes, three kinds of debris.""",
        sources=W.PIECES["wood"] + W.PIECES["brick"] + W.PIECES["iron"], takes=3, lufs=-22)
def jolt(rng, k):
    b = dsp.Bus(4.0)
    kind = ("wood", "brick", "wood")[k]
    b.at(0, norm(mix(W.piece(rng, kind, (-7, -3)), W.piece(rng, "iron", (-8, -5), tau=0.25) * 0.7, W.knock(rng, 50, 0.4))) * 0.9)
    n = samples(1.2)
    shove = W.friction(n, rng, [90, 170, 300, 520, 800] if kind == "wood" else [300, 700, 1300, 2200], rough=2, grit=0.5, q=6)
    b.at(0.05, norm(shove) * env([(0, 0), (0.1, 1), (0.9, 0.6), (1.2, 0)], 1.2)[:n] * 0.4)
    if kind == "brick":
        b.at(0.1, norm(W.spray(rng, 0.8, 50)) * 0.5)
    if k == 2:
        b.at(0.6, norm(W.splinter(0.3, rng, env([(0, 200), (0.3, 50)], 0.3))) * 0.4)
        b.at(0.75, norm(W.piece(rng, "scrap", (-5, -1))) * 0.4)
    b.at(0.15, norm(slack_run(rng, 4)) * 0.5)
    return outdoors(b.x, rng, 0.15)


@recipe("world-debris-hit", "hit-fast", "pilot",
        "Debris taken fast: a violent smash at the pilot, wood and stone bursting, pieces battering down the train",
        """At speed: the pilot smashes through it with the crash model (iron, wood bursting, ballast thrown), pieces fly
        and batter along the engine and the first cars (the packs' wood, stone and metal hits passing down the train),
        and a hard slack run-in follows as the train takes the blow. Close to a derailment, and it can become one.
        Stand-in for its big-metal part until recorded crashes (Sonniss).""",
        sources=W.PIECES["wood"] + W.PIECES["brick"] + W.PIECES["iron"] + W.PIECES["scrap"], takes=2, lufs=-22)
def hit_fast(rng, k):
    b = dsp.Bus(5.0)
    b.at(0, W.crash(rng, 0.9, iron=0.8, wood=1.0 if k == 0 else 0.4, ground=0.4 if k == 0 else 1.0, crumple=0.1))
    t = 0.1
    for i in range(10):
        kind = rng.choice(["wood", "brick", "scrap"])
        b.at(t, dsp.distance(norm(W.piece(rng, kind, (-4, 2))), 3 + 6 * i, rng=rng) * rng.uniform(0.4, 0.8))
        t += rng.uniform(0.05, 0.2)
    b.at(0.12, norm(slack_run(rng, 5, hard=True)) * 0.6)
    return outdoors(b.x, rng, 0.15)


# ---- Wind as a hazard -------------------------------------------------------------------------------------------------------

@recipe("world-wind", "gale", "storm",
        "Strong wind: a roaring gale, buffeting hard, the train's fittings howling and moaning, loose canvas cracking",
        """The wind model at gale force (around 30 m/s, gusting hard): a heavy roar whose brightness follows
        every gust, the buffeting that pummels anyone on a roof (enough to mask a voice beside you), the fittings'
        aeolian tones climbing into a howl and falling back to a moan with each gust, and loose canvas cracking and
        flapping. 12 s exact cycle.""", loop=True, takes=1, lufs=-20)
def gale(rng, k):
    n = samples(12.0)
    y, _ = W.wind(n, rng, 30.0, gust=0.17, gust_rate=0.3, buffet=0.9, whistle=0.5, flap=0.9)
    return seamless(W.cyclic(lambda z: dsp.compress(z, -16, 3.0, 0.02, 0.3), norm(y)))


@recipe("world-wind", "gust", "hard",
        "A hard gust: the wind slamming in, a howl rising through the fittings and falling, the buffeting peaking",
        """The wind model driven through a hard gust, 18 m/s rising to 34-42 m/s in under a second and dying away over
        two or three: the roar brightens and thickens, the fittings' tones sweep up into a howl and back down, the
        buffeting thumps at the peak, canvas cracks. Four takes, different strengths and shapes (one double gust).""",
        takes=4, lufs=-20)
def hard_gust(rng, k):
    L = (3.0, 3.6, 4.2, 4.0)[k]
    peak = (34, 38, 42, 36)[k]
    if k == 3:
        sp = env([(0, 18), (0.7, peak), (1.4, 24), (2.1, peak * 0.9), (L, 17)], L)
    else:
        sp = env([(0, 18), (L * 0.25, peak), (L * 0.45, peak * 0.85), (L, 17)], L)
    y, _ = W.wind(samples(L), rng, sp, gust=0.06, gust_rate=1.0, buffet=0.9, whistle=0.5, flap=0.6)
    return dsp.shaped(y, [(0, 0), (0.3, 0.5), (L * 0.3, 1), (L - 0.5, 0.5), (L, 0)])


# ---- Livestock aboard (stand-ins) -----------------------------------------------------------------------------------------

def hooves(rng, L, rate, semis=(-4, 0)):
    """Animals shifting on a wooden car floor: hooves knocking and scuffing (the packs' wood hits and footsteps)."""
    ev = []
    for t in W.poisson(L, rate, rng):
        key = f"kenney_impact-sounds:{rng.choice(['impactWood_light', 'impactWood_medium', 'footstep_wood'])}_00{rng.integers(5)}"
        ev.append((t, norm(W.rec(key, semis=rng.uniform(*semis))), rng.uniform(0.2, 0.8)))
    return W.place(samples(L), ev)


def stall(rng, n, calls, steps, straw=0.15, bumps=0.3, size=0.45, herd=4):
    """A stock car's floor and walls with animals in it: calls placed round the cycle, the herd breathing and snorting
    out of step, hooves, straw, a body against the boards, all inside the car."""
    L = n / SR
    breaths = []
    for a in range(herd):
        t = rng.uniform(0, 3)
        while t < L:
            bl = rng.uniform(0.5, 1.1)
            br = synth.breath(bl, "h", size, rng=rng, shape=env([(0, 0), (bl * 0.3, 1), (bl, 0)], bl))
            breaths.append((t, br, rng.uniform(0.3, 1.0) * (2.5 if rng.random() < 0.1 else 1.0)))    # now and then a snort
            t += rng.uniform(2.0, 4.0)
    y = norm(W.place(n, calls)) * 0.8 + norm(hooves(rng, L, steps)) * 0.25
    y += norm(W.cyclic(lambda z: lp(z, 2500), W.place(n, breaths))) * 0.2
    y += norm(W.cyclic(lambda z: lp(z, 6000), synth.rustle(L, 120, rng, f=(1500, 6000), ticks=0.1))) * straw
    thuds = [(t, norm(mix(W.piece(rng, "wood", (-8, -5)), W.knock(rng, 70, 0.2))), rng.uniform(0.3, 0.7))
             for t in W.poisson(L, bumps, rng)]
    y += norm(W.place(n, thuds)) * 0.25
    return seamless(croom(y, "car", 0.3, rng))


def moo(rng):
    """A cow's low: a closed-mouth hum opening into 'oo' and on to 'aa' as the head lifts, pitch rising then sagging."""
    L = rng.uniform(1.2, 2.4)
    f0 = rng.uniform(95, 140)
    pts = [(0, f0 * 0.85), (L * 0.3, f0 * 1.15), (L * 0.7, f0 * 1.05), (L, f0 * 0.8)]
    vow = [(0, "m"), (L * 0.25, "u"), (L * 0.55, "o"), (L * 0.8, "a"), (L, "u")]
    return synth.tract(synth.glottis(env(pts, L, "exp"), L, rng, jitter=0.01, shimmer=0.12, sub=0.2, rough=0.15), vow, 0.42,
                       breath=0.2, rng=rng) * dsp.fit(env([(0, 0), (0.15, 0.7), (L * 0.4, 1), (L * 0.85, 0.8), (L, 0)], L), samples(L))


def grunt(rng):
    """A pig's grunt: a short rough nasal pulse, low and noisy."""
    L = rng.uniform(0.12, 0.35)
    f0 = rng.uniform(85, 150)
    g = synth.glottis(env([(0, f0), (L, f0 * 0.8)], L, "exp"), L, rng, jitter=0.03, shimmer=0.3, sub=0.5, rough=0.8)
    v = synth.tract(g, "o", 0.55, breath=0.5, rng=rng, extra=[(250, 4, 0.6)])
    return v * dsp.fit(env([(0, 0), (0.02, 1), (L, 0)], L), samples(L))


def squeal(rng):
    """A pig's squeal: high, harsh and wavering."""
    L = rng.uniform(0.4, 0.9)
    f0 = rng.uniform(700, 1200)
    g = synth.glottis(env([(0, f0 * 0.8), (L * 0.3, f0 * 1.2), (L, f0 * 0.9)], L, "exp"), L, rng, jitter=0.02, shimmer=0.2,
                      rough=0.6)
    return synth.tract(g, "e", 0.6, breath=0.3, rng=rng) * dsp.fit(env([(0, 0), (0.05, 1), (L * 0.8, 0.8), (L, 0)], L), samples(L))


def bleat(rng):
    """A sheep's 'baa': its quaver is the glottis beating in pulses five to eight times a second."""
    L = rng.uniform(0.6, 1.2)
    f0 = rng.uniform(190, 330)
    q = rng.uniform(5.5, 8)
    t = np.arange(samples(L)) / SR
    f = env([(0, f0 * 0.95), (L * 0.3, f0 * 1.05), (L, f0 * 0.92)], L, "exp") * (1 + 0.03 * np.sin(2 * np.pi * q * t))
    g = synth.glottis(f, L, rng, jitter=0.02, shimmer=0.15, rough=0.2)
    g = g * (0.55 + 0.45 * np.sin(2 * np.pi * q * t) ** 2)
    v = synth.tract(g, [(0, "e"), (L * 0.2, "a"), (L, "a")], 0.62, breath=0.25, rng=rng)
    return v * dsp.fit(env([(0, 0), (0.06, 1), (L * 0.7, 0.8), (L, 0)], L), samples(L))


@recipe("world-livestock", "cattle", "stock-car",
        "Stand-in: cattle in a car: low moos from two or three beasts, hooves shifting on the boards, bodies against walls",
        """Stand-in until animal recordings (Sonniss). Two or three cattle synthesised as throats: a glottis at 95-140 Hz
        through a vocal tract the size of a cow's, starting as a closed-mouth hum and opening through 'oo' to 'aa',
        rough and breathy, and the herd breathing and snorting between (breath through the same tracts). Round them, the
        stock car itself from the packs: hooves knocking and scuffing on the wooden floor, straw, a heavy body bumping
        the boards. 16 s exact cycle.""",
        sources=["kenney_impact-sounds:impactWood_light_000"], loop=True, takes=1, lufs=-22)
def cattle(rng, k):
    n = samples(16.0)
    calls = [(t, moo(rng), rng.uniform(0.5, 1.0)) for t in np.sort(rng.uniform(0, 16, 5))]
    return stall(rng, n, calls, steps=4.0, straw=0.2, bumps=0.4, size=0.42)


@recipe("world-livestock", "pigs", "stock-car",
        "Stand-in: pigs in a car: constant grunting from several animals, a squeal now and then, trotters and straw",
        """Stand-in until animal recordings (Sonniss). Several pigs synthesised as throats: short, rough, nasal grunts
        (a glottis at 85-150 Hz with heavy roughness through a small tract and a nasal resonance) two or three a second
        among them, and now and then a squeal (high, harsh, wavering). Trotters on the wooden floor and straw from the
        packs. 12 s exact cycle.""", sources=["kenney_impact-sounds:impactWood_light_000"], loop=True, takes=1, lufs=-22)
def pigs(rng, k):
    n = samples(12.0)
    calls = [(t, grunt(rng), rng.uniform(0.3, 1.0)) for t in W.poisson(12.0, 2.6, rng)]
    calls += [(t, squeal(rng), rng.uniform(0.4, 0.7)) for t in rng.uniform(0, 12, 2)]
    return stall(rng, n, calls, steps=4.0, straw=0.25, bumps=0.3, size=0.55, herd=6)


@recipe("world-livestock", "sheep", "stock-car",
        "Stand-in: sheep in a car: bleats from a flock, quavering, small hooves pattering, straw",
        """Stand-in until animal recordings (Sonniss). A flock's bleats synthesised as throats: a glottis at 190-330 Hz
        beating in pulses five to eight times a second (the quaver that makes a bleat a bleat) through a small tract
        opening from 'eh' to 'aa'. Many light hooves pattering on the boards and straw from the packs. 12 s exact
        cycle.""", sources=["kenney_impact-sounds:impactWood_light_000"], loop=True, takes=1, lufs=-22)
def sheep(rng, k):
    n = samples(12.0)
    calls = [(t, bleat(rng), rng.uniform(0.4, 1.0)) for t in W.poisson(12.0, 0.9, rng)]
    return stall(rng, n, calls, steps=6.0, straw=0.2, bumps=0.15, size=0.62, herd=8)
