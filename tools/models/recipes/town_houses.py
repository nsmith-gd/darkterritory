"""The dead towns' houses (GDD v1.2 §11.3 towns and halts; level-design P9), modelled, for TownKit.House: a frontier
town's, weatherboarded, abandoned. A playtest found the old box houses flat and untextured-looking in the fog
(ARCHITECTURE §8 note 139); these read by their lines: the courses of the boards, the white trim round the windows,
the raw new boards nailed across them, the porch's posts.

  * house_0: two storeys, weatherboard once painted white and gone grey, a front porch on posts, its roof sagging; the
    ground floor's windows boarded, the upper ones' glass broken; a brick chimney at the end;
  * house_1: a one-storey cottage, its boards barn-red and peeling, a lean-to off its side, a stove pipe through the
    roof, the door hanging open;
  * house_2: two storeys of sooted brick, white-painted window heads and sills, a slate roof, a timber porch;
  * house_3: a one-storey weatherboard house fallen in: half its roof down inside it, the gable end leaning out.

Axes (Blender): +Z up, the front (+Y, the engine's -Z) toward the line, the footprint centred on the origin, the
ground at z = 0 (the foundation runs 0.4 m under it for uneven ground).

    tools/models/build.sh town_houses
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
        "white": make.lib("wood_grey", 1.0, tint=(1.15, 1.12, 1.05), rough=0.8),
        "red": make.lib("paint_oxide", 0.5, tint=(0.75, 0.42, 0.34), rough=0.75),
        "raw": make.lib("wood_sleeper", 1.0, tint=(1.1, 0.95, 0.75), rough=0.85),
        "grey": make.lib("wood_grey", 1.0, tint=(0.8, 0.78, 0.74), rough=0.85),
        "trim": make.lib("wood_grey", 2.0, tint=(1.35, 1.32, 1.25), rough=0.7),
        "brick": make.lib("brick_soot", 0.5, rough=0.85),
        "stone": make.lib("rock_cliff", 0.6, tint=(0.8, 0.78, 0.74), rough=0.9),
        "slate": make.lib("roof_slate", 0.6, rough=0.6),
        "iron": make.lib("corrugated_iron", 0.6, tint=(0.75, 0.72, 0.68), rough=0.55, metal=0.4),
        "rust": make.lib("rust_heavy", 0.8, tint=(0.6, 0.5, 0.45), rough=0.6),
        "glass": make.flat("house_glass", (0.03, 0.035, 0.04), rough=0.08),
        "dark": make.flat("house_dark", (0.012, 0.011, 0.01), rough=0.9),
    }


def place(o, m):
    """Moves a part and its game-mesh twin (the last LOW) by matrix m."""
    o.data.transform(m)
    return o


def rbox(centre, half, mat, rot=None, name="box", bevel=0.008, low=True):
    """A box turned by `rot` (a Matrix) about its own centre, then put at `centre`."""
    o = make.box((0, 0, 0), half, mat, bevel=bevel, name=name, rot=rot, low=low)
    t = Matrix.Translation(Vector(centre))
    o.data.transform(t)
    if low:
        make.LOW[-1].data.transform(t)
    return o


def wall_boards(p, x0, x1, y, z0, z1, mat, facing, course=0.2, gable=None):
    """A wall of weatherboards along X (at depth y, `facing` +1 out to +Y, -1 to -Y), course by course, each board
    lapped (its bottom edge out); `gable` (ridge height): a gable end, the courses narrowing to the ridge."""
    z = z0
    k = 0
    while z < (gable if gable else z1) - 1e-3:
        top = min(z + course, gable if gable else z1)
        if gable and z >= z1:
            f = (z - z1) / (gable - z1)
            ft = (top - z1) / (gable - z1)
            half = (x1 - x0) / 2 * (1 - (f + ft) / 2)
            cx = (x0 + x1) / 2
            a, b = cx - half, cx + half
        else:
            a, b = x0, x1
        if b - a > 0.05:
            p.append(rbox(((a + b) / 2, y + facing * 0.012, (z + top) / 2), ((b - a) / 2, 0.012, (top - z) / 2 + 0.004), mat,
                          rot=Matrix.Rotation(facing * 0.06, 4, "X"), name=f"board", bevel=0.004, low=False))
        z = top
        k += 1
    return p


def side_boards(p, y0, y1, x, z0, z1, mat, facing, course=0.2):
    """A wall along Y at x (`facing` +1 out to +X)."""
    z = z0
    while z < z1 - 1e-3:
        top = min(z + course, z1)
        p.append(rbox((x + facing * 0.012, (y0 + y1) / 2, (z + top) / 2), (0.012, (y1 - y0) / 2, (top - z) / 2 + 0.004), mat,
                      rot=Matrix.Rotation(-facing * 0.06, 4, "Y"), name="board", bevel=0.004, low=False))
        z = top
    return p


def shell(p, w, d, h, mat, rise=None):
    """The walls' game-mesh box (the boards bake onto it), and the gables' triangles over its front and back."""
    make.low_box((0, 0, h / 2), (w / 2, d / 2, h / 2), mat, name="walls")
    if rise:
        for sy in (-1, 1):
            mesh = bpy.data.meshes.new("gable_low")
            y = sy * (d / 2 + 0.01)
            verts = [(-w / 2, y, h), (w / 2, y, h), (0, y, h + rise), (-w / 2, y - sy * 0.2, h), (w / 2, y - sy * 0.2, h), (0, y - sy * 0.2, h + rise)]
            faces = [(0, 1, 2) if sy > 0 else (0, 2, 1), (3, 5, 4) if sy > 0 else (3, 4, 5)]
            mesh.from_pydata(verts, [], faces)
            mesh.materials.append(mat)
            o = bpy.data.objects.new("gable_low", mesh)
            bpy.context.scene.collection.objects.link(o)
            make.LOW.append(o)


def window(p, m, at, face, size=(0.8, 1.15), state="glass", trim="trim"):
    """A window in a wall: its frame and sill in white trim, and the glass, the dark of it broken, or boards nailed
    across it. `face` +-Y or +-X (a Vector), `at` its middle on the wall."""
    face = Vector(face)
    along = Vector((0, 0, 1)).cross(face).normalized()
    ww, hh = size
    at = Vector(at)

    def put(o_local, half, mat, rot=None, low=False):
        c = at + along * o_local[0] + face * o_local[1] + Vector((0, 0, o_local[2]))
        R = Matrix((along, face, (0, 0, 1))).transposed().to_4x4()
        if rot is not None:
            R = R @ rot
        p.append(rbox(c, half, mat, rot=R, name="win", bevel=0.004, low=low))

    put((0, 0.005, 0), (ww / 2, 0.01, hh / 2), m["glass"] if state == "glass" else m["dark"])
    for sx in (-1, 1):
        put((sx * (ww / 2 + 0.04), 0.03, 0), (0.05, 0.03, hh / 2 + 0.06), m[trim])
    put((0, 0.03, hh / 2 + 0.06), (ww / 2 + 0.1, 0.035, 0.06), m[trim])
    put((0, 0.06, -hh / 2 - 0.05), (ww / 2 + 0.12, 0.07, 0.03), m[trim])
    if state == "glass":
        put((0, 0.02, 0), (0.015, 0.015, hh / 2), m[trim])
        put((0, 0.02, 0), (ww / 2, 0.015, 0.015), m[trim])
    if state == "boarded":
        for k, (a, dz) in enumerate(((0.5, 0.25), (-0.45, -0.1), (0.1, -0.35))):
            put((0, 0.07 + 0.01 * k, dz), (ww / 2 + 0.15, 0.012, 0.075), m["raw"], rot=Matrix.Rotation(a, 4, "Y"))


def door(p, m, at, face, open_=False):
    face = Vector(face)
    along = Vector((0, 0, 1)).cross(face).normalized()
    at = Vector(at)
    R = Matrix((along, face, (0, 0, 1))).transposed().to_4x4()
    p.append(rbox(at + face * 0.005 + Vector((0, 0, 1.05)), (0.48, 0.01, 1.05), m["dark"], rot=R, name="doorway", low=False))
    for sx in (-1, 1):
        p.append(rbox(at + along * sx * 0.53 + face * 0.03 + Vector((0, 0, 1.1)), (0.05, 0.03, 1.1), m["trim"], rot=R, name="door_trim", low=False))
    p.append(rbox(at + face * 0.03 + Vector((0, 0, 2.24)), (0.62, 0.035, 0.06), m["trim"], rot=R, name="door_head", low=False))
    leaf = at - along * 0.45 + face * (0.45 if open_ else 0.02) + Vector((0, 0, 1.03))
    Rl = R @ Matrix.Rotation(-1.2 if open_ else 0, 4, "Z")
    o = rbox((0, 0, 0), (0.45, 0.025, 1.0), m["grey"], rot=Rl, name="door", bevel=0.006, low=False)
    o.data.transform(Matrix.Translation(leaf + (along * 0.45 if not open_ else (Rl.to_3x3() @ Vector((0.45, 0, 0))))))
    p.append(o)
    p.append(rbox(at + face * 0.25 + Vector((0, 0, 0.06)), (0.6, 0.25, 0.06), m["stone"], name="step", low=True))


def gable_roof(p, w, d, h, pitch, mat, over=0.35, broken=None, sag=0.0):
    """Two slopes over w (X) x d (Y), the ridge along Y, eaves at h overhanging by `over`; `broken` (y0, y1): that run
    of the +X slope fallen in; `sag` lets the whole roof down in its middle. Returns the ridge's rise over the eaves."""
    rise = w / 2 * math.tan(pitch)
    drop = over * math.tan(pitch)
    run = math.hypot(w / 2 + over, rise + drop)
    for sx in (-1, 1):
        R = Matrix.Rotation(sx * pitch, 4, "Y")
        centre = Vector((sx * (w / 2 + over) / 2, 0, h + (rise - drop) / 2 - sag))
        segs = [(-d / 2 - over, d / 2 + over)]
        if broken and sx > 0:
            segs = [(-d / 2 - over, broken[0]), (broken[1], d / 2 + over)]
        for y0, y1 in segs:
            if y1 - y0 < 0.1:
                continue
            p.append(rbox((centre.x, (y0 + y1) / 2, centre.z), (run / 2, (y1 - y0) / 2, 0.05), mat, rot=R, name="roof", bevel=0.01))
            # Its courses, for the lines: a lip along the slope every 0.32 m.
            n = int(run / 0.32)
            for k in range(1, n):
                q = centre + (R.to_3x3() @ Vector((run * (k / n - 0.5), 0, 0.055)))
                p.append(rbox((q.x, (y0 + y1) / 2, q.z), (0.012, (y1 - y0) / 2, 0.01), mat, rot=R, name="course", bevel=0.002, low=False))
    p.append(rbox((0, 0, h + rise + 0.02 - sag), (0.08, d / 2 + over, 0.06), mat, name="ridge", bevel=0.01))
    return rise


def chimney(p, m, at, top):
    at = Vector(at)
    p.append(rbox(at + Vector((0, 0, top / 2)), (0.35, 0.3, top / 2), m["brick"], name="chimney", bevel=0.01))
    p.append(rbox(at + Vector((0, 0, top + 0.06)), (0.42, 0.37, 0.06), m["brick"], name="chimney_cap", bevel=0.01))
    p.append(make.cyl(at + Vector((0.12, 0, top + 0.1)), at + Vector((0.12, 0, top + 0.35)), 0.09, m["rust"], n=10, name="pot", low=6))


def porch(p, m, w, d, h, sag=0.0, depth=1.8):
    y = d / 2
    p.append(rbox((0, y + depth / 2, 0.3), (w / 2, depth / 2, 0.06), m["grey"], name="porch_floor"))
    for k in range(int(w / 0.18)):
        x = -w / 2 + 0.09 + k * 0.18
        p.append(rbox((x, y + depth / 2, 0.37), (0.085, depth / 2, 0.012), m["grey"], name="deck", bevel=0.003, low=False))
    for x in (-w / 2 + 0.15, -w / 6, w / 6, w / 2 - 0.15):
        p.append(rbox((x, y + depth - 0.15, 1.45), (0.07, 0.07, 1.1), m["trim"], name="post"))
    a = 0.22
    sl = 0.08 + sag
    p.append(rbox((0, y + depth / 2 + 0.1, 2.6 - sl), (w / 2 + 0.2, depth / 2 + 0.35, 0.04), m["iron"],
                  rot=Matrix.Rotation(-a, 4, "X") @ Matrix.Rotation(sag * 0.4, 4, "Y"), name="porch_roof"))
    for k in range(3):
        p.append(rbox((0, y + depth + 0.15 + k * 0.28, 0.24 - k * 0.09), (0.7, 0.14, 0.03), m["grey"], name="porch_step"))
    # The railing, a few balusters gone.
    for sx in (-1, 1):
        p.append(rbox((sx * w / 3.2, y + depth - 0.15, 1.15), (w / 6.5, 0.03, 0.03), m["trim"], name="rail"))
        for k in range(7):
            if (k * 3 + sx) % 4 == 0:
                continue
            p.append(rbox((sx * (w / 6 + 0.15 + k * 0.16), y + depth - 0.15, 0.75), (0.02, 0.02, 0.38), m["trim"], name="baluster", low=False))


def foundation(p, m, w, d):
    p.append(rbox((0, 0, -0.1), (w / 2 + 0.06, d / 2 + 0.06, 0.32), m["stone"], name="foundation"))


def corner_boards(p, m, w, d, h):
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(rbox((sx * (w / 2 + 0.02), sy * (d / 2 + 0.02), h / 2 + 0.1), (0.07, 0.07, h / 2), m["trim"], name="corner"))


def boarded_house(m, w, d, h, boards, storeys, roof_mat, porch_=False, chimney_=True, broken=None, sag=0.0, door_open=False,
                  windows=("boarded", "glass")):
    p = []
    foundation(p, m, w, d)
    rise = w / 2 * math.tan(math.radians(38))
    shell(p, w, d, h, boards, rise)
    z0 = 0.2
    wall_boards(p, -w / 2, w / 2, d / 2, z0, h, boards, +1)
    wall_boards(p, -w / 2, w / 2, -d / 2, z0, h, boards, -1)
    side_boards(p, -d / 2, d / 2, w / 2, z0, h, boards, +1)
    side_boards(p, -d / 2, d / 2, -w / 2, z0, h, boards, -1)
    # The ridge runs front to back (the slopes face +-X), so the gables are the front's and the back's tops.
    wall_boards(p, -w / 2, w / 2, d / 2, h, h, boards, +1, gable=h + rise)
    wall_boards(p, -w / 2, w / 2, -d / 2, h, h, boards, -1, gable=h + rise)
    corner_boards(p, m, w, d, h)
    gable_roof(p, w, d, h, math.radians(38), roof_mat, broken=broken, sag=sag)
    # Windows, front and back and sides, and the door.
    for s in range(storeys):
        zc = 1.45 + s * 2.6
        state = windows[min(s, len(windows) - 1)]
        for x in (-w / 2 + 1.1, w / 2 - 1.1):
            window(p, m, (x, d / 2 + 0.02, zc), (0, 1, 0), state=state)
            window(p, m, (-x, -d / 2 - 0.02, zc), (0, -1, 0), state="dark" if s else state)
        for y in (-d / 4, d / 4):
            window(p, m, (w / 2 + 0.02, y, zc), (1, 0, 0), state="dark" if (s + int(y > 0)) % 2 else state)
            window(p, m, (-w / 2 - 0.02, y, zc), (-1, 0, 0), state=state)
    if storeys > 1:
        window(p, m, (0, d / 2 + 0.02, h + rise * 0.4), (0, 1, 0), size=(0.6, 0.7), state="dark")
    door(p, m, (0, d / 2 + 0.02, 0.2), (0, 1, 0), open_=door_open)
    if porch_:
        porch(p, m, w, d, h, sag=sag)
    if chimney_:
        chimney(p, m, (-w / 2 + 0.6, -d / 2 + 0.6, 0), h + rise + 0.9)
    return p


def house_0(m):
    return boarded_house(m, 6.6, 7.2, 5.4, m["white"], 2, m["iron"], porch_=True, sag=0.12, windows=("boarded", "dark"))


def house_1(m):
    p = boarded_house(m, 5.4, 6.2, 2.9, m["red"], 1, m["iron"], chimney_=False, door_open=True, windows=("dark",))
    # The lean-to off its side, under a single slope of iron; the stove pipe through the main roof.
    lw, ld, lh = 2.4, 4.0, 2.2
    x = 5.4 / 2 + lw / 2
    p.append(rbox((x, -0.6, -0.1), (lw / 2, ld / 2, 0.3), m["stone"], name="lean_foundation"))
    make.low_box((x, -0.6, lh / 2), (lw / 2, ld / 2, lh / 2), m["grey"], name="lean_walls")
    wall_boards(p, x - lw / 2, x + lw / 2, -0.6 + ld / 2, 0.2, lh, m["grey"], +1)
    wall_boards(p, x - lw / 2, x + lw / 2, -0.6 - ld / 2, 0.2, lh, m["grey"], -1)
    side_boards(p, -0.6 - ld / 2, -0.6 + ld / 2, x + lw / 2, 0.2, lh, m["grey"], +1)
    p.append(rbox((x, -0.6, lh + 0.25), (lw / 2 + 0.3, ld / 2 + 0.25, 0.04), m["iron"], rot=Matrix.Rotation(0.2, 4, "Y"), name="lean_roof"))
    p.append(make.cyl((-1.2, -1.2, 3.6), (-1.2, -1.2, 5.6), 0.1, m["rust"], n=12, name="stovepipe", low=6))
    p.append(make.cyl((-1.2, -1.2, 5.6), (-1.2, -1.2, 5.75), 0.18, m["rust"], n=12, name="pipe_cap", r1=0.05, low=6))
    return p


def house_2(m):
    p = []
    w, d, h = 6.2, 7.0, 5.6
    foundation(p, m, w, d)
    p.append(rbox((0, 0, h / 2), (w / 2, d / 2, h / 2), m["brick"], name="walls", bevel=0.02))
    rise = w / 2 * math.tan(math.radians(40))
    for sy in (-1, 1):
        bpy.ops.mesh.primitive_cone_add(vertices=3, radius1=1, radius2=1, depth=0.25)
        o = bpy.context.view_layer.objects.active
        o.data.transform(Matrix.Translation((0, sy * (d / 2 - 0.12), h)) @ Matrix.Rotation(math.pi / 2, 4, "X") @ Matrix.Diagonal((w / 2 / 0.866, rise / 1.5 * 1.0, 1, 1))
                         @ Matrix.Rotation(-math.pi / 2, 4, "Z") @ Matrix.Translation((0.5, 0, 0)))
        o.data.materials.append(m["brick"])
        lo = o.copy()
        lo.data = o.data.copy()
        bpy.context.scene.collection.objects.link(lo)
        make.LOW.append(lo)
        p.append(o)
    gable_roof(p, w, d, h, math.radians(40), m["slate"])
    for s in range(2):
        zc = 1.45 + s * 2.6
        for x in (-w / 2 + 1.2, 0.0 if s else None, w / 2 - 1.2):
            if x is None:
                continue
            window(p, m, (x, d / 2 + 0.01, zc), (0, 1, 0), state="glass" if (s + int(x > 0)) % 2 else "dark")
        for y in (-d / 4, d / 4):
            window(p, m, (w / 2 + 0.01, y, zc), (1, 0, 0), state="dark")
            window(p, m, (-w / 2 - 0.01, y, zc), (-1, 0, 0), state="boarded" if s == 0 else "dark")
    door(p, m, (0, d / 2 + 0.02, 0.2), (0, 1, 0))
    porch(p, m, w * 0.6, d, h, depth=1.5)
    for sx in (-1, 1):
        chimney(p, m, (sx * (w / 2 - 0.4), 0, 0), h + rise * 0.3 + 1.0)
    return p


def house_3(m):
    # Fallen in: half the roof's +X slope down, its rafters and boards in a slide into the house; the gable leaning.
    p = boarded_house(m, 5.6, 6.6, 3.0, m["grey"], 1, m["iron"], chimney_=True, broken=(-1.2, 2.6), windows=("boarded",))
    for k in range(5):
        y = -1.0 + k * 0.85
        p.append(rbox((1.3, y, 2.4 - 0.25 * k % 2), (0.06, 0.06, 1.5), m["raw"], rot=Matrix.Rotation(0.9 + 0.1 * k, 4, "Y") @ Matrix.Rotation(0.15 * k, 4, "X"),
                      name="rafter"))
    p.append(rbox((1.1, 0.7, 1.4), (1.3, 1.6, 0.05), m["iron"], rot=Matrix.Rotation(0.6, 4, "Y") @ Matrix.Rotation(0.12, 4, "X"), name="fallen_roof"))
    return p


PIECES = [("house_0", house_0, "a frontier town's two-storey weatherboard house, porch, boarded"),
          ("house_1", house_1, "a frontier town's red weatherboard cottage and lean-to"),
          ("house_2", house_2, "a frontier town's brick house, slate roof, porch"),
          ("house_3", house_3, "a frontier town's weatherboard house, fallen in")]
want = set(cook.args()) or {n for n, *_ in PIECES}
for name, fn, what in PIECES:
    if name not in want:
        continue
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, f"{name}_low", 3000, colour=None, size=1024, cage=0.01, reach=0.04, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=3200, grime=0.6, made=make.provenance("town_houses", what))
