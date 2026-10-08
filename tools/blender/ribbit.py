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
pulsing), tongue (mouth gaping, braced, reeling in), creep (the leader in low on its frozen catch, the tongue still out),
devour (reared up against them, forelegs on them, the head wrenching, the jaw snapping), hit.

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
        # (Its head at the top of the sac, just under the jaw: scaled, the sac balloons down and out under the chin, bigger
        # than the head, not up into it.)
        Bone("throat", "jaw", (0, 0.45, 0.58), (0, 0.45, 0.48)),
        # The tongue: tongue_01 is all of its length (scaled along itself it shoots out, as thin as it was: the engine
        # stretches it to its catch, CreatureArt), tongue_02 the sticky club at its end (scaled back, it stays round).
        Bone("tongue_01", "jaw", (0, 0.41, 0.588), (0, 0.575, 0.588)),
        Bone("tongue_02", "tongue_01", (0, 0.575, 0.588), (0, 0.625, 0.588)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        b += [
            # Long, and limp: back off the skull and hanging down its back and sides.
            # (Lop: off the back of the skull out over the side, then hanging down past the neck, never stood up off the
            # head like a board: the Look Review.)
            Bone(f"ear_{side}_01", "head", (sx * 0.1, 0.42, 0.715), (sx * 0.19, 0.33, 0.69)),
            Bone(f"ear_{side}_02", f"ear_{side}_01", (sx * 0.19, 0.33, 0.69), (sx * 0.26, 0.2, 0.43)),
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
TONGUE = Mat("flesh.ribbit_tongue", hexc("#8a4a4c"), shine=0.9)
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
# The head: a toad's, a broad flat wedge, the mouth a slit right across it under a rolled lip with a row of human teeth
# in it; the eyes up on top, bulging out of their mounds, milky; the glands swollen behind them; the throat's sac under
# the jaw; the long bare ears. The skin of it is one surface (`skull`, fused by rig.fuse): the eye mounds, the glands,
# the lip and the ears' roots grow out of the skull; the jaw (it opens), the mouth, the eyes and the sac stay `head`.
head = kit.part("head")
skull = kit.part("skull")
HC = Vector((0, 0.49, 0.645))
# (along y, half width, top, bottom): from the neck to the blunt snout, seen from above a rounded spade.
SKULL = [(0.36, 0.15, 0.7, 0.6), (0.4, 0.175, 0.716, 0.603), (0.46, 0.186, 0.722, 0.605), (0.52, 0.181, 0.714, 0.607),
         (0.58, 0.166, 0.697, 0.608), (0.63, 0.136, 0.677, 0.61), (0.66, 0.092, 0.657, 0.612), (0.674, 0.03, 0.642, 0.618)]


def brow(i, j, ang, p):
    p = Vector(p)
    # A ridge over each eye running back to the glands; the snout's top a little dished between the nostrils.
    p.z += 0.008 * bell((abs(p.x) - 0.1) / 0.03) * bell((p.y - 0.5) / 0.08) * (p.z > 0.68)
    p.z -= 0.004 * bell(p.x / 0.03) * bell((p.y - 0.63) / 0.03)
    return p


skull.sections(SKULL, 24, SKIN, "head", sq=0.72, shape=brow, cap0=True, cap1=True)


def lip_path(scale=1.0, inset=0.0, z=0.614):
    """The mouth's line round the skull's underside, back on the left to the snout and back on the right."""
    side = [(hw * scale - inset, y - inset * 0.5) for y, hw, _, _ in SKULL[1:-1]]
    return ([Vector((-x, y, z)) for x, y in reversed(side)] + [Vector((0, SKULL[-1][0] - 0.004 - inset, z))]
            + [Vector((x, y, z)) for x, y in side])


# The upper lip, rolled and heavy, overhanging the gums.
LIP = lip_path(0.985, 0.0, 0.613)
skull.tube(LIP, [(0.016, 0.013)] * len(LIP), 8, SKIN, "head", ref=(0, 0, 1))
# The eyes' mounds, the glands behind them (a toad's paratoids, swollen and pitted), the nostrils.
EYES = {}
for s, sx in (("r", 1), ("l", -1)):
    mound = Vector((sx * 0.112, 0.505, 0.712))
    skull.blob(mound, (0.066, 0.066, 0.05), 14, 8, SKIN, "head")
    skull.blob(Vector((sx * 0.135, 0.41, 0.71)), (0.05, 0.075, 0.032), 12, 7, SKIN, "head", rot=rig.rot(rz=sx * 14).to_matrix().to_4x4())
    skull.blob(Vector((sx * 0.032, 0.652, 0.662)), (0.014, 0.012, 0.01), 8, 5, SKIN, "head")
    e = Vector((sx * 0.126, 0.52, 0.744))
    EYES[s] = e
    head.blob(e, (0.036, 0.036, 0.033), 16, 10, EYE, "head")
    # A heavy lid over the top and back of it: the eye's never all there.
    skull.blob(e + Vector((sx * -0.004, -0.012, 0.017)), (0.041, 0.037, 0.02), 12, 6, SKIN, "head")
# The lower jaw: a wide heavy scoop under the head, hinged at the back, the chin deeper, as broad as the lip over it
# and rolled at its rim (a Look Review ask: the old one was a thin tray under a ring of lip). Its own skin (`jaw`,
# fused): it opens.
jaw = kit.part("jaw")
JAW = [(0.37, 0.15, 0.607, 0.55), (0.44, 0.18, 0.605, 0.535), (0.52, 0.182, 0.603, 0.528), (0.59, 0.166, 0.603, 0.533),
       (0.64, 0.13, 0.604, 0.546), (0.665, 0.08, 0.605, 0.568), (0.676, 0.025, 0.606, 0.592)]
jaw.sections(JAW, 22, BELLY, "jaw", sq=0.78, cap0=True, cap1=True,
             fmat=lambda pts, n: SKIN if sum(q.x * q.x for q in pts) / len(pts) > 0.012 else BELLY)
LOWLIP = lip_path(0.97, 0.006, 0.596)
jaw.tube(LOWLIP, [(0.012, 0.008)] * len(LOWLIP), 8, SKIN, "jaw", ref=(0, 0, 1))
# The gums just inside the lip, the mouth's dark line under them; the teeth: flat, square, human, a row round the jaw.
GUMLINE = lip_path(0.93, 0.012, 0.606)
head.tube(GUMLINE, [0.008] * len(GUMLINE), 6, GUM, "head", ref=(0, 0, 1))
head.tube(lip_path(0.92, 0.01, 0.6), [0.007] * len(GUMLINE), 5, MOUTH, "jaw", ref=(0, 0, 1))
TEETH_AT = []
ROW = lip_path(0.935, 0.008, 0.6)
for k in range(18):
    t = 1 + k * (len(ROW) - 3) / 17
    i0 = int(t)
    c = ROW[i0].lerp(ROW[i0 + 1], t - i0)
    TEETH_AT.append(c)
    d = (ROW[i0 + 1] - ROW[i0]).normalized()
    out = Vector((d.y, -d.x, 0)) * (1 if c.x >= 0 else -1)
    # Human teeth, but wrong: crooked, uneven, a few gone.
    if k in (3, 12):
        continue
    w = (0.011 if abs(c.x) < 0.09 else 0.008) * (0.8 + 0.4 * (0.5 + 0.5 * math.sin(k * 2.7)))
    tilt = Vector((0.004 * math.sin(k * 1.9), 0.003 * math.cos(k * 2.3), 0))
    long_ = 0.013 + 0.006 * (0.5 + 0.5 * math.sin(k * 3.1))
    head.tube([c + Vector((0, 0, 0.009)), c - Vector((0, 0, long_)) + tilt], [(w, 0.006), (w * 0.85, 0.005)], 4, TEETH, "head",
              ref=(out.x, out.y, 0))
# The throat's sac, under the jaw: loose skin hanging in folds that swells (the throat bone's scale, about its top
# under the chin) into a pale veined balloon bigger than the head (the Look Review: "the swell needs to be much bigger").
THROAT = Vector((0, 0.47, 0.535))


def sac_folds(i, j, a, th, p):
    p = Vector(p)
    # Slack: creased across, sagging lowest at the front; stretched, the folds open out.
    p.z += 0.007 * math.sin(p.y * 90 + 1.5 * math.sin(p.x * 30)) * smooth01(0.56, 0.5, p.z)
    p.z -= 0.012 * smooth01(0.42, 0.56, p.y) * smooth01(0.55, 0.49, p.z)
    return p


head.blob(THROAT, (0.105, 0.1, 0.058), 18, 10, BELLY, "throat", shape=sac_folds)
# The tongue: a toad's, broad and flat and wet, a groove down its middle, lying in the mouth's floor; at its end the
# sticky club it catches with, swollen and puckered. Out, tongue_01 is scaled along its length (the club scaled back):
# the tongue shoots out as thick as it lay (clips `tongue`, `creep`; in the game CreatureArt stretches it to its catch).
t0, t1 = H("tongue_01"), T("tongue_01")
TPTS = [t0 + Vector((0, 0.004, -0.004))] + [t0.lerp(t1, k / 7) for k in range(1, 8)]
head.tube(TPTS, [(0.03, 0.011), (0.036, 0.012), (0.038, 0.012), (0.036, 0.011), (0.033, 0.01), (0.03, 0.009), (0.027, 0.009),
                 (0.026, 0.009)], 10, TONGUE, "tongue_01", ref=(0, 0, 1),
          shape=lambda i, j, a, p, fr: Vector(p) - fr[1] * (0.004 * bell(math.sin(a) / 0.25) * (math.cos(a) > 0)))
head.blob(T("tongue_01") + Vector((0, 0.022, 0.002)), (0.04, 0.034, 0.022), 12, 8, TONGUE, "tongue_02",
          shape=lambda i, j, a, th, p: Vector(p) * 1.0 + Vector((0, 0, 0.003 * math.sin(a * 5) * math.sin(th * 4))))
# The ears: a rabbit's, long and bare, cupped along their length, hanging down its back. Sheets too thin for the skin's
# remesh (`ears`, their own part), their roots sunk deep in the skull's.
ears = kit.part("ears")
for s, sx in (("r", 1), ("l", -1)):
    e0, e1, e2 = H(f"ear_{s}_01"), H(f"ear_{s}_02"), T(f"ear_{s}_02")
    # (The root sunk in the skull behind the gland and thick there, so the ear grows out of the head (Look Review: "geometry
    # that doesn't seem attached"); then the thin blade.)
    pts = [e0 + Vector((-sx * 0.02, 0.05, -0.035)), e0 + Vector((0, 0.015, -0.012)), e0.lerp(e1, 0.5), e1, e1.lerp(e2, 0.35),
           e1.lerp(e2, 0.7), e2]
    cup = [0.0, 0.004, 0.014, 0.022, 0.02, 0.014, 0.0]
    ears.tube(pts, [(0.045, 0.034), (0.04, 0.02), (0.046, 0.01), (0.054, 0.008), (0.052, 0.008), (0.04, 0.007), (0.012, 0.005)],
               12, EAR, lambda p, s=s, e1=e1: {f"ear_{s}_01": 1.0} if p.y > e1.y + 0.02 else {f"ear_{s}_02": 1.0},
               ref=(sx, 0, 0.3), cap1="point",
               shape=lambda i, j, a, p, fr, cup=cup: Vector(p) + fr[1] * (cup[i] * math.sin(a) ** 2))


# ----------------------------------------------------------------------------------------------------------------
# Legs: the front thin with small human hands, four fingers and a thumb; the hind a rabbit's, folded under, long feet.
legs = kit.part("legs")
paws = kit.part("paws")    # the hands and the long feet: fine, kept out of the fused skin
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    # (The forearm runs on into the hand's heel and the wrist is thick, so the hand is grown on, not a disc stuck on a
    # stick: the Look Review.)
    legs.tube([sh, sh.lerp(el, 0.5), el, el.lerp(wr, 0.5), wr, wr.lerp(tip, 0.3)], [0.064, 0.052, 0.042, 0.036, 0.032, 0.028],
              10, SKIN, lambda p, el=el, wr=wr, s=s: {f"upperarm_{s}": 1.0} if p.z > el.z + 0.02
              else {f"lowerarm_{s}": 1.0} if p.z > wr.z + 0.012 else {f"hand_{s}": 1.0}, ref=(1, 0, 0))
    # A small human hand, flat on the ground: the palm, four long fingers with their knuckles, the thumb turned in.
    paws.blob(wr.lerp(tip, 0.42) + Vector((0, 0, 0.004)), (0.036, 0.044, 0.017), 10, 6, BELLY, f"hand_{s}")
    for f in range(5):
        thumb = f == 4
        a = math.radians(-sx * 62 if thumb else (f - 1.5) * 15)
        base = wr.lerp(tip, 0.36 if thumb else 0.66) + Vector((math.sin(a) * (0.03 if thumb else 0.022), 0, 0.002))
        d = Vector((math.sin(a), math.cos(a), -0.05)).normalized()
        n = (0.04 if thumb else 0.062) * (1 if thumb else 0.82 + 0.18 * (1 - abs(f - 1.5) / 1.5))
        kn = base + d * n * 0.45 + Vector((0, 0, 0.006))
        paws.tube([base - d * 0.012, base, kn, base + d * n - Vector((0, 0, 0.012))], [0.011, 0.0095, 0.0085, 0.0055], 6, BELLY,
                  f"hand_{s}", ref=(0, 0, 1), cap1="point")
    hp, kn, an, bl, toe = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    # The haunch: a rabbit's thigh folded along its flank, heavy, the hop's spring in it.
    legs.tube([hp + Vector((0, -0.04, 0.03)), hp.lerp(kn, 0.45), kn], [(0.1, 0.12), (0.09, 0.1), 0.055], 12, SKIN, f"thigh_{s}", ref=(1, 0, 0))
    legs.tube([kn, kn.lerp(an, 0.5), an], [0.058, 0.048, 0.042], 8, SKIN, f"calf_{s}", ref=(1, 0, 0))
    # The long foot, a hare's: a heel that's a lump of bone, thick through the sole, and toes, grown into the leg (the
    # skin's): no more a ski laid by the leg (the Look Review). Under the haunch, the toes out in front of it.
    legs.tube([an + Vector((0, -0.03, 0.01)), an, an.lerp(bl, 0.45), bl], [(0.044, 0.042), (0.046, 0.036), (0.042, 0.026), (0.038, 0.02)],
              10, SKIN, lambda p, bl=bl, s=s: {f"foot_{s}": 1.0} if p.y < bl.y - 0.03 else {f"toe_{s}": 1.0}, ref=(0, 0, 1))
    for k in range(4):
        dx = (k - 1.5) * 0.017
        a0 = bl + Vector((dx, -0.01, -0.002))
        legs.tube([a0, a0 + Vector((dx * 0.4, 0.06, -0.003)), a0 + Vector((dx * 0.6, 0.1 - 0.012 * abs(k - 1.5), -0.012))],
                  [0.017, 0.015, 0.011], 6, BELLY, f"toe_{s}", ref=(0, 0, 1), cap1=True)


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
# (The sac about its top under the chin, in the throat bone's axes: x across, y down, z fore and aft. Blown up it's a
# pale ball bigger than the head, out in front of the chest; each pulse a croak, the jaw lifting off it.)
SAC_FULL, SAC_PULSE = (2.6, 3.2, 2.1), (0.25, 0.4, 0.2)
swell = Clip("swell")
for f, k in ((0, 0.4), (6, 1.0), (14, 0.55), (22, 1.0), (30, 0.4), (36, 0.85), (46, 0.5), (54, 1.0)):
    swell.key(f, {**TALL, "throat@scale": tuple(a + b * (k - 1) for a, b in zip(SAC_FULL, SAC_PULSE)), "jaw": (-2 - 4 * k, 0, 0),
                  "neck": (-12 + 2 * k, 0, 0), "head": (-8, 0, 0)}, "BEZIER")
swell.close(60)

# Tongue (1 s, loop): the mouth gaping right open, the body braced back on its haunches, jerking as it reels its
# catch in; the sac pumping. (The tongue itself, out to its catch, is the engine's.)
BRACE = {"pelvis": (-6, 0, 0), "chest": (6, 0, 0), "head": (6, 0, 0), "upperarm_r": (24, 0, 0), "upperarm_l": (24, 0, 0),
         "ear_r_01": (20, 0, 0), "ear_l_01": (20, 0, 0)}
# The tongue out at full stretch (TONGUE_OUT its length over its 0.165 m at rest, ~1.2 m out of the mouth, sagging),
# jerking as it reels; the club scaled back by as much, so it stays a club. In the game CreatureArt aims it at its
# catch and stretches it the rest of the way, so what the reel shows is what the player's hit by.
TONGUE_OUT = 7.0


def lash(out, droop=0.0):
    return {"tongue_01@scale": (1, out, 1), "tongue_02@scale": (1, 1 / out, 1), "tongue_01": (36 - droop, 0, 0)}


tongue = Clip("tongue")
for f, k in ((0, 0.0), (5, 1.0), (10, 0.3), (18, 1.0), (24, 0.0)):
    tongue.key(f, {**BRACE, "jaw": (-34 - 6 * k, 0, 0), "chest": (6 + 4 * k, 0, 0), **lash(TONGUE_OUT - 0.5 * k, 3 * k),
                   "throat@scale": (1.2 + 0.4 * k, 1.2, 1.3 + 0.5 * k)}, "BEZIER" if f % 2 == 0 else "CONSTANT")
tongue.close(30)

# Creep (1.6 s, loop): the leader's slow way in on its frozen catch (the sim's quarter-speed hop, App. A.6 "the pack hops
# in to eat"): down low on its belly, the mouth still gaping and the tongue out to them, the little hands placed one and
# then the other, the haunches pushing it on after them; the sac pumping, the ears laid back.
LOW = {"root@loc": (0, 0, -0.07), "pelvis": (-4, 0, 0), "spine_01": (-6, 0, 0), "chest": (-8, 0, 0), "neck": (4, 0, 0), "head": (2, 0, 0),
       "ear_r_01": (-12, 0, 0), "ear_l_01": (-12, 0, 0), **lash(4.0, 6)}
creep = Clip("creep")
for f, k in ((0, 1), (12, -1), (24, 1), (36, -1)):
    creep.key(f, {**LOW, "jaw": (-30 - 4 * (k > 0), 0, 0), "root@loc": (0, 0.03 * k, -0.07),
                  "upperarm_r": (-34 if k > 0 else 8, 0, 0), "lowerarm_r": (20 if k > 0 else 4, 0, 0),
                  "upperarm_l": (8 if k > 0 else -34, 0, 0), "lowerarm_l": (4 if k > 0 else 20, 0, 0),
                  "thigh_r": (-6 if k > 0 else 6, 0, 0), "thigh_l": (6 if k > 0 else -6, 0, 0),
                  "throat@scale": (1.2 + 0.15 * (k > 0), 1.2, 1.3 + 0.2 * (k > 0))}, "BEZIER")
creep.close(48)

# Devour (1.2 s, loop): on them (App. A.6, ~8 s of it): reared up on its haunches against its frozen catch, the front
# half lifted and leant in over them, the little hands up on them gripping, the head bent down into them, driving in and
# wrenching side to side, the jaw snapping shut and tearing back, the sac heaving. (The sim's hop stops 0.8 m short, so it
# leans the rest of the way.)
OVER = {"root@loc": (0, 0.22, 0.06), "pelvis": (28, 0, 0), "spine_01": (14, 0, 0), "chest": (8, 0, 0), "neck": (-14, 0, 0),
        "thigh_r": (-28, 0, 0), "thigh_l": (-28, 0, 0),
        "upperarm_r": (-20, 0, 0), "upperarm_l": (-20, 0, 0), "lowerarm_r": (10, 0, 0), "lowerarm_l": (10, 0, 0),
        "ear_r_01": (-16, 0, 0), "ear_l_01": (-16, 0, 0)}
devour = Clip("devour")
for f, jaw, shake, drive in ((0, -46, 0, 0), (4, -6, 16, 1), (8, -20, -14, 0.5), (12, -4, 18, 1), (17, -44, 0, 0), (22, -8, -18, 1),
                             (26, -24, 10, 0.4), (31, -5, -16, 1), (36, -46, 0, 0)):
    # (The neck bent down no further than the jaw clears the chest when it gapes: dt art clearance.)
    devour.key(f, {**OVER, "jaw": (jaw, 0, 0), "head": (-30 - 12 * drive, 0, shake), "neck": (-12 - 6 * drive, 0, shake * 0.4),
                   "root@loc": (0, 0.22 + 0.06 * drive, 0.06), "throat@scale": (1.15 + 0.2 * drive, 1.1, 1.2 + 0.25 * drive)},
               "CONSTANT" if jaw > -10 else "LINEAR")
devour.close(36)

hit = Clip("hit", loop=False)
hit.key(0, {}, "CONSTANT")
# (The ears left behind as the head snaps round, not swung through its flank: dt art clearance.)
hit.key(2, {"pelvis": (-10, 0, 14), "chest": (-6, 0, 10), "head": (-10, 0, 20), "root@loc": (0, -0.06, 0.04),
            "ear_r_01": (10, 0, -10), "ear_l_01": (10, 0, -26)}, "CONSTANT")
hit.key(8, {"pelvis": (4, 0, -4)}, "LINEAR")
hit.key(12, {}, "CONSTANT")

# One skin, the haunches and the legs grown out of the body rather than pushed into it (rig.fuse); then up toward
# GDD §27's budget (a beast's, 2-8k): rig.densify rounds the head's forms out (Look Review asks).
# (The skull fused into the body too, the Look Review: the head sat on the neck with a crease all round it.)
# (QuadriFlow smooths the groove under the upper lip over at this density, ~2% of the union: kept, for its even quads.)
kit.fuse("body", ["body", "legs", "skull"], voxel=0.003, faces=2450, lose=0.03)
kit.fuse("jaw", ["jaw"], voxel=0.0025, faces=550)
kit.target_tris = 7600
kit.build()
rig.bake(sk, [sit, hop, swell, tongue, creep, devour, hit])
print("[dt] ribbit", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "ribbit.glb", kit)
