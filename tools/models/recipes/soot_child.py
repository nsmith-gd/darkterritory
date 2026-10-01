"""SOOT CHILDREN (GDD v1.2 §21 outside, App. A.6): the child of tools/blender/soot_child.py, taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/soot_child.py stays its source: the rig, the clips, the two variants (the real child and the Soot Child)
and the game mesh. This recipe runs it, models a high-resolution copy and bakes it into one 1024 atlas:
  * the skin waxy-pale, grimed, soot run down it in streaks from the hair and the eyes, the knees and the shins black
    with ash where it's knelt;
  * the shirt an adult's gone to rag-grey, stained darker down the front and along the hem;
  * the hair matted with ash and grease;
  * a Soot Child's hands and feet black and wet as tar (their own parts: the variant), its eyes the same black; the real
    child's eyes a child's, the iris brown.
Before this the Soot Children were a sourced scan (the Boy Room's boy, CC BY 4.0; tools/models/figures.py), posed into
the huddle and rigged on it, which could rock and lift its head but never get up: note 125.

    tools/models/build.sh soot_child
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

kit, g, arm, parts = overbake.hold("soot_child.py")
print("[dt] soot_child parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "flesh.soot_child_lips": (lambda: make.flat("soot_child_lips", (0.16, 0.1, 0.1), rough=0.45), 2),
    "flesh.soot_child_teeth": (lambda: make.flat("soot_child_teeth", (0.36, 0.32, 0.22), rough=0.4), 0),
    "flesh.soot_child_eye": (lambda: make.flat("soot_child_eye_white", (0.5, 0.47, 0.42), rough=0.1), 2),
    "flesh.soot_child": (lambda: make.flat("soot_child_skin", (0.24, 0.215, 0.195), rough=0.55), 2),
    "wool.soot_child_shirt": (lambda: make.flat("soot_child_shirt", (0.085, 0.08, 0.072), rough=0.95), 2),
    "rope.soot_child_string": (lambda: make.flat("soot_child_string", (0.05, 0.036, 0.022), rough=0.9), 0),
    "tar.soot_child_hair": (lambda: make.flat("soot_child_hair", (0.008, 0.007, 0.006), rough=0.5), 1),
    "tar.soot_child_mouth": (lambda: make.flat("soot_child_mouth", (0.006, 0.002, 0.002), rough=0.3), 0),
    "tar.soot_child_iris": (lambda: make.flat("soot_child_iris", (0.05, 0.025, 0.012), rough=0.1), 1),
    "tar.soot_child_eye": (lambda: make.flat("soot_child_eye_black", (0.002, 0.002, 0.002), rough=0.03), 2),
    "tar.soot_child_soot": (lambda: make.flat("soot_child_soot", (0.006, 0.005, 0.005), rough=0.15), 2),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"soot_child: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def skin(p, n):
    return 0.00012 * cook.noise_np(p, 1901, 200.0) + fine(p, 0.00004, 2500, 1902)


def cloth(p, n):
    # Rag: the weave, the wear-holes' lips, slack folds.
    return 0.0008 * cook.noise_np(p * np.array([1, 1, 0.4], np.float32), 1903, 25.0) + fine(p, 0.00015, 600, 1904)


def hair(p, n):
    # Matted: clumped, stringy down its length.
    return 0.0015 * smooth01(0.6, 0.95, ridged(p * np.array([1, 1, 0.3], np.float32), 1905, 90.0)) + fine(p, 0.0002, 900, 1906)


def soot(p, n):
    # Tar-thick, lumped and run.
    return 0.0006 * np.maximum(0, cook.noise_np(p, 1907, 120.0)) + fine(p, 0.00005, 2000, 1908)


SHAPE = {"flesh.soot_child_lips": skin, "flesh.soot_child_teeth": lambda p, n: 0 * p[:, 0], "flesh.soot_child_eye": lambda p, n: 0 * p[:, 0],
         "flesh.soot_child": skin, "wool.soot_child": cloth, "rope.soot_child": lambda p, n: fine(p, 0.0003, 800, 1909), "tar.soot_child_hair": hair,
         "tar.soot_child_mouth": lambda p, n: 0 * p[:, 0], "tar.soot_child_iris": lambda p, n: 0 * p[:, 0], "tar.soot_child_eye": lambda p, n: 0 * p[:, 0],
         "tar.soot_child_soot": soot}
BAKED = sorted(parts)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] soot_child highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

# Where tools/blender/soot_child.py has the head and eyes at rest (its HC, HR, EYE_U, EYE_W).
HC = np.array([0.0, 0.012, 1.075], np.float32)
HR = np.array([0.079, 0.086, 0.09], np.float32)


def marks(p, kind):
    """R: soot streaked down the skin (from the hair and the eyes, and the knees and shins ashed); G: the shirt stained
    (down the front, along the hem); B: grime in the skin."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("flesh.soot_child") and not kind.startswith(("flesh.soot_child_eye", "flesh.soot_child_teeth")):
        u, w = (p[:, 0] - HC[0]) / HR[0], (p[:, 2] - HC[2]) / HR[2]
        front = p[:, 1] > HC[1]
        # Runs down from under the eyes and from the hairline, in streaks.
        streak = smooth01(0.55, 0.95, np.abs(np.sin(p[:, 0] * 260 + 0.7 * cook.noise_np(p, 1911, 10.0))))
        under_eyes = sum(smooth01(0.22, 0.05, np.abs(u - eu)) for eu in (-0.4, 0.4)) * smooth01(0.1, -0.15, w) * smooth01(-0.9, -0.3, w) * front
        hairline = smooth01(0.35, 0.65, w) * front
        out[:, 0] = np.clip(streak * (under_eyes * 0.9 + hairline * 0.6), 0, 1)
        knees = smooth01(0.42, 0.3, np.abs(p[:, 2] - 0.3)) * smooth01(0.0, 0.03, p[:, 1])
        out[:, 0] = np.maximum(out[:, 0], np.clip(knees * smooth01(-0.2, 0.6, cook.noise_np(p, 1912, 20.0)), 0, 1))
        out[:, 2] = np.clip(0.6 * smooth01(0.1, 0.8, cook.noise_np(p, 1913, 30.0)), 0, 1)
    if kind.startswith("wool.soot_child"):
        front = smooth01(0.0, 0.06, p[:, 1])
        out[:, 1] = np.clip(0.5 * front * smooth01(0.0, 0.7, cook.noise_np(p, 1914, 9.0)) + 0.6 * smooth01(0.48, 0.36, p[:, 2]) +
                            0.3 * smooth01(0.2, 0.9, cook.noise_np(p, 1915, 30.0)), 0, 1)
    return out


FACE = 1


def kind_of(m):
    return FACE if m.name.startswith(("flesh.soot_child", "tar.soot_child_eye", "tar.soot_child_iris", "tar.soot_child_mouth")) else 0


atlas = overbake.Atlas("soot_child", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.2})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.002, 0.008) for name in BAKED}, height=1.2, masks={"marks": marks})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.012, 0.011, 0.01), crease=0.45, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.08, 0.075, 0.07), mk[..., 2] * 0.35)     # grime
base = paint(base, (0.012, 0.011, 0.01), mk[..., 0] * 0.85)    # soot, run down it
base = paint(base, (0.035, 0.03, 0.025), mk[..., 1] * 0.55)    # the shirt's stains
atlas.finish(base, kit, arm, made=make.provenance("soot_child", "the Soot Child, modelled over tools/blender/soot_child.py"))
