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


def split(p, scale=14.0):
    """Where the charred skin's split open on the fire under it: a network, 0..1."""
    return smooth01(0.9, 0.97, ridged(p, 811, scale))


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


def skull(i, j, a, th, p):
    """Burnt to the bone's shape: the sockets deep, the cheekbones standing, the nose gone to two holes, the lips burnt
    off so the jaw's edge stands round the teeth; lumps of blistered char."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(-0.2, 0.4, v)
    n = head_normal(p)
    off = 0.004 * noise3(p * 40, 812, 1.0)
    for eu in (-EYE_U, EYE_U):
        off -= 0.03 * front * bell(math.hypot((u - eu) / 0.28, (w - EYE_W) / 0.25))
        off += 0.01 * front * bell((u - eu * 1.4) / 0.2) * bell((w - EYE_W + 0.3) / 0.12)
    off -= 0.016 * front * bell(math.hypot(u / 0.16, (w + 0.08) / 0.12))
    off -= 0.022 * front * bell(math.hypot(u / 0.5, (w - MOUTH_W) / 0.16))
    off -= 0.012 * front * bell((abs(u) - 0.62) / 0.18) * bell((w + 0.25) / 0.2)
    return p + n * off


def skull_weights(p):
    u, v, w = head_units(p)
    k = smooth01(MOUTH_W + 0.04, MOUTH_W - 0.06, w) * smooth01(-0.6, -0.2, v)
    return {n: x for n, x in (("head", 1 - k), ("jaw", k)) if x > 1e-4}


head = kit.part("head")
head.blob(HC, tuple(HR), 22, 18, CHAR, skull_weights, shape=skull,
          fmat=lambda pts, n: burnt(pts, n, FACE if head_units(sum(pts, Vector()) / len(pts))[1] > 0.2 else CHAR))
for eu in (-EYE_U, EYE_U):
    v = math.sqrt(max(0.0, 1 - eu * eu - EYE_W * EYE_W))
    surf = HC + Vector((eu * HR.x, v * HR.y, EYE_W * HR.z))
    head.blob(surf - head_normal(surf) * 0.024, (0.006, 0.005, 0.006), 6, 4, EYE, "head")


def on_face(u, w, inset):
    v = math.sqrt(max(0.02, 1 - u * u - w * w))
    surf = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    return skull(0, 0, 0, 0, surf) - head_normal(surf) * inset


# The fire in its mouth, set back behind the teeth.
head.blob(on_face(0.0, MOUTH_W, 0.03), (0.03, 0.02, 0.022), 8, 5, CORE, {"head": 0.5, "jaw": 0.5})
for k in range(12):
    u = -0.55 + 1.1 * k / 11
    for dw, bone in ((0.07, "head"), (-0.07, "jaw")):
        c = on_face(u, MOUTH_W + dw, 0.012)
        tall = 0.012 * (0.8 + 0.4 * (0.5 + 0.5 * math.sin(k * 2.7 + dw * 30)))
        tip = c + Vector((0, 0.004, -tall if dw > 0 else tall))
        head.tube([c, tip], [(0.005, 0.0045), (0.003, 0.0025)], 4, TEETH, bone, ref=(0, 1, 0), cap1="point")
NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.03))]
head.tube(NECK, [(0.034, 0.032), (0.026, 0.026), (0.03, 0.03)], 9, CHAR, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * noise3(Vector(p) * 30, 813, 1.0),
          fmat=lambda pts, n: burnt(pts, n, CHAR))

# The body: a starved fireman's, burnt black, the ribs standing; the rags of his jacket burnt onto it.
body = kit.part("body")
TORSO = [(HIP - 0.04, 0.1, 0.07), (WAIST, 0.075, 0.055), (CHEST, 0.11, 0.08), (TOP - 0.03, 0.12, 0.075), (TOP + 0.02, 0.05, 0.04)]


def ribs(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    d = 0.008 * max(0.0, math.sin((p.z - WAIST) * 55)) ** 2 * smooth01(WAIST, CHEST, p.z) + 0.004 * noise3(p * 25, 814, 1.0)
    return p + out * d


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

# The limbs: thin as kindling; the fingers long and black, the nails gone.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.03):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    limbs.tube([sh, sh.lerp(e, 0.5), e, e.lerp(wr, 0.5), wr], [0.026, 0.02, 0.022, 0.018, 0.015], 8, CHAR, jointed(sh, e, wr, ua, la),
               ref=(0, 0, 1), fmat=lambda pts, n: burnt(pts, n, CHAR))
    k0 = T(hd)
    limbs.tube([wr, wr.lerp(k0, 0.6), k0], [(0.024, 0.01), (0.032, 0.011), (0.034, 0.01)], 8, CHAR, hd, ref=(0, 0, 1))
    for f in range(4):
        spread = (f - 1.5) * 0.017
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.4, 0)).normalized()
        n = 0.16 * (0.85 + 0.15 * (1 - abs(f - 1.5) / 1.5))
        pts = [base, base + along * n * 0.4, base + along * n * 0.75, base + along * n]
        limbs.tube(pts, [0.0075, 0.0068, 0.006, 0.003], 5, CHAR,
                   lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}, ref=(0, 0, 1),
                   cap1="point", shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.0025 if i in (1, 2) else 0.0))
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.8], [0.008, 0.007, 0.003], 5, CHAR, f"thumb_{s}", ref=(0, 0, 1), cap1="point")
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.02)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.5), an], [0.04, 0.03, 0.03, 0.024, 0.02], 8, CHAR,
               jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0), fmat=lambda pts, n: burnt(pts, n, CHAR))
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

kit.build()
rig.bake(sk, [peer, reach, hit, perch])
print("[dt] stoker", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "stoker.glb", kit)
