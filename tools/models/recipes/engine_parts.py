"""The engine's running gear and boiler fittings (GDD §26, spec B.4: an armoured 2-8-0), modelled, as the pieces
TrainKit.Engine sets where it built boxes and lathes. Sizes are TrainKit's and content/tuning/train.json's (the boiler
casing's half width 0.95, deck 1.4, top 3.6):

  * driver_wheel: a 1.4 m spoked driver: tyre and flange, twelve spokes into the hub, the counterweight, the crank boss
    and its pin at 0.3 m (towards the engine's +Z in the model; TrainKit turns it to the side's phase);
  * pilot_wheel: the pilot truck's 0.8 m spoked wheel;
  * coupling_rod: the fluted rod over the four crank pins, 1.6 m apart, with bushes and oil cups;
  * cylinder_r, cylinder_l: a steam cylinder with its steam chest on top, cover studs, drain cocks, and the crosshead
    guides back to the crosshead (the right side's guides inboard of it, the left's mirrored);
  * smokebox_door: the dished door on the armour face, its hinge straps, the dart and its handles, the clamps;
  * headlamp_box: the armoured lamp's box and hood, a cage over the glass (TrainKit's lens stays the light);
  * steam_dome, sand_dome: the domes, with their bolted base rings and lids;
  * casing_strap: the strap round the octagonal boiler casing where its plates meet, riveted.

Axes (Blender): +Y towards the engine's front (the engine's -Z), +X right, +Z up. Origins as TrainKit places them: a
wheel's at its centre, a rod's at its middle pin pair, a cylinder's on its axis at its middle, the door's at its centre
on the armour face, the lamp box's at the lens, a dome's at its foot, the strap's at its middle.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

BW, DECK, TOP = 0.95, 1.4, 3.6


def materials():
    return {
        "iron": make.lib("wheel_iron", 2.0, tint=(0.42, 0.4, 0.38), rough=0.4, metal=0.8),
        "black": make.lib("paint_black", 1.5, tint=(0.75, 0.75, 0.75), rough=0.5),
        "rod": make.lib("iron_plate", 3.0, tint=(0.75, 0.75, 0.73), rough=0.3, metal=0.9),
        "plate": make.lib("iron_plate", 1.5, tint=(0.6, 0.6, 0.6), rough=0.5, metal=0.6),
        "rust": make.lib("rust_heavy", 2.0, tint=(0.35, 0.3, 0.28), rough=0.6, metal=0.5),
        "smoke": make.lib("iron_smokebox", 1.5, tint=(0.8, 0.8, 0.8), rough=0.6),
        "brass": make.lib("brass", 4.0, rough=0.3, metal=0.9),
    }


def build(name, fn, what, size=1024, budget=2000):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.55, made=make.provenance("engine_parts", what))


def beam(a, b, w, h, material, name, up=Vector((0, 0, 1))):
    """A bar of w x h section from a to b, its h side towards `up`."""
    a, b = Vector(a), Vector(b)
    d = (b - a)
    z = d.normalized()
    x = Vector(up).cross(z)
    if x.length < 1e-6:
        x = Vector((1, 0, 0))
    x.normalize()
    y = z.cross(x)
    rot = Matrix((x, y, z)).transposed().to_4x4()
    o = make.box((0, 0, 0), (w / 2, h / 2, d.length / 2), material, bevel=0.006, name=name, rot=rot)
    shift = Matrix.Translation((a + b) / 2)
    o.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    return o


def bolt(at, normal, m, r=0.014):
    return make.nail(at, normal, m, r=r)


# ----------------------------------------------------------------------------------------------------------------
# Wheels (on the X axis, outer face +X; a crank pin towards the engine's +Z, Blender -Y)

def spoked(m, r, spokes, w=0.13, driver=False):
    p = []
    inner, outer = -w / 2, w / 2
    p.append(make.cyl((inner, 0, 0), (outer, 0, 0), r, m["iron"], n=48, bevel=0.008, name="tyre", low=24))
    p.append(make.cyl((inner - 0.03, 0, 0), (inner + 0.004, 0, 0), r + 0.035, m["iron"], n=48, bevel=0.01, name="flange",
                      low=24))
    # The rim inside the tyre, the spokes, the hub.
    p.append(make.torus((outer - 0.03, 0, 0), (1, 0, 0), r * 0.86, r * 0.05, m["iron"], n=40, m=8, name="rim", low=(20, 4)))
    for k in range(spokes):
        a = k * 2 * math.pi / spokes + 0.2
        d = Vector((0, math.sin(a), math.cos(a)))
        s0 = Vector((outer - 0.03, 0, 0)) + d * r * 0.2
        s1 = Vector((outer - 0.03, 0, 0)) + d * r * 0.84
        p.append(make.cyl(s0, s1, 0.03 if driver else 0.022, m["iron"], n=10, bevel=0.004, name="spoke", r1=0.022 if driver else 0.018,
                          low=5))
    p.append(make.cyl((outer - 0.05, 0, 0), (outer + 0.03, 0, 0), r * 0.22, m["iron"], n=20, bevel=0.01, name="hub", low=10))
    p.append(make.cyl((outer + 0.02, 0, 0), (outer + 0.05, 0, 0), 0.07, m["iron"], n=16, bevel=0.006, name="axle_end", low=8))
    if driver:
        # The counterweight opposite the pin (towards +Y), a heavy block between the spokes, bolted.
        p.append(make.box((outer - 0.03, r * 0.55, 0), (0.028, r * 0.2, r * 0.42), m["iron"], bevel=0.02, name="counterweight"))
        for dz in (-0.18, 0.0, 0.18):
            p.append(bolt((outer - 0.001, r * 0.6, dz), (1, 0, 0), m["rust"], r=0.016))
        # The crank boss and pin at 0.3 m, towards -Y (the engine's +Z).
        p.append(make.cyl((outer - 0.02, -0.3, 0), (outer + 0.07, -0.3, 0), 0.1, m["iron"], n=20, bevel=0.01, name="boss", low=10))
        p.append(make.cyl((outer + 0.07, -0.3, 0), (outer + 0.16, -0.3, 0), 0.055, m["rod"], n=16, bevel=0.006, name="pin", low=8))
        p.append(make.cyl((outer + 0.16, -0.3, 0), (outer + 0.18, -0.3, 0), 0.07, m["brass"], n=16, bevel=0.004, name="collar", low=8))
    return p


def driver_wheel(m):
    return spoked(m, 0.7, 12, driver=True)


def pilot_wheel(m):
    return spoked(m, 0.4, 8)


def coupling_rod(m):
    p = []
    pins = [-2.4, -0.8, 0.8, 2.4]
    # The fluted body between the bosses: an I-section (top and bottom flanges, the web) either side of each boss.
    for a, b in zip(pins, pins[1:]):
        y0, y1 = a + 0.11, b - 0.11
        for dz in (-0.05, 0.05):
            p.append(beam((0, y0, dz), (0, y1, dz), 0.06, 0.02, m["rod"], "flange"))
        p.append(beam((0, y0, 0), (0, y1, 0), 0.025, 0.1, m["rod"], "web"))
    for y in pins:
        p.append(make.cyl((-0.035, y, 0), (0.035, y, 0), 0.1, m["rod"], n=24, bevel=0.01, name="boss", low=12))
        p.append(make.cyl((0.035, y, 0), (0.05, y, 0), 0.065, m["brass"], n=20, bevel=0.004, name="bush", low=10))
        # The oil cup on top of each boss.
        p.append(make.cyl((0, y, 0.1), (0, y, 0.15), 0.025, m["brass"], n=12, bevel=0.004, name="oil_cup", low=6))
    return p


def cylinder(m, side):
    """The cylinder on its axis at the origin, 1.1 m long; the guides run back 0.55-1.75 m behind its middle, 0.14 m
    inboard (towards the frames)."""
    p = []
    L, R = 1.1, 0.34
    p.append(make.cyl((0, L / 2, 0), (0, -L / 2, 0), R, m["black"], n=40, bevel=0.012, name="barrel", low=16))
    for y, s in ((L / 2, 1), (-L / 2, -1)):
        p.append(make.cyl((0, y, 0), (0, y + s * 0.05, 0), R + 0.03, m["iron"], n=40, bevel=0.012, name="cover", low=16))
        p.append(make.cyl((0, y + s * 0.05, 0), (0, y + s * 0.09, 0), R * 0.6, m["iron"], n=32, bevel=0.015, name="boss",
                          r1=R * 0.45, low=12))
        for k in range(12):
            a = k * math.pi / 6
            p.append(bolt((math.cos(a) * (R + 0.012), y + s * 0.052, math.sin(a) * (R + 0.012)), (0, s, 0), m["rust"], r=0.014))
    # The steam chest above it, the steam pipe up into the smokebox.
    p.append(make.box((0, 0, R + 0.16), (0.2, L / 2 - 0.05, 0.14), m["black"], bevel=0.02, name="chest"))
    p.append(make.box((0, 0, R + 0.31), (0.22, L / 2 - 0.02, 0.018), m["iron"], bevel=0.006, name="chest_lid"))
    p.append(make.cyl((-side * 0.1, 0.2, R + 0.3), (-side * 0.45, 0.25, R + 0.9), 0.08, m["black"], n=16, name="steam_pipe", low=8))
    # Drain cocks underneath, and their levers.
    for y in (-L / 2 + 0.12, L / 2 - 0.12):
        p.append(make.cyl((0, y, -R), (0, y, -R - 0.12), 0.022, m["brass"], n=10, name="cock", low=6))
        p.append(make.cyl((0, y, -R - 0.12), (0, y - 0.08, -R - 0.2), 0.012, m["brass"], n=8, name="cock_pipe", low=0))
    # The crosshead guides back from the rear cover, and the crosshead on them.
    x = -side * 0.14
    for dz in (-0.07, 0.07):
        p.append(beam((x, -L / 2 - 0.04, 0.02 + dz - 0.04 + 0.04), (x, -1.75, 0.02 + dz), 0.06, 0.03, m["rod"], "guide"))
    p.append(make.box((x, -1.35, -0.03), (0.05, 0.12, 0.05), m["iron"], bevel=0.012, name="crosshead"))
    p.append(make.box((x, -1.75, 0.02), (0.07, 0.03, 0.12), m["black"], bevel=0.01, name="yoke"))
    return p


def cylinder_r(m):
    return cylinder(m, 1)


def cylinder_l(m):
    return cylinder(m, -1)


# ----------------------------------------------------------------------------------------------------------------
# The front

def smokebox_door(m):
    p = []
    # The door proud of the armour face (it faces +Y): a ring, the dished door in it.
    p.append(make.torus((0, 0.03, 0), (0, 1, 0), 0.5, 0.035, m["smoke"], n=48, m=8, name="ring", low=(24, 4)))
    p.append(make.cyl((0, 0.0, 0), (0, 0.08, 0), 0.49, m["smoke"], n=48, bevel=0.03, name="door", r1=0.42, low=20))
    # Two hinge straps across to the left, their hinge knuckles on the ring.
    for z in (-0.25, 0.25):
        p.append(beam((0.05, 0.085, z), (-0.56, 0.05, z), 0.03, 0.09, m["iron"], "hinge_strap", up=Vector((0, 1, 0))))
        p.append(make.cyl((-0.56, 0.05, z - 0.06), (-0.56, 0.05, z + 0.06), 0.03, m["iron"], n=12, name="knuckle", low=6))
        for x in (-0.1, -0.3):
            p.append(bolt((x, 0.1, z), (0, 1, 0), m["rust"]))
    # The dart: its spindle out of the middle, the two handles on it.
    p.append(make.cyl((0, 0.08, 0), (0, 0.2, 0), 0.035, m["brass"], n=14, name="spindle", low=6))
    p.append(beam((-0.18, 0.21, 0), (0.18, 0.21, 0), 0.03, 0.03, m["brass"], "handle"))
    p.append(beam((0, 0.17, -0.14), (0, 0.17, 0.14), 0.025, 0.025, m["brass"], "handle"))
    # Clamps round the rim.
    for k in range(8):
        a = k * math.pi / 4 + math.pi / 8
        c = Vector((math.cos(a) * 0.5, 0.06, math.sin(a) * 0.5))
        p.append(make.box(tuple(c), (0.03, 0.035, 0.03), m["iron"], bevel=0.006, name="clamp"))
    # Its number plate below the dart.
    p.append(make.box((0, 0.09, -0.3), (0.15, 0.01, 0.055), m["brass"], bevel=0.006, name="plate"))
    return p


def headlamp_box(m):
    """The lens at the origin facing +Y; the box runs back 1.0 m (TrainKit stretches it to the boiler front)."""
    p = []
    D = 1.0
    p.append(make.box((0, -D / 2, 0), (0.42, D / 2, 0.38), m["black"], bevel=0.02, name="box"))
    # The hood over the front, its cheeks down each side; the shelf it sits on, on brackets.
    p.append(make.box((0, 0.12, 0.41), (0.5, 0.2, 0.025), m["plate"], bevel=0.01, name="hood"))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.47, 0.1, 0.1), (0.02, 0.18, 0.32), m["plate"], bevel=0.008, name="cheek"))
    p.append(make.box((0, -D / 2 + 0.1, -0.41), (0.46, D / 2 + 0.1, 0.025), m["plate"], bevel=0.01, name="shelf"))
    # The cage over the lens: three bars down, two across, riveted into the frame round it.
    p.append(make.torus((0, 0.03, 0), (0, 1, 0), 0.36, 0.022, m["iron"], n=32, m=6, name="bezel", low=(16, 4)))
    for x in (-0.18, 0, 0.18):
        p.append(make.cyl((x, 0.06, -0.36), (x, 0.06, 0.36), 0.013, m["iron"], n=8, name="bar", low=0))
    for z in (-0.15, 0.15):
        p.append(make.cyl((-0.36, 0.065, z), (0.36, 0.065, z), 0.011, m["iron"], n=8, name="bar", low=0))
    for x in (-0.38, 0.38):
        for z in (-0.3, 0, 0.3):
            p.append(bolt((x, 0.003, z), (0, 1, 0), m["rust"], r=0.012))
    return p


def dome(m, r, h, lid):
    p = []
    prof = [(r, 0), (r * 0.92, h * 0.3), (r * 0.75, h * 0.7), (r * 0.45, h * 0.95), (0, h)]
    # A lathed dome as stacked frusta (the bake smooths the steps), on a bolted base ring.
    for (r0, z0), (r1, z1) in zip(prof, prof[1:]):
        p.append(make.cyl((0, 0, z0), (0, 0, z1), r0, m["plate"], n=40, bevel=0, name="shell", r1=max(r1, 0.01), low=12))
    p.append(make.cyl((0, 0, -0.03), (0, 0, 0.05), r + 0.05, m["plate"], n=40, bevel=0.01, name="base", low=12))
    for k in range(16):
        a = k * math.pi / 8
        p.append(bolt((math.cos(a) * (r + 0.03), math.sin(a) * (r + 0.03), 0.05), (0, 0, 1), m["rust"], r=0.012))
    if lid:
        p.append(make.cyl((0, 0, h - 0.01), (0, 0, h + 0.04), r * 0.35, m["iron"], n=24, bevel=0.01, name="lid", low=8))
        for k in range(8):
            a = k * math.pi / 4
            p.append(bolt((math.cos(a) * r * 0.3, math.sin(a) * r * 0.3, h + 0.04), (0, 0, 1), m["rust"], r=0.01))
    return p


def steam_dome(m):
    return dome(m, 0.5, 0.42, True)


def sand_dome(m):
    return dome(m, 0.4, 0.3, False)


def casing_strap(m):
    """TrainKit's strap round the casing: Octagon(-BW - 0.025, DECK + 0.02, BW + 0.025, TOP + 0.025, 0.46, 0.18), 0.14 m
    along the boiler, rivets down both edges."""
    p = []
    x0, y0, x1, y1, tb, bb = -BW - 0.025, DECK + 0.02, BW + 0.025, TOP + 0.025, 0.46, 0.18
    octa = [(x0 + bb, y0), (x1 - bb, y0), (x1, y0 + bb), (x1, y1 - tb), (x1 - tb, y1), (x0 + tb, y1), (x0, y1 - tb),
            (x0, y0 + bb)]
    for (ax, az), (bx, bz) in zip(octa, octa[1:] + octa[:1]):
        a, b = Vector((ax, 0, az)), Vector((bx, 0, bz))
        mid = (a + b) / 2
        out = Vector((mid.x, 0, mid.z - (DECK + TOP) / 2)).normalized()
        if az == y0 and bz == y0:
            continue  # (the underside, hidden by the running boards)
        p.append(beam(a, b, 0.14, 0.02, m["rust"], "strap", up=out))
        n = max(2, int((b - a).length / 0.18))
        for k in range(1, n):
            q = a + (b - a) * (k / n)
            for dy in (-0.045, 0.045):
                p.append(bolt(tuple(q + out * 0.012 + Vector((0, dy, 0))), tuple(out), m["iron"], r=0.011))
    return p


build("driver_wheel", driver_wheel, "the engine's driving wheel", budget=1600)
build("pilot_wheel", pilot_wheel, "the engine's pilot wheel", budget=700)
build("coupling_rod", coupling_rod, "the engine's coupling rod", budget=700)
build("cylinder_r", cylinder_r, "the engine's right cylinder and guides", budget=1800)
build("cylinder_l", cylinder_l, "the engine's left cylinder and guides", budget=1800)
build("smokebox_door", smokebox_door, "the engine's smokebox door", budget=1400)
build("headlamp_box", headlamp_box, "the engine's armoured headlamp box", budget=900)
build("steam_dome", steam_dome, "the engine's steam dome", budget=900)
build("sand_dome", sand_dome, "the engine's sand dome", budget=700)
build("casing_strap", casing_strap, "a strap round the boiler casing", budget=500)
