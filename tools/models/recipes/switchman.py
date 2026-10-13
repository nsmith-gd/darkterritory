"""THE SWITCHMAN (GDD §21 corrupted humans, App. A.7): "a corrupted railway worker still doing its job"; "a distant
figure at the switch", its lantern out.

A kitbash of three scans and the game's own lamp:
  * the body is Harriet Hosmer's "Zenobia in Chains" (Three D Scans): the long gown and the mantle over it read, sooted
    and oiled black, as a railwayman's long coat, and the chains she carries in her hands carry his lantern;
  * the head is Lee Perry-Smith's scanned head (CC BY 3.0), a man's, set on her shoulders in place of hers;
  * over it the Khronos Flight Helmet (CC0): the leather cap, the goggles, the rubber mask with its corrugated hose
    hanging down the chest. No face at all, then; just the goggles catching the light;
  * the hand lantern (tools/models hand_lantern), hung low in front from her chain, a flame in it.

2.0 m, as the statue stands. Rigged on its own pose (cook.rig_creature); the lantern on its own bone under the left hand
so it hangs plumb and swings. Clips: wait (still as a statue but for the lantern swinging, and the head snapping round
now and then, loop), flee (a lurching, dragging run, the hem swinging, loop), grip (its hand on the lever, waiting to
throw it under the train: the derail tell, loop), throw (the lever heaved over, and then dead still, watching, once). Replaces tools/blender's procedural one.

    python3 tools/models/fetch.py gk-hosmer threejs-leeperrysmith khronos-flighthelmet && tools/models/build.sh switchman
    SWITCH_PREVIEW=1 tools/models/build.sh switchman   # renders the assembled figure to out/review/switch-pose-*.png
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
from rig import Clip, over  # noqa: E402


def verts(objs):
    return np.concatenate([np.array([o.matrix_world @ v.co for v in o.data.vertices], np.float32) for o in objs])


def plain(objs, colour, image=None):
    """A plain material on scan meshes that have none (or a texture the file doesn't carry)."""
    m = bpy.data.materials.new("scan")
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = 0.6
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(image)
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    for o in objs:
        o.data.materials.clear()
        o.data.materials.append(m)


cook.reset()
# The body, stood up (the scan lies along its Y, the plinth at +Y), off its plinth, and the head taken off at the neck.
body = cook.load("gk-hosmer", "models/threedscans/Hosmer.plain.glb")
cook.transform(body, Matrix.Rotation(-math.pi / 2, 4, "X"))
cook.fit(body, height=2.0)
PLINTH = 0.105
cook.cut(body, lambda c: c.z > PLINTH)
NECK = 1.665
cook.cut(body, lambda c: c.z < NECK + 0.02)
# The coat: the marble as oiled black cloth.
plain(body, (0.05, 0.047, 0.04))
co = verts(body)
top = co[co[:, 2] > NECK - 0.04]
neck = Vector((float(top[:, 0].mean()), float(top[:, 1].mean()), NECK))
# Her hair, knotted low at the back of her neck, goes with her head.
cook.cut(body, lambda c: not (c.z > NECK - 0.1 and c.y < neck.y - 0.06))

# The man's head (it faces -Y: turned to face the body's +Y), from the neck up, on her neck.
head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
plain(head, (0.5, 0.42, 0.38), os.path.join(cook.SOURCES, "threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/Map-COL.jpg"))
cook.fit(head, height=0.46)
cook.rotate(head, 180)
cook.cut(head, lambda c: c.z > 0.15)
hco = verts(head)
hc = Vector((float(hco[:, 0].mean()), float(hco[:, 1].mean()), float(hco[:, 2].min())))
cook.move(head, (neck.x - hc.x, neck.y - hc.y, NECK - 0.06 - hc.z))
hco = verts(head)
skull = hco[hco[:, 2] > np.percentile(hco[:, 2], 70)]
crown = Vector((float(skull[:, 0].mean()), float(skull[:, 1].mean()), float(hco[:, 2].max())))
skull_w = float(skull[:, 0].max() - skull[:, 0].min())
# Only the face and neck are ever seen, under the cap: the scalp goes.
cook.cut(head, lambda c: (c.z < crown.z - 0.09 or c.y > crown.y + 0.02) and c.y > crown.y - 0.035)

# The flying helmet over it: its stand taken away (the wooden base and post), the hose cut where it reached the base.
helmet = cook.load("khronos-flighthelmet", "Models/FlightHelmet/glTF/FlightHelmet.gltf")
cook.fit(helmet, height=0.5)
for o in helmet:
    if "RubberWood" in o.name:
        # The stand is the wood: everything of it below the cap, and the post up behind the head.
        cook.cut([o], lambda c: c.z > 0.3 and not (c.y > 0.02 and c.z < 0.4))
    elif "Hose" in o.name:
        cook.cut([o], lambda c: c.z > 0.12)
helmet = [o for o in helmet if len(o.data.polygons)]
cook.rotate(helmet, 180)
leather = [o for o in helmet if "Leather" in o.name]
lco = verts(leather)
ltop = lco[lco[:, 2] > np.percentile(lco[:, 2], 70)]
k = skull_w * 1.18 / float(ltop[:, 0].max() - ltop[:, 0].min())
lc = Vector((float(ltop[:, 0].mean()), float(ltop[:, 1].mean()), float(lco[:, 2].max())))
cook.transform(helmet, Matrix.Translation(crown + Vector((0, 0, 0.012))) @ Matrix.Scale(k, 4) @ Matrix.Translation(-lc))

# Where her left hand holds the chain: the figure's furthest point out to its -X, at the hip.
band = co[(co[:, 2] > 0.8) & (co[:, 2] < 1.1)]
edge = band[band[:, 0] < band[:, 0].min() + 0.05]
hand_l = Vector((float(edge[:, 0].mean()) + 0.04, float(edge[:, 1].mean()), float(edge[:, 2].mean())))
hang = hand_l + Vector((0.02, 0.1, -0.34))

if os.environ.get("SWITCH_PREVIEW"):
    out = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 400, 700
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.7, 0.3, 2.6)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.03, location=hang)
    for view, d, c, dist in (("front", (0, 1, 0.1), (0, 0.1, 1.0), 4.6), ("side", (1, 0, 0.1), (0, 0.1, 1.0), 4.6),
                             ("head", (0.4, 1, 0.1), tuple(crown - Vector((0, 0, 0.14))), 1.1)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"switch-pose-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] switchman preview -> out/review/switch-pose-*.png")
    sys.exit(0)

# ----------------------------------------------------------------------------------------------------------------
# The lantern: the cooked hand lantern, hung by its ring from her chain, a flame in it (pure light).
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=os.path.join(cook.PROPS, "hand_lantern.glb"))
new = [o for o in bpy.data.objects if o not in before]
# (The importer also makes a bone-display sphere: only the lantern's own meshes.)
lantern = [o for o in new if o.type == "MESH" and o.data.materials and o.data.materials[0].name.startswith("hand_lantern")]
for o in lantern:
    o.modifiers.clear()
    o.vertex_groups.clear()
    w = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = w
bpy.ops.object.select_all(action="DESELECT")
for o in lantern:
    o.select_set(True)
bpy.context.view_layer.objects.active = lantern[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
cook.delete([o for o in new if o not in lantern])
llo, lhi = cook.bounds(lantern)
cook.move(lantern, (hang.x - (llo.x + lhi.x) / 2, hang.y - (llo.y + lhi.y) / 2, hang.z - lhi.z))
flame = cook.eyes_at([hang - Vector((0, 0, (lhi.z - llo.z) * 0.58))], 0.022, colour=(1.0, 0.7, 0.36), name="flame")
# Its chain, from her hand down to the ring: a run of small links in the library's rusted iron.
iron = cook.library_material("rust_heavy", 0.3)
links = []
n = 7
for i in range(n):
    a = hand_l + (hang - hand_l) * (i / n)
    b = hand_l + (hang - hand_l) * ((i + 1) / n)
    c = cook.cube((a + b) / 2, (0.006, 0.006, (b - a).length / 2 + 0.004), f"link_{i}")
    d = (b - a).normalized()
    cook.transform([c], Matrix.Translation((a + b) / 2) @ Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
                   @ Matrix.Rotation(i * math.pi / 2, 4, "Z") @ Matrix.Translation(-(a + b) / 2))
    c.data.materials.append(iron)
    uv = c.data.uv_layers.new(name="UVMap")
    for li in range(len(c.data.loops)):
        uv.data[li].uv = (li % 2 * 0.05, li // 2 % 2 * 0.05)
    links.append(c)


# ----------------------------------------------------------------------------------------------------------------
# Baked down as one figure (coat, head and helmet), sooted: oil and soot run down the coat from the shoulders, the
# leather and rubber gone dull and black at the edges.
def streaks(p):
    n = cook.noise_np(p * np.array([26, 26, 1.8]), 71) * 0.7 + cook.noise_np(p * np.array([70, 70, 5]), 72) * 0.3
    return np.clip((n - 0.1) * 2.5, 0, 1)


def paint(base, ao, m):
    s = m["streaks"][..., None]
    return base * (0.3 + 0.7 * ao ** 2)[..., None] * (1 - 0.6 * s) + np.array([0.004, 0.0036, 0.003], np.float32) * s


figure = cook.bake_down(body + head + helmet, "switchman", 6200, colour=None, size=1024, masks={"streaks": streaks}, paint=paint)[0]
layers = cook.bake_layers("switchman", [figure], family="creature", source_ids=["gk-hosmer", "threejs-leeperrysmith", "khronos-flighthelmet"])
cook._merge_index("switchman", layers)

# On the floor: everything down by the plinth's height.
parts = [figure] + lantern + flame + links
cook.move(parts, (0, 0, -PLINTH))
drop = Vector((0, 0, PLINTH))
hand_l, hang, neck, crown = hand_l - drop, hang - drop, neck - drop, crown - drop

# ----------------------------------------------------------------------------------------------------------------
# The rig, on the statue's pose (joints read off its silhouette: the centre line at x -0.03, y 0.17; the right arm
# hanging at its side, the left bent with the hand at the hip holding the chain; the legs under the gown).
Z = PLINTH


def at(x, y, z):
    return (x, y, z - Z)


J = {
    "pelvis": at(-0.03, 0.17, 1.0), "spine_01": at(-0.03, 0.17, 1.15), "spine_02": at(-0.035, 0.17, 1.3),
    "spine_03": at(-0.04, 0.17, 1.45), "neck": tuple(neck + Vector((0, 0, -0.03))), "head": tuple(neck + Vector((0, 0.02, 0.07))),
    "head_top": tuple(crown),
    "clavicle_r": at(0.04, 0.16, 1.5), "upperarm_r": at(0.15, 0.16, 1.53), "lowerarm_r": at(0.21, 0.16, 1.22),
    "hand_r": at(0.23, 0.2, 0.93), "fingers_r": at(0.23, 0.2, 0.83),
    "clavicle_l": at(-0.1, 0.16, 1.5), "upperarm_l": at(-0.22, 0.16, 1.53), "lowerarm_l": at(-0.3, 0.13, 1.22),
    "hand_l": tuple(hand_l), "fingers_l": tuple(hand_l + Vector((0.02, 0.05, -0.08))),
    "thigh_l": at(-0.11, 0.17, 0.97), "calf_l": at(-0.11, 0.2, 0.52), "foot_l": at(-0.11, 0.22, 0.13), "toe_l": at(-0.11, 0.33, 0.1),
    "thigh_r": at(0.07, 0.17, 0.97), "calf_r": at(0.07, 0.2, 0.52), "foot_r": at(0.07, 0.22, 0.13), "toe_r": at(0.07, 0.33, 0.1),
}
chain = [
    ("root", None, (0, 0, 0), (0, 0.2, 0)),
    ("pelvis", "root", J["pelvis"], J["spine_01"]),
    ("spine_01", "pelvis", J["spine_01"], J["spine_02"]),
    ("spine_02", "spine_01", J["spine_02"], J["spine_03"]),
    ("spine_03", "spine_02", J["spine_03"], J["neck"]),
    ("neck", "spine_03", J["neck"], J["head"]),
    ("head", "neck", J["head"], J["head_top"]),
]
for sd in ("l", "r"):
    chain += [
        (f"clavicle_{sd}", "spine_03", J[f"clavicle_{sd}"], J[f"upperarm_{sd}"]),
        (f"upperarm_{sd}", f"clavicle_{sd}", J[f"upperarm_{sd}"], J[f"lowerarm_{sd}"]),
        (f"lowerarm_{sd}", f"upperarm_{sd}", J[f"lowerarm_{sd}"], J[f"hand_{sd}"]),
        (f"hand_{sd}", f"lowerarm_{sd}", J[f"hand_{sd}"], J[f"fingers_{sd}"]),
        (f"thigh_{sd}", "pelvis", J[f"thigh_{sd}"], J[f"calf_{sd}"]),
        (f"calf_{sd}", f"thigh_{sd}", J[f"calf_{sd}"], J[f"foot_{sd}"]),
        (f"foot_{sd}", f"calf_{sd}", J[f"foot_{sd}"], J[f"toe_{sd}"]),
    ]
chain.append(("lantern", "hand_l", tuple(hang), tuple(hang - Vector((0, 0, 0.2)))))

# ----------------------------------------------------------------------------------------------------------------
# Clips. About X, + swings a hanging thing forward (toward +Y, the way it faces) and tips a head back; about Z it turns.
STILL = {}
wait = Clip("wait")
# Still as the statue it was; only the lantern swings on its chain, slowly, as if something just let go of it.
for f, sw in ((0, 9), (22, 0), (45, -9), (68, 0)):
    wait.key(f, {"lantern": (sw, sw * 0.3, 0)})
# The head snaps round to the side and holds, then back, two frames each way.
wait.key(78, {"lantern": (6, 1.8, 0)}, "CONSTANT")
wait.key(80, {"lantern": (7, 2, 0), "neck": (0, 0, 20), "head": (8, 12, 38)}, "CONSTANT")
wait.key(98, {"lantern": (9, 2.7, 0), "neck": (0, 0, 20), "head": (8, 12, 38)}, "CONSTANT")
wait.key(100, {"lantern": (9, 2.7, 0)})
wait.close(112)

# The flee (2.1 s, loop: two long strides): slow and deliberate, not a scurry (the Look Review, 6 Oct: "more fluid,
# slow, and intentioned"). Pitched forward from the chest, each foot set down heavy and the weight rolled over it before
# the other's dragged through under the hem; the hips and the shoulders turning against each other, the free arm
# swinging long, the lantern swinging behind the walk; once a cycle the head turns all the way round over its shoulder,
# unhurried, to look back at the train, holds it, and turns away again. Eased all through.
FLEE_FRAMES = 64


def flee_pose(u):
    w = 2 * math.pi * u
    s, c = math.sin(w), math.cos(w)
    # (The look back: up from 0.3 of the cycle, held to 0.55, away by 0.75.)
    look = min(1.0, max(0.0, (u - 0.3) / 0.12)) * min(1.0, max(0.0, (0.75 - u) / 0.15))
    look = look * look * (3 - 2 * look)
    return {
        "root@loc": (0.025 * s, 0, -0.02 + 0.025 * math.cos(2 * w)),
        "pelvis": (0, 0, 7 * s), "spine_01": (-4, 0, -2 * s), "spine_02": (-10, 3 * s, -4 * s), "spine_03": (-10, 2 * s, -5 * s + 8 * look),
        "neck": (10 - 4 * look, 0, -2 * s + 26 * look), "head": (6 - 4 * look, 6 * look, 34 * look),
        "thigh_l": (24 * s, 0, 0), "thigh_r": (-24 * s, 0, 0),
        # The knee bends through the swing (the thigh going forward), straight under the weight.
        "calf_l": (-6 - 36 * max(0.0, c) ** 1.4, 0, 0), "calf_r": (-6 - 36 * max(0.0, -c) ** 1.4, 0, 0),
        "foot_l": (8 * max(0.0, c), 0, 0), "foot_r": (8 * max(0.0, -c), 0, 0),
        "upperarm_r": (-20 * s, 0, 4), "lowerarm_r": (14 + 8 * max(0.0, s), 0, 0),
        "lantern": (24 * math.sin(w - 0.9), 6 * math.sin(w - 0.9), 0),
    }


flee = Clip("flee")
for f in range(0, FLEE_FRAMES, 4):
    flee.key(f, flee_pose(f / FLEE_FRAMES), "BEZIER")
flee.close(FLEE_FRAMES)

# Grip (1.6 s, loop; the derailer's tell, COMMIT): its right hand down on the lever at its side and gripping, leant to
# it, the arm locked; the hand trembling on it in pops, waiting for the train to be over the points; the head turned
# a little to watch it come, and once, snapped round to it.
GRIP = {"spine_02": (-8, 8, -8), "spine_03": (-4, 6, -6), "upperarm_r": (30, -24, 0), "lowerarm_r": (30, 0, 0), "hand_r": (10, 0, 0),
        "head": (-6, 0, -10), "lantern": (4, 1, 0)}
grip = Clip("grip")
grip.key(0, GRIP, "CONSTANT")
for f, k in ((6, 1), (8, -1), (10, 1), (14, 0), (26, 1), (27, -1), (28, 0)):
    grip.key(f, over(GRIP, lowerarm_r=(36 + 2 * k, 0, 0), hand_r=(10 - 3 * k, 0, 0), spine_03=(-4, 4, -6 + k)), "CONSTANT")
grip.key(36, over(GRIP, head=(-4, 0, -34)), "CONSTANT")
grip.key(44, over(GRIP, head=(-4, 0, -34)), "CONSTANT")
grip.key(45, GRIP, "CONSTANT")
grip.close(48)

# Throw (1 s, once; PUNISH, the points thrown: the train down the wrong road, or off the rails): the lever heaved over
# from its side and across in front of it, the body twisting with it and leaning back into the pull; then dead still,
# its hand still on it, the head cocked, watching what it's done.
THROWN = {"spine_01": (2, -4, 10), "spine_02": (4, -6, 16), "spine_03": (6, -4, 14), "upperarm_r": (52, 18, 10), "lowerarm_r": (40, 0, 0),
          "hand_r": (-10, 0, 0), "head": (-10, -14, -6), "neck": (0, 0, -8), "lantern": (-14, -4, 0)}
throw = Clip("throw", loop=False)
throw.key(0, GRIP, "CONSTANT")
throw.key(4, over(GRIP, spine_02=(-12, 8, -14), upperarm_r=(18, -18, 0), lowerarm_r=(44, 0, 0)), "LINEAR")
throw.key(9, THROWN, "LINEAR")
throw.key(12, over(THROWN, spine_02=(6, -6, 18), lantern=(-22, -6, 0)), "BEZIER")
throw.key(18, over(THROWN, lantern=(8, 2, 0)), "BEZIER")
throw.key(30, THROWN, "BEZIER")

rigid = {o: "lantern" for o in lantern + flame + links[-2:]}
rigid.update({o: "hand_l" for o in links[:-2]})
cook.rig_creature("switchman", parts, chain, [wait, flee, grip, throw], rigid=rigid)
