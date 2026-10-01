"""THE WHISTLER (GDD v1.2 §21 between the cars, App. A.4 · absence): "Blows your own whistle, then hides in a coupling
gap. Only strikes when the train is stopped. It carries its victim off to a nest."

A long thing folded small. Stood up it would be well over two metres, all limb, but it lives crammed under the bridge
plate between two cars, wrapped round the drawgear, soot-black and slick with the grease and muck of the couplers. Its
face is the one pale thing on it, and the one thing you'll see if you look: a long face like a mask of skin, the eyes
grown over, and the mouth drawn out into a puckered tube, pursed for ever as if to whistle. Its right arm is a third as
long again as its left: it reaches up out of the gap to pull the cord. A loop of rusted coupling chain has grown into
its waist. It runs on all fours, too fast, with someone over its back.

SK_Human (rig.human) stretched, the right forearm lengthened. Faces +Y (the engine's -Z). Its origin is the gap's (the
sim's GapLocal, 0.6 m over the rail): CreatureArt drops it to the rail. Clips (GDD §31: still, then too fast):
fold (hidden in its gap, crammed in, breathing), whistle (the long arm up through the gap, two yanks, the head thrown
back), watch (unfolded into a crouch at the gap's mouth, the head turning in jerks), run (on all fours, carrying),
hit (struck, once).

    tools/models/build.sh whistler        # this, its high copy and the bake -> content/art/models/whistler.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, over  # noqa: E402

rig.reset()
sk = rig.human(height=2.15, leg=1.12, arm=1.25, torso=0.95, neck=1.5, head=1.05, width=0.75, fingers=True, sockets=False)
# The right forearm a third as long again, for the cord: the chain past the elbow moved out along the arm.
REACH = 0.24
for name in ("hand_r", "fingers_r", "thumb_r"):
    b = sk[name]
    b.head.x += REACH
    b.tail.x += REACH
sk["lowerarm_r"].tail.x += REACH
sk.build()
kit = rig.Kit(sk, "whistler")


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
# The head: long, narrow, the face drawn out forward into the mouth's tube.
HC = Vector((0.0, 0.02, NECK_TOP + 0.13))
HR = Vector((0.085, 0.115, 0.15))
EYE_U, EYE_W = 0.36, 0.12
MOUTH_W = -0.52

SKIN = Mat("skin.whistler", hexc("#1d1a18"), shine=0.55)
FACE = Mat("skin.whistler_face", hexc("#a9a49a"), shine=0.35)
MOUTH = Mat("tar.whistler_mouth", hexc("#0a0606"), shine=0.9)
NAIL = Mat("tar.nail", hexc("#2a2320"), shine=0.5)
CHAIN = Mat("rust_heavy.chain", hexc("#5a3a26"), shine=0.3)


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


def on_face(u, w):
    """A point on the front of the egg at (u across, w up)."""
    v = math.sqrt(max(0.0, 1 - u * u - w * w))
    return HC + Vector((u * HR.x, v * HR.y, w * HR.z))


MOUTH_AT = on_face(0.0, MOUTH_W)


def skull(i, j, a, th, p):
    """The long face: the sockets there but grown over, shallow dishes of skin; the brow heavy over them; the cheeks
    drawn in hard towards the mouth, which pulls the whole lower face forward into its pucker; the jaw narrow."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = max(0.0, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.022 * front * bell(math.hypot((u - eu) / 0.26, (w - EYE_W) / 0.2))          # grown-over sockets
        off += 0.01 * front * bell((u - eu) / 0.3) * bell((w - EYE_W - 0.22) / 0.09)         # the brow
    off -= 0.022 * front * bell((abs(u) - 0.5) / 0.22) * bell((w - MOUTH_W) / 0.3)           # cheeks drawn in
    # The lower face pulled forward to the mouth, as lips pulled by a drawstring.
    off += 0.03 * front * bell(u / 0.45) * bell((w - MOUTH_W) / 0.28)
    off += 0.012 * front * bell(u / 0.12) * smooth01(0.12, -0.05, w) * smooth01(MOUTH_W + 0.12, -0.15, w)  # the nose's ridge
    q = p + n * off
    if w < -0.35:
        k = 1 - 0.25 * smooth01(-0.35, -1.0, w)
        q.x = HC.x + (q.x - HC.x) * k
    return q


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    # The pale skin over the whole skull but the back of the neck: a mask, not a face with a cap of black over it.
    return FACE if v > -0.35 and w > -1.1 else SKIN


# ----------------------------------------------------------------------------------------------------------------
# The head and neck.
head = kit.part("head")
head.blob(HC, tuple(HR), 26, 20, SKIN, "head", shape=skull, fmat=face_or_skull)
# The mouth: the lips drawn out into a tube a hand's breadth long, puckered in folds round its rim, and the hole black.
mn = head_normal(MOUTH_AT)
fwd = (mn + Vector((0, 0.4, -0.25))).normalized()
LIP = [MOUTH_AT - fwd * 0.02, MOUTH_AT + fwd * 0.03, MOUTH_AT + fwd * 0.07, MOUTH_AT + fwd * 0.095]
TUBE_END = LIP[-1]
head.tube(LIP, [0.034, 0.03, 0.022, 0.019], 14, FACE, "head", ref=(0, 0, 1),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.004 * math.cos(a * 7) * (1 if i >= 1 else 0)))
head.tube([TUBE_END - fwd * 0.004, TUBE_END - fwd * 0.05], [0.014, 0.006], 10, MOUTH, "head", ref=(0, 0, 1), cap1="point")
for sx in (-1, 1):
    e = HC + Vector((sx * HR.x * 0.96, -0.02, 0.02))
    head.blob(e, (0.01, 0.024, 0.035), 8, 5, SKIN, "head")
NECK = [Vector((0, -0.02, TOP - 0.02)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.01, NECK_TOP + 0.04))]
head.tube(NECK, [(0.034, 0.032), (0.028, 0.028), (0.032, 0.03)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * bell((abs(math.sin(a)) - 0.7) / 0.15))


# ----------------------------------------------------------------------------------------------------------------
# The body: narrow, hunched, the spine's knuckles standing in a ridge down its back, the ribs; the coupling chain
# grown into its waist.
body = kit.part("body")
SPINE = (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0)
TORSO = [(HIP - 0.05, 0.11, 0.08), (HIP + 0.05, 0.1, 0.075), (WAIST, 0.075, 0.06), (WAIST + 0.08, 0.09, 0.07),
         (H("spine_02").z + 0.05, 0.12, 0.09), (CHEST + 0.03, 0.13, 0.095), (TOP - 0.07, 0.13, 0.085),
         (TOP - 0.03, 0.11, 0.07), (TOP, 0.075, 0.055), (TOP + 0.03, 0.042, 0.04)]


def resampled(secs, step):
    out = []
    for (z0, x0, y0), (z1, x1, y1) in zip(secs, secs[1:]):
        n = max(1, round((z1 - z0) / step))
        out += [(z0 + (z1 - z0) * k / n, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    return out + [secs[-1]]


def ridged_back(i, j, a, p, fr):
    """The spine's knuckles standing in a ridge, the shoulder blades, the ribs, the belly sunk."""
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    front, back = max(0.0, math.cos(a)), max(0.0, -math.cos(a))
    d = 0.0
    d += 0.014 * back * bell(math.sin(a) / 0.18) * (0.5 + 0.5 * max(0.0, math.sin(p.z * 80)))
    d += 0.01 * back * bell((abs(math.sin(a)) - 0.55) / 0.2) * bell((p.z - CHEST - 0.02) / 0.07)
    if H("spine_02").z - 0.06 < p.z < TOP - 0.05:
        d += 0.008 * max(0.0, math.sin((p.z - WAIST) * 55)) ** 2 * (0.4 + 0.6 * abs(math.sin(a)))
    d -= 0.01 * front * bell((p.z - WAIST - 0.02) / 0.06)
    return p + out * d


RIBBED = resampled(TORSO, 0.022)
body.tube([Vector((0, 0.0, z)) for z, _, _ in RIBBED], [(rx, ry) for _, rx, ry in RIBBED], 14, SKIN, SPINE, ref=(0, 1, 0),
          square=0.9, shape=ridged_back)
for sx in (-1, 1):
    side = "r" if sx > 0 else "l"
    body.tube([Vector((sx * 0.018, 0.055, TOP - 0.03)), Vector((sx * 0.09, 0.045, TOP - 0.012)), Vector((sx * (SHOULDER.x - 0.01), 0.01, SHOULDER.z + 0.008))],
              [0.01, 0.009, 0.013], 6, SKIN, [f"clavicle_{side}"], ref=(0, 0, 1))
    body.blob(Vector((sx * 0.09, 0.02, HIP + 0.05)), (0.026, 0.03, 0.026), 8, 4, SKIN, "pelvis")
# The chain: links round its waist, sunk into the skin, two hanging loose.
chain = kit.part("chain")
for k in range(13):
    a = 2 * math.pi * k / 13
    c = Vector((math.sin(a) * 0.085, math.cos(a) * 0.068, WAIST + 0.01 + 0.006 * math.sin(a * 3)))
    t = Vector((math.cos(a), -math.sin(a), 0)).normalized()
    up = Vector((0, 0, 1)) if k % 2 == 0 else Vector((math.sin(a), math.cos(a), 0))
    ring = [c + (t * math.cos(b) * 0.022 + up * math.sin(b) * 0.012) for b in (2 * math.pi * q / 6 for q in range(6))]
    chain.tube(ring, [0.004] * 6, 4, CHAIN, ["spine_01"], ref=(0, 0, 1), loop=True)
for k in range(4):
    c = Vector((-0.06 - 0.008 * k, 0.05, WAIST - 0.03 - 0.032 * k))
    up = Vector((0, 1, 0)) if k % 2 == 0 else Vector((1, 0, 0))
    ring = [c + (Vector((0, 0, 1)) * math.cos(b) * 0.02 + up * math.sin(b) * 0.011) for b in (2 * math.pi * q / 6 for q in range(6))]
    chain.tube(ring, [0.004] * 6, 4, CHAIN, ["pelvis"], ref=(1, 0, 0), loop=True)


# ----------------------------------------------------------------------------------------------------------------
# Limbs: one skin over each two bones, bent by its weights; the hands long and hooked, the feet long-toed, for gripping
# the drawgear as hands do.
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
    limbs.tube([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.45), e, e.lerp(wr, 0.3), e.lerp(wr, 0.75), wr],
               [0.03, 0.022, (0.026, 0.028), (0.022, 0.02), (0.018, 0.015), (0.017, 0.014)], 8, SKIN, jointed(sh, e, wr, ua, la),
               ref=(0, 0, 1))
    k0 = T(hd)
    limbs.blob(wr, (0.019, 0.017, 0.015), 7, 4, SKIN, hd)
    limbs.tube([wr + Vector((sx * 0.01, 0, 0)), wr.lerp(k0, 0.55), k0], [(0.026, 0.012), (0.04, 0.013), (0.042, 0.012)], 8, SKIN, hd,
               ref=(0, 0, 1), cap0=True)
    fl = 0.24 if s == "r" else 0.2
    for f in range(4):
        spread = (f - 1.5) * 0.022
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.2, 0)).normalized()
        n = fl * (0.8 + 0.2 * (1 - abs(f - 1.5) / 1.5)) * (0.85 if f == 3 else 1.0)
        # Hooked: the last joint bent down already.
        pts = [base, base + along * n * 0.4, base + along * n * 0.72, base + along * n * 0.9 - Vector((0, 0, 0.03))]
        w = lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}
        limbs.tube(pts, [0.009, 0.008, 0.0065, 0.004], 5, SKIN, w, ref=(0, 0, 1), cap1="point",
                   shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.0025 if i in (1, 2) else 0.0))
        limbs.tube([pts[-1] + Vector((0, 0, 0.012)), pts[-1] - Vector((0, 0, 0.012))], [(0.006, 0.003), (0.004, 0.002)], 4, NAIL, f"fingers_{s}",
                   ref=(1, 0, 0))
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.8], [0.01, 0.008, 0.004], 5, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")

for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.03)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.4), an + Vector((0, 0, 0.02))],
               [0.048, 0.032, (0.036, 0.038), (0.026, 0.03), 0.02], 8, SKIN, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0))
    limbs.blob(an + Vector((0, -0.03, -0.01)), (0.016, 0.022, 0.018), 6, 4, SKIN, f"foot_{s}")
    # The foot long and thin, the toes long as fingers, splayed for gripping.
    limbs.tube([an, an.lerp(bl, 0.5), bl], [(0.022, 0.018), (0.024, 0.014), (0.026, 0.012)], 7, SKIN,
               lambda p, s=s, bl=bl: {f"ball_{s}": 1.0} if p.y > bl.y - 0.01 else {f"foot_{s}": 1.0}, ref=(0, 0, 1))
    for f in range(4):
        dx = (f - 1.5) * 0.014 * sx
        base = bl + Vector((dx, 0.0, 0))
        along = Vector((dx * 2.5, 1, 0)).normalized()
        n = 0.11 + 0.02 * (1.5 - abs(f - 1.5))
        pts = [base, base + along * n * 0.5, base + along * n - Vector((0, 0, 0.02))]
        limbs.tube(pts, [0.007, 0.006, 0.003], 5, SKIN, f"ball_{s}", ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/tippy_toesie.py: arms lowered by Y, swung by X (+ forward); a spine tipped forward by -X
# and turned by Z; a thigh raised forward by +X, a knee bent by -X; a foot pointed down by -X.
BASE = mirror({
    "upperarm_r": (0, 70, 0), "lowerarm_r": (0, 8, 0), "hand_r": (0, 0, 0), "fingers_r": (0, 30, 0), "thumb_r": (0, 6, 0),
    "thigh_r": (6, 0, 0), "calf_r": (-10, 0, 0), "foot_r": (0, 0, 0),
})


def elbow_out(e):
    return 0.4 if abs(e.x) < 0.12 and e.y > -0.05 else 0.0


FEET = [(b, e) for b in ("foot_l", "foot_r", "ball_l", "ball_r") for e in ("head", "tail")]


def ground(pose):
    """Where the floor is in this pose, before the bake plants its feet (rig.feet_planter): its lowest foot point. A hand
    meant to be on the ground, or on a shin, has to be placed from the pose, not at an absolute height."""
    return min(p.z for p in rig.pose_points(sk, pose, FEET)) - 0.02


def knee(pose, side):
    return rig.pose_points(sk, pose, [(f"calf_{side}", "head")])[0]


def arm_to(pose, side, wrist, curl=0.0, avoid=elbow_out):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=avoid)
    p[f"fingers_{side}"] = (0, (30 + 60 * curl) * k, 0)
    p[f"thumb_{side}"] = (0, (6 + 20 * curl) * k, 0)
    return p


# Fold (5 s, loop): crammed into the gap, squatting on its heels as low as it goes, the long legs folded up against its
# chest, the arms wrapped round them, the chin on its knees and the face looking out over them. Breathing, slow; once,
# the fingers of the right hand open and close on its shin.
CROUCH = dict(thigh_r=(118, 0, 0), calf_r=(-150, 0, 0), thigh_l=(118, 0, 0), calf_l=(-150, 0, 0), foot_r=(40, 0, 0), foot_l=(40, 0, 0))
FD = over(BASE, spine_01=(-30, 0, 0), spine_02=(-28, 0, 0), spine_03=(-22, 0, 0), neck=(-6, 0, 0), head=(46, 0, -14), **CROUCH)
# The hands round the shins, a hand's length under the knees and in front.
FD = arm_to(arm_to(FD, "r", knee(FD, "r") + Vector((0.02, 0.06, -0.2)), curl=0.8), "l", knee(FD, "l") + Vector((-0.02, 0.06, -0.22)), curl=0.8)
fold = Clip("fold")
for f, k in ((0, 0.0), (36, 1.0), (75, 0.0), (110, 1.0)):
    fold.key(f, over(FD, spine_02=(-28 + 3 * k, 0, 0), spine_03=(-22 + 2 * k, 0, 0)), "BEZIER")
for f, c in ((128, 0.2), (131, 0.9), (134, 0.3), (137, 0.8)):
    fold.key(f, FD | {"fingers_r": (0, 30 + 60 * c, 0)}, "CONSTANT")
fold.close(150)

# Whistle (2.5 s, loop for as long as it blows): up out of the fold, the long right arm shoots up through the gap, the
# hand hooked over the cord along the cars' eaves, 3 m up, and yanks: twice, hard, the head thrown back.
# It rises out of the fold for it, up on its toes in the gap, all its length, the body stretched up after the arm.
UP = over(BASE, spine_01=(6, 0, 0), spine_02=(4, 0, 0), spine_03=(2, 0, 0), neck=(16, 0, 0), head=(30, 0, -30),
          thigh_r=(14, 0, 0), calf_r=(-24, 0, 0), thigh_l=(18, 0, 0), calf_l=(-30, 0, 0), foot_r=(-40, 0, 0), foot_l=(-40, 0, 0))
UP = arm_to(UP, "l", Vector((-0.25, 0.1, ground(UP) + 1.2)), curl=0.8)
CORD = Vector((0.08, 0.05, ground(UP) + 3.05))
whistle = Clip("whistle")
for f, pull, interp in ((0, 0.0, "CONSTANT"), (3, 0.0, "LINEAR"), (10, 1.0, "CONSTANT"), (16, 0.0, "LINEAR"), (22, 1.0, "CONSTANT"),
                        (34, 1.0, "LINEAR"), (44, 0.0, "LINEAR")):
    p = arm_to(over(UP, head=(30 + 8 * pull, 0, -30)), "r", CORD - Vector((0, 0, 0.32 * pull)), curl=0.9)
    whistle.key(f, p, interp)
whistle.key(60, arm_to(UP, "r", CORD, curl=0.9), "CONSTANT")
whistle.close(75)
# (The fold's the first key it's drawn from when the whistle starts: the sim's TELEGRAPH pops it up out of the gap.)

# Watch (4 s, loop): unfolded into a crouch at the gap's mouth, on its toes and knuckles, the head out low and turning
# in jerks, held, turning; the long arm laid out along the ballast ahead of it.
WT = over(BASE, spine_01=(-40, 0, 0), spine_02=(-24, 0, 0), spine_03=(-10, 0, 0), neck=(-6, 0, 0), head=(36, 0, 0),
          thigh_r=(100, 0, 0), calf_r=(-128, 0, 0), thigh_l=(96, 0, 0), calf_l=(-124, 0, 0), foot_r=(30, 0, 0), foot_l=(30, 0, 0))
WT = arm_to(arm_to(WT, "r", Vector((0.18, 0.95, ground(WT) + 0.03)), curl=0.6), "l", Vector((-0.2, 0.55, ground(WT) + 0.04)), curl=0.6)
watch = Clip("watch")
for f, turn in ((0, 0), (30, 34), (52, 34), (54, -28), (86, -28), (88, 6), (120, 6)):
    watch.key(f, over(WT, neck=(-6, 0, turn * 0.4), head=(36, 0, turn)), "CONSTANT")
watch.close(120)

# Run (0.6 s, loop): on all fours, the back low and long, the arms and legs going in a lope too fast for it, the long
# arm reaching furthest; the head held up and still.
RN = over(BASE, spine_01=(-62, 0, 0), spine_02=(-16, 0, 0), spine_03=(-6, 0, 0), neck=(10, 0, 0), head=(40, 0, 0))
run = Clip("run")
for f, k in ((0, 1.0), (4.5, 0.0), (9, -1.0), (13.5, 0.0)):
    p = over(RN, thigh_r=(70 + 40 * k, 0, 0), calf_r=(-80 - 30 * max(0.0, -k), 0, 0), thigh_l=(70 - 40 * k, 0, 0),
             calf_l=(-80 - 30 * max(0.0, k), 0, 0), pelvis=(0, 0, 6 * k))
    floor = ground(p)
    p = arm_to(p, "r", Vector((0.2, 0.75 + 0.3 * k, floor + 0.05 + 0.15 * max(0.0, -k))), curl=0.5)
    p = arm_to(p, "l", Vector((-0.2, 0.6 - 0.25 * k, floor + 0.05 + 0.15 * max(0.0, k))), curl=0.5)
    run.key(f, p, "LINEAR")
run.close(18)

# Hit: struck, it folds round the blow and snaps back too fast.
hit = Clip("hit", loop=False)
hit.key(0, WT, "CONSTANT")
hit.key(2, over(WT, spine_01=(-20, 0, 18), spine_02=(-10, 0, 10), head=(10, 0, 30)), "CONSTANT")
hit.key(8, over(WT, spine_02=(-30, 0, -6)), "LINEAR")
hit.key(12, WT, "CONSTANT")

kit.build()
rig.bake(sk, [fold, whistle, watch, run, hit], plant=rig.feet_planter(sk, bones=("foot_l", "foot_r", "ball_l", "ball_r"), lowest=0.02))
print("[dt] whistler", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "whistler.glb", kit)
