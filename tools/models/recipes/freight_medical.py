"""Facility freight (BodyKind.Cargo), medical stores: a painted field-hospital chest, 0.88 m, the red cross on its
sides and lid, brass corners, leather handles, a hasp. Baked onto its shell. Centred on the body."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

H = 0.44
cook.reset()
paint = make.lib("paint_olive", 0.8, tint=(1.6, 1.55, 1.45), rough=0.65)  # grey-white paint over the olive
wood = make.lib("wood_sleeper", 2.0, tint=(1.1, 0.95, 0.78))
brass = make.lib("brass", 3.0, rough=0.35, metal=0.85)
leather = make.lib("leather", 3.0, tint=(0.7, 0.55, 0.42), rough=0.7)
red = make.flat("cross_red", (0.42, 0.03, 0.025), rough=0.8)
ink = make.flat("ink", (0.03, 0.03, 0.03), rough=0.9)
parts = []
# The chest's boards, painted; the lid's seam a hand below the top.
for sy in (-1, 1):
    parts += make.planks((-H, sy * H - 0.02, -H), (H, sy * H, H), 2, 3, paint, gap=0.003, name=f"side{sy}", seed=71 + sy)
for sx in (-1, 1):
    parts += make.planks((sx * H - 0.02, -H + 0.02, -H), (sx * H, H - 0.02, H), 2, 3, paint, gap=0.003, name=f"end{sx}", seed=75 + sx)
for sz in (-1, 1):
    parts.append(make.box((0, 0, sz * (H - 0.01)), (H - 0.02, H - 0.02, 0.01), paint, bevel=0.004, name="lid", low=False))
parts.append(make.box((0, 0, H - 0.14), (H + 0.004, H + 0.004, 0.004), make.flat("seam", (0.05, 0.05, 0.05)), bevel=0, name="seam", low=False))
make.low_box((0, 0, 0), (H, H, H), paint)
# Brass corner caps; leather handles at the ends; a hasp at the front.
for sx in (-1, 1):
    for sy in (-1, 1):
        for sz in (-1, 1):
            parts.append(make.box((sx * (H - 0.025), sy * (H - 0.025), sz * (H - 0.025)), (0.03, 0.03, 0.03), brass, bevel=0.006, name="cap", low=False))
    parts.append(make.torus((sx * (H + 0.025), 0, 0.12), (1, 0, 0), 0.07, 0.012, leather, name="handle"))
parts.append(make.box((0, -H - 0.006, H - 0.12), (0.035, 0.006, 0.06), brass, bevel=0.003, name="hasp", low=False))
# The red cross, painted on the sides and lid, and whose it is.
for n, u, at in (((0, -1, 0), (0, 0, 1), (0, -H - 0.002, -0.03)), ((0, 1, 0), (0, 0, 1), (0, H + 0.002, -0.03)),
                 ((0, 0, 1), (1, 0, 0), (0, 0, H + 0.002))):
    for arm in ((0.2, 0.06), (0.06, 0.2)):
        r = (u[1] * n[2] - u[2] * n[1], u[2] * n[0] - u[0] * n[2], u[0] * n[1] - u[1] * n[0])
        half = [0.001 + abs(r[i]) * arm[0] + abs(u[i]) * arm[1] for i in range(3)]
        parts.append(make.box(at, half, red, bevel=0, name="cross", low=False))
parts.append(make.stencil("MEDICAL STORES  -  R.A.M.C.", (0, -H - 0.002, -0.33), (0, -1, 0), (0, 0, 1), 0.045, ink))
make.centre_on_origin(parts)
low = cook.bake_down(parts, "freight_medical_low", 600, colour=None, size=1024, cage=0.004, reach=0.012, low=make.LOW)[0]
cook.finish("freight_medical", [low], budget=800, grime=0.45, made=make.provenance("freight_medical", "facility freight: medical stores"))
