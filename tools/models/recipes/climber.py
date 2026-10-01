"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4): the six-limbed crawler of tools/blender/climber.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake; the non-human redo, note 136).

tools/blender/climber.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin soot-black and smooth as wet rubber, a sheen on it, finely wrinkled at the joints, scuffed grey where it's
    dragged along steel;
  * the belly and the folds at the joints a paler ash-grey;
  * the sucker's gums dark red and wet, the teeth yellowed horn; the hooks and the spines black, polished.

    tools/models/build.sh climber
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

kit, g, arm, parts = overbake.hold("climber.py")
print("[dt] climber parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.climber_belly": (lambda: make.flat("climber_belly", (0.06, 0.056, 0.052), rough=0.4), 2),
    "skin.climber_teeth": (lambda: make.flat("climber_teeth", (0.3, 0.26, 0.17), rough=0.4), 1),
    "skin.climber": (lambda: make.flat("climber_skin", (0.016, 0.015, 0.014), rough=0.25), 2),
    "tar.climber_mouth": (lambda: make.flat("climber_mouth", (0.09, 0.015, 0.012), rough=0.12), 1),
    "tar.climber_nail": (lambda: make.flat("climber_nail", (0.008, 0.007, 0.006), rough=0.2), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"climber: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def skin_shape(p, n):
    # Rubber: smooth, but wrinkled finely in places, the wrinkles crossing.
    w = smooth01(0.4, 0.8, cook.noise_np(p, 1701, 6.0))
    return -0.0003 * w * smooth01(0.85, 0.97, ridged(p, 1702, 160.0)) + fine(p, 0.00004, 1200, 1703)


SHAPE = {"skin.climber_belly": skin_shape, "skin.climber_teeth": lambda p, n: fine(p, 0.00003, 1200, 1704), "skin.climber": skin_shape,
         "tar.climber": lambda p, n: fine(p, 0.00003, 1200, 1705)}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] climber highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: scuffed grey where it's dragged along steel (its underside, its forearms, its hooks' roots); G: sheen
    blotches (wetter)."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.climber") and not kind.startswith("skin.climber_teeth"):
        low = smooth01(0.35, 0.05, p[:, 2])
        out[:, 0] = np.clip(low * smooth01(0.1, 0.8, cook.noise_np(p * np.array([1, 0.3, 1], np.float32), 1711, 25.0)), 0, 1)
        out[:, 1] = smooth01(0.2, 0.8, cook.noise_np(p, 1712, 4.0))
    return out


def gloss(p, kind):
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.12 if kind.startswith("tar.climber_mouth") else 0.4 if kind.startswith("skin.climber_teeth") else
                 0.2 if kind.startswith("tar.climber_nail") else 0.4 if kind.startswith("skin.climber_belly") else 0.25)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("tar.climber_mouth", "skin.climber_teeth")) else 0


atlas = overbake.Atlas("climber", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.003, 0.012) for name in BAKED}, height=1.0, masks={"marks": marks, "gloss": gloss})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.01, 0.009, 0.009), crease=0.5, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.07, 0.066, 0.062), mk[..., 0] * 0.55)    # scuffed grey
rough = np.clip(atlas.maps["gloss"][..., 0] - 0.1 * mk[..., 1] + 0.25 * mk[..., 0], 0.05, 1)
atlas.finish(base, kit, arm, made=make.provenance("climber", "the Climber, modelled over tools/blender/climber.py"), rough=rough)
