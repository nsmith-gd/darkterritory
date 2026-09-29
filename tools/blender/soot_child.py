"""SOOT CHILDREN (GDD §21 structural, App. A.6 · sound): "outside in the dark, calling for help in your crewmates'
voices."

Small crouched figures at the edge of the lamplight, three together, huddled with their arms round their knees:
at a glance, children sheltering by the line, which is the lure. Waxy pale skin streaked with soot and oil, a rag
of a shift, matted hair, eyes that are just dark. They don't move while looked at; when they call, the heads come
up towards the doors one after another. 0.75 m crouched (a small child's frame, big head, thin limbs).

SK_Human, child proportions. Clips: huddle (still, an occasional head snap, loop), turn (the head comes up to the
doors, and holds).

    blender -b --python tools/blender/soot_child.py -- content/art/models/soot_child.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import figure  # noqa: E402
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
sk = rig.human(1.12, head=1.35, leg=0.9, arm=0.92, torso=0.92, width=0.82, neck=0.8)
sk.build()
kit = rig.Kit(sk, "soot_child")

# Waxy: the warm skin texture drained to a grey ivory, with a dull wet sheen.
SKIN = Mat("flesh.waxy", hexc("#8a8276"), shine=0.4, tint=(0.9, 0.88, 0.86))
SOOT = Mat("tar.soot", hexc("#161312"), shine=0.3)
RAG = Mat("wool.rag", hexc("#2a2622"), tint=(0.55, 0.52, 0.5))
HAIR = Mat("tar.hair", hexc("#0e0c0b"), shine=0.2)


def streaked(pts, n):
    """Soot runs down it in streaks (down the front of the shins, the backs of the arms)."""
    c = sum(pts, Vector()) / len(pts)
    return SOOT if noise3(Vector((c.x * 4, c.y * 4, c.z * 0.8)), 111, 6.0) > 0.05 else None


body = kit.part("body")
for s, sx in (("r", 1), ("l", -1)):
    figure.limb(body, sk, [f"thigh_{s}", f"calf_{s}"], [0.055, 0.04, 0.03], 8, SKIN, extra_bones=["pelvis"], sub=3,
                fmat=streaked)
    body.tube([sk[f"foot_{s}"].head, sk[f"ball_{s}"].head, sk[f"ball_{s}"].tail], [(0.03, 0.025), (0.032, 0.015), (0.02, 0.01)],
              6, SOOT, ([f"foot_{s}", f"ball_{s}", f"calf_{s}"], 6.0), ref=(0, 0, 1), cap1=True)
    figure.limb(body, sk, [f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], [0.035, 0.028, 0.022, 0.02], 8, SKIN,
                extra_bones=["clavicle_" + s], ref=(0, 0, 1), sub=3, fmat=streaked)
    h, f = sk[f"hand_{s}"], sk[f"fingers_{s}"]
    body.tube([h.tail, f.tail], [(0.022, 0.009), (0.018, 0.007)], 5, SKIN, f"fingers_{s}", ref=(0, 0, 1), cap1=True)
# The shift: a rag from the shoulders to the thigh, torn.
pel = sk["pelvis"].head.z
top = sk["spine_03"].tail.z


def torn(i, j, a, p):
    q = Vector(p)
    if i == 0:
        q.z += 0.04 * noise3(Vector((math.sin(a) * 3, math.cos(a) * 3, 0)), 112, 2.0)
    return q


figure.torso(body, sk, [(pel - 0.14, 0.13, 0.11, 0.0), (pel - 0.04, 0.12, 0.095, 0.0), (pel + 0.06, 0.1, 0.075, 0.0),
                        (pel + 0.16, 0.105, 0.08, 0.005), (top - 0.07, 0.115, 0.08, 0.0), (top - 0.01, 0.1, 0.07, -0.005),
                        (top + 0.02, 0.05, 0.045, 0.0)], 14, RAG, shape=torn)
figure.limb(body, sk, ["neck"], [0.035, 0.032], 8, SKIN, extra_bones=["spine_03", "head"], sub=2)
hc = (sk["head"].head + sk["head"].tail) / 2 + Vector((0, 0.01, 0.0))
def child_skull(i, j, a, th, p):
    q = p - hc
    # A child's skull, but wrong: too long in the jaw, the cheeks fallen in.
    if q.z < -0.02:
        q.x *= 0.82
        q.z *= 1.18
    return hc + q * (1 + 0.05 * noise3(p, 113, 15.0))


body.blob(hc, (0.075, 0.086, 0.1), 12, 8, SKIN, "head", shape=child_skull)
# Eyes that are only dark, and a small open mouth (it calls).
for sx in (-1, 1):
    body.blob(hc + Vector((sx * 0.03, 0.074, 0.008)), (0.026, 0.02, 0.024), 6, 3, SOOT, "head")
body.blob(hc + Vector((0, 0.08, -0.07)), (0.016, 0.012, 0.014), 6, 3, SOOT, "head")
# Matted hair: a cap of it plastered over the skull and down the back of the neck, a few locks over the brow.
hair = kit.part("hair", smooth=False)
hair.blob(hc + Vector((0, -0.012, 0.018)), (0.082, 0.092, 0.095), 10, 5, HAIR, "head", z0=-0.15,
          shape=lambda i, j, a, th, p: p + (p - hc) * 0.1 * noise3(p, 114, 25.0))
hair.blob(hc + Vector((0, -0.06, -0.045)), (0.06, 0.04, 0.06), 8, 4, HAIR, ["head", "neck"])
figure.tatters(hair, sk, [(hc + Vector((sx * 0.035, 0.075, 0.07)), "head", (sx * 0.2, 0.4, -1)) for sx in (-1, 0.3, 1)], HAIR,
               114, length=0.07, width=0.035)

# --------------------------------------------------------------------------------------------------------------
# Clips. Crouched in a ball: knees to the chest, arms round the shins, head down on the knees.
HUDDLE = mirror({
    "pelvis": (-30, 0, 0), "spine_01": (-18, 0, 0), "spine_02": (-16, 0, 0), "spine_03": (-12, 0, 0),
    "neck": (-10, 0, 0), "head": (-10, 0, 0),
    "thigh_r": (120, -8, 0), "calf_r": (-142, 0, 0), "foot_r": (26, 0, -6),
    "clavicle_r": (0, 10, 18), "upperarm_r": (58, 58, 4), "lowerarm_r": (0, 0, 104), "hand_r": (0, 10, 0),
    "fingers_r": (0, 40, 0),
})
HUDDLE["head"] = (-10, 6, 4)  # a little to one side
huddle = Clip("huddle")
huddle.key(0, HUDDLE, "CONSTANT")
huddle.key(50, HUDDLE, "CONSTANT")
huddle.key(52, over(HUDDLE, neck=(-4, 0, 14), head=(0, 6, 24)), "CONSTANT")  # a snap to one side...
huddle.key(58, over(HUDDLE, neck=(-4, 0, 14), head=(0, 6, 24)), "CONSTANT")
huddle.key(60, HUDDLE, "CONSTANT")  # ...and back, as if it never moved
huddle.close(96)

LOOK = over(HUDDLE, spine_02=(-8, 0, 0), spine_03=(0, 0, 0), neck=(30, 0, 0), head=(26, 4, 0))
turn = Clip("turn", loop=False)
turn.key(0, HUDDLE, "CONSTANT")
turn.key(3, rig.blend(HUDDLE, LOOK, 0.7), "LINEAR")
turn.key(5, over(LOOK, head=(32, 4, 0)), "LINEAR")
turn.key(9, LOOK, "CONSTANT")
turn.key(20, LOOK, "CONSTANT")

kit.build()
rig.bake(sk, [huddle, turn], plant=rig.feet_planter(sk))
rig.export(rig.args()[0] if rig.args() else "soot_child.glb", kit)
