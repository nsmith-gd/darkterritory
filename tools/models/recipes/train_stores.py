"""The train's small stores and the loot carried by hand (GDD §12 "where the repair kits sit, where emergency lamps are kept,
where tools and fire extinguishers hang"; App. C.3-C.5), modelled and baked, one file, a prop each:

  * toy_bear, toy_horse, toy_doll: the toys (App. C.4, BodyKind.Toy: hand loot, and what appeases the Track Doll). Each
    fits the toy's 0.15 m body and is centred on it. Somebody's child loved each one, and they've been out here since:
    - toy_bear: a rag bear gone bald at the paws, one button eye and a cross of thread where the other was, its mouth
      stitched shut and a split seam in its belly with the stuffing coming out;
    - toy_horse: a pull-along horse on a wheeled board, the paint flaked to the wood, an ear snapped off, its tow string;
    - toy_doll: a little porcelain-headed doll in a smock, the Track Doll's own kind (A.2), the face's paint rubbed thin.
    The noisy toys (App. C.4, C.7: Body.Noise, each heard while it's carried) look like what they sound like:
    - toy_squeaker: a squeeze toy, a papier-mache pig stood on a pleated cloth bellows on a turned base, the reed's hole
      in the base's side; the pig's paint crazed, one ear chewed;
    - toy_musicbox: a little walnut music box with its lid up, the brass comb and pinned cylinder under glass, a bone
      dancer on her spindle in the lid's well and the winding key in the side;
    - toy_drummer: a tin wind-up drummer boy, red tunic and a tall shako, the drum on his front and both sticks up mid
      beat, the big winding key in his back.
  * extinguisher: a copper soda-acid extinguisher (App. C.5) 0.6 m tall, its brass cap and plunger, carrying handle and
    hose, the FIRE band, and a sight glass up its front: the game draws the charge as a column in the glass between the
    sockets glass_lo and glass_hi (Body.Charge, replicated to the percent). Centred.
  * extinguisher_mount: its place on every car's wall: a red board with FIRE on it, and an iron floor cradle and a hoop
    strap to stand it in. Its origin is World.ExtinguisherMount, where the sim stands the extinguisher (on the floor,
    0.3 m out from the left wall); the wall is at -X (Blender axes; the car's own).
  * repair_kit: the engineer's toolbox (§12 "a toolbox somebody grabbed"): sheet steel painted oxide, the paint worn
    off the corners and the handle, a hasp, the stencil. Centred.
  * shot_locker: the gun car's ready-use locker (App. C.3 "stocked at departure; carried to the cannon"): an iron-bound
    chest with its lid propped open, twelve wells for the balls on one side and six powder bags' places on the other.
    The game fills it from the gun's stock: sockets ball_0..ball_11 and bag_0..bag_5, where a cannon_ball or a
    powder_bag sits. Its origin is on the floor under its middle, its length along Y.
  * powder_bag: one powder charge, a serge cartridge bag tied off at its neck, 0.2 m, its bottom at the origin.

Axes: Blender +Z up. Modelled here and baked (tools/models/make).

    tools/models/build.sh train_stores [toy_bear ...]
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


def materials():
    return {
        "fur": make.lib("wool", 9.0, tint=(0.55, 0.42, 0.3), rough=0.95),
        "bald": make.lib("wool", 14.0, tint=(0.62, 0.52, 0.42), rough=0.9),
        "stuffing": make.lib("wool", 16.0, tint=(0.82, 0.78, 0.68), rough=0.95),
        "thread": make.flat("thread_black", (0.03, 0.025, 0.02), rough=0.8),
        "button": make.lib("brass", 6.0, tint=(0.35, 0.3, 0.2), rough=0.4, metal=0.6),
        "wood": make.lib("wood_sleeper", 9.0, tint=(0.7, 0.55, 0.4), rough=0.6),
        "paint_white": make.lib("plaster_ruin", 8.0, tint=(0.82, 0.8, 0.74), rough=0.55),
        "paint_red": make.lib("paint_oxide", 6.0, tint=(1.0, 0.55, 0.45), rough=0.55),
        "paint_dark": make.lib("paint_black", 6.0, tint=(0.6, 0.6, 0.6), rough=0.5),
        "porcelain": make.lib("plaster_ruin", 18.0, tint=(0.95, 0.9, 0.86), rough=0.3),
        "smock": make.lib("wool", 12.0, tint=(0.5, 0.52, 0.55), rough=0.95),
        "hair": make.lib("wool", 20.0, tint=(0.22, 0.15, 0.1), rough=0.9),
        "string": make.lib("wool", 20.0, tint=(0.75, 0.66, 0.5), rough=0.95),
        "copper": make.lib("copper_pipe", 1.1, tint=(1.05, 0.82, 0.66), rough=0.35, metal=0.85),
        "brass": make.lib("brass", 4.0, rough=0.35, metal=0.8),
        "hose": make.lib("leather", 8.0, tint=(0.35, 0.3, 0.26), rough=0.7),
        "glass": make.lib("glass_dirty", 6.0, tint=(0.75, 0.8, 0.78), rough=0.15),
        "board": make.lib("wood_siding", 2.5, tint=(1.0, 0.42, 0.34), rough=0.6),
        "iron": make.lib("rust_heavy", 5.0, tint=(0.42, 0.38, 0.35), rough=0.55, metal=0.6),
        "oxide": make.lib("paint_oxide", 3.0, tint=(0.85, 0.75, 0.7), rough=0.5, metal=0.3),
        "steel": make.lib("iron_plate", 5.0, tint=(0.7, 0.68, 0.65), rough=0.35, metal=0.8),
        "crate": make.lib("wood_crate", 1.6, tint=(0.85, 0.78, 0.68), rough=0.65),
        "serge": make.lib("wool", 10.0, tint=(0.7, 0.62, 0.48), rough=0.95),
        "rope": make.lib("wool", 7.0, tint=(0.8, 0.68, 0.5), rough=0.95),
        "ink_white": make.flat("ink_white", (0.78, 0.76, 0.7), rough=0.8),
        "ink_black": make.flat("ink_black", (0.05, 0.045, 0.04), rough=0.8),
        "ink_red": make.flat("ink_red", (0.45, 0.06, 0.04), rough=0.8),
        "pig": make.lib("plaster_ruin", 10.0, tint=(0.95, 0.66, 0.6), rough=0.6),
        "walnut": make.lib("wood_sleeper", 16.0, tint=(0.8, 0.52, 0.34), rough=0.35),
        "velvet": make.lib("wool", 18.0, tint=(0.45, 0.08, 0.1), rough=0.95),
        "vellum": make.lib("plaster_ruin", 12.0, tint=(0.9, 0.85, 0.72), rough=0.6),
        "tin_red": make.lib("paint_oxide", 8.0, tint=(1.2, 0.45, 0.35), rough=0.4, metal=0.4),
        "tin_blue": make.lib("paint_black", 8.0, tint=(0.45, 0.55, 0.85), rough=0.4, metal=0.4),
        "tin_green": make.lib("paint_black", 8.0, tint=(0.45, 0.7, 0.45), rough=0.45, metal=0.4),
        # The toys (note 372; the director, 8 Oct, on what's found at a stop: "a bit of a brighter more unique look to them so
        # that they stand out from the background as interactable objects. Do this with good texture work, not VFX"): their own
        # paint and cloth, each tinted to come out at the colour in its comment on its library layer, so after the finds atlas
        # they're the brightest, most coloured things in a dark house; glaze and enamel kept smooth, so a lamp's highlight sits
        # on them. Still worn: the library layers carry their wear.
        "toy_fur": make.lib("wool", 9.0, tint=(7.73, 4.11, 1.12), rough=0.9),  # sRGB (176, 132, 76)
        "toy_bald": make.lib("wool", 14.0, tint=(9.83, 6.79, 3.33), rough=0.85),  # sRGB (196, 166, 128)
        "toy_stuffing": make.lib("wool", 16.0, tint=(13.28, 12.23, 8.71), rough=0.95),  # sRGB (224, 216, 198)
        "toy_button": make.lib("brass", 6.0, tint=(3.47, 2.66, 1.16), rough=0.3, metal=0.6),  # sRGB (196, 156, 84)
        "toy_wood": make.lib("wood_sleeper", 9.0, tint=(3.94, 2.62, 1.29), rough=0.5),  # sRGB (156, 116, 76)
        "toy_paint_white": make.lib("plaster_ruin", 8.0, tint=(5.61, 6.13, 5.92), rough=0.35),  # sRGB (226, 218, 200)
        "toy_paint_red": make.lib("paint_oxide", 6.0, tint=(2.65, 0.33, 0.34), rough=0.35),  # sRGB (184, 46, 38)
        "toy_porcelain": make.lib("plaster_ruin", 18.0, tint=(6.3, 6.78, 7.04), rough=0.15),  # sRGB (238, 228, 216)
        "toy_smock": make.lib("wool", 12.0, tint=(10.06, 3.47, 3.44), rough=0.9),  # sRGB (198, 122, 130)
        "toy_hair": make.lib("wool", 20.0, tint=(3.85, 1.09, 0.3), rough=0.85),  # sRGB (128, 70, 38)
        "toy_string": make.lib("wool", 20.0, tint=(11.98, 9.61, 5.13), rough=0.95),  # sRGB (214, 194, 156)
        "toy_pig": make.lib("plaster_ruin", 10.0, tint=(6.18, 3.24, 3.22), rough=0.3),  # sRGB (236, 164, 152)
        "toy_walnut": make.lib("wood_sleeper", 16.0, tint=(3.94, 1.76, 0.75), rough=0.2),  # sRGB (156, 96, 58)
        "toy_velvet": make.lib("wool", 18.0, tint=(5.92, 0.26, 0.39), rough=0.95),  # sRGB (156, 32, 44)
        "toy_vellum": make.lib("plaster_ruin", 12.0, tint=(6.07, 6.38, 5.4), rough=0.5),  # sRGB (234, 222, 192)
        "toy_tin_red": make.lib("paint_oxide", 8.0, tint=(3.33, 0.42, 0.4), rough=0.25, metal=0.45),  # sRGB (204, 52, 42)
        "toy_tin_blue": make.lib("paint_black", 8.0, tint=(0.91, 2.15, 8.21), rough=0.25, metal=0.45),  # sRGB (62, 92, 176)
        "toy_tin_green": make.lib("paint_black", 8.0, tint=(1.23, 4.95, 1.3), rough=0.3, metal=0.45),  # sRGB (72, 136, 74)
        "toy_brass": make.lib("brass", 4.0, tint=(3.8, 3.14, 1.4), rough=0.25, metal=0.8),  # sRGB (204, 168, 92)
    }


def blob(centre, radii, material, name="blob", n=20, low=14, rot=None):
    """An ellipsoid (a bear's belly, a horse's barrel, a doll's head), `radii` along its own x, y, z, and its coarser twin."""
    out = None
    for segs, keep in ((low, bool(low)), (n, True)):
        if not keep:
            continue
        m = Matrix.Diagonal((*radii, 1))
        if rot is not None:
            m = rot @ m
        o = cook.uv_sphere(segs, max(4, segs // 2), 1, Matrix.Translation(Vector(centre)) @ m)
        o.data.materials.clear()
        o.data.materials.append(material)
        if segs == low and segs != n:
            o.name = name + "_low"
            make.LOW.append(o)
        else:
            out = make._finish(o, material, 0, 1, name)
    return out


def limb(a, b, r, material, name="limb", r1=None, low=10):
    """A stuffed or turned limb: a cylinder with a ball on its far end."""
    return [make.cyl(a, b, r, material, n=12, bevel=0, name=name, r1=r1, low=low),
            blob(b, (r1 or r,) * 3, material, name=name + "_end", n=12, low=0)]


def run(points, r, material, name="run", low=6):
    """A string, hose or strap through `points`: straight lengths and no joint balls (a pipe has those)."""
    pts = [Vector(p) for p in points]
    return [make.cyl(a, b, r, material, n=8, bevel=0, name=name, low=low) for a, b in zip(pts, pts[1:])]


# ---------------------------------------------------------------------------------------------------------- toys

def toy_bear(m):
    p = [blob((0, 0, -0.035), (0.068, 0.058, 0.075), m["toy_fur"], "belly")]
    p.append(blob((0, -0.002, 0.075), (0.055, 0.05, 0.05), m["toy_fur"], "head"))
    p.append(blob((0, -0.045, 0.065), (0.025, 0.022, 0.02), m["toy_bald"], "snout"))
    p.append(blob((0, -0.066, 0.07), (0.009, 0.006, 0.007), m["thread"], "nose", n=10, low=0))
    for sx in (-1, 1):
        p.append(blob((sx * 0.042, 0.005, 0.118), (0.02, 0.01, 0.02), m["toy_fur"], "ear", n=12, low=6))
        # The arms hanging forward and down, the legs out in front: it's been sat somewhere.
        p += limb((sx * 0.06, -0.01, 0.01), (sx * 0.08, -0.04, -0.06), 0.022, m["toy_fur"], "arm", r1=0.02)
        p.append(blob((sx * 0.08, -0.042, -0.068), (0.02, 0.02, 0.018), m["toy_bald"], "paw", n=12, low=0))
        p += limb((sx * 0.04, -0.02, -0.09), (sx * 0.055, -0.09, -0.1), 0.027, m["toy_fur"], "leg", r1=0.025)
        p.append(blob((sx * 0.055, -0.112, -0.1), (0.024, 0.006, 0.026), m["toy_bald"], "sole", n=12, low=0))
    # One brass button eye; the other's gone and a cross of black thread is sewn over the place.
    p.append(make.cyl((-0.021, -0.043, 0.088), (-0.021, -0.05, 0.088), 0.008, m["toy_button"], n=10, bevel=0.001, name="eye", low=0))
    for a in (0.8, -0.8):
        p.append(make.box((0.021, -0.047, 0.088), (0.009, 0.0012, 0.0012), m["thread"], bevel=0, name="cross", low=False,
                          rot=Matrix.Rotation(a, 4, "Y")))
    # The mouth sewn shut: a line of stitches under the snout.
    for k in range(5):
        x = -0.014 + k * 0.007
        p.append(make.box((x, -0.061, 0.052), (0.0012, 0.0012, 0.005), m["thread"], bevel=0, name="stitch", low=False))
    p.append(make.box((0, -0.0605, 0.052), (0.016, 0.001, 0.001), m["thread"], bevel=0, name="seam", low=False))
    # The belly's seam split, the stuffing pushed out of it.
    p.append(make.box((0.01, -0.057, -0.03), (0.004, 0.003, 0.03), m["thread"], bevel=0.001, name="split", low=False,
                      rot=Matrix.Rotation(0.15, 4, "Y")))
    for k, (dz, s) in enumerate(((0.012, 0.012), (-0.008, 0.014), (-0.03, 0.01))):
        p.append(blob((0.012 + 0.003 * k, -0.06, -0.03 + dz), (s, s * 0.7, s), m["toy_stuffing"], "stuffing", n=10, low=0))
    return p


def toy_horse(m):
    # The board on its four iron wheels; the horse stood on it.
    p = [make.box((0, 0, -0.1), (0.045, 0.12, 0.01), m["toy_wood"], bevel=0.003, name="board")]
    for sx in (-1, 1):
        for y in (-0.09, 0.09):
            p.append(make.cyl((sx * 0.05, y, -0.115), (sx * 0.06, y, -0.115), 0.022, m["iron"], n=14, bevel=0.002, name="wheel", low=8))
    p.append(blob((0, 0, 0.0), (0.04, 0.09, 0.045), m["toy_paint_white"], "barrel"))
    for sx in (-1, 1):
        for y in (-0.06, 0.06):
            p.append(make.cyl((sx * 0.025, y, -0.02), (sx * 0.028, y, -0.09), 0.012, m["toy_paint_white"], n=10, bevel=0, name="leg", r1=0.009, low=6))
            p.append(make.cyl((sx * 0.028, y, -0.085), (sx * 0.028, y, -0.092), 0.011, m["paint_dark"], n=10, bevel=0, name="hoof", low=0))
    # The neck up and forward (the horse faces -Y), the head down off it.
    p.append(make.cyl((0, -0.06, 0.02), (0, -0.1, 0.1), 0.028, m["toy_paint_white"], n=12, bevel=0, name="neck", r1=0.022, low=6))
    p.append(blob((0, -0.125, 0.1), (0.022, 0.045, 0.024), m["toy_paint_white"], "head", rot=Matrix.Rotation(0.5, 4, "X")))
    # Its one ear (the other snapped off at the root: a stub of bare wood), the mane, the painted saddle and eyes.
    p.append(make.cyl((0.012, -0.105, 0.125), (0.016, -0.1, 0.152), 0.007, m["toy_paint_white"], n=8, bevel=0, name="ear", r1=0.002, low=4))
    p.append(make.cyl((-0.012, -0.105, 0.125), (-0.013, -0.104, 0.131), 0.007, m["toy_wood"], n=8, bevel=0, name="ear_stub", low=0))
    for k in range(6):
        t = k / 5
        p.append(blob((0, -0.06 - 0.04 * t, 0.05 + 0.07 * t), (0.006, 0.012, 0.016), m["toy_hair"], "mane", n=8, low=0))
    p.append(make.box((0, 0.01, 0.04), (0.042, 0.03, 0.01), m["toy_paint_red"], bevel=0.004, name="saddle"))
    for sx in (-1, 1):
        p.append(blob((sx * 0.02, -0.135, 0.11), (0.003, 0.006, 0.004), m["ink_black"], "eye", n=8, low=0))
    p.append(make.cyl((0, 0.09, 0.0), (0, 0.13, -0.03), 0.009, m["toy_hair"], n=8, bevel=0, name="tail", r1=0.004, low=4))
    # Its tow string, a ring on the board's front and the string slack along the floor.
    p.append(make.torus((0, -0.125, -0.1), (0, 1, 0), 0.008, 0.002, m["iron"], name="eye_ring", low=False))
    pts = [Vector((0, -0.13, -0.1)), Vector((0.01, -0.16, -0.115)), Vector((0.03, -0.2, -0.122)), Vector((0.02, -0.24, -0.122))]
    p += run(pts, 0.002, m["toy_string"], name="tow", low=0)
    return p


def toy_doll(m):
    # The smock: a bell of cloth to the knees; the legs and button boots under it.
    p = [make.cyl((0, 0, -0.06), (0, 0, 0.04), 0.06, m["toy_smock"], n=16, bevel=0.002, name="smock", r1=0.03, low=8)]
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.018, 0, -0.06), (sx * 0.02, -0.005, -0.12), 0.011, m["toy_porcelain"], n=10, bevel=0, name="leg", low=6))
        p.append(blob((sx * 0.02, -0.012, -0.125), (0.013, 0.02, 0.01), m["paint_dark"], "boot", n=10, low=6))
        # The arms down at its sides, a little forward, the hands open.
        p += limb((sx * 0.032, 0, 0.032), (sx * 0.05, -0.02, -0.02), 0.009, m["toy_porcelain"], "arm", r1=0.008)
    p.append(blob((0, -0.005, 0.045), (0.03, 0.022, 0.012), m["toy_smock"], "collar", n=12, low=6))
    # The head: porcelain, too big for it, tipped a little to one side; the paint of the face rubbed thin.
    tilt = Matrix.Rotation(0.18, 4, "Y")
    p.append(blob((0.004, 0, 0.088), (0.038, 0.036, 0.042), m["toy_porcelain"], "head", rot=tilt))
    p.append(blob((0.004, 0.006, 0.105), (0.04, 0.036, 0.03), m["toy_hair"], "hair", rot=tilt))
    for sx in (-1, 1):
        p.append(blob((0.004 + sx * 0.014, -0.033, 0.094), (0.006, 0.003, 0.007), m["ink_black"], "eye", n=10, low=0))
        p.append(blob((0.004 + sx * 0.016, -0.03, 0.078), (0.007, 0.003, 0.005), m["ink_red"], "cheek", n=8, low=0))
    p.append(blob((0.004, -0.035, 0.07), (0.005, 0.002, 0.002), m["ink_red"], "mouth", n=8, low=0))
    return p


def toy_squeaker(m):
    # The turned base and the pleated bellows on it: rings of cloth in and out, the reed's hole in the base.
    p = [make.cyl((0, 0, -0.15), (0, 0, -0.12), 0.05, m["toy_wood"], n=18, bevel=0.004, name="base", low=10)]
    p.append(make.cyl((0.05, 0, -0.135), (0.054, 0, -0.135), 0.008, m["ink_black"], n=10, bevel=0, name="reed_hole", low=0))
    z = -0.12
    for k in range(5):
        r = 0.046 if k % 2 == 0 else 0.038
        p.append(make.cyl((0, 0, z), (0, 0, z + 0.012), r, m["serge"], n=18, bevel=0.002, name="pleat", r1=0.038 if k % 2 == 0 else 0.046, low=8))
        z += 0.012
    p.append(make.cyl((0, 0, z), (0, 0, z + 0.006), 0.046, m["toy_wood"], n=18, bevel=0.002, name="top", low=10))
    # The pig on it: a fat pink barrel, snout and ears, four stub legs on the board, a curl of tail.
    pig = z + 0.006
    p.append(blob((0, 0, pig + 0.035), (0.036, 0.05, 0.034), m["toy_pig"], "body"))
    p.append(blob((0, -0.048, pig + 0.045), (0.026, 0.024, 0.024), m["toy_pig"], "head"))
    p.append(make.cyl((0, -0.068, pig + 0.042), (0, -0.08, pig + 0.04), 0.012, m["toy_pig"], n=12, bevel=0.002, name="snout", low=6))
    for sx in (-1, 1):
        p.append(blob((sx * 0.005, -0.081, pig + 0.041), (0.002, 0.001, 0.003), m["ink_black"], "nostril", n=6, low=0))
        p.append(blob((sx * 0.011, -0.066, pig + 0.055), (0.003, 0.002, 0.003), m["ink_black"], "eye", n=8, low=0))
        for y in (-0.03, 0.03):
            p.append(make.cyl((sx * 0.02, y, pig + 0.012), (sx * 0.02, y, pig), 0.009, m["toy_pig"], n=8, bevel=0, name="leg", low=6))
    # One ear whole, flopped forward over the eye; the other chewed to a ragged stub.
    p.append(blob((0.014, -0.052, pig + 0.066), (0.012, 0.004, 0.011), m["toy_pig"], "ear", n=12, low=6,
                  rot=Matrix.Rotation(-0.7, 4, "X") @ Matrix.Rotation(0.3, 4, "Y")))
    p.append(blob((-0.014, -0.046, pig + 0.064), (0.006, 0.004, 0.005), m["toy_pig"], "ear_stub", n=10, low=6))
    p.append(make.torus((0, 0.052, pig + 0.045), (0, 1, 0), 0.008, 0.002, m["toy_pig"], name="tail"))
    return p


def toy_musicbox(m):
    # The box: walnut, a brass escutcheon; inside under its glass the comb and the pinned cylinder.
    w, d, h = 0.07, 0.05, 0.05
    base = -0.15
    p = [make.box((0, 0, base + h / 2), (w, d, h / 2), m["toy_walnut"], bevel=0.004, name="box")]
    p.append(make.box((0, 0, base + h + 0.001), (w - 0.008, d - 0.008, 0.002), m["glass"], bevel=0, name="glass", low=False))
    p.append(make.cyl((-0.035, 0.012, base + h - 0.004), (0.035, 0.012, base + h - 0.004), 0.009, m["toy_brass"], n=12, bevel=0.001, name="cylinder", low=0))
    for k in range(14):
        x = -0.03 + k * 0.0046
        p.append(make.box((x, -0.008, base + h - 0.006), (0.0018, 0.014, 0.0008), m["steel"], bevel=0, name="tooth", low=False))
    p.append(make.box((0, -d - 0.001, base + h * 0.6), (0.008, 0.001, 0.006), m["toy_brass"], bevel=0.001, name="escutcheon", low=False))
    # Brass corners and a pale inlaid panel on its front, so it reads as a keepsake, not a crate.
    p.append(make.box((0, -d - 0.0008, base + h * 0.45), (w * 0.7, 0.0008, h * 0.3), m["toy_vellum"], bevel=0, name="inlay", low=False))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.box((sx * (w - 0.004), sy * (d - 0.004), base + h / 2), (0.0045, 0.0045, h / 2 + 0.001), m["toy_brass"],
                              bevel=0.001, name="corner", low=False))
    # The winding key in the right side: a stem and its butterfly.
    p.append(make.cyl((w, 0, base + 0.02), (w + 0.012, 0, base + 0.02), 0.003, m["toy_brass"], n=8, bevel=0, name="key_stem", low=6))
    p.append(make.box((w + 0.014, 0, base + 0.02), (0.002, 0.012, 0.007), m["toy_brass"], bevel=0.002, name="key_wings"))
    # The lid up on its hinge at the back, tipped past square; in its lid's well the bone dancer on her spindle.
    hinge = Vector((0, d, base + h))
    lid = Matrix.Translation(hinge) @ Matrix.Rotation(-1.75, 4, "X") @ Matrix.Translation(-hinge)
    p.append(make.box(tuple(lid @ Vector((0, 0, base + h + 0.006))), (w, d, 0.006), m["toy_walnut"], bevel=0.003, name="lid",
                      rot=Matrix.Rotation(-1.75, 4, "X")))
    p.append(make.box(tuple(lid @ Vector((0, 0, base + h - 0.0005))), (w - 0.01, d - 0.01, 0.001), m["toy_velvet"], bevel=0, name="lining", low=False,
                      rot=Matrix.Rotation(-1.75, 4, "X")))
    # The dancer on top of the box, by the glass: a spindle, a skirt, arms up in an arc, her head.
    at = Vector((0.0, -0.025, base + h + 0.004))
    k = 1.7
    p.append(make.cyl(tuple(at - Vector((0, 0, 0.004))), tuple(at + Vector((0, 0, 0.002))), 0.012, m["toy_brass"], n=12, bevel=0.001, name="pedestal", low=8))
    p.append(make.cyl(tuple(at), tuple(at + Vector((0, 0, 0.012 * k))), 0.0025, m["toy_brass"], n=8, bevel=0, name="spindle", low=4))
    p.append(make.cyl(tuple(at + Vector((0, 0, 0.012 * k))), tuple(at + Vector((0, 0, 0.022 * k))), 0.012 * k, m["toy_porcelain"], n=12, bevel=0.001, name="skirt", r1=0.003 * k, low=8))
    p.append(make.cyl(tuple(at + Vector((0, 0, 0.02 * k))), tuple(at + Vector((0, 0, 0.034 * k))), 0.004 * k, m["toy_porcelain"], n=8, bevel=0, name="bodice", low=4))
    p.append(blob(tuple(at + Vector((0, 0, 0.039 * k))), (0.005 * k, 0.005 * k, 0.006 * k), m["toy_porcelain"], "head", n=10, low=6))
    for sx in (-1, 1):
        p.append(make.cyl(tuple(at + Vector((sx * 0.003 * k, 0, 0.032 * k))), tuple(at + Vector((sx * 0.006 * k, 0, 0.046 * k))), 0.0015 * k, m["toy_porcelain"], n=6, bevel=0, name="arm", low=4))
    return p


def toy_drummer(m):
    # A tin drummer boy: his legs on a stand, the tunic, the shako; the drum on his front, the sticks up mid beat.
    p = [make.cyl((0, 0, -0.15), (0, 0, -0.14), 0.035, m["toy_tin_green"], n=16, bevel=0.002, name="stand", low=8)]
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.01, 0, -0.14), (sx * 0.01, 0, -0.08), 0.008, m["toy_tin_blue"], n=10, bevel=0, name="leg", low=6))
        p.append(make.box((sx * 0.01, -0.006, -0.137), (0.007, 0.012, 0.004), m["paint_dark"], bevel=0.001, name="boot"))
    p.append(make.cyl((0, 0, -0.085), (0, 0, -0.025), 0.02, m["toy_tin_red"], n=14, bevel=0.002, name="tunic", r1=0.017, low=8))
    p.append(make.box((0, 0, -0.055), (0.021, 0.021, 0.003), m["ink_white"], bevel=0, name="belt", low=False))
    p.append(blob((0, 0, -0.012), (0.012, 0.012, 0.013), m["toy_porcelain"], "head", n=12, low=6))
    for sx in (-1, 1):
        p.append(blob((sx * 0.005, -0.011, -0.01), (0.002, 0.001, 0.002), m["ink_black"], "eye", n=6, low=0))
    p.append(make.cyl((0, 0, -0.003), (0, 0, 0.03), 0.013, m["paint_dark"], n=12, bevel=0.001, name="shako", r1=0.015, low=6))
    p.append(make.box((0, -0.014, -0.002), (0.012, 0.004, 0.002), m["paint_dark"], bevel=0, name="peak", low=False))
    p.append(blob((0, -0.01, 0.033), (0.004, 0.004, 0.007), m["ink_red"], "plume", n=8, low=6))
    # The drum: tin shell, brass hoops, cord zigzag up its side.
    drum = Vector((0, -0.035, -0.07))
    p.append(make.cyl(tuple(drum + Vector((0, 0, -0.015))), tuple(drum + Vector((0, 0, 0.015))), 0.026, m["toy_tin_blue"], n=18, bevel=0.002, name="drum", low=10))
    for dz in (-0.015, 0.015):
        p.append(make.torus(tuple(drum + Vector((0, 0, dz))), (0, 0, 1), 0.026, 0.0025, m["toy_brass"], name="hoop", low=False))
    p.append(make.cyl(tuple(drum + Vector((0, 0, 0.015))), tuple(drum + Vector((0, 0, 0.016))), 0.024, m["toy_vellum"], n=18, bevel=0, name="head", low=0))
    for k in range(8):
        a = k * math.tau / 8
        lo = drum + Vector((math.cos(a) * 0.027, math.sin(a) * 0.027, -0.014))
        hi = drum + Vector((math.cos(a + 0.4) * 0.027, math.sin(a + 0.4) * 0.027, 0.014))
        p.append(make.cyl(tuple(lo), tuple(hi), 0.0012, m["ink_white"], n=6, bevel=0, name="cord", low=0))
    # The arms out to the sticks, the sticks raised over the drum head.
    for sx in (-1, 1):
        sh = Vector((sx * 0.021, 0, -0.035))
        hand = Vector((sx * 0.02, -0.03, -0.04))
        p.append(make.cyl(tuple(sh), tuple(hand), 0.006, m["toy_tin_red"], n=8, bevel=0, name="arm", low=6))
        p.append(make.cyl(tuple(hand), tuple(hand + Vector((-sx * 0.008, -0.02, 0.03))), 0.0018, m["toy_wood"], n=6, bevel=0, name="stick", low=4))
        p.append(blob(tuple(hand + Vector((-sx * 0.008, -0.02, 0.03))), (0.003, 0.003, 0.003), m["toy_wood"], "stick_end", n=8, low=4))
    # The big key in his back.
    p.append(make.cyl((0, 0.018, -0.05), (0, 0.035, -0.05), 0.003, m["toy_brass"], n=8, bevel=0, name="key_stem", low=6))
    p.append(make.box((0, 0.037, -0.05), (0.02, 0.002, 0.01), m["toy_brass"], bevel=0.003, name="key_wings"))
    return p


# ---------------------------------------------------------------------------------------------------- extinguisher

EXT_R, EXT_H = 0.085, 0.6
GLASS_LO, GLASS_HI = Vector((0, -EXT_R - 0.016, -0.21)), Vector((0, -EXT_R - 0.016, 0.16))


def extinguisher(m):
    lo, hi = -EXT_H / 2, EXT_H / 2 - 0.08
    p = [make.cyl((0, 0, lo + 0.01), (0, 0, hi), EXT_R, m["copper"], n=24, bevel=0.004, name="shell", low=12)]
    p.append(make.cyl((0, 0, lo), (0, 0, lo + 0.02), EXT_R + 0.004, m["brass"], n=24, bevel=0.003, name="foot", low=12))
    # The domed top and the brass cap screwed on it, the plunger knob you strike to set it off.
    p.append(blob((0, 0, hi), (EXT_R, EXT_R, 0.04), m["copper"], "dome", n=24, low=12))
    p.append(make.cyl((0, 0, hi + 0.03), (0, 0, hi + 0.06), 0.035, m["brass"], n=16, bevel=0.003, name="cap", low=8))
    p.append(make.cyl((0, 0, hi + 0.06), (0, 0, hi + 0.075), 0.012, m["brass"], n=10, bevel=0.002, name="plunger", low=6))
    p.append(blob((0, 0, hi + 0.082), (0.018, 0.018, 0.01), m["paint_red"], "knob", n=12, low=6))
    # The carrying handle over the top, the hose from the cap's side down to the nozzle clipped low on the shell.
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.05, 0.02, hi + 0.01), (sx * 0.05, 0.02, hi + 0.08), 0.007, m["brass"], n=8, bevel=0, name="handle_post", low=4))
    p.append(make.cyl((-0.05, 0.02, hi + 0.08), (0.05, 0.02, hi + 0.08), 0.009, m["hose"], n=8, bevel=0, name="handle", low=4))
    hose = [Vector((EXT_R * 0.5, 0.03, hi + 0.04)), Vector((EXT_R + 0.03, 0.04, hi)), Vector((EXT_R + 0.035, 0.045, 0.0)),
            Vector((EXT_R + 0.025, 0.04, lo + 0.12))]
    p += run(hose, 0.011, m["hose"], name="hose", low=6)
    p.append(make.cyl(hose[-1], hose[-1] + Vector((0, 0, -0.07)), 0.01, m["brass"], n=10, bevel=0.002, name="nozzle", r1=0.006, low=6))
    p.append(make.torus((EXT_R + 0.004, 0.035, lo + 0.1), (0, 0, 1), 0.012, 0.003, m["brass"], name="clip", low=False))
    # The bands: rivetted brass at the shoulder and the foot; the painted FIRE band round the middle.
    for z in (lo + 0.06, hi - 0.02):
        p.append(make.torus((0, 0, z), (0, 0, 1), EXT_R + 0.002, 0.004, m["brass"], n=28, m=5, name="band", low=False))
    p.append(make.cyl((0, 0, 0.17), (0, 0, 0.23), EXT_R + 0.0015, m["paint_dark"], n=24, bevel=0, name="fire_band", low=0))
    p.append(make.stencil("FIRE", (0.0, -EXT_R - 0.0015, 0.2), (0, -1, 0), (0, 0, 1), 0.04, m["ink_white"]))
    p.append(make.stencil("TURN UP  STRIKE KNOB", (0.0, -EXT_R - 0.0015, -0.03), (0, -1, 0), (0, 0, 1), 0.012, m["ink_black"],
                          name="instr"))
    # The sight glass up the front: a glass tube in a brass cage between two glands (the charge is drawn in it).
    for z in (GLASS_LO.z - 0.015, GLASS_HI.z + 0.015):
        p.append(make.box((0, -EXT_R - 0.008, z), (0.024, 0.014, 0.012), m["brass"], bevel=0.003, name="gland"))
    p.append(make.cyl(GLASS_LO, GLASS_HI, 0.014, m["glass"], n=12, bevel=0, name="glass", low=8))
    for sx in (-1, 1):
        p.append(make.cyl(GLASS_LO + Vector((sx * 0.018, -0.003, 0)), GLASS_HI + Vector((sx * 0.018, -0.003, 0)), 0.0025,
                          m["brass"], n=6, bevel=0, name="cage", low=0))
    for k in range(5):
        z = GLASS_LO.z + (GLASS_HI.z - GLASS_LO.z) * k / 4
        p.append(make.box((0.024, -EXT_R - 0.012, z), (0.008, 0.001, 0.0015), m["ink_white"], bevel=0, name="tick", low=False))
    return p


def extinguisher_mount(m):
    # The board on the wall (the wall at x -0.3, the board's face out to +X) from knee height to over your head.
    wall = -0.3
    p = [make.box((wall + 0.012, 0, 0.62), (0.012, 0.19, 0.4), m["board"], bevel=0.006, name="board")]
    p += make.planks((wall + 0.024, -0.19, 0.22), (wall + 0.03, 0.19, 1.02), 2, 5, m["board"], gap=0.004, bevel=0.003, name="board_face", low=True)
    p.append(make.stencil("FIRE", (wall + 0.031, 0, 0.92), (1, 0, 0), (0, 0, 1), 0.09, m["ink_white"]))
    p.append(make.stencil("PUT IT BACK", (wall + 0.031, 0, 0.84), (1, 0, 0), (0, 0, 1), 0.035, m["ink_white"], name="put_back"))
    for z in (0.3, 0.95):
        for y in (-0.16, 0.16):
            p.append(make.nail((wall + 0.031, y, z), (1, 0, 0), m["iron"], r=0.008))
    # The cradle on the floor: an iron ring the extinguisher's foot stands in, on an arm back to the wall.
    p.append(make.torus((0, 0, 0.04), (0, 0, 1), EXT_R + 0.012, 0.008, m["iron"], n=24, m=6, name="cradle", low=(12, 4)))
    p.append(make.box(((wall + 0.03 - EXT_R) / 2 - 0.005, 0, 0.04), ((-wall - 0.03 - EXT_R) / 2, 0.012, 0.006), m["iron"], bevel=0.002, name="cradle_arm"))
    p.append(make.box((wall + 0.03, 0, 0.05), (0.006, 0.04, 0.03), m["iron"], bevel=0.002, name="cradle_foot"))
    # The strap at its shoulder: a half hoop open to the room, on two arms off the board; the hook for the hose.
    z = 0.48
    arc = [Vector((math.cos(a) * (EXT_R + 0.012), math.sin(a) * (EXT_R + 0.012), z))
           for a in [math.pi * (0.5 + k / 8) for k in range(9)]]
    p += run(arc, 0.006, m["iron"], name="hoop", low=4)
    for y in (-1, 1):
        p.append(make.box(((wall + 0.03 - 0.0) / 2, y * (EXT_R + 0.012), z), ((-wall - 0.03) / 2, 0.005, 0.012), m["iron"], bevel=0.002, name="hoop_arm"))
    p.append(make.cyl((wall + 0.03, 0.13, 0.7), (wall + 0.09, 0.13, 0.72), 0.008, m["iron"], n=8, bevel=0, name="hook", low=4))
    return p


# ---------------------------------------------------------------------------------------------------------- repair kit

def repair_kit(m):
    w, d, h = 0.23, 0.1, 0.09
    p = [make.box((0, 0, 0), (w, d, h), m["oxide"], bevel=0.008, name="box")]
    # The lid's lip and its two hinges; the hasp and a staple at the front, a padlock hung in it.
    p.append(make.box((0, 0, h + 0.012), (w + 0.004, d + 0.004, 0.012), m["oxide"], bevel=0.005, name="lid"))
    p.append(make.box((0, 0, h + 0.003), (w + 0.006, d + 0.006, 0.004), m["steel"], bevel=0.002, name="lid_lip", low=False))
    for x in (-0.14, 0.14):
        p.append(make.cyl((x - 0.03, d + 0.007, h + 0.002), (x + 0.03, d + 0.007, h + 0.002), 0.006, m["steel"], n=8, bevel=0, name="hinge", low=4))
    p.append(make.box((0, -d - 0.004, h - 0.01), (0.018, 0.003, 0.03), m["steel"], bevel=0.002, name="hasp"))
    p.append(make.torus((0, -d - 0.012, h - 0.04), (1, 0, 0), 0.012, 0.003, m["steel"], name="shackle", low=False))
    p.append(make.box((0, -d - 0.012, h - 0.065), (0.016, 0.006, 0.018), m["brass"], bevel=0.003, name="padlock"))
    # The handle: a strap of iron folded flat on the lid.
    for sx in (-1, 1):
        p.append(make.box((sx * 0.07, 0, h + 0.03), (0.008, 0.012, 0.008), m["steel"], bevel=0.002, name="handle_lug"))
    p.append(make.cyl((-0.07, 0, h + 0.038), (0.07, 0, h + 0.038), 0.009, m["iron"], n=8, bevel=0, name="handle", low=6))
    # Its stencil, both long sides; the paint worn off the corners to the steel.
    for sy in (-1, 1):
        p.append(make.stencil("REPAIR KIT", (0, sy * (d + 0.001), 0.01), (0, sy, 0), (0, 0, 1), 0.035, m["ink_white"]))
        p.append(make.stencil("LOCO DEPT", (0, sy * (d + 0.001), -0.04), (0, sy, 0), (0, 0, 1), 0.018, m["ink_white"], name="dept"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.box((sx * (w - 0.004), sy * (d - 0.004), 0), (0.006, 0.006, h + 0.001), m["steel"], bevel=0.003, name="corner", low=False))
    return p


# ---------------------------------------------------------------------------------------------------------- powder & shot

LOCKER = (0.22, 0.4, 0.34)  # half width, half length, height
BALL_R = 0.048


def ball_slots():
    w, l, h = LOCKER
    return [Vector((-0.1 + 0.1 * (k % 3), -l + 0.08 + 0.105 * (k // 3), h - 0.1 + BALL_R)) for k in range(12)]


def bag_slots():
    w, l, h = LOCKER
    return [Vector((-0.08 + 0.16 * (k % 2), 0.1 + 0.13 * (k // 2) - 0.02, 0.08)) for k in range(6)]


def shot_locker(m):
    w, l, h = LOCKER
    # A hollow chest: four walls of boards, 2 cm thick, on a floor (each wall its boards along its length, low twins too).
    t = 0.02
    p = [make.box((0, 0, 0.01), (w, l, 0.01), m["crate"], bevel=0.003, name="floor")]
    for sx in (-1, 1):
        x0, x1 = sorted((sx * w, sx * (w - t)))
        p += make.planks((x0, -l, 0), (x1, l, h), 2, 4, m["crate"], gap=0.005, bevel=0.004, name="side", low=True)
    for sy in (-1, 1):
        y0, y1 = sorted((sy * l, sy * (l - t)))
        p += make.planks((-w + t, y0, 0), (w - t, y1, h), 2, 4, m["crate"], gap=0.005, bevel=0.004, name="end", low=True)
    # Iron straps round it and at its corners; rope beckets at its ends.
    for y in (-l + 0.06, l - 0.06):
        p.append(make.box((0, y, h / 2), (w + 0.004, 0.022, h / 2 + 0.003), m["iron"], bevel=0.003, name="strap", low=False))
    for sy in (-1, 1):
        p.append(make.torus((0, sy * (l + 0.012), h * 0.62), (0, 1, 0), 0.04, 0.008, m["rope"], name="becket", low=False))
    # Inside: the shot tray (a board drilled with twelve wells) at the front half, the powder's bay at the back.
    p.append(make.box((0, -l / 2, h - 0.1), (w - 0.02, l / 2 - 0.02, 0.012), m["crate"], bevel=0.003, name="tray"))
    for c in ball_slots():
        p.append(make.cyl(c - Vector((0, 0, BALL_R + 0.012)), c - Vector((0, 0, BALL_R - 0.004)), BALL_R * 0.75, m["iron"], n=12, bevel=0, name="well", low=0))
    p.append(make.box((0, 0.0, h / 2), (w - 0.02, 0.012, h / 2 - 0.01), m["crate"], bevel=0.003, name="divider"))
    p.append(make.box((0, l / 2, 0.035), (w - 0.02, l / 2 - 0.02, 0.008), m["crate"], bevel=0.003, name="bay_floor"))
    # The lid on its hinges at the back long side, propped open past upright on its stay; the stencil inside it.
    lid = Matrix.Translation((-w, 0, h)) @ Matrix.Rotation(-1.9, 4, "Y") @ Matrix.Translation((w, 0, 0))
    # (Each piece modelled shut over the chest, then swung up about the hinge line.)
    lid_parts = [make.box((0, 0, 0.015), (w, l, 0.015), m["crate"], bevel=0.005, name="lid"),
                 make.box((0, 0, 0.035), (w - 0.02, 0.02, 0.006), m["iron"], bevel=0.002, name="lid_strap"),
                 make.stencil("POWDER & SHOT", (0.03, 0, -0.0015), (0, 0, -1), (1, 0, 0), 0.05, m["ink_white"]),
                 make.stencil("NO NAKED LIGHTS", (-0.06, 0, -0.0015), (0, 0, -1), (1, 0, 0), 0.028, m["ink_red"], name="warn")]
    for o in lid_parts:
        o.data.transform(lid)
    for o in make.LOW[-2:]:
        if o.name.startswith(("lid_low", "lid_strap_low")):
            o.data.transform(lid)
    p += lid_parts
    p.append(make.cyl((-w + 0.01, -l + 0.05, h), (-w - 0.06, -l + 0.05, h + 0.2), 0.006, m["iron"], n=6, bevel=0, name="stay", low=4))
    for y in (-l + 0.1, l - 0.1):
        p.append(make.cyl((-w - 0.005, y - 0.04, h), (-w - 0.005, y + 0.04, h), 0.008, m["iron"], n=8, bevel=0, name="hinge", low=4))
    for sx in (-1, 1):
        p.append(make.stencil("POWDER & SHOT", (sx * (w + 0.002), 0, h * 0.5), (sx, 0, 0), (0, 0, 1), 0.045, m["ink_white"], name="side_stencil"))
    return p


def powder_bag(m):
    # A serge bag on its end: the body, the shoulder gathered in to the neck and the tied-off tuft.
    p = [make.cyl((0, 0, 0), (0, 0, 0.15), 0.055, m["serge"], n=16, bevel=0.01, name="bag", r1=0.052, low=8)]
    p.append(blob((0, 0, 0.15), (0.052, 0.052, 0.03), m["serge"], "shoulder", n=16, low=8))
    p.append(make.cyl((0, 0, 0.17), (0, 0, 0.19), 0.016, m["serge"], n=10, bevel=0, name="neck", low=6))
    p.append(make.torus((0, 0, 0.18), (0, 0, 1), 0.017, 0.004, m["string"], name="tie", low=False))
    p.append(blob((0, 0, 0.2), (0.022, 0.022, 0.014), m["serge"], "tuft", n=10, low=0))
    p.append(make.stencil("1 LB", (0, -0.0545, 0.08), (0, -1, 0), (0, 0, 1), 0.03, m["ink_black"]))
    return p


def build(name, fn, what, budget, centre=True, size=512, sockets=None, floor=None):
    """`floor`: stood with its foot that far below the origin (a toy on the floor its 0.15 m body rests on), centred
    across."""
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = [o for o in fn(materials()) if o is not None]
    if centre:
        make.centre_on_origin(parts)
    if floor is not None:
        lo, hi = cook.bounds(parts + list(make.LOW))
        cook.move(parts + list(make.LOW), (0, 0, floor - lo.z))
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.005, reach=0.02, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.45, made=make.provenance("train_stores", what), sockets=sockets)


PIECES = {
    "toy_bear": lambda: build("toy_bear", toy_bear, "a toy: the rag bear", 900, floor=-0.15),
    "toy_horse": lambda: build("toy_horse", toy_horse, "a toy: the pull-along horse", 900, floor=-0.15),
    "toy_doll": lambda: build("toy_doll", toy_doll, "a toy: the porcelain doll", 900, floor=-0.15),
    "toy_squeaker": lambda: build("toy_squeaker", toy_squeaker, "a noisy toy: the squeeze pig", 900, floor=-0.15),
    "toy_musicbox": lambda: build("toy_musicbox", toy_musicbox, "a noisy toy: the music box", 900, floor=-0.15),
    "toy_drummer": lambda: build("toy_drummer", toy_drummer, "a noisy toy: the wind-up drummer", 900, floor=-0.15),
    # Centred by hand: its middle, so the glass's sockets are where the game draws the charge.
    "extinguisher": lambda: build("extinguisher", extinguisher, "a car's soda-acid extinguisher", 1400, centre=False,
                                  sockets={"glass_lo": tuple(GLASS_LO), "glass_hi": tuple(GLASS_HI)}),
    "extinguisher_mount": lambda: build("extinguisher_mount", extinguisher_mount, "an extinguisher's board and cradle", 900,
                                        centre=False),
    "repair_kit": lambda: build("repair_kit", repair_kit, "the engineer's repair kit", 1000),
    "shot_locker": lambda: build("shot_locker", shot_locker, "the gun car's powder and shot locker", 1800, centre=False, size=1024,
                                 sockets={**{f"ball_{k}": tuple(c) for k, c in enumerate(ball_slots())},
                                          **{f"bag_{k}": tuple(c) for k, c in enumerate(bag_slots())}}),
    "powder_bag": lambda: build("powder_bag", powder_bag, "a powder charge", 400, centre=False),
}
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
