"""SLEEPER (GDD §21 forward, App. A.2 · vibration): "lie across the rail, shaped like ties. Invisible until lit."

A person stretched to a tie. Ligier Richier's transi (Three D Scans; the same cadaver the Hollow is made from) laid on
its back across the track and drawn out to 2.8 m by 0.35 by 0.28, the brown-black of old creosoted timber with the
grain running along it. At a glance, in the lamp, it is a sleeper. Closer, the "grain" is the scan: the ribs open
across its width, the flayed arms, the face at one end with the raised arm reaching on past it and the feet at the
other. "You can still tell what they used to be" (§26.5).

Pivot at the middle of its underside, lying along X (across the rail). Chain rig: root + seg_01..seg_10. Clips as
before: dormant (still, loop), writhe (the telegraph when the lamp finds it, loop), lift (arching up as the engine
comes on: braced, never fleeing). Replaces tools/blender's procedural one.

    python3 tools/models/fetch.py gk-transi && tools/models/build.sh sleeper
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
import bmesh  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix  # noqa: E402

import cook  # noqa: E402
from rig import Clip  # noqa: E402

L, DEPTH, HIGH = 2.8, 0.3, 0.22
TIE = (2.62, 0.25, 0.14)  # the timber it has grown into: a standard tie's length, a little narrow

cook.reset()
body = cook.load("gk-transi", "models/threedscans/Le_Transi_De_Rene_De_Chalon.plain.glb")
cook.fit(body, height=2.1)
cook.cut(body, lambda c: c.z > 0.095)  # off its plinth
# Laid on its back along X: its height becomes the length (head to +X), its front faces up.
cook.transform(body, Matrix.Rotation(math.pi / 2, 4, "X") @ Matrix.Rotation(math.pi / 2, 4, "Y"))
lo, hi = cook.bounds(body)
ext = hi - lo
# Drawn out to a tie: stretched along, pressed flat and narrow; the pivot at the middle of its underside.
cook.transform(body, Matrix.Diagonal((L / ext.x, DEPTH / ext.y, HIGH / ext.z, 1)) @ Matrix.Translation(-(lo + hi) / 2))
lo, hi = cook.bounds(body)
# Sunk into the tie's top, so the wood's edge runs straight along under it and the body is its upper half.
cook.move(body, (0, 0, TIE[2] * 0.55 - lo.z))


def grain(p):
    # Creosoted timber's grain, running along it; checks (splits) here and there across it.
    g = cook.noise_np(p * np.array([1.2, 70, 40]), 81) * 0.6 + cook.noise_np(p * np.array([3, 220, 120]), 82) * 0.4
    return np.clip(g * 0.5 + 0.5, 0, 1)


def checks(p):
    n = cook.noise_np(p * np.array([9, 3, 30]), 83)
    return np.clip((n - 0.45) * 5, 0, 1)


def paint(base, ao, m):
    # Old sleeper-brown, darker in the grain and black in the checks; the deep places (between the ribs, the open
    # chest, round the face) nearly black, so the body shows in the lamp only as the relief of the "wood".
    wood = np.array([0.075, 0.05, 0.032], np.float32) * (0.55 + 0.6 * m["grain"][..., None])
    wood = wood * (1 - 0.8 * m["checks"][..., None])
    return wood * (0.15 + 0.85 * ao ** 2.2)[..., None]


low = cook.bake_down(body, "sleeper", 2800, colour=(1, 1, 1), size=1024, masks={"grain": grain, "checks": checks}, paint=paint)[0]
layers = cook.bake_layers("sleeper", [low], family="creature", source_ids=["gk-transi"])
cook._merge_index("sleeper", layers)

# The tie: the library's old sleeper timber, cut along its length so the chain can bend it.
tie = cook.cube((0, 0, TIE[2] / 2), (TIE[0] / 2, TIE[1] / 2, TIE[2] / 2), "tie")
bm = bmesh.new()
bm.from_mesh(tie.data)
along_x = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).x) > 0.5]
bmesh.ops.subdivide_edges(bm, edges=along_x, cuts=19, use_grid_fill=True)
bm.to_mesh(tie.data)
bm.free()
# Its ends split and rotted away a little, not sawn square.
def rot(p):
    t = max(0.0, (abs(p.x) - (TIE[0] / 2 - 0.3)) / 0.3)  # 0 along the middle, 1 at the ends
    k = 1 - 0.35 * t * (0.5 + 0.5 * cook.noise3(p, 84, 9.0))
    return (p.x, p.y * k, p.z * (1 - 0.3 * t * (0.5 + 0.5 * cook.noise3(p, 85, 7.0))))


cook.deform([tie], rot)
tie.data.materials.append(cook.library_material("wood_sleeper", 0.05))
uv = tie.data.uv_layers.new(name="UVMap")
for poly in tie.data.polygons:
    n = poly.normal
    for li in poly.loop_indices:
        p = tie.data.vertices[tie.data.loops[li].vertex_index].co
        a = (p.y, p.z) if abs(n.x) > 0.5 else (p.x, p.z) if abs(n.y) > 0.5 else (p.x, p.y)
        uv.data[li].uv = (a[0], a[1])

# ----------------------------------------------------------------------------------------------------------------
# The chain along it, as the procedural sleeper had: bones point +X at mid height.
N = 10
bones = [("root", None, (0, 0, 0), (0, 0.2, 0))]
for i in range(N):
    x0 = -L / 2 + L * i / N
    bones.append((f"seg_{i + 1:02d}", "root" if i == 0 else f"seg_{i:02d}", (x0, 0, HIGH / 2), (x0 + L / N, 0, HIGH / 2)))
seg = [b[0] for b in bones[1:]]

# Clips. Bones point +X; about Y, + tips a segment's far end down (- lifts it), about Z, + swings it toward +Y.
dormant = Clip("dormant")
dormant.key(0, {}, "CONSTANT")
dormant.key(60, {}, "CONSTANT")

# Writhe: a slow wave through it, out of step side to side and up and down, with a catch (a held frame) each half
# cycle: not a snake's even ripple, something that's forgotten how to move.
writhe = Clip("writhe")
for f in range(0, 48, 3):
    t = f / 48
    pose = {}
    for i, n in enumerate(seg):
        ph = 2 * math.pi * (t * 2 - i / N * 1.5)
        pose[n] = (0, 5 * math.sin(ph) * (0.4 if i in (0, N - 1) else 1.0), 3.5 * math.sin(ph * 0.5 + i))
    pose["root@loc"] = (0, 0, 0.03 + 0.02 * math.sin(2 * math.pi * t * 2))
    writhe.key(f, pose, "CONSTANT" if f % 12 == 9 else "LINEAR")
writhe.close(48)

# Lift: the middle arches up off the ballast, the ends dug in, braced for the engine. It holds.
# First segment rises at 16 degrees; each after bends 32/9 back down, so the far end comes back to the ground.
ARCH = [-9.0] + [18.0 / 9] * 9
lift = Clip("lift", loop=False)
lift.key(0, {}, "LINEAR")
lift.key(4, {n: (0, a * 0.6, 0) for n, a in zip(seg, ARCH)} | {"root@loc": (0, 0, 0.02)}, "CONSTANT")
lift.key(7, {n: (0, a, 2 * math.sin(i)) for i, (n, a) in enumerate(zip(seg, ARCH))} | {"root@loc": (0, 0, 0.03)}, "LINEAR")
lift.key(16, {n: (0, a * 1.1, -2 * math.sin(i)) for i, (n, a) in enumerate(zip(seg, ARCH))} | {"root@loc": (0, 0, 0.04)},
         "LINEAR")
lift.key(24, {n: (0, a * 1.05, 0) for n, a in zip(seg, ARCH)} | {"root@loc": (0, 0, 0.04)}, "LINEAR")

cook.rig_creature("sleeper", [low, tie], bones, [dormant, writhe, lift], skeleton="SK_Chain")
