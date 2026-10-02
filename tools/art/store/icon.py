"""The game's icon (GDD §36: the window, the taskbar, Steam's library): the headlamp's ringed glow on black, the thing you
see of the train first, and the shops' stencilled DT under it (tools/art stencil_numerals' font). Written at 512 px and
down to the sizes a window and Steam ask for, into content/art/ui (the window's) and out/store (Steam's, not shipped).

    python3 tools/art/store/icon.py
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def icon(S=512):
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    cx, cy = S / 2, S * 0.4
    r = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / S
    # The lamp: a hot core, its glass's ring, the glow thrown into the fog round it, the sooted iron of the lamp's rim.
    core = np.exp(-(r / 0.07) ** 2)
    ring = np.exp(-((r - 0.16) / 0.012) ** 2)
    glow = np.exp(-(r / 0.3) ** 2) * 0.55
    rim = np.exp(-((r - 0.205) / 0.02) ** 2)
    lamp = np.array([1.0, 0.74, 0.42], np.float32)
    img = np.zeros((S, S, 3), np.float32) + np.array([0.035, 0.04, 0.05], np.float32)
    img += (glow[..., None] * 0.55 + ring[..., None] * 0.6 + core[..., None] * 1.4) * lamp
    img = img * (1 - 0.7 * rim[..., None]) + rim[..., None] * np.array([0.12, 0.08, 0.06])
    # Grain: soot on the glass.
    rng = np.random.default_rng(7)
    img *= (0.94 + 0.12 * rng.random((S, S, 1))).astype(np.float32)
    out = Image.fromarray((np.clip(img, 0, 1) ** (1 / 1.1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    # The stencil: DT in the shops' white, the bridges cut through it.
    txt = Image.new("L", (S, S), 0)
    d = ImageDraw.Draw(txt)
    f = ImageFont.truetype(FONT, int(S * 0.24)) if os.path.exists(FONT) else ImageFont.load_default()
    box = d.textbbox((0, 0), "DT", font=f)
    d.text(((S - (box[2] - box[0])) / 2 - box[0], S * 0.68 - box[1]), "DT", fill=255, font=f)
    t = np.asarray(txt, np.float32) / 255
    band = (yy - S * 0.68) / (box[3] - box[1])
    t *= 1 - (np.abs(band - 0.5) < 0.035)
    paint = Image.new("RGBA", (S, S), (205, 198, 182, 255))
    out = Image.composite(paint, out, Image.fromarray((t * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(0.6)))
    # Rounded corners, a hard black edge (a small icon reads by its outline).
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=S // 9, fill=255)
    out.putalpha(mask)
    return out


def main():
    big = icon(512)
    ui = os.path.join(ROOT, "content", "art", "ui")
    store = os.path.join(ROOT, "out", "store")
    os.makedirs(ui, exist_ok=True)
    os.makedirs(store, exist_ok=True)
    big.resize((256, 256), Image.LANCZOS).save(os.path.join(ui, "icon.png"))
    for s in (16, 32, 48, 64, 128, 184, 256, 512):
        big.resize((s, s), Image.LANCZOS).save(os.path.join(store, f"icon_{s}.png"))
    big.save(os.path.join(store, "icon.ico"), sizes=[(16, 16), (32, 32), (48, 48), (256, 256)])
    print("[dt] icon -> content/art/ui/icon.png, out/store/icon_*.png, icon.ico")


if __name__ == "__main__":
    sys.exit(main())
