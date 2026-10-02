"""Steam capsules and key art at L0 (GDD §36: the Coming Soon page): a frame of the game itself (dt screenshot, out/store/
shots), darkened to the edges, the title set over it in the shops' plain bold, the lamp left to lead the eye. Steam's
sizes: header 920x430, small 462x174, main 1232x706, vertical 748x896, library 600x900. Into out/store (not shipped).

    dotnet run --project src/DarkTerritory.Cli -- screenshot --working --threats --view trackside --width 1232 --height 706 \
        --out out/store/shots/trackside.png
    python3 tools/art/store/capsule.py [out/store/shots/trackside.png]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
FONT = "/usr/share/fonts/truetype/dejavu/DejaVuSerifCondensed-Bold.ttf"
SIZES = {"header": (920, 430), "small": (462, 174), "main": (1232, 706), "vertical": (748, 896), "library": (600, 900)}


def fit(shot, w, h, focus):
    """The shot cropped to w:h round `focus` (0..1 across, down), then scaled."""
    sw, sh = shot.size
    scale = max(w / sw, h / sh)
    big = shot.resize((int(sw * scale + 0.5), int(sh * scale + 0.5)), Image.LANCZOS)
    x = int(min(max(focus[0] * big.width - w / 2, 0), big.width - w))
    y = int(min(max(focus[1] * big.height - h / 2, 0), big.height - h))
    return big.crop((x, y, x + w, y + h))


def capsule(shot, w, h, focus):
    img = np.asarray(fit(shot, w, h, focus).convert("RGB"), np.float32) / 255
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    v = ((xx / w - 0.5) ** 2 * 1.4 + (yy / h - 0.45) ** 2) ** 0.5
    img *= np.clip(1.15 - v * 1.1, 0.25, 1)[..., None]
    out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8))
    # The title: a dark band under it so it reads on any frame, the letters a little worn.
    title, sub = "DARK TERRITORY", "the line runs till dawn"
    size = int(min(w * 0.11, h * 0.2))
    f = ImageFont.truetype(FONT, size) if os.path.exists(FONT) else ImageFont.load_default()
    fs = ImageFont.truetype(FONT, max(10, size // 3)) if os.path.exists(FONT) else f
    d = ImageDraw.Draw(out, "RGBA")
    # As big as fits across with a margin.
    while d.textbbox((0, 0), title, font=f)[2] > w * 0.9 and size > 10:
        size = int(size * 0.92)
        f = ImageFont.truetype(FONT, size) if os.path.exists(FONT) else f
    tb = d.textbbox((0, 0), title, font=f)
    tw, th = tb[2] - tb[0], tb[3] - tb[1]
    ty = int(h * (0.72 if h < w else 0.78)) - th // 2
    band = Image.new("L", (w, h), 0)
    ImageDraw.Draw(band).rectangle([0, ty - th * 0.5, w, ty + th * 1.9], fill=150)
    out = Image.composite(Image.new("RGB", (w, h), (8, 9, 11)), out, band.filter(ImageFilter.GaussianBlur(th * 0.5)))
    d = ImageDraw.Draw(out, "RGBA")
    d.text(((w - tw) / 2 - tb[0], ty - tb[1]), title, font=f, fill=(222, 214, 196, 255))
    if h > 200:
        sb = d.textbbox((0, 0), sub, font=fs)
        d.text(((w - (sb[2] - sb[0])) / 2 - sb[0], ty + th * 1.25), sub, font=fs, fill=(196, 150, 96, 230))
    return out


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "store", "shots", "trackside.png")
    shot = Image.open(src)
    out = os.path.join(ROOT, "out", "store")
    os.makedirs(out, exist_ok=True)
    # The train's lamp sits right of middle, a little above it in the trackside view: keep it in every crop.
    for name, (w, h) in SIZES.items():
        capsule(shot, w, h, (0.6, 0.38)).save(os.path.join(out, f"capsule_{name}.png"))
    print(f"[dt] capsules {list(SIZES)} -> out/store/capsule_*.png")


if __name__ == "__main__":
    main()
