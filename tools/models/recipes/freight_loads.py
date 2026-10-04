"""Facility freight with no case of its own yet (GDD §19 "coal, timber, gunpowder, chemicals, food, medicine, machine
parts: physically aboard and readable"), the size of a crate's body in the sim (0.88 m), centred on it:

  * freight_carboys: the chemical works' (CargoKind.Chemicals): four glass carboys of something green-black in a slatted
    crate, packed in straw, their necks stoppered and wired, a skull-and-bones POISON plate on the crate's side;
  * freight_ore: the mine head's (CargoKind.Ore): an iron-hooped wooden tub heaped with broken ore, lumps over the rim;
  * freight_comet: comet-derived material (CargoKind.Comet, GDD §19 "attracts everything"): a lead casket, banded and
    riveted, its lid cracked where what's inside has swollen, the cracks a sick green (the scene lights the car with it);
  * freight_timber: sawn planks in a strapped bundle on bearers (goods);
  * freight_medicine: a white-painted chest, a red cross on its lid and sides, MEDICAL STORES stencilled (goods).

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
        "lead": make.lib("iron_plate", 3.0, tint=(0.55, 0.56, 0.58), rough=0.6, metal=0.4),
        "band": make.lib("rust_heavy", 4.0, tint=(0.42, 0.38, 0.35), rough=0.55, metal=0.6),
        "comet": make.flat("comet_glow", (0.55, 0.85, 0.35), rough=0.3),
        "mineral": make.lib("mineral_growth", 4.0, tint=(0.75, 1.0, 0.7), rough=0.3),
        "plank": make.lib("wood_floor", 2.0, tint=(0.95, 0.85, 0.7), rough=0.7),
        "strap": make.lib("rust_heavy", 6.0, tint=(0.35, 0.32, 0.3), rough=0.5, metal=0.6),
        "white": make.lib("paint_olive", 3.0, tint=(1.5, 1.5, 1.4), rough=0.6),
        "red": make.flat("cross_red", (0.55, 0.06, 0.05), rough=0.6),
    }


def lump(at, r, material, seed, name="lump"):
    """A broken lump of rock: a low sphere knocked about by noise."""
    # Made at the origin and its mesh moved onto `at`, as make.pipe's joints are: zeroing the location of one added
    # at `at` leaves matrix_world holding it until the next depsgraph update.
    bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=r)
    o = bpy.context.view_layer.objects.active
    for v in o.data.vertices:
        d = v.co.normalized()
        v.co = v.co * (0.75 + 0.5 * cook.noise3(d * 2.0, seed, 3)) * Vector((1, 1, 0.75))
    o.data.transform(Matrix.Translation(Vector(at)))
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
            o = cook.uv_sphere(20, 12, 0.17, Matrix.Translation(c) @ Matrix.Diagonal((1, 1, 1.15, 1)))
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
    o = cook.uv_sphere(20, 10, H - 0.07, Matrix.Translation((0, 0, H - 0.2)) @ Matrix.Diagonal((1, 1, 0.45, 1)))
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


def comet(m):
    p = []
    w, d, h = H - 0.05, H - 0.12, H - 0.08
    # The casket: lead sheet over a box, its lid a little proud, iron bands round it and rivets along them.
    p.append(make.box((0, 0, -0.06), (w, d, h - 0.06), m["lead"], bevel=0.02, name="casket"))
    p.append(make.box((0, 0, h - 0.06), (w + 0.015, d + 0.015, 0.05), m["lead"], bevel=0.015, name="lid"))
    for x in (-0.25, 0.25):
        p.append(make.box((x, 0, -0.02), (0.03, d + 0.012, h), m["band"], bevel=0.005, name="band"))
        for k in range(5):
            z = -h + 0.12 + k * (2 * h - 0.2) / 4
            for sy in (-1, 1):
                p.append(make.cyl((x, sy * (d + 0.012), z), (x, sy * (d + 0.02), z), 0.008, m["band"], n=6, bevel=0, name="rivet", low=0))
    # The lid split along its length where what's inside has swollen: a crack and the mineral grown up through it,
    # the green showing in the seam.
    for k in range(7):
        x = -0.32 + k * 0.1
        y = 0.04 * math.sin(k * 1.9)
        p.append(make.box((x, y, h + 0.0), (0.05, 0.012, 0.012), m["comet"], bevel=0, name="seam", low=False,
                          rot=Matrix.Rotation(0.3 * math.sin(k * 2.3), 4, "Z")))
    for k in range(5):
        x = -0.28 + k * 0.14
        p.append(lump((x, 0.04 * math.cos(k), h + 0.03), 0.03 + 0.012 * (k % 3), m["mineral"], 70 + k, name="growth"))
    p.append(make.stencil("DO NOT OPEN", (0, -d - 0.016, 0.05), (0, -1, 0), (0, 0, 1), 0.05, m["ink_white"]))
    p.append(make.stencil("LEAD LINED", (0, -d - 0.016, -0.1), (0, -1, 0), (0, 0, 1), 0.035, m["ink_white"], name="lined"))
    return p


def timber(m):
    p = []
    # Sawn planks in a bundle on two bearers, strapped twice.
    for k in range(5):
        for j in range(4):
            z = -H + 0.12 + k * 0.13
            y = -H + 0.12 + j * 0.205
            p.append(make.box((0.01 * ((k + j) % 3 - 1), y, z), (H - 0.03, 0.095, 0.058), m["plank"], bevel=0.006, name="plank"))
    for x in (-0.3, 0.3):
        p.append(make.box((x, 0, -H + 0.03), (0.06, H - 0.02, 0.04), m["slat"], bevel=0.006, name="bearer"))
        for side in (-1, 1):
            p.append(make.box((x, side * (H - 0.005), -0.02), (0.02, 0.004, 0.4), m["strap"], bevel=0.002, name="strap_side", low=False))
        p.append(make.box((x, 0, 0.42), (0.02, H - 0.005, 0.004), m["strap"], bevel=0.002, name="strap_top", low=False))
    return p


def medicine(m):
    p = []
    w, d, h = H - 0.08, H - 0.16, H - 0.18
    p.append(make.box((0, 0, -0.1), (w, d, h), m["white"], bevel=0.015, name="chest"))
    p.append(make.box((0, 0, -0.1 + h + 0.02), (w + 0.01, d + 0.01, 0.025), m["white"], bevel=0.01, name="lid"))
    for sx in (-1, 1):
        p.append(make.box((sx * (w + 0.02), 0, 0.0), (0.02, 0.09, 0.025), m["band"], bevel=0.005, name="handle"))
    # The red cross on the lid and the long sides, and the stencil.
    top = -0.1 + h + 0.047
    p.append(make.box((0, 0, top), (0.16, 0.05, 0.003), m["red"], bevel=0, name="cross_a", low=False))
    p.append(make.box((0, 0, top), (0.05, 0.16, 0.003), m["red"], bevel=0, name="cross_b", low=False))
    for sy in (-1, 1):
        p.append(make.box((-0.12, sy * (d + 0.003), -0.05), (0.09, 0.003, 0.03), m["red"], bevel=0, name="side_a", low=False))
        p.append(make.box((-0.12, sy * (d + 0.003), -0.05), (0.03, 0.003, 0.09), m["red"], bevel=0, name="side_b", low=False))
        p.append(make.stencil("MEDICAL STORES", (0.14, sy * (d + 0.006), -0.05), (0, sy, 0), (0, 0, 1), 0.03, m["ink_black"], name="stores"))
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
    "freight_comet": lambda: build("freight_comet", comet, "facility freight: comet-derived material in a lead casket", 900),
    "freight_timber": lambda: build("freight_timber", timber, "goods: a strapped bundle of sawn timber", 700),
    "freight_medicine": lambda: build("freight_medicine", medicine, "goods: a chest of medical stores", 700),
}
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
