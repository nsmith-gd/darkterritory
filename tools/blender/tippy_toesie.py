"""TIPPY TOESIE (GDD v1.2 §21 interior, App. A.5 · absence): "Tiptoes up behind idle players. Runs if you see it coming.
Covers your mouth and suffocates you over 20 seconds, and your voice goes muffled."

A starved thing over two metres tall that walks on the points of its toes, as a dancer does en pointe: the feet grown
into long bony spikes, the heels never down, so it makes no sound but a tick, tick on the boards (its tell). Hairless,
grey-white as old plaster, the ribs and the spine standing under skin gone thin as paper, in a filthy shift. It has no
mouth: the skin runs smooth from its nose to its chin, puckered where a mouth should be, and it holds one long finger
to that place, shh. Its hands are the worst of it: broad, and the fingers a hand's length again, for covering a mouth
from behind. The eyes are small, black and wet, deep in the sockets. A few long hairs hang from the back of its head.
"The strongest monsters are the ones where you can still tell what they used to be" (§26.5): it was a child once.

SK_Human (rig.human) in its proportions: long legs, very long arms, a long neck. Faces +Y (the engine's -Z).
Clips (GDD §31: unnaturally still when observed, then too-fast corrections): stalk (tiptoeing up behind someone: a
high step, placed, and a long hold, the finger to its face), wait (stood on its points, dead still but for a hand),
recoil (pulled off or seen: jerked up and back, arms flung up, held a beat), flee (scuttling off on its points, too
fast, arms up), smother (behind its victim, leaning over them, one hand over their mouth, rocking them; CreatureArt puts
the hand on the mouth), hit (struck, once).

    tools/models/build.sh tippy_toesie        # this, its high copy and the bake -> content/art/models/tippy_toesie.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# Long in the leg and very long in the arm, a thin frame on a long neck.
sk = rig.human(height=2.0, leg=1.2, arm=1.38, torso=1.0, neck=1.7, head=1.0, width=0.7, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "tippy_toesie")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
SHOULDER = H("upperarm_r")
KNEE = H("calf_r").z
HIP = H("thigh_r").z
WAIST = H("spine_01").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
# The skull: an egg longer than a person's, on the neck's top; HC its centre, HR its radii (x across, y deep, z tall).
HC = Vector((0.0, 0.015, NECK_TOP + 0.125))
HR = Vector((0.092, 0.112, 0.142))
EYE_U, EYE_W, EYE_R = 0.36, 0.06, 0.016

SKIN = Mat("skin.tippy", hexc("#b8b4ab"), shine=0.3)
FACE = Mat("skin.tippy_face", hexc("#bdb8ae"), shine=0.3)
EYE = Mat("glass_dirty.tippy_eye", hexc("#0c0a0a"), shine=0.95)
NAIL = Mat("tar.nail", hexc("#2a2320"), shine=0.5)
HAIR = Mat("wool.tippy_hair", hexc("#15120f"), shine=0.3)
SHIFT = Mat("wool.shift", hexc("#8f887b"), shine=0.06)


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


def eye_centre(side):
    u = EYE_U * (-1 if side == "l" else 1)
    v = math.sqrt(max(0.0, 1 - u * u - EYE_W * EYE_W))
    surf = HC + Vector((u * HR.x, v * HR.y, EYE_W * HR.z))
    return surf - head_normal(surf) * 0.04


def skull(i, j, a, th, p):
    """The face on the egg: sockets sunk deep, the brow over them, cheekbones standing out over hollow cheeks, a long
    thin nose, the temples pinched; and under the nose, where a mouth should be, nothing: the skin smooth and a little
    sunk, puckered in a line, down to a long chin."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = max(0.0, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.042 * front * bell(math.hypot((u - eu) / 0.32, (w - EYE_W + 0.02) / 0.3))   # sockets, deep
        off += 0.008 * front * bell((u - eu) / 0.34) * bell((w - EYE_W - 0.26) / 0.1)        # the brow
        off += 0.012 * front * bell((u - eu * 1.3) / 0.22) * bell((w + 0.2) / 0.1)           # cheekbones
    off -= 0.014 * front * bell((abs(u) - 0.55) / 0.2) * bell((w + 0.45) / 0.2)              # hollow cheeks
    off += 0.02 * front * bell(u / 0.1) * smooth01(0.1, -0.1, w) * smooth01(-0.52, -0.3, w)  # the nose's ridge
    off += 0.012 * front * bell(u / 0.16) * bell((w + 0.38) / 0.07)                          # its tip
    off -= 0.006 * front * bell(u / 0.35) * bell((w + 0.6) / 0.1)                            # where the mouth was
    off -= 0.018 * bell((abs(u) - 0.92) / 0.14) * bell((w - 0.25) / 0.3)                     # pinched temples
    q = p + n * off
    # The jaw long and narrow to the chin.
    if w < -0.3:
        k = 1 - 0.2 * smooth01(-0.3, -1.0, w)
        q.x = HC.x + (q.x - HC.x) * k
        q.z -= 0.02 * smooth01(-0.5, -1.0, w)
    return q


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    return FACE if v > 0.25 and -1.1 < w < 0.6 and abs(u) < 0.85 else SKIN


# ----------------------------------------------------------------------------------------------------------------
# The head: the skull, the small black eyes deep in it, the ears flat to it, a few long hairs from the back; the neck
# long and corded.
head = kit.part("head")
head.blob(HC, tuple(HR), 26, 20, SKIN, "head", shape=skull, fmat=face_or_skull)
EYES = {}
for side in ("l", "r"):
    c = eye_centre(side)
    EYES[side] = c
    head.blob(c, (EYE_R, EYE_R, EYE_R * 0.9), 10, 6, EYE, "head")
for sx in (-1, 1):
    e = HC + Vector((sx * HR.x * 0.97, -0.01, 0.0))
    head.blob(e, (0.012, 0.028, 0.04), 8, 5, SKIN, "head")
for k in range(7):
    a = math.radians(-60 + 20 * k)
    root = HC + Vector((math.sin(a) * HR.x * 0.8, -HR.y * 0.8, 0.07 + 0.01 * math.cos(a * 3)))
    drop = 0.35 + 0.18 * (0.5 + 0.5 * math.sin(k * 2.1))
    pts = [root, root + Vector((math.sin(a) * 0.02, -0.03, -0.06)), root + Vector((math.sin(a) * 0.04, -0.05, -drop * 0.5)),
           root + Vector((math.sin(a) * 0.05 + 0.01 * math.sin(k), -0.06, -drop))]
    head.tube(pts, [0.004, 0.0035, 0.003, 0.0015], 4, HAIR, ["head", "neck"], ref=(1, 0, 0), cap1="point")
NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.04))]
head.tube(NECK, [(0.036, 0.034), (0.03, 0.03), (0.034, 0.032)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * bell((abs(math.sin(a)) - 0.7) / 0.15))


# ----------------------------------------------------------------------------------------------------------------
# The body: starved, the waist a hand across, every rib and knuckle of the spine showing, the shoulders' bones
# standing, the pelvis's crests; the shift hangs off it.
body = kit.part("body")
SPINE = (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0)
TORSO = [(HIP - 0.04, 0.13, 0.085), (HIP + 0.05, 0.12, 0.08), (WAIST, 0.085, 0.065), (WAIST + 0.08, 0.1, 0.075),
         (H("spine_02").z + 0.05, 0.13, 0.095), (CHEST + 0.03, 0.14, 0.1), (TOP - 0.07, 0.145, 0.088),
         (TOP - 0.03, 0.125, 0.074), (TOP, 0.08, 0.056), (TOP + 0.03, 0.045, 0.042)]


def ribs(i, j, a, p, fr):
    """Ribs round the chest, the belly sunk under them, the spine's knuckles down the back."""
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    front, back = max(0.0, math.cos(a)), max(0.0, -math.cos(a))
    d = 0.0
    if H("spine_02").z - 0.06 < p.z < TOP - 0.05:
        d += 0.01 * max(0.0, math.sin((p.z - WAIST) * 55)) ** 2 * (0.4 + 0.6 * abs(math.sin(a)))
    if CHEST - 0.04 < p.z < TOP - 0.03:
        # Over the top of the slip the ribs stand hardest, either side of the breastbone's groove.
        d += 0.008 * front * max(0.0, math.sin((p.z - WAIST) * 55)) ** 2
    d -= 0.007 * front * bell(math.sin(a) / 0.12) * smooth01(WAIST + 0.05, WAIST + 0.12, p.z)
    d -= 0.012 * front * bell((p.z - WAIST - 0.02) / 0.06)
    d += 0.008 * back * bell(math.sin(a) / 0.2) * max(0.0, math.sin(p.z * 70))
    return p + out * d




def resampled(secs, step):
    """The sections again every `step` metres (a ring each), so the ribs have rings to stand on."""
    out = []
    for (z0, x0, y0), (z1, x1, y1) in zip(secs, secs[1:]):
        n = max(1, round((z1 - z0) / step))
        out += [(z0 + (z1 - z0) * k / n, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    return out + [secs[-1]]


RIBBED = resampled(TORSO, 0.022)
body.tube([Vector((0, 0.0, z)) for z, _, _ in RIBBED], [(rx, ry) for _, rx, ry in RIBBED], 16, SKIN, SPINE, ref=(0, 1, 0),
          square=0.9, shape=ribs)
for sx in (-1, 1):
    # The collarbones and the shoulders' knobs.
    body.tube([Vector((sx * 0.018, 0.062, TOP - 0.03)), Vector((sx * 0.1, 0.05, TOP - 0.012)), Vector((sx * (SHOULDER.x - 0.01), 0.012, SHOULDER.z + 0.008))],
              [0.011, 0.01, 0.014], 6, SKIN, [f"clavicle_{'r' if sx > 0 else 'l'}"], ref=(0, 0, 1))
    body.blob(Vector((sx * SHOULDER.x, 0.0, SHOULDER.z)), (0.028, 0.03, 0.026), 8, 5, SKIN, [f"upperarm_{'r' if sx > 0 else 'l'}"])
    # The hips' crests standing.
    body.blob(Vector((sx * 0.1, 0.02, HIP + 0.06)), (0.03, 0.035, 0.03), 8, 4, SKIN, "pelvis")


# ----------------------------------------------------------------------------------------------------------------
# The arms, bone with skin over it, the elbows knobs; the hands broad, the fingers a hand's length again, knuckled, the
# nails black; the legs long, knees like knots, and the feet grown into spikes it stands on the points of.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.035):
    """Weights for one tube over two bones a-b-c: the upper's to the joint, the lower's past it, blended across it."""
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    # One skin from the shoulder to the wrist, bent at the elbow by its weights (no ball joints: it's not a doll), thin
    # as a broom handle, the elbow a knob of bone.
    limbs.tube([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.45), e, e.lerp(wr, 0.35), e.lerp(wr, 0.8), wr],
               [0.031, 0.024, (0.027, 0.029), (0.024, 0.021), (0.02, 0.016), (0.019, 0.015)], 9, SKIN, jointed(sh, e, wr, ua, la),
               ref=(0, 0, 1))
    limbs.blob(wr, (0.022, 0.02, 0.018), 7, 4, SKIN, hd)
    k0 = T(hd)
    # The palm, broad and flat.
    limbs.tube([wr + Vector((sx * 0.01, 0, 0)), wr.lerp(k0, 0.55), k0], [(0.034, 0.014), (0.05, 0.015), (0.056, 0.014)], 10, SKIN, hd,
               ref=(0, 0, 1), cap0=False)
    # Four fingers, three knuckles each, a hand's length again; the knuckles swollen, the nails black.
    fl = 0.3
    for f in range(4):
        spread = (f - 1.5) * 0.028
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.2, 0)).normalized()
        n = fl * (0.8 + 0.2 * (1 - abs(f - 1.5) / 1.5)) * (0.85 if f == 3 else 1.0)
        pts = [base, base + along * n * 0.36, base + along * n * 0.66, base + along * n]
        w = lambda p, base=base, n=n: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}
        limbs.tube(pts, [0.011, 0.01, 0.008, 0.005], 6, SKIN, w, ref=(0, 0, 1), cap1="point",
                   shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.003 if i in (1, 2) else 0.0))
        limbs.tube([pts[-1] - along * 0.03, pts[-1] + along * 0.004], [(0.007, 0.003), (0.005, 0.002)], 4, NAIL, f"fingers_{s}",
                   ref=(0, 0, 1))
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.9], [0.012, 0.01, 0.005], 6, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")

for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.03)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.4), an + Vector((0, 0, 0.03))],
               [0.052, 0.036, (0.04, 0.042), (0.03, 0.034), 0.022], 9, SKIN, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0))
    limbs.blob(an, (0.026, 0.028, 0.026), 8, 4, SKIN, f"foot_{s}")
    # The heel's knob behind the ankle, and the foot grown to a spike: the arch, the ball, then all the toes gone into one
    # long point it stands on.
    limbs.blob(an + Vector((0, -0.035, -0.015)), (0.018, 0.024, 0.02), 6, 4, SKIN, f"foot_{s}")
    spike = toe + (toe - bl).normalized() * 0.06
    limbs.tube([an, an.lerp(bl, 0.5), bl, bl.lerp(spike, 0.55), spike], [(0.024, 0.02), (0.026, 0.017), (0.02, 0.014), (0.012, 0.01), 0.003], 8, SKIN,
               lambda p, s=s, bl=bl: {f"ball_{s}": 1.0} if p.y > bl.y - 0.01 else {f"foot_{s}": 1.0}, ref=(0, 0, 1), cap1="point")
    limbs.tube([spike - (spike - bl).normalized() * 0.03, spike + (spike - bl).normalized() * 0.008], [0.006, 0.002], 5, NAIL, f"ball_{s}",
               ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# The shift: a child's nightshirt on a body twice the size, so it hangs to the top of the thighs, off one shoulder,
# torn and filthy at the hem.
shift = kit.part("shift")


def shift_weights(p):
    k = 0.6 * smooth01(HIP, KNEE + 0.2, -p.z + 2 * HIP)
    side = smooth01(-0.06, 0.06, p.x)
    if p.z > WAIST:
        return {"spine_02": 0.5, "spine_03": 0.5} if p.z > CHEST - 0.05 else {"spine_01": 0.6, "spine_02": 0.4}
    out = {"pelvis": 1 - k}
    if k > 0:
        out["thigh_r"] = k * side
        out["thigh_l"] = k * (1 - side)
    return {n: w for n, w in out.items() if w > 1e-4}


SN = 26
HEM = HIP - 0.24
# It starts under the arms, a slip more than a shirt, so the bare chest shows over it: the collarbones, the top ribs.
NECKLINE = CHEST + 0.02
rings = []
for z, rx, ry, folds in ((NECKLINE, 0.142, 0.104, 0.0), (CHEST - 0.04, 0.148, 0.112, 0.1), (WAIST + 0.04, 0.14, 0.11, 0.3),
                         (HIP, 0.16, 0.12, 0.5), (HIP - 0.12, 0.19, 0.15, 0.8), (HEM, 0.2, 0.16, 1.0)):
    ring = []
    for jj in range(SN):
        a = 2 * math.pi * jj / SN
        k = 1 + folds * (0.05 * math.sin(5 * a + 0.4) + 0.025 * math.sin(11 * a))
        zz = z
        if z == HEM:
            zz += 0.03 * noise3(Vector((a * 3, 0, 0)), 13, 1.0)
            for tear in (0.7, 2.9, 4.8):
                zz += 0.09 * bell((a - tear) / 0.1)
        if z == NECKLINE:
            # Dipped at the front, and fallen on the left where its strap's gone.
            zz -= 0.03 * bell(a / 0.6) + 0.035 * bell((a + math.pi / 2) / 0.45)
        ring.append(Vector((math.sin(a) * rx * k, math.cos(a) * ry * k, zz)))
    rings.append(ring)
rings.append([Vector((p.x * 0.95, p.y * 0.95, p.z + 0.03)) for p in rings[-1]])
shift.loft(rings, SHIFT, shift_weights, centres=[Vector((0, 0, r[0].z)) for r in rings], inside=Vector((0, 0, WAIST)))
# The straps: the right over its shoulder, the left slipped off and hanging down the arm.
shift.tube([Vector((0.075, 0.09, NECKLINE - 0.02)), Vector((0.1, 0.04, TOP)), Vector((0.11, -0.02, TOP + 0.01)), Vector((0.08, -0.09, NECKLINE))],
           [(0.014, 0.004)] * 4, 5, SHIFT, "spine_03", ref=(0, 0, 1))
UA = H("upperarm_l").lerp(H("lowerarm_l"), 0.3)
shift.tube([Vector((-0.08, 0.08, NECKLINE - 0.05)), UA + Vector((0.0, 0.035, 0.0)), Vector((-0.1, -0.09, NECKLINE - 0.02))],
           [(0.014, 0.004)] * 3, 5, SHIFT, lambda p: {"upperarm_l": 1.0} if p.x < -0.14 else {"spine_03": 1.0}, ref=(0, 0, 1))


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): arms lowered by Y, swung by X (+ forward); a spine tipped forward by
# -X and turned by Z (+ to its left); a thigh raised forward by +X, a knee bent by -X; a foot pointed down by -X. Held
# and popped (CONSTANT) where it's stalking: it moves, then it's still, too still.
EN_POINTE = {"foot_r": (-58, 0, 0), "ball_r": (-24, 0, 0)}
BASE = mirror({
    "upperarm_r": (0, 70, 0), "lowerarm_r": (0, 8, 0), "hand_r": (0, 0, 0), "fingers_r": (0, 12, 0), "thumb_r": (0, 6, 0),
    "thigh_r": (6, 0, 0), "calf_r": (-10, 0, 0), **EN_POINTE,
})


def elbow_out(e):
    return 0.4 if abs(e.x) < 0.14 and e.y > -0.05 else 0.0


def elbow_down(e):
    """Keeps an elbow in out of the body and down by the ribs (a hand up to the face from a raised elbow is a salute)."""
    return elbow_out(e) + 2.0 * max(0.0, e.z - (SHOULDER.z - 0.18)) + 1.0 * max(0.0, abs(e.x) - 0.2)


def arm_to(pose, side, wrist, curl=0.0, avoid=elbow_out):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=avoid)
    p[f"fingers_{side}"] = (0, (12 + 70 * curl) * k, 0)
    p[f"thumb_{side}"] = (0, (6 + 20 * curl) * k, 0)
    return p


def face_front(pose, forward=0.12, down=0.11):
    base, tip = rig.pose_points(sk, pose, [("head", "head"), ("head", "tail")])
    q = rig.world_rotation(sk, pose, "head")
    return base + (tip - base) * 0.35 + q @ Vector((0, forward, 0)) - Vector((0, 0, down))


# Hunched to creep: the back curled over, the neck out, the head up to watch the back it's creeping on.
HUNCH = over(BASE, spine_01=(-8, 0, 0), spine_02=(-12, 0, 0), spine_03=(-10, 0, 0), neck=(-18, 0, 0), head=(26, 0, 0))


def shush(pose):
    """The right hand up, its longest finger laid upright against where its mouth should be."""
    p = arm_to(pose, "r", face_front(pose, forward=0.2, down=0.18), curl=0.0, avoid=elbow_down)
    p["hand_r"] = (0, -30, 70)
    return p


def reaching(pose, k):
    """The left hand out in front and a little up, the fingers spread, `k` of the way to reaching."""
    at = Vector((-0.2, 0.4 + 0.3 * k, SHOULDER.z - 0.2 + 0.1 * k))
    p = arm_to(pose, "l", at, curl=-0.3)
    return p


def bent(pose, knee=22):
    """Both knees bent `knee` degrees more (thigh forward, calf back), whatever the step's doing."""
    out = dict(pose)
    for s in ("l", "r"):
        tx, ty, tz = out.get(f"thigh_{s}", (0, 0, 0))
        cx, cy, cz = out.get(f"calf_{s}", (0, 0, 0))
        out[f"thigh_{s}"] = (tx + knee * 0.6, ty, tz)
        out[f"calf_{s}"] = (cx - knee, cy, cz)
    return out


def step(pose, side, lift):
    """A leg lifted high on the step (`lift` 0..1): the thigh up, the knee folded, the point hanging."""
    return over(pose, **{f"thigh_{side}": (6 + 58 * lift, 0, 0), f"calf_{side}": (-10 - 80 * lift, 0, 0),
                         f"foot_{side}": (-58 + 10 * lift, 0, 0)})


# Stalk (3.2 s, loop): a step on the right, placed with the point and weighted, then a long hold, dead still, the
# finger to its face and the other hand drifting out; a step on the left; the hold again. (The sim moves it on.)
S = shush(HUNCH)
stalk = Clip("stalk")
stalk.key(0, reaching(S, 0.3), "CONSTANT")
stalk.key(6, reaching(step(S, "r", 0.6), 0.35), "LINEAR")
stalk.key(12, reaching(step(S, "r", 1.0), 0.4), "BEZIER")
stalk.key(20, reaching(over(step(S, "r", 0.3), pelvis=(0, 0, -4)), 0.45), "CONSTANT")
stalk.key(22, reaching(over(S, pelvis=(0, 0, -4)), 0.5), "CONSTANT")
stalk.key(48, reaching(over(S, pelvis=(0, 0, -4), head=(26, 0, 8)), 0.55), "CONSTANT")
stalk.key(54, reaching(step(S, "l", 0.6), 0.5), "LINEAR")
stalk.key(60, reaching(step(S, "l", 1.0), 0.45), "BEZIER")
stalk.key(68, reaching(over(step(S, "l", 0.3), pelvis=(0, 0, 4)), 0.4), "CONSTANT")
stalk.key(70, reaching(over(S, pelvis=(0, 0, 4)), 0.35), "CONSTANT")
stalk.key(90, reaching(over(S, pelvis=(0, 0, 4), head=(26, 0, -6)), 0.3), "CONSTANT")
stalk.close(96)

# Wait (4 s, loop): stood on its points, upright, arms hanging, the fingers down past its knees; dead still. Now and
# then its head tips over to one side, all at once; once, the fingers of a hand ripple.
W = over(BASE, spine_03=(-4, 0, 0), neck=(-6, 0, 0), head=(4, 0, 0),
         upperarm_r=(2, 82, 0), upperarm_l=(2, -82, 0), lowerarm_r=(0, 4, 0), lowerarm_l=(0, -4, 0))
wait = Clip("wait")
wait.key(0, W, "CONSTANT")
wait.key(50, over(W, head=(6, 0, -34)), "CONSTANT")
wait.key(80, W, "CONSTANT")
for f, c in ((96, 0.4), (99, -0.2), (102, 0.5), (105, 0.0)):
    wait.key(f, W | {"fingers_r": (0, 12 + 60 * c, 0)}, "CONSTANT")
wait.close(120)

# Flee (0.6 s, loop): seen, and off, low, fast, the points ticking, the arms up and back like a thing ducking under a
# blow, the head down.
FL = over(BASE, spine_01=(-20, 0, 0), spine_02=(-14, 0, 0), neck=(-10, 0, 0), head=(-12, 0, 0),
          upperarm_r=(-40, 30, 20), upperarm_l=(-40, -30, -20), lowerarm_r=(0, 40, 0), lowerarm_l=(0, -40, 0))
flee = Clip("flee")
for f, (r, l) in enumerate(((1.0, 0.0), (0.4, 0.4), (0.0, 1.0), (0.4, 0.4))):
    flee.key(f * 4.5, over(step(step(FL, "r", r), "l", l), pelvis=(0, 0, 8 * (r - l)), spine_03=(-8, 0, -6 * (r - l))), "LINEAR")
flee.close(18)

# Smother (1.6 s, loop): stood close behind its victim, bent over them, its head beside theirs; the right hand over
# their mouth (CreatureArt reaches it there), the left on their shoulder; rocking them, slowly, side to side, as you
# would a child to sleep, the fingers tightening on each rock.
# Bent right over them, its head laid down beside theirs, cheek to their hair; the knees bent to bring it down to them.
SM = over(BASE, spine_01=(-16, 0, 0), spine_02=(-18, 0, 0), spine_03=(-14, 0, 0), neck=(-34, 0, -10), head=(-6, 14, -26),
          thigh_r=(24, 0, 0), calf_r=(-36, 0, 0), thigh_l=(18, 0, 0), calf_l=(-28, 0, 0))
MOUTH_AT = Vector((0.02, 0.5, 1.55))
SHOULDER_AT = Vector((-0.24, 0.42, 1.4))
smother = Clip("smother")
for f, k in ((0, 0.0), (12, 1.0), (24, 0.0), (36, -1.0)):
    p = over(SM, pelvis=(0, 0, 3 * k), spine_02=(-14, 0, 4 * k), head=(8, 10 + 4 * k, -16))
    p = arm_to(p, "r", MOUTH_AT + Vector((0.03 * k, 0, 0)), curl=0.6 + 0.2 * abs(k))
    p = arm_to(p, "l", SHOULDER_AT + Vector((0.03 * k, 0, 0)), curl=0.8)
    smother.key(f, p, "BEZIER")
smother.close(48)

# It's taller than the cars were built for: 2.45 m stood on its points, and 2.75 m under a car's roof. Inside one it
# stoops, and through a doorway it folds right down (CreatureArt picks by where it is; CreatureArtTests holds each to
# the room it's drawn in).
# Stoop (4 s, loop): stood under a roof, bowed, the knees bent, the head laid over on one side along the ceiling as if
# listening to it. Dead still but for the head coming upright, all at once, and going back over.
ST = over(BASE, spine_01=(-6, 0, 0), spine_02=(-10, 0, 0), spine_03=(-12, 0, 0), neck=(-28, 0, 0), head=(16, 0, -38),
          thigh_r=(14, 0, 0), calf_r=(-26, 0, 0), thigh_l=(14, 0, 0), calf_l=(-26, 0, 0),
          upperarm_r=(10, 80, 0), upperarm_l=(10, -80, 0), lowerarm_r=(0, 16, 0), lowerarm_l=(0, -16, 0))
stoop = Clip("stoop")
stoop.key(0, ST, "CONSTANT")
stoop.key(64, over(ST, head=(14, 0, 10)), "CONSTANT")
stoop.key(78, ST, "CONSTANT")
for f, c in ((92, 0.5), (95, -0.2), (98, 0.4), (101, 0.0)):
    stoop.key(f, ST | {"fingers_l": (0, -(12 + 60 * c), 0)}, "CONSTANT")
stoop.close(120)

# Stalk, stooped (3.2 s, loop): the stalk bowed lower under the roof, the knees bent through every step.
LOWER = dict(spine_01=(-14, 0, 0), spine_02=(-16, 0, 0), spine_03=(-12, 0, 0), neck=(-22, 0, 0), head=(30, 0, 0))
SS = shush(over(HUNCH, **LOWER))
stalk_stoop = Clip("stalk_stoop")
for f, interp, pose in ((0, "CONSTANT", reaching(SS, 0.3)), (6, "LINEAR", reaching(step(SS, "r", 0.5), 0.35)),
                        (12, "BEZIER", reaching(step(SS, "r", 0.85), 0.4)), (20, "CONSTANT", reaching(over(step(SS, "r", 0.25), pelvis=(0, 0, -4)), 0.45)),
                        (22, "CONSTANT", reaching(over(SS, pelvis=(0, 0, -4)), 0.5)), (48, "CONSTANT", reaching(over(SS, pelvis=(0, 0, -4), head=(30, 0, 8)), 0.55)),
                        (54, "LINEAR", reaching(step(SS, "l", 0.5), 0.5)), (60, "BEZIER", reaching(step(SS, "l", 0.85), 0.45)),
                        (68, "CONSTANT", reaching(over(step(SS, "l", 0.25), pelvis=(0, 0, 4)), 0.4)), (70, "CONSTANT", reaching(over(SS, pelvis=(0, 0, 4)), 0.35)),
                        (90, "CONSTANT", reaching(over(SS, pelvis=(0, 0, 4), head=(30, 0, -6)), 0.3))):
    stalk_stoop.key(f, bent(pose), interp)
stalk_stoop.close(96)

# Duck (2 s, loop): through a doorway, folded double: the knees deep, the back bent flat, the head turned on its side to
# go under the lintel first; the long arms out ahead, hands spread on the jambs either side, drawing it through.
DK = over(BASE, spine_01=(-38, 0, 0), spine_02=(-30, 0, 0), spine_03=(-16, 0, 0), neck=(-8, 0, 0), head=(16, 0, -64),
          thigh_r=(20, 0, 0), calf_r=(-30, 0, 0), thigh_l=(20, 0, 0), calf_l=(-30, 0, 0))
JAMB_R, JAMB_L = Vector((0.36, 0.7, 1.0)), Vector((-0.36, 0.7, 0.92))
duck = Clip("duck")
for f, k in ((0, 0.0), (15, 1.0), (30, 0.0), (45, -1.0)):
    # (Its steps lift a foot from the crouch: added to the bent knee, not step()'s, which would straighten it.)
    p = over(DK, pelvis=(0, 0, 4 * k), spine_02=(-30, 0, 3 * k))
    for side, lift in (("r", max(0.0, k)), ("l", max(0.0, -k))):
        tx, _, _ = p[f"thigh_{side}"]
        cx, _, _ = p[f"calf_{side}"]
        p[f"thigh_{side}"], p[f"calf_{side}"] = (tx + 14 * lift, 0, 0), (cx - 12 * lift, 0, 0)
    p = arm_to(p, "r", JAMB_R + Vector((0, 0.06 * k, 0.04 * k)), curl=0.35)
    p = arm_to(p, "l", JAMB_L - Vector((0, 0.06 * k, 0.04 * k)), curl=0.35)
    duck.key(f, p, "BEZIER")
duck.close(60)

# Recoil (0.4 s, once): pulled off its victim, or seen (App. A.5 "any friend hits or pulls it -> it flees"): it jerks up
# and back off them all at once, thrown upright onto its points, the head snapped back, the long arms flung up and out
# with the fingers splayed; held there a beat, dead still, too still; then it drops into the flee's crouch.
RC = over(BASE, spine_01=(14, 0, 0), spine_02=(10, 0, 0), spine_03=(8, 0, 0), neck=(12, 0, 0), head=(26, 0, 0),
          upperarm_r=(-30, 14, 24), upperarm_l=(-30, -14, -24), lowerarm_r=(0, 30, 0), lowerarm_l=(0, -30, 0),
          fingers_r=(0, -20, 0), fingers_l=(0, 20, 0), thumb_r=(0, -10, 0), thumb_l=(0, 10, 0),
          thigh_r=(-8, 0, 0), thigh_l=(14, 0, 0), calf_l=(-24, 0, 0))
recoil = Clip("recoil", loop=False)
recoil.key(0, RC, "CONSTANT")
recoil.key(7, over(RC, head=(26, 0, 12)), "CONSTANT")
recoil.key(9, over(RC, head=(26, 0, 12)), "LINEAR")
recoil.key(12, FL, "CONSTANT")

# Hit: struck, it folds away from the blow with a jerk and comes back up too fast.
hit = Clip("hit", loop=False)
hit.key(0, W, "CONSTANT")
hit.key(2, over(W, spine_01=(10, 0, 8), spine_02=(8, 0, 6), neck=(12, 0, 0), head=(20, 0, 14)), "CONSTANT")
hit.key(8, over(W, spine_02=(-6, 0, -4), head=(-6, 0, -6)), "LINEAR")
hit.key(12, W, "CONSTANT")

kit.build()
rig.bake(sk, [stalk, wait, flee, recoil, smother, hit, stoop, stalk_stoop, duck], plant=rig.feet_planter(sk, bones=("ball_l", "ball_r"), lowest=0.004))
print("[dt] tippy_toesie", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "tippy_toesie.glb", kit)
