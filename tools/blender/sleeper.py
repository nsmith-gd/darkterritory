"""SLEEPER (GDD §21 forward, App. A.2 · vibration): "lie across the rail, shaped like ties. Invisible until lit."

One body, laid across the track where a railway tie would be, and at a glance it is one: 2.8 m long, 0.35 deep,
0.28 high, the brown of old creosoted timber (§26.1: it reads by shape; its shape is a lie). Closer, the "grain" is
skin stretched tight over bars of something like wood: ribs across its width, sagging between, a ridge of spine
along its top. At one end, pressed into the end grain, the ghost of a face; at the other, what's left of hands,
split into splinters. "You can still tell what they used to be" (§26.5): a person, stretched to a tie.

Pivot at the middle of its underside, lying along X (across the rail). Chain rig: root + seg_01..seg_10.
Clips: dormant (still, loop), writhe (the telegraph when the lamp finds it, loop), lift (arching up as the engine
comes on: braced, never fleeing).

    blender -b --python tools/blender/sleeper.py -- content/art/models/sleeper.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, noise3  # noqa: E402

rig.reset()
N = 10
L = 2.8
bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
for i in range(N):
    x0 = -L / 2 + L * i / N
    bones.append(Bone(f"seg_{i + 1:02d}", "root" if i == 0 else f"seg_{i:02d}", (x0, 0, 0.14), (x0 + L / N, 0, 0.14)))
sk = Skeleton("SK_Chain", bones)
sk.build()
kit = rig.Kit(sk, "sleeper")

# Tie-coloured: the wood reads as the sleeper texture; the skin is the flesh texture darkened to the same brown.
WOOD = Mat("wood_sleeper.ribs", hexc("#3a2e26"), shine=0.05, tint=(0.62, 0.56, 0.5))
SKIN = Mat("flesh.stretched", hexc("#3a2c24"), shine=0.25, tint=(0.36, 0.29, 0.24))
TAR = Mat("tar.sleeper", hexc("#141110"), shine=0.3)

def chain(p):
    """Weights down the chain by x: each vertex between the two nearest bone middles."""
    u = (p.x + L / 2) / (L / N) - 0.5
    i = max(0, min(N - 1, math.floor(u)))
    t = max(0.0, min(1.0, u - i))
    if u < 0:
        return {"seg_01": 1.0}
    if i >= N - 1:
        return {f"seg_{N:02d}": 1.0}
    return {k: v for k, v in ((f"seg_{i + 1:02d}", 1 - t), (f"seg_{i + 2:02d}", t)) if v > 0.02}


RIB = 0.2  # rib spacing along the body


def rib(x):
    """1 on a rib, 0 midway between: the bars the skin is stretched over."""
    d = abs(((x + L / 2) % RIB) - RIB / 2) / (RIB / 2)
    return max(0.0, (d - 0.72) / 0.28)


body = kit.part("body")
secs = []
xs = []
x = -L / 2 + 0.02
while x < L / 2 - 0.02 + 1e-9:
    xs.append(x)
    x += RIB / 5
for x in xs:
    r = rib(x)
    end = min(1.0, (L / 2 - abs(x)) / 0.05)  # sawn-square ends, barely rounded
    # A tie's box section; the skin between the ribs sags only a little: it has to pass for timber in a lamp beam.
    hw = (0.166 + 0.009 * r) * (0.9 + 0.1 * end)
    top = (0.262 + 0.018 * r) * (0.92 + 0.08 * end)
    secs.append((x, hw, top, 0.0, 0.22))


def lumps(i, j, a, p):
    q = Vector(p)
    # Warped like an old tie; the skin puckers.
    q.z += 0.01 * noise3(q, 51, 3.0) * (q.z / 0.28)
    q.y += 0.004 * noise3(q, 52, 7.0)
    # The spine along the top: a low ridge, like a split in old timber.
    if math.cos(a) > 0.97:
        q.z += 0.008
    return q


def skin_or_wood(pts, n):
    c = sum(pts, Vector()) / len(pts)
    if n.z < -0.6:
        return TAR
    # The top and the ribs are timber-coloured bars; skin shows in the gaps down the sides.
    return WOOD if rib(c.x) > 0.25 or n.z > 0.8 and noise3(c, 54, 5.0) > -0.3 else SKIN


body.sections(secs, 16, SKIN, chain, axis="x", cap0=True, cap1=True, shape=lumps, fmat=skin_or_wood)

# The face at the +X end: brow, sockets, a jaw pressed flat into the end grain.
face = Vector((L / 2 - 0.05, 0, 0.14))
body.blob(face, (0.06, 0.13, 0.1), 8, 5, SKIN, f"seg_{N:02d}",
          shape=lambda i, j, a, th, p: p + Vector((0.012 * noise3(p, 53, 18.0), 0, 0)))
for sy in (-1, 1):
    body.blob(face + Vector((0.045, sy * 0.045, 0.02)), (0.018, 0.025, 0.018), 6, 3, TAR, f"seg_{N:02d}")
body.box(face + Vector((0.05, 0, -0.05)), (0.02, 0.06, 0.012), TAR, f"seg_{N:02d}")

# The hands at the -X end: fingers split into splinters, fused, splayed on the ballast.
for k, (dy, ang) in enumerate(((-0.12, -0.5), (-0.05, -0.2), (0.03, 0.15), (0.11, 0.45))):
    base = Vector((-L / 2 + 0.04, dy, 0.07))
    tip = base + Vector((-0.2 * math.cos(ang), 0.2 * math.sin(ang) + dy * 0.3, -0.05))
    mid = (base + tip) / 2 + Vector((0, 0, 0.02))
    body.tube([base, mid, tip], [0.028, 0.02, 0.006], 5, WOOD, "seg_01", ref=(0, 0, 1), cap1=True)

# --------------------------------------------------------------------------------------------------------------
# Clips. Bones point +X; about Y, + tips a segment's far end down (- lifts it), about Z, + swings it toward +Y.
seg = [f"seg_{i + 1:02d}" for i in range(N)]

dormant = Clip("dormant")
dormant.key(0, {}, "CONSTANT")
dormant.key(60, {}, "CONSTANT")

# Writhe: a slow wave through it, out of step side to side and up and down, with a catch (a held frame) each half
# cycle: not a snake's even ripple, something that's forgotten how to move.
writhe = Clip("writhe")
for f in range(0, 48, 3):
    t = f / 48
    pose = {}
    for i, n in enumerate(seg):
        ph = 2 * math.pi * (t * 2 - i / N * 1.5)
        pose[n] = (0, 5 * math.sin(ph) * (0.4 if i in (0, N - 1) else 1.0), 3.5 * math.sin(ph * 0.5 + i))
    pose["root@loc"] = (0, 0, 0.03 + 0.02 * math.sin(2 * math.pi * t * 2))
    writhe.key(f, pose, "CONSTANT" if f % 12 == 9 else "LINEAR")
writhe.close(48)

# Lift: the middle arches up off the ballast, the ends dug in, braced for the engine. It holds.
# First segment rises at 16 degrees; each after bends 32/9 back down, so the far end comes back to the ground.
ARCH = [-9.0] + [18.0 / 9] * 9
lift = Clip("lift", loop=False)
lift.key(0, {}, "LINEAR")
lift.key(4, {n: (0, a * 0.6, 0) for n, a in zip(seg, ARCH)} | {"root@loc": (0, 0, 0.02)}, "CONSTANT")
lift.key(7, {n: (0, a, 2 * math.sin(i)) for i, (n, a) in enumerate(zip(seg, ARCH))} | {"root@loc": (0, 0, 0.03)}, "LINEAR")
lift.key(16, {n: (0, a * 1.1, -2 * math.sin(i)) for i, (n, a) in enumerate(zip(seg, ARCH))} | {"root@loc": (0, 0, 0.04)},
         "LINEAR")
lift.key(24, {n: (0, a * 1.05, 0) for n, a in zip(seg, ARCH)} | {"root@loc": (0, 0, 0.04)}, "LINEAR")

kit.build()
rig.bake(sk, [dormant, writhe, lift])
rig.export(rig.args()[0] if rig.args() else "sleeper.glb", kit)
