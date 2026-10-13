"""THE PICKERS (GDD §21, OUTSIDE; note 574; docs/design/creatures/pickers.md §3): tools/blender/picker.py's knee-high
drain scavenger with its colour and its fine relief baked into one 1024 atlas (the Look Review: organic, not primitives;
the Mourners' and the Ribbit's way, notes 361, 362).

tools/blender/picker.py stays its source: the rig, the fused skin and the jaw and the parts over them, the clips. This
recipe runs it, unwraps every part into one atlas (the hands, the ears and the mouth given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the hide mangy and tight, the
    ribs standing in the starved flanks, the spine's knuckles; the knuckles of the long fingers wrinkled at every joint;
    the ears' thin skin veined;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): grey-brown and patchy,
    bare grey skin showing through in mangy patches, the ribs' shadows; coal-black from the drains to the elbows and the
    knees, the black ragged where it's dried; the big hands pale, grimed in the creases and black under the nails; the
    ears' insides thin and pinkish-grey, veined; the mouth's insides dark wet red, the needle teeth yellowed, brown at
    the roots; the eye dimples sooted.
Dry and dusty (rough 0.75-0.9); the mouth wet.

    tools/models/build.sh picker        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/picker.py)
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
from overbake import bell, fine, smooth01  # noqa: E402
from texels import Texels  # noqa: E402

S = 1024  # the roster's small-creature atlas (the Mourners'): every renderer loads every atlas (note 367)
kit, g, arm, parts = overbake.hold("picker.py")
print("[dt] picker parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(tuple(g["HC"]), np.float32)
JC = np.array(tuple(g["JC"]), np.float32)
TEETH_AT = np.array([tuple(p) for p in g["TEETH_AT"]], np.float32)
TIPS = np.array([tuple(p) for p in g["TIPS"]], np.float32)


def suffix(name):
    return name.split(".")[1]


DRESS = {"picker": 3, "picker_hand": 3, "picker_ear": 2, "picker_mouth": 2, "picker_tooth": 0, "picker_nail": 0}


def dress(m):
    return make.flat("picker_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def band_of(p):
    """The starved flanks of the narrow cage, where the ribs show."""
    band = smooth01(0.31, 0.35, p[:, 2]) * smooth01(0.46, 0.42, p[:, 2]) * smooth01(-0.04, 0.02, p[:, 1]) * smooth01(0.2, 0.15, p[:, 1])
    return band * smooth01(0.035, 0.06, np.abs(p[:, 0]))


def ribs_of(p):
    """The ribs standing in the flanks."""
    return band_of(p) * (0.5 + 0.5 * np.sin((p[:, 1] + 0.25 * p[:, 2]) * 150))


def mange_of(p):
    """Bare patches where the thin fur's gone."""
    return smooth01(0.25, 0.6, cook.noise_np(p, 5701, 7.0))


def skin_shape(p, n):
    # Tight over the ribs; a short dry pelt over the rest, matted, gone in patches; wrinkled where the joints fold.
    pelt = 0.0005 * cook.noise_np(p, 5702, 160.0) * (1 - mange_of(p))
    folds = 0.0007 * np.sin(p[:, 2] * 520 + 3 * cook.noise_np(p, 5703, 20.0)) * smooth01(0.24, 0.12, p[:, 2])
    return 0.0018 * ribs_of(p) + pelt + folds + fine(p, 0.0002, 520, 5704)


def hand_shape(p, n):
    # Every knuckle of the long fingers creased across; the palm's lines.
    return 0.0005 * np.sin(np.linalg.norm(p - p.mean(0), axis=1) * 900) - 0.0006 * ridged(p, 5711, 60.0) ** 6 + fine(p, 0.00015, 700, 5712)


def ear_shape(p, n):
    # Veins branching in the thin skin.
    return 0.0006 * smooth01(0.9, 0.97, ridged(p, 5721, 45.0)) + fine(p, 0.0001, 800, 5722)


def flat(p, n):
    return 0 * p[:, 0]


SHAPE = {"skin.picker_hand": hand_shape, "skin.picker_ear": ear_shape, "skin.picker_mouth": lambda p, n: fine(p, 0.0002, 600, 5731),
         "skin.picker_tooth": flat, "skin.picker_nail": flat, "skin.picker": skin_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] picker highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("picker_hand", "picker_ear", "picker_mouth", "picker_tooth", "picker_nail") else 0


atlas = overbake.Atlas("picker", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.005, 0.02), "jaw": (0.004, 0.012), "fine": (0.003, 0.01)}, samples=16, height=0.6)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] picker atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
hide = is_("picker")
hands = is_("picker_hand")
# Grey-brown and patchy: a dun pelt, darker down the spine, the mangy patches bare grey skin.
base = np.broadcast_to(np.array((0.15, 0.125, 0.1), np.float32), (S, S, 3)).copy()
base *= (0.85 + 0.25 * noise(5741, 4.0))[..., None]
base = paint(base, (0.25, 0.22, 0.18), smooth01(0.1, 0.65, noise(5742, 6.0)) * 0.55 * hide)
base = paint(base, (0.08, 0.065, 0.055), smooth01(0.2, 0.75, noise(5743, 9.0)) * 0.45 * hide)
base = paint(base, (0.07, 0.06, 0.05), smooth01(0.3, 0.9, up) * smooth01(0.05, 0.0, np.abs(P[..., 0])) * 0.5 * hide)
mange = field(mange_of)
base = paint(base, (0.3, 0.27, 0.25), mange * 0.6 * hide)
# The ribs' shadows down the flanks; the belly paler where it's tucked.
between = field(band_of) - field(ribs_of)
base = paint(base, (0.06, 0.05, 0.045), between * 0.55 * hide)
base = paint(base, (0.27, 0.23, 0.2), smooth01(-0.2, -0.7, up) * smooth01(0.33, 0.28, P[..., 2]) * smooth01(0.24, 0.3, P[..., 2]) * 0.45 * hide)
# Coal-black from the drains to the elbows and the knees: a ragged line, the black dried grey at its edge and splashed up.
edge = 0.2 + 0.035 * noise(5744, 14.0)
coal = smooth01(edge + 0.02, edge - 0.02, P[..., 2])
splash = smooth01(0.55, 0.85, noise(5745, 30.0)) * smooth01(edge + 0.09, edge, P[..., 2])
base = paint(base, (0.018, 0.016, 0.015), np.maximum(coal, splash * 0.8) * hide)
base = paint(base, (0.12, 0.115, 0.11), smooth01(edge + 0.035, edge + 0.01, P[..., 2]) * smooth01(edge - 0.01, edge + 0.01, P[..., 2]) * 0.5 * hide)
# The big hands pale, a dead grey-white, grimed in the creases and sooty at the fingertips and the backs of the knuckles.
base = paint(base, (0.42, 0.39, 0.35), hands * (0.9 + 0.1 * noise(5746, 20.0)))
base = paint(base, (0.3, 0.26, 0.23), hands * smooth01(0.2, 0.8, noise(5747, 18.0)) * 0.4)
tip = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TIPS], 0), 0)
base = paint(base, (0.05, 0.045, 0.04), smooth01(0.03, 0.008, tip) * hands * 0.75)
base = paint(base, (0.08, 0.07, 0.06), smooth01(0.3, 0.8, up) * hands * 0.35)
# The ears: the backs the pelt's, the insides thin and pinkish-grey, veined red.
ears = is_("picker_ear")
opens = np.where(P[..., 0:1] > 0, np.array(tuple(g["EAR_OPEN"]["r"]), np.float32), np.array(tuple(g["EAR_OPEN"]["l"]), np.float32))
inside = smooth01(-0.1, 0.4, (N * opens).sum(-1))
base = paint(base, (0.3, 0.22, 0.21), ears * (0.25 + 0.6 * inside))
vein = field(lambda q: smooth01(0.9, 0.97, ridged(q, 5721, 45.0)))
base = paint(base, (0.2, 0.07, 0.07), vein * ears * 0.6)
# The mouth's insides: dark wet red, the gums; the needle teeth yellowed, brown at their roots.
mouth = is_("picker_mouth")
base = paint(base, (0.12, 0.025, 0.025), mouth * (0.85 + 0.3 * noise(5748, 40.0)))
teeth = is_("picker_tooth")
root = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TEETH_AT], 0), 0)
base[teeth] = np.array((0.55, 0.5, 0.38), np.float32)
base = paint(base, (0.2, 0.13, 0.08), smooth01(0.005, 0.001, root) * teeth)
nail = is_("picker_nail")
base[nail] = np.array((0.02, 0.018, 0.016), np.float32)
# The lips dark, the dimples where eyes would be sooted.
lips = field(lambda q: smooth01(0.012, 0.004, np.abs(q[:, 2] - 0.358)) * smooth01(0.3, 0.33, q[:, 1]))
base = paint(base, (0.06, 0.035, 0.03), lips * hide * 0.6)
for sx in (1, -1):
    d = np.linalg.norm(P - np.array((sx * 0.03, 0.395, 0.405), np.float32), axis=-1)
    base = paint(base, (0.03, 0.025, 0.022), smooth01(0.016, 0.006, d) * hide * 0.8)
base = np.clip(base, 0, 1)

rough = np.full((S, S), 0.82, np.float32) + 0.06 * noise(5749, 10.0)
rough[mouth] = 0.2
rough[teeth] = 0.35
rough[nail] = 0.45
rough = np.where(hands, 0.7, rough)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.02, 0.018, 0.016), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("picker", "the Pickers, modelled in tools/blender/picker.py, their colour painted here"),
             rough=rough, lod=0.4)
