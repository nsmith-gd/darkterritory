"""THE HANGER (GDD §21, OUTSIDE; note 586; the director, 9 Oct 2026): tools/blender/hanger.py's pale bloated thing that
hangs in a house's roof space, with its colour and its fine relief baked into one 1024 atlas (the Look Review: organic,
not primitives; the Mourners' way, note 362).

tools/blender/hanger.py stays its source: the rig, the fused skin of the forebody and the sac, the legs, the fangs, the
eyes, the clips. This recipe runs it, unwraps every part into one atlas (the mouth, the fangs and the eyes given more of
it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the sac's thin skin stretched
    tight and wrinkled where it sags, veins raised in it; the forebody's skin harder, pitted; the legs ringed with fine
    creases, the knuckles knotted; the lips puckered;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): a sick grub-white,
    the sac paler and thinner, so the dark veins and the darker shapes packed inside it show through, yellowed and
    bruised where it sags, grimed grey where it rubs the beams; the legs a dirtier ivory banded dark at every knuckle,
    darkening to black hooks; the mouth a wet dark red; the fangs black horn; the eyes black glass.
Wet on the sac and the mouth (it glistens in a lamp), dry on the legs.

    tools/models/build.sh hanger        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/hanger.py)
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402,F401  (first: as a Python module, Blender's bmesh is only importable after it)
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import fine, smooth01  # noqa: E402
from texels import Texels  # noqa: E402

S = 1024  # the roster's creature atlas: every renderer holds every atlas, and the 2048 tier is full (note 367)
kit, g, arm, parts = overbake.hold("hanger.py")
print("[dt] hanger parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
MOUTH = np.array(tuple(g["MOUTH"]), np.float32)
SAC = np.array(tuple(g["SAC"]), np.float32)


def suffix(name):
    return name.split(".")[1]


DRESS = {"hanger": 3, "hanger_leg": 1, "hanger_mouth": 2, "hanger_fang": 0, "hanger_eye": 1}


def dress(m):
    return make.flat("hanger_high", (0.3, 0.3, 0.3), rough=0.5), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def veins_of(p):
    """Veins branching over the sac: the crests of two scales of ridged noise, stretched along it."""
    q = p * np.array([1.0, 0.45, 1.0], np.float32)
    return np.maximum(smooth01(0.9, 0.975, ridged(q, 2101, 9.0)), 0.7 * smooth01(0.93, 0.985, ridged(q, 2102, 22.0)))


def sac_of(p):
    """How far into the sac a point is (0 the forebody, 1 the sac): it's behind the waist."""
    return smooth01(-0.05, -0.18, p[:, 1])


def skin_shape(p, n):
    sac = sac_of(p)
    # Stretched and wrinkled where it sags (its underside, the lower the more), raised veins over it; the forebody pitted.
    sag = smooth01(-0.3, -0.48, p[:, 2]) * sac
    wrinkle = 0.0012 * np.sin(p[:, 0] * 90 + 3 * cook.noise_np(p, 2103, 5.0)) * sag
    pits = -0.0008 * smooth01(0.6, 0.9, cook.noise_np(p, 2104, 40.0)) * (1 - sac)
    return 0.0011 * veins_of(p) * sac + wrinkle + pits + 0.0008 * cook.noise_np(p, 2105, 7.0) + fine(p, 0.0002, 380, 2106)


def leg_shape(p, n):
    return 0.0004 * np.sin(np.linalg.norm(p[:, :2], axis=1) * 260) + fine(p, 0.00015, 500, 2111)


def lips_shape(p, n):
    r = np.linalg.norm(p - MOUTH, axis=1)
    return 0.0015 * np.sin(np.arctan2(p[:, 1] - MOUTH[1], p[:, 0] - MOUTH[0]) * 14) * smooth01(0.06, 0.02, r) + fine(p, 0.0002, 400, 2121)


SHAPE = {"skin.hanger_leg": leg_shape, "skin.hanger_mouth": lips_shape, "skin.hanger_fang": lambda p, n: 0 * p[:, 0],
         "glass_dirty.hanger_eye": lambda p, n: 0 * p[:, 0], "skin.hanger": skin_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] hanger highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE, LIMB = 1, 2


def kind_of(m):
    # (The legs are most of its area, and thin: they're given less of the atlas than the sac a lamp shows.)
    return FACE if suffix(m.name) in ("hanger_mouth", "hanger_eye", "hanger_fang") else LIMB if suffix(m.name) == "hanger_leg" else 0


atlas = overbake.Atlas("hanger", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.5, LIMB: 0.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "legs": (0.003, 0.01), "fine": (0.003, 0.01)}, samples=16, height=1.0)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] hanger atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
skin = is_("hanger")
sac = field(sac_of) * skin
# A sick grub-white, mottled.
base = np.broadcast_to(np.array((0.58, 0.55, 0.48), np.float32), (S, S, 3)).copy()
base *= (0.9 + 0.14 * noise(4101, 3.0))[..., None]
base = paint(base, (0.68, 0.66, 0.6), smooth01(0.1, 0.6, noise(4102, 5.0)) * 0.5 * skin)
# The sac thinner and paler, the shapes packed in it showing through dark, the veins dark blue-grey over it.
inside = smooth01(0.2, 0.7, noise(4103, 4.0)) * sac
base = paint(base, (0.36, 0.33, 0.3), inside * 0.45)
vein = field(veins_of) * sac
base = paint(base, (0.18, 0.17, 0.22), vein * 0.75)
# Yellowed and bruised where it sags, lower down; grimed grey where it rubs the beams (whatever faces up).
low = smooth01(-0.3, -0.5, P[..., 2]) * sac
base = paint(base, (0.55, 0.47, 0.3), low * 0.5)
base = paint(base, (0.42, 0.3, 0.32), low * smooth01(0.3, 0.8, noise(4104, 9.0)) * 0.35)
base = paint(base, (0.27, 0.26, 0.24), smooth01(0.3, 0.9, up) * 0.55 * skin)
# The forebody a little darker, harder, its pits dark.
fore = (1 - field(sac_of)) * skin
base = paint(base, (0.45, 0.42, 0.36), fore * 0.45)
rough = np.full((S, S), 0.45, np.float32) - 0.25 * sac + 0.2 * fore
# The legs: dirtier ivory, banded dark at each knuckle, darkening to the hooks.
legs_ = is_("hanger_leg")
base = paint(base, (0.5, 0.46, 0.38), legs_ * 0.7)
reach = np.linalg.norm(P[..., :2], axis=-1)
band = smooth01(0.6, 0.95, np.sin(reach * 7.5) ** 8) * legs_
base = paint(base, (0.12, 0.1, 0.09), band * 0.55)
base = paint(base, (0.08, 0.07, 0.06), smooth01(0.8, 1.2, reach) * legs_ * 0.7)
rough[legs_] = 0.7
# The mouth wet dark red, darker into the opening; the fangs and hooks black horn; the eyes black glass.
lips = is_("hanger_mouth")
r = np.linalg.norm(P - MOUTH, axis=-1)
base = paint(base, (0.3, 0.07, 0.06), lips * 0.9)
base = paint(base, (0.08, 0.01, 0.01), lips * smooth01(0.035, 0.015, r))
base = paint(base, (0.42, 0.24, 0.2), skin * smooth01(0.08, 0.04, r) * 0.6)
rough[lips] = 0.12
fang = is_("hanger_fang")
base[fang] = np.array((0.05, 0.04, 0.035), np.float32) * (0.8 + 0.4 * noise(4105, 30.0))[fang][:, None]
rough[fang] = 0.35
for e in EYES:
    d = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.1, 0.08, 0.07), smooth01(0.03, 0.012, d) * skin * 0.6)
eye = is_("hanger_eye")
base[eye] = np.array((0.01, 0.009, 0.009), np.float32)
rough[eye] = 0.04
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye, ao ** 0.35, ao)
base = base * (0.4 + 0.6 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.4
base = base * (1 - k)[..., None] + np.array((0.03, 0.025, 0.022), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("hanger", "the Hanger, modelled in tools/blender/hanger.py, its colour painted here"),
             rough=rough, lod=0.4)
