"""The gun car's cannon (GDD v1.2 §19, §26: "crude cannons with a manual reload"; App. C.3 powder, ball, ram), modelled in
the pieces that move so the game can work them: what stays on the roof, what turns, what elevates, and what's loaded.

A crude breech-loading swivel gun, the kind a railway shop could cast and bore: a short fat iron barrel reinforced with
shrunk-on rings, trunnioned in a forked yoke on a turntable; the gunner sits behind it on an iron seat on the turntable,
traversing it with a tiller under the left hand and elevating it with a handwheel under the right; a boiler-plate shield
in front of them with a slot to aim through. It loads at the breech from the seat: the ball rolled into the bore's open
back, a powder chamber (an iron mug with a handle) dropped in behind it, an iron wedge driven down behind that to lock it
(the ram), and fired at the touch hole with the friction lanyard. Three spare chambers ride in a rack by the seat, the
shot in a box on the other side.

The pieces, all in the gun's frame (Blender axes: +Y along the barrel, the engine's -Z; +X right; +Z up; the origin the
traverse pivot and trunnions, 0.9 m over the roof as Sim.Combat.Guns.PivotHeight has it):
  * cannon_mount: the trolley on the roof rail (its wheels on the rail's 0.32 m gauge) and the pedestal up to the
    traverse ring; fixed to the car;
  * cannon_carriage: the turntable, the yoke, the seat (its pan's top at z -0.42, 0.75 m behind the pivot: SEAT), the
    tiller, the handwheel, the shield, the chamber rack and the shot box; turns about Z;
  * cannon_barrel: the barrel, its rings, the breech's open trough and the wedge, the sights, the lanyard's friction
    tube; elevates about X (the trunnions); the muzzle 1.5 m out along +Y;
  * cannon_chamber: one powder chamber, its mouth at the origin facing +Y (sat in the breech at CHAMBER);
  * cannon_ball: a round of shot, 0.1 m across, its middle at the origin.

    tools/models/build.sh cannon
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

SEAT = (0.0, -0.75, -0.42)          # the seat pan's top middle (the gunner's hips sit here)
CHAMBER = (0.0, -0.36, 0.0)         # where a loaded chamber sits in the breech, its mouth facing the bore
MUZZLE = 1.5
BORE = 0.05


def materials():
    return {
        "iron": make.lib("wheel_iron", 3.0, tint=(0.42, 0.4, 0.38), rough=0.45, metal=0.8),
        "cast": make.lib("wheel_iron", 2.5, tint=(0.3, 0.29, 0.28), rough=0.55, metal=0.7),
        "plate": make.lib("iron_plate", 2.0, tint=(0.55, 0.54, 0.52), rough=0.5, metal=0.6),
        "rust": make.lib("rust_heavy", 3.0, tint=(0.35, 0.3, 0.27), rough=0.65, metal=0.4),
        "wood": make.lib("wood_grey", 6.0, tint=(0.6, 0.46, 0.34), rough=0.55),
        "brass": make.lib("brass", 6.0, tint=(0.8, 0.7, 0.5), rough=0.35, metal=0.9),
        "rope": make.lib("wood_grey", 12.0, tint=(0.55, 0.47, 0.34), rough=0.85),
        "black": make.flat("cannon_bore", (0.01, 0.009, 0.008), rough=0.6),
    }


def rivet(at, normal, m, r=0.009):
    n = Vector(normal).normalized()
    return make.cyl(Vector(at) - n * 0.002, Vector(at) + n * 0.006, r, m, n=8, bevel=0.002, name="rivet", r1=r * 0.6, low=0)


def mount(m):
    p = []
    # The trolley: a cast frame on four flanged wheels on the rail (0.32 m gauge), a clamp to lock it.
    roof = -0.9
    p.append(make.box((0, 0, roof + 0.1), (0.24, 0.32, 0.04), m["cast"], bevel=0.012, name="trolley"))
    for sx in (-1, 1):
        for sy in (-1, 1):
            c = Vector((sx * 0.16, sy * 0.22, roof + 0.075))
            p.append(make.cyl(c - Vector((0.025, 0, 0)), c + Vector((0.025, 0, 0)), 0.06, m["iron"], n=16, bevel=0.004, name="wheel", low=8))
            p.append(make.cyl(c - Vector((sx * 0.03, 0, 0)) - Vector((0.008, 0, 0)), c - Vector((sx * 0.03, 0, 0)) + Vector((0.008, 0, 0)), 0.072,
                              m["iron"], n=16, name="flange", low=0))
    p.append(make.box((0.27, 0, roof + 0.12), (0.03, 0.05, 0.05), m["rust"], bevel=0.006, name="clamp"))
    p.append(make.cyl((0.3, 0, roof + 0.12), (0.42, 0, roof + 0.2), 0.012, m["iron"], n=8, name="clamp_lever", low=4))
    # The pedestal: a flared column up to the traverse ring.
    p.append(make.cyl((0, 0, roof + 0.14), (0, 0, roof + 0.24), 0.2, m["cast"], n=24, bevel=0.01, name="foot", r1=0.13, low=10))
    p.append(make.cyl((0, 0, roof + 0.24), (0, 0, -0.3), 0.11, m["cast"], n=20, bevel=0.008, name="column", low=10))
    p.append(make.cyl((0, 0, -0.3), (0, 0, -0.24), 0.2, m["iron"], n=28, bevel=0.008, name="ring", low=12))
    for k in range(10):
        a = 2 * math.pi * k / 10
        p.append(rivet((math.cos(a) * 0.17, math.sin(a) * 0.17, -0.24), (0, 0, 1), m["iron"], r=0.012))
    for k in range(4):
        a = 2 * math.pi * k / 4 + 0.4
        d = Vector((math.cos(a), math.sin(a), 0))
        p.append(make.box(tuple(d * 0.15 + Vector((0, 0, roof + 0.3))), (0.012, 0.06, 0.08), m["cast"], bevel=0.004, name="web",
                          rot=Matrix.Rotation(a + math.pi / 2, 4, "Z")))
    return p


def carriage(m):
    p = []
    # The turntable and the yoke's two cheeks up to the trunnions.
    p.append(make.cyl((0, 0, -0.24), (0, 0, -0.2), 0.22, m["plate"], n=28, bevel=0.008, name="turntable", low=12))
    for sx in (-1, 1):
        x = sx * 0.2
        p.append(make.box((x, 0, -0.12), (0.025, 0.12, 0.1), m["cast"], bevel=0.01, name="cheek"))
        p.append(make.cyl((x - 0.03, 0, 0), (x + 0.03, 0, 0), 0.05, m["cast"], n=18, bevel=0.006, name="bearing", low=8))
        p.append(make.box((x + sx * 0.035, 0, 0.0), (0.008, 0.045, 0.012), m["iron"], bevel=0.002, name="cap_strap"))
        for dy in (-0.035, 0.035):
            p.append(rivet((x + sx * 0.04, dy, 0.0), (sx, 0, 0), m["iron"]))
    # The seat: a pressed iron pan on a sprung arm off the turntable's back, the footrest under it.
    sx_, sy, sz = SEAT
    arm = [Vector((0, -0.18, -0.21)), Vector((0, -0.45, -0.3)), Vector((0, -0.7, -0.46))]
    for a, b in zip(arm, arm[1:]):
        p.append(make.box(tuple((a + b) / 2), (0.03, (b - a).length / 2 + 0.01, 0.012), m["iron"], bevel=0.004, name="seat_arm",
                          rot=Matrix.Rotation(math.atan2(b.z - a.z, b.y - a.y), 4, "X")))
    pan = Vector((0, sy, sz - 0.02))
    p.append(make.cyl(pan - Vector((0, 0, 0.02)), pan, 0.18, m["plate"], n=24, bevel=0.01, name="seat_pan", r1=0.2, low=10))
    p.append(make.torus(pan + Vector((0, -0.04, 0.02)), (0, 0, 1), 0.17, 0.014, m["plate"], n=24, m=6, name="seat_lip", low=(10, 4)))
    for k in range(5):
        a = -0.6 + 0.3 * k
        p.append(make.cyl(pan + Vector((math.sin(a) * 0.1, math.cos(a) * 0.04 - 0.02, -0.004)),
                          pan + Vector((math.sin(a) * 0.1, math.cos(a) * 0.04 - 0.02, 0.004)), 0.016, m["black"], n=8, name="seat_hole", low=0))
    p.append(make.box((0, -0.38, -0.66), (0.17, 0.05, 0.012), m["plate"], bevel=0.004, name="footrest"))
    p.append(make.box((0, -0.3, -0.45), (0.02, 0.08, 0.2), m["iron"], bevel=0.004, name="footrest_post", rot=Matrix.Rotation(-0.6, 4, "X")))
    # The traverse tiller under the left hand; the elevating handwheel under the right, its screw up to the barrel's
    # breech quadrant.
    p.append(make.cyl((-0.21, -0.1, -0.15), (-0.3, -0.55, -0.22), 0.018, m["iron"], n=10, name="tiller", low=6))
    p.append(make.cyl((-0.3, -0.55, -0.22), (-0.31, -0.64, -0.24), 0.024, m["wood"], n=12, bevel=0.004, name="tiller_grip", low=6))
    wheel = Vector((0.26, -0.45, -0.18))
    p.append(make.torus(wheel, (0, 1, 0.3), 0.09, 0.011, m["iron"], n=24, m=6, name="handwheel", low=(10, 4)))
    for k in range(4):
        a = math.pi / 4 + k * math.pi / 2
        p.append(make.cyl(wheel, wheel + Vector((math.cos(a) * 0.085, 0, math.sin(a) * 0.085)), 0.008, m["iron"], n=6, name="spoke", low=0))
    p.append(make.cyl(wheel + Vector((math.cos(0.4) * 0.09, 0, math.sin(0.4) * 0.09)),
                      wheel + Vector((math.cos(0.4) * 0.09, -0.06, math.sin(0.4) * 0.09)), 0.012, m["wood"], n=8, name="knob", low=4))
    p.append(make.cyl(wheel, (0.2, -0.28, -0.12), 0.014, m["iron"], n=8, name="screw_shaft", low=4))
    p.append(make.box((0.2, -0.26, -0.12), (0.03, 0.03, 0.04), m["cast"], bevel=0.006, name="screw_box"))
    # The shield: boiler plate bent round in front, a slot to aim through, riveted to two stays off the cheeks.
    for k in range(7):
        a = -0.75 + 0.25 * k
        if k in (2, 3, 4):
            # The slot, at a sat gunner's eye (the seat's 0.78 m under it): only the plate under it and over it.
            for z0, z1 in ((-0.3, 0.12), (0.44, 0.52)):
                c = Vector((math.sin(a) * 0.5, 0.38 + math.cos(a) * 0.08 - 0.06 * abs(math.sin(a)), (z0 + z1) / 2))
                p.append(make.box(tuple(c), (0.065, 0.008, (z1 - z0) / 2), m["plate"], bevel=0.004, name="shield",
                                  rot=Matrix.Rotation(-a, 4, "Z")))
            continue
        c = Vector((math.sin(a) * 0.5, 0.38 + math.cos(a) * 0.08 - 0.06 * abs(math.sin(a)), 0.11))
        p.append(make.box(tuple(c), (0.065, 0.008, 0.41), m["plate"], bevel=0.004, name="shield", rot=Matrix.Rotation(-a, 4, "Z")))
        for z in (-0.26, 0.11, 0.46):
            n = Vector((math.sin(a), math.cos(a), 0))
            p.append(rivet(tuple(c + Vector((0, 0, z - 0.11)) + n * 0.008), tuple(n), m["iron"], r=0.008))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.2, 0.22, -0.16), (0.012, 0.16, 0.02), m["iron"], bevel=0.004, name="shield_stay"))
    # The chamber rack by the seat's left, three spare chambers in it; the shot box on the right with balls in it.
    rack = Vector((-0.32, -0.62, -0.42))
    p.append(make.box(tuple(rack), (0.07, 0.17, 0.012), m["plate"], bevel=0.004, name="rack_shelf"))
    p.append(make.box(tuple(rack + Vector((0, 0, 0.09))), (0.07, 0.17, 0.008), m["plate"], bevel=0.003, name="rack_top"))
    for k in range(3):
        c = rack + Vector((0, -0.11 + 0.11 * k, 0.012))
        p.extend(chamber_parts(m, base=c, up=Vector((0, 0, 1))))
    box_c = Vector((0.32, -0.62, -0.36))
    p.append(make.box(tuple(box_c), (0.08, 0.12, 0.07), m["wood"], bevel=0.008, name="shot_box"))
    for k in range(4):
        p.append(make.cyl(box_c + Vector((-0.035 + 0.07 * (k % 2), -0.04 + 0.08 * (k // 2), 0.07)),
                          box_c + Vector((-0.035 + 0.07 * (k % 2), -0.04 + 0.08 * (k // 2), 0.071)), 0.04, m["cast"], n=12, name="ball_top", low=0))
    for k in range(3):
        y = -0.7 + 0.08 * k
        p.append(ball_part(m, Vector((0.32 - 0.035 + 0.035 * (k % 2), y, -0.285))))
    return p


def ball_part(m, at, r=BORE * 0.96, low=True):
    """A round of shot: an iron sphere (and its game-mesh twin, coarser)."""
    for segs, rings, keep in ((10, 6, low), (20, 12, True)):
        if not keep:
            continue
        o = cook.uv_sphere(segs, rings, r, Matrix.Translation(Vector(at)))
        o.data.materials.append(m["cast"])
        if segs == 10:
            o.name = "ball_low"
            make.LOW.append(o)
        else:
            o.name = "ball"
            for f in o.data.polygons:
                f.use_smooth = True
    return o


def chamber_parts(m, base=Vector((0, 0, 0)), up=Vector((0, 1, 0))):
    """A powder chamber: an iron mug, its mouth `up` from `base` (0.2 m long), a handle on its side, the touch hole."""
    up = up.normalized()
    side = up.cross(Vector((0, 0, 1)) if abs(up.z) < 0.9 else Vector((0, 1, 0))).normalized()
    p = [make.cyl(base, base + up * 0.2, 0.062, m["iron"], n=16, bevel=0.006, name="chamber", r1=0.055, low=12),
         make.torus(base + up * 0.2, up, 0.05, 0.008, m["iron"], n=16, m=5, name="chamber_lip", low=None),
         make.cyl(base + up * 0.199, base + up * 0.201, BORE * 0.8, m["black"], n=12, name="chamber_mouth", low=0)]
    a, b = base + up * 0.05 + side * 0.062, base + up * 0.15 + side * 0.062
    p.append(make.cyl(a, a + side * 0.05, 0.01, m["iron"], n=6, name="handle", low=0))
    p.append(make.cyl(b, b + side * 0.05, 0.01, m["iron"], n=6, name="handle", low=0))
    p.append(make.cyl(a + side * 0.05, b + side * 0.05, 0.011, m["iron"], n=6, name="handle", low=4))
    return p


def barrel(m):
    p = []
    # The barrel: cast, fat at the breech, the chase tapering to the muzzle's swell; rings shrunk on.
    prof = [(-0.22, 0.15), (0.1, 0.15), (0.25, 0.13), (1.1, 0.1), (1.36, 0.098), (1.42, 0.12), (MUZZLE, 0.115)]
    for (y0, r0), (y1, r1) in zip(prof, prof[1:]):
        p.append(make.cyl((0, y0, 0), (0, y1, 0), r0, m["cast"], n=28, bevel=0.006, name="barrel", r1=r1, low=12))
    for y, r in ((-0.2, 0.162), (0.08, 0.162), (0.26, 0.142), (0.7, 0.122), (1.38, 0.112)):
        p.append(make.torus((0, y, 0), (0, 1, 0), r, 0.014, m["iron"], n=28, m=6, name="ring", low=(12, 4)))
    p.append(make.cyl((0, MUZZLE - 0.001, 0), (0, MUZZLE + 0.001, 0), BORE, m["black"], n=16, name="bore", low=0))
    # The trunnions.
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.14, 0, 0), (sx * 0.23, 0, 0), 0.042, m["cast"], n=16, bevel=0.004, name="trunnion", low=8))
    # The breech: two side straps back from the barrel's end round an open trough for the chamber, the strap's back
    # plate, the wedge's slot in front of it and the wedge driven down in it; the cascabel knob behind.
    for sx in (-1, 1):
        p.append(make.box((sx * 0.085, -0.4, -0.02), (0.022, 0.19, 0.05), m["iron"], bevel=0.008, name="breech_strap"))
    p.append(make.box((0, -0.4, -0.08), (0.1, 0.19, 0.016), m["iron"], bevel=0.006, name="trough"))
    p.append(make.box((0, -0.6, -0.01), (0.1, 0.02, 0.07), m["iron"], bevel=0.006, name="back_plate"))
    p.append(make.box((0, -0.565, 0.02), (0.07, 0.014, 0.1), m["rust"], bevel=0.004, name="wedge", rot=Matrix.Rotation(-0.08, 4, "X")))
    p.append(make.box((0, -0.565, 0.125), (0.075, 0.02, 0.012), m["rust"], bevel=0.003, name="wedge_head"))
    p.append(make.cyl((0, -0.62, -0.01), (0, -0.7, -0.01), 0.03, m["cast"], n=14, bevel=0.004, name="cascabel_neck", low=6))
    p.append(make.cyl((0, -0.7, -0.01), (0, -0.78, -0.01), 0.05, m["cast"], n=16, bevel=0.012, name="cascabel", r1=0.03, low=8))
    # The elevating quadrant under the breech (the carriage's screw meets it).
    p.append(make.box((0.12, -0.25, -0.12), (0.012, 0.09, 0.05), m["iron"], bevel=0.004, name="quadrant"))
    # The sights: a notch at the breech, a blade at the muzzle swell; the friction tube and its lanyard.
    p.append(make.box((0, -0.18, 0.165), (0.03, 0.012, 0.02), m["iron"], bevel=0.003, name="rear_sight"))
    p.append(make.box((0, MUZZLE - 0.06, 0.13), (0.006, 0.02, 0.022), m["iron"], bevel=0.002, name="fore_sight", low=False))
    p.append(make.cyl((0, -0.3, 0.06), (0, -0.3, 0.1), 0.012, m["brass"], n=8, name="touch_hole", low=0))
    lanyard = [Vector((0, -0.3, 0.1)), Vector((-0.05, -0.38, 0.06)), Vector((-0.1, -0.5, -0.05)), Vector((-0.12, -0.56, -0.12))]
    for a, b in zip(lanyard, lanyard[1:]):
        p.append(make.cyl(a, b, 0.005, m["rope"], n=6, name="lanyard", low=0))
    p.append(make.cyl(lanyard[-1], lanyard[-1] + Vector((0, 0, -0.06)), 0.012, m["wood"], n=8, name="toggle", low=0))
    # Its maker's mark, cast in.
    p.append(make.box((0, 0.5, 0.118), (0.035, 0.06, 0.003), m["cast"], bevel=0.002, name="mark", low=False))
    return p


PIECES = [("cannon_mount", mount, 1600, "the cannon's trolley and pedestal"),
          ("cannon_carriage", carriage, 3600, "the cannon's carriage: yoke, seat, tiller, handwheel, shield, rack"),
          ("cannon_barrel", barrel, 2400, "the cannon's barrel and breech"),
          ("cannon_chamber", lambda m: chamber_parts(m), 300, "a powder chamber"),
          ("cannon_ball", lambda m: [ball_part(m, Vector((0, 0, 0)))], 200, "a round of shot")]
want = set(cook.args()) or {n for n, *_ in PIECES}
for name, fn, budget, what in PIECES:
    if name not in want:
        continue
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, f"{name}_low", budget, colour=None, size=512 if budget < 1000 else 1024, cage=0.004, reach=0.012,
                         low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.35, made=make.provenance("cannon", what))
