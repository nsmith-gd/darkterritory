"""Concepts for the Wakers' model (queue #411, ARCHITECTURE §8 note 679; docs/design/creatures/wakers.md §8.3: "It is
gargantuan: a car is a mouthful. It is long-limbed enough to run down a train, and earth-coloured, so it reads as the land
getting up"). Not a recipe: nothing here goes into the game. Each concept is a blockout (one voxel-joined skin over its
limbs, its parts in their own materials), staged on a stretch of line with the train at its real size, and rendered in the
five moments the design turns on:

    lie      before dawn, lying in the land: it should pass for one of the hills (wakers.md §2, "the hills move")
    risen    up on the line behind the train, against the cold dawn line, from the rear car: the silhouette that has to read
    run      down the line after the train, from the side
    catch    the train held: WakerLift's ramp (each lifted car's rear end 6 m over its front's) into its grip and mouth
    portrait close enough to see what it is

    blender -b --factory-startup --python tools/models/concepts/wakers.py -- [concept ...] [--shots lie,risen] [--size 960x540]
    -> out/notes/wakers/<concept>-<shot>.png (docs/design/creatures/wakers-model.md shows them)

Concepts: drumlin (a hill on stilts), peatman (a bog body crawling on its long arms), starnose (a star-nosed mole the
size of a hill, swimming the topsoil), snowcrab (a snow crab come up out of the bog on legs fifty metres across).
Axes: Blender +Z up; the train runs toward +Y, the Waker comes from -Y.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Euler, Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
OUT = os.path.join(ROOT, "out", "notes", "wakers")

# The train as the game has it (content/tuning/train.json): cars 14.5 m over their ends, 1 m between, floors 1.1 m up.
CAR_L, CAR_GAP, CAR_W, CAR_H, FLOOR = 14.5, 1.0, 3.0, 3.6, 1.1
CARS = 5
STEP = 6.0  # WakerLift.Step: how much higher each lifted car's rear end is than its front's


def args():
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    concepts, shots, size = [], ["lie", "risen", "run", "catch", "portrait"], (960, 540)
    i = 0
    while i < len(a):
        if a[i] == "--shots":
            shots = a[i + 1].split(",")
            i += 2
        elif a[i] == "--size":
            w, h = a[i + 1].split("x")
            size = (int(w), int(h))
            i += 2
        else:
            concepts.append(a[i])
            i += 1
    return concepts or list(CONCEPTS), shots, size


# ----------------------------------------------------------------------------------------------------------------
# Building blocks

def mat(name, colour, rough=0.8, bump=0.3, scale=0.6, sss=0.0, metal=0.0, emit=None, mottle=0.35, colour2=None):
    """A procedural material: a colour mottled toward `colour2` (darker by default) by noise, bumped."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    tc = nt.nodes.new("ShaderNodeTexCoord")
    noise = nt.nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = scale
    noise.inputs["Detail"].default_value = 8
    nt.links.new(tc.outputs["Object"], noise.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    c2 = colour2 or tuple(c * (1 - mottle) for c in colour)
    ramp.color_ramp.elements[0].color = (*c2, 1)
    ramp.color_ramp.elements[1].color = (*colour, 1)
    nt.links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bsdf.inputs["Base Color"])
    if sss:
        bsdf.inputs["Subsurface Weight"].default_value = sss
        bsdf.inputs["Subsurface Radius"].default_value = (1.0, 0.4, 0.3)
    b = nt.nodes.new("ShaderNodeBump")
    b.inputs["Strength"].default_value = bump
    nt.links.new(noise.outputs["Fac"], b.inputs["Height"])
    nt.links.new(b.outputs["Normal"], bsdf.inputs["Normal"])
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*emit[0], 1)
        bsdf.inputs["Emission Strength"].default_value = emit[1]
    return m


def ellipsoid(bm, c, r, rot=(0, 0, 0), seg=28, rings=18):
    m = Matrix.Translation(Vector(c)) @ Euler(rot).to_matrix().to_4x4() @ Matrix.Diagonal((*r, 1))
    bmesh.ops.create_uvsphere(bm, u_segments=seg, v_segments=rings, radius=1.0, matrix=m)


def _frame(t):
    ref = Vector((0, 0, 1)) if abs(t.z) < 0.9 else Vector((1, 0, 0))
    u = (ref - t * t.dot(ref)).normalized()
    return u, t.cross(u)


def tube(bm, pts, radii, n=14, joints=True):
    """A limb through `pts`, `radii` at each, capped, a ball at every joint so the voxel union rounds it."""
    pts = [Vector(p) for p in pts]
    rings = []
    for i, p in enumerate(pts):
        t = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        u, v = _frame(t)
        r = radii[i]
        rings.append([bm.verts.new(p + (u * math.cos(2 * math.pi * j / n) + v * math.sin(2 * math.pi * j / n)) * r) for j in range(n)])
    for a, b in zip(rings, rings[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])
    if joints:
        for p, r in zip(pts, radii):
            ellipsoid(bm, p, (r, r, r), seg=12, rings=8)


def cone(bm, a, b, r0, r1=0.0, n=10):
    a, b = Vector(a), Vector(b)
    m = Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(b - a).to_matrix().to_4x4()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=n, radius1=r0, radius2=max(r1, 0.001), depth=(b - a).length, matrix=m)


def obj(bm, name, material, voxel=None, smooth=3, lumps=0.0, lump_scale=3.0, shade=True):
    """The bmesh as an object: voxel-joined (one skin) and relaxed if `voxel`, lumped by a noise displacement."""
    me = bpy.data.meshes.new(name)
    bm.normal_update()
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(material)
    bpy.context.view_layer.objects.active = o
    if voxel:
        me.remesh_voxel_size = voxel
        me.remesh_voxel_adaptivity = 0
        bpy.ops.object.select_all(action="DESELECT")
        o.select_set(True)
        bpy.ops.object.voxel_remesh()
        if smooth:
            m = o.modifiers.new("relax", "SMOOTH")
            m.factor, m.iterations = 0.6, smooth
    if lumps:
        tex = bpy.data.textures.new(name + "_lumps", "CLOUDS")
        tex.noise_scale = lump_scale
        tex.noise_depth = 2
        m = o.modifiers.new("lumps", "DISPLACE")
        m.texture, m.strength, m.mid_level = tex, lumps, 0.5
        m.texture_coords = "GLOBAL"
    if shade:
        for f in me.polygons:
            f.use_smooth = True
    return o


# ----------------------------------------------------------------------------------------------------------------
# The land, the line, the train

HILLS = [(-70, -380, 9, 26, 55, 0.3), (85, -520, 11, 30, 70, -0.2), (-150, -650, 13, 40, 90, 0.4), (160, -300, 7, 22, 40, 0.1),
         (-60, -900, 14, 45, 100, 0.2), (40, -1100, 16, 60, 120, -0.3), (-210, -260, 8, 28, 50, 0.6)]


def ground_height(x, y):
    """Drumlins (the line's land: maritime-rules.md, elongated along the ice's flow) and a low swell, flat near the line."""
    h = 0.6 * np.sin(x * 0.013 + 1.3) * np.cos(y * 0.009) + 0.4 * np.sin(y * 0.021 + x * 0.004)
    for hx, hy, hh, rx, ry, ang in HILLS:
        c, s = math.cos(ang), math.sin(ang)
        u = ((x - hx) * c - (y - hy) * s) / rx
        v = ((x - hx) * s + (y - hy) * c) / ry
        d = u * u + v * v
        h = h + hh * np.exp(-d * 1.6)
    near = np.clip((np.abs(x) - 6) / 30, 0, 1)
    return h * near - 0.15


def land():
    n = 260
    xs = np.linspace(-700, 700, n)
    ys = np.linspace(-1600, 400, n)
    X, Y = np.meshgrid(xs, ys)
    Z = ground_height(X, Y)
    bm = bmesh.new()
    vs = [[bm.verts.new((X[j, i], Y[j, i], Z[j, i])) for i in range(n)] for j in range(n)]
    for j in range(n - 1):
        for i in range(n - 1):
            bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
    obj(bm, "land", mat("land", (0.03, 0.034, 0.028), rough=0.95, bump=0.5, scale=0.08, colour2=(0.015, 0.018, 0.013)))
    # Ballast and rails.
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation((0, -600, 0.15)) @ Matrix.Diagonal((4.4, 2000, 0.5, 1)))
    obj(bm, "ballast", mat("ballast", (0.12, 0.115, 0.11), rough=0.9, scale=2.0), shade=False)
    bm = bmesh.new()
    for x in (-0.72, 0.72):
        bmesh.ops.create_cube(bm, size=1, matrix=Matrix.Translation((x, -600, 0.48)) @ Matrix.Diagonal((0.08, 2000, 0.16, 1)))
    obj(bm, "rails", mat("rail", (0.35, 0.33, 0.3), rough=0.35, metal=0.8), shade=False)
    # Spruce along the line, a dark wall broken by the hills.
    rng = np.random.default_rng(7)
    bm = bmesh.new()
    for _ in range(900):
        x = rng.choice([-1, 1]) * rng.uniform(25, 420)
        y = rng.uniform(-1500, 300)
        # (Clear of every shot's camera, so no trunk stands in front of the lens.)
        if any((x - cx) ** 2 + (y - cy) ** 2 < 70 ** 2 for cx, cy in ((150, -30), (95, -10), (62, 62))):
            continue
        z = float(ground_height(np.array(x), np.array(y)))
        h = rng.uniform(8, 18)
        cone(bm, (x, y, z), (x, y, z + h), h * 0.18, 0.0, n=6)
    obj(bm, "spruce", mat("spruce", (0.02, 0.03, 0.025), rough=0.95, scale=1.0), shade=False)


def train(lift=None, rear_y=0.0):
    """The engine and CARS cars, the rear car's rear end at `rear_y`. `lift`, one 0..1 per car from the rear: how far up
    each has gone, WakerLift's way (each rear end the sum of its own and every car's in front of it, STEP a car)."""
    lift = lift or [0] * CARS
    bm_body, bm_dark, bm_lamp = bmesh.new(), bmesh.new(), bmesh.new()

    def height(k):
        return sum(lift[m] * STEP for m in range(k, CARS))
    for k in range(CARS):
        y_rear = rear_y + k * (CAR_L + CAR_GAP)
        y_front = y_rear + CAR_L
        zr, zf = FLOOR + height(k), FLOOR + height(k + 1)
        rear = Vector((0, y_rear, zr))
        front = Vector((0, y_front, zf))
        axis = (front - rear).normalized()
        up = Vector((0, 0, 1))
        up = (up - axis * up.dot(axis)).normalized()
        centre = (rear + front) / 2 + up * (CAR_H / 2)
        bmesh_cube_oriented(bm_body, centre, axis, up, (CAR_W, CAR_H, CAR_L))
        bmesh_cube_oriented(bm_dark, (rear + front) / 2 - up * 0.45, axis, up, (2.2, 0.7, CAR_L - 3))
        # A lamp in each car's end window: someone's in there.
        bmesh_cube_oriented(bm_lamp, centre + axis * (CAR_L / 2 - 0.02) + up * 0.6, axis, up, (0.6, 0.5, 0.05))
    # The engine.
    y0 = rear_y + CARS * (CAR_L + CAR_GAP)
    bmesh_cube_oriented(bm_dark, Vector((0, y0 + 9, FLOOR + 2.2)), Vector((0, 1, 0)), Vector((0, 0, 1)), (3.0, 4.4, 18))
    cyl = bm_body
    bmesh.ops.create_cone(cyl, cap_ends=True, segments=16, radius1=1.4, radius2=1.4, depth=12,
                          matrix=Matrix.Translation((0, y0 + 11, FLOOR + 3.4)) @ Matrix.Rotation(math.pi / 2, 4, "X"))
    bmesh_cube_oriented(bm_lamp, Vector((0, y0 + 17.6, FLOOR + 3.6)), Vector((0, 1, 0)), Vector((0, 0, 1)), (0.7, 0.7, 0.1))
    obj(bm_body, "cars", mat("car", (0.16, 0.08, 0.05), rough=0.7, scale=3.0), shade=False)
    obj(bm_dark, "gear", mat("gear", (0.03, 0.03, 0.03), rough=0.6), shade=False)
    obj(bm_lamp, "lamps", mat("lamp", (1, 0.7, 0.35), emit=((1.0, 0.62, 0.3), 30)), shade=False)
    return Vector((0, rear_y, FLOOR + height(0)))


def bmesh_cube_oriented(bm, centre, axis, up, size):
    side = axis.cross(up).normalized()
    up = side.cross(axis).normalized()
    rot = Matrix((side, axis, up)).transposed().to_4x4()
    m = Matrix.Translation(centre) @ rot @ Matrix.Diagonal((size[0], size[2], size[1], 1))
    bmesh.ops.create_cube(bm, size=1, matrix=m)


# ----------------------------------------------------------------------------------------------------------------
# The concepts. Each builds itself at the origin facing +Y (toward the train), its feet on z = 0, in a pose; `grip` is
# where the held car's rear end is, relative to it (catch only). Returns what it built.

def surface_points(c, r, n, rng, top=0.35):
    """Points over the top of an ellipsoid (where trees stand on a back, boulders sit)."""
    out = []
    while len(out) < n:
        x, y = rng.uniform(-1, 1), rng.uniform(-1, 1)
        if x * x + y * y > 0.85:
            continue
        z = math.sqrt(max(0.0, 1 - x * x - y * y))
        if z < top:
            continue
        out.append(Vector((c[0] + x * r[0], c[1] + y * r[1], c[2] + z * r[2])))
    return out


def drumlin(pose, grip=None):
    """THE DRUMLIN: a hill on stilts. Its back is a drumlin, turf and spruce and granite erratics, grown on it through
    decades of lying still; under it, four long thin legs that kneel up above its back like a harvestman's, a bone-pale
    head slung low beneath the front, its jaw opening downward like a dredge's bucket, and two hooked arms (a sloth's)
    either side of it that take the car."""
    rng = np.random.default_rng(11)
    skin, bone = bmesh.new(), bmesh.new()
    lying = pose == "lie"
    H = 1.0 if lying else 22.0                      # the body's middle
    lean = 0.0
    if pose == "catch":
        H, lean = 20.0, 0.1
    body_c, body_r = (0, 0, H), (11, 18, 6.5)
    ellipsoid(skin, body_c, body_r, rot=(lean, 0, 0))
    for c, r in (((3, -6, H + 4), (6, 8, 4)), ((-4, 7, H + 3.5), (5, 7, 3.5)), ((1, 12, H + 1), (6, 6, 4))):
        ellipsoid(skin, c, r, rot=(lean, 0, 0))
    if not lying:
        # The legs: hip under the back's edge, knee kneeling up over it, foot far out.
        stride = {"run": 9.0}.get(pose, 0.0)
        for i, (sx, sy) in enumerate(((-1, -1), (1, -1), (-1, 1), (1, 1))):
            phase = 1 if (i in (0, 3)) else -1
            fy = sy * 15 + phase * stride
            hip = Vector((sx * 8, sy * 11, H - 3))
            knee = Vector((sx * 19, sy * 13 + phase * stride * 0.5, H + 9))
            foot = Vector((sx * 17, fy, 0.6 if not (pose == "run" and phase > 0) else 4.0))
            tube(skin, [hip, knee, foot], [1.6, 1.0, 0.55])
            cone(skin, foot, foot + Vector((0, 1.5, -1.2)), 0.5, 0.05, n=8)
        # Its neck down from under the front; the head, the jaw; the two arms.
        neck0 = Vector((0, 15, H - 3))
        if pose == "catch":
            head = Vector(grip) + Vector((0, -3.0, 2.0))
            hands = [Vector(grip) + Vector((sx * 2.2, 2.0, -1.0)) for sx in (-1, 1)]
            jaw_open = 0.9
        else:
            head = Vector((0, 27, H - 9 if pose != "run" else H - 7))
            hands = [Vector((sx * 5, 30, H - 15)) for sx in (-1, 1)]
            jaw_open = 0.35
        tube(skin, [neck0, (neck0 + head) / 2 + Vector((0, 0, 2)), head - Vector((0, 3, 0))], [3.2, 2.4, 2.0])
        ellipsoid(bone, head, (2.6, 6.0, 2.4), rot=(-0.35, 0, 0))
        ellipsoid(bone, head + Vector((0, 1.0, -2.0 - jaw_open * 2.5)), (2.3, 5.2, 1.0), rot=(-0.35 + jaw_open * 0.5, 0, 0))
        for sx in (-1, 1):
            cone(bone, head + Vector((sx * 1.6, 2.5, -1.2)), head + Vector((sx * 1.4, 4.2, -2.6)), 0.35, 0.05)
        for sx, hand in zip((-1, 1), hands):
            sh = Vector((sx * 6, 14, H - 4))
            el = (sh + hand) / 2 + Vector((sx * 5, -2, 3))
            tube(skin, [sh, el, hand], [1.4, 0.9, 0.6])
            for k in range(3):
                d = Vector((sx * (k - 1) * 0.6, 1.0, -0.4)).normalized()
                cone(bone, hand, hand + d * 3.2 + Vector((0, 0, -1.5)), 0.32, 0.03, n=8)
    built = [obj(skin, "drumlin", mat("turf", (0.07, 0.065, 0.045), rough=0.95, bump=0.8, scale=0.15, colour2=(0.03, 0.04, 0.025)),
                 voxel=0.45, smooth=4, lumps=0.9, lump_scale=4.0)]
    if not lying:
        built.append(obj(bone, "drumlin_bone", mat("bone", (0.2, 0.17, 0.13), rough=0.6, bump=0.5, scale=1.5, colour2=(0.1, 0.08, 0.06)), voxel=0.18, smooth=2,
                         lumps=0.15, lump_scale=1.0))
    # Its back: dead spruce and erratics, which a hill has and a beast doesn't.
    trees, rocks = bmesh.new(), bmesh.new()
    for p in surface_points(body_c, (body_r[0] * 0.9, body_r[1] * 0.9, body_r[2] + 1.5), 26, rng):
        h = rng.uniform(5, 11)
        cone(trees, p - Vector((0, 0, 1)), p + Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), h)), h * 0.14, 0.0, n=6)
    for p in surface_points(body_c, (body_r[0] * 0.8, body_r[1] * 0.8, body_r[2] + 1.0), 9, rng):
        s = rng.uniform(1.2, 2.8)
        ellipsoid(rocks, p, (s, s * 1.3, s * 0.8), rot=tuple(rng.uniform(0, 3, 3)), seg=8, rings=6)
    built.append(obj(trees, "drumlin_trees", mat("deadspruce", (0.03, 0.028, 0.025), rough=0.95), shade=False))
    built.append(obj(rocks, "drumlin_rocks", mat("granite", (0.32, 0.31, 0.29), rough=0.85, bump=0.6, scale=2.0)))
    return built


def peatman(pose, grip=None):
    """THE PEAT MAN: a bog body. A man that lay in the peat until it tanned him to leather and the Corruption grew him to
    the size of a hill: shrunk over long bones, the ribs and spine standing out, the rope still round his neck, the hair
    gone red with the bog. He runs bent double on his long arms like an ape, and picks up the train in both hands like a
    child with a toy one, to his mouth."""
    skin, rope, eyes = bmesh.new(), bmesh.new(), bmesh.new()
    if pose == "lie":
        # Face down in the land, half sunk: a long barrow with a spine down it and the back of a head at one end.
        ellipsoid(skin, (0, -4, 0.5), (8, 16, 5))
        ellipsoid(skin, (0, 15, 0.8), (4.5, 5.5, 4.5))
        for k in range(12):
            ellipsoid(skin, (0, -16 + k * 2.4, 5.2 - abs(k - 6) * 0.15), (1.0, 1.0, 0.8), seg=10, rings=6)
        for sx in (-1, 1):
            tube(skin, [(sx * 9, 8, 0.5), (sx * 11, -4, 1.0), (sx * 10, -16, 0.2)], [2.2, 1.8, 1.6])
        built = [obj(skin, "peatman", mat("peat", (0.11, 0.06, 0.035), rough=0.55, bump=0.6, scale=0.4, colour2=(0.05, 0.025, 0.015)),
                     voxel=0.35, smooth=3, lumps=0.4, lump_scale=2.5)]
        return built
    if pose == "catch":
        pelvis, chest, head = Vector((0, -10, 9)), Vector((0, -6, 20)), Vector(grip) + Vector((0, -2.5, 2.5))
        hands = [Vector(grip) + Vector((sx * 2.4, 1.5, -1.5)) for sx in (-1, 1)]
        elbows = [Vector((sx * 10, -2, 14)) for sx in (-1, 1)]
        knees = [Vector((sx * 9, 4, 10)) for sx in (-1, 1)]
        feet = [Vector((sx * 7, 2, 0.5)) for sx in (-1, 1)]
        mouth = 1.0
    else:
        reach = 10.0 if pose == "run" else 0.0
        pelvis, chest, head = Vector((0, -9, 17)), Vector((0, 6, 23)), Vector((0, 16, 21))
        hands = [Vector((sx * 9, 22 + reach * (1 if sx < 0 else 0.3), 0.5)) for sx in (-1, 1)]
        elbows = [Vector((sx * 12, 13 + reach * 0.4, 12)) for sx in (-1, 1)]
        knees = [Vector((sx * 10, -2 - reach * 0.3 * (1 if sx > 0 else -1), 11)) for sx in (-1, 1)]
        feet = [Vector((sx * 7, -14 - (reach if sx > 0 else -reach * 0.3), 0.5)) for sx in (-1, 1)]
        mouth = 0.4
    tube(skin, [pelvis, (pelvis + chest) / 2 + Vector((0, 0, 1.5)), chest], [6.0, 5.5, 7.0], n=18)
    ellipsoid(skin, chest, (8.0, 6.5, 6.0), rot=(0.3, 0, 0))
    # Ribs and spine standing out of the leather.
    axis = (chest - pelvis).normalized()
    for k in range(7):
        p = pelvis.lerp(chest, 0.35 + k * 0.1)
        for sx in (-1, 1):
            ellipsoid(skin, p + Vector((sx * 6.8, 0, 0.5)), (0.6, 0.6, 2.4), rot=(0, sx * 0.4, 0), seg=10, rings=6)
        ellipsoid(skin, p + axis.cross(Vector((1, 0, 0))) * -5.8 + Vector((0, 0, 0.5)), (0.9, 0.9, 0.9), seg=10, rings=6)
    neck = chest.lerp(head, 0.55)
    tube(skin, [chest + Vector((0, 2, 2)), neck, head], [3.0, 2.2, 2.0])
    # A man's skull under the leather: the brow, the nose, the cheekbones; the jaw hanging open under it, too far; the
    # eyes sunk to pits of bog water.
    ellipsoid(skin, head, (3.4, 3.8, 4.4), rot=(0.15, 0, 0))
    ellipsoid(skin, head + Vector((0, 3.0, 1.4)), (3.1, 1.2, 0.9))                                  # the brow
    ellipsoid(skin, head + Vector((0, 3.9, -0.6)), (0.8, 0.9, 1.5), rot=(0.3, 0, 0))                # the nose
    for sx in (-1, 1):
        ellipsoid(skin, head + Vector((sx * 2.2, 2.8, -0.8)), (1.0, 1.0, 0.8))                       # cheekbones
    ellipsoid(skin, head + Vector((0, 1.6, -3.4 - mouth * 1.6)), (2.6, 2.6, 1.2 + mouth * 0.3))     # the jaw, hanging
    for sx in (-1, 1):
        ellipsoid(eyes, head + Vector((sx * 1.3, 3.3, 0.4)), (0.75, 0.5, 0.65), seg=12, rings=8)
    tube(rope, [neck + Vector((math.cos(a) * 2.6, math.sin(a) * 2.6, 0)) for a in np.linspace(0, 2 * math.pi, 13)],
         [0.35] * 13, n=8, joints=False)
    tube(rope, [neck + Vector((0, -2.6, 0)), neck + Vector((0.5, -4, -4)), neck + Vector((1.5, -3.5, -8))], [0.35, 0.3, 0.25], n=8)
    for sx, el, hand, kn, ft in zip((-1, 1), elbows, hands, knees, feet):
        sh = chest + Vector((sx * 7.5, 1, 2))
        tube(skin, [sh, el, hand], [2.6, 1.8, 1.4])
        for f in range(4):
            d = Vector(((f - 1.5) * 0.45 * sx, 1.0, -0.6)).normalized()
            tube(skin, [hand, hand + d * 3.0, hand + d * 5.2 + Vector((0, 0, -1.2))], [0.55, 0.45, 0.3], n=8)
        hip = pelvis + Vector((sx * 4.5, 0, -1))
        tube(skin, [hip, kn, ft], [3.0, 2.0, 1.6])
        ellipsoid(skin, ft + Vector((0, 2, -0.2)), (1.6, 3.5, 0.9))
    return [obj(skin, "peatman", mat("peat", (0.11, 0.06, 0.035), rough=0.55, bump=0.6, scale=0.4, colour2=(0.05, 0.025, 0.015)),
                voxel=0.3, smooth=3, lumps=0.35, lump_scale=2.5),
            obj(rope, "peatman_rope", mat("rope", (0.18, 0.13, 0.08), rough=0.9, scale=4.0)),
            obj(eyes, "peatman_eyes", mat("bogwater", (0.01, 0.012, 0.01), rough=0.05))]


def starnose(pose, grip=None):
    """THE STAR-NOSE: a star-nosed mole (the Maritimes' own, a bog animal) grown to the size of a hill. Blind, its velvet
    caked with mud; it lies under a molehill a hundred metres round; it doesn't run so much as swim the topsoil on its
    spade hands, throwing the land up in a bow wave; and the twenty-two pink rays of its nose close round the last car
    like a hand round a worm."""
    fur, star, claws, earth = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    rng = np.random.default_rng(5)
    if pose == "lie":
        # The molehill, the size of a hill: a heap of turned earth, crumbs and clods round it, the star's tips just out.
        ellipsoid(earth, (0, 0, -2), (26, 30, 13))
        for _ in range(60):
            a, d = rng.uniform(0, 2 * math.pi), rng.uniform(22, 38)
            s = rng.uniform(1, 3)
            ellipsoid(earth, (math.cos(a) * d, math.sin(a) * d * 1.1, 0), (s, s, s * 0.6), seg=8, rings=6)
        for k in range(8):
            a = 2 * math.pi * k / 8
            base = Vector((math.cos(a) * 1.5, 18, 9 + math.sin(a) * 1.5))
            cone(star, base, base + Vector((math.cos(a) * 1.5, 2.5, math.sin(a) * 1.5 + 0.5)), 0.5, 0.15)
        return [obj(earth, "molehill", mat("tilth", (0.08, 0.06, 0.04), rough=0.95, bump=1.0, scale=0.5), voxel=0.5, smooth=2,
                    lumps=1.2, lump_scale=3.0),
                obj(star, "star", mat("star", (0.55, 0.3, 0.3), rough=0.45, sss=0.3, bump=0.2, scale=2.0), voxel=0.12, smooth=2)]
    sink = {"run": 5.0}.get(pose, 0.0)
    pitch = 0.55 if pose == "catch" else -0.04
    rot = Matrix.Rotation(pitch, 4, "X")

    def P(x, y, z):
        return rot @ Vector((x, y, z)) + Vector((0, 0, 9 - sink))
    if pose == "catch":
        # Reared up on its spades, the star round the car: put the snout at the grip.
        snout0 = P(0, 22, 0)
        shift = Vector(grip) - snout0 + Vector((0, -1.0, 0))
    else:
        shift = Vector((0, 0, 0))

    def Q(x, y, z):
        return P(x, y, z) + shift
    ellipsoid(fur, Q(0, -2, 0), (10, 17, 8.5), rot=(pitch, 0, 0))
    ellipsoid(fur, Q(0, 13, -0.5), (6.5, 7, 5.5), rot=(pitch, 0, 0))
    tube(fur, [Q(0, 16, -1), Q(0, 20, -1.2), Q(0, 22, -1.2)], [4.5, 2.8, 2.2])
    tube(fur, [Q(0, -17, -3), Q(0, -24, -5), Q(0, -29, -6)], [1.6, 1.1, 0.6])             # the tail
    # The star: 22 rays round the snout, spread wide, curling in when it holds.
    tip = Q(0, 22.3, -1.2)
    hold = pose == "catch"
    for k in range(22):
        a = 2 * math.pi * (k + 0.5) / 22
        length = 9.5 if k not in (5, 16) else 6.0
        spread = 0.55 if hold else 1.0
        d = (rot @ Vector((math.cos(a) * spread, 0.9 if hold else 0.55, math.sin(a) * spread))).normalized()
        mid = tip + d * length * 0.55
        end = tip + d * length + (rot @ Vector((0, 1.5, 0)) if hold else Vector((0, 0, 0))) - (d * 1.5 if hold else Vector((0, 0, 0)))
        tube(star, [tip + d * 0.6, mid, end], [1.05, 0.8, 0.35], n=8)
    # The spades: the hands turned out, palms back, five claws each; short hind legs.
    for sx in (-1, 1):
        sh = Q(sx * 7.5, 9, -4)
        hand = Q(sx * 13.5, 13, -9.0 + (3 if pose == "run" and sx < 0 else 0)) if pose != "catch" else Q(sx * 12, 16, -14)
        if pose == "catch":
            hand = Vector((hand.x, hand.y, 1.5))
        tube(fur, [sh, (sh + hand) / 2, hand], [3.5, 3.0, 2.5])
        ellipsoid(fur, hand, (1.4, 4.0, 5.0), rot=(0.2, sx * 0.6, sx * 0.3))
        for c in range(5):
            base = hand + Vector((sx * 1.0, 2.0, (c - 2) * 1.8))
            cone(claws, base, base + Vector((sx * 1.2, 3.8, (c - 2) * 0.7 - 0.6)), 0.55, 0.05)
        hip = Q(sx * 6, -12, -5)
        ft = Q(sx * 8, -10, -9) if pose != "catch" else Vector((sx * 8, Q(0, -12, 0).y - 2, 0.6))
        tube(fur, [hip, ft], [2.6, 1.8])
    built = [obj(fur, "starnose", mat("velvet", (0.08, 0.07, 0.065), rough=0.9, bump=0.4, scale=0.6, colour2=(0.04, 0.032, 0.025)),
                 voxel=0.4, smooth=4, lumps=0.25, lump_scale=2.0),
             obj(star, "star", mat("star", (0.55, 0.3, 0.3), rough=0.45, sss=0.3, bump=0.2, scale=2.0), voxel=0.15, smooth=2),
             obj(claws, "claws", mat("claw", (0.5, 0.45, 0.36), rough=0.5, scale=3.0))]
    if pose == "run":
        # The bow wave: the land thrown up off its spades, clods flying.
        for _ in range(140):
            a = rng.uniform(-1.3, 1.3)
            d = rng.uniform(10, 22)
            p = Vector((math.sin(a) * d, 14 + math.cos(a) * d * 0.5 + rng.uniform(-4, 6), rng.uniform(0, 9)))
            s = rng.uniform(0.6, 2.2)
            ellipsoid(earth, p, (s, s, s * 0.8), rot=tuple(rng.uniform(0, 3, 3)), seg=8, rings=6)
        ellipsoid(earth, (0, -6, 0), (14, 28, 3))
        built.append(obj(earth, "wave", mat("tilth", (0.08, 0.06, 0.04), rough=0.95, bump=1.0, scale=0.5), voxel=0.4, smooth=1,
                         lumps=0.6, lump_scale=2.0))
    return built


def snowcrab(pose, grip=None):
    """THE SNOW CRAB: the Maritimes' crab (the fishery's snow crab, legs ten times its body) come up out of the bog: a
    carapace like a granite boulder crusted with barnacles and peat, eight walking legs fifty metres across that it holds
    high and fast, and two claws it takes the car in, to the mouthparts working under the front of its shell."""
    shell, legs, crust, eyes = bmesh.new(), bmesh.new(), bmesh.new(), bmesh.new()
    rng = np.random.default_rng(19)
    lying = pose == "lie"
    H = 3.0 if lying else 24.0
    tilt = 0.4 if pose == "catch" else 0.0
    if pose == "catch":
        H = 22.0
    rot = Matrix.Rotation(tilt, 4, "X")

    def P(x, y, z):
        return rot @ Vector((x, y, z)) + Vector((0, 0, H))
    ellipsoid(shell, P(0, 0, 0), (9.5, 9.0, 4.2), rot=(tilt, 0, 0))
    ellipsoid(shell, P(0, 5, 0.5), (7.0, 6.0, 3.6), rot=(tilt, 0, 0))
    for k in range(14):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(0.2, 0.85)
        p = P(math.cos(a) * 9 * r, math.sin(a) * 8.5 * r, 3.6 * math.sqrt(max(0.1, 1 - r * r)) + 0.3)
        cone(shell, p - Vector((0, 0, 0.6)), p + Vector((0, 0, 1.8)), 0.9, 0.1, n=8)   # its spines
    for p in surface_points((0, 0, H), (9.0, 8.5, 4.4), 40, rng, top=0.2):
        p = rot @ (p - Vector((0, 0, H))) + Vector((0, 0, H))
        s = rng.uniform(0.3, 0.8)
        cone(crust, p, p + Vector((0, 0, s * 1.3)), s, s * 0.45, n=7)
    # Eight walking legs: up from the shell's edge to a knee high over it, down to a point far out.
    for i, (sx, sy) in enumerate([(sx, sy) for sy in (-0.7, -0.15, 0.4, 0.85) for sx in (-1, 1)]):
        base = P(sx * 8.0, sy * 8.0, -0.5)
        if lying:
            knee = base + Vector((sx * 9, sy * 5, 4))
            tip = base + Vector((sx * 14, sy * 12, -2))
        else:
            gait = (1 if (i % 4) in (0, 3) else -1) * (7 if pose == "run" else 0)
            knee = base + Vector((sx * 13, sy * 6 + gait * 0.4, 8))
            tip = Vector((base.x + sx * 22, base.y + sy * 16 + gait, 0.3))
        tube(legs, [base, knee, (knee + tip) / 2 + Vector((sx * 2, 0, 1)), tip], [1.8, 1.3, 1.0, 0.35], n=10)
    # The claws (chelipeds) and the pincers; the eye stalks; the mouthparts.
    for sx in (-1, 1):
        base = P(sx * 5.5, 7.5, -1.0)
        if pose == "catch":
            claw = Vector(grip) + Vector((sx * 2.4, 1.0, -0.5))
            elbow = (base + claw) / 2 + Vector((sx * 6, -2, -2))
        elif lying:
            claw, elbow = base + Vector((sx * 2, 8, -3)), base + Vector((sx * 6, 3, -1))
        else:
            claw, elbow = base + Vector((sx * 3, 10, -9)), base + Vector((sx * 8, 4, -4))
        tube(legs, [base, elbow, claw], [1.6, 1.4, 1.6], n=12)
        ellipsoid(legs, claw, (1.8, 3.2, 2.0), rot=(0.3, 0, sx * 0.3))
        gap = 0.6 if pose == "catch" else 0.3
        for k, up in ((0, 1), (1, -1)):
            cone(legs, claw + Vector((0, 2.5, up * 0.8)), claw + Vector((-sx * 0.8, 7.0, up * (0.6 + gap * 2))), 0.9, 0.08, n=8)
        stalk = P(sx * 2.2, 9.0, 2.5)
        tube(legs, [P(sx * 1.8, 8.0, 1.5), stalk], [0.35, 0.3], n=8)
        ellipsoid(eyes, stalk, (0.55, 0.55, 0.55), seg=10, rings=6)
    for k in range(6):
        p = P((k - 2.5) * 0.9, 9.3, -1.6 - (k % 2) * 0.6)
        ellipsoid(legs, p, (0.45, 0.3, 1.4), seg=8, rings=6)
    built = [obj(shell, "snowcrab", mat("carapace", (0.2, 0.1, 0.06), rough=0.55, bump=0.7, scale=0.5, colour2=(0.08, 0.05, 0.035)),
                 voxel=0.3, smooth=2, lumps=0.4, lump_scale=2.0),
             obj(legs, "snowcrab_legs", mat("legs", (0.24, 0.12, 0.07), rough=0.5, bump=0.4, scale=1.0, colour2=(0.1, 0.06, 0.04)),
                 voxel=0.22, smooth=2, lumps=0.1, lump_scale=1.0),
             obj(crust, "barnacles", mat("barnacle", (0.5, 0.48, 0.42), rough=0.8, scale=3.0)),
             obj(eyes, "crab_eyes", mat("crab_eye", (0.01, 0.01, 0.01), rough=0.1))]
    if lying:
        # Lying in the bog: sunk to its shell's rim, mud over its legs, a granite outcrop with something wrong on it.
        mud = bmesh.new()
        ellipsoid(mud, (0, 0, 0), (26, 22, 3.5))
        built.append(obj(mud, "bogmud", mat("land", (0.05, 0.055, 0.045)), voxel=0.6, smooth=1, lumps=1.0, lump_scale=4))
    return built


CONCEPTS = {"drumlin": drumlin, "peatman": peatman, "starnose": starnose, "snowcrab": snowcrab}
# How far ahead of its origin each takes hold (m): the catch sets it that far behind the held car's rear end. (The
# Star-nose sets its own snout on the grip.)
REACH = {"drumlin": 26.0, "peatman": 3.0, "starnose": 22.0, "snowcrab": 14.0}


# ----------------------------------------------------------------------------------------------------------------
# Staging

def world_and_light(shot):
    w = bpy.data.worlds.new("dawn")
    bpy.context.scene.world = w
    w.use_nodes = True
    nt = w.node_tree
    bg = nt.nodes["Background"]
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    nt.links.new(tc.outputs["Generated"], sep.inputs["Vector"])
    # Height above the horizon (z), and how far toward the dawn (-Y, behind the train): the cold line low behind.
    up = nt.nodes.new("ShaderNodeMapRange")
    nt.links.new(sep.outputs["Z"], up.inputs["Value"])
    up.inputs["From Min"].default_value, up.inputs["From Max"].default_value = 0.0, 0.25
    toward = nt.nodes.new("ShaderNodeMapRange")
    nt.links.new(sep.outputs["Y"], toward.inputs["Value"])
    toward.inputs["From Min"].default_value, toward.inputs["From Max"].default_value = 0.2, -1.0
    band = nt.nodes.new("ShaderNodeMath")
    band.operation = "MULTIPLY"
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    nt.links.new(up.outputs["Result"], inv.inputs[1])
    pw = nt.nodes.new("ShaderNodeMath")
    pw.operation = "POWER"
    nt.links.new(inv.outputs["Value"], pw.inputs[0])
    pw.inputs[1].default_value = 6.0
    nt.links.new(pw.outputs["Value"], band.inputs[0])
    nt.links.new(toward.outputs["Result"], band.inputs[1])
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.006, 0.008, 0.012, 1)
    ramp.color_ramp.elements[1].color = (0.32, 0.36, 0.42, 1) if shot != "lie" else (0.07, 0.075, 0.09, 1)
    nt.links.new(band.outputs["Value"], ramp.inputs["Fac"])
    nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = 1.0
    # The low cold light from the dawn behind, and the moon over.
    sun = bpy.data.objects.new("dawnlight", bpy.data.lights.new("dawnlight", "SUN"))
    sun.data.energy = 0.0 if shot == "lie" else 1.2
    sun.data.color = (0.75, 0.82, 0.95)
    sun.rotation_euler = Euler((math.radians(86), 0, math.radians(0)))   # from -Y, low
    sun.data.angle = math.radians(4)
    bpy.context.scene.collection.objects.link(sun)
    moon = bpy.data.objects.new("moon", bpy.data.lights.new("moon", "SUN"))
    moon.data.energy = {"portrait": 0.9, "lie": 0.7}.get(shot, 0.25)
    moon.data.color = (0.55, 0.62, 0.8)
    moon.rotation_euler = Euler((math.radians(40), 0, math.radians(150)))
    bpy.context.scene.collection.objects.link(moon)


def camera(at, look, lens):
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    bpy.context.scene.collection.objects.link(cam)
    cam.location = Vector(at)
    cam.rotation_euler = (Vector(look) - Vector(at)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    cam.data.clip_end = 5000
    bpy.context.scene.camera = cam


def compositing(fog=(0.05, 0.058, 0.07), start=40, depth=900):
    """Distance fog on what's rendered (the mist pass), not on the sky: the film is transparent, the sky comes back from
    the environment pass under it, so the dawn's cold line stays where it is."""
    sc = bpy.context.scene
    sc.render.film_transparent = True
    vl = sc.view_layers[0]
    vl.use_pass_mist = True
    vl.use_pass_environment = True
    sc.world.mist_settings.start, sc.world.mist_settings.depth = start, depth
    sc.world.mist_settings.falloff = "QUADRATIC"
    sc.use_nodes = True
    nt = sc.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    rl = nt.nodes.new("CompositorNodeRLayers")
    mix = nt.nodes.new("CompositorNodeMixRGB")
    mix.inputs[2].default_value = (*fog, 1)
    mul = nt.nodes.new("CompositorNodeMath")
    mul.operation = "MULTIPLY"
    mul.inputs[1].default_value = 0.9
    nt.links.new(rl.outputs["Mist"], mul.inputs[0])
    nt.links.new(mul.outputs["Value"], mix.inputs[0])
    nt.links.new(rl.outputs["Image"], mix.inputs[1])
    keep = nt.nodes.new("CompositorNodeSetAlpha")
    nt.links.new(mix.outputs["Image"], keep.inputs["Image"])
    nt.links.new(rl.outputs["Alpha"], keep.inputs["Alpha"])
    over = nt.nodes.new("CompositorNodeAlphaOver")
    nt.links.new(rl.outputs["Env"], over.inputs[1])
    nt.links.new(keep.outputs["Image"], over.inputs[2])
    comp = nt.nodes.new("CompositorNodeComposite")
    nt.links.new(over.outputs["Image"], comp.inputs["Image"])


def place(objs, at, yaw=0.0):
    m = Matrix.Translation(Vector(at)) @ Matrix.Rotation(yaw, 4, "Z")
    for o in objs:
        o.matrix_world = m @ o.matrix_world


def stage(name, shot, size):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    # (No denoiser in the distro's Blender: more samples, clamped, and a light-path budget for a night scene.)
    sc.cycles.samples = 96 if shot == "portrait" else 64
    sc.cycles.use_denoising = False
    sc.cycles.sample_clamp_indirect = 2.0
    sc.cycles.max_bounces = 4
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in sc.view_settings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
    sc.view_settings.look = "None"
    sc.view_settings.exposure = 0.6 if shot != "portrait" else 0.3
    world_and_light(shot)
    land()
    fn = CONCEPTS[name]
    # The train stands with its rear car's rear end at y = 0 (catch: lifted, the Waker behind it at the rear end).
    if shot == "catch":
        grip = train(lift=[1, 1, 1, 0.55, 0], rear_y=0.0)
        back = REACH[name]
        objs = fn("catch", grip=tuple(grip + Vector((0, back, 0))))     # (relative to it: set `back` behind the rear end)
        place(objs, (0, -back, 0))
        camera((95, -10, 14), (0, 8, 14), 32)
        compositing(start=80, depth=1400)
    elif shot == "run":
        train(rear_y=0.0)
        objs = fn("run")
        place(objs, (0, -55, 0))
        camera((150, -30, 9), (0, -18, 16), 30)
        compositing(start=90, depth=1400)
    elif shot == "risen":
        train(rear_y=0.0)
        objs = fn("stand")
        place(objs, (0, -330, 0))
        camera((0, -2.0, 6.0), (0, -330, 16), 45)
        compositing(start=60, depth=900)
    elif shot == "lie":
        train(rear_y=0.0)
        objs = fn("lie")
        # Off the line to the left, 160 m back, among the drumlins: close enough to see, still passing for a hill.
        place(objs, (-48, -160, float(ground_height(np.array(-48.0), np.array(-160.0))) * 0.5), yaw=0.35)
        camera((2, -2.0, 6.0), (-42, -160, 5), 40)
        compositing(fog=(0.025, 0.03, 0.038), start=60, depth=1200)
    else:  # portrait
        train(rear_y=40.0)
        objs = fn("stand")
        place(objs, (0, 0, 0))
        camera((62, 62, 16), (0, 4, 15), 35)
        compositing(start=150, depth=2500)
    os.makedirs(OUT, exist_ok=True)
    sc.render.filepath = os.path.join(OUT, f"{name}-{shot}.png")
    bpy.ops.render.render(write_still=True)
    print(f"[dt] wakers concept {name} {shot} -> {sc.render.filepath}", flush=True)


if __name__ == "__main__":
    concepts, shots, size = args()
    for c in concepts:
        for s in shots:
            stage(c, s, size)
