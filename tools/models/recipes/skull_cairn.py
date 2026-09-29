"""A cairn of skulls on a low mound for the corrupted lineside (the photoscanned skull, Khronos, CC0): seven, heaped,
turned every way, the lowest half sunk in the earth. What the Soot children keep, or what's left of a work gang."""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from mathutils import Matrix, Vector
import cook

cook.reset()
skulls = []
places = [(0, 0, 0.02, 10, 0), (0.2, 0.06, 0.0, -30, 15), (-0.19, 0.09, -0.01, 50, -10), (0.05, -0.2, -0.02, 150, 20),
          (-0.06, 0.2, 0.0, -110, 5), (0.02, 0.04, 0.17, 25, -25), (0.12, -0.05, 0.14, -60, 35)]
for i, (x, y, z, yaw, tilt) in enumerate(places):
    s = cook.load("khronos-skull", "Models/ScatteringSkull/glTF-Binary/ScatteringSkull.glb")
    cook.fit(s, height=0.2, floor=False)
    cook.decimate(s, 1100)
    cook.transform(s, Matrix.Translation(Vector((x, y, z + 0.08))) @ Matrix.Rotation(math.radians(yaw), 4, "Z")
                   @ Matrix.Rotation(math.radians(tilt), 4, "X"))
    skulls += s
cook.finish("skull_cairn", skulls, budget=7800, grime=0.9,
            grade=lambda d: d.mean(-1, keepdims=True) * cook.np.array([0.3, 0.26, 0.2], cook.np.float32) + 0.006)
