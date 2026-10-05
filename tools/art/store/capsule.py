"""Steam capsules and key art (GDD §36: the Coming Soon page): a frame of the game itself (dt screenshot, out/store/shots/
key.png: the engine looming out of the fog off its front quarter, its lamp blazing, the train running back into the haze),
darkened to the edges, and the logotype: the title on a station's nameboard, as the game's own title screen sets it. Steam's
sizes: header 920x430, small 462x174, main 1232x706, vertical 748x896, library 600x900. Into out/store (not shipped).

    tools/art/store/screens.sh                                    # renders the key frame, then this
    python3 tools/art/store/capsule.py [out/store/shots/key.png]
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
    # The logotype: the title on a station's nameboard, as the game's own front end sets it (Game/UiStyle.Nameboard):
    # cream letters on a dark enamel board, a cream line inset round it, a bolt at each end. Over the upper left in the
    # wide sizes (the engine and its lamp hold the right), across the top in the tall ones.
    title, sub = "DARK TERRITORY", "THE LINE RUNS TILL DAWN"
    tall = h > w
    size = int((w * 0.06) if not tall else (w * 0.072))
    f = ImageFont.truetype(FONT, size) if os.path.exists(FONT) else ImageFont.load_default()
    fs = ImageFont.truetype(FONT, max(9, size // 3)) if os.path.exists(FONT) else f
    d = ImageDraw.Draw(out, "RGBA")
    tb = d.textbbox((0, 0), title, font=f)
    tw, th = tb[2] - tb[0], tb[3] - tb[1]
    pad = max(4, int(th * 0.45))
    bw, bh = tw + pad * 4, th + pad * 2
    bx = int(w * 0.05) if not tall else (w - bw) // 2
    by = int(h * (0.1 if not tall else 0.07))
    if h < 200:
        bx, by = int(w * 0.04), (h - bh) // 2
    # A soft dark under it, so it sits on any frame; then the board.
    shade = Image.new("L", (w, h), 0)
    ImageDraw.Draw(shade).rectangle([bx - pad, by - pad, bx + bw + pad, by + bh + pad * 3], fill=140)
    out = Image.composite(Image.new("RGB", (w, h), (6, 7, 9)), out, shade.filter(ImageFilter.GaussianBlur(pad * 2)))
    d = ImageDraw.Draw(out, "RGBA")
    d.rectangle([bx + 3, by + 3, bx + bw + 3, by + bh + 3], fill=(0, 0, 0, 150))
    d.rectangle([bx, by, bx + bw, by + bh], fill=(22, 25, 31, 240))
    line = max(1, pad // 4)
    d.rectangle([bx + pad // 2, by + pad // 2, bx + bw - pad // 2, by + bh - pad // 2], outline=(219, 209, 181, 230), width=line)
    for cx in (bx + pad, bx + bw - pad):
        r = max(2, pad // 4)
        d.ellipse([cx - r, by + bh / 2 - r, cx + r, by + bh / 2 + r], fill=(178, 143, 82, 255))
    d.text((bx + pad * 2 - tb[0], by + pad - tb[1]), title, font=f, fill=(222, 212, 186, 255))
    if h > 200:
        sb = d.textbbox((0, 0), sub, font=fs)
        sx = bx + (bw - (sb[2] - sb[0])) / 2 - sb[0]
        d.text((sx, by + bh + pad * 0.8 - sb[1]), sub, font=fs, fill=(214, 160, 92, 235))
    return out


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "store", "shots", "key.png")
    shot = Image.open(src)
    out = os.path.join(ROOT, "out", "store")
    os.makedirs(out, exist_ok=True)
    # The engine's lamp sits right of middle, a little above it in the key frame: keep it in every crop.
    for name, (w, h) in SIZES.items():
        capsule(shot, w, h, (0.66, 0.42)).save(os.path.join(out, f"capsule_{name}.png"))
    print(f"[dt] capsules {list(SIZES)} -> out/store/capsule_*.png")


if __name__ == "__main__":
    main()
