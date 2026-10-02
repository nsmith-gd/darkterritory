"""Facility freight with no case of its own yet (GDD §19 "coal, timber, gunpowder, chemicals, food, medicine, machine
parts: physically aboard and readable"), the size of a crate's body in the sim (0.88 m), centred on it:

  * freight_carboys: the chemical works' (CargoKind.Chemicals): four glass carboys of something green-black in a slatted
    crate, packed in straw, their necks stoppered and wired, a skull-and-bones POISON plate on the crate's side;
  * freight_ore: the mine head's (CargoKind.Ore): an iron-hooped wooden tub heaped with broken ore, lumps over the rim.

Axes: Blender +Z up. Modelled here and baked (tools/models/make).

    tools/models/build.sh freight_loads
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

H = 0.44


def materials():
    return {
        "crate": make.lib("wood_crate", 2.0, tint=(0.88, 0.8, 0.68), rough=0.65),
        "slat": make.lib("wood_sleeper", 3.0, tint=(0.75, 0.62, 0.48), rough=0.7),
        "straw": make.lib("grass_card", 6.0, tint=(1.1, 0.95, 0.65), rough=0.95),
        "glass": make.lib("glass_dirty", 3.0, tint=(0.35, 0.45, 0.32), rough=0.15),
        "cork": make.lib("leather", 8.0, tint=(0.55, 0.4, 0.28), rough=0.8),
        "wire": make.lib("rust_heavy", 6.0, tint=(0.45, 0.4, 0.36), rough=0.5, metal=0.6),
        "plate": make.lib("paint_oxide", 4.0, tint=(1.1, 0.5, 0.38), rough=0.5),
        "ink_white": make.flat("ink_white", (0.8, 0.78, 0.72), rough=0.8),
        "ink_black": make.flat("ink_black", (0.05, 0.045, 0.04), rough=0.8),
        "tub": make.lib("wood_sleeper", 2.5, tint=(0.62, 0.5, 0.4), rough=0.7),
        "hoop": make.lib("rust_heavy", 4.0, tint=(0.4, 0.36, 0.33), rough=0.55, metal=0.6),
        "ore": make.lib("slag", 3.0, tint=(0.75, 0.6, 0.5), rough=0.85),
        "coal": make.lib("coal", 3.0, rough=0.6),
    }


def lump(at, r, material, seed, name="lump"):
    """A broken lump of rock: a low sphere knocked about by noise."""
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r, location=at)
    o = bpy.context.view_layer.objects.active
    for v in o.data.vertices:
        d = v.co.normalized()
        v.co = v.co * (0.75 + 0.5 * cook.noise3(d * 2.0, seed, 3)) * Vector((1, 1, 0.75))
    o.data.transform(Matrix.Translation(o.location))
    o.location = (0, 0, 0)
    o.data.materials.append(material)
    return make._finish(o, material, 0, 1, name)


def carboys(m):
    p = []
    # The crate: slats on all four sides with gaps you see the straw and glass through, a solid floor, corner posts.
    p.append(make.box((0, 0, -H + 0.02), (H, H, 0.02), m["crate"], bevel=0.006, name="floor"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.box((sx * (H - 0.03), sy * (H - 0.03), -0.04), (0.03, 0.03, H - 0.04), m["crate"], bevel=0.006, name="post"))
    for k in range(4):
        z = -H + 0.12 + k * 0.2
        for sy in (-1, 1):
            p.append(make.box((0, sy * (H - 0.012), z), (H - 0.04, 0.012, 0.045), m["slat"], bevel=0.004, name="slat_x"))
        for sx in (-1, 1):
            p.append(make.box((sx * (H - 0.012), 0, z), (0.012, H - 0.04, 0.045), m["slat"], bevel=0.004, name="slat_y"))
    make.low_box((0, 0, -0.04), (H - 0.015, H - 0.015, H - 0.04), m["straw"], name="straw_shell")
    # The straw packed in round them, up to their shoulders.
    p.append(make.box((0, 0, -0.2), (H - 0.03, H - 0.03, 0.2), m["straw"], bevel=0.02, name="straw", low=False))
    # Four carboys: round-bellied, short-necked, a stopper wired down in each.
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = Vector((sx * 0.2, sy * 0.2, -0.05))
            bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=12, radius=0.17, location=c)
            o = bpy.context.view_layer.objects.active
            o.data.transform(Matrix.Translation(o.location) @ Matrix.Diagonal((1, 1, 1.15, 1)))
            o.location = (0, 0, 0)
            o.data.materials.append(m["glass"])
            p.append(make._finish(o, m["glass"], 0, 1, "carboy"))
            p.append(make.cyl(c + Vector((0, 0, 0.17)), c + Vector((0, 0, 0.27)), 0.04, m["glass"], n=12, bevel=0, name="neck", r1=0.035, low=6))
            p.append(make.cyl(c + Vector((0, 0, 0.27)), c + Vector((0, 0, 0.31)), 0.034, m["cork"], n=10, bevel=0.003, name="stopper", low=6))
            p.append(make.torus(c + Vector((0, 0, 0.28)), (0, 0, 1), 0.037, 0.003, m["wire"], name="wire", low=False))
    # The plate on its side: POISON under a skull and crossbones, and the works' mark.
    p.append(make.box((0, -H - 0.004, 0.1), (0.13, 0.004, 0.1), m["plate"], bevel=0.002, name="plate"))
    p.append(make.stencil("POISON", (0, -H - 0.009, 0.04), (0, -1, 0), (0, 0, 1), 0.04, m["ink_white"]))
    p.append(make.stencil("X", (0, -H - 0.009, 0.13), (0, -1, 0), (0, 0, 1), 0.08, m["ink_white"], name="bones"))
    p.append(make.stencil("o", (0, -H - 0.009, 0.155), (0, -1, 0), (0, 0, 1), 0.06, m["ink_white"], name="skull"))
    p.append(make.stencil("ACID  THIS WAY UP", (0, H + 0.004, 0.1), (0, 1, 0), (0, 0, 1), 0.035, m["ink_black"], name="back"))
    return p


def ore(m):
    p = []
    # The tub: staves round a slight bulge, two iron hoops, rope handles.
    staves = 18
    for k in range(staves):
        a = k / staves * math.tau
        d = Vector((math.cos(a), math.sin(a), 0))
        p.append(make.box(tuple(d * (H - 0.04)), (0.025, 0.074, H - 0.06), m["tub"], bevel=0.006, name="stave",
                          rot=Matrix.Rotation(a, 4, "Z")))
    p.append(make.cyl((0, 0, -H + 0.02), (0, 0, -H + 0.06), H - 0.06, m["tub"], n=20, bevel=0.004, name="bottom", low=12))
    p.append(make.cyl((0, 0, -H + 0.02), (0, 0, H - 0.06), H - 0.03, m["tub"], n=20, bevel=0, name="tub_shell", low=0))
    for z in (-0.25, 0.22):
        p.append(make.torus((0, 0, z), (0, 0, 1), H - 0.012, 0.012, m["hoop"], n=32, m=5, name="hoop", low=(16, 4)))
    # The ore heaped in it and over the rim: a mound and the lumps on it.
    bpy.ops.mesh.primitive_uv_sphere_add(segments=20, ring_count=10, radius=H - 0.07, location=(0, 0, H - 0.2))
    o = bpy.context.view_layer.objects.active
    o.data.transform(Matrix.Translation(o.location) @ Matrix.Diagonal((1, 1, 0.45, 1)))
    o.location = (0, 0, 0)
    o.data.materials.append(m["ore"])
    p.append(make._finish(o, m["ore"], 0, 1, "heap"))
    mound = make.cyl((0, 0, H - 0.21), (0, 0, H - 0.05), H - 0.07, m["ore"], n=16, bevel=0, name="heap_shell", r1=0.1, low=10)
    p.append(mound)
    rng = 7
    for k in range(26):
        a = (k * 2.399) % math.tau
        r = (H - 0.12) * math.sqrt((k + 0.5) / 26)
        z = H - 0.18 + 0.14 * (1 - r / H)
        p.append(lump((math.cos(a) * r, math.sin(a) * r, z), 0.04 + 0.03 * ((k * 37) % 7) / 7, m["ore"] if k % 4 else m["coal"], rng + k))
    return p


def build(name, fn, what, budget):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = [o for o in fn(materials()) if o is not None]
    make.centre_on_origin(parts)
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=1024, cage=0.006, reach=0.03, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.5, made=make.provenance("freight_loads", what))


PIECES = {
    "freight_carboys": lambda: build("freight_carboys", carboys, "facility freight: chemicals in carboys", 900),
    "freight_ore": lambda: build("freight_ore", ore, "facility freight: a tub of ore", 900),
}
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
