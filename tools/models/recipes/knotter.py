"""THE KNOTTER (GDD §21 structural, App. A.4; docs/design/creatures/knotter.md §3; ARCHITECTURE §8 note 365):
tools/blender/knotter.py's living hawser with its colour and its fine relief baked into one 1024 atlas (the Look Review:
organic, not tiled library textures on boxes; the Gannet's and the Ribbit's way).

tools/blender/knotter.py stays its source: the rig, the fused skin and the parts over it, the clips. This recipe runs it,
unwraps every part into one atlas (the claws given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: each strand's fibres laid up the
    other way round from the lay, as a rope's yarns are, worn flat along the ridges; the whipping's turns of tarred twine;
    the knots' folds; the legs' and the claws' joints;
  * paints the colour from where each texel is on it at rest (deterministic, no render): pale grey-white flesh, waxy, the
    ridges worn paler and yellowed, the grooves between the strands dirty with soot and grease, bruised grey-violet in
    places, faint veins; the underside paler; the whipping black and tarry, glossy; the legs pale, darker at the hooks;
    the claws grey, darkening to black horn at their tips.

    tools/models/build.sh knotter        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/knotter.py)
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
import traintexels as texels  # noqa: E402
from overbake import fine, smooth01  # noqa: E402
from traintexels import paint, suffix  # noqa: E402

S = 1024  # the roster's human- and small-creature atlas (the crew's, the Gaunt's): every renderer loads every atlas, and ten at 2048 took the CI runner over (note 367)
kit, g, arm, parts = overbake.hold("knotter.py")
print("[dt] knotter parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()
PITCH = float(g["PITCH"])
END = float(g["END"])
WHIP = [float(w) for w in g["WHIP"]]

SOFT = {"knotter": 3, "knotter_under": 3, "knotter_whip": 2, "knotter_leg": 1, "knotter_claw": 0}


def dress(m):
    k = suffix(m.name)
    return (make.flat("knotter_high", (0.3, 0.3, 0.3), rough=0.5), SOFT[k]) if k in SOFT else None


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def fibres(p):
    """The yarns, laid up the other way round from the strands (a left-hand twist in a right-hand lay), fine and close."""
    th = np.arctan2(p[:, 2], p[:, 0])
    return np.sin(36 * (th + 2 * np.pi * p[:, 1] / (PITCH * 0.55)) + 2 * cook.noise_np(p, 3651, 18.0))


def lay(p, n):
    near = smooth01(END + 0.02, END - 0.1, np.abs(p[:, 1]))
    d = 0.0011 * fibres(p) * near + 0.0014 * (ridged(p, 3652, 30.0) ** 3) * (1 - near)
    return d + fine(p, 0.00025, 350, 3653)


def whip(p, n):
    # Turns of tarred twine round it, a few millimetres each.
    return 0.0012 * np.sin(p[:, 1] * 2 * np.pi / 0.007 + 1.5 * cook.noise_np(p, 3661, 25.0)) + fine(p, 0.0002, 400, 3662)


def joint(p, n):
    return 0.0008 * (ridged(p, 3671, 60.0) ** 2) + fine(p, 0.0002, 500, 3672)


SHAPE = {"flesh.knotter_under": lay, "flesh.knotter": lay, "tar.knotter_whip": whip, "skin.knotter_leg": joint, "tar.knotter_claw": joint}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] knotter highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) == "knotter_claw" else 0


atlas = overbake.Atlas("knotter", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.6})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "legs": (0.003, 0.01), "claws": (0.004, 0.012)}, samples=16, height=1.0)

t = texels.Texels(atlas, S)
P, N = t.P, t.N
base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.35, np.float32)
up = N[..., 2]

# The flesh: pale grey-white and waxy; the strands' ridges worn paler and yellowed (where the cars' weight rubs), the
# grooves dirty with soot and grease, the knots and the underside paler, bruised grey-violet in patches, faint veins.
flesh = t.is_("knotter", "knotter_under")
base[flesh] = np.array((0.5, 0.48, 0.44), np.float32) * (0.9 + 0.18 * t.noise(3681, 6.0))[flesh][:, None]
r = np.linalg.norm(P[..., [0, 2]], axis=-1)
ridge = smooth01(0.06, 0.085, r) * smooth01(END + 0.05, END - 0.2, np.abs(P[..., 1]))
base = paint(base, (0.62, 0.58, 0.48), flesh * ridge * 0.6)
groove = smooth01(0.074, 0.05, r) * smooth01(END, END - 0.15, np.abs(P[..., 1]))
base = paint(base, (0.09, 0.075, 0.06), flesh * groove * 0.85)
base = paint(base, (0.3, 0.27, 0.3), flesh * smooth01(0.55, 0.85, t.noise(3682, 3.0) * 0.5 + 0.5) * 0.5)
base = paint(base, (0.2, 0.17, 0.14), flesh * smooth01(0.3, 0.8, t.noise(3683, 9.0) * 0.5 + 0.5) * smooth01(0.3, 0.9, up) * 0.45)
vein = smooth01(0.9, 0.97, t.field(lambda q: ridged(q, 3684, 14.0)))
base = paint(base, (0.32, 0.22, 0.24), flesh * vein * 0.5)
base = paint(base, (0.62, 0.6, 0.56), t.is_("knotter_under") * 0.35)
rough[flesh] = 0.32
# The whipping: tarred twine, black, glossy where it's wet, its turns catching the light.
whip_ = t.is_("knotter_whip")
base[whip_] = np.array((0.018, 0.016, 0.014), np.float32) * (0.8 + 0.4 * t.noise(3691, 40.0))[whip_][:, None]
rough[whip_] = 0.25
# The legs: pale, translucent-looking, darker toward the hooks.
leg = t.is_("knotter_leg")
base[leg] = (0.56, 0.53, 0.47)
base = paint(base, (0.18, 0.15, 0.13), leg * smooth01(-0.05, -0.12, P[..., 2]))
rough[leg] = 0.3
# The claws: grey-white like the rope at their roots, darkening down their length to black horn at the tips (by how near
# each texel is to its claw's own tip: some curl back along the lay, so not by how far along the lay it is), the knuckle
# rings a shade darker.
TIPS = np.array([tuple(v) for v in g["TIPS"]], np.float32)
claws_part = t.part("claws")
tip_near = np.full((S, S), 9.0, np.float32)
idx = np.nonzero(claws_part)
for tip in TIPS:
    tip_near[idx] = np.minimum(tip_near[idx], np.linalg.norm(P[idx] - tip, axis=-1))
base[claws_part] = np.array((0.46, 0.44, 0.4), np.float32) * (0.9 + 0.15 * t.noise(3685, 20.0))[claws_part][:, None]
base = paint(base, (0.2, 0.19, 0.17), claws_part * smooth01(0.24, 0.1, tip_near) * 0.8)
base = paint(base, (0.015, 0.013, 0.012), claws_part * smooth01(0.11, 0.05, tip_near))
claw = t.is_("knotter_claw")
base = paint(base, (0.012, 0.011, 0.01), claw * 0.85)
rough[claws_part] = 0.3
rough[claw] = 0.2
base = np.clip(base, 0, 1)
ao = atlas.maps["AO"][..., 0]
base = base * (0.3 + 0.7 * ao)[..., None]
k = np.clip((1 - ao) * 2.2, 0, 1) * 0.5
base = base * (1 - k)[..., None] + np.array((0.02, 0.017, 0.014), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("knotter", "the Knotter, modelled in tools/blender/knotter.py, its colour painted here"),
             rough=rough, lod=0.4)
