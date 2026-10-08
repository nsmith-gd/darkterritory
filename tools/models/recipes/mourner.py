"""THE MOURNERS (GDD §21, OUTSIDE; note 362; docs/design/creatures/mourners.md §3): tools/blender/mourner.py's stooping
scavenger with its colour and its fine relief baked into one 2048 atlas (the Look Review: organic, not primitives; the
Gannet's and the Ribbit's way, notes 340, 361).

tools/blender/mourner.py stays its source: the rig, the fused skin and the parts over it, the clips. This recipe runs
it, unwraps every part into one atlas (the veil, the hands and the eyes given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the hide dried and cracked like
    old clay into plates, deeper over the shoulders and the arms where it stretches; the ribs standing in the starved
    flanks; the knuckles, elbows and knees wrinkled; the veil's loose skin hanging in soft folds;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): ash-pale and dry, a
    grey dust settled on everything that faces up, the cracks dark between the plates, the creases sooted, the veil a
    little darker and bruised where it hangs, the eyes black and wet under it, the nails long and dark as horn.
Dry all over (rough 0.8-0.9); only the eyes glint.

    tools/models/build.sh mourner        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/mourner.py)
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
kit, g, arm, parts = overbake.hold("mourner.py")
print("[dt] mourner parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
HEAD = np.array(tuple(g["HEAD"]), np.float32)


def suffix(name):
    return name.split(".")[1]


DRESS = {"mourner": 3, "mourner_veil": 2, "mourner_nail": 0, "mourner_eye": 1}


def dress(m):
    return make.flat("mourner_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def cracks_of(p, scale=34.0):
    """Dried clay: a net of cracks (two scales of ridged noise's crests), wider where the skin stretches."""
    return np.maximum(smooth01(0.93, 0.982, ridged(p, 1101, scale)), 0.8 * smooth01(0.94, 0.985, ridged(p, 1102, scale * 2.6)))


def ribs_of(p):
    """The ribs in the starved flanks, under the chest's leant-back cage."""
    band = smooth01(0.66, 0.72, p[:, 2]) * smooth01(0.88, 0.8, p[:, 2]) * smooth01(0.05, 0.11, np.abs(p[:, 0]))
    return band * (0.5 + 0.5 * np.sin((p[:, 2] - 0.02 * p[:, 1]) * 170))


def skin_shape(p, n):
    d = -0.0022 * cracks_of(p) + 0.0011 * ribs_of(p) + 0.0006 * cook.noise_np(p, 1103, 9.0)
    return d + fine(p, 0.00025, 420, 1104)


def veil_shape(p, n):
    # Soft hanging folds, down the veil's length; pores.
    return 0.0012 * np.sin(p[:, 0] * 260 + 2 * cook.noise_np(p, 1111, 12.0)) - 0.0008 * cracks_of(p, 40.0) + fine(p, 0.0002, 500, 1112)


SHAPE = {"skin.mourner_veil": veil_shape, "skin.mourner_nail": lambda p, n: 0 * p[:, 0], "glass_dirty.mourner_eye": lambda p, n: 0 * p[:, 0],
         "skin.mourner": skin_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] mourner highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("mourner_veil", "mourner_eye", "mourner_nail") else 0


atlas = overbake.Atlas("mourner", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.6})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "fine": (0.004, 0.012)}, samples=16, height=1.0)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] mourner atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
skin = is_("mourner", "mourner_veil")
base = np.broadcast_to(np.array((0.23, 0.22, 0.205), np.float32), (S, S, 3)).copy()
base *= (0.9 + 0.14 * noise(3601, 3.0))[..., None]
# Ash: blotches paler and greyer, and a dust settled on whatever faces up (the shoulders' tops, the back of the head).
base = paint(base, (0.36, 0.355, 0.34), smooth01(0.1, 0.6, noise(3602, 4.5)) * 0.5 * skin)
base = paint(base, (0.13, 0.12, 0.115), smooth01(0.2, 0.7, noise(3606, 7.0)) * 0.45 * skin)
# Bruised grey-violet round the joints where the dry skin folds (the elbows, the knees, the armpits).
base = paint(base, (0.16, 0.13, 0.14), smooth01(0.35, 0.8, noise(3607, 16.0)) * 0.3 * skin)
base = paint(base, (0.4, 0.4, 0.385), smooth01(0.2, 0.9, up) * smooth01(-0.2, 0.5, noise(3603, 11.0)) * 0.55 * skin)
# Bruised and sallow lower down, where it's been dragged through the mud and the cinders: the shins, the forearms' undersides.
low = smooth01(0.3, 0.06, P[..., 2])
base = paint(base, (0.25, 0.22, 0.19), low * 0.6 * skin)
base = paint(base, (0.33, 0.29, 0.27), smooth01(0.0, -0.6, up) * 0.35 * skin)
# The cracks: dark between the plates of dried skin, a pale lifted rim along each.
crack = field(cracks_of)
base = paint(base, (0.06, 0.055, 0.05), crack * 0.75 * skin)
rim = np.clip(field(lambda q: cracks_of(q * 1.0) - cracks_of(q * 1.0 + 0.002)), 0, 1)
base = paint(base, (0.45, 0.44, 0.42), rim * 0.25 * skin)
rough = np.full((S, S), 0.82, np.float32) + 0.1 * crack
# The veil: darker, a bruised yellow-grey, its folds sooted; the eyes under it black and wet; the nails dark horn.
veil = is_("mourner_veil")
base = paint(base, (0.09, 0.075, 0.065), veil * 0.85)
base = paint(base, (0.22, 0.14, 0.13), veil * smooth01(0.3, 0.8, noise(3604, 14.0)) * 0.35)
for e in EYES:
    r = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.05, 0.04, 0.035), smooth01(0.03, 0.014, r) * skin * 0.8)
eye = is_("mourner_eye")
base[eye] = np.array((0.012, 0.01, 0.009), np.float32)
rough[eye] = 0.05
nail = is_("mourner_nail")
base[nail] = np.array((0.07, 0.06, 0.045), np.float32) * (0.8 + 0.4 * noise(3605, 30.0))[nail][:, None]
rough[nail] = 0.45
# The long fingers greyer and dirtier to the tips, grimed from what they've hauled.
tips = smooth01(0.42, 0.3, P[..., 2]) * (np.abs(P[..., 0]) > 0.14) * smooth01(0.24, 0.32, P[..., 1])
base = paint(base, (0.16, 0.14, 0.12), tips * 0.6 * skin)
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.025, 0.022, 0.02), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("mourner", "the Mourners, modelled in tools/blender/mourner.py, their colour painted here"),
             rough=rough, lod=0.4)
