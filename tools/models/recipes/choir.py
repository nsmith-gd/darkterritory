"""THE CHOIR (GDD v1.2 §21 drawn by noise, App. A.7): the singing bell of tools/blender/choir.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake; the non-human redo, note 134).

tools/blender/choir.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the bell's membrane a dead blue-grey, thin, the veins standing dark in it and branching down from the crown, the
    panels between them paler, a faint wet sheen over all of it;
  * the frilled skirt and the curtains paler still, almost white at their edges, veined finer;
  * the lips a child's, gone grey-violet, cracked; the milk teeth yellowed; the throat black and wet;
  * the tendrils banded faintly, darker at their roots.
The membrane's wet (rough 0.35), the lips and the throat wetter (0.15, 0.1).

    tools/models/build.sh choir
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

kit, g, arm, parts = overbake.hold("choir.py")
print("[dt] choir parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.choir_face": (lambda: make.flat("choir_lips", (0.13, 0.115, 0.135), rough=0.15), 3),
    "skin.choir_frill": (lambda: make.flat("choir_frill", (0.24, 0.255, 0.27), rough=0.35), 2),
    "skin.choir_teeth": (lambda: make.flat("choir_teeth", (0.34, 0.31, 0.24), rough=0.4), 1),
    "skin.choir": (lambda: make.flat("choir_skin", (0.17, 0.19, 0.21), rough=0.35), 2),
    "tar.choir_hole": (lambda: make.flat("choir_hole", (0.004, 0.003, 0.003), rough=0.1), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"choir: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


BC = np.array(g["BC"], np.float32)


def veins_of(p, scale=14.0):
    """0..1 on a vein: branching down from the crown (stretched along the bell's height)."""
    d = p - BC
    q = np.stack([np.arctan2(d[:, 0], d[:, 1]) * 0.35, d[:, 2] * 0.6, np.hypot(d[:, 0], d[:, 1]) * 0.3], axis=1).astype(np.float32)
    return np.clip(smooth01(0.88, 0.97, ridged(q, 1201, scale)) + 0.6 * smooth01(0.92, 0.985, ridged(p, 1202, scale * 3)), 0, 1)


def skin_shape(p, n):
    return 0.0007 * veins_of(p) + fine(p, 0.00006, 900, 1203)


def lip_shape(p, n):
    # Cracked across, the way dry lips crack.
    return -0.0004 * smooth01(0.85, 0.97, ridged(p * np.array([1, 1, 0.25], np.float32), 1204, 120.0)) + fine(p, 0.00005, 900, 1205)


SHAPE = {"skin.choir_face": lip_shape, "skin.choir_frill": lambda p, n: 0.0004 * veins_of(p, 30.0) + fine(p, 0.00005, 900, 1206),
         "skin.choir_teeth": lambda p, n: fine(p, 0.00003, 900, 1207), "skin.choir": skin_shape, "tar.choir_hole": lambda p, n: 0 * p[:, 0]}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] choir highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: veins, dark; G: the membrane's paler panels and frill edges; B: the tendrils' bands."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith(("skin.choir_frill", "skin.choir.")) or kind == "skin.choir":
        out[:, 0] = veins_of(p, 30.0 if kind.startswith("skin.choir_frill") else 14.0)
        out[:, 1] = np.clip(0.5 + 0.5 * cook.noise_np(p, 1211, 6.0), 0, 1) * (1 - out[:, 0])
        # Below the rim (the tendrils): faint bands down them.
        below = smooth01(BC[2] - 0.15, BC[2] - 0.25, p[:, 2])
        out[:, 2] = below * smooth01(0.3, 0.9, np.abs(np.sin(p[:, 2] * 60)))
    return out


def gloss(p, kind):
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.15 if kind.startswith("skin.choir_face") else 0.1 if kind.startswith("tar.choir_hole") else
                 0.4 if kind.startswith("skin.choir_teeth") else 0.35)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.choir_face", "tar.choir_hole", "skin.choir_teeth")) else 0


atlas = overbake.Atlas("choir", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.004, 0.012) for name in BAKED}, height=1.3, masks={"marks": marks, "gloss": gloss})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.015, 0.015, 0.018), crease=0.45, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.26, 0.28, 0.3), mk[..., 1] * 0.35)      # paler panels
base = paint(base, (0.05, 0.045, 0.08), mk[..., 0] * 0.7)     # veins
base = paint(base, (0.09, 0.09, 0.11), mk[..., 2] * 0.4)      # the tendrils' bands
rough = atlas.maps["gloss"][..., 0]
atlas.finish(base, kit, arm, made=make.provenance("choir", "the Choir, modelled over tools/blender/choir.py"), rough=rough)
