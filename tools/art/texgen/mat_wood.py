"""Wood (GDD §27: dark, worn, rough grain). Boards are laid out explicitly - widths, gaps,
nails, straps - and each board samples the wood photo at its own offset and tone, so no two
neighbours match (the tell of a single photo tiled)."""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from . import common as C
from . import convert, core, noise
from .core import hexc, lerp, saturate, smoothstep
from .mat_metal import rivet_field, band
from .reg import texture


def boards(widths, W):
    """Per-pixel board index and position across its board (0..1) for boards laid side by side
    across `W` pixels. widths must sum to W so the layout tiles."""
    assert sum(widths) == W, (sum(widths), W)
    edges = np.cumsum([0] + list(widths))
    idx = np.zeros(W, np.int32)
    across = np.zeros(W, np.float32)
    for i in range(len(widths)):
        a, b = edges[i], edges[i + 1]
        idx[a:b] = i
        across[a:b] = (np.arange(a, b) - a + 0.5) / (b - a)
    return idx, across, edges


def board_sample(photo, idx_line, rng, H, W, along_axis=0):
    """Sample a (grain-along-axis-0) photo per board with random offsets: board i at pixel
    (y, x) reads photo[(y + oy_i) % ph, (x + ox_i) % pw]. Returns an array of H x W (x C)."""
    ph, pw = photo.shape[:2]
    n = int(idx_line.max()) + 1
    oy = rng.integers(0, ph, n)
    ox = rng.integers(0, pw, n)
    ys, xs = np.mgrid[0:H, 0:W]
    b = idx_line[None, :].repeat(H, 0) if along_axis == 0 else idx_line[:, None].repeat(W, 1)
    return photo[(ys + oy[b]) % ph, (xs + ox[b]) % pw]


def wood_maps(ctx, H, W, rot=1, name="Wood095"):
    """The wood photo with grain running along axis 0 (down the image), linear colour +
    displacement, sized so the grain is chunky (a 1 m tile reads ~1 m of the scan)."""
    alb = ctx.load(name, "diffuse", shape=(H, W), rot90=rot, crop=None)
    disp = ctx.load(name, "displacement", shape=(H, W), rot90=rot)
    return alb, disp


def plank_face(ctx, widths, ramp, gap=5, tone_var=0.35, grain_boost=1.0, horizontal=False, photo="Wood095",
               rot=1, contrast=1.3):
    """Shared board builder. Boards run down v (or across u if horizontal). Returns diffuse,
    spec, gloss, height, board index map, across (0..1 within a board)."""
    W = ctx.W
    rng = ctx.rng("boards")
    alb, disp = wood_maps(ctx, W, W, rot=rot if not horizontal else 0, name=photo)
    if horizontal:
        # Photo grain already runs across u; sample by rows.
        idx_l, across_l, edges = boards(widths, W)
        alb_s = board_sample(alb, idx_l, rng, W, W, along_axis=1)
        rng2 = ctx.rng("boards")
        disp_s = board_sample(disp, idx_l, rng2, W, W, along_axis=1)
        idx = idx_l[:, None].repeat(W, 1)
        across = across_l[:, None].repeat(W, 1)
    else:
        idx_l, across_l, edges = boards(widths, W)
        alb_s = board_sample(alb, idx_l, rng, W, W)
        rng2 = ctx.rng("boards")
        disp_s = board_sample(disp, idx_l, rng2, W, W)
        idx = idx_l[None, :].repeat(W, 0)
        across = across_l[None, :].repeat(W, 0)
    tone = ctx.rng("tone").random(len(widths)).astype(np.float32)
    # Grain: the photo's displacement, sharpened (weathered wood's grain stands proud).
    grain = noise.standardize(disp_s) * grain_boost
    d, s, g = convert.pbr_to_legacy(alb_s, height=grain, rough=np.full((W, W), 0.85, np.float32),
                                    ramp=ramp, contrast=contrast, cav_amt=0.45, dielectric_spec=0.05, desat=0.85)
    d = d * (1 - tone_var / 2 + tone_var * tone[idx])[..., None]
    # Rounded board edges and the dark gap between boards.
    px_w = np.array(widths, np.float32)[idx]
    dist_edge = np.minimum(across, 1 - across) * px_w
    bevel = smoothstep(0, 8, dist_edge)
    gapm = smoothstep(gap * 0.5 + 1, gap * 0.5 - 0.5, dist_edge)
    height = grain * 0.35 + bevel * 2.0 - gapm * 2.0
    d = d * (1 - 0.92 * gapm)[..., None]
    s = s * (1 - gapm)
    return d, s, g, height, idx, across


@texture("wood_siding", "wood", tile=1.0)
def wood_siding(ctx):
    """Boxcar siding: vertical tongue-and-groove planks, dark brown, faint iron straps."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, height, idx, across = plank_face(ctx, [86, 80, 90, 84, 88, 84], "wood", gap=5)
    # One butt joint in one board, so the eye can't find a full-length repeat.
    butt = (idx == 2) & (np.abs(ys - 330) < 2.5)
    d = d * (1 - 0.85 * butt)[..., None]
    height = height - 2 * butt
    # Faint iron strap across the planks at v=0 (wraps), bolted through each plank.
    strap = band(ys, 0, 20, 2, period=W)
    bolts = [(e + w / 2, 0) for e, w in zip(np.cumsum([0, 86, 80, 90, 84, 88]), [86, 80, 90, 84, 88, 84])]
    bh = rivet_field(W, W, bolts, 8)
    iron = core.apply_ramp(saturate(0.3 + 0.3 * noise.fbm01(ctx.rng("iron"), (W, W), 6)), "black_paint")
    d, s, g = C.reveal(d, s, g, strap * 0.75, iron, 0.2, 0.4)
    height = height + strap * 1.5 + bh * 1.5
    rust, _ = C.rust_layer(ctx, (W, W))
    d, s, g, _ = C.streak(ctx.rng("rs"), d, s, g, strap * 0.5 + bh, amount=0.55, colour="#3A2014", length=0.975)
    # Weathering: grain opened and lighter where the rain hits, dirt in the grain.
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.45, scale=50, coverage=0.45, stretch=(0.6, 1.6))
    d = C.light(d, height, strength=3, amount=0.6)
    return ctx.out(d, s, g, procedural="plank layout, per-board offsets, iron strap, soot")


@texture("wood_sleeper", "wood", tile=1.0)
def wood_sleeper(ctx):
    """Creosoted railway tie (top face): near-black oily wood, grain along u, deep checking
    cracks, creosote sheen in patches, ballast dust on the dry parts."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    alb = ctx.load("Wood095", "diffuse", shape=(W, W))
    disp = ctx.load("Wood095", "displacement", shape=(W, W))
    asph = ctx.load("Asphalt033", "diffuse")
    alb = alb * (0.6 + 0.8 * noise.normalize(core.lum(asph)))[..., None]
    grain = noise.standardize(disp)
    # Checking: long cracks along the grain.
    # Checking: a few long, open cracks along the grain (zero-crossings of one smooth, very
    # stretched noise octave), opening and closing along their length.
    rid = np.abs(noise.fbm(ctx.rng("chk"), (W, W), 10, octaves=1, stretch=(12, 0.5)))
    cracks = smoothstep(0.3, 0.05, rid) * smoothstep(0.35, 0.6, noise.fbm01(ctx.rng("cm"), (W, W), 30, stretch=(3, 0.5)))
    # Grain lines: the tie's annual rings seen on the sawn top face.
    rings = noise.fbm01(ctx.rng("rings"), (W, W), 2.5, octaves=2, stretch=(16, 0.6))
    grain = grain * 0.6 + noise.standardize(rings) * 0.8
    alb = alb * (0.7 + 0.6 * rings)[..., None]
    height = grain * 0.5 - cracks * 4
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.8, np.float32), ramp="sleeper",
                                    contrast=1.2, cav_amt=0.5, dielectric_spec=0.05)
    d = d * (1 - 0.9 * cracks)[..., None]
    # Raised grain: the dry top face weathers lighter on the ridges of the grain.
    d = d * (1 + 0.5 * saturate(noise.convexity_from_height(grain, 2)))[..., None]
    creo = smoothstep(0.5, 0.75, noise.fbm01(ctx.rng("creo"), (W, W), 30, octaves=5, stretch=(2, 0.7)))
    d = lerp(d, d * 0.55 + hexc("#0E0A07") * 0.3, creo * 0.8)
    s = lerp(s, 0.25, creo)
    g = lerp(g, 0.6, creo)
    dust = smoothstep(0.55, 0.8, noise.fbm01(ctx.rng("dust"), (W, W), 20, octaves=5)) * (1 - creo)
    d = lerp(d, hexc("#3A3630") * (0.7 + 0.6 * noise.white(ctx.rng("dw"), (W, W)))[..., None], dust * 0.5)
    d = C.light(d, height, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="checking cracks, creosote sheen, ballast dust")


@texture("wood_grey", "wood", tile=1.0)
def wood_grey(ctx):
    """Silvered weathered boards (fence, shed): horizontal boards, raised grain, nail pairs at a
    post, rot darkening at the board edges."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    widths = [100, 104, 98, 106, 104]
    d, s, g, height, idx, across = plank_face(ctx, widths, "wood_grey", gap=7, grain_boost=1.8, horizontal=True,
                                              tone_var=0.3, contrast=1.5)
    edges = np.cumsum([0] + widths[:-1])
    nails = []
    for e, w in zip(edges, widths):
        nails += [(W * 0.25 - 10, e + w * 0.3), (W * 0.25 + 10, e + w * 0.7)]
    nh = rivet_field(W, W, nails, 5)
    d, s, g = C.reveal(d, s, g, saturate(nh * 3), "#2A2624", 0.2, 0.3)
    d, s, g, _ = C.streak(ctx.rng("nr"), d, s, g, saturate(nh * 3), amount=0.6, colour="#3A2418", length=0.97)
    dist = np.minimum(across, 1 - across)
    rot = smoothstep(0.25, 0.0, dist) * smoothstep(0.4, 0.8, noise.fbm01(ctx.rng("rot"), (W, W), 20))
    d = lerp(d, d * 0.45, rot)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=60, coverage=0.4, colour="#1E1E1C")
    height = height + nh * 2
    d = C.light(d, height, strength=3, amount=0.65)
    return ctx.out(d, s, g, procedural="board layout, raised grain, nails, edge rot")


def _stencil_font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSansCondensed-Bold.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            continue
    return ImageFont.load_default(size=size)


def stencil_mask(W, lines, sizes, y0s, rng):
    """Stencilled lettering as a 0..1 mask, with the stencil bridges cut through each glyph
    (horizontal gaps) and a patchy spray so it reads as painted through a card."""
    im = Image.new("L", (W, W), 0)
    dr = ImageDraw.Draw(im)
    for text, size, y in zip(lines, sizes, y0s):
        f = _stencil_font(size)
        tw = dr.textlength(text, font=f)
        dr.text(((W - tw) / 2, y), text, fill=255, font=f)
    m = np.asarray(im, np.float32) / 255
    # Stencil bridges: thin horizontal cuts at the glyph middles.
    for size, y in zip(sizes, y0s):
        cy = int(y + size * 0.62)
        m[cy - 2:cy + 2] *= 0.0
    return m


@texture("wood_crate", "wood", tile=1.0)
def wood_crate(ctx):
    """A crate side (not tiling): four boards inside a frame, a diagonal brace, and a faded
    stencil. The frame reads the crate as a crate at a glance (§26)."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    d, s, g, height, idx, across = plank_face(ctx, [128, 128, 128, 128], "wood", gap=6, horizontal=True,
                                              tone_var=0.4)
    # Frame boards: a second set of planks laid over the edges.
    fr = 56
    frame = ((xs < fr) | (xs >= W - fr) | (ys < fr) | (ys >= W - fr)).astype(np.float32)
    fd, fs, fg, fh, _, _ = plank_face(ctx, [fr, W - 2 * fr, fr], "wood", gap=6, tone_var=0.3)
    # Diagonal brace between the frame's inner corners.
    diag = np.abs((xs - fr) - (ys - fr)) / np.sqrt(2)
    brace = (diag < 30) & (xs >= fr) & (xs < W - fr) & (ys >= fr) & (ys < W - fr)
    brace = brace.astype(np.float32)
    over = saturate(frame + brace)
    d = lerp(d, fd * 1.1, over)
    s = lerp(s, fs, over)
    g = lerp(g, fg, over)
    # Edges of the overlaid boards cast a shadow onto the boards beneath.
    h2 = height + over * 4 + fh * over * 0.3
    shadow = saturate(noise.blur(over, 3) - over) * 2.0
    d = d * (1 - 0.6 * shadow)[..., None]
    # Nails at the frame corners and brace ends.
    nails = [(fr / 2, fr / 2), (W - fr / 2, fr / 2), (fr / 2, W - fr / 2), (W - fr / 2, W - fr / 2),
             (W / 2, fr / 2), (W / 2, W - fr / 2), (fr / 2, W / 2), (W - fr / 2, W / 2)]
    nails = [(x + dx, y + dy) for x, y in nails for dx, dy in ((-9, -6), (9, 6))]
    nh = rivet_field(W, W, nails, 4.5)
    d, s, g = C.reveal(d, s, g, saturate(nh * 3), "#2A2624", 0.3, 0.4)
    d, s, g, _ = C.streak(ctx.rng("nr"), d, s, g, saturate(nh * 3), amount=0.5, colour="#3A2418", length=0.96)
    # Faded stencil (off-white paint, mostly worn off; only on the inner boards).
    sm = stencil_mask(W, ["D.T.R. CO", "No 14", "THIS SIDE UP"], [58, 70, 30], [118, 196, 320], ctx.rng("st"))
    wear = noise.fbm01(ctx.rng("sw"), (W, W), 6, octaves=4)
    sm = sm * smoothstep(0.25, 0.6, wear) * (1 - over) * (1 - brace)
    d = lerp(d, hexc("#8A8272") * (0.6 + 0.4 * wear)[..., None], sm * 0.5)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.4, scale=50, coverage=0.4)
    d = C.light(d, h2 + nh * 2, strength=3, amount=0.6)
    tex = ctx.out(d, s, g, tiling=False, procedural="crate boards, frame, brace, nails, faded stencil")
    return tex


@texture("wood_floor", "wood", tile=1.0)
def wood_floor(ctx):
    """Car floor boards along v (the car's length): narrow boards with staggered butt joints and
    nail pairs, dark and scuffed; a worn lane where boots go (polished, lighter), dirt and grit
    swept into the gaps and the edges. WoodFloor051 tints the boards' tone."""
    W = ctx.W
    xs, ys = noise.grid(W, W)
    widths = [66, 62, 64, 60, 68, 62, 64, 66]
    d, s, g, height, idx, across = plank_face(ctx, widths, "wood", gap=4, tone_var=0.4, contrast=1.2)
    floor = ctx.load("WoodFloor051", "diffuse", rot90=1)
    d = d * (0.75 + 0.5 * noise.normalize(noise.blur(core.lum(floor), 2)))[..., None]
    # Butt joints: one per board, staggered, with a nail pair each side.
    rng = ctx.rng("butts")
    edges = np.cumsum([0] + widths)
    nails = []
    for i, w in enumerate(widths):
        y = rng.uniform(0, W)
        m = (idx == i) & (np.minimum(np.abs(ys - y), W - np.abs(ys - y)) < 1.5)
        d = d * (1 - 0.9 * m)[..., None]
        height = height - 2 * m
        cx = edges[i] + w / 2
        nails += [(cx - w * 0.25, (y - 9) % W), (cx + w * 0.25, (y - 9) % W),
                  (cx - w * 0.25, (y + 9) % W), (cx + w * 0.25, (y + 9) % W)]
    nh = rivet_field(W, W, nails, 3.5)
    d, s, g = C.reveal(d, s, g, saturate(nh * 3), "#1E1C1A", 0.3, 0.5)
    # Worn lane: a broad band, wavering, polished (a little gloss) and lighter.
    lane_x = 0.5 + 0.08 * noise.fbm(ctx.rng("lane"), (W, W), 60, octaves=2, stretch=(0.3, 3))
    lane = smoothstep(0.3, 0.1, np.abs(xs / W - lane_x))
    scuff = noise.fbm01(ctx.rng("sc"), (W, W), 4, octaves=3, stretch=(1.5, 0.6))
    d = lerp(d, d * 1.4, lane * (0.4 + 0.6 * scuff) * 0.7)
    g = lerp(g, 0.45, lane * 0.6)
    s = lerp(s, 0.12, lane * 0.6)
    dirt = (1 - lane) * smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("d"), (W, W), 12, octaves=5, gain=0.65))
    d = lerp(d, d * 0.4 + hexc("#16120E") * 0.4, dirt * 0.7)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=40, coverage=0.4)
    d = C.light(d, height + nh * 2, strength=3, amount=0.5)
    return ctx.out(d, s, g, procedural="board layout, butt joints, nails, worn lane, dirt")
