"""The Corruption (GDD §26 pillar 5, "contamination, not magic"): swollen flesh, mineralised
growths, fungal crust, pale sacs - coal soot, rot, oil and tissue mixing together. Accents
from §28 (bruised violet, dead ivory, blackened red, fungal beige, bile green) are used at low
saturation and small coverage: "Don't let corruption become neon soup. Less is more."
"""
from __future__ import annotations

import numpy as np

from . import common as C
from . import convert, core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def veins(ctx, W, n, key="veins", length=(60, 200), width=(1.5, 4.0), branches=3):
    """Branching veins as wrapping strokes, each branch thinner."""
    rng = ctx.rng(key)
    lines, widths = [], []

    def grow(x, y, a, L, w, depth):
        pts = draw.wander_line(rng, x, y, a, L, 10, 0.25)
        lines.append(pts)
        widths.append(w)
        if depth > 0:
            for _ in range(branches):
                px, py = pts[rng.integers(2, len(pts))]
                grow(px, py, a + rng.choice([-1, 1]) * rng.uniform(0.4, 1.2), L * 0.5, w * 0.6, depth - 1)
    for _ in range(n):
        x, y = rng.random(2) * W
        grow(x, y, rng.uniform(0, 2 * np.pi), rng.uniform(*length), rng.uniform(*width), 2)
    return draw.strokes((W, W), lines, widths)


@texture("flesh", "flesh", tile=0.5)
def flesh(ctx):
    """Waxy dead-ivory flesh, bruised violet in broad fields, blackened red in the folds, veins
    under the surface, damp (glossy) in patches. Marble012's veining gives it a real-surface
    irregularity no noise function has."""
    W = ctx.W
    marble = ctx.load("Marble012", "diffuse")
    mdisp = ctx.load("Marble012", "displacement")
    mv = noise.normalize(1 - core.lum(marble))
    folds_n = noise.fbm(ctx.rng("folds"), (W, W), 30, octaves=4)
    folds = smoothstep(0.25, 0.0, np.abs(folds_n)) * smoothstep(0.3, 0.6, noise.fbm01(ctx.rng("fm"), (W, W), 40))
    swell = noise.fbm(ctx.rng("swell"), (W, W), 45, octaves=3)
    height = swell * 2 - folds * 2.5 + noise.standardize(mdisp) * 0.2
    mv = noise.blur(mv, 2.0)
    t = saturate(0.6 + 0.08 * noise.fbm(ctx.rng("t"), (W, W), 20, octaves=3) + 0.12 * np.tanh(swell) - 0.12 * mv)
    d = core.apply_ramp(t, "flesh")
    bruise = smoothstep(0.5, 0.85, noise.fbm01(ctx.rng("bruise"), (W, W), 45, octaves=3))
    d = lerp(d, core.apply_ramp(saturate(0.25 + 0.3 * mv), ["#1A1016", "#3A2434", "#4A3446", "#5E4658"]), bruise * 0.75)
    v = veins(ctx, W, 7)
    v = noise.blur(v, 1.0)
    d = lerp(d, hexc("#3A2030"), v * 0.55 * (1 - bruise * 0.5))
    d = lerp(d, hexc("#2A0E0E"), folds * 0.9)
    # Waxy: a soft sheen on the swellings, as if lit through a skin of tallow.
    d = d * (1 + 0.15 * smoothstep(0.3, 1.5, swell))[..., None]
    d = lerp(d, d * 0.6 + hexc("#1A1412") * 0.3, smoothstep(0.6, 0.9, mv) * 0.3)
    damp = smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("damp"), (W, W), 20, octaves=4))
    s = 0.12 + 0.3 * damp
    g = 0.35 + 0.45 * damp
    d = C.light(d, height, strength=2, amount=0.5)
    d, _ = C.occlude(d, height, 8, 0.4)
    return ctx.out(d, s.astype(np.float32), g.astype(np.float32), procedural="folds, bruise fields, veins, damp")


@texture("fungal_crust", "flesh", tile=0.5)
def fungal_crust(ctx):
    """Fungal beige crust: clustered bumps and small shelf-lips, bile-green hints deep in the
    pits, dusted with soot. Dry: almost no specular."""
    W = ctx.W
    f1, f2, idx = noise.voronoi(ctx.rng("bumps"), (W, W), 700)
    size = np.sqrt(W * W / 700)
    bump = np.sqrt(saturate(1 - f1 / (size * 0.75)))
    cv = noise.cell_values(ctx.rng("cv"), idx)
    cluster = smoothstep(0.15, 0.5, noise.fbm01(ctx.rng("cl"), (W, W), 30, octaves=4))
    ground = ctx.load("Ground111", "displacement", crop=(256, 0, 512, 512))
    height = bump * (0.5 + cv) * cluster + noise.standardize(ground) * 0.3 + cluster * 1.5
    t = saturate(0.2 + 0.55 * bump * cluster + 0.2 * cv * cluster + 0.1 * noise.normalize(ground))
    d = core.apply_ramp(t, "fungal")
    pits = noise.cavity_from_height(height, 4)
    d = lerp(d, hexc("#4A5028") * 0.6, smoothstep(0.35, 0.8, pits) * 0.55)
    base = 1 - cluster
    d = lerp(d, core.apply_ramp(saturate(0.3 + 0.3 * noise.normalize(ground)), "mud") * 1.2, base * 0.7)
    s = np.full((W, W), 0.04, np.float32)
    g = np.full((W, W), 0.12, np.float32)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=30, coverage=0.4)
    d = C.light(d, height, strength=3, amount=0.7)
    return ctx.out(d, s, g, procedural="bump clusters (voronoi), pits, soot")


@texture("mineral_growth", "flesh", tile=0.5)
def mineral_growth(ctx):
    """Grey crystalline growth: angular facets packed in clusters, each facet's plane catching
    the light differently; soot packed between; the lit facets throw a sharp specular."""
    W = ctx.W
    from .mat_ground import faceted_stones
    height, cv, body, idx = faceted_stones(ctx, 360, key="crystals", gap=1.5, tilt=2.2, aspect=(1.0, 0.55))
    rock = ctx.load("Rock064", "diffuse", crop=(512, 0, 512, 512))
    detail = noise.normalize(core.lum(rock))
    t = saturate(0.3 + 0.45 * cv + 0.2 * (detail - 0.5))
    d = core.apply_ramp(t, "mineral")
    d = d * (0.05 + 0.95 * body)[..., None]
    lit = noise.bake_light(height, 0.8, 1.0)
    sharp = smoothstep(1.1, 1.6, lit) * body
    s = 0.1 + 0.6 * sharp
    g = 0.5 + 0.35 * sharp
    d = C.light(d, height, strength=0.8, amount=1.0)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.5, scale=30, coverage=0.5)
    # A faint bruised-violet cast in the crystal cores: the only hint it's not rock.
    d = C.tint(d, (cv > 0.8) * body, "#4A3446", 0.35)
    return ctx.out(d, s.astype(np.float32), g.astype(np.float32), procedural="crystal facets (voronoi), soot")


@texture("tar", "flesh", tile=0.5)
def tar(ctx):
    """Oily black tissue-tar: near-black, fibrous strands under the surface, bubbles and
    blisters, a blackened-red undertone; very glossy (the headlamp slides over it)."""
    W = ctx.W
    asph = ctx.load("Asphalt033", "displacement")
    fibres = noise.fbm01(ctx.rng("fib"), (W, W), 2, octaves=3, stretch=(0.3, 6))
    flow = noise.fbm(ctx.rng("flow"), (W, W), 40, octaves=4)
    blisters = draw.ellipses((W, W), [(*ctx.rng("b").random(2) * W, r, r * 0.9, 1.0)
                                      for r in ctx.rng("br").uniform(4, 14, 60)])
    blisters = noise.blur(blisters, 2)
    height = flow * 1.5 + blisters * 2 + fibres * 0.3 + noise.standardize(asph) * 0.1
    t = saturate(0.3 + 0.3 * fibres + 0.15 * flow + 0.3 * blisters)
    d = core.apply_ramp(t, "tar")
    d = C.tint(d, smoothstep(0.3, 1.2, flow), "#3A1414", 0.6)
    s = np.full((W, W), 0.55, np.float32) * (0.8 + 0.2 * fibres)
    g = np.full((W, W), 0.9, np.float32)
    d = C.light(d, height, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="fibres, flow, blisters", grain=0.04)


@texture("sac", "flesh", tile=0.5)
def sac(ctx):
    """Pale sac membrane: dead ivory with a faked translucency (brighter where the membrane is
    thin, over the middles of the swellings), branching dark veins, wet gloss."""
    W = ctx.W
    marble = ctx.load("Marble012", "diffuse", crop=(256, 256, 512, 512))
    mv = noise.normalize(1 - core.lum(marble))
    f1, f2, idx = noise.voronoi(ctx.rng("sacs"), (W, W), 11, jitter=1.0)
    size = np.sqrt(W * W / 11)
    # Swellings of uneven size: warp the distance so they bulge irregularly.
    f1 = f1 * (0.75 + 0.5 * noise.fbm01(ctx.rng("warp"), (W, W), 40, octaves=3))
    swell = saturate(1 - (f1 / (size * 0.7)) ** 2)
    rim = smoothstep(6, 0, f2 - f1)
    t = saturate(0.45 + 0.35 * swell - 0.2 * mv)
    d = core.apply_ramp(t, ["#1A1416", "#3A2E2E", "#5E5248", "#7A6E5E", "#948872"])
    thin = smoothstep(0.6, 1.0, swell)
    d = lerp(d, hexc("#A89A80"), thin * 0.3)
    # Bruising where the membrane is stretched, grime and soot caked in the creases between.
    bruise = smoothstep(0.5, 0.8, noise.fbm01(ctx.rng("bruise"), (W, W), 30, octaves=4)) * (0.5 + 0.5 * swell)
    d = lerp(d, hexc("#3A2A36"), bruise * 0.5)
    grime = smoothstep(0.4, 0.8, noise.fbm01(ctx.rng("grime"), (W, W), 10, octaves=5, gain=0.65)) * (1 - thin * 0.5)
    d = lerp(d, d * 0.4 + hexc("#141210") * 0.3, grime * 0.6)
    v = noise.blur(veins(ctx, W, 10, width=(1.5, 3.5)), 0.8)
    d = lerp(d, hexc("#4A2A34"), v * 0.6)
    d = lerp(d, hexc("#2A1418"), rim * 0.8)
    d = d * 0.8
    height = swell * 4 - rim * 2
    s = (0.25 + 0.3 * swell) * (1 - 0.6 * grime)
    g = (0.6 + 0.3 * swell) * (1 - 0.4 * grime)
    d = C.light(d, height, strength=1.5, amount=0.5)
    return ctx.out(d, s.astype(np.float32), g.astype(np.float32), procedural="swellings (voronoi), veins, translucency")
