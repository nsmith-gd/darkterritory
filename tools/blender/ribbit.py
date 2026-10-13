"""RIBBITS (GDD v1.2 §21 yards and villages, App. A.6 · sight): "Giant toad-rabbits in packs of two to four. Catch
someone alone and their tongues freeze them in place while the pack hops over to eat."

A toad the size of a big dog, sat up on a rabbit's haunches: hairless, the skin grey-white and wet as something that
lives under a stone, warted down the back, the belly paler and loose. Long rabbit's ears with no fur on them, bare skin
veined pink, hanging down its back. Two bulging eyes gone milky, set high, that don't blink. A mouth across the whole
of its head, and in it a row of flat human teeth. Under its jaw the throat sac: the tell, swelling huge and pale as the
pack lines up. Small human hands on its front legs; long rabbit feet behind.

How it's made (the Look Review: organic, not primitives; the Gannet's way, note 340): the body, the haunches folded
along its flanks, the legs and the long feet, the arms and the palms of the hands, the neck, the skull with its eye
mounds, lids, glands and rolled lip, the roots of the ears and the throat's sac are one skin. Each is a smooth volume (an
ellipsoid, a tapering limb) that's both a solid for rig.fuse's union and a term in a signed-distance field, blended
into what's there with its own softness (`Flesh`): the union's voxels are settled onto that field, so the haunch swells
out of the flank and the sac out of the throat with the crease filled as flesh fills it, before QuadriFlow lays the
game mesh's quads over it (smooth-shaded). The jaw is a second skin the same way (it opens). Over them, kept apart
because they're finer than the skin's quads: the eyes, the teeth, the tongue, the ears' bare blades (cupped, their
roots sunk in the skin's), the fingers and the toes. Its colour (and the warts, wrinkles and veins too fine for the
mesh) is baked into one atlas by tools/models/recipes/ribbit.py; built alone (this script), it wears the shared tiling
textures.

Its own rig (SK_Ribbit): root, pelvis, spine, chest, neck, head, jaw, throat (the sac, scaled), tongue (two bones),
ears (two bones each, they droop and swing), front legs (upperarm, lowerarm, hand), hind legs (thigh, calf, foot, toe).
Sat (its rest pose), facing +Y (the engine's -Z). The tongue's long shot is drawn by the engine (CreatureArt.Lash), from
the mouth to whoever it's got. Clips (GDD §31: still, then too fast): sit (dead still but for the throat; the ears
twitch), hop (a leap and a sit, 1 s: the sim's half-second bursts), swell (the telegraph: up tall, the sac swelling and
pulsing), tongue (mouth gaping, braced, reeling in), creep (the leader in low on its frozen catch, the tongue still out),
devour (reared up against them, forelegs on them, the head wrenching, the jaw snapping), hit.

    tools/models/build.sh ribbit        # this, with its colour baked -> content/art/models/ribbit.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/ribbit.py)
"""
import math
import os
import sys

import numpy as np

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
# The skin's and the jaw's quads (QuadriFlow lands a little under).
SKIN_FACES, JAW_FACES = 2780, 340


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/ribbit.py),
# the names say what to paint and how to sculpt it: the warted back, the paler loose belly, the veined ears.
SKIN = Mat("skin.ribbit", hexc("#9a918c"), shine=0.6)
BELLY = Mat("skin.ribbit_belly", hexc("#b4a9a2"), shine=0.6)
EAR = Mat("skin.ribbit_ear", hexc("#b09490"), shine=0.5)
EYE = Mat("glass_dirty.ribbit_eye", hexc("#c8c6bc"), shine=0.95)
GUM = Mat("flesh.ribbit_gum", hexc("#5a3434"), shine=0.6)
TEETH = Mat("skin.ribbit_teeth", hexc("#c2b89c"), shine=0.4)
TONGUE = Mat("flesh.ribbit_tongue", hexc("#8a4a4c"), shine=0.9)
NAIL = Mat("skin.ribbit_nail", hexc("#a89a84"), shine=0.5)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def h01(*k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 291, 1.0)


# ----------------------------------------------------------------------------------------------------------------
# Flesh: smooth volumes that are a part's solids and a signed-distance field's terms at once.

def _smin(a, b, k):
    """The polynomial smooth minimum: the union of two volumes with the crease between them filled over `k` metres."""
    if k <= 0:
        return np.minimum(a, b)
    h = np.clip(0.5 + 0.5 * (b - a) / k, 0.0, 1.0)
    return b * (1 - h) + a * h - k * h * (1 - h)


def _ellipsoid(c, r, R=None):
    c, r = np.array(c, np.float64), np.array(r, np.float64)
    M = None if R is None else np.array([[R[i][j] for j in range(3)] for i in range(3)], np.float64)

    def f(P):
        q = P - c
        if M is not None:
            q = q @ M
        k0 = np.linalg.norm(q / r, axis=1)
        k1 = np.linalg.norm(q / (r * r), axis=1)
        return k0 * (k0 - 1) / np.maximum(k1, 1e-9)
    return f


def _round_cone(a, b, r1, r2):
    """A limb's length between two joints, tapering from r1 to r2 (rounded at both ends)."""
    a, b = np.array(a, np.float64), np.array(b, np.float64)
    ba = b - a
    l2 = float(ba @ ba)
    rr = r1 - r2
    a2 = l2 - rr * rr
    il2 = 1.0 / l2

    def f(P):
        pa = P - a
        y = pa @ ba
        z = y - l2
        x2 = np.sum((pa * l2 - np.outer(y, ba)) ** 2, axis=1)
        y2 = y * y * l2
        z2 = z * z * l2
        k = math.copysign(1.0, rr) * rr * rr * x2 if rr != 0 else np.zeros_like(x2)
        mid = (np.sqrt(np.maximum(x2 * a2 * il2, 0)) + y * rr) * il2 - r1
        end = np.where(np.sign(z) * a2 * z2 > k, np.sqrt(x2 + z2) * il2 - r2, mid)
        return np.where(np.sign(y) * a2 * y2 < k, np.sqrt(x2 + y2) * il2 - r1, end)
    return f


class Flesh:
    """A skin's volumes: each `blob`/`limb` both models a solid in `part` (rig.fuse's union, and where the skin's
    materials and bone weights come from) and adds a term to the field, smooth-blended over its `k` metres into what's
    there already; `carve` takes a volume out softly (a nostril, the mouth's roof). `settle` moves points onto the
    field's surface (rig.fuse: the union's voxels, before the retopology)."""

    def __init__(self, part):
        self.part = part
        self.ops = []

    def __call__(self, P):
        d = None
        for fn, k, cut in self.ops:
            v = fn(P)
            if d is None:
                d = v if not cut else -v
            elif cut:
                d = -_smin(-d, v, k)
            else:
                d = _smin(d, v, k)
        return d

    def blob(self, c, r, k, mat, bones, rot=None, around=18, rings=10, fmat=None):
        R = None if rot is None else rot.to_3x3()
        self.part.blob(Vector(c), r, around, rings, mat, bones, rot=rot, fmat=fmat)
        self.ops.append((_ellipsoid(c, r, R), k, False))

    def limb(self, points, radii, k, mat, bones, sides=14, ref=(1, 0, 0), fmat=None):
        pts = [Vector(p) for p in points]
        self.part.tube(pts, radii, sides, mat, bones, ref=ref, cap0="point", cap1="point", fmat=fmat)
        cones = [_round_cone(tuple(a), tuple(b), ra, rb) for a, b, ra, rb in zip(pts, pts[1:], radii, radii[1:])]
        self.ops.append((lambda P, cones=cones: np.min([f(P) for f in cones], axis=0), k, False))

    def carve(self, c, r, k, rot=None):
        self.ops.append((_ellipsoid(c, r, None if rot is None else rot.to_3x3()), k, True))

    def surface(self, origin, direction, reach=0.5):
        """Where a ray from `origin` (inside) along `direction` leaves the flesh so far."""
        o, d = np.array(origin, np.float64), np.array(direction, np.float64)
        d /= np.linalg.norm(d)
        lo, hi = 0.0, reach
        for _ in range(40):
            mid = (lo + hi) / 2
            if self(o[None, :] + d[None, :] * mid)[0] < 0:
                lo = mid
            else:
                hi = mid
        return Vector(tuple(o + d * lo))

    def settle(self, P, steps=8):
        P = np.asarray(P, np.float64).copy()
        e = 0.0008
        for _ in range(steps):
            d = self(P)
            g = np.stack([(self(P + np.array(a) * e) - self(P - np.array(a) * e)) / (2 * e)
                          for a in ((1, 0, 0), (0, 1, 0), (0, 0, 1))], axis=1)
            step = (d / np.maximum((g * g).sum(axis=1), 0.05))[:, None] * g
            n = np.linalg.norm(step, axis=1)
            P -= step * np.minimum(1.0, 0.015 / np.maximum(n, 1e-9))[:, None]
        print(f"[dt] ribbit flesh: {len(P)} points settled, worst {np.abs(self(P)).max() * 1000:.2f} mm off")
        return P


def norm(w):
    w = {b: v for b, v in w.items() if v > 0.02}
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def mix(a, b, k):
    """Weights `a` going over to `b` by k (0..1)."""
    out = {n: v * (1 - k) for n, v in a.items()}
    for n, v in b.items():
        out[n] = out.get(n, 0) + v * k
    return norm(out)


def chain(joints, bones, soft=0.035):
    """Weights along a limb: each point its nearest length's bone, blended half and half at a joint over `soft` metres."""
    joints = [Vector(j) for j in joints]

    def fn(p):
        p = Vector(p)
        best, bi, bt = 1e9, 0, 0.0
        for i, (a, b) in enumerate(zip(joints, joints[1:])):
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            d = (a + ab * t - p).length
            if d < best:
                best, bi, bt = d, i, t
        L = (joints[bi + 1] - joints[bi]).length
        w = {bones[bi]: 1.0}
        if bi > 0 and bt * L < soft:
            w = mix(w, {bones[bi - 1]: 1.0}, 0.5 * (1 - bt * L / soft))
        if bi < len(bones) - 1 and (1 - bt) * L < soft:
            w = mix(w, {bones[bi + 1]: 1.0}, 0.5 * (1 - (1 - bt) * L / soft))
        return norm(w)
    return fn


def R(rx=0.0, ry=0.0, rz=0.0):
    return rig.rot(rx, ry, rz).to_matrix()


# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the rump to the snout (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body)
SPINE = along("y", [(-0.46, "pelvis"), (-0.3, "pelvis"), (-0.1, "spine_01"), (0.1, "chest"), (0.26, "chest"),
                    (0.34, "neck"), (0.42, "head")])


def trunk_w(p):
    """Along the spine; the haunches' tops go with the thighs, the shoulders with the arms, the head's underside back
    by the gape with the jaw."""
    p = Vector(p)
    w = SPINE(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.y < 0.06 and ax > 0.1 and p.z < 0.46:
        k = smooth01(0.1, 0.2, ax) * smooth01(0.06, -0.08, p.y) * smooth01(0.46, 0.36, p.z) * 0.75
        w = mix(w, {f"thigh_{s}": 1.0}, k)
    if 0.12 < p.y < 0.36 and ax > 0.1 and p.z < 0.5:
        k = smooth01(0.1, 0.18, ax) * bell((p.y - 0.23) / 0.08) * smooth01(0.5, 0.4, p.z) * 0.5
        w = mix(w, {f"upperarm_{s}": 1.0}, k)
    return w


def head_w(p):
    p = Vector(p)
    w = trunk_w(p)
    if p.y > 0.38 and p.z < 0.6:
        # (Below the lip's line by the gape the cheeks go a little with the jaw: the mouth's corner stretches open.)
        w = mix(w, {"jaw": 1.0}, 0.5 * smooth01(0.6, 0.57, p.z) * smooth01(0.38, 0.44, p.y))
    return w


def under(pts, n):
    """The belly's skin where the body faces down (smoother, paler, loose); the warted skin over the rest."""
    c = sum(pts, Vector()) / len(pts)
    return BELLY if n.z < -0.35 or (n.z < 0.1 and c.z < 0.3 and abs(c.x) < 0.14) else SKIN


# The trunk: a rabbit's hunch, as wide as a toad's: the round rump low over the haunches, the back rising over the loins
# to the shoulders' hump and the thick short neck, the belly slung under it all.
flesh.blob((0, -0.31, 0.34), (0.19, 0.17, 0.17), 0, SKIN, trunk_w, fmat=under)                  # the rump
flesh.blob((0, -0.1, 0.375), (0.205, 0.2, 0.165), 0.08, SKIN, trunk_w, fmat=under)              # the loins
flesh.blob((0, 0.14, 0.42), (0.2, 0.17, 0.15), 0.08, SKIN, trunk_w, fmat=under)                 # the chest
flesh.blob((0, 0.17, 0.515), (0.15, 0.14, 0.075), 0.06, SKIN, trunk_w, fmat=under)              # the shoulders' hump
flesh.blob((0, 0.33, 0.55), (0.165, 0.1, 0.1), 0.07, SKIN, trunk_w, fmat=under)                 # the neck
# The belly: loose, slung low between the legs, a fold where it hangs off the chest and the flanks.
flesh.blob((0, 0.01, 0.245), (0.165, 0.2, 0.085), 0.035, BELLY, trunk_w)
flesh.blob((0, 0.2, 0.3), (0.14, 0.09, 0.07), 0.04, BELLY, trunk_w)
# Starved under the loose skin: the hip bones standing either side of the rump, the shoulder blades, and the spine's
# knuckles in a ridge down the back (each sat on the back where it is).
for sx in (1, -1):
    flesh.blob((sx * 0.105, -0.29, 0.49), (0.038, 0.055, 0.03), 0.025, SKIN, trunk_w)
    flesh.blob((sx * 0.115, 0.12, 0.545), (0.05, 0.085, 0.028), 0.03, SKIN, trunk_w, rot=R(rz=-sx * 12))
for k in range(12):
    y = -0.44 + k * 0.064
    top = flesh.surface((0, y, 0.3), (0, 0, 1))
    flesh.blob(top - Vector((0, 0, 0.012)), (0.022, 0.024, 0.02), 0.014, SKIN, trunk_w, around=10, rings=6)
# The loose skin of the neck in folds across the nape.
for y, z, w in ((0.27, 0.625, 0.12), (0.33, 0.66, 0.13)):
    top = flesh.surface((0, y, 0.45), (0, -0.25, 1))
    flesh.blob(top - Vector((0, 0, 0.012)), (w, 0.018, 0.016), 0.02, SKIN, trunk_w, around=14, rings=6)

def thigh_w(p, s):
    return chain([H(f"thigh_{s}") + Vector((0, -0.08, 0.06)), H(f"calf_{s}")], [f"thigh_{s}"])(p)


# The haunches: a rabbit's, big and bunched along its flanks over the folded hind legs; the knee forward under the
# belly, the shin folded back under the thigh to the heel, and the long hare's foot flat on the ground in front of it.
for s, sx in (("r", 1), ("l", -1)):
    thigh = chain([H(f"thigh_{s}") + Vector((0, -0.08, 0.06)), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")],
                  [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.05)
    flesh.blob((sx * 0.215, -0.16, 0.295), (0.105, 0.205, 0.14), 0.05, SKIN, lambda p, s=s: mix(thigh_w(p, s), trunk_w(p), smooth01(0.36, 0.45, p[2]) * 0.6),
               rot=R(rx=-14, rz=-sx * 6), around=22, rings=12, fmat=under)
    flesh.blob((sx * 0.25, 0.015, 0.215), (0.062, 0.062, 0.062), 0.03, SKIN, thigh, fmat=under)                       # the knee
    flesh.limb([(sx * 0.262, 0.0, 0.2), (sx * 0.264, -0.16, 0.13), (sx * 0.264, -0.3, 0.08)], [0.055, 0.05, 0.04], 0.03, SKIN, thigh)
    flesh.blob((sx * 0.262, -0.315, 0.062), (0.04, 0.05, 0.05), 0.02, SKIN, thigh)                                    # the heel
    flesh.blob((sx * 0.262, -0.125, 0.034), (0.047, 0.215, 0.034), 0.03, BELLY, thigh, around=16, rings=8,
               fmat=lambda pts, n: BELLY if n.z < 0.3 else SKIN)                                                         # the foot
    flesh.blob((sx * 0.262, 0.075, 0.022), (0.05, 0.06, 0.022), 0.02, BELLY, thigh, around=14, rings=6,
               fmat=lambda pts, n: BELLY if n.z < 0.3 else SKIN)                                                         # its ball



# The front legs: a crawling man's arms out of a toad's shoulders, the elbows out, the forearms down to small human
# hands flat on the ground; the palms are the skin's, the fingers their own (`paws`).
PALMS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([sh, el, wr, tip], [f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.04)
    flesh.blob((sx * 0.175, 0.23, 0.405), (0.07, 0.085, 0.095), 0.05, SKIN, lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(0.38, 0.48, p[2]) * 0.7),
               fmat=under)                                                                                               # the shoulder
    flesh.limb([sh + Vector((-sx * 0.02, 0, 0.01)), sh.lerp(el, 0.5), el], [0.062, 0.052, 0.044], 0.035, SKIN, arm)
    flesh.blob(el + Vector((sx * 0.01, -0.012, 0.002)), (0.04, 0.04, 0.042), 0.015, SKIN, arm, around=12, rings=8)  # the elbow
    flesh.limb([el, el.lerp(wr, 0.35), wr], [0.044, 0.04, 0.026], 0.015, SKIN, arm,
               fmat=lambda pts, n: BELLY if n.y < -0.4 else SKIN)
    palm = wr.lerp(tip, 0.45) + Vector((0, 0, 0.002))
    PALMS[s] = palm
    flesh.blob(palm, (0.04, 0.05, 0.018), 0.022, BELLY, arm, around=14, rings=6,
               fmat=lambda pts, n: BELLY if n.z < 0.2 else SKIN)

# ----------------------------------------------------------------------------------------------------------------
# The head: a toad's, broad and flat, a spade seen from above; the eyes up on top in their mounds under heavy lids, the
# glands swollen behind them, the cheeks heavy, a rolled lip right round the mouth; the roof of the mouth vaulted
# inside it. (The jaw is a skin of its own: it opens.)
HC = Vector((0, 0.47, 0.612))
flesh.blob((0, 0.47, 0.655), (0.182, 0.15, 0.07), 0.06, SKIN, head_w, around=22, rings=12)           # the skull
flesh.blob((0, 0.575, 0.645), (0.155, 0.1, 0.056), 0.05, SKIN, head_w, around=20, rings=10)         # the snout
flesh.blob((0, 0.645, 0.637), (0.095, 0.042, 0.037), 0.03, SKIN, head_w)                             # its blunt end
# The upper jaw: the head's broadest at the mouth, overhanging the lower all round (a toad's).
flesh.blob((0, 0.5, 0.626), (0.178, 0.165, 0.034), 0.03, SKIN, head_w, around=22, rings=8)
for sx in (1, -1):
    flesh.blob((sx * 0.155, 0.43, 0.618), (0.052, 0.085, 0.046), 0.04, SKIN, head_w)                 # the jowls
# The upper lip, rolled and heavy, overhanging the jaw: its line found on the skull at the mouth's height.
LIP = [flesh.surface(HC, (math.sin(math.radians(a)), math.cos(math.radians(a)), 0)) for a in range(-108, 109, 12)]
LIP = [p - (p - HC).normalized() * 0.006 for p in LIP]
flesh.limb(LIP, [0.012] + [0.016] * (len(LIP) - 2) + [0.012], 0.012, SKIN, head_w, sides=10, ref=(0, 0, 1))
# The roof of the mouth, vaulted up inside the lip (it shows when the jaw drops).
flesh.carve((0, 0.49, 0.582), (0.15, 0.15, 0.032), 0.012)
EYES = {}
for s, sx in (("r", 1), ("l", -1)):
    flesh.blob((sx * 0.114, 0.5, 0.71), (0.066, 0.066, 0.05), 0.03, SKIN, head_w)                  # the eye's mound
    e = Vector((sx * 0.127, 0.515, 0.744))
    EYES[s] = e
    # Heavy lids, the upper over the eye's top and back (it's never all there), a lower one under it.
    flesh.blob(e + Vector((-sx * 0.004, -0.01, 0.021)), (0.047, 0.044, 0.022), 0.012, SKIN, head_w, around=14, rings=7)
    flesh.blob(e + Vector((sx * 0.004, 0.006, -0.02)), (0.045, 0.042, 0.017), 0.012, SKIN, head_w, around=14, rings=7)
    # A toad's glands behind the eyes, swollen and pitted, running back onto the neck.
    flesh.blob((sx * 0.138, 0.395, 0.703), (0.052, 0.085, 0.036), 0.025, SKIN, head_w, rot=R(rz=sx * 14))
    # The nostrils' rims at the snout's end, and the nostrils.
    flesh.blob((sx * 0.032, 0.656, 0.664), (0.016, 0.014, 0.011), 0.012, SKIN, head_w, around=10, rings=6)
    flesh.carve((sx * 0.033, 0.664, 0.669), (0.008, 0.007, 0.007), 0.004)

# The ears' roots: thick fleshy stalks grown off the back of the skull (the bare blades grow on out of them, `ears`).
EAR_ROOT = {}
for s, sx in (("r", 1), ("l", -1)):
    e0, e1 = H(f"ear_{s}_01"), T(f"ear_{s}_01")
    a, b = e0 + Vector((-sx * 0.02, 0.035, -0.022)), e0.lerp(e1, 0.42)
    EAR_ROOT[s] = (a, b)
    flesh.limb([a, e0.lerp(e1, 0.12), b], [0.042, 0.036, 0.026], 0.03, EAR, lambda p, s=s, e0=e0, e1=e1: mix(
        {"head": 1.0}, {f"ear_{s}_01": 1.0}, smooth01(0.0, 0.3, (Vector(p) - e0).dot((e1 - e0).normalized()) / (e1 - e0).length)),
        sides=12, fmat=lambda pts, n: SKIN if n.z > 0.5 else EAR)

# The throat's sac: under the jaw, loose skin in folds (the bake's), grown out of the throat into the chest. It goes with
# the throat bone (about its top under the chin it swells into a pale veined balloon bigger than the head), the skin
# behind it stretching back to the chest's.
THROAT = Vector((0, 0.47, 0.527))


def sac_w(p):
    p = Vector(p)
    k = smooth01(0.38, 0.47, p.y) * smooth01(0.42, 0.5, p.z)
    return mix(trunk_w(p), {"throat": 1.0}, k)


flesh.blob(THROAT, (0.118, 0.108, 0.062), 0.04, BELLY, sac_w, around=20, rings=10)
flesh.blob(THROAT + Vector((0, -0.03, -0.035)), (0.1, 0.075, 0.045), 0.04, BELLY, sac_w)

# Warts: the big ones raised in the skin itself (the small, in their hundreds, are the bake's), down the back, the
# haunches and the head's top, none on the belly.
WARTS = []
for k in range(26):
    a = h01(k, 1) * 2 * math.pi
    y = -0.42 + 0.84 * h01(k, 2)
    up = Vector((0.75 * math.sin(a * 0.5 + 0.4), 0, 1)).normalized()
    for sx in (1, -1):
        o = Vector((0, y, 0.33))
        d = Vector((sx * up.x, 0, up.z))
        p = flesh.surface(o, d, 0.4)
        if p.z < 0.38:
            continue
        r = 0.011 + 0.01 * h01(k, 3)
        WARTS.append(p)
        flesh.blob(p - d * r * 0.45, (r, r * 1.1, r * 0.8), 0.008, SKIN, trunk_w, around=8, rings=5)
for sx in (1, -1):
    for k in range(5):
        p = flesh.surface((sx * 0.2, -0.2 + 0.07 * k, 0.3), (sx * 0.4, 0.1 * (h01(k, 7) - 0.5), 1), 0.3)
        r = 0.012 + 0.008 * h01(k, 8, sx)
        flesh.blob(p - Vector((0, 0, r * 0.4)), (r, r * 1.1, r * 0.8), 0.008, SKIN, trunk_w, around=8, rings=5)

# ----------------------------------------------------------------------------------------------------------------
# The jaw: a wide heavy scoop under the head, hinged at the back, its rim a rolled lip tucked in under the upper one,
# the floor of the mouth hollowed for the tongue to lie in. Its own skin (rig.fuse): it opens.
jaw = kit.part("jaw")
jflesh = Flesh(jaw)


def jaw_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    return GUM if n.z > 0.6 and c.z > 0.582 and (c - HC).xy.length < 0.13 else BELLY if n.z < -0.4 else SKIN


jflesh.blob((0, 0.505, 0.57), (0.158, 0.145, 0.031), 0, SKIN, "jaw", around=22, rings=10, fmat=jaw_mat)
jflesh.blob((0, 0.6, 0.572), (0.1, 0.055, 0.029), 0.03, SKIN, "jaw", fmat=jaw_mat)                    # the chin
for sx in (1, -1):
    jflesh.blob((sx * 0.13, 0.41, 0.583), (0.038, 0.062, 0.027), 0.03, SKIN, "jaw", fmat=jaw_mat)       # the hinge
JC = Vector((0, 0.47, 0.592))
LOWLIP = [jflesh.surface(JC, (math.sin(math.radians(a)), math.cos(math.radians(a)), 0)) for a in range(-102, 103, 12)]
LOWLIP = [p - (p - JC).normalized() * 0.012 for p in LOWLIP]
jflesh.limb(LOWLIP, [0.008] + [0.01] * (len(LOWLIP) - 2) + [0.008], 0.01, SKIN, "jaw", sides=8, ref=(0, 0, 1))
jflesh.carve((0, 0.49, 0.614), (0.122, 0.12, 0.021), 0.012)                                           # the mouth's floor

# ----------------------------------------------------------------------------------------------------------------
# What's finer than the skin: the eyes, the teeth, the tongue (`head`); the ears' blades; the fingers and the toes.
head = kit.part("head")
for s, e in EYES.items():
    head.blob(e, (0.04, 0.04, 0.037), 12, 8, EYE, "head")
# The teeth: flat, square, human, a row round each jaw just inside the lips; wrong: crooked, uneven, a few gone.
TEETH_AT = []


def tooth_row(lip, centre, n, inset, top, long_, bone, skip, seed):
    for k in range(n):
        if k in skip:
            continue
        t = 0.5 + k * (len(lip) - 2) / (n - 1)
        i0 = min(int(t), len(lip) - 2)
        c = lip[i0].lerp(lip[i0 + 1], t - i0)
        c = c - (c - centre).normalized() * inset
        d = (lip[i0 + 1] - lip[i0]).normalized()
        out = Vector((d.y, -d.x, 0)) * (1 if c.x >= 0 else -1)
        front = bell(c.x / 0.09)
        w = (0.0075 + 0.0035 * front) * (0.8 + 0.4 * h01(k, seed))
        tilt = Vector((0.003 * (h01(k, seed + 1) - 0.5), 0.003 * (h01(k, seed + 2) - 0.5), 0))
        L = long_ * (0.8 + 0.4 * h01(k, seed + 3)) * (0.75 + 0.25 * front)
        base = Vector((c.x, c.y, top))
        tipz = top + (L if top < centre.z else -L)
        TEETH_AT.append(base)
        head.tube([base, Vector((c.x, c.y, tipz)) + tilt], [(w, 0.0055), (w * 0.9, 0.005)], 4, TEETH, bone,
                  ref=(out.x, out.y, 0), cap1=True)


tooth_row(LIP, HC, 16, 0.017, 0.612, 0.02, "head", (4, 11), 10)
tooth_row(LOWLIP, JC, 14, 0.008, 0.584, 0.014, "jaw", (2, 9, 12), 20)
# The tongue: a toad's, broad and flat and wet, a groove down its middle, lying in the mouth's floor; at its end the
# sticky club it catches with, swollen and puckered. Out, tongue_01 is scaled along its length (the club scaled back):
# the tongue shoots out as thick as it lay (clips `tongue`, `creep`; in the game CreatureArt stretches it to its catch).
t0, t1 = H("tongue_01"), T("tongue_01")
TPTS = [t0 + Vector((0, 0.004, -0.004))] + [t0.lerp(t1, k / 6) for k in range(1, 7)]
head.tube(TPTS, [(0.03, 0.011), (0.036, 0.012), (0.038, 0.012), (0.036, 0.011), (0.032, 0.01), (0.028, 0.009), (0.026, 0.009)],
          10, TONGUE, "tongue_01", ref=(0, 0, 1),
          shape=lambda i, j, a, p, fr: Vector(p) - fr[1] * (0.004 * bell(math.sin(a) / 0.25) * (math.cos(a) > 0)))
head.blob(T("tongue_01") + Vector((0, 0.022, 0.002)), (0.04, 0.034, 0.022), 12, 7, TONGUE, "tongue_02",
          shape=lambda i, j, a, th, p: Vector(p) + Vector((0, 0, 0.003 * math.sin(a * 5) * math.sin(th * 4))))

# The ears: a rabbit's, long and bare, cupped along their length, hanging down its back and sides: thin blades grown on
# out of the skin's stalks (their roots sunk in them), thick at the root and thin as a leaf at the end.
ears = kit.part("ears")
for s, sx in (("r", 1), ("l", -1)):
    e0, e1, e2 = H(f"ear_{s}_01"), H(f"ear_{s}_02"), T(f"ear_{s}_02")
    a, b = EAR_ROOT[s]
    bend = Vector((sx * 0.025, -0.01, 0))
    pts = [a.lerp(b, 0.45), b, e1 + Vector((sx * 0.006, 0.01, 0.0)), e1.lerp(e2, 0.25) + bend, e1.lerp(e2, 0.5) + bend * 1.4,
           e1.lerp(e2, 0.72) + bend, e1.lerp(e2, 0.88) + bend * 0.4, e2 + Vector((0, -0.01, -0.012))]
    half = [(0.03, 0.024), (0.036, 0.016), (0.046, 0.01), (0.054, 0.0085), (0.056, 0.008), (0.05, 0.0075), (0.036, 0.007), (0.01, 0.005)]
    cup = [0.0, 0.004, 0.012, 0.02, 0.022, 0.018, 0.01, 0.0]
    ears.tube(pts, half, 12, EAR, lambda p, s=s, e1=e1: mix({f"ear_{s}_01": 1.0}, {f"ear_{s}_02": 1.0}, smooth01(e1.y + 0.03, e1.y - 0.03, p.y)),
              ref=[(sx, 0, 0.35)] * 3 + [(sx, 0.15, 0.25)] * 5, cap1="point",
              shape=lambda i, j, a, p, fr, cup=cup: Vector(p) + fr[1] * (cup[i] * math.sin(a) ** 2))
# Small human hands: four long fingers with their knuckles, the thumb turned in, the nails cracked; the hare's toes.
paws = kit.part("paws")
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    palm, wr, tip = PALMS[s], H(f"hand_{s}"), T(f"hand_{s}")
    for f in range(5):
        thumb = f == 4
        a = math.radians(-sx * 58 if thumb else (f - 1.5) * 14)
        base = palm + Vector((math.sin(a) * (0.03 if thumb else 0.024), 0.02 if not thumb else -0.008, 0.002))
        d = Vector((math.sin(a), math.cos(a), -0.08)).normalized()
        n = (0.042 if thumb else 0.06) * (1 if thumb else 0.82 + 0.18 * (1 - abs(f - 1.5) / 1.5))
        k1 = base + d * n * 0.42 + Vector((0, 0, 0.007))
        end = base + d * n
        end.z = max(0.006, end.z - 0.008)
        TIPS.append(end)
        paws.tube([base - d * 0.014, base, k1, base + d * n * 0.75 + Vector((0, 0, 0.003)), end],
                  [0.0115, 0.0105, 0.0092, 0.0078, 0.0058], 5, BELLY, f"hand_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, nn, end=end: NAIL if (sum(pts, Vector()) / len(pts) - end).length < 0.013 and nn.z > 0.2 else BELLY)
    ball = Vector((sx * 0.262, 0.075, 0.022))
    for k in range(4):
        dx = (k - 1.5) * 0.019
        a0 = ball + Vector((dx, 0.0, 0.0))
        L = 0.075 - 0.012 * abs(k - 1.5)
        end = a0 + Vector((dx * 0.5, L, -0.012))
        TIPS.append(end)
        paws.tube([a0 - Vector((0, 0.02, 0)), a0 + Vector((dx * 0.25, L * 0.5, 0.004)), end], [0.016, 0.0135, 0.008], 5, BELLY,
                  f"toe_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, nn, end=end: NAIL if (sum(pts, Vector()) / len(pts) - end).length < 0.016 and nn.z > 0.2 else BELLY)


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


# One skin from the rump to the snout, the legs, the hands' palms, the ears' roots and the sac grown out of it (rig.fuse,
# settled on the flesh's smooth-blended field, then QuadriFlow); the jaw a second skin the same way. Toward GDD §27's
# budget for a beast (CreatureArtTests: 2-8k), the skin given most of it.
kit.fuse("body", ["body"], voxel=0.003, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.fuse("jaw", ["jaw"], voxel=0.0025, faces=JAW_FACES, lose=0.03, settle=jflesh.settle)
kit.build()
rig.bake(sk, [sit, hop, swell, tongue, creep, devour, hit])
print("[dt] ribbit", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "ribbit.glb"
rig.export(out, kit)
# A distance copy (CreatureArtTests: two fifths or so). (Not when the recipe runs this to bake over it: it exports both.)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
