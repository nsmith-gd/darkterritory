"""THE CHOIR (GDD v1.2 §21 drawn by noise, App. A.7 · sound): "Small flying ghosts that come for a loud crew. Long
warning, then the swarm. Takes anyone outside, on the roofs, or behind no door. Killable, barely."

Choir children, gone wrong. Each is the size of a child, in what's left of a chorister's surplice: grey with filth, a
ruff gone limp at the throat, and below the waist no legs, only the surplice hanging in long ragged strips that stream
out behind it as it flies. The skin a dead blue-grey, veined dark. The face is the worst: the eyes black hollows that
weep, and the jaw dropped open far wider than a jaw can go, in the round O of a child singing, and held there. It's all
mouth. Long thin fingers. (§26: the corruption palette, not neon: no light of its own but the faint cold of its skin.)

SK_Human (rig.human) at a child's size, a big head; the legs are hidden in the strips and swing them. Faces +Y (the
engine's -Z). Clips (§31: still, then too fast): drift (circling, slow, bobbing, the head tipped back singing, the hands
open), swoop (on someone: leant into the flight, arms reaching, the strips streaming back), seize (wrapped round its
catch's head from above, its mouth at their ear), besiege (at a shut door, fists hammering, in uneven bursts), hit.

    tools/models/build.sh choir        # this, its high copy and the bake -> content/art/models/choir.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
sk = rig.human(height=1.15, leg=0.95, arm=1.15, torso=0.9, neck=1.1, head=1.35, width=0.85, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "choir")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
SHOULDER = H("upperarm_r")
HIP = H("thigh_r").z
WAIST = H("spine_01").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
HC = Vector((0.0, 0.01, NECK_TOP + 0.1))
HR = Vector((0.085, 0.095, 0.11))
EYE_U, EYE_W = 0.38, 0.12
MOUTH_W = -0.42

SKIN = Mat("skin.choir", hexc("#6e7378"), shine=0.3)
FACE = Mat("skin.choir_face", hexc("#7a7f84"), shine=0.3)
HOLE = Mat("tar.choir_hole", hexc("#050404"), shine=0.9)
ROBE = Mat("wool.surplice", hexc("#8a8679"), shine=0.05)
RUFF = Mat("wool.ruff", hexc("#9a968a"), shine=0.05)
NAIL = Mat("tar.nail", hexc("#2a2320"), shine=0.5)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def head_units(p):
    d = p - HC
    return d.x / HR.x, d.y / HR.y, d.z / HR.z


def head_normal(p):
    d = p - HC
    return Vector((d.x / HR.x ** 2, d.y / HR.y ** 2, d.z / HR.z ** 2)).normalized()


def skull(i, j, a, th, p):
    """A child's face, gone: the eye sockets deep hollows, the cheeks sunk, and the jaw pulled down, long, the mouth's
    hole driven in deep; the cranium big over it."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = max(0.0, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.03 * front * bell(math.hypot((u - eu) / 0.27, (w - EYE_W) / 0.24))
    off -= 0.012 * front * bell((abs(u) - 0.55) / 0.2) * bell((w + 0.25) / 0.25)
    # The mouth: an O, deep, its rim the lips stretched thin round it.
    r = math.hypot(u / 0.3, (w - MOUTH_W) / 0.38)
    off -= 0.045 * front * smooth01(1.0, 0.55, r)
    off += 0.006 * front * bell((r - 1.0) / 0.12)
    q = p + n * off
    # The jaw dropped: everything under the mouth's middle pulled down.
    if w < MOUTH_W:
        q.z -= 0.09 * smooth01(MOUTH_W, MOUTH_W - 0.5, w)
    return q


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    if v > 0.25 and math.hypot(u / 0.3, (w - MOUTH_W) / 0.38) < 0.95:
        return HOLE
    for eu in (-EYE_U, EYE_U):
        if v > 0.3 and math.hypot((u - eu) / 0.2, (w - EYE_W) / 0.17) < 1.15:
            return HOLE
    return FACE if v > 0.1 else SKIN


head = kit.part("head")
head.blob(HC, tuple(HR), 20, 16, SKIN, "head", shape=skull, fmat=face_or_skull)
head.tube([Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.03))],
          [0.03, 0.027, 0.03], 8, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0))
# The ruff, gone limp: a frilled collar round the throat.
RN = 18
ruff = []
for k in range(3):
    ring = []
    rad = 0.045 + 0.035 * k
    for jj in range(RN):
        a = 2 * math.pi * jj / RN
        frill = 0.012 * math.sin(a * 9) * (k / 2)
        ring.append(Vector((math.sin(a) * (rad + frill), math.cos(a) * (rad + frill) * 0.9, TOP + 0.01 - 0.012 * k - 0.01 * k * k * (0.5 + 0.5 * math.sin(a * 3)))))
    ruff.append(ring)
head.loft(ruff, RUFF, ["spine_03"], centres=[Vector((0, 0, r[0].z)) for r in ruff])


# ----------------------------------------------------------------------------------------------------------------
# The body under the surplice: thin arms out of wide sleeves, long-fingered hands. The surplice from the shoulders
# down, and below the waist the strips.
body = kit.part("body")
TORSO = [(HIP, 0.08, 0.06), (WAIST, 0.07, 0.055), (CHEST, 0.09, 0.065), (TOP - 0.02, 0.085, 0.055), (TOP + 0.02, 0.04, 0.035)]
body.tube([Vector((0, 0, z)) for z, _, _ in TORSO], [(rx, ry) for _, rx, ry in TORSO], 10, SKIN, (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0),
          ref=(0, 1, 0))


def jointed(a, b, c, upper, lower, blend=0.03):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    body.tube([sh, sh.lerp(e, 0.5), e, e.lerp(wr, 0.5), wr], [0.022, 0.017, 0.018, 0.015, 0.013], 7, SKIN, jointed(sh, e, wr, ua, la), ref=(0, 0, 1))
    # The sleeve, wide, ragged at its end, hanging off the upper arm.
    # (Hanging: its cloth draped down off the arm, longer underneath, torn ragged at the end.)
    body.tube([sh + Vector((sx * 0.01, 0, -0.005)), sh.lerp(e, 0.5) - Vector((0, 0, 0.02)), e + Vector((sx * 0.03, 0, -0.06))],
              [(0.03, 0.035), (0.04, 0.06), (0.05, 0.085)], 10, ROBE, jointed(sh, e, wr, ua, la), ref=(0, 0, 1),
              shape=lambda i, j, a, p, fr: Vector(p) - Vector((0, 0, (0.025 * max(0.0, math.sin(a * 5 + 1)) + 0.03 * max(0.0, -math.cos(a))) * (i == 2))))
    k0 = T(hd)
    body.tube([wr, wr.lerp(k0, 0.6), k0], [(0.018, 0.008), (0.024, 0.009), (0.024, 0.008)], 6, SKIN, hd, ref=(0, 0, 1), cap0=True)
    for f in range(4):
        spread = (f - 1.5) * 0.013
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.5, 0)).normalized()
        n = 0.11 * (0.85 + 0.15 * (1 - abs(f - 1.5) / 1.5))
        pts = [base, base + along * n * 0.5, base + along * n]
        w = lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.12 else {f"fingers_{s}": 1.0}
        body.tube(pts, [0.0055, 0.0045, 0.002], 4, SKIN, w, ref=(0, 0, 1), cap1="point")
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    body.tube([th0, th1 + (th1 - th0) * 0.6], [0.006, 0.002], 4, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")

# The surplice: from the shoulders, wide over the chest, gathered at the waist, and below it the strips, each hanging from
# the hem and weighted down a leg, so the legs swing them.
robe = kit.part("robe")


def robe_weights(p):
    if p.z > WAIST:
        return {"spine_02": 0.5, "spine_03": 0.5} if p.z > CHEST - 0.03 else {"spine_01": 0.6, "spine_02": 0.4}
    side = smooth01(-0.05, 0.05, p.x)
    k = smooth01(HIP, HIP - 0.25, p.z)
    out = {"pelvis": 1 - k}
    for leg, wt in (("r", side), ("l", 1 - side)):
        if k * wt > 1e-4:
            depth = smooth01(HIP - 0.1, HIP - 0.5, p.z)
            out[f"thigh_{leg}"] = k * wt * (1 - depth)
            if depth * k * wt > 1e-4:
                out[f"calf_{leg}"] = k * wt * depth
    return {n: w for n, w in out.items() if w > 1e-4}


RS = 20
rings = []
for z, rx, ry in ((TOP + 0.005, 0.1, 0.07), (CHEST, 0.13, 0.1), (WAIST, 0.12, 0.095), (HIP, 0.15, 0.12)):
    rings.append([Vector((math.sin(2 * math.pi * jj / RS) * rx, math.cos(2 * math.pi * jj / RS) * ry, z)) for jj in range(RS)])
robe.loft(rings, ROBE, robe_weights, centres=[Vector((0, 0, r[0].z)) for r in rings], inside=Vector((0, 0, WAIST)))
# The strips: nine of them round the hem, long and ragged, narrowing, each a flat ribbon down past where the feet are.
STRIPS = []
for k in range(9):
    a = 2 * math.pi * (k + 0.3 * math.sin(k * 2.3)) / 9
    root = Vector((math.sin(a) * 0.14, math.cos(a) * 0.11, HIP + 0.01))
    length = 0.45 + 0.25 * (0.5 + 0.5 * math.sin(k * 1.7 + 0.4))
    out = Vector((math.sin(a), math.cos(a), 0))
    pts = [root, root + out * 0.03 - Vector((0, 0, length * 0.35)), root + out * 0.05 - Vector((0, 0, length * 0.7)), root + out * 0.06 - Vector((0, 0, length))]
    wide = 0.035 + 0.025 * (0.5 + 0.5 * math.sin(k * 2.9))
    widths = [(wide, 0.003), (wide * 0.9, 0.003), (wide * 0.7, 0.0025), (wide * 0.25, 0.002)]
    robe.tube(pts, widths, 4, ROBE, robe_weights, ref=[out] * 4, cap1="point",
              shape=lambda i, j, an, p, fr: Vector(p) + fr[0] * 0.008 * math.sin(i * 2.1 + k))
    STRIPS.append(pts)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/tippy_toesie.py. It flies: no feet planted; the legs, in the strips, trail and swing them.
BASE = mirror({
    "upperarm_r": (0, 60, 0), "lowerarm_r": (0, 10, 0), "fingers_r": (0, 20, 0), "thumb_r": (0, 6, 0),
    "thigh_r": (-10, 0, 0), "calf_r": (-20, 0, 0), "foot_r": (-40, 0, 0),
})


def legs(k, back=0.0):
    """The legs trailing, swinging the strips: `k` -1..1 the swing, `back` how far they stream behind."""
    return {"thigh_r": (-10 - 30 * back + 12 * k, 0, 4 * k), "thigh_l": (-10 - 30 * back - 12 * k, 0, 4 * k),
            "calf_r": (-20 - 20 * back - 10 * k, 0, 0), "calf_l": (-20 - 20 * back + 10 * k, 0, 0)}


def arm_to(pose, side, wrist, curl=0.0):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k)
    p[f"fingers_{side}"] = (0, (20 + 60 * curl) * k, 0)
    return p


# Drift (4 s, loop): circling slow, bobbing, the head tipped back singing, the hands out and open, palms up.
SING = over(BASE, spine_03=(6, 0, 0), neck=(16, 0, 0), head=(18, 0, 0), upperarm_r=(20, 40, 0), upperarm_l=(20, -40, 0),
            lowerarm_r=(0, 20, -30), lowerarm_l=(0, -20, 30))
drift = Clip("drift")
for f, k in ((0, 0.0), (30, 1.0), (60, 0.0), (90, -1.0)):
    drift.key(f, over(SING, root__loc=(0, 0, 0.08 * k), spine_01=(4 * k, 0, 0), head=(18 + 6 * k, 0, 4 * k), **legs(k)), "BEZIER")
drift.key(100, over(SING, head=(18, 0, 40), **legs(-0.5)), "CONSTANT")
drift.key(106, over(SING, head=(18, 0, 0), **legs(-0.2)), "CONSTANT")
drift.close(120)

# Swoop (1 s, loop): on someone, leant into the flight, the arms out ahead reaching, the fingers spread, the strips streaming.
SW = over(BASE, pelvis=(-50, 0, 0), spine_01=(-10, 0, 0), spine_03=(10, 0, 0), neck=(20, 0, 0), head=(20, 0, 0))
swoop = Clip("swoop")
for f, k in ((0, 0.0), (8, 1.0), (15, 0.0), (23, -1.0)):
    p = over(SW, **legs(0.6 * k, back=1.0))
    p = arm_to(p, "r", Vector((0.12, 0.42 + 0.04 * k, CHEST + 0.05)), curl=-0.2)
    p = arm_to(p, "l", Vector((-0.12, 0.42 - 0.04 * k, CHEST + 0.03)), curl=-0.2)
    swoop.key(f, p, "LINEAR")
swoop.close(30)

# Seize (1.6 s, loop): over its catch from above and behind, its arms wrapped round their head, the hands over their eyes,
# its gaping mouth down at their ear, the strips hanging down their back; it shudders.
SZ = over(BASE, spine_01=(-20, 0, 0), spine_02=(-20, 0, 0), neck=(-20, 0, 0), head=(-30, 0, 20))
seize = Clip("seize")
for f, k in ((0, 0.0), (6, 1.0), (12, 0.0), (24, 0.6), (36, 0.0)):
    p = over(SZ, root__loc=(0.01 * k, 0, 0), **legs(0.3 * k))
    p = arm_to(p, "r", Vector((0.1, 0.22, HIP - 0.25 + 0.01 * k)), curl=0.6)
    p = arm_to(p, "l", Vector((-0.12, 0.2, HIP - 0.22 - 0.01 * k)), curl=0.6)
    seize.key(f, p, "CONSTANT" if f in (6, 24) else "BEZIER")
seize.close(48)

# Besiege (1.2 s, loop): at a shut door, beating on it with both fists, in uneven bursts, the head thrown about.
BS = over(BASE, pelvis=(-14, 0, 0), spine_03=(-6, 0, 0))
besiege = Clip("besiege")
for f, (r, l) in ((0, (1, 0)), (3, (0, 1)), (5, (1, 0)), (8, (0, 0)), (14, (1, 1)), (17, (0, 0)), (22, (1, 0)), (25, (0, 1))):
    p = over(BS, head=(-10 + 14 * r, 0, 10 * (r - l)), **legs(0.4 * (r - l)))
    p = arm_to(p, "r", Vector((0.1, 0.34 - 0.12 * (1 - r), SHOULDER.z + 0.06 * r)), curl=1.0)
    p = arm_to(p, "l", Vector((-0.1, 0.34 - 0.12 * (1 - l), SHOULDER.z + 0.06 * l)), curl=1.0)
    besiege.key(f, p, "CONSTANT")
besiege.close(36)

hit = Clip("hit", loop=False)
hit.key(0, SING, "CONSTANT")
hit.key(2, over(SING, spine_01=(14, 0, 16), spine_03=(10, 0, 10), head=(-10, 0, 30), root__loc=(0.0, -0.12, 0.04)), "CONSTANT")
hit.key(8, over(SING, head=(30, 0, -10)), "LINEAR")
hit.key(12, SING, "CONSTANT")

kit.build()
rig.bake(sk, [drift, swoop, seize, besiege, hit])
print("[dt] choir", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "choir.glb", kit)
