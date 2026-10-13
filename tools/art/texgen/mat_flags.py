"""The towns' flags (queue #405, note 674; the director, 13 Oct: "we need to do a series of flag designs for the towns").

One atlas, `town_flags`: a flag for every custom a town keeps (world/towns.json `cultures`) in every trade it lives by
(`industries`), sewn the way a walled town would sew one, out of what it had:

- the field is the trade's: a pit town's coal black with the lamp's yellow along the foot, a growing town's green over
  the ochre of turned earth, a foundry town's oxide red bound in iron grey;
- the canton is the old province's, the same for every town: New Scotland's blue saltire on white, as the engine's lamp
  carries it (TrainKit), so the towns read as one country's;
- the charge is the custom's, cut from bone-white sheeting and stitched on: the bell with its clapper bound, the rings,
  the tally with one mark too many, the shuttered lantern, the horn, the eye, the toy horse, rule 9, the banked fire,
  the white line, the coat on its peg, four lamps with one gone, the bitten van, the back gun;
- and all of it faded, patched, stained and frayed at the fly.

Cells are 3:2, six to a row; tools/art/flags.py writes which cell is whose (content/art/flags.json) and the contact sheet.
"""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from . import noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture

# The towns.json ids, in the order their cells are laid out (culture-major, three trades each).
CULTURES = ["hush", "pairs", "count", "shutters", "talkers", "eyes", "offerings", "rulebook", "banked", "crown", "backs",
            "fours", "lastCar", "backGun"]
TRADES = ["coal", "farm", "foundry"]
COLS, ROWS = 6, 9
SIZE = 1024          # the atlas, a hero layer (over look.json's layerSize)
K = 2                # drawn at twice that, boxed down

BONE = hexc("#C9C0AA")       # the sheeting the charges are cut from
STITCH = hexc("#3A3026")
SALTIRE = hexc("#2E4A78")
CANTON = hexc("#B8B4A6")


def cell_rect(i: int) -> tuple[int, int, int, int]:
    """Cell i's rectangle in the atlas (x, y, w, h), at its final size."""
    cy, cx = divmod(i, COLS)
    x0, x1 = round(cx * SIZE / COLS), round((cx + 1) * SIZE / COLS)
    h = round(SIZE / COLS * 2 / 3)
    return x0, cy * h, x1 - x0, h


def cell_of(culture: str, trade: str) -> int:
    return CULTURES.index(culture) * len(TRADES) + TRADES.index(trade)


def _mask(w, h, fn):
    im = Image.new("L", (w, h), 0)
    fn(ImageDraw.Draw(im), w, h)
    return np.asarray(im, np.float32) / 255


def _field(trade, w, h, rng):
    xs, ys = noise.grid(h, w)
    u, v = xs / w, ys / h
    if trade == "coal":
        d = np.broadcast_to(hexc("#24221F"), (h, w, 3)).copy()
        d = lerp(d, hexc("#B88A24"), ((v > 0.80) & (v < 0.92))[..., None] * 1.0)
    elif trade == "farm":
        d = lerp(np.broadcast_to(hexc("#4B5A34"), (h, w, 3)), hexc("#8A6E3A"), (v > 0.56)[..., None] * 1.0)
        furrow = (np.sin(ys * 2 * np.pi / (h * 0.055)) > 0.55) & (v > 0.6)
        d = lerp(d, hexc("#6E5530"), furrow[..., None] * 0.8)
    else:
        d = np.broadcast_to(hexc("#7A2E1E"), (h, w, 3)).copy()
        edge = (u < 0.0) | (v < 0.07) | (v > 0.93) | (u > 0.94)
        d = lerp(d, hexc("#4A4846"), edge[..., None] * 1.0)
    return d


def _canton(w, h):
    """The old province's blue saltire on white, in the hoist's upper corner."""
    cw, ch = int(w * 0.30), int(h * 0.42)

    def draw(dr, W, H):
        t = max(2, int(ch * 0.2))
        dr.line([(0, 0), (cw, ch)], fill=255, width=t)
        dr.line([(0, ch), (cw, 0)], fill=255, width=t)
    blue = _mask(w, h, draw)
    box = _mask(w, h, lambda dr, W, H: dr.rectangle([0, 0, cw, ch], fill=255))
    return box, blue * box


def _charge(culture, w, h):
    """The custom's emblem, bone white, as a mask (and a second mask for details in the field's colour)."""
    cx, cy, s = w * 0.62, h * 0.52, h * 0.30

    def P(*pts):
        return [(cx + x * s, cy + y * s) for x, y in pts]

    def main(dr, W, H):
        if culture == "hush":           # the bell, its clapper bound
            dr.polygon(P((-0.55, 0.75), (-0.45, 0.2), (-0.4, -0.45), (-0.2, -0.8), (0.2, -0.8), (0.4, -0.45), (0.45, 0.2), (0.55, 0.75)), fill=255)
            dr.rectangle([cx - 0.12 * s, cy - 1.0 * s, cx + 0.12 * s, cy - 0.78 * s], fill=255)
            dr.ellipse([cx - 0.18 * s, cy + 0.72 * s, cx + 0.18 * s, cy + 1.02 * s], fill=255)
        elif culture == "pairs":        # two rings, linked
            for dx in (-0.35, 0.35):
                dr.ellipse([cx + (dx - 0.6) * s, cy - 0.6 * s, cx + (dx + 0.6) * s, cy + 0.6 * s], outline=255, width=int(0.2 * s))
        elif culture == "count":        # the tally, one mark too many
            for k in range(6):
                x = cx + (-0.9 + k * 0.33) * s
                dr.line([(x, cy - 0.75 * s), (x + 0.05 * s, cy + 0.75 * s)], fill=255, width=int(0.14 * s))
            dr.line([(cx - 1.05 * s, cy + 0.55 * s), (cx + 0.45 * s, cy - 0.55 * s)], fill=255, width=int(0.12 * s))
        elif culture == "shutters":     # a lantern, its glass shuttered
            dr.rectangle([cx - 0.45 * s, cy - 0.55 * s, cx + 0.45 * s, cy + 0.8 * s], fill=255)
            dr.polygon(P((-0.55, -0.55), (0, -0.95), (0.55, -0.55)), fill=255)
            dr.ellipse([cx - 0.2 * s, cy - 1.25 * s, cx + 0.2 * s, cy - 0.9 * s], outline=255, width=int(0.08 * s))
        elif culture == "talkers":      # the reading horn on its pole
            dr.polygon(P((-0.9, -0.15), (-0.2, -0.25), (0.75, -0.8), (0.75, 0.6), (-0.2, 0.15), (-0.9, 0.1)), fill=255)
            dr.rectangle([cx - 0.75 * s, cy + 0.05 * s, cx - 0.62 * s, cy + 1.0 * s], fill=255)
        elif culture == "eyes":         # the eye, open
            dr.ellipse([cx - 1.0 * s, cy - 0.5 * s, cx + 1.0 * s, cy + 0.5 * s], fill=255)
        elif culture == "offerings":    # a toy horse on its rockers
            dr.polygon(P((-0.7, -0.15), (0.35, -0.15), (0.45, -0.55), (0.8, -0.7), (0.85, -0.45), (0.6, -0.3), (0.55, 0.15),
                         (0.4, 0.15), (0.4, 0.5), (0.25, 0.5), (0.25, 0.15), (-0.45, 0.15), (-0.45, 0.5), (-0.6, 0.5), (-0.6, 0.15),
                         (-0.85, 0.0)), fill=255)
            dr.arc([cx - 1.0 * s, cy + 0.1 * s, cx + 0.9 * s, cy + 0.85 * s], 20, 160, fill=255, width=int(0.12 * s))
        elif culture == "rulebook":     # the rule book, open
            dr.polygon(P((-1.0, -0.55), (0, -0.4), (1.0, -0.55), (1.0, 0.65), (0, 0.8), (-1.0, 0.65)), fill=255)
        elif culture == "banked":       # the brazier and its low fire
            dr.polygon(P((-0.7, 0.05), (0.7, 0.05), (0.45, 0.55), (-0.45, 0.55)), fill=255)
            for dx in (-0.35, 0.35):
                dr.line([(cx + dx * s, cy + 0.5 * s), (cx + dx * 1.4 * s, cy + 0.95 * s)], fill=255, width=int(0.1 * s))
            dr.polygon(P((-0.45, 0.0), (-0.25, -0.5), (-0.05, -0.25), (0.1, -0.85), (0.3, -0.3), (0.45, 0.0)), fill=255)
        elif culture == "crown":        # the white line down the middle, a boot beside it
            dr.rectangle([cx - 0.12 * s, 0, cx + 0.12 * s, H], fill=255)
            dr.polygon(P((0.45, -0.6), (0.75, -0.6), (0.75, 0.3), (1.2, 0.45), (1.2, 0.65), (0.45, 0.65)), fill=255)
        elif culture == "backs":        # a coat on its peg, hung back to front
            dr.rectangle([cx - 0.95 * s, cy - 1.0 * s, cx + 0.95 * s, cy - 0.88 * s], fill=255)
            dr.rectangle([cx - 0.05 * s, cy - 0.9 * s, cx + 0.05 * s, cy - 0.72 * s], fill=255)
            dr.polygon(P((-0.18, -0.78), (0.18, -0.78), (0.72, -0.52), (0.85, 0.5), (0.6, 0.52), (0.5, -0.1), (0.48, 0.95),
                         (-0.48, 0.95), (-0.5, -0.1), (-0.6, 0.52), (-0.85, 0.5), (-0.72, -0.52)), fill=255)
        elif culture == "fours":        # four lamps; one hook empty
            for k, (dx, dy) in enumerate(((-0.45, -0.45), (0.45, -0.45), (-0.45, 0.45), (0.45, 0.45))):
                box = [cx + (dx - 0.28) * s, cy + (dy - 0.32) * s, cx + (dx + 0.28) * s, cy + (dy + 0.32) * s]
                if k == 3:
                    dr.rectangle(box, outline=255, width=int(0.07 * s))
                else:
                    dr.rectangle(box, fill=255)
        elif culture == "lastCar":      # the van, its back half bitten away
            dr.rectangle([cx - 1.1 * s, cy - 0.5 * s, cx + 0.6 * s, cy + 0.45 * s], fill=255)
            dr.polygon(P((-1.2, -0.5), (0.7, -0.5), (0.5, -0.75), (-1.0, -0.75)), fill=255)
            for dx in (-0.75, 0.25):
                dr.ellipse([cx + (dx - 0.18) * s, cy + 0.4 * s, cx + (dx + 0.18) * s, cy + 0.76 * s], fill=255)
        elif culture == "backGun":      # the old gun, aimed back up the line
            dr.polygon(P((-1.0, -0.25), (0.6, -0.15), (0.6, 0.12), (-1.0, 0.25)), fill=255)
            dr.ellipse([cx + 0.1 * s, cy - 0.05 * s, cx + 0.75 * s, cy + 0.6 * s], fill=255)
            dr.polygon(P((-0.2, 0.25), (0.9, 0.25), (1.0, 0.8), (-0.3, 0.8)), fill=255)

    def detail(dr, W, H):
        # Cut back out of the bone white: the bell's binding, the eye's pupil, the lantern's shutter, the book's 9...
        if culture == "hush":
            dr.line([(cx - 0.42 * s, cy + 0.25 * s), (cx + 0.42 * s, cy + 0.05 * s)], fill=255, width=int(0.08 * s))
            dr.line([(cx - 0.42 * s, cy + 0.05 * s), (cx + 0.42 * s, cy + 0.25 * s)], fill=255, width=int(0.08 * s))
        elif culture == "shutters":
            dr.rectangle([cx - 0.3 * s, cy - 0.35 * s, cx + 0.3 * s, cy + 0.6 * s], fill=255)
            for k in range(4):
                y = cy + (-0.25 + k * 0.22) * s
                dr.line([(cx - 0.3 * s, y), (cx + 0.3 * s, y)], fill=0, width=max(1, int(0.04 * s)))
        elif culture == "eyes":
            dr.ellipse([cx - 0.3 * s, cy - 0.3 * s, cx + 0.3 * s, cy + 0.3 * s], fill=255)
        elif culture == "rulebook":
            dr.line([(cx, cy - 0.4 * s), (cx, cy + 0.8 * s)], fill=255, width=max(2, int(0.05 * s)))
            # A 9 on the right-hand page.
            dr.ellipse([cx + 0.3 * s, cy - 0.3 * s, cx + 0.7 * s, cy + 0.1 * s], outline=255, width=int(0.08 * s))
            dr.line([(cx + 0.66 * s, cy - 0.1 * s), (cx + 0.6 * s, cy + 0.55 * s)], fill=255, width=int(0.08 * s))
        elif culture == "lastCar":
            dr.ellipse([cx + 0.15 * s, cy - 0.95 * s, cx + 1.3 * s, cy + 0.6 * s], fill=255)
        elif culture == "banked":
            dr.line([(cx - 0.8 * s, cy - 0.2 * s), (cx + 0.6 * s, cy + 0.15 * s)], fill=255, width=int(0.07 * s))
        elif culture == "backs":
            dr.line([(cx, cy - 0.7 * s), (cx, cy + 0.95 * s)], fill=255, width=max(2, int(0.05 * s)))
    m = _mask(w, h, main)
    cut = _mask(w, h, detail)
    if culture == "lastCar":       # the bite takes the van's back end clean away
        m = m * (1 - cut)
        cut = cut * 0
    return m, cut


def flag(culture: str, trade: str, w: int, h: int, rng: np.random.Generator):
    """One flag's diffuse (h, w, 3) and alpha (h, w) at w x h."""
    xs, ys = noise.grid(h, w)
    u, v = xs / w, ys / h
    d = _field(trade, w, h, rng)
    box, blue = _canton(w, h)
    d = lerp(d, CANTON, box[..., None])
    d = lerp(d, SALTIRE, blue[..., None])
    weave = (np.sin(xs * 2 * np.pi / (2.2 * K)) * np.sin(ys * 2 * np.pi / (2.2 * K))) * 0.5 + 0.5
    # A patch or two in the field where it wore through, under the charge.
    for _ in range(rng.integers(1, 3)):
        px, py = rng.uniform(0.35, 0.85) * w, rng.uniform(0.1, 0.7) * h
        pw, ph = rng.uniform(0.08, 0.14) * w, rng.uniform(0.12, 0.2) * h
        patch = ((np.abs(xs - px) < pw / 2) & (np.abs(ys - py) < ph / 2)).astype(np.float32)
        tone = hexc(rng.choice(["#5E5648", "#3E4448", "#6A4E36"]))
        d = lerp(d, tone * (0.9 + 0.2 * weave[..., None]), patch[..., None] * 0.85)
        ring = saturate(noise.blur(patch, 0.8 * K) - patch * 0.9) * 3
        d = lerp(d, STITCH, saturate(ring)[..., None] * 0.5)
    charge, cut = _charge(culture, w, h)
    # Appliqué: a stitched edge round the charge, a shadow under its lip.
    edge = saturate(noise.blur(charge, 1.2 * K) - charge * 0.5) * (1 - charge)
    d = lerp(d, d * 0.6, saturate(edge * 2)[..., None])
    d = lerp(d, BONE, charge[..., None])
    d = lerp(d, _field(trade, w, h, rng) * 0.9, (cut * charge)[..., None])
    stitches = (np.sin((xs + ys) * 2 * np.pi / (3.0 * K)) > 0.3) * saturate(noise.blur(charge, 0.7 * K) * (1 - charge) * 6)
    d = lerp(d, STITCH, (stitches * 0.6)[..., None])
    # Cloth: the weave, fading toward the fly where the wind works it, rain stains, soot.
    d = d * (0.88 + 0.12 * weave)[..., None]
    fade = smoothstep(0.3, 1.0, u) * 0.35 + 0.15 * noise.fbm01(rng, (h, w), 8)
    d = lerp(d, hexc("#9A9486"), (fade * 0.45)[..., None])
    stain = smoothstep(0.55, 0.8, noise.fbm01(rng, (h, w), 4, stretch=(1, 3))) * smoothstep(0.2, 1.0, v)
    d = lerp(d, d * 0.6, stain[..., None] * 0.6)
    soot = smoothstep(0.5, 0.85, noise.fbm01(rng, (h, w), 3))
    d = d * (1 - 0.3 * soot)[..., None]
    # The hoist's doubled cloth and its rope sleeve.
    d = lerp(d, hexc("#8A8272"), (u < 0.035)[..., None] * 1.0)
    # The fly frayed: ragged tongues and a tear or two, cut away (alpha tested).
    tongues = 0.92 + 0.06 * noise.fbm01(rng, (h, w), 2, stretch=(4, 0.3)) - 0.05 * (np.sin(v * np.pi * rng.uniform(5, 9)) > 0.7)
    alpha = (u < tongues).astype(np.float32)
    threads = (u > tongues - 0.03) & (np.sin(ys * 2 * np.pi / (1.6 * K)) > 0.2)
    d = lerp(d, d * 0.7, threads[..., None] * 1.0)
    return saturate(d).astype(np.float32), alpha


@texture("town_flags", "cloth", tile=None)
def town_flags(ctx):
    """The towns' flags, a cell each: 1024², six 3:2 cells to a row, 14 customs x 3 trades."""
    W = SIZE * K
    D = np.zeros((W, W, 3), np.float32) + hexc("#2A2622")
    A = np.zeros((W, W), np.float32)
    cells = []
    for ci, culture in enumerate(CULTURES):
        for ti, trade in enumerate(TRADES):
            i = cell_of(culture, trade)
            x, y, w, h = cell_rect(i)
            d, a = flag(culture, trade, w * K, h * K, ctx.rng("flag", culture, trade))
            D[y * K:(y + h) * K, x * K:(x + w) * K] = d
            A[y * K:(y + h) * K, x * K:(x + w) * K] = a
            cells.append({"name": f"{culture}-{trade}", "x": x, "y": y, "w": w, "h": h})
    S = np.full((W, W), 0.04, np.float32)
    G = np.full((W, W), 0.15, np.float32)
    return ctx.out(D, S, G, alpha=A, tiling=False, alpha_test=True, factor=K, cells=cells,
                   procedural="the towns' flags: trade fields, the province's saltire, the customs' charges, weathered")
