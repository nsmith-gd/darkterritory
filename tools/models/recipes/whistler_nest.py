"""The Whistler's nest (GDD v1.1 App. A.4: "carried off at a run to its nest; there it paralyses them, and after ~20 s
eats them"; the crew chases on foot and has to find it): a scraped hollow off the line about 2.6 m across, ringed with what
it's dragged there. Sleepers prised up and leant together, a crewman's coat and boots, a smashed hand lamp, bones picked
clean and stacked, and over all of it the pale, wet strands it spins, strung between the sleepers, pooled glistening in the
bottom. Its origin is the hollow's middle, on the ground.

Axes: Blender +Z up. Modelled here and baked (tools/models/make).

    tools/models/build.sh whistler_nest
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


def materials():
    return {
        "earth": make.lib("ground_mud", 1.5, tint=(0.75, 0.68, 0.62), rough=0.9),
        "sleeper": make.lib("wood_sleeper", 2.0, tint=(0.62, 0.55, 0.48), rough=0.75),
        "coat": make.lib("coat_oilskin", 3.0, tint=(0.55, 0.5, 0.42), rough=0.8),
        "leather": make.lib("leather", 5.0, tint=(0.4, 0.32, 0.25), rough=0.7),
        "bone": make.lib("plaster_ruin", 10.0, tint=(0.92, 0.86, 0.74), rough=0.5),
        "silk": make.lib("sac", 2.0, tint=(1.05, 1.02, 0.95), rough=0.15),
        "iron": make.lib("rust_heavy", 5.0, tint=(0.42, 0.38, 0.35), rough=0.55, metal=0.6),
        "glass": make.lib("glass_dirty", 6.0, tint=(0.7, 0.62, 0.45), rough=0.2),
    }


def blob(centre, radii, material, name="blob", n=14, low=10, rot=None):
    out = None
    for segs, keep in ((low, bool(low)), (n, True)):
        if not keep:
            continue
        m = Matrix.Diagonal((*radii, 1))
        if rot is not None:
            m = rot @ m
        o = cook.uv_sphere(segs, max(4, segs // 2), 1, Matrix.Translation(Vector(centre)) @ m)
        o.data.materials.clear()
        o.data.materials.append(material)
        if segs == low and segs != n:
            o.name = name + "_low"
            make.LOW.append(o)
        else:
            out = make._finish(o, material, 0, 1, name)
    return out


def bone(a, b, r, m, name="bone"):
    a, b = Vector(a), Vector(b)
    return [make.cyl(a, b, r, m["bone"], n=8, bevel=0, name=name, low=5),
            blob(a, (r * 1.8,) * 3, m["bone"], name + "_end", n=8, low=0),
            blob(b, (r * 1.8,) * 3, m["bone"], name + "_end", n=8, low=0)]


def nest(m):
    p = []
    # The scraped hollow: a raised lip of churned earth round a dished floor.
    p.append(make.torus((0, 0, 0.02), (0, 0, 1), 1.15, 0.22, m["earth"], n=32, m=8, name="lip", low=(16, 5)))
    p.append(make.cyl((0, 0, -0.04), (0, 0, 0.02), 1.2, m["earth"], n=28, bevel=0.02, name="floor", r1=1.2, low=14))
    # Sleepers prised up and leant together over one side, a rough lean-to; two more lying across the lip.
    for k, (a, tilt) in enumerate(((0.3, 0.7), (0.75, 0.6), (1.2, 0.75), (1.65, 0.65))):
        d = Vector((math.cos(a), math.sin(a), 0))
        c = d * 0.85 + Vector((0, 0, 0.55))
        rot = Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(-tilt, 4, "Y")
        p.append(make.box(tuple(c), (0.11, 0.07, 0.62), m["sleeper"], bevel=0.01, name=f"leant_{k}", rot=rot))
    for k, (a, y) in enumerate(((3.6, 0.0), (4.6, 0.3))):
        p.append(make.box((math.cos(a) * 1.1, math.sin(a) * 1.1, 0.12), (1.0, 0.11, 0.07), m["sleeper"], bevel=0.01, name=f"across_{k}",
                          rot=Matrix.Rotation(a + math.pi / 2 + y, 4, "Z")))
    # What it's dragged here: a coat flung down in a heap, a boot, a smashed hand lamp.
    p.append(blob((-0.45, 0.35, 0.1), (0.45, 0.3, 0.12), m["coat"], "coat", rot=Matrix.Rotation(0.4, 4, "Z")))
    p.append(blob((-0.25, 0.55, 0.16), (0.2, 0.14, 0.09), m["coat"], "sleeve", rot=Matrix.Rotation(1.2, 4, "Z")))
    p.append(make.box((0.5, -0.5, 0.07), (0.13, 0.05, 0.07), m["leather"], bevel=0.03, name="boot", rot=Matrix.Rotation(0.8, 4, "Z")))
    p.append(make.cyl((0.58, -0.47, 0.07), (0.58, -0.47, 0.26), 0.05, m["leather"], n=10, bevel=0.01, name="boot_leg", low=6))
    p.append(make.cyl((0.2, -0.8, 0.05), (0.32, -0.75, 0.14), 0.07, m["iron"], n=8, bevel=0.005, name="lamp", low=6))
    for k in range(4):
        a = k * 1.6
        p.append(make.box((0.26 + 0.12 * math.cos(a), -0.86 + 0.1 * math.sin(a), 0.03), (0.04, 0.025, 0.004), m["glass"], bevel=0,
                          name="shard", low=False, rot=Matrix.Rotation(a, 4, "Z")))
    # Bones picked clean and stacked at the back: long bones crossed, ribs, a skull.
    for k in range(5):
        a = 2.4 + k * 0.22
        c = Vector((math.cos(a) * 0.6, math.sin(a) * 0.6, 0.06 + 0.035 * (k % 2)))
        d = Vector((math.cos(a + 1.3 + k), math.sin(a + 1.3 + k), 0.05))
        p += bone(c - d * 0.22, c + d * 0.22, 0.022, m, f"long_{k}")
    for k in range(6):
        a = -0.6 + k * 0.18
        c = Vector((-0.75, -0.15, 0.04))
        p.append(make.torus(c + Vector((0, k * 0.05, 0)), (0, 1, 0.2), 0.14, 0.009, m["bone"], n=14, m=4, name="rib", low=False))
    p.append(blob((-0.62, 0.05, 0.12), (0.09, 0.11, 0.09), m["bone"], "skull"))
    p.append(blob((-0.6, -0.04, 0.09), (0.05, 0.03, 0.03), m["earth"], "socket", n=8, low=0))
    # The strands: pale, wet ropes of it strung between the sleepers and the lip, and pooled in the bottom.
    pts = [Vector((math.cos(a) * 1.0, math.sin(a) * 1.0, 0.15)) for a in (0.2, 0.9, 1.5, 2.6, 3.4, 4.2, 5.0, 5.8)]
    tops = [Vector((math.cos(a) * 0.55, math.sin(a) * 0.55, 0.95)) for a in (0.4, 0.95, 1.4)]
    for i, a in enumerate(pts):
        b = tops[i % len(tops)]
        mid = (a + b) / 2 - Vector((0, 0, 0.18))
        for s, e in ((a, mid), (mid, b)):
            p.append(make.cyl(s, e, 0.012, m["silk"], n=6, bevel=0, name="strand", low=4))
    p.append(blob((0.15, 0.05, 0.03), (0.55, 0.42, 0.05), m["silk"], "pool"))
    p.append(blob((0.35, 0.3, 0.06), (0.16, 0.12, 0.08), m["silk"], "cocoon"))
    return p


cook.reset()
make.LOW.clear()
make.USED.clear()
make._mats.clear()
parts = [o for o in nest(materials()) if o is not None]
low = cook.bake_down(parts, "whistler_nest_low", 2400, colour=None, size=1024, cage=0.01, reach=0.04, low=list(make.LOW))[0]
cook.finish("whistler_nest", [low], budget=2400, grime=0.6, made=make.provenance("whistler_nest", "the Whistler's nest"))
