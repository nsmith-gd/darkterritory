"""THE PORTRAIT. In a third house the front has come away from a photographer's studio, and from the line you can see
the sitting it was set up for: a small boy in his good clothes propped upright in a tufted chair in front of the
painted backdrop, a posing stand's iron clamp behind his head to hold it up, his hands put in his lap. He is dead,
the way the children in memorial photographs were, and the bellows camera on its tripod is still pointed at him.
A candle burns on a crate beside the chair.

A bash of four sources and the texture library:
  * the Boy Room's boy (Iman Aliakbar, CC BY 4.0; the same child as the Soot children), posed seated at full
    resolution (tools/models/figures.py) and baked down, waxy pale;
  * the Khronos samples' Antique Camera (Maximillan Kamps for UX3D, CC0) and Chair Damask Purplegold (Eric Chadwick
    for Wayfair, CC BY 4.0), dust over both, the damask drained to a dead mauve;
  * the candle from their Glass Hurricane Candle Holder (Eric Chadwick for Wayfair, CC BY 4.0), its glass gone;
  * the room: the library's broken plaster, sooted brick and floorboards (cook.ruined_shell), a torn backdrop
    painted mottled grey (the library's stained render), an iron posing stand, a crate of its timber.

The open side toward the model's front (the engine's -Z, facing the line). Its socket "lamp" is the candle's flame.

    python3 tools/models/fetch.py gk-boyroom khronos-camera khronos-damask khronos-candle && tools/models/build.sh portrait_room
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import figures  # noqa: E402
import rig  # noqa: E402

HALF, DEPTH = 2.0, 3.5
cook.reset()


def facing(objs, frm, to):
    """Turns things that face -Y (as these sources do) about their own centre to face from `frm` toward `to`."""
    d = Vector((to[0] - frm[0], to[1] - frm[1], 0)).normalized()
    ang = math.atan2(d.x, -d.y)
    cook.transform(objs, Matrix.Translation(Vector((frm[0], frm[1], 0))) @ Matrix.Rotation(ang, 4, "Z"))
    return ang


def at_floor(objs):
    lo, hi = cook.bounds(objs)
    cook.move(objs, (-(lo.x + hi.x) / 2, -(lo.y + hi.y) / 2, -lo.z))


CHAIR_AT, CAMERA_AT, CRATE_AT = (0.35, 2.45), (-0.95, 0.95), (1.2, 2.25)

# ----------------------------------------------------------------------------------------------------------------
# The chair, turned to the camera.
chair = cook.load("khronos-damask", "Models/ChairDamaskPurplegold/glTF-Binary/ChairDamaskPurplegold.glb")
label = [o for o in chair if "label" in o.name]  # the maker's label under the seat
chair = [o for o in chair if o not in label]
cook.delete(label)
cook.fit(chair, height=0.84)
at_floor(chair)
seat = [o for o in chair if "seat-fabric" in o.name]
sco = np.concatenate([figures.verts(o) for o in seat])
seat_top = float(np.percentile(sco[:, 2], 92))
seat_back = float(sco[:, 1].max())  # the seat's back edge (the chair faces -Y)
cook.decimate(chair, 3200)

# The boy, sat down: thighs forward on the seat, the legs hanging, slumped, the head fallen to one side the way the
# clamp couldn't quite hold it; his hands put in his lap.
boy, stand = figures.boy()
SEATED = {
    "pelvis": (4, 0, 0), "spine_01": (-6, 0, 0), "spine_02": (-6, 0, 0), "spine_03": (-4, 3, 0),
    "neck": (-14, 8, 4), "head": (-30, 22, 16),
    "thigh_l": (92, -3, 0), "calf_l": (-88, 0, 0), "foot_l": (10, 0, 0),
    "thigh_r": (88, 4, 0), "calf_r": (-84, 0, 0), "foot_r": (8, 0, 0),
}
for s in ("l", "r"):
    knee, hip = rig.pose_points(stand, SEATED, [(f"calf_{s}", "head"), (f"thigh_{s}", "head")])
    target = hip + (knee - hip) * 0.55 + Vector((0.03 if s == "l" else -0.03, 0, 0.07))
    SEATED = rig.reach(stand, SEATED, f"upperarm_{s}", f"lowerarm_{s}", target, elbow_axis=0, bend=1)
    SEATED[f"hand_{s}"] = (30, 0, (1 if s == "l" else -1) * 20)
J, world, _ = figures.pose(boy, stand, SEATED, floor=False)
bco = figures.verts(boy)
pel = J["pelvis"][0]
under = bco[(np.hypot(bco[:, 0] - pel.x, bco[:, 1] - pel.y) < 0.13) & (bco[:, 2] < pel.z)]
sit_z = float(under[:, 2].min())
back_y = float(bco[bco[:, 2] < pel.z + 0.25][:, 1].min())  # his back (he faces +Y)
# Onto the seat: turned to face the chair's way (-Y), bottom on the cushion, back to its back.
cook.transform([boy], Matrix.Rotation(math.pi, 4, "Z"))
cook.move([boy], (0, seat_back - 0.08 - (-back_y), seat_top - sit_z - 0.015))
head = Matrix.Rotation(math.pi, 4, "Z") @ J["head"][0] + Vector((0, seat_back - 0.08 + back_y, seat_top - sit_z - 0.015))


def ivory(p):
    return np.clip((cook.noise_np(p * np.array([40, 40, 8]), 95) + 0.2) * 1.5, 0, 1)


def pale(base, ao, m):
    # The skin gone the ivory of old photographs; his good dark clothes greyed with the room's dust.
    lum = base.mean(-1, keepdims=True)
    skin = np.clip((base[..., 0:1] - base[..., 2:3]) * 8 - 0.2, 0, 1) * np.clip(lum * 6, 0, 1)
    wax = (0.45 + 0.4 * lum) * np.array([0.6, 0.58, 0.54], np.float32)
    cloth = lum * 0.3 * np.array([0.85, 0.83, 0.8], np.float32) + 0.012
    c = (wax * skin + cloth * (1 - skin)) * (0.35 + 0.65 * ao ** 1.6)[..., None]
    return c * (1 - 0.15 * m["ivory"][..., None])


boy = cook.bake_down([boy], "portrait_boy", 4000, colour=None, size=1024, masks={"ivory": ivory}, paint=pale)[0]
sitter = chair + [boy]
lo, hi = cook.bounds(chair)
chair_centre = ((lo.x + hi.x) / 2, (lo.y + hi.y) / 2)

# The posing stand: an iron rod up behind the chair on a three-footed base, a clamp at the back of his head.
iron = cook.library_material("rust_heavy", 0.3)
stand_parts = []
rod_x, rod_y = head.x, seat_back + 0.12
stand_parts.append(cook.cube((rod_x, rod_y, head.z / 2), (0.012, 0.012, head.z / 2), "rod"))
for k in range(3):
    a = k * 2 * math.pi / 3
    foot = cook.cube((rod_x + math.sin(a) * 0.14, rod_y + math.cos(a) * 0.14, 0.012), (0.012, 0.15, 0.01), f"foot_{k}")
    piv = Vector((rod_x, rod_y, 0.012))
    cook.transform([foot], Matrix.Translation(piv) @ Matrix.Rotation(-a, 4, "Z") @ Matrix.Translation(-piv))
    stand_parts.append(foot)
for sx in (-1, 1):
    stand_parts.append(cook.cube((head.x + sx * 0.06, (rod_y + head.y) / 2 + 0.02, head.z), (0.008, abs(rod_y - head.y) / 2, 0.01), f"clamp_{sx}"))
for o in stand_parts:
    o.data.materials.append(iron)
cook.box_uv(stand_parts, 0.5)

# The whole sitting turned to the camera.
cook.move(sitter + stand_parts, (-chair_centre[0], -chair_centre[1], 0))
facing(sitter + stand_parts, CHAIR_AT, CAMERA_AT)

# The camera, pointed at him.
camera = cook.load("khronos-camera", "Models/AntiqueCamera/glTF-Binary/AntiqueCamera.glb")
cook.fit(camera, height=1.55)
at_floor(camera)
cook.decimate(camera, 6000)
facing(camera, CAMERA_AT, CHAIR_AT)

# The candle (its glass, and the logos on it, gone) on a crate by the chair, lit.
candle = cook.load("khronos-candle", "Models/GlassHurricaneCandleHolder/glTF-Binary/GlassHurricaneCandleHolder.glb")
glass = [o for o in candle if "glass" in o.name]
cook.delete(glass)
candle = [o for o in candle if o not in glass]
cook.fit(candle, height=0.3)
cook.decimate(candle, 900)
crate = cook.cube((CRATE_AT[0], CRATE_AT[1], 0.22), (0.24, 0.2, 0.22), "crate")
crate.data.materials.append(cook.library_material("wood_crate", 0.05))
cook.box_uv([crate], 0.5)
at_floor(candle)
cook.move(candle, (CRATE_AT[0], CRATE_AT[1], 0.44))
clo, chi = cook.bounds(candle)
wick = Vector((CRATE_AT[0], CRATE_AT[1], chi.z + 0.02))
flame = cook.eyes_at([wick], 0.014, colour=(1.0, 0.72, 0.38), name="flame")

# The backdrop: a painted cloth hung across the back of the room, sagging, its hem torn.
cloth = cook.grid(2.7, 2.5, 16, cook.library_material("concrete_stain", 0.02), tile=1.4, name="backdrop")
cook.transform([cloth], Matrix.Rotation(math.pi, 4, "Z"))
cook.move([cloth], (0.2, DEPTH - 0.22, 1.3))
cook.deform([cloth], lambda p: (p.x, p.y - 0.06 * math.cos(p.x * 2.3) - 0.04 * (2.55 - p.z) * cook.noise3(p, 97, 1.5), p.z))
cook.cut([cloth], lambda c: c.z > 0.2 + 0.25 * (cook.noise3(c, 98, 2.5) + 1) * (0.5 + 0.5 * math.sin(c.x * 1.7)))

shell, standing = cook.ruined_shell(HALF, DEPTH, seed=61)
everything = shell + sitter + stand_parts + camera + candle + [crate, cloth] + flame
# Turned so the open side faces the model's front (+Y), centred on the room.
R = Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation(Vector((0, -DEPTH / 2, 0)))
cook.transform(everything, R)
wick = R @ wick


def grade(d, part):
    lum = d.mean(-1, keepdims=True)
    grey = lum * np.array([0.95, 0.9, 0.84], np.float32)
    if "portrait_boy" in part:
        return d
    if part in ("fabric",):
        # Damask drained to a dead mauve under dust.
        return (grey * 0.75 + d * 0.25) * np.array([0.9, 0.8, 0.86], np.float32) * 1.05
    if "Candle" in part:
        return d * 0.8
    return (grey * 0.7 + d * 0.3) * 0.6


cook.finish("portrait_room", everything, budget=29500, grade=grade, grime=0.7, sockets={"lamp": wick})
