"""The blighted trees' kit (ARCHITECTURE §8 note 696; tools/models/recipes/corrupted_trees.py): the pieces a corrupted
tree is made of, built straight into one bmesh and worn in the texture library's layers (cook.library_material's way:
no baking, metre UVs over each layer's tile, a tint of its own per part), so a tree is a prop the world's cells merge
like the kit's own trees, in the layers the world already has.

  * Tube: a limb along a polyline, its rings on parallel-transport frames (no twisting at a bend), tapering, ending in a
    point or cut square; the bark's u round it a whole number of tiles, so the seam doesn't show.
  * Blob, Crystal, Shelf, Cone: barnacles, mussels, pods, nodules; quartz; bracket fungus; spikes and drips.
  * Hang: a hanging card, crossed and two-sided, root at v 1 (where scene.vert bends a card from), so it swings.
  * Sheet: a membrane stretched between points, two-sided, sagging in the middle.
  * Hole: a dark oval with a lip, proud of the bark: a hole in the trunk.
  * The Corruption's (the director's second pass): Drip (tar hanging in a string to a drop, in a "_drip" layer it sags
    in the vertex shader), Creep (a vein crawling over the bark), Wound (the bark split on the meat), Growth (a tumour),
    Pustules, Wire (barbed, wound round and bitten in).

Blender's frame: Z up, metres, the tree's foot at the origin. Deterministic (random.Random(seed), no hash order).
"""
from __future__ import annotations

import math
import os
import random

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

import cook

UP = Vector((0, 0, 1))


def lerp(a, b, t):
    return a + (b - a) * t


class Tree:
    """One tree's mesh being built: its bmesh, its materials (one per layer and tint), and what it's made of so far."""

    def __init__(self, name: str, seed: int):
        self.name = name
        self.rng = random.Random(seed)
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mats: list = []
        self._slots: dict = {}

    # -- materials ------------------------------------------------------------------------------------------------
    def mat(self, layer: str, tint=(1, 1, 1), shine=0.1, emissive=0.0):
        """The slot of a library material wearing `layer`, tinted: named <layer>.<n>, so the engine finds the layer by the
        name before its dot and each tint is a material of its own."""
        key = (layer, tuple(round(float(c), 3) for c in tint), round(shine, 3), round(emissive, 3))
        if key not in self._slots:
            m = bpy.data.materials.new(f"{layer}.{len(self.mats)}")
            m["dt_library"] = True
            m["dt_shine"] = float(shine)
            m["dt_emissive"] = float(emissive)
            m["dt_glow"] = 0.0
            m["dt_tint"] = [float(c) for c in tint]
            self.mats.append(m)
            self._slots[key] = len(self.mats) - 1
        return self._slots[key]

    # -- raw faces ------------------------------------------------------------------------------------------------
    def face(self, verts, uvs, mat, smooth=True):
        try:
            f = self.bm.faces.new(verts)
        except ValueError:
            return None
        f.material_index = mat
        f.smooth = smooth
        for loop, uv in zip(f.loops, uvs):
            loop[self.uv].uv = uv
        return f

    def verts(self, points):
        return [self.bm.verts.new(p) for p in points]

    # -- tubes ----------------------------------------------------------------------------------------------------
    @staticmethod
    def frames(pts):
        """Tangent, normal and binormal at each point, the normal carried along without twisting."""
        n = len(pts)
        T = []
        for i in range(n):
            d = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
            T.append(d.normalized() if d.length > 1e-9 else UP.copy())
        a = Vector((1, 0, 0)) if abs(T[0].x) < 0.9 else Vector((0, 1, 0))
        N = T[0].cross(a).normalized()
        out = []
        for i in range(n):
            if i > 0:
                N = T[i - 1].rotation_difference(T[i]) @ N
                N = (N - T[i] * N.dot(T[i])).normalized()
            out.append((T[i], N, T[i].cross(N)))
        return out

    def tube(self, pts, radii, sides, mat, tile=1.0, point=None, bulge=None, smooth=True, twist=0.0, grain=False):
        """A limb along `pts` (Vectors) at `radii`, `sides` round. Ends in a point when its last radius is ~0 (or `point`
        is True); `bulge(i, j) -> factor` roughens a ring (bark, knots). UVs in metres over `tile`: v along it, u round it
        a whole number of tiles at its root's girth (`grain`: the other way about, for a layer whose lines should run up
        the limb: wood_grey's boards as bleached grain)."""
        n = len(pts)
        if n < 2:
            return []
        fr = self.frames(pts)
        point = radii[-1] <= 1e-4 if point is None else point
        length = [0.0]
        for i in range(1, n):
            length.append(length[-1] + (pts[i] - pts[i - 1]).length)
        U = max(1, round(2 * math.pi * max(radii[0], 1e-3) / tile))
        rings = []
        last = n - 1 if point else n
        for i in range(last):
            T, N, B = fr[i]
            ring = []
            for j in range(sides):
                a = 2 * math.pi * j / sides + twist * i
                r = radii[i] * (bulge(i, j) if bulge else 1.0)
                ring.append(self.bm.verts.new(pts[i] + (N * math.cos(a) + B * math.sin(a)) * r))
            rings.append(ring)
        uv = (lambda u, v: (v, u)) if grain else (lambda u, v: (u, v))
        for i in range(len(rings) - 1):
            v0, v1 = length[i] / tile, length[i + 1] / tile
            for j in range(sides):
                k = j + 1
                self.face([rings[i][j], rings[i][k % sides], rings[i + 1][k % sides], rings[i + 1][j]],
                          [uv(U * j / sides, v0), uv(U * k / sides, v0), uv(U * k / sides, v1), uv(U * j / sides, v1)], mat, smooth)
        if point:
            tip = self.bm.verts.new(pts[-1])
            i = len(rings) - 1
            v0, v1 = length[i] / tile, length[-1] / tile
            for j in range(sides):
                k = j + 1
                self.face([rings[i][j], rings[i][k % sides], tip], [uv(U * j / sides, v0), uv(U * k / sides, v0), uv(U * (j + 0.5) / sides, v1)], mat, smooth)
        return rings

    def cone(self, base, tip, r, sides, mat, tile=0.5, smooth=True):
        """A spike, a drip, a thorn: base radius `r` to a point."""
        return self.tube([Vector(base), Vector(tip)], [r, 0.0], sides, mat, tile, point=True, smooth=smooth)

    # -- lumps ----------------------------------------------------------------------------------------------------
    def blob(self, centre, radii, mat, rot=None, seg=6, rings=4, tile=0.5, noise=0.0, seed=0, half=False, band=None):
        """An ellipsoid (radii along its own x y z, turned by `rot`), `seg` round and `rings` from pole to pole (`half`:
        the top half only, a dome on what it grows from). Metre UVs over `tile`; `band` (an int) puts its u round it all
        within [band, band + 1), one tile round, so a layer that reads the whole part of u (a "_pod"'s breathing:
        scene.frag) sees one number on the whole of it."""
        rot = rot or Quaternion()
        c = Vector(centre)
        rx, ry, rz = radii
        top = self.bm.verts.new(c + rot @ Vector((0, 0, rz)))
        layers = []
        stop = rings // 2 if half else rings - 1
        for i in range(1, stop + 1):
            phi = math.pi * i / rings
            ring = []
            for j in range(seg):
                th = 2 * math.pi * j / seg
                p = Vector((math.sin(phi) * math.cos(th) * rx, math.sin(phi) * math.sin(th) * ry, math.cos(phi) * rz))
                if noise:
                    p *= 1 + noise * cook.noise3(p * 4, seed)
                ring.append(self.bm.verts.new(c + rot @ p))
            layers.append(ring)
        U = max(1, round(2 * math.pi * max(rx, ry) / tile))
        V = math.pi * rz / tile
        if band is not None:
            U = 0.96
        u0 = 0.0 if band is None else band + 0.02
        for j in range(seg):
            k = j + 1
            self.face([top, layers[0][j], layers[0][k % seg]], [(u0 + U * (j + 0.5) / seg, 0), (u0 + U * j / seg, V / rings), (u0 + U * k / seg, V / rings)], mat)
        for i in range(len(layers) - 1):
            a, b = V * (i + 1) / rings, V * (i + 2) / rings
            for j in range(seg):
                k = j + 1
                self.face([layers[i][j], layers[i + 1][j], layers[i + 1][k % seg], layers[i][k % seg]],
                          [(u0 + U * j / seg, a), (u0 + U * j / seg, b), (u0 + U * k / seg, b), (u0 + U * k / seg, a)], mat)
        if not half:
            bottom = self.bm.verts.new(c + rot @ Vector((0, 0, -rz)))
            i = len(layers) - 1
            for j in range(seg):
                k = j + 1
                self.face([layers[i][j], bottom, layers[i][k % seg]], [(u0 + U * j / seg, V * (i + 1) / rings), (u0 + U * (j + 0.5) / seg, V), (u0 + U * k / seg, V * (i + 1) / rings)], mat)

    def crystal(self, base, direction, length, r, mat, sides=6, tile=0.4, tip=0.3):
        """A quartz point: a prism `length` long, its last `tip` of it a pyramid; flat-shaded so each facet takes its own
        light."""
        d = Vector(direction).normalized()
        b = Vector(base)
        body = b + d * length * (1 - tip)
        self.tube([b - d * r * 0.5, body, b + d * length], [r, r * 0.95, 0.0], sides, mat, tile, point=True, smooth=False)

    def shelf(self, at, out, radius, thick, mat, seg=7, tile=0.4, droop=0.25, under=None):
        """A bracket fungus on a trunk: a half disc `radius` out from `at` toward `out` (horizontal), domed on top, a lip
        round its edge and flat beneath, its rim drooping a little; `under` its underside's material (the lantern tree's
        gills, glowing), else the cap's."""
        under = mat if under is None else under
        o = Vector((out.x, out.y, 0)).normalized()
        side = UP.cross(o).normalized()
        at = Vector(at)
        rim, below = [], []
        for j in range(seg + 1):
            a = math.pi * j / seg
            r = radius * (0.85 + 0.15 * math.sin(a * 3 + radius * 7))
            p = at + (side * math.cos(a) + o * math.sin(a)) * r
            fall = droop * thick * math.sin(a) ** 2
            rim.append(self.bm.verts.new(p + UP * (thick * 0.25 - fall)))
            below.append(self.bm.verts.new(p - UP * (thick * 0.25 + fall)))
        crown = self.bm.verts.new(at + o * radius * 0.25 + UP * thick)
        foot = self.bm.verts.new(at - UP * thick * 0.3)
        for j in range(seg):
            k = j + 1
            uv = lambda p: ((p - at).dot(side) / tile, (p - at).dot(o) / tile)
            self.face([crown, rim[j], rim[k]], [uv(crown.co), uv(rim[j].co), uv(rim[k].co)], mat)
            self.face([rim[j], below[j], below[k], rim[k]], [(j * 0.1, 0), (j * 0.1, 0.15), (k * 0.1, 0.15), (k * 0.1, 0)], mat)
            self.face([foot, below[k], below[j]], [uv(foot.co), uv(below[k].co), uv(below[j].co)], under)

    def barnacle(self, at, normal, r, h, mat, sides=5):
        """An acorn barnacle on what it's stuck to: a squat cone `r` round at its foot and `h` high, open at the top in a
        crater (its plates parted), flat-shaded so its plates catch the light."""
        n = Vector(normal).normalized()
        a = Vector((1, 0, 0)) if abs(n.x) < 0.9 else Vector((0, 1, 0))
        e1 = n.cross(a).normalized()
        e2 = n.cross(e1)
        at = Vector(at)
        foot, lip = [], []
        for j in range(sides):
            ang = 2 * math.pi * j / sides
            q = e1 * math.cos(ang) + e2 * math.sin(ang)
            foot.append(self.bm.verts.new(at + q * r - n * h * 0.2))
            lip.append(self.bm.verts.new(at + q * r * 0.5 + n * h))
        pit = self.bm.verts.new(at + n * h * 0.45)
        for j in range(sides):
            k = (j + 1) % sides
            self.face([foot[j], foot[k], lip[k], lip[j]], [(j * 0.05, 0), ((j + 1) * 0.05, 0), ((j + 1) * 0.05, 0.05), (j * 0.05, 0.05)], mat, smooth=False)
            self.face([lip[j], lip[k], pit], [(j * 0.03, 0.1), ((j + 1) * 0.03, 0.1), (j * 0.03, 0.12)], mat, smooth=False)

    def streak(self, path, normals, widths, mat, proud=0.006, tile=0.5):
        """A run of something down a surface (rust from a spike, tar from a fork): a strip along `path`, `widths` across,
        lying on the surface (whose `normals` are given), proud of it by `proud`. One-sided, facing out."""
        lefts, rights = [], []
        for i, p in enumerate(path):
            n = Vector(normals[i]).normalized()
            d = (path[min(i + 1, len(path) - 1)] - path[max(i - 1, 0)])
            side = n.cross(d).normalized() if d.length > 1e-9 else Vector((1, 0, 0))
            q = Vector(p) + n * proud
            lefts.append(self.bm.verts.new(q - side * widths[i] / 2))
            rights.append(self.bm.verts.new(q + side * widths[i] / 2))
        run = 0.0
        for i in range(len(path) - 1):
            step = (Vector(path[i + 1]) - Vector(path[i])).length / tile
            self.face([lefts[i], lefts[i + 1], rights[i + 1], rights[i]], [(0, run), (0, run + step), (0.3, run + step), (0.3, run)], mat, smooth=True)
            run += step

    def drip(self, top, length, r, mat, k, sides=4, bulb=0.6, lean=None):
        """Something thick hanging off a limb in a string (tar): `length` down from `top`, `r` round at its root, thinning
        to a thread and swelling to a drop at its tip. In a "_drip" layer it sags from its root and draws back up
        (scene.vert): v runs from 1 at its root (Blender's 0.001) down a quarter of the metres it hangs, and the whole part
        of u is `k`, which drip it is, so each moves on its own phase and moves as one."""
        top = Vector(top)
        lean = Vector(lean) if lean is not None else Vector()
        tip = top - UP * length + lean
        a = Vector((1, 0, 0))
        e1 = UP.cross(a).normalized()
        e2 = UP.cross(e1)
        uvz = lambda p: 0.001 + max(0.0, top.z - p.z) / 4.0
        ring_at = [(0.0, r), (0.3, r * 0.5), (0.7, r * 0.26), (0.86, r * 0.3)]
        rings = []
        for f, rr in ring_at:
            c = top.lerp(tip, f)
            rings.append([self.bm.verts.new(c + (e1 * math.cos(2 * math.pi * j / sides) + e2 * math.sin(2 * math.pi * j / sides)) * rr)
                          for j in range(sides)])
        uu = lambda j: k + 0.02 + 0.96 * j / sides
        for i in range(len(rings) - 1):
            for j in range(sides):
                q = (j + 1) % sides
                vs = [rings[i][j], rings[i][q], rings[i + 1][q], rings[i + 1][j]]
                self.face(vs, [(uu(j), uvz(rings[i][j].co)), (uu(j + 1), uvz(rings[i][q].co)), (uu(j + 1), uvz(rings[i + 1][q].co)),
                               (uu(j), uvz(rings[i + 1][j].co))], mat)
        # The drop: wider than the thread, hanging under it.
        rb = r * bulb
        c = tip + UP * rb * 0.6
        mid = [self.bm.verts.new(c + (e1 * math.cos(2 * math.pi * (j + 0.5) / sides) + e2 * math.sin(2 * math.pi * (j + 0.5) / sides)) * rb)
               for j in range(sides)]
        low = self.bm.verts.new(tip - UP * rb * 0.9)
        last = rings[-1]
        for j in range(sides):
            q = (j + 1) % sides
            self.face([last[j], last[q], mid[q], mid[j]], [(uu(j), uvz(last[j].co)), (uu(j + 1), uvz(last[q].co)), (uu(j + 1), uvz(mid[q].co)),
                                                          (uu(j), uvz(mid[j].co))], mat)
            self.face([mid[j], mid[q], low], [(uu(j), uvz(mid[j].co)), (uu(j + 1), uvz(mid[q].co)), (uu(j + 0.5), uvz(low.co))], mat)
        return tip

    def creep(self, limb, t0, t1, mat, r=0.03, turns=0.6, angle=0.0, steps=10, sides=3, wobble=0.35, out=0.0, seed=0):
        """A vein or a tendril of the Corruption crawling over a limb's bark from `t0` to `t1` of it, winding `turns` round
        it as it goes, swelling and thinning, lying on the bark (`out` proud of it): what makes a tree read as taken from
        the line, the bark netted over with it."""
        rng = random.Random(seed)
        pts, radii = [], []
        ph = rng.uniform(0, 6.28)
        for i in range(steps + 1):
            f = i / steps
            tt = lerp(t0, t1, f)
            a = angle + turns * 2 * math.pi * f + wobble * math.sin(f * 7 + ph)
            p, n = limb.surface(tt, a, out + r * 0.4)
            pts.append(p)
            radii.append(r * (0.55 + 0.45 * math.sin(math.pi * min(1.0, f * 1.4))) * (1 + 0.35 * math.sin(f * 11 + ph)))
        radii[-1] = 0.0
        return self.tube(pts, radii, sides, mat, 0.4, point=True)

    def wound(self, at, normal, rx, ry, flesh, lip, seg=8, swell=0.05, proud=0.02):
        """Where the bark's split open on the meat under it: an oval of `flesh` bulging out of the split by `swell`, its
        lips of bark (`lip`) rolled back and swollen round it."""
        n = Vector(normal).normalized()
        side = UP.cross(n)
        side = side.normalized() if side.length > 1e-6 else Vector((1, 0, 0))
        up = n.cross(side).normalized()
        at = Vector(at)
        inner, roll, outer = [], [], []
        for j in range(seg):
            a = 2 * math.pi * j / seg
            jag = 1 + 0.18 * math.sin(a * 3 + rx * 31) + 0.1 * math.sin(a * 5 + ry * 17)
            q = (side * math.cos(a) * rx + up * math.sin(a) * ry) * jag
            inner.append(self.bm.verts.new(at + n * proud + q * 0.92))
            roll.append(self.bm.verts.new(at + n * (proud + swell * 0.9) + q * 1.12))
            outer.append(self.bm.verts.new(at - n * proud * 0.4 + q * 1.5))
        mid = self.bm.verts.new(at + n * (proud + swell))
        mids = [self.bm.verts.new(at + n * (proud + swell * 0.75) + (side * math.cos(2 * math.pi * j / seg) * rx + up * math.sin(2 * math.pi * j / seg) * ry) * 0.5)
                for j in range(seg)]
        uv = lambda p: ((p - at).dot(side) / 0.4 + 0.5, (p - at).dot(up) / 0.4 + 0.5)
        for j in range(seg):
            k = (j + 1) % seg
            self.face([mid, mids[j], mids[k]], [uv(mid.co), uv(mids[j].co), uv(mids[k].co)], flesh)
            self.face([mids[j], inner[j], inner[k], mids[k]], [uv(mids[j].co), uv(inner[j].co), uv(inner[k].co), uv(mids[k].co)], flesh)
            self.face([inner[j], roll[j], roll[k], inner[k]], [(j * 0.15, 0), (j * 0.15, 0.08), (j * 0.15 + 0.15, 0.08), (j * 0.15 + 0.15, 0)], lip)
            self.face([roll[j], outer[j], outer[k], roll[k]], [(j * 0.15, 0.08), (j * 0.15, 0.2), (j * 0.15 + 0.15, 0.2), (j * 0.15 + 0.15, 0.08)], lip)

    def growth(self, at, normal, size, mat, n=4, seed=0, seg=6, rings=4):
        """A tumour of the Corruption's on a limb: `n` swollen lumps fused into each other and half sunk into what they grow
        out of, the biggest in the middle."""
        rng = random.Random(seed)
        nrm = Vector(normal).normalized()
        a = Vector((1, 0, 0)) if abs(nrm.x) < 0.9 else Vector((0, 1, 0))
        e1 = nrm.cross(a).normalized()
        e2 = nrm.cross(e1)
        rot = Vector((0, 0, 1)).rotation_difference(nrm)
        at = Vector(at)
        for i in range(n):
            s = size * (1.0 if i == 0 else rng.uniform(0.45, 0.75))
            off = Vector() if i == 0 else (e1 * rng.gauss(0, 1) + e2 * rng.gauss(0, 1)).normalized() * size * rng.uniform(0.6, 1.0)
            c = at + off + nrm * s * 0.15
            self.blob(c, (s, s * rng.uniform(0.8, 1.0), s * rng.uniform(0.7, 0.95)), mat, rot=rot, seg=seg, rings=rings, noise=0.18, seed=seed + i)

    def pustules(self, at, normal, spread, n, mat, size=0.03, seed=0):
        """A crop of blisters on the bark: domes of a sac, crowded in a patch `spread` across."""
        rng = random.Random(seed)
        nrm = Vector(normal).normalized()
        a = Vector((1, 0, 0)) if abs(nrm.x) < 0.9 else Vector((0, 1, 0))
        e1 = nrm.cross(a).normalized()
        e2 = nrm.cross(e1)
        rot = Vector((0, 0, 1)).rotation_difference(nrm)
        for _ in range(n):
            s = size * rng.uniform(0.6, 1.4)
            c = Vector(at) + (e1 * rng.gauss(0, 0.5) + e2 * rng.gauss(0, 0.5)) * spread - nrm * s * 0.25
            self.blob(c, (s, s, s * 0.8), mat, rot=rot, seg=5, rings=4, half=True)

    def wire(self, limb, t0, t1, turns, mat, r=0.008, out=0.01, barbs=0, barb_mat=None, steps_per_turn=9, seed=0):
        """Barbed wire wound round a limb from `t0` to `t1` of it, `turns` times, biting into it, with `barbs` along it."""
        rng = random.Random(seed)
        steps = max(4, int(turns * steps_per_turn))
        pts = []
        for i in range(steps + 1):
            f = i / steps
            p, _ = limb.surface(lerp(t0, t1, f), turns * 2 * math.pi * f + rng.uniform(-0.05, 0.05), out)
            pts.append(p)
        self.tube(pts, [r] * len(pts), 3, mat, 0.2, point=False, smooth=True)
        for i in range(barbs):
            p, n = limb.surface(lerp(t0, t1, (i + 0.5) / barbs), turns * 2 * math.pi * (i + 0.5) / barbs, out)
            d = (n + Vector((rng.gauss(0, 0.6), rng.gauss(0, 0.6), rng.gauss(0, 0.6)))).normalized()
            self.cone(p - d * 0.01, p + d * 0.045, 0.006, 3, barb_mat if barb_mat is not None else mat)

    # -- cards and sheets -----------------------------------------------------------------------------------------
    def hang(self, top, length, width, mat, u0=0.0, u1=1.0, turn=0.0, sway=None, crossed=True, taper=0.8):
        """A hanging card from `top`, `length` down, `width` across, crossed (two at right angles) and two-sided. Its root
        at v 1 (Blender's 0.001, the exporter flipping v), its tip at 0, so the wind bends it from where it hangs.
        `sway` leans its foot off plumb (a vector, metres)."""
        top = Vector(top)
        low = top + Vector((0, 0, -length)) + (Vector(sway) if sway is not None else Vector())
        mid = (top + low) / 2 + Vector((0, 0, length * 0.04))
        planes = (turn, turn + math.pi / 2) if crossed else (turn,)
        for a in planes:
            across = Vector((math.cos(a), math.sin(a), 0)) * width / 2
            for flip in (False, True):
                row = [(top, 0.001), (mid, 0.5), (low, 0.999)]
                vs = [(self.bm.verts.new(p - across * (taper if t < 0.1 else 1)), self.bm.verts.new(p + across * (taper if t < 0.1 else 1)), t) for p, t in row]
                for i in range(2):
                    (a0, b0, t0), (a1, b1, t1) = vs[i], vs[i + 1]
                    quad = [a0, b0, b1, a1]
                    uvs = [(u0, t0), (u1, t0), (u1, t1), (u0, t1)]
                    if flip:
                        quad, uvs = quad[::-1], uvs[::-1]
                    self.face(quad, uvs, mat, smooth=False)

    def card(self, root, tip, width, normal, mat, u=(0.0, 1.0), v=(0.0, 1.0)):
        """A flat two-sided card from `root` to `tip` (a bough's spray: pine_bough's u runs root to tip, v across)."""
        root, tip = Vector(root), Vector(tip)
        along = tip - root
        across = Vector(normal).cross(along).normalized() * width / 2
        mid = root + along * 0.55 - Vector((0, 0, along.length * 0.08))
        pts = [(root, u[0]), (mid, lerp(u[0], u[1], 0.55)), (tip, u[1])]
        for flip in (False, True):
            vs = [(self.bm.verts.new(p - across * (0.6 if i == 0 else 1)), self.bm.verts.new(p + across * (0.6 if i == 0 else 1)), uu) for i, (p, uu) in enumerate(pts)]
            for i in range(2):
                (a0, b0, s0), (a1, b1, s1) = vs[i], vs[i + 1]
                quad = [a0, a1, b1, b0]
                uvs = [(s0, v[0]), (s1, v[0]), (s1, v[1]), (s0, v[1])]
                if flip:
                    quad, uvs = quad[::-1], uvs[::-1]
                self.face(quad, uvs, mat, smooth=False)

    def sheet(self, ring, mat, sag=0.15, tile=1.0):
        """A membrane stretched over a closed loop of points, two-sided, its middle sagging `sag` of its span; UVs in
        metres on its own plane."""
        ring = [Vector(p) for p in ring]
        c = sum(ring, Vector()) / len(ring)
        span = max((p - c).length for p in ring)
        n = Vector()
        for i in range(len(ring)):
            n += (ring[i] - c).cross(ring[(i + 1) % len(ring)] - c)
        n = n.normalized() if n.length > 1e-9 else UP.copy()
        e1 = (ring[0] - c)
        e1 = (e1 - n * e1.dot(n)).normalized()
        e2 = n.cross(e1)
        mid = c - UP * sag * span
        # A ring halfway out, sagging half as much, so the membrane bellies rather than tents.
        halfway = [lerp(c, p, 0.55) - UP * sag * span * 0.55 for p in ring]
        uv = lambda p: ((p - c).dot(e1) / tile, (p - c).dot(e2) / tile)
        for flip in (False, True):
            m = self.bm.verts.new(mid)
            hv = [self.bm.verts.new(p) for p in halfway]
            rv = [self.bm.verts.new(p) for p in ring]
            k = len(ring)
            for i in range(k):
                j = (i + 1) % k
                tris = [([m, hv[i], hv[j]], [uv(mid), uv(halfway[i]), uv(halfway[j])]),
                        ([hv[i], rv[i], rv[j], hv[j]], [uv(halfway[i]), uv(ring[i]), uv(ring[j]), uv(halfway[j])])]
                for vs, uvs in tris:
                    if flip:
                        vs, uvs = vs[::-1], uvs[::-1]
                    self.face(vs, uvs, mat, smooth=True)

    def hole(self, at, normal, rx, ry, dark, lip, seg=8, proud=0.012):
        """A hole in a trunk: a dark oval `rx` across and `ry` up, proud of the bark by `proud`, with a raised lip of bark
        round it (`lip`: the bark's material)."""
        n = Vector(normal).normalized()
        side = UP.cross(n)
        side = side.normalized() if side.length > 1e-6 else Vector((1, 0, 0))
        up = n.cross(side).normalized()
        at = Vector(at)
        inner, outer = [], []
        for j in range(seg):
            a = 2 * math.pi * j / seg
            q = side * math.cos(a) * rx + up * math.sin(a) * ry
            inner.append(self.bm.verts.new(at + n * proud + q))
            outer.append(self.bm.verts.new(at - n * proud * 0.5 + q * 1.35))
        mid = self.bm.verts.new(at + n * proud * 0.5)
        for j in range(seg):
            k = (j + 1) % seg
            self.face([mid, inner[j], inner[k]], [(0.5, 0.5), (0.5 + 0.5 * math.cos(2 * math.pi * j / seg), 0.5), (0.5, 0.6)], dark, smooth=False)
            self.face([inner[j], outer[j], outer[k], inner[k]], [(j * 0.2, 0), (j * 0.2, 0.1), (j * 0.2 + 0.2, 0.1), (j * 0.2 + 0.2, 0)], lip)

    def box(self, centre, x, y, z, mat, tile=0.5):
        """A box with half-extent vectors `x`, `y`, `z` (any orientation): beams, plates, spike heads. Flat-shaded."""
        c = Vector(centre)
        corners = [c + x * sx + y * sy + z * sz for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        vs = self.verts(corners)
        quads = [(0, 1, 3, 2, x), (4, 6, 7, 5, x), (0, 4, 5, 1, y), (2, 3, 7, 6, y), (0, 2, 6, 4, z), (1, 5, 7, 3, z)]
        for a, b, cc, d, _ in quads:
            pa = corners[a]
            e1 = (corners[b] - pa)
            e2 = (corners[d] - pa)
            uvs = [(0, 0), (e1.length / tile, 0), (e1.length / tile, e2.length / tile), (0, e2.length / tile)]
            f = self.face([vs[a], vs[b], vs[cc], vs[d]], uvs, mat, smooth=False)
            if f is not None:
                f.normal_update()
                if f.normal.dot(f.calc_center_median() - c) < 0:
                    f.normal_flip()

    # -- finishing ------------------------------------------------------------------------------------------------
    def triangles(self):
        return sum(len(f.verts) - 2 for f in self.bm.faces)

    def export(self):
        """The tree as content/art/models/props/<name>.glb (cook's one-bone prop). Returns it (the scene's object)."""
        mesh = bpy.data.meshes.new(self.name)
        self.bm.normal_update()
        self.bm.to_mesh(mesh)
        tris = self.triangles()
        self.bm.free()
        for m in self.mats:
            mesh.materials.append(m)
        obj = bpy.data.objects.new(self.name, mesh)
        bpy.context.scene.collection.objects.link(obj)
        path = cook.rig_and_export(self.name, [obj])
        lo, hi = cook.bounds([obj])
        size = hi - lo
        print(f"[dt] {self.name}: {tris} tris, {len(self.mats)} library materials, {size.x:.1f} x {size.y:.1f} x {size.z:.1f} m "
              f"-> {os.path.relpath(path, cook.ROOT)}")
        return obj


# ---------------------------------------------------------------------------------------------------------------------
# Growing: a trunk and limbs off it

class Limb:
    """A limb's centre line and radii, and where along it things grow: `at(t)` -> (point, tangent, radius), t 0..1."""

    def __init__(self, pts, radii):
        self.pts, self.radii = pts, radii
        self.lengths = [0.0]
        for i in range(1, len(pts)):
            self.lengths.append(self.lengths[-1] + (pts[i] - pts[i - 1]).length)

    @property
    def length(self):
        return self.lengths[-1]

    def at(self, t):
        d = max(0.0, min(1.0, t)) * self.length
        for i in range(1, len(self.pts)):
            if self.lengths[i] >= d or i == len(self.pts) - 1:
                seg = max(self.lengths[i] - self.lengths[i - 1], 1e-9)
                f = max(0.0, min(1.0, (d - self.lengths[i - 1]) / seg))
                p = self.pts[i - 1].lerp(self.pts[i], f)
                tan = (self.pts[i] - self.pts[i - 1]).normalized()
                return p, tan, lerp(self.radii[i - 1], self.radii[i], f)
        return self.pts[-1], UP.copy(), self.radii[-1]

    def surface(self, t, angle, out=0.0):
        """A point on its bark `angle` round (from its own frame's normal), `out` beyond the bark, and the bark's normal there."""
        p, tan, r = self.at(t)
        a = Vector((1, 0, 0)) if abs(tan.x) < 0.9 else Vector((0, 1, 0))
        n0 = tan.cross(a).normalized()
        b0 = tan.cross(n0)
        n = n0 * math.cos(angle) + b0 * math.sin(angle)
        return p + n * (r + out), n


def grow(rng, start, direction, length, r0, r1, steps, wander=0.12, pull=None, pull_amount=0.0):
    """A limb from `start` along `direction`, wandering, pulled toward `pull` (a direction: up for a limb that reached for
    the light, down for one weighed down) by `pull_amount` a step."""
    d = Vector(direction).normalized()
    pts = [Vector(start)]
    for i in range(steps):
        d = (d + Vector((rng.gauss(0, wander), rng.gauss(0, wander), rng.gauss(0, wander * 0.6)))).normalized()
        if pull is not None:
            d = (d + Vector(pull) * pull_amount).normalized()
        pts.append(pts[-1] + d * (length / steps))
    radii = [lerp(r0, r1, (i / steps) ** 0.9) for i in range(steps + 1)]
    return Limb(pts, radii)


def trunk(rng, height, r_base, r_top, steps, lean=0.0, lean_to=0.0, wobble=0.05, flare=0.6, twist=0.0):
    """A trunk from the ground up: leaning `lean` (metres over its height) toward `lean_to` (radians), wandering by
    `wobble`, its foot flared to `flare` more than its base radius below a metre, tapering to `r_top`."""
    lean_dir = Vector((math.cos(lean_to), math.sin(lean_to), 0))
    pts, radii = [], []
    phase = rng.uniform(0, 6.28)
    for i in range(steps + 1):
        t = i / steps
        z = height * t
        # The wander grows with height; a twisted trunk winds as it goes.
        w = wobble * height * t
        off = lean_dir * lean * t * t + Vector((math.sin(phase + t * 5.1 + twist * t * 6) * w, math.cos(phase * 1.3 + t * 4.3 + twist * t * 6) * w, 0))
        pts.append(Vector((off.x, off.y, z if i else -0.3)))
        r = lerp(r_base, r_top, t ** 0.85) * (1 + flare * math.exp(-z / 0.7))
        radii.append(r)
    return Limb(pts, radii)
