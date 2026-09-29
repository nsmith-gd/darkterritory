"""THE BOY'S ROOM. In one of the dead villages a house has lost its front wall, and from the line you can see into a
child's bedroom: the wardrobe, the toys on the floor, the pictures, a boy standing in the middle of it facing the thing
he drew, a hulking imaginary friend twice his height sitting against the wall. Everything is grey with dust and soot,
the walls broken off raggedly where the house came down, rafters fallen in across it. And the bedside lamp is still on.

"Boy Room" by Iman Aliakbar (CC BY 4.0), ruined: its palette drained to dust, the wardrobe knocked askew, a picture
fallen, the walls' tops broken away, fallen rafters and rubble in the library's timber and brick. 2.7 m to the ceiling
line, the open side toward the model's front (the engine's -Z, the way the town kit's houses face the line).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402

cook.reset()
objs = cook.load("gk-boyroom", "models/imaginary-friend-room/scene.plain.glb")
cook.fit(objs, height=2.7)
room = objs[0]

# The room's walls are at +Y (the pictures) and -X (the window): turn it so the back wall is at -Y, the open side
# toward the front (+Y).
cook.rotate(objs, 180)

# By material, so the wardrobe and a picture can be moved.
bpy.ops.object.select_all(action="DESELECT")
room.select_set(True)
bpy.context.view_layer.objects.active = room
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="MATERIAL")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in bpy.context.scene.objects if o.type == "MESH"]


def named(key):
    return [o for o in parts if o.data.materials and key in o.data.materials[0].name]


# The wardrobe leans off true against the wall, as if the floor gave under it.
for o in named("schrank"):
    olo, ohi = cook.bounds([o])
    pivot = Vector(((olo.x + ohi.x) / 2, olo.y, olo.z))
    cook.transform([o], Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(-7), 4, "Y") @ Matrix.Rotation(math.radians(4), 4, "X") @ Matrix.Translation(-pivot))
# A picture hangs crooked from one nail.
for o in named("pic"):
    olo, ohi = cook.bounds([o])
    nail = Vector(((olo.x + ohi.x) / 2, (olo.y + ohi.y) / 2, ohi.z))
    cook.transform([o], Matrix.Translation(nail) @ Matrix.Rotation(math.radians(24), 4, "Y") @ Matrix.Translation(-nail))
# Where the lamp is: its shade (on the dresser, 1.07, 0.88, 2.0 in the turned room before the tilt) is part of the
# furniture, so it's found where the tilt left it; the bulb a hand below the shade's middle.
lamp = None
near = [o.matrix_world @ v.co for o in named("schrank") for v in o.data.vertices]
near = [p for p in near if abs(p.z - 1.98) < 0.14 and (Vector((p.x, p.y, 0)) - Vector((1.07, 0.88, 0))).length < 0.35]
if near:
    lamp = sum(near, Vector()) / len(near) + Vector((0, 0, -0.06))

lo3, hi3 = cook.bounds(parts)

# The house around it: its outer walls in the library's plaster, standing up raggedly past the room's ceiling where
# the storey above broke away, and falling to nothing toward the open front.
plaster = cook.library_material("plaster_ruin", 0.05)
shell = []
back = cook.grid(hi3.x - lo3.x + 0.3, 4.2, 26, plaster, tile=2.0, name="shell_back")
cook.transform([back], Matrix.Rotation(math.pi, 4, "Z"))
cook.move([back], ((lo3.x + hi3.x) / 2, lo3.y - 0.04, 2.1))
side = cook.grid(hi3.y - lo3.y + 0.3, 4.2, 26, plaster, tile=2.0, name="shell_side")
cook.transform([side], Matrix.Rotation(-math.pi / 2, 4, "Z"))
cook.move([side], (hi3.x + 0.04, (lo3.y + hi3.y) / 2, 2.1))
shell = [back, side]
# Plaster inside and out (the broken upper storey is seen from the line, from inside the room).
for o in list(shell):
    inner = cook.duplicate([o])[0]
    inner.data.flip_normals()
    shell.append(inner)


def standing(c):
    front = min(1.0, max(0.0, (c.y - lo3.y) / max(hi3.y - lo3.y, 1e-6)))
    line = 4.1 - 1.6 * front ** 1.3 + 0.45 * cook.noise3(c, 31, 1.4) + 0.2 * cook.noise3(c, 32, 5)
    return c.z < line


cook.cut(shell, standing)

# Fallen rafters across it and rubble along the broken front edge, in the library's timber and brick.
wood = cook.library_material("wood_grey", 0.05)
brick = cook.library_material("brick_soot", 0.05)
extra = []
cx, cy = (lo3.x + hi3.x) / 2, (lo3.y + hi3.y) / 2
for i, (x, z0, z1, yaw) in enumerate(((-0.6, 2.5, 0.3, 8), (0.4, 2.2, 0.9, -14), (1.1, 1.6, 0.05, 20))):
    b = cook.cube((cx + x, cy, (z0 + z1) / 2 + 0.4), (0.07, (hi3.y - lo3.y) * 0.55, 0.09), f"rafter_{i}")
    ang = math.atan2(z0 - z1, (hi3.y - lo3.y) * 1.1)
    cook.transform([b], Matrix.Translation(Vector((cx + x, cy, (z0 + z1) / 2))) @ Matrix.Rotation(ang, 4, "X")
                   @ Matrix.Rotation(math.radians(yaw), 4, "Z") @ Matrix.Translation(-Vector((cx + x, cy, (z0 + z1) / 2))))
    b.data.materials.append(wood)
    extra.append(b)
for i in range(14):
    x = lo3.x + (hi3.x - lo3.x) * ((i * 0.618) % 1)
    y = lo3.y + 0.15 + 0.5 * ((i * 0.381) % 1)
    s = 0.08 + 0.14 * ((i * 0.713) % 1)
    r = cook.cube((x, y, s * 0.6), (s, s * 0.7, s * 0.5), f"rubble_{i}")
    cook.transform([r], Matrix.Translation(Vector((x, y, s * 0.6))) @ Matrix.Rotation(i * 1.3, 4, "Z") @ Matrix.Rotation(i * 0.7, 4, "X") @ Matrix.Translation(-Vector((x, y, s * 0.6))))
    r.data.materials.append(brick)
    extra.append(r)
for o in extra:
    # Metre UVs from the box's own axes, so the library layers tile at their size.
    uv = o.data.uv_layers.new(name="UVMap")
    for poly in o.data.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            p = o.data.vertices[o.data.loops[li].vertex_index].co
            a = (p.y, p.z) if abs(n.x) > 0.5 else (p.x, p.z) if abs(n.y) > 0.5 else (p.x, p.y)
            uv.data[li].uv = (a[0] / 1.2, a[1] / 1.2)


def dust(d, part):
    # Every colour drained toward the grey of dust and soot, a little brown left, the whole of it darker. The boy is
    # ash-pale, like something left out in the fall of it; the thing he drew is soot-black but for its eyes.
    lum = d.mean(-1, keepdims=True)
    grey = lum * np.array([0.95, 0.9, 0.84], np.float32)
    if "boy" in part:
        return grey * 0.55 + 0.06
    if "mansta" in part:
        eyes = np.clip((lum - 0.55) * 6, 0, 1)
        return grey * 0.06 * (1 - eyes) + np.array([0.5, 0.47, 0.4], np.float32) * eyes
    return (grey * 0.8 + d * 0.2) * 0.55


sockets = {"lamp": lamp} if lamp is not None else None
cook.finish("boy_room", parts + extra + shell, budget=26000, grade=dust, grime=0.8, sockets=sockets)
