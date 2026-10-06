"""TIPPY TOESIE (GDD v1.2 §21 interior, App. A.5): the starved thing of tools/blender/tippy_toesie.py, taken to the
fidelity target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/tippy_toesie.py stays its source: the rig, the clips and the game mesh. This recipe runs it, models a
high-resolution copy and bakes it into one 1024 atlas:
  * the skin grey-white as old plaster and thin as paper: fine creases all over it, the veins showing through blue-grey,
    the ribs', the collarbones' and the knuckles' edges standing and shining where the skin's drawn over them;
  * where its mouth should be, the skin grown over: a seam puckered in, drawn up in creases all round it like a
    purse's string pulled tight, darker in the pucker; the nostrils two slits;
  * the sockets bruised dark, the lids raw at the rims, the eyes black and wet;
  * the long fingers' knuckles creased and their tips grey with dirt; the points of its feet black from the boards;
  * the shift a child's, linen gone the grey-yellow of an old dressing, the hem torn and filthy, a stain down the front;
  * the few long hairs dark and lank.
The skin is dry (rough 0.6), the eyes and the pucker's wet (0.05, 0.3).

    tools/models/build.sh tippy_toesie
    TIPPY_PREVIEW=1 tools/models/build.sh tippy_toesie   # renders the high copy to out/review/tippy_toesie-high-*.png
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

kit, g, arm, parts = overbake.hold("tippy_toesie.py")
print("[dt] tippy_toesie parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(g["HC"], np.float32)
HR = np.array(g["HR"], np.float32)
EYES = {s: np.array(v, np.float32) for s, v in g["EYES"].items()}
EYE_U, EYE_W, EYE_R = g["EYE_U"], g["EYE_W"], g["EYE_R"]
WAIST, CHEST, TOP, HIP = g["WAIST"], g["CHEST"], g["TOP"], g["HIP"]

# Linear colours. The skin's a flat grey-white and all its colour is painted (the veins, the bruising, the dirt): the
# library's flesh is pink and blotched, a living thing's.
PLASTER = (0.36, 0.37, 0.36)
DRESS = {
    "skin.tippy_face": (lambda: make.flat("tippy_face", PLASTER, rough=0.6), 4),
    "skin.tippy": (lambda: make.flat("tippy_skin", PLASTER, rough=0.6), 3),
    "glass_dirty.tippy_eye": (lambda: make.flat("tippy_eye", (0.01, 0.009, 0.009), rough=0.05), 2),
    "tar.nail": (lambda: make.flat("nail", (0.03, 0.025, 0.022), rough=0.4), 1),
    # (Flat, the weave in its shape: the library's wool is knitted, and a nightshirt's linen.)
    "wool.shift": (lambda: make.flat("shift", (0.19, 0.18, 0.15), rough=0.9), 2),
    "wool.tippy_hair": (lambda: make.flat("tippy_hair", (0.02, 0.018, 0.016), rough=0.6), 1),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"tippy_toesie: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def units(p):
    """Positions in the head's own units: u across, v deep (front > 0), w up; each -1..1 over the egg."""
    d = (p - HC) / HR
    return d[:, 0], d[:, 1], d[:, 2]


# Where the mouth was: a seam across, under the nose.
MOUTH_W, MOUTH_U = -0.6, 0.26


def pucker(p):
    """The grown-over mouth: `seam` 0..1 on its line, `creases` -1..1 the drawn-up creases radiating from it, `near` how
    close (0..1), on the front of the face only."""
    u, v, w = units(p)
    front = smooth01(0.2, 0.5, v)
    across = np.clip(u / MOUTH_U, -1, 1)
    # The seam's line wanders a little and bows down at the corners, as a mouth's would.
    line = MOUTH_W - 0.05 * across ** 2 + 0.012 * np.sin(u * 60)
    seam = bell((w - line) / 0.022) * smooth01(1.1, 0.8, np.abs(u) / MOUTH_U) * front
    du, dw = u / (MOUTH_U * 1.2), (w - MOUTH_W) / 0.2
    r = np.hypot(du, dw)
    near = smooth01(1.3, 0.3, r) * front
    creases = np.cos(np.arctan2(dw, du) * 22 + 4 * r) * near
    return seam, creases, near


def face_shape(p, n):
    """The small forms over the game mesh's: the pucker and its creases, the nostrils' slits, the lids' rims round the
    eyes, the crow's-feet, and the skin's papery creasing."""
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v)
    seam, creases, near = pucker(p)
    d = -0.004 * seam + 0.0012 * creases * (1 - seam) - 0.002 * near
    for s in (-1, 1):
        d -= 0.004 * bell((u - s * 0.07) / 0.02) * bell((w + 0.34) / 0.012) * front
    for e in EYES.values():
        r = np.linalg.norm(p - e, axis=1)
        d += 0.0025 * bell((r - EYE_R * 1.25) / (EYE_R * 0.2))
        d -= 0.0008 * smooth01(EYE_R * 1.4, EYE_R * 2.5, r) * smooth01(EYE_R * 4.5, EYE_R * 2.5, r) * np.sin(np.arctan2(p[:, 2] - e[2], p[:, 0] - e[0]) * 14)
    return d + skin_shape(p, n)


def skin_shape(p, n):
    # Thin and dry: a fine crazing of creases all over, deeper at the joints; the veins standing a little under it.
    d = 0.00022 * (ridged(p, 601, 160.0) ** 6 - 0.2)
    d += 0.00018 * (ridged(p * np.array([1.0, 1.0, 2.5], np.float32), 602, 70.0) ** 8 - 0.2)
    # (Not on the head: over its smooth skull a vein's rise reads as a lump.)
    d += 0.0003 * veins_of(p) * (p[:, 2] < TOP - 0.05)
    return d + fine(p, 0.0001, 500, 603)


def veins_of(p):
    return smooth01(0.86, 0.96, ridged(p, 611, 9.0)) * smooth01(-0.2, 0.4, cook.noise_np(p, 612, 3.0))


def body_shape(p, n):
    # The ribs' edges sharper than the game mesh can make them, the belly's skin creased where it's sunk.
    z = p[:, 2]
    rib = np.where((z > WAIST + 0.04) & (z < TOP - 0.06), np.maximum(0, np.sin((z - WAIST) * 55)) ** 6, 0)
    belly = bell((z - WAIST) / 0.06) * np.maximum(0, p[:, 1]) / 0.1
    d = 0.0025 * rib - 0.0008 * belly * np.sin(z * 300)
    return d + skin_shape(p, n)


def linen(p, n):
    # Old linen: a fine weave, the drape's folds, crumpled; darned in places.
    d = 0.003 * (ridged(p * np.array([1.0, 1.0, 0.3], np.float32), 621, 14.0) ** 2 - 0.3)
    d += 0.0015 * cook.noise_np(p, 622, 30.0)
    # The weave: threads across and down, a millimetre apart.
    d += 0.00025 * (np.sin(p[:, 2] * 2200) + np.sin((p[:, 0] + p[:, 1]) * 2200))
    return d + fine(p, 0.0003, 420, 623)


def strands(p, n):
    return 0.002 * (ridged(p * np.array([1.0, 1.0, 0.12], np.float32), 631, 90.0) ** 3 - 0.3)


def skin_by_part(p, n):
    # The torso's own (its ribs); the arms, legs and hands the plain skin.
    return np.where((np.abs(p[:, 0]) < 0.17) & (p[:, 2] > HIP - 0.05) & (p[:, 2] < TOP + 0.02), body_shape(p, n), skin_shape(p, n))


SHAPE = {"skin.tippy_face": face_shape, "skin.tippy": skin_by_part, "wool.shift": linen, "wool.tippy_hair": strands}
BAKED = ["head", "body", "hands", "shift"]   # (the limbs grown into the body: rig.fuse)
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] tippy_toesie highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


# ----------------------------------------------------------------------------------------------------------------
# The grade, as masks on the high copy (three to a bake).

def face_marks(p, kind):
    """R: the bruising round and under the sockets; G: the pucker (its seam and creases, darker); B: the nostrils' and
    the lids' raw rims."""
    out = np.zeros((len(p), 3), np.float32)
    if not kind.startswith("skin.tippy_face") and not kind.startswith("skin.tippy"):
        return out
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v) * (np.linalg.norm((p - HC) / HR, axis=1) < 1.4)
    bruise = np.zeros(len(p), np.float32)
    rims = np.zeros(len(p), np.float32)
    for e in EYES.values():
        rel = p - e
        r = np.linalg.norm(rel, axis=1)
        # Heavier under the eye, dragged down the cheek.
        under = np.hypot(rel[:, 0], np.where(rel[:, 2] < 0, rel[:, 2] * 0.6, rel[:, 2] * 1.3))
        bruise = np.maximum(bruise, smooth01(EYE_R * 4.2, EYE_R * 1.3, under))
        rims = np.maximum(rims, bell((r - EYE_R * 1.2) / (EYE_R * 0.18)))
    seam, creases, near = pucker(p)
    out[:, 0] = np.clip(bruise * front * (0.75 + 0.25 * cook.noise_np(p, 701, 60.0)), 0, 1)
    out[:, 1] = np.clip(seam + 0.5 * np.maximum(0, -creases) + 0.25 * near, 0, 1)
    for s in (-1, 1):
        rims = np.maximum(rims, bell((u - s * 0.07) / 0.02) * bell((w + 0.34) / 0.012))
    out[:, 2] = np.clip(rims * front, 0, 1)
    return out


def skin_marks(p, kind):
    """R: veins, blue-grey through the skin; G: dirt (fingertips, the points of its feet, up its shins, the hem); B: the
    bone's sheen, where the skin's drawn tight over it (ribs, collarbones, knuckles, knees, shins)."""
    out = np.zeros((len(p), 3), np.float32)
    skin = kind.startswith("skin.tippy")
    if skin:
        out[:, 0] = veins_of(p)
        z = p[:, 2]
        rib = np.where((z > WAIST + 0.04) & (z < TOP - 0.06), np.maximum(0, np.sin((z - WAIST) * 55)) ** 4, 0) \
            * (np.abs(p[:, 0]) < 0.17)
        out[:, 2] = np.clip(rib + 0.6 * smooth01(0.6, 0.9, ridged(p, 711, 4.0)), 0, 1)
    ground = smooth01(0.35, 0.02, p[:, 2]) * (0.6 + 0.4 * cook.noise_np(p, 712, 9.0))
    tips = np.zeros(len(p), np.float32)
    if skin:
        # The fingertips, the last few centimetres of each finger (out past the palm, both hands).
        for side in ("l", "r"):
            h = np.array(g["T"](f"hand_{side}"), np.float32)
            r = np.linalg.norm(p - h, axis=1)
            tips = np.maximum(tips, smooth01(0.21, 0.27, r) * smooth01(0.4, 0.33, r))
    hem = kind.startswith("wool.shift") * smooth01(HIP - 0.08, HIP - 0.3, p[:, 2])
    out[:, 1] = np.clip(ground + 0.6 * tips + 0.7 * hem, 0, 1)
    return out


def shift_marks(p, kind):
    """R: a stain down its front, old and brown; G: the linen's yellowing, blotched; B: grime in its folds."""
    out = np.zeros((len(p), 3), np.float32)
    if not kind.startswith("wool.shift"):
        return out
    front = smooth01(0.0, 0.08, p[:, 1])
    run = bell((p[:, 0] + 0.03 + 0.02 * np.sin(p[:, 2] * 20)) / 0.05) * smooth01(CHEST + 0.03, CHEST - 0.1, p[:, 2]) \
        * smooth01(HIP - 0.25, WAIST, p[:, 2])
    out[:, 0] = np.clip(run * front * (0.6 + 0.4 * cook.noise_np(p, 721, 15.0)), 0, 1)
    out[:, 1] = smooth01(0.0, 0.9, cook.noise_np(p, 722, 9.0))
    out[:, 2] = smooth01(0.8, 0.95, ridged(p, 723, 7.0))
    return out


def glass_and_blotch(p, kind):
    """R: the eyes' glass; G: the skin's blotching, uneven as old plaster is; B: nothing."""
    out = np.zeros((len(p), 3), np.float32)
    out[:, 0] = kind.startswith("glass_dirty.tippy_eye")
    if kind.startswith("skin.tippy"):
        out[:, 1] = np.clip(0.5 + 0.35 * cook.noise_np(p, 801, 6.0) + 0.15 * cook.noise_np(p, 802, 25.0), 0, 1)
    return out


if os.environ.get("TIPPY_PREVIEW"):
    out_dir = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 560, 760
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.7
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.8, 0.2, 0.5)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    for n, o in parts.items():
        o.hide_render = n in BAKED
    for view, d, c, dist in (("full", (0.4, 1, 0.1), (0, 0, 1.1), 4.2), ("face", (0.15, 1, 0.05), tuple(HC), 0.6),
                             ("chest", (0.2, 1, 0.1), (0, 0, CHEST), 0.9)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"tippy_toesie-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] tippy_toesie preview -> out/review/tippy_toesie-high-*.png")
    sys.exit(0)

FACE, EYE = 1, 2


def kind_of(m):
    return FACE if m.name.startswith("skin.tippy_face") else EYE if m.name.startswith("glass_dirty.tippy_eye") else 0


atlas = overbake.Atlas("tippy_toesie", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 3.0, EYE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.008, 0.02), "body": (0.01, 0.03), "hands": (0.004, 0.012), "shift": (0.015, 0.03)},
           height=2.2, masks={"face": face_marks, "skin": skin_marks, "shift": shift_marks, "glass": glass_and_blotch})

M = atlas.maps
face, skin, cloth, glass = M["face"], M["skin"], M["shift"], M["glass"]
# (The shift gently: the body inside it darkens it in blotches where it touches.)
base = atlas.base(soot=(0.02, 0.018, 0.018), crease=0.55, ao_floor=0.4, gentle=atlas.masks["shift"])


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


flesh = (atlas.masks["head"] | atlas.masks["body"] | atlas.masks["hands"])
base = np.where(flesh[..., None], base * (0.86 + 0.28 * glass[..., 1:2]), base)   # uneven, as plaster is
base = paint(base, (0.17, 0.18, 0.21), skin[..., 0] * 0.55)       # veins, blue-grey through the skin
base = paint(base, (0.5, 0.49, 0.46), skin[..., 2] * 0.25)        # drawn tight over the bone
base = paint(base, (0.1, 0.075, 0.09), face[..., 0] * 0.8)        # bruised sockets
base = paint(base, (0.19, 0.11, 0.1), face[..., 2] * 0.7)         # raw rims
base = paint(base, (0.13, 0.1, 0.095), face[..., 1] * 0.75)       # the pucker
base = paint(base, (0.2, 0.17, 0.1), cloth[..., 1] * 0.3)         # linen gone yellow
base = paint(base, (0.07, 0.04, 0.025), cloth[..., 0] * 0.7)      # the stain
base = paint(base, (0.05, 0.045, 0.04), cloth[..., 2] * 0.5)      # grime in the folds
base = paint(base, (0.035, 0.03, 0.028), skin[..., 1] * 0.85)     # dirt

# Dry skin, the bone's drawn skin a little glossier, wet eyes and a wet pucker; the linen and the hair dull.
rough = np.full(base.shape[:2], 0.9, np.float32)
rough[flesh] = 0.62
rough = np.where(flesh, rough - 0.2 * skin[..., 2] - 0.3 * face[..., 1] - 0.2 * face[..., 2], rough)
rough = np.where(glass[..., 0] > 0.5, 0.05, rough)
atlas.finish(base, kit, arm, made=make.provenance("tippy_toesie", "Tippy Toesie, modelled over tools/blender/tippy_toesie.py"),
             rough=rough, lod=0.4)
