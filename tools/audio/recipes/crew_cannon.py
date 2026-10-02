"""The gun car's cannon (GDD §8: a crude cast-iron muzzle-loader on a wooden carriage that runs on rails across the car's
roof): the shot and the gun bucking on its mount (crew-cannon-fire), and the three held reload steps, powder, ball and
ram (crew-cannon-reload, crew-cannon-ball, crew-cannon-ram).

The director on the last shot: "At the gun they sound massive." A black-powder gun fired a few feet away isn't a sound so
much as a blow: a shock front too fast and loud for any microphone (a clipped, broadband crack), a huge pressure thump
under it that you feel more than hear, the muzzle blast's roar, the whole carriage slamming back on its rails, and then
the land throwing it back as a rolling echo for seconds. The packs have no cannon, so the close shot is a stand-in built
from that physics (a shock wave, a sub thump, a roar of noise, all driven into saturation) with the packs' real thunder
for the roll and their real iron and wood for the gun bucking. The reload steps are iron and wood and serge: a cloth bag
pushed down a bore, an iron ball dropped in, a wooden rammer, all inside a two-metre iron tube that rings at its own
pitches (its hollow, 300/600/900 Hz).
"""

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import R, S, K
from recipes.crew_items import hit_of
from recipes.crew_train import iron, rolling

THUNDER = S("thunder_01")
MINING = K("impactMining")
PLATE_H, METAL_H = K("impactPlate_heavy"), K("impactMetal_heavy")
BORE = [300, 600, 900, 1210]


def bore(x, wet=0.35, q=18):
    """Inside the gun's bore: a 2 m iron tube, ringing at its own pitches."""
    return ck.norm(mix(x * (1 - wet), ck.norm(dsp.resonate(x, BORE, q=q, gains=[1, 0.7, 0.45, 0.3])) * wet))


def blast(rng, take):
    """The report at the gun (stand-in): a shock wave (an instant rise and a fast fall: a clipped, broadband crack), the
    muzzle's roar (a burst of noise whose band closes from the top down over a third of a second), the boom of the gas
    in the air (dark noise, slower), and the pressure thump under it all (a sub falling from ~55 Hz), driven into
    saturation the way a mic and an ear are at a few feet."""
    L = 0.9
    n = samples(L)
    t = np.arange(n) / dsp.SR
    shock = np.zeros(n, np.float32)
    k = samples(0.0015)
    shock[:k] = np.linspace(1, -0.6, k)
    shock = mix(shock, synth.noise(L, rng) * np.exp(-t / 0.012).astype(np.float32))
    roar = dsp.sweep_filter(synth.noise(L, rng, "pink"), "lp", env([(0, 12000), (0.06, 4000), (0.3, 600), (L, 200)], L, "exp"), q=0.7)
    roar = ck.norm(roar) * np.exp(-t / 0.16).astype(np.float32)
    boom = ck.norm(lp(synth.noise(L, rng, "brown"), 380, 2)) * env([(0, 0), (0.01, 1), (L, 0)], L, "exp") ** 0.7
    thump = synth.thump(rng.uniform(48, 58), 0.6, drop=0.5)
    crack = hit_of("kenney_rpg-audio:bookPlace2", 1, 0.2)
    y = mix(shock * 0.9, roar * 1.6, boom * 1.0, thump * 0.55, dsp.vari(crack, -12) * 0.4)
    return dsp.saturate(ck.norm(y), 14 + 3 * take)


def buck(rng, take, force=1.0):
    """The carriage slamming back on its roof rails: a heavy iron-and-wood thud, the trucks' wheels rumbling back, the
    breeching rope snapping taut, the roof booming under it."""
    thud = mix(iron(rng, take, -9, 0.08, 0.3), ck.floor(rng, "wood", take, 3.0 * force, 0.9))
    roll = rolling(rng, 0.45) * env([(0, 1), (0.45, 0)], 0.45)
    rope = synth.creak(0.15, 60, rng, body=[110, 240, 400], q=6) * env([(0, 1), (0.15, 0)], 0.15)
    boom = ck.hollow(rng, [70, 110, 170], 0.4, q=5)
    return ck.place([(0, thud, 0), (0, boom, -2), (0.03, roll, -6), (0.38, rope, -10),
                     (0.4, iron(rng, take + 1, -7, 0.04, 0.2), -10)])


def echo(rng, take, length=4.5):
    """The land throwing the shot back: the packs' real thunder roll, darkened, with two slap echoes off near ground."""
    th = ck.get(THUNDER[0])
    a = samples(rng.uniform(0.7, 1.0))
    roll = lp(dsp.fit(th[a:a + samples(length)], samples(length)), 900)
    roll = ck.norm(roll) * env([(0, 0), (0.15, 1), (length * 0.5, 0.5), (length, 0)], length)
    return roll


def shot(rng, take):
    b = blast(rng, take)
    y = ck.place([(0, b, 0), (0.02, buck(rng, take), -6), (0.25, echo(rng, take), -9),
                  (0.32 + 0.05 * take, lp(b, 1200), -18), (0.7 + 0.1 * take, lp(b, 700), -24)])
    return dsp.room(y, "night", wet=0.3, rng=np.random.default_rng(21))


def fire_line():
    L = "crew-cannon-fire"

    @recipe(L, "shot-close", "blast", "Stand-in: the shot at the gun: a clipped blast, a huge thump, the gun bucking, the land rolling it back",
            """Stand-in until the Sonniss library (no cannon in the packs), built from the physics of a muzzle blast a few
            feet away: a shock wave (instant rise, clipped broadband crack), a pressure thump you feel (a sub falling
            from 55 Hz), the muzzle's roar (noise whose band closes over a third of a second), all saturated like an ear
            at the gun; then the carriage slamming back on its rails (the packs' heavy plate and iron, a wooden knock,
            the trucks rumbling, the rope snapping taut, the roof booming), and the land rolling it back (the packs' real
            thunder roll darkened, two slap echoes, a long night tail).""",
            sources=THUNDER + PLATE_H + METAL_H + ck.WOOD_KNOCKS + R("bookPlace2"), takes=3, lufs=-11)
    def close(rng, k):
        return shot(rng, k)

    @recipe(L, "shot-far", "blast", "Stand-in: the shot from down the train and beyond: the boom and the roll, the crack gone",
            """The close shot put 250-350 m away (as the kept far take did): the air takes the crack and the top off, so
            what's left is the boom and the land's long roll, mostly reverb.""",
            sources=THUNDER + PLATE_H + METAL_H, takes=3, lufs=-18)
    def far(rng, k):
        return dsp.distance(shot(rng, k), rng.uniform(250, 350), rng)

    @recipe(L, "ignite", "flash", "The linstock to the touch hole: the priming flashing off",
            """The linstock's iron tip knocked on the vent (a small iron tick) and the priming powder flashing: a short,
            soft 'ffft' of burning powder (a burst of noise whose band falls as it burns out in a tenth of a second), with
            a little crackle. No fizz.""", sources=METAL_H, takes=2, lufs=-24)
    def ignite(rng, k):
        L_ = 0.16
        f = dsp.sweep_filter(synth.noise(L_, rng, "pink"), "bp", env([(0, 5000), (L_, 900)], L_, "exp"), q=0.8)
        f = ck.norm(f) * env([(0, 0), (0.008, 1), (L_, 0)], L_) ** 1.5
        tickx = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[k]), 6))), 0.002, 0.01)
        return ck.place([(0, tickx, -10), (0.04, f, 0), (0.05, synth.crackle(0.12, 60, rng), -14)])

    @recipe(L, "recoil", "carriage", "The carriage bucking back on its mount",
            """The gun's wooden carriage thrown back along its roof rails: a heavy iron-and-wood slam (the packs' heavy plate
            and iron pitched well down, a wooden knock), the trucks' wheels rumbling back, the breeching rope snapping
            taut and the carriage's last clank against it, with the roof tin booming under it all.""",
            sources=PLATE_H + METAL_H + ck.WOOD_KNOCKS, takes=2, lufs=-15)
    def recoil(rng, k):
        return buck(rng, k, 1.2)

    @recipe(L, "traverse", "rails", "The gun slid along its roof rails, held",
            """The carriage pushed along its rails: iron wheels grinding and rumbling on iron rails (low stick-slip and the
            wheels' rumble), the carriage's timbers creaking under the gun's weight, the roof tin booming faintly under
            the wheels. One 8 s cycle.""", loop=True, takes=1, lufs=-21)
    def traverse(rng, k):
        L_ = 8.3
        r = rolling(rng, L_)
        cr = synth.creak(L_, 14, rng, body=synth.WOOD, q=14) * 0.4
        boom = lp(synth.noise(L_, rng, "brown"), 140) * 0.5
        return dsp.wrap(mix(r, ck.norm(cr) * 0.25, ck.norm(boom) * 0.4), samples(L_))

    @recipe(L, "traverse-stop", "rails", "The gun stopped on its rails",
            """The carriage brought up against its chocks: a heavy wooden-and-iron clunk, the gun's weight rocking once
            and the roof tin booming under it.""", sources=PLATE_H + METAL_H + ck.WOOD_KNOCKS, takes=3, lufs=-19)
    def stop(rng, k):
        return ck.place([(0, iron(rng, k + 2, -8, 0.05, 0.3), 0), (0, ck.floor(rng, "wood", k, 2.0, 0.8), -3),
                         (0, ck.hollow(rng, [70, 110, 170], 0.3, q=5), -6), (0.14, iron(rng, k + 3, -10, 0.03, 0.2), -14)])


def reload_lines():
    @recipe("crew-cannon-reload", "powder", "bag", "The powder charge pushed down the muzzle, for the 1.5 s hold",
            """The serge cartridge bag shoved down the bore by hand and arm: wool rubbing on iron (a soft, dark friction in
            strokes), rung through the bore's hollow (300/600/900 Hz), and the coat sleeve going in with it. Continuous
            strokes from the first instant, so it can stop clean whenever the hold is let go. One 8 s cycle.""",
            sources=R("cloth1", "cloth2", "cloth3", "cloth4"), loop=True, takes=1, lufs=-22)
    def powder(rng, k):
        L_ = 8.3
        n = samples(L_)
        shape = np.zeros(n, np.float32)
        t = 0.0
        while t < L_:
            d = rng.uniform(0.45, 0.6)
            e = env([(0, 0.25), (0.06, 1), (d * 0.8, 0.8), (d, 0.25)], d)
            a = samples(t)
            shape[a:a + len(e)] = e[:n - a]
            t += d
        rub = ck.friction(rng, L_, 70, 200, 2500, 0.8, shape)
        sleeve = mix(*[ck.place([(t0, ck.cloth(rng, i, 0.3), 0)], L_) for i, t0 in enumerate(np.arange(0, L_, 1.1))])
        return bore(mix(rub, sleeve * 0.35), 0.4)

    @recipe("crew-cannon-reload", "powder-done", "bag", "The charge seated at the breech",
            """The bag arriving at the bottom of the bore: a soft, dull thump (a muffled pad of serge and powder) rung
            through the bore's hollow, and the arm drawn back out with a brush of cloth.""",
            sources=R("cloth2", "cloth4"), takes=3, lufs=-21)
    def powder_done(rng, k):
        thump = mix(ck.pad(rng, 0.12, 260), ck.hollow(rng, [150, 300, 600], 0.25, q=8) * 0.5)
        return ck.place([(0, bore(thump, 0.5), 0), (0.15, ck.cloth(rng, k + 1, 0.25), -10)])

    @recipe("crew-cannon-ball", "ball-in", "ball", "The iron ball dropped into the muzzle",
            """The iron shot set on the muzzle and let go: a heavy iron clonk on the lip (the packs' heavy metal and plate,
            pitched down to a cannonball and choked), the bore's hollow answering, and the first turn of it rolling.""",
            sources=METAL_H + PLATE_H, takes=2, lufs=-18)
    def ball_in(rng, k):
        clonk = iron(rng, k, -4, 0.05, 0.6)
        roll = ball_roll(rng, 0.25) * env([(0, 1), (0.25, 0)], 0.25)
        return ck.place([(0, bore(clonk, 0.3), 0), (0.06, roll, -8)])

    @recipe("crew-cannon-ball", "ball-roll", "ball", "The ball rolling down the bore, iron on iron, and stopping on the charge",
            """Iron on iron: the ball rumbling down the bore and gathering speed (a gritty rolling rumble whose rate
            climbs, rung through the bore's 300/600/900 Hz), and stopping dead on the powder bag with a dull, cushioned
            clunk at the breech (iron muffled by serge).""", sources=METAL_H + PLATE_H, takes=2, lufs=-19)
    def roll(rng, k):
        L_ = rng.uniform(0.8, 1.0)
        r = ball_roll(rng, L_) * env([(0, 0.4), (L_ * 0.8, 1), (L_, 0.9)], L_)
        stopx = mix(ck.pad(rng, 0.15, 400), lp(iron(rng, k + 1, -6, 0.03, 0.2), 1500) * 0.6)
        return ck.place([(0, r, 0), (L_, bore(stopx, 0.4), 2)])

    @recipe("crew-cannon-ram", "ram", "rammer", "Rammer strokes driving the shot home, for the 1.5 s hold",
            """The wooden rammer worked down the bore in short strokes: its head scraping the iron (a dark wooden
            friction rung through the bore's hollow) and a knock each time it meets the ball (a wooden knock, muffled
            inside the tube). Strokes from the first instant, so it stops clean when the hold is let go. One 8 s cycle.""",
            sources=ck.WOOD_KNOCKS, loop=True, takes=1, lufs=-20)
    def ram(rng, k):
        L_ = 8.3
        n = samples(L_)
        parts, t = [], 0.0
        while t < L_:
            d = rng.uniform(0.5, 0.62)
            sc = ck.friction(rng, d * 0.6, 90, 250, 2500, 0.7, env([(0, 0), (0.05, 1), (d * 0.6, 0.6)], d * 0.6))
            parts += [(t, sc, -4), (t + d * 0.6, lp(ck.floor(rng, "wood", int(t * 10), 1.4, 0.8), 2500), 0)]
            t += d
        return dsp.wrap(bore(ck.place(parts, L_ + 0.6), 0.4), n)

    @recipe("crew-cannon-ram", "ram-home", "rammer", "Rammed home: two hard strokes, the second solid: the gun is ready",
            """The gun's ready confirmation, built to cut through the train: two strokes of the rammer on the seated shot,
            the second harder (a heavy wooden knock with a heavy plate under it, pitched down, rung through the bore so the
            mids carry over the bed), then the rammer drawn out with a short scrape.""",
            sources=ck.WOOD_KNOCKS + PLATE_H, takes=2, lufs=-14)
    def ram_home(rng, k):
        def stroke(force, i):
            return bore(mix(ck.floor(rng, "wood", k + i, 2.0 * force, 1.0), iron(rng, k + i, -8, 0.04, 0.2) * 0.5 * force), 0.35)
        out = ck.friction(rng, 0.25, 100, 300, 3000, 0.7, env([(0, 0), (0.04, 1), (0.25, 0)], 0.25))
        gap = rng.uniform(0.3, 0.36)
        return ck.place([(0, stroke(0.8, 0), -4), (gap, stroke(1.2, 1), 0), (gap + 0.3, bore(out, 0.3), -14)])


def ball_roll(rng, length):
    """An iron ball rolling in an iron tube: rough rolling contact (fine stick-slip and grains) rung through the bore."""
    fr = ck.friction(rng, length, 140, 150, 2500, 0.8)
    rum = lp(synth.noise(length, rng, "brown"), 300)
    return bore(mix(fr, ck.norm(rum) * 0.5), 0.55, q=25)


fire_line()
reload_lines()
