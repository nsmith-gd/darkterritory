"""THE LINESMAN (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's fitter): a grey work jacket and trousers,
a flat cap with goggles pushed up on it, a wool wrap over his face, his belt, a coil of cable on a strap.

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_linesman ; tools/models/build.sh townsfolk_linesman
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

DRESS = {
    "crew_atlas.coat": ("wool", 8.0, (1.0, 1.02, 0.95), 0.9, 3),
    "crew_atlas.sleeve": ("wool", 8.0, (1.0, 1.02, 0.95), 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.62, 0.62, 0.58), 0.9, 3),
    "crew_atlas.gloves": ("leather", 6.0, (0.62, 0.45, 0.32), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.55, 0.38, 0.25), 0.5, 0),
    "crew_atlas.scarf": ("wool", 10.0, (1.0, 1.02, 0.95), 0.95, 2),
    "town.cap": ("wool", 10.0, (1.05, 1.05, 0.95), 0.9, 2),
    "town.brass": ("brass", 6.0, (1.6, 1.4, 1.0), 0.3, 0),
    "town.glass": ("glass_dirty", 6.0, (0.3, 0.34, 0.34), 0.06, 0),
    "town.wrap": ("wool", 10.0, (1.6, 1.42, 1.1), 0.95, 2),
    "town.wire": ("leather", 8.0, (0.4, 0.26, 0.16), 0.4, 0),
    "town.strap": ("leather", 7.0, (0.4, 0.3, 0.22), 0.55, 0),
}

tf.recipe("townsfolk_linesman", "linesman", DRESS,
          lambda fit: [tf.flat_cap(fit, "cap", goggles=("brass", "glass")), tf.wrap(fit), tf.coil(fit)], coat=False,
          off=("strap", "satchel", "lamp"))
