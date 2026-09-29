"""SOOT CHILDREN (GDD §21 structural, App. A.6 · sound): "outside in the dark, calling for help in your crewmates'
voices."

Three together at the edge of the lamplight, huddled with their arms round their knees: at a glance, children
sheltering by the line, which is the lure. It is the same child each time, and the same child as the one standing in
the ruined bedroom in the villages (tools/models/recipes/boy_room.py): the boy from Iman Aliakbar's "Boy Room"
(CC BY 4.0), taken out of his room, sat down in the ash and sooted. Waxy pale where the skin shows, soot and oil
run down him, the clothes gone to rag-grey, eyes that are just dark. They don't move while looked at; when they call,
the heads come up towards the doors one after another.

The figure is posed into the huddle at full resolution (a skin of its own, weighted to a skeleton placed on the boy's
joints), then baked down to a game mesh, and rigged again on the huddle: so what the game bends is only the small
movement it has (the head snap, the heads coming up), never the big fold from standing to crouched.

    python3 tools/models/fetch.py gk-boyroom && tools/models/build.sh soot_child
    SOOT_PREVIEW=1 tools/models/build.sh soot_child   # renders the posed figure to out/review/soot-pose-*.png, no bake
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import cook  # noqa: E402
import figures  # noqa: E402
import rig  # noqa: E402
from rig import Clip, over  # noqa: E402

cook.reset()
boy, stand = figures.boy()


# ----------------------------------------------------------------------------------------------------------------
# The huddle, from his standing pose: sat down in the ash, knees drawn up to the chest, the back curled over them,
# the head down on the knees and a little to one side, the arms round the shins.
HUDDLE = {
    "pelvis": (12, 0, 0), "spine_01": (-22, 0, 0), "spine_02": (-24, 0, 0), "spine_03": (-16, 0, 0),
    "neck": (-30, 0, 0), "head": (-52, 8, 10),
    "thigh_l": (116, -4, 0), "calf_l": (-150, 0, 0), "foot_l": (34, 0, 0),
    "thigh_r": (110, 6, 0), "calf_r": (-146, 0, 0), "foot_r": (30, 0, 0),
}


def shin_target(pose, s, lift, inward):
    k, a = rig.pose_points(stand, pose, [(f"calf_{s}", "head"), (f"calf_{s}", "tail")])
    p = k + (a - k) * lift
    return p + Vector((-inward if s == "r" else inward, 0.07, 0))


for s in ("l", "r"):
    target = shin_target(HUDDLE, s, 0.45, 0.05)
    elbow_out = Vector((-1 if s == "l" else 1, 0, 0))

    def avoid(e, s=s):
        # Elbows out to the side of the knees, not through them.
        k = rig.pose_points(stand, HUDDLE, [(f"calf_{s}", "head")])[0]
        return 0.5 * max(0.0, 0.07 - (e - k).dot(elbow_out))
    HUDDLE = rig.reach(stand, HUDDLE, f"upperarm_{s}", f"lowerarm_{s}", target, elbow_axis=0, bend=1, avoid=avoid)
    HUDDLE[f"hand_{s}"] = (20, 0, (1 if s == "l" else -1) * 30)

# Posed at full resolution; the joints where the huddle left them are the game rig's bind pose.
P, world, floor = figures.pose(boy, stand, HUDDLE)
posed = figures.verts(boy)

if os.environ.get("SOOT_PREVIEW"):
    out = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 12
    scene.cycles.use_denoising = False
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.7, 0.3, 0.6)
    scene.collection.objects.link(sun)
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.6
    scene.render.resolution_x = scene.render.resolution_y = 512
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    lo, hi = cook.bounds([boy])
    c = (lo + hi) / 2
    for view, d in (("front", Vector((0, 1, 0.25))), ("side", Vector((1, 0, 0.2))), ("three", Vector((0.7, 0.7, 0.4)))):
        cam.location = c + d.normalized() * 2.2
        cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"soot-pose-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] soot_child preview -> out/review/soot-pose-*.png")
    sys.exit(0)


# ----------------------------------------------------------------------------------------------------------------
# Baked down, and sooted: skin drained to a waxy ivory, the clothes to rag-grey, soot and oil run down him from the
# head, thick on the hands and feet where he sat in it.
def streaks(p):
    n = cook.noise_np(p * np.array([34, 34, 2.5]), 41) * 0.7 + cook.noise_np(p * np.array([90, 90, 6]), 42) * 0.3
    return np.clip((n + 0.05) * 3, 0, 1)


def low(p):
    return np.clip((0.16 - p[:, 2]) / 0.12, 0, 1)


# Eyes that are just dark: two pits of soot where the face's eyes were. The face looks the way the head bone does
# (it was tipped 15 degrees up at the drawing); the eyes sit on the front of the skull, either side.
R_head = world["head"][0]
face = (R_head @ Vector((0, math.cos(math.radians(15)), math.sin(math.radians(15))))).normalized()
side = (R_head @ Vector((1, 0, 0))).normalized()
up = face.cross(side).normalized() * -1
hc = (P["head"][0] + P["head"][1]) / 2
near = posed[np.linalg.norm(posed - np.array(hc, np.float32), axis=1) < 0.2]
along = (near - np.array(hc, np.float32)) @ np.array(face, np.float32)
front = hc + face * float(along.max())
EYES = [np.array(front - face * 0.02 + side * (sx * 0.034) + up * 0.005, np.float32) for sx in (-1, 1)]


def eyes(p):
    return np.clip(sum(np.exp(-np.sum((p - e) ** 2, axis=1) / (2 * 0.017 ** 2)) for e in EYES) * 1.6, 0, 1)


def paint(base, ao, m):
    lum = base.mean(-1, keepdims=True)
    skin = np.clip((base[..., 0:1] - base[..., 2:3]) * 8 - 0.2, 0, 1) * np.clip(lum * 6, 0, 1)
    wax = (0.35 + 0.5 * lum) * np.array([0.52, 0.53, 0.5], np.float32)
    rag = lum * 0.35 * np.array([0.9, 0.87, 0.82], np.float32)
    c = wax * skin + rag * (1 - skin)
    s = np.clip(m["streaks"] * 0.8 + m["low"], 0, 1)[..., None]
    c = c * (0.3 + 0.7 * ao ** 2)[..., None]
    c = c * (1 - 0.85 * s) + np.array([0.006, 0.005, 0.0045], np.float32) * s
    e = m["eyes"][..., None]
    return c * (1 - e) + np.array([0.002, 0.0018, 0.0016], np.float32) * e


body = cook.bake_down([boy], "soot_child", 4400, colour=None, masks={"streaks": streaks, "low": low, "eyes": eyes}, paint=paint)[0]
layers = cook.bake_layers("soot_child", [body], family="creature", source_ids=["gk-boyroom"])
cook._merge_index("soot_child", layers)

# ----------------------------------------------------------------------------------------------------------------
# The rig, on the huddle. Rotations from it, in the armature's axes: about X, + tips a head back (up).
bones = [(n, stand[n].parent, P[n][0], P[n][1]) for n in (b.name for b in stand.bones)]
bones[0] = ("root", None, (0, 0, 0), (0, 0.15, 0))

STILL = {}
huddle = Clip("huddle")
huddle.key(0, STILL, "CONSTANT")
huddle.key(50, STILL, "CONSTANT")
snap = {"neck": (4, 0, 12), "head": (6, -4, 26)}
huddle.key(52, snap, "CONSTANT")  # a snap to one side...
huddle.key(58, snap, "CONSTANT")
huddle.key(60, STILL, "CONSTANT")  # ...and back, as if it never moved
huddle.key(80, STILL, "CONSTANT")
huddle.key(81, {"hand_l": (-12, 0, 0)}, "CONSTANT")
huddle.key(83, STILL, "CONSTANT")
huddle.close(96)

# The call: the back straightens a little and the head comes up off the knees to the doors, too fast, and holds.
LOOK = {"spine_02": (10, 0, 0), "spine_03": (10, 0, 0), "neck": (30, 0, 0), "head": (34, -6, -4)}
turn = Clip("turn", loop=False)
turn.key(0, STILL, "CONSTANT")
turn.key(3, rig.blend(STILL, LOOK, 0.7), "LINEAR")
turn.key(5, over(LOOK, head=(40, -6, -4)), "LINEAR")
turn.key(9, LOOK, "CONSTANT")
turn.key(20, LOOK, "CONSTANT")

cook.rig_creature("soot_child", [body], bones, [huddle, turn])
