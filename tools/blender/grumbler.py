"""THE GRUMBLER (GDD v1.2 §21 facility cranes, App. A.8 · sound): "Scuttles like a spider over the crane, gnawing food
crates. Interrupt it and it hunts whoever hit it last. No one player can kill it."

A dock labourer, still in what he worked in, who's gone wrong all the way through. He goes face-down like a spider now,
the long arms and legs folded up over his back so the elbows and the knees stand higher than he does, the hands and the
bare feet splayed flat. A second pair of arms has come out through his ribs, thin and bare, and they do the holding
while he eats. His spine has pushed up through the back of his shirt. The flat cap's still on. His eyes are rolled right
up, white; the cheeks are split back to the ears, so the jaw drops a long way and the teeth go on and on, and he gnaws,
and grumbles while he does it (the tell is the gnawing you hear).

SK_Human (rig.human) with a jaw and the two rib arms (arm2_*). Faces +Y (the engine's -Z). Clips (§31: still, then too
fast): gnaw (on the crates: head down in one, the jaw going, the rib arms tearing at it, a limb twitching), scuttle (after
whoever hit it, low and very fast, the limbs in diagonal pairs), bite (in reach: reared up on its back legs, the arms out,
the head lunging, the jaw wide), maul (on its victim: over them, every hand down on them, the head going down and down),
hit.

    tools/models/build.sh grumbler        # this, its high copy and the bake -> content/art/models/grumbler.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# A grown man's frame, the arms and legs drawn out long for it to fold up over itself.
_s = rig.human(height=1.8, leg=1.2, arm=1.9, torso=1.0, neck=1.2, head=1.0, width=0.95, fingers=True, sockets=False)
_b = {b.name: b for b in _s.bones}
NT = _b["head"].head.z
RIB = _b["spine_02"].head.z + 0.05
extra = [Bone("jaw", "head", (0, 0.02, NT + 0.06), (0, 0.12, NT + 0.02))]
for side, sx in (("l", -1), ("r", 1)):
    a, e, w = Vector((sx * 0.12, 0.03, RIB)), Vector((sx * 0.38, 0.03, RIB)), Vector((sx * 0.62, 0.03, RIB))
    extra += [Bone(f"arm2_{side}_01", "spine_02", a, e), Bone(f"arm2_{side}_02", f"arm2_{side}_01", e, w),
              Bone(f"hand2_{side}", f"arm2_{side}_02", w, w + Vector((sx * 0.1, 0, 0)))]
sk = rig.human(height=1.8, leg=1.2, arm=1.9, torso=1.0, neck=1.2, head=1.0, width=0.95, fingers=True, sockets=False, extra=extra)
sk.build()
kit = rig.Kit(sk, "grumbler")


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
HC = Vector((0.0, 0.02, NECK_TOP + 0.11))
HR = Vector((0.085, 0.1, 0.115))
EYE_U, EYE_W = 0.36, 0.18
SLIT_W = -0.3                  # the split across the face, ear to ear, where the jaw comes away

SKIN = Mat("skin.grumbler", hexc("#6e6a5e"), shine=0.25)
FACE = Mat("skin.grumbler_face", hexc("#757063"), shine=0.3)
EYE = Mat("glass_dirty.grumbler_eye", hexc("#c9c4b4"), shine=0.9)
MOUTH = Mat("tar.grumbler_mouth", hexc("#170a08"), shine=0.4)
TEETH = Mat("skin.grumbler_teeth", hexc("#b4a681"), shine=0.5)
NAIL = Mat("tar.grumbler_nail", hexc("#2a2219"), shine=0.4)
SHIRT = Mat("wool.grumbler_shirt", hexc("#6f6a5c"), shine=0.04)
COAT = Mat("wool.grumbler_waistcoat", hexc("#2f2a24"), shine=0.06)
TROUSERS = Mat("wool.grumbler_trousers", hexc("#3a362f"), shine=0.04)
CAP = Mat("wool.grumbler_cap", hexc("#3d3830"), shine=0.04)


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


def slit_at(u):
    """The split's height (in w) across the face at u: level at the mouth, rising a little to the ears (a grin)."""
    return SLIT_W + 0.18 * u * u


def skull(i, j, a, th, p):
    """A man's head gone wrong: the brow a heavy shelf over sockets sunk deep, the nose broken flat, the cheekbones
    standing over cheeks gone hollow; and the face split from ear to ear along slit_at, the lips gone from it, so the
    edges are raw and the teeth stand in them; the jaw long under it."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(-0.3, 0.3, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.034 * front * bell(math.hypot((u - eu) / 0.27, (w - EYE_W) / 0.22))                    # sockets, deep
        off += 0.016 * front * bell((u - eu * 0.8) / 0.4) * bell((w - EYE_W - 0.26) / 0.09)             # the brow's shelf
        off += 0.012 * front * bell((u - eu * 1.35) / 0.2) * bell((w - EYE_W + 0.26) / 0.1)             # cheekbones
    off -= 0.016 * front * bell((abs(u) - 0.6) / 0.22) * bell((w - SLIT_W - 0.16) / 0.12)                # hollow cheeks
    # The nose: broken, flattened, bent to one side.
    nu = u - 0.08 * smooth01(EYE_W, SLIT_W + 0.1, w)
    off += 0.02 * front * bell(nu / 0.16) * smooth01(EYE_W - 0.02, EYE_W - 0.14, w) * smooth01(SLIT_W + 0.06, SLIT_W + 0.18, w)
    # The split: a groove along the slit, deepest at the mouth, running back round the cheeks to under the ears.
    reach = smooth01(1.15, 0.9, abs(u)) * smooth01(-0.5, -0.1, v)
    off -= 0.03 * reach * bell((w - slit_at(u)) / 0.09)
    q = p + n * off
    if w < SLIT_W - 0.2:
        k = smooth01(SLIT_W - 0.2, -1.0, w)
        q.z -= 0.035 * k
        q.y += 0.015 * k * front
    return q


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    return FACE if v > 0.1 and w < 0.7 else SKIN


def skull_weights(p):
    """Under the split it's the jaw's, over it the head's (so the jaw comes away along it, and the cheeks tear back)."""
    u, v, w = head_units(p)
    if v < -0.55:
        return {"head": 1.0}
    k = smooth01(slit_at(u) + 0.04, slit_at(u) - 0.04, w)
    return {n: x for n, x in (("head", 1 - k), ("jaw", k)) if x > 1e-4}


# ----------------------------------------------------------------------------------------------------------------
# The head: the skull split along its face; the rolled-up eyes; inside the split the dark of the mouth, and the teeth,
# two long rows, the upper on the head and the lower on the jaw; the flat cap.
head = kit.part("head")
head.blob(HC, tuple(HR), 24, 20, SKIN, skull_weights, shape=skull, fmat=face_or_skull)
for eu in (-EYE_U, EYE_U):
    v = math.sqrt(max(0.0, 1 - eu * eu - EYE_W * EYE_W))
    surf = HC + Vector((eu * HR.x, v * HR.y, EYE_W * HR.z))
    c = surf - head_normal(surf) * 0.03
    head.blob(c, (0.015, 0.013, 0.014), 10, 6, EYE, "head")
    # The lids half down over them, so what shows is a sliver of the white the eye's rolled up into.
    head.blob(c + Vector((0, 0.002, 0)), (0.017, 0.0145, 0.0155), 10, 4, FACE, "head", z0=-0.05)


def on_slit(u, dw, inset):
    w = slit_at(u) + dw
    v = math.sqrt(max(0.02, 1 - u * u - w * w))
    surf = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    return surf - head_normal(surf) * inset


# The mouth's dark, set back in the split.
mouth = [on_slit(u, 0.0, 0.034) for u in (-0.95, -0.6, -0.3, 0.0, 0.3, 0.6, 0.95)]
head.tube(mouth, [(0.012, 0.01), (0.02, 0.02), (0.024, 0.026), (0.026, 0.028), (0.024, 0.026), (0.02, 0.02), (0.012, 0.01)], 6, MOUTH,
          lambda p: {"head": 0.5, "jaw": 0.5}, ref=(0, 0, 1))
# The teeth: too many, crowded, a little crooked, in two rows round the split.
for k in range(18):
    u = -0.92 + 1.84 * k / 17
    for row, dw, bone in (("upper", 0.075, "head"), ("lower", -0.075, "jaw")):
        c = on_slit(u, dw, 0.012)
        tall = (0.022 if abs(u) < 0.45 else 0.016) * (0.75 + 0.4 * (0.5 + 0.5 * math.sin(k * 2.3 + (row == "upper"))))
        lean = 0.004 * math.sin(k * 1.7 + (row == "lower") * 2)
        tip = c + Vector((lean, 0.005, -tall if row == "upper" else tall))
        head.tube([c, c.lerp(tip, 0.5), tip], [(0.0075, 0.0055), (0.0065, 0.005), (0.0025, 0.002)], 5, TEETH, bone, ref=(0, 1, 0), cap1="point")
# The flat cap: a soft crown pulled down to the brow, the peak over the eyes.
head.blob(HC + Vector((0, -0.01, HR.z * 0.42)), (HR.x * 1.1, HR.y * 1.12, HR.z * 0.62), 18, 6, CAP, "head", z0=0.25)
peak = [HC + Vector((x * HR.x * 0.9, HR.y * (0.92 - 0.25 * x * x) + 0.03, HR.z * 0.62 - 0.02 * x * x)) for x in (-0.9, -0.45, 0.0, 0.45, 0.9)]
head.tube(peak, [(0.004, 0.03), (0.005, 0.042), (0.005, 0.046), (0.005, 0.042), (0.004, 0.03)], 6, CAP, "head", ref=(0, 1, 0.3))
NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.03))]
head.tube(NECK, [(0.05, 0.048), (0.042, 0.042), (0.044, 0.044)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.006 * bell((abs(math.sin(a)) - 0.65) / 0.15))


# ----------------------------------------------------------------------------------------------------------------
# The body: a labourer's shirt, the sleeves rolled to the elbow, an open waistcoat over it; up the back the spine has come
# through the shirt, a ridge of knuckled bone in the torn cloth; the rib arms come out through holes torn in its sides.
body = kit.part("body")
SPINE = (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0)
TORSO = [(HIP - 0.06, 0.16, 0.11), (HIP + 0.04, 0.155, 0.105), (WAIST, 0.15, 0.1), (RIB, 0.165, 0.11), (CHEST + 0.04, 0.18, 0.115),
         (TOP - 0.05, 0.19, 0.11), (TOP, 0.12, 0.08), (TOP + 0.03, 0.06, 0.05)]


def torso_shape(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    back = max(0.0, -math.cos(a))
    d = 0.006 * noise3(p * 9, 41, 1.0)
    # The spine come up through the back, a knuckle at every vertebra.
    d += back * bell(math.sin(a) / 0.12) * (0.03 + 0.012 * max(0.0, math.sin(p.z * 55))) * smooth01(HIP, WAIST, p.z)
    return p + out * d


def torso_mat(points, normal):
    c = sum(points, Vector()) / len(points)
    a = math.atan2(c.x, c.y)        # 0 at the front, ±pi at the back
    if abs(abs(a) - math.pi) < 0.32 and HIP < c.z < TOP - 0.02:
        return SKIN                 # the torn back, the spine through it
    if abs(a) < 0.55 or c.z < HIP + 0.02:
        return SHIRT                # the shirt down the open front
    return COAT


def resampled(secs, step):
    out = []
    for (z0, x0, y0), (z1, x1, y1) in zip(secs, secs[1:]):
        n = max(1, round((z1 - z0) / step))
        out += [(z0 + (z1 - z0) * k / n, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    return out + [secs[-1]]


RINGS = resampled(TORSO, 0.03)
body.tube([Vector((0, 0, z)) for z, _, _ in RINGS], [(rx, ry) for _, rx, ry in RINGS], 16, COAT, SPINE, ref=(0, 1, 0), square=0.85,
          shape=torso_shape, fmat=torso_mat, cap1=True)
for sx in (-1, 1):
    body.blob(Vector((sx * SHOULDER.x, 0.0, SHOULDER.z)), (0.05, 0.055, 0.05), 8, 5, SHIRT, [f"upperarm_{'r' if sx > 0 else 'l'}"])


# ----------------------------------------------------------------------------------------------------------------
# The limbs: the sleeves rolled to the elbows, the forearms bare, the hands long and the fingers longer; the trousers
# torn off at the shins and the feet bare, the toes long, splayed to grip; the rib arms bare and thin.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.04):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


def hand(at, along, bone, fingers, n, size, sx):
    """A long hand at `at` pointing `along`: the palm, `n` long fingers, black nails."""
    tip = at + along * 0.11 * size
    limbs.tube([at, at.lerp(tip, 0.55), tip], [(0.03 * size, 0.013 * size), (0.045 * size, 0.014 * size), (0.047 * size, 0.013 * size)], 8, SKIN, bone,
               ref=(0, 0, 1))
    for f in range(n):
        spread = (f - (n - 1) / 2) * 0.026 * size
        base = tip + Vector((0, spread, 0))
        dirn = (along + Vector((0, spread * 1.5, 0))).normalized()
        ln = 0.15 * size * (0.85 + 0.15 * (1 - abs(f - (n - 1) / 2) / max(1, (n - 1) / 2)))
        # Three bones, swollen at each joint and wasted between (note 543).
        ts = (0.0, 0.2, 0.4, 0.5, 0.64, 0.76, 0.88, 1.0)
        pts = [base + dirn * ln * t for t in ts]
        limbs.tube(pts, [r * size for r in (0.0105, 0.0078, 0.0095, 0.0078, 0.0065, 0.0078, 0.006, 0.0045)], 6, SKIN,
                   lambda p, base=base, ln=ln: {bone: 1.0} if (p - base).length < ln * 0.1 else {fingers: 1.0}, ref=(0, 0, 1))
        limbs.tube([pts[-1] - dirn * 0.012, pts[-1] + dirn * 0.012 - Vector((0, 0, 0.006))], [(0.006, 0.003), (0.002, 0.002)], 4, NAIL, fingers,
                   ref=(0, 0, 1), cap1="point")


def sinew(i, j, a, p, fr):
    """Tendons standing in ridges round a wasted limb, most toward its thin end (i: along the tube)."""
    side, up, _ = fr
    out = side * math.sin(a) + up * math.cos(a)
    ridge = max(0.0, math.cos(a * 4 + 0.6)) ** 3
    return Vector(p) + out * (0.004 * ridge * min(1.0, i / 2) + 0.0015 * noise3(Vector(p) * 40, 151, 1.0))


def along(points, radii, per):
    """`points` and `radii` with `per` more rings between each pair (note 543: a limb's shape needs rings along it)."""
    pts, rr = [], []
    rs = [r if isinstance(r, tuple) else (r, r) for r in radii]
    for (p0, r0), (p1, r1) in zip(zip(points, rs), zip(points[1:], rs[1:])):
        for k in range(per + 1):
            f = k / (per + 1)
            pts.append(p0.lerp(p1, f))
            rr.append((r0[0] + (r1[0] - r0[0]) * f, r0[1] + (r1[1] - r0[1]) * f))
    return pts + [points[-1]], rr + [rs[-1]]


def limb(points, radii, per, sides, mat, bones, ref, bumps=(), folds=0.0, bunch=(), lumps=0.0, ridges=0.0, seed=0):
    """A limb as it is, not a tube of one width (note 543, the art checklist's grumbler: "a mannequin"): `bumps`
    [(t, direction, metres, along, power)] raise a bone's knob or a muscle's belly where the surface faces `direction`
    at t (0..1 along it); `ridges` the tendons standing round it, more toward its end; `lumps` it wasted unevenly. In
    cloth, `folds` hang in it, and at each t in `bunch` it's rucked up in rings (a sleeve's crook, a trouser's knee)."""
    pts, rr = along(points, radii, per)
    last = len(pts) - 1

    def shape(i, j, a, p, fr):
        p = Vector(p)
        o = fr[0] * math.sin(a) + fr[1] * math.cos(a)
        t = i / last
        off = lumps * noise3(p * 24, 152 + seed, 1.0)
        for t0, d, amt, wt, pw in bumps:
            off += amt * bell((t - t0) / wt) * max(0.0, o.dot(Vector(d).normalized())) ** pw
        off += ridges * max(0.0, math.cos(a * 4 + 0.6)) ** 3 * smooth01(0.1, 0.6, t)
        off += folds * math.sin(a * 5 + t * 11 + seed) * (0.6 + 0.4 * math.sin(t * 17 + a * 2))
        for tb in bunch:
            off += 0.006 * bell((t - tb) / 0.09) * (0.5 + 0.5 * math.sin(a * 7 + seed))
        return p + o * off
    return limbs.tube(pts, rr, sides, mat, bones, ref=ref, shape=shape)


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    w = jointed(sh, e, wr, ua, la)
    # (The T-pose's arm runs out along x: the back of the forearm up (+z), the elbow's point behind (-y).) The sleeve torn
    # off ragged half way down the upper arm (note 543: a sleeve to the elbow made the whole long limb one cloth tube), and
    # under it the arm wasted to the bone: the muscle shrunk to cords along it, the elbow's point standing out behind.
    sm = sh.lerp(e, 0.42)
    limb([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.22), sm], [0.052, 0.047, 0.047], 2, 12, SHIRT, w, (0, 0, 1), folds=0.004, seed=1 if sx > 0 else 2)
    limbs.tube([sm - Vector((sx * 0.01, 0, 0)), sm + Vector((sx * 0.03, 0, 0))], [0.047, 0.045], 12, SHIRT, w, ref=(0, 0, 1),
               shape=lambda i, j, a, p, fr, sx=sx: Vector(p) + Vector((sx * 0.035 * max(0.0, math.sin(j * 2.3 + 1)) ** 2 * (i == 1), 0, 0)))
    limb([sh.lerp(e, 0.3), sh.lerp(e, 0.6), e.lerp(sh, 0.04), e], [0.03, 0.025, 0.028, 0.03], 3, 12, SKIN, w, (0, 0, 1), ridges=0.004,
         bumps=[(0.55, (0, 0, 1), 0.008, 0.2, 3), (0.5, (0, -1, 0), 0.007, 0.25, 4), (1.0, (0, -1, 0), 0.042, 0.09, 2), (0.95, (0, 0, -1), 0.008, 0.08, 2)],
         lumps=0.004, seed=3)
    # The forearm a labourer's gone stringy: the muscle's belly up by the elbow, wasting to cords toward a knobbed wrist,
    # the tendons standing in ridges along it, the two bones' ridge down its back.
    limb([e, e.lerp(wr, 0.15), e.lerp(wr, 0.38), e.lerp(wr, 0.75), wr], [(0.034, 0.03), (0.04, 0.033), (0.03, 0.025), (0.019, 0.016), (0.023, 0.017)],
         2, 12, SKIN, w, (0, 0, 1), ridges=0.006, lumps=0.004,
         bumps=[(0.0, (0, -1, 0), 0.022, 0.07, 2), (0.25, (0, 0, 1), 0.007, 0.16, 2), (0.6, (0, -0.4, 1), 0.004, 0.25, 6),
                (0.97, (0, -0.3, 1), 0.009, 0.05, 3), (0.95, (0, 1, 0), 0.004, 0.05, 3)], seed=4)
    hand(wr, Vector((sx, 0, 0)), hd, f"fingers_{s}", 4, 1.15, sx)
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.8], [0.013, 0.011, 0.006], 6, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")
    # The rib arm: out through a hole torn in the waistcoat's side; thin, bare, the joints knobs.
    a0, a1, a2 = H(f"arm2_{s}_01"), H(f"arm2_{s}_02"), H(f"hand2_{s}")
    limbs.tube([a0 - Vector((sx * 0.04, 0, 0)), a0.lerp(a1, 0.5), a1, a1.lerp(a2, 0.5), a2], [0.026, 0.019, 0.022, 0.017, 0.015], 8, SKIN,
               jointed(a0, a1, a2, f"arm2_{s}_01", f"arm2_{s}_02"), ref=(0, 0, 1))
    limbs.blob(a1, (0.022, 0.024, 0.022), 6, 4, SKIN, f"arm2_{s}_02")
    hand(a2, Vector((sx, 0, 0)), f"hand2_{s}", f"hand2_{s}", 3, 0.8, sx)
    # The torn hole it comes out of: a ragged lip of cloth round it.
    lip = [a0 + Vector((sx * 0.005, 0.04 * math.cos(t), 0.04 * math.sin(t))) for t in (2 * math.pi * k / 7 for k in range(7))]
    limbs.tube(lip, [0.01] * 7, 4, COAT, "spine_02", ref=(1, 0, 0), loop=True)

for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    w = jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}")
    # The trousers torn off above the knee (note 543), and the knee bare: the cap and the bone's ends standing, the thigh
    # under the rag gone to cords.
    tm = hp.lerp(kn, 0.5)
    limb([hp + Vector((0, 0, 0.04)), hp.lerp(kn, 0.25), tm], [0.075, 0.064, 0.062], 2, 12, TROUSERS, w, (0, 1, 0), folds=0.004, seed=5 if sx > 0 else 6)
    limbs.tube([tm + Vector((0, 0, 0.012)), tm - Vector((0, 0, 0.035))], [0.062, (0.06, 0.06)], 12, TROUSERS, w, ref=(0, 1, 0),
               shape=lambda i, j, a, p, fr: Vector(p) - Vector((0, 0, 0.04 * max(0.0, math.sin(j * 2.1)) ** 2 * (i == 1))))
    limb([hp.lerp(kn, 0.4), hp.lerp(kn, 0.75), kn, kn.lerp(an, 0.32)], [0.045, 0.04, (0.044, 0.046), 0.036], 3, 12, SKIN, w, (0, 1, 0), ridges=0.003,
         lumps=0.002, bumps=[(0.62, (0, 1, 0), 0.026, 0.09, 2), (0.6, (1, 0.3, 0), 0.01, 0.1, 3), (0.6, (-1, 0.3, 0), 0.01, 0.1, 3),
                             (0.3, (0, 1, 0), 0.006, 0.2, 4)], seed=8)
    # The shin bare, wasted to the bone: its edge standing down the front, the calf gone to cords, the ankle's knobs.
    limb([kn.lerp(an, 0.3), kn.lerp(an, 0.5), kn.lerp(an, 0.8), an + Vector((0, 0, 0.02))], [(0.042, 0.038), (0.034, 0.03), (0.024, 0.02), (0.027, 0.021)],
         2, 12, SKIN, w, (0, 1, 0), ridges=0.005, lumps=0.004,
         bumps=[(0.5, (0, 1, 0), 0.007, 0.35, 8), (0.25, (0, -1, 0), 0.006, 0.18, 2), (0.97, (1, 0, 0), 0.008, 0.06, 4), (0.97, (-1, 0, 0), 0.008, 0.06, 4)],
         seed=7)
    limbs.blob(an, (0.03, 0.034, 0.03), 8, 4, SKIN, f"foot_{s}")
    limbs.tube([an - Vector((0, 0.05, 0)), an.lerp(bl, 0.5), bl], [(0.036, 0.026), (0.046, 0.02), (0.052, 0.017)], 8, SKIN, f"foot_{s}", ref=(0, 0, 1),
               cap0=True)
    for t in range(4):
        spread = (t - 1.5) * 0.026
        b0 = bl + Vector((spread, 0, 0))
        dirn = Vector((spread * 2.2, 1, -0.05)).normalized()
        tl = 0.08 if t in (1, 2) else 0.065
        limbs.tube([b0, b0 + dirn * tl * 0.55, b0 + dirn * tl], [0.012, 0.01, 0.008], 5, SKIN, f"ball_{s}", ref=(0, 0, 1))
        limbs.tube([b0 + dirn * (tl - 0.008), b0 + dirn * (tl + 0.012) - Vector((0, 0, 0.008))], [(0.007, 0.004), (0.002, 0.002)], 4, NAIL,
                   f"ball_{s}", ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. (The jaw drops by -X.) The spider crouch is set by reach (rig.reach, two-bone IK) from where the hands and feet go: down flat on the
# ground, wide, the elbows and knees pushed up high over the back by the IK's `avoid`.
BASE = mirror({"fingers_r": (0, 10, 0), "thumb_r": (0, 6, 0)})
LOW = 0.4                          # the hips' height in the crouch


def knee_high(min_z):
    return lambda e: 3.0 * max(0.0, min_z - e.z)


def limb(pose, upper, lower, target, axis, bend, high):
    return rig.reach(sk, pose, upper, lower, target, elbow_axis=axis, bend=bend, avoid=knee_high(high))


def crouch(pose, hands, feet, ribs=None, elbow=0.56, knee=0.66):
    """`pose` with its hands and feet put where `hands` and `feet` say ({side: point}, pose space, before planting), its
    elbows over `elbow` and its knees over `knee`; the rib arms' hands to `ribs`."""
    p = dict(pose)
    # (Started from a rough crouch, the legs brought forward under the tipped-over body and the arms down, for the IK to
    # settle from: from the T-pose it sticks with the feet up behind.)
    tip = -pose.get("pelvis", (0, 0, 0))[0]
    for s, k in (("r", 1), ("l", -1)):
        if s in feet:
            p.setdefault(f"thigh_{s}", (tip + 30, 0, 0))
            p.setdefault(f"calf_{s}", (-90, 0, 0))
        if s in hands:
            p.setdefault(f"upperarm_{s}", (20, 50 * k, 0))
    for s, at in feet.items():
        p = limb(p, f"thigh_{s}", f"calf_{s}", at, 0, -1, knee)
        p[f"foot_{s}"] = (30, 0, 0)
    for s, at in hands.items():
        p = limb(p, f"upperarm_{s}", f"lowerarm_{s}", at, 2, 1 if s == "r" else -1, elbow)
    for s, at in (ribs or {}).items():
        p = rig.reach(sk, p, f"arm2_{s}_01", f"arm2_{s}_02", at, elbow_axis=2, bend=1 if s == "r" else -1)
    return p


# The body laid forward over its hips and dropped low: the pelvis tipped right over, the back level, the neck down.
BODY = over(BASE, root__loc=(0, 0, LOW - HIP), pelvis=(-78, 0, 0), spine_01=(-4, 0, 0), spine_02=(2, 0, 0), spine_03=(4, 0, 0),
            neck=(-4, 0, 0), head=(66, 0, 0))
FEET = {"r": Vector((0.48, -0.2, 0.08)), "l": Vector((-0.48, -0.2, 0.08))}
HANDS = {"r": Vector((0.62, 0.78, 0.05)), "l": Vector((-0.62, 0.78, 0.05))}


def head_at(pose):
    return rig.pose_points(sk, pose, [("head", "head")])[0]


# Gnaw (1.6 s, loop): crouched over a crate, its head down in it and the jaw going hard; the rib arms tearing at what it's
# eating; still, but for the head's jerks, and once a leg twitches out and back.
GN = over(BODY, neck=(-38, 0, 0), head=(34, 0, 0))
mouth_to = head_at(GN) + Vector((0, 0.16, -0.1))
GN = crouch(GN, HANDS, FEET, ribs={"r": mouth_to + Vector((0.07, 0.02, -0.04)), "l": mouth_to + Vector((-0.07, 0.03, -0.03))})
gnaw = Clip("gnaw")
for f, (jaw, turn, tear) in enumerate(((10, 0, 0.0), (34, 6, 0.4), (14, -4, 0.8), (40, 10, 0.3), (8, -2, 0.0), (30, -12, 0.6), (12, 4, 1.0),
                                       (38, 0, 0.2))):
    p = over(GN, jaw=(-jaw, 0, 0), head=(34 - jaw * 0.2, 0, turn))
    p[f"hand2_r"] = (0, 0, 20 * tear)
    p[f"hand2_l"] = (0, 0, -20 * (1 - tear))
    gnaw.key(f * 6, p, "CONSTANT")
gnaw.key(30, over(crouch(GN, HANDS, {"r": FEET["r"] + Vector((0.12, -0.08, 0.06)), "l": FEET["l"]}, ribs={}), jaw=(-30, 0, 0)), "CONSTANT")
gnaw.key(33, over(GN, jaw=(-30, 0, 0)), "CONSTANT")
gnaw.close(48)

# Scuttle (0.5 s, loop): after whoever hit it, very low and very fast, the limbs in diagonal pairs (right hand with left
# foot), each thrown forward and slapped down; the head up and level, looking where it's going, the jaw hanging.
SC = over(BODY, root__loc=(0, 0, LOW - HIP - 0.06), neck=(2, 0, 0), head=(74, 0, 0), jaw=(-34, 0, 0))
scuttle = Clip("scuttle")
for f, k in ((0, 1.0), (4, 0.0), (8, -1.0), (12, 0.0)):
    lift_a, lift_b = max(0.0, k), max(0.0, -k)
    hands = {"r": HANDS["r"] + Vector((0, 0.14 * k, 0.12 * lift_a)), "l": HANDS["l"] + Vector((0, -0.14 * k, 0.12 * lift_b))}
    feet = {"l": FEET["l"] + Vector((0, 0.12 * k, 0.1 * lift_a)), "r": FEET["r"] + Vector((0, -0.12 * k, 0.1 * lift_b))}
    p = crouch(over(SC, pelvis=(-78, 0, 6 * k), spine_03=(4, 0, -6 * k)), hands, feet,
               ribs={"r": head_at(SC) + Vector((0.14, 0.05, -0.18)), "l": head_at(SC) + Vector((-0.14, 0.05, -0.18))})
    scuttle.key(f, p, "LINEAR")
scuttle.close(16)

# Bite (0.9 s, loop): in reach of its prey, reared up on its back legs, all four arms out and grabbing, the head lunging
# in with the jaw dropped as far as it goes, and snapping shut; and back.
REAR = over(BASE, root__loc=(0, 0, LOW + 0.1 - HIP), pelvis=(-34, 0, 0), spine_02=(-6, 0, 0), spine_03=(-10, 0, 0), neck=(-20, 0, 0), head=(44, 0, 0))
REAR = crouch(REAR, {}, FEET, knee=0.7)
sh_r, sh_l = rig.pose_points(sk, REAR, [("upperarm_r", "head"), ("upperarm_l", "head")])
bite = Clip("bite")
for f, (jaw, lunge) in ((0, (10, 0.0)), (8, (62, 0.6)), (12, (64, 1.0)), (14, (4, 1.0)), (20, (8, 0.3))):
    p = over(REAR, spine_03=(-10 - 24 * lunge, 0, 0), neck=(-20 - 10 * lunge, 0, 0), jaw=(-jaw, 0, 0))
    reach = 0.55 + 0.2 * lunge
    p = rig.reach(sk, p, "upperarm_r", "lowerarm_r", sh_r + Vector((0.18, reach, 0.05)), elbow_axis=2, bend=1, avoid=knee_high(sh_r.z + 0.1))
    p = rig.reach(sk, p, "upperarm_l", "lowerarm_l", sh_l + Vector((-0.18, reach, 0.05)), elbow_axis=2, bend=-1, avoid=knee_high(sh_l.z + 0.1))
    hd = head_at(p)
    p = rig.reach(sk, p, "arm2_r_01", "arm2_r_02", hd + Vector((0.2, 0.2 + 0.1 * lunge, -0.3)), elbow_axis=2, bend=1)
    p = rig.reach(sk, p, "arm2_l_01", "arm2_l_02", hd + Vector((-0.2, 0.2 + 0.1 * lunge, -0.3)), elbow_axis=2, bend=-1)
    p["fingers_r"], p["fingers_l"] = (0, 10 + 60 * lunge, 0), (0, -10 - 60 * lunge, 0)
    bite.key(f, p, "LINEAR" if f in (8, 12) else "CONSTANT")
bite.close(27)

# Maul (1.2 s, loop): on its victim, down on the ground in front of it: over them, every hand down on them, holding, and
# the head going down into them, again and again, the body shuddering with it.
MA = over(BODY, root__loc=(0, 0, LOW - HIP + 0.04), neck=(-46, 0, 0), head=(24, 0, 0))
ON = Vector((0, 0.75, 0.2))
maul = Clip("maul")
for f, (k, jaw) in enumerate(((0.0, 20), (1.0, 50), (0.3, 6), (1.0, 46), (0.2, 10), (0.9, 40))):
    p = over(MA, spine_03=(4 - 10 * k, 0, 0), neck=(-46 - 14 * k, 0, 0), jaw=(-jaw, 0, 0), pelvis=(-78, 0, 3 * math.sin(f * 2.1)))
    p = crouch(p, {"r": ON + Vector((0.3, 0.1, 0.0)), "l": ON + Vector((-0.3, 0.12, 0.0))}, FEET,
               ribs={"r": ON + Vector((0.14, -0.08, 0.08)), "l": ON + Vector((-0.14, -0.06, 0.08))})
    maul.key(f * 6, p, "LINEAR" if k > 0.5 else "CONSTANT")
maul.close(36)

hit = Clip("hit", loop=False)
hit.key(0, GN, "CONSTANT")
hit.key(2, over(GN, spine_02=(6, 0, 14), spine_03=(10, 0, 10), head=(30, 0, 30), jaw=(-50, 0, 0)), "CONSTANT")
hit.key(10, over(GN, jaw=(-20, 0, 0)), "LINEAR")
hit.key(14, GN, "CONSTANT")


def top(pose):
    pts = rig.pose_points(sk, pose, [("head", "tail"), ("spine_03", "tail"), ("lowerarm_r", "head"), ("lowerarm_l", "head"), ("calf_r", "head"),
                                     ("calf_l", "head")])
    feet = rig.pose_points(sk, pose, [(b, e) for b in ("foot_l", "foot_r", "ball_l", "ball_r") for e in ("head", "tail")])
    return max(p.z for p in pts) - min(p.z for p in feet)


for name, pose in (("gnaw", GN), ("scuttle", SC), ("bite", REAR), ("maul", MA)):
    print("[dt] grumbler", name, "stands", round(top(pose), 2), "m")

kit.build()
rig.bake(sk, [gnaw, scuttle, bite, maul, hit], plant=rig.feet_planter(sk, lowest=0.02))
print("[dt] grumbler", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "grumbler.glb", kit)
