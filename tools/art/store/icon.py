"""The game's icon (GDD §36: the window, the taskbar, Steam's library): the engine itself, head on in the fog at night, its
smokebox face and chimney black against the haze and its headlamp blazing, the smoke going up: the thing you see of the
train first, and what the whole game is (§4: "the train is the protagonist"). A square frame of the real model rendered
headless by dt (tools/art/store/screens.sh renders it: out/store/shots/icon.png), graded, vignetted to black so the
silhouette holds at 16 px, the corners rounded. Written at 512 px and down to the sizes a window and Steam ask for, into
content/art/ui (the window's) and out/store (Steam's, not shipped).

    tools/art/store/screens.sh                                 # renders the frame, then this
    python3 tools/art/store/icon.py out/store/shots/icon.png   # just the icon, from a frame already rendered
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))


def icon(frame, S=512):
    src = Image.open(frame).convert("RGB")
    # The engine's face fills the middle: crop to it (the frame is square, the engine centred).
    w, h = src.size
    c = min(w, h)
    src = src.crop(((w - c) // 2, (h - c) // 2, (w - c) // 2 + c, (h - c) // 2 + c)).resize((S, S), Image.LANCZOS)
    img = np.asarray(src, np.float32) / 255
    yy, xx = np.mgrid[0:S, 0:S].astype(np.float32)
    # Graded: the haze pushed down and cooled, the lamp and its glow kept hot, so the read is light on black.
    lum = img.mean(-1, keepdims=True)
    img = np.clip((img - 0.06) * 1.35, 0, 1) * (0.75 + 0.25 * np.clip(lum * 3, 0, 1))
    img = img * np.array([0.93, 0.96, 1.04], np.float32) * (1 - lum) + img * np.array([1.08, 1.0, 0.86], np.float32) * lum
    # Vignetted to black round the edge: the silhouette's outline holds at a small size.
    r = np.sqrt((xx - S / 2) ** 2 + (yy - S * 0.46) ** 2) / S
    img *= np.clip(1.25 - (r / 0.62) ** 2.2, 0, 1)[..., None]
    out = Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8), "RGB").convert("RGBA")
    # Rounded corners, a thin dark rim.
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=S // 9, fill=255)
    rim = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(rim).rounded_rectangle([1, 1, S - 2, S - 2], radius=S // 9, outline=(10, 9, 8, 255), width=max(2, S // 96))
    out = Image.alpha_composite(out, rim)
    out.putalpha(mask)
    return out


def main():
    frame = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "out", "store", "shots", "icon.png")
    big = icon(frame, 512)
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
