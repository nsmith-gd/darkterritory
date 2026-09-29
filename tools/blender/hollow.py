"""HOLLOW (GDD §21 interior, App. A.5 · heat): "comes down the smokestack when the fire burns low."

"Gives boiler neglect a face." A tall thin figure standing in the cab, blacker than the dark around it: a person
drawn out long (2.1 m) and caked in soot until nothing but outline is left - narrow shoulders pulled up, arms that
hang past the knees, long fingers, a long neck and a narrow head tipped a little too far to one side. Soot hangs off
it in flakes like burnt paper. Two faint pale points where its eyes are (the only light it gives: §28's "limited
visual certainty"). Motion (§31): "unnaturally still when observed", then a reach that is too fast.

SK_Human stretched (long legs, longer arms and neck). Clips: idle (still, a micro-twitch, loop), reach.

    blender -b --python tools/blender/hollow.py -- content/art/models/hollow.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import figure  # noqa: E402
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
sk = rig.human(2.1, leg=1.08, arm=1.28, neck=1.55, width=0.82, head=0.88, torso=0.95)
sk.build()
kit = rig.Kit(sk, "hollow")

SOOT = Mat("tar.hollow", hexc("#0c0b0b"), shine=0.12, tint=(0.5, 0.48, 0.47))
CRUST = Mat("mineral_growth.soot", hexc("#161518"), shine=0.1, tint=(0.45, 0.45, 0.47))
EYE = Mat("eye.hollow", hexc("#d8d2bc"), emissive=1.0)

body = kit.part("body")


def gaunt(i, j, a, p):
    q = Vector(p)
    q.x *= 1 + 0.06 * noise3(q, 91, 11.0)
    q.y *= 1 + 0.06 * noise3(q, 92, 11.0)
    return q


pel = sk["pelvis"].head.z
top = sk["spine_03"].tail.z
figure.torso(body, sk, [
    (pel - 0.08, 0.1, 0.075, 0.0), (pel + 0.02, 0.115, 0.08, 0.0), (pel + 0.14, 0.085, 0.065, 0.005),
    (pel + 0.26, 0.095, 0.07, 0.01), (pel + 0.38, 0.12, 0.08, 0.01), (top - 0.08, 0.14, 0.08, 0.0),
    (top - 0.02, 0.15, 0.07, -0.01), (top + 0.03, 0.09, 0.06, -0.005), (top + 0.07, 0.04, 0.035, 0.0)],
    16, SOOT, square=0.85, shape=gaunt)

for s, sx in (("r", 1), ("l", -1)):
    figure.limb(body, sk, [f"thigh_{s}", f"calf_{s}"], [0.065, 0.042, 0.03], 10, SOOT, extra_bones=["pelvis"],
                shape=lambda i, j, a, p, fr: p + fr[0] * 0.004 * noise3(p, 93, 25.0), sub=3)
    # Bare feet, long, the toes clawing.
    body.tube([sk[f"foot_{s}"].head, sk[f"ball_{s}"].head, sk[f"ball_{s}"].tail + Vector((0, 0.04, -0.01))],
              [(0.035, 0.03), (0.035, 0.018), (0.02, 0.008)], 6, SOOT, ([f"foot_{s}", f"ball_{s}", f"calf_{s}"], 6.0),
              ref=(0, 0, 1), cap1=True)
    figure.limb(body, sk, [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], [0.05, 0.048, 0.034, 0.028, 0.024],
                10, SOOT, extra_bones=["spine_03"], ref=(0, 0, 1), sub=3)
    # Long fingers: the hand's outline is what it has instead of a face.
    h = sk[f"hand_{s}"]
    f = sk[f"fingers_{s}"]
    for k, dy in enumerate((-0.03, -0.01, 0.01, 0.03)):
        base = h.tail + Vector((0, dy, 0))
        tip = f.tail + Vector((sx * 0.05, dy * 1.8, -0.02))
        body.tube([base, base.lerp(tip, 0.5) + Vector((0, 0, 0.005)), tip], [0.012, 0.01, 0.004], 4, SOOT,
                  ([f"hand_{s}", f"fingers_{s}"], 6.0), ref=(0, 0, 1), cap1=True)
    t = sk[f"thumb_{s}"]
    body.tube([t.head, t.tail + Vector((sx * 0.02, 0.03, 0))], [0.013, 0.005], 4, SOOT, f"thumb_{s}", ref=(0, 0, 1), cap1=True)

figure.limb(body, sk, ["neck"], [0.04, 0.034], 8, SOOT, extra_bones=["spine_03", "head"], sub=3)
hc = (sk["head"].head + sk["head"].tail) / 2 + Vector((0, 0.01, -0.01))


def skull(i, j, a, th, p):
    q = p - hc
    if q.z < -0.02:
        q.x *= 0.8
        q.y *= 1.05
    return hc + q * (1 + 0.05 * noise3(p, 94, 17.0))


body.blob(hc, (0.07, 0.095, 0.125), 14, 9, SOOT, "head", shape=skull)
# The eyes: two faint pale points, deep-set, a little too far apart.
for sx in (-1, 1):
    body.box(hc + Vector((sx * 0.032, 0.083, 0.018)), (0.01, 0.004, 0.006), EYE, "head")
    body.blob(hc + Vector((sx * 0.032, 0.07, 0.02)), (0.022, 0.02, 0.018), 6, 3, SOOT, "head")

# Soot hanging off it in flakes, and a crust of it on the shoulders and back.
crust = kit.part("soot", smooth=False)
anchors = []
for s, sx in (("r", 1), ("l", -1)):
    ua, la = sk[f"upperarm_{s}"], sk[f"lowerarm_{s}"]
    anchors += [(ua.head.lerp(ua.tail, 0.4) + Vector((0, 0, -0.04)), f"upperarm_{s}", (0, 0.2, -1)),
                (ua.head.lerp(ua.tail, 0.8) + Vector((0, -0.02, -0.03)), f"upperarm_{s}", (0.1, -0.3, -1)),
                (la.head.lerp(la.tail, 0.5) + Vector((0, 0, -0.03)), f"lowerarm_{s}", (0, 0.2, -1)),
                (Vector((sx * 0.1, 0.06, pel - 0.05)), f"thigh_{s}", (0, 0.1, -1)),
                (Vector((sx * 0.12, -0.05, pel + 0.2)), "spine_01", (sx * 0.2, -0.3, -1))]
    for k in range(3):
        crust.rock(Vector((sx * (0.08 + 0.03 * k), -0.03 + 0.02 * k, top - 0.03 + 0.02 * k)), (0.035, 0.03, 0.025), CRUST,
                   "spine_03", seed=500 + k + (10 if sx > 0 else 0))
anchors += [(Vector((0.0, 0.07, pel + 0.05)), "pelvis", (0, 0.2, -1)), (Vector((0.03, -0.075, pel + 0.3)), "spine_02", (0, -0.2, -1))]
FLAKE = Mat("tar.flakes", hexc("#0a0909"), shine=0.05, tint=(0.35, 0.34, 0.34))
figure.tatters(crust, sk, anchors, FLAKE, 95, length=0.22, width=0.08)

# --------------------------------------------------------------------------------------------------------------
# Clips.
STILL = mirror({
    "spine_01": (1, 0, 0), "spine_02": (0, 0, 0), "spine_03": (-4, 0, 0), "neck": (-14, 0, 0), "head": (4, 16, 0),
    "clavicle_r": (0, -10, 0), "upperarm_r": (6, 80, 4), "lowerarm_r": (0, 0, 8), "hand_r": (0, 4, 0),
    "fingers_r": (0, 12, 0), "thumb_r": (0, 0, 0),
    "thigh_r": (0, 1, 0), "calf_r": (-1, 0, 0),
})
# The head's tilt is one-sided (mirror made it symmetric): put it back.
STILL["head"] = (4, 16, 0)

idle = Clip("idle")
idle.key(0, STILL, "CONSTANT")
idle.key(38, STILL, "CONSTANT")
idle.key(40, over(STILL, head=(4, 26, 8)), "CONSTANT")   # a twitch: two frames, then as if nothing happened
idle.key(42, STILL, "CONSTANT")
idle.key(71, over(STILL, fingers_l=(0, -30, 0)), "CONSTANT")
idle.key(73, STILL, "CONSTANT")
idle.close(90)

REACH = over(STILL, spine_02=(-8, 0, -4), spine_03=(-10, 0, -6), neck=(-22, 0, 0), head=(18, 10, 0),
             clavicle_r=(0, -4, 16), upperarm_r=(-4, 6, 84), lowerarm_r=(0, 0, 6), hand_r=(0, -14, 0),
             fingers_r=(0, -22, 0), thumb_r=(0, 0, 20),
             upperarm_l=(20, -70, -10), lowerarm_l=(0, 0, -20))
reach = Clip("reach", loop=False)
reach.key(0, STILL, "LINEAR")
reach.key(2, rig.blend(STILL, REACH, 0.8), "LINEAR")
reach.key(4, over(REACH, upperarm_r=(-8, 4, 90), spine_03=(-13, 0, -6)), "LINEAR")  # overshoot
reach.key(8, REACH, "CONSTANT")
reach.key(26, REACH, "CONSTANT")
reach.key(28, over(REACH, fingers_r=(0, 30, 0)), "CONSTANT")  # the hand closes
reach.key(34, over(REACH, fingers_r=(0, 30, 0)), "CONSTANT")

kit.build()
rig.bake(sk, [idle, reach], plant=rig.feet_planter(sk))
rig.export(rig.args()[0] if rig.args() else "hollow.glb", kit)
