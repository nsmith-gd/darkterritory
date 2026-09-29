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


def curve(keys, t):
    """A piecewise-linear cycle, t in 0..1 (wraps)."""
    t %= 1.0
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / max(t1 - t0, 1e-9)
            return v0 + (v1 - v0) * u
    return keys[-1][1]


def legs(front, rear, phases, stride=1.0):
    """A gait from per-joint cycles: front {joint: keys}, rear likewise; phases {rear_r, rear_l, front_r, front_l}."""
    def pose(t):
        out = {}
        for side in ("r", "l"):
            for group, table in (("front", front), ("rear", rear)):
                ph = t + phases[f"{group}_{side}"]
                for joint, keys in table.items():
                    out[f"{joint}_{side}"] = (curve(keys, ph) * stride, 0, 0)
        return out
    return pose


# Gallop (rotary): 16 frames. Hind legs reach under together-ish, front legs follow half a stride on.
GALLOP_FRONT = {
    "upperarm": [(0, 45), (0.25, 10), (0.5, -45), (0.72, -20), (1, 45)],
    "lowerarm": [(0, 20), (0.25, 0), (0.5, 5), (0.72, 70), (1, 20)],
    "hand": [(0, 10), (0.25, -10), (0.5, -60), (0.72, -40), (1, 10)],
}
GALLOP_REAR = {
    "thigh": [(0, 50), (0.3, 5), (0.55, -45), (0.8, 5), (1, 50)],
    "calf": [(0, -50), (0.3, -20), (0.55, 20), (0.8, -60), (1, -50)],
    "foot": [(0, 40), (0.3, 20), (0.55, 30), (0.8, 55), (1, 40)],
    "toe": [(0, 10), (0.3, -10), (0.55, -40), (0.8, 0), (1, 10)],
}
gallop = legs(GALLOP_FRONT, GALLOP_REAR, {"rear_r": 0.0, "rear_l": 0.07, "front_r": 0.5, "front_l": 0.58})
run = Clip("run")
for f in range(0, 16, 2):
    t = f / 16
    p = gallop(t)
    arch = math.sin(2 * math.pi * t)  # +: back arched (gathered), -: stretched out
    p.update({
        "root@loc": (0, 0, 0.03 * math.sin(2 * math.pi * t + 1.2) - 0.02),
        "pelvis": (8 * arch, 0, 0), "spine_01": (6 * arch, 0, 0), "spine_02": (-2 * arch, 0, 0),
        "spine_03": (-6 * arch, 0, 0), "chest": (-6 * arch, 0, 0),
        "neck_01": (-12 + 5 * arch, 0, 0), "neck_02": (-4, 0, 0), "head": (6 - 6 * arch, 0, 0),
        "jaw": (-14 - 6 * max(0.0, arch), 0, 0), "tongue": (-10, 0, 0),
        "ear_r": (-40, 0, 0), "ear_l": (-40, 0, 0),
        "tail_01": (-15 + 10 * arch, 0, 0), "tail_02": (8, 0, 0), "tail_03": (6 * arch, 0, 0), "tail_04": (6, 0, 0),
        "belly": (6 * arch, 0, 0),
        "crest_02": (4 * arch, 0, 0),
    })
    run.key(f, p, "LINEAR")
run.close(16)

# Prowl: a slinking walk, head below the shoulders, 48 frames; it stops dead mid-stride, head snaps to one side, holds,
# and snaps back (CONSTANT), then carries on as if it hadn't. That hitch is the whole creature.
WALK_FRONT = {
    "upperarm": [(0, 25), (0.5, -25), (0.62, -30), (0.85, 10), (1, 25)],
    "lowerarm": [(0, 0), (0.5, 5), (0.62, 50), (0.85, 30), (1, 0)],
    "hand": [(0, 0), (0.5, -30), (0.62, -50), (0.85, 0), (1, 0)],
}
WALK_REAR = {
    "thigh": [(0, 25), (0.5, -20), (0.62, -15), (0.85, 30), (1, 25)],
    "calf": [(0, -35), (0.5, 0), (0.62, -40), (0.85, -50), (1, -35)],
    "foot": [(0, 30), (0.5, 20), (0.62, 50), (0.85, 40), (1, 30)],
}
walk = legs(WALK_FRONT, WALK_REAR, {"rear_r": 0.0, "front_r": 0.25, "rear_l": 0.5, "front_l": 0.75})
PROWL_BODY = {"root@loc": (0, 0, -0.07), "pelvis": (-4, 0, 0), "spine_02": (3, 0, 0), "chest": (-6, 0, 0),
              "neck_01": (-18, 0, 0), "neck_02": (-8, 0, 0), "head": (-6, 0, 0), "jaw": (-6, 0, 0),
              "ear_r": (-25, 0, -10), "ear_l": (-25, 0, 10), "tail_01": (12, 0, 0), "tail_02": (8, 0, 0),
              "tail_03": (4, 0, 0)}
prowl = Clip("prowl")
# (frame, gait phase, extra) - the gait advances 0..1 over the clip except through the stop.
beats = [(0, 0.0), (6, 0.125), (12, 0.25), (18, 0.375), (22, 0.375), (30, 0.375), (34, 0.5), (40, 0.75), (48, 1.0)]
for f, ph in beats:
    p = dict(PROWL_BODY)
    p.update(walk(ph))
    sway = math.sin(2 * math.pi * ph)
    p["spine_02"] = (3, 0, 4 * sway)
    p["chest"] = (-6, 0, -3 * sway)
    interp = "LINEAR"
    if 22 <= f <= 30:
        # The stop: frozen, then the head snaps round to look (a hold, then a pop) and back.
        p["neck_02"] = (-2, 0, 38 if f < 30 else 0)
        p["head"] = (6, 0, 12 if f < 30 else 0)
        p["ear_r"] = (10, 0, -20)
        p["ear_l"] = (10, 0, 20)
        interp = "CONSTANT"
    prowl.key(f, p, interp)
prowl.close(48)

# Crouch (telegraph): belly to the ground, shoulders up, head low and forward, tail stiff; it trembles.
CROUCH = over(PROWL_BODY, root__loc=(0, 0.02, -0.13), pelvis=(-10, 0, 0), spine_01=(6, 0, 0), spine_02=(4, 0, 0),
              chest=(-10, 0, 0), neck_01=(-6, 0, 0), neck_02=(-10, 0, 0), head=(4, 0, 0), jaw=(-22, 0, 0),
              tongue=(-8, 0, 0), ear_r=(-50, 0, 0), ear_l=(-50, 0, 0),
              tail_01=(-8, 0, 0), tail_02=(0, 0, 0), tail_03=(0, 0, 0))
CROUCH.update(mirror({"upperarm_r": (48, 0, 0), "lowerarm_r": (-40, 0, 0), "hand_r": (60, 0, 0), "finger_r": (-60, 0, 0),
                      "scapula_r": (-10, 0, 0),
                      "thigh_r": (65, 0, 0), "calf_r": (-75, 0, 0), "foot_r": (55, 0, 0), "toe_r": (-40, 0, 0)}))
crouch = Clip("crouch")
crouch.key(0, CROUCH, "CONSTANT")
for f, d in ((3, 1), (5, -1), (9, 1), (12, 0), (19, 1), (21, -1), (24, 0)):
    # Tremble: small, fast, out of step between the crest, the flanks and the head.
    crouch.key(f, over(CROUCH, belly=(4 * d, 0, 0), crest_01=(6 * d, 0, 0), crest_03=(-5 * d, 0, 3 * d),
                       head=(4 + 2 * d, 0, 2 * d), root__loc=(0.004 * d, 0.02, -0.13)), "CONSTANT")
crouch.close(30)

# Lunge (commit): uncoils in two frames, hangs stretched in the air with the jaws wide, lands.
LAUNCH = over(CROUCH, root__loc=(0, 0.12, 0.08), pelvis=(10, 0, 0), spine_01=(-4, 0, 0), spine_02=(0, 0, 0),
              chest=(6, 0, 0), neck_01=(8, 0, 0), neck_02=(4, 0, 0), head=(-4, 0, 0), jaw=(-40, 0, 0), tongue=(-20, 0, 0),
              tail_01=(10, 0, 0), tail_02=(6, 0, 0))
LAUNCH.update(mirror({"upperarm_r": (80, 0, 0), "lowerarm_r": (20, 0, 0), "hand_r": (-10, 0, 0), "finger_r": (-20, 0, 0),
                      "thigh_r": (-50, 0, 0), "calf_r": (30, 0, 0), "foot_r": (40, 0, 0), "toe_r": (-40, 0, 0)}))
AIR = over(LAUNCH, root__loc=(0, 0.3, 0.22), chest=(0, 0, 0), jaw=(-45, 0, 0))
LAND = over(CROUCH, root__loc=(0, 0.38, -0.08), jaw=(-30, 0, 0), head=(-10, 0, 0))
lunge = Clip("lunge", loop=False)
lunge.key(0, CROUCH, "LINEAR")
lunge.key(2, LAUNCH, "LINEAR")
lunge.key(9, AIR, "LINEAR")
lunge.key(14, LAND, "BEZIER")
lunge.key(20, over(LAND, jaw=(-20, 0, 0)), "BEZIER")

# Hit: snapped sideways by the round, a twitch through the crest, recovers too fast.
STAND = over(PROWL_BODY, root__loc=(0, 0, -0.02), neck_01=(-10, 0, 0), head=(0, 0, 0))
HIT = over(STAND, root__loc=(-0.06, 0.02, -0.06), pelvis=(0, 10, -10), spine_02=(0, 8, -12), chest=(0, 10, -14),
           neck_01=(4, 10, 30), head=(18, 0, 20), jaw=(-35, 0, 0), crest_02=(0, 20, 0), crest_03=(0, -20, 0),
           tail_01=(20, 0, 30))
HIT.update(mirror({"thigh_r": (-10, 8, 0), "calf_r": (-20, 0, 0), "upperarm_r": (-10, 0, 0), "lowerarm_r": (-30, 0, 0)}))
hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(1, HIT, "CONSTANT")
hit.key(5, over(HIT, crest_02=(0, -15, 0), chest=(0, 6, -8)), "LINEAR")
hit.key(9, over(STAND, head=(-6, 0, -10)), "CONSTANT")
hit.key(12, STAND, "CONSTANT")

kit.build()
rig.bake(sk, [prowl, run, crouch, lunge, hit],
         plant=rig.feet_planter(sk, bones=("hand_l", "hand_r", "foot_l", "foot_r", "finger_l", "finger_r", "toe_l", "toe_r"),
                                clips={"prowl", "crouch"}, lowest=0.014))
rig.export(rig.args()[0] if rig.args() else "cinder_hound.glb", kit)
