"""THE GANNET (GDD §21 flank; docs/design/creatures/gannet.md §3; ARCHITECTURE §8 note 340): tools/blender/gannet.py's
corrupted seabird with its colour baked into one atlas (the Look Review: organic, not tiled library textures on boxes).

tools/blender/gannet.py stays its source: the rig, the fused skin and the feathers, the clips. This recipe runs it,
unwraps every part into one 2048 atlas (the head and the spear given more of it), and paints the atlas from where each
texel is on the bird at rest, by what it's part of (deterministic, no render: each texel's position, normal and
material are rasterized from the unwrap):
  * the plumage off-white, mottled densely with sooty brown-black flecks, thickest on the back, the coverts and the
    scapulars, light on the belly; the trailing edge's feathers dusky at their tips; the primaries black;
  * the head and the nape sulphur-yellow, fading into the white down the neck; black skin from the eye to the gape;
  * the skull's plates ochre bone, cracked and sooted; the spear grey-white bone stained brown toward its hook, the
    teeth yellowed, browner at the roots;
  * the sacs pale and translucent-looking, veined dark red, glowing orange from within (an emission map: the engine
    draws those texels lit, scaled by the instance's glow, so they throb);
  * the legs and toes black and scaled, the webs leathery, the talons glossy black.

    python tools/models/recipes/gannet.py        (Blender as a Python module: pip install "bpy<5")
    tools/models/build.sh gannet                 (or with Blender)
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import smooth01  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("gannet.py")
print("[dt] gannet parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()

# (In the scene's own order: the join keeps the faces in it, and the atlas lines its faces' material names up by it.)
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
FACE = 1
# The eye's glass and its pupil keep their own materials (lit glass, a breath of emissive: SceneArt's eyes).
KEPT = ("glass_dirty.gannet_eye", "tar.gannet_pupil")


def kind_of(m):
    if m.name.startswith(KEPT):
        return overbake.Atlas.KEEP
    return FACE if m.name.split(".")[-1].startswith(("gannet_head", "gannet_face", "gannet_beak", "gannet_plate", "gannet_tooth")) else 0


atlas = overbake.Atlas("gannet", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 2.2})

# --- where each texel is ----------------------------------------------------------------------------------------
low = atlas.low
me = low.data
me.calc_loop_triangles()
nv, nl = len(me.vertices), len(me.loops)
co = np.empty(nv * 3, np.float32)
me.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
no = np.empty(nv * 3, np.float32)
me.vertices.foreach_get("normal", no)
no = no.reshape(-1, 3)
uv = np.empty(nl * 2, np.float32)
me.uv_layers["UVMap"].data.foreach_get("uv", uv)
uv = uv.reshape(-1, 2)
lv = np.empty(nl, np.int32)
me.loops.foreach_get("vertex_index", lv)
nt = len(me.loop_triangles)
tl = np.empty(nt * 3, np.int32)
me.loop_triangles.foreach_get("loops", tl)
tl = tl.reshape(-1, 3)
tp = np.empty(nt, np.int32)
me.loop_triangles.foreach_get("polygon_index", tp)

KINDS = sorted(set(atlas.slot_name))
kind_index = {k: i for i, k in enumerate(KINDS)}
face_kind = np.array([kind_index[n] for n in atlas.slot_name], np.int32)

P = np.zeros((S, S, 3), np.float32)
N = np.zeros((S, S, 3), np.float32)
K = np.full((S, S), -1, np.int32)
for t in range(nt):
    loops = tl[t]
    if atlas.keep[tp[t]]:
        continue
    q = uv[loops] * S
    x, y = q[:, 0] - 0.5, (1 - uv[loops][:, 1]) * S - 0.5
    x0, x1 = int(max(0, np.floor(x.min()))), int(min(S - 1, np.ceil(x.max())))
    y0, y1 = int(max(0, np.floor(y.min()))), int(min(S - 1, np.ceil(y.max())))
    if x1 < x0 or y1 < y0:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1, dtype=np.float32), np.arange(y0, y1 + 1, dtype=np.float32))
    d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
    if abs(d) < 1e-12:
        continue
    a = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
    b = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
    c = 1 - a - b
    inside = (a >= -0.02) & (b >= -0.02) & (c >= -0.02)
    if not inside.any():
        continue
    vi = lv[loops]
    w = np.stack([a, b, c], -1)[inside]
    rows, cols = gy[inside].astype(np.int32), gx[inside].astype(np.int32)
    P[rows, cols] = w @ co[vi]
    N[rows, cols] = w @ no[vi]
    K[rows, cols] = face_kind[tp[t]]
# Grown out a few texels past every island's edge, so filtering never reads the empty atlas between them.
for _ in range(4):
    empty = K < 0
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        src = np.roll(np.roll(K, dy, 0), dx, 1)
        take = empty & (src >= 0)
        K[take] = src[take]
        P[take] = np.roll(np.roll(P, dy, 0), dx, 1)[take]
        N[take] = np.roll(np.roll(N, dy, 0), dx, 1)[take]
        empty = K < 0
N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-6)
print("[dt] gannet atlas", f"{(K >= 0).mean():.0%} covered by", {k: int((K == i).sum()) for i, k in enumerate(KINDS)})

# --- what colour it is ----------------------------------------------------------------------------------------
flat = P.reshape(-1, 3)


def noise(seed, scale):
    return cook.noise_np(flat, seed, scale).reshape(S, S)


def is_(*names):
    m = np.zeros((S, S), bool)
    for i, k in enumerate(KINDS):
        if k.split(".")[-1].startswith(names):
            m |= K == i
    return m


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


up = N[..., 2]
white = np.array((0.7, 0.68, 0.63), np.float32)
base = np.broadcast_to(white, (S, S, 3)).copy()
rough = np.full((S, S), 0.85, np.float32)
emit = np.zeros((S, S, 3), np.float32)

# Plumage: off-white; a sooty wash over the back and the wings' tops, and flecks, dense where the smoke settles.
plume = is_("gannet_white", "gannet_back", "gannet_belly", "gannet_covert", "gannet_flight")
grain = 0.04 * noise(3401, 90.0)
base[plume] = (white + grain[plume][:, None])
wash = smooth01(0.0, 0.8, up) * (0.12 + 0.12 * noise(3402, 1.5))
base = paint(base, (0.48, 0.46, 0.42), wash * plume * (is_("gannet_back", "gannet_covert") * 1.0 + 0.4))
fleck = 0.6 * noise(3403, 26.0) + 0.4 * noise(3404, 61.0)
density = (0.2 * is_("gannet_white") + 0.08 * is_("gannet_belly") + 0.42 * is_("gannet_back") + 0.5 * is_("gannet_covert")
           + 0.3 * is_("gannet_flight")) * (0.7 + 0.6 * smooth01(-0.2, 0.6, up))
# (The fleck field sits round 0.5: a threshold of 0.52 flecks about half of it, the coverts' and the back's tops; 0.64 a
# tenth, the belly's.)
flecks = smooth01(0.64 - 0.2 * density, 0.69 - 0.2 * density, fleck * 0.5 + 0.5)
base = paint(base, (0.04, 0.032, 0.025), flecks * plume * 0.9)
# The trailing edge's tips dusky, the primaries black (a brown sheen in them), black facial skin.
dusky = is_("gannet_dusky")
base[dusky] = (np.array((0.05, 0.043, 0.038), np.float32) + 0.02 * noise(3405, 40.0)[dusky][:, None])
prim = is_("gannet_primary")
base[prim] = (np.array((0.016, 0.014, 0.013), np.float32) + 0.012 * (0.5 + 0.5 * noise(3406, 20.0))[prim][:, None])
rough[prim] = 0.65
face = is_("gannet_face")
base[face] = (0.012, 0.011, 0.01)
rough[face] = 0.45
# The head: sulphur, fading into the white down the nape, a few sooty flecks.
head = is_("gannet_head")
yellow = np.array((0.66, 0.4, 0.06), np.float32)
fade = smooth01(1.86, 2.02, P[..., 2] + 0.04 * noise(3407, 8.0))
base = paint(base, yellow, head * np.maximum(fade, 0.65))
base = paint(base, (0.05, 0.04, 0.02), head * flecks * 0.4)
# The plates: ochre bone, cracked, the cracks sooted.
plate = is_("gannet_plate")
crack = smooth01(0.9, 0.97, 1 - np.abs(noise(3408, 18.0)))
base[plate] = (np.array((0.42, 0.31, 0.15), np.float32) * (0.85 + 0.3 * noise(3409, 6.0))[plate][:, None])
base = paint(base, (0.05, 0.035, 0.02), plate * crack * 0.8)
rough[plate] = 0.5
# The spear: grey-white bone stained brown toward the hook and along its lower edge; the teeth yellowed, browner at the
# roots; the mouth dark red.
bone = is_("gannet_beak")
tip = smooth01(1.7, 2.3, P[..., 1])
base[bone] = (np.array((0.55, 0.52, 0.45), np.float32) * (0.9 + 0.2 * noise(3410, 5.0))[bone][:, None])
stain = smooth01(0.1, 0.6, noise(3411, 3.0) * 0.5 + 0.5 + 0.4 * tip)
base = paint(base, (0.16, 0.11, 0.07), bone * stain * 0.75)
base = paint(base, (0.08, 0.06, 0.05), bone * crack * 0.5)
rough[bone] = 0.45
tooth = is_("gannet_tooth")
base[tooth] = (0.6, 0.53, 0.38)
rough[tooth] = 0.4
mouth = is_("gannet_mouth")
base[mouth] = (0.16, 0.03, 0.025)
rough[mouth] = 0.3
# The sacs: pale and translucent-looking, veined dark red, glowing orange through the skin between the veins.
sac = is_("gannet_sac")
vein = smooth01(0.88, 0.96, 1 - np.abs(noise(3412, 9.0))) * 0.8 + smooth01(0.92, 0.98, 1 - np.abs(noise(3413, 22.0))) * 0.5
base[sac] = (np.array((0.78, 0.55, 0.4), np.float32) * (0.92 + 0.15 * noise(3414, 4.0))[sac][:, None])
base = paint(base, (0.22, 0.04, 0.035), sac * np.clip(vein, 0, 1))
glow = sac * (1 - np.clip(vein, 0, 1)) * (0.2 + 0.08 * noise(3415, 3.0))
emit = np.array((1.0, 0.5, 0.16), np.float32) * glow[..., None]
rough[sac] = 0.15
# Legs and toes black and scaled (each scale's rim catching the light); the webs leathery; the talons glossy.
leg = is_("gannet_leg")
scale = smooth01(0.8, 0.95, 1 - np.abs(noise(3416, 34.0)))
base[leg] = (np.array((0.02, 0.019, 0.018), np.float32) + 0.05 * scale[leg][:, None])
rough[leg] = 0.4
web = is_("gannet_web")
base[web] = (np.array((0.026, 0.023, 0.021), np.float32) * (0.8 + 0.4 * noise(3417, 12.0))[web][:, None])
talon = is_("gannet_talon")
base[talon] = (0.008, 0.008, 0.008)
rough[talon] = 0.25
base = np.clip(base, 0, 1)

# No normals baked (the shapes are the mesh's own): a flat normal map; the sacs' glow goes in as the material's emission.
atlas.maps = {"NORMAL": np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), (S, S, 1))}
bake_layers = cook.bake_layers


def with_glow(name, objs, **kw):
    """The atlas's material with its emission image (the sacs' texels lit): cook.bake_layers reads it into the layer's
    emissive mask and draws those texels in the glow's colour."""
    for m in bpy.data.materials:
        if m.name == "gannet_final" and m.use_nodes:
            img = bpy.data.images.new("gannet_emit", S, S, alpha=True)
            rgba = np.ones((S, S, 4), np.float32)
            rgba[..., :3] = emit
            img.pixels.foreach_set(rgba[::-1].ravel())
            tex = m.node_tree.nodes.new("ShaderNodeTexImage")
            tex.image = img
            bsdf = m.node_tree.nodes["Principled BSDF"]
            m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Emission Color"])
    return bake_layers(name, objs, **kw)


cook.bake_layers = with_glow
atlas.finish(base, kit, arm, made=make.provenance("gannet", "the Gannet, modelled in tools/blender/gannet.py, its colour painted here"),
             rough=rough, lod=0.4)
