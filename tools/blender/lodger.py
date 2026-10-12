"""THE LODGER (GDD §21, OUTSIDE; ARCHITECTURE §8 note 583; the director, 9 Oct 2026; docs/design/creatures/lodger.md):
"it hides in the dark of a village house ... heard breathing from the next room. It shrieks and lunges."

A long, starved thing, human-ish but wrong: about 2.1 m when it rises, but it lives folded up in a corner, knees by its
ears. Very long limbs, bone-thin, the joints knobbed; ash-grey skin stretched over the bones, every rib and knuckle of the
spine standing in it; a narrow ribcage, deep as a greyhound's, the belly sucked in under it; long-fingered hands, the
fingertips past its knees. Its head is mostly jaw: a small skull up behind, and in front of it a long face of jaw sloping
down to a chin under its collarbones, the mouth's line running the whole length of it, so when it shrieks the jaw drops
right open, unhinged, a dark throat and two rows of thin teeth; two small sunk eyes up under the brow.

How it's made (the Look Review: organic, one skin, not primitives; the Mourners' and the Ribbit's way, notes 361, 362):
the feet to the crown and the upper jaw are one skin of smooth volumes (tools/blender/flesh.py), settled onto their field
before QuadriFlow; the lower jaw a second skin the same way (it opens wide). Over them, finer than their quads: the long
fingers and the thumbs, the toes, the teeth, the eyes' wet glints. Its colour is baked into one atlas by
tools/models/recipes/lodger.py.

Its own rig (SK_Lodger, 46 bones): root, pelvis, spine_01..03, neck, head, jaw; clavicles, arms, hands, three three-boned
fingers and a two-boned thumb a hand; thighs, calves, feet, toes. Its rest is stood up, stooped, facing +Y (the engine's
-Z). Clips (GDD §31: still, then too fast): hidden (folded into a corner, breathing), shriek (rearing up out of its crouch,
arms flung wide, the jaw dropped open: the telegraph, a second), lunge (a leap forward, arms out), chase (a fast loping
run, low), break (bashing at a door, clubbing it with both arms), return (a slow walk back), hit, death.

    tools/models/build.sh lodger        # this, with its colour baked -> content/art/models/lodger.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/lodger.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

FINGERS = ("a", "b", "c")


def lodger_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.25, 0)),
        Bone("pelvis", "root", (0, -0.02, 1.15), (0, -0.02, 1.25)),
        Bone("spine_01", "pelvis", (0, -0.02, 1.25), (0, -0.015, 1.41)),
        Bone("spine_02", "spine_01", (0, -0.015, 1.41), (0, 0.0, 1.57)),
        # (Stooped: the chest's top and the neck come forward over the hips.)
        Bone("spine_03", "spine_02", (0, 0.0, 1.57), (0, 0.04, 1.75)),
        Bone("neck", "spine_03", (0, 0.04, 1.75), (0, 0.075, 1.88)),
        Bone("head", "neck", (0, 0.075, 1.88), (0, 0.085, 2.05)),
        # The jaw, hinged far back under the skull, its chin down under the mouth: it drops right open.
        Bone("jaw", "head", (0, 0.05, 1.86), (0, 0.2, 1.72)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"clavicle_{s}", "spine_03", (sx * 0.03, 0.05, 1.71), (sx * 0.17, 0.02, 1.72)),
            # Long arms hanging, the fingertips past the knees.
            Bone(f"upperarm_{s}", f"clavicle_{s}", (sx * 0.19, 0.02, 1.7), (sx * 0.24, 0.0, 1.26)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.24, 0.0, 1.26), (sx * 0.25, 0.07, 0.84)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.25, 0.07, 0.84), (sx * 0.25, 0.09, 0.73)),
            Bone(f"thumb_{s}_01", f"hand_{s}", (sx * 0.225, 0.1, 0.8), (sx * 0.215, 0.14, 0.75)),
            Bone(f"thumb_{s}_02", f"thumb_{s}_01", (sx * 0.215, 0.14, 0.75), (sx * 0.21, 0.16, 0.7)),
            Bone(f"thigh_{s}", "pelvis", (sx * 0.1, -0.02, 1.14), (sx * 0.11, 0.03, 0.62)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.11, 0.03, 0.62), (sx * 0.11, -0.02, 0.09)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.11, -0.02, 0.09), (sx * 0.115, 0.13, 0.025)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.115, 0.13, 0.025), (sx * 0.12, 0.22, 0.018)),
        ]
        for i, f in enumerate(FINGERS):
            # Three long fingers hanging off the knuckles, a little spread, three joints each, curled a little at rest.
            dx = (i - 1) * 0.022
            k0 = Vector((sx * (0.25 + dx), 0.09 + 0.012 * (1 - abs(i - 1)), 0.73))
            k1 = k0 + Vector((sx * dx * 0.4, 0.012, -0.075))
            k2 = k1 + Vector((sx * dx * 0.2, 0.016, -0.06))
            k3 = k2 + Vector((0, 0.02, -0.045))
            b += [Bone(f"finger_{f}_{s}_01", f"hand_{s}", tuple(k0), tuple(k1)),
                  Bone(f"finger_{f}_{s}_02", f"finger_{f}_{s}_01", tuple(k1), tuple(k2)),
                  Bone(f"finger_{f}_{s}_03", f"finger_{f}_{s}_02", tuple(k2), tuple(k3))]
    return Skeleton("SK_Lodger", b)


sk = lodger_skeleton()
sk.build()
kit = rig.Kit(sk, "lodger")
SKIN_FACES, JAW_FACES = 3000, 420


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/lodger.py),
# the names say what to paint: the ash-grey hide, the mouth, the teeth, the eyes, the nails.
SKIN = Mat("skin.lodger", hexc("#8f8b85"), shine=0.12)
MOUTH = Mat("skin.lodger_mouth", hexc("#2a1212"), shine=0.5)
TOOTH = Mat("skin.lodger_tooth", hexc("#bdb59c"), shine=0.5)
NAIL = Mat("skin.lodger_nail", hexc("#2e2a26"), shine=0.3)
EYE = Mat("glass_dirty.lodger_eye", hexc("#111010"), shine=0.95)

# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the feet to the crown and the upper teeth (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "lodger flesh")
SPINE_PTS = [H("pelvis") + Vector((0, 0, -0.08)), H("spine_01"), H("spine_02"), H("spine_03"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "spine_03", "neck", "head"], soft=0.05)
# The mouth: its line round from the corners, far back under the cheekbones, to the front teeth; the jaw's under it.
MOUTH_Z = 1.805
HC = Vector((0, 0.13, MOUTH_Z + 0.01))
JC = Vector((0, 0.13, MOUTH_Z - 0.012))


def mouth_line(a):
    """A point on the mouth's line (in plan) by its angle round from the front (radians, ±π/2 the corners)."""
    return Vector((0.058 * math.sin(a), 0.085 + 0.115 * max(0.0, math.cos(a)) ** 0.7, MOUTH_Z))


def trunk_w(p):
    """Up the stooped spine; the shoulders' knobs go with the clavicles and the upper arms, the hips with the thighs."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.z > 1.6 and ax > 0.08:
        k = smooth01(0.08, 0.17, ax) * smooth01(1.6, 1.68, p.z)
        w = mix(w, {f"clavicle_{s}": 1.0}, 0.55 * k)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.5 * k * smooth01(0.15, 0.21, ax))
    d = (p - H(f"thigh_{s}")).length
    if d < 0.14 and p.z < 1.2:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.5 * smooth01(0.14, 0.05, d) * smooth01(0.03, 0.08, ax))
    return w


def head_w(p):
    p = Vector(p)
    w = trunk_w(p)
    # (Under the mouth's line the cheeks and the corners go with the jaw, more toward the corners: they stretch open with
    # it, the skin drawn down in sheets either side of the gape.)
    if p.z < MOUTH_Z + 0.02 and p.y > 0.0 and p.z > 1.7:
        k = smooth01(MOUTH_Z + 0.015, MOUTH_Z - 0.02, p.z) * smooth01(0.0, 0.05, p.y) * (0.45 + 0.4 * smooth01(0.03, 0.06, abs(p.x)))
        w = mix(w, {"jaw": 1.0}, k)
    return w


# The trunk: a small hard pelvis, the waist sucked in to nothing, the ribs' cage narrow and deep and long, the sternum
# a keel, the collarbones and the shoulders' knobs; every rib and the spine's knuckles under the stretched skin.
flesh.blob((0, -0.02, 1.17), (0.12, 0.08, 0.08), 0, SKIN, trunk_w)                                    # the pelvis
flesh.blob((0, -0.03, 1.29), (0.075, 0.06, 0.09), 0.06, SKIN, trunk_w)                                # the waist
flesh.blob((0, 0.005, 1.5), (0.115, 0.1, 0.17), 0.07, SKIN, trunk_w, rot=R(rx=-6))                     # the ribs' cage
flesh.blob((0, 0.03, 1.66), (0.13, 0.09, 0.07), 0.06, SKIN, trunk_w, rot=R(rx=-12))                    # the chest's top
flesh.blob((0, 0.075, 1.48), (0.03, 0.03, 0.12), 0.03, SKIN, trunk_w, rot=R(rx=-8))                    # the sternum's keel
for sx in (1, -1):
    flesh.blob((sx * 0.19, 0.01, 1.69), (0.05, 0.05, 0.045), 0.04, SKIN, trunk_w)                     # the shoulder's knob
    flesh.limb([(sx * 0.03, 0.08, 1.71), (sx * 0.1, 0.07, 1.725), (sx * 0.18, 0.03, 1.715)], [0.018, 0.016, 0.02], 0.025, SKIN, trunk_w)
    flesh.blob((sx * 0.08, -0.07, 1.6), (0.05, 0.025, 0.08), 0.03, SKIN, trunk_w, rot=R(rz=sx * 10))   # the shoulder blade
    flesh.blob((sx * 0.1, 0.03, 1.2), (0.03, 0.03, 0.03), 0.03, SKIN, trunk_w)                         # the hip bone
# The belly sucked in under the ribs; the spine's knuckles in a row up the back.
flesh.carve((0, 0.06, 1.3), (0.07, 0.04, 0.07), 0.04)
for k in range(12):
    t = k / 11
    c = Vector((0, -0.02, 1.2)).lerp(Vector((0, 0.03, 1.74)), t)
    top = flesh.surface(c, (0, -1, 0.1), 0.3)
    flesh.blob(top - Vector((0, -0.008, 0)), (0.017, 0.013, 0.016), 0.012, SKIN, trunk_w, around=10, rings=6)

# The arms: bone-thin and very long, the elbows knobbed, the forearms all sinew; the hands long, the palms narrow.
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([H(f"clavicle_{s}"), sh, el, wr, tip], [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.04)
    flesh.limb([sh + Vector((0, 0, 0.01)), sh.lerp(el, 0.4), el], [0.045, 0.036, 0.03], 0.04, SKIN,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(1.6, 1.7, p[2]) * 0.6))
    flesh.blob(el + Vector((0, -0.012, 0)), (0.033, 0.034, 0.036), 0.015, SKIN, arm, around=12, rings=8)      # the elbow
    flesh.limb([el, el.lerp(wr, 0.35), wr], [0.03, 0.028, 0.019], 0.015, SKIN, arm)
    flesh.blob(wr.lerp(tip, 0.55), (0.03, 0.017, 0.055), 0.02, SKIN, arm, around=14, rings=6)          # the palm
    flesh.blob(wr + Vector((0, 0, -0.008)), (0.022, 0.016, 0.02), 0.012, SKIN, arm, around=10, rings=6)     # the wrist

# The legs: long, thin, the knees big knobs, the shins all bone, the feet long and flat.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.04)
    flesh.limb([hip + Vector((0, 0, 0.03)), hip.lerp(kn, 0.4), kn], [0.06, 0.048, 0.034], 0.04, SKIN,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(1.08, 1.16, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.015, 0)), (0.04, 0.038, 0.045), 0.015, SKIN, leg, around=12, rings=8)        # the knee
    flesh.limb([kn, kn.lerp(an, 0.35), an], [0.034, 0.03, 0.022], 0.015, SKIN, leg)
    flesh.blob(an + Vector((0, -0.015, -0.01)), (0.026, 0.035, 0.03), 0.012, SKIN, leg)                       # the heel
    flesh.blob(an.lerp(ball, 0.55) + Vector((0, 0, -0.03)), (0.036, 0.1, 0.02), 0.025, SKIN, leg, rot=R(rx=-14))  # the foot
    flesh.blob(ball + Vector((0, 0.02, -0.006)), (0.04, 0.035, 0.016), 0.02, SKIN, leg)                       # its ball

# The neck, long and thin, the windpipe standing in it; the head: a long narrow skull, a heavy brow over two small sunk
# eyes, the cheekbones high and the cheeks fallen in under them, a flat ruin of a nose; and below, most of it, the mouth:
# its line runs right back under the cheekbones, the upper teeth hung in a long upper jaw, the roof of the mouth vaulted
# deep inside it (it shows when the jaw drops).
flesh.limb([H("neck") + Vector((0, -0.02, -0.01)), H("neck").lerp(T("neck"), 0.5), T("neck") + Vector((0, -0.01, 0.02))],
           [0.05, 0.042, 0.045], 0.04, SKIN, trunk_w)
flesh.blob((0, 0.062, 1.8), (0.02, 0.02, 0.055), 0.02, SKIN, trunk_w, rot=R(rx=-15))                  # the windpipe
SKULL = Vector((0, 0.07, 1.985))
flesh.blob(SKULL, (0.072, 0.092, 0.08), 0.04, SKIN, head_w, around=20, rings=12)                      # the skull
flesh.blob((0, 0.135, 1.955), (0.068, 0.032, 0.028), 0.03, SKIN, head_w)                              # the brow
flesh.blob((0, 0.13, 1.86), (0.058, 0.07, 0.07), 0.04, SKIN, head_w)                                  # the upper face
flesh.blob((0, 0.17, 1.83), (0.048, 0.04, 0.035), 0.03, SKIN, head_w)                                 # the upper jaw
flesh.blob((0, 0.185, 1.9), (0.016, 0.018, 0.035), 0.015, SKIN, head_w, rot=R(rx=10))                 # the nose's ruin
for sx in (1, -1):
    flesh.blob((sx * 0.062, 0.1, 1.9), (0.02, 0.05, 0.022), 0.025, SKIN, head_w)                     # the cheekbone
    flesh.carve((sx * 0.06, 0.13, 1.855), (0.018, 0.03, 0.03), 0.02)                                 # the fallen cheek
    flesh.carve((sx * 0.013, 0.2, 1.885), (0.005, 0.006, 0.01), 0.004)                               # a nostril's slit
EYES = []
for sx in (1, -1):
    e = Vector((sx * 0.034, 0.142, 1.925))
    EYES.append(e)
    flesh.carve(e + Vector((0, 0.008, 0.002)), (0.019, 0.016, 0.016), 0.012)                          # the socket
# The upper lip: thin, along the mouth's line round from one corner to the other.
LINE = []
for k in range(11):
    a = math.radians(-90 + 18 * k)
    m = mouth_line(a)
    out = Vector((math.sin(a), 0.6 * math.cos(a), 0)).normalized()
    LINE.append(flesh.surface(Vector((0, m.y * 0.7, MOUTH_Z + 0.008)), out, 0.15) - out * 0.004)
flesh.limb(LINE, [0.006] + [0.008] * (len(LINE) - 2) + [0.006], 0.008, SKIN, head_w, sides=8, ref=(0, 0, 1))
flesh.carve((0, 0.12, MOUTH_Z - 0.004), (0.046, 0.075, 0.022), 0.012)                                # the mouth's roof

# ----------------------------------------------------------------------------------------------------------------
# The jaw: huge, a long heavy scoop under the face, hinged far back under the skull, the chin long; the floor of the mouth
# hollowed. Its own skin (rig.fuse): it drops right open.
jaw = kit.part("jaw")
jflesh = Flesh(jaw, "lodger jaw")


def jaw_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    return MOUTH if n.z > 0.5 and c.z > MOUTH_Z - 0.03 and abs(c.x) < 0.045 else SKIN


jflesh.blob((0, 0.12, 1.78), (0.056, 0.085, 0.032), 0, SKIN, "jaw", around=20, rings=10, fmat=jaw_mat)
jflesh.blob((0, 0.18, 1.755), (0.04, 0.04, 0.045), 0.035, SKIN, "jaw", fmat=jaw_mat)                        # the chin
for sx in (1, -1):
    jflesh.blob((sx * 0.06, 0.06, 1.83), (0.016, 0.03, 0.045), 0.03, SKIN, "jaw", rot=R(rx=20), fmat=jaw_mat)  # the hinge
LOWLINE = []
for k in range(11):
    a = math.radians(-90 + 18 * k)
    m = mouth_line(a)
    out = Vector((math.sin(a), 0.6 * math.cos(a), 0)).normalized()
    LOWLINE.append(jflesh.surface(Vector((0, m.y * 0.7, MOUTH_Z - 0.01)), out, 0.15) - out * 0.003)
jflesh.limb(LOWLINE, [0.006] + [0.007] * (len(LOWLINE) - 2) + [0.006], 0.007, SKIN, "jaw", sides=8, ref=(0, 0, 1))
jflesh.carve((0, 0.12, MOUTH_Z - 0.002), (0.042, 0.07, 0.022), 0.01)                                 # the mouth's floor

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the eyes, the teeth, the long fingers, the thumbs and the toes.
fine = kit.part("fine")
for e in EYES:
    fine.blob(e, (0.0105, 0.009, 0.009), 8, 5, EYE, "head")
TEETH_AT = []


def teeth(line, n, drop, long_, bone, seed):
    """Thin, long, crooked teeth along a lip's line just inside it, longest at the front (hashed: never even)."""
    for k in range(n):
        t = k * (len(line) - 1) / (n - 1)
        i0 = min(int(t), len(line) - 2)
        c = line[i0].lerp(line[i0 + 1], t - i0)
        inward = (Vector((0, HC.y, c.z)) - c)
        inward.z = 0
        c = c + inward.normalized() * 0.008
        front = smooth01(0.12, 0.19, c.y)
        L = long_ * (0.55 + 0.45 * front) * (0.75 + 0.5 * h01(seed, k))
        lean = Vector((0.004 * (h01(seed + 1, k) - 0.5), 0.004 * (h01(seed + 2, k) - 0.5), 0))
        base = c + Vector((0, 0, 0.006 if drop else -0.006))
        TEETH_AT.append(base)
        tip = base + Vector((0, 0, -L if drop else L)) + lean
        fine.tube([base, base.lerp(tip, 0.55), tip], [0.0042, 0.0032, 0.0008], 4, TOOTH, bone, ref=(0, 1, 0), cap1="point")


teeth(LINE, 22, True, 0.026, "head", 31)
teeth(LOWLINE, 20, False, 0.022, "jaw", 32)

TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for f in FINGERS:
        k0, k1, k2, k3 = H(f"finger_{f}_{s}_01"), H(f"finger_{f}_{s}_02"), H(f"finger_{f}_{s}_03"), T(f"finger_{f}_{s}_03")
        a = k0 - (k1 - k0).normalized() * 0.02
        bones = chain([a, k1, k2, k3], [f"finger_{f}_{s}_01", f"finger_{f}_{s}_02", f"finger_{f}_{s}_03"], soft=0.012)
        nail = k3 + (k3 - k2).normalized() * 0.008
        TIPS.append(nail)
        fine.tube([a, a.lerp(k1, 0.5), k1, k1.lerp(k2, 0.5), k2, k2.lerp(k3, 0.6), nail],
                  [0.0115, 0.0105, 0.0112, 0.0095, 0.0098, 0.008, 0.0035], 6, SKIN, bones, ref=(0, 1, 0), cap0="point", cap1="point",
                  fmat=lambda pts, n, k3=k3: NAIL if (sum(pts, Vector()) / len(pts) - k3).length < 0.012 else SKIN)
    t0, t1, t2 = H(f"thumb_{s}_01"), H(f"thumb_{s}_02"), T(f"thumb_{s}_02")
    a = t0 - (t1 - t0).normalized() * 0.015
    fine.tube([a, t1, t1.lerp(t2, 0.6), t2 + (t2 - t1).normalized() * 0.006], [0.013, 0.011, 0.009, 0.0035], 6, SKIN,
              chain([a, t1, t2], [f"thumb_{s}_01", f"thumb_{s}_02"], soft=0.012), ref=(0, 1, 0), cap0="point", cap1="point",
              fmat=lambda pts, n, t2=t2: NAIL if (sum(pts, Vector()) / len(pts) - t2).length < 0.012 else SKIN)
    ball = H(f"toe_{s}")
    for k in range(4):
        dx = (k - 1.5) * 0.017
        a0 = ball + Vector((sx * dx, -0.012, 0.004))
        end = a0 + Vector((sx * dx * 0.3, 0.075 - 0.012 * abs(k - 1.5), -0.006))
        TIPS.append(end)
        fine.tube([a0, a0.lerp(end, 0.55) + Vector((0, 0, 0.006)), end, end + Vector((0, 0.01, -0.006))], [0.011, 0.009, 0.006, 0.002], 5,
                  SKIN, f"toe_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, n, end=end: NAIL if (sum(pts, Vector()) / len(pts) - end).length < 0.012 else SKIN)


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest is stood up, stooped; angles in the armature's axes: +X swings a hanging limb forward and tips a spine
# or the head back; a finger curls with -X; the jaw opens with -X.

def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


def curl(k, thumb=0.0):
    out = {}
    for s in ("r", "l"):
        for i, f in enumerate(FINGERS):
            out[f"finger_{f}_{s}_01"] = (-k * (0.8 + 0.1 * i), 0, 0)
            out[f"finger_{f}_{s}_02"] = (-k, 0, 0)
            out[f"finger_{f}_{s}_03"] = (-k * 0.8, 0, 0)
        out[f"thumb_{s}_01"] = (-thumb, 0, 0)
        out[f"thumb_{s}_02"] = (-thumb, 0, 0)
    return out


STAND = with_({"jaw": (-2, 0, 0), "jaw@loc": (0, 0, 0)}, curl(10, 6))


def euler(q):
    return tuple(round(math.degrees(a), 2) for a in q.to_euler("YXZ"))


def plant(pose, s, target, pole, foot=(0, 0, 0), high=None):
    """A leg posed so its ankle lands at `target`, the knee toward `pole` (and kept over its own side: never across the
    other leg), above `high` if given; the foot then turned to world `foot`."""
    x = H(f"thigh_{s}").x
    avoid = (lambda e: 0.4 * abs(e.x - x) + (2.0 * max(0.0, high - e.z) if high is not None else 0.0))  # noqa: E731
    p = rig.reach(sk, pose, f"thigh_{s}", f"calf_{s}", Vector(target), elbow_axis=0, bend=-1, pole=Vector(pole), avoid=avoid)
    p[f"foot_{s}"] = euler(rig.hang(sk, p, f"foot_{s}", *foot))
    return p


def reach_arm(pose, s, wrist, pole):
    return rig.reach(sk, pose, f"upperarm_{s}", f"lowerarm_{s}", Vector(wrist), elbow_axis=0, bend=1, pole=Vector(pole))


def mirrored(pose):
    """`pose` with its left side the right's mirrored (the right's reach solved once, so the two sides match)."""
    return rig.mirror({k: v for k, v in pose.items() if not rig._split(k)[0].endswith("_l")})


def legs(pose, r, l, knee=0.5, foot_r=(0, 0, 0), foot_l=(0, 0, 0)):
    """Both legs planted: the right ankle at `r`, the left at `l` (x given for the right side; the left's mirrored)."""
    p = dict(pose)
    for s, sx, a, ft in (("r", 1, r, foot_r), ("l", -1, l, foot_l)):
        a = Vector((sx * a[0], a[1], a[2]))
        p = plant(p, s, a, a + Vector((sx * 0.06, knee, 0.6)), ft)
    return p


# Hidden (3 s, loop): folded into a corner: squatted right down on its heels, the knees up by its ears, its long arms
# folded over them and the hands hanging, the head down between the knees; breathing, slow and deep, the ribs heaving
# and the shoulders lifting with it; now and then the head twitching.
def folded(breath=0.0, twitch=0.0):
    p = with_(STAND, {"root@loc": (0, -0.12, -0.83), "pelvis": (-26, 0, 0), "spine_01": (-16 - 3 * breath, 0, 0),
                      "spine_02": (-14 + 4 * breath, 0, 0), "spine_03": (-10 + 3 * breath, 0, 0), "neck": (-26, 0, 0),
                      "head": (-22, 0, 0), "jaw": (-4 - 3 * breath, 0, 0), "clavicle_r": (0, -4 * breath, 0), **curl(26, 14)})
    p = plant(p, "r", (0.19, 0.06, 0.09), (0.22, 0.3, 1.3), high=0.56)
    p = reach_arm(p, "r", (0.21, 0.5, 0.32), (0.55, 0.2, 0.75))
    p = mirrored(p)
    p["neck"] = (-26, 0, twitch * 12)
    p["head"] = (-22 + twitch * 8, 0, twitch * 18)
    return p


hidden = Clip("hidden")
for f, br, tw in ((0, 0, 0), (24, 1, 0), (44, 0.2, 0), (46, 0.2, 1), (58, 0.2, 1), (60, 0.0, 0), (72, 0, 0)):
    hidden.key(f, folded(br, tw), "CONSTANT" if f in (46, 60) else "BEZIER")
hidden.close(90)


# Shriek (1 s, held: enemies.json lodger shriekSeconds): up out of its crouch all at once, rearing to its full height, the
# arms flung out wide, its face to you and the jaw dropped right open, unhinged, the whole of it shaking with the scream.
def reared(shake=0.0, open_=1.0):
    p = with_(STAND, {
        "root@loc": (0, -0.06, 0.0), "pelvis": (6, 0, 0), "spine_01": (6, 0, shake * 2), "spine_02": (8, 0, -shake * 2),
        "spine_03": (-4, 0, shake * 3), "neck": (-6, 0, -shake * 4), "head": (6, 0, shake * 5), "jaw": (-56 * open_ - 3 * shake, 0, 0),
        "jaw@loc": (0, 0.01 * open_, -0.035 * open_),
        "clavicle_r": (0, -14, 0), "upperarm_r": (20, -78 - 4 * shake, 0), "lowerarm_r": (30, 0, 0), "hand_r": (10, 0, 0),
        "thigh_r": (6, 0, 4), "calf_r": (-10, 0, 0), "foot_r": (4, 0, 0), **curl(-24, -10)})
    return mirrored(p)


def crouched(arms=24, pelvis=-34, at=-0.05):
    p = with_(STAND, {"root@loc": (0, at, -0.42), "pelvis": (pelvis, 0, 0), "spine_01": (-8, 0, 0), "spine_02": (-6, 0, 0),
                      "neck": (-10, 0, 0), "head": (-14, 0, 0), "upperarm_r": (arms, -10, 0), "lowerarm_r": (20, 0, 0), **curl(30, 16)})
    return mirrored(legs(p, (0.17, 0.17 + at, 0.09), (0.17, 0.17 + at, 0.09), knee=0.6))


CROUCHED = crouched()
shriek = Clip("shriek", loop=False)
shriek.key(0, CROUCHED, "LINEAR")
shriek.key(5, rig.blend(CROUCHED, reared(0, 0.5), 0.6), "LINEAR")
for f, sh in ((9, 1), (12, -1), (15, 1), (18, -1), (21, 1), (24, -1), (27, 0.5)):
    shriek.key(f, reared(sh), "LINEAR")
shriek.key(30, reared(0, 0.9), "BEZIER")

# Lunge (0.6 s): from the shriek: down and out: it drops and springs, laid out flat in the air, the long arms out in
# front, the hands open; it lands crouched, low.
lunge = Clip("lunge", loop=False)
lunge.key(0, reared(0, 0.9), "LINEAR")
lunge.key(3, with_(crouched(-20), {"jaw": (-36, 0, 0)}), "LINEAR")
lunge.key(7, mirrored(with_(STAND, {"root@loc": (0, 0.06, -0.12), "pelvis": (-80, 0, 0), "spine_01": (-4, 0, 0), "neck": (30, 0, 0),
                                    "head": (24, 0, 0), "jaw": (-46, 0, 0), "upperarm_r": (165, -18, 0), "lowerarm_r": (6, 0, 0),
                                    "thigh_r": (8, 0, 0), "calf_r": (-30, 0, 0), **curl(-30, -20)})), "LINEAR")
lunge.key(12, with_(crouched(70, -50, 0.08), {"neck": (16, 0, 0), "head": (10, 0, 0),
                                   "jaw": (-30, 0, 0)}), "BEZIER")
lunge.key(18, with_(crouched(50, -44, 0.08), {"jaw": (-14, 0, 0)}), "BEZIER")


# Chase (0.4 s, loop: enemies.json lodger chase, 5 m/s, a metre a stride): a fast loping run, low, pitched forward from
# the hips, the long arms swinging and reaching, the head out in front, the jaw hanging a little open.
def lope(k, lift):
    p = with_(STAND, {"root@loc": (0, 0, -0.2 + 0.05 * lift), "pelvis": (-40, 0, -4 * k), "spine_01": (-6, 0, 0),
                      "spine_02": (-4, 0, 6 * k), "spine_03": (4, 0, 0), "neck": (24, 0, 0), "head": (14, 0, 0), "jaw": (-16, 0, 0),
                      "upperarm_r": (30 - 55 * k, -6, 0), "upperarm_l": (30 + 55 * k, 6, 0),
                      "lowerarm_r": (20 + 25 * max(0, k), 0, 0), "lowerarm_l": (20 + 25 * max(0, -k), 0, 0), **curl(20, 10)})
    return p


chase = Clip("chase")
chase.key(0, legs(lope(1, 0), (0.12, 0.5, 0.09), (0.12, -0.5, 0.4), foot_l=(-25, 0, 0)), "LINEAR")
chase.key(3, legs(lope(0, 1), (0.12, -0.05, 0.09), (0.12, 0.25, 0.45)), "LINEAR")
chase.key(6, legs(lope(-1, 0), (0.12, -0.5, 0.4), (0.12, 0.5, 0.09), foot_r=(-25, 0, 0)), "LINEAR")
chase.key(9, legs(lope(0, 1), (0.12, 0.25, 0.45), (0.12, -0.05, 0.09)), "LINEAR")
chase.close(12)

# Break (1 s, loop): at a shut door: reared back, both arms up over its head, then flung down and forward against it
# with all its length, the shoulder after them; again.
brk = Clip("break")
UP = mirrored(legs(with_(STAND, {"root@loc": (0, -0.12, -0.03), "pelvis": (8, 0, 0), "spine_02": (8, 0, 0), "spine_03": (6, 0, 0),
                                 "neck": (6, 0, 0), "jaw": (-24, 0, 0), "upperarm_r": (170, -16, 0), "lowerarm_r": (30, 0, 0),
                                 **curl(40, 20)}), (0.13, 0.15, 0.09), (0.13, 0.15, 0.09)))
SLAM = legs(with_(STAND, {"root@loc": (0, 0.14, -0.14), "pelvis": (-26, 0, 10), "spine_01": (-10, 0, 0), "spine_02": (-8, 0, 6),
                          "neck": (10, 0, 0), "head": (6, 0, 0), "jaw": (-36, 0, 0), "upperarm_r": (90, -8, 0), "upperarm_l": (100, 8, 0),
                          "lowerarm_r": (6, 0, 0), "lowerarm_l": (6, 0, 0), **curl(60, 30)}), (0.13, 0.45, 0.09), (0.13, -0.2, 0.09))
brk.key(0, UP, "BEZIER")
brk.key(10, UP, "LINEAR")
brk.key(14, SLAM, "CONSTANT")
brk.key(20, with_(SLAM, {"jaw": (-30, 0, 0)}), "BEZIER")
brk.close(30)


# Return (0.93 s, loop: enemies.json lodger walk, 1.6 m/s): a slow walk back to its room, stooped, the arms hanging and
# swinging long, the head low and turning.
def stroll(k, rise):
    return with_(STAND, {"root@loc": (0, 0, -0.07 + 0.05 * rise), "pelvis": (-10, 0, 3 * k), "spine_02": (-6, 0, 4 * k),
                         "spine_03": (-6, 0, 0), "neck": (-10, 0, 0), "head": (-8, 0, 10 * k),
                         "upperarm_r": (4 - 14 * k, -4, 0), "upperarm_l": (4 + 14 * k, 4, 0)})


ret = Clip("return")
ret.key(0, legs(stroll(1, 0), (0.12, 0.36, 0.09), (0.12, -0.36, 0.11)), "BEZIER")
ret.key(7, legs(stroll(0, 1), (0.12, 0.0, 0.09), (0.12, 0.05, 0.22)), "BEZIER")
ret.key(14, legs(stroll(-1, 0), (0.12, -0.36, 0.11), (0.12, 0.36, 0.09)), "BEZIER")
ret.key(21, legs(stroll(0, 1), (0.12, 0.05, 0.22), (0.12, 0.0, 0.09)), "BEZIER")
ret.close(28)

hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(2, mirrored(with_(STAND, {"pelvis": (8, 0, 10), "spine_02": (10, 0, 14), "neck": (10, 0, 16), "head": (16, 0, 24), "jaw": (-40, 0, 0),
                                  "upperarm_r": (40, -30, 0), "root@loc": (0, -0.08, 0.0), **curl(-20, -10)})), "CONSTANT")
hit.key(8, with_(STAND, {"pelvis": (2, 0, -4), "head": (4, 0, -6)}), "LINEAR")
hit.key(12, STAND, "CONSTANT")

# Death (1.2 s): its legs go and it folds down where it stood onto its knees, then over onto its side, all its length in
# a heap, the jaw hanging.
death = Clip("death", loop=False)
death.key(0, STAND, "CONSTANT")
death.key(4, mirrored(with_(STAND, {"pelvis": (10, 0, 6), "spine_02": (10, 0, 8), "head": (24, 0, 14), "jaw": (-40, 0, 0),
                                    "upperarm_r": (30, -30, 0), "root@loc": (0, -0.05, -0.04)})), "LINEAR")
death.key(14, mirrored(legs(with_(STAND, {"root@loc": (0, 0.0, -0.58), "pelvis": (-20, 0, 0), "spine_01": (-10, 0, 0), "neck": (-20, 0, 0),
                                          "head": (-10, 0, 0), "jaw": (-36, 0, 0), "upperarm_r": (20, -10, 0), **curl(40, 20)}),
                            (0.14, -0.45, 0.12), (0.14, -0.45, 0.12), knee=0.8, foot_r=(0, 0, 180), foot_l=(0, 0, 180))), "LINEAR")
LYING = with_(STAND, {"root@loc": (0, -0.1, -0.98), "pelvis": (-84, 0, 6), "spine_01": (-2, 0, 0), "spine_02": (2, 0, 0),
                      "neck": (10, 0, 30), "head": (10, 0, 40), "jaw": (-38, 0, 0), "thigh_r": (-6, 0, -6), "thigh_l": (-10, 0, 10),
                      "calf_r": (-20, 0, 0), "calf_l": (-60, 0, 0), "upperarm_r": (150, -10, 0), "upperarm_l": (-10, 10, 0),
                      "lowerarm_r": (50, 0, 0), "lowerarm_l": (0, 0, 0), **curl(60, 30)})
death.key(24, LYING, "BEZIER")
death.key(36, with_(LYING, {"jaw": (-42, 0, 0), "head": (-8, 0, 24), **curl(70, 30)}), "BEZIER")

CLIPS = [hidden, shriek, lunge, chase, brk, ret, hit, death]

if os.environ.get("DT_POSES"):
    want = [(f"{b}_{s}", "tail") for b in ("toe", "hand") for s in ("r", "l")] + [("head", "head"), ("jaw", "tail"), ("pelvis", "head"), ("spine_03", "head"),
                                                                               (f"calf_r", "head")]
    for clip in CLIPS:
        for frame, pose, _ in clip.keys:
            P = rig.pose_points(sk, pose, want)
            print(f"{clip.name:8s} {frame:3d} " + " ".join(f"{n[0][:7]}=({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})" for n, p in zip(want, P)))
    sys.exit(0)

# One skin from the feet to the crown (rig.fuse, settled on the flesh's field, then QuadriFlow), the jaw a second: a
# character's budget (GDD §27: 4-10k).
kit.fuse("body", ["body"], voxel=0.0035, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.fuse("jaw", ["jaw"], voxel=0.0025, faces=JAW_FACES, lose=0.03, settle=jflesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] lodger", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "lodger.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
