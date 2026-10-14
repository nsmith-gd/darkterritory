"""THE WAKER · the Peat Man (GDD §8 "A titan"; queue #411, note 679; docs/design/creatures/wakers-model.md §B):
tools/blender/waker.py's bog body with its colour and its relief baked into one 2048 atlas (GDD §27's density on a monster
drawn sixty metres tall: Look.BigHeroLayerSize; the face, the eyes and the teeth given more of it, since the crew in a lifted
car see them from closest).

tools/blender/waker.py stays its source: the rig, the fused skin and the parts over it, the clips. This recipe runs it,
unwraps every part into one atlas, and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the leather's deep creases and
    folds where the peat shrank it onto the bones, the tighter skin of the face, the hair's matted ropes, the noose's lay,
    the peat's fibre and the moss's cushions;
  * paints the colour from where each texel is on him at rest (deterministic, no render): the skin a bog body's, tanned
    near black and reddened where it's stretched over the bone, ochre on the ridges, peat-stained darker toward the hands
    and the feet, wet; moss streaking his back and shoulders; the eyes black wet pits with a milky film; the gums dark,
    the teeth brown and yellow; the nails black; the hair gone fox-red with the bog; the rope old hemp, black with peat;
    the peat clods black-brown and fibrous, the moss sphagnum green with red tips.

    tools/models/build.sh waker   (or with no Blender: pip install "bpy<5", then python tools/models/recipes/waker.py)
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

S = 2048
kit, g, arm, parts = overbake.hold("waker.py")
print("[dt] waker parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)
HEAD = np.array(tuple(g["HEAD_C"]), np.float32)
LIPS = float(g["LIPS"])
HIP = float(g["HIP"])


def suffix(name):
    return name.split(".")[1]


DRESS = {"waker": 3, "waker_face": 3, "waker_gum": 2, "waker_tooth": 0, "waker_eye": 1, "waker_nail": 0, "waker_hair": 2,
         "waker_rope": 2, "waker_peat": 1, "waker_moss": 2}


def dress(m):
    return make.flat("waker_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def leather(p, n):
    """The bog's leather: deep creases where it shrank onto him, folds across the joints, a fine pebbling."""
    creases = -0.0014 * smooth01(0.86, 0.97, ridged(p, 6101, 28.0)) - 0.0007 * smooth01(0.88, 0.97, ridged(p, 6102, 70.0))
    folds = 0.0007 * np.sin(p[:, 2] * 160 + 3 * cook.noise_np(p, 6103, 9.0)) * smooth01(0.3, 0.8, ridged(p, 6104, 6.0))
    return creases + folds + 0.0004 * cook.noise_np(p, 6105, 20.0) + fine(p, 0.00025, 420, 6106)


def face(p, n):
    return -0.0008 * smooth01(0.88, 0.97, ridged(p, 6111, 60.0)) + 0.0003 * cook.noise_np(p, 6112, 30.0) + fine(p, 0.0002, 600, 6113)


def hair(p, n):
    return 0.0009 * np.sin(p[:, 0] * 700 + p[:, 1] * 300 + 5 * cook.noise_np(p, 6121, 25.0)) + fine(p, 0.0003, 500, 6122)


def rope(p, n):
    return 0.0008 * np.sin((p[:, 0] + p[:, 1] + p[:, 2]) * 520) + fine(p, 0.0002, 600, 6131)


def peat(p, n):
    return 0.002 * cook.noise_np(p, 6141, 45.0) + 0.0008 * np.sin(p[:, 0] * 900 + 4 * cook.noise_np(p, 6142, 30.0)) + fine(p, 0.0005, 300, 6143)


def moss(p, n):
    return 0.0016 * smooth01(0.3, 0.9, 0.5 + 0.5 * cook.noise_np(p, 6151, 90.0)) + fine(p, 0.0004, 500, 6152)


def flat(p, n):
    return 0 * p[:, 0]


SHAPE = {"skin.waker": leather, "skin.waker_face": face, "flesh.waker_gum": face, "skin.waker_tooth": flat,
         "glass_dirty.waker_eye": flat, "tar.waker_nail": flat, "fleece.waker_hair": hair, "wool.waker_rope": rope,
         "fungal_crust.waker_peat": peat, "fungal_crust.waker_moss": moss}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] waker highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("waker_face", "waker_eye", "waker_tooth", "waker_gum") else 0


atlas = overbake.Atlas("waker", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.8})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "fine": (0.004, 0.012)}, samples=16, height=2.1)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] waker atlas", tx.coverage())
is_, noise, paint = tx.is_, tx.noise, tx.paint

up = N[..., 2]
base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.5, np.float32)
# The skin: tanned near black by the bog, reddened where it's stretched over the bone, ochre on the ridges; stained
# darker toward the hands and the feet (where the peat held him longest), wet.
skin = is_("waker", "waker_face")
base = paint(base, (0.075, 0.04, 0.022), skin * (0.85 + 0.3 * noise(6201, 4.0)))
base = paint(base, (0.16, 0.075, 0.035), smooth01(0.5, 0.85, noise(6202, 9.0)) * 0.55 * skin)
base = paint(base, (0.24, 0.14, 0.06), smooth01(0.7, 0.95, noise(6203, 30.0)) * 0.35 * skin)
base = paint(base, (0.03, 0.02, 0.014), smooth01(0.55, 0.85, noise(6204, 14.0)) * 0.5 * skin)
low = smooth01(0.6, 0.1, P[..., 2]) + smooth01(0.95, 0.7, P[..., 2]) * smooth01(0.18, 0.25, np.abs(P[..., 0]))
base = paint(base, (0.025, 0.017, 0.012), np.clip(low, 0, 1) * 0.6 * skin)
rough = np.where(skin, 0.38 + 0.2 * noise(6205, 12.0), rough)
# Moss in streaks down his back and over his shoulders, where he lay uppermost.
back = smooth01(0.0, -0.06, P[..., 1] - 0.0 * N[..., 1]) * smooth01(-0.2, -0.6, N[..., 1]) + smooth01(0.5, 0.9, up) * smooth01(1.55, 1.68, P[..., 2])
mossy = np.clip(back, 0, 1) * smooth01(0.5, 0.8, noise(6206, 7.0)) * smooth01(1.1, 1.3, P[..., 2]) * skin
base = paint(base, (0.06, 0.065, 0.022), mossy * 0.75)
rough = np.where(mossy > 0.3, 0.7, rough)
# The face: the leather tighter and darker round the eyes' pits and the nose, the lips drawn back and black.
face_ = is_("waker_face")
for e in EYES:
    d = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.012, 0.009, 0.007), smooth01(0.045, 0.016, d) * face_ * 0.85)
nose = np.linalg.norm(P - (HEAD + np.array((0, 0.11, -0.025), np.float32)), axis=-1)
base = paint(base, (0.02, 0.012, 0.01), smooth01(0.03, 0.01, nose) * face_ * 0.7)
lips = smooth01(0.016, 0.004, np.abs(P[..., 2] - LIPS)) * (P[..., 1] > HEAD[1] + 0.06) * smooth01(0.06, 0.04, np.abs(P[..., 0]))
base = paint(base, (0.02, 0.008, 0.008), lips * face_ * 0.9)
# The eyes: black wet pits, a milky film over them.
eye = is_("waker_eye")
base[eye] = np.array((0.008, 0.008, 0.007), np.float32)
for e in EYES:
    d = P - e
    look = d[..., 1] / np.maximum(np.linalg.norm(d, axis=-1), 1e-6)
    near = np.linalg.norm(d, axis=-1) < 0.02
    base = paint(base, (0.08, 0.09, 0.08), (eye & near) * smooth01(0.6, 0.95, look) * 0.6)
rough[eye] = 0.04
# The gums dark and wet, the teeth brown and yellowed, darker at their roots.
gum = is_("waker_gum")
base = paint(base, (0.06, 0.016, 0.014), gum * (0.8 + 0.3 * noise(6207, 30.0)))
rough = np.where(gum, 0.22, rough)
tooth = is_("waker_tooth")
root = smooth01(0.006, 0.0, np.abs(P[..., 2] - LIPS))
base = paint(base, (0.17, 0.12, 0.055), tooth * (0.8 + 0.3 * noise(6208, 80.0)))
base = paint(base, (0.08, 0.05, 0.025), tooth * root * 0.8)
rough = np.where(tooth, 0.3, rough)
# The nails black and horny.
nail = is_("waker_nail")
base = paint(base, (0.018, 0.013, 0.01), nail)
rough = np.where(nail, 0.35, rough)
# The hair fox-red with the bog, darker in its ropes.
hair_ = is_("waker_hair")
strands = 0.5 + 0.5 * np.sin(P[..., 0] * 700 + 5 * noise(6209, 20.0))
base = paint(base, (0.2, 0.055, 0.018), hair_ * (0.7 + 0.4 * strands))
base = paint(base, (0.05, 0.02, 0.01), hair_ * smooth01(0.6, 0.9, noise(6210, 25.0)) * 0.6)
rough = np.where(hair_, 0.6, rough)
# The noose: old hemp, black with the peat.
rope_ = is_("waker_rope")
base = paint(base, (0.13, 0.09, 0.05), rope_ * (0.7 + 0.4 * noise(6211, 40.0)))
base = paint(base, (0.03, 0.022, 0.015), rope_ * smooth01(0.4, 0.8, noise(6212, 12.0)) * 0.7)
rough = np.where(rope_, 0.85, rough)
# The peat: black-brown, fibrous; the moss: sphagnum, green with red tips.
peat_ = is_("waker_peat")
fibre = 0.5 + 0.5 * np.sin(P[..., 0] * 900 + 4 * noise(6213, 30.0))
base = paint(base, (0.035, 0.024, 0.016), peat_)
base = paint(base, (0.08, 0.05, 0.028), peat_ * fibre * 0.5)
rough = np.where(peat_, 0.8, rough)
moss_ = is_("waker_moss")
base = paint(base, (0.075, 0.085, 0.025), moss_ * (0.7 + 0.4 * noise(6214, 50.0)))
base = paint(base, (0.12, 0.04, 0.02), moss_ * smooth01(0.6, 0.9, noise(6215, 70.0)) * 0.6)
rough = np.where(moss_, 0.75, rough)
base = np.clip(base, 0, 1)
rough = np.clip(rough + 0.04 * noise(6216, 12.0), 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye | tooth, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.012, 0.009, 0.007), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("waker", "the Waker (the Peat Man), modelled in tools/blender/waker.py, its colour painted here"),
             rough=rough, lod=0.35)
