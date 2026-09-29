"""CLINGER (GDD §21 flank, App. A.4 · vibration): "latch onto cargo hulls and drill through. Audible, locatable."

A parasite pressed flat to a car's side. A kitbash of two scans:
  * a spiny crab (Three D Scans), turned so its back faces out from the hull and pressed flat to the plate, its legs
    splayed on the steel. From along the train, "a blister on the car's flank with legs" (§26.1);
  * Lee Perry-Smith's scanned face (CC BY 3.0), three times, pushed up through the shell from underneath where the
    sacs were: they swell and ebb with it.
At its lower front seam, a lip of tar round a segmented drill boring into the car; the plate round the drill point
heats as it works (the one warm light on it: CreatureArt drives it with drill progress).

Model space: the hull is the plane x = 0 and the creature bulges to +X; its origin is the middle of its grip.
Bones and clips as the procedural one had (cling, drill, punish); the legs ride radial limb bones off the root, so
when the body drives into the hull they stay gripping the plate. Replaces tools/blender's procedural Clinger.

    python3 tools/models/fetch.py gk-crab threejs-leeperrysmith && tools/models/build.sh clinger
    CLINGER_PREVIEW=1 tools/models/build.sh clinger   # renders the assembly to out/review/clinger-pose-*.png
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
from rig import Clip  # noqa: E402

LONG, HIGH, PROUD = 2.1, 1.3, 0.44
SACS = {"sac_01": (0.25, 0.2), "sac_02": (-0.3, 0.1), "sac_03": (0.05, -0.25)}  # (y, z) on the shell
DRILL = [(0.16, 0.36, -0.24), (0.13, 0.5, -0.31), (0.07, 0.6, -0.35), (0.0, 0.65, -0.37)]


def verts(objs):
    return np.concatenate([np.array([o.matrix_world @ v.co for v in o.data.vertices], np.float32) for o in objs])


def plain(objs, colour, image=None):
    m = bpy.data.materials.new("scan")
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(image)
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)


cook.reset()
# The crab: in the file its back faces -Y and it's long along Z. Turned so its back faces +X (out of the hull), its
# width runs along the car, and pressed out to size, flat on the plate.
crab = cook.load("gk-crab", "models/threedscans/Crab.plain.glb")
cook.transform(crab, Matrix.Rotation(math.pi / 2, 4, "Z"))
lo, hi = cook.bounds(crab)
ext = hi - lo
cook.transform(crab, Matrix.Diagonal((PROUD / ext.x, LONG / ext.y, HIGH / ext.z, 1)) @ Matrix.Translation(-(lo + hi) / 2))
lo, hi = cook.bounds(crab)
cook.move(crab, (-lo.x - 0.01, 0, 0))
plain(crab, (0.2, 0.16, 0.16))
cco = verts(crab)


def shell_at(y, z, r=0.07):
    near = cco[(np.abs(cco[:, 1] - y) < r) & (np.abs(cco[:, 2] - z) < r)]
    return float(near[:, 0].max()) if len(near) else PROUD * 0.8


# The faces: the scanned head's face alone (the front of it, brow to chin), turned to look out of the hull.
faces = []
for i, (sac, (y, z)) in enumerate(sorted(SACS.items())):
    head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
    plain(head, (0.5, 0.42, 0.38), os.path.join(cook.SOURCES, "threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/Map-COL.jpg"))
    cook.fit(head, height=0.46)
    hco = verts(head)
    mid_y = float(np.median(hco[:, 1]))
    cook.cut(head, lambda c: 0.2 < c.z < 0.45 and c.y < mid_y)  # (it faces -Y)
    cook.transform(head, Matrix.Rotation(math.pi / 2, 4, "Z"))  # now it looks +X
    hco = verts(head)
    centre = Vector((float(hco[:, 0].max()), float(hco[:, 1].mean()), float(hco[:, 2].mean())))
    roll = (-22, 14, 31)[i]
    cook.transform(head, Matrix.Translation(Vector((shell_at(y, z) + 0.035, y, z))) @ Matrix.Rotation(math.radians(roll), 4, "X")
                   @ Matrix.Rotation(math.radians((8, -12, 5)[i]), 4, "Z") @ Matrix.Translation(-centre))
    faces.append((sac, head))


def cone(a, b, r0, r1, material, name, n=8):
    a, b = Vector(a), Vector(b)
    bpy.ops.mesh.primitive_cone_add(vertices=n, radius1=r0, radius2=r1, depth=(b - a).length)
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.data.transform(Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(b - a).to_matrix().to_4x4())
    o.data.materials.append(material)
    uv = o.data.uv_layers[0] if o.data.uv_layers else o.data.uv_layers.new(name="UVMap")
    for li, loop in enumerate(o.data.loops):
        p = o.data.vertices[loop.vertex_index].co
        uv.data[li].uv = (math.atan2(p.y - a.y, p.z - a.z) * 0.1, (p - a).length * 4)
    return o


# The drill: a lip of tar round a segmented bit of mineral crust, boring in at a slant.
crust = cook.library_material("mineral_growth", 0.3)
tar = cook.library_material("tar", 0.4)
drill = [cone(DRILL[i], DRILL[i + 1], (0.075, 0.06, 0.045)[i], (0.06, 0.045, 0.012)[i], crust, f"drill_{i}") for i in range(3)]
lip = cone(Vector(DRILL[0]) + Vector((0.05, -0.07, 0.04)), Vector(DRILL[0]) + Vector((-0.02, 0.04, -0.02)), 0.12, 0.1, tar, "lip", n=12)
# The hot plate where the bit goes in: a scorched ring on the hull (pure light; CreatureArt sets its colour).
hot = bpy.data.materials.new("hot.drill")
hot.use_nodes = True
hot.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.82, 0.38, 0.13, 1)
hot["dt_library"] = True
hot["dt_emissive"] = 1.0
hot["dt_shine"] = 0.0
hot["dt_glow"] = 0.0
bpy.ops.mesh.primitive_circle_add(vertices=12, radius=0.055, fill_type="NGON")
plate = bpy.context.view_layer.objects.active
plate.name = "hot_plate"
plate.data.transform(Matrix.Translation(Vector(DRILL[3]) + Vector((0.004, 0, 0))) @ Matrix.Rotation(math.pi / 2, 4, "Y"))
plate.data.materials.append(hot)

if os.environ.get("CLINGER_PREVIEW"):
    out = os.path.join(cook.ROOT, "out", "review")
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 700, 500
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.9, -0.6, 0.4)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for view, d, dist in (("out", (1, 0.15, 0.1), 3.4), ("along", (0.5, 1, 0.2), 3.6), ("face", (1, 0.3, 0.1), 1.2)):
        c = Vector((0.2, 0, 0)) if view != "face" else Vector((0.3, 0.25, 0.2))
        cam.location = c + Vector(d).normalized() * dist
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"clinger-pose-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] clinger preview -> out/review/clinger-pose-*.png")
    sys.exit(0)


# ----------------------------------------------------------------------------------------------------------------
# Baked down as one (the shell and the faces in it): the shell bruised dark, glued to the plate with tar at its rim.
def rim(p):
    return np.clip((0.07 - p[:, 0]) / 0.06, 0, 1)


def veins(p):
    return np.clip((cook.noise_np(p * np.array([4, 14, 14]), 91) - 0.2) * 3, 0, 1)


def paint(base, ao, m):
    c = base * (0.25 + 0.75 * ao ** 2)[..., None]
    c = c * (1 - 0.5 * m["veins"][..., None]) + np.array([0.05, 0.012, 0.02], np.float32) * m["veins"][..., None] * 0.5
    return c * (1 - m["rim"][..., None]) + np.array([0.008, 0.007, 0.006], np.float32) * m["rim"][..., None]


face_objs = [o for _, f in faces for o in f]
body = cook.bake_down(crab + face_objs, "clinger", 3800, colour=None, size=1024, masks={"rim": rim, "veins": veins}, paint=paint)[0]
layers = cook.bake_layers("clinger", [body], family="creature", source_ids=["gk-crab", "threejs-leeperrysmith"])
cook._merge_index("clinger", layers)

# ----------------------------------------------------------------------------------------------------------------
# The rig, as the procedural Clinger's: body, the sacs (here the faces), the seams, the drill; and limb bones
# radiating out over the hull off the root for the legs.
bones = [("root", None, (0, 0, 0), (0.15, 0, 0)),
         ("body", "root", (0.02, 0, 0), (0.3, 0, 0)),
         ("seam_l", "body", (0.3, 0.0, 0.35), (0.3, 0.0, -0.3)),
         ("seam_r", "body", (0.3, 0.12, 0.35), (0.3, 0.12, -0.3))]
for sac, (y, z) in sorted(SACS.items()):
    x = shell_at(y, z)
    bones.append((sac, "body", (x - 0.12, y, z), (x + 0.02, y, z)))
for i in range(3):
    bones.append((f"drill_0{i + 1}", "body" if i == 0 else f"drill_0{i}", DRILL[i], DRILL[i + 1]))
LIMBS = []
for k in range(8):
    a = math.radians(22.5 + 45 * k)
    d = Vector((0, math.cos(a) * LONG / 2, math.sin(a) * HIGH / 2))
    LIMBS.append(f"limb_{k}")
    bones.append((f"limb_{k}", "root", tuple(Vector((0.08, 0, 0)) + d * 0.45), tuple(Vector((0.05, 0, 0)) + d * 1.0)))

# The body's mesh is weighted to its nearest bones: the shell's middle to the body, the legs to the limbs off the
# root, so the body can drive into the hull and leave the legs gripping.
# (The faces are baked into the body: the sac bones on them move them through their weights.)
rigid = {o: f"drill_0{i + 1}" for i, o in enumerate(drill)}
rigid.update({lip: "drill_01", plate: "root"})

# Clips. The body bone points +X (out of the hull): about X is its twist; about Y tips it up/down; about Z swings it
# fore/aft. Limbs lie flat on the hull, so their clench is about X.
cling = Clip("cling")
cling.key(0, {}, "BEZIER")
cling.key(30, {"sac_01@scale": (1.1, 1.08, 1.1), "sac_02@scale": (1.06, 1.1, 1.06), "sac_03@scale": (1.08, 1.05, 1.1),
               "body@scale": (1.0, 1.04, 1.02), "body@loc": (0.0, 0.0, 0.0)}, "BEZIER")
cling.key(44, {"sac_01@scale": (1.04, 1.02, 1.04), "sac_02@scale": (1.1, 1.12, 1.08), "sac_03@scale": (1.02, 1.0, 1.02)}, "BEZIER")
cling.close(72)

# Drill: 20 frames, four ratchet steps of the bit a loop (CONSTANT: it clicks round, it doesn't spin), the body
# shuddering with each, the legs clenching out of step.
drill_clip = Clip("drill")
for k, f in enumerate((0, 5, 10, 15)):
    pose = {"drill_03@spin": 90 * k, "drill_02@spin": -90 * k, "drill_01": (0, 2 * (-1) ** k, 0),
            "body": (0, 1.5 * (-1) ** k, 1.5 * (-1) ** k), "body@loc": (-0.01 if k % 2 else 0.0, 0, 0),
            "sac_02@scale": (1.0 + 0.04 * (k % 2), 1.0, 1.0)}
    for i, n in enumerate(LIMBS):
        pose[n] = (4 * math.sin(k * 1.6 + i), 0, 0)
    drill_clip.key(f, pose, "CONSTANT")
drill_clip.key(20, {**drill_clip.keys[0][1], "drill_03@spin": 360, "drill_02@spin": -360}, "CONSTANT")

# Punish: it draws in on itself (the faces sink back into it), the seam gapes, then it drives into the car: the body
# shoved half through the plate, the legs hauling, the bit buried.
punish = Clip("punish", loop=False)
punish.key(0, {}, "LINEAR")
punish.key(6, {"body@scale": (0.95, 0.8, 0.95), "sac_01@scale": (0.7, 0.7, 0.7), "sac_02@scale": (0.7, 0.7, 0.7),
               "sac_03@scale": (0.7, 0.7, 0.7), "seam_l": (0, 0, 14), "seam_r": (0, 0, -14)}, "CONSTANT")
thrust = {"body@loc": (-0.22, 0, 0), "body@scale": (0.9, 1.1, 0.9), "seam_l": (0, 0, 24), "seam_r": (0, 0, -24),
          "drill_01": (0, 0, 0), "drill_02@spin": -60, "drill_03@spin": 140}
for i, n in enumerate(LIMBS):
    thrust[n] = (9 * (-1) ** i, 0, 0)
punish.key(9, thrust, "LINEAR")
punish.key(18, {**thrust, "body@loc": (-0.3, 0, 0)}, "LINEAR")
punish.key(24, {**thrust, "body@loc": (-0.3, 0, 0), "seam_l": (0, 0, 18), "seam_r": (0, 0, -18)}, "LINEAR")

cook.rig_creature("clinger", [body] + drill + [lip, plate], bones, [cling, drill_clip, punish], rigid=rigid,
                  skeleton="SK_Clinger")
