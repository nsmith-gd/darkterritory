"""THE HUSK: a crewman gone wrong (GDD §26.5, "the strongest monsters are the ones where you can still tell what they
used to be"), for the Climbers (App. A.4: "drawn out thin, soot-black") and the Deadman (A.5: "a crewman, or was").

The crew figure (tools/models/crewfigure), with the crew's own rig and clips, so it runs, climbs, walks the roofs and
crouches in a car as they do, but:
  * the face has shrunk onto the skull: the cheeks and temples sunk, the eyes gone back into their sockets, the chin
    drawn down; the skin dead grey-green, black tar welling from the eyes and running down from the mouth;
  * the clothes are burned through in ragged holes, char at the edges and raw meat in them, the coat hanging in rags
    at the hem, everything soot-black and oily;
  * the chest lamp is dead: glass gone brown-black (a lit lamp is how the crew find each other; this isn't one of them).

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh husk
    HUSK_PREVIEW=1 tools/models/build.sh husk   # renders the high figure to out/review/husk-high-*.png, no bake
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np  # noqa: E402

import cook  # noqa: E402
import crewfigure  # noqa: E402
from overbake import bell, smooth01  # noqa: E402

EYES = []


def shrink(head, centre):
    """The face shrunk onto the skull, from the scan's own landmarks (the nose tip, the eyes above and behind it)."""
    for o in head:
        me = o.data
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        tip = co[co[:, 1].argmax()].copy()
        eyes = [np.array([sx * 0.042, tip[1] - 0.042, tip[2] + 0.022], np.float32) for sx in (-1, 1)]
        # The sockets' mask sits where the eyes end up, pushed back into the skull below.
        EYES[:] = [e - np.array([0, 0.022, 0], np.float32) for e in eyes]
        x, y, z = co[:, 0], co[:, 1], co[:, 2]
        front = smooth01(tip[1] - 0.09, tip[1] - 0.05, y)
        # Cheeks sunk under the cheekbones, temples hollowed.
        cheek = bell((np.abs(x) - 0.052) / 0.02) * bell((z - (tip[2] - 0.035)) / 0.025) * front
        co[:, 0] -= np.sign(x) * 0.016 * cheek
        co[:, 1] -= 0.008 * cheek
        temple = bell((np.abs(x) - 0.07) / 0.012) * bell((z - (tip[2] + 0.05)) / 0.02)
        co[:, 0] -= np.sign(x) * 0.01 * temple
        # The eyes gone back into the skull.
        for e in eyes:
            d = np.linalg.norm(co - e, axis=1)
            co[:, 1] -= 0.022 * bell(d / 0.02) * front
        # The nose rotted back toward the skull, its tip gone.
        nose = bell(np.linalg.norm((co - tip) * np.array([1.0, 0.6, 1.0], np.float32), axis=1) / 0.028)
        co[:, 1] -= 0.018 * nose
        # The chin drawn down: the jaw hanging a little long.
        jaw = smooth01(tip[2] - 0.045, tip[2] - 0.075, z) * front
        co[:, 2] -= 0.02 * jaw
        me.vertices.foreach_set("co", co.ravel())
        me.update()


def rot(p):
    """Where the clothes are burned through: patches, most at the hem and the cuffs."""
    n = cook.noise_np(p, 31, 7) * 0.7 + cook.noise_np(p, 32, 19) * 0.3
    hem = smooth01(0.8, 0.5, p[:, 2]) * 0.35 + smooth01(0.55, 0.7, np.abs(p[:, 0])) * 0.3
    return smooth01(0.25, 0.5, n + hem)


def torn(p, n):
    """The holes sunk into the cloth, their edges ragged."""
    r = rot(p)
    return -0.02 * smooth01(0.5, 0.8, r) + 0.006 * bell((r - 0.5) / 0.1) * cook.noise_np(p, 33, 90)


def eyes(p):
    if not EYES:
        return np.zeros(len(p), np.float32)
    out = np.zeros(len(p), np.float32)
    for e in EYES:
        out = np.maximum(out, bell(np.linalg.norm((p - e) * np.array([1.0, 0.5, 1.2], np.float32), axis=1) / 0.024))
        # The tears: tar run down from each eye over the cheek.
        run = bell((p[:, 0] - e[0] * 1.05) / 0.005) * smooth01(e[2] + 0.005, e[2] - 0.02, p[:, 2]) * smooth01(e[2] - 0.11, e[2] - 0.07, p[:, 2])
        out = np.maximum(out, run * smooth01(e[1] - 0.04, e[1], p[:, 1]))
    return out


def tar(p):
    """Tar out of the mouth and down the front, in runs."""
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    runs = np.maximum(0, np.sin(x * 140 + cook.noise_np(p * np.array([1, 1, 0.2]), 34, 8) * 3)) ** 3
    front = smooth01(0.0, 0.08, y) * bell(x / 0.09)
    return np.clip(runs * front * smooth01(0.7, 1.2, z) * smooth01(1.62, 1.5, z) * 1.5, 0, 1)


def grade(base, atlas, face):
    soot = np.array([0.012, 0.011, 0.01], np.float32)
    grey = base.mean(-1, keepdims=True)
    # Soot-black, oily: the whole figure pushed down and toward grey.
    body = (base * 0.35 + grey * 0.65) * 0.42
    dead = (base.mean(-1, keepdims=True) * np.array([0.72, 0.8, 0.76], np.float32) + 0.012) * 0.8
    out = np.where(face[..., None], dead, body)
    r = atlas.maps["rot"]
    meat = np.array([0.16, 0.035, 0.03], np.float32)
    char = np.array([0.008, 0.007, 0.006], np.float32)
    out = np.where((r > 0.62)[..., None], meat * (0.6 + 0.4 * grey / max(float(grey.mean()), 1e-3)), out)
    edge = np.clip(1 - np.abs(r - 0.55) / 0.12, 0, 1)
    out = out * (1 - edge)[..., None] + char * edge[..., None]
    black = np.array([0.006, 0.005, 0.005], np.float32)
    for m in ("eyes", "tar"):
        # Hard-edged: tar is black, not a grey smudge (a mask baked soft from the high's vertices tops out below one).
        k = smooth01(0.06, 0.45, atlas.maps[m])[..., None]
        out = out * (1 - k) + black * k
    return out * (1 - 0.1) + soot * 0.1


DRESS = {
    "crew_atlas.coat": ("coat_oilskin", 7.0, (0.45, 0.45, 0.4), 0.5, 3),
    "crew_atlas.sleeve": ("coat_oilskin", 7.0, (0.45, 0.45, 0.4), 0.5, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.25, 0.26, 0.28), 0.9, 3),
    "crew_atlas.gloves": ("skin", 5.0, (0.55, 0.5, 0.48), 0.6, 0),
    "crew_atlas.lamp": ("glass_dirty", 8.0, (0.12, 0.09, 0.07), 0.3, 0),
}
SHAPES = {k: torn for k in ("crew_atlas.coat", "crew_atlas.sleeve", "crew_atlas.trouser", "crew_atlas.scarf", "crew_atlas.cap")}

crewfigure.build("husk", crewfigure.Style(
    head=shrink, dress=DRESS, shapes=SHAPES, masks={"rot": rot, "eyes": eyes, "tar": tar}, grade=grade,
    preview="HUSK_PREVIEW", lamp=False, mask="torn", figure="bare",
    what="a crewman gone wrong, modelled over tools/blender/crew.py"))
