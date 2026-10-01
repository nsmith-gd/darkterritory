"""THE STOKER (GDD v1.2 §21 the firebox, App. A.5): what lives in the fire, of tools/blender/stoker.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/stoker.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the char: black, dry, blistered and crazed all over like burnt wood, ash-grey on the high points, the ribs and the
    knuckles standing;
  * the rags of the jacket burnt onto it, the same black, frayed to threads;
  * the teeth brown and cracked.
The fire keeps its own layer, as the Cinder Hound's does: the splits in the skin (ember_crack), the mouth's fire
(ember_core) and the eyes, all glowing the sick green of a fire with a Stoker in it.

    tools/models/build.sh stoker
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

kit, g, arm, parts = overbake.hold("stoker.py")
print("[dt] stoker parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "tar.stoker_face": (lambda: make.flat("stoker_face", (0.022, 0.019, 0.017), rough=0.75), 3),
    "tar.stoker_teeth": (lambda: make.flat("stoker_teeth", (0.12, 0.09, 0.055), rough=0.5), 1),
    "tar.stoker": (lambda: make.flat("stoker_char", (0.018, 0.016, 0.014), rough=0.8), 2),
    "wool.stoker_rags": (lambda: make.flat("stoker_rags", (0.02, 0.018, 0.015), rough=0.95), 2),
}
# The fire keeps its own layer: the splits, the mouth's fire, the eyes.
KEEP = ("ember_crack", "ember_core", "eye.")


def dress(m):
    if m.name.startswith(KEEP):
        return None
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"stoker: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def char(p, n):
    """Burnt wood's crazing: a fine network of cracks, blisters between them."""
    crack = smooth01(0.9, 0.97, ridged(p, 1501, 60.0))
    return -0.0012 * crack + 0.0009 * np.maximum(0, cook.noise_np(p, 1502, 45.0)) ** 2 + fine(p, 0.00015, 500, 1503)


def rags(p, n):
    return 0.0012 * cook.noise_np(p * np.array([1.0, 1.0, 0.3], np.float32), 1511, 20.0) + fine(p, 0.0002, 420, 1512)


SHAPE = {"tar.stoker_face": char, "tar.stoker_teeth": lambda p, n: fine(p, 0.0002, 400, 1504), "tar.stoker": char, "wool.stoker_rags": rags}
BAKED = ["head", "body", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] stoker highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the ash on the char's high points and crazing; G: the rags' scorching."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("tar.stoker") and not kind.startswith("tar.stoker_teeth"):
        out[:, 0] = np.clip(0.5 * smooth01(0.3, 0.8, cook.noise_np(p, 1521, 9.0)) + 0.5 * smooth01(0.9, 0.97, ridged(p, 1501, 60.0)), 0, 1)
    if kind.startswith("wool.stoker"):
        out[:, 1] = np.clip(smooth01(0.0, 0.7, cook.noise_np(p, 1522, 6.0)), 0, 1)
    return out


FACE = 1


def kind_of(m):
    if m.name.startswith(KEEP):
        return overbake.Atlas.KEEP
    return FACE if m.name.startswith(("tar.stoker_face", "tar.stoker_teeth")) else 0


atlas = overbake.Atlas("stoker", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.4})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.006, 0.02), "body": (0.008, 0.02), "limbs": (0.006, 0.018)}, height=1.0, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.008, 0.007, 0.006), crease=0.4, ao_floor=0.5)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.075, 0.07, 0.065), mk[..., 0] * 0.5)    # ash on the char
base = paint(base, (0.035, 0.022, 0.012), mk[..., 1] * 0.5)   # the rags scorched brown
atlas.finish(base, kit, arm, made=make.provenance("stoker", "the Stoker, modelled over tools/blender/stoker.py"))
