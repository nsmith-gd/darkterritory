"""THE DINING CAR (docs/design/upgrades.md §4 and §8.5; ARCHITECTURE §8 notes 959, 974): the catalogue's "cargo car
converted: a chandelier and a table laid for eight", and its bar, modelled and baked, a prop each, over the greybox fit
note 959 built on the sim's own table and counter (CarFrame.DiningRoom). The joke is a dining room bolted into a box car
at the end of the world, so the pieces are the real thing, a first-class diner's, and the plank walls stay a box car's:

  * dining_table: the long table, 0.8 m by 6.6 m and 0.76 m high (the sim's Box), six turned legs under a white cloth
    that hangs a hand down all round, folding where it falls and pooling in a cone at each corner. The game stretches it
    along its length if a car's length gives the table another (TableLength).
  * dining_chair: a railway diner's chair in mahogany: turned front legs, the back legs raked and running up into the
    back's stiles, a vase-shaped splat under a curved top rail, an H stretcher, and the seat upholstered in oxblood
    leather, buttoned, with a row of brass nails round it. Its sitter faces +X (the aisle side's way; the wall side's
    chairs are turned round).
  * dining_place: one place laid for the sitter on its -X side: a gold-rimmed dinner plate with a napkin stood on it
    folded in a mitre, the side plate, knife and spoon on the right, the fork on the left, a wine glass with a finger of
    red in it. Its origin is the plate's middle on the cloth.
  * dining_candelabrum: a three-light brass candelabrum for the middle of the table, the candles burnt down unevenly
    and run with wax. Sockets flame_0..flame_2: the wicks, where the game draws each flame.
  * dining_chandelier: a brass chandelier of six lights hung on its chain from a rose in the ceiling, S-scrolled arms out
    of a turned body, a drip pan and a candle on each and cut-glass drops hanging off the pans. Its origin is the rose's
    top (the ceiling); sockets flame_0..flame_5 at the wicks (TrainKit.ChandelierAt is their height, 0.63 m down).
  * bar_bell: the pub counter's service bell: a nickelled dome on a black base, its plunger on top.

Note 1016 (the director's Look Review verdict, 23 Oct: "More like a fancy bar with bar stools to sit on ... a cool classic
wooden english pub bar. Place it at the back of the car"): the pub down the car's rear half (CarFrame.DiningRoom, train.json
composition.dining.bar), each modelled in the car's own axes:

  * pub_counter: the pub's long counter, PUB_L along the car (Y) and PUB_D deep, its front to -X (the aisle) in raised
    and fielded mahogany panels between pilasters, a plinth and base moulding, a thick bullnosed top overhanging the front,
    a brass foot rail on brackets; its return at the front end (+Y) across to the wall, panelled to the front; on the top
    three beer engines (porcelain handles, brass collars, a pump clip each) over a drip tray, pint glasses, the bar cloth;
    behind (the barman's side, +X), open shelves of glasses turned down. Origin: the run's middle, on the floor.
  * pub_back: the back bar on the car's right wall behind the walkway, facing -X: a shelf at the counter's height, then
    bays of mirror between turned columns, three shelves of bottles across them, a row of optics (spirits upside down with
    their measures), a carved cornice and a gilt board painted THE DINING CAR. Origin: on the floor at the wall, its middle.
  * pub_stool: a bar stool, turned mahogany, four splayed legs, a brass foot ring, a round seat buttoned in oxblood
    leather and nailed round in brass, its top STOOL_H. Origin: on the floor, under the seat.

Axes (Blender): +X the car's right, +Y its front (the game's -Z), +Z up; each piece's origin as above.

    tools/models/build.sh dining_car [dining_chair ...]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

# The sim's (CarFrame.DiningRoom, train.json geometry: a 14 m car, 3 m wide, its walls 0.1 thick, floor at 1.1 m): the
# table x 0.05..0.85, 0.76 high, modelled 6.6 m long and stretched to the sim's (note 1016: the front half's, 5.9 m).
TABLE_W, TABLE_L, TABLE_H = 0.8, 6.6, 0.76
CANDLE_TIP = 0.63  # the chandelier's wicks, down from the ceiling (TrainKit.ChandelierAt: 0.75 less the candle)
# The pub (train.json composition.dining.bar, note 1016): the counter's run, its depth and the return's width across to the
# wall (from its back at x 0.6 to the wall at 1.4), the back bar's depth and length (the run less the return's depth), a
# stool's seat.
PUB_L, PUB_D, PUB_H = 4.8, 0.5, 1.08
PUB_RETURN = 0.8
PUB_BACK_D, PUB_BACK_L = 0.15, 4.3
STOOL_H = 0.76


def materials():
    return {
        "wood": make.lib("wood_floor", 2.6, tint=(0.66, 0.38, 0.27), rough=0.35),
        "dark": make.lib("wood_floor", 2.6, tint=(0.44, 0.25, 0.18), rough=0.4),
        "leather": make.lib("leather", 5.0, tint=(0.6, 0.17, 0.13), rough=0.4),
        "brass": make.lib("brass", 3.0, rough=0.3, metal=0.9),
        "cloth": make.flat("linen", (0.8, 0.78, 0.72), rough=0.85),
        "china": make.flat("china", (0.86, 0.85, 0.8), rough=0.25),
        "silver": make.flat("silver", (0.66, 0.66, 0.63), rough=0.3, metal=0.9),
        "glass": make.flat("glass", (0.58, 0.64, 0.62), rough=0.05, metal=0.3),
        "wine": make.flat("wine", (0.28, 0.03, 0.05), rough=0.1, metal=0.2),
        "wax": make.flat("wax", (0.88, 0.84, 0.72), rough=0.55),
        "wick": make.flat("wick", (0.08, 0.07, 0.06), rough=0.9),
        "crystal": make.flat("crystal", (0.8, 0.84, 0.86), rough=0.05, metal=0.4),
        "mirror": make.flat("mirror", (0.36, 0.39, 0.38), rough=0.06, metal=0.95),
        "nickel": make.flat("nickel", (0.72, 0.72, 0.7), rough=0.2, metal=0.95),
        "ebony": make.flat("ebony", (0.07, 0.06, 0.05), rough=0.4),
        "paper": make.flat("label", (0.82, 0.76, 0.6), rough=0.8),
        "ink": make.flat("ink", (0.25, 0.08, 0.06), rough=0.7),
        "gilt": make.flat("gilt", (0.75, 0.58, 0.25), rough=0.35, metal=0.8),
        "cork": make.flat("cork", (0.5, 0.36, 0.22), rough=0.9),
        "stone": make.flat("stoneware", (0.72, 0.66, 0.55), rough=0.6),
        "glaze": make.flat("glaze", (0.35, 0.24, 0.14), rough=0.3),
        "bottle_green": make.flat("bottle_green", (0.08, 0.2, 0.09), rough=0.08, metal=0.3),
        "bottle_amber": make.flat("bottle_amber", (0.42, 0.2, 0.05), rough=0.08, metal=0.3),
        "bottle_clear": make.flat("bottle_clear", (0.62, 0.66, 0.62), rough=0.08, metal=0.3),
        "bottle_brown": make.flat("bottle_brown", (0.22, 0.1, 0.04), rough=0.08, metal=0.3),
    }


# ----------------------------------------------------------------------------------------------------------------
# Shapes the make module hasn't: a turned profile, a flat outline given thickness, a beam between two points.

def _object(name, bm, material):
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    o = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    for f in o.data.polygons:
        f.use_smooth = True
    return o


def _lathe_mesh(profile, n, name, material, place):
    bm = bmesh.new()
    rings = []
    for r, z in profile:
        if r < 1e-6:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            rings.append([bm.verts.new((r * math.cos(k * 2 * math.pi / n), r * math.sin(k * 2 * math.pi / n), z)) for k in range(n)])
    for a, b in zip(rings, rings[1:]):
        if len(a) == 1 and len(b) == 1:
            continue
        for k in range(n):
            k1 = (k + 1) % n
            if len(a) == 1:
                bm.faces.new((a[0], b[k], b[k1]))
            elif len(b) == 1:
                bm.faces.new((a[k1], a[k], b[0]))
            else:
                bm.faces.new((a[k], a[k1], b[k1], b[k]))
    if len(rings[0]) > 1:
        bm.faces.new(list(reversed(rings[0])))
    if len(rings[-1]) > 1:
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.transform(place)
    return _object(name, bm, material)


def lathe(profile, material, at=(0, 0, 0), axis=(0, 0, 1), n=24, low=8, name="turned", scale=(1, 1, 1)):
    """A turned piece: `profile` (radius, height) from its foot up, spun round `axis` from `at`; its twin in LOW with
    `low` sides (0: none). `scale` squashes it across (an oval knop)."""
    place = (Matrix.Translation(Vector(at)) @ Vector((0, 0, 1)).rotation_difference(Vector(axis)).to_matrix().to_4x4()
             @ Matrix.Diagonal((*scale, 1)))
    if low:
        make.LOW.append(_lathe_mesh(profile, low, name + "_low", material, place))
    return _lathe_mesh(profile, n, name, material, place)


def _slab_mesh(outline, thick, name, material, place):
    bm = bmesh.new()
    lo = [bm.verts.new((u, v, -thick / 2)) for u, v in outline]
    hi = [bm.verts.new((u, v, thick / 2)) for u, v in outline]
    bm.faces.new(list(reversed(lo)))
    bm.faces.new(hi)
    n = len(outline)
    for k in range(n):
        k1 = (k + 1) % n
        bm.faces.new((lo[k], lo[k1], hi[k1], hi[k]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.transform(place)
    return _object(name, bm, material)


def slab(outline, thick, material, at, u, v, name="slab", low=True, bevel=0.0):
    """A flat shape: `outline` (u, v) points round it, `thick` through it, laid on the plane through `at` spanned by
    the directions `u` and `v`. Its twin in LOW unless `low` is False."""
    u, v = Vector(u).normalized(), Vector(v).normalized()
    w = u.cross(v).normalized()
    place = Matrix.Translation(Vector(at)) @ Matrix((u, v, w)).transposed().to_4x4()
    if low:
        make.LOW.append(_slab_mesh(outline, thick, name + "_low", material, place))
    o = _slab_mesh(outline, thick, name, material, place)
    if bevel > 0:
        mod = o.modifiers.new("bevel", "BEVEL")
        mod.width = bevel
        mod.segments = 2
        mod.limit_method = "ANGLE"
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=mod.name)
    return o


def beam(a, b, w, d, material, up=(0, 0, 1), bevel=0.004, name="beam", low=True):
    """A square-sectioned length of wood from a to b, `w` across and `d` deep (half sizes), its `d` toward `up`."""
    a, b = Vector(a), Vector(b)
    along = (b - a).normalized()
    side = along.cross(Vector(up))
    if side.length < 1e-6:
        side = along.cross(Vector((1, 0, 0)))
    side.normalize()
    top = side.cross(along).normalized()
    rot = Matrix((side, top, along)).transposed().to_4x4()
    return make.box((a + b) / 2, (w, d, (b - a).length / 2), material, bevel=bevel, name=name, rot=rot, low=low)


# ----------------------------------------------------------------------------------------------------------------
# The table

def _cloth_loop(step, folds, drop):
    """Round the table's top edge (corners rounded 4 cm) at about `step` apart: for each point its place on the top and
    the outward direction, and how far out and down the hem falls there (folds when `folds`)."""
    hw, hl, rc = TABLE_W / 2 + 0.04, TABLE_L / 2 + 0.04, 0.04
    pts = []
    corners = [((hw - rc, hl - rc), 0), ((-hw + rc, hl - rc), 90), ((-hw + rc, -hl + rc), 180), ((hw - rc, -hl + rc), 270)]
    for i, ((cx, cy), a0) in enumerate(corners):
        # The corner's arc, then the straight edge to the next corner.
        for k in range(4):
            a = math.radians(a0 + k * 90 / 4)
            pts.append(((cx + rc * math.cos(a), cy + rc * math.sin(a)), (math.cos(a), math.sin(a)), True))
        (nx, ny), _ = corners[(i + 1) % 4]
        a = math.radians(a0 + 90)
        sx, sy = cx + rc * math.cos(a), cy + rc * math.sin(a)
        ex, ey = nx + rc * math.cos(a), ny + rc * math.sin(a)
        length = math.hypot(ex - sx, ey - sy)
        m = max(1, int(round(length / step)))
        for k in range(m):
            t = k / m
            pts.append(((sx + (ex - sx) * t, sy + (ey - sy) * t), (math.cos(a), math.sin(a)), k == 0))
    out = []
    s = 0.0
    for k, ((x, y), (ox, oy), _) in enumerate(pts):
        if k:
            s += math.hypot(x - pts[k - 1][0][0], y - pts[k - 1][0][1])
        # A corner's cloth falls in a cone, out and down; along a side it waves gently where it was folded in the press.
        dist = min(math.hypot(x - sgn * (TABLE_W / 2), y - sgn2 * (TABLE_L / 2)) for sgn in (-1, 1) for sgn2 in (-1, 1))
        corner = max(0.0, 1 - dist / 0.18)
        out_by = 0.012 + 0.05 * corner ** 2
        down = drop + 0.035 * corner ** 2
        if folds:
            out_by += 0.01 * math.sin(s * 2 * math.pi / 0.17) * (1 - corner) + 0.004 * math.sin(s * 2 * math.pi / 0.061 + 1.3)
            down += 0.008 * math.sin(s * 2 * math.pi / 0.29 + 0.7) * (1 - corner)
        out.append((x, y, ox, oy, out_by, down))
    return out


def _cloth_mesh(step, folds, name, material):
    loop = _cloth_loop(step, folds, 0.21)
    bm = bmesh.new()
    top = TABLE_H + 0.004
    n = len(loop)
    # Rings: on the top, over the edge (a round), the hem outside, the hem's underside, and back up inside to the edge.
    rings = []
    shapes = [
        lambda x, y, ox, oy, o, d: (x - ox * 0.012, y - oy * 0.012, top),
        lambda x, y, ox, oy, o, d: (x + ox * 0.004, y + oy * 0.004, top - 0.008),
        lambda x, y, ox, oy, o, d: (x + ox * (o * 0.45), y + oy * (o * 0.45), top - d * 0.45),
        lambda x, y, ox, oy, o, d: (x + ox * o, y + oy * o, top - d),
        lambda x, y, ox, oy, o, d: (x + ox * (o - 0.004), y + oy * (o - 0.004), top - d),
        lambda x, y, ox, oy, o, d: (x - ox * 0.006, y - oy * 0.006, top - 0.02),
    ]
    for f in shapes:
        rings.append([bm.verts.new(f(*p)) for p in loop])
    for a, b in zip(rings, rings[1:]):
        for k in range(n):
            k1 = (k + 1) % n
            bm.faces.new((a[k], a[k1], b[k1], b[k]))
    bm.faces.new(rings[0])
    bm.faces.new(list(reversed(rings[-1])))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _object(name, bm, material)


def table(m):
    p = []
    # The cloth over its top (the game mesh is the same cloth with its folds left to the bake, the corners' cones kept).
    p.append(_cloth_mesh(0.02, True, "cloth", m["cloth"]))
    make.LOW.append(_cloth_mesh(0.45, False, "cloth_low", m["cloth"]))
    # Six turned legs, square where the apron takes them, a ring and a long taper to a foot.
    leg = [(0.0, 0.0), (0.022, 0.0), (0.026, 0.02), (0.02, 0.05), (0.024, 0.07), (0.03, 0.09), (0.024, 0.11), (0.021, 0.3),
           (0.026, 0.4), (0.03, 0.43), (0.025, 0.46), (0.033, 0.5), (0.033, 0.53), (0.0, 0.53)]
    for x in (-TABLE_W / 2 + 0.07, TABLE_W / 2 - 0.07):
        for y in (-TABLE_L / 2 + 0.08, 0.0, TABLE_L / 2 - 0.08):
            p.append(lathe(leg, m["wood"], at=(x, y, 0), n=16, low=6, name="leg"))
            p.append(make.box((x, y, (0.53 + TABLE_H - 0.05) / 2), (0.034, 0.034, (TABLE_H - 0.05 - 0.53) / 2), m["wood"], bevel=0.004,
                              name="leg_block"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# A chair

SEAT = 0.46


def chair(m):
    p = []
    # The seat's frame and its leather pad, domed, buttoned, nailed round in brass.
    p.append(make.box((0, 0, SEAT - 0.03), (0.215, 0.215, 0.032), m["wood"], bevel=0.008, name="seat_frame"))
    p.append(make.box((0.005, 0, SEAT + 0.018), (0.205, 0.205, 0.022), m["leather"], bevel=0.018, segments=4, name="pad"))
    for k in range(4):
        for j in range(3):
            at = (-0.12 + k * 0.085, -0.1 + j * 0.1, SEAT + 0.04)
            p.append(make.cyl(at, (at[0], at[1], at[2] + 0.004), 0.008, m["leather"], n=8, bevel=0.002, r1=0.004, name="button", low=0))
    for k in range(12):
        t = -0.2 + k * 0.4 / 11
        for at, n in (((0.212, t, SEAT + 0.006), (1, 0, 0)), ((t, 0.212, SEAT + 0.006), (0, 1, 0)), ((t, -0.212, SEAT + 0.006), (0, -1, 0))):
            p.append(make.nail(at, n, m["brass"], r=0.0055))
    # Turned front legs (the sitter's side, +X), square back legs raked out behind and on up into the back's stiles.
    front = [(0.0, 0.0), (0.016, 0.0), (0.019, 0.015), (0.016, 0.04), (0.02, 0.07), (0.016, 0.09), (0.018, 0.2), (0.022, 0.26),
             (0.017, 0.29), (0.02, 0.33), (0.024, 0.36), (0.024, SEAT - 0.06), (0.0, SEAT - 0.06)]
    for y in (-0.18, 0.18):
        p.append(lathe(front, m["wood"], at=(0.18, y, 0), n=14, low=6, name="front_leg"))
        p.append(beam((-0.22, y, 0.0), (-0.18, y, SEAT - 0.04), 0.017, 0.017, m["wood"], up=(1, 0, 0), name="back_leg"))
        p.append(beam((-0.18, y, SEAT - 0.04), (-0.255, y * 0.94, 0.97), 0.017, 0.014, m["wood"], up=(1, 0, 0), name="stile"))
        # The side stretchers.
        p.append(beam((0.17, y, 0.14), (-0.2, y, 0.14), 0.01, 0.012, m["wood"], name="stretcher"))
    p.append(beam((-0.01, -0.18, 0.14), (-0.01, 0.18, 0.14), 0.01, 0.012, m["wood"], name="stretcher_x"))
    # The back: a rail across at the seat, the curved top rail, and the vase splat between, all leant back with the stiles.
    rake = Vector((-0.255 + 0.18, 0, 0.97 - SEAT + 0.04)).normalized()
    base = Vector((-0.19, 0, SEAT + 0.02))
    def up_back(h):
        return base + rake * h
    p.append(beam(up_back(0.04) + Vector((0, -0.17, 0)), up_back(0.04) + Vector((0, 0.17, 0)), 0.014, 0.02, m["wood"], up=(1, 0, 0),
                  name="back_rail"))
    arc = [(-0.19 + 0.38 * k / 16, 0.0 + 0.035 * math.sin(math.pi * k / 16)) for k in range(17)]
    top_outline = [(u, v - 0.035) for u, v in arc] + [(u, v + 0.035) for u, v in reversed(arc)]
    p.append(slab(top_outline, 0.026, m["wood"], up_back(0.44), (0, 1, 0), rake, name="top_rail", bevel=0.006))
    vase = [(0.0, 0.0), (0.045, 0.0), (0.035, 0.04), (0.06, 0.11), (0.07, 0.17), (0.055, 0.23), (0.03, 0.28), (0.04, 0.33),
            (0.06, 0.36), (0.0, 0.36)]
    splat = [(w, h) for w, h in vase] + [(-w, h) for w, h in reversed(vase)]
    splat = [pt for k, pt in enumerate(splat) if k == 0 or pt != splat[k - 1]]
    p.append(slab(splat[:-1], 0.014, m["wood"], up_back(0.06), (0, 1, 0), rake, name="splat", bevel=0.003))
    # A turned finial on each stile's top.
    for y in (-0.18, 0.18):
        tip = Vector((-0.255, y * 0.94, 0.97))
        p.append(lathe([(0.0, 0.0), (0.018, 0.0), (0.02, 0.012), (0.012, 0.022), (0.016, 0.034), (0.0, 0.05)], m["wood"], at=tip,
                       n=12, low=5, name="finial"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# A place laid

def place(m):
    p = []
    plate = [(0.0, 0.0), (0.07, 0.0), (0.078, 0.003), (0.118, 0.012), (0.125, 0.015), (0.122, 0.017), (0.08, 0.009), (0.07, 0.007),
             (0.0, 0.007)]
    p.append(lathe(plate, m["china"], n=40, low=20, name="plate"))
    p.append(make.torus((0, 0, 0.0158), (0, 0, 1), 0.112, 0.0018, m["gilt"], n=40, m=4, name="rim", low=None))
    side = [(r * 0.62, z * 0.7) for r, z in plate]
    p.append(lathe(side, m["china"], at=(0.13, 0.19, 0), n=32, low=16, name="side_plate"))
    # The napkin stood on the plate, folded in a mitre: two peaks, one behind the other.
    mitre = [(-0.055, 0.0), (0.055, 0.0), (0.05, 0.08), (0.018, 0.13), (0.0, 0.105), (-0.022, 0.15), (-0.05, 0.075)]
    p.append(slab(mitre, 0.012, m["cloth"], (-0.006, 0, 0.012), (0, 1, 0), (0, 0, 1), name="napkin", bevel=0.004))
    p.append(slab([(u * 0.9, v * 0.85) for u, v in mitre], 0.01, m["cloth"], (0.012, 0.004, 0.012), (0, -1, 0), (0, 0, 1),
                  name="napkin_back", bevel=0.004))
    # The sitter faces +X: their right hand is at -Y. Knife and spoon there, the fork on the left.
    p.append(make.box((0.02, -0.155, 0.003), (0.095, 0.007, 0.0015), m["silver"], bevel=0.001, name="blade"))
    p.append(make.box((-0.1, -0.155, 0.005), (0.045, 0.0065, 0.004), m["silver"], bevel=0.003, name="knife_handle"))
    p.append(make.box((-0.02, -0.19, 0.003), (0.07, 0.0045, 0.002), m["silver"], bevel=0.001, name="spoon_stem"))
    bowl = cook.uv_sphere(12, 6, 0.02, Matrix.Translation((0.07, -0.19, 0.006)) @ Matrix.Diagonal((1.5, 1.0, 0.3, 1)))
    p.append(make._finish(bowl, m["silver"], 0, 1, "spoon_bowl"))
    make.low_box((0.07, -0.19, 0.005), (0.03, 0.02, 0.005), m["silver"], name="spoon_bowl")
    p.append(make.box((-0.03, 0.155, 0.003), (0.07, 0.005, 0.002), m["silver"], bevel=0.001, name="fork_stem"))
    for k in range(4):
        p.append(make.box((0.07, 0.146 + k * 0.006, 0.003), (0.025, 0.0012, 0.0012), m["silver"], bevel=0, name="tine", low=False))
    make.low_box((0.07, 0.155, 0.003), (0.025, 0.012, 0.002), m["silver"], name="tines")
    # The wine glass, top right, a finger of red in it.
    glass = [(0.0, 0.0), (0.03, 0.0), (0.03, 0.003), (0.006, 0.008), (0.0035, 0.02), (0.0035, 0.065), (0.012, 0.075),
             (0.03, 0.095), (0.036, 0.12), (0.033, 0.155), (0.031, 0.155), (0.033, 0.12), (0.027, 0.097), (0.0, 0.08)]
    p.append(lathe(glass, m["glass"], at=(0.14, -0.16, 0), n=20, low=8, name="wine_glass"))
    p.append(lathe([(0.0, 0.0), (0.012, 0.0), (0.027, 0.017), (0.029, 0.03), (0.0, 0.03)], m["wine"], at=(0.14, -0.16, 0.08),
                   n=20, low=0, name="wine"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# Candles and their brass

def candle(m, base, height, r=0.011, n=12, seed=0):
    """A candle from `base` up `height`, a run of wax down its side and its wick; returns (parts, wick tip)."""
    p = []
    base = Vector(base)
    tip = base + Vector((0, 0, height))
    burnt = [(0.0, 0.0), (r, 0.0), (r, height - 0.006), (r * 0.8, height), (0.0, height - 0.003)]
    p.append(lathe(burnt, m["wax"], at=base, n=n, low=6, name="candle"))
    a = seed * 2.1
    run = base + Vector((math.cos(a) * r, math.sin(a) * r, height * 0.55))
    drip = cook.uv_sphere(8, 5, r * 0.45, Matrix.Translation(run) @ Matrix.Diagonal((0.8, 0.8, 0.45 * height / r, 1)))
    p.append(make._finish(drip, m["wax"], 0, 1, "run"))
    p.append(make.cyl(tip - Vector((0, 0, 0.004)), tip + Vector((0, 0, 0.009)), 0.0012, m["wick"], n=5, bevel=0, name="wick", low=0))
    return p, tip + Vector((0, 0, 0.009))


def candelabrum(m):
    p = []
    stem = [(0.0, 0.0), (0.075, 0.0), (0.077, 0.006), (0.06, 0.016), (0.04, 0.02), (0.016, 0.04), (0.012, 0.07), (0.022, 0.08),
            (0.012, 0.09), (0.01, 0.2), (0.02, 0.21), (0.024, 0.22), (0.012, 0.235), (0.0, 0.235)]
    p.append(lathe(stem, m["brass"], n=20, low=8, name="stem"))
    cup = [(0.0, 0.0), (0.006, 0.0), (0.03, 0.012), (0.032, 0.016), (0.016, 0.016), (0.016, 0.04), (0.0, 0.04)]
    flames = {}
    for k, (x, z) in enumerate(((0.0, 0.235), (-0.12, 0.2), (0.12, 0.2))):
        if x != 0:
            p += make.pipe([(0, 0, 0.17), (x * 0.4, 0, 0.14), (x * 0.85, 0, 0.15), (x, 0, z - 0.01)], 0.008, m["brass"], name="arm", n=8, low=5)
        p.append(lathe(cup, m["brass"], at=(x, 0, z - 0.01), n=16, low=8, name="cup"))
        parts, wick = candle(m, (x, 0, z + 0.03), (0.17, 0.13, 0.15)[k], seed=k)
        p += parts
        flames[f"flame_{k}"] = tuple(wick)
    return p, flames


def chandelier(m):
    p = []
    # The ceiling rose, and the chain down from it, link by link (one rod in the game mesh).
    p.append(lathe([(0.0, 0.0), (0.12, 0.0), (0.115, -0.015), (0.07, -0.03), (0.03, -0.04), (0.0, -0.045)], m["brass"], n=24, low=10,
                   name="rose"))
    z = -0.05
    k = 0
    while z > -0.46:
        axis = (1, 0, 0) if k % 2 == 0 else (0, 1, 0)
        p.append(make.torus((0, 0, z - 0.017), axis, 0.017, 0.0035, m["brass"], n=12, m=5, name="link", low=None))
        z -= 0.03
        k += 1
    # (The game mesh's chain is a rod, the links baked onto it: its modelled twin isn't kept.)
    cook.delete([make.cyl((0, 0, -0.05), (0, 0, -0.48), 0.006, m["brass"], n=6, bevel=0, name="chain", low=5)])
    # The body: a finial ball, a vase, a flared bowl the arms come out of, and a drop under it.
    body = [(0.0, -0.48), (0.02, -0.48), (0.03, -0.5), (0.02, -0.52), (0.012, -0.55), (0.018, -0.6), (0.04, -0.66), (0.045, -0.7),
            (0.03, -0.75), (0.016, -0.78), (0.03, -0.8), (0.09, -0.84), (0.1, -0.87), (0.06, -0.9), (0.025, -0.92), (0.03, -0.95),
            (0.012, -0.99), (0.0, -1.0)]
    p.append(lathe(body, m["brass"], n=28, low=10, name="body"))
    flames = {}
    pan = [(0.0, 0.0), (0.008, 0.0), (0.05, 0.012), (0.052, 0.018), (0.046, 0.018), (0.016, 0.01), (0.016, 0.03), (0.0, 0.03)]
    tip_z = -CANDLE_TIP
    for k in range(6):
        a = k * math.pi / 3 + math.pi / 6
        c, s = math.cos(a), math.sin(a)
        def at(r, zz):
            return (r * c, r * s, zz)
        # An S-scroll arm: out of the bowl, down, round and up to its pan.
        p += make.pipe([at(0.08, -0.86), at(0.17, -0.92), at(0.27, -0.93), at(0.34, -0.89), at(0.38, -0.83), at(0.38, -0.8)],
                       0.009, m["brass"], name="arm", n=8, low=5)
        p.append(make.torus(at(0.165, -0.905), (-s, c, 0), 0.022, 0.006, m["brass"], n=14, m=5, name="scroll", low=(6, 3)))
        p.append(lathe(pan, m["brass"], at=at(0.38, -0.8), n=18, low=8, name="pan"))
        parts, wick = candle(m, at(0.38, -0.77), tip_z + 0.77 - 0.009 - 0.006 * (k % 3), seed=k + 3)
        p += parts
        flames[f"flame_{k}"] = tuple(wick)
        # Cut-glass drops off the pan's rim, between the arms.
        for j in range(3):
            b = a + (j - 1) * 0.55
            hang = Vector((0.38 * c + 0.05 * math.cos(b), 0.38 * s + 0.05 * math.sin(b), -0.785))
            p.append(make.cyl(hang, hang - Vector((0, 0, 0.012)), 0.0009, m["brass"], n=4, bevel=0, name="hook", low=0))
            p.append(make.cyl(hang - Vector((0, 0, 0.012)), hang - Vector((0, 0, 0.032)), 0.002, m["crystal"], n=6, bevel=0, r1=0.009,
                              name="drop_top", low=0))
            p.append(make.cyl(hang - Vector((0, 0, 0.032)), hang - Vector((0, 0, 0.06)), 0.009, m["crystal"], n=6, bevel=0, r1=0.0005,
                              name="drop", low=4))
    return p, flames


# ----------------------------------------------------------------------------------------------------------------
# The bar

def panel(m, centre, w, h, normal, depth=0.012, frame=0.05):
    """A raised and fielded panel in its frame, on the face whose outward direction is `normal` (+Y or -X here), centred
    at `centre` on that face, `w` across and `h` high."""
    c = Vector(centre)
    n = Vector(normal)
    across = Vector((0, 0, 1)).cross(n).normalized()
    p = []
    def box_on(off_a, off_z, ha, hz, proud, mat, bevel, name):
        mid = c + across * off_a + Vector((0, 0, off_z)) + n * (proud / 2)
        half = Vector((abs(across.x) * ha + abs(n.x) * proud / 2, abs(across.y) * ha + abs(n.y) * proud / 2, hz))
        return make.box(tuple(mid), tuple(half), mat, bevel=bevel, name=name, low=False)
    # Stiles and rails.
    for s in (-1, 1):
        p.append(box_on(s * (w / 2 - frame / 2), 0, frame / 2, h / 2, depth, m["dark"], 0.004, "stile"))
        p.append(box_on(0, s * (h / 2 - frame / 2), w / 2 - frame, frame / 2, depth, m["dark"], 0.004, "rail"))
    # The panel: its field raised in the middle, bevelled down to the frame.
    p.append(box_on(0, 0, w / 2 - frame - 0.004, h / 2 - frame - 0.004, depth * 0.4, m["wood"], 0.002, "panel"))
    p.append(box_on(0, 0, w / 2 - frame - 0.03, h / 2 - frame - 0.03, depth * 0.95, m["wood"], 0.012, "field"))
    return p


def bottle(m, at, kind, seed):
    """One bottle on a shelf or the counter, by `kind`: wine, gin (square), whisky (square, squat), rum, jar (stoneware),
    brandy (a flask). Returns its parts."""
    x, y, z = at
    p = []
    rnd = (seed * 7919 % 101) / 100.0
    if kind in ("gin", "whisky"):
        hw, hh = (0.032, 0.11) if kind == "gin" else (0.042, 0.075)
        mat = m["bottle_clear"] if kind == "gin" else m["bottle_amber"]
        p.append(make.box((x, y, z + hh), (hw, hw, hh), mat, bevel=0.01, segments=2, name=kind))
        top = z + hh * 2
        p.append(lathe([(0.0, 0.0), (hw * 0.95, 0.0), (0.012, 0.03), (0.011, 0.045), (0.013, 0.05), (0.0, 0.05)], mat, at=(x, y, top - 0.004),
                       n=12, low=6, name="neck"))
        p.append(make.cyl((x, y, top + 0.046), (x, y, top + 0.066), 0.011, m["cork"], n=10, bevel=0.002, name="cork", low=0))
        p.append(make.box((x, y - hw - 0.0005, z + hh * 1.05), (hw * 0.75, 0.0008, hh * 0.45), m["paper"], bevel=0, name="label", low=False))
        p.append(make.box((x, y - hw - 0.0012, z + hh * 1.15), (hw * 0.5, 0.0006, hh * 0.08), m["ink"], bevel=0, name="label_ink", low=False))
        return p
    if kind == "jar":
        prof = [(0.0, 0.0), (0.045, 0.0), (0.05, 0.02), (0.052, 0.09), (0.046, 0.12), (0.03, 0.14), (0.016, 0.15), (0.017, 0.17),
                (0.0, 0.17)]
        p.append(lathe(prof, m["stone"], at=at, n=18, low=8, name="jar"))
        p.append(lathe([(0.0, 0.0), (0.0525, 0.0), (0.053, 0.05), (0.0, 0.05)], m["glaze"], at=(x, y, z + 0.09), n=18, low=0, name="glaze"))
        p.append(make.torus((x + 0.05, y, z + 0.12), (0, 1, 0), 0.025, 0.005, m["stone"], n=12, m=5, name="handle", low=(6, 3)))
        return p
    if kind == "brandy":
        prof = [(0.0, 0.0), (0.03, 0.0), (0.05, 0.03), (0.052, 0.06), (0.04, 0.1), (0.014, 0.13), (0.012, 0.17), (0.0, 0.17)]
        p.append(lathe(prof, m["bottle_brown"], at=at, n=16, low=8, name="brandy", scale=(1, 0.55, 1)))
        p.append(make.cyl((x, y, z + 0.165), (x, y, z + 0.185), 0.011, m["cork"], n=10, bevel=0.002, name="cork", low=0))
        return p
    # Round ones: wine (tall, its shoulders), rum (dark, shorter).
    tall = 0.29 if kind == "wine" else 0.24
    mat = m["bottle_green"] if kind == "wine" else (m["bottle_brown"] if rnd < 0.5 else m["bottle_amber"])
    r = 0.036 if kind == "wine" else 0.04
    prof = [(0.0, 0.0), (r * 0.9, 0.0), (r, 0.01), (r, tall * 0.6), (r * 0.8, tall * 0.68), (0.013, tall * 0.78), (0.012, tall * 0.96),
            (0.014, tall * 0.97), (0.014, tall), (0.0, tall)]
    p.append(lathe(prof, mat, at=at, n=16, low=8, name=kind))
    p.append(make.cyl((x, y, z + tall * 0.94), (x, y, z + tall + 0.006), 0.0145, m["gilt"] if kind == "wine" else m["cork"], n=10,
                      bevel=0.001, name="capsule", low=0))
    p.append(lathe([(0.0, 0.0), (r + 0.0008, 0.0), (r + 0.0008, tall * 0.25), (0.0, tall * 0.25)], m["paper"], at=(x, y, z + tall * 0.22),
                   n=16, low=0, name="label"))
    p.append(lathe([(0.0, 0.0), (r + 0.0012, 0.0), (r + 0.0012, tall * 0.04), (0.0, tall * 0.04)], m["ink"], at=(x, y, z + tall * 0.38),
                   n=16, low=0, name="label_band"))
    return p


def tumbler(m, at, down=False):
    prof = [(0.0, 0.0), (0.03, 0.0), (0.034, 0.085), (0.031, 0.085), (0.027, 0.012), (0.0, 0.012)]
    if down:
        prof = [(r, 0.085 - z) for r, z in reversed(prof)]
    return lathe(prof, m["glass"], at=at, n=16, low=8, name="tumbler")


def bell(m):
    p = []
    p.append(lathe([(0.0, 0.0), (0.046, 0.0), (0.048, 0.006), (0.044, 0.016), (0.0, 0.016)], m["ebony"], n=24, low=10, name="base"))
    dome = [(0.0, 0.016)] + [(0.042 * math.cos(a), 0.016 + 0.04 * math.sin(a)) for a in [k * math.pi / 2 / 8 for k in range(9)]]
    p.append(lathe(dome, m["nickel"], n=24, low=10, name="dome"))
    p.append(make.cyl((0, 0, 0.054), (0, 0, 0.068), 0.004, m["nickel"], n=8, bevel=0, name="plunger", low=0))
    knob = cook.uv_sphere(10, 5, 0.008, Matrix.Translation((0, 0, 0.072)))
    p.append(make._finish(knob, m["nickel"], 0, 1, "knob"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# The pub (note 1016).

def placed(make_parts, at):
    """Parts made about the origin (a bottle), and their game-mesh twins, moved by the matrix `at`."""
    n0 = len(make.LOW)
    parts = [o for o in make_parts() if o is not None]
    for o in parts + make.LOW[n0:]:
        o.data.transform(at)
    return parts


def handpump(m, at, seed):
    """A beer engine's handle on the counter's top at `at`: a brass collar and a tall turned porcelain handle, a brass
    ferrule, the brewer's clip on its front (to -X); the swan neck over a drip tray is on the barman's side."""
    x, y, z = at
    p = []
    p.append(lathe([(0.0, 0.0), (0.038, 0.0), (0.04, 0.012), (0.026, 0.03), (0.022, 0.06), (0.0, 0.06)], m["brass"], at=at, n=18, low=8,
                   name="pump_collar"))
    lean = 0.035 * (1 if seed % 2 else -1) * 0.3
    prof = [(0.0, 0.0), (0.016, 0.0), (0.017, 0.03), (0.013, 0.05), (0.016, 0.1), (0.021, 0.2), (0.022, 0.26), (0.018, 0.3), (0.012, 0.32),
            (0.0, 0.325)]
    p.append(make.cyl((x, y, z + 0.06), (x, y, z + 0.085), 0.017, m["brass"], n=12, bevel=0.002, name="ferrule", low=6))
    p.append(lathe(prof, m["china"], at=(x + lean, y, z + 0.085), n=16, low=8, name="pump_handle"))
    # The clip: an oval badge on the handle's front, its brewer's colours.
    clip = [(0.05 * math.cos(a), 0.035 * math.sin(a)) for a in [k * 2 * math.pi / 16 for k in range(16)]]
    tint = ("ink", "gilt", "bottle_green")[seed % 3]
    p.append(slab(clip, 0.006, m[tint], (x + lean - 0.026, y, z + 0.27), (0, -1, 0), (0, 0, 1), name="pump_clip", bevel=0.002))
    rim = [(0.04 * math.cos(a), 0.027 * math.sin(a)) for a in [k * 2 * math.pi / 16 for k in range(16)]]
    p.append(slab(rim, 0.002, m["paper"], (x + lean - 0.03, y, z + 0.27), (0, -1, 0), (0, 0, 1), name="pump_clip_face", bevel=0, low=False))
    # The swan neck, out over the drip tray behind.
    p += make.pipe([(x + 0.02, y, z + 0.05), (x + 0.1, y, z + 0.09), (x + 0.16, y, z + 0.05), (x + 0.16, y, z + 0.0)], 0.007, m["brass"],
                   name="swan_neck", n=8, low=0)
    return p


def pint(m, at, full=0.0):
    """A dimpled pint pot (a straight jug of a glass, its handle to +X); `full` of it beer, a collar of head on it."""
    x, y, z = at
    p = [lathe([(0.0, 0.0), (0.036, 0.0), (0.04, 0.13), (0.037, 0.13), (0.032, 0.008), (0.0, 0.008)], m["glass"], at=at, n=16, low=8,
               name="pint")]
    if full > 0:
        h = 0.12 * full
        p.append(lathe([(0.0, 0.0), (0.033, 0.0), (0.036, h), (0.0, h)], m["bottle_amber"], at=(x, y, z + 0.008), n=16, low=0, name="beer"))
        p.append(lathe([(0.0, 0.0), (0.0365, 0.0), (0.037, 0.012), (0.0, 0.014)], m["cloth"], at=(x, y, z + 0.008 + h), n=16, low=0,
                       name="head"))
    p.append(make.torus((x + 0.045, y, z + 0.07), (0, 1, 0), 0.03, 0.006, m["glass"], n=12, m=5, name="pint_handle", low=(6, 3)))
    return p


def pub_counter(m):
    p = []
    hd, hl, top = PUB_D / 2, PUB_L / 2, PUB_H
    fx = -hd  # the front face, to the aisle
    # The run's carcass: a plinth set back, the body, open behind on the barman's side.
    p.append(make.box((0.02, 0, 0.05), (hd - 0.04, hl - 0.02, 0.05), m["dark"], bevel=0.006, name="plinth"))
    p.append(make.box((0, 0, 0.11), (hd - 0.01, hl - 0.005, 0.016), m["dark"], bevel=0.006, name="base_moulding"))
    p.append(make.box((fx + 0.02, 0, 0.55), (0.02, hl - 0.01, 0.43), m["dark"], bevel=0.004, name="front"))
    p.append(make.box((0, -hl + 0.02, 0.55), (hd - 0.02, 0.02, 0.43), m["dark"], bevel=0.004, name="rear_end"))
    for z in (0.16, 0.5, 0.82):
        p.append(make.box((0.06, 0, z), (hd - 0.1, hl - 0.04, 0.012), m["wood"], bevel=0.003, name="shelf"))
    make.low_box((hd - 0.06, 0, 0.55), (0.005, hl - 0.04, 0.43), m["dark"], name="back_shadow")
    # The front in eight panels between pilasters; the rear end in one.
    bays = 8
    bay = (PUB_L - 0.04) / bays
    for k in range(bays):
        p += panel(m, (fx, -hl + 0.02 + bay * (k + 0.5), 0.56), bay - 0.07, 0.72, (-1, 0, 0))
    p += panel(m, (0, -hl, 0.56), PUB_D - 0.1, 0.72, (0, -1, 0))
    for k in range(bays + 1):
        y = -hl + 0.02 + bay * k
        p.append(make.box((fx - 0.012, y, 0.56), (0.014, 0.03, 0.42), m["wood"], bevel=0.006, name="pilaster"))
        p.append(make.box((fx - 0.02, y, 0.97), (0.022, 0.04, 0.022), m["wood"], bevel=0.006, name="capital"))
        p.append(make.box((fx - 0.02, y, 0.16), (0.022, 0.04, 0.03), m["wood"], bevel=0.006, name="pilaster_foot"))
    # The return across the front end (+Y) to the wall, its face to the room's front, panelled in two, its top as the run's;
    # the run's top and the return's meet in the corner.
    rx0, rx1 = hd, hd + PUB_RETURN
    ry = hl - PUB_D / 2
    p.append(make.box(((rx0 + rx1) / 2, ry, 0.05), (PUB_RETURN / 2, hd - 0.04, 0.05), m["dark"], bevel=0.006, name="return_plinth"))
    p.append(make.box(((rx0 + rx1) / 2, hl - 0.02, 0.55), (PUB_RETURN / 2, 0.02, 0.43), m["dark"], bevel=0.004, name="return_front"))
    p.append(make.box(((rx0 + rx1) / 2, ry - hd + 0.02, 0.55), (PUB_RETURN / 2, 0.02, 0.43), m["dark"], bevel=0.004, name="return_back"))
    p += panel(m, (fx, hl, 0.56), PUB_D - 0.1, 0.72, (0, 1, 0))
    for k in range(2):
        p += panel(m, (rx0 + PUB_RETURN / 4 * (2 * k + 1), hl, 0.56), PUB_RETURN / 2 - 0.07, 0.72, (0, 1, 0))
    # Under the top a moulding; the top thick and bullnosed, mahogany, overhanging the front and the ends.
    p.append(make.box((0, 0, 1.0), (hd + 0.005, hl + 0.005, 0.02), m["wood"], bevel=0.008, name="moulding"))
    p.append(make.box((-0.02, 0, top - 0.03), (hd + 0.04, hl + 0.03, 0.03), m["dark"], bevel=0.022, segments=4, name="top"))
    p.append(make.box(((rx0 + rx1) / 2 + 0.02, ry, top - 0.03), (PUB_RETURN / 2 + 0.02, hd + 0.04, 0.03), m["dark"], bevel=0.022, segments=4,
                      name="return_top"))
    p.append(make.cyl((fx - 0.065, -hl - 0.02, top - 0.03), (fx - 0.065, hl + 0.02, top - 0.03), 0.006, m["brass"], n=10, bevel=0,
                      name="nosing", low=0))
    # The foot rail on its brackets, out from the front, the length of the run.
    rail_x, rail_z = fx - 0.17, 0.2
    p.append(make.cyl((rail_x, -hl + 0.08, rail_z), (rail_x, hl - 0.05, rail_z), 0.024, m["brass"], n=16, bevel=0, name="foot_rail", low=8))
    for y in (-hl + 0.08, hl - 0.05):
        end = cook.uv_sphere(12, 6, 0.028, Matrix.Translation((rail_x, y, rail_z)))
        p.append(make._finish(end, m["brass"], 0, 1, "rail_end"))
    for k in range(5):
        y = -hl + 0.15 + (PUB_L - 0.3) * k / 4
        p += make.pipe([(fx, y, 0.32), (fx - 0.1, y, 0.3), (rail_x, y, rail_z + 0.02)], 0.01, m["brass"], name="bracket", n=8, low=5)
        p.append(make.cyl((fx + 0.002, y, 0.32), (fx - 0.012, y, 0.32), 0.03, m["brass"], n=12, bevel=0.002, name="rose", low=6))
    # On the top: the drip tray and three beer engines a little behind the middle, pints, a cloth, the bar bell's place
    # (the bell's its own piece) left clear at the rear.
    p.append(make.box((0.06, 0.0, top + 0.006), (0.08, 0.34, 0.006), m["brass"], bevel=0.003, name="drip_tray"))
    for k, y in enumerate((-0.24, 0.0, 0.24)):
        p += handpump(m, (-0.04, y, top), k)
    p += pint(m, (-0.12, 0.55, top), full=0.85)
    p += pint(m, (-0.08, 0.68, top), full=0.4)
    p += pint(m, (0.1, -0.5, top))
    p += bottle(m, (0.05, 1.2, top), "whisky", 3)
    p.append(tumbler(m, (-0.1, 1.08, top)))
    p.append(make.box((0.05, -0.9, top + 0.008), (0.07, 0.11, 0.008), m["cloth"], bevel=0.006, segments=2, name="bar_cloth"))
    # Behind, on the barman's side, glasses turned down along the shelves.
    for z in (0.172, 0.512):
        for k in range(12):
            p.append(tumbler(m, (0.12, -hl + 0.25 + k * 0.36, z), down=True))
    return p


def pub_back(m):
    p = []
    d, hl = PUB_BACK_D, PUB_BACK_L / 2
    face = -d  # its face, to the walkway (-X); the wall at 0
    # A shelf at the counter's height on brackets, glasses and a jar on it; under it the wall's panelled dado.
    p.append(make.box((face / 2, 0, 1.06), (d / 2 + 0.01, hl, 0.02), m["dark"], bevel=0.008, segments=2, name="ledge"))
    bays = 6
    bay = PUB_BACK_L / bays
    make.low_box((-0.006, 0, 0.53), (0.006, hl, 0.47), m["dark"], name="dado")
    for k in range(bays):
        y = -hl + bay * (k + 0.5)
        p += panel(m, (-0.01, y, 0.55), bay - 0.06, 0.8, (-1, 0, 0), depth=0.01)
        p.append(make.box((face / 2, y - bay / 2 + 0.04, 0.98), (d / 2 - 0.02, 0.02, 0.06), m["wood"], bevel=0.006, name="ledge_bracket"))
    # Columns between mirrored bays, three shelves of bottles across them, the optics on a rail under the cornice.
    for k in range(bays + 1):
        y = -hl + bay * k
        p.append(lathe([(0.0, 0.0), (0.03, 0.0), (0.03, 0.05), (0.022, 0.08), (0.02, 1.0), (0.026, 1.03), (0.03, 1.12), (0.0, 1.12)],
                       m["wood"], at=(face + 0.03, y, 1.08), n=12, low=6, name="column"))
    p.append(make.box((-0.006, 0, 1.68), (0.004, hl - 0.02, 0.56), m["mirror"], bevel=0.001, name="mirror"))
    kinds = ("gin", "whisky", "rum", "wine", "whisky", "brandy", "gin", "jar", "rum", "whisky", "gin", "wine")
    seed = 0
    for i, z in enumerate((1.12, 1.44, 1.76)):
        p.append(make.box((face / 2, 0, z - 0.012), (d / 2, hl - 0.03, 0.012), m["wood"], bevel=0.004, name="shelf"))
        p.append(make.box((face + 0.006, 0, z - 0.018), (0.006, hl - 0.03, 0.018), m["brass"], bevel=0.002, name="shelf_lip"))
        y = -hl + 0.08
        while y < hl - 0.08:
            kind = kinds[(seed * 5 + i) % len(kinds)]
            if kind == "wine" and z > 1.5:
                kind = "rum"
            # The bottles' labels to the room: each one's turned so its -Y face is to -X.
            p += placed(lambda: bottle(m, (0, 0, 0), kind, seed), Matrix.Translation((face / 2 - 0.005, y, z)) @ Matrix.Rotation(-math.pi / 2, 4, "Z"))
            y += 0.11 if kind in ("whisky", "jar", "brandy") else 0.095
            seed += 1
    # The optics: spirits upside down in their brackets, a measure under each.
    for k in range(8):
        y = -hl + 0.4 + k * (PUB_BACK_L - 0.8) / 7
        p += placed(lambda: bottle(m, (0, 0, 0), ("gin", "whisky", "rum", "brandy")[k % 4], 40 + k),
                    Matrix.Translation((face / 2 - 0.01, y, 2.28)) @ Matrix.Rotation(math.pi, 4, "X") @ Matrix.Rotation(-math.pi / 2, 4, "Z"))
        p.append(make.cyl((face / 2 - 0.01, y, 2.0), (face / 2 - 0.01, y, 1.96), 0.018, m["glass"], n=10, bevel=0.001, name="measure", low=6))
        p.append(make.cyl((face / 2 - 0.01, y, 1.96), (face / 2 - 0.01, y, 1.94), 0.008, m["nickel"], n=8, bevel=0, name="tap", low=0))
    p.append(make.box((face / 2, 0, 2.3), (d / 2, hl - 0.03, 0.012), m["brass"], bevel=0.003, name="optic_rail"))
    # The cornice, dentilled, and the board on it.
    p.append(make.box((face / 2 - 0.01, 0, 2.36), (d / 2 + 0.02, hl + 0.02, 0.04), m["wood"], bevel=0.012, segments=3, name="cornice"))
    for k in range(int(PUB_BACK_L / 0.06)):
        p.append(make.box((face - 0.02, -hl + 0.03 + k * 0.06, 2.31), (0.012, 0.015, 0.012), m["dark"], bevel=0.002, name="dentil", low=False))
    p.append(make.box((face / 2 - 0.02, 0, 2.43), (d / 2 + 0.03, hl + 0.04, 0.025), m["dark"], bevel=0.01, segments=2, name="cornice_cap"))
    p.append(make.box((face - 0.035, 0, 2.53), (0.012, 0.85, 0.075), m["ebony"], bevel=0.006, name="board"))
    p.append(make.stencil("THE DINING CAR", (face - 0.048, 0, 2.53), (-1, 0, 0), (0, 0, 1), 0.075, m["gilt"], name="board_word"))
    return p


def pub_stool(m):
    p = []
    seat = STOOL_H
    # Four turned legs splayed out to the floor, a brass foot ring and a stretcher ring under it.
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        foot = Vector((0.19 * math.cos(a), 0.19 * math.sin(a), 0.0))
        head = Vector((0.12 * math.cos(a), 0.12 * math.sin(a), seat - 0.06))
        p.append(beam(foot, head, 0.016, 0.016, m["wood"], name="leg"))
        p.append(make.cyl(foot, foot + Vector((0, 0, 0.02)), 0.02, m["brass"], n=8, bevel=0.002, name="ferrule", low=0))
    p.append(make.torus((0, 0, 0.3), (0, 0, 1), 0.165, 0.011, m["brass"], n=28, m=8, name="foot_ring", low=(10, 4)))
    p.append(make.torus((0, 0, 0.52), (0, 0, 1), 0.138, 0.012, m["wood"], n=28, m=8, name="stretcher", low=(10, 4)))
    # The seat: an apron, the board, the buttoned leather cushion nailed round in brass.
    p.append(make.cyl((0, 0, seat - 0.07), (0, 0, seat - 0.04), 0.15, m["dark"], n=24, bevel=0.006, name="apron", low=10))
    p.append(make.cyl((0, 0, seat - 0.045), (0, 0, seat - 0.025), 0.18, m["wood"], n=28, bevel=0.008, name="seat_board", low=12))
    p.append(lathe([(0.0, 0.0), (0.172, 0.0), (0.176, 0.012), (0.165, 0.03), (0.12, 0.042), (0.0, 0.046)], m["leather"], at=(0, 0, seat - 0.025),
                   n=28, low=12, name="cushion"))
    for k in range(5):
        a = k * 2 * math.pi / 5
        p.append(make.cyl((0.08 * math.cos(a), 0.08 * math.sin(a), seat + 0.012), (0.08 * math.cos(a), 0.08 * math.sin(a), seat + 0.018),
                          0.007, m["leather"], n=8, bevel=0.002, r1=0.004, name="button", low=0))
    for k in range(24):
        a = k * 2 * math.pi / 24
        n = (math.cos(a), math.sin(a), 0)
        p.append(make.nail((0.176 * math.cos(a), 0.176 * math.sin(a), seat - 0.016), n, m["brass"], r=0.005))
    return p

# ----------------------------------------------------------------------------------------------------------------

def build(name, fn, what, budget, size=512, grime=0.25):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    out = fn(materials())
    parts, sockets = out if isinstance(out, tuple) else (out, None)
    parts = [o for o in parts if o is not None]
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.015, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=grime, made=make.provenance("dining_car", what), sockets=sockets)


PIECES = {
    "dining_table": lambda: build("dining_table", table, "the dining car's table under its cloth", 1800, size=1024, grime=0.1),
    "dining_chair": lambda: build("dining_chair", chair, "a dining car's chair", 1100),
    "dining_place": lambda: build("dining_place", place, "a place laid at the dining car's table", 700, grime=0.1),
    "dining_candelabrum": lambda: build("dining_candelabrum", candelabrum, "the table's candelabrum", 700),
    "dining_chandelier": lambda: build("dining_chandelier", chandelier, "the dining car's chandelier", 2600, size=1024),
    "bar_bell": lambda: build("bar_bell", bell, "the bar's service bell", 300, size=256),
    "pub_counter": lambda: build("pub_counter", pub_counter, "the dining car's pub counter, its return and its beer engines", 6500, size=2048),
    "pub_back": lambda: build("pub_back", pub_back, "the pub's back bar: mirrors, bottles and optics", 7800, size=2048),
    "pub_stool": lambda: build("pub_stool", pub_stool, "a bar stool", 900),
}
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
