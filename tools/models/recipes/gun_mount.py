"""The gun car's gun (GDD §26, spec B.5), modelled: a water-cooled heavy machine gun on a pedestal, the gunner's view of
it the one they have all night. To TrainKit.Gun's frame: the pivot at the origin, the barrel out along the engine's -Z,
its muzzle 1.5 m out (SceneArt puts the flash there), the grips behind at 0.4 m, the shield at 0.45 m.

  * the pedestal: a column on a bolted base flange, the traversing ring and its clamp handle;
  * the cradle: a yoke up to the trunnion pins;
  * the receiver: riveted side plates, the top cover and its catch, the crank handle, the rear crossbar and the spade
    grips with their trigger;
  * the water jacket: corrugated, a filler cap on top and a drain plug under, the steam tube's union; the barrel's end
    and the muzzle booster;
  * the feed block on the right, the belt of rounds from it into the ammunition box on its bracket;
  * the shield: a plate either side of the barrel and over and under the aiming slot, raked forward, rimmed, riveted,
    dented, bolted to the cradle.

Axes (Blender): +Y along the barrel (the engine's -Z), +X right, +Z up.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402


def materials():
    return {
        "olive": make.lib("paint_olive", 3.0, tint=(0.55, 0.62, 0.48), rough=0.55, metal=0.3),
        "steel": make.lib("iron_plate", 4.0, tint=(0.55, 0.55, 0.55), rough=0.35, metal=0.9),
        "black": make.lib("wheel_iron", 3.0, tint=(0.4, 0.4, 0.4), rough=0.4, metal=0.8),
        "plate": make.lib("iron_plate", 2.0, tint=(0.62, 0.62, 0.6), rough=0.5, metal=0.6),
        "rust": make.lib("rust_heavy", 3.0, tint=(0.4, 0.34, 0.3), rough=0.6, metal=0.5),
        "wood": make.lib("wood_grey", 6.0, tint=(0.62, 0.48, 0.36), rough=0.5),
        "brass": make.lib("brass", 6.0, rough=0.3, metal=0.9),
    }


def bolt(at, normal, m, r=0.01):
    return make.nail(at, normal, m, r=r)


def gun_mount(m):
    p = []
    # The pedestal.
    p.append(make.cyl((0, 0, -0.9), (0, 0, -0.86), 0.3, m["olive"], n=32, bevel=0.01, name="flange", low=12))
    for k in range(8):
        a = k * math.pi / 4
        p.append(bolt((math.cos(a) * 0.25, math.sin(a) * 0.25, -0.86), (0, 0, 1), m["rust"], r=0.016))
    p.append(make.cyl((0, 0, -0.86), (0, 0, -0.42), 0.16, m["olive"], n=28, bevel=0.01, name="column", low=10))
    p.append(make.cyl((0, 0, -0.42), (0, 0, -0.34), 0.26, m["olive"], n=32, bevel=0.012, name="ring", r1=0.2, low=12))
    p.append(make.torus((0, 0, -0.4), (0, 0, 1), 0.25, 0.018, m["black"], n=32, m=6, name="race", low=(16, 4)))
    p.append(make.cyl((0.24, 0, -0.38), (0.36, 0, -0.38), 0.018, m["black"], n=8, name="clamp", low=5))
    p.append(make.cyl((0.36, 0, -0.38), (0.36, 0, -0.3), 0.024, m["wood"], n=10, name="clamp_knob", low=6))
    # The cradle: a yoke up either side to the trunnions.
    for sx in (-1, 1):
        p.append(make.box((sx * 0.15, 0, -0.22), (0.02, 0.1, 0.13), m["olive"], bevel=0.008, name="yoke"))
        p.append(make.cyl((sx * 0.13, 0, -0.05), (sx * 0.18, 0, -0.05), 0.04, m["black"], n=16, name="trunnion", low=8))
    p.append(make.box((0, 0, -0.34), (0.17, 0.12, 0.02), m["olive"], bevel=0.006, name="saddle"))
    # The receiver: side plates, the top cover, riveted.
    p.append(make.box((0, -0.02, -0.04), (0.1, 0.32, 0.12), m["black"], bevel=0.01, name="receiver"))
    p.append(make.box((0, -0.02, 0.09), (0.095, 0.3, 0.012), m["black"], bevel=0.005, name="cover"))
    p.append(make.cyl((-0.09, 0.27, 0.09), (0.09, 0.27, 0.09), 0.012, m["steel"], n=8, name="cover_hinge", low=0))
    p.append(make.box((0, -0.3, 0.1), (0.03, 0.02, 0.015), m["steel"], bevel=0.004, name="catch"))
    for sx in (-1, 1):
        for y in (-0.28, -0.1, 0.08, 0.24):
            for z in (-0.13, 0.05):
                p.append(bolt((sx * 0.101, y, z), (sx, 0, 0), m["steel"], r=0.008))
    # The crank on the right, its handle folded back; the fusee box on the left.
    p.append(make.cyl((0.1, -0.05, -0.02), (0.13, -0.05, -0.02), 0.03, m["steel"], n=12, name="crank_hub", low=6))
    p.append(make.box((0.14, -0.12, -0.02), (0.012, 0.08, 0.015), m["steel"], bevel=0.004, name="crank"))
    p.append(make.box((-0.12, 0.05, -0.04), (0.025, 0.1, 0.06), m["black"], bevel=0.008, name="fusee"))
    # The rear crossbar, the spade grips, the trigger between them.
    p.append(make.box((0, -0.36, -0.04), (0.14, 0.03, 0.03), m["black"], bevel=0.008, name="crossbar"))
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.12, -0.38, -0.14), (sx * 0.12, -0.44, 0.06), 0.022, m["wood"], n=12, bevel=0.004, name="grip",
                          low=6))
        p.append(make.cyl((sx * 0.12, -0.37, -0.15), (sx * 0.12, -0.39, -0.1), 0.028, m["steel"], n=12, name="ferrule", low=6))
    p.append(make.box((0, -0.4, -0.04), (0.045, 0.012, 0.02), m["brass"], bevel=0.004, name="trigger"))
    # The water jacket: its corrugations, the filler and drain, the steam union.
    p.append(make.cyl((0, 0.3, -0.02), (0, 1.0, -0.02), 0.095, m["olive"], n=32, bevel=0.01, name="jacket", low=12))
    for k in range(14):
        y = 0.35 + k * 0.045
        p.append(make.torus((0, y, -0.02), (0, 1, 0), 0.097, 0.007, m["olive"], n=32, m=5, name="flute", low=None))
    for y in (0.31, 0.99):
        p.append(make.cyl((0, y - 0.015, -0.02), (0, y + 0.015, -0.02), 0.11, m["black"], n=32, bevel=0.005, name="band", low=12))
    p.append(make.cyl((0, 0.45, 0.07), (0, 0.45, 0.11), 0.028, m["brass"], n=14, bevel=0.004, name="filler", low=6))
    p.append(make.cyl((0, 0.85, -0.11), (0, 0.85, -0.14), 0.022, m["brass"], n=12, name="drain", low=6))
    p.append(make.cyl((0.09, 0.95, -0.02), (0.13, 0.95, -0.02), 0.018, m["brass"], n=10, name="union", low=6))
    # The barrel's end, the muzzle booster, the front sight.
    p.append(make.cyl((0, 1.0, -0.02), (0, 1.36, -0.02), 0.035, m["steel"], n=16, name="barrel", low=8))
    p.append(make.cyl((0, 1.36, -0.02), (0, 1.5, -0.02), 0.05, m["black"], n=20, bevel=0.006, name="booster", low=8))
    p.append(make.cyl((0, 1.49, -0.02), (0, 1.51, -0.02), 0.025, m["black"], n=16, name="muzzle", low=0))
    p.append(make.box((0, 0.97, 0.1), (0.008, 0.012, 0.03), m["steel"], bevel=0.002, name="sight", low=False))
    p.append(make.box((0, -0.25, 0.13), (0.03, 0.015, 0.03), m["steel"], bevel=0.003, name="rear_sight", low=False))
    # The feed block on the right, the belt out of the box and up into it.
    p.append(make.box((0.13, 0.08, 0.0), (0.035, 0.07, 0.05), m["black"], bevel=0.006, name="feed_block"))
    box_c = Vector((0.25, 0.05, -0.12))
    p.append(make.box(tuple(box_c), (0.11, 0.15, 0.1), m["olive"], bevel=0.008, name="ammo_box"))
    p.append(make.box((0.25, 0.05, -0.015), (0.115, 0.155, 0.01), m["olive"], bevel=0.004, name="lid"))
    p.append(make.box((0.25, -0.1, -0.13), (0.03, 0.01, 0.02), m["steel"], bevel=0.003, name="box_catch"))
    p.append(make.box((0.18, 0.05, -0.24), (0.04, 0.12, 0.012), m["olive"], bevel=0.004, name="bracket"))
    # (Rounds: the belt drops from the block, curls over the box's edge and down into it.)
    for k in range(10):
        t = k / 9
        at = Vector((0.16 + 0.07 * t, 0.08 - 0.02 * t, 0.0 - 0.06 * t * t))
        p.append(make.cyl(at + Vector((0, -0.03, 0)), at + Vector((0, 0.03, 0)), 0.008, m["brass"], n=8, name="round",
                          r1=0.006, low=0))
        p.append(make.box(tuple(at + Vector((0, -0.035, 0))), (0.012, 0.004, 0.014), m["rust"], bevel=0.002, name="link",
                          low=False))
    # The shield: raked forward, a plate either side of the barrel, over and under the slot; a rim, rivets, dents.
    rake = Matrix.Translation((0, 0.45, 0.02)) @ Matrix.Rotation(-0.15, 4, "X")
    for (x0, x1, z0, z1) in ((-0.48, -0.08, -0.35, 0.32), (0.08, 0.48, -0.35, 0.32), (-0.08, 0.08, 0.08, 0.32),
                             (-0.08, 0.08, -0.35, -0.08)):
        o = make.box(((x0 + x1) / 2, 0, (z0 + z1) / 2), ((x1 - x0) / 2, 0.012, (z1 - z0) / 2), m["plate"], bevel=0.006,
                     name="shield")
        o.data.transform(rake)
        make.LOW[-1].data.transform(rake)
        p.append(o)
    for (a, b) in (((-0.48, 0.32), (0.48, 0.32)), ((-0.48, -0.35), (0.48, -0.35)), ((-0.48, -0.35), (-0.48, 0.32)),
                   ((0.48, -0.35), (0.48, 0.32))):
        mid = ((a[0] + b[0]) / 2, 0, (a[1] + b[1]) / 2)
        half = (max(abs(b[0] - a[0]) / 2, 0.012), 0.016, max(abs(b[1] - a[1]) / 2, 0.012))
        o = make.box(mid, half, m["plate"], bevel=0.005, name="rim", low=False)
        o.data.transform(rake)
        p.append(o)
    for x in (-0.42, -0.25, 0.25, 0.42):
        for z in (-0.28, 0.0, 0.26):
            q = rake @ Vector((x, 0.013, z))
            n = (rake.to_3x3() @ Vector((0, -1, 0))).normalized()
            p.append(bolt(tuple(q - n * 0.026), tuple(n), m["steel"], r=0.012))
    # A couple of dents where it's been hit: pressed-in bosses on its face.
    for (x, z) in ((-0.3, 0.12), (0.34, -0.2), (0.18, 0.2)):
        q = rake @ Vector((x, -0.004, z))
        p.append(make.cyl(tuple(q), tuple(q + Vector((0, 0.01, 0))), 0.035, m["plate"], n=12, bevel=0.01, name="dent",
                          r1=0.02, low=0))
    # The shield's brackets back to the cradle.
    for sx in (-1, 1):
        p.append(make.box((sx * 0.14, 0.3, -0.18), (0.012, 0.14, 0.02), m["olive"], bevel=0.004, name="shield_bracket"))
    return p


cook.reset()
make.LOW.clear()
make.USED.clear()
make._mats.clear()
parts = gun_mount(materials())
low = cook.bake_down(parts, "gun_mount_low", 3500, colour=None, size=1024, cage=0.004, reach=0.012, low=list(make.LOW))[0]
cook.finish("gun_mount", [low], budget=3500, grime=0.5, made=make.provenance("gun_mount", "the gun car's machine gun"))
