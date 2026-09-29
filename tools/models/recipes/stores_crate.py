"""The train's own stores (BodyKind.Crate): a nailed crate, 0.68 m, the size of its body in the sim.

Built board by board in the library's wood grain (wood_sleeper, pale for the boards, darker for the corner and lid
battens), with nail heads, rope grips at the ends and the railway's stencil, then baked down onto its plain game
mesh. Centred on the body.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Vector  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402

H = 0.34
T = 0.018  # board thickness
cook.reset()
# Plain grain (the library's wood_sleeper, which has no boards of its own printed in), pale deal for the boards and
# darker for the battens.
boards = make.lib("wood_sleeper", 2.2, tint=(1.5, 1.28, 0.98))
batten = make.lib("wood_sleeper", 2.6, tint=(0.95, 0.82, 0.66))
iron = make.lib("rust_heavy", 3.0, tint=(0.5, 0.48, 0.46), rough=0.5, metal=0.6)
ink = make.flat("ink", (0.02, 0.02, 0.018), rough=0.9)
rope = make.lib("wool", 6.0, tint=(0.75, 0.62, 0.45), rough=0.95)
parts = []
# Sides: boards running along, four to a face; the ends' boards stand upright, the lid and floor run across.
for sy in (-1, 1):
    parts += make.planks((-H, sy * H - T, -H), (H, sy * H, H), 2, 4, boards, name=f"side{sy}", wobble=0.002, seed=3 + sy)
for sx in (-1, 1):
    parts += make.planks((sx * H - T, -H + T, -H), (sx * H, H - T, H), 1, 4, boards, name=f"end{sx}", wobble=0.002, seed=7 + sx)
for sz in (-1, 1):
    parts += make.planks((-H + T, -H + T, sz * H - T), (H - T, H - T, sz * H), 1, 4, boards, name=f"lid{sz}", wobble=0.0015, seed=11 + sz)
# The game mesh is the crate's outside: the boards, their gaps and nails bake onto it.
make.low_box((0, 0, 0), (H, H, H), boards)
# Battens round the corners and across the lid, nailed.
B, W = 0.022, 0.05
for sx in (-1, 1):
    for sy in (-1, 1):
        parts.append(make.box((sx * (H + B / 2 - 0.004), sy * (H - W / 2 + 0.004), 0), (B / 2, W / 2, H + 0.004), batten, name="corner"))
for sy in (-1, 1):
    for z in (-H + 0.04, H - 0.04):
        parts.append(make.box((0, sy * (H + B / 2), z), (H - 0.002, B / 2, 0.032), batten, name="band"))
        for x in (-H + 0.06, -0.1, 0.1, H - 0.06):
            parts.append(make.nail((x, sy * (H + B), z), (0, sy, 0), iron))
for x in (-0.18, 0.18):
    parts.append(make.box((x, 0, H + B / 2), (0.035, H - 0.02, B / 2), batten, name="lidbatten"))
    for y in (-0.24, 0, 0.24):
        parts.append(make.nail((x, y, H + B), (0, 0, 1), iron))
# Rope grips through the ends.
for sx in (-1, 1):
    parts.append(make.torus((sx * (H + 0.03), 0, 0.08), (0, 1, 0), 0.06, 0.012, rope, name="grip"))
# The stencil: whose it is and what it's for.
for sy in (-1, 1):
    parts.append(make.stencil("D.T.& N. RY", (0, sy * (H + 0.001), 0.1), (0, sy, 0), (0, 0, 1), 0.12, ink))
    parts.append(make.stencil("STORES No.4", (0, sy * (H + 0.001), -0.1), (0, sy, 0), (0, 0, 1), 0.1, ink))

make.centre_on_origin(parts)
low = cook.bake_down(parts, "stores_crate_low", 900, colour=None, size=1024, cage=0.004, reach=0.01, low=make.LOW)[0]
cook.finish("stores_crate", [low], budget=1000, grime=0.5, made=make.provenance("stores_crate", "the train's stores crate"))
