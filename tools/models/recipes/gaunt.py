"""THE GAUNT (GDD v1.2 §21 asleep in villages and yards, App. A.6): the thing on stilts of tools/blender/gaunt.py, taken
to the fidelity target (ARCHITECTURE §8 note 58, tools/models overbake; the non-human redo, note 130).

tools/blender/gaunt.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the legs and the body the grey of old wood, ridged and split like bark, lichen in the splits, the knees' knots
    darker and burred: asleep it's a heap of dead branches;
  * the skull bone-pale and dry, cracked finely, the hollows where eyes would be grimed dark;
  * the ears thin membrane, a dull flesh-grey with the veins standing in them red-brown, a faint wet sheen on them
    (the only skin of it that's alive-looking);
  * the lip's pucker red and wet, the mouth's hole black and wet; the spikes horn, dark and polished by the ground.
Dry (rough 0.85) but the ears (0.45), the lip and the mouth (0.1) and the spikes (0.35).

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
    "skin.gaunt_face": (lambda: make.flat("gaunt_face", (0.2, 0.19, 0.17), rough=0.8), 3),
    "skin.gaunt_knot": (lambda: make.flat("gaunt_knot", (0.085, 0.08, 0.072), rough=0.9), 2),
    "skin.gaunt_ear": (lambda: make.flat("gaunt_ear", (0.16, 0.12, 0.11), rough=0.45), 3),
    "skin.gaunt_rim": (lambda: make.flat("gaunt_rim", (0.16, 0.04, 0.035), rough=0.15), 3),
    "skin.gaunt": (lambda: make.flat("gaunt_bark", (0.12, 0.115, 0.105), rough=0.85), 2),
    "tar.gaunt_nail": (lambda: make.flat("gaunt_nail", (0.035, 0.03, 0.024), rough=0.35), 1),
    "tar.gaunt_mouth": (lambda: make.flat("gaunt_mouth", (0.01, 0.005, 0.005), rough=0.1), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"gaunt: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


# Bark's grain runs along the limb: stretched noise, the legs' rest pose near enough upright and the body's along Y.
GRAIN_UP = np.array([1, 1, 0.22], np.float32)
GRAIN_AHEAD = np.array([1, 0.22, 1], np.float32)


def grain(p):
    """0..1 in the bark's splits."""
    up = p[:, 2:3] < 1.85     # the legs below the body: their grain up; the body's and the neck's along it
    q = np.where(up, p * GRAIN_UP, p * GRAIN_AHEAD)
    return np.clip(smooth01(0.8, 0.95, ridged(q, 1301, 34.0)) + 0.5 * smooth01(0.86, 0.97, ridged(q, 1302, 80.0)), 0, 1)


def cracks_of(p, scale=60.0):
    return np.clip(smooth01(0.9, 0.98, ridged(p, 1303, scale)) + 0.5 * smooth01(0.92, 0.99, ridged(p, 1304, scale * 2.3)), 0, 1)


def veins_of(p):
    return np.clip(smooth01(0.9, 0.975, ridged(p * np.array([1, 1, 0.6], np.float32), 1305, 22.0)) +
                   0.6 * smooth01(0.93, 0.985, ridged(p, 1306, 55.0)), 0, 1)


def bark_shape(p, n):
    return -0.003 * grain(p) + 0.0015 * cook.noise_np(p, 1307, 10.0) + fine(p, 0.0002, 400, 1308)


def knot_shape(p, n):
    # Burred: whorls, lumps.
    return 0.003 * cook.noise_np(p, 1309, 30.0) - 0.002 * smooth01(0.8, 0.95, ridged(p, 1310, 50.0)) + fine(p, 0.0002, 400, 1311)


SHAPE = {"skin.gaunt_face": lambda p, n: -0.0007 * cracks_of(p) + 0.0004 * cook.noise_np(p, 1312, 25.0) + fine(p, 0.0001, 600, 1313),
         "skin.gaunt_knot": knot_shape,
         "skin.gaunt_ear": lambda p, n: 0.0006 * veins_of(p) + fine(p, 0.00006, 900, 1314),
         "skin.gaunt_rim": lambda p, n: fine(p, 0.0001, 500, 1315),
         "skin.gaunt": bark_shape,
         "tar.gaunt": lambda p, n: fine(p, 0.00008, 600, 1316)}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] gaunt highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the bark's splits and the skull's cracks, dark; G: lichen in the splits and in blotches; B: the ears' veins."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.gaunt") and not kind.startswith(("skin.gaunt_ear", "skin.gaunt_rim")):
        c = cracks_of(p) if kind.startswith("skin.gaunt_face") else grain(p)
        out[:, 0] = c
        if not kind.startswith("skin.gaunt_face"):
            out[:, 1] = np.clip(c * smooth01(0.0, 0.6, cook.noise_np(p, 1321, 3.0)) + 0.7 * smooth01(0.5, 0.85, cook.noise_np(p, 1322, 6.0)), 0, 1)
    if kind.startswith("skin.gaunt_ear"):
        out[:, 2] = veins_of(p)
    return out


def gloss(p, kind):
    """R: the roughness: dry wood and bone; the ears' sheen; the wet lip and mouth; the polished spikes."""
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.45 if kind.startswith("skin.gaunt_ear") else 0.1 if kind.startswith(("skin.gaunt_rim", "tar.gaunt_mouth"))
                 else 0.35 if kind.startswith("tar.gaunt_nail") else 0.8 if kind.startswith("skin.gaunt_face") else 0.88)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.gaunt_face", "skin.gaunt_rim", "skin.gaunt_ear", "tar.gaunt_mouth")) else 0


atlas = overbake.Atlas("gaunt", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.006, 0.02) for name in BAKED}, height=3.0, masks={"marks": marks, "gloss": gloss})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.015, 0.014, 0.012), crease=0.55, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.035, 0.032, 0.028), mk[..., 0] * 0.7)    # the splits, the cracks
base = paint(base, (0.09, 0.1, 0.075), mk[..., 1] * 0.45)      # lichen, grey-green
base = paint(base, (0.09, 0.025, 0.02), mk[..., 2] * 0.8)      # the ears' veins

rough = atlas.maps["gloss"][..., 0]
atlas.finish(base, kit, arm, made=make.provenance("gaunt", "the Gaunt, modelled over tools/blender/gaunt.py"), rough=rough)
