"""LE TRANSI: the cadaver saint. Ligier Richier's 1547 transi of René de Chalon (scanned by Three D Scans, no copyright
restrictions): a flayed, half-rotted corpse standing on its tomb, raising its own heart to the sky. Here it stands in
the dead towns' churchyards and at the stations, 1.95 m, limestone gone grey-green, the stone washed into black streaks
by a century of rain and soot, the soot pooled at its feet. Baked down from 990k triangles to a game mesh wearing the
scan's detail as normal and occlusion maps."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
import cook

cook.reset()
objs = cook.load("gk-transi", "models/threedscans/Le_Transi_De_Rene_De_Chalon.plain.glb")
cook.fit(objs, height=1.95)


def streaks(p):
    # Rain and soot running down the stone: narrow in x and y, long in z, heavier higher up where it starts.
    n = cook.noise_np(p * np.array([26, 26, 1.4]), 11) * 0.7 + cook.noise_np(p * np.array([60, 60, 3]), 12) * 0.3
    return np.clip((n - 0.18) * 3, 0, 1) * np.clip(0.35 + p[:, 2] / 1.95, 0, 1)


def foot(p):
    return np.clip(1 - p[:, 2] / 0.45, 0, 1) ** 1.5


def paint(base, ao, m):
    stained = base * (1 - 0.8 * m["streaks"])[..., None] + np.array([0.004, 0.004, 0.003]) * m["streaks"][..., None]
    moss = np.array([0.018, 0.022, 0.012], np.float32)
    return stained * (1 - 0.7 * m["foot"])[..., None] + moss * m["foot"][..., None]


low = cook.bake_down(objs, "transi", 7800, colour=(0.2, 0.2, 0.175), masks={"streaks": streaks, "foot": foot}, paint=paint)
cook.finish("transi", low, budget=7800)
