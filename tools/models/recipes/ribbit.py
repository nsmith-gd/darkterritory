"""RIBBITS (GDD v1.2 §21 yards and villages, App. A.6): the toad-rabbit of tools/blender/ribbit.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/ribbit.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin grey-white and wet as something that lives under a stone, warted down the back, the warts darker, blotched
    with grey-green; the belly paler, loose, creased where it hangs;
  * the bare ears thin enough to show their veins, pinkish; the throat's sac paler still and veined, stretched shiny;
  * the eyes milky, grey-green, a toad's flat bar of pupil clouded over in each; the gums dark red, wet; the teeth a
    row of human teeth, flat and square, yellowed, brown at the roots.
Wet all over (rough 0.25); the eyes wetter.

    tools/models/build.sh ribbit
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

kit, g, arm, parts = overbake.hold("ribbit.py")
print("[dt] ribbit parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = {s: np.array(v, np.float32) for s, v in g["EYES"].items()}
THROAT = np.array(g["THROAT"], np.float32)

DRESS = {
    "skin.ribbit_belly": (lambda: make.flat("ribbit_belly", (0.3, 0.25, 0.24), rough=0.3), 3),
    "skin.ribbit_ear": (lambda: make.flat("ribbit_ear", (0.3, 0.2, 0.19), rough=0.35), 3),
    "skin.ribbit_teeth": (lambda: make.flat("ribbit_teeth", (0.45, 0.4, 0.3), rough=0.4), 0),
    "skin.ribbit": (lambda: make.flat("ribbit_skin", (0.22, 0.18, 0.175), rough=0.3), 3),
    "glass_dirty.ribbit_eye": (lambda: make.flat("ribbit_eye", (0.3, 0.31, 0.27), rough=0.05), 2),
    "flesh.ribbit_gum": (lambda: make.flat("ribbit_gum", (0.05, 0.015, 0.015), rough=0.2), 2),
    "flesh.ribbit_tongue": (lambda: make.flat("ribbit_tongue", (0.2, 0.07, 0.07), rough=0.12), 2),
    "tar.ribbit_mouth": (lambda: make.flat("ribbit_mouth", (0.02, 0.006, 0.006), rough=0.1), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"ribbit: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def warts_of(p):
    """The warts: cells raised on the back and the haunches, few on the belly."""
    top = smooth01(0.28, 0.5, p[:, 2])
    return smooth01(0.6, 0.9, cook.noise_np(p, 1001, 60.0) * 0.5 + 0.5) * (0.3 + 0.7 * top)


def skin_shape(p, n):
    # Wrinkled like a hairless rat's: folds across, deep; small warts over the back.
    d = 0.002 * warts_of(p) + 0.0016 * (ridged(p * np.array([1.0, 0.35, 1.0], np.float32), 1002, 45.0) ** 3 - 0.3)
    return d + fine(p, 0.00015, 500, 1003)


def belly_shape(p, n):
    # Loose skin, creased across where it hangs.
    return 0.0012 * np.sin(p[:, 1] * 140 + 4 * cook.noise_np(p, 1011, 6.0)) + fine(p, 0.0001, 500, 1012)


def ear_shape(p, n):
    return 0.0006 * veins_of(p) + fine(p, 0.0001, 500, 1021)


def veins_of(p):
    return smooth01(0.84, 0.95, ridged(p, 1031, 22.0))


SHAPE = {"skin.ribbit_belly": belly_shape, "skin.ribbit_ear": ear_shape, "skin.ribbit_teeth": lambda p, n: 0 * p[:, 0],
         "skin.ribbit": skin_shape}
BAKED = ["body", "jaw", "head", "paws"]   # (the legs and the skull grown into the body, the jaw its own skin: rig.fuse)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] ribbit highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the warts (darker); G: grey-green blotching on the back; B: veins (the ears, the sac)."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.ribbit") and not kind.startswith("skin.ribbit_teeth"):
        out[:, 0] = warts_of(p) * (not kind.startswith("skin.ribbit_belly"))
        out[:, 1] = smooth01(0.1, 0.7, cook.noise_np(p, 1041, 5.0)) * smooth01(0.25, 0.5, p[:, 2])
        sac = np.linalg.norm((p - THROAT) / np.array([0.12, 0.115, 0.07], np.float32), axis=1) < 1.3
        out[:, 2] = veins_of(p) * (kind.startswith("skin.ribbit_ear") | sac)
    return out


def eyes_and_teeth(p, kind):
    """R: the pupils, clouded bars; G: the teeth's roots, brown; B: nothing."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("glass_dirty.ribbit_eye"):
        for e in EYES.values():
            rel = p - e
            r = np.linalg.norm(rel, axis=1)
            near = r < 0.05
            d = rel / np.maximum(r, 1e-6)[:, None]
            # Looking out and forward; a toad's pupil, a flat bar across, clouded over.
            look = np.array([np.sign(e[0]) * 0.75, 0.55, 0.35], np.float32)
            look /= np.linalg.norm(look)
            right = np.cross(look, np.array([0, 0, 1], np.float32))
            right /= np.linalg.norm(right)
            up = np.cross(right, look)
            u, v, c = d @ right, d @ up, d @ look
            bar = (c > 0) & ((u / 0.5) ** 2 + (v / 0.17) ** 2 < 1)
            out[:, 0] = np.maximum(out[:, 0], (near & bar) * 0.75)
    if kind.startswith("skin.ribbit_teeth"):
        out[:, 1] = smooth01(0.6, 0.592, p[:, 2])
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("glass_dirty.ribbit_eye", "skin.ribbit_teeth", "flesh.ribbit_gum", "flesh.ribbit_tongue")) else 0


atlas = overbake.Atlas("ribbit", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "jaw": (0.006, 0.02), "head": (0.006, 0.02), "paws": (0.004, 0.012)}, height=1.2,
           masks={"marks": marks, "eyes": eyes_and_teeth})

M = atlas.maps
mk, ey = M["marks"], M["eyes"]
base = atlas.base(soot=(0.02, 0.02, 0.018), crease=0.5, ao_floor=0.4)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.13, 0.1, 0.11), mk[..., 1] * 0.3)       # bruised blotching
base = paint(base, (0.11, 0.09, 0.085), mk[..., 0] * 0.35)    # the warts, a little darker
base = paint(base, (0.2, 0.08, 0.09), mk[..., 2] * 0.55)      # veins
base = paint(base, (0.008, 0.008, 0.01), ey[..., 0])          # pupils
base = paint(base, (0.12, 0.07, 0.03), ey[..., 1] * 0.7)      # teeth's roots

rough = np.full(base.shape[:2], 0.25, np.float32)
rough = rough + 0.15 * mk[..., 0]
atlas.finish(base, kit, arm, made=make.provenance("ribbit", "the Ribbits, modelled over tools/blender/ribbit.py"), rough=rough, lod=0.4)
