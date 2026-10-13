"""The blighted trees' hangings (ARCHITECTURE §8 note 696, tools/models/recipes/corrupted_trees.py): what hangs off the
Corruption's trees and stretches between their limbs. Two cards that hang from their root (kept at v 1, where the wind
bends a card from, as it does the boughs) and one membrane that tiles, holed, between the branches. Imported last,
so the library's other layers keep their places."""
from __future__ import annotations

import numpy as np

from . import common as C
from . import core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .mat_foliage import card_out, rim, self_shadow
from .reg import texture


def hung(*maps):
    """Painted hanging from the top edge, stored hanging from the bottom: a card bends in the wind from v 1 (scene.vert),
    which is the image's bottom row, so a hanging card's root is kept there and its model's UVs turn it the right way up."""
    return [np.ascontiguousarray(m[::-1]) for m in maps]


@texture("wrack_card", "foliage", tile=None)
def wrack_card(ctx):
    """Hangings for a tree the tide's been at, and an old one's beard (256x512, hanging from the bottom edge: hung()). The left half:
    knotted wrack, olive-brown ribbons a finger wide, forking, swollen with air bladders along them, lank and wet. The right
    half: beard lichen, hundreds of hair-fine grey-green threads, tangled, longest in the middle. A tree's model takes one
    half or the other (u 0-0.5, 0.5-1)."""
    H, W = 1024, 512
    rng = ctx.rng("wrack")
    lines, widths, vals = [], [], []
    bladders = []
    half = W // 2
    # The wrack: ribbons from the top edge, down and a little apart, forking twice; bladders where they swell.
    for k in range(9):
        x = 14 + (k + rng.uniform(0.15, 0.85)) / 9 * (half - 28)
        length = rng.uniform(0.55, 0.95) * (H - 20)

        def ribbon(x, y, a, length, w, depth):
            pts = [(x, y)]
            seg = length / 9
            for _ in range(9):
                a += rng.normal(0, 0.09) + 0.12 * (np.pi / 2 - a)
                x = float(np.clip(x + np.cos(a) * seg, 10, half - 10))
                y = min(y + np.sin(a) * seg, H - 6)
                pts.append((x, y))
            lines.append(pts)
            widths.append(w)
            vals.append(rng.uniform(0.35, 0.6))
            for px, py in pts[2:-1]:
                if rng.random() < 0.45:
                    bladders.append((px + rng.normal(0, 1.5), py, w * rng.uniform(0.9, 1.3), w * rng.uniform(1.6, 2.4), 1.0))
            if depth > 0:
                for _ in range(rng.integers(1, 3)):
                    j = int(rng.integers(3, len(pts) - 2))
                    px, py = pts[j]
                    ribbon(px, py, np.pi / 2 + rng.choice([-1, 1]) * rng.uniform(0.25, 0.5), (H - py) * rng.uniform(0.4, 0.8),
                           w * 0.75, depth - 1)
        ribbon(x, 0, np.pi / 2 + rng.normal(0, 0.15), length, rng.uniform(9, 13), 2)
    ribbons = draw.strokes((H, W), lines, widths, vals, wrap=False, supersample=2)
    blad = draw.ellipses((H, W), bladders, wrap=False)
    # The lichen: fine threads, many, from tufts along the top, tangling as they fall.
    threads = []
    # From four tufts where it's caught on the limb, fanning as it falls, so a card hangs in locks, not as a curtain.
    tufts = [half + (k + 0.5) / 4 * half + rng.normal(0, 6) for k in range(4)]
    for k in range(240):
        x0 = tufts[k % 4] + rng.normal(0, 7)
        L = rng.uniform(0.3, 0.95) * H * (1.0 if k % 4 in (1, 2) else 0.75)
        a = np.pi / 2 + rng.normal(0, 0.16)
        pts = draw.wander_line(rng, x0, 0, a, L, 14, 0.14)
        threads.append([(float(np.clip(px, half + 4, W - 4)), min(py, H - 4)) for px, py in pts])
    thr = draw.strokes((H, W), threads, [rng.uniform(1.4, 2.4) for _ in threads], None, wrap=False, supersample=2)
    alpha = saturate(np.maximum(np.maximum(ribbons * 2.4, blad), thr * 1.8))
    ys = np.arange(H, dtype=np.float32)[:, None] / H
    xs = np.arange(W)[None, :]
    left = (xs < half).astype(np.float32)
    n = noise.white(ctx.rng("n"), (H, W)) - 0.5
    # Wrack olive-brown, the bladders a shade paler and glossy; the lichen grey-green, paler at the tips.
    t_w = saturate(0.35 + 0.5 * ribbons + 0.25 * blad + 0.1 * n)
    d_w = core.apply_ramp(t_w, ["#0E0B05", "#1F1A0C", "#3A3014", "#4E421E", "#62542A"])
    t_l = saturate(0.35 + 0.4 * ys + 0.2 * n)
    d_l = core.apply_ramp(t_l, ["#1A1D16", "#323A2C", "#4E5A46", "#66725C", "#7E8A72"])
    d = d_w * left[..., None] + d_l * (1 - left)[..., None]
    d = d * self_shadow(alpha, 6, 0.35)[..., None]
    d = d * (0.6 + 0.4 * (1 - (1 - ys) ** 6))[..., None]  # darker in under the limb it hangs from
    d = lerp(d, hexc("#34404A"), rim(alpha, 1.5) * 0.25)
    s = (0.05 + 0.35 * blad * left + 0.15 * ribbons * left).astype(np.float32)
    g = (0.15 + 0.6 * left * alpha).astype(np.float32)
    d, alpha, s, g = hung(d, alpha, s, g)
    return card_out(ctx, d, alpha, s, g, "wrack ribbons with bladders; beard lichen threads (strokes)")


@texture("leaf_card", "foliage", tile=None)
def leaf_card(ctx):
    """Dead leaves that never fell (256x512, hanging from the bottom edge: hung()): a twig hung down off a limb with a dozen leaves on
    it, withered, curled and gone the brown of an old paper bag, each on its own stalk, turning a different way; a few
    torn to the ribs."""
    H, W = 1024, 512
    rng = ctx.rng("leaf")
    # Two twigs off the limb, the longer one forking.
    twigs = [draw.wander_line(rng, W * 0.42, 0, np.pi / 2 + 0.12, H * 0.8, 10, 0.1),
             draw.wander_line(rng, W * 0.6, 0, np.pi / 2 - 0.3, H * 0.5, 7, 0.12)]
    fx, fy = twigs[0][4]
    twigs.append(draw.wander_line(rng, fx, fy, np.pi / 2 + 0.55, H * 0.35, 6, 0.12))
    lines, widths, vals = list(twigs), [8.0, 6.0, 5.0], [0.15, 0.15, 0.15]
    leaves = []
    for twig in twigs:
      for j in range(1, len(twig)):
        (xa, ya), (xb, yb) = twig[j - 1], twig[j]
        for q in (0.15, 0.5, 0.85):
            px, py = xa + (xb - xa) * q, ya + (yb - ya) * q
            if rng.random() < 0.25:
                continue
            side = rng.choice([-1, 1])
            a = np.pi / 2 + side * rng.uniform(0.2, 0.9)
            stalk = rng.uniform(18, 36)
            sx, sy = px + np.cos(a) * stalk, py + np.sin(a) * stalk
            lines.append([(px, py), (sx, sy)])
            widths.append(3.0)
            vals.append(0.2)
            # The leaf: an ellipse hung from its stalk's end, curled (narrowed) and turned.
            L = rng.uniform(120, 190) * (1.1 - 0.3 * j / len(twig))
            curl = rng.uniform(0.32, 0.6)
            cx, cy = sx + np.cos(a) * L / 2, sy + np.sin(a) * L / 2
            if not (L / 2 < cx < W - L / 2 and cy < H - L / 2):
                continue
            leaves.append((cx, cy, L, curl, a, rng.uniform(0.4, 0.9), rng.random() < 0.1))
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    leaf = np.zeros((H, W), np.float32)
    shade = np.zeros((H, W), np.float32)
    holed = noise.fbm01(ctx.rng("hole"), (H, W), 9, octaves=2) > 0.78
    for cx, cy, L, curl, a, v, torn in leaves:
        ca, sa = np.cos(a), np.sin(a)
        u = (xs - cx) * ca + (ys - cy) * sa
        w = -(xs - cx) * sa + (ys - cy) * ca
        r = (u / (L / 2)) ** 2 + (w / (L * curl / 2)) ** 2
        inside = r < 1
        if torn:
            # Torn to the ribs: only the midrib and a few veins left of it.
            mid = np.abs(w) < 2.5
            veins = (np.abs(np.sin(u * 0.12)) < 0.12) & (np.abs(w) < L * curl * 0.4)
            inside = inside & (mid | veins)
        else:
            # A ragged edge and a hole or two.
            inside = inside & ~(holed & (r > 0.5))
        leaf = np.maximum(leaf, inside.astype(np.float32))
        # Curled: darker toward the edge it curls under, the midrib a dark line.
        k = inside * (v + 0.35 * (w / (L * curl / 2 + 1e-3)) - 0.35 * (np.abs(w) < 2))
        shade = np.where(inside, k, shade)
    stem = draw.strokes((H, W), lines, widths, None, wrap=False, supersample=2)
    alpha = saturate(np.maximum(leaf, stem * 1.5))
    n = noise.fbm01(ctx.rng("blot"), (H, W), 14, octaves=3)
    t = saturate(0.2 + 0.55 * shade + 0.25 * (n - 0.5))
    d = core.apply_ramp(t, ["#140C06", "#2E1D0E", "#4A3018", "#664424", "#7C5A34"])
    d = lerp(d, hexc("#1A120A"), (stem > 0.3) * (leaf < 0.5) * 0.8)
    d = d * self_shadow(alpha, 5, 0.3)[..., None]
    d = lerp(d, hexc("#34404A"), rim(alpha, 1.5) * 0.25)
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.18, np.float32)
    d, alpha, s, g = hung(d, alpha, s, g)
    return card_out(ctx, d, alpha, s, g, "withered leaves on stalks along a hanging twig (ellipses, strokes)")


@texture("membrane_web", "flesh", tile=1.0)
def membrane_web(ctx):
    """The skin the Corruption grows between a tree's limbs (tiling, a metre; alpha-tested): a thin membrane, darker where
    it's thick along its veins and paler where it's stretched thin between them, holed through (a quarter of it open) and
    ragged round the holes, wet. Painted the pale of old tallow so a tree's tint gives it its colour: pink-red on the
    birch, near-black red on the thorn, grey-white on the lantern tree's webs."""
    W = ctx.W
    from .mat_corruption import veins
    v = noise.blur(veins(ctx, W, 9, key="mveins", length=(80, 260), width=(1.5, 3.5)), 1.5)
    thin = noise.fbm01(ctx.rng("thin"), (W, W), 40, octaves=4)
    # The holes: where the membrane's thinnest, and never through a vein; ragged round the edge.
    holes = noise.fbm01(ctx.rng("holes"), (W, W), 30, octaves=3) + 0.18 * (noise.fbm01(ctx.rng("rag"), (W, W), 6, octaves=2) - 0.5)
    cut = np.percentile(holes, 74)
    alpha = ((holes < cut) | (v > 0.35)).astype(np.float32)
    edge = smoothstep(cut - 0.04, cut, holes) * (v < 0.35)
    height = v * 2.0 - thin * 0.6 + edge * 0.8
    t = saturate(0.55 + 0.35 * (thin - 0.5) - 0.45 * v - 0.25 * edge)
    d = core.apply_ramp(t, ["#221C1A", "#423834", "#6E625A", "#958880", "#B8ACA2"])
    d = lerp(d, hexc("#3A2C2A"), v * 0.5)
    s = (0.25 + 0.3 * thin).astype(np.float32)
    g = np.full((W, W), 0.7, np.float32)
    d = C.light(d, height, strength=2, amount=0.5)
    return ctx.out(d, s, g, alpha=alpha, alpha_test=True, procedural="veins (strokes), thin fields and holes (fbm)")


@texture("quartz", "flesh", tile=0.5)
def quartz(ctx):
    """White quartz, milky and part clear (tiling, half a metre): facets packed at angles, each lit its own way, cloudy
    white in the body, a cold grey-blue depth where it's clear, hairline fractures; a hard glint on the lit faces. The
    crystal tree's points and clumps (note 696): the one bright thing on a bone-pale tree."""
    W = ctx.W
    from .mat_ground import faceted_stones
    height, cv, body, idx = faceted_stones(ctx, 140, key="quartz", gap=1.0, tilt=2.6, aspect=(0.45, 1.0))
    cloud = noise.fbm01(ctx.rng("cloud"), (W, W), 30, octaves=4)
    t = saturate(0.55 + 0.3 * (cloud - 0.5) + 0.25 * (cv - 0.5))
    d = core.apply_ramp(t, ["#5E6670", "#8A929A", "#B4B8BA", "#D2D2CE", "#E6E4DE"])
    # Clear in places: the grey-blue depth showing through the milk.
    clear = smoothstep(0.55, 0.8, noise.fbm01(ctx.rng("clear"), (W, W), 18, octaves=3))
    d = lerp(d, hexc("#4A5866"), clear * 0.5)
    d = d * (0.35 + 0.65 * body)[..., None]
    lit = noise.bake_light(height, 0.8, 1.0)
    sharp = smoothstep(1.0, 1.5, lit) * body
    s = (0.3 + 0.6 * sharp).astype(np.float32)
    g = (0.7 + 0.25 * sharp).astype(np.float32)
    d = C.light(d, height, strength=0.8, amount=0.9)
    return ctx.out(d, s, g, procedural="quartz facets (voronoi), cloud and clear (fbm)")
