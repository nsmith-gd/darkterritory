"""CINDER HOUND (GDD §21 rear, App. A.3): the starved lurcher of tools/blender/cinder_hound.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/cinder_hound.py stays its source: SK_Quad, the weights, the clips (prowl, run, crouch, lunge, hit, bite) and
the game mesh. This recipe runs it and models a high-resolution copy over the hide and the slag:
  * the hide gone to oily soot (the library's tar), matted into clumps that lie back along the body, the skin shrunk
    onto the frame: every rib a bar down the barrel, the spine a ridge of knuckles, sinew and tendon standing out down
    the legs, the skull's brow and the muzzle's wrinkles drawn back off the teeth, old scars across the flanks;
  * the slag crest and crusts (the library's mineral growth) pitted and blistered like clinker;
  * the gums and teeth in the library's flesh, the teeth dead ivory, cracked;
and bakes it into one 1024 atlas on the game mesh, the grime settling into every crease. The cracks keep their fire:
the ember-crack faces and the cracks' cores are left on their own layers (their glow is the tell), and so are the eyes.

    tools/models/build.sh cinder_hound
    HOUND_PREVIEW=1 tools/models/build.sh cinder_hound   # renders the high copy to out/review/hound-high-*.png, no bake
"""
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

kit, g, arm, parts = overbake.hold("cinder_hound.py")
print("[dt] hound parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

DRESS = {
    "tar.hound": ("tar", 6.0, (0.32, 0.3, 0.29), 0.75, 2),
    "mineral_growth.hound": ("mineral_growth", 4.0, (0.7, 0.68, 0.74), 0.6, 0),
    "flesh.gums": ("flesh", 8.0, (0.55, 0.3, 0.3), 0.4, 1),
    "flesh.teeth": ("flesh", 10.0, (0.95, 0.88, 0.72), 0.4, 0),
}
# What keeps its own layer: the fire (cracks, cores) and the eyes.
KEEP = ("ember_crack", "ember_core", "eye.")


def dress(m):
    if m.name.startswith(KEEP):
        return None
    key = next((k for k in DRESS if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"cinder_hound: no dress for {m.name}")
    layer, scale, tint, rough, subdiv = DRESS[key]
    return make.lib(layer, scale, tint, rough), subdiv


def hide(p, n):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    # Matted hair: clumps drawn out back along the body (noise stretched along y), a coarser lump under them.
    streak = cook.noise_np(p * np.array([55.0, 14.0, 55.0]), 3, 1.0)
    d = 0.007 * streak + 0.004 * cook.noise_np(p, 4, 30)
    # Starved: the ribs as bars down the barrel's sides, the gaps between them sunk.
    barrel = smooth01(-0.14, -0.08, y) * smooth01(0.32, 0.26, y) * smooth01(0.03, 0.06, ax) * smooth01(0.52, 0.46, z) \
        * smooth01(0.27, 0.32, z)
    d += 0.014 * barrel * (np.maximum(0, np.sin(y * 64 + 0.6)) - 0.35)
    # The spine's knuckles along the top, the hips and the shoulder blades standing up out of the hide.
    top = bell(ax / 0.018) * smooth01(0.47, 0.5, z) * smooth01(-0.58, -0.5, y) * smooth01(0.45, 0.4, y)
    d += 0.01 * top * np.maximum(0, np.sin(y * 90))
    for (cy, cz) in ((-0.47, 0.5), (0.3, 0.52)):
        d += 0.014 * bell(np.hypot(y - cy, z - cz) / 0.035) * smooth01(0.05, 0.09, ax)
    # Sinew down the legs: tendons as ridges down the back of each shin.
    legs = smooth01(0.3, 0.26, z)
    d += 0.005 * legs * np.sin(np.arctan2(x - np.sign(x) * 0.1, y - np.where(y > 0, 0.29, -0.47)) * 5 + z * 20)
    # Old scars: a few long grooves across the flanks, and pale in the hide's grain.
    for k, (sy, sz, ang) in enumerate(((-0.05, 0.43, 0.5), (0.12, 0.38, -0.4), (-0.3, 0.45, 0.3))):
        u = (y - sy) * np.cos(ang) + (z - sz) * np.sin(ang)
        v = -(y - sy) * np.sin(ang) + (z - sz) * np.cos(ang)
        d -= 0.006 * bell(v / 0.005) * bell(u / 0.06) * smooth01(0.04, 0.08, ax)
    # The face: the brow heavy over the sockets, the muzzle's skin wrinkled back off the teeth.
    muzzle = smooth01(0.66, 0.7, y) * smooth01(0.88, 0.84, y)
    d += 0.0025 * muzzle * np.sin(y * 260) * smooth01(0.52, 0.56, z)
    return d + fine(p, 0.0008, 120)


def slag(p, n):
    # Clinker: blistered, pitted, glassy.
    blister = np.maximum(0, cook.noise_np(p, 7, 55)) ** 2
    pits = np.minimum(0, cook.noise_np(p, 8, 90))
    return 0.006 * blister + 0.003 * pits + 0.003 * cook.noise_np(p, 9, 20)


def teeth(p, n):
    return -0.0008 * bell(cook.noise_np(p, 10, 200) / 0.1)


SHAPE = {"tar.hound": hide, "mineral_growth.hound": slag, "flesh.teeth": teeth,
         "flesh.gums": lambda p, n: 0.001 * cook.noise_np(p, 11, 150)}
BAKED = ["body", "slag"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] hound highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

if os.environ.get("HOUND_PREVIEW"):
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
    for view, d, c, dist in (("side", (1, 0.15, 0.2), (0, 0, 0.4), 2.2), ("head", (0.6, 1, 0.3), (0, 0.72, 0.58), 0.6),
                             ("back", (0.5, -0.4, 1), (0, -0.1, 0.5), 1.4)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"hound-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] hound preview -> out/review/hound-high-*.png")
    sys.exit(0)


def kind(m):
    return overbake.Atlas.KEEP if m.name.startswith(KEEP) else 0


atlas = overbake.Atlas("cinder_hound", parts, BAKED, kind)
atlas.unwrap()
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
# (Modelled at a hound's scale, a few mm over its game mesh: a tighter cage than a person's coat.)
atlas.bake(groups, cages={"body": (0.012, 0.03), "slag": (0.01, 0.03)}, height=0.7, hide=[parts["cracks"]])
# Soot deep in every crease; ash settled on the back; the paws and the belly caked in the ballast's mud.
soot = np.array([0.012, 0.011, 0.01], np.float32)
base = atlas.base(soot=soot, crease=0.55)
z = atlas.height(0.7)
mud = np.array([0.045, 0.037, 0.03], np.float32)
low_down = np.clip((0.2 - z) / 0.18, 0, 1) * 0.5
base = base * (1 - low_down)[..., None] + mud * low_down[..., None]
ash = np.array([0.09, 0.085, 0.08], np.float32)
settled = np.clip((z - 0.5) / 0.08, 0, 1) * 0.12
base = base * (1 - settled)[..., None] + ash * settled[..., None]
atlas.finish(base, kit, arm, made=make.provenance("cinder_hound", "the Cinder Hound's hide, modelled over "
                                                                 "tools/blender/cinder_hound.py"))
