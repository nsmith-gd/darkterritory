"""The value grade: each texture's mean linear albedo, set per material family.

Why: the hand pass paints value *structure* (AO, grime, soot in the crevices, edge wear, §27),
but the night itself comes from the engine's lighting (moon, ambient, fog, practical lamps,
§28). Textures painted dark on top of dark lighting read as black soup; lamp-lit surfaces need
readable mid values. So after the hand pass every texture is lifted by one tone curve to a
target mean, set here per material, measured the way the engine team measures it (mean over
opaque texels of sRGB^2.2, `tools/art/measure.py`).

The curve is a gain with a soft shoulder on luminance, out = kL / (1 + (k-1)L): dark values
scale by k, so a crevice stays the same fraction of its surroundings (blacks stay in the
crevices), the top end rolls off instead of clipping, and hue is untouched (the palette
survives). k is solved per texture by bisection.

Targets (lead's in-engine pass, 2026-09): soot/tar/coal/glass 0.03-0.05; wood 0.07-0.11;
iron/steel 0.08-0.12; painted metal 0.08-0.13 (black ~0.05); brass/copper 0.10-0.15; ballast
0.10-0.14 (light stone, among the lightest things under the headlamp); mud/forest/marsh
0.05-0.08; grass 0.07-0.10; rock/stone/concrete/plaster 0.09-0.15; brick 0.06-0.09; slate
0.06; cloth/leather 0.06-0.10; flesh/skin ~0.13-0.16; sac ~0.08. Emissive, paper, FX and the
sky are not graded (None).
"""
from __future__ import annotations

import numpy as np

from . import core

ALBEDO = {
    # iron and steel
    "iron_plate": 0.10, "iron_smokebox": 0.04, "paint_oxide": 0.11, "paint_olive": 0.10, "paint_black": 0.05,
    "corrugated_iron": 0.10, "steel_grate": 0.10, "brass": 0.12, "copper_pipe": 0.12, "rail_steel": 0.11,
    "wheel_iron": 0.09, "rust_heavy": 0.09,
    # wood
    "wood_siding": 0.09, "wood_sleeper": 0.075, "wood_grey": 0.11, "wood_crate": 0.09, "wood_floor": 0.09,
    "pine_bark": 0.07,
    # ground
    "ballast": 0.12, "ground_mud": 0.06, "ground_grass": 0.085, "ground_forest": 0.055, "marsh": 0.05,
    "rock_cliff": 0.10, "coal": 0.04, "slag": 0.08, "cobbles": 0.10,
    # the Maritimes (mat_maritime): the heath and the clay a little lighter than the black forest's floor, the
    # granite the palest ground there is (it catches the headlamp), the bog near black
    "ground_heath": 0.075, "ground_needles": 0.065, "granite_lichen": 0.13, "ground_red_clay": 0.075, "water_dark": 0.016,
    "bog_sphagnum": 0.06, "shore_shingle": 0.10,
    # the towns' walls (note 281): silver cedar shingle a touch lighter than grey boards; the clapboard's white paint
    # the lightest wood there is, so a house's paint (the kit's tint over it) reads under a door lamp
    "shingle_cedar": 0.13, "clapboard": 0.26,
    # masonry
    "stone_block": 0.11, "brick_soot": 0.075, "concrete_stain": 0.13, "plaster_ruin": 0.13, "roof_slate": 0.06,
    # foliage
    "pine_card": 0.04, "pine_bough": 0.04, "dead_tree_card": 0.07, "grass_card": 0.08, "brass_weed_card": 0.08,
    # cloth and crew
    "coat_oilskin": 0.07, "leather": 0.08, "wool": 0.06, "fleece": 0.2, "skin": 0.14, "crew_atlas": 0.09,
    # corruption (flesh/skin already sit right; sac comes down)
    "flesh": 0.14, "fungal_crust": 0.10, "mineral_growth": 0.07, "tar": 0.035, "sac": 0.08,
    # glass
    "glass_dirty": 0.04,
    # not graded: emitted colour, paper/dials, FX, sky
    "lamp_lens": None, "window_lit": None, "firebox": None, "ember_crack": None,
    "gauge_face": None, "paper_form": None, "sky_backdrop": None,
    "fx_smoke": None, "fx_steam": None, "fx_spark": None, "fx_fog": None,
}


DESAT = 0.18


def measure(diffuse_lin, alpha=None):
    """Mean of sRGB^2.2 over opaque texels: the engine team's measure."""
    enc = core.lin_to_srgb(diffuse_lin) ** 2.2
    if alpha is not None:
        m = alpha >= 0.5
        return float(enc[m].mean()) if m.any() else 0.0
    return float(enc.mean())


def curve(diffuse_lin, k):
    L = core.lum(diffuse_lin)
    L2 = k * L / (1 + (k - 1) * L)
    return np.clip(diffuse_lin * (L2 / np.maximum(L, 1e-6))[..., None], 0, 1).astype(np.float32)


def grade(tex: core.Tex) -> core.Tex:
    target = ALBEDO.get(tex.name)
    if target is None:
        return tex
    lo, hi = 0.25, 40.0
    for _ in range(28):
        k = np.sqrt(lo * hi)
        if measure(curve(tex.diffuse, k), tex.alpha) < target:
            lo = k
        else:
            hi = k
    d = curve(tex.diffuse, np.sqrt(lo * hi))
    # Lifting values makes chroma read stronger on screen; take a little back so the set stays
    # desaturated and cold (§28). Luminance (and so the measured mean) is unchanged.
    L = core.lum(d)[..., None]
    tex.diffuse = (d + (L - d) * DESAT).astype(np.float32)
    tex.extra.setdefault("albedoTarget", target)
    return tex
