"""THE CHOIR (GDD v1.2 §21 drawn by noise, App. A.7): the choir child of tools/blender/choir.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/choir.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin a dead blue-grey, the veins dark under it, darkest round the eyes and the mouth;
  * the eyes and the mouth black holes, and tar weeping from them: streaks down the cheeks from the eyes, down the chin
    and the throat from the mouth's lower rim;
  * the surplice linen gone grey with filth, stained brown-black up from the strips' ends, the ruff yellowed and limp.
The skin's dull (rough 0.6); the tar wet (0.15).

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
from overbake import bell, fine, smooth01  # noqa: E402

kit, g, arm, parts = overbake.hold("choir.py")
print("[dt] choir parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(g["HC"], np.float32)
HR = np.array(g["HR"], np.float32)
EYE_U, EYE_W, MOUTH_W = g["EYE_U"], g["EYE_W"], g["MOUTH_W"]
HIP = g["HIP"]

DRESS = {
    "skin.choir_face": (lambda: make.flat("choir_face", (0.2, 0.215, 0.235), rough=0.6), 3),
    "skin.choir": (lambda: make.flat("choir_skin", (0.17, 0.185, 0.2), rough=0.6), 2),
    "tar.choir_hole": (lambda: make.flat("choir_hole", (0.004, 0.003, 0.003), rough=0.1), 1),
    "wool.surplice": (lambda: make.flat("surplice", (0.11, 0.105, 0.092), rough=0.9), 2),
    "wool.ruff": (lambda: make.flat("ruff", (0.17, 0.15, 0.11), rough=0.9), 2),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"choir: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def units(p):
    d = (p - HC) / HR
    return d[:, 0], d[:, 1], d[:, 2]


def veins_of(p):
    return smooth01(0.86, 0.96, ridged(p, 1201, 20.0))


def skin_shape(p, n):
    return 0.0004 * veins_of(p) + 0.0003 * (ridged(p, 1202, 90.0) ** 6 - 0.2) + fine(p, 0.0001, 500, 1203)


def linen(p, n):
    d = 0.0025 * (ridged(p * np.array([1.0, 1.0, 0.3], np.float32), 1211, 16.0) ** 2 - 0.3)
    d += 0.00025 * (np.sin(p[:, 2] * 2200) + np.sin((p[:, 0] + p[:, 1]) * 2200))
    return d + fine(p, 0.0003, 420, 1212)


SHAPE = {"skin.choir_face": skin_shape, "skin.choir": skin_shape, "wool.surplice": linen, "wool.ruff": linen,
         "tar.choir_hole": lambda p, n: 0 * p[:, 0]}
BAKED = ["head", "body", "robe"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] choir highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: veins, dark; G: tar weeping from the eyes and the mouth; B: the surplice's stains, up from its ends."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.choir"):
        u, v, w = units(p)
        front = smooth01(0.0, 0.3, v)
        around = 0.0
        for eu in (-EYE_U, EYE_U):
            around = np.maximum(around, smooth01(0.7, 0.25, np.hypot((u - eu) / 0.4, (w - EYE_W) / 0.35)))
        around = np.maximum(around, smooth01(0.9, 0.4, np.hypot(u / 0.45, (w - MOUTH_W) / 0.5)))
        out[:, 0] = np.clip(veins_of(p) * (0.5 + 0.5 * around), 0, 1)
        tar = np.zeros(len(p), np.float32)
        for eu, k in ((-EYE_U, 1.0), (EYE_U, 0.8)):
            x = u - eu - 0.05 * np.sin(w * 6 + eu * 4)
            tar += k * bell(x / 0.07) * smooth01(EYE_W - 0.1, EYE_W - 0.2, w) * smooth01(-0.9, -0.5, w)
        x = u - 0.04 * np.sin(w * 5)
        tar += bell(x / 0.09) * smooth01(MOUTH_W - 0.3, MOUTH_W - 0.4, w)
        out[:, 1] = np.clip(tar * front, 0, 1)
    if kind.startswith("wool.surplice"):
        out[:, 2] = smooth01(HIP + 0.05, HIP - 0.45, p[:, 2]) * (0.6 + 0.4 * cook.noise_np(p, 1221, 8.0))
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.choir_face", "tar.choir_hole")) else 0


atlas = overbake.Atlas("choir", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.006, 0.02), "body": (0.006, 0.02), "robe": (0.01, 0.025)}, height=1.3, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.015, 0.015, 0.018), crease=0.5, ao_floor=0.4, gentle=atlas.masks["robe"])


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.06, 0.06, 0.09), mk[..., 0] * 0.6)      # veins
base = paint(base, (0.008, 0.006, 0.006), mk[..., 1] * 0.9)   # tar weeping
base = paint(base, (0.03, 0.022, 0.016), mk[..., 2] * 0.85)   # the surplice's stains, black with filth down the strips

rough = np.full(base.shape[:2], 0.6, np.float32)
rough[atlas.masks["robe"]] = 0.9
rough = rough - 0.45 * mk[..., 1]
atlas.finish(base, kit, arm, made=make.provenance("choir", "the Choir, modelled over tools/blender/choir.py"), rough=rough)
