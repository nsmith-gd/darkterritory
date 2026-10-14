"""The blighted trees' layers (ARCHITECTURE §8 note 696, tools/models/recipes/corrupted_trees.py): what hangs off the
Corruption's trees and stretches between their limbs, and what it makes of them. Cards that hang from their root (kept at
v 1, where the wind bends a card from, as it does the boughs: wrack and lichen, dead leaves, the barnacle spruce's
shrouds); the skin webbed between the branches (holed); quartz; and from the director's second pass, raw meat in the
splits, a living membrane that glows from within (its glow the spec map's B), barnacles packed in a crust, and tar
running in drips. Imported last, so the library's other layers keep their places; each new one appended to the index."""
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


@texture("membrane_web", "flesh", tile=1.5)
def membrane_web(ctx):
    """The skin the Corruption grows between a tree's limbs (tiling, a metre; alpha-tested; the director, 13 Oct: "This
    needs to look like skin and flesh more"): skin, not a web. Sickly pale and pink where it's stretched thin and the light
    comes through it, mottled with bruises gone purple and yellow, its pores and fine creases, stretch marks pulled the
    way it's pulled, blue-grey veins under it and red ones standing proud of it, wet in patches; torn through here and
    there (a sixth of it), the tears raw and red round their lips. Painted in its own colours: a tree's tint only warms or
    pales it."""
    W = ctx.W
    from .mat_corruption import veins
    blue = noise.blur(veins(ctx, W, 12, key="bveins", length=(60, 220), width=(1.8, 3.6)), 1.6)
    red = noise.blur(veins(ctx, W, 7, key="mveins", length=(120, 320), width=(4.0, 8.0)), 1.4)
    thin = noise.fbm01(ctx.rng("thin"), (W, W), 50, octaves=4)
    pores = noise.fbm(ctx.rng("pores"), (W, W), 0.7, octaves=2)
    specks = smoothstep(0.86, 0.97, noise.white(ctx.rng("specks"), (W, W)))
    # Creases: fine wrinkles every way, and stretch marks the one way it's pulled.
    wr = noise.fbm(ctx.rng("wrinkle"), (W, W), 3.0, octaves=3)
    wrinkles = smoothstep(0.22, 0.0, np.abs(wr)) * smoothstep(0.35, 0.7, noise.fbm01(ctx.rng("wf"), (W, W), 30))
    st = noise.fbm(ctx.rng("stretch"), (W, W), 6.0, octaves=2, stretch=(0.12, 3.0))
    marks = smoothstep(0.18, 0.0, np.abs(st)) * smoothstep(0.55, 0.85, noise.fbm01(ctx.rng("sf"), (W, W), 40))
    # The tears: where it's thinnest, never through a red vein; ragged; a raw lip round each.
    holes = thin + 0.12 * (noise.fbm01(ctx.rng("rag"), (W, W), 5, octaves=2) - 0.5)
    cut = np.percentile(holes, 84)
    alpha = ((holes < cut) | (red > 0.35)).astype(np.float32)
    lip = smoothstep(cut - 0.07, cut, holes) * alpha
    height = red * 2.2 + blue * 0.4 - wrinkles * 0.8 - marks * 0.4 + pores * 0.12 - specks * 0.3 + lip * 1.2
    t = saturate(0.5 + 0.4 * (thin - 0.5) + 0.05 * pores)
    d = core.apply_ramp(t, ["#3A2228", "#6A444C", "#946870", "#B48C8E", "#CCA8A6"])
    # Thin: the light through it, warm and pink.
    d = lerp(d, hexc("#C47A80"), smoothstep(0.55, 0.9, thin) * 0.45)
    # Bruised: purple fields going yellow-green at their edges.
    br = noise.fbm01(ctx.rng("bruise"), (W, W), 45, octaves=4)
    d = lerp(d, hexc("#5A3A52"), smoothstep(0.62, 0.85, br) * 0.7)
    d = lerp(d, hexc("#8A8456"), smoothstep(0.5, 0.62, br) * smoothstep(0.75, 0.62, br) * 0.35)
    blot = smoothstep(0.6, 0.85, noise.fbm01(ctx.rng("blot"), (W, W), 12, octaves=4))
    d = lerp(d, hexc("#9A4A44"), blot * 0.3)
    d = lerp(d, hexc("#4A4A6A"), blue * 0.45)
    d = lerp(d, hexc("#5A1A1E"), red * 0.75)
    d = d * (1 - 0.35 * wrinkles - 0.15 * marks - 0.25 * specks)[..., None]
    d = lerp(d, hexc("#D8B4A4"), marks * 0.25)
    d = lerp(d, hexc("#4A0E10"), lip * 0.85)
    wet = smoothstep(0.45, 0.75, noise.fbm01(ctx.rng("wet"), (W, W), 22, octaves=4))
    s = (0.12 + 0.35 * wet + 0.25 * red + 0.3 * lip).astype(np.float32)
    g = (0.35 + 0.45 * wet + 0.2 * lip).astype(np.float32)
    d = C.light(d, height, strength=2.2, amount=0.55)
    d, _ = C.occlude(d, height, 3, 0.3)
    return ctx.out(d, saturate(s), saturate(g), alpha=alpha, alpha_test=True,
                   procedural="skin: pores, wrinkles and stretch marks (fbm), bruises, veins (strokes), tears", grain=0.02)


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


@texture("wound", "flesh", tile=0.4)
def wound(ctx):
    """What's under the bark where the Corruption's split it (tiling, 40 cm; the iron tree's sores round each spike, the
    birch's holes, the barnacle spruce's weeping splits): raw meat, wet. Dark red going to maroon in its folds and near
    black where it's clotted, the grain of muscle running through it, yellow knots of fat, pale pink where it's
    stretched over a swelling; glossy all over."""
    W = ctx.W
    fib = noise.fbm(ctx.rng("fibre"), (W, W), 1.6, octaves=2, stretch=(0.12, 4.0))
    bands = noise.fbm(ctx.rng("band"), (W, W), 10, octaves=2, stretch=(0.3, 3.0))
    swell = noise.fbm(ctx.rng("swell"), (W, W), 40, octaves=3)
    folds = smoothstep(0.7, 0.0, np.abs(noise.fbm(ctx.rng("folds"), (W, W), 36, octaves=2)))
    region = smoothstep(0.55, 0.75, noise.fbm01(ctx.rng("fatr"), (W, W), 30, octaves=3))
    knots = smoothstep(0.62, 0.8, noise.fbm01(ctx.rng("fat"), (W, W), 3.5, octaves=2)) * region
    clot = smoothstep(0.62, 0.85, noise.fbm01(ctx.rng("clot"), (W, W), 18, octaves=4))
    height = swell * 1.5 - folds * 1.8 + fib * 0.25 + bands * 0.3 + knots * 1.0
    t = saturate(0.5 + 0.15 * swell + 0.08 * fib + 0.1 * bands - 0.35 * folds)
    d = core.apply_ramp(t, ["#140406", "#3A0A0E", "#6A1418", "#8E2A26", "#B0544A"])
    d = lerp(d, hexc("#C88A7C"), smoothstep(0.9, 1.8, swell) * 0.3)
    d = lerp(d, hexc("#A89050"), knots * 0.7)
    d = lerp(d, hexc("#100406"), clot * 0.7)
    s = (0.45 + 0.35 * (1 - folds) - 0.2 * clot).astype(np.float32)
    g = (0.75 + 0.2 * (1 - clot)).astype(np.float32)
    d = C.light(d, height, strength=2.0, amount=0.5)
    d, _ = C.occlude(d, height, 6, 0.45)
    return ctx.out(d, saturate(s), saturate(g), procedural="raw flesh: muscle grain (fbm), fat knots (voronoi), clots")


@texture("glow_pod", "flesh", tile=0.3)
def glow_pod(ctx):
    """The lantern tree's light (tiling, 30 cm; the director, 13 Oct: "make the lanterns natural bioluminescence that is
    haunting"): a living membrane that glows from within, like foxfire or a deep-sea thing. Cells of it pale and lit
    through where it's thinnest, a cold greenish white, dimming toward their walls; dark veins branching over it and not
    lit, so each pod is drawn over with its own veins; dull clouded patches. The glow is the spec map's emissive mask (B),
    so the veins and walls stay dark while the cells burn; a tree's tint gives the light its colour."""
    W = ctx.W
    from .mat_corruption import veins
    f1, f2, idx = noise.voronoi(ctx.rng("cells"), (W, W), 18, jitter=1.0)
    size = np.sqrt(W * W / 18)
    f1 = f1 * (0.8 + 0.4 * noise.fbm01(ctx.rng("warp"), (W, W), 30, octaves=3))
    cell = saturate(1 - (f1 / (size * 0.75)) ** 2)
    wall = smoothstep(5, 0, f2 - f1)
    v = noise.blur(veins(ctx, W, 9, key="pveins", length=(70, 220), width=(1.6, 3.6)), 0.9)
    cloud = smoothstep(0.55, 0.85, noise.fbm01(ctx.rng("cloud"), (W, W), 20, octaves=4))
    specks = smoothstep(0.92, 0.99, noise.white(ctx.rng("specks"), (W, W)))
    light = saturate(0.35 + 0.65 * cell) * (1 - 0.85 * v) * (1 - 0.8 * wall) * (1 - 0.45 * cloud)
    light = saturate(light + specks * 0.5 * (1 - v))
    d = core.apply_ramp(saturate(light), ["#0C120E", "#24302A", "#5A6E60", "#9AB0A0", "#D6E6D8"])
    d = lerp(d, hexc("#1A1416"), v * 0.8)
    height = cell * 3 - wall * 2 - v * 1.2
    s = np.full((W, W), 0.55, np.float32)
    g = np.full((W, W), 0.85, np.float32)
    glow = saturate(light ** 1.3).astype(np.float32)
    return ctx.out(d, s, g, emissive=glow, procedural="lit cells (voronoi), dark veins (strokes), cloud (fbm); glow in B")


@texture("barnacle_crust", "flesh", tile=0.5)
def barnacle_crust(ctx):
    """A crust of acorn barnacles packed on each other (tiling, half a metre; the barnacle spruce's trunk, roots and limbs,
    under its modelled ones): each a chalky cone with its plates' seams and a black slot of a mouth, big ones and small
    ones crowded between, green-black slime in the gaps, a clump of mussels here and there, all of it wet."""
    W = ctx.W

    def shells(key, n, scale, fill):
        """One size of barnacle: the wall rising to its lip, the crater inside, the black slot of its mouth."""
        f1, f2, idx, ox, oy = noise.voronoi(ctx.rng(key), (W, W), n, jitter=1.0, offsets=True)
        size = np.sqrt(W * W / n)
        big = noise.cell_values(ctx.rng(key + "v"), idx)
        live = noise.cell_values(ctx.rng(key + "l"), idx) < fill
        x = f1 / (size * scale * (0.65 + 0.35 * big))       # 0 at its centre, 1 at its foot
        foot = (x < 1) & live
        wall = smoothstep(1.0, 0.45, x)
        crater = smoothstep(0.42, 0.2, x)
        h = (wall - 0.7 * crater) * (0.8 + 0.4 * big)
        ang = np.arctan2(oy, ox)
        seams = smoothstep(0.93, 1.0, np.abs(np.cos(ang * 3))) * smoothstep(0.95, 0.5, x) * (x > 0.35)
        mouth = smoothstep(0.2, 0.08, x)
        return foot, h * foot, (seams * foot), (mouth * foot), (smoothstep(0.55, 0.42, x) * smoothstep(0.3, 0.42, x) * foot)

    fa, ha, sa, ma, la = shells("big", 70, 0.55, 0.85)
    fb, hb, sb, mb, lb = shells("small", 700, 0.5, 0.9)
    fb = fb & ~fa
    height = np.where(fa, ha * 1.6, hb * 0.6)
    seams = np.where(fa, sa, sb * fb)
    mouth = np.where(fa, ma, mb * fb)
    lip = np.where(fa, la, lb * fb)
    shell = (fa | fb).astype(np.float32)
    gap = 1 - shell
    # Mussels: long dark shells lying in clumps over the barnacles.
    rng = ctx.rng("mussels")
    items = []
    for _ in range(10):
        cx, cy = rng.random(2) * W
        for _ in range(int(rng.integers(3, 7))):
            items.append((cx + rng.normal(0, 16), cy + rng.normal(0, 16), rng.uniform(6, 9), rng.uniform(16, 26), 1.0))
    mus = noise.blur(draw.ellipses((W, W), items), 1.5)
    grain = noise.fbm(ctx.rng("grain"), (W, W), 1.2, octaves=2)
    height = height + mus * 2 + grain * 0.08
    t = saturate(0.45 + 0.35 * height + 0.1 * grain)
    d = core.apply_ramp(t, ["#141614", "#3A3A34", "#6E6A60", "#9A958A", "#B8B2A4"])
    d = lerp(d, hexc("#C4BEB0"), lip * 0.5)
    d = d * (1 - 0.45 * seams)[..., None]
    d = lerp(d, hexc("#0C0B0A"), mouth * 0.95)
    slime = gap * (0.6 + 0.4 * noise.fbm01(ctx.rng("slime"), (W, W), 10, octaves=3))
    d = lerp(d, hexc("#18241A"), slime)
    d = lerp(d, hexc("#0E1016"), smoothstep(0.2, 0.6, mus))
    d = lerp(d, hexc("#2E3644"), smoothstep(0.8, 0.95, mus) * 0.35)
    s = (0.15 + 0.35 * gap + 0.4 * smoothstep(0.3, 0.8, mus) + 0.15 * shell).astype(np.float32)
    g = (0.45 + 0.4 * gap + 0.3 * mus).astype(np.float32)
    d = C.light(d, height, strength=2.5, amount=0.6)
    d, _ = C.occlude(d, height, 4, 0.45)
    return ctx.out(d, saturate(s), saturate(g), procedural="barnacles (voronoi cones, plates, mouths), slime, mussels")


@texture("tar_drip", "flesh", tile=0.5)
def tar_drip(ctx):
    """The tar tree's drips (tiling; v runs down a drip, scene.vert stretching it from its root: GreyboxRenderer.Motion's
    "_drip"; the director, 13 Oct: "It needs to feel more drippy, possibly even lightly animated"): tar running, so
    everything in it runs down: long glossy runs and the light sliding down them, fibres drawn out in strings, a bubble
    caught here and there; black with the blackened red in it where it's thin."""
    W = ctx.W
    runs = noise.fbm(ctx.rng("runs"), (W, W), 6, octaves=3, stretch=(0.25, 8.0))
    sheen = smoothstep(0.9, 2.0, runs)
    strings = smoothstep(0.3, 0.0, np.abs(noise.fbm(ctx.rng("str"), (W, W), 1.5, octaves=2, stretch=(0.3, 12.0))))
    thin = smoothstep(0.55, 0.9, noise.fbm01(ctx.rng("thin"), (W, W), 30, octaves=3, stretch=(0.4, 4.0)))
    rng = ctx.rng("bub")
    bub = noise.blur(draw.ellipses((W, W), [(*rng.random(2) * W, r, r * 1.3, 1.0) for r in rng.uniform(3, 9, 40)]), 1.5)
    height = runs * 1.6 + strings * 0.3 + bub * 2
    t = saturate(0.3 + 0.2 * runs + 0.25 * strings + 0.3 * bub)
    d = core.apply_ramp(t, "tar")
    d = C.tint(d, thin, "#3A1414", 0.7)
    s = (0.6 + 0.35 * sheen).astype(np.float32)
    g = np.full((W, W), 0.92, np.float32)
    d = C.light(d, height, strength=3, amount=0.45)
    return ctx.out(d, saturate(s), g, procedural="runs and strings (stretched fbm), bubbles", grain=0.03)


@texture("shroud_card", "foliage", tile=None)
def shroud_card(ctx):
    """The barnacle spruce's shrouds (256x512, hanging from the bottom edge: hung(); the director, 13 Oct: "make this look
    properly corrupted and haunted"): beard lichen grown long and pale as a winding sheet, from a narrow fist of it where
    it's caught on the limb, fanning as it falls, hair-fine threads gathered into lank strands, the ends ragged at
    different lengths; the grey-white of something drowned, a breath of green in it."""
    H, W = 1024, 512
    rng = ctx.rng("shroud")
    threads, widths, vals = [], [], []
    for k in range(420):
        x0 = W / 2 + rng.normal(0, 26)
        spread = rng.normal(0, 0.16)
        L = H * min(0.98, rng.uniform(0.45, 1.0) ** 0.6)
        pts = [(x0, 0.0)]
        x, y, a = x0, 0.0, np.pi / 2 + spread * 0.3
        for i in range(16):
            a += rng.normal(0, 0.05) + 0.04 * (np.pi / 2 + spread - a)
            x = float(np.clip(x + np.cos(a) * L / 16, 4, W - 4))
            y = min(y + np.sin(a) * L / 16, H - 4)
            pts.append((x, y))
        threads.append(pts)
        widths.append(rng.uniform(1.2, 2.6))
        vals.append(rng.uniform(0.5, 1.0))
    thr = draw.strokes((H, W), threads, widths, vals, wrap=False, supersample=2)
    # Gathered into strands: thicker locks where many threads run together.
    locks = noise.blur(thr, 3.0)
    alpha = saturate(np.maximum(thr * 1.7, smoothstep(0.25, 0.5, locks)))
    ys = np.arange(H, dtype=np.float32)[:, None] / H
    n = noise.white(ctx.rng("n"), (H, W)) - 0.5
    t = saturate(0.45 + 0.35 * thr + 0.15 * ys + 0.15 * n)
    d = core.apply_ramp(t, ["#2A302C", "#56605A", "#868E88", "#B0B8B0", "#D0D6CE"])
    d = d * self_shadow(alpha, 6, 0.3)[..., None]
    d = d * (0.65 + 0.35 * (1 - (1 - ys) ** 5))[..., None]
    d = lerp(d, hexc("#5A6A78"), rim(alpha, 1.5) * 0.3)
    s = np.full((H, W), 0.05, np.float32)
    g = np.full((H, W), 0.2, np.float32)
    d, alpha, s, g = hung(d, alpha, s, g)
    return card_out(ctx, d, alpha, s, g, "pale lichen threads fanning from a fist (strokes), gathered into locks")
