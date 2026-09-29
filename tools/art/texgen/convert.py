"""PBR -> legacy conversion and the hand pass (GDD §27 texture targets).

The engine is a 2006-style forward renderer: a diffuse colour and a Phong specular with an
intensity and a gloss (exponent 4..128), no metalness, no roughness, no normal maps. So every
PBR-ish input (a photoscanned set, or a procedural material that builds albedo/height/roughness
the same way) is flattened here:

  diffuse = albedo x AO (60-80%) x cavity (30%), metals darkened to ~40% albedo
            (a metal's colour lives in its reflection, which a Phong highlight can't carry,
            so a metal with full albedo reads as grey plastic);
  spec    = metal: its albedo's luminance (tinted metals reflect their own colour);
            non-metal: flat 0.04-0.2;
  gloss   = 1 - roughness, capped so nothing mirror-polishes.

Then `hand_pass` does what a texture artist on a PS2/PS3 horror game did by hand in
Photoshop: pull everything into the palette, paint in broad grime and soot, wear the edges,
run oil streaks down, add grain, and crunch it a little.
"""
from __future__ import annotations

import numpy as np
from PIL import Image

from . import core, noise
from .core import lerp, lum, saturate


def pbr_to_legacy(albedo, ao=None, height=None, normal=None, rough=None, metal=0.0,
                  ramp=None, pull=0.75, desat=0.8, contrast=1.0, bias=0.0,
                  ao_amt=0.7, cav_amt=0.3, cav_sigma=3.0, metal_albedo=0.45,
                  dielectric_spec=0.08, metal_spec=1.0, gloss_cap=0.8, gloss_floor=0.0):
    """Returns (diffuse_linear HxWx3, spec HxW, gloss HxW). `metal` may be a map or a scalar.

    With `ramp`, the albedo is first pushed into the palette (to_ramp) - the hand pass's palette
    step, done before shading so AO, cavity and the metal darkening survive it. Metal ramps are
    therefore authored as raw metal albedo, ~2.2x brighter than the dark iron they end up as."""
    h, w = albedo.shape[:2]
    metal = np.broadcast_to(np.asarray(metal, np.float32), (h, w)).astype(np.float32)
    if ramp is not None:
        albedo = to_ramp(albedo, ramp, pull=pull, desat=desat, contrast=contrast, bias=bias)
    shade = np.ones((h, w), np.float32)
    if ao is not None:
        shade *= 1 - ao_amt * (1 - saturate(ao))
    cav = None
    if height is not None:
        cav = noise.cavity_from_height(height, cav_sigma)
    elif normal is not None:
        cav = noise.cavity_from_normal(normal)
    if cav is not None:
        shade *= 1 - cav_amt * cav
    base_l = lum(albedo)
    diffuse = albedo * shade[..., None]
    diffuse = diffuse * lerp(1.0, metal_albedo, metal)[..., None]
    # Specular: metals reflect roughly their own brightness, dielectrics a flat few percent.
    # Occlusion kills highlights too: nothing shines from inside a crack.
    spec_metal = saturate(core.lin_to_srgb(base_l) * metal_spec)
    spec = lerp(np.float32(dielectric_spec), spec_metal, metal) * shade
    r = np.full((h, w), 0.7, np.float32) if rough is None else rough
    gloss = np.clip(1 - r, gloss_floor, gloss_cap).astype(np.float32)
    return diffuse.astype(np.float32), spec.astype(np.float32), gloss


def to_ramp(diffuse, ramp, pull=0.75, desat=0.8, lo=1.0, hi=99.0, contrast=1.0, bias=0.0, keep_value=0.0):
    """The palette push. Luminance is normalised (percentiles), optionally contrast-shaped, and
    mapped through a palette ramp; the result is blended with a heavily desaturated copy of the
    input so some of the photo's local hue variation survives (pure gradient maps look flat).
    `keep_value` > 0 keeps some of the input's absolute brightness instead of the ramp's."""
    l = lum(diffuse)
    t = noise.normalize(np.log(l + 1e-3), lo, hi)
    if contrast != 1.0:
        t = saturate((t - 0.5) * contrast + 0.5)
    t = saturate(t + bias)
    mapped = core.apply_ramp(t, ramp)
    grey = l[..., None]
    des = lerp(diffuse, np.repeat(grey, 3, -1), desat)
    # Bring the desaturated copy to the mapped value, so blending shifts hue, not brightness.
    des = des * (lum(mapped) / np.maximum(lum(des), 1e-5))[..., None]
    out = lerp(des, mapped, pull)
    if keep_value > 0:
        out = out * lerp(1.0, l / np.maximum(lum(out), 1e-5), keep_value)[..., None]
    return out.astype(np.float32)


def grime(rng, shape, scale, amount, coverage=0.5, stretch=(1.0, 1.0)):
    """A broad multiplicative grime/soot field (GDD §27 "broad soot, grime"): 1 = clean."""
    f = noise.fbm01(rng, shape, scale, octaves=5, stretch=stretch)
    m = core.smoothstep(1 - coverage - 0.2, 1 - coverage + 0.2, f)
    return (1 - amount * m).astype(np.float32), m


def streaks(rng, seed_mask, length=0.97, wander=0.03, density=1.0):
    """Oil/rust streaks running down (+v) from a seed mask (rivets, seams, rust patches).
    Column-varying decay so streaks have ragged lengths; thinned by a column mask so not every
    seed drips."""
    h, w = seed_mask.shape
    cols = noise.blur(rng.random((1, w), dtype=np.float32).repeat(4, 0), 1.2, 0.1)[0]
    cols = noise.normalize(cols)
    keep = (cols > 1 - density).astype(np.float32)
    s = noise.drip(seed_mask * keep[None, :], length, jitter_rng=rng, wander=wander)
    # Fade with a vertical noise so a streak thins and breaks up as it runs.
    brk = noise.fbm01(rng, (h, w), 12, octaves=3, stretch=(0.15, 2.0))
    return saturate(s * (0.4 + 0.9 * brk)).astype(np.float32)


def chips(rng, shape, edge=None, amount=0.5, scale=10.0, edge_weight=1.2):
    """Chipped-paint mask (1 = paint gone): noise thresholded, biased toward edges."""
    n = noise.fbm01(rng, shape, scale, octaves=5, gain=0.6)
    if edge is not None:
        n = n + edge_weight * edge
    thr = np.percentile(n, 100 * (1 - amount))
    m = core.smoothstep(thr - 0.02, thr + 0.02, n)
    return m.astype(np.float32)


def finish(tex: core.Tex, rng) -> core.Tex:
    """Per-texel pass at final resolution: grain (one grain = one texel, §27 "visible grain,
    subtle pixel crawl"), a little 2x2 chroma averaging and quantisation for the compression feel."""
    d = tex.diffuse.astype(np.float32)
    h, w = d.shape[:2]
    if tex.grain > 0:
        g = (rng.random((h, w), dtype=np.float32) - 0.5) * 2
        g2 = (noise.block_noise(rng, (h, w), 2) - 0.5) * 2
        mul = 1 + tex.grain * (0.7 * g + 0.5 * g2)
        d = d * mul[..., None]
        tex.gloss = saturate(tex.gloss * (1 + 0.5 * tex.grain * g))
        tex.spec = saturate(tex.spec * (1 + tex.grain * g2))
    if tex.chroma_block > 1 and h % tex.chroma_block == 0:
        b = tex.chroma_block
        l = lum(d)[..., None]
        chroma = d / np.maximum(l, 1e-5)
        cb = noise.box_down(chroma, b)
        cb = np.repeat(np.repeat(cb, b, 0), b, 1)
        d = lerp(d, cb * l, 0.6)
    tex.diffuse = d
    return tex


def quantise(x01, levels, rng, dither=True):
    """0..1 -> uint8 with `levels` steps and a faint noise dither (banding would read as modern
    8-bit gradient; noisy posterisation reads as an old compressed texture)."""
    x = np.clip(x01, 0, 1)
    if levels >= 256:
        return np.round(x * 255).astype(np.uint8)
    step = 1.0 / (levels - 1)
    if dither:
        x = x + (rng.random(x.shape, dtype=np.float32) - 0.5) * step * 0.8
    q = np.round(np.clip(x, 0, 1) / step) * step
    return np.round(q * 255).astype(np.uint8)


def encode(tex: core.Tex, rng):
    """-> (diffuse RGBA uint8, spec RGB uint8)."""
    srgb = core.lin_to_srgb(tex.diffuse)
    rgb8 = quantise(srgb, tex.quant, rng, tex.dither)
    h, w = rgb8.shape[:2]
    if tex.alpha is None:
        a8 = np.full((h, w), 255, np.uint8)
    elif tex.alpha_test:
        # Cutout: a hard mask (the engine alpha-tests at 0.5). Pixel-hard edges are the look.
        a8 = np.where(tex.alpha >= 0.5, 255, 0).astype(np.uint8)
    else:
        a8 = np.round(saturate(tex.alpha) * 255).astype(np.uint8)
    diffuse = np.dstack([rgb8, a8])
    em = np.zeros((h, w), np.float32) if tex.emissive is None else tex.emissive
    spec = np.dstack([
        quantise(tex.spec, 32, rng, False),
        quantise(tex.gloss, 32, rng, False),
        np.round(saturate(em) * 255).astype(np.uint8),
    ])
    return diffuse, spec


def save_png(arr: np.ndarray, path):
    mode = {3: "RGB", 4: "RGBA"}[arr.shape[2]]
    # optimize=True is deterministic in Pillow (zlib level 9); no metadata chunks are written.
    Image.fromarray(arr, mode).save(path, format="PNG", optimize=True)
