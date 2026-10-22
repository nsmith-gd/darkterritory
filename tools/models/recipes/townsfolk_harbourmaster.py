"""THE HARBOUR MASTER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's gatekeeper): a navy greatcoat,
double-breasted in brass, broad at the belly, a peaked cap with a gold cord, a full white beard. His eyeglass is worn
and his spyglass and keys carried (Art/LookbookKit.cs).

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_harbourmaster ; tools/models/build.sh townsfolk_harbourmaster
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

NAVY = (0.42, 0.48, 0.95)


def belly(p, n):
    """Broad at the belly: the frame let out in front over the belt."""
    return 0.03 * np.exp(-((p[:, 2] - 1.1) / 0.14) ** 2) * np.clip(p[:, 1] / 0.12, 0, 1)


DRESS = {
    "crew_atlas.coat": ("wool", 8.0, NAVY, 0.85, 3),
    "crew_atlas.sleeve": ("wool", 8.0, NAVY, 0.85, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.35, 0.37, 0.55), 0.9, 3),
    "crew_atlas.gloves": ("leather", 6.0, (0.3, 0.26, 0.22), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.3, 0.24, 0.2), 0.5, 0),
    "crew_atlas.scarf": ("wool", 10.0, NAVY, 0.95, 2),
    "town.navy": ("wool", 10.0, NAVY, 0.85, 2),
    "town.peak": ("paint_black", 6.0, (0.3, 0.3, 0.3), 0.15, 0),
    "town.braid": ("brass", 6.0, (1.8, 1.5, 0.8), 0.3, 0),
    "town.beard": ("fleece", 12.0, (1.55, 1.55, 1.5), 0.95, 2),
    "town.brass": ("brass", 6.0, (1.7, 1.5, 1.0), 0.3, 0),
}

tf.recipe("townsfolk_harbourmaster", "harbour master", DRESS,
          lambda fit: [tf.captains_cap(fit), tf.beard(fit), tf.buttons(fit)], low_shapes={"frame": belly})
