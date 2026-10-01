"""FIRE FLIES (GDD v1.2 §21 lit cars, App. A.5): the moth of tools/blender/fire_fly.py, taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/fire_fly.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 512 atlas (a moth: there are a lot of them, and none of them is big):
  * the fur ash: grey, matted, darker in its roots;
  * the wings charred paper, mottled and veined, scorched darker towards their smouldering edges, and on each forewing
    an eye: a human one, the white of it gone yellow, the iris dark brown, a wet black pupil and the glint of the lamp in
    it, lidded on the side towards the head (folded, the wings put the two side by side, looking up out of the moth);
  * the abdomen's plates black and glossy.
The fire keeps its own layer, as the Stoker's and the Cinder Hound's do: the joins (ember_crack), the coal at its tail
and the wings' rims (ember_core), the eyes on its head.

    tools/models/build.sh fire_fly
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

kit, g, arm, parts = overbake.hold("fire_fly.py")
print("[dt] fire_fly parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "wool.firefly": (lambda: make.flat("firefly_fur", (0.11, 0.1, 0.09), rough=0.95), 2),
    "tar.firefly_wing": (lambda: make.flat("firefly_wing", (0.05, 0.045, 0.04), rough=0.85), 4),
    "tar.firefly_hindwing": (lambda: make.flat("firefly_wing", (0.05, 0.045, 0.04), rough=0.85), 2),
    "tar.firefly": (lambda: make.flat("firefly_plate", (0.012, 0.01, 0.009), rough=0.3), 1),
}
KEEP = ("ember_crack", "ember_core", "eye.")


def dress(m):
    if m.name.startswith(KEEP):
        return None
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"fire_fly: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def fur(p, n):
    # Matted tufts.
    return 0.00035 * cook.noise_np(p, 1701, 700.0) + fine(p, 0.00008, 3000, 1702)


def wing(p, n):
    # The veins standing (ridged lines across the membrane), the paper's buckle.
    return 0.00006 * smooth01(0.93, 0.99, ridged(p, 1711, 260.0)) + 0.00004 * cook.noise_np(p, 1712, 500.0)


SHAPE = {"wool.firefly": fur, "tar.firefly_wing": wing, "tar.firefly_hindwing": wing, "tar.firefly": lambda p, n: fine(p, 0.00003, 3000, 1703)}
BAKED = ["moth"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] fire_fly highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

# The forewings' eyes (the right one's; the left mirrored), where tools/blender/fire_fly.py puts its wing at rest: out
# along X, the chord along Y. The eye's long axis runs along the chord (folded, across the moth), its upper lid
# towards the wing's root (folded, towards the head).
EYE_AT = np.array([0.043, 0.0035], np.float32)
EYE_HALF = np.array([0.0062, 0.0115], np.float32)     # (across the span, along the chord)
IRIS, PUPIL = 0.0042, 0.0017


def eye_marks(p):
    """R: the white; G: the iris; B: the pupil and the lid's dark line round it."""
    out = np.zeros((len(p), 3), np.float32)
    x, y = np.abs(p[:, 0]) - EYE_AT[0], p[:, 1] - EYE_AT[1]
    # An almond: two arcs meeting at the corners, the upper (root, -x) lid fuller.
    u = y / EYE_HALF[1]
    lid = EYE_HALF[0] * np.sqrt(np.clip(1 - u * u, 0, 1)) ** 1.3
    top = np.where(x < 0, lid * 1.15, lid * 0.8)
    inside = np.abs(x) < top
    edge = np.clip((top - np.abs(x)) / 0.0008, 0, 1)
    white = inside * edge
    # The iris looks a little up (towards the root), half under the upper lid.
    r = np.hypot(x + 0.0007, y)
    iris = white * smooth01(IRIS + 0.0004, IRIS - 0.0004, r)
    pupil = white * smooth01(PUPIL + 0.0003, PUPIL - 0.0003, r)
    line = smooth01(0.0012, 0.0, np.abs(np.abs(x) - top)) * (np.abs(u) < 1.05) + pupil
    out[:, 0] = white
    out[:, 1] = iris
    out[:, 2] = np.clip(line, 0, 1)
    return out


def marks(p, kind):
    """R, G, B: the eyes, on the forewings only (eye_marks)."""
    if kind.startswith("tar.firefly_wing"):
        return eye_marks(p)
    return np.zeros((len(p), 3), np.float32)


def scorch(p, kind):
    """R: the wing scorched (towards its tip and edges, in blotches); G: the mottle and veins; B: the fur's roots."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("wool.firefly"):
        out[:, 2] = np.clip(smooth01(0.1, 0.8, cook.noise_np(p, 1721, 600.0)), 0, 1)
    if kind.startswith(("tar.firefly_wing", "tar.firefly_hindwing")):
        span = np.clip((np.abs(p[:, 0]) - 0.006) / 0.07, 0, 1)
        out[:, 0] = np.clip(span ** 2.5 * 0.8 + 0.5 * smooth01(0.2, 0.9, cook.noise_np(p, 1731, 90.0)) * span, 0, 1)
        out[:, 1] = np.clip(0.6 * smooth01(0.0, 0.8, cook.noise_np(p, 1732, 220.0)) + 0.6 * smooth01(0.93, 0.99, ridged(p, 1711, 260.0)), 0, 1)
    return out


FORE = 1


def kind_of(m):
    if m.name.startswith(KEEP):
        return overbake.Atlas.KEEP
    return FORE if m.name.startswith("tar.firefly_wing") else 0


atlas = overbake.Atlas("fire_fly", parts, BAKED, kind_of, size=512)
atlas.unwrap(boosts={FORE: 2.5})     # (the forewings carry the eyes)
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"moth": (0.0003, 0.0012)}, height=0.04, masks={"marks": marks, "scorch": scorch})

mk, sc = atlas.maps["marks"], atlas.maps["scorch"]
base = atlas.base(soot=(0.02, 0.018, 0.016), crease=0.35, ao_floor=0.55)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.12, 0.112, 0.1), sc[..., 1] * 0.55)      # the wing's mottle, pale ash
base = paint(base, (0.02, 0.016, 0.012), sc[..., 0] * 0.75)     # scorched to the tips
base = paint(base, (0.035, 0.03, 0.026), sc[..., 2] * 0.6)      # the fur dark at its roots
eye = mk[..., 0]
base = paint(base, (0.52, 0.47, 0.36), eye)                     # the white, yellowed
base = paint(base, (0.09, 0.05, 0.025), mk[..., 1])             # the iris
base = paint(base, (0.004, 0.003, 0.003), mk[..., 2])           # the pupil, the lid's line
rough = np.full(base.shape[:2], 0.75, np.float32)
rough = np.where(eye > 0.5, 0.15, rough)                        # the eyes wet
atlas.finish(base, kit, arm, made=make.provenance("fire_fly", "the Fire Fly, modelled over tools/blender/fire_fly.py"), rough=rough)
