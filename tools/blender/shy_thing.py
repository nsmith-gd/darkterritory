"""THE SHY THING (GDD v1.5 §21, App. A.6 · sight): "A tall, pale thing standing where you have to look. Only you can see
it. Watch it and you can't move. ... At arm's length its jaw comes down like a snake's, and now everyone can see it."

A thing near two and a half metres tall and as thin as a rake, standing out in the dark past the lamps like a child
who's been told to wait there. It's bare, and smooth all over as a peeled egg, the dead ivory of old candles (§28),
the collarbones and the knuckles of the spine standing under it; no hair, no ears. The head is small for the height of
it, on a neck too long, and the face is a child's face stretched: two big black eyes set wide and deep, the wet in them
the only shine on it; no nose but two slits; a little mouth, shut tight, a line. It stands bashful, the shoulders up,
the head bowed and tipped to one side, its long hands held together under its chin with the fingertips at its lips;
the arms are long enough that, let down, the hands hang past its knees.

It isn't shy. The jaw is a snake's: under the little mouth it's hinged far back, and when it unhinges the lower jaw
comes away and down to its chest on the stretched skin of its cheeks, the mouth a wet black gape a head can go into,
needle teeth round the rim.

SK_Human (rig.human) drawn out, with a jaw (bone `jaw`, under the head). Faces +Y (the engine's -Z). Clips (§31:
still, too still, then too-fast corrections): wait (bashful, dead still: a slow breath, and now and then its head tips
further over, all at once), stare (it has someone: the head up and turned to them, the hands let down, dead still but
the fingers), walk (drawing in at 0.7 m/s: long slow strides, the body going on under a head that never moves), unhinge
(the GRAB, played once over its window: the jaw cracking down in jerks while its hands come up either side of where
its victim's head is, the head going over them), swallow (PUNISH: down over them, the maw wide, gulping), hit.

    tools/blender/build.sh shy_thing        # -> content/art/models/shy_thing.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# Long in the leg, the neck very long and the arms longer than a man's by half again: a rake of a thing, 2.47 m.
PROPS = dict(height=2.2, leg=1.12, arm=1.6, torso=1.05, neck=1.9, head=0.95, width=0.7, fingers=True, sockets=False)
_s = rig.human(**PROPS)
_b = {b.name: b for b in _s.bones}
NT = _b["head"].head.z
# The jaw's hinge far back, low under where the ears would be; it points forward to the chin.
JAW_HINGE = Vector((0, -0.045, NT + 0.045))
sk = rig.human(**PROPS, extra=[Bone("jaw", "head", tuple(JAW_HINGE), tuple(JAW_HINGE + Vector((0, 0.13, -0.02))))])
sk.build()
kit = rig.Kit(sk, "shy_thing")


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
# The skull: small for the height, a child's proportions (the cranium full, the face small and low on it).
HC = Vector((0.0, 0.02, NECK_TOP + 0.115))
HR = Vector((0.088, 0.098, 0.113))
SLIT = -0.44                     # the mouth, in head units: the line the jaw comes away along
EYE_U, EYE_W, EYE_R = 0.47, 0.06, 0.022

# Dead ivory (§28): the skin texture lightened and cooled, a little wax-shine; the gape wet and black-red.
SKIN = Mat("skin.shy", hexc("#d8d2c4"), shine=0.36, tint=(3.0, 4.0, 4.8))
FACE = Mat("skin.shy_face", hexc("#ddd7ca"), shine=0.4, tint=(3.1, 4.1, 4.9))
EYE = Mat("glass_dirty.shy_eye", hexc("#050404"), shine=0.95, tint=(0.08, 0.07, 0.07))
MOUTH = Mat("flesh.shy_mouth", hexc("#1d0807"), shine=0.8, tint=(0.32, 0.09, 0.09))
GUM = Mat("flesh.shy_gum", hexc("#5a2a28"), shine=0.6, tint=(0.95, 0.5, 0.48))
TEETH = Mat("skin.shy_teeth", hexc("#d6ccb0"), shine=0.55, tint=(2.8, 3.2, 3.1))
NAIL = Mat("tar.shy_nail", hexc("#6b6152"), shine=0.4, tint=(1.6, 1.5, 1.35))


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


def on_head(u, w, inset=0.0, front=True):
    """The point on the skull's egg at (u across, w up), on its face (or its back), `inset` in under the skin."""
    v = math.sqrt(max(0.0, 1 - u * u - w * w)) * (1 if front else -1)
    surf = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    return surf - head_normal(surf) * inset


def face_shape(i, j, a, th, p):
    """A child's face stretched: the cranium full and smooth, the face small and low on it and nearly flat; the eyes'
    sockets wide-set and deep, no brow to speak of; the nose only two slits on a soft rise; the cheeks full, the chin
    small, narrowed to a point under the jaw's hinge."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(-0.2, 0.35, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.034 * front * bell(math.hypot((u - eu) / 0.24, (w - EYE_W) / 0.24))          # the sockets, wide-set and deep
        off += 0.006 * front * bell((u - eu * 1.25) / 0.2) * bell((w - EYE_W + 0.32) / 0.12)    # the cheeks' fullness
    off += 0.007 * front * bell(u / 0.14) * bell((w + 0.2) / 0.1)                              # a soft rise for a nose
    for su in (-0.07, 0.07):
        off -= 0.004 * front * bell((u - su) / 0.03) * bell((w + 0.24) / 0.035)                 # its two slits
    off -= 0.006 * front * bell(u / 0.4) * bell((w - SLIT) / 0.05)                              # the mouth's line
    q = p + n * off
    # The face flattened a touch, the cranium swelling over and behind it.
    if v > 0.3:
        q.y -= 0.01 * smooth01(0.3, 1.0, v) * smooth01(0.4, -0.2, w)
    if w > 0.3:
        q += n * 0.006 * smooth01(0.3, 0.9, w)
    return q


def jaw_shape(i, j, a, th, p):
    """The jaw under the mouth: the face's shape (so the two meet on the mouth's line), narrowed to a small pointed chin,
    tucked back under the face."""
    u, v, w = head_units(Vector(p))
    p = face_shape(i, j, a, th, p)
    k = smooth01(SLIT, -1.0, w)
    p.x = HC.x + (p.x - HC.x) * (1 - 0.32 * k)
    p.y -= 0.012 * k * smooth01(-0.2, 0.6, v)
    p.z -= 0.012 * k
    return p


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    if normal.z < -0.6 and w < SLIT + 0.08:
        return MOUTH                # the roof of the mouth, seen only once it's open
    return FACE if v > 0.2 and SLIT - 0.05 < w < 0.55 and abs(u) < 0.9 else SKIN


def jaw_or_floor(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    if normal.z > 0.6 and w > SLIT - 0.08:
        return MOUTH                # the floor of the mouth
    return FACE if v > 0.2 else SKIN


# ----------------------------------------------------------------------------------------------------------------
# The head: the skull above the mouth's line; the jaw under it, its own; between them, round the sides and the back,
# the skin of the cheeks that stretches when it comes away, black and wet inside; the teeth on both rims, small and
# many; the eyes.
head = kit.part("head")
head.blob(HC, tuple(HR), 24, 18, SKIN, "head", z0=SLIT, shape=face_shape, fmat=face_or_skull)
jaw = kit.part("jaw")
jaw.blob(HC, tuple(HR), 24, 6, SKIN, "jaw", z1=SLIT, shape=jaw_shape, fmat=jaw_or_floor)

# The mouth's ring at the line, as the skull's last ring and the jaw's first stand there, round the head (24 round).
N_RING = 24
RING = []
for jj in range(N_RING):
    a = 2 * math.pi * jj / N_RING
    rr = math.sin(math.acos(SLIT))
    p = HC + Vector((math.sin(a) * rr * HR.x, math.cos(a) * rr * HR.y, SLIT * HR.z))
    RING.append(face_shape(0, 0, 0, 0, p))
# Round the sides and the back, from one corner of the mouth to the other: the cheeks. (a = 0 is the front.)
CORNER = 3                          # the mouth runs 3 ring steps either side of the front: it's small, shut
CHEEK = [RING[(CORNER + k) % N_RING] for k in range(N_RING - 2 * CORNER + 1)]


def strip(part, rows, mat, weights, away, opened):
    """Quads between successive rows of points (each the same count), each vertex weighted by `weights[row]`. The rows
    lie on each other in the bind pose (the sheet folded flat while the jaw's shut), so each face is turned by where it
    is once it's open (`opened(row, point)`): to face away from `away(centre)`, a point inside the solid for a skin,
    outside for a lining. UVs from the open sheet too, so the skin's texture stretches with it."""
    idx = [[part.add_v(p, weights[r]) for p in row] for r, row in enumerate(rows)]
    open_ = [[opened(r, p) for p in row] for r, row in enumerate(rows)]
    for r in range(len(rows) - 1):
        for k in range(len(rows[r]) - 1):
            q = [idx[r][k], idx[r][k + 1], idx[r + 1][k + 1], idx[r + 1][k]]
            pts = [open_[r][k], open_[r][k + 1], open_[r + 1][k + 1], open_[r + 1][k]]
            c = sum(pts, Vector()) / 4
            n = (pts[1] - pts[0]).cross(pts[3] - pts[0]).normalized()
            if n.dot(c - away(c)) < 0:
                q, pts = list(reversed(q)), list(reversed(pts))
                n = -n
            part.face(q, [part.axis_uv(pt, n, mat) for pt in pts], mat, True)


# The cheeks: five rows from the skull's rim to the jaw's, their weights going over from the head to the jaw, so they
# stretch into a sheet when it comes away (and fold flat to nothing while it's shut).
ROWS = 7
axis = Vector((HC.x, HC.y, HC.z + SLIT * HR.z))


def pinched(r, p, k=1.0):
    """The cheeks' skin is drawn in towards the middle of its length (a stretched membrane's waist): its middle rows lie
    folded inside the head while it's shut, and stretch out narrower than the rims when it's open."""
    return axis + (p - axis) * k * (1 - 0.28 * math.sin(math.pi * r / (ROWS - 1)))


rows = [[pinched(r, p) for p in CHEEK] for r in range(ROWS)]
weights = [{"head": 1 - r / (ROWS - 1), "jaw": r / (ROWS - 1)} for r in range(ROWS)]
weights = [{k: v for k, v in w.items() if v > 1e-4} for w in weights]


def opened(r, p):
    return p - Vector((0, 0, 0.1 * r))


strip(jaw, rows, FACE, weights, lambda c: Vector((axis.x, axis.y, c.z)), opened)                 # the skin, outside
inner = [[pinched(r, p, 0.93) for p in CHEEK] for r in range(ROWS)]
strip(jaw, inner, MOUTH, weights, lambda c: c + (c - Vector((axis.x, axis.y, c.z))) * 4, opened)  # the gape's lining, inside
# The gullet: a black throat behind the gape, from the back of the mouth down into the neck.
throat = [axis + Vector((0, -0.04, 0.0)), axis + Vector((0, -0.05, -0.05)), axis + Vector((0, -0.06, -0.11))]
jaw.tube(throat, [(0.045, 0.035), (0.04, 0.03), (0.03, 0.025)], 8, MOUTH,
         lambda p: {"head": 1 - smooth01(axis.z + 0.01, axis.z - 0.09, p.z), "jaw": smooth01(axis.z + 0.01, axis.z - 0.09, p.z)},
         ref=(0, 1, 0), cap1="point")


def tooth_at(part, base, tip, bone, w=0.0034):
    part.tube([base, base.lerp(tip, 0.55), tip], [w, w * 0.75, w * 0.18], 4, TEETH, bone, ref=(0, 1, 0), cap1="point")


# The teeth: needles round both rims of the gape, the front ones longest, hooked back; inside the head while it's shut.
for k in range(N_RING - 2 * CORNER + 1):
    a = 2 * math.pi * (CORNER + k) / N_RING
    if math.cos(a) > 0.55:
        continue                    # none at the mouth itself: it shuts on lips, not teeth
    rim = RING[(CORNER + k) % N_RING]
    inward = (axis - rim).normalized()
    ln = 0.026 * (0.6 + 0.4 * max(0.0, math.cos(a) + 0.4))
    gum_up = rim + inward * 0.009 + Vector((0, 0, 0.004))
    tooth_at(head, gum_up, gum_up + Vector((0, 0, -ln)) + inward * 0.006, "head")
    gum_dn = rim + inward * 0.009 - Vector((0, 0, 0.004))
    tooth_at(jaw, gum_dn, gum_dn + Vector((0, 0, ln * 0.85)) + inward * 0.006, "jaw")
# The gums under them, a ridge round each rim.
gum_ring = [RING[(CORNER + k) % N_RING] + (axis - RING[(CORNER + k) % N_RING]).normalized() * 0.008 for k in range(N_RING - 2 * CORNER + 1)]
head.tube([p + Vector((0, 0, 0.003)) for p in gum_ring], [(0.006, 0.004)] * len(gum_ring), 5, GUM, "head", ref=(0, 0, 1))
jaw.tube([p - Vector((0, 0, 0.003)) for p in gum_ring], [(0.006, 0.004)] * len(gum_ring), 5, GUM, "jaw", ref=(0, 0, 1))

# The eyes: big, black, set deep and wide, wet; a lid's rim round each.
for eu in (-EYE_U, EYE_U):
    c = on_head(eu, EYE_W, inset=0.026)
    head.blob(c, (EYE_R * 1.1, EYE_R * 0.9, EYE_R * 0.86), 12, 8, EYE, "head")

NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, -0.005, TOP + 0.08)), Vector((0, 0.0, (TOP + NECK_TOP) / 2 + 0.04)),
        Vector((0, 0.005, NECK_TOP + 0.02))]
head.tube(NECK, [(0.05, 0.044), (0.034, 0.032), (0.031, 0.03), (0.034, 0.032)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * bell((abs(math.sin(a)) - 0.75) / 0.12))


# ----------------------------------------------------------------------------------------------------------------
# The body: narrow, the chest shallow and the shoulders sloped; the collarbones and the breastbone under the skin, the
# belly flat, the spine's knuckles down the back, smooth everywhere else as wax.
body = kit.part("body")
SPINE = (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0)
TORSO = [(HIP - 0.06, 0.13, 0.085), (HIP + 0.04, 0.125, 0.08), (WAIST, 0.095, 0.07), (WAIST + 0.1, 0.105, 0.075),
         (H("spine_02").z + 0.06, 0.125, 0.085), (CHEST + 0.04, 0.135, 0.088), (TOP - 0.07, 0.14, 0.082),
         (TOP - 0.03, 0.12, 0.07), (TOP, 0.075, 0.055), (TOP + 0.03, 0.05, 0.045)]


def torso_shape(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    front, back = max(0.0, math.cos(a)), max(0.0, -math.cos(a))
    d = 0.0
    if CHEST - 0.06 < p.z < TOP - 0.04:
        d += 0.004 * front * max(0.0, math.sin((p.z - WAIST) * 48)) ** 2 * (0.3 + 0.7 * abs(math.sin(a)))   # ribs, faint
    d -= 0.005 * front * bell(math.sin(a) / 0.1) * smooth01(CHEST - 0.1, CHEST, p.z)                       # the breastbone's groove
    d += 0.007 * back * bell(math.sin(a) / 0.16) * max(0.0, math.sin(p.z * 60)) * smooth01(HIP, WAIST, p.z)  # the spine's knuckles
    return p + out * d


def resampled(secs, step):
    out = []
    for (z0, x0, y0), (z1, x1, y1) in zip(secs, secs[1:]):
        n = max(1, round((z1 - z0) / step))
        out += [(z0 + (z1 - z0) * k / n, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    return out + [secs[-1]]


RINGS = resampled(TORSO, 0.035)
body.tube([Vector((0, 0, z)) for z, _, _ in RINGS], [(rx, ry) for _, rx, ry in RINGS], 14, SKIN, SPINE, ref=(0, 1, 0), square=0.92,
          shape=torso_shape, cap0=True)
for sx, s in ((-1, "l"), (1, "r")):
    body.tube([Vector((sx * 0.02, 0.055, TOP - 0.025)), Vector((sx * 0.09, 0.045, TOP - 0.01)), Vector((sx * (SHOULDER.x - 0.01), 0.01, SHOULDER.z + 0.006))],
              [0.01, 0.009, 0.012], 6, SKIN, [f"clavicle_{s}"], ref=(0, 0, 1))
    body.blob(Vector((sx * SHOULDER.x, 0.0, SHOULDER.z)), (0.03, 0.032, 0.028), 8, 5, SKIN, [f"upperarm_{s}"])
    body.blob(Vector((sx * 0.095, 0.02, HIP + 0.05)), (0.028, 0.032, 0.03), 8, 4, SKIN, "pelvis")


# ----------------------------------------------------------------------------------------------------------------
# The limbs: the arms like the stems of something grown in a cellar, the elbows knobs, the hands long and narrow and
# the fingers longer, the nails grey; the legs long, the knees knotted, the feet long and flat and bare.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.04):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    limbs.tube([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.45), e, e.lerp(wr, 0.4), wr],
               [0.03, 0.022, (0.025, 0.027), (0.02, 0.018), (0.016, 0.014)], 8, SKIN, jointed(sh, e, wr, ua, la), ref=(0, 0, 1))
    limbs.blob(e, (0.022, 0.024, 0.022), 6, 4, SKIN, la)
    k0 = T(hd)
    limbs.tube([wr, wr.lerp(k0, 0.55), k0], [(0.026, 0.011), (0.036, 0.012), (0.04, 0.011)], 8, SKIN, hd, ref=(0, 0, 1))
    for f in range(4):
        spread = (f - 1.5) * 0.02
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.0, 0)).normalized()
        ln = 0.21 * (0.82 + 0.18 * (1 - abs(f - 1.5) / 1.5)) * (0.86 if f == 0 else 1.0)
        pts = [base, base + along * ln * 0.38, base + along * ln * 0.7, base + along * ln]
        w = lambda p, base=base, ln=ln, s=s: {f"hand_{s}": 1.0} if (p - base).length < ln * 0.08 else {f"fingers_{s}": 1.0}
        limbs.tube(pts, [0.0085, 0.0078, 0.0068, 0.0045], 5, SKIN, w, ref=(0, 0, 1), cap1="point",
                   shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.0022 if i in (1, 2) else 0.0))
        limbs.tube([pts[-1] - along * 0.02, pts[-1] + along * 0.003], [(0.006, 0.0025), (0.004, 0.002)], 4, NAIL, f"fingers_{s}", ref=(0, 0, 1))
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.35], [0.011, 0.0095, 0.006], 5, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")

for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.04)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.45), an + Vector((0, 0, 0.03))],
               [0.055, 0.04, (0.036, 0.038), (0.03, 0.032), 0.02], 9, SKIN, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0))
    limbs.blob(kn + Vector((0, 0.012, 0)), (0.03, 0.03, 0.034), 7, 4, SKIN, {f"thigh_{s}": 0.4, f"calf_{s}": 0.6})
    limbs.blob(an, (0.024, 0.026, 0.024), 7, 4, SKIN, f"foot_{s}")
    # The foot long and flat, the heel narrow; four long toes, a little apart.
    limbs.tube([an - Vector((0, 0.04, 0.03)), an.lerp(bl, 0.5) - Vector((0, 0, 0.015)), bl], [(0.022, 0.016), (0.034, 0.014), (0.04, 0.012)], 8, SKIN,
               f"foot_{s}", ref=(0, 0, 1), cap0=True)
    for t in range(4):
        spread = (t - 1.5) * 0.019
        b0 = bl + Vector((spread, 0, -0.004))
        dirn = Vector((spread * 1.6, 1, -0.08)).normalized()
        tl = 0.075 if t in (1, 2) else 0.062
        limbs.tube([b0, b0 + dirn * tl * 0.55, b0 + dirn * tl], [0.0085, 0.0075, 0.005], 5, SKIN, f"ball_{s}", ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): arms lowered by Y, swung by X (+ forward); a spine tipped forward by
# -X and turned by Z (+ to its left); a thigh raised forward by +X, a knee bent by -X; the jaw dropped by -X and let down
# by its @loc (in the head's own axes). Its victim, when it has one, is in front of it (+Y).
BASE = mirror({"upperarm_r": (0, 76, 0), "lowerarm_r": (0, 4, 0), "fingers_r": (0, 10, 0), "thumb_r": (0, 6, -40),
               "thigh_r": (2, 0, 0), "calf_r": (-4, 0, 0), "foot_r": (2, 0, 0)})


def elbow_in(e):
    return 0.5 if abs(e.x) < 0.12 and e.y > -0.06 else 0.0


def arm_to(pose, side, wrist, curl=0.0, avoid=elbow_in):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=avoid)
    p[f"fingers_{side}"] = (0, (10 + 70 * curl) * k, 0)
    p[f"thumb_{side}"] = (0, 6 * k, -(40 + 30 * curl) * k)     # laid in along the palm
    return p


def chin(pose, forward=0.07, down=0.07):
    """Just under its chin, in front, wherever the head's gone."""
    base = rig.pose_points(sk, pose, [("head", "head")])[0]
    q = rig.world_rotation(sk, pose, "head")
    return base + q @ Vector((0, forward, 0.03)) - Vector((0, 0, down))


def with_jaw(pose, k):
    """`pose` with the jaw `k` of the way down (0 shut, 1 at its chest): let down on the cheeks' skin straight down from
    the head (and a little forward), whichever way the head's bowed, and swung open."""
    p = dict(pose)
    q = rig.world_rotation(sk, p, "head")
    p["jaw@loc"] = tuple(q.inverted() @ Vector((0, 0.1 * k, -0.46 * k)))
    p["jaw"] = (-38 * min(1.0, k * 1.6), 0, 0)
    return p


# Bashful: the shoulders drawn up, the back a little rounded, the head bowed and tipped over to its left; the hands held
# together under its chin, the fingertips at its lips.
SHY = over(BASE, spine_02=(-4, 0, 0), spine_03=(-6, 0, 0), neck=(-18, 0, 8), head=(-24, 22, -14),
           clavicle_r=(0, -14, 0), clavicle_l=(0, 14, 0), thigh_r=(2, 0, 8), thigh_l=(1, 0, -12), calf_r=(-8, 0, 0), foot_l=(2, 0, 14))


def hands_clasped(pose):
    """The long hands held together in front of it, low, the fingers laced: a child told to stand there and wait."""
    p = arm_to(pose, "r", Vector((0.035, 0.17, 0.98)), curl=0.55)
    p = arm_to(p, "l", Vector((-0.035, 0.19, 0.95)), curl=0.55)
    p["hand_r"], p["hand_l"] = (0, -10, 34), (0, 10, -34)
    return p


WAIT = hands_clasped(SHY)

# Wait (6 s, loop): bashful and dead still, but for one slow breath; then, all at once, the head tips further over, and
# holds; and back.
wait = Clip("wait")
wait.key(0, WAIT, "BEZIER")
wait.key(50, over(WAIT, spine_03=(-7.5, 0, 0), clavicle_r=(0, -14, 0), clavicle_l=(0, 14, 0)), "BEZIER")
wait.key(100, WAIT, "CONSTANT")
wait.key(120, over(WAIT, head=(-16, 26, -28)), "CONSTANT")
wait.key(160, WAIT, "CONSTANT")
wait.close(180)

# Stare (4 s, loop): it has someone. Upright, the head up and level, straight at them; the hands let down to hang past
# its knees. Dead still, but the fingers of one hand curling and uncurling, slowly.
STARE = over(BASE, spine_03=(-2, 0, 0), neck=(-4, 0, 0), head=(2, 0, 0), upperarm_r=(2, 84, 0), upperarm_l=(2, -84, 0))
stare = Clip("stare")
stare.key(0, STARE, "CONSTANT")
for f, c in ((60, 0.3), (66, 0.55), (72, 0.3), (78, 0.0)):
    stare.key(f, STARE | {"fingers_r": (0, 10 + 70 * c, 0)}, "BEZIER")
stare.close(120)

# Walk (2.4 s, loop): drawing in, 0.84 m a step (0.7 m/s): long slow strides from the hip, the knees hardly bending, the
# feet set down flat; and the head held dead level on the long neck, turned against every sway of the body so it
# never leaves them.
walk = Clip("walk")
for f, k in ((0, 1.0), (18, 0.0), (36, -1.0), (54, 0.0)):
    sway = 3 * k
    p = over(STARE, pelvis=(0, sway, 4 * k), spine_02=(0, -sway * 0.6, -2 * k), spine_03=(-2, -sway * 0.4, -2 * k),
             neck=(-4, 0, 0), head=(2, -sway * 0.2, 0),
             thigh_r=(2 + 24 * k, 0, 0), thigh_l=(2 - 24 * k, 0, 0),
             calf_r=(-4 - (18 if f == 54 else 4), 0, 0), calf_l=(-4 - (18 if f == 18 else 4), 0, 0),
             foot_r=(2 - 10 * k, 0, 0), foot_l=(2 + 10 * k, 0, 0),
             upperarm_r=(2 - 8 * k, 84, 0), upperarm_l=(2 + 8 * k, -84, 0))
    walk.key(f, p, "BEZIER")
walk.close(72)

# Unhinge (10 s, once: CreatureArt plays it over the GRAB's window). Its victim's head is a metre in front of it at 1.7 m.
# It leans in over them with its hands coming up to either side of their head, then the jaw: a crack and a drop, a hold,
# a crack and a drop, quicker and quicker (the wet cracks of the telegraph), until it hangs at its chest and the gape is
# over them.
VICTIM = Vector((0, 1.0, 1.68))
unhinge = Clip("unhinge", loop=False)


def over_them(k):
    """Leant `k` of the way over its victim: the back bowed, the neck out, the head tipped down to them."""
    p = over(STARE, spine_01=(-3 * k, 0, 0), spine_02=(-6 * k, 0, 0), spine_03=(-8 * k, 0, 0), neck=(-10 * k, 0, 0), head=(2 - 12 * k, 0, 0),
             thigh_r=(2 + 6 * k, 0, 0), thigh_l=(2 + 6 * k, 0, 0), calf_r=(-4 - 10 * k, 0, 0), calf_l=(-4 - 10 * k, 0, 0))
    hands = 0.15 + 0.85 * min(1.0, k * 1.4)
    for side, sx in (("r", 1), ("l", -1)):
        rest = Vector((sx * 0.3, 0.15, 1.1))
        held = VICTIM + Vector((sx * 0.13, 0.0, -0.02))
        p = arm_to(p, side, rest.lerp(held, hands), curl=0.2 + 0.4 * hands)
    return p


STEPS = [(21, 0.06, 0.15), (63, 0.15, 0.38), (103, 0.27, 0.58), (138, 0.4, 0.72), (168, 0.54, 0.83), (192, 0.68, 0.9), (214, 0.8, 0.95),
         (232, 0.9, 0.98), (248, 1.0, 1.0)]
unhinge.key(0, with_jaw(over_them(0.0), 0.0), "LINEAR")
unhinge.key(18, with_jaw(over_them(0.1), 0.0), "CONSTANT")
for f, jk, lean in STEPS:
    # Each crack is a jerk of the head as the jaw lets go another notch; then still.
    p = with_jaw(over_them(lean), jk)
    x, y, z = p["head"]
    unhinge.key(f, with_jaw(over(p, head=(x + 7, y + 5 * (1 if f % 2 else -1), z)), jk), "CONSTANT")
    unhinge.key(f + 2, p, "CONSTANT")
unhinge.hold(300, "CONSTANT")

# Swallow (1.4 s, loop): PUNISH. Down over its victim, the knees giving to bring the gape down on them, the hands holding;
# gulping, the jaw working wider and the back heaving with each.
swallow = Clip("swallow")
for f, (k, g) in enumerate(((0.0, 0.0), (1.0, 0.6), (0.4, 1.0), (1.0, 0.3), (0.2, 0.8), (0.8, 0.0))):
    p = over_them(1.0)
    p = over(p, spine_02=(-10 - 4 * k, 0, 0), spine_03=(-14 - 6 * k, 0, 0), neck=(-16 - 6 * k, 0, 0),
             thigh_r=(18, 0, 0), thigh_l=(18, 0, 0), calf_r=(-32 - 6 * g, 0, 0), calf_l=(-32 - 6 * g, 0, 0), foot_r=(14, 0, 0), foot_l=(14, 0, 0))
    swallow.key(f * 7, with_jaw(p, 1.0 + 0.12 * g), "LINEAR")
swallow.close(42)

# Hit: struck, it jerks back from the blow, the head snapping over, and is still again too fast.
hit = Clip("hit", loop=False)
hit.key(0, STARE, "CONSTANT")
hit.key(2, over(STARE, spine_02=(8, 0, 10), spine_03=(10, 0, 8), neck=(10, 0, 0), head=(22, 20, 30)), "CONSTANT")
hit.key(9, over(STARE, spine_03=(-4, 0, -4), head=(-4, 0, -6)), "LINEAR")
hit.key(12, STARE, "CONSTANT")

for name, pose in (("unhinged", with_jaw(over_them(1.0), 1.0)),):
    pts = rig.pose_points(sk, pose, [("lowerarm_r", "tail"), ("lowerarm_l", "tail"), ("jaw", "head")])
    print("[dt] shy_thing", name, "wrists", [tuple(round(c, 2) for c in q) for q in pts[:2]], "jaw", tuple(round(c, 2) for c in pts[2]))
for name, pose in (("wait", WAIT), ("stare", STARE)):
    top = max(q.z for q in rig.pose_points(sk, pose, [("head", "tail"), ("head", "head")]))
    print("[dt] shy_thing", name, "stands", round(top, 2), "m")

kit.build()
rig.bake(sk, [wait, stare, walk, unhinge, swallow, hit], plant=rig.feet_planter(sk, lowest=0.02))
print("[dt] shy_thing", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "shy_thing.glb", kit)
