"""Facility freight (BodyKind.Cargo), machine parts: a heavy case of boards, 0.88 m, strapped with iron both ways,
corner irons, and stencilled for the foundry. Baked onto its shell. Centred on the body."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

H, T = 0.44, 0.022
cook.reset()
boards = make.lib("wood_sleeper", 2.0, tint=(1.6, 1.38, 1.1))
iron = make.lib("rust_heavy", 2.5, tint=(0.55, 0.5, 0.47), rough=0.45, metal=0.7)
ink = make.flat("ink", (0.02, 0.02, 0.018), rough=0.9)
red = make.flat("ink_red", (0.35, 0.03, 0.02), rough=0.9)
parts = []
for sy in (-1, 1):
    parts += make.planks((-H, sy * H - T, -H), (H, sy * H, H), 2, 5, boards, name=f"side{sy}", wobble=0.002, seed=21 + sy)
for sx in (-1, 1):
    parts += make.planks((sx * H - T, -H + T, -H), (sx * H, H - T, H), 1, 5, boards, name=f"end{sx}", wobble=0.002, seed=25 + sx)
for sz in (-1, 1):
    parts += make.planks((-H + T, -H + T, sz * H - T), (H - T, H - T, sz * H), 1, 5, boards, name=f"lid{sz}", wobble=0.0015, seed=29 + sz)
make.low_box((0, 0, 0), (H, H, H), boards)
# Strap iron round it both ways, riveted where it crosses the edges; corner irons.
S = 0.004
for x in (-0.24, 0.24):
    parts.append(make.box((x, 0, 0), (0.022, H + S, H + S), iron, bevel=0.002, name="strap_x"))
for z in (-0.24, 0.24):
    parts.append(make.box((0, 0, z), (H + S, H + S, 0.022), iron, bevel=0.002, name="strap_z"))
for sx in (-1, 1):
    for sy in (-1, 1):
        for sz in (-1, 1):
            c = (sx * (H - 0.03), sy * (H - 0.03), sz * (H - 0.03))
            parts.append(make.box(c, (0.035 + S, 0.035 + S, 0.035 + S), iron, bevel=0.004, name="corner"))
            for n in ((sx, 0, 0), (0, sy, 0), (0, 0, sz)):
                at = tuple(c[i] + n[i] * (0.035 + S) for i in range(3))
                parts.append(make.nail(at, n, iron, r=0.007))
for sy in (-1, 1):
    parts.append(make.stencil("MACHINE PARTS", (0, sy * (H + S), 0.1), (0, sy, 0), (0, 0, 1), 0.085, ink))
    parts.append(make.stencil("FOUNDRY No.2  -  FRAGILE", (0, sy * (H + S), -0.1), (0, sy, 0), (0, 0, 1), 0.05, red))
for sx in (-1, 1):
    parts.append(make.stencil("^ THIS SIDE UP ^", (sx * (H + S), 0, 0.1), (sx, 0, 0), (0, 0, 1), 0.05, ink))
make.centre_on_origin(parts)
low = cook.bake_down(parts, "freight_parts_low", 600, colour=None, size=1024, cage=0.004, reach=0.012, low=make.LOW)[0]
cook.finish("freight_parts", [low], budget=800, grime=0.5, made=make.provenance("freight_parts", "facility freight: machine parts"))
