"""A tomb effigy for the churchyards: the reclining figure scanned by Three D Scans (Hosmer; no copyright restrictions)
laid on a stone slab, 2.1 m long, gone black with soot along every fold and green where the rain pools. Baked down from
1.35M triangles; the slab is baked with it, so it shares the figure's stone."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
import cook

cook.reset()
objs = cook.load("gk-hosmer", "models/threedscans/Hosmer.plain.glb")
cook.fit(objs, size=1.85)
lo, hi = cook.bounds(objs)
cook.move(objs, (0, 0, 0.55))
slab = cook.cube((0, 0, 0.3), (0.55, 1.08, 0.3), "slab")
cook.rotate(objs + [slab], 90)


def pooling(p):
    return np.clip((0.95 - p[:, 2]) * 2.2, 0, 1) * np.clip(cook.noise_np(p * 7, 3) + 0.4, 0, 1)


def paint(base, ao, m):
    soot = base * (0.35 + 0.65 * ao ** 2.2)[..., None]
    moss = np.array([0.02, 0.026, 0.014], np.float32)
    return soot * (1 - 0.6 * m["pooling"])[..., None] + moss * m["pooling"][..., None]


low = cook.bake_down(objs + [slab], "effigy", 7800, colour=(0.19, 0.19, 0.17), masks={"pooling": pooling}, paint=paint)
cook.finish("effigy", low, budget=7800)
