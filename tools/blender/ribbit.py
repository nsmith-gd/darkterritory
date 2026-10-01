"""RIBBITS (GDD v1.2 §21 yards and villages, App. A.6 · sight): "Giant toad-rabbits in packs of two to four. Catch
someone alone and their tongues freeze them in place while the pack hops over to eat."

A toad the size of a big dog, sat up on a rabbit's haunches: hairless, the skin grey-white and wet as something that
lives under a stone, warted down the back, the belly paler and loose. Long rabbit's ears with no fur on them, bare skin
veined pink, hanging down its back. Two bulging eyes gone milky, set high, that don't blink. A mouth across the whole
of its head, and in it a row of flat human teeth. Under its jaw the throat sac: the tell, swelling huge and pale as the
pack lines up. Small human hands on its front legs; long rabbit feet behind.

Its own rig (SK_Ribbit): root, pelvis, spine, chest, neck, head, jaw, throat (the sac, scaled), tongue (two bones),
ears (two bones each, they droop and swing), front legs (upperarm, lowerarm, hand), hind legs (thigh, calf, foot, toe).
Sat (its rest pose), facing +Y (the engine's -Z). The tongue's long shot is drawn by the engine (CreatureArt), from the
mouth to whoever it's got. Clips (GDD §31: still, then too fast): sit (dead still but for the throat; the ears twitch),
hop (a leap and a sit, 1 s: the sim's half-second bursts), swell (the telegraph: up tall, the sac swelling and
pulsing), tongue (mouth gaping, braced, reeling in), hit.

    tools/models/build.sh ribbit        # this, its high copy and the bake -> content/art/models/ribbit.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over  # noqa: E402

rig.reset()


def ribbit_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.34, 0.34), (0, -0.14, 0.4)),
        Bone("spine_01", "pelvis", (0, -0.14, 0.4), (0, 0.06, 0.47)),
        Bone("chest", "spine_01", (0, 0.06, 0.47), (0, 0.26, 0.55)),
        Bone("neck", "chest", (0, 0.26, 0.55), (0, 0.36, 0.62)),
        Bone("head", "neck", (0, 0.36, 0.62), (0, 0.66, 0.66)),
        Bone("jaw", "head", (0, 0.38, 0.6), (0, 0.65, 0.58)),
        # (Its head at the top of the sac, under the jaw: scaled, the sac swells down over the chest and out, not up.)
        Bone("throat", "jaw", (0, 0.45, 0.57), (0, 0.45, 0.47)),
        Bone("tongue_01", "jaw", (0, 0.44, 0.6), (0, 0.53, 0.6)),
        Bone("tongue_02", "tongue_01", (0, 0.53, 0.6), (0, 0.62, 0.6)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        b += [
            # Long, and limp: back off the skull and hanging down its back and sides.
            Bone(f"ear_{side}_01", "head", (sx * 0.07, 0.42, 0.73), (sx * 0.13, 0.26, 0.76)),
            Bone(f"ear_{side}_02", f"ear_{side}_01", (sx * 0.13, 0.26, 0.76), (sx * 0.24, 0.14, 0.42)),
            # The front legs a crawling man's arms: the elbows out, the hands flat on the ground, the fingers spread.
            Bone(f"upperarm_{side}", "chest", (sx * 0.14, 0.22, 0.44), (sx * 0.27, 0.27, 0.27)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.27, 0.27, 0.27), (sx * 0.22, 0.37, 0.04)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.22, 0.37, 0.04), (sx * 0.24, 0.46, 0.015)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.19, -0.26, 0.34), (sx * 0.26, 0.0, 0.24)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.26, 0.0, 0.24), (sx * 0.26, -0.3, 0.09)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.26, -0.3, 0.09), (sx * 0.26, 0.04, 0.025)),
            Bone(f"toe_{side}", f"foot_{side}", (sx * 0.26, 0.04, 0.025), (sx * 0.26, 0.14, 0.015)),
        ]
    return Skeleton("SK_Ribbit", b)


sk = ribbit_skeleton()
sk.build()
kit = rig.Kit(sk, "ribbit")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


SKIN = Mat("skin.ribbit", hexc("#8d807c"), shine=0.6)
BELLY = Mat("skin.ribbit_belly", hexc("#a4958f"), shine=0.6)
EAR = Mat("skin.ribbit_ear", hexc("#a69088"), shine=0.4)
EYE = Mat("glass_dirty.ribbit_eye", hexc("#c8c6bc"), shine=0.95)
GUM = Mat("flesh.ribbit_gum", hexc("#5a3434"), shine=0.5)
TEETH = Mat("skin.ribbit_teeth", hexc("#c2b89c"), shine=0.4)
MOUTH = Mat("tar.ribbit_mouth", hexc("#1a0a0a"), shine=0.8)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------------------------------------------
# The body: a squat sack of a toad, wide and low at the rump where the haunches are, the back warted, the belly hanging.
body = kit.part("body")
SPINE = along("y", [(-0.46, "pelvis"), (-0.3, "pelvis"), (-0.1, "spine_01"), (0.1, "chest"), (0.26, "chest"),
                    (0.34, "neck"), (0.42, "head")])


def body_weights(p):
    w = SPINE(p)
    # The haunches go with the thighs.
    if p.y < -0.05 and p.z < 0.42 and abs(p.x) > 0.12:
        k = smooth01(0.12, 0.26, abs(p.x)) * smooth01(-0.05, -0.2, p.y) * 0.8
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def warts(i, j, ang, p):
    """Starved under loose skin: the spine's knuckles in a ridge down the back, the hips' bones standing either side of
    it, ribs, lumps and warts; under it all, the belly's skin hanging slack."""
    p = Vector(p)
    top = smooth01(0.3, 0.55, p.z)
    n = noise3(p * 9.0, 7, 1.0)
    out = Vector((p.x, 0, p.z - 0.33)).normalized() if abs(p.x) + abs(p.z - 0.33) > 1e-6 else Vector((0, 0, 1))
    d = 0.014 * top * max(0.0, n - 0.1) + 0.02 * noise3(p * 3.0, 11, 1.0)
    d += 0.03 * bell(p.x / 0.05) * top * (0.6 + 0.4 * max(0.0, math.sin(p.y * 70)))       # the spine's ridge
    d += 0.035 * bell((abs(p.x) - 0.12) / 0.05) * bell((p.y + 0.3) / 0.07) * top            # the hips' bones
    d -= 0.018 * bell((abs(p.x) - 0.24) / 0.08) * top * smooth01(-0.25, 0.0, p.y) * max(0.0, math.sin(p.y * 45)) ** 2  # between the ribs
    belly = smooth01(0.24, 0.08, p.z)
    p = p + out * d
    p.z -= 0.06 * belly * bell(p.x / 0.22) * bell((p.y + 0.05) / 0.25)                    # the slack belly
    return p


SECS = [(-0.47, 0.04, 0.36, 0.3, 0.9), (-0.43, 0.15, 0.47, 0.16), (-0.32, 0.23, 0.52, 0.1), (-0.14, 0.25, 0.55, 0.1),
        (0.04, 0.25, 0.59, 0.12), (0.18, 0.24, 0.63, 0.2), (0.3, 0.22, 0.66, 0.3), (0.4, 0.19, 0.67, 0.4)]
body.sections(SECS, 22, SKIN, body_weights, axis="y", sq=0.75, shape=warts,
              fmat=lambda pts, n: BELLY if sum(q.z for q in pts) / len(pts) < 0.2 else SKIN)
# The haunches: a rabbit's, big and bunched over folded hind legs.
for s, sx in (("r", 1), ("l", -1)):
    body.blob(Vector((sx * 0.2, -0.18, 0.3)), (0.09, 0.17, 0.13), 10, 7, SKIN, f"thigh_{s}",
              shape=lambda i, j, a, th, p: Vector(p) + Vector((0, 0, 0.015 * noise3(Vector(p) * 8, 3, 1.0))))


# ----------------------------------------------------------------------------------------------------------------
# The head: wide and flat, the mouth a slit right across it with a row of human teeth in it; the eyes up on top,
# bulging, milky; the throat's sac under the jaw; the long bare ears.
head = kit.part("head")
HC = Vector((0, 0.49, 0.645))
HR = Vector((0.175, 0.17, 0.07))


def skullcap(i, j, ang, th, p):
    p = Vector(p)
    d = p - HC
    # Flat on top, a ridge over each eye, the snout blunt and wide.
    if d.z > 0:
        p.z = HC.z + d.z * 0.85
    p.z += 0.012 * bell((abs(d.x) - 0.11) / 0.05) * bell((d.y + 0.01) / 0.06) * (d.z > 0)
    return p


head.blob(HC, tuple(HR), 22, 14, SKIN, "head", shape=skullcap,
          fmat=lambda pts, n: BELLY if sum((q.z for q in pts)) / len(pts) < HC.z - 0.03 else SKIN)
# The lower jaw, a wide flat scoop under the head, hinged at the back.
JC = Vector((0, 0.49, 0.585))
head.blob(JC, (0.175, 0.15, 0.032), 20, 8, BELLY, "jaw")
# The gums along the upper jaw, the mouth's dark line under them; the teeth: flat, square, human, a row round the jaw.
GUMLINE = [Vector((math.sin(math.radians(a)) * 0.175, HC.y + math.cos(math.radians(a)) * 0.16, 0.616)) for a in range(-84, 85, 12)]
head.tube(GUMLINE, [0.012] * len(GUMLINE), 6, GUM, "head", ref=(0, 0, 1))
head.tube([Vector((p.x * 0.97, p.y - 0.005, 0.598)) for p in GUMLINE], [0.008] * len(GUMLINE), 5, MOUTH, "jaw", ref=(0, 0, 1))
TEETH_AT = []
for k in range(16):
    a = math.radians(-76 + 152 * k / 15)
    c = Vector((math.sin(a) * 0.168, HC.y + math.cos(a) * 0.152, 0.601))
    TEETH_AT.append(c)
    t = Vector((math.cos(a), -math.sin(a), 0))
    # Human teeth, but wrong: crooked, uneven, a few gone.
    if k in (3, 11):
        continue
    w = (0.011 if abs(a) < 0.6 else 0.008) * (0.8 + 0.4 * (0.5 + 0.5 * math.sin(k * 2.7)))
    tilt = Vector((0.004 * math.sin(k * 1.9), 0.003 * math.cos(k * 2.3), 0))
    long_ = 0.012 + 0.006 * (0.5 + 0.5 * math.sin(k * 3.1))
    head.tube([c + Vector((0, 0, 0.008)), c - Vector((0, 0, long_)) + tilt], [(w, 0.006), (w * 0.85, 0.005)], 4, TEETH, "head",
              ref=(math.sin(a), math.cos(a), 0))
# The eyes: up on top, bulging out of their own mounds, milky.
EYES = {}
for s, sx in (("r", 1), ("l", -1)):
    mound = Vector((sx * 0.11, 0.5, 0.71))
    head.blob(mound, (0.06, 0.06, 0.045), 12, 7, SKIN, "head")
    e = mound + Vector((sx * 0.012, 0.012, 0.022))
    EYES[s] = e
    head.blob(e, (0.034, 0.034, 0.03), 12, 8, EYE, "head")
    # A heavy lid over it, half down: the eye's never all there.
    head.blob(e + Vector((0, -0.004, 0.012)), (0.04, 0.04, 0.022), 12, 6, SKIN, "head")
# The throat's sac, under the jaw: loose skin, folded, that swells (the throat bone's scale).
THROAT = Vector((0, 0.46, 0.53))
head.blob(THROAT, (0.11, 0.1, 0.055), 14, 8, BELLY, "throat",
          shape=lambda i, j, a, th, p: Vector(p) + Vector((0, 0, 0.006 * math.sin(Vector(p).x * 60))))
# The tongue, curled in the mouth's floor: the engine draws it out.
head.tube([H("tongue_01"), H("tongue_02"), T("tongue_02")], [(0.04, 0.012), (0.035, 0.011), (0.02, 0.008)], 8, GUM,
          lambda p: {"tongue_01": 1.0} if p.y < H("tongue_02").y else {"tongue_02": 1.0}, ref=(0, 0, 1), cap1=True)
# The ears: long, flat, bare, hanging down its back.
for s, sx in (("r", 1), ("l", -1)):
    e0, e1, e2 = H(f"ear_{s}_01"), H(f"ear_{s}_02"), T(f"ear_{s}_02")
    pts = [e0, e0.lerp(e1, 0.5), e1, e1.lerp(e2, 0.5), e2]
    head.tube(pts, [(0.035, 0.009), (0.06, 0.01), (0.068, 0.01), (0.058, 0.009), (0.018, 0.006)], 8, EAR,
              lambda p, s=s, e1=e1: {f"ear_{s}_01": 1.0} if p.y > e1.y + 0.02 else {f"ear_{s}_02": 1.0},
              ref=(sx, 0, 0.3), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Legs: the front thin with small human hands, four fingers and a thumb; the hind a rabbit's, folded under, long feet.
legs = kit.part("legs")
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    legs.tube([sh, sh.lerp(el, 0.5), el, el.lerp(wr, 0.5), wr], [0.045, 0.032, 0.028, 0.022, 0.02], 8, SKIN,
              lambda p, el=el, s=s: {f"upperarm_{s}": 1.0} if p.z > el.z + 0.02 else {f"lowerarm_{s}": 1.0}, ref=(1, 0, 0))
    legs.blob(wr.lerp(tip, 0.3), (0.03, 0.04, 0.012), 8, 4, BELLY, f"hand_{s}")
    for f in range(4):
        a = math.radians((f - 1.5) * 16)
        base = wr.lerp(tip, 0.55) + Vector((math.sin(a) * 0.02, 0, -0.006))
        d = Vector((math.sin(a), math.cos(a), -0.15)).normalized()
        n = 0.06 * (0.8 + 0.2 * (1 - abs(f - 1.5) / 1.5))
        legs.tube([base, base + d * n * 0.55, base + d * n - Vector((0, 0, 0.01))], [0.007, 0.006, 0.004], 5, BELLY, f"hand_{s}",
                  ref=(0, 0, 1), cap1="point")
    hp, kn, an, bl, toe = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    legs.tube([kn, kn.lerp(an, 0.5), an], [0.05, 0.04, 0.03], 8, SKIN, f"calf_{s}", ref=(1, 0, 0))
    legs.tube([an, an.lerp(bl, 0.5), bl, toe], [(0.035, 0.022), (0.035, 0.016), (0.03, 0.012), (0.02, 0.008)], 8, BELLY,
              lambda p, bl=bl, s=s: {f"foot_{s}": 1.0} if p.y < bl.y - 0.02 else {f"toe_{s}": 1.0}, ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest pose is it sat; angles in the armature's axes: +X tips a bone's tail up (a thigh swings it back down
# the body, a jaw opens with -X).
SIT = {}
EARS = {"ear_r_01": (0, 0, 0), "ear_l_01": (0, 0, 0)}


def breathe(k):
    """The throat's slow pulse (sitting), k 0..1; the jaw always a little open, the teeth showing."""
    return {"throat@scale": (1 + 0.12 * k, 1 + 0.12 * k, 1 + 0.25 * k), "jaw": (-7, 0, 0)}


sit = Clip("sit")
for f, k in ((0, 0.0), (20, 1.0), (40, 0.0), (60, 1.0), (80, 0.0)):
    sit.key(f, breathe(k), "BEZIER")
sit.key(84, {"ear_r_01": (0, 0, -14), **breathe(0)}, "CONSTANT")
sit.key(87, {"ear_r_01": (0, 0, 0), **breathe(0.1)}, "CONSTANT")
sit.key(100, breathe(0.6), "BEZIER")
sit.close(120)

# Hop (1 s, loop): half a second of leap, the hind legs flung out straight behind, the body stretched long, the front
# hands reaching; half a second sat, landed heavily, dead still. (The sim moves it in its half-second bursts.)
LEAP = {"root@loc": (0, 0, 0.5), "pelvis": (-24, 0, 0), "spine_01": (-8, 0, 0), "chest": (-4, 0, 0), "head": (6, 0, 0),
        "thigh_r": (-70, 0, 0), "thigh_l": (-70, 0, 0), "calf_r": (60, 0, 0), "calf_l": (60, 0, 0), "foot_r": (-40, 0, 0),
        "foot_l": (-40, 0, 0), "upperarm_r": (-55, 0, 0), "upperarm_l": (-55, 0, 0), "lowerarm_r": (20, 0, 0),
        "lowerarm_l": (20, 0, 0), "ear_r_01": (14, 0, 0), "ear_l_01": (14, 0, 0), "ear_r_02": (30, 0, 0), "ear_l_02": (30, 0, 0)}
hop = Clip("hop")
hop.key(0, {"root@loc": (0, 0, -0.03), "pelvis": (6, 0, 0), "thigh_r": (8, 0, 0), "thigh_l": (8, 0, 0)}, "LINEAR")
hop.key(3, {**LEAP, "root@loc": (0, 0, 0.42)}, "LINEAR")
hop.key(8, LEAP, "LINEAR")
hop.key(13, {"root@loc": (0, 0, 0.06), "upperarm_r": (-20, 0, 0), "upperarm_l": (-20, 0, 0), "ear_r_01": (20, 0, 0), "ear_l_01": (20, 0, 0)}, "LINEAR")
hop.key(15, {"root@loc": (0, 0, -0.04), "pelvis": (4, 0, 0), "ear_r_01": (34, 0, 0), "ear_l_01": (30, 0, 0), "ear_r_02": (20, 0, 0), "ear_l_02": (20, 0, 0)}, "LINEAR")
hop.key(18, {"ear_r_01": (8, 0, 0), "ear_l_01": (6, 0, 0), **breathe(0.3)}, "CONSTANT")
hop.close(30)

# Swell (2 s, loop): the telegraph. Up tall on its front legs, the head raised, the sac blown up huge under it and
# pulsing, pale and veined, a croak in each pulse; the ears laid flat back.
TALL = {"pelvis": (14, 0, 0), "spine_01": (10, 0, 0), "chest": (8, 0, 0), "neck": (10, 0, 0), "head": (10, 0, 0),
        "upperarm_r": (16, 0, 0), "upperarm_l": (16, 0, 0), "ear_r_01": (-10, 0, 0), "ear_l_01": (-10, 0, 0),
        "root@loc": (0, 0, 0.05)}
swell = Clip("swell")
for f, k in ((0, 0.6), (8, 1.0), (16, 0.7), (24, 1.0), (40, 0.6)):
    swell.key(f, {**TALL, "throat@scale": (1.35 + 0.2 * k, 1.6 + 0.35 * k, 1.45 + 0.25 * k), "jaw": (-3 * k, 0, 0)}, "BEZIER")
swell.close(60)

# Tongue (1 s, loop): the mouth gaping right open, the body braced back on its haunches, jerking as it reels its
# catch in; the sac pumping. (The tongue itself, out to its catch, is the engine's.)
BRACE = {"pelvis": (-6, 0, 0), "chest": (6, 0, 0), "head": (6, 0, 0), "upperarm_r": (24, 0, 0), "upperarm_l": (24, 0, 0),
         "ear_r_01": (20, 0, 0), "ear_l_01": (20, 0, 0)}
tongue = Clip("tongue")
for f, k in ((0, 0.0), (5, 1.0), (10, 0.3), (18, 1.0), (24, 0.0)):
    tongue.key(f, {**BRACE, "jaw": (-34 - 6 * k, 0, 0), "tongue_01": (8, 0, 0), "chest": (6 + 4 * k, 0, 0),
                   "throat@scale": (1.2 + 0.4 * k, 1.2, 1.3 + 0.5 * k)}, "BEZIER" if f % 2 == 0 else "CONSTANT")
tongue.close(30)

hit = Clip("hit", loop=False)
hit.key(0, {}, "CONSTANT")
hit.key(2, {"pelvis": (-10, 0, 14), "chest": (-6, 0, 10), "head": (-10, 0, 20), "root@loc": (0, -0.06, 0.04)}, "CONSTANT")
hit.key(8, {"pelvis": (4, 0, -4)}, "LINEAR")
hit.key(12, {}, "CONSTANT")

kit.build()
rig.bake(sk, [sit, hop, swell, tongue, hit])
print("[dt] ribbit", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "ribbit.glb", kit)
