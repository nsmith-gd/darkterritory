"""TOWER JAW (GDD §21, OUTSIDE; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 363;
docs/design/creatures/tower-jaw.md §3 and the director's reference, tower-jaw-reference.webp): "a beaver as big as a
bear, its hide bristling with splinters, gnawing at a tower's legs with iron-dark teeth."

A beaver's body, front-heavy, about 1.6 m at the shoulder: enormous hunched shoulders standing higher than its head; a
blunt wet muzzle and a black nose; two iron-dark incisors, great chisels as long as a forearm, chipped and rust-streaked;
dark brown fur matted wet and slicked into spikes; the hide full of splinters (shards of timber driven through it
everywhere) and a ridge of black splintered wood like spines down the back, ember-red in the cracks; broad shovel paws,
their claws like split planks; a broad flat scaled tail.

How it's made (the Look Review: organic, one skin; the Gannet's and the Ribbit's way, notes 340, 361): the rump, the
loins, the barrel of the chest, the hump of the shoulders, the neck, the skull with its cheeks, muzzle, lips and nose,
the ears, the legs to the paws and the webbed hind feet, and the tail's root out to its paddle are one skin of smooth
volumes settled onto their smooth-blended field (tools/blender/flesh.py) before QuadriFlow. Over it: the wet fur slicked
into spikes (hundreds of clumps, combed back and down the way the fur lies), the splinters driven into it, the black
spines down the back, the incisors, the eyes, and the plank claws. Its colour (and the fur's grain, the tail's scales,
the wood's grain and the embers in its cracks, an emission map) is baked into one atlas by
tools/models/recipes/tower_jaw.py.

Its own rig (SK_TowerJaw, 26 bones): root, pelvis, spine_01, chest, neck, head, jaw, three tail bones; upperarm,
lowerarm, hand and claws (fingers) a front leg; thigh, calf, foot and toe a hind leg. Its rest is stood on all fours,
facing +Y (the engine's -Z). Clips: gnaw (at a post, the head turned side-on and working, chips flying), turn (head up,
listening), threat (reared up, the incisors bared, the tail slapping the ground), lunge (a short charge and a bite),
retreat (a heavy lope), hit, death. The game draws it MOUTH_BACK back of the sim's point (where the sim keeps it is its
jaws at the post: CreatureArt.TowerJawBack).

    tools/models/build.sh tower_jaw        # this, its colour baked -> content/art/models/tower_jaw.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/tower_jaw.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, chain, h01, mix, smooth01  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()


def jaw_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.3, 0)),
        Bone("pelvis", "root", (0, -0.7, 0.85), (0, -0.25, 0.97)),
        Bone("spine_01", "pelvis", (0, -0.25, 0.97), (0, 0.2, 1.15)),
        Bone("chest", "spine_01", (0, 0.2, 1.15), (0, 0.6, 1.18)),
        Bone("neck", "chest", (0, 0.6, 1.1), (0, 0.95, 1.0)),
        Bone("head", "neck", (0, 0.95, 1.0), (0, 1.45, 0.92)),
        Bone("jaw", "head", (0, 1.05, 0.84), (0, 1.42, 0.74)),
        Bone("tail_01", "pelvis", (0, -1.0, 0.62), (0, -1.38, 0.38)),
        Bone("tail_02", "tail_01", (0, -1.38, 0.38), (0, -1.85, 0.27)),
        Bone("tail_03", "tail_02", (0, -1.85, 0.27), (0, -2.35, 0.3)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"upperarm_{s}", "chest", (sx * 0.42, 0.5, 1.0), (sx * 0.48, 0.62, 0.55)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.48, 0.62, 0.55), (sx * 0.47, 0.82, 0.15)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.47, 0.82, 0.15), (sx * 0.48, 1.0, 0.05)),
            Bone(f"fingers_{s}", f"hand_{s}", (sx * 0.48, 1.0, 0.05), (sx * 0.48, 1.24, 0.02)),
            Bone(f"thigh_{s}", "pelvis", (sx * 0.36, -0.72, 0.8), (sx * 0.44, -0.42, 0.42)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.44, -0.42, 0.42), (sx * 0.44, -0.82, 0.13)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.44, -0.82, 0.13), (sx * 0.45, -0.5, 0.03)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.45, -0.5, 0.03), (sx * 0.46, -0.3, 0.02)),
        ]
    return Skeleton("SK_TowerJaw", b)


sk = jaw_skeleton()
sk.build()
kit = rig.Kit(sk, "tower_jaw")
SKIN_FACES = 3600
# Where its incisors' chisel edges are at rest (the game sets it back by about this less the post's reach: CreatureArt).
MOUTH_BACK = 0.85


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


FUR = Mat("fleece.tj_fur", hexc("#3a2a1e"), shine=0.5)
MUZZLE = Mat("skin.tj_muzzle", hexc("#3a2c26"), shine=0.6)
NOSE = Mat("skin.tj_nose", hexc("#141110"), shine=0.8)
TAIL = Mat("leather.tj_tail", hexc("#2a201c"), shine=0.5)
PAW = Mat("skin.tj_paw", hexc("#241c18"), shine=0.4)
TOOTH = Mat("rust_heavy.tj_tooth", hexc("#3a302a"), shine=0.4)
EYE = Mat("glass_dirty.tj_eye", hexc("#0c0a09"), shine=0.95)
CLAW = Mat("wood_grey.tj_claw", hexc("#3a2e24"), shine=0.3)
SPLINTER = Mat("wood_grey.tj_splinter", hexc("#8a6a48"), shine=0.2)
SPINE = Mat("wood_grey.tj_spine", hexc("#16120f"), shine=0.4)

# ----------------------------------------------------------------------------------------------------------------
# The skin.
body = kit.part("body")
flesh = Flesh(body, "tower jaw flesh")
SPINE_W = chain([Vector((0, -1.0, 0.8)), H("pelvis"), H("spine_01"), H("chest"), Vector((0, 0.6, 1.15)), H("head"), T("head")],
                ["pelvis", "pelvis", "spine_01", "chest", "neck", "head"], soft=0.18)


def trunk_w(p):
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.y > 0.25 and ax > 0.25 and p.z < 1.25:
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.6 * smooth01(0.25, 0.42, ax) * smooth01(1.25, 1.0, p.z) * smooth01(0.25, 0.4, p.y))
    if p.y < -0.4 and ax > 0.2 and p.z < 0.95:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.6 * smooth01(0.2, 0.36, ax) * smooth01(0.95, 0.75, p.z))
    if p.y < -0.95:
        w = mix(w, {"tail_01": 1.0}, smooth01(-0.95, -1.15, p.y))
    return w


def head_w(p):
    p = Vector(p)
    w = trunk_w(p)
    if p.y > 1.1 and p.z < 0.86:
        w = mix(w, {"jaw": 1.0}, smooth01(0.86, 0.8, p.z) * smooth01(1.1, 1.2, p.y))
    return w


# The trunk: the rump low behind, the loins, the deep barrel of the chest, and the hump of the shoulders over it all,
# standing higher than the head; the belly slung under, the fur hanging off it.
flesh.blob((0, -0.75, 0.8), (0.46, 0.42, 0.42), 0, FUR, trunk_w, around=22, rings=12)
flesh.blob((0, -0.25, 0.9), (0.52, 0.55, 0.5), 0.15, FUR, trunk_w, around=22, rings=12)
flesh.blob((0, 0.35, 1.04), (0.56, 0.44, 0.52), 0.15, FUR, trunk_w, around=24, rings=12)
flesh.blob((0, 0.28, 1.33), (0.48, 0.4, 0.3), 0.14, FUR, trunk_w, around=22, rings=10)            # the hump
flesh.blob((0, -0.05, 0.6), (0.42, 0.55, 0.26), 0.12, FUR, trunk_w)                               # the belly
for sx in (1, -1):
    flesh.blob((sx * 0.36, 0.45, 1.12), (0.26, 0.34, 0.4), 0.12, FUR, trunk_w, rot=R(rx=-10))    # the shoulders
    flesh.blob((sx * 0.34, -0.65, 0.66), (0.22, 0.32, 0.3), 0.1, FUR, trunk_w)                   # the haunches
# The neck: thick, short, low off the front of the hump.
flesh.limb([Vector((0, 0.55, 1.12)), Vector((0, 0.8, 1.05)), Vector((0, 1.0, 1.0))], [0.42, 0.38, 0.34], 0.1, FUR, trunk_w, sides=18)

# The head: a beaver's, big and blunt: a broad flat skull, the cheeks puffed out wide, a heavy rounded muzzle, wet, a big
# black nose on its end; the upper lips' flews hanging either side of the incisors; small round ears and small eyes set
# high on the skull. (The G1 review: not a nub on the body, a head to match it.)
flesh.blob((0, 1.1, 1.01), (0.36, 0.38, 0.27), 0.1, FUR, head_w, around=24, rings=12)              # the skull
flesh.blob((0, 1.02, 1.13), (0.3, 0.28, 0.12), 0.08, FUR, head_w)                                  # its flat crown
for sx in (1, -1):
    flesh.blob((sx * 0.24, 1.24, 0.9), (0.2, 0.22, 0.19), 0.07, FUR, head_w)                       # the cheeks
    flesh.blob((sx * 0.29, 1.0, 1.25), (0.085, 0.05, 0.085), 0.03, FUR, head_w, rot=R(rz=sx * 25))  # the ears
    flesh.blob((sx * 0.11, 1.6, 0.82), (0.115, 0.1, 0.1), 0.035, MUZZLE, head_w)                  # the flews
flesh.blob((0, 1.47, 0.92), (0.27, 0.23, 0.21), 0.07, MUZZLE, head_w)                              # the muzzle
flesh.blob((0, 1.66, 0.98), (0.14, 0.075, 0.1), 0.035, NOSE, head_w)                               # the nose
for sx in (1, -1):
    flesh.carve((sx * 0.06, 1.725, 0.99), (0.032, 0.022, 0.026), 0.012)                            # the nostrils
flesh.blob((0, 1.4, 0.72), (0.19, 0.2, 0.1), 0.05, MUZZLE, head_w)                                 # the chin

# The front legs: thick as a bear's, the elbows out, down to broad flat shovel paws.
PAWS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([sh, el, wr, tip, T(f"fingers_{s}")], [f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}", f"fingers_{s}"], soft=0.08)
    flesh.limb([sh + Vector((0, 0, 0.1)), sh.lerp(el, 0.5), el], [0.25, 0.21, 0.17], 0.1, FUR,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(0.85, 1.1, p[2]) * 0.7))
    flesh.blob(el + Vector((sx * 0.02, -0.03, 0)), (0.13, 0.13, 0.13), 0.05, FUR, arm)
    flesh.limb([el, el.lerp(wr, 0.5), wr], [0.16, 0.14, 0.11], 0.05, FUR, arm)
    paw = wr.lerp(tip, 0.6) + Vector((0, 0, -0.02))
    PAWS[s] = paw
    flesh.blob(paw, (0.19, 0.17, 0.065), 0.06, PAW, arm, fmat=lambda pts, n: FUR if n.z > 0.5 else PAW)
# The hind legs: short and thick, folded under the haunches, on big webbed feet.
FEET = {}
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hp, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.07)
    flesh.limb([hp, hp.lerp(kn, 0.5), kn], [0.24, 0.2, 0.15], 0.08, FUR, lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(0.7, 0.9, p[2]) * 0.6))
    flesh.limb([kn, kn.lerp(an, 0.5), an], [0.14, 0.12, 0.09], 0.05, FUR, leg)
    foot = an.lerp(tt, 0.5) + Vector((0, 0, -0.022))
    FEET[s] = foot
    flesh.blob(foot, (0.17, 0.3, 0.05), 0.05, PAW, leg, fmat=lambda pts, n: FUR if n.z > 0.6 else PAW)
# The tail: its thick furred root, then the paddle, broad and flat and scaled, lying on the ground.
TAIL_W = chain([Vector((0, -0.95, 0.66)), H("tail_01"), H("tail_02"), H("tail_03"), T("tail_03")], ["pelvis", "tail_01", "tail_02", "tail_03"], soft=0.12)
flesh.limb([Vector((0, -0.95, 0.66)), Vector((0, -1.2, 0.5)), H("tail_02")], [0.22, 0.16, 0.09], 0.08, FUR, TAIL_W,
           fmat=lambda pts, n: TAIL if (sum(pts, Vector()) / len(pts)).y < -1.3 else FUR)
# (Held up off the ground and its back end tipped up, so its broad scaled top shows from the side.)
flesh.blob((0, -1.8, 0.27), (0.32, 0.45, 0.055), 0.06, TAIL, TAIL_W, rot=R(rx=-5), around=24, rings=10)
flesh.blob((0, -2.17, 0.3), (0.29, 0.3, 0.048), 0.05, TAIL, TAIL_W, rot=R(rx=-8), around=22, rings=8)

# ----------------------------------------------------------------------------------------------------------------
# Over the skin: the incisors, the eyes, the claws (`hard`); the wet fur's spikes, the splinters and the spines (`hide`).
hard = kit.part("hard")
EYES = []
for sx in (1, -1):
    e = Vector((sx * 0.235, 1.3, 1.13))
    EYES.append(e)
    hard.blob(e, (0.032, 0.03, 0.028), 12, 7, EYE, "head")
# The incisors: two great chisels out of the upper jaw, curved down and a little back, flat-faced and squared off at
# their edges, chipped; the lower pair short behind them.
TEETH = []
for sx in (1, -1):
    # (Broad, flat-faced, side by side, their roots up under the lip and hanging well below it: readable at 10 m.)
    top = Vector((sx * 0.043, 1.64, 0.86))
    pts = [top + Vector((0, -0.04, 0.05)), top, top + Vector((0, 0.04, -0.16)), top + Vector((0, 0.045, -0.32)), top + Vector((0, 0.025, -0.48))]
    TEETH.append(pts[-1])
    hard.tube(pts, [(0.04, 0.034), (0.04, 0.032), (0.04, 0.03), (0.039, 0.026), (0.038, 0.012)], 8, TOOTH, "head",
              ref=(0, 1, 0), square=0.35, cap0=True, cap1=True,
              shape=lambda i, j, a, p, fr, sx=sx: Vector(p) + (Vector((0, 0, 0.018 * (math.sin(a) * sx > 0.3))) if i == 4 else Vector()))
    low = Vector((sx * 0.035, 1.53, 0.7))
    hard.tube([low, low + Vector((0, 0.04, -0.06)), low + Vector((0, 0.06, -0.13))], [(0.026, 0.018), (0.025, 0.016), (0.022, 0.008)], 6, TOOTH, "jaw",
              ref=(0, 1, 0), square=0.4, cap0=True, cap1=True)
# The claws: split planks, flat and squared, splintered at their ends; five a front paw, splayed and laid forward;
# shorter ones on the hind feet.
CLAWS = []
for s, sx in (("r", 1), ("l", -1)):
    paw = PAWS[s]
    for k in range(5):
        a = math.radians((k - 2) * 13 + sx * 4)
        d = Vector((math.sin(a) * sx, math.cos(a), -0.12)).normalized()
        L = 0.25 + 0.05 * (1 - abs(k - 2) / 2) + 0.03 * h01(301, k, sx)
        root = paw + Vector((math.sin(a) * sx * 0.12, 0.1, 0.02))
        tip = root + d * L + Vector((0, 0, -0.03))
        tip.z = max(0.015, tip.z)
        CLAWS.append(tip)
        hard.tube([root - d * 0.04, root + d * L * 0.5 + Vector((0, 0, 0.012)), tip], [(0.034, 0.02), (0.032, 0.018), (0.028, 0.014)], 4,
                  CLAW, chain([paw, root, tip], [f"hand_{s}", f"fingers_{s}"], soft=0.05), ref=(0, 0, 1), square=0.3, cap0=True, cap1=True,
                  shape=lambda i, j, a_, p, fr, k=k: Vector(p) + (fr[2] * (0.025 * h01(302, k, j)) if i == 2 else Vector()))
    foot = FEET[s]
    for k in range(5):
        a = math.radians((k - 2) * 15)
        d = Vector((math.sin(a) * sx * 0.5, 1, -0.1)).normalized()
        root = foot + Vector((math.sin(a) * sx * 0.12, 0.26, 0.0))
        tip = root + d * 0.12
        hard.tube([root, tip], [(0.022, 0.013), (0.018, 0.008)], 4, CLAW, f"toe_{s}", ref=(0, 0, 1), square=0.3, cap0=True, cap1=True)

hide = kit.part("hide")
LIMBS = ["pelvis", "spine_01", "chest", "neck", "head", "tail_01", "upperarm_r", "upperarm_l", "lowerarm_r", "lowerarm_l",
         "thigh_r", "thigh_l", "calf_r", "calf_l"]


def near_w(p):
    return hide.weights_for(Vector(p), (LIMBS, 6.0))


def surface_from(c, d, reach=1.2):
    """Where a ray out from inside the body leaves its skin, and the skin's way out there."""
    p = flesh.surface(c, d, reach)
    return p, flesh.normal(p)


# Inner points (the body's axis and the limbs') to shoot rays out from, to scatter things over the skin.
CORES = [(Vector((0, y, 0.95 + 0.2 * smooth01(-0.6, 0.3, y) - 0.15 * smooth01(0.6, 1.2, y))), 1.0) for y in [-0.85 + 0.15 * i for i in range(14)]]
for s, sx in (("r", 1), ("l", -1)):
    for a, b in ((H(f"upperarm_{s}"), H(f"lowerarm_{s}")), (H(f"lowerarm_{s}"), H(f"hand_{s}")), (H(f"thigh_{s}"), H(f"calf_{s}"))):
        for t in (0.2, 0.5, 0.8):
            CORES.append((a.lerp(b, t) - Vector((sx * 0.05, 0, 0)), 0.5))
# The fur: wet, clumped and slicked into spikes, combed back along the body and down its flanks and legs; longest and
# shaggiest over the shoulders and hanging under the belly and the jowls; none on the muzzle, the paws or the tail.
SPIKES = 0
for i in range(560):
    ci = int(h01(501, i) * len(CORES)) % len(CORES)
    c, _ = CORES[ci]
    th = h01(502, i) * 2 * math.pi
    ph = (h01(503, i) - 0.5) * math.pi * 0.95
    d = Vector((math.cos(ph) * math.sin(th), math.cos(ph) * math.cos(th) * 0.6, math.sin(ph)))
    if d.length < 1e-6:
        continue
    p, n = surface_from(c, d)
    if p.y > 1.28 or p.y < -1.05 or p.z < 0.12:
        continue
    flow = Vector((0, -0.65, -0.75)) if abs(p.x) < 0.45 or p.z > 0.8 else Vector((0, -0.1, -1.0))
    under = n.z < -0.4
    L = (0.07 + 0.09 * h01(504, i)) * (1.5 if under else 1.0) * (1.35 if p.z > 1.2 and p.y > 0.0 else 1.0)
    way = (n * 0.28 + flow.normalized() * 0.72).normalized()
    if under:
        way = (n * 0.2 + Vector((0, -0.2, -1))).normalized()
    r = 0.026 + 0.016 * h01(505, i)
    base = p - n * 0.012
    tip = base + way * L + Vector((0, 0, -0.012 * L / 0.1))
    if tip.z < 0.03:
        continue
    hide.tube([base, base.lerp(tip, 0.5) + n * 0.008, tip], [(r * 1.2, r * 0.6), (r * 0.7, r * 0.35), 0.0], 3, FUR, near_w, ref=tuple(n),
              twist=h01(506, i) * 1.5)
    SPIKES += 1
# The splinters: shards of timber driven into it everywhere, broken off at all angles, pale where they're fresh.
SPLINTERS = []
for i in range(84):
    ci = int(h01(511, i) * len(CORES)) % len(CORES)
    c, _ = CORES[ci]
    th = h01(512, i) * 2 * math.pi
    ph = (h01(513, i) - 0.3) * math.pi * 0.7
    d = Vector((math.cos(ph) * math.sin(th), math.cos(ph) * math.cos(th) * 0.7, math.sin(ph)))
    p, n = surface_from(c, d)
    if p.z < 0.25 or p.y > 1.25 or p.y < -1.0:
        continue
    tilt = Vector(((h01(514, i) - 0.5), (h01(515, i) - 0.5) - 0.4, (h01(516, i) - 0.5))).normalized()
    way = (n * 0.75 + tilt * 0.5).normalized()
    L = 0.2 + 0.42 * h01(517, i) ** 1.4
    w = 0.024 + 0.034 * h01(518, i)
    base = p - way * L * 0.3
    tip = p + way * L * 0.7
    SPLINTERS.append((base, tip))
    hide.tube([base, base.lerp(tip, 0.55), tip], [(w, w * 0.6), (w * 0.85, w * 0.5), (w * 0.15, w * 0.1)], 4, SPLINTER, near_w,
              ref=tuple(tilt), square=0.4, twist=h01(519, i), cap0=True, cap1=True)
# The spines: a ridge of black splintered wood down the back from the nape to the rump, tallest over the hump, each a
# cluster of jagged shards leant back, the embers glowing in their cracks (the bake's emission).
RIDGE = []
for i in range(18):
    t = i / 17
    y = 0.95 - 1.9 * t
    top = flesh.surface(Vector((0, y, 0.9)), (0, 0, 1), 1.2)
    height = 0.2 + 0.45 * math.exp(-((y - 0.3) / 0.45) ** 2) + 0.06 * h01(521, i)
    for k in range(2 if i % 3 else 3):
        jx = (h01(522, i, k) - 0.5) * 0.16
        lean = Vector((jx * 0.8, -0.45 - 0.3 * h01(523, i, k), 1.0)).normalized()
        base = top + Vector((jx, 0.03 * (k - 1), -0.05))
        L = height * (1.0 if k == 0 else 0.55 + 0.3 * h01(524, i, k))
        tip = base + lean * L
        RIDGE.append((base, tip))
        w = 0.05 + 0.04 * height
        hide.tube([base - lean * 0.06, base.lerp(tip, 0.35), base.lerp(tip, 0.7) + Vector((0.02 * (h01(525, i, k) - 0.5), 0, 0)), tip],
                  [(w, w * 0.55), (w * 0.85, w * 0.45), (w * 0.5, w * 0.3), (0.004, 0.003)], 5, SPINE, near_w,
                  ref=(1, 0, 0), square=0.6, twist=h01(526, i, k) * 2, cap0=True)

# ----------------------------------------------------------------------------------------------------------------
# Clips. Its rest is stood on all fours; angles in the armature's axes (+X tips a forward bone up, swings a hanging limb
# forward; +Z turns toward its left).


def pose(*parts):
    out = {}
    for p in parts:
        out.update(p)
    return out


STAND = {}

# Gnaw (1.6 s, loop): side-on at the post, the head low and turned on its side to it, the jaw working, the whole head
# wrenching at the wood in jerks; one paw up against the post, the shoulders bunching; chips flying (CreatureArt).
gnaw = Clip("gnaw")
BITE = pose({"chest": (-6, 0, 0), "neck": (-14, 0, 0), "head": (-10, 34, -8), "upperarm_r": (24, 0, 0), "lowerarm_r": (-30, 0, 0),
             "hand_r": (30, 0, 0), "root@loc": (0, 0.1, -0.04), "tail_01": (0, 0, 0)})
for f, j, tw in ((0, -14, 0), (5, 0, 10), (8, -12, -6), (13, 0, 12), (16, -16, 0), (21, 0, 8), (24, -12, -10), (29, 0, 14),
                 (32, -14, 0), (37, 0, 10), (40, -12, -8), (45, 0, 12)):
    gnaw.key(f, pose(BITE, {"jaw": (j, 0, 0), "head": (-10 - 0.3 * tw, 34 + tw, -8 + 0.5 * tw), "neck": (-14, tw * 0.3, 0),
                            "chest": (-6, -tw * 0.15, 0)}), "CONSTANT" if j == 0 else "LINEAR")
gnaw.close(48)

# Turn (2 s, loop): head up off the work, listening; it swings round to look, holds, turns back.
turn = Clip("turn")
for f, look, up in ((0, 0, 0), (6, 30, 14), (22, 30, 16), (28, -12, 12), (44, -12, 14), (54, 0, 8)):
    turn.key(f, {"neck": (up * 0.6, 0, look * 0.4), "head": (up, 0, look * 0.6), "chest": (2, 0, look * 0.1), "jaw": (-3, 0, 0)},
             "CONSTANT" if f in (6, 28) else "BEZIER")
turn.close(60)

# Threat (1.5 s, loop): reared up on its haunches, the front lifted, the paws up and spread, the head up and the lips
# drawn back off the incisors; the tail raised and slammed flat on the ground (the slap).
REAR = {"pelvis": (26, 0, 0), "spine_01": (8, 0, 0), "chest": (4, 0, 0), "neck": (-16, 0, 0), "head": (-12, 0, 0), "jaw": (-16, 0, 0),
        "thigh_r": (-24, 0, 0), "thigh_l": (-24, 0, 0), "calf_r": (8, 0, 0), "calf_l": (8, 0, 0), "foot_r": (-10, 0, 0), "foot_l": (-10, 0, 0),
        "upperarm_r": (36, -16, 0), "upperarm_l": (36, 16, 0), "lowerarm_r": (-44, 0, 0), "lowerarm_l": (-44, 0, 0),
        "hand_r": (-10, 0, 0), "hand_l": (-10, 0, 0), "fingers_r": (-24, 0, 0), "fingers_l": (-24, 0, 0),
        "root@loc": (0, -0.25, 0.0), "tail_01": (-18, 0, 0), "tail_02": (16, 0, 0)}
threat = Clip("threat")
threat.key(0, pose(REAR, {"tail_01": (30, 0, 0), "tail_02": (20, 0, 0), "tail_03": (10, 0, 0)}), "BEZIER")
threat.key(14, pose(REAR, {"tail_01": (44, 0, 0), "tail_02": (24, 0, 0), "tail_03": (14, 0, 0), "jaw": (-20, 0, 0), "head": (-18, 0, 4)}), "LINEAR")
threat.key(18, pose(REAR, {"tail_01": (-24, 0, 0), "tail_02": (18, 0, 0), "tail_03": (6, 0, 0), "head": (-10, 0, -4)}), "CONSTANT")
threat.key(30, pose(REAR, {"tail_01": (-14, 0, 0), "tail_02": (16, 0, 0), "jaw": (-12, 0, 0)}), "BEZIER")
threat.close(45)

# Lunge (0.8 s): a short charge, the head thrown forward and the incisors driven down in a bite, then back.
lunge = Clip("lunge", loop=False)
lunge.key(0, {"pelvis": (-4, 0, 0), "chest": (-6, 0, 0), "root@loc": (0, -0.1, -0.05)}, "LINEAR")
lunge.key(6, {"pelvis": (-10, 0, 0), "chest": (-4, 0, 0), "neck": (10, 0, 0), "head": (14, 0, 0), "jaw": (-22, 0, 0),
              "upperarm_r": (40, 0, 0), "upperarm_l": (30, 0, 0), "thigh_r": (-30, 0, 0), "thigh_l": (-36, 0, 0), "root@loc": (0, 0.8, 0.1)}, "LINEAR")
lunge.key(9, {"pelvis": (-4, 0, 0), "chest": (-10, 0, 0), "neck": (-16, 0, 0), "head": (-22, 0, 0), "jaw": (0, 0, 0),
              "upperarm_r": (20, 0, 0), "upperarm_l": (16, 0, 0), "root@loc": (0, 1.0, -0.06)}, "CONSTANT")
lunge.key(16, {"neck": (-8, 0, 6), "head": (-12, 0, 10), "jaw": (-4, 0, 0), "root@loc": (0, 0.8, -0.04)}, "BEZIER")
lunge.key(24, {"root@loc": (0, 0, 0)}, "BEZIER")


# Retreat (0.8 s, loop): the heavy lope off, a beaver's humping gallop: the front paws together, then the hind.
retreat = Clip("retreat")
for f, k in ((0, 0), (6, 1), (12, 2), (18, 3)):
    a = k * math.pi / 2
    retreat.key(f, {"pelvis": (6 * math.sin(a), 0, 0), "chest": (-8 * math.sin(a + 1.2), 0, 0), "neck": (6 * math.sin(a), 0, 0),
                    "upperarm_r": (34 * math.cos(a), 0, 0), "upperarm_l": (34 * math.cos(a + 0.4), 0, 0),
                    "lowerarm_r": (-20 * max(0, math.sin(a)), 0, 0), "lowerarm_l": (-20 * max(0, math.sin(a + 0.4)), 0, 0),
                    "thigh_r": (30 * math.cos(a + math.pi), 0, 0), "thigh_l": (30 * math.cos(a + math.pi + 0.4), 0, 0),
                    "calf_r": (-20 * max(0, math.sin(a + math.pi)), 0, 0), "calf_l": (-20 * max(0, math.sin(a + math.pi + 0.4)), 0, 0),
                    "tail_01": (8 * math.sin(a), 0, 0), "tail_02": (-6 * math.sin(a), 0, 0),
                    "root@loc": (0, 0, 0.06 * max(0, math.sin(a)))}, "LINEAR")
retreat.close(24)

hit = Clip("hit", loop=False)
hit.key(0, STAND, "CONSTANT")
hit.key(2, {"chest": (8, 0, 10), "neck": (10, 0, 14), "head": (14, 0, 20), "jaw": (-14, 0, 0), "root@loc": (0, -0.12, 0.02),
            "tail_01": (16, 0, 0)}, "CONSTANT")
hit.key(9, {"chest": (-2, 0, -4), "head": (-4, 0, -6)}, "LINEAR")
hit.key(14, STAND, "CONSTANT")

# Death (1.5 s): it staggers, the front goes, and it rolls over onto its side, the legs out stiff, the tail flat.
death = Clip("death", loop=False)
death.key(0, STAND, "CONSTANT")
death.key(6, {"chest": (-12, 0, 8), "neck": (-10, 0, 10), "head": (-14, 0, 14), "jaw": (-18, 0, 0), "upperarm_r": (-20, 0, 0),
              "lowerarm_r": (30, 0, 0), "root@loc": (0, 0.1, -0.12)}, "LINEAR")
death.key(16, {"root": (0, 70, 0), "root@loc": (-0.1, 0.1, 0.28), "chest": (-8, 0, 6), "head": (-20, 0, 18), "jaw": (-22, 0, 0),
               "upperarm_r": (20, -20, 0), "upperarm_l": (20, 20, 0), "thigh_r": (-10, 0, 0), "thigh_l": (10, 0, 0)}, "LINEAR")
death.key(22, {"root": (0, 88, 0), "root@loc": (-0.2, 0.1, 0.5), "chest": (-6, 0, 4), "head": (-24, 0, 22), "jaw": (-26, 0, 0),
               "upperarm_r": (24, -24, 0), "upperarm_l": (26, 24, 0), "lowerarm_r": (10, 0, 0), "thigh_r": (-14, 0, 0), "thigh_l": (14, 0, 0),
               "tail_01": (0, -20, 0)}, "BEZIER")
death.key(45, {"root": (0, 90, 0), "root@loc": (-0.2, 0.1, 0.5), "chest": (-6, 0, 4), "head": (-26, 0, 24), "jaw": (-28, 0, 0),
               "upperarm_r": (24, -24, 0), "upperarm_l": (26, 24, 0), "lowerarm_r": (14, 0, 0), "thigh_r": (-16, 0, 0), "thigh_l": (16, 0, 0),
               "tail_01": (0, -24, 0)}, "BEZIER")

kit.fuse("body", ["body"], voxel=0.009, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, [gnaw, turn, threat, lunge, retreat, hit, death])
print("[dt] tower_jaw", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones), "spikes", SPIKES, "splinters", len(SPLINTERS), "spines", len(RIDGE))
out = rig.args()[0] if rig.args() else "tower_jaw.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
