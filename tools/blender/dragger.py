"""DRAGGER (T46, App. A.4, B.4): "reach up from beneath the car edges. RULE: stay off the edges."

What you see of one is a limb: an arm too long and too thin to be anyone's, the pale of something kept out of the
light (§26.1: the one light-coloured thing at a car's dark edge, so the reach reads in time), coming up just outside
the eave and hooking in over the roof, one joint more than an arm should have. The hand is long-fingered, the nails
black; it lays itself flat on the roof sheet and then grips. Below the lip it goes on down, out of sight: the rest
of it is under the car and nobody has seen it.

Pivot at the attach point (GreyboxScene: 0.35 m under the roof's top, just outside the car's side), the limb 0.14 m
further out, hooking toward +X (the model is turned for the car's other side). Chain rig: root, arm_01 (rising
out from under), arm_02 (over the lip), arm_03 (down onto the roof), hand, and four two-bone fingers.
Clips: reach (the telegraph: up over the lip in stepped pops, then down flat on the roof, once), grip (the grab:
fingers clenched, pulling, loop).

    blender -b --python tools/blender/dragger.py -- content/art/models/dragger.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()
X0 = -0.14   # the limb's line, outside the eave
ROOF = 0.35  # the roof's top above the pivot
FINGERS = [(-0.075, 1.05), (-0.025, 1.12), (0.025, 1.1), (0.075, 0.95)]  # (spread across, length)

wrist = Vector((0.4, 0, ROOF + 0.06))
knuckle = Vector((0.52, 0, ROOF + 0.035))
bones = [
    Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
    Bone("arm_01", "root", (X0, 0, -1.0), (X0, 0, 0.52)),
    Bone("arm_02", "arm_01", (X0, 0, 0.52), (0.14, 0, 0.6)),
    Bone("arm_03", "arm_02", (0.14, 0, 0.6), wrist),
    Bone("hand", "arm_03", wrist, knuckle),
]
for k, (dy, ln) in enumerate(FINGERS):
    a = knuckle + Vector((0, dy, 0))
    b = a + Vector((0.13 * ln, dy * 0.9, -0.02))
    c = b + Vector((0.12 * ln, dy * 0.6, -0.015))
    bones += [Bone(f"finger_{k + 1}_a", "hand", a, b), Bone(f"finger_{k + 1}_b", f"finger_{k + 1}_a", b, c)]
sk = Skeleton("SK_Chain", bones)
sk.build()
kit = rig.Kit(sk, "dragger")

# Pale, grey-blue in the veins; the knuckles and elbows raw; the nails tar-black and wet.
FLESH = Mat("flesh.pale", hexc("#b3aa9c"), shine=0.3, tint=(2.3, 2.95, 3.4))
RAW = Mat("flesh.raw", hexc("#7d6a60"), shine=0.35, tint=(1.5, 1.5, 1.6))
NAIL = Mat("tar.nail", hexc("#141110"), shine=0.6)
ARM = ["arm_01", "arm_02", "arm_03", "hand"]


def lumpy(scale, amount):
    """Knotted, not smooth: tendons standing out under skin pulled too tight."""
    def fn(i, j, a, p, frame):
        side, up, _ = frame
        out = side * math.sin(a) + up * math.cos(a)
        return p + out * (0.008 * amount * noise3(p, 61, scale))
    return fn


limb = kit.part("limb")
# The arm: three long bones and their knuckled joints, thinner than it has any right to be.
path = [Vector((X0, 0, -1.0)), Vector((X0, 0, -0.5)), Vector((X0 - 0.01, 0, 0.0)), Vector((X0, 0, 0.38)),
        Vector((X0 + 0.02, 0, 0.52)), Vector((0.0, 0, 0.6)), Vector((0.14, 0, 0.6)), Vector((0.26, 0, 0.52)),
        wrist]
radii = [0.06, 0.058, 0.052, 0.05, (0.064, 0.058), 0.046, (0.056, 0.052), 0.04, (0.036, 0.042)]
limb.tube(path, radii, 7, FLESH, (ARM, 6.0), ref=(0, 1, 0), shape=lumpy(9.0, 1.0))
# The joints are raw where they bend: swollen, split.
for c, r in ((path[4], 0.068), (path[6], 0.062)):
    limb.blob(c, (r, r * 0.9, r), 6, 3, RAW, (ARM, 6.0),
              shape=lambda i, j, a, th, p, c=c: c + (p - c) * (1 + 0.25 * noise3(p, 62, 30.0)))

# The hand: a narrow palm laid flat, too long, then the fingers splayed on the roof sheet.
hand = kit.part("hand")
palm = [wrist + Vector((-0.02, 0, 0.0)), wrist + Vector((0.05, 0, -0.012)), knuckle + Vector((0.01, 0, 0))]
hand.tube(palm, [(0.052, 0.022), (0.062, 0.02), (0.068, 0.018)], 6, FLESH, ["arm_03", "hand"], ref=(0, 0, 1),
          cap1=True, square=0.7)
for k, (dy, ln) in enumerate(FINGERS):
    fa, fb = sk[f"finger_{k + 1}_a"], sk[f"finger_{k + 1}_b"]
    pts = [fa.head, (fa.head + fa.tail) / 2, fa.tail, (fb.head + fb.tail) / 2, fb.tail]
    hand.tube(pts, [0.016, 0.015, 0.016, 0.012, 0.007], 5, FLESH,
              ([f"finger_{k + 1}_a", f"finger_{k + 1}_b", "hand"], 6.0), ref=(0, 0, 1), cap1="point")
    # A black nail at the tip, hooked down into the roof.
    tip = fb.tail
    d = (fb.tail - fb.head).normalized()
    hand.tube([tip - d * 0.03, tip + d * 0.012 + Vector((0, 0, -0.012))], [0.009, 0.002], 4, NAIL,
              f"finger_{k + 1}_b", ref=(0, 0, 1), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# Clips. The chain lies in the XZ plane: about Y, + tips a bone's far end down (toward -Z), - lifts it.
FIN_A = [f"finger_{k + 1}_a" for k in range(4)]
FIN_B = [f"finger_{k + 1}_b" for k in range(4)]


def fingers(curl, spread=0.0):
    """Fingers curled down by `curl` degrees (a grip digs the nails in), splayed wider by `spread`."""
    pose = {}
    for k, (a, b) in enumerate(zip(FIN_A, FIN_B)):
        s = (k - 1.5) * spread
        pose[a] = (0, curl * 0.6, s)
        pose[b] = (0, curl, 0)
    return pose


# Reach: from out of sight under the lip, up past the eave in three pops (held frames: GDD §31's "still, then
# abrupt"), the hook raised clear of the roof edge; then it comes down flat on the sheet, fingers spread, and holds.
# The telegraph's first second (App. A.1's window is 1.5 s): it's on the roof by 0.6 s, so the grab can't surprise.
hidden = {"root@loc": (0, 0, -0.75), "arm_02": (0, 40, 0), "arm_03": (0, 50, 0)} | fingers(40, -3)
reach = Clip("reach", loop=False)
reach.key(0, hidden, "CONSTANT")
reach.key(4, {"root@loc": (0, 0, -0.5), "arm_02": (0, 25, 0), "arm_03": (0, 45, 0)} | fingers(35), "CONSTANT")
reach.key(8, {"root@loc": (0, 0, -0.22), "arm_02": (0, 5, 0), "arm_03": (0, 30, 0)} | fingers(25, 2), "CONSTANT")
reach.key(12, {"root@loc": (0, 0, 0.06), "arm_02": (0, -12, 0), "arm_03": (0, 10, 0)} | fingers(10, 4), "LINEAR")
reach.key(16, {"root@loc": (0, 0, 0.02), "arm_02": (0, -3, 0), "arm_03": (0, -4, 0)} | fingers(-4, 6), "CONSTANT")
reach.key(18, {"root@loc": (0, 0, 0.0)} | fingers(-2, 5), "LINEAR")
reach.key(30, {"root@loc": (0, 0, 0.0), "arm_03": (0, 1, 0)} | fingers(0, 5), "LINEAR")

# Grip: clenched on the roof edge, pulling. It hauls in jerks, the arm shuddering between them.
grip = Clip("grip")
clench = fingers(38, 1)
for f, (dz, dx, tw) in enumerate(((0, 0, 0), (-0.03, -0.02, 2), (-0.03, -0.02, -1), (0.0, 0.0, 1),
                                   (-0.05, -0.03, -2), (-0.02, -0.01, 1))):
    pose = clench | {"root@loc": (dx, 0, dz), "arm_02": (0, 4 + tw, 0), "arm_03": (0, 6 - tw, 0), "hand": (0, 8, 0)}
    grip.key(f * 6, pose, "CONSTANT" if f % 2 else "LINEAR")
grip.close(36)

kit.build()
rig.bake(sk, [reach, grip])
rig.export(rig.args()[0] if rig.args() else "dragger.glb", kit)
