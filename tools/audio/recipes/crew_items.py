"""What the crew carries and works with: the soda-acid extinguisher (crew-extinguisher), the three melee tools
(crew-melee), things picked up and put down (crew-carry), the lamps (crew-lamps) and the repair kit (crew-repair).

Each object is built from what it's made of, from the packs' real recordings: the extinguisher is a copper cylinder (a
copper pot's knock, pitched down to its size and damped by the ten litres of water in it, which slosh); the shovel a thin
steel blade on an ash shaft (thin plate and a light metal ring); the wrench solid forged steel (a heavy metal hit, choked
short: solid steel barely rings); the crowbar a steel bar (it rings like a struck bar, long); the crate slatted wood;
the lantern a tin frame, a wire bail and a glass globe. Where it lands, the floor answers (crew_kit.floor: a car's
boards, a steel grate, ballast, concrete). Swings are air moved by the tool's shape (a broad blade is a low whoof, a thin
bar a quick high swish) with the coat sleeve moving under them. The extinguisher's discharge is a stand-in: the packs
have no jet of water.
"""

import functools

import numpy as np

import dsp
import synth
from build import recipe
from dsp import samples, lp, hp, bp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import DROP, R, S, K
from recipes.kit import snap

POT = R("metalPot1", "metalPot2", "metalPot3")
WATER = S("loop_water_02", "loop_water_03")
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
LEATHER = R("handleSmallLeather", "handleSmallLeather2", "dropLeather")
CLINKS = S("metal_02", "metal_06") + R("beltHandle1")
GLASS = S("glass_01", "glass_04", "glass_06")
TIN = K("impactTin_medium")
PLATE_L, PLATE_M, PLATE_H = K("impactPlate_light"), K("impactPlate_medium"), K("impactPlate_heavy")
METAL_L, METAL_M, METAL_H = K("impactMetal_light"), K("impactMetal_medium"), K("impactMetal_heavy")
WOOD_L = K("impactWood_light")
SOFT = K("impactSoft_heavy")


def hit_of(key, i=0, length=0.3, floor_db=-14):
    """The i-th hit of a recording, cut to `length`."""
    x = ck.get(key)
    h = ck.hits(x, floor_db=floor_db, gap=0.03)
    a = h[i % len(h)][0] if h else 0
    return ck.norm(ck.cut(x, a - samples(0.002), a + samples(length), 0.001, min(0.05, length / 3)))


# ---- The extinguisher -----------------------------------------------------------------------------------------------------

def copper(rng, take, damp=0.06, size=-5.0):
    """The copper cylinder knocked: a copper pot's hit pitched down to a 0.6 m vessel and damped by the water inside."""
    k = hit_of(POT[take % 3], int(rng.integers(3)), 0.4)
    return ck.norm(ck.choke(dsp.vari(k, size + rng.uniform(-0.6, 0.6)), 0.01, damp))


def slosh(rng, length=0.5, amount=1.0, take=0):
    """Ten litres of water slopping inside the copper: real running water cut short and darkened, rung through the
    cylinder's hollow, with a few slow low bubbles; a swell as the weight swings and a smaller one coming back."""
    w = ck.get(WATER[take % 2])
    a = int(rng.integers(0, len(w) - samples(length) - 1))
    w = lp(w[a:a + samples(length)], 1400)
    w = mix(w * 0.6, dsp.resonate(w, [420, 830, 1250], q=8) * 0.4)
    b = synth.bubbles(length, 18 * amount, 140, 600, rng, rise=(0.05, 0.3))
    y = mix(ck.norm(w), b * 0.5)
    sw = env([(0, 0), (length * 0.25, 1), (length * 0.5, 0.35), (length * 0.7, 0.6), (length, 0)], length)
    return ck.norm(y * sw) * amount


def jet(rng, length, take=0):
    """A pressurised water jet (stand-in): broadband jet noise with its pressure wavering, the packs' running water
    under it for the wet, and the spatter of it landing. Not a fizz: no bubbles riding on top, the body is the roar."""
    n = samples(length)
    roar = synth.noise(length, rng, "pink")
    roar = bp(roar, 500, 9000, 2)
    wobble = 1 + 0.18 * lp(rng.standard_normal(n).astype(np.float32), 3) * 12 + 0.08 * lp(rng.standard_normal(n).astype(np.float32), 25) * 20
    roar = roar * np.clip(wobble, 0.4, 1.6)
    w = ck.get(WATER[(take + 1) % 2])
    reps = int(np.ceil(n / len(w))) + 1
    w = np.tile(w, reps)[samples(rng.uniform(0, 2)):][:n]
    spatter = ck.grains(rng, int(260 * length), length, 1800, 7000, q=(2, 5), length=(0.002, 0.006), shape=0.0)
    return ck.norm(mix(ck.norm(roar) * 0.55, ck.norm(hp(w, 300)) * 0.5, ck.norm(spatter[:n]) * 0.12))


def extinguisher():
    L = "crew-extinguisher"

    @recipe(L, "grab-mount", "cradle", "Lifted out of its iron cradle: the hoop strap knocked open, copper on iron, the water slopping",
            """The hoop strap knocked back (the packs' metal latch), the copper cylinder's base dragging up out of its iron
            floor cradle (a copper pot's knock, pitched down to a 0.6 m vessel and damped by the water in it, over a short
            iron scrape), and the ten litres inside slopping as it swings up.""",
            sources=R("metalLatch") + POT + WATER, takes=3)
    def grab(rng, k):
        latch = ck.norm(ck.align(ck.get("kenney_rpg-audio:metalLatch")))
        scrape = ck.friction(rng, 0.12, 240, 900, 5000, 0.6, env([(0, 0), (0.02, 1), (0.12, 0)], 0.12))
        return ck.place([(0, dsp.vari(latch, rng.uniform(-2, 0)), -4), (0.09, scrape, -14), (0.13, copper(rng, k), -2),
                         (0.18, slosh(rng, 0.6, 1.0, k), -6)])

    @recipe(L, "equip", "hands", "From the hotbar into the hands: a coat sleeve, the handle taken, the water slopping",
            """A coat sleeve moving (the packs' cloth), the brass carrying handle taking the weight with a small tick, and the
            water slopping inside the copper.""", sources=CLOTH + CLINKS + WATER, takes=3)
    def equip(rng, k):
        tickx = hit_of(CLINKS[k % 3], 0, 0.12)
        return ck.place([(0, ck.cloth(rng, k, 0.3), -6), (0.12, tickx, -16), (0.12, copper(rng, k, 0.04), -16),
                         (0.14, slosh(rng, 0.5, 0.8, k), -4)])

    @recipe(L, "spray-start", "plunger", "Stand-in: the plunger struck and the jet coming on with a gulp and a sputter",
            """Stand-in until the Sonniss library: the brass plunger struck down (a short metal knock with the copper
            ringing under it), a gulp as the pressure finds the hose, two or three sputters, then the jet (pink jet noise
            and the packs' running water) settling to full.""", sources=METAL_M + POT + WATER, takes=2)
    def spray_start(rng, k):
        knock = ck.choke(hit_of(METAL_M[k + 1], 0, 0.2), 0.01, 0.03)
        j = jet(rng, 1.4, k) * env([(0, 0), (0.08, 0.5), (0.11, 0.15), (0.16, 0.8), (0.2, 0.3), (0.3, 1.0), (1.4, 1.0)], 1.4)
        gulp = ck.slurp(rng, 0.12, 200, 1100)
        return ck.place([(0, knock, -6), (0, copper(rng, k, 0.08), -14), (0.05, gulp, -10), (0.1, j, 0)])

    @recipe(L, "spray", "jet", "Stand-in: spraying, held: a pressurised water jet and its spatter",
            """Stand-in until the Sonniss library has a real discharge: jet noise (pink noise, 0.5-9 kHz) whose pressure
            wavers slowly and flutters fast, the packs' running water under it for the wet, and a fine spatter of drops
            landing. The roar is the body, so it reads as water under pressure rather than a fizz. One 8 s cycle.""",
            sources=WATER, loop=True, takes=1, lufs=-20)
    def spray(rng, k):
        return jet(rng, 8.3, k)

    @recipe(L, "spray-stop", "cutoff", "Stand-in: the jet cut off, a last spit and the hose dribbling",
            """Stand-in: the jet collapsing in a tenth of a second (the spray's own noise, cut), one last spit, the hose
            dribbling (a few low drips) and the plunger's small click.""", sources=WATER + CLINKS, takes=2)
    def spray_stop(rng, k):
        j = jet(rng, 0.35, k) * env([(0, 1), (0.08, 0.7), (0.15, 0.05), (0.35, 0)], 0.35)
        spit = jet(rng, 0.08, k + 1) * env([(0, 0), (0.01, 1), (0.08, 0)], 0.08)
        drips = mix(*[ck.place([(t, synth.bubble(rng.uniform(350, 750), rise=0.3), 0)], 0.6)
                      for t in rng.uniform(0.25, 0.55, 3)])
        return ck.place([(0, j, 0), (0.22, spit, -6), (0.15, hit_of(CLINKS[k], 0, 0.1), -18), (0.0, drips, -22)])

    @recipe(L, "run-dry", "sputter", "Stand-in: the charge running out: the jet breaking into spits, then gas, then drips",
            """Stand-in: the jet breaking up as the charge runs out: spits of water and gas getting shorter and further
            apart over a second and a half (the jet sound gated in bursts), a last gassy breath of the empty cylinder,
            and the hose dripping.""", sources=WATER, takes=2)
    def run_dry(rng, k):
        L_ = 1.8
        j = jet(rng, L_, k)
        g = np.zeros(samples(L_), np.float32)
        t, w = 0.0, 0.35
        while t < 1.4:
            a, b = samples(t), samples(t + w * rng.uniform(0.5, 1.0))
            g[a:b] = np.hanning(b - a) ** 0.5 * (1 - t / 1.6)
            t += w + rng.uniform(0.05, 0.15)
            w *= 0.7
        gas = bp(synth.noise(0.4, rng, "pink"), 300, 3000) * env([(0, 0), (0.05, 1), (0.4, 0)], 0.4)
        drips = mix(*[ck.place([(t, synth.bubble(rng.uniform(500, 1000), rise=0.4), 0)], L_)
                      for t in rng.uniform(1.4, 1.75, 3)])
        return mix(j * g, ck.place([(1.35, ck.norm(gas), -12)], L_), drips * 0.2)

    @recipe(L, "dry-trigger", "click", "Trigger squeezed on an empty cylinder: a dry brass click and the hollow copper",
            """The plunger's dry click (the packs' metal click, its two parts cut to the first) and the empty copper ringing
            a little, undamped now there's no water in it. Nothing comes out.""",
            sources=R("metalClick") + POT, takes=3, lufs=-24)
    def dry(rng, k):
        c = hit_of("kenney_rpg-audio:metalClick", k % 2, 0.12)
        return ck.place([(0, dsp.vari(c, rng.uniform(-3, -1)), 0), (0.003, copper(rng, k, 0.18, -4), -18)])

    @recipe(L, "return-mount", "cradle", "Stood back in its iron cradle and the hoop strap snapped over",
            """The copper base set down into the iron cradle (the damped copper knock and an iron clank under it), the water
            settling, and the hoop strap snapped back over (the packs' metal latch).""",
            sources=POT + METAL_H + R("metalLatch") + WATER, takes=3)
    def ret(rng, k):
        clank = ck.choke(hit_of(METAL_H[k], 0, 0.2), 0.008, 0.02)
        latch = ck.norm(ck.align(ck.get("kenney_rpg-audio:metalLatch")))
        return ck.place([(0, copper(rng, k), -2), (0.004, dsp.vari(clank, -7), -12), (0.02, slosh(rng, 0.45, 0.6, k), -10),
                         (0.32 + rng.uniform(0, 0.06), dsp.vari(latch, rng.uniform(-2, 0)), -4)])

    @recipe(L, "stow", "hotbar", "Back into the hotbar: the handle let go, a sleeve, the water slopping",
            """The handle let go (a small tick), a coat sleeve, the water slopping as it's swung away.""",
            sources=CLOTH + CLINKS + WATER, takes=3)
    def stow(rng, k):
        return ck.place([(0, slosh(rng, 0.45, 0.7, k + 1), -4), (0.05, ck.cloth(rng, k + 1, 0.25), -8),
                         (0.2, hit_of(CLINKS[(k + 1) % 3], 0, 0.1), -16)])

    for mat in DROP:
        @recipe(L, "drop", "cylinder", f"The full copper cylinder hitting the {FLOOR[mat]}, rolling a little, water slopping",
                f"""The cylinder lands on its side: the copper's knock (pitched to its size, damped by the water), the
                floor's answer ({FLOOR_HOW[mat]}), a smaller second knock as it settles and rolls, and the water slopping
                inside.""", sources=POT + WATER + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-18)
        def drop(rng, k, m=mat):
            f = ck.floor(rng, m, k, weight=2.0, hard=0.8)
            return ck.place([(0, copper(rng, k, 0.07, -6), 0), (0, f, -2), (0.16, copper(rng, k + 1, 0.04, -6), -12),
                             (0.16, ck.floor(rng, m, k + 1, 1.0, 0.6), -12), (0.05, slosh(rng, 0.6, 1.0, k), -8)])


FLOOR = dict(wood="car's boards", grate="steel grate", ground="ballast", concrete="concrete floor")
FLOOR_HOW = dict(wood="the packs' real wood knocks with the car floor's hollow under them",
                 grate="thin plate clatter and the bars chattering",
                 ground="a dull pad, stones knocking and a slice of real gravel crunch",
                 concrete="the packs' concrete slaps and a short hard room")


# ---- Melee: the shovel, the wrench, the crowbar ---------------------------------------------------------------------------

def tool_ring(rng, tool, take, damp=1.0):
    """The tool's own voice when it strikes something. Shovel: its thin blade's clatter and tang. Wrench: forged steel,
    a hard short tink. Crowbar: a steel bar, which rings on like a struck bar. `damp` < 1 lets less of it ring (on
    flesh)."""
    if tool == "shovel":
        blade = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_L[take % 5]), 1.5))), 0.01, 0.05 * damp)
        tang = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_L[(take + 2) % 5]), -3 + rng.uniform(-0.5, 0.5)))), 0.005, 0.07 * damp)
        return mix(blade, tang * 0.3)
    if tool == "wrench":
        t = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[take % 5]), -2 + rng.uniform(-0.5, 0.5)))), 0.004, 0.03 * damp)
        return mix(t, ck.choke(hp(ck.norm(ck.align(ck.get(PLATE_M[take % 5]))), 400), 0.005, 0.02) * 0.4)
    bar = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_M[take % 5]), -4 + rng.uniform(-0.4, 0.4)))), 0.01, 0.35 * damp)
    hi = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_L[(take + 1) % 5]), -2))), 0.01, 0.25 * damp)
    return mix(bar, hi * 0.45, ck.choke(bar_ring(rng), 0.02, 0.4 * damp) * 0.3)


SWING = {
    # length (s), band low/high at the swing's peak (Hz), filter Q, sleeve level: a broad blade moves more air, lower
    "shovel": (0.42, 110, 900, 0.8, -8),
    "wrench": (0.3, 180, 1500, 1.0, -8),
    "crowbar": (0.28, 260, 2400, 1.5, -9),
}


def swing(rng, tool, take):
    L, lo, hi, q, cl = SWING[tool]
    L *= rng.uniform(0.9, 1.12)
    pk = rng.uniform(0.5, 0.65)
    w = ck.whoosh(rng, L, lo, hi * rng.uniform(0.9, 1.1), pk, q)
    parts = [(0, ck.cloth(rng, take, 0.3), cl), (0.05, w, 0)]
    if tool == "crowbar":
        # a thin bar whistles a little at the top of the swing
        wh = dsp.sweep_filter(synth.noise(L, rng), "bp", env([(0, hi * 0.5), (L * pk, hi * 1.3), (L, hi * 0.6)], L, "exp"), q=8)
        parts.append((0.05, ck.norm(wh) * env([(0, 0), (L * pk, 1), (L, 0)], L) ** 2, -16))
    if tool == "shovel":
        # the blade rattles on its rivets as it's brought round
        parts.append((0.03, ck.choke(tool_ring(rng, "shovel", take + 3), 0.003, 0.01), -30))
    return ck.place(parts)


def flesh(rng, take, weight=1.0):
    """A blow landing on a body or a creature: a heavy hide-slap (the packs' leather drop, or a book slapped flat), the
    deep thud of the mass under it, and the flesh giving (a short dark squish, no bubbles: it's meat, not water)."""
    leather = hit_of(LEATHER[2], 0, 0.3) if take % 2 else hit_of("kenney_rpg-audio:bookPlace2", 1, 0.2)
    thud = hit_of(SOFT[take % 5], 0, 0.35)
    give = ck.slurp(rng, 0.14, 160, 700, bubbles=0.0)
    return ck.place([(0, leather, -2), (0, thud, -4 + 2 * (weight - 1)), (0.004, give, -14)])


def bar_ring(rng, length=0.9, f=None):
    """A steel bar's own ring when it's struck: a free bar's modes (1 : 2.76 : 5.40), lightly damped, so a crowbar
    rings on after the hit like a tuning fork."""
    f = f or rng.uniform(650, 850)
    x = np.zeros(samples(length), np.float32)
    x[:samples(0.001)] = rng.standard_normal(samples(0.001))
    y = dsp.resonate(x, [f, f * 2.756, f * 5.404], q=500, gains=[1, 0.6, 0.3])
    return ck.norm(y * env([(0, 1), (length, 0)], length, "exp"))


def melee():
    L = "crew-melee"
    WHAT = dict(shovel="the fireman's coal shovel (a thin steel blade on an ash shaft)",
                wrench="a heavy forged-steel wrench", crowbar="a steel crowbar")
    for tool in ("shovel", "wrench", "crowbar"):
        what = WHAT[tool]
        ring_src = (PLATE_L + METAL_L) if tool == "shovel" else (METAL_H + PLATE_M) if tool == "wrench" else (METAL_M + METAL_L)

        @recipe(L, f"{tool}-swing", "swing", f"The swing of {what}: air moved by its shape, the coat sleeve under it",
                {"shovel": """A broad blade shoves air: a low, wide whoof (pink noise through a band that rises to about
                    900 Hz at the swing's fastest and falls away), the coat sleeve moving (the packs' cloth), and the blade
                    rattling faintly on its rivets.""",
                 "wrench": """A compact heavy tool: a shorter, tighter swish peaking about 1.5 kHz, and the coat sleeve
                    under it (the packs' cloth).""",
                 "crowbar": """A thin bar swung fast: a quick, high swish peaking about 2.4 kHz with a faint whistle off
                    the bar at its fastest, and the coat sleeve (the packs' cloth)."""}[tool],
                sources=CLOTH, takes=4, lufs=-22)
        def sw(rng, k, t=tool):
            return swing(rng, t, k)

        @recipe(L, f"{tool}-hit-flesh", "body", f"{what.capitalize()} landing on a creature or a body",
                {"shovel": """The flat of the blade smacking into a body: a heavy hide-slap (the packs' leather drop, a
                    thwack), the thud of the mass behind it, a little wet, and the blade's clatter and tang choked short
                    by what it hit.""",
                 "wrench": """A dense blunt blow: the hide-slap and deep thud of the body, a bone-dull crack inside it, and
                    the wrench's forged steel barely ringing.""",
                 "crowbar": """The bar hitting a body: hide-slap and thud, a crack, and the bar's own ring, shorter than on
                    iron but there: a crowbar always rings."""}[tool],
                sources=R("dropLeather", "bookPlace2") + SOFT + ring_src, takes=4, lufs=-17)
        def fl(rng, k, t=tool):
            parts = [(0, flesh(rng, k, 1.3 if t != "shovel" else 1.1), 0),
                     (0, tool_ring(rng, t, k, 0.35 if t != "crowbar" else 0.5), {"shovel": -10, "wrench": -18, "crowbar": -14}[t])]
            if t != "shovel":
                parts.append((0.004, snap(rng, 0.06, rng.uniform(900, 1400)), -18))
            return ck.place(parts)

        @recipe(L, f"{tool}-hit-metal", "iron", f"{what.capitalize()} striking iron (a car side, a rail, the engine)",
                {"shovel": """The blade's loud tang and clatter (thin plate and a light metal ring, pitched to a coal
                    shovel's blade) over the heavy iron it struck (the packs' metal slam and a heavy plate under it).""",
                 "wrench": """Forged steel on iron: a hard, bright, short clank (a heavy metal hit, choked: solid steel
                    barely rings) on the dead weight of the iron it struck.""",
                 "crowbar": """The bar's ring is the sound: a struck steel bar ringing on for half a second (medium and light
                    metal hits, the bar's partials), with the iron's heavy clank under it."""}[tool],
                sources=ring_src + S("metal_hit_01") + PLATE_H, takes=4, lufs=-16)
        def mt(rng, k, t=tool):
            iron = ck.choke(hit_of("sfx_100_v2:metal_hit_01", 0, 0.5), 0.03, 0.08)
            heavy = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_H[k % 5]), -2))), 0.03, 0.06)
            return ck.place([(0, tool_ring(rng, t, k), 0), (0, iron, -9), (0, heavy, -8)])

        @recipe(L, f"{tool}-hit-wood", "boards", f"{what.capitalize()} striking wood (car walls, crates, boards)",
                {"shovel": """The blade's edge biting into a board (the packs' axe chop) and the board's knock under it,
                    the blade's tang short.""",
                 "wrench": """A hard blunt knock on boards (the packs' wood knocks, the car wall's hollow under them) with the
                    wrench barely ringing, and a splinter of the wood giving.""",
                 "crowbar": """A knock on the boards and the bar's ring over it, a splinter of the wood giving."""}[tool],
                sources=R("chop") + ck.WOOD_KNOCKS + S("misc_36") + ring_src, takes=4, lufs=-17)
        def wd(rng, k, t=tool):
            knock = ck.floor(rng, "wood", k, weight=1.4, hard=1.0)
            board = hit_of("sfx_100_v2:misc_36", 0, 0.3)
            parts = [(0, knock, 0), (0, board, -6),
                     (0, tool_ring(rng, t, k, 0.4 if t != "crowbar" else 0.7), {"shovel": -14, "wrench": -20, "crowbar": -12}[t])]
            if t == "shovel":
                parts.append((0, hit_of("kenney_rpg-audio:chop", 0, 0.22), -3))
            else:
                parts.append((0.006, snap(rng, 0.08, rng.uniform(1600, 2600)), -16))
            return ck.place(parts)

        @recipe(L, f"{tool}-equip", "hands", f"{what.capitalize()} from the hotbar into the hands",
                {"shovel": """The ash shaft sliding through a gloved hand (a short wooden rub), the blade knocking once on
                    something, a coat sleeve.""",
                 "wrench": """Taken from the kit: the steel clinking once against another tool, the leather glove closing on
                    it (the packs' small leather handling), a sleeve.""",
                 "crowbar": """The bar lifted: a dull iron clink with a little of its ring, the leather glove closing on it, a
                    sleeve."""}[tool],
                sources=CLOTH + LEATHER + CLINKS + ring_src, takes=3, lufs=-24)
        def eq(rng, k, t=tool):
            grip = hit_of(LEATHER[k % 2], 0, 0.25)
            parts = [(0, ck.cloth(rng, k, 0.25), -6), (0.12, grip, -4)]
            if t == "shovel":
                parts += [(0.02, ck.friction(rng, 0.16, 140, 300, 2500, 0.6, env([(0, 0), (0.03, 1), (0.16, 0)], 0.16)), -10),
                          (0.17, ck.choke(tool_ring(rng, t, k), 0.004, 0.02), -18)]
            else:
                parts += [(0.06, ck.choke(tool_ring(rng, t, k), 0.004, 0.03 if t == "wrench" else 0.08), -16),
                          (0.06, hit_of(CLINKS[k % 2], 0, 0.1), -20)]
            return ck.place(parts)

        @recipe(L, f"{tool}-stow", "away", f"{what.capitalize()} put away into the hotbar",
                """The glove letting go (the packs' small leather handling), a sleeve, and the tool's one small knock as it's
                put away.""", sources=CLOTH + LEATHER + ring_src, takes=3, lufs=-24)
        def st(rng, k, t=tool):
            return ck.place([(0, hit_of(LEATHER[(k + 1) % 2], 0, 0.2), -4), (0.04, ck.cloth(rng, k + 1, 0.22), -8),
                             (0.18, ck.choke(tool_ring(rng, t, k + 2), 0.004, 0.02), -18)])

        for mat in DROP:
            @recipe(L, f"{tool}-drop", "clatter", f"{what.capitalize()} dropped on the {FLOOR[mat]}",
                    {"shovel": "The handle's end landing first (a light wood knock), then the blade clattering flat (thin "
                               "plate and its tang) and skittering once.",
                     "wrench": "Forged steel landing: one solid clank, a bounce, and a little spin to rest.",
                     "crowbar": "One end then the other: two clanks a beat apart, each ringing the bar, and the ring "
                                "carrying on as it rocks to rest."}[tool] + f" The floor answers: {FLOOR_HOW[mat]}.",
                    sources=ring_src + WOOD_L + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-18)
            def dr(rng, k, t=tool, m=mat):
                if t == "shovel":
                    handle = hit_of(WOOD_L[k % 5], 0, 0.2)
                    blade = mix(tool_ring(rng, t, k, 0.7), ck.floor(rng, m, k, 0.8, 1.0) * 0.8)
                    return ck.place([(0, handle, -8), (0, ck.floor(rng, m, k + 1, 0.4, 0.5), -10),
                                     (0.07, ck.bounce(rng, blade, 1, 0.09), 0)])
                if t == "wrench":
                    clank = mix(tool_ring(rng, t, k), ck.floor(rng, m, k, 1.0, 1.0) * 0.9)
                    return ck.bounce(rng, clank, 2, 0.11, 0.4, 9)
                a = mix(tool_ring(rng, t, k, 0.6), ck.floor(rng, m, k, 1.0, 1.0) * 0.8)
                b = mix(tool_ring(rng, t, k + 1, 1.0), ck.floor(rng, m, k + 2, 0.8, 1.0) * 0.6)
                return ck.place([(0, a, 0), (rng.uniform(0.09, 0.14), b, -3)])


# ---- Carrying ------------------------------------------------------------------------------------------------------------

CRATE_KNOCK = R("bookPlace1", "bookPlace2") + S("misc_08", "wood_hit_02")


def crate(rng, take, hard=1.0):
    """The crate itself: a slatted wooden box's knock, pitched down to its size, and its load shifting inside."""
    k = hit_of(CRATE_KNOCK[take % 4], 0, 0.3)
    k = ck.tilt(dsp.vari(k, -4 + rng.uniform(-0.6, 0.6)), lo_db=3, hi_db=-4 * (1 - hard))
    load = ck.pad(rng, 0.09, 350)
    creak = synth.creak(0.18, rng.uniform(40, 70), rng, body=synth.WOOD, q=14) * env([(0, 0), (0.03, 1), (0.18, 0)], 0.18)
    return ck.place([(0, ck.norm(k), 0), (0.035, load, -6), (0.05, creak, -22)])


def lantern(rng, take, part):
    """A hand lantern: its tin base (a tin hit pitched down), the wire bail clinking, the glass globe rattling in its frame."""
    if part == "base":
        t = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(TIN[take % 5]), -3 + rng.uniform(-0.5, 0.5)))), 0.006, 0.03)
        return mix(t, ck.choke(hit_of(S("metal_02")[0], take % 3, 0.15), 0.01, 0.03) * 0.5)
    if part == "bail":
        # the wire bail knocking in its ears: two small real clinks, pitched up for thin wire
        c = [dsp.vari(hit_of(k, i, 0.1), rng.uniform(2, 4)) for k, i in (("sfx_100_v2:metal_06", 0), ("sfx_100_v2:metal_02", 2))]
        return ck.place([(0, c[take % 2], 0), (rng.uniform(0.03, 0.06), c[(take + 1) % 2], -5)])
    # the globe: two or three small glass tinks
    g = hit_of(GLASS[take % 3], 0, 0.12)
    return ck.place([(rng.uniform(0, 0.05) * i, dsp.vari(g, rng.uniform(-2, 2)), -6 * i) for i in range(3)])


def carry():
    L = "crew-carry"

    @recipe(L, "crate-lift", "crate", "A crate picked up: the slats creaking as it takes the weight, its load shifting",
            """Two hands under the crate: the slats creaking as it takes the weight (a slow stick-slip creak through a
            board's modes), a light wooden knock as it leaves the floor (the packs' wooden knocks), its load sliding
            over inside (a muffled thud), and a coat sleeve.""",
            sources=CRATE_KNOCK + CLOTH, takes=3, lufs=-22)
    def crate_lift(rng, k):
        creak = synth.creak(0.35, rng.uniform(25, 45), rng, body=synth.WOOD, q=16) * env([(0, 0), (0.08, 1), (0.35, 0)], 0.35)
        return ck.place([(0, ck.cloth(rng, k, 0.3), -8), (0.05, creak, -10), (0.12, crate(rng, k, 0.3), -8),
                         (0.22, ck.pad(rng, 0.1, 300), -10)])

    for mat in DROP:
        @recipe(L, "crate-set", "crate", f"A crate set down on the {FLOOR[mat]}",
                f"""The crate's slatted body set down (the packs' wooden knocks pitched down to a crate's size), its load
                settling a moment later, a creak of the slats. The floor answers: {FLOOR_HOW[mat]}.""",
                sources=CRATE_KNOCK + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-19)
        def crate_set(rng, k, m=mat):
            return ck.place([(0, crate(rng, k, 0.5), -2), (0, ck.floor(rng, m, k, 2.0, 0.6), 0)])

        @recipe(L, "crate-land", "crate", f"A crate thrown and landing on the {FLOOR[mat]}",
                f"""Thrown: the crate landing hard on a corner and then flat (two knocks 40-70 ms apart), its load slamming
                inside, the slats rattling. The floor answers hard: {FLOOR_HOW[mat]}.""",
                sources=CRATE_KNOCK + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-16)
        def crate_land(rng, k, m=mat):
            d = rng.uniform(0.04, 0.07)
            rattle = ck.grains(rng, 6, 0.08, 600, 2500, q=(8, 16), length=(0.01, 0.03))
            return ck.place([(0, crate(rng, k, 1.0), -2), (0, ck.floor(rng, m, k, 2.5, 0.9), 0),
                             (d, crate(rng, k + 1, 0.8), 0), (d, ck.floor(rng, m, k + 1, 3.0, 0.8), 0),
                             (d + 0.01, rattle, -14), (d + 0.03, ck.pad(rng, 0.1, 400), -4)])

        @recipe(L, "crate-drag", "crate", f"A heavy crate dragged over the {FLOOR[mat]}, held",
                f"""Dragged in pulls (a heave every second or so): the crate's runners judder over the surface (stick-slip
                friction in the surface's band), {DRAG_HOW[mat]}, and the slats creak under the strain. One 8 s cycle.""",
                sources=ck.FLOOR_SOURCES[mat], loop=True, takes=1, mat=mat, lufs=-20)
        def crate_drag(rng, k, m=mat):
            return drag(rng, m, 8.0)

    @recipe(L, "lamp-lift", "lantern", "A hand lantern picked up: the wire bail and the glass globe rattling in its frame",
            """The wire bail lifted and clinking in its ears (tiny bright metal ticks), the tin frame's small knock as it
            leaves the floor, the glass globe rattling in its frame (the packs' small glass tinks), and the kerosene
            moving.""", sources=GLASS + TIN + S("metal_02", "metal_06") + WATER, takes=3, lufs=-24)
    def lamp_lift(rng, k):
        return ck.place([(0, lantern(rng, k, "bail"), -4), (0.12, lantern(rng, k, "base"), -10),
                         (0.14, lantern(rng, k, "globe"), -10), (0.15, slosh(rng, 0.3, 0.3, k), -22)])

    for mat in DROP:
        @recipe(L, "lamp-set", "lantern", f"A lantern set down on the {FLOOR[mat]}",
                f"""The tin base set down (a tin hit pitched down and damped), the globe rattling, then the wire bail
                falling over onto the frame with a clink. The floor answers lightly: {FLOOR_HOW[mat]}.""",
                sources=TIN + GLASS + S("metal_02", "metal_06") + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-22)
        def lamp_set(rng, k, m=mat):
            return ck.place([(0, lantern(rng, k, "base"), 0), (0, ck.floor(rng, m, k, 0.5, 0.8), -6),
                             (0.02, lantern(rng, k, "globe"), -10), (rng.uniform(0.15, 0.25), lantern(rng, k + 1, "bail"), -8)])

    @recipe(L, "lamp-break", "lantern", "A lantern dropped hard enough to break: the tin crumpling, the globe shattering",
            """The tin frame crumpling on the floor (thin plate and a tin hit), the glass globe shattering (the packs' glass
            breaks) and its pieces skittering, the bail clattering after.""",
            sources=S("glass_03", "glass_05") + K("impactGlass_heavy") + TIN + PLATE_L, takes=3, lufs=-17)
    def lamp_break(rng, k):
        shatter = ck.norm(ck.align(ck.get(S("glass_03", "glass_05")[k % 2])))
        crash = mix(ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_L[k]), 2))), 0.02, 0.05),
                    lantern(rng, k, "base"))
        bits = ck.grains(rng, 18, 0.35, 3000, 9000, q=(20, 50), length=(0.01, 0.04), shape=0.5)
        return ck.place([(0, crash, -2), (0.005, shatter, 0), (0.03, hit_of(K("impactGlass_heavy")[k], 0, 0.2), -10),
                         (0.08, bits, -16), (0.2, lantern(rng, k + 1, "bail"), -10)])

    toys(L)
    radio(L)
    bodies(L)

    @recipe(L, "loot-take", "pocket", "Loot taken: small salvage scooped up and pocketed",
            """A gloved hand closing on something small (the packs' small leather handling), its bits clinking (the packs'
            coin handling, cut short), and the coat pocket taking it (cloth).""",
            sources=LEATHER + R("handleCoins", "handleCoins2") + CLOTH, takes=3, lufs=-24)
    def loot(rng, k):
        coins = hit_of(R("handleCoins", "handleCoins2")[k % 2], 0, 0.3)
        return ck.place([(0, hit_of(LEATHER[k % 2], 0, 0.2), -4), (0.08, coins, -8), (0.25, ck.cloth(rng, k, 0.25), -8)])


DRAG_HOW = dict(wood="catching on the board seams with a knock every few boards",
                grate="its runners rattling over the bars", ground="grinding stones aside and over",
                concrete="the grit under it scraping, in a hard room")


def drag(rng, mat, length):
    """A crate dragged over a surface in heaves, for a loop exactly `length` long."""
    n = samples(length)
    band = dict(wood=(250, 2500, 110), grate=(500, 4000, 160), ground=(300, 3000, 70), concrete=(500, 5000, 200))[mat]
    pulls, t = [], 0.0
    while t < length:
        d = rng.uniform(0.7, 1.0)
        pulls.append((t, d))
        t += d + rng.uniform(0.15, 0.3)
    shape = np.zeros(n + samples(2), np.float32)
    for t0, d in pulls:
        e = env([(0, 0), (0.08, 1), (d * 0.7, 0.85), (d, 0)], d)
        a = samples(t0)
        shape[a:a + len(e)] = np.maximum(shape[a:a + len(e)], e[:len(shape) - a])
    shape = shape[:n]
    fr = ck.friction(rng, length, band[2], band[0], band[1], 0.7, shape)
    parts = [(0, fr, 0)]
    creak = synth.creak(length, 35, rng, body=synth.WOOD, q=16, shape=shape * 0.5)
    parts.append((0, creak, -16))
    for t0, d in pulls:
        if mat == "wood":
            for tt in np.arange(t0 + 0.15, t0 + d, rng.uniform(0.25, 0.35)):
                parts.append((tt, ck.floor(rng, "wood", int(tt * 10), 0.6, 0.6), -14))
        elif mat == "grate":
            for tt in np.arange(t0 + 0.05, t0 + d - 0.05, 0.028):
                parts.append((tt, ck.tick(rng, rng.uniform(1500, 3500), q=25, length=0.03), -16))
        elif mat == "ground":
            parts.append((t0, ck.grains(rng, int(90 * d), d, 600, 4000, q=(3, 8), shape=0.0), -6))
            parts.append((t0, ck.pad(rng, d, 200) * env([(0, 0), (0.1, 1), (d, 0)], d), -10))
        else:
            parts.append((t0, ck.grains(rng, int(60 * d), d, 2500, 8000, q=(2, 5), length=(0.002, 0.008), shape=0.0), -12))
    y = ck.place(parts, length + 1.0)
    if mat == "concrete":
        y = ck.space(y, 0.45, 0.14, 5000, ((0.006, 0.5), (0.013, 0.35), (0.021, 0.2)))
    elif mat == "wood":
        y = dsp.room(y, "car", wet=0.12, rng=np.random.default_rng(11))
    return dsp.wrap(y, n)


TOY = dict(horse="the pull-along horse (painted wood on a wheeled board)", doll="the porcelain-headed doll in its smock",
           bear="the rag bear")


def toy(rng, which, take, part):
    """horse: painted wood and four little wheels; doll: a cloth body with a porcelain head; bear: all rag and stuffing."""
    if which == "horse":
        knock = ck.tilt(hit_of(WOOD_L[take % 5], 0, 0.15), hi_db=6, hi_f=1500)
        wheels = ck.grains(rng, 5, 0.08, 1800, 3500, q=(10, 20), length=(0.01, 0.025))
        return mix(ck.norm(knock), ck.norm(wheels) * 0.35) if part == "drop" else mix(ck.norm(wheels) * 0.6, ck.norm(knock) * 0.25)
    if which == "doll":
        tink = ck.choke(hit_of(GLASS[take % 3], 0, 0.1), 0.005, 0.02)
        soft = ck.cloth(rng, take + 1, 0.15)
        return ck.place([(0, soft, 0), (0.01, dsp.vari(tink, -7), -8 if part == "drop" else -14)])
    return ck.place([(0, ck.cloth(rng, take + 2, 0.2), 0), (0.01, ck.pad(rng, 0.06, 500), -8)])


def toys(L):
    for which in ("horse", "doll", "bear"):
        @recipe(L, "toy-lift", which, f"A toy picked up: {TOY[which]}",
                {"horse": "The wooden horse lifted off its wheels: the little wheels spinning and knocking on their axles "
                          "(tiny wooden ticks), the board's light knock (Kenney's light wood hit).",
                 "doll": "The doll lifted: its smock (the packs' cloth, cut short) and the porcelain head ticking once.",
                 "bear": "The rag bear lifted: soft cloth and stuffing, nothing hard in it."}[which],
                sources=WOOD_L + GLASS + CLOTH, takes=3, lufs=-26)
        def lift(rng, k, w=which):
            return ck.place([(0, ck.cloth(rng, k, 0.2), -10), (0.05, toy(rng, w, k, "lift"), 0)])

        for mat in DROP:
            @recipe(L, "toy-drop", which, f"{TOY[which].capitalize()} dropped on the {FLOOR[mat]}",
                    {"horse": "Painted wood landing and bouncing once, its wheels rattling.",
                     "doll": "The cloth body flopping and the porcelain head knocking once on the floor.",
                     "bear": "A soft rag flop, the floor barely answering."}[which] + f" The floor: {FLOOR_HOW[mat]}.",
                    sources=WOOD_L + GLASS + CLOTH + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-24)
            def drop(rng, k, w=which, m=mat):
                hard = dict(horse=1.0, doll=0.5, bear=0.05)[w]
                y = mix(toy(rng, w, k, "drop"), ck.floor(rng, m, k, 0.25, hard) * (0.7 if w != "bear" else 0.25))
                return ck.bounce(rng, y, 1, 0.1) if w == "horse" else y


def radio(L):
    @recipe(L, "radio-lift", "set", "The field radio picked up: its steel case, the strap, the aerial twanging",
            """The olive steel case lifted by its strap loop (a leather creak), something loose rattling inside it (tiny
            metal ticks), and the whip aerial twanging as it's swung up (a thin steel rod's ring, wavering).""",
            sources=LEATHER + METAL_L, takes=3, lufs=-24)
    def lift(rng, k):
        return ck.place([(0, hit_of(LEATHER[k % 2], 0, 0.2), -4), (0.08, aerial(rng, k), -12),
                         (0.08, ck.grains(rng, 4, 0.06, 2500, 5000, q=(15, 30)), -18)])

    for mat in DROP:
        @recipe(L, "radio-drop", "set", f"The field radio dropped on the {FLOOR[mat]}",
                f"""The steel case landing on a corner (a medium metal hit choked short over a thin plate's box sound), its
                insides rattling, and the whip aerial twanging. The floor answers: {FLOOR_HOW[mat]}.""",
                sources=METAL_M + PLATE_L + METAL_L + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-20)
        def drop(rng, k, m=mat):
            case = mix(ck.choke(ck.norm(ck.align(ck.get(METAL_M[k]))), 0.004, 0.02),
                       ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_L[k]), 4))), 0.01, 0.03) * 0.7)
            return ck.place([(0, case, 0), (0, ck.floor(rng, m, k, 0.8, 0.9), -3),
                             (0.01, ck.grains(rng, 6, 0.1, 2000, 5000, q=(12, 30)), -14), (0.01, aerial(rng, k), -10)])


def aerial(rng, take):
    """A whip aerial set swinging: a thin steel rod's low ring (a few partials) wavering as it whips."""
    f = rng.uniform(55, 75)
    x = np.zeros(samples(0.8), np.float32)
    x[:samples(0.002)] = 1
    y = dsp.resonate(x, [f * 6.27, f * 17.5, f * 34.4], q=60, gains=[1, 0.5, 0.25])
    y = y * env([(0, 1), (0.8, 0)], 0.8, "exp")
    return ck.norm(dsp.tremolo(y, rng.uniform(6, 9), 0.6))


def bodies(L):
    @recipe(L, "body-lift", "body", "A body or a child hoisted onto a shoulder: heavy cloth, a belt, the weight settling",
            """Heavy clothing dragged and bunched (the packs' cloth handlings, two of them), a belt buckle's clink, a leather
            strap creaking, and the dull weight of the body settling onto a shoulder.""",
            sources=CLOTH + R("clothBelt", "beltHandle2") + LEATHER + SOFT, takes=3, lufs=-21)
    def lift(rng, k):
        buckle = hit_of(R("clothBelt", "beltHandle2")[k % 2], 0, 0.15)
        return ck.place([(0, ck.cloth(rng, k, 0.4), -2), (0.18, ck.cloth(rng, k + 1, 0.3), -6), (0.25, buckle, -18),
                         (0.4, hit_of(SOFT[k], 0, 0.3), -8), (0.42, hit_of(LEATHER[2], 0, 0.25), -12)])

    for mat in DROP:
        @recipe(L, "body-set", "body", f"A body laid down on the {FLOOR[mat]}",
                f"""Laid down: the torso's dull weight (a soft heavy hit, the packs' leather drop over it), an arm and the
                head following, softer, and the clothes settling. The floor answers dully: {FLOOR_HOW[mat]}.""",
                sources=SOFT + R("dropLeather") + CLOTH + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-20)
        def body_set(rng, k, m=mat):
            return limbs(rng, k, m, 1.0)

        @recipe(L, "body-land", "body", f"A body thrown and landing on the {FLOOR[mat]}",
                f"""Thrown: the body slamming down (a heavy soft hit and the leather drop, driven harder), limbs flopping
                after it in three or four smaller thuds, clothes. The floor answers hard: {FLOOR_HOW[mat]}.""",
                sources=SOFT + R("dropLeather") + CLOTH + ck.FLOOR_SOURCES[mat], takes=3, mat=mat, lufs=-16)
        def body_land(rng, k, m=mat):
            return limbs(rng, k, m, 1.8)


def limbs(rng, take, mat, force):
    """A body coming down: the torso, then limbs and the head in a loose sequence, each a soft weight on the floor."""
    torso = mix(hit_of(SOFT[take % 5], 0, 0.4), hit_of(R("dropLeather")[0], 0, 0.35) * 0.6)
    parts = [(0, torso, 0), (0, ck.floor(rng, mat, take, 2.5 * force, 0.15), -4 + 2 * (force - 1))]
    t = rng.uniform(0.05, 0.1)
    for i in range(int(2 + force)):
        limb = mix(hit_of(SOFT[(take + i + 1) % 5], 0, 0.2) * 0.6, ck.cloth(rng, take + i, 0.12) * 0.5)
        parts += [(t, limb, -8 - 3 * i), (t, ck.floor(rng, mat, take + i + 1, 0.7, 0.25), -12 - 3 * i)]
        t += rng.uniform(0.07, 0.16) / force
    parts.append((0.05, ck.cloth(rng, take + 2, 0.4), -10))
    return ck.place(parts)


# ---- Lamps ---------------------------------------------------------------------------------------------------------------

UI_SWITCH = [f"kenney_ui-audio:switch{i}" for i in (20, 24, 27, 30, 16, 33)]
UI_CLICK = [f"kenney_ui-audio:switch{i}" for i in (1, 2, 3, 5, 7, 10)]


def lamps():
    L = "crew-lamps"

    @recipe(L, "car-lamp-on", "switch", "A car lamp's brass switch clicked on: just the click",
            """Just the click, as the director asked: a real toggle switch (Kenney UI pack switch recordings), pitched down a
            little for a chunky brass switch on a car wall, with the car's small wooden room.""",
            sources=UI_SWITCH[:3], takes=3, lufs=-24)
    def on(rng, k):
        return click(rng, UI_SWITCH[k], -3)

    @recipe(L, "car-lamp-off", "switch", "A car lamp's brass switch clicked off: just the click",
            """Just the click: the same switch thrown back (three other takes from the Kenney UI switch recordings), pitched
            down a little, in the car's small wooden room.""",
            sources=UI_SWITCH[3:], takes=3, lufs=-24)
    def off(rng, k):
        return click(rng, UI_SWITCH[3 + k], -3.5)

    @recipe(L, "cab-lamp", "switch", "The engine's forward lamp switched: a heavier click in the iron cab",
            """Just a click, heavier: a switch recording pitched down further (a bigger lever switch on the boiler backhead)
            with a small iron tick under it, in the cab's iron room.""",
            sources=UI_CLICK[:3] + METAL_H, takes=3, lufs=-23)
    def cab(rng, k):
        c = ck.norm(ck.align(dsp.vari(ck.get(UI_CLICK[k]), -6)))
        iron = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[k]), -5))), 0.002, 0.008)
        return dsp.room(mix(c, iron * 0.15), "cab", wet=0.12, rng=np.random.default_rng(3))

    @recipe(L, "lantern-light", "match", "A hand lantern lit: a match struck, the wick catching, the globe dropped back",
            """A match struck on the box (a rough scrape, about 80 ms) and flaring (a short soft rush with a few crackles),
            the wick taking the flame with a little breath of air, and the globe lowered back into its frame with a small
            tin-and-glass click.""", sources=GLASS + TIN, takes=3, lufs=-24)
    def light(rng, k):
        strike = ck.friction(rng, 0.08, 900, 1500, 7000, 0.9, env([(0, 0), (0.01, 1), (0.08, 0.3)], 0.08))
        flare = mix(bp(synth.noise(0.5, rng, "pink"), 400, 5000) * env([(0, 0), (0.03, 1), (0.5, 0)], 0.5, "exp"),
                    synth.crackle(0.5, 30, rng) * 0.6)
        catch = lp(synth.noise(0.35, rng, "pink"), 900) * env([(0, 0), (0.06, 1), (0.35, 0)], 0.35)
        globe = mix(lantern(rng, k, "base") * 0.5, ck.choke(hit_of(GLASS[k], 0, 0.1), 0.004, 0.02) * 0.4)
        return ck.place([(0, strike, -2), (0.07, ck.norm(flare), -8), (0.55, ck.norm(catch), -14), (0.9, globe, -10)])

    @recipe(L, "lantern-out", "wick", "A hand lantern put out: the wick wound down and the flame gone with a puff",
            """The wick winder's knurled wheel turned down (three tiny clicks) and the flame going out with a soft puff
            of air. Nothing else. The clicks are a real small mechanism's ticks (sfx_100 metal_04) pitched up.""",
            sources=S("metal_04"), takes=3, lufs=-26)
    def out(rng, k):
        clicks = [(0.07 * i + rng.uniform(0, 0.02), dsp.vari(hit_of("sfx_100_v2:metal_04", (k + i) % 6, 0.03, -20), 3), -4 * i)
                  for i in range(3)]
        puff = lp(synth.noise(0.25, rng, "pink"), 1200) * env([(0, 0), (0.02, 1), (0.25, 0)], 0.25, "exp")
        return ck.place(clicks + [(0.32, ck.norm(puff), -6)])


def click(rng, key, semis):
    c = ck.norm(ck.align(dsp.vari(ck.get(key), semis + rng.uniform(-0.3, 0.3))))
    return dsp.room(ck.tilt(c, lo_db=3, lo_f=400), "car", wet=0.1, rng=np.random.default_rng(3))


# ---- The repair kit ------------------------------------------------------------------------------------------------------

def toolbox(rng, take, shut):
    """The engineer's sheet-steel toolbox: its lid's thin, boxy clank and the tools jumping inside."""
    lid = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLATE_L[take % 5]), 3 + rng.uniform(-0.5, 0.5)))), 0.01, 0.06 if shut else 0.03)
    box = dsp.room(lid, "box", wet=0.3, rng=np.random.default_rng(4))
    tools = ck.grains(rng, 7 if shut else 4, 0.08, 1200, 4500, q=(15, 35), length=(0.02, 0.05))
    return mix(box, ck.norm(tools) * (0.35 if shut else 0.2))


def repair():
    L = "crew-repair"
    HASP = R("metalLatch", "metalClick")

    @recipe(L, "kit-open", "toolbox", "The repair kit opened: the hasp flipped, the steel lid swung up, the tools inside",
            """The hasp flipped up (the packs' metal latch), the sheet-steel lid's hinge and its light boxy knock as it
            swings back against its stay, and the tools shifting inside it.""",
            sources=HASP + PLATE_L, takes=3, lufs=-22)
    def kit_open(rng, k):
        hasp = dsp.vari(ck.norm(ck.align(ck.get(HASP[0]))), rng.uniform(-2, 0))
        hinge = synth.creak(0.2, rng.uniform(60, 90), rng, body=synth.IRON, q=20) * env([(0, 0), (0.04, 1), (0.2, 0)], 0.2)
        return ck.place([(0, hasp, -4), (0.18, hinge, -16), (0.36, toolbox(rng, k, False), -6)])

    @recipe(L, "kit-shut", "toolbox", "The repair kit shut: the steel lid banged down and the hasp snapped over",
            """The sheet-steel lid banged down (a thin plate's clank, boxy, the tools jumping inside), and the hasp snapped
            over (the packs' metal click).""", sources=HASP + PLATE_L, takes=3, lufs=-20)
    def kit_shut(rng, k):
        hasp = dsp.vari(hit_of(HASP[1], 1, 0.15), rng.uniform(-2, 0))
        return ck.place([(0, toolbox(rng, k + 1, True), 0), (0.3 + rng.uniform(0, 0.08), hasp, -8)])

    @recipe(L, "ratchet", "spanner", "A ratchet spanner worked on a boiler nut, held: the pull, the pawl clicking back",
            """Worked in strokes about a second long: the pull (the nut turning under load, an iron creak through the
            boiler's mass) and the swing back with the pawl clicking over the teeth (the packs' ratchet, sfx_100 misc_20,
            and the lock's ratchet clicks, played as a run of single teeth). One 8 s cycle.""",
            sources=S("misc_20", "lock_open_01"), loop=True, takes=1, lufs=-20)
    def ratchet(rng, k):
        teeth = [hit_of("sfx_100_v2:misc_20", 0, 0.03, -20)] + [hit_of("sfx_100_v2:lock_open_01", i, 0.04, -14) for i in range(4)]
        n = samples(8.0)
        parts, t = [], 0.0
        while t < 8.0:
            pull = rng.uniform(0.5, 0.65)
            strain = synth.creak(pull, rng.uniform(18, 30), rng, body=synth.IRON, q=12) * env([(0, 0), (0.08, 1), (pull, 0.3)], pull)
            parts.append((t, strain, -12))
            parts.append((t, ck.pad(rng, pull, 200), -24))
            t += pull + 0.05
            back = rng.uniform(0.32, 0.42)
            for j in range(int(rng.integers(6, 9))):
                parts.append((t + j * back / 8 + rng.normal(0, 0.003), dsp.vari(teeth[int(rng.integers(len(teeth)))],
                                                                               rng.uniform(-1, 1)), -2 - j * 0.6))
            t += back + rng.uniform(0.05, 0.12)
        y = ck.place(parts, 9.0)
        return dsp.wrap(dsp.room(y, "cab", wet=0.12, rng=np.random.default_rng(3)), n)

    @recipe(L, "board-place", "plank", "A board set against the hole: the plank knocked against the frame",
            """A plank brought up against the breach's frame: its knock (the packs' wooden knocks, a plank's length of
            ring in it), a short scrape as it's slid into place, and the car wall's hollow.""",
            sources=ck.WOOD_KNOCKS + K("impactPlank_medium"), takes=3, lufs=-20)
    def board(rng, k):
        knock = ck.floor(rng, "wood", k, 1.2, 0.9)
        plank = hit_of(K("impactPlank_medium")[k], 0, 0.4)
        sl = ck.friction(rng, 0.18, 120, 300, 2500, 0.7, env([(0, 0), (0.03, 1), (0.18, 0)], 0.18))
        return ck.place([(0, knock, 0), (0, plank, -8), (0.12, sl, -12)])

    @recipe(L, "hammer", "nail", "A nail hammered into the board",
            """The hammer's face on the nail head: a hard, bright steel tak (the packs' hammer-on-metal hit, choked short)
            with the nail's small ring, and the board and the car wall taking the blow (the wooden knocks and the wall's
            hollow). Six blows, from the first ringing taps to the last dull ones as the nail goes home.""",
            sources=S("misc_19") + METAL_H + ck.WOOD_KNOCKS, takes=6, lufs=-17)
    def hammer(rng, k):
        tak = ck.choke(hit_of("sfx_100_v2:misc_19", 0, 0.3), 0.004, 0.02 + 0.03 * (5 - k) / 5)
        nail = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(METAL_H[k % 5]), 5 - k * 0.6))), 0.003, 0.02 + 0.02 * (5 - k) / 5)
        wood = ck.floor(rng, "wood", k, 1.0 + 0.15 * k, 0.9)
        return ck.place([(0, tak, -2 - k * 0.6), (0, nail, -12 - k), (0.002, wood, -4 + k * 0.4)])

    @recipe(L, "done", "flush", "The last nail driven flush: boarded up",
            """The confirmation: two blows, a tap and then a solid one: the hammer's face meeting the board as the nail
            goes flush (a deep wooden THOCK, the wall's hollow booming, no ring left in the nail), and the board settling
            tight with a creak.""", sources=S("misc_19") + ck.WOOD_KNOCKS + S("misc_36"), takes=3, lufs=-15)
    def done(rng, k):
        tap = ck.choke(hit_of("sfx_100_v2:misc_19", 0, 0.2), 0.003, 0.015)
        thock = mix(ck.floor(rng, "wood", k, 2.2, 1.0), hit_of("sfx_100_v2:misc_36", 0, 0.35) * 0.5)
        creak = synth.creak(0.25, rng.uniform(30, 50), rng, body=synth.WOOD, q=16) * env([(0, 0), (0.05, 1), (0.25, 0)], 0.25)
        gap = rng.uniform(0.32, 0.4)
        return ck.place([(0, tap, -10), (0, ck.floor(rng, "wood", k + 3, 0.8, 0.8), -12), (gap, thock, 0),
                         (gap + 0.18, creak, -18)])


extinguisher()
melee()
carry()
lamps()
repair()
