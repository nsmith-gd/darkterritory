"""Searching the open houses (queue #148, note 412; note 326): a crewmate on foot holds Use at a hiding spot in an open village
house, for the kind's seconds (loot.json `search`: a cupboard 2.5 s, a cabinet 2, a cellar 4, the boards 5), and what it
kept comes out where it was kept. The game holds each kind's sound while the replicated search is under way and cuts it
when the hands come off; the find coming out is its own short sound.

Each sound runs about the kind's whole search, in the order hands would do it: a cupboard's door pulled open and its
shelves gone through (jars and crockery knocked together, tins, cloth, a box shoved along); a cabinet's drawers pulled on
dry runners and their oddments rattled; a cellar's hatch lifted and laid back, the steps creaking down and crates shifted
in a cold stone hole below; the boards prised up one by one, the bar's bite, the wood groaning and the nails squealing out
of the joists, the board cracking free and laid aside.

Gameplay foley, so the packs' real things first: Kenney RPG's doors, creaks, drawers' cousins (book and leather handling,
coins, metal clicks), cloth; Kenney's glass, tin, wood and plank hits; sfx_100's wood and misc handling. Where the packs
have nothing (a drawer's runner, a nail drawn out of a joist), stick-slip friction made for it (synth.creak, ck.friction).
The house's room is the game's (spaces.json `room`); the cellar's stone hole is the sound's own, since it's down there.
"""

import numpy as np

import dsp
import synth
from dsp import samples, hp, lp, env, mix
from recipes import crew_kit as ck
from recipes.crew_kit import recipe, R, S, K
from recipes.crew_items import hit_of

L = "crew-search"
OPENS = R("doorOpen_1", "doorOpen_2")
CREAKS = R("creak1", "creak2", "creak3")
GLASS = K("impactGlass_light")
JARS = S("glass_01", "glass_04", "glass_06")
TIN = K("impactTin_medium")
BOOKS = R("bookPlace1", "bookPlace2", "bookClose")
CLOTH = R("cloth1", "cloth2", "cloth3", "cloth4")
LEATHER = R("handleSmallLeather", "handleSmallLeather2", "dropLeather")
COINS = R("handleCoins", "handleCoins2")
CLICKS = R("metalClick") + S("misc_01", "misc_09")
WOOD_L, WOOD_M, WOOD_H = K("impactWood_light"), K("impactWood_medium"), K("impactWood_heavy")
PLANK = K("impactPlank_medium")
STEPS = K("footstep_wood")
DEAL = [310, 520, 870, 1340]        # thin deal: a cupboard door, a drawer, a floorboard
IRON = [2400, 3700, 5300, 7900]      # a cut nail's shank squealing in its hole


def dry(y):
    """A whisker of the furniture's own reflections: the room is the game's."""
    return ck.space(y, 0.12, 0.08, 6000, ((0.003, 0.4), (0.007, 0.25)))


def hit(key, rng, semis=0.0, length=0.3, tau=0.06, at=0.02):
    """One real handling hit, cut, pitched and choked (things in a cupboard knock, they don't ring)."""
    x = dsp.vari(hit_of(key, int(rng.integers(3)), length, -18), semis + rng.uniform(-0.8, 0.8))
    return ck.norm(ck.choke(x, at, tau))


def handling(rng, length, keys, n, semis=0.0, db=(-14, -4), lead=0.0, tau=0.06):
    """Hands going through things: `n` real handling hits from `keys` spread unevenly over `lead`..`length` s, a third of
    them with a quick second knock (a jar set against another)."""
    parts = []
    for t in np.sort(rng.uniform(lead, length, n)):
        parts.append((float(t), hit(keys[int(rng.integers(len(keys)))], rng, semis, tau=tau), rng.uniform(*db)))
        if rng.random() < 0.33:
            parts.append((float(t) + rng.uniform(0.02, 0.06), hit(keys[int(rng.integers(len(keys)))], rng, semis, tau=tau), rng.uniform(*db) - 4))
    return parts


def hinge(x, length, lo):
    """A real creak cut to at most `length` s and faded out, above `lo` Hz."""
    x = x[:samples(length)]
    n = len(x) / dsp.SR
    return ck.norm(hp(x, lo)) * env([(0, 1), (n * 0.75, 1), (n, 0)], n)


def rustle(rng, take, length):
    """Cloth and paper shifted under the hands (the packs' cloth, cut short)."""
    return ck.cloth(rng, take, min(length, 0.3), keys=tuple(CLOTH))


def runner(rng, length, out=True):
    """A drawer's wooden runner: stick-slip, deal on deal, quicker as it comes free."""
    rate = env([(0, 60), (length, 160)], length, "exp") if out else env([(0, 160), (length, 70)], length, "exp")
    s = synth.creak(length, rate, rng, body=[f * rng.uniform(1.2, 1.6) for f in DEAL], q=6, jitter=0.5, grit=0.6)
    f = ck.friction(rng, length, rate=220, lo=500, hi=3500, rough=0.7)
    return ck.norm(mix(hp(s, 200), f * 0.6)) * env([(0, 0), (0.02, 1), (length * 0.85, 0.9), (length, 0)], length)


def squeal(rng, length):
    """A cut nail drawn out of a joist: iron stick-slip, rising as it comes, rung high through its shank."""
    s = synth.creak(length, env([(0, 90), (length, 260)], length, "exp"), rng, body=[f * rng.uniform(0.9, 1.1) for f in IRON],
                    q=30, jitter=0.2, grit=0.25)
    return ck.norm(hp(s, 1200)) * env([(0, 0), (0.04, 0.7), (length * 0.7, 1), (length, 0)], length)


def groan(rng, length, lo=20, hi=70):
    """A board under the bar: deal groaning as it's levered, slow stick-slip through the plank."""
    s = synth.creak(length, env([(0, lo), (length, hi)], length, "exp"), rng, body=DEAL, q=10, jitter=0.4)
    return ck.norm(hp(s, 120)) * env([(0, 0), (0.05, 1), (length, 0.6)], length)


# ---- The cupboard ---------------------------------------------------------------------------------------------------------

@recipe(L, "cupboard", "crockery", "A cupboard gone through: its door pulled open, jars and crockery knocked about, a tin, cloth",
        """A kitchen cupboard's plank door pulled open on dry hinges (the packs' real creak, short and high for a small door),
        then hands through its shelves: jars and crockery knocked together (Kenney's light glass and sfx_100's clinks, choked
        short: they're stood on wood), a tin knocked over, cloth and paper shifted, a box shoved along the shelf (a book's
        thud on wood). About its 2.5 s search.""",
        sources=CREAKS + GLASS + JARS + TIN + CLOTH + BOOKS, takes=3, lufs=-22)
def cupboard_crockery(rng, k):
    door = hinge(dsp.vari(ck.align(ck.get(CREAKS[(k + 2) % 3])), 4 + rng.uniform(-1, 1)), 0.3, 300)
    parts = [(0, door, -10), (0.28, hit(BOOKS[k % 3], rng, 2, tau=0.04), -10)]
    parts += handling(rng, 2.4, GLASS + JARS, 9, 3, (-20, -9), lead=0.35, tau=0.05)
    parts += handling(rng, 2.4, TIN + BOOKS, 5, 0, (-16, -8), lead=0.45)
    parts += [(0.5 + 1.5 * rng.random(), rustle(rng, k + i, 0.25), -15) for i in range(2)]
    return dry(ck.place(parts, 2.6))


@recipe(L, "cupboard", "pantry", "A cupboard gone through: the door, then tins, boxes and sacking shoved about (no glass)",
        """The same cupboard's door, and a pantry's shelves gone through with nothing to break: tins knocked and slid
        (Kenney's tin, choked), a cardboard box and a crock shoved along (books' thuds and leather handling), sacking and
        paper rustled (the packs' cloth). Duller and quieter than the crockery.""",
        sources=CREAKS + TIN + BOOKS + LEATHER + CLOTH, takes=3, lufs=-23)
def cupboard_pantry(rng, k):
    door = hinge(dsp.vari(ck.align(ck.get(CREAKS[k % 3])), 3 + rng.uniform(-1, 1)), 0.25, 300)
    parts = [(0, door, -10)]
    parts += handling(rng, 2.4, TIN + BOOKS + LEATHER, 9, -1, (-16, -6), lead=0.25)
    parts += [(0.3 + 1.8 * rng.random(), rustle(rng, k + i, 0.3), -13) for i in range(3)]
    return dry(ck.place(parts, 2.6))


# ---- The cabinet ----------------------------------------------------------------------------------------------------------

@recipe(L, "cabinet", "drawers", "A cabinet gone through: a drawer pulled on its runner, oddments rattled, shoved back; another",
        """A chest of drawers: a drawer dragged out on its dry wooden runner (stick-slip, deal on deal: there's no drawer in
        the packs), its oddments rattled through (coins, a metal click, keys, a book knocked: the packs' real handling),
        shoved back in with a knock, and the next one pulled. About its 2 s search.""",
        sources=COINS + CLICKS + BOOKS + WOOD_L + CLOTH, takes=3, lufs=-22)
def cabinet_drawers(rng, k):
    parts = []
    t = 0.0
    for d in range(2):
        pull = 0.22 + rng.uniform(0, 0.08)
        parts.append((t, runner(rng, pull), -8))
        parts.append((t + pull, hit(WOOD_L[(k + d) % 5], rng, 2, tau=0.03), -12))
        parts += handling(rng, t + pull + 0.6, COINS + CLICKS + BOOKS, 4, 0, (-18, -8), lead=t + pull + 0.05)
        parts.append((t + pull + 0.1, rustle(rng, k + d, 0.2), -16))
        shove = 0.12 + rng.uniform(0, 0.05)
        parts.append((t + pull + 0.65, runner(rng, shove, out=False), -10))
        parts.append((t + pull + 0.65 + shove, hit(WOOD_M[(k + d + 2) % 5], rng, 1, tau=0.04), -6))
        t += pull + 0.65 + shove + rng.uniform(0.06, 0.14)
    return dry(ck.place(parts, max(2.1, t + 0.2)))


# ---- The cellar -----------------------------------------------------------------------------------------------------------

@recipe(L, "cellar", "hatch", "A cellar gone through: the hatch lifted and laid back, the steps down, crates shifted in the stone below",
        """The cellar's plank hatch heaved up on its strap hinges (the packs' real creak, dropped) and laid back on the boards
        with a heavy wooden flop (a heavy plank hit), two steps creaking down the wooden stair (Kenney's wood footsteps and a
        tread's creak), then down in a cold stone hole (its own small stone room, dark): a crate dragged and set down, a
        bottle knocked, sacking shifted. About its 4 s search.""",
        sources=CREAKS + PLANK + STEPS + WOOD_H + GLASS + CLOTH, takes=3, lufs=-21)
def cellar_hatch(rng, k):
    lift = hinge(dsp.vari(ck.align(ck.get(CREAKS[(k + 1) % 3])), -4 + rng.uniform(-1, 1)), 0.5, 150)
    flop = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLANK[k % 5]), -2 + rng.uniform(-0.5, 0.5)))), 0.06, 0.08)
    above = [(0, lift, -6), (0.5, flop, 0), (0.5, ck.floor(rng, "wood", k, 1.6, 0.5), -8)]
    for i, t in enumerate((0.95, 1.45)):
        step = ck.choke(ck.norm(ck.align(dsp.vari(ck.get(STEPS[(k + i) % 5]), -1 + rng.uniform(-0.5, 0.5)))), 0.05, 0.06)
        above += [(t, step, -6), (t + 0.03, groan(rng, 0.18, 40, 70), -16)]
    below = [(0.0, ck.friction(rng, 0.45, rate=90, lo=200, hi=1800, rough=0.8), -8),
             (0.45, hit(WOOD_H[(k + 1) % 5], rng, -1, tau=0.07), -2)]
    below += handling(rng, 1.4, GLASS + WOOD_L, 4, -2, (-16, -8), lead=0.6)
    below += [(0.8 + rng.random(), rustle(rng, k, 0.3), -12)]
    hole = lp(ck.place(below, 2.0), 3500, 2)
    hole = ck.space(hole, 0.9, 0.3, 3000, ((0.007, 0.5), (0.016, 0.35), (0.025, 0.2)))
    return dry(ck.place(above + [(1.9, hole, -3)], 4.1))


# ---- The boards -----------------------------------------------------------------------------------------------------------

@recipe(L, "boards", "prise", "Floorboards prised up: the bar's bite, the board groaning, the nails squealing out, laid aside",
        """A bar's iron bite under a floorboard's edge (a small metal knock into wood), the deal groaning as it's levered
        (stick-slip through the plank), the cut nails squealing out of the joist (iron stick-slip, high, rising: there's no
        nail drawn in the packs), the board cracking free (a real wood hit) and laid aside on the boards (a plank's knock).
        Board after board through its 5 s search, each a little quicker.""",
        sources=CLICKS + WOOD_L + WOOD_M + PLANK, takes=3, lufs=-20)
def boards_prise(rng, k):
    parts = []
    t = 0.0
    b = 0
    while t < 4.2:
        bite = hit(CLICKS[(k + b) % len(CLICKS)], rng, -6, tau=0.02)
        lever = max(0.45, 0.7 - 0.12 * b) + rng.uniform(0, 0.2)
        parts += [(t, bite, -8), (t + 0.02, hit(WOOD_M[(k + b) % 5], rng, 2, tau=0.03), -10),
                  (t + 0.1, groan(rng, lever), -6), (t + 0.25, squeal(rng, lever * 0.8), -12)]
        if rng.random() < 0.7:
            parts.append((t + 0.3 + lever * 0.5, squeal(rng, lever * 0.5), -15))
        crack = t + 0.1 + lever
        parts += [(crack, hit(WOOD_L[(k + b + 1) % 5], rng, -1, tau=0.03), -2),
                  (crack + 0.35 + rng.uniform(0, 0.15), ck.choke(ck.norm(ck.align(dsp.vari(ck.get(PLANK[(k + b) % 5]), 1))), 0.04, 0.06), -6)]
        t = crack + 0.9 + rng.uniform(0, 0.3)
        b += 1
    parts += handling(rng, t + 0.6, CLICKS + WOOD_L, 2, 0, (-18, -12), lead=t - 0.2)
    return dry(ck.place(parts, max(5.0, t + 0.6)))


# ---- Gone through ---------------------------------------------------------------------------------------------------------

@recipe(L, "found", "find", "The spot gone through: the find lifted out and set down where it was kept",
        """Done: the last of it pulled out (a cloth rustle), and the find set down on the boards where it was kept (a real
        small wooden knock with a tin or a leather handling under it), short so it reads as an ending.""",
        sources=CLOTH + WOOD_M + TIN + LEATHER, takes=3, lufs=-20)
def found_find(rng, k):
    under = (TIN + LEATHER)[k % (len(TIN) + len(LEATHER))]
    return dry(ck.place([(0, rustle(rng, k, 0.2), -8), (0.14, hit(WOOD_M[k % 5], rng, 0, tau=0.05), 0),
                         (0.145, hit(under, rng, 0, tau=0.04), -8)]))
