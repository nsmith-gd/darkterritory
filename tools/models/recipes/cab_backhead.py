"""The engine's backhead dressed: what the fireman and driver face all night. The kit's backhead is the plate, the
firebox door's frame, the four gauges and the water glass (TrainKit.Cab); this is everything on it:

  * the firebox doors, a pair of riveted butterfly leaves swung wide on their hinges (the fire's glow, and whatever
    else is in there, shows through them), their handles, the firehole ring;
  * the steam turret across the top, its valves' handwheels, the siphon pipes down to each gauge;
  * the injectors' steam valves either side and their copper feed pipes going down to the floor;
  * the blower valve, the lubricator with its sight glasses, the whistle's lever and pull, the damper's notched
    quadrant by the floor.

Origin: the firebox door's centre on the backhead's face (the engine's (0, deck + 0.7, cabFront + 0.085)); it
stands out of the face toward the model's front (-Y here, the engine's +Z, into the cab). Baked in four groups (the
doors, the turret, the left and right sides) so it keeps its texel density up close.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

cook.reset()
iron = make.lib("rust_heavy", 2.5, tint=(0.5, 0.46, 0.43), rough=0.5, metal=0.6)
plate = make.lib("iron_smokebox", 1.5, tint=(0.55, 0.52, 0.5), rough=0.6, metal=0.4)
brass = make.lib("brass", 3.0, rough=0.3, metal=0.9)
copper = make.lib("copper_pipe", 2.0, rough=0.35, metal=0.9)
wood = make.lib("wood_sleeper", 4.0, tint=(0.55, 0.42, 0.3), rough=0.6)
glass = make.flat("sight", (0.35, 0.3, 0.18), rough=0.1)
OUT = Vector((0, -1, 0))  # out of the backhead, into the cab
groups = {}


def group(name):
    start = len(make.LOW)
    parts = []
    groups[name] = (parts, start)
    return parts


# ----------------------------------------------------------------------------------------------------------------
# The firebox doors: two leaves hinged at the opening's sides, swung 70 degrees open (wide enough to see into the fire,
# and what's in it: the Stoker, CreatureArt).
doors = group("doors")
for sx in (-1, 1):
    hinge = Vector((sx * 0.33, -0.09, 0))
    rot = Matrix.Translation(hinge) @ Matrix.Rotation(sx * math.radians(70), 4, "Z") @ Matrix.Translation(-hinge)
    start = len(make.LOW)
    leaf_c = Vector((sx * 0.17, -0.105, 0))
    leaf = [make.box(leaf_c, (0.155, 0.014, 0.215), plate, bevel=0.006, name="leaf")]
    # A riveted rim on the leaf, and a curved handle.
    for k in range(10):
        t = k / 9
        for z in (-0.19, 0.19):
            leaf.append(make.nail((leaf_c.x + sx * (-0.13 + 0.26 * t), leaf_c.y - 0.015, z), OUT, iron, r=0.008))
    leaf.append(make.cyl((sx * 0.07, -0.135, -0.07), (sx * 0.07, -0.135, 0.07), 0.012, iron, n=8, name="handle"))
    for z in (-0.07, 0.07):
        leaf.append(make.cyl((sx * 0.07, -0.12, z), (sx * 0.07, -0.135, z), 0.01, iron, n=6, name="handle_foot", low=0))
    # Swung open on its hinge (and its game mesh with it); the hinge pin stays.
    cook.transform(leaf + make.LOW[start:], rot)
    doors += leaf
    doors.append(make.cyl((sx * 0.33, -0.1, -0.23), (sx * 0.33, -0.1, 0.23), 0.02, iron, n=10, name="hinge"))
# The firehole ring, round the opening, proud of the frame.
for z in (-0.25, 0.25):
    doors.append(make.box((0, -0.09, z), (0.36, 0.012, 0.03), iron, bevel=0.006, name="ring_h"))
for x in (-0.36, 0.36):
    doors.append(make.box((x, -0.09, 0), (0.03, 0.012, 0.25), iron, bevel=0.006, name="ring_v"))

# ----------------------------------------------------------------------------------------------------------------
# The turret: a manifold across the top, fed from the dome, its valves' wheels facing the crew.
turret = group("turret")
TZ = 1.4
turret += make.pipe([(-0.42, -0.12, TZ), (0.42, -0.12, TZ)], 0.055, brass, name="turret")
turret.append(make.cyl((0, -0.03, TZ), (0, -0.12, TZ), 0.07, brass, n=14, name="turret_neck"))
for k, x in enumerate((-0.3, -0.1, 0.1, 0.3)):
    turret.append(make.cyl((x, -0.12, TZ + 0.05), (x, -0.12, TZ + 0.13), 0.028, brass, n=12, name="valve_body"))
    turret += make.handwheel((x, -0.12, TZ + 0.15), (0, 0, 1), 0.055, iron, name=f"turret_wheel{k}")
# Siphons from the turret down to each gauge (the gauges are TrainKit's, at deck + 1.85 and alternately 6 cm up).
for i in range(4):
    x = -0.54 + i * 0.36
    gz = 1.15 + (i % 2) * 0.06
    turret += make.pipe([(x * 0.55, -0.12, TZ - 0.05), (x, -0.06, TZ - 0.12), (x, -0.06, gz + 0.14)], 0.008, copper, name="siphon", n=8, low=4)

# ----------------------------------------------------------------------------------------------------------------
# The sides: the injectors' steam valves and feed pipes; the blower; the lubricator; the whistle; the damper.
left = group("left")
x = -0.75
left += make.pipe([(-0.42, -0.12, TZ), (x, -0.12, TZ), (x, -0.12, 0.95)], 0.03, copper, name="inj_steam")
left.append(make.cyl((x, -0.12, 0.95), (x, -0.12, 0.83), 0.045, brass, n=14, name="inj_valve"))
left += make.handwheel((x, -0.2, 0.89), (0, -1, 0), 0.07, iron, name="inj_wheel_l")
left += make.pipe([(x, -0.12, 0.83), (x, -0.12, -0.3), (x + 0.05, -0.2, -0.7)], 0.03, copper, name="inj_feed")
# The blower valve, left of the doors, and its wheel.
left.append(make.cyl((-0.5, -0.02, 0.65), (-0.5, -0.12, 0.65), 0.035, brass, n=12, name="blower"))
left += make.handwheel((-0.5, -0.14, 0.65), (0, -1, 0), 0.05, iron, name="blower_wheel")
left += make.pipe([(-0.5, -0.06, 0.68), (-0.5, -0.06, TZ - 0.06)], 0.012, copper, name="blower_pipe", n=8, low=4)
# The lubricator: a brass box with three sight glasses, its oil pipes.
left.append(make.box((-0.55, -0.1, 0.3), (0.1, 0.06, 0.12), brass, bevel=0.01, name="lubricator"))
for k in range(3):
    left.append(make.box((-0.62 + k * 0.07, -0.162, 0.32), (0.018, 0.004, 0.05), glass, bevel=0, name="sight", low=False))
left += make.pipe([(-0.55, -0.1, 0.42), (-0.55, -0.1, 0.6), (-0.62, -0.06, 0.62)], 0.009, copper, name="oil", n=8, low=4)

right = group("right")
x = 0.78
right += make.pipe([(0.42, -0.12, TZ), (x, -0.12, TZ), (x, -0.12, 0.95)], 0.03, copper, name="inj_steam")
right.append(make.cyl((x, -0.12, 0.95), (x, -0.12, 0.83), 0.045, brass, n=14, name="inj_valve"))
right += make.handwheel((x, -0.2, 0.89), (0, -1, 0), 0.07, iron, name="inj_wheel_r")
right += make.pipe([(x, -0.12, 0.83), (x, -0.12, -0.3), (x - 0.05, -0.2, -0.7)], 0.03, copper, name="inj_feed")
# The whistle's valve up by the roof, its lever and the pull hanging from it (wooden handle).
right.append(make.cyl((0.45, -0.02, 1.48), (0.45, -0.1, 1.48), 0.04, brass, n=12, name="whistle_valve"))
right.append(make.box((0.53, -0.12, 1.48), (0.1, 0.012, 0.014), iron, bevel=0.004, name="whistle_lever"))
right.append(make.cyl((0.62, -0.12, 1.47), (0.62, -0.12, 1.2), 0.004, iron, n=6, bevel=0, name="pull", low=4))
right.append(make.cyl((0.62, -0.12, 1.2), (0.62, -0.12, 1.12), 0.016, wood, n=10, name="pull_handle"))
# The damper: a notched quadrant low on the right, its lever in the second notch.
right.append(make.box((0.35, -0.06, -0.55), (0.1, 0.008, 0.06), iron, bevel=0.004, name="quadrant"))
for k in range(5):
    right.append(make.box((0.27 + k * 0.04, -0.07, -0.49), (0.006, 0.006, 0.01), iron, bevel=0, name="notch", low=False))
lever = make.box((0.3, -0.08, -0.45), (0.012, 0.012, 0.13), iron, bevel=0.004, name="damper_lever")
right.append(lever)
right.append(make.cyl((0.3, -0.08, -0.33), (0.3, -0.08, -0.25), 0.018, wood, n=10, name="damper_handle"))

# The regulator's rack: a toothed bar out from the backhead's right, the throttle handle (lever_regulator) sliding
# back along it as it opens. It runs where the sim's handle does (CabLevers.Regulator, deck + 1.55, cabFront + 0.3,
# and 0.3 m back): the bar 6 cm under the grip.
RX, RZ = 0.55, 0.79
right.append(make.box((RX, -0.29, RZ), (0.016, 0.29, 0.02), iron, bevel=0.004, name="rack"))
for k in range(14):
    right.append(make.box((RX, -0.05 - k * 0.036, RZ + 0.024), (0.014, 0.006, 0.006), iron, bevel=0, name="tooth", low=False))
right.append(make.cyl((RX, 0, RZ), (RX, -0.06, RZ), 0.045, brass, n=14, name="gland"))
right.append(make.box((RX, -0.57, RZ - 0.1), (0.014, 0.014, 0.1), iron, bevel=0.004, name="rack_stay"))

# ----------------------------------------------------------------------------------------------------------------
# Bake each group onto its own game mesh (and so its own layer).
lows = []
names = list(groups)
for i, name in enumerate(names):
    parts, start = groups[name]
    end = groups[names[i + 1]][1] if i + 1 < len(names) else len(make.LOW)
    low = make.LOW[start:end]
    lows += cook.bake_down(parts, f"backhead_{name}", 800, colour=None, size=1024, cage=0.004, reach=0.012, low=low)
cook.finish("cab_backhead", lows, budget=7000, grime=0.6, made=make.provenance("cab_backhead", "the engine's backhead fittings"))
