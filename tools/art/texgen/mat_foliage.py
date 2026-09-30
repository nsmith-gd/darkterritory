"""Foliage cards (GDD §30 "black forests", §26 "readability through silhouette"): alpha-tested
cutouts for crossed-card trees and ground tufts. The silhouette is the whole read - ragged,
chunky tiers, big gaps you can see fog through - so the cutout is drawn at 2x and box-filtered,
then hard-thresholded (pixel-hard edges, the PS2 look)."""
from __future__ import annotations

import numpy as np

from . import common as C
from . import core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def self_shadow(alpha, reach=10, amount=0.6):
    """Foliage under foliage is darker: shade by how much cover lies above each texel."""
    above = np.zeros_like(alpha)
    for k in range(2, reach * 3, 3):
        above += np.roll(alpha, k, 0) * (1 - k / (reach * 3))
    above = noise.normalize(above, 0, 99)
    return (1 - amount * above).astype(np.float32)


def rim(alpha, width=2.0):
    """Silhouette rim (texels near the edge of the cutout), for the cold moonlit edge."""
    inner = noise.blur(alpha, width) > 0.85
    return saturate(alpha - inner.astype(np.float32))


def card_out(ctx, d, alpha, s, g, procedural, **extra):
    # Colour bleeds outward under the cutout so mip levels don't fringe with black.
    a = alpha[..., None]
    bleed = noise.blur(d * a, 6) / np.maximum(noise.blur(alpha, 6)[..., None], 1e-3)
    d = d * a + bleed * (1 - a)
    return ctx.out(d, s, g, alpha=alpha, tiling=False, alpha_test=True, procedural=procedural, factor=2, **extra)


@texture("pine_card", "foliage", tile=None)
def pine_card(ctx):
    """A whole black-forest pine for crossed cards (256x512; three cards 0.62 x height wide).
    A SOLID, chunky mass (~50% of the card opaque): broad drooping tiers, wide at the base,
    each tier a filled skirt whose lower edge hangs in ragged needle clumps, a few holes where
    fog shows through. Near-black green, the underside of every tier in shadow, a faint
    blue-grey rim on the silhouette (§26: the tree reads by outline, not detail)."""
    H, W = 1024, 512
    rng = ctx.rng("tree")
    cx = W / 2
    top, base = 24, 900
    mass = np.zeros((H, W), np.float32)       # filled tier skirts
    tone = np.zeros((H, W), np.float32)       # lit tier tops -> 1, shadowed undersides -> 0
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    tiers = 11
    tips = []
    for t in range(tiers):
        f = (t + 0.5) / tiers
        y = top + (base - top) * f ** 0.9 - 20
        half = (28 + 232 * f ** 0.85) * rng.uniform(0.92, 1.06)
        droop = (40 + 70 * f) * rng.uniform(0.8, 1.2)
        thick = (55 + 55 * f) * rng.uniform(0.9, 1.1)
        lean = rng.normal(0, 8)
        # Skirt: between an upper curve (trunk -> tip, drooping) and a lower one (tip -> back up
        # under the trunk), each side slightly different so it isn't a Christmas-card triangle.
        for side in (-1, 1):
            if f > 0.2 and rng.random() < 0.12:
                continue                       # a tier with one side broken off
            hs = half * rng.uniform(0.8, 1.0)
            u = saturate((xs - cx) * side / hs)
            inside = ((xs - cx) * side >= 0) & ((xs - cx) * side <= hs)
            # Ragged upper and lower edges: 1D noise across the tier (branch ends, gaps).
            kx = np.arange(W, dtype=np.float32)
            n_up = np.interp(kx, np.linspace(0, W, 24), rng.normal(0, 1, 24))[None, :]
            n_lo = np.interp(kx, np.linspace(0, W, 40), rng.normal(0, 1, 40))[None, :]
            upper = y + droop * u ** 1.25 + lean * u + n_up * 9 * u
            lower = upper + thick * (1 - u ** 1.4) + 12 + n_lo * 20 * (0.3 + u)
            sk = inside & (ys >= upper) & (ys <= lower)
            mass = np.maximum(mass, sk.astype(np.float32))
            tone = np.where(sk, np.maximum(tone, saturate(1 - (ys - upper) / thick)), tone)
            tips.append((cx + side * hs, y + droop + lean, side, f))
    # Ragged edges: needle clumps hanging from each tier's lower edge and bristling at the tips.
    lines, widths, vals = [], [], []
    edge = saturate(noise.blur(mass, 3) - 0.0) * (1 - (noise.blur(mass, 3) > 0.97))
    ey, ex = np.nonzero(edge[::6, ::6] > 0.05)
    for y6, x6 in zip(ey, ex):
        px, py = x6 * 6 + rng.uniform(0, 6), y6 * 6 + rng.uniform(0, 6)
        for _ in range(2):
            a = rng.normal(np.pi / 2 + (0.5 if px > cx else -0.5) * -1, 0.6)
            ln = rng.uniform(10, 34)
            lines.append([(px, py), (px + np.cos(a) * ln, py + np.sin(a) * ln)])
            widths.append(rng.uniform(4, 8))
            vals.append(rng.uniform(0.2, 0.6))
    for tx, ty, side, f in tips:
        for _ in range(14):
            a = rng.normal(np.pi / 2 - side * 1.0, 0.6)
            ln = rng.uniform(14, 40) * (0.6 + 0.6 * f)
            sx, sy = tx + rng.normal(0, 10), ty + rng.normal(0, 8)
            lines.append([(sx, sy), (sx + np.cos(a) * ln, sy + np.sin(a) * ln)])
            widths.append(rng.uniform(4, 8))
            vals.append(rng.uniform(0.3, 0.7))
    # Leader.
    for _ in range(30):
        a = rng.normal(np.pi / 2, 0.8)
        ln = rng.uniform(10, 26)
        sx, sy = cx + rng.normal(0, 5), top + rng.uniform(0, 60)
        lines.append([(sx, sy), (sx + np.cos(a) * ln, sy + np.sin(a) * ln)])
        widths.append(rng.uniform(4, 6))
        vals.append(0.7)
    lines.append([(cx, top + 30), (cx + 2, H - 8)])     # trunk (shows under the lowest tier)
    widths.append(20)
    vals.append(0.15)
    fringe = draw.strokes((H, W), lines, widths, None, wrap=False)
    fval = draw.strokes((H, W), lines, widths, vals, wrap=False)
    # Holes: a few gaps inside the mass where the fog shows (the tree isn't a cut-out blob).
    holes = smoothstep(0.78, 0.8, noise.fbm01(ctx.rng("holes"), (H, W), 9, octaves=3)) * (np.abs(xs - cx) > 30)
    alpha = saturate(np.maximum(mass * (1 - holes), fringe))
    # Needle clumps: blocky light/dark breakup inside the mass, lit on their upper sides.
    clumps = noise.fbm01(ctx.rng("clump"), (H, W), 5, octaves=3)
    cl_lit = saturate(clumps - np.roll(clumps, 5, 0) + 0.5)
    t = np.where(mass > 0.5, 0.12 + 0.45 * tone + 0.4 * (cl_lit - 0.5) + 0.25 * (clumps - 0.5), fval)
    t = saturate(t + 0.15 * (noise.white(ctx.rng("n"), (H, W)) - 0.5))
    d = core.apply_ramp(t, "pine")
    d = d * self_shadow(alpha, 10, 0.4)[..., None]
    trunk = (fval > 0.1) & (fval < 0.18) & (mass < 0.5)
    d = lerp(d, hexc("#16120F"), trunk * 0.9)
    r = rim(alpha, 2.5)
    d = lerp(d, hexc("#34404A"), r * 0.4)
    s = np.full((H, W), 0.05, np.float32)
    g = np.full((H, W), 0.2, np.float32)
    return card_out(ctx, d, alpha, s, g, "tier skirts, ragged needle fringe, holes, shade and rim")


@texture("pine_bough", "foliage", tile=None)
def pine_bough(ctx):
    """One spruce bough seen from above, for the modelled pines' whorls (WorldKit.Pine: 512x256; the trunk end at the
    left, the tip at the right, laid flat and bent in the mesh so it droops). A frond, not a leaf: a twig with dozens of
    branchlets raked toward the tip, longest a third of the way out, each furred with needles both sides; gaps between
    them where the fog shows through. Darker at the base and deep in, lit toward the tips."""
    H, W = 512, 1024
    rng = ctx.rng("bough")
    x0, x1, cy = 24, W - 16, H / 2
    lines, widths, vals = [], [], []

    def twig_y(t):
        return cy + 14 * np.sin(t * np.pi * 0.8) + 8 * t

    # The branchlets, both sides, raked forward, each with its needles; drawn inner (dark) to outer (lit).
    for k in range(34):
        t = 0.04 + 0.92 * (k + rng.uniform(-0.3, 0.3)) / 34
        bx, by = x0 + t * (x1 - x0), twig_y(t)
        reach = (H * 0.46) * max(0.0, np.sin(np.pi * t ** 0.7)) ** 0.7 * rng.uniform(0.75, 1.0)
        for side in (-1, 1):
            ang = side * rng.uniform(0.75, 1.0)            # off the twig, raked forward
            pts = [(bx, by)]
            x, y, a = bx, by, ang
            steps = 6
            for _ in range(steps):
                a += rng.normal(0, 0.08) - side * 0.04     # curling a little toward the tip
                x += np.cos(a) * reach / steps
                y += np.sin(a) * reach / steps
                pts.append((x, y))
            lines.append(pts)
            widths.append(4.0)
            vals.append(0.1)
            # Needles along it, both sides, angled forward.
            for j in range(1, len(pts)):
                (xa, ya), (xb, yb) = pts[j - 1], pts[j]
                da = np.arctan2(yb - ya, xb - xa)
                seg = np.hypot(xb - xa, yb - ya)
                for q in np.arange(0, seg, 4.5):
                    px, py = xa + np.cos(da) * q, ya + np.sin(da) * q
                    near = j / len(pts)
                    for ns in (-1, 1):
                        na = da + ns * rng.uniform(0.6, 1.0)
                        ln = rng.uniform(9, 17) * (1.1 - 0.4 * near)
                        lines.append([(px, py), (px + np.cos(na) * ln, py + np.sin(na) * ln)])
                        widths.append(rng.uniform(2.5, 4.0))
                        vals.append(saturate(0.2 + 0.35 * t + 0.3 * near + rng.normal(0, 0.08)))
    lines.append([(x0 - 20, cy)] + [(x0 + t * (x1 - x0), twig_y(t)) for t in np.linspace(0.1, 1.0, 8)])
    widths.append(12)
    vals.append(0.06)
    alpha = saturate(draw.strokes((H, W), lines, widths, None, wrap=False))
    val = draw.strokes((H, W), lines, widths, vals, wrap=False)
    clumps = noise.fbm01(ctx.rng("clump"), (H, W), 5, octaves=3)
    t = saturate(val + 0.25 * (clumps - 0.5) + 0.12 * (noise.white(ctx.rng("n"), (H, W)) - 0.5))
    d = core.apply_ramp(t, "pine")
    d = d * self_shadow(alpha, 6, 0.3)[..., None]
    twig = (val > 0.03) & (val < 0.12)
    d = lerp(d, hexc("#1a130d"), twig * 0.85)
    r = rim(alpha, 2.0)
    d = lerp(d, hexc("#34404A"), r * 0.3)
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.15, np.float32)
    return card_out(ctx, d, alpha, s, g, "a frond: branchlets raked along a twig, furred with needles, gaps between")


def branch(rng, x, y, ang, length, width, depth, lines, widths):
    """Recursive gnarled branching for the dead tree."""
    pts = [(x, y)]
    steps = 5
    a = ang
    for _ in range(steps):
        a += rng.normal(0, 0.18)
        x += np.cos(a) * length / steps
        y += np.sin(a) * length / steps
        pts.append((x, y))
    lines.append(pts)
    widths.append(width)
    if depth == 0 or width < 3.0:
        return
    n = 2 if rng.random() < 0.7 else 3
    for i in range(n):
        spread = rng.uniform(0.35, 0.8) * (1 if i % 2 == 0 else -1)
        branch(rng, x, y, a + spread, length * rng.uniform(0.55, 0.78), width * rng.uniform(0.55, 0.72),
               depth - 1, lines, widths)


@texture("dead_tree_card", "foliage", tile=None)
def dead_tree_card(ctx):
    """A bare dead tree (256x512): a leaning trunk forking into gnarled, broken limbs."""
    H, W = 1024, 512
    rng = ctx.rng("tree")
    lines, widths = [], []
    base = (W / 2 + 10, H - 10)
    # Trunk: two segments, leaning.
    trunk = [base, (W / 2 - 6, H - 360), (W / 2 + 14, H - 560)]
    lines.append(trunk)
    widths.append(46)
    lines.append([base, (W / 2 - 6, H - 360)])
    widths.append(58)
    # Main limbs from the upper trunk.
    for (y, a, L, w) in [(H - 540, -np.pi / 2 - 0.35, 300, 28), (H - 560, -np.pi / 2 + 0.4, 280, 26),
                         (H - 420, -np.pi / 2 - 1.0, 200, 18), (H - 380, -np.pi / 2 + 1.1, 180, 17),
                         (H - 600, -np.pi / 2 + 0.05, 330, 22)]:
        branch(rng, W / 2 + 4, y, a, L, w, 5, lines, widths)
    # Snapped stubs.
    for y in (H - 250, H - 300, H - 460):
        s = rng.choice([-1, 1])
        lines.append([(W / 2, y), (W / 2 + s * rng.uniform(30, 50), y - rng.uniform(10, 25))])
        widths.append(9)
    alpha = draw.strokes((H, W), lines, widths, None, wrap=False)
    # Bark: vertical furrows in a dark grey-brown, lighter silvered patches where it's dead longest.
    fur = noise.fbm01(ctx.rng("bark"), (H, W), 2, octaves=3, stretch=(0.5, 5))
    silver = smoothstep(0.5, 0.8, noise.fbm01(ctx.rng("sil"), (H, W), 20))
    d = core.apply_ramp(saturate(0.25 + 0.45 * fur), "wood_grey")
    d = lerp(d, d * 0.6 + hexc("#141210") * 0.4, 1 - silver * 0.8)
    # Light from the upper left: the right side of each limb darker.
    shade = noise.blur(alpha, 3)
    gx, gy = noise.grad(shade)
    d = d * saturate(1 + (-gx * 1.0 - gy * 0.6) * 8)[..., None]
    d = lerp(d, hexc("#3A4450"), rim(alpha, 2) * 0.35)
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.15, np.float32)
    return card_out(ctx, d, alpha, s, g, "recursive gnarled branches (strokes), bark furrows")


@texture("grass_card", "foliage", tile=None)
def grass_card(ctx):
    """Dead grass tufts along the bottom edge (256x128): straw-olive blades, darker at the root.
    Tiles horizontally so a strip of cards can run along the lineside."""
    H, W = 256, 512
    rng = ctx.rng("grass")
    spread, widths, vals = [], [], []
    # Spiky tufts with plenty of cut-out between the blades (~40% opaque): most blades are
    # short, a few in each tuft reach toward the top; strays between the tufts.
    centres = [(c + rng.uniform(0.2, 0.8)) / 11 * W for c in range(11)]
    roots = [(cx + rng.normal(0, 11), rng.uniform(0.6, 1.0)) for cx in centres for _ in range(32)]
    roots += [(rng.uniform(0, W), 0.45) for _ in range(40)]
    for x, scale in roots:
        hgt = (40 + 200 * rng.random() ** 2.2) * scale
        # Blades fan out from the tuft's root.
        pts = draw.wander_line(rng, x, H - 1, -np.pi / 2 + rng.normal(0, 0.42), hgt, 6, 0.07)
        bend = rng.uniform(-1, 1) * 0.12
        spread.append([(px + bend * (H - py) ** 1.3 / 30, py) for px, py in pts])
        widths.append(rng.uniform(2.2, 3.6))
        vals.append(rng.uniform(0.3, 1.0))
    # Tapered blades: draw each at full width for the lower half and thinner above.
    lower = [pl[:4] for pl in spread]
    upper = [pl[3:] for pl in spread]
    val = np.maximum(draw.strokes((H, W), lower, widths, vals, wrap=True),
                     draw.strokes((H, W), upper, [w * 0.6 for w in widths], vals, wrap=True))
    alpha = np.maximum(draw.strokes((H, W), lower, widths, None, wrap=True),
                       draw.strokes((H, W), upper, [max(1.2, w * 0.6) for w in widths], None, wrap=True))
    ys = np.arange(H, dtype=np.float32)[:, None] / H
    t = saturate(val * 0.6 + 0.5 * (1 - ys) * 0.6)
    d = core.apply_ramp(t, ["#141208", "#2A2616", "#403A24", "#564C32", "#6A5E42"])
    d = d * (0.5 + 0.5 * (1 - ys ** 3 * 0.9))[..., None]
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.15, np.float32)
    return card_out(ctx, d, alpha, s, g, "dead grass blades (strokes)", tilesU=True)


@texture("brass_weed_card", "foliage", tile=None)
def brass_weed_card(ctx):
    """Corrupted weed stalks (256x128): stiff segmented stalks the colour of tarnished bronze,
    swollen nodes and split seed-pods. The wrongness is subtle - it reads as weeds first,
    then as metal (GDD §26 pillar 5: contamination, not magic)."""
    H, W = 256, 512
    rng = ctx.rng("weed")
    lines, widths, vals = [], [], []
    nodes = []
    for c in range(14):
        x = rng.uniform(0, W)
        hgt = rng.uniform(90, 240)
        pts = draw.wander_line(rng, x, H - 2, -np.pi / 2 + rng.normal(0, 0.2), hgt, 5, 0.12)
        lines.append(pts)
        widths.append(rng.uniform(3, 5))
        vals.append(0.6)
        for px, py in pts[1:-1]:
            if rng.random() < 0.6:
                nodes.append((px, py, rng.uniform(4, 7), rng.uniform(3, 5), 1.0))
            # Short stiff side spurs.
            if rng.random() < 0.5:
                a = -np.pi / 2 + rng.choice([-1, 1]) * rng.uniform(0.5, 1.0)
                L = rng.uniform(15, 35)
                lines.append([(px, py), (px + np.cos(a) * L, py + np.sin(a) * L)])
                widths.append(2.5)
                vals.append(0.5)
        tx, ty = pts[-1]
        nodes.append((tx, ty, rng.uniform(5, 8), rng.uniform(7, 11), 1.0))
    stalk = draw.strokes((H, W), lines, widths, vals, wrap=True)
    pods = draw.ellipses((H, W), nodes, wrap=True)
    alpha = saturate(stalk * 2 + pods)
    t = saturate(0.35 + 0.4 * pods + 0.2 * noise.white(ctx.rng("n"), (H, W)))
    d = core.apply_ramp(t, ["#120D08", "#261D12", "#3C301D", "#4E4028", "#5E4E30"])
    ys = np.arange(H, dtype=np.float32)[:, None] / H
    d = d * (0.45 + 0.55 * (1 - ys ** 2))[..., None]
    # Pods split: a dark wet slit and the faint bile tint inside.
    slit = pods * smoothstep(0.6, 0.9, noise.fbm01(ctx.rng("slit"), (H, W), 2))
    d = lerp(d, hexc("#2A2C14"), slit * 0.6)
    # A metal sheen: the one foliage with a specular.
    s = 0.25 * alpha + 0.2 * pods
    g = np.full((H, W), 0.5, np.float32)
    d = lerp(d, hexc("#3A4450"), rim(alpha, 1.5) * 0.25)
    return card_out(ctx, d, alpha, s, g, "segmented stalks and pods (strokes)", tilesU=True)


@texture("pine_bark", "wood", tile=1.0)
def pine_bark(ctx):
    """Pine bark (tiling): long vertical plates split by deep dark furrows, flaking at the plate
    edges, soot and lichen."""
    W = ctx.W
    f1, f2, idx = noise.voronoi(ctx.rng("plates"), (W, W), 70, aspect=(1.0, 0.28))
    edge = f2 - f1
    cv = noise.cell_values(ctx.rng("cv"), idx)
    furrow = smoothstep(9, 0, edge)
    rock = ctx.load("Rock064", "displacement", crop=(0, 0, 512, 512))
    detail = noise.fbm(ctx.rng("d"), (W, W), 2, octaves=3, stretch=(0.6, 2.5))
    height = (1 - furrow) * (1.5 + 0.5 * cv) + 0.3 * noise.standardize(rock) + 0.3 * detail
    t = saturate(0.25 + 0.3 * cv + 0.15 * detail - 0.4 * furrow)
    d = core.apply_ramp(t, ["#0A0807", "#1A1410", "#2C221B", "#3C3026", "#4E4034"])
    d = d * (1 - 0.8 * furrow)[..., None]
    flake = smoothstep(3, 9, edge) * smoothstep(14, 9, edge) * smoothstep(0.5, 0.7, noise.fbm01(ctx.rng("f"), (W, W), 6))
    d = lerp(d, hexc("#4A3A2C"), flake * 0.4)
    lich = smoothstep(0.72, 0.85, noise.fbm01(ctx.rng("l"), (W, W), 10)) * (1 - furrow)
    d = lerp(d, hexc("#3E4234"), lich * 0.5)
    s = np.full((W, W), 0.05, np.float32)
    g = np.full((W, W), 0.15, np.float32)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=50, coverage=0.4)
    d = C.light(d, height, strength=2.5, amount=0.7)
    return ctx.out(d, s, g, procedural="bark plates (stretched voronoi), furrows, flaking, lichen")
