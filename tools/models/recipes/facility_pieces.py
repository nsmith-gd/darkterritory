"""The facilities' signature structures (GDD §18, §30), modelled, for StructureKit.Facility to set among its buildings,
so each kind reads by its shape from the line:

  * headframe (the mine head): an 18 m riveted steel headframe, its legs and back-stays, the two sheave wheels at
    the top, the cage's guides down the middle;
  * chem_tank (the chemical works): a riveted storage tank, 7 m across and 7 high, a ladder up its side, a
    railed walkway round its crown, a vent and a stained drip down from its valve;
  * watchtower (the military depot): a timber tower, 8 m to its sandbagged platform, a roof, a searchlight;
  * sandbags: a 3 m length of sandbag wall, four courses;
  * cattle_pen: a 3 m panel of timber pen fence, and cattle_ramp, the loading ramp up to a car's door;
  * signal_box (the switchyard): a timber box on a brick base, windows all round, its lever frame inside, stairs;
  * water_tower: a riveted tank on a timber trestle, its canvas spout swung out over the track.
  * grain_elevator (note 381): a concrete terminal elevator, 36 m along the line and 33 m to the bin floor's roof: four
    slip-formed silos in a row, their pour lines banded and rain-streaked from the top, a caged ladder up the end one;
    the bin-floor gallery along their tops, iron-clad, its windows dark but one; the leg house over the far end, the
    tallest thing for miles, with its own lit window; at the foot the receiving shed and its dark doorway; and the
    loading spout swung down from the gallery to 2.5 m off the track, its sock hanging. Its origin is on the ground at
    the silos' middle, 15 m off the line (StructureKit.Facility).

Axes (Blender): +Z up, the model's front (-Y, the engine's +Z) toward the line.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402


def materials():
    return {
        "paint": make.lib("paint_oxide", 0.4, tint=(0.75, 0.66, 0.6), rough=0.6),
        "steel": make.lib("iron_plate", 0.6, tint=(0.62, 0.62, 0.6), rough=0.45, metal=0.6),
        "rust": make.lib("rust_heavy", 0.5, tint=(0.6, 0.54, 0.5), rough=0.6, metal=0.4),
        "timber": make.lib("wood_sleeper", 0.9, tint=(0.95, 0.84, 0.7), rough=0.8),
        "grey": make.lib("wood_sleeper", 0.9, tint=(0.75, 0.74, 0.72), rough=0.85),
        "brick": make.lib("brick_soot", 0.5, rough=0.85),
        "sack": make.lib("wool", 3.0, tint=(0.75, 0.68, 0.5), rough=0.95),
        "glass": make.flat("window", (0.08, 0.09, 0.09), rough=0.1),
        "lit": make.flat("lit", (0.9, 0.7, 0.4), rough=0.5),
        "roof": make.lib("corrugated_iron", 0.6, tint=(0.7, 0.7, 0.68), rough=0.5),
        "stain": make.flat("stain", (0.08, 0.1, 0.05), rough=0.4),
        "concrete": make.lib("concrete_stain", 0.25, tint=(0.82, 0.82, 0.8), rough=0.9),
        "streak": make.flat("streak", (0.035, 0.034, 0.03), rough=0.85),
        "clad": make.lib("corrugated_iron", 0.5, tint=(0.62, 0.6, 0.58), rough=0.55, metal=0.3),
    }


def build(name, fn, what, size=1024, budget=4000, layer=None):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts, extra = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.003, reach=0.008, low=list(make.LOW))[0]
    cook.finish(name, [low] + extra, budget=budget + 200, grime=0.6, made=make.provenance("facility_pieces", what), size=layer)


def _beam(a, b, w, material, name):
    """A square member from a to b, w across (a steel leg, a timber brace), and its game-mesh twin."""
    d = b - a
    bpy_rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
    o = make.box((0, 0, 0), (w / 2, w / 2, d.length / 2), material, bevel=0.01, name=name, rot=bpy_rot)
    shift = Matrix.Translation((a + b) / 2)
    o.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    return o


def headframe(m):
    p = []
    H = 18.0
    # Two legs each side, leaning in, braced every 3 m with X-bracing; the back-stays raking back to the winding house.
    feet = [(-2.2, -1.8), (2.2, -1.8), (-2.2, 1.8), (2.2, 1.8)]
    tops = [(-1.0, -1.0), (1.0, -1.0), (-1.0, 1.0), (1.0, 1.0)]
    for (fx, fy), (tx, ty) in zip(feet, tops):
        p.append(_beam(Vector((fx, fy, 0)), Vector((tx, ty, H)), 0.35, m["paint"], "leg"))
    for z in range(3, int(H), 3):
        t = z / H
        for (a, b) in ((0, 1), (2, 3), (0, 2), (1, 3)):
            pa = Vector(feet[a] + (0,)).lerp(Vector(tops[a] + (H,)), t)
            pb = Vector(feet[b] + (0,)).lerp(Vector(tops[b] + (H,)), t)
            pa.z = pb.z = z
            p.append(_beam(pa, pb, 0.18, m["paint"], "strut"))
    for sx in (-1, 1):
        p.append(_beam(Vector((sx * 2.0, 9.0, 0)), Vector((sx * 1.0, 1.0, H - 1.5)), 0.3, m["paint"], "backstay"))
    # The head: a platform, the two sheave wheels side by side, the cage's guides.
    p.append(make.box((0, 0, H + 0.1), (1.4, 1.4, 0.1), m["steel"], bevel=0.02, name="deck"))
    for sx in (-0.55, 0.55):
        c = Vector((sx, 0.2, H + 1.9))
        p.append(make.torus(c, (1, 0, 0), 1.6, 0.09, m["steel"], n=40, m=8, name="sheave", low=(20, 4)))
        for k in range(8):
            a = k * math.pi / 4
            d = Vector((0, math.cos(a), math.sin(a)))
            p.append(make.cyl(c, c + d * 1.55, 0.05, m["steel"], n=6, bevel=0, name="spoke", low=4))
        p.append(make.cyl(c - Vector((0.15, 0, 0)), c + Vector((0.15, 0, 0)), 0.2, m["steel"], n=12, name="hub"))
        p.append(_beam(Vector((sx, 0.2, H + 0.2)), c, 0.2, m["paint"], "pedestal"))
        # The rope over the sheave, down the shaft and back to the winding house.
        p.append(make.cyl(c + Vector((0, -1.6, 0)), Vector((sx, -1.4, 2.0)), 0.03, m["rust"], n=6, bevel=0, name="rope", low=4))
        p.append(make.cyl(c + Vector((0, 1.6, 0.3)), Vector((sx * 0.6, 12.0, 4.0)), 0.03, m["rust"], n=6, bevel=0, name="rope", low=4))
    for sx in (-0.6, 0.6):
        p.append(_beam(Vector((sx, -0.6, 0)), Vector((sx, -0.6, H)), 0.14, m["timber"], "guide"))
    return p, []


def chem_tank(m):
    p = []
    R, H = 3.5, 7.0
    p.append(make.cyl((0, 0, 0), (0, 0, H), R, m["rust"], n=32, bevel=0.03, name="shell", low=16))
    p.append(make.cyl((0, 0, H), (0, 0, H + 0.8), R, m["rust"], n=32, bevel=0.03, name="crown", low=16, r1=0.6))
    for z in (1.2, 2.6, 4.0, 5.4):
        p.append(make.torus((0, 0, z), (0, 0, 1), R + 0.01, 0.03, m["steel"], n=48, m=4, name="seam", low=False))
    p.append(make.cyl((0, 0, -0.3), (0, 0, 0.05), R + 0.4, m["steel"], n=32, bevel=0.02, name="plinth", low=16))
    # The ladder up the side, a railed walkway round the crown, a vent, a valve and its drip.
    for sx in (-0.25, 0.25):
        p.append(make.cyl((sx, -R - 0.25, 0), (sx, -R - 0.25, H + 0.9), 0.03, m["steel"], n=6, bevel=0, name="stile", low=4))
    for k in range(int(H / 0.3)):
        z = 0.3 + k * 0.3
        p.append(make.cyl((-0.25, -R - 0.25, z), (0.25, -R - 0.25, z), 0.018, m["steel"], n=6, bevel=0, name="rung", low=0))
    for k in range(24):
        a = k * math.pi / 12
        p.append(make.cyl((math.cos(a) * 2.2, math.sin(a) * 2.2, H + 0.5), (math.cos(a) * 2.2, math.sin(a) * 2.2, H + 1.5), 0.025, m["steel"], n=6, bevel=0, name="post", low=4))
    p.append(make.torus((0, 0, H + 1.5), (0, 0, 1), 2.2, 0.03, m["steel"], n=40, m=4, name="rail", low=(20, 3)))
    p.append(make.cyl((0, 0, H + 0.8), (0, 0, H + 2.0), 0.18, m["steel"], n=12, name="vent"))
    p.append(make.cyl((1.5, -R + 0.1, 0.8), (1.5, -R - 0.5, 0.8), 0.12, m["steel"], n=12, name="valve"))
    p.append(make.box((1.5, -R - 0.02, 0.4), (0.25, 0.01, 0.4), m["stain"], bevel=0, name="drip", low=False))
    return p, []


def watchtower(m):
    p = []
    H = 8.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(_beam(Vector((sx * 1.6, sy * 1.6, 0)), Vector((sx * 1.2, sy * 1.2, H)), 0.22, m["timber"], "post"))
    for z in (2.5, 5.2):
        for (a, b) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            w = 1.6 - (z / H) * 0.4
            p.append(_beam(Vector((a[0] * w, a[1] * w, z)), Vector((b[0] * w, b[1] * w, z + 2.4)), 0.12, m["timber"], "brace"))
    p.append(make.box((0, 0, H), (1.6, 1.6, 0.08), m["timber"], bevel=0.02, name="deck"))
    # Sandbags round the platform's edge, a corrugated roof on four posts, the searchlight facing the line.
    for k in range(4):
        a = k * math.pi / 2
        c = Vector((math.cos(a) * 1.45, math.sin(a) * 1.45, H + 0.35))
        rot = Matrix.Rotation(a, 4, "Z")
        p.append(make.box((0, 0, 0), (0.18, 1.5, 0.3), m["sack"], bevel=0.08, segments=3, name="bags", rot=rot))
        make.LOW[-1].data.transform(Matrix.Translation(c))
        p[-1].data.transform(Matrix.Translation(c))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.cyl((sx * 1.4, sy * 1.4, H), (sx * 1.4, sy * 1.4, H + 2.2), 0.07, m["timber"], n=8, name="roofpost"))
    p.append(make.box((0, 0, H + 2.3), (1.9, 1.9, 0.05), m["roof"], bevel=0.01, name="roof"))
    lamp_at = Vector((0, -1.3, H + 1.0))
    p.append(make.cyl(lamp_at + Vector((0, 0.25, 0)), lamp_at, 0.3, m["steel"], n=16, name="searchlight"))
    p.append(make.cyl(lamp_at + Vector((0, 0.3, -0.35)), lamp_at + Vector((0, 0.3, 0)), 0.05, m["steel"], n=8, name="yoke"))
    glow = cook.eyes_at([lamp_at + Vector((0, -0.01, 0))], 0.26, colour=(0.95, 0.9, 0.75), name="searchlight")
    for o in glow:
        o.data.transform(Matrix.Diagonal((1, 0.1, 1, 1)) @ Matrix.Translation(-lamp_at))
        o.data.transform(Matrix.Translation(lamp_at))
    # A ladder up one face.
    for sx in (-0.25, 0.25):
        p.append(make.cyl((sx, -1.75, 0), (sx, -1.35, H), 0.04, m["timber"], n=6, bevel=0, name="stile", low=4))
    return p, glow


def sandbags(m):
    p = []
    L = 3.0
    for course in range(4):
        z = 0.14 + course * 0.24
        n = 6
        for k in range(n):
            x = -L / 2 + (k + 0.5 + (0.5 if course % 2 else 0)) * L / n
            if x > L / 2 - 0.1:
                continue
            for sy in (-0.18, 0.18):
                o = make.box((x, sy, z), (0.24, 0.15, 0.11), m["sack"], bevel=0.07, segments=3, name="bag")
                p.append(o)
    return p, []


def cattle_pen(m):
    p = []
    for x in (-1.5, 0, 1.5):
        p.append(make.box((x, 0, 0.7), (0.07, 0.07, 0.75), m["grey"], bevel=0.015, name="post"))
    for z in (0.35, 0.75, 1.15):
        p.append(make.box((0, -0.09, z), (1.55, 0.025, 0.08), m["grey"], bevel=0.008, name="rail"))
    return p, []


def cattle_ramp(m):
    p = []
    # A timber ramp 4 m long rising to a car's floor (1.1 m), cleated, with side rails.
    L, Hh = 4.0, 1.1
    ang = math.atan2(Hh, L)
    rot = Matrix.Rotation(-ang, 4, "X")
    deck = make.box((0, 0, 0), (0.8, L / 2 / math.cos(ang), 0.05), m["grey"], bevel=0.01, name="deck", rot=rot)
    shift = Matrix.Translation((0, 0, Hh / 2))
    deck.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    p.append(deck)
    for k in range(10):
        t = (k + 0.5) / 10
        p.append(make.box((0, -L / 2 + t * L, t * Hh + 0.06), (0.75, 0.025, 0.02), m["grey"], bevel=0.004, name="cleat", low=False))
    for sx in (-0.85, 0.85):
        for y in (-L / 2, 0, L / 2):
            t = (y + L / 2) / L
            p.append(make.box((sx, y, t * Hh + 0.5), (0.06, 0.06, 0.55), m["grey"], bevel=0.01, name="post"))
        p.append(_beam(Vector((sx, -L / 2, 1.0)), Vector((sx, L / 2, Hh + 1.0)), 0.08, m["grey"], "rail"))
    return p, []


def signal_box(m):
    p = []
    W, D = 3.2, 2.4
    p.append(make.box((0, 0, 1.25), (W / 2, D / 2, 1.25), m["brick"], bevel=0.02, name="base"))
    p.append(make.box((0, 0, 3.6), (W / 2 + 0.05, D / 2 + 0.05, 1.1), m["grey"], bevel=0.02, name="box"))
    # Windows all round the top half (dark glass, one lit), glazing bars.
    for sy in (-1, 1):
        p.append(make.box((0, sy * (D / 2 + 0.06), 3.9), (W / 2 - 0.2, 0.01, 0.55), m["lit" if sy < 0 else "glass"], bevel=0, name="window", low=False))
        for k in range(5):
            x = -W / 2 + 0.2 + k * (W - 0.4) / 4
            p.append(make.box((x, sy * (D / 2 + 0.07), 3.9), (0.03, 0.02, 0.56), m["grey"], bevel=0, name="bar", low=False))
    for sx in (-1, 1):
        p.append(make.box((sx * (W / 2 + 0.06), 0, 3.9), (0.01, D / 2 - 0.2, 0.55), m["glass"], bevel=0, name="window", low=False))
    # The hipped roof, a stove pipe, the stairs up the side to the door.
    bpy.ops.mesh.primitive_cone_add(vertices=4, radius1=2.5, radius2=0.2, depth=1.2)
    roof = bpy.context.view_layer.objects.active
    roof.data.transform(Matrix.Translation((0, 0, 5.3)) @ Matrix.Rotation(math.pi / 4, 4, "Z") @ Matrix.Diagonal((1.0, 0.78, 1, 1)))
    roof.data.materials.append(m["roof"])
    r_low = roof.copy()
    r_low.data = roof.data.copy()
    bpy.context.scene.collection.objects.link(r_low)
    make.LOW.append(r_low)
    p.append(roof)
    p.append(make.cyl((1.0, 0.6, 5.2), (1.0, 0.6, 6.2), 0.08, m["rust"], n=10, name="pipe"))
    for k in range(10):
        z = 0.25 + k * 0.25
        p.append(make.box((W / 2 + 0.5, -D / 2 + 0.3 + k * 0.23, z), (0.4, 0.13, 0.03), m["grey"], bevel=0.005, name="step"))
    p.append(_beam(Vector((W / 2 + 0.9, -D / 2 + 0.3, 1.0)), Vector((W / 2 + 0.9, D / 2 - 0.3, 3.5)), 0.05, m["grey"], "handrail"))
    return p, []


def water_tower(m):
    p = []
    H = 6.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(_beam(Vector((sx * 2.0, sy * 2.0, 0)), Vector((sx * 1.6, sy * 1.6, H)), 0.3, m["timber"], "leg"))
    for z in (2.0, 4.0):
        for (a, b) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
            w = 2.0 - z / H * 0.4
            p.append(_beam(Vector((a[0] * w, a[1] * w, z)), Vector((b[0] * w, b[1] * w, z)), 0.15, m["timber"], "girt"))
    p.append(make.box((0, 0, H + 0.1), (2.0, 2.0, 0.1), m["timber"], bevel=0.02, name="deck"))
    p.append(make.cyl((0, 0, H + 0.2), (0, 0, H + 3.4), 1.9, m["rust"], n=28, bevel=0.03, name="tank", low=14))
    p.append(make.cyl((0, 0, H + 3.4), (0, 0, H + 4.2), 1.95, m["roof"], n=28, bevel=0.02, name="lid", low=14, r1=0.2))
    for z in (H + 1.0, H + 2.2):
        p.append(make.torus((0, 0, z), (0, 0, 1), 1.91, 0.04, m["steel"], n=40, m=4, name="hoop", low=False))
    # The spout: a pipe out from the tank's base over the track, its canvas sock hanging from the end.
    p.append(make.cyl((0, -1.8, H + 0.5), (0, -4.0, H + 0.2), 0.16, m["steel"], n=12, name="spout"))
    p.append(make.cyl((0, -4.0, H + 0.2), (0, -4.1, H - 1.4), 0.15, m["sack"], n=10, name="sock", r1=0.12))
    p.append(make.cyl((0.2, -1.9, H - 0.3), (0.2, -1.9, 1.0), 0.02, m["steel"], n=6, bevel=0, name="chain", low=4))
    return p, []


def grain_elevator(m):
    p = []
    R, H = 4.4, 26.0  # the silos: four in a row along X, touching
    xs = [-13.2, -4.4, 4.4, 13.2]
    for x in xs:
        p.append(make.cyl((x, 0, -0.3), (x, 0, H), R, m["concrete"], n=40, bevel=0.02, name="silo", low=28))
        # The slip-form's pour lines, a lip every 1.5 m (baked into the shell, not kept).
        for z in [k * 1.5 for k in range(1, int(H / 1.5))]:
            p.append(make.torus((x, 0, z), (0, 0, 1), R + 0.005, 0.025, m["concrete"], n=40, m=4, name="pour", low=False))
        # Rain off the gallery's eaves: dark streaks down the line side of each, some long, some short.
        for k, (a, length) in enumerate(((-0.55, 14.0), (-0.2, 22.0), (0.15, 9.0), (0.5, 17.0), (-0.85, 6.0))):
            ang = -math.pi / 2 + a + 0.07 * (xs.index(x) - 1.5)
            cx, cy = math.cos(ang) * (R + 0.02), math.sin(ang) * (R + 0.02)
            p.append(make.box((x + cx, cy, H - length / 2), (0.16 + 0.1 * (k % 2), 0.01, length / 2), m["streak"], bevel=0,
                              name="streak", low=False, rot=Matrix.Rotation(ang + math.pi / 2, 4, "Z")))
    # Between the silos' necks, the interstice walls that close the row at the top and bottom.
    for x0, x1 in zip(xs, xs[1:]):
        xm = (x0 + x1) / 2
        p.append(make.box((xm, 0, H / 2), (0.6, R * 0.55, H / 2 + 0.3), m["concrete"], bevel=0.02, name="web"))
    # The bin floor: a long iron-clad gallery along the silos' tops, its windows dark, one lit.
    G0, G1 = H, H + 5.0
    p.append(make.box((0, 0, H + 0.15), (17.9, 4.6, 0.15), m["concrete"], bevel=0.02, name="slab"))
    p.append(make.box((0, 0, (G0 + G1) / 2 + 0.15), (17.6, 4.0, 2.5), m["clad"], bevel=0.03, name="gallery"))
    p.append(make.box((0, 0, G1 + 0.3), (18.0, 4.4, 0.15), m["roof"], bevel=0.02, name="gallery_roof"))
    for k in range(9):
        x = -14.0 + k * 3.5
        mat = m["lit"] if k == 6 else m["glass"]
        p.append(make.box((x, -4.02, G0 + 2.7), (0.7, 0.03, 0.55), mat, bevel=0, name="window", low=False))
        p.append(make.box((x, -4.03, G0 + 2.1), (0.8, 0.04, 0.05), m["rust"], bevel=0, name="sill", low=False))
    # The leg house over the far end: the elevator's boot-to-head legs run up inside it, so it stands 12 m clear.
    L0, L1 = G1 + 0.3, G1 + 12.0
    p.append(make.box((12.5, 0.5, (L0 + L1) / 2), (4.0, 3.6, (L1 - L0) / 2), m["clad"], bevel=0.03, name="leg_house"))
    p.append(make.box((12.5, 0.5, L1 + 1.2), (4.3, 3.9, 0.1), m["roof"], bevel=0.02, name="leg_eave"))
    p.append(make.cyl((12.5, 0.5, L1), (12.5, 0.5, L1 + 1.2), 3.6, m["roof"], n=4, bevel=0, name="leg_roof", r1=0.4, low=4))
    for z in (L0 + 3.0, L0 + 7.5):
        for x in (10.5, 14.5):
            mat = m["lit"] if (z, x) == (L0 + 7.5, 14.5) else m["glass"]
            p.append(make.box((x, -3.12, z), (0.6, 0.03, 0.8), mat, bevel=0, name="window", low=False))
    # A caged ladder up the near end's silo to the gallery: its stiles, rungs and the hoops of the cage.
    lx, ly = -13.2 - R - 0.25, 0.0
    for dy in (-0.25, 0.25):
        p.append(make.cyl((lx, ly + dy, 0.5), (lx, ly + dy, G0 + 1.0), 0.03, m["rust"], n=6, bevel=0, name="stile", low=False))
    for z in [0.8 + k * 0.3 for k in range(int((G0 - 0.5) / 0.3))]:
        p.append(make.cyl((lx, ly - 0.25, z), (lx, ly + 0.25, z), 0.015, m["rust"], n=5, bevel=0, name="rung", low=False))
    for z in [3.0 + k * 1.2 for k in range(int((G0 - 2.0) / 1.2))]:
        p.append(make.torus((lx - 0.35, ly, z), (0, 0, 1), 0.38, 0.02, m["rust"], n=12, m=4, name="hoop", low=False))
    # At the foot, the receiving shed against the silos on the line side, its doorway dark.
    p.append(make.box((-4.4, -R - 2.0, 2.5), (6.0, 2.2, 2.5), m["clad"], bevel=0.03, name="shed"))
    p.append(make.box((-4.4, -R - 2.0, 5.15), (6.3, 2.5, 0.12), m["roof"], bevel=0.02, name="shed_roof",
                      rot=Matrix.Rotation(-0.12, 4, "X")))
    p.append(make.box((-4.4, -R - 4.22, 1.6), (1.6, 0.03, 1.6), m["glass"], bevel=0, name="doorway", low=False))
    # The loading spout: from the gallery's front, swung down and out to 2.5 m off the track at 6 m up, its sock hanging.
    top, end = Vector((4.4, -4.0, G0 + 1.5)), Vector((4.4, -12.5, 6.0))
    p.append(make.cyl(top, end, 0.35, m["rust"], n=12, bevel=0.01, name="spout", low=8))
    for t in (0.25, 0.5, 0.75):
        p.append(make.torus(top.lerp(end, t), end - top, 0.38, 0.05, m["steel"], n=12, m=4, name="spout_band", low=False))
    p.append(make.cyl(end, end + Vector((0, -0.2, -1.6)), 0.32, m["sack"], n=10, bevel=0, name="sock", r1=0.26, low=6))
    # The cables that hold the spout up, from the leg house's corner.
    p.append(make.cyl((9.0, -3.0, L0 + 1.0), end + Vector((0, 0, 0.3)), 0.025, m["steel"], n=5, bevel=0, name="stay", low=False))
    p.append(make.cyl((9.0, -3.0, L0 + 1.0), top.lerp(end, 0.5) + Vector((0, 0, 0.3)), 0.025, m["steel"], n=5, bevel=0,
                      name="stay", low=False))
    return p, []


PIECES = {
    "headframe": lambda: build("headframe", headframe, "the mine head's headframe", budget=3500),
    "chem_tank": lambda: build("chem_tank", chem_tank, "a chemical works' storage tank", budget=2500),
    "watchtower": lambda: build("watchtower", watchtower, "the military depot's watchtower", budget=2500),
    "sandbags": lambda: build("sandbags", sandbags, "a length of sandbag wall", size=512, budget=1500),
    "cattle_pen": lambda: build("cattle_pen", cattle_pen, "a panel of cattle pen", size=512, budget=400),
    "cattle_ramp": lambda: build("cattle_ramp", cattle_ramp, "the cattle loading ramp", size=512, budget=800),
    "signal_box": lambda: build("signal_box", signal_box, "the switchyard's signal box", budget=1500),
    "water_tower": lambda: build("water_tower", water_tower, "a water tower", budget=2000),
    # (Its layer at 1024, a hero's: some 4500 m² of concrete and iron would get 13 cm a texel at the props' 512.)
    "grain_elevator": lambda: build("grain_elevator", grain_elevator, "the grain elevator", size=2048, budget=3000, layer=1024),
}
# tools/models/build.sh builds them all; `blender -b --python facility_pieces.py -- grain_elevator` just the one.
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
