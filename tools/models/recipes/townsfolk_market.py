"""THE MARKET WOMAN (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's storekeeper): the survivors' figure in
a dark wool bodice, a red headscarf knotted under her chin, a red plaid shawl round her shoulders crossed on her chest,
a long cream apron over a red skirt. Her keys and her crate of greens are carried (Art/LookbookKit.cs), not baked.

    tools/models/build.sh townsfolk_market
    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_market   # the high figure to out/review, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import townfolk as tf  # noqa: E402

RED = (2.6, 0.62, 0.42)

DRESS = {
    "crew_atlas.coat": ("wool", 9.0, (0.55, 0.42, 0.36), 0.9, 3),
    "crew_atlas.sleeve": ("wool", 9.0, (0.55, 0.42, 0.36), 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.3, 0.27, 0.25), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.scarf": ("wool", 10.0, (0.55, 0.42, 0.36), 0.95, 2),
    "town.scarf": ("wool", 11.0, RED, 0.95, 2),
    "town.skirt": ("wool", 9.0, RED, 0.95, 2),
    "town.apron": ("wool", 12.0, (3.4, 3.1, 2.5), 0.95, 2),
}


def garments(fit):
    DRESS["town.shawl"] = (tf.plaid("town_plaid", (0.11, 0.012, 0.009), (0.018, 0.02, 0.018), (0.14, 0.11, 0.025), repeat=5.5), 2)
    return [tf.headscarf(fit),
            tf.worn([tf.shawl(fit, tf.mat("shawl"), "shawl")], "shawl", None),
            tf.worn([tf.skirt(fit, tf.mat("skirt"), "skirt"), tf.apron(fit, tf.mat("apron"), "apron")], "skirt", None, stiff=0.6)]


def grade(base, atlas, face):
    return tf.soften_face(base, face)


tf.recipe("townsfolk_market", "market woman", DRESS, garments, coat=False, off=("strap", "satchel", "lamp", "belt"), head=tf.softer,
          grade=grade)
