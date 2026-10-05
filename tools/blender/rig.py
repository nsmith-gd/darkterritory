"""Shared kit for the character and creature scripts (tools/blender/*.py): skeleton templates, a small
low-poly modelling kit, hand-keyed clip authoring and the glTF export every model goes through.

Why a kit rather than sculpting: every model in content/art/models is rebuilt from text by
`tools/blender/build.sh` (CLAUDE.md: "everything authored is text", deterministic). The look is GDD §27's:
chunky faceted forms, big planes, visible simplification, 4-10k triangles for a character, and §31's motion:
stiff 30 fps keys with holds, snapping and lurches for the monsters, heavy and grounded for the crew.

Conventions (shared with the engine, see src/Ballast.Assets):
  * Blender space: +X right, +Y forward (the way the model faces), +Z up, metres. The glTF exporter's +Y-up
    conversion turns that into the engine's +X right, +Y up, -Z forward. Pivot on the floor between the feet.
  * Materials are named <texture>[.<part>]: the engine takes the texture layer from the name up to the first
    dot (content/art/textures/index.json names) and keeps the rest only to tell parts apart. Each material also
    carries a flat fallback colour (base colour) and extras the engine reads: dt_shine, dt_emissive (a pure light,
    drawn at full colour), dt_glow (emissive only when the texture is missing: ember cracks carry their own mask).
  * UVs are authored in image space (u right, v DOWN from the top of the image, the glTF convention) and flipped
    into Blender's bottom-up space on the way in, so the exporter's flip gives the image space back.
  * Mesh objects may carry extras: `variants` ("0,2": drawn only for those variant indices) and `clip` (drawn only
    while that clip plays: a prop such as the fireman's shovel).
  * Poses are authored as rotations in the armature's own axes at each joint (see `Clip`), converted to Blender's
    bone-local quaternions here, so the scripts read as "thigh forward 25 degrees" whatever the bone rolls are.
"""
from __future__ import annotations

import math
import os
import sys

import bpy
import bmesh
from mathutils import Euler, Matrix, Quaternion, Vector

FPS = 30

# Metres per repeat of each tiling texture (content/art/textures/index.json "tileMetres"). Atlases (crew_atlas,
# lamp_lens) are mapped whole and have no entry.
TILE = {
    "coat_oilskin": 0.5, "leather": 0.5, "wool": 0.5, "fleece": 0.5, "skin": 0.25, "flesh": 0.5, "fungal_crust": 0.5,
    "mineral_growth": 0.5, "tar": 0.5, "sac": 0.5, "ember_crack": 0.5, "glass_dirty": 0.5, "iron_plate": 1.0,
    "rust_heavy": 1.0, "wood_grey": 1.0, "wood_sleeper": 1.0, "brass": 0.5,
}

# crew_atlas: a 4x4 grid of 64 px cells (tools/art/texgen/mat_cloth.py CELLS, index.json "cells").
ATLAS_CELLS = ["face_front", "face_side", "cap", "helmet",
               "gloves", "belt_buckle", "harness", "boot",
               "lantern_glass", "coat_front", "coat_back", "trouser",
               "satchel", "badge", "hair", "scarf"]


def cell(name, inset=0.004):
    """(u0, v0, du, dv) of an atlas cell in image space, pulled in a hair so filtering doesn't bleed neighbours."""
    i = ATLAS_CELLS.index(name)
    cx, cy = i % 4, i // 4
    return (cx * 0.25 + inset, cy * 0.25 + inset, 0.25 - 2 * inset, 0.25 - 2 * inset)


def in_cell(name, u, v):
    """A point 0..1 across and 0..1 down a cell, in atlas image space (clamped: no wrapping into a neighbour)."""
    u0, v0, du, dv = cell(name)
    u = min(max(u, 0.0), 1.0)
    v = min(max(v, 0.0), 1.0)
    return (u0 + u * du, v0 + v * dv)


# --------------------------------------------------------------------------------------------------------------
# Scene

def args():
    """Arguments after `--` on the blender command line."""
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.fps = FPS
    sc.render.fps_base = 1
    sc.frame_start = 0
    sc.frame_end = 1
    return sc


# --------------------------------------------------------------------------------------------------------------
# Skeletons

class Bone:
    def __init__(self, name, parent, head, tail, deform=True):
        self.name, self.parent = name, parent
        self.head, self.tail = Vector(head), Vector(tail)
        self.deform = deform


class Skeleton:
    """A named bone list (parents before children) plus the armature once built."""

    def __init__(self, name, bones):
        self.name = name
        self.bones = bones
        self.by = {b.name: b for b in bones}
        self.rig = None

    def __getitem__(self, name):
        return self.by[name]

    def build(self):
        arm = bpy.data.armatures.new(self.name)
        rig = bpy.data.objects.new(self.name, arm)
        bpy.context.scene.collection.objects.link(rig)
        bpy.context.view_layer.objects.active = rig
        bpy.ops.object.mode_set(mode="EDIT")
        for b in self.bones:
            eb = arm.edit_bones.new(b.name)
            eb.head, eb.tail = b.head, b.tail
            eb.roll = 0
            eb.use_deform = b.deform
            if b.parent:
                eb.parent = arm.edit_bones[b.parent]
                eb.use_connect = False
        bpy.ops.object.mode_set(mode="OBJECT")
        for pb in rig.pose.bones:
            pb.rotation_mode = "QUATERNION"
        self.rig = rig
        return rig

    def segment_distance(self, name, p):
        b = self.by[name]
        a, c = b.head, b.tail
        ab = c - a
        t = 0.0 if ab.length_squared < 1e-12 else max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
        return (a + ab * t - p).length


def human(height=1.8, leg=1.0, arm=1.0, torso=1.0, neck=1.0, head=1.0, width=1.0, fingers=True, sockets=True,
          extra=()):
    """SK_Human: root, pelvis, spine_01..03, neck, head, clavicles, arms, hands (two-bone mitten fingers), legs,
    feet, balls, and the sockets hand_r_weapon, hand_l_prop, head_hat. T-pose facing +Y (the engine's -Z).
    Proportions for a 1.8 m adult, scaled by `height / 1.8` and bent by the per-part multipliers (a child's big
    head, the Hollow's long limbs)."""
    s = height / 1.8
    foot_z = 0.085 * s
    calf = 0.43 * s * leg
    thigh = 0.44 * s * leg
    knee_z = foot_z + calf
    hip_z = knee_z + thigh
    pel = hip_z + 0.02 * s
    seg = 0.13 * s * torso
    sp1, sp2, sp3, top = pel + 0.105 * s * torso, pel + 0.105 * s * torso + seg, pel + 0.105 * s * torso + 2 * seg, pel + 0.105 * s * torso + 3 * seg
    neck_top = top + 0.11 * s * neck
    head_top = neck_top + 0.2 * s * head
    sh = top - 0.04 * s
    hx = 0.10 * s * width
    bones = [
        Bone("root", None, (0, 0, 0), (0, 0.15 * s, 0)),
        Bone("pelvis", "root", (0, 0, pel), (0, 0, sp1)),
        Bone("spine_01", "pelvis", (0, 0, sp1), (0, 0, sp2)),
        Bone("spine_02", "spine_01", (0, 0, sp2), (0, 0, sp3)),
        Bone("spine_03", "spine_02", (0, 0, sp3), (0, 0, top)),
        Bone("neck", "spine_03", (0, 0, top), (0, 0.01 * s, neck_top)),
        Bone("head", "neck", (0, 0.01 * s, neck_top), (0, 0.01 * s, head_top)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        cl = 0.15 * s * width
        ua = 0.29 * s * arm
        la = 0.26 * s * arm
        hd = 0.10 * s * arm
        x0 = 0.02 * s
        bones += [
            Bone(f"clavicle_{side}", "spine_03", (sx * x0, 0, sh), (sx * (x0 + cl), 0, sh + 0.02 * s)),
            Bone(f"upperarm_{side}", f"clavicle_{side}", (sx * (x0 + cl), 0, sh + 0.02 * s), (sx * (x0 + cl + ua), 0, sh + 0.02 * s)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * (x0 + cl + ua), 0, sh + 0.02 * s), (sx * (x0 + cl + ua + la), 0, sh + 0.02 * s)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * (x0 + cl + ua + la), 0, sh + 0.02 * s), (sx * (x0 + cl + ua + la + hd), 0, sh + 0.02 * s)),
        ]
        wrist = x0 + cl + ua + la
        if fingers:
            bones += [
                Bone(f"fingers_{side}", f"hand_{side}", (sx * (wrist + hd), 0, sh + 0.02 * s), (sx * (wrist + hd + 0.09 * s * arm), 0, sh + 0.02 * s)),
                Bone(f"thumb_{side}", f"hand_{side}", (sx * (wrist + 0.03 * s), 0.03 * s, sh + 0.01 * s), (sx * (wrist + 0.08 * s), 0.08 * s, sh + 0.01 * s)),
            ]
        bones += [
            Bone(f"thigh_{side}", "pelvis", (sx * hx, 0, hip_z), (sx * (hx + 0.005 * s), 0.01 * s, knee_z)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * (hx + 0.005 * s), 0.01 * s, knee_z), (sx * (hx + 0.005 * s), -0.01 * s, foot_z)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * (hx + 0.005 * s), -0.01 * s, foot_z), (sx * (hx + 0.005 * s), 0.13 * s, 0.03 * s)),
            Bone(f"ball_{side}", f"foot_{side}", (sx * (hx + 0.005 * s), 0.13 * s, 0.03 * s), (sx * (hx + 0.005 * s), 0.21 * s, 0.03 * s)),
        ]
    if sockets:
        grip = 0.02 * s + 0.15 * s * width + (0.29 + 0.26) * s * arm + 0.07 * s * arm
        bones += [
            Bone("hand_r_weapon", "hand_r", (grip, 0, sh), (grip, 0.1 * s, sh), deform=False),
            Bone("hand_l_prop", "hand_l", (-grip, 0, sh), (-grip, 0.1 * s, sh), deform=False),
            Bone("head_hat", "head", (0, 0.01 * s, head_top - 0.02 * s), (0, 0.11 * s, head_top - 0.02 * s), deform=False),
        ]
    bones += [Bone(*e) if not isinstance(e, Bone) else e for e in extra]
    return Skeleton("SK_Human", bones)


# --------------------------------------------------------------------------------------------------------------
# Materials

class Mat:
    """`tint` multiplies the texture (a flesh texture darkened to a railway tie's brown); `colour` is the flat
    fallback when the texture isn't there."""

    def __init__(self, name, colour, shine=0.08, emissive=0.0, glow=0.0, tint=None):
        self.name, self.colour, self.shine, self.emissive, self.glow = name, colour, shine, emissive, glow
        self.tint = tint
        self.texture = name.split(".")[0]

    def tile(self):
        return TILE.get(self.texture)


def hexc(h, k=1.0):
    h = h.lstrip("#")
    return tuple(k * int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


def make_material(m: Mat):
    mat = bpy.data.materials.get(m.name) or bpy.data.materials.new(m.name)
    mat.use_nodes = True
    mat.use_backface_culling = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    # Fallback colour, linear (glTF baseColorFactor is linear; the engine treats vertex colours as linear too).
    lin = tuple(c ** 2.2 for c in m.colour)
    bsdf.inputs["Base Color"].default_value = (*lin, 1)
    bsdf.inputs["Roughness"].default_value = 1 - min(0.9, m.shine)
    if m.emissive > 0:
        bsdf.inputs["Emission Color"].default_value = (*lin, 1)
        bsdf.inputs["Emission Strength"].default_value = m.emissive
    mat["dt_shine"] = float(m.shine)
    mat["dt_emissive"] = float(m.emissive)
    mat["dt_glow"] = float(m.glow)
    if m.tint is not None:
        mat["dt_tint"] = [float(c) for c in m.tint]
    return mat


# --------------------------------------------------------------------------------------------------------------
# The modelling kit

def frame_from(t, ref):
    """Orthonormal (side, up, t) around direction t, with `up` as close to `ref` as it can be."""
    t = t.normalized()
    r = Vector(ref)
    a = r - t * r.dot(t)
    if a.length < 1e-6:
        a = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((0, 1, 0))
        a = a - t * a.dot(t)
    a.normalize()
    side = t.cross(a)
    return side, a, t


class Part:
    """One mesh object's worth of faces: vertices with bone weights, faces with per-corner UVs and a material."""

    def __init__(self, kit, name, variants=None, clip=None, smooth=True):
        self.kit, self.name, self.variants, self.clip = kit, name, variants, clip
        self.v = []          # Vector
        self.w = []          # {bone: weight}
        self.f = []          # (indices, uvs, mat name, smooth)
        self.smooth = smooth

    # -- vertices and weights ----------------------------------------------------------------------------------
    def weights_for(self, p, bones):
        sk = self.kit.skeleton
        if isinstance(bones, str):
            return {bones: 1.0}
        if callable(bones):
            return bones(p)
        if isinstance(bones, dict):
            return dict(bones)
        names, k = (bones[0], bones[1]) if isinstance(bones, tuple) else (bones, 5.0)
        raw = []
        for n in names:
            d = sk.segment_distance(n, p)
            raw.append((1.0 / (d + 0.012) ** k, n))
        raw.sort(reverse=True)
        raw = raw[:4]
        tot = sum(w for w, _ in raw)
        out = {n: w / tot for w, n in raw if w / tot > 0.04}
        tot = sum(out.values())
        return {n: w / tot for n, w in out.items()}

    def add_v(self, p, bones):
        p = Vector(p)
        self.v.append(p)
        self.w.append(self.weights_for(p, bones))
        return len(self.v) - 1

    def face(self, idx, uvs, mat, smooth=None, outward=None, fuv=None, fmat=None):
        """A polygon; with `outward` (a point inside the solid), it's turned to face away from that point. `fuv(points,
        normal) -> uvs` maps the whole face at once (per-face projection: an atlas cell chosen by which way it faces)."""
        if outward is not None:
            n = self.normal(idx)
            c = sum((self.v[i] for i in idx), Vector()) / len(idx)
            if n.dot(c - Vector(outward)) < 0:
                idx, uvs = list(reversed(idx)), list(reversed(uvs))
        if fmat is not None:
            mat = fmat([self.v[i] for i in idx], self.normal(idx)) or mat
        if fuv is not None:
            uvs = fuv([self.v[i] for i in idx], self.normal(idx))
        elif fmat is not None:
            # Re-map for the material picked (its tile may differ): planar by the face's main axis, in metres.
            uvs = [self.axis_uv(self.v[i], self.normal(idx), mat) for i in idx]
        self.kit.use(mat)
        self.f.append((list(idx), list(uvs), mat.name, self.smooth if smooth is None else smooth))

    def normal(self, idx):
        n = Vector()
        pts = [self.v[i] for i in idx]
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            n += Vector(((a.y - b.y) * (a.z + b.z), (a.z - b.z) * (a.x + b.x), (a.x - b.x) * (a.y + b.y)))
        return n.normalized() if n.length > 1e-12 else Vector((0, 0, 1))

    def tris(self):
        return sum(len(f[0]) - 2 for f in self.f)

    # -- primitives ------------------------------------------------------------------------------------------
    def loft(self, rings, mat, bones, centres=None, uv=None, cap0=False, cap1=False, closed=True, smooth=None,
             inside=None, fuv=None, fmat=None):
        """Quads between successive rings (each a list of points, all the same count). UVs: `uv(i_ring, j, u_frac,
        v_frac, point)` -> (u, v) image space, or the default: around and along in metres over the texture's tile.
        Faces turn outward from each ring's centre (or `inside`, a point inside the solid)."""
        n = len(rings[0])
        centres = centres or [sum(r, Vector()) / len(r) for r in rings]
        idx = [[self.add_v(p, bones) for p in r] for r in rings]
        # Lengths for metre UVs.
        along = [0.0]
        for i in range(1, len(rings)):
            along.append(along[-1] + (centres[i] - centres[i - 1]).length)
        tot_along = max(along[-1], 1e-6)
        tile = mat.tile() or 1.0
        cols = n if closed else n - 1

        def around_len(r):
            return sum((r[(j + 1) % n] - r[j]).length for j in range(cols))

        def U(i, j):
            r = rings[i]
            if uv is not None:
                return uv(i, j, j / cols, along[i] / tot_along, r[j % n])
            circ = around_len(r)
            acc = sum((r[(k + 1) % n] - r[k]).length for k in range(j))
            return (acc / tile, along[i] / tile) if circ > 0 else (0, along[i] / tile)

        for i in range(len(rings) - 1):
            for j in range(cols):
                j1 = (j + 1) % n
                q = [idx[i][j], idx[i][j1], idx[i + 1][j1], idx[i + 1][j]]
                uvs = [U(i, j), U(i, j + 1), U(i + 1, j + 1), U(i + 1, j)]
                c = inside if inside is not None else (centres[i] + centres[i + 1]) / 2
                # Degenerate corners (a ring collapsed to a point) make triangles.
                if (self.v[q[0]] - self.v[q[1]]).length < 1e-7:
                    q, uvs = [q[0], q[2], q[3]], [uvs[0], uvs[2], uvs[3]]
                elif (self.v[q[2]] - self.v[q[3]]).length < 1e-7:
                    q, uvs = [q[0], q[1], q[2]], [uvs[0], uvs[1], uvs[2]]
                self.face(q, uvs, mat, smooth, outward=c, fuv=fuv, fmat=fmat)
        for cap, i, sign in ((cap0, 0, -1), (cap1, len(rings) - 1, 1)):
            if not cap:
                continue
            ring = idx[i]
            c = centres[i]
            nxt = centres[i - sign] if 0 <= i - sign < len(centres) else c
            inside_pt = c - (c - nxt).normalized() * 0.01 if (c - nxt).length > 1e-9 else c
            if cap == "point":
                ci = self.add_v(c, bones)
                for j in range(n if closed else n - 1):
                    tri = [ring[j], ring[(j + 1) % n], ci]
                    uvs = [self.planar_uv(self.v[t], mat) for t in tri]
                    self.face(tri, uvs, mat, smooth, outward=inside_pt, fuv=fuv, fmat=fmat)
            else:
                uvs = [self.planar_uv(self.v[t], mat) for t in ring]
                self.face(ring, uvs, mat, False if smooth is None else smooth, outward=inside_pt, fuv=fuv, fmat=fmat)
        return idx

    def axis_uv(self, p, n, mat):
        tile = mat.tile() or 1.0
        a = (abs(n.x), abs(n.y), abs(n.z))
        if a[2] >= a[0] and a[2] >= a[1]:
            return (p.x / tile, p.y / tile)
        if a[0] >= a[1]:
            return (p.y / tile, -p.z / tile)
        return (p.x / tile, -p.z / tile)

    def planar_uv(self, p, mat, axis=None):
        tile = mat.tile() or 1.0
        return (p.x / tile, -p.z / tile) if axis == "y" else (p.x / tile, p.y / tile)

    def tube(self, points, radii, sides, mat, bones, ref=(0, 1, 0), uv=None, cap0=False, cap1=False, twist=0.0,
             smooth=None, shape=None, square=1.0, loop=False, fuv=None, fmat=None):
        """A faceted tube through `points` with (rx, ry) radii per point (a number for round). `ref` (or a list of
        them, one per point) says which way the ry radius points; `square` < 1 squares the section off (shoulders,
        boots). `loop` joins the last point back to the first (a scarf, a belt). `shape(i, j, angle, point, (side,
        up, t))` may move each vertex (swellings, lumps)."""
        pts = [Vector(p) for p in points]
        refs = ref if isinstance(ref, list) else [ref] * len(pts)
        sq = square if isinstance(square, list) else [square] * len(pts)
        rings = []
        n = len(pts)
        for i, c in enumerate(pts):
            if loop:
                t = pts[(i + 1) % n] - pts[(i - 1) % n]
            else:
                t = (pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)])
            side, up, tt = frame_from(t, refs[i])
            r = radii[i]
            rx, ry = (r, r) if not isinstance(r, (tuple, list)) else r
            e = sq[i]
            ring = []
            for j in range(sides):
                a = 2 * math.pi * j / sides + twist
                sa, ca = math.sin(a), math.cos(a)
                sa = math.copysign(abs(sa) ** e, sa)
                ca = math.copysign(abs(ca) ** e, ca)
                p = c + side * (sa * rx) + up * (ca * ry)
                if shape is not None:
                    p = Vector(shape(i, j, a, p, (side, up, tt)))
                ring.append(p)
            rings.append(ring)
        if loop:
            rings.append([p.copy() for p in rings[0]])
            pts = pts + [pts[0]]
        return self.loft(rings, mat, bones, centres=pts, uv=uv, cap0=cap0, cap1=cap1, smooth=smooth, fuv=fuv, fmat=fmat)

    def sections(self, secs, sides, mat, bones, axis="y", cap0=False, cap1=False, sq=0.8, shape=None, **kw):
        """A loft through cross-sections along an axis: each (along, half_width, top, bottom[, squareness]) - the way a
        2006 modeller boxes out a torso or a skull from a side view and a top view."""
        rings, cents = [], []
        for s in secs:
            a, hw, top, bot = s[:4]
            e = s[4] if len(s) > 4 else sq
            zc, hz = (top + bot) / 2, (top - bot) / 2
            ring = []
            for j in range(sides):
                ang = 2 * math.pi * j / sides
                sa, ca = math.sin(ang), math.cos(ang)
                sa = math.copysign(abs(sa) ** e, sa)
                ca = math.copysign(abs(ca) ** e, ca)
                if axis == "y":
                    p = Vector((sa * hw, a, zc + ca * hz))
                else:  # along x (a sleeper lying across the rail)
                    p = Vector((a, sa * hw, zc + ca * hz))
                if shape is not None:
                    p = Vector(shape(len(rings), j, ang, p))
                ring.append(p)
            rings.append(ring)
            cents.append(Vector((0, a, zc)) if axis == "y" else Vector((a, 0, zc)))
        return self.loft(rings, mat, bones, centres=cents, cap0=cap0, cap1=cap1, **kw)

    def rock(self, centre, size, mat, bones, seed, sides=5, rings=3, spike=1.0, rot=None):
        """An angular lump (slag, mineral crust, a fused knot): a coarse blob with its vertices pushed about by a
        fixed hash, flat-shaded so each facet catches the lantern."""
        c = Vector(centre)
        sx, sy, sz = size if isinstance(size, (tuple, list)) else (size, size, size)

        def jag(i, j, a, th, p):
            q = p - c
            k = 1 + 0.35 * noise3(p, seed, 23.0)
            if i == 0:
                k *= spike
            return c + q * k
        return self.blob(c, (sx, sy, sz), sides, rings, mat, bones, rot=rot, shape=jag, smooth=False)

    def slab(self, outline, thickness, mat, bones, down=(0, 0, -1), uv=None, smooth=False):
        """A flat plate: `outline` (a convex polygon, any winding) and a copy `thickness` along `down`, joined round
        the edge (a cap's peak, a shovel blade, a sleeper's plank)."""
        top = [Vector(p) for p in outline]
        d = Vector(down).normalized() * thickness
        bot = [p + d for p in top]
        c = sum(top, Vector()) / len(top) + d / 2
        it = [self.add_v(p, bones) for p in top]
        ib = [self.add_v(p, bones) for p in bot]
        tile = mat.tile() or 1.0
        U = uv or (lambda p: (p.x / tile, p.y / tile))
        self.face(it, [U(p) for p in top], mat, smooth, outward=c)
        self.face(ib, [U(p) for p in bot], mat, smooth, outward=c)
        n = len(top)
        for j in range(n):
            k = (j + 1) % n
            q = [it[j], it[k], ib[k], ib[j]]
            self.face(q, [U(top[j]), U(top[k]), U(bot[k]), U(bot[j])], mat, smooth, outward=c)
        return it + ib

    def blob(self, centre, radii, around, rings, mat, bones, rot=None, uv=None, shape=None, smooth=None,
             z0=-1.0, z1=1.0, fuv=None, fmat=None):
        """An ellipsoid (lat-long), optionally only the band z0..z1 of it (in -1..1 of the z radius)."""
        c = Vector(centre)
        rx, ry, rz = radii
        R = rot.to_3x3() if rot is not None else Matrix.Identity(3)
        ringpts, cents = [], []
        lo, hi = math.acos(max(-1, min(1, z1))), math.acos(max(-1, min(1, z0)))
        for i in range(rings + 1):
            th = lo + (hi - lo) * i / rings
            z = math.cos(th)
            rr = math.sin(th)
            ring = []
            for j in range(around):
                a = 2 * math.pi * j / around
                local = Vector((math.sin(a) * rr * rx, math.cos(a) * rr * ry, z * rz))
                p = c + R @ local
                if shape is not None:
                    p = Vector(shape(i, j, a, th, p))
                ring.append(p)
            ringpts.append(ring)
            cents.append(c + R @ Vector((0, 0, z * rz)))
        return self.loft(ringpts, mat, bones, centres=cents, uv=uv, inside=c, smooth=smooth, fuv=fuv, fmat=fmat,
                         cap0="point" if z1 < 1 else False, cap1="point" if z0 > -1 else False)

    def box(self, centre, half, mat, bones, rot=None, taper=(1.0, 1.0), uv=None, smooth=False, bevel=0.0):
        """A box (optionally tapered towards +Z: top scaled by `taper` (x, y)); faces planar-mapped in metres,
        or by `uv(face_axis, corner_local)`."""
        c = Vector(centre)
        hx, hy, hz = half
        R = rot.to_3x3() if rot is not None else Matrix.Identity(3)
        corners = []
        for sz in (-1, 1):
            for sy in (-1, 1):
                for sx in (-1, 1):
                    k = taper if sz > 0 else (1.0, 1.0)
                    corners.append(Vector((sx * hx * k[0], sy * hy * k[1], sz * hz)))
        ids = [self.add_v(c + R @ p, bones) for p in corners]
        tile = mat.tile() or 1.0

        def ci(sx, sy, sz):
            return ((sz > 0) << 2) | ((sy > 0) << 1) | (sx > 0)

        faces = [("x", 1, [(1, -1, -1), (1, 1, -1), (1, 1, 1), (1, -1, 1)]),
                 ("x", -1, [(-1, 1, -1), (-1, -1, -1), (-1, -1, 1), (-1, 1, 1)]),
                 ("y", 1, [(1, 1, -1), (-1, 1, -1), (-1, 1, 1), (1, 1, 1)]),
                 ("y", -1, [(-1, -1, -1), (1, -1, -1), (1, -1, 1), (-1, -1, 1)]),
                 ("z", 1, [(-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]),
                 ("z", -1, [(-1, 1, -1), (1, 1, -1), (1, -1, -1), (-1, -1, -1)])]
        for axis, sgn, cs in faces:
            q = [ids[ci(*s)] for s in cs]
            loc = [corners[ci(*s)] for s in cs]
            if uv is not None:
                uvs = [uv(axis, sgn, l) for l in loc]
            elif axis == "x":
                uvs = [(l.y * sgn / tile, -l.z / tile) for l in loc]
            elif axis == "y":
                uvs = [(-l.x * sgn / tile, -l.z / tile) for l in loc]
            else:
                uvs = [(l.x / tile, l.y / tile) for l in loc]
            self.face(q, uvs, mat, smooth, outward=c)
        return ids

    def displace(self, fn, start=0):
        """Moves vertices added since `start` by fn(p) -> new p (deterministic lumps and swellings)."""
        for i in range(start, len(self.v)):
            self.v[i] = Vector(fn(self.v[i]))

    def mark(self):
        return len(self.v)


class Kit:
    def __init__(self, skeleton: Skeleton, name):
        self.skeleton, self.name = skeleton, name
        self.parts = []
        self.mats = {}

    def part(self, name, **kw):
        p = Part(self, name, **kw)
        self.parts.append(p)
        return p

    def use(self, mat: Mat):
        if mat.name not in self.mats:
            self.mats[mat.name] = mat

    def tris(self):
        return sum(p.tris() for p in self.parts)

    def build(self):
        rig = self.skeleton.rig
        blender_mats = {n: make_material(m) for n, m in sorted(self.mats.items())}
        for part in self.parts:
            if not part.f:
                continue
            me = bpy.data.meshes.new(f"{self.name}_{part.name}")
            bm = bmesh.new()
            bv = [bm.verts.new(p) for p in part.v]
            bm.verts.ensure_lookup_table()
            uvl = bm.loops.layers.uv.new("UVMap")
            used = sorted({f[2] for f in part.f})
            slot = {n: i for i, n in enumerate(used)}
            for idx, uvs, mname, smooth in part.f:
                if len(set(idx)) < 3:
                    continue
                try:
                    face = bm.faces.new([bv[i] for i in idx])
                except ValueError:
                    continue  # a duplicate face: skip, the other copy draws it
                face.material_index = slot[mname]
                face.smooth = smooth
                for loop, (u, v) in zip(face.loops, uvs):
                    loop[uvl].uv = (u, 1.0 - v)
            bm.to_mesh(me)
            bm.free()
            for n in used:
                me.materials.append(blender_mats[n])
            ob = bpy.data.objects.new(part.name, me)
            bpy.context.scene.collection.objects.link(ob)
            ob.parent = rig
            if part.variants is not None:
                ob["variants"] = ",".join(str(v) for v in part.variants)
            if part.clip is not None:
                ob["clip"] = part.clip
            groups = {}
            for i, w in enumerate(part.w):
                for bname, wt in w.items():
                    g = groups.get(bname)
                    if g is None:
                        g = groups[bname] = ob.vertex_groups.new(name=bname)
                    g.add([i], wt, "REPLACE")
            mod = ob.modifiers.new("Armature", "ARMATURE")
            mod.object = rig
        return self


# --------------------------------------------------------------------------------------------------------------
# Clips

def rot(rx=0.0, ry=0.0, rz=0.0):
    """A joint rotation in the armature's axes, applied Y first (lower/raise an arm from the T-pose, lean a spine
    sideways), then X (swing forward/back: + swings a hanging limb forward, tips a spine or head back), then Z
    (turn about the vertical: + turns towards -X, the left)."""
    return Euler((math.radians(rx), math.radians(ry), math.radians(rz)), "YXZ").to_quaternion()


def _split(k):
    """'thigh_r@loc' -> ('thigh_r', '@loc')."""
    i = k.find("@")
    return (k, "") if i < 0 else (k[:i], k[i:])


def _flip(suffix, v):
    """A value mirrored across the body's centre plane (x = 0)."""
    if suffix == "@spin":
        return -v
    if suffix == "@loc":
        return (-v[0], v[1], v[2])
    if suffix == "@scale":
        return v
    return (v[0], -v[1], -v[2])


def _other(name):
    if name.endswith("_r"):
        return name[:-2] + "_l"
    if name.endswith("_l"):
        return name[:-2] + "_r"
    return None


def mirror(pose):
    """Adds the _l side of every _r entry (and vice versa) mirrored across the body's centre plane."""
    out = dict(pose)
    for k, v in pose.items():
        name, suffix = _split(k)
        o = _other(name)
        if o is not None and o + suffix not in pose:
            out[o + suffix] = _flip(suffix, v)
    return out


def swap(pose):
    """The pose with left and right exchanged (the second half of a gait from the first)."""
    out = {}
    for k, v in pose.items():
        name, suffix = _split(k)
        o = _other(name)
        out[(o or name) + suffix] = _flip(suffix, v)
    return out


def blend(a, b, t):
    """Per-joint mix of two poses (angles and offsets linearly: fine for the small differences keys have)."""
    out = {}
    for k in set(a) | set(b):
        va, vb = a.get(k, (0, 0, 0)), b.get(k, (0, 0, 0))
        out[k] = tuple(x + (y - x) * t for x, y in zip(va, vb))
    return out


def over(base, **changes):
    """`base` with some joints replaced (keyword names use __ for @: pelvis__loc)."""
    out = dict(base)
    for k, v in changes.items():
        out[k.replace("__", "@")] = v
    return out


def add(base, delta):
    """`base` with `delta`'s angles added on."""
    out = dict(base)
    for k, v in delta.items():
        out[k] = tuple(x + y for x, y in zip(out.get(k, (0, 0, 0)), v))
    return out


class Clip:
    """A hand-keyed clip: poses at frames (30 fps), each pose {bone: (rx, ry, rz) degrees} (see `rot`) and
    {bone@loc: (x, y, z) metres}, in the armature's axes. `interp` per key: BEZIER (eased, the crew's weight),
    LINEAR, or CONSTANT (a hold then a pop: the monsters' too-fast corrections, GDD §31). A loop's last key must
    equal its first: `Clip.close()` does that."""

    def __init__(self, name, loop=True):
        self.name, self.loop = name, loop
        self.keys = []

    def key(self, frame, pose, interp="BEZIER"):
        self.keys.append((int(frame), dict(pose), interp))
        return self

    def hold(self, frame, interp="BEZIER"):
        """The previous key's pose again (a hold)."""
        return self.key(frame, self.keys[-1][1], interp)

    def close(self, frame):
        """Ends a loop on its first pose."""
        return self.key(frame, self.keys[0][1], self.keys[0][2])

    @property
    def length(self):
        return max(k[0] for k in self.keys)


def bake(skeleton: Skeleton, clips, plant=None):
    """Turns each clip into a Blender action stashed on the rig (so the exporter writes one glTF animation per
    clip). Every deform bone is keyed at every key (unlisted: at rest), so no clip inherits another's pose.
    `plant(clip_name, frame, pose) -> dz` may lift or drop the root so feet stay on the floor."""
    rig = skeleton.rig
    rig.animation_data_create()
    rest = {b.name: b.matrix_local.to_quaternion() for b in rig.data.bones}
    names = [b.name for b in skeleton.bones]
    for clip in clips:
        act = bpy.data.actions.new(clip.name)
        act.use_fake_user = True
        rig.animation_data.action = act
        prev = {}
        # Bones some key scales (a sac pulsing) are keyed for scale at every key of the clip (bone-local axes).
        scaled = {n: any(n + "@scale" in k[1] for k in clip.keys) for n in names}
        for frame, pose, interp in sorted(clip.keys, key=lambda k: k[0]):
            pose = dict(pose)
            if plant is not None:
                dz = plant(clip.name, frame, pose)
                if dz:
                    x, y, z = pose.get("root@loc", (0, 0, 0))
                    pose["root@loc"] = (x, y, z + dz)
            for n in names:
                pb = rig.pose.bones[n]
                B = rest[n]
                r = pose.get(n, (0, 0, 0))
                R = r.copy() if isinstance(r, Quaternion) else rot(*r)
                if n + "@spin" in pose:
                    # Turned about its own length (a drill bit), then posed as usual.
                    axis = (B @ Vector((0, 1, 0))).normalized()
                    R = R @ Quaternion(axis, math.radians(pose[n + "@spin"]))
                q = B.inverted() @ R @ B
                if n in prev and prev[n].dot(q) < 0:
                    q = -q
                prev[n] = q
                pb.rotation_quaternion = q
                pb.keyframe_insert("rotation_quaternion", frame=frame, group=n)
                if scaled.get(n):
                    pb.scale = Vector(pose.get(n + "@scale", (1, 1, 1)))
                    pb.keyframe_insert("scale", frame=frame, group=n)
                if n + "@loc" in pose or n in ("root", "pelvis"):
                    t = Vector(pose.get(n + "@loc", (0, 0, 0)))
                    pb.location = B.inverted() @ t
                    pb.keyframe_insert("location", frame=frame, group=n)
        # Per-key interpolation.
        interps = {k[0]: k[2] for k in clip.keys}
        for fc in act.fcurves:
            for kp in fc.keyframe_points:
                kp.interpolation = interps.get(int(round(kp.co.x)), "BEZIER")
                kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
        act.use_frame_range = True
        act.frame_start, act.frame_end = 0, clip.length
        tr = rig.animation_data.nla_tracks.new()
        tr.name = clip.name
        tr.strips.new(clip.name, 0, act)
        rig.animation_data.action = None
    # Back to the bind pose, so nothing exports posed.
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()
        pb.scale = Vector((1, 1, 1))


def pose_points(skeleton: Skeleton, pose, points):
    """Where bone heads/tails end up in `pose` (armature space), without Blender: plain FK over the templates, for
    planting feet. `points` is [(bone, 'head'|'tail')]."""
    world = {}
    out = []
    for b in skeleton.bones:
        r = pose.get(b.name, (0, 0, 0))
        R = (r if isinstance(r, Quaternion) else rot(*r)).to_matrix()
        loc = Vector(pose.get(b.name + "@loc", (0, 0, 0)))
        if b.parent is None:
            W = R
            head = b.head + loc
        else:
            pW, ph = world[b.parent]
            pb = skeleton[b.parent]
            W = pW @ R
            head = ph + pW @ (b.head - pb.head) + pW @ loc
        world[b.name] = (W, head)
    for name, which in points:
        W, head = world[name]
        b = skeleton[name]
        out.append(head if which == "head" else head + W @ (b.tail - b.head))
    return out


def world_rotation(skeleton: Skeleton, pose, name):
    """The rotation a bone has picked up from its chain in `pose` (armature axes, relative to its rest)."""
    W = Matrix.Identity(3)
    chain = []
    b = skeleton[name]
    while b is not None:
        chain.append(b.name)
        b = skeleton[b.parent] if b.parent else None
    for n in reversed(chain):
        r = pose.get(n, (0, 0, 0))
        W = W @ (r if isinstance(r, Quaternion) else rot(*r)).to_matrix()
    return W.to_quaternion()


def hang(skeleton: Skeleton, pose, name, rx=0.0, ry=0.0, rz=0.0):
    """The local rotation that leaves bone `name` at world rotation rot(rx, ry, rz) whatever its parents do: a lantern
    that hangs plumb from a swinging hand."""
    parent = skeleton[name].parent
    Wp = world_rotation(skeleton, pose, parent) if parent else Quaternion()
    return Wp.inverted() @ rot(rx, ry, rz)


def reach(skeleton: Skeleton, pose, upper, lower, target, end=None, elbow_axis=2, bend=1.0, avoid=None, pole=None):
    """Poses an arm (or leg) so the tip of `end` (default: `lower`'s child's tail... the lower bone's tail) reaches
    `target`: a deterministic coordinate descent over the upper bone's three angles and the lower bone's bend about
    `elbow_axis` (0 x, 1 y, 2 z; `bend` its sign). Returns the pose with those two joints set. `avoid(point_of_elbow)`
    may add a penalty (keep an elbow out of the body); `pole`, a point the elbow (or knee) is drawn toward, says which
    way it points (down and out on a ladder's rungs, back behind a runner)."""
    target = Vector(target)
    if pole is not None:
        pole = Vector(pole)
        avoid0 = avoid
        avoid = lambda e: (avoid0(e) if avoid0 else 0.0) + 0.05 * (e - pole).length  # noqa: E731
    p = dict(pose)
    up = list(p.get(upper, (0.0, 0.0, 0.0)))
    lo = list(p.get(lower, (0.0, 0.0, 0.0)))
    tip = (end, "tail") if end else (lower, "tail")

    def cost(u, l):
        q = dict(p)
        q[upper] = tuple(u)
        q[lower] = tuple(l)
        t, e = pose_points(skeleton, q, [tip, (lower, "head")])
        c = (t - target).length
        if avoid is not None:
            c += avoid(e)
        if l[elbow_axis] * bend < 0:
            c += 0.01 * abs(l[elbow_axis])  # elbows and knees bend one way
        return c

    best = cost(up, lo)
    for step in (24, 12, 6, 3, 1.5):
        improved = True
        while improved:
            improved = False
            for k in range(4):
                for sgn in (1, -1):
                    u, l = list(up), list(lo)
                    if k < 3:
                        u[k] += sgn * step
                    else:
                        l[elbow_axis] += sgn * step
                    c = cost(u, l)
                    if c < best - 1e-6:
                        best, up, lo, improved = c, u, l, True
    p[upper] = tuple(round(v, 2) for v in up)
    p[lower] = tuple(round(v, 2) for v in lo)
    return p


def feet_planter(skeleton: Skeleton, floor=None, bones=("foot_l", "foot_r", "ball_l", "ball_r"), clips=None,
                 lowest=0.03):
    """A `plant` for `bake`: drops or lifts the whole body so its lowest foot point sits at `lowest` above the
    floor (the sole under it). Only for the clips named (a jump's flight must leave the ground)."""
    def plant(name, frame, pose):
        if clips is not None and name not in clips:
            return 0.0
        pts = pose_points(skeleton, pose, [(b, "head") for b in bones] + [(b, "tail") for b in bones])
        low = min(p.z for p in pts)
        return lowest - low
    return plant


# --------------------------------------------------------------------------------------------------------------
# Export

def export(path, kit: Kit):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    # Working meshes a script leaves about (a dense high copy for tools/models to bake from) don't go in the file.
    for o in [o for o in bpy.data.objects if o.get("dt_scrap")]:
        bpy.data.objects.remove(o)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False,
        export_animations=True, export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_step=1,
        export_optimize_animation_size=False, export_anim_slide_to_zero=True, export_def_bones=False,
        export_extras=True, export_skins=True, export_morph=False, export_texcoords=True, export_normals=True,
        export_tangents=False, export_materials="EXPORT", export_image_format="NONE", export_cameras=False,
        export_lights=False, export_all_influences=False, export_reset_pose_bones=True)
    bones = len(kit.skeleton.bones)
    # Every mesh on the rig, the kit's parts and any a script modelled itself (tools/blender/crewbody.py's).
    meshes = sorted((o for o in bpy.data.objects if o.type == "MESH" and o.parent is kit.skeleton.rig), key=lambda o: o.name)
    tris = {}
    for o in meshes:
        o.data.calc_loop_triangles()
        tris[o.name] = len(o.data.loop_triangles)
    print(f"[dt] {os.path.basename(path)}: {sum(tris.values())} tris, {bones} bones, "
          f"{len(bpy.data.materials)} materials, parts {[n + ':' + str(t) for n, t in tris.items()]}")


def along(axis, stops, extra=None):
    """Weights as a function of one coordinate: `stops` [(coord, bone)...] ascending; between two stops a vertex
    blends the two bones linearly. Rigid PS2-style skinning with soft joints. axis 'x' uses |x| (both sides share
    stops; name bones with {s} for the side: 'upperarm_{s}'). `extra(p, w)` may adjust the result."""
    ai = "xyz".index(axis[0])

    def fn(p):
        c = abs(p[ai]) if axis == "x" else p[ai]
        side = "r" if p.x >= 0 else "l"
        names = [(z, b.format(s=side)) for z, b in stops]
        if c <= names[0][0]:
            w = {names[0][1]: 1.0}
        elif c >= names[-1][0]:
            w = {names[-1][1]: 1.0}
        else:
            w = {}
            for (z0, b0), (z1, b1) in zip(names, names[1:]):
                if z0 <= c <= z1:
                    t = (c - z0) / max(z1 - z0, 1e-9)
                    w[b0] = w.get(b0, 0) + 1 - t
                    w[b1] = w.get(b1, 0) + t
                    break
        if extra is not None:
            w = extra(p, w)
        w = {k: v for k, v in w.items() if v > 0.02}
        tot = sum(w.values())
        return {k: v / tot for k, v in w.items()}
    return fn


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


def noise3(p, seed=0, scale=1.0):
    """A cheap deterministic value noise in -1..1 (lumps and asymmetry; no RNG state, so rebuilds are identical)."""
    x, y, z = p.x * scale, p.y * scale, p.z * scale
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    xf, yf, zf = x - xi, y - yi, z - zi

    def h(i, j, k):
        n = (i * 73856093) ^ (j * 19349663) ^ (k * 83492791) ^ (seed * 2654435761)
        n = (n ^ (n >> 13)) * 1274126177
        return ((n ^ (n >> 16)) & 0xFFFF) / 32767.5 - 1.0

    def s(t):
        return t * t * (3 - 2 * t)

    u, v, w = s(xf), s(yf), s(zf)
    acc = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                acc += h(xi + dx, yi + dy, zi + dz) * (u if dx else 1 - u) * (v if dy else 1 - v) * (w if dz else 1 - w)
    return acc
