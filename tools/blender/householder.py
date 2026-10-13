"""THE HOUSEHOLDER (GDD §21, OUTSIDE; ARCHITECTURE §8 note 584; the director, 9 Oct 2026; docs/design/creatures/householder.md):
"a gaunt figure at its set table in a lived-in-looking house, harmless to guests. Take one of its house's things out of the
door unpaid and it gets up and hunts whoever carries it."

Tall and thin, about 1.95 m stood, in its rotting Sunday best: a waistcoat buttoned to the breastbone, a watch chain across
it, a collarless shirt gone yellow under it with the sleeves too short, so the bony wrists and a hand's width of forearm
show; dark trousers, worn shoes. Grey, dry skin; a long face too still, the eyes wide and wet and always on you (CreatureArt
turns the head to whoever's looking); fingers too long, a joint too many, resting on its table.

How it's made (the Look Review: organic, one skin, not primitives; the Mourners' way, note 362): the body and its clothes are
one skin of smooth volumes (tools/blender/flesh.py) settled onto their field before QuadriFlow, each face taking the cloth
of the volume nearest it (the waistcoat over the chest, the shirt in its V and down the arms, the trousers, the shoes, the
skin of the face, the wrists and the hands). Over it, finer than its quads: the waistcoat's hem and the cuffs' edges, the
buttons, the watch chain, the eyes, the ears, the long fingers. Its colour is baked into one atlas by
tools/models/recipes/householder.py.

Its own rig (SK_Householder, 51 bones): root, pelvis, spine_01..03, neck, head; clavicles, arms, hands, four three-boned
fingers and a two-boned thumb a hand; thighs, calves, feet, toes. Its rest is stood straight, facing +Y (the engine's -Z).
Its chair's seat is 0.45 m high, at its origin; its table 0.7 m in front (Householder.TableOut), the top 0.76 m up.
Clips (GDD §31: still, then too fast): sit (sat at its table, hands flat on it, still, a finger lifting and setting
down), watch (sat, leant a little to you: CreatureArt turns the head), rise (getting up, slowly, hands pressing on the
table), hunt (a stiff, relentless walk), grab (both hands out, seized on someone, wrenching), return (walking back to its
chair), hit, death (over backward, stiff as a board).

    tools/models/build.sh householder   # this, with its colour baked -> content/art/models/householder.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/householder.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from mathutils import Matrix  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

FINGERS = ("a", "b", "c", "d")
SEAT, TABLE_OUT, TABLE_TOP = 0.45, 0.7, 0.75


def householder_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, 0, 1.0), (0, 0, 1.1)),
        Bone("spine_01", "pelvis", (0, 0, 1.1), (0, 0, 1.25)),
        Bone("spine_02", "spine_01", (0, 0, 1.25), (0, 0.0, 1.4)),
        Bone("spine_03", "spine_02", (0, 0.0, 1.4), (0, 0.01, 1.56)),
        Bone("neck", "spine_03", (0, 0.01, 1.56), (0, 0.03, 1.7)),
        Bone("head", "neck", (0, 0.03, 1.7), (0, 0.03, 1.93)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"clavicle_{s}", "spine_03", (sx * 0.03, 0.02, 1.53), (sx * 0.16, 0.0, 1.54)),
            Bone(f"upperarm_{s}", f"clavicle_{s}", (sx * 0.18, 0.0, 1.52), (sx * 0.21, -0.01, 1.18)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.21, -0.01, 1.18), (sx * 0.225, 0.03, 0.89)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.225, 0.03, 0.89), (sx * 0.23, 0.04, 0.79)),
            Bone(f"thumb_{s}_01", f"hand_{s}", (sx * 0.21, 0.06, 0.86), (sx * 0.205, 0.095, 0.82)),
            Bone(f"thumb_{s}_02", f"thumb_{s}_01", (sx * 0.205, 0.095, 0.82), (sx * 0.2, 0.11, 0.78)),
            Bone(f"thigh_{s}", "pelvis", (sx * 0.09, 0.0, 0.98), (sx * 0.095, 0.02, 0.53)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.095, 0.02, 0.53), (sx * 0.095, -0.01, 0.09)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.095, -0.01, 0.09), (sx * 0.1, 0.13, 0.03)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.1, 0.13, 0.03), (sx * 0.1, 0.21, 0.025)),
        ]
        for i, f in enumerate(FINGERS):
            # Four long fingers hanging off the knuckles, three joints each, a joint too long.
            dx = (i - 1.5) * 0.017
            k0 = Vector((sx * (0.23 + dx * 0.3), 0.04 + 0.014 * (1 - abs(i - 1.5)) - 0.004 * i, 0.79))
            L = (0.05, 0.042, 0.034) if i in (1, 2) else (0.044, 0.036, 0.03)
            k1 = k0 + Vector((sx * dx * 0.2, 0.006, -L[0]))
            k2 = k1 + Vector((0, 0.008, -L[1]))
            k3 = k2 + Vector((0, 0.01, -L[2]))
            b += [Bone(f"finger_{f}_{s}_01", f"hand_{s}", tuple(k0), tuple(k1)),
                  Bone(f"finger_{f}_{s}_02", f"finger_{f}_{s}_01", tuple(k1), tuple(k2)),
                  Bone(f"finger_{f}_{s}_03", f"finger_{f}_{s}_02", tuple(k2), tuple(k3))]
    return Skeleton("SK_Householder", b)


sk = householder_skeleton()
sk.build()
kit = rig.Kit(sk, "householder")
SKIN_FACES = 3600


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/householder.py),
# the names say what to paint: the grey skin, the waistcoat, the shirt, the trousers, the shoes, the buttons, the eyes.
SKIN = Mat("skin.householder", hexc("#8d8a84"), shine=0.1)
FACE = Mat("skin.householder_face", hexc("#8d8a84"), shine=0.1)
VEST = Mat("wool.householder_vest", hexc("#2e2420"), shine=0.12)
SHIRT = Mat("wool.householder_shirt", hexc("#a39a83"), shine=0.08)
TROUSERS = Mat("wool.householder_trousers", hexc("#2a2826"), shine=0.08)
SHOE = Mat("leather.householder_shoe", hexc("#1e1a17"), shine=0.3)
BUTTON = Mat("brass.householder_button", hexc("#6a5a3a"), shine=0.5)
EYE = Mat("glass_dirty.householder_eye", hexc("#c8c2b0"), shine=0.95)
NAIL = Mat("skin.householder_nail", hexc("#6a6258"), shine=0.3)

# ----------------------------------------------------------------------------------------------------------------
# The skin and its clothes: one surface (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "householder flesh")
SPINE_PTS = [H("pelvis") + Vector((0, 0, -0.08)), H("spine_01"), H("spine_02"), H("spine_03"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "spine_03", "neck", "head"], soft=0.05)
VEST_HEM = 1.0      # where the waistcoat's points come down to, in front
V_TOP, V_BOTTOM = 1.53, 1.36  # the waistcoat's V: the shirt shows above where it's buttoned


def trunk_w(p):
    """Up the spine; the shoulders with the clavicles and the upper arms, the hips with the thighs."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.z > 1.42 and ax > 0.09:
        k = smooth01(0.09, 0.17, ax) * smooth01(1.42, 1.5, p.z)
        w = mix(w, {f"clavicle_{s}": 1.0}, 0.5 * k)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.5 * k * smooth01(0.14, 0.2, ax))
    d = (p - H(f"thigh_{s}")).length
    if d < 0.16 and p.z < 1.05:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.55 * smooth01(0.16, 0.05, d) * smooth01(0.02, 0.07, ax))
    return w


def torso_mat(pts, n):
    """The waistcoat, but for its V at the throat (the shirt) and below its hem (the trousers)."""
    c = sum(pts, Vector()) / len(pts)
    v = V_BOTTOM + (V_TOP - V_BOTTOM) * min(1.0, abs(c.x) / 0.1)
    if c.y > 0.02 and c.z > v and abs(c.x) < 0.12:
        return SHIRT
    hem = VEST_HEM + 0.05 * min(1.0, abs(c.x) / 0.08) + (0.05 if c.y < 0 else 0)
    return TROUSERS if c.z < hem else VEST


# The torso: narrow everywhere, the shoulders bony and sloped, the chest flat, the waistcoat close over it and the belly
# sunk under it; the hips a little wider, the trousers pulled high.
flesh.blob((0, 0.0, 1.0), (0.15, 0.1, 0.1), 0, TROUSERS, trunk_w, fmat=torso_mat)                     # the hips
flesh.blob((0, 0.0, 1.14), (0.125, 0.085, 0.1), 0.06, VEST, trunk_w, fmat=torso_mat)                  # the waist
flesh.blob((0, 0.0, 1.34), (0.15, 0.1, 0.15), 0.07, VEST, trunk_w, fmat=torso_mat)                    # the chest
flesh.blob((0, 0.0, 1.49), (0.17, 0.085, 0.07), 0.06, VEST, trunk_w, fmat=torso_mat)                  # the chest's top
for sx in (1, -1):
    flesh.blob((sx * 0.17, 0.0, 1.5), (0.055, 0.055, 0.05), 0.04, SHIRT, trunk_w)                    # the shoulder
    flesh.blob((sx * 0.085, -0.075, 1.42), (0.05, 0.02, 0.08), 0.03, VEST, trunk_w, fmat=torso_mat)  # the shoulder blade
# The collar band of the collarless shirt, round the base of the neck.
flesh.limb([Vector((sx * 0.055 * math.sin(a), 0.045 * math.cos(a) + 0.005, 1.565 - 0.012 * math.cos(a))) for a, sx in
            ((math.radians(a), 1) for a in range(-150, 151, 30))], [0.012] * 11, 0.012, SHIRT, trunk_w, sides=8)

# The arms: long and thin in their shirt sleeves to just past the elbow, the sleeves too short: the forearm bare from
# there, bony, the wrist knobbed, the hands long.
SLEEVE_END = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([H(f"clavicle_{s}"), sh, el, wr, tip], [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.04)
    cuff = el.lerp(wr, 0.42)
    SLEEVE_END[s] = cuff

    def sleeve(pts, n, el=el, wr=wr):
        c = sum(pts, Vector()) / len(pts)
        t = (c - el).dot((wr - el).normalized()) / (wr - el).length
        return SHIRT if t < 0.42 else SKIN
    flesh.limb([sh + Vector((0, 0, 0.01)), sh.lerp(el, 0.45), el], [0.05, 0.045, 0.04], 0.04, SHIRT,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(1.42, 1.5, p[2]) * 0.6))
    flesh.limb([el, el.lerp(wr, 0.25), el.lerp(wr, 0.41)], [0.04, 0.038, 0.036], 0.02, SHIRT, arm, fmat=sleeve)
    flesh.limb([el.lerp(wr, 0.4), el.lerp(wr, 0.7), wr], [0.026, 0.022, 0.018], 0.015, SKIN, arm, fmat=sleeve)
    flesh.blob(wr + Vector((0, 0, -0.004)), (0.024, 0.017, 0.02), 0.012, SKIN, arm, around=10, rings=6)     # the wrist
    flesh.blob(wr.lerp(tip, 0.55), (0.028, 0.014, 0.05), 0.015, SKIN, arm, around=14, rings=6)          # the palm

# The legs: long and thin in the old trousers, the knees standing out, down to the worn shoes.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.04)
    flesh.limb([hip + Vector((0, 0, 0.02)), hip.lerp(kn, 0.4), kn], [0.075, 0.062, 0.05], 0.04, TROUSERS,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(0.92, 1.0, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.012, 0)), (0.05, 0.048, 0.05), 0.02, TROUSERS, leg, around=12, rings=8)     # the knee
    flesh.limb([kn, kn.lerp(an, 0.4), an + Vector((0, 0, 0.03))], [0.05, 0.046, 0.042], 0.02, TROUSERS, leg)
    # The shoe: an old laced shoe, long and narrow, the toe turned up a little with wear.
    flesh.blob(an.lerp(ball, 0.4) + Vector((0, 0, -0.035)), (0.045, 0.12, 0.04), 0.03, SHOE, leg, rot=R(rx=-6))
    flesh.blob(ball + Vector((0, 0.035, -0.008)), (0.042, 0.055, 0.025), 0.03, SHOE, leg)

# The neck: long, thin, the cords and the Adam's apple; the head: a long narrow skull, the hair gone thin and combed flat,
# the brow and the cheekbones standing, the cheeks sunk, a long thin nose, a thin wide mouth set straight, the chin long.
flesh.limb([H("neck") + Vector((0, -0.005, -0.01)), H("neck").lerp(T("neck"), 0.5), T("neck") + Vector((0, 0.005, 0.02))],
           [0.058, 0.052, 0.054], 0.03, SKIN, trunk_w)
flesh.blob((0, 0.062, 1.62), (0.012, 0.015, 0.018), 0.012, SKIN, trunk_w)                              # the Adam's apple
HEAD = Vector((0, 0.03, 1.81))
flesh.blob(HEAD + Vector((0, -0.01, 0.03)), (0.082, 0.102, 0.108), 0.04, FACE, trunk_w, around=20, rings=12)   # the skull
flesh.blob(HEAD + Vector((0, 0.055, -0.045)), (0.055, 0.05, 0.075), 0.04, FACE, trunk_w)              # the face
flesh.blob(HEAD + Vector((0, 0.06, -0.105)), (0.034, 0.04, 0.035), 0.025, FACE, trunk_w)              # the chin
flesh.blob(HEAD + Vector((0, 0.085, 0.035)), (0.06, 0.02, 0.018), 0.02, FACE, trunk_w)                # the brow
flesh.limb([HEAD + Vector((0, 0.1, 0.02)), HEAD + Vector((0, 0.115, -0.02)), HEAD + Vector((0, 0.118, -0.04))],
           [0.008, 0.011, 0.012], 0.01, FACE, trunk_w)                                                # the nose
for sx in (1, -1):
    flesh.blob(HEAD + Vector((sx * 0.048, 0.07, -0.005)), (0.018, 0.022, 0.014), 0.015, FACE, trunk_w)    # the cheekbone
    flesh.carve(HEAD + Vector((sx * 0.045, 0.085, -0.04)), (0.018, 0.012, 0.022), 0.015)                  # the sunk cheek
    flesh.carve(HEAD + Vector((sx * 0.03, 0.09, 0.012)), (0.018, 0.012, 0.012), 0.008)                    # the eye's socket
flesh.carve(HEAD + Vector((0, 0.106, -0.072)), (0.026, 0.006, 0.003), 0.003)                          # the mouth's line

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the eyes, the ears, the waistcoat's hem and the cuffs' frayed edges, the trousers' hems, the
# buttons and the watch chain, the long fingers and the thumbs.
fine = kit.part("fine")
EYES = []
for sx in (1, -1):
    e = HEAD + Vector((sx * 0.03, 0.084, 0.012))
    EYES.append(e)
    fine.blob(e, (0.0125, 0.01, 0.0105), 10, 6, EYE, "head")
    # The ears: long, flat to the head.
    ear = HEAD + Vector((sx * 0.074, -0.005, -0.005))
    fine.blob(ear, (0.012, 0.022, 0.035), 10, 7, FACE, "head", rot=R(rz=-sx * 10))
# The waistcoat's hem: round the waist, down to its two points in front.
hem = []
for k in range(25):
    a = 2 * math.pi * k / 24
    x, y = 0.152 * math.sin(a), 0.108 * math.cos(a) + 0.003
    z = VEST_HEM + 0.05 * min(1.0, abs(x) / 0.08) + (0.05 if y < 0 else 0.0) + 0.006
    hem.append(Vector((x, y, z)))
fine.tube(hem[:-1], [(0.008, 0.005)] * 24, 5, VEST, trunk_w, loop=True, ref=(0, 0, 1))
# The cuffs' edges, the sleeves too short; the trousers' hems over the shoes.
CUFFS = []
for s, sx in (("r", 1), ("l", -1)):
    el, wr = H(f"lowerarm_{s}"), H(f"hand_{s}")
    d = (wr - el).normalized()
    c = SLEEVE_END[s]
    side = d.cross(Vector((0, 1, 0))).normalized()
    up = side.cross(d).normalized()
    ring = [c + side * 0.041 * math.cos(2 * math.pi * k / 10) + up * 0.039 * math.sin(2 * math.pi * k / 10) for k in range(10)]
    CUFFS.append(c)
    fine.tube(ring, [0.006] * 10, 5, SHIRT, f"lowerarm_{s}", loop=True, ref=tuple(d))
    an = H(f"foot_{s}")
    ring = [an + Vector((0.052 * math.cos(2 * math.pi * k / 12), 0.05 * math.sin(2 * math.pi * k / 12) - 0.005, 0.03 + 0.012 * math.sin(2 * math.pi * k / 12)))
            for k in range(12)]
    fine.tube(ring, [0.007] * 12, 5, TROUSERS, f"calf_{s}", loop=True, ref=(0, 0, 1))
# The buttons, five down the waistcoat's front from the V; the watch chain from the third to a pocket.
BUTTONS = []
for k in range(5):
    z = V_BOTTOM - 0.02 - k * 0.075
    p = flesh.surface(Vector((0, 0.0, z)), (0, 1, 0), 0.2)
    BUTTONS.append(p)
    fine.blob(p + Vector((0, 0.003, 0)), (0.009, 0.004, 0.009), 8, 4, BUTTON, trunk_w)
a = BUTTONS[2]
pocket = flesh.surface(Vector((0.08, 0.0, a.z - 0.06)), (0.3, 1, 0), 0.2)
chain_pts = [a.lerp(pocket, t) + Vector((0, 0.006, -0.035 * math.sin(math.pi * t))) for t in (0, 0.25, 0.5, 0.75, 1)]
fine.tube(chain_pts, [0.0025] * 5, 4, BUTTON, trunk_w, ref=(0, 1, 0))
WATCH = pocket

# The fingers: too long, a joint too many, knuckled, the nails long and yellowed; the thumbs long too.
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for f in FINGERS:
        k0, k1, k2, k3 = H(f"finger_{f}_{s}_01"), H(f"finger_{f}_{s}_02"), H(f"finger_{f}_{s}_03"), T(f"finger_{f}_{s}_03")
        a = k0 - (k1 - k0).normalized() * 0.018
        bones = chain([a, k1, k2, k3], [f"finger_{f}_{s}_01", f"finger_{f}_{s}_02", f"finger_{f}_{s}_03"], soft=0.01)
        nail = k3 + (k3 - k2).normalized() * 0.006
        TIPS.append(nail)
        fine.tube([a, a.lerp(k1, 0.5), k1, k1.lerp(k2, 0.5), k2, k2.lerp(k3, 0.6), nail],
                  [0.0085, 0.0076, 0.0082, 0.007, 0.0074, 0.006, 0.0028], 6, SKIN, bones, ref=(0, 1, 0), cap0="point", cap1="point",
                  fmat=lambda pts, n, k3=k3: NAIL if (sum(pts, Vector()) / len(pts) - k3).length < 0.01 else SKIN)
    t0, t1, t2 = H(f"thumb_{s}_01"), H(f"thumb_{s}_02"), T(f"thumb_{s}_02")
    a = t0 - (t1 - t0).normalized() * 0.012
    fine.tube([a, t1, t1.lerp(t2, 0.6), t2 + (t2 - t1).normalized() * 0.005], [0.0105, 0.009, 0.0075, 0.003], 6, SKIN,
              chain([a, t1, t2], [f"thumb_{s}_01", f"thumb_{s}_02"], soft=0.01), ref=(0, 1, 0), cap0="point", cap1="point",
              fmat=lambda pts, n, t2=t2: NAIL if (sum(pts, Vector()) / len(pts) - t2).length < 0.01 else SKIN)


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest is stood straight; angles in the armature's axes: +X swings a hanging limb forward and tips a spine or
# the head back; a finger curls with -X.

def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


def curl(k, thumb=0.0):
    out = {}
    for s in ("r", "l"):
        for i, f in enumerate(FINGERS):
            out[f"finger_{f}_{s}_01"] = (-k * (0.7 + 0.1 * i), 0, 0)
            out[f"finger_{f}_{s}_02"] = (-k, 0, 0)
            out[f"finger_{f}_{s}_03"] = (-k * 0.8, 0, 0)
        out[f"thumb_{s}_01"] = (-thumb, 0, 0)
        out[f"thumb_{s}_02"] = (-thumb, 0, 0)
    return out


def euler(q):
    return tuple(round(math.degrees(a), 2) for a in q.to_euler("YXZ"))


def mirrored(pose):
    """`pose` with its left side the right's mirrored (the right's reach solved once, so the two sides match)."""
    return rig.mirror({k: v for k, v in pose.items() if not rig._split(k)[0].endswith("_l")})


def plant(pose, s, target, pole, foot=(0, 0, 0)):
    x = H(f"thigh_{s}").x
    p = rig.reach(sk, pose, f"thigh_{s}", f"calf_{s}", Vector(target), elbow_axis=0, bend=-1, pole=Vector(pole),
                  avoid=lambda e: 0.4 * abs(e.x - x))
    p[f"foot_{s}"] = euler(rig.hang(sk, p, f"foot_{s}", *foot))
    return p


def legs(pose, r, l, knee=0.5):
    p = dict(pose)
    for s, sx, a in (("r", 1, r), ("l", -1, l)):
        a = Vector((sx * a[0], a[1], a[2]))
        p = plant(p, s, a, a + Vector((sx * 0.04, knee, 0.6)))
    return p


def reach_arm(pose, s, wrist, pole):
    return rig.reach(sk, pose, f"upperarm_{s}", f"lowerarm_{s}", Vector(wrist), elbow_axis=0, bend=1, pole=Vector(pole))


def flat_hand(pose, s, forward=(0, 1, -0.08)):
    """The right hand (its rest: hanging, the palm in) laid flat, pointing `forward`, the palm down."""
    f = Vector(forward).normalized()
    down = Vector((0, 0, -1))
    # Rest: the hand along -Z, its palm's normal -X. Wanted: -Z -> f, -X -> down.
    X, Z = -down, -f
    Y = Z.cross(X)
    q = Matrix((X, Y, Z)).transposed().to_quaternion()
    p = dict(pose)
    parent = rig.world_rotation(sk, p, f"lowerarm_{s}")
    p[f"hand_{s}"] = euler(parent.inverted() @ q)
    return p


STAND = curl(8, 4)


# Sit (5 s, loop): sat at its table, upright, the long hands flat on the cloth, utterly still (CreatureArt turns its head
# after you in ticks). Long stillness, then: one long finger of the right hand lifts in two frames, held, and sets down as
# fast; later the left hand's fingers lift one after another, the little finger first; one slow breath.
def seated(lean=0.0, tap=0.0, press=0.0, breath=0.0, hunch=0.0, grip=0.0, drum=(0, 0, 0, 0)):
    p = with_(STAND, {"root@loc": (0, -0.03 - 0.03 * press, SEAT + 0.09 - 0.98 + 0.06 * press), "pelvis": (-4 - 8 * lean - 10 * press, 0, 0),
                      "spine_01": (2, 0, 0), "spine_02": (2 - 2 * lean + 1.5 * breath, 0, 0), "spine_03": (2 + 1.5 * breath, 0, 0),
                      "neck": (-4 - 4 * lean, 0, 0), "head": (-4, 0, 0), "clavicle_r": (0, -7 * hunch - 1.5 * breath, -4)})
    p = plant(p, "r", (0.13, 0.48, 0.1), (0.15, 0.9, 0.7))
    p = reach_arm(p, "r", (0.19, 0.4 + 0.02 * lean, TABLE_TOP + 0.03 + 0.03 * press), (0.5, -0.2, 0.9))
    p = flat_hand(p, "r")
    # (Flat on the cloth; gripping, the knuckles rise and the nails dig in.)
    p.update({f"finger_{f}_r_0{k}": ((6 + 14 * grip) if k == 1 else (-4 - 16 * grip), 0, 0) for f in FINGERS for k in (1, 2, 3)})
    p = mirrored(p)
    if tap:
        p["finger_b_r_01"] = (6 + 22 * tap, 0, 0)
        p["finger_b_r_02"] = (-4 - 6 * tap, 0, 0)
    for f, d in zip(FINGERS, drum):
        if d:
            p[f"finger_{f}_l_01"] = (6 + 14 * grip + 24 * d, 0, 0)
            p[f"finger_{f}_l_02"] = (-4 - 16 * grip - 8 * d, 0, 0)
    return p


sit = Clip("sit")
sit.key(0, seated(), "BEZIER")
sit.key(70, seated(), "LINEAR")
sit.key(72, seated(tap=1), "LINEAR")                                           # a finger up, in two frames
sit.key(82, seated(tap=1), "LINEAR")
sit.key(84, seated(), "BEZIER")                                                # and down as fast
sit.key(104, seated(), "BEZIER")
for f, dr in ((107, (0, 0, 0, 1)), (110, (0, 0, 1, 0.3)), (113, (0, 1, 0.3, 0)), (116, (1, 0.3, 0, 0)), (119, (0.3, 0, 0, 0)), (122, (0, 0, 0, 0))):
    sit.key(f, seated(drum=dr), "BEZIER")                                      # the left hand's fingers, one by one
sit.key(134, seated(breath=1), "BEZIER")                                       # one slow breath
sit.close(150)

# Watch (3 s, loop): sat, leant toward whoever it's watching (CreatureArt turns the head after them): creeping further in,
# the long fingers drawing into the cloth; the shoulders ticking up, held; settling back. Its beats uneven.
watch = Clip("watch")
watch.key(0, seated(lean=0.8), "BEZIER")
watch.key(26, seated(lean=1.0, grip=0.3), "BEZIER")
watch.key(40, seated(lean=1.08, grip=0.5), "LINEAR")
watch.key(42, seated(lean=1.08, grip=0.5, hunch=0.6), "LINEAR")               # a tick
watch.key(60, seated(lean=1.08, grip=0.55, hunch=0.6), "BEZIER")
watch.close(90)


# Rise (1.5 s: enemies.json householder riseSeconds; the telegraph): in two frames its hands slam flat on the table and its
# shoulders come up; held; then it pushes itself up off the chair (the chair scraping back), bent over the table on its
# straight arms, the head held level on you; held there a beat; then up, straight, in three frames: stood, dead still.
def standing_at(y, bend=0.0):
    p = with_(STAND, {"root@loc": (0, y, -0.02 * bend), "pelvis": (-6 * bend, 0, 0), "neck": (-4, 0, 0)})
    return mirrored(legs(p, (0.125, y - 0.01, 0.09), (0.125, y - 0.01, 0.09)))


def up_over(thrust=0.0):
    p = with_(STAND, {"root@loc": (0, 0.1, -0.3), "pelvis": (-30, 0, 0), "spine_02": (4, 0, 0), "neck": (10 + 6 * thrust, 0, 0),
                      "head": (6 + 8 * thrust, 0, 0), "clavicle_r": (0, -5, -4)})
    p = plant(p, "r", (0.12, 0.22, 0.09), (0.15, 0.8, 0.7))
    p = reach_arm(p, "r", (0.19, 0.42, TABLE_TOP + 0.03), (0.5, -0.2, 0.9))
    return mirrored(flat_hand(p, "r"))


rise = Clip("rise", loop=False)
rise.key(0, seated(), "LINEAR")
rise.key(2, seated(lean=0.6, press=0.15, hunch=1, grip=0.2), "LINEAR")         # slammed flat, the shoulders up
rise.key(8, seated(lean=0.6, press=0.15, hunch=1, grip=0.2), "BEZIER")         # held
rise.key(19, seated(lean=1.0, press=1.0, hunch=0.6), "BEZIER")                 # pushing up, the chair scraping
rise.key(28, up_over(), "BEZIER")
rise.key(33, up_over(1), "LINEAR")                                             # bent over the table, held on you
rise.key(36, standing_at(0.16, 0.3), "BEZIER")                                 # up, too fast
rise.key(40, standing_at(0.16, -0.2), "BEZIER")
rise.key(45, standing_at(0.16), "BEZIER")


# Hunt (0.93 s, loop, two strides: enemies.json householder hunt, 3.4 m/s): a stiff, relentless, too-even stride: the knees
# hardly bending, no bob to it, the arms held out a little in front and never swinging, the long fingers half open and
# ready, the shoulders level, the head steady (on whoever it's after: CreatureArt); once, a twitch of the fingers.
def stiff(k, rise_, twitch=0.0):
    p = with_(STAND, {"root@loc": (0, 0, -0.03 + 0.008 * rise_), "pelvis": (-4, 0, 2 * k), "spine_02": (0, 0, -2 * k),
                      "spine_03": (-2, 0, 0), "upperarm_r": (24, -8, 0), "upperarm_l": (24, 8, 0), "lowerarm_r": (22, 0, 0),
                      "lowerarm_l": (22, 0, 0), **curl(12, 6)})
    if twitch:
        p.update({f"finger_{f}_r_0{j}": (-12 - 40 * twitch, 0, 0) for f in FINGERS for j in (1, 2)})
    return p


hunt = Clip("hunt")
for o in (0, 14):
    hunt.key(o + 0, legs(stiff(1, 0), (0.1, 0.42, 0.09), (0.1, -0.42, 0.11)), "LINEAR")
    hunt.key(o + 4, legs(stiff(0, 1, twitch=1 if o else 0), (0.1, 0.0, 0.09), (0.1, 0.02, 0.17), knee=0.3), "LINEAR")
    hunt.key(o + 7, legs(stiff(-1, 0), (0.1, -0.42, 0.11), (0.1, 0.42, 0.09)), "LINEAR")
    hunt.key(o + 11, legs(stiff(0, 1), (0.1, 0.02, 0.17), (0.1, 0.0, 0.09), knee=0.3), "LINEAR")
hunt.close(28)


# Grab (0.8 s, loop): both hands out in front at a man's chest, the long fingers locked on them, wrenching them about:
# held, then wrenched hard one way in two frames, held, the other.
def gripping(k):
    g_ = with_(STAND, {"root@loc": (0, 0.02, -0.04), "pelvis": (-8, 0, 4 * k), "spine_02": (-4, 0, 6 * k), "neck": (-6, 0, -4 * k)})
    g_ = legs(g_, (0.12, 0.12, 0.09), (0.12, -0.16, 0.09))
    for s, sx in (("r", 1), ("l", -1)):
        g_ = reach_arm(g_, s, (sx * (0.2 - 0.03 * k * sx), 0.5, 1.32 + 0.04 * k * sx), (sx * 0.7, 0.1, 1.1))
    g_.update(curl(70, 40))
    return g_


grab = Clip("grab")
for f, k, ip in ((0, 0, "LINEAR"), (4, 0.15, "LINEAR"), (6, 1.2, "BEZIER"), (11, 0.9, "LINEAR"), (13, -1.1, "BEZIER"), (19, -0.7, "BEZIER")):
    grab.key(f, gripping(k), ip)
grab.close(24)


# Return (1.47 s, loop, two of its strides: householder walk, 1.4 m/s): walking back to its chair, its business done: looser,
# the arms hanging; on the second stride its head turns back over its shoulder at you, sudden, and holds there as it walks.
def easy(k, rise_, look=0.0):
    return with_(STAND, {"root@loc": (0, 0, -0.03 + 0.02 * rise_), "pelvis": (-2, 0, 3 * k), "spine_02": (0, 0, -3 * k + 0.1 * look),
                         "upperarm_r": (4 - 8 * k, -4, 0), "upperarm_l": (4 + 8 * k, 4, 0), "lowerarm_r": (8 + 3 * k, 0, 0),
                         "lowerarm_l": (8 - 3 * k, 0, 0), "neck": (-6, 0, 0.35 * look), "head": (0, 0, 0.55 * look)})


ret = Clip("return")
for o, looks in ((0, (0, 0, 0, 0)), (22, (0, -70, -70, -66))):
    ret.key(o + 0, legs(easy(1, 0, looks[0]), (0.1, 0.26, 0.09), (0.1, -0.26, 0.1)), "BEZIER")
    ret.key(o + 5, legs(easy(0, 1, looks[1]), (0.1, 0.0, 0.09), (0.1, 0.02, 0.15), knee=0.3), "BEZIER")
    ret.key(o + 11, legs(easy(-1, 0, looks[2]), (0.1, -0.26, 0.1), (0.1, 0.26, 0.09)), "BEZIER")
    ret.key(o + 16, legs(easy(0, 1, looks[3]), (0.1, 0.02, 0.15), (0.1, 0.0, 0.09), knee=0.3), "BEZIER")
# (The look round: in two frames, from the stride's start.)
ret.key(25, legs(easy(0.5, 0.5, 0), (0.1, 0.13, 0.09), (0.1, -0.12, 0.13), knee=0.3), "LINEAR")
ret.keys.sort(key=lambda k: k[0])
ret.close(44)

# Hit: knocked aside, held two frames; then back upright too fast, all at once, as if nothing happened.
hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(2, mirrored(with_(STAND, {"pelvis": (6, 0, 6), "spine_02": (6, 0, 8), "neck": (8, 0, 10), "head": (10, 0, 12),
                                  "upperarm_r": (20, -14, 0), "root@loc": (0, -0.05, 0.0)})), "CONSTANT")
hit.key(6, mirrored(with_(STAND, {"pelvis": (5, 0, 5), "spine_02": (5, 0, 7), "neck": (7, 0, 9), "head": (8, 0, 10),
                                  "upperarm_r": (18, -12, 0), "root@loc": (0, -0.045, 0.0)})), "LINEAR")
hit.key(8, with_(STAND, {"pelvis": (1, 0, -2), "head": (2, 0, -3)}), "CONSTANT")
hit.key(12, STAND, "CONSTANT")

# Death (1.2 s): it stiffens, held a beat, then goes over backward, stiff as a board, faster and faster, and lands with a
# jolt; the long hands still open; the fingers close, one after another, and it's still.
death = Clip("death", loop=False)
death.key(0, STAND, "CONSTANT")
death.key(3, with_(STAND, {"root": (4, 0, 0), "head": (8, 0, 0), "upperarm_r": (8, -10, 0), "upperarm_l": (8, 10, 0)}), "CONSTANT")
death.key(9, with_(STAND, {"root": (6, 0, 0), "head": (6, 0, 0), "upperarm_r": (10, -10, 0), "upperarm_l": (10, 10, 0)}), "LINEAR")
death.key(14, with_(STAND, {"root": (28, 0, 0), "root@loc": (0, 0.0, 0.02), "head": (-2, 0, 2), "upperarm_r": (12, -12, 0),
                            "upperarm_l": (12, 12, 0)}), "LINEAR")
death.key(17, with_(STAND, {"root": (66, 0, 0), "root@loc": (0, 0.0, 0.07), "head": (-8, 0, 4), "upperarm_r": (14, -13, 0),
                            "upperarm_l": (13, 13, 0), **curl(14, 8)}), "LINEAR")
death.key(19, with_(STAND, {"root": (90, 0, 0), "root@loc": (0, 0.0, 0.1), "head": (-20, 0, 10), "upperarm_r": (24, -20, 0),
                            "upperarm_l": (20, 20, 0), **curl(30, 10)}), "LINEAR")
death.key(22, with_(STAND, {"root": (86, 0, 0), "root@loc": (0, 0.0, 0.1), "head": (-12, 0, 10), "upperarm_r": (20, -18, 0),
                            "upperarm_l": (18, 18, 0), **curl(30, 10)}), "BEZIER")
LAID = with_(STAND, {"root": (90, 0, 0), "root@loc": (0, 0.0, 0.1), "head": (-16, 0, 12), "upperarm_r": (20, -18, 0),
                     "upperarm_l": (18, 18, 0), **curl(30, 10)})
death.key(25, LAID, "BEZIER")
for i, f in enumerate(FINGERS):
    LAID = with_(LAID, {f"finger_{f}_r_0{j}": (-55 * (0.7 if j == 1 else 1), 0, 0) for j in (1, 2, 3)})
    death.key(28 + 2 * i, LAID, "BEZIER")
death.key(36, with_(LAID, curl(40, 20), {k: v for k, v in LAID.items() if k.startswith("finger_") and "_r_" in k}), "BEZIER")

CLIPS = [sit, watch, rise, hunt, grab, ret, hit, death]

if os.environ.get("DT_POSES"):
    want = [(f"{b}_{s}", "tail") for b in ("toe", "hand") for s in ("r", "l")] + [("head", "tail"), ("pelvis", "head"),
                                                                               ("calf_r", "head"), ("hand_r", "head")]
    for clip in CLIPS:
        for frame, pose, _ in clip.keys:
            P = rig.pose_points(sk, pose, want)
            print(f"{clip.name:8s} {frame:3d} " + " ".join(f"{n[0][:7]}=({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})" for n, p in zip(want, P)))
    sys.exit(0)

# One skin, clothes and all (rig.fuse, settled on the flesh's field, then QuadriFlow): a character's budget (GDD §27: 4-10k).
kit.fuse("body", ["body"], voxel=0.0035, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] householder", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "householder.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
