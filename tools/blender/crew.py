"""CREW: a rail worker (GDD §29 "practical, rail-working, soot-covered, bundled against cold, slightly anonymous"), in the
steampunk, post-apocalyptic kit art direction asked for: masked (nothing to lip-sync, no eyes to animate), but with some
fun to it, and each player in their own colour.

Heavy knee-length oilskin coat, patched; gloves, boots for wet metal and ballast, a belt with a lantern hook, a satchel on
a strap, and the chest lamp the greybox had: the thing you find each other by in the dark (GreyboxScene.DrawCrewmate).
The head is the helm: a smokebox for a head, a riveted drum painted the player's colour and closed by a smokebox door
with two portholes for eyes, the dart's knob for a nose and a speaking grille for a mouth; a gauge on its side, a
chimney stack on top. A copper air tank on the back feeds it by a hose over the shoulder. 1.8 m to the shoulders' coat,
SK_Human, the helm big for its body: at forty metres in the fog a crewmate is a lamp and a round head.

Variants (CreatureArt `variant % 4`): 0 short stack, 1 tall stack and whistle, 2 short + scarf, 3 tall + scarf.
DT_CREW=bare builds the figure bare-headed instead, the cap or the steel helmet for the stacks (the husk's source).
Clips (30 fps): idle, walk (1.4 m/s, in place), run (4 m/s), climb, shovel (with the shovel prop), crouch_idle, dead.

    blender -b --python tools/blender/crew.py -- content/art/models/crew.glb

This is the crew's game mesh, rig and clips. The game's crew.glb is tools/models/recipes/crew.py's, which runs this
script, models a high-resolution copy over it and bakes that down onto this mesh (tools/models/build.sh crew).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, along, hexc, in_cell, mirror, over, smoothstep, swap  # noqa: E402

rig.reset()
sk = rig.human(1.8)
sk.build()
kit = rig.Kit(sk, "crew")

COAT = Mat("crew_atlas.coat", hexc("#3a3826"), shine=0.12)
SLEEVE = Mat("crew_atlas.sleeve", hexc("#3a3826"), shine=0.12)
TROUSER = Mat("crew_atlas.trouser", hexc("#26262c"))
BOOT = Mat("crew_atlas.boot", hexc("#2a2018"), shine=0.1)
GLOVE = Mat("crew_atlas.gloves", hexc("#4a3424"))
FACE = Mat("crew_atlas.face", hexc("#8a6a58"))
HAIR = Mat("crew_atlas.hair", hexc("#1a1410"))
NECK = Mat("skin.neck", hexc("#7a5a4a"))
CAP = Mat("crew_atlas.cap", hexc("#26282e"))
HELMET = Mat("crew_atlas.helmet", hexc("#4a4a30"), shine=0.3)
BELT = Mat("crew_atlas.belt", hexc("#3a2818"), shine=0.15)
SATCHEL = Mat("crew_atlas.satchel", hexc("#5a5040"))
STRAP = Mat("leather.strap", hexc("#3a2818"))
BADGE = Mat("crew_atlas.badge", hexc("#8a7040"), shine=0.4)
SCARF = Mat("crew_atlas.scarf", hexc("#221c1c"))
LAMP = Mat("crew_atlas.lamp", hexc("#e0a050"), emissive=1.0)
IRON = Mat("iron_plate.crew", hexc("#2e2e2e"), shine=0.4)
SHOVEL = Mat("rust_heavy.shovel", hexc("#3a2a20"), shine=0.3)
HANDLE = Mat("wood_grey.handle", hexc("#4a4034"))

body = kit.part("body")

# --- weights -------------------------------------------------------------------------------------------------
# The torso blends up the spine by height; the coat's skirt follows the thighs part-way so a stride doesn't push the
# legs through it (a PS2 coat: one skirt, dragged by the legs, no cloth sim).
TORSO = along("z", [(0.9, "pelvis"), (1.12, "spine_01"), (1.27, "spine_02"), (1.40, "spine_03")])


def coat_weights(p):
    w = TORSO(p)
    t = smoothstep(0.98, 0.5, p.z) * 0.6
    if t > 0:
        side = max(-1.0, min(1.0, p.x / 0.1))
        w = {k: v * (1 - t) for k, v in w.items()}
        w["thigh_r"] = w.get("thigh_r", 0) + t * (0.5 + 0.5 * side)
        w["thigh_l"] = w.get("thigh_l", 0) + t * (0.5 - 0.5 * side)
    if p.z > 1.36 and abs(p.x) > 0.12:
        # Shoulder corners ride the clavicle, so a raised arm lifts the coat's shoulder with it.
        k = smoothstep(0.12, 0.2, abs(p.x)) * 0.6
        side = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"clavicle_{side}"] = w.get(f"clavicle_{side}", 0) + k
    tot = sum(w.values())
    return {k: v / tot for k, v in w.items() if v / tot > 0.02}


ARM = along("x", [(0.14, "spine_03"), (0.2, "clavicle_{s}"), (0.26, "upperarm_{s}"), (0.43, "upperarm_{s}"),
                  (0.49, "lowerarm_{s}"), (0.70, "lowerarm_{s}"), (0.74, "hand_{s}")])
LEG = along("z", [(0.08, "foot_{s}"), (0.12, "calf_{s}"), (0.47, "calf_{s}"), (0.56, "thigh_{s}"),
                  (0.9, "thigh_{s}"), (1.0, "pelvis")])


def sided(fn):
    """Resolves {s} in `along` stops that key on a coordinate other than x."""
    def w(p):
        out = fn(p)
        s = "r" if p.x >= 0 else "l"
        return {k.format(s=s): v for k, v in out.items()}
    return w


LEGW = sided(LEG)

# --- head ----------------------------------------------------------------------------------------------------
HEAD_C = Vector((0, 0.02, 1.675))

# The crew's own heads are the helm (ART DIRECTION, ARCHITECTURE §8 note 58: masked, so nothing to lip-sync and no eyes
# to animate, and with some fun to it): DT_CREW=bare builds the bare-headed figure in a cap or a helmet instead, for
# what the crew become (tools/models/recipes/husk.py).
HELM = os.environ.get("DT_CREW", "helm") != "bare"
HC = Vector((0, 0.03, 1.73))    # the helm's drum, round the head
PAINT = Mat("helm.paint", hexc("#8a3a2a"), shine=0.3)          # the player's colour (CreatureArt tints it)
DOOR = Mat("helm.door", hexc("#7a3428"), shine=0.3)            # painted too: the face is the player's colour
BRASS = Mat("helm.brass", hexc("#a08040"), shine=0.6)
PORT = Mat("helm.glass", hexc("#5a3414"), emissive=1.0)         # lit dimly from inside, by the wearer's own lamp
RUBBER = Mat("helm.rubber", hexc("#18161a"), shine=0.2)
COPPER = Mat("tank.copper", hexc("#8a5030"), shine=0.55)
STACK = Mat("helm.stack", hexc("#1e1c1a"), shine=0.3)
HARNESS = Mat("leather.harness", hexc("#3a2818"))


def ring_loop(centre, axis, r, thick, mat, bones, n=16):
    """A torus-ish band (a rim, a strap round a tank): a closed tube round `axis` through `centre`."""
    c, ax = Vector(centre), Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    pts = [c + (u * math.cos(2 * math.pi * k / n) + v * math.sin(2 * math.pi * k / n)) * r for k in range(n)]
    body.tube(pts, [thick] * n, 5, mat, bones, ref=[tuple(ax)] * n, loop=True)


def helm_head():
    """A smokebox for a head: a riveted drum lying fore and aft round the skull, painted the wearer's colour, closed at
    the front by a smokebox door, painted too (the loco's face: two portholes for eyes, lit dimly by the wearer's lamp, the dart's knob for a nose, a speaking grille
    for a mouth, its hinges across one side), a pressure gauge on the right, sealed onto the coat's collar by a rubber
    ring. Bigger than a head needs to be: at forty metres in the fog, a crewmate is a lamp and a round head."""
    zc = HC.z
    secs = [(-0.182, 0.035, zc + 0.03, zc - 0.03, 1.0), (-0.172, 0.095, zc + 0.09, zc - 0.085, 1.0),
            (-0.152, 0.132, zc + 0.125, zc - 0.12, 1.0), (-0.12, 0.148, zc + 0.14, zc - 0.135, 1.0),
            (0.0, 0.15, zc + 0.142, zc - 0.137, 1.0), (0.1, 0.15, zc + 0.142, zc - 0.137, 1.0),
            (0.132, 0.152, zc + 0.144, zc - 0.139, 1.0)]
    body.sections([(y + HC.y, hw, t, b_, e) for y, hw, t, b_, e in secs], 20, PAINT, "head", axis="y", cap0=True, cap1=True)
    y0 = HC.y + 0.132
    # The door: a shallow dish over the drum's open front, and the brass rim it closes on.
    body.blob((0, y0, zc), (0.138, 0.13, 0.034), 18, 5, DOOR, "head", rot=rig.Matrix.Rotation(math.radians(-90), 4, "X"), z0=0.0)
    ring_loop((0, y0 + 0.002, zc), (0, 1, 0), 0.146, 0.011, BRASS, "head", n=20)
    for y in (HC.y - 0.118, HC.y + 0.098):
        ring_loop((0, y, zc), (0, 1, 0), 0.153, 0.009, STACK, "head", n=20)
    # The eyes: two portholes, brass-rimmed, smoked glass.
    for sx in (-1, 1):
        e = Vector((sx * 0.058, y0 + 0.029, zc + 0.03))
        ring_loop(e, (0, 1, 0), 0.036, 0.01, BRASS, "head", n=12)
        body.blob(e - Vector((0, 0.002, 0)), (0.031, 0.031, 0.008), 10, 3, PORT, "head",
                  rot=rig.Matrix.Rotation(math.radians(-90), 4, "X"))
    # The nose: the door's dart, a knob on a boss, and its crossbar; the mouth, a speaking grille.
    body.tube([(0, y0 + 0.03, zc - 0.012), (0, y0 + 0.052, zc - 0.012), (0, y0 + 0.058, zc - 0.012)], [0.014, 0.012, 0.016],
              8, BRASS, "head", ref=(0, 0, 1), cap1=True)
    body.box((0, y0 + 0.024, zc - 0.072), (0.052, 0.01, 0.02), BRASS, "head")
    # The hinges: two straps across the door from the left.
    for dz in (0.09, -0.09):
        body.box((-0.06, y0 + 0.014, zc + dz), (0.038, 0.005, 0.01), STACK, "head")   # (inside the rim: it's 0.11 wide there)
    # The gauge on the right, and the seal on the collar.
    body.tube([(0.148, HC.y - 0.02, zc + 0.03), (0.172, HC.y - 0.02, zc + 0.03)], [0.034, 0.034], 12, BRASS, "head",
              ref=(0, 0, 1), cap1=True)
    ring_loop((0, 0.008, 1.612), (0, 0, 1), 0.106, 0.02, RUBBER, "neck", n=16)


def helm_tops():
    """What stands up out of the helm (variants): a short capped stack, or a tall one with a spark arrester and a whistle
    beside it. The two heads you can tell apart on a roof in the dark."""
    top = HC.z + 0.13
    short = kit.part("stack_short", variants=(0, 2))
    short.tube([(0, HC.y + 0.03, top - 0.02), (0, HC.y + 0.03, top + 0.05), (0, HC.y + 0.03, top + 0.062),
                (0, HC.y + 0.03, top + 0.07)], [0.034, 0.03, 0.046, 0.04], 12, STACK, "head", ref=(0, 1, 0), cap1=True)
    tall = kit.part("stack_tall", variants=(1, 3))
    tall.tube([(0, HC.y + 0.03, top - 0.02), (0, HC.y + 0.03, top + 0.12), (0, HC.y + 0.03, top + 0.15),
               (0, HC.y + 0.03, top + 0.2), (0, HC.y + 0.03, top + 0.215)], [0.03, 0.027, 0.05, 0.05, 0.03], 12, STACK,
              "head", ref=(0, 1, 0), cap1=True)
    tall.tube([(0.085, HC.y - 0.06, top - 0.03), (0.085, HC.y - 0.06, top + 0.04), (0.085, HC.y - 0.06, top + 0.07)],
              [0.011, 0.011, 0.017], 8, BRASS, "head", ref=(0, 1, 0), cap1=True)


def tank():
    """The air: a copper tank on the back, banded in brass, a valve on top and a hose over the shoulder into the helm
    (what the dark out here does to the air is why nobody takes the helm off)."""
    x, y = 0.0, -0.222
    body.tube([(x, y, 1.03), (x, y, 1.05), (x, y, 1.08), (x, y, 1.36), (x, y, 1.39), (x, y, 1.41)],
              [0.04, 0.066, 0.078, 0.078, 0.066, 0.04], 14, COPPER, TORSO, ref=(0, 1, 0), cap0=True, cap1=True)
    for z in (1.12, 1.32):
        ring_loop((x, y, z), (0, 0, 1), 0.081, 0.008, BRASS, TORSO, n=14)
    body.tube([(x, y, 1.41), (x, y, 1.44)], [0.018, 0.022], 8, BRASS, TORSO, ref=(0, 1, 0), cap1=True)

    # The tank's harness: two straps over the shoulders and down the chest to the belt, brass buckles at the breast.
    for sx in (-1, 1):
        pts = [(sx * 0.09, -0.2, 1.36), (sx * 0.11, -0.13, 1.47), (sx * 0.12, 0.0, 1.5), (sx * 0.115, 0.12, 1.45),
               (sx * 0.1, 0.165, 1.3), (sx * 0.1, 0.17, 1.15), (sx * 0.1, 0.165, 1.03)]
        body.tube(pts, [(0.022, 0.006)] * len(pts), 4, HARNESS, coat_weights,
                  ref=[Vector((0, 0, 1)), Vector((0, 0.3, 1)), Vector((0, 0, 1)), Vector((0, 1, 0.4)), Vector((0, 1, 0)),
                       Vector((0, 1, 0)), Vector((0, 1, 0))])
        body.box((sx * 0.1, 0.176, 1.22), (0.026, 0.006, 0.02), BRASS, TORSO)

    def hose_w(p):
        return {"head": 1.0} if p.z > 1.64 else ({"neck": 1.0} if p.z > 1.55 else TORSO(p))
    body.tube([(0.03, y, 1.43), (0.075, y + 0.02, 1.5), (0.09, -0.15, 1.58), (0.075, -0.11, 1.64), (0.05, -0.09, 1.67)],
              [0.018, 0.019, 0.019, 0.019, 0.018], 7, RUBBER, hose_w, ref=(0, 0, 1))


def head_shape(i, j, a, th, p):
    q = p - HEAD_C
    # Jaw narrower and forward, back of the skull flatter, a heavy brow: a tired face under a cap.
    if q.z < -0.03:
        q.x *= 0.86 - 0.3 * max(0.0, -q.z - 0.06)
        if q.y > 0:
            q.y *= 1.04
    if q.y < -0.05:
        q.y *= 0.9
    if 0.0 < q.z < 0.04 and q.y > 0.06:
        q.y += 0.008
    return HEAD_C + q


def face_uv(pts, n):
    out = []
    if n.y > 0.5 and abs(n.z) < 0.8:
        for p in pts:
            out.append(in_cell("face_front", 0.5 + p.x / 0.2, (1.79 - p.z) / 0.235))
    elif n.z < -0.55:
        for p in pts:
            out.append(in_cell("face_front", 0.5 + p.x / 0.2, 0.9 + (p.y - 0.0) * 0.3))
    elif abs(n.x) > 0.35 and n.y > -0.45:
        for p in pts:
            out.append(in_cell("face_side", (0.12 - p.y) / 0.21, (1.79 - p.z) / 0.235))
    else:
        for p in pts:
            out.append(in_cell("hair", 0.5 + p.x / 0.22, (1.8 - p.z) / 0.24))
    return out


if not HELM:
    # The egg of a head, its nose and ears, and the neck: the bare figure (the husk's, under tools/models).
    body.blob(HEAD_C, (0.086, 0.103, 0.118), 14, 9, FACE, "head", shape=head_shape, fuv=face_uv)
    # Nose and ears: small, blunt, enough to break the egg.
    body.box((0, 0.122, 1.662), (0.014, 0.014, 0.026), FACE, "head", taper=(0.7, 0.5),
             uv=lambda ax, sg, l: in_cell("face_front", 0.5 + l.x / 0.2, 0.55 - l.z / 0.235))
    for sx in (-1, 1):
        body.box((sx * 0.085, 0.0, 1.672), (0.012, 0.02, 0.03), FACE, "head",
                 uv=lambda ax, sg, l: in_cell("face_side", 0.55 - l.y / 0.2, 0.45 - l.z / 0.2))
    # Neck.
    body.tube([(0, 0.0, 1.49), (0, 0.01, 1.57), (0, 0.015, 1.61)], [0.056, 0.052, 0.05], 8, NECK, "neck")
else:
    helm_head()


# --- coat ----------------------------------------------------------------------------------------------------
# (z, rx, ry, y offset, squareness): hem at the knee, flared; belted waist; square, slightly rounded shoulders.
COAT_RINGS = [(0.50, 0.24, 0.19, 0.0, 0.9), (0.56, 0.234, 0.184, 0.0, 0.9), (0.62, 0.228, 0.178, 0.0, 0.9),
              (0.69, 0.22, 0.171, 0.0, 0.9), (0.76, 0.212, 0.165, 0.0, 0.9), (0.83, 0.205, 0.158, -0.003, 0.88),
              (0.89, 0.198, 0.152, -0.005, 0.85), (0.95, 0.19, 0.145, -0.005, 0.85), (1.0, 0.182, 0.138, -0.005, 0.85),
              (1.05, 0.185, 0.14, -0.003, 0.82), (1.1, 0.19, 0.142, 0.0, 0.8), (1.16, 0.198, 0.146, 0.004, 0.78),
              (1.22, 0.205, 0.148, 0.008, 0.75), (1.28, 0.211, 0.147, 0.008, 0.72), (1.33, 0.215, 0.145, 0.006, 0.7),
              (1.38, 0.215, 0.14, 0.0, 0.67), (1.41, 0.212, 0.13, -0.004, 0.65), (1.45, 0.19, 0.118, -0.008, 0.68),
              (1.47, 0.165, 0.108, -0.01, 0.7), (1.51, 0.098, 0.09, 0.0, 1.0), (1.585, 0.1, 0.094, 0.004, 1.0),
              (1.625, 0.112, 0.104, 0.0, 1.0)]


def coat_uv(pts, n):
    cellname = "coat_front" if n.y >= 0 else "coat_back"
    k = 1 if n.y >= 0 else -1
    return [in_cell(cellname, 0.5 + k * p.x / 0.52, (1.58 - p.z) / 1.1) for p in pts]


rings = []
for z, rx, ry, dy, sq in COAT_RINGS:
    ring = []
    for j in range(22):
        a = 2 * math.pi * j / 22
        sa, ca = math.sin(a), math.cos(a)
        sa = math.copysign(abs(sa) ** sq, sa)
        ca = math.copysign(abs(ca) ** sq, ca)
        # The front hangs open a little lower than the back (a heavy coat's weight), and the hem is ragged.
        drop = 0.02 * max(0.0, ca) if z < 0.55 else 0.0
        rag = 0.012 * rig.noise3(Vector((sa * 4, ca * 4, z)), 3, 3.0) if z < 0.55 else 0.0
        ring.append(Vector((sa * rx, dy + ca * ry, z - drop + rag)))
    rings.append(ring)
body.loft(rings, COAT, coat_weights, centres=[Vector((0, r[3], r[0])) for r in COAT_RINGS], fuv=coat_uv)

# The coat's front edge, lapped over, standing proud down the middle; two pocket flaps on the hips.
edge = []
for z, rx, ry, dy, sq in COAT_RINGS[:-3]:
    edge.append(Vector((0.035, dy + ry + 0.004, z + 0.01)))
body.tube(edge, [(0.016, 0.006)] * len(edge), 4, COAT, coat_weights, ref=(0, 1, 0),
          uv=lambda i, j, uf, vf, p: in_cell("coat_front", 0.62 + uf * 0.1, vf))
for sx in (-1, 1):
    body.box((sx * 0.13, 0.155, 0.8), (0.07, 0.012, 0.03), COAT, coat_weights,
             rot=rig.Matrix.Rotation(math.radians(-sx * 28), 4, "Z"),
             uv=lambda ax, sg, l: in_cell("coat_front", 0.2 + l.x * 2, 0.72 - l.z * 2))

# Sleeves: shoulder to a turned-back cuff over the glove.
for sx in (-1, 1):
    pts = [(sx * x, 0, 1.448) for x in (0.12, 0.2, 0.33, 0.45, 0.58, 0.69, 0.705, 0.73)]
    radii = [0.088, 0.082, 0.074, 0.067, 0.063, 0.058, 0.068, 0.066]
    body.tube(pts, radii, 12, SLEEVE, ARM, ref=(0, 0, 1), cap1=False,
              uv=lambda i, j, uf, vf, p: in_cell("coat_back", uf, 0.35 + vf * 0.6))

# Belt over the coat, the buckle at the front; the lantern hook on the left hip.
belt = [(0, -0.005, z) for z in (0.975, 1.035)]
body.tube(belt, [(0.194, 0.15), (0.19, 0.147)], 18, BELT, TORSO, ref=(0, 1, 0), twist=math.pi, square=0.85,
          uv=lambda i, j, uf, vf, p: in_cell("belt_buckle", 0.41 + (uf - 0.5) * 1.0, 0.33 + vf * 0.34))
body.box((-0.196, 0.03, 0.975), (0.008, 0.016, 0.04), IRON, TORSO)
body.box((-0.205, 0.03, 0.94), (0.012, 0.012, 0.008), IRON, TORSO)

# Satchel on the right hip, on a strap from the left shoulder.
body.box((0.225, 0.02, 0.86), (0.042, 0.125, 0.105), SATCHEL, sided(lambda p: {"pelvis": 0.7, "thigh_{s}": 0.3}),
         rot=rig.Matrix.Rotation(math.radians(-6), 4, "Y"),
         uv=lambda ax, sg, l: in_cell("satchel", 0.5 + l.y / 0.25, 0.5 - l.z / 0.21) if ax == "x" and sg > 0
         else in_cell("satchel", 0.2 + l.y * 0.5, 0.9 - l.z * 0.5))
strap = [(0.21, 0.11, 0.96), (0.13, 0.158, 1.13), (0.03, 0.17, 1.27), (-0.08, 0.152, 1.39), (-0.135, 0.085, 1.475),
         (-0.145, -0.02, 1.49), (-0.12, -0.13, 1.41), (-0.02, -0.158, 1.27), (0.1, -0.153, 1.12), (0.2, -0.11, 0.97)]
refs = [Vector((p[0] * 0.6, p[1], 0.0 if p[2] < 1.44 else 1.0)).normalized() for p in strap]
body.tube(strap, [(0.024, 0.006)] * len(strap), 4, STRAP, TORSO, ref=refs)

if HELM:
    tank()

# The chest lamp, clipped to the strap: iron box, amber lens. It's emissive: the one warm point on a crewmate.
body.box((-0.04, 0.178, 1.315), (0.038, 0.026, 0.048), IRON, "spine_02")
body.box((-0.04, 0.203, 1.315), (0.03, 0.004, 0.036), LAMP, "spine_02",
         uv=lambda ax, sg, l: in_cell("lantern_glass", 0.5 + l.x / 0.07, 0.55 - l.z / 0.08))
body.box((-0.04, 0.178, 1.372), (0.02, 0.018, 0.01), IRON, "spine_02")

# --- gloves --------------------------------------------------------------------------------------------------
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    g = lambda ax, sg, l: in_cell("gloves", 0.5 + l.y / 0.1, 0.6 - l.x / 0.12)  # noqa: E731
    body.box((sx * 0.768, 0, 1.446), (0.052, 0.046, 0.024), GLOVE, f"hand_{s}", uv=g)
    body.box((sx * 0.858, 0.0, 1.441), (0.044, 0.043, 0.019), GLOVE, f"fingers_{s}", uv=g)
    body.box((sx * 0.787, 0.052, 1.44), (0.03, 0.015, 0.016), GLOVE, f"thumb_{s}",
             rot=rig.Matrix.Rotation(math.radians(-40 * sx), 4, "Z"), uv=g)

# --- legs and boots ------------------------------------------------------------------------------------------
for sx in (-1, 1):
    x = sx * 0.105
    pts = [(sx * 0.1, 0, 0.99), (sx * 0.103, 0.004, 0.76), (x, 0.012, 0.515), (x, 0.0, 0.34), (x, -0.008, 0.22)]
    body.tube(pts, [0.098, 0.086, 0.07, 0.062, 0.058], 12, TROUSER, LEGW, ref=(0, 1, 0), twist=math.pi,
              uv=lambda i, j, uf, vf, p: in_cell("trouser", uf, vf))
    # Boot shaft, then the foot as a squared loft heel to toe.
    body.tube([(x, -0.01, 0.285), (x, -0.012, 0.16), (x, -0.012, 0.1)], [0.066, 0.068, 0.066], 10, BOOT,
              sided(lambda p: {"calf_{s}": 1.0} if p.z > 0.12 else {"calf_{s}": 0.4, "foot_{s}": 0.6}),
              twist=math.pi, uv=lambda i, j, uf, vf, p: in_cell("boot", uf, 0.05 + vf * 0.3))
    foot = [(-0.075, 0.056, 0.105), (-0.03, 0.062, 0.115), (0.05, 0.062, 0.09), (0.13, 0.058, 0.07),
            (0.195, 0.052, 0.055), (0.228, 0.036, 0.042)]
    rings = []
    cents = []
    for y, hw, top in foot:
        ring = []
        for j in range(8):
            a = 2 * math.pi * j / 8 + math.pi / 8
            sa, ca = math.sin(a), math.cos(a)
            sa = math.copysign(abs(sa) ** 0.55, sa)
            ca = math.copysign(abs(ca) ** 0.55, ca)
            zc = top / 2 + 0.012
            ring.append(Vector((x + sa * hw, y, zc + ca * (top / 2 - 0.004))))
        rings.append(ring)
        cents.append(Vector((x, y, top / 2)))
    body.loft(rings, BOOT, sided(lambda p: {"foot_{s}": 1.0} if p.y < 0.1 else
                                 ({"ball_{s}": 1.0} if p.y > 0.15 else {"foot_{s}": 0.5, "ball_{s}": 0.5})),
              centres=cents, cap0=True, cap1=True,
              fuv=lambda pts, n: [in_cell("boot", (p.y + 0.08) / 0.32, (0.3 - p.z) / 0.3) for p in pts])
    # A thick sole, proud of the upper all round: boots made for wet metal and ballast.
    body.box((x, 0.075, 0.009), (0.064, 0.158, 0.009), BOOT,
             sided(lambda p: {"foot_{s}": 1.0} if p.y < 0.1 else {"ball_{s}": 1.0}),
             uv=lambda ax, sg, l: in_cell("boot", 0.5 + l.y / 0.32, 0.93))

# --- hats and scarf (variants) -------------------------------------------------------------------------------
if not HELM:
    cap = kit.part("hat_cap", variants=(0, 2))
    cap.tube([(0, 0.012, 1.728), (0, 0.012, 1.762), (0, 0.006, 1.795), (0, 0.004, 1.808)],
             [(0.097, 0.108), (0.1, 0.111), (0.112, 0.12), (0.098, 0.106)], 12, CAP, "head", cap1=True,
             uv=lambda i, j, uf, vf, p: in_cell("cap", uf, 0.72 - vf * 0.6))
    cap.slab([(-0.088, 0.075, 1.738), (0.088, 0.075, 1.738), (0.075, 0.165, 1.722), (0.0, 0.19, 1.717),
              (-0.075, 0.165, 1.722)], 0.008, CAP, "head", uv=lambda p: in_cell("cap", 0.5 + p.x / 0.2, 0.84 + (p.y - 0.07) * 0.5))
    cap.box((0, 0.118, 1.762), (0.022, 0.003, 0.014), BADGE, "head",
            uv=lambda ax, sg, l: in_cell("badge", 0.5 + l.x / 0.05, 0.5 - l.z / 0.034))

    helmet = kit.part("hat_helmet", variants=(1, 3))
    helmet.blob((0, 0.006, 1.73), (0.118, 0.13, 0.09), 12, 4, HELMET, "head", z0=0.0,
                uv=lambda i, j, uf, vf, p: in_cell("helmet", uf, vf * 0.8))
    helmet.tube([(0, 0.006, 1.733), (0, 0.006, 1.722), (0, 0.006, 1.712)], [(0.12, 0.132), (0.142, 0.156), (0.146, 0.16)],
                12, HELMET, "head", uv=lambda i, j, uf, vf, p: in_cell("helmet", uf, 0.84 + vf * 0.12))
else:
    helm_tops()

scarf = kit.part("scarf", variants=(2, 3))
ring = [(math.sin(a) * 0.098, 0.004 + math.cos(a) * 0.095, 1.535 + 0.012 * math.cos(a))
        for a in [2 * math.pi * k / 12 for k in range(12)]]
scarf.tube(ring, [(0.034, 0.04)] * 12, 6, SCARF, along("z", [(1.5, "spine_03"), (1.56, "neck")]),
           ref=[Vector((0, 0, 1))] * 12, loop=True, uv=lambda i, j, uf, vf, p: in_cell("scarf", vf, uf))
scarf.tube([(0.05, 0.1, 1.52), (0.065, 0.14, 1.42), (0.07, 0.15, 1.32)], [(0.03, 0.01), (0.032, 0.01), (0.03, 0.01)],
           4, SCARF, TORSO, ref=(0, 1, 0), cap1=True, uv=lambda i, j, uf, vf, p: in_cell("scarf", uf, vf))

# --- the shovel (only in the shovel clip) --------------------------------------------------------------------
# Held in the right fist across the palm (bind: along Y), blade forward. The left hand takes the D-grip in the clip.
shovel = kit.part("shovel", clip="shovel", smooth=False)
G = Vector((0.80, 0.0, 1.425))
shovel.tube([G + Vector((0, -0.62, 0)), G + Vector((0, 0.42, 0))], [0.017, 0.019], 6, HANDLE, "hand_r_weapon",
            ref=(0, 0, 1))
shovel.box(G + Vector((0, -0.66, 0)), (0.05, 0.012, 0.012), HANDLE, "hand_r_weapon")
blade = [G + Vector(v) for v in ((-0.12, 0.42, 0.0), (0.12, 0.42, 0.0), (0.13, 0.72, 0.02), (0.0, 0.76, 0.02),
                                 (-0.13, 0.72, 0.02))]
shovel.slab(blade, 0.01, SHOVEL, "hand_r_weapon")
for sx in (-1, 1):
    shovel.box(G + Vector((sx * 0.125, 0.58, 0.035)), (0.008, 0.15, 0.03), SHOVEL, "hand_r_weapon")

# --------------------------------------------------------------------------------------------------------------
# Clips. GDD §31: humans are "heavy, grounded, readable, a bit stiff, practical, hurried under stress".
# Angles in degrees, armature axes (rig.rot): arms lowered by Y, swung by X; legs swung by X (+ forward).

STAND = mirror({
    "spine_01": (-2, 0, 0), "spine_02": (-3, 0, 0), "spine_03": (-5, 0, 0), "neck": (8, 0, 0), "head": (-3, 0, 0),
    "clavicle_r": (0, 9, 4), "upperarm_r": (6, 72, 4), "lowerarm_r": (0, 0, 16), "hand_r": (0, 6, 0),
    "fingers_r": (0, 35, 0), "thumb_r": (0, 15, 0),
    "thigh_r": (1, 0, 0), "calf_r": (-2, 0, 0), "foot_r": (1, 0, -6),
})

idle = Clip("idle")
idle.key(0, STAND)
idle.key(22, over(STAND, spine_03=(-3.5, 0, 0), clavicle_r=(0, 6, 4), clavicle_l=(0, -6, -4), head=(-1, 0, 0)))
shift = over(STAND, pelvis__loc=(0.025, 0.0, 0.0), pelvis=(0, 3, 0), spine_01=(-2, -2.5, 0), spine_02=(-3, -1.5, 0),
             thigh_r=(1, -2.5, 0), thigh_l=(1, -3.5, 0), head=(-3, 1, 6))
idle.key(40, shift)
idle.key(52, over(shift, head=(-4, 0, -4), spine_03=(-4, 0, 0)))
idle.close(72)

# Walk: 1.4 m/s, one cycle 30 frames = 1.4 m (two 0.7 m steps). Contact, down, passing, up.
W_CONTACT = over(STAND, pelvis__loc=(0, 0, -0.025), pelvis=(0, 0, -5), spine_02=(-5, 0, 3), spine_03=(-6, 0, 3),
                 thigh_r=(24, 0, 0), calf_r=(-4, 0, 0), foot_r=(10, 0, -6),
                 thigh_l=(-16, 0, 0), calf_l=(-18, 0, 0), foot_l=(-12, 0, 6),
                 upperarm_r=(-18, 72, 4), lowerarm_r=(0, 0, 12), upperarm_l=(22, -72, -4), lowerarm_l=(0, 0, -28))
W_DOWN = over(W_CONTACT, pelvis__loc=(0, 0, -0.04), thigh_r=(18, 0, 0), calf_r=(-14, 0, 0), foot_r=(2, 0, -6),
              thigh_l=(-10, 0, 0), calf_l=(-40, 0, 0), foot_l=(-20, 0, 6))
W_PASS = over(STAND, pelvis__loc=(0, 0, 0.01), pelvis=(0, 0, 0), spine_02=(-4, 0, 0), spine_03=(-6, 0, 0),
              thigh_r=(0, 0, 0), calf_r=(-4, 0, 0), foot_r=(0, 0, -6),
              thigh_l=(26, 0, 0), calf_l=(-58, 0, 0), foot_l=(8, 0, 6),
              upperarm_r=(2, 72, 4), upperarm_l=(4, -72, -4))
W_UP = over(W_PASS, pelvis__loc=(0, 0, 0.02), thigh_r=(-8, 0, 0), calf_r=(-4, 0, 0), foot_r=(-8, 0, -6),
            thigh_l=(30, 0, 0), calf_l=(-30, 0, 0), foot_l=(12, 0, 6))
walk = Clip("walk")
for f, p in ((0, W_CONTACT), (4, W_DOWN), (8, W_PASS), (11, W_UP)):
    walk.key(f, p)
for f, p in ((15, W_CONTACT), (19, W_DOWN), (23, W_PASS), (26, W_UP)):
    walk.key(f, swap(p))
walk.close(30)

# Run: 4 m/s, one cycle 20 frames = 2.67 m. Leaning in, arms pumping, a flight phase (no planting there).
R_BASE = over(STAND, spine_01=(-6, 0, 0), spine_02=(-8, 0, 0), spine_03=(-8, 0, 0), neck=(14, 0, 0), head=(2, 0, 0),
              lowerarm_r=(0, 0, 80), lowerarm_l=(0, 0, -80), fingers_r=(0, 70, 0), fingers_l=(0, -70, 0))
R_CONTACT = over(R_BASE, pelvis__loc=(0, 0, -0.04), pelvis=(0, 0, -8), spine_03=(-8, 0, 8),
                 thigh_r=(38, 0, 0), calf_r=(-14, 0, 0), foot_r=(10, 0, -6),
                 thigh_l=(-28, 0, 0), calf_l=(-40, 0, 0), foot_l=(-20, 0, 6),
                 upperarm_r=(-38, 70, 4), upperarm_l=(40, -70, -4))
R_PASS = over(R_BASE, pelvis__loc=(0, 0, -0.02), thigh_r=(-6, 0, 0), calf_r=(-10, 0, 0), foot_r=(-6, 0, -6),
              thigh_l=(50, 0, 0), calf_l=(-100, 0, 0), foot_l=(10, 0, 6),
              upperarm_r=(0, 70, 4), upperarm_l=(0, -70, -4))
R_FLIGHT = over(R_BASE, pelvis__loc=(0, 0, 0.05), thigh_r=(-30, 0, 0), calf_r=(-50, 0, 0), foot_r=(-24, 0, -6),
                thigh_l=(52, 0, 0), calf_l=(-60, 0, 0), foot_l=(12, 0, 6),
                upperarm_r=(30, 70, 4), upperarm_l=(-34, -70, -4))
run = Clip("run")
for f, p in ((0, R_CONTACT), (3, R_PASS), (6, R_FLIGHT)):
    run.key(f, p)
for f, p in ((10, R_CONTACT), (13, R_PASS), (16, R_FLIGHT)):
    run.key(f, swap(p))
run.close(20)

# Hands and feet are placed by FK (rig.reach), not guessed: a ladder in front and a shovel's two grips have to be
# where the hands are, or the pose reads as mime.
def elbow_out(e):
    """Keeps an elbow out beside the body rather than through it."""
    return 0.3 if abs(e.x) < 0.14 and abs(e.y) < 0.14 else 0.0


def arm_to(pose, side, wrist, fist=True):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=elbow_out)
    if fist:
        p[f"fingers_{side}"] = (0, 80 * k, 0)
        p[f"thumb_{side}"] = (0, 30 * k, 0)
    return p


def leg_to(pose, side, ankle):
    return rig.reach(sk, pose, f"thigh_{side}", f"calf_{side}", ankle, elbow_axis=0, bend=-1)


# Climb: a ladder 0.3 m in front, rungs 0.3 m apart; hand over hand, 40 frames per two rungs. The root doesn't rise
# (the sim moves the climber).
C_BASE = over(STAND, spine_01=(-4, 0, 0), spine_02=(-2, 0, 0), spine_03=(2, 0, 0), neck=(4, 0, 0), head=(16, 0, 0),
              pelvis__loc=(0, 0.04, 0))


def climb_pose(high):
    """`high`: the side whose hand is up and whose foot is down."""
    low = "l" if high == "r" else "r"
    kh, kl = (1, -1) if high == "r" else (-1, 1)
    p = dict(C_BASE)
    p = arm_to(p, high, (kh * 0.2, 0.26, 1.9))
    p = arm_to(p, low, (kl * 0.2, 0.26, 1.5))
    p = leg_to(p, low, (kl * 0.12, 0.2, 0.6))
    p = leg_to(p, high, (kh * 0.12, 0.2, 0.2))
    p[f"foot_{low}"] = (10, 0, 0)
    p[f"foot_{high}"] = (0, 0, 0)
    return p


C_A = climb_pose("r")
C_B = climb_pose("l")
C_MID = over(C_BASE, pelvis__loc=(0, 0.04, 0.05))
C_MID = arm_to(C_MID, "r", (0.2, 0.26, 1.7))
C_MID = arm_to(C_MID, "l", (-0.2, 0.26, 1.7))
C_MID = leg_to(C_MID, "r", (0.12, 0.2, 0.4))
C_MID = leg_to(C_MID, "l", (-0.12, 0.2, 0.4))
climb = Clip("climb")
climb.key(0, C_A)
climb.key(10, C_MID)
climb.key(20, C_B)
climb.key(30, C_MID)
climb.close(40)

# Shovel: stoop into the coal at the right, load, turn left and pitch it through the firehole ahead, turn back.
# The right fist holds the shovel (its handle runs through the fist along the bind pose's +Y); the left hand is put
# on the D-grip wherever that ends up. 48 frames.
HANDLE_BACK = Vector((0, -0.62, 0))
WRIST_R = sk["hand_r"].head.copy()


def with_shovel(pose, wrist, direction):
    p = arm_to(pose, "r", wrist)
    d = Vector(direction).normalized()
    R = Vector((0, 1, 0)).rotation_difference(d)
    p["hand_r"] = rig.world_rotation(sk, p, "lowerarm_r").inverted() @ R
    w = rig.pose_points(sk, p, [("hand_r", "head")])[0]
    grip_end = w + R @ (G - WRIST_R) + R @ HANDLE_BACK
    # The left wrist a hand's length short of the D-grip.
    return arm_to(p, "l", grip_end - (grip_end - w).normalized() * 0.05)


S_BODY = over(STAND, pelvis__loc=(0, -0.06, 0), pelvis=(-8, 0, -18), spine_01=(-14, 0, -8), spine_02=(-14, 0, -6),
              spine_03=(-8, 0, -4), neck=(12, 0, 0), head=(8, 0, 0),
              thigh_r=(38, 0, 0), calf_r=(-56, 0, 0), foot_r=(18, 0, -6),
              thigh_l=(22, 0, 0), calf_l=(-38, 0, 0), foot_l=(16, 0, 6))
S_LOAD = with_shovel(S_BODY, (0.3, 0.42, 0.6), (0.35, 0.6, -0.72))
S_DIG = with_shovel(S_BODY, (0.32, 0.5, 0.52), (0.3, 0.75, -0.6))
S_LIFT_BODY = over(S_BODY, pelvis=(-4, 0, -8), spine_01=(-8, 0, -2), spine_02=(-8, 0, -2), spine_03=(-4, 0, 0),
                   thigh_r=(22, 0, 0), calf_r=(-30, 0, 0), thigh_l=(12, 0, 0), calf_l=(-20, 0, 0))
S_LIFT = with_shovel(S_LIFT_BODY, (0.2, 0.38, 0.85), (0.2, 0.95, -0.15))
S_THROW_BODY = over(S_LIFT_BODY, pelvis=(-4, 0, 16), spine_01=(-6, 0, 10), spine_02=(-8, 0, 10), spine_03=(-8, 0, 8),
                    head=(0, 0, 8), thigh_l=(30, 0, 0), calf_l=(-36, 0, 0))
S_THROW = with_shovel(S_THROW_BODY, (-0.02, 0.55, 0.8), (-0.25, 0.9, -0.3))
shovel_clip = Clip("shovel")
shovel_clip.key(0, S_LOAD)
shovel_clip.key(6, S_DIG)
shovel_clip.key(16, S_LIFT)
shovel_clip.key(26, S_THROW, "LINEAR")
shovel_clip.hold(31)
shovel_clip.close(48)

# Crouch: down on the haunches behind cover, forearms on the knees, breathing hard.
CR = over(STAND, pelvis__loc=(0, -0.12, 0), pelvis=(-10, 0, 0), spine_01=(-14, 0, 0), spine_02=(-12, 0, 0),
          spine_03=(-8, 0, 0), neck=(18, 0, 0), head=(4, 0, 0),
          thigh_r=(100, -6, 0), calf_r=(-128, 0, 0), foot_r=(30, 0, -8),
          thigh_l=(96, 6, 0), calf_l=(-124, 0, 0), foot_l=(28, 0, 8),
          clavicle_r=(0, 12, 10), upperarm_r=(40, 66, 10), lowerarm_r=(0, 0, 60), hand_r=(0, 20, 0),
          upperarm_l=(40, -66, -10), lowerarm_l=(0, 0, -60), hand_l=(0, -20, 0))
crouch = Clip("crouch_idle")
crouch.key(0, CR)
crouch.key(18, over(CR, spine_03=(-5, 0, 0), clavicle_r=(0, 8, 10), clavicle_l=(0, -8, -10), head=(8, 0, -10)))
crouch.key(36, over(CR, head=(2, 0, 14)))
crouch.close(54)

# Dead: slumped face down and half on the side, one arm under, legs crooked. A still pose (two identical keys).
DEAD = mirror({
    "root": (-88, 0, 0), "root@loc": (0, -0.95, 0.15), "pelvis": (0, 18, 0), "spine_01": (0, -6, 4),
    "spine_02": (4, -8, 6), "spine_03": (6, -6, 4), "neck": (0, 20, 30), "head": (10, 10, 40),
    "clavicle_r": (0, 0, 0), "upperarm_r": (0, 20, 60), "lowerarm_r": (0, 0, 70), "hand_r": (0, 30, 0),
    "fingers_r": (0, 40, 0),
    "upperarm_l": (70, -40, -10), "lowerarm_l": (0, 0, -30), "hand_l": (0, -20, 0), "fingers_l": (0, -20, 0),
    "thigh_r": (40, -10, 0), "calf_r": (-70, 0, 0), "foot_r": (-40, 0, 0),
    "thigh_l": (6, 10, 0), "calf_l": (-20, 0, 0), "foot_l": (-50, 0, 0),
})
dead = Clip("dead", loop=False)
dead.key(0, DEAD, "CONSTANT")
dead.key(1, DEAD, "CONSTANT")

CLIPS = [idle, walk, run, climb, shovel_clip, crouch, dead]

kit.build()
rig.bake(sk, CLIPS, plant=rig.feet_planter(sk, clips={"idle", "walk", "crouch_idle", "shovel"}))
rig.export(rig.args()[0] if rig.args() else "crew.glb", kit)
