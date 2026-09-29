"""The driver's controls that move (T29: a headset player takes hold of them; CabLevers gives where the handles
are). Each is its own prop, its origin where the sim puts its handle at rest, so SceneArt only has to move it:

  * lever_regulator: the throttle's handle, a forged grip and latch riding the rack on the backhead
    (cab_backhead), slid straight back as it opens, exactly as far as the sim's handle goes;
  * lever_brake and brake_stand: the brake valve on its pedestal by the cab side, and its handle, 0.25 m from the
    pivot, swung back as the brake goes on;
  * lever_reverser and reverser_quadrant: the tall reverser from the floor, 0.9 m to its grip, and the notched
    quadrant it rides in, thrown forward for ahead;
  * vent_valve: the blow-off valve on the cab wall, its lever and its pipe up through the roof.

Modelled here and baked (tools/models/make). Axes: Blender +Z up, -Y toward the cab's back (the engine's +Z).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

LEVER_BRAKE, LEVER_REVERSER = 0.25, 0.9  # pivot to handle (SceneArt.CabControls swings them about these)


def materials():
    return {
        "iron": make.lib("rust_heavy", 3.0, tint=(0.5, 0.46, 0.43), rough=0.45, metal=0.7),
        "steel": make.lib("iron_plate", 5.0, tint=(0.75, 0.74, 0.72), rough=0.35, metal=0.8),
        "brass": make.lib("brass", 4.0, rough=0.3, metal=0.9),
        "wood": make.lib("wood_sleeper", 6.0, tint=(0.62, 0.45, 0.3), rough=0.55),
        "paint": make.lib("paint_black", 3.0, rough=0.5),
    }


def build(name, fn, what, budget=900):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    m = materials()
    parts = fn(m)
    low = cook.bake_down(parts, name + "_low", 600, colour=None, size=512, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.45, made=make.provenance("cab_levers", what))


def regulator(m):
    # The grip stands up from the rack (6 cm below the handle point); the latch lever behind it that frees it.
    p = [make.cyl((0, 0, -0.06), (0, 0, 0.02), 0.014, m["iron"], n=10, name="shank")]
    p.append(make.box((0, 0, -0.07), (0.03, 0.04, 0.018), m["iron"], bevel=0.005, name="shoe"))
    p.append(make.cyl((0, 0, 0.0), (0, 0, 0.1), 0.022, m["wood"], n=14, name="grip", r1=0.018))
    p.append(make.cyl((0, 0, 0.1), (0, 0, 0.115), 0.022, m["brass"], n=14, name="cap"))
    p.append(make.box((0, 0.028, 0.02), (0.008, 0.006, 0.05), m["steel"], bevel=0.002, name="latch"))
    return p


def brake_lever(m):
    # Pivot at the origin (on the valve's top), the handle 0.25 m up it: a forged arm, a wooden knob.
    L = LEVER_BRAKE
    p = [make.cyl((0, 0, 0), (0, 0, L - 0.02), 0.012, m["iron"], n=8, name="arm")]
    p.append(make.cyl((-0.02, 0, 0), (0.02, 0, 0), 0.02, m["iron"], n=12, name="boss"))
    p.append(make.cyl((0, 0, L - 0.035), (0, 0, L + 0.035), 0.022, m["wood"], n=14, name="knob"))
    return p


def brake_stand(m):
    # The valve body on its pedestal, from the cab floor up to the handle's pivot (its origin: the sim's brake
    # handle is at deck + 1.15, the pivot 0.25 below it), a brass valve with its ports.
    H = 1.15 - LEVER_BRAKE
    p = [make.box((0, 0, -H + 0.02), (0.1, 0.1, 0.02), m["paint"], bevel=0.005, name="foot")]
    p.append(make.cyl((0, 0, -H + 0.04), (0, 0, -0.14), 0.04, m["paint"], n=12, name="column"))
    p.append(make.cyl((0, 0, -0.14), (0, 0, -0.03), 0.075, m["brass"], n=18, name="valve"))
    p.append(make.cyl((0, 0, -0.03), (0, 0, 0), 0.05, m["brass"], n=16, name="cover"))
    for a in (0.3, 2.4, 4.2):
        p.append(make.cyl((math.cos(a) * 0.06, math.sin(a) * 0.06, -0.09), (math.cos(a) * 0.12, math.sin(a) * 0.12, -0.09), 0.015, m["brass"], n=8, name="port"))
    for k in range(6):
        a = k * math.pi / 3
        p.append(make.nail((math.cos(a) * 0.045, math.sin(a) * 0.045, 0.001), (0, 0, 1), m["steel"], r=0.006))
    return p


def reverser_lever(m):
    # From its pivot at the floor, 0.9 m up to the grip; a spring latch rod down its side to the quadrant's notch.
    L = LEVER_REVERSER
    p = [make.box((0, 0, L / 2), (0.018, 0.012, L / 2 - 0.02), m["iron"], bevel=0.004, name="arm")]
    p.append(make.cyl((-0.03, 0, 0.0), (0.03, 0, 0.0), 0.028, m["iron"], n=12, name="boss"))
    p.append(make.cyl((0, 0, L - 0.08), (0, 0, L + 0.06), 0.024, m["wood"], n=14, name="grip"))
    p.append(make.cyl((0, -0.018, 0.42), (0, -0.018, L - 0.1), 0.006, m["steel"], n=6, name="latch_rod"))
    p.append(make.box((0, -0.03, L - 0.1), (0.01, 0.02, 0.012), m["steel"], bevel=0.003, name="trigger"))
    return p


def reverser_quadrant(m):
    # The notched arc it rides in (about the pivot, 0.42 m up), on two stays from the floor.
    p = []
    r = 0.42
    for k in range(9):
        a0, a1 = math.radians(-14 + k * 28 / 9), math.radians(-14 + (k + 1) * 28 / 9)
        p.append(make.box((0, -r * math.sin((a0 + a1) / 2), r * math.cos((a0 + a1) / 2)),
                          (0.01, r * (a1 - a0) / 2 + 0.002, 0.018), m["iron"], bevel=0.003, name="arc"))
        p.append(make.box((0.012, -r * math.sin(a0), r * math.cos(a0) + 0.02), (0.004, 0.006, 0.006), m["steel"], bevel=0, name="notch", low=False))
    for y in (-0.11, 0.11):
        p.append(make.cyl((0.01, y * 0.9, 0.0), (0.01, y, 0.4), 0.01, m["iron"], n=8, name="stay"))
    p.append(make.box((0, 0, 0.01), (0.05, 0.14, 0.01), m["paint"], bevel=0.004, name="base"))
    return p


def vent_valve(m):
    # The valve on the cab wall at the vent's height (its origin), a pull lever, and the pipe up through the roof.
    p = [make.cyl((0, 0, -0.1), (0, 0, 0.1), 0.07, m["brass"], n=18, name="body")]
    p.append(make.cyl((0, 0, 0.1), (0, 0, 1.6), 0.035, m["iron"], n=12, name="pipe"))
    p.append(make.cyl((0, 0, -0.1), (0, 0, -0.3), 0.035, m["iron"], n=12, name="pipe_down"))
    p.append(make.cyl((0, -0.05, 0.02), (0, -0.12, 0.02), 0.02, m["brass"], n=10, name="spindle"))
    p.append(make.box((0.08, -0.13, 0.02), (0.1, 0.01, 0.012), m["iron"], bevel=0.003, name="lever"))
    p.append(make.cyl((0.17, -0.13, 0.02), (0.17, -0.2, 0.02), 0.016, m["wood"], n=10, name="handle"))
    for z in (-0.1, 0.1):
        p.append(make.torus((0, 0, z), (0, 0, 1), 0.07, 0.01, m["brass"], name="flange", low=(10, 3)))
    return p


build("lever_regulator", regulator, "the regulator's handle", budget=600)
build("lever_brake", brake_lever, "the brake valve's handle", budget=500)
build("brake_stand", brake_stand, "the brake valve on its pedestal")
build("lever_reverser", reverser_lever, "the reverser", budget=600)
build("reverser_quadrant", reverser_quadrant, "the reverser's quadrant")
build("vent_valve", vent_valve, "the blow-off valve")
