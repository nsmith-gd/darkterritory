"""CINDER HOUND (GDD §21 rear, App. A.3): the burnt, stilt-legged hound of tools/blender/cinder_hound.py (the director's
concept sheet, 7 Oct 2026; ARCHITECTURE §8 note 312), taken to the fidelity target (note 58, tools/models overbake).

tools/blender/cinder_hound.py stays its source: SK_Quad, the weights, the clips (prowl, run, crouch, lunge, board, hit,
bite) and the game mesh. This recipe runs it and models a high-resolution copy over it:
  * the hide burnt to charred plates (the library's pine bark, black): scales of char split by cracks, lifted at their
    edges, the skin shrunk onto the frame under them, every rib a bar down the keel, the spine a ridge of knuckles;
  * the faceted skull the same char, its planes cut sharper, the brow heavy over the sockets;
  * the thorn ridges, the leg plates, the spurs and the shards (the library's slag and mineral growth) pitted and
    blistered like clinker, the shards glassy;
  * the singed fur (the library's fleece, burnt brown) in strands; the claws black and glassy;
  * the gums and teeth in the library's flesh, the teeth dead ivory, cracked;
and bakes it into one 1024 atlas on the game mesh, the grime settling into every crease. The cracks keep their fire:
the ember-crack faces, the cracks' cores and the flame off the shards are left on their own layers (their glow is the
tell), and so are the eyes.

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
    "char.hound": ("pine_bark", 7.0, (0.16, 0.14, 0.13), 0.85, 2),
    "thorn.hound": ("slag", 6.0, (0.42, 0.26, 0.2), 0.7, 0),
    "fur.hound": ("fleece", 9.0, (0.55, 0.38, 0.24), 0.95, 0),
    "claw.hound": ("slag", 10.0, (0.12, 0.11, 0.11), 0.35, 0),
    "mineral_growth.hound": ("mineral_growth", 4.0, (0.42, 0.4, 0.46), 0.35, 0),
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
    # Charred plates: scales of char lying back along the body, split by cracks between them, lifted at their edges
    # (noise ridged into a cell-like crazing; the cracks are where it crosses zero).
    cell = cook.noise_np(p * np.array([34.0, 22.0, 34.0]), 3, 1.0)
    crack = bell(cell / 0.07)
    lift = np.clip(np.abs(cell) * 3, 0, 1)
    d = 0.006 * lift - 0.007 * crack + 0.002 * cook.noise_np(p, 4, 60)
    # Starved: the ribs as bars down the keel's sides, the gaps between them sunk.
    barrel = smooth01(-0.14, -0.08, y) * smooth01(0.32, 0.26, y) * smooth01(0.03, 0.06, ax) * smooth01(0.7, 0.64, z) \
        * smooth01(0.42, 0.47, z)
    d += 0.012 * barrel * (np.maximum(0, np.sin(y * 64 + 0.6)) - 0.35)
    # The spine's knuckles along the top, the hips and the shoulder blades standing up out of the hide.
    top = bell(ax / 0.018) * smooth01(0.64, 0.67, z) * smooth01(-0.58, -0.5, y) * smooth01(0.45, 0.4, y)
    d += 0.01 * top * np.maximum(0, np.sin(y * 90))
    for (cy, cz) in ((-0.47, 0.67), (0.3, 0.7)):
        d += 0.014 * bell(np.hypot(y - cy, z - cz) / 0.035) * smooth01(0.05, 0.09, ax)
    # Sinew down the legs under the char: tendons as ridges down the back of each shin.
    legs = smooth01(0.42, 0.36, z)
    d += 0.004 * legs * np.sin(np.arctan2(x - np.sign(x) * 0.1, y - np.where(y > 0, 0.29, -0.47)) * 5 + z * 20)
    # The skull: its planes cut sharper (the char's crazing finer there), the brow heavy over the sockets.
    face = smooth01(0.58, 0.62, y)
    d += face * (0.003 * np.sign(cook.noise_np(p, 5, 45)) - 0.002 * crack)
    return d + fine(p, 0.0008, 120)


def fur(p, n):
    # Singed strands: grooves run along each blade, the ends crisped.
    return 0.0015 * np.sin(cook.noise_np(p, 12, 30) * 40) + 0.001 * cook.noise_np(p, 13, 200)


def slag(p, n):
    # Clinker: blistered, pitted, glassy.
    blister = np.maximum(0, cook.noise_np(p, 7, 55)) ** 2
    pits = np.minimum(0, cook.noise_np(p, 8, 90))
    return 0.006 * blister + 0.003 * pits + 0.003 * cook.noise_np(p, 9, 20)


def teeth(p, n):
    return -0.0008 * bell(cook.noise_np(p, 10, 200) / 0.1)


SHAPE = {"char.hound": hide, "thorn.hound": slag, "claw.hound": lambda p, n: 0.0005 * cook.noise_np(p, 14, 80),
         "mineral_growth.hound": slag, "fur.hound": fur, "flesh.teeth": teeth,
         "flesh.gums": lambda p, n: 0.001 * cook.noise_np(p, 11, 150)}
BAKED = ["body", "skull", "thorn", "fur"]
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
    for view, d, c, dist in (("side", (1, 0.15, 0.2), (0, 0, 0.5), 2.4), ("head", (0.6, 1, 0.3), (0, 0.8, 0.76), 0.7),
                             ("back", (0.5, -0.4, 1), (0, -0.1, 0.66), 1.5)):
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
atlas.bake(groups, cages={"body": (0.012, 0.03), "skull": (0.008, 0.02), "thorn": (0.008, 0.025), "fur": (0.004, 0.012)},
           height=1.1, hide=[parts["cracks"]])
# Soot deep in every crease; ash settled on the back; the paws and the belly caked in the ballast's mud.
soot = np.array([0.012, 0.011, 0.01], np.float32)
base = atlas.base(soot=soot, crease=0.55)
z = atlas.height(1.1)
mud = np.array([0.045, 0.037, 0.03], np.float32)
low_down = np.clip((0.16 - z) / 0.14, 0, 1) * 0.5
base = base * (1 - low_down)[..., None] + mud * low_down[..., None]
ash = np.array([0.09, 0.085, 0.08], np.float32)
settled = np.clip((z - 0.68) / 0.08, 0, 1) * 0.12
base = base * (1 - settled)[..., None] + ash * settled[..., None]
atlas.finish(base, kit, arm, made=make.provenance("cinder_hound", "the Cinder Hound's hide, modelled over "
                                                                 "tools/blender/cinder_hound.py"))
