"""THE GAUNT (GDD v1.2 §21 asleep in villages and yards, App. A.6 · sound (silence)): "A lonely, spindly thing. Wake it
and it follows you home. Every silence makes it angrier, and it hits hard."

Not a man. A thing on stilts: four legs, each longer than a man is tall and jointed three times, the knees standing up
higher than its back like a harvestman's, so stood up it's a cage of grey poles round a starved, narrow body slung three
metres up. From the front of that body a long neck goes up and out, and on the end of it the head: long and narrow and
pale as a horse's skull, no eyes at all, only a round puckered mouth at its tip, and on either side of it an ear, huge,
a bat's ear the size of a door's panel, ribbed and veined and thin enough to see the light through. It listens. It hears
everything; it's waiting for you to stop making a sound. Asleep, it lies down in the middle of its legs, the neck curled
under, the ears folded, and what's left is a heap of dead grey branches by the track.

Its skin is the grey of old wood, dry, cracked like bark; its feet are spikes.

The cars weren't built for it (a car's 2.75 m under its roof, the doorway 2.1): aboard it folds its legs in zig-zags
round itself and creeps, the body near the floor, and squats to listen, the knees jammed up under the roof (CreatureArt
picks by the room; note 118).

SK_Gaunt: root, the body's four spine bones (pelvis to spine_03 at the shoulders), the neck's three, the head, the jaw,
the ears, and the legs' three bones each (thigh up to the high knee, shin down to the hock, cannon down to the spike).
Faces +Y (the engine's -Z). Clips (GDD §31: still, then too fast): sleep (the heap, breathing), stir (the neck coming
up out of it, the ears opening, once), follow (the stilts stepping in slow diagonal pairs, the head carried low and
ahead, ears forward), listen (stood over you, the neck let down to you, the head on one side, the ears cupped at you,
quivering; the engine lets it further down and tips the head over as its anger climbs), attack (it rears up on its hind
legs, the forelegs high, and drives them down into you, too fast), crawl (aboard: low, the legs folded, creeping),
squat (aboard: folded up tight, listening), smash (aboard: the forelegs stabbed out from the squat), hit; carry and carry_low
(leaving with what it took: the load under it in its mouth, note 505).

    tools/models/build.sh gaunt        # this, its high copy and the bake -> content/art/models/gaunt.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# The body, slung high: hips behind, shoulders ahead.
SPINE = [Vector((0, -0.48, 2.0)), Vector((0, -0.18, 2.06)), Vector((0, 0.08, 2.11)), Vector((0, 0.3, 2.15)), Vector((0, 0.46, 2.2))]
NECK = [SPINE[-1], Vector((0, 0.62, 2.5)), Vector((0, 0.78, 2.8)), Vector((0, 0.92, 2.98))]
HEAD_TIP = Vector((0, 1.32, 2.88))
# Each leg (the right's; the left mirrored): its hip, the high knee, the hock, the spike's tip on the ground.
LEGS = {"f": [(0.12, 0.34, 2.1), (0.6, 0.66, 2.78), (0.98, 0.92, 1.36), (1.02, 0.96, 0.0)],
        "b": [(0.12, -0.36, 2.0), (0.62, -0.72, 2.72), (0.98, -1.02, 1.36), (1.02, -1.06, 0.0)]}
bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
for k, name in enumerate(["pelvis", "spine_01", "spine_02", "spine_03"]):
    bones.append(Bone(name, "root" if k == 0 else bones[-1].name, tuple(SPINE[k]), tuple(SPINE[k + 1])))
for k in range(3):
    bones.append(Bone(f"neck_0{k + 1}", "spine_03" if k == 0 else f"neck_0{k}", tuple(NECK[k]), tuple(NECK[k + 1])))
bones.append(Bone("head", "neck_03", tuple(NECK[-1]), tuple(HEAD_TIP)))
bones.append(Bone("jaw", "head", (0, 1.0, 2.93), (0, 1.27, 2.84)))
for s, sx in (("r", 1), ("l", -1)):
    bones.append(Bone(f"ear_{s}", "head", (sx * 0.06, 0.98, 3.02), (sx * 0.3, 0.92, 3.44)))
    for leg, pts in LEGS.items():
        P = [Vector((sx * x, y, z)) for x, y, z in pts]
        parent = "spine_03" if leg == "f" else "pelvis"
        for k, part in enumerate(("thigh", "shin", "cannon")):
            bones.append(Bone(f"{part}_{leg}{s}", parent, tuple(P[k]), tuple(P[k + 1])))
            parent = f"{part}_{leg}{s}"
sk = Skeleton("SK_Gaunt", bones)
sk.build()
kit = rig.Kit(sk, "gaunt")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


BARK = Mat("skin.gaunt", hexc("#6b665e"), shine=0.06)
KNOT = Mat("skin.gaunt_knot", hexc("#57524b"), shine=0.08)
EAR = Mat("skin.gaunt_ear", hexc("#8a7a74"), shine=0.12)
SKULL = Mat("skin.gaunt_face", hexc("#a49d90"), shine=0.12)
MOUTH = Mat("tar.gaunt_mouth", hexc("#1a0c0c"), shine=0.5)
LIP = Mat("skin.gaunt_rim", hexc("#5e3e3a"), shine=0.3)
SPIKE = Mat("tar.gaunt_nail", hexc("#2a241e"), shine=0.4)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def bark(i, j, a, p, fr):
    """Old wood: ridged along its length, knotted."""
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    return p + out * (0.004 * math.sin(a * 5 + i * 1.3) + 0.003 * noise3(p * 18, 91, 1.0))


# --- the body: narrow, starved, the ribs and the spine standing out of it --------------------------------------
body = kit.part("body")


def ribs(i, j, a, p, fr):
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    top = max(0.0, math.cos(a))
    d = 0.012 * max(0.0, math.sin(p.y * 42)) ** 2 * smooth01(-0.2, 0.15, p.y) * (1 - top)    # the ribs, along the chest
    d += 0.026 * top * bell(math.sin(a) / 0.18) * max(0.0, math.sin(p.y * 30)) ** 3             # the spine's knuckles
    t = smooth01(SPINE[0].y, SPINE[-1].y, p.y)
    d += 0.07 * max(0.0, -math.cos(a)) ** 2 * math.sin(math.pi * t)                                 # the belly, slack, hung
    return p + out * (d + 0.004 * noise3(p * 14, 92, 1.0))


rings = []
for k in range(len(SPINE) - 1):
    for f in (0.0, 0.33, 0.66):
        rings.append(SPINE[k].lerp(SPINE[k + 1], f))
rings.append(SPINE[-1])
radii = []
for q in rings:
    t = (q.y - SPINE[0].y) / (SPINE[-1].y - SPINE[0].y)
    radii.append((0.13 + 0.06 * math.sin(math.pi * min(1, t * 1.1)), 0.16 + 0.08 * math.sin(math.pi * t)))
body.tube([rings[0] - Vector((0, 0.1, 0.02))] + rings + [SPINE[-1] + Vector((0, 0.07, 0.04))], [(0.07, 0.08)] + radii + [(0.1, 0.11)], 20, BARK,
          (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0), ref=(0, 0, 1), shape=ribs, cap0=True)
# The neck: the vertebrae standing in it like knots on a pole.
neck = [NECK[0] + Vector((0, -0.02, -0.02))] + [NECK[k].lerp(NECK[k + 1], f) for k in range(3) for f in (0.0, 0.5)] + [NECK[-1]]
body.tube(neck, [0.1, 0.085, 0.072, 0.064, 0.058, 0.055, 0.055, 0.06], 12, BARK, (["spine_03", "neck_01", "neck_02", "neck_03", "head"], 6.0), ref=(1, 0, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.012 * max(0.0, math.cos(a)) * max(0.0, math.sin(i * 2.1)) ** 2)

# --- the head: a long skull, no eyes; the mouth a puckered hole at its tip; the ears ------------------------------
head = kit.part("head")
HC = H("head").lerp(T("head"), 0.45)
along = (T("head") - H("head")).normalized()


def skull(i, j, a, th, p):
    p = Vector(p)
    d = p - HC
    s = d.dot(along) / 0.25
    # A horse's skull's taper: the cranium broad behind, the face drawn out narrow to the snout, the snout hooked down.
    k = 1.18 - 0.62 * smooth01(-0.7, 0.95, s)
    perp = d - along * d.dot(along)
    p = HC + along * d.dot(along) + perp * k + Vector((0, 0, -0.035)) * smooth01(0.3, 1.0, s)
    d = p - HC
    # Long and narrow, a ridge down the top; hollows where eyes would be, and nothing in them; slits for nostrils.
    off = 0.01 * bell((d.z - 0.05) / 0.03) * bell(d.x / 0.02) * (s > -0.6)
    for ex in (-1, 1):
        off -= 0.022 * bell(math.hypot((d.x - ex * 0.06) / 0.028, (s + 0.2) / 0.16)) * (d.z > -0.02)
        off -= 0.006 * bell(math.hypot((d.x - ex * 0.018) / 0.006, (s - 0.62) / 0.12)) * (d.z > 0)
    n = Vector((d.x / 0.09 ** 2, 0, d.z / 0.1 ** 2))
    n = (n.normalized() if n.length > 1e-6 else Vector((0, 0, 1)))
    return p + n * (off + 0.002 * noise3(p * 40, 93, 1.0))


rotm = Vector((0, 1, 0)).rotation_difference(along).to_matrix().to_4x4()
head.blob(HC, (0.09, 0.25, 0.1), 18, 14, SKULL, {"head": 1.0}, rot=rotm, shape=skull)
# The mouth: a ring of puckered lip at the tip, the dark in it, the jaw's lower half under it.
MT = T("head") - along * 0.03
ring = []
for k in range(12):
    t = 2 * math.pi * k / 12
    side = Vector((1, 0, 0))
    upv = along.cross(side).normalized()
    ring.append(MT + side * math.cos(t) * 0.032 + upv * math.sin(t) * 0.028 + along * 0.004 * math.sin(t * 6))
head.tube(ring, [0.011] * 12, 6, LIP, lambda p: {"jaw": 1.0} if (p - MT).dot(along.cross(Vector((1, 0, 0))).normalized()) < -0.005 else {"head": 1.0},
          ref=list(along for _ in range(12)), loop=True)
head.blob(MT - along * 0.01, (0.028, 0.02, 0.024), 10, 5, MOUTH, {"head": 0.5, "jaw": 0.5}, rot=rotm)
# In the pucker, a ring of needle teeth, raked in towards the throat.
side, upv = Vector((1, 0, 0)), along.cross(Vector((1, 0, 0))).normalized()
for k in range(14):
    t = 2 * math.pi * (k + 0.5) / 14
    rim = MT + side * math.cos(t) * 0.024 + upv * math.sin(t) * 0.02 + along * 0.004
    head.tube([rim, rim.lerp(MT - along * 0.03, 0.55)], [0.0035, 0.0004], 4, SKULL, {"head": 1.0} if math.sin(t) > 0 else {"jaw": 1.0}, ref=(1, 0, 0))
head.blob(H("jaw").lerp(T("jaw"), 0.5) + Vector((0, 0, -0.012)), (0.05, 0.12, 0.03), 12, 6, SKULL, "jaw", rot=rotm, z0=-1.0, z1=0.3)
# The ears: thin membranes, each taller than the head is long, cupped forward, pointed like a bat's, the rim scalloped,
# ribbed with cartilage. Modelled in the ear's own frame (z up the ear, y forward, the way it cups).
ER = (0.2, 0.014, 0.3)
for s, sx in (("r", 1), ("l", -1)):
    root, tip = H(f"ear_{s}"), T(f"ear_{s}")
    c = root.lerp(tip, 0.5)
    R = Vector((0, 0, 1)).rotation_difference((tip - root).normalized()).to_matrix()
    Ri = R.inverted()

    def taper(i, j, a, th, p, c=c, R=R, Ri=Ri):
        q = Ri @ (Vector(p) - c)
        u, w = q.x / ER[0], q.z / ER[2]
        # Narrowed to a point at the tip, broad at the root, the rim scalloped between the ribs.
        q.x *= 1.0 - 0.55 * smooth01(-0.2, 1.0, w)
        rim = smooth01(0.55, 1.0, u * u + w * w) * max(0.0, math.sin(math.atan2(w, u) * 4.5)) ** 2
        q.x *= 1.0 - 0.1 * rim
        q.z *= 1.0 - 0.08 * rim
        return c + R @ q

    def cup(p, c=c, R=R, Ri=Ri):
        q = Ri @ (Vector(p) - c)
        q.y += 0.11 * (q.x / ER[0]) ** 2 + 0.03 * (q.z / ER[2]) ** 2 - 0.05     # the cup, open forward
        return c + R @ q

    # Modelled flat (so the loft turns its faces out of the thin shell), then bent into the cup.
    n0 = len(head.v)
    head.blob(c, ER, 20, 14, EAR, f"ear_{s}", rot=R.to_4x4(), shape=taper)
    for k in range(n0, len(head.v)):
        head.v[k] = cup(head.v[k])
    for k in range(5):
        a = -1.1 + 0.55 * k
        lz = 0.9 * math.cos(a)
        lx = 0.9 * math.sin(a) * (1.0 - 0.55 * smooth01(-0.2, 1.0, lz))
        pts = []
        for f in (0.0, 0.45, 1.0):
            q = Vector((lx * ER[0] * f, 0, (-0.85 + (lz + 0.85) * f) * ER[2]))
            q.y = 0.11 * (q.x / ER[0]) ** 2 + 0.03 * (q.z / ER[2]) ** 2 - 0.05 + 0.012
            pts.append(c + R @ q)
        head.tube(pts, [0.011, 0.008, 0.003], 5, KNOT, f"ear_{s}", ref=(0, 1, 0))

# --- the legs: dead branches, gnarled at the joints, ending in spikes -----------------------------------------
# (Note 548: they were dowels of one taper with a ball at every joint, a wooden toy's. Asleep it's to be a heap of dead
# branches, note 132, so each bone's length is a branch: bent and uneven along it, the bark split in fissures, the joints
# gnarled lumps grown over, a broken-off twig's stub here and there, the cannons drawn down to a splintered spike.)
legs = kit.part("legs")


def branch(a, b, r0, r1, bone, seed, rings=9, sides=10, bend=0.04, mat=None, fmat=None, splinter=False):
    """A branch from a to b: bowed off its line (most in its middle), its girth swelling and pinching along it, the bark
    fissured deep along its length."""
    d = (b - a).normalized()
    side = d.cross(Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))).normalized()
    up = d.cross(side).normalized()
    ph = noise3(Vector((seed * 1.7, 0.3, 0.9)), 92, 1.0) * 3.14
    bow = side * math.cos(ph) + up * math.sin(ph)
    pts, rr = [], []
    for k in range(rings + 1):
        t = k / rings
        wob = 0.35 * noise3(Vector((seed * 2.1, t * 3.0, 0.5)), 93, 1.0)
        pts.append(a.lerp(b, t) + bow * (bend * math.sin(math.pi * t) + 0.012 * wob * math.sin(math.pi * t)))
        r = r0 + (r1 - r0) * t
        r *= 1 + 0.18 * noise3(Vector((seed * 3.3, t * 5.0, 1.7)), 94, 1.0)
        rr.append(max(0.002, r))

    def shape(i, j, aa, p, fr):
        p = Vector(p)
        out = fr[0] * math.sin(aa) + fr[1] * math.cos(aa)
        # Fissured: deep cracks running along it between ridges of bark, wandering a little.
        crack = abs(math.sin(aa * 4 + i * 0.35 + seed + 1.2 * noise3(p * 6, 95, 1.0)))
        off = 0.01 * (crack ** 0.5 - 0.6) + 0.004 * noise3(p * 22, 91, 1.0)
        if splinter and i == len(pts) - 2:
            off -= 0.006 * max(0.0, math.sin(aa * 3 + seed))
        return p + out * off * (rr[i] / 0.05)
    return legs.tube(pts, [(r, r) for r in rr], sides, mat or BARK, bone, ref=(0, 1, 0), shape=shape, fmat=fmat, cap1="point" if splinter else False)


def gnarl(c, size, bones, seed):
    """A joint grown over: a lump of knotted wood, wider one way than the other, its burls standing."""
    def jag(i, j, aa, th, p):
        q = Vector(p) - c
        k = 1 + 0.32 * noise3(Vector(p), seed, 24.0) + 0.18 * max(0.0, math.sin(aa * 3 + th * 2 + seed)) ** 3
        return c + q * k
    legs.blob(c, size, 12, 9, KNOT, bones, shape=jag)


def stub(a, d, ln, r, bone, seed):
    """A twig broken off short: out of the branch at a, along d."""
    d = d.normalized()
    legs.tube([a, a + d * ln * 0.6, a + d * ln], [r, r * 0.7, r * 0.45], 6, BARK, bone, ref=(0, 0, 1), cap1=True,
              shape=lambda i, j, aa, p, fr: Vector(p) + (fr[0] * math.sin(aa) + fr[1] * math.cos(aa)) * 0.002 * noise3(Vector(p) * 30, seed, 1.0))


for s, sx in (("r", 1), ("l", -1)):
    for n, leg in enumerate(LEGS):
        seed = n * 2 + (s == "l") + 1
        th, sh, ca = f"thigh_{leg}{s}", f"shin_{leg}{s}", f"cannon_{leg}{s}"
        branch(H(th) + (H(th) - T(th)).normalized() * 0.05, T(th), 0.072, 0.046, th, seed, rings=10, bend=0.05)
        gnarl(T(th), (0.072, 0.064, 0.082), {th: 0.5, sh: 0.5}, 96 + seed)
        branch(H(sh), T(sh), 0.047, 0.029, sh, seed + 10, rings=10, bend=-0.045)
        gnarl(T(sh), (0.048, 0.044, 0.056), {sh: 0.5, ca: 0.5}, 106 + seed)
        branch(H(ca), T(ca), 0.029, 0.006, ca, seed + 20, rings=9, sides=8, bend=0.03, splinter=True,
               fmat=lambda pts, nn: SPIKE if sum(pts, Vector()).z / len(pts) < 0.14 else BARK)
        # A twig's stub or two off the thigh and the shin, pointing out and up, away from the body.
        out = Vector((sx, 0, 0.6))
        stub(H(th).lerp(T(th), 0.4 + 0.1 * (seed % 3)), out + Vector((0, 0.3 * (1 if leg == "f" else -1), 0)), 0.15, 0.018, th, 120 + seed)
        stub(H(sh).lerp(T(sh), 0.3 + 0.15 * (seed % 2)), Vector((sx, 0.2, -0.2)), 0.11, 0.013, sh, 130 + seed)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes: a bone's angles in the armature's axes (rig.rot). The legs' thighs and shins go by IK (rig.reach) to put
# the hock where it's wanted; the cannon hangs plumb under it (rig.hang), its spike on the ground, unless a clip lays it
# down.
LEGNAMES = [(leg, s) for leg in LEGS for s in ("r", "l")]
CANNON = {leg: (Vector(LEGS[leg][2]) - Vector(LEGS[leg][3])).length for leg in LEGS}


def leg_to(pose, leg, s, hock, knee_low=None, knee_high=None):
    """The thigh and shin set so the hock is at `hock` (armature space), the knee bent up and out (or held under
    `knee_low` / over `knee_high`), from a spread of starts."""
    th, sh = f"thigh_{leg}{s}", f"shin_{leg}{s}"
    sign = 1 if s == "r" else -1
    hip = rig.pose_points(sk, pose, [(th, "head")])[0]

    def avoid(knee):
        c = 0.0
        if knee_low is not None and knee.z > knee_low:
            c += (knee.z - knee_low) * 4
        if knee_high is not None and knee.z < knee_high:
            c += (knee_high - knee.z) * 4
        c += max(0.0, -(knee.x - hip.x) * sign) * 4   # the knee out to its own side, never across under the body
        return c
    best = None
    for x in (-50, 0, 50):
        for y in (-40, 0, 40):
            for b in (1, -1):
                start = dict(pose) | {th: (x, y, 0), sh: (0, 0, 0)}
                p = rig.reach(sk, start, th, sh, hock, elbow_axis=1, bend=b, avoid=avoid)
                tip, knee = rig.pose_points(sk, p, [(sh, "tail"), (sh, "head")])
                cost = (tip - Vector(hock)).length + avoid(knee)
                if best is None or cost < best[0]:
                    best = (cost, p)
    return best[1]


def stand_on(pose, feet, hocks=None, **knees):
    """Each leg's spike on its foot point. The hock right over it at the cannon's length, the cannon hanging plumb; or,
    for the legs in `hocks` {leg+side: height}, the hock that high and the cannon slanted out from it to the foot, away
    from the hip (folded, or laid down nearly flat)."""
    p = dict(pose)
    hocks = hocks or {}
    for (leg, s), foot in feet.items():
        th, sh, name = f"thigh_{leg}{s}", f"shin_{leg}{s}", f"cannon_{leg}{s}"
        foot, L = Vector(foot), CANNON[leg]
        rest = sk[name].tail - sk[name].head
        if (leg + s) in hocks:
            h = min(hocks[leg + s], L * 0.98)
            hip = rig.pose_points(sk, p, [(th, "head")])[0]
            u = Vector((foot.x - hip.x, foot.y - hip.y, 0)).normalized()
            hock = foot - u * math.sqrt(L * L - h * h) + Vector((0, 0, h))
        else:
            hock = foot + Vector((0, 0, L))
        p = leg_to(p, leg, s, hock, **knees.get(leg + s, {}))
        q = rest.normalized().rotation_difference((foot - hock).normalized()) if (leg + s) in hocks else rig.rot(0, 0, 0)
        p[name] = rig.world_rotation(sk, p, sh).inverted() @ q
    return p


def rest_feet(spread=1.0, dy=0.0):
    out = {}
    for leg, pts in LEGS.items():
        x, y, _ = pts[3]
        for s, sx in (("r", 1), ("l", -1)):
            out[(leg, s)] = (sx * x * spread, y * spread + dy, 0.0)
    return out


def euler_of(q):
    e = q.to_euler("YXZ")
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


def done(p, ear=(0.0, 0.0), quiver=0.0):
    """The ears set (they're held level whatever the skull does, the way a listening thing holds them: rx back, rz out,
    `quiver` the two out of step), and rig.hang's quaternions as the clips' degree triples (rig.rot's YXZ order)."""
    p = dict(p)
    rx, rz = ear
    p["ear_r"] = rig.hang(sk, p, "ear_r", rx + quiver, 0, -rz)
    p["ear_l"] = rig.hang(sk, p, "ear_l", rx - quiver, 0, rz)
    return {k: (euler_of(v) if hasattr(v, "to_euler") else v) for k, v in p.items()}


def gait(base, phase, stride, lift):
    """Slow diagonal pairs (the right fore with the left hind): half the cycle swinging, lifted and carried forward;
    half planted, going back under it."""
    feet = {}
    for (leg, s), foot in base.items():
        a = (leg == "f") == (s == "r")
        ph = (phase + (0.0 if a else 0.5)) % 1.0
        if ph < 0.5:
            u = ph / 0.5
            dy, up = stride * (u - 0.5) * 2, lift * math.sin(math.pi * u)
        else:
            u = (ph - 0.5) / 0.5
            dy, up = stride * (0.5 - u) * 2, 0.0
        feet[(leg, s)] = (foot[0], foot[1] + dy, up)
    return feet


EARS = (-8, 6)
STAND = done(stand_on({"root@loc": (0, 0, 0)}, rest_feet()), EARS)

# Listen (3 s, loop): stood over you, the body let down a little at the front, the neck let down to you and the head on
# one side, the ears cupped at you; dead still but for the ears' quiver, and once the head turning to the other side.
LISTEN_BODY = {"root@loc": (0, 0, -0.18), "pelvis": (4, 0, 0), "spine_02": (-4, 0, 0), "spine_03": (-8, 0, 0),
               "neck_01": (-12, 0, 0), "neck_02": (-18, 0, 0), "neck_03": (-14, 0, 0), "head": (-34, 0, 18), "jaw": (-6, 0, 0)}
LISTEN_LEGS = stand_on(LISTEN_BODY, rest_feet(1.0, 0.1))
LEAR = (-18, 14)
LISTEN = done(LISTEN_LEGS, LEAR)
listen = Clip("listen")
listen.key(0, LISTEN, "CONSTANT")
for f, k in ((14, 1), (15, -1), (16, 0), (40, 1), (41, 0)):
    listen.key(f, done(LISTEN_LEGS, LEAR, 6 * k), "CONSTANT")
listen.key(54, LISTEN, "BEZIER")
listen.key(64, done(over(LISTEN_LEGS, head=(-34, 0, -22)), LEAR), "BEZIER")
listen.key(78, done(over(LISTEN_LEGS, head=(-34, 0, -22)), LEAR), "CONSTANT")
listen.key(80, LISTEN, "CONSTANT")
listen.close(90)

# Follow (1.6 s, loop): stalking on its stilts, each leg lifted high and set down ahead; the body swaying between them,
# the head carried low and out ahead, the ears up and forward.
FOLLOW_BODY = {"root@loc": (0, 0, -0.08), "spine_03": (-4, 0, 0), "neck_01": (-24, 0, 0), "neck_02": (-16, 0, 0), "neck_03": (-6, 0, 0),
               "head": (-14, 0, 0)}
follow = Clip("follow")
for f, phase in ((0, 0.0), (12, 0.25), (24, 0.5), (36, 0.75)):
    body = over(FOLLOW_BODY, root__loc=(0.03 * math.sin(phase * 2 * math.pi), 0, -0.08 + 0.03 * math.cos(phase * 4 * math.pi)),
                spine_02=(0, 3 * math.sin(phase * 2 * math.pi), 0), head=(-14, 0, -6 * math.sin(phase * 2 * math.pi)))
    follow.key(f, done(stand_on(body, gait(rest_feet(), phase, 0.4, 0.35)), (-12, 4)), "LINEAR")
follow.close(48)

# Attack (2.4 s, loop): up on its hind legs, the body tipped back, the forelegs lifted high over its head, too slowly;
# held; then driven down into you, too fast, the spikes in the ground where you stood, the head after them; pulled out,
# and up again.
REAR_BODY = {"root@loc": (0, -0.25, 0.25), "pelvis": (24, 0, 0), "spine_01": (8, 0, 0), "spine_02": (6, 0, 0), "spine_03": (4, 0, 0),
             "neck_01": (6, 0, 0), "neck_02": (-6, 0, 0), "head": (-30, 0, 0), "jaw": (-30, 0, 0)}
hind = {k: v for k, v in rest_feet(1.0, -0.2).items() if k[0] == "b"}
REAR = stand_on(REAR_BODY, hind)
for s, sx in (("r", 1), ("l", -1)):
    REAR = leg_to(REAR, "f", s, (sx * 0.5, 1.1, 3.3), knee_high=3.2)
    REAR[f"cannon_f{s}"] = rig.hang(sk, REAR, f"cannon_f{s}", -40, 0, 0)
REAR = done(REAR, (40, 20))
STAB_BODY = {"root@loc": (0, 0.1, -0.15), "pelvis": (-6, 0, 0), "spine_02": (-8, 0, 0), "spine_03": (-12, 0, 0), "neck_01": (-40, 0, 0),
             "neck_02": (-24, 0, 0), "head": (-20, 0, 0), "jaw": (-40, 0, 0)}
STAB = stand_on(STAB_BODY, hind)
for s, sx in (("r", 1), ("l", -1)):
    STAB = leg_to(STAB, "f", s, (sx * 0.3, 1.5, 1.2), knee_high=2.2)
    STAB[f"cannon_f{s}"] = rig.hang(sk, STAB, f"cannon_f{s}", 20, 0, 0)
STAB_OPEN = done(over(STAB, jaw=(-20, 0, 0)), (20, 10))
STAB = done(STAB, (20, 10))
attack = Clip("attack")
attack.key(0, STAND, "BEZIER")
attack.key(20, REAR, "BEZIER")
attack.key(30, REAR, "CONSTANT")
attack.key(33, STAB, "LINEAR")
attack.key(42, STAB_OPEN, "BEZIER")
attack.key(54, STAB_OPEN, "BEZIER")
attack.close(72)


# Aboard: the legs folded (the knees jammed up high, the hocks down near the floor and the cannons slanted out along it
# fore and aft), the body down near the floor: a thing on stilts packed into a box.
def low_feet(spread, reach, dy=0.0):
    out = {}
    for (leg, s), _ in rest_feet().items():
        sx = 1 if s == "r" else -1
        out[(leg, s)] = (sx * spread, (reach if leg == "f" else -reach - 0.1) + dy, 0.0)
    return out


ALL = [leg + s for leg, s in LEGNAMES]
CRAWL_BODY = {"root@loc": (0, 0, -1.28), "spine_03": (-6, 0, 0), "neck_01": (-50, 0, 0), "neck_02": (-20, 0, 0), "neck_03": (4, 0, 0),
              "head": (-6, 0, 0)}
crawl = Clip("crawl")
for f, phase in ((0, 0.0), (10, 0.25), (20, 0.5), (30, 0.75)):
    p = stand_on(over(CRAWL_BODY, root__loc=(0, 0, -1.28 + 0.02 * math.cos(phase * 4 * math.pi)), head=(-6, 0, 8 * math.sin(phase * 2 * math.pi))),
                 gait(low_feet(0.85, 2.2), phase, 0.3, 0.2), hocks={k: 0.35 for k in ALL}, **{k: {"knee_low": 1.5} for k in ALL})
    crawl.key(f, done(p, (30, 20)), "LINEAR")
    crawl_probe = done(p, (30, 20))
crawl.close(40)

# Squat (3 s, loop): folded up tight in the car, the knees jammed up under the roof round it like a cage, the neck up out
# of the fold and the head on one side, the ears open, listening; still, then the ears flinch.
SQUAT_BODY = {"root@loc": (0, 0, -1.3), "pelvis": (-6, 0, 0), "neck_01": (-26, 0, 0), "neck_02": (-12, 0, 0), "neck_03": (-6, 0, 0),
              "head": (-20, 0, 20)}
SQUAT_KNEES = {k: {"knee_low": 1.62, "knee_high": 1.45} for k in ALL}
SQUAT_LEGS = stand_on(SQUAT_BODY, low_feet(0.8, 1.9), hocks={k: 0.6 for k in ALL}, **SQUAT_KNEES)
SQUAT = done(SQUAT_LEGS, (-10, 18))
squat = Clip("squat")
squat.key(0, SQUAT, "CONSTANT")
squat.key(40, done(over(SQUAT_LEGS, head=(-20, 0, -18)), (-10, 18)), "BEZIER")
squat.key(56, done(over(SQUAT_LEGS, head=(-20, 0, -18)), (-10, 18)), "CONSTANT")
squat.key(58, done(over(SQUAT_LEGS, head=(-20, 0, -18)), (30, 4)), "CONSTANT")
squat.key(62, SQUAT, "BEZIER")
squat.close(90)

# Smash (1 s, loop): from the squat, the forelegs stabbed out ahead and down along the floor, one and then the other,
# too fast.
smash = Clip("smash")
for f, which in ((0, None), (6, "r"), (10, None), (16, "l"), (20, None)):
    p = SQUAT
    if which:
        sx = 1 if which == "r" else -1
        feet = low_feet(0.8, 1.9) | {("f", which): (sx * 0.25, 2.7, 0.0)}
        p = stand_on(SQUAT_BODY | {"neck_01": (-30, 0, 0), "jaw": (-30, 0, 0)}, feet, hocks={k: 0.6 for k in ALL} | {"f" + which: 0.25}, **SQUAT_KNEES)
        p = done(p, (24, 10))
    smash.key(f, p, "CONSTANT" if which else "LINEAR")
smash.close(30)

# Sleep (4 s, loop): lain down in the middle of its legs, the legs out round it in a tangle on the ground like fallen
# branches, the neck curled under, the ears folded flat; breathing, slowly.
SLEEP_BODY = {"root@loc": (0, 0, -1.68), "pelvis": (0, 4, 0), "spine_02": (0, -3, 0), "neck_01": (-70, 0, 30), "neck_02": (-40, 0, 20),
              "neck_03": (-10, 0, 10), "head": (10, 0, 30)}
heap = {("f", "r"): (1.6, 1.5, 0.0), ("f", "l"): (-1.3, 1.9, 0.0), ("b", "r"): (1.8, -1.0, 0.0), ("b", "l"): (-1.6, -1.5, 0.0)}
SLEEP_LEGS = stand_on(SLEEP_BODY, heap, hocks={k: 0.1 for k in ALL}, **{k: {"knee_low": 0.9} for k in ALL})
FLAT = (75, -10)
SLEEP = done(SLEEP_LEGS, FLAT)
sleep = Clip("sleep")
sleep.key(0, SLEEP, "BEZIER")
sleep.key(60, done(over(SLEEP_LEGS, spine_01=(3, 0, 0), spine_02=(3, -3, 0)), FLAT), "BEZIER")
sleep.close(120)

# Stir (2 s, once): the neck coming up out of the heap, too slowly, the head lifting, the ears opening; held.
STIR = over(SLEEP_LEGS, neck_01=(-10, 0, 10), neck_02=(-4, 0, 0), neck_03=(0, 0, 0), head=(-20, 0, 26))
stir = Clip("stir", loop=False)
stir.key(0, SLEEP, "BEZIER")
stir.key(40, done(STIR, (-6, 14)), "BEZIER")
stir.key(60, done(over(STIR, head=(-20, 0, -10)), (-14, 14)), "CONSTANT")

# Hit (0.5 s, once): struck, it shudders through its whole length, the ears flattened, and holds again.
hit = Clip("hit", loop=False)
hit.key(0, LISTEN, "CONSTANT")
hit.key(2, done(over(LISTEN_LEGS, spine_02=(8, 6, 10), neck_01=(-10, 0, 20), head=(-10, 0, 40)), (60, 0)), "CONSTANT")
hit.key(8, done(over(LISTEN_LEGS, spine_02=(-4, -4, -6)), (30, 10)), "LINEAR")
hit.key(14, LISTEN, "CONSTANT")



# Carrying off what it took (App. A.6: "it carries the body out at walking pace, in full view"; note 505): the sim holds
# the load under its body (Gaunt.Carry: carryHigh 1.7 m over its feet, aboard carryLow 0.55), so the neck goes down
# and back under it and the mouth clamps on the load's top, as a cat carries a kitten. The neck has no IK of its own:
# mouth_to searches its three bends and the head's for the mouth's place.
NECKS = ["neck_01", "neck_02", "neck_03", "head"]


def mouth_to(pose, target, aim=(0.0, -0.35, -1.0)):
    """The neck curled and the head set so the head's tip (the mouth) is at `target` (armature space), the head
    pointing along `aim`: a coordinate search over the four bends (pitch only), from a spread of starts."""
    target, aim = Vector(target), Vector(aim).normalized()

    def posed(a):
        return over(pose, **{n: (a[k], 0, 0) for k, n in enumerate(NECKS)})

    def seg(p, a, b):
        ab = b - a
        u = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.dot(ab))))
        return (p - (a + ab * u)).length

    def cost(a):
        tip, base, n0, n1, jaw = rig.pose_points(sk, posed(a), [("head", "tail"), ("head", "head"), ("neck_01", "head"), ("neck_01", "tail"),
                                                               ("jaw", "tail")])
        # Not folded back into its own neck (art clearance): the head and the jaw kept a hand's breadth off its root.
        near = min(seg(q, n0, n1) for q in (tip, (tip + base) / 2, jaw))
        return (tip - target).length + 0.05 * (1 - (tip - base).normalized().dot(aim)) + 2.0 * max(0.0, 0.2 - near)
    best = None
    for start in ((-60, -40, -30, -20), (-100, -50, -20, -10), (-40, -70, -60, -30), (-130, -40, 0, 10), (-90, -90, -60, -40),
                  (-150, -80, -40, -20), (-120, -110, -30, 0), (-178, -90, -90, -30), (40, -170, -100, -40), (-60, -178, -100, 0)):
        a, c, step = list(start), cost(start), 16.0
        while step > 0.25:
            moved = False
            for k in range(4):
                for dv in (step, -step):
                    t = list(a)
                    t[k] = max(-178.0, min(90.0, t[k] + dv))
                    ct = cost(t)
                    if ct < c:
                        a, c, moved = t, ct, True
            if not moved:
                step /= 2
        if best is None or c < best[0]:
            best = (c, a)
    return posed(best[1]), best[0]


CARRY_HIGH, CARRY_LOW = 1.7, 0.55   # enemies.json gaunt.carryHigh, carryLow
# Where on the load the mouth takes hold: in front of it, under the chest, where the neck's seen hanging down to it (the
# load's centre is under the body; a crewmate's draped over its underside, a crate's face is a quarter metre ahead).
GRIP_HIGH = Vector((0.0, 0.36, CARRY_HIGH - 0.15))
AIM_HIGH = (0.0, -1.0, -0.25)
GRIP_LOW = Vector((0.0, 0.5, CARRY_LOW + 0.1))
JAW_SHUT = (-14, 0, 0)

# Carry (1.6 s, loop): the follow's stalk, slower to lift its feet, the body let down under the weight, the neck down
# under it and the load in its mouth, swaying with it.
CARRY_BODY = over(FOLLOW_BODY, root__loc=(0, 0, -0.14), spine_03=(2, 0, 0), jaw=JAW_SHUT)
carry = Clip("carry")
carry_worst = 0.0
for f, phase in ((0, 0.0), (12, 0.25), (24, 0.5), (36, 0.75)):
    sway, bob = 0.03 * math.sin(phase * 2 * math.pi), 0.03 * math.cos(phase * 4 * math.pi)
    body = over(CARRY_BODY, root__loc=(sway, 0, -0.14 + bob), spine_02=(0, 3 * math.sin(phase * 2 * math.pi), 0))
    body, miss = mouth_to(body, GRIP_HIGH + Vector((sway, 0, bob)), aim=AIM_HIGH)
    carry_worst = max(carry_worst, miss)
    print(f"[dt] gaunt carry {f}: {miss:.3f}")
    carry.key(f, done(stand_on(body, gait(rest_feet(), phase, 0.32, 0.28)), (-4, 10)), "LINEAR")
carry.close(48)

# Carry low (1.3 s, loop): aboard, the crawl's fold, the load dragged under its chest in its mouth.
CARRY_LOW_BODY = over(CRAWL_BODY, jaw=JAW_SHUT)
carry_low = Clip("carry_low")
for f, phase in ((0, 0.0), (10, 0.25), (20, 0.5), (30, 0.75)):
    bob = 0.02 * math.cos(phase * 4 * math.pi)
    body = over(CARRY_LOW_BODY, root__loc=(0, 0, -1.28 + bob))
    body, miss = mouth_to(body, GRIP_LOW + Vector((0, 0, bob)), aim=(0.0, 0.2, -1.0))
    carry_worst = max(carry_worst, miss)
    print(f"[dt] gaunt carry_low {f}: {miss:.3f}")
    p = stand_on(body, gait(low_feet(0.85, 2.2), phase, 0.26, 0.18), hocks={k: 0.35 for k in ALL}, **{k: {"knee_low": 1.5} for k in ALL})
    carry_low.key(f, done(p, (30, 20)), "LINEAR")
carry_low.close(40)
print(f"[dt] gaunt carry: the mouth within {carry_worst:.3f} of its hold")


def tops(name, p):
    """(Checked against GauntTests: its highest joint in a pose, the ears' tips included.)"""
    pts = rig.pose_points(sk, p, [(b.name, "tail") for b in sk.bones] + [(b.name, "head") for b in sk.bones])
    print(f"[dt] gaunt {name} top {max(q.z for q in pts):.2f}")


for name, p in (("squat", SQUAT), ("crawl", crawl_probe), ("listen", LISTEN), ("sleep", SLEEP)):
    tops(name, p)
kit.build()
rig.bake(sk, [sleep, stir, follow, listen, attack, crawl, squat, smash, hit, carry, carry_low])
print("[dt] gaunt", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "gaunt.glb", kit)
