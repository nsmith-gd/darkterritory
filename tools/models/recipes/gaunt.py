"""THE GAUNT (GDD v1.2 §21 asleep in villages and yards, App. A.6): the lonely thing of tools/blender/gaunt.py, taken to
the fidelity target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/gaunt.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin ash-grey and dry as an old saddle, cracked all over in a network, grey-green mould in the cracks: it's lain
    out in the weather a long time;
  * the slack skin hanging off it (the belly's sack, the webs, the folds) darker, weathered, the cracks deeper, the ragged edges worn thin;
  * the eyes big, black and wet, the lower lids' red rims and the mouth's hole wet too (the only wet on it); the nails
    horn, brown and ridged.
Dry (rough 0.85) but the eyes, the rims and the mouth (0.08).

    tools/models/build.sh gaunt
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import fine, smooth01  # noqa: E402

kit, g, arm, parts = overbake.hold("gaunt.py")
print("[dt] gaunt parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.gaunt_face": (lambda: make.flat("gaunt_face", (0.16, 0.15, 0.135), rough=0.85), 3),
    "skin.gaunt_fold": (lambda: make.flat("gaunt_fold", (0.11, 0.1, 0.09), rough=0.9), 2),
    "skin.gaunt_rim": (lambda: make.flat("gaunt_rim", (0.16, 0.035, 0.03), rough=0.3), 3),
    "skin.gaunt": (lambda: make.flat("gaunt_skin", (0.14, 0.13, 0.115), rough=0.85), 2),
    "glass_dirty.gaunt_eye": (lambda: make.flat("gaunt_eye", (0.006, 0.006, 0.007), rough=0.05), 2),
    "tar.gaunt_nail": (lambda: make.flat("gaunt_nail", (0.06, 0.045, 0.03), rough=0.5), 1),
    "tar.gaunt_mouth": (lambda: make.flat("gaunt_mouth", (0.012, 0.006, 0.006), rough=0.2), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"gaunt: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def cracks_of(p, scale=40.0):
    """The dry skin's cracks: a network, 0..1 on a crack."""
    return smooth01(0.88, 0.97, ridged(p, 1301, scale)) + 0.6 * smooth01(0.9, 0.98, ridged(p, 1302, scale * 2.3))


def skin_shape(p, n):
    return -0.0012 * np.clip(cracks_of(p), 0, 1) + 0.0008 * cook.noise_np(p, 1303, 18.0) + fine(p, 0.00015, 500, 1304)


def fold_shape(p, n):
    return -0.0018 * np.clip(cracks_of(p, 26.0), 0, 1) + 0.002 * cook.noise_np(p, 1311, 9.0) + fine(p, 0.0002, 400, 1312)


SHAPE = {"skin.gaunt_rim": lambda p, n: fine(p, 0.0001, 500, 1305), "skin.gaunt_face": skin_shape, "skin.gaunt_fold": fold_shape,
         "skin.gaunt": skin_shape}
BAKED = ["head", "body", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] gaunt highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the cracks; G: mould in them, and in blotches; B: the eyes (their wet)."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.gaunt") and not kind.startswith("skin.gaunt_rim"):
        c = np.clip(cracks_of(p, 26.0 if kind.startswith("skin.gaunt_fold") else 40.0), 0, 1)
        out[:, 0] = c
        out[:, 1] = np.clip(c * smooth01(0.0, 0.6, cook.noise_np(p, 1321, 3.0)) + 0.6 * smooth01(0.55, 0.85, cook.noise_np(p, 1322, 5.0)), 0, 1)
    out[:, 2] = kind.startswith(("glass_dirty.gaunt_eye", "skin.gaunt_rim", "tar.gaunt_mouth"))
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.gaunt_face", "skin.gaunt_rim", "glass_dirty.gaunt_eye", "tar.gaunt_mouth")) else 0


atlas = overbake.Atlas("gaunt", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.2})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.008, 0.02), "body": (0.012, 0.03), "limbs": (0.01, 0.025)}, height=3.0, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.015, 0.014, 0.012), crease=0.55, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.05, 0.045, 0.04), mk[..., 0] * 0.6)     # the cracks, dark
base = paint(base, (0.07, 0.085, 0.06), mk[..., 1] * 0.5)     # mould, grey-green

rough = np.full(base.shape[:2], 0.85, np.float32)
rough = np.where(mk[..., 2] > 0.5, 0.08, rough)    # the wet: the eyes, the red of the lids, the mouth
atlas.finish(base, kit, arm, made=make.provenance("gaunt", "the Gaunt, modelled over tools/blender/gaunt.py"), rough=rough)
