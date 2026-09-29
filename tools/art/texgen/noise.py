"""Periodic noise and shape helpers. Everything here wraps on both axes, so anything built only
from these functions tiles by construction (the FFT is periodic; voronoi uses toroidal distance;
shifts use np.roll). That is how the procedural textures stay seamless without a seam fix.

Image convention: row 0 is the TOP of the surface (up), rows increase downward, so "running
down" means +row. Columns are u, rows are v.
"""
from __future__ import annotations

import numpy as np


def _freqs(h, w):
    fy = np.fft.fftfreq(h).astype(np.float32)[:, None]
    fx = np.fft.fftfreq(w).astype(np.float32)[None, :]
    return fy, fx


def blur(img: np.ndarray, sx: float, sy: float | None = None) -> np.ndarray:
    """Periodic gaussian blur (sigma in pixels, anisotropic allowed). Works on HxW or HxWxC."""
    sy = sx if sy is None else sy
    h, w = img.shape[:2]
    fy, fx = _freqs(h, w)
    g = np.exp(-2 * (np.pi ** 2) * ((fx * sx) ** 2 + (fy * sy) ** 2)).astype(np.float32)
    if img.ndim == 3:
        g = g[..., None]
        out = np.fft.ifft2(np.fft.fft2(img, axes=(0, 1)) * g, axes=(0, 1)).real
    else:
        out = np.fft.ifft2(np.fft.fft2(img) * g).real
    return out.astype(np.float32)


def normalize(x: np.ndarray, lo_pct=1.0, hi_pct=99.0) -> np.ndarray:
    """Robustly map to 0..1 by percentiles (so one outlier texel can't flatten the range)."""
    lo, hi = np.percentile(x, [lo_pct, hi_pct])
    return np.clip((x - lo) / max(hi - lo, 1e-6), 0, 1).astype(np.float32)


def standardize(x):
    x = x - x.mean()
    return (x / max(float(x.std()), 1e-6)).astype(np.float32)


def fbm(rng, shape, scale: float, octaves=4, gain=0.5, stretch=(1.0, 1.0)) -> np.ndarray:
    """Tileable fractal noise, zero-mean unit-std. `scale` is the largest feature sigma in px;
    stretch=(sx, sy) elongates features (e.g. (0.2, 3) for streaks running down)."""
    h, w = shape
    total = np.zeros(shape, np.float32)
    amp = 1.0
    s = scale
    for _ in range(octaves):
        white = rng.standard_normal(shape).astype(np.float32)
        total += amp * standardize(blur(white, max(s * stretch[0], 0.3), max(s * stretch[1], 0.3)))
        amp *= gain
        s /= 2
    return standardize(total)


def fbm01(rng, shape, scale, octaves=4, gain=0.5, stretch=(1.0, 1.0)):
    return normalize(fbm(rng, shape, scale, octaves, gain, stretch))


def white(rng, shape):
    return rng.random(shape, dtype=np.float32)


def block_noise(rng, shape, block: int):
    """Nearest-neighbour upsampled noise: chunky NxN texel blocks (the crunchy look)."""
    h, w = shape
    small = rng.random((h // block, w // block), dtype=np.float32)
    return np.repeat(np.repeat(small, block, 0), block, 1)


def voronoi(rng, shape, n: int, aspect=(1.0, 1.0), jitter=0.9, offsets=False):
    """Tileable voronoi on a torus. Returns (f1, f2, cell_index) with distances in pixels, plus
    (dx, dy) - each texel's signed offset from its cell's point - if offsets=True.
    Points are jittered on a grid so cells are even-sized (stones, lumps, scales)."""
    h, w = shape
    gx = max(1, int(round(np.sqrt(n * w / h))))
    gy = max(1, int(round(n / gx)))
    pts = []
    for j in range(gy):
        for i in range(gx):
            ox, oy = (rng.random(2) - 0.5) * jitter
            pts.append(((i + 0.5 + ox) * w / gx, (j + 0.5 + oy) * h / gy))
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    f1 = np.full(shape, 1e9, np.float32)
    f2 = np.full(shape, 1e9, np.float32)
    idx = np.zeros(shape, np.int32)
    ox = np.zeros(shape, np.float32)
    oy = np.zeros(shape, np.float32)
    for k, (px, py) in enumerate(pts):
        sx = (xs - px + w / 2) % w - w / 2            # signed, wrapped
        sy = (ys - py + h / 2) % h - h / 2
        dx = np.abs(sx) * aspect[0]
        dy = np.abs(sy) * aspect[1]
        d = np.sqrt(dx * dx + dy * dy)
        closer = d < f1
        f2 = np.where(closer, f1, np.minimum(f2, d))
        idx = np.where(closer, k, idx)
        f1 = np.where(closer, d, f1)
        if offsets:
            ox = np.where(closer, sx, ox)
            oy = np.where(closer, sy, oy)
    if offsets:
        return f1, f2, idx, ox, oy
    return f1, f2, idx


def cell_values(rng, idx, count=None):
    """A random 0..1 value per voronoi cell."""
    count = int(idx.max()) + 1 if count is None else count
    return rng.random(count, dtype=np.float32)[idx]


def grad(h: np.ndarray):
    """Periodic central-difference gradient (d/dx, d/dy)."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    return gx, gy


def normals_from_height(h: np.ndarray, strength: float) -> np.ndarray:
    gx, gy = grad(h)
    n = np.stack([-gx * strength, -gy * strength, np.ones_like(h)], axis=-1)
    return (n / np.linalg.norm(n, axis=-1, keepdims=True)).astype(np.float32)


def bake_light(h: np.ndarray, strength: float, amount: float, light=(-0.35, -0.75, 0.55)) -> np.ndarray:
    """Painted-in lighting (GDD §27 "baked shadows"): a lambert term from the height's normals
    with the light up and a little left (row 0 is up, so y is negative), rescaled so a flat
    texel is exactly 1. Returns a multiplier."""
    n = normals_from_height(h, strength)
    l = np.array(light, np.float32)
    l /= np.linalg.norm(l)
    d = np.clip((n * l).sum(-1), 0, 1)
    return (1 + amount * (d / l[2] - 1)).astype(np.float32)


def cavity_from_height(h: np.ndarray, sigma: float) -> np.ndarray:
    """0..1, high in pits and cracks: how far below its neighbourhood a texel sits."""
    d = blur(h, sigma) - h
    return normalize(np.maximum(d, 0), 0, 99.5)


def convexity_from_height(h: np.ndarray, sigma: float) -> np.ndarray:
    """0..1, high on ridges and edges: where paint chips and metal gets rubbed bright."""
    d = h - blur(h, sigma)
    return normalize(np.maximum(d, 0), 0, 99.5)


def cavity_from_normal(nrm: np.ndarray, sigma: float = 1.0) -> np.ndarray:
    """0..1 cavity from a tangent-space normal map (divergence of the xy components: normals
    pointing inward on both sides of a pit converge)."""
    nx = nrm[..., 0] * 2 - 1
    ny = nrm[..., 1] * 2 - 1
    # OpenGL-style normal map (ambientCG ships _NormalGL): +y is up in the image, i.e. -row.
    div = (np.roll(nx, -1, 1) - np.roll(nx, 1, 1)) - (np.roll(ny, -1, 0) - np.roll(ny, 1, 0))
    div = blur(div.astype(np.float32), sigma)
    return normalize(np.maximum(div, 0), 0, 99.5)


def drip(seed: np.ndarray, decay: float, jitter_rng=None, wander=0.0, passes=2) -> np.ndarray:
    """Streaks running DOWN the surface from a seed mask: each row keeps the max of itself and
    the row above times `decay`. Wraps vertically (two passes), so the result still tiles.
    `wander` blurs sideways a little each row so streaks aren't ruler-straight."""
    h, w = seed.shape
    out = seed.astype(np.float32).copy()
    carry = np.zeros(w, np.float32)
    rowdecay = np.full(w, decay, np.float32)
    if jitter_rng is not None:
        # Each column runs a little differently: some streaks long, some short.
        rowdecay = np.clip(decay + (jitter_rng.random(w, dtype=np.float32) - 0.5) * (1 - decay) * 1.5, 0, 0.999)
    for _ in range(passes):
        for y in range(h):
            carry = np.maximum(out[y], carry * rowdecay)
            if wander > 0:
                carry = carry * (1 - wander) + wander * 0.5 * (np.roll(carry, 1) + np.roll(carry, -1))
            out[y] = carry
    return out


def box_down(img: np.ndarray, factor: int) -> np.ndarray:
    """Box filter downscale (the brief: NOT Lanczos, which rings and smooths the grain away)."""
    if factor == 1:
        return img
    h, w = img.shape[:2]
    if img.ndim == 3:
        return img.reshape(h // factor, factor, w // factor, factor, img.shape[2]).mean((1, 3)).astype(np.float32)
    return img.reshape(h // factor, factor, w // factor, factor).mean((1, 3)).astype(np.float32)


def box_resize(img: np.ndarray, out_h: int, out_w: int) -> np.ndarray:
    """Box-filter resize for integer ratios in each axis (sources are 1024 or 1024x512)."""
    h, w = img.shape[:2]
    if h % out_h == 0 and w % out_w == 0:
        fy, fx = h // out_h, w // out_w
        if img.ndim == 3:
            return img.reshape(out_h, fy, out_w, fx, img.shape[2]).mean((1, 3)).astype(np.float32)
        return img.reshape(out_h, fy, out_w, fx).mean((1, 3)).astype(np.float32)
    # Upscale: nearest (keeps it crunchy).
    ys = (np.arange(out_h) * h // out_h)
    xs = (np.arange(out_w) * w // out_w)
    return img[ys][:, xs].astype(np.float32)


def make_tileable(img: np.ndarray, band: float = 0.18) -> np.ndarray:
    """Seam fix for a cropped photo: cross-fade each edge band with the image rolled by half.
    The rolled copy is continuous across the tile border (its border is the original's middle),
    so fading to it near the edges hides the crop's seam; the middle stays untouched photo."""
    h, w = img.shape[:2]
    out = img.astype(np.float32)
    for axis, size in ((1, w), (0, h)):
        rolled = np.roll(out, size // 2, axis)
        t = np.arange(size, dtype=np.float32)
        d = np.minimum(t, size - 1 - t) / (size * band)
        wgt = np.clip(d, 0, 1)
        wgt = wgt * wgt * (3 - 2 * wgt)
        shape = [1] * out.ndim
        shape[axis] = size
        wgt = wgt.reshape(shape)
        # The fade itself can show as a soft band on high-contrast photos; the rolled copy is
        # blended with the original in the band rather than replacing it outright.
        out = out * wgt + rolled * (1 - wgt)
    return out


def seam_ratio(img: np.ndarray) -> float:
    """How visible the wrap seam is. For each axis, the mean |difference| across every pair of
    adjacent lines (the wrap pair included) is a profile; the score is the wrap pair's value over
    the 95th percentile of the rest. Deliberate structure (a plate seam) shows up elsewhere in
    the profile too, so it doesn't count; an accidental seam is an outlier. ~1 or less is clean."""
    a = img.astype(np.float32)
    if a.ndim == 3:
        a = a.mean(-1)
    worst = 0.0
    for ax in (0, 1):
        prof = np.abs(a - np.roll(a, -1, ax)).mean(1 - ax)   # prof[i] = |line i - line i+1|
        wrap = prof[-1]
        ref = max(np.percentile(prof[:-1], 95), 0.8 * prof[:-1].max(), 1e-6)
        worst = max(worst, float(wrap / ref))
    return worst


def wrapdist(xs, ys, cx, cy, w, h):
    """Toroidal distance components from (cx, cy)."""
    dx = np.abs(xs - cx)
    dx = np.minimum(dx, w - dx)
    dy = np.abs(ys - cy)
    dy = np.minimum(dy, h - dy)
    return dx, dy


def grid(h, w):
    ys, xs = np.mgrid[0:h, 0:w].astype(np.float32)
    return xs, ys


def dome(d, r):
    """A rivet/bolt-head profile: height 1 at the centre, 0 at radius r."""
    t = np.clip(1 - (d / r) ** 2, 0, 1)
    return np.sqrt(t).astype(np.float32)


def stamp(dst: np.ndarray, src: np.ndarray, x: int, y: int, op="max", wrap=True):
    """Paste a small array into dst at (x, y) with wrap-around (keeps stamped details tiling)."""
    h, w = dst.shape[:2]
    sh, sw = src.shape[:2]
    ys = (np.arange(sh) + y) % h if wrap else np.arange(sh) + y
    xs = (np.arange(sw) + x) % w if wrap else np.arange(sw) + x
    if not wrap:
        ok_y = (ys >= 0) & (ys < h)
        ok_x = (xs >= 0) & (xs < w)
        src = src[ok_y][:, ok_x]
        ys, xs = ys[ok_y], xs[ok_x]
    region = dst[np.ix_(ys, xs)]
    if op == "max":
        dst[np.ix_(ys, xs)] = np.maximum(region, src)
    elif op == "min":
        dst[np.ix_(ys, xs)] = np.minimum(region, src)
    elif op == "add":
        dst[np.ix_(ys, xs)] = region + src
    elif op == "set":
        dst[np.ix_(ys, xs)] = src
    return dst
