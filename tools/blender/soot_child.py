"""SOOT CHILDREN (GDD v1.2 §21 outside, App. A.6 · sound): "A child calling for help. Half the time it's a real
survivor. Black eyes and blackened hands mean a Soot Child. Get within five metres and it pins you and drinks."

A child of seven or eight, too thin, sat on its heels in the ash by the line with its arms round its knees: an adult's
shirt gone to rag-grey hanging off it to the knees, tied at the waist with string; bare legs, bare feet; the hair
matted dark and hanging over the face; the skin waxy pale and grimed. It is the same child either way, which is the
lure. Variant 0 is the real one: eyes, a child's, the whites and the irises, and its hands and feet only dirty.
Variant 1 is a Soot Child: the eyes black, all of them, wet, filling the sockets, and the hands and the feet black to
the wrists and the ankles as if dipped in tar: the tell, from five metres, for anyone who looks. And when it's on you
the jaw drops, too far, to its chest, and there are teeth in there, rings of them, and nothing human about it.

SK_Human (rig.human) at a child's height and proportions, with a jaw. Faces +Y (the engine's -Z). Clips (§31: still,
then too fast): huddle (squatted, arms round its knees, head down on them, rocking), call (the head up to the train,
the mouth open calling, one hand out to whoever's there; still rocking), pin (one go: up off the ground onto the one
it's taken, standing, and locked round them, arms round the neck and legs round the waist; the jaw dropping), drink
(clinging there, its face in their throat, the jaw wide, pumping; now and then the head comes up and round to look at
whoever's watching, and goes back down).

    tools/models/build.sh soot_child        # this, its high copy and the bake -> content/art/models/soot_child.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
PROPS = dict(height=1.22, leg=0.92, arm=0.95, torso=0.95, neck=0.7, head=1.45, width=0.85, fingers=True, sockets=False)
_s = rig.human(**PROPS)
_b = {b.name: b for b in _s.bones}
NT = _b["head"].head.z
sk = rig.human(**PROPS, extra=[Bone("jaw", "head", (0, 0.03, NT + 0.075), (0, 0.095, NT + 0.03))])
sk.build()
kit = rig.Kit(sk, "soot_child")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


HIP = H("thigh_r").z
WAIST = H("spine_01").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
KNEE = H("calf_r").z
HC = Vector((0.0, 0.012, NT + 0.09))
HR = Vector((0.079, 0.086, 0.09))
HEM = KNEE + 0.04

SKIN = Mat("flesh.soot_child", hexc("#a39a92"), shine=0.3)
SHIRT = Mat("wool.soot_child_shirt", hexc("#4a4844"), shine=0.04)
STRING = Mat("rope.soot_child_string", hexc("#3a3025"), shine=0.1)
HAIR = Mat("tar.soot_child_hair", hexc("#120f0d"), shine=0.35)
LIPS = Mat("flesh.soot_child_lips", hexc("#6a5452"), shine=0.4)
MOUTH = Mat("tar.soot_child_mouth", hexc("#0c0606"), shine=0.5)
TEETH = Mat("flesh.soot_child_teeth", hexc("#b0a48a"), shine=0.45)
WHITE = Mat("flesh.soot_child_eye", hexc("#c8c2b4"), shine=0.75)
IRIS = Mat("tar.soot_child_iris", hexc("#2a1c12"), shine=0.8)
BLACK = Mat("tar.soot_child_eye", hexc("#030303"), shine=0.95)
SOOT = Mat("tar.soot_child_soot", hexc("#0b0a09"), shine=0.4)


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


EYE_U, EYE_W, MOUTH_W = 0.4, 0.04, -0.48


def face(i, j, a, th, p):
    """A child's face, starved (note 543's way, note 546): the forehead high and round, but the skin thin over the bones,
    so the brow and the cheekbones show and the cheeks are drawn in under them; the eyes sunk deep in their sockets, so
    the eyes in them catch the light, or don't; the nose small, the chin small, the jaw a child's, narrow."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(0.15, 0.6, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.023 * front * bell(math.hypot((u - eu) / 0.27, (w - EYE_W) / 0.21))
        off += 0.008 * front * bell(math.hypot((u - eu) / 0.32, (w - EYE_W - 0.24) / 0.08))      # the brow over it
        off += 0.007 * front * bell(math.hypot((u - eu * 1.22) / 0.2, (w + 0.12) / 0.09))      # the cheekbones
        off -= 0.009 * front * bell(math.hypot((u - eu * 1.1) / 0.22, (w + 0.42) / 0.16))      # the cheeks drawn in
    off += 0.017 * front * bell(u / 0.1) * bell((w + 0.22) / 0.13) + 0.006 * front * bell(u / 0.07) * bell((w + 0.02) / 0.15)   # the nose, small
    off -= 0.004 * front * bell(math.hypot(u / 0.3, (w - MOUTH_W) / 0.08))
    off += 0.005 * front * bell(math.hypot(u / 0.28, (w + 0.78) / 0.12))                         # the chin
    off -= 0.006 * bell((w + 0.75) / 0.25) * smooth01(0.5, 0.95, abs(u))
    off -= 0.007 * bell((abs(u) - 0.88) / 0.12) * bell((w - 0.2) / 0.22) * smooth01(-0.3, 0.3, v)   # the temples
    return p + n * (off + 0.001 * noise3(p * 40, 81, 1.0))


def on_face(u, w, lift=0.0):
    v = math.sqrt(max(0.0, 1 - u * u - w * w))
    p = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    n = head_normal(p)
    return face(0, 0, 0, 0, p) + n * lift, n


def head_weights(p):
    """The jaw takes the face below the mouth (and the chin under it); the rest's the head's."""
    u, v, w = head_units(p)
    k = smooth01(MOUTH_W + 0.06, MOUTH_W - 0.06, w) * smooth01(-0.2, 0.25, v)
    return {n: x for n, x in (("head", 1 - k), ("jaw", k)) if x > 1e-4}


head = kit.part("head")
head.blob(HC, tuple(HR), 32, 26, SKIN, head_weights, shape=face,
          fmat=lambda pts, n: MOUTH if all(abs(head_units(q)[0]) < 0.28 and abs(head_units(q)[2] - MOUTH_W) < 0.03 and head_units(q)[1] > 0.6 for q in pts) else SKIN)
for sx in (1, -1):
    head.blob(HC + Vector((sx * HR.x * 0.95, -0.006, -0.005)), (0.009, 0.019, 0.026), 6, 5, SKIN, "head")
# The lips, a child's, parted; behind them the dark of the mouth, and its throat, both on the jaw and the head.
upper = [on_face(x, MOUTH_W + 0.03 - 0.02 * (x / 0.22) ** 2, 0.001)[0] for x in (-0.22, -0.12, 0.0, 0.12, 0.22)]
lower = [on_face(x, MOUTH_W - 0.025 + 0.015 * (x / 0.22) ** 2, 0.001)[0] for x in (-0.22, -0.12, 0.0, 0.12, 0.22)]
head.tube(upper, [0.0015, 0.003, 0.0034, 0.003, 0.0015], 6, LIPS, "head", ref=(0, 0, 1))
head.tube(lower, [0.0015, 0.0034, 0.0038, 0.0034, 0.0015], 6, LIPS, "jaw", ref=(0, 0, 1))
M0, _ = on_face(0.0, MOUTH_W, -0.03)
head.blob(M0 + Vector((0, -0.005, 0.0)), (0.026, 0.03, 0.024), 10, 6, MOUTH, {"head": 0.5, "jaw": 0.5})
head.blob(M0 + Vector((0, -0.01, -0.02)), (0.024, 0.026, 0.016), 10, 5, MOUTH, "jaw")
# The hair: matted, dark, chopped, hanging; strands down over the face.
hair = kit.part("hair")


def hair_shape(i, j, a, th, p):
    p = Vector(p)
    u, v, w = head_units(p)
    # Off the face, and hanging lank behind and at the sides, longer at the back.
    if v > 0.25 and w < 0.62:
        p += Vector((0, -0.06 * smooth01(0.25, 0.7, v), 0.0))
    p.z -= 0.05 * smooth01(0.2, -0.6, w) * smooth01(0.4, -0.6, v)
    # Matted into clumps (note 546: it was a helmet): ridges running down the head, uneven, and the clumps' ends ragged.
    clump = max(0.0, math.sin(a * 11 + 1.7 * noise3(p * 18, 85, 1.0))) ** 2
    return p + head_normal(p) * (0.007 * clump + 0.004 * noise3(p * 60, 82, 1.0))


hair.blob(HC + Vector((0, -0.006, 0.012)), (HR.x * 1.08, HR.y * 1.06, HR.z * 1.04), 26, 12, HAIR, "head", shape=hair_shape, z0=-0.5)
# Lank clumps down the sides and the back (round from the temples: the face, and its eyes, stay clear), and a few hanging
# forward over the brow and the cheeks, matted into points.
for k in range(26):
    a = (0.7 + 2.1 * (k // 2) / 12) * (1 if k % 2 else -1)
    root = HC + Vector((math.sin(a) * HR.x * 0.98, math.cos(a) * HR.y * 0.95, HR.z * (0.25 + 0.2 * math.sin(k * 1.3))))
    ln = 0.08 + 0.07 * (0.5 + 0.5 * math.sin(k * 2.3)) + 0.05 * (abs(a) > 2.0)
    out = Vector((math.sin(a), math.cos(a), 0)) * 0.012
    sway = Vector((0.01 * math.sin(k * 3.1), 0.0, 0.0))
    pts = [root, root + out + sway * 0.5 + Vector((0, 0, -ln * 0.45)), root + out * 1.3 + sway + Vector((0, 0, -ln * 0.8)), root + out * 1.2 + sway + Vector((0, 0, -ln))]
    hair.tube(pts, [0.011, 0.009, 0.005, 0.0015], 5, HAIR, "head", ref=(0, 0, 1), cap1="point")
# The fringe: matted clumps down over the forehead to the brows, so there's no clean band of brow between the hair and
# the eyes (note 546), parted a little over the left eye.
for k in range(9):
    u = -0.62 + 1.24 * k / 8
    if abs(u - 0.38) < 0.12:
        continue
    root = HC + Vector((u * HR.x * 0.95, HR.y * (0.55 - 0.25 * u * u), HR.z * 0.82))
    ln = 0.05 + 0.02 * (0.5 + 0.5 * math.sin(k * 2.9))
    tip = on_face(u * 1.05, EYE_W + 0.24 + 0.06 * math.sin(k * 1.7), 0.008)[0]
    mid = root.lerp(tip, 0.5) + Vector((0, 0.012, 0))
    hair.tube([root, mid, tip + (tip - mid).normalized() * (ln - 0.05)], [0.009, 0.007, 0.0015], 5, HAIR, "head", ref=(0, 0, 1), cap1="point")
for sx, du in ((1, 0.72), (-1, 0.72), (1, 0.3), (-1, 0.15)):
    root = HC + Vector((sx * HR.x * du, HR.y * 0.62, HR.z * 0.55))
    ln = 0.12 if du > 0.5 else 0.07
    hair.tube([root, root + Vector((sx * 0.01, 0.018, -ln * 0.5)), root + Vector((sx * 0.012, 0.014, -ln))], [0.008, 0.006, 0.0015], 5, HAIR, "head",
              ref=(0, 0, 1), cap1="point")
# The neck, thin.
NECK = [Vector((0, 0.0, TOP - 0.02)), Vector((0, 0.004, (TOP + NT) / 2)), Vector((0, 0.008, NT + 0.03))]
head.tube(NECK, [(0.032, 0.03), (0.026, 0.026), (0.028, 0.03)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0))

# The eyes. Variant 0: a child's, the white and the iris. Variant 1: black, all of them, filling the sockets.
real_eyes = kit.part("eyes_real", variants=(0,))
soot_eyes = kit.part("eyes_soot", variants=(1,))
for eu in (-EYE_U, EYE_U):
    c, n = on_face(eu, EYE_W, -0.004)
    real_eyes.blob(c, (0.0135, 0.011, 0.012), 10, 7, WHITE, "head")
    real_eyes.blob(c + n * 0.0095, (0.0068, 0.003, 0.0068), 8, 4, IRIS, "head")
    soot_eyes.blob(c + n * 0.001, (0.0165, 0.012, 0.0128), 12, 7, BLACK, "head")
# A Soot Child's teeth, in rings back in the mouth: only seen when the jaw's down.
teeth = kit.part("teeth", variants=(1,))
for ring, (r, n_) in enumerate(((0.019, 12), (0.014, 10))):
    for k in range(n_):
        t = 2 * math.pi * k / n_
        base = M0 + Vector((math.cos(t) * r, -0.004 - ring * 0.008, math.sin(t) * r * 0.85))
        tip = base + Vector((-math.cos(t) * 0.008, 0.003, -math.sin(t) * 0.007))
        teeth.tube([base, tip], [0.0018, 0.0002], 3, TEETH, "jaw" if math.sin(t) < 0 else "head", ref=(0, 1, 0))

# ----------------------------------------------------------------------------------------------------------------
# The shirt: an adult's, hanging off it to the knees, the collar slack off one shoulder, the sleeves rolled to the
# elbow, tied in at the waist with string; the hem ragged.
body = kit.part("body")
TORSO = [(WAIST - 0.03, 0.105, 0.082), (WAIST + 0.04, 0.098, 0.078), (CHEST - 0.03, 0.108, 0.082), (CHEST + 0.04, 0.12, 0.084),
         (TOP - 0.01, 0.13, 0.078), (TOP + 0.02, 0.07, 0.055)]


def baggy(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    return p + out * (0.004 * math.sin(a * 6 + p.z * 30) + 0.002 * noise3(p * 20, 83, 1.0))


rings = []
for (z0, x0, y0), (z1, x1, y1) in zip(TORSO, TORSO[1:]):
    for k in range(3):
        f = k / 3
        rings.append((z0 + (z1 - z0) * f, x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
rings.append(TORSO[-1])
body.tube([Vector((0, -0.004, z)) for z, _, _ in rings], [(rx, ry) for _, rx, ry in rings], 14, SHIRT, (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0),
          ref=(0, 1, 0), shape=baggy)


def skirt_weights(p):
    k = smooth01(WAIST, KNEE + 0.1, p.z)
    side = smooth01(-0.04, 0.04, p.x)
    w = {"pelvis": 1 - k}
    if k > 0:
        if side > 1e-4:
            w["thigh_r"] = k * side
        if side < 1 - 1e-4:
            w["thigh_l"] = k * (1 - side)
    return {n: v for n, v in w.items() if v > 1e-4}


SKIRT = [(WAIST - 0.02, 0.108, 0.084), (HIP, 0.122, 0.094), (HIP - 0.12, 0.135, 0.104), (HEM + 0.03, 0.142, 0.11), (HEM, 0.144, 0.112)]


def hem(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    if i == len(SKIRT) - 1:
        p.z += 0.04 * max(0.0, noise3(Vector((math.cos(a), math.sin(a), 0)) * 3, 84, 1.0)) + 0.01 * abs(math.sin(a * 9))
    return p + out * 0.005 * math.sin(a * 7) * smooth01(HIP, HEM, p.z)


body.tube([Vector((0, -0.004, z)) for z, _, _ in SKIRT], [(rx, ry) for _, rx, ry in SKIRT], 16, SHIRT, skirt_weights, ref=(0, 1, 0), shape=hem)
body.tube([Vector((math.sin(t) * 0.112, math.cos(t) * 0.086 - 0.004, WAIST + 0.01 + 0.004 * math.sin(t * 3))) for t in (2 * math.pi * k / 12 for k in range(12))],
          [0.0045] * 12, 4, STRING, "spine_01", ref=(0, 0, 1), loop=True)
# The collar, slack, off the left shoulder.
body.tube([Vector((math.sin(t) * 0.075 - 0.012, math.cos(t) * 0.06 - 0.004, TOP + 0.005 - 0.02 * max(0.0, -math.sin(t)))) for t in (2 * math.pi * k / 12 for k in range(12))],
          [0.008] * 12, 5, SHIRT, "spine_03", ref=(0, 0, 1), loop=True)

limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.025):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


def built(part, points, radii, per, sides, mat, bones, ref, bumps=(), lumps=0.0, fmat=None, **kw):
    """A limb built along its length (note 546, as the Grumbler's, note 543): `per` more rings between each pair, a bone's
    knob or a tendon raised at t (0..1 along it) where the surface faces `direction` (`bumps`: (t, direction, metres,
    along, power)), and wasted unevenly (`lumps`)."""
    rs = [r if isinstance(r, tuple) else (r, r) for r in radii]
    pts, rr = [], []
    for (p0, r0), (p1, r1) in zip(zip(points, rs), zip(points[1:], rs[1:])):
        for k in range(per + 1):
            f = k / (per + 1)
            pts.append(p0.lerp(p1, f))
            rr.append((r0[0] + (r1[0] - r0[0]) * f, r0[1] + (r1[1] - r0[1]) * f))
    pts.append(points[-1])
    rr.append(rs[-1])
    last = len(pts) - 1

    def shape(i, j, a, p, fr):
        p = Vector(p)
        o = fr[0] * math.sin(a) + fr[1] * math.cos(a)
        t = i / last
        off = lumps * noise3(p * 30, 86, 1.0)
        for t0, d, amt, wt, pw in bumps:
            off += amt * bell((t - t0) / wt) * max(0.0, o.dot(Vector(d).normalized())) ** pw
        return p + o * off
    return part.tube(pts, rr, sides, mat, bones, ref=ref, shape=shape, fmat=fmat, **kw)


def sooted(a, b, start, mat_soot, mat_skin):
    """A face's material along a→b: the soot climbs to `start` (0..1 from b back toward a), raggedly, in runs and
    tongues, not stopping at a cuff (note 546: the hands were black gloves)."""
    d = b - a

    def fmat(pts, n):
        c = sum(pts, Vector()) / len(pts)
        t = (c - a).dot(d) / d.length_squared
        line = 1 - start + 0.1 * noise3(c * 45, 87, 1.0) - 0.12 * max(0.0, noise3(c * 18, 88, 1.0))
        return mat_soot if t > line else mat_skin
    return fmat


hands = {0: kit.part("hands_real", variants=(0,)), 1: kit.part("hands_soot", variants=(1,))}
for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    elbow = jointed(sh, e, wr, ua, la)
    # The sleeve, rolled to the elbow.
    limbs.tube([sh - Vector((sx * 0.012, 0, 0)), sh.lerp(e, 0.5), e + Vector((sx * 0.012, 0, 0))], [(0.045, 0.042), 0.036, 0.04], 9, SHIRT,
               elbow, ref=(0, 0, 1), shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.003 * math.sin(a * 5))
    k0 = T(hd)

    def fore(p, e=e, wr=wr, la=la, hd=hd, elbow=elbow, sx=sx):
        k = smooth01(-0.008, 0.008, (p - wr).x * sx)
        w = elbow(p) if (p - e).x * sx < 0.03 else {la: 1.0}
        if k > 0:
            w = {n: x * (1 - k) for n, x in w.items()}
            w[hd] = w.get(hd, 0) + k
        return {n: x for n, x in w.items() if x > 1e-4}
    # The forearm, thin as a stick, the elbow's knob and the wrist's knobs standing; the hand. A Soot Child's black from
    # the fingers up past the wrist, in a ragged line half way up the forearm; the real child's only dirty (the bake).
    for v, mat in ((0, SKIN), (1, SOOT)):
        part = hands[v]
        built(part, [e - Vector((sx * 0.01, 0, 0)), e.lerp(wr, 0.5), wr, wr.lerp(k0, 0.6), k0], [0.022, 0.018, (0.017, 0.015), (0.025, 0.01), (0.026, 0.009)],
              2, 10, SKIN, fore, (0, 0, 1), bumps=[(0.02, (0, -1, 0), 0.008, 0.06, 2), (0.5, (0, 0, 1), 0.004, 0.06, 3), (0.5, (0, -0.4, 1), 0.003, 0.06, 4)],
              lumps=0.0012, fmat=sooted(e, k0, 0.62, SOOT, SKIN) if v else None)
        for f in range(4):
            spread = (f - 1.5) * 0.013
            base = k0 + Vector((0, spread, 0))
            along = Vector((sx, spread * 0.6, 0)).normalized()
            n = 0.055 * (0.88 + 0.12 * (1 - abs(f - 1.5) / 1.5))
            # Thin, the knuckles standing.
            ts = (0.0, 0.25, 0.45, 0.55, 0.72, 0.85, 1.0)
            part.tube([base + along * n * t for t in ts], [0.0068, 0.0052, 0.006, 0.005, 0.0055, 0.0045, 0.003], 6, mat,
                      lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.15 else {f"fingers_{s}": 1.0}, ref=(0, 0, 1), cap1=True)
        th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
        part.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.3], [0.0075, 0.0065, 0.004], 5, mat, f"thumb_{s}", ref=(0, 0, 1), cap1=True)

feet = {0: kit.part("feet_real", variants=(0,)), 1: kit.part("feet_soot", variants=(1,))}
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    knee = jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}")
    # The thigh, thin, and the knee too big for it, a child's starved.
    built(limbs, [hp + Vector((0, 0, -0.04)), hp.lerp(kn, 0.5), kn], [0.042, 0.032, (0.03, 0.031)], 2, 10, SKIN, knee, (0, 1, 0),
          bumps=[(1.0, (0, 1, 0), 0.012, 0.12, 2), (0.95, (1, 0.3, 0), 0.005, 0.1, 3), (0.95, (-1, 0.3, 0), 0.005, 0.1, 3)], lumps=0.001)
    for v, mat in ((0, SKIN), (1, SOOT)):
        # The shin to the ankle (black from the foot up, raggedly, half way to the knee: a Soot Child's), and the foot.
        built(feet[v], [kn, kn.lerp(an, 0.5), an + Vector((0, -0.006, 0.01))], [(0.03, 0.031), 0.023, (0.023, 0.025)], 3, 10, SKIN, knee, (0, 1, 0),
              bumps=[(0.0, (0, 1, 0), 0.01, 0.1, 2), (0.45, (0, 1, 0), 0.004, 0.3, 8), (1.0, (1, 0, 0), 0.005, 0.07, 4), (1.0, (-1, 0, 0), 0.005, 0.07, 4)],
              lumps=0.001, fmat=sooted(kn, an, 0.55, SOOT, SKIN) if v else None)
        feet[v].tube([an + Vector((0, -0.008, 0.012)), an.lerp(bl, 0.5) + Vector((0, 0, -0.012)), bl + Vector((0, 0, -0.008)),
                      toe + Vector((0, -0.006, -0.01))], [(0.026, 0.028), (0.03, 0.02), (0.031, 0.016), (0.026, 0.012)], 9, mat,
                     [f"foot_{s}", f"foot_{s}", f"ball_{s}", f"ball_{s}"], ref=(0, 0, 1), cap1=True)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/crew.py (rig.rot): the thigh forward by +X, the knee by -X; the spine forward by -X; the
# arm lowered from the T-pose by +Y (the right; mirror() the left), swung forward by -X... and the jaw opens by -X.
def arm_to(pose, s, target, curl=0.6):
    sign = 1 if s == "r" else -1
    best = None
    for x in (-60, 0, 60):
        for y in (20, 60, 90):
            start = dict(pose) | {f"upperarm_{s}": (x, y * sign, 0), f"lowerarm_{s}": (0, 0, 0)}
            p = rig.reach(sk, start, f"upperarm_{s}", f"lowerarm_{s}", target, elbow_axis=2, bend=sign)
            tip = rig.pose_points(sk, p, [(f"lowerarm_{s}", "tail")])[0]
            cost = (tip - target).length
            if best is None or cost < best[0]:
                best = (cost, p)
    p = best[1]
    p[f"fingers_{s}"] = (0, sign * (10 + 70 * curl), 0)
    return p


# The huddle: squatted on its heels, knees to its chest, the back curled over them, the head down on them; the arms
# round the shins, the hands holding each other's wrists in front.
SQUAT = mirror({"thigh_r": (128, 0, 10), "calf_r": (-150, 0, 0), "foot_r": (34, 0, 0)}) | {
    "pelvis": (-8, 0, 0), "spine_01": (-26, 0, 0), "spine_02": (-20, 0, 0), "spine_03": (-12, 0, 0), "neck": (-10, 0, 0), "head": (-20, 0, 0)}
kr, kl = rig.pose_points(sk, SQUAT, [("calf_r", "head"), ("calf_l", "head")])
HUDDLE = arm_to(arm_to(SQUAT, "r", kr + Vector((-0.05, 0.07, -0.13)), curl=0.8), "l", kl + Vector((0.05, 0.07, -0.13)), curl=0.8)

# Huddle (4 s, loop): rocking, slowly, forward and back on its heels, the head down.
huddle = Clip("huddle")
for f, k in ((0, 0.0), (30, 1.0), (60, 0.0), (90, -0.6)):
    huddle.key(f, over(HUDDLE, pelvis=(-8 + 6 * k, 0, 0), spine_01=(-26 - 3 * k, 0, 0), head=(-20 + 4 * k, 0, 0)), "BEZIER")
huddle.close(120)

# Call (3 s, loop): the head up off the knees to the train, the mouth open, calling; the right hand off its shin and out
# to whoever's there, open, pleading; still rocking. Then the head down again a moment, and up.
CALLING = over(HUDDLE, spine_01=(-18, 0, 0), spine_02=(-10, 0, 0), spine_03=(-4, 0, 0), neck=(10, 0, 0), head=(16, 0, 0), jaw=(-16, 0, 0))
sh_r = rig.pose_points(sk, CALLING, [("upperarm_r", "head")])[0]
CALLING = arm_to(CALLING, "r", sh_r + Vector((0.08, 0.3, -0.12)), curl=0.25)
call = Clip("call")
call.key(0, CALLING, "BEZIER")
call.key(20, over(CALLING, pelvis=(-4, 0, 0), jaw=(-22, 0, 0), head=(20, 0, 4)), "BEZIER")
call.key(40, over(CALLING, jaw=(-8, 0, 0)), "BEZIER")
call.key(62, over(CALLING, pelvis=(-4, 0, 0), jaw=(-24, 0, 0), head=(18, 0, -4)), "BEZIER")
call.key(76, over(CALLING, neck=(-6, 0, 0), head=(-8, 0, 0), jaw=(-4, 0, 0)), "BEZIER")
call.close(90)

# On them: locked round the one it's taken, standing (they're in front of it, facing it, 0.2 m off: CreatureArt puts it
# there), lifted to them, its legs round their waist and its arms round their neck, its face at their throat.
VICTIM_NECK = Vector((0, 0.3, 1.42))
LIFT = 0.34
CLING = mirror({"thigh_r": (70, 0, -44), "calf_r": (-104, 0, 0), "foot_r": (40, 0, 0)}) | {
    "root@loc": (0, 0.02, LIFT), "pelvis": (14, 0, 0), "spine_01": (-8, 0, 0), "spine_02": (-10, 0, 0), "spine_03": (-8, 0, 0),
    "neck": (-16, 0, 0), "head": (-28, 0, 20), "jaw": (-48, 0, 0)}


def clinging(pose):
    p = arm_to(pose, "r", VICTIM_NECK + Vector((-0.07, 0.04, 0.0)), curl=0.9)
    return arm_to(p, "l", VICTIM_NECK + Vector((0.07, 0.04, 0.02)), curl=0.9)


# Pin (0.6 s, once): from a crouch at their feet, up and onto them, too fast; the jaw dropping as it goes.
pin = Clip("pin", loop=False)
pin.key(0, over(SQUAT, root__loc=(0, -0.05, 0), jaw=(-10, 0, 0)), "CONSTANT")
pin.key(3, over(SQUAT, root__loc=(0, -0.02, -0.06), spine_01=(-36, 0, 0), jaw=(-20, 0, 0)), "LINEAR")
pin.key(8, clinging(over(CLING, root__loc=(0, 0.04, LIFT + 0.12), jaw=(-60, 0, 0), head=(-6, 0, 0))), "LINEAR")
pin.key(14, clinging(over(CLING, jaw=(-62, 0, 0))), "BEZIER")
pin.key(18, clinging(CLING), "BEZIER")

# Drink (2.4 s, loop): face in their throat, the jaw wide, the head and the shoulders pumping with each swallow, the
# fingers kneading at the back of their neck. Then the head comes up off them and round, slowly, to look over its
# shoulder at whoever's watching, the black eyes and the mouth wide, held; and back down, fast.
drink = Clip("drink")
for f, k in ((0, 0.0), (6, 1.0), (12, 0.0), (18, 1.0), (24, 0.0), (30, 1.0)):
    drink.key(f, clinging(over(CLING, spine_03=(-8 - 6 * k, 0, 0), neck=(-16 - 6 * k, 0, 0), jaw=(-48 + 10 * k, 0, 0))) |
              {"fingers_r": (0, 80 - 30 * k, 0), "fingers_l": (0, -80 + 30 * k, 0)}, "BEZIER")
drink.key(44, clinging(over(CLING, neck=(4, 0, -20), head=(10, 0, -64), jaw=(-58, 0, 0))), "BEZIER")
drink.key(60, clinging(over(CLING, neck=(4, 0, -20), head=(10, 0, -66), jaw=(-60, 0, 0))), "CONSTANT")
drink.key(63, clinging(CLING), "BEZIER")
drink.close(72)

# Clutch (3 s, loop; GDD App. C.4 "the child is carried in the arms"): a rescued child, the real one, held by whoever
# carries it: the same hold as the lure's (CLING, its legs round their waist and its arms round their neck), but the
# face turned away and laid on their shoulder, the mouth shut, the fingers holding on; breathing, a hitch now and then.
# (SceneArt puts its origin where CreatureArt puts the lure's, at the carrier's feet, 0.3 m out, facing them.)
CLUTCH = over(CLING, pelvis=(10, 0, 0), spine_03=(-4, 0, 0), neck=(-6, 0, 18), head=(-14, 0, 52), jaw=(0, 0, 0))
clutch = Clip("clutch")
for f, (k, hitch) in ((0, (0.0, 0.0)), (24, (1.0, 0.0)), (48, (0.0, 0.0)), (60, (0.6, 1.0)), (66, (0.2, 0.0))):
    clutch.key(f, clinging(over(CLUTCH, spine_02=(-10 - 2 * k, 0, 0), spine_03=(-4 - 2 * k - 3 * hitch, 0, 0),
                                head=(-14 + 3 * hitch, 0, 52 - 4 * hitch))) | {"fingers_r": (0, 85, 0), "fingers_l": (0, -85, 0)}, "BEZIER")
clutch.close(90)

kit.build()
rig.bake(sk, [huddle, call, pin, drink, clutch], plant=rig.feet_planter(sk, lowest=0.015, clips=["huddle", "call"]))
print("[dt] soot_child", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "soot_child.glb", kit)
