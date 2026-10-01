"""THE WHISTLER (GDD v1.2 §21 between the cars, App. A.4): the gap-dweller of tools/blender/whistler.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/whistler.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin soot-black and slick, grease from the couplers worked into every crease and shining on the knuckles of
    the spine, the elbows and knees; scraped grey where it's rubbed on the drawgear;
  * the face the one pale thing: a mask of grey-white skin stretched over the long skull, the sockets grown over and
    faintly dished, a few dark veins under the thin skin there, the brow heavy;
  * the mouth's tube creased in folds all round like a drawn purse, the lips raw and wet at the rim, the hole black;
  * the long fingers' and toes' tips worn grey; the coupling chain rusted, the skin swollen and dark where it's grown
    into it.
Wet: the grease shines in a lamp (rough 0.25); the face is dry (0.6), the mouth's rim and hole wet.

    tools/models/build.sh whistler
    WHISTLER_PREVIEW=1 tools/models/build.sh whistler   # renders the high copy to out/review/whistler-high-*.png
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

kit, g, arm, parts = overbake.hold("whistler.py")
print("[dt] whistler parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(g["HC"], np.float32)
HR = np.array(g["HR"], np.float32)
EYE_U, EYE_W, MOUTH_W = g["EYE_U"], g["EYE_W"], g["MOUTH_W"]
MOUTH = np.array(g["MOUTH_AT"], np.float32)
TUBE_END = np.array(g["TUBE_END"], np.float32)
WAIST = g["WAIST"]

DRESS = {
    "skin.whistler_face": (lambda: make.flat("whistler_face", (0.36, 0.35, 0.32), rough=0.6), 4),
    "skin.whistler": (lambda: make.flat("whistler_skin", (0.025, 0.022, 0.02), rough=0.3), 3),
    "tar.whistler_mouth": (lambda: make.flat("whistler_mouth", (0.006, 0.003, 0.003), rough=0.1), 1),
    "tar.nail": (lambda: make.flat("nail", (0.02, 0.018, 0.016), rough=0.4), 1),
    "rust_heavy.chain": (lambda: make.lib("rust_heavy", 8.0, (1.0, 1.0, 1.0), 0.8), 0),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"whistler: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def units(p):
    d = (p - HC) / HR
    return d[:, 0], d[:, 1], d[:, 2]


def pucker(p):
    """Along the mouth's tube: `on` 0..1 near it, `creases` -1..1 the folds running along it, `rim` 0..1 at its lips."""
    axis = TUBE_END - MOUTH
    L = np.linalg.norm(axis)
    a = axis / L
    rel = p - MOUTH
    t = rel @ a
    radial = rel - np.outer(t, a)
    r = np.linalg.norm(radial, axis=1)
    side = np.cross(a, np.array([0, 0, 1], np.float32))
    side /= np.linalg.norm(side)
    up = np.cross(side, a)
    ang = np.arctan2(radial @ up, radial @ side)
    on = smooth01(-0.03, 0.0, t) * smooth01(L + 0.02, L, t) * smooth01(0.05, 0.03, r)
    rim = bell((t - L) / 0.008) * smooth01(0.03, 0.015, r)
    creases = np.cos(ang * 16 + 6 * t / L) * on
    return on, creases, rim


def face_shape(p, n):
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v)
    on, creases, rim = pucker(p)
    d = 0.0016 * creases + 0.0012 * rim
    # The grown-over sockets: a faint ring of scar where the lids sealed, the skin puckered in towards it.
    for eu in (-EYE_U, EYE_U):
        r = np.hypot((u - eu) / 0.2, (w - EYE_W) / 0.12)
        d -= 0.0012 * bell((r - 0.5) / 0.08) * front
        d += 0.0006 * np.cos(np.arctan2(w - EYE_W, u - eu) * 10) * smooth01(1.2, 0.6, r) * smooth01(0.3, 0.6, r) * front
    return d + skin_shape(p, n) * 0.5


def skin_shape(p, n):
    d = 0.00025 * (ridged(p, 901, 150.0) ** 6 - 0.2)
    d += 0.0002 * (ridged(p * np.array([1.0, 1.0, 2.5], np.float32), 902, 60.0) ** 8 - 0.2)
    return d + fine(p, 0.0001, 500, 903)


def grease_shape(p, n):
    # Caked grease: lumps of it in the creases, smoothed where it's wiped; the chain's swelling.
    d = skin_shape(p, n) + 0.0012 * smooth01(0.5, 0.9, cook.noise_np(p, 904, 14.0))
    near_chain = bell((p[:, 2] - WAIST - 0.01) / 0.025) * (np.abs(p[:, 0]) < 0.12)
    return d + 0.004 * near_chain


SHAPE = {"skin.whistler_face": face_shape, "skin.whistler": grease_shape, "tar.whistler_mouth": lambda p, n: 0 * p[:, 0]}
BAKED = ["head", "body", "chain", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] whistler highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


def face_marks(p, kind):
    """R: veins under the face's thin skin; G: the mouth's rim, raw; B: the grown-over sockets' scar and their shadow."""
    out = np.zeros((len(p), 3), np.float32)
    if not kind.startswith("skin.whistler_face"):
        return out
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v)
    out[:, 0] = smooth01(0.86, 0.96, ridged(p, 911, 18.0)) * smooth01(-0.2, 0.4, cook.noise_np(p, 912, 4.0))
    on, creases, rim = pucker(p)
    out[:, 1] = np.clip(rim + 0.4 * np.maximum(0, -creases), 0, 1)
    sock = np.zeros(len(p), np.float32)
    for eu in (-EYE_U, EYE_U):
        r = np.hypot((u - eu) / 0.2, (w - EYE_W) / 0.12)
        sock = np.maximum(sock, smooth01(1.3, 0.4, r))
    out[:, 2] = np.clip(sock * front, 0, 1)
    return out


def grime_marks(p, kind):
    """R: grease sheen (the knuckles of the spine, elbows, knees, the shins it squats on); G: scraped grey where it's
    rubbed on iron; B: the chain's sore (the skin swollen dark-red round it)."""
    out = np.zeros((len(p), 3), np.float32)
    if not kind.startswith("skin.whistler") or kind.startswith("skin.whistler_face"):
        return out
    out[:, 0] = smooth01(0.4, 0.85, cook.noise_np(p, 921, 5.0))
    out[:, 1] = smooth01(0.8, 0.95, ridged(p, 922, 6.0)) * smooth01(0.0, 0.5, cook.noise_np(p, 923, 2.5))
    out[:, 2] = bell((p[:, 2] - WAIST - 0.01) / 0.04) * (np.abs(p[:, 0]) < 0.13)
    return out


if os.environ.get("WHISTLER_PREVIEW"):
    out_dir = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 560, 700
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.8, 0.2, 0.5)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for nm, o in parts.items():
        o.hide_render = nm in BAKED
    for view, d, c, dist in (("full", (0.4, 1, 0.1), (0, 0, 1.1), 4.4), ("face", (0.3, 1, 0.05), tuple(HC), 0.6)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"whistler-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] whistler preview -> out/review/whistler-high-*.png")
    sys.exit(0)

FACE = 1


def kind_of(m):
    return FACE if m.name.startswith("skin.whistler_face") or m.name.startswith("tar.whistler_mouth") else 0


atlas = overbake.Atlas("whistler", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 3.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.008, 0.02), "body": (0.01, 0.03), "chain": (0.006, 0.015), "limbs": (0.008, 0.02)},
           height=2.2, masks={"face": face_marks, "grime": grime_marks})

M = atlas.maps
face, grime = M["face"], M["grime"]
base = atlas.base(soot=(0.01, 0.009, 0.008), crease=0.6, ao_floor=0.35, gentle=None)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


base = paint(base, (0.12, 0.11, 0.1), grime[..., 1] * 0.5)       # scraped grey on the iron
base = paint(base, (0.06, 0.012, 0.01), grime[..., 2] * 0.7)     # the chain's sore
base = paint(base, (0.14, 0.12, 0.13), face[..., 0] * 0.45)      # veins under the face
base = paint(base, (0.18, 0.16, 0.15), face[..., 2] * 0.55)      # the sockets' shadow, grey
base = paint(base, (0.22, 0.07, 0.065), face[..., 1] * 0.7)      # the mouth's rim, raw

rough = np.full(base.shape[:2], 0.3, np.float32)
rough[atlas.masks["head"]] = 0.6
rough[atlas.masks["chain"]] = 0.8
rough = rough - 0.15 * grime[..., 0] - 0.35 * face[..., 1]
atlas.finish(base, kit, arm, made=make.provenance("whistler", "the Whistler, modelled over tools/blender/whistler.py"), rough=rough)
