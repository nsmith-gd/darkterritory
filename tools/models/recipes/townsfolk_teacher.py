"""THE SCHOOLTEACHER (the townsfolk lookbook, ARCHITECTURE §8 note 694; a town's clerk): a green tweed coat over a long
dark skirt, her hair drawn up in a bun. Her spectacles are worn and her books carried (Art/LookbookKit.cs).

    TOWN_PREVIEW=1 tools/models/build.sh townsfolk_teacher ; tools/models/build.sh townsfolk_teacher
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import numpy as np  # noqa: E402,F401

import townfolk as tf  # noqa: E402
from overbake import bell, smooth01  # noqa: E402,F401

GREEN = (0.85, 1.0, 0.7)

DRESS = {
    "crew_atlas.coat": ("wool", 8.0, GREEN, 0.9, 3),
    "crew_atlas.sleeve": ("wool", 8.0, GREEN, 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.45, 0.42, 0.4), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.scarf": ("wool", 10.0, (1.9, 1.75, 1.5), 0.95, 2),
    "town.skirt": ("wool", 9.0, (0.6, 0.55, 0.52), 0.95, 2),
    "town.hair": ("fleece", 14.0, (0.55, 0.36, 0.26), 0.7, 2),
}


def grade(base, atlas, face):
    return tf.soften_face(base, face)


tf.recipe("townsfolk_teacher", "schoolteacher", DRESS,
          lambda fit: [tf.hair_up(fit), tf.worn([tf.skirt(fit, tf.mat("skirt"), "skirt", top=0.96, hem=0.1, flare=0.04)], "skirt", None, stiff=0.6)],
          off=("strap", "satchel", "lamp", "belt"), head=tf.softer, grade=grade)
