"""The crew knocking each other about (crew-horseplay; K1's #451, ARCHITECTURE §8 notes 717 and 768): a friend shoved, a
shoulder bumped in passing, a tool's swing bonking a friend's head, a lump of coal off the shovel landing on a friend or
clattering on the train and the ground.

None of it is damage (the director's rule for horseplay, 14 Oct), so none of it may sound like a hurt: no cries, no
flesh tearing, no creature. It's the mishaps' kind of funny (mishaps_body.py): the real event, small, and the comedy in
how little there is of it. A shove is two palms slapping a coat and the breath going out of someone; the bonk is the
tunnel's hollow-head bonk with the tool's own clang on top; the coal is a brittle lump breaking.
"""

import numpy as np

import dsp
from dsp import samples, hp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import recipe
from recipes.crew_items import hit_of, flesh, tool_ring, CLOTH, SOFT, LEATHER, PLATE_L, METAL_L, METAL_M, METAL_H, PLATE_M, WOOD_L, TIN
from recipes.crew_body import breath
from recipes.crew_feet import STONES
from recipes.mishaps_body import bonk_core, burst, roof_thump

L = "crew-horseplay"
TOOLS = ("shovel", "wrench", "crowbar")


def palms(rng, take, n=2):
    """Hands meeting a coat: the cloth slapped flat, n of them a few hundredths apart."""
    return ck.place([(i * rng.uniform(0.018, 0.032), ck.cloth(rng, take + i, rng.uniform(0.09, 0.13)), -2 * i) for i in range(n)])


def stumble(rng, length=0.22):
    """A boot caught back under someone shoved: a short scuff across the boards, stick-slip, dark."""
    shape = env([(0, 0), (length * 0.2, 1), (length * 0.7, 0.6), (length, 0)], length)
    return ck.friction(rng, length, 120, 400, 3200, 0.8, shape)


def brittle(rng, n, spread=0.14, lo=1400, hi=7000):
    """Coal breaking: glassy little clinks, brighter than stone (crew_feet's coal), spread out as the pieces skitter."""
    return ck.grains(rng, n, spread, lo, hi, q=(4, 12), length=(0.003, 0.01), shape=0.4)


def soot(rng, length=0.3):
    """A puff of coal dust off a coat: a soft, dark breath of noise, no pitch."""
    return burst(rng, length, 250, 1800, att=0.01) * 0.6


@recipe(L, "shove", "palms", "Both hands shoved into a friend: palms slapping a coat, the breath knocked out, a stumble",
        """Two palms slapping a heavy coat a few hundredths apart (the packs' cloth), the dull weight of a body taking it
        under them (a soft thud, no flesh: nobody's hurt), the breath going out of whoever was shoved (a short open
        exhale, modelled), and a beat later their boot scuffing back across the boards as they catch themselves. Three
        shoves, each a little harder.""",
        sources=CLOTH + SOFT + LEATHER, takes=3, lufs=-20)
def shove_palms(rng, k):
    weight = [0.5, 0.65, 0.8][k]
    thud = hit_of(SOFT[(k + 2) % 5], 0, 0.25)
    oof = breath(rng, rng.uniform(0.2, 0.28), False)
    return ck.place([(0, palms(rng, k), 0), (0.006, thud, -10 + 4 * weight), (0.03, oof, -11),
                     (0.13 + 0.03 * k, stumble(rng), -15)])


@recipe(L, "bump", "shoulder", "A shoulder knocked in passing: a coat's thump and a grunt",
        """Two crewmates' shoulders meeting in a crowd: one coat thumping on another (the packs' cloth, a single slap),
        a little of the body's weight under it, and the short grunt of breath it knocks out. Smaller than a shove: nobody
        staggers.""",
        sources=CLOTH + SOFT, takes=3, lufs=-22)
def bump_shoulder(rng, k):
    thud = hit_of(SOFT[k % 5], 0, 0.2)
    grunt = breath(rng, rng.uniform(0.12, 0.17), False)
    return ck.place([(0, palms(rng, k + 3, 1), 0), (0.004, thud, -12), (0.02, grunt, -14)])


@recipe(L, "bonk", "clang", "A tool's swing landing on a friend's head: the tool's clang and the head's hollow bonk",
        """The tunnel's bonk (mishaps_body: a hollow head's modes dropping in pitch as it's struck, a real hollow wooden
        knock bent the same way) with the tool's own voice on top, choked short as it would be on a head: the shovel's
        blade clattering, the wrench's hard tink, the crowbar's ring cut off. Take 0 the shovel, 1 the wrench, 2 the
        crowbar. A clang and a bonk, no cry: none of horseplay is damage.""",
        sources=WOOD_L + PLATE_L + PLATE_M + METAL_L + METAL_M + METAL_H, takes=3, lufs=-18)
def bonk_clang(rng, k):
    f0 = [430, 470, 400][k] * rng.uniform(0.97, 1.03)
    head = bonk_core(rng, k, f0, [6.0, 5.0, 6.5][k], [0.12, 0.1, 0.13][k])
    # The clang level with the bonk: the first build had the head's modes swamp the tool (90% of it under 490 Hz).
    ring = ck.norm(hp(tool_ring(rng, TOOLS[k], k, damp=0.45), 600))
    return ck.place([(0, ring, 0), (0.002, head, -2)])


@recipe(L, "coal-hit", "coat", "A lump of coal off a friend: a hard thud on a coat, soot puffing, the lump breaking",
        """A fist of coal thrown hard into a crewmate: the lump's own hard knock (a real stone hit, its low end taken out)
        and the coat's slap, only a touch of the body's weight under them (nothing fleshy, no boom), a puff of soot off
        the coat, and the lump breaking as it drops off them, its pieces clinking bright and glassy on the way down.""",
        sources=CLOTH + SOFT + STONES, takes=3, lufs=-20)
def knock_of_coal(rng, take):
    """The lump itself striking: a hard little rock knock (the packs' stones cut to one hit), no weight under it."""
    stones = ck.get(STONES[take % 3])
    h = ck.hits(stones, floor_db=-14, gap=0.03)
    a = h[int(rng.integers(len(h)))][0] if h else 0
    return ck.norm(hp(ck.cut(stones, a, a + samples(0.06), 0.001, 0.03), 400))


def coal_hit_coat(rng, k):
    # The coat's slap and the lump's own knock carry it; the body's weight is only a touch under them (the first build's
    # soft thud put half of it under 90 Hz, a squish).
    thud = hp(hit_of(SOFT[(k + 1) % 5], 0, 0.15), 160)
    pieces = brittle(rng, 9 + 3 * k, 0.18)
    return ck.place([(0, palms(rng, k + 1, 1), 0), (0, knock_of_coal(rng, k), -3), (0.002, thud, -12),
                     (0.01, soot(rng), -14), (0.04, pieces, -7)])


def landing(rng, k, on):
    """A lump coming down on `on` and breaking: the knock it makes there, then its pieces skittering."""
    if on == "roof":
        # A fist of coal on the tin, not a body: the sheet's bright knock and its tinny ring, a little of the hollow under
        # it (roof_thump's boom was half of the first build, a person landing).
        tin = ck.norm(ck.choke(ck.align(dsp.vari(ck.get(TIN[k % 5]), rng.uniform(-1, 1))), 0.01, 0.06))
        sheet = ck.norm(hp(ck.choke(ck.align(ck.get(PLATE_L[(k + 2) % 5])), 0.01, 0.04), 400))
        knock = mix(tin, sheet * 0.6, hp(roof_thump(rng, k, 0.3), 150) * 0.25)
    elif on == "wood":
        board = hit_of(WOOD_L[k % 5], 0, 0.2)
        knock = mix(board, ck.hollow(rng, [105, 170, 260], 0.1, q=4) * 0.25)
    else:
        stones = ck.get(STONES[k % 3])
        h = ck.hits(stones, floor_db=-14, gap=0.03)
        a = h[int(rng.integers(len(h)))][0] if h else 0
        knock = ck.norm(hp(ck.cut(stones, a, a + samples(0.12), 0.002, 0.05), 300))
    pieces = brittle(rng, 10 + 2 * k, 0.2 if on != "ground" else 0.1)
    return dsp.fade(ck.place([(0, knock, 0), (0.015, pieces, -9 if on != "ground" else -13)]), 0.0, 0.1)


@recipe(L, "coal-land", "tin", "A thrown lump of coal landing on a car roof's tin and breaking",
        """The lump hitting the roof tin: the tin's bright knock and short ring (the packs' tin and a light plate), only a
        touch of the panel's hollow under it (a fist of coal, not a body), then breaking, its pieces clinking and
        skittering across the tin.""",
        sources=TIN + PLATE_L + STONES, takes=3, lufs=-20, mat="roof")
def coal_land_tin(rng, k):
    return landing(rng, k, "roof")


@recipe(L, "coal-land", "boards", "A thrown lump of coal landing on a car's boards and breaking",
        """A real wood knock on the boards with the floor's low hollow under it, then the lump's pieces clinking and
        rolling across the planks.""",
        sources=WOOD_L + STONES, takes=3, lufs=-20, mat="wood")
def coal_land_boards(rng, k):
    return landing(rng, k, "wood")


@recipe(L, "coal-land", "ballast", "A thrown lump of coal landing in the ballast",
        """The lump coming down among the stones (a real stone knock cut from the packs' gravel), breaking, a few pieces
        clinking off the rock and gone.""",
        sources=STONES, takes=3, lufs=-22, mat="ground")
def coal_land_ballast(rng, k):
    return landing(rng, k, "ground")
