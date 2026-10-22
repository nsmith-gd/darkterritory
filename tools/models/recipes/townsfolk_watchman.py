"""THE WATCHMAN (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's wall guard): a long grey coat quilted in
channels, a fur ushanka, binoculars on their strap. His flag is carried (Art/LookbookKit.cs).

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_watchman ; tools/models/build.sh townsfolk_watchman
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

GREY = (1.55, 1.48, 1.2)


def quilted(p, n):
    """The padding sewn in channels a hand apart: down the coat in rings, round the sleeves."""
    z = p[:, 2]
    return -0.0045 * np.exp(-(np.sin(z * np.pi / 0.075) / 0.28) ** 2)


def quilted_sleeve(p, n):
    return -0.004 * np.exp(-(np.sin(np.abs(p[:, 0]) * np.pi / 0.07) / 0.28) ** 2)


DRESS = {
    "crew_atlas.coat": ("wool", 8.0, GREY, 0.9, 3),
    "crew_atlas.sleeve": ("wool", 8.0, GREY, 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.55, 0.52, 0.48), 0.9, 3),
    "crew_atlas.gloves": ("leather", 6.0, (0.3, 0.26, 0.22), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.36, 0.28, 0.22), 0.5, 0),
    "town.fur": ("fleece", 6.0, (0.62, 0.48, 0.36), 0.95, 2),
    "town.binocular": ("paint_black", 6.0, (0.5, 0.48, 0.45), 0.4, 0),
    "town.brass": ("brass", 6.0, (1.6, 1.4, 1.0), 0.3, 0),
    "town.glass": ("glass_dirty", 6.0, (0.15, 0.16, 0.17), 0.06, 0),
    "town.strap": ("leather", 7.0, (0.4, 0.3, 0.22), 0.55, 0),
}

tf.recipe("townsfolk_watchman", "watchman", DRESS, lambda fit: [tf.ushanka(fit), tf.binoculars(fit)],
          shapes={"crew_atlas.coat": quilted, "crew_atlas.sleeve": quilted_sleeve})
