"""THE TRAPPER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's porter): the survivors' figure in a long
yellow oilskin over dark wool, a bucket hat of the same oilskin, a gas mask (black rubber, brass eyepieces, an olive
can). The gaff and the lobster pot are carried (Art/LookbookKit.cs), not baked.

    tools/models/build.sh townsfolk_trapper
    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_trapper   # the high figure to out/review, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import townfolk as tf  # noqa: E402

OILSKIN = (2.6, 1.95, 0.45)

DRESS = {
    "crew_atlas.coat": ("coat_oilskin", 7.0, OILSKIN, 0.35, 3),
    "crew_atlas.sleeve": ("coat_oilskin", 7.0, OILSKIN, 0.35, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.42, 0.38, 0.34), 0.9, 3),
    "crew_atlas.gloves": ("leather", 6.0, (0.32, 0.27, 0.22), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.36, 0.28, 0.22), 0.5, 0),
    "town.hat": ("coat_oilskin", 9.0, OILSKIN, 0.35, 2),
    "town.rubber": ("paint_black", 6.0, (0.42, 0.4, 0.38), 0.45, 2),
    "town.brass": ("brass", 6.0, (1.7, 1.5, 1.1), 0.3, 0),
    "town.glass": ("glass_dirty", 6.0, (0.12, 0.13, 0.14), 0.06, 0),
    "town.tin": ("paint_olive", 6.0, (0.6, 0.62, 0.48), 0.5, 0),
    "town.strap": ("leather", 7.0, (0.3, 0.24, 0.2), 0.55, 0),
}


def garments(fit):
    return [tf.bucket_hat(fit), tf.gas_mask(fit)]


tf.recipe("townsfolk_trapper", "trapper", DRESS, garments)
