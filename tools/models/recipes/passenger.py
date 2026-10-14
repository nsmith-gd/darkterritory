"""THE PASSENGER (GDD v1.2 §21, App. A.8): the conductor of tools/blender/passenger.py, taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/passenger.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the greatcoat's wool gone to felt, mould blooming pale in its folds and along the hem, salt-white at the shoulders
    and the collar where something has dried on it for years; the brass verdigris-green;
  * the cap's wool the same, its peak cracked;
  * the face grey, mottled, the veins dark under it, the lips bruised, the wire rust-brown; the eyes a wet milky white;
  * the scarf a pale neutral wool, so CreatureArt can dye it the colour of whichever of the crew it's copying (its own
    material, <name>_0.paint, as the crew's cap and scarf are).

    tools/models/build.sh passenger
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

kit, g, arm, parts = overbake.hold("passenger.py")
print("[dt] passenger parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

# Where tools/blender/passenger.py puts the eyes and the mouth (its HC, HR, EYE_U, EYE_W, MOUTH_W at rest).
HC = np.array(tuple(g["HC"]), np.float32)
HR = np.array(tuple(g["HR"]), np.float32)

DRESS = {
    "wool.passenger_coat": (lambda: make.flat("passenger_coat", (0.03, 0.032, 0.04), rough=0.95), 2),
    "wool.passenger_cap": (lambda: make.flat("passenger_cap", (0.022, 0.023, 0.028), rough=0.9), 2),
    "wool.passenger_trouser": (lambda: make.flat("passenger_trouser", (0.025, 0.025, 0.03), rough=0.95), 1),
    "wool.passenger.paint": (lambda: make.flat("passenger_scarf", (0.11, 0.11, 0.11), rough=0.95), 2),
    "leather.passenger_peak": (lambda: make.flat("passenger_peak", (0.008, 0.007, 0.006), rough=0.35), 1),
    "leather.passenger_boot": (lambda: make.flat("passenger_boot", (0.016, 0.012, 0.009), rough=0.55), 1),
    "brass.passenger": (lambda: make.flat("passenger_brass", (0.11, 0.12, 0.07), rough=0.45, metal=0.6), 0),
    "flesh.passenger_lips": (lambda: make.flat("passenger_lips", (0.09, 0.065, 0.07), rough=0.45), 2),
    "flesh.passenger_eye": (lambda: make.flat("passenger_eye", (0.48, 0.49, 0.45), rough=0.1), 2),
    "flesh.passenger": (lambda: make.flat("passenger_skin", (0.13, 0.14, 0.14), rough=0.6), 2),
    "rust_heavy.passenger_wire": (lambda: make.flat("passenger_wire", (0.09, 0.045, 0.022), rough=0.6, metal=0.4), 0),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"passenger: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def wool(p, n):
    # Felted: soft lumps, and the weave just showing.
    return 0.0006 * cook.noise_np(p, 1801, 30.0) + fine(p, 0.00012, 700, 1802)


def skin(p, n):
    """Dry, the pores; and drawn tight (note 538): creased across the forehead, cracked fine all over like old paper."""
    u, w = (p[:, 0] - HC[0]) / HR[0], (p[:, 2] - HC[2]) / HR[2]
    front = smooth01(HC[1] + 0.02, HC[1] + 0.07, p[:, 1])
    brow = front * smooth01(0.3, 0.45, w) * smooth01(0.95, 0.75, w) * (0.5 + 0.5 * np.cos(w * 70.0 + 0.3 * np.sin(u * 6.0))) ** 4
    return (0.00045 * smooth01(0.88, 0.97, ridged(p, 1803, 160.0)) - 0.0007 * brow + fine(p, 0.00006, 2000, 1804)
            - 0.0003 * smooth01(0.93, 0.98, ridged(p, 1808, 90.0)))


SHAPE = {"wool.passenger": wool, "leather.passenger_peak": lambda p, n: -0.0004 * smooth01(0.92, 0.98, ridged(p, 1805, 60.0)),
         "leather.passenger_boot": lambda p, n: -0.0003 * smooth01(0.9, 0.97, ridged(p, 1806, 50.0)),
         "brass.passenger": lambda p, n: 0 * p[:, 0], "flesh.passenger_eye": lambda p, n: 0 * p[:, 0], "flesh.passenger_lips": skin,
         "flesh.passenger": skin, "rust_heavy.passenger_wire": lambda p, n: fine(p, 0.00008, 3000, 1807)}
BAKED = ["head", "cap", "coat", "scarf", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] passenger highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: mould blooming on the wool (in the folds, up from the hem); G: salt-white dried on the shoulders and collar;
    B: the face's mottle and veins."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("wool.passenger") and not kind.startswith("wool.passenger.paint"):
        hem = smooth01(0.85, 0.4, p[:, 2])
        out[:, 0] = np.clip(0.55 * smooth01(0.25, 0.8, cook.noise_np(p, 1811, 7.0)) * (0.4 + 0.6 * hem) + 0.4 * hem * smooth01(0.0, 0.6, cook.noise_np(p, 1812, 25.0)), 0, 1)
        out[:, 1] = np.clip(smooth01(1.3, 1.52, p[:, 2]) * smooth01(0.1, 0.7, cook.noise_np(p, 1813, 12.0)), 0, 1)
    if kind.startswith("flesh.passenger") and not kind.startswith(("flesh.passenger_eye", "flesh.passenger_lips")):
        out[:, 2] = np.clip(0.6 * smooth01(0.9, 0.97, ridged(p, 1814, 70.0)) + 0.4 * smooth01(0.2, 0.9, cook.noise_np(p, 1815, 18.0)), 0, 1)
    return out




def face(p, kind):
    """R: the sockets, dark and bruised round the eyes; G: the mouth's bruising round its stitches."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("flesh.passenger") and not kind.startswith("flesh.passenger_eye"):
        u, w = (p[:, 0] - HC[0]) / HR[0], (p[:, 2] - HC[2]) / HR[2]
        front = p[:, 1] > HC[1] + 0.03
        for eu in (-g["EYE_U"], g["EYE_U"]):
            d = np.hypot((u - eu) / 0.3, (w - 0.1) / 0.24)
            out[:, 0] = np.maximum(out[:, 0], smooth01(1.0, 0.35, d) * front)
        d = np.hypot(u / 0.42, (w + 0.52) / 0.14)
        out[:, 1] = smooth01(1.0, 0.3, d) * front
    return out


FACE, PAINT = 1, 2


def kind_of(m):
    if m.name.startswith("wool.passenger.paint"):
        return PAINT
    return FACE if m.name.startswith(("flesh.passenger", "rust_heavy.passenger_wire")) else 0


atlas = overbake.Atlas("passenger", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 3.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.003, 0.012), "cap": (0.004, 0.012), "coat": (0.006, 0.02), "scarf": (0.005, 0.015), "limbs": (0.004, 0.014)},
           height=1.8, masks={"marks": marks, "face": face})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.01, 0.01, 0.012), crease=0.45, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.075, 0.08, 0.065), mk[..., 0] * 0.6)     # mould, grey-green
base = paint(base, (0.2, 0.2, 0.19), mk[..., 1] * 0.45)        # dried salt
base = paint(base, (0.06, 0.06, 0.08), mk[..., 2] * 0.4)       # the face's veins and mottle
fc = atlas.maps["face"]
base = paint(base, (0.025, 0.018, 0.022), fc[..., 0] * 0.75)    # the sockets sunk and dark
base = paint(base, (0.05, 0.025, 0.03), fc[..., 1] * 0.55)      # the mouth bruised round the wire
atlas.finish(base, kit, arm, made=make.provenance("passenger", "the Passenger, modelled over tools/blender/passenger.py"), split={PAINT: "paint"})
