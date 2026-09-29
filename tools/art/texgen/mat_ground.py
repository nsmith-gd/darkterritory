"""Ground (GDD §30: rail corridor - ballast, marsh, black forest, slag heaps, contaminated
rural spaces). Environment density: 256 px over 2 m = 128 px/m (§27's floor for environment),
since the ground is seen at a grazing angle through fog and repeats over kilometres."""
from __future__ import annotations

import numpy as np

from . import common as C
from . import convert, core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def faceted_stones(ctx, n, key="stones", aspect=(1.0, 1.0), gap=2.0, tilt=0.9):
    """Crushed stone / lumps: voronoi cells, each a tilted facet (random plane) so the baked
    light picks out angular faces, sunk at the cell borders. Returns height, cell values,
    cell edge mask (0 in the gaps), cell index."""
    W = ctx.W
    rng = ctx.rng(key)
    f1, f2, idx, ox, oy = noise.voronoi(rng, (W, W), n, aspect=aspect, offsets=True)
    count = int(idx.max()) + 1
    size = np.sqrt(W * W / n)
    edge = f2 - f1
    body = smoothstep(gap * 0.5, gap * 2.5, edge)
    # Crushed stone: each stone is two planes meeting at a ridge through its centre (a random
    # direction), tilted a little - so the baked light gives every stone a lit face and a dark
    # face, the read of a pile of angular rock rather than a mosaic.
    ang = rng.uniform(0, np.pi, count).astype(np.float32)[idx]
    tx = rng.normal(0, tilt * 0.4, count).astype(np.float32)[idx]
    ty = rng.normal(0, tilt * 0.4, count).astype(np.float32)[idx]
    u = (ox * np.cos(ang) + oy * np.sin(ang)) / size
    ridge = -np.abs(u) * tilt * 1.6
    facet = ridge + tx * ox / size + ty * oy / size
    crown = np.sqrt(smoothstep(0, size * 0.5, edge))
    height = (body * (1.0 + facet) + crown * 0.8) * size * 0.15
    cv = noise.cell_values(rng, idx, count)
    return height.astype(np.float32), cv, body, idx


@texture("ballast", "ground", tile=2.0)
def ballast(ctx):
    """Crushed-stone track ballast: angular grey-brown stones, a few rusty and a few pale, dark
    gaps, oil and soot stains dripped from passing engines."""
    W = ctx.W
    height, cv, body, idx = faceted_stones(ctx, 900, gap=1.6)
    grav = ctx.load("gravel_ground", "diffuse")
    rock = ctx.load("Rock064", "diffuse", crop=(512, 512, 512, 512))
    detail = noise.normalize(core.lum(rock) * 0.6 + core.lum(grav) * 0.4)
    t = saturate(0.2 + 0.55 * cv + 0.25 * (detail - 0.5))
    base = core.apply_ramp(t, "ballast")
    rn = ctx.rng("kind").random(int(idx.max()) + 1).astype(np.float32)[idx]
    base = lerp(base, core.apply_ramp(t * 0.8, "rust") * 0.8, (rn > 0.9) * 0.6)
    base = lerp(base, base * 1.35, (rn < 0.07) * 1.0)
    d = base * (0.2 + 0.8 * body)[..., None]
    # Occlusion between stones: the gaps are dark, the stones sit on each other. Not pure black:
    # a pepper-and-salt ballast shimmers at distance.
    d, _ = C.occlude(d, height, 5, 0.4)
    s = np.full((W, W), 0.08, np.float32) * body
    g = np.full((W, W), 0.2, np.float32)
    oil = smoothstep(0.55, 0.8, noise.fbm01(ctx.rng("oil"), (W, W), 45, octaves=5))
    d = lerp(d, d * 0.3 + hexc("#0A0A0A") * 0.3, oil * 0.8)
    s = lerp(s, 0.3, oil * body)
    g = lerp(g, 0.7, oil)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=60, coverage=0.45)
    d = C.light(d, height, strength=3.0, amount=1.0)
    return ctx.out(d, s, g, procedural="angular stones (voronoi facets), oil stains")


def dead_blades(ctx, n, length=(8, 26), width=(1.2, 2.2), angle_spread=np.pi, key="blades", curl=0.25):
    rng = ctx.rng(key)
    W = ctx.W
    lines, widths, vals = [], [], []
    for _ in range(n):
        x, y = rng.random(2) * W
        a = rng.uniform(-angle_spread, angle_spread)
        L = rng.uniform(*length)
        lines.append(draw.wander_line(rng, x, y, a, L, 4, curl))
        widths.append(rng.uniform(*width))
        vals.append(rng.uniform(0.4, 1.0))
    return draw.strokes((W, W), lines, widths, vals)


@texture("ground_mud", "ground", tile=2.0)
def ground_mud(ctx):
    """Dark wet mud and dead grass: Ground111's soil, wet-darkened, puddled in the lows (the
    spec map carries the wet), matted dead blades pressed into it."""
    W = ctx.W
    alb = ctx.load("Ground111", "diffuse")
    disp = ctx.load("Ground111", "displacement")
    ao = ctx.load("Ground111", "ao")
    dirt = ctx.load("dirt_ground", "diffuse")
    alb = alb * 0.7 + dirt * 0.3
    height = noise.standardize(disp) + 1.2 * noise.fbm(ctx.rng("h"), (W, W), 40, octaves=4)
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=height, rough=np.full((W, W), 0.6, np.float32), ramp="mud",
                                    contrast=1.3, dielectric_spec=0.1)
    wet = smoothstep(-0.2, -1.0, noise.blur(height, 6))
    d = d * (1 - 0.45 * wet)[..., None]
    s = lerp(s, 0.35, wet)
    g = lerp(g, 0.8, wet)
    blades = dead_blades(ctx, 700)
    bt = noise.fbm01(ctx.rng("bt"), (W, W), 8)
    grass = core.apply_ramp(saturate(0.4 + 0.5 * bt), "grass") * 1.1
    d = lerp(d, grass, blades * (1 - wet) * 0.85)
    tracks = smoothstep(0.6, 0.8, noise.fbm01(ctx.rng("tr"), (W, W), 12, octaves=4, stretch=(0.5, 2)))
    d = d * (1 - 0.3 * tracks)[..., None]
    d = C.light(d, height + blades * 0.5, strength=2, amount=0.5)
    return ctx.out(d, s, g, procedural="wet lows, pressed dead grass")


@texture("ground_grass", "ground", tile=2.0)
def ground_grass(ctx):
    """Sparse dead grass on mud: Grass005 drained to muddy olive, balding to mud in patches."""
    W = ctx.W
    alb = ctx.load("Grass005", "diffuse")
    ao = ctx.load("Grass005", "ao")
    disp = ctx.load("Grass005", "displacement")
    mud = ctx.load("Ground111", "diffuse", crop=(256, 256, 512, 512))
    d_g, s, g = convert.pbr_to_legacy(alb, ao=ao, height=disp, rough=np.full((W, W), 0.8, np.float32), ramp="grass",
                                      contrast=1.4, desat=0.9, dielectric_spec=0.05)
    d_m, _, _ = convert.pbr_to_legacy(mud, rough=np.full((W, W), 0.7, np.float32), ramp="mud", contrast=1.2)
    bald = smoothstep(0.5, 0.7, noise.fbm01(ctx.rng("bald"), (W, W), 35, octaves=5))
    d = lerp(d_g, d_m, bald)
    blades = dead_blades(ctx, 1100, length=(10, 30), key="dead")
    straw = core.apply_ramp(saturate(0.55 + 0.4 * noise.fbm01(ctx.rng("st"), (W, W), 6)), "canvas")
    d = lerp(d, straw, blades * 0.6)
    d = C.light(d, disp * 3 + blades, strength=2, amount=0.5)
    return ctx.out(d, s, g, procedural="bald mud patches, straw-dead blades")


@texture("ground_forest", "ground", tile=2.0)
def ground_forest(ctx):
    """Black forest floor: a mat of dead needles over black soil, a root or two, cones."""
    W = ctx.W
    alb = ctx.load("Ground111", "diffuse", crop=(512, 0, 512, 512))
    disp = ctx.load("Ground111", "displacement", crop=(512, 0, 512, 512))
    d, s, g = convert.pbr_to_legacy(alb, height=disp, rough=np.full((W, W), 0.8, np.float32), ramp="forest",
                                    contrast=1.2, dielectric_spec=0.04)
    rng = ctx.rng("roots")
    roots, rw = [], []
    for _ in range(3):
        x, y = rng.random(2) * W
        roots.append(draw.wander_line(rng, x, y, rng.uniform(0, 2 * np.pi), rng.uniform(180, 380), 18, 0.18))
        rw.append(rng.uniform(7, 11))
    rootm = draw.strokes((W, W), roots, rw)
    rootm = noise.blur(rootm, 1.2)
    needles = dead_blades(ctx, 2600, length=(8, 16), width=(1.0, 1.6), key="needles", curl=0.05)
    needles2 = dead_blades(ctx, 1200, length=(6, 14), width=(1.0, 1.4), key="needles2", curl=0.05)
    nt = noise.white(ctx.rng("nt"), (W, W))
    ncol = core.apply_ramp(saturate(0.2 + 0.7 * nt), ["#0C0A08", "#221A12", "#3A2C1E", "#4E3E2A"])
    d = lerp(d, ncol * 0.8, needles2 * 0.7)
    d = lerp(d, ncol, needles * 0.8)
    bark = core.apply_ramp(saturate(0.2 + 0.4 * noise.fbm01(ctx.rng("rb"), (W, W), 3, stretch=(1, 3))), "wood_grey") * 0.6
    d = lerp(d, bark, rootm)
    height = noise.standardize(disp) * 0.5 + needles * 0.6 + needles2 * 0.4 + rootm * 3
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.3, scale=50, coverage=0.45, colour="#08090A")
    d = C.light(d, height, strength=2, amount=0.6)
    d, _ = C.occlude(d, height, 5, 0.4)
    return ctx.out(d, s, g, procedural="needle litter, roots")


@texture("marsh", "ground", tile=2.0)
def marsh(ctx):
    """Wet black-green marsh: moss hummocks, black standing water between them (near-black in
    the diffuse, high gloss and spec so the headlamp glints off it), reed litter."""
    W = ctx.W
    moss = ctx.load("moss_ground", "diffuse")
    ground = ctx.load("Ground111", "diffuse")
    gdisp = ctx.load("Ground111", "displacement")
    alb = moss * 0.5 + ground * 0.5
    height = noise.standardize(gdisp) * 0.4 + noise.fbm(ctx.rng("hum"), (W, W), 30, octaves=5, gain=0.55)
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.75, np.float32), ramp="marsh",
                                    contrast=1.3, desat=0.85, dielectric_spec=0.06)
    water = smoothstep(-0.25, -0.45, height)
    shore = smoothstep(0.1, -0.25, height) * (1 - water)
    d = d * (1 - 0.35 * shore)[..., None]
    d = lerp(d, hexc("#07090A") + d * 0.1, water)
    s = lerp(lerp(s, 0.2, shore), 0.85, water)
    g = lerp(lerp(g, 0.5, shore), 0.95, water)
    reeds = dead_blades(ctx, 500, length=(14, 36), width=(1.2, 2.0), key="reeds", curl=0.08) * (1 - water * 0.6)
    d = lerp(d, core.apply_ramp(saturate(0.5 + 0.3 * noise.white(ctx.rng("rw"), (W, W))), "grass"), reeds * 0.7)
    d = C.light(d, np.maximum(height, -0.35) + reeds * 0.4, strength=4, amount=0.5)
    return ctx.out(d, s, g, procedural="hummocks, standing water (spec), reed litter")


@texture("rock_cliff", "ground", tile=2.0)
def rock_cliff(ctx):
    """Dark wet rock face: Rock064's scan, its moss drained to dark olive, wet streaks running
    down the face (darker, glossy), soot bloom."""
    W = ctx.W
    alb = ctx.load("Rock064", "diffuse")
    disp = ctx.load("Rock064", "displacement")
    ao = ctx.load("Rock064", "ao")
    rough = ctx.load("Rock064", "roughness")
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=disp, rough=np.clip(rough + 0.2, 0, 1), ramp="rock",
                                    contrast=1.3, desat=0.85, dielectric_spec=0.08)
    # The scan's green moss: keep a trace as dark olive in the cracks.
    green = saturate((alb[..., 1] - alb[..., 0]) * 8)
    d = lerp(d, core.apply_ramp(saturate(0.3 + 0.4 * noise.normalize(core.lum(alb))), "olive_paint") * 0.7, green * 0.6)
    wet_seed = smoothstep(0.7, 0.9, noise.fbm01(ctx.rng("ws"), (W, W), 10)) * noise.cavity_from_height(disp, 4)
    d, s, g, wet = C.streak(ctx.rng("wet"), d, s, g, wet_seed + 0.3 * smoothstep(0.8, 0.95, noise.white(ctx.rng("wx"), (W, W))),
                            amount=0.7, colour="#0A0B0C", length=0.992, gloss_add=0.5, spec_add=0.35, density=0.6)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.3, scale=60, coverage=0.4)
    d = C.light(d, noise.standardize(disp), strength=4, amount=0.6)
    return ctx.out(d, s, g, procedural="wet streaks, moss drained to olive")


@texture("coal", "ground", tile=1.0)
def coal(ctx):
    """Coal lumps in a tender/heap: black faceted lumps whose facets throw sharp glossy
    highlights (the spec map does the work), dust between."""
    W = ctx.W
    height, cv, body, idx = faceted_stones(ctx, 260, key="lumps", gap=2.0, tilt=1.4)
    height2, cv2, body2, _ = faceted_stones(ctx, 900, key="small", gap=1.2, tilt=1.2)
    under = 1 - body
    height = np.maximum(height, height2 * 0.6 * under + height * body)
    asph = ctx.load("Asphalt033", "diffuse")
    tex = noise.normalize(core.lum(asph))
    t = saturate(0.3 + 0.45 * cv + 0.3 * (tex - 0.5))
    d = core.apply_ramp(t, "coal") * 1.3
    d = d * (0.1 + 0.9 * np.maximum(body, body2 * 0.7))[..., None]
    # Facets facing the light (upper-left) are glossy: coal's conchoidal fracture.
    lit = noise.bake_light(height, 1.5, 1.0)
    glossy = smoothstep(1.05, 1.5, lit)
    s = saturate(0.15 + 0.6 * glossy) * body
    g = saturate(0.55 + 0.35 * glossy)
    d = C.light(d, height, strength=1.5, amount=1.0)
    # The lit facets catch a cold blue-grey sheen even in the diffuse: coal reads by its glints.
    d = lerp(d, hexc("#3A3E46"), glossy * 0.35 * body)
    return ctx.out(d, s, g, procedural="faceted lumps (voronoi), gloss on lit facets")


@texture("slag", "ground", tile=2.0)
def slag(ctx):
    """Industrial slag and ash heap: grey-brown ash, vesicular clinker (pitted), glassy black
    beads, rusty crust patches."""
    W = ctx.W
    asph = ctx.load("Asphalt033", "diffuse")
    adisp = ctx.load("Asphalt033", "displacement")
    ground = ctx.load("Ground111", "diffuse", crop=(0, 512, 512, 512))
    alb = asph * 0.6 + ground * 0.4
    clinker_h, cv, body, idx = faceted_stones(ctx, 320, key="clinker", gap=2.5, tilt=0.8)
    ash = smoothstep(0.45, 0.65, noise.fbm01(ctx.rng("ash"), (W, W), 40, octaves=5))
    vesicles = smoothstep(0.78, 0.9, noise.fbm01(ctx.rng("ves"), (W, W), 1.5, octaves=2))
    height = lerp(clinker_h, noise.standardize(adisp) * 0.5, ash) - vesicles * 1.2
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.85, np.float32), ramp="slag",
                                    contrast=1.2, dielectric_spec=0.05)
    d = d * lerp(0.3 + 0.8 * body, 1.15, ash)[..., None]
    d = d * (1 - 0.6 * vesicles)[..., None]
    beads = (cv > 0.88) * body * (1 - ash)
    d = lerp(d, hexc("#0A0A0C"), beads * 0.9)
    s = lerp(s, 0.6, beads)
    g = lerp(g, 0.85, beads)
    rust, _ = C.rust_layer(ctx, (W, W))
    crust = smoothstep(0.7, 0.85, noise.fbm01(ctx.rng("cr"), (W, W), 15)) * (1 - ash)
    d = lerp(d, rust * 0.7, crust * 0.6)
    d = C.light(d, height, strength=1.5, amount=0.7)
    return ctx.out(d, s, g, procedural="clinker (voronoi), ash drifts, vesicles, glassy beads")


@texture("cobbles", "masonry", tile=1.0)
def cobbles(ctx):
    """Station platform/street cobbles: PavingStones151's fan setts, soot-darkened, dirt and
    moss in the joints, worn tops a touch glossy with wet."""
    W = ctx.W
    # Half the scan per tile (setts ~10 cm, readable at 256 px/m); the crop gets the seam fix.
    cr = (256, 256, 512, 512)
    alb = ctx.load("PavingStones151", "diffuse", crop=cr)
    disp = ctx.load("PavingStones151", "displacement", crop=cr)
    ao = ctx.load("PavingStones151", "ao", crop=cr)
    rough = ctx.load("PavingStones151", "roughness", crop=cr)
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=disp, rough=np.clip(rough + 0.15, 0, 1), ramp="stone",
                                    contrast=1.3, ao_amt=0.8, dielectric_spec=0.1, gloss_cap=0.6)
    joints = noise.cavity_from_height(disp, 3)
    d = lerp(d, hexc("#12110E"), smoothstep(0.2, 0.6, joints) * 0.7)
    moss = smoothstep(0.3, 0.6, joints) * smoothstep(0.5, 0.7, noise.fbm01(ctx.rng("moss"), (W, W), 20))
    d = lerp(d, hexc("#1E2216"), moss * 0.6)
    tops = noise.convexity_from_height(disp, 6)
    g = lerp(g, 0.6, tops * 0.6)
    s = lerp(s, 0.2, tops * 0.5)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.4, scale=50, coverage=0.45)
    d = C.light(d, noise.standardize(disp), strength=4, amount=0.5)
    return ctx.out(d, s, g, procedural="joint dirt and moss, soot, wet tops")
