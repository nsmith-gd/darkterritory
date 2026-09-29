"""Cloth and crew (GDD §29: "practical, rail-working, soot-covered, bundled against cold,
slightly anonymous. Ordinary people in bad circumstances"). Heavy coats, caps, helmets,
gloves, harnesses, belts, boots. Tiling fabrics at 0.5 m (512 px/m: characters are seen up
close in the cab), plus the crew atlas that dresses the character mesh."""
from __future__ import annotations

import numpy as np
from PIL import Image, ImageDraw, ImageFont

from . import common as C
from . import convert, core, draw, noise
from .core import hexc, lerp, saturate, smoothstep
from .reg import texture


def weave(W, pitch=4.0):
    """Plain-weave height: alternating over/under threads (periodic: pitch divides W)."""
    xs, ys = noise.grid(W, W)
    k = 2 * np.pi / pitch
    warp = np.sin(xs * k) * 0.5 + 0.5
    weft = np.sin(ys * k) * 0.5 + 0.5
    over = np.sign(np.sin(xs * k / 2) * np.sin(ys * k / 2))
    return (warp * (over > 0) + weft * (over <= 0)).astype(np.float32)


def creases(rng, W, n=18, key_len=(80, 260)):
    """Fabric creases: long soft ridges (folds) as strokes, blurred."""
    lines, widths = [], []
    for _ in range(n):
        x, y = rng.random(2) * W
        lines.append(draw.wander_line(rng, x, y, rng.normal(np.pi / 2, 0.5), rng.uniform(*key_len), 8, 0.12))
        widths.append(rng.uniform(3, 7))
    return noise.blur(draw.strokes((W, W), lines, widths), 3.0)


def oilskin_maps(ctx, W=None):
    W = W or ctx.W
    canvas = ctx.load("fabric_canvas", "diffuse", size=W)
    wv = weave(W, 4.0)
    cr = creases(ctx.rng("creases"), W)
    height = wv * 0.4 + cr * 6 + noise.fbm(ctx.rng("lump"), (W, W), 30, octaves=3) * 0.8
    alb = canvas * (0.8 + 0.4 * wv)[..., None]
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.55, np.float32), ramp="canvas",
                                    contrast=1.1, desat=0.9, dielectric_spec=0.12, cav_amt=0.4)
    # Waxed: the proofing wears pale and dry on the fold ridges and stays dark and glossy between.
    ridge = noise.convexity_from_height(noise.blur(height, 2), 6)
    d = lerp(d, d * 1.5 + hexc("#2A2A20") * 0.2, saturate(ridge * 1.3) * 0.6)
    g = lerp(np.full((W, W), 0.5, np.float32), 0.2, saturate(ridge * 1.3))
    s = lerp(np.full((W, W), 0.18, np.float32), 0.06, saturate(ridge * 1.3))
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.4, scale=40, coverage=0.45)
    d = C.light(d, height, strength=1.0, amount=0.5)
    return d, s, g


def leather_maps(ctx, W=None):
    W = W or ctx.W
    # The generated leather set has big black blotches (camouflage at this scale); only its
    # average colour is taken. The surface is built: pebble grain, creases, scuffs.
    tone = ctx.load("leather_brown", "diffuse", size=W).reshape(-1, 3).mean(0)
    # Pebble grain: fine and tight (a few texels), not a crackle.
    pebble = noise.fbm01(ctx.rng("pebble"), (W, W), 1.2, octaves=2)
    cr = creases(ctx.rng("cr"), W, 10, (40, 140))
    height = pebble * 0.5 + cr * 5 + noise.fbm(ctx.rng("l"), (W, W), 20, octaves=3) * 0.5
    mott = noise.fbm01(ctx.rng("mott"), (W, W), 12, octaves=5, gain=0.6)
    alb = tone[None, None, :] * (0.7 + 0.3 * pebble + 0.4 * mott)[..., None]
    d, s, g = convert.pbr_to_legacy(alb, height=height, rough=np.full((W, W), 0.55, np.float32), ramp="leather",
                                    contrast=1.2, desat=0.8, dielectric_spec=0.15, cav_amt=0.5)
    scuff = smoothstep(0.6, 0.85, noise.fbm01(ctx.rng("sc"), (W, W), 6, octaves=4)) + saturate(
        noise.convexity_from_height(noise.blur(height, 2), 5))
    d = lerp(d, d * 1.6 + hexc("#3A2A1E") * 0.2, saturate(scuff) * 0.5)
    s = s * (1 - 0.4 * saturate(scuff))
    g = np.full((W, W), 0.45, np.float32) * (1 - 0.5 * saturate(scuff))
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.35, scale=35, coverage=0.4)
    d = C.light(d, height, strength=1.2, amount=0.5)
    return d, s, g


def wool_maps(ctx, W=None, ramp="wool"):
    """Stockinette knit: columns of V stitches (chunky: 16 px stitches at work res)."""
    W = W or ctx.W
    xs, ys = noise.grid(W, W)
    sw, sh = W / 32, W / 40
    u = (xs % sw) / sw
    v = (ys % sh) / sh
    # Each stitch is two slanted lobes meeting at the column centre, like a V.
    lobe = 1 - np.abs((np.abs(u - 0.5) * 2) - (1 - v)) * 1.6
    stitch = saturate(lobe) * smoothstep(0, 0.2, v) * smoothstep(1.0, 0.8, v) + saturate(lobe) * 0.3
    # Fibre fuzz from the canvas scan's fine structure plus a little noise.
    canvas = core.lum(ctx.load("fabric_canvas", "diffuse", size=W))
    fuzz = noise.standardize(canvas - noise.blur(canvas, 2)) * 0.6 + noise.fbm(ctx.rng("fuzz"), (W, W), 1.2, octaves=2) * 0.6
    height = stitch * 2 + fuzz * 0.3
    t = saturate(0.3 + 0.45 * stitch + 0.1 * fuzz + 0.2 * (noise.fbm01(ctx.rng("dye"), (W, W), 30) - 0.5))
    d = core.apply_ramp(t, ramp)
    pill = smoothstep(0.85, 0.95, noise.white(ctx.rng("pill"), (W, W))) * smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("pm"), (W, W), 20))
    d = lerp(d, d * 1.5, pill)
    s = np.full((W, W), 0.04, np.float32)
    g = np.full((W, W), 0.1, np.float32)
    d, s, g, _ = C.soot(ctx.rng("soot"), d, s, g, amount=0.3, scale=35, coverage=0.4)
    d = C.light(d, height, strength=1.0, amount=0.5)
    return d, s, g


def skin_maps(ctx, W=None):
    W = W or ctx.W
    pores = noise.fbm(ctx.rng("pores"), (W, W), 0.8, octaves=2)
    blotch = noise.fbm01(ctx.rng("blotch"), (W, W), 25, octaves=5)
    t = saturate(0.7 + 0.06 * pores + 0.1 * (blotch - 0.5))
    d = core.apply_ramp(t, "skin")
    # Tired: blotchy redness and a sallow cast, not a healthy glow.
    red = smoothstep(0.55, 0.85, noise.fbm01(ctx.rng("red"), (W, W), 15, octaves=4))
    d = C.tint(d, red, "#8A5040", 0.15)
    d = C.tint(d, 1 - red, "#7A7058", 0.35)
    grime = smoothstep(0.6, 0.9, noise.fbm01(ctx.rng("grime"), (W, W), 6, octaves=5, gain=0.7))
    d = lerp(d, d * 0.5 + hexc("#1A1612") * 0.3, grime * 0.5)
    s = np.full((W, W), 0.1, np.float32) * (1 - 0.6 * grime)
    g = np.full((W, W), 0.35, np.float32) * (1 + 0.3 * noise.fbm01(ctx.rng("oily"), (W, W), 20))
    d = C.light(d, pores, strength=1.5, amount=0.3)
    return d, s, g


@texture("coat_oilskin", "cloth", tile=0.5)
def coat_oilskin(ctx):
    """Waxed canvas coat: dark olive-brown, weave just visible, folds worn pale where the wax is
    rubbed off, glossy in the hollows."""
    d, s, g = oilskin_maps(ctx)
    return ctx.out(d, s, g, procedural="plain weave, creases, wax wear on ridges, soot")


@texture("leather", "cloth", tile=0.5)
def leather(ctx):
    d, s, g = leather_maps(ctx)
    return ctx.out(d, s, g, procedural="pebble grain (voronoi), creases, scuffs")


@texture("wool", "cloth", tile=0.5)
def wool(ctx):
    d, s, g = wool_maps(ctx)
    return ctx.out(d, s, g, procedural="stockinette knit, fuzz, pilling")


@texture("skin", "flesh", tile=0.5)
def skin(ctx):
    """Pale, tired, dirty skin - neutral, not stylised: pores, blotchy redness, sallow cast,
    grime worked into it."""
    d, s, g = skin_maps(ctx)
    return ctx.out(d, s, g, procedural="pores, blotches, grime", grain=0.03)


# --- crew atlas --------------------------------------------------------------------------------

CELLS = ["face_front", "face_side", "cap", "helmet",
         "gloves", "belt_buckle", "harness", "boot",
         "lantern_glass", "coat_front", "coat_back", "trouser",
         "satchel", "badge", "hair", "scarf"]


def _font(size, bold=True):
    for p in (("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",) if bold else ()) + (
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",):
        try:
            return ImageFont.truetype(p, size)
        except OSError:
            pass
    return ImageFont.load_default(size=size)


def _mask(S, fn):
    """Draw with PIL into an SxS mask (fn(draw)), 0..1."""
    im = Image.new("L", (S, S), 0)
    fn(ImageDraw.Draw(im))
    return np.asarray(im, np.float32) / 255


@texture("crew_atlas", "cloth", tile=None)
def crew_atlas(ctx):
    """4x4 atlas of 64 px cells (built at 128) for the crew mesh; cell layout in index.json."""
    S = 128
    A = 4 * S
    rng = ctx.rng("atlas")
    oil = oilskin_maps(ctx, S)
    lea = leather_maps(ctx, S)
    woo = wool_maps(ctx, S)
    woo_scarf = wool_maps(ctx, S, ramp="black_paint")
    ski = skin_maps(ctx, S)
    xs, ys = noise.grid(S, S)
    D = np.zeros((A, A, 3), np.float32)
    Sp = np.zeros((A, A), np.float32)
    G = np.zeros((A, A), np.float32)
    E = np.zeros((A, A), np.float32)
    brass = core.apply_ramp(saturate(0.45 + 0.3 * noise.fbm01(ctx.rng("br"), (S, S), 4)), "brass") * 0.45
    iron = core.apply_ramp(saturate(0.3 + 0.3 * noise.fbm01(ctx.rng("ir"), (S, S), 4)), "iron") * 0.45

    def put(i, d, s, g, e=None):
        cy, cx = divmod(i, 4)
        D[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = d
        Sp[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = s
        G[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = g
        if e is not None:
            E[cy * S:(cy + 1) * S, cx * S:(cx + 1) * S] = e

    def shade(d, m, amt):
        return d * (1 - amt * m)[..., None]

    # face_front: unwrapped front of the head. Hair at the top, brow shadow, dark eye sockets,
    # nose shadow, mouth line, stubble over the jaw, grime. Lit from above.
    d, s, g = [x.copy() for x in ski]
    hair = smoothstep(26, 18, ys + 4 * np.sin(xs * 0.3))
    sockets = _mask(S, lambda dr: [dr.ellipse([30, 50, 56, 66], fill=255), dr.ellipse([72, 50, 98, 66], fill=255)])
    sockets = noise.blur(sockets, 3)
    eyes = _mask(S, lambda dr: [dr.ellipse([38, 55, 49, 62], fill=255), dr.ellipse([79, 55, 90, 62], fill=255)])
    brows = _mask(S, lambda dr: [dr.line([29, 47, 56, 45], fill=255, width=4), dr.line([72, 45, 99, 47], fill=255, width=4)])
    nose = noise.blur(_mask(S, lambda dr: dr.polygon([(64, 58), (58, 84), (70, 86)], fill=255)), 2)
    nose_sh = noise.blur(_mask(S, lambda dr: dr.line([(68, 60), (72, 86)], fill=255, width=4)), 2)
    mouth = noise.blur(_mask(S, lambda dr: dr.line([(50, 100), (64, 102), (78, 100)], fill=255, width=3)), 1)
    jaw = smoothstep(84, 104, ys) * (1 - smoothstep(118, 128, ys) * 0)
    stubble = jaw * smoothstep(0.35, 0.6, noise.white(ctx.rng("stub"), (S, S)))
    stubble = stubble * smoothstep(0, 18, np.minimum(xs, S - xs)) * (1 - mouth)
    d = shade(d, sockets, 0.75)
    d = lerp(d, hexc("#0E0C0B"), eyes * 0.9)
    d = lerp(d, hexc("#2A1E18"), brows * 0.85)
    d = d * (1 + 0.2 * nose)[..., None]
    d = shade(d, nose_sh, 0.35)
    d = lerp(d, hexc("#2A1816"), mouth * 0.85)
    d = lerp(d, hexc("#1E1814"), stubble * 0.75)
    d = lerp(d, hexc("#16120F"), hair)
    d = d * (1 - 0.25 * smoothstep(0, 64, np.abs(xs - 64)))[..., None]   # cheeks turn away
    # Soot around the eyes and in the creases: a stoker's face.
    soot = noise.blur(sockets, 6) * 0.5 + smoothstep(0.6, 0.85, noise.fbm01(ctx.rng("fs"), (S, S), 10)) * 0.5
    d = lerp(d, d * 0.5, soot * 0.6)
    put(0, d, s * (1 - hair), g)

    # face_side: ear, hair over the back, stubble along the jaw.
    d, s, g = [x.copy() for x in ski]
    hair = smoothstep(0, 10, 60 - xs * 0.9 - ys * 0.2 + 30) * 0 + smoothstep(28, 18, ys + 0.2 * (S - xs)) + smoothstep(80, 96, xs) * smoothstep(100, 70, ys)
    ear = noise.blur(_mask(S, lambda dr: dr.ellipse([60, 44, 80, 78], fill=255)), 1.5)
    ear_in = noise.blur(_mask(S, lambda dr: dr.ellipse([65, 50, 75, 72], fill=255)), 2)
    d = d * (1 + 0.1 * ear)[..., None]
    d = shade(d, ear_in, 0.4)
    d = shade(d, noise.blur(ear, 3) - ear * 0.8, 0.5)
    jaw = smoothstep(80, 100, ys) * smoothstep(60, 20, xs)
    d = lerp(d, hexc("#241C18"), jaw * smoothstep(0.35, 0.6, noise.white(ctx.rng("st2"), (S, S))) * 0.55)
    d = lerp(d, hexc("#16120F"), saturate(hair))
    put(1, d, s * (1 - saturate(hair)), g)

    # cap: wool crown with stitched panels, a stiff peak (brim) along the bottom, sweat stain.
    d, s, g = [x.copy() for x in woo]
    d = d * 0.9
    panels = sum(np.exp(-((xs - c) / 1.5) ** 2) for c in (32, 64, 96))
    d = shade(d, panels, 0.5)
    brim = smoothstep(96, 100, ys)
    d = lerp(d, oil[0] * 0.7, brim)
    d = shade(d, np.exp(-((ys - 98) / 2) ** 2), 0.6)
    stain = smoothstep(84, 94, ys) * (1 - brim) * smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("sw"), (S, S), 8))
    d = lerp(d, d * 0.55, stain)
    put(2, d, lerp(s, oil[1], brim), lerp(g, oil[2], brim))

    # helmet: dull olive painted steel, a rolled rim, chips to iron, leather chinstrap stub.
    paint = core.apply_ramp(saturate(0.4 + 0.3 * noise.fbm01(ctx.rng("hp"), (S, S), 10)), "olive_paint")
    chip = convert.chips(ctx.rng("hc"), (S, S), smoothstep(110, 124, ys), amount=0.12, scale=4)
    d = lerp(paint, iron, chip)
    rim = np.exp(-((ys - 118) / 3) ** 2)
    d = d * (1 + 0.4 * rim - 0.3 * np.exp(-((ys - 124) / 2) ** 2))[..., None]
    rivets = sum(np.exp(-(((xs - cx) ** 2 + (ys - 104) ** 2) / 8)) for cx in (20, 64, 108))
    d = d * (1 + 0.6 * rivets)[..., None]
    strap = (np.abs(xs - 64) < 8) & (ys > 108)
    d = lerp(d, lea[0], strap * 1.0)
    d, s2, g2, _ = C.soot(ctx.rng("hs"), d, np.full((S, S), 0.12, np.float32), np.full((S, S), 0.3, np.float32),
                          amount=0.4, scale=20)
    put(3, d, lerp(s2, 0.4, chip), lerp(g2, 0.55, chip))

    # gloves: leather, finger seams running down, palm darkened with grease and soot.
    d, s, g = [x.copy() for x in lea]
    seams = sum(np.exp(-((xs - c) / 1.2) ** 2) for c in (32, 64, 96)) * smoothstep(40, 30, ys)
    d = shade(d, seams, 0.7)
    cuff = smoothstep(96, 100, ys)
    d = lerp(d, oil[0], cuff)
    palm = smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("gp"), (S, S), 12)) * (1 - cuff)
    d = lerp(d, d * 0.35, palm * 0.8)
    put(4, d, s, lerp(g, 0.6, palm))

    # belt_buckle: a leather belt band across the middle, brass frame buckle, punched holes.
    d = oil[0] * 0.8
    s, g = oil[1].copy(), oil[2].copy()
    band = (np.abs(ys - 64) < 22)
    d = lerp(d, lea[0], band * 1.0)
    d = shade(d, np.exp(-((np.abs(ys - 64) - 22) / 1.5) ** 2), 0.6)
    holes = sum(np.exp(-(((xs - cx) ** 2 + (ys - 64) ** 2) / 6)) for cx in (92, 104, 116))
    d = shade(d, holes, 0.9)
    frame = _mask(S, lambda dr: dr.rectangle([34, 36, 70, 92], outline=255, width=7))
    tongue = _mask(S, lambda dr: dr.line([(40, 64), (78, 64)], fill=255, width=4))
    buckle = saturate(frame + tongue)
    d = lerp(d, brass, buckle)
    d = d * (1 + 0.5 * noise.convexity_from_height(noise.blur(buckle, 1.5), 3))[..., None]
    put(5, d, lerp(s, 0.5, buckle), lerp(g, 0.55, buckle))

    # harness: canvas webbing crossing on the dark coat, a riveted iron D-ring at the cross.
    d = oil[0] * 0.8
    s, g = oil[1].copy(), oil[2].copy()
    web = _mask(S, lambda dr: [dr.line([(0, 10), (128, 118)], fill=255, width=26), dr.line([(128, 10), (0, 118)], fill=255, width=26)])
    webt = core.apply_ramp(saturate(0.45 + 0.25 * weave(S, 3.0)), "canvas") * 0.9
    d = lerp(d, webt, web)
    stitch = _mask(S, lambda dr: [dr.line([(0, 2), (128, 110)], fill=255, width=1), dr.line([(0, 18), (128, 126)], fill=255, width=1)])
    d = shade(d, stitch * web, 0.4)
    ring = _mask(S, lambda dr: dr.ellipse([46, 44, 82, 84], outline=255, width=6))
    d = lerp(d, iron * 1.4, ring)
    d = shade(d, noise.blur(web, 2) - web, 1.2)
    put(6, d, lerp(s, 0.45, ring), lerp(g, 0.6, ring))

    # boot: leather upper with eyelets and laces, scuffed toe cap, mud caked at the sole.
    d, s, g = [x.copy() for x in lea]
    d = d * 0.8
    for k, y in enumerate(range(10, 80, 14)):
        d = shade(d, np.exp(-(((xs - 50) ** 2 + (ys - y) ** 2) / 5)), 0.9)
        d = shade(d, np.exp(-(((xs - 78) ** 2 + (ys - y) ** 2) / 5)), 0.9)
    lace = _mask(S, lambda dr: [dr.line([(50, y), (78, y + 14)], fill=255, width=3) for y in range(10, 70, 14)] +
                 [dr.line([(78, y), (50, y + 14)], fill=255, width=3) for y in range(10, 70, 14)])
    d = lerp(d, hexc("#1A1612"), lace * 0.9)
    toe = smoothstep(84, 90, ys)
    d = d * (1 + 0.25 * toe * noise.fbm01(ctx.rng("toe"), (S, S), 4))[..., None]
    mud = smoothstep(104, 116, ys + 8 * noise.fbm(ctx.rng("mud"), (S, S), 6, octaves=2))
    d = lerp(d, hexc("#1E1812") * (0.7 + 0.6 * noise.white(ctx.rng("mw"), (S, S)))[..., None], mud)
    put(7, d, s * (1 - mud), g * (1 - mud * 0.6))

    # lantern_glass: dirty amber glass, brighter at the middle where the flame is; emissive.
    r = np.sqrt((xs - 64) ** 2 + (ys - 70) ** 2)
    glow = saturate(1 - r / 90)
    d = lerp(hexc("#6A3812"), hexc("#E0A050"), glow ** 1.5)
    soot_g = smoothstep(0.45, 0.75, noise.fbm01(ctx.rng("lg"), (S, S), 10)) * smoothstep(60, 0, ys)
    d = lerp(d, d * 0.3, soot_g * 0.8)
    guard = (np.abs(xs - 64) < 3) | (np.abs(ys - 64) < 3)
    d = lerp(d, iron, guard * 1.0)
    put(8, d, np.full((S, S), 0.6, np.float32) * (1 - guard), np.full((S, S), 0.8, np.float32),
        e=saturate(0.4 + 0.6 * glow) * (1 - soot_g * 0.6) * (1 - guard))

    # coat_front: oilskin, placket down the middle with horn buttons, pocket flaps.
    d, s, g = [x.copy() for x in oil]
    placket = np.exp(-((xs - 70) / 1.5) ** 2)
    d = shade(d, placket, 0.6)
    for y in (18, 50, 82, 114):
        btn = np.exp(-(((xs - 60) ** 2 + (ys - y) ** 2) / 14))
        d = lerp(d, hexc("#2A2016"), smoothstep(0.3, 0.5, btn))
        d = d * (1 + 0.5 * np.exp(-(((xs - 58.5) ** 2 + (ys - y + 1.5) ** 2) / 3)))[..., None]
    flap = _mask(S, lambda dr: [dr.rectangle([6, 88, 44, 96], fill=255), dr.rectangle([90, 88, 124, 96], fill=255)])
    d = shade(d, np.roll(flap, 2, 0) - flap, 0.6)
    put(9, d, s, g)

    # coat_back: centre seam, yoke seam across the shoulders, soot heavy across the top.
    d, s, g = [x.copy() for x in oil]
    d = shade(d, np.exp(-((xs - 64) / 1.2) ** 2) * smoothstep(34, 40, ys), 0.6)
    d = shade(d, np.exp(-((ys - 36) / 1.5) ** 2), 0.6)
    d = lerp(d, d * 0.5, smoothstep(40, 0, ys) * 0.6)
    put(10, d, s, g)

    # trouser: dark wool twill, side seam, knees worn pale, mud splashed low.
    d, s, g = [x.copy() for x in woo]
    d = d * 0.85
    twill = (np.sin((xs + ys) * 2 * np.pi / 4) * 0.5 + 0.5)
    d = d * (0.85 + 0.3 * twill)[..., None]
    d = shade(d, np.exp(-((xs - 16) / 1.2) ** 2), 0.6)
    knee = np.exp(-(((xs - 70) ** 2) / 600 + ((ys - 50) ** 2) / 300))
    d = d * (1 + 0.5 * knee * noise.fbm01(ctx.rng("kn"), (S, S), 3))[..., None]
    mud = smoothstep(90, 125, ys) * smoothstep(0.4, 0.7, noise.fbm01(ctx.rng("tm"), (S, S), 5))
    d = lerp(d, hexc("#241C14"), mud * 0.8)
    put(11, d, s, g)

    # satchel: canvas bag, leather-bound flap with a strap and small iron buckle.
    d = core.apply_ramp(saturate(0.4 + 0.25 * weave(S, 3.0) + 0.2 * noise.fbm01(ctx.rng("sa"), (S, S), 10)), "canvas")
    s, g = np.full((S, S), 0.06, np.float32), np.full((S, S), 0.2, np.float32)
    flapm = _mask(S, lambda dr: dr.polygon([(8, 8), (120, 8), (120, 70), (64, 82), (8, 70)], fill=255))
    d = lerp(d, d * 0.8, flapm)
    edge = saturate(noise.blur(flapm, 1.5) * (1 - flapm) * 4 + (flapm - _mask(S, lambda dr: dr.polygon(
        [(14, 8), (114, 8), (114, 66), (64, 76), (14, 66)], fill=255))))
    d = lerp(d, lea[0], saturate(edge))
    strap = (np.abs(xs - 64) < 9) & (ys > 60)
    d = lerp(d, lea[0], strap * 1.0)
    bk = _mask(S, lambda dr: dr.rectangle([52, 88, 76, 104], outline=255, width=3))
    d = lerp(d, iron * 1.3, bk)
    d = shade(d, noise.blur(flapm, 3) * (1 - flapm), 0.7)
    put(12, d, lerp(s, 0.35, bk), g)

    # badge: an oval brass railway badge stamped "D.T.R." and a number, on coat cloth.
    d, s, g = [x.copy() for x in oil]
    ov = _mask(S, lambda dr: dr.ellipse([14, 30, 114, 98], fill=255))
    ovi = _mask(S, lambda dr: dr.ellipse([22, 38, 106, 90], outline=255, width=2))

    def txt(dr):
        f = _font(22)
        tw = dr.textlength("D.T.R.", font=f)
        dr.text(((S - tw) / 2, 42), "D.T.R.", fill=255, font=f)
        f2 = _font(16)
        tw = dr.textlength("No 212", font=f2)
        dr.text(((S - tw) / 2, 66), "No 212", fill=255, font=f2)
    letters = _mask(S, txt)
    b = brass * 1.3
    b = shade(b, letters + ovi, 0.6)
    b = b * (1 + 0.6 * noise.convexity_from_height(noise.blur(ov - letters * 0.5, 1.2), 3))[..., None]
    d = lerp(d, b, ov)
    d = shade(d, saturate(np.roll(ov, 3, 0) - ov), 0.7)
    put(13, d, lerp(s, 0.55, ov), lerp(g, 0.55, ov))

    # hair: dark, greasy, clumped strands running down.
    strands = noise.fbm01(ctx.rng("hair"), (S, S), 1.5, octaves=3, stretch=(0.3, 8))
    clumps = noise.fbm01(ctx.rng("hc2"), (S, S), 8, octaves=2, stretch=(0.5, 3))
    d = core.apply_ramp(saturate(0.2 + 0.5 * strands * clumps + 0.2 * strands), ["#0A0807", "#16110E", "#261D17", "#3A2E24"])
    put(14, d, np.full((S, S), 0.15, np.float32) * strands, np.full((S, S), 0.45, np.float32))

    # scarf: coarse dark knit.
    put(15, *woo_scarf)

    cells = []
    for i, name in enumerate(CELLS):
        cy, cx = divmod(i, 4)
        cells.append({"name": name, "x": cx * 64, "y": cy * 64, "w": 64, "h": 64})
    return ctx.out(D, Sp, G, emissive=E, tiling=False, factor=2, cells=cells,
                   procedural="crew atlas cells drawn over the cloth, leather, wool and skin materials")
