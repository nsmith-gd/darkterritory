"""CREW: a rail worker (GDD §29 "practical, rail-working, soot-covered, bundled against cold, slightly anonymous"), in the
steampunk, post-apocalyptic kit art direction asked for: masked (nothing to lip-sync, no eyes to animate), but with some
fun to it, and each player in their own colour.

Heavy knee-length oilskin coat, patched; gloves, boots for wet metal and ballast, a belt with a lantern hook, a satchel on
a strap, and the chest lamp the greybox had: the thing you find each other by in the dark (GreyboxScene.DrawCrewmate).
The head is the gas mask (the director's pick of the headgear concepts): a black rubber hood with brass eyepieces lit
dimly amber, a filter drum at the chin and a corrugated hose to the chest, under a quilted flying cap in the player's
colour with a fleece rim and earflaps, and a welder's visor on pivots at the temples. SK_Human, 1.8 m: at forty metres
in the fog a crewmate is a lamp, two dim eyes and a coloured cap.

Variants (CreatureArt `variant % 4`): 0 visor up, 1 visor down, 2 up + scarf, 3 down + scarf.
DT_CREW=bare builds the figure bare-headed instead, the cap or the steel helmet for the stacks (the husk's source).
DT_CREW=dave builds Dave, the wandering painter, bare-headed in his shirt sleeves and a waistcoat (tools/blender/davebody.py).
Clips (30 fps): idle, walk (1.4 m/s, in place), run (4 m/s), climb, shovel (with the shovel prop), crouch_idle, dead.

    blender -b --python tools/blender/crew.py -- content/art/models/crew.glb

This is the crew's game mesh, rig and clips; the body under the mask is modelled by crewbody.py (blocked in solids,
voxel-remeshed, retopologised). The game's crew.glb is tools/models/recipes/crew.py's, which runs this
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

# The crew's own heads are the gas mask (ART DIRECTION, ARCHITECTURE §8 note 58; the director's pick of the headgear
# concepts, tools/models/concepts/crew_headgear.py "welder"): masked, so nothing to lip-sync and no eyes to animate, and
# with some fun to it. DT_CREW=bare builds the bare-headed figure in a cap or a helmet instead, for what the crew become
# (tools/models/recipes/husk.py).
HELM = os.environ.get("DT_CREW", "helm") not in ("bare", "dave")
# Dave (ARCHITECTURE §8 note 491): the bare figure in his own clothes, no chest lamp and no scarf; his body is
# davebody's (a shirt over a belly, sleeves rolled, bare forearms, sandals), his five waistcoats its variants.
DAVE = os.environ.get("DT_CREW") == "dave"
MASK_C = Vector((0, 0.014, 1.674))     # the gas hood, round the head
CAP_C = Vector((0, 0.004, 1.698))      # the flying cap over it
PIVOT = Vector((0, 0.012, 1.702))      # the welder's visor turns about its own centre, on its temple pivots
VISOR_UP = 40                          # degrees it's raised (any further and its edges meet over the crown in a crest)
# The hose from the chin's filter drum to its coupling on the chest, out in front of the scarf.
HOSE = [Vector((0, 0.206, 1.562)), Vector((0, 0.228, 1.53)), Vector((0.035, 0.225, 1.44)), Vector((0.06, 0.19, 1.36))]
PAINT = Mat("helm.paint", hexc("#8a3a2a"), shine=0.2)          # the cap's dyed leather: the player's colour (CreatureArt tints it)
BRASS = Mat("helm.brass", hexc("#a08040"), shine=0.6)
PORT = Mat("helm.glass", hexc("#5a3414"), emissive=1.0)         # the lenses, lit dimly from inside by the wearer's own lamp
RUBBER = Mat("helm.rubber", hexc("#18161a"), shine=0.25)
TIN = Mat("helm.tin", hexc("#4e5040"), shine=0.35)
VISOR = Mat("helm.iron", hexc("#2a2826"), shine=0.4)
SMOKED = Mat("helm.smoked", hexc("#0c0d0e"), shine=0.8)          # the visor's dark glass: not lit, a slot of nothing
FLEECE = Mat("helm.wool", hexc("#8a8070"))


def ring_loop(centre, axis, r, thick, mat, bones, n=16, part=None, stretch=(1.0, 1.0)):
    """A torus-ish band (a rim, a fleece edge): a closed tube round `axis` through `centre`, `stretch`ed across it."""
    c, ax = Vector(centre), Vector(axis).normalized()
    u = ax.orthogonal().normalized()
    v = ax.cross(u)
    if abs(u.x) < abs(v.x):
        u, v = v, u
    pts = [c + u * (math.cos(2 * math.pi * k / n) * r * stretch[0]) + v * (math.sin(2 * math.pi * k / n) * r * stretch[1])
           for k in range(n)]
    (part or body).tube(pts, [thick] * n, 5, mat, bones, ref=[tuple(ax)] * n, loop=True)


def facing(axis):
    """The rotation taking a blob's +Z onto `axis` (a lens, a disc facing out)."""
    return Vector(axis).normalized().to_track_quat("Z", "Y").to_matrix().to_4x4()


def shell(part, centre, radii, around, heights, mat, bones, thick=0.005, xform=None, steps=(10, 5)):
    """A patch of an ellipsoid's front with a thickness (the visor, its glass): azimuths `around` (radians from +Y, x to
    the right), heights `heights` (z, absolute), lofted across as closed rings so it reads from both sides."""
    c = Vector(centre)
    rx, ry, rz = radii
    rings = []
    for i in range(steps[0] + 1):
        a = around[0] + (around[1] - around[0]) * i / steps[0]
        outer, inner = [], []
        for k in range(steps[1] + 1):
            z = heights[0] + (heights[1] - heights[0]) * k / steps[1]
            h = max(0.0, 1 - ((z - c.z) / rz) ** 2) ** 0.5
            for lst, grow in ((outer, 0.0), (inner, -thick)):
                p = c + Vector((math.sin(a) * (rx * h + grow), math.cos(a) * (ry * h + grow), z - c.z))
                lst.append(xform @ p if xform is not None else p)
        rings.append(outer + inner[::-1])
    part.loft(rings, mat, bones, cap0=True, cap1=True)


def hood_w(p):
    # The hood rides the head; its skirt, down in the coat's collar, the neck.
    return {"head": 1.0} if p.z > 1.6 else ({"neck": 1.0} if p.z < 1.56 else {"head": 0.5, "neck": 0.5})


def gas_mask():
    """The gas hood, the flying cap and what hangs off them (the visor is a variant: welder_visor). A black rubber hood
    over the whole head and down into the collar, two brass-ringed eyepieces with their lenses lit dimly amber (the one
    thing you see of a face), a filter drum at the chin and two small ones at the cheeks, and a corrugated hose from the
    drum down to a coupling on the chest. Over the hood, a quilted flying cap in the player's colour, fleece at its rim,
    earflaps buckled under the chin. At forty metres in the fog a crewmate is a lamp, two dim eyes and a coloured cap."""
    body.blob(MASK_C, (0.106, 0.138, 0.158), 16, 10, RUBBER, hood_w)
    for sx in (-1, 1):
        e = Vector((sx * 0.042, 0.138, 1.702))
        ax = Vector((sx * 0.35, 1, 0.05)).normalized()
        body.tube([e - ax * 0.012, e + ax * 0.012, e + ax * 0.011], [0.03, 0.03, 0.024], 12, BRASS, "head", ref=(0, 0, 1))
        body.blob(e + ax * 0.007, (0.025, 0.025, 0.004), 10, 3, PORT, "head", rot=facing(ax))
    # The chin's filter drum, its brass grille at the front; the cheek filters angled back.
    f0 = Vector((0, 0.141, 1.614))
    f1 = f0 + Vector((0, 0.06, -0.03))
    body.tube([f0, f1], [0.038, 0.038], 14, TIN, "head", ref=(1, 0, 0))
    body.blob(f1, (0.034, 0.034, 0.004), 12, 2, BRASS, "head", rot=facing(f1 - f0))
    ring_loop(f1, f1 - f0, 0.035, 0.005, BRASS, "head", n=14)
    for sx in (-1, 1):
        c0 = Vector((sx * 0.075, 0.095, 1.612))
        c1 = c0 + Vector((sx * 0.035, 0.0, -0.02))
        body.tube([c0, c1], [0.02, 0.02], 10, TIN, "head", ref=(0, 1, 0), cap1=True)
        ring_loop(c1, c1 - c0, 0.018, 0.004, BRASS, "head", n=10)

    # The hose, down from the drum to its coupling on the chest.
    def hose_w(p):
        return {"head": 1.0} if p.z > 1.6 else ({"neck": 1.0} if p.z > 1.52 else TORSO(p))
    body.tube(HOSE, [0.018] * len(HOSE), 10, RUBBER, hose_w, ref=(1, 0, 0))
    body.tube([Vector((0.06, 0.19, 1.365)), Vector((0.062, 0.186, 1.34))], [0.024, 0.022], 10, BRASS, TORSO, ref=(1, 0, 0),
              cap1=True)

    # The cap: an ellipsoid over the hood cut on a slant (low at the nape, up off the eyepieces at the brow), its fleece
    # rim, the earflaps, the chinstrap and its buckles.
    tilt = rig.Matrix.Rotation(math.radians(11), 4, "X")
    body.blob(CAP_C, (0.114, 0.144, 0.15), 16, 6, PAINT, "head", rot=tilt, z0=0.1)
    rim = CAP_C + tilt.to_3x3() @ Vector((0, 0, 0.015))
    ring_loop(rim, tilt.to_3x3() @ Vector((0, 0, 1)), 0.117, 0.014, FLEECE, "head", n=20, stretch=(1.0, 1.25))
    for sx in (-1, 1):
        body.blob((sx * 0.108, 0.008, 1.655), (0.013, 0.048, 0.06), 8, 4, PAINT, "head")
        body.tube([(sx * 0.106, 0.03, 1.603), (sx * 0.085, 0.075, 1.585), (sx * 0.06, 0.112, 1.572)], [(0.008, 0.003)] * 3, 4,
                  STRAP, "head", ref=(1, 0, 0))
        body.box((sx * 0.108, 0.03, 1.606), (0.005, 0.012, 0.01), BRASS, "head")
        # The visor's pivots, on the cap either side at the temples (both variants: the visor's there, up or down).
        body.tube([(sx * 0.126, PIVOT.y, PIVOT.z), (sx * 0.142, PIVOT.y, PIVOT.z)], [0.012, 0.012], 10, BRASS, "head",
                  ref=(0, 0, 1), cap1=True)


def welder_visor():
    """The welder's visor, the variants' tell (you can tell two heads apart on a roof in the dark): a riveted steel shield
    on the temple pivots, a slot of dark glass across it. Flipped up over the cap (variants 0, 2), or down over the face
    (1, 3): then there are no eyes at all, only the slot."""
    up = rig.Matrix.Translation(PIVOT) @ rig.Matrix.Rotation(math.radians(VISOR_UP), 4, "X") @ rig.Matrix.Translation(-PIVOT)
    for name, variants, xform in (("visor_up", (0, 2), up), ("visor_down", (1, 3), rig.Matrix.Identity(4))):
        part = kit.part(name, variants=variants)
        shell(part, PIVOT, (0.13, 0.158, 0.16), (-0.93, 0.93), (1.635, 1.8), VISOR, "head", xform=xform)
        shell(part, PIVOT, (0.132, 0.16, 0.16), (-0.38, 0.38), (1.69, 1.716), SMOKED, "head", thick=0.003, xform=xform,
              steps=(6, 1))


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
    gas_mask()


# --- the body --------------------------------------------------------------------------------------------------
# Modelled, retopologised and weighted by tools/blender/crewbody.py after the kit is built (it needs the kit's
# materials): the coat, the sleeves, the gloves, the belt, the bandolier and the satchel, the trousers and the boots.

# The chest lamp, clipped to the strap: iron box, amber lens. It's emissive: the one warm point on a crewmate.
if not DAVE:
    body.box((-0.04, 0.178, 1.315), (0.038, 0.026, 0.048), IRON, "spine_02")
    body.box((-0.04, 0.203, 1.315), (0.03, 0.004, 0.036), LAMP, "spine_02",
             uv=lambda ax, sg, l: in_cell("lantern_glass", 0.5 + l.x / 0.07, 0.55 - l.z / 0.08))
    body.box((-0.04, 0.178, 1.372), (0.02, 0.018, 0.01), IRON, "spine_02")

# --- hats and scarf (variants) -------------------------------------------------------------------------------
if DAVE:
    pass    # His hats are props on the head bone (tools/models/recipes/dave_kit.py); his variants are his waistcoats.
elif not HELM:
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
    welder_visor()

if not DAVE:
    scarf = kit.part("scarf", variants=(2, 3))
    # Wool wound round the coat's stood-up collar (lower at the front, over the collarbones), the end hanging down the
    # left of the chest, clear of the lamp and the hose.
    ring = [(math.sin(a) * 0.158, 0.012 + math.cos(a) * 0.148, 1.558 - 0.03 * math.cos(a))
            for a in [2 * math.pi * k / 16 for k in range(16)]]
    scarf.tube(ring, [(0.028, 0.034)] * 16, 8, SCARF, along("z", [(1.5, "spine_03"), (1.6, "neck")]),
               ref=[Vector((0, 0, 1))] * 16, loop=True, uv=lambda i, j, uf, vf, p: in_cell("scarf", vf, uf))
    scarf.tube([(-0.098, 0.168, 1.535), (-0.112, 0.19, 1.49), (-0.122, 0.198, 1.44), (-0.128, 0.196, 1.395)],
               [(0.034, 0.017), (0.036, 0.016), (0.035, 0.014), (0.03, 0.01)], 8, SCARF, TORSO, ref=(0, 1, 0), cap1=True,
               uv=lambda i, j, uf, vf, p: in_cell("scarf", uf, vf))

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
if DAVE:
    import davebody  # noqa: E402
    davebody.build(globals())
else:
    import crewbody  # noqa: E402
    crewbody.build(globals())
rig.bake(sk, CLIPS, plant=rig.feet_planter(sk, clips={"idle", "walk", "crouch_idle", "shovel"}))
rig.export(rig.args()[0] if rig.args() else "crew.glb", kit)
