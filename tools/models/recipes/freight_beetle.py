"""THE FREIGHT BEETLE (GDD §21, OUTSIDE; note 366; docs/design/creatures/freight-beetle.md §3 and the director's
reference): tools/blender/freight_beetle.py's pushing beetle with its colour and its fine relief baked into one 2048
atlas (the Look Review: organic, not boxes; the Gannet's and the Ribbit's way, notes 340, 361).

tools/blender/freight_beetle.py stays its source: the rig, the soft body's fused skin, the plates, the shovel and the
legs, the clips. This recipe runs it, unwraps every part into one atlas (the head, the shovel and the eyes given more
of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the plates dented and scratched
    across, their edges chipped; the head, the neck and the belly wrinkled like an old hide; the soft flesh between the
    plates folded; the legs ringed with creases;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): the bands worn and
    painted like old freight (buff and ochre bands, an orange stripe run down them, grey patches, scorch, the paint
    chipped to dark iron at the edges and scratched across), the hood a scuffed grey-brown; the shovel dark iron, its
    edge scraped bright; the hide grey-brown; the soft flesh between the plates and under their edges red and wet; the
    legs grey-beige, darker at the joints and grimed toward the feet; the claws black; the eyes glossy green-black.

    tools/models/build.sh freight_beetle   (or with no Blender: pip install "bpy<5", then python tools/models/recipes/freight_beetle.py)
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402,F401  (first: as a Python module, Blender's bmesh is only importable after it)
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import fine, smooth01  # noqa: E402
from texels import Texels  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("freight_beetle.py")
print("[dt] freight_beetle parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

BANDS = g["BANDS"]
EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
EDGE_Y = float(g["EDGE_Y"])


def suffix(name):
    return name.split(".")[1]


DRESS = {"beetle_shell": 1, "beetle_hood": 1, "beetle_blade": 0, "beetle_hide": 2, "beetle_flesh": 2, "beetle_leg": 1,
         "beetle_claw": 0, "beetle_eye": 1}


def dress(m):
    return make.flat("beetle_high", (0.3, 0.3, 0.3), rough=0.6), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def scratches_of(p):
    """Scrapes dragged across the plates (stretched along x: what it's pushed and been pushed against)."""
    q = p * np.array([0.25, 1.0, 1.0], np.float32)
    return smooth01(0.95, 0.99, ridged(q, 1201, 60.0)) * smooth01(0.0, 0.5, cook.noise_np(p, 1202, 6.0))


def plate_shape(p, n):
    dents = 0.0025 * cook.noise_np(p, 1203, 5.0) + 0.0012 * cook.noise_np(p, 1204, 14.0)
    return dents - 0.0012 * scratches_of(p) + fine(p, 0.00015, 300, 1205)


def hide_shape(p, n):
    # An old hide's wrinkles: folds across, and finer crossing them.
    w = 0.0022 * np.sin(p[:, 1] * 55 + 3 * cook.noise_np(p, 1211, 4.0)) + 0.0012 * ridged(p * np.array([1.0, 0.4, 1.0], np.float32), 1212, 30.0) ** 2
    return w + fine(p, 0.0003, 250, 1213)


def flesh_shape(p, n):
    return 0.0018 * np.sin(p[:, 2] * 70 + 2 * cook.noise_np(p, 1221, 5.0)) + fine(p, 0.0002, 300, 1222)


def leg_shape(p, n):
    rings = 0.0018 * np.sin((p[:, 1] * 0.6 + p[:, 2]) * 90 + 2 * cook.noise_np(p, 1231, 6.0))
    return rings + 0.001 * cook.noise_np(p, 1232, 12.0) + fine(p, 0.0002, 280, 1233)


FLAT = lambda p, n: 0 * p[:, 0]  # noqa: E731
SHAPE = {"wood_crate.beetle_shell": plate_shape, "wood_crate.beetle_hood": plate_shape, "iron_plate.beetle_blade": FLAT,
         "skin.beetle_hide": hide_shape, "flesh.beetle_flesh": flesh_shape, "skin.beetle_leg": leg_shape,
         "iron_plate.beetle_claw": FLAT, "glass_dirty.beetle_eye": FLAT}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] freight_beetle highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("beetle_blade", "beetle_eye", "beetle_hood") else 0


atlas = overbake.Atlas("freight_beetle", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.4})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.015, 0.05), "shell": (0.01, 0.03), "blade": (0.008, 0.02), "legs": (0.01, 0.03), "fine": (0.006, 0.02)},
           samples=16, height=1.5)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] freight_beetle atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint
x, y, z = P[..., 0], P[..., 1], P[..., 2]
up = N[..., 2]

base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.7, np.float32)

# The shell: each band its own old paint, worn and scuffed; an orange stripe run down the bands; grey patches; scorch.
shell = is_("beetle_shell")
BUFF, OCHRE, GREYBUFF = (0.42, 0.32, 0.17), (0.42, 0.22, 0.05), (0.24, 0.22, 0.17)
colours = [GREYBUFF, OCHRE, BUFF, OCHRE, BUFF]
band = np.full((S, S), -1, np.int32)
for i, (y0, y1) in enumerate(BANDS):
    band = np.where(shell & (y >= y0 - 0.005) & (y <= y1 + 0.02), i, band)
for i, c in enumerate(colours):
    base[shell & (band == i)] = c
base[shell & (band < 0)] = BUFF
base *= (0.85 + 0.25 * noise(3701, 2.5))[..., None]
stripe = shell * smooth01(0.06, 0.09, x) * smooth01(0.34, 0.3, x) * smooth01(-0.95, -0.85, y) * smooth01(0.2, 0.1, y)
base = paint(base, (0.5, 0.15, 0.025), stripe * (0.75 + 0.25 * noise(3702, 9.0)))
# Grey patches (old repairs, primer) and the paint flaking off them.
patch = smooth01(0.35, 0.5, noise(3703, 2.2)) * shell
base = paint(base, (0.21, 0.21, 0.2), patch * 0.85)
# Scorch: a burn across the rear top, black to brown at its edge.
burn = smooth01(0.1, 0.55, noise(3704, 1.6) + 0.6 - np.linalg.norm(np.stack([x + 0.25, y + 0.65], -1) / np.array([0.5, 0.45], np.float32), axis=-1))
base = paint(base, (0.12, 0.07, 0.03), burn * shell * 0.7)
base = paint(base, (0.015, 0.013, 0.012), smooth01(0.3, 0.8, burn) * shell * 0.9)
# Worn to the iron at the edges (each band's back edge and its bottom rim) and in chips and scrapes everywhere.
edge = np.zeros((S, S), np.float32)
for i, (y0, y1) in enumerate(BANDS):
    edge = np.maximum(edge, (band == i) * smooth01(y1 - 0.06, y1 + 0.01, y))
low = smooth01(0.52, 0.42, z)
chips = smooth01(0.6, 0.8, noise(3705, 18.0) * 0.5 + 0.5 * (edge + low))
iron = (shell | is_("beetle_hood")) * np.clip(chips + 0.8 * field(scratches_of), 0, 1)
base = paint(base, (0.06, 0.05, 0.045), iron * 0.85)
base = paint(base, (0.18, 0.08, 0.035), iron * smooth01(0.3, 0.7, noise(3706, 25.0)) * 0.5)
rough[shell] = 0.62
grime = smooth01(0.1, 0.7, field(lambda q: cook.noise_np(q * np.array([3.0, 3.0, 0.6], np.float32), 3715, 4.0)) * 0.5 + 0.5)
base = paint(base, (0.09, 0.075, 0.06), (shell | is_("beetle_hood")) * grime * 0.45)
# The hood: a scuffed grey-brown, darker under its rim.
hood = is_("beetle_hood")
base[hood] = (np.array((0.16, 0.145, 0.12), np.float32) * (0.8 + 0.35 * noise(3707, 4.0))[hood][:, None])
base = paint(base, (0.06, 0.05, 0.045), hood * iron * 0.8)
# The plates' undersides (facing in toward the body): bare, dark, sooted.
HOOD_C = np.array(tuple(g["HOOD_C"]), np.float32)
DOME_C = np.array(tuple(g["DOME_C"]), np.float32)
inner = (hood & (np.sum(N * (P - HOOD_C), axis=-1) < 0)) | (shell & (np.sum(N * (P - DOME_C), axis=-1) < 0))
base[inner] = np.array((0.035, 0.03, 0.026), np.float32)
# The shovel: dark oily iron, its edge scraped bright, scratches running back from it.
blade = is_("beetle_blade")
base[blade] = (0.05, 0.047, 0.044)
bright = blade * smooth01(EDGE_Y - 0.07, EDGE_Y - 0.02, y + 0.08 * x * x / 0.13)
base = paint(base, (0.5, 0.49, 0.46), bright * (0.75 + 0.25 * noise(3708, 40.0)))
base = paint(base, (0.32, 0.31, 0.29), blade * field(scratches_of) * 0.6)
base = paint(base, (0.12, 0.05, 0.02), blade * (1 - bright) * smooth01(0.3, 0.7, noise(3709, 8.0)) * 0.6)
rough[blade] = 0.45
rough = np.where(bright > 0.5, 0.18, rough)
# The hide: grey-brown, wrinkled, the creases darker; paler underneath.
hide = is_("beetle_hide")
base[hide] = (np.array((0.17, 0.155, 0.13), np.float32) * (0.85 + 0.3 * noise(3710, 6.0))[hide][:, None])
base = paint(base, (0.24, 0.22, 0.19), hide * smooth01(0.0, -0.6, up) * 0.5)
rough[hide] = 0.6
# The soft flesh between the plates: red and wet, veined darker, paler where it's stretched.
fl = is_("beetle_flesh")
base[fl] = (np.array((0.32, 0.055, 0.04), np.float32) * (0.8 + 0.4 * noise(3711, 7.0))[fl][:, None])
vein = smooth01(0.9, 0.97, ridged(P.reshape(-1, 3), 1241, 20.0).reshape(S, S))
base = paint(base, (0.12, 0.015, 0.02), fl * vein * 0.7)
base = paint(base, (0.42, 0.16, 0.12), fl * smooth01(0.4, 0.8, noise(3712, 12.0)) * 0.3)
rough[fl] = 0.32
# The legs: grey-beige plate, darker in the joints' creases, grimed to the feet; the forelimbs' outer plates paler.
leg = is_("beetle_leg")
base[leg] = (np.array((0.15, 0.135, 0.11), np.float32) * (0.85 + 0.3 * noise(3713, 5.0))[leg][:, None])
base = paint(base, (0.1, 0.085, 0.065), leg * smooth01(0.35, 0.05, z) * 0.75)
base = paint(base, (0.22, 0.2, 0.165), leg * smooth01(0.3, 0.8, up) * smooth01(0.6, 0.9, np.abs(N[..., 0])) * 0.3)
rough[leg] = 0.6
claw = is_("beetle_claw")
base[claw] = (0.025, 0.022, 0.02)
rough[claw] = 0.3
# The eyes: glossy green-black, a dull green depth in them.
eye = is_("beetle_eye")
base[eye] = (0.012, 0.02, 0.014)
for e in EYES:
    r = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.03, 0.07, 0.04), eye * smooth01(0.06, 0.02, r) * smooth01(0.2, 0.9, N[..., 1]) * 0.6)
rough[eye] = 0.03
# Mud and dust thrown up over everything low down.
dust = smooth01(0.45, 0.1, z) * smooth01(0.0, 0.6, noise(3714, 3.0) + 0.3)
base = paint(base, (0.13, 0.11, 0.085), dust * ~eye * 0.6)
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye | blade, ao ** 0.4, ao)
base = base * (0.32 + 0.68 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.4
base = base * (1 - k)[..., None] + np.array((0.02, 0.017, 0.015), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("freight_beetle", "the Freight Beetle, modelled in tools/blender/freight_beetle.py, its colour painted here"),
             rough=rough, lod=0.4)
