"""The Corruption's trees (the director, 13 Oct 2026, from eight renders; ARCHITECTURE §8 note 696): about three trees in
ten anywhere along the line are corrupted, in eight kinds, three of each, so you never see the same one twice running.
Each is a prop (content/art/models/props/ctree_<kind>_<n>.glb, 12 m tall, its foot at the origin: PlanArt scales it to
the Sim's height) and a distance copy (ctree_<kind>_<n>_far.glb) for the trees out past the chase camera's reach, worn in
the texture library's layers (tools/models/treekit.py), the hangings in mat_blight's cards.

  barnacle  a spruce the tide's been at: barnacles and mussels crusting its foot and its roots on the mud, wrack and
            beard lichen hanging off dead spiky limbs
  lantern   bracket fungus stacked up the trunk, amber egg pods hung glowing off the limbs and heaped in a hollow, webs
  tar       a twisted trunk running with tar, black and glossy, hanging off every limb in drips; a pool of it at its foot
  iron      rail spikes, an I-beam and fishplates driven through it, holes in it, the rust run down it
  crystal   bleached like bone, hollowed, white quartz breaking out of it and hanging in clumps, red nodules, thorns
  webbed    a birch clump, oval holes in its bark, pink membranes grown between its limbs, dead leaves hanging
  thorn     a twisted trunk throwing claw-curved limbs, spiked, red webs between them, cocoons hung off them, a few sprays
            of needles left
  lattice   a trunk of roots woven round nothing, you see through it; skin in some of the gaps, a few dead leaves

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
# The kinds


def barnacle(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.8, 0.78, 0.74), 0.08)
    crust = t.mat("mineral_growth", (1.35, 1.32, 1.25), 0.25)
    shell = t.mat("mineral_growth", (2.1, 2.05, 1.95), 0.35)
    mussel = t.mat("tar", (0.55, 0.62, 0.85), 0.55)
    wrack = t.mat("wrack_card", (1.0, 1.0, 1.0), 0.15)
    grey = t.mat("wood_grey", (0.72, 0.72, 0.7), 0.05)
    tall = H * (0.92, 1.0, 0.74)[v]
    stem = trunk(rng, tall, R * 0.95, 0.025 if v != 2 else 0.09, 12, lean=(0.3, 0.9, 0.5)[v], lean_to=rng.uniform(0, 6.28), wobble=0.012, flare=0.8)
    crusted = (0.3, 0.42, 0.34)[v]
    radii = list(stem.radii)
    if v != 2:
        radii[-1] = 0
    t.tube(stem.pts, radii, 7, bark, 1.0, point=v != 2, bulge=bark_bulge(v + 3, 0.1))
    # The foot sheathed in old crust, ragged, under the barnacles.
    cut = max(2, int(len(stem.pts) * crusted * 0.55))
    t.tube(stem.pts[:cut + 1], [r * 1.06 + 0.012 for r in stem.radii[:cut]] + [stem.radii[cut] * 0.9], 7, crust, 0.5, point=False,
           bulge=bark_bulge(v, 0.3, 3.0))
    # Roots out over the mud, crusted.
    roots = []
    for k in range(6 + v):
        a = k * 2.4 + rng.uniform(-0.3, 0.3)
        d = ring_dir(a)
        reach = 1.6 + rng.uniform(0, 1.2)
        root = Limb([Vector((0, 0, 0.45)), d * 0.6 + Vector((0, 0, 0.18)), d * reach * 0.6 + Vector((0, 0, 0.0)), d * reach + Vector((0, 0, -0.2))],
                    [R * 0.6, R * 0.45, R * 0.28, 0.01])
        t.tube(root.pts, root.radii, 5, crust, 0.5, point=True)
        roots.append(root)
    # Dead limbs all the way up: short, spiky, drooping low down, a few gone.
    limbs = limbs_off(t, stem, 16 + 2 * v, 0.22, 0.97, 2.4, 0.4, 0.07, 3, 0.16)
    for L in limbs:
        draw(t, L, 3, grey, 0.6)
        twigs(t, L, 3, 0.5, grey)
    # Barnacles thick over the foot and the roots, thinning up the trunk; mussels in clumps among them.
    for _ in range(34 + 4 * v):
        tt = (rng.random() ** 1.8) * crusted
        p, n = stem.surface(tt, rng.uniform(0, 6.28), 0.0)
        s = rng.uniform(0.045, 0.1) * (1.35 - tt)
        t.barnacle(p, n, s, s * rng.uniform(0.6, 1.0), shell, sides=4)
    for root in roots:
        for _ in range(3):
            p, n = root.surface(rng.uniform(0.05, 0.75), rng.uniform(-1.3, 1.3) + math.pi / 2, 0)
            s = rng.uniform(0.035, 0.065)
            t.barnacle(p, n, s, s * 0.8, shell, sides=4)
    for _ in range(4 + v):
        p, n = stem.surface(rng.uniform(0.0, crusted * 0.5), rng.uniform(0, 6.28), 0.02)
        for m in range(4):
            q = p + Vector((rng.gauss(0, 0.08), rng.gauss(0, 0.08), rng.gauss(0, 0.12)))
            rot = Vector((0, 0, 1)).rotation_difference((n + Vector((rng.gauss(0, 0.4), rng.gauss(0, 0.4), -0.9))).normalized())
            t.blob(q, (0.035, 0.024, 0.085), mussel, rot=rot, seg=4, rings=3)
    # Wrack off the low limbs (the card's left half), the beard lichen in locks higher up (its right).
    for i, L in enumerate(limbs):
        f = i / max(1, len(limbs) - 1)
        if rng.random() < 0.25:
            continue
        p, _, _ = L.at(rng.uniform(0.3, 0.75))
        lichen = f > 0.4
        t.hang(p, rng.uniform(0.8, 1.5) if lichen else rng.uniform(1.2, 2.2), 0.5 if lichen else 0.45, wrack,
               0.5 if lichen else 0.0, 1.0 if lichen else 0.5, turn=rng.uniform(0, 3.14), crossed=True, taper=0.45 if lichen else 0.8)


def lantern(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.55, 0.47, 0.44), 0.08)
    fungus = t.mat("fungal_crust", (1.5, 1.08, 0.62), 0.12)
    pod = t.mat("sac", (1.8, 1.08, 0.36), 0.4, emissive=0.85)
    dark = t.mat("tar", (0.22, 0.15, 0.1), 0.0)
    web = t.mat("membrane_web", (1.15, 1.15, 1.12), 0.3)
    thread = t.mat("sac", (1.2, 1.1, 0.9), 0.3)
    tall = H * (0.88, 0.96, 0.82)[v]
    stem = trunk(rng, tall * 0.58, R * 1.25, R * 0.8, 8, lean=0.4, lean_to=rng.uniform(0, 6.28), wobble=0.03, flare=0.9)
    t.tube(stem.pts, stem.radii, 8, bark, 1.0, point=False, bulge=bark_bulge(v, 0.2, 2.3))
    # It forks: leaders up and out, each branching again, a wide crown.
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
            twigs(t, B, 2, 0.6, bark)
            for C in limbs_off(t, B, 1, 0.4, 0.8, 1.2, 0.0, 0.03, 3, 0.2, up=0.2):
                draw(t, C, 3, bark, 0.5)
                limbs.append(C)
    # Brackets stacked up the trunk in columns, biggest low down.
    cols = [rng.uniform(0, 6.28) + k * 2.1 for k in range(3)]
    for a in cols:
        n = 6
        for k in range(n):
            tt = 0.08 + 0.85 * (k + rng.uniform(-0.2, 0.2)) / n
            p, nrm = stem.surface(tt, a + rng.uniform(-0.3, 0.3), -0.03)
            size = rng.uniform(0.32, 0.55) * (1.25 - 0.45 * tt)
            t.shelf(p, nrm, size, size * 0.3, fungus, seg=6)
    # The hollow, low down, heaped with pods.
    ha = cols[0] + 1.05
    hp, hn = stem.surface(0.32, ha, 0)
    t.hole(hp, hn, 0.24, 0.42, dark, bark, seg=10)
    for k in range(8):
        q = hp + hn * 0.06 + Vector((rng.gauss(0, 0.08), rng.gauss(0, 0.08), rng.gauss(0, 0.15)))
        t.blob(q, (0.1, 0.1, 0.14), pod, seg=4, rings=3)
    # Pods hung glowing off the limbs on threads.
    for B in limbs[: 16 + 2 * v]:
        p, _, _ = B.at(rng.uniform(0.4, 0.85))
        drop = rng.uniform(0.3, 1.0)
        end = p - UP * drop
        t.tube([p, end], [0.012, 0.012], 3, thread, 0.3, point=False)
        s = rng.uniform(0.13, 0.22)
        t.blob(end - UP * s, (s * 0.78, s * 0.78, s * 1.3), pod, seg=4, rings=3)
    # Webs between neighbouring limbs, up in the crown, grey-white.
    for k in range(5 + v):
        a, b = limbs[(3 * k) % len(limbs)], limbs[(3 * k + 2) % len(limbs)]
        pa, _, _ = a.at(0.15)
        pb, _, _ = b.at(0.15)
        qa, _, _ = a.at(0.85)
        qb, _, _ = b.at(0.85)
        if (pa - pb).length < 4.0 and (qa - qb).length < 4.0:
            t.sheet([pa, qa, qb, pb], web, sag=0.14)


def tar(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.42, 0.36, 0.34), 0.1)
    pitch = t.mat("tar", (1.0, 1.0, 1.0), 0.8)
    tall = H * (0.9, 0.82, 0.96)[v]
    stem = trunk(rng, tall, R * 1.1, 0.03, 14, lean=(0.6, 1.2, 0.3)[v], lean_to=rng.uniform(0, 6.28), wobble=0.035, flare=0.7, twist=1.2)
    draw(t, stem, 8, bark, 1.0, bulge=bark_bulge(v, 0.16, 2.0), point=True)
    # Black streams down the trunk from where the limbs come off to the ground, swelling as they go.
    for k in range(9 + v):
        a = rng.uniform(0, 6.28)
        t0 = rng.uniform(0.25, 0.75)
        steps = 6
        path, normals, widths = [], [], []
        for i in range(steps + 1):
            tt = t0 * (1 - i / steps)
            p, n = stem.surface(tt, a + 0.22 * math.sin(i * 1.3 + k), 0)
            path.append(p)
            normals.append(n)
            widths.append(lerp(0.05, 0.14, i / steps) * (1 + 0.35 * math.sin(i * 2.1 + k)))
        t.streak(path, normals, widths, pitch, proud=0.012)
    # Limbs bowed down under it, every one coated and dripping.
    limbs = limbs_off(t, stem, 11 + v, 0.28, 0.93, 3.4, 0.5, 0.085, 5, 0.1)
    for L in limbs:
        draw(t, L, 4, bark, 0.8)
        coat = Limb(L.pts[len(L.pts) // 3:], [r * 1.3 + 0.008 for r in L.radii[len(L.pts) // 3:]])
        draw(t, coat, 4, pitch, 0.5)
        for _ in range(5):
            p, tan, r = L.at(rng.uniform(0.35, 0.98))
            long = rng.uniform(0.3, 1.4) * (1.8 if rng.random() < 0.2 else 1)
            long = min(long, p.z - 0.15)  # a low limb's drips stop short of the ground (its pool takes them)
            if long < 0.2:
                continue
            t.cone(p, p - UP * long + tan * 0.05, max(0.025, r * 1.3 + 0.018), 4, pitch, 0.3)
            if long > 0.8:
                t.blob(p - UP * long * 0.85, (0.04, 0.04, 0.07), pitch, seg=5, rings=3)
    # A pool of it round its foot, its edge run out in tongues.
    rim = []
    k = 14
    for j in range(k):
        a = 2 * math.pi * j / k
        rr = (1.0 + 0.5 * cook.noise3((math.cos(a) * 2, math.sin(a) * 2, v), v + 7, 1.0) + rng.uniform(0, 0.35)) * (1.25 if v == 1 else 1)
        rim.append(Vector((math.cos(a) * rr, math.sin(a) * rr, 0.03)))
    t.sheet(rim, pitch, sag=-0.012, tile=0.5)


def iron(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.48, 0.4, 0.37), 0.08)
    rust = t.mat("rust_heavy", (1.0, 0.9, 0.85), 0.2)
    steel = t.mat("rail_steel", (0.9, 0.72, 0.62), 0.35)
    plate = t.mat("iron_plate", (0.9, 0.62, 0.48), 0.25)
    run = t.mat("rust_heavy", (1.5, 0.75, 0.45), 0.15)
    dark = t.mat("tar", (0.2, 0.14, 0.11), 0.0)
    tall = H * (0.88, 0.7, 0.95)[v]
    stem = trunk(rng, tall, R * 1.25, 0.06 if v == 1 else 0.025, 16, lean=0.5, lean_to=rng.uniform(0, 6.28), wobble=0.015, flare=0.6)
    draw(t, stem, 10, bark, 1.0, bulge=bark_bulge(v, 0.15), point=v != 1)
    # Its limbs mostly snapped off short: stubs, a few long dead ones.
    limbs = limbs_off(t, stem, 10, 0.35, 0.94, 2.2, 0.15, 0.1, 4, 0.16, up=0.2)
    for i, L in enumerate(limbs):
        if i % 3 != 0:
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

    # An I-beam (two on the second) driven through it, out both sides.
    for k in range((1, 2, 1)[v]):
        p, _, _ = stem.at(rng.uniform(0.22, 0.5))
        a = rng.uniform(0, 6.28)
        along = (ring_dir(a) + UP * rng.uniform(-0.5, 0.6)).normalized()
        I_beam(p, along, rng.uniform(3.6, 5.2))
    # Rail spikes hammered in all over it, heads out, the rust run down from each.
    for _ in range(34 + 5 * v):
        tt = rng.uniform(0.05, 0.75)
        a = rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, 0)
        d = (n + Vector((rng.gauss(0, 0.25), rng.gauss(0, 0.25), rng.uniform(-0.4, 0.1)))).normalized()
        out = rng.uniform(0.25, 0.6)
        head = p + d * out
        t.tube([p - d * 0.06, head], [0.03, 0.03], 4, steel, 0.3, point=False, smooth=False)
        side = d.cross(UP).normalized() if abs(d.z) < 0.95 else Vector((1, 0, 0))
        t.box(head, d * 0.025, side * 0.065, d.cross(side).normalized() * 0.038, steel)
        path, normals, widths = [], [], []
        fall = rng.uniform(0.04, 0.12)
        for i in range(4):
            q, qn = stem.surface(max(0, tt - fall * i / 3), a + 0.03 * i, 0)
            path.append(q)
            normals.append(qn)
            widths.append(lerp(0.09, 0.03, i / 3))
        t.streak(path, normals, widths, run)
    # Fishplates bolted on.
    for _ in range(4 + v):
        tt = rng.uniform(0.08, 0.55)
        a = rng.uniform(0, 6.28)
        p, n = stem.surface(tt, a, 0.015)
        side = UP.cross(n).normalized()
        tilt = rng.uniform(-0.5, 0.5)
        up = (UP * math.cos(tilt) + side * math.sin(tilt)).normalized()
        t.box(p, up * 0.36, up.cross(n).normalized() * 0.07, n * 0.014, plate)
    # Holes where the iron's been and gone.
    for _ in range(5):
        p, n = stem.surface(rng.uniform(0.1, 0.65), rng.uniform(0, 6.28), 0)
        s = rng.uniform(0.09, 0.16)
        t.hole(p, n, s, s * rng.uniform(1.0, 1.6), dark, bark, seg=7)
    # The third: a length of rail bent through its crown.
    if v == 2:
        a = rng.uniform(0, 6.28)
        p, _, _ = stem.at(0.7)
        d = ring_dir(a)
        pts = [p - d * 1.8 + UP * 0.7, p - d * 0.5 + UP * 0.1, p + d * 0.6, p + d * 2.0 - UP * 0.8]
        t.tube(pts, [0.07] * 4, 4, rust, 0.5, point=False, smooth=False)


def crystal(t: Tree, v: int):
    rng = t.rng
    bone = t.mat("wood_grey", (1.75, 1.7, 1.55), 0.12)
    quartz = t.mat("quartz", (1.25, 1.25, 1.28), 0.7)
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
        for B in limbs_off(t, L, 4, 0.4, 0.92, 2.0, 0.0, 0.055, 5, 0.25, up=0.25):
            t.tube(B.pts, B.radii[:-1] + [0], 3, bone, 0.6, point=True, grain=True)
            limbs.append(B)
            twigs(t, B, 3, 0.4, bone)

    def cluster(p, n, count, size):
        for _ in range(count):
            d = (n + Vector((rng.gauss(0, 0.42), rng.gauss(0, 0.42), rng.gauss(0, 0.42)))).normalized()
            L = size * rng.uniform(0.6, 1.3)
            t.crystal(p + Vector((rng.gauss(0, 0.05), rng.gauss(0, 0.05), rng.gauss(0, 0.05))), d, L, L * rng.uniform(0.09, 0.14), quartz,
                      sides=5)

    # Quartz breaking out of the trunk and the forks, big.
    for k in range((5, 6, 4)[v]):
        p, n = stem.surface(rng.uniform(0.12, 0.95), rng.uniform(0, 6.28), -0.05)
        cluster(p, n, 5, rng.uniform(0.5, 1.0))
    cluster(top, UP, 6, 0.8)
    # Clumps hung off the limbs.
    for B in limbs[:5]:
        p, _, _ = B.at(rng.uniform(0.45, 0.8))
        cluster(p - UP * 0.15, -UP, 4, 0.45)
    # The hollows; red nodules.
    for _ in range(3):
        p, n = stem.surface(rng.uniform(0.1, 0.6), rng.uniform(0, 6.28), 0)
        s = rng.uniform(0.1, 0.16)
        t.hole(p, n, s, s * 1.7, dark, bone, seg=8)
    for _ in range(12 + 2 * v):
        L = [stem] + crown + limbs
        B = L[rng.randrange(len(L))]
        p, n = B.surface(rng.uniform(0.1, 0.9), rng.uniform(0, 6.28), -0.01)
        s = rng.uniform(0.08, 0.2) * (0.6 if B in limbs else 1)
        t.blob(p, (s, s, s * 0.85), nodule, rot=UP.rotation_difference(n), seg=5, rings=3, noise=0.15, seed=v)


def webbed(t: Tree, v: int):
    rng = t.rng
    birch = t.mat("wood_grey", (1.95, 1.9, 1.78), 0.1)
    mark = t.mat("tar", (0.32, 0.28, 0.26), 0.05)
    dark = t.mat("tar", (0.16, 0.08, 0.08), 0.0)
    skin = t.mat("membrane_web", (1.55, 0.72, 0.68), 0.45)
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
    limbs = []
    for s in trunks:
        for B in limbs_off(t, s, 8 - stems, 0.4, 0.94, 2.8, -0.05, 0.055, 5, 0.12, up=0.45):
            draw(t, B, 4 if stems == 1 else 3, birch, 0.5)
            limbs.append(B)
            twigs(t, B, 3, 0.5, birch)
        # The birch's black marks: short dark slashes across its bark.
        for _ in range(12 - 2 * stems):
            p, n = s.surface(rng.uniform(0.04, 0.85), rng.uniform(0, 6.28), 0.004)
            t.hole(p, n, rng.uniform(0.05, 0.1), rng.uniform(0.008, 0.02), mark, birch, seg=4, proud=0.006)
        # Oval holes in it, long ways up.
        for _ in range(4):
            p, n = s.surface(rng.uniform(0.12, 0.65), rng.uniform(0, 6.28), 0)
            rx = rng.uniform(0.07, 0.1)
            t.hole(p, n, rx, rx * rng.uniform(1.9, 2.6), dark, birch, seg=8)
    # Skin between neighbouring limbs (and limb and trunk), most of the crown.
    for k in range(7 + v):
        a = limbs[(2 * k) % len(limbs)]
        b = limbs[(2 * k + 1) % len(limbs)]
        pa, _, _ = a.at(0.0)
        qa, _, _ = a.at(0.75)
        pb, _, _ = b.at(0.05)
        qb, _, _ = b.at(0.7)
        if (qa - qb).length < 3.6:
            t.sheet([pa, qa, qb, pb], skin, sag=0.1)
        else:
            mid = (pa + qa) / 2 - UP * 0.35
            t.sheet([pa, qa, mid], skin, sag=0.06)
    # Dead leaves hanging off the limbs.
    for B in limbs:
        for _ in range(2):
            p, _, _ = B.at(rng.uniform(0.4, 0.95))
            t.hang(p, rng.uniform(0.6, 1.1), 0.45, leaf, turn=rng.uniform(0, 3.14), crossed=True)


def thorn(t: Tree, v: int):
    rng = t.rng
    bark = t.mat("pine_bark", (0.45, 0.36, 0.34), 0.1)
    vein = t.mat("membrane_web", (1.25, 0.36, 0.32), 0.45)
    pod = t.mat("sac", (1.05, 0.86, 0.66), 0.35)
    needles = t.mat("pine_bough", (0.75, 0.78, 0.7), 0.25)
    thread = t.mat("sac", (0.8, 0.7, 0.6), 0.3)
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
    # Red webs between a few neighbouring claws, close in to the trunk.
    for i in range(2 + v % 2):
        a, b = claws[2 * i], claws[2 * i + 1]
        pa, _, _ = a.at(0.06)
        qa, _, _ = a.at(0.4)
        pb, _, _ = b.at(0.06)
        qb, _, _ = b.at(0.4)
        if (qa - qb).length < 3.0:
            t.sheet([pa, qa, qb, pb], vein, sag=0.12)
    # Cocoons hung off the claws.
    for C in claws[1::2][:4 + v]:
        p, _, _ = C.at(rng.uniform(0.3, 0.55))
        drop = rng.uniform(0.3, 0.9)
        end = p - UP * drop
        t.tube([p, end], [0.013, 0.013], 3, thread, 0.3, point=False)
        s = rng.uniform(0.14, 0.22)
        t.blob(end - UP * s * 1.6, (s, s * 0.9, s * 1.9), pod, seg=6, rings=5, noise=0.08, seed=v)
    # A few sprays of needles left on the claws.
    for C in claws:
        if rng.random() < 0.75:
            p, tan, _ = C.at(rng.uniform(0.35, 0.6))
            d = (C.at(0.9)[0] - p).normalized()
            t.card(p, p + d * rng.uniform(0.9, 1.4), 0.6, UP, needles)


def lattice(t: Tree, v: int):
    rng = t.rng
    root = t.mat("pine_bark", (0.62, 0.52, 0.46), 0.1)
    skin = t.mat("membrane_web", (1.45, 0.78, 0.7), 0.4)
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
    # Skin in some of the gaps, between two neighbouring strands over a stretch.
    for k in range((4, 5, 3)[v]):
        a = strands[(2 * k) % n]
        b = strands[(2 * k + 1) % n]
        s0, s1 = rng.uniform(0.15, 0.45), rng.uniform(0.5, 0.75)
        pa, _, _ = a.at(s0)
        qa, _, _ = a.at(s1)
        pb, _, _ = b.at(s0)
        qb, _, _ = b.at(s1)
        t.sheet([pa, qa, qb, pb], skin, sag=0.05)
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
