"""Facilities and places (GDD places, App. D): the fortress and its gates, the coaling tower, the grain elevator, the
foundry cranes, a wreck yard, the slaughterhouse, dead towns, the mine head, the chemical works, and breaking into a
Holdout.

Machinery, pours and steam are modelled (recipes/world_kit.py: a granular pour of lumps or grains through a ringing
chute, the jet, stick-slip for creaking hinges, props and hooks); every blow, latch, chain link and board starts from
the packs' real impacts, varispeeded to the part's size and put in the place's space (open yard, stone hall, mine).
A real fortress crowd and a real steam whistle aren't in the packs: those parts are synthesised stand-ins.
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, bp, env, mix
from recipes import world_kit as W
from recipes.world_kit import norm, seamless, slow, pnoise, cfilter, croom
from recipes.world_bed import exhaust, outdoors, circ_outdoors, frame_body, src_loop
from recipes.world_out import moo, grunt, hooves

SR = dsp.SR
CHUTE = [140, 262, 410, 655, 980, 1490, 2210]     # a riveted steel chute's plate modes


def yard(y, rng, wet=0.25):
    """An open works yard with buildings round it: slaps off the walls and a short outdoor tail."""
    return W.space(y, rng, "hall", wet=wet, early=((0.018, 0.3), (0.041, 0.2)))


# ---- The fortress and the threshold --------------------------------------------------------------------------------------

def walla(rng, n, voices=14, far=60.0):
    """A crowd far off: many synthesised throats talking at once, each a man's voice wandering through vowels in
    syllables with a speech-like pitch contour, too many and too far to make out a word. A stand-in for a real crowd."""
    L = n / SR
    out = np.zeros(n, np.float32)
    vows = ["a", "e", "o", "u", "i", "m"]
    for v in range(voices):
        t = rng.uniform(0, 2)
        ev = []
        while t < L:
            pl = rng.uniform(0.8, 2.5)                        # a phrase
            f0 = rng.uniform(95, 150)
            pts = [(0, f0 * 1.1), (pl * 0.3, f0 * rng.uniform(1.0, 1.3)), (pl, f0 * 0.8)]
            syl = np.arange(0, pl, rng.uniform(0.14, 0.22))
            vw = [(s, vows[rng.integers(len(vows))]) for s in syl]
            g = synth.glottis(env(pts, pl, "exp"), pl, rng, jitter=0.01, shimmer=0.15)
            ph = synth.tract(g, vw, "man", breath=0.3, rng=rng)
            sy = np.abs(np.sin(np.pi * np.arange(len(ph)) / SR / (syl[1] - syl[0] if len(syl) > 1 else 0.2))) ** 0.7
            ev.append((t, ph * sy * dsp.fit(env([(0, 0), (0.05, 1), (pl - 0.1, 1), (pl, 0)], pl), len(ph)), rng.uniform(0.4, 1.0)))
            t += pl + rng.uniform(0.3, 2.5)
        out += W.place(n, ev)
    return W.cyclic(lambda z: lp(z, 18000 / (1 + far / 25)), out)


def pea_whistle(rng, blasts=2):
    """A guard's whistle: a ~3 kHz tone trilled by the pea rattling in its chamber (a fast rough flutter), in blasts."""
    b = dsp.Bus(2.5)
    t = 0.0
    for i in range(blasts):
        L = rng.uniform(0.25, 0.7)
        n = samples(L)
        f = 3050 * rng.uniform(0.95, 1.05) * (1 + 0.01 * slow(n, 30, rng))
        trill = 0.55 + 0.45 * np.sin(2 * np.pi * np.cumsum(28 + 6 * slow(n, 5, rng)) / SR)
        x = np.sin(2 * np.pi * np.cumsum(f) / SR) * trill + 0.15 * bp(rng.standard_normal(n).astype(np.float32), 2000, 6000)
        b.at(t, x * env([(0, 0), (0.02, 1), (L - 0.04, 1), (L, 0)], L)[:n])
        t += L + rng.uniform(0.1, 0.25)
    return b.x


def steam_whistle(rng, L=1.6):
    """A works whistle (stand-in): a chime of three pipes sounding a chord, driven by a steam jet, breathy and wavering,
    with the jet's hiss over it."""
    n = samples(L)
    t = np.arange(n) / SR
    y = np.zeros(n, np.float32)
    for f in (262, 330, 392):
        fi = f * (1 + 0.004 * slow(n, 4, rng))
        ph = 2 * np.pi * np.cumsum(fi) / SR
        y += (np.sin(ph) + 0.5 * np.sin(2 * ph) + 0.25 * np.sin(3 * ph) + 0.12 * np.sin(4 * ph)).astype(np.float32)
    y = y * np.interp(t, [0, 0.12, L - 0.2, L], [0, 1, 0.9, 0])
    hiss = W.jet(L, rng, peak=3000, low=0.0) * np.interp(t, [0, 0.05, L - 0.15, L], [0, 1, 1, 0])
    return norm(y) + norm(hiss) * 0.15


@recipe("place-threshold", "fortress", "works",
        "Stand-in (crowd, whistles): inside the fortress: engines and line shafts, hammering, a crowd, guards' whistles",
        """The fortress as a working place, layered by distance: a stationary steam engine driving the line shafts (the
        blast-pipe model, slow, distant) over the packs' engine-idle and knocking loops pitched down for big machines,
        boilermakers hammering iron plate in bursts (Kenney's heavy metal hits), carts and chains clanking, a crowd of
        workers talking (synthesised throats, too far off to make out), guards' pea whistles now and then and a works
        whistle once (both synthesised). Crowd and whistles are stand-ins until real recordings (Sonniss).
        16 s exact cycle.""", sources=["sfx_100_v2:loop_machine_01", "sfx_100_v2:loop_machine_04"] + W.PIECES["iron"],
        loop=True, takes=1, lufs=-20)
def fortress(rng, k):
    n = samples(16.0)
    L = 16.0
    m1 = src_loop("sfx_100_v2:loop_machine_01", n, -5, rng)
    m4 = cfilter(src_loop("sfx_100_v2:loop_machine_04", n, -7, rng), lambda f: 1 / (1 + (f / 1500) ** 2))
    beats = [(t, norm(exhaust(rng, False)), 0.6) for t in np.arange(0, L, L / 40)]             # 2.5 beats a second
    engine = cfilter(W.place(n, beats), lambda f: 1 / (1 + (f / 1200) ** 2))
    ham = []
    for burst in W.poisson(L, 0.35, rng):
        for j in range(rng.integers(4, 9)):
            h = mix(norm(W.piece(rng, "iron", (-3, 1), tau=0.25)), norm(W.body(rng, rng.uniform(600, 900), W.PLATE, decay=0.3)) * 0.3)
            ham.append((burst + j * rng.uniform(0.42, 0.5), h, rng.uniform(0.5, 0.9)))
    clanks = [(t, norm(W.piece(rng, rng.choice(["iron", "scrap"]), (-6, 0))), rng.uniform(0.2, 0.5)) for t in W.poisson(L, 1.2, rng)]
    crowd = walla(rng, n)
    whistles = [(t, pea_whistle(rng, int(rng.integers(1, 4))), 0.5) for t in rng.uniform(0, L, 2)]
    works = [(rng.uniform(0, L), steam_whistle(rng), 0.6)]
    near = (norm(W.place(n, ham)) * 0.45 + norm(W.place(n, clanks)) * 0.3)
    farx = (norm(m1) * 0.45 + norm(m4) * 0.3 + norm(engine) * 0.35 + norm(crowd) * 0.35
            + norm(W.place(n, whistles)) * 0.12 + norm(W.place(n, works)) * 0.15)
    farx = cfilter(farx, lambda f: 1 / (1 + (f / 4000) ** 2))
    y = croom(near, "hall", 0.35, rng) + croom(farx, "hall", 0.6, rng)
    return seamless(y)


def gate_groan(rng, L, up=True):
    """A great iron gate's hinge pins turning under the weight: a slow stick-slip groan through heavy iron, its pace
    rising and easing as the gate swings."""
    rate = env([(0, 9), (L * 0.5, 26 if up else 20), (L, 10)], L)
    body = [f * 0.55 * rng.uniform(0.97, 1.03) for f in synth.IRON] + [f * 1.3 for f in synth.IRON[:3]]
    return synth.creak(L, rate, rng, body=body, q=40, jitter=0.3, grit=0.2)


def big_clang(rng, weight=1.0):
    """A gate leaf meeting its post or stop: sheet iron ringing low, real heavy hits pitched down, the weight behind it."""
    return mix(norm(W.sheet(rng, f1=48, fmax=4000, decay=1.4, contact=0.0008)) * 0.6,
               norm(W.piece(rng, "iron", (-9, -6), tau=0.6)) * 0.8,
               norm(W.rec("sfx_100_v2:metal_hit_01", semis=-8)) * 0.6, W.knock(rng, 45, 0.6) * 0.7 * weight)


@recipe("place-threshold", "gate-open", "iron",
        "The fortress gates opening: the bar drawn, chains rattling, the great leaves groaning open, booming on their stops",
        """The locking bar dragged back through its staples (modelled iron rubbing, a clank as it clears), the counterweight
        chains rattling (real light metal hits as links), the leaves swinging open on hinge pins that groan under the
        weight (modelled stick-slip through heavy iron) and a low rumble of their rollers, then each leaf booming against
        its stop (sheet iron, the packs' heavy hits pitched down). Echoing off the fortress walls.""",
        sources=W.PIECES["iron"] + W.PIECES["scrap"], takes=1, lufs=-22)
def gate_open(rng, k):
    b = dsp.Bus(9.0)
    n = samples(1.0)
    bar = W.friction(n, rng, [180, 420, 760, 1300], rough=1.8, grit=0.3, q=20) * env([(0, 0), (0.1, 1), (0.9, 1), (1.0, 0)], 1.0)[:n]
    b.at(0, norm(bar) * 0.45)
    b.at(1.0, norm(W.piece(rng, "iron", (-6, -3), tau=0.3)) * 0.7)
    for i in range(14):
        b.at(1.2 + i * 0.07 + rng.uniform(-0.01, 0.01), norm(W.piece(rng, "scrap", (-5, -1), tau=0.1)) * rng.uniform(0.2, 0.4))
    b.at(1.6, norm(gate_groan(rng, 4.2)) * 0.6)
    roll = cfilter(pnoise(samples(4.2), rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(80)) / 0.8) ** 2))
    b.at(1.6, norm(roll) * env([(0, 0), (0.5, 1), (4.2, 0)], 4.2)[:len(roll)] * 0.35)
    b.at(5.8, norm(big_clang(rng)) * 0.9)
    b.at(6.15, norm(big_clang(rng, 0.7)) * 0.6)
    return yard(b.x, rng, 0.35)


@recipe("place-threshold", "gate-shut", "iron",
        "The gates shutting behind the train: the leaves groaning closed, a booming clang, the bar dropped home",
        """The two leaves swung shut (the hinge groan, modelled), meeting with a booming clang (sheet iron ringing low with
        the packs' heavy metal hits pitched down for the mass), then the locking bar thrown home with a heavy clunk and
        the chains settling. It rings off the walls and dies away, and there's only the track.""",
        sources=W.PIECES["iron"] + W.PIECES["scrap"], takes=1, lufs=-22)
def gate_shut(rng, k):
    b = dsp.Bus(8.0)
    b.at(0, norm(gate_groan(rng, 3.0, up=False)) * 0.55)
    b.at(3.0, norm(big_clang(rng, 1.2)) * 1.0)
    b.at(3.08, norm(big_clang(rng, 0.6)) * 0.5)
    n = samples(0.6)
    bar = W.friction(n, rng, [180, 420, 760, 1300], rough=1.8, grit=0.3, q=20) * env([(0, 0), (0.1, 1), (0.6, 0)], 0.6)[:n]
    b.at(3.9, norm(bar) * 0.35)
    b.at(4.5, norm(mix(W.piece(rng, "iron", (-7, -4), tau=0.3), W.knock(rng, 60, 0.3))) * 0.8)
    for i in range(8):
        b.at(4.6 + i * 0.09, norm(W.piece(rng, "scrap", (-5, -1), tau=0.1)) * rng.uniform(0.15, 0.3) * (1 - i / 10))
    return yard(b.x, rng, 0.4)


# ---- Coaling tower --------------------------------------------------------------------------------------------------------

def coal_stream(rng, n, flow, loop=True):
    """Coal down a steel chute: lumps cracking against each other and the plates (the pour model, brittle clicks, big lumps
    thudding), the packs' stone cracks for the larger lumps, the mass of it roaring, the chute ringing."""
    p = W.pour(n, rng, flow, grain=(900, 5000), lump=14, thunder=1.0, ring=CHUTE, loop=loop)
    L = n / SR
    lumps = [(t, norm(W.piece(rng, "brick", (-8, -2))), rng.uniform(0.2, 0.6)) for t in W.poisson(L, 10, rng)]
    return norm(p) + norm(W.place(n, lumps, loop=loop)) * 0.35


@recipe("place-coaling", "coal-pour", "chute",
        "Coal pouring down the chute: a deafening rattling roar of lumps, the steel chute ringing, the tender filling",
        """A gravity chute's flood modelled as a pour: a few thousand lump impacts a second, each a brittle crack whose pitch
        says its size, big lumps thudding among them (the packs' stone cracks pitched down), the weight of it as a low
        roar surging, and the riveted steel chute ringing under the stream. Fast and deafening. 10 s exact cycle.""",
        sources=W.PIECES["brick"], loop=True, takes=1, lufs=-18)
def coal_pour(rng, k):
    n = samples(10.0)
    flow = np.clip(2600 + 900 * slow(n, 0.8, rng), 600, None)
    return seamless(circ_outdoors(coal_stream(rng, n, flow), rng, 0.15))


def lever(rng, clicks=4):
    """A heavy iron lever thrown: its pawl ratcheting over the quadrant (real light metal clicks), then the latch."""
    b = dsp.Bus(1.2)
    for i in range(clicks):
        b.at(i * rng.uniform(0.06, 0.09), norm(W.piece(rng, "scrap", (-6, -3), tau=0.05)) * rng.uniform(0.3, 0.5))
    b.at(clicks * 0.08 + 0.05, norm(W.piece(rng, "iron", (-5, -2), tau=0.15)) * 0.6)
    return b.x


@recipe("place-coaling", "chute-open", "gate",
        "The chute lever pulled: the lever ratcheting, the gate dropping with a clank, the coal bursting out",
        """The lever's pawl ratcheting over its quadrant (Kenney's light metal hits pitched for heavy iron), the chute's
        apron gate dropping open with a heavy clank and its counterweight chain rattling, then the coal let go all at
        once: the pour model surging from nothing to full flood in half a second. Ends fading into the pour loop.""",
        sources=W.PIECES["scrap"] + W.PIECES["iron"] + W.PIECES["brick"], lufs=-20)
def chute_open(rng, k):
    L = 3.0
    b = dsp.Bus(L + 1)
    b.at(0, lever(rng, 3 + k))
    g = 0.45 + 0.05 * k
    b.at(g, norm(mix(W.piece(rng, "iron", (-8, -5), tau=0.4), W.knock(rng, 70, 0.3) * 0.6)) * 0.8)
    for i in range(6):
        b.at(g + 0.05 + i * 0.06, norm(W.piece(rng, "scrap", (-4, 0), tau=0.08)) * 0.2)
    n = samples(L - g)
    flow = env([(0, 50), (0.15, 600), (0.5, 3200), (L - g, 2800)], L - g)
    st = coal_stream(rng, n, flow, loop=False) * env([(0, 0.3), (0.5, 1), (L - g - 0.6, 1), (L - g, 0)], L - g)[:n]
    b.at(g + 0.05, norm(st) * 0.8)
    return outdoors(b.x, rng, 0.15)


@recipe("place-coaling", "chute-shut", "gate",
        "The chute shut: the gate slammed into the stream, the flood cut off, the last lumps rattling down",
        """The apron gate heaved shut against the coal (a heavy iron clank and scrape, real hits pitched down), the pour model
        choking from full flood to nothing in a third of a second, and the last few lumps clattering down the chute and
        onto the tender.""", sources=W.PIECES["iron"] + W.PIECES["brick"], lufs=-23)
def chute_shut(rng, k):
    L = 2.6
    b = dsp.Bus(L + 1)
    n = samples(1.0)
    flow = env([(0, 2800), (0.25 + 0.05 * k, 200), (0.5, 10), (1.0, 5)], 1.0)
    b.at(0, norm(coal_stream(rng, n, flow, loop=False) * env([(0, 1), (0.6, 0.2), (1.0, 0)], 1.0)[:n]) * 0.8)
    b.at(0.2 + 0.05 * k, norm(mix(W.piece(rng, "iron", (-7, -4), tau=0.4), W.knock(rng, 65, 0.3) * 0.5)) * 0.9)
    t = 0.6
    for i in range(rng.integers(4, 8)):
        b.at(t, norm(W.piece(rng, "brick", (-5, 2))) * rng.uniform(0.15, 0.4))
        t += rng.uniform(0.08, 0.3)
    return outdoors(b.x, rng, 0.15)


@recipe("place-coaling", "coal-settle", "tender",
        "The coal settling in the tender: the heap slumping with a crunching rush, lumps rolling down and knocking iron",
        """The new heap in the tender giving way: a short crunching slide (the pour model at a trickle surging once), lumps
        tumbling down its face (the packs' stone cracks) and the odd one knocking the tender's side plate (a modelled
        sheet-iron ring).""", sources=W.PIECES["brick"], takes=2, lufs=-24)
def coal_settle(rng, k):
    L = (1.8, 2.4)[k]
    n = samples(L)
    flow = env([(0, 20), (0.2, 900), (0.6 + 0.2 * k, 300), (L, 5)], L)
    b = dsp.Bus(L + 1)
    b.at(0, norm(W.pour(n, rng, flow, grain=(900, 5000), lump=5, thunder=0.8, loop=False)) * 0.7)
    for t in np.sort(rng.uniform(0.3, L - 0.2, 4)):
        b.at(t, norm(W.piece(rng, "brick", (-5, 1))) * rng.uniform(0.2, 0.45))
    b.at(L * 0.7, norm(W.sheet(rng, f1=110, fmax=3000, decay=0.4, contact=0.001)) * 0.25)
    return outdoors(b.x, rng, 0.12)


# ---- Grain elevator -------------------------------------------------------------------------------------------------------

SPOUT = [118, 236, 354, 472, 590]      # a 1.4 m steel spout pipe, open both ends: its pipe resonances


@recipe("place-grain", "grain-pour", "spout",
        "Grain pouring: a smooth, heavy hiss of kernels rushing down the spout and drumming into the car",
        """Grain modelled as a pour of tens of thousands of tiny hard kernels a second (so it smooths into a hiss, unlike
        coal's rattle), its rush hollow through the steel spout's pipe resonances, the stream landing on the growing
        heap with a soft roar and drumming the wooden car's floor and sides low. 10 s exact cycle.""", loop=True,
        takes=1, lufs=-20)
def grain_pour(rng, k):
    n = samples(10.0)
    flow = np.clip(30000 + 3000 * slow(n, 0.5, rng), 5000, None)
    p = W.pour(n, rng, flow, grain=(1200, 9000), lump=0, thunder=0.5, voices=32, decay=0.003)
    pipe = W.cyclic(lambda z: dsp.resonate(z, SPOUT, q=12), p)
    car = W.cyclic(lambda z: dsp.resonate(z, [f * 0.35 for f in synth.WOOD[:4]], q=6), lp(p, 800))
    y = norm(p) * 0.7 + norm(pipe) * 0.35 + norm(car) * 0.35
    return seamless(circ_outdoors(y, rng, 0.15))


@recipe("place-grain", "spout-swing", "pivot",
        "The spout swung over a car: its pivot creaking, the counterweight chain rattling, a hollow clank on the stop",
        """A long steel spout swinging on its pivot: an iron-on-iron creak as it turns (modelled stick-slip, its pitch
        following the swing), the counterweight chain's links rattling (Kenney's light metal hits), and the spout knocking
        hollow against its stop over the hatch (a real metal hit rung through the pipe's resonances).""",
        sources=W.PIECES["scrap"] + W.PIECES["iron"], lufs=-22)
def spout_swing(rng, k):
    L = (1.6, 2.2, 1.9)[k]
    b = dsp.Bus(L + 1.5)
    cr = synth.creak(L, env([(0, 20), (L * 0.5, 70), (L, 25)], L), rng,
                     body=[f * rng.uniform(1.6, 2.2) for f in synth.IRON[:5]], q=30, jitter=0.3)
    b.at(0, norm(cr) * env([(0, 0), (0.2, 1), (L - 0.2, 0.7), (L, 0)], L)[:len(cr)] * 0.5)
    for i in range(int(L * 6)):
        b.at(rng.uniform(0, L), norm(W.piece(rng, "scrap", (-4, 0), tau=0.08)) * rng.uniform(0.1, 0.25))
    clank = W.piece(rng, "iron", (-4, -1), tau=0.3)
    b.at(L, norm(mix(norm(clank), norm(dsp.resonate(clank, SPOUT, q=25)) * 0.6)) * 0.8)
    return outdoors(b.x, rng, 0.15)


@recipe("place-grain", "spout-stop", "gate",
        "The pour cut off: the slide gate shut with a clank, the stream thinning to a trickle, the last grains pattering",
        """The spout's slide gate driven shut (a real metal clank and scrape), the grain stream thinning from a hiss to a
        dribble over a second (the pour model's flow falling away, the pipe's hollow ring fading with it), and the last
        few kernels pattering onto the heap.""", sources=W.PIECES["iron"], lufs=-22)
def spout_stop(rng, k):
    L = 2.4
    n = samples(L)
    flow = env([(0, 30000), (0.15, 25000), (0.6 + 0.2 * k, 1500), (1.2 + 0.2 * k, 120), (L, 2)], L, curve="exp")
    p = W.pour(n, rng, flow, grain=(1200, 9000), lump=0, thunder=0.4, loop=False, voices=32, decay=0.003)
    pipe = dsp.resonate(p, SPOUT, q=12)
    b = dsp.Bus(L + 1)
    b.at(0, norm(norm(p) * 0.7 + norm(pipe) * 0.3) * 0.8)
    m = samples(0.25)
    scr = W.friction(m, rng, [400, 900, 1700], rough=1.5, grit=0.5, q=15) * env([(0, 0), (0.05, 1), (0.25, 0)], 0.25)[:m]
    b.at(0.05, norm(scr) * 0.3)
    b.at(0.15, norm(W.piece(rng, "iron", (-3, 0), tau=0.2)) * 0.7)
    return outdoors(b.x, rng, 0.15)


# ---- Cranes and winches ---------------------------------------------------------------------------------------------------

@recipe("place-crane", "motor", "steam-winch",
        "The crane's motor running: a small steam winch engine chuffing fast, gears whining and clattering, the drum turning",
        """A foundry crane's steam winch: a little twin-cylinder engine beating fast (the blast-pipe model, smaller and
        quicker, eight beats a second), the spur gears' mesh whining at their tooth rate with the clatter of worn teeth
        (modelled), the rope drum rumbling, the whole frame rattling. 8 s exact cycle, the beat locked to it.""",
        loop=True, takes=1, lufs=-20)
def motor(rng, k):
    n = samples(8.0)
    t = np.arange(n) / SR
    beats = [(i / 8.0, norm(dsp.vari(exhaust(rng, False), 7)), 0.7 if i % 2 else 1.0) for i in range(64)]
    eng = W.place(n, beats)
    mesh = 2 * np.pi * 312 * t                                     # 24 teeth at 13 turns a second
    whine = (np.sin(mesh) + 0.5 * np.sin(2 * mesh) + 0.25 * np.sin(3 * mesh)) * (0.7 + 0.3 * np.sin(2 * np.pi * 13 * t))
    teeth = W.place(n, [(tt, W.body(rng, rng.uniform(1500, 3000), W.PLATE, decay=0.01, length=0.04, count=6), rng.uniform(0.2, 0.6))
                        for tt in np.arange(0, 8.0, 1 / 39.0)])
    drum = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(90)) / 0.7) ** 2))
    y = norm(eng) * 0.6 + norm(whine.astype(np.float32)) * 0.12 + norm(teeth) * 0.15 + norm(drum) * 0.3
    return seamless(circ_outdoors(y, rng, 0.2))


@recipe("place-crane", "chain", "links",
        "Chain paying out or hauling in: links clattering over the sprocket in a fast rhythm, the drum knocking, ringing",
        """Each link a real light metal hit (Kenney's, pitched for heavy chain), fourteen a second as they run over the
        sprocket, with the sprocket's teeth knocking every seventh link, the slack chain slapping, and the jib's iron
        ringing faintly with it. 8 s exact cycle.""", sources=W.PIECES["scrap"] + W.PIECES["iron"], loop=True, takes=1,
        lufs=-22)
def chain(rng, k):
    n = samples(8.0)
    ev = []
    for i in range(112):
        t = i / 14.0 + rng.normal(0, 0.004)
        ev.append((t, norm(W.piece(rng, "scrap", (-7, -3), tau=0.06)), rng.uniform(0.4, 0.8)))
        if i % 7 == 0:
            ev.append((t, norm(W.piece(rng, "iron", (-6, -3), tau=0.12)), 0.6))
    slaps = [(t, norm(W.piece(rng, "scrap", (-9, -6), tau=0.05)), 0.3) for t in W.poisson(8.0, 1.5, rng)]
    links = W.place(n, ev) + W.place(n, slaps)
    jib = W.cyclic(lambda z: dsp.resonate(z, [210, 540, 1020, 1630], q=60), links)
    y = norm(links) + norm(jib) * 0.15
    return seamless(circ_outdoors(y, rng, 0.2))


@recipe("place-crane", "load-swing", "hook",
        "A load swinging: the hook grinding in its shackle as it sways back and forth, the chain clinking at each turn",
        """The hook working in its shackle under the load's weight: an iron-on-iron creak (modelled stick-slip, high and
        tight) sweeping one way and back as the load swings with a pendulum's three-second period, and the chain's links
        settling with a clink at each end of the swing.""", sources=W.PIECES["scrap"], takes=3, lufs=-24)
def load_swing(rng, k):
    period = rng.uniform(2.6, 3.4)
    L = period * 1.1
    b = dsp.Bus(L + 1)
    for half in range(2):
        hl = period / 2 * 0.8
        cr = synth.creak(hl, env([(0, 30), (hl * 0.5, rng.uniform(80, 140)), (hl, 30)], hl), rng,
                         body=[f * rng.uniform(2.4, 3.2) for f in synth.IRON[:5]], q=35, jitter=0.35)
        b.at(half * period / 2, norm(cr) * env([(0, 0), (hl * 0.5, 1), (hl, 0)], hl)[:len(cr)] * (0.6, 0.45)[half])
        b.at(half * period / 2 + hl, norm(W.piece(rng, "scrap", (-6, -2), tau=0.1)) * 0.25)
    return outdoors(b.x, rng, 0.15)


def load_set(rng, mat):
    """A heavy load set down: the blow of its weight on the deck or the ground, then the chains going slack."""
    b = dsp.Bus(3.0)
    if mat == "wood":
        hit = mix(norm(W.piece(rng, "wood", (-6, -3))), norm(W.body(rng, rng.uniform(70, 95), W.PLATE, decay=0.25, contact=0.003))
                  * 0.5, W.knock(rng, 55, 0.4) * 0.7)
        b.at(0.0, norm(hit) * 0.9)
        b.at(0.3, norm(synth.creak(0.5, 40, rng, body=frame_body(rng, 0.7, False), q=14)) * 0.15)
    else:
        hit = mix(norm(W.rec(f"kenney_impact-sounds:impactSoft_heavy_00{rng.integers(5)}", semis=-3)),
                  norm(W.piece(rng, "brick", (-9, -6))) * 0.5, W.knock(rng, rng.uniform(38, 60), 0.5) * 0.6)
        b.at(0.0, norm(hit) * 0.9)
        b.at(0.02, norm(W.spray(rng, 0.3, 25)) * 0.3)
    t = 0.15
    for i in range(rng.integers(6, 11)):
        b.at(t, norm(W.piece(rng, "scrap", (-7, -3), tau=0.07)) * rng.uniform(0.15, 0.35))
        t += rng.uniform(0.04, 0.1)
    return outdoors(b.x, rng, 0.15)


LOADSET_HOW = """A crane setting a heavy casting or crate down: the blow of its weight (the packs' {what} pitched for the mass,
        with {body} and a deep knock), then the chain going slack in a run of link clinks (Kenney's light metal hits).
        Three takes."""


@recipe("place-crane", "load-set", "heavy", "A load set down on a wooden car deck: a heavy woody blow booming the car, the chain going slack",
        LOADSET_HOW.format(what="heavy wood hits", body="the car's deck booming"), sources=W.PIECES["wood"] + W.PIECES["scrap"],
        mat="wood", lufs=-24)
def load_set_wood(rng, k):
    return load_set(rng, "wood")


@recipe("place-crane", "load-set", "heavy", "A load set down on the ground: a deep earthy thud, grit crunching, the chain going slack",
        LOADSET_HOW.format(what="soft heavy hits and stone cracks", body="grit crunching under it"),
        sources=W.PIECES["brick"] + W.PIECES["scrap"] + [f"kenney_impact-sounds:impactSoft_heavy_00{i}" for i in range(5)],
        mat="ground", lufs=-24)
def load_set_ground(rng, k):
    return load_set(rng, "ground")


# ---- Wreck yard ------------------------------------------------------------------------------------------------------------

@recipe("place-wreck", "creak", "car",
        "A derailed car creaking: a big wooden car on its side groaning as its weight shifts, iron straps complaining",
        """Modelled stick-slip through a wrecked car's frame (low wooden members and iron straps) as its weight settles: long
        slow groans that catch and let go, with a little grit ticking down. Four takes, each a different joint (deeper,
        higher, more iron, a creak that ends in a small crack).""", sources=["sfx_100_v2:misc_35"], takes=4, lufs=-24)
def wreck_creak(rng, k):
    L = rng.uniform(1.6, 3.2)
    b = dsp.Bus(L + 1.5)
    scale = (0.35, 0.6, 0.45, 0.5)[k]
    c = synth.creak(L, env([(0, 8), (L * 0.6, rng.uniform(22, 40)), (L, 10)], L), rng,
                    body=frame_body(rng, scale, iron=k == 2), q=18, jitter=0.45)
    b.at(0, norm(c) * env([(0, 0), (L * 0.4, 1), (L, 0)], L)[:len(c)])
    for t in np.sort(rng.uniform(0.2, L, 3)):
        b.at(t, norm(W.piece(rng, "brick", (2, 6))) * 0.06)
    if k == 3:
        b.at(L * 0.8, norm(W.rec("sfx_100_v2:misc_35", semis=-3)) * 0.3)
    return W.space(b.x, rng, "night", wet=0.25)


@recipe("place-wreck", "shift", "slump",
        "Wreckage shifting: a sudden slump, sheet iron scraping and banging, wood cracking, bits sliding after",
        """A heap of wreck giving way under someone's weight or a pull: a sheet of iron scraping and dropping (modelled
        rubbing, a ringing sheet hit), timbers cracking (real wood cracks), a heavy blow as it lands, then small debris
        sliding and ticking down after. Three takes.""", sources=["sfx_100_v2:misc_34", "sfx_100_v2:misc_35"] + W.PIECES["iron"],
        takes=3, lufs=-24)
def wreck_shift(rng, k):
    b = dsp.Bus(4.0)
    n = samples(0.6)
    scr = W.friction(n, rng, [90, 160, 290, 470, 800, 1300], rough=2.2, grit=0.6, q=12) * env([(0, 0), (0.1, 1), (0.6, 0.6)], 0.6)[:n]
    b.at(0, norm(scr) * 0.5)
    b.at(0.55, norm(W.sheet(rng, f1=rng.uniform(55, 80), fmax=4000, decay=0.8, contact=0.0008)) * 0.6)
    b.at(0.55, norm(mix(W.piece(rng, "iron", (-7, -4), tau=0.4), W.knock(rng, 50, 0.4))) * 0.8)
    b.at(0.3 + 0.1 * k, norm(W.rec(("sfx_100_v2:misc_34", "sfx_100_v2:misc_35")[k % 2], semis=-rng.uniform(2, 5))) * 0.5)
    b.at(0.7, W.debris(rng, 1.5, 8, {"scrap": 2, "wood": 2, "brick": 1}, decay=0.5, semis=(-3, 3)) * 0.35)
    return W.space(b.x, rng, "night", wet=0.25)


@recipe("place-wreck", "cargo-pull", "drag",
        "Cargo dragged out of a wreck: crates scraping over boards and iron in hard pulls, catching, knocking, creaking",
        """A crate hauled out of a wrecked car in heaves: wood scraping on wood and iron (modelled rubbing through a crate's
        and the floor's modes), each pull catching and jerking free, the crate knocking on the doorframe and the wreck
        creaking under it (the packs' wood hits, modelled creaks), something loose inside the crate rattling. 10 s exact
        cycle.""", sources=W.PIECES["wood"], loop=True, takes=1, lufs=-22)
def cargo_pull(rng, k):
    n = samples(10.0)
    ev = []
    t = 0.0
    while t < 10.0 - 0.3:
        pl = rng.uniform(0.6, 1.1)
        m = samples(pl)
        scr = W.friction(m, rng, [f * 0.6 for f in synth.WOOD[:5]] + [700, 1200], rough=2.0, grit=0.5, q=10)
        jerk = np.clip(1 + 0.8 * lp(rng.standard_normal(m).astype(np.float32), 8) * 10, 0, 2)
        ev.append((t, scr * jerk * env([(0, 0), (0.06, 1), (pl - 0.1, 0.8), (pl, 0)], pl)[:m], 0.7))
        if rng.random() < 0.6:
            ev.append((t + pl * rng.uniform(0.3, 0.9), norm(W.piece(rng, "wood", (-6, -2))), 0.5))
        for j in range(rng.integers(0, 4)):
            ev.append((t + rng.uniform(0, pl), norm(W.piece(rng, "scrap", (0, 5), tau=0.05)), 0.12))
        t += pl + rng.uniform(0.4, 0.8)
    creaks = [(tt, norm(synth.creak(0.6, 35, rng, body=frame_body(rng, 0.5, False), q=16)), 0.25) for tt in W.poisson(10.0, 0.5, rng)]
    y = norm(W.place(n, ev)) + norm(W.place(n, creaks)) * 0.3
    return seamless(circ_outdoors(y, rng, 0.2))


# ---- Slaughterhouse -------------------------------------------------------------------------------------------------------

def hook_clink(rng):
    """Two meat hooks touching: small heavy steel chinks ringing in a tiled hall."""
    return mix(norm(W.piece(rng, "scrap", (-5, -1), tau=0.25)), norm(W.body(rng, rng.uniform(900, 1500), W.BAR, decay=0.4)) * 0.3)


@recipe("place-slaughterhouse", "inside", "hall",
        "Stand-in (animals): inside the slaughterhouse: hooks clinking on the rail, drips, penned animals, something moving",
        """A long stone killing floor. Empty hooks swaying on the overhead rail and clinking together (real metal hits with a
        steel ring), their chains creaking, blood and water dripping into the gutter, the drain gurgling, all in a long
        hard hall reverb; somewhere back in the pens, cattle and pigs shifting and calling (the synthesised livestock,
        stand-ins until recordings); and once in a while, far off in the building, something heavy dragged across the
        floor that nobody's moving. 16 s exact cycle.""", sources=W.PIECES["scrap"], loop=True, takes=1, lufs=-24)
def slaughter_inside(rng, k):
    n = samples(16.0)
    L = 16.0
    hooks = [(t, hook_clink(rng), rng.uniform(0.2, 0.7)) for t in W.poisson(L, 0.8, rng)]
    chains = [(t, norm(synth.creak(rng.uniform(0.4, 0.9), 50, rng, body=[f * 2.5 for f in synth.IRON[:4]], q=30)), 0.2)
              for t in W.poisson(L, 0.4, rng)]
    drips = [(t, W.drip(rng, "water", 0.8), rng.uniform(0.3, 0.8)) for t in W.poisson(L, 1.5, rng)]
    drain = W.cyclic(lambda z: lp(z, 1800), synth.bubbles(L, 40, 200, 900, rng))
    pens = [(t, moo(rng), 0.5) for t in rng.uniform(0, L, 2)] + [(t, grunt(rng), 0.4) for t in W.poisson(L, 1.0, rng)]
    pens_x = W.cyclic(lambda z: lp(z, 2500), W.place(n, pens) + hooves(rng, L, 1.5) * 0.5)
    m = samples(2.0)
    drag = W.friction(m, rng, [60, 110, 190, 300], rough=1.5, grit=0.2, q=6) * env([(0, 0), (0.6, 1), (1.4, 0.7), (2.0, 0)], 2.0)[:m]
    air = cfilter(pnoise(n, rng), lambda f: 1 / (1 + (f / 120) ** 2) * (f / 30) / (1 + f / 30))
    near = norm(W.place(n, hooks)) * 0.5 + norm(W.place(n, chains)) * 0.2 + norm(W.place(n, drips)) * 0.3 + norm(drain) * 0.1
    farx = norm(pens_x) * 0.35 + norm(W.place(n, [(rng.uniform(0, L), lp(drag, 600), 1.0)])) * 0.3
    y = croom(near, "hall", 0.45, rng) + croom(farx, "hall", 0.8, rng) + norm(air) * 0.15
    return seamless(y)


@recipe("place-slaughterhouse", "hook-chain", "rail",
        "Hooks and chains moving: a hook trolley rolling along the overhead rail, hooks swinging into each other, a hoist chain",
        """Three things the killing floor's iron does: a hook trolley rolling along the overhead rail (its little iron wheels
        rumbling and squeaking, modelled, with the hook clinking as it goes), a row of hanging hooks set swinging and
        knocking into one another (real metal hits ringing), and a hoist chain rattling through its block (link after link).
        In the hall's long reverb.""", sources=W.PIECES["scrap"], takes=3, lufs=-24)
def hook_chain(rng, k):
    b = dsp.Bus(4.0)
    if k == 0:
        L = 2.2
        n = samples(L)
        roll = W.friction(n, rng, [520, 1310, 2460], rough=1.2, grit=0.2, q=30)
        roll *= env([(0, 0), (0.2, 1), (L - 0.3, 1), (L, 0)], L)[:n]
        sq = W.squeal(n, rng, [1870], np.clip(slow(n, 2.0, rng), 0, 1) * env([(0, 0), (0.3, 1), (L - 0.3, 1), (L, 0)], L)[:n],
                      wander=0.02)
        b.at(0, norm(roll) * 0.4 + sq * 0.08)
        for t in np.sort(rng.uniform(0.1, L, 5)):
            b.at(t, hook_clink(rng) * 0.3)
        b.at(L - 0.1, norm(W.piece(rng, "iron", (-3, 0), tau=0.2)) * 0.5)
    elif k == 1:
        t = 0.0
        for i in range(9):
            b.at(t, hook_clink(rng) * rng.uniform(0.3, 0.8) * (1 - i / 12))
            t += rng.uniform(0.12, 0.4)
    else:
        for i in range(30):
            b.at(i / 16 + rng.normal(0, 0.004), norm(W.piece(rng, "scrap", (-5, -2), tau=0.06)) * rng.uniform(0.3, 0.6))
        b.at(1.9, norm(W.piece(rng, "iron", (-4, -1), tau=0.25)) * 0.6)
    return dsp.room(b.x, "hall", 0.4, rng=rng)


# ---- Dead towns ------------------------------------------------------------------------------------------------------------

@recipe("place-villages", "dead-town", "street",
        "A dead town at night: wind down empty streets, whistling through gaps, leaves skittering, a loose thing tapping",
        """An empty street at night, built from the wind model at a breeze (6 m/s, gusting) whistling thinly through the
        gaps between houses, dry leaves skittering along the cobbles (modelled rustle with ticks), a loose shutter knocking
        softly somewhere up the street and a sign creaking on its rings far off (the packs' wood hits, modelled creaks),
        all bounced off close walls. Quiet enough to hear what sleeps there. 16 s exact cycle.""",
        sources=W.PIECES["wood"], loop=True, takes=1, lufs=-26)
def dead_town(rng, k):
    n = samples(16.0)
    L = 16.0
    wind, _ = W.wind(n, rng, 6.0, gust=0.22, gust_rate=0.12, buffet=0.1, whistle=0.5, fittings=[0.004, 0.012, 0.03])
    leaves = W.cyclic(lambda z: z, synth.rustle(L, 90, rng, f=(2000, 8000), ticks=0.8)) * np.clip(0.4 + slow(n, 0.15, rng), 0, None)
    taps = [(t, norm(W.piece(rng, "wood", (-2, 2))), rng.uniform(0.1, 0.3)) for t in W.poisson(L, 0.4, rng)]
    sign = [(t, norm(synth.creak(0.7, 60, rng, body=[f * 3 for f in synth.IRON[:4]], q=30)), 0.15) for t in W.poisson(L, 0.25, rng)]
    y = norm(wind) * 0.7 + norm(leaves) * 0.2 + cfilter(norm(W.place(n, taps)) + norm(W.place(n, sign)) * 0.6,
                                                     lambda f: 1 / (1 + (f / 3000) ** 2)) * 0.4
    return seamless(croom(y + 0.3 * np.roll(y, samples(0.035)), "night", 0.3, rng))


@recipe("place-villages", "shutter", "bang",
        "A shutter banging: a wooden shutter blown against its frame, slats rattling, bouncing back",
        """A loose wooden shutter caught by the wind and slammed against its frame: the packs' real door slam and heavy wood
        hits for the blow, its slats rattling and the iron hinge clacking after (light wood and metal hits), a bounce.
        Three takes: one hard bang, one with a bounce, one a double slam as the wind catches it twice.""",
        sources=["sfx_100_v2:door_03", "sfx_100_v2:wood_hit_02"] + W.PIECES["wood"] + W.PIECES["scrap"], lufs=-24)
def shutter(rng, k):
    b = dsp.Bus(3.0)

    def bang(g):
        x = mix(norm(W.rec(("sfx_100_v2:door_03", "sfx_100_v2:wood_hit_02")[int(rng.integers(2))], semis=rng.uniform(1, 3))),
                norm(W.piece(rng, "wood", (0, 3))) * 0.6)
        slats = dsp.Bus(0.5)
        for i in range(4):
            slats.at(0.03 + 0.025 * i, norm(W.rec(f"kenney_impact-sounds:impactWood_light_00{i}", semis=4)) * 0.15)
        return mix(x, slats.x, np.concatenate([np.zeros(samples(0.05)), norm(W.piece(rng, "scrap", (2, 5), tau=0.05)) * 0.15])) * g

    b.at(0, bang(1.0))
    if k >= 1:
        b.at(rng.uniform(0.18, 0.28), bang(0.35))
    if k == 2:
        b.at(rng.uniform(0.9, 1.3), bang(0.8))
    return W.space(b.x, rng, "night", wet=0.2, early=((0.012, 0.3), (0.03, 0.2)))


@recipe("place-villages", "sign", "rings",
        "A sign creaking: a hanging board swinging on its iron rings, squealing one way and back",
        """A shop sign swinging from an iron bracket: its rings grinding on the hooks (modelled iron-on-iron stick-slip,
        high and squeaky, rising and falling as the swing speeds through the middle) one way and back, and the board
        knocking faintly at the end of the swing. Three takes, different rings and winds.""",
        sources=W.PIECES["wood"], lufs=-26)
def sign(rng, k):
    period = rng.uniform(1.6, 2.4)
    b = dsp.Bus(period * 1.2 + 1)
    for half in range(2):
        hl = period / 2 * 0.85
        cr = synth.creak(hl, env([(0, 25), (hl * 0.5, rng.uniform(90, 160)), (hl, 25)], hl), rng,
                         body=[f * rng.uniform(3.5, 5) for f in synth.IRON[:4]], q=40, jitter=0.4)
        b.at(half * period / 2, norm(cr) * env([(0, 0), (hl * 0.5, 1), (hl, 0)], hl)[:len(cr)] * (0.7, 0.55)[half])
    b.at(period * 0.45, norm(W.piece(rng, "wood", (2, 5))) * 0.15)
    return W.space(b.x, rng, "night", wet=0.25, early=((0.012, 0.3), (0.03, 0.2)))


# ---- Mine head ------------------------------------------------------------------------------------------------------------

@recipe("place-mine", "underground", "adit",
        "Underground at the mine head: the air's low moan through the drift, water trickling, stone ticking, pressing quiet",
        """The space closing in: a low moan of air drawn through the adit (a narrow band of low noise, slowly breathing),
        water trickling somewhere in the dark (a stream of small low bubbles), grit and small stones ticking down, a far
        rock settling with a thud now and then, a prop creaking far back, all in a tight, hard stone reverb that gives
        nothing back from far away. 16 s exact cycle.""", sources=W.PIECES["brick"], loop=True, takes=1, lufs=-26)
def underground(rng, k):
    n = samples(16.0)
    L = 16.0
    moan = cfilter(pnoise(n, rng), lambda f: np.exp(-0.5 * ((np.log2(f) - np.log2(75)) / 0.45) ** 2))
    moan *= np.clip(0.7 + 0.3 * slow(n, 0.1, rng), 0.2, None)
    trickle = W.cyclic(lambda z: lp(z, 2200), synth.bubbles(L, 60, 250, 1200, rng, rise=(0.1, 0.4)))
    ticks = [(t, norm(W.piece(rng, "brick", (3, 8))), rng.uniform(0.05, 0.2)) for t in W.poisson(L, 1.0, rng)]
    thuds = [(t, norm(mix(W.knock(rng, 50, 0.5), W.piece(rng, "brick", (-9, -6)) * 0.4)), 0.4) for t in W.poisson(L, 0.12, rng)]
    prop = [(rng.uniform(0, L), norm(synth.creak(1.2, env([(0, 10), (0.6, 25), (1.2, 10)], 1.2), rng,
                                                 body=frame_body(rng, 0.4, False), q=14)), 0.2)]
    y = norm(moan) * 0.4 + norm(trickle) * 0.15 + norm(W.place(n, ticks)) * 0.25 + norm(W.place(n, thuds)) * 0.3 \
        + norm(W.place(n, prop)) * 0.2
    return seamless(croom(y, "stone", 0.4, rng))


@recipe("place-mine", "drip", "pool",
        "Water dripping in the mine: drops into pools in the rock, ringing off close stone",
        """Each drop modelled as it lands: the splash's tick and the entrained bubble's rising ring (the 'plip'), sizes
        varying; one hits bare rock. Rung through a tight stone reverb (closer and harder than the tunnel's brick).""",
        takes=4, lufs=-30)
def mine_drip(rng, k):
    return dsp.room(W.drip(rng, "stone" if k == 2 else "water", size=(1.2, 0.8, 1.0, 1.6)[k]), "stone", 0.5, rng=rng)


@recipe("place-mine", "timber", "props",
        "Pit props creaking: round timbers groaning under the rock's weight, a split cracking, grit trickling down",
        """Modelled stick-slip through big round timbers under load: deep slow groans that tighten and catch; in two takes a
        split cracks in the wood (the packs' real wood crack, pitched down) and grit and pebbles trickle down from the
        roof after (the pour model at a dribble). Tight stone reverb.""", sources=["sfx_100_v2:misc_35"] + W.PIECES["brick"],
        takes=3, lufs=-26)
def mine_timber(rng, k):
    L = rng.uniform(1.8, 2.8)
    b = dsp.Bus(L + 2)
    c = synth.creak(L, env([(0, 6), (L * 0.6, 20), (L, 8)], L), rng, body=frame_body(rng, 0.3, False), q=16, jitter=0.5)
    b.at(0, norm(c) * env([(0, 0), (L * 0.5, 1), (L, 0)], L)[:len(c)])
    if k > 0:
        b.at(L * 0.7, norm(W.rec("sfx_100_v2:misc_35", semis=-rng.uniform(5, 8))) * 0.4)
        m = samples(1.4)
        g = W.pour(m, rng, env([(0, 10), (0.2, 300), (1.4, 5)], 1.4), grain=(2000, 7000), lump=1, thunder=0.0, loop=False)
        b.at(L * 0.75, norm(g) * 0.2)
    return dsp.room(b.x, "stone", 0.4, rng=rng)


# ---- Chemical works -------------------------------------------------------------------------------------------------------

@recipe("place-chemical", "leak", "pipes",
        "A leak hissing in the works: gas jetting from a cracked pipe in uneven spurts, a second leak whistling thinly",
        """Outdoors among the works' pipes. The modelled jet through a crack (higher and thinner than steam from a valve, no
        plume), its pressure spurting unevenly as the line surges; a second, smaller leak whistling at a gap (a narrow
        unsteady tone over its hiss); the pipe it's in ringing faintly. 12 s exact cycle.""", loop=True, takes=1, lufs=-24)
def leak(rng, k):
    n = samples(12.0)
    spurt = np.clip(0.75 + 0.3 * slow(n, 0.7, rng) + 0.1 * slow(n, 6, rng), 0.3, None)
    a = W.jet(12.0, rng, n=n, pressure=spurt, peak=2600, low=0.2, eddy=1.2)
    b_ = W.jet(12.0, rng, n=n, peak=5200, opening=0.5, low=0.0, eddy=1.4) + W.howl(n, rng, [3900], 0.3, wander=0.02, rate=0.4)
    pipe = W.cyclic(lambda z: dsp.resonate(z, [410, 1130, 2210], q=40), a)
    y = norm(a) * 0.7 + norm(b_) * 0.25 + norm(pipe) * 0.08
    return seamless(circ_outdoors(y, rng, 0.2))


@recipe("place-chemical", "drip", "vat",
        "Something dripping: a thick drop plopping into a pool, sizzling faintly where it lands",
        """Thick drops modelled landing in a pool of something (the splash and a low bubble ring, slower and heavier than
        water), and where it reacts, a faint short sizzle dying away (fine crackle, the one place in these lines a fizz
        belongs). Three takes: a plain plop, a plop that sizzles, a drop on iron that sizzles.""", takes=3, lufs=-30)
def chem_drip(rng, k):
    if k == 2:
        d = W.drip(rng, "iron", 1.0)
    else:
        d = W.drip(rng, "water", size=2.0)
    if k > 0:
        fz = synth.crackle(0.6, env([(0, 2500), (0.6, 50)], 0.6), rng, size=(0.0001, 0.0005), hi=3500)
        d = mix(d, np.concatenate([np.zeros(samples(0.01)), norm(fz) * 0.25]))
    return dsp.room(d, "hall", 0.3, rng=rng)


# ---- Holdout breach -------------------------------------------------------------------------------------------------------

def blow(rng, last=False):
    """A sledge on a padlock and hasp on a plank door: steel on steel, the door booming, the lock jumping on its staple."""
    x = mix(norm(W.piece(rng, "iron", (-4, -1), tau=0.3)), norm(W.rec("sfx_100_v2:metal_hit_01", semis=-rng.uniform(1, 3))) * 0.6,
            norm(W.piece(rng, "wood", (-4, -1))) * 0.7, W.knock(rng, 75, 0.3) * 0.5)
    rattle = norm(W.piece(rng, "scrap", (0, 4), tau=0.08)) * 0.25
    x = mix(x, np.concatenate([np.zeros(samples(0.05)), rattle]))
    if last:
        x = mix(x, norm(W.splinter(0.25, rng, env([(0, 500), (0.25, 60)], 0.25))) * 0.4,
                np.concatenate([np.zeros(samples(0.35)), norm(W.piece(rng, "scrap", (-3, 0), tau=0.2)) * 0.5]),
                np.concatenate([np.zeros(samples(0.5)), norm(W.piece(rng, "scrap", (-1, 2), tau=0.1)) * 0.3]))
    return x


# The breach on its beats (queue #234, note 497; C1's #200, note 464): the lock jumps at each blow of the crew's smash clip
# (a blow every 0.8 s) and each board flexes out on the pry clip's heave (every 1.33 s) and comes off at each fifth of the
# breach. The smash was three seconds of blows played every half second (a din of them over one another), the pry a held
# loop heaving on its own; now each blow and each heave is its own sound, played on the clip's beat, and each board torn
# off as it goes.

@recipe("place-breach", "smash", "blow",
        "One sledge blow on a padlock and hasp: steel on steel, the plank door booming, the lock jumping on its staple",
        """One blow, played on each blow of the crew's smash clip as the lock jumps on its hasp (C1's #200): steel on steel
        (the packs' heavy metal hits and metal door hit) with the plank door booming under it (heavy wood hits, a deep
        knock) and the lock rattling on its staple after. Loud as a cannon, as D.7 has it. Five takes, swung differently.""",
        sources=W.PIECES["iron"] + W.PIECES["wood"] + W.PIECES["scrap"], takes=5, lufs=-20)
def smash(rng, k):
    return yard(blow(rng) * (0.85 + 0.15 * rng.random()), rng, 0.25)


@recipe("place-breach", "smash-give", "tear",
        "The last blow: the hasp torn out of the splintering wood and the lock clattering down",
        """The blow that breaks it: the same steel on steel and the door booming, the staple tearing out of the splintering
        plank (modelled splintering) and the lock and hasp clattering down onto the step and the ballast (the packs'
        scrap). Two takes.""", sources=W.PIECES["iron"] + W.PIECES["wood"] + W.PIECES["scrap"], takes=2, lufs=-20)
def smash_give(rng, k):
    return yard(blow(rng, last=True), rng, 0.25)


PRY_ON, PRY_HELD, PRY_OFF = 14 / 30, 22 / 30, 32 / 30     # the pry clip's heave (crew_clips.py, 30 fps): hauled back, held, eased


@recipe("place-breach", "pry", "heave",
        "One heave on the bar: a board flexing out on its nails, the nails squealing, the wood creaking and splitting",
        """One heave, played on each heave of the crew's pry clip as the board being worked flexes out (C1's #200): the bar
        seated under the board with an iron knock, the board bending as it's hauled back (modelled creak through a plank's
        modes, rising with the strain), its nails squealing as they draw (stick-slip through a nail's ring, shrill) and a
        crack of splitting wood at the full heave (the packs' real wood cracks), then the board easing back with a groan.
        Loud as machinery. Four takes.""", sources=["sfx_100_v2:misc_35", "kenney_rpg-audio:chop"] + W.PIECES["iron"],
        takes=4, lufs=-22)
def pry(rng, k):
    b = dsp.Bus(1.8)
    b.at(0, norm(W.piece(rng, "iron", (-3, 0), tau=0.15)) * 0.5)                     # the bar seated
    hl = PRY_ON + 0.05
    groan = synth.creak(hl, env([(0, 15), (hl, 60)], hl), rng, body=frame_body(rng, 0.9, False), q=12, jitter=0.5)
    b.at(0.04, norm(groan) * env([(0, 0), (hl * 0.7, 1), (hl, 0.7)], hl)[:len(groan)] * 0.5)
    nl = PRY_HELD - PRY_ON * 0.6
    nail = synth.creak(nl, env([(0, 200), (nl, rng.uniform(500, 900))], nl), rng,
                       body=[rng.uniform(1800, 2600) * r for r in (1, 2.76, 5.4)], q=50, jitter=0.15)
    b.at(PRY_ON * 0.6, norm(nail) * env([(0, 0), (0.05, 1), (nl, 0.3)], nl)[:len(nail)] * 0.4)
    b.at(PRY_ON + rng.uniform(-0.02, 0.03), norm(W.rec(("sfx_100_v2:misc_35", "kenney_rpg-audio:chop")[k % 2],
                                                         semis=-rng.uniform(1, 4))) * 0.5)
    el = PRY_OFF - PRY_HELD
    ease = synth.creak(el, env([(0, 50), (el, 18)], el), rng, body=frame_body(rng, 0.9, False), q=12, jitter=0.5)
    b.at(PRY_HELD, norm(ease) * env([(0, 0.6), (el, 0)], el)[:len(ease)] * 0.3)
    return yard(b.x, rng, 0.25)


@recipe("place-breach", "board", "torn",
        "A board torn off a barricade: its last nails shrieking out, the board wrenched free and clattering down",
        """At each fifth of the breach a board comes away (C1's #200: the chest's, then above, below, the top, the bottom):
        the board's last nails shrieking out of the jamb (stick-slip through a nail's ring, rising), the wood splitting
        where it held (the packs' real crack), the board wrenched free and dropping onto the step, clattering and rolling
        (heavy and plank wood hits), a nail pinging off. Three takes.""",
        sources=["sfx_100_v2:misc_34", "sfx_100_v2:misc_35"] + W.PIECES["wood"] + W.PIECES["iron"], takes=3, lufs=-22)
def board(rng, k):
    b = dsp.Bus(2.4)
    nl = rng.uniform(0.25, 0.4)
    nail = synth.creak(nl, env([(0, 300), (nl, rng.uniform(900, 1400))], nl), rng,
                       body=[rng.uniform(1900, 2700) * r for r in (1, 2.76, 5.4)], q=50, jitter=0.2)
    b.at(0, norm(nail) * env([(0, 0), (0.03, 1), (nl, 0.4)], nl)[:len(nail)] * 0.45)
    b.at(nl * 0.7, norm(W.rec(("sfx_100_v2:misc_34", "sfx_100_v2:misc_35")[k % 2], semis=-rng.uniform(1, 3))) * 0.6)
    b.at(nl * 0.8, norm(W.body(rng, rng.uniform(2500, 3800), W.BAR, decay=0.12, contact=0.0001)) * 0.15)
    t = nl + rng.uniform(0.3, 0.4)
    for i in range(int(rng.integers(2, 4))):
        b.at(t, norm(W.piece(rng, "wood", (-4, 1))) * rng.uniform(0.5, 0.9) * (0.7 ** i))
        t += rng.uniform(0.09, 0.22)
    return yard(b.x, rng, 0.25)


@recipe("place-breach", "pry-give", "boards",
        "The barricade giving way: boards splitting and tearing off, nails pinging out, planks crashing down",
        """The last heave: a board splits along its length (modelled splintering speeding up, the packs' real wood cracks
        and breaks), its nails shriek and ping out, and the planks crash down and clatter on the threshold (heavy and
        plank wood hits), the bar ringing as it drops. Two takes.""",
        sources=["sfx_100_v2:misc_34", "sfx_100_v2:misc_35"] + W.PIECES["wood"] + W.PIECES["iron"], takes=2, lufs=-22)
def pry_give(rng, k):
    b = dsp.Bus(4.0)
    b.at(0, norm(W.splinter(0.6, rng, env([(0, 40), (0.5, 500), (0.6, 900)], 0.6))) * 0.6)
    b.at(0.5, norm(W.rec(("sfx_100_v2:misc_34", "sfx_100_v2:misc_35")[k], semis=-2)) * 0.8)
    for t in (0.55, 0.62, 0.75):
        b.at(t, norm(W.body(rng, rng.uniform(2500, 3800), W.BAR, decay=0.12, contact=0.0001)) * 0.2)
    t = 0.8
    for i in range(rng.integers(4, 7)):
        b.at(t, norm(W.piece(rng, "wood", (-4, 1))) * rng.uniform(0.5, 0.9))
        t += rng.uniform(0.08, 0.3)
    b.at(t + 0.1, norm(W.piece(rng, "iron", (-2, 1), tau=0.4)) * 0.4)
    return yard(b.x, rng, 0.25)
