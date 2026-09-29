"""Contact sheets for review: every tiling texture repeated 2x2 at 1:1 so a seam shows as a
cross through the middle, cards and atlases at 1:1 over a night-blue checker (to judge the
cutout), and the spec map (R spec, G gloss, B emissive) beside each."""
from __future__ import annotations

from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

LABEL = 14
SIDE = 128
PAD = 6


def _font(size=12):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",):
        if Path(p).exists():
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def _backdrop(w, h):
    ys, xs = np.mgrid[0:h, 0:w]
    chk = ((xs // 16 + ys // 16) % 2).astype(np.float32)
    c = np.array([28, 32, 40], np.float32) + chk[..., None] * 10
    return c


def _composite(rgba, bg=None):
    a = rgba[..., 3:4].astype(np.float32) / 255
    rgb = rgba[..., :3].astype(np.float32)
    if bg is None:
        bg = _backdrop(rgba.shape[1], rgba.shape[0])
    return (rgb * a + bg * (1 - a)).astype(np.uint8)


def _cell(out_dir: Path, e: dict):
    d = np.asarray(Image.open(out_dir / e["diffuse"]).convert("RGBA"))
    s = np.asarray(Image.open(out_dir / e["spec"]).convert("RGB"))
    if e.get("tiling", True):
        d = np.tile(d, (2, 2, 1))
    img = _composite(d)
    # Spec thumbnail: nearest-neighbour fit into SIDE x SIDE.
    sh, sw = s.shape[:2]
    k = max(1, int(np.ceil(max(sh, sw) / SIDE)))
    thumb = s[::k, ::k]
    return img, thumb


def contact_sheets(out_dir: Path, entries: list[dict], path: Path, per_page=12, cols=4):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    font = _font()
    cells = []
    for e in entries:
        img, thumb = _cell(out_dir, e)
        cells.append((e, img, thumb))
    # Big items (sky) get their own page; the rest are paged in a grid of 512 cells.
    small = [c for c in cells if c[1].shape[1] <= 1024 and c[1].shape[0] <= 1024]
    big = [c for c in cells if c not in small]
    pages = [small[i:i + per_page] for i in range(0, len(small), per_page)]
    written = []
    for pi, page in enumerate(pages):
        cw = 512 + SIDE + PAD * 3
        ch = 1024 + LABEL + PAD * 2
        rows = (len(page) + cols - 1) // cols
        # Row height: tallest cell in that row.
        heights = []
        for r in range(rows):
            heights.append(max(c[1].shape[0] for c in page[r * cols:(r + 1) * cols]) + LABEL + PAD * 2)
        sheet = Image.new("RGB", (cw * cols, sum(heights)), (12, 12, 12))
        dr = ImageDraw.Draw(sheet)
        y0 = 0
        for r in range(rows):
            for ci, (e, img, thumb) in enumerate(page[r * cols:(r + 1) * cols]):
                x = ci * cw + PAD
                sheet.paste(Image.fromarray(img), (x, y0 + LABEL + PAD))
                sheet.paste(Image.fromarray(thumb), (x + min(img.shape[1], 512) + PAD, y0 + LABEL + PAD))
                tm = e.get("tileMetres")
                dr.text((x, y0 + 1), f"{e['name']}  {tm if tm else '-'} m  {e['family']}", fill=(220, 220, 200), font=font)
            y0 += heights[r]
        p = path.with_name(f"{path.stem}-{pi + 1}{path.suffix}")
        sheet.save(p)
        written.append(p)
    for bi, (e, img, thumb) in enumerate(big):
        sheet = Image.new("RGB", (img.shape[1], img.shape[0] + LABEL + SIDE + PAD * 2), (12, 12, 12))
        sheet.paste(Image.fromarray(img), (0, LABEL))
        sheet.paste(Image.fromarray(thumb), (0, LABEL + img.shape[0] + PAD))
        ImageDraw.Draw(sheet).text((2, 1), e["name"], fill=(220, 220, 200), font=font)
        p = path.with_name(f"{path.stem}-{e['name']}{path.suffix}")
        sheet.save(p)
        written.append(p)
    # Overview: everything small at half size on one sheet (the file named on the command line).
    thumbs = []
    for e, img, _ in small:
        im = Image.fromarray(img)
        im = im.resize((im.width // 2, im.height // 2), Image.BOX)
        thumbs.append((e, im))
    ocols = 8
    ow = 256 + PAD
    orows = (len(thumbs) + ocols - 1) // ocols
    rowh = []
    for r in range(orows):
        rowh.append(max(t[1].height for t in thumbs[r * ocols:(r + 1) * ocols]) + LABEL + PAD)
    ov = Image.new("RGB", (ow * ocols, sum(rowh)), (12, 12, 12))
    dr = ImageDraw.Draw(ov)
    y0 = 0
    for r in range(orows):
        for ci, (e, im) in enumerate(thumbs[r * ocols:(r + 1) * ocols]):
            ov.paste(im, (ci * ow, y0 + LABEL))
            dr.text((ci * ow + 1, y0 + 1), e["name"], fill=(220, 220, 200), font=font)
        y0 += rowh[r]
    ov.save(path)
    written.insert(0, path)
    for p in written:
        print("sheet:", p)
