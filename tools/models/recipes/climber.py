"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4): the mangy, starved climbing thing of tools/blender/climber.py (redone,
note 1015: the director's "I just dont like how this thing looks or animates"), taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/climber.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin bald with mange, a sick grey-pink, wrinkled and loose over the ribs, crusted with scabs, the veins showing
    blue-grey through it, raw red at the scabs' edges; the belly paler, dirt-grey from the ballast;
  * what fur is left near black and matted, in patches (painted: the bare skin shows round them) and in the ridge down
    the spine;
  * the gums bare and dark red, wet, the needle teeth yellowed; the claws black; the eyes kept blind milk glass.

    tools/models/build.sh climber
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import fine, smooth01  # noqa: E402

kit, g, arm, parts = overbake.hold("climber.py")
print("[dt] climber parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "skin.climber_teeth": (lambda: make.flat("climber_teeth", (0.34, 0.29, 0.18), rough=0.4), 1),
    "skin.climber": (lambda: make.flat("climber_skin", (0.115, 0.1, 0.095), rough=0.55), 2),
    "wool.climber_fur": (lambda: make.flat("climber_fur", (0.014, 0.012, 0.011), rough=0.8), 1),
    "glass_dirty.climber_eye": (lambda: make.flat("climber_eye", (0.5, 0.52, 0.5), rough=0.05), 0),
    "tar.climber_mouth": (lambda: make.flat("climber_mouth", (0.045, 0.012, 0.01), rough=0.3), 1),
    "tar.climber_nail": (lambda: make.flat("climber_nail", (0.008, 0.007, 0.006), rough=0.2), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"climber: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def scabs(p):
    return np.clip(smooth01(0.5, 0.75, cook.noise_np(p, 1706, 28.0)) + 0.6 * smooth01(0.55, 0.8, cook.noise_np(p, 1716, 70.0)), 0, 1)


def skin_shape(p, n):
    # Loose and wrinkled, the wrinkles crossing; crusted where the scabs are, raised; the fur's patches a matted nap.
    w = smooth01(0.25, 0.7, cook.noise_np(p, 1701, 6.0))
    return (-0.0008 * w * smooth01(0.8, 0.97, ridged(p, 1702, 150.0)) + 0.0009 * scabs(p) * (0.6 + 0.4 * cook.noise_np(p, 1708, 140.0))
            + 0.0004 * fur_at(p) * ridged(p * np.array([3, 0.6, 3], np.float32), 1709, 220.0) + fine(p, 0.00006, 1200, 1703))


def fur_at(p):
    """0..1 where its fur's left: in patches, thicker down the back and over the shoulders and haunches."""
    back = smooth01(0.3, 0.5, p[:, 2]) * smooth01(-1.0, -0.6, p[:, 1])
    return np.clip(smooth01(0.15, 0.45, cook.noise_np(p, 1714, 5.0) + 0.35 * back), 0, 1)


SHAPE = {"skin.climber_teeth": lambda p, n: fine(p, 0.00003, 1200, 1704), "skin.climber": skin_shape,
         "wool.climber_fur": lambda p, n: 0.001 * ridged(p * np.array([4, 0.5, 4], np.float32), 1710, 260.0),
         "tar.climber": lambda p, n: fine(p, 0.00003, 1200, 1705), "glass_dirty.climber_eye": lambda p, n: 0 * p[:, 0]}
# (In the scene's order: the atlas's join keeps that, and the eyes are kept faces, matched to it by name.)
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] climber highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def marks(p, kind):
    """R: the scabs (crusted, raw at their edges); G: the fur's patches; B: the veins showing through the bare skin."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("skin.climber") and not kind.startswith("skin.climber_teeth"):
        out[:, 0] = scabs(p)
        out[:, 1] = fur_at(p)
        out[:, 2] = np.clip(smooth01(0.82, 0.96, ridged(p, 1713, 14.0)) + 0.6 * smooth01(0.88, 0.97, ridged(p, 1717, 40.0)), 0, 1) * (1 - out[:, 1])
    return out


def dirt(p, kind):
    """R: ballast dirt, up its belly and its legs from the ground; G: blood and wet round the gums; B: mottling."""
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = smooth01(0.42, 0.08, p[:, 2]) * (0.5 + 0.5 * cook.noise_np(p, 1715, 9.0))
    out[:, 1] = smooth01(0.08, 0.0, np.hypot(p[:, 0], (p[:, 1] - 0.82) * 0.5) - 0.04) * smooth01(0.47, 0.43, p[:, 2])
    out[:, 2] = smooth01(0.0, 0.8, cook.noise_np(p, 1718, 4.0))     # B: the bare skin's mottling
    return out


def gloss(p, kind):
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = (0.3 if kind.startswith("tar.climber_mouth") else 0.4 if kind.startswith("skin.climber_teeth") else
                 0.2 if kind.startswith("tar.climber_nail") else 0.85 if kind.startswith("wool") else 0.55)
    return out


FACE = 1


def kind_of(m):
    if m.name.startswith("glass_dirty.climber_eye"):
        return overbake.Atlas.KEEP
    return FACE if m.name.startswith(("tar.climber_mouth", "skin.climber_teeth")) else 0


atlas = overbake.Atlas("climber", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.003, 0.012) for name in BAKED}, height=1.0, masks={"marks": marks, "gloss": gloss, "dirt": dirt})

mk = atlas.maps["marks"]
base = atlas.base(soot=(0.01, 0.009, 0.009), crease=0.5, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


dt_ = atlas.maps["dirt"]
# The bare skin mottled, sick and uneven (grey-pink to a dead yellow-grey), the veins blue-grey under it.
mott = atlas.maps["dirt"][..., 2]
base = paint(base, (0.1, 0.095, 0.07), mott * 0.5)
base = paint(base, (0.05, 0.055, 0.075), mk[..., 2] * 0.6)
base = paint(base, (0.018, 0.015, 0.013), mk[..., 1] * 0.9)     # the fur's patches, near black, matted
base = paint(base, (0.09, 0.03, 0.022), mk[..., 0] * 0.75)      # the scabs, crusted dark red-brown
base = paint(base, (0.045, 0.038, 0.03), dt_[..., 0] * 0.6)     # the ballast's dirt
base = paint(base, (0.06, 0.012, 0.01), dt_[..., 1] * 0.6)      # blood round the mouth, dried dark
rough = np.clip(atlas.maps["gloss"][..., 0] + 0.25 * mk[..., 1] - 0.3 * dt_[..., 1], 0.05, 1)
atlas.finish(base, kit, arm, made=make.provenance("climber", "the Climber, modelled over tools/blender/climber.py"), rough=rough)
