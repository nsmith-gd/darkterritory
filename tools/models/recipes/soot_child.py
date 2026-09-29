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
from mathutils import Matrix, Quaternion, Vector  # noqa: E402

import cook  # noqa: E402
import rig  # noqa: E402
from rig import Bone, Clip, Skeleton, over, rot  # noqa: E402

cook.reset()
objs = cook.load("gk-boyroom", "models/imaginary-friend-room/scene.plain.glb")
cook.fit(objs, height=2.7)
room = objs[0]
bpy.ops.object.select_all(action="DESELECT")
room.select_set(True)
bpy.context.view_layer.objects.active = room
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="MATERIAL")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]
boy = [o for o in parts if o.data.materials and "boy" in o.data.materials[0].name]
cook.delete([o for o in parts if o not in boy])
boy = boy[0]
co = np.empty(len(boy.data.vertices) * 3, np.float32)
boy.data.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
cook.transform([boy], Matrix.Translation(Vector((-float(co[:, 0].mean()), -float(co[:, 1].mean()), -float(co[:, 2].min())))))
# His toy sword out of his hand: everything past the fist. (He had it up at the thing he drew.)
cook.cut([boy], lambda c: c.y < 0.37)

# The boy's joints as he stands (vertex centroids in bands of the figure, 1.1 m tall, facing +Y): the right arm
# reaching out to the thing he drew, the left hanging back, the head tipped up to it.
J = {
    "pelvis": (0.004, 0.01, 0.463), "belly": (-0.001, 0.095, 0.594), "chest": (0.019, 0.043, 0.707),
    "neck": (0.017, -0.024, 0.81), "head": (0.048, -0.061, 0.955), "top": (0.056, -0.097, 1.086),
    "hip_l": (-0.07, 0.0, 0.44), "hip_r": (0.07, 0.03, 0.44),
    "knee_l": (-0.078, -0.037, 0.251), "knee_r": (0.061, 0.07, 0.251),
    "ankle_l": (-0.086, -0.07, 0.058), "ankle_r": (0.098, 0.07, 0.058),
    "toe_l": (-0.086, 0.06, 0.02), "toe_r": (0.098, 0.2, 0.02),
    "sh_l": (-0.097, -0.076, 0.741), "sh_r": (0.117, 0.115, 0.744),
    "elbow_l": (-0.178, -0.219, 0.606), "hand_l": (-0.247, -0.274, 0.496),
    "elbow_r": (0.114, 0.174, 0.688), "hand_r": (0.024, 0.35, 0.629),
}
J = {k: Vector(v) for k, v in J.items()}
J["tip_l"] = J["hand_l"] + (J["hand_l"] - J["elbow_l"]) * 0.5
J["tip_r"] = J["hand_r"] + (J["hand_r"] - J["elbow_r"]) * 0.5
J["mid"] = (J["belly"] + J["chest"]) / 2


def chain(J):
    out = [
        ("root", None, (0, 0, 0), (0, 0.15, 0)),
        ("pelvis", "root", J["pelvis"], J["belly"]),
        ("spine_01", "pelvis", J["belly"], J["mid"]),
        ("spine_02", "spine_01", J["mid"], J["chest"]),
        ("spine_03", "spine_02", J["chest"], J["neck"]),
        ("neck", "spine_03", J["neck"], J["head"]),
        ("head", "neck", J["head"], J["top"]),
    ]
    for s in ("l", "r"):
        out += [
            (f"clavicle_{s}", "spine_03", J["chest"] + (J[f"sh_{s}"] - J["chest"]) * 0.3, J[f"sh_{s}"]),
            (f"upperarm_{s}", f"clavicle_{s}", J[f"sh_{s}"], J[f"elbow_{s}"]),
            (f"lowerarm_{s}", f"upperarm_{s}", J[f"elbow_{s}"], J[f"hand_{s}"]),
            (f"hand_{s}", f"lowerarm_{s}", J[f"hand_{s}"], J[f"tip_{s}"]),
            (f"thigh_{s}", "pelvis", J[f"hip_{s}"], J[f"knee_{s}"]),
            (f"calf_{s}", f"thigh_{s}", J[f"knee_{s}"], J[f"ankle_{s}"]),
            (f"foot_{s}", f"calf_{s}", J[f"ankle_{s}"], J[f"toe_{s}"]),
        ]
    return out


stand = Skeleton("SK_Stand", [Bone(*b) for b in chain(J)])


def fk(sk, pose):
    """Each bone's world rotation and posed head (rig.pose_points' FK, kept per bone for skinning)."""
    world = {}
    for b in sk.bones:
        r = pose.get(b.name, (0, 0, 0))
        R = (r if isinstance(r, Quaternion) else rot(*r)).to_matrix()
        loc = Vector(pose.get(b.name + "@loc", (0, 0, 0)))
        if b.parent is None:
            world[b.name] = (R, b.head + loc)
        else:
            pW, ph = world[b.parent]
            world[b.name] = (pW @ R, ph + pW @ (b.head - sk[b.parent].head) + pW @ loc)
    return world


def weights(sk, co):
    names = [b.name for b in sk.bones if b.name != "root"]
    d = np.empty((len(co), len(names)), np.float32)
    for i, n in enumerate(names):
        a, c = np.array(sk[n].head, np.float32), np.array(sk[n].tail, np.float32)
        ab = c - a
        t = np.clip(((co - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)
        d[:, i] = np.linalg.norm(co - (a + t[:, None] * ab), axis=1)
    w = 1.0 / (d + 0.012) ** 6
    keep = np.argsort(-w, axis=1)[:, :4]
    mask = np.zeros_like(w)
    np.put_along_axis(mask, keep, 1.0, axis=1)
    w = w * mask
    return names, w / w.sum(axis=1, keepdims=True)


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

co = np.empty(len(boy.data.vertices) * 3, np.float32)
boy.data.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
names, w = weights(stand, co)
world = fk(stand, HUDDLE)
posed = np.zeros_like(co)
for i, n in enumerate(names):
    R, h = world[n]
    Rm = np.array(R, np.float32)
    rest = np.array(stand[n].head, np.float32)
    posed += w[:, i:i + 1] * ((co - rest) @ Rm.T + np.array(h, np.float32))
floor = float(posed[:, 2].min())
posed[:, 2] -= floor
boy.data.vertices.foreach_set("co", posed.ravel())
boy.data.update()

# The joints where the huddle left them: the game rig's bind pose.
P = {}
for b in stand.bones:
    R, h = world[b.name]
    P[b.name] = (h - Vector((0, 0, floor)), h + R @ (b.tail - b.head) - Vector((0, 0, floor)))

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
