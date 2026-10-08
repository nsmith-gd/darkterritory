"""THE MOOSE (GDD §21 outside; docs/design/creatures/moose.md §3; ARCHITECTURE §8 note 339): a corrupted bull moose.

"The strongest monsters are the ones where you can still tell what they used to be" (§26.5): anyone who has seen a moose
knows this one at once, then sees what's wrong with it. The silhouette is the RACK: never shed, grown for years,
mineralized grey-black like slag (contamination, not magic: no glow), 3.2 m across, the widest thing in the roster, so
heavy the head hangs low and the withers have swollen into a hump to carry it. Bloody strips of velvet hang off the
tines, and the junk it has run through is caught in it: fence wire, a telegraph insulator, a smashed lamp, a crew cap,
splintered planks. The hide is a "ghost moose's": grey, patchy and hairless, crusted with clusters of engorged pale tick
sacs, grape to plum, a ridge of them down the spine that stands up when it warns. Oversized torn ears (its meter: forward
when it's listening, pinned flat when it's coming), the bell swollen into a long sac, legs too long, knobbed knees, split
black hooves.

How it's made (the Look Review: "super low quality, very boxy and rigid, not organic at all"; the Gannet's way, note 340):
the trunk, the hump, the neck, the head and its lower jaw, the legs, the bell, the tail and the ridge's crest are one skin
(rig.fuse: their union voxel-remeshed and QuadriFlowed, smooth-shaded), so the haunches flow into the flanks, the shoulders
into the hump and the neck, the long overhanging muzzle and its drooping lip into the face, with no seam anywhere. The
mouth is the gap left between the muzzle's lip and the jaw (it opens as the jaw does). Over the skin: the tick sacs (each
riding the skin it's on), the ears (cupped and torn), the eyes, the split hooves and dewclaws. The rack is a second fused
skin, both sides at once: each palm a broad cupped, twisted plate thick at the beam and thinning to its rim, the tines
growing out of the rim (out, up and curving, the brow palm's forward), slag crusted on in lumps, the burr knotted; its
plates' edges shade hard and its faces smooth. The velvet hangs off it in strips that swing; the junk is caught in it.
Its colour is baked into one atlas by tools/models/recipes/moose.py; built alone (this script), it wears the shared tiling
textures.

2.4 m at the shoulder, 3.4 m to the top of the rack, 3.2 m across it; head forward (-Z in the engine). SK_Moose.
A large monster (GDD §27: 8,000-16,000 triangles), spent where the silhouette is: the rack's palms, the head and the hump.
Clips (§31: unnervingly still, then abrupt): graze, listen, warn, walk, strut, search, trot, squareUp, charge, overrun,
snag, ram, pin, trainPass, hit (loops but squareUp, overrun and hit). They flex through the spine, the neck and the legs;
the charge is a gallop with the head and the rack held level on the line.

    blender -b --python tools/blender/moose.py -- content/art/models/moose.glb
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/blender/moose.py -- out.glb)
    python tools/models/recipes/moose.py      # the game's: this, with its colour baked into an atlas
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over, smoothstep  # noqa: E402

rig.reset()

# The brief's numbers (docs/design/creatures/moose.md §3), printed at the end and pinned by CreatureArtTests.
SHOULDER, RACK_TOP, RACK_SPAN = 2.4, 3.4, 3.2

# The head's frame: from the poll along the face to the muzzle (u), across (x), and up out of the brow (v). It hangs 46
# degrees down under the rack's weight.
POLL = Vector((0, 1.3, 2.2))
ALONG = (Vector((0, 1.97, 1.47)) - Vector((0, 1.32, 2.14))).normalized()
OVER = Vector((0, -ALONG.z, ALONG.y))


def head_at(u, x=0.0, v=0.0):
    return POLL + ALONG * u + Vector((x, 0, 0)) + OVER * v


# The back's top line (y, z): the croup, the loins, then up over the withers into the hump, and down the neck to the poll.
TOPLINE = [(-1.24, 1.92), (-1.18, 1.98), (-1.04, 2.03), (-0.88, 2.05), (-0.7, 2.07), (-0.48, 2.09), (-0.24, 2.13), (0.0, 2.2),
           (0.22, 2.31), (0.42, 2.43), (0.58, 2.5), (0.74, 2.49), (0.88, 2.42), (1.04, 2.33), (1.18, 2.28), (1.3, 2.25)]


def interp(table, x):
    for (x0, a), (x1, b) in zip(table, table[1:]):
        if x0 <= x <= x1:
            t = (x - x0) / (x1 - x0)
            return a + (b - a) * t
    return table[0][1] if x < table[0][0] else table[-1][1]


def top_at(y):
    return interp(TOPLINE, y)


def skeleton():
    """SK_Moose: root, pelvis, spine_01..02, chest, neck_01..02, head, jaw (hinged at the back of the cheek), ears, the bell
    (two), the rack (a bone a side, rigid on the skull, so its weight reads as its own in `dt art clearance`), a velvet bone
    a side (the strips swing), the sac ridge (three bones just under the crest of the back, lying back along it: they
    stand up and swell when it warns), a tail, the front legs (scapula, upperarm, lowerarm, hand, finger) and the rear
    (thigh, calf, foot, toe)."""
    def ridge(y0, y1):
        return (0, y0, top_at(y0) - 0.03), (0, y1, top_at(y1) - 0.03)
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.3, 0)),
        Bone("pelvis", "root", (0, -0.85, 1.95), (0, -0.5, 2.0)),
        Bone("spine_01", "pelvis", (0, -0.5, 2.0), (0, 0.0, 2.08)),
        Bone("spine_02", "spine_01", (0, 0.0, 2.08), (0, 0.42, 2.2)),
        Bone("chest", "spine_02", (0, 0.42, 2.2), (0, 0.72, 2.3)),
        Bone("neck_01", "chest", (0, 0.72, 2.3), (0, 1.02, 2.27)),
        Bone("neck_02", "neck_01", (0, 1.02, 2.27), (0, 1.32, 2.14)),
        Bone("head", "neck_02", (0, 1.32, 2.14), (0, 1.97, 1.47)),
        Bone("jaw", "head", tuple(head_at(0.12, 0, -0.12)), tuple(head_at(0.76, 0, -0.17))),
        Bone("bell_01", "neck_02", (0, 1.18, 1.72), (0, 1.21, 1.44)),
        Bone("bell_02", "bell_01", (0, 1.21, 1.44), (0, 1.23, 1.18)),
        Bone("ridge_01", "spine_01", *ridge(-0.15, -0.62)),
        Bone("ridge_02", "spine_02", *ridge(0.36, -0.15)),
        Bone("ridge_03", "chest", *ridge(0.98, 0.38)),
        Bone("tail_01", "pelvis", (0, -1.16, 1.98), (0, -1.24, 1.78)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"ear_{side}", "head", (sx * 0.12, 1.33, 2.25), (sx * 0.47, 1.25, 2.47)),
            Bone(f"rack_{side}", "head", (sx * 0.14, 1.46, 2.26), (sx * 0.95, 1.46, 2.6)),
            Bone(f"velvet_{side}", f"rack_{side}", (sx * 1.0, 1.4, 2.62), (sx * 1.0, 1.4, 2.2)),
            Bone(f"scapula_{side}", "chest", (sx * 0.2, 0.62, 2.22), (sx * 0.28, 0.84, 1.62)),
            Bone(f"upperarm_{side}", f"scapula_{side}", (sx * 0.28, 0.84, 1.62), (sx * 0.28, 0.75, 1.3)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.28, 0.75, 1.3), (sx * 0.28, 0.76, 0.66)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.28, 0.76, 0.66), (sx * 0.28, 0.8, 0.16)),
            Bone(f"finger_{side}", f"hand_{side}", (sx * 0.28, 0.8, 0.16), (sx * 0.28, 0.9, 0.0)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.24, -0.74, 1.9), (sx * 0.27, -0.5, 1.42)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.27, -0.5, 1.42), (sx * 0.27, -0.94, 0.8)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.27, -0.94, 0.8), (sx * 0.27, -0.86, 0.16)),
            Bone(f"toe_{side}", f"foot_{side}", (sx * 0.27, -0.86, 0.16), (sx * 0.27, -0.76, 0.0)),
        ]
    return Skeleton("SK_Moose", b)


sk = skeleton()
sk.build()
kit = rig.Kit(sk, "moose")

# --- materials ----------------------------------------------------------------------------------------------
# Each region its own material: built alone they're the shared tiling textures; baked (tools/models/recipes/moose.py), the
# names say what to paint. The hide: grey and hairless, a winter-tick "ghost moose" (§3), pale enough to read in the
# headlamp at distance in fog (Part Eleven Q6); darker down the legs and on the muzzle.
HIDE = Mat("flesh.moose_hide", hexc("#8e8e8e"), shine=0.3, tint=(0.98, 1.06, 1.16))
LEGS = Mat("flesh.moose_legs", hexc("#5c5c5e"), shine=0.3, tint=(0.62, 0.66, 0.74))
MUZZLE = Mat("flesh.moose_muzzle", hexc("#6a6664"), shine=0.35, tint=(0.7, 0.72, 0.78))
MOUTH = Mat("tar.moose_mouth", hexc("#1e0e0c"), shine=0.4)
# The sacs: engorged, pale, stretched shiny. (The skin texture, not the sac's: a sac is a few centimetres, and the sac
# texture's dark seams are tiled at 0.5 m.)
SAC = Mat("skin.moose_sac", hexc("#d6cebe"), shine=0.6, tint=(1.75, 1.72, 1.7))
SAC_OLD = Mat("skin.moose_sac_old", hexc("#a49a8e"), shine=0.5, tint=(1.3, 1.22, 1.18))
# The rack: slag, grey-black, dull (no glow, no purple: contamination, not magic).
RACK = Mat("slag.moose_rack", hexc("#3e3c3a"), shine=0.22, tint=(0.8, 0.79, 0.77))
VELVET = Mat("flesh.moose_velvet", hexc("#4a1612"), shine=0.45, tint=(0.62, 0.2, 0.17))
HOOF = Mat("tar.moose_hoof", hexc("#141110"), shine=0.3)
EAR = Mat("flesh.moose_ear", hexc("#7e7a78"), shine=0.3, tint=(0.86, 0.9, 0.98))
# The eye: dark, wet, set back on the side of the skull; the lamp catches it. (Kept out of the atlas: its own glass.)
EYE = Mat("glass_dirty.moose_eye", hexc("#1c1a18"), shine=0.85, tint=(0.35, 0.33, 0.3))
# What's caught in the rack (environmental story): rusted fence wire, a green glass telegraph insulator, a smashed lamp,
# a crew cap, splintered planks.
WIRE = Mat("rust_heavy.moose_wire", hexc("#5a3a26"), shine=0.3)
INSULATOR = Mat("glass_dirty.moose_insulator", hexc("#5f8a74"), shine=0.7, tint=(0.75, 1.15, 0.95))
LAMP = Mat("paint_black.moose_lamp", hexc("#22201e"), shine=0.3)
LAMP_GLASS = Mat("glass_dirty.moose_lamp_glass", hexc("#8a8670"), shine=0.8)
CAP = Mat("wool.moose_cap", hexc("#20242c"), shine=0.06, tint=(0.4, 0.44, 0.52))
WOOD = Mat("wood_grey.moose_wood", hexc("#6a645a"), shine=0.05)


def h01(*k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 77, 1.0)


def path_frame(pts):
    """A frame along a polyline in the y-z plane (u 0..1 by length): at(u, x, v) is the point x across (+X) and v up off it
    (square to its length, toward +Z)."""
    pts = [Vector(p) for p in pts]
    lens = [0.0]
    for a, b in zip(pts, pts[1:]):
        lens.append(lens[-1] + (b - a).length)
    total = lens[-1]

    def where(u):
        d = max(0.0, min(1.0, u)) * total
        for i in range(len(pts) - 1):
            if d <= lens[i + 1] or i == len(pts) - 2:
                k = (d - lens[i]) / max(lens[i + 1] - lens[i], 1e-9)
                c = pts[i] + (pts[i + 1] - pts[i]) * k
                # (The tangent blended across each joint, so the rings turn smoothly round a bend.)
                t0 = (pts[i + 1] - pts[i]).normalized()
                tp = (pts[i] - pts[i - 1]).normalized() if i > 0 else t0
                tn = (pts[i + 2] - pts[i + 1]).normalized() if i + 2 < len(pts) else t0
                t = (tp * max(0.0, 0.5 - k) + t0 + tn * max(0.0, k - 0.5)).normalized()
                return c, t
        return pts[-1], (pts[-1] - pts[-2]).normalized()

    def at(u, x=0.0, v=0.0):
        c, t = where(u)
        up = Vector((0, -t.z, t.y))
        return c + Vector((x, 0, 0)) + up * v
    return at


def egg_ring(at, u, hw_up, hw_lo, top, bot, e, sides, shape=None, lip=0.0, k=0):
    """A cross-section round `at(u, ...)`: an egg (`hw_up` wide over the middle, `hw_lo` under it, `top` over the axis and
    `bot` under it, squareness `e`); `lip` raises the middle of its underside (a channel between two draped sides)."""
    vc, hv = (top + bot) / 2, (top - bot) / 2
    ring = []
    for j in range(sides):
        a = 2 * math.pi * j / sides
        s, c = math.sin(a), math.cos(a)
        blend = 0.5 + 0.5 * c
        hw = hw_lo + (hw_up - hw_lo) * blend * blend * (3 - 2 * blend)
        x = math.copysign(abs(s) ** e, s) * hw
        v = vc + math.copysign(abs(c) ** e, c) * hv
        if c < 0 and lip:
            v += lip * c * c * max(0.0, 1 - 1.8 * s * s)
        p = at(u, x, v)
        ring.append(Vector(shape(k, j, a, p)) if shape else p)
    return ring, at(u, 0, vc)


def sweep(part, at, secs, sides, mat, bones, shape=None, cap0=False, cap1=False, fmat=None):
    """A loft through egg sections along a frame: each (u, hw_up, hw_lo, top, bot[, squareness[, lip]])."""
    rings, cents = [], []
    for k, s in enumerate(secs):
        u, hw_up, hw_lo, top, bot = s[:5]
        e = s[5] if len(s) > 5 else 0.9
        lip = s[6] if len(s) > 6 else 0.0
        r, c = egg_ring(at, u, hw_up, hw_lo, top, bot, e, sides, shape, lip, k)
        rings.append(r)
        cents.append(c)
    return part.loft(rings, mat, bones, centres=cents, cap0=cap0, cap1=cap1, fmat=fmat)


def finer(secs, n=2):
    """Each gap between sections split in `n` (smoothstep-free: the voxel union and the relax smooth the rest)."""
    out = []
    for a, b in zip(secs, secs[1:]):
        for k in range(n):
            t = k / n
            out.append(tuple(x + (y - x) * t for x, y in zip(a, b)))
    out.append(secs[-1])
    return out


def section_at(secs, u):
    for (u0, *a), (u1, *b) in zip(secs, secs[1:]):
        if u0 <= u <= u1:
            t = (u - u0) / (u1 - u0)
            return tuple(x + (y - x) * t for x, y in zip(a, b))
    return tuple(secs[0][1:]) if u < secs[0][0] else tuple(secs[-1][1:])


# --- weights -------------------------------------------------------------------------------------------------
SPINE = along("y", [(-1.2, "pelvis"), (-0.75, "pelvis"), (-0.4, "spine_01"), (0.05, "spine_02"), (0.45, "chest"),
                    (0.74, "chest"), (0.98, "neck_01"), (1.18, "neck_02"), (1.34, "neck_02"), (1.44, "head")])


def blend_in(w, bone, k):
    if k <= 0:
        return w
    w = {b: v * (1 - k) for b, v in w.items()}
    w[bone] = w.get(bone, 0) + k
    return w


def trunk(p):
    """Along the spine; the haunches go with the thighs and the shoulders' undersides with the scapulas (the long legs come
    out of them), so a stride moves the muscle over the bone and not a tube under a box."""
    p = Vector(p)
    w = SPINE(p)
    s = "r" if p.x > 0 else "l"
    if p.y < -0.4 and p.z < 2.0 and abs(p.x) > 0.1:
        w = blend_in(w, f"thigh_{s}", smoothstep(1.98, 1.55, p.z) * smoothstep(-0.4, -0.65, p.y) * 0.7 * smoothstep(0.1, 0.25, abs(p.x)))
    if 0.4 < p.y < 1.0 and p.z < 2.05 and abs(p.x) > 0.1:
        w = blend_in(w, f"scapula_{s}", smoothstep(2.05, 1.55, p.z) * smoothstep(0.4, 0.55, p.y) * 0.6 * smoothstep(0.1, 0.25, abs(p.x)))
    t = sum(w.values())
    return {b: v / t for b, v in w.items() if v / t > 0.02}


# --- the skin: trunk, hump, neck, head, jaw, legs, bell and tail, one continuous surface (rig.fuse) ----------------------
body = kit.part("body")

# The trunk, rump to chest: (y, half width over the middle, under it, top, bottom). Narrow hips, the barrel deep and
# slab-sided, the brisket low, and the withers rising into the hump over the shoulders. (A ghost moose: lean.)
TRUNK = [(-1.24, 0.08, 0.07, 1.92, 1.8, 1.0), (-1.18, 0.18, 0.16, 1.98, 1.58), (-1.04, 0.27, 0.27, 2.03, 1.42),
         (-0.88, 0.34, 0.33, 2.05, 1.42), (-0.7, 0.4, 0.38, 2.07, 1.4), (-0.48, 0.47, 0.46, 2.09, 1.32),
         (-0.24, 0.54, 0.58, 2.13, 1.17), (0.0, 0.59, 0.69, 2.2, 1.04), (0.22, 0.61, 0.68, 2.31, 1.02),
         (0.42, 0.6, 0.62, 2.43, 1.03), (0.58, 0.57, 0.54, 2.5, 1.09), (0.74, 0.52, 0.45, 2.49, 1.2),
         (0.88, 0.44, 0.36, 2.42, 1.36), (1.0, 0.34, 0.29, 2.35, 1.52, 0.95)]
TRUNK_AT = path_frame([(0, -1.3, 0), (0, 1.1, 0)])


def trunk_u(y):
    return (y + 1.3) / 2.4


def trunk_secs():
    return [(trunk_u(s[0]),) + tuple(s[1:]) for s in TRUNK]


def hide(k, j, a, p):
    """Hairless and stretched over the frame: the spine a low ridge, the hip points and the shoulder blades standing out,
    the ribs in faint bars behind the shoulder, the belly tucked up toward the flank."""
    q = Vector(p)
    side = abs(q.x)
    # Hip points (tuber coxae) and the shoulder blades' spines: knobs under the skin.
    for y0, z0, x0, r, k_ in ((-0.72, 2.05, 0.33, 0.16, 0.05), (0.52, 2.32, 0.45, 0.22, 0.03), (-1.08, 1.94, 0.17, 0.12, 0.03)):
        d = math.sqrt((q.y - y0) ** 2 + (q.z - z0) ** 2 + (side - x0) ** 2)
        if d < r:
            q += Vector((math.copysign(1, q.x) * 0.6, 0, 0.8)) * (k_ * (1 - d / r) ** 2)
    # The ribs: low bars down the barrel's side.
    if -0.35 < q.y < 0.35 and side > 0.25 and q.z < 2.05:
        q.x *= 1 + 0.018 * max(0.0, math.sin(q.y * 30 + 1.1)) * smoothstep(-0.35, -0.15, q.y) * smoothstep(0.35, 0.15, q.y)
    # The flank tucked up in front of the haunch.
    if -0.62 < q.y < -0.2 and q.z < 1.6:
        q.z += 0.04 * smoothstep(1.6, 1.32, q.z) * math.sin(math.pi * (q.y + 0.62) / 0.42)
    q.x *= 1 + 0.03 * noise3(q, 11, 4.0)
    q.z += 0.012 * noise3(q, 12, 5.0)
    return q


sweep(body, TRUNK_AT, finer(trunk_secs(), 3), 32, HIDE, trunk, shape=hide, cap0=True, cap1=True)

# The neck: short and deep, up out of the chest under the hump, forward and down into the back of the skull.
NECK_AT = path_frame([(0, 0.62, 1.88), (0, 0.9, 1.91), (0, 1.1, 1.95), (0, 1.28, 1.99), (0, 1.4, 2.02)])
NECK = [(0.0, 0.47, 0.44, 0.6, -0.74), (0.3, 0.41, 0.4, 0.5, -0.62), (0.55, 0.33, 0.33, 0.39, -0.5), (0.78, 0.26, 0.27, 0.29, -0.36),
        (0.92, 0.21, 0.22, 0.22, -0.27), (1.0, 0.16, 0.17, 0.15, -0.18)]
neck_w = along("y", [(0.72, "chest"), (0.86, "neck_01"), (1.04, "neck_01"), (1.18, "neck_02"), (1.32, "neck_02"), (1.42, "head")])


def neck_shape(k, j, a, p):
    q = Vector(p)
    # The crest of the neck under the hump thickened (a bull's), lumps where the sacs root.
    q.z += 0.015 * noise3(q, 13, 6.0)
    return q


sweep(body, NECK_AT, finer(NECK, 2), 28, HIDE, lambda p: neck_w(p), shape=neck_shape, cap0=True, cap1=True)

# The head: long and deep, Roman-nosed, the bulb of the muzzle overhanging, its upper lip drooping over the chin and down
# the sides of the jaw. (u from the poll, half width over / under the axis, top, bottom, squareness, the lip's channel.)
HEAD = [(-0.08, 0.07, 0.07, 0.07, -0.1, 1.0), (-0.02, 0.13, 0.14, 0.13, -0.17), (0.07, 0.16, 0.17, 0.155, -0.2),
        (0.17, 0.165, 0.165, 0.15, -0.18), (0.27, 0.145, 0.14, 0.125, -0.15), (0.37, 0.12, 0.12, 0.105, -0.13),
        (0.47, 0.108, 0.115, 0.1, -0.15, 0.9, 0.04), (0.57, 0.118, 0.135, 0.112, -0.18, 0.9, 0.08),
        (0.66, 0.13, 0.152, 0.124, -0.21, 0.9, 0.11), (0.74, 0.133, 0.155, 0.118, -0.225, 0.9, 0.12),
        (0.81, 0.118, 0.138, 0.09, -0.24, 0.9, 0.1), (0.86, 0.085, 0.1, 0.055, -0.235, 0.95, 0.06),
        (0.895, 0.035, 0.04, 0.015, -0.19, 1.0)]


HEAD = [(s[0] * 1.15, s[1] * 1.2 * (1 + 0.12 * max(0.0, s[0] - 0.55) / 0.35), s[2] * 1.2 * (1 + 0.12 * max(0.0, s[0] - 0.55) / 0.35),
         s[3] * 1.2, s[4] * 1.2) + tuple(s[5:6]) + tuple(x * 1.2 for x in s[6:]) for s in HEAD]


def head_u(p):
    return (Vector(p) - POLL).dot(ALONG)


def head_v(p):
    return (Vector(p) - POLL).dot(OVER)


def skull(k, j, a, p):
    """The brow over the eye, the cheek's long muscle, the nasal bones' ridge and the nostrils' swell, the bulb of the
    muzzle; small lumps."""
    q = Vector(p)
    u, v, x = head_u(q), head_v(q), q.x
    for (u0, v0, x0, r, h) in ((0.18, 0.1, 0.18, 0.08, 0.026), (0.06, -0.1, 0.18, 0.14, 0.022), (0.83, 0.0, 0.15, 0.09, 0.026)):
        d = math.sqrt((u - u0) ** 2 + (v - v0) ** 2 + (abs(x) - x0) ** 2)
        if d < r:
            q += Vector((math.copysign(1, x), 0, 0)) * (h * (1 - d / r) ** 2)
    return q + OVER * (0.006 * noise3(q, 21, 14.0))


def head_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    u, v = head_u(c), head_v(c)
    if u > 0.48 and v < -0.07 and abs(c.x) < 0.085:
        return MOUTH
    return MUZZLE if u > 0.63 else HIDE


sweep(body, head_at, finer(HEAD, 2), 28, HIDE, "head", shape=skull, cap0=True, cap1=True, fmat=head_mat)

# The lower jaw: its own solid in the lip's channel (the mouth the gap between them), grown into the cheek at its angle.
JAW = [(0.02, 0.12, 0.125, -0.04, -0.2), (0.12, 0.115, 0.115, -0.07, -0.25), (0.24, 0.1, 0.095, -0.1, -0.235),
       (0.38, 0.07, 0.068, -0.13, -0.22), (0.52, 0.058, 0.056, -0.145, -0.215), (0.64, 0.052, 0.05, -0.15, -0.21),
       (0.72, 0.045, 0.043, -0.155, -0.205), (0.765, 0.02, 0.02, -0.162, -0.19, 1.0)]


JAW = [(s[0] * 1.15, s[1] * 1.2, s[2] * 1.2, s[3] * 1.2, s[4] * 1.2) + tuple(s[5:]) for s in JAW]


def jaw_w(p):
    u = head_u(p)
    k = smoothstep(0.12, 0.3, u)
    return {"jaw": k, "head": 1 - k} if 0 < k < 1 else ({"jaw": 1.0} if k >= 1 else {"head": 1.0})


def jaw_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    if head_u(c) > 0.37 and n.dot(OVER) > 0.55:
        return MOUTH
    return MUZZLE if head_u(c) > 0.57 else HIDE


sweep(body, head_at, finer(JAW, 2), 20, HIDE, jaw_w, cap0=True, cap1=True, fmat=jaw_mat)

# The bell: the dewlap swollen into a long sac, hanging off the throat, a bulb at its foot; it swings as it walks.
BELL = [(0, 1.18, 1.74), (0, 1.2, 1.62), (0, 1.22, 1.52), (0, 1.235, 1.43), (0, 1.245, 1.35), (0, 1.25, 1.27), (0, 1.25, 1.2)]
bell_w = along("z", [(1.12, "bell_02"), (1.38, "bell_02"), (1.5, "bell_01"), (1.66, "bell_01"), (1.78, "neck_02")])
body.tube(BELL, [(0.09, 0.2), (0.072, 0.155), (0.068, 0.13), (0.076, 0.12), (0.084, 0.11), (0.07, 0.095), (0.03, 0.04)], 14, HIDE, bell_w,
          ref=(0, 1, 0), cap1=True, shape=lambda i, j, a, p, fr: p + fr[0] * (0.008 * noise3(p, 31, 14.0)))

# The tail: a stub.
body.tube([(0, -1.14, 1.99), (0, -1.21, 1.9), (0, -1.25, 1.8)], [0.07, 0.06, 0.035], 10, HIDE, "tail_01", ref=(0, 0, 1), cap1=True)

# The ridge's crest: the skin swollen along the spine where the sacs root (it stands up with them: the ridge bones).
RIDGE = [(-0.62, "ridge_01"), (-0.15, "ridge_01"), (-0.14, "ridge_02"), (0.36, "ridge_02"), (0.37, "ridge_03"), (0.98, "ridge_03")]


def ridge_bone(y):
    if y < -0.145:
        return "ridge_01"
    return "ridge_02" if y < 0.365 else "ridge_03"


crest = [(0, y, top_at(y) - 0.02) for y in [-0.6 + 1.56 * i / 12 for i in range(13)]]
body.tube(crest, [(0.045, 0.03)] + [(0.075, 0.05)] * 11 + [(0.045, 0.03)], 10, HIDE, lambda p: {ridge_bone(p.y): 1.0}, ref=(0, 0, 1),
          cap0=True, cap1=True, shape=lambda i, j, a, p, fr: p + fr[1] * (0.02 * max(0.0, noise3(p, 41, 9.0))))

# --- legs: too long, the forearm and the gaskin muscled, knobbed at the knees, the hocks and the fetlocks, the cannons lean
LEG_SIDES = 14


def leg_shape(bulges):
    """Muscle and bone under the skin: each (z, which way: +1 the front, -1 the back, 0 all round; how much; how far it
    spreads up and down) swells the tube there."""
    def fn(i, j, a, p, fr):
        side, up, t = fr
        c = math.cos(a)
        out = side * math.sin(a) + up * c
        q = Vector(p)
        for z0, way, amount, spread in bulges:
            k = math.exp(-((q.z - z0) / spread) ** 2)
            facing = max(0.0, c * way) ** 1.5 if way else 1.0
            q += out * (amount * k * facing)
        return q
    return fn


for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * 0.29
    front = along("z", [(0.08, "finger_{s}"), (0.17, "hand_{s}"), (0.22, "hand_{s}"), (0.6, "hand_{s}"), (0.72, "lowerarm_{s}"),
                        (1.18, "lowerarm_{s}"), (1.36, "upperarm_{s}"), (1.56, "upperarm_{s}"), (1.78, "scapula_{s}")])
    fw = lambda p, f=front, s=s: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x * 1.15, 0.68, 1.96), (x * 1.22, 0.8, 1.62), (x * 1.1, 0.73, 1.32), (x * 1.02, 0.73, 1.1), (x, 0.745, 0.9), (x, 0.755, 0.75),
               (x, 0.76, 0.66), (x, 0.765, 0.56), (x, 0.775, 0.38), (x, 0.79, 0.22), (x, 0.8, 0.16), (x, 0.825, 0.09)],
              [(0.23, 0.35), (0.21, 0.31), (0.165, 0.23), (0.135, 0.17), (0.1, 0.125), (0.08, 0.095), (0.092, 0.108), (0.068, 0.08),
               (0.061, 0.072), (0.064, 0.078), (0.07, 0.08), (0.062, 0.07)], LEG_SIDES, HIDE, fw, ref=(0, 1, 0),
              fmat=lambda pts, n: LEGS if sum(p.z for p in pts) / len(pts) < 1.0 else HIDE,
              shape=leg_shape([(1.0, 1, 0.025, 0.12), (1.3, -1, 0.05, 0.07), (0.66, 1, 0.012, 0.04), (0.17, -1, 0.012, 0.03)]))
    rear = along("z", [(0.08, "toe_{s}"), (0.17, "foot_{s}"), (0.22, "foot_{s}"), (0.7, "foot_{s}"), (0.86, "calf_{s}"),
                       (1.3, "calf_{s}"), (1.48, "thigh_{s}"), (1.95, "thigh_{s}")])
    rw = lambda p, f=rear, s=s: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    xr = sx * 0.27
    # The haunch: a long teardrop of muscle from the croup down into the gaskin, its back the hamstring's curve.
    body.tube([(xr * 0.85, -0.86, 2.0), (xr * 1.18, -0.88, 1.72), (xr * 1.14, -0.74, 1.46), (xr * 1.04, -0.66, 1.22), (xr, -0.78, 1.02),
               (xr, -0.9, 0.86), (xr, -0.93, 0.8), (xr, -0.925, 0.7), (xr, -0.9, 0.46), (xr, -0.875, 0.26), (xr, -0.86, 0.16),
               (xr, -0.835, 0.09)],
              [(0.21, 0.31), (0.25, 0.37), (0.215, 0.31), (0.155, 0.215), (0.115, 0.16), (0.088, 0.128), (0.094, 0.138), (0.07, 0.092),
               (0.061, 0.073), (0.06, 0.07), (0.07, 0.08), (0.062, 0.07)], LEG_SIDES, HIDE, rw, ref=(0, 1, 0),
              fmat=lambda pts, n: LEGS if sum(p.z for p in pts) / len(pts) < 1.05 else HIDE,
              shape=leg_shape([(1.45, -1, 0.04, 0.16), (0.82, -1, 0.03, 0.05), (1.4, 1, 0.03, 0.08), (0.17, -1, 0.012, 0.03)]))

# --- the hooves: split in two, long in the toe, black; the dewclaws behind (crisp: not in the skin) -----------------------
hooves = kit.part("hooves")
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    for x0, y0, bone, root in ((sx * 0.29, 0.86, f"finger_{s}", f"hand_{s}"), (sx * 0.27, -0.78, f"toe_{s}", f"foot_{s}")):
        for k in (-1, 1):
            # A toe: a half-cone pointed forward and splayed a little out, its sole flat on the ground.
            yaw = k * 0.12
            fwd = Vector((math.sin(yaw), math.cos(yaw), 0))
            rings = []
            for t, (half, high) in zip((0.0, 0.3, 0.62, 0.88, 1.0), ((0.042, 0.11), (0.044, 0.1), (0.037, 0.08), (0.022, 0.05), (0.005, 0.024))):
                c = Vector((x0 + k * 0.046, y0 - 0.08, 0)) + fwd * (0.19 * t)
                ring = []
                for j in range(6):
                    a = 2 * math.pi * j / 6
                    # (The inner face flat against its fellow: the split.)
                    xs = math.sin(a) * half
                    if xs * k < 0:
                        xs *= 0.45
                    zz = max(0.0, (0.5 + 0.5 * math.cos(a))) * high
                    ring.append(c + Vector((xs, 0, zz)) + fwd * (0.008 * math.cos(a) * t))
                rings.append(ring)
            hooves.loft(rings, HOOF, {bone: 0.85, root: 0.15}, cap0=True, cap1="point")
        for k in (-1, 1):
            base = Vector((x0 + k * 0.032, y0 - 0.14 + (0.02 if y0 > 0 else 0.0), 0.15))
            hooves.tube([base, base + Vector((k * 0.008, -0.03, -0.03)), base + Vector((k * 0.012, -0.045, -0.07))], [0.017, 0.013, 0.003], 4, HOOF,
                        root, ref=(0, 1, 0), cap1="point")

# --- the ears: oversized, cupped, torn: a notch out of the back edge, the tip split -----------------------------------------
ears = kit.part("ears")
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    root = Vector((sx * 0.1, 1.33, 2.23))
    d = Vector((sx * 0.8, -0.2, 0.52)).normalized()
    fwd = Vector((0, 1, 0))
    n = (fwd - d * fwd.dot(d)).normalized()       # the cup's opening (forward)
    w_side = d.cross(n).normalized() * sx          # across the ear
    L = 0.46
    rings = []
    for i in range(9):
        t = i / 8
        width = 0.13 * math.sin(math.pi * min(1.0, 0.08 + t * 0.95)) ** 0.8 + 0.02 * (1 - t)
        thick = 0.022 * (1 - 0.7 * t) + 0.004
        c = root + d * (L * t) + Vector((0, 0, -0.04 * t * t))
        ring = []
        for j in range(10):
            a = 2 * math.pi * j / 10
            across = math.sin(a)
            # The torn back edge (a bite out of it two thirds up) and the split tip.
            wk = width
            if across * sx < 0 and 0.5 < t < 0.75:
                wk *= 0.55 + 0.45 * abs(t - 0.62) / 0.13
            if t > 0.85 and abs(across) < 0.3:
                wk *= 1.0
            cup = 0.05 * (1 - t * 0.6) * (1 - across * across)
            ring.append(c + w_side * (across * wk) + n * (math.cos(a) * thick - cup))
        rings.append(ring)
    ears.loft(rings, EAR, {f"ear_{s}": 1.0}, cap0=True, cap1="point")

# --- the eyes: small, dark, wet, under the brow -------------------------------------------------------------------------------
eyes = kit.part("eyes")
for sx in (-1, 1):
    c = head_at(0.19, sx * 0.192, 0.055)
    R = rig.Matrix.Rotation(sx * math.pi / 2, 4, "Y") @ rig.Matrix.Rotation(-0.35, 4, "X")
    eyes.blob(tuple(c), (0.03, 0.022, 0.018), 10, 4, EYE, "head", rot=R, smooth=True)

# --- the sacs: engorged ticks, grape to plum, in clusters over the hide (bound to the skin they sit on after the fuse) -------
sacs = kit.part("sacs")
ridge_sacs = kit.part("ridge")


def trunk_surface(y, a):
    """A point on the trunk's skin at y, `a` round from the top, and its way out (the egg's, before the union)."""
    u = trunk_u(y)
    sec = section_at(trunk_secs(), u)
    hw_up, hw_lo, top, bot = sec[:4]
    blend = 0.5 + 0.5 * math.cos(a)
    hw = hw_lo + (hw_up - hw_lo) * blend * blend * (3 - 2 * blend)
    zc, hz = (top + bot) / 2, (top - bot) / 2
    s, cs = math.sin(a), math.cos(a)
    p = Vector((math.copysign(abs(s) ** 0.9, s) * hw, y, zc + math.copysign(abs(cs) ** 0.9, cs) * hz))
    n = Vector((s / max(hw, 1e-3), 0, cs / max(hz, 1e-3))).normalized()
    return hide(0, 0, a, p), n


def neck_surface(u, a):
    sec = section_at(NECK, u)
    hw_up, hw_lo, top, bot = sec[:4]
    blend = 0.5 + 0.5 * math.cos(a)
    hw = hw_lo + (hw_up - hw_lo) * blend * blend * (3 - 2 * blend)
    vc, hv = (top + bot) / 2, (top - bot) / 2
    s, cs = math.sin(a), math.cos(a)
    p = NECK_AT(u, math.copysign(abs(s) ** 0.9, s) * hw, vc + math.copysign(abs(cs) ** 0.9, cs) * hv)
    q = NECK_AT(u, 0, vc)
    n = (p - q).normalized()
    return p, n


def sac(part, c, r, bones, mat=SAC, seed=0, n=None):
    """One sac: a lumpy, drooping ball, stretched shiny; given the skin's way out `n`, a flattened one swollen in the hide."""
    c = Vector(c)
    if n is not None:
        R = Vector((0, 0, 1)).rotation_difference(Vector(n)).to_matrix().to_4x4()
        part.blob(tuple(c), (r, r * 0.85, r * 0.42), 6, 3, mat, bones, smooth=True, rot=R,
                  shape=lambda i, j, a, th, p: p + (p - c) * 0.1 * noise3(p, 81 + seed, 40.0))
        return
    part.blob(tuple(c), (r, r * 0.94, r * 0.88), 5, 3, mat, bones, smooth=True,
              shape=lambda i, j, a, th, p: p + (p - c) * 0.1 * noise3(p, 81 + seed, 50.0) - Vector((0, 0, 0.18 * r * max(0.0, (c - p).z / r))))


GOLDEN = math.pi * (3 - math.sqrt(5))


def cluster(surface, y, a, count, spread, side, k, big=0.05, bones=trunk):
    """Packed like a bunch of grapes out from a middle (plum-sized there, grapes round the edge)."""
    for i in range(count):
        d = spread * math.sqrt((i + 0.3) / count)
        ang = i * GOLDEN + 2.0 * h01(k, side)
        p, n = surface(y + d * math.cos(ang), side * (a + d * math.sin(ang) * 2.2))
        r = 0.022 + big * (1 - i / count) * (0.55 + 0.45 * h01(k * 3 + i, side + 9))
        sac(sacs, p + n * r * 0.12, r, bones, SAC if h01(i, k + side) > 0.3 else SAC_OLD, seed=k, n=n)


# Clusters thickest on the hump and the shoulders, the flanks behind the ribs and the rump; few on the belly. (y, round from
# the top, how many, how far they spread.)
CLUSTERS = [(-0.95, 1.05, 4, 0.12), (-0.6, 1.3, 5, 0.14), (-0.1, 1.2, 4, 0.12), (0.3, 0.9, 4, 0.12), (0.52, 1.45, 5, 0.14),
            (0.72, 0.7, 4, 0.12)]
for side in (1, -1):
    for k, (y, a, n, spread) in enumerate(CLUSTERS):
        # (Not mirror images: each side its own.)
        y = y + 0.1 * (h01(k, side) - 0.5)
        a = a + 0.25 * (h01(k + 40, side) - 0.5)
        n = max(3, n + int(3 * (h01(k + 80, side) - 0.5)))
        cluster(trunk_surface, y, a, n, spread, side, k + (0 if side > 0 else 50))
    # Up the neck under the hump and on the throat.
    for k, (u, a, n, spread) in enumerate(((0.35, 1.0, 4, 0.1), (0.7, 0.7, 3, 0.08))):
        cluster(neck_surface, u, a, n, spread * 0.6, side, 100 + k + (0 if side > 0 else 50), big=0.022, bones=lambda p: neck_w(p))
# The ridge down the spine: two staggered rows on the crest, lying back along it (they stand up when it warns).
for row in range(2):
    n = 12
    for i in range(n):
        y = -0.56 + 1.5 * (i + 0.5 * row) / n
        x = (row - 0.5) * 0.07
        r = 0.04 + 0.028 * h01(i, row + 20)
        c = Vector((x, y, top_at(y) + 0.02 + r * 0.5))
        sac(ridge_sacs, c, r, {ridge_bone(y): 1.0}, SAC, seed=200 + i)
        if row == 0 and i % 2 == 0:
            # Heaped: a second sac on the first, so the ridge stands proud of the back even lying down.
            sac(ridge_sacs, c + Vector((0.012, 0.02, r * 1.15)), r * 0.72, {ridge_bone(y): 1.0}, SAC_OLD, seed=240 + i)

# --- the rack: grown for years, never shed, mineral slag; 3.2 m across (rig.fuse: its own skin, both palms) -----------------
rack = kit.part("rack")


def palm_point(sx, s, t):
    """The palm's middle surface: s from the beam (0) out to the rim (1), t from its back edge (0) to its front (1). It fans
    out broad to the rim and rises up and out; as it broadens it turns its face forward (like an open hand: its back edge
    high, its front edge low over the brow), cups toward its face, and its outer rim curls in; the rim is scalloped
    between the tines."""
    c = 2 * t - 1
    w = 0.07 + 0.36 * smoothstep(0.0, 1.0, s) ** 0.8
    s_rim = s * (1 - 0.06 * s ** 4 * (0.5 - 0.5 * math.cos(2 * math.pi * 4 * t)))
    th = math.radians(14 + 24 * smoothstep(0.0, 0.6, s))
    face = Vector((0, math.sin(th), math.cos(th)))
    x = sx * (0.4 + 0.95 * s_rim)
    y = 1.42 - 0.14 * s * s + c * w * math.cos(th)
    z = 2.31 + 0.08 * s + 0.25 * s ** 2.2 - c * w * math.sin(th)
    p = Vector((x, y, z)) + face * (0.12 * s * c * c + 0.1 * s ** 3)
    p.z += 0.03 * noise3(p, 91, 3.5) * s
    return p


def palm_normal(sx, s, t):
    e = 1e-3
    du = palm_point(sx, min(1.0, s + e), t) - palm_point(sx, max(0.0, s - e), t)
    dv = palm_point(sx, s, min(1.0, t + e)) - palm_point(sx, s, max(0.0, t - e))
    n = du.cross(dv).normalized()
    return n if n.y + n.z > 0 else -n


def palm_thick(s):
    return 0.11 - 0.065 * s


def palm(sx, bone):
    """A thick, closed plate: lens-shaped sections across it, beam to rim."""
    rings, cents = [], []
    for i in range(15):
        s = (i / 14) ** 0.9
        ring = []
        for j in range(24):
            ph = 2 * math.pi * j / 24
            t = 0.5 - 0.5 * math.cos(ph)
            th = palm_thick(s) * (0.35 + 0.65 * math.sin(math.pi * t) ** 0.5)
            ring.append(palm_point(sx, s, t) + palm_normal(sx, s, t) * (th / 2 * math.sin(ph)))
        rings.append(ring)
        cents.append(palm_point(sx, s, 0.5))
    rack.loft(rings, RACK, bone, centres=cents, cap0=True, cap1=True)


def tine(base, d, length, r0, bone, curl=0.25, seed=0):
    """A tine: rooted in the palm with a flare, curving up as it goes, to a blunt point."""
    base, d = Vector(base), Vector(d).normalized()
    up = Vector((0, 0, 1))
    pts, radii = [], []
    for k in range(6):
        t = k / 5
        bend = up * (curl * length * t * t)
        pts.append(base - d * 0.06 + d * ((length + 0.06) * t) + bend)
        radii.append(r0 * (1.5 - 0.5 * min(1.0, t * 4)) * (1 - 0.82 * t) + 0.006)
    rack.tube(pts, radii, 8, RACK, bone, ref=(0, 0, 1), cap0=True, cap1="point",
              shape=lambda i, j, a, p, fr: p + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.008 * noise3(p, 92 + seed, 18.0)))
    return pts[-1]


TIPS = {}
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    bone = f"rack_{s}"
    # The burr and the beam: knotted where it comes out of the skull, thick as a man's thigh.
    rack.tube([(sx * 0.08, 1.45, 2.19), (sx * 0.2, 1.45, 2.27), (sx * 0.32, 1.445, 2.32), (sx * 0.46, 1.44, 2.38)],
              [0.07, 0.085, 0.08, 0.075], 12, RACK, bone, ref=(0, 1, 0), cap0=True,
              shape=lambda i, j, a, p, fr: p + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.012 * noise3(p, 93, 16.0)))
    for k in range(7):
        a = 2 * math.pi * k / 7
        c = Vector((sx * 0.17, 1.45 + 0.085 * math.cos(a), 2.26 + 0.085 * math.sin(a)))
        rack.blob(tuple(c), (0.035, 0.03, 0.03), 6, 3, RACK, bone, smooth=True)
    palm(sx, bone)
    TIPS[s] = []
    # Tines round the rim, back to front: out, up and curving, the tallest at the back (3.4 m), the brow palm's forward.
    for k in range(8):
        t = 0.03 + 0.94 * k / 7
        base = palm_point(sx, 0.97, t)
        d = Vector((sx * (0.6 + 0.2 * math.sin(math.pi * t)), -0.3 + 1.1 * t ** 1.3, 0.85 - 0.75 * t))
        length = (0.23 + 0.07 * (1 - t)) * (0.85 + 0.3 * h01(k, sx + 3))
        tip = tine(base, d, length, 0.05 - 0.01 * t, bone, curl=0.25 - 0.15 * t, seed=k + (20 if sx > 0 else 0))
        TIPS[s].append(tip)
    # Two up off the back edge, and two short knobs grown out of the palm's face.
    for k, sv in enumerate((0.55, 0.8)):
        base = palm_point(sx, sv, 0.02)
        TIPS[s].append(tine(base, Vector((sx * 0.2, -0.6, 0.75)), 0.17 + 0.05 * k, 0.04, bone, curl=0.2, seed=40 + k))
    for k, (sv, tv) in enumerate(((0.45, 0.55), (0.7, 0.35))):
        base = palm_point(sx, sv, tv)
        tine(base, palm_normal(sx, sv, tv) + Vector((sx * 0.3, 0, 0)), 0.1, 0.04, bone, curl=0.0, seed=60 + k)
    # Slag crusted on the palms: lumps and knots, grown in (the union melts them into the plate).
    for k in range(12):
        sv, tv = 0.12 + 0.8 * h01(k, sx + 50), 0.12 + 0.76 * h01(k + 9, sx + 51)
        p = palm_point(sx, sv, tv)
        n = palm_normal(sx, sv, tv)
        side = 1 if h01(k, sx + 55) > 0.35 else -1
        rack.rock(tuple(p + n * side * palm_thick(sv) * 0.4), (0.06 + 0.05 * h01(k, 52), 0.06 + 0.04 * h01(k, 53), 0.035 + 0.02 * h01(k, 54)),
                  RACK, bone, seed=520 + k + (20 if sx > 0 else 0), sides=6, rings=3, spike=1.15)

# --- velvet: bloody strips hanging off the tines and the palms' edges, swinging --------------------------------------------
velvet = kit.part("velvet")
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    hangs = [palm_point(sx, 0.95, t) - palm_normal(sx, 0.95, t) * 0.03 for t in (0.2, 0.55, 0.85)] + \
            [palm_point(sx, sv, 0.0) + Vector((0, 0.02, -0.03)) for sv in (0.5, 0.85)] + \
            [palm_point(sx, 0.7, 1.0) + Vector((0, -0.02, -0.03))] + \
            [(TIPS[s][1] * 0.35 + palm_point(sx, 1.0, 0.16) * 0.65), (TIPS[s][5] * 0.3 + palm_point(sx, 1.0, 0.7) * 0.7)]
    for k, top in enumerate(hangs):
        length = 0.18 + 0.45 * h01(k, sx + 60)
        width = 0.04 + 0.035 * h01(k, sx + 64)
        swing = Vector((0.05 * (h01(k, 61) - 0.5), 0.06 * (h01(k, 62) - 0.5), 0))
        segs = 5
        pts = [top + swing * (i / segs) ** 2 - Vector((0, 0, length * i / segs)) for i in range(segs + 1)]

        def strip(p, z0=top.z, length=length, s=s):
            down = min(1.0, (z0 - p.z) / length * 1.5)
            return {f"rack_{s}": 1.0} if down <= 0 else {f"rack_{s}": 1 - down, f"velvet_{s}": down} if down < 1 else {f"velvet_{s}": 1.0}
        # A ribbon: wide and thin, twisting as it hangs, tattered to a point.
        radii = [(width * (1 - 0.7 * (i / segs) ** 1.2) + 0.004, 0.005 * (1 - 0.5 * i / segs) + 0.002) for i in range(segs + 1)]
        velvet.tube(pts, radii, 4, VELVET, strip, ref=(1, 0.3 * (h01(k, 63) - 0.5), 0), twist=1.4 * h01(k, 63), cap0=True, cap1="point",
                    shape=lambda i, j, a, p, fr: p + fr[0] * (0.01 * noise3(p, 65, 20.0)))

# --- what it has run through, caught in the rack -----------------------------------------------------------------------------
junk = kit.part("junk", smooth=False)
# Fence wire: rusted barbed wire wound round the right palm's tines and hanging in a loop under it, a barb every hand.
wire = [TIPS["r"][1] - Vector((0, 0, 0.12)), TIPS["r"][3] - Vector((0, 0, 0.16)), palm_point(1, 0.85, 0.55) - Vector((0, 0, 0.3)),
        palm_point(1, 0.62, 0.42) - Vector((0, 0, 0.44)), palm_point(1, 0.42, 0.22) - Vector((0, 0, 0.24)), palm_point(1, 0.45, 0.08) - Vector((0, 0, 0.06))]
junk.tube(wire, [0.008] * len(wire), 5, WIRE, "rack_r", ref=(0, 0, 1))
for k in range(len(wire) - 1):
    for f in (0.3, 0.7):
        c = wire[k] + (wire[k + 1] - wire[k]) * f
        R = rig.Matrix.Rotation(0.7 * k + f, 4, "Z") @ rig.Matrix.Rotation(0.5, 4, "X")
        junk.box(tuple(c), (0.032, 0.004, 0.004), WIRE, "rack_r", rot=R)
# A telegraph insulator, green glass, still on its wire: a domed bell with its skirt.
ins = palm_point(1, 0.62, 0.42) - Vector((0, 0, 0.5))
junk.tube([ins + Vector((0, 0, 0.075)), ins + Vector((0, 0, 0.05)), ins + Vector((0, 0, 0.0)), ins - Vector((0, 0, 0.04)), ins - Vector((0, 0, 0.05))],
          [0.02, 0.04, 0.05, 0.055, 0.04], 10, INSULATOR, "rack_r", ref=(0, 1, 0), cap0="point", cap1=True)
# A smashed lamp hooked on the right brow tine: its tank, its cage, its glass broken out.
lamp = TIPS["r"][6] - Vector((0.02, 0.04, 0.2))
LR = rig.Matrix.Rotation(0.35, 4, "Y")
junk.tube([lamp - Vector((0, 0, 0.09)), lamp - Vector((0, 0, 0.05)), lamp + Vector((0, 0, 0.06)), lamp + Vector((0, 0, 0.09))],
          [0.055, 0.06, 0.055, 0.03], 8, LAMP, "rack_r", ref=(0, 1, 0), cap0=True, cap1=True)
for k in range(4):
    a = math.pi / 2 * k + 0.4
    junk.tube([lamp + Vector((0.062 * math.cos(a), 0.062 * math.sin(a), -0.05)), lamp + Vector((0.062 * math.cos(a), 0.062 * math.sin(a), 0.06))],
              [0.005, 0.005], 4, LAMP, "rack_r", ref=(0, 0, 1))
junk.slab([lamp + Vector((-0.03, 0.05, -0.03)), lamp + Vector((0.035, 0.05, -0.035)), lamp + Vector((0.02, 0.052, 0.04))], 0.004, LAMP_GLASS, "rack_r",
          down=(0, -1, 0))
junk.tube([lamp + Vector((0, 0, 0.09)), lamp + Vector((0.02, 0.03, 0.17)), TIPS["r"][6] - Vector((0, 0.01, 0.02))], [0.005, 0.005, 0.005], 4, LAMP,
          "rack_r", ref=(0, 1, 0))
# A crew cap snagged on a left rim tine: its crown and its peak.
cap = TIPS["l"][3] * 0.55 + palm_point(-1, 1.0, 0.4) * 0.45 - Vector((0, 0, 0.06))
CR = rig.Matrix.Rotation(0.6, 4, "Y") @ rig.Matrix.Rotation(-0.3, 4, "X")
junk.blob(tuple(cap), (0.1, 0.11, 0.05), 10, 4, CAP, "rack_l", rot=CR, smooth=True)
junk.slab([cap + CR.to_3x3() @ Vector(v) for v in ((-0.075, 0.06, -0.02), (0.075, 0.06, -0.02), (0.06, 0.15, -0.045), (-0.06, 0.15, -0.045))],
          0.008, CAP, "rack_l")
# Splintered planks jammed across the left palm, sticking out past the tines: a board and a broken half of one, their ends
# torn to splinters.


def plank(a, b, width, thick, bone, seed):
    a, b = Vector(a), Vector(b)
    d = (b - a).normalized()
    up = Vector((0, 0, 1))
    side = d.cross(up).normalized()
    up = side.cross(d).normalized()
    junk.tube([a, b], [(width, thick), (width, thick)], 4, WOOD, bone, ref=tuple(up), cap0=True, twist=math.pi / 4)
    for k in range(4):
        off = (k / 3 - 0.5) * width * 1.4
        length = 0.06 + 0.12 * h01(k, seed)
        junk.tube([b + side * off - d * 0.01, b + side * (off * 1.1) + d * length], [(0.018, thick * 0.9), 0.002], 4, WOOD, bone, ref=tuple(up),
                  cap1="point")


plank(palm_point(-1, 0.1, 0.78) + Vector((0, 0, 0.08)), palm_point(-1, 1.0, 0.42) + Vector((-0.25, -0.12, 0.14)), 0.06, 0.016, "rack_l", 1)
plank(palm_point(-1, 0.38, 0.12) + Vector((0, 0, 0.1)), palm_point(-1, 0.95, 0.9) + Vector((-0.1, 0.18, 0.1)), 0.05, 0.015, "rack_l", 2)

# One skin: the trunk, the neck, the head and its jaw, the legs, the bell, the tail and the crest grown into one another,
# no seam where they meet (rig.fuse); the rack's beams, burrs, palms, tines and slag likewise, a skin of their own.
kit.fuse("skin", ["body"], voxel=0.0095, faces=3850, lose=0.03, relax=6)
kit.fuse("rack", ["rack"], voxel=0.0075, faces=1850, quads=False)
kit.build()


def rebind(part_name, skin_name, keep=None):
    """A part's weights taken from the skin under it (each vertex the skin's nearest point's, interpolated), so what sits on
    the skin rides it as it stretches."""
    import bpy
    from mathutils.bvhtree import BVHTree
    from mathutils.interpolate import poly_3d_calc
    skin = bpy.data.objects[skin_name]
    o = bpy.data.objects[part_name]
    names = {g.index: g.name for g in skin.vertex_groups}
    sv = [v.co.copy() for v in skin.data.vertices]
    sw = [{names[g.group]: g.weight for g in v.groups if g.weight > 0} for v in skin.data.vertices]
    polys = [list(f.vertices) for f in skin.data.polygons]
    tree = BVHTree.FromPolygons(sv, polys)
    o.vertex_groups.clear()
    groups = {}
    for v in o.data.vertices:
        hit, _, i, _ = tree.find_nearest(v.co)
        ring = polys[i]
        k = poly_3d_calc([sv[j] for j in ring], hit)
        acc = {}
        for j, kk in zip(ring, k):
            for b, x in sw[j].items():
                acc[b] = acc.get(b, 0.0) + x * kk
        top = sorted(acc.items(), key=lambda t: (-t[1], t[0]))[:4]
        tot = sum(x for _, x in top)
        for b, x in top:
            if x / tot > 0.02:
                g = groups.get(b) or groups.setdefault(b, o.vertex_groups.new(name=b))
                g.add([v.index], x / tot, "REPLACE")


def hard_edges(name, angle):
    """The rack's plates' edges shade hard (the slag's facets catch the lamp), its faces smooth."""
    import bmesh
    import bpy
    o = bpy.data.objects[name]
    bm = bmesh.new()
    bm.from_mesh(o.data)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2 and e.calc_face_angle(0) > math.radians(angle):
            e.smooth = False
    bm.to_mesh(o.data)
    bm.free()


rebind("sacs", "skin")
hard_edges("rack", 38)

# --------------------------------------------------------------------------------------------------------------
# Clips. Angles are the armature's axes (rig.rot): +X tips a forward-pointing bone's end up (a head raised) and swings a
# hanging leg forward; the ridge's bones lie back along the spine, so -X stands them up; +Z turns towards -X (the left).


def curve(keys, t):
    t %= 1.0
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / max(t1 - t0, 1e-9)
            u = u * u * (3 - 2 * u)
            return v0 + (v1 - v0) * u
    return keys[-1][1]


def legs(front, rear, phases, stride=1.0):
    def pose(t):
        out = {}
        for side in ("r", "l"):
            for group, table in (("front", front), ("rear", rear)):
                ph = t + phases[f"{group}_{side}"]
                for joint, keys in table.items():
                    out[f"{joint}_{side}"] = (curve(keys, ph) * stride, 0, 0)
        return out
    return pose


def bell_hangs(p, swing=0.0):
    """The bell hangs straight down whatever the neck does (it's a sac on the throat), swinging."""
    tip = sum(p.get(b, (0, 0, 0))[0] for b in ("neck_01", "neck_02", "chest", "spine_02", "spine_01", "pelvis"))
    out = dict(p)
    # (Tucked, the head would sweep into it: it swings back out of the way under the throat.)
    out["bell_01"] = (-tip * 0.6 + 0.6 * min(0.0, p.get("head", (0, 0, 0))[0] if not isinstance(p.get("head"), rig.Quaternion) else 0) + swing, 0, 0)
    # (Its bulb folded back under the throat as the head tucks down onto it, a sac bent double.)
    tuck = min(0.0, p.get("head", (0, 0, 0))[0]) if not isinstance(p.get("head"), rig.Quaternion) else -26.0
    # (Tucked hard, it can't swing forward: the head's in the way.)
    out["bell_02"] = ((swing if tuck > -20 else min(swing, 0.0)) * 0.6 + 1.4 * tuck, 0, 0)
    return out


def held_world(pose, bone, world):
    """The local rotation that leaves `bone` at world rotation `world` (a Quaternion) whatever its parents do: the head and
    the rack held level on the line through a gallop."""
    parent = sk[bone].parent
    Wp = rig.world_rotation(sk, pose, parent) if parent else rig.Quaternion()
    return Wp.inverted() @ world


EARS_IDLE = mirror({"ear_r": (0, 0, 0)})
EARS_FORWARD = mirror({"ear_r": (8, -10, 48)})       # listening: turned to the front, up
EARS_FLAT = mirror({"ear_r": (0, -12, -42)})          # pinned flat back along the neck: it's coming
# (Swollen up off the spine and raised a little, not lifted off it: the bones lie along the skin, so scaling them across
# their length pushes the sacs up out of it and swells them.)
RIDGE_UP = {"ridge_01": (-8, 0, 0), "ridge_02": (-10, 0, 0), "ridge_03": (-8, 0, 0),
            "ridge_01@scale": (1.3, 1.0, 1.5), "ridge_02@scale": (1.35, 1.0, 1.6), "ridge_03@scale": (1.3, 1.0, 1.5)}
STAND = {"root@loc": (0, 0, 0)}

# Head down in the browse: the front of it let down, the forelegs a little spread, the neck and head reached down to the bog
# shrubs (the rack's palms come down beside its knees).
GRAZE = over(STAND, root__loc=(0, 0, -0.04), spine_01=(-1, 0, 0), spine_02=(-3, 0, 0), chest=(-5, 0, 0), neck_01=(-30, 0, 0),
             neck_02=(-25, 0, 0), head=(26, 0, 0), tail_01=(4, 0, 0))
GRAZE.update(mirror({"upperarm_r": (10, -5, 0), "lowerarm_r": (-6, 0, 0), "hand_r": (-4, 0, 0), "thigh_r": (-4, 0, 0)}))
graze = Clip("graze")
# 4 s: chewing, the ears never still, the sacs slowly pulsing, the weight shifting from foot to foot.
for f in range(0, 120, 6):
    k = f / 120
    chew = 1 if (f // 6) % 2 else 0
    er = 20 * math.sin(2 * math.pi * k * 3) if (f // 24) % 2 == 0 else -12
    el = -16 * math.sin(2 * math.pi * k * 2 + 1) if (f // 30) % 2 == 1 else 10
    pulse = 1.0 + 0.06 * math.sin(2 * math.pi * k * 2)
    sway = math.sin(2 * math.pi * k)
    p = over(GRAZE, jaw=(-4 * chew, 0, 2 * chew), head=(26 + 2 * sway, 0, 4 * sway),
             neck_01=(-30, 0, 2 * sway), neck_02=(-25, 0, 3 * sway), spine_02=(-3, 0, -1.5 * sway), spine_01=(-1, 0, 1.0 * sway),
             ear_r=(0, 0, er), ear_l=(0, 0, -el),
             ridge_01__scale=(pulse, 1, pulse), ridge_02__scale=(2 - pulse, 1, 2 - pulse), ridge_03__scale=(pulse, 1, pulse),
             tail_01=(4 + (12 if f in (48, 54) else 0), 0, 0))
    graze.key(f, bell_hangs(p, 2 * sway), "CONSTANT" if f % 24 == 0 else "BEZIER")
graze.close(120)

# Listening (aggro 20+): head up, the chewing stopped, the ears forward and turning; dead still otherwise.
LISTEN = over(STAND, spine_02=(1, 0, 0), chest=(2, 0, 0), neck_01=(6, 0, 0), neck_02=(4, 0, 0), head=(4, 0, 0))
LISTEN.update(EARS_FORWARD)
listen = Clip("listen")
for f, (er, el, hz) in zip((0, 14, 30, 44, 60), ((48, 48, 0), (30, 62, 4), (62, 36, -3), (44, 52, 2), (48, 48, 0))):
    p = over(LISTEN, ear_r=(8, -10, er), ear_l=(8, 10, -el), head=(4, 0, hz), neck_02=(4, 0, hz * 0.5))
    listen.key(f, bell_hangs(p), "CONSTANT" if f in (14, 44) else "BEZIER")
listen.close(60)

# Warning (aggro 50+): the head comes down and the rack swings toward you, the ears pinned flat, the ridge of sacs stood
# up; a cough-grunt (the jaw), a hoof dragged back through the ground.
WARN = over(STAND, root__loc=(0, 0, -0.03), spine_01=(-1, 0, 0), spine_02=(-2, 0, 0), chest=(-4, 0, 0), neck_01=(-6, 0, 0),
            neck_02=(-10, 0, 0), head=(-12, 0, 0), tail_01=(-14, 0, 0))
WARN.update(EARS_FLAT)
WARN.update(RIDGE_UP)
warn = Clip("warn")
warn.key(0, bell_hangs(WARN))
warn.key(10, bell_hangs(over(WARN, jaw=(-9, 0, 0), head=(-8, 0, 0), chest=(-2, 0, 0))), "CONSTANT")
warn.key(14, bell_hangs(WARN), "CONSTANT")
# The drag: the right forehoof lifted forward and raked back, the shoulder working over it.
warn.key(24, bell_hangs(over(WARN, upperarm_r=(24, 0, 0), lowerarm_r=(30, 0, 0), hand_r=(-30, 0, 0), scapula_r=(4, 0, 0), spine_02=(-2, 0, 3))))
warn.key(32, bell_hangs(over(WARN, upperarm_r=(-14, 0, 0), lowerarm_r=(4, 0, 0), hand_r=(-6, 0, 0), head=(-16, 0, 6), scapula_r=(-3, 0, 0),
                             spine_02=(-2, 0, -2))), "LINEAR")
warn.key(40, bell_hangs(WARN))
warn.key(52, bell_hangs(over(WARN, jaw=(-9, 0, 0), head=(-8, 0, -4))), "CONSTANT")
warn.key(56, bell_hangs(WARN), "CONSTANT")
warn.close(66)

# Walk (2.5 m/s, a stride every second): a four-beat walk, the long legs swinging slow, the shoulders rolling over them,
# the rack nodding with each step.
WALK_FRONT = {"scapula": [(0, 5), (0.55, -6), (0.7, -2), (0.85, 4), (1, 5)],
              "upperarm": [(0, 12), (0.55, -18), (0.68, -12), (0.85, 12), (1, 12)],
              "lowerarm": [(0, 0), (0.55, -2), (0.68, 34), (0.85, 12), (1, 0)],
              "hand": [(0, 2), (0.55, -10), (0.68, -60), (0.85, -6), (1, 2)]}
WALK_REAR = {"thigh": [(0, 16), (0.55, -16), (0.68, -6), (0.85, 20), (1, 16)],
             "calf": [(0, -6), (0.55, 4), (0.68, -34), (0.85, -22), (1, -6)],
             "foot": [(0, 4), (0.55, 4), (0.68, 40), (0.85, 20), (1, 4)]}
walk_legs = legs(WALK_FRONT, WALK_REAR, {"rear_l": 0.0, "front_l": 0.25, "rear_r": 0.5, "front_r": 0.75})


def gait(name, frames, base, legs_at, stride=1.0, nod=3.0, bob=0.03, sway=3.0, swing=6.0, ears=None, extra=None):
    """A gait: the legs' cycle under a body that rolls and flexes over it (the hips dropping to the leg in the air, the
    spine bending from side to side, the shoulders working, the neck taking up the nod so the rack bobs less than the
    body), the bell and the velvet swinging after it."""
    c = Clip(name)
    for f in range(0, frames, 2 if frames <= 24 else 3):
        t = f / frames
        p = dict(base)
        p.update({k: (v[0] * stride, v[1], v[2]) for k, v in legs_at(t).items()})
        w = math.sin(4 * math.pi * t)
        sd = math.sin(2 * math.pi * t)
        b = lambda k: base.get(k, (0, 0, 0))  # noqa: E731
        p["root@loc"] = (0, 0, b("root@loc")[2] + bob * w)
        p["pelvis"] = (b("pelvis")[0] + 1.2 * w, 2.5 * sd, 1.5 * sd)
        p["spine_01"] = (b("spine_01")[0] - 0.8 * w, 0, -sway * 0.6 * sd)
        p["spine_02"] = (b("spine_02")[0] - 0.4 * w, -1.5 * sd, sway * 0.4 * math.sin(2 * math.pi * t + 0.8))
        p["chest"] = (b("chest")[0], -1.5 * sd, -sway * math.sin(2 * math.pi * t + 0.4))
        p["neck_01"] = (b("neck_01")[0] - nod * 0.5 * w, 0, b("neck_01")[2] + sway * 0.5 * sd)
        p["head"] = (b("head")[0] + nod * w, 0, b("head")[2])
        p["velvet_r"] = (swing * math.sin(2 * math.pi * t + 0.6), 0, swing * 0.5 * math.cos(2 * math.pi * t))
        p["velvet_l"] = (swing * math.sin(2 * math.pi * t + 0.9), 0, -swing * 0.5 * math.cos(2 * math.pi * t))
        p["tail_01"] = (b("tail_01")[0], 0, 6 * sd)
        if ears is not None:
            p.update(ears(t))
        if extra is not None:
            p.update(extra(t, p))
        c.key(f, bell_hangs(p, 8 * math.sin(2 * math.pi * t + 1.2)), "LINEAR")
    c.close(frames)
    return c


WALK_BODY = over(STAND, spine_02=(-1, 0, 0), neck_01=(-4, 0, 0), neck_02=(-4, 0, 0), head=(-4, 0, 0))
walk = gait("walk", 30, WALK_BODY, walk_legs, ears=lambda t: mirror({"ear_r": (0, 0, 12 * math.sin(2 * math.pi * t))}))
# Strut (the walk home, 1.2 s a stride): stiff-legged and swaggering, the head up, the rack swung side to side.
STRUT_BODY = over(STAND, neck_01=(4, 0, 0), neck_02=(2, 0, 0), head=(4, 0, 0))
strut = gait("strut", 36, STRUT_BODY, walk_legs, stride=0.85, nod=1.5, sway=5,
             extra=lambda t, p: {"neck_02": (2, 0, 9 * math.sin(2 * math.pi * t)), "head": (4, 0, 6 * math.sin(2 * math.pi * t - 0.5))})
# Search (2.4 s, slow): walking where it lost them, the head sweeping low side to side, the ears swivelling, snorting.
SEARCH_BODY = over(STAND, neck_01=(-12, 0, 0), neck_02=(-10, 0, 0), head=(-6, 0, 0), tail_01=(-8, 0, 0))
search = gait("search", 72, SEARCH_BODY, walk_legs, stride=0.7, nod=2, sway=2,
              ears=lambda t: {"ear_r": (6, -6, 50 * math.sin(2 * math.pi * t * 2)), "ear_l": (6, 6, -50 * math.sin(2 * math.pi * t * 2 + 1.4))},
              extra=lambda t, p: {"neck_01": (-12, 0, 16 * math.sin(2 * math.pi * t)), "neck_02": (-10, 0, 14 * math.sin(2 * math.pi * t)),
                                  "head": (-6 + 3 * math.sin(4 * math.pi * t), 0, 10 * math.sin(2 * math.pi * t)),
                                  "jaw": (-5 if math.sin(6 * math.pi * t) > 0.7 else 0, 0, 0)})

# Trot (4.5 m/s, a stride every two thirds of a second): diagonal pairs, the head out level, the ears back.
TROT_FRONT = {"scapula": [(0, 8), (0.45, -8), (0.6, -4), (0.8, 6), (1, 8)],
              "upperarm": [(0, 20), (0.45, -26), (0.6, -14), (0.8, 20), (1, 20)],
              "lowerarm": [(0, 0), (0.45, -2), (0.6, 46), (0.8, 20), (1, 0)],
              "hand": [(0, 4), (0.45, -16), (0.6, -80), (0.8, -10), (1, 4)]}
TROT_REAR = {"thigh": [(0, 24), (0.45, -24), (0.6, -10), (0.8, 26), (1, 24)],
             "calf": [(0, -10), (0.45, 8), (0.6, -44), (0.8, -30), (1, -10)],
             "foot": [(0, 6), (0.45, 4), (0.6, 56), (0.8, 30), (1, 6)]}
TROT_BODY = over(STAND, root__loc=(0, 0, 0.02), spine_02=(-1, 0, 0), neck_01=(-6, 0, 0), neck_02=(-6, 0, 0), head=(-2, 0, 0),
                 tail_01=(-10, 0, 0))
TROT_BODY.update(mirror({"ear_r": (4, 14, -30)}))
trot = gait("trot", 20, TROT_BODY, legs(TROT_FRONT, TROT_REAR, {"rear_l": 0.0, "front_r": 0.02, "rear_r": 0.5, "front_l": 0.52}),
            nod=3, bob=0.06, sway=2, swing=12)

# Square up (2.5 s, once; the telegraph): the head drops and the rack comes level, pointed at them; ears flat, the ridge up;
# two stamps of the forehooves; then it holds, dead still, the heading locked.
LEVEL = over(STAND, root__loc=(0, 0, -0.08), spine_01=(-2, 0, 0), spine_02=(-3, 0, 0), chest=(-6, 0, 0), neck_01=(-16, 0, 0),
             neck_02=(-10, 0, 0), head=(-26, 0, 0), jaw=(-3, 0, 0), tail_01=(-20, 0, 0))
LEVEL.update(EARS_FLAT)
LEVEL.update(RIDGE_UP)
LEVEL.update(mirror({"upperarm_r": (-6, 0, 0), "lowerarm_r": (6, 0, 0), "hand_r": (-2, 0, 0), "thigh_r": (8, 0, 0), "calf_r": (-12, 0, 0),
                     "foot_r": (8, 0, 0)}))
STAMP_R = over(LEVEL, upperarm_r=(26, 0, 0), lowerarm_r=(40, 0, 0), hand_r=(-44, 0, 0), head=(-30, 0, 0), scapula_r=(5, 0, 0), chest=(-7, 0, 2))
STAMP_L = over(LEVEL, upperarm_l=(26, 0, 0), lowerarm_l=(40, 0, 0), hand_l=(-44, 0, 0), head=(-30, 0, 0), scapula_l=(5, 0, 0), chest=(-7, 0, -2))
square = Clip("squareUp", loop=False)
square.key(0, bell_hangs(WARN))
square.key(12, bell_hangs(LEVEL))
square.key(22, bell_hangs(STAMP_R), "BEZIER")
square.key(26, bell_hangs(LEVEL), "LINEAR")
square.key(44, bell_hangs(STAMP_L), "BEZIER")
square.key(48, bell_hangs(LEVEL), "LINEAR")
square.key(56, bell_hangs(over(LEVEL, jaw=(-10, 0, 0))), "CONSTANT")
square.key(60, bell_hangs(LEVEL), "CONSTANT")
square.key(75, bell_hangs(over(LEVEL, root__loc=(0, -0.04, -0.1))))

# Charge (11 m/s): a flat-out rotary gallop, 16 frames a stride. The hind pair lands (left, right), drives, and the body
# extends over the fore pair (right, left) reaching out ahead; then the gathered flight, every leg folded under it, the back
# rounded. The spine flexes through it and the hump heaves, the ridge up, the bell flung about; the head and the rack held
# level and dead steady on the line however the body pitches (the neck takes it up).
GALLOP_FRONT = {"scapula": [(0, 8), (0.22, 0), (0.42, -10), (0.6, -5), (0.85, 10), (1, 8)],
                "upperarm": [(0, 26), (0.22, 2), (0.42, -30), (0.6, -16), (0.85, 30), (1, 26)],
                "lowerarm": [(0, 2), (0.22, 0), (0.42, 4), (0.6, 56), (0.85, 20), (1, 2)],
                "hand": [(0, 4), (0.22, -6), (0.42, -40), (0.6, -76), (0.85, -10), (1, 4)]}
GALLOP_REAR = {"thigh": [(0, 26), (0.22, 2), (0.42, -46), (0.6, -20), (0.85, 30), (1, 26)],
               "calf": [(0, -24), (0.22, -6), (0.42, 28), (0.6, -36), (0.85, -46), (1, -24)],
               "foot": [(0, 24), (0.22, 8), (0.42, 16), (0.6, 64), (0.85, 40), (1, 24)]}
gallop = legs(GALLOP_FRONT, GALLOP_REAR, {"rear_l": 0.0, "rear_r": -0.1, "front_r": -0.4, "front_l": -0.5})
CHARGE_BODY = over(LEVEL, root__loc=(0, 0, 0.0))
HEAD_LEVEL = rig.world_rotation(sk, LEVEL, "head")
charge = Clip("charge")
for f in range(0, 16, 2):
    t = f / 16
    p = dict(CHARGE_BODY)
    p.update(gallop(t))
    # (The hind legs swung a little wide, outside the forelegs: they reach past them at the gallop.)
    p["thigh_r"], p["thigh_l"] = (p["thigh_r"][0], -9, 0), (p["thigh_l"][0], 9, 0)
    # Gathered (the back rounded, the hind end under) at the flight, after the forelegs leave; extended as the hinds drive.
    gather = math.cos(2 * math.pi * (t - 0.8))
    heave = math.sin(2 * math.pi * (t - 0.15))
    p.update({"root@loc": (0, 0, 0.12 * math.sin(2 * math.pi * (t - 0.6)) - 0.04),
              # (Pitched forward over its shoulders the whole stride, the front driving low, never rearing.)
              "pelvis": (-7 - 3 * gather, 0, 0), "spine_01": (3 * gather, 0, 0), "spine_02": (-3 + 2 * gather, 0, 0),
              "chest": (-7 + 3 * heave, 0, 0), "scapula_r": (p["scapula_r"][0], 0, 0), "scapula_l": (p["scapula_l"][0], 0, 0),
              "neck_01": (-20 - 4 * heave, 0, 0), "neck_02": (-12 - 2 * heave, 0, 0),
              "jaw": (-7, 0, 0), "tail_01": (-30 + 8 * gather, 0, 0),
              "velvet_r": (30 * heave, 0, 10), "velvet_l": (28 * math.sin(2 * math.pi * t + 0.5), 0, -10)})
    p["head"] = held_world(p, "head", HEAD_LEVEL)
    charge.key(f, bell_hangs(p, -30 + 14 * heave), "LINEAR")
charge.close(16)

# Overrun (2.5 s, once): the skid, its hind end under it and the forelegs braced, ploughing; then it wheels round on its
# haunches, stamping, head tossing.
SKID = over(LEVEL, root__loc=(0, 0.05, -0.18), pelvis=(-10, 0, 0), spine_01=(4, 0, 0), chest=(6, 0, 0), neck_01=(4, 0, 0), head=(-14, 0, 0))
SKID.update(mirror({"upperarm_r": (-34, 0, 0), "lowerarm_r": (6, 0, 0), "hand_r": (10, 0, 0), "thigh_r": (32, 0, 0), "calf_r": (-34, 0, 0),
                    "foot_r": (30, 0, 0), "ear_r": (0, -12, -42)}))
overrun = Clip("overrun", loop=False)
c0 = dict(charge.keys[0][1])
c0["head"] = (-26, 0, 0)
overrun.key(0, bell_hangs(c0, -30))
overrun.key(5, bell_hangs(SKID, 8), "LINEAR")
overrun.key(18, bell_hangs(over(SKID, root__loc=(0, 0.02, -0.16)), 20))
# The wheel: up on the haunches, the forehand swung round (the sim turns its heading), the forelegs pawing.
REAR_UP = over(SKID, root__loc=(0, -0.2, 0.05), pelvis=(-20, 0, 0), spine_01=(8, 0, 6), spine_02=(6, 0, 8), chest=(6, 0, 10),
               neck_01=(10, 0, 8), head=(6, 0, 10), jaw=(-10, 0, 0))
REAR_UP.update({"upperarm_r": (50, 0, 0), "lowerarm_r": (50, 0, 0), "hand_r": (-60, 0, 0), "upperarm_l": (30, 0, 0), "lowerarm_l": (60, 0, 0),
                "hand_l": (-70, 0, 0)})
overrun.key(30, bell_hangs(REAR_UP, 20))
overrun.key(40, bell_hangs(over(REAR_UP, head=(-10, 0, -20), neck_01=(0, 0, -10), upperarm_r=(10, 0, 0), upperarm_l=(40, 0, 0)), -10))
overrun.key(52, bell_hangs(over(LEVEL, head=(-20, 0, 14), neck_01=(-12, 0, 8)), 14))
overrun.key(60, bell_hangs(STAMP_R), "LINEAR")
overrun.key(64, bell_hangs(LEVEL), "LINEAR")
overrun.key(75, bell_hangs(LEVEL))

# Snag (1.2 s, loop): the rack jammed in something it can't get through: the head fixed forward and low, the body hauling
# back on braced legs, the neck straining, the head wrenched side to side in pops, bellowing.
SNAG = over(LEVEL, root__loc=(0, -0.14, -0.12), pelvis=(-8, 0, 0), spine_01=(2, 0, 0), chest=(2, 0, 0), neck_01=(-6, 0, 0),
            neck_02=(-4, 0, 0), head=(-38, 0, 0), jaw=(-11, 0, 0))
SNAG.update(mirror({"upperarm_r": (24, 0, 0), "lowerarm_r": (-2, 0, 0), "hand_r": (-14, 0, 0), "thigh_r": (30, 0, 0), "calf_r": (-30, 0, 0),
                    "foot_r": (28, 0, 0)}))
snag = Clip("snag")
for f, (w, back, interp_) in enumerate(((0, 0.0, "CONSTANT"), (14, 0.04, "CONSTANT"), (-16, 0.08, "LINEAR"), (10, 0.03, "CONSTANT"),
                                        (-12, 0.06, "CONSTANT"), (0, 0.02, "LINEAR"))):
    snag.key(f * 6, bell_hangs(over(SNAG, root__loc=(0, -0.14 - back, -0.12), neck_01=(-6, 0, w * 0.4), neck_02=(-4, 0, w * 0.5),
                                    head=(-38, w * 0.8, w * 0.3), spine_02=(0, 0, -w * 0.3), spine_01=(2, 0, w * 0.2),
                                    jaw=(-11 - abs(w) * 0.6, 0, 0)), w), interp_)
snag.close(36)

# Ram (3 s, loop: one ram every 3 s, enemies.json moose ramEvery): backs a step, drops the rack, drives it into the car's
# side (the boom), shoves, grinds it along the plates, draws back.
ram = Clip("ram")
ram.key(0, bell_hangs(LEVEL))
ram.key(18, bell_hangs(over(LEVEL, root__loc=(0, -0.3, -0.06), head=(-28, 0, 0), neck_01=(-10, 0, 0), spine_01=(3, 0, 0), pelvis=(-4, 0, 0))))
ram.key(26, bell_hangs(over(LEVEL, root__loc=(0, 0.35, -0.12), head=(-32, 0, 0), neck_01=(-20, 0, 0), chest=(-10, 0, 0), spine_02=(-5, 0, 0),
                            thigh_r=(-20, 0, 0), thigh_l=(-24, 0, 0), calf_r=(10, 0, 0), calf_l=(14, 0, 0)), -20), "LINEAR")
ram.key(28, bell_hangs(over(LEVEL, root__loc=(0, 0.28, -0.12), head=(-30, 0, 0), neck_01=(-18, 0, 0), chest=(-8, 0, 0)), 6), "CONSTANT")
ram.key(40, bell_hangs(over(LEVEL, root__loc=(0, 0.3, -0.12), head=(-30, 3, 3), neck_01=(-18, 0, 8), jaw=(-9, 0, 0), spine_02=(-3, 0, -3)), 10))
ram.key(52, bell_hangs(over(LEVEL, root__loc=(0, 0.3, -0.12), head=(-30, -3, -3), neck_01=(-18, 0, -8), spine_02=(-3, 0, 3)), -10))
ram.key(66, bell_hangs(over(LEVEL, root__loc=(0, 0.0, -0.08))))
ram.close(90)

# Pin (1.2 s, loop; GRAB): someone under the rack, ground into the peat: the head right down, the palms either side of
# them, the forelegs braced wide, shoving, the head grinding side to side.
PIN = over(STAND, root=(-20, 0, 0), root__loc=(0, -0.55, -0.45), spine_02=(-2, 0, 0), chest=(-6, 0, 0), neck_01=(-30, 0, 0),
           neck_02=(-8, 0, 0), head=(45, 0, 0), jaw=(-6, 0, 0), tail_01=(-20, 0, 0))
# (Its ears turned forward, down at who it has: back along the neck they'd be through it, the head down this far.)
PIN.update(mirror({"ear_r": (0, -14, 55)}))
PIN.update(RIDGE_UP)
# Down on its knees: each knee on the ground ahead, the cannon folded back under it along the ground; the hind legs
# stretched back and dug in, shoving.
for sx, s in ((1, "r"), (-1, "l")):
    PIN = rig.reach(sk, PIN, f"upperarm_{s}", f"lowerarm_{s}", (sx * 0.34, 1.0, 0.12), elbow_axis=0, bend=1)
    PIN[f"hand_{s}"] = rig.hang(sk, PIN, f"hand_{s}", -98)
    PIN[f"finger_{s}"] = rig.hang(sk, PIN, f"finger_{s}", -60)
    PIN = rig.reach(sk, PIN, f"thigh_{s}", f"calf_{s}", (sx * 0.28, -1.2, 0.72), elbow_axis=0, bend=-1)
    PIN[f"foot_{s}"] = rig.hang(sk, PIN, f"foot_{s}", -14)
pin = Clip("pin")
for f, (w, push) in enumerate(((0, 0.0), (10, 0.04), (0, 0.06), (-10, 0.03))):
    pin.key(f * 9, bell_hangs(over(PIN, root__loc=(0, -0.55 + push, -0.45 - push * 0.5), head=(45, w * 0.6, w * 0.6), neck_02=(-8, 0, w * 0.4),
                                   spine_02=(-2, 0, -w * 0.2), jaw=(-6 - 6 * (f % 2), 0, 0))))
pin.close(36)

# A train going by (2 s, loop): head up and tossing, bellowing after it, a forehoof thrashing the verge.
toss = Clip("trainPass")
TOSS = over(LISTEN, neck_01=(14, 0, 0), neck_02=(8, 0, 0), head=(18, 0, 0))
TOSS.update(EARS_FORWARD)
toss.key(0, bell_hangs(TOSS))
toss.key(8, bell_hangs(over(TOSS, head=(30, 0, 12), neck_02=(10, 0, 8), jaw=(-16, 0, 0), spine_02=(2, 0, 3))), "CONSTANT")
toss.key(16, bell_hangs(over(TOSS, head=(4, 0, -13), neck_02=(4, 0, -7), jaw=(-18, 0, 0), upperarm_r=(30, 0, 0), lowerarm_r=(50, 0, 0),
                             hand_r=(-50, 0, 0), scapula_r=(5, 0, 0), spine_02=(1, 0, -3)), 20), "LINEAR")
toss.key(24, bell_hangs(over(TOSS, jaw=(-12, 0, 0), upperarm_r=(-10, 0, 0)), -15), "LINEAR")
toss.key(38, bell_hangs(over(TOSS, head=(22, 0, 8), jaw=(-17, 0, 0))), "CONSTANT")
toss.key(46, bell_hangs(TOSS))
toss.close(60)

# Hit (0.5 s, once): a blow lands; it doesn't hurt it. The head tosses round at it and the ridge flares, too fast.
hit = Clip("hit", loop=False)
HIT = over(WARN, head=(-2, 0, 13), neck_02=(0, 0, 8), neck_01=(0, 0, 5), jaw=(-12, 0, 0), spine_02=(0, 6, -6), spine_01=(0, 3, -3),
           root__loc=(-0.04, 0, -0.03))
hit.key(0, bell_hangs(LISTEN), "CONSTANT")
hit.key(1, bell_hangs(HIT, 18), "CONSTANT")
hit.key(6, bell_hangs(over(HIT, head=(-8, 0, 10)), -10), "LINEAR")
hit.key(15, bell_hangs(WARN), "CONSTANT")

CLIPS = [graze, listen, warn, walk, strut, search, trot, square, charge, overrun, snag, ram, pin, toss, hit]
HOOVES = ("hand_l", "hand_r", "foot_l", "foot_r", "finger_l", "finger_r", "toe_l", "toe_r")
_planted = rig.feet_planter(sk, bones=HOOVES, clips={"graze", "listen", "warn", "walk", "strut", "search", "squareUp", "snag", "ram", "pin",
                                                    "trainPass", "hit"}, lowest=0.0)


def plant(name, frame, pose):
    """Stood: the lowest hoof on the ground. Running (the trot, the charge, the skid): only ever lifted out of it, so the
    gallop's flight stays in the air."""
    if name in ("trot", "charge", "overrun"):
        low = min(p.z for p in rig.pose_points(sk, pose, [(b, w) for b in HOOVES for w in ("head", "tail")]))
        return max(0.0, -low)
    return _planted(name, frame, pose)


rig.bake(sk, CLIPS, plant=plant)
out = rig.args()[0] if rig.args() else "moose.glb"
rig.export(out, kit)
# A distance copy (past look.json's creatureLodMetres). (Not when the recipe runs this to bake over it,
# tools/models/recipes/moose.py: it exports both itself, from the baked mesh.)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
# The brief's numbers (§3), at rest: ~3.4 m to the rack's top, 3.2 m across it (CreatureArtTests pins them).
lo = Vector((1e9, 1e9, 1e9))
hi = -lo
import bpy  # noqa: E402

for o in (o for o in bpy.data.objects if o.type == "MESH" and o.parent is sk.rig):
    for v in o.data.vertices:
        lo = Vector((min(lo.x, v.co.x), min(lo.y, v.co.y), min(lo.z, v.co.z)))
        hi = Vector((max(hi.x, v.co.x), max(hi.y, v.co.y), max(hi.z, v.co.z)))
print(f"[dt] moose: {hi.x - lo.x:.2f} m across (brief {RACK_SPAN}), {hi.z:.2f} m to the rack's top (brief {RACK_TOP}), "
      f"{hi.y - lo.y:.2f} m nose to rump; clips {[c.name + ':' + str(c.length) for c in CLIPS]}")
