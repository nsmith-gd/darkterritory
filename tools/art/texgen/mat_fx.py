"""VFX flipbooks (GDD §31: "steam venting, sparks, smoke trails, cinders, drifting fog...
Mechanical first, supernatural second"; "clear and slightly rough, never hyper-smooth").
Frames are 64 px (built at 128) with a posterised alpha so they stay chunky on screen."""
from __future__ import annotations

import numpy as np

from . import core, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def puff_frames(ctx, key, grid=4, cell=128, grow=(0.2, 0.42), erode=(0.0, 0.8), blobs=7, rise=0.06,
                fade=1.0):
    """Frames of an expanding, dissolving puff: a cluster of cauliflower lobes that grows,
    lifts a little and gets eaten from the edges by noise. Returns (density, lit) per frame:
    density drives alpha, lit (0..1) the painted-in top light on each lobe."""
    rng = ctx.rng(key)
    n = grid * grid
    xs, ys = noise.grid(cell, cell)
    xs = xs / cell - 0.5
    ys = ys / cell - 0.5
    # Lobe centres in units of the puff radius, a big core lobe first.
    offs = np.clip(rng.normal(0, 0.4, (blobs, 2)), -0.6, 0.6)
    offs[0] = 0
    radii = rng.uniform(0.55, 0.85, blobs)
    radii[0] = 1.0
    big = cell * 2
    lumps = noise.fbm01(rng, (big, big), 6, octaves=3)
    edge_n = noise.fbm01(rng, (big, big), 9, octaves=4)
    frames = []
    for f in range(n):
        t = f / (n - 1)
        R = grow[0] + (grow[1] - grow[0]) * np.sqrt(t)
        dens = np.zeros((cell, cell), np.float32)
        lit = np.zeros((cell, cell), np.float32)
        for (ox, oy), rr in zip(offs * R, radii):
            cy = oy - rise * t
            rad = R * rr * 0.7
            d2 = ((xs - ox) ** 2 + (ys - cy) ** 2) / rad ** 2
            lobe = saturate(1 - d2)
            # Each lobe is lit from above: brighter on its upper side.
            up = saturate(0.55 - (ys - cy) / rad * 0.6)
            lit = np.where(lobe > dens, up, lit)
            dens = np.maximum(dens, lobe)
        sh = int(t * cell * 0.4)
        ln = lumps[sh:sh + cell, sh // 2:sh // 2 + cell]
        en = edge_n[cell - sh:2 * cell - sh, sh:sh + cell]
        # Cauliflower breakup: lumps add thickness and their own little top-light.
        dens = dens * (0.7 + 0.6 * ln)
        lit = saturate(lit * 0.7 + 0.5 * saturate(ln - np.roll(ln, 3, 0) + 0.5) - 0.1)
        e = erode[0] + (erode[1] - erode[0]) * t
        dens = saturate((dens - e * en) * 1.6)
        dens = dens * (1 - t) ** fade if fade > 0 else dens
        dens = dens * smoothstep(0.5, 0.44, np.maximum(np.abs(xs), np.abs(ys)))
        frames.append((dens, lit))
    return frames


def sheet(frames, grid, cell, fn):
    A = grid * cell
    D = np.zeros((A, A, 3), np.float32)
    Al = np.zeros((A, A), np.float32)
    E = np.zeros((A, A), np.float32)
    for i, fr in enumerate(frames):
        cy, cx = divmod(i, grid)
        d, a, e = fn(*fr) if isinstance(fr, tuple) else fn(fr)
        D[cy * cell:(cy + 1) * cell, cx * cell:(cx + 1) * cell] = d
        Al[cy * cell:(cy + 1) * cell, cx * cell:(cx + 1) * cell] = a
        E[cy * cell:(cy + 1) * cell, cx * cell:(cx + 1) * cell] = e
    return D, Al, E


def posterise(a, levels=10):
    return np.round(saturate(a) * (levels - 1)) / (levels - 1)


@texture("fx_smoke", "fx", tile=None)
def fx_smoke(ctx):
    """Billowing sooty smoke puff, 4x4 frames: dense and dark at birth, spreading and thinning."""
    frames = puff_frames(ctx, "smoke", erode=(0.05, 0.6), fade=0.6)

    def colour(dens, lit):
        c = core.apply_ramp(saturate(lit * 0.8 + 0.1), ["#0E0E0E", "#1C1C1C", "#2E2D2C", "#46443F", "#5E5B55"])
        return c, posterise(saturate(dens * 1.6) * 0.92), np.zeros_like(dens)
    D, A, E = sheet(frames, 4, 128, colour)
    return ctx.out(D, np.zeros_like(A), np.zeros_like(A), alpha=A, tiling=False, factor=2, frames=[4, 4],
                   procedural="blob cluster + sliding noise erosion, top-lit, posterised alpha", grain=0.03,
                   chroma_block=1)


@texture("fx_steam", "fx", tile=None)
def fx_steam(ctx):
    """Steam puff, 4x4 frames: whiter, grows faster, wispier, gone by the last frame."""
    frames = puff_frames(ctx, "steam", grow=(0.16, 0.44), erode=(0.1, 0.9), blobs=10, rise=0.08, fade=0.9)

    def colour(dens, lit):
        c = core.apply_ramp(saturate(lit * 0.8 + 0.15), ["#3E444A", "#5E646A", "#80868C", "#A2A6AA", "#BEC2C4"])
        return c, posterise(saturate(dens * 1.0) * 0.8), np.zeros_like(dens)
    D, A, E = sheet(frames, 4, 128, colour)
    return ctx.out(D, np.zeros_like(A), np.zeros_like(A), alpha=A, tiling=False, factor=2, frames=[4, 4],
                   procedural="blob cluster + sliding noise erosion, top-lit, posterised alpha", grain=0.03,
                   chroma_block=1)


def glow(xs, ys, cx, cy, r):
    return np.exp(-(((xs - cx) ** 2 + (ys - cy) ** 2) / (r * r)))


def streak_mask(xs, ys, x0, y0, x1, y1, w):
    """A tapered streak from (x0, y0) (head, bright) to (x1, y1) (tail)."""
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy
    t = saturate(((xs - x0) * dx + (ys - y0) * dy) / L2)
    px, py = x0 + t * dx, y0 + t * dy
    d = np.sqrt((xs - px) ** 2 + (ys - py) ** 2)
    return np.exp(-(d / (w * (1 - 0.7 * t))) ** 2) * (1 - t) ** 1.5


@texture("fx_spark", "fx", tile=None)
def fx_spark(ctx):
    """Sparks and embers (additive; alpha = intensity), 2x2: a single ember, a flying spark
    streak, a cluster of cinders, a grinding-spark burst."""
    rng = ctx.rng("sparks")
    C = 128
    xs, ys = noise.grid(C, C)
    frames = []
    frames.append(saturate(glow(xs, ys, 64, 64, 6) * 1.2 + glow(xs, ys, 64, 64, 18) * 0.35))
    frames.append(saturate(streak_mask(xs, ys, 92, 34, 30, 98, 4.5) * 1.3 + glow(xs, ys, 92, 34, 10) * 0.4))
    cl = np.zeros((C, C), np.float32)
    for _ in range(7):
        cx, cy = rng.uniform(28, 100, 2)
        r = rng.uniform(2.5, 5)
        cl += glow(xs, ys, cx, cy, r) * rng.uniform(0.6, 1.1) + glow(xs, ys, cx, cy, r * 3) * 0.15
    frames.append(saturate(cl))
    b = np.zeros((C, C), np.float32)
    for k in range(9):
        a = rng.uniform(-np.pi * 0.95, -np.pi * 0.05)
        L = rng.uniform(25, 55)
        b += streak_mask(xs, ys, 64 + np.cos(a) * L, 90 + np.sin(a) * L, 64, 90, 2.5)
    b += glow(xs, ys, 64, 90, 8) * 0.8
    frames.append(saturate(b))

    def colour(i):
        c = core.apply_ramp(saturate(i), ["#200402", "#A02810", "#D06020", "#F0A040", "#FFF0C8"])
        return c, posterise(i, 12), i
    D, A, E = sheet(frames, 2, C, colour)
    return ctx.out(D, np.zeros_like(A), np.zeros_like(A), emissive=E, alpha=A, tiling=False, factor=2,
                   frames=[2, 2], blend="additive", procedural="glows and tapered streaks", grain=0.0, chroma_block=1)


@texture("fx_fog", "fx", tile=None)
def fx_fog(ctx):
    """A soft fog card (256x128): horizontal wisps, faded out at every edge so cards overlap."""
    H, W = 256, 512
    rng = ctx.rng("fog")
    n = noise.fbm01(rng, (H, W), 26, octaves=5, stretch=(3.0, 0.7))
    n2 = noise.fbm01(rng, (H, W), 10, octaves=3, stretch=(4.0, 0.6))
    xs, ys = noise.grid(H, W)
    fade = smoothstep(0, 0.3, xs / W) * smoothstep(1, 0.7, xs / W) * smoothstep(0, 0.35, ys / H) * smoothstep(1, 0.6, ys / H)
    a = saturate((n * 0.7 + n2 * 0.3 - 0.3) * 1.6) * fade
    d = lerp(hexc("#3A4450"), hexc("#6A7480"), n2)
    return ctx.out(d, np.zeros_like(a), np.zeros_like(a), alpha=posterise(a * 0.8, 16), tiling=False, factor=2,
                   procedural="stretched fractal wisps, edge fade", grain=0.02, chroma_block=1)


@texture("fx_flame", "fx", tile=None)
def fx_flame(ctx):
    """Flames (additive; alpha = intensity), 4x4 frames that loop: a broad licking tongue with two smaller ones beside
    it, eaten into from the edges by noise scrolling up through them, hot white-yellow at the root, deep red at the
    ragged tips. For a car fire, the furnace at the firehole, a muzzle's lick of flame. The noise is periodic and the
    scroll goes once round it in 16 frames, so the loop has no seam."""
    rng = ctx.rng("flame")
    C, n = 128, 16
    xs, ys = noise.grid(C, C)
    x = xs / C
    v = 1 - ys / C                      # 0 at the root (the frame's foot), 1 at the top
    lick = noise.fbm01(rng, (C, C), 9, octaves=4, stretch=(0.6, 1.4))
    sway = noise.fbm01(rng, (C, C), 22, octaves=2)
    frames = []
    for f in range(n):
        t = f / n
        sh = int(round(t * C))
        nl = np.roll(lick, sh, axis=0)          # scrolls upward through the flame, once round in the loop
        ns = np.roll(sway, sh // 2, axis=0)
        I = np.zeros((C, C), np.float32)
        for cx, h0, w0, ph in ((0.5, 1.05, 0.36, 0.0), (0.27, 0.72, 0.19, 0.37), (0.73, 0.78, 0.2, 0.71)):
            h = h0 * (0.86 + 0.14 * np.sin(2 * np.pi * (t * 2 + ph)))
            vv = v / h
            # A tongue: round at its root, widest a quarter up, drawn out to a point; the sway bends it more the higher
            # it goes, so the tips lick about while the root stays put.
            half = w0 * saturate(1 - vv) ** 1.1 * (0.55 + 0.45 * smoothstep(0.0, 0.28, vv)) + 0.006
            dx = x - cx - 0.2 * (ns - 0.5) * vv ** 1.3
            across = saturate(1 - (np.abs(dx) / half) ** 1.6)
            # The root rises out of nothing over a ragged band (noise-broken), not off a ruled line: tongues side by side
            # in a car fire otherwise join their feet into one straight bright edge, the card showing.
            root = smoothstep(0.0, 0.1 + 0.16 * np.roll(nl, C // 5, axis=1), v)
            body = across * root * saturate(1.1 - vv) * (1 - 0.55 * vv)
            I = np.maximum(I, body)
        # Eaten from the edges and the top by the rising noise: the tips break into separate licks; the inside is uneven.
        I = saturate((I - (0.04 + 0.42 * v) * nl) * 1.5) * (0.75 + 0.35 * np.roll(nl, C // 3, axis=1))
        frames.append(I)

    def colour(i):
        c = core.apply_ramp(saturate(i), ["#1A0300", "#7A1606", "#C8420E", "#F08A24", "#FFD27A", "#FFF6DC"])
        return c, posterise(i, 14), i
    D, A, E = sheet(frames, 4, C, colour)
    return ctx.out(D, np.zeros_like(A), np.zeros_like(A), emissive=E, alpha=A, tiling=False, factor=2,
                   frames=[4, 4], blend="additive", procedural="noise-eaten flame tongues, ragged roots, scrolled once round a loop",
                   grain=0.0, chroma_block=1)


@texture("fx_flash", "fx", tile=None)
def fx_flash(ctx):
    """A black-powder muzzle flash (additive), 2x2 variants: a ragged star of flame jets round a white-hot core, the jets
    of uneven length, a few sparks flung past them. Seen end on or from the side, it's a burst; stretched, a jet."""
    rng = ctx.rng("flash")
    C = 128
    xs, ys = noise.grid(C, C)
    dx, dy = (xs - 64) / 64, (ys - 64) / 64
    r = np.sqrt(dx * dx + dy * dy) + 1e-6
    a = np.arctan2(dy, dx)
    frames = []
    for k in range(4):
        jets = rng.integers(5, 9)
        rays = np.zeros((C, C), np.float32)
        for _ in range(jets):
            a0 = rng.uniform(-np.pi, np.pi)
            L = rng.uniform(0.6, 1.0)
            w = rng.uniform(0.18, 0.34)
            da = np.angle(np.exp(1j * (a - a0)))
            rays = np.maximum(rays, saturate(1 - np.abs(da) / (w * (1.1 - r / L))) * saturate(1 - r / L))
        breakup = noise.fbm01(rng, (C, C), 6, octaves=3)
        core_ = np.exp(-(r / 0.14) ** 2)
        I = saturate(rays * (0.55 + 0.6 * breakup) + core_ * 1.3 + np.exp(-(r / 0.45) ** 2) * 0.25)
        for _ in range(5):
            sa, sr = rng.uniform(-np.pi, np.pi), rng.uniform(0.5, 0.9)
            I = np.maximum(I, glow(xs, ys, 64 + np.cos(sa) * sr * 60, 64 + np.sin(sa) * sr * 60, 2.5) * 0.9)
        frames.append(I)

    def colour(i):
        c = core.apply_ramp(saturate(i), ["#200500", "#A03010", "#E87828", "#FFC060", "#FFF8E8"])
        return c, posterise(i, 12), i
    D, A, E = sheet(frames, 2, C, colour)
    return ctx.out(D, np.zeros_like(A), np.zeros_like(A), emissive=E, alpha=A, tiling=False, factor=2,
                   frames=[2, 2], blend="additive", procedural="ragged jet star round a hot core", grain=0.0, chroma_block=1)
