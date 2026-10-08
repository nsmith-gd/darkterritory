"""TOWER JAW (GDD §21, OUTSIDE; note 363; docs/design/creatures/tower-jaw.md §3 and the director's reference):
tools/blender/tower_jaw.py's corrupted beaver with its colour, its fine relief and the embers in its cracks baked into
one 2048 atlas (the Look Review: organic, not primitives; the Gannet's and the Ribbit's way, notes 340, 361).

tools/blender/tower_jaw.py stays its source: the rig, the fused skin, the fur's spikes, the splinters, the spines, the
incisors and the claws, the clips. This recipe runs it, unwraps every part into one atlas (the face, the teeth and the
claws given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the wet fur's grain combed back
    and down and matted in clumps; the tail's scales; the wood's grain along every splinter, spine and plank claw, their
    ends split; the incisors' chips and grooves;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): the fur dark brown and
    soaked, the clumps' tips lighter and the hollows black, charred black round the spines' roots; the muzzle and the
    paws dark and wet, the nose black and glistening; the tail a dark scaled leather; the splinters weathered grey-brown,
    pale where they're broken fresh; the spines black as char, split by cracks that glow ember-red (an emission map:
    the engine draws those texels lit, scaled by the instance's glow); the incisors iron-dark, rust running down them,
    their chipped edges bare grey metal; the plank claws old dark timber; the eyes black and wet.

    tools/models/build.sh tower_jaw        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/tower_jaw.py)
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
kit, g, arm, parts = overbake.hold("tower_jaw.py")
print("[dt] tower_jaw parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

TEETH = np.array([tuple(p) for p in g["TEETH"]], np.float32)
RIDGE = np.array([tuple(b) for b, _ in g["RIDGE"]], np.float32)
EYES = np.array([tuple(p) for p in g["EYES"]], np.float32)


def suffix(name):
    return name.split(".")[1]


DRESS = {"tj_fur": 2, "tj_muzzle": 2, "tj_nose": 1, "tj_tail": 2, "tj_paw": 2, "tj_tooth": 0, "tj_eye": 1, "tj_claw": 0,
         "tj_splinter": 0, "tj_spine": 0}


def dress(m):
    return make.flat("tj_high", (0.3, 0.3, 0.3), rough=0.4), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


FLOW = np.array([0.0, -0.65, -0.76], np.float32)
FLOW /= np.linalg.norm(FLOW)
ACROSS = np.cross(FLOW, np.array([1.0, 0.0, 0.0], np.float32))


def fur_grain(p):
    """The wet fur combed back and down: streaks long along the way it lies (back along the body and down its flanks),
    tight across it; the streaks' grid warped by a coarser noise so it never lines up into blocks."""
    warp = np.stack([cook.noise_np(p, 1305 + i, 9.0) for i in range(3)], axis=1) * 0.012
    q = p + warp
    frame = np.stack([q[:, 0] * 80.0, q @ FLOW * 9.0, q @ ACROSS * 80.0], axis=1)
    fine_ = np.stack([q[:, 0] * 170.0, q @ FLOW * 20.0, q @ ACROSS * 170.0], axis=1)
    return 0.65 * cook.noise_np(frame, 1301, 1.0) + 0.35 * cook.noise_np(fine_, 1304, 1.0)


def clumps(p):
    return smooth01(0.2, 0.8, (0.7 * cook.noise_np(p, 1302, 16.0) + 0.3 * cook.noise_np(p, 1306, 37.0)) * 0.5 + 0.5)


def fur_shape(p, n):
    return 0.0024 * fur_grain(p) + 0.003 * clumps(p) + fine(p, 0.0003, 260, 1303)


def scales_of(p):
    """The tail's scales: a staggered grid of plates over its paddle."""
    u, v = p[:, 0] * 38.0, p[:, 1] * 30.0
    v2 = v + 0.5 * (np.floor(u) % 2)
    return np.minimum(np.abs(u - np.round(u)), np.abs(v2 - np.round(v2)))


def tail_shape(p, n):
    return 0.0018 * smooth01(0.02, 0.12, scales_of(p)) + fine(p, 0.0002, 300, 1311)


def grain(p, scale=90.0):
    return ridged(p, 1321, scale) ** 3


def wood_shape(p, n):
    return 0.0015 * grain(p) - 0.002 * smooth01(0.9, 0.98, ridged(p, 1322, 30.0)) + fine(p, 0.0003, 200, 1323)


def cracks_of(p):
    """The char's cracks: a net of them, deepest at the spines' roots (where the embers are)."""
    return np.maximum(smooth01(0.94, 0.985, ridged(p, 1331, 26.0)), 0.7 * smooth01(0.955, 0.99, ridged(p, 1332, 55.0)))


def spine_shape(p, n):
    return -0.004 * cracks_of(p) + 0.0015 * grain(p, 60.0) + fine(p, 0.0004, 180, 1333)


def tooth_shape(p, n):
    return -0.0015 * smooth01(0.88, 0.97, ridged(p, 1341, 40.0)) + fine(p, 0.0002, 300, 1342)


SHAPE = {"fleece.tj_fur": fur_shape, "skin.tj_muzzle": lambda p, n: fine(p, 0.0004, 200, 1351), "skin.tj_nose": lambda p, n: fine(p, 0.0003, 300, 1352),
         "leather.tj_tail": tail_shape, "skin.tj_paw": lambda p, n: 0.0012 * clumps(p) + fine(p, 0.0004, 200, 1353),
         "rust_heavy.tj_tooth": tooth_shape, "glass_dirty.tj_eye": lambda p, n: 0 * p[:, 0], "wood_grey.tj_claw": wood_shape,
         "wood_grey.tj_splinter": wood_shape, "wood_grey.tj_spine": spine_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] tower_jaw highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("tj_muzzle", "tj_nose", "tj_tooth", "tj_eye") else 0


atlas = overbake.Atlas("tower_jaw", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.5})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.015, 0.05), "hard": (0.006, 0.02), "hide": (0.006, 0.02)}, samples=16, height=2.4)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] tower_jaw atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint
x, y, z = P[..., 0], P[..., 1], P[..., 2]
up = N[..., 2]
base = np.zeros((S, S, 3), np.float32)
rough = np.full((S, S), 0.5, np.float32)
emit = np.zeros((S, S, 3), np.float32)

# The fur: dark brown and soaked, the clumps' tips lighter, the hollows black; a little redder on the belly.
fur = is_("tj_fur")
gr = field(fur_grain)
cl = field(clumps)
base[fur] = np.array((0.045, 0.026, 0.015), np.float32)
base = paint(base, (0.1, 0.062, 0.036), fur * np.clip(0.5 * gr + 0.6 * cl, 0, 1) * 0.8)
base = paint(base, (0.012, 0.008, 0.006), fur * smooth01(0.1, -0.5, gr) * 0.7)
base = paint(base, (0.07, 0.035, 0.02), fur * smooth01(0.0, -0.6, up) * 0.4)
rough[fur] = 0.3
# Charred black and cracked round the spines' roots, the cracks ember-red and glowing.
char = np.zeros((S, S), np.float32)
for r in RIDGE:
    char = np.maximum(char, smooth01(0.2, 0.05, np.linalg.norm(P - r, axis=-1)))
hide_crack = field(cracks_of)
base = paint(base, (0.012, 0.01, 0.009), fur * char * 0.85)
ember_hide = fur * char * hide_crack * smooth01(0.0, 0.5, noise(3801, 5.0) + 0.3)
base = paint(base, (0.45, 0.08, 0.015), ember_hide)
emit += np.array((1.0, 0.28, 0.05), np.float32) * (ember_hide * 0.15)[..., None]
# The muzzle and the paws: dark, wet, bare; the nose black and glistening; the eyes black and wet.
muz = is_("tj_muzzle")
base[muz] = (np.array((0.035, 0.026, 0.022), np.float32) * (0.8 + 0.4 * noise(3802, 20.0))[muz][:, None])
rough[muz] = 0.2
nose = is_("tj_nose")
base[nose] = (0.008, 0.007, 0.007)
rough[nose] = 0.12
paw = is_("tj_paw")
base[paw] = (np.array((0.028, 0.022, 0.02), np.float32) * (0.8 + 0.4 * noise(3803, 14.0))[paw][:, None])
rough[paw] = 0.35
eye = is_("tj_eye")
base[eye] = (0.005, 0.004, 0.004)
rough[eye] = 0.04
# The tail: a dark scaled leather, each scale's rim paler, mud in the hollows.
tail = is_("tj_tail")
sc = field(scales_of)
base[tail] = (np.array((0.035, 0.03, 0.026), np.float32) * (0.8 + 0.4 * noise(3804, 6.0))[tail][:, None])
base = paint(base, (0.08, 0.07, 0.06), tail * smooth01(0.06, 0.12, sc) * 0.5)
base = paint(base, (0.012, 0.01, 0.009), tail * smooth01(0.04, 0.0, sc) * 0.8)
rough[tail] = 0.35
# The incisors: iron-dark, rust running down from their roots in streaks, the chipped edges and the cutting ends bare.
tooth = is_("tj_tooth")
base[tooth] = (0.3, 0.11, 0.025)
streak = smooth01(0.3, 0.8, field(lambda q: cook.noise_np(q * np.array([12.0, 12.0, 1.5], np.float32), 3805, 3.0)) * 0.5 + 0.5)
base = paint(base, (0.09, 0.035, 0.01), tooth * streak * 0.6)
tipz = np.min(TEETH[:, 2])
bare = tooth * np.maximum(smooth01(tipz + 0.04, tipz + 0.005, z), smooth01(0.95, 0.99, field(lambda q: ridged(q, 1341, 40.0))))
base = paint(base, (0.42, 0.3, 0.17), bare * 0.7)
rough[tooth] = 0.4
rough = np.where(bare > 0.5, 0.25, rough)
# The claws: old dark timber, split and paler at their ends.
claw = is_("tj_claw")
gw = field(grain)
base[claw] = (np.array((0.07, 0.05, 0.033), np.float32) * (0.75 + 0.45 * gw)[claw][:, None])
base = paint(base, (0.2, 0.15, 0.1), claw * smooth01(0.05, 0.015, z) * 0.6)
rough[claw] = 0.7
# The splinters: weathered grey-brown, the grain dark, pale and raw where they've snapped.
spl = is_("tj_splinter")
base[spl] = (np.array((0.14, 0.1, 0.065), np.float32) * (0.7 + 0.5 * gw)[spl][:, None])
base = paint(base, (0.36, 0.26, 0.16), spl * smooth01(0.4, 0.8, noise(3806, 9.0)) * 0.6)
base = paint(base, (0.04, 0.03, 0.02), spl * smooth01(0.5, 0.9, gw) * 0.5)
rough[spl] = 0.75
# The spines: black char, split by cracks that glow ember-red from deep inside, brightest at their roots.
spine = is_("tj_spine")
sk = field(cracks_of)
base[spine] = (np.array((0.014, 0.012, 0.011), np.float32) * (0.7 + 0.6 * gw)[spine][:, None])
root = np.zeros((S, S), np.float32)
for r in RIDGE:
    root = np.maximum(root, smooth01(0.35, 0.0, np.linalg.norm(P - r, axis=-1)))
ember = spine * sk * (0.2 + 0.8 * root)
base = paint(base, (0.5, 0.09, 0.015), ember)
emit += np.array((1.0, 0.3, 0.06), np.float32) * (ember * 0.4)[..., None]
rough[spine] = 0.55
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
ao = np.where(eye | nose | tooth, ao ** 0.4, ao)
base = base * (0.3 + 0.7 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.4
base = base * (1 - k)[..., None] + np.array((0.008, 0.006, 0.005), np.float32) * k[..., None]
emit = np.clip(emit * (0.4 + 0.6 * ao)[..., None], 0, 1)

bake_layers = cook.bake_layers


def with_glow(name, objs, **kw):
    """The atlas's material with its emission image (the embers' texels lit): cook.bake_layers reads it into the layer's
    emissive mask and draws those texels in the glow's colour (the Gannet's way)."""
    for m in bpy.data.materials:
        if m.name == "tower_jaw_final" and m.use_nodes:
            img = bpy.data.images.new("tower_jaw_emit", S, S, alpha=True)
            rgba = np.ones((S, S, 4), np.float32)
            rgba[..., :3] = emit
            img.pixels.foreach_set(rgba[::-1].ravel())
            tex = m.node_tree.nodes.new("ShaderNodeTexImage")
            tex.image = img
            bsdf = m.node_tree.nodes["Principled BSDF"]
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
    return bake_layers(name, objs, **kw)


cook.bake_layers = with_glow
atlas.finish(base, kit, arm, made=make.provenance("tower_jaw", "Tower Jaw, modelled in tools/blender/tower_jaw.py, its colour painted here"),
             rough=rough, lod=0.4)
