"""THE SWITCHMAN (GDD §21 corrupted humans, App. A.7): "a corrupted railway worker still doing its job."

"The Corruption preserves fragments of learned behaviour" - so it still has the uniform: the long railway coat, the
peaked cap, the hand lamp held out on a bent arm the way a switchman signals. What's wrong is the body inside it:
stooped wrong (bent forward at the chest, not the hips), the neck craned up out of the collar to look ahead, the
head cocked too far, a hump of fungal crust splitting the coat between the shoulders with bracket-fungus shelves
standing off it, and the face half grown over. Silhouette (§26.1, "a distant figure at the switch"): a hunched,
shelved shape with one light held out low to its right. The lantern is part of the model (lamp_lens glass,
emissive), on its own bone under hand_r so it hangs plumb and swings.

1.8 m, SK_Human + lantern. Clips: wait (lantern swinging slowly, a twitch now and then, loop), flee (a lurching,
dragging run, loop).

    blender -b --python tools/blender/switchman.py -- content/art/models/switchman.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import figure  # noqa: E402
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, along, hexc, mirror, noise3, over, smoothstep, swap  # noqa: E402

rig.reset()
GRIP = Vector((0.80, 0.0, 1.43))
sk = rig.human(1.8, extra=[Bone("lantern", "hand_r", tuple(GRIP), tuple(GRIP + Vector((0, 0, -0.2))))])
sk.build()
kit = rig.Kit(sk, "switchman")

COAT = Mat("coat_oilskin.switchman", hexc("#2e2c22"), shine=0.14, tint=(0.8, 0.78, 0.72))
WOOL = Mat("wool.cap", hexc("#20222a"), tint=(0.7, 0.7, 0.72))
LEATHER = Mat("leather.switchman", hexc("#2a2018"), shine=0.12, tint=(0.7, 0.66, 0.62))
FLESH = Mat("flesh.switchman", hexc("#6e5a52"), shine=0.25)
FUNGUS = Mat("fungal_crust.switchman", hexc("#6a5e48"), shine=0.05)
IRON = Mat("iron_plate.lantern", hexc("#2a2a2a"), shine=0.5)
GLASS = Mat("lamp_lens.switchman", hexc("#e0a050"), emissive=1.0)

body = kit.part("body")
TORSO = along("z", [(0.9, "pelvis"), (1.12, "spine_01"), (1.27, "spine_02"), (1.40, "spine_03")])


def coat_w(p):
    w = TORSO(p)
    t = smoothstep(0.98, 0.5, p.z) * 0.6
    if t > 0:
        side = max(-1.0, min(1.0, p.x / 0.1))
        w = {k: v * (1 - t) for k, v in w.items()}
        w["thigh_r"] = w.get("thigh_r", 0) + t * (0.5 + 0.5 * side)
        w["thigh_l"] = w.get("thigh_l", 0) + t * (0.5 - 0.5 * side)
    return w


def ragged(i, j, a, p):
    q = Vector(p)
    if q.z < 0.6:
        q.z += 0.05 * noise3(Vector((math.sin(a) * 3, math.cos(a) * 3, 0)), 101, 2.0)  # torn hem
    q.x *= 1 + 0.04 * noise3(q, 102, 6.0)
    return q


rings = []
for z, rx, ry, dy in [(0.46, 0.25, 0.2, 0.0), (0.53, 0.243, 0.193, 0.0), (0.6, 0.235, 0.185, 0.0), (0.68, 0.225, 0.176, 0.0),
                      (0.76, 0.215, 0.168, 0.0), (0.83, 0.206, 0.16, -0.003), (0.9, 0.198, 0.152, -0.005),
                      (0.96, 0.19, 0.146, -0.005), (1.02, 0.185, 0.14, -0.005), (1.08, 0.19, 0.144, 0.0),
                      (1.14, 0.195, 0.148, 0.0), (1.2, 0.202, 0.15, 0.0), (1.26, 0.208, 0.15, 0.0), (1.31, 0.212, 0.148, -0.005),
                      (1.36, 0.215, 0.145, -0.01), (1.43, 0.205, 0.13, -0.02), (1.48, 0.15, 0.11, -0.02), (1.52, 0.095, 0.09, 0.0),
                      (1.58, 0.1, 0.095, 0.004)]:
    loop = []
    for j in range(20):
        a = 2 * math.pi * j / 20
        sa, ca = math.sin(a), math.cos(a)
        e = 0.8
        sa = math.copysign(abs(sa) ** e, sa)
        ca = math.copysign(abs(ca) ** e, ca)
        loop.append(ragged(len(rings), j, a, Vector((sa * rx, dy + ca * ry, z))))
    rings.append(loop)
body.loft(rings, COAT, coat_w)

ARM = along("x", [(0.14, "spine_03"), (0.2, "clavicle_{s}"), (0.26, "upperarm_{s}"), (0.43, "upperarm_{s}"),
                  (0.49, "lowerarm_{s}"), (0.70, "lowerarm_{s}"), (0.74, "hand_{s}")])
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    body.tube([(sx * x, 0, 1.448) for x in (0.12, 0.2, 0.33, 0.45, 0.58, 0.69, 0.71)],
              [0.086, 0.08, 0.072, 0.066, 0.062, 0.058, 0.064], 10, COAT, ARM, ref=(0, 0, 1))
    # Gloved hands: the lamp hand a fist, the other hanging open, fingers too long.
    body.box((sx * 0.765, 0, 1.446), (0.05, 0.045, 0.022), LEATHER, f"hand_{s}")
    body.box((sx * 0.87 if s == "l" else sx * 0.85, 0, 1.441), (0.06 if s == "l" else 0.04, 0.042, 0.018), LEATHER, f"fingers_{s}")
    body.box((sx * 0.787, 0.05, 1.44), (0.03, 0.015, 0.016), LEATHER, f"thumb_{s}")
    figure.limb(body, sk, [f"thigh_{s}", f"calf_{s}"], [0.095, 0.068, 0.058], 10, WOOL, extra_bones=["pelvis"], sub=3)
    x = sx * 0.105
    body.tube([(x, -0.01, 0.28), (x, -0.012, 0.1)], [0.066, 0.066], 9, LEATHER, f"calf_{s}")
    body.box((x, 0.07, 0.055), (0.06, 0.15, 0.055), LEATHER, lambda p, s=s: {f"foot_{s}": 1.0} if p.y < 0.12 else {f"ball_{s}": 1.0},
             taper=(0.9, 0.8))

# Neck: pulled long out of the collar, a swelling on one side of it.
figure.limb(body, sk, ["neck"], [0.055, 0.05], 8, FLESH, extra_bones=["spine_03", "head"], sub=3)
body.blob((0.035, 0.02, 1.54), (0.05, 0.05, 0.06), 8, 5, FLESH, ["neck", "spine_03"])
HC = Vector((0, 0.02, 1.675))
body.blob(HC, (0.086, 0.1, 0.116), 12, 8, FLESH, "head",
          shape=lambda i, j, a, th, p: p + (p - HC) * 0.08 * noise3(p, 103, 12.0))
for sx in (-1, 1):
    body.blob(HC + Vector((sx * 0.035, 0.085, 0.015)), (0.022, 0.015, 0.016), 6, 3, Mat("tar.eyes", hexc("#0e0c0b")), "head")
# The cap, pulled low.
body.tube([(0, 0.015, 1.725), (0, 0.015, 1.76), (0, 0.008, 1.79), (0, 0.006, 1.802)],
          [(0.098, 0.108), (0.101, 0.111), (0.112, 0.12), (0.098, 0.106)], 12, WOOL, "head", cap1=True)
body.slab([(-0.088, 0.078, 1.732), (0.088, 0.078, 1.732), (0.075, 0.17, 1.712), (0.0, 0.195, 1.705), (-0.075, 0.17, 1.712)],
          0.008, LEATHER, "head")

# The Corruption: a hump of fungal crust out through the coat between the shoulders, shelves standing off it, and the
# same growth over the left side of the face.
growth = kit.part("growth")
growth.blob((0.02, -0.13, 1.36), (0.17, 0.12, 0.16), 12, 7, FUNGUS, ["spine_03", "spine_02"],
            shape=lambda i, j, a, th, p: p + (p - Vector((0.02, -0.13, 1.36))) * 0.25 * noise3(p, 104, 8.0))
for k, (c, r, ang) in enumerate([((0.13, -0.22, 1.42), 0.12, 0.3), ((-0.06, -0.24, 1.3), 0.14, -0.2), ((0.08, -0.25, 1.22), 0.1, 0.1),
                                 ((-0.14, -0.18, 1.45), 0.09, -0.5), ((0.2, -0.12, 1.5), 0.08, 0.6)]):
    c = Vector(c)
    # Bracket fungus: flat half-discs standing out from the hump, the outline you see from the cab.
    outline = [c + Vector((math.cos(t) * r, -math.sin(t) * r * 0.7 - 0.01, 0.01 * math.sin(t * 3))) for t in
               [math.pi * q / 6 for q in range(7)]]
    rot = rig.Matrix.Rotation(ang, 3, "Y")
    outline = [c + rot @ (p - c) for p in outline]
    growth.slab(outline, 0.018, FUNGUS, "spine_03")
growth.blob(HC + Vector((-0.06, 0.05, 0.0)), (0.05, 0.06, 0.08), 8, 5, FUNGUS, "head",
            shape=lambda i, j, a, th, p: p + (p - HC) * 0.1 * noise3(p, 105, 20.0))

# The lantern: a railway hand lamp, iron frame, amber glass, a wire bail in the fist. It hangs from the lantern bone.
lamp = kit.part("lantern", smooth=False)
L0 = GRIP + Vector((0, 0, -0.07))
lamp.tube([GRIP + Vector((0, -0.05, 0)), GRIP + Vector((0, -0.05, -0.05)), L0 + Vector((0, 0, -0.02))], [0.005] * 3, 4, IRON,
          "lantern", ref=(1, 0, 0))
lamp.tube([GRIP + Vector((0, 0.05, 0)), GRIP + Vector((0, 0.05, -0.05)), L0 + Vector((0, 0, -0.02))], [0.005] * 3, 4, IRON,
          "lantern", ref=(1, 0, 0))
lamp.tube([L0 + Vector((0, 0, -0.02)), L0 + Vector((0, 0, -0.04)), L0 + Vector((0, 0, -0.08))], [0.02, 0.05, 0.075], 8, IRON,
          "lantern", ref=(1, 0, 0))
lamp.tube([L0 + Vector((0, 0, -0.08)), L0 + Vector((0, 0, -0.25))], [0.07, 0.07], 8, GLASS, "lantern", ref=(1, 0, 0),
          uv=lambda i, j, uf, vf, p: (0.25 + uf * 0.5, 0.15 + vf * 0.7))
for k in range(4):
    a = math.pi / 4 + k * math.pi / 2
    lamp.box(L0 + Vector((math.cos(a) * 0.07, math.sin(a) * 0.07, -0.165)), (0.008, 0.008, 0.09), IRON, "lantern")
lamp.tube([L0 + Vector((0, 0, -0.25)), L0 + Vector((0, 0, -0.28))], [0.08, 0.075], 8, IRON, "lantern", ref=(1, 0, 0),
          cap1=True)

# --------------------------------------------------------------------------------------------------------------
# Clips. Stooped wrong: bent forward at the chest, the neck craned up to look ahead, the head cocked.
STOOP = mirror({
    "pelvis": (6, 0, 0), "spine_01": (-8, 0, 0), "spine_02": (-40, 0, -4), "spine_03": (-38, 6, -4),
    "neck": (52, 0, 0), "head": (30, 26, -6),
    # Arm poses are relative to the bent chest: fitted by FK (rig.pose_points) so the lamp hangs out front and to the
    # right at knee height (x 0.33, 0.15 forward, 0.75 up), where the greybox held it, on a bent arm.
    "clavicle_r": (0, 4, 10), "upperarm_r": (20, 20, 30), "lowerarm_r": (0, 0, 60), "hand_r": (0, 0, 0),
    "fingers_r": (0, 70, 0),
    "thigh_r": (10, 0, 0), "calf_r": (-16, 0, 0), "foot_r": (6, 0, -4),
})
STOOP.update({"clavicle_l": (0, 14, -12), "upperarm_l": (0, -8, -78), "lowerarm_l": (0, 0, -12), "hand_l": (0, -10, 0),
              "fingers_l": (0, -10, 0), "thumb_l": (0, 0, 0)})


def with_lamp(pose, rx=0.0, rz=0.0):
    p = dict(pose)
    p["lantern"] = rig.hang(sk, p, "lantern", rx, 0, rz)
    return p


wait = Clip("wait")
for f in range(0, 72, 6):
    t = f / 72
    sw = math.sin(2 * math.pi * t)
    pose = over(STOOP, upperarm_r=(20 + 4 * sw, 20, 30 + 2 * sw), spine_02=(-40, 0, -4 + 2 * sw))
    if f == 42:
        pose = over(pose, head=(40, 34, -14), neck=(58, 0, 8))  # the twitch
    wait.key(f, with_lamp(pose, 14 * math.sin(2 * math.pi * t - 0.6), 5 * math.cos(2 * math.pi * t)),
             "CONSTANT" if f in (42, 48) else "BEZIER")
wait.close(72)

# Flee: 24 frames; the left leg drags stiff, the body lurches over it; the lamp swings wild.
F_A = over(STOOP, spine_01=(-12, 8, 0), spine_02=(-40, 8, -8), neck=(52, 0, 6), head=(24, 26, 0),
           thigh_r=(40, 0, 0), calf_r=(-50, 0, 0), foot_r=(10, 0, -4),
           thigh_l=(-24, 0, 0), calf_l=(-4, 0, 0), foot_l=(-10, 0, 4),
           upperarm_r=(10, 10, 50), lowerarm_r=(0, 0, 70), upperarm_l=(20, -8, -60))
F_B = over(STOOP, spine_01=(-10, -6, 0), spine_02=(-34, -8, 6), neck=(48, 0, -6), head=(28, 14, -6),
           thigh_r=(-20, 0, 0), calf_r=(-20, 0, 0), foot_r=(-10, 0, -4),
           thigh_l=(20, 0, 0), calf_l=(-10, 0, 0), foot_l=(6, 0, 4),
           upperarm_r=(30, 30, 20), lowerarm_r=(0, 0, 50), upperarm_l=(-20, -8, -95))
flee = Clip("flee")
flee.key(0, with_lamp(F_A, 30, 10), "LINEAR")
flee.key(6, with_lamp(rig.blend(F_A, F_B, 0.5), -10, 0), "CONSTANT")
flee.key(9, with_lamp(F_B, -30, -10), "LINEAR")
flee.key(18, with_lamp(rig.blend(F_B, F_A, 0.5), 10, 5), "CONSTANT")
flee.close(24)

kit.build()
rig.bake(sk, [wait, flee], plant=rig.feet_planter(sk))
rig.export(rig.args()[0] if rig.args() else "switchman.glb", kit)
