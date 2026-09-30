"""THE WEIGHT (T59, App. A.3, B.3): "takes hold of the last car and drags. Speed bleeds off." It waits buried at the
water crossings, low ground, the marsh: so what comes up out of it is a bog body. Several, gone into one: a heap of
drowned men the peat has tanned brown-black and pressed together, their backs and shoulders breaking the surface of
it, faces sunk in the mass and looking up. The arms that are still arms have hold of the car: two hooked over the
coupler, two over the end beam at its corners, fingers dug in; two more and the legs trail behind it on the ballast,
clawing. Wet: it shines where the lantern finds it.

Frame (GreyboxScene: the Weight's Local): the pivot 0.35 m above the rail tops and 0.4 m behind the rear car's end
beam, where the coupler's head is; the car is ahead (+Y here, -Z in the engine), the rails 0.35 m below. Below the
gun's arc, as the GDD has it. SK_Chain: root, mass_01 (the front of the heap, under the arms), mass_02 (the trailing
half), three heads, and six arms of arm_01, arm_02, hand and a grip bone for the fingers.
Clips: grab (the telegraph: slapped on and clenched, once), drag (hauling on the car in heaves, loop), release (it
lets go and slumps back, once).

    blender -b --python tools/blender/weight.py -- content/art/models/weight.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()
GROUND = -0.4  # the sleepers' tops under it

# The arms: (name, shoulder, elbow, wrist, the way the hand's fingers hook). The front four hold the car: over the
# coupler's head and its shank, and over the end beam out at its corners. The last two trail behind, hands on the stones.
ARMS = [
    ("a", (0.19, -0.26, 0.28), (0.3, -0.1, 0.62), (0.1, 0.02, 0.66), (0.0, 0.3, -0.6)),
    ("b", (-0.2, -0.24, 0.28), (-0.34, -0.05, 0.58), (-0.12, 0.05, 0.64), (0.0, 0.3, -0.6)),
    ("c", (0.66, -0.6, 0.12), (0.8, -0.1, 0.4), (0.78, 0.36, 0.78), (0.0, 0.4, -0.6)),
    ("d", (-0.64, -0.5, 0.14), (-0.84, -0.06, 0.36), (-0.8, 0.36, 0.74), (0.0, 0.4, -0.6)),
    ("e", (0.46, -1.3, -0.06), (0.7, -1.7, -0.2), (0.76, -2.12, GROUND + 0.05), (0.15, -0.3, -0.3)),
    ("f", (-0.44, -1.45, -0.08), (-0.64, -1.9, -0.24), (-0.54, -2.36, GROUND + 0.05), (-0.1, -0.3, -0.3)),
]
# The bodies in it: (hips, shoulders, which way the back faces). Face-down along the middle under the arms that hold
# the coupler; rolled on its side on the left flank; face-down and twisted on the right, its head gone into the heap;
# and the last on its back in the trailing half, face up, dragged by the others with its legs out behind.
TORSOS = [
    ((0.04, -1.0, 0.1), (0.0, -0.25, 0.28), (0, 0.2, 1)),
    ((-0.22, -1.2, 0.0), (-0.52, -0.52, 0.14), (-0.6, 0, 0.8)),
    ((0.3, -1.4, -0.04), (0.52, -0.62, 0.12), (0.5, 0, 0.85)),
    ((0.02, -1.95, -0.2), (0.1, -1.25, 0.04), (0, 0, 1)),
]
HEADS = [
    # (centre, the way it faces, tilt: how far it's sunk). One under the hooked arms looking up at the car; one on the
    # heap's flank, turned out at whoever's on the platform; one in the trailing half, face up, mouth open.
    ((0.0, -0.04, 0.4), (0.1, 0.6, 0.8)),
    ((-0.64, -0.32, 0.26), (-0.8, 0.3, 0.5)),
    ((0.13, -1.06, 0.14), (0.1, 0.3, 1.0)),
]

bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
         Bone("mass_01", "root", (0, -0.9, 0.0), (0, 0.0, 0.15)),
         Bone("mass_02", "mass_01", (0, -0.9, 0.0), (0, -2.0, -0.2))]
for k, (c, d) in enumerate(HEADS):
    c = Vector(c)
    bones.append(Bone(f"head_{k + 1}", "mass_01" if k < 2 else "mass_02", c - Vector(d).normalized() * 0.08, c))
for n, s, e, w, hook in ARMS:
    parent = "mass_01" if n in "abcd" else "mass_02"
    s, e, w, hook = Vector(s), Vector(e), Vector(w), Vector(hook)
    k = w + hook.normalized() * 0.1
    bones += [Bone(f"arm_{n}_01", parent, s, e), Bone(f"arm_{n}_02", f"arm_{n}_01", e, w),
              Bone(f"hand_{n}", f"arm_{n}_02", w, k), Bone(f"grip_{n}", f"hand_{n}", k, k + hook.normalized() * 0.09)]
sk = Skeleton("SK_Chain", bones)
sk.build()
kit = rig.Kit(sk, "weight")

# Peat-tanned: brown-black, leathery, wet; the palms and the faces a shade paler where the skin's thinner; tar where
# eyes and mouths were.
HIDE = Mat("flesh.bog", hexc("#4a3a2b"), shine=0.5, tint=(1.2, 1.05, 0.85))
PALE = Mat("flesh.bog_pale", hexc("#6b5845"), shine=0.45, tint=(1.3, 1.15, 0.95))
TAR = Mat("tar.hole", hexc("#0b0908"), shine=0.7)
MASS = ["mass_01", "mass_02"]


def sodden(amount, scale=7.0, seed=71):
    """Swollen and uneven: the heap's skin pushed out in lumps, sagging toward the ground."""
    def fn(i, j, a, th, p):
        q = Vector(p)
        q += (q - Vector((0, q.y, -0.1))).normalized() * (0.05 * amount * noise3(q, seed, scale))
        q.z = max(q.z - 0.02 * amount * (1 + noise3(q, seed + 1, 3.0)), GROUND - 0.04)
        return q
    return fn


# ----------------------------------------------------------------------------------------------------------------
# The heap: the front half under the arms, the trailing half dragged out behind, and backs and shoulders of the
# bodies in it breaking its skin.
heap = kit.part("heap")
# Under them, what they've gone into: one sodden mass on the stones, its poles buried fore and aft.
heap.blob((0, -0.95, -0.2), (0.62, 0.24, 1.0), 20, 12, HIDE, (MASS, 4.0), rot=rig.rot(90, 0, 0).to_matrix().to_4x4(),
          shape=sodden(1.0))
for k, (hips, sh, up) in enumerate(TORSOS):
    # A torso: hips, waist, the chest's barrel, the shoulders' yoke, the neck; wide and flat, the back its top.
    hips, sh, up = Vector(hips), Vector(sh), Vector(up).normalized()
    ax = sh - hips
    pts = [hips - ax * 0.12, hips, hips + ax * 0.32, hips + ax * 0.62, sh, sh + ax.normalized() * 0.12]
    radii = [(0.12, 0.08), (0.17, 0.12), (0.14, 0.1), (0.19, 0.13), (0.22, 0.1), (0.065, 0.06)]
    if k == 2:
        radii[-1] = (0.08, 0.07)  # the neck goes on into the heap: no head
    b = "mass_02" if k == 3 else ["mass_01", "mass_02"]
    heap.tube(pts, radii, 12, HIDE, (b, 4.0) if isinstance(b, list) else b, ref=tuple(up), cap0=True, cap1=True, square=0.85,
              shape=lambda i, j, a, p, fr, s=80 + k: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.012 * noise3(Vector(p), s, 8.0))
# The faces: skulls with the skin shrunk tight over them, sunk to the ears in the heap; black holes for the eyes and a
# mouth dropped open.
faces = kit.part("faces")
for k, (c, d) in enumerate(HEADS):
    c, d = Vector(c), Vector(d).normalized()
    b = f"head_{k + 1}"
    q = d.to_track_quat("Z", "Y")
    R = q.to_matrix().to_4x4()
    up = q @ Vector((0, 1, 0))
    faces.blob(c, (0.082, 0.1, 0.09), 14, 8, PALE, b, rot=R, z0=-0.5,
               shape=lambda i, j, a, th, p, c=c, d=d: Vector(p) - d * 0.012 * max(0, noise3(Vector(p), 110, 40.0)))
    face = c + d * 0.06
    side = d.cross(up).normalized()
    for sx in (-1, 1):
        faces.blob(face + side * (sx * 0.032) + up * 0.022, (0.018, 0.016, 0.01), 6, 3, TAR, b, rot=R)
    faces.blob(face - up * 0.042 + d * 0.004, (0.022, 0.03 if k == 2 else 0.018, 0.012), 7, 3, TAR, b, rot=R)
    # The nose gone to a ridge; the brow heavy over the holes; the cheekbones standing out over the hollows.
    faces.blob(face + d * 0.018, (0.011, 0.02, 0.014), 5, 3, PALE, b, rot=R)
    faces.blob(face + up * 0.045 + d * 0.004, (0.07, 0.016, 0.02), 8, 3, PALE, b, rot=R)
    for sx in (-1, 1):
        faces.blob(face + side * (sx * 0.05) - up * 0.004 - d * 0.004, (0.018, 0.014, 0.016), 5, 3, PALE, b, rot=R)

# The arms: bloated, long, the skin loose on them, hands swollen, the fingers hooked over steel.
arms = kit.part("arms")
for n, s, e, w, hook in ARMS:
    s, e, w, hook = Vector(s), Vector(e), Vector(w), Vector(hook).normalized()
    chain = [f"arm_{n}_01", f"arm_{n}_02", f"hand_{n}"]
    trailing = n in "ef"
    r0 = 0.078 if not trailing else 0.066
    path = [s - (e - s).normalized() * 0.12, s, s.lerp(e, 0.5), e, e.lerp(w, 0.5), w]
    arms.tube(path, [r0 * 1.5, r0, r0 * 0.9, (r0 * 0.95, r0 * 0.85), r0 * 0.72, (r0 * 0.66, r0 * 0.5)], 8, HIDE,
              (chain, 6.0), ref=(0, 0, 1), cap0=True, shape=lambda i, j, a, p, fr, n=n: Vector(p) + fr[0] * 0.01 * noise3(Vector(p), 120 + ord(n), 14.0))
    # The palm over the steel, then four fingers hooked down past its edge.
    k = w + hook * 0.1
    side = hook.cross(Vector((0, 0, 1)) if abs(hook.z) < 0.9 else Vector((1, 0, 0))).normalized()
    arms.tube([w, w.lerp(k, 0.5), k], [(0.05, 0.026), (0.058, 0.024), (0.056, 0.022)], 6, PALE, ([f"hand_{n}"], 6.0),
              ref=(0, 0, 1), cap1=True, square=0.7)
    for f in range(4):
        o = side * (f - 1.5) * 0.026
        a = k + o
        tip_dir = (hook + Vector((0, 0, -0.9 if not trailing else -0.3))).normalized()
        b = a + hook * 0.045
        c2 = b + tip_dir * 0.05
        t = c2 + tip_dir * 0.03 + Vector((0, 0, -0.02))
        arms.tube([a, b, c2, t], [0.017, 0.016, 0.013, 0.006], 5, PALE, ([f"hand_{n}", f"grip_{n}"], 6.0), ref=(0, 0, 1),
                  cap1="point")
    # A thumb round the other side.
    th = k - side * 0.05 - hook * 0.03
    arms.tube([th, th + (hook - Vector((0, 0, 0.5))).normalized() * 0.05], [0.018, 0.008], 5, PALE, f"grip_{n}",
              ref=(0, 0, 1), cap1="point")

# The legs trailing: two, fused at the hip into the tail of the heap, feet dragging on the stones.
legs = kit.part("legs")
for sx, seed in ((-1, 130), (1, 131)):
    pts = [Vector((sx * 0.1, -2.0, -0.24)), Vector((sx * 0.2, -2.4, -0.3)), Vector((sx * 0.22, -2.8, GROUND + 0.06)),
           Vector((sx * 0.24, -3.05, GROUND + 0.04))]
    legs.tube(pts, [0.1, 0.075, 0.055, 0.05], 8, HIDE, "mass_02", ref=(0, 0, 1),
              shape=lambda i, j, a, p, fr, s=seed: Vector(p) + fr[0] * 0.012 * noise3(Vector(p), s, 12.0))
    legs.blob(pts[-1] + Vector((0, -0.08, -0.01)), (0.045, 0.1, 0.035), 7, 4, PALE, "mass_02")

# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles are the armature's axes (rig.rot): +X tips a bone's far end up, about Y leans it sideways.
HOLD = "abcd"


def pull(k):
    """The holding arms at `k` of their haul: 0 slack, 1 hauled in (the elbows bent, the heap drawn up to the car)."""
    pose = {"root@loc": (0, 0.2 * k, 0.07 * k), "mass_01": (11 * k, 0, 0), "mass_02": (-8 * k, 0, 0)}
    for n in HOLD:
        s = 1 if n in "ac" else -1
        pose[f"arm_{n}_01"] = (-20 * k, s * 7 * k, 0)
        pose[f"arm_{n}_02"] = (34 * k, 0, 0)
        pose[f"grip_{n}"] = (-30 - 10 * k, 0, 0)
    return pose


def claw(phase):
    """The trailing arms clawing the ballast: reach back, dig in, drag forward."""
    pose = {}
    for n, off in (("e", 0.0), ("f", 0.5)):
        a = math.sin(2 * math.pi * (phase + off))
        pose[f"arm_{n}_01"] = (6 * a, 0, 8 * a)
        pose[f"arm_{n}_02"] = (-8 * max(0, a), 0, 0)
        pose[f"grip_{n}"] = (-40 * max(0, -a), 0, 0)
    return pose


def heads(k, loll):
    return {"head_1": (-8 * k, 0, 6 * loll), "head_2": (4 * loll, 0, -12 * loll), "head_3": (10 * k, 0, 0)}


# Grab: it comes up off the stones with the arms flung forward, slaps onto the steel, and clenches (App. A.1's window:
# it has hold by 0.5 s).
slack = {"root@loc": (0, -0.35, -0.12), "mass_01": (-8, 0, 0)}
for n in HOLD:
    slack |= {f"arm_{n}_01": (-35, 0, 0), f"arm_{n}_02": (-20, 0, 0), f"grip_{n}": (40, 0, 0)}
grab = Clip("grab", loop=False)
grab.key(0, slack | claw(0.25) | heads(0, 0), "CONSTANT")
grab.key(5, {"root@loc": (0, -0.2, -0.05), "mass_01": (-4, 0, 0)}
         | {f"arm_{n}_01": (-12, 0, 0) for n in HOLD} | {f"grip_{n}": (30, 0, 0) for n in HOLD} | claw(0.3), "CONSTANT")
grab.key(9, pull(0.0) | {f"grip_{n}": (10, 0, 0) for n in HOLD} | claw(0.4) | heads(0.2, 0.3), "LINEAR")
grab.key(12, pull(0.15) | claw(0.5) | heads(0.5, 0.5), "CONSTANT")
grab.key(20, pull(0.0) | claw(0.6) | heads(0.2, 0.2), "BEZIER")

# Drag: it hauls on the car in heaves, uneven (a big one, two small, a shudder), the heads lolling with each, the arms
# behind clawing at the stones all the while.
drag = Clip("drag")
for f, (k, loll) in enumerate(((0.0, 0.2), (0.9, -0.4), (0.7, -0.2), (0.2, 0.3), (0.45, 0.1), (0.3, -0.1), (0.5, 0.2), (0.1, 0.0))):
    drag.key(f * 8, pull(k) | claw(f / 8) | heads(k, loll), "CONSTANT" if f in (1, 4) else "BEZIER")
drag.close(64)

# Release: the fingers open, the arms slide off the steel and it slumps back onto the stones, still.
release = Clip("release", loop=False)
release.key(0, pull(0.3) | claw(0.0) | heads(0.3, 0.0), "LINEAR")
release.key(6, pull(0.0) | {f"grip_{n}": (35, 0, 0) for n in HOLD} | heads(0, 0.3), "CONSTANT")
release.key(10, slack | heads(0, 0.6), "BEZIER")
release.key(24, slack | {"root@loc": (0, -0.5, -0.16)} | heads(-0.3, 0.8), "BEZIER")

kit.build()
rig.bake(sk, [grab, drag, release])
print("[dt] weight", {p.name: p.tris() for p in kit.parts}, "bones", len(bones))
rig.export(rig.args()[0] if rig.args() else "weight.glb", kit)
