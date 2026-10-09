"""DAVE'S THINGS: HIS HATS, HIS BRUSH AND HIS PALETTE (ARCHITECTURE §8 note 491; the director, 8 Oct: "He loves vests
and cool hats. Players often find him wearing different cool hats and vests"): five hats, in P1's DaveKit.Hats order,
each worn on the head bone of tools/models/recipes/dave.py's figure and modelled in its bind pose round Lee Perry-Smith's
scan where it sits on him (crewfigure.place_scan), so the crown goes round his head everywhere with 9 mm to spare:

  * dave_hat_0, the wide straw: a sun hat in coiled braid, a low round crown, its brim the widest thing on any head out
    there and a little wavy at the edge, a red ribbon round it tied in a bow;
  * dave_hat_1, the beret: plum wool felt, full, pulled down over his right ear, its little stalk on top;
  * dave_hat_2, the fedora: grey felt, the crown creased down the middle and pinched at the front, the brim snapped
    down at the front and up behind, a black band and a pheasant's feather in it;
  * dave_hat_3, the bucket hat: faded denim in six panels, the brim sloping down all round, stitched in rows, a brass
    eyelet either side;
  * dave_hat_4, the panama: fine cream straw, creased and pinched like the fedora, a black band, the brim down at the
    front.

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh dave_kit
    DAVE_KIT=dave_hat_2 tools/models/build.sh dave_kit        # one of them
    DAVE_KIT_PREVIEW=1 tools/models/build.sh dave_kit         # every hat on the scan, rendered to out/review, no bake
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
import crewfigure  # noqa: E402
import make  # noqa: E402

HEAD_C = Vector((0, 0.02, 1.675))   # tools/blender/crew.py's, where the scan is placed
YC = 0.016                          # the scan's middle front to back, the hats' axis
N_HIGH, N_LOW = 96, 24              # sectors round a hat, the bake's copy and the game's
# Air between a hat and the scan: the game's head is the scan collapsed to 2,000 triangles, which can stand a millimetre
# or two proud of it, and the game's hat is flat between its rings, which cuts in a few more.
CLEAR = 0.009


# ----------------------------------------------------------------------------------------------------------------
# His head, and what a hat has to go round

def _head():
    cook.reset()
    head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
    crewfigure.place_scan(head, HEAD_C)
    co = np.concatenate([np.array([v.co for v in o.data.vertices], np.float32) for o in head])
    cook.delete(head)
    return co


HEAD = _head()
ZS = np.arange(1.58, 1.81, 0.0025)
SECTORS = 64


def _outline():
    """For each height on the grid, the head's reach in each of SECTORS directions about its middle from that height up:
    what a crown at that height has to go round (spread to its neighbours, so nothing slips between two sectors)."""
    a = np.arctan2(HEAD[:, 0], HEAD[:, 1] - YC)
    r = np.hypot(HEAD[:, 0], HEAD[:, 1] - YC)
    idx = np.round(a / (2 * np.pi) * SECTORS).astype(int) % SECTORS
    out = np.zeros((len(ZS), SECTORS), np.float32)
    for i, z in enumerate(ZS):
        above = HEAD[:, 2] >= z
        row = np.zeros(SECTORS, np.float32)
        np.maximum.at(row, idx[above], r[above])
        row = np.max([np.roll(row, k) for k in (-2, -1, 0, 1, 2)], axis=0)
        # Evened round (a scan's outline is ragged sector to sector; a crown that followed it would be pleated), and
        # never inside the ragged one.
        even = np.mean([np.roll(row, k) for k in range(-4, 5)], axis=0)
        out[i] = np.maximum(even, row - 0.0015)
    return out


OUTLINE = _outline()
RINGS = 0.005     # the height map's rings, out from the axis


def _tops():
    """The head's height in each SECTORS direction at each ring out from its middle (spread to the neighbours): what a
    crown's top has to go over."""
    a = np.arctan2(HEAD[:, 0], HEAD[:, 1] - YC)
    r = np.hypot(HEAD[:, 0], HEAD[:, 1] - YC)
    s = np.round(a / (2 * np.pi) * SECTORS).astype(int) % SECTORS
    ri = np.minimum((r / RINGS).astype(int), 39)
    out = np.full((40, SECTORS), ZS[0], np.float32)
    np.maximum.at(out, (ri, s), HEAD[:, 2])
    # Each ring spread round over a centimetre of its own arc (near the middle a sector's a sliver with nothing in it).
    for i in range(40):
        k = int(math.ceil(0.01 / ((i + 0.5) * RINGS * 2 * math.pi / SECTORS)))
        out[i] = np.max([np.roll(out[i], j) for j in range(-min(k, SECTORS // 2), min(k, SECTORS // 2) + 1)], axis=0)
    # And each ring at least as high as the next out (a top's middle can sit over a sparse middle of the scan).
    for i in range(38, -1, -1):
        out[i] = np.maximum(out[i], out[i + 1])
    return out


TOPS = _tops()


def reach(theta, z):
    """How far out the head reaches from z up in the direction theta (0: ahead, +x to the right), between the outline's
    sectors and heights."""
    i = int(np.clip(np.searchsorted(ZS, z) - 1, 0, len(ZS) - 2))
    fz = float(np.clip((z - ZS[i]) / (ZS[i + 1] - ZS[i]), 0, 1))
    t = np.mod(np.asarray(theta, np.float64) / (2 * np.pi) * SECTORS, SECTORS)
    s0 = np.floor(t).astype(int) % SECTORS
    s1 = (s0 + 1) % SECTORS
    f = t - np.floor(t)
    row = OUTLINE[i] * (1 - fz) + OUTLINE[i + 1] * fz
    return row[s0] * (1 - f) + row[s1] * f


def clear_head(o, gap):
    """Every vertex of `o` (a hat's surface, before it's thickened inward) at least `gap` off his head: pushed out from
    its middle, or up over its crown, whichever's the shorter way."""
    for v in o.data.vertices:
        x, y, z = v.co.x, v.co.y - YC, v.co.z
        r = math.hypot(x, y)
        if z < ZS[0]:
            continue
        th = math.atan2(x, y)
        out = float(reach(th, z)) + gap - r
        s = int(round(th / (2 * math.pi) * SECTORS)) % SECTORS
        up = float(TOPS[min(int(r / RINGS), 39), s]) + gap - z
        if out <= 0 or up <= 0:
            continue
        if up < out or r < 1e-6:
            v.co.z = z + up
        else:
            v.co.x, v.co.y = x * (r + out) / r, YC + y * (r + out) / r
    o.data.update()


def band(z, gap=0.005, n=N_HIGH):
    """The ring a hat's band sits on at z: the head's outline there and `gap` more, as radii round n sectors."""
    th = np.arange(n) * 2 * np.pi / n
    return reach(th, z) + gap


# ----------------------------------------------------------------------------------------------------------------
# Building one: rings of radii round the axis, lofted, closed at the top

def ring_at(radii, z, warp=None):
    """Points for radii round the axis at height z; warp(theta, r, z) -> (r, z) shapes it (a pinch, a snap, a tilt)."""
    n = len(radii)
    out = []
    for i, r in enumerate(radii):
        th = 2 * math.pi * i / n
        rr, zz = (r, z) if warp is None else warp(th, float(r), z)
        out.append((rr * math.sin(th), YC + rr * math.cos(th), zz))
    return out


def loft(rings, material, name, top=None):
    """A surface through rings (each the same count, round the axis), `top` (a point) closing the last."""
    bm = bmesh.new()
    vs = [[bm.verts.new(Vector(p)) for p in r] for r in rings]
    n = len(rings[0])
    for a, b in zip(vs, vs[1:]):
        for j in range(n):
            bm.faces.new((a[j], a[(j + 1) % n], b[(j + 1) % n], b[j]))
    if top is not None:
        c = bm.verts.new(Vector(top))
        for j in range(n):
            bm.faces.new((vs[-1][j], vs[-1][(j + 1) % n], c))
    # Faces away from his head (out from a crown, up from a brim): the thickness goes the other way.
    bm.normal_update()
    ref = Vector((0, YC, 1.62))
    if sum(f.normal.dot(f.calc_center_median() - ref) for f in bm.faces) < 0:
        bmesh.ops.reverse_faces(bm, faces=list(bm.faces))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(material)
    for f in me.polygons:
        f.use_smooth = True
    return o


def _modify(o, kind, **kw):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    m = o.modifiers.new(kind.lower(), kind)
    for k, v in kw.items():
        setattr(m, k, v)
    bpy.ops.object.modifier_apply(modifier=m.name)


def finish_high(o, thick, weave=None, levels=2):
    """The bake's copy of a surface: smoothed, its weave pressed in, thickened inward (felt and straw have an edge)."""
    _modify(o, "SUBSURF", levels=levels, render_levels=levels)
    if weave is not None:
        me = o.data
        co = np.array([v.co for v in me.vertices], np.float32)
        no = np.array([v.normal for v in me.vertices], np.float32)
        d = weave(co)
        for v, c, nn, dd in zip(me.vertices, co, no, d):
            v.co = Vector(c + nn * dd)
        me.update()
    _modify(o, "SOLIDIFY", thickness=thick, offset=-1.0, use_rim=True)
    for f in o.data.polygons:
        f.use_smooth = True
    return o


def finish_low(o, thick):
    _modify(o, "SOLIDIFY", thickness=thick, offset=-1.0, use_rim=True)
    return o


def hat(rings_fn, material, name, thick, weave=None, top=None):
    """A hat's surface twice, from rings_fn(n) -> (rings, top point): the bake's copy at N_HIGH sectors with its weave,
    and the game's at N_LOW; each pushed clear of his head and thickened."""
    rings, tp = rings_fn(N_HIGH)
    high = loft(rings, material, name, top=tp)
    clear_head(high, CLEAR + thick)
    finish_high(high, thick, weave)
    rings, tp = rings_fn(N_LOW)
    low = loft(rings, material, name + "_low", top=tp)
    clear_head(low, CLEAR + thick)
    finish_low(low, thick)
    make.LOW.append(low)
    return high


def lerp_rings(base, scale, n):
    """Base radii (N_HIGH of them) resampled to n sectors and scaled."""
    th = np.arange(n) / n * len(base)
    i0 = np.floor(th).astype(int) % len(base)
    f = th - np.floor(th)
    return (base[i0] * (1 - f) + base[(i0 + 1) % len(base)] * f) * scale


# ----------------------------------------------------------------------------------------------------------------
# Weaves (displacements, metres along the surface's normal, from position)

def braid(p, pitch=0.006, depth=0.0013):
    """Coiled straw braid: rows running round the hat (by height up the crown, by radius out along the brim), each a
    plait of chevrons."""
    x, y, z = p[:, 0], p[:, 1] - YC, p[:, 2]
    r = np.hypot(x, y)
    u = r + (z - 1.7) * 1.4
    th = np.arctan2(x, y)
    rows = np.abs(np.sin(np.pi * u / pitch))
    chev = np.abs(np.sin(th * r * 2 * np.pi / (pitch * 0.9) + np.sign(np.sin(np.pi * u / pitch)) * 1.2))
    return depth * (0.6 * rows + 0.4 * chev) - depth * 0.5


def felt(p):
    return 0.0004 * cook.noise_np(p, 61, 140) + 0.0003 * cook.noise_np(p, 62, 40)


def denim(p):
    """A twill's diagonal, and the stitching: rows round the brim every 7 mm, six seams up the crown."""
    x, y, z = p[:, 0], p[:, 1] - YC, p[:, 2]
    r = np.hypot(x, y)
    th = np.arctan2(x, y)
    twill = 0.00018 * np.sin((th * r + z) * 2 * np.pi / 0.0018)
    brim = (z < 1.705) & (r > 0.1)
    stitch = 0.0006 * (np.abs(np.sin(np.pi * r / 0.007)) > 0.92) * brim
    seam = -0.0008 * (np.abs(np.sin(th * 3)) < 0.03) * (z > 1.7)
    return twill + stitch + seam


def fine_straw(p):
    return braid(p, pitch=0.0032, depth=0.0005)


# ----------------------------------------------------------------------------------------------------------------
# The hats

def ribbon(z0, height, gap, material, n_high=N_HIGH, name="band", scale=1.0):
    """A band of ribbon round the crown at z0, `height` tall, standing `gap` off the head's ring there."""
    def rings(n):
        b0 = band(z0, gap, n) * scale
        b1 = band(z0 + height, gap, n) * scale
        return [ring_at(b0, z0 - 0.001), ring_at(b0 + 0.0012, z0 + 0.002), ring_at(b1 + 0.0012, z0 + height - 0.002),
                ring_at(b1, z0 + height + 0.001)], None
    return hat(rings, material, name, 0.0015, felt)


def bow(at, side, material, size=0.02):
    """A flat bow on the band at `at` (a point on it), facing out `side`: two loops and the knot, and two tails."""
    out = []
    s = Vector(side).normalized()
    t = Vector((0, 0, 1)).cross(s).normalized()
    rot = Matrix((t, Vector((0, 0, 1)), s)).transposed().to_4x4()
    for k in (-1, 1):
        loop = Matrix.Translation(at + t * k * size * 0.75 + s * 0.003) @ rot @ Matrix.Rotation(k * 0.25, 4, "Z")
        o = make.box((0, 0, 0), (size * 0.7, size * 0.45, 0.003), material, bevel=0.002, name="bow", low=True)
        o.data.transform(loop)
        make.LOW[-1].data.transform(loop)
        out.append(o)
    knot = Matrix.Translation(at + s * 0.005) @ rot
    o = make.box((0, 0, 0), (size * 0.25, size * 0.4, 0.004), material, bevel=0.002, name="knot", low=True)
    o.data.transform(knot)
    make.LOW[-1].data.transform(knot)
    out.append(o)
    for k in (-1, 1):
        tail = Matrix.Translation(at + t * k * size * 0.35 + Vector((0, 0, -size * 0.9)) + s * 0.004) @ rot @ Matrix.Rotation(k * 0.3, 4, "Y")
        o = make.box((0, 0, 0), (size * 0.22, size * 0.8, 0.0015), material, bevel=0.001, name="tail", low=True)
        o.data.transform(tail)
        make.LOW[-1].data.transform(tail)
        out.append(o)
    return out


def on_band(theta, z, gap):
    """A point on a band's ring at theta, z, and the way out from the axis there."""
    r = float(reach(theta, z)) + gap
    d = Vector((math.sin(theta), math.cos(theta), 0))
    return Vector((0, YC, z)) + d * r, d


def straw_hat(m):
    zb = 1.703
    base = band(zb, 0.006)

    def crown(n):
        b = lerp_rings(base, 1.0, n)
        rings = [ring_at(b * s, zb + h) for h, s in ((0.0, 1.0), (0.015, 1.005), (0.03, 1.01), (0.045, 1.003), (0.06, 0.99),
                                                    (0.085, 0.92), (0.1, 0.78),
                                                    (0.108, 0.55), (0.112, 0.28))]
        return rings, (0, YC, zb + 0.114)

    def brim(n):
        b = lerp_rings(base, 1.0, n)
        def wave(d):
            return lambda th, r, z: (r, z - 0.012 * (d / 0.14) ** 1.5 + 0.007 * math.sin(2 * th + 0.6) * (d / 0.14) ** 2)
        rings = [ring_at(b + d, zb + 0.001, wave(d)) for d in (0.0, 0.02, 0.05, 0.09, 0.125, 0.14)]
        # The edge rolled under a little.
        rings.append(ring_at(b + 0.143, zb - 0.004, wave(0.14)))
        return rings, None

    parts = [hat(crown, m["straw"], "crown", 0.0025, braid), hat(brim, m["straw"], "brim", 0.0025, braid)]
    parts.append(ribbon(zb + 0.002, 0.03, 0.008, m["red"], name="ribbon"))
    at, d = on_band(-math.pi / 2, zb + 0.017, 0.012)
    parts += bow(at, d, m["red"], 0.018)
    return parts


def beret(m):
    zb = 1.705      # (its band clear over his spectacles' rims at the front)
    base = band(zb, 0.004)
    pivot = Vector((0, YC, 1.73))
    # Down over his right ear, and tipped back a touch so its front rides over his spectacles.
    tilt = (Matrix.Translation(pivot + Vector((0.012, 0, -0.004))) @ Matrix.Rotation(0.26, 4, "Y") @ Matrix.Rotation(0.1, 4, "X")
            @ Matrix.Translation(-pivot))

    def body(n):
        b = lerp_rings(base, 1.0, n)
        rings = [ring_at(b * s, zb + h) for h, s in ((0.0, 1.0), (0.01, 1.07), (0.028, 1.3), (0.05, 1.42), (0.075, 1.36),
                                                    (0.095, 1.12), (0.106, 0.74), (0.111, 0.38))]
        return [[tuple(tilt @ Vector(p)) for p in r] for r in rings], tuple(tilt @ Vector((0, YC, zb + 0.113)))

    parts = [hat(body, m["plum"], "beret", 0.003, felt)]
    # The headband's piping, and the stalk on top.
    parts.append(ribbon(zb - 0.002, 0.008, 0.005, m["plum_dark"], name="piping"))
    for o in (parts[-1], make.LOW[-1]):
        o.data.transform(tilt)
        clear_head(o, CLEAR)
    top = tilt @ Vector((0, YC, zb + 0.112))
    parts.append(make.cyl(top, top + (tilt.to_3x3() @ Vector((0, 0, 1))) * 0.016, 0.0028, m["plum_dark"], n=10, bevel=0.001,
                          name="stalk", low=6))
    return parts


def creased(zb, height, base, pinch=0.012, crease=0.028):
    """A creased crown's rings (the fedora's and the panama's): tapering a little, pinched either side of the front up
    top, and its top dented down the middle front to back."""
    def rings_fn(n):
        b = lerp_rings(base, 1.0, n)
        def shape(h):
            def warp(th, r, z):
                pinch_here = pinch * (h / height) ** 1.5 * (math.exp(-((th - 0.6) / 0.35) ** 2) + math.exp(-((th + 0.6) / 0.35) ** 2))
                return r - pinch_here, z + 0.006 * (h / height) * -math.cos(th)
            return warp
        rings = [ring_at(b * s, zb + h, shape(h)) for h, s in ((0.0, 1.0), (0.02, 0.993), (0.04, 0.985), (0.06, 0.968),
                                                               (0.08, 0.95), (height, 0.9))]
        # The top: rings in towards the middle, sinking along the crease (x near 0).
        for f in (0.75, 0.5, 0.25):
            def top_warp(th, r, z, f=f):
                x = r * math.sin(th)
                return r, z - crease * (1 - f) ** 0.6 * math.exp(-(x / 0.03) ** 2) + 0.004 * (1 - f)
            rings.append(ring_at(b * 0.9 * f, zb + height, top_warp))
        return rings, (0, YC, zb + height - crease * 0.95)
    return rings_fn


def snapped_brim(zb, base, width, down=0.014, up=0.016, curl=0.006):
    """A brim snapped down at the front and turned up behind, its sides curled up a touch."""
    def rings_fn(n):
        b = lerp_rings(base, 1.0, n)
        def warp(d):
            k = d / width
            return lambda th, r, z: (r, z + k * (-down * max(0.0, math.cos(th)) ** 1.5 + up * max(0.0, -math.cos(th)) ** 2
                                                 + curl * k * math.sin(th) ** 2))
        rings = [ring_at(b + d, zb + 0.001, warp(d)) for d in (0.0, width * 0.3, width * 0.65, width)]
        return rings, None
    return rings_fn


def fedora(m):
    zb = 1.708
    base = band(zb, 0.006)
    parts = [hat(creased(zb, 0.128, base, pinch=0.016, crease=0.034), m["grey"], "crown", 0.003, felt),
             hat(snapped_brim(zb, base, 0.058), m["grey"], "brim", 0.003, felt),
             ribbon(zb + 0.002, 0.032, 0.0085, m["black"], name="band")]
    at, d = on_band(-math.pi / 2 - 0.25, zb + 0.018, 0.014)
    parts += bow(at, d, m["black"], 0.013)
    # The pheasant's feather, tucked in the band behind the bow and swept up and back: its quill, and its vane in bars.
    root = on_band(-math.pi / 2 - 0.6, zb + 0.012, 0.013)[0]
    sweep = [root + Vector((-0.004 * k, -0.018 * k, 0.017 * k - 0.0012 * k * k)) for k in range(8)]
    parts += make.pipe(sweep, 0.0011, m["quill"], name="quill", n=6, low=3)
    for k in range(1, 7):
        a, b = sweep[k], sweep[k + 1]
        dirn = (b - a).normalized()
        side = dirn.cross(Vector((1, 0, 0))).normalized()
        w = 0.011 * math.sin(math.pi * k / 7.5)
        mat = m["bar"] if k % 2 else m["feather"]
        o = make.box((a + b) / 2 + side * w * 0.3, (0.0006, w, (b - a).length / 2), mat, bevel=0, name="vane",
                     rot=Matrix((Vector((1, 0, 0)), side, dirn)).transposed().to_4x4(), low=True)
        parts.append(o)
    return parts


def bucket(m):
    zb = 1.704      # (its band over his spectacles' rims at the front)
    base = band(zb, 0.006)

    def crown(n):
        b = lerp_rings(base, 1.0, n)
        rings = [ring_at(b * s, zb + h) for h, s in ((0.0, 1.0), (0.02, 0.993), (0.04, 0.985), (0.06, 0.97), (0.075, 0.95),
                                                    (0.095, 0.88), (0.103, 0.62),
                                                    (0.106, 0.3))]
        return rings, (0, YC, zb + 0.107)

    def brim(n):
        b = lerp_rings(base, 1.0, n)
        rings = [ring_at(b + d, zb + 0.001 - d * 0.62) for d in (0.0, 0.02, 0.04, 0.056)]
        return rings, None

    parts = [hat(crown, m["denim"], "crown", 0.0025, denim), hat(brim, m["denim"], "brim", 0.003, denim)]
    for sx in (-1, 1):
        at, d = on_band(sx * math.pi / 2, zb + 0.05, 0.0085)
        parts.append(make.torus(at, d, 0.0045, 0.0012, m["brass"], n=16, m=6, name="eyelet", low=(8, 3)))
    return parts


def panama(m):
    zb = 1.708
    base = band(zb, 0.006)
    parts = [hat(creased(zb, 0.108, base, pinch=0.01, crease=0.024), m["panama"], "crown", 0.0022, fine_straw),
             hat(snapped_brim(zb, base, 0.066, down=0.016, up=0.02), m["panama"], "brim", 0.0022, fine_straw),
             ribbon(zb + 0.002, 0.034, 0.008, m["black"], name="band")]
    at, d = on_band(-math.pi / 2 - 0.2, zb + 0.019, 0.012)
    parts += bow(at, d, m["black"], 0.012)
    return parts


# ----------------------------------------------------------------------------------------------------------------
# What he paints with: in the bind pose, where crew_clips' paint holds them

GRIP = Vector((0.79, 0.0, 1.43))     # the right fist's grip (rig.human's hand_r_weapon), the brush along +Y from it
BRUSH_TIP = 0.235                    # crew_clips.BRUSH_TIP: the bristles' tip, on from the grip
PALETTE_FACE = 1.414                 # crew_clips.PALETTE_FACE: the palette's painted face, under the palm-down left hand
PAINTS = [(0.85, 0.84, 0.8), (0.62, 0.36, 0.06), (0.62, 0.05, 0.03), (0.04, 0.07, 0.42), (0.03, 0.26, 0.14),
          (0.25, 0.48, 0.75)]


def brush(m):
    """A long-handled filbert: a lacquered handle swelling before the ferrule, the nickel ferrule crimped flat, the hog
    bristles in a rounded tuft, ultramarine worked into their tip."""
    parts = []
    handle = [(-0.075, 0.0036), (-0.04, 0.0042), (0.04, 0.0049), (0.11, 0.0056), (0.15, 0.0052), (0.165, 0.0044)]
    for (y0, r0), (y1, r1) in zip(handle, handle[1:]):
        parts.append(make.cyl(GRIP + Vector((0, y0, 0)), GRIP + Vector((0, y1, 0)), r0, m["lacquer"], n=14, bevel=0, name="handle",
                              r1=r1, low=6))
    fer = make.cyl(GRIP + Vector((0, 0.165, 0)), GRIP + Vector((0, 0.2, 0)), 0.0045, m["nickel"], n=16, bevel=0.0005, name="ferrule",
                   r1=0.004, low=6)
    for o in (fer, make.LOW[-1]):
        o.data.transform(Matrix.Translation(GRIP) @ Matrix.Diagonal((1.0, 1.0, 0.62, 1.0)) @ Matrix.Translation(-GRIP))
    parts.append(fer)
    tuft = cook.uv_sphere(14, 8, 1.0, Matrix.Translation(GRIP + Vector((0, 0.214, 0))) @ Matrix.Diagonal((0.0062, 0.021, 0.0026, 1.0)))
    parts.append(make._finish(tuft, m["bristle"], 0, 1, "bristles"))
    make._low(tuft, "bristles")
    tip = cook.uv_sphere(12, 6, 1.0, Matrix.Translation(GRIP + Vector((0, 0.228, 0))) @ Matrix.Diagonal((0.0052, 0.0075, 0.0024, 1.0)))
    parts.append(make._finish(tip, m["ultramarine"], 0, 1, "paint"))
    make._low(tip, "paint")
    return parts


def palette(m):
    """His palette: a kidney of oiled wood under the left hand, the thumb's hole at the hand's root, his paints squeezed
    out in a ring round its far edge and worked together in its middle (its face down in the bind pose: the hold turns
    it up)."""
    parts = []
    c = Vector((-0.85, 0.0, PALETTE_FACE + 0.003))
    outline = []
    for k in range(40):
        a = 2 * math.pi * k / 40
        r = 1.0 - 0.28 * math.exp(-((a - 0.35) / 0.45) ** 2)      # the kidney's bay, by the thumb
        outline.append((c.x + math.cos(a) * 0.17 * r, c.y + math.sin(a) * 0.12 * r))
    bm = bmesh.new()
    lo = [bm.verts.new((x, y, c.z - 0.003)) for x, y in outline]
    hi = [bm.verts.new((x, y, c.z + 0.003)) for x, y in outline]
    bm.faces.new(lo)
    bm.faces.new(list(reversed(hi)))
    for i in range(len(lo)):
        j = (i + 1) % len(lo)
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new("palette")
    bm.to_mesh(me)
    bm.free()
    board = bpy.data.objects.new("palette", me)
    bpy.context.scene.collection.objects.link(board)
    make._low(board, "palette")
    _modify(board, "BEVEL", width=0.0025, segments=2, limit_method="ANGLE")
    parts.append(make._finish(board, m["palette"], 0, 1, "palette"))
    # The thumb's hole, a ring of darker oiled wood (the hole itself is the hand's).
    parts.append(make.torus(Vector((-0.765, 0.05, PALETTE_FACE)), (0, 0, 1), 0.016, 0.0022, m["palette_dark"], n=20, m=6,
                            name="hole", low=(10, 3)))
    # The paints round the far edge, each a squeezed curl; the mixing in the middle in smears.
    for i, colour in enumerate(PAINTS):
        a = math.radians(150 + i * 26)
        at = Vector((c.x + math.cos(a) * 0.13, c.y + math.sin(a) * 0.085, PALETTE_FACE - 0.004))
        blob = cook.uv_sphere(10, 6, 1.0, Matrix.Translation(at) @ Matrix.Diagonal((0.016, 0.013, 0.0065, 1.0)))
        parts.append(make._finish(blob, m[f"paint{i}"], 0, 1, "paint"))
        make._low(blob, "paint")
    for i, (dx, dy, k) in enumerate(((0.0, 0.0, 3), (0.035, -0.03, 5), (-0.03, 0.028, 1), (0.05, 0.035, 0))):
        smear = cook.uv_sphere(10, 4, 1.0, Matrix.Translation(Vector((c.x + dx, c.y + dy, PALETTE_FACE - 0.0012)))
                               @ Matrix.Rotation(0.6 * i, 4, "Z") @ Matrix.Diagonal((0.03, 0.017, 0.0016, 1.0)))
        parts.append(make._finish(smear, m[f"paint{k}"], 0, 1, "smear"))
        make._low(smear, "smear")
    return parts


PIECES = {"dave_hat_0": (straw_hat, "the wide straw sun hat, its red ribbon tied in a bow"),
        "dave_hat_1": (beret, "a plum felt beret, pulled down over the right ear"),
        "dave_hat_2": (fedora, "a grey felt fedora, black band, a pheasant's feather"),
        "dave_hat_3": (bucket, "a faded denim bucket hat, stitched brim, brass eyelets"),
        "dave_hat_4": (panama, "a cream panama, black band"),
        "dave_brush": (brush, "Dave's long filbert brush, ultramarine on its tip"),
        "dave_palette": (palette, "Dave's palette, his paints round its edge")}


def materials():
    return {
        # (The library's wool is dark, about 0.06 linear: the tints are what each is over it.)
        "straw": make.lib("wool", 60.0, (9.5, 7.2, 3.6), 0.8),
        "panama": make.lib("wool", 90.0, (11.5, 10.6, 8.2), 0.75),
        "red": make.lib("wool", 40.0, (7.0, 0.7, 0.45), 0.55),
        "plum": make.flat("plum_felt", (0.11, 0.022, 0.06), rough=0.9),
        "plum_dark": make.lib("wool", 30.0, (0.3, 0.07, 0.2), 0.8),
        "grey": make.flat("grey_felt", (0.16, 0.16, 0.17), rough=0.85),
        "black": make.lib("wool", 40.0, (0.35, 0.35, 0.38), 0.5),
        "denim": make.lib("wool", 30.0, (2.0, 3.2, 5.2), 0.9),
        "brass": make.lib("brass", 10.0, (1.0, 0.88, 0.62), 0.3),
        "quill": make.flat("quill", (0.6, 0.52, 0.38), rough=0.5),
        "feather": make.flat("feather", (0.32, 0.16, 0.06), rough=0.6),
        "bar": make.flat("feather_bar", (0.035, 0.025, 0.02), rough=0.6),
        "lacquer": make.flat("lacquer", (0.22, 0.03, 0.02), rough=0.3),
        "nickel": make.lib("iron_plate", 20.0, (1.5, 1.5, 1.45), 0.3, metal=0.8),
        "bristle": make.flat("bristle", (0.5, 0.4, 0.26), rough=0.8),
        "ultramarine": make.flat("ultramarine", PAINTS[3], rough=0.25),
        "palette": make.lib("wood_floor", 10.0, (1.1, 0.78, 0.5), 0.45),
        "palette_dark": make.lib("wood_floor", 10.0, (0.6, 0.4, 0.26), 0.45),
        **{f"paint{i}": make.flat(f"paint{i}", colour, rough=0.25) for i, colour in enumerate(PAINTS)},
    }


def build(name, fn, what):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    if os.environ.get("DAVE_KIT_PREVIEW"):
        return parts
    # (The bake's copy is smoothed, so its crown's top edge rounds in from the game's: a reach that finds it.)
    low = cook.bake_down(parts, name + "_low", 1600, colour=None, size=512, cage=0.012, reach=0.035, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=1800, grime=0.0, made=make.provenance("dave_kit", what), size=512)


def preview():
    """Every hat on his head, the high copies, rendered from his three-quarter front to out/review/dave_kit-*.png."""
    out = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out, exist_ok=True)
    for name, (fn, what) in PIECES.items():
        if not name.startswith("dave_hat"):
            continue
        parts = build(name, fn, what)
        head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
        crewfigure.place_scan(head, HEAD_C)
        for o in make.LOW:
            o.hide_render = True
        scene = bpy.context.scene
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = 16
        scene.cycles.use_denoising = False
        scene.render.resolution_x, scene.render.resolution_y = 520, 520
        scene.world = bpy.data.worlds.new("w")
        scene.world.use_nodes = True
        scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
        sun.rotation_euler = (0.7, 0.3, 2.6)
        sun.data.energy = 4
        scene.collection.objects.link(sun)
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        scene.collection.objects.link(cam)
        scene.camera = cam
        for view, d in (("front", (0.45, 1, 0.25)), ("side", (-1, 0.15, 0.1))):
            c = Vector((0, 0.03, 1.69))
            cam.location = c + Vector(d).normalized() * 0.85
            cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
            scene.render.filepath = os.path.join(out, f"dave_kit-{name}-{view}.png")
            bpy.ops.render.render(write_still=True)
    print("[dt] dave_kit preview -> out/review/dave_kit-*.png")


if os.environ.get("DAVE_KIT_PREVIEW"):
    preview()
else:
    only = os.environ.get("DAVE_KIT")
    for name, (fn, what) in PIECES.items():
        if only is None or only == name:
            build(name, fn, what)
