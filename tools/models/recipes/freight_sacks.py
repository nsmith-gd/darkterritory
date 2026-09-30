"""Facility freight (BodyKind.Cargo), grain from an elevator: sacks of it on a small pallet, lashed down with rope.
0.88 m, the size of the body in the sim. Centred on it."""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

H = 0.44
cook.reset()
wood = make.lib("wood_sleeper", 2.2, tint=(1.2, 1.05, 0.85))
hessian = make.lib("wool", 4.0, tint=(1.1, 0.92, 0.66), rough=0.95)
rope = make.lib("wool", 7.0, tint=(0.8, 0.68, 0.5), rough=0.95)
ink = make.flat("ink", (0.05, 0.04, 0.03), rough=0.9)
parts = []
# The pallet: three bearers and a deck of slats.
for x in (-0.36, 0, 0.36):
    parts.append(make.box((x, 0, -H + 0.045), (0.04, H, 0.045), wood, bevel=0.004, name="bearer"))
parts += make.planks((-H, -H, -H + 0.09), (H, H, -H + 0.115), 1, 6, wood, gap=0.02, name="slat", wobble=0.002, seed=41, low=True)


def sack(centre, half, yaw, seed, name):
    """A filled sack: a box gone soft (subdivided round), slumped, the grain settling it lumpy; a coarse twin."""
    def one(levels, low):
        bpy.ops.mesh.primitive_cube_add(size=1)
        o = bpy.context.view_layer.objects.active
        o.data.transform(Matrix.Diagonal((half[0] * 2, half[1] * 2, half[2] * 2, 1)))
        mod = o.modifiers.new("round", "SUBSURF")
        mod.levels = mod.render_levels = levels
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
        for v in o.data.vertices:
            p = v.co
            sag = 1 - 0.18 * max(0.0, p.z / half[2]) ** 2  # it slumps under its own weight
            lump = 1 + 0.06 * cook.noise3(p, seed, 9) + 0.03 * cook.noise3(p, seed + 1, 23)
            v.co = Vector((p.x * lump, p.y * lump, p.z * sag * lump))
        o.data.transform(Matrix.Translation(Vector(centre)) @ Matrix.Rotation(yaw, 4, "Z"))
        o.data.materials.append(hessian)
        for f in o.data.polygons:
            f.use_smooth = True
        o.name = name + ("_low" if low else "")
        if low:
            make.LOW.append(o)
        return o
    one(1, True)
    return one(3, False)


z0 = -H + 0.115
# Three courses of them, laid crosswise to each other the way sacks are stacked to stay put.
for course in range(3):
    z = z0 + 0.13 + course * 0.23
    along = course % 2 == 0
    for i, off in enumerate((-0.22, 0.22)):
        x, y = (0.0, off) if along else (off, 0.0)
        yaw = (0 if along else 1.5708) + 0.06 * cook.noise3((course, i, 0.3), 51, 1.0)
        parts.append(sack((x + 0.02 * cook.noise3((course, i, 1.1), 52, 1), y, z), (0.4, 0.2, 0.12), yaw, 50 + course * 3 + i, f"sack_{course}{i}"))
    if course == 2:
        for i, off in enumerate((-0.22, 0.22)):
            parts.append(make.stencil("GRAIN  50 KG", (off, 0, z + 0.115), (0, 0, 1), (0, 1, 0), 0.07, ink))
z_top = z0 + 0.13 + 2 * 0.23 + 0.12
# Lashing: two turns of rope over the top and down to the pallet.
for x in (-0.18, 0.18):
    for a in range(12):
        t0, t1 = a / 12 * math.pi, (a + 1) / 12 * math.pi
        p0 = (x, -0.45 * math.cos(t0), z0 + (z_top - z0 + 0.01) * math.sin(t0))
        p1 = (x, -0.45 * math.cos(t1), z0 + (z_top - z0 + 0.01) * math.sin(t1))
        parts.append(make.cyl(p0, p1, 0.008, rope, n=6, bevel=0, name="lash", low=0))
make.centre_on_origin(parts)
# Shorter than its body: its pallet on the body's floor, not floating in its middle.
lo, _ = cook.bounds(parts)
cook.move(parts + make.LOW, (0, 0, -H - lo.z))
low = cook.bake_down(parts, "freight_sacks_low", 900, colour=None, size=1024, cage=0.006, reach=0.02, low=make.LOW)[0]
cook.finish("freight_sacks", [low], budget=1400, grime=0.5, made=make.provenance("freight_sacks", "facility freight: grain sacks"))
