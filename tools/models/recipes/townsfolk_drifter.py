"""THE DRIFTER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's driver): a long brown coat, a flat cap, a
red wool scarf wound over his face. His flask is carried (Art/LookbookKit.cs).

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_drifter ; tools/models/build.sh townsfolk_drifter
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

BROWN = (1.15, 0.85, 0.62)

DRESS = {
    "crew_atlas.coat": ("coat_oilskin", 7.0, BROWN, 0.7, 3),
    "crew_atlas.sleeve": ("coat_oilskin", 7.0, BROWN, 0.7, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.8, 0.68, 0.52), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.36, 0.28, 0.22), 0.5, 0),
    "crew_atlas.scarf": ("wool", 10.0, (2.0, 0.45, 0.32), 0.95, 2),
    "town.cap": ("wool", 10.0, (1.2, 0.95, 0.68), 0.9, 2),
    "town.wrap": ("wool", 10.0, (2.0, 0.45, 0.32), 0.95, 2),
}

tf.recipe("townsfolk_drifter", "drifter", DRESS, lambda fit: [tf.flat_cap(fit), tf.wrap(fit)])
