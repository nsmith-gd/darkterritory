"""The walkie-talkie (BodyKind.Radio, T41): a field radio the size of its body (0.14 x 0.24 x 0.08 m): an olive
steel case with a speaker grille, a tuning dial and knobs, a strap loop, its aerial up, and a pinprick of a red lamp
(pure light) so a dropped one's found. Baked onto its game mesh. Centred on the body."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import cook  # noqa: E402
import make  # noqa: E402

W, H, D = 0.07, 0.12, 0.04  # half sizes: x across, z up, y through (engine: x, y, z)
cook.reset()
olive = make.lib("paint_olive", 3.0, tint=(0.9, 0.95, 0.85), rough=0.55)
black = make.lib("paint_black", 4.0, rough=0.5)
steel = make.lib("iron_plate", 6.0, tint=(0.8, 0.8, 0.8), rough=0.35, metal=0.8)
ink = make.flat("ink_white", (0.7, 0.68, 0.6), rough=0.8)
parts = [make.box((0, 0, 0), (W, D, H), olive, bevel=0.008, segments=3, name="case")]
# The face: a grille of slots, a dial window, two knobs, the press-to-talk bar on the side.
for i in range(6):
    parts.append(make.box((0, -D - 0.001, 0.02 + i * 0.012), (0.045, 0.002, 0.003), black, bevel=0.001, name="slot", low=False))
parts.append(make.box((0, -D - 0.001, -0.045), (0.05, 0.003, 0.018), black, bevel=0.002, name="dial"))
parts.append(make.box((0, -D - 0.004, -0.045), (0.044, 0.0005, 0.013), make.flat("dial_face", (0.55, 0.5, 0.36), rough=0.3), bevel=0, name="dial_face", low=False))
for x in (-0.035, 0.035):
    parts.append(make.cyl((x, -D, -0.09), (x, -D - 0.012, -0.09), 0.011, black, n=16, name="knob", low=8))
parts.append(make.box((W + 0.004, 0, 0.03), (0.005, 0.02, 0.04), black, bevel=0.003, name="ptt"))
parts.append(make.stencil("SET No.38", (0, -D - 0.002, 0.1), (0, -1, 0), (0, 0, 1), 0.016, ink))
# Aerial and its base; the strap loop; the lamp.
parts.append(make.cyl((0.04, 0, H), (0.04, 0, H + 0.015), 0.009, steel, n=12, name="aerial_base", low=6))
parts.append(make.cyl((0.04, 0, H + 0.015), (0.04, 0, H + 0.2), 0.0035, steel, n=8, name="aerial", low=4))
parts.append(make.torus((-0.04, 0, H + 0.01), (0, 1, 0), 0.018, 0.004, black, name="loop", low=(8, 4)))
# (Not centred on its bounds: the body is the case, which is already on the origin; the aerial stands above it.)
low = cook.bake_down(parts, "field_radio_low", 400, colour=None, size=512, cage=0.004, reach=0.012, low=make.LOW)[0]
lamp = cook.eyes_at([(-0.04, -D - 0.003, 0.095)], 0.004, colour=(1.0, 0.12, 0.06), name="lamp")
cook.finish("field_radio", [low] + lamp, budget=600, grime=0.35, made=make.provenance("field_radio", "the crew's walkie-talkie"))
