"""The night horizon (GDD §30 environment, §28 "cool ambient night", §32 landmarking): one
2048x512 band that wraps 360 degrees in u. Silhouettes in depth bands, each lighter and bluer
with distance (aerial perspective): far jagged mountains, mid ridges with a rail viaduct, a
fortified town on a crag with a gothic spire and a few lit windows, a ruined windmill, the
black pine edge along the horizon, smoke. Alpha 1 on silhouettes, 0 on open sky (the engine
draws its gradient/cloud sky behind), smoke in between.

Built at 2x (4096x1024) and box-filtered so the tiny lit windows land as 1-2 px points."""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw

from . import core, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture

K = 2
W, H = 2048 * K, 512 * K
HORIZON = int(H * 0.85)


def profile(rng, scale, octaves=5, gain=0.55, ridged=False):
    """A periodic 1D fractal profile over W (so the sky wraps), 0..1."""
    total = np.zeros(W, np.float32)
    amp, s = 1.0, scale
    f = np.fft.fftfreq(W).astype(np.float32)
    for _ in range(octaves):
        wn = rng.standard_normal(W).astype(np.float32)
        g = np.exp(-2 * (np.pi ** 2) * (f * s) ** 2)
        n = np.fft.ifft(np.fft.fft(wn) * g).real.astype(np.float32)
        n = n / max(float(n.std()), 1e-6)
        if ridged:
            n = 1 - np.abs(n)
        total += amp * n
        amp *= gain
        s /= 2
    return noise.normalize(total, 0, 100)


def fill_below(prof_y):
    """Mask of everything below a profile line (y per column, in pixels)."""
    ys = np.arange(H, dtype=np.float32)[:, None]
    # One pixel of antialiasing on the line itself; box-filtering at the end does the rest.
    return saturate(ys - prof_y[None, :] + 0.5)


def wrap_polys(dr, polys, fill=255):
    for poly in polys:
        for ox in (-W, 0, W):
            dr.polygon([(x + ox, y) for x, y in poly], fill=fill)


def mask_from(draw_fn):
    im = Image.new("L", (W, H), 0)
    draw_fn(ImageDraw.Draw(im))
    return np.asarray(im, np.float32) / 255


def xdist(x0):
    xs = np.arange(W, dtype=np.float32)
    d = np.abs(xs - x0)
    return np.minimum(d, W - d)


@texture("sky_backdrop", "sky", tile=None)
def sky_backdrop(ctx):
    rng = ctx.rng("sky")
    xs = np.arange(W, dtype=np.float32)
    layers = []   # (mask, colour-near-top, colour-at-horizon), far -> near

    # 1. Far mountains: jagged (ridged noise), in ranges with low gaps between.
    env = profile(ctx.rng("range"), W / 10, octaves=2)
    peaks = profile(ctx.rng("peaks"), W / 60, octaves=6, gain=0.55, ridged=True)
    far_h = H * (0.06 + 0.30 * smoothstep(0.2, 0.9, env) * (0.4 + 0.6 * peaks ** 1.6))
    far = fill_below(HORIZON - far_h)
    layers.append((far, "#262D38", "#343C48"))

    # 2. Mid ridges: lower, rounder, with a valley the viaduct spans.
    vx, vw = W * 0.60, W * 0.075
    mid_n = profile(ctx.rng("mid"), W / 30, octaves=5, gain=0.5)
    valley = smoothstep(vw * 1.5, vw * 0.6, xdist(vx))
    mid_h = H * (0.05 + 0.13 * mid_n) * (1 - 0.95 * valley) + H * 0.12 * smoothstep(vw * 2.2, vw * 1.3, xdist(vx)) * (1 - valley)
    mid = fill_below(HORIZON - mid_h)
    layers.append((mid, "#1C222A", "#262D36"))

    # 3. The viaduct across the valley: a deck on tall piers with round arches, in the mid band.
    deck_y = HORIZON - H * 0.17
    x0, x1 = vx - vw * 1.25, vx + vw * 1.25
    n_arch = 9
    span = (x1 - x0) / n_arch

    def viaduct(dr):
        dr.rectangle([x0, deck_y - 5 * K, x1, deck_y + 9 * K], fill=255)       # deck + parapet
        dr.rectangle([x0, deck_y, x1, HORIZON], fill=255)                       # the body (arches cut below)
    via = mask_from(viaduct)
    arches = mask_from(lambda dr: [dr.rectangle([x0 + i * span + span * 0.16, deck_y + span * 0.55 + 12 * K,
                                                 x0 + (i + 1) * span - span * 0.16, HORIZON], fill=255)
                                   for i in range(n_arch)] +
                       [dr.ellipse([x0 + i * span + span * 0.16, deck_y + 12 * K,
                                    x0 + (i + 1) * span - span * 0.16, deck_y + 12 * K + span * 1.1], fill=255)
                        for i in range(n_arch)])
    via = saturate(via - arches)
    # Only above the valley floor matters; below it the mid ridge covers it anyway.
    layers.append((via, "#1A2028", "#222932"))

    # 4. The town on its crag (near-mid band): a steep rock with walls, towers, roofs, a spire.
    tx = W * 0.22
    crag_top = HORIZON - H * 0.30
    # Crag outline: key points, then each segment broken into jittered steps (ledges, buttresses).
    key_pts = [(tx - 150 * K, HORIZON), (tx - 110 * K, HORIZON - H * 0.12), (tx - 80 * K, HORIZON - H * 0.19),
               (tx - 62 * K, crag_top + 18 * K), (tx - 40 * K, crag_top + 4 * K), (tx + 30 * K, crag_top),
               (tx + 58 * K, crag_top + 10 * K), (tx + 72 * K, HORIZON - H * 0.2), (tx + 96 * K, HORIZON - H * 0.13),
               (tx + 140 * K, HORIZON - H * 0.05), (tx + 190 * K, HORIZON)]
    crng = ctx.rng("crag")
    outline = [key_pts[0]]
    for (xa, ya), (xb, yb) in zip(key_pts, key_pts[1:]):
        steps = 6
        for k in range(1, steps + 1):
            f = k / steps
            jx, jy = (crng.normal(0, 4 * K), crng.normal(0, 5 * K)) if k < steps else (0, 0)
            outline.append((xa + (xb - xa) * f + jx, ya + (yb - ya) * f + jy))
    crag = mask_from(lambda dr: wrap_polys(dr, [outline]))
    trng = ctx.rng("town")
    town_polys = []
    # Curtain wall with crenellations along the crag top.
    wall_y = crag_top - 10 * K
    town_polys.append([(tx - 66 * K, crag_top + 20 * K), (tx - 66 * K, wall_y), (tx + 62 * K, wall_y),
                       (tx + 62 * K, crag_top + 14 * K)])
    for cx in np.arange(tx - 64 * K, tx + 60 * K, 7 * K):
        town_polys.append([(cx, wall_y), (cx, wall_y - 4 * K), (cx + 4 * K, wall_y - 4 * K), (cx + 4 * K, wall_y)])
    # Houses with pitched roofs, jostling inside the wall.
    houses = []
    hx = tx - 58 * K
    while hx < tx + 52 * K:
        w = trng.uniform(9, 18) * K
        h = trng.uniform(10, 22) * K
        roof = trng.uniform(5, 11) * K
        base = wall_y + 2 * K
        town_polys.append([(hx, base), (hx, base - h), (hx + w / 2, base - h - roof), (hx + w, base - h), (hx + w, base)])
        houses.append((hx, base - h, w, h))
        hx += w * trng.uniform(0.7, 1.0)
    # Two round towers with conical caps, one broken.
    for cx, hgt, broken in ((tx - 60 * K, 44 * K, False), (tx + 56 * K, 36 * K, True)):
        town_polys.append([(cx - 7 * K, wall_y + 4 * K), (cx - 7 * K, wall_y - hgt), (cx + 7 * K, wall_y - hgt),
                           (cx + 7 * K, wall_y + 4 * K)])
        if broken:
            town_polys.append([(cx - 7 * K, wall_y - hgt), (cx - 3 * K, wall_y - hgt - 6 * K), (cx + 1 * K, wall_y - hgt - 2 * K),
                               (cx + 7 * K, wall_y - hgt - 9 * K), (cx + 7 * K, wall_y - hgt)])
        else:
            town_polys.append([(cx - 9 * K, wall_y - hgt), (cx, wall_y - hgt - 18 * K), (cx + 9 * K, wall_y - hgt)])
    # The gothic church: nave, tower, and a tall needle spire with pinnacles - the landmark.
    sx = tx + 8 * K
    town_polys.append([(sx - 26 * K, wall_y), (sx - 26 * K, wall_y - 30 * K), (sx - 4 * K, wall_y - 44 * K),
                       (sx + 2 * K, wall_y - 30 * K), (sx + 2 * K, wall_y)])
    town_polys.append([(sx, wall_y), (sx, wall_y - 62 * K), (sx + 14 * K, wall_y - 62 * K), (sx + 14 * K, wall_y)])
    town_polys.append([(sx - 1 * K, wall_y - 62 * K), (sx + 7 * K, wall_y - 150 * K), (sx + 15 * K, wall_y - 62 * K)])
    for px in (sx - 2 * K, sx + 16 * K):
        town_polys.append([(px - 2 * K, wall_y - 60 * K), (px, wall_y - 76 * K), (px + 2 * K, wall_y - 60 * K)])
    town = saturate(crag + mask_from(lambda dr: wrap_polys(dr, town_polys)))
    layers.append((town, "#141920", "#1A2028"))

    # 5. The ruined windmill on a knoll (near-mid band, the other side of the sky).
    mx = W * 0.83
    knoll_h = H * 0.055 * np.exp(-(xdist(mx) / (W * 0.03)) ** 2)
    knoll = fill_below(HORIZON - knoll_h)
    mb = HORIZON - H * 0.05
    mill_polys = [[(mx - 16 * K, mb), (mx - 10 * K, mb - 60 * K), (mx + 10 * K, mb - 60 * K), (mx + 16 * K, mb)],
                  [(mx - 12 * K, mb - 60 * K), (mx - 4 * K, mb - 70 * K), (mx + 6 * K, mb - 71 * K), (mx + 12 * K, mb - 60 * K)]]

    def mill(dr):
        wrap_polys(dr, mill_polys)
        hub = (mx - 2 * K, mb - 64 * K)
        # Sails: one intact, one snapped short, one hanging, one gone.
        for ang, L, w in ((-0.9, 70, 5), (0.75, 34, 5), (2.3, 62, 4)):
            ex, ey = hub[0] + np.cos(ang) * L * K, hub[1] - np.sin(ang) * L * K
            dr.line([hub, (ex, ey)], fill=255, width=w * K)
            # Lattice frame of the sail: a thin bar offset parallel to the stock.
            ox, oy = -np.sin(ang) * 7 * K, -np.cos(ang) * 7 * K
            sx0 = hub[0] + np.cos(ang) * 14 * K
            sy0 = hub[1] - np.sin(ang) * 14 * K
            dr.line([(sx0 + ox, sy0 + oy), (ex + ox * 0.9, ey + oy * 0.9)], fill=255, width=2 * K)
    windmill = saturate(knoll + mask_from(mill))
    layers.append((windmill, "#14181F", "#1A1F27"))

    # 6. The black pine edge along the horizon: a ragged band of spikes, dense.
    frng = ctx.rng("forest")
    # Lower in the viaduct's valley (marsh along the river), so the arches read.
    band_h = H * (0.018 + 0.03 * profile(ctx.rng("fb"), W / 40, octaves=3)) * (1 - 0.6 * valley)
    pines = []
    x = 0.0
    while x < W:
        hgt = frng.uniform(0.6, 1.6) * band_h[int(x) % W] + frng.uniform(0, 10) * K
        w = frng.uniform(4, 9) * K
        base = HORIZON + 2 * K
        # Tiered pine: a spike with two shoulders.
        top = base - hgt - band_h[int(x) % W] * 0.5
        pines.append([(x - w, base), (x - w * 0.6, top + hgt * 0.55), (x - w * 0.8, top + hgt * 0.6),
                      (x - w * 0.35, top + hgt * 0.3), (x - w * 0.5, top + hgt * 0.34), (x, top),
                      (x + w * 0.5, top + hgt * 0.34), (x + w * 0.35, top + hgt * 0.3), (x + w * 0.8, top + hgt * 0.6),
                      (x + w * 0.6, top + hgt * 0.55), (x + w, base)])
        x += w * frng.uniform(0.5, 1.1)
    forest = saturate(fill_below(HORIZON - band_h * 0.6) + mask_from(lambda dr: wrap_polys(dr, pines)))
    layers.append((forest, "#0B0E11", "#0E1216"))

    # Composite far -> near. Each band lightens toward the horizon (fog pools low).
    ys = np.arange(H, dtype=np.float32)[:, None]
    haze = smoothstep(HORIZON - H * 0.35, HORIZON, ys)
    col = np.zeros((H, W, 3), np.float32)
    alpha = np.zeros((H, W), np.float32)
    for m, top_c, low_c in layers:
        c = lerp(hexc(top_c), hexc(low_c), np.broadcast_to(haze, (H, W)))
        col = lerp(col, c, m)
        alpha = saturate(alpha + m * (1 - alpha))
    # A little value noise inside the silhouettes so they aren't flat vector fills.
    grain = noise.fbm01(ctx.rng("sg"), (H // 4, W // 4), 6, octaves=3)
    grain = np.repeat(np.repeat(grain, 4, 0), 4, 1)
    col = col * (0.85 + 0.3 * grain)[..., None]

    # Lit windows in the town: a handful, amber, tiny. Emissive.
    wrng = ctx.rng("windows")
    lit = np.zeros((H, W), np.float32)
    picks = wrng.choice(len(houses), size=min(9, len(houses)), replace=False)
    for i in picks:
        hx, hy, w, h = houses[i]
        wx = int(hx + wrng.uniform(0.25, 0.7) * w)
        wy = int(hy + wrng.uniform(0.3, 0.7) * h)
        sz = int(wrng.choice([2, 2, 4])) if True else 2
        lit[wy:wy + sz, wx % W:(wx % W) + sz] = 1.0
    # One in the church tower, one lower on the crag (a guard post).
    lit[int(wall_y - 40 * K):int(wall_y - 40 * K) + 2, int(sx + 6 * K):int(sx + 6 * K) + 2] = 1
    lit[int(crag_top + 60 * K):int(crag_top + 60 * K) + 2, int(tx - 30 * K):int(tx - 30 * K) + 4] = 0.8
    col = lerp(col, hexc("#E0A050"), lit)

    # Smoke: from two town chimneys and a far foundry; drifting right with the wind, widening,
    # thinning. Semi-transparent over the sky.
    smoke_a = np.zeros((H, W), np.float32)
    snoise = noise.fbm01(ctx.rng("smoke"), (H // 2, W // 2), 10, octaves=4)
    snoise = np.repeat(np.repeat(snoise, 2, 0), 2, 1)
    X, Y = np.meshgrid(xs, np.arange(H, dtype=np.float32))
    for (ox, oy, strength, width) in ((tx - 20 * K, wall_y - 26 * K, 0.7, 5 * K), (tx + 36 * K, wall_y - 22 * K, 0.5, 4 * K),
                                      (W * 0.47, HORIZON - H * 0.02, 0.8, 9 * K)):
        rise = np.clip(oy - Y, 0, None)
        cx = ox + rise * 0.55 + 12 * K * np.sin(rise / (60 * K))
        wd = width + rise * 0.22
        dxs = np.abs(X - cx)
        dxs = np.minimum(dxs, W - dxs)
        col_m = np.exp(-(dxs / wd) ** 2) * (Y < oy) * np.exp(-rise / (H * 0.6))
        smoke_a = np.maximum(smoke_a, col_m * strength * (0.5 + 0.9 * snoise))
    smoke_a = saturate(smoke_a * 1.2) * 0.85
    # Smoke drifts in front of the far bands too (it's nearer than the mountains), thinly.
    smoke_col = hexc("#30363E") * (0.8 + 0.4 * snoise)[..., None]
    col = lerp(col, lerp(col, smoke_col, 0.6), smoke_a * (alpha > 0.5))
    col = np.where((alpha > 0.5)[..., None], col, lerp(col, smoke_col, saturate(smoke_a * 4)))
    alpha = saturate(alpha + smoke_a * (1 - alpha))

    # Below the horizon: dark ground haze, opaque, lighter just under the horizon line.
    below = ys >= HORIZON + 2 * K
    gh = smoothstep(HORIZON, H, ys)
    ground = lerp(hexc("#161B21"), hexc("#07090B"), np.broadcast_to(gh, (H, W)))
    ground = ground * (0.9 + 0.2 * grain)[..., None]
    col = np.where(below[..., None], ground, col)
    alpha = np.where(below, 1.0, alpha)

    s = np.zeros((H, W), np.float32)
    g = np.zeros((H, W), np.float32)
    return ctx.out(col, s, g, emissive=lit, alpha=alpha, tiling=False, factor=K, wrapsU=True,
                   horizonV=round(HORIZON / H, 4), grain=0.02, chroma_block=1,
                   procedural="layered silhouettes: mountains, ridges, viaduct, town on crag, windmill, pine edge, smoke")
