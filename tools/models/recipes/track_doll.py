"""THE TRACK DOLL (GDD v1.2 §21 forward, App. A.2): the bisque doll of tools/blender/track_doll.py, taken to the fidelity
target (ARCHITECTURE §8 note 58, tools/models overbake).

tools/blender/track_doll.py stays its source: the jointed rig, the clips and the game mesh. This recipe runs it, models
a high-resolution copy and bakes it into one 1024 atlas:
  * the porcelain glazed and old: smooth, crazed all over with a fine network the grime has got into, a crack from
    the hairline down through the left eye to the cheek, a chip out of the right temple showing the hollow inside;
  * the face moulded and painted as the old ones were: lids over the glass eyes, a button nose, a small closed
    rosebud mouth; feathered brows, painted lashes, rouge on the cheeks and the lips, and soot run down from the eyes;
  * glass eyes: a grey-blue iris ringed dark, a black pupil, the left one turned a little down and out;
  * the moulded fingers parted by grooves; the wig's hair in strands; the dress in linen gone grey, the lace
    collar and frill, a satin sash faded to grey-blue; the boots' leather creased at the ankle;
  * dirt rising from the hem and the boots: it has stood out on the line.
The porcelain's gloss is kept in the atlas (rough 0.2; the chip's unglazed bisque and the cloth matte), and the face
and eyes draw under a material of their own (track_doll_0.face) that the game gives a faint light of its own: GDD §21's
"white face shines in the lamp out to 200m" through the fog.

    tools/models/build.sh track_doll
    TRACK_DOLL_PREVIEW=1 tools/models/build.sh track_doll   # renders the high copy to out/review/track_doll-high-*.png
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

kit, g, arm, parts = overbake.hold("track_doll.py")
print("[dt] track_doll parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

HC = np.array(g["HC"], np.float32)
HR = np.array(g["HR"], np.float32)
EYES = {s: np.array(v, np.float32) for s, v in g["EYES"].items()}
GAZE = {s: np.array(v, np.float32) for s, v in g["GAZE"].items()}
EYE_R, EYE_U, EYE_W = g["EYE_R"], g["EYE_U"], g["EYE_W"]
CHIP = g["CHIP"]

# Linear colours.
IVORY = (0.8, 0.76, 0.67)
DRESS = {
    "skin.porcelain_face": (lambda: make.flat("porcelain_face", IVORY, rough=0.2), 4),
    "skin.porcelain": (lambda: make.flat("porcelain", IVORY, rough=0.2), 2),
    "glass_dirty.eye": (lambda: make.flat("eye_white", (0.72, 0.71, 0.66), rough=0.1), 3),
    # (The library's wool is a dark blue-grey, a quarter bright: the tints bring it up to linen and lace, down to hair.)
    "wool.dress": (lambda: make.lib("wool", 12.0, (6.2, 5.8, 4.3), 0.9), 2),
    "wool.lace": (lambda: make.lib("wool", 9.0, (7.4, 7.0, 5.7), 0.9), 2),
    "wool.hair": (lambda: make.lib("wool", 3.0, (0.95, 0.62, 0.42), 0.7), 1),
    "wool.ribbon": (lambda: make.lib("wool", 7.0, (2.9, 3.5, 4.0), 0.5), 1),
    "leather.boot": (lambda: make.lib("leather", 4.0, (0.3, 0.27, 0.25), 0.35), 2),
}


def dress(m):
    key = next((k for k in sorted(DRESS, key=len, reverse=True) if m.name.startswith(k)), None)
    if key is None:
        raise KeyError(f"track_doll: no dress for {m.name}")
    fn, subdiv = DRESS[key]
    return fn(), subdiv


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def units(p):
    """Positions in the head's own units: u across, v deep (front > 0), w up; each -1..1 over the egg."""
    d = (p - HC) / HR
    return d[:, 0], d[:, 1], d[:, 2]


def seg_dist(px, pz, a, b):
    """Distance (metres, in the face's plane: across and up) from points to the segment a-b given in (u, w)."""
    ax, az = a[0] * HR[0], a[1] * HR[2]
    bx, bz = b[0] * HR[0], b[1] * HR[2]
    dx, dz = bx - ax, bz - az
    t = np.clip(((px - ax) * dx + (pz - az) * dz) / (dx * dx + dz * dz), 0, 1)
    return np.hypot(px - ax - t * dx, pz - az - t * dz)


# The crack: from under the hairline down through the left eye to the cheek, with a branch off it across the brow.
CRACK = [((-0.1, 0.98), (-0.2, 0.62)), ((-0.2, 0.62), (-0.29, 0.3)), ((-0.29, 0.3), (-0.36, 0.08)),
         ((-0.36, 0.08), (-0.43, -0.16)), ((-0.43, -0.16), (-0.47, -0.4)), ((-0.47, -0.4), (-0.42, -0.6)),
         ((-0.29, 0.3), (-0.14, 0.2)), ((-0.47, -0.4), (-0.58, -0.47))]


def crack(p):
    """0..1 on the crack's line (a wobbling hairline, a millimetre and a half), on the front of the head only."""
    u, v, w = units(p)
    px, pz = (p[:, 0] - HC[0]) + 0.0012 * cook.noise_np(p, 301, 90.0), (p[:, 2] - HC[2])
    d = np.min([seg_dist(px, pz, a, b) for a, b in CRACK], axis=0)
    return bell(d / 0.0011) * smooth01(-0.1, 0.2, v)


def chip(p):
    """The chip out of the right temple: 1 inside the break (the hollow's dark), its edge jagged; and its rim."""
    u, v, w = units(p)
    r = np.hypot((u - CHIP[0]) / 0.13, (w - CHIP[1]) / 0.12) + 0.25 * cook.noise_np(p, 311, 60.0)
    front = smooth01(-0.3, 0.1, v)
    return smooth01(1.0, 0.85, r) * front, bell((r - 1.0) / 0.12) * front


def crazing(p):
    """The glaze's fine network of cracks: the edges of a 3D cell pattern (about 2 cm cells), 0..1 on a line."""
    q = p * 55.0
    base = np.floor(q)
    best = np.full(len(p), 9.0, np.float32)
    second = np.full(len(p), 9.0, np.float32)
    for dx in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dz in (-1, 0, 1):
                c = base + np.array([dx, dy, dz], np.float32)
                h = np.sin(c @ np.array([12.9898, 78.233, 37.719], np.float32)) * 43758.5453
                jitter = np.stack([h - np.floor(h), (h * 1.37) - np.floor(h * 1.37), (h * 1.91) - np.floor(h * 1.91)], 1)
                d = np.linalg.norm(q - (c + jitter), axis=1)
                second = np.where(d < best, best, np.minimum(second, d))
                best = np.minimum(best, d)
    return bell((second - best) / 0.06)


def lips_and_mouth(p):
    u, v, w = units(p)
    front = smooth01(0.2, 0.5, v)
    wm = -0.5
    upper = (w > wm) & (w < wm + 0.055 - 0.028 * (u / 0.15) ** 2 - 0.015 * bell(u / 0.03)) & (np.abs(u) < 0.15)
    lower = (w <= wm) & (w > wm - 0.05 * (1 - (u / 0.12) ** 2)) & (np.abs(u) < 0.12)
    lips = (upper | lower).astype(np.float32) * front
    line = bell((w - wm) / 0.01) * smooth01(0.13, 0.08, np.abs(u)) * front
    return lips, line


def face_shape(p, n):
    """The moulded face over the egg: the upper lids' edge over the glass, the nose's tip and nostrils, the philtrum,
    the lips; the crack's groove, the chip, the crazing; all small (the game mesh has the big forms)."""
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v)
    d = np.zeros(len(p), np.float32)
    for side, eu in (("l", -EYE_U), ("r", EYE_U)):
        e = EYES[side]
        r = np.linalg.norm(p - e, axis=1)
        # The lid: a rim standing a few millimetres proud round the top of the eye's opening.
        up = smooth01(-0.3, 0.3, (p[:, 2] - e[2]) / EYE_R)
        d += 0.004 * bell((r - EYE_R * 1.06) / (EYE_R * 0.12)) * up
    d += 0.004 * bell(u / 0.06) * bell((w + 0.3) / 0.05) * front                  # the nose's tip
    for s in (-1, 1):
        d -= 0.003 * bell((u - s * 0.05) / 0.02) * bell((w + 0.335) / 0.02) * front  # nostrils
    d -= 0.0015 * bell(u / 0.03) * bell((w + 0.42) / 0.05) * front                 # philtrum
    lips, line = lips_and_mouth(p)
    d += 0.0025 * lips - 0.002 * line
    cr = crack(p)
    inside, rim = chip(p)
    d -= 0.0012 * cr + 0.007 * inside - 0.0008 * rim
    d -= 0.00012 * crazing(p)
    return d


def porcelain_shape(p, n):
    inside, rim = chip(p)
    d = -0.007 * inside + 0.0008 * rim - 0.00012 * crazing(p)
    # The fingers, moulded as one piece: three grooves part them, along the hand (x) out past the knuckles.
    x = np.abs(p[:, 0])
    on = smooth01(0.87, 0.9, x) * smooth01(1.0, 0.97, x) * bell((p[:, 2] - g["SHOULDER"].z) / 0.04)
    for k in (-1, 0, 1):
        d -= 0.004 * on * bell((p[:, 1] - k * 0.021) / 0.0035)
    # The wrist's seam, where the hand is set into the forearm.
    wx = g["H"]("hand_r").x
    d -= 0.002 * bell((x - wx - 0.005) / 0.003) * bell((p[:, 2] - g["SHOULDER"].z) / 0.05)
    return d


def linen(p, n):
    # Linen: a fine weave, the skirt's folds hanging, a crumple where it's been folded up in a crate for years.
    d = 0.004 * (ridged(p * np.array([1.0, 1.0, 0.3], np.float32), 401, 16.0) ** 2 - 0.3)
    d += 0.002 * cook.noise_np(p, 402, 30.0)
    return d + fine(p, 0.0004, 400, 403)


def lace(p, n):
    # Lace: a small raised pattern of rings, and the holes between.
    k = np.sin(p[:, 0] * 260) * np.sin(p[:, 1] * 260) * np.sin(p[:, 2] * 260)
    return 0.0012 * np.clip(k * 3, -1, 1) + fine(p, 0.0003, 500, 411)


def strands(p, n):
    # Hair in strands, running down (the ringlets' spiral is in the game mesh), the wig's fibres coarse and matted.
    d = 0.003 * (ridged(p * np.array([1.0, 1.0, 0.12], np.float32), 421, 90.0) ** 3 - 0.3)
    return d + 0.002 * cook.noise_np(p, 422, 25.0)


def satin(p, n):
    return 0.0015 * (ridged(p, 431, 22.0) ** 4 - 0.2)


def boot(p, n):
    # Creases round the ankle, the leather cracked across the toe.
    ankle = bell((p[:, 2] - 0.14) / 0.04)
    return 0.0015 * ankle * np.sin(p[:, 2] * 400) + 0.0008 * (ridged(p, 441, 60.0) ** 3 - 0.3)


SHAPE = {"skin.porcelain_face": face_shape, "skin.porcelain": porcelain_shape, "wool.dress": linen, "wool.lace": lace,
         "wool.hair": strands, "wool.ribbon": satin, "leather.boot": boot}
BAKED = ["head", "hair", "dress", "limbs"]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] track_doll highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})


# ----------------------------------------------------------------------------------------------------------------
# The paint, as masks on the high copy (three to a bake), and the wear.

def face_paint(p, kind):
    """R: the dark paint (brows, lashes, the lids' line, the mouth's line, the nostrils); G: the lips' rouge; B: the
    cheeks'. Only on the face."""
    out = np.zeros((len(p), 3), np.float32)
    if not kind.startswith("skin.porcelain_face"):
        return out
    u, v, w = units(p)
    front = smooth01(0.1, 0.4, v)
    dark = np.zeros(len(p), np.float32)
    for side, s in (("l", -1), ("r", 1)):
        e = EYES[side]
        au = np.abs(u) * np.sign(u) * s  # across, out from this side's eye
        # The brows: thin and high, feathered at the ends.
        wb = 0.26 + 0.06 * (1 - ((au - 0.37) / 0.21) ** 2)
        dark += 0.9 * bell((w - wb) / 0.02) * smooth01(0.12, 0.2, au) * smooth01(0.62, 0.52, au)
        # Round the eye: the upper lid's heavy painted line; short lashes along the top of the lid only, swept out
        # towards the outer corner, and fewer, finer ones under the eye.
        rel = p - e
        r = np.linalg.norm(rel, axis=1)
        ang = np.degrees(np.arctan2(rel[:, 2], rel[:, 0] * s)) % 360  # 0 out from the nose, 90 up
        line = bell((r - EYE_R * 1.09) / 0.005) * ((ang > 8) & (ang < 172))
        sweep = ang - 60 * (r / EYE_R - 1.08)
        strokes = np.clip(np.cos(np.radians(sweep) * 26) * 2.4 - 1.0, 0, 1)
        lash_up = strokes * smooth01(EYE_R * 1.08, EYE_R * 1.13, r) * smooth01(EYE_R * 1.36, EYE_R * 1.22, r) \
            * ((ang > 25) & (ang < 150)) * (0.6 + 0.4 * smooth01(150, 40, ang))
        lash_lo = np.clip(np.cos(np.radians(ang) * 16) * 2.4 - 1.5, 0, 1) * smooth01(EYE_R * 1.06, EYE_R * 1.1, r) \
            * smooth01(EYE_R * 1.26, EYE_R * 1.15, r) * ((ang > 205) & (ang < 330))
        dark += (line + lash_up + 0.6 * lash_lo) * front
    lips, mouth = lips_and_mouth(p)
    dark += 0.9 * mouth
    for s in (-1, 1):
        dark += 0.6 * bell((u - s * 0.05) / 0.018) * bell((w + 0.335) / 0.016) * front
    out[:, 0] = np.clip(dark, 0, 1)
    out[:, 1] = lips * 0.9
    out[:, 2] = np.clip(bell(np.hypot(np.abs(u) - 0.5, w + 0.28) / 0.2) * front, 0, 1)
    return out


def eyes_and_tears(p, kind):
    """R: the iris; G: the pupil and the iris's dark ring; B: the soot run down from the eyes."""
    out = np.zeros((len(p), 3), np.float32)
    if kind.startswith("glass_dirty.eye"):
        for side in ("l", "r"):
            rel = p - EYES[side]
            r = np.linalg.norm(rel, axis=1)
            near = r < EYE_R * 1.3
            c = (rel @ GAZE[side]) / np.maximum(r, 1e-6)
            a = np.arccos(np.clip(c, -1, 1))
            iris = smooth01(0.66, 0.6, a) * near
            ring = bell((a - 0.6) / 0.07) * near
            pupil = smooth01(0.34, 0.3, a) * near
            # The iris's glass: streaks radiating from the pupil, lighter and darker.
            streak = 0.5 + 0.5 * np.sin(np.arctan2(rel[:, 2], rel[:, 0]) * 17 + 3 * cook.noise_np(p, 501, 200.0))
            out[:, 0] = np.maximum(out[:, 0], iris * (0.75 + 0.25 * streak))
            out[:, 1] = np.maximum(out[:, 1], np.clip(ring * 0.8 + pupil, 0, 1))
        return out
    if kind.startswith("skin.porcelain_face"):
        u, v, w = units(p)
        front = smooth01(0.1, 0.4, v)
        tears = np.zeros(len(p), np.float32)
        # From each lower lid down the cheek, wandering; heavier from the cracked eye, running on past the jaw.
        for eu, k, end in ((-EYE_U, 1.0, -0.95), (EYE_U, 0.6, -0.7)):
            for off, width in ((0.0, 0.035), (0.09 * np.sign(eu), 0.02)):
                x = u - eu - off - 0.04 * np.sin(w * 7 + eu * 5) - 0.02 * cook.noise_np(p, 511, 20.0)
                along = smooth01(EYE_W - 0.1, EYE_W - 0.2, w) * smooth01(end, end + 0.25, w)
                tears += k * bell(x / width) * along * (0.6 + 0.4 * cook.noise_np(p, 512, 40.0))
        out[:, 2] = np.clip(tears * front, 0, 1)
    return out


def wear(p, kind):
    """R: the crack's line and the chip's hollow; G: dirt up from the ground; B: grime in the glaze's crazing."""
    out = np.zeros((len(p), 3), np.float32)
    porcelain = kind.startswith("skin.porcelain") or kind.startswith("glass_dirty.eye")
    if porcelain:
        inside, rim = chip(p)
        out[:, 0] = np.clip(crack(p) + inside, 0, 1)
        # Only here and there: where the glaze has taken knocks, and never much.
        out[:, 2] = crazing(p) * smooth01(0.1, 0.7, cook.noise_np(p, 521, 5.0))
    out[:, 1] = smooth01(0.75, 0.04, p[:, 2]) * (0.55 + 0.45 * cook.noise_np(p, 522, 8.0))
    return out


if os.environ.get("TRACK_DOLL_PREVIEW"):
    out_dir = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out_dir, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 640, 760
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
    for view, d, c, dist in (("full", (0.5, 1, 0.2), (0, 0, 1.1), 4.6), ("face", (0.25, 1, 0.08), tuple(HC), 0.9),
                             ("hand", (0.3, 0.6, 0.9), (0.85, 0, 1.5), 0.6)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out_dir, f"track_doll-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] track_doll preview -> out/review/track_doll-high-*.png")
    sys.exit(0)

FACE, EYE = 1, 2


def kind_of(m):
    return FACE if m.name.startswith("skin.porcelain_face") else EYE if m.name.startswith("glass_dirty.eye") else 0


atlas = overbake.Atlas("track_doll", parts, BAKED, kind_of)
atlas.unwrap(boosts={FACE: 3.2, EYE: 3.2})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"head": (0.008, 0.02), "hair": (0.02, 0.04), "dress": (0.02, 0.04), "limbs": (0.01, 0.03)},
           height=2.2, masks={"face": face_paint, "eyes": eyes_and_tears, "wear": wear})

M = atlas.maps
face = M["face"]
eyes = M["eyes"]
worn = M["wear"]
gentle = atlas.masks["head"]
base = atlas.base(soot=(0.02, 0.018, 0.016), crease=0.4, ao_floor=0.55, gentle=gentle)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


# The glaze gone ivory-grey with age; then the paint over it, the glass eyes, the soot and the break.
base = paint(base, (0.36, 0.1, 0.1), face[..., 2] * 0.3)           # rouge on the cheeks
base = paint(base, (0.3, 0.07, 0.07), face[..., 1] * 0.85)          # the lips
base = paint(base, (0.028, 0.02, 0.017), face[..., 0])              # brows, lashes, lids, mouth line
base = paint(base, (0.07, 0.11, 0.16), eyes[..., 0])                # grey-blue glass, dark enough to read far off
base = paint(base, (0.008, 0.008, 0.01), eyes[..., 1])              # pupil and ring
base = paint(base, (0.03, 0.026, 0.022), eyes[..., 2] * 0.75)       # soot run from the eyes
base = paint(base, (0.1, 0.085, 0.07), worn[..., 2] * 0.3)          # grime in the crazing
base = paint(base, (0.05, 0.04, 0.03), worn[..., 1] * 0.55)         # dirt from the ground up
base = paint(base, (0.012, 0.01, 0.01), worn[..., 0])               # the crack, the chip's hollow

# Gloss: the porcelain and the glass glazed, the chip's bisque matte, the hair and the cloth dull, the boots between.
rough = np.full(base.shape[:2], 0.85, np.float32)
rough[atlas.masks["head"] | atlas.masks["limbs"]] = 0.2
rough[atlas.masks["hair"]] = 0.65
rough = np.where(worn[..., 0] > 0.3, 0.85, rough)
rough = np.where(worn[..., 1] > 0.4, np.maximum(rough, 0.5), rough)
atlas.finish(base, kit, arm, made=make.provenance("track_doll", "the Track Doll, modelled over tools/blender/track_doll.py"),
             split={FACE: "face", EYE: "face"}, rough=rough)
