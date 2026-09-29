"""Glass and light sources (GDD §28: warm accents live ONLY in emissive - furnace orange,
lamp amber, ember red; "inside = warm, human, temporary safety"). For emissive textures the
diffuse holds the emitted colour and spec B the emissive mask."""
from __future__ import annotations

import numpy as np

from . import common as C
from . import core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


@texture("glass_dirty", "glass", tile=0.5)
def glass_dirty(ctx):
    """Dark grimy glass: near-black diffuse (you see through to the dark), a dust film settled
    along the bottom of each pane-width band, rain runs cleared through it, smears; spec high
    only where it's clean (§27: "dirty, dark, small reflective highlights")."""
    W = ctx.W
    # The grime film: Concrete034's scan gives it a real, uneven deposit; noise breaks it up.
    conc = ctx.load("Concrete034", "diffuse", shape=(W, W), repeat=(1, 2))
    film = noise.normalize(0.5 * noise.fbm01(ctx.rng("film"), (W, W), 25, octaves=6, gain=0.6) +
                           0.5 * noise.normalize(1 - core.lum(conc)))
    runs = C.convert.streaks(ctx.rng("runs"), smoothstep(0.93, 0.97, noise.white(ctx.rng("rs"), (W, W))) * 1.0,
                             length=0.995, density=0.7)
    dirt = saturate(smoothstep(0.35, 0.75, film) - runs * 0.8)
    smear = smoothstep(0.55, 0.8, noise.fbm01(ctx.rng("sm"), (W, W), 8, octaves=3, stretch=(2.5, 0.8)))
    t = saturate(0.2 + 0.2 * film)
    d = core.apply_ramp(t, "glass")
    d = lerp(d, hexc("#2E2C28") * (0.7 + 0.6 * noise.white(ctx.rng("dw"), (W, W)))[..., None], dirt * 0.8)
    d = lerp(d, d * 1.3, smear * 0.4)
    s = 0.75 * (1 - dirt) * (1 - 0.4 * smear) + 0.05
    g = 0.85 * (1 - 0.5 * dirt) * (1 - 0.3 * smear) + 0.05
    return ctx.out(d, s.astype(np.float32), g.astype(np.float32), procedural="dust film, rain runs, smears")


@texture("lamp_lens", "emissive", tile=None)
def lamp_lens(ctx):
    """A lit lamp lens (headlamp / marker): stepped Fresnel rings, amber, hottest at the centre,
    in a tarnished brass bezel. Emissive = 1 over the lens."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    c = W / 2
    r = np.sqrt((xs - c) ** 2 + (ys - c) ** 2) / (W * 0.42)
    lens = smoothstep(1.0, 0.985, r)
    # Fresnel steps: each ring bright at its inner edge fading outward (the prism facets).
    rings = 9
    ph = (r * rings) % 1.0
    ring = (1 - ph) ** 2 * 0.6 + 0.4
    hot = saturate(1 - r) ** 0.8
    t = saturate(hot * 0.85 + ring * 0.25 * (0.4 + 0.6 * (1 - r)))
    lamp = core.apply_ramp(t, ["#3A1A08", "#8A4A18", "#D08030", "#E0A050", "#F4D8A0"])
    # Soot on the lens: a dirty crescent at the top, dust specks.
    soot = smoothstep(0.4, 0.8, noise.fbm01(ctx.rng("soot"), (W, W), 20)) * smoothstep(0.1, -0.6, (ys - c) / c)
    lamp = lerp(lamp, lamp * 0.45, soot * 0.7)
    specks = smoothstep(0.97, 0.99, noise.white(ctx.rng("sp"), (W, W)))
    lamp = lamp * (1 - 0.6 * specks)[..., None]
    bez_r = smoothstep(0.99, 1.0, r) * smoothstep(1.22, 1.2, r)
    bez = core.apply_ramp(saturate(0.4 + 0.3 * noise.fbm01(ctx.rng("bz"), (W, W), 6)), "brass") * 0.4
    bez = bez * (1 + 0.8 * np.exp(-((r - 1.06) / 0.03) ** 2) - 0.5 * np.exp(-((r - 1.17) / 0.02) ** 2))[..., None]
    outside = hexc("#101010")
    d = lerp(lerp(outside, bez, bez_r), lamp, lens)
    e = lens * (0.55 + 0.45 * hot) * (1 - 0.5 * soot)
    s = lerp(0.5 * bez_r, 0.8, lens)
    g = lerp(0.55 * bez_r, 0.85, lens)
    return ctx.out(d, s.astype(np.float32), g.astype(np.float32), emissive=e, tiling=False,
                   procedural="Fresnel rings, bezel, soot crescent")


@texture("window_lit", "emissive", tile=None)
def window_lit(ctx):
    """A lit warm window: six panes in a dark wooden sash (muntins), warm lamp light inside,
    brighter low and to one side (the lamp is on a table), grime on the glass, one pane cracked.
    Emissive mask = the panes."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    frame = 34
    mun = 10
    cols, rows = 2, 3
    inner = (xs > frame) & (xs < W - frame) & (ys > frame) & (ys < W - frame)
    pw = (W - 2 * frame) / cols
    ph = (W - 2 * frame) / rows
    mx = np.abs(((xs - frame) % pw) - pw / 2) > pw / 2 - mun / 2
    my = np.abs(((ys - frame) % ph) - ph / 2) > ph / 2 - mun / 2
    pane = inner & ~mx & ~my
    pane = pane.astype(np.float32)
    lampx, lampy = W * 0.35, W * 0.75
    glow = saturate(1 - np.sqrt((xs - lampx) ** 2 + (ys - lampy) ** 2) / (W * 0.9))
    t = saturate(glow ** 1.2 * 0.9 + 0.1)
    light = core.apply_ramp(t, ["#2A1206", "#6A3410", "#B0621E", "#D88C38", "#E8B060"])
    # Shapes inside: a curtain edge and a dark silhouette of something on the sill.
    curtain = smoothstep(0.55, 0.6, (xs / W) + 0.03 * np.sin(ys * 0.08))
    light = lerp(light, light * 0.45, curtain * 0.8)
    sill = (ys > W * 0.78) & (np.abs(xs - W * 0.62) < 18) & (ys < W - frame)
    light = lerp(light, light * 0.15, sill * 1.0)
    grime = smoothstep(0.4, 0.8, noise.fbm01(ctx.rng("g"), (W, W), 18, octaves=5))
    light = lerp(light, light * 0.5, grime * 0.6)
    rng = ctx.rng("crack")
    crack = draw.strokes((W, W), [draw.wander_line(rng, W * 0.7, W * 0.2, a, 60, 6, 0.3) for a in (0.5, 2.2, 3.8, 5.0)],
                         [2, 2, 1.5, 1.5], wrap=False)
    light = lerp(light, hexc("#E8C890"), crack * pane * 0.6)
    wood = core.apply_ramp(saturate(0.3 + 0.35 * noise.fbm01(ctx.rng("w"), (W, W), 2, stretch=(1, 6))), "wood")
    edge = saturate(noise.blur(pane, 2.5) * (1 - pane) * 3)
    wood = wood * (1 - 0.6 * edge)[..., None]
    d = lerp(wood, light, pane)
    e = pane * (0.35 + 0.65 * t) * (1 - 0.4 * grime)
    s = lerp(0.05, 0.6, pane)
    g = lerp(0.2, 0.8, pane)
    return ctx.out(d, np.asarray(s, np.float32), np.asarray(g, np.float32), emissive=e, tiling=False,
                   procedural="sash and muntins, interior glow, curtain, grime, crack")


@texture("firebox", "emissive", tile=0.5)
def firebox(ctx):
    """The firebox coal bed: lumps with black-crusted tops over a glowing bed, white-yellow where
    the draught is hottest, orange, then dull ember red. Emissive = heat."""
    W = ctx.W
    from .mat_ground import faceted_stones
    height, cv, body, idx = faceted_stones(ctx, 220, key="coals", gap=2.5, tilt=1.0)
    heat_field = noise.fbm01(ctx.rng("heat"), (W, W), 40, octaves=4)
    gaps = 1 - body
    # Heat: gaps glow most, lump tops least; the whole bed hotter in broad patches.
    # Black crust only on some lumps' tops; the rest glow through their own cracks.
    crust = smoothstep(0.45, 0.95, noise.normalize(height)) * smoothstep(0.3, 0.6, cv) * \
        smoothstep(0.3, 0.6, noise.fbm01(ctx.rng("crust"), (W, W), 6, octaves=3))
    # Most of the bed is dull red under black crust; white-hot only where the draught pulls.
    hot = smoothstep(0.45, 0.85, heat_field)
    heat = saturate(gaps * (0.35 + 0.6 * hot) + (1 - crust) * (0.2 + 0.4 * hot) + 0.35 * hot - 0.15)
    d = core.apply_ramp(heat, ["#0A0605", "#3A0E06", "#A02810", "#D06020", "#F0A040", "#F8E0A0"])
    d = lerp(d, core.apply_ramp(saturate(cv * 0.5), "coal"), smoothstep(0.35, 0.1, heat) * 0.8)
    s = np.full((W, W), 0.1, np.float32)
    g = np.full((W, W), 0.3, np.float32)
    return ctx.out(d, s, g, emissive=smoothstep(0.1, 0.9, heat), procedural="coal lumps (voronoi), heat field",
                   grain=0.04)


@texture("ember_crack", "emissive", tile=0.5)
def ember_crack(ctx):
    """Black crust with glowing cracks (Cinder Hounds): dull cinder plates, cracks between them
    glowing orange, hotter and wider in a few places. Emissive only on the cracks."""
    W = ctx.W
    f1, f2, idx = noise.voronoi(ctx.rng("plates"), (W, W), 120)
    edge = f2 - f1
    wid = 1.5 + 3.5 * noise.fbm01(ctx.rng("w"), (W, W), 30)
    crack = smoothstep(wid, wid * 0.3, edge)
    fine = noise.fbm(ctx.rng("fine"), (W, W), 6, octaves=3)
    crack = saturate(crack + smoothstep(0.08, 0.0, np.abs(fine)) * smoothstep(0.6, 0.8, noise.fbm01(ctx.rng("fm"), (W, W), 20)) * 0.8)
    cv = noise.cell_values(ctx.rng("cv"), idx)
    asph = ctx.load("Asphalt033", "diffuse")
    crustv = saturate(0.2 + 0.3 * cv + 0.3 * (noise.normalize(core.lum(asph)) - 0.5))
    crust_d = core.apply_ramp(crustv, "coal")
    heat = crack * (0.6 + 0.4 * noise.fbm01(ctx.rng("h"), (W, W), 15))
    glow = core.apply_ramp(saturate(heat), ["#1A0604", "#6A1808", "#B03A10", "#D06020", "#F0A040"])
    halo = saturate(noise.blur(crack, 4) * 1.5) * (1 - crack)
    crust_d = lerp(crust_d, hexc("#3A1208"), halo * 0.5)
    d = lerp(crust_d, glow, crack)
    height = (1 - crack) * (1 + 0.5 * cv)
    d = lerp(d, C.light(d, height, strength=2, amount=0.6), 1 - crack)
    s = lerp(np.full((W, W), 0.15, np.float32), 0.0, crack)
    g = np.full((W, W), 0.35, np.float32)
    return ctx.out(d, s, g, emissive=heat, procedural="cinder plates (voronoi), glowing cracks")
