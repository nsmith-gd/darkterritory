"""The hand lantern (the crew's lamp, the cars' hanging lamps): the lantern head cut from the Khronos Lantern post
(CC0) and scaled down to a hand lamp, 0.36 m, its ring at the top. Its glass keeps the source's emissive mask, which
the engine lights by glow."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
import cook

cook.reset()
objs = cook.load("khronos-lantern", "Models/Lantern/glTF-Binary/Lantern.glb")
keep = [o for o in objs if "Lantern" in o.name and "Body" not in o.name and "Chain" not in o.name]
cook.delete([o for o in objs if o not in keep])
cook.fit(keep, height=0.36)
lo, hi = cook.bounds(keep)


def sooted(d):
    return d * np.array([0.8, 0.74, 0.66], np.float32)
# The flame in the glass (a little under half way up), and the ring it hangs by at the top.
cook.finish("hand_lantern", keep, budget=2400, grade=sooted, grime=0.6,
            sockets={"lamp": (0, 0, hi.z * 0.42), "hang": (0, 0, hi.z)})
