"""THE FOLLOWERS (GDD v1.2 §21 facility grounds, App. A.6): the tick of tools/blender/follower.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake; the non-human redo, note 133).

tools/blender/follower.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the sac's leather grey-white, blue-grey where it's stretched thinnest over the top, mottled darker, the folds across
    it creased dark, a sheen on it;
  * the shield and the palps a hard red-brown, pitted, glossy;
  * the eyes black glass, wet; the beak dark red;
  * the legs red-brown, banded paler at the joints.

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
    "skin.follower_shield": (lambda: make.flat("follower_shield", (0.07, 0.03, 0.022), rough=0.25), 2),
    "skin.follower_leg": (lambda: make.flat("follower_leg", (0.055, 0.03, 0.022), rough=0.35), 1),
    "skin.follower": (lambda: make.flat("follower_sac", (0.34, 0.34, 0.33), rough=0.3), 2),
    "tar.follower_eye": (lambda: make.flat("follower_eye", (0.004, 0.004, 0.005), rough=0.03), 1),
    "tar.follower_mouth": (lambda: make.flat("follower_mouth", (0.05, 0.012, 0.01), rough=0.2), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"follower: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


SAC = np.array(g["SAC"], np.float32)
SR = np.array(g["SR"], np.float32)


def sac_shape(p, n):
    # Leather: fine wrinkles, pores.
    return 0.00025 * smooth01(0.85, 0.97, ridged(p, 1601, 500.0)) - 0.0002 * smooth01(0.9, 0.99, ridged(p, 1602, 900.0)) + fine(p, 0.00002, 3000, 1603)


def shield_shape(p, n):
    # Pitted.
    return -0.00025 * smooth01(0.6, 0.9, cook.noise_np(p, 1604, 900.0)) + fine(p, 0.00002, 3000, 1605)


SHAPE = {"skin.follower_shield": shield_shape, "skin.follower_leg": lambda p, n: fine(p, 0.00002, 3000, 1606), "skin.follower": sac_shape,
         "tar.follower": lambda p, n: 0 * p[:, 0]}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] follower highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: mottling and the folds' creases; G: the stretched blue-grey over the top; B: the legs' pale bands."""
    out = np.zeros((len(p), 3), np.float32)
    if kind == "skin.follower" or kind.startswith("skin.follower."):
        d = (p - SAC) / SR
        # (The folds are tools/blender/follower.py's: sin(y * 140) over the sac's middle; their troughs creased.)
        crease = smooth01(0.85, 1.0, np.abs(np.cos(d[:, 1] * SR[1] * 140))) * smooth01(-0.2, 0.4, d[:, 2])
        out[:, 0] = np.maximum(0.6 * smooth01(0.2, 0.8, cook.noise_np(p, 1611, 80.0)), 0.7 * crease)
        out[:, 1] = smooth01(0.2, 0.9, d[:, 2])
    if kind.startswith("skin.follower_leg"):
        out[:, 2] = smooth01(0.7, 0.95, np.abs(np.sin(np.linalg.norm(p[:, :2], axis=1) * 90)))
    return out


def gloss(p, kind):
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.03 if kind.startswith("tar.follower_eye") else 0.25 if kind.startswith("skin.follower_shield") else
                 0.35 if kind.startswith("skin.follower_leg") else 0.2 if kind.startswith("tar.follower_mouth") else 0.3)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("skin.follower_shield", "tar.follower_eye", "tar.follower_mouth")) else 0


atlas = overbake.Atlas("follower", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 1.8})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.0008, 0.003) for name in BAKED}, height=0.12, masks={"marks": marks, "gloss": gloss})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.012, 0.011, 0.01), crease=0.55, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.22, 0.25, 0.3), mk[..., 1] * 0.45)      # stretched thin and blue over the top
base = paint(base, (0.08, 0.07, 0.07), mk[..., 0] * 0.6)      # mottling, the creases
base = paint(base, (0.16, 0.11, 0.08), mk[..., 2] * 0.6)      # the legs' bands
rough = atlas.maps["gloss"][..., 0]
atlas.finish(base, kit, arm, made=make.provenance("follower", "the Follower, modelled over tools/blender/follower.py"), rough=rough)
