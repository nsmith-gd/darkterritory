"""Hand-pass operations shared by the material builders (GDD §27: "visible grain, broad rust
fields, chipped paint, soot, grime, oil staining, baked shadows, hand-authored roughness
breakup"). Each takes and returns work-res maps: diffuse (linear HxWx3), spec, gloss (HxW)."""
from __future__ import annotations

import numpy as np

from . import convert, core, noise
from .core import hexc, lerp, saturate


def light(d, height, strength=6.0, amount=0.6):
    """Bake the painted-in key light (from above) into the diffuse. With normal maps (the "ps3" era) the engine lights
    the relief itself, so only a little of the painted light stays: enough to read, not to double the shading."""
    core.capture("height", height)
    if core.ERA == "ps3":
        amount *= 0.35
    return d * noise.bake_light(height, strength, amount)[..., None]


def occlude(d, height, sigma=6.0, amount=0.4):
    """Baked ambient occlusion from a height map: crevices collect shadow and dirt."""
    core.capture("height", height)
    cav = noise.cavity_from_height(height, sigma)
    return d * (1 - amount * cav)[..., None], cav


def soot(rng, d, spec, gloss, amount=0.4, scale=60.0, coverage=0.45, stretch=(1.0, 1.0), colour="#141312"):
    """Broad soot/grime fields (§27): darken toward soot, kill the specular under the dirt."""
    shape = d.shape[:2]
    f = noise.fbm01(rng, shape, scale, octaves=5, stretch=stretch)
    m = core.smoothstep(1 - coverage - 0.25, 1 - coverage + 0.25, f) * amount
    d = lerp(d, d * 0.35 + hexc(colour) * 0.65, m)
    return d, spec * (1 - 0.7 * m), gloss * (1 - 0.5 * m), m


def streak(rng, d, spec, gloss, seed, amount=0.5, colour="#121110", length=0.985, gloss_add=0.1,
           spec_add=0.0, wander=0.03, density=0.8):
    """Streaks running down from `seed` (oil from rivets and seams, rust bleeding from chips)."""
    s = convert.streaks(rng, seed, length=length, wander=wander, density=density) * amount
    d = lerp(d, d * 0.3 + hexc(colour) * 0.7, s)
    return d, saturate(spec + spec_add * s), saturate(gloss + gloss_add * s), s


def reveal(d, spec, gloss, mask, colour, spec_v=None, gloss_v=None, var=None):
    """Where `mask` is 1 the surface shows `colour` (bare iron under chipped paint, rust...).
    `colour` may be a hex string or an HxWx3 layer."""
    c = hexc(colour) if isinstance(colour, str) else colour
    if var is not None:
        c = c * (0.75 + 0.5 * var)[..., None]
    d = lerp(d, c, mask)
    if spec_v is not None:
        spec = lerp(spec, spec_v, mask)
    if gloss_v is not None:
        gloss = lerp(gloss, gloss_v, mask)
    return d, spec, gloss


def tint(d, mask, colour, amount):
    """Multiply-tint toward a colour's hue at its own brightness (heat tint, verdigris wash)."""
    c = hexc(colour)
    c = c / max(float(core.lum(c)), 1e-5)
    l = core.lum(d)[..., None]
    return lerp(d, l * c, mask * amount)


def rust_layer(ctx, shape, key="rust"):
    """A rust colour layer with its own mottling (not flat rust-red): used by every metal."""
    rng = ctx.rng(key)
    n1 = noise.fbm01(rng, shape, 18, octaves=5)
    n2 = noise.fbm01(rng, shape, 4, octaves=3)
    t = saturate(0.15 + 0.6 * n1 + 0.35 * (n2 - 0.5))
    return core.apply_ramp(t, "rust"), n1


def expose(x, gain):
    return (x * gain).astype(np.float32)
