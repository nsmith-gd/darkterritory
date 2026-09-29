"""HOLLOW (GDD §21 interior, App. A.5 · heat): "comes down the smokestack when the fire burns low."

A kitbash of a real cadaver. Ligier Richier's 1547 transi of René de Chalon (scanned by Three D Scans): a flayed,
half-rotted man with his ribs open, one arm raised. Here it stands in the cab, 2.1 m, sooted black and ashen where
the lamp catches it, two pale points where its eyes should be. Motion (§31): "unnaturally still when observed" (it
stands as the statue stood, a two-frame twitch of the head now and then), then the raised arm comes down at you and
it lunges, too fast.

The scan is baked down to a game mesh (tools/models cook.bake_down), then rigged on its own pose: a 22-bone skeleton
placed on the statue's joints (read off its silhouette), each vertex weighted to its nearest bones. Replaces
tools/blender's procedural Hollow; same clips (idle, reach), same place (feet at the origin, facing the engine's -Z).

    python3 tools/models/fetch.py gk-transi && tools/models/build.sh hollow
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import rig  # noqa: E402
from rig import Bone, Clip, Skeleton, over  # noqa: E402

cook.reset()
objs = cook.load("gk-transi", "models/threedscans/Le_Transi_De_Rene_De_Chalon.plain.glb")
cook.fit(objs, height=2.1)
# Off its plinth (the top of it is at 9 cm): what stands in the cab is the man, not the monument.
cook.cut(objs, lambda c: c.z > 0.095)

# The statue's joints at this scale (vertex centroids in bands of its silhouette; see the recipe's history), then
# the whole taken down onto the floor, centred on its feet and scaled to 2.1 m standing.
J = {
    "pelvis": (-0.085, -0.02, 0.97), "spine_01": (-0.09, -0.03, 1.1), "spine_02": (-0.1, -0.015, 1.25),
    "spine_03": (-0.1, 0.0, 1.38), "neck": (-0.085, 0.055, 1.49), "head": (-0.099, 0.07, 1.56), "head_top": (-0.11, 0.08, 1.75),
    "clavicle_r": (-0.05, 0.06, 1.44), "upperarm_r": (0.06, 0.07, 1.45), "lowerarm_r": (0.256, -0.034, 1.717),
    "hand_r": (0.288, -0.115, 1.948), "fingers_r": (0.258, -0.112, 2.06),
    "clavicle_l": (-0.15, 0.03, 1.42), "upperarm_l": (-0.261, 0.034, 1.392), "lowerarm_l": (-0.258, 0.04, 1.107),
    "hand_l": (-0.252, 0.0, 0.902), "fingers_l": (-0.25, 0.0, 0.8),
    "thigh_l": (-0.17, -0.01, 0.93), "calf_l": (-0.161, 0.03, 0.55), "foot_l": (-0.144, 0.04, 0.153), "toe_l": (-0.144, 0.14, 0.1),
    "thigh_r": (-0.02, -0.03, 0.93), "calf_r": (-0.055, -0.105, 0.55), "foot_r": (-0.067, -0.025, 0.151), "toe_r": (-0.067, 0.08, 0.1),
}
lift, scale, centre = 0.095, 2.1 / (2.1 - 0.095), Vector((-0.105, 0.0, 0.0))
T = Matrix.Scale(scale, 4) @ Matrix.Translation(Vector((-centre.x, -centre.y, -lift)))
cook.transform(objs, T)
J = {k: tuple(T @ Vector(v)) for k, v in J.items()}


def streaks(p):
    n = cook.noise_np(p * np.array([30, 30, 1.6]), 21) * 0.7 + cook.noise_np(p * np.array([70, 70, 4]), 22) * 0.3
    return np.clip((n - 0.1) * 3, 0, 1)


def paint(base, ao, m):
    # Ash-grey where it stands out, soot-black in every hollow of it, black runs down it from the stack.
    s = m["streaks"][..., None]
    return base * (0.2 + 0.8 * ao ** 2.5)[..., None] * (1 - 0.85 * s) + np.array([0.004, 0.0036, 0.0034], np.float32) * s


low = cook.bake_down(objs, "hollow", 4600, colour=(0.06, 0.056, 0.052), masks={"streaks": streaks}, paint=paint)[0]

# The eyes: two pale points, a little under the brow, turned the way the face is.
head, top = Vector(J["head"]), Vector(J["head_top"])
face = Vector((-0.5, 0.87, 0)).normalized()
side = Vector((face.y, -face.x, 0))
eye_mat = bpy.data.materials.new("eye.hollow")
eye_mat.use_nodes = True
eye_mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.69, 0.64, 0.5, 1)
eye_mat["dt_library"] = True
eye_mat["dt_emissive"] = 1.0
eye_mat["dt_shine"] = 0.0
eye_mat["dt_glow"] = 0.0
eyes = []
for s in (-1, 1):
    at = head + (top - head) * 0.42 + face * 0.075 * scale + side * (s * 0.032 * scale)
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=0.008 * scale, location=at)
    e = bpy.context.view_layer.objects.active
    e.name = f"eye_{s}"
    e.data.materials.append(eye_mat)
    eyes.append(e)

layers = cook.bake_layers("hollow", [low], family="creature", source_ids=["gk-transi"])
cook._merge_index("hollow", layers)

# ----------------------------------------------------------------------------------------------------------------
# The rig, on the statue's own pose: each bone from its joint to the next one down the chain.
chain = [
    ("root", None, (0, 0, 0), (0, 0.2, 0)),
    ("pelvis", "root", J["pelvis"], J["spine_01"]),
    ("spine_01", "pelvis", J["spine_01"], J["spine_02"]),
    ("spine_02", "spine_01", J["spine_02"], J["spine_03"]),
    ("spine_03", "spine_02", J["spine_03"], J["neck"]),
    ("neck", "spine_03", J["neck"], J["head"]),
    ("head", "neck", J["head"], J["head_top"]),
]
for s in ("l", "r"):
    chain += [
        (f"clavicle_{s}", "spine_03", J[f"clavicle_{s}"], J[f"upperarm_{s}"]),
        (f"upperarm_{s}", f"clavicle_{s}", J[f"upperarm_{s}"], J[f"lowerarm_{s}"]),
        (f"lowerarm_{s}", f"upperarm_{s}", J[f"lowerarm_{s}"], J[f"hand_{s}"]),
        (f"hand_{s}", f"lowerarm_{s}", J[f"hand_{s}"], J[f"fingers_{s}"]),
        (f"thigh_{s}", "pelvis", J[f"thigh_{s}"], J[f"calf_{s}"]),
        (f"calf_{s}", f"thigh_{s}", J[f"calf_{s}"], J[f"foot_{s}"]),
        (f"foot_{s}", f"calf_{s}", J[f"foot_{s}"], J[f"toe_{s}"]),
    ]
sk = Skeleton("SK_Human", [Bone(n, p, h, t) for n, p, h, t in chain])
arm_obj = sk.build()


def weigh(obj, only=None):
    """Each vertex to its nearest bones (inverse distance to the bone's segment, the closest four), or wholly to `only`."""
    co = np.empty(len(obj.data.vertices) * 3, np.float32)
    obj.data.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    names = [b.name for b in sk.bones if b.name != "root"]
    if only is not None:
        g = obj.vertex_groups.new(name=only)
        g.add(list(range(len(co))), 1.0, "REPLACE")
        return
    d = np.empty((len(co), len(names)), np.float32)
    for i, n in enumerate(names):
        b = sk[n]
        a, c = np.array(b.head, np.float32), np.array(b.tail, np.float32)
        ab = c - a
        t = np.clip(((co - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)
        d[:, i] = np.linalg.norm(co - (a + t[:, None] * ab), axis=1)
    w = 1.0 / (d + 0.015) ** 6
    top = np.argsort(-w, axis=1)[:, :4]
    groups = {n: obj.vertex_groups.new(name=n) for n in names}
    for vi in range(len(co)):
        ws = w[vi, top[vi]]
        ws = ws / ws.sum()
        for k, bi in enumerate(top[vi]):
            if ws[k] > 0.03:
                groups[names[bi]].add([vi], float(ws[k]), "REPLACE")


weigh(low)
for e in eyes:
    weigh(e, only="head")
for o in [low] + eyes:
    for a in list(o.data.color_attributes):
        o.data.color_attributes.remove(a)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges)
    bm.to_mesh(o.data)
    bm.free()
    o.data.validate(clean_customdata=True)
    for f in o.data.polygons:
        f.use_smooth = True
    mod = o.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj
    o.parent = arm_obj

# ----------------------------------------------------------------------------------------------------------------
# Clips. Rotations in the armature's axes at each joint: about X, - tips something standing forward (toward +Y, the
# way it faces); about Z it turns.
STILL = {}
idle = Clip("idle")
idle.key(0, STILL, "CONSTANT")
idle.key(38, STILL, "CONSTANT")
# A twitch: two frames, then as if nothing happened.
idle.key(40, over(STILL, head=(6, 18, 22), neck=(0, 6, 0)), "CONSTANT")
idle.key(42, STILL, "CONSTANT")
idle.key(70, STILL, "CONSTANT")
idle.key(71, over(STILL, hand_r=(-25, 0, 0)), "CONSTANT")
idle.key(73, STILL, "CONSTANT")
idle.close(90)

# The reach: a held frame, then the raised arm is down and out at you in two frames, the body pitched after it, the
# other arm coming up, the head cocked; it holds there, then as suddenly draws back up.
lunge = {
    "upperarm_r": (-95, 0, -10), "lowerarm_r": (-8, 0, 0), "hand_r": (10, 0, 0),
    "upperarm_l": (40, -8, 0), "lowerarm_l": (25, 0, 0),
    "spine_02": (-10, 0, 0), "spine_03": (-12, 0, 0), "neck": (-8, 0, 0), "head": (-5, 12, 26),
    "root@loc": (0, 0.16, 0),
}
half = {k: tuple(x * 0.35 for x in v) for k, v in lunge.items()}
reach = Clip("reach", loop=False)
reach.key(0, STILL, "CONSTANT")
reach.key(2, half, "LINEAR")
reach.key(4, lunge, "CONSTANT")
reach.key(22, lunge, "LINEAR")
reach.key(26, over(lunge, head=(0, 0, 0)), "LINEAR")
reach.key(33, STILL, "CONSTANT")
rig.bake(sk, [idle, reach])

path = os.path.join(cook.ROOT, "content", "art", "models", "hollow.glb")
bpy.ops.object.select_all(action="DESELECT")
bpy.ops.export_scene.gltf(
    filepath=path, export_format="GLB", export_yup=True, export_apply=False,
    export_animations=True, export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_step=1,
    export_optimize_animation_size=False, export_anim_slide_to_zero=True, export_def_bones=False,
    export_extras=True, export_skins=True, export_morph=False, export_texcoords=True, export_normals=True,
    export_tangents=False, export_materials="EXPORT", export_image_format="NONE", export_cameras=False,
    export_lights=False, export_all_influences=False, export_reset_pose_bones=True)
print(f"[dt] hollow: {cook.tris([low] + eyes)} tris, {len(sk.bones)} bones, from gk-transi -> content/art/models/hollow.glb")
