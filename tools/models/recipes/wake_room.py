"""THE WAKE. In another of the dead villages a house's front has come away from its parlour, and from the line you can
see the body laid out in it: a woman in a long gown on a bier of planks and trestles in the middle of the room, under
a pendant lamp whose glass is all broken and whose bare bulbs are still, somehow, lit. The pictures on the wall behind
are hung with black crepe, as they were at a death; the sofa is pushed back against the wall with its cushions still
on it; the curtain hangs at the broken window; the ceiling is gone, and the walls broken off raggedly where the storey
above came down.

A bash of two sources and the texture library:
  * "Interior Scene" by Allay Design (CC BY 4.0): a modern living room, cut open on its long side and its far half
    taken away. Its furniture is kept, drained to dust: the glass knocked out of it, its plants dead and gone, its
    coffee table cleared for the bier. The heavy soft furnishings (sofa, cushions, curtain) are baked down to game
    meshes wearing their detail (cook.bake_down). Its walls and floor (lit by a baked atlas that only works from
    inside a closed box) are replaced by the library's: ruined plaster inside, sooted brick out, grey floorboards.
  * Harriet Hosmer's "Zenobia in Chains", scanned by Three D Scans: the chained queen, off her plinth and laid out
    on her back on the bier. Marble in the scan; here the pale body the room is for.

2.8 m to where the ceiling was; the open side toward the model's front (the engine's -Z, facing the line). Its
socket "lamp" is the pendant's bulbs (WorldArt hangs a light there).

    python3 tools/models/fetch.py gk-interior gk-hosmer && tools/models/build.sh wake_room
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402

cook.reset()
room = cook.load("gk-interior", "models/interior-scene/scene.plain.glb")[0]

# In the source's units the floor is at z -16.4 and the ceiling at 21; its walls are at x -28.3 and 31, the sofa's
# wall at y 31.6 and the far wall at -64.5. Keep the sofa's end, the near half: cut straight across at y -22.
FLOOR, CEIL, BACK, OPEN, LEFT, RIGHT = -16.4, 21.0, 31.6, -22.0, -28.3, 31.0
S = 2.8 / (CEIL - FLOOR)
bm = bmesh.new()
bm.from_mesh(room.data)
bmesh.ops.bisect_plane(bm, geom=bm.verts[:] + bm.edges[:] + bm.faces[:], dist=1e-4, plane_co=(0, OPEN, 0),
                       plane_no=(0, -1, 0), clear_outer=True)
bm.to_mesh(room.data)
bm.free()
room.data.update()

bpy.ops.object.select_all(action="DESELECT")
room.select_set(True)
bpy.context.view_layer.objects.active = room
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.separate(type="MATERIAL")
bpy.ops.object.mode_set(mode="OBJECT")
parts = [o for o in bpy.context.scene.objects if o.type == "MESH" and len(o.data.polygons)]


def mat(o):
    return o.data.materials[0].name if o.data.materials else ""


def base_colour(m):
    bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if m and m.use_nodes else None
    if bsdf is None:
        return np.array([0.5, 0.5, 0.5]), 1.0
    img, _ = cook._linked_image(bsdf.inputs["Base Color"])
    c = np.array(bsdf.inputs["Base Color"].default_value[:3])
    if img is not None:
        c = cook._image_array(img, 64)[..., :3].reshape(-1, 3).mean(0)
    alpha = bsdf.inputs["Alpha"].default_value
    if bsdf.inputs["Alpha"].is_linked or bsdf.inputs["Transmission Weight"].default_value > 0.5:
        alpha = 0.0
    return c, alpha


SHELL = ("Surface42", "Surface43", "Surface87")  # walls and ceiling, floor, cornice: the baked-atlas box
BULBS = ("bulbaiStandardSurface",)
doomed, bulbs = [], []
for o in parts:
    lo, hi = cook.bounds([o])
    c, alpha = base_colour(o.data.materials[0] if o.data.materials else None)
    green = c[1] > c[0] * 1.15 and c[1] > c[2] * 1.15
    table = lo.x > -7 and hi.x < 9 and lo.y > 1 and hi.y < 16 and hi.z < -7
    if any(k in mat(o) for k in BULBS):
        bulbs.append(o)
    elif mat(o).endswith(SHELL) or alpha < 0.6 or green or table:
        # The glass is broken out (the lamp's globes with it); the plants are dead and gone; the coffee table is
        # cleared away for the bier; the box it all stood in is replaced.
        doomed.append(o)
cook.delete(doomed)
parts = [o for o in parts if o not in doomed and o not in bulbs]

# Into metres: the floor on z = 0, the room centred across, its open side at y = 0 for now (turned at the end).
cx = (LEFT + RIGHT) / 2
M = Matrix.Scale(S, 4) @ Matrix.Translation(Vector((-cx, -OPEN, -FLOOR)))
cook.transform(parts + bulbs, M)
depth, half = (BACK - OPEN) * S, (RIGHT - LEFT) / 2 * S

# The pendant's bare bulbs, lit: small emissive spheres where each bulb was.
bpy.ops.object.select_all(action="DESELECT")
for o in bulbs:
    o.select_set(True)
bpy.context.view_layer.objects.active = bulbs[0]
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="SELECT")
bpy.ops.mesh.separate(type="LOOSE")
bpy.ops.object.mode_set(mode="OBJECT")
pieces = [o for o in bpy.context.scene.objects if o.type == "MESH" and any(k in mat(o) for k in BULBS)]
glass = []
for o in pieces:
    lo, hi = cook.bounds([o])
    glass.append((lo + hi) / 2)
cook.delete(pieces)
# One per bulb: the loose parts' centres, merged where several are parts of the same bulb.
at = []
for p in sorted(glass, key=lambda g: (round(g.x, 3), round(g.y, 3), round(g.z, 3))):
    if all((p - q).length > 0.06 for q in at):
        at.append(p)
lights = cook.eyes_at(at, 0.028, colour=(1.0, 0.74, 0.45), name="bulb")
lamp_at = sum(at, Vector()) / len(at)

lamp_parts = [o for o in parts if "Surface22" in mat(o)]  # its frame and cord
heavy = sorted((o for o in parts if o not in lamp_parts and cook.tris([o]) > 4000), key=lambda o: -cook.tris([o]))
rest = [o for o in parts if o not in lamp_parts and o not in heavy]
cook.decimate(lamp_parts, 900)
cook.decimate(rest, 7000)
# The heavy soft things baked down to game meshes wearing their own detail.
baked = []
for i, o in enumerate(heavy):
    target = int(min(4000, max(600, cook.tris([o]) * 0.03)))
    baked += cook.bake_down([o], f"wake_soft_{i}", target, colour=None, size=512)
parts = rest + baked + lamp_parts


# The house around it: its walls broken off raggedly where the storey above came down, the window's hole left in the
# right-hand wall (where the source's window is: y -2.4..20.4, z -5.6..18.5 in its units).
window = (((-2.4 - OPEN) * S, (20.4 - OPEN) * S), ((-5.6 - FLOOR) * S, (18.5 - FLOOR) * S))
shell, standing = cook.ruined_shell(half, depth, window=window)
T = 0.22

# Nothing through the back wall (a cupboard of the next room's stood against it).
cook.cut([o for o in parts if o not in lamp_parts], lambda c: standing(c) and c.y < depth - 0.01)

# The body: Zenobia off her plinth (the round base at the scan's +Y end), laid on her back along the room.
queen = cook.load("gk-hosmer", "models/threedscans/Hosmer.plain.glb")
cook.fit(queen, size=1.78)
qlo, qhi = cook.bounds(queen)
cook.cut(queen, lambda c: c.y < qhi.y - 0.075)


def dust(p):
    return np.clip(cook.noise_np(p * np.array([9, 9, 30]), 61) * 0.8 + 0.3, 0, 1)


def pale(base, ao, m):
    # Marble gone the grey-white of skin that's been dead a day: the folds of the gown filled with grime and dust.
    return base * (0.25 + 0.75 * ao ** 1.8)[..., None] * (1 - 0.35 * m["dust"][..., None])


body = cook.bake_down(queen, "wake_body", 5000, colour=(0.66, 0.645, 0.62), size=1024, masks={"dust": dust}, paint=pale)[0]

# The bier: two trestles and three planks of the library's grey timber, under the lamp.
wood = cook.library_material("wood_grey", 0.05)
bx, by, top = lamp_at.x, lamp_at.y, 0.62
bier = []
for i, dy in enumerate((-0.19, 0.0, 0.19)):
    bier.append(cook.cube((bx, by + dy, top - 0.025), (1.02, 0.09, 0.025), f"plank_{i}"))
for i, dx in enumerate((-0.72, 0.72)):
    for j, sy in enumerate((-1, 1)):
        leg = cook.cube((bx + dx, by + sy * 0.2, (top - 0.05) / 2), (0.035, 0.035, (top - 0.05) / 2 + 0.03), f"leg_{i}{j}")
        piv = Vector((bx + dx, by + sy * 0.2, top - 0.05))
        cook.transform([leg], Matrix.Translation(piv) @ Matrix.Rotation(0.12 * sy, 4, "X") @ Matrix.Translation(-piv))
        bier.append(leg)
    bier.append(cook.cube((bx + dx, by, top - 0.07), (0.04, 0.3, 0.03), f"bar_{i}"))
# Her, lengthwise along the bier (the scan's head end is its -Y: turned so she lies along the room's X).
cook.transform([body], Matrix.Rotation(math.pi / 2, 4, "Z"))
blo, bhi = cook.bounds([body])
cook.move([body], (bx - (blo.x + bhi.x) / 2, by - (blo.y + bhi.y) / 2, top - blo.z - 0.01))

# Joists across where the ceiling was, in the same timber: one the lamp still hangs from, one come down at an angle.
joists = []
for i, (y, z0, z1) in enumerate(((by, 2.8, 2.8), (depth * 0.82, 2.76, 2.76), (depth * 0.66, 2.7, 1.05))):
    j = cook.cube((0, y, (z0 + z1) / 2), (half + T, 0.06, 0.09), f"joist_{i}")
    piv = Vector((0, y, (z0 + z1) / 2))
    cook.transform([j], Matrix.Translation(piv) @ Matrix.Rotation(math.atan2(z0 - z1, 2 * half), 4, "Y") @ Matrix.Translation(-piv))
    joists.append(j)
for o in bier + joists:
    o.data.materials.append(wood)
cook.box_uv(bier + joists)

# The pictures: flat things hung on the back wall (thin in depth, off the floor, not wide), under black crepe.
CREPE = set()
for o in rest:
    if not o.data.polygons:
        continue
    lo, hi = cook.bounds([o])
    if hi.y - lo.y < 0.12 and hi.y > depth - 0.3 and lo.z > 0.5 and (hi.x - lo.x) < 1.6:
        CREPE.add(mat(o))

# Turned so the open side faces the model's front (+Y), centred on the room.
everything = [o for o in parts + shell + [body] + bier + joists + lights if o.data.polygons]
R = Matrix.Rotation(math.pi, 4, "Z") @ Matrix.Translation(Vector((0, -depth / 2, 0)))
cook.transform(everything, R)
lamp_at = R @ lamp_at


def grade(d, part):
    # Dust over everything; the white sofa and curtain yellowed and filthy; the pictures under black crepe.
    lum = d.mean(-1, keepdims=True)
    grey = lum * np.array([0.95, 0.9, 0.84], np.float32)
    if "wake_body" in part:
        return d
    if "wake_soft" in part:
        return grey * np.array([0.85, 0.78, 0.64], np.float32) * 0.62
    if part in CREPE:
        return grey * 0.04 + 0.004
    return (grey * 0.8 + d * 0.2) * 0.5


print(f"[dt] wake_room parts: furniture {cook.tris(rest)}, soft {cook.tris(baked)}, lamp {cook.tris(lamp_parts + lights)}, "
      f"shell {cook.tris(shell)}, body {cook.tris([body])}, timber {cook.tris(bier + joists)}, crepe {sorted(CREPE)}")
cook.finish("wake_room", everything, budget=29500, grade=grade, grime=0.7, sockets={"lamp": lamp_at})
