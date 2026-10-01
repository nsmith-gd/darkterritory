"""THE WHISTLER (GDD v1.2 §21 between the cars, App. A.4): the coiled thing of tools/blender/whistler.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake; the non-human redo, note 132).

tools/blender/whistler.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the plates down its back the yellowed grey of old ivory, finely ridged across, polished where they rub, the edges
    darker;
  * where they part, grub-white flesh, faintly veined, wet;
  * all of it streaked with the couplers' black grease, heaviest under it and in the joints, glossy where it's thick;
  * the legs a darker horn, the hooks black; the siphon's lip and its stops red and wet, the holes black.

    tools/models/build.sh whistler
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

kit, g, arm, parts = overbake.hold("whistler.py")
print("[dt] whistler parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.whistler_plate": (lambda: make.flat("whistler_plate", (0.28, 0.25, 0.2), rough=0.3), 2),
    "skin.whistler_leg": (lambda: make.flat("whistler_leg", (0.09, 0.075, 0.062), rough=0.45), 1),
    "skin.whistler_lip": (lambda: make.flat("whistler_lip", (0.17, 0.05, 0.04), rough=0.15), 2),
    "skin.whistler": (lambda: make.flat("whistler_flesh", (0.38, 0.34, 0.29), rough=0.35), 2),
    "tar.whistler_claw": (lambda: make.flat("whistler_claw", (0.012, 0.01, 0.009), rough=0.25), 1),
    "tar.whistler_mouth": (lambda: make.flat("whistler_mouth", (0.008, 0.004, 0.004), rough=0.1), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"whistler: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


# The body lies along Y at rest: the plates' ridges run across it (fine bands in Y), the grease runs down its sides.
def plate_shape(p, n):
    bands = 0.0004 * np.sin(p[:, 1] * 260 + 2 * cook.noise_np(p, 1401, 6.0))
    return bands + 0.0006 * cook.noise_np(p, 1402, 30.0) + fine(p, 0.00008, 700, 1403)


def flesh_shape(p, n):
    folds = 0.0008 * np.sin(p[:, 1] * 420 + 3 * cook.noise_np(p, 1404, 8.0))
    return folds + 0.0004 * smooth01(0.9, 0.98, ridged(p, 1405, 40.0)) + fine(p, 0.00006, 900, 1406)


def leg_shape(p, n):
    return 0.0003 * cook.noise_np(p * np.array([1, 1, 0.2], np.float32), 1407, 60.0) + fine(p, 0.00006, 900, 1408)


SHAPE = {"skin.whistler_plate": plate_shape, "skin.whistler_leg": leg_shape, "skin.whistler_lip": lambda p, n: fine(p, 0.0001, 600, 1409),
         "skin.whistler": flesh_shape, "tar.whistler": lambda p, n: fine(p, 0.00005, 800, 1410)}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] whistler highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})
Z0 = g["Z0"]


def grease(p):
    """0..1: the couplers' grease, run down its sides from the top in streaks, thick underneath."""
    under = smooth01(Z0 + 0.02, Z0 - 0.1, p[:, 2])
    streak = smooth01(0.5, 0.9, np.abs(np.sin(p[:, 1] * 38 + 2.5 * cook.noise_np(p, 1411, 4.0)))) * smooth01(-0.3, 0.6, cook.noise_np(p, 1412, 3.0))
    return np.clip(under * 0.8 + streak * 0.75 + 0.5 * smooth01(0.3, 0.8, cook.noise_np(p, 1413, 7.0)), 0, 1)


def marks(p, kind):
    """R: grease; G: the plates yellowed (and their edges darker); B: the flesh's veins."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith(("skin.whistler_plate", "skin.whistler_leg")) or kind == "skin.whistler" or kind.startswith("skin.whistler."):
        out[:, 0] = grease(p)
    if kind.startswith("skin.whistler_plate"):
        out[:, 1] = np.clip(0.5 + 0.5 * cook.noise_np(p, 1414, 5.0), 0, 1)
    if kind == "skin.whistler" or kind.startswith("skin.whistler."):
        out[:, 2] = np.clip(smooth01(0.9, 0.975, ridged(p * np.array([1, 0.5, 1], np.float32), 1415, 30.0)), 0, 1)
    return out


def gloss(p, kind):
    """R: the roughness."""
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.3 if kind.startswith("skin.whistler_plate") else 0.45 if kind.startswith("skin.whistler_leg") else
                 0.15 if kind.startswith("skin.whistler_lip") else 0.25 if kind.startswith("tar.whistler_claw") else
                 0.1 if kind.startswith("tar.whistler_mouth") else 0.35)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.whistler_lip", "tar.whistler_mouth")) else 0


atlas = overbake.Atlas("whistler", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 1.6})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.004, 0.015) for name in BAKED}, height=1.0, masks={"marks": marks, "gloss": gloss})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.012, 0.011, 0.01), crease=0.6, ao_floor=0.35)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.3, 0.26, 0.16), mk[..., 1] * 0.35)      # the plates yellowed
base = paint(base, (0.2, 0.08, 0.07), mk[..., 2] * 0.45)      # veins in the flesh
base = paint(base, (0.01, 0.009, 0.008), mk[..., 0] * 0.85)   # grease
rough = np.clip(atlas.maps["gloss"][..., 0] - 0.15 * mk[..., 0], 0.05, 1)
atlas.finish(base, kit, arm, made=make.provenance("whistler", "the Whistler, modelled over tools/blender/whistler.py"), rough=rough)
