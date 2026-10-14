"""THE BUTCHER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's cook): a pale shirt, a knitted cap, a gas
mask, a canvas bib apron stained dark with blood, more of it low down where he wipes his hands. His knife is carried.

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_butcher ; tools/models/build.sh townsfolk_butcher
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

import cook  # noqa: E402

DRESS = {
    "crew_atlas.coat": ("wool", 9.0, (2.7, 2.4, 1.85), 0.9, 3),
    "crew_atlas.sleeve": ("wool", 9.0, (2.7, 2.4, 1.85), 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.5, 0.48, 0.45), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.36, 0.28, 0.22), 0.5, 0),
    "crew_atlas.scarf": ("wool", 10.0, (2.7, 2.4, 1.85), 0.95, 2),
    "town.knit": ("wool", 14.0, (1.7, 1.7, 1.6), 0.95, 2),
    "town.apron": ("wool", 11.0, (3.0, 2.7, 2.0), 0.9, 2),
    "town.tie": ("wool", 11.0, (2.2, 1.95, 1.45), 0.9, 0),
    "town.rubber": ("paint_black", 6.0, (0.42, 0.4, 0.38), 0.45, 2),
    "town.brass": ("brass", 6.0, (1.6, 1.4, 1.0), 0.3, 0),
    "town.glass": ("glass_dirty", 6.0, (0.12, 0.13, 0.14), 0.06, 0),
    "town.tin": ("paint_olive", 6.0, (0.6, 0.62, 0.48), 0.5, 0),
    "town.strap": ("leather", 7.0, (0.3, 0.24, 0.2), 0.55, 0),
}


def blood(p):
    """Blood on the apron's front: blots, more low down where he wipes his hands, run downward."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    on = (y > 0.12) & (z > 0.5) & (z < 1.4) & (np.abs(x) < 0.25)
    blots = np.clip(cook.noise_np(p * np.array([1.0, 1.0, 0.45], np.float32), 81, 9) * 2.2 - 0.9, 0, 1)
    low = 0.35 + 0.65 * smooth01(1.3, 0.75, z)
    return (blots * low * on).astype(np.float32)


def grade(base, atlas, face):
    b = atlas.maps["blood"][..., None]
    return base * (1 - 0.85 * b) + np.array([0.12, 0.012, 0.01], np.float32) * (0.85 * b)


tf.recipe("townsfolk_butcher", "butcher", DRESS, lambda fit: [tf.beanie(fit), tf.gas_mask(fit), tf.bib_apron(fit)], coat=False,
          masks={"blood": blood}, grade=grade)
