"""The hotbar's tools (T108, GDD §1322's kit: the crowbar, the shovel, the wrench), modelled and baked, each in a
crewmate's fist: the origin is where the fist closes on it, the haft along +Y through the fist, the business end at +Y
(the crew rig's hand_r_weapon socket: CreatureArt hangs a tool there; the first-person view holds it the same way).

  * tool_crowbar: a 0.8 m hex bar of forged iron, rust under old black paint worn off where it's held; the goose-neck
    and its split claw at the far end, a chisel at the near, past the fist;
  * tool_shovel: the fireman's coal scoop, crew.py's (the ash haft, the D-grip at the near end, the blade at the far),
    carried one-handed up near the grip, the blade's lip worn bright;
  * tool_wrench: the engineering kit's spanner (T109), a long flat iron handle and an open jaw, its hex nut-end at the
    near end; the same tool TrainKit hangs on the cab's rack.

Axes: Blender +Z up, +Y along the haft. Modelled here and baked (tools/models/make).
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


def materials():
    return {
        "iron": make.lib("rust_heavy", 4.0, tint=(0.42, 0.38, 0.35), rough=0.5, metal=0.7),
        "paint": make.lib("paint_black", 5.0, tint=(0.8, 0.8, 0.8), rough=0.45),
        "steel": make.lib("iron_plate", 6.0, tint=(0.72, 0.7, 0.68), rough=0.3, metal=0.85),
        "wood": make.lib("wood_sleeper", 8.0, tint=(0.66, 0.5, 0.36), rough=0.55),
    }


def build(name, fn, what, budget=900, grip=0.0):
    """`grip`: where along the haft the fist holds it one-handed, if not at the modelled origin (m along +Y)."""
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    m = materials()
    parts = fn(m)
    for o in parts + list(make.LOW):
        o.data.transform(Matrix.Translation((0, -grip, 0)))
    low = cook.bake_down(parts, name + "_low", 600, colour=None, size=512, cage=0.003, reach=0.01, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.5, made=make.provenance("hand_tools", what))


def crowbar(m):
    # The bar: hex, from the chisel 0.14 m behind the fist to where it bends into the goose-neck 0.62 m ahead.
    p = [make.cyl((0, -0.12, 0), (0, 0.62, 0), 0.013, m["paint"], n=6, bevel=0.002, name="bar", low=6)]
    # Where it's held the paint's worn to the iron: a band of bare bar round the fist.
    p.append(make.cyl((0, -0.06, 0), (0, 0.08, 0), 0.0134, m["iron"], n=6, bevel=0.001, name="worn", low=0))
    # The chisel end: tapered flat.
    p.append(make.box((0, -0.15, 0), (0.014, 0.03, 0.006), m["iron"], bevel=0.002, name="chisel",
                      rot=None))
    # The goose-neck: the bar bent round through a half circle (r 0.05) back under itself, then the claw.
    c = Vector((0, 0.62, -0.05))
    pts = [c + Vector((0, math.sin(a) * 0.05, math.cos(a) * 0.05)) for a in [k * math.pi / 6 for k in range(7)]]
    p += make.pipe(pts, 0.013, m["paint"], name="neck", n=6, low=6)
    end = pts[-1]
    claw_dir = Vector((0, -0.35, -1)).normalized()
    for sx in (-1, 1):
        p.append(make.cyl(end + Vector((sx * 0.006, 0, 0)), end + claw_dir * 0.05 + Vector((sx * 0.008, 0, 0)), 0.006,
                          m["iron"], n=6, bevel=0.001, name="claw", r1=0.002, low=4))
    return p


def shovel(m):
    # crew.py's shovel, centred on the fist: the haft from the D-grip 0.62 m back to the blade's socket 0.42 m ahead.
    p = [make.cyl((0, -0.62, 0), (0, 0.42, 0), 0.017, m["wood"], n=10, bevel=0.002, name="haft", low=6, r1=0.019)]
    # The D-grip: a crossbar and its two cheeks.
    p.append(make.cyl((-0.05, -0.7, 0), (0.05, -0.7, 0), 0.014, m["wood"], n=8, name="grip", low=6))
    for sx in (-1, 1):
        p.append(make.cyl((sx * 0.012, -0.6, 0), (sx * 0.05, -0.7, 0), 0.008, m["iron"], n=6, name="cheek", low=4))
    # The socket over the haft's end, riveted, and the blade: a broad scoop, its sides turned up, its lip worn.
    p.append(make.cyl((0, 0.36, 0), (0, 0.46, 0), 0.022, m["iron"], n=10, name="socket", low=6))
    p.append(make.box((0, 0.6, 0.004), (0.13, 0.15, 0.004), m["iron"], bevel=0.002, name="blade"))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.128, 0.6, 0.03), (0.004, 0.15, 0.028), m["iron"], bevel=0.002, name="side"))
    p.append(make.box((0, 0.452, 0.025), (0.13, 0.004, 0.024), m["iron"], bevel=0.002, name="back"))
    p.append(make.box((0, 0.75, 0.004), (0.125, 0.006, 0.0045), m["steel"], bevel=0.002, name="lip", low=False))
    for x in (-0.012, 0.012):
        p.append(make.nail((x, 0.41, 0.022), (0, 0, 1), m["steel"], r=0.004))
    return p


def wrench(m):
    # A long flat handle from the nut end 0.12 m back of the fist to the jaw 0.42 m ahead; the open jaw's two lips.
    p = [make.box((0, 0.15, 0), (0.016, 0.28, 0.006), m["iron"], bevel=0.003, name="handle")]
    p.append(make.cyl((0, -0.15, -0.007), (0, -0.15, 0.007), 0.024, m["iron"], n=6, bevel=0.002, name="ring", low=6))
    p.append(make.box((0, 0.46, 0), (0.04, 0.03, 0.008), m["iron"], bevel=0.003, name="head"))
    for sx in (-1, 1):
        p.append(make.box((sx * 0.03, 0.51, 0), (0.012, 0.035, 0.008), m["steel"], bevel=0.003, name="jaw"))
    return p


build("tool_crowbar", crowbar, "the crowbar")
# Carried one-handed up by the D-grip, the blade trailing low (two-handed at the firebox, it's crew.py's own shovel).
build("tool_shovel", shovel, "the fireman's shovel", grip=-0.5)
build("tool_wrench", wrench, "the engineering kit's wrench")
