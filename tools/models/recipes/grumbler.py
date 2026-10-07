"""THE GRUMBLER (GDD v1.2 §21 facility cranes, App. A.8): the labourer of tools/blender/grumbler.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/grumbler.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin a sick grey-green, blotched and veined, the knuckles and the knees scabbed dark; round the split face raw
    and red-brown, and wet;
  * the clothes a labourer's, worn through: the shirt gone grey-yellow, sweat-stained at the collar and under the arms,
    the waistcoat and the trousers black with grease and coal dust, frayed; the cap felted with filth;
  * the eyes rolled up white and wet; the teeth yellow-brown, the gums black.
Skin 0.6, cloth 0.9, the eyes and the mouth wet (0.1).

    tools/models/build.sh grumbler
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

kit, g, arm, parts = overbake.hold("grumbler.py")
print("[dt] grumbler parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(g["HC"], np.float32)
HR = np.array(g["HR"], np.float32)
SLIT_W = g["SLIT_W"]

DRESS = {
    "skin.grumbler_face": (lambda: make.flat("grumbler_face", (0.18, 0.18, 0.145), rough=0.55), 3),
    "skin.grumbler_teeth": (lambda: make.flat("grumbler_teeth", (0.3, 0.25, 0.15), rough=0.4), 1),
    "skin.grumbler": (lambda: make.flat("grumbler_skin", (0.17, 0.17, 0.13), rough=0.6), 2),
    "glass_dirty.grumbler_eye": (lambda: make.flat("grumbler_eye", (0.42, 0.4, 0.34), rough=0.1), 2),
    "tar.grumbler_mouth": (lambda: make.flat("grumbler_mouth", (0.02, 0.006, 0.005), rough=0.15), 1),
    "tar.grumbler_nail": (lambda: make.flat("grumbler_nail", (0.04, 0.032, 0.022), rough=0.5), 1),
    "wool.grumbler_shirt": (lambda: make.flat("grumbler_shirt", (0.045, 0.055, 0.07), rough=0.9), 2),
    "wool.grumbler_waistcoat": (lambda: make.flat("grumbler_waistcoat", (0.045, 0.04, 0.034), rough=0.85), 2),
    "wool.grumbler_trousers": (lambda: make.flat("grumbler_trousers", (0.032, 0.03, 0.027), rough=0.9), 2),
    "wool.grumbler_cap": (lambda: make.flat("grumbler_cap", (0.065, 0.058, 0.046), rough=0.95), 2),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"grumbler: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def skin_shape(p, n):
    return 0.0005 * smooth01(0.86, 0.96, ridged(p, 1401, 22.0)) + 0.0004 * cook.noise_np(p, 1402, 30.0) + fine(p, 0.00012, 500, 1403)


def cloth(p, n):
    d = 0.0018 * (ridged(p * np.array([1.0, 1.0, 0.4], np.float32), 1411, 14.0) ** 2 - 0.3)
    d += 0.0002 * (np.sin(p[:, 2] * 2400) + np.sin((p[:, 0] + p[:, 1]) * 2400))
    return d + fine(p, 0.00025, 420, 1412)


SHAPE = {"skin.grumbler_face": skin_shape, "skin.grumbler_teeth": lambda p, n: fine(p, 0.0002, 400, 1404), "skin.grumbler": skin_shape,
         "wool.grumbler": cloth}
BAKED = ["head", "body", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] grumbler highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def units(p):
    d = (p - HC) / HR
    return d[:, 0], d[:, 1], d[:, 2]


def marks(p, kind):
    """R: blotches and veins on the skin, grime on the cloth; G: raw and wet round the split face; B: the stains (the
    shirt's collar and armpits, the grease on the trousers' knees)."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.grumbler") and not kind.startswith("skin.grumbler_teeth"):
        out[:, 0] = np.clip(0.6 * smooth01(0.2, 0.7, cook.noise_np(p, 1421, 6.0)) + 0.7 * smooth01(0.88, 0.97, ridged(p, 1422, 24.0)), 0, 1)
        if kind.startswith("skin.grumbler_face"):
            u, v, w = units(p)
            slit = SLIT_W + 0.18 * u * u
            out[:, 1] = np.clip(bell((w - slit) / 0.16) * smooth01(1.1, 0.8, np.abs(u)) * smooth01(-0.4, 0.0, v), 0, 1)
    if kind.startswith("wool.grumbler"):
        out[:, 0] = np.clip(0.5 + 0.5 * cook.noise_np(p, 1431, 5.0), 0, 1)
        out[:, 2] = np.clip(smooth01(0.3, 0.8, cook.noise_np(p, 1432, 3.0)), 0, 1)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.grumbler_face", "skin.grumbler_teeth", "glass_dirty.grumbler_eye", "tar.grumbler_mouth")) else 0


atlas = overbake.Atlas("grumbler", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.4})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.006, 0.02), "body": (0.01, 0.025), "limbs": (0.008, 0.02)}, height=1.0, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.02, 0.018, 0.014), crease=0.55, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.07, 0.075, 0.055), mk[..., 0] * 0.45)   # blotches and veins; grime on the cloth
base = paint(base, (0.16, 0.035, 0.025), mk[..., 1] * 0.85)   # raw round the split
base = paint(base, (0.02, 0.018, 0.014), mk[..., 2] * 0.5)    # grease and sweat

rough = np.full(base.shape[:2], 0.6, np.float32)
rough = rough - 0.45 * mk[..., 1]
atlas.finish(base, kit, arm, made=make.provenance("grumbler", "the Grumbler, modelled over tools/blender/grumbler.py"), rough=rough)
