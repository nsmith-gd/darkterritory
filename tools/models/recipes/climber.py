"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4): the crewman gone wrong of tools/blender/climber.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/climber.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin soot-black and oily where the coveralls are torn off it, wet in the creases;
  * the coveralls the crew's, worn to rags, scorched and burnt into the skin at the edges of every rent;
  * the mask's rubber cracked and perished, grown seamless into the face; the eyepieces' rims and the canister rusted.
Skin 0.45, cloth 0.9, the rubber 0.6, the glass black and wet (0.05).

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
    "tar.climber_nail": (lambda: make.flat("climber_nail", (0.01, 0.009, 0.008), rough=0.4), 1),
    "tar.climber": (lambda: make.flat("climber_skin", (0.02, 0.018, 0.015), rough=0.45), 2),
    "wool.climber_coverall": (lambda: make.flat("climber_coverall", (0.05, 0.047, 0.04), rough=0.9), 2),
    "leather.climber_mask": (lambda: make.flat("climber_mask", (0.03, 0.028, 0.025), rough=0.6), 2),
    "glass_dirty.climber_eye": (lambda: make.flat("climber_eye", (0.004, 0.004, 0.004), rough=0.05), 1),
    "rust_heavy.climber_rim": (lambda: make.flat("climber_rim", (0.1, 0.07, 0.045), rough=0.5), 1),
    "rust_heavy.climber_canister": (lambda: make.flat("climber_canister", (0.08, 0.06, 0.04), rough=0.55), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"climber: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def skin(p, n):
    return 0.0006 * cook.noise_np(p, 1701, 40.0) + fine(p, 0.00015, 500, 1702)


def cloth(p, n):
    d = 0.0016 * (ridged(p * np.array([1.0, 1.0, 0.4], np.float32), 1711, 14.0) ** 2 - 0.3)
    d += 0.0002 * (np.sin(p[:, 2] * 2400) + np.sin((p[:, 0] + p[:, 1]) * 2400))
    return d + fine(p, 0.00025, 420, 1712)


def rubber(p, n):
    crack = smooth01(0.92, 0.98, ridged(p, 1721, 70.0))
    return -0.0005 * crack + fine(p, 0.0001, 600, 1722)


def rust(p, n):
    return 0.0006 * np.maximum(0, cook.noise_np(p, 1731, 60.0)) ** 2 + fine(p, 0.0001, 600, 1732)


SHAPE = {"tar.climber_nail": lambda p, n: 0 * p[:, 0], "tar.climber": skin, "wool.climber": cloth, "leather.climber": rubber,
         "glass_dirty.climber": lambda p, n: 0 * p[:, 0], "rust_heavy.climber": rust}
BAKED = ["head", "body", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] climber highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: soot and scorch on the cloth; G: the rubber's cracks pale; B: the glass's wet."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("wool.climber"):
        out[:, 0] = np.clip(smooth01(0.1, 0.8, cook.noise_np(p, 1741, 7.0)), 0, 1)
    if kind.startswith("leather.climber"):
        out[:, 1] = np.clip(smooth01(0.92, 0.98, ridged(p, 1721, 70.0)), 0, 1)
    out[:, 2] = kind.startswith("glass_dirty.climber")
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("leather.climber_mask", "glass_dirty.climber", "rust_heavy.climber")) else 0


atlas = overbake.Atlas("climber", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.006, 0.02), "body": (0.01, 0.025), "limbs": (0.008, 0.02)}, height=1.9, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.012, 0.011, 0.01), crease=0.5, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.02, 0.016, 0.012), mk[..., 0] * 0.6)    # soot and scorch
base = paint(base, (0.07, 0.065, 0.06), mk[..., 1] * 0.5)     # the perished rubber's cracks
rough = np.full(base.shape[:2], 0.7, np.float32)
rough = np.where(mk[..., 2] > 0.5, 0.05, rough)
atlas.finish(base, kit, arm, made=make.provenance("climber", "the Climber, modelled over tools/blender/climber.py"), rough=rough)
