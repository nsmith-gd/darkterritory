"""Scanned figures posed at full resolution (for the recipes that need a body in another pose than its scan's).

A figure is loaded with its joint table and a skeleton placed on those joints; `pose` skins its mesh to that skeleton
(linear blend, each vertex to its nearest bones) and moves it into a pose given in tools/blender/rig's terms (joint
rotations in the armature's axes). The mesh is then baked down and rigged again on the pose it was left in, so the
game rig never has to make the big fold itself (see recipes/soot_child.py).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Quaternion, Vector  # noqa: E402

import cook  # noqa: E402
from rig import Bone, Skeleton, rot  # noqa: E402

# The Boy Room's boy (Iman Aliakbar, CC BY 4.0) as he stands, fitted as the room is (2.7 m to its ceiling), centred
# on his feet, facing +Y: vertex centroids in bands of the figure, 1.1 m tall. The right arm reaches out to the thing
# he drew, the left hangs back, the head is tipped up to it.
BOY = {
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


def human_chain(J):
    """The bone list [(name, parent, head, tail)] for a human joint table like BOY's."""
    J = {k: Vector(v) for k, v in J.items()}
    J["tip_l"] = J["hand_l"] + (J["hand_l"] - J["elbow_l"]) * 0.5
    J["tip_r"] = J["hand_r"] + (J["hand_r"] - J["elbow_r"]) * 0.5
    J["mid"] = (J["belly"] + J["chest"]) / 2
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


def boy():
    """The boy, alone (the rest of his room deleted), his toy sword cut out of his hand; and his standing skeleton."""
    before = set(bpy.data.objects)
    objs = cook.load("gk-boyroom", "models/imaginary-friend-room/scene.plain.glb")
    cook.fit(objs, height=2.7)
    room = objs[0]
    bpy.ops.object.select_all(action="DESELECT")
    room.select_set(True)
    bpy.context.view_layer.objects.active = room
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.separate(type="MATERIAL")
    bpy.ops.object.mode_set(mode="OBJECT")
    parts = [o for o in bpy.data.objects if o not in before and o.type == "MESH"]
    body = [o for o in parts if o.data.materials and "boy" in o.data.materials[0].name][0]
    cook.delete([o for o in parts if o is not body])
    co = verts(body)
    cook.transform([body], Matrix.Translation(Vector((-float(co[:, 0].mean()), -float(co[:, 1].mean()), -float(co[:, 2].min())))))
    # His toy sword: everything past the fist. (He had it up at the thing he drew.)
    cook.cut([body], lambda c: c.y < 0.37)
    return body, Skeleton("SK_Stand", [Bone(*b) for b in human_chain(BOY)])


def verts(obj):
    co = np.empty(len(obj.data.vertices) * 3, np.float32)
    obj.data.vertices.foreach_get("co", co)
    return co.reshape(-1, 3)


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
    """Each vertex to its nearest four bones (inverse distance^6 to the bone's segment)."""
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


def pose(obj, sk, pose_, floor=True):
    """Skins `obj` to `sk` and leaves it in `pose_` (and, with `floor`, stood back on z = 0). Returns the posed joints
    {bone: (head, tail)} (the bind pose to rig it again on) and the drop applied."""
    co = verts(obj)
    names, w = weights(sk, co)
    world = fk(sk, pose_)
    posed = np.zeros_like(co)
    for i, n in enumerate(names):
        R, h = world[n]
        posed += w[:, i:i + 1] * ((co - np.array(sk[n].head, np.float32)) @ np.array(R, np.float32).T + np.array(h, np.float32))
    drop = float(posed[:, 2].min()) if floor else 0.0
    posed[:, 2] -= drop
    obj.data.vertices.foreach_set("co", posed.ravel())
    obj.data.update()
    down = Vector((0, 0, drop))
    joints = {}
    for b in sk.bones:
        R, h = world[b.name]
        joints[b.name] = (h - down, h + R @ (b.tail - b.head) - down)
    return joints, world, drop
