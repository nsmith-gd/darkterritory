"""Two-man freight (BodyKind.Heavy, spec D.2 "heavy items need two"): a long case, 1.43 x 0.88 x 0.88 m (the
body's size at facilities.json heavy.radius 0.55), iron-banded three times, a rope grip at each end for the two
who carry it, stencilled for it. Baked onto its shell. Centred on the body."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

R = 0.55
X, Y, Z = R * 1.3, R * 0.8, R * 0.8
T = 0.024
cook.reset()
boards = make.lib("wood_sleeper", 2.0, tint=(1.45, 1.25, 1.0))
iron = make.lib("rust_heavy", 2.5, tint=(0.5, 0.46, 0.44), rough=0.45, metal=0.7)
rope = make.lib("wool", 7.0, tint=(0.8, 0.68, 0.5), rough=0.95)
ink = make.flat("ink", (0.02, 0.02, 0.018), rough=0.9)
red = make.flat("ink_red", (0.38, 0.04, 0.02), rough=0.9)
parts = []
for sy in (-1, 1):
    parts += make.planks((-X, sy * Y - T, -Z), (X, sy * Y, Z), 2, 5, boards, name=f"side{sy}", wobble=0.002, seed=81 + sy)
for sz in (-1, 1):
    parts += make.planks((-X, -Y + T, sz * Z - T), (X, Y - T, sz * Z), 1, 5, boards, name=f"lid{sz}", wobble=0.002, seed=85 + sz)
for sx in (-1, 1):
    parts += make.planks((sx * X - T, -Y + T, -Z + T), (sx * X, Y - T, Z - T), 2, 4, boards, name=f"end{sx}", wobble=0.002, seed=89 + sx)
make.low_box((0, 0, 0), (X, Y, Z), boards)
S = 0.005
for x in (-X * 0.78, 0, X * 0.78):
    parts.append(make.box((x, 0, 0), (0.035, Y + S, Z + S), iron, bevel=0.003, name="band"))
    for sy in (-1, 1):
        for z in (-Z * 0.6, 0, Z * 0.6):
            parts.append(make.nail((x, sy * (Y + S), z), (0, sy, 0), iron, r=0.008))
# The grips: a loop of rope through each end, for a man at each.
for sx in (-1, 1):
    parts.append(make.torus((sx * (X + 0.05), 0, 0.1), (0, 0, 1), 0.08, 0.016, rope, name="grip"))
    parts.append(make.box((sx * (X + 0.012), 0, 0.1), (0.012, 0.1, 0.03), iron, bevel=0.003, name="grip_plate", low=False))
for sy in (-1, 1):
    parts.append(make.stencil("MACHINERY", (0, sy * (Y + S), 0.12), (0, sy, 0), (0, 0, 1), 0.12, ink))
    parts.append(make.stencil("TWO MEN LIFT  -  1 TON", (0, sy * (Y + S), -0.12), (0, sy, 0), (0, 0, 1), 0.065, red))
make.centre_on_origin(parts)
low = cook.bake_down(parts, "heavy_crate_low", 700, colour=None, size=1024, cage=0.004, reach=0.012, low=make.LOW)[0]
cook.finish("heavy_crate", [low], budget=900, grime=0.5, made=make.provenance("heavy_crate", "two-man freight"))
