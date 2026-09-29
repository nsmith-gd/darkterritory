"""Wrapping vector strokes (grass blades, needles, roots, cracks, veins) drawn with PIL.
Each stroke is also drawn shifted by +-W/+-H, so anything crossing the border continues on the
other side and the texture still tiles."""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

OFFS = [(dx, dy) for dx in (-1, 0, 1) for dy in (-1, 0, 1)]


def strokes(shape, polylines, widths, values=None, wrap=True, supersample=1):
    """Rasterise polylines (lists of (x, y)) into an HxW float mask. `values` 0..1 per stroke
    (later strokes draw over earlier ones)."""
    h, w = shape
    ss = supersample
    im = Image.new("L", (w * ss, h * ss), 0)
    dr = ImageDraw.Draw(im)
    for i, pl in enumerate(polylines):
        v = int(round(255 * (1.0 if values is None else float(values[i]))))
        wd = max(1, int(round(widths[i] * ss)))
        offs = OFFS if wrap else [(0, 0)]
        for ox, oy in offs:
            pts = [((x + ox * w) * ss, (y + oy * h) * ss) for x, y in pl]
            xs = [p[0] for p in pts]
            ys = [p[1] for p in pts]
            if max(xs) < -wd or min(xs) > w * ss + wd or max(ys) < -wd or min(ys) > h * ss + wd:
                continue
            dr.line(pts, fill=v, width=wd, joint="curve")
    a = np.asarray(im, np.float32) / 255
    if ss > 1:
        a = a.reshape(h, ss, w, ss).mean((1, 3))
    return a


def wander_line(rng, x, y, angle, length, steps, curl=0.3):
    """A slightly curving polyline (a blade, a root, a crack)."""
    pts = [(x, y)]
    seg = length / steps
    for _ in range(steps):
        angle += rng.normal(0, curl)
        x += np.cos(angle) * seg
        y += np.sin(angle) * seg
        pts.append((x, y))
    return pts


def ellipses(shape, items, wrap=True):
    """Filled ellipses: items of (cx, cy, rx, ry, value)."""
    h, w = shape
    im = Image.new("L", (w, h), 0)
    dr = ImageDraw.Draw(im)
    for cx, cy, rx, ry, v in items:
        for ox, oy in (OFFS if wrap else [(0, 0)]):
            x, y = cx + ox * w, cy + oy * h
            if x + rx < 0 or x - rx > w or y + ry < 0 or y - ry > h:
                continue
            dr.ellipse([x - rx, y - ry, x + rx, y + ry], fill=int(255 * v))
    return np.asarray(im, np.float32) / 255
