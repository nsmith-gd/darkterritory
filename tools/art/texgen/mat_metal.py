"""Metals (GDD §27 material families): iron/steel dark, oily, cold with a narrow hard highlight;
brass/copper muted warm and tarnished, never polished; painted metal chipped, faded, practical.

Layout scale: WORK is 512 px per tile, so on a 1 m tile one work pixel is ~2 mm and a final
texel ~4 mm. Rivets are drawn big (~2 cm heads, 4-5 final texels) so they survive at distance
(§26: "if something only reads because of tiny texture detail, it is wrong").
"""
from __future__ import annotations

import numpy as np

from . import common as C
from . import convert, core, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


# --- shared -------------------------------------------------------------------------------

def metal_base(ctx, ramp, repeat=(1, 1), crop=None, gloss_lo=0.35, gloss_hi=0.75, **conv):
    """Metal063 (ambientCG photoscan steel) pushed through the legacy conversion onto a ramp.
    Returns diffuse, spec, gloss and the photo's displacement for further use."""
    alb = ctx.load("Metal063", "diffuse", repeat=repeat, crop=crop)
    rough = ctx.load("Metal063", "roughness", repeat=repeat, crop=crop)
    disp = ctx.load("Metal063", "displacement", repeat=repeat, crop=crop)
    # The scan is near-mirror (roughness ~0.13); keep its breakup but re-range it so the
    # §27 "harsh specular on metal" stays narrow without going chrome.
    r = 1 - (gloss_lo + (gloss_hi - gloss_lo) * noise.normalize(1 - rough))
    d, s, g = convert.pbr_to_legacy(alb, height=disp, rough=r, metal=1.0, ramp=ramp, **conv)
    return d, s, g, disp


def rivet_field(W, H, positions, r):
    """Height of domed rivet heads at a list of (x, y) centres, wrapping."""
    xs, ys = noise.grid(H, W)
    h = np.zeros((H, W), np.float32)
    for cx, cy in positions:
        dx, dy = noise.wrapdist(xs, ys, cx, cy, W, H)
        h = np.maximum(h, noise.dome(np.sqrt(dx * dx + dy * dy), r))
    return h


def rivet_row(W, y, n, offset=0.5):
    return [((i + offset) * W / n, y) for i in range(n)]


def rivet_col(H, x, n, offset=0.5):
    return [(x, (i + offset) * H / n) for i in range(n)]


def rivet_base_mask(W, H, positions, r):
    """A seed just below each rivet: where oil and rust weep out and run down."""
    xs, ys = noise.grid(H, W)
    m = np.zeros((H, W), np.float32)
    for cx, cy in positions:
        dx, dy = noise.wrapdist(xs, ys, cx, cy + r * 0.8, W, H)
        m = np.maximum(m, saturate(1 - np.sqrt((dx / r) ** 2 + (dy / (r * 0.5)) ** 2)))
    return m


def band(coord, centre, half, soft=1.5, period=None):
    """1 inside |coord - centre| < half (wrapping if period), soft edges."""
    d = np.abs(coord - centre)
    if period:
        d = np.minimum(d, period - d)
    return smoothstep(half + soft, half - soft, d)


def heat_tint(ctx, d, amount=0.25):
    """Heat discolouration: broad horizontal-ish bands of blued and browned steel."""
    n = noise.fbm(ctx.rng("heat"), d.shape[:2], 40, octaves=3, stretch=(3.0, 0.5))
    blue = smoothstep(0.4, 1.6, n)
    brown = smoothstep(0.4, 1.6, -n)
    d = C.tint(d, blue, "#3A4452", amount)
    d = C.tint(d, brown, "#5A4030", amount)
    return d


# --- iron and steel -------------------------------------------------------------------------

@texture("iron_plate", "iron", tile=1.0)
def iron_plate(ctx):
    """Riveted boiler/armour plate: lap seams every 0.5 m with a rivet row, a butt strap at u=0."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, disp = metal_base(ctx, "iron", crop=(0, 0, 768, 768))
    # Plate geometry: lower plates tuck under the upper ones' bottom edges (lap joints).
    v = ys % (W / 2)
    lap = smoothstep(W / 2 - 10, W / 2 - 2, v)            # the upper plate's rolled bottom edge
    height = -0.8 * lap
    strap = band(xs, 0, 30, 2.5, period=W)               # butt strap over the vertical joint
    height = height + 0.7 * strap
    rivets = rivet_row(W, W / 2 - 24, 12) + rivet_row(W, W - 24, 12, 0.0)
    rivets += rivet_col(W, -14 % W, 12) + rivet_col(W, 14, 12, 0.0)
    rh = rivet_field(W, W, rivets, 9.5)
    height = height + 1.3 * rh + 0.08 * noise.standardize(disp)
    # Weathering.
    d = heat_tint(ctx, d, 0.3)
    rust, rn = C.rust_layer(ctx, (W, W))
    seamcav = saturate(lap * 0.8 + band(xs, 30, 2, 2, W) + band(xs, W - 30, 2, 2, W))
    rust_m = saturate(smoothstep(0.55, 0.85, noise.fbm01(ctx.rng("rustm"), (W, W), 10) * 0.6 + seamcav * 0.5 + rh * 0.2))
    d, s, g = C.reveal(d, s, g, rust_m * 0.8, rust, 0.05, 0.1)
    # The lap's top ledge (just below each seam) collects soot, wet and rust.
    ledge = smoothstep(26, 2, v) * (0.6 + 0.4 * noise.fbm01(ctx.rng("ledge"), (W, W), 8))
    d = lerp(d, d * 0.35, ledge * 0.8)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.5, scale=45, coverage=0.5)
    # Rain-wash: long vertical grime from every seam, the plate's dominant weathering read.
    d, s, g, _ = C.soot(ctx.rng("wash"), d, s, g, amount=0.45, scale=14, coverage=0.4, stretch=(0.35, 4.0))
    seed = rivet_base_mask(W, W, rivets, 12) + 0.6 * seamcav
    d, s, g, _ = C.streak(ctx.rng("oil"), d, s, g, seed, amount=0.95, colour="#0A0A0B", length=0.99,
                          gloss_add=0.25, spec_add=0.1)
    d, s, g, _ = C.streak(ctx.rng("rstreak"), d, s, g, rust_m * 0.9, amount=0.45, colour="#4A2A1C", length=0.975,
                          gloss_add=-0.2)
    # Rubbed-bright rivet heads and strap edges: the only light metal on the plate.
    wear = saturate(noise.convexity_from_height(height, 3) * 1.4) * (1 - rust_m)
    d, s, g = C.reveal(d, s, g, wear * 0.6, "#5A5E64", 0.55, 0.75)
    d = C.light(d, height, strength=5, amount=0.75)
    d, _ = C.occlude(d, height, 6, 0.45)
    return ctx.out(d, s, g, procedural="plate seams, rivets, heat tint, soot, oil and rust streaks")


@texture("iron_smokebox", "iron", tile=1.0)
def iron_smokebox(ctx):
    """Smokebox: soot-black, heat-scaled, flaking in plates of scale to rusty brown beneath."""
    W = ctx.W
    d, s, g, disp = metal_base(ctx, "soot", crop=(256, 256, 768, 768), gloss_lo=0.15, gloss_hi=0.4, metal_albedo=0.9)
    asph = ctx.load("Asphalt033", "diffuse")
    d = d * (0.75 + 0.5 * noise.normalize(core.lum(asph)))[..., None]
    f1, f2, idx = noise.voronoi(ctx.rng("scale"), (W, W), 380, aspect=(1.0, 1.3))
    # Cracks only in patches: scale is crazed where the heat was worst, smooth crust elsewhere.
    crazed = smoothstep(0.2, 0.5, noise.fbm01(ctx.rng("craze"), (W, W), 40, octaves=3))
    edge = saturate(1 - (f2 - f1) / 2.0) * crazed          # cracks between scale flakes
    cv = noise.cell_values(ctx.rng("cells"), idx)
    flake = (cv > 0.9).astype(np.float32) * crazed         # flakes that have fallen away
    ashy = ((cv > 0.6) & (cv <= 0.9)).astype(np.float32) * crazed * 0.6  # heat-whitened scale
    lift = noise.fbm01(ctx.rng("lift"), (W, W), 20, octaves=3)
    height = (1 - flake) * (0.6 + 0.2 * cv * crazed) - edge * 0.7 + 0.3 * lift
    under, _ = C.rust_layer(ctx, (W, W))
    d, s, g = C.reveal(d, s, g, flake * 0.9, under * 0.55, 0.06, 0.12)
    d = C.tint(d, ashy, "#4A4846", 0.0)
    d = lerp(d, d * 1.6 + hexc("#2A2826") * 0.5, ashy * 0.7)
    d = d * (1 - 0.6 * edge)[..., None]
    d = heat_tint(ctx, d, 0.35)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.5, scale=50, coverage=0.55, colour="#0A0A0A")
    d = C.light(d, height, strength=4, amount=0.6)
    s = s * 0.6
    return ctx.out(d, s, g, procedural="heat-scale flakes (voronoi), ash bloom, soot fields")


def painted(ctx, ramp, chip_amount, seam_axis="x", pitch=2, primer="#3A302A", fade="#6A6A66", fade_amt=0.12,
            paint_gloss=0.3, rivet_n=12, streak_col="#3A2216"):
    """Painted sheet-steel siding: panel seams with rivets every 1/pitch tile, paint chipped at
    edges and in random patches down to rust, sometimes bare iron; rust bleeds down from chips."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    # Under the paint: the steel photo gives a surface to chip into.
    iron_d, iron_s, iron_g, disp = metal_base(ctx, "iron")
    # The paint coat itself: flat colour with a faint photo breakup and brush/roller mottling.
    mott = noise.fbm01(ctx.rng("mott"), (W, W), 30, octaves=6, gain=0.55)
    t = saturate(0.35 + 0.35 * mott + 0.2 * (noise.normalize(disp) - 0.5))
    paint = core.apply_ramp(t, ramp)
    coord = xs if seam_axis == "x" else ys
    seams = [i * W / pitch for i in range(pitch)]
    seam_h = np.zeros((W, W), np.float32)
    rivets = []
    for c in seams:
        seam_h = np.maximum(seam_h, band(coord, c, 3, 1.5, period=W))
        # Rivets either side of each seam.
        if seam_axis == "x":
            rivets += rivet_col(W, (c + 16) % W, rivet_n) + rivet_col(W, (c - 16) % W, rivet_n)
        else:
            rivets += rivet_row(W, (c + 16) % W, rivet_n) + rivet_row(W, (c - 16) % W, rivet_n)
    rh = rivet_field(W, W, rivets, 8.5)
    # Pressed stiffening rib halfway between seams (boxcar siding).
    rib_c = [(c + W / pitch / 2) % W for c in seams]
    rib = sum(np.exp(-((np.minimum(np.abs(coord - rc), W - np.abs(coord - rc))) / 14.0) ** 2) for rc in rib_c)
    height = -1.0 * seam_h + 1.2 * rh + 0.8 * rib + 0.05 * noise.standardize(disp)
    edges = saturate(noise.convexity_from_height(height, 3) * 1.6 + seam_h * 0.8)
    spec = np.full((W, W), 0.1, np.float32)
    gloss = np.full((W, W), paint_gloss, np.float32) * (0.7 + 0.6 * mott)
    d = paint
    # Faded (sun and weather chalk the paint) in broad patches.
    fadem = noise.fbm01(ctx.rng("fade"), (W, W), 80, octaves=3)
    d = lerp(d, lerp(d, hexc(fade) * 0.5 + d * 0.5, 0.5), smoothstep(0.5, 0.9, fadem) * fade_amt * 4)
    # Chips: primer ring, then rust, and at the worst spots bare iron.
    chip = convert.chips(ctx.rng("chips"), (W, W), edges, amount=chip_amount, scale=7, edge_weight=0.9)
    chip_wide = saturate(noise.blur(chip, 2.5) * 1.8)
    rust, _ = C.rust_layer(ctx, (W, W))
    d, spec, gloss = C.reveal(d, spec, gloss, chip_wide * 0.5, primer, 0.08, 0.2)
    d, spec, gloss = C.reveal(d, spec, gloss, chip, rust, 0.05, 0.12)
    bare = chip * smoothstep(0.6, 0.8, noise.fbm01(ctx.rng("bare"), (W, W), 6))
    d = lerp(d, iron_d * 0.6, bare)
    spec = lerp(spec, iron_s, bare)
    gloss = lerp(gloss, iron_g, bare)
    d, spec, gloss, _ = C.streak(ctx.rng("rs"), d, spec, gloss, chip * 0.8, amount=0.55, colour=streak_col,
                                 length=0.982, gloss_add=-0.1)
    seed = rivet_base_mask(W, W, rivets, 8.5) + seam_h * 0.3
    d, spec, gloss, _ = C.streak(ctx.rng("oil"), d, spec, gloss, seed, amount=0.5, colour="#16120F", length=0.99,
                                 gloss_add=0.1)
    d, spec, gloss, _ = C.soot(ctx.rng("soot"), d, spec, gloss, amount=0.45, scale=70, coverage=0.45)
    d = C.light(d, height, strength=5, amount=0.7)
    d, _ = C.occlude(d, height, 6, 0.35)
    return d, spec, gloss


@texture("paint_oxide", "paint", tile=1.0)
def paint_oxide(ctx):
    d, s, g = painted(ctx, "oxide_paint", 0.16, "x", pitch=2, fade="#8A6A5A", fade_amt=0.15)
    return ctx.out(d, s, g, procedural="panel seams, rivets, ribs, chipped oxide paint, rust bleed")


@texture("paint_olive", "paint", tile=1.0)
def paint_olive(ctx):
    d, s, g = painted(ctx, "olive_paint", 0.13, "y", pitch=2, fade="#6A6C5E", fade_amt=0.12, primer="#4A2E22")
    return ctx.out(d, s, g, procedural="panel seams, rivets, ribs, chipped olive paint, rust bleed")


@texture("paint_black", "paint", tile=1.0)
def paint_black(ctx):
    """Black loco sheet metal: less chipping, more rubbing - worn to grey steel at the edges."""
    W = ctx.W
    d, s, g = painted(ctx, "black_paint", 0.05, "x", pitch=1, fade="#3A3C40", fade_amt=0.1,
                      paint_gloss=0.45, primer="#2A2826", streak_col="#2A1A12")
    # Rubbed through where hands, boots and hoses touch: along the seam's raised strip and the
    # rivet heads, feathered out in a fine scratchy breakup, not in soft clouds.
    xs, _ = noise.grid(W, W)
    near_seam = band(xs, 0, 16, 6, period=W)
    rub = noise.fbm01(ctx.rng("rub"), (W, W), 6, octaves=4, stretch=(1.0, 2.5))
    broad = noise.fbm01(ctx.rng("broad"), (W, W), 40, octaves=2)
    worn = smoothstep(0.62, 0.8, rub * 0.55 + near_seam * 0.35 + broad * 0.3)
    grey = core.apply_ramp(saturate(0.5 + 0.3 * noise.fbm01(ctx.rng("g"), (W, W), 3)), "iron")
    d = lerp(d, grey * 0.35, worn * 0.5)
    s = lerp(s, 0.3, worn * 0.5)
    return ctx.out(d, s, g, procedural="black paint rubbed to grey steel at edges, rivets, soot")


@texture("corrugated_iron", "iron", tile=1.0)
def corrugated_iron(ctx):
    """Roof sheet: 8 corrugations per metre running down the slope (along v), shading baked;
    rust streaks down the valleys, a lap and nail row across the top."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, disp = metal_base(ctx, "iron_warm", gloss_lo=0.25, gloss_hi=0.6)
    phase = xs / W * 8 * 2 * np.pi
    height = np.sin(phase) * 4.0
    lap = smoothstep(4, 0, np.minimum(ys, W - ys))      # the sheet above overlaps here
    nails = rivet_row(W, 14, 8, offset=0.25)             # nailed through the crests
    rh = rivet_field(W, W, nails, 5.5)
    height = height + 2.0 * rh - 1.5 * lap
    valley = smoothstep(0.2, -0.9, np.sin(phase))
    rust, _ = C.rust_layer(ctx, (W, W))
    rn = noise.fbm01(ctx.rng("rm"), (W, W), 25, octaves=5)
    rust_m = smoothstep(0.45, 0.8, rn * 0.8 + valley * 0.25 + lap * 0.4)
    d, s, g = C.reveal(d, s, g, rust_m, rust, 0.05, 0.1)
    seed = saturate(rivet_base_mask(W, W, nails, 5.5) * 1.5 + lap * 0.5) + rust_m * 0.4 * valley
    d, s, g, _ = C.streak(ctx.rng("rs"), d, s, g, seed, amount=0.8, colour="#4A2616", length=0.993,
                          gloss_add=-0.15, wander=0.05, density=0.9)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.5, scale=60, coverage=0.45, stretch=(0.5, 2.0))
    # Corrugation shading: the lit flank vs. the shadowed flank, strongly (it's what reads).
    d = C.light(d, height, strength=1.2, amount=0.9)
    s = s * (0.6 + 0.4 * saturate(np.cos(phase) * 0.5 + 0.5))
    return ctx.out(d, s, g, procedural="corrugation profile, lap and nails, valley rust streaks")


@texture("steel_grate", "iron", tile=1.0)
def steel_grate(ctx):
    """Bar grating walkway: bearing bars along v every ~6 cm, cross rods every 25 cm; holes cut
    out (alphaTest). The bar tops are worn bright by boots, the sides rusted."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, disp = metal_base(ctx, "iron")
    pitch = W / 16
    bx = np.abs(((xs + pitch / 2) % pitch) - pitch / 2)
    bar = smoothstep(6.5, 5.0, bx)
    rpitch = W / 4
    ry = np.abs(((ys + rpitch / 2) % rpitch) - rpitch / 2)
    rod = smoothstep(5.5, 4.0, ry)
    solid = saturate(bar + rod)
    height = bar * (1 - (bx / 6.5) ** 2) * 2 + rod * (1 - (ry / 5.5) ** 2) * 2.4
    top = saturate(bar * smoothstep(3.5, 1.0, bx) + rod * smoothstep(2.5, 0.5, ry))
    rust, _ = C.rust_layer(ctx, (W, W))
    joint = bar * rod
    rust_m = saturate(smoothstep(0.5, 0.8, noise.fbm01(ctx.rng("r"), (W, W), 12)) * (1 - top) + joint * 0.6)
    d, s, g = C.reveal(d, s, g, rust_m, rust, 0.05, 0.1)
    worn = top * smoothstep(0.3, 0.7, noise.fbm01(ctx.rng("w"), (W, W), 30, stretch=(0.5, 1.5)))
    d, s, g = C.reveal(d, s, g, worn * 0.8, "#62666C", 0.6, 0.75)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.4, scale=60, coverage=0.45)
    d = C.light(d, height, strength=1.2, amount=0.8)
    return ctx.out(d, s, g, alpha=solid, alpha_test=True, procedural="bar grating, cutout holes, worn tops")


# --- brass, copper ----------------------------------------------------------------------------

@texture("brass", "brass", tile=0.5)
def brass(ctx):
    """Tarnished brass/bronze fittings: muted warm, dark tarnish fields, verdigris packed into
    the crevices, only the handled high spots rubbed toward brass colour. Never polished."""
    W = ctx.W
    d, s, g, disp = metal_base(ctx, "brass", repeat=(1, 1), crop=(128, 384, 640, 640), gloss_lo=0.3, gloss_hi=0.65,
                               metal_albedo=0.4, contrast=1.4, desat=0.9)
    # The steel scan's speckle reads as sand on a warm metal: soften it to cast-metal mottling.
    d = noise.blur(d, 3.0)
    # Cast/hammered surface: broad soft dents, plus the scans' fine structure.
    rock = ctx.load("Rock064", "displacement", crop=(0, 0, 512, 512))
    dents = noise.fbm(ctx.rng("dents"), (W, W), 28, octaves=3, gain=0.4)
    height = noise.standardize(disp) * 0.2 + dents + noise.standardize(noise.blur(rock, 2)) * 0.2
    cav = noise.cavity_from_height(height, 5)
    # Tarnish is the norm, not the exception: a dark brown-olive film everywhere, finely mottled.
    tarn = noise.fbm01(ctx.rng("tarnish"), (W, W), 40, octaves=3, gain=0.5)
    tm = 0.7 + 0.15 * smoothstep(0.3, 0.8, tarn)
    d = lerp(d, d * 0.35 + hexc("#1A160E") * 0.25, tm)
    s = s * (1 - 0.5 * tm)
    verd = smoothstep(0.35, 0.8, cav + 0.3 * noise.fbm01(ctx.rng("v"), (W, W), 8) - 0.15)
    d, s, g = C.reveal(d, s, g, verd * 0.75, "#3E4A40", 0.03, 0.08,
                       var=noise.fbm01(ctx.rng("vv"), (W, W), 3))
    # Only the handled high spots are rubbed back toward brass colour.
    # Broad rubbed crowns (painted-in highlights, the §27 "harsh specular" baked in a little),
    # plus the small convex details.
    crown = smoothstep(0.5, 0.95, noise.normalize(dents))
    hi = saturate(crown * 0.9 + noise.convexity_from_height(height, 6) * 0.6)
    rub = core.apply_ramp(saturate(0.5 + 0.4 * noise.fbm01(ctx.rng("rub"), (W, W), 3)), "brass") * 0.5
    d, s, g = C.reveal(d, s, g, hi * 0.75, rub, 0.7, 0.62)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.3, scale=50, coverage=0.4)
    d = C.light(d, height, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="tarnish fields, verdigris in cavities, rubbed high spots")


@texture("copper_pipe", "brass", tile=0.5)
def copper_pipe(ctx):
    """Copper pipe skin: u around the pipe, v along it. Drawn-tube streaks along v, dark brown
    tarnish, muted verdigris weeping from a sweated joint at v=0."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    # Metal063's scan for the surface; the colour is copper's own (the generated metal_copper set
    # is a soft cloud that reads as leather at this scale, so it isn't used).
    steel = ctx.load("Metal063", "diffuse", crop=(512, 512, 512, 512))
    rough = ctx.load("Metal063", "roughness", crop=(512, 512, 512, 512))
    alb = hexc("#B07050")[None, None, :] * (0.6 + 0.8 * noise.normalize(noise.blur(core.lum(steel), 1.0)))[..., None]
    drawn = noise.fbm(ctx.rng("drawn"), (W, W), 2, octaves=2, stretch=(0.5, 10))
    alb = alb * (1 + 0.05 * drawn)[..., None]
    d, s, g = convert.pbr_to_legacy(alb, rough=0.35 + 0.3 * noise.normalize(rough), metal=1.0, ramp="copper",
                                    metal_albedo=0.5, contrast=0.8, gloss_cap=0.7)
    # Tarnish: dark oxide everywhere except where hands and wipes keep it brown-bright; broken up
    # finely (fingerprints, wipes) so it doesn't read as clouds.
    tarn = noise.fbm01(ctx.rng("t"), (W, W), 10, octaves=6, gain=0.7)
    tm = smoothstep(0.3, 0.6, tarn)
    d = lerp(d, d * 0.45 + hexc("#1A120C") * 0.25, tm * 0.8)
    s = s * (1 - 0.5 * tm)
    # A soldered joint collar at v=0 (wraps): raised, with verdigris weeping down from it.
    collar = band(ys, 0, 18, 2, period=W)
    height = collar * 2.0 + 0.2 * drawn
    solder = band(ys, 21, 3, 1.5, period=W)
    d, s, g = C.reveal(d, s, g, solder * 0.9, "#4A4A46", 0.3, 0.4)
    verd_seed = solder * smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("vs"), (W, W), 6))
    d, s, g, _ = C.streak(ctx.rng("vd"), d, s, g, verd_seed, amount=0.6, colour="#3A4A40", length=0.975,
                          gloss_add=-0.3, spec_add=-0.1)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=50, coverage=0.4)
    d = C.light(d, height, strength=2, amount=0.6)
    return ctx.out(d, s, g, procedural="drawn streaks, tarnish, sweated joint, verdigris weep")


# --- running gear ----------------------------------------------------------------------------

@texture("rail_steel", "iron", tile=1.0)
def rail_steel(ctx):
    """Rail: u across the railhead and web, v along the line. A bright polished running strip
    down the middle where the wheels roll; rusty brown sides. Symmetric in u so it wraps."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, disp = metal_base(ctx, "iron", crop=(0, 256, 512, 512))
    u = xs / W
    centre = np.abs(u - 0.5)
    wob = noise.fbm(ctx.rng("wob"), (W, W), 30, octaves=2, stretch=(0.1, 6)) * 0.015
    strip = smoothstep(0.17, 0.13, centre + wob)
    rust, _ = C.rust_layer(ctx, (W, W))
    side = smoothstep(0.16, 0.3, centre + wob * 2)
    rust_m = side * (0.6 + 0.4 * smoothstep(0.3, 0.7, noise.fbm01(ctx.rng("r"), (W, W), 15, stretch=(1, 3))))
    d, s, g = C.reveal(d, s, g, rust_m, rust, 0.04, 0.08)
    scratches = noise.fbm01(ctx.rng("sc"), (W, W), 1.5, octaves=2, stretch=(0.6, 40))
    shine = core.apply_ramp(saturate(0.55 + 0.4 * scratches), ["#26282B", "#3A3D41", "#4E5256", "#62666B"])
    d, s, g = C.reveal(d, s, g, strip, shine, 0.85, 0.8)
    # Burn spots where a wheel slipped: dark blued patches on the strip.
    burn = strip * smoothstep(0.78, 0.92, noise.fbm01(ctx.rng("b"), (W, W), 8, stretch=(0.8, 2)))
    d = lerp(d, d * 0.4 + hexc("#1E2430") * 0.3, burn)
    # Rail edge: a thin highlight at the gauge corner, dark underside shadow toward the web.
    d = d * (1 - 0.5 * smoothstep(0.35, 0.5, centre))[..., None]
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.3, scale=50, coverage=0.4, stretch=(0.5, 2))
    return ctx.out(d, s, g, procedural="running strip, side rust, lengthwise scratches, wheel burns")


@texture("wheel_iron", "iron", tile=0.5)
def wheel_iron(ctx):
    """Cast iron wheel centres and rods: casting pits, black grease fields (glossy), grit."""
    W = ctx.W
    d, s, g, disp = metal_base(ctx, "iron", crop=(512, 0, 512, 512), gloss_lo=0.3, gloss_hi=0.6)
    asph = ctx.load("Asphalt033", "displacement")
    pits = noise.cavity_from_height(asph, 2)
    height = noise.standardize(asph) * 0.6 + noise.standardize(disp) * 0.2
    d = d * (1 - 0.55 * pits)[..., None]
    # Grease packs into the casting pits and smears outward in a fine, broken film.
    film = noise.fbm01(ctx.rng("gr"), (W, W), 8, octaves=6, gain=0.7)
    grease = saturate(smoothstep(0.55, 0.75, film) * 0.7 + pits * 0.8)
    d = lerp(d, hexc("#0C0C0C") + d * 0.25, grease * 0.85)
    g = lerp(g, 0.78, grease)
    s = lerp(s, 0.45, grease)
    rust, _ = C.rust_layer(ctx, (W, W))
    d, s, g = C.reveal(d, s, g, pits * (1 - grease) * 0.6, rust * 0.7, 0.05, 0.1)
    grit = smoothstep(0.8, 0.95, noise.white(ctx.rng("grit"), (W, W))) * grease
    d = lerp(d, hexc("#3A3530"), grit * 0.5)
    d, s, g, _ = C.streak(ctx.rng("drip"), d, s, g, grease * 0.4, amount=0.5, colour="#0A0A0A", length=0.985,
                          gloss_add=0.2)
    d = C.light(d, height, strength=4, amount=0.5)
    return ctx.out(d, s, g, procedural="casting pits, grease fields, grit")


@texture("rust_heavy", "iron", tile=1.0)
def rust_heavy(ctx):
    """Deep scaled rust: layered flakes lifting off, pitted dark craters, orange-brown bloom."""
    W = ctx.W
    alb = ctx.load("metal_rust", "diffuse")
    ground = ctx.load("Ground111", "diffuse", crop=(0, 0, 512, 512))
    ghd = ctx.load("Ground111", "displacement", crop=(0, 0, 512, 512))
    alb = alb * 0.4 + ground * 0.6
    f1, f2, idx = noise.voronoi(ctx.rng("flakes"), (W, W), 220, aspect=(1.0, 1.2))
    cv = noise.cell_values(ctx.rng("cv"), idx)
    crack = saturate(1 - (f2 - f1) / 2.5)
    height = cv * 0.8 - crack * 0.8 + noise.standardize(ghd) * 0.4
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.9, np.float32), metal=0.0,
                                    ramp="rust", contrast=1.3, cav_amt=0.5, dielectric_spec=0.06)
    d = d * (0.7 + 0.6 * cv)[..., None]
    pits = smoothstep(0.75, 0.95, noise.fbm01(ctx.rng("p"), (W, W), 5))
    d = lerp(d, hexc("#120C09"), pits * 0.8)
    d = d * (1 - 0.6 * crack)[..., None]
    d, s, g, _ = C.streak(ctx.rng("s"), d, s, g, pits * 0.5, amount=0.5, colour="#2A1810", length=0.98)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=60, coverage=0.4)
    d = C.light(d, height, strength=3, amount=0.6)
    return ctx.out(d, s, g, procedural="rust flakes (voronoi), pits, streaks")
