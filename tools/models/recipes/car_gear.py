"""The cars' running gear and couplings (GDD §26, spec B.4), modelled, as the pieces TrainKit sets where it built boxes:

  * truck_archbar: a freight car's arch-bar truck, 1.6 m wheelbase on the 1.44 m gauge, 0.42 m wheels (TrainKit.Truck):
    plate wheels with their flanges, axles, journal boxes with hinged lids, the top, arch and tie bars bolted through
    columns, a nest of coil springs under the bolster, the brake beams with their heads and shoes on the treads;
  * coupler_knuckle, coupler_open: a knuckle coupler out from the end beam, 0.9 m up (TrainKit.Coupler), shut, and cut: the striker casting, the
    shank in its yoke, the head with its knuckle, pin and guard arm, the cut lever (the uncoupling rod, spec B.4's
    "cut the train") run out along the end beam to its handle, the air hose with its glad hand, and the angle cock;
  * brake_gear: what hangs under a car's middle: the air reservoir on its straps, the brake cylinder, the triple valve
    and the pipe between them, the levers and the push rods out towards the trucks.

Axes (Blender): +Y along the track, towards the engine (the engine's -Z), +X right, +Z up; the origin on the rail head
under the part's centre (a coupler's: at the end beam).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

GAUGE = 0.72
R = 0.42
PITCH = 0.8


def materials():
    return {
        "iron": make.lib("rust_heavy", 2.0, tint=(0.3, 0.27, 0.25), rough=0.55, metal=0.6),
        "cast": make.lib("rust_heavy", 3.0, tint=(0.22, 0.2, 0.19), rough=0.6, metal=0.5),
        "wheel": make.lib("wheel_iron", 2.0, tint=(0.42, 0.4, 0.38), rough=0.4, metal=0.8),
        "steel": make.lib("iron_plate", 3.0, tint=(0.6, 0.6, 0.58), rough=0.45, metal=0.7),
        "paint": make.lib("paint_black", 2.0, tint=(0.8, 0.8, 0.8), rough=0.6),
        "rubber": make.flat("hose_rubber", (0.03, 0.028, 0.026), rough=0.8),
        "brass": make.lib("brass", 4.0, rough=0.35, metal=0.9),
    }


def build(name, fn, what, size=1024, budget=3000):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.6, made=make.provenance("car_gear", what))


def beam(a, b, w, h, material, name, up=(0, 0, 1)):
    """A bar of section w x h from a to b (strap iron, a rod of square stock), and its game-mesh twin."""
    a, b = Vector(a), Vector(b)
    d = b - a
    rot = Vector((0, 0, 1)).rotation_difference(d).to_matrix().to_4x4()
    o = make.box((0, 0, 0), (w / 2, h / 2, d.length / 2), material, bevel=0.006, name=name, rot=rot)
    shift = Matrix.Translation((a + b) / 2)
    o.data.transform(shift)
    make.LOW[-1].data.transform(shift)
    return o


def bolt(at, normal, m, r=0.014):
    return make.nail(at, normal, m, r=r)


def coil(centre, r, wire, height, turns, m, name="spring"):
    """A coil spring, as stacked rings (a helix bakes the same at this size); its game mesh a plain cylinder."""
    c = Vector(centre)
    out = []
    for k in range(turns):
        z = c.z + wire + (height - 2 * wire) * k / max(1, turns - 1)
        out.append(make.torus((c.x, c.y, z), (0, 0, 1), r, wire, m, n=14, m=5, name=name, low=None))
    body = make.cyl((c.x, c.y, c.z), (c.x, c.y, c.z + height), r + wire, m, n=8, bevel=0, name=name + "_body", low=6)
    cook.delete([body])
    return out


# ----------------------------------------------------------------------------------------------------------------
# The truck

def wheel(m, x, y, side):
    p = []
    w = 0.13
    inner, outer = x - side * w / 2, x + side * w / 2
    # The tread and the flange on the inside, proud of it.
    p.append(make.cyl((inner, y, R), (outer, y, R), R, m["wheel"], n=40, bevel=0.006, name="tread", low=16))
    p.append(make.cyl((inner - side * 0.028, y, R), (inner + side * 0.004, y, R), R + 0.032, m["wheel"], n=40, bevel=0.008,
                      name="flange", low=16))
    # A dished plate, the hub standing out of it, the axle's end in the hub.
    p.append(make.cyl((outer, y, R), (outer + side * 0.03, y, R), 0.2, m["wheel"], n=24, bevel=0.01, name="plate", r1=0.11,
                      low=10))
    p.append(make.cyl((outer + side * 0.02, y, R), (outer + side * 0.07, y, R), 0.09, m["wheel"], n=20, bevel=0.008,
                      name="hub", low=8))
    return p


def journal_box(m, x, y, side):
    p = []
    p.append(make.box((x, y, R + 0.02), (0.09, 0.15, 0.13), m["cast"], bevel=0.015, name="journal"))
    # The hinged lid on its outer face, its lug and hinge pin; oil-streaked.
    p.append(make.box((x + side * 0.095, y, R + 0.0), (0.012, 0.11, 0.09), m["cast"], bevel=0.008, name="lid"))
    p.append(make.cyl((x + side * 0.105, y - 0.06, R + 0.1), (x + side * 0.105, y + 0.06, R + 0.1), 0.014, m["iron"], n=8,
                      name="hinge", low=0))
    p.append(make.box((x + side * 0.1, y, R - 0.1), (0.018, 0.02, 0.015), m["cast"], bevel=0.004, name="lug", low=False))
    for dy in (-0.12, 0.12):
        p.append(bolt((x + side * 0.09, y + dy, R + 0.1), (side, 0, 0), m["iron"]))
    return p


def truck_archbar(m):
    p = []
    for y in (-PITCH, PITCH):
        p.append(make.cyl((-GAUGE + 0.06, y, R), (GAUGE - 0.06, y, R), 0.07, m["steel"], n=16, bevel=0.004, name="axle"))
        for side in (-1, 1):
            p += wheel(m, side * GAUGE, y, side)
    for side in (-1, 1):
        x = side * (GAUGE + 0.14)
        for y in (-PITCH, PITCH):
            p += journal_box(m, x, y, side)
        # Top bar straight over the journal boxes; the arch bar bowed down between them; the tie bar under.
        p.append(beam((x, -PITCH - 0.24, 0.62), (x, PITCH + 0.24, 0.62), 0.1, 0.028, m["iron"], "top_bar", ))
        arch = [(-PITCH - 0.16, 0.3), (-PITCH + 0.2, 0.3), (-0.34, 0.2), (0.34, 0.2), (PITCH - 0.2, 0.3), (PITCH + 0.16, 0.3)]
        for (y0, z0), (y1, z1) in zip(arch, arch[1:]):
            p.append(beam((x, y0, z0), (x, y1, z1), 0.1, 0.026, m["iron"], "arch_bar"))
        p.append(beam((x, -PITCH + 0.12, 0.17), (x, PITCH - 0.12, 0.17), 0.09, 0.022, m["iron"], "tie_bar"))
        # The columns bolted through all three, and the spring plank between them.
        for y in (-0.34, 0.34):
            p.append(make.box((x, y, 0.4), (0.06, 0.035, 0.24), m["cast"], bevel=0.01, name="column"))
            for z in (0.18, 0.21, 0.62):
                p.append(bolt((x + side * 0.055, y, z), (side, 0, 0), m["iron"], r=0.016))
        for y in (-PITCH, PITCH):
            for z in (0.3, 0.62):
                p.append(bolt((x + side * 0.05, y - math.copysign(0.2, y) * -1, z), (side, 0, 0), m["iron"], r=0.016))
        p.append(make.box((x, 0, 0.225), (0.1, 0.3, 0.02), m["steel"], bevel=0.006, name="plank"))
        # The spring nest: four coils under the bolster's end.
        for dy in (-0.15, -0.05, 0.05, 0.15):
            p += coil((x, dy, 0.245), 0.042, 0.011, 0.3, 6, m["steel"])
    # The bolster across, its centre plate, and the side bearings.
    p.append(make.box((0, 0, 0.64), (GAUGE + 0.24, 0.17, 0.085), m["cast"], bevel=0.02, name="bolster"))
    p.append(make.cyl((0, 0, 0.72), (0, 0, 0.77), 0.2, m["cast"], n=24, bevel=0.008, name="centre_plate", low=10))
    for side in (-1, 1):
        p.append(make.box((side * 0.55, 0, 0.745), (0.07, 0.09, 0.025), m["cast"], bevel=0.006, name="side_bearing"))
    # The brake beams inboard of the wheels, their heads and shoes on the treads, and the hangers.
    for sy in (-1, 1):
        y = sy * (PITCH - R - 0.05)
        p.append(make.cyl((-GAUGE - 0.02, y, R - 0.02), (GAUGE + 0.02, y, R - 0.02), 0.028, m["iron"], n=10, name="brake_beam",
                          low=6))
        p.append(beam((-0.35, y, R - 0.02), (0, y - sy * 0.09, R + 0.06), 0.03, 0.03, m["iron"], "strut"))
        p.append(beam((0.35, y, R - 0.02), (0, y - sy * 0.09, R + 0.06), 0.03, 0.03, m["iron"], "strut"))
        for side in (-1, 1):
            x = side * GAUGE
            p.append(make.box((x, y + sy * 0.015, R - 0.02), (0.075, 0.03, 0.11), m["cast"], bevel=0.008, name="brake_head"))
            p.append(make.box((x, y + sy * 0.04, R - 0.02), (0.065, 0.012, 0.13), m["steel"], bevel=0.004, name="shoe"))
            p.append(beam((x - side * 0.08, y, R + 0.04), (x - side * 0.08, y + sy * 0.05, 0.64), 0.02, 0.02, m["iron"], "hanger"))
    return p


# ----------------------------------------------------------------------------------------------------------------
# The coupler

def coupler_knuckle(m, open_=False, knuckle=True, jaw=False):
    """`open_`: cut (T91, spec B.4): the cut lever lifted the lock, the knuckle swung wide open on its pin, the hose parted
    at its glad hand and hanging straight down. `knuckle` False: the head without its knuckle (the game swings that on its
    pin, note 402); `jaw`: the knuckle alone, shut, in the same frame (its pin at x 0.1, y 0.47, 0.9 up)."""
    p = []
    H = 0.9
    # The knuckle turns about its pin (x 0.1, y 0.47, vertical): -18 degrees shut, swung out 80 more open.
    swing = Matrix.Rotation(math.radians(80 if open_ else 0), 4, "Z")

    def turned(c):
        pin = Vector((0.1, 0.47, H))
        return tuple(pin + swing.to_3x3() @ (Vector(c) - pin))
    if jaw:
        p.append(make.box(turned((0.05, 0.54, H)), (0.08, 0.035, 0.12), m["cast"], bevel=0.015, name="knuckle",
                          rot=swing @ Matrix.Rotation(math.radians(-18), 4, "Z")))
        p.append(make.box(turned((0.12, 0.575, H)), (0.035, 0.03, 0.11), m["cast"], bevel=0.012, name="knuckle_nose", rot=swing))
        # The pin's boss turns with it about its own axis (it looks the same at any swing).
        p.append(make.cyl((0.1, 0.47, H - 0.13), (0.1, 0.47, H + 0.13), 0.05, m["cast"], n=14, bevel=0.008, name="pin_boss", low=8))
        return p
    # The striker casting on the end beam's face, the yoke behind it, the shank out through it.
    p.append(make.box((0, 0.03, H), (0.24, 0.03, 0.17), m["cast"], bevel=0.015, name="striker"))
    p.append(make.box((0, 0.2, H), (0.075, 0.17, 0.065), m["cast"], bevel=0.012, name="shank"))
    # The head: its body, the knuckle turned on its pin to the right, the guard arm on the left, the lock's lift.
    p.append(make.box((0, 0.42, H), (0.13, 0.07, 0.13), m["cast"], bevel=0.02, name="head"))
    if knuckle:
        p.append(make.cyl((0.1, 0.47, H - 0.13), (0.1, 0.47, H + 0.13), 0.05, m["cast"], n=14, bevel=0.008, name="pin_boss", low=8))
        p.append(make.box(turned((0.05, 0.54, H)), (0.08, 0.035, 0.12), m["cast"], bevel=0.015, name="knuckle",
                          rot=swing @ Matrix.Rotation(math.radians(-18), 4, "Z")))
        p.append(make.box(turned((0.12, 0.575, H)), (0.035, 0.03, 0.11), m["cast"], bevel=0.012, name="knuckle_nose", rot=swing))
    p.append(make.box((-0.11, 0.53, H), (0.03, 0.06, 0.12), m["cast"], bevel=0.012, name="guard_arm"))
    p.append(make.cyl((0.1, 0.47, H + 0.13), (0.1, 0.47, H + 0.16), 0.022, m["iron"], n=10, name="pin_head", low=0))
    lift = 0.07 if open_ else 0.0
    p.append(make.box((-0.02, 0.42, H + 0.15 + lift), (0.02, 0.02, 0.025), m["iron"], bevel=0.004, name="lock_lift", low=False))
    # The cut lever: from the lock lift up to the end beam's top, along it through two brackets to the handle at the side
    # (thrown up, cut: the handle swung up level).
    lever = [(-0.02, 0.42, H + 0.17 + lift), (-0.02, 0.2, H + 0.2 + lift * 0.5), (-0.1, 0.08, H + 0.21), (-1.2, 0.08, H + 0.21),
             (-1.3, 0.1, H + (0.2 if open_ else 0.08)), (-1.3, 0.17 if not open_ else 0.04, H + (0.3 if open_ else 0.06))]
    p += make.pipe(lever, 0.013, m["iron"], name="cut_lever", n=8, low=5)
    for x in (-0.5, -1.05):
        p.append(make.box((x, 0.04, H + 0.2), (0.015, 0.04, 0.035), m["iron"], bevel=0.004, name="bracket", low=False))
    # The air hose: from the angle cock under the end beam, hanging in a loop to its glad hand.
    p.append(make.cyl((0.35, 0.02, H - 0.08), (0.35, 0.14, H - 0.08), 0.03, m["brass"], n=12, name="angle_cock", low=6))
    p.append(make.box((0.35, 0.09, H - 0.02), (0.012, 0.012, 0.05), m["paint"], bevel=0.003, name="cock_handle", low=False))
    hose = ([(0.35, 0.14, H - 0.08), (0.36, 0.17, H - 0.25), (0.36, 0.18, H - 0.45), (0.36, 0.18, H - 0.6)] if open_ else
            [(0.35, 0.14, H - 0.08), (0.37, 0.2, H - 0.22), (0.38, 0.23, H - 0.4), (0.37, 0.27, H - 0.47), (0.33, 0.33, H - 0.44)])
    p += make.pipe(hose, 0.022, m["rubber"], name="hose", n=10, low=6)
    p.append(make.box((0.36, 0.18, H - 0.64) if open_ else (0.32, 0.36, H - 0.43), (0.035, 0.03, 0.022), m["brass"], bevel=0.006, name="glad_hand"))
    for (x, y, z) in [(0.2, 0.06, H + 0.12), (-0.2, 0.06, H + 0.12), (0.2, 0.06, H - 0.12), (-0.2, 0.06, H - 0.12)]:
        p.append(bolt((x, y, z), (0, 1, 0), m["iron"], r=0.018))
    return p


# ----------------------------------------------------------------------------------------------------------------
# The brake gear

def brake_gear(m):
    """In the car's own frame: the reservoir 0.45 m right of centre from 1.2 m behind the middle to 0.4 m ahead of it,
    0.8 m up; the cylinder 0.5 m left, 0.2-0.9 m ahead (TrainKit's boxes, turned to Blender's +Y forward)."""
    p = []
    # The reservoir: a drum with dished ends, two straps up to the underframe.
    p.append(make.cyl((0.45, -1.2 + 0.06, 0.8), (0.45, 0.4 - 0.06, 0.8), 0.2, m["steel"], n=28, bevel=0.01, name="reservoir",
                      low=12))
    for y, s in ((-1.2 + 0.06, -1), (0.4 - 0.06, 1)):
        p.append(make.cyl((0.45, y, 0.8), (0.45, y + s * 0.06, 0.8), 0.2, m["steel"], n=28, bevel=0.03, name="dish", r1=0.12,
                          low=12))
    for y in (-0.85, 0.05):
        p.append(make.torus((0.45, y, 0.8), (0, 1, 0), 0.205, 0.012, m["iron"], n=28, m=5, name="strap_band", low=None))
        for sx in (-1, 1):
            p.append(beam((0.45 + sx * 0.19, y, 0.8), (0.45 + sx * 0.19, y, 0.93), 0.04, 0.012, m["iron"], "strap"))
    # The cylinder: its body, the pressure head, the piston rod out to the live lever.
    p.append(make.cyl((-0.5, 0.2, 0.82), (-0.5, 0.9, 0.82), 0.15, m["cast"], n=24, bevel=0.012, name="cylinder", low=10))
    p.append(make.cyl((-0.5, 0.18, 0.82), (-0.5, 0.22, 0.82), 0.175, m["cast"], n=24, bevel=0.01, name="flange", low=10))
    for k in range(8):
        a = k * math.pi / 4
        p.append(bolt((-0.5 + math.cos(a) * 0.16, 0.175, 0.82 + math.sin(a) * 0.16), (0, -1, 0), m["iron"], r=0.013))
    p.append(make.cyl((-0.5, 0.9, 0.82), (-0.5, 1.25, 0.82), 0.025, m["steel"], n=10, name="piston_rod", low=6))
    # The triple valve on the reservoir's end, and the pipe to the cylinder.
    p.append(make.box((0.2, -1.1, 0.85), (0.08, 0.1, 0.09), m["cast"], bevel=0.015, name="triple_valve"))
    p += make.pipe([(0.2, -1.0, 0.86), (0.2, -0.3, 0.9), (-0.35, -0.2, 0.9), (-0.45, 0.2, 0.86)], 0.018, m["iron"], name="pipe",
                   n=8, low=5)
    # The levers, and the push rods out towards both trucks (they meet the trucks' brake beams).
    p.append(beam((-0.5, 1.25, 0.68), (-0.5, 1.25, 0.96), 0.05, 0.02, m["iron"], "live_lever", up=(1, 0, 0)))
    p.append(beam((0.2, -1.3, 0.68), (0.2, -1.3, 0.96), 0.05, 0.02, m["iron"], "dead_lever", up=(1, 0, 0)))
    p.append(beam((-0.5, 1.25, 0.72), (0.2, -1.3, 0.72), 0.03, 0.03, m["iron"], "tie_rod"))
    p.append(beam((-0.5, 1.27, 0.94), (-0.1, 3.4, 0.62), 0.028, 0.028, m["iron"], "push_rod"))
    p.append(beam((0.2, -1.32, 0.94), (0.1, -3.4, 0.62), 0.028, 0.028, m["iron"], "push_rod"))
    return p


build("truck_archbar", truck_archbar, "a freight car's arch-bar truck", budget=4000)
build("coupler_knuckle", coupler_knuckle, "a knuckle coupler, its cut lever and air hose", budget=1500)
build("coupler_open", lambda m: coupler_knuckle(m, open_=True), "a cut knuckle coupler: the knuckle open, the hose parted", budget=1500)
# The knuckle on its own and the heads without it (note 402): the game swings the knuckle open on its pin as it's cut.
build("coupler_head_shut", lambda m: coupler_knuckle(m, knuckle=False), "a knuckle coupler's head without its knuckle", budget=1400)
build("coupler_head_open", lambda m: coupler_knuckle(m, open_=True, knuckle=False), "a cut coupler's head without its knuckle", budget=1400)
build("coupler_jaw", lambda m: coupler_knuckle(m, jaw=True), "a knuckle coupler's knuckle, shut, on its pin", size=512, budget=300)
build("brake_gear", brake_gear, "a car's brake gear", budget=1500)
