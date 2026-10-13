"""The Corruption's trees (the director, 13 Oct 2026, from eight renders; ARCHITECTURE §8 note 696): about three trees in
ten anywhere along the line are corrupted, in eight kinds, three of each, so you never see the same one twice running.
Each is a prop (content/art/models/props/ctree_<kind>_<n>.glb, 12 m tall, its foot at the origin: PlanArt scales it to
the Sim's height) and a distance copy (ctree_<kind>_<n>_far.glb) for the trees out past the chase camera's reach, worn in
the texture library's layers (tools/models/treekit.py), the hangings in mat_blight's cards.

  barnacle  drowned and haunted: a black wet spruce up on stilt roots, crusted to the crown in barnacles, tumours swelling
            out of the crust, a gaping mouth of a hollow ringed in shell teeth with a cold light down in it, long pale
            shrouds of lichen off its drooping limbs
  lantern   lit by what lives on it: pods of a glowing membrane on threads off every limb, breathing (scene.frag), heaped
            in a hollow; brackets dark above and their gills lit beneath; foxfire up the bark, glowing bonnets at its foot
  tar       a twisted trunk drowned in tar, every limb coated, globs sagging and strings hung between; dozens of drips
            thinning to a drop, which sag and draw back up (scene.vert); a pool with bubbles at its foot
  iron      spikes driven in all over, the bark split on raw meat round them and the rust bleeding down; split open its
            length and stapled across; barbed wire bitten in; burls of scar with iron out of them; rebar for limbs
  crystal   bone and quartz: clusters bursting from the trunk and every fork, a crown of them, a clump at every limb's
            end, a ring of big ones broken up out of the ground; milk white and smoked violet
  webbed    a birch clump with skin webbed between its limbs, in its crotches and between its stems, a cord of flesh
            along each web's hanging edge, skin grown over its bark in sleeves, its holes split on the meat
  thorn     a twisted trunk throwing claw-curved limbs, spiked, red skin webbed between them, cocoons hung off them, a
            few sprays of needles left; veins and blisters on it
  lattice   a trunk of roots woven round nothing, you see through it; skin in the gaps, knots of flesh, dead leaves

  blender -b --factory-startup --python tools/models/recipes/corrupted_trees.py [-- barnacle_0 lattice ...]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Quaternion, Vector

import cook
import impostor
import treekit as tk
from treekit import UP, Limb, Tree, grow, lerp, trunk

H = 12.0                 # built this tall; PlanArt scales to the Sim's height
R = 0.024 * H            # the trunk's radius a metre up: LinesideProp.Radius's for a corrupted tree (its wall)
KINDS = ["barnacle", "lantern", "tar", "iron", "crystal", "webbed", "thorn", "lattice"]
VARIANTS = 3
# Which way round a tree's face is (the barnacle spruce's mouth, the iron tree's split), as a bark angle (Limb.surface's,
# 0 toward Blender's +Y): toward `dt art trees`' camera, so the lineup shows it; the line turns each tree its own way.
FACING = -0.44


def ring_dir(a):
    return Vector((math.cos(a), math.sin(a), 0))


def limbs_off(t: Tree, stem: Limb, n, z0, z1, length, droop, r0, steps, wander=0.12, up=0.0, spread=2.4):
    """`n` limbs off `stem` between `z0` and `z1` of its length, turning round it by about the golden angle, longest low."""
    rng = t.rng
    out = []
    turn = rng.uniform(0, 6.28)
    for i in range(n):
        f = (i + rng.uniform(0.1, 0.9)) / n
        tt = lerp(z0, z1, f)
        p, tan, r = stem.at(tt)
        turn += spread + rng.uniform(-0.4, 0.4)
        out_dir = ring_dir(turn)
        d = (out_dir + UP * (up - droop * (1 - f))).normalized()
        L = length * (1.15 - 0.6 * f) * rng.uniform(0.75, 1.15)
        out.append(grow(rng, p + out_dir * r * 0.6, d, L, min(r0, r * 0.7), 0.004, steps, wander,
                        pull=-UP if droop > 0 else UP, pull_amount=abs(droop) * 0.08))
    return out


def draw(t: Tree, limb: Limb, sides, mat, tile=1.0, bulge=None, point=True):
    radii = list(limb.radii)
    if point:
        radii[-1] = 0.0
    return t.tube(limb.pts, radii, sides, mat, tile, point=point, bulge=bulge)


def bark_bulge(seed, amount=0.12, scale=1.7):
    return lambda i, j: 1 + amount * cook.noise3((i * 0.37, j * 0.9, seed), seed, scale)


def twigs(t: Tree, limb: Limb, n, length, mat, sides=3):
    """Short spiky dead twigs off a limb."""
    rng = t.rng
    for _ in range(n):
        p, tan, r = limb.at(rng.uniform(0.3, 0.95))
        side = tan.cross(ring_dir(rng.uniform(0, 6.28))).normalized()
        d = (tan * 0.6 + side + UP * rng.uniform(-0.3, 0.4)).normalized()
        t.cone(p, p + d * length * rng.uniform(0.5, 1.2), max(0.006, r * 0.6), sides, mat)


# ---------------------------------------------------------------------------------------------------------------------
# What the Corruption does to any tree it takes (the director's notes, 13 Oct: "it needs more corruption")


def weep(t: Tree, limb: Limb, tt, a, mat, fall=0.18, width=0.06, steps=5):
    """A run down the bark from a wound or a hole: wide at its source, narrowing as it goes, a little off plumb."""
    path, normals, widths = [], [], []
    for i in range(steps + 1):
        f = i / steps
        q, qn = limb.surface(max(0.0, tt - fall * f), a + 0.05 * math.sin(f * 4), 0)
        path.append(q)
        normals.append(qn)
        widths.append(width * (1.0 - 0.6 * f) * (1 + 0.25 * math.sin(f * 9)))
    t.streak(path, normals, widths, mat, proud=0.008)


def corrupt(t: Tree, stems, vein, flesh=None, lip=None, tumour=None, blister=None, weeping=None, veins=3, wounds=2, growths=2,
            crops=2, z=(0.06, 0.7)):
    """The marks every kind shares: veins of it crawling up the bark from the ground, splits weeping where the meat
    shows through, tumours swelling out of the wood, crops of blisters."""
    rng = t.rng
    for i in range(veins):
        s = stems[i % len(stems)]
        t.creep(s, rng.uniform(0.0, 0.06), rng.uniform(0.3, 0.75), vein, r=rng.uniform(0.02, 0.034), turns=rng.uniform(-0.6, 0.6),
                angle=rng.uniform(0, 6.28), steps=11, seed=rng.randrange(1 << 20))
    for i in range(wounds if flesh is not None else 0):
        s = stems[i % len(stems)]
        tt, a = rng.uniform(*z), rng.uniform(0, 6.28)
        p, n = s.surface(tt, a, 0)
        rx = rng.uniform(0.06, 0.1)
        t.wound(p, n, rx, rx * rng.uniform(1.7, 2.6), flesh, lip)
        if weeping is not None:
            weep(t, s, tt - rx * 2.2 / max(s.length, 1), a, weeping, fall=rng.uniform(0.08, 0.2), width=rx * 0.9)
    for i in range(growths if tumour is not None else 0):
        s = stems[i % len(stems)]
        p, n = s.surface(rng.uniform(*z), rng.uniform(0, 6.28), -0.03)
        t.growth(p, n, rng.uniform(0.09, 0.16), tumour, n=rng.randint(3, 5), seed=rng.randrange(1 << 20), seg=6, rings=4)
    for i in range(crops if blister is not None else 0):
        s = stems[i % len(stems)]
        p, n = s.surface(rng.uniform(*z), rng.uniform(0, 6.28), 0)
        t.pustules(p, n, 0.12, rng.randint(5, 8), blister, size=0.028, seed=rng.randrange(1 << 20))


def web(t: Tree, a: Limb, b: Limb, skin, cord, s0=0.05, s1=0.75, sag=0.14, droop=0.22, boils=None):
    """Skin stretched between two limbs from `s0` to `s1` along them, its free edge hanging in a curve between their
    tips, a cord of flesh along that edge so it has a thickness to it (webbed, like a foot's)."""
    pa, _, _ = a.at(s0)
    qa, _, _ = a.at(s1)
    pb, _, _ = b.at(s0)
    qb, _, _ = b.at(s1)
    span = (qa - qb).length
    mid = (qa + qb) / 2 - UP * span * droop
    t.sheet([pa, qa, mid, qb, pb], skin, sag=sag, tile=1.5)
    edge = [qa, qa.lerp(mid, 0.55) - UP * span * droop * 0.25, mid, qb.lerp(mid, 0.55) - UP * span * droop * 0.25, qb]
    t.tube(edge, [0.018, 0.032, 0.04, 0.032, 0.018], 4, cord, 0.3, point=False)
    if boils is not None:
        c = (pa + qa + mid + qb + pb) / 5 - UP * sag * span * 0.5
        n = (qa - pa).cross(pb - pa)
        if n.length > 1e-6:
            t.pustules(c, n.normalized(), 0.18, 3, boils, size=0.05, seed=int(abs(c.x * 1000 + c.y * 37)))
            t.pustules(c, -n.normalized(), 0.18, 2, boils, size=0.045, seed=int(abs(c.z * 1000 + c.x * 41)))


# ---------------------------------------------------------------------------------------------------------------------
# The kinds


def barnacle(t: Tree, v: int):
    """Drowned and haunted (the director: "This tree just looks deadish, let's make this look properly corrupted and
    haunted"): a black wet spruce up on stilt roots the tide's been at, crusted to the crown with barnacles, its bark
    split on the meat and weeping, tumours swelling out of the crust with barnacles grown on them; a gaping hollow of a
    mouth low on it, ringed in shell teeth, and a cold light far down in it, two smaller hollows over it; long pale
    shrouds of lichen hanging off its drooping limbs."""
    rng = t.rng
    bark = t.mat("pine_bark", (0.3, 0.31, 0.35), 0.45)
    crust = t.mat("barnacle_crust", (1.0, 1.0, 1.0), 0.3)
    shell = t.mat("mineral_growth", (2.1, 2.05, 1.95), 0.35)
    grey = t.mat("wood_grey", (0.34, 0.38, 0.42), 0.3)
    wrack = t.mat("wrack_card", (0.9, 0.95, 0.9), 0.2)
    shroud = t.mat("shroud_card", (0.9, 1.05, 1.3), 0.1)
    flesh = t.mat("flesh", (0.9, 0.8, 0.82), 0.5)
    meat = t.mat("wound", (0.9, 0.85, 0.85), 0.6)
    ichor = t.mat("tar", (0.6, 0.42, 0.42), 0.75)
    dark = t.mat("tar", (0.06, 0.05, 0.05), 0.0)
    cold = t.mat("fungal_crust", (0.6, 1.15, 1.05), 0.1, emissive=0.9)
    vein = t.mat("flesh", (0.5, 0.4, 0.5), 0.5)
    tall = H * (0.94, 1.0, 0.8)[v]
    stem = trunk(rng, tall, R * 1.0, 0.025 if v != 2 else 0.1, 12, lean=(0.5, 1.1, 0.7)[v], lean_to=rng.uniform(0, 6.28), wobble=0.016,
                 flare=0.45, twist=0.4)
    radii = list(stem.radii)
    if v != 2:
        radii[-1] = 0
    t.tube(stem.pts, radii, 8, bark, 1.0, point=v != 2, bulge=bark_bulge(v + 3, 0.12))
    # Crusted most of the way up, the crust's edge ragged.
    crusted = (0.55, 0.62, 0.7)[v]
    cut = max(2, int(len(stem.pts) * crusted))
    t.tube(stem.pts[:cut + 1], [r * 1.07 + 0.012 for r in stem.radii[:cut]] + [stem.radii[cut] * 0.92], 8, crust, 0.5, point=False,
           bulge=bark_bulge(v, 0.25, 3.0))
    # Up on stilt roots, arched out over the mud as a mangrove's are, crusted.
    roots = []
    for k in range(7 + v):
        a = k * 2.4 + rng.uniform(-0.3, 0.3)
        d = ring_dir(a)
        h0 = rng.uniform(0.5, 1.3)
        reach = rng.uniform(1.4, 2.5)
        p0, _ = stem.surface(h0 / tall, a, -0.04)
        root = Limb([p0, p0 + d * 0.45 + UP * 0.12, p0 + d * reach * 0.6 - UP * h0 * 0.35, Vector((0, 0, -0.25)) + d * reach],
                    [R * 0.48, R * 0.4, R * 0.3, 0.02])
        t.tube(root.pts, root.radii, 5, crust, 0.5, point=False)
        roots.append(root)
    # Dead limbs drooping all the way up, crusted on the low ones.
    limbs = limbs_off(t, stem, 15 + 2 * v, 0.2, 0.96, 2.8, 0.75, 0.075, 4, 0.18)
    for i, L in enumerate(limbs):
        draw(t, L, 3, grey, 0.6)
        if i < len(limbs) * crusted:
            half = Limb(L.pts[:3], [r * 1.35 + 0.015 for r in L.radii[:3]])
            t.tube(half.pts, half.radii, 4, crust, 0.5, point=False, bulge=bark_bulge(i, 0.3, 2.0))
            q, qn = L.surface(0.2, math.pi / 2, 0.01)
            t.barnacle(q, qn, 0.06, 0.05, shell, sides=4)
        twigs(t, L, 1, 0.5, grey)
    # The big barnacles in clusters standing out of the crust, so it's knobbled with them all the way up.
    for _ in range(10 + v):
        tt = (rng.random() ** 1.2) * crusted
        a = rng.uniform(0, 6.28)
        for _ in range(rng.randint(4, 6)):
            p, n = stem.surface(tt + rng.gauss(0, 0.012), a + rng.gauss(0, 0.25), 0.01)
            s = rng.uniform(0.06, 0.15) * (1.25 - tt * 0.6)
            t.barnacle(p, n, s, s * rng.uniform(0.7, 1.1), shell, sides=4)
    for root in roots[:5]:
        p, n = root.surface(rng.uniform(0.1, 0.6), math.pi / 2 + rng.uniform(-1, 1), 0.01)
        t.barnacle(p, n, 0.05, 0.04, shell, sides=4)
    # Tumours swelling out of the crust, barnacles grown on them too.
    for _ in range(2 + v % 2):
        tt = rng.uniform(0.12, crusted * 0.8)
        a = rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, -0.03)
        size = rng.uniform(0.13, 0.2)
        t.growth(p, n, size, flesh, n=4, seed=rng.randrange(1 << 20))
        for _ in range(3):
            q = p + n * size * 0.9 + Vector((rng.gauss(0, 0.06), rng.gauss(0, 0.06), rng.gauss(0, 0.06)))
            t.barnacle(q, (n + Vector((rng.gauss(0, 0.5), rng.gauss(0, 0.5), 0.3))).normalized(), 0.035, 0.03, shell, sides=4)
    # The mouth: a long gaping hollow, shell teeth round its lip, a cold light far down in it; two smaller hollows above.
    face = FACING + rng.uniform(-0.15, 0.15)
    zm = (1.9, 2.3, 1.7)[v] / tall
    lift = 0.06  # out past the crust (its thickness and its lumps)
    p, n = stem.surface(zm, face, lift)
    rx, ry = (0.17, 0.2, 0.15)[v], (0.42, 0.55, 0.38)[v]
    side = UP.cross(n).normalized()
    up = n.cross(side).normalized()
    t.hole(p, n, rx, ry, dark, crust, seg=10, proud=0.03)
    t.hole(p - up * ry * 0.18 + n * 0.004, n, rx * 0.62, ry * 0.5, cold, dark, seg=8, proud=0.03)
    for j in range(9):
        ang = 2 * math.pi * (j + 0.5) / 9
        rim = p + n * 0.03 + (side * math.cos(ang) * rx + up * math.sin(ang) * ry) * 1.05
        t.cone(rim, rim + (p - rim) * rng.uniform(0.25, 0.4) + n * 0.04, 0.035, 4, shell)
    if v != 1:
        for sgn in (-1, 1):
            q, qn = stem.surface(zm + 0.75 / tall, face + sgn * rng.uniform(0.38, 0.5), lift)
            e = rng.uniform(0.07, 0.095)
            t.hole(q, qn, e, e * rng.uniform(1.2, 1.5), dark, crust, seg=7, proud=0.025)
            t.hole(q + qn * 0.004, qn, e * 0.5, e * 0.55, cold, dark, seg=6, proud=0.025)
    # The Corruption's own marks over the drowning: veins up from the roots, splits weeping.
    corrupt(t, [stem], vein, meat, bark, None, None, ichor, veins=3, wounds=2 + v % 2, growths=0, crops=0, z=(crusted, 0.85))
    # Shrouds of beard lichen, long and pale, off the high limbs (the card's right half); wrack off the low ones (its left).
    for i, L in enumerate(limbs):
        f = i / max(1, len(limbs) - 1)
        if rng.random() < 0.2:
            continue
        p, _, _ = L.at(rng.uniform(0.35, 0.8))
        if f > 0.3:
            t.hang(p, rng.uniform(2.2, 3.8), rng.uniform(0.75, 1.0), shroud, turn=rng.uniform(0, 3.14), crossed=True, taper=0.25)
        else:
            t.hang(p, rng.uniform(1.2, 2.0), 0.45, wrack, 0.0, 0.5, turn=rng.uniform(0, 3.14), crossed=True, taper=0.8)


def mycena(t: Tree, at, normal, n, cap, foot, band):
    """A cluster of little glowing bonnet caps on thread-thin stems, crowded on the bark (foxfire): `n` of them, all one
    `band`, so the cluster breathes as one (scene.frag)."""
    rng = t.rng
    nrm = Vector(normal).normalized()
    for _ in range(n):
        base = Vector(at) + Vector((rng.gauss(0, 0.05), rng.gauss(0, 0.05), rng.gauss(0, 0.06)))
        d = (nrm * 0.8 + UP + Vector((rng.gauss(0, 0.3), rng.gauss(0, 0.3), 0))).normalized()
        top = base + d * rng.uniform(0.05, 0.15)
        t.tube([base, top], [0.006, 0.005], 3, foot, 0.2, point=False)
        r = rng.uniform(0.022, 0.042)
        t.blob(top, (r, r, r * 0.9), cap, rot=UP.rotation_difference(d), seg=5, rings=4, half=True, band=band)


def lantern(t: Tree, v: int):
    """Lit from within by what lives on it (the director: "make the lanterns natural bioluminescence that is
    haunting"): pods of a living membrane hung off every limb on threads, in strings, each glowing a cold green-white
    through its own veins and breathing slowly, out of step with the next; heaped glowing in a hollow; the brackets
    stacked up its trunk dark above and their gills lit beneath; foxfire threads of mycelium running up the bark, and
    clusters of little glowing bonnets at its foot."""
    rng = t.rng
    bark = t.mat("pine_bark", (0.4, 0.37, 0.37), 0.12)
    cap = t.mat("fungal_crust", (0.7, 0.64, 0.58), 0.12)
    gill = t.mat("fungal_crust", (0.6, 1.1, 0.95), 0.1, emissive=0.6)
    fox = t.mat("fungal_crust", (0.5, 1.0, 0.85), 0.1, emissive=0.45)
    dark = t.mat("tar", (0.08, 0.07, 0.06), 0.0)
    thread = t.mat("sac", (0.7, 0.85, 0.8), 0.45)
    film = t.mat("membrane_web", (0.6, 0.66, 0.7), 0.35)
    pods = [t.mat("glow_pod", c, 0.55) for c in ((0.8, 1.25, 1.05), (0.68, 1.12, 1.22), (0.95, 1.2, 0.88))]
    tall = H * (0.88, 0.96, 0.82)[v]
    stem = trunk(rng, tall * 0.58, R * 1.25, R * 0.8, 8, lean=0.4, lean_to=rng.uniform(0, 6.28), wobble=0.03, flare=0.9)
    t.tube(stem.pts, stem.radii, 8, bark, 1.0, point=False, bulge=bark_bulge(v, 0.2, 2.3))
    top, tan, r = stem.at(1.0)
    leaders = []
    for k in range((3, 3, 2)[v]):
        a = rng.uniform(0, 6.28) + k * 2.2
        L = grow(rng, top - UP * 0.25, (ring_dir(a) * 0.75 + UP).normalized(), tall * 0.5, r * 0.75, 0.02, 6, 0.12, UP, 0.04)
        draw(t, L, 5, bark, 1.0)
        leaders.append(L)
    limbs = []
    for L in [stem] + leaders:
        for B in limbs_off(t, L, 4, 0.35, 0.9, 3.0, -0.1, 0.08, 4, 0.15, up=0.3):
            draw(t, B, 4, bark, 0.8)
            limbs.append(B)
            twigs(t, B, 1, 0.6, bark)
            for C in limbs_off(t, B, 1, 0.4, 0.8, 1.2, 0.0, 0.03, 3, 0.2, up=0.2):
                draw(t, C, 3, bark, 0.5)
                limbs.append(C)
    # Brackets stacked up the trunk in columns, biggest low down, dark caps over lit gills.
    cols = [rng.uniform(0, 6.28) + k * 2.1 for k in range(3)]
    for a in cols:
        for k in range(5):
            tt = 0.08 + 0.85 * (k + rng.uniform(-0.2, 0.2)) / 5
            p, nrm = stem.surface(tt, a + rng.uniform(-0.3, 0.3), -0.03)
            size = rng.uniform(0.32, 0.55) * (1.25 - 0.45 * tt)
            t.shelf(p, nrm, size, size * 0.3, cap, seg=6, under=gill)
    for L in leaders:
        for k in range(2):
            p, nrm = L.surface(rng.uniform(0.1, 0.45), rng.uniform(0, 6.28), -0.02)
            size = rng.uniform(0.18, 0.3)
            t.shelf(p, nrm, size, size * 0.3, cap, seg=5, under=gill)
    # Foxfire: threads of lit mycelium up the bark, under the brackets and out along the leaders.
    for k in range(4):
        t.creep(stem, rng.uniform(0.0, 0.1), rng.uniform(0.55, 1.0), fox, r=rng.uniform(0.012, 0.02), turns=rng.uniform(-0.5, 0.5),
                angle=cols[k % 3] + rng.uniform(-0.6, 0.6), steps=10, seed=rng.randrange(1 << 20))
    for L in leaders:
        t.creep(L, 0.0, rng.uniform(0.4, 0.7), fox, r=0.012, turns=rng.uniform(-0.4, 0.4), angle=rng.uniform(0, 6.28), steps=6,
                seed=rng.randrange(1 << 20))
    band = 0
    # Bonnets clustered at its foot and up the trunk.
    for k in range(3):
        p, nrm = stem.surface(rng.uniform(0.02, 0.3) if k < 2 else rng.uniform(0.4, 0.7), rng.uniform(0, 6.28), 0)
        mycena(t, p, nrm, 6, pods[k % 3], thread, band)
        band += 1
    # The hollow, low down, heaped with pods.
    ha = cols[0] + 1.05
    hp, hn = stem.surface(0.32, ha, 0)
    t.hole(hp, hn, 0.24, 0.42, dark, bark, seg=10)
    for k in range(7):
        q = hp + hn * 0.05 + Vector((rng.gauss(0, 0.07), rng.gauss(0, 0.07), rng.gauss(0, 0.13)))
        s = rng.uniform(0.07, 0.11)
        t.blob(q, (s, s, s * 1.3), pods[k % 3], seg=5, rings=3, noise=0.1, seed=band, band=band)
        band += 1
    # Pods hung off the limbs on threads, in strings of one to three, each its own and breathing on its own.
    for i, B in enumerate(limbs[: 18 + 2 * v]):
        p, _, _ = B.at(rng.uniform(0.4, 0.85))
        end = p
        for j in range(1 if rng.random() < 0.5 else rng.randint(2, 3)):
            drop = rng.uniform(0.25, 0.8) if j == 0 else rng.uniform(0.06, 0.18)
            low = end - UP * drop
            t.tube([end, low], [0.008, 0.008], 3, thread, 0.3, point=False)
            s = rng.uniform(0.11, 0.2) * (1 - 0.2 * j)
            c = low - UP * s * 1.25
            t.blob(c, (s * 0.78, s * 0.78, s * 1.3), pods[(i + j) % 3], seg=5, rings=3, noise=0.12, seed=band, band=band)
            band += 1
            end = c - UP * s * 1.25
    # A few dead grey films between neighbouring limbs up in the crown.
    for k in range(2 + v % 2):
        a, b = limbs[(3 * k) % len(limbs)], limbs[(3 * k + 2) % len(limbs)]
        if (a.at(0.85)[0] - b.at(0.85)[0]).length < 3.5:
            t.sheet([a.at(0.15)[0], a.at(0.85)[0], b.at(0.85)[0], b.at(0.15)[0]], film, sag=0.14)


def tar(t: Tree, v: int):
    """Running with tar (the director: "It needs to feel more drippy, possibly even lightly animated in an excessively
    performantly way"): a twisted trunk drowned in it at its foot and streaming with it from every fork, every limb
    coated, globs of it sagging under them and strings of it hung between them; drips by the dozen off every limb, long
    and thinning to a thread with a drop at the end, sagging slowly and drawing back up (the "_drip" layer, in the vertex
    shader: no cost but a sin); a pool at its foot with bubbles standing in it."""
    rng = t.rng
    bark = t.mat("pine_bark", (0.36, 0.31, 0.3), 0.15)
    pitch = t.mat("tar", (1.0, 1.0, 1.0), 0.85)
    drip = t.mat("tar_drip", (1.0, 1.0, 1.0), 0.9)
    tall = H * (0.9, 0.82, 0.96)[v]
    stem = trunk(rng, tall, R * 1.1, 0.03, 14, lean=(0.6, 1.2, 0.3)[v], lean_to=rng.uniform(0, 6.28), wobble=0.035, flare=0.7, twist=1.2)
    draw(t, stem, 8, bark, 1.0, bulge=bark_bulge(v, 0.16, 2.0), point=True)
    # Its foot drowned in it, a swollen skirt of tar running into the pool, and the tar coating it up to where its limbs
    # start, ragged where it gives out.
    t.tube(stem.pts[:4], [r * 1.15 + 0.03 for r in stem.radii[:3]] + [stem.radii[3] * 1.02], 8, pitch, 0.5, point=False,
           bulge=bark_bulge(v + 5, 0.3, 2.5))
    coat_to = (6, 5, 7)[v]
    t.tube(stem.pts[3:coat_to + 1], [r * 1.06 + 0.012 for r in stem.radii[3:coat_to]] + [stem.radii[coat_to] * 0.96], 8, pitch, 0.5,
           point=False, bulge=bark_bulge(v + 9, 0.18, 2.0))
    # Streams down the trunk from the forks to the ground, swelling as they go.
    for k in range(13 + v):
        a = rng.uniform(0, 6.28)
        t0 = rng.uniform(0.2, 0.8)
        steps = 7
        path, normals, widths = [], [], []
        for i in range(steps + 1):
            tt = t0 * (1 - i / steps)
            p, n = stem.surface(tt, a + 0.22 * math.sin(i * 1.3 + k), 0)
            path.append(p)
            normals.append(n)
            widths.append(lerp(0.05, 0.18, i / steps) * (1 + 0.35 * math.sin(i * 2.1 + k)))
        t.streak(path, normals, widths, pitch, proud=0.014)
    # Limbs bowed down under it, every one coated, globs sagging under them, dripping.
    limbs = limbs_off(t, stem, 11 + v, 0.28, 0.93, 3.4, 0.5, 0.085, 5, 0.1)
    k = 0
    for L in limbs:
        draw(t, L, 4, bark, 0.8)
        coat = Limb(L.pts[1:], [r * 1.35 + 0.012 for r in L.radii[1:]])
        draw(t, coat, 4, pitch, 0.5, bulge=bark_bulge(k + 11, 0.25, 1.5))
        for _ in range(2):
            p, tan, r = L.at(rng.uniform(0.2, 0.8))
            s = r * 1.8 + 0.035
            t.blob(p - UP * s * 0.75, (s, s, s * 1.5), pitch, seg=4, rings=3)
        for _ in range(rng.randint(4, 6)):
            f = rng.uniform(0.2, 0.98)
            p, tan, r = L.at(f)
            long = rng.uniform(0.35, 1.2) * (1 + f) * (1.5 if rng.random() < 0.25 else 1)
            long = min(long, p.z - 0.5 - (long / 4) ** 2 * 1.2, 3.6)  # it sags as far again as (long/4)^2 * 1.2 (scene.vert)
            if long < 0.25:
                continue
            t.drip(p - UP * (r * 1.2), long, max(0.035, r * 1.5 + 0.02), drip, k, sides=4 if long > 1.2 else 3, bulb=0.85, lean=tan * 0.04)
            k += 1
    # Strings of it sagging between neighbouring limbs.
    for i in range(4 + v):
        A, B = limbs[i], limbs[(i + 2) % len(limbs)]
        pa, _, _ = A.at(rng.uniform(0.4, 0.8))
        pb, _, _ = B.at(rng.uniform(0.4, 0.8))
        span = (pa - pb).length
        if span > 2.6 or span < 0.4:
            continue
        mid = (pa + pb) / 2 - UP * span * 0.35
        pts = [pa, pa.lerp(mid, 0.6) - UP * span * 0.06, mid, pb.lerp(mid, 0.6) - UP * span * 0.06, pb]
        t.tube(pts, [0.012, 0.016, 0.024, 0.016, 0.012], 3, pitch, 0.3, point=False)
        long = min(rng.uniform(0.3, 0.9), mid.z - 0.6)
        if long > 0.2:
            t.drip(mid, long, 0.035, drip, k, sides=4, bulb=0.85)
            k += 1
    # A pool of it round its foot, its edge run out in tongues, bubbles standing in it.
    rim = []
    n = 14
    for j in range(n):
        a = 2 * math.pi * j / n
        rr = (1.2 + 0.6 * cook.noise3((math.cos(a) * 2, math.sin(a) * 2, v), v + 7, 1.0) + rng.uniform(0, 0.4)) * (1.25 if v == 1 else 1)
        rim.append(Vector((math.cos(a) * rr, math.sin(a) * rr, 0.03)))
    t.sheet(rim, pitch, sag=-0.015, tile=0.5)
    for _ in range(7):
        a, rr = rng.uniform(0, 6.28), rng.uniform(0.35, 1.1)
        s = rng.uniform(0.04, 0.1)
        t.blob(Vector((math.cos(a) * rr, math.sin(a) * rr, 0.03)), (s, s, s * 0.7), pitch, seg=5, rings=4, half=True)


def iron(t: Tree, v: int):
    """Iron gone into it and it gone wrong round the iron (the director: "This doesn't look corrupted enough"): rail
    spikes driven in all over it, the bark split open on raw meat round them and the rust bleeding out of every one;
    barbed wire wound round it and bitten in, the wood swelling between its turns; an I-beam through it with the tree
    grown round it in tumours of scar; rusted rebar come out of it where its limbs were and bent up like them; its bark
    gone to rusted scales in places; holes where the iron's been and gone, weeping."""
    rng = t.rng
    bark = t.mat("pine_bark", (0.36, 0.3, 0.28), 0.1)
    swell = t.mat("pine_bark", (0.5, 0.38, 0.35), 0.15)
    rust = t.mat("rust_heavy", (1.0, 0.9, 0.85), 0.2)
    steel = t.mat("rail_steel", (0.9, 0.72, 0.62), 0.35)
    plate = t.mat("iron_plate", (0.9, 0.62, 0.48), 0.25)
    bleed = t.mat("rust_heavy", (1.15, 0.5, 0.38), 0.45)
    meat = t.mat("wound", (1.0, 0.92, 0.9), 0.6)
    scar = t.mat("flesh", (1.0, 0.78, 0.72), 0.4)
    wiring = t.mat("rust_heavy", (0.75, 0.68, 0.64), 0.3)
    vein = t.mat("flesh", (0.7, 0.42, 0.42), 0.5)
    dark = t.mat("tar", (0.16, 0.08, 0.07), 0.3)
    tall = H * (0.88, 0.7, 0.95)[v]
    stem = trunk(rng, tall, R * 1.25, 0.06 if v == 1 else 0.025, 16, lean=0.5, lean_to=rng.uniform(0, 6.28), wobble=0.015, flare=0.6)
    draw(t, stem, 10, bark, 1.0, bulge=bark_bulge(v, 0.18), point=v != 1)
    # Its limbs mostly snapped off short; rebar come out where the others were, bent up like limbs, rusted.
    limbs = limbs_off(t, stem, 10, 0.35, 0.94, 2.2, 0.15, 0.1, 4, 0.16, up=0.2)
    for i, L in enumerate(limbs):
        if i % 3 == 1:
            p, _, _ = L.at(0.0)
            d = (L.pts[-1] - p).normalized()
            q = p + d * rng.uniform(0.9, 1.6)
            tip = q + (UP * 0.8 + d * 0.3).normalized() * rng.uniform(0.6, 1.2)
            t.tube([p - d * 0.05, p + d * 0.4, q, tip], [0.024] * 4, 4, rust, 0.4, point=False, smooth=False)
            t.growth(p, d, 0.07, swell, n=2, seed=rng.randrange(1 << 20), seg=5, rings=3)
            continue
        if i % 3 == 2:
            L = Limb(L.pts[:2], [L.radii[0], L.radii[0] * 0.7])
            t.tube(L.pts, L.radii, 5, bark, 0.8, point=False)
            continue
        draw(t, L, 5, bark, 0.8)
        twigs(t, L, 1, 0.5, bark)

    def I_beam(centre, along, length, w=0.26, h=0.34):
        along = along.normalized()
        up = (UP - along * along.z).normalized() if abs(along.z) < 0.95 else Vector((1, 0, 0))
        side = along.cross(up).normalized()
        L = along * length / 2
        t.box(centre + up * (h / 2 - 0.015), L, side * w / 2, up * 0.015, rust)
        t.box(centre - up * (h / 2 - 0.015), L, side * w / 2, up * 0.015, rust)
        t.box(centre, L, side * 0.013, up * (h / 2 - 0.025), rust)

    # An I-beam (two on the second) driven through it, out both sides, the tree swollen round it in scar tissue.
    for k in range((1, 2, 1)[v]):
        tt = rng.uniform(0.22, 0.5)
        p, _, r = stem.at(tt)
        a = rng.uniform(0, 6.28)
        along = (ring_dir(a) + UP * rng.uniform(-0.5, 0.6)).normalized()
        I_beam(p, along, rng.uniform(3.6, 5.2))
        for sgn in (-1, 1):
            q = p + along * sgn * r * 0.9
            t.growth(q, along * sgn, 0.14, swell, n=3, seed=rng.randrange(1 << 20), seg=5, rings=4)
            t.pustules(q + along * sgn * 0.12, along * sgn, 0.1, 4, scar, size=0.035, seed=rng.randrange(1 << 20))
    # Rail spikes hammered in all over it, heads out; the bark split open round a third of them, the rust bleeding down.
    for i in range(24 + 3 * v):
        tt = rng.uniform(0.05, 0.78)
        a = rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, 0)
        d = (n + Vector((rng.gauss(0, 0.25), rng.gauss(0, 0.25), rng.uniform(-0.4, 0.1)))).normalized()
        out = rng.uniform(0.35, 0.85)
        head = p + d * out
        t.tube([p - d * 0.06, head], [0.036, 0.034], 4, steel, 0.3, point=False, smooth=False)
        side = d.cross(UP).normalized() if abs(d.z) < 0.95 else Vector((1, 0, 0))
        t.box(head, d * 0.03, side * 0.08, d.cross(side).normalized() * 0.045, steel)
        if i % 3 == 0:
            t.wound(p, n, rng.uniform(0.08, 0.12), rng.uniform(0.13, 0.2), meat, swell, seg=6, swell=0.05)
        if i % 3 == 1:
            weep(t, stem, tt - 0.01, a, bleed, fall=rng.uniform(0.06, 0.2), width=rng.uniform(0.06, 0.1), steps=4)
    # Split open down its length on the meat, and stapled shut across it with iron.
    tt0 = rng.uniform(0.18, 0.3)
    sa = FACING + rng.uniform(-0.3, 0.3)
    p, n = stem.surface(tt0 + 1.2 / tall, sa, 0)
    t.wound(p, n, 0.2, 1.25, meat, swell, seg=10, swell=0.09, proud=0.03)
    side = UP.cross(n).normalized()
    for j in range(6):
        q, qn = stem.surface(tt0 + (0.25 + 0.38 * j) / tall, sa, 0.0)
        a0, b0 = q - side * 0.3 + qn * 0.02, q + side * 0.3 + qn * 0.02
        t.tube([a0 - qn * 0.06, a0 + qn * 0.06, b0 + qn * 0.06, b0 - qn * 0.06], [0.018] * 4, 4, rust, 0.3, point=False, smooth=False)
    weep(t, stem, tt0, sa, bleed, fall=tt0 * 0.95, width=0.16, steps=5)
    # Burls of scar swollen out of it with the iron coming out of them.
    for _ in range(2 + v % 2):
        tt, a = rng.uniform(0.35, 0.75), rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, -0.04)
        size = rng.uniform(0.25, 0.38)
        t.growth(p, n, size, swell, n=3, seed=rng.randrange(1 << 20), seg=6, rings=4)
        for _ in range(4):
            d = (n + Vector((rng.gauss(0, 0.5), rng.gauss(0, 0.5), rng.gauss(0, 0.4)))).normalized()
            q = p + n * size * 0.6
            t.tube([q - d * 0.1, q + d * rng.uniform(0.35, 0.7)], [0.026, 0.02], 4, steel, 0.3, point=False, smooth=False)
        t.wound(p + n * size * 0.95, n, size * 0.35, size * 0.45, meat, swell, seg=6, swell=0.03)
    # Barbed wire wound round it and bitten in, the wood swelling between its turns.
    for k in range((2, 2, 3)[v]):
        t0 = rng.uniform(0.08, 0.55)
        t1 = t0 + rng.uniform(0.05, 0.1)
        t.wire(stem, t0, t1, rng.uniform(3, 5), wiring, r=0.01, out=0.005, barbs=8, steps_per_turn=6, seed=rng.randrange(1 << 20))
        p, _, r = stem.at((t0 + t1) / 2)
        t.tube([p - UP * 0.08, p + UP * 0.08], [r * 1.12, r * 1.12], 10, swell, 0.6, point=False, bulge=bark_bulge(k + 2, 0.15, 3.0))
    # Its bark gone to rusted scales in places.
    for k in range(2):
        tt0 = rng.uniform(0.15, 0.6)
        a0 = rng.uniform(0, 6.28)
        for j in range(6):
            tt = tt0 + (j // 3) * 0.035 + rng.uniform(-0.008, 0.008)
            a = a0 + (j % 3) * 0.32 + (j // 3) * 0.16
            p, n = stem.surface(tt, a, 0.012)
            side = UP.cross(n).normalized()
            tilt = (UP - n * 0.35).normalized()
            t.box(p, side * 0.075, tilt * 0.09, n * 0.008, plate)
    # Fishplates bolted on.
    for _ in range(3 + v):
        tt = rng.uniform(0.08, 0.55)
        p, n = stem.surface(tt, rng.uniform(0, 6.28), 0.015)
        side = UP.cross(n).normalized()
        tilt = rng.uniform(-0.5, 0.5)
        up = (UP * math.cos(tilt) + side * math.sin(tilt)).normalized()
        t.box(p, up * 0.36, up.cross(n).normalized() * 0.07, n * 0.014, plate)
    # Holes where the iron's been and gone, weeping rust.
    for _ in range(4):
        tt, a = rng.uniform(0.1, 0.65), rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, 0)
        s = rng.uniform(0.09, 0.15)
        t.hole(p, n, s, s * rng.uniform(1.0, 1.6), dark, swell, seg=7)
        weep(t, stem, tt - s * 1.6 / tall, a, bleed, fall=rng.uniform(0.1, 0.25), width=s * 0.8)
    corrupt(t, [stem], vein, None, None, scar, None, None, veins=2, wounds=0, growths=2, crops=0, z=(0.05, 0.6))
    # The third: a length of rail bent through its crown.
    if v == 2:
        a = rng.uniform(0, 6.28)
        p, _, _ = stem.at(0.7)
        d = ring_dir(a)
        pts = [p - d * 1.8 + UP * 0.7, p - d * 0.5 + UP * 0.1, p + d * 0.6, p + d * 2.0 - UP * 0.8]
        t.tube(pts, [0.07] * 4, 4, rust, 0.5, point=False, smooth=False)


def crystal(t: Tree, v: int):
    """Turned to bone and quartz breaking out of every part of it (the director: "This needs so many more crystals"):
    clusters bursting out all up the trunk and out of every fork, a crown of them, points all along the limbs and a
    clump at each one's end, clumps hanging under, and a ring of big ones broken up out of the ground round its foot; milk
    white, some smoked grey-violet; a few red nodules and its hollows left."""
    rng = t.rng
    bone = t.mat("wood_grey", (1.75, 1.7, 1.55), 0.12)
    milk = t.mat("quartz", (1.1, 1.2, 1.4), 0.75)
    smoke = t.mat("quartz", (0.75, 0.72, 0.95), 0.8)
    nodule = t.mat("flesh", (1.0, 0.5, 0.4), 0.45)
    dark = t.mat("tar", (0.22, 0.16, 0.14), 0.0)
    tall = H * (0.84, 0.94, 0.78)[v]
    stem = trunk(rng, tall * 0.68, R * 1.15, R * 0.6, 10, lean=0.6, lean_to=rng.uniform(0, 6.28), wobble=0.04, flare=0.8, twist=0.6)
    t.tube(stem.pts, stem.radii, 8, bone, 1.0, point=False, bulge=bark_bulge(v, 0.24, 2.5), grain=True)
    top, tan, r = stem.at(1.0)
    crown = []
    for k in range(3):
        a = rng.uniform(0, 6.28) + k * 2.1
        L = grow(rng, top - UP * 0.15, (ring_dir(a) * 0.75 + UP).normalized(), tall * 0.42, r * 0.75, 0.01, 6, 0.2, UP, 0.03)
        t.tube(L.pts, L.radii[:-1] + [0], 6, bone, 0.8, point=True, grain=True)
        crown.append(L)
    limbs = []
    for L in [stem] + crown:
        for B in limbs_off(t, L, 3, 0.4, 0.92, 2.0, 0.0, 0.055, 5, 0.25, up=0.25):
            t.tube(B.pts, B.radii[:-1] + [0], 3, bone, 0.6, point=True, grain=True)
            limbs.append(B)

    def point(p, d, L, sides):
        t.crystal(p, d, L, L * rng.uniform(0.15, 0.22), smoke if rng.random() < 0.25 else milk, sides=sides, tip=0.2, tile=1.2)

    def cluster(p, n, count, size, spread=0.42):
        for _ in range(count):
            d = (n + Vector((rng.gauss(0, spread), rng.gauss(0, spread), rng.gauss(0, spread)))).normalized()
            L = size * rng.uniform(0.5, 1.3)
            q = p + Vector((rng.gauss(0, 0.05), rng.gauss(0, 0.05), rng.gauss(0, 0.05)))
            point(q, d, L, 6 if L > 0.8 else 5 if L > 0.4 else 4)

    # Clusters bursting out all up the trunk and the leaders.
    for k in range((12, 14, 11)[v]):
        p, n = stem.surface(rng.uniform(0.04, 0.97), rng.uniform(0, 6.28), -0.06)
        cluster(p, n, rng.randint(5, 7), rng.uniform(0.5, 1.15))
    for L in crown:
        for _ in range(2):
            p, n = L.surface(rng.uniform(0.15, 0.7), rng.uniform(0, 6.28), -0.03)
            cluster(p, n, 4, rng.uniform(0.25, 0.5))
    cluster(top, UP, 12, 1.2, spread=0.55)
    # Points along every limb and a clump at its end; clumps hung under some.
    for i, B in enumerate(limbs):
        p, n = B.surface(rng.uniform(0.3, 0.7), rng.uniform(0, 6.28), -0.01)
        point(p, (n + UP * 0.4).normalized(), rng.uniform(0.25, 0.45), 4)
        p, tan, _ = B.at(0.97)
        cluster(p, tan, 3, rng.uniform(0.35, 0.6), spread=0.5)
        if i % 4 == 0:
            p, _, _ = B.at(rng.uniform(0.45, 0.8))
            cluster(p - UP * 0.1, -UP, 4, 0.4)
    # A ring of big ones broken up out of the ground round its foot.
    for k in range((10, 12, 9)[v]):
        a = 2 * math.pi * k / 10 + rng.uniform(-0.3, 0.3)
        rr = rng.uniform(0.35, 1.0)
        base = Vector((math.cos(a) * rr, math.sin(a) * rr, -0.15))
        d = (ring_dir(a) * rng.uniform(0.3, 0.8) + UP).normalized()
        point(base, d, rng.uniform(0.8, 1.9) * (1.2 - rr * 0.4), 6)
        if rng.random() < 0.6:
            point(base + ring_dir(a + 1.5) * 0.12, (d + ring_dir(a + 1.5) * 0.4).normalized(), rng.uniform(0.25, 0.5), 4)
    # The hollows; red nodules.
    for _ in range(3):
        p, n = stem.surface(rng.uniform(0.1, 0.6), rng.uniform(0, 6.28), 0)
        s = rng.uniform(0.1, 0.16)
        t.hole(p, n, s, s * 1.7, dark, bone, seg=8)
    for _ in range(8 + v):
        L = [stem] + crown + limbs
        B = L[rng.randrange(len(L))]
        p, n = B.surface(rng.uniform(0.1, 0.9), rng.uniform(0, 6.28), -0.01)
        s = rng.uniform(0.08, 0.2) * (0.6 if B in limbs else 1)
        t.blob(p, (s, s, s * 0.85), nodule, rot=UP.rotation_difference(n), seg=5, rings=3, noise=0.15, seed=v)


def webbed(t: Tree, v: int):
    """A birch with skin grown between its limbs (the director: "This needs to look like skin and flesh more"): skin
    with pores and creases and bruises and veins, wet, webbed between the limbs and in the crotches and, on the two- and
    three-stemmed, between the stems, its free edges hanging in curves with a cord of flesh along them; its holes split
    on raw meat; tumours of flesh at its forks; veins of it running down the bark from the webs."""
    rng = t.rng
    birch = t.mat("wood_grey", (1.95, 1.9, 1.78), 0.1)
    mark = t.mat("tar", (0.32, 0.28, 0.26), 0.05)
    skin = t.mat("membrane_web", (1.2, 1.05, 1.08), 0.5)
    cord = t.mat("flesh", (1.25, 0.92, 0.88), 0.5)
    meat = t.mat("wound", (1.0, 0.95, 0.95), 0.6)
    tumour = t.mat("flesh", (1.15, 0.88, 0.84), 0.45)
    vein = t.mat("flesh", (0.85, 0.48, 0.5), 0.55)
    ichor = t.mat("wound", (0.6, 0.5, 0.5), 0.7)
    leaf = t.mat("leaf_card", (1.0, 1.0, 1.0), 0.05)
    stems = (1, 2, 3)[v]
    tall = H * 0.92
    trunks = []
    for k in range(stems):
        a = rng.uniform(0, 6.28) + k * 2.1
        lean = 0.4 + 1.0 * (stems > 1)
        s = trunk(rng, tall * rng.uniform(0.85, 1.0), R * (1.0 if stems == 1 else 0.75), 0.02, 14, lean=lean, lean_to=a, wobble=0.012, flare=0.45)
        off = ring_dir(a) * (0.15 if stems > 1 else 0)
        s = Limb([p + off for p in s.pts], s.radii)
        draw(t, s, 7, birch, 0.7)
        trunks.append(s)
    limbs, own = [], []
    for s in trunks:
        own.append([])
        for B in limbs_off(t, s, 8 - stems, 0.4, 0.94, 2.8, -0.05, 0.055, 5, 0.12, up=0.45):
            draw(t, B, 4 if stems == 1 else 3, birch, 0.5)
            limbs.append(B)
            own[-1].append(B)
            twigs(t, B, 1, 0.5, birch)
        for _ in range(10 - 2 * stems):
            p, n = s.surface(rng.uniform(0.04, 0.85), rng.uniform(0, 6.28), 0.004)
            t.hole(p, n, rng.uniform(0.05, 0.1), rng.uniform(0.008, 0.02), mark, birch, seg=4, proud=0.006)
        # Its holes split on the meat, weeping.
        for _ in range(4 - (stems > 2)):
            tt, a = rng.uniform(0.1, 0.65), rng.uniform(0, 6.28)
            p, n = s.surface(tt, a, 0)
            rx = rng.uniform(0.06, 0.09)
            t.wound(p, n, rx, rx * rng.uniform(1.9, 2.6), meat, birch, seg=8)
            weep(t, s, tt - rx * 2.4 / s.length, a, ichor, fall=rng.uniform(0.05, 0.14), width=rx * 0.8)
    # Skin webbed between neighbouring limbs, most of the crown.
    for k in range(8 + v):
        a = limbs[(2 * k) % len(limbs)]
        b = limbs[(2 * k + 1) % len(limbs)]
        if (a.at(0.75)[0] - b.at(0.7)[0]).length < 3.6:
            web(t, a, b, skin, cord, 0.03, 0.75, sag=0.24, droop=0.3, boils=tumour if k % 2 == 0 else None)
    # In the crotches: from the trunk above a limb out along it.
    for s, mine in zip(trunks, own):
        for B in mine:
            if rng.random() > 0.4:
                continue
            p0, _, _ = B.at(0.0)
            up = p0 + UP * rng.uniform(0.5, 0.9)
            q, _, _ = B.at(rng.uniform(0.35, 0.5))
            mid = (up + q) / 2 - UP * 0.15
            t.sheet([p0, q, mid, up], skin, sag=0.1, tile=1.5)
            t.tube([q, mid, up], [0.012, 0.02, 0.012], 3, cord, 0.3, point=False)
    # Between the stems.
    for i in range(stems - 1):
        a, b = trunks[i], trunks[i + 1]
        lo, hi = rng.uniform(0.2, 0.3), rng.uniform(0.45, 0.6)
        pa, qa = a.at(lo)[0], a.at(hi)[0]
        pb, qb = b.at(lo)[0], b.at(hi)[0]
        low = (pa + pb) / 2 - UP * 0.4
        t.sheet([pa, qa, qb, pb, low], skin, sag=0.08, tile=1.5)
        t.tube([pa, low, pb], [0.015, 0.03, 0.015], 3, cord, 0.3, point=False)
    # Skin grown over the bark in sleeves, torn here and there on the birch under it.
    for s in trunks:
        i0 = rng.randint(3, 5)
        i1 = min(len(s.pts) - 2, i0 + rng.randint(3, 5))
        t.tube(s.pts[i0:i1 + 1], [r * 1.07 + 0.01 for r in s.radii[i0:i1 + 1]], 7, skin, 1.5, point=False, bulge=bark_bulge(i0, 0.12, 2.0))
    corrupt(t, trunks, vein, None, None, tumour, None, None, veins=2 + stems, wounds=0, growths=2 + v, crops=0, z=(0.35, 0.85))
    # Dead leaves still hanging off the limbs.
    for B in limbs[::2]:
        p, _, _ = B.at(rng.uniform(0.5, 0.95))
        t.hang(p, rng.uniform(0.6, 1.1), 0.45, leaf, turn=rng.uniform(0, 3.14), crossed=True)


def thorn(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.45, 0.36, 0.34), 0.1)
    skin = t.mat("membrane_web", (0.9, 0.55, 0.55), 0.5)
    cord = t.mat("flesh", (0.8, 0.45, 0.42), 0.5)
    pod = t.mat("sac", (1.05, 0.86, 0.66), 0.35)
    needles = t.mat("pine_bough", (0.75, 0.78, 0.7), 0.25)
    thread = t.mat("sac", (0.8, 0.7, 0.6), 0.3)
    vein = t.mat("flesh", (0.7, 0.36, 0.38), 0.5)
    meat = t.mat("wound", (0.9, 0.85, 0.85), 0.6)
    blister = t.mat("sac", (1.1, 0.95, 0.85), 0.5)
    tall = H * (0.86, 0.96, 0.8)[v]
    stem = trunk(rng, tall, R * 1.1, 0.03, 16, lean=0.5, lean_to=rng.uniform(0, 6.28), wobble=0.035, flare=0.8, twist=1.0)
    draw(t, stem, 9, bark, 1.0, bulge=bark_bulge(v, 0.15, 2.2))
    # Claws: limbs that go out level, dipping a little, then sweep up to a point: a crescent, the point over the root.
    claws = []
    n = (9, 11, 8)[v]
    turn = rng.uniform(0, 6.28)
    for i in range(n):
        f = (i + 0.5) / n
        tt = lerp(0.26, 0.92, f)
        p, _, r = stem.at(tt)
        turn += 2.4 + rng.uniform(-0.3, 0.3)
        out = ring_dir(turn)
        reach = (3.4 - 1.8 * f) * rng.uniform(0.85, 1.15)
        steps = 9
        pts = []
        for k in range(steps + 1):
            s = k / steps
            ang = math.pi * 0.72 * s
            pts.append(p + out * (math.sin(ang) * reach * 0.85 + s * 0.2 * reach) + UP * ((1 - math.cos(ang)) * reach * 0.62 - 0.3 * reach * math.sin(ang * 1.4) * (1 - s)))
        radii = [lerp(min(0.1, r * 0.7), 0.0, s ** 0.75) for s in (k / steps for k in range(steps + 1))]
        C = Limb(pts, radii)
        t.tube(pts, radii, 6, bark, 0.7, point=True)
        claws.append(C)
        for _ in range(4):
            q, tan, rr = C.at(rng.uniform(0.15, 0.8))
            d = (tan.cross(UP).normalized() * rng.choice([-1, 1]) + UP * 0.5 - tan * 0.5).normalized()
            t.cone(q, q + d * rng.uniform(0.18, 0.4), max(0.014, rr * 0.7), 4, bark)
    # Thorns all up the trunk.
    for _ in range(30 + 4 * v):
        tt = rng.uniform(0.04, 0.92)
        p, nrm = stem.surface(tt, rng.uniform(0, 6.28), -0.01)
        d = (nrm + UP * rng.uniform(-0.2, 0.6)).normalized()
        t.cone(p, p + d * rng.uniform(0.15, 0.38) * (1.2 - tt * 0.6), 0.035, 4, bark)
    # Red skin webbed between neighbouring claws, close in to the trunk.
    for i in range(3 + v % 2):
        a, b = claws[2 * i], claws[2 * i + 1]
        if (a.at(0.4)[0] - b.at(0.4)[0]).length < 3.0:
            web(t, a, b, skin, cord, 0.06, 0.42, sag=0.12, droop=0.18)
    # Cocoons hung off the claws.
    for C in claws[1::2][:4 + v]:
        p, _, _ = C.at(rng.uniform(0.3, 0.55))
        drop = rng.uniform(0.3, 0.9)
        end = p - UP * drop
        t.tube([p, end], [0.013, 0.013], 3, thread, 0.3, point=False)
        s = rng.uniform(0.14, 0.22)
        t.blob(end - UP * s * 1.6, (s, s * 0.9, s * 1.9), pod, seg=6, rings=5, noise=0.08, seed=v)
    corrupt(t, [stem], vein, meat, bark, None, blister, None, veins=3, wounds=2, growths=0, crops=2, z=(0.05, 0.5))
    # A few sprays of needles left on the claws.
    for C in claws:
        if rng.random() < 0.75:
            p, tan, _ = C.at(rng.uniform(0.35, 0.6))
            d = (C.at(0.9)[0] - p).normalized()
            t.card(p, p + d * rng.uniform(0.9, 1.4), 0.6, UP, needles)


def lattice(t: Tree, v: int):
    rng = t.rng
    root = t.mat("pine_bark", (0.62, 0.52, 0.46), 0.1)
    skin = t.mat("membrane_web", (1.0, 0.9, 0.88), 0.45)
    knot = t.mat("flesh", (1.05, 0.8, 0.76), 0.45)
    blister = t.mat("sac", (1.1, 0.95, 0.85), 0.5)
    leaf = t.mat("leaf_card", (0.9, 0.85, 0.8), 0.05)
    tall = H * (0.84, 0.92, 0.78)[v]
    n = (9, 10, 8)[v]
    cage = tall * 0.7
    steps = 12
    strands = []
    for k in range(n):
        phase = 2 * math.pi * k / n
        wind = (1 if k % 2 == 0 else -1) * rng.uniform(2.3, 2.9)  # half wind one way, half the other: they cross
        pts, radii = [], []
        for i in range(steps + 1):
            s = i / steps
            z = cage * s
            # Splayed out as roots at the foot, drawn in to a waist, opening again where the crown starts.
            R0 = 1.0 * math.exp(-z / 0.6) + 0.28 + 0.1 * math.sin(s * math.pi) + 0.12 * s * s
            a = phase + wind * s * math.pi
            pts.append(Vector((math.cos(a) * R0, math.sin(a) * R0, z - (0.3 if i == 0 else 0))))
            radii.append(lerp(0.09, 0.055, s) * (1.35 if i < 2 else 1))
        t.tube(pts, radii, 4, root, 0.5, point=False)
        strands.append(Limb(pts, radii))
    # Out of the top of the cage the strands go on as the crown's limbs.
    limbs = []
    for k, S in enumerate(strands):
        p = S.pts[-1]
        out = Vector((p.x, p.y, 0)).normalized()
        L = grow(rng, p, (out * 0.7 + UP).normalized(), (tall - cage) * rng.uniform(1.1, 1.8), S.radii[-1], 0.004, 5, 0.2, UP, 0.02)
        draw(t, L, 5, root, 0.5)
        limbs.append(L)
        for B in limbs_off(t, L, 1, 0.3, 0.8, 1.6, 0.1, 0.03, 3, 0.2, up=0.2):
            draw(t, B, 3, root, 0.4)
            limbs.append(B)
    # Skin in the gaps, between two neighbouring strands over a stretch.
    for k in range((6, 7, 5)[v]):
        a = strands[(2 * k + k // 4) % n]
        b = strands[(2 * k + k // 4 + 1) % n]
        s0, s1 = rng.uniform(0.1, 0.45), rng.uniform(0.5, 0.8)
        t.sheet([a.at(s0)[0], a.at(s1)[0], b.at(s1)[0], b.at(s0)[0]], skin, sag=0.06)
    # Knots of flesh swollen where strands cross, blisters on a few.
    for k in range(3 + v % 2):
        S = strands[rng.randrange(n)]
        p, nrm = S.surface(rng.uniform(0.15, 0.8), rng.uniform(0, 6.28), -0.01)
        t.growth(p, nrm, rng.uniform(0.06, 0.1), knot, n=3, seed=rng.randrange(1 << 20), seg=5, rings=4)
    for k in range(2):
        S = strands[rng.randrange(n)]
        p, nrm = S.surface(rng.uniform(0.1, 0.5), rng.uniform(0, 6.28), 0)
        t.pustules(p, nrm, 0.08, 5, blister, size=0.025, seed=rng.randrange(1 << 20))
    # A few dead leaves hanging.
    for L in limbs[::2]:
        p, _, _ = L.at(rng.uniform(0.6, 0.95))
        t.hang(p, rng.uniform(0.5, 0.9), 0.4, leaf, turn=rng.uniform(0, 3.14), crossed=True)


BUILD = {k: globals()[k] for k in KINDS}


def main():
    """Each kind's three trees, each shot from two sides for the kind's distance cards, then the cards."""
    import tempfile
    wanted = cook.args()
    tmp = tempfile.mkdtemp(prefix="ctree_")
    for kind in KINDS:
        if wanted and not any(w in (kind, f"ctree_{kind}") or w.startswith((f"{kind}_", f"ctree_{kind}_")) for w in wanted):
            continue
        shots, used = [], set()
        for v in range(VARIANTS):
            cook.reset()
            t = Tree(f"ctree_{kind}_{v}", seed=6890 + KINDS.index(kind) * 31 + v * 7)
            BUILD[kind](t, v)
            used |= {m.name.split(".")[0] for m in t.mats}
            shots += impostor.shoot(t.export(), tmp)
        layer = impostor.bake(f"ctree_{kind}_far", shots, f"the {kind} trees, each from two sides: their distance copies' cards", used)
        for v in range(VARIANTS):
            cook.reset()
            impostor.cards(f"ctree_{kind}_{v}_far", layer, 2 * v, [shot[3] for shot in shots[2 * v: 2 * v + 2]])


main()
