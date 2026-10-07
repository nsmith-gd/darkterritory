"""THE MOOSE (GDD §21 outside; docs/design/creatures/moose.md §3; ARCHITECTURE §8 note 339): a corrupted bull moose.

"The strongest monsters are the ones where you can still tell what they used to be" (§26.5): anyone who has seen a moose
knows this one at once, then sees what's wrong with it. The silhouette is the RACK: never shed, grown for years,
mineralized grey-black like slag (contamination, not magic: no glow), 3.2 m across, the widest thing in the roster, so
heavy the head hangs low and the neck has swollen into a hump to carry it. Bloody strips of velvet hang off the tines,
and the junk it has run through is caught in it: fence wire, a telegraph insulator, a smashed lamp, a crew cap, a
splintered door frame. The hide is a "ghost moose's": grey and hairless, crusted with clusters of engorged pale tick sacs,
grape to plum, a ridge of them down the spine that stands up when it warns. Oversized torn ears (its meter: forward
when it's listening, pinned flat when it's coming), a swollen bell, legs too long, knobbed knees, split black hooves.

2.4 m at the shoulder, 3.4 m to the top of the rack, 3.2 m across it; head forward (-Z in the engine). SK_Moose.
A large monster (GDD §27: 8,000-16,000 triangles). Material families: hide (flesh), sac, mineral rack (slag), velvet.
Clips (§31: unnervingly still, then abrupt): graze, listen, warn, walk, trot, squareUp, charge, overrun, snag, search,
ram, pin, strut, trainPass (loops but squareUp, overrun and hit).

    blender -b --python tools/blender/moose.py -- content/art/models/moose.glb
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/blender/moose.py -- out.glb)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over, smoothstep  # noqa: E402

rig.reset()

# Where the rack's tines reach (the brief's numbers, docs/design/creatures/moose.md §3): printed at the end. (2.4 m at the
# shoulder: the trunk's sections, TRUNK.)
RACK_TOP, RACK_SPAN = 3.4, 3.2


def skeleton():
    """SK_Moose: root, pelvis, spine_01..02, chest, neck_01..02, head, jaw, ears, the bell (two), the rack (a bone a side,
    rigid on the skull, so its weight reads as its own in `dt art clearance`), a velvet bone a side (the strips swing), the
    sac ridge (three bones along the spine, lying back: they stand up when it warns), a tail, the front legs (scapula,
    upperarm, lowerarm, hand, finger) and the rear (thigh, calf, foot, toe)."""
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.3, 0)),
        Bone("pelvis", "root", (0, -0.85, 1.95), (0, -0.5, 2.0)),
        Bone("spine_01", "pelvis", (0, -0.5, 2.0), (0, 0.0, 2.08)),
        Bone("spine_02", "spine_01", (0, 0.0, 2.08), (0, 0.42, 2.2)),
        Bone("chest", "spine_02", (0, 0.42, 2.2), (0, 0.72, 2.3)),
        Bone("neck_01", "chest", (0, 0.72, 2.3), (0, 1.02, 2.27)),
        Bone("neck_02", "neck_01", (0, 1.02, 2.27), (0, 1.32, 2.14)),
        Bone("head", "neck_02", (0, 1.32, 2.14), (0, 1.97, 1.47)),
        Bone("jaw", "head", (0, 1.5, 1.88), (0, 1.9, 1.42)),
        Bone("bell_01", "neck_02", (0, 1.28, 1.86), (0, 1.31, 1.6)),
        Bone("bell_02", "bell_01", (0, 1.31, 1.6), (0, 1.34, 1.28)),
        Bone("ridge_01", "spine_01", (0, -0.15, 2.18), (0, -0.62, 2.09)),
        Bone("ridge_02", "spine_02", (0, 0.35, 2.37), (0, -0.15, 2.18)),
        Bone("ridge_03", "chest", (0, 0.95, 2.72), (0, 0.4, 2.39)),
        Bone("tail_01", "pelvis", (0, -1.08, 1.98), (0, -1.16, 1.78)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"ear_{side}", "head", (sx * 0.11, 1.38, 2.3), (sx * 0.46, 1.34, 2.42)),
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

# The hide: grey and hairless, the veined flesh texture greyed and paled (a winter-tick "ghost moose", §3). Pale enough to
# read in the headlamp at distance in fog (Part Eleven Q6).
HIDE = Mat("flesh.moose", hexc("#8e8e8e"), shine=0.3, tint=(0.98, 1.06, 1.16))
HIDE_DARK = Mat("flesh.moose_dark", hexc("#5c5c5e"), shine=0.3, tint=(0.62, 0.66, 0.74))
# The sacs: engorged, pale, stretched shiny.
# (The skin texture, not the sac's: a sac is a few centimetres, and the sac texture's dark seams are tiled at 0.5 m.)
SAC = Mat("skin.moose_sac", hexc("#d6cebe"), shine=0.6, tint=(1.75, 1.72, 1.7))
SAC_DARK = Mat("skin.moose_sac_old", hexc("#a49a8e"), shine=0.5, tint=(1.3, 1.22, 1.18))
# The rack: slag, grey-black, dull (no glow, no purple: contamination, not magic).
RACK = Mat("slag.moose_rack", hexc("#3e3c3a"), shine=0.22, tint=(0.8, 0.79, 0.77))
RACK_CRUST = Mat("slag.moose_crust", hexc("#504c48"), shine=0.15, tint=(0.95, 0.92, 0.88))
VELVET = Mat("flesh.moose_velvet", hexc("#4a1612"), shine=0.45, tint=(0.62, 0.2, 0.17))
HOOF = Mat("tar.moose_hoof", hexc("#141110"), shine=0.3)
EYE = Mat("glass_dirty.moose_eye", hexc("#1c1a18"), shine=0.8, tint=(0.35, 0.33, 0.3))
MOUTH = Mat("tar.moose_mouth", hexc("#1e0e0c"), shine=0.4)
# What's caught in the rack (environmental story): rusted fence wire, a green glass telegraph insulator, a smashed lamp,
# a crew cap, a splintered door frame.
WIRE = Mat("rust_heavy.moose_wire", hexc("#5a3a26"), shine=0.3)
INSULATOR = Mat("glass_dirty.moose_insulator", hexc("#5f8a74"), shine=0.7, tint=(0.75, 1.15, 0.95))
LAMP = Mat("paint_black.moose_lamp", hexc("#22201e"), shine=0.3)
LAMP_GLASS = Mat("glass_dirty.moose_lamp", hexc("#8a8670"), shine=0.8)
CAP = Mat("wool.moose_cap", hexc("#20242c"), shine=0.06, tint=(0.4, 0.44, 0.52))
WOOD = Mat("wood_grey.moose_frame", hexc("#6a645a"), shine=0.05)

body = kit.part("body")

# --- weights -------------------------------------------------------------------------------------------------
SPINE = along("y", [(-1.12, "pelvis"), (-0.75, "pelvis"), (-0.4, "spine_01"), (0.05, "spine_02"), (0.45, "chest"),
                    (0.72, "chest"), (0.98, "neck_01"), (1.22, "neck_02"), (1.34, "neck_02")])


def trunk(p):
    w = SPINE(p)
    # The haunches go with the thighs, the shoulders' undersides with the scapulas (the long legs come out of them).
    if p.y < -0.5 and p.z < 1.9 and abs(p.x) > 0.12:
        k = smoothstep(1.9, 1.55, p.z) * smoothstep(-0.5, -0.7, p.y) * 0.7
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    if 0.45 < p.y < 1.0 and p.z < 1.95 and abs(p.x) > 0.1:
        k = smoothstep(1.95, 1.5, p.z) * 0.6
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"scapula_{s}"] = w.get(f"scapula_{s}", 0) + k
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def finer(secs, n=2):
    out = []
    for a, b in zip(secs, secs[1:]):
        for k in range(n):
            t = k / n
            out.append(tuple(x + (y - x) * t for x, y in zip(a, b)))
    out.append(secs[-1])
    return out


# --- the trunk: rump, barrel, the shoulders, and the neck swollen into a hump (side and top views) ------------
# (y, half width, top, bottom, squareness)
TRUNK = [(-1.14, 0.1, 1.96, 1.78, 1.0), (-1.08, 0.27, 2.02, 1.58, 0.85), (-0.94, 0.38, 2.05, 1.45, 0.75),
         (-0.74, 0.42, 2.08, 1.38, 0.7), (-0.5, 0.42, 2.11, 1.33, 0.72), (-0.24, 0.47, 2.17, 1.25, 0.72),
         (0.04, 0.5, 2.25, 1.2, 0.72), (0.3, 0.48, 2.36, 1.19, 0.72), (0.52, 0.43, 2.47, 1.23, 0.76),
         (0.7, 0.38, 2.6, 1.33, 0.8), (0.86, 0.34, 2.74, 1.44, 0.85), (1.0, 0.3, 2.72, 1.55, 0.85),
         (1.14, 0.25, 2.6, 1.68, 0.85), (1.28, 0.2, 2.46, 1.8, 0.9), (1.36, 0.15, 2.34, 1.87, 0.95)]


def hide(i, j, a, p):
    """Hairless and stretched: the spine a ridge, the hip bones and the shoulder blades standing out, the ribs in bars, lumps
    under the skin where the sacs root."""
    q = Vector(p)
    if abs(math.sin(a)) < 0.25 and math.cos(a) > 0.8:
        q.z += 0.02
    # Hip points (tuber coxae) and the shoulder blades' spines.
    for y0, z0, k in ((-0.72, 2.0, 0.07), (0.5, 2.2, 0.04)):
        d = math.hypot(q.y - y0, q.z - z0)
        if abs(q.x) > 0.15:
            q.x *= 1 + k * max(0.0, 1 - d / 0.22)
    if -0.3 < q.y < 0.35 and abs(math.sin(a)) > 0.6 and q.z < 2.05:
        q.x *= 1 + 0.035 * max(0.0, math.sin(q.y * 34))
    q.x *= 1 + 0.05 * noise3(q, 11, 6.0)
    q.z += 0.02 * noise3(q, 12, 7.0)
    return q


def hide_mat(pts, n):
    """Darker on the belly and the inside of the legs, where the sacs crowd thickest."""
    c = sum(pts, Vector()) / len(pts)
    return HIDE_DARK if c.z < 1.42 or (n.z < -0.6) else HIDE


body.sections(finer(TRUNK, 3), 28, HIDE, trunk, cap0=True, shape=hide, fmat=hide_mat)

# --- the head: long and deep, Roman-nosed, the bulb of the muzzle drooping over the lip; hanging low under the rack -----
# Sections along the head's own length (u from the poll, m), in its frame: (u, half width, top, bottom, squareness) with
# top and bottom over and under its axis, the way `sections` boxes out a trunk.
POLL = Vector((0, 1.3, 2.22))
ALONG = (Vector((0, 1.97, 1.47)) - Vector((0, 1.32, 2.14))).normalized()
OVER = Vector((0, -ALONG.z, ALONG.y))  # up out of the brow, square to its length
HEAD = [(-0.04, 0.12, 0.12, -0.14, 0.9), (0.04, 0.15, 0.15, -0.2, 0.8), (0.14, 0.16, 0.15, -0.23, 0.8), (0.24, 0.14, 0.12, -0.2, 0.8),
        (0.36, 0.115, 0.095, -0.15, 0.8), (0.48, 0.105, 0.095, -0.13, 0.8), (0.58, 0.125, 0.125, -0.13, 0.75),
        (0.68, 0.14, 0.13, -0.16, 0.75), (0.76, 0.125, 0.09, -0.19, 0.8), (0.81, 0.06, 0.02, -0.15, 0.9)]


def head_at(u, x, v):
    """A point in the head's frame: u along it from the poll, x across, v over its axis."""
    return POLL + ALONG * u + Vector((x, 0, 0)) + OVER * v


def head_sections(secs, sides, mat, bones, shape=None, cap1=False):
    rings, cents = [], []
    for (u, hw, top, bot, e) in secs:
        vc, hv = (top + bot) / 2, (top - bot) / 2
        ring = []
        for j in range(sides):
            ang = 2 * math.pi * j / sides
            sa, ca = math.sin(ang), math.cos(ang)
            p = head_at(u, math.copysign(abs(sa) ** e, sa) * hw, vc + math.copysign(abs(ca) ** e, ca) * hv)
            ring.append(Vector(shape(len(rings), j, ang, p)) if shape else p)
        rings.append(ring)
        cents.append(head_at(u, 0, vc))
    return body.loft(rings, mat, bones, centres=cents, cap0=True, cap1=cap1)


def skull(i, j, a, p):
    # Brow ridges over sunk eyes, lumps where the sacs root.
    return p + OVER * (0.01 * noise3(p, 21, 16.0))


head_sections(finer(HEAD, 2), 16, HIDE, "head", shape=skull, cap1="point")
# The lower jaw: its own piece under the deep cheeks, so the chewing and the grunts show.
head_sections([(0.12, 0.1, -0.12, -0.235, 0.85), (0.3, 0.09, -0.1, -0.19, 0.85), (0.5, 0.075, -0.1, -0.16, 0.85), (0.66, 0.065, -0.1, -0.16, 0.85),
               (0.74, 0.04, -0.11, -0.15, 0.9)], 10, HIDE, "jaw", cap1="point")
body.box(tuple(head_at(0.68, 0, -0.145)), (0.07, 0.05, 0.012), MOUTH, "jaw", rot=rig.Matrix.Rotation(-0.8, 4, "X"))
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    # Eyes: small and dark, wet, set back on the sides of the skull (the lamp catches them).
    body.blob(tuple(head_at(0.2, sx * 0.135, 0.06)), (0.022, 0.032, 0.022), 6, 3, EYE, "head")
    # Nostrils: dark slits on the bulb.
    body.box(tuple(head_at(0.78, sx * 0.06, -0.02)), (0.016, 0.04, 0.012), MOUTH, "head", rot=rig.Matrix.Rotation(-0.8, 4, "X"))
    # The ears: oversized, torn, a ragged notch out of the back edge and the tip split.
    body.slab([(sx * 0.1, 1.33, 2.28), (sx * 0.22, 1.27, 2.33), (sx * 0.3, 1.29, 2.37), (sx * 0.34, 1.26, 2.38),
               (sx * 0.48, 1.31, 2.44), (sx * 0.5, 1.35, 2.45), (sx * 0.44, 1.41, 2.43), (sx * 0.3, 1.45, 2.38),
               (sx * 0.12, 1.43, 2.31)], 0.022, HIDE, f"ear_{s}", down=(0, 0.1, -1))

# --- the bell: swollen into a long sac that swings when it walks ----------------------------------------------
BELL = [(0, 1.27, 1.9), (0, 1.29, 1.76), (0, 1.31, 1.62), (0, 1.33, 1.5), (0, 1.34, 1.4), (0, 1.34, 1.32), (0, 1.34, 1.28)]
bell_w = along("z", [(1.3, "bell_02"), (1.55, "bell_02"), (1.65, "bell_01"), (1.82, "bell_01"), (1.92, "neck_02")])
# (A flap, narrow side to side, with a bulb at its foot.)
body.tube(BELL, [(0.05, 0.1), (0.04, 0.09), (0.045, 0.085), (0.075, 0.1), (0.085, 0.1), (0.06, 0.07), 0.02], 10, HIDE_DARK, bell_w,
          ref=(0, 1, 0), cap1="point", shape=lambda i, j, a, p, fr: p + fr[0] * (0.01 * noise3(p, 31, 20.0)))

# --- legs: too long, knobbed at the knees and the fetlocks, the hooves split and black --------------------------
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * 0.28
    front = along("z", [(0.04, "finger_{s}"), (0.14, "hand_{s}"), (0.2, "hand_{s}"), (0.6, "hand_{s}"), (0.72, "lowerarm_{s}"),
                        (1.2, "lowerarm_{s}"), (1.36, "upperarm_{s}"), (1.58, "upperarm_{s}"), (1.8, "scapula_{s}")])
    fw = lambda p, f=front: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x * 0.95, 0.76, 1.98), (x, 0.84, 1.62), (x, 0.76, 1.32), (x, 0.74, 1.02), (x, 0.76, 0.72), (x, 0.765, 0.64),
               (x, 0.77, 0.52), (x, 0.79, 0.28), (x, 0.8, 0.17), (x, 0.83, 0.08)],
              [(0.17, 0.24), 0.155, 0.12, 0.085, 0.085, 0.088, 0.058, 0.05, 0.063, 0.055], 10, HIDE, fw, ref=(0, 1, 0))
    rear = along("z", [(0.04, "toe_{s}"), (0.14, "foot_{s}"), (0.2, "foot_{s}"), (0.72, "foot_{s}"), (0.88, "calf_{s}"),
                       (1.3, "calf_{s}"), (1.48, "thigh_{s}"), (1.9, "thigh_{s}")])
    rw = lambda p, f=rear: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    xr = sx * 0.27
    body.tube([(xr * 0.9, -0.74, 1.98), (xr, -0.56, 1.5), (xr, -0.7, 1.16), (xr, -0.9, 0.86), (xr, -0.93, 0.78), (xr, -0.91, 0.66),
               (xr, -0.88, 0.32), (xr, -0.86, 0.17), (xr, -0.83, 0.08)],
              [(0.2, 0.27), 0.18, 0.125, 0.085, 0.09, 0.062, 0.052, 0.063, 0.055], 10, HIDE, rw, ref=(0, 1, 0))
    # Hooves: split in two, black, long in the toe; the dewclaws behind.
    for y0, bone, root in ((0.86, f"finger_{s}", f"hand_{s}"), (-0.78, f"toe_{s}", f"foot_{s}")):
        xx = x if y0 > 0 else xr
        for k in (-1, 1):
            body.box((xx + k * 0.032, y0 + 0.01, 0.045), (0.028, 0.075, 0.045), HOOF, {bone: 0.8, root: 0.2}, taper=(0.75, 0.6),
                     rot=rig.Matrix.Rotation(k * 0.08, 4, "Z"))
        for k in (-1, 1):
            body.box((xx + k * 0.03, y0 - 0.1, 0.12), (0.012, 0.02, 0.02), HOOF, root)

# --- tail: a stub ---------------------------------------------------------------------------------------------
body.tube([(0, -1.1, 1.98), (0, -1.14, 1.88), (0, -1.16, 1.78)], [0.05, 0.045, 0.02], 6, HIDE, "tail_01", ref=(0, 0, 1), cap1="point")

# --- the sacs: engorged ticks, grape to plum, in clusters over the grey hide --------------------------------------
sacs = kit.part("sacs")


def section_at(y):
    """The trunk's section at y (interpolated): (half width, top, bottom, squareness)."""
    for (y0, *a), (y1, *b) in zip(TRUNK, TRUNK[1:]):
        if y0 <= y <= y1:
            t = (y - y0) / (y1 - y0)
            return tuple(u + (v - u) * t for u, v in zip(a, b))
    return tuple(TRUNK[0][1:]) if y < TRUNK[0][0] else tuple(TRUNK[-1][1:])


def surface(y, a):
    """A point on the trunk's skin at y, `a` round from the top (as `sections` has it), and its way out."""
    hw, top, bot, e = section_at(y)
    zc, hz = (top + bot) / 2, (top - bot) / 2
    sa, ca = math.sin(a), math.cos(a)
    p = Vector((math.copysign(abs(sa) ** e, sa) * hw, y, zc + math.copysign(abs(ca) ** e, ca) * hz))
    n = Vector((sa / max(hw, 1e-3), 0, ca / max(hz, 1e-3))).normalized()
    return p, n


def h01(*k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, 0.53)), 77, 1.0)


def sac(c, r, bones, mat=SAC):
    """One sac: a lumpy, slightly drooping ball."""
    sacs.blob(tuple(c), (r, r * 0.95, r * 0.9), 6, 3, mat, bones, smooth=True,
              shape=lambda i, j, a, th, p: p + (p - c) * 0.12 * noise3(p, 81, 60.0) - Vector((0, 0, 0.15 * r * max(0.0, (c - p).z / r))))


# Clusters, packed like bunches of grapes: thickest on the neck and the hump, the shoulders, the flanks behind the ribs and
# the rump; few on the belly. (y, round from the top, how many.)
CLUSTERS = [(-1.0, 1.05, 9), (-0.86, 1.85, 7), (-0.7, 1.4, 11), (-0.55, 0.7, 6), (-0.3, 1.9, 8), (-0.18, 1.2, 10), (0.08, 1.6, 7),
            (0.3, 0.95, 9), (0.5, 1.55, 11), (0.68, 0.75, 8), (0.82, 1.25, 12), (0.98, 0.6, 9), (1.08, 1.45, 10), (1.24, 0.95, 8),
            (-0.75, 2.3, 5), (0.2, 2.2, 5)]
GOLDEN = math.pi * (3 - math.sqrt(5))
for side in (1, -1):
    for k, (y, a, n) in enumerate(CLUSTERS):
        # (Not mirror images: each side its own.)
        y = y + 0.1 * (h01(k, side) - 0.5)
        a = a + 0.3 * (h01(k + 40, side) - 0.5)
        n = max(4, n + int(4 * (h01(k + 80, side) - 0.5)))
        hw = section_at(y)[0]
        for i in range(n):
            # Packed out from the middle (the biggest there, plum-sized; grapes round the edge).
            d = 0.032 * math.sqrt(i + 0.3)
            ang = i * GOLDEN + 2.0 * h01(k, side)
            p, nn = surface(y + d * math.cos(ang), side * (a + d * math.sin(ang) / max(hw, 0.15)))
            r = 0.016 + 0.03 * (1 - i / n) * (0.6 + 0.4 * h01(k * 3 + i, side + 9))
            sac(p + nn * r * 0.5, r, trunk, SAC if h01(i, k + side) > 0.3 else SAC_DARK)
# The ridge down the spine: two staggered rows, lying back along it (they stand up when it warns: the ridge bones).
RIDGE = [(-0.6, "ridge_01"), (-0.15, "ridge_01"), (-0.14, "ridge_02"), (0.34, "ridge_02"), (0.36, "ridge_03"), (0.95, "ridge_03")]
for row in range(2):
    n = 15
    for i in range(n):
        y = -0.58 + 1.5 * (i + 0.5 * row) / n
        bone = next(b for (y0, b), (y1, _) in zip(RIDGE[::2], RIDGE[1::2]) if y0 <= y <= y1 + 0.02) if y <= 0.95 else "ridge_03"
        p, nn = surface(y, (row - 0.5) * 0.22)
        r = 0.045 + 0.03 * h01(i, row + 20)
        sac(p + Vector((0, 0, r * 0.7)), r, bone, SAC)
        if row == 0 and i % 2 == 0:
            # Heaped: a second sac on the first, so the ridge stands proud of the back even lying down.
            sac(p + Vector((0.01, 0.02, r * 1.9)), r * 0.75, bone, SAC_DARK)
# Some on the cheeks and the throat, and down the tops of the legs.
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    for i, (y, z, r) in enumerate([(1.55, 2.02, 0.03), (1.6, 1.94, 0.025), (1.5, 1.95, 0.035), (1.68, 1.86, 0.022)]):
        sac(Vector((sx * 0.12, y, z)), r, "head")
    for i, (y, z, r) in enumerate([(0.82, 1.5, 0.04), (0.8, 1.38, 0.03), (0.78, 1.2, 0.028), (0.88, 1.44, 0.03)]):
        sac(Vector((sx * (0.36 + 0.1 * (z - 1.2)), y, z)), r, f"upperarm_{s}")
    for i, (y, z, r) in enumerate([(-0.6, 1.5, 0.045), (-0.66, 1.38, 0.035), (-0.7, 1.24, 0.03), (-0.52, 1.6, 0.04)]):
        sac(Vector((sx * 0.4, y, z)), r, f"thigh_{s}")

# --- the rack: grown for years, never shed, mineral slag; 3.2 m across --------------------------------------------
rack = kit.part("rack", smooth=False)


def palm_point(sx, u, v):
    """The palm's top surface: u from the beam (0) out to the rim (1), v from its back edge (0) to its front (1). It fans out
    wider to the rim, sweeps up and back, and cups; its corners are rounded off and its rim notched between the tines (a
    palm, not a plate)."""
    c = 2 * v - 1
    w = (0.15 + 0.37 * u) * (1 - 0.12 * u * c * c)
    u = u * (1 - 0.2 * u ** 3 * c ** 4) * (1 - 0.07 * u ** 4 * (0.5 - 0.5 * math.cos(14 * math.pi * v)))
    x = sx * (0.46 + 1.02 * u)
    y = 1.44 + (v - 0.5) * 2 * w + 0.06 * u
    z = 2.38 + 0.46 * u + 0.12 * u * u - 0.11 * u * math.sin(math.pi * v) + 0.18 * (1 - v) * u
    p = Vector((x, y, z))
    p.z += 0.025 * noise3(p, 91, 7.0) * u
    return p


def palm(sx, bone):
    nu, nv = 7, 8
    top = [[palm_point(sx, i / nu, j / nv) for j in range(nv + 1)] for i in range(nu + 1)]
    thick = [[0.085 - 0.04 * (i / nu)] * (nv + 1) for i in range(nu + 1)]
    it = [[rack.add_v(p, bone) for p in row] for row in top]
    ib = [[rack.add_v(top[i][j] - Vector((0, 0, thick[i][j])), bone) for j in range(nv + 1)] for i in range(nu + 1)]
    inside = palm_point(sx, 0.5, 0.5) - Vector((0, 0, 0.03))
    tile = RACK.tile() or 1.0

    def uv(p):
        return (p.x / tile, p.y / tile)
    for i in range(nu):
        for j in range(nv):
            q = [it[i][j], it[i + 1][j], it[i + 1][j + 1], it[i][j + 1]]
            rack.face(q, [uv(rack.v[k]) for k in q], RACK, False, outward=inside)
            q = [ib[i][j], ib[i][j + 1], ib[i + 1][j + 1], ib[i + 1][j]]
            rack.face(q, [uv(rack.v[k]) for k in q], RACK, False, outward=inside)
    # Round the edge.
    ring = [(i, 0) for i in range(nu + 1)] + [(nu, j) for j in range(1, nv + 1)] + [(i, nv) for i in range(nu - 1, -1, -1)] + \
           [(0, j) for j in range(nv - 1, 0, -1)]
    for (a, b), (c, d) in zip(ring, ring[1:] + ring[:1]):
        q = [it[a][b], it[c][d], ib[c][d], ib[a][b]]
        rack.face(q, [uv(rack.v[k]) for k in q], RACK, False, outward=inside)


def tine(base, tip, r0, bone, sides=6):
    base, tip = Vector(base), Vector(tip)
    mid = base + (tip - base) * 0.55 + Vector((0, 0, 0.02))
    rack.tube([base, mid, tip], [r0, r0 * 0.6, 0.004], sides, RACK, bone, ref=(0, 1, 0), cap1="point",
              shape=lambda i, j, a, p, fr: p + fr[0] * (0.012 * noise3(p, 92, 25.0)))


TIPS = {}
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    bone = f"rack_{s}"
    # The burr and the beam: knotted where it comes out of the skull, as thick as a man's arm.
    rack.rock((sx * 0.15, 1.46, 2.26), (0.085, 0.085, 0.07), RACK_CRUST, bone, seed=500 + (sx > 0), spike=1.1)
    rack.tube([(sx * 0.13, 1.46, 2.24), (sx * 0.3, 1.46, 2.3), (sx * 0.5, 1.44, 2.38)], [0.075, 0.07, 0.065], 8, RACK, bone, ref=(0, 1, 0),
              shape=lambda i, j, a, p, fr: p + fr[0] * (0.012 * noise3(p, 93, 18.0)) + fr[1] * (0.01 * noise3(p, 94, 18.0)))
    palm(sx, bone)
    TIPS[s] = []
    # Tines round the rim: out, up and a little back, the tallest at the back (3.4 m), fanning forward.
    for k in range(8):
        v = k / 7
        base = palm_point(sx, 1.0, v) - Vector((0, 0, 0.03))
        out = Vector((sx * (0.34 - 0.16 * v), (v - 0.55) * 0.5, 0.95 - 0.35 * v)).normalized()
        length = 0.34 - 0.1 * v + 0.05 * (k % 2)
        tip = base + out * length
        tine(base, tip, 0.045 - 0.008 * v, bone)
        TIPS[s].append(tip)
    # Two down the back edge, and the brow palm's tines forward over the face.
    for u in (0.55, 0.8):
        base = palm_point(sx, u, 0.0) - Vector((0, 0, 0.02))
        tip = base + Vector((sx * 0.05, -0.26, 0.24))
        tine(base, tip, 0.04, bone)
        TIPS[s].append(tip)
    for u, l in ((0.12, 0.3), (0.3, 0.36)):
        base = palm_point(sx, u, 1.0) - Vector((0, 0, 0.02))
        tip = base + Vector((sx * 0.08, 0.32 * l / 0.3, 0.05))
        tine(base, tip, 0.038, bone)
        TIPS[s].append(tip)
    # Slag crusted on the palms: lumps, knots, a scab of it on the beam.
    for k in range(9):
        u, v = 0.15 + 0.75 * h01(k, sx + 50), 0.1 + 0.8 * h01(k + 9, sx + 51)
        p = palm_point(sx, u, v)
        rack.rock(tuple(p + Vector((0, 0, 0.012))), (0.05 + 0.04 * h01(k, 52), 0.05 + 0.03 * h01(k, 53), 0.03 + 0.02 * h01(k, 54)),
                  RACK_CRUST, bone, seed=520 + k + (20 if sx > 0 else 0), sides=5, rings=3, spike=1.2)

# --- velvet: bloody strips hanging off the tines and the palms' edges ------------------------------------------------
velvet = kit.part("velvet")
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    hangs = [palm_point(sx, 1.0, v) - Vector((0, 0, 0.06)) for v in (0.1, 0.35, 0.62, 0.88)] + \
            [palm_point(sx, u, 0.0) - Vector((0, 0, 0.05)) for u in (0.4, 0.75)] + \
            [palm_point(sx, u, 1.0) - Vector((0, 0, 0.05)) for u in (0.5, 0.8)] + [(TIPS[s][1] + TIPS[s][0]) * 0.5]
    for k, top in enumerate(hangs):
        length = 0.22 + 0.4 * h01(k, sx + 60)
        swing = Vector((0.04 * (h01(k, 61) - 0.5), 0.05 * (h01(k, 62) - 0.5), 0))
        pts = [top + swing * (i / 3) * (i / 3) - Vector((0, 0, length * i / 3)) for i in range(4)]
        # Its top on the rack, the rest of it swinging on the velvet bone.
        def strip(p, z0=top.z, length=length, s=s):
            down = min(1.0, (z0 - p.z) / length * 1.6)
            return {f"rack_{s}": 1.0} if down <= 0 else {f"rack_{s}": 1 - down, f"velvet_{s}": down} if down < 1 else {f"velvet_{s}": 1.0}
        velvet.tube(pts, [(0.035, 0.007), (0.03, 0.006), (0.022, 0.005), (0.008, 0.003)], 4, VELVET, strip, ref=(0, 1, 0),
                    twist=0.6 * h01(k, 63), cap1="point")

# --- what it has run through, caught in the rack ------------------------------------------------------------------
junk = kit.part("junk", smooth=False)
# Fence wire: a strand of rusted barbed wire wound round the right palm's tines and hanging in a loop under it.
wire = [TIPS["r"][1] - Vector((0, 0, 0.12)), TIPS["r"][3] - Vector((0, 0, 0.18)), palm_point(1, 0.85, 0.55) - Vector((0, 0, 0.3)),
        palm_point(1, 0.6, 0.4) - Vector((0, 0, 0.42)), palm_point(1, 0.45, 0.2) - Vector((0, 0, 0.22)), TIPS["r"][8] - Vector((0, 0, 0.1))]
junk.tube(wire, [0.008] * len(wire), 4, WIRE, "rack_r", ref=(0, 0, 1))
for k in range(1, len(wire) - 1):
    c = wire[k]
    junk.box(tuple(c), (0.03, 0.004, 0.004), WIRE, "rack_r", rot=rig.Matrix.Rotation(0.6 * k, 4, "Z"))
# A telegraph insulator, green glass, still on its wire.
ins = palm_point(1, 0.6, 0.4) - Vector((0, 0, 0.48))
junk.tube([ins + Vector((0, 0, 0.06)), ins + Vector((0, 0, 0.0)), ins - Vector((0, 0, 0.05))], [0.03, 0.05, 0.048], 8, INSULATOR,
          "rack_r", ref=(0, 1, 0), cap0=True, cap1=True)
# A smashed lamp hooked on the right brow tine.
lamp = TIPS["r"][-1] - Vector((0.02, 0.06, 0.14))
junk.box(tuple(lamp), (0.055, 0.055, 0.075), LAMP, "rack_r", rot=rig.Matrix.Rotation(0.4, 4, "Y"))
junk.box(tuple(lamp + Vector((0, 0.058, 0.0))), (0.04, 0.004, 0.05), LAMP_GLASS, "rack_r", rot=rig.Matrix.Rotation(0.4, 4, "Y"))
junk.tube([lamp + Vector((0, 0, 0.075)), lamp + Vector((0.03, 0.04, 0.13))], [0.006, 0.006], 4, LAMP, "rack_r", ref=(0, 1, 0))
# A crew cap snagged on a left rim tine.
cap = TIPS["l"][4] - Vector((-0.05, 0, 0.16))
junk.blob(tuple(cap), (0.1, 0.11, 0.045), 9, 3, CAP, "rack_l", rot=rig.Matrix.Rotation(0.5, 4, "Y"))
junk.slab([cap + Vector((-0.07, 0.08, -0.03)), cap + Vector((0.07, 0.08, -0.03)), cap + Vector((0.05, 0.16, -0.05)),
           cap + Vector((-0.05, 0.16, -0.05))], 0.01, CAP, "rack_l")
# A splintered door frame jammed across the left palm: two planks and a shard, sticking out past the tines.
for k, (u0, v0, u1, v1, dz) in enumerate([(0.15, 0.75, 1.15, 0.45, 0.06), (0.35, 0.2, 1.05, 0.95, 0.1)]):
    a, b = palm_point(-1, u0, v0) + Vector((0, 0, dz)), palm_point(-1, min(u1, 1.0), v1) + Vector((0, 0, dz))
    if u1 > 1.0:
        b = a + (b - a) * u1 / 1.0
    junk.tube([a, b], [(0.045, 0.016), (0.04, 0.014)], 4, WOOD, "rack_l", ref=(0, 0, 1), cap0=True, cap1=True, twist=math.pi / 4)
junk.tube([palm_point(-1, 0.9, 0.5) + Vector((0, 0, 0.1)), palm_point(-1, 0.9, 0.5) + Vector((-0.18, 0.12, 0.34))],
          [(0.025, 0.01), 0.003], 4, WOOD, "rack_l", ref=(0, 0, 1), cap1="point")

# --------------------------------------------------------------------------------------------------------------
# Clips. Angles are the armature's axes (rig.rot): +X tips a forward-pointing bone's end up (a head raised) and swings a
# hanging leg forward; the ridge's bones lie back along the spine, so -X stands them up; +Z turns towards -X (the left).


def curve(keys, t):
    t %= 1.0
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / max(t1 - t0, 1e-9)
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
    tip = p.get("neck_01", (0, 0, 0))[0] + p.get("neck_02", (0, 0, 0))[0] + p.get("chest", (0, 0, 0))[0] + p.get("spine_02", (0, 0, 0))[0]
    out = dict(p)
    # (Tucked, the head would sweep into it: it swings back out of the way under the throat.)
    out["bell_01"] = (-tip * 0.55 + 0.8 * min(0.0, p.get("head", (0, 0, 0))[0]) + swing, 0, 0)
    out["bell_02"] = (swing * 0.6, 0, 0)
    return out


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
GRAZE = over(STAND, root__loc=(0, 0, -0.04), spine_02=(-3, 0, 0), chest=(-5, 0, 0), neck_01=(-30, 0, 0), neck_02=(-25, 0, 0),
             head=(26, 0, 0), tail_01=(4, 0, 0))
GRAZE.update(mirror({"upperarm_r": (10, -5, 0), "lowerarm_r": (-6, 0, 0), "hand_r": (-4, 0, 0), "thigh_r": (-4, 0, 0)}))
graze = Clip("graze")
# (frame, chew 0/1, ear twitch r, ear twitch l, sac pulse) - 4 s: chewing, the ears never still, the sacs slowly pulsing.
for f in range(0, 120, 6):
    k = f / 120
    chew = 1 if (f // 6) % 2 else 0
    er = 30 * math.sin(2 * math.pi * k * 3) if (f // 24) % 2 == 0 else -20
    el = -25 * math.sin(2 * math.pi * k * 2 + 1) if (f // 30) % 2 == 1 else 15
    pulse = 1.0 + 0.06 * math.sin(2 * math.pi * k * 2)
    p = over(GRAZE, jaw=(-6 * chew, 0, 2 * chew), head=(26 + 2 * math.sin(2 * math.pi * k), 0, 4 * math.sin(2 * math.pi * k)),
             neck_02=(-25, 0, 3 * math.sin(2 * math.pi * k)),
             ear_r=(0, 0, er), ear_l=(0, 0, -el),
             ridge_01__scale=(pulse, 1, pulse), ridge_02__scale=(2 - pulse, 1, 2 - pulse), ridge_03__scale=(pulse, 1, pulse),
             tail_01=(4 + (12 if f in (48, 54) else 0), 0, 0))
    graze.key(f, bell_hangs(p, 2 * math.sin(2 * math.pi * k)), "CONSTANT" if f % 24 == 0 else "BEZIER")
graze.close(120)

# Listening (aggro 20+): head up, the chewing stopped, the ears forward and turning; dead still otherwise.
LISTEN = over(STAND, spine_02=(1, 0, 0), chest=(2, 0, 0), neck_01=(6, 0, 0), neck_02=(4, 0, 0), head=(4, 0, 0))
LISTEN.update(EARS_FORWARD)
listen = Clip("listen")
for f, (er, el, hz) in zip((0, 14, 30, 44, 60), ((48, 48, 0), (30, 62, 4), (62, 36, -3), (44, 52, 2), (48, 48, 0))):
    p = over(LISTEN, ear_r=(8, -10, er), ear_l=(8, 10, -el), head=(4, 0, hz))
    listen.key(f, bell_hangs(p), "CONSTANT" if f in (14, 44) else "BEZIER")
listen.close(60)

# Warning (aggro 50+): the head comes down and the rack swings toward you, the ears pinned flat, the ridge of sacs stood
# up; a cough-grunt (the jaw), a hoof dragged back through the ground.
WARN = over(STAND, root__loc=(0, 0, -0.03), spine_02=(-2, 0, 0), chest=(-4, 0, 0), neck_01=(-6, 0, 0), neck_02=(-10, 0, 0),
            head=(-12, 0, 0), tail_01=(-14, 0, 0))
WARN.update(EARS_FLAT)
WARN.update(RIDGE_UP)
warn = Clip("warn")
warn.key(0, bell_hangs(WARN))
warn.key(10, bell_hangs(over(WARN, jaw=(-14, 0, 0), head=(-8, 0, 0))), "CONSTANT")
warn.key(14, bell_hangs(WARN), "CONSTANT")
# The drag: the right forehoof lifted forward and raked back.
warn.key(24, bell_hangs(over(WARN, upperarm_r=(24, 0, 0), lowerarm_r=(30, 0, 0), hand_r=(-30, 0, 0))))
warn.key(32, bell_hangs(over(WARN, upperarm_r=(-14, 0, 0), lowerarm_r=(4, 0, 0), hand_r=(-6, 0, 0), head=(-16, 0, 6))), "LINEAR")
warn.key(40, bell_hangs(WARN))
warn.key(52, bell_hangs(over(WARN, jaw=(-14, 0, 0), head=(-8, 0, -4))), "CONSTANT")
warn.key(56, bell_hangs(WARN), "CONSTANT")
warn.close(66)

# Walk (2.5 m/s, a stride every second): a four-beat walk, the long legs swinging slow, the rack nodding with each step.
WALK_FRONT = {"upperarm": [(0, 16), (0.55, -18), (0.68, -12), (0.85, 14), (1, 16)],
              "lowerarm": [(0, 0), (0.55, -2), (0.68, 34), (0.85, 12), (1, 0)],
              "hand": [(0, 2), (0.55, -10), (0.68, -60), (0.85, -6), (1, 2)]}
WALK_REAR = {"thigh": [(0, 16), (0.55, -16), (0.68, -6), (0.85, 20), (1, 16)],
             "calf": [(0, -6), (0.55, 4), (0.68, -34), (0.85, -22), (1, -6)],
             "foot": [(0, 4), (0.55, 4), (0.68, 40), (0.85, 20), (1, 4)]}
walk_legs = legs(WALK_FRONT, WALK_REAR, {"rear_l": 0.0, "front_l": 0.25, "rear_r": 0.5, "front_r": 0.75})


def gait(name, frames, base, legs_at, stride=1.0, nod=3.0, bob=0.03, sway=3.0, swing=6.0, ears=None, extra=None):
    c = Clip(name)
    for f in range(0, frames, 2 if frames <= 24 else 3):
        t = f / frames
        p = dict(base)
        p.update({k: (v[0] * stride, v[1], v[2]) for k, v in legs_at(t).items()})
        w = math.sin(4 * math.pi * t)
        p["root@loc"] = (0, 0, base.get("root@loc", (0, 0, 0))[2] + bob * w)
        p["spine_02"] = (base.get("spine_02", (0, 0, 0))[0], 0, sway * math.sin(2 * math.pi * t))
        p["chest"] = (base.get("chest", (0, 0, 0))[0], 0, -sway * math.sin(2 * math.pi * t))
        p["head"] = (base.get("head", (0, 0, 0))[0] + nod * w, 0, base.get("head", (0, 0, 0))[2])
        p["velvet_r"] = (swing * math.sin(2 * math.pi * t + 0.6), 0, swing * 0.5 * math.cos(2 * math.pi * t))
        p["velvet_l"] = (swing * math.sin(2 * math.pi * t + 0.9), 0, -swing * 0.5 * math.cos(2 * math.pi * t))
        p["tail_01"] = (base.get("tail_01", (0, 0, 0))[0], 0, 6 * math.sin(2 * math.pi * t))
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
search = gait("search", 72, SEARCH_BODY, legs(WALK_FRONT, WALK_REAR, {"rear_l": 0.0, "front_l": 0.25, "rear_r": 0.5, "front_r": 0.75}),
              stride=0.7, nod=2, sway=2,
              ears=lambda t: {"ear_r": (6, -6, 50 * math.sin(2 * math.pi * t * 2)), "ear_l": (6, 6, -50 * math.sin(2 * math.pi * t * 2 + 1.4))},
              extra=lambda t, p: {"neck_01": (-12, 0, 16 * math.sin(2 * math.pi * t)), "neck_02": (-10, 0, 14 * math.sin(2 * math.pi * t)),
                                  "head": (-6 + 3 * math.sin(4 * math.pi * t), 0, 10 * math.sin(2 * math.pi * t)),
                                  "jaw": (-8 if math.sin(6 * math.pi * t) > 0.7 else 0, 0, 0)})

# Trot (4.5 m/s, a stride every two thirds of a second): diagonal pairs, the head out level, the ears back.
TROT_FRONT = {"upperarm": [(0, 26), (0.45, -26), (0.6, -14), (0.8, 22), (1, 26)],
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
             neck_02=(-10, 0, 0), head=(-26, 0, 0), jaw=(-4, 0, 0), tail_01=(-20, 0, 0))
LEVEL.update(EARS_FLAT)
LEVEL.update(RIDGE_UP)
LEVEL.update(mirror({"upperarm_r": (-6, 0, 0), "lowerarm_r": (6, 0, 0), "hand_r": (-2, 0, 0), "thigh_r": (8, 0, 0), "calf_r": (-12, 0, 0),
                     "foot_r": (8, 0, 0)}))
STAMP_R = over(LEVEL, upperarm_r=(26, 0, 0), lowerarm_r=(40, 0, 0), hand_r=(-44, 0, 0), head=(-30, 0, 0))
STAMP_L = over(LEVEL, upperarm_l=(26, 0, 0), lowerarm_l=(40, 0, 0), hand_l=(-44, 0, 0), head=(-30, 0, 0))
square = Clip("squareUp", loop=False)
square.key(0, bell_hangs(WARN))
square.key(12, bell_hangs(LEVEL))
square.key(22, bell_hangs(STAMP_R), "BEZIER")
square.key(26, bell_hangs(LEVEL), "LINEAR")
square.key(44, bell_hangs(STAMP_L), "BEZIER")
square.key(48, bell_hangs(LEVEL), "LINEAR")
square.key(56, bell_hangs(over(LEVEL, jaw=(-16, 0, 0))), "CONSTANT")
square.key(60, bell_hangs(LEVEL), "CONSTANT")
square.key(75, bell_hangs(over(LEVEL, root__loc=(0, -0.04, -0.1))))

# Charge (11 m/s): a flat-out rotary gallop, the rack level in front, the head low and dead steady on the line, the hump
# heaving, the ridge up, the bell flung about. 16 frames a stride.
GALLOP_FRONT = {"upperarm": [(0, 46), (0.25, 10), (0.5, -40), (0.72, -18), (1, 46)],
                "lowerarm": [(0, 6), (0.25, 0), (0.5, 4), (0.72, 70), (1, 6)],
                "hand": [(0, 8), (0.25, -8), (0.5, -50), (0.72, -86), (1, 8)]}
GALLOP_REAR = {"thigh": [(0, 50), (0.3, 8), (0.55, -40), (0.8, 6), (1, 50)],
               "calf": [(0, -40), (0.3, -10), (0.55, 16), (0.8, -54), (1, -40)],
               "foot": [(0, 40), (0.3, 16), (0.55, 24), (0.8, 64), (1, 40)]}
gallop = legs(GALLOP_FRONT, GALLOP_REAR, {"rear_r": 0.0, "rear_l": 0.08, "front_r": 0.5, "front_l": 0.6})
charge = Clip("charge")
for f in range(0, 16, 2):
    t = f / 16
    p = dict(LEVEL)
    p.update(gallop(t))
    # (The hind legs swung a little wide, outside the forelegs: they reach past them at the gallop.)
    p["thigh_r"], p["thigh_l"] = (p["thigh_r"][0], -7, 0), (p["thigh_l"][0], 7, 0)
    arch = math.sin(2 * math.pi * t)
    p.update({"root@loc": (0, 0, 0.1 * math.sin(2 * math.pi * t + 1.2) - 0.02), "pelvis": (6 * arch, 0, 0), "spine_01": (4 * arch, 0, 0),
              "spine_02": (-3 - 2 * arch, 0, 0), "chest": (-6 - 4 * arch, 0, 0),
              # The head held on the line however the body pitches: the neck takes it up.
              "neck_01": (-16 + 6 * arch, 0, 0), "neck_02": (-10 + 2 * arch, 0, 0), "head": (-34 - 2 * arch, 0, 0),
              "jaw": (-10, 0, 0), "tail_01": (-30, 0, 0),
              "velvet_r": (30 * arch, 0, 10), "velvet_l": (28 * math.sin(2 * math.pi * t + 0.5), 0, -10)})
    charge.key(f, bell_hangs(p, -30 + 14 * arch), "LINEAR")
charge.close(16)

# Overrun (2.5 s, once): the skid, its hind end under it and the forelegs braced, ploughing; then it wheels round on its
# haunches, stamping, head tossing.
SKID = over(LEVEL, root__loc=(0, 0.05, -0.18), pelvis=(-10, 0, 0), spine_01=(4, 0, 0), chest=(6, 0, 0), neck_01=(4, 0, 0), head=(-14, 0, 0))
SKID.update(mirror({"upperarm_r": (-34, 0, 0), "lowerarm_r": (6, 0, 0), "hand_r": (10, 0, 0), "thigh_r": (44, 0, 0), "calf_r": (-46, 0, 0),
                    "foot_r": (40, 0, 0), "ear_r": (0, -12, -42)}))
overrun = Clip("overrun", loop=False)
overrun.key(0, bell_hangs(charge.keys[0][1], -30))
overrun.key(5, bell_hangs(SKID, 8), "LINEAR")
overrun.key(18, bell_hangs(over(SKID, root__loc=(0, 0.02, -0.16)), 20))
# The wheel: up on the haunches, the forehand swung round (the sim turns its heading), the forelegs pawing.
REAR_UP = over(SKID, root__loc=(0, -0.2, 0.05), pelvis=(-20, 0, 0), spine_01=(8, 0, 6), spine_02=(6, 0, 8), chest=(6, 0, 10),
               neck_01=(10, 0, 8), head=(6, 0, 10), jaw=(-16, 0, 0))
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
            neck_02=(-4, 0, 0), head=(-38, 0, 0), jaw=(-18, 0, 0))
SNAG.update(mirror({"upperarm_r": (24, 0, 0), "lowerarm_r": (-2, 0, 0), "hand_r": (-14, 0, 0), "thigh_r": (30, 0, 0), "calf_r": (-30, 0, 0),
                    "foot_r": (28, 0, 0)}))
snag = Clip("snag")
for f, (w, back, interp) in enumerate(((0, 0.0, "CONSTANT"), (14, 0.04, "CONSTANT"), (-16, 0.08, "LINEAR"), (10, 0.03, "CONSTANT"),
                                       (-12, 0.06, "CONSTANT"), (0, 0.02, "LINEAR"))):
    snag.key(f * 6, bell_hangs(over(SNAG, root__loc=(0, -0.14 - back, -0.12), neck_01=(-6, 0, w * 0.4), neck_02=(-4, 0, w * 0.5),
                                    head=(-38, w * 0.8, w * 0.3), spine_02=(0, 0, -w * 0.3), jaw=(-18 - abs(w), 0, 0)), w), interp)
snag.close(36)

# Ram (3 s, loop: one ram every 3 s, enemies.json moose ramEvery): backs a step, drops the rack, drives it into the car's
# side (the boom), shoves, grinds it along the plates, draws back.
ram = Clip("ram")
ram.key(0, bell_hangs(LEVEL))
ram.key(18, bell_hangs(over(LEVEL, root__loc=(0, -0.3, -0.06), head=(-28, 0, 0), neck_01=(-10, 0, 0))))
ram.key(26, bell_hangs(over(LEVEL, root__loc=(0, 0.35, -0.12), head=(-40, 0, 0), neck_01=(-20, 0, 0), chest=(-10, 0, 0),
                            thigh_r=(-20, 0, 0), thigh_l=(-24, 0, 0), calf_r=(10, 0, 0), calf_l=(14, 0, 0)), -20), "LINEAR")
ram.key(28, bell_hangs(over(LEVEL, root__loc=(0, 0.28, -0.12), head=(-36, 0, 0), neck_01=(-18, 0, 0), chest=(-8, 0, 0)), 6), "CONSTANT")
ram.key(40, bell_hangs(over(LEVEL, root__loc=(0, 0.3, -0.12), head=(-38, 8, 10), neck_01=(-18, 0, 6), jaw=(-14, 0, 0)), 10))
ram.key(52, bell_hangs(over(LEVEL, root__loc=(0, 0.3, -0.12), head=(-38, -8, -10), neck_01=(-18, 0, -6)), -10))
ram.key(66, bell_hangs(over(LEVEL, root__loc=(0, 0.0, -0.08))))
ram.close(90)

# Pin (1.2 s, loop; GRAB): someone under the rack, ground into the peat: the head right down, the palms either side of
# them, the forelegs braced wide, shoving, the head grinding side to side.
PIN = over(STAND, root=(-20, 0, 0), root__loc=(0, -0.55, -0.45), spine_02=(-2, 0, 0), chest=(-6, 0, 0), neck_01=(-30, 0, 0),
           neck_02=(-8, 0, 0), head=(45, 0, 0), jaw=(-10, 0, 0), tail_01=(-20, 0, 0))
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
                                   jaw=(-10 - 10 * (f % 2), 0, 0))))
pin.close(36)

# A train going by (2 s, loop): head up and tossing, bellowing after it, a forehoof thrashing the verge.
toss = Clip("trainPass")
TOSS = over(LISTEN, neck_01=(14, 0, 0), neck_02=(8, 0, 0), head=(18, 0, 0))
TOSS.update(EARS_FORWARD)
toss.key(0, bell_hangs(TOSS))
toss.key(8, bell_hangs(over(TOSS, head=(30, 0, 12), neck_02=(10, 0, 8), jaw=(-26, 0, 0))), "CONSTANT")
toss.key(16, bell_hangs(over(TOSS, head=(4, 0, -13), neck_02=(4, 0, -7), jaw=(-30, 0, 0), upperarm_r=(30, 0, 0), lowerarm_r=(50, 0, 0),
                             hand_r=(-50, 0, 0)), 20), "LINEAR")
toss.key(24, bell_hangs(over(TOSS, jaw=(-20, 0, 0), upperarm_r=(-10, 0, 0)), -15), "LINEAR")
toss.key(38, bell_hangs(over(TOSS, head=(22, 0, 8), jaw=(-28, 0, 0))), "CONSTANT")
toss.key(46, bell_hangs(TOSS))
toss.close(60)

# Hit (0.5 s, once): a blow lands; it doesn't hurt it. The head tosses round at it and the ridge flares, too fast.
hit = Clip("hit", loop=False)
HIT = over(WARN, head=(-2, 0, 13), neck_02=(0, 0, 8), neck_01=(0, 0, 5), jaw=(-20, 0, 0), spine_02=(0, 6, -6), root__loc=(-0.04, 0, -0.03))
hit.key(0, bell_hangs(LISTEN), "CONSTANT")
hit.key(1, bell_hangs(HIT, 18), "CONSTANT")
hit.key(6, bell_hangs(over(HIT, head=(-8, 0, 10)), -10), "LINEAR")
hit.key(15, bell_hangs(WARN), "CONSTANT")

kit.build()
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
# The brief's numbers (§3), at rest: ~3.4 m to the rack's top, 3.2 m across it (CreatureArtTests pins them).
lo = Vector((1e9, 1e9, 1e9))
hi = -lo
for v in (v for p in kit.parts for v in p.v):
    lo = Vector((min(lo.x, v.x), min(lo.y, v.y), min(lo.z, v.z)))
    hi = Vector((max(hi.x, v.x), max(hi.y, v.y), max(hi.z, v.z)))
print(f"[dt] moose: {hi.x - lo.x:.2f} m across (brief {RACK_SPAN}), {hi.z:.2f} m to the rack's top (brief {RACK_TOP}), "
      f"{hi.y - lo.y:.2f} m nose to rump; clips {[c.name + ':' + str(c.length) for c in CLIPS]}")
