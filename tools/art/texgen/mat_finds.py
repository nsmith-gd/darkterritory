"""The village finds (GDD App. F.3, the director, 8 Oct 2026: "loot needs to be discoverable in there ... a bit of a brighter
more unique look to them so that they stand out from the background as interactable objects. Do this with good texture work,
not VFX"; ARCHITECTURE §8 note 326).

One atlas, 4x4 cells of 128 px, a cell per kind of find (PropKit.Find maps each model's label onto its cell): printed tin
labels, wrappers, enamel, gingham, baize, polished brass. Against the house's worn plaster and grey boards they are the
cleanest, brightest, most saturated things in the room: cream paper, pillar-box red, ultramarine, chrome yellow, gilt. Not
new; kept, in a cupboard, out of the weather: a little foxing and a scuffed edge, no soot. Their spec is the paper's sheen
and the metal's glint, so a lamp picks them out by their surface, not a glow. Not value-graded (a paper family): bright on
purpose. Text is rendered large and box-filtered down, crunchy but legible at arm's length.
"""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from . import core, noise
from .core import hexc, lerp, saturate, smoothstep
from .mat_paper import font, font_sources
from .reg import texture

K = 4            # supersample
C = 128 * K      # a cell, at work size
CELLS = 4        # cells a side

# Cell order is the content contract with PropKit.Find (Art/PropKit.cs FindCells): don't reorder, append.
ITEMS = ["tinnedFood", "treats", "candles", "medicine", "bandages", "morphine", "preserves", "lampOil",
         "tools", "valuableTools", "pocketWatch", "lampParts", "rope", "supplies", "brass", "cream"]


def _text(dr, box, txt, kind, size, fill=255, spacing=0):
    """Centred text in a box (x0, y0, x1, y1), the font shrunk until it fits the width."""
    x0, y0, x1, y1 = box
    while size > 6 * K:
        f = font(kind, size)
        w = dr.textlength(txt, font=f) + spacing * (len(txt) - 1)
        if w <= (x1 - x0) * 0.92:
            break
        size -= K
    f = font(kind, size)
    w = dr.textlength(txt, font=f) + spacing * (len(txt) - 1)
    a, d = f.getmetrics()
    x, y = (x0 + x1) / 2 - w / 2, (y0 + y1) / 2 - (a + d) / 2
    for ch in txt:
        dr.text((x, y), ch, fill=fill, font=f)
        x += dr.textlength(ch, font=f) + spacing


def _mask():
    im = Image.new("L", (C, C), 0)
    return im, ImageDraw.Draw(im)


def _m(im):
    return np.asarray(im, np.float32) / 255


def _paper(rng, base, shade=0.86):
    """Clean printed stock: fine fibre, a faint mottle, a little foxing, a scuffed edge."""
    fib = noise.fbm01(rng, (C, C), 1.2 * K, octaves=3, stretch=(2.0, 0.6))
    mot = noise.fbm01(rng, (C, C), 18 * K, octaves=4)
    d = core.rgb(C, C, hexc(base)) * (shade + (1 - shade) * (0.6 * mot + 0.4 * fib))[..., None]
    fox = smoothstep(0.95, 0.985, noise.fbm01(rng, (C, C), 2 * K, octaves=2)) * mot
    d = lerp(d, hexc("#8A6A44"), fox * 0.35)
    xs, ys = noise.grid(C, C)
    edge = np.minimum(np.minimum(xs, C - 1 - xs), np.minimum(ys, C - 1 - ys)) / (C * 0.05)
    return d * (0.82 + 0.18 * saturate(edge + 0.3 * mot))[..., None]


def _metal(rng, ramp, scale=0.55):
    """Polished metal: a brushed grain and a broad sheen across it."""
    xs, ys = noise.grid(C, C)
    grain = noise.fbm01(rng, (C, C), 0.8 * K, octaves=3, stretch=(8.0, 0.4))
    sheen = 0.5 + 0.5 * np.sin((xs + ys * 0.6) / C * 5.0)
    return core.apply_ramp(saturate(0.35 + 0.35 * sheen + 0.2 * grain), ramp) * scale


def _cell(name, rng):
    """One cell: diffuse (C, C, 3), spec intensity, gloss."""
    xs, ys = noise.grid(C, C)
    spec = np.full((C, C), 0.3, np.float32)
    gloss = np.full((C, C), 0.55, np.float32)
    ink, dr = _mask()
    red, rdr = _mask()
    if name == "tinnedFood":
        # A pillar-box red label, a cream band with the name, gilt rules: the tin's.
        d = _paper(rng, "#B0261C")
        band = (ys > C * 0.3) & (ys < C * 0.7)
        d = lerp(d, _paper(rng, "#E8DCB8"), band * 1.0)
        gilt = (np.abs(ys - C * 0.3) < 2 * K) | (np.abs(ys - C * 0.7) < 2 * K) | (np.abs(ys - C * 0.12) < K) | (np.abs(ys - C * 0.88) < K)
        d = lerp(d, hexc("#D8A838"), gilt * 1.0)
        spec, gloss = np.where(gilt, 0.8, 0.35), np.where(gilt, 0.8, 0.6)
        _text(dr, (0, C * 0.32, C, C * 0.55), "BULLY BEEF", "serif_bold", 22 * K)
        _text(dr, (0, C * 0.54, C, C * 0.68), "CORNED · 1 LB", "sans_bold", 10 * K)
        d = lerp(d, hexc("#1A1210"), _m(ink) * 0.92)
    elif name == "treats":
        # Barley-sugar paper: red and cream stripes on the bias, a cream oval with the name.
        stripe = (((xs + ys) // (14 * K)) % 2) == 0
        d = lerp(_paper(rng, "#E8DCB8"), _paper(rng, "#C03A26"), stripe * 1.0)
        oval = ((xs - C / 2) / (C * 0.42)) ** 2 + ((ys - C / 2) / (C * 0.22)) ** 2 < 1
        d = lerp(d, _paper(rng, "#F0E6C8"), oval * 1.0)
        _text(dr, (C * 0.1, C * 0.36, C * 0.9, C * 0.64), "TOFFEE", "serif_bold", 26 * K)
        d = lerp(d, hexc("#5A1A10"), _m(ink) * 0.9)
    elif name == "candles":
        # Ultramarine wrapping paper, a white printed name, as candles came from the chandler's.
        d = _paper(rng, "#2A48A0")
        _text(dr, (0, C * 0.25, C, C * 0.55), "CANDLES", "serif_bold", 24 * K, spacing=K)
        _text(dr, (0, C * 0.55, C, C * 0.75), "SIX · BEST WAX", "sans_bold", 10 * K)
        d = lerp(d, hexc("#F2ECDA"), _m(ink) * 0.95)
    elif name == "medicine":
        # A chemist's label: cream, a red double border, the name in serif, the dose in a hand.
        d = _paper(rng, "#EDE2C2")
        rdr.rectangle([6 * K, 6 * K, C - 6 * K, C - 6 * K], outline=255, width=3 * K)
        rdr.rectangle([12 * K, 12 * K, C - 12 * K, C - 12 * K], outline=255, width=K)
        _text(dr, (0, C * 0.18, C, C * 0.45), "TONIC", "serif_bold", 26 * K)
        _text(dr, (0, C * 0.48, C, C * 0.64), "ONE SPOONFUL", "sans_bold", 9 * K)
        _text(dr, (0, C * 0.66, C, C * 0.84), "night & morning", "hand", 11 * K)
        d = lerp(d, hexc("#B02418"), _m(red) * 0.95)
        d = lerp(d, hexc("#201810"), _m(ink) * 0.9)
    elif name == "bandages":
        # White wrapper, a big red cross: the one thing in the house that says "first aid" from across the room.
        d = _paper(rng, "#F2F0E8", shade=0.92)
        rdr.rectangle([C * 0.38, C * 0.12, C * 0.62, C * 0.62], fill=255)
        rdr.rectangle([C * 0.2, C * 0.26, C * 0.8, C * 0.48], fill=255)
        _text(dr, (0, C * 0.66, C, C * 0.82), "LINT BANDAGE", "sans_bold", 11 * K)
        d = lerp(d, hexc("#C8201A"), _m(red) * 0.97)
        d = lerp(d, hexc("#202020"), _m(ink) * 0.85)
    elif name == "morphine":
        # A white enamel tin's lid, a red cross in a ring, black lettering round it.
        d = _paper(rng, "#F4F2EC", shade=0.95)
        r = np.sqrt((xs - C / 2) ** 2 + (ys - C / 2) ** 2) / (C / 2)
        rdr.ellipse([C * 0.18, C * 0.18, C * 0.82, C * 0.82], outline=255, width=4 * K)
        rdr.rectangle([C * 0.44, C * 0.3, C * 0.56, C * 0.7], fill=255)
        rdr.rectangle([C * 0.3, C * 0.44, C * 0.7, C * 0.56], fill=255)
        _text(dr, (0, C * 0.02, C, C * 0.17), "MORPHIA", "sans_bold", 12 * K)
        _text(dr, (0, C * 0.83, C, C * 0.98), "¼ GRAIN", "sans_bold", 10 * K)
        d = lerp(d, hexc("#C01E18"), _m(red) * 0.97)
        d = lerp(d, hexc("#151515"), _m(ink) * 0.9)
        chip = smoothstep(0.9, 0.97, noise.fbm01(rng, (C, C), 3 * K, octaves=2)) * (r > 0.85)
        d = lerp(d, hexc("#2A2A30"), chip)
        spec, gloss = np.full((C, C), 0.6, np.float32), np.full((C, C), 0.8, np.float32)
    elif name == "preserves":
        # A red-and-white gingham cover tied over the jar, a cream tag with the fruit and the year.
        check = ((xs // (10 * K)) % 2) + ((ys // (10 * K)) % 2)
        d = lerp(_paper(rng, "#F0E8D8"), _paper(rng, "#B8281E"), (check / 2.0) ** 1.0)
        cloth = noise.fbm01(rng, (C, C), 0.6 * K, octaves=2)
        d = d * (0.88 + 0.12 * cloth)[..., None]
        tag = (xs > C * 0.25) & (xs < C * 0.75) & (ys > C * 0.62) & (ys < C * 0.88)
        d = lerp(d, _paper(rng, "#EDE4C8"), tag * 1.0)
        _text(dr, (C * 0.25, C * 0.62, C * 0.75, C * 0.88), "PLUM '86", "hand", 13 * K)
        d = lerp(d, hexc("#3A2010"), _m(ink) * 0.9)
        spec, gloss = np.full((C, C), 0.15, np.float32), np.full((C, C), 0.3, np.float32)
    elif name == "lampOil":
        # Chrome yellow tin, a black diamond with the name: a paraffin tin is meant to be seen.
        d = _paper(rng, "#E0B020")
        dia = np.abs(xs - C / 2) / (C * 0.42) + np.abs(ys - C / 2) / (C * 0.4) < 1
        d = lerp(d, hexc("#18140E"), dia * 1.0)
        _text(dr, (C * 0.18, C * 0.38, C * 0.82, C * 0.62), "PARAFFIN", "sans_bold", 15 * K)
        d = lerp(d, hexc("#E8C030"), _m(ink) * 0.95)
        spec, gloss = np.full((C, C), 0.55, np.float32), np.full((C, C), 0.7, np.float32)
    elif name == "tools":
        # A tool roll in red oilcloth: a sheen, stitched edges, a leather tie.
        d = _paper(rng, "#9C2A1C")
        stitch = ((np.abs(ys - 8 * K) < K) | (np.abs(ys - (C - 8 * K)) < K)) & (((xs // (5 * K)) % 2) == 0)
        d = lerp(d, hexc("#E0D0A8"), stitch * 1.0)
        tie = np.abs(xs - C * 0.62) < 6 * K
        d = lerp(d, hexc("#5A3418"), tie * 1.0)
        spec, gloss = np.where(tie, 0.2, 0.55), np.where(tie, 0.4, 0.75)
    elif name == "valuableTools":
        # A fitted case: green baize, a polished brass plate with the maker.
        d = _paper(rng, "#1E6A3A") * (0.9 + 0.1 * noise.fbm01(rng, (C, C), 0.5 * K, octaves=2))[..., None]
        plate = (xs > C * 0.18) & (xs < C * 0.82) & (ys > C * 0.36) & (ys < C * 0.64)
        d = lerp(d, _metal(rng, "brass", 0.95), plate * 1.0)
        _text(dr, (C * 0.18, C * 0.36, C * 0.82, C * 0.64), "SHEFFIELD", "serif_bold", 13 * K)
        d = lerp(d, hexc("#2A1A08"), _m(ink) * 0.85)
        spec, gloss = np.where(plate, 0.85, 0.1), np.where(plate, 0.85, 0.25)
    elif name == "pocketWatch":
        # A white enamel dial, black numerals, a gilt bezel: the whole cell, round, for the watch's face.
        r = np.sqrt((xs - C / 2) ** 2 + (ys - C / 2) ** 2) / (C / 2)
        d = _paper(rng, "#F6F2E6", shade=0.95)
        for h in range(12):
            a = h / 12 * 2 * np.pi
            x, y = C / 2 + np.sin(a) * C * 0.33, C / 2 - np.cos(a) * C * 0.33
            _text(dr, (x - 12 * K, y - 10 * K, x + 12 * K, y + 10 * K), ["XII", "I", "II", "III", "IIII", "V", "VI", "VII", "VIII", "IX", "X", "XI"][h], "serif_bold", 9 * K)
        dr.line([C / 2, C / 2, C / 2 + C * 0.14, C / 2 - C * 0.1], fill=255, width=2 * K)
        dr.line([C / 2, C / 2, C / 2 - C * 0.05, C / 2 - C * 0.26], fill=255, width=K)
        d = lerp(d, hexc("#101010"), _m(ink) * 0.92)
        bez = smoothstep(0.86, 0.9, r)
        d = lerp(d, _metal(rng, "brass", 1.0), bez)
        spec, gloss = np.where(bez > 0.5, 0.95, 0.7), np.where(bez > 0.5, 0.9, 0.85)
    elif name == "lampParts":
        # Polished brass, stamped with the burner's number.
        d = _metal(rng, "brass", 1.0)
        _text(dr, (0, C * 0.3, C, C * 0.7), "No. 2 BURNER", "sans_bold", 13 * K)
        d = lerp(d, d * 0.45, _m(ink) * 0.9)
        spec, gloss = np.full((C, C), 0.9, np.float32), np.full((C, C), 0.85, np.float32)
    elif name == "rope":
        # Manila: pale gold strands twisted on the bias.
        tw = 0.5 + 0.5 * np.sin((xs * 0.7 + ys) / (6 * K))
        fib = noise.fbm01(rng, (C, C), 0.5 * K, octaves=3, stretch=(6.0, 0.4))
        d = core.rgb(C, C, hexc("#C8A462")) * (0.65 + 0.25 * tw + 0.15 * fib)[..., None]
        spec, gloss = np.full((C, C), 0.12, np.float32), np.full((C, C), 0.25, np.float32)
    elif name == "supplies":
        # Clean sacking, a red stencil: anything else the house kept.
        weave = (np.sin(xs / (2 * K)) * np.sin(ys / (2 * K)) * 0.5 + 0.5)
        d = core.rgb(C, C, hexc("#CDB88C")) * (0.75 + 0.25 * weave)[..., None]
        _text(dr, (0, C * 0.3, C, C * 0.7), "SUPPLIES", "sans_bold", 18 * K, spacing=K)
        d = lerp(d, hexc("#A82418"), _m(ink) * 0.85)
        spec, gloss = np.full((C, C), 0.1, np.float32), np.full((C, C), 0.2, np.float32)
    elif name == "brass":
        d = _metal(rng, "brass", 1.0)
        spec, gloss = np.full((C, C), 0.9, np.float32), np.full((C, C), 0.85, np.float32)
    else:
        d = _paper(rng, "#EDE4C8")
    return d, np.asarray(spec, np.float32), np.asarray(gloss, np.float32)


@texture("finds_atlas", "paper", tile=None)
def finds_atlas(ctx):
    """4x4 atlas of 128 px cells, one per kind of find (ITEMS' order), built at 4x."""
    A = C * CELLS
    D = np.zeros((A, A, 3), np.float32)
    Sp = np.zeros((A, A), np.float32)
    G = np.zeros((A, A), np.float32)
    cells = []
    for i, name in enumerate(ITEMS):
        cy, cx = divmod(i, CELLS)
        d, s, g = _cell(name, ctx.rng("cell", name))
        D[cy * C:(cy + 1) * C, cx * C:(cx + 1) * C] = d
        Sp[cy * C:(cy + 1) * C, cx * C:(cx + 1) * C] = s
        G[cy * C:(cy + 1) * C, cx * C:(cx + 1) * C] = g
        cells.append({"name": name, "cell": [cx * 128, cy * 128, 128, 128]})
    ctx.src.used += font_sources()
    return ctx.out(D, Sp, G, tiling=False, factor=K, cells=cells,
                   procedural="printed labels, wrappers, enamel, gingham, baize and polished brass for the village finds",
                   grain=0.02, quant=64)
