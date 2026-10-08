"""SURVIVOR: THE PRISONER (GDD App. D.4, D.8): a convict off a derelict penal transport, the guards long gone. A freed
player comes back as this, and plays as it for the rest of the run, so it's the crew figure (tools/models/crewfigure)
with the crew's own rig and clips, but:
  * bare-headed, Lee Perry-Smith's scan for the face, no mask: nobody issued them one;
  * in penal greys, the coat and trousers a washed-out grey wool with broad darker bands round the legs and sleeves, a
    white cloth patch on the chest where the number was stencilled;
  * no gloves: the hands bare, raw at the wrists where the irons were;
  * the chest lamp, theirs now (it's how the crew find each other).

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh survivor_prisoner
    PRISONER_PREVIEW=1 tools/models/build.sh survivor_prisoner   # the high figure to out/review, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402

import cook  # noqa: E402
import crewfigure  # noqa: E402
from overbake import smooth01  # noqa: E402


def bands(p):
    """Broad bands round the legs and the forearms: the penal issue's darker stripes."""
    x, z = np.abs(p[:, 0]), p[:, 2]
    legs = (z < 0.95) & (z > 0.12)
    arms = x > 0.3
    stripe = (np.sin(z * 2 * np.pi / 0.16) > 0.35).astype(np.float32)
    return stripe * (legs | arms)


def number(p):
    """The number patch: a white cloth square on the left breast, its stencilled number long since faded."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    return (smooth01(0.0, 0.01, y) * (np.abs(x - 0.09) < 0.055) * (np.abs(z - 1.36) < 0.045)).astype(np.float32)



def scars(p):
    """Where the irons were (note 407): a raw band round each wrist, ragged at its edges, between the cuff and the hand."""
    x, z = np.abs(p[:, 0]), p[:, 2]
    ring = smooth01(0.655, 0.67, x) * smooth01(0.72, 0.705, x) * (np.abs(z - 1.44) < 0.09)
    return (ring * np.clip(cook.noise_np(p, 71, 90) * 0.6 + 0.7, 0, 1)).astype(np.float32)


def grade(base, atlas, face):
    grey = base.mean(-1, keepdims=True)
    # Washed out toward grey: what's left of the issue's dye.
    out = np.where(face[..., None], base, (base * 0.3 + grey * 0.7 * np.array([0.98, 1.0, 1.02], np.float32)) * 1.5)
    b = atlas.maps["bands"][..., None]
    out = out * (1 - 0.45 * b)
    n = atlas.maps["number"][..., None]
    out = out * (1 - n) + np.array([0.46, 0.45, 0.42], np.float32) * n
    # The irons' scars: raw red-brown, crusted darker at their middle.
    sc = atlas.maps["scars"][..., None]
    out = out * (1 - 0.8 * sc) + np.array([0.34, 0.12, 0.09], np.float32) * (0.8 * sc)
    return out


DRESS = {
    "crew_atlas.coat": ("wool", 8.0, (0.95, 0.96, 0.96), 0.9, 3),
    "crew_atlas.sleeve": ("wool", 8.0, (0.95, 0.96, 0.96), 0.9, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.88, 0.89, 0.9), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.82, 0.66, 0.58), 0.6, 0),
    "crew_atlas.cap": ("wool", 10.0, (0.36, 0.37, 0.38), 0.9, 2),
    "crew_atlas.satchel": ("wool", 9.0, (0.42, 0.42, 0.4), 0.9, 0),
}

crewfigure.build("survivor_prisoner", crewfigure.Style(
    dress=DRESS, masks={"bands": bands, "number": number, "scars": scars}, grade=grade, preview="PRISONER_PREVIEW", figure="bare", hats=False,
    what="a freed prisoner in penal greys, modelled over tools/blender/crew.py"))
