"""THE PASSENGER (GDD v1.2 §21 corrupted humans, App. A.8 · absence): "A crew member from a train lost long ago. In the
dark it passes for one of yours. It never talks."

A conductor off a train that went into the Territory a lifetime ago and never came out. In the dark he's a man in a
heavy coat and a cap, like any of the crew. Close to, it's all old: a greatcoat to below the knee, double-breasted,
the wool gone to felt and mould, the brass buttons green, the hem rotted ragged; a conductor's peaked cap gone soft,
its badge tarnished black; old laced boots. A scarf wound up high round the throat, in the colour of whichever of the
crew it's copying (CreatureArt tints it, as the crew's own): from across a car, theirs. His face is grey, the cheeks
fallen in, the eyes open and filmed milk-white, and the mouth is sewn shut, the lips drawn together with rusted
wire: that's why it never says a word. In his left hand, always, a pocket watch that stopped when his train did.

SK_Human (rig.human) at a man's height, a little narrow. Faces +Y (the engine's -Z). Clips (§31: still, then too fast):
stand (hanging about near the rear like crew, but dead still: no breath in it; the head turned slowly to look, and
snapped back; the watch lifted to the face and looked at, and put down), walk (the crew's own gait, 1.4 m/s, but the
head never moves and the arms never swing), drag (GRAB: hauling someone behind it to the caboose by their collar,
leant into it, at walking pace), pin (UNCOUPLE: bent at the coupling, heaving at the lever in jerks, the other hand
still in the collar), hit.

    tools/models/build.sh passenger        # this, its high copy and the bake -> content/art/models/passenger.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from mathutils import Euler  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over, swap  # noqa: E402

rig.reset()
sk = rig.human(1.8, neck=0.92, width=0.92, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "passenger")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
HIP = H("thigh_r").z
WAIST = H("spine_01").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
KNEE = H("calf_r").z
HC = Vector((0.0, 0.018, NECK_TOP + 0.098))
HR = Vector((0.077, 0.095, 0.11))
HEM = 0.4

COAT = Mat("wool.passenger_coat", hexc("#1e2026"), shine=0.04)
SCARF = Mat("wool.passenger.paint", hexc("#9a9a96"), shine=0.04)     # the copied crewmate's colour (CreatureArt tints it)
CAP = Mat("wool.passenger_cap", hexc("#16171b"), shine=0.06)
PEAK = Mat("leather.passenger_peak", hexc("#0d0c0b"), shine=0.55)
BRASS = Mat("brass.passenger", hexc("#4f5338"), shine=0.4)
SKIN = Mat("flesh.passenger", hexc("#7f8584"), shine=0.2)
LIPS = Mat("flesh.passenger_lips", hexc("#4e4446"), shine=0.3)
EYE = Mat("flesh.passenger_eye", hexc("#bdbeb4"), shine=0.75)
WIRE = Mat("rust_heavy.passenger_wire", hexc("#5a3a26"), shine=0.4)
BOOT = Mat("leather.passenger_boot", hexc("#16120f"), shine=0.3)
TROUSER = Mat("wool.passenger_trouser", hexc("#191a1e"), shine=0.04)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------------------------------------------
# The head: a dead man's, the cheeks fallen in under the bones, the eyes deep, the mouth drawn shut.
def head_units(p):
    d = p - HC
    return d.x / HR.x, d.y / HR.y, d.z / HR.z


def head_normal(p):
    d = p - HC
    return Vector((d.x / HR.x ** 2, d.y / HR.y ** 2, d.z / HR.z ** 2)).normalized()


EYE_U, EYE_W = 0.43, 0.12
MOUTH_W = -0.52


def face(i, j, a, th, p):
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(0.1, 0.55, v)
    n = head_normal(p)
    off = 0.0
    # The brow standing over sockets sunk deep; the cheekbones, and under them the cheeks fallen in.
    off += 0.008 * front * bell((w - 0.3) / 0.1) * bell(u / 0.7)
    for eu in (-EYE_U, EYE_U):
        off -= 0.016 * front * bell(math.hypot((u - eu) / 0.24, (w - EYE_W) / 0.17))
        off += 0.006 * front * bell(math.hypot((u - eu * 1.25) / 0.2, (w + 0.08) / 0.1))
        off -= 0.014 * front * bell(math.hypot((u - eu * 1.15) / 0.22, (w + 0.32) / 0.2))
    # The nose: thin, the bridge high, the tip drooped.
    off += 0.022 * front * bell(u / 0.11) * bell((w + 0.12) / 0.2) * smooth01(-0.3, 0.1, w + 0.25)
    # The mouth pursed in on its stitches; the chin, and the jaw's line sharp under the skin.
    off -= 0.006 * front * bell(math.hypot(u / 0.32, (w - MOUTH_W) / 0.07))
    off += 0.006 * front * bell(math.hypot(u / 0.3, (w + 0.78) / 0.12))
    off += 0.004 * bell((w + 0.6) / 0.08) * bell((abs(u) - 0.75) / 0.15)
    # The temples hollow.
    off -= 0.006 * bell((abs(u) - 0.85) / 0.12) * bell((w - 0.25) / 0.2) * front
    return p + n * (off + 0.0015 * noise3(p * 40, 71, 1.0))


def on_face(u, w, lift=0.0):
    """A point on the face's (deformed) surface, and its normal, at head units (u, w)."""
    v = math.sqrt(max(0.0, 1 - u * u - w * w))
    p = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    n = head_normal(p)
    p = face(0, 0, 0, 0, p)
    return p + n * lift, n


head = kit.part("head")
head.blob(HC, tuple(HR), 22, 18, SKIN, "head", shape=face)
# The ears, small and flat to the skull.
for sx in (1, -1):
    head.blob(HC + Vector((sx * HR.x * 0.96, -0.004, 0.0)), (0.008, 0.02, 0.03), 6, 5, SKIN, "head")
# The eyes: filmed over, white, open and staring, bulged a little out of the sunk sockets.
for eu in (-EYE_U, EYE_U):
    c, n = on_face(eu, EYE_W, -0.002)
    head.blob(c, (0.0145, 0.0115, 0.0122), 10, 7, EYE, "head")
# The mouth: the lips drawn tight together, and the wire through them, seven stitches.
lips = [on_face(x, MOUTH_W - 0.012 * (x / 0.3) ** 2, 0.001)[0] for x in (-0.3, -0.2, -0.1, 0.0, 0.1, 0.2, 0.3)]
head.tube(lips, [0.003, 0.0055, 0.0065, 0.0068, 0.0065, 0.0055, 0.003], 6, LIPS, "head", ref=(0, 0, 1))
for k in range(7):
    x = -0.24 + 0.08 * k
    top, _ = on_face(x, MOUTH_W + 0.1, 0.0015)
    mid, nm = on_face(x, MOUTH_W + 0.015, 0.0075)
    bot, _ = on_face(x, MOUTH_W - 0.08, 0.0015)
    head.tube([top, mid, bot], [0.0011, 0.0013, 0.0011], 4, WIRE, "head", ref=(1, 0, 0))
# The neck, thin, the cords standing.
NECK = [Vector((0, -0.005, TOP - 0.03)), Vector((0, 0.0, (TOP + NECK_TOP) / 2)), Vector((0, 0.012, NECK_TOP + 0.03))]
head.tube(NECK, [(0.045, 0.043), (0.036, 0.037), (0.04, 0.042)], 10, SKIN, ["spine_03", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * bell((abs(math.sin(a)) - 0.5) / 0.15))

# The cap: a conductor's, the crown gone soft and leaning, the peak stiff over the eyes, the band, the badge.
CROWN0 = HC.z + 0.045
cap = kit.part("cap")


def crown_shape(i, j, a, p, fr):
    p = Vector(p)
    sag = 0.01 * (i >= 2) * max(0.0, math.cos(a + 0.8))                # the crown slumped to one side and back
    return p - Vector((0, 0, sag)) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.002 * noise3(p * 30, 72, 1.0)


crown = [Vector((0, 0.008, CROWN0)), Vector((0, 0.01, CROWN0 + 0.03)), Vector((0, 0.006, CROWN0 + 0.058)), Vector((0, 0.0, CROWN0 + 0.072))]
cap.tube(crown, [(0.084, 0.1), (0.087, 0.104), (0.096, 0.114), (0.097, 0.116)], 18, CAP, "head", ref=(0, 1, 0), cap1=True, shape=crown_shape)
cap.tube([crown[0] + Vector((0, 0, 0.004)), crown[0] + Vector((0, 0, 0.026))], [(0.087, 0.103), (0.089, 0.106)], 18, PEAK, "head", ref=(0, 1, 0))
# The peak, out over the face and down a little.
PK = Vector((0, 0.1, CROWN0 + 0.002))
tilt = Euler((math.radians(-14), 0, 0), "XYZ").to_matrix().to_4x4()
cap.blob(PK, (0.078, 0.048, 0.005), 14, 4, PEAK, "head", rot=tilt,
         shape=lambda i, j, a, th, p: Vector(p) if (Vector(p) - PK).y > -0.02 else Vector(p) + Vector((0, 0.02, 0)))
cap.blob(Vector((0, 0.104, CROWN0 + 0.04)), (0.018, 0.005, 0.013), 8, 4, BRASS, "head")

# ----------------------------------------------------------------------------------------------------------------
# The coat: a greatcoat to below the knee, double-breasted, the collar up; the skirt over the legs, ragged at its hem.
coat = kit.part("coat")
TORSO = [(WAIST - 0.06, 0.168, 0.124), (WAIST + 0.04, 0.16, 0.118), (CHEST - 0.06, 0.172, 0.128), (CHEST + 0.06, 0.186, 0.13),
         (TOP - 0.02, 0.196, 0.118), (TOP + 0.02, 0.13, 0.095), (TOP + 0.04, 0.07, 0.07)]


def coat_shape(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    # Sagging wool, the folds hanging from the shoulders; the front's overlap standing a little.
    d = 0.004 * math.sin(a * 7 + p.z * 9) * smooth01(TOP, WAIST, p.z) + 0.003 * bell((math.sin(a) - 0.25) / 0.06) * (math.cos(a) > 0)
    return p + out * (d + 0.002 * noise3(p * 14, 73, 1.0))


rings = []
for (z0, x0, y0), (z1, x1, y1) in zip(TORSO, TORSO[1:]):
    for k in range(3):
        f = k / 3
        rings.append((z0 + (z1 - z0) * f, x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
rings.append(TORSO[-1])
coat.tube([Vector((0, -0.005, z)) for z, _, _ in rings], [(rx, ry) for _, rx, ry in rings], 16, COAT, (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0),
          ref=(0, 1, 0), shape=coat_shape)


def skirt_weights(p):
    """The skirt goes with the thigh on its side (its middle between both), the more so the further down."""
    k = smooth01(WAIST - 0.02, KNEE + 0.15, p.z)
    side = smooth01(-0.06, 0.06, p.x)
    w = {"pelvis": 1 - k}
    if k > 0:
        if side > 1e-4:
            w["thigh_r"] = k * side
        if side < 1 - 1e-4:
            w["thigh_l"] = k * (1 - side)
    return {n: v for n, v in w.items() if v > 1e-4}


SKIRT = [(WAIST - 0.04, 0.17, 0.126), (HIP - 0.02, 0.19, 0.14), (HIP - 0.2, 0.21, 0.155), (KNEE + 0.05, 0.225, 0.168), (HEM + 0.05, 0.235, 0.176),
         (HEM, 0.238, 0.178)]


def skirt_shape(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    # Heavy folds hanging; the hem rotted ragged, up in tongues.
    d = 0.006 * math.sin(a * 9 + 0.7) * smooth01(HIP, HEM, p.z)
    if i == len(SKIRT) - 1:
        p.z += 0.05 * max(0.0, noise3(Vector((math.cos(a), math.sin(a), 0)) * 3, 74, 1.0)) + 0.015 * abs(math.sin(a * 11))
    return p + out * d


coat.tube([Vector((0, -0.005, z)) for z, _, _ in SKIRT], [(rx, ry) for _, rx, ry in SKIRT], 20, COAT, skirt_weights, ref=(0, 1, 0), shape=skirt_shape)
# The collar turned up high round the scarf.
coat.tube([Vector((0, -0.01, TOP - 0.01)), Vector((0, -0.006, TOP + 0.06)), Vector((0, -0.004, TOP + 0.12))], [(0.115, 0.1), (0.104, 0.096), (0.11, 0.1)], 16,
          COAT, ["spine_03", "spine_03", "neck"], ref=(0, 1, 0), shape=lambda i, j, a, p, fr: Vector(p) if math.cos(a) < 0.85 or i == 0 else Vector(p) - Vector((0, 0.02, 0)))
# The buttons: two rows of four down the front, brass gone green.
for row, x in (("r", 0.07), ("l", -0.07)):
    for k in range(4):
        z = WAIST + 0.04 + k * 0.095
        y = 0.124 + 0.006 * (k > 1)
        coat.blob(Vector((x, y, z)), (0.011, 0.005, 0.011), 8, 4, BRASS, ["spine_01", "spine_02", "spine_03"][min(2, k * 3 // 4)])

# The scarf, wound up high round the throat, its tail hanging down the front inside the collar.
scarf = kit.part("scarf")
scarf.tube([Vector((0, 0.0, TOP + 0.0)), Vector((0, 0.008, TOP + 0.045)), Vector((0, 0.012, TOP + 0.09)), Vector((0, 0.016, NECK_TOP + 0.02))],
           [(0.092, 0.088), (0.088, 0.086), (0.082, 0.082), (0.07, 0.072)], 14, SCARF, ["spine_03", "neck", "neck", "head"], ref=(0, 1, 0),
           shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.009 * math.sin(a * 3 + i * 2))
scarf.tube([Vector((0.02, 0.07, TOP + 0.04)), Vector((0.03, 0.11, TOP - 0.02)), Vector((0.034, 0.124, TOP - 0.07))], [(0.036, 0.008), (0.036, 0.008), (0.032, 0.007)],
           6, SCARF, ["neck", "spine_03", "spine_03"], ref=(0, 1, 0), cap1=True)

# ----------------------------------------------------------------------------------------------------------------
# The arms in the coat's sleeves, the cuffs wide; the hands grey, the nails dark; the watch in the left.
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
    limbs.tube([sh + Vector((-sx * 0.02, 0, 0)), sh.lerp(e, 0.5), e, e.lerp(wr, 0.6), wr - Vector((sx * 0.01, 0, 0))],
               [(0.068, 0.064), 0.056, (0.054, 0.056), 0.052, 0.056], 10, COAT, jointed(sh, e, wr, ua, la), ref=(0, 0, 1),
               shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.004 * math.sin(a * 5 + i))
    # The cuff's turn-back, and its two buttons.
    limbs.tube([wr - Vector((sx * 0.06, 0, 0)), wr - Vector((sx * 0.012, 0, 0))], [0.059, 0.06], 10, COAT, la, ref=(0, 0, 1))
    k0 = T(hd)
    limbs.tube([wr - Vector((sx * 0.02, 0, 0)), wr.lerp(k0, 0.6), k0], [(0.032, 0.014), (0.042, 0.015), (0.043, 0.013)], 8, SKIN, hd, ref=(0, 0, 1))
    for f in range(4):
        spread = (f - 1.5) * 0.021
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 0.6, 0)).normalized()
        n = 0.085 * (0.88 + 0.12 * (1 - abs(f - 1.5) / 1.5))
        pts = [base, base + along * n * 0.5, base + along * n]
        limbs.tube(pts, [0.0095, 0.0085, 0.006], 5, SKIN,
                   lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.15 else {f"fingers_{s}": 1.0}, ref=(0, 0, 1), cap1=True)
        limbs.tube([pts[-1] - along * 0.012 + Vector((0, 0, 0.005)), pts[-1] + Vector((0, 0, 0.004))], [(0.006, 0.002), (0.005, 0.0018)], 4, PEAK,
                   f"fingers_{s}", ref=(0, 0, 1))
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.3], [0.011, 0.009, 0.006], 5, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1=True)
# The watch: a brass case, its glass, under the left palm (folded in the fingers when the hand hangs), its fob dangling.
WA = T("hand_l") + Vector((0.01, 0.0, -0.022))
limbs.blob(WA, (0.024, 0.024, 0.008), 12, 5, BRASS, "hand_l")
limbs.blob(WA + Vector((0, 0, -0.006)), (0.019, 0.019, 0.003), 12, 3, EYE, "hand_l")
fob = [WA + Vector((0.024, 0.0, 0.0)), WA + Vector((0.04, 0.0, -0.03)), WA + Vector((0.045, 0.0, -0.07)), WA + Vector((0.042, 0.004, -0.1))]
limbs.tube(fob, [0.0025] * 4, 4, BRASS, "hand_l", ref=(0, 1, 0))

# The legs below the coat: trousers, and old laced boots to the ankle.
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, -0.1)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.5), an + Vector((0, 0, 0.05))], [0.07, 0.062, (0.055, 0.056), 0.05, 0.046], 10,
               TROUSER, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0))
    limbs.tube([an + Vector((0, -0.02, 0.07)), an + Vector((0, -0.01, 0.0)), an.lerp(bl, 0.5) + Vector((0, 0, -0.01)), bl + Vector((0, 0, -0.005)),
                toe + Vector((0, -0.01, -0.01))],
               [(0.05, 0.05), (0.046, 0.05), (0.046, 0.04), (0.046, 0.032), (0.038, 0.022)], 10, BOOT, [f"calf_{s}", f"foot_{s}", f"foot_{s}", f"ball_{s}", f"ball_{s}"],
               ref=(0, 0, 1), cap0=True, cap1=True)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/crew.py: an arm lowered from the T-pose by +Y (the right; mirror() the left) and swung
# forward by -X... (rig.rot: + X swings a hanging limb forward); the elbow by Z; the spine forward by -X.
def arm_to(pose, s, target, curl=0.6):
    """The arm on side s reaching `target`, from a spread of starts (rig.reach sticks from the T-pose)."""
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


# Stood as the crew stand (tools/blender/crew.py STAND), the left hand closed on its watch.
STAND = mirror({
    "spine_01": (-2, 0, 0), "spine_02": (-3, 0, 0), "spine_03": (-5, 0, 0), "neck": (8, 0, 0), "head": (-3, 0, 0),
    "clavicle_r": (0, 9, 4), "upperarm_r": (6, 72, 4), "lowerarm_r": (0, 0, 16), "hand_r": (0, 6, 0),
    "fingers_r": (0, 35, 0), "thumb_r": (0, 15, 0),
    "thigh_r": (1, 0, 0), "calf_r": (-2, 0, 0), "foot_r": (1, 0, -6),
}) | {"fingers_l": (0, -70, 0), "thumb_l": (0, -30, 0)}

# Stand (8 s, loop): stood near the rear like crew, but dead still: no breath, no shift of weight. Then the head turns,
# slowly, to look along the car; holds; snaps back. Then the watch comes up to the face, and it looks at it, a long
# time; and down.
stand = Clip("stand")
stand.key(0, STAND, "CONSTANT")
stand.key(60, STAND, "BEZIER")
stand.key(105, over(STAND, neck=(8, 0, 30), head=(-6, 0, 34)), "CONSTANT")
stand.key(140, over(STAND, neck=(8, 0, 30), head=(-6, 0, 34)), "CONSTANT")
stand.key(142, STAND, "CONSTANT")
WATCH = over(STAND, neck=(2, 0, -6), head=(-18, 0, -8), upperarm_l=(40, -52, -10), lowerarm_l=(0, 0, -128), hand_l=(-30, -10, 0))
stand.key(170, STAND, "BEZIER")
stand.key(186, WATCH, "CONSTANT")
stand.key(222, WATCH, "BEZIER")
stand.key(236, STAND, "CONSTANT")
stand.close(240)

# Walk: the crew's own gait (crew.py: 1.4 m/s, two 0.7 m steps in 30 frames), but the head held level and dead still
# on it and the arms hanging at its sides, never swinging: from behind, crew; beside it, wrong.
W_CONTACT = over(STAND, pelvis__loc=(0, 0, -0.025), pelvis=(0, 0, -5), spine_02=(-5, 0, 3), spine_03=(-6, 0, 3), neck=(8, 0, -3), head=(-3, 0, -3),
                 thigh_r=(24, 0, 0), calf_r=(-4, 0, 0), foot_r=(10, 0, -6),
                 thigh_l=(-16, 0, 0), calf_l=(-18, 0, 0), foot_l=(-12, 0, 6))
W_DOWN = over(W_CONTACT, pelvis__loc=(0, 0, -0.04), thigh_r=(18, 0, 0), calf_r=(-14, 0, 0), foot_r=(2, 0, -6),
              thigh_l=(-10, 0, 0), calf_l=(-40, 0, 0), foot_l=(-20, 0, 6))
W_PASS = over(STAND, pelvis__loc=(0, 0, 0.01), pelvis=(0, 0, 0), spine_02=(-4, 0, 0), spine_03=(-6, 0, 0),
              thigh_r=(0, 0, 0), calf_r=(-4, 0, 0), foot_r=(0, 0, -6),
              thigh_l=(26, 0, 0), calf_l=(-58, 0, 0), foot_l=(8, 0, 6))
W_UP = over(W_PASS, pelvis__loc=(0, 0, 0.02), thigh_r=(-8, 0, 0), calf_r=(-4, 0, 0), foot_r=(-8, 0, -6),
            thigh_l=(30, 0, 0), calf_l=(-30, 0, 0), foot_l=(12, 0, 6))
KEEP_STILL = ("upperarm_r", "upperarm_l", "lowerarm_r", "lowerarm_l", "hand_l", "fingers_l", "thumb_l")


def still(p):
    """The arms as they hang stood (the swap would swing them over to the other side's)."""
    return p | {k: STAND[k] for k in KEEP_STILL}


walk = Clip("walk")
for f, p in ((0, W_CONTACT), (4, W_DOWN), (8, W_PASS), (11, W_UP)):
    walk.key(f, still(p))
for f, p in ((15, W_CONTACT), (19, W_DOWN), (23, W_PASS), (26, W_UP)):
    walk.key(f, still(swap(p) | {"neck": p["neck"], "head": p["head"]}))
walk.close(30)

# Drag (1.07 s a cycle at 1.3 m/s, loop): leant forward into it, hauling. The right hand back and down behind it in
# someone's collar at knee height (CreatureArt draws it a stride ahead of the one it's dragging), the elbow locked;
# short heavy steps, the body jerking forward at each; the head level, as ever, looking ahead.
LEAN = over(STAND, spine_01=(-16, 0, 0), spine_02=(-10, 0, -4), spine_03=(-6, 0, -6), neck=(20, 0, 6), head=(10, 0, 4))
COLLAR = Vector((0.22, -0.66, 0.52))
drag = Clip("drag")
for f, p, jerk in ((0, W_CONTACT, 1.0), (8, W_PASS, 0.0), (16, swap(W_CONTACT), 1.0), (24, swap(W_PASS), 0.0)):
    q = over(LEAN | {k: v for k, v in p.items() if k.startswith(("thigh", "calf", "foot", "pelvis"))},
             spine_01=(-16 - 5 * jerk, 0, 0), spine_02=(-10 - 3 * jerk, 0, -4))
    q = arm_to(q, "r", COLLAR + Vector((0, 0.04 * jerk, 0.02 * jerk)), curl=1.0)
    q |= {k: STAND[k] for k in ("upperarm_l", "lowerarm_l", "hand_l", "fingers_l", "thumb_l")}
    drag.key(f, q, "LINEAR" if jerk else "BEZIER")
drag.close(32)

# Pin (2 s, loop): at the car's end, bent over the coupling: the left hand still back in the collar behind; the right
# down on the lever at its knee, heaving up on it in jerks, held, and again.
BENT = over(STAND, pelvis=(-34, 0, 0), spine_01=(-14, 0, 0), spine_02=(-10, 0, 0), spine_03=(-6, 0, 0), neck=(28, 0, 0), head=(14, 0, 0),
            thigh_r=(52, 0, 6), calf_r=(-70, 0, 0), foot_r=(18, 0, -6), thigh_l=(30, 0, -6), calf_l=(-40, 0, 0), foot_l=(10, 0, 6))
LEVER = Vector((0.12, 0.42, 0.42))
pin = Clip("pin")
for f, k, interp in ((0, 0.0, "CONSTANT"), (10, 0.0, "LINEAR"), (13, 1.0, "CONSTANT"), (24, 1.0, "LINEAR"), (27, 0.2, "CONSTANT"),
                     (36, 0.2, "LINEAR"), (38, 1.0, "CONSTANT"), (52, 1.0, "BEZIER")):
    q = over(BENT, spine_02=(-10 + 8 * k, 0, 4 * k), spine_03=(-6 + 6 * k, 0, 0))
    q = arm_to(q, "r", LEVER + Vector((0, -0.02 * k, 0.12 * k)), curl=1.0)
    q = arm_to(q, "l", Vector((-0.22, -0.55, 0.6)), curl=1.0)
    pin.key(f, q, interp)
pin.close(60)

hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(2, over(STAND, spine_02=(8, 0, 14), neck=(16, 0, 10), head=(20, 0, 26)), "CONSTANT")
hit.key(9, over(STAND, spine_02=(-6, 0, -4)), "LINEAR")
hit.key(13, STAND, "CONSTANT")

kit.build()
rig.bake(sk, [stand, walk, drag, pin, hit], plant=rig.feet_planter(sk, lowest=0.02, clips=["stand", "walk", "drag", "pin", "hit"]))
print("[dt] passenger", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "passenger.glb", kit)
