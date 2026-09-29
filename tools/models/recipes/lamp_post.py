"""A lamp post for the dead towns, the fortress and the home station: the Khronos Lantern (CC0), leaning, its timber
gone grey and its lantern black with soot. 3.4 m, the pivot at its foot, the arm reaching toward -Y (the engine's
-Z: turn it to hang over what it lights)."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
import cook

cook.reset()
objs = cook.load("khronos-lantern", "Models/Lantern/glTF-Binary/Lantern.glb")
cook.fit(objs, height=3.4)
# Leaning a few degrees off true: nobody's straightened it since.
cook.deform(objs, lambda p: (p.x + 0.035 * p.z * p.z / 3.4, p.y, p.z))
head = [o for o in objs if "Lantern" in o.name and "Body" not in o.name and "Chain" not in o.name]
lo, hi = cook.bounds(head)
lamp = (lo + hi) / 2 + cook.Vector((0, 0, -(hi.z - lo.z) * 0.1))


cook.finish("lamp_post", objs, budget=4500, grade=cook.weathered, grime=0.6, sockets={"lamp": lamp})
