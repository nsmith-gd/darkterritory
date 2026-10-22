"""THE PICKERS (GDD §21, OUTSIDE; ARCHITECTURE §8 note 574; docs/design/creatures/pickers.md §3): "knee-high scavengers
that live in a yard's drains", racing the crew for the loose crates and carrying them off over their heads.

About 0.5 m at the shoulder and 0.9 m long, lean and quick, built to carry: a narrow hunched body like a starved hare's,
the skin tight over the ribs, the spine's knuckles and the shoulder blades standing out of it; long forelimbs ending in
big, pale, many-jointed hands (three fingers of three joints and a thumb) for gripping a crate's edges; short hind legs
for scuttling; a small eyeless head that's mostly mouth, needle teeth round it, and wide cupped ears. Grey-brown and
patchy, coal-black from the drains to the elbows and the knees (the bake's). They run on all fours, and carry a crate
over their heads, upright on their hind legs, the long arms up its sides: a yard of them reads at a distance as crates
hurrying along by themselves.

How it's made (the Look Review: organic, one skin, not primitives; the Mourners' and the Ribbit's way, notes 361, 362):
the haunches, the starved waist, the ribs' cage, the shoulders, the long arms to the palms, the short legs to the feet,
the neck and the head down to the upper lip are one skin of smooth volumes (tools/blender/flesh.py), settled onto their
field before QuadriFlow; the jaw is a second skin the same way (it opens, the Ribbit's). Over them, finer than their
quads: the fingers and the thumbs, the toes, the ears' cupped blades, the needle teeth. Its colour is baked into one
atlas by tools/models/recipes/picker.py.

Its own rig (SK_Picker, 47 bones): root, pelvis, spine_01, spine_02, neck, head, jaw, two ears; clavicles, arms, hands,
three three-boned fingers and a two-boned thumb a hand; thighs, calves, feet, toes. Its rest is the crouch it waits in on
all fours, facing +Y (the engine's -Z). Clips (GDD §31: still, then too fast): idle (crouched, the head jerking round, the
ears turning: it hunts by ear), scuttle (all fours, quick), carry (upright on its hind legs, the crate over its head in
its hands, the little legs pattering), heave (the second under a heavy crate, beside it, hoisting at its edge), grab
(taking a crate up), bite (a lunge and a snap), scatter (a startled bolt, ears flat), emerge (up out of a drain),
descend (down it head first), hit, death.

    tools/models/build.sh picker        # this, with its colour baked -> content/art/models/picker.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/picker.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from mathutils import Matrix, Quaternion  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

FINGERS = ("a", "b", "c")
# The crate it carries (enemies.json pickers holdHeight: its centre 1.0 m over its feet; a crate's 0.88 m, Bodies'
# 0.45 radius): over it, the palms on its sides. A heavy crate's (facilities.json heavy radius 0.55) is carried by two,
# the second (`heave`) heaveBeside 0.9 m to the lead's right: the crate's edge on its left.
CRATE_HALF, HOLD = 0.44, 1.0
HEAVY_HALF, BESIDE = 0.55, 0.9


def picker_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.22, 0.3), (0, -0.08, 0.37)),
        # (The loins arched up over the tucked belly: a hare's hunch.)
        Bone("spine_01", "pelvis", (0, -0.08, 0.37), (0, 0.07, 0.42)),
        Bone("spine_02", "spine_01", (0, 0.07, 0.42), (0, 0.2, 0.425)),
        Bone("neck", "spine_02", (0, 0.2, 0.425), (0, 0.3, 0.39)),
        Bone("head", "neck", (0, 0.3, 0.39), (0, 0.45, 0.36)),
        Bone("jaw", "head", (0, 0.315, 0.36), (0, 0.45, 0.335)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"ear_{s}", "head", (sx * 0.035, 0.32, 0.41), (sx * 0.1, 0.3, 0.49)),
            Bone(f"clavicle_{s}", "spine_02", (sx * 0.015, 0.15, 0.44), (sx * 0.08, 0.165, 0.43)),
            # The long forelimbs: the elbow back under the shoulder, the forearm forward to the wrist (a hyena's).
            Bone(f"upperarm_{s}", f"clavicle_{s}", (sx * 0.085, 0.17, 0.42), (sx * 0.115, 0.1, 0.19)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.115, 0.1, 0.19), (sx * 0.12, 0.27, 0.04)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.12, 0.27, 0.04), (sx * 0.125, 0.345, 0.022)),
            Bone(f"thumb_{s}_01", f"hand_{s}", (sx * 0.1, 0.295, 0.035), (sx * 0.08, 0.33, 0.02)),
            Bone(f"thumb_{s}_02", f"thumb_{s}_01", (sx * 0.08, 0.33, 0.02), (sx * 0.068, 0.36, 0.012)),
            # The short hind legs: the knee forward, the hock back and up off the ground, the long foot flat.
            Bone(f"thigh_{s}", "pelvis", (sx * 0.07, -0.24, 0.27), (sx * 0.085, -0.13, 0.15)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.085, -0.13, 0.15), (sx * 0.085, -0.27, 0.06)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.085, -0.27, 0.06), (sx * 0.085, -0.17, 0.018)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.085, -0.17, 0.018), (sx * 0.085, -0.115, 0.012)),
        ]
        for i, f in enumerate(FINGERS):
            # Three long fingers off the palm, fanned, three joints each, lying on the ground, the tips hooked under.
            dx = (i - 1) * 0.022
            k0 = Vector((sx * (0.125 + dx), 0.345 - 0.008 * abs(i - 1), 0.022))
            k1 = k0 + Vector((sx * dx * 0.5, 0.04, -0.008))
            k2 = k1 + Vector((sx * dx * 0.25, 0.034, -0.003))
            k3 = k2 + Vector((sx * dx * 0.1, 0.026, -0.004))
            b += [Bone(f"finger_{f}_{s}_01", f"hand_{s}", tuple(k0), tuple(k1)),
                  Bone(f"finger_{f}_{s}_02", f"finger_{f}_{s}_01", tuple(k1), tuple(k2)),
                  Bone(f"finger_{f}_{s}_03", f"finger_{f}_{s}_02", tuple(k2), tuple(k3))]
    return Skeleton("SK_Picker", b)


sk = picker_skeleton()
sk.build()
kit = rig.Kit(sk, "picker")
SKIN_FACES, JAW_FACES = 1900, 170


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/picker.py),
# the names say what to paint: the patchy hide, the pale hands, the ears' thin skin, the mouth, the teeth, the nails.
SKIN = Mat("skin.picker", hexc("#6e6255"), shine=0.12)
HAND = Mat("skin.picker_hand", hexc("#b4aa9c"), shine=0.14)
EAR = Mat("skin.picker_ear", hexc("#8a7470"), shine=0.12)
MOUTH = Mat("skin.picker_mouth", hexc("#3a1a18"), shine=0.5)
TOOTH = Mat("skin.picker_tooth", hexc("#cfc6a8"), shine=0.55)
NAIL = Mat("skin.picker_nail", hexc("#2a2622"), shine=0.3)

# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the hind toes to the upper lip (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "picker flesh")
SPINE_PTS = [H("pelvis") + Vector((0, -0.09, -0.02)), H("spine_01"), H("spine_02"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "neck", "head"], soft=0.04)
HC = Vector((0, 0.37, 0.36))  # the mouth's middle, between the jaws


def trunk_w(p):
    """Along the spine; the shoulders' bulk goes with the clavicles and the upper arms, the skin over each hip with its
    thigh."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if 0.08 < p.y < 0.26 and ax > 0.04 and p.z < 0.47:
        k = smooth01(0.04, 0.09, ax) * bell((p.y - 0.17) / 0.07) * smooth01(0.47, 0.38, p.z)
        w = mix(w, {f"clavicle_{s}": 1.0}, 0.4 * k)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.45 * k * smooth01(0.42, 0.33, p.z))
    d = (p - H(f"thigh_{s}")).length
    if d < 0.09:
        # (Only the haunch's skin right over the hip goes with the thigh: the rump stays the pelvis's.)
        w = mix(w, {f"thigh_{s}": 1.0}, 0.45 * smooth01(0.09, 0.03, d) * smooth01(0.02, 0.06, ax))
    return w


def head_w(p):
    p = Vector(p)
    w = trunk_w(p)
    if p.y > 0.3 and p.z < 0.37:
        # (Under the lip's line by the gape the cheeks go a little with the jaw: the mouth's corners stretch open.)
        w = mix(w, {"jaw": 1.0}, 0.5 * smooth01(0.366, 0.352, p.z) * smooth01(0.3, 0.34, p.y))
    return w


# The trunk: a small hard rump low over the hind legs, the waist drawn in under the arched loins (starved), the ribs'
# cage narrow and deep, and on it the shoulders, higher than the hips, carrying the long arms.
flesh.blob((0, -0.235, 0.295), (0.068, 0.085, 0.075), 0, SKIN, trunk_w)                               # the rump
flesh.blob((0, -0.1, 0.36), (0.06, 0.125, 0.066), 0.06, SKIN, trunk_w, rot=R(rx=-14))                 # the loins
flesh.blob((0, 0.08, 0.39), (0.07, 0.115, 0.085), 0.06, SKIN, trunk_w, rot=R(rx=-4))                   # the ribs' cage
flesh.blob((0, 0.165, 0.45), (0.065, 0.07, 0.04), 0.04, SKIN, trunk_w)                                # the withers
for sx in (1, -1):
    flesh.blob((sx * 0.05, -0.2, 0.27), (0.042, 0.075, 0.065), 0.04, SKIN, trunk_w, rot=R(rx=-20))       # the haunch
    flesh.blob((sx * 0.072, 0.165, 0.405), (0.042, 0.055, 0.058), 0.035, SKIN, trunk_w)                 # the shoulder
    # The shoulder blades and the hip bones, standing proud.
    flesh.blob((sx * 0.045, 0.13, 0.468), (0.026, 0.05, 0.014), 0.02, SKIN, trunk_w, rot=R(rz=-sx * 15))
    flesh.blob((sx * 0.045, -0.19, 0.35), (0.022, 0.032, 0.016), 0.018, SKIN, trunk_w)
# The belly tucked up under the loins; the spine's knuckles in a ridge down the back.
flesh.carve((0, -0.06, 0.285), (0.05, 0.08, 0.028), 0.03)
for k in range(10):
    t = k / 9
    c = Vector((0, -0.27, 0.3)).lerp(Vector((0, 0.17, 0.42)), t)
    top = flesh.surface(c, (0, 0, 1), 0.2)
    flesh.blob(top - Vector((0, 0, 0.006)), (0.011, 0.013, 0.01), 0.008, SKIN, trunk_w, around=10, rings=6)

# The arms: long and lean, the elbows knobbed, the forearms all sinew; the palms big and pale.
PALMS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([H(f"clavicle_{s}"), sh, el, wr, tip], [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.03)
    flesh.limb([sh + Vector((0, 0, 0.01)), sh.lerp(el, 0.45), el], [0.036, 0.03, 0.021], 0.03, SKIN,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(0.36, 0.43, p[2]) * 0.6))
    flesh.blob(el + Vector((0, -0.008, 0)), (0.022, 0.023, 0.023), 0.012, SKIN, arm, around=12, rings=8)      # the elbow
    flesh.limb([el, el.lerp(wr, 0.4), wr], [0.021, 0.019, 0.015], 0.012, SKIN, arm)
    palm = wr.lerp(tip, 0.55)
    PALMS[s] = palm
    d = (tip - wr).normalized()
    flesh.blob(palm, (0.04, 0.05, 0.014), 0.016, HAND, arm, rot=R(rx=math.degrees(math.atan2(d.z, d.y))), around=14, rings=6)
    flesh.blob(wr + Vector((0, 0.008, 0.004)), (0.02, 0.018, 0.016), 0.012, HAND, arm, around=10, rings=6)     # the wrist

# The hind legs: short, the thigh in the haunch, the shin all bone back to the hock, the long foot flat.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.03)
    flesh.limb([hip + Vector((0, 0, 0.02)), hip.lerp(kn, 0.5), kn], [0.044, 0.036, 0.023], 0.03, SKIN,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(0.25, 0.32, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.006, 0)), (0.021, 0.021, 0.021), 0.01, SKIN, leg, around=12, rings=8)      # the knee
    flesh.limb([kn, kn.lerp(an, 0.5), an], [0.02, 0.016, 0.013], 0.01, SKIN, leg)
    flesh.blob(an + Vector((0, -0.008, 0)), (0.014, 0.018, 0.016), 0.008, SKIN, leg)                     # the hock
    flesh.blob(an.lerp(ball, 0.55) + Vector((0, 0, -0.012)), (0.019, 0.055, 0.011), 0.014, SKIN, leg,
               rot=R(rx=-math.degrees(math.atan2(an.z - ball.z, ball.y - an.y)) * 0.6))                  # the foot

# The neck, thin and stretched forward and down, and the head: small, eyeless, mostly mouth. The skull's a low round
# cap behind the ears; the upper jaw a deep blunt wedge in front of it, longer than the skull, its lip rolled and
# running back along the sides to the corners under the ears; the roof of the mouth vaulted up inside (it shows when the
# jaw drops). Where the eyes would be, the skin's drawn in: two shallow dimples.
flesh.limb([H("neck") + Vector((0, -0.02, 0.005)), H("neck").lerp(T("neck"), 0.5), T("neck") + Vector((0, 0.01, 0))],
           [0.045, 0.034, 0.032], 0.03, SKIN, trunk_w)
flesh.blob((0, 0.33, 0.398), (0.042, 0.05, 0.036), 0.025, SKIN, head_w, around=18, rings=10)        # the skull
flesh.blob((0, 0.4, 0.38), (0.045, 0.066, 0.024), 0.025, SKIN, head_w, around=18, rings=8)          # the upper jaw
flesh.blob((0, 0.452, 0.374), (0.028, 0.026, 0.02), 0.018, SKIN, head_w)                             # the snout's blunt end
for sx in (1, -1):
    flesh.blob((sx * 0.037, 0.35, 0.37), (0.017, 0.04, 0.017), 0.018, SKIN, head_w)                  # the cheek
    flesh.carve((sx * 0.03, 0.395, 0.405), (0.012, 0.014, 0.008), 0.008)                             # no eye
# The upper lip, rolled and thin, right round the mouth's front and back along the sides to the corners under the ears.
LIP = [flesh.surface(HC + Vector((0, 0, 0.004)), (math.sin(math.radians(a)), math.cos(math.radians(a)), 0), 0.15)
       for a in range(-100, 101, 14)]
LIP = [p - (p - HC).normalized() * 0.003 for p in LIP]
flesh.limb(LIP, [0.005] + [0.006] * (len(LIP) - 2) + [0.005], 0.006, SKIN, head_w, sides=8, ref=(0, 0, 1))
flesh.carve((0, 0.39, 0.356), (0.036, 0.06, 0.014), 0.008)                                           # the mouth's roof

# ----------------------------------------------------------------------------------------------------------------
# The jaw: a narrow scoop under the upper one, hinged back under the ears, its lip rolled; the floor of the mouth
# hollowed. Its own skin (rig.fuse): it opens.
jaw = kit.part("jaw")
jflesh = Flesh(jaw, "picker jaw")
JC = Vector((0, 0.375, 0.348))


def jaw_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    return MOUTH if n.z > 0.5 and c.z > 0.346 and abs(c.x) < 0.03 else SKIN


jflesh.blob((0, 0.383, 0.34), (0.037, 0.066, 0.018), 0, SKIN, "jaw", around=18, rings=8, fmat=jaw_mat)
jflesh.blob((0, 0.438, 0.343), (0.022, 0.02, 0.014), 0.014, SKIN, "jaw", fmat=jaw_mat)                # the chin
for sx in (1, -1):
    jflesh.blob((sx * 0.03, 0.33, 0.35), (0.012, 0.03, 0.015), 0.014, SKIN, "jaw", fmat=jaw_mat)     # the hinge
LOWLIP = [jflesh.surface(JC, (math.sin(math.radians(a)), math.cos(math.radians(a)), 0), 0.15) for a in range(-96, 97, 16)]
LOWLIP = [p - (p - JC).normalized() * 0.004 + Vector((0, 0, 0.004)) for p in LOWLIP]
jflesh.limb(LOWLIP, [0.004] + [0.005] * (len(LOWLIP) - 2) + [0.004], 0.005, SKIN, "jaw", sides=8, ref=(0, 0, 1))
jflesh.carve((0, 0.385, 0.357), (0.028, 0.052, 0.01), 0.006)                                           # the mouth's floor

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the needle teeth, the ears' cupped blades, the fingers, the thumbs and the toes.
fine = kit.part("fine")
TEETH_AT = []


def tooth_row(lip, centre, n, inset, z, long_, bone, seed):
    """Needles round a jaw just inside its lip, longest at the front, raked in (crooked: hashed)."""
    for k in range(n):
        t = 0.5 + k * (len(lip) - 2) / (n - 1)
        i0 = min(int(t), len(lip) - 2)
        c = lip[i0].lerp(lip[i0 + 1], t - i0)
        c = c - (c - centre).normalized() * inset
        front = bell(c.x / 0.03) * smooth01(0.34, 0.42, c.y)
        L = long_ * (0.6 + 0.4 * front) * (0.8 + 0.4 * h01(seed, k))
        rake = (centre - c).normalized() * 0.003
        base = Vector((c.x, c.y, z))
        tip = base + Vector((rake.x, rake.y, L if z < centre.z else -L))
        TEETH_AT.append(base)
        fine.tube([base, base.lerp(tip, 0.5), tip], [0.0022, 0.0016, 0.0004], 3, TOOTH, bone, ref=(0, 1, 0), cap1="point")


tooth_row(LIP, HC, 16, 0.006, 0.362, 0.014, "head", 21)
tooth_row(LOWLIP, JC, 14, 0.005, 0.346, 0.011, "jaw", 22)

# The ears: wide, cupped like a bat's, thin and veined, standing out from the sides of the skull toward the front:
# a flattened ellipsoid bent round into a cup (its hollow forward), on its own bone.
EARS, EAR_OPEN = {}, {}
for s, sx in (("r", 1), ("l", -1)):
    e0, e1 = H(f"ear_{s}"), T(f"ear_{s}")
    c = e0.lerp(e1, 0.6)
    EARS[s] = c
    rot = R(rx=-16, rz=-sx * 40)
    M = rot.to_3x3()
    Mi = M.inverted()
    EAR_OPEN[s] = (M @ Vector((0, 1, 0))).normalized()

    def cup(i, j, a, th, p, c=c, M=M, Mi=Mi):
        q = Mi @ (Vector(p) - c)
        # The rim's bent forward round the hollow (it opens along local +y: forward and out).
        q.y += 0.85 * (q.x * q.x) / 0.05 + 0.25 * max(0.0, -q.z) ** 2 / 0.05
        return c + M @ q
    fine.blob(c, (0.052, 0.006, 0.06), 14, 9, EAR, f"ear_{s}", rot=rot, shape=cup)
    # Its root: a stalk of the same skin into the skull.
    fine.tube([e0 + Vector((-sx * 0.012, 0.006, -0.01)), e0.lerp(e1, 0.3)], [0.013, 0.01], 8, EAR, {"head": 0.5, f"ear_{s}": 0.5},
              ref=(0, 0, 1), cap0="point")

# The fingers: long, knuckled at every joint, the nails short and black; the thumb short.
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for f in FINGERS:
        k0, k1, k2, k3 = H(f"finger_{f}_{s}_01"), H(f"finger_{f}_{s}_02"), H(f"finger_{f}_{s}_03"), T(f"finger_{f}_{s}_03")
        a = k0 - (k1 - k0).normalized() * 0.014
        bones = chain([a, k1, k2, k3], [f"finger_{f}_{s}_01", f"finger_{f}_{s}_02", f"finger_{f}_{s}_03"], soft=0.01)
        TIPS.append(k3)
        fine.tube([a, a.lerp(k1, 0.5), k1, k1.lerp(k2, 0.5), k2, k2.lerp(k3, 0.6), k3 + (k3 - k2).normalized() * 0.004],
                  [0.0112, 0.0095, 0.0102, 0.0086, 0.009, 0.0074, 0.0035], 6, HAND, bones, ref=(0, 0, 1), cap0="point", cap1="point",
                  fmat=lambda pts, n, k3=k3: NAIL if (sum(pts, Vector()) / len(pts) - k3).length < 0.01 and n.z > -0.2 else HAND)
    t0, t1, t2 = H(f"thumb_{s}_01"), H(f"thumb_{s}_02"), T(f"thumb_{s}_02")
    a = t0 - (t1 - t0).normalized() * 0.01
    fine.tube([a, t1, t1.lerp(t2, 0.6), t2 + (t2 - t1).normalized() * 0.004], [0.012, 0.01, 0.0085, 0.0035], 6, HAND,
              chain([a, t1, t2], [f"thumb_{s}_01", f"thumb_{s}_02"], soft=0.01), ref=(0, 0, 1), cap0="point", cap1="point",
              fmat=lambda pts, n, t2=t2: NAIL if (sum(pts, Vector()) / len(pts) - t2).length < 0.01 else HAND)
    # Three short toes, splayed, black-clawed.
    ball = H(f"toe_{s}")
    for k in range(3):
        dx = (k - 1) * 0.011
        a0 = ball + Vector((sx * dx, -0.008, 0.002))
        end = a0 + Vector((sx * dx * 0.5, 0.05 - 0.008 * abs(k - 1), -0.004))
        TIPS.append(end)
        fine.tube([a0, a0.lerp(end, 0.55) + Vector((0, 0, 0.003)), end, end + Vector((0, 0.008, -0.004))], [0.0075, 0.006, 0.004, 0.0015], 5,
                  SKIN, f"toe_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, n, end=end: NAIL if (sum(pts, Vector()) / len(pts) - end).length < 0.008 else SKIN)


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest is the crouch on all fours it waits in; angles in the armature's axes: +X swings a hanging limb
# forward and tips a forward-lying bone (the spine, the head) up; a finger curls with -X; the jaw opens with -X.

def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


def curl(k, thumb=0.0, sides=("r", "l")):
    out = {}
    for s in sides:
        for i, f in enumerate(FINGERS):
            out[f"finger_{f}_{s}_01"] = (-k * (0.7 + 0.1 * i), 0, 0)
            out[f"finger_{f}_{s}_02"] = (-k, 0, 0)
            out[f"finger_{f}_{s}_03"] = (-k * 0.8, 0, 0)
        out[f"thumb_{s}_01"] = (-thumb, 0, 0)
        out[f"thumb_{s}_02"] = (-thumb, 0, 0)
    return out


def hang_q(pose, name, q):
    """The local rotation that leaves `name` at world rotation `q` (a quaternion) whatever its parents do."""
    parent = sk[name].parent
    Wp = rig.world_rotation(sk, pose, parent) if parent else Quaternion()
    return Wp.inverted() @ q


def pts(pose, names):
    return rig.pose_points(sk, pose, names)


def plant(pose, s, target, foot=(0, 0, 0), pole=None):
    """A hind leg posed so its hock lands at `target`, the foot then turned to world rotation `foot` (its rest: flat)."""
    p = rig.reach(sk, pose, f"thigh_{s}", f"calf_{s}", Vector(target), elbow_axis=0, bend=-1,
                  pole=pole or Vector(target) + Vector((0, 0.3, 0.3)))
    p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", *foot)
    p[f"toe_{s}"] = (0, 0, 0)
    return p


def reach_arm(pose, s, wrist, pole):
    return rig.reach(sk, pose, f"upperarm_{s}", f"lowerarm_{s}", Vector(wrist), elbow_axis=0, bend=1, pole=Vector(pole))


def palm_on(pose, s, up, inward):
    """The hand turned (world) so it points `up` with its palm facing `inward` (its rest: forward, palm down)."""
    up, inward = Vector(up).normalized(), Vector(inward).normalized()
    # Rest: the hand along +Y, the palm's normal -Z. Wanted: +Y -> up, -Z -> inward.
    side = up.cross(-inward)
    M = Matrix((side, up, -inward)).transposed()  # columns: where X, Y, Z go
    q = M.to_quaternion()
    p = dict(pose)
    p[f"hand_{s}"] = hang_q(p, f"hand_{s}", q)
    return p


CROUCH = {"ear_r": (0, 0, 0), "ear_l": (0, 0, 0), "jaw": (-3, 0, 0)}

# Idle (3.3 s, loop): crouched on all fours, dead still, like a bird: the head jerked round to a sound in two frames
# and frozen there, the ears swivelling after it (one late); a tick further and cocked over; snapped the other way; the
# jaw chattering on nothing; dropped low to sniff; cocked up, listening; slowly back. No two beats the same length.
def alert(look=0.0, tilt=0.0, cock=0.0, ear_r=0.0, ear_l=0.0, jaw=-3.0, low=0.0):
    return with_(CROUCH, {
        "neck": (2 + tilt * 0.3 - 8 * low, cock * 0.3, look * 0.35), "head": (tilt - 6 * low, cock * 0.7, look * 0.65), "jaw": (jaw, 0, 0),
        "ear_r": (0, 0, -18 * ear_r + 8), "ear_l": (0, 0, -18 * ear_l - 8),
        "spine_02": (0, 0, look * 0.08), "root@loc": (0, 0.01 * low, -0.004 * abs(ear_r) - 0.02 * low)})


idle = Clip("idle")
for f, pose_, ip in (
        (0, alert(), "BEZIER"), (10, alert(), "LINEAR"),
        (12, alert(36, 6, 0, 1, 0.4), "LINEAR"), (14, alert(36, 6, 0, 1, 1), "LINEAR"),          # snapped round; an ear late
        (23, alert(36, 6, 0, 1, 1), "LINEAR"), (24, alert(42, 8, 24, 1, 1), "LINEAR"),           # a tick further, cocked
        (32, alert(42, 8, 24, 1, 1), "LINEAR"), (34, alert(-30, -4, -8, -1, -1), "LINEAR"),      # the other way
        (37, alert(-30, -4, -8, -1, -1), "LINEAR"), (38, alert(-30, -4, -8, -1, -1, jaw=-14), "LINEAR"),  # chattering
        (39, alert(-30, -4, -8, -1, -1), "LINEAR"), (40, alert(-30, -4, -8, -1, -1, jaw=-12), "LINEAR"),
        (41, alert(-30, -4, -8, -1, -1), "LINEAR"), (49, alert(-30, -4, -8, -1, -1), "LINEAR"),
        (51, alert(-6, 14, 0, 0.3, -0.5, low=1), "LINEAR"),                                       # down, sniffing
        (57, alert(-6, 14, 0, 0.3, -0.5, low=1), "LINEAR"), (58, alert(-6, 14, 0, 0.3, -0.5, jaw=-8, low=1), "LINEAR"),
        (59, alert(-6, 14, 0, 0.3, -0.5, low=1), "LINEAR"), (66, alert(-6, 14, 0, 0.3, -0.5, low=1), "LINEAR"),
        (68, alert(12, 10, 30, 0.8, 0.8), "BEZIER"),                                              # up, cocked, listening
        (80, alert(12, 10, 32, 0.8, 0.8), "BEZIER"), (92, alert(2, 2, 4, 0.1, 0.1), "BEZIER")):
    idle.key(f, pose_, ip)
idle.close(100)


# Scuttle (0.53 s, loop, two strides): all fours at a rotary gallop (the sim's 4.8 m/s: a stride of 1.28 m every
# 0.27 s), the long arms reaching far out in front and the short legs driving under, the back flexing, the head held
# level; on the second stride the head jerks aside and back.
def run_pose(k, gather, look=0.0):
    RUN = with_(CROUCH, {"pelvis": (-6, 0, 0), "neck": (-6, 0, look * 0.4), "head": (6, 0, look * 0.6), "ear_r": (-24, 0, -10), "ear_l": (-24, 0, 10)})
    return with_(RUN, {
        "upperarm_r": (-34 * k + 10 * gather, 0, 0), "upperarm_l": (-34 * k - 10 * gather + 8, 0, 0),
        "lowerarm_r": (22 * max(0, -k) + 10, 0, 0), "lowerarm_l": (22 * max(0, -k) + 14, 0, 0),
        "hand_r": (18 * k, 0, 0), "hand_l": (18 * k, 0, 0),
        "thigh_r": (34 * k + 6 * gather, 0, 0), "thigh_l": (34 * k - 6 * gather + 6, 0, 0),
        "calf_r": (-26 * max(0, k), 0, 0), "calf_l": (-26 * max(0, k) - 6, 0, 0),
        "foot_r": (-14 * k, 0, 0), "foot_l": (-14 * k, 0, 0),
        "spine_01": (8 * k, 0, 0), "pelvis": (-6 - 6 * k, 0, 0), "spine_02": (-4 * k, 0, 0),
        "root@loc": (0, 0, 0.035 * (1 - abs(k)) - 0.01)})


scuttle = Clip("scuttle")
for o, looks in ((0, (0, 0, 0, 0)), (8, (0, 26, 22, 0))):
    for i, (k, gather) in enumerate(((1, 0), (0, 1), (-1, 0), (0, -1))):
        scuttle.key(o + 2 * i, run_pose(k, gather, looks[i]), "LINEAR")
scuttle.close(16)

# Scatter (0.27 s, loop): the startled bolt: flatter and lower, bounding, the ears pinned flat back, the head down.
scatter = Clip("scatter")
FLAT = with_(CROUCH, {"pelvis": (-10, 0, 0), "spine_01": (-4, 0, 0), "neck": (-14, 0, 0), "head": (2, 0, 0),
                      "ear_r": (-60, 0, -20), "ear_l": (-60, 0, 20), "jaw": (-10, 0, 0)})
for f, k in ((0, 1), (2, 0.3), (4, -1), (6, -0.3)):
    scatter.key(f, with_(FLAT, {
        "upperarm_r": (-44 * k + 6, 0, 0), "upperarm_l": (-44 * k + 2, 0, 0),
        "lowerarm_r": (30 * max(0, -k) + 6, 0, 0), "lowerarm_l": (30 * max(0, -k) + 10, 0, 0),
        "hand_r": (22 * k, 0, 0), "hand_l": (22 * k, 0, 0),
        "thigh_r": (44 * k, 0, 0), "thigh_l": (44 * k + 4, 0, 0),
        "calf_r": (-34 * max(0, k), 0, 0), "calf_l": (-34 * max(0, k), 0, 0),
        "foot_r": (-18 * k, 0, 0), "foot_l": (-18 * k, 0, 0),
        "spine_01": (12 * k, 0, 0), "pelvis": (-10 - 8 * k, 0, 0),
        "root@loc": (0, 0, -0.012 + 0.04 * (1 - abs(k)))}), "LINEAR")
scatter.close(8)


# Carry (0.53 s, loop, two of its strides): up on its hind legs under the crate, hunched forward with its back to the crate's underside and
# its head out under the front edge, the long arms up the crate's sides and the big hands flat on them, the fingers
# spread; the little legs pattering under it (the sim's 2.6 m/s: two quick steps a loop). (The sim holds the crate's
# middle holdHeight over its feet: its underside's at 0.56 m, as high as the Picker's shoulders, so it can't stand
# straight under it: it bears it on its hunched back.)
def carry_pose(step=0.0, bob=0.0):
    p = with_(CROUCH, {"root@loc": (0, 0.07, -0.1 + bob), "pelvis": (26, 0, 0), "spine_01": (2, 0, 0), "spine_02": (-2, 0, 0),
                       "neck": (-30, 0, 0), "head": (-2, 0, 0), "ear_r": (0, -30, -30), "ear_l": (0, 30, 30), "jaw": (-8, 0, 0)})
    for s, sx in (("r", 1), ("l", -1)):
        k = step if s == "r" else -step
        p = plant(p, s, (sx * 0.08, -0.2 + 0.08 * k, 0.05 + 0.035 * max(0.0, -k)))
        p[f"clavicle_{s}"] = (0, sx * 26, 0)
        p = reach_arm(p, s, (sx * (CRATE_HALF + 0.03), 0.08, HOLD - CRATE_HALF + 0.02), (sx * 0.5, 0.1, 0.35))
        p = palm_on(p, s, (-sx * 0.05, -0.1, 1), (-sx, 0, 0))
        p.update(curl(-12, -10, sides=(s,)))
    return p


carry = Clip("carry")
# (Two of its loops: on the second the head under the crate's edge jerks aside, and back.)
for f, step, bob, look in ((0, 1, 0.0, 0), (2, 0, 0.012, 0), (4, -1, 0.0, 0), (6, 0, 0.012, 0),
                           (8, 1, 0.0, 0), (10, 0, 0.012, 22), (12, -1, 0.0, 18), (14, 0, 0.012, 0)):
    carry.key(f, with_(carry_pose(step, bob), {"head": (-2, 0, look), "neck": (-30, 0, look * 0.3)}), "LINEAR")
carry.close(16)


# Heave (0.4 s, loop): the second under a heavy crate (its edge on its left), leant into it, both hands up under the
# edge, palms up, hoisting; braced, shuffling along with its lead.
def heave_pose(step=0.0, strain=0.0):
    edge = -(BESIDE - HEAVY_HALF)
    under = HOLD - HEAVY_HALF
    p = with_(CROUCH, {"root@loc": (0.0, 0.05, -0.05), "pelvis": (10, -14 - 3 * strain, 0), "spine_01": (4, -8, 0),
                       "spine_02": (2, -6, 0), "neck": (-14, 6, -16), "head": (0, 8, -18),
                       "ear_r": (-20, 0, -30), "ear_l": (-30, 0, 30), "jaw": (-18 - 10 * strain, 0, 0)})
    for s, sx in (("r", 1), ("l", -1)):
        k = step if s == "r" else -step
        p = plant(p, s, (sx * 0.1 + 0.02, -0.18 + 0.06 * k, 0.05 + 0.03 * max(0.0, -k)))
        p[f"clavicle_{s}"] = (0, sx * 10, 0)
    p = reach_arm(p, "l", (edge - 0.03, 0.12, under - 0.04 + 0.01 * strain), (-0.1, 0.1, 0.1))
    p = reach_arm(p, "r", (edge + 0.01, 0.0, under - 0.045 + 0.01 * strain), (0.25, 0.0, 0.25))
    p = palm_on(p, "l", (-1, 0.2, 0.1), (0, 0, 1))
    p = palm_on(p, "r", (-1, -0.1, 0.25), (0, 0, 1))
    p.update(curl(30, 10))
    return p


heave = Clip("heave")
for f, step, strain in ((0, 1, 0), (3, 0, 1), (6, -1, 0), (9, 0, 1)):
    heave.key(f, heave_pose(step, strain), "BEZIER")
heave.close(12)

# Grab (0.4 s: CreatureArt.PickerGrabSeconds): taking a crate up: crouched to it, the hands under its edges, a frozen
# beat; then up on its hind legs with it in one heave (the sim has it up at once: this is it getting under it).
grab = Clip("grab", loop=False)
REACH = with_(CROUCH, {"root@loc": (0, 0.05, -0.04), "pelvis": (8, 0, 0), "neck": (-10, 0, 0), "head": (-6, 0, 0),
                       "upperarm_r": (40, 0, 10), "upperarm_l": (40, 0, -10), "lowerarm_r": (-10, 0, 0), "lowerarm_l": (-10, 0, 0),
                       "hand_r": (-30, 0, 0), "hand_l": (-30, 0, 0), **curl(30, 20)})
grab.key(0, REACH, "LINEAR")
grab.key(3, with_(REACH, {"head": (-2, 0, 10)}), "LINEAR")
grab.key(5, rig.blend(REACH, carry_pose(0, -0.02), 0.5), "LINEAR")
grab.key(8, carry_pose(0, 0.025), "BEZIER")
grab.key(12, carry_pose(0, 0.0), "BEZIER")

# Bite (0.4 s: CreatureArt.PickerBiteSeconds): someone's come right up to it: it drops off the crate onto all fours,
# low, drawn back, its head squared on them and the jaw coming open, the ears pinned: held, three frames; then the lunge,
# in two, the jaw gaping wide, and the snap shut, held; it jerks back (open to a blow) and bolts.
COILED = with_(CROUCH, {"root@loc": (0, -0.04, -0.05), "pelvis": (4, 0, 0), "spine_01": (-6, 0, 0), "neck": (-12, 0, 0),
                        "head": (16, 0, 0), "jaw": (-26, 0, 0), "upperarm_r": (30, 0, 0), "upperarm_l": (30, 0, 0),
                        "ear_r": (-60, 0, -20), "ear_l": (-60, 0, 20)})
LUNGED = with_(CROUCH, {"root@loc": (0, 0.22, 0.02), "pelvis": (6, 0, 0), "spine_01": (4, 0, 0), "neck": (10, 0, 0), "head": (20, 0, 0),
                        "jaw": (-62, 0, 0), "upperarm_r": (50, 0, 6), "upperarm_l": (50, 0, -6), "lowerarm_r": (10, 0, 0),
                        "lowerarm_l": (10, 0, 0), "ear_r": (-60, 0, -10), "ear_l": (-60, 0, 10), **curl(-20, -10)})
bite = Clip("bite", loop=False)
bite.key(0, carry_pose(0, 0.0), "LINEAR")
bite.key(2, COILED, "LINEAR")
bite.key(5, with_(COILED, {"jaw": (-32, 0, 0), "root@loc": (0, -0.05, -0.055)}), "LINEAR")  # held, squared on them
bite.key(7, LUNGED, "LINEAR")
bite.key(8, with_(LUNGED, {"root@loc": (0, 0.26, 0.0), "jaw": (0, 0, 0), "head": (12, 0, 0)}), "CONSTANT")  # snap
bite.key(10, with_(CROUCH, {"root@loc": (0, 0.19, 0), "head": (-4, 0, 6), "jaw": (-20, 0, 0), "ear_r": (-40, 0, -10), "ear_l": (-40, 0, 10)}), "LINEAR")
bite.key(12, with_(CROUCH, {"root@loc": (0, 0.16, 0)}), "BEZIER")


# Emerge (0.8 s: the sim's emergeSeconds): up out of a drain: the hands first, over the rim in a snap, then the head,
# which stops dead, jerks round and freezes; then it hauls itself out onto all fours.
def hauling(rootz, pitch, reach=0.3, look=0.0):
    """Coming up out of the drain: the body pitched up by `pitch`, `rootz` under the ground, the hands flat on the ground
    in front (`reach` ahead), hauling."""
    p = with_(CROUCH, {"root@loc": (0, -0.05, rootz), "pelvis": (pitch, 0, 0), "spine_01": (6, 0, 0), "neck": (-pitch * 0.5, 0, look * 0.35),
                       "head": (-pitch * 0.2, 0, look * 0.65), "thigh_r": (-pitch, 0, 0), "thigh_l": (-pitch, 0, 0),
                       "ear_r": (-30, 0, -10), "ear_l": (-30, 0, 10)})
    for s, sx in (("r", 1), ("l", -1)):
        p = reach_arm(p, s, (sx * 0.15, reach, 0.04), (sx * 0.4, -0.2, 0.3))
        p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}")
    return p


emerge = Clip("emerge", loop=False)
emerge.key(0, with_(CROUCH, {"root@loc": (0, -0.1, -0.95), "pelvis": (70, 0, 0), "spine_01": (10, 0, 0), "neck": (-20, 0, 0),
                             "upperarm_r": (150, 0, 0), "upperarm_l": (150, 0, 0), "lowerarm_r": (10, 0, 0), "lowerarm_l": (10, 0, 0),
                             "ear_r": (-60, 0, -20), "ear_l": (-60, 0, 20), **curl(-10, 0)}), "LINEAR")
emerge.key(5, hauling(-0.62, 64, 0.32), "CONSTANT")
emerge.key(9, hauling(-0.46, 56, 0.35), "LINEAR")
emerge.key(10, hauling(-0.44, 55, 0.35, look=30), "LINEAR")                    # the head up: jerked round, frozen
emerge.key(14, hauling(-0.44, 55, 0.35, look=30), "LINEAR")
emerge.key(19, hauling(-0.14, 26, 0.34), "LINEAR")
emerge.key(24, CROUCH, "BEZIER")

# Descend (0.5 s): down its drain head first, the long arms going in before it, the hind legs last, kicking.
descend = Clip("descend", loop=False)
descend.key(0, CROUCH, "LINEAR")
descend.key(5, with_(CROUCH, {"root@loc": (0, 0.12, -0.2), "pelvis": (-40, 0, 0), "spine_01": (-14, 0, 0), "neck": (-20, 0, 0),
                              "head": (-20, 0, 0), "upperarm_r": (60, 0, 0), "upperarm_l": (60, 0, 0), "thigh_r": (40, 0, 0),
                              "thigh_l": (40, 0, 0), "ear_r": (-60, 0, -20), "ear_l": (-60, 0, 20)}), "LINEAR")
descend.key(10, with_(CROUCH, {"root@loc": (0, 0.2, -0.55), "pelvis": (-80, 0, 0), "spine_01": (-10, 0, 0), "neck": (-10, 0, 0),
                               "upperarm_r": (90, 0, 0), "upperarm_l": (90, 0, 0), "thigh_r": (60, 0, 0), "thigh_l": (20, 0, 0),
                               "calf_r": (-10, 0, 0), "ear_r": (-70, 0, -20), "ear_l": (-70, 0, 20)}), "LINEAR")
descend.key(15, with_(CROUCH, {"root@loc": (0, 0.24, -0.95), "pelvis": (-88, 0, 0), "thigh_r": (30, 0, 0), "thigh_l": (70, 0, 0),
                               "upperarm_r": (90, 0, 0), "upperarm_l": (90, 0, 0)}), "LINEAR")

hit = Clip("hit", loop=False)
hit.key(0, CROUCH, "CONSTANT")
hit.key(2, with_(CROUCH, {"pelvis": (-6, 0, 8), "spine_02": (-8, 0, 8), "neck": (10, 0, 10), "head": (14, 0, 14), "jaw": (-30, 0, 0),
                          "ear_r": (-40, 0, -20), "ear_l": (-40, 0, 20), "root@loc": (0, -0.05, 0.02), **curl(-20, -10)}), "CONSTANT")
hit.key(7, with_(CROUCH, {"pelvis": (2, 0, -4), "head": (-4, 0, -6)}), "LINEAR")
hit.key(12, CROUCH, "CONSTANT")

# Death (0.8 s): a blow kills one (they're frail): it's knocked over onto its side, kicks, and curls, the hands closing.
death = Clip("death", loop=False)
death.key(0, CROUCH, "CONSTANT")
death.key(3, with_(CROUCH, {"root": (0, 30, 0), "root@loc": (0, -0.04, 0.03), "head": (20, 0, 20), "jaw": (-40, 0, 0),
                            "upperarm_r": (30, 0, 0), "upperarm_l": (50, 0, 0), "ear_r": (-40, 0, -20), "ear_l": (-40, 0, 20)}), "LINEAR")
death.key(9, with_(CROUCH, {"root": (0, 84, 0), "root@loc": (0.0, -0.08, 0.06), "head": (10, 0, 10), "jaw": (-30, 0, 0),
                            "upperarm_r": (60, 0, 0), "upperarm_l": (40, 0, 0), "thigh_r": (40, 0, 0), "thigh_l": (10, 0, 0),
                            "ear_r": (-40, 0, -20), "ear_l": (-40, 0, 20), **curl(40, 20)}), "LINEAR")
death.key(14, with_(CROUCH, {"root": (0, 88, 0), "root@loc": (0.0, -0.08, 0.06), "head": (20, 0, 14), "jaw": (-34, 0, 0),
                             "upperarm_r": (30, 0, 0), "upperarm_l": (70, 0, 0), "lowerarm_r": (40, 0, 0), "lowerarm_l": (30, 0, 0),
                             "thigh_r": (50, 0, 0), "thigh_l": (30, 0, 0), "calf_r": (-40, 0, 0), "calf_l": (-20, 0, 0),
                             "ear_r": (-40, 0, -20), "ear_l": (-40, 0, 20), **curl(80, 40)}), "BEZIER")
death.key(24, with_(CROUCH, {"root": (0, 88, 0), "root@loc": (0.0, -0.08, 0.06), "head": (24, 0, 16), "jaw": (-36, 0, 0),
                             "upperarm_r": (50, 0, 0), "upperarm_l": (80, 0, 0), "lowerarm_r": (60, 0, 0), "lowerarm_l": (50, 0, 0),
                             "thigh_r": (60, 0, 0), "thigh_l": (44, 0, 0), "calf_r": (-60, 0, 0), "calf_l": (-50, 0, 0),
                             "ear_r": (-40, 0, -20), "ear_l": (-40, 0, 20), **curl(100, 50)}), "BEZIER")

CLIPS = [idle, scuttle, carry, heave, grab, bite, scatter, emerge, descend, hit, death]

if os.environ.get("DT_POSES"):
    # A quick look at the clips' keys without building the mesh: where the feet, the hands and the head are.
    want = [(f"{b}_{s}", "tail") for b in ("toe", "hand") for s in ("r", "l")] + [("head", "tail"), ("neck", "head"), ("pelvis", "head"), ("upperarm_r", "head"), ("hand_r", "head")]
    for clip in CLIPS:
        for frame, pose, _ in clip.keys:
            if os.environ.get("DT_CLIP") == f"{clip.name}:{frame}":
                print({k: tuple(round(x, 1) for x in (v.to_euler("YXZ") if isinstance(v, Quaternion) else v)) for k, v in pose.items() if "finger" not in k})
            P = pts(pose, want)
            print(f"{clip.name:8s} {frame:3d} " + " ".join(f"{n[0][:6]}{n[0][-2:] if n[0][-2] == '_' else ''}=({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})"
                                                       for n, p in zip(want, P)))
    sys.exit(0)

# One skin from the hind toes to the upper lip (rig.fuse, settled on the flesh's field, then QuadriFlow), the jaw a
# second: a beast's budget (GDD §27: 2-8k), kept light, as they come in a group of up to eight.
kit.fuse("body", ["body"], voxel=0.0022, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.fuse("jaw", ["jaw"], voxel=0.0016, faces=JAW_FACES, lose=0.03, settle=jflesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] picker", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "picker.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
