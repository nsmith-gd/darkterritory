"""THE WEIGHT (GDD §21 rear, App. A.3): the heap of bog bodies of tools/blender/weight.py, taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/weight.py stays its source: the chain rig, the weights, the clips (grab, drag, release) and the game
mesh. This recipe runs it and models a high-resolution copy:
  * the hide as the peat leaves a body: tanned to leather, brown-black, folded and creased over itself, sagging
    toward the ground in wrinkles, the bones of the backs showing through it (spines as ridges, the blades up);
  * the faces and hands the thinner skin, shrunk tight over the bone, the knuckles standing;
and bakes it into one 1024 atlas on the game mesh, the peat still wet and caked on everything low down.

    tools/models/build.sh weight
    WEIGHT_PREVIEW=1 tools/models/build.sh weight   # renders the high copy to out/review/weight-high-*.png, no bake
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import bell, fine, smooth01  # noqa: E402

kit, g, arm, parts = overbake.hold("weight.py")
print("[dt] weight parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "flesh.bog_pale": ("skin", 7.0, (0.72, 0.63, 0.53), 0.4, 2),
    "flesh.bog": ("leather", 3.0, (0.6, 0.5, 0.4), 0.35, 2),
    "tar.hole": ("tar", 6.0, (0.06, 0.05, 0.045), 0.2, 0),
}


def dress(m):
    key = next((k for k in DRESS if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"weight: no dress for {m.name}")
    layer, scale, tint, rough, subdiv = DRESS[key]
    return make.lib(layer, scale, tint, rough), subdiv


def ridged(p, seed, scale):
    """Folds: the creases of a noise field turned into ridges (1 - |n|), sharp at the top."""
    return 1 - np.abs(cook.noise_np(p, seed, scale))


TORSOS = [tuple(np.array(v, np.float32) for v in t) for t in g["TORSOS"]]


def hide(p, n):
    # Leather folded over itself: big sagging folds (stretched along the ground, as it slumps), then the finer creases.
    sag = ridged(p * np.array([1.0, 1.0, 2.6]), 21, 7.0)
    d = 0.02 * (sag ** 3 - 0.3)
    d += 0.007 * (ridged(p, 22, 26.0) ** 4 - 0.25)
    d -= 0.003 * bell(cook.noise_np(p, 23, 70.0) / 0.12)
    # The bodies: down each back the spine's knuckles, the blades to either side, and the ribs as bars round the chest,
    # the skin sunk between them.
    for hips, sh, up in TORSOS:
        ax = sh - hips
        L = float(np.linalg.norm(ax))
        ax, up = ax / L, up / np.linalg.norm(up)
        side = np.cross(ax, up)
        q = p - hips
        u = q @ ax / L                # 0 at the hips, 1 at the shoulders
        v = q @ side                  # across the back
        w = q @ up                    # up off it
        on = smooth01(-0.05, 0.05, u) * smooth01(1.1, 1.0, u) * smooth01(-0.02, 0.04, w)
        d += 0.012 * on * bell(v / 0.016) * np.maximum(0, np.sin(u * L * 110))
        chest = smooth01(0.45, 0.55, u) * smooth01(0.95, 0.85, u) * smooth01(-0.2, 0.05, w)
        d += 0.01 * chest * (np.maximum(0, np.sin(u * L * 55)) - 0.3) * smooth01(0.03, 0.08, np.abs(v))
        blade = smooth01(0.02, 0.06, w)
        for sgn in (-1, 1):
            d += 0.016 * blade * bell(np.hypot(v - sgn * 0.1, (u - 0.85) * L) / 0.06)
    # The arms: the skin loose on them, slack folds ringing the forearms.
    d += 0.004 * np.sin(np.linalg.norm(p[:, :2], axis=1) * 90 + cook.noise_np(p, 24, 6.0) * 3) * smooth01(0.2, 0.35, p[:, 2])
    return d + fine(p, 0.0009, 140, 25)


def thin(p, n):
    # Faces and hands: skin shrunk onto bone, fine wrinkles, the pores gone to leather.
    return 0.0018 * (ridged(p, 31, 120.0) ** 3 - 0.3) + 0.002 * cook.noise_np(p, 32, 40.0) + fine(p, 0.0005, 260, 33)


SHAPE = {"flesh.bog_pale": thin, "flesh.bog": hide, "tar.hole": lambda p, n: 0.0005 * cook.noise_np(p, 43, 200)}
BAKED = ["heap", "faces", "arms", "legs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] weight highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

if os.environ.get("WEIGHT_PREVIEW"):
    out = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 700, 500
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.8, 0.3, 2.2)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for n, o in parts.items():
        o.hide_render = n in BAKED
    for view, d, c, dist in (("rear", (0.7, -1, 0.7), (0, -0.8, 0.0), 3.2), ("face", (0.3, 1, 0.9), (0.0, -0.04, 0.4), 0.6),
                             ("hands", (0.8, -0.6, 0.5), (0.4, 0.2, 0.6), 1.2)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"weight-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] weight preview -> out/review/weight-high-*.png")
    sys.exit(0)


def low(p):
    """Where the peat's still caked on it: the underside and everything down on the stones."""
    return smooth01(-0.05, -0.36, p[:, 2]).astype(np.float32)


atlas = overbake.Atlas("weight", parts, BAKED, lambda m: 0)
atlas.unwrap()
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={name: (0.03, 0.06) for name in BAKED}, height=1.0, masks={"low": low})
# Black in every fold, and the peat caked wet on everything low down. Kept a shade lighter than the hound: nothing
# glows on it, and under the car's end it has only the moon (§26.1: the silhouette has to read).
soot = np.array([0.008, 0.007, 0.006], np.float32)
base = atlas.base(soot=soot, crease=0.45, ao_floor=0.5)
peat = np.array([0.02, 0.015, 0.01], np.float32)
k = atlas.maps["low"][..., None] * 0.5
base = base * (1 - k) + peat * k
atlas.finish(base, kit, arm, made=make.provenance("weight", "the Weight's bog bodies, modelled over tools/blender/weight.py"))
