"""THE MIMIC (GDD v1.5 §21, App. A.6 · movement): "The best-looking crate in the yard. ... Its tells are all small: it's
one more than the count chalked by the stack, it breathes when you stand still beside it, it's heavier than a crate
should be, and it never goes into a car's load. Set down aboard, it wakes, and bites whoever comes near."

It's a medicine chest: the field-hospital chest of the facilities' freight (tools/models/recipes/freight_medical), the
grey-white paint, the red cross, the brass corners; the crate a crew most wants, and one they've seen real ones of in
every yard. Shut, it IS that chest: its shell is the prop's own game mesh and baked layers, cut along the lid's seam
(so nothing about it is a tell but the four the GDD gives it). Open, the chest is a mouth: the inside of it raw and wet,
the dark of a throat at the bottom; the rim of the box and the lid's edge are gums, crowded with teeth, long and
yellowed and every way crooked; and a tongue, thick and grey-red and too long, lying in it.

SK_Mimic: root, the box, the lid (hinged along its back edge), and the tongue's six. Faces +Y (the hasp's side; the
prop's is -Y, so CreatureArt turns it to lie as the prop would). Its origin is on the floor under its middle. Clips:
shut (a chest: nothing moves), breathe (someone stood still beside it: the lid lifts a hair and settles, slowly), lid
(the TELEGRAPH, once over its 1.8 s: the lid creaks up in two goes, and the tongue's tip comes over the rim), chew (the
GRAB: wide, slamming shut on its catch and wide again, the tongue out and wrapped up them, the box rocking and heaving),
swallow (PUNISH: shut, gulping), hit.

    tools/blender/build.sh mimic        # -> content/art/models/mimic.glb (needs content/art/models/props/freight_medical.glb)
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, hexc, noise3, over  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
PROP = os.path.join(ROOT, "content", "art", "models", "props", "freight_medical.glb")

rig.reset()
H = 0.44                      # the chest's half size (freight_medical's H)
LIFT = H                      # its middle is the prop's origin; the model's is the floor under it
SEAM = H - 0.14 + LIFT        # the lid's seam, a hand below the top (freight_medical: "the lid's seam a hand below the top")
WALL = 0.022                  # the boards' thickness: the inside is this far in
# The prop's box, turned to face +Y (x and y negated): its walls at these, measured off the prop's game mesh below.
X0, X1 = -H, H
Y0, Y1 = -H, H
TOPZ = 2 * H

bones = [Bone("root", None, (0, 0, 0), (0, 0.1, 0)),
         Bone("box", "root", (0, 0, 0), (0, 0, SEAM)),
         Bone("lid", "box", (0, Y0, SEAM), (0, Y1, SEAM))]
TONGUE = [Vector((0, -0.3, 0.42)), Vector((0, -0.16, 0.44)), Vector((0, -0.02, 0.46)), Vector((0, 0.1, 0.48)), Vector((0, 0.2, 0.5)),
          Vector((0, 0.28, 0.52)), Vector((0, 0.34, 0.53))]
for k in range(6):
    bones.append(Bone(f"tongue_{k + 1}", "box" if k == 0 else f"tongue_{k}", tuple(TONGUE[k]), tuple(TONGUE[k + 1])))
sk = Skeleton("SK_Mimic", bones)
sk.build()
kit = rig.Kit(sk, "mimic")

FLESH = Mat("flesh.mimic", hexc("#6a3430"), shine=0.6, tint=(1.2, 0.46, 0.42))
WET = Mat("tar.mimic_throat", hexc("#1a0606"), shine=0.8, tint=(0.5, 0.18, 0.18))
GUM = Mat("flesh.mimic_gum", hexc("#7a3e3a"), shine=0.65, tint=(1.45, 0.55, 0.5))
TEETH = Mat("skin.mimic_teeth", hexc("#c8b78e"), shine=0.5, tint=(1.5, 1.45, 1.2))
TONGUE_M = Mat("flesh.mimic_tongue", hexc("#8a5550"), shine=0.6, tint=(1.4, 0.9, 0.88))
RAW = Mat("wood_crate.mimic_raw", hexc("#6e5a44"), shine=0.1, tint=(1.0, 0.92, 0.84))


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------------------------------------------
# The shell: the prop's own game mesh (its UVs on its own baked layers, freight_medical_0), turned to face +Y and set on
# the floor, cut through along the seam: the lid above it, the box under.
bpy.ops.import_scene.gltf(filepath=PROP)
low = bpy.data.objects["freight_medical_low"]
place = Matrix.Translation((0, 0, LIFT)) @ Matrix.Rotation(math.pi, 4, "Z")
bm = bmesh.new()
bm.from_mesh(low.data)
bm.transform(place @ low.matrix_world)
ys = [v.co.y for v in bm.verts if abs(v.co.x) < H + 1e-4]
Y0, Y1 = min(ys), max(ys)
print("[dt] mimic shell", len(bm.faces), "faces, walls", round(Y0, 4), round(Y1, 4))
bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], plane_co=(0, 0, SEAM), plane_no=(0, 0, 1))
for f in bm.faces:
    f.smooth = True
shell_mat = low.data.materials[0]
shell_mat.name = "freight_medical_0"


def shade_as_prop(me):
    """The prop's normals, which bmesh doesn't carry: its box is shaded smooth from corner normals on the diagonals (its
    layers were baked against them), which on a cube is the way from its middle to the point, wherever the cut put a
    vertex; the handles' rings smooth."""
    centre = Vector((0, (Y0 + Y1) / 2, LIFT))

    def on_box(co):
        d = co - centre
        return abs(max(abs(d.x), abs(d.y), abs(d.z)) - H) < 2e-3

    me.calc_normals_split()
    normals = [l.normal.copy() for l in me.loops]
    for poly in me.polygons:
        if all(on_box(me.vertices[me.loops[li].vertex_index].co) for li in poly.loop_indices):
            for li in poly.loop_indices:
                normals[li] = (me.vertices[me.loops[li].vertex_index].co - centre).normalized()
    me.use_auto_smooth = True
    me.normals_split_custom_set(normals)


def piece(name, keep, bone):
    b = bm.copy()
    bmesh.ops.delete(b, geom=[f for f in b.faces if not keep(f.calc_center_median().z)], context="FACES")
    me = bpy.data.meshes.new(f"mimic_{name}")
    b.to_mesh(me)
    b.free()
    me.materials.clear()
    me.materials.append(shell_mat)
    shade_as_prop(me)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    ob.parent = sk.rig
    g = ob.vertex_groups.new(name=bone)
    g.add(list(range(len(me.vertices))), 1.0, "REPLACE")
    ob.modifiers.new("Armature", "ARMATURE").object = sk.rig
    return ob


piece("shell_box", lambda z: z < SEAM, "box")
piece("shell_lid", lambda z: z > SEAM, "lid")
bm.free()
# What the import brought besides the mesh (its armature, its bone shape) goes; the shell's material stays.
for o in [o for o in bpy.data.objects if o.parent is not sk.rig and o is not sk.rig]:
    bpy.data.objects.remove(o)
for a in [a for a in bpy.data.armatures if a is not sk.rig.data]:
    bpy.data.armatures.remove(a)
# The bones' rest along the box as measured: the lid's hinge on the back wall.
for b in sk.bones:
    if b.name == "lid":
        b.head, b.tail = Vector((0, Y0, SEAM)), Vector((0, Y1, SEAM))
bpy.context.view_layer.objects.active = sk.rig
bpy.ops.object.mode_set(mode="EDIT")
eb = sk.rig.data.edit_bones["lid"]
eb.head, eb.tail = Vector((0, Y0, SEAM)), Vector((0, Y1, SEAM))
bpy.ops.object.mode_set(mode="OBJECT")
YC = (Y0 + Y1) / 2


def strip(part, rows, mat, bone, inward, uvs=None):
    """Quads between successive rows of points; each face turned to the inside of the chest (`inward`: towards the middle
    line x = 0, y = YC) or away from it."""
    idx = [[part.add_v(p, bone) for p in row] for row in rows]
    for r in range(len(rows) - 1):
        for k in range(len(rows[r]) - 1):
            q = [idx[r][k], idx[r][k + 1], idx[r + 1][k + 1], idx[r + 1][k]]
            pts = [part.v[i] for i in q]
            c = sum(pts, Vector()) / 4
            n = part.normal(q)
            mid = Vector((0, YC, c.z))
            out = n.dot(c - mid) > 0
            if out == inward:
                q, pts, n = list(reversed(q)), list(reversed(pts)), -n
            part.face(q, [part.axis_uv(p, n, mat) for p in pts], mat, False)


def ring(x0, x1, y0, y1, z, steps=4):
    """A rectangle's outline at height z, round from the back left, `steps` points a side, closed."""
    pts = []
    for k in range(steps):
        pts.append(Vector((x0 + (x1 - x0) * k / steps, y0, z)))
    for k in range(steps):
        pts.append(Vector((x1, y0 + (y1 - y0) * k / steps, z)))
    for k in range(steps):
        pts.append(Vector((x1 - (x1 - x0) * k / steps, y1, z)))
    for k in range(steps):
        pts.append(Vector((x0, y1 - (y1 - y0) * k / steps, z)))
    return pts + [pts[0].copy()]


# ----------------------------------------------------------------------------------------------------------------
# The inside of the box: the cut edge of the boards round its rim; the walls inside raw and wet, going down into the dark
# of a throat at the bottom; gums along the rim, and the teeth on them.
mouth = kit.part("mouth")
xi0, xi1, yi0, yi1 = X0 + WALL, X1 - WALL, Y0 + WALL, Y1 - WALL
# The boards' cut edge, the rim: a band from the outside to the inside at the seam.
rim_out, rim_in = ring(X0, X1, Y0, Y1, SEAM), ring(xi0, xi1, yi0, yi1, SEAM)
strip(mouth, [rim_out, rim_in], RAW, "box", inward=False)  # (faces up: neither in nor out, the normal's z; turned below)
mouth.f = [(list(reversed(f[0])), list(reversed(f[1])), f[2], f[3]) if mouth.normal(f[0]).z < 0 else f for f in mouth.f]
# The walls inside, down into it, narrowing to a throat.
walls = [ring(xi0, xi1, yi0, yi1, SEAM)]
for z, k in ((SEAM - 0.08, 0.98), (SEAM - 0.2, 0.9), (0.28, 0.72), (0.16, 0.4)):
    xm, ym = (xi1 - xi0) / 2 * k, (yi1 - yi0) / 2 * k
    walls.append(ring(-xm, xm, YC - ym, YC + ym, z))
strip(mouth, walls[:4], FLESH, "box", inward=True)
strip(mouth, walls[3:], WET, "box", inward=True)
mouth.blob(Vector((0, YC, 0.16)), ((xi1 - xi0) / 2 * 0.4, (yi1 - yi0) / 2 * 0.4, 0.03), 12, 3, WET, "box", z1=0.0)
# The gums, a fat ridge round the inside of the rim, lumpy.
gum_pts = ring(xi0 + 0.012, xi1 - 0.012, yi0 + 0.012, yi1 - 0.012, SEAM - 0.012, steps=6)
mouth.tube(gum_pts[:-1], [(0.016, 0.022)] * (len(gum_pts) - 1), 6, GUM, "box", ref=(0, 0, 1), loop=True,
           shape=lambda i, j, a, p, fr: Vector(p) + Vector((0, 0, 0.004 * noise3(Vector(p) * 30, 701, 1.0))))


def teeth_along(part, pts, bone, up, seed):
    """Teeth on a rim: long and short and every way crooked, crowded, one every few centimetres; `up` the way they point."""
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        n = max(1, int((b - a).length / 0.045))
        for m in range(n):
            base = a.lerp(b, (m + 0.5) / n)
            h = 0.5 + 0.5 * noise3(base * 13, seed, 1.0)
            ln = 0.03 + 0.055 * h * h
            lean = Vector((0.012 * noise3(base * 17, seed + 1, 1.0), 0.012 * noise3(base * 19, seed + 2, 1.0), 0))
            inward = (Vector((0, YC, base.z)) - base).normalized() * 0.01
            tip = base + Vector((0, 0, up * ln)) + lean + inward
            w = 0.008 + 0.006 * h
            part.tube([base, base.lerp(tip, 0.45), tip], [(w, w * 0.7), (w * 0.8, w * 0.6), (w * 0.15, w * 0.12)], 5, TEETH, bone,
                      ref=(0, 0, 1), cap1="point")


teeth_along(mouth, ring(xi0 + 0.016, xi1 - 0.016, yi0 + 0.016, yi1 - 0.016, SEAM - 0.006, steps=5), "box", 1, 711)

# The lid's inside: its cut edge, the walls in it, the roof of the mouth (ridged, wet), gums and teeth along its edge.
lid = kit.part("lid")
strip(lid, [ring(X0, X1, Y0, Y1, SEAM), ring(xi0, xi1, yi0, yi1, SEAM)], RAW, "lid", inward=False)
lid.f = [(list(reversed(f[0])), list(reversed(f[1])), f[2], f[3]) if lid.normal(f[0]).z > 0 else f for f in lid.f]
roof = TOPZ - WALL
strip(lid, [ring(xi0, xi1, yi0, yi1, SEAM), ring(xi0, xi1, yi0, yi1, roof)], FLESH, "lid", inward=True)
# The roof of the mouth, under the lid's boards: ridged across like a dog's, sagging a little in the middle.
NX, NY = 8, 14
grid = [[Vector((xi0 + (xi1 - xi0) * i / NX, yi0 + (yi1 - yi0) * j / NY,
                 roof - 0.004 - 0.018 * math.sin(math.pi * i / NX) * math.sin(math.pi * j / NY) - 0.006 * (j % 2)))
         for i in range(NX + 1)] for j in range(NY + 1)]
gi = [[lid.add_v(p, "lid") for p in row] for row in grid]
for j in range(NY):
    for i in range(NX):
        q = [gi[j][i], gi[j][i + 1], gi[j + 1][i + 1], gi[j + 1][i]]
        pts = [lid.v[k] for k in q]
        c = sum(pts, Vector()) / 4
        lid.face(q, [lid.axis_uv(p, Vector((0, 0, -1)), FLESH) for p in pts], FLESH, True, outward=c + Vector((0, 0, 1)))
gum_lid = ring(xi0 + 0.012, xi1 - 0.012, yi0 + 0.012, yi1 - 0.012, SEAM + 0.012, steps=6)
lid.tube(gum_lid[:-1], [(0.016, 0.02)] * (len(gum_lid) - 1), 6, GUM, "lid", ref=(0, 0, 1), loop=True)
teeth_along(lid, ring(xi0 + 0.018, xi1 - 0.018, yi0 + 0.018, yi1 - 0.018, SEAM + 0.008, steps=5), "lid", -1, 721)

# The tongue: thick, grey-red, a groove down it, lying in the mouth from the back of the throat to the front.
tongue = kit.part("tongue")
TR = [(0.075, 0.045), (0.08, 0.044), (0.074, 0.04), (0.066, 0.036), (0.056, 0.03), (0.042, 0.024), (0.02, 0.012)]


def tongue_weights(p):
    best, out = None, {}
    for k in range(6):
        d = sk.segment_distance(f"tongue_{k + 1}", p)
        if best is None or d < best[0]:
            best = (d, k)
    k = best[1]
    out[f"tongue_{k + 1}"] = 1.0
    # Soft across each joint with the next.
    a, b = TONGUE[k], TONGUE[k + 1]
    t = (p - a).dot(b - a) / (b - a).length_squared
    if t > 0.75 and k < 5:
        s = smooth01(0.75, 1.25, t)
        out = {f"tongue_{k + 1}": 1 - s * 0.5, f"tongue_{k + 2}": s * 0.5}
    elif t < 0.25 and k > 0:
        s = smooth01(0.25, -0.25, t)
        out = {f"tongue_{k + 1}": 1 - s * 0.5, f"tongue_{k}": s * 0.5}
    return out


tongue.tube(TONGUE, TR, 10, TONGUE_M, tongue_weights, ref=(0, 0, 1), cap1="point",
            shape=lambda i, j, a, p, fr: Vector(p) - fr[1] * 0.012 * math.exp(-(math.sin(a) / 0.25) ** 2) * (math.cos(a) > 0))


# ----------------------------------------------------------------------------------------------------------------
# Clips. The lid opens by +X (about its hinge, the back edge); the tongue's joints lift by +X and swing by Z; the box
# rocks on the root. A shut chest is the prop to the millimetre, so `shut` moves nothing.
SHUT = {}


def tongue_pose(lift=(0, 0, 0, 0, 0, 0), swing=(0, 0, 0, 0, 0, 0), out=0.0, up=0.0):
    """The tongue's joints lifted and swung (degrees, each on the last), its root slid `out` forward and `up` out of the
    throat."""
    p = {f"tongue_{k + 1}": (lift[k], 0, swing[k]) for k in range(6)}
    p["tongue_1@loc"] = (0, out, up)
    return p


REST_TONGUE = tongue_pose()

shut = Clip("shut")
shut.key(0, REST_TONGUE, "CONSTANT")
shut.close(60)

# Breathe (3.6 s, loop, 0.28 a second): the lid lifting a hair (a degree and a half) and settling, slowly; the box
# swelling with it, by less than anyone would swear to.
breathe = Clip("breathe")
for f, k in ((0, 0.0), (40, 1.0), (60, 0.85), (108, 0.0)):
    p = REST_TONGUE | {"lid": (1.5 * k, 0, 0), "box@scale": (1 + 0.006 * k, 1 + 0.006 * k, 1 + 0.004 * k)}
    breathe.key(f, p, "BEZIER")
breathe.close(108)

# Lid (1.8 s, once): it creaks up a crack, holds, and up again; the tongue's tip comes forward and over the front rim.
lid_clip = Clip("lid", loop=False)
lid_clip.key(0, REST_TONGUE | {"lid": (0, 0, 0)}, "LINEAR")
lid_clip.key(8, REST_TONGUE | {"lid": (5, 0, 0)}, "CONSTANT")
lid_clip.key(26, REST_TONGUE | {"lid": (7, 0, 0)}, "LINEAR")
lid_clip.key(36, tongue_pose(lift=(4, 6, 8, 10, 6, -10), out=0.04) | {"lid": (24, 0, 0)}, "CONSTANT")
lid_clip.key(46, tongue_pose(lift=(6, 8, 10, 14, 10, -24), out=0.08) | {"lid": (26, 0, 0)}, "LINEAR")
lid_clip.key(54, tongue_pose(lift=(6, 8, 10, 16, 18, -40), out=0.1) | {"lid": (30, 0, 0)}, "CONSTANT")

# Chew (1 s, loop): flung wide, slammed shut on what it's got, and wide again; the tongue out over the front and up,
# wrapped round its catch (CreatureArt sets it at their feet, tipped up at them); the whole chest rocking and heaving.
# (Up out of the throat, over the front rim, then up its catch: the tip round their thighs, half a metre out.)
CHEW_LIFT, CHEW_OUT, CHEW_UP = (20, -10, -70, 110, 10, 20), 0.45, 0.3
chew = Clip("chew")
for f, (open_, rock) in ((0, (95, 0.0)), (6, (100, 1.0)), (10, (34, 0.4)), (12, (28, -0.6)), (20, (70, -1.0)), (24, (96, 0.2))):
    p = tongue_pose(lift=CHEW_LIFT, swing=(0, 4 * rock, 6 * rock, -8 * rock, -12 * rock, 18 * rock), out=CHEW_OUT, up=CHEW_UP) | {"lid": (open_, 0, 0)}
    p["root"] = (6 + 4 * rock, 0, 3 * rock)
    p["box@scale"] = (1 + 0.02 * abs(rock), 1 + 0.02 * abs(rock), 1 - 0.015 * abs(rock))
    chew.key(f, p, "LINEAR" if f in (6, 20) else "CONSTANT")
chew.close(30)

# Swallow (1.2 s, loop): shut on it, gulping, the box heaving and the lid lifting a little with each swallow.
swallow = Clip("swallow")
for f, k in ((0, 0.0), (8, 1.0), (14, 0.2), (24, 0.8), (36, 0.0)):
    swallow.key(f, REST_TONGUE | {"lid": (3 * k, 0, 0), "box@scale": (1 + 0.03 * k, 1 + 0.03 * k, 1 - 0.02 * k), "root": (-3 * k, 0, 0)}, "BEZIER")
swallow.close(36)

hit = Clip("hit", loop=False)
hit.key(0, REST_TONGUE | {"box@scale": (1, 1, 1)}, "CONSTANT")
hit.key(2, REST_TONGUE | {"lid": (12, 0, 0), "root": (6, 0, 4), "box@scale": (0.97, 0.97, 1.03)}, "CONSTANT")
hit.key(8, REST_TONGUE | {"lid": (2, 0, 0), "root": (-2, 0, -1), "box@scale": (1.01, 1.01, 0.99)}, "LINEAR")
hit.key(12, REST_TONGUE | {"box@scale": (1, 1, 1)}, "CONSTANT")

kit.build()
rig.bake(sk, [shut, breathe, lid_clip, chew, swallow, hit])
print("[dt] mimic", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "mimic.glb", kit)
