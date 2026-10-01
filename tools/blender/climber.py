"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4 · movement): "A pack runs alongside the train and mounts any coupling
gap held by fewer players than there are Climbers."

One of us, once: a crewman in the same coveralls and the same gas mask the crew wear (tools/blender/crew.py), gone
wrong all the way through. Drawn out long and thin, the coveralls burnt into its skin and torn open down the back and
the limbs, soot-black where it shows. The mask has grown into the face: the rubber is the skin now, the two round
glass eyes cracked and black with nothing behind them, the filter's canister a snout with the hose torn off and
hanging. Its hands are long and hooked for the gaps, its boots split open and the long toes out of them. It runs bent
double on all fours beside the train, as fast as the train, and it climbs like something that was never a man.

SK_Human (rig.human) drawn out. Faces +Y (the engine's -Z). Clips (§31: still, then too fast): run (beside the train on
all fours, a long loping gallop), scrabble (in a coupling gap, up between the cars: the hands and the feet going on
the buffers and the couplers, too fast), walk (along the roofs toward the engine, crouched low, the masked head
turning), crouch (inside an unlit car, folded in a corner, the head cocked, still), grab (on its victim: over them,
the hands on their head, the snout at their face), hit.

    tools/models/build.sh climber        # this, its high copy and the bake -> content/art/models/climber.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
sk = rig.human(height=1.95, leg=1.12, arm=1.42, torso=1.0, neck=1.3, head=1.0, width=0.85, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "climber")


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
HC = Vector((0.0, 0.012, NECK_TOP + 0.105))
HR = Vector((0.082, 0.098, 0.112))

SKIN = Mat("tar.climber", hexc("#17130f"), shine=0.35)
COVERALL = Mat("wool.climber_coverall", hexc("#2e2b26"), shine=0.06)
MASK = Mat("leather.climber_mask", hexc("#1c1a17"), shine=0.4)
GLASS = Mat("glass_dirty.climber_eye", hexc("#050505"), shine=0.95)
RIM = Mat("rust_heavy.climber_rim", hexc("#4a3a2c"), shine=0.5)
CANISTER = Mat("rust_heavy.climber_canister", hexc("#3b3226"), shine=0.45)
NAIL = Mat("tar.climber_nail", hexc("#0a0807"), shine=0.6)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def torn(points, normal, cloth):
    """The coveralls burnt into it and torn open: the skin showing in rents, worst down the back."""
    c = sum(points, Vector()) / len(points)
    back = max(0.0, -normal.y)
    return SKIN if noise3(c * 9, 61, 1.0) + 0.35 * back > 0.32 else cloth


def head_units(p):
    d = p - HC
    return d.x / HR.x, d.y / HR.y, d.z / HR.z


def head_normal(p):
    d = p - HC
    return Vector((d.x / HR.x ** 2, d.y / HR.y ** 2, d.z / HR.z ** 2)).normalized()


def skull(i, j, a, th, p):
    """The mask grown in: the face a smooth rubber plate, the eyepieces' rims standing, the cheeks pulled in under them to
    the snout; behind it the skull bald and lumped, the straps sunk into it."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(0.0, 0.4, v)
    n = head_normal(p)
    off = 0.003 * noise3(p * 30, 62, 1.0) * (1 - front)
    for eu in (-0.42, 0.42):
        off += 0.012 * front * bell(math.hypot((u - eu) / 0.32, (w - 0.18) / 0.3))
    off -= 0.014 * front * bell((abs(u) - 0.55) / 0.2) * bell((w + 0.35) / 0.25)
    off -= 0.006 * bell((w - 0.35) / 0.06) * (1 - front)          # the strap sunk into the back of the skull
    return p + n * off


def face_mat(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    return MASK if v > 0.0 or abs(w - 0.35) < 0.08 else SKIN


head = kit.part("head")
head.blob(HC, tuple(HR), 22, 18, SKIN, "head", shape=skull, fmat=face_mat)
for eu in (-0.42, 0.42):
    v = math.sqrt(max(0.0, 1 - eu * eu - 0.18 ** 2))
    surf = HC + Vector((eu * HR.x, v * HR.y, 0.18 * HR.z))
    nrm = head_normal(surf)
    c = surf + nrm * 0.004
    # The eyepiece: a brass-rusted rim, the glass black and cracked, set a little in.
    ring = [c + (nrm.cross(Vector((0, 0, 1))).normalized() * math.cos(t) + Vector((0, 0, 1)) * math.sin(t)) * 0.024 for t in
            (2 * math.pi * k / 12 for k in range(12))]
    head.tube(ring, [0.005] * 12, 5, RIM, "head", ref=(0, 1, 0), loop=True)
    head.blob(c - nrm * 0.004, (0.021, 0.008, 0.021), 10, 4, GLASS, "head")
# The snout: the filter canister grown out of the mask, ribbed, the hose's stump hanging off it.
SN = HC + Vector((0, HR.y * 0.95, -HR.z * 0.4))
head.tube([SN - Vector((0, 0.02, 0)), SN + Vector((0, 0.03, -0.01)), SN + Vector((0, 0.07, -0.025))], [0.032, 0.034, 0.03], 12, CANISTER, "head",
          ref=(0, 0, 1), cap1=True, shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.003 * (i == 1))
hose = [SN + Vector((0.01, 0.06, -0.04)), SN + Vector((0.03, 0.05, -0.12)), SN + Vector((0.035, 0.03, -0.2)), SN + Vector((0.025, 0.01, -0.25))]
head.tube(hose, [0.014, 0.013, 0.012, 0.011], 8, MASK, ["head", "head", "neck", "neck"], ref=(0, 1, 0), cap1=True,
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.0025 * max(0.0, math.sin(i * 9 + j)))
NECK = [Vector((0, -0.01, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.03))]
head.tube(NECK, [(0.042, 0.04), (0.034, 0.034), (0.038, 0.038)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.005 * bell((abs(math.sin(a)) - 0.7) / 0.15))

# The body: thin, the ribs and the spine standing through the coveralls where they're torn.
body = kit.part("body")
TORSO = [(HIP - 0.05, 0.13, 0.09), (WAIST, 0.105, 0.075), (CHEST, 0.14, 0.095), (TOP - 0.04, 0.15, 0.09), (TOP, 0.1, 0.07), (TOP + 0.03, 0.05, 0.045)]


def ribs(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    back = max(0.0, -math.cos(a))
    d = 0.006 * max(0.0, math.sin((p.z - WAIST) * 50)) ** 2 * smooth01(WAIST, CHEST, p.z)
    d += 0.014 * back * bell(math.sin(a) / 0.15) * max(0.0, math.sin(p.z * 65)) * smooth01(HIP, WAIST, p.z)
    return p + out * (d + 0.004 * noise3(p * 12, 63, 1.0))


rings = []
for (z0, x0, y0), (z1, x1, y1) in zip(TORSO, TORSO[1:]):
    for k in range(4):
        f = k / 4
        rings.append((z0 + (z1 - z0) * f, x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
rings.append(TORSO[-1])
body.tube([Vector((0, 0, z)) for z, _, _ in rings], [(rx, ry) for _, rx, ry in rings], 14, COVERALL, (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0),
          ref=(0, 1, 0), shape=ribs, fmat=lambda pts, n: torn(pts, n, COVERALL), cap1=True)

limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.035):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    limbs.tube([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.5), e, e.lerp(wr, 0.5), wr], [0.04, 0.032, (0.034, 0.036), 0.028, 0.022], 9, COVERALL,
               jointed(sh, e, wr, ua, la), ref=(0, 0, 1), fmat=lambda pts, n: torn(pts, n, COVERALL))
    k0 = T(hd)
    limbs.tube([wr, wr.lerp(k0, 0.6), k0], [(0.032, 0.012), (0.042, 0.013), (0.044, 0.012)], 8, SKIN, hd, ref=(0, 0, 1))
    for f in range(4):
        spread = (f - 1.5) * 0.022
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.2, 0)).normalized()
        n = 0.15 * (0.85 + 0.15 * (1 - abs(f - 1.5) / 1.5))
        pts = [base, base + along * n * 0.45, base + along * n * 0.8, base + along * n - Vector((0, 0, 0.025))]
        limbs.tube(pts, [0.009, 0.008, 0.007, 0.004], 5, SKIN,
                   lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}, ref=(0, 0, 1))
        limbs.tube([pts[-1], pts[-1] + along * 0.015 - Vector((0, 0, 0.022))], [(0.006, 0.003), (0.0015, 0.0015)], 4, NAIL, f"fingers_{s}",
                   ref=(0, 0, 1), cap1="point")
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.8], [0.01, 0.009, 0.004], 5, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.03)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.5), an + Vector((0, 0, 0.02))], [0.06, 0.048, (0.044, 0.046), 0.036, 0.03], 9,
               COVERALL, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0), fmat=lambda pts, n: torn(pts, n, COVERALL))
    # The boot split open, and the long toes out of it.
    limbs.tube([an - Vector((0, 0.05, 0)), an.lerp(bl, 0.5), bl], [(0.042, 0.035), (0.05, 0.03), (0.05, 0.026)], 8, MASK, f"foot_{s}", ref=(0, 0, 1),
               cap0=True)
    for t in range(4):
        spread = (t - 1.5) * 0.022
        b0 = bl + Vector((spread, 0, -0.005))
        d = Vector((spread * 1.5, 1, -0.15)).normalized()
        ln = 0.07 if t in (1, 2) else 0.055
        limbs.tube([b0, b0 + d * ln * 0.6, b0 + d * ln], [0.01, 0.008, 0.006], 5, SKIN, f"ball_{s}", ref=(0, 0, 1))
        limbs.tube([b0 + d * (ln - 0.006), b0 + d * (ln + 0.015) - Vector((0, 0, 0.012))], [(0.006, 0.003), (0.0015, 0.0015)], 4, NAIL, f"ball_{s}",
                   ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/tippy_toesie.py. Limbs put down by rig.reach where they have to land (the hands on the
# ground running, on the couplers scrabbling), from a spread of starts (it sticks from the T-pose bent over).
def reach_from(pose, upper, lower, target, axis, bend, avoid=None):
    best = None
    for x in (-60, 0, 60):
        for y in (0, 45, 90):
            start = dict(pose) | {upper: (x, y * bend if axis == 2 else 0, 0), lower: (0, 0, 0)}
            p = rig.reach(sk, start, upper, lower, target, elbow_axis=axis, bend=bend, avoid=avoid)
            tip = rig.pose_points(sk, p, [(lower, "tail")])[0]
            cost = (tip - target).length + (avoid(rig.pose_points(sk, p, [(lower, "head")])[0]) if avoid else 0)
            if best is None or cost < best[0]:
                best = (cost, p)
    return best[1]


def hands_to(pose, r, l, curl=0.7):
    p = reach_from(pose, "upperarm_r", "lowerarm_r", r, 2, 1)
    p = reach_from(p, "upperarm_l", "lowerarm_l", l, 2, -1)
    p["fingers_r"], p["fingers_l"] = (0, 10 + 70 * curl, 0), (0, -10 - 70 * curl, 0)
    return p


FEETP = [(b, e) for b in ("foot_l", "foot_r", "ball_l", "ball_r") for e in ("head", "tail")]


def ground(pose):
    return min(p.z for p in rig.pose_points(sk, pose, FEETP)) + 0.03


BASE = mirror({"upperarm_r": (0, 70, 0), "fingers_r": (0, 30, 0)})
# Bent double: the body laid forward over the legs, the masked head up to look ahead.
DOUBLE = over(BASE, pelvis=(-80, 0, 0), spine_01=(-4, 0, 0), spine_02=(-2, 0, 0), spine_03=(2, 0, 0), neck=(24, 0, 0), head=(52, 0, 0),
              thigh_r=(104, 0, 8), thigh_l=(104, 0, -8), calf_r=(-128, 0, 0), calf_l=(-128, 0, 0), foot_r=(28, 0, 0), foot_l=(28, 0, 0))

# Run (0.55 s, loop): a gallop on all fours, the hands thrown out ahead together and the feet coming up past them,
# the back flexing; the head held level and still through it, looking ahead.
run = Clip("run")
for f, k in ((0, 0.0), (4, 1.0), (8, 0.5), (12, -0.6)):
    p = over(DOUBLE, pelvis=(-80 + 8 * k, 0, 0), spine_02=(-2 - 10 * k, 0, 0), neck=(24 - 8 * k, 0, 0), head=(52 + 6 * k, 0, 0),
             thigh_r=(104 - 30 * k, 0, 8), thigh_l=(108 - 30 * k, 0, -8), calf_r=(-128 + 20 * k, 0, 0), calf_l=(-132 + 20 * k, 0, 0))
    g = ground(p)
    sh = rig.pose_points(sk, p, [("upperarm_r", "head")])[0]
    ahead = sh.y + 0.25 + 0.35 * k
    lift = 0.12 * max(0.0, -k)
    p = hands_to(p, Vector((0.16, ahead, g + lift)), Vector((-0.16, ahead + 0.05, g + lift)), curl=0.5)
    run.key(f, p, "LINEAR")
run.close(16)

# Scrabble (1.2 s, loop): up between the cars, facing in at the couplers: the hands going one over the other up the
# buffers, the feet kicking for a hold, all of it too fast; the head still.
UP = over(BASE, spine_01=(-12, 0, 0), spine_02=(-8, 0, 0), neck=(-6, 0, 0), head=(14, 0, 0), thigh_r=(60, 0, 0), calf_r=(-80, 0, 0),
          thigh_l=(30, 0, 0), calf_l=(-50, 0, 0))
scrabble = Clip("scrabble")
for f, k in enumerate((0, 1, 2, 3, 4, 5)):
    a = k % 2
    p = over(UP, thigh_r=(60 - 30 * a, 0, 0), calf_r=(-80 + 30 * a, 0, 0), thigh_l=(30 + 30 * a, 0, 0), calf_l=(-50 - 30 * a, 0, 0),
             spine_03=(0, 0, 8 * (1 if a else -1)))
    sh = rig.pose_points(sk, p, [("upperarm_r", "head")])[0]
    p = hands_to(p, Vector((0.2, sh.y + 0.35, sh.z + (0.35 if a else 0.05))), Vector((-0.2, sh.y + 0.35, sh.z + (0.05 if a else 0.35))), curl=0.9)
    scrabble.key(f * 6, p, "CONSTANT" if f % 2 else "LINEAR")
scrabble.close(36)

# Walk (2.4 s, loop): along a roof, crouched low on bent legs, the hands down and touching the roof as it goes; the
# masked head turning, too fast, to one side, holding, and back.
LOW = over(BASE, pelvis=(-58, 0, 0), spine_02=(-6, 0, 0), neck=(18, 0, 0), head=(38, 0, 0), thigh_r=(88, 0, 4), thigh_l=(88, 0, -4),
           calf_r=(-112, 0, 0), calf_l=(-112, 0, 0), foot_r=(24, 0, 0), foot_l=(24, 0, 0))
walk = Clip("walk")
for f, k in ((0, 1.0), (18, 0.0), (36, -1.0), (54, 0.0)):
    p = over(LOW, thigh_r=(88 + 18 * k, 0, 4), thigh_l=(88 - 18 * k, 0, -4), calf_r=(-112 - 16 * max(0.0, -k), 0, 0), calf_l=(-112 - 16 * max(0.0, k), 0, 0),
             head=(30, 0, 60 if f == 36 else 0))
    g = ground(p)
    sh = rig.pose_points(sk, p, [("upperarm_r", "head")])[0]
    p = hands_to(p, Vector((0.22, sh.y + 0.3 + 0.12 * k, g + 0.02)), Vector((-0.22, sh.y + 0.3 - 0.12 * k, g + 0.02)), curl=0.6)
    walk.key(f, p, "CONSTANT" if f == 36 else "BEZIER")
walk.close(72)

# Crouch (4 s, loop): folded in a corner of a dark car, on its heels, the arms round its knees, the head cocked, still;
# the hooked fingers drumming once on its shin.
CR = over(BASE, spine_01=(-30, 0, 0), spine_02=(-20, 0, 0), spine_03=(-10, 0, 0), neck=(-10, 0, 0), head=(30, 24, 0), thigh_r=(120, 0, 12),
          thigh_l=(120, 0, -12), calf_r=(-150, 0, 0), calf_l=(-150, 0, 0), foot_r=(32, 0, 0), foot_l=(32, 0, 0))
kr, kl = rig.pose_points(sk, CR, [("calf_r", "head"), ("calf_l", "head")])
CR = hands_to(CR, kr + Vector((-0.06, 0.12, -0.22)), kl + Vector((0.06, 0.12, -0.2)), curl=0.8)
crouch = Clip("crouch")
crouch.key(0, CR, "CONSTANT")
crouch.key(60, over(CR, head=(30, -10, 0)), "CONSTANT")
for f, c in ((84, 0.2), (87, 1.0), (90, 0.2), (93, 1.0), (96, 0.8)):
    crouch.key(f, CR | {"fingers_r": (0, 10 + 70 * c, 0)}, "CONSTANT")
crouch.close(120)

# Grab (1.4 s, loop): over its victim, crouched on them, both hooked hands on their head, the snout pushed into their
# face; jerking.
GR = over(BASE, pelvis=(-30, 0, 0), spine_02=(-12, 0, 0), spine_03=(-10, 0, 0), neck=(-20, 0, 0), head=(-10, 0, 0), thigh_r=(70, 0, 10),
          thigh_l=(70, 0, -10), calf_r=(-100, 0, 0), calf_l=(-100, 0, 0), foot_r=(20, 0, 0), foot_l=(20, 0, 0))
grab = Clip("grab")
for f, k in ((0, 0.0), (6, 1.0), (10, 0.2), (16, 1.0), (24, 0.0)):
    p = over(GR, spine_03=(-10 - 10 * k, 0, 6 * k), head=(-10 - 12 * k, 0, 0))
    hd = rig.pose_points(sk, p, [("head", "head")])[0]
    p = hands_to(p, hd + Vector((0.14, 0.35, -0.3)), hd + Vector((-0.14, 0.35, -0.28)), curl=0.9)
    grab.key(f, p, "LINEAR")
grab.close(42)

hit = Clip("hit", loop=False)
hit.key(0, LOW, "CONSTANT")
hit.key(2, over(LOW, spine_02=(10, 0, 16), neck=(20, 0, 0), head=(50, 0, 30)), "CONSTANT")
hit.key(10, over(LOW, spine_02=(-10, 0, -4)), "LINEAR")
hit.key(14, LOW, "CONSTANT")

kit.build()
rig.bake(sk, [run, scrabble, walk, crouch, grab, hit], plant=rig.feet_planter(sk, lowest=0.02, clips=["run", "walk", "crouch", "grab", "hit"]))
print("[dt] climber", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "climber.glb", kit)
