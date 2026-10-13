#!/usr/bin/env python3
"""The towns' flags (queue #405, note 674): which cell of the `town_flags` atlas is whose, and a contact sheet of them.

    python3 tools/art/flags.py              # content/art/flags.json (the game reads it: Art/FlagKit)
    python3 tools/art/flags.py --sheet out/flags/sheet.png   # every flag, labelled, for review

The atlas itself is a texture like any other: `python3 tools/art/textures.py --only town_flags` (texgen/mat_flags.py).
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
ROOT = HERE.parents[1]

from texgen import mat_flags  # noqa: E402

TRADE_NAMES = {"coal": "pit town", "farm": "growing town", "foundry": "foundry town"}
CHARGES = {
    "hush": "the church bell, its clapper bound",
    "pairs": "two iron rings, linked",
    "count": "the tally, one mark too many",
    "shutters": "a lantern, its glass shuttered",
    "talkers": "the reading horn on its pole",
    "eyes": "an eye, open",
    "offerings": "a toy horse on its rockers",
    "rulebook": "the rule book open at rule 9",
    "banked": "the brazier, its fire banked, the poker bent",
    "crown": "the white line down the middle, a left boot",
    "backs": "a coat on its peg, hung back to front",
    "fours": "four lamps, one hook empty",
    "lastCar": "the van, its back half bitten away",
    "backGun": "the old gun, aimed back up the line",
}


def catalogue() -> dict:
    flags = []
    for c in mat_flags.CULTURES:
        for t in mat_flags.TRADES:
            i = mat_flags.cell_of(c, t)
            x, y, w, h = mat_flags.cell_rect(i)
            flags.append({"culture": c, "trade": t, "cell": i, "x": x, "y": y, "w": w, "h": h,
                          "charge": CHARGES[c], "field": TRADE_NAMES[t]})
    return {
        "//": "Written by tools/art/flags.py (queue #405, note 674): which cell of the town_flags atlas each town's flag is, "
              "by its custom (world/towns.json cultures) and its trade (industries). Pixels in the 1024 atlas.",
        "texture": "town_flags",
        "size": mat_flags.SIZE,
        "flags": flags,
    }


def sheet(path: Path):
    from PIL import Image, ImageDraw, ImageFont
    atlas = Image.open(ROOT / "content" / "art" / "textures" / "town_flags.png").convert("RGBA")
    cols, cw, ch, pad, lab = 6, 300, 200, 14, 40
    rows = -(-len(mat_flags.CULTURES) * len(mat_flags.TRADES) // cols)
    out = Image.new("RGB", (cols * (cw + pad) + pad, rows * (ch + lab + pad) + pad + 30), (24, 22, 20))
    dr = ImageDraw.Draw(out)
    try:
        f = ImageFont.truetype("DejaVuSans.ttf", 13)
        fb = ImageFont.truetype("DejaVuSans-Bold.ttf", 15)
    except OSError:
        f = fb = ImageFont.load_default()
    dr.text((pad, 8), "The towns' flags: a field for the trade, the province's saltire, a charge for the custom (queue #405)", fill=(220, 210, 190), font=fb)
    for k, e in enumerate(catalogue()["flags"]):
        r, c = divmod(k, cols)
        x0, y0 = pad + c * (cw + pad), 36 + r * (ch + lab + pad)
        cell = atlas.crop((e["x"], e["y"], e["x"] + e["w"], e["y"] + e["h"])).resize((cw, ch), Image.LANCZOS)
        bg = Image.new("RGBA", cell.size, (60, 64, 70, 255))
        out.paste(Image.alpha_composite(bg, cell).convert("RGB"), (x0, y0))
        dr.text((x0, y0 + ch + 4), f"{e['culture']} · {e['trade']}", fill=(230, 220, 200), font=fb)
        dr.text((x0, y0 + ch + 22), e["charge"], fill=(170, 160, 145), font=f)
    path.parent.mkdir(parents=True, exist_ok=True)
    out.save(path)
    print(path)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--sheet", type=Path, help="also write a labelled contact sheet of every flag")
    args = ap.parse_args()
    out = ROOT / "content" / "art" / "flags.json"
    out.write_text(json.dumps(catalogue(), indent=2) + "\n")
    print(out.relative_to(ROOT))
    if args.sheet:
        sheet(args.sheet)


if __name__ == "__main__":
    main()
