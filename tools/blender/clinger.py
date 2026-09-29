"""CLINGER (GDD §21 flank, App. A.4 · vibration): "latch onto cargo hulls and drill through. Audible, locatable."

A parasite pressed flat to a car's side: a swollen, bruised sac-body (1.6 m long, 1.1 m tall, 0.4 m proud of the
hull), pale sacs budding on it, glued to the plate with a skirt of black tar. Round it, fused limbs - arms, once,
several people's - grip the hull, fingers splayed on the rivets. At its lower front seam, where it meets the steel,
a mouth: a ring of lip around a segmented drill that bores into the car; the plate round the drill point heats as it
works (the one warm light on it, and the thing a crewman on the roof can see before he hears the scraping stop).
Silhouette (§26.1): from along the train, a blister on the car's flank with legs.

Model space: the hull is the plane x = 0 and the creature bulges to +X; its origin is the middle of its grip.
Clips: cling (a slow pulse, loop), drill (working: the drill ratchets round, the body shudders, loop), punish (it
bursts through: contracts, then drives into the hull).

    blender -b --python tools/blender/clinger.py -- content/art/models/clinger.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Matrix, Skeleton, Vector, hexc, noise3, over, smoothstep  # noqa: E402

rig.reset()

# Limbs: (name, root on the body, elbow, hand) in (x, y, z), all lying on the hull.
LIMBS = [
    ("limb_fu", (0.08, 0.55, 0.3), (0.06, 0.85, 0.5), (0.04, 1.05, 0.38)),
    ("limb_fd", (0.08, 0.5, -0.3), (0.06, 0.8, -0.52), (0.04, 1.02, -0.42)),
    ("limb_mu", (0.1, 0.05, 0.45), (0.07, 0.15, 0.72), (0.04, 0.02, 0.9)),
    ("limb_md", (0.1, -0.05, -0.45), (0.07, -0.2, -0.68), (0.04, -0.05, -0.86)),
    ("limb_bu", (0.08, -0.55, 0.28), (0.06, -0.86, 0.46), (0.04, -1.06, 0.32)),
    ("limb_bd", (0.08, -0.5, -0.3), (0.06, -0.82, -0.48), (0.04, -1.03, -0.36)),
]
DRILL = [(0.14, 0.6, -0.3), (0.12, 0.72, -0.37), (0.07, 0.81, -0.41), (0.0, 0.86, -0.43)]

b = [Bone("root", None, (0, 0, 0), (0.15, 0, 0)),
     Bone("body", "root", (0.02, 0, 0), (0.3, 0, 0)),
     Bone("sac_01", "body", (0.2, 0.25, 0.2), (0.34, 0.25, 0.2)),
     Bone("sac_02", "body", (0.2, -0.3, 0.1), (0.36, -0.3, 0.1)),
     Bone("sac_03", "body", (0.2, 0.05, -0.25), (0.33, 0.05, -0.25)),
     Bone("seam_l", "body", (0.3, 0.0, 0.35), (0.3, 0.0, -0.3)),
     Bone("seam_r", "body", (0.3, 0.12, 0.35), (0.3, 0.12, -0.3))]
for i in range(3):
    b.append(Bone(f"drill_0{i + 1}", "body" if i == 0 else f"drill_0{i}", DRILL[i], DRILL[i + 1]))
for name, a, e, h in LIMBS:
    tip = Vector(h) + (Vector(h) - Vector(e)).normalized() * 0.12
    # The limbs hang off the root, not the body: when the body drives into the hull they stay gripping the plate.
    b += [Bone(f"{name}_1", "root", a, e), Bone(f"{name}_2", f"{name}_1", e, h), Bone(f"{name}_3", f"{name}_2", h, tuple(tip))]
sk = Skeleton("SK_Clinger", b)
sk.build()
kit = rig.Kit(sk, "clinger")

FLESH = Mat("flesh.clinger", hexc("#6e5a52"), shine=0.3, tint=(0.85, 0.78, 0.8))
SAC = Mat("sac.clinger", hexc("#9a8c78"), shine=0.4)
TAR = Mat("tar.clinger", hexc("#141110"), shine=0.4)
CRUST = Mat("mineral_growth.drill", hexc("#302c30"), shine=0.3)
HOT = Mat("hot.drill", hexc("#d06020"), emissive=1.0)

body = kit.part("body")


def body_weights(p):
    w = {"body": 1.0}
    for n in ("sac_01", "sac_02", "sac_03"):
        bn = sk[n]
        d = (p - bn.head).length
        k = smoothstep(0.32, 0.08, d) * 0.8
        if k > 0:
            w = {x: v * (1 - k) for x, v in w.items()}
            w[n] = w.get(n, 0) + k
    if p.x > 0.24 and abs(p.y - 0.06) < 0.12 and abs(p.z) < 0.34:
        side = "seam_l" if p.y < 0.06 else "seam_r"
        k = smoothstep(0.12, 0.02, abs(p.y - 0.06)) * 0.7
        w = {x: v * (1 - k) for x, v in w.items()}
        w[side] = w.get(side, 0) + k
    t = sum(w.values())
    return {x: v / t for x, v in w.items()}


# The sac-body: an ellipsoid pressed flat, cut by the hull, swollen unevenly (heavier at the bottom, like it's
# filling), bruised: bands of lumps, a dent where the seam runs.
C = Vector((0.0, 0.0, 0.0))
toX = Matrix.Rotation(math.radians(90), 4, "Y")  # the blob's own +Z points out of the hull (+X)


def swell(i, j, a, th, p):
    q = Vector(p)
    out = max(0.0, q.x)
    k = 1 + 0.18 * noise3(q, 61, 3.2) + 0.08 * noise3(q, 62, 9.0)
    q.x = out * k * (1.15 if q.z < -0.1 else 1.0)
    q.y *= 1 + 0.05 * noise3(q, 63, 4.0)
    q.z *= 1 + 0.05 * noise3(q, 64, 4.0)
    if abs(q.y - 0.06) < 0.05 and q.x > 0.2 and abs(q.z) < 0.36:
        q.x -= 0.035 * (1 - abs(q.y - 0.06) / 0.05)  # the seam
    return q


body.blob(C, (0.55, 0.78, 0.36), 22, 11, FLESH, body_weights, rot=toX, shape=swell, z0=-0.05,
          fmat=lambda pts, n: TAR if (sum(pts, Vector()) / len(pts)).x < 0.06 else None)
# The tar skirt that glues it on: a flat, ragged ring on the hull round its base.
ring = []
for k in range(20):
    a = 2 * math.pi * k / 20
    r = 1 + 0.12 * noise3(Vector((math.cos(a), math.sin(a), 0)), 65, 3.0)
    ring.append((0.012, math.cos(a) * 0.86 * r, math.sin(a) * 0.6 * r))
body.tube(ring, [(0.03, 0.012)] * 20, 4, TAR, "body", ref=[Vector((1, 0, 0))] * 20, loop=True)
# The seam's lips: black, wet, running top to bottom down its back.
for sy, bone in ((0.035, "seam_l"), (0.085, "seam_r")):
    body.tube([(0.31, sy, 0.34), (0.345, sy, 0.1), (0.35, sy, -0.12), (0.33, sy, -0.3)], [0.012, 0.022, 0.022, 0.012], 6,
              TAR, bone, ref=(1, 0, 0), cap0="point", cap1="point")

# Pale sacs budding off it: the swollen, filling look (§26.5 "pale sacs").
sacs = kit.part("sacs")
for k, (c, r, bone) in enumerate([((0.3, 0.25, 0.2), (0.12, 0.14, 0.11), "sac_01"), ((0.3, -0.3, 0.1), (0.13, 0.17, 0.13), "sac_02"),
                                  ((0.28, 0.05, -0.25), (0.11, 0.14, 0.1), "sac_03"), ((0.22, -0.55, -0.12), (0.08, 0.1, 0.08), "sac_02"),
                                  ((0.24, 0.52, 0.02), (0.07, 0.08, 0.09), "sac_01"), ((0.2, -0.12, 0.32), (0.07, 0.08, 0.06), "sac_01")]):
    sacs.blob(c, r, 10, 6, SAC, bone, shape=lambda i, j, a, th, p, k=k: p + (p - Vector(c)) * 0.12 * noise3(p, 70 + k, 14.0))

# Limbs: fused arms lying flat on the hull, bent wrong at the elbow, fingers splayed on the plate.
limbs = kit.part("limbs")
for name, a, e, h in LIMBS:
    A, E, H = Vector(a), Vector(e), Vector(h)
    pts = [A, A.lerp(E, 0.5) + Vector((0.02, 0, 0)), E, E.lerp(H, 0.5) + Vector((0.01, 0, 0)), H]

    def lw(p, name=name, A=A, E=E, H=H):
        dA = (p - A.lerp(E, 0.5)).length
        dE = (p - E).length
        dH = (p - H).length
        if dH < dE * 0.7:
            return {f"{name}_2": 0.5, f"{name}_3": 0.5} if dH < 0.05 else {f"{name}_2": 1.0}
        if dE < 0.06:
            return {f"{name}_1": 0.5, f"{name}_2": 0.5}
        return {f"{name}_1": 1.0} if dA < dE else {f"{name}_1": 0.4, f"{name}_2": 0.6}
    limbs.tube(pts, [0.075, 0.06, 0.05, 0.042, 0.04], 8, FLESH, lw, ref=(1, 0, 0),
               shape=lambda i, j, ang, p, fr: p + fr[1] * (0.01 * noise3(p, 80, 20.0)))
    # A knot of tar at the elbow, where two arms grew into one.
    limbs.rock(E + Vector((0.02, 0, 0)), (0.05, 0.06, 0.05), TAR, f"{name}_1", seed=sum(map(ord, name)), sides=6)
    # Fingers: four, splayed on the hull.
    d = (H - E).normalized()
    side = Vector((1, 0, 0)).cross(d).normalized()
    for f in range(4):
        spread = (f - 1.5) * 0.45
        dirf = (d * math.cos(spread) + side * math.sin(spread)).normalized()
        f0 = H + dirf * 0.03
        f1 = f0 + dirf * 0.09 + Vector((-0.01, 0, 0))
        f2 = f1 + dirf * 0.07 + Vector((-0.015, 0, 0))
        limbs.tube([f0, f1, f2], [0.017, 0.013, 0.006], 5, FLESH, f"{name}_3", ref=(1, 0, 0), cap1=True)

# The mouth and drill at the front seam: a lipped ring, and a segmented bit boring into the hull at a slant.
drill = kit.part("drill", smooth=False)
mouth = Vector(DRILL[0]) + Vector((0.03, -0.03, 0.02))
LIP = Mat("sac.lip", hexc("#8a6a60"), shine=0.4, tint=(0.9, 0.7, 0.68))
drill.tube([mouth + Vector((0.04, -0.06, 0.035)), mouth, mouth + Vector((-0.03, 0.05, -0.025))], [0.12, 0.135, 0.1], 10, LIP,
           "drill_01", ref=(1, 0, 0), smooth=True)
drill.tube([mouth + Vector((0.01, -0.02, 0.01)), mouth + Vector((-0.04, 0.06, -0.03))], [0.09, 0.08], 10, TAR, "drill_01",
           ref=(1, 0, 0))
for i in range(3):
    p0, p1 = Vector(DRILL[i]), Vector(DRILL[i + 1])
    n = f"drill_0{i + 1}"
    r0 = 0.085 - i * 0.022
    # Each segment a ridged cone (a ratcheting bit made of something that was bone), turned a little off its axis.
    drill.tube([p0, p0.lerp(p1, 0.35), p0.lerp(p1, 0.7), p1], [r0, r0 * 0.9, r0 * 1.05, r0 * 0.7], 6, CRUST, n,
               ref=(1, 0, 0), twist=0.4 * i)
drill.tube([Vector(DRILL[3]), Vector(DRILL[3]) + (Vector(DRILL[3]) - Vector(DRILL[2])).normalized() * 0.06], [0.02, 0.002], 6,
           CRUST, "drill_03", ref=(1, 0, 0), cap1=True)
# The hot plate where the bit goes in: a scorched ring on the hull that glows with drill progress (CreatureArt).
hot_c = Vector(DRILL[3]) + Vector((0.004, 0, 0))
hot = []
for k in range(10):
    a = 2 * math.pi * k / 10
    hot.append(hot_c + Vector((0, math.cos(a) * 0.05, math.sin(a) * 0.045)))
drill.slab(hot, 0.003, HOT, "root", down=(-1, 0, 0))

# --------------------------------------------------------------------------------------------------------------
# Clips. The body bone points +X (out of the hull): about X is its twist; about Y tips it up/down; about Z swings it
# fore/aft. Limbs are flat on the hull, so their "clench" is about X (rolling them onto their edges).
limb_names = [n for n, *_ in LIMBS]

cling = Clip("cling")
cling.key(0, {}, "BEZIER")
cling.key(30, {"sac_01@scale": (1.1, 1.08, 1.1), "sac_02@scale": (1.06, 1.1, 1.06), "sac_03@scale": (1.08, 1.05, 1.1),
               "body@scale": (1.0, 1.04, 1.02), "body@loc": (0.0, 0.0, 0.0)}, "BEZIER")
cling.key(44, {"sac_01@scale": (1.04, 1.02, 1.04), "sac_02@scale": (1.1, 1.12, 1.08), "sac_03@scale": (1.02, 1.0, 1.02)}, "BEZIER")
cling.close(72)

# Drill: 20 frames, four ratchet steps of the bit a loop (CONSTANT: it clicks round, it doesn't spin), the body
# shuddering with each, limbs clenching out of step.
drill_clip = Clip("drill")
for k, f in enumerate((0, 5, 10, 15)):
    pose = {"drill_03@spin": 90 * k, "drill_02@spin": -90 * k, "drill_01": (0, 2 * (-1) ** k, 0),
            "body": (0, 1.5 * (-1) ** k, 1.5 * (-1) ** k), "body@loc": (-0.01 if k % 2 else 0.0, 0, 0),
            "sac_02@scale": (1.0 + 0.04 * (k % 2), 1.0, 1.0)}
    for i, n in enumerate(limb_names):
        pose[f"{n}_2"] = (6 * math.sin(k * 1.6 + i), 0, 0)
        pose[f"{n}_3"] = (-8 * math.sin(k * 1.6 + i), 0, 0)
    drill_clip.key(f, pose, "CONSTANT")
drill_clip.key(20, {**drill_clip.keys[0][1], "drill_03@spin": 360, "drill_02@spin": -360}, "CONSTANT")

# Punish: it draws in on itself (the sacs empty into it), the seam gapes, then it drives into the car: the whole
# body shoved half through the plate, limbs hauling, the bit buried.
punish = Clip("punish", loop=False)
punish.key(0, {}, "LINEAR")
punish.key(6, {"body@scale": (0.95, 0.8, 0.95), "sac_01@scale": (0.7, 0.7, 0.7), "sac_02@scale": (0.7, 0.7, 0.7),
               "sac_03@scale": (0.7, 0.7, 0.7), "seam_l": (0, 0, 14), "seam_r": (0, 0, -14),
               **{f"{n}_1": (0, 0, 0) for n in limb_names}}, "CONSTANT")
thrust = {"body@loc": (-0.22, 0, 0), "body@scale": (0.9, 1.1, 0.9), "seam_l": (0, 0, 24), "seam_r": (0, 0, -24),
          "drill_01": (0, 0, 0), "drill_02@spin": -60, "drill_03@spin": 140}
for i, n in enumerate(limb_names):
    thrust[f"{n}_1"] = (14 * (-1) ** i, 0, 0)
    thrust[f"{n}_2"] = (-20, 0, 0)
punish.key(9, thrust, "LINEAR")
punish.key(18, {**thrust, "body@loc": (-0.3, 0, 0)}, "LINEAR")
punish.key(24, {**thrust, "body@loc": (-0.3, 0, 0), "seam_l": (0, 0, 18), "seam_r": (0, 0, -18)}, "LINEAR")

kit.build()
rig.bake(sk, [cling, drill_clip, punish])
rig.export(rig.args()[0] if rig.args() else "clinger.glb", kit)
