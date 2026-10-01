"""THE FOLLOWERS (GDD v1.2 §21 facility grounds, App. A.6 · scent): "A hand-sized parasite that rides on your back. You
can't see it; your friends can, if they look. It drops off aboard and nests in your best loot car."

A hand, an infant's hand grown to a man's and gone wrong: pale, soft, too many knuckles on the fingers, the nails dirty
and split, and at the wrist no arm but a raw stump trailing threads of nerve, the way it came off whatever it grew on.
On the back of the hand, where the knuckles should meet, a mouth: round, lipless, ringed with tiny teeth, opening and
closing on nothing. It walks on its fingertips like a spider. On someone's back it lies flat between the shoulder
blades, the fingers spread and dug in, the threads of its stump gone in under their collar, and it twitches (the
tell); nesting, it swells over the loot it's eating, the mouth working.

SK_Follower: root, palm, the wrist's stump, a thumb and four fingers of three bones each, and the mouth. Faces +Y (its
fingers ahead; the engine's -Z), its palm down. Clips (§31: still, then too fast): cling (on a back: flat, gripping,
dead still, then the twitch), crawl (scuttling on its fingertips, the palm held up), nest (spread flat over what it's
eating, the mouth working, the fingers kneading), hit.

    tools/models/build.sh follower        # this, its high copy and the bake -> content/art/models/follower.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3, over  # noqa: E402

rig.reset()
PALM_Z = 0.03                                   # the palm's height off the ground at rest
# Where each finger's knuckle is (x across, y along the palm), its splay (degrees out from straight ahead), and the
# lengths of its three bones; the thumb's off the palm's side.
FINGERS = [("thumb", (0.045, 0.0), 55, (0.03, 0.025, 0.02)), ("index", (0.032, 0.062), 12, (0.036, 0.03, 0.024)),
           ("middle", (0.011, 0.068), 3, (0.04, 0.032, 0.026)), ("ring", (-0.011, 0.064), -6, (0.037, 0.03, 0.024)),
           ("little", (-0.031, 0.054), -16, (0.03, 0.025, 0.02))]
bones = [Bone("root", None, (0, 0, 0), (0, 0.05, 0)),
         Bone("palm", "root", (0, -0.03, PALM_Z), (0, 0.06, PALM_Z)),
         Bone("stump", "palm", (0, -0.03, PALM_Z), (0, -0.08, PALM_Z)),
         Bone("mouth", "palm", (0, 0.022, PALM_Z + 0.012), (0, 0.022, PALM_Z + 0.03))]
TIPS = []
for name, (x, y), splay, lengths in FINGERS:
    d = Vector((math.sin(math.radians(splay)), math.cos(math.radians(splay)), 0))
    p = Vector((x, y, PALM_Z))
    parent = "palm"
    for k, ln in enumerate(lengths):
        q = p + d * ln
        bones.append(Bone(f"{name}_{k + 1}", parent, tuple(p), tuple(q)))
        parent, p = f"{name}_{k + 1}", q
    TIPS.append(parent)
sk = Skeleton("SK_Follower", bones)
sk.build()
kit = rig.Kit(sk, "follower")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


SKIN = Mat("skin.follower", hexc("#9c8a80"), shine=0.35)
STUMP = Mat("skin.follower_stump", hexc("#7b4740"), shine=0.6)
LIPS = Mat("skin.follower_mouth", hexc("#6c2c2a"), shine=0.6)
TEETH = Mat("skin.follower_teeth", hexc("#cfc3a6"), shine=0.5)
CORE = Mat("tar.follower_core", hexc("#120606"), shine=0.4)
NAIL = Mat("tar.follower_nail", hexc("#5a4a3a"), shine=0.5)


def bell(x):
    return math.exp(-x * x)


hand = kit.part("hand")


def palm_shape(i, j, a, th, p):
    """The back of the hand domed and soft, the tendons standing to each knuckle, wrinkled over them."""
    p = Vector(p)
    up = max(0.0, p.z - PALM_Z)
    d = 0.0
    for name, (x, y), _, _ in FINGERS[1:]:
        d += 0.003 * bell((p.x - x * (0.4 + 0.6 * max(0.0, p.y) / 0.06)) / 0.005) * (p.y > -0.02)
    d += 0.0015 * noise3(p * 120, 31, 1.0)
    return p + Vector((0, 0, 1)) * d * (up > 0.002)


# The palm: a flattened lump from the wrist to the knuckles.
hand.blob(Vector((0, 0.02, PALM_Z)), (0.04, 0.054, 0.014), 18, 10, SKIN, "palm", shape=palm_shape)
# The knuckles: a ridge across the palm's front that the fingers grow out of, swollen at each one; the thumb's pad.
KN = [Vector((x, y, PALM_Z + 0.001)) for _, (x, y), _, _ in FINGERS[1:]]
hand.tube(KN, [0.011, 0.012, 0.0115, 0.0105], 8, SKIN, "palm", ref=(0, 0, 1), cap0=True, cap1=True)
hand.tube([Vector((0.025, -0.012, PALM_Z)), Vector((0.038, 0.0, PALM_Z)), Vector(FINGERS[0][1] + (PALM_Z,))], [0.012, 0.011, 0.0095], 8, SKIN,
          "palm", ref=(0, 0, 1), cap0=True)
# The stump, raw, and its threads trailing.
hand.tube([H("stump"), H("stump").lerp(T("stump"), 0.5), T("stump")], [(0.03, 0.015), (0.026, 0.013), (0.02, 0.011)], 10, SKIN, "stump",
          ref=(0, 0, 1), cap1=True, fmat=lambda pts, n: STUMP if sum(pts, Vector()).y / len(pts) < -0.07 else SKIN)
for k in range(5):
    a = (k - 2) * 0.35
    root = T("stump") + Vector((math.sin(a) * 0.012, 0, 0.004 * math.cos(a * 3)))
    ln = 0.07 + 0.03 * (0.5 + 0.5 * math.sin(k * 2.7))
    pts = [root + Vector((math.sin(a) * ln * f * 0.4, -ln * f, 0.006 * math.sin(f * 6 + k))) for f in (0.0, 0.33, 0.66, 1.0)]
    hand.tube(pts, [0.0025, 0.002, 0.0015, 0.0006], 4, STUMP, "stump", ref=(0, 0, 1), cap1="point")
# The mouth on the back of the hand: a ring of lip, two rings of tiny teeth in it, the dark.
M = H("mouth")
# Puckered, swollen round its rim, wet.
hand.tube([M + Vector((math.cos(t) * 0.015, math.sin(t) * 0.015, 0.003 + 0.0015 * math.sin(t * 5))) for t in (2 * math.pi * k / 14 for k in range(14))],
          [0.005] * 14, 6, LIPS, "mouth", ref=(0, 0, 1), loop=True)
hand.blob(M + Vector((0, 0, -0.003)), (0.013, 0.013, 0.005), 12, 4, CORE, "mouth")
for ring, r in ((0, 0.013), (1, 0.009), (2, 0.0055)):
    n = (16, 12, 8)[ring]
    for k in range(n):
        t = 2 * math.pi * k / n + ring * 0.3
        base = M + Vector((math.cos(t) * r, math.sin(t) * r, 0.003 - ring * 0.0015))
        tip = base + Vector((-math.cos(t) * 0.0055, -math.sin(t) * 0.0055, -0.0015))
        hand.tube([base, tip], [0.0016, 0.0002], 3, TEETH, "mouth", ref=(0, 0, 1))
# The fingers: soft, the knuckles swollen, an extra crease between each; the nails split.
for name, (x, y), splay, lengths in FINGERS:
    chain = [f"{name}_{k + 1}" for k in range(3)]
    pts, radii, ws = [], [], []
    for k, b in enumerate(chain):
        h, t = H(b), T(b)
        r = 0.0085 - 0.0015 * k - (0.0015 if name in ("thumb", "little") else 0)
        pts += [h, h.lerp(t, 0.5)]
        radii += [r * 1.15, r * 0.92]
    pts.append(T(chain[-1]))
    radii.append(0.0045)

    def fw(p, chain=chain):
        best = min(chain, key=lambda b: sk.segment_distance(b, p))
        return {best: 1.0}
    hand.tube(pts, radii, 7, SKIN, fw, ref=(0, 0, 1), cap1=True,
              shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.0012 * math.sin(i * 3.1))
    tip, back = T(chain[-1]), H(chain[-1])
    d = (tip - back).normalized()
    hand.tube([tip - d * 0.012 + Vector((0, 0, 0.004)), tip - d * 0.001 + Vector((0, 0, 0.003))], [(0.004, 0.0015), (0.0035, 0.0012)], 4, NAIL,
              chain[-1], ref=(0, 0, 1))


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes: a finger's bone lifted by +X, curled down by -X, splayed by Z.
def fingers(knuckle, mid, tip, spread=0.0, only=None):
    out = {}
    for name, *_ in FINGERS:
        if only is not None and name not in only:
            continue
        s = 1 if name in ("thumb", "index") else -1 if name in ("ring", "little") else 0
        out[f"{name}_1"] = (knuckle, 0, s * spread)
        out[f"{name}_2"] = (mid, 0, 0)
        out[f"{name}_3"] = (tip, 0, 0)
    return out


# Cling (4 s, loop): flat on a back, the fingers spread and their tips dug in; dead still, the mouth working slowly;
# then the twitch: every finger clenches at once, and lets go, and the stump's threads draw in.
FLAT = fingers(-4, -12, -18, 6)
cling = Clip("cling")
cling.key(0, FLAT | {"mouth@scale": (1, 1, 1)}, "BEZIER")
cling.key(40, FLAT | {"mouth@scale": (1.4, 1.4, 1)}, "BEZIER")
cling.key(70, FLAT | {"mouth@scale": (0.9, 0.9, 1)}, "BEZIER")
cling.key(90, FLAT | fingers(-14, -40, -40, -4) | {"stump": (8, 0, 6)}, "CONSTANT")
cling.key(94, FLAT | fingers(-10, -30, -30, 0) | {"stump": (4, 0, 2)}, "CONSTANT")
cling.key(97, FLAT | fingers(-16, -44, -44, -6) | {"stump": (10, 0, -4)}, "CONSTANT")
cling.key(102, FLAT, "BEZIER")
cling.close(120)

# Crawl (0.6 s, loop): up on its fingertips, the palm held high, scuttling: the fingers in two sets (thumb, middle,
# little; index, ring) each lifted and thrown forward in turn, the stump dragging its threads.
STAND = fingers(48, -68, -42, 4) | {"palm": (6, 0, 0)}
crawl = Clip("crawl")
for f, k in ((0, 1.0), (4.5, 0.0), (9, -1.0), (13.5, 0.0)):
    p = dict(STAND)
    for name, *_ in FINGERS:
        lift = max(0.0, k) if name in ("thumb", "middle", "little") else max(0.0, -k)
        kx, _, kz = p[f"{name}_1"]
        p[f"{name}_1"] = (kx + 22 * lift - 8 * (1 - lift), 0, kz)
    p["palm"] = (6, 4 * k, 0)
    p["stump"] = (0, 0, 10 * k)
    crawl.key(f, p, "LINEAR")
crawl.close(18)

# Nest (3 s, loop): spread out flat over what it's eating, the fingers kneading, slowly, one after another; the mouth
# working, wide.
nest = Clip("nest")
for f, k in ((0, 0), (22, 1), (45, 2), (67, 3)):
    p = dict(FLAT)
    for n, (name, *_r) in enumerate(FINGERS):
        if (n + k) % 2 == 0:
            p[f"{name}_1"], p[f"{name}_2"], p[f"{name}_3"] = (-14, 0, 0), (-38, 0, 0), (-34, 0, 0)
    p["mouth@scale"] = (1.6 if k % 2 else 1.0,) * 2 + (1,)
    nest.key(f, p, "BEZIER")
nest.close(90)

hit = Clip("hit", loop=False)
hit.key(0, FLAT, "CONSTANT")
hit.key(2, fingers(40, 30, 20, 20) | {"palm": (-20, 10, 0)}, "CONSTANT")
hit.key(10, fingers(-20, -60, -60, -4), "LINEAR")
hit.key(14, FLAT, "CONSTANT")

kit.build()
rig.bake(sk, [cling, crawl, nest, hit], plant=rig.feet_planter(sk, bones=tuple(TIPS), lowest=0.002))
print("[dt] follower", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "follower.glb", kit)
