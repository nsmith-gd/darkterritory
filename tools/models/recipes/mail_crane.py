"""The lineside mail crane (the playtest's rewards; GreyboxScene.Crane's: a post a little out from the track, an arm
reaching in over the cess, the bag hung at a car's doorway height for the hook out of a side door to take):

  * mail_crane: a tall timber post on a stone footing, braced, with its two iron arms swung out toward the line, the bag's
    clamps at their ends, a step-iron ladder up it and a tin flag. Its origin is the post's foot; +X is toward the line;
    the arms' clamps reach 0.85 m in (sockets bag_top and bag_foot: where the bag's ends are held);
  * mail_bag: the bag: a canvas sack strapped round in leather, tied at its neck, 0.6 m long, its middle at the origin,
    pale enough for the scene to tint by what's in it (mail grey, coal black, rounds olive, spares brass).

Axes: Blender +Z up. Modelled here and baked (tools/models/make).

    tools/models/build.sh mail_crane
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix  # noqa: E402

REACH = 0.85
BAG_TOP, BAG_FOOT = 2.65, 2.05


def materials():
    return {
        "post": make.lib("wood_sleeper", 1.5, tint=(0.62, 0.52, 0.42), rough=0.7),
        "stone": make.lib("stone_block", 1.2, tint=(0.7, 0.7, 0.68), rough=0.85),
        "iron": make.lib("rust_heavy", 3.0, tint=(0.42, 0.38, 0.35), rough=0.55, metal=0.6),
        "paint": make.lib("paint_oxide", 3.0, tint=(1.0, 0.55, 0.45), rough=0.55),
        "canvas": make.lib("wool", 4.0, tint=(0.82, 0.8, 0.74), rough=0.95),
        "strap": make.lib("leather", 6.0, tint=(0.5, 0.4, 0.3), rough=0.7),
        "brass": make.lib("brass", 4.0, rough=0.35, metal=0.8),
        "ink": make.flat("ink_black", (0.08, 0.07, 0.06), rough=0.8),
    }


def crane(m):
    p = [make.box((0, 0, 0.15), (0.3, 0.3, 0.15), m["stone"], bevel=0.02, name="footing")]
    p.append(make.box((0, 0, 0.3 + 1.7), (0.09, 0.09, 1.7), m["post"], bevel=0.012, name="post"))
    p.append(make.cyl((0, 0, 3.7), (0, 0, 3.78), 0.11, m["iron"], n=12, bevel=0.004, name="cap", low=6))
    # Two raking braces from the footing's back corners up the post.
    for sy in (-1, 1):
        p.append(make.box((-0.3, sy * 0.06, 0.9), (0.03, 0.03, 0.75), m["post"], bevel=0.006, name="brace",
                          rot=Matrix.Rotation(0.32, 4, "Y")))
    # The arms: iron bars swung out toward the line from collars on the post, each with the bag's clamp at its end.
    for z in (BAG_TOP + 0.08, BAG_FOOT - 0.08):
        p.append(make.box((0, 0, z), (0.12, 0.12, 0.03), m["iron"], bevel=0.004, name="collar"))
        p.append(make.box((REACH / 2, 0, z), (REACH / 2, 0.02, 0.018), m["iron"], bevel=0.004, name="arm"))
        p.append(make.cyl((REACH / 2 - 0.1, 0, z - 0.02), (0.08, 0, z - 0.25), 0.012, m["iron"], n=8, bevel=0, name="stay", low=4))
        for sy in (-1, 1):
            p.append(make.box((REACH, sy * 0.035, z), (0.015, 0.008, 0.04), m["iron"], bevel=0.002, name="clamp"))
    # Step irons up the post's side, and its tin flag at the top, the line's colour on it.
    for k in range(9):
        z = 0.7 + k * 0.33
        p.append(make.cyl((0.0, -0.09, z), (0.0, -0.2, z), 0.012, m["iron"], n=6, bevel=0, name="step", low=0))
    p.append(make.box((0, 0.2, 3.45), (0.012, 0.11, 0.08), m["paint"], bevel=0.003, name="flag"))
    p.append(make.stencil("MAIL", (0.014, 0.2, 3.45), (1, 0, 0), (0, 0, 1), 0.05, m["ink"]))
    return p


def bag(m):
    p = [make.cyl((0, 0, -0.27), (0, 0, 0.2), 0.16, m["canvas"], n=18, bevel=0.03, name="sack", r1=0.15, low=16)]
    p.append(make.cyl((0, 0, 0.2), (0, 0, 0.27), 0.15, m["canvas"], n=18, bevel=0, name="shoulder", r1=0.04, low=16))
    p.append(make.cyl((0, 0, 0.27), (0, 0, 0.32), 0.035, m["canvas"], n=10, bevel=0, name="neck", low=6))
    p.append(make.torus((0, 0, 0.28), (0, 0, 1), 0.04, 0.008, m["strap"], name="tie", low=False))
    for z in (-0.12, 0.08):
        p.append(make.torus((0, 0, z), (0, 0, 1), 0.163, 0.01, m["strap"], n=24, m=5, name="strap", low=False))
    # The strap down its length, the brass rings the crane's clamps take at each end.
    p.append(make.box((0, -0.162, 0), (0.025, 0.008, 0.28), m["strap"], bevel=0.003, name="long_strap"))
    for z in (-0.3, 0.34):
        p.append(make.torus((0, 0, z), (1, 0, 0), 0.03, 0.006, m["brass"], name="ring", low=False))
    p.append(make.stencil("G.P.O.", (0, -0.152, -0.02), (0, -1, 0), (0, 0, 1), 0.05, m["ink"]))
    return p


def build(name, fn, what, budget, sockets=None, centre=False):
    cook.reset()
    make.LOW.clear()
    make.USED.clear()
    make._mats.clear()
    parts = [o for o in fn(materials()) if o is not None]
    if centre:
        make.centre_on_origin(parts)
    low = cook.bake_down(parts, name + "_low", budget, colour=None, size=1024, cage=0.006, reach=0.03, low=list(make.LOW))[0]
    cook.finish(name, [low], budget=budget, grime=0.55, made=make.provenance("mail_crane", what), sockets=sockets)


PIECES = {
    "mail_crane": lambda: build("mail_crane", crane, "a lineside mail crane", 1600,
                                sockets={"bag_top": (REACH, 0, BAG_TOP), "bag_foot": (REACH, 0, BAG_FOOT)}),
    "mail_bag": lambda: build("mail_bag", bag, "a mail crane's bag", 700, centre=True),
}
want = set(cook.args()) or set(PIECES)
for name, fn in PIECES.items():
    if name in want:
        fn()
