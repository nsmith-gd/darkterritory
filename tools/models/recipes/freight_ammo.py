"""Facility freight (BodyKind.Cargo), ammunition from a military depot: six olive-painted ammunition boxes stacked
two by three, each with its rope beckets and stencil, lashed together with two canvas straps. 0.88 m, the size of
the body in the sim. Centred on it."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

H = 0.44
cook.reset()
olive = make.lib("paint_olive", 0.7, tint=(0.95, 1.0, 0.92), rough=0.6)
wood = make.lib("wood_sleeper", 2.2, tint=(0.9, 0.82, 0.68))
canvas = make.lib("wool", 5.0, tint=(0.85, 0.78, 0.6), rough=0.95)
rope = make.lib("wool", 7.0, tint=(0.8, 0.68, 0.5), rough=0.95)
ink = make.flat("ink_yellow", (0.55, 0.45, 0.12), rough=0.8)
brass = make.lib("brass", 3.0, rough=0.35, metal=0.8)
parts = []
w, h = H, 2 * H / 3  # each box: half the width, a third of the height, the full depth
for i, x in enumerate((-H / 2, H / 2)):
    for j in range(3):
        z = -H + h / 2 + j * h
        c = (x, 0, z)
        parts.append(make.box(c, (w / 2 - 0.006, H - 0.01, h / 2 - 0.006), olive, bevel=0.008, name=f"ammo_{i}{j}"))
        # The lid's lip, the hinge side, the beckets at each end.
        parts.append(make.box((x, 0, z + h / 2 - 0.02), (w / 2 - 0.002, H - 0.006, 0.012), olive, bevel=0.004, name="lip"))
        for sy in (-1, 1):
            parts.append(make.torus((x, sy * (H - 0.004), z), (0, 1, 0), 0.035, 0.007, rope, name="becket", low=False))
            parts.append(make.box((x, sy * (H - 0.002), z + 0.04), (0.02, 0.004, 0.012), brass, bevel=0.002, name="catch", low=False))
        parts.append(make.stencil(".303 BALL", (x, -H - 0.002, z + 0.015), (0, -1, 0), (0, 0, 1), 0.05, ink))
        parts.append(make.stencil("1000 RDS  MkVII", (x, -H - 0.002, z - 0.035), (0, -1, 0), (0, 0, 1), 0.03, ink))
        parts.append(make.stencil(".303 BALL", (x, H + 0.002, z + 0.015), (0, 1, 0), (0, 0, 1), 0.05, ink))
# Two canvas straps round the stack.
for y in (-0.22, 0.22):
    parts.append(make.box((0, y, 0), (H + 0.006, 0.03, H + 0.006), canvas, bevel=0.003, name="strap"))
make.centre_on_origin(parts)
low = cook.bake_down(parts, "freight_ammo_low", 600, colour=None, size=1024, cage=0.004, reach=0.012, low=make.LOW)[0]
cook.finish("freight_ammo", [low], budget=800, grime=0.4, made=make.provenance("freight_ammo", "facility freight: ammunition"))
