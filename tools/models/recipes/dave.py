"""DAVE, the wandering painter (GDD §3.2; ARCHITECTURE §8 notes 487 and 491). The director, 8 Oct: "a unique model so
he's recognizable from afar ... Dave doesn't have a beard, he's a bit tubby on the belly and has glasses. He's extremely
fashionable and wears Birks sandals often. He loves vests and cool hats. Players often find him wearing different cool
hats and vests. He's a true artist." The crew's figure (tools/models/crewfigure, the crew's rig and clips) in his own
things, the only person out there dressed for anything but the dark:
  * dressed after the director's photographs of the man he's named for, as P1's DaveKit dresses him (ARCHITECTURE §8
    note 570): grey hair, short and pushed about (a cap over the scan's scalp), clean-shaven, black rectangular glasses,
    a small black hoop in his left ear and a silver chain at his neck; Lee Perry-Smith's scan for his face; bare-headed
    under whichever hat he's in tonight (tools/models/recipes/dave_kit.py's, worn on the head bone);
  * a black tee over the belly (tools/blender/davebody.py), a ribbed crew neck, short sleeves, his arms bare; paint
    flecked down its front, on his hands and his jeans' knees; light washed jeans turned up over bare ankles;
  * cork-soled sandals, two buckled straps;
  * six waistcoats, the figure's variants (DaveKit.Vests' order): mustard corduroy, plum velvet, a green tartan, teal
    brocade, a patchwork of his paints, his black leather; each with a back, its buttons, welt pockets and a watch chain.

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh dave
    DAVE_PREVIEW=vest_0 tools/models/build.sh dave      # the high figure in a waistcoat to out/review, no bake
    DAVE_SIZE=512 tools/models/build.sh dave            # a quick bake at a quarter the atlas, to look at before the real one
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402
from mathutils.bvhtree import BVHTree  # noqa: E402

import bpy  # noqa: E402
import cook  # noqa: E402
import crewfigure  # noqa: E402
from overbake import bell, fine, smooth01  # noqa: E402

VESTS = 6


# ----------------------------------------------------------------------------------------------------------------
# His spectacles, on the scan's own face

def _tree(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    return [BVHTree.FromObject(o, dg) for o in objs]


def _hit(trees, origin, direction):
    """The nearest hit along a ray against the scan, or None."""
    hits = [h for h in (t.ray_cast(Vector(origin), Vector(direction).normalized()) for t in trees) if h[0] is not None]
    return min(hits, key=lambda h: h[3])[0] if hits else None


def glasses(tip, head, make):
    """His black rectangular glasses (the photographs; DaveKit's): two frames of black acetate before the eyes, tilted a
    little to the cheeks, a straight bridge over the nose, and the arms back along the head over the ears, each laid a
    few millimetres off the scan where it passes. Returns (high parts, game-mesh parts), the game's on the head bone."""
    black = make.lib("paint_black", 8.0, (0.55, 0.55, 0.55), 0.25)
    trees = _tree(head)
    before = len(make.LOW)
    high = []
    HW, HH, CR, RIM = 0.0222, 0.0152, 0.005, 0.0021
    tilt = Vector((0, 1, -0.16)).normalized()
    u = Vector((1, 0, 0))
    w = tilt.cross(u).normalized()
    c = [Vector((sx * 0.0315, tip.y - 0.027, tip.z + 0.036)) for sx in (-1, 1)]

    def frame(ci):
        """A rounded rectangle round ci, in the lens's plane, closed."""
        pts = []
        for cx, cy, a0 in ((HW - CR, HH - CR, 0.0), (-(HW - CR), HH - CR, 0.5 * np.pi), (-(HW - CR), -(HH - CR), np.pi),
                           (HW - CR, -(HH - CR), 1.5 * np.pi)):
            for k in range(4):
                a = a0 + 0.5 * np.pi * k / 3
                pts.append(ci + u * (cx + CR * np.cos(a)) + w * (cy + CR * np.sin(a)))
        return pts + [pts[0]]
    # Clear of the face: the frames' every point at least 6 mm before the skin behind it.
    clear = 1.0
    for ci in c:
        for p in frame(ci):
            hit = _hit(trees, p + Vector((0, 0.1, 0)), (0, -1, 0))
            if hit is not None:
                clear = min(clear, p.y - hit.y)
    push = max(0.0, 0.006 - clear)
    c = [ci + Vector((0, push, 0)) for ci in c]
    print(f"[dt] dave glasses: {clear * 1000:.1f} mm clear of the face, pushed {push * 1000:.1f} mm")
    for ci in c:
        high += make.pipe(frame(ci), RIM, black, name="rim", n=8, low=4)
    # The bridge: straight across, a little over the frames' middle, clear of the nose.
    top = c[0].z + 0.006
    nose = _hit(trees, (0, 0.3, top), (0, -1, 0))
    by = max((nose.y + 0.004) if nose is not None else c[0].y, c[0].y)
    high += make.pipe([Vector((-(0.0315 - HW), c[0].y, top)), Vector((0, by, top)), Vector((0.0315 - HW, c[0].y, top))],
                      RIM, black, name="bridge", n=8, low=4)
    # The arms: from each frame's outer top corner back along the side of the head, then down behind the ear.
    for sx, ci in zip((-1, 1), c):
        hinge = ci + Vector((sx * HW, -0.003, HH - 0.004))
        high.append(make.box(hinge, (0.003, 0.005, 0.0035), black, bevel=0.001, name="hinge", low=True))
        path = [hinge]
        for y in (hinge.y - 0.025, hinge.y - 0.055, hinge.y - 0.085, tip.y - 0.13):
            side = _hit(trees, (sx * 0.3, y, hinge.z + 0.002), (-sx, 0, 0))
            x = (side.x + sx * 0.003) if side is not None else path[-1].x
            x = sx * max(abs(x), abs(path[-1].x))
            path.append(Vector((x, y, hinge.z + 0.002 - (y < tip.y - 0.12) * 0.004)))
        end = path[-1]
        path.append(end + Vector((-sx * 0.006, -0.014, -0.026)))
        high += make.pipe(path, RIM * 0.85, black, name="arm", n=8, low=4)
    low = make.LOW[before:]
    del make.LOW[before:]
    return high, low


def hoop(tip, head, make):
    """The small black hoop in his left ear (the photographs; DaveKit's), through the lobe and hanging under it."""
    black = make.lib("paint_black", 8.0, (0.4, 0.4, 0.4), 0.2)
    trees = _tree(head)
    before = len(make.LOW)
    # The lobe: the left side of the head (−x: he faces +y) at the ear's foot, behind the cheek.
    lobe = None
    for dz in (0.05, 0.045, 0.055, 0.04):
        lobe = _hit(trees, (-0.3, tip.y - 0.103, tip.z - dz), (1, 0, 0))
        if lobe is not None:
            break
    if lobe is None:
        return [], []
    print(f"[dt] dave hoop: lobe at {tuple(round(v, 3) for v in lobe)}")
    high = [make.torus(lobe + Vector((-0.002, 0.0, -0.007)), (1, 0, 0), 0.0072, 0.0011, black, n=24, m=6, name="hoop", low=(10, 3))]
    low = make.LOW[before:]
    del make.LOW[before:]
    return high, low


def _surface(name, verts, faces, material, thick):
    """A mesh from verts and quads, smooth, thickened inward by `thick` (so it has an edge and a back)."""
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in verts], [], faces)
    me.update()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.data.materials.append(material)
    for f in me.polygons:
        f.use_smooth = True
    sol = o.modifiers.new("solid", "SOLIDIFY")
    sol.thickness, sol.offset = thick, -1.0
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    bpy.ops.object.modifier_apply(modifier=sol.name)
    o.select_set(False)
    return o


def _hairline(a):
    """Where his hair starts, up from the skull's middle (degrees of elevation), all round (a: degrees from his front):
    receding a little off the forehead, over the temples, above the ears, and down behind them to the nape."""
    a = abs((a + 180) % 360 - 180)
    return float(np.interp(a, (0, 40, 75, 115, 150, 180), (40, 40, 17, 15, -30, -32)))


def hair(tip, head, make):
    """His hair (the photographs; DaveKit's: "grey, short and pushed about, a cap of it round the back and sides from the
    nape to the crown"): a cap laid over the scan's bald scalp, a few millimetres proud of it and thinning to nothing at
    its hairline, its strands' lie and the grey left to the bake (the grade's `hair`)."""
    grey = make.lib("wool", 30.0, (7.0, 7.0, 7.3), 0.85)
    trees = [t for t in _tree(head)]
    centre = Vector((0, tip.y - 0.095, tip.z + 0.05))

    def grid(A, E):
        """Each column round the head from its own hairline up to the crown, so the cap's edge is the hairline."""
        verts, index = [], {}
        for j in range(A):
            az = 2 * np.pi * j / A
            line = _hairline(np.degrees(az))
            for i in range(E + 1):
                f = i / E
                el = np.radians(line + (89.0 - line) * f)
                d = Vector((np.sin(az) * np.cos(el), np.cos(az) * np.cos(el), np.sin(el)))
                best = None
                for t in trees:
                    h = t.ray_cast(centre + d * 0.4, -d)
                    if h[0] is not None and (best is None or h[3] < best[3]):
                        best = h
                if best is None:
                    continue
                # Proud of the scalp by its thickness, pushed about, thinning off to nothing at the hairline.
                push = 0.5 + 0.5 * float(cook.noise_np(np.array([tuple(best[0])], np.float32), 51, 25)[0])
                lift = 0.0012 + (0.0028 + 0.003 * push) * smooth01(0.0, 0.18, f)
                index[(i, j)] = len(verts)
                verts.append(best[0] + best[1] * lift)
        faces = []
        for j in range(A):
            for i in range(E):
                q = [(i, j), (i, (j + 1) % A), (i + 1, (j + 1) % A), (i + 1, j)]
                if all(k in index for k in q):
                    faces.append([index[k] for k in q])
        return verts, faces
    hv, hf = grid(96, 18)
    lv, lf = grid(24, 6)
    high = _surface("hair", hv, hf, grey, 0.002)
    high["dt_kind"] = "dave_hair"
    low = _surface("hair_low", lv, lf, grey, 0.002)
    print(f"[dt] dave hair: {len(hf)} faces, {len(lf)} in the game's")
    return [high], [low]


def head_kit(tip, head, make):
    """What's on his head: his hair, his glasses, his hoop (Style.mask: on the head bone)."""
    out_high, out_low = [], []
    for fn in (hair, glasses, hoop):
        h, lo = fn(tip, head, make)
        out_high += h
        out_low += lo
    return out_high, out_low


# ----------------------------------------------------------------------------------------------------------------
# The paint on him, and his waistcoats' cloths (masks the bake carries into the grade)

# His paints (linear): cadmium red, ultramarine, yellow ochre, viridian, titanium white, a sky blue.
PAINTS = np.array([[0.62, 0.05, 0.03], [0.04, 0.07, 0.42], [0.62, 0.36, 0.06], [0.03, 0.26, 0.14], [0.85, 0.84, 0.8],
                   [0.25, 0.48, 0.75]], np.float32)


def _hash(ix, iy, seed):
    h = (ix * 73856093) ^ (iy * 19349663) ^ (seed * 83492791)
    return ((h & 0xFFFF) / 65535.0).astype(np.float32)


def spatter(p, kind=""):
    """Paint on him: flecks down the tee's front and its right sleeve (the brush hand), smears on the fingers, a few on
    his jeans' knees (none on his waistcoats: he minds those)."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    fleck = smooth01(0.62, 0.7, cook.noise_np(p, 31, 90) * 0.75 + cook.noise_np(p, 32, 300) * 0.35)
    where = np.zeros(len(p), np.float32)
    if kind.startswith(("crew_atlas.coat", "crew_atlas.sleeve")):
        where = np.maximum(where, 0.8 * (y > 0.05))
        where = np.maximum(where, smooth01(0.38, 0.47, x))
    elif kind.startswith("crew_atlas.gloves"):
        # Fingers and the side of the hand dipped in it.
        where = np.where(np.abs(x) > 0.8, 1.6, 0.3) * np.where(x > 0, 1.0, 0.5)
    elif kind.startswith("crew_atlas.trouser"):
        where = 0.7 * bell((z - 0.5) / 0.08) * (y > 0)
    return np.clip(fleck * where, 0, 1)


def fleck_colour(p, kind=""):
    """Which paint each fleck is: blotches a few centimetres across, one colour each."""
    n = cook.noise_np(p, 33, 12) * 0.5 + 0.5
    i = np.clip((n * len(PAINTS)).astype(int), 0, len(PAINTS) - 1)
    return PAINTS[i]


def tartan(u):
    """A tartan's sett along one thread (period 8 cm): the green ground, a broad navy band, red and yellow lines."""
    f = np.mod(u, 0.08) / 0.08
    ground, navy, red, yellow = (np.array(c, np.float32) for c in ((0.02, 0.11, 0.04), (0.01, 0.015, 0.06),
                                                                  (0.48, 0.02, 0.02), (0.65, 0.45, 0.04)))
    out = np.tile(ground, (len(u), 1))
    out = np.where(((f > 0.08) & (f < 0.34))[:, None], navy, out)
    out = np.where(((f > 0.2) & (f < 0.22))[:, None], ground * 1.6, out)
    out = np.where(((f > 0.6) & (f < 0.625))[:, None], red, out)
    out = np.where(((f > 0.86) & (f < 0.875))[:, None], yellow, out)
    return out


def brocade(p):
    """Teal brocade's figure: a medallion in each 4.5 x 6 cm cell (a ring of petals round a boss), linked by tendrils."""
    x, z = p[:, 0], p[:, 2]
    u = np.mod(x, 0.045) / 0.045 - 0.5
    v = np.mod(z + 0.5 * 0.06 * np.mod(np.floor(x / 0.045), 2), 0.06) / 0.06 - 0.5
    r = np.hypot(u, v * 1.2)
    a = np.arctan2(v, u)
    petals = smooth01(0.06, 0.02, np.abs(r - (0.27 + 0.07 * np.cos(a * 6))))
    boss = smooth01(0.11, 0.07, r)
    tendril = smooth01(0.035, 0.0, np.abs(np.sin((u + v) * np.pi * 2) * 0.12)) * smooth01(0.35, 0.45, r)
    return np.clip(np.maximum(np.maximum(petals, boss), tendril * 0.8), 0, 1)


def patchwork(p):
    """Squares of his paints sewn together (rows offset like brickwork), each its own colour; the seams darker."""
    x, z = p[:, 0], p[:, 2]
    row = np.floor(z / 0.07).astype(np.int64)
    col = np.floor((x + 0.5 * 0.065 * (row % 2)) / 0.065).astype(np.int64)
    i = np.clip((_hash(col, row, 7) * len(PAINTS)).astype(int), 0, len(PAINTS) - 1)
    colours = PAINTS[i] * 0.85 + 0.03
    fu = np.mod(x + 0.5 * 0.065 * (row % 2), 0.065) / 0.065
    fv = np.mod(z, 0.07) / 0.07
    seam = smooth01(0.06, 0.0, np.minimum(np.minimum(fu, 1 - fu), np.minimum(fv, 1 - fv)))
    return colours * (1 - 0.6 * seam)[:, None]


CLOTHS = {0: np.array([0.6, 0.27, 0.025], np.float32), 1: np.array([0.14, 0.012, 0.065], np.float32),
          3: np.array([0.012, 0.14, 0.16], np.float32), 5: np.array([0.018, 0.015, 0.013], np.float32)}
SATINS = [np.array(c, np.float32) for c in ((0.1, 0.045, 0.012), (0.05, 0.005, 0.03), (0.012, 0.02, 0.045),
                                            (0.008, 0.06, 0.07), (0.07, 0.05, 0.035), (0.01, 0.009, 0.008))]
GOLD = np.array([0.55, 0.36, 0.08], np.float32)


def cloth(p, kind=""):
    """Each waistcoat's cloth, its colour where it's woven or sewn in (linear RGB; 0 off a waistcoat): the grade lays it
    on the bake's own shading."""
    out = np.zeros((len(p), 3), np.float32)
    for k in range(VESTS):
        if kind.startswith((f"dave_back.{k}", f"dave_lining.{k}")):
            return out + SATINS[k]
        if not kind.startswith(f"dave_vest.{k}"):
            continue
        if k == 2:
            return (tartan(p[:, 0] + 0.013) + tartan(p[:, 2])) * 0.5
        if k == 3:
            b = brocade(p)[:, None]
            return CLOTHS[3] * (1 - b) + GOLD * b
        if k == 4:
            return patchwork(p)
        return out + CLOTHS[k]
    return out


def wales(p, n):
    """Corduroy's ribs, up and down."""
    a = np.arctan2(p[:, 0], p[:, 1]) * 0.19
    return 0.0007 * np.abs(np.sin(a * np.pi / 0.0032)) + fine(p, 0.0004, 120)


def raised(p, n):
    """The brocade's figure stands a little proud of its ground."""
    return 0.0006 * brocade(p) + fine(p, 0.0003, 140)


def nap(p, n):
    """Velvet's pile, brushed one way and crushed in places."""
    return 0.0004 * cook.noise_np(p, 41, 60) + fine(p, 0.0002, 200)


def grain(p, n):
    """The black leather waistcoat's grain: pebbled, a little creased."""
    return 0.0005 * smooth01(0.4, 0.75, cook.noise_np(p, 47, 260) * 0.5 + 0.5) + fine(p, 0.0002, 160)


def denim(p, n):
    """His jeans: a twill's diagonal, creased behind the knee and across its front where it bends, the inseam and
    outseam, breaking over the turn-ups."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    c = np.arctan2(x - np.sign(x) * 0.105, y)
    wobble = cook.noise_np(p, 43, 7)
    d = 0.00035 * np.abs(np.sin((c * 0.07 + z) * np.pi / 0.0022))
    d += 0.0008 * bell((np.abs(c) - 1.57) / 0.03)
    d += 0.004 * np.sin(z * 120 + c * 3 + wobble * 2) * bell((z - 0.5) / 0.05) * (np.abs(c) > 1.8)
    d += 0.003 * np.sin(z * 90 - c * 4 + wobble) * bell((z - 0.55) / 0.04) * (np.abs(c) < 1.2)
    d += 0.004 * np.sin(c * 5 + wobble * 3) * bell((z - 0.19) / 0.025)
    return d + fine(p, 0.0007, 90)


# ----------------------------------------------------------------------------------------------------------------
# What only the bake sees: each waistcoat's buttons, pockets, cinch and watch chain

def detail(highs, make):
    horn = make.lib("leather", 12.0, (0.3, 0.2, 0.12), 0.35)
    brass = make.lib("brass", 8.0, (1.0, 0.85, 0.6), 0.3)
    shell = make.lib("paint_black", 8.0, (1.6, 1.5, 1.35), 0.3)
    welt = make.lib("wool", 14.0, (0.5, 0.5, 0.5), 0.8)
    buttons = [horn, shell, horn, brass, shell, brass]
    # His silver chain at the neck (the photographs), lying on the tee's crew neck and dipping onto his chest.
    silver = make.lib("rail_steel", 20.0, (1.7, 1.72, 1.8), 0.25)
    body = [h for h in highs.get("body", []) + highs.get("coat", []) if h.get("dt_kind", "").startswith(("crew_atlas.coat", "skin.neck"))]
    if body:
        trees = _tree(body)
        links = []
        for k in range(29):
            a = 2 * np.pi * k / 28
            z = 1.522 - 0.045 * max(0.0, float(np.cos(a))) ** 3
            d = Vector((float(np.sin(a)), float(np.cos(a)), 0))
            hit = None
            for t in trees:
                h = t.ray_cast(Vector((0, 0.01, z)) + d * 0.3, -d)
                if h[0] is not None and (hit is None or h[3] < hit[3]):
                    hit = h
            if hit is not None:
                links.append(hit[0] + hit[1] * 0.002)
        if len(links) > 20:
            highs.setdefault("body", []).extend(make.pipe(links, 0.0013, silver, name="chain", n=6, low=0))
            print(f"[dt] dave chain: {len(links)} links round his neck")
    for k in range(VESTS):
        part = f"vest_{k}"
        if part not in highs:
            continue
        trees = _tree([h for h in highs[part] if h.get("dt_kind", "").startswith("dave_vest.")])

        def on(x, z, back=False):
            hit = None
            for t in trees:
                h = t.ray_cast(Vector((x, -0.6 if back else 0.6, z)), Vector((0, 1 if back else -1, 0)))
                if h[0] is not None and (hit is None or h[3] < hit[3]):
                    hit = h
            return hit

        # The buttons down the left front's edge, top to the last above the points.
        for i, z in enumerate(np.linspace(1.175, 0.985, 5)):
            hit = on(0.013, float(z))
            if hit is None:
                continue
            at, nrm = hit[0], hit[1]
            mat = buttons[(k + i) % len(buttons)] if k == 4 else buttons[k]
            highs[part].append(make.cyl(at - nrm * 0.001, at + nrm * 0.0035, 0.0085, mat, n=14, bevel=0.0015, name="button",
                                        r1=0.0075, low=0))
        # Welt pockets either side low down, and one on the left breast.
        for x, z, w in ((0.105, 1.03, 0.055), (-0.105, 1.03, 0.055), (-0.1, 1.27, 0.042)):
            hit = on(x, z)
            if hit is None:
                continue
            at, nrm = hit[0], hit[1]
            side = Vector((0, 0, 1)).cross(nrm).normalized()
            rot = Matrix((side, nrm.cross(side), nrm)).transposed().to_4x4()
            highs[part].append(make.box(at + nrm * 0.0012, (w / 2, 0.0045, 0.0012), welt, bevel=0.0008, name="welt", rot=rot,
                                        low=False))
        # The watch chain from the third buttonhole to the right pocket, swagged across the belly.
        a, b = on(0.013, 1.08), on(0.105, 1.045)
        if a is not None and b is not None:
            pts = []
            for t in np.linspace(0, 1, 7):
                p = a[0].lerp(b[0], float(t)) + Vector((0, 0, -0.035 * float(np.sin(np.pi * t))))
                hit = on(p.x, p.z)
                pts.append((hit[0] + hit[1] * 0.0025) if hit is not None else p)
            highs[part] += make.pipe(pts, 0.0016, brass, name="chain", n=6, low=0)
        # The back's cinch: two tabs to a buckle.
        hit = on(0.0, 1.02, back=True)
        if hit is not None:
            at, nrm = hit[0], hit[1]
            side = Vector((0, 0, 1)).cross(nrm).normalized()
            rot = Matrix((side, nrm.cross(side), nrm)).transposed().to_4x4()
            highs[part].append(make.box(at + nrm * 0.002, (0.07, 0.012, 0.0015), welt, bevel=0.001, name="cinch", rot=rot,
                                        low=False))
            highs[part].append(make.box(at + nrm * 0.0045, (0.012, 0.014, 0.0015), brass, bevel=0.0008, name="buckle", rot=rot,
                                        low=False))


def lining(p, kind=""):
    """The waistcoats' insides (davebody's lining, solidify's new side)."""
    return np.full(len(p), 1.0 if kind.startswith("dave_lining.") else 0.0, np.float32)


def hair_mask(p, kind=""):
    """His hair cap (dave.hair's)."""
    return np.full(len(p), 1.0 if kind.startswith("dave_hair") else 0.0, np.float32)


def bare(p, kind=""):
    """His skin off the scan (the neck, the forearms and hands, the feet): the grade gives it the face's own tone."""
    return np.full(len(p), 1.0 if kind.startswith(("skin.neck", "crew_atlas.gloves")) else 0.0, np.float32)


def grade(base, atlas, face):
    # His face warmed from the scan's studio grey (he's out in the air, not the smoke), and the rest of his skin the
    # same tone as it, its own shading kept.
    out = np.where(face[..., None], base * np.array([1.14, 1.0, 0.86], np.float32) * 1.22, base)
    skin = atlas.maps["bare"] > 0.5
    if face.any() and skin.any():
        tone = out[face].mean(0)
        lum = base.mean(-1)
        ratio = lum / max(float(np.median(lum[skin])), 1e-3)
        out = np.where(skin[..., None], tone * np.clip(ratio, 0.3, 1.8)[..., None], out)
    # His hair grey (not warmed with his face), its own shading kept.
    hair = atlas.maps["hair"] > 0.5
    if hair.any():
        out = np.where(hair[..., None], base.mean(-1, keepdims=True) * np.array([0.98, 0.99, 1.02], np.float32), out)
    # The waistcoats: each its cloth on the bake's shading (the library's wool, laid down neutral).
    col = atlas.maps["cloth"]
    on = col.max(-1) > 1e-3
    if on.any():
        lum = base.mean(-1)
        ratio = np.clip(lum / max(float(np.median(lum[on])), 1e-3), 0.2, 2.2)
        # The lining, seen at the armholes, the hem and the fronts' edges, lies against the shirt: its occlusion is
        # all shadow, which drew it black. A satin lining catches what light gets in.
        ratio = np.where(atlas.maps["lining"] > 0.5, np.clip(ratio, 0.6, 1.2), ratio)
        out = np.where(on[..., None], col * ratio[..., None], out)
    # His paints, flecked and smeared on.
    s = np.clip(atlas.maps["spatter"], 0, 1)[..., None]
    out = out * (1 - s) + atlas.maps["fleck"] * s
    return out


DRESS = {
    # A black tee (the library's wool fine at this repeat, about 0.06 linear: the tints are what the cloth is over that),
    # its sleeves the same; light washed jeans.
    "crew_atlas.coat": ("wool", 22.0, (0.76, 0.74, 0.73), 0.85, 3),
    "crew_atlas.sleeve": ("wool", 22.0, (0.76, 0.74, 0.73), 0.85, 3),
    "crew_atlas.trouser": ("wool", 16.0, (5.6, 7.2, 9.6), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.95, 0.76, 0.64), 0.6, 0),
    "skin.neck": ("skin", 4.0, (0.95, 0.76, 0.64), 0.6, 2),
    # The sandals: a cork footbed, a dark tread, oiled tan straps, brass buckles.
    "crew_atlas.boot": ("concrete_stain", 30.0, (2.4, 1.75, 1.05), 0.9, 0),
    "crew_atlas.satchel": ("paint_black", 6.0, (0.42, 0.4, 0.38), 0.7, 0),
    "leather.strap": ("leather", 6.0, (0.62, 0.4, 0.24), 0.45, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.38, 0.24, 0.15), 0.45, 0),
    "crew_atlas.badge": ("brass", 6.0, (1.0, 0.92, 0.8), 0.32, 0),
    "dave_vest.": ("wool", 11.0, (0.9, 0.9, 0.9), 0.8, 2),
    "dave_back.": ("wool", 24.0, (0.9, 0.9, 0.9), 0.35, 2),
    "dave_lining.": ("wool", 24.0, (0.9, 0.9, 0.9), 0.35, 2),
}

crewfigure.build("dave", crewfigure.Style(
    dress=DRESS, figure="dave", hats=False, lamp=False, mask=head_kit,
    shapes={"dave_vest.0": wales, "dave_vest.1": nap, "dave_vest.3": raised, "dave_vest.5": grain, "crew_atlas.trouser": denim},
    masks={"spatter": spatter, "fleck": fleck_colour, "cloth": cloth, "lining": lining, "bare": bare, "hair": hair_mask}, grade=grade,
    preview="DAVE_PREVIEW",
    reshape=["crew_atlas.trouser"], baked=[f"vest_{k}" for k in range(VESTS)], shells=["coat"] + [f"vest_{k}" for k in range(VESTS)],
    # (No buttons: a tee. One that misses everything, so crewfigure doesn't fall back to the crew's coat's.)
    buttons=[(0.0, -5.0)], laces=False, grime=0.2, size=int(os.environ.get("DAVE_SIZE", 2048)), detail=detail,
    views=[("front", (0.25, 1, 0.12), (0, 0, 0.95), 4.0), ("back", (-0.3, -1, 0.1), (0, 0, 0.95), 4.0),
           ("head", (0.35, 1, 0.1), (0, 0.04, 1.64), 0.75), ("chest", (0.2, 1, 0.05), (0, 0.1, 1.15), 1.4),
           ("profile", (1, 0.05, 0.05), (0, 0.05, 1.1), 2.4), ("sandal", (0.5, 1, 0.5), (0.1, 0.06, 0.06), 0.65),
           ("hand", (0.2, 0.6, 1), (0.72, 0, 1.44), 0.6)],
    what="Dave, the wandering painter, in his black tee and jeans and one of six waistcoats, modelled over tools/blender/crew.py"))
