"""CINDER HOUND (GDD §21 rear, App. A.3 · heat, scent): a pack animal that runs the line behind the train.

"The strongest monsters are the ones where you can still tell what they used to be" (§26.5): this is a dog, a big
lean wolfish lurcher, starved to the frame: deep narrow chest, ribs, tucked waist, long thin legs, a long skull,
pricked ears. Then the Corruption: slag fused onto the spine in a ragged crest and crusted on the shoulders and
haunches, the hide gone to oily soot (tar), and the flanks and back split in cracks that glow like a banked fire
(ember_crack: "the heat they hunt by is what you see of them at night", GreyboxScene). Silhouette first (§26.1): at
distance in fog it's a low, long, spiked shape with a smoulder along its side.

0.55 m at the shoulder, 1.4 m nose to rump (the tail hangs on behind), head forward (-Z in the engine). SK_Quad.
Clips: prowl (loop), run (gallop loop), crouch (telegraph: low, ready, trembling), lunge (commit), hit.
Motion (§31): "unnaturally still when observed, disturbing changes in pace, abrupt turns, lurches, too-fast
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
    and three crest bones for the slag along the back (they twitch out of step: the "desync" of §31)."""
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.56, 0.47), (0, -0.38, 0.49)),
        Bone("spine_01", "pelvis", (0, -0.38, 0.49), (0, -0.18, 0.5)),
        Bone("spine_02", "spine_01", (0, -0.18, 0.5), (0, 0.02, 0.5)),
        Bone("spine_03", "spine_02", (0, 0.02, 0.5), (0, 0.22, 0.51)),
        Bone("chest", "spine_03", (0, 0.22, 0.51), (0, 0.38, 0.52)),
        Bone("neck_01", "chest", (0, 0.38, 0.52), (0, 0.49, 0.56)),
        Bone("neck_02", "neck_01", (0, 0.49, 0.56), (0, 0.59, 0.58)),
        Bone("head", "neck_02", (0, 0.59, 0.58), (0, 0.86, 0.53)),
        Bone("jaw", "head", (0, 0.62, 0.52), (0, 0.84, 0.49)),
        Bone("tongue", "jaw", (0, 0.66, 0.51), (0, 0.8, 0.5)),
        Bone("belly", "spine_02", (0, 0.05, 0.36), (0, 0.2, 0.3)),
        Bone("crest_01", "spine_01", (0, -0.3, 0.53), (0, -0.3, 0.62)),
        Bone("crest_02", "spine_02", (0, -0.05, 0.54), (0, -0.05, 0.66)),
        Bone("crest_03", "spine_03", (0, 0.2, 0.56), (0, 0.2, 0.7)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        b += [
            Bone(f"ear_{side}", "head", (sx * 0.045, 0.63, 0.64), (sx * 0.065, 0.6, 0.74)),
            Bone(f"scapula_{side}", "chest", (sx * 0.075, 0.28, 0.52), (sx * 0.1, 0.34, 0.37)),
            Bone(f"upperarm_{side}", f"scapula_{side}", (sx * 0.1, 0.34, 0.37), (sx * 0.1, 0.28, 0.24)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.1, 0.28, 0.24), (sx * 0.1, 0.29, 0.075)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.1, 0.29, 0.075), (sx * 0.1, 0.32, 0.028)),
            Bone(f"finger_{side}", f"hand_{side}", (sx * 0.1, 0.32, 0.028), (sx * 0.1, 0.38, 0.014)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.085, -0.5, 0.45), (sx * 0.1, -0.38, 0.28)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.1, -0.38, 0.28), (sx * 0.095, -0.53, 0.14)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.095, -0.53, 0.14), (sx * 0.095, -0.5, 0.03)),
            Bone(f"toe_{side}", f"foot_{side}", (sx * 0.095, -0.5, 0.03), (sx * 0.095, -0.44, 0.014)),
        ]
    tail = [(0, -0.62, 0.47), (0, -0.71, 0.44), (0, -0.79, 0.39), (0, -0.86, 0.33), (0, -0.92, 0.27), (0, -0.99, 0.18)]
    b += [Bone(f"tail_0{i + 1}", "pelvis" if i == 0 else f"tail_0{i}", tail[i], tail[i + 1]) for i in range(5)]
    return Skeleton("SK_Quad", b)


sk = quad()
sk.build()
kit = rig.Kit(sk, "cinder_hound")

TAR = Mat("tar.hound", hexc("#161312"), shine=0.18)
EMBER = Mat("ember_crack.hound", hexc("#c05420"), shine=0.1, glow=0.9)
SLAG = Mat("mineral_growth.hound", hexc("#2c2a30"), shine=0.3)
GUMS = Mat("flesh.gums", hexc("#5a3434"), shine=0.3)
TEETH = Mat("flesh.teeth", hexc("#a89c84"), shine=0.3)
EYE = Mat("eye.hound", hexc("#f0a040"), emissive=1.0)
# The cracks' open cores: pure light, proud of the hide, so the heat reads from any side and through fog.
CORE = Mat("ember_core.hound", hexc("#d86a24"), emissive=1.0)

body = kit.part("body")

# --- weights -------------------------------------------------------------------------------------------------
SPINE = along("y", [(-0.62, "pelvis"), (-0.44, "pelvis"), (-0.28, "spine_01"), (-0.08, "spine_02"), (0.12, "spine_03"),
                    (0.3, "chest"), (0.44, "neck_01"), (0.54, "neck_02"), (0.62, "head")])


def trunk(p):
    w = SPINE(p)
    # The underside of the ribcage heaves with the belly bone; the haunches go with the thighs a little.
    if -0.15 < p.y < 0.35 and p.z < 0.38:
        k = smoothstep(0.38, 0.28, p.z) * 0.6
        w = {b: v * (1 - k) for b, v in w.items()}
        w["belly"] = w.get("belly", 0) + k
    if p.y < -0.36 and p.z < 0.48 and abs(p.x) > 0.04:
        k = smoothstep(0.48, 0.36, p.z) * smoothstep(-0.36, -0.5, p.y) * 0.7
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    if 0.22 < p.y < 0.42 and p.z < 0.46 and abs(p.x) > 0.05:
        k = smoothstep(0.46, 0.34, p.z) * 0.6
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"scapula_{s}"] = w.get(f"scapula_{s}", 0) + k
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def hide(pts, n):
    """Which faces crack and glow: the flanks over the ribs and a seam along the spine; the rest is soot and oil."""
    c = sum(pts, Vector()) / len(pts)
    flank = abs(n.x) > 0.55 and -0.22 < c.y < 0.36 and 0.3 < c.z < 0.5
    haunch = abs(n.x) > 0.6 and -0.56 < c.y < -0.4 and 0.38 < c.z < 0.5
    back = n.z > 0.75 and -0.45 < c.y < 0.3 and abs(c.x) < 0.035
    return EMBER if (flank and noise3(c, 5, 9.0) > -0.35) or haunch or back else TAR


# --- trunk: rump to withers to neck, by side and top views ----------------------------------------------------
# (y, half width, top, bottom, squareness)
TRUNK = [(-0.67, 0.035, 0.47, 0.42, 1.0), (-0.62, 0.075, 0.5, 0.36, 0.9), (-0.52, 0.1, 0.52, 0.33, 0.8),
         (-0.42, 0.098, 0.525, 0.35, 0.8), (-0.32, 0.07, 0.515, 0.39, 0.9), (-0.22, 0.068, 0.51, 0.385, 0.9),
         (-0.12, 0.082, 0.515, 0.335, 0.85), (-0.02, 0.1, 0.52, 0.29, 0.8), (0.08, 0.108, 0.525, 0.26, 0.8),
         (0.17, 0.11, 0.535, 0.245, 0.8), (0.26, 0.108, 0.555, 0.26, 0.8), (0.34, 0.1, 0.57, 0.3, 0.8),
         (0.41, 0.085, 0.58, 0.37, 0.85), (0.48, 0.066, 0.595, 0.44, 0.9), (0.56, 0.058, 0.62, 0.49, 0.9)]


def ribs(i, j, a, p):
    """Starved: the spine a ridge, the ribs standing out in bars down the barrel, hips and shoulder blades sharp."""
    q = Vector(p)
    if abs(math.sin(a)) < 0.3 and math.cos(a) > 0.8:
        q.z += 0.012  # the spine
    if -0.12 < q.y < 0.3 and abs(math.sin(a)) > 0.55 and q.z < 0.5:
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


body.sections(finer(TRUNK, 3), 24, TAR, trunk, cap0=True, shape=ribs, fmat=hide)

# --- head: a long skull, a narrow muzzle, pricked ears; the lower jaw its own piece so the mouth can open --------
HEAD = [(0.56, 0.055, 0.63, 0.5, 0.9), (0.61, 0.068, 0.665, 0.5, 0.8), (0.67, 0.066, 0.655, 0.505, 0.8),
        (0.72, 0.05, 0.625, 0.51, 0.85), (0.78, 0.038, 0.6, 0.515, 0.9), (0.84, 0.03, 0.583, 0.52, 0.9),
        (0.875, 0.02, 0.57, 0.525, 1.0)]


def skull(i, j, a, p):
    q = Vector(p)
    # A heavy brow over sunk eyes, and the skin shrunk back off the muzzle.
    if 0.6 < q.y < 0.68 and q.z > 0.6:
        q.z += 0.01
    q.x *= 1 + 0.05 * noise3(q, 21, 20.0)
    return q


body.sections(finer(HEAD, 2), 16, TAR, "head", cap1=True, shape=skull)
body.sections([(0.62, 0.05, 0.515, 0.47, 0.9), (0.7, 0.045, 0.515, 0.48, 0.9), (0.78, 0.033, 0.515, 0.49, 0.9),
               (0.845, 0.022, 0.515, 0.497, 1.0)], 8, TAR, "jaw", cap1=True)
# Gums and teeth along both jaws: dead ivory in a black mouth, the one pale thing on it.
for sx in (-1, 1):
    for k in range(5):
        y = 0.69 + k * 0.035
        top = 0.519
        body.box((sx * (0.036 - k * 0.003), y, top - 0.012), (0.006, 0.008, 0.014 if k else 0.022), TEETH, "head",
                 taper=(0.4, 0.4), rot=rig.Matrix.Rotation(math.pi, 4, "X"))
        body.box((sx * (0.032 - k * 0.003), y + 0.012, 0.512), (0.005, 0.007, 0.011 if k else 0.018), TEETH, "jaw",
                 taper=(0.35, 0.35))
body.box((0, 0.73, 0.513), (0.034, 0.1, 0.006), GUMS, "jaw")
body.tube([(0, 0.66, 0.508), (0, 0.74, 0.505), (0, 0.8, 0.5)], [(0.02, 0.006)] * 3, 4, GUMS, "tongue", ref=(0, 0, 1),
          cap1=True)
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    # Ears: tall, thin, torn at the tip.
    body.slab([(sx * 0.03, 0.645, 0.635), (sx * 0.068, 0.62, 0.64), (sx * 0.075, 0.6, 0.755), (sx * 0.06, 0.607, 0.74)],
              0.012, TAR, f"ear_{s}", down=(0, -1, 0))
    # Eyes: two coals, deep in the sockets. The only warm point on the face.
    body.box((sx * 0.052, 0.665, 0.622), (0.008, 0.012, 0.006), EYE, "head")

# --- legs: thin as sticks, knobbed at the joints, paws splayed ------------------------------------------------
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * 0.1
    front = along("z", [(0.02, "finger_{s}"), (0.05, "hand_{s}"), (0.085, "lowerarm_{s}"), (0.22, "lowerarm_{s}"),
                        (0.26, "upperarm_{s}"), (0.34, "upperarm_{s}"), (0.4, "scapula_{s}")])
    fw = lambda p, f=front: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x * 0.85, 0.3, 0.47), (x, 0.335, 0.38), (x, 0.31, 0.3), (x * 1.02, 0.28, 0.245), (x, 0.285, 0.2),
               (x, 0.288, 0.13), (x, 0.29, 0.085), (x, 0.3, 0.06), (x, 0.315, 0.035)],
              [(0.052, 0.07), 0.05, 0.038, 0.034, 0.026, 0.022, 0.024, 0.021, 0.019], 9, TAR, fw, ref=(0, 1, 0))
    rear = along("z", [(0.02, "toe_{s}"), (0.05, "foot_{s}"), (0.12, "foot_{s}"), (0.16, "calf_{s}"), (0.25, "calf_{s}"),
                       (0.31, "thigh_{s}"), (0.42, "thigh_{s}")])
    rw = lambda p, f=rear: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    xr = sx * 0.095
    body.tube([(xr * 0.95, -0.47, 0.47), (xr * 1.05, -0.43, 0.38), (xr * 1.05, -0.39, 0.3), (xr, -0.4, 0.26),
               (xr, -0.46, 0.2), (xr, -0.52, 0.145), (xr, -0.518, 0.1), (xr, -0.505, 0.05), (xr, -0.49, 0.03)],
              [(0.06, 0.075), 0.062, 0.042, 0.032, 0.027, 0.026, 0.02, 0.02, 0.019], 9, TAR, rw, ref=(0, 1, 0))
    # Paws: long splayed toes, the claws gone to slag.
    for y0, bone, root in ((0.35, f"finger_{s}", f"hand_{s}"), (-0.455, f"toe_{s}", f"foot_{s}")):
        body.box((x * (0.98 if y0 > 0 else 0.95), y0, 0.018), (0.028, 0.04, 0.016), TAR, {bone: 0.7, root: 0.3},
                 taper=(0.8, 0.7))
        for k in (-1, 0, 1):
            body.box((x * (0.98 if y0 > 0 else 0.95) + k * 0.016, y0 + 0.045, 0.008), (0.005, 0.014, 0.007), SLAG, bone,
                     taper=(0.5, 0.3))

# --- tail: thin, ragged, carried low -------------------------------------------------------------------------
tail_pts = [(0, -0.64, 0.47), (0, -0.71, 0.44), (0, -0.79, 0.39), (0, -0.86, 0.33), (0, -0.92, 0.27),
            (0, -0.97, 0.21), (0, -1.0, 0.16)]
body.tube(tail_pts, [0.042, 0.05, 0.046, 0.038, 0.03, 0.02, 0.008], 8, TAR,
          along("y", [(-0.98, "tail_05"), (-0.92, "tail_04"), (-0.85, "tail_03"), (-0.76, "tail_02"), (-0.66, "tail_01"),
                      (-0.6, "pelvis")]),
          ref=(0, 0, 1), cap1=True, shape=lambda i, j, a, p, fr: p + (p - Vector(tail_pts[i])) * 0.5 * noise3(p, 31, 40.0))

# --- cracks: jagged seams of fire across the ribs and down the back --------------------------------------------
cracks = kit.part("cracks", smooth=False)
for sx in (-1, 1):
    for k, (y0, z0, dy, dz, n) in enumerate([(-0.14, 0.47, 0.07, -0.02, 6), (0.02, 0.49, 0.06, -0.035, 6), (0.16, 0.5, 0.05, -0.04, 5),
                                              (-0.02, 0.4, 0.07, -0.01, 4), (0.26, 0.47, 0.03, -0.045, 4), (-0.5, 0.49, 0.05, -0.03, 3)]):
        pts = []
        y, z = y0, z0
        for i in range(n):
            # Wander: each step jinks by a fixed hash, so the cracks look torn, not drawn.
            j = noise3(Vector((sx * 3 + k, i, 0)), 131, 1.7)
            y += dy + 0.02 * j
            z += dz + 0.025 * noise3(Vector((sx * 5 + k, i, 1)), 132, 1.9)
            # On the hide: just outside the barrel's half width there.
            hw = max((w for (yy, w, *_rest) in TRUNK if yy <= y), default=0.08)
            nxt = [w for (yy, w, *_rest) in TRUNK if yy > y]
            hw = (hw + (nxt[0] if nxt else hw)) / 2
            pts.append(Vector((sx * (hw * (0.95 + 0.3 * max(0.0, 0.47 - z) / 0.2) + 0.006), y, z)))
        cracks.tube(pts, [0.009 if i % 2 else 0.006 for i in range(len(pts))], 4, CORE, trunk, ref=(sx, 0, 0), cap0="point",
                    cap1="point")
spine_crack = [Vector((0.006 * noise3(Vector((y, 0, 0)), 133, 20.0), y, 0.537 + (0.02 if 0.15 < y < 0.35 else 0.0))) for y in
               [-0.4 + 0.07 * i for i in range(10)]]
cracks.tube(spine_crack, [0.008] * len(spine_crack), 4, CORE, trunk, ref=(0, 0, 1), cap0="point", cap1="point")

# --- slag: a ragged crest down the spine, crust on the shoulders and haunches, a lump fused on the skull --------
crest = kit.part("slag", smooth=False)
CREST = [(-0.52, 0.035, "pelvis"), (-0.44, 0.05, "pelvis"), (-0.36, 0.045, "crest_01"), (-0.28, 0.07, "crest_01"),
         (-0.2, 0.05, "crest_01"), (-0.12, 0.06, "crest_02"), (-0.04, 0.09, "crest_02"), (0.04, 0.075, "crest_02"),
         (0.12, 0.06, "crest_03"), (0.2, 0.1, "crest_03"), (0.27, 0.085, "crest_03"), (0.33, 0.06, "chest"),
         (0.4, 0.045, "neck_01")]
for k, (y, h, bone) in enumerate(CREST):
    top = 0.53 + (0.03 if 0.15 < y < 0.35 else 0.0) + (0.02 if y > 0.35 else 0.0)
    dx = 0.012 * noise3(Vector((y, 0, 0)), 41, 13.0)
    crest.rock((dx, y, top + h * 0.4), (0.022 + h * 0.15, 0.03, h * 0.6), SLAG, bone, seed=100 + k, spike=1.4,
               rot=rig.Matrix.Rotation(0.3 * noise3(Vector((y, 1, 0)), 42, 9.0), 4, "Y"))
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    for k, (y, z, r) in enumerate([(0.3, 0.47, 0.05), (0.26, 0.42, 0.035), (0.34, 0.43, 0.03)]):
        crest.rock((sx * 0.1, y, z), (0.03, r, r * 0.9), SLAG, f"scapula_{s}", seed=200 + k + (10 if sx > 0 else 0))
    for k, (y, z, r) in enumerate([(-0.5, 0.46, 0.05), (-0.45, 0.4, 0.035), (-0.55, 0.42, 0.03)]):
        crest.rock((sx * 0.1, y, z), (0.03, r, r * 0.9), SLAG, f"thigh_{s}", seed=300 + k + (10 if sx > 0 else 0))
crest.rock((0.03, 0.6, 0.66), (0.035, 0.04, 0.03), SLAG, "head", seed=400, spike=1.2)
crest.rock((-0.04, -0.66, 0.46), (0.03, 0.03, 0.035), SLAG, "tail_01", seed=401)

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
    "run": {"duty": 0.32, "reach": {"rear": (0.2, -0.2, 0.15), "front": (0.17, -0.12, 0.19)},
            "pitch": {"foot": {"stance": (-78, -58), "swing": (-58, -96, -80)},
                      "toe": {"stance": (-6, -2), "swing": (-50, -110, -20)},
                      "hand": {"stance": (-74, -44), "swing": (-44, -190, -78)},
                      "finger": {"stance": (-6, -4), "swing": (-60, -250, -40)}}},
    # The prowl: a stalking walk, each paw set down with care, low and short.
    "walk": {"duty": 0.68, "reach": {"rear": (0.12, -0.13, 0.06), "front": (0.13, -0.1, 0.08)},
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
    x = (0.1 if side == "r" else -0.1) * (0.95 if group == "rear" else 1.0) * spread
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
        "root@loc": (0, 0, -0.075 + 0.008 * math.cos(2 * w * (t - 0.1))),
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
CROUCH_BODY = posed(root__loc=(0, 0.0, -0.15), pelvis=(-8, 0, 0), spine_01=(8, 0, 0), spine_02=(4, 0, 0), spine_03=(-2, 0, 0),
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
    p = posed(root__loc=(0.01 * w / 30, -0.05 - back, -0.12), root=(0, 0, -0.2 * w), pelvis=(-14, 0, 0.3 * w),
             spine_01=(6, 0, 0.2 * w), spine_02=(4, 0, -0.2 * w), chest=(-6, 0, -0.3 * w),
             neck_01=(-14, 0, w * 0.5), neck_02=(-12, 0, w * 0.5), head=(10, w * 0.4, w * 0.3), jaw=(-8, 0, 0),
             ear_r=(-60, 0, 0), ear_l=(-60, 0, 0), tail_01=(-10, 0, -w), tail_02=(0, 0, -0.6 * w), tail_03=(0, 0, 0.5 * w),
             crest_02=(0, 0.3 * w, 0))
    return planted(p, {"front_r": (0.44, GROUND), "front_l": (0.44, GROUND), "rear_r": (-0.52 + 0.06 * slip, GROUND + 0.05 * slip, -50, -5),
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

kit.build()
# (No feet planter: every clip's paws are placed by IK.)
rig.bake(sk, [prowl, run, crouch, lunge, board, hit, bite])
rig.export(rig.args()[0] if rig.args() else "cinder_hound.glb", kit)
