"""HOTBOX (GDD §21 structural, App. A.4; docs/design/creatures/hotbox.md §3; ARCHITECTURE §8 note 367):
tools/blender/hotbox.py's axle parasite with its colour, its fine relief and its glow baked into one 2048 atlas (the Look
Review: organic, not tiled library textures on boxes; the Gannet's and the Ribbit's way).

tools/blender/hotbox.py stays its source: the rig, the fused skin and the parts over it, the clips. This recipe runs it,
unwraps every part into one atlas (the head and the mandibles given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the plates ridged, pitted and
    flashed like a rough casting, their rims knobbed; the abdomen's segments creased across and stretched shiny; the
    legs' joints ringed like rivets;
  * paints the colour from where each texel is on it at rest (deterministic, no render): the plates scorched purple-brown
    and black, heat-blued in streaks, rusty orange on the ridges, sooted and greasy in every seam; the abdomen glossy, a
    deep red-orange under its skin, blackened in its creases; the head and the legs black chitin with a brown sheen, the
    claws black and glossy;
  * and paints its glow: an emission map over the abdomen, brightest in the middle of each swollen segment, none in its
    creases: the engine draws those texels lit, scaled by the instance's glow (CreatureArt: dim knocking, bright glowing,
    white-hot seized).

    tools/models/build.sh hotbox        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/hotbox.py)
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
import texels  # noqa: E402
from overbake import fine, smooth01  # noqa: E402
from texels import paint, suffix  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("hotbox.py")
print("[dt] hotbox parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()
SEGS = g["SEGS"]

SOFT = {"hotbox_belly": 3, "hotbox_chitin": 2, "hotbox_plate": 0, "hotbox_leg": 0, "hotbox_claw": 0}


def dress(m):
    k = suffix(m.name)
    return (make.flat("hotbox_high", (0.3, 0.3, 0.3), rough=0.5), SOFT[k]) if k in SOFT else None


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def casting(p, n):
    # A rough casting: ridges running over it, pitted all over, blistered with flash.
    d = 0.003 * (ridged(p, 3711, 9.0) ** 4) - 0.0022 * smooth01(0.55, 0.85, cook.noise_np(p, 3712, 45.0) * 0.5 + 0.5)
    d += 0.0016 * smooth01(0.7, 0.95, cook.noise_np(p, 3713, 16.0) * 0.5 + 0.5)
    return d + fine(p, 0.0004, 220, 3714)


def segmented(p, n):
    # The abdomen's skin creased across its segments, stretched smooth between.
    return 0.0012 * np.sin(p[:, 1] * 2 * np.pi / 0.03) ** 8 - 0.0006 * smooth01(0.9, 0.97, ridged(p, 3721, 20.0)) + fine(p, 0.0001, 600, 3722)


def ringed(p, n):
    return 0.0008 * np.sin(np.linalg.norm(p, axis=1) * 2 * np.pi / 0.025) ** 6 + fine(p, 0.0002, 400, 3731)


SHAPE = {"iron_plate.hotbox_plate": casting, "sac.hotbox_belly": segmented, "tar.hotbox_chitin": casting, "tar.hotbox_leg": ringed,
         "tar.hotbox_claw": lambda p, n: 0 * p[:, 0]}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] hotbox highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("hotbox_chitin", "hotbox_claw") else 0


atlas = overbake.Atlas("hotbox", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.006, 0.02), "plates": (0.006, 0.02), "legs": (0.003, 0.01)}, samples=16, height=0.6)

t = texels.Texels(atlas, S)
P, N = t.P, t.N
base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.5, np.float32)
emit = np.zeros((S, S, 3), np.float32)
up = N[..., 2]

# The plates: scorched purple-brown and black, heat-blued in streaks, the ridges rubbed to a rusty orange, the seams black
# with soot and grease.
plate = t.is_("hotbox_plate")
base[plate] = np.array((0.075, 0.04, 0.055), np.float32) * (0.75 + 0.5 * t.noise(3741, 5.0))[plate][:, None]
base = paint(base, (0.02, 0.016, 0.02), plate * smooth01(0.3, 0.75, t.noise(3742, 7.0) * 0.5 + 0.5) * 0.7)
base = paint(base, (0.06, 0.06, 0.12), plate * smooth01(0.6, 0.9, t.noise(3743, 3.0) * 0.5 + 0.5) * 0.45)
rid = t.field(lambda q: ridged(q, 3711, 9.0) ** 4)
base = paint(base, (0.3, 0.13, 0.06), plate * smooth01(0.5, 0.9, rid) * 0.6)
base = paint(base, (0.16, 0.1, 0.12), plate * smooth01(0.7, 0.95, t.noise(3713, 16.0) * 0.5 + 0.5) * 0.4)
rough[plate] = 0.55
# The abdomen: glossy, a deep red-orange under its stretched skin, darker in its creases and underneath; its glow brightest
# in the middle of each segment.
belly = t.is_("hotbox_belly")
seg_y = np.array([s[0] for s in SEGS], np.float32)
mid = np.min(np.abs(P[..., 1][..., None] - seg_y[None, None, :]), axis=-1)
swell = smooth01(0.07, 0.0, mid)
base[belly] = np.array((0.42, 0.11, 0.035), np.float32) * (0.8 + 0.3 * t.noise(3751, 8.0))[belly][:, None]
base = paint(base, (0.08, 0.02, 0.012), belly * (1 - swell) * 0.7)
vein = smooth01(0.88, 0.96, t.field(lambda q: ridged(q, 3752, 16.0)))
base = paint(base, (0.12, 0.02, 0.01), belly * vein * 0.6)
rough[belly] = 0.12
heat = belly * (0.35 + 0.65 * swell) * (1 - 0.8 * vein) * (0.85 + 0.3 * t.noise(3753, 6.0))
emit = np.array((1.0, 0.36, 0.07), np.float32) * np.clip(heat, 0, 1)[..., None] * 0.6
# The head and the legs: black chitin, a brown sheen; the claws and the mandibles' points black and glossy.
chitin = t.is_("hotbox_chitin", "hotbox_leg")
base[chitin] = np.array((0.035, 0.024, 0.024), np.float32) * (0.8 + 0.4 * t.noise(3761, 30.0))[chitin][:, None]
base = paint(base, (0.12, 0.07, 0.05), chitin * smooth01(0.6, 0.9, t.noise(3762, 20.0) * 0.5 + 0.5) * 0.4)
rough[chitin] = 0.4
claw = t.is_("hotbox_claw")
base[claw] = (0.01, 0.008, 0.008)
rough[claw] = 0.2
base = np.clip(base, 0, 1)
ao = atlas.maps["AO"][..., 0]
gentle = belly
ao = np.where(gentle, ao ** 0.5, ao)
base = base * (0.3 + 0.7 * ao)[..., None]
k = np.clip((1 - ao) * 2.2, 0, 1) * 0.5
base = base * (1 - k)[..., None] + np.array((0.012, 0.01, 0.01), np.float32) * k[..., None]
texels.with_glow("hotbox", emit)
atlas.finish(base, kit, arm, made=make.provenance("hotbox", "Hotbox, modelled in tools/blender/hotbox.py, its colour painted here"),
             rough=rough, lod=0.4)
