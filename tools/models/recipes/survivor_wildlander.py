"""SURVIVOR: THE WILDLANDER (GDD App. D.4, D.8): someone who held out alone in a signal box, a lamp room or a water
tower. A freed player comes back as this, and plays as it for the rest of the run, so it's the crew figure
(tools/models/crewfigure) with the crew's own rig and clips, but:
  * bare-headed, Lee Perry-Smith's scan for the face, no mask, the face blacked with soot;
  * the coat patched together out of hides and furs, the patches each their own hide, shaggy at the collar and the
    shoulders where the fur's left on, everything sooted from a fire kept going in a box room;
  * wool trousers gone dark, rag-wrapped hands.

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh survivor_wildlander
    WILDLANDER_PREVIEW=1 tools/models/build.sh survivor_wildlander   # the high figure to out/review, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402

import cook  # noqa: E402
import crewfigure  # noqa: E402
from overbake import smooth01  # noqa: E402


def patches(p):
    """Which hide a bit of coat is cut from: blotchy cells, 0..1, quantised into four hides."""
    n = cook.noise_np(p, 61, 6) * 0.7 + cook.noise_np(p, 62, 14) * 0.3
    return np.floor(np.clip(n, 0, 0.999) * 4) / 3


def fur(p):
    """Where the fur's left on: the collar and the shoulders, and the cuffs."""
    x, z = np.abs(p[:, 0]), p[:, 2]
    return np.clip(smooth01(1.3, 1.45, z) + smooth01(0.5, 0.6, x) * 0.6, 0, 1)


def shaggy(p, n):
    """Tufts standing off the hide where the fur is."""
    return 0.012 * fur(p) * np.maximum(0, cook.noise_np(p, 63, 70)) + 0.004 * cook.noise_np(p, 64, 25)


def grade(base, atlas, face):
    hides = np.array([[0.36, 0.27, 0.18], [0.24, 0.2, 0.15], [0.42, 0.36, 0.28], [0.18, 0.15, 0.12]], np.float32)
    k = np.clip(np.rint(atlas.maps["patches"] * 3), 0, 3).astype(int)
    grey = base.mean(-1, keepdims=True) / max(float(base.mean()), 1e-3)
    coat = atlas.maps["coat"][..., None]
    out = base * (1 - coat) + hides[k] * np.clip(grey, 0.5, 1.4) * coat
    # Sooted all over, and the face blacked.
    soot = np.array([0.02, 0.018, 0.016], np.float32)
    out = out * 0.75 + soot * 0.25
    out = np.where(face[..., None], out * 0.6 + soot * 0.4, out)
    return out


def coat(p):
    """The coat and its sleeves (above the trousers' top, outward of the body's core)."""
    return smooth01(0.88, 0.95, p[:, 2]) * smooth01(1.62, 1.52, p[:, 2])


DRESS = {
    "crew_atlas.coat": ("leather", 5.0, (0.5, 0.4, 0.3), 0.85, 3),
    "crew_atlas.sleeve": ("leather", 5.0, (0.5, 0.4, 0.3), 0.85, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.22, 0.21, 0.2), 0.95, 3),
    "crew_atlas.gloves": ("wool", 10.0, (0.32, 0.29, 0.25), 0.95, 0),
    "crew_atlas.cap": ("leather", 6.0, (0.3, 0.24, 0.18), 0.85, 2),
    "crew_atlas.scarf": ("wool", 10.0, (0.3, 0.26, 0.22), 0.95, 2),
}
SHAPES = {k: shaggy for k in ("crew_atlas.coat", "crew_atlas.sleeve")}

crewfigure.build("survivor_wildlander", crewfigure.Style(
    dress=DRESS, shapes=SHAPES, masks={"patches": patches, "coat": coat}, grade=grade, preview="WILDLANDER_PREVIEW",
    figure="bare", hats=False, what="a freed wildlander in patched hides and furs, modelled over tools/blender/crew.py"))
