"""THE PREACHER (the townsfolk lookbook, ARCHITECTURE §8 note 694; the keeper of a town's hall): a black cassock to
the ankle, a wide-brimmed black felt hat, a gold stole down his front with a cross near each end, a brass pectoral
cross. His veil is gauze (Art/LookbookKit.cs: the screen-door membrane), so it isn't baked; his hand cross is carried.

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_preacher ; tools/models/build.sh townsfolk_preacher
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

BLACK = (0.32, 0.3, 0.3)


def cassock(p, n):
    """The cassock's skirt to the ankle: the coat's shells let down (they're 0.47 m at the hem)."""
    return 0.0


DRESS = {
    "crew_atlas.coat": ("wool", 8.0, BLACK, 0.85, 3),
    "crew_atlas.sleeve": ("wool", 8.0, BLACK, 0.85, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.25, 0.24, 0.24), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.scarf": ("wool", 10.0, BLACK, 0.95, 2),
    "town.felt": ("wool", 10.0, (0.3, 0.28, 0.28), 0.8, 2),
    "town.ribbon": ("wool", 14.0, (0.5, 0.42, 0.3), 0.5, 2),
    "town.stole": ("wool", 12.0, (2.4, 1.85, 0.6), 0.45, 2),
    "town.cross_ink": ("wool", 12.0, (0.3, 0.2, 0.1), 0.6, 0),
    "town.brass": ("brass", 6.0, (1.6, 1.4, 1.0), 0.3, 0),
}

tf.recipe("townsfolk_preacher", "preacher", DRESS, lambda fit: [tf.wide_hat(fit), tf.stole(fit), tf.pectoral_cross(fit)],
          off=("strap", "satchel", "lamp", "belt"))
