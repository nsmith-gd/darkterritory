"""THE LAMPLIGHTER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's lampman): a hooded brown coat, the hood
down onto his shoulders as a cowl, a black rubber mask, two bottles on his back in a harness. The lamp before his mask
and the lamp on his pole burn (Art/LookbookKit.cs), so they're not baked.

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_lamplighter ; tools/models/build.sh townsfolk_lamplighter
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

BROWN = (1.35, 1.0, 0.7)

DRESS = {
    "crew_atlas.coat": ("wool", 8.0, BROWN, 0.9, 3),
    "crew_atlas.sleeve": ("wool", 8.0, BROWN, 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.5, 0.44, 0.38), 0.9, 3),
    "crew_atlas.gloves": ("leather", 6.0, (0.3, 0.24, 0.2), 0.6, 0),
    "crew_atlas.belt": ("leather", 5.0, (0.36, 0.28, 0.22), 0.5, 0),
    "town.hood": ("wool", 8.0, (1.45, 1.1, 0.78), 0.92, 2),
    "town.rubber": ("paint_black", 6.0, (0.42, 0.4, 0.38), 0.45, 2),
    "town.bottle": ("iron_smokebox", 4.0, (1.1, 1.0, 0.85), 0.45, 0),
    "town.brass": ("brass", 6.0, (1.6, 1.4, 1.0), 0.3, 0),
    "town.strap": ("leather", 7.0, (0.4, 0.28, 0.2), 0.55, 0),
}


def garments(fit):
    return [tf.worn([tf.hooded(fit, tf.mat("hood"), "hood", foot=1.5, brow=1.74, half=1.05, ease=0.045, fullness=0.012, thick=0.014,
                               top=1.87, cowl=1.38, peak=0.03, n=20, levels=9)], "hood", "neck"),
            tf.worn([tf.facepiece(fit, tf.mat("rubber"), brow=1.7, chin=1.51, half=0.95, snout=0.02)], "lampmask", "head"),
            tf.bottles(fit)]


# (His face is under the facepiece and the hood: its triangles go where they show, the figure inside GDD §27's 10k.)
tf.recipe("townsfolk_lamplighter", "lamplighter", DRESS, garments, head_tris=1700)
