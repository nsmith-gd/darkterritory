"""THE CAR HUGGER (GDD v1.2 §21 rear, App. A.3): the thing of tools/blender/car_hugger.py, taken to the fidelity target
(ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/car_hugger.py stays its source: the chain rig, the clips and the game mesh. This recipe runs it, models
a high-resolution copy and bakes it into one 1024 atlas:
  * the hide waxy and slack, grey going to bruised violet, marbled like a drowned body's, folded where it sags,
    veined; dirt from the ballast caked on its belly and its dragged tail;
  * the hands and the lips paler, the knuckles creased, the fingertips rust-brown from the steel they're dug into;
  * the mouth's gums red-black and ridged in rings down the funnel, the teeth dead ivory, stained at the root;
  * the eaten iron rusted and pitted, the hide swollen and inflamed round it where it's grown in;
and wet: the hide shines where the guard van's lamp finds it (rough 0.3).

    tools/models/build.sh car_hugger
    CAR_HUGGER_PREVIEW=1 tools/models/build.sh car_hugger   # renders the high copy to out/review/car_hugger-high-*.png
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

kit, g, arm, parts = overbake.hold("car_hugger.py")
print("[dt] car_hugger parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

MOUTH = np.array(g["MOUTH"], np.float32)
ARMS = g["ARMS"]
# Where the eaten plates sit (the hide swells round them).
PLATE_AT = [np.array(g["on_body"](y, ang)[0], np.float32) for y, ang, _, _ in g["PLATES"]]

# The hide is a plain waxy grey-violet, and all its colour is painted from noise (the marbling, the bruises, the veins,
# the back darker and the belly paler): the library's flesh repeats every half metre, and over a body four metres long
# its blotches tile into a pattern whatever its scale. The hands, lips and gums are small enough to wear it (the hands
# and lips paler, the gums red-black).
DRESS = {
    "flesh.hugger_pale": (lambda: make.lib("flesh", 3.0, (1.75, 1.85, 1.95), 0.4), 2),
    "flesh.hugger_gum": (lambda: make.lib("flesh", 5.0, (0.62, 0.12, 0.15), 0.25), 2),
    "flesh.hugger": (lambda: make.flat("hide", (0.28, 0.25, 0.27), rough=0.35), 2),
    "tar.throat": (lambda: make.flat("throat", (0.012, 0.005, 0.005), rough=0.3), 1),
    "skin.tooth": (lambda: make.flat("tooth", (0.6, 0.55, 0.44), rough=0.35), 0),
    "rust_heavy.eaten": (lambda: make.lib("rust_heavy", 2.5, (1.0, 1.0, 1.0), 0.75), 0),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"car_hugger: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def near_plates(p, r):
    return np.max([bell(np.linalg.norm(p - c, axis=1) / r) for c in PLATE_AT], axis=0)


def hide(p, n):
    # Slack and folded: big sagging folds (stretched along the body), finer creases, and veins standing in it.
    d = 0.018 * (ridged(p * np.array([1.0, 0.4, 1.0], np.float32), 101, 4.0) ** 3 - 0.3)
    d += 0.005 * (ridged(p, 102, 16.0) ** 4 - 0.25)
    d += 0.0025 * ridged(p, 103, 7.0) ** 14
    # Swollen round the eaten iron.
    d += 0.02 * near_plates(p, 0.35)
    return d + fine(p, 0.0006, 180, 104)


def pale(p, n):
    # Knuckle creases across the fingers and the lips' radial creases.
    d = 0.0015 * np.sin(np.linalg.norm(p[:, :2], axis=1) * 160 + 2 * cook.noise_np(p, 111, 8.0))
    rel = p - MOUTH
    around = np.arctan2(rel[:, 2], rel[:, 0])
    lips = bell((np.hypot(rel[:, 0], rel[:, 2]) - 0.52) / 0.12) * smooth01(0.0, -0.2, np.abs(rel[:, 1]) - 0.2)
    d += 0.004 * lips * np.cos(around * 34)
    return d + fine(p, 0.0005, 220, 112)


def gum(p, n):
    rel = p - MOUTH
    return 0.005 * np.sin(rel[:, 1] * 60) + 0.003 * cook.noise_np(p, 121, 25.0)


SHAPE = {"flesh.hugger_pale": pale, "flesh.hugger_gum": gum, "flesh.hugger": hide,
         "rust_heavy.eaten": lambda p, n: 0.002 * (ridged(p, 131, 40.0) ** 3 - 0.4)}
BAKED = ["body", "hands", "mouth", "iron"]   # (the arms grown into the body: rig.fuse)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] car_hugger highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def wear(p, kind):
    """R: bruising (the hide's violet blotches); G: ballast dirt caked on the belly and the tail; B: rust and old blood
    (the mouth's rim and the teeth's roots, the fingertips, round the eaten iron)."""
    out = np.zeros((len(p), 3), np.float32)
    flesh = kind.startswith("flesh")
    if flesh:
        out[:, 0] = smooth01(0.3, 0.75, cook.noise_np(p, 201, 2.6)) * (0.6 + 0.4 * cook.noise_np(p, 202, 9.0))
    out[:, 1] = smooth01(-0.25, -0.85, p[:, 2]) * (0.6 + 0.4 * cook.noise_np(p, 203, 6.0)) * (1.0 if not kind.startswith("skin") else 0.3)
    rel = p - MOUTH
    rim = bell((np.hypot(rel[:, 0], rel[:, 2]) - 0.5) / 0.14) * smooth01(-0.6, -0.1, rel[:, 1])
    tips = np.zeros(len(p), np.float32)
    for n, s, e, w, gd in ARMS:
        wv = np.array(w, np.float32)
        tips = np.maximum(tips, smooth01(0.3, 0.55, np.linalg.norm(p - wv, axis=1)))
    tips *= kind.startswith("flesh.hugger_pale")
    root = kind.startswith("skin.tooth") * smooth01(0.35, 0.25, np.linalg.norm(p - MOUTH, axis=1))
    out[:, 2] = np.clip(rim * 0.8 + tips * 0.45 + root + near_plates(p, 0.3) * flesh * 0.8, 0, 1)
    return out


CENTRE = np.array([[y, g["centre_z"](y)] for y in np.linspace(-4.0, 0.6, 47)], np.float32)


def veins(p, kind):
    """R: veins, dark under the waxy skin; G: its wet sheen's pools (the hollows keep the wet longest); B: which way
    the hide faces, 1 on its back and 0 under its belly (the back darkens, as a leech's does; the belly's the colour
    of something that's lain in water)."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("flesh.hugger") and not kind.startswith("flesh.hugger_gum"):
        out[:, 0] = smooth01(0.82, 0.95, ridged(p, 211, 5.0)) * smooth01(0.0, 0.5, cook.noise_np(p, 212, 2.0))
    out[:, 1] = smooth01(0.2, 0.7, cook.noise_np(p, 213, 3.0))
    zc = np.interp(p[:, 1], CENTRE[:, 0], CENTRE[:, 1])
    out[:, 2] = smooth01(-0.35, 0.35, p[:, 2] - zc + 0.15 * cook.noise_np(p, 214, 3.0))
    return out


def marbling(p, kind):
    """R: fine mottling (the skin's own unevenness); G: marbling, the dark branching a drowned body shows under the skin;
    B: pallor, blotches gone waxy white. The hide only: the rest wears the library's."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("flesh.hugger") and not kind.startswith(("flesh.hugger_pale", "flesh.hugger_gum")):
        out[:, 0] = np.clip(0.5 + 0.35 * cook.noise_np(p, 221, 14.0) + 0.15 * cook.noise_np(p, 222, 40.0), 0.05, 1)
        out[:, 1] = smooth01(0.7, 0.9, ridged(p, 223, 3.2)) * smooth01(-0.1, 0.5, cook.noise_np(p, 224, 1.3))
        out[:, 2] = smooth01(0.35, 0.8, cook.noise_np(p, 225, 2.2))
    return out


if os.environ.get("CAR_HUGGER_PREVIEW"):
    out_dir = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 760, 560
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.8, 0.3, 2.4)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for n, o in parts.items():
        o.hide_render = n in BAKED
    for view, d, c, dist in (("back", (-0.6, -1, 0.8), (0, -1.4, 0.4), 5.5), ("mouth", (0.5, 1.0, 0.25), tuple(MOUTH), 1.6),
                             ("hand", (0.9, 0.3, 0.3), (1.6, 1.3, 2.8), 1.0)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"car_hugger-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] car_hugger preview -> out/review/car_hugger-high-*.png")
    sys.exit(0)

MOUTHK = 1


def kind_of(m):
    return MOUTHK if m.name.startswith("flesh.hugger_gum") or m.name.startswith("skin.tooth") else 0


# 2048: it wraps most of a car (48 m² of skin), and GDD §27 wants 256 px/m on what's seen close (look.json
# bigHeroLayerSize keeps it at full size).
atlas = overbake.Atlas("car_hugger", parts, BAKED, kind_of, size=2048)
atlas.unwrap(boosts={MOUTHK: 1.6})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.04, 0.08), "hands": (0.02, 0.05), "mouth": (0.02, 0.05), "iron": (0.02, 0.04)},
           height=3.0, masks={"wear": wear, "veins": veins, "marbling": marbling})
M = atlas.maps
worn, vein, marb = M["wear"], M["veins"], M["marbling"]
base = atlas.base(soot=(0.02, 0.014, 0.016), crease=0.5, ao_floor=0.45)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


hide = atlas.masks["body"][..., None]
skin = (marb[..., 0] > 0.02)[..., None]
base = np.where(skin, base * (0.8 + 0.4 * marb[..., 0:1]), base)                        # mottled
base = paint(base, (0.34, 0.33, 0.3), marb[..., 2] * 0.3)                               # waxy pallor
base = paint(base, (0.07, 0.05, 0.06), marb[..., 1] * 0.55)                             # marbling
base = np.where(hide, base * (1.0 - 0.3 * vein[..., 2:3]), base)                      # the back darker
base = np.where(hide, paint(base, (0.3, 0.3, 0.22), (1 - vein[..., 2]) * 0.3), base)  # the belly waterlogged
base = paint(base, (0.1, 0.05, 0.1), worn[..., 0] * 0.4)       # bruised violet
base = paint(base, (0.05, 0.02, 0.05), vein[..., 0] * 0.6)      # the veins
base = paint(base, (0.08, 0.014, 0.012), worn[..., 2] * 0.7)    # old blood and rust
base = paint(base, (0.05, 0.042, 0.034), worn[..., 1] * 0.75)   # ballast dirt, caked
rough = np.full(base.shape[:2], 0.3, np.float32)
rough[atlas.masks["iron"]] = 0.75
rough = rough - 0.12 * vein[..., 1]                            # the wet pooling in the hollows
rough = np.where(worn[..., 1] > 0.35, np.maximum(rough, 0.7), rough)
atlas.finish(base, kit, arm, made=make.provenance("car_hugger", "the Car Hugger, modelled over tools/blender/car_hugger.py"), rough=rough, lod=0.4)
