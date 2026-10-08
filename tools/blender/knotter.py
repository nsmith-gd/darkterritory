"""THE KNOTTER (GDD §21 structural, App. A.4 · vibration; docs/design/creatures/knotter.md §3; ARCHITECTURE §8 note 365): a
rope-bodied parasite that becomes the coupling.

A length of ship's hawser come alive: a body thick as a thigh, laid up of three twisted strands of pale grey-white flesh
exactly like a hawser's lay, the strands' ridges worn and dirty; black whipping (tarred twine) bound round it every metre
or so; rows of small pale hooked legs along its underside like a centipede's; and each end a knot, the strands wrung
round into a fist, clustered with ten short thick jointed grey claws hooked in like a fist's knuckles, that clamp the cars'
end sills. Stretched across a coupling it's 5 m long and taut.

How it's made (the Look Review: organic, not boxes; the Gannet's and the Ribbit's way, notes 340, 362): its body is one
skin (tools/blender/flesh.py): the three strands laid up round each other (each a term of a signed-distance field, their
grooves kept sharp where they meet), the whipping's bands swelling the lay, each end's knot (the strands swelling and
wrung round tighter into a fist, one turned back over it) and the roots of the claws growing out of it: the union of their solids settled onto that field,
QuadriFlowed and smooth-shaded, so the strands run into the knots and the knots into the claws with no seam. Over it: the
legs in their two rows, and the claws' jointed fingers with their hooked horn tips. Its colour (and the strands' fibres,
the twine's turns and the grime, too fine for the mesh) is baked into one atlas by tools/models/recipes/knotter.py; built
alone (this script), it wears the shared tiling textures.

SK_Knotter: a root at its middle, 33 segments along it (flat under the root: CreatureArt lays them along the gap's span,
however wide it's been forced, sagging or coiled), and at each end the knot's bone and its ten claws. At rest it
lies straight along +Y (the engine's -Z), 4.8 m from knot to knot and 5.4 m claw tip to claw tip, its middle on the origin:
the origin is the gap's middle at the coupler's height (the sim's Local). A large monster's budget (GDD §27). Clips (§31:
still, then abrupt; laid along the span by the engine): creep (writhing up out from under, the claws feeling), clamp
(the claws closing on the sills), force (swelling and twisting, the cars pushed apart), taut (held, the lay creaking round),
slip (a strand rolling under a foot), coil (squeezing what it's wrapped round), exposed (slack, writhing), hit, death
(unlaid: the strands twisting loose, the claws springing open, limp).

    tools/models/build.sh knotter        # this, with its colour baked -> content/art/models/knotter.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/knotter.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
import flesh as fl  # noqa: E402
import numpy as np  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()

# The brief's numbers (knotter.md §3, §9), printed at the end and pinned by CreatureArtTests: 5 m across a forced coupling
# (enemies.json knotter.gap), thick as a thigh.
SPAN = 5.0
SEGMENTS = 33
END = 2.4                   # its knots' centres either side of the middle (m along it)
REACH = 2.7                 # its claws' tips
RADIUS = 0.104              # the lay's outside, over the strands' ridges
LAY_R, STRAND_R = 0.054, 0.05
PITCH = 0.42                # one turn of the lay (m)
WHIP = [-1.6, -0.55, 0.55, 1.6]  # the whipping's bands, along it
CLAWS = 10                  # each knot's clawed fingers


def skeleton():
    b = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
    step = 2 * END / (SEGMENTS - 1)
    for i in range(SEGMENTS):
        y = END - i * step
        b.append(Bone(f"seg_{i:02d}", "root", (0, y, 0), (0, y - step, 0)))
    for end, sy, seg in (("f", 1, 0), ("b", -1, SEGMENTS - 1)):
        y0 = sy * END
        b.append(Bone(f"knot_{end}", f"seg_{seg:02d}", (0, y0, 0), (0, y0 + sy * 0.12, 0)))
        for k in range(CLAWS):
            # Clustered round the knot's face, short, pointing out of it as a fist's knuckles do.
            a = 2 * math.pi * k / CLAWS + 0.3
            r = 0.1 if k % 2 else 0.065
            root = Vector((math.sin(a) * r, y0 + sy * (0.1 if k % 2 else 0.13), math.cos(a) * r))
            tip = root + Vector((math.sin(a) * 0.035, sy * 0.07, math.cos(a) * 0.035))
            b.append(Bone(f"claw_{end}{k}", f"knot_{end}", tuple(root), tuple(tip)))
    return Skeleton("SK_Knotter", b)


sk = skeleton()
sk.build()
kit = rig.Kit(sk, "knotter")
SKIN_FACES = 3600
STEP = 2 * END / (SEGMENTS - 1)

FLESH = Mat("flesh.knotter", hexc("#b8b2a6"), shine=0.35)
UNDER = Mat("flesh.knotter_under", hexc("#c4bdb2"), shine=0.35)
WHIPPING = Mat("tar.knotter_whip", hexc("#141210"), shine=0.3)
LEG = Mat("skin.knotter_leg", hexc("#c8c0b0"), shine=0.4)
CLAW = Mat("tar.knotter_claw", hexc("#4a4640"), shine=0.5)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def h01(*k):
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 365, 1.0)


def along(p):
    """Weights along it: the two segments either side of a point, by where it is between their heads."""
    y = Vector(p).y
    u = (END - y) / STEP
    i = int(math.floor(u))
    if i < 0:
        return {"seg_00": 1.0}
    if i >= SEGMENTS - 1:
        return {f"seg_{SEGMENTS - 1:02d}": 1.0}
    t = u - i
    w = {f"seg_{i:02d}": 1 - t, f"seg_{i + 1:02d}": t}
    return {n: v for n, v in w.items() if v > 0.02}


def end_w(end):
    """The knot's own (it turns on the end of the lay)."""
    seg = "seg_00" if end == "f" else f"seg_{SEGMENTS - 1:02d}"
    sy = 1 if end == "f" else -1

    def fn(p):
        y = Vector(p).y * sy
        k = smooth01(END - 0.1, END + 0.02, y)
        w = {seg: 1 - k, f"knot_{end}": k}
        return {n: v for n, v in w.items() if v > 0.02}
    return fn


def under(pts, n):
    return UNDER if n.z < -0.55 else FLESH


# ----------------------------------------------------------------------------------------------------------------
# The skin: the lay, the whipping, the knots and the claws' roots (flesh.fuse, settled on `F`).
body = kit.part("body")
F = fl.Flesh(body, "knotter flesh")


KNOT_TWIST = 1.3            # the extra turns the strands take round each knot, wrung tight into the fist


def twist(y):
    """The strands' extra turn at y: none along the lay, wrung round tighter and tighter into each knot."""
    t = min(1.0, max(0.0, (abs(y) - (END - 0.2)) / 0.3))
    return math.copysign(2 * math.pi * KNOT_TWIST * t * t * (3 - 2 * t), y)


def strand_at(y, k):
    """The centre of strand k at y along the lay (turning right-handed, as a hawser's)."""
    a = 2 * math.pi * y / PITCH + 2 * math.pi * k / 3 + twist(y)
    return math.cos(a) * LAY_R, math.sin(a) * LAY_R


# The lay as a solid (for the union, the materials and the weights): a tube whose section is the three strands' outline,
# turning with them.
KNOT_LEN = END + 0.1         # the lay runs on into each knot, swelling into it


def swell(y):
    """How much fatter the lay is at y: at each end it bunches up into the knot, the strands thrown round in a fist."""
    return 1 + 0.9 * math.exp(-((abs(y) - END) / 0.09) ** 2)


rings, cents = [], []
n_around = 30
for i in range(int(2 * KNOT_LEN / 0.03) + 1):
    y = KNOT_LEN - i * 0.03
    s = swell(y)
    ring = []
    for j in range(n_around):
        th = 2 * math.pi * j / n_around
        # Out to the furthest strand's edge along this direction.
        r = 0.0
        for k in range(3):
            cx, cz = strand_at(y, k)
            cx, cz = cx * s, cz * s
            dx, dz = math.cos(th), math.sin(th)
            proj = cx * dx + cz * dz
            perp2 = (cx * cx + cz * cz) - proj * proj
            if perp2 < (STRAND_R * s) ** 2:
                r = max(r, proj + math.sqrt((STRAND_R * s) ** 2 - perp2))
        ring.append(Vector((math.cos(th) * r, y, math.sin(th) * r)))
    rings.append(ring)
    cents.append(Vector((0, y, 0)))
body.loft(rings, FLESH, along, centres=cents, cap0=True, cap1=True, fmat=under)

COS_ALPHA = math.cos(math.atan(2 * math.pi * LAY_R / PITCH))


def lay(P):
    """The three strands' field (each a helix's tube, measured across the lay: its section there an ellipse), their grooves
    blended over only a few millimetres; and none of it past its knots."""
    y = P[:, 1]
    s = 1 + 0.9 * np.exp(-((np.abs(y) - END) / 0.09) ** 2)
    d = None
    for k in range(3):
        tw = np.clip((np.abs(y) - (END - 0.2)) / 0.3, 0.0, 1.0)
        a = 2 * np.pi * y / PITCH + 2 * np.pi * k / 3 + np.sign(y) * 2 * np.pi * KNOT_TWIST * tw * tw * (3 - 2 * tw)
        cx, cz = np.cos(a) * LAY_R * s, np.sin(a) * LAY_R * s
        dk = np.hypot(P[:, 0] - cx, P[:, 2] - cz) * COS_ALPHA - STRAND_R * s * COS_ALPHA
        d = dk if d is None else fl._smin(d, dk, 0.006)
    # (Rounded off at the knot's face, the strands' ends turned in.)
    return -fl._smin(-d, KNOT_LEN - np.abs(y), 0.04)


F.ops.append((lay, 0, False))
# The whipping: a band of tarred twine bound tight round the lay every metre or so, the lay swelling either side of it.
for wy in WHIP:
    ring = [Vector((math.cos(a) * 0.086, wy, math.sin(a) * 0.086)) for a in [2 * math.pi * j / 14 for j in range(15)]]
    F.limb(ring, [0.036] * 15, 0.01, WHIPPING, along, sides=8, ref=(0, 1, 0))
    F.limb([Vector((0, wy - 0.04, 0)), Vector((0, wy + 0.04, 0))], [0.11, 0.11], 0.012, WHIPPING, along, sides=18, ref=(0, 0, 1))
# The knots: each end's strands turned back on themselves into a fist (three lobes over a core), the claws' roots
# swelling out of it toward the sill they grip.
for end, sy in (("f", 1), ("b", -1)):
    c = Vector((0, sy * END, 0))
    # A small core under the swollen strands, so the fist is solid where they part.
    F.blob(c + Vector((0, sy * 0.03, 0)), (0.1, 0.07, 0.1), 0.02, FLESH, end_w(end), around=16, rings=8, fmat=under)
    # A strand's end looped back round over the fist (the knot's turn), laid as the rest is.
    loop = [c + Vector((math.cos(t) * 0.17, -sy * (0.0 + 0.07 * math.sin(t * 0.5) ** 2), math.sin(t) * 0.17)) for t in
            [2 * math.pi * j / 12 for j in range(13)]]
    F.limb(loop, [0.042] * 13, 0.02, FLESH, end_w(end), sides=10, ref=(0, 1, 0))
    for k in range(CLAWS):
        cb = sk[f"claw_{end}{k}"]
        F.limb([c.lerp(cb.head, 0.4), cb.head, cb.head.lerp(cb.tail, 0.4)], [0.036, 0.03, 0.026], 0.02, FLESH,
               (lambda p, end=end, k=k: {f"knot_{end}": 0.6, f"claw_{end}{k}": 0.4}), sides=10, ref=(0, 1, 0))

# ----------------------------------------------------------------------------------------------------------------
# The legs: two rows along its underside, a pair every 0.13 m, small and pale, jointed, hooked at the tip, curled under.
legs = kit.part("legs")
LEGS = []
y = END - 0.2
k = 0
while y > -END + 0.2:
    if all(abs(y - wy) > 0.07 for wy in WHIP):
        for sx in (1, -1):
            a = math.radians(-90 + sx * 38)
            base = Vector((math.cos(a) * 0.085, y, math.sin(a) * 0.085))
            out = Vector((math.cos(a), 0, math.sin(a)))
            kn = base + out * 0.045 + Vector((0, -0.012, -0.004))
            an = kn + Vector((sx * 0.006, -0.02, -0.04)) + Vector((0, 0.008 * (h01(k, sx) - 0.5), 0))
            tip = an + Vector((-sx * 0.016, -0.004, -0.018))
            hook = tip + Vector((-sx * 0.012, 0.01, 0.006))
            LEGS.append(tip)
            legs.tube([base - out * 0.01, kn, an, tip, hook], [0.012, 0.0095, 0.0075, 0.005, 0.0015], 4, LEG, along(base), ref=(0, 1, 0), cap0=True)
    y -= 0.13
    k += 1

# The claws' fingers: short and thick, three joints, each knuckled, bent in toward the knot's axis as a fist's are,
# hooked over at the tip into horn; grey, darker to the tip.
claws = kit.part("claws", smooth=False)
TIPS = []
for end, sy in (("f", 1), ("b", -1)):
    for k in range(CLAWS):
        cb = sk[f"claw_{end}{k}"]
        h, t = cb.head, cb.tail
        radial = Vector((h.x, 0, h.z)).normalized()
        ax = Vector((0, sy, 0))
        long_ = 1.0 if k % 2 else 0.8
        j1 = t
        j2 = j1 + (ax * 0.055 + radial * 0.012) * long_
        j3 = j2 + (ax * 0.035 - radial * 0.03) * long_
        tip = j3 + (ax * 0.005 - radial * 0.05) * long_
        TIPS.append(tip)
        claws.tube([h.lerp(t, 0.3), j1, j1.lerp(j2, 0.5) + radial * 0.006, j2, j2.lerp(j3, 0.5) + radial * 0.005, j3],
                   [0.027, 0.025, 0.022, 0.021, 0.019, 0.018], 7, FLESH, f"claw_{end}{k}", ref=(radial.x, radial.y, radial.z), smooth=True)
        for j, r in ((j1, 0.03), (j2, 0.026), (j3, 0.022)):
            claws.blob(j, (r, r, r), 7, 4, FLESH, f"claw_{end}{k}", smooth=True)
        claws.tube([j3, j3.lerp(tip, 0.5) + ax * 0.01 - radial * 0.004, tip], [(0.018, 0.016), (0.011, 0.01), 0.002], 6, CLAW,
                   f"claw_{end}{k}", ref=(radial.x, radial.y, radial.z), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# Clips. At rest it lies straight; CreatureArt lays the segments along the gap. Here each segment's own movement: offsets
# (@loc) across and up for its waves, turns about its own length (@spin) for the lay's twist, swelling (@scale).
SEG = [f"seg_{i:02d}" for i in range(SEGMENTS)]


def wave(t, amp_x=0.0, amp_z=0.0, waves=1.5, speed=1.0, spin=0.0, swell=0.0, swell_waves=3.0, envelope=True):
    """Each segment's pose for a travelling wave at phase t (0..1 over the clip)."""
    p = {}
    for i, n in enumerate(SEG):
        u = i / (SEGMENTS - 1)
        env = math.sin(math.pi * u) if envelope else 1.0
        ph = 2 * math.pi * (u * waves - t * speed)
        p[n + "@loc"] = (amp_x * env * math.sin(ph), 0.0, amp_z * env * math.cos(ph * 0.7 + 1.1))
        if spin:
            p[n + "@spin"] = spin * env * math.sin(ph)
        if swell:
            s = 1 + swell * max(0.0, math.sin(2 * math.pi * (u * swell_waves - t)))
            p[n + "@scale"] = (s, 1.0, s)
    return p


def claws_pose(open_, end="fb", shake=0.0, t=0.0):
    """The claws spread (open_ 1) or clamped shut (0): each finger turned out from or in toward the knot's axis."""
    p = {}
    for e in end:
        for k in range(CLAWS):
            a = 2 * math.pi * k / CLAWS + 0.3
            ang = (-30 + 70 * open_ + shake * 25 * math.sin(2 * math.pi * t * 2 + k * 1.7))
            # Turned about the axis square to its own radial direction: out (open) or in over the sill (clamped).
            ax = Vector((math.cos(a), 0, -math.sin(a)))
            p[f"claw_{e}{k}"] = rig.Quaternion(ax, math.radians(ang) * (1 if e == "f" else -1))
    return p


REST = {n + "@loc": (0.0, 0.0, 0.0) for n in SEG}


class Clip(rig.Clip):
    """rig.Clip, every key carrying every segment's offset (bake keys a bone's location only where a key names it: a key
    without them would take the next key's)."""

    def key(self, frame, pose, interp="BEZIER"):
        return super().key(frame, {**REST, **pose}, interp)


creep = Clip("creep")
for f in range(0, 60, 10):
    t = f / 60
    creep.key(f, {**wave(t, 0.09, 0.07, 1.2, 1.0, spin=10), **claws_pose(0.6 + 0.4 * math.sin(2 * math.pi * t), shake=1, t=t)})
creep.close(60)

clamp = Clip("clamp", loop=False)
clamp.key(0, claws_pose(1.0), "BEZIER")
clamp.key(8, claws_pose(1.15), "LINEAR")
clamp.key(14, claws_pose(-0.05), "CONSTANT")
clamp.key(18, claws_pose(0.0), "BEZIER")

force = Clip("force")
for f in range(0, 30, 6):
    t = f / 30
    force.key(f, {**wave(t, 0.02, 0.02, 2.0, 1.0, spin=18, swell=0.16, swell_waves=2.5), **claws_pose(0.0)})
force.close(30)

taut = Clip("taut")
for f, (sp, tr) in ((0, (0, 0)), (12, (7, 0.006)), (20, (-3, -0.004)), (34, (5, 0.003))):
    q = {n + "@spin": sp * math.sin(math.pi * i / (SEGMENTS - 1)) for i, n in enumerate(SEG)}
    q |= {n + "@loc": (tr * math.sin(i * 1.9), 0, tr * math.cos(i * 2.3)) for i, n in enumerate(SEG)}
    taut.key(f, {**q, **claws_pose(0.0)}, "BEZIER" if f % 2 == 0 else "CONSTANT")
taut.close(45)

slip = Clip("slip", loop=False)
slip.key(0, claws_pose(0.0), "BEZIER")
slip.key(4, {**{n + "@spin": 60 * math.sin(math.pi * i / (SEGMENTS - 1)) for i, n in enumerate(SEG)}, **claws_pose(0.0)}, "LINEAR")
slip.key(10, {**{n + "@spin": 110 * math.sin(math.pi * i / (SEGMENTS - 1)) for i, n in enumerate(SEG)}, **claws_pose(0.0)}, "BEZIER")
slip.key(15, {**{n + "@spin": 90 * math.sin(math.pi * i / (SEGMENTS - 1)) for i, n in enumerate(SEG)}, **claws_pose(0.0)}, "BEZIER")

coil = Clip("coil")
for f in range(0, 30, 5):
    t = f / 30
    coil.key(f, {**wave(t, 0.0, 0.0, 3.0, 1.0, spin=6, swell=0.12, swell_waves=4.0, envelope=False), **claws_pose(0.0)}, "BEZIER")
coil.close(30)

exposed = Clip("exposed")
for f in range(0, 72, 12):
    t = f / 72
    exposed.key(f, {**wave(t, 0.16, 0.08, 1.0, 1.0, spin=16), **claws_pose(0.7 + 0.3 * math.sin(2 * math.pi * t * 2), shake=1.2, t=t)})
exposed.close(72)

hit = Clip("hit", loop=False)
hit.key(0, claws_pose(0.0), "CONSTANT")
hit.key(2, {**wave(0.25, 0.1, 0.12, 0.5, 0.0, spin=30), **claws_pose(0.6)}, "CONSTANT")
hit.key(9, {**wave(0.6, 0.03, 0.03, 0.5, 0.0, spin=8), **claws_pose(0.2)}, "LINEAR")
hit.key(14, claws_pose(0.0), "CONSTANT")

# Death (1.5 s): unlaid: the lay twisting loose from the middle out, the body swelling slack, the claws springing open,
# then limp.
death = Clip("death", loop=False)
death.key(0, claws_pose(0.0), "CONSTANT")
for f, k in ((6, 0.4), (16, 0.8), (30, 1.0), (44, 1.0)):
    q = {}
    for i, n in enumerate(SEG):
        u = i / (SEGMENTS - 1)
        mid = math.sin(math.pi * u)
        q[n + "@spin"] = 220 * k * mid * (1 if i % 2 else 0.85)
        s = 1 + 0.25 * k * mid
        q[n + "@scale"] = (s, 1.0, s * 0.85)
        q[n + "@loc"] = (0.04 * k * math.sin(i * 1.3), 0, -0.05 * k * mid)
    death.key(f, {**q, **claws_pose(1.3 * k if f < 30 else 0.9)}, "LINEAR" if f < 30 else "BEZIER")

CLIPS = [creep, clamp, force, taut, slip, coil, exposed, hit, death]

kit.build()
fl.fuse(kit, "body", ["body"], voxel=0.004, faces=SKIN_FACES, lose=0.03, settle=F.settle, relax=2, symmetric=False)
rig.bake(sk, CLIPS)
print("[dt] knotter", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones), "legs", len(LEGS))
out = rig.args()[0] if rig.args() else "knotter.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
