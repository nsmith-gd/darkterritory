"""THE FOLLOWERS (GDD v1.2 §21 facility grounds, App. A.6): the hand of tools/blender/follower.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/follower.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin an infant's, soft, pale and faintly translucent, the veins showing blue-grey through it, wrinkled at every
    knuckle and grimed in the creases;
  * the stump raw and wet, its threads darker;
  * the mouth's lips red and wet, the teeth yellowed, the nails dirty and split.
Skin 0.45; the stump, the mouth and its teeth wet (0.15).

    tools/models/build.sh follower
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

kit, g, arm, parts = overbake.hold("follower.py")
print("[dt] follower parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.follower_stump": (lambda: make.flat("follower_stump", (0.2, 0.07, 0.06), rough=0.15), 2),
    "skin.follower_mouth": (lambda: make.flat("follower_mouth", (0.18, 0.04, 0.04), rough=0.15), 2),
    "skin.follower_teeth": (lambda: make.flat("follower_teeth", (0.45, 0.4, 0.3), rough=0.3), 0),
    "skin.follower": (lambda: make.flat("follower_skin", (0.3, 0.26, 0.24), rough=0.45), 2),
    "tar.follower_core": (lambda: make.flat("follower_core", (0.01, 0.003, 0.003), rough=0.2), 0),
    "tar.follower_nail": (lambda: make.flat("follower_nail", (0.12, 0.1, 0.075), rough=0.5), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"follower: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def skin(p, n):
    # Fine wrinkles across the fingers (along y the creases run across), a soft lumpiness.
    return 0.00012 * np.sin(p[:, 1] * 900) ** 8 + 0.0002 * cook.noise_np(p, 1601, 120.0) + fine(p, 0.00005, 2000, 1602)


SHAPE = {"skin.follower_stump": lambda p, n: 0.0004 * cook.noise_np(p, 1603, 300.0), "skin.follower_mouth": lambda p, n: fine(p, 0.00006, 1500, 1604),
         "skin.follower_teeth": lambda p, n: 0 * p[:, 0], "skin.follower": skin, "tar.follower": lambda p, n: 0 * p[:, 0]}
BAKED = ["hand"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] follower highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the veins under the skin; G: grime in the creases."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.follower") and not kind.startswith("skin.follower_"):
        out[:, 0] = np.clip(smooth01(0.9, 0.97, ridged(p, 1611, 90.0)), 0, 1)
        out[:, 1] = np.clip(np.sin(p[:, 1] * 900) ** 8 * 0.6 + 0.3 * smooth01(0.2, 0.8, cook.noise_np(p, 1612, 40.0)), 0, 1)
    return out


atlas = overbake.Atlas("follower", parts, BAKED, lambda m: 0)
atlas.unwrap()
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"hand": (0.002, 0.006)}, height=0.1, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.05, 0.04, 0.035), crease=0.45, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.2, 0.22, 0.27), mk[..., 0] * 0.5)    # veins, blue-grey under the skin
base = paint(base, (0.12, 0.1, 0.08), mk[..., 1] * 0.5)    # grime in the creases
atlas.finish(base, kit, arm, made=make.provenance("follower", "the Follower, modelled over tools/blender/follower.py"))
