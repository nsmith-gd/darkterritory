"""A defaced saint for the dead towns: Thorvaldsen's Mercury (scanned by the Virtual Museums of Malopolska, CC0),
marble kept in its own scanned colour, but its face gouged away into a pit, the stone around it swollen with growth
as if something grew out of the hole, and tar weeping from it down the chest and the drapery. 1.8 m."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
from mathutils import Vector
import cook

cook.reset()
objs = cook.load("gk-mercury", "models/mercury-about-to-kill-argos/scene.plain.glb")
cook.fit(objs, height=1.8)
top = cook.top_point(objs, 0.004)
# The face is where the nose is: of the vertices at nose height (below the winged hat's brim), the one that stands
# furthest out from the head's axis.
centre = top + Vector((0, 0, -0.17))
pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
ring = [p for p in pts if -0.03 < p.z - centre.z < 0.03 and (Vector((p.x, p.y, 0)) - Vector((centre.x, centre.y, 0))).length < 0.16]
nose = max(ring, key=lambda p: (Vector((p.x, p.y, 0)) - Vector((centre.x, centre.y, 0))).length)
out = Vector((nose.x - centre.x, nose.y - centre.y, 0)).normalized()
head = centre
face = centre + out * 0.06 + Vector((0, 0, 0.01))
print(f"[dbg] top {tuple(round(x, 3) for x in top)} nose {tuple(round(x, 3) for x in nose)} out {tuple(round(x, 2) for x in out)}")


def deface(p):
    d = (p - face).length
    if d < 0.11:
        # Gouged in: pushed back into the skull, deeper at the middle.
        return p + (head - p).normalized() * (0.11 - d) * 0.8
    if d < 0.3:
        # Swollen round the wound: growth pushing the stone out.
        k = cook.noise3(p, 7, 38) * 0.6 + cook.noise3(p, 8, 90) * 0.4
        return p + (p - face).normalized() * max(0.0, k + 0.3) * (0.3 - d) * 0.22
    return p


cook.deform(objs, deface)
f = np.array(face, np.float32)


def tar(p):
    # From the wound, running down: narrow wandering streaks, fading with the fall.
    dx = np.abs(p[:, 0] - f[0] + cook.noise_np(p * np.array([0, 0, 3]), 4) * 0.05)
    below = np.clip((f[2] - p[:, 2]) / 1.3, 0, 1)
    stream = np.clip(1 - dx / (0.05 + 0.1 * below), 0, 1) * (p[:, 2] < f[2] + 0.02)
    near = np.clip(1 - np.linalg.norm(p - f, axis=1) / 0.16, 0, 1)
    return np.maximum(stream * (1 - below) * 0.9, near)


def paint(base, ao, m):
    t = m["tar"][..., None]
    return base * (0.4 + 0.6 * ao)[..., None] * (1 - 0.95 * t) + np.array([0.004, 0.002, 0.0015], np.float32) * t


low = cook.bake_down(objs, "mercury_defaced", 7800, colour=None, masks={"tar": tar}, paint=paint)
cook.finish("mercury_defaced", low, budget=7800)
