"""THE LODGER (GDD §21, OUTSIDE; note 583; docs/design/creatures/lodger.md): tools/blender/lodger.py's long starved thing with
its colour and its fine relief baked into one 1024 atlas (the Look Review: organic, not primitives; the Mourners' way,
note 362).

tools/blender/lodger.py stays its source: the rig, the fused skin and the jaw and the parts over them, the clips. This
recipe runs it, unwraps every part into one atlas (the mouth, the teeth and the eyes given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the skin stretched so tight over
    the bones that every rib, the breastbone, the hip bones and the spine's knuckles stand in it; creased at every joint;
    the jaw's corners and the throat in long stretched folds;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): ash-grey and bloodless,
    mottled darker where it's lain in the dust of its corner, bruised violet at the joints and round the sunk eyes, the
    hands and the feet grimed black; the mouth's insides black-red and wet, the teeth long and brown, the eyes small and
    black and wet.
Dry (rough 0.75-0.85); the mouth and the eyes wet.

    tools/models/build.sh lodger        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/lodger.py)
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

S = 1024  # a character's atlas at the roster's 1024 (the 2048 tier is full: note 367)
kit, g, arm, parts = overbake.hold("lodger.py")
print("[dt] lodger parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
TEETH_AT = np.array([tuple(p) for p in g["TEETH_AT"]], np.float32)
TIPS = np.array([tuple(p) for p in g["TIPS"]], np.float32)
MOUTH_Z = float(g["MOUTH_Z"])


def suffix(name):
    return name.split(".")[1]


DRESS = {"lodger": 3, "lodger_mouth": 2, "lodger_tooth": 0, "lodger_nail": 0, "lodger_eye": 1}


def dress(m):
    return make.flat("lodger_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def ribs_of(p):
    """The ribs standing round the narrow cage, front and sides, under the stretched skin."""
    band = smooth01(1.32, 1.4, p[:, 2]) * smooth01(1.68, 1.6, p[:, 2]) * smooth01(0.02, 0.05, np.abs(p[:, 0]) + 0.5 * np.maximum(p[:, 1], 0))
    return band * (0.5 + 0.5 * np.sin((p[:, 2] + 0.08 * np.abs(p[:, 0])) * 110))


def skin_shape(p, n):
    folds = 0.0007 * np.sin(p[:, 2] * 300 + 4 * cook.noise_np(p, 5831, 12.0)) * smooth01(0.25, 0.4, ridged(p, 5832, 6.0))
    return 0.0024 * ribs_of(p) + folds - 0.0006 * ridged(p, 5833, 40.0) ** 8 + fine(p, 0.00025, 480, 5834)


def flat(p, n):
    return 0 * p[:, 0]


SHAPE = {"skin.lodger_mouth": lambda p, n: fine(p, 0.0003, 500, 5841), "skin.lodger_tooth": flat, "skin.lodger_nail": flat,
         "glass_dirty.lodger_eye": flat, "skin.lodger": skin_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] lodger highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("lodger_mouth", "lodger_tooth", "lodger_eye") else 0


atlas = overbake.Atlas("lodger", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.6})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "jaw": (0.006, 0.02), "fine": (0.004, 0.012)}, samples=16, height=2.2)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] lodger atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
skin = is_("lodger")
# Ash-grey and bloodless, mottled; a grey dust on whatever faces up; darker in the creases.
base = np.broadcast_to(np.array((0.3, 0.295, 0.285), np.float32), (S, S, 3)).copy()
base *= (0.88 + 0.18 * noise(5851, 3.0))[..., None]
base = paint(base, (0.2, 0.195, 0.19), smooth01(0.15, 0.7, noise(5852, 6.0)) * 0.55 * skin)
base = paint(base, (0.42, 0.41, 0.39), smooth01(0.3, 0.9, up) * smooth01(0.0, 0.5, noise(5853, 10.0)) * 0.35 * skin)
# The ribs' and the bones' shadows; bruised violet-grey round the joints, the sockets and the mouth's corners.
between = field(lambda q: smooth01(1.32, 1.4, q[:, 2]) * smooth01(1.68, 1.6, q[:, 2])) - field(ribs_of)
base = paint(base, (0.12, 0.11, 0.115), np.clip(between, 0, 1) * 0.45 * skin)
bruise = smooth01(0.4, 0.85, noise(5854, 14.0))
base = paint(base, (0.2, 0.16, 0.19), bruise * 0.35 * skin)
for e in EYES:
    d = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.07, 0.055, 0.06), smooth01(0.045, 0.015, d) * skin * 0.85)
# Round the mouth's line the stretched skin's darker and veined.
lip = smooth01(0.03, 0.005, np.abs(P[..., 2] - MOUTH_Z)) * smooth01(0.03, 0.08, P[..., 1]) * smooth01(1.95, 1.9, P[..., 2])
base = paint(base, (0.13, 0.09, 0.1), lip * skin * 0.7)
# The hands and the feet grimed black from the floors it crawls over; the knees and the elbows too.
low = smooth01(0.22, 0.05, P[..., 2])
tip = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TIPS], 0), 0)
base = paint(base, (0.04, 0.035, 0.03), np.maximum(low, smooth01(0.12, 0.03, tip)) * 0.85 * skin)
knees = smooth01(0.54, 0.6, P[..., 2]) * smooth01(0.7, 0.64, P[..., 2]) * smooth01(0.0, 0.05, P[..., 1])
base = paint(base, (0.12, 0.11, 0.1), knees * 0.5 * skin)
# The mouth: black-red and wet inside; the teeth long, yellowed to brown at the roots; the eyes black glints; the nails.
mouth = is_("lodger_mouth")
base = paint(base, (0.07, 0.012, 0.012), mouth * (0.9 + 0.2 * noise(5855, 40.0)))
teeth = is_("lodger_tooth")
root = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TEETH_AT], 0), 0)
base[teeth] = np.array((0.5, 0.45, 0.33), np.float32)
base = paint(base, (0.16, 0.1, 0.06), smooth01(0.012, 0.003, root) * teeth)
eye = is_("lodger_eye")
base[eye] = np.array((0.01, 0.009, 0.009), np.float32)
nail = is_("lodger_nail")
base[nail] = np.array((0.06, 0.05, 0.04), np.float32)
base = np.clip(base, 0, 1)

rough = np.full((S, S), 0.8, np.float32) + 0.06 * noise(5856, 10.0)
rough[mouth] = 0.15
rough[teeth] = 0.35
rough[eye] = 0.05
rough[nail] = 0.45
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.02, 0.018, 0.018), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("lodger", "the Lodger, modelled in tools/blender/lodger.py, its colour painted here"),
             rough=rough, lod=0.4)
