"""Kitbash: a lamp post whose lantern holds a human skull where the flame should be (the Khronos Lantern and
ScatteringSkull, both CC0). For the fortress gate and the dead stations: the first thing that tells you this world is
wrong. The skull sits in the lantern's cage, lit from behind by the glass's glow, turned a little to look down at
whoever passes under it."""
import math, os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
from mathutils import Matrix, Vector
import cook

cook.reset()
post = cook.load("khronos-lantern", "Models/Lantern/glTF-Binary/Lantern.glb")
cook.fit(post, height=3.4)
cook.deform(post, lambda p: (p.x - 0.03 * p.z * p.z / 3.4, p.y, p.z))
lantern = [o for o in post if "Lantern" in o.name and "Body" not in o.name and "Chain" not in o.name][0]
# No glass: the cage is open, so the skull inside is what you see.
cook.cut_emissive([lantern])
lo, hi = cook.bounds([lantern])
# A big cage lantern, not a street lamp's: scaled up from the point it hangs by, so the chain still meets it.
top = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, hi.z))
cook.transform([lantern], Matrix.Translation(top) @ Matrix.Scale(1.7, 4) @ Matrix.Translation(-top))
lo, hi = cook.bounds([lantern])
centre = (lo + hi) / 2
skull = cook.load("khronos-skull", "Models/ScatteringSkull/glTF-Binary/ScatteringSkull.glb")
cook.fit(skull, size=min(hi.x - lo.x, hi.y - lo.y) * 0.8, floor=False)
# Looking down and a little aside, at whoever walks under the arm.
cook.transform(skull, Matrix.Rotation(math.radians(20), 4, "X") @ Matrix.Rotation(math.radians(160), 4, "Z"))
cook.move(skull, centre + Vector((0, 0, -(hi.z - lo.z) * 0.08)))
cook.decimate(skull, 2200)


cook.finish("skull_lantern", post + skull, budget=7000, grade=cook.weathered, grime=0.6,
            sockets={"lamp": centre + Vector((0, 0, (hi.z - lo.z) * 0.12))})
