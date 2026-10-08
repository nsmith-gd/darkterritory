"""CINDER HOUND (GDD §21 rear, App. A.3 · heat, scent): a pack animal that runs the line behind the train.

The director's concept sheet (7 Oct 2026, queue #51, note 312: "base what you have for the Cinder Hound off of this look
moving forward"). "The strongest monsters are the ones where you can still tell what they used to be" (§26.5): a hound,
burnt to its frame and grown over by what burnt it. Gaunt and stilt-legged, the hocks high, the chest deep and narrow and
the waist tucked up hard; a long, narrow, faceted skull carried low, tall pointed ears, two coals for eyes. The hide gone
to charred plates (char: the library's bark, burnt black), overrun by thorny ridges of clinker that branch over it like
roots (thorn), and between them cracks glowing like a banked fire (ember_crack: "the heat they hunt by is what you see of
them at night", GreyboxScene); shards of black crystal up the spine (shard) with flame licking off the tallest; singed fur
still on the neck, the shoulders, the chest and the haunches (fur); the lower legs cased in plates, spurred at the elbow
and the hock; big splayed paws with long black claws; a tail like a burnt thorn branch. Silhouette first (§26.1): at
distance in fog, a tall, spiked, long-legged shape with a smoulder along its side.

0.72 m at the withers, 1.6 m nose to rump (the tail hangs on behind, 1.88 m all told), head forward (-Z in the engine).
SK_Quad. Clips: prowl (loop), run (gallop loop), crouch (telegraph: low, ready, trembling), lunge (commit), board, hit,
bite. Motion (§31): "unnaturally still when observed, disturbing changes in pace, abrupt turns, lurches, too-fast
corrections": holds and CONSTANT-key pops, the head snapping round a beat before the body.

    blender -b --python tools/blender/cinder_hound.py -- content/art/models/cinder_hound.glb

This is the hound's game mesh, rig and clips. The game's cinder_hound.glb is tools/models/recipes/cinder_hound.py's,
which runs this script, models a high-resolution hide over it and bakes that down onto this mesh.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over, smoothstep  # noqa: E402

rig.reset()


def quad():
    """SK_Quad: root, pelvis, spine_01..03, chest, neck_01..02, head, jaw, tongue, ears, tail_01..05, front legs
    (scapula, upperarm, lowerarm, hand, finger), rear legs (thigh, calf, foot, toe), belly (the heaving ribcage)
    and three crest bones for the shards along the back (they twitch out of step: the "desync" of §31)."""
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.56, 0.64), (0, -0.38, 0.66)),
        Bone("spine_01", "pelvis", (0, -0.38, 0.66), (0, -0.18, 0.665)),
        Bone("spine_02", "spine_01", (0, -0.18, 0.665), (0, 0.02, 0.675)),
        Bone("spine_03", "spine_02", (0, 0.02, 0.675), (0, 0.22, 0.695)),
        Bone("chest", "spine_03", (0, 0.22, 0.695), (0, 0.38, 0.715)),
        Bone("neck_01", "chest", (0, 0.38, 0.715), (0, 0.5, 0.765)),
        Bone("neck_02", "neck_01", (0, 0.5, 0.765), (0, 0.6, 0.79)),
        Bone("head", "neck_02", (0, 0.6, 0.79), (0, 0.98, 0.7)),
        Bone("jaw", "head", (0, 0.64, 0.715), (0, 0.95, 0.66)),
        Bone("tongue", "jaw", (0, 0.68, 0.705), (0, 0.9, 0.675)),
        Bone("belly", "spine_02", (0, 0.05, 0.5), (0, 0.2, 0.44)),
        Bone("crest_01", "spine_01", (0, -0.3, 0.7), (0, -0.3, 0.82)),
        Bone("crest_02", "spine_02", (0, -0.05, 0.71), (0, -0.05, 0.86)),
        Bone("crest_03", "spine_03", (0, 0.2, 0.73), (0, 0.2, 0.9)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        b += [
            Bone(f"ear_{side}", "head", (sx * 0.045, 0.645, 0.85), (sx * 0.07, 0.605, 1.0)),
            Bone(f"scapula_{side}", "chest", (sx * 0.075, 0.28, 0.7), (sx * 0.115, 0.34, 0.52)),
            Bone(f"upperarm_{side}", f"scapula_{side}", (sx * 0.115, 0.34, 0.52), (sx * 0.115, 0.265, 0.36)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.115, 0.265, 0.36), (sx * 0.115, 0.28, 0.12)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.115, 0.28, 0.12), (sx * 0.115, 0.31, 0.035)),
            Bone(f"finger_{side}", f"hand_{side}", (sx * 0.115, 0.31, 0.035), (sx * 0.115, 0.38, 0.014)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.085, -0.5, 0.62), (sx * 0.1, -0.36, 0.42)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.1, -0.36, 0.42), (sx * 0.095, -0.55, 0.21)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.095, -0.55, 0.21), (sx * 0.095, -0.51, 0.035)),
            Bone(f"toe_{side}", f"foot_{side}", (sx * 0.095, -0.51, 0.035), (sx * 0.095, -0.44, 0.014)),
        ]
    tail = [(0, -0.62, 0.64), (0, -0.69, 0.61), (0, -0.76, 0.56), (0, -0.82, 0.49), (0, -0.86, 0.41), (0, -0.88, 0.32)]
    b += [Bone(f"tail_0{i + 1}", "pelvis" if i == 0 else f"tail_0{i}", tail[i], tail[i + 1]) for i in range(5)]
    return Skeleton("SK_Quad", b)


sk = quad()
sk.build()
kit = rig.Kit(sk, "cinder_hound")

CHAR = Mat("char.hound", hexc("#17130f"), shine=0.12)
EMBER = Mat("ember_crack.hound", hexc("#c05420"), shine=0.1, glow=0.9)
THORN = Mat("thorn.hound", hexc("#2a1712"), shine=0.25)
SHARD = Mat("mineral_growth.hound", hexc("#1e1b20"), shine=0.45)
FUR = Mat("fur.hound", hexc("#4a3524"), shine=0.05)
CLAW = Mat("claw.hound", hexc("#0e0c0b"), shine=0.5)
GUMS = Mat("flesh.gums", hexc("#4a2a26"), shine=0.3)
TEETH = Mat("flesh.teeth", hexc("#a89c84"), shine=0.3)
EYE = Mat("eye.hound", hexc("#f0a040"), emissive=1.0)
# The cracks' open cores and the flame off the shards: pure light, proud of the hide, so the heat reads from any side
# and through fog.
CORE = Mat("ember_core.hound", hexc("#d86a24"), emissive=1.0)

body = kit.part("body")

# --- weights -------------------------------------------------------------------------------------------------
SPINE = along("y", [(-0.62, "pelvis"), (-0.44, "pelvis"), (-0.28, "spine_01"), (-0.08, "spine_02"), (0.12, "spine_03"),
                    (0.3, "chest"), (0.44, "neck_01"), (0.54, "neck_02"), (0.62, "head")])


def trunk(p):
    w = SPINE(p)
    # The underside of the ribcage heaves with the belly bone; the haunches go with the thighs a little.
    if -0.15 < p.y < 0.35 and p.z < 0.56:
        k = smoothstep(0.56, 0.45, p.z) * 0.6
        w = {b: v * (1 - k) for b, v in w.items()}
        w["belly"] = w.get("belly", 0) + k
    if p.y < -0.36 and p.z < 0.66 and abs(p.x) > 0.04:
        k = smoothstep(0.66, 0.52, p.z) * smoothstep(-0.36, -0.5, p.y) * 0.7
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    if 0.22 < p.y < 0.42 and p.z < 0.66 and abs(p.x) > 0.05:
        k = smoothstep(0.66, 0.52, p.z) * 0.6
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"scapula_{s}"] = w.get(f"scapula_{s}", 0) + k
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


# --- trunk: rump to withers to neck, by side and top views ----------------------------------------------------
# (y, half width, top, bottom, squareness): the haunches high and narrow, the waist tucked up hard under the loin, the
# chest a deep narrow keel down near the elbows, the withers the highest point, the neck carried forward and low.
TRUNK = [(-0.67, 0.035, 0.64, 0.6, 1.0), (-0.62, 0.08, 0.67, 0.53, 0.9), (-0.52, 0.104, 0.685, 0.5, 0.8),
         (-0.42, 0.098, 0.69, 0.52, 0.8), (-0.32, 0.068, 0.68, 0.585, 0.9), (-0.22, 0.064, 0.675, 0.59, 0.9),
         (-0.12, 0.078, 0.68, 0.53, 0.85), (-0.02, 0.096, 0.69, 0.47, 0.8), (0.08, 0.104, 0.7, 0.42, 0.8),
         (0.17, 0.106, 0.712, 0.4, 0.8), (0.26, 0.104, 0.728, 0.41, 0.8), (0.34, 0.098, 0.745, 0.45, 0.8),
         (0.41, 0.084, 0.762, 0.53, 0.85), (0.48, 0.07, 0.782, 0.61, 0.9), (0.56, 0.062, 0.8, 0.67, 0.9)]


def section_at(y):
    """The trunk's section at y, between TRUNK's."""
    for a, b in zip(TRUNK, TRUNK[1:]):
        if a[0] <= y <= b[0]:
            t = (y - a[0]) / (b[0] - a[0])
            return tuple(u + (v - u) * t for u, v in zip(a, b))
    return TRUNK[0] if y < TRUNK[0][0] else TRUNK[-1]


def on_trunk(y, ang, out=0.0, side=1):
    """A point on the trunk's hide at y, `ang` round its section from the top (0) to the side (pi/2) and the belly
    (pi), on that side, `out` metres proud of it."""
    _, hw, top, bot, e = section_at(y)
    zc, hz = (top + bot) / 2, (top - bot) / 2
    sa, ca = math.sin(ang), math.cos(ang)
    p = Vector((side * abs(sa) ** e * hw, y, zc + math.copysign(abs(ca) ** e, ca) * hz))
    n = Vector((p.x / max(hw, 1e-3) ** 2, 0, (p.z - zc) / max(hz, 1e-3) ** 2))
    return p + n.normalized() * out if out else p


def ribs(i, j, a, p):
    """Starved: the spine a ridge, the ribs standing out in bars down the keel, hips and shoulder blades sharp."""
    q = Vector(p)
    if abs(math.sin(a)) < 0.3 and math.cos(a) > 0.8:
        q.z += 0.012  # the spine
    if -0.12 < q.y < 0.3 and abs(math.sin(a)) > 0.55 and q.z < 0.68:
        q.x *= 1 + 0.07 * max(0.0, math.sin(q.y * 62))
    q.x *= 1 + 0.08 * noise3(q, 11, 14.0)
    q.z += 0.008 * noise3(q, 12, 17.0)
    return q


def finer(secs, n=2):
    """Each gap between sections split in n (smoother barrel, more ribs to catch the light)."""
    out = []
    for a, b in zip(secs, secs[1:]):
        for k in range(n):
            t = k / n
            out.append(tuple(x + (y - x) * t for x, y in zip(a, b)))
    out.append(secs[-1])
    return out


def hide(pts, n):
    """Which faces crack and glow: the flanks over the ribs, the haunches and a seam along the spine; the rest char."""
    c = sum(pts, Vector()) / len(pts)
    flank = abs(n.x) > 0.55 and -0.22 < c.y < 0.36 and 0.46 < c.z < 0.68
    haunch = abs(n.x) > 0.6 and -0.56 < c.y < -0.4 and 0.55 < c.z < 0.67
    back = n.z > 0.75 and -0.45 < c.y < 0.3 and abs(c.x) < 0.035
    return EMBER if (flank and noise3(c, 5, 9.0) > 0.1) or (haunch and noise3(c, 6, 9.0) > 0) or back else CHAR


body.sections(finer(TRUNK, 3), 24, CHAR, trunk, cap0=True, shape=ribs, fmat=hide)

# --- head: a long, narrow, faceted skull carried low, its own piece flat-shaded so its planes catch the light like a
# carved thing; the lower jaw its own piece so the mouth can open -------------------------------------------------
skull_part = kit.part("skull", smooth=False)
HEAD = [(0.56, 0.06, 0.83, 0.68, 0.9), (0.62, 0.07, 0.862, 0.69, 0.7), (0.68, 0.066, 0.852, 0.7, 0.7),
        (0.75, 0.05, 0.815, 0.705, 0.75), (0.82, 0.04, 0.778, 0.7, 0.8), (0.89, 0.032, 0.75, 0.695, 0.85),
        (0.94, 0.024, 0.73, 0.69, 0.9), (0.965, 0.013, 0.716, 0.692, 1.0)]


def skull(i, j, a, p):
    q = Vector(p)
    # A blade of a nasal ridge down the middle, the brow jutting over sunk sockets, the cheeks cut flat.
    if math.cos(a) > 0.85:
        q.z += 0.012 * smoothstep(0.62, 0.7, q.y)
    if 0.62 < q.y < 0.7 and q.z > 0.82:
        q.z += 0.012
    q.x *= 1 + 0.06 * noise3(q, 21, 20.0)
    return q


skull_part.sections(HEAD, 8, CHAR, "head", cap1=True, shape=skull)
skull_part.sections([(0.64, 0.05, 0.705, 0.66, 0.8), (0.74, 0.044, 0.703, 0.665, 0.8), (0.84, 0.034, 0.7, 0.67, 0.85),
                     (0.92, 0.02, 0.695, 0.675, 1.0)], 6, CHAR, "jaw", cap1=True)
# Teeth along both jaws: dead ivory in a black mouth, long ones at the front.
for sx in (-1, 1):
    for k in range(6):
        y = 0.74 + k * 0.035
        top = 0.703 - k * 0.002
        skull_part.box((sx * (0.038 - k * 0.0035), y, top - 0.014), (0.006, 0.008, 0.016 if k else 0.026), TEETH, "head",
                       taper=(0.35, 0.35), rot=rig.Matrix.Rotation(math.pi, 4, "X"))
        skull_part.box((sx * (0.034 - k * 0.0035), y + 0.012, 0.703), (0.005, 0.007, 0.012 if k else 0.022), TEETH, "jaw",
                       taper=(0.3, 0.3))
body.box((0, 0.78, 0.702), (0.03, 0.12, 0.005), GUMS, "jaw")
body.tube([(0, 0.68, 0.7), (0, 0.78, 0.697), (0, 0.86, 0.69)], [(0.02, 0.006)] * 3, 4, GUMS, "tongue", ref=(0, 0, 1),
          cap1=True)
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    # Ears: tall, narrow, pointed, charred thin as burnt card, a notch torn from the edge.
    skull_part.slab([(sx * 0.028, 0.66, 0.84), (sx * 0.072, 0.635, 0.85), (sx * 0.078, 0.605, 1.01), (sx * 0.06, 0.615, 0.99)],
                    0.012, CHAR, f"ear_{s}", down=(0, -1, 0))
    # Eyes: two coals, deep under the brow. The only warm points on the face.
    skull_part.box((sx * 0.054, 0.69, 0.812), (0.009, 0.014, 0.007), EYE, "head")

# --- legs: long and thin as sticks, knobbed at the joints, the lower legs cased in plates ------------------------
# (The forelegs stand out past the deep keel of the chest, FORE from the middle.)
FORE = 0.115
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * FORE
    front = along("z", [(0.02, "finger_{s}"), (0.05, "hand_{s}"), (0.13, "lowerarm_{s}"), (0.3, "lowerarm_{s}"),
                        (0.37, "upperarm_{s}"), (0.5, "upperarm_{s}"), (0.6, "scapula_{s}")])
    fw = lambda p, f=front: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x * 0.85, 0.3, 0.68), (x, 0.33, 0.56), (x, 0.3, 0.46), (x * 1.02, 0.265, 0.37), (x, 0.272, 0.28),
               (x, 0.276, 0.2), (x, 0.28, 0.13), (x, 0.295, 0.08), (x, 0.305, 0.05), (x, 0.315, 0.03)],
              [(0.055, 0.075), 0.054, 0.042, 0.036, 0.029, 0.025, 0.026, 0.024, 0.022, 0.02], 9, CHAR, fw, ref=(0, 1, 0))
    rear = along("z", [(0.02, "toe_{s}"), (0.05, "foot_{s}"), (0.19, "foot_{s}"), (0.23, "calf_{s}"), (0.38, "calf_{s}"),
                       (0.44, "thigh_{s}"), (0.6, "thigh_{s}")])
    rw = lambda p, f=rear: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    xr = sx * 0.095
    body.tube([(xr * 0.95, -0.47, 0.66), (xr * 1.05, -0.43, 0.56), (xr * 1.05, -0.39, 0.47), (xr, -0.37, 0.41),
               (xr, -0.44, 0.33), (xr, -0.51, 0.25), (xr, -0.548, 0.2), (xr, -0.535, 0.13), (xr, -0.52, 0.07),
               (xr, -0.505, 0.035)],
              [(0.066, 0.08), 0.066, 0.05, 0.036, 0.03, 0.028, 0.026, 0.022, 0.021, 0.02], 9, CHAR, rw, ref=(0, 1, 0))

# --- thorn: the clinker grown over it, ridges branching over the hide like roots, the plates on the lower legs and
# their spurs, the claws ------------------------------------------------------------------------------------------
thorn = kit.part("thorn", smooth=False)


def spike(part, root, tip, r, mat, bones):
    """A thorn: a four-sided cone from `root` out to `tip`."""
    part.tube([root, root.lerp(tip, 0.55), tip], [r, r * 0.55, 0.0015], 4, mat, bones, ref=(0, 0, 1), cap1="point")


# The ridges: per side, roots from the spine down over the flank and the haunch, each branching once, thorned.
for sx in (-1, 1):
    for k, (y0, dy, a1, branch) in enumerate([(-0.5, 0.05, 1.7, 0.55), (-0.3, -0.06, 1.55, 0.5), (-0.1, 0.07, 1.95, 0.6),
                                              (0.1, -0.05, 2.05, 0.45), (0.28, 0.06, 1.85, 0.55), (0.42, 0.04, 1.6, 0.5)]):
        pts, angs = [], [0.18 + (a1 - 0.18) * i / 5 for i in range(6)]
        for i, ang in enumerate(angs):
            y = y0 + dy * i / 5 + 0.012 * noise3(Vector((sx, k, i)), 141, 1.3)
            pts.append(on_trunk(y, ang, 0.006, sx))
        thorn.tube(pts, [0.016, 0.014, 0.012, 0.01, 0.008, 0.005], 4, THORN, trunk, ref=(sx, 0, 0), cap1="point")
        # Its branch, off the middle, back and down.
        m = int(len(pts) * branch)
        by = pts[m].y
        bpts = [pts[m]] + [on_trunk(by - 0.03 * i * sx * (1 if k % 2 else -1), angs[m] + 0.18 * i, 0.006, sx) for i in (1, 2, 3)]
        thorn.tube(bpts, [0.01, 0.008, 0.006, 0.004], 4, THORN, trunk, ref=(sx, 0, 0), cap1="point")
        # A thorn off it, standing out of the hide (one each: the budget's the crack network's).
        for i in (2,):
            root = pts[i]
            out = on_trunk(root.y, angs[i], 0.06, sx)
            spike(thorn, root, out + Vector((0, -0.02, 0.01)), 0.011, THORN, trunk)
# The legs' plates: charred scutes down the fronts of the forearms and the cannons, a spur back off each elbow and hock.
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * FORE
    for k, z in enumerate((0.31, 0.24, 0.17)):
        thorn.box((x * 1.02, 0.262 + 0.004 * k, z), (0.03, 0.014, 0.036), THORN, f"lowerarm_{s}", taper=(0.75, 0.9),
                  rot=rig.Matrix.Rotation(-0.25, 4, "X"))
    spike(thorn, Vector((x, 0.255, 0.37)), Vector((x * 1.1, 0.19, 0.42)), 0.016, THORN, f"upperarm_{s}")
    xr = sx * 0.095
    for k, z in enumerate((0.17, 0.11)):
        thorn.box((xr * 1.02, -0.522 - 0.006 * k, z), (0.028, 0.013, 0.032), THORN, f"foot_{s}", taper=(0.75, 0.9),
                  rot=rig.Matrix.Rotation(0.2, 4, "X"))
    spike(thorn, Vector((xr, -0.56, 0.22)), Vector((xr * 1.05, -0.64, 0.27)), 0.018, THORN, f"calf_{s}")
    # Paws: big and splayed, four long toes, the claws long and black and hooked.
    for y0, bone, root in ((0.33, f"finger_{s}", f"hand_{s}"), (-0.47, f"toe_{s}", f"foot_{s}")):
        px = x * 0.98 if y0 > 0 else sx * 0.095
        body.box((px, y0, 0.02), (0.036, 0.05, 0.018), CHAR, {bone: 0.7, root: 0.3}, taper=(0.85, 0.75))
        for t in (-1.5, -0.5, 0.5, 1.5):
            tx = px + t * 0.017
            body.box((tx, y0 + 0.05, 0.014), (0.007, 0.022, 0.011), CHAR, bone, taper=(0.7, 0.6))
            spike(thorn, Vector((tx, y0 + 0.068, 0.014)), Vector((tx + t * 0.006, y0 + 0.11, 0.0015)), 0.006, CLAW, bone)

# --- shards: black crystal up the spine, tallest over the withers, raked back, twitching on the crest bones -------
SHARDS = [(-0.52, 0.05, "pelvis"), (-0.42, 0.07, "pelvis"), (-0.32, 0.06, "crest_01"), (-0.24, 0.1, "crest_01"),
          (-0.14, 0.08, "crest_02"), (-0.05, 0.12, "crest_02"), (0.04, 0.1, "crest_02"), (0.13, 0.13, "crest_03"),
          (0.21, 0.16, "crest_03"), (0.29, 0.13, "crest_03"), (0.36, 0.09, "chest"), (0.43, 0.06, "neck_01")]
def shard_lean(y):
    """How far a spine shard rakes back towards the tail (rad)."""
    return 0.45 + 0.15 * noise3(Vector((y, 1, 0)), 42, 9.0)


def shard_tip(y, h):
    """Where a spine shard's point is: from its root on the spine, its length raked back."""
    a = shard_lean(y)
    return Vector((0.012 * noise3(Vector((y, 0, 0)), 41, 13.0), y - math.sin(a) * h * 1.05, on_trunk(y, 0.0).z - 0.01 + math.cos(a) * h * 1.05))


for k, (y, h, bone) in enumerate(SHARDS):
    top = on_trunk(y, 0.0).z
    lean = shard_lean(y)
    dx = 0.012 * noise3(Vector((y, 0, 0)), 41, 13.0)
    # (A box's rotation is about its middle: put that half its length up the raked line from the root.)
    mid = Vector((dx, y - math.sin(lean) * h * 0.5, top - 0.01 + math.cos(lean) * h * 0.5))
    thorn.box(tuple(mid), (0.016 + h * 0.06, 0.02 + h * 0.08, h * 0.55), SHARD, bone, taper=(0.08, 0.25),
              rot=rig.Matrix.Rotation(lean, 4, "X"))
    # A splinter beside each of the bigger ones, off to one side.
    if h >= 0.1:
        sx = 1 if k % 2 else -1
        thorn.box((dx + sx * 0.035, y + 0.02, top + h * 0.22), (0.01, 0.014, h * 0.3), SHARD, bone,
                  taper=(0.1, 0.3), rot=rig.Matrix.Rotation(0.3, 4, "X") @ rig.Matrix.Rotation(sx * 0.45, 4, "Y"))
# Shards on the skull, raked back between the ears, and on the haunches.
thorn.box((0, 0.6, 0.875), (0.012, 0.018, 0.05), SHARD, "head", taper=(0.1, 0.3), rot=rig.Matrix.Rotation(0.7, 4, "X"))
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    thorn.box((sx * 0.08, -0.5, 0.7), (0.01, 0.016, 0.05), SHARD, f"thigh_{s}", taper=(0.1, 0.3),
              rot=rig.Matrix.Rotation(0.5, 4, "X") @ rig.Matrix.Rotation(sx * 0.5, 4, "Y"))

# --- fur: what's left of its coat, singed, in tufts at the neck and the chest, the shoulders and the haunches -------
fur = kit.part("fur", smooth=False)


def tuft(at, out, bones, n=4, length=0.07, seed=0):
    """A tuft: n thin blades from `at`, splayed about `out`, swept back along the body."""
    o = Vector(out).normalized()
    side = o.cross(Vector((0, 1, 0)))
    if side.length < 1e-3:
        side = Vector((1, 0, 0))
    side.normalize()
    for i in range(n):
        j = noise3(Vector((seed, i, 0)), 151, 1.7)
        d = (o + side * (0.5 * (i - (n - 1) / 2) / max(1, n - 1) + 0.2 * j) + Vector((0, -0.6, 0))).normalized()
        tip = Vector(at) + d * length * (0.8 + 0.4 * abs(j))
        fur.tube([Vector(at), Vector(at).lerp(tip, 0.5) + Vector((0, 0, -0.008)), tip], [0.008, 0.005, 0.001], 3, FUR, bones,
                 ref=(0, 0, 1), cap1="point")


k = 0
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    # The neck's ruff and the shoulders.
    for y, ang in ((0.44, 1.2), (0.5, 1.5), (0.52, 2.2), (0.46, 2.6), (0.38, 1.7), (0.32, 1.9)):
        p = on_trunk(y, ang, 0.0, sx)
        k += 1
        tuft(p, (p - Vector((0, y, section_at(y)[2] * 0.5 + section_at(y)[3] * 0.5))), trunk(p), length=0.08, seed=k)
    # The haunch and the elbow.
    for y, ang in ((-0.52, 1.8), (-0.44, 2.1)):
        p = on_trunk(y, ang, 0.0, sx)
        k += 1
        tuft(p, (sx, -0.2, -0.3), trunk(p), length=0.07, seed=k)
    tuft((sx * (FORE + 0.012), 0.3, 0.44), (sx, -0.3, -0.5), {f"upperarm_{s}": 1.0}, n=3, length=0.06, seed=50 + sx)
# The chest's beard, hanging under the neck.
for y in (0.4, 0.47):
    k += 1
    tuft(on_trunk(y, math.pi, 0.0), (0, 0.2, -1), trunk(Vector((0, y, 0.5))), n=5, length=0.09, seed=k)

# --- tail: a burnt thorn branch, ragged, carried low, split at the end --------------------------------------------
TAIL = along("y", [(-0.875, "tail_05"), (-0.85, "tail_04"), (-0.8, "tail_03"), (-0.73, "tail_02"), (-0.66, "tail_01"),
                   (-0.6, "pelvis")])
tail_pts = [(0, -0.64, 0.645), (0, -0.69, 0.61), (0, -0.76, 0.56), (0, -0.82, 0.49), (0, -0.86, 0.41), (0, -0.88, 0.33),
            (0, -0.885, 0.27)]
body.tube(tail_pts, [0.04, 0.04, 0.034, 0.028, 0.022, 0.015, 0.006], 8, CHAR, TAIL, ref=(0, 0, 1), cap1=True,
          shape=lambda i, j, a, p, fr: p + (p - Vector(tail_pts[i])) * 0.4 * noise3(p, 31, 40.0))
for i, (side, up) in enumerate(((1, 0.6), (-1, 0.4), (1, -0.2), (-1, 0.5), (1, 0.1), (-1, -0.3))):
    at = Vector(tail_pts[i + 1])
    tip = at + Vector((side * 0.055, -0.03, 0.04 * up + 0.02))
    thorn.tube([at, at.lerp(tip, 0.5) + Vector((0, -0.01, 0.01)), tip], [0.012, 0.007, 0.002], 4, THORN, TAIL(at),
               ref=(0, 0, 1), cap1="point")
    spike(thorn, at + Vector((0, 0.005, 0.02)), at + Vector((side * -0.01, -0.025, 0.06)), 0.008, THORN, TAIL(at))
# The tip split three ways like a snapped branch.
end = Vector(tail_pts[-2])
for d in ((0.03, -0.02, -0.06), (-0.03, -0.015, -0.06), (0, 0.02, -0.07)):
    thorn.tube([end, end + Vector(d)], [0.01, 0.002], 4, THORN, "tail_05", ref=(0, 1, 0), cap1="point")

# --- cracks: jagged seams of fire across the ribs, the haunches and down the back, between the ridges --------------
cracks = kit.part("cracks", smooth=False)
for sx in (-1, 1):
    for k, (y0, a0, dy, da, n) in enumerate([(-0.14, 1.0, 0.07, 0.12, 6), (0.02, 0.9, 0.06, 0.16, 6), (0.18, 1.0, 0.05, 0.15, 5),
                                              (-0.02, 1.55, 0.07, 0.05, 4), (0.3, 1.2, 0.03, 0.18, 4), (-0.5, 1.0, 0.05, 0.12, 3)]):
        pts = []
        y, a = y0, a0
        for i in range(n):
            # Wander: each step jinks by a fixed hash, so the cracks look torn, not drawn.
            y += dy + 0.035 * noise3(Vector((sx * 3 + k, i, 0)), 131, 1.7)
            a += da + 0.16 * noise3(Vector((sx * 5 + k, i, 1)), 132, 1.9)
            pts.append(on_trunk(y, a, 0.005, sx))
        cracks.tube(pts, [0.006 if i % 2 else 0.004 for i in range(len(pts))], 4, CORE, trunk, ref=(sx, 0, 0), cap0="point",
                    cap1="point")
        # A fork off its middle, the way a crack runs off another.
        f = pts[len(pts) // 2]
        fork = [f] + [on_trunk(f.y + 0.03 * i * (1 if k % 2 else -1), a - 0.12 * i, 0.005, sx) for i in (1, 2)]
        cracks.tube(fork, [0.004, 0.003, 0.002], 4, CORE, trunk, ref=(sx, 0, 0), cap1="point")
spine_crack = [on_trunk(y, 0.0, 0.004) + Vector((0.006 * noise3(Vector((y, 0, 0)), 133, 20.0), 0, 0)) for y in
               [-0.4 + 0.07 * i for i in range(10)]]
cracks.tube(spine_crack, [0.008] * len(spine_crack), 4, CORE, trunk, ref=(0, 0, 1), cap0="point", cap1="point")
# Flame licking off the tallest shards: thin blades of it, raked back with them.
for y, h, bone in SHARDS:
    if h < 0.12:
        continue
    # Off the shard's upper third, streaming back past its point.
    tip = shard_tip(y, h)
    base = Vector((tip.x, y, on_trunk(y, 0.0).z)).lerp(tip, 0.6)
    for k, (dx, up, back) in enumerate(((0.0, 0.1, 0.07), (0.014, 0.07, 0.09), (-0.014, 0.08, 0.06))):
        root = base + Vector((dx, 0.004, 0))
        end = tip + Vector((dx * 1.2, -back * 0.6, 0.6 * up * h / 0.16))
        cracks.tube([root, root.lerp(end, 0.5) + Vector((dx, 0.008, 0.006)), end], [0.01, 0.006, 0.001], 3, CORE, bone,
                    ref=(0, 0, 1), cap1="point")

# --------------------------------------------------------------------------------------------------------------
# Clips. Angles are the armature's axes (rig.rot): +X tips a forward-pointing bone's end up (a head raised, a spine
# arched at that joint) and swings a hanging leg forward; +Z turns towards -X.


# The gaits: the paws placed by IK on footfall paths, not swung by angle tables (the Look Review, 6 Oct: "these
# animations look rather weak ... a full overhaul with much better quality"). Each paw is down for `duty` of the stride,
# sliding back under the body as the body goes over it, then picked up, folded and swung through to reach ahead.
# `reach`: where each paw lands and leaves, ahead of and behind its rest place along the body, and how high it's
# lifted. `pitch`: the lower legs' pitch (degrees above the line ahead, side on) through a stride, the hind's foot (the
# hock down to the paw) and toes, the fore's hand (the wrist down to the paw) and fingers; in stance (landing, leaving),
# in swing (lifted, the fold at mid-swing, about to land). The pitches put the hock and the wrist where IK reaches them
# and keep the paw flat on the ground while it's down.
PAW_REST = {"rear": -0.44, "front": 0.38}
GROUND = 0.014
GAITS = {
    # The run: a rotary gallop, a stride in 14 frames.
    # (Its legs are long, a third again what a dog's would be at this size: the stride's long and the paws lift high.)
    "run": {"duty": 0.32, "reach": {"rear": (0.26, -0.26, 0.2), "front": (0.22, -0.16, 0.25)},
            "pitch": {"foot": {"stance": (-78, -58), "swing": (-58, -96, -80)},
                      "toe": {"stance": (-6, -2), "swing": (-50, -110, -20)},
                      "hand": {"stance": (-74, -44), "swing": (-44, -190, -78)},
                      "finger": {"stance": (-6, -4), "swing": (-60, -250, -40)}}},
    # The prowl: a stalking walk, each paw set down with care, low and short.
    "walk": {"duty": 0.68, "reach": {"rear": (0.15, -0.16, 0.08), "front": (0.16, -0.13, 0.11)},
             "pitch": {"foot": {"stance": (-76, -64), "swing": (-64, -98, -76)},
                       "toe": {"stance": (-6, -3), "swing": (-40, -90, -10)},
                       "hand": {"stance": (-72, -54), "swing": (-54, -160, -72)},
                       "finger": {"stance": (-6, -6), "swing": (-40, -170, -20)}}},
}


def bone_pitch(name):
    b = sk[name]
    d = b.tail - b.head
    return math.degrees(math.atan2(d.z, d.y)), d.length


def smooth(u):
    u = min(1.0, max(0.0, u))
    return u * u * (3 - 2 * u)


def phase_of(gait, t):
    """(in stance, progress 0..1 through that half of the stride) at stride phase t (0 = touchdown)."""
    t %= 1.0
    d = gait["duty"]
    return (True, t / d) if t < d else (False, (t - d) / (1 - d))


def paw_path(gait, group, t):
    """The paw's place in the body's frame: (along y, height)."""
    ahead, behind, lift = gait["reach"][group]
    stance, u = phase_of(gait, t)
    if stance:
        return PAW_REST[group] + ahead + (behind - ahead) * u, GROUND
    # Picked up quick and folded high under the body, swung through, then reached out and down for the next landing.
    y = PAW_REST[group] + behind + (ahead - behind) * smooth(u * 1.1 - 0.05)
    return y, GROUND + lift * math.sin(math.pi * min(1.0, u * 1.12)) ** 0.7


def pitch_at(gait, seg, t):
    stance, u = phase_of(gait, t)
    if stance:
        a, b = gait["pitch"][seg]["stance"]
        return a + (b - a) * smooth(u)
    a, m, b = gait["pitch"][seg]["swing"]
    return a + (m - a) * smooth(u / 0.45) if u < 0.45 else m + (b - m) * smooth((u - 0.45) / 0.55)


LEGS = {"rear": ("thigh", "calf", "foot", "toe"), "front": ("upperarm", "lowerarm", "hand", "finger")}


def paw(p, group, side, y, z, mid_pitch, end_pitch, spread=1.0):
    """IK for one leg: its paw at (along y, height z), the two lower segments at those pitches, the upper two reached.
    The stifle always forward of the line from the hip to the hock, the elbow always behind the shoulder's to the wrist
    (a dog's legs fold one way): a hard penalty on the wrong side, a pole to settle the rest."""
    upper, lower, mid, end = (f"{n}_{side}" for n in LEGS[group])
    x = (1 if side == "r" else -1) * (0.095 if group == "rear" else FORE) * spread
    pm, lm = bone_pitch(mid)
    pe, le = bone_pitch(end)
    am, ae = math.radians(mid_pitch), math.radians(end_pitch)
    joint = Vector((x, y - le * math.cos(ae) - lm * math.cos(am), z - le * math.sin(ae) - lm * math.sin(am)))
    hip = rig.pose_points(sk, p, [(upper, "head")])[0]
    way = 1 if group == "rear" else -1

    def wrong_way(e):
        d, k = joint - hip, e - hip
        return 4.0 * max(0.0, -way * (d.y * k.z - d.z * k.y) + 0.004)

    pole = (x, 0.0, 0.3) if group == "rear" else (x, 0.0, 0.25)
    # (A stifle bends with -X, an elbow with +X: reach's `bend`.)
    p = rig.reach(sk, p, upper, lower, joint, elbow_axis=0, bend=-way, pole=pole, avoid=wrong_way)
    p[mid] = rig.hang(sk, p, mid, mid_pitch - pm, 0, 0)
    p[end] = rig.hang(sk, p, end, end_pitch - pe, 0, 0)
    return p


def stride(p, gait, footfall, t):
    """Every leg on its footfall at stride phase t."""
    for leg, ph in footfall.items():
        group, side = leg.split("_")
        lp = (t - ph) % 1.0
        y, z = paw_path(gait, group, lp)
        mid, end = LEGS[group][2], LEGS[group][3]
        p = paw(p, group, side, y, z, pitch_at(gait, mid, lp), pitch_at(gait, end, lp))
    return p


# Gallop (rotary, a running dog's): one stride in RUN_FRAMES at 30 fps, the footfalls hind left, hind right, fore right,
# fore left. The back gathered (the hind feet reaching up under the chest, the spine bowed, all four off the ground)
# then extended (the hind legs driving, the forelegs reaching, the spine flat out); the body pitching over the forelegs
# as they take it; the head held steady against it, the ears pinned flat, the jaw open; the tail streaming behind,
# lagging the body's swing; the slag crest jolting out of step.
RUN_FRAMES = 14
RUN_FEET = {"rear_l": 0.0, "rear_r": 0.1, "front_r": 0.42, "front_l": 0.52}


def gallop(t):
    """The run's pose at stride phase t (0..1)."""
    w = 2 * math.pi
    flex = math.cos(w * (t - 0.86))          # +1 gathered (bowed), -1 extended
    pitch = math.sin(w * (t - 0.2))          # +: the front up (the hind legs driving), -: the front down on the forelegs
    p = {
        "root@loc": (0, 0, -0.04 + 0.045 * math.cos(w * (t - 0.86)) + 0.012 * math.cos(2 * w * (t - 0.15))),
        "root": (3.5 * pitch, 0, 0),
        "pelvis": (-9 * flex, 0, 0), "spine_01": (7 * flex, 0, 0), "spine_02": (6 * flex, 0, 0),
        "spine_03": (4 * flex, 0, 0), "chest": (-3 * flex, 0, 0),
        # The head steadied against the body's pitch and bow: the neck takes it up.
        "neck_01": (-14 - 3.5 * pitch + 4 * flex, 0, 0), "neck_02": (-5 - 2 * flex, 0, 0), "head": (8 - 2 * flex, 0, 0),
        "jaw": (-18 - 8 * max(0.0, math.sin(w * t)), 0, 0), "tongue": (-14, 0, 0),
        "ear_r": (-50, 0, -6), "ear_l": (-50, 0, 6),
        "belly": (5 * flex, 0, 0),
        "crest_01": (5 * math.sin(w * (t - 0.1)), 0, 0), "crest_02": (5 * math.sin(w * (t - 0.2)), 0, 0),
        "crest_03": (4 * math.sin(w * (t - 0.3)), 0, 2 * math.sin(w * t)),
    }
    for i in range(5):
        # (+X tips a bone pointing back down: streaming out behind is -X.)
        p[f"tail_0{i + 1}"] = ((-24 if i == 0 else -5) + (6 if i == 0 else 4) * math.sin(w * (t - 0.75 - 0.08 * i)), 0,
                               4 * math.sin(w * (t * 0.5 - 0.1 * i)) * (i > 1))
    for side in ("r", "l"):
        lp = (t - RUN_FEET[f"front_{side}"]) % 1.0
        p[f"scapula_{side}"] = (8 * math.cos(w * (lp - 0.15)), 0, 0)
    return stride(p, GAITS["run"], RUN_FEET, t)


run = Clip("run")
for f in range(RUN_FRAMES):
    run.key(f, gallop(f / RUN_FRAMES), "LINEAR")
run.close(RUN_FRAMES)

# Prowl (48 frames): a slinking stalk, the head carried level below the shoulders, the shoulder blades rolling over each
# step, the back swaying, the tail low; each paw set down with care (a lateral walk, its footfalls hind left, fore left,
# hind right, fore right). Then it stops dead mid-step, a forepaw held up off the ground where it was lifted, and the
# head snaps round to look (a hold, then a pop, §31), holds, snaps back, and it walks on as if it hadn't. That hitch is
# the whole creature.
WALK_FEET = {"rear_l": 0.0, "front_l": 0.25, "rear_r": 0.5, "front_r": 0.75}
STOP = 0.45                                   # the stride's phase it stops at: the right forepaw just lifted


def stalk(t, look=0.0):
    w = 2 * math.pi
    sway = math.sin(w * t)
    p = {
        "root@loc": (0, 0, -0.1 + 0.01 * math.cos(2 * w * (t - 0.1))),
        "pelvis": (-4, 0, 2 * sway), "spine_01": (2, 0, -2 * sway), "spine_02": (3, 0, 3 * sway), "spine_03": (0, 0, 2 * sway),
        "chest": (-6, 0, -3 * sway),
        "neck_01": (-20, 0, 2 * sway), "neck_02": (-8, 0, 0), "head": (-2 + 1.5 * math.cos(2 * w * t), 0, -2 * sway),
        "jaw": (-5, 0, 0), "ear_r": (-20, 0, -10), "ear_l": (-20, 0, 10),
        "belly": (2 * math.sin(w * 2 * t), 0, 0),
        "crest_02": (2 * math.sin(w * (t - 0.2)), 0, 0),
    }
    for i in range(5):
        p[f"tail_0{i + 1}"] = (-6 if i == 0 else 3, 0, 5 * math.sin(w * (t - 0.12 * i)))
    for side, ph in (("r", WALK_FEET["front_r"]), ("l", WALK_FEET["front_l"])):
        p[f"scapula_{side}"] = (6 * math.cos(w * (t - ph - 0.2)), 0, 0)
    if look:
        # The stop: frozen, the head snapped round to stare (at whoever's on the train), the ears up.
        p["neck_02"] = (-2, 0, 36 * look)
        p["head"] = (8, 0, 14 * look)
        p["ear_r"], p["ear_l"] = (12, 0, -18), (12, 0, 18)
    return stride(p, GAITS["walk"], WALK_FEET, t)


prowl = Clip("prowl")
for f in range(0, 49, 2):
    if f <= 20:
        ph, look, interp = STOP * f / 20, 0.0, "LINEAR"
    elif f < 32:
        # 22 frozen; 24 the head snaps round; 30 still staring; 32 snapped back and walking.
        ph, look, interp = STOP, (0.0 if f < 24 else 1.0), "CONSTANT"
    else:
        ph, look, interp = STOP + (1 - STOP) * (f - 32) / 16, 0.0, "LINEAR"
    if f in (20, 22):
        interp = "CONSTANT"
    prowl.key(f, stalk(ph % 1.0, look), interp)

# Planted poses: each paw put where it stands, flat (the stance pitches), by the same IK.
FLAT = {"rear": (-72, -5), "front": (-68, -6)}


def planted(p, feet, spread=1.0):
    """`feet` {leg: (along y, height[, mid pitch, end pitch])}: each paw there."""
    for leg, at in feet.items():
        group, side = leg.split("_")
        mp, ep = (at[2], at[3]) if len(at) > 2 else FLAT[group]
        p = paw(p, group, side, at[0], at[1], mp, ep, spread)
    return p


def posed(**bones):
    """A pose's spine, head and tail from keyword angles (root__loc for root@loc)."""
    return {k.replace("__", "@"): v for k, v in bones.items()}


# Crouch (the telegraph, 1 s loop): the predator's crouch, the front down low and the shoulder blades up past the spine,
# the hind legs gathered under it, the head low and forward and dead level, staring; the lips peeled off the teeth,
# the ears pinned, the tail low and stiff. It breathes, slow, then trembles in hard pops out of step (the crest, the
# flanks, the head), and edges its weight forward onto its forelegs and back as if about to go.
CROUCH_BODY = posed(root__loc=(0, 0.0, -0.22), pelvis=(-8, 0, 0), spine_01=(8, 0, 0), spine_02=(4, 0, 0), spine_03=(-2, 0, 0),
                   chest=(-12, 0, 0), neck_01=(-4, 0, 0), neck_02=(-12, 0, 0), head=(6, 0, 0), jaw=(-16, 0, 0),
                   tongue=(-6, 0, 0), ear_r=(-55, 0, -4), ear_l=(-55, 0, 4), scapula_r=(-16, 0, 0), scapula_l=(-16, 0, 0),
                   tail_01=(-14, 0, 0), tail_02=(-4, 0, 0), tail_03=(0, 0, 0), tail_04=(4, 0, 0), tail_05=(6, 0, 0))
CROUCH_FEET = {"front_r": (0.44, GROUND), "front_l": (0.42, GROUND), "rear_r": (-0.33, GROUND, -60, -4),
               "rear_l": (-0.35, GROUND, -60, -4)}


def crouched(lean=0.0, **extra):
    p = dict(CROUCH_BODY)
    x, y, z = p["root@loc"]
    p["root@loc"] = (x, y + 0.04 * lean, z - 0.015 * lean)
    p.update(posed(**extra))
    return planted(p, CROUCH_FEET)


CROUCH = crouched()
crouch = Clip("crouch")
for f, lean, breath in ((0, 0.0, 0.0), (8, 0.3, 1.0), (14, 0.6, 0.4)):
    crouch.key(f, crouched(lean, belly=(5 * breath, 0, 0), jaw=(-16 - 4 * breath, 0, 0)), "BEZIER")
for f, d in ((16, 1), (17, -1), (19, 1), (21, 0)):
    # Tremble: small, fast, out of step between the crest, the flanks and the head.
    crouch.key(f, crouched(0.6, belly=(4 * d, 0, 0), crest_01=(7 * d, 0, 0), crest_03=(-6 * d, 0, 3 * d),
                           head=(6 + 2 * d, 0, 2 * d), tail_05=(6, 0, 14 * d)), "CONSTANT")
crouch.key(24, crouched(0.2, belly=(2, 0, 0)), "BEZIER")
crouch.close(30)

# Lunge (the commit, 20 frames once): a beat's sink and rock back onto the haunches, then it uncoils, the hind legs
# driving it out flat, forelegs reaching past its head and the jaws wide; it lands on its forelegs a body length on,
# the back legs coming through under it, and gathers.
LUNGE_SINK = crouched(-0.6, pelvis=(-14, 0, 0), chest=(-16, 0, 0), jaw=(-24, 0, 0))
DRIVE = planted(posed(root__loc=(0, 0.14, 0.06), root=(12, 0, 0), pelvis=(8, 0, 0), spine_01=(-6, 0, 0), spine_02=(-4, 0, 0),
                     chest=(0, 0, 0), neck_01=(-6, 0, 0), neck_02=(-4, 0, 0), head=(-2, 0, 0), jaw=(-40, 0, 0), tongue=(-20, 0, 0),
                     ear_r=(-60, 0, 0), ear_l=(-60, 0, 0), tail_01=(-16, 0, 0), tail_02=(-6, 0, 0)),
                {"rear_r": (-0.62, GROUND, -40, 0), "rear_l": (-0.6, GROUND, -40, 0),
                 "front_r": (0.72, 0.3, -40, -30), "front_l": (0.68, 0.26, -50, -40)})
FLIGHT = planted(posed(root__loc=(0, 0.32, 0.16), root=(-2, 0, 0), pelvis=(4, 0, 0), spine_01=(-4, 0, 0), chest=(2, 0, 0),
                      neck_01=(-8, 0, 0), head=(-4, 0, 0), jaw=(-46, 0, 0), tongue=(-22, 0, 0), ear_r=(-60, 0, 0),
                      ear_l=(-60, 0, 0), tail_01=(-26, 0, 0), tail_02=(-6, 0, 0), tail_03=(-4, 0, 0)),
                 {"rear_r": (-0.5, 0.22, -10, -30), "rear_l": (-0.48, 0.26, -10, -30),
                  "front_r": (0.98, 0.3, -24, -12), "front_l": (0.95, 0.34, -28, -14)})
LANDED = planted(posed(root__loc=(0, 0.42, -0.1), root=(-8, 0, 0), pelvis=(-12, 0, 0), spine_01=(10, 0, 0), spine_02=(8, 0, 0),
                      chest=(-10, 0, 0), neck_01=(-16, 0, 0), neck_02=(-6, 0, 0), head=(8, 0, 0), jaw=(-32, 0, 0),
                      ear_r=(-50, 0, 0), ear_l=(-50, 0, 0), tail_01=(-8, 0, 0), tail_02=(6, 0, 0)),
                 {"front_r": (0.88, GROUND, -60, -6), "front_l": (0.84, GROUND, -60, -6),
                  "rear_r": (0.12, 0.06, -70, -20), "rear_l": (0.08, 0.04, -70, -20)})
GATHERED = planted(posed(root__loc=(0, 0.42, -0.12), pelvis=(-10, 0, 0), spine_01=(6, 0, 0), spine_02=(4, 0, 0), chest=(-10, 0, 0),
                        neck_01=(-8, 0, 0), neck_02=(-10, 0, 0), head=(6, 0, 0), jaw=(-22, 0, 0), ear_r=(-55, 0, 0),
                        ear_l=(-55, 0, 0), tail_01=(-10, 0, 0)),
                   {"front_r": (0.86, GROUND), "front_l": (0.82, GROUND), "rear_r": (0.08, GROUND, -60, -4),
                    "rear_l": (0.06, GROUND, -60, -4)})
lunge = Clip("lunge", loop=False)
lunge.key(0, CROUCH, "BEZIER")
lunge.key(3, LUNGE_SINK, "LINEAR")
lunge.key(6, DRIVE, "LINEAR")
lunge.key(10, FLIGHT, "LINEAR")
lunge.key(13, LANDED, "BEZIER")
lunge.key(20, GATHERED, "BEZIER")

# Hit (12 frames once): snapped sideways by the round, its legs splayed to keep it up, a twitch through the crest, and
# it rights itself too fast (§31).
STAND_FEET = {"front_r": (0.38, GROUND), "front_l": (0.38, GROUND), "rear_r": (-0.44, GROUND), "rear_l": (-0.44, GROUND)}
STAND = planted(posed(root__loc=(0, 0, -0.04), neck_01=(-12, 0, 0), neck_02=(-6, 0, 0), jaw=(-8, 0, 0), ear_r=(-30, 0, 0),
                     ear_l=(-30, 0, 0), tail_01=(10, 0, 0), tail_02=(6, 0, 0)), STAND_FEET)
HIT = planted(posed(root__loc=(-0.07, -0.02, -0.08), root=(0, 0, -6), pelvis=(0, 10, -10), spine_02=(0, 8, -12),
                   chest=(0, 10, -14), neck_01=(4, 10, 30), head=(18, 0, 20), jaw=(-35, 0, 0), crest_02=(0, 20, 0),
                   crest_03=(0, -20, 0), ear_r=(-60, 0, 0), ear_l=(-60, 0, 0), tail_01=(20, 0, 30)),
              {"front_r": (0.36, GROUND), "front_l": (0.42, GROUND), "rear_r": (-0.46, GROUND), "rear_l": (-0.4, GROUND)},
              spread=1.5)
hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(1, HIT, "CONSTANT")
hit.key(5, {**HIT, "crest_02": (0, -15, 0), "chest": (0, 6, -8)}, "LINEAR")
hit.key(9, {**STAND, "head": (-6, 0, -10)}, "CONSTANT")
hit.key(12, STAND, "CONSTANT")

# Bite (32 frames, loop; GRAB, the pack fight on someone): braced, the forepaws planted wide, the hind legs dug in and
# hauling back, the jaws clamped; the head wrenching side to side in hard pops, the whole body thrown the other way
# behind it, held, wrenched again; a hind paw losing its footing and stamping back down; the tail lashing.
def biting(w, back, slip=0.0):
    p = posed(root__loc=(0.01 * w / 30, -0.05 - back, -0.08), root=(0, 0, -0.2 * w), pelvis=(-14, 0, 0.3 * w),
             spine_01=(6, 0, 0.2 * w), spine_02=(4, 0, -0.2 * w), chest=(-6, 0, -0.3 * w),
             neck_01=(-14, 0, w * 0.5), neck_02=(-12, 0, w * 0.5), head=(10, w * 0.4, w * 0.3), jaw=(-8, 0, 0),
             ear_r=(-60, 0, 0), ear_l=(-60, 0, 0), tail_01=(-10, 0, -w), tail_02=(0, 0, -0.6 * w), tail_03=(0, 0, 0.5 * w),
             crest_02=(0, 0.3 * w, 0))
    return planted(p, {"front_r": (0.49, GROUND), "front_l": (0.49, GROUND), "rear_r": (-0.52 + 0.06 * slip, GROUND + 0.05 * slip, -50, -5),
                       "rear_l": (-0.5, GROUND, -50, -5)}, spread=1.25)


bite = Clip("bite")
for f, (w, back, slip, interp) in enumerate(((0, 0.0, 0, "CONSTANT"), (28, 0.03, 0, "CONSTANT"), (-30, 0.05, 1, "CONSTANT"),
                                             (24, 0.02, 0.5, "LINEAR"), (0, 0.0, 0, "CONSTANT"), (-26, 0.04, 0, "CONSTANT"),
                                             (32, 0.05, 0, "CONSTANT"), (0, 0.01, 0, "LINEAR"))):
    bite.key(f * 4, biting(w, back, slip), interp)
bite.close(32)

# Board (1.1 s, once; COMMIT, onto the rear car): the checklist's boarding leap, not the lunge. Off the ballast behind the
# car (the root starts a car's roof-height below and 2.5 m back of where the sim puts it, on the roof near the end), a
# bound onto the car's end, the body reared up it and the hind legs scrabbling at the planks, the forelegs hooked over the
# roof's lip, then a heave up and over onto the roof, landing in the pack fight's crouch.
REAR = {**DRIVE, "root": (62, 0, 0), "chest": (-6, 0, 0), "neck_01": (-20, 0, 0), "head": (-20, 0, 0), "jaw": (-35, 0, 0),
        "tail_01": (30, 0, 0), "tail_02": (14, 0, 0)}
REAR.update(mirror({"upperarm_r": (110, 0, 0), "lowerarm_r": (-30, 0, 0), "hand_r": (-60, 0, 0), "finger_r": (-50, 0, 0)}))
board = Clip("board", loop=False)
board.key(0, {**gallop(0.0), "root@loc": (0, -3.6, -3.75)}, "LINEAR")
board.key(5, {**DRIVE, "root@loc": (0, -3.3, -3.55)}, "LINEAR")
board.key(11, {**REAR, "root@loc": (0, -2.75, -1.55)}, "BEZIER")
for f, d in ((14, 1), (17, -1), (20, 1)):
    # Scrabbling: the forelegs hooked over the lip, the hind legs kicking at the car's end out of step.
    board.key(f, {**REAR, "root": (58 + 4 * d, 0, 0), "root@loc": (0, -2.62 + 0.02 * d, -1.05 + 0.08 * (f - 14) / 6),
                  "thigh_r": (-20 + 45 * d, 0, 0), "calf_r": (20 - 40 * d, 0, 0), "thigh_l": (-20 - 45 * d, 0, 0),
                  "calf_l": (20 + 40 * d, 0, 0), "head": (-24, 0, 8 * d)}, "CONSTANT")
board.key(25, {**DRIVE, "root": (18, 0, 0), "root@loc": (0, -1.3, -0.25)}, "BEZIER")
board.key(30, {**CROUCH, "root@loc": (0, -0.2, -0.06), "jaw": (-30, 0, 0)}, "BEZIER")
board.key(33, CROUCH, "LINEAR")

# The patrol aboard (queue #213, note 477; D1's #208; the director, 8 Oct: "It matters that they dont just stand there and
# howl, they should either patrol between cars that have doors open or patrol the roofs of the cars, jumping between them
# if they can make the jump").
#
# Patrol (64 frames, two strides): the hunting walk along the roofs. The prowl's slink without its stop: nose down at the
# boards, the head swept slowly from side to side over the two strides, the ears pricked forward, the tail low and
# swinging; it doesn't stop to stare, it's looking.
def hunting(t, sweep):
    p = stalk(t)
    p.update(posed(neck_01=(-30, 0, 4 * sweep), neck_02=(-10, 0, 12 * sweep), head=(10, 0, 10 * sweep), jaw=(-3, 0, 0),
                   ear_r=(8, 0, -14), ear_l=(8, 0, 14)))
    return p


patrol = Clip("patrol")
for f in range(0, 64, 2):
    patrol.key(f, hunting((f / 32) % 1.0, math.sin(2 * math.pi * f / 64)), "LINEAR")
patrol.close(64)

# Leap (27 frames once): over a coupling gap from one roof's end to the next. In place along the line: the sim carries it
# the 2-3 m across between frames 8 and 20 (0.27 s to 0.67 s); the clip is its gather, its arc up off the roof and its
# landing on the far one, the forelegs taking it, and on into the walk.
def aloft(pose, z, **extra):
    q = {**pose, "root@loc": (0, 0, z)}
    q.update(posed(**extra))
    return q


leap = Clip("leap", loop=False)
leap.key(0, stalk(0.0), "BEZIER")
leap.key(5, aloft(crouched(-0.3), -0.2, pelvis=(-14, 0, 0), chest=(-14, 0, 0), jaw=(-12, 0, 0)), "LINEAR")
leap.key(8, aloft(DRIVE, 0.12, jaw=(-20, 0, 0)), "LINEAR")
leap.key(14, aloft(FLIGHT, 0.62, jaw=(-14, 0, 0), tongue=(-10, 0, 0)), "BEZIER")
leap.key(20, aloft(LANDED, -0.05, jaw=(-14, 0, 0)), "LINEAR")
leap.key(24, aloft(GATHERED, -0.06, jaw=(-8, 0, 0)), "BEZIER")
leap.key(27, stalk(0.0), "BEZIER")

# Drop (36 frames once): from a roof's edge down in at an open door. It ends where the sim puts it, on the car's floor
# 0.7 m in from the doorway, facing in; it starts up on the roof over the doorway (2.9 m up: the roof at 4.0, the floor
# at 1.1), facing out over the edge. It looks down, springs out off the edge, turns in the air and swoops in through the
# doorway, lands on the sill on its forelegs and comes in.
DROP_UP, DOOR_IN = 2.9, 0.7
drop = Clip("drop", loop=False)
drop.key(0, {**STAND, "root@loc": (0, -DOOR_IN + 0.2, DROP_UP), "root": (0, 0, 180)}, "BEZIER")
drop.key(8, {**crouched(0.6, neck_01=(-34, 0, 0), neck_02=(-20, 0, 0), head=(-8, 0, 0)),
             "root@loc": (0, -DOOR_IN + 0.15, DROP_UP - 0.1), "root": (-12, 0, 180)}, "BEZIER")
drop.key(13, {**DRIVE, "root@loc": (0, -DOOR_IN - 0.25, DROP_UP + 0.05), "root": (-10, 0, 180)}, "LINEAR")
drop.key(19, {**FLIGHT, "root@loc": (0, -DOOR_IN - 0.75, DROP_UP - 0.9), "root": (-30, 0, 90)}, "LINEAR")
drop.key(24, {**FLIGHT, "root@loc": (0, -DOOR_IN - 0.35, 0.45), "root": (-18, 0, 15)}, "LINEAR")
drop.key(28, {**LANDED, "root@loc": (0, -DOOR_IN - 0.25, -0.08), "root": (-8, 0, 0)}, "BEZIER")
drop.key(32, {**GATHERED, "root@loc": (0, -0.25, -0.06)}, "BEZIER")
drop.key(36, STAND, "BEZIER")

# Climb (40 frames once): the drop backwards, out at an open door and up onto the roof. It starts where the sim has it, on
# the floor 0.7 m in from the doorway, facing out; it trots to the sill, springs out and up, turning in the air to face the
# car, hooks its forelegs over the roof's edge and hangs there scrabbling at the side as it did boarding, then heaves up
# and over onto the roof. It ends up there 2.9 m up and 0.5 m out from where it started, turned round (facing back in over
# the car).
climb = Clip("climb", loop=False)
climb.key(0, STAND, "BEZIER")
climb.key(6, {**stalk(0.3), "root@loc": (0, DOOR_IN - 0.15, 0)}, "LINEAR")
climb.key(10, {**DRIVE, "root@loc": (0, DOOR_IN + 0.05, 0.05)}, "LINEAR")
climb.key(15, {**FLIGHT, "root@loc": (0, DOOR_IN + 0.55, 1.4), "root": (20, 0, 90)}, "LINEAR")
climb.key(19, {**REAR, "root@loc": (0, DOOR_IN + 0.45, DROP_UP - 1.2), "root": (62, 0, 180)}, "BEZIER")
for f, d in ((22, 1), (25, -1), (28, 1)):
    climb.key(f, {**REAR, "root": (58 + 4 * d, 0, 180), "root@loc": (0, DOOR_IN + 0.4, DROP_UP - 1.1 + 0.05 * (f - 22) / 6),
                  "thigh_r": (-20 + 45 * d, 0, 0), "calf_r": (20 - 40 * d, 0, 0), "thigh_l": (-20 - 45 * d, 0, 0),
                  "calf_l": (20 + 40 * d, 0, 0), "head": (-24, 0, 8 * d)}, "CONSTANT")
climb.key(33, {**DRIVE, "root": (18, 0, 180), "root@loc": (0, DOOR_IN + 0.05, DROP_UP - 0.3)}, "BEZIER")
climb.key(37, {**crouched(0.2), "root@loc": (0, DOOR_IN - 0.2, DROP_UP - 0.06), "root": (0, 0, 180)}, "BEZIER")
climb.key(40, {**STAND, "root@loc": (0, DOOR_IN - 0.2, DROP_UP), "root": (0, 0, 180)}, "BEZIER")

# Sniff (120 frames, loop): the idle beat aboard, not a howl. Nose down to the boards, sniffing in quick pops along them;
# then a forepaw scraping at them (at a hatch's edge, at a seam) three times; then a long look over the side, head low
# and turned out, the ears up, still; and back to the boards.
def nosing(dip, turn=0.0, **extra):
    p = posed(root__loc=(0, 0.02, -0.06), pelvis=(0, 0, 0), chest=(-4 - 6 * dip, 0, 0), neck_01=(-34 - 14 * dip, 0, 6 * turn),
              neck_02=(-14 - 10 * dip, 0, 26 * turn), head=(4 + 6 * dip, 0, 16 * turn), jaw=(-4, 0, 0),
              ear_r=(4, 0, -16), ear_l=(4, 0, 16), tail_01=(-6, 0, 0), tail_02=(4, 0, 0))
    p.update(posed(**extra))
    return planted(p, {"front_r": (0.36, GROUND), "front_l": (0.4, GROUND), "rear_r": (-0.46, GROUND), "rear_l": (-0.44, GROUND)})


def scraping(lift):
    p = nosing(0.6, 0.0)
    return paw(p, "front", "r", 0.52 - 0.1 * lift, GROUND + 0.1 * lift, -40 - 20 * lift, -30 - 30 * lift)


sniff = Clip("sniff")
for f, pose in ((0, nosing(0.4)), (6, nosing(0.8)), (8, nosing(0.7, 0.1)), (10, nosing(0.85, 0.1)), (14, nosing(0.75, -0.15)),
                (16, nosing(0.9, -0.15)), (24, nosing(0.6, 0.2)), (28, nosing(0.9, 0.25)), (30, nosing(0.8, 0.25))):
    sniff.key(f, pose, "CONSTANT" if f in (8, 10, 14, 16, 28, 30) else "BEZIER")
for f, lift in ((38, 1), (42, 0), (46, 1), (50, 0), (54, 1), (58, 0)):
    sniff.key(f, scraping(lift), "BEZIER")
for f, pose in ((66, nosing(0.2, 1.0, ear_r=(14, 0, -20), ear_l=(14, 0, 20))), (72, nosing(0.3, 1.1, ear_r=(14, 0, -20), ear_l=(14, 0, 20))),
                (96, nosing(0.3, 1.1, ear_r=(14, 0, -20), ear_l=(14, 0, 20))), (104, nosing(0.5, 0.2)), (112, nosing(0.4))):
    sniff.key(f, pose, "BEZIER")
sniff.close(120)

kit.build()
# (No feet planter: every clip's paws are placed by IK.)
rig.bake(sk, [prowl, run, crouch, lunge, board, hit, bite, patrol, leap, drop, climb, sniff])
rig.export(rig.args()[0] if rig.args() else "cinder_hound.glb", kit)
