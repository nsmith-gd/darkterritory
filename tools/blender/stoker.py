"""THE STOKER (GDD v1.2 §21 the firebox, App. A.5 · heat): "Gets into the firebox when the fire burns low or the door is
left open. Pressure climbs, and so does speed. Open the firebox and club it to kill it, and get burned doing it."

What lives in the fire. It was a fireman, once, and it's still the shape of one, shrunk: burnt right through to
charcoal and alive in it, the skin black and split, and in every split the fire showing, the wrong colour (the sick
green GreyboxScene gives a fire with a Stoker in it). No hair, no ears, no lips: the teeth are bare in a grin the heat
drew back, and the mouth behind them is full of fire. The eyes are two pale points. The fingers are long, thin and
black as burnt twigs, and they're what you see first: curled over the lip of the firebox door, from the inside.

Only ever seen at the firebox door (CreatureArt draws it there when the door's open): its head pushed out between the
door's leaves into the cab, its fingers over the lip. Its origin is the door's centre, on the backhead's face; it faces out of the door (+Y, the engine's -Z before CreatureArt turns it to the cab).
SK_Human shrunk, with a jaw. Clips (§31: still, then too fast): peer (its face up close behind the door, the fingers
over the lip, watching, the jaw opening slowly on the fire in its mouth), reach (an arm out over the lip, groping for
whoever's there, quick and slow), hit (struck: back into the fire, and up again).

    tools/models/build.sh stoker        # this, its high copy and the bake -> content/art/models/stoker.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
_s = rig.human(height=1.5, leg=1.0, arm=1.5, torso=0.9, neck=1.3, head=1.0, width=0.8, fingers=True, sockets=False)
NT = {b.name: b for b in _s.bones}["head"].head.z
sk = rig.human(height=1.5, leg=1.0, arm=1.5, torso=0.9, neck=1.3, head=1.0, width=0.8, fingers=True, sockets=False,
               extra=[Bone("jaw", "head", (0, 0.015, NT + 0.05), (0, 0.1, NT + 0.02))])
sk.build()
kit = rig.Kit(sk, "stoker")


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
HC = Vector((0.0, 0.015, NECK_TOP + 0.095))
HR = Vector((0.07, 0.085, 0.095))
EYE_U, EYE_W = 0.38, 0.16
MOUTH_W = -0.42

CHAR = Mat("tar.stoker", hexc("#151210"), shine=0.15)
FACE = Mat("tar.stoker_face", hexc("#1b1714"), shine=0.2)
RAGS = Mat("wool.stoker_rags", hexc("#1a1612"), shine=0.04)
EMBER = Mat("ember_crack.stoker", hexc("#9ad86a"), shine=0.1, glow=0.9)
CORE = Mat("ember_core.stoker", hexc("#b8f080"), emissive=1.0)
EYE = Mat("eye.stoker", hexc("#e4f8c8"), emissive=1.0)
TEETH = Mat("tar.stoker_teeth", hexc("#5a4a32"), shine=0.4)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def ridged(p, seed, scale):
    return 1 - abs(noise3(p * scale, seed, 1.0))


# The char (note 536; the art checklist's audit, 5 Oct: "the arm ... a smooth featureless tube, and the head is smooth as a
# helmet close to; neither reads as char at the door"). Burnt wood's alligatoring: the skin's split into blocks a few
# centimetres across, each domed and dry, with the fire showing in the cracks between them. The same cells, at the same
# scale and seed, are cut sharp into the high copy (tools/models/recipes/stoker.py), so the bake's normals land on the
# game mesh's own blocks.
CELL = 30.0          # cells per metre: a block 3-4 cm across
CELL_SEED = 836


def _h(ix, iy, iz, k):
    x = math.sin(ix * 127.1 + iy * 311.7 + iz * 74.7 + CELL_SEED * 13.13 + k * 91.3) * 43758.5453
    return x - math.floor(x)


def cells(p, scale=CELL):
    """The two nearest of a jittered grid's points to `p` (in cells): (F1, F2). F2 - F1 is 0 on a block's edge."""
    q = (p.x * scale, p.y * scale, p.z * scale)
    b = [math.floor(c) for c in q]
    f1 = f2 = 9.0
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                cx, cy, cz = b[0] + dx, b[1] + dy, b[2] + dz
                d = math.dist(q, (cx + _h(cx, cy, cz, 0), cy + _h(cx, cy, cz, 1), cz + _h(cx, cy, cz, 2)))
                if d < f1:
                    f1, f2 = d, f1
                elif d < f2:
                    f2 = d
    return f1, f2


def edge(p, scale=CELL):
    f1, f2 = cells(p, scale)
    return f2 - f1


def char_off(p, depth=0.0035, dome=0.0022, scale=CELL):
    """Metres out along the surface: each block domed, sunk at its edges into the crack."""
    e = edge(p, scale)
    return dome * smooth01(0.08, 0.55, e) - depth * (1 - smooth01(0.0, 0.16, e)) + 0.0012 * noise3(p * 60, 837, 1.0)


def split(p, scale=CELL):
    """Where the char's split open on the fire: a face whose middle is on a block's edge, most of them (a few cracks are
    dead black), 0..1."""
    return (1 - smooth01(0.06, 0.13, edge(p, scale))) * smooth01(-0.45, -0.2, noise3(p * 9, 838, 1.0))


def burnt(points, normal, base):
    """The face's material: the fire showing where the skin's split, else the char."""
    c = sum(points, Vector()) / len(points)
    return EMBER if split(c) > 0.5 else base


def head_units(p):
    d = p - HC
    return d.x / HR.x, d.y / HR.y, d.z / HR.z


def head_normal(p):
    d = p - HC
    return Vector((d.x / HR.x ** 2, d.y / HR.y ** 2, d.z / HR.z ** 2)).normalized()


def fissure(u, v, w):
    """The skull split along its crown, front to back a little off the middle: 0..1 on the split (where it's above the
    brow and not down the back of the neck)."""
    line = u - 0.16 - 0.09 * math.sin(v * 3.1 + 0.6)
    return bell(line / 0.07) * smooth01(0.15, 0.45, w) * smooth01(-0.85, -0.55, v) * smooth01(0.85, 0.6, v)


def skull(i, j, a, th, p):
    """Burnt to the bone's shape: the sockets deep under a brow, the cheekbones standing over hollow cheeks, the temples
    sunk, the nose gone to a hole, the lips burnt off so the jaw's edge stands round the teeth, the crown split on the fire;
    and over it all the char's blocks."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(-0.2, 0.4, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.042 * front * bell(math.hypot((u - eu) / 0.27, (w - EYE_W) / 0.24))
        # The brow over each socket, and the cheekbone standing under and out from it.
        off += 0.02 * front * bell((u - eu) / 0.34) * bell((w - EYE_W - 0.28) / 0.1)
        off += 0.013 * front * bell((u - eu * 1.45) / 0.2) * bell((w - EYE_W + 0.3) / 0.12)
        # The cheek hollowed under it.
        off -= 0.014 * front * bell((u - eu * 1.2) / 0.2) * bell((w + 0.3) / 0.16)
    off -= 0.02 * front * bell(math.hypot(u / 0.15, (w + 0.08) / 0.13))
    off -= 0.024 * front * bell(math.hypot(u / 0.5, (w - MOUTH_W) / 0.16))
    # The jaw's edge, and the temples sunk either side of the skull.
    off += 0.008 * front * bell((abs(u) - 0.55) / 0.12) * bell((w + 0.62) / 0.12)
    off -= 0.012 * bell((abs(u) - 0.93) / 0.12) * bell((w - 0.15) / 0.25) * smooth01(-0.5, 0.0, v)
    off -= 0.012 * fissure(u, v, w)
    # The skull's own shape under it: the cranium broad over the temples, the jaw narrow under the cheekbones.
    off += 0.012 * smooth01(0.1, 0.6, w) * smooth01(-0.2, 0.3, abs(u)) - 0.016 * smooth01(-0.2, -0.75, w) * abs(u) ** 2
    return p + n * (off + char_off(p, depth=0.0045, dome=0.0025))


def skull_weights(p):
    u, v, w = head_units(p)
    k = smooth01(MOUTH_W + 0.04, MOUTH_W - 0.06, w) * smooth01(-0.6, -0.2, v)
    return {n: x for n, x in (("head", 1 - k), ("jaw", k)) if x > 1e-4}


def head_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    u, v, w = head_units(c)
    if fissure(u, v, w) > 0.45:
        return EMBER
    return burnt(pts, n, FACE if v > 0.2 else CHAR)


head = kit.part("head")
head.blob(HC, tuple(HR), 30, 24, CHAR, skull_weights, shape=skull, fmat=head_mat)
for eu in (-EYE_U, EYE_U):
    v = math.sqrt(max(0.0, 1 - eu * eu - EYE_W * EYE_W))
    surf = HC + Vector((eu * HR.x, v * HR.y, EYE_W * HR.z))
    head.blob(surf - head_normal(surf) * 0.03, (0.006, 0.005, 0.006), 6, 4, EYE, "head")


def on_face(u, w, inset):
    v = math.sqrt(max(0.02, 1 - u * u - w * w))
    surf = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    return skull(0, 0, 0, 0, surf) - head_normal(surf) * inset


# The fire in its mouth, set back behind the teeth.
head.blob(on_face(0.0, MOUTH_W, 0.026), (0.036, 0.02, 0.026), 8, 5, CORE, {"head": 0.5, "jaw": 0.5})
for k in range(12):
    u = -0.55 + 1.1 * k / 11
    for dw, bone in ((0.07, "head"), (-0.07, "jaw")):
        c = on_face(u, MOUTH_W + dw, 0.012)
        tall = 0.012 * (0.8 + 0.4 * (0.5 + 0.5 * math.sin(k * 2.7 + dw * 30)))
        tip = c + Vector((0, 0.004, -tall if dw > 0 else tall))
        head.tube([c, tip], [(0.005, 0.0045), (0.003, 0.0025)], 4, TEETH, bone, ref=(0, 1, 0), cap1="point")


def out_of(a, fr):
    return fr[0] * math.sin(a) + fr[1] * math.cos(a)


def sinew(bumps, depth=0.005, dome=0.003, lumps=0.0035):
    """A tube's shape: `bumps` [(t, direction, metres, along, across)] raised along the tube's length t (0..1 by its ring
    index; a bone's knob, a tendon) where its surface faces `direction`, and the char's blocks over it."""
    def shape(i, j, a, p, fr):
        p = Vector(p)
        o = out_of(a, fr)
        t = i / shape.last
        off = 0.0
        for t0, d, amt, wt, wa in bumps:
            facing = max(0.0, o.dot(Vector(d).normalized()))
            off += amt * bell((t - t0) / wt) * facing ** wa
        # Burnt unevenly: the flesh shrunk to the bone here, a lump of it left there.
        off += lumps * noise3(p * 22, 840, 1.0)
        return p + o * (off + char_off(p, depth, dome))
    shape.last = 1
    return shape


def tube(part, pts, radii, sides, mat, bones, ref, bumps=(), **kw):
    s = sinew(bumps, **{k: kw.pop(k) for k in ("depth", "dome", "lumps") if k in kw})
    s.last = len(pts) - 1
    return part.tube(pts, radii, sides, mat, bones, ref=ref, shape=s, fmat=kw.pop("fmat", lambda q, n: burnt(q, n, mat)), **kw)


def along(points, per):
    """`points` with `per` more evenly between each pair (the rings the char's blocks need), and the radii to match."""
    out = []
    for a, b in zip(points, points[1:]):
        out += [a.lerp(b, k / (per + 1)) for k in range(per + 1)]
    return out + [points[-1]]


def radii_along(radii, per):
    rr = [r if isinstance(r, tuple) else (r, r) for r in radii]
    out = []
    for a, b in zip(rr, rr[1:]):
        out += [(a[0] + (b[0] - a[0]) * k / (per + 1), a[1] + (b[1] - a[1]) * k / (per + 1)) for k in range(per + 1)]
    return out + [rr[-1]]


NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.03))]
# The neck: the two cords standing either side of the throat, the windpipe between them.
tube(head, along(NECK, 2), radii_along([(0.034, 0.032), (0.026, 0.026), (0.03, 0.03)], 2), 12, CHAR, ["spine_03", "neck", "head"], (0, 1, 0),
     bumps=[(0.5, (0.6, 0.8, 0), 0.006, 0.4, 4), (0.5, (-0.6, 0.8, 0), 0.006, 0.4, 4), (0.45, (0, 1, 0), 0.004, 0.25, 8)])

# The body: a starved fireman's, burnt black, the ribs standing; the rags of his jacket burnt onto it.
body = kit.part("body")
TORSO = [(HIP - 0.04, 0.1, 0.07), (WAIST, 0.075, 0.055), (CHEST, 0.11, 0.08), (TOP - 0.03, 0.12, 0.075), (TOP + 0.02, 0.05, 0.04)]


def ribs(i, j, a, p, fr):
    p = Vector(p)
    out = out_of(a, fr)
    d = 0.008 * max(0.0, math.sin((p.z - WAIST) * 55)) ** 2 * smooth01(WAIST, CHEST, p.z)
    return p + out * (d + char_off(p))


rings = []
for (z0, x0, y0), (z1, x1, y1) in zip(TORSO, TORSO[1:]):
    for k in range(4):
        f = k / 4
        rings.append((z0 + (z1 - z0) * f, x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
rings.append(TORSO[-1])


def torso_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    if noise3(c * 6, 815, 1.0) > 0.25 and c.z < CHEST:
        return RAGS
    return burnt(pts, n, CHAR)


body.tube([Vector((0, 0, z)) for z, _, _ in rings], [(rx, ry) for _, rx, ry in rings], 12, CHAR, (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0),
          ref=(0, 1, 0), shape=ribs, fmat=torso_mat, cap1=True)

# The limbs: thin as kindling, the bones' knobs standing at the joints and the tendons down the forearm; the fingers long
# and black, knuckled like burnt twigs, the nails gone.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.03):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


# (The T-pose's arm runs out along x, the back of the hand up (+z) and the elbow's point behind (−y).)
for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    pts = [sh, sh.lerp(e, 0.5), e, e.lerp(wr, 0.5), wr]
    # Upper arm wasted to the bone; the elbow's point behind; down the forearm the two bones' ridge on its back and the
    # tendons on its front, flat as a slat; the wrist's knob.
    tube(limbs, along(pts, 4), radii_along([0.022, 0.014, (0.019, 0.016), (0.018, 0.012), (0.016, 0.01)], 4), 14, CHAR,
         jointed(sh, e, wr, ua, la), (0, 0, 1),
         bumps=[(0.5, (0, -1, 0), 0.016, 0.05, 2), (0.5, (0, 0, -1), 0.004, 0.06, 2), (0.08, (0, 0, 1), 0.006, 0.08, 2),
                (0.72, (0, 0, 1), 0.004, 0.14, 6), (0.72, (0, -0.4, -1), 0.003, 0.12, 6), (0.96, (0, -1, 0.3), 0.006, 0.04, 3),
                (0.25, (0, 0, 1), 0.003, 0.12, 4)])
    k0 = T(hd)
    # The back of the hand: the tendons out to each knuckle, the knuckles standing.
    tube(limbs, along([wr, wr.lerp(k0, 0.6), k0], 2), radii_along([(0.022, 0.009), (0.03, 0.01), (0.033, 0.011)], 2), 12, CHAR, hd, (0, 0, 1),
         bumps=[(1.0, (0, 0, 1), 0.006, 0.12, 6), (0.55, (0, 0, 1), 0.0025, 0.2, 10)], depth=0.0025, dome=0.0015, lumps=0.001)
    for f in range(4):
        spread = (f - 1.5) * 0.017
        base = k0 + Vector((0, spread, 0))
        along_ = Vector((sx, spread * 1.4, 0)).normalized()
        n = 0.16 * (0.85 + 0.15 * (1 - abs(f - 1.5) / 1.5))
        # Three bones, each swollen at its joint and wasted between: the knuckle, the two joints, and a burnt point.
        ts = [0.0, 0.18, 0.36, 0.45, 0.6, 0.72, 0.82, 0.92, 1.0]
        rs = [0.0078, 0.0058, 0.0072, 0.0062, 0.005, 0.0062, 0.0046, 0.0036, 0.0016]
        fpts = [base + along_ * n * t for t in ts]
        limbs.tube(fpts, rs, 6, CHAR,
                   lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}, ref=(0, 0, 1),
                   cap1="point", shape=lambda i, j, a, p, fr: Vector(p) + out_of(a, fr) * 0.0012 * noise3(Vector(p) * 90, 839, 1.0),
                   fmat=lambda q, nn: EMBER if split(sum(q, Vector()) / len(q), 70) > 0.6 else CHAR)
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.4), th0.lerp(th1, 0.75), th1 + (th1 - th0) * 0.4, th1 + (th1 - th0) * 0.8], [0.0085, 0.0065, 0.0075, 0.005, 0.002],
               6, CHAR, f"thumb_{s}", ref=(0, 0, 1), cap1="point")
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    # (In the fire behind it, all but never seen: the fewest rings.)
    tube(limbs, [hp + Vector((0, 0, 0.02)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.5), an], radii_along([0.04, 0.03, 0.03, 0.024, 0.02], 0), 8, CHAR,
         jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), (0, 1, 0), bumps=[(0.5, (0, 1, 0), 0.008, 0.06, 2), (0.75, (0, 1, 0), 0.003, 0.15, 6)])
    limbs.tube([an - Vector((0, 0.03, 0)), bl, T(f"ball_{s}")], [(0.024, 0.018), (0.03, 0.012), (0.012, 0.008)], 6, CHAR,
               lambda p, s=s, bl=bl: {f"ball_{s}": 1.0} if p.y > bl.y - 0.01 else {f"foot_{s}": 1.0}, ref=(0, 0, 1), cap0=True)

# ----------------------------------------------------------------------------------------------------------------
# Clips. Posed in the firebox: the origin is the door's centre on the backhead's face, and the door's 0.64 x 0.44 m.
# Its head's brought up just behind the opening (`root@loc` from where FK leaves it), the hands to the door's lower lip.
DOOR_HALF_W, DOOR_HALF_H = 0.32, 0.22
FACE_AT = Vector((0, 0.15, 0.0))       # the head's centre: out of the door into the cab, pushed between its leaves
LIP_Z = -DOOR_HALF_H                   # the lower lip of the door
FLOOR = -0.7                           # the cab's floor (the door's centre is 0.7 m over it: Art/TrainKit.FireDoor)


def at_door(pose, face=FACE_AT):
    """`pose` moved bodily so its head's centre is at `face`."""
    p = dict(pose)
    p.pop("root@loc", None)
    hc = rig.pose_points(sk, p, [("head", "head")])[0] + Vector((0, 0, HC.z - NECK_TOP))
    p["root@loc"] = tuple(face - hc)
    return p


def arm_to(pose, side, wrist, curl):
    """The arm's wrist to `wrist` (rig.reach), from whichever of a spread of starting angles gets it nearest, the elbow
    kept down by the shoulder (crouched over, rig.reach's descent from one start sticks with the arm thrown back)."""
    k = 1 if side == "r" else -1
    ua, la = f"upperarm_{side}", f"lowerarm_{side}"
    sh = rig.pose_points(sk, pose, [(ua, "head")])[0]
    best = None
    for x in (-60, 0, 60):
        for y in (0, 45, 90):
            for z in (-40, 0, 40):
                start = dict(pose) | {ua: (x, y * k, z * k), la: (0, 0, 0)}
                p = rig.reach(sk, start, ua, la, wrist, elbow_axis=2, bend=k, avoid=lambda e: 2.0 * max(0.0, e.z - sh.z - 0.05))
                tip, elbow = rig.pose_points(sk, p, [(la, "tail"), (la, "head")])
                cost = (tip - wrist).length + 2.0 * max(0.0, elbow.z - sh.z - 0.05)
                if best is None or cost < best[0]:
                    best = (cost, p)
    p = best[1]
    p[f"fingers_{side}"] = (0, (20 + 60 * curl) * k, 0)
    return p


# Crouched in the coals, the body laid back and down into the firebox behind its head, the knees drawn up under it.
CROUCH = mirror({"pelvis": (-50, 0, 0), "spine_01": (-10, 0, 0), "spine_02": (-8, 0, 0), "spine_03": (-6, 0, 0), "neck": (-10, 0, 0),
                 "head": (66, 0, 0), "thigh_r": (110, 0, 10), "calf_r": (-140, 0, 0), "upperarm_r": (20, 50, 0)})


def peer_pose(tilt=0.0, jaw=0.0, grip=0.0):
    # (The head tipped right back on the neck to look up at whoever's stood over the door.)
    p = at_door(over(CROUCH, head=(96, tilt, 0), jaw=(-jaw, 0, 0)))
    for side, sx in (("r", 1), ("l", -1)):
        # Out over the lip and down to the cab's floor, the hands flat on the boards in front of the door, splayed: it's
        # coming out of the fire on them.
        p = arm_to(p, side, Vector((sx * 0.24, 0.26, FLOOR + 0.05)), 0.0)
        # The hand laid flat, the fingers forward along the boards (the T-pose's hand points out along x).
        p[f"hand_{side}"] = rig.hang(sk, p, f"hand_{side}", 0, 0, sx * 90)
        p[f"fingers_{side}"] = (0, sx * (10 + 40 * grip), 0)
    return p


# Peer (5 s, loop): its head out of the door, watching, its arms out over the lip and its hands on the cab's floor; the
# head tips over, slowly, and back all at once; the jaw comes open on the fire in its mouth, slowly, and shuts; the
# fingers draw in on the boards.
peer = Clip("peer")
peer.key(0, peer_pose(0, 0, 0), "BEZIER")
peer.key(60, peer_pose(28, 10, 0.3), "BEZIER")
peer.key(90, peer_pose(30, 24, 0.6), "CONSTANT")
peer.key(96, peer_pose(0, 4, 0.0), "CONSTANT")
peer.key(130, peer_pose(-6, 0, 0.2), "BEZIER")
peer.close(150)

# Reach (2 s, loop): its right arm up off the floor and out into the cab, groping for whoever's at the door, the fingers
# spread and closing; quick, then slow; the left still on the boards.
reach = Clip("reach")
for f, (out, side, close), interp in ((0, (0.15, 0.0, 0.0), "CONSTANT"), (8, (0.5, 0.1, 0.2), "LINEAR"), (20, (0.62, -0.05, 0.8), "BEZIER"),
                              (40, (0.55, 0.12, 0.3), "BEZIER"), (52, (0.3, 0.0, 0.9), "LINEAR")):
    p = peer_pose(10, 18, 0.5)
    p = arm_to(p, "r", Vector((0.14 + side, 0.1 + out, LIP_Z + 0.2 + 0.3 * out)), close)
    reach.key(f, p, interp)
reach.close(60)

hit = Clip("hit", loop=False)
hit.key(0, peer_pose(), "CONSTANT")
hit.key(2, at_door(over(CROUCH, head=(40, 20, 0), jaw=(-40, 0, 0)), FACE_AT + Vector((0, -0.28, -0.05))), "CONSTANT")
hit.key(14, at_door(over(CROUCH, head=(50, 10, 0), jaw=(-20, 0, 0)), FACE_AT + Vector((0, -0.22, -0.04))), "LINEAR")
hit.key(18, peer_pose(), "CONSTANT")

# Perch (4 s, loop; App. A.5 "it perches on the smokestack"): squatted on the chimney's rim while the fire burns low,
# hands gripping the rim between its feet, head hung down over the boiler toward the cab, turning slowly, then snapping
# back. Its origin here is the rim's centre (SceneArt sets it on the stack's top), the head above it.
PERCH = mirror({"pelvis": (-20, 0, 0), "spine_01": (-24, 0, 0), "spine_02": (-18, 0, 0), "spine_03": (-12, 0, 0),
                "neck": (24, 0, 0), "head": (30, 0, 0), "thigh_r": (120, 0, 14), "calf_r": (-150, 0, 0), "foot_r": (30, 0, 0)})


def perch_pose(turn=0.0, lean=0.0):
    p = at_door(over(PERCH, head=(30 + lean, turn, 0)), Vector((0, 0.18 + lean * 0.004, 0.62)))
    for side, sx in (("r", 1), ("l", -1)):
        p = arm_to(p, side, Vector((sx * 0.16, 0.1, 0.04)), 0.8)
    return p


perch = Clip("perch")
perch.key(0, perch_pose(0, 0), "BEZIER")
perch.key(50, perch_pose(-30, 10), "BEZIER")
perch.key(80, perch_pose(-34, 12), "CONSTANT")
perch.key(84, perch_pose(10, 0), "CONSTANT")
perch.close(120)

# Descend (3 s, once; App. A.5 "down the stack"): off the perch, up onto the rim, and down into the chimney feet first, the
# body sinking into it, the hands on the rim last, then gone (the stack's own black throat hides what's gone in; SceneArt
# plays it over the last seconds before the sim puts it in the firebox).
STOOD = mirror({"pelvis": (-6, 0, 0), "spine_01": (-8, 0, 0), "spine_02": (-6, 0, 0), "neck": (16, 0, 0), "head": (24, 0, 0),
                "thigh_r": (10, 0, 6), "calf_r": (-14, 0, 0), "foot_r": (8, 0, 0)})


def lowered(depth, hands=True, head=24.0):
    p = at_door(over(STOOD, head=(head, 0, 0)), Vector((0, 0.02, 1.2 - depth)))
    for side, sx in (("r", 1), ("l", -1)):
        # On the rim; let go, straight up over the head, going down into the throat last.
        p = arm_to(p, side, Vector((sx * 0.2, 0.06, 0.04)) if hands else Vector((sx * 0.08, 0.02, 1.95 - depth)), 0.9 if hands else 0.3)
    return p


descend = Clip("descend", loop=False)
descend.key(0, perch_pose(0, 0), "BEZIER")
descend.key(14, lowered(0.25, head=40), "BEZIER")
descend.key(40, lowered(0.9, head=30), "BEZIER")
descend.key(66, lowered(1.45, head=10), "BEZIER")
descend.key(78, lowered(1.9, hands=False, head=-10), "LINEAR")
descend.key(90, lowered(2.6, hands=False, head=-10), "LINEAR")

kit.build()
rig.bake(sk, [peer, reach, hit, perch, descend])
print("[dt] stoker", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "stoker.glb", kit)
