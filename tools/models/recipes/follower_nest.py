"""The Follower's nest (GDD v1.1 App. A.6: it rides a crewmate's back into the car with the most loot, "builds a nest there
over ~60 s, and eats that car's loot"; the crew bludgeons it). A heap of the car's own freight, a crate stoved in and a
split sack, grown over by the thing's husk: grey-pink crust in lobes, swollen nodules feeding on what's under them, pale
strands tying it to the floor, a wet hollow in its top where the Follower sits. About 1.4 m across and 0.7 high; its
origin is the middle, on the floor. The scene scales it with the nest's progress (Enemy.Extra2).

Axes: Blender +Z up. Modelled here and baked (tools/models/make).

    tools/models/build.sh follower_nest
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import bpy  # noqa: E402
import cook  # noqa: E402
import make  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402


def materials():
    return {
        "crate": make.lib("wood_crate", 2.0, tint=(0.75, 0.66, 0.55), rough=0.7),
        "sack": make.lib("wool", 4.0, tint=(0.75, 0.66, 0.5), rough=0.95),
        "grain": make.lib("ground_red_clay", 4.0, tint=(1.1, 0.95, 0.7), rough=0.9),
        "crust": make.lib("fungal_crust", 2.0, tint=(0.85, 0.72, 0.7), rough=0.6),
        "flesh": make.lib("flesh", 3.0, tint=(0.8, 0.55, 0.52), rough=0.35),
        "silk": make.lib("sac", 2.0, tint=(1.0, 0.95, 0.9), rough=0.15),
    }


def blob(centre, radii, material, name="blob", n=16, low=10, rot=None, lumpy=0.0, seed=0):
    out = None
    for segs, keep in ((low, bool(low)), (n, True)):
        if not keep:
            continue
        bpy.ops.mesh.primitive_uv_sphere_add(segments=segs, ring_count=max(4, segs // 2), radius=1)
        o = bpy.context.view_layer.objects.active
        if lumpy and segs == n:
            for v in o.data.vertices:
                v.co *= 1 + lumpy * cook.noise3(v.co * 2.5, seed, 3)
        o.data.transform(Matrix.Diagonal((*radii, 1)))
        if rot is not None:
            o.data.transform(rot)
        o.data.transform(Matrix.Translation(Vector(centre)))
        o.data.materials.clear()
        o.data.materials.append(material)
        if segs == low and segs != n:
            o.name = name + "_low"
            make.LOW.append(o)
        else:
            out = make._finish(o, material, 0, 1, name)
    return out


def nest(m):
    p = []
    # What it's built on: a crate stoved in on its side, a split sack spilling grain, a second crate under the heap.
    p.append(make.box((-0.3, 0.15, 0.22), (0.3, 0.26, 0.22), m["crate"], bevel=0.02, name="crate", rot=Matrix.Rotation(0.35, 4, "Z")))
    p.append(make.box((0.35, -0.2, 0.2), (0.24, 0.24, 0.2), m["crate"], bevel=0.02, name="crate_b", rot=Matrix.Rotation(-0.5, 4, "Z") @ Matrix.Rotation(0.2, 4, "X")))
    p.append(blob((0.3, 0.42, 0.13), (0.34, 0.2, 0.13), m["sack"], "sack", rot=Matrix.Rotation(0.8, 4, "Z")))
    p.append(blob((0.55, 0.62, 0.02), (0.28, 0.16, 0.03), m["grain"], "spill", n=12, low=8))
    # Grown over all of it: lobes of crust, heaped to a crown, a hollow in its top.
    for k, (a, r, z, s) in enumerate(((0.0, 0.25, 0.45, 0.42), (1.3, 0.4, 0.3, 0.36), (2.5, 0.35, 0.34, 0.38), (3.7, 0.45, 0.25, 0.3),
                                      (4.8, 0.3, 0.4, 0.35), (5.6, 0.5, 0.18, 0.28))):
        c = (math.cos(a) * r, math.sin(a) * r, z)
        p.append(blob(c, (s, s * 0.9, s * 0.7), m["crust"], f"lobe_{k}", lumpy=0.18, seed=80 + k))
    p.append(blob((0.05, 0.0, 0.62), (0.2, 0.18, 0.06), m["flesh"], "hollow", n=14, low=8))
    # Nodules swollen where it's feeding, glistening.
    for k in range(9):
        a = k * 2.399
        r = 0.2 + 0.35 * ((k * 37) % 10) / 10
        z = 0.5 - 0.35 * r
        p.append(blob((math.cos(a) * r, math.sin(a) * r, z), (0.07 + 0.02 * (k % 3),) * 3, m["flesh"], "nodule", n=10, low=0))
    # Pale strands tying it down to the floor all round.
    for k in range(10):
        a = k * math.tau / 10 + 0.2
        top = Vector((math.cos(a) * 0.45, math.sin(a) * 0.45, 0.35))
        foot = Vector((math.cos(a) * 0.85, math.sin(a) * 0.85, 0.01))
        p.append(make.cyl(top, foot, 0.01, m["silk"], n=6, bevel=0, name="strand", low=4))
    return p


cook.reset()
make.LOW.clear()
make.USED.clear()
make._mats.clear()
parts = [o for o in nest(materials()) if o is not None]
low = cook.bake_down(parts, "follower_nest_low", 2200, colour=None, size=1024, cage=0.01, reach=0.04, low=list(make.LOW))[0]
cook.finish("follower_nest", [low], budget=2200, grime=0.5, made=make.provenance("follower_nest", "the Follower's nest"))
