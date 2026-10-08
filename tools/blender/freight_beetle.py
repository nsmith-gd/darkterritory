"""THE FREIGHT BEETLE (GDD §21, OUTSIDE; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 366;
docs/design/creatures/freight-beetle.md §3 and the director's reference, freight-beetle-reference.webp): "a beetle the
size of a handcart, shovel-headed, its back plated like a crate, shoving a load of freight across the yard."

Bulky and workmanlike, 2.4 m long and 1.4 m to the top of its back. A domed back of broad overlapping plates, each band
lapped over the next like a woodlouse's, worn and scuffed like painted freight (the bake's: buff, ochre, an orange
stripe, grey patches, scorch); a hooded front plate over a grey, wrinkled head, and out of the head the shovel: a flat
wedge of plate, its edge scraped bright; small glossy eyes, feeler mouthparts hanging and working under it; huge pushing
forelimbs, segmented and plated, wider than the head; four thinner legs behind with hooked feet; and red soft flesh
showing between the plates.

How it's made (the Look Review: organic, not boxes; the Gannet's and the Ribbit's way, notes 340, 361): the soft body
under the shell (the abdomen, the thorax, the neck and the head, wrinkled and folded) is one skin of smooth volumes, the
union settled onto their smooth-blended field (tools/blender/flesh.py) before QuadriFlow. Over it, rigid as a beetle's are:
the shell's bands (each a thick curved plate following the dome, its back edge lifted over the next), the hood, the
shovel; and the legs, each segment a swelling, tapering sleeve of plate sunk into the next with the joint's flesh
between, the forelimbs' segments ringed and plated, the feet hooked. Its colour (and the dents, scratches, chips and
wrinkles) is baked into one atlas by tools/models/recipes/freight_beetle.py.

Its own rig (SK_Beetle, 32 bones): root, thorax, abdomen, head, two two-boned palps; six legs of four bones (coxa,
femur, tibia, foot): fore_, mid_, hind_. Its rest is settled on its legs, facing +Y (the engine's -Z). Clips: idle
(settled, feelers working), walk (a tripod gait), brace (head down behind a load, forelimbs set), push (heaving,
shoving forward in steady strides), turn (stepping round on the spot), startle (rearing back off its load), hit, death
(over on its back, legs curling).

    tools/models/build.sh freight_beetle   # this, its colour baked -> content/art/models/freight_beetle.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/freight_beetle.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

LEGS = ("fore", "mid", "hind")
# Each leg's joints (right side; the left mirrored): coxa root, femur root, knee, ankle, toe tip.
JOINTS = {
    "fore": [(0.36, 0.48, 0.62), (0.58, 0.52, 0.64), (0.84, 0.78, 0.52), (0.8, 1.02, 0.1), (0.8, 1.17, 0.015)],
    "mid": [(0.36, 0.0, 0.5), (0.58, 0.02, 0.5), (0.98, 0.1, 0.64), (1.04, 0.04, 0.07), (1.08, 0.14, 0.01)],
    "hind": [(0.34, -0.48, 0.5), (0.56, -0.53, 0.5), (0.94, -0.74, 0.62), (1.0, -0.98, 0.07), (1.03, -1.09, 0.01)],
}
# Where the shovel's edge is (the load it pushes is against it: enemies.json freightBeetle headAt, 1.1 m ahead).
EDGE_Y = 1.12


def beetle_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.3, 0)),
        Bone("thorax", "root", (0, 0.05, 0.66), (0, 0.5, 0.66)),
        Bone("abdomen", "thorax", (0, 0.05, 0.66), (0, -1.0, 0.62)),
        Bone("head", "thorax", (0, 0.55, 0.6), (0, 1.0, 0.55)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [Bone(f"palp_{s}_01", "head", (sx * 0.1, 0.96, 0.36), (sx * 0.13, 1.04, 0.25)),
              Bone(f"palp_{s}_02", f"palp_{s}_01", (sx * 0.13, 1.04, 0.25), (sx * 0.11, 1.09, 0.13))]
        for leg in LEGS:
            j = [Vector((sx * x, y, z)) for x, y, z in JOINTS[leg]]
            parent = "thorax" if leg in ("fore", "mid") else "abdomen"
            b += [Bone(f"{leg}_{s}_01", parent, tuple(j[0]), tuple(j[1])),
                  Bone(f"{leg}_{s}_02", f"{leg}_{s}_01", tuple(j[1]), tuple(j[2])),
                  Bone(f"{leg}_{s}_03", f"{leg}_{s}_02", tuple(j[2]), tuple(j[3])),
                  Bone(f"{leg}_{s}_04", f"{leg}_{s}_03", tuple(j[3]), tuple(j[4]))]
    return Skeleton("SK_Beetle", b)


sk = beetle_skeleton()
sk.build()
kit = rig.Kit(sk, "freight_beetle")
SKIN_FACES = 2300


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked, the names say what to paint.
SHELL = Mat("wood_crate.beetle_shell", hexc("#b8a27a"), shine=0.2)
HOOD = Mat("wood_crate.beetle_hood", hexc("#8f8678"), shine=0.2)
BLADE = Mat("iron_plate.beetle_blade", hexc("#6f6a62"), shine=0.4)
HIDE = Mat("skin.beetle_hide", hexc("#7b746a"), shine=0.25)
FLESH = Mat("flesh.beetle_flesh", hexc("#8a3a32"), shine=0.5)
LEG = Mat("skin.beetle_leg", hexc("#8a8274"), shine=0.25)
CLAW = Mat("iron_plate.beetle_claw", hexc("#2a2622"), shine=0.4)
EYE = Mat("glass_dirty.beetle_eye", hexc("#1a241c"), shine=0.95)

# ----------------------------------------------------------------------------------------------------------------
# The soft body under the shell: one skin (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "beetle flesh")
TRUNK = chain([Vector((0, -1.05, 0.62)), H("abdomen"), Vector((0, 0.5, 0.66)), T("head")], ["abdomen", "thorax", "head"], soft=0.2)


def trunk_w(p):
    return TRUNK(p)


def hide_or_flesh(pts, n):
    """Grey wrinkled hide on the head, the neck's top and the underside; the red soft flesh along the flanks, where it
    shows between the plates and under their edges."""
    c = sum(pts, Vector()) / len(pts)
    if c.y > 0.52 or n.z < -0.55:
        return HIDE
    return FLESH if abs(n.x) > 0.35 or n.z > 0.3 else HIDE


# The abdomen: a long round barrel under the dome; the thorax, broad and deep for the forelimbs; the neck folded.
flesh.blob((0, -0.42, 0.66), (0.6, 0.66, 0.4), 0, FLESH, trunk_w, fmat=hide_or_flesh, around=24, rings=14)
flesh.blob((0, -0.95, 0.66), (0.42, 0.24, 0.3), 0.12, FLESH, trunk_w, fmat=hide_or_flesh)
flesh.blob((0, 0.22, 0.64), (0.55, 0.38, 0.38), 0.14, FLESH, trunk_w, fmat=hide_or_flesh, around=24, rings=12)
# The belly's segments, slung under it (the bake's folds between them).
for k in range(5):
    y = -0.9 + k * 0.25
    flesh.blob((0, y, 0.38 + 0.02 * k), (0.42 - 0.02 * abs(k - 2), 0.14, 0.12), 0.06, HIDE, trunk_w)
# The coxae: each leg's root, a thick soft knuckle grown out of the flank.
for leg in LEGS:
    for s, sx in (("r", 1), ("l", -1)):
        a = Vector((sx * JOINTS[leg][0][0], JOINTS[leg][0][1], JOINTS[leg][0][2]))
        b = Vector((sx * JOINTS[leg][1][0], JOINTS[leg][1][1], JOINTS[leg][1][2]))
        r0 = 0.17 if leg == "fore" else 0.11
        flesh.limb([a - (b - a) * 0.3, a.lerp(b, 0.5)], [r0, r0 * 0.85], 0.08, FLESH,
                   lambda p, leg=leg, s=s: mix(trunk_w(p), {f"{leg}_{s}_01": 1.0}, smooth01(0.3, 0.5, abs(p[0]))), fmat=hide_or_flesh)
# The neck: wrinkled rolls, out from under the hood to the head.
for k, y in enumerate((0.52, 0.6, 0.68)):
    flesh.blob((0, y, 0.62 - 0.01 * k), (0.32 - 0.02 * k, 0.07, 0.24 - 0.015 * k), 0.05, HIDE,
               lambda p: mix(trunk_w(p), {"head": 1.0}, smooth01(0.5, 0.66, p[1])))
# The head: a heavy, rounded, wrinkled capsule, the cheeks puffed either side where the eyes sit, the mouth under it with
# its lips and the palps' roots.
HEAD_W = lambda p: mix(trunk_w(p), {"head": 1.0}, smooth01(0.6, 0.7, p[1]))  # noqa: E731
flesh.blob((0, 0.86, 0.5), (0.27, 0.2, 0.16), 0.06, HIDE, HEAD_W, around=22, rings=12)
for sx in (1, -1):
    flesh.blob((sx * 0.19, 0.88, 0.49), (0.11, 0.13, 0.11), 0.05, HIDE, HEAD_W)                         # the cheeks
    flesh.blob((sx * 0.08, 0.98, 0.36), (0.07, 0.07, 0.055), 0.04, HIDE, HEAD_W)                        # the lips
flesh.blob((0, 0.99, 0.38), (0.13, 0.08, 0.06), 0.05, HIDE, HEAD_W)                                     # the chin
flesh.carve((0, 1.05, 0.34), (0.06, 0.04, 0.025), 0.02)                                                # the mouth

# ----------------------------------------------------------------------------------------------------------------
# The shell: broad bands over the dome, each a thick curved plate lapped over the next; the hood over the head.
shell = kit.part("shell")
DOME_C, DOME_R = Vector((0, -0.28, 0.56)), Vector((0.74, 0.98, 0.72))


def dome(phi, y, lift=0.0, c=DOME_C, r=DOME_R):
    """A point on the dome's surface at angle `phi` round from its top (radians, + to the right) and length `y`,
    `lift` metres out off it."""
    t = max(-0.995, min(0.995, (y - c.y) / r.y))
    k = math.sqrt(1 - t * t)
    rx, rz = r.x * k, r.z * k
    p = Vector((math.sin(phi) * rx, y, c.z + math.cos(phi) * rz))
    n = Vector((math.sin(phi) / max(rx, 1e-3), 0, math.cos(phi) / max(rz, 1e-3))).normalized()
    return p + n * lift


def plate(part, y0, y1, phi, thick, mat, bones, lift0, lift1, seed, c=DOME_C, r=DOME_R, steps=26, along=5, droop=0.0, bulge=0.0, keel=0.0):
    """A band of the shell: from length y0 (its front, under the band before it) to y1 (its back edge, lifted
    `lift1` off the dome over the band behind), round to ±`phi`, `thick` through. Lofted round the dome (its rings are
    the band's cross-sections, `along` points down its front and back), its edges dented a little (a fixed hash)."""
    rings = []
    for i in range(steps + 1):
        a = -phi + 2 * phi * i / steps
        ring_out, ring_in = [], []
        for j in range(along):
            t = j / (along - 1)
            y = y0 + (y1 - y0) * t
            dent = 0.008 * math.sin(i * 0.9 + seed * 1.3) * math.sin(i * 0.37 + seed) * (j in (0, along - 1))
            # (Each band swells in its middle, a woodlouse's segment, and rides up into a low keel along the top.)
            lift = lift0 + (lift1 - lift0) * t * t + dent + droop * (abs(a) / phi) ** 3 + bulge * math.sin(math.pi * t) + keel * math.exp(-(a / 0.25) ** 2)
            o = dome(a, y, lift + thick, c, r)
            ins = dome(a, y, lift, c, r)
            if j == along - 1:
                # The back edge rolled down a little over the band behind it: a lip.
                o = o - Vector((0, 0.012, 0)) + (o - ins).normalized() * -0.01
            ring_out.append(o)
            ring_in.append(ins)
        rings.append(ring_out + list(reversed(ring_in)))
    part.loft(rings, mat, bones, cap0="point", cap1="point")


def shell_w(y):
    return lambda p: mix({"abdomen": 1.0}, {"thorax": 1.0}, smooth01(-0.2, 0.15, p[1]))


# Five bands from the tail to the shoulders, each lapped over the one behind it (a woodlouse's way); the hood over them
# at the front.
BANDS = [(-1.2, -0.9), (-0.94, -0.56), (-0.6, -0.16), (-0.2, 0.14), (0.1, 0.4)]
for i, (y0, y1) in enumerate(BANDS):
    plate(shell, y0, y1, math.radians(97 - 3 * abs(i - 2)), 0.04, SHELL, shell_w(y0), 0.04, 0.1, 401 + i,
          droop=0.04, bulge=0.035, keel=0.02, along=7)
# The hood: a helmet over the head and the neck, its front edge curled down and out over the face (the shovel under it).
HOOD_C, HOOD_R = Vector((0, 0.25, 0.52)), Vector((0.68, 0.6, 0.82))
plate(shell, 0.34, 0.72, math.radians(98), 0.05, HOOD, lambda p: mix({"thorax": 1.0}, {"head": 1.0}, smooth01(0.6, 0.8, p[1]) * 0.5),
      0.0, 0.06, 409, c=HOOD_C, r=HOOD_R, steps=28, along=7, droop=0.03)
# The shovel: a flat wedge of plate out of the head's front, broad, thick at its root and sharp at its edge, sloped down
# forward to it; its edge's corners worn round.
blade = kit.part("blade")
BLADE_PTS = []
SECTION = ((0.0, 0.006), (0.3, 0.04), (0.7, 0.07), (1.0, 0.08))
rings = []
for i in range(8):
    x = (i / 7 - 0.5) * 2
    # The edge bows forward in the middle; the root runs back up under the hood.
    edge = Vector((x * 0.36, EDGE_Y - 0.08 * x * x, 0.52 + 0.07 * x * x))
    root = Vector((x * 0.24, 0.74, 0.88 - 0.03 * x * x))
    BLADE_PTS.append(edge)
    rings.append([edge.lerp(root, k) + Vector((0, 0, d * 0.5)) for k, d in SECTION]
                 + [edge.lerp(root, k) - Vector((0, 0, d * 0.5)) for k, d in reversed(SECTION)])
blade.loft(rings, BLADE, "head", cap0="point", cap1="point")
# ----------------------------------------------------------------------------------------------------------------
# The legs: each segment a swelling sleeve of plate, sunk into the next (the joint's flesh between); the forelimbs huge
# and ringed, wider than the head; the rest thinner, their feet hooked.
legs = kit.part("legs")
EYES, FEET = [], []
for s, sx in (("r", 1), ("l", -1)):
    for leg in LEGS:
        j = [Vector((sx * x, y, z)) for x, y, z in JOINTS[leg]]
        big = leg == "fore"
        radii = [(0.19, 0.17), (0.16, 0.11), (0.1, 0.07)] if big else [(0.085, 0.075), (0.07, 0.05), (0.045, 0.03)]
        for seg in range(3):
            a, b = j[seg + 1], j[seg + 2]
            bone = f"{leg}_{s}_{seg + 2:02d}"
            r0, r1 = radii[seg]
            if seg == 2:
                # (The last sleeve ends on the foot, its rim clear of the ground the foot stands on.)
                b = b + Vector((0, 0, r1))
            d = (b - a).normalized()
            n = 7 if big else 5
            pts, rr = [], []
            for k in range(n + 1):
                t = k / n
                pts.append(a - d * (0.03 if seg else 0.0) + (b - a + d * 0.03) * t)
                # A sleeve: swollen past its root, tapering to its end; the big ones ringed in plates (each ring's lip).
                r = r0 + (r1 - r0) * t
                r *= 0.82 + 0.25 * math.sin(min(1.0, t * 1.4) * math.pi * 0.85)
                if big and 0 < k < n:
                    r *= 1.0 + 0.07 * (k % 2)
                rr.append((r, r * (0.9 if big else 1.0)))
            legs.tube(pts, rr, 14 if big else 10, LEG, bone, ref=(0, 0, 1), cap0="point", cap1="point")
            if big:
                # The forelimbs' outer plates: a curved shield down the outside of each segment.
                out = Vector((sx, 0.15, 0.3)).normalized()
                for k in range(2):
                    c0, c1 = a.lerp(b, 0.1 + 0.42 * k), a.lerp(b, 0.48 + 0.42 * k)
                    w = (r0 + (r1 - r0) * (0.3 + 0.4 * k)) * 1.05
                    legs.tube([c0 + out * w * 0.85, c1 + out * w * 0.85], [(w * 0.9, 0.025), (w * 0.8, 0.022)], 8, LEG, bone,
                              ref=[(out.x, out.y, out.z)] * 2, cap0=True, cap1=True)
        # The foot: hooked claws off the last joint, splayed; the forelimbs' broad as a shovel's teeth.
        a, b = j[3], j[4]
        foot = f"{leg}_{s}_04"
        legs.tube([a, a.lerp(b, 0.5), b], [(0.075, 0.06) if big else (0.032, 0.028), (0.06, 0.045) if big else (0.026, 0.022), 0.02 if big else 0.012],
                  10 if big else 8, LEG, foot, ref=(0, 0, 1), cap0="point", cap1="point")
        for c in range(3 if big else 2):
            spread = (c - (1 if big else 0.5)) * (0.07 if big else 0.04)
            side = Vector((spread, 0, 0)) if leg == "fore" else Vector((0, spread, 0))
            root = b + side * 0.6 + Vector((0, 0, 0.015))
            fwd = (b - a).normalized()
            fwd = Vector((fwd.x, fwd.y, 0)).normalized()
            tip = root + fwd * (0.09 if big else 0.06) + side * 0.4 + Vector((0, 0, -0.012))
            # (Their points on the ground, not through it.)
            tip.z = max(tip.z, 0.014)
            FEET.append(tip)
            legs.tube([root, root.lerp(tip, 0.5) + Vector((0, 0, 0.02 if big else 0.012)), tip, tip + Vector((0, 0, -0.01)) - fwd * 0.012],
                      [0.024 if big else 0.012, 0.018 if big else 0.009, 0.01 if big else 0.005, 0.002], 6, CLAW, foot, ref=(0, 0, 1), cap1="point")

# The eyes, glossy and dark, set in the cheeks; the palps, segmented feelers hanging from the lips and working.
fine = kit.part("fine")
for sx in (1, -1):
    e = Vector((sx * 0.265, 0.92, 0.5))
    EYES.append(e)
    fine.blob(e, (0.055, 0.06, 0.05), 14, 8, EYE, "head")
    s = "r" if sx > 0 else "l"
    a, b, c = H(f"palp_{s}_01"), H(f"palp_{s}_02"), T(f"palp_{s}_02")
    pts = [a - (b - a) * 0.2, a.lerp(b, 0.5), b, b.lerp(c, 0.5), c, c + Vector((-sx * 0.02, 0.02, -0.02))]
    fine.tube(pts, [0.03, 0.026, 0.022, 0.02, 0.016, 0.008], 8, LEG, chain([a, b, c], [f"palp_{s}_01", f"palp_{s}_02"], soft=0.02),
              ref=(0, 1, 0), cap0="point", cap1="point")
    # A pair of shorter mandible-feelers inside them.
    m0 = Vector((sx * 0.05, 1.0, 0.35))
    fine.tube([m0, m0 + Vector((sx * 0.02, 0.06, -0.06)), m0 + Vector((sx * 0.0, 0.09, -0.14))], [0.018, 0.014, 0.006], 6, LEG,
              f"palp_{s}_01", ref=(0, 1, 0), cap0="point", cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Its rest is settled on its legs; angles in the armature's axes: a leg out to the side swings forward with +Z on
# the right (−Z on the left: `leg`), lifts with −Y on the right (+Y on the left); the thorax tips its front up with +X.

def leg(side, name, swing=0.0, lift=0.0, knee=0.0, foot=0.0):
    """One leg's pose: swung forward (degrees) at the coxa, lifted at the femur, the knee bent (+: the tibia tucked in
    under), the foot curled; mirrored for the left."""
    sg = 1 if side == "r" else -1
    return {f"{name}_{side}_01": (0, 0, sg * swing), f"{name}_{side}_02": (0, -sg * lift, 0),
            f"{name}_{side}_03": (0, sg * knee, 0), f"{name}_{side}_04": (0, sg * foot, 0)}


def pose(*parts):
    out = {}
    for p in parts:
        out.update(p)
    return out


def palps(k):
    """The feelers working: k in -1..1."""
    return {"palp_r_01": (10 * k, 0, 8 * k), "palp_l_01": (-10 * k, 0, 8 * k), "palp_r_02": (-14 * k, 0, 0), "palp_l_02": (14 * k, 0, 0)}


SETTLED = pose({"root@loc": (0, 0, -0.04)}, *[leg(s, n, lift=-4, knee=4) for s in "rl" for n in LEGS])

# Idle (3 s, loop): settled, the feelers working in little jerks, the head turning a touch, the abdomen breathing.
idle = Clip("idle")
for f, k, look, br in ((0, 0, 0, 0), (12, 1, 0, 1), (18, -1, 0, 1), (30, 0.5, 6, 0), (42, -0.6, 6, 1), (50, 1, -4, 0), (66, -1, -4, 1),
                       (78, 0.3, 0, 0)):
    idle.key(f, pose(SETTLED, palps(k), {"head": (-2, 0, look), "abdomen": (-1.5 * br, 0, 0), "thorax": (0.6 * br, 0, 0)}),
             "CONSTANT" if f in (18, 50) else "BEZIER")
idle.close(90)


def tripod(phase, stride=24.0, lift=18.0, low=0.0, push=False):
    """A tripod gait's pose at `phase` (0..1): fore-right, mid-left, hind-right swing together, the other three stand."""
    out = {}
    for s in "rl":
        for n in LEGS:
            group = (n == "mid") != (s == "r")
            p = (phase + (0.5 if group else 0.0)) % 1.0
            # Half the cycle on the ground going back, half in the air coming forward.
            if p < 0.5:
                sw, lf = stride * (0.5 - 2 * p), 0.0
            else:
                q = (p - 0.5) * 2
                sw, lf = stride * (-0.5 + q), lift * math.sin(q * math.pi)
            if n == "fore" and push:
                sw, lf = sw * 0.6, lf * 0.7
            out.update(leg(s, n, swing=sw, lift=lf - 4 - low, knee=4 + lf * 0.6 + low))
    return out


# Walk (1.2 s, loop): a tripod gait, the body swaying a little with it.
walk = Clip("walk")
for f in range(0, 36, 6):
    ph = f / 36
    walk.key(f, pose(tripod(ph), palps(math.sin(ph * 6.28)), {"thorax": (0, 2 * math.sin(ph * 12.57), 3 * math.sin(ph * 6.28)),
                                                            "head": (0, 0, -3 * math.sin(ph * 6.28)), "root@loc": (0, 0, 0.02 * abs(math.sin(ph * 6.28)))}),
             "LINEAR")
walk.close(36)

# Brace (1 s, loop): down behind the load, the head and the shovel lowered to it, the forelimbs set wide and forward,
# the hind legs dug in; a shiver through it.
BRACED = pose({"root@loc": (0, 0, -0.1), "thorax": (-7, 0, 0), "head": (-10, 0, 0), "abdomen": (5, 0, 0)},
              leg("r", "fore", swing=12, lift=-8, knee=-6), leg("l", "fore", swing=12, lift=-8, knee=-6),
              leg("r", "mid", swing=-6, lift=-10, knee=10), leg("l", "mid", swing=-6, lift=-10, knee=10),
              leg("r", "hind", swing=-14, lift=-12, knee=14), leg("l", "hind", swing=-14, lift=-12, knee=14), palps(-0.6))
brace = Clip("brace")
for f, k in ((0, 0), (8, 1), (10, -1), (14, 1), (22, 0)):
    brace.key(f, pose(BRACED, {"thorax": (-7 - 0.6 * k, 0, 0.5 * k), "root@loc": (0, -0.008 * k, -0.1)}), "CONSTANT" if f in (8, 10, 14) else "BEZIER")
brace.close(30)

# Push (1.4 s, loop): heaving behind the load, the shovel in it, every leg driving it on in a slow heavy tripod, the body
# low and lunging forward each stride.
push = Clip("push")
for f in range(0, 42, 7):
    ph = f / 42
    heave = math.sin(ph * 12.57)
    push.key(f, pose(BRACED, tripod(ph, stride=30, lift=12, low=6, push=True), palps(-0.8),
                     {"thorax": (-7 + 2 * heave, 0, 0), "head": (-10 - 2 * heave, 0, 0), "root@loc": (0, 0.03 * heave, -0.1)}), "BEZIER")
push.close(42)

# Turn (1 s, loop): stepping round on the spot, the legs of each side going opposite ways.
turn = Clip("turn")
for f in range(0, 30, 5):
    ph = f / 30
    out = {}
    for s in "rl":
        for n in LEGS:
            group = (n == "mid") != (s == "r")
            p = (ph + (0.5 if group else 0.0)) % 1.0
            sw = (12 * (0.5 - 2 * p) if p < 0.5 else 12 * (-0.5 + (p - 0.5) * 2)) * (1 if s == "r" else -1)
            lf = 0 if p < 0.5 else 16 * math.sin((p - 0.5) * 2 * math.pi)
            out.update(leg(s, n, swing=sw, lift=lf - 4, knee=4 + lf * 0.6))
    turn.key(f, pose(out, palps(0.5 * math.sin(ph * 6.28)), {"thorax": (0, 0, 4 * math.sin(ph * 6.28))}), "LINEAR")
turn.close(30)

# Startle (0.8 s): reared back off its load, the front up and the forelimbs lifted and spread, the feelers flung out;
# then back down.
startle = Clip("startle", loop=False)
startle.key(0, SETTLED, "LINEAR")
startle.key(5, pose(SETTLED, {"root@loc": (0, -0.18, 0.02), "thorax": (22, 0, 0), "head": (14, 0, 0), "abdomen": (-14, 0, 0)},
                    leg("r", "fore", swing=24, lift=44, knee=30), leg("l", "fore", swing=24, lift=44, knee=30),
                    leg("r", "mid", swing=10, lift=8), leg("l", "mid", swing=10, lift=8), palps(1.4)), "LINEAR")
startle.key(12, pose(SETTLED, {"root@loc": (0, -0.22, 0.0), "thorax": (16, 0, 0), "head": (8, 0, 0), "abdomen": (-10, 0, 0)},
                     leg("r", "fore", swing=18, lift=30, knee=24), leg("l", "fore", swing=18, lift=30, knee=24), palps(-1)), "BEZIER")
startle.key(24, pose(SETTLED, {"root@loc": (0, -0.2, -0.04)}), "BEZIER")

hit = Clip("hit", loop=False)
hit.key(0, SETTLED, "CONSTANT")
hit.key(2, pose(SETTLED, {"thorax": (6, 0, 8), "head": (8, 0, 10), "root@loc": (0.04, -0.06, 0.0)}, palps(1.2)), "CONSTANT")
hit.key(8, pose(SETTLED, {"thorax": (-2, 0, -3)}), "LINEAR")
hit.key(12, SETTLED, "CONSTANT")

# Death (1.6 s): it heaves up on one side and goes over onto its back, the legs curling in over its belly and twitching.
CURLED = pose(*[leg(s, n, swing=8 if n == "hind" else -8, lift=50, knee=70, foot=40) for s in "rl" for n in LEGS])
death = Clip("death", loop=False)
death.key(0, SETTLED, "CONSTANT")
death.key(6, pose(SETTLED, {"root@loc": (0.0, 0, 0.35), "root": (0, 50, 0)}, leg("r", "mid", lift=30), leg("r", "hind", lift=30)), "LINEAR")
death.key(14, pose(CURLED, {"root@loc": (0.0, 0, 1.33), "root": (0, 175, 0)}), "LINEAR")
death.key(18, pose(CURLED, {"root@loc": (0.0, 0, 1.37), "root": (0, 180, 0)}, leg("r", "mid", lift=40, knee=40)), "BEZIER")
death.key(24, pose(CURLED, {"root@loc": (0.0, 0, 1.36), "root": (0, 180, 0)}, leg("l", "hind", lift=60, knee=80)), "CONSTANT")
death.key(48, pose(CURLED, {"root@loc": (0.0, 0, 1.36), "root": (0, 180, 0)}), "BEZIER")

# One soft skin under the shell (rig.fuse, settled); the plates, the shovel and the legs are rigid parts over it. Toward
# a large beast's budget (CreatureArtTests: 6-12k).
kit.fuse("body", ["body"], voxel=0.008, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, [idle, walk, brace, push, turn, startle, hit, death])
print("[dt] freight_beetle", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "freight_beetle.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
