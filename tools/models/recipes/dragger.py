"""THE DRAGGER (T46, App. A.4, B.4): the limb of tools/blender/dragger.py, taken to the fidelity target (ARCHITECTURE §8
note 58, tools/models overbake).

tools/blender/dragger.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin the dead pale of something kept out of the light (§26.1: the one light thing at a car's dark edge, so the
    reach reads in time), dry and crazed fine all over, the tendons standing under it the length of the arm, the veins
    blue-grey through it;
  * every joint creased in rings, the extra ones (one more than an arm should have) swollen, split and raw;
  * below the roof's lip, where it lives under the car, black with the train's soot, grading up into the pale;
  * the long fingertips grey with dirt worked into the creases, the nails black and wet.
The skin is dry (rough 0.6); the raw joints and the nails wet.

    tools/models/build.sh dragger
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import bell, fine, smooth01  # noqa: E402

kit, g, arm, parts = overbake.hold("dragger.py")
print("[dt] dragger parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

sk = g["sk"]
ROOF = g["ROOF"]
# Where it bends: the elbows' raw knots (the game mesh's), the wrist, and every finger's two knuckles.
ELBOWS = np.array([tuple(g["path"][4]), tuple(g["path"][6])], np.float32)
KNUCKLES = np.array([tuple(g["wrist"])] + [tuple(sk[f"finger_{k + 1}_{s}"].head) for k in range(4) for s in "ab"], np.float32)
TIPS = np.array([tuple(sk[f"finger_{k + 1}_b"].tail) for k in range(4)], np.float32)

# Linear colours. The skin's a flat dead grey-white; its colour is painted (the veins, the raw joints, the soot).
PALE = (0.34, 0.345, 0.34)
DRESS = {
    "flesh.pale": (lambda: make.flat("dragger_skin", PALE, rough=0.6), 3),
    "flesh.raw": (lambda: make.flat("dragger_raw", (0.16, 0.1, 0.09), rough=0.35), 3),
    "tar.nail": (lambda: make.flat("dragger_nail", (0.02, 0.018, 0.016), rough=0.2), 0),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"dragger: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def nearest(p, points):
    return np.min(np.linalg.norm(p[:, None, :] - points[None, :, :], axis=2), axis=1)


def veins_of(p):
    return smooth01(0.86, 0.96, ridged(p, 911, 11.0)) * smooth01(-0.2, 0.4, cook.noise_np(p, 912, 3.0))


def skin_shape(p, n):
    """Dry and crazed fine; tendons standing in long cords (stretched noise: the arm runs up, then across, so both); the
    veins raised a little; every joint creased in rings round it."""
    d = 0.00022 * (ridged(p, 901, 170.0) ** 6 - 0.2)
    d += 0.0009 * (ridged(p * np.array([3.0, 3.0, 0.35], np.float32), 902, 22.0) ** 5)
    d += 0.0007 * (ridged(p * np.array([0.35, 3.0, 3.0], np.float32), 903, 22.0) ** 5) * smooth01(ROOF - 0.15, ROOF + 0.1, p[:, 2])
    d += 0.0003 * veins_of(p)
    r = np.minimum(nearest(p, KNUCKLES), nearest(p, ELBOWS))
    d += 0.00045 * smooth01(0.035, 0.0, r) * np.sin(r * 900)
    return d + fine(p, 0.0001, 520, 904)


def raw_shape(p, n):
    """The swollen joints split open: deep cracks across them, the edges lifted."""
    crack = smooth01(0.88, 0.97, ridged(p, 921, 40.0))
    return -0.0016 * crack + 0.0007 * np.maximum(0, cook.noise_np(p, 922, 30.0)) + fine(p, 0.00015, 480, 923)


SHAPE = {"flesh.pale": skin_shape, "flesh.raw": raw_shape, "tar.nail": lambda p, n: fine(p, 0.00008, 600, 931)}
BAKED = ["limb", "hand"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] dragger highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: veins, blue-grey through the skin; G: soot, from under the car (below the lip) and in the creases; B: raw, round
    the swollen joints and the knuckles, and the dirt worked into the fingertips."""
    out = np.zeros((len(p), 3), np.float32)
    skin = kind.startswith("flesh")
    if skin:
        out[:, 0] = veins_of(p)
    under = smooth01(ROOF + 0.05, ROOF - 0.45, p[:, 2]) * (0.75 + 0.25 * cook.noise_np(p, 941, 7.0))
    creases = smooth01(0.85, 0.97, ridged(p, 942, 60.0)) * 0.35
    out[:, 1] = np.clip(under + creases, 0, 1)
    raw = smooth01(0.09, 0.03, nearest(p, ELBOWS)) + 0.5 * smooth01(0.03, 0.008, nearest(p, KNUCKLES))
    raw = np.where(kind.startswith("flesh.raw"), 1.0, raw)
    tips = smooth01(0.06, 0.015, nearest(p, TIPS))
    out[:, 2] = np.clip(raw * (0.7 + 0.3 * cook.noise_np(p, 943, 25.0)) + 0.6 * tips, 0, 1)
    return out


def kind_of(m):
    return 0


atlas = overbake.Atlas("dragger", parts, BAKED, kind_of)
atlas.unwrap()
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"limb": (0.006, 0.02), "hand": (0.004, 0.014)}, height=1.6, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.012, 0.011, 0.01), crease=0.55, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.16, 0.18, 0.22), mk[..., 0] * 0.6)     # veins, blue-grey through the skin
base = paint(base, (0.21, 0.11, 0.095), mk[..., 2] * 0.75)   # raw at the joints, dirt in the tips
base = paint(base, (0.018, 0.016, 0.015), mk[..., 1] * 0.9)  # the train's soot, from under the car
# Dry skin; the raw joints wet.
rough = np.full(base.shape[:2], 0.62, np.float32) - 0.3 * np.clip(mk[..., 2], 0, 1)
atlas.finish(base, kit, arm, made=make.provenance("dragger", "the Dragger's limb, modelled over tools/blender/dragger.py"), rough=rough)
