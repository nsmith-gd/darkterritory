"""The walk-in cars' bodywork (GDD §26), modelled, as modules TrainKit.Car repeats where it built plain boxes, to the
car's geometry in content/tuning/train.json (roofWidth 3.0, carHeight 4.0, interior floorHeight 1.1, roofThickness
0.15: a post runs 2.7 m from the sill to the top chord):

  * roof_walk_bay: 1.1 m of the running board down the roof's safe centreline, five gapped boards nailed to a saddle,
    worn pale down the middle where the crew run; TrainKit lays them end to end (spec B.4: the walk is the roof);
  * roof_seam: the cap strip over a joint between roof sheets, bent to the roof's arch, riveted, 1.1 m apart;
  * post_steel: a steel car's side rib, a pressed hat section with rivet rows down both flanges and a gusset at each
    end;
  * post_wood: a planked car's side post, timber with its bolts, and the iron plates at sill and chord.

Axes (Blender): +Y along the car (towards the engine), +X outward (a post faces +X; TrainKit turns it for the other
side), +Z up. A roof piece's origin is on the roof's ridge; a post's at its foot on the car side.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

W = 1.5          # half the roof's width
POST = 2.7       # sill to top chord


def materials():
    return {
        "board": make.lib("wood_grey", 1.2, tint=(0.46, 0.43, 0.4), rough=0.85),
        "timber": make.lib("wood_sleeper", 1.4, tint=(0.75, 0.62, 0.5), rough=0.85),
        "iron": make.lib("rust_heavy", 2.5, tint=(0.35, 0.3, 0.28), rough=0.55, metal=0.6),
        "steel": make.lib("paint_oxide", 1.5, tint=(0.85, 0.75, 0.7), rough=0.6, metal=0.3),
        "roof": make.lib("iron_plate", 1.5, tint=(0.62, 0.62, 0.62), rough=0.5, metal=0.5),
    }


def build(name, fn, what, size=512, budget=400):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.6, made=make.provenance("car_body", what))


def rivet(at, normal, m, r=0.011):
    return make.nail(at, normal, m, r=r)


def roof_walk_bay(m):
    p = []
    L = 1.1
    for i in range(-2, 3):
        x = i * 0.14
        # Each board a little proud or sunk, a little turned: nailed down by hand.
        dz = 0.003 * math.sin(i * 2.3)
        p.append(make.box((x, 0, 0.02 + dz), (0.06, L / 2 - 0.004, 0.02), m["board"], bevel=0.006, name="board",
                          rot=Matrix.Rotation(math.radians(0.6 * math.sin(i * 1.7)), 4, "Z")))
        for y in (-0.05, 0.05):
            p.append(rivet((x + (0.025 if y > 0 else -0.025), y, 0.041 + dz), (0, 0, 1), m["iron"], r=0.008))
    # The saddle under them at the bay's middle, and the strap along each edge board.
    p.append(make.box((0, 0, -0.005), (0.36, 0.05, 0.012), m["timber"], bevel=0.004, name="saddle", low=False))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.33, 0, 0.042), (0.015, L / 2 - 0.02, 0.003), m["iron"], bevel=0.002, name="edge_strap",
                          low=False))
    return p


def roof_seam(m):
    """Over the roof's arch (TrainKit: h - 0.035 s² - 0.005, s across the eaves at W + 0.02), a cap 6 cm wide."""
    p = []
    n = 16
    pts = []
    for k in range(n + 1):
        s = k / n * 2 - 1
        pts.append(Vector((s * (W + 0.02), 0, -0.035 * s * s - 0.005)))
    for a, b in zip(pts, pts[1:]):
        d = b - a
        rot = Vector((1, 0, 0)).rotation_difference(d).to_matrix().to_4x4()
        o = make.box((0, 0, 0), (d.length / 2 + 0.004, 0.03, 0.008), m["roof"], bevel=0.004, name="cap", rot=rot)
        shift = Matrix.Translation((a + b) / 2 + Vector((0, 0, 0.006)))
        o.data.transform(shift)
        make.LOW[-1].data.transform(shift)
        p.append(o)
    for k in range(1, n):
        q = pts[k]
        for y in (-0.018, 0.018):
            p.append(rivet((q.x, y, q.z + 0.015), (0, 0, 1), m["iron"], r=0.007))
    return p


def post_steel(m):
    p = []
    # A hat section: the crown out from the side, two flanges flat on it; rivets down both flanges.
    p.append(make.box((0.028, 0, POST / 2), (0.022, 0.035, POST / 2), m["steel"], bevel=0.008, name="crown"))
    for sy in (-1, 1):
        p.append(make.box((0.006, sy * 0.05, POST / 2), (0.006, 0.022, POST / 2), m["steel"], bevel=0.003, name="flange"))
        for k in range(18):
            z = 0.1 + k * (POST - 0.2) / 17
            p.append(rivet((0.013, sy * 0.055, z), (1, 0, 0), m["iron"], r=0.009))
    # Gussets at the sill and the chord.
    for z in (0.08, POST - 0.08):
        p.append(make.box((0.02, 0, z), (0.018, 0.09, 0.07), m["iron"], bevel=0.004, name="gusset"))
        for sy in (-1, 1):
            p.append(rivet((0.039, sy * 0.06, z), (1, 0, 0), m["iron"], r=0.01))
    return p


def post_wood(m):
    p = []
    p.append(make.box((0.03, 0, POST / 2), (0.03, 0.06, POST / 2), m["timber"], bevel=0.01, name="post"))
    # Carriage bolts through it at intervals, a square washer under each.
    for z in (0.35, 1.0, 1.7, 2.35):
        p.append(make.box((0.062, 0, z), (0.003, 0.022, 0.022), m["iron"], bevel=0.002, name="washer"))
        p.append(rivet((0.066, 0, z), (1, 0, 0), m["iron"], r=0.012))
    # The iron plates that tie it to the sill and the top chord.
    for z in (0.12, POST - 0.12):
        p.append(make.box((0.063, 0, z), (0.004, 0.075, 0.12), m["iron"], bevel=0.003, name="plate"))
        for sy in (-1, 1):
            for dz in (-0.07, 0.07):
                p.append(rivet((0.068, sy * 0.05, z + dz), (1, 0, 0), m["iron"], r=0.009))
    return p


build("roof_walk_bay", roof_walk_bay, "a car's roof walk, a bay of it", budget=300)
build("roof_seam", roof_seam, "a car's roof seam cap", budget=300)
build("post_steel", post_steel, "a steel car's side rib", budget=300)
build("post_wood", post_wood, "a planked car's side post", budget=300)
