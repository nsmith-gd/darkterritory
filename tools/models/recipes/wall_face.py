"""FACE IN THE WALL. A tunnel's brick lining bulged and split, and pushing out through it a human face: the Infinite
head scan of Lee Perry-Smith (CC BY 3.0), a little too long, pale as plaster against the soot-black brick, tar run from the
eyes. The headlamp sweeps over it and it's gone. 2 x 2.4 m of wall (the library's brick_soot, so it meets the lining),
facing the model's front; the scene sets it into the bore's side wall."""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import numpy as np
from mathutils import Vector
import cook

cook.reset()
head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
cook.fit(head, height=1.0, floor=False)
# Just the face: everything behind the ears and below the jaw goes into the wall.
lo, hi = cook.bounds(head)
depth = hi.y - lo.y
cook.cut(head, lambda c: c.y > lo.y + depth * 0.0 and c.z > lo.z + (hi.z - lo.z) * 0.18)
lo, hi = cook.bounds(head)
# Which way it looks: the nose is its furthest point from the middle, in the horizontal plane.
mid = (lo + hi) / 2
pts = [o.matrix_world @ v.co for o in head for v in o.data.vertices]
nose = max(pts, key=lambda p: (Vector((p.x, p.y, 0)) - Vector((mid.x, mid.y, 0))).length * (1 if abs(p.z - mid.z) < 0.15 else 0))
import math
from mathutils import Matrix
ang = math.atan2(nose.x - mid.x, nose.y - mid.y)
cook.transform(head, Matrix.Rotation(ang, 4, "Z"))
# Uncanny: a little too long, the jaw hanging.
lo, hi = cook.bounds(head)
cook.deform(head, lambda p: Vector((p.x * 0.95, p.y, (p.z - lo.z) * 1.22 + lo.z - (0.05 if p.z < lo.z + (hi.z - lo.z) * 0.32 else 0))))
cook.fit(head, height=0.74, floor=False)
lo, hi = cook.bounds(head)
# Pushed half out through the wall (the wall at y = 0, the face coming out toward +Y), the nose 0.21 m proud; what's
# behind the wall is cut away, so nothing of it shows from the far side.
cook.move(head, (0, -hi.y + 0.24, 1.45))
cook.cut(head, lambda c: c.y > 0.03)
eyes = np.array([0, 0.14, 1.45 + 0.74 * 0.13], np.float32)


def tears(p):
    dx = np.minimum(np.abs(p[:, 0] - 0.06), np.abs(p[:, 0] + 0.06)) + cook.noise_np(p * np.array([0, 0, 9]), 5) * 0.012
    below = eyes[2] - p[:, 2]
    return np.clip(1 - dx / (0.012 + 0.02 * np.clip(below, 0, 1)), 0, 1) * (below > -0.02) * np.clip(1 - below / 0.5, 0, 1)


def paint(base, ao, m):
    t = m["tears"][..., None]
    return base * (0.3 + 0.7 * ao ** 1.5)[..., None] * (1 - 0.9 * t) + np.array([0.003, 0.002, 0.002], np.float32) * t


face = cook.bake_down(head, "wall_face", 4200, colour=(0.075, 0.07, 0.065), masks={"tears": tears}, paint=paint)
wall = cook.grid(2.0, 2.4, 28, cook.library_material("brick_soot", 0.1), tile=1.2, name="wall")
cook.move([wall], (0, 0, 1.2))
# The wall bulges out round the face, as if the brick were skin stretched over it: pushed forward toward the face,
# most at its edge, the courses bending round it.
def bulge(p):
    # A ring round the face, highest just outside its edge, as if the brick were stretched over its skull.
    r = ((p.x ** 2 + ((p.z - 1.45) * 0.85) ** 2) ** 0.5)
    ring = max(0.0, 1 - abs(r - 0.38) / 0.45) ** 1.5
    lift = 0.13 * ring * (1 + 0.25 * cook.noise3(p, 9, 7)) * (0.35 if r < 0.2 else 1)
    return Vector((p.x, lift, p.z))
cook.deform([wall], bulge)
cook.finish("wall_face", face + [wall], budget=6000)
