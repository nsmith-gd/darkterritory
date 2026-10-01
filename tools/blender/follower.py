"""THE FOLLOWERS (GDD v1.2 §21 facility grounds, App. A.6 · scent): "A hand-sized parasite that rides on your back. You
can't see it; your friends can, if they look. It drops off aboard and nests in your best loot car."

Not a hand (note 134). A tick, grown to the size of one and bloated: the body a swollen leathery sac, grey-white and
blue where it's stretched, mottled, ridged across with the folds it hasn't filled yet; at its front a hard shield the
red-brown of dried blood, and on the shield a cluster of eyes, too many, small and black and wet, bunched like roe. Under
the shield its mouthparts, barbed, working. Ten legs, too many, thin and jointed and red-brown, hooked at the tips. On
someone's back it lies flat between their shoulder blades with every leg dug in, and twitches (the tell); nesting, it
swells over the loot it's feeding on, pulsing, the legs kneading.

SK_Follower: root, the sac, the shield, the mouthparts, and the ten legs' two bones each. Faces +Y (the engine's -Z),
belly down. Clips (§31: still, then too fast): cling (on a back: flat, gripping, dead still but the sac's slow pulse,
then the twitch), crawl (scuttling, the legs in two alternating sets, too fast), nest (spread flat over what it's
feeding on, the sac swelling and easing, the legs kneading, the mouthparts working), hit.

    tools/models/build.sh follower        # this, its high copy and the bake -> content/art/models/follower.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3  # noqa: E402

rig.reset()
SAC = Vector((0, -0.025, 0.045))        # the sac's middle
SR = (0.058, 0.078, 0.04)               # its radii
SHIELD = Vector((0, 0.05, 0.04))
# Each pair of legs: where it leaves the body (y), its splay (degrees: + forward), and its two bones' lengths.
PAIRS = [(0.06, 50, (0.07, 0.08)), (0.045, 22, (0.075, 0.085)), (0.03, -4, (0.075, 0.085)), (0.015, -28, (0.07, 0.08)), (0.0, -52, (0.065, 0.075))]
bones = [Bone("root", None, (0, 0, 0), (0, 0.05, 0)),
         Bone("sac", "root", tuple(SAC + Vector((0, 0.04, 0))), tuple(SAC - Vector((0, 0.07, 0)))),
         Bone("shield", "root", tuple(SHIELD - Vector((0, 0.03, 0))), tuple(SHIELD + Vector((0, 0.03, 0)))),
         Bone("jaws", "shield", tuple(SHIELD + Vector((0, 0.03, -0.012))), tuple(SHIELD + Vector((0, 0.06, -0.02))))]
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for k, (y, splay, (a, b)) in enumerate(PAIRS):
        d = Vector((math.cos(math.radians(splay)) * sx, math.sin(math.radians(splay)), 0))
        hip = Vector((sx * 0.03, y, 0.032))
        knee = hip + d * a + Vector((0, 0, 0.035))
        tip = knee + d * (b * 0.6) - Vector((0, 0, knee.z - 0.0))
        tip = knee + (tip - knee).normalized() * b
        bones.append(Bone(f"femur_{k + 1}{s}", "shield" if k < 2 else "root", tuple(hip), tuple(knee)))
        bones.append(Bone(f"tibia_{k + 1}{s}", f"femur_{k + 1}{s}", tuple(knee), tuple(tip)))
        TIPS.append(f"tibia_{k + 1}{s}")
sk = Skeleton("SK_Follower", bones)
sk.build()
kit = rig.Kit(sk, "follower")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


SKIN = Mat("skin.follower", hexc("#a8a8a6"), shine=0.4)
PLATE = Mat("skin.follower_shield", hexc("#4a2a22"), shine=0.5)
LEG = Mat("skin.follower_leg", hexc("#3e2620"), shine=0.4)
EYE = Mat("tar.follower_eye", hexc("#050505"), shine=0.95)
JAW = Mat("tar.follower_mouth", hexc("#2a0c0a"), shine=0.6)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


# --- the sac: swollen, ridged across with its unfilled folds, a groove down its back -------------------------------
body = kit.part("body")


def sac(i, j, a, th, p):
    p = Vector(p)
    d = p - SAC
    up = d.z / SR[2]
    # Flat underneath (it lies on what it's on), the folds across its back, a groove down the middle.
    if up < -0.3:
        p.z = SAC.z - SR[2] * (0.3 + 0.7 * (1 - math.exp(up + 0.3)) * 0.5)
    folds = 0.003 * max(0.0, math.sin(d.y * 140)) ** 2 * smooth01(-0.2, 0.4, up)
    groove = -0.004 * bell(d.x / 0.008) * smooth01(0.3, 0.8, up)
    n = Vector((d.x / SR[0] ** 2, d.y / SR[1] ** 2, d.z / SR[2] ** 2)).normalized()
    return p + n * (folds + groove + 0.0012 * noise3(p * 60, 161, 1.0))


body.blob(SAC, SR, 20, 14, SKIN, {"sac": 1.0}, shape=sac)
# The shield: a hard plate over its front, the eyes bunched on it, the mouthparts under its lip.
front = kit.part("front")


def plate(i, j, a, th, p):
    p = Vector(p)
    d = p - SHIELD
    if d.z < 0:
        p.z = SHIELD.z + d.z * 0.4
    return p + Vector((0, 0, 0.0008 * noise3(p * 90, 162, 1.0)))


front.blob(SHIELD, (0.034, 0.036, 0.016), 14, 8, PLATE, {"shield": 1.0}, shape=plate)
EYES = [(0.0, 0.074, 0.05, 0.0062), (0.009, 0.071, 0.051, 0.005), (-0.009, 0.071, 0.051, 0.0052), (0.005, 0.064, 0.055, 0.0045),
        (-0.006, 0.065, 0.055, 0.0048), (0.0, 0.058, 0.057, 0.0042), (0.014, 0.066, 0.049, 0.0038), (-0.014, 0.066, 0.049, 0.004),
        (0.011, 0.058, 0.054, 0.0034), (-0.011, 0.059, 0.054, 0.0036), (0.004, 0.077, 0.045, 0.0036), (-0.004, 0.077, 0.045, 0.0034)]
for x, y, z, r in EYES:
    front.blob(Vector((x * 1.2, y, z - 0.003)), (r * 1.45, r * 1.45, r * 1.45), 8, 6, EYE, {"shield": 1.0})
# The mouthparts: a barbed beak between two palps.
beak0, beak1 = H("jaws"), T("jaws")
front.tube([beak0, beak0.lerp(beak1, 0.5), beak1], [0.008, 0.006, 0.0012], 6, JAW, "jaws", ref=(0, 0, 1),
           shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.0015 * (j % 2))
for sx in (1, -1):
    p0 = beak0 + Vector((sx * 0.011, 0, 0.003))
    front.tube([p0, p0 + Vector((sx * 0.004, 0.018, -0.004)), p0 + Vector((sx * 0.002, 0.03, -0.01))], [0.0055, 0.005, 0.003], 6, PLATE, "jaws", ref=(0, 0, 1))

# --- the legs: thin, jointed, red-brown, hooked ----------------------------------------------------------------
legs = kit.part("legs")
for s in ("r", "l"):
    for k in range(len(PAIRS)):
        fe, ti = f"femur_{k + 1}{s}", f"tibia_{k + 1}{s}"
        legs.tube([H(fe), H(fe).lerp(T(fe), 0.5), T(fe)], [0.0055, 0.005, 0.0045], 6, LEG, fe, ref=(0, 0, 1))
        legs.blob(T(fe), (0.005, 0.005, 0.005), 6, 4, LEG, {fe: 0.5, ti: 0.5})
        legs.tube([H(ti), H(ti).lerp(T(ti), 0.5), H(ti).lerp(T(ti), 0.85), T(ti)], [0.0042, 0.0035, 0.0025, 0.0006], 6, LEG, ti, ref=(0, 0, 1))


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): a right leg (out along +X) swings forward by +Z and lifts by -Y;
# mirror() gives the left. The feet planter keeps its lowest hook on whatever it's on.
def legs_pose(swing=lambda k: 0.0, lift=lambda k: 0.0, bend=lambda k: 0.0, side="both"):
    """Each pair k (0..4): `swing` (+ forward), `lift` (+ up), `bend` (+ the tibia folded in under)."""
    p = {}
    for k in range(len(PAIRS)):
        p[f"femur_{k + 1}r"] = (0, -lift(k), swing(k))
        p[f"tibia_{k + 1}r"] = (0, bend(k), 0)
    return mirror(p)


# Cling (4 s, loop): flat on a back, every leg spread and its hook dug in; dead still but the sac's slow pulse and the
# mouthparts; then the twitch: every leg clenches at once and lets go, the sac jerking.
FLAT = legs_pose(lift=lambda k: -18, bend=lambda k: -62)
cling = Clip("cling")
cling.key(0, FLAT | {"sac@scale": (1, 1, 1)}, "BEZIER")
cling.key(40, FLAT | {"sac@scale": (1.06, 1.03, 1.1), "jaws": (-10, 0, 0)}, "BEZIER")
cling.key(70, FLAT | {"sac@scale": (0.98, 1, 0.95)}, "BEZIER")
cling.key(90, legs_pose(lift=lambda k: -26, bend=lambda k: -30, swing=lambda k: 6 if k < 2 else -6) | {"sac@scale": (0.95, 1.05, 1.15)}, "CONSTANT")
cling.key(93, legs_pose(lift=lambda k: -22, bend=lambda k: -48) | {"sac@scale": (1.02, 1, 1.0)}, "CONSTANT")
cling.key(96, legs_pose(lift=lambda k: -28, bend=lambda k: -24, swing=lambda k: -4 if k < 2 else 4) | {"sac@scale": (0.95, 1.05, 1.12)}, "CONSTANT")
cling.key(101, FLAT | {"sac@scale": (1, 1, 1)}, "BEZIER")
cling.close(120)

# Crawl (0.4 s, loop): up on its hooks, the sac carried just off the ground, scuttling: the legs in two sets (1, 3, 5 on
# the right with 2, 4 on the left, and the other way), each set lifted and thrown forward in turn, too fast; the sac
# swaying with it.
STAND = legs_pose(lift=lambda k: 18, bend=lambda k: 30)
crawl = Clip("crawl")
for f, ph in ((0, 0.0), (3, 0.25), (6, 0.5), (9, 0.75)):
    p = {}
    for s, sx in (("r", 1), ("l", -1)):
        for k in range(len(PAIRS)):
            a = ((k % 2 == 0) == (s == "r"))
            u = 2 * math.pi * (ph + (0.0 if a else 0.5))
            lift = 18 + 16 * max(0.0, math.sin(u))
            swing = 16 * math.cos(u)
            p[f"femur_{k + 1}{s}"] = (0, -lift * sx, swing * sx)
            p[f"tibia_{k + 1}{s}"] = (0, 30 * sx, 0)
    p["root"] = (0, 4 * math.sin(2 * math.pi * ph), 0)
    p["sac@scale"] = (1, 1, 1)
    crawl.key(f, p, "LINEAR")
crawl.close(12)

# Nest (3 s, loop): spread flat over what it's feeding on, the sac swelling and easing (the engine swells it with the
# nest besides), the legs kneading one pair after another, the mouthparts working, buried.
nest = Clip("nest")
for f in range(0, 90, 9):
    ph = 2 * math.pi * f / 90
    knead = lambda k, ph=ph: 30 * max(0.0, math.sin(ph * 2 - k * 1.2))
    p = legs_pose(lift=lambda k: -18, bend=lambda k, kn=knead: -62 + kn(k), swing=lambda k: 4 * math.sin(ph * 2 - k))
    p |= {"sac@scale": (1 + 0.1 * math.sin(ph), 1 + 0.05 * math.sin(ph), 1 + 0.16 * math.sin(ph)), "jaws": (-25 + 15 * math.sin(ph * 6), 0, 0),
          "shield": (-8, 0, 0)}
    nest.key(f, p, "BEZIER")
nest.close(90)

hit = Clip("hit", loop=False)
hit.key(0, FLAT | {"sac@scale": (1, 1, 1)}, "CONSTANT")
hit.key(2, legs_pose(lift=lambda k: 40, bend=lambda k: -20) | {"sac@scale": (1.15, 0.9, 0.8), "root": (-20, 10, 0)}, "CONSTANT")
hit.key(8, legs_pose(lift=lambda k: -10, bend=lambda k: 70) | {"sac@scale": (0.95, 1, 1.05)}, "LINEAR")
hit.key(14, FLAT | {"sac@scale": (1, 1, 1)}, "CONSTANT")

kit.build()
rig.bake(sk, [cling, crawl, nest, hit], plant=rig.feet_planter(sk, bones=tuple(TIPS), lowest=0.002))
print("[dt] follower", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "follower.glb", kit)
