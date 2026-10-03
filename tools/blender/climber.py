"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4 · movement): "A pack runs alongside the train and mounts any coupling
gap held by fewer players than there are Climbers."

Not men (note 136). Long, low, soot-black things with six limbs, splayed out from their sides like a gecko's so the
belly runs a hand off the ground: the forelimbs and the middle pair long and ending in hands of three hooked fingers
each, made for gripping steel; the hind pair shorter, heavier, the same hooks. The skin is smooth and wet-looking as
rubber, black, paler grey along the belly and in the folds at the joints; a row of short spines stands down the spine
to a whip of a tail. The head is a smooth blunt wedge, no eyes, nothing on it at all, until it lifts it: under the
wedge's front is a lamprey's mouth, a round sucker ringed with rows of hooked yellow teeth, opening wide. They run
beside the train as fast as it, too low, their bodies snaking; at a gap they go up between the cars like lizards up a
wall.

SK_Climber: root, the body (pelvis, three spine bones), the neck's two, the head and the mouth, the tail's three, and
the six limbs' four bones each (upper out from the body to the elbow, lower down to the wrist, the hand, its hooks).
Faces +Y (the engine's -Z). Clips (§31: still, then too fast): run (beside the train, low, the body snaking, the limbs
in alternating threes, too fast), scrabble (in a coupling gap, up between the cars facing in: the hooks going on the
buffers and the couplers), walk (along the roofs toward the engine, slow, flattened, the head swinging), crouch (inside
an unlit car, folded up in a corner, the head lifted and cocked), grab (reared over its victim, the forehands on their
head, the mouth opened on their face), hit.

    tools/models/build.sh climber        # this, its high copy and the bake -> content/art/models/climber.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3  # noqa: E402

rig.reset()
BZ = 0.38                                      # the body's middle over the ground at rest: the belly a hand off it
LIFT = 0.6 - BZ                                # what the reared-up clips raise the root by to stand where they did
SPINE = [Vector((0, -0.55, BZ)), Vector((0, -0.25, BZ + 0.02)), Vector((0, 0.02, BZ + 0.03)), Vector((0, 0.26, BZ + 0.04)), Vector((0, 0.46, BZ + 0.04))]
NECK = [SPINE[-1], Vector((0, 0.62, BZ + 0.06)), Vector((0, 0.78, BZ + 0.07))]
HEAD_TIP = Vector((0, 1.12, BZ + 0.03))
TAIL = [SPINE[0], Vector((0, -0.85, BZ - 0.04)), Vector((0, -1.15, BZ - 0.1)), Vector((0, -1.45, BZ - 0.16))]
# Each limb (the right's): where it leaves the body, its elbow (out to the side), its wrist (on the ground), the hand
# and the hooks' tips; and the bone it hangs from.
LIMBS = {"f": ("spine_03", (0.12, 0.42, BZ), (0.46, 0.5, BZ + 0.04), (0.52, 0.62, 0.05), (0.54, 0.74, 0.02), (0.55, 0.82, 0.0)),
         "m": ("spine_01", (0.13, 0.02, BZ), (0.48, 0.02, BZ + 0.04), (0.56, 0.04, 0.05), (0.6, 0.15, 0.02), (0.62, 0.23, 0.0)),
         "h": ("pelvis", (0.13, -0.48, BZ - 0.02), (0.42, -0.6, BZ + 0.02), (0.48, -0.72, 0.05), (0.52, -0.6, 0.02), (0.54, -0.52, 0.0))}
bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
for k, name in enumerate(["pelvis", "spine_01", "spine_02", "spine_03"]):
    bones.append(Bone(name, "root" if k == 0 else bones[-1].name, tuple(SPINE[k]), tuple(SPINE[k + 1])))
bones.append(Bone("neck_01", "spine_03", tuple(NECK[0]), tuple(NECK[1])))
bones.append(Bone("neck_02", "neck_01", tuple(NECK[1]), tuple(NECK[2])))
bones.append(Bone("head", "neck_02", tuple(NECK[2]), tuple(HEAD_TIP)))
MOUTH = Vector((0, 1.02, BZ - 0.035))
bones.append(Bone("mouth", "head", tuple(MOUTH), tuple(MOUTH + Vector((0, 0.06, -0.06)))))
for k in range(3):
    bones.append(Bone(f"tail_0{k + 1}", "pelvis" if k == 0 else f"tail_0{k}", tuple(TAIL[k]), tuple(TAIL[k + 1])))
for s, sx in (("r", 1), ("l", -1)):
    for limb, (parent, *pts) in LIMBS.items():
        P = [Vector((sx * x, y, z)) for x, y, z in pts]
        for k, part in enumerate(("upper", "lower", "hand", "hook")):
            bones.append(Bone(f"{part}_{limb}{s}", parent, tuple(P[k]), tuple(P[k + 1])))
            parent = f"{part}_{limb}{s}"
sk = Skeleton("SK_Climber", bones)
sk.build()
kit = rig.Kit(sk, "climber")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


SKIN = Mat("skin.climber", hexc("#1d1b1a"), shine=0.55)
BELLY = Mat("skin.climber_belly", hexc("#4a4542"), shine=0.4)
GUM = Mat("tar.climber_mouth", hexc("#3a0f0d"), shine=0.6)
TOOTH = Mat("skin.climber_teeth", hexc("#a8996f"), shine=0.4)
HOOK = Mat("tar.climber_nail", hexc("#0b0a09"), shine=0.6)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


BODY_BONES = ["tail_03", "tail_02", "tail_01", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "neck_02", "head"]

# --- the body: lean, ribbed under the skin, the spines down its back, the tail a whip --------------------------------
body = kit.part("body")
path = list(reversed(TAIL[1:])) + SPINE + NECK[1:]
pts, radii = [], []
for i in range(len(path) - 1):
    for f in (0.0, 0.5):
        pts.append(path[i].lerp(path[i + 1], f))
pts.append(path[-1])
for q in pts:
    y = q.y
    if y < SPINE[0].y:                        # the tail, thinning to a whip
        t = (SPINE[0].y - y) / (SPINE[0].y - TAIL[-1].y)
        radii.append((0.09 * (1 - t) + 0.01, 0.08 * (1 - t) + 0.01))
    elif y <= SPINE[-1].y:                    # the body, deepest through the chest
        t = (y - SPINE[0].y) / (SPINE[-1].y - SPINE[0].y)
        radii.append((0.16 + 0.06 * math.sin(math.pi * t), 0.11 + 0.04 * math.sin(math.pi * min(1.0, t * 1.2))))
    else:                                     # the neck
        radii.append((0.095, 0.09))


def ribs(i, j, a, p, fr):
    p = Vector(p)
    side, up, _ = fr
    out = side * math.sin(a) + up * math.cos(a)
    chest = smooth01(-0.3, -0.1, p.y) * smooth01(0.46, 0.3, p.y)
    d = 0.006 * max(0.0, math.sin(p.y * 70)) ** 2 * chest * (1 - abs(math.cos(a)))
    d += -0.006 * bell(math.sin(a) / 0.15) * (math.cos(a) > 0) * smooth01(-1.0, -0.4, p.y)
    return p + out * (d + 0.002 * noise3(p * 20, 171, 1.0))


def belly_or_back(face, n):
    return BELLY if n.z < -0.55 else SKIN


body.tube(pts, radii, 16, SKIN, (BODY_BONES, 5.0), ref=(0, 0, 1), shape=ribs, cap0=True, fmat=belly_or_back)
for k in range(11):
    y = SPINE[0].y + 0.1 + k * 0.09
    seg = "pelvis" if y < SPINE[1].y else "spine_01" if y < SPINE[2].y else "spine_02" if y < SPINE[3].y else "spine_03"
    top = Vector((0, y, BZ + 0.12 + 0.01 * math.sin(k)))
    body.tube([top - Vector((0, 0, 0.03)), top + Vector((0, -0.02, 0.025 + 0.01 * (k % 3)))], [0.011, 0.001], 5, HOOK, seg, ref=(0, 1, 0))

# --- the head: a smooth blunt wedge, eyeless; the lamprey's mouth under its front --------------------------------
head = kit.part("head")
HC = NECK[-1].lerp(HEAD_TIP, 0.45)


def wedge(i, j, a, th, p):
    p = Vector(p)
    d = p - HC
    s = d.y / 0.2
    # Flat on top, a blunt edge at the front, tapered from the skull behind.
    p.z = HC.z + d.z * (0.55 + 0.45 * smooth01(0.4, -0.6, s)) * (0.7 if d.z > 0 else 1.0)
    p.x = HC.x + d.x * (1.0 - 0.3 * smooth01(-0.2, 1.0, s))
    # The sucker's lip pressed into the underside at the front.
    m = math.hypot(d.x / 0.06, (d.y - 0.13) / 0.06)
    if d.z < 0:
        p.z += 0.025 * bell(m)
    return p + Vector((0, 0, 0.0015 * noise3(p * 30, 172, 1.0)))


head.blob(HC, (0.1, 0.21, 0.08), 18, 12, SKIN, {"head": 1.0}, shape=wedge)
# The sucker: a round lip, the gums inside, rings of hooked teeth raked in toward the throat.
MN = Vector((0, 0.5, -0.86)).normalized()
side = Vector((1, 0, 0))
upm = side.cross(MN).normalized()
ring = [MOUTH + side * math.cos(2 * math.pi * k / 16) * 0.05 + upm * math.sin(2 * math.pi * k / 16) * 0.05 for k in range(16)]
head.tube(ring, [(0.014, 0.012)] * 16, 8, BELLY, "mouth", ref=[MN] * 16, loop=True)
head.blob(MOUTH - MN * 0.008, (0.05, 0.05, 0.012), 14, 6, GUM, "mouth", rot=Vector((0, 0, 1)).rotation_difference(MN).to_matrix().to_4x4())
for ring_r, n in ((0.04, 12), (0.027, 9), (0.014, 6)):
    for k in range(n):
        t = 2 * math.pi * (k + 0.5 * (n % 2)) / n
        at = MOUTH + side * math.cos(t) * ring_r + upm * math.sin(t) * ring_r + MN * 0.002
        inward = (MOUTH - at).normalized() if (MOUTH - at).length > 1e-6 else -MN
        head.tube([at, at + MN * 0.01 + inward * 0.006], [0.0035, 0.0004], 4, TOOTH, "mouth", ref=(0, 1, 0))

# --- the limbs: splayed, the elbows out at its sides, the hands of three hooks -------------------------------------
limbs = kit.part("limbs")
for s, sx in (("r", 1), ("l", -1)):
    for limb in LIMBS:
        up_, lo, ha, ho = (f"{part}_{limb}{s}" for part in ("upper", "lower", "hand", "hook"))
        big = 1.4 if limb == "h" else 1.22
        limbs.tube([H(up_) + (H(up_) - T(up_)).normalized() * 0.04, H(up_).lerp(T(up_), 0.5), T(up_)], [0.065 * big, 0.052 * big, 0.04 * big], 10, SKIN,
                   ([up_, lo] + (["spine_03"] if limb == "f" else ["spine_01"] if limb == "m" else ["pelvis"]), 6.0), ref=(0, 0, 1),
                   fmat=belly_or_back)
        limbs.blob(T(up_), (0.044 * big, 0.044 * big, 0.044 * big), 8, 6, SKIN, {up_: 0.5, lo: 0.5})
        limbs.tube([H(lo), H(lo).lerp(T(lo), 0.5), T(lo)], [0.04 * big, 0.03 * big, 0.024 * big], 8, SKIN, ([lo, ha], 6.0), ref=(0, 1, 0))
        # The hand: a narrow palm, three long hooked fingers off its end.
        w, e = H(ha), T(ha)
        fwd = (e - w).normalized()
        across = fwd.cross(Vector((0, 0, 1))).normalized()
        limbs.blob(w.lerp(e, 0.5), (0.03, 0.06, 0.016), 8, 5, SKIN, {ha: 1.0}, rot=Vector((0, 1, 0)).rotation_difference(fwd).to_matrix().to_4x4())
        for f in (-1, 0, 1):
            root = e + across * 0.018 * f
            tip = T(ho) + across * 0.03 * f + fwd * 0.03 + Vector((0, 0, 0.034))
            curl = tip - Vector((0, 0, 0.035))
            limbs.tube([root, root.lerp(tip, 0.5) + Vector((0, 0, 0.012)), tip, curl], [0.011, 0.009, 0.006, 0.0012], 6, SKIN, ho, ref=(0, 0, 1),
                       fmat=lambda pts_, n, curl=curl, tip=tip: HOOK if (sum(pts_, Vector()) / len(pts_) - curl).length < (tip - curl).length * 1.4 else SKIN)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot). The limbs go by IK (rig.reach, from a spread of starts): the wrist where
# it's wanted, the elbow kept up and out at its own side; the hand then laid at a world rotation (rig.hang), the hooks
# curled by their own bone.
LIMBNAMES = [(limb, s) for limb in LIMBS for s in ("r", "l")]


def limb_to(pose, limb, s, wrist, elbow_up=True):
    up_, lo = f"upper_{limb}{s}", f"lower_{limb}{s}"
    sign = 1 if s == "r" else -1
    shoulder = rig.pose_points(sk, pose, [(up_, "head")])[0]

    def avoid(elbow):
        # Sprawled: the elbow well out to its own side, and (on the ground) up at the body's height, the forearm
        # coming down from it like a gecko's.
        c = max(0.0, shoulder.x * sign + 0.24 - elbow.x * sign) * 4
        if elbow_up:
            c += max(0.0, shoulder.z - 0.08 - elbow.z) * 3
        return c
    best = None
    for y in (-40, 0, 40):
        for z in (-40, 0, 40):
            for b in (1, -1):
                start = dict(pose) | {up_: (0, y, z), lo: (0, 0, 0)}
                p = rig.reach(sk, start, up_, lo, wrist, elbow_axis=1, bend=b, avoid=avoid)
                tip, elbow = rig.pose_points(sk, p, [(lo, "tail"), (lo, "head")])
                cost = (tip - Vector(wrist)).length + avoid(elbow)
                if best is None or cost < best[0]:
                    best = (cost, p)
    return best[1]


def hands(p, lay=None, curl=0.0):
    """Each hand laid at a world rotation (default: flat, as it rests); the hooks curled `curl` degrees in."""
    p = dict(p)
    lay = lay or {}
    for limb, s in LIMBNAMES:
        p[f"hand_{limb}{s}"] = rig.hang(sk, p, f"hand_{limb}{s}", *lay.get(limb + s, (0, 0, 0)))
        p[f"hook_{limb}{s}"] = (curl, 0, 0)
    return p


def euler_of(q):
    e = q.to_euler("YXZ")
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


def done(p):
    return {k: (euler_of(v) if hasattr(v, "to_euler") else v) for k, v in p.items()}


def rest_wrist(limb, s, spread=1.0, dy=0.0, z=0.05):
    x, y, _ = LIMBS[limb][3]
    return (x * spread * (1 if s == "r" else -1), y + dy, z)


def stance(body, wrists, lay=None, curl=0.0, elbow_up=True):
    p = dict(body)
    for (limb, s), w in wrists.items():
        p = limb_to(p, limb, s, w, elbow_up)
    return done(hands(p, lay, curl))


TRIPOD = {("f", "r"), ("m", "l"), ("h", "r")}

# Run (0.5 s, loop): beside the train, low, the body snaking side to side, the limbs going in alternating threes, each
# thrown far forward and hooked down, too fast; the head held still and level, the tail whipping behind.
run = Clip("run")
for f, ph in ((0, 0.0), (4, 0.27), (8, 0.53), (11, 0.73)):
    u = 2 * math.pi * ph
    body = {"root@loc": (0, 0, -0.06 + 0.03 * abs(math.sin(u))), "pelvis": (0, 0, 10 * math.sin(u)), "spine_01": (0, 0, -6 * math.sin(u)),
            "spine_02": (0, 0, -6 * math.sin(u)), "spine_03": (0, 0, -4 * math.sin(u)), "neck_01": (0, 0, 8 * math.sin(u)), "head": (0, 0, 4 * math.sin(u)),
            "tail_01": (0, 0, -16 * math.sin(u - 0.8)), "tail_02": (0, 0, -20 * math.sin(u - 1.6)), "tail_03": (0, 0, -24 * math.sin(u - 2.4))}
    wr = {}
    for limb, s in LIMBNAMES:
        v = u + (0.0 if (limb, s) in TRIPOD else math.pi)
        swing, lift = 0.28 * math.cos(v), 0.16 * max(0.0, math.sin(v))
        wr[(limb, s)] = rest_wrist(limb, s, 1.25, swing, 0.05 + lift)
    run.key(f, stance(body, wr, curl=10), "LINEAR")
run.close(15)

# Scrabble (0.6 s, loop): up between the cars facing in, the body stood up the gap like a lizard on a wall, the forehooks
# on the car ends above, the middle on the buffers, the hind on the coupler; climbing, the hands going in quick snatches,
# the head pressed up and turning in jerks.
UPRIGHT = {"root@loc": (0, -0.2, 0.25 + LIFT), "pelvis": (62, 0, 0), "spine_01": (8, 0, 0), "spine_02": (6, 0, 0), "spine_03": (4, 0, 0),
           "neck_01": (-14, 0, 0), "neck_02": (-10, 0, 0), "head": (-16, 0, 0), "tail_01": (-50, 0, 0), "tail_02": (-20, 0, 0)}
scrabble = Clip("scrabble")
GRIP = {"f": (0.36, 0.42, 1.75), "m": (0.46, 0.4, 1.2), "h": (0.32, 0.22, 0.75)}
for f, which, turn in ((0, None, 0), (3, ("f", "r"), 10), (5, ("m", "l"), 10), (8, ("h", "r"), -14), (10, ("f", "l"), -14), (13, ("m", "r"), 4),
                       (15, ("h", "l"), 4)):
    wr = {}
    for limb, s in LIMBNAMES:
        x, y, z = GRIP[limb]
        reach = 0.14 if which == (limb, s) else 0.0
        wr[(limb, s)] = (x * (1 if s == "r" else -1), y + 0.04 * (reach > 0), z + reach)
    body = UPRIGHT | {"head": (-16, 0, turn), "neck_02": (-10, 0, turn * 0.5)}
    scrabble.key(f, stance(body, wr, lay={k + s: (90, 0, 0) for k, s in LIMBNAMES}, curl=40, elbow_up=False), "CONSTANT")
scrabble.close(18)

# Walk (2 s, loop): along the roofs toward the engine, slow, flattened to the roof, one limb at a time, the head low and
# swinging from side to side as if it could smell its way.
walk = Clip("walk")
ORDER = [("h", "r"), ("m", "r"), ("f", "r"), ("h", "l"), ("m", "l"), ("f", "l")]
for f in range(0, 60, 5):
    ph = f / 60
    body = {"root@loc": (0, 0, -0.14), "spine_03": (-4, 0, 0), "neck_01": (-8, 0, 20 * math.sin(2 * math.pi * ph)), "head": (-4, 0, 14 * math.sin(2 * math.pi * ph)),
            "pelvis": (0, 0, 4 * math.sin(2 * math.pi * ph)), "tail_01": (0, 0, -8 * math.sin(2 * math.pi * ph))}
    wr = {}
    for idx, (limb, s) in enumerate(ORDER):
        local = (ph * 6 - idx) % 6
        if local < 1:
            dy, lift = 0.25 * (local - 0.5) * 2, 0.12 * math.sin(math.pi * local)
        else:
            dy, lift = 0.25 * (1 - 2 * (local - 1) / 5), 0.0
        wr[(limb, s)] = rest_wrist(limb, s, 1.3, dy, 0.05 + lift)
    walk.key(f, stance(body, wr, curl=14), "LINEAR")
walk.close(60)

# Crouch (4 s, loop): inside an unlit car, folded up in a corner, sat back on its hind limbs, the others drawn in under
# it, the body tipped up and the head lifted and cocked, still; once, the head snaps to the other side.
SIT = {"root@loc": (0, 0.1, -0.2 + LIFT), "pelvis": (22, 0, 0), "spine_01": (4, 0, 0), "spine_02": (2, 0, 0), "neck_01": (-12, 0, 0), "neck_02": (-8, 0, 0),
       "tail_01": (-30, 0, 30), "tail_02": (0, 0, 40), "tail_03": (0, 0, 40)}
SIT_WR = {("f", "r"): (0.24, 0.62, 0.05), ("f", "l"): (-0.24, 0.6, 0.05), ("m", "r"): (0.36, 0.3, 0.05), ("m", "l"): (-0.36, 0.28, 0.05),
          ("h", "r"): (0.4, -0.3, 0.05), ("h", "l"): (-0.4, -0.32, 0.05)}
crouch = Clip("crouch")
crouch.key(0, stance(SIT | {"head": (-20, 0, 28)}, SIT_WR, curl=24), "CONSTANT")
crouch.key(70, stance(SIT | {"head": (-20, 0, 28)}, SIT_WR, curl=24), "CONSTANT")
crouch.key(72, stance(SIT | {"head": (-24, 0, -30)}, SIT_WR, curl=30), "CONSTANT")
crouch.key(110, stance(SIT | {"head": (-24, 0, -30)}, SIT_WR, curl=30), "BEZIER")
crouch.close(120)

# Grab (1.2 s, loop): reared up over its victim in front of it, the forehooks over their head, the middle hooks in
# their shoulders, the hind limbs braced; the head pressed down at their face, the sucker opening on it, and working.
REAR = {"root@loc": (0, -0.25, 0.1 + LIFT), "pelvis": (40, 0, 0), "spine_01": (10, 0, 0), "spine_02": (6, 0, 0), "spine_03": (-6, 0, 0),
        "neck_01": (-24, 0, 0), "neck_02": (-24, 0, 0), "head": (-30, 0, 0), "tail_01": (-30, 0, 0)}
GRAB_WR = {("f", "r"): (0.16, 0.62, 1.72), ("f", "l"): (-0.16, 0.6, 1.72), ("m", "r"): (0.26, 0.5, 1.4), ("m", "l"): (-0.26, 0.5, 1.42),
           ("h", "r"): (0.4, -0.55, 0.05), ("h", "l"): (-0.4, -0.55, 0.05)}
grab = Clip("grab")
for f, k in ((0, 0.0), (8, 1.0), (14, 0.4), (22, 1.0), (30, 0.2)):
    p = stance(REAR | {"head": (-30 - 6 * k, 0, 4 * k), "mouth@scale": (1 + 0.5 * k, 1, 1 + 0.5 * k)}, GRAB_WR,
               lay={"fr": (0, 0, -30), "fl": (0, 0, 30)}, curl=50 + 10 * k, elbow_up=False)
    grab.key(f, p, "CONSTANT" if f in (8, 22) else "BEZIER")
grab.close(36)

# Hit (0.4 s, once): struck, it flattens and coils round the blow, the tail lashing, and snaps back.
REST = stance({}, {(limb, s): rest_wrist(limb, s, 1.25) for limb, s in LIMBNAMES})
hit = Clip("hit", loop=False)
hit.key(0, REST, "CONSTANT")
hit.key(2, stance({"root@loc": (0, 0, -0.15), "spine_01": (0, 10, 20), "spine_03": (0, 0, -24), "head": (20, 0, 30), "tail_01": (0, 0, 50)},
                  {(limb, s): rest_wrist(limb, s, 1.2) for limb, s in LIMBNAMES}, curl=40), "CONSTANT")
hit.key(8, stance({"spine_01": (0, 0, -6)}, {(limb, s): rest_wrist(limb, s) for limb, s in LIMBNAMES}), "LINEAR")
hit.key(12, REST, "CONSTANT")

kit.build()
rig.bake(sk, [run, scrabble, walk, crouch, grab, hit])
print("[dt] climber", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "climber.glb", kit)
