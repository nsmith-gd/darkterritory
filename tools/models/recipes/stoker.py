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


def cells(p, scale, seed):
    """F2 - F1 of a jittered grid's points (tools/blender/stoker.py's `cells`, the same hash): 0 on a block's edge."""
    q = p.astype(np.float64) * scale
    b = np.floor(q)
    f1 = np.full(len(q), 9.0)
    f2 = np.full(len(q), 9.0)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = b + np.array((dx, dy, dz), np.float64)
                h = [np.sin(c[:, 0] * 127.1 + c[:, 1] * 311.7 + c[:, 2] * 74.7 + seed * 13.13 + k * 91.3) * 43758.5453 for k in range(3)]
                fp = c + np.stack([x - np.floor(x) for x in h], axis=1)
                d = np.linalg.norm(q - fp, axis=1)
                f2 = np.where(d < f1, f1, np.minimum(f2, d))
                f1 = np.minimum(f1, d)
    return (f2 - f1).astype(np.float32)


CELL, CELL_SEED = g["CELL"], g["CELL_SEED"]


def char(p, n):
    """Burnt wood's alligatoring (note 536): the game mesh's blocks (the same cells) cut sharp, their edges a narrow deep
    crack, each block's top domed and crazed finer still, blistered."""
    e = cells(p, CELL, CELL_SEED)
    crack = 1 - smooth01(0.0, 0.07, e)
    dome = smooth01(0.05, 0.5, e)
    craze = 1 - smooth01(0.0, 0.05, cells(p, CELL * 3.2, CELL_SEED + 1))
    return (-0.0028 * crack + 0.0012 * dome - 0.0006 * craze * dome + 0.0005 * np.maximum(0, cook.noise_np(p, 1502, 45.0)) ** 2
            + fine(p, 0.00015, 500, 1503))


def rags(p, n):
    return 0.0012 * cook.noise_np(p * np.array([1.0, 1.0, 0.3], np.float32), 1511, 20.0) + fine(p, 0.0002, 420, 1512)


SHAPE = {"tar.stoker_face": char, "tar.stoker_teeth": lambda p, n: fine(p, 0.0002, 400, 1504), "tar.stoker": char, "wool.stoker_rags": rags}
BAKED = ["head", "body", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] stoker highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the ash on the char's blocks; G: the rags' scorching; B: the cracks between the blocks."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("tar.stoker") and not kind.startswith("tar.stoker_teeth"):
        # Ash grey on the blocks' domed tops where they stand proudest; the cracks between them black.
        e = cells(p, CELL, CELL_SEED)
        out[:, 0] = np.clip(0.35 * smooth01(0.3, 0.8, cook.noise_np(p, 1521, 9.0)) + 0.65 * smooth01(0.35, 0.75, e), 0, 1)
        out[:, 2] = 1 - smooth01(0.0, 0.08, e)
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


base = paint(base, (0.11, 0.1, 0.092), mk[..., 0] * 0.75)    # ash on the char's blocks
base = paint(base, (0.035, 0.022, 0.012), mk[..., 1] * 0.5)   # the rags scorched brown
base = paint(base, (0.004, 0.0035, 0.003), mk[..., 2] * 0.9)  # the cracks black (the lit ones are their own faces)
# Dry and matte all over (burnt wood has no sheen but on a crack's glassy edge), so the fire's light doesn't slide off it
# like oilcloth.
atlas.finish(base, kit, arm, rough=0.93 - 0.2 * mk[..., 2], made=make.provenance("stoker", "the Stoker, modelled over tools/blender/stoker.py"))
