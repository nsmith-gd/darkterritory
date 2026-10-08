"""HOTBOX (GDD §21 structural, App. A.4 · vibration; docs/design/creatures/hotbox.md §3; ARCHITECTURE §8 note 367): an
axle parasite that feeds on the train's heat.

A horseshoe crab's carapace (a low dome of overlapping armour plates, scorched purple-brown and black, ridged and pitted
like a casting), a centipede's many legs down both sides ending in hooked black claws, and between and under the plates a
heat-swollen abdomen: segmented, glossy, lit orange-red from inside, the brightest thing on it. Greasy machinery in its
make: plates like brake shoes, joints like rivets. A short blunt head under the front plate with mandibles. About 1.1 m
long and 0.4 m high: folded into a truck, its plates read as part of the bogie until it glows.

How it's made (the Look Review: organic, not boxes; the Gannet's and the Ribbit's way, notes 340, 362): the soft body is one
skin (tools/blender/flesh.py): the abdomen's swollen segments, pinched between, the head and its mouthparts' roots, and
each leg's root, smooth-blended into one surface and QuadriFlowed. Over it, hard: the carapace's plates (each a thick
domed shell, its rim knobbed, its keel ridged, the front shield a horseshoe with two pits for eyes), the legs (three joints
each, black, a hooked claw at the end), the mandibles. The abdomen's glow is an emission map (tools/models/recipes/hotbox.py
bakes it), scaled by the engine by what it's doing. Its colour is baked into one atlas by that recipe; built alone (this
script), it wears the shared tiling textures.

SK_Hotbox: root, body, the abdomen's three segments (they swell), the head and its mandibles, the front shield and four
plates behind it (they lift), and eight pairs of legs of two bones each. At rest it stands on the floor facing +Y (the
engine's -Z), its pivot under its middle. In the clips that have it in its truck, it's rolled onto its right side, its
back out (+X: CreatureArt turns it for the car's left side), centred on the origin: the axle box the sim has it at. A
beast's budget (GDD §27: 2,000-8,000 triangles). Clips (§31: still, then abrupt): clamped (folded into the truck, plates
shut, only the abdomen's pulse), knock (a leg hammering the axle box, once a beat), glow (the abdomen swelling bright, the
plates lifting to vent), unfold (half out of the truck: down onto the rail and the ballast), out (there, legs splayed,
mandibles working), snap (a lunge with the mandibles), prised (levered out, flailing over onto the ballast), scuttle (off
into the dark), hit, death (over on its back, the legs curling).

    tools/models/build.sh hotbox        # this, with its colour baked -> content/art/models/hotbox.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/hotbox.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
import flesh as fl  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()

# The brief's numbers (hotbox.md §3), printed at the end and pinned by CreatureArtTests.
LENGTH, HEIGHT = 1.1, 0.4
LEGS = 8
# The legs' roots along each side (y), front to back.
LEG_Y = [0.36 - 0.1 * k for k in range(LEGS)]


def skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("body", "root", (0, -0.1, 0.2), (0, 0.15, 0.2)),
        Bone("abd_1", "body", (0, 0.12, 0.17), (0, -0.05, 0.17)),
        Bone("abd_2", "abd_1", (0, -0.05, 0.17), (0, -0.25, 0.16)),
        Bone("abd_3", "abd_2", (0, -0.25, 0.16), (0, -0.45, 0.15)),
        Bone("head", "body", (0, 0.36, 0.15), (0, 0.5, 0.13)),
        Bone("shield", "body", (0, 0.05, 0.3), (0, 0.5, 0.3)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        b.append(Bone(f"mandible_{side}", "head", (sx * 0.05, 0.47, 0.1), (sx * 0.04, 0.6, 0.07)))
    for k in range(4):
        y0 = 0.04 - 0.14 * k
        b.append(Bone(f"plate_{k + 1}", "body" if k == 0 else f"plate_{k}", (0, y0, 0.3 - 0.03 * k), (0, y0 - 0.14, 0.28 - 0.03 * k)))
    for side, sx in (("r", 1), ("l", -1)):
        for k, y in enumerate(LEG_Y):
            hip = Vector((sx * 0.26, y, 0.09))
            knee = Vector((sx * 0.45, y + 0.015, 0.16))
            foot = Vector((sx * 0.58, y + 0.03, 0.01))
            b.append(Bone(f"leg_{side}{k + 1}_a", "body", tuple(hip), tuple(knee)))
            b.append(Bone(f"leg_{side}{k + 1}_b", f"leg_{side}{k + 1}_a", tuple(knee), tuple(foot)))
    return Skeleton("SK_Hotbox", b)


sk = skeleton()
sk.build()
kit = rig.Kit(sk, "hotbox")
SKIN_FACES = 1500

# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/hotbox.py),
# the names say what to paint: the scorched plates, the glowing belly, the black legs and claws, the head's chitin.
PLATE = Mat("iron_plate.hotbox_plate", hexc("#2a1c22"), shine=0.35)
BELLY = Mat("sac.hotbox_belly", hexc("#c2481c"), shine=0.85, emissive=0.0)
CHITIN = Mat("tar.hotbox_chitin", hexc("#1a1416"), shine=0.5)
LEG = Mat("tar.hotbox_leg", hexc("#120e0e"), shine=0.55)
CLAW = Mat("tar.hotbox_claw", hexc("#080606"), shine=0.7)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def h01(*k):
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 367, 1.0)


def abd_w(p):
    """The abdomen's three segments along it, blended where they meet; the front of it with the body."""
    y = Vector(p).y
    stops = [(0.2, "body"), (0.08, "abd_1"), (-0.12, "abd_2"), (-0.34, "abd_3")]
    if y >= stops[0][0]:
        return {"body": 1.0}
    if y <= stops[-1][0]:
        return {"abd_3": 1.0}
    for (y0, b0), (y1, b1) in zip(stops, stops[1:]):
        if y1 <= y <= y0:
            t = (y0 - y) / (y0 - y1)
            return {n: v for n, v in ((b0, 1 - t), (b1, t)) if v > 0.02}
    return {"body": 1.0}


# ----------------------------------------------------------------------------------------------------------------
# The soft body: the swollen abdomen in its segments, the head, the legs' roots (flesh.fuse, settled on `F`).
body = kit.part("body")
F = fl.Flesh(body, "hotbox flesh")
# (Low and wide, bulging out under the carapace's rim at the sides, where it's seen glowing.)
SEGS = [(0.26, 0.22, 0.09, 0.1), (0.12, 0.33, 0.11, 0.12), (-0.04, 0.36, 0.115, 0.12), (-0.2, 0.34, 0.11, 0.12), (-0.35, 0.28, 0.1, 0.1),
        (-0.48, 0.18, 0.08, 0.08)]
for y, rx, rz, ry in SEGS:
    F.blob((0, y, 0.135), (rx, ry * 1.1, rz), 0.035, BELLY, abd_w, around=22, rings=10)
# Pinched between its segments, a ridge of chitin round each pinch.
for (y0, *_), (y1, *_) in zip(SEGS, SEGS[1:]):
    ym = (y0 + y1) / 2
    F.carve((0, ym, 0.135), (0.45, 0.016, 0.45), 0.02)
# The head: short and blunt, under the shield's front, the mouthparts' roots either side.
F.blob((0, 0.38, 0.1), (0.16, 0.1, 0.07), 0.04, CHITIN, "head", around=16, rings=8)
F.blob((0, 0.46, 0.085), (0.11, 0.06, 0.05), 0.03, CHITIN, "head")
for sx in (1, -1):
    F.blob((sx * 0.06, 0.48, 0.07), (0.035, 0.035, 0.03), 0.02, CHITIN, "head", around=10, rings=6)
# The legs' roots: a knob of chitin for each leg along the body's sides.
for side, sx in (("r", 1), ("l", -1)):
    for k, y in enumerate(LEG_Y):
        hip = sk[f"leg_{side}{k + 1}_a"].head
        F.limb([Vector((sx * 0.16, y, 0.1)), hip], [0.04, 0.032], 0.02, CHITIN, {f"leg_{side}{k + 1}_a": 0.4, "body": 0.6}, sides=10, ref=(0, 0, 1))

# ----------------------------------------------------------------------------------------------------------------
# The carapace: thick domed plates, overlapping front to back, scorched and pitted (the bake's); each a shell with a
# knobbed rim and a keel down its middle.
plates = kit.part("plates")


def shell(part, y0, y1, half, top, rim, thick, mat, bone, nu=9, nv=13, horseshoe=False, keel=0.012):
    """A domed plate from y0 (front) to y1 (back), `half` wide at its widest, its crown `top` high and its edge `rim` high:
    its outer face smooth, its underside and its edge hard."""
    def width(u):
        if horseshoe:
            # Round at the front (a horseshoe), squared at the back where the next plate tucks under.
            return half * math.sqrt(max(0.0, 1 - ((1 - u) ** 2.2)))
        return half * (1 - 0.18 * u)

    def at(u, v, inner=False):
        y = y0 + (y1 - y0) * u
        w = max(width(u), 0.02)
        x = v * w
        crown = (1 - abs(v) ** 1.7) ** 1.15
        z = rim + (top - rim) * crown * (0.35 + 0.65 * smooth01(0.0, 0.45, u) if horseshoe else 1.0)
        # The rim's lip, turned up a little at the very edge.
        z += 0.012 * smooth01(0.82, 1.0, abs(v))
        z += keel * bell(v / 0.12)
        # Lumped like a rough casting (bosses, sunk places), its rim knobbed and ragged with flash; each plate's back edge
        # lifted a little proud of the next, shingled.
        z += 0.022 * noise3(Vector((x * 5, y * 5, 0.3)), 3669, 1.0) * crown
        z += 0.01 * noise3(Vector((x * 14, y * 14, 0.7)), 3670, 1.0)
        if not horseshoe:
            z += 0.02 * u * u
        if inner:
            z -= thick
        return Vector((x, y, z))

    uvs_at = lambda p: (p.x * 2, p.y * 2)  # noqa: E731
    outer = [[part.add_v(at(i / (nu - 1), -1 + 2 * j / (nv - 1)), bone) for j in range(nv)] for i in range(nu)]
    inner = [[part.add_v(at(i / (nu - 1), -1 + 2 * j / (nv - 1), True), bone) for j in range(nv)] for i in range(nu)]
    for i in range(nu - 1):
        for j in range(nv - 1):
            q = [outer[i][j], outer[i][j + 1], outer[i + 1][j + 1], outer[i + 1][j]]
            part.face(q, [uvs_at(part.v[k]) for k in q], mat, True, outward=part.v[q[0]] - Vector((0, 0, 1)))
            q = [inner[i][j], inner[i][j + 1], inner[i + 1][j + 1], inner[i + 1][j]]
            part.face(q, [uvs_at(part.v[k]) for k in q], mat, True, outward=part.v[q[0]] + Vector((0, 0, 1)))
    # The edge all round, hard.
    loop = [(0, j) for j in range(nv)] + [(i, nv - 1) for i in range(1, nu)] + [(nu - 1, j) for j in range(nv - 2, -1, -1)] + \
           [(i, 0) for i in range(nu - 2, 0, -1)]
    c = at(0.5, 0.0) - Vector((0, 0, thick * 2))
    for a, b in zip(loop, loop[1:] + loop[:1]):
        q = [outer[a[0]][a[1]], outer[b[0]][b[1]], inner[b[0]][b[1]], inner[a[0]][a[1]]]
        part.face(q, [uvs_at(part.v[k]) for k in q], mat, False, outward=c)


# The front shield: a horseshoe over the head and the front of the body, its edge down over the head; two eye pits in it
# (the bake's).
shell(plates, 0.6, 0.0, 0.46, 0.33, 0.1, 0.028, PLATE, "shield", nu=11, nv=15, horseshoe=True, keel=0.02)
# Four plates behind it, each tucked under the one before, narrowing to the tail.
for k in range(4):
    y0 = 0.08 - 0.14 * k
    shell(plates, y0, y0 - 0.2, 0.44 - 0.05 * k, 0.33 - 0.025 * k, 0.11 - 0.008 * k, 0.024, PLATE, f"plate_{k + 1}", nu=5, nv=13, keel=0.016)
# Knobs along the keels and the shield's ridges (its casting's sprues).
for k in range(7):
    y = 0.48 - 0.15 * k
    z = 0.35 - 0.012 * k if y < 0.3 else 0.33 - 0.25 * (y - 0.3)
    plates.blob(Vector((0, y, z)), (0.02, 0.035, 0.018), 6, 3, PLATE, "shield" if y > 0.05 else f"plate_{min(4, max(1, int((0.08 - y) / 0.14) + 1))}",
                smooth=False)

# ----------------------------------------------------------------------------------------------------------------
# The legs: eight pairs, black, jointed, each a thigh up and out, a shin down to a hooked claw. Centipede's legs.
legs = kit.part("legs")
CLAWS = []
for side, sx in (("r", 1), ("l", -1)):
    for k, y in enumerate(LEG_Y):
        a, b = f"leg_{side}{k + 1}_a", f"leg_{side}{k + 1}_b"
        hip, knee, foot = sk[a].head, sk[a].tail, sk[b].tail
        # (Thick at the thigh, a spur at the knee, the shin bowed out and hooked: a crab's leg as much as a centipede's.)
        legs.tube([hip, hip.lerp(knee, 0.5) + Vector((0, 0, 0.018)), knee], [0.034, 0.03, 0.024], 6, LEG, a, ref=(0, 1, 0), cap0=True)
        legs.blob(knee, (0.028, 0.026, 0.026), 6, 4, LEG, a)
        legs.tube([knee, knee + Vector((sx * 0.02, -0.01, 0.045))], [0.012, 0.002], 4, CLAW, a, ref=(0, 1, 0), cap1="point", smooth=False)
        shin = [knee, knee.lerp(foot, 0.45) + Vector((sx * 0.03, 0, 0.01)), foot + Vector((0, 0, 0.03))]
        legs.tube(shin, [0.022, 0.018, 0.012], 6, LEG, b, ref=(0, 1, 0))
        # The claw: hooked in under it.
        c0 = foot + Vector((0, 0, 0.03))
        tip = foot + Vector((-sx * 0.035, 0.012, -0.002))
        CLAWS.append(tip)
        legs.tube([c0, foot + Vector((sx * 0.004, 0.004, 0.006)), tip], [0.011, 0.008, 0.0015], 5, CLAW, b, ref=(0, 1, 0), cap1="point",
                  smooth=False)
# The mandibles: short, hooked, toothed, under the shield's front.
for side, sx in (("r", 1), ("l", -1)):
    m = sk[f"mandible_{side}"]
    h, t = m.head, m.tail
    legs.tube([h, h.lerp(t, 0.5) + Vector((sx * 0.012, 0, 0)), t, t + Vector((-sx * 0.04, 0.01, -0.01))], [0.022, 0.018, 0.012, 0.002], 6, CLAW,
              f"mandible_{side}", ref=(0, 0, 1), cap1="point", smooth=False)

# ----------------------------------------------------------------------------------------------------------------
# Clips. In its truck it's rolled onto its right side, its back out (+X), centred on the origin (the axle box).
LEG_NAMES = [(side, k) for side in "rl" for k in range(1, LEGS + 1)]


def legs_pose(fold=0.0, splay=0.0, wave=None, t=0.0, curl=0.0):
    """Every leg: folded in against it (fold 1: hugging the truck), splayed out (splay 1: braced wide on the ground), a
    walking wave (wave: its phase speed; t 0..1), curled under (curl 1: dead)."""
    p = {}
    for side, k in LEG_NAMES:
        sx = 1 if side == "r" else -1
        lift = 0.0
        swing = 0.0
        if wave is not None:
            ph = 2 * math.pi * (t * wave - k * 0.17 + (0.5 if side == "l" else 0))
            lift = max(0.0, math.sin(ph)) * 25
            swing = math.cos(ph) * 18
        up = -40 * fold + 10 * splay + lift + 50 * curl
        p[f"leg_{side}{k}_a"] = (swing * 0.3, -sx * up, swing)
        p[f"leg_{side}{k}_b"] = (0, sx * (40 * fold - 15 * splay + 60 * curl), 0)
    return p


def rolled(pose, x=0.0, y=0.0, z=0.0, roll=90.0, pitch=0.0):
    """The pose rolled onto its right side (its back out to +X) and carried to (x, y, z)."""
    p = dict(pose)
    p["root"] = (pitch, roll, 0)
    p["root@loc"] = (x, y, z)
    return p


# (Its middle 0.2 m up from its pivot: rolled 90 degrees, the pivot goes 0.2 m in from its middle. Carried so its middle is
# on the axle box, its belly against the truck.)
IN_TRUCK = dict(x=-0.16, y=0.0, z=0.0)
SHUT = {"shield": (4, 0, 0), **{f"plate_{k}": (2, 0, 0) for k in range(1, 5)}}


def belly(k):
    """The abdomen swelling (k 0..1) with heat."""
    s = 1 + 0.12 * k
    return {"abd_1@scale": (s, 1.0, s), "abd_2@scale": (s * 1.04, 1.0, s * 1.04), "abd_3@scale": (s, 1.0, s)}


clamped = Clip("clamped")
for f, k in ((0, 0.0), (24, 0.5), (48, 0.0)):
    clamped.key(f, rolled({**legs_pose(fold=1.0), **SHUT, **belly(k)}, **IN_TRUCK), "BEZIER")
clamped.close(60)

knock = Clip("knock")
for f, hammer in ((0, 0.0), (5, 1.0), (7, -0.3), (15, 0.0)):
    p = {**legs_pose(fold=1.0), **SHUT, **belly(0.2)}
    p["leg_r1_a"] = (-20 * hammer, -40 + 30 * hammer, 0)
    p["leg_r1_b"] = (0, 60 * hammer, 0)
    knock.key(f, rolled(p, **IN_TRUCK), "LINEAR" if f in (5, 7) else "BEZIER")
knock.close(15)

glow = Clip("glow")
for f, k in ((0, 0.6), (8, 1.0), (16, 0.7), (22, 1.0), (30, 0.6)):
    lift = {"shield": (-6 * k, 0, 0), **{f"plate_{j}": (-8 * k, 0, 0) for j in range(1, 5)}}
    glow.key(f, rolled({**legs_pose(fold=1.0, wave=None), **lift, **belly(0.6 + 0.6 * k)}, **IN_TRUCK), "BEZIER")
glow.close(36)

# Half out of its truck: its back half still in there, the front down on the ballast (0.62 m under the axle box) and onto
# the rail, braced, the head up.
OUT = dict(x=0.14, y=0.05, z=-0.62)
unfold = Clip("unfold", loop=False)
unfold.key(0, rolled({**legs_pose(fold=1.0), **SHUT, **belly(0.5)}, **IN_TRUCK), "BEZIER")
unfold.key(10, rolled({**legs_pose(fold=0.3, splay=0.6), **belly(0.7)}, x=0.1, z=-0.25, roll=50, pitch=-20), "LINEAR")
unfold.key(20, rolled({**legs_pose(splay=1.0), **belly(0.8)}, roll=6, pitch=-24, **OUT), "LINEAR")
unfold.key(30, rolled({**legs_pose(splay=0.8), **belly(0.7), "head": (14, 0, 0)}, roll=4, pitch=-20, **OUT), "BEZIER")

out = Clip("out")
for f, (k, chew) in enumerate(((0.6, 0), (0.9, 1), (0.7, 0), (1.0, 1))):
    p = {**legs_pose(splay=0.8 + 0.1 * chew, wave=None), **belly(k), "head": (14 + 4 * chew, 0, 0),
         "mandible_r": (0, 0, 18 * chew), "mandible_l": (0, 0, -18 * chew),
         "shield": (-3 * chew, 0, 0)}
    out.key(f * 12, rolled(p, roll=4, pitch=-20, **OUT), "BEZIER")
out.close(48)

snap = Clip("snap", loop=False)
base = {**legs_pose(splay=0.8), **belly(0.8)}
snap.key(0, rolled({**base, "head": (14, 0, 0)}, roll=4, pitch=-20, **OUT), "BEZIER")
snap.key(6, rolled({**base, "head": (-10, 0, 0), "mandible_r": (0, 0, 40), "mandible_l": (0, 0, -40), "shield": (8, 0, 0)}, roll=4, pitch=-14,
                   x=0.14, y=-0.05, z=-0.6), "LINEAR")
snap.key(9, rolled({**base, "head": (24, 0, 0), "mandible_r": (0, 0, -16), "mandible_l": (0, 0, 16), "shield": (-6, 0, 0)}, roll=4, pitch=-26,
                   x=0.14, y=0.25, z=-0.64), "CONSTANT")
snap.key(14, rolled({**base, "head": (20, 0, 0), "mandible_r": (0, 0, -16), "mandible_l": (0, 0, 16)}, roll=4, pitch=-24, x=0.14, y=0.22, z=-0.63),
         "LINEAR")
snap.key(20, rolled({**base, "head": (14, 0, 0)}, roll=4, pitch=-20, **OUT), "BEZIER")

# Prised (1 s, once): levered out of the truck, it drops over onto its back on the ballast, flailing, then rights itself.
prised = Clip("prised", loop=False)
prised.key(0, rolled({**legs_pose(splay=0.8), **belly(0.7)}, roll=4, pitch=-20, **OUT), "LINEAR")
prised.key(6, rolled({**legs_pose(wave=3, t=0.2), **belly(0.6)}, x=0.5, y=0.0, z=-0.45, roll=120, pitch=-10), "LINEAR")
prised.key(12, rolled({**legs_pose(wave=3, t=0.5, curl=0.3), **belly(0.6)}, x=0.8, y=0.0, z=-0.32, roll=175, pitch=0), "LINEAR")
prised.key(18, rolled({**legs_pose(wave=3, t=0.8, curl=0.2), **belly(0.6)}, x=0.9, y=0.0, z=-0.32, roll=178, pitch=0), "LINEAR")
prised.key(24, rolled({**legs_pose(splay=0.6), **belly(0.5)}, x=1.0, y=0.0, z=-0.62, roll=360, pitch=0), "LINEAR")
prised.key(30, rolled({**legs_pose(splay=0.4), **belly(0.5)}, x=1.0, y=0.0, z=-0.62, roll=360, pitch=0), "BEZIER")

# Scuttle (0.5 s, loop): off at a run (CreatureArt carries it off into the dark), its legs in waves down each side.
scuttle = Clip("scuttle")
for f in range(0, 15, 3):
    t = f / 15
    p = {**legs_pose(wave=1, t=t), **belly(0.4)}
    scuttle.key(f, rolled(p, x=0.0, y=0.0, z=0.02 * math.sin(2 * math.pi * t * 2), roll=0), "LINEAR")
scuttle.close(15)

hit = Clip("hit", loop=False)
hit.key(0, rolled({**legs_pose(splay=0.8), **belly(0.8)}, roll=4, pitch=-20, **OUT), "CONSTANT")
hit.key(2, rolled({**legs_pose(fold=0.4, splay=0.2), **belly(1.0), "shield": (10, 0, 0), "head": (-14, 0, 0)}, roll=-6, pitch=-12, x=0.18, y=-0.06,
                  z=-0.6), "CONSTANT")
hit.key(8, rolled({**legs_pose(splay=0.7), **belly(0.8)}, roll=2, pitch=-18, **OUT), "LINEAR")
hit.key(12, rolled({**legs_pose(splay=0.8), **belly(0.8)}, roll=4, pitch=-20, **OUT), "CONSTANT")

# Death (1.2 s, once): flipped over onto its back on the ballast, its legs curling in over its belly, the glow going out.
death = Clip("death", loop=False)
death.key(0, rolled({**legs_pose(splay=0.8), **belly(0.8)}, roll=4, pitch=-20, **OUT), "CONSTANT")
death.key(8, rolled({**legs_pose(wave=4, t=0.3), **belly(1.0)}, x=0.35, y=0.0, z=-0.4, roll=110, pitch=-10), "LINEAR")
death.key(16, rolled({**legs_pose(wave=4, t=0.6, curl=0.5), **belly(0.5)}, x=0.5, y=0.0, z=-0.2, roll=180, pitch=0), "LINEAR")
death.key(36, rolled({**legs_pose(curl=1.0), **belly(0.0)}, x=0.5, y=0.0, z=-0.2, roll=180, pitch=0), "BEZIER")

CLIPS = [clamped, knock, glow, unfold, out, snap, prised, scuttle, hit, death]

kit.build()
fl.fuse(kit, "body", ["body"], voxel=0.0035, faces=SKIN_FACES, lose=0.03, settle=F.settle, relax=2)
rig.bake(sk, CLIPS)
print("[dt] hotbox", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
dest = rig.args()[0] if rig.args() else "hotbox.glb"
rig.export(dest, kit)
if __name__ != "overbake_source":
    rig.export_lod(dest.replace(".glb", ".lod1.glb"), kit, 0.4)
