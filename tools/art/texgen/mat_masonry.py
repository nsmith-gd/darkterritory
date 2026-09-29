"""Masonry (GDD §30: fortified towns, rail bridges and viaducts, foundries, dead settlements).
Blocks are laid out in explicit courses that sum to the tile, so the bond tiles exactly; each
block samples the stone photo at its own offset and tone."""
from __future__ import annotations

import numpy as np

from . import common as C
from . import convert, core, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def coursed(W, course_h, rng, min_len, max_len):
    """Running bond: courses of height course_h (list, sums to W) each split into blocks of
    random length (summing to W), offset per course. Returns block id, distance to the block's
    nearest edge (px), v within the block (0 top .. 1 bottom), block count."""
    assert sum(course_h) == W
    bid = np.zeros((W, W), np.int32)
    dist = np.zeros((W, W), np.float32)
    vin = np.zeros((W, W), np.float32)
    xs = np.arange(W, dtype=np.float32)
    y0, n = 0, 0
    for ch in course_h:
        lens = []
        while sum(lens) < W:
            lens.append(int(rng.integers(min_len, max_len + 1)))
        lens[-1] -= sum(lens) - W
        if lens[-1] < min_len // 2 and len(lens) > 1:
            last = lens.pop()
            lens[-1] += last
        off = int(rng.integers(0, W))
        starts = np.cumsum([0] + lens[:-1])
        # Block index per column (after the course offset) and distance to its vertical joints.
        col = (xs - off) % W
        k = np.searchsorted(np.cumsum(lens), col, side="right")
        a = starts[k]
        b = a + np.array(lens)[k]
        dx = np.minimum(col - a, b - col)
        ys = np.arange(ch, dtype=np.float32)
        dy = np.minimum(ys + 0.5, ch - ys - 0.5)
        bid[y0:y0 + ch] = (n + k)[None, :]
        dist[y0:y0 + ch] = np.minimum(dx[None, :], dy[:, None])
        vin[y0:y0 + ch] = ((ys + 0.5) / ch)[:, None]
        y0 += ch
        n += len(lens)
    return bid, dist, vin, n


def per_block(photo, bid, n, rng):
    """Each block reads the photo at its own offset (no two blocks alike)."""
    H, W = bid.shape
    ph, pw = photo.shape[:2]
    oy = rng.integers(0, ph, n)[bid]
    ox = rng.integers(0, pw, n)[bid]
    ys, xs = np.mgrid[0:H, 0:W]
    return photo[(ys + oy) % ph, (xs + ox) % pw]


@texture("stone_block", "masonry", tile=2.0)
def stone_block(ctx):
    """Large dressed grey-brown blocks (viaducts, fortress walls): four courses per 2 m, pillowed
    faces, chipped arrises, soot washed down from every joint, moss creeping from the joints."""
    W = ctx.W
    rng = ctx.rng("bond")
    bid, dist, vin, n = coursed(W, [128, 128, 128, 128], rng, 150, 300)
    rock = ctx.load("Rock064", "diffuse")
    rdisp = ctx.load("Rock064", "displacement")
    conc = ctx.load("Concrete034", "diffuse", shape=(W, W), repeat=(1, 2))
    r2 = ctx.rng("blocks")
    alb = per_block(rock, bid, n, r2) * 0.5 + per_block(conc, bid, n, ctx.rng("blocks")) * 0.5
    hd = per_block(rdisp, bid, n, ctx.rng("blocks"))
    tone = ctx.rng("tone").random(n).astype(np.float32)[bid]
    # Chipped arrises: the joint's edge wobbles with noise so blocks aren't CAD rectangles.
    chip = noise.fbm(ctx.rng("chip"), (W, W), 4, octaves=3) * 2.5
    de = dist + chip
    mortar = smoothstep(6.0, 3.5, de)
    pillow = smoothstep(0, 22, de)
    height = pillow * 2.0 + noise.standardize(hd) * 0.4 - mortar * 1.5
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.85, np.float32), ramp="stone",
                                    contrast=1.1, desat=0.9, dielectric_spec=0.06)
    d = d * (0.65 + 0.7 * tone)[..., None]
    mcol = core.apply_ramp(saturate(0.2 + 0.3 * noise.fbm01(ctx.rng("m"), (W, W), 3)), "concrete") * 0.35
    d = lerp(d, mcol, mortar)
    # Soot and rain: washed down the face from each horizontal joint.
    joint_seed = mortar * (vin > 0.5)
    d, s, g, _ = C.streak(ctx.rng("rain"), d, s, g, joint_seed * 0.8, amount=0.55, colour="#12110F", length=0.985,
                          density=0.7, gloss_add=0.0)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.55, scale=45, coverage=0.5, stretch=(0.7, 2.0),
                        colour="#0E0D0C")
    # Moss in and just above the bed joints, patchy.
    near = smoothstep(14, 0, de) * (vin > 0.6)
    moss = near * smoothstep(0.5, 0.75, noise.fbm01(ctx.rng("moss"), (W, W), 12, octaves=4))
    d = lerp(d, core.apply_ramp(saturate(0.3 + 0.5 * noise.white(ctx.rng("mw"), (W, W))), "grass") * 0.8, moss * 0.7)
    d = C.light(d, height, strength=2, amount=0.65)
    return ctx.out(d, s, g, procedural="coursed bond, pillowed faces, chipped arrises, joint soot, moss")


@texture("brick_soot", "masonry", tile=2.0)
def brick_soot(ctx):
    """Soot-blackened brick: Bricks105 darkened toward charcoal, the soot heaviest in broad
    plumes (smoke from the line), bricks' faces still just readable as brick."""
    W = ctx.W
    alb = ctx.load("Bricks105", "diffuse", repeat=(1, 2))
    disp = ctx.load("Bricks105", "displacement", repeat=(1, 2))
    ao = ctx.load("Bricks105", "ao", repeat=(1, 2))
    rough = ctx.load("Bricks105", "roughness", repeat=(1, 2))
    d, s, g = convert.pbr_to_legacy(alb, ao=ao, height=disp, rough=np.clip(rough + 0.2, 0, 1), ramp="brick",
                                    contrast=1.3, desat=0.75, ao_amt=0.8, dielectric_spec=0.06)
    joints = noise.cavity_from_height(disp, 3)
    d = d * (1 - 0.5 * smoothstep(0.2, 0.6, joints))[..., None]
    # Soot-blackened: a general blackening (decades of smoke) plus heavier plumes.
    d = lerp(d, core.lum(d)[..., None] * np.array([1.0, 0.95, 0.9], np.float32), 0.4) * 0.55
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.85, scale=40, coverage=0.65, stretch=(0.7, 2.2),
                        colour="#0C0B0B")
    d, s, g, _ = C.streak(ctx.rng("rain"), d, s, g, smoothstep(0.3, 0.7, joints) * 0.4, amount=0.45,
                          colour="#0E0D0C", length=0.985, density=0.5)
    # Efflorescence: a faint pale bloom leaching out of a few joints.
    eff = smoothstep(0.75, 0.9, noise.fbm01(ctx.rng("eff"), (W, W), 25)) * smoothstep(0.1, 0.5, joints)
    d = lerp(d, hexc("#5A5650"), eff * 0.4)
    d = C.light(d, noise.standardize(disp), strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="soot plumes, rain streaks, efflorescence")


@texture("concrete_stain", "masonry", tile=2.0)
def concrete_stain(ctx):
    """Stained concrete: Concrete034 darkened, rust and water stains running down, hairline
    cracks, a formwork line."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    # Concrete034's swirl is strong enough to read as marble at this scale: it gives the
    # breakup, the plainer generated concrete_wall and Asphalt033's aggregate give the body.
    alb = ctx.load("Concrete034", "diffuse", shape=(W, W), repeat=(1, 2))
    disp = ctx.load("Concrete034", "displacement", shape=(W, W), repeat=(1, 2))
    wall = ctx.load("concrete_wall", "diffuse")
    agg = ctx.load("Asphalt033", "diffuse")
    alb = alb * 0.35 + wall * 0.4 + agg * 0.25 * (np.mean(wall) / np.mean(agg))
    height = noise.standardize(disp) * 0.5
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.85, np.float32), ramp="concrete",
                                    contrast=0.9, dielectric_spec=0.06)
    # Formwork lines every 1 m (two per tile): a slight lip and a bleed line.
    form = np.exp(-(np.minimum(ys % 256, 256 - ys % 256) / 2.0) ** 2)
    d = d * (1 - 0.35 * form)[..., None]
    from . import draw
    rng = ctx.rng("cracks")
    lines = [draw.wander_line(rng, *(rng.random(2) * W), rng.uniform(0.9, 2.2), rng.uniform(80, 220), 20, 0.35)
             for _ in range(7)]
    cr = draw.strokes((W, W), lines, [rng.uniform(1.5, 3) for _ in lines])
    d = d * (1 - 0.7 * cr)[..., None]
    rust_seed = smoothstep(0.85, 0.95, noise.fbm01(ctx.rng("rs"), (W, W), 6)) * form
    d, s, g, _ = C.streak(ctx.rng("rust"), d, s, g, rust_seed + form * 0.15, amount=0.6, colour="#3A2618",
                          length=0.99, density=0.6)
    d, s, g, _ = C.streak(ctx.rng("wet"), d, s, g, cr * 0.6 + form * 0.3, amount=0.5, colour="#141412",
                          length=0.99, density=0.7)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.45, scale=60, coverage=0.45)
    d = C.light(d, height - cr * 2, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="formwork lines, cracks, rust and water streaks, soot")


@texture("plaster_ruin", "masonry", tile=2.0)
def plaster_ruin(ctx):
    """Peeling plaster over brick (ruined town buildings): a dirty lime render, fallen away in
    big irregular patches to show soot-dark brick; the render's broken edge casts a lip."""
    W = ctx.W
    pl = ctx.load("plaster_wall", "diffuse")
    pdisp = ctx.load("Concrete034", "displacement", shape=(W, W), repeat=(1, 2))
    pl = pl * (0.85 + 0.3 * noise.normalize(pdisp))[..., None]
    br = ctx.load("Bricks105", "diffuse", repeat=(2, 4))
    bdisp = ctx.load("Bricks105", "displacement", repeat=(2, 4))
    bao = ctx.load("Bricks105", "ao", repeat=(2, 4))
    dp, sp, gp = convert.pbr_to_legacy(pl, height=pdisp, rough=np.full((W, W), 0.9, np.float32), ramp="plaster",
                                       contrast=0.8, dielectric_spec=0.05)
    db, sb, gb = convert.pbr_to_legacy(br, ao=bao, height=bdisp, rough=np.full((W, W), 0.85, np.float32), ramp="brick",
                                       contrast=1.3, ao_amt=0.8, dielectric_spec=0.05)
    loss = noise.fbm01(ctx.rng("loss"), (W, W), 45, octaves=6, gain=0.6)
    gone = smoothstep(0.56, 0.6, loss)
    lip = smoothstep(0.5, 0.56, loss) * (1 - gone)          # the thin broken edge of the render
    height = (1 - gone) * 2.5 + noise.standardize(pdisp) * 0.2 * (1 - gone) + noise.standardize(bdisp) * 0.4 * gone
    d = lerp(dp, db * 0.7, gone)
    s = lerp(sp, sb, gone)
    g = lerp(gp, gb, gone)
    d = d * (1 - 0.3 * lip)[..., None]
    # Damp: the render darkens in tide marks, soot in broad plumes.
    damp = smoothstep(0.45, 0.7, noise.fbm01(ctx.rng("damp"), (W, W), 50, stretch=(1.5, 0.8)))
    d = d * (1 - 0.35 * damp * (1 - gone))[..., None]
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.5, scale=55, coverage=0.45, stretch=(0.7, 2))
    d, s, g, _ = C.streak(ctx.rng("rain"), d, s, g, lip * 0.6, amount=0.5, colour="#1A1816", length=0.985,
                          density=0.6)
    d = C.light(d, height, strength=2.5, amount=0.7)
    return ctx.out(d, s, g, procedural="plaster loss mask, broken lip, damp, soot")


@texture("roof_slate", "masonry", tile=1.0)
def roof_slate(ctx):
    """Dark slate roof: eight courses per metre (image top = ridge), slates of uneven width,
    each course's ragged bottom edge shadowing the one below; a few cracked or slipped."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    rng = ctx.rng("bond")
    bid, dist, vin, n = coursed(W, [64] * 8, rng, 56, 96)
    rock = ctx.load("Rock064", "diffuse")
    rdisp = ctx.load("Rock064", "displacement")
    alb = per_block(rock, bid, n, ctx.rng("s"))
    hd = per_block(rdisp, bid, n, ctx.rng("s"))
    tone = ctx.rng("tone").random(n).astype(np.float32)[bid]
    # Ragged bottom edge: the slate's lower boundary wobbles; above it the slate thickens.
    rag = noise.fbm(ctx.rng("rag"), (W, W), 3, octaves=2, stretch=(1, 0.2)) * 0.05
    vv = vin + rag
    # Slates are laid over the course below: brighter at the top of the slate (fresh),
    # a dark shadow under the bottom edge of the course above (i.e. at the top of this one).
    shadow = smoothstep(0.25, 0.0, vv)
    height = vv * 1.5 + noise.standardize(hd) * 0.3
    side = smoothstep(3.0, 1.0, dist) * (vin > 0.05)
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.7, np.float32), ramp="slate",
                                    contrast=1.2, desat=0.95, dielectric_spec=0.1)
    d = d * (0.75 + 0.5 * tone)[..., None]
    d = d * (1 - 0.7 * shadow)[..., None] * (1 - 0.8 * side)[..., None]
    broken = (ctx.rng("br").random(n).astype(np.float32)[bid] > 0.95)
    d = lerp(d, d * 0.3, broken * 0.8)
    # Wet sheen on the exposed slate, lichen patches.
    g = np.full((W, W), 0.55, np.float32) * (1 - shadow)
    s = np.full((W, W), 0.18, np.float32) * (1 - shadow) * (1 - side)
    lich = smoothstep(0.7, 0.85, noise.fbm01(ctx.rng("lich"), (W, W), 8, octaves=4))
    d = lerp(d, hexc("#3A3C30"), lich * 0.5)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.4, scale=50, coverage=0.45)
    return ctx.out(d, s, g, procedural="slate courses, ragged edges, lichen, soot")
