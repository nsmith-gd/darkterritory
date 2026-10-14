"""THE HOUSEHOLDER (GDD §21, OUTSIDE; note 584; docs/design/creatures/householder.md): tools/blender/householder.py's gaunt
figure in its Sunday best with its colour and its fine relief baked into one 1024 atlas (the Look Review: organic, not
primitives; the Mourners' way, note 362).

tools/blender/householder.py stays its source: the rig, the fused skin and its clothes and the parts over them, the clips.
This recipe runs it, unwraps every part into one atlas (the face, the eyes and the hands given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the waistcoat's wool, its seams and
    its pockets' welts; the shirt's creases; the trousers' folds at the knees; the face's lines and the dry skin's
    crazing; the knuckles of the long fingers;
  * paints the colour from where each texel is on the figure at rest (deterministic, no render): the waistcoat a black
    gone brown, threadbare and mould-bloomed, moth-eaten; the shirt yellowed to the colour of old teeth, grimed at the
    collar band and the cuffs; the trousers dark and shined at the knees, the shoes black, cracked and grey with dust;
    the skin grey and dry, liver-spotted, the eyes' sockets shadowed, the hair thin and combed flat; the eyes wide, wet,
    the whites gone yellow, the irises dark, looking straight out; the buttons and the watch chain tarnished brass.

    tools/models/build.sh householder   (or with no Blender: pip install "bpy<5", then python tools/models/recipes/householder.py)
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

S = 1024  # a character's atlas at the roster's 1024 (the 2048 tier is full: note 367)
kit, g, arm, parts = overbake.hold("householder.py")
print("[dt] householder parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
HEAD = np.array(tuple(g["HEAD"]), np.float32)
BUTTONS = np.array([tuple(p) for p in g["BUTTONS"]], np.float32)
TIPS = np.array([tuple(p) for p in g["TIPS"]], np.float32)
CUFFS = np.array([tuple(p) for p in g["CUFFS"]], np.float32)
V_TOP, V_BOTTOM, VEST_HEM = float(g["V_TOP"]), float(g["V_BOTTOM"]), float(g["VEST_HEM"])


def suffix(name):
    return name.split(".")[1]


DRESS = {"householder": 3, "householder_face": 3, "householder_vest": 2, "householder_shirt": 2, "householder_trousers": 2,
         "householder_shoe": 2, "householder_button": 0, "householder_eye": 1, "householder_nail": 0}


def dress(m):
    return make.flat("householder_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def weave(p):
    """A wool's weave, fine."""
    return 0.00025 * np.sin(p[:, 0] * 1400) * np.sin(p[:, 2] * 1400)


def vest_shape(p, n):
    # Pocket welts either side, a seam down each side, the wool's weave, sagging folds where it's worn.
    welt = sum(smooth01(0.008, 0.002, np.abs(p[:, 2] - z)) * smooth01(0.06, 0.04, np.abs(np.abs(p[:, 0]) - 0.07)) * (p[:, 1] > 0)
               for z in (1.17, 1.32))
    return 0.0012 * welt + 0.0006 * np.sin(p[:, 2] * 180 + 3 * cook.noise_np(p, 5901, 8.0)) + weave(p) + fine(p, 0.0002, 400, 5902)


def shirt_shape(p, n):
    return 0.0011 * np.sin(p[:, 2] * 140 + 4 * cook.noise_np(p, 5911, 6.0)) * smooth01(0.3, 0.7, ridged(p, 5912, 5.0)) + fine(p, 0.00015, 500, 5913)


def trouser_shape(p, n):
    knees = smooth01(0.12, 0.0, np.abs(p[:, 2] - 0.55))
    return 0.0016 * knees * np.sin(p[:, 2] * 220 + 2 * cook.noise_np(p, 5921, 10.0)) + 0.0007 * np.sin(p[:, 2] * 60) + weave(p)


def skin_shape(p, n):
    # Dry, crazed, the lines of a long face.
    return -0.0005 * smooth01(0.9, 0.97, ridged(p, 5931, 60.0)) + 0.0004 * cook.noise_np(p, 5932, 30.0) + fine(p, 0.0002, 600, 5933)


def flat(p, n):
    return 0 * p[:, 0]


SHAPE = {"skin.householder_face": skin_shape, "skin.householder_nail": flat, "skin.householder": skin_shape,
         "wool.householder_vest": vest_shape, "wool.householder_shirt": shirt_shape, "wool.householder_trousers": trouser_shape,
         "leather.householder_shoe": lambda p, n: -0.0006 * smooth01(0.9, 0.97, ridged(p, 5941, 40.0)),
         "brass.householder_button": flat, "glass_dirty.householder_eye": flat}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] householder highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("householder_face", "householder_eye", "householder_button") else 0


atlas = overbake.Atlas("householder", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.8})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "fine": (0.004, 0.012)}, samples=16, height=2.0)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] householder atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.85, np.float32)
# The skin: grey and dry, liver-spotted, the sockets and the cheeks shadowed, the lips a bloodless line.
skin = is_("householder", "householder_face")
face = is_("householder_face")
base = paint(base, (0.29, 0.28, 0.265), skin * (0.9 + 0.15 * noise(5951, 5.0)))
base = paint(base, (0.19, 0.16, 0.14), smooth01(0.55, 0.8, noise(5952, 25.0)) * 0.5 * skin)
for e in EYES:
    d = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.1, 0.085, 0.085), smooth01(0.04, 0.014, d) * face * 0.75)
mouth = smooth01(0.006, 0.0015, np.abs(P[..., 2] - (HEAD[2] - 0.072))) * smooth01(0.03, 0.022, np.abs(P[..., 0])) * (P[..., 1] > HEAD[1] + 0.07)
base = paint(base, (0.1, 0.075, 0.08), mouth * face)
# The hair: thin, dark grey, combed flat back over the crown, the scalp showing through.
hair = smooth01(HEAD[2] + 0.02, HEAD[2] + 0.06, P[..., 2]) * smooth01(HEAD[1] + 0.09, HEAD[1] + 0.05, P[..., 1]) * face
strands = 0.5 + 0.5 * np.sin(P[..., 0] * 900 + 6 * noise(5953, 20.0))
base = paint(base, (0.06, 0.06, 0.06), hair * (0.4 + 0.5 * strands))
# The hands' knuckles and nails.
hands = skin * smooth01(0.93, 0.88, P[..., 2]) * smooth01(0.15, 0.19, np.abs(P[..., 0]))
base = paint(base, (0.22, 0.19, 0.18), hands * smooth01(0.4, 0.8, noise(5954, 30.0)) * 0.5)
nail = is_("householder_nail")
base[nail] = np.array((0.3, 0.27, 0.19), np.float32)
rough[nail] = 0.5
# The eyes: the whites yellowed, a dark iris and a black pupil straight out in front of each (it never blinks).
eye = is_("householder_eye")
for e in EYES:
    d = P - e
    look = d[..., 1] / np.maximum(np.linalg.norm(d, axis=-1), 1e-6)
    near = np.linalg.norm(d, axis=-1) < 0.02
    base[eye & near] = np.array((0.5, 0.46, 0.36), np.float32)
    base = paint(base, (0.08, 0.07, 0.05), (eye & near) * smooth01(0.72, 0.8, look))
    base = paint(base, (0.005, 0.005, 0.005), (eye & near) * smooth01(0.9, 0.94, look))
rough[eye] = 0.05
# The waistcoat: a black gone brown, threadbare at its edges and its points, mould blooming grey-green on it, moth holes.
vest = is_("householder_vest")
base = paint(base, (0.055, 0.042, 0.036), vest * (0.85 + 0.3 * noise(5955, 8.0)))
base = paint(base, (0.13, 0.12, 0.09), smooth01(0.55, 0.85, noise(5956, 6.0)) * 0.45 * vest)
base = paint(base, (0.12, 0.13, 0.1), smooth01(0.6, 0.9, noise(5957, 22.0)) * 0.5 * vest)
base = paint(base, (0.01, 0.01, 0.01), smooth01(0.82, 0.9, noise(5958, 70.0)) * vest)
edge = smooth01(0.03, 0.0, np.abs(P[..., 2] - VEST_HEM - 0.03)) + smooth01(0.012, 0.0, np.abs(P[..., 0])) * (P[..., 1] > 0)
base = paint(base, (0.12, 0.1, 0.08), np.clip(edge, 0, 1) * 0.5 * vest)
rough = np.where(vest, 0.9, rough)
# The shirt: yellowed, grimed at the collar band and the cuffs, stained down its front.
shirt = is_("householder_shirt")
base = paint(base, (0.38, 0.34, 0.25), shirt * (0.85 + 0.2 * noise(5959, 5.0)))
base = paint(base, (0.22, 0.17, 0.1), smooth01(0.5, 0.85, noise(5960, 9.0)) * 0.55 * shirt)
collar = smooth01(0.04, 0.0, np.abs(P[..., 2] - 1.565))
cuff = np.min(np.stack([np.linalg.norm(P - c, axis=-1) for c in CUFFS], 0), 0)
base = paint(base, (0.12, 0.1, 0.07), np.maximum(collar, smooth01(0.06, 0.02, cuff)) * 0.6 * shirt)
# The trousers: dark, shined at the knees and the seat, dusty at the hems; the shoes black, cracked, dusty.
trousers = is_("householder_trousers")
base = paint(base, (0.065, 0.06, 0.055), trousers * (0.85 + 0.25 * noise(5961, 7.0)))
base = paint(base, (0.14, 0.13, 0.12), smooth01(0.2, 0.05, P[..., 2]) * 0.6 * trousers)
shoe = is_("householder_shoe")
base = paint(base, (0.025, 0.022, 0.02), shoe)
base = paint(base, (0.12, 0.11, 0.1), smooth01(0.3, 0.9, up) * smooth01(0.4, 0.8, noise(5962, 15.0)) * 0.5 * shoe)
rough = np.where(shoe, 0.5, rough)
# The buttons and the watch chain: tarnished brass.
brass = is_("householder_button")
base = paint(base, (0.22, 0.17, 0.08), brass * (0.7 + 0.4 * noise(5963, 50.0)))
rough = np.where(brass, 0.4, rough)
base = np.clip(base, 0, 1)
rough = np.clip(rough + 0.04 * noise(5964, 12.0), 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.018, 0.016, 0.014), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("householder", "the Householder, modelled in tools/blender/householder.py, its colour painted here"),
             rough=rough, lod=0.4)
