"""The handcar (GDD §24.1; ARCHITECTURE §8 notes 729, 816), modelled, as parts the scene moves where the sim has them:

  * handcar_body: the trolley that isn't moving: a plank deck on two iron sills (train.json handcar: 3.0 m long, 1.7 m wide,
    its deck 0.55 m over the rails), its end beams, the axles in their boxes (wheels 1.9 m apart, on a 1.44 m gauge's
    centres), a side step each side 0.28 m up on its hangers, the A-frame post with the handle's bearing 0.95 m over the deck,
    the gear case on the back axle, the brake lever at the front, and the lamp's bracket at the front right corner;
  * handcar_wheel: one spoked iron wheel with its flange (0.26 m to the tread), turned by the scene by the distance run;
  * handcar_handle: the see-saw over the post, a timber beam 1.6 m long strapped in iron with a grip bar across each end
    (GreyboxScene.HandleGrip: the bars 0.3 m either side of the middle), turned about its bearing by the sim's swing;
  * handcar_chain: a metre of chain, stretched by the scene from the van's platform rail to the hung trolley's top corners.

Axes (Blender): +X right, +Z up, +Y the front (the game's -Z); the body's origin at rail height under its middle, the
wheel's and the handle's at their own axles, the chain's along +X from its first link.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

LENGTH, WIDTH, DECK = 3.0, 1.7, 0.55
AXLE_Y, GAUGE_X, WHEEL_R = 0.95, 0.72, 0.26
POST, ARM, GRIP = 0.95, 0.8, 0.3


def materials():
    return {
        "iron": make.lib("rust_heavy", 1.5, tint=(0.5, 0.46, 0.43), rough=0.55, metal=0.6),
        "cast": make.lib("rust_heavy", 2.5, tint=(0.33, 0.31, 0.3), rough=0.6, metal=0.5),
        "timber": make.lib("wood_sleeper", 1.6, tint=(0.95, 0.82, 0.66), rough=0.8),
        "deck": make.lib("wood_sleeper", 2.2, tint=(0.82, 0.7, 0.56), rough=0.85),
        "paint": make.lib("paint_oxide", 0.8, tint=(0.75, 0.62, 0.55), rough=0.6),
        "brass": make.lib("brass", 3.0, rough=0.3, metal=0.9),
    }


def build(name, fn, what, size=1024, budget=3000):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = fn(materials())
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=size, cage=0.004, reach=0.012, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.6, made=make.provenance("handcar", what))


def bolts(a, b, n, normal, m, r=0.01):
    return [make.nail(tuple(a[i] + (b[i] - a[i]) * k / max(1, n - 1) for i in range(3)), normal, m["iron"], r=r) for k in range(n)]


def body(m):
    p = []
    w, l = WIDTH / 2, LENGTH / 2
    # The deck: nine planks across it, a little uneven, nailed to the sills; the end beams under its ends.
    p += make.planks((-w, -l + 0.06, DECK - 0.05), (w, l - 0.06, DECK), 1, 9, m["deck"], gap=0.012, wobble=0.004, seed=3, low=False)
    make.low_box((0, 0, DECK - 0.025), (w, l - 0.06, 0.025), m["deck"], name="deck")
    for sy in (-1, 1):
        p.append(make.box((0, sy * (l - 0.05), DECK - 0.07), (w + 0.02, 0.06, 0.08), m["timber"], bevel=0.01, name="end_beam"))
        p += bolts((-w + 0.1, sy * (l - 0.05), DECK + 0.012), (w - 0.1, sy * (l - 0.05), DECK + 0.012), 5, (0, 0, 1), m)
    # The sills: iron channels along under the deck, the axle boxes and their pedestals hung from them.
    for sx in (-1, 1):
        p.append(make.box((sx * 0.6, 0, DECK - 0.13), (0.05, l - 0.02, 0.06), m["iron"], bevel=0.006, name="sill"))
        p += bolts((sx * 0.6, -l + 0.2, DECK + 0.002), (sx * 0.6, l - 0.2, DECK + 0.002), 6, (0, 0, 1), m, r=0.012)
        for sy in (-1, 1):
            y = sy * AXLE_Y
            p.append(make.box((sx * 0.6, y, WHEEL_R + 0.07), (0.07, 0.11, 0.09), m["cast"], bevel=0.012, name="axle_box"))
            for side in (-1, 1):
                p.append(make.box((sx * 0.6, y + side * 0.13, (WHEEL_R + DECK - 0.2) / 2 + 0.04), (0.045, 0.02, (DECK - WHEEL_R) / 2),
                                  m["iron"], bevel=0.004, name="pedestal"))
            # The brake shoe against the wheel's tread, hung off the sill.
            p.append(make.box((sx * GAUGE_X, y + sy * -0.3, WHEEL_R + 0.02), (0.035, 0.03, 0.08), m["cast"], bevel=0.006, name="shoe"))
    # The axles across (the wheels go on their ends, turned by the scene).
    for sy in (-1, 1):
        p.append(make.cyl((-GAUGE_X - 0.05, sy * AXLE_Y, WHEEL_R), (GAUGE_X + 0.05, sy * AXLE_Y, WHEEL_R), 0.035, m["iron"], n=12, name="axle"))
    # The gear case on the back axle, where the handle's rod drives it.
    p.append(make.box((0, -AXLE_Y, WHEEL_R + 0.05), (0.13, 0.16, 0.18), m["cast"], bevel=0.02, name="gear_case"))
    p.append(make.cyl((0.14, -AXLE_Y, WHEEL_R), (0.18, -AXLE_Y, WHEEL_R), 0.12, m["cast"], n=18, name="gear_cover"))
    # The side steps on their hangers, half way up.
    for sx in (-1, 1):
        p.append(make.box((sx * (w + 0.15), 0, 0.28 - 0.025), (0.15, 0.4, 0.025), m["timber"], bevel=0.008, name="step"))
        for sy in (-1, 1):
            p.append(make.box((sx * (w + 0.03), sy * 0.33, (0.28 + DECK) / 2 - 0.05), (0.02, 0.03, (DECK - 0.28) / 2 + 0.03), m["iron"],
                              bevel=0.004, name="step_hanger"))
    # The post: an A-frame of iron legs up from the deck to the handle's bearing, braced across.
    top = DECK + POST
    for sx in (-1, 1):
        for sy in (-1, 1):
            p.append(make.cyl((sx * 0.12, sy * 0.36, DECK), (sx * 0.07, sy * 0.03, top - 0.05), 0.03, m["iron"], n=10, name="post_leg"))
        p.append(make.cyl((sx * 0.1, -0.2, DECK + 0.4), (sx * 0.1, 0.2, DECK + 0.4), 0.018, m["iron"], n=8, name="post_brace"))
        p.append(make.box((sx * 0.07, 0, top - 0.05), (0.025, 0.07, 0.07), m["cast"], bevel=0.01, name="bearing"))
    p.append(make.cyl((-0.11, 0, top), (0.11, 0, top), 0.03, m["iron"], n=12, name="pivot"))
    # The brake lever at the front, stood up by the post, and its rod down to the shoes.
    p.append(make.cyl((0.45, l - 0.35, DECK), (0.45, l - 0.42, DECK + 0.7), 0.02, m["iron"], n=8, name="brake_lever"))
    p.append(make.cyl((0.45, l - 0.42, DECK + 0.7), (0.45, l - 0.43, DECK + 0.78), 0.03, m["timber"], n=8, name="brake_grip"))
    p.append(make.cyl((0.45, l - 0.35, DECK - 0.06), (0.6, AXLE_Y - 0.3, WHEEL_R + 0.02), 0.012, m["iron"], n=6, name="brake_rod", low=0))
    # The lamp's bracket at the front right corner (the lamp's hung on its arm: GreyboxScene).
    p.append(make.cyl((w - 0.15, l - 0.1, DECK), (w - 0.15, l - 0.1, DECK + 0.6), 0.02, m["iron"], n=8, name="lamp_post"))
    p.append(make.cyl((w - 0.15, l - 0.1, DECK + 0.6), (w - 0.15, l + 0.02, DECK + 0.6), 0.015, m["iron"], n=6, name="lamp_arm"))
    return p


def wheel(m):
    # A spoked iron wheel: its tread and the flange inboard (to -X, the side it's mirrored for), eight spokes, the hub.
    p = [make.cyl((-0.035, 0, 0), (0.035, 0, 0), WHEEL_R, m["iron"], n=28, name="tread", low=14)]
    p.append(make.cyl((-0.05, 0, 0), (-0.03, 0, 0), WHEEL_R + 0.03, m["iron"], n=28, name="flange", low=14))
    p.append(make.cyl((-0.06, 0, 0), (0.06, 0, 0), 0.06, m["cast"], n=14, name="hub", low=8))
    for k in range(8):
        a = k * math.pi / 4
        d = (0, math.cos(a) * (WHEEL_R - 0.03), math.sin(a) * (WHEEL_R - 0.03))
        p.append(make.cyl((0, 0, 0), d, 0.018, m["iron"], n=6, name="spoke", low=4))
    return p


def handle(m):
    # The see-saw: a timber beam along the car, iron straps round it, the bearing's collar at the middle, and at each end an
    # iron grip bar across on its fork.
    p = [make.box((0, 0, 0), (0.05, ARM + 0.04, 0.05), m["timber"], bevel=0.01, name="beam")]
    for y in (-0.6, -0.25, 0.25, 0.6):
        p.append(make.box((0, y, 0), (0.056, 0.02, 0.056), m["iron"], bevel=0.003, name="strap"))
    p.append(make.cyl((-0.065, 0, 0), (0.065, 0, 0), 0.06, m["cast"], n=14, name="collar"))
    for sy in (-1, 1):
        y = sy * ARM
        for sx in (-1, 1):
            p.append(make.box((sx * 0.05, y, 0.0), (0.008, 0.04, 0.05), m["iron"], bevel=0.002, name="fork"))
        p.append(make.cyl((-GRIP - 0.12, y, 0), (GRIP + 0.12, y, 0), 0.025, m["iron"], n=10, name="grip_bar"))
        for sx in (-1, 1):
            p.append(make.cyl((sx * GRIP - 0.07, y, 0), (sx * GRIP + 0.07, y, 0), 0.032, m["timber"], n=10, name="grip"))
    return p


def chain(m):
    # A metre of iron chain along +X: links turned alternately flat and edge on.
    p = []
    pitch = 0.08
    for k in range(13):
        x = k * pitch
        axis = (0, 0, 1) if k % 2 == 0 else (0, 1, 0)
        p.append(make.torus((x, 0, 0), axis, 0.035, 0.009, m["iron"], n=10, m=5, name="link", low=(6, 3)))
    return p


build("handcar_body", body, "the handcar's deck, frame, post and fittings", budget=3200)
build("handcar_wheel", wheel, "a handcar wheel", size=512, budget=400)
build("handcar_handle", handle, "the handcar's see-saw handle", size=512, budget=600)
build("handcar_chain", chain, "a metre of chain the hung handcar hangs by", size=256, budget=200)
