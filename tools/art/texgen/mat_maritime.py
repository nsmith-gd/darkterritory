"""The Maritimes' own ground (the world is Nova Scotia and New Brunswick gone dark; the fidelity target is 2008-2012,
ARCHITECTURE §8 note 57): what the line's land is made of between the ballast and the fog.

- ground_heath: the granite barrens' heath, crowberry and blueberry turned rust-red in the fall, grey reindeer lichen,
  grit where the granite shows through the thin soil.
- ground_needles: the spruce and fir floor, a brown-orange duff of fallen needles with feather moss in clumps, cones.
- granite_lichen: the grey granite of the ledges and erratics, blotched with lichen (grey-green, black, a little
  orange), split along its joints.
- ground_red_clay: the red soil of the Fundy shore and the valley, rust-red clay, rutted and wet in the lows.
- bog_sphagnum: sphagnum and peat, sodden, with black pools that glint in the lamp.
- shore_shingle: the cobble of a river bank or a beach, rounded and wet, wrack caught between.

Each is built at 512 like the rest of the library, from the CC0 scans (mini_textures, tools/art/fetch_sources.sh)
and procedural passes, and gets its normal map from the heights it shades with (convert.normal_map).
"""
from __future__ import annotations

import numpy as np

from . import common as C
from . import convert, core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .mat_ground import dead_blades, faceted_stones
from .reg import texture

# The Maritime palette: rust-red heath, spruce duff, the grey of the granite, the red of the clay.
core.RAMPS.update({
    "heath": ["#110E09", "#201910", "#302416", "#40301D", "#524026"],
    "lichen": ["#262824", "#363A34", "#4A4E46", "#5E6458", "#72786A"],
    "duff": ["#100A07", "#20140C", "#332114", "#48301D", "#5C412A"],
    "feather_moss": ["#0E120A", "#182010", "#243018", "#324020", "#40502A"],
    "granite": ["#1E1F20", "#303234", "#46484A", "#5E6062", "#76787A"],
    "red_clay": ["#1A0E0A", "#301812", "#46241A", "#5C3224", "#704230"],
    "sphagnum": ["#141408", "#24220E", "#3A3416", "#50461E", "#62562A"],
    "peat": ["#070504", "#0E0A07", "#17110C", "#211811", "#2C2016"],
})


def detile(d, amount=0.85, sigma=None):
    """Take the tile's own large-scale light and dark out (the terrain's world-space macro tint, PlanArt.Macro, does that
    job without repeating): a blotch half a tile across reads as a quilt when the tile repeats over a hillside."""
    sigma = sigma or d.shape[0] / 24
    lum = d.mean(axis=2)
    low = noise.blur(lum, sigma)
    ratio = (lum.mean() + 1e-4) / (low + 1e-4)
    return saturate(d * lerp(np.ones_like(ratio), ratio, amount)[..., None])


@texture("ground_heath", "ground", tile=2.0)
def ground_heath(ctx):
    """Barrens heath: a low mat of crowberry and blueberry gone rust-red, grey reindeer lichen in round cushions, and
    bare granite grit in the gaps between (the soil there is inches deep over the rock)."""
    W = ctx.W
    moss = ctx.load("moss_ground", "diffuse")
    mdisp = ctx.load("moss_ground", "displacement")
    grav = ctx.load("Ground111", "diffuse", crop=(0, 512, 512, 512))
    gdisp = ctx.load("Ground111", "displacement", crop=(0, 512, 512, 512))
    # The mat: many tiny leaves (a fine fbm) in clumps (a coarser one), the lows bare.
    clumps = noise.fbm01(ctx.rng("clump"), (W, W), 22, octaves=5, gain=0.55)
    leaves = noise.fbm01(ctx.rng("leaf"), (W, W), 2.2, octaves=3)
    height = noise.standardize(mdisp) * 0.6 + (clumps - 0.5) * 3 + (leaves - 0.5) * 0.8
    # The mat is the moss scan's leafy structure alone (the gravel scan's round pebbles would read as polka dots).
    alb = moss
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.85, np.float32), ramp="heath",
                                    contrast=1.35, desat=0.75, dielectric_spec=0.04)
    # Grit where the mat thins: pale grey-pink granite gravel.
    bare = smoothstep(0.38, 0.28, clumps)
    grit, _, _ = convert.pbr_to_legacy(grav, height=gdisp, rough=np.full((W, W), 0.8, np.float32), ramp="granite", contrast=1.2)
    d = lerp(d, grit, bare)
    height = lerp(height, noise.standardize(gdisp) * 0.4 - 1.2, bare)
    # Reindeer lichen: pale grey-green cushions in ragged clusters (a thresholded noise, broken up finely at its edge),
    # sitting proud of the mat.
    lf = noise.fbm01(ctx.rng("lichen"), (W, W), 9, octaves=5, gain=0.6)
    ragged = noise.fbm01(ctx.rng("lr"), (W, W), 1.6, octaves=2)
    cushion = smoothstep(0.68, 0.74, lf + (ragged - 0.5) * 0.12) * (1 - bare)
    lich = core.apply_ramp(saturate(0.4 + 0.45 * ragged), "lichen")
    d = lerp(d, lich, cushion * 0.6)
    height = height + cushion * (0.8 + ragged)
    # Leaves: a scatter of redder and darker, the blueberry's crimson, the crowberry's near-black.
    tint = noise.fbm01(ctx.rng("tint"), (W, W), 6, octaves=3)
    d = lerp(d, d * hexc("#FF9A80") * 1.5, smoothstep(0.66, 0.84, tint) * (1 - bare) * (1 - cushion) * 0.45)
    d = lerp(d, d * 0.45, smoothstep(0.4, 0.25, tint) * (1 - bare) * 0.6)
    d, _ = C.occlude(d, height, 4, 0.45)
    d = C.light(d, height, strength=2.5, amount=0.6)
    d = detile(d)
    return ctx.out(d, s, g, procedural="heath mat (fbm clumps), lichen cushions (voronoi), granite grit")


@texture("ground_needles", "ground", tile=2.0)
def ground_needles(ctx):
    """Spruce and fir floor: a brown-orange duff of fallen needles, feather moss in clumps where it's damp, the odd
    cone and a root under the litter."""
    W = ctx.W
    alb = ctx.load("Ground111", "diffuse", crop=(512, 0, 512, 512))
    disp = ctx.load("Ground111", "displacement", crop=(512, 0, 512, 512))
    moss = ctx.load("moss_ground", "diffuse")
    d, s, g = convert.pbr_to_legacy(alb, height=disp, rough=np.full((W, W), 0.85, np.float32), ramp="duff",
                                    contrast=1.2, dielectric_spec=0.04)
    needles = dead_blades(ctx, 3400, length=(6, 13), width=(0.9, 1.3), key="needles", curl=0.03)
    needles2 = dead_blades(ctx, 1800, length=(5, 11), width=(0.9, 1.2), key="needles2", curl=0.03)
    nt = noise.white(ctx.rng("nt"), (W, W))
    ncol = core.apply_ramp(saturate(0.25 + 0.6 * nt), "duff")
    d = d * 0.6
    d = lerp(d, ncol * 0.7, needles2 * 0.8)
    d = lerp(d, ncol * hexc("#FFB080") * 1.2, needles * 0.85)
    # Feather moss: soft green clumps in the damp lows.
    damp = smoothstep(0.6, 0.74, noise.fbm01(ctx.rng("damp"), (W, W), 30, octaves=5))
    mcol, _, _ = convert.pbr_to_legacy(moss, rough=np.full((W, W), 0.9, np.float32), ramp="feather_moss", contrast=1.3, desat=0.5)
    mcol = lerp(mcol, core.lum(mcol)[..., None] * np.ones(3, np.float32), 0.35)
    fuzz = noise.fbm01(ctx.rng("fuzz"), (W, W), 1.8, octaves=3)
    d = lerp(d, mcol * (0.55 + 0.3 * fuzz)[..., None], damp * 0.75)
    # Cones: small dark ovals, lit on top.
    rng = ctx.rng("cones")
    cones = np.zeros((W, W), np.float32)
    ys, xs = np.mgrid[0:W, 0:W].astype(np.float32)
    for _ in range(14):
        cx, cy = rng.random(2) * W
        a = rng.uniform(0, np.pi)
        dx = (xs - cx + W / 2) % W - W / 2
        dy = (ys - cy + W / 2) % W - W / 2
        u = dx * np.cos(a) + dy * np.sin(a)
        v = -dx * np.sin(a) + dy * np.cos(a)
        cones = np.maximum(cones, saturate(1 - (u / 9) ** 2 - (v / 5) ** 2))
    d = lerp(d, core.apply_ramp(0.35 + 0.3 * nt, "duff") * 0.8, smoothstep(0, 0.2, cones) * (1 - damp))
    height = noise.standardize(disp) * 0.5 + needles * 0.5 + needles2 * 0.35 + damp * fuzz * 1.5 + cones * 2
    d, _ = C.occlude(d, height, 5, 0.4)
    d = C.light(d, height, strength=2, amount=0.6)
    d = detile(d)
    return ctx.out(d, s, g, procedural="needle duff, feather moss clumps, cones")


@texture("granite_lichen", "ground", tile=3.0)
def granite_lichen(ctx):
    """Grey Maritime granite: the scan's speckled grain, split along its joints, weathered pale on top and dark in the
    cracks, blotched with crustose lichen (grey-green rosettes, black spots, a little orange)."""
    W = ctx.W
    alb = ctx.load("rock_granite", "diffuse")
    disp = ctx.load("rock_granite", "displacement")
    ao = ctx.load("rock_granite", "ao")
    rough = ctx.load("rock_granite", "roughness")
    # The joints: a few long straight-ish cracks across the tile, cutting the rock into blocks.
    rng = ctx.rng("joints")
    lines, widths = [], []
    for _ in range(5):
        x, y = rng.random(2) * W
        lines.append(draw.wander_line(rng, x, y, rng.uniform(0, np.pi), W * rng.uniform(0.6, 1.2), 22, 0.05))
        widths.append(rng.uniform(1.5, 3.5))
    joints = noise.blur(draw.strokes((W, W), lines, widths), 1.0)
    height = noise.standardize(disp) + 0.8 * noise.fbm(ctx.rng("h"), (W, W), 60, octaves=4) - joints * 3
    # The scan's salt-and-pepper grain, calmed: a 2008 granite reads as mottled grey at a few metres, not static.
    alb = noise.blur(alb, 2.5) * 0.8 + alb * 0.2
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=height, rough=np.clip(rough + 0.1, 0, 1), ramp="granite",
                                    contrast=1.1, desat=0.7, dielectric_spec=0.1)
    mottle = noise.fbm01(ctx.rng("mottle"), (W, W), 70, octaves=4)
    d = d * (0.8 + 0.35 * mottle)[..., None]
    # Lichen rosettes: grey-green (most), black (some), orange (a few), each a blotchy disc.
    f1, _, idx = noise.voronoi(ctx.rng("ros"), (W, W), 260)[:3]
    cv = noise.cell_values(ctx.rng("rv"), idx, int(idx.max()) + 1)
    edge = noise.fbm01(ctx.rng("re"), (W, W), 2.5, octaves=3)
    r = W / np.sqrt(260)
    size = noise.cell_values(ctx.rng("rs"), idx, int(idx.max()) + 1)
    disc = smoothstep(r * (0.1 + 0.3 * size) * (0.8 + 0.5 * edge), r * (0.05 + 0.2 * size) * (0.8 + 0.5 * edge), f1) * (1 - joints)
    disc *= noise.fbm01(ctx.rng("rz"), (W, W), 50) > 0.42
    greygreen = core.apply_ramp(saturate(0.5 + 0.4 * edge), "lichen")
    d = lerp(d, greygreen, disc * (cv < 0.55) * 0.85)
    d = lerp(d, hexc("#101110") + d * 0.2, disc * ((cv >= 0.55) & (cv < 0.8)) * 0.8)
    d = lerp(d, d * hexc("#E08040") * 1.8, disc * (cv >= 0.93) * 0.8)
    height = height + disc * 0.3
    d = d * (1 - 0.7 * joints)[..., None]
    wet_seed = joints * 0.6 + smoothstep(0.75, 0.9, noise.fbm01(ctx.rng("ws"), (W, W), 12)) * 0.4
    d, s, g, _ = C.streak(ctx.rng("wet"), d, s, g, wet_seed, amount=0.5, colour="#141516", length=0.99, gloss_add=0.4,
                          spec_add=0.25, density=0.5)
    d, _ = C.occlude(d, height, 6, 0.35)
    d = C.light(d, height, strength=3.5, amount=0.6)
    d = detile(d)
    return ctx.out(d, s, g, procedural="joints, lichen rosettes (voronoi), wet streaks")


@texture("ground_red_clay", "ground", tile=2.0)
def ground_red_clay(ctx):
    """Red clay: the rust-red soil of the Fundy shore and the Annapolis Valley, cracked where it's dried, slick and
    darker where water stands in the ruts, a little dead grass holding the edges."""
    W = ctx.W
    alb = ctx.load("dirt_ground", "diffuse")
    disp = ctx.load("dirt_ground", "displacement")
    ao = ctx.load("dirt_ground", "ao")
    height = noise.standardize(disp) + 1.0 * noise.fbm(ctx.rng("h"), (W, W), 45, octaves=4)
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=height, rough=np.full((W, W), 0.7, np.float32), ramp="red_clay",
                                    contrast=1.3, desat=0.6, dielectric_spec=0.08)
    # Dried-mud cracks on the highs.
    f1, f2 = noise.voronoi(ctx.rng("crack"), (W, W), 420)[:2]
    crack = smoothstep(2.2, 0.4, f2 - f1) * smoothstep(-0.2, 0.6, noise.blur(height, 5))
    d = d * (1 - 0.3 * crack)[..., None]
    height = height - crack * 0.8
    # Ruts along v, and the wet in the lows (glossy, darker).
    ruts = smoothstep(0.62, 0.8, noise.fbm01(ctx.rng("ruts"), (W, W), 18, octaves=4, stretch=(0.35, 2.5)))
    height = height - ruts * 1.2
    wet = smoothstep(-0.4, -1.2, noise.blur(height, 5))
    d = d * (1 - 0.4 * wet)[..., None]
    s = lerp(s, 0.4, wet)
    g = lerp(g, 0.85, wet)
    blades = dead_blades(ctx, 500, length=(10, 26), key="edge") * smoothstep(0.55, 0.75, noise.fbm01(ctx.rng("gz"), (W, W), 20))
    straw = core.apply_ramp(saturate(0.5 + 0.4 * noise.fbm01(ctx.rng("st"), (W, W), 6)), "canvas")
    d = lerp(d, straw, blades * (1 - wet) * 0.7)
    d = C.light(d, height + blades * 0.4, strength=2.5, amount=0.55)
    d = detile(d)
    return ctx.out(d, s, g, procedural="drying cracks (voronoi), ruts, wet lows, dead grass")


@texture("bog_sphagnum", "ground", tile=2.0)
def bog_sphagnum(ctx):
    """A raised bog: hummocks of sphagnum (ochre, rust and green-gold), black peat pools between them that take the
    headlamp as a glint, sedge and the pale threads of cotton-grass."""
    W = ctx.W
    moss = ctx.load("moss_ground", "diffuse")
    mdisp = ctx.load("moss_ground", "displacement")
    height = noise.standardize(mdisp) * 0.5 + noise.fbm(ctx.rng("hum"), (W, W), 34, octaves=5, gain=0.55)
    d, s, g = convert.pbr_to_legacy(moss, height=height, rough=np.full((W, W), 0.8, np.float32), ramp="sphagnum",
                                    contrast=1.35, desat=0.7, dielectric_spec=0.06)
    # The sphagnum's colour patches: rust red and green-gold on the hummocks.
    patch = noise.fbm01(ctx.rng("patch"), (W, W), 18, octaves=4)
    d = lerp(d, d * hexc("#D07050") * 1.7, smoothstep(0.6, 0.75, patch) * 0.6)
    d = lerp(d, d * hexc("#A8B060") * 1.5, smoothstep(0.35, 0.22, patch) * 0.5)
    # The capitula: a fine starry texture, each head a pale point.
    heads = smoothstep(0.7, 0.85, noise.fbm01(ctx.rng("heads"), (W, W), 1.3, octaves=2))
    d = d * (0.85 + 0.35 * heads)[..., None]
    water = smoothstep(-0.3, -0.55, height)
    shore = smoothstep(0.05, -0.3, height) * (1 - water)
    peat = core.apply_ramp(saturate(0.3 + 0.4 * patch), "peat")
    d = lerp(d, peat, shore * 0.8)
    d = lerp(d, hexc("#050606") + d * 0.08, water)
    s = lerp(lerp(s, 0.25, shore), 0.9, water)
    g = lerp(lerp(g, 0.55, shore), 0.97, water)
    sedge = dead_blades(ctx, 420, length=(14, 34), width=(1.0, 1.6), key="sedge", curl=0.06) * (1 - water)
    d = lerp(d, core.apply_ramp(saturate(0.55 + 0.3 * noise.white(ctx.rng("sw"), (W, W))), "canvas"), sedge * 0.7)
    d = C.light(d, np.maximum(height, -0.45) + sedge * 0.4 + heads * 0.15, strength=3.5, amount=0.55)
    d = detile(d)
    return ctx.out(d, s, g, procedural="sphagnum hummocks, peat pools (spec), sedge")


@texture("shore_shingle", "ground", tile=5.0)
def shore_shingle(ctx):
    """River or beach cobble: rounded, flattened stones of every size (grey granite most, brown sandstone, the odd red
    one), lying on and in gravel and sand, wet and dark low down, wrack caught between."""
    W = ctx.W
    grav = ctx.load("gravel_ground", "diffuse")
    gdisp = ctx.load("gravel_ground", "displacement")
    sand = ctx.load("sand_desert", "diffuse")
    base = grav * 0.6 + sand * 0.4
    bd, s, g = convert.pbr_to_legacy(base, height=gdisp, rough=np.full((W, W), 0.8, np.float32), ramp="stone", contrast=1.2, desat=0.7)
    height = noise.standardize(gdisp) * 0.3
    d = bd * 0.7
    # The cobbles: flattened ellipsoids, placed big first so small ones fill round them (a max of domes, on a torus).
    rng = ctx.rng("cobbles")
    ys, xs = np.mgrid[0:W, 0:W].astype(np.float32)
    tex = noise.normalize(core.lum(grav))
    top = np.full((W, W), -1e9, np.float32)
    col = np.zeros((W, W, 3), np.float32)
    # At the terrain's 5 m tile (WorldArt.TerrainTile): hand-sized to head-sized stones, a lot of them.
    n = 900
    for i in range(n):
        r = W * (0.03 * (1 - i / n) ** 1.5 + 0.0065)
        cx, cy = rng.random(2) * W
        a = rng.uniform(0, np.pi)
        e = rng.uniform(0.55, 0.9)
        x0, x1, y0, y1 = int(cx - r) - 1, int(cx + r) + 2, int(cy - r) - 1, int(cy + r) + 2
        yy, xx = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        u = (xx - cx) * np.cos(a) + (yy - cy) * np.sin(a)
        v = (-(xx - cx) * np.sin(a) + (yy - cy) * np.cos(a)) / e
        q = 1 - (u * u + v * v) / (r * r)
        dome = np.where(q > 0, np.sqrt(np.maximum(q, 0)) * r * 0.35 + 1.0, -1e9).astype(np.float32)
        kind = rng.random()
        ramp = "granite" if kind < 0.7 else "stone" if kind < 0.98 else "red_clay"
        shade = rng.uniform(0.3, 0.7)
        iy, ix = (yy.astype(int) % W), (xx.astype(int) % W)
        cur = top[iy, ix]
        win = dome > cur
        top[iy, ix] = np.where(win, dome, cur)
        c = core.apply_ramp(saturate(shade + 0.25 * (tex[iy, ix] - 0.5)), ramp)
        col[iy, ix] = np.where(win[..., None], c, col[iy, ix])
    stone = top > 0
    height = np.where(stone, top * 0.3, height)
    d = np.where(stone[..., None], col, d)
    wet = smoothstep(0.6, 0.3, noise.fbm01(ctx.rng("wet"), (W, W), 60, octaves=3))
    d = d * (1 - 0.45 * wet)[..., None]
    s = lerp(np.full((W, W), 0.06, np.float32), 0.6, wet) * lerp(0.4, 1.0, stone.astype(np.float32))
    g = lerp(np.full((W, W), 0.3, np.float32), 0.9, wet)
    wrack = dead_blades(ctx, 500, length=(6, 14), width=(1.0, 1.6), key="wrack", curl=0.4) * (1 - stone)
    d = lerp(d, hexc("#1A1A0C"), wrack * 0.85)
    d, _ = C.occlude(d, height, 4, 0.5)
    d = C.light(d, height, strength=2, amount=0.8)
    d = detile(d)
    return ctx.out(d, s, g, procedural="elliptical cobbles (domes, largest first) on gravel and sand, wet low line, wrack")


@texture("water_dark", "ground", tile=8.0)
def water_dark(ctx):
    """Still dark water: a Southern Upland lake stained with tannin, a cove on a windless night. Near black, with wind
    ripples in the normal map and a high gloss, so what it shows is what it reflects (the lamp, the sky's last light)."""
    W = ctx.W
    # Ripples: long, low swell across the wind, fine cat's-paws over it.
    swell = noise.fbm(ctx.rng("swell"), (W, W), 90, octaves=3, stretch=(3.0, 0.5))
    paws = noise.fbm(ctx.rng("paws"), (W, W), 14, octaves=4, stretch=(2.2, 0.7))
    height = swell * 0.6 + paws * 0.25 * (0.5 + 0.5 * noise.fbm01(ctx.rng("gust"), (W, W), 60, octaves=2))
    core.capture("height", height)
    tone = 0.8 + 0.4 * noise.fbm01(ctx.rng("tone"), (W, W), 70, octaves=3)
    d = np.stack([np.full((W, W), 0.018, np.float32) * tone, np.full((W, W), 0.016, np.float32) * tone, np.full((W, W), 0.013, np.float32) * tone], axis=-1)
    s = np.full((W, W), 0.65, np.float32)
    g = np.full((W, W), 0.93, np.float32)
    return ctx.out(d, s, g, procedural="wind swell and cat's-paws (fbm heights for the normal map), tannin-dark and glossy")


def _spruce(rng, mass, tone, cx, top, base, half_base, lines, widths, vals, club=True):
    """One Maritime spruce into the given buffers: a narrow ragged spire (red and black spruce, maritime-rules.md §5),
    short drooping branches in uneven whorls with gaps where the trunk shows, and a dense club of growth at the top."""
    H, W = mass.shape
    ys, xs = np.mgrid[0:H, 0:W].astype(np.float32)
    y = top + (base - top) * 0.06
    while y < base:
        f = (y - top) / (base - top)
        # The club: the top tenth packed with short dense whorls, wider than the spire just under it.
        clubbed = club and f < 0.12
        half = half_base * ((0.3 + 0.2 * f) if clubbed else (0.14 + 0.86 * f ** 0.85)) * rng.uniform(0.7, 1.1)
        thick = (base - top) * rng.uniform(0.03, 0.055)
        for side in (-1, 1):
            if rng.random() < (0.02 if clubbed else 0.06 + 0.1 * f):
                continue                                  # a whorl missing on one side: the ragged look
            hs = half * rng.uniform(0.55, 1.0)
            droop = hs * rng.uniform(0.3, 0.65)
            u = saturate((xs - cx) * side / hs)
            inside = ((xs - cx) * side >= 0) & ((xs - cx) * side <= hs)
            upper = y + droop * u ** 1.3
            lower = upper + thick * (1 - 0.6 * u)
            sk = inside & (ys >= upper) & (ys <= lower)
            mass[...] = np.maximum(mass, sk.astype(np.float32))
            tone[...] = np.where(sk, np.maximum(tone, saturate(1 - (ys - upper) / thick)), tone)
            # Needles hanging off the branch's end and underside.
            for _ in range(int(3 + 6 * f)):
                t = rng.uniform(0.3, 1.0)
                px, py = cx + side * hs * t, y + droop * t ** 1.3 + thick * 0.5
                a = rng.normal(np.pi / 2 + side * -0.3, 0.5)
                ln = rng.uniform(3, 9) * (0.6 + f)
                lines.append([(px, py), (px + np.cos(a) * ln, py + np.sin(a) * ln)])
                widths.append(rng.uniform(1.5, 3.5))
                vals.append(rng.uniform(0.15, 0.5))
        y += (base - top) * (rng.uniform(0.008, 0.016) if clubbed else rng.uniform(0.014, 0.03))
    # Fill out the whorls a little into a ragged mass (needle clumps between the branches), keeping the gaps.
    grown = noise.blur(mass, 2.5) + 0.25 * (noise.white(rng, (H, W)) - 0.5)
    grown[:8] = 0                                         # the blur wraps: nothing bleeds in from the bottom edge
    mass[...] = np.maximum(mass, smoothstep(0.32, 0.5, grown))
    lines.append([(cx, top), (cx + rng.normal(0, 1), base + 4)])     # leader and trunk
    widths.append(max(2.5, half_base * 0.06))
    vals.append(0.12)


@texture("spruce_card", "foliage", tile=None)
def spruce_card(ctx):
    """A Maritime spruce for crossed cards: a narrow ragged spire, not a Christmas tree. Short drooping branches in
    uneven whorls, gaps where the trunk shows through, a club of dense growth at the top; near-black green with a cold
    rim. Card width 0.3 of its height."""
    from .mat_foliage import card_out, rim, self_shadow
    H, W = 1024, 320
    rng = ctx.rng("spruce")
    mass = np.zeros((H, W), np.float32)
    tone = np.zeros((H, W), np.float32)
    lines, widths, vals = [], [], []
    _spruce(rng, mass, tone, W / 2, 16, H - 20, W * 0.46, lines, widths, vals)
    fringe = draw.strokes((H, W), lines, widths, None, wrap=False)
    fval = draw.strokes((H, W), lines, widths, vals, wrap=False)
    alpha = saturate(np.maximum(mass, fringe))
    clumps = noise.fbm01(ctx.rng("clump"), (H, W), 4, octaves=3)
    t = np.where(mass > 0.5, 0.1 + 0.4 * tone + 0.3 * (clumps - 0.5), fval)
    d = core.apply_ramp(saturate(t), "pine")
    d = d * self_shadow(alpha, 8, 0.4)[..., None]
    d = lerp(d, hexc("#34404A"), rim(alpha, 2.0) * 0.45)
    s = np.full((H, W), 0.05, np.float32)
    g = np.full((H, W), 0.2, np.float32)
    return card_out(ctx, d, alpha, s, g, "ragged whorls, trunk gaps, clubbed top, rim")


@texture("treeline_card", "foliage", tile=None)
def treeline_card(ctx):
    """The edge of a spruce stand, for long cards at the forest's edge and the far treeline: a row of spires of every
    height, packed shoulder to shoulder, its bottom solid (the stand behind), wrapping left to right so it tiles along."""
    from .mat_foliage import card_out, rim
    H, W = 512, 1024
    rng = ctx.rng("treeline")
    mass = np.zeros((H, W), np.float32)
    tone = np.zeros((H, W), np.float32)
    lines, widths, vals = [], [], []
    x = 0.0
    while x < W:
        h = H * rng.uniform(0.5, 0.98)
        half = h * rng.uniform(0.09, 0.14)
        for cx in (x, x - W, x + W):                       # drawn wrapped, so the strip tiles
            if -half < cx < W + half:
                _spruce(rng, mass, tone, cx, H - h, H - 2, half, lines, widths, vals, club=rng.random() < 0.6)
        x += half * rng.uniform(0.7, 1.3)
    ys = np.mgrid[0:H, 0:W][0].astype(np.float32)
    mass = np.maximum(mass, (ys > H * 0.62).astype(np.float32))     # the stand behind, solid below
    fringe = draw.strokes((H, W), lines, widths, None, wrap=False)
    alpha = saturate(np.maximum(mass, fringe))
    t = 0.08 + 0.3 * tone * (ys < H * 0.8) + 0.08 * noise.fbm01(ctx.rng("tl"), (H, W), 6, octaves=3)
    d = core.apply_ramp(saturate(t), "pine")
    d = lerp(d, hexc("#34404A"), rim(alpha, 2.0) * 0.35)
    s = np.full((H, W), 0.04, np.float32)
    g = np.full((H, W), 0.2, np.float32)
    return card_out(ctx, d, alpha, s, g, "packed spires wrapped to tile, solid stand below, rim")


# ---------------------------------------------------------------------------------------------------------------------
# The towns' walls (the director, 7 Oct 2026, with photographs of Cape Breton, Peggy's Cove, Lunenburg, Shelburne and
# Blue Rocks; ARCHITECTURE §8 note 281). Both are procedural, so they rebuild the same without the CC0 scans, and both
# are near-neutral: a house's paint (or a shingle's stain) is the kit's tint over them (Art/MaritimeKit.cs).

def _courses(ctx, W, rows, widths, key):
    """Courses of pieces across the tile: for each pixel, its row, the piece in that row (a per-piece id), how far down
    the row it is (0 at the top, 1 at the butt) and how far it is from the nearest joint. Each row's joints are offset
    at random and its pieces' widths drawn from `widths` (px), wrapping round the tile so it repeats seamlessly."""
    xs, ys = noise.grid(W, W)
    h = W // rows
    row = (ys // h).astype(np.int32)
    down = (ys - row * h) / h
    rng = ctx.rng(key)
    piece = np.zeros((W, W), np.int32)
    joint = np.full((W, W), 1e9, np.float32)
    pid = 0
    for r in range(rows):
        off = rng.uniform(0, W)
        edges = [off]
        while edges[-1] < off + W - widths[0]:
            edges.append(edges[-1] + rng.uniform(*widths))
        edges[-1] = off + W
        e = np.array(edges, np.float32)
        x = (xs[row == r] - off) % W + off
        k = np.searchsorted(e, x, side="right") - 1
        piece[row == r] = pid + k
        joint[row == r] = np.minimum(x - e[k], e[k + 1] - x)
        pid += len(edges)
    return row, piece, down, joint, pid


@texture("shingle_cedar", "wood", tile=1.0)
def shingle_cedar(ctx):
    """Cedar shingles weathered silver: eight courses a metre (a 125 mm exposure), each shingle its own width, tone and
    grain, the butt of each course throwing a shadow on the one below, thin dark joints, a little lichen and the rain's
    streaks. Near-neutral grey, so a stain or a coat of paint is the kit's tint over it (Peggy's Cove's teal house)."""
    W = ctx.W
    row, piece, down, joint, n = _courses(ctx, W, 8, (36, 120), "courses")
    tone = ctx.rng("tone").random(n).astype(np.float32)[piece]
    warm = ctx.rng("warm").random(n).astype(np.float32)[piece]
    # Grain along the shingle (down it), each shingle's own piece of it.
    shift = (ctx.rng("shift").random(n).astype(np.float32) * W)[piece]
    grain = noise.fbm01(ctx.rng("grain"), (W, W), 2.2, octaves=4, stretch=(0.08, 1.0))
    grain = np.take_along_axis(grain, ((np.arange(W)[None, :] + shift.astype(np.int32)) % W), axis=1)
    silver, brown = core.hexc("#8C8A84"), core.hexc("#7A6E62")
    d = lerp(silver, brown, (warm * 0.7)[..., None] * np.ones((1, 1, 3), np.float32))
    d = d * (0.72 + 0.4 * tone[..., None]) * (0.7 + 0.6 * grain[..., None])
    # Each shingle a little darker down one side, where it cupped as it dried.
    cup = (ctx.rng("cup").random(n).astype(np.float32) - 0.5)[piece]
    d = d * (1 - 0.12 * cup * smoothstep(0, 1, down))[..., None]
    # Each course thicker toward its butt; the butt's shadow over the top of the course below; dark joints.
    butt_shadow = smoothstep(0.18, 0.0, down)
    gapm = smoothstep(2.2, 0.6, joint)
    height = down * 2.0 - butt_shadow * 1.5 - gapm * 2.0 + grain * 0.6
    d = d * (1 - 0.55 * butt_shadow)[..., None] * (1 - 0.8 * gapm)[..., None]
    # Weather: the bottoms of the shingles darker where the rain sits, streaks down from the joints, lichen here and there.
    d = d * (1 - 0.18 * smoothstep(0.7, 1.0, down))[..., None]
    s = np.full((W, W), 0.05, np.float32)
    g = np.full((W, W), 0.12, np.float32)
    d, s, g, _ = C.soot(ctx.rng("rain"), d, s, g, amount=0.3, scale=40, coverage=0.35, stretch=(0.4, 2.0), colour="#2A2826")
    lf = noise.fbm01(ctx.rng("lichen"), (W, W), 48, octaves=4)
    spots = smoothstep(0.8, 0.84, lf) * (1 - gapm)
    d = lerp(d, core.hexc("#7E8670") * (0.8 + 0.4 * grain[..., None]), spots * 0.3)
    d = C.light(d, height, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="courses of cedar shingles: widths, tone, grain, butt shadow, joints, rain, lichen")


@texture("clapboard", "wood", tile=1.0)
def clapboard(ctx):
    """Painted clapboard: eight lapped boards a metre, each lap's shadow under the board above, a butt joint here and
    there, the grain faint under the paint, and the paint worn through to grey wood along the laps and the weather side.
    Near-white, so the house's paint is the kit's tint over it."""
    W = ctx.W
    row, piece, down, joint, n = _courses(ctx, W, 8, (300, 700), "boards")
    paint, wood = core.hexc("#D9D7D0"), core.hexc("#8E877C")
    grain = noise.fbm01(ctx.rng("grain"), (W, W), 3.0, octaves=4, stretch=(1.0, 0.06))
    tone = ctx.rng("tone").random(n).astype(np.float32)[piece]
    d = paint * (0.93 + 0.06 * tone[..., None] + 0.04 * grain[..., None])
    # The lap: a board thin at its top, thickest at its bottom edge, which shades the top of the board under it.
    lap = smoothstep(0.16, 0.0, down)
    lip = smoothstep(0.93, 1.0, down)
    joints = smoothstep(1.6, 0.5, joint)
    height = down * 1.6 - lap * 1.6 + grain * 0.25 - joints * 1.5
    d = d * (1 - 0.5 * lap)[..., None] * (1 - 0.15 * lip)[..., None] * (1 - 0.7 * joints)[..., None]
    # Paint worn through: flakes clustered along the laps and in broad weathered patches.
    wear = noise.fbm01(ctx.rng("wear"), (W, W), 60, octaves=5, stretch=(1.0, 0.5))
    flake = smoothstep(0.9, 0.93, wear + 0.08 * lip) * 0.3
    d = lerp(d, wood * (0.8 + 0.4 * grain[..., None]), flake)
    height = height - flake * 0.3
    s = np.full((W, W), 0.08, np.float32) * (1 - flake)
    g = np.full((W, W), 0.22, np.float32) * (1 - 0.6 * flake)
    d, s, g, _ = C.soot(ctx.rng("grime"), d, s, g, amount=0.25, scale=50, coverage=0.3, stretch=(0.6, 1.6), colour="#3A3632")
    d = C.light(d, height, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="lapped painted boards: lap shadow, butt joints, grain, worn paint, grime")
