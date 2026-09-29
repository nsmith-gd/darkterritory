"""A human skull (Khronos ScatteringSkull, CC0), decimated from 189k triangles to a prop and yellowed like bone left
out: for the bone piles along the corrupted track, the Soot children's hoard, a shelf in a dead house."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
import cook

cook.reset()
objs = cook.load("khronos-skull", "Models/ScatteringSkull/glTF-Binary/ScatteringSkull.glb")
cook.fit(objs, height=0.2)


def bone(d):
    # The scan's material is plain subsurface white: give it old bone's yellow-grey, darker where it's dirty.
    lum = d.mean(-1, keepdims=True)
    return lum * np.array([0.52, 0.45, 0.33], np.float32) + np.array([0.02, 0.016, 0.01], np.float32)
cook.finish("skull", objs, budget=2400, grade=bone, grime=0.7)
