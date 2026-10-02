"""LIVESTOCK (GDD §19 "livestock: makes noise constantly", §18 the slaughterhouse's "animals are loud"): a frontier
sheep, the kind a slaughterhouse ships: rangy, long-legged, its fleece grown out ragged and grey with soot, a black face
and legs, the ears out sideways. A car of them stands packed down the load side, never still.

0.72 m at the shoulder, 1.15 m nose to rump, head forward (-Z in the engine). SK_Sheep.
Clips (all loops): idle (breathing, an ear flicking, weight shifting), shuffle (a few steps in place, turning a little,
the head swinging to the next one), bleat (the head comes up, the jaw drops, held, again: the noise has to be seen),
startle (a jolt: the head up and back, the whole body bunched, then the jostle).

    blender -b --python tools/blender/sheep.py -- content/art/models/sheep.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over, smoothstep  # noqa: E402

rig.reset()


def quad():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.42, 0.62), (0, -0.25, 0.64)),
        Bone("spine_01", "pelvis", (0, -0.25, 0.64), (0, -0.05, 0.65)),
        Bone("spine_02", "spine_01", (0, -0.05, 0.65), (0, 0.15, 0.66)),
        Bone("chest", "spine_02", (0, 0.15, 0.66), (0, 0.3, 0.67)),
        Bone("neck_01", "chest", (0, 0.3, 0.67), (0, 0.4, 0.74)),
        Bone("neck_02", "neck_01", (0, 0.4, 0.74), (0, 0.46, 0.8)),
        Bone("head", "neck_02", (0, 0.46, 0.8), (0, 0.64, 0.74)),
        Bone("jaw", "head", (0, 0.5, 0.74), (0, 0.63, 0.7)),
        Bone("tail_01", "pelvis", (0, -0.47, 0.62), (0, -0.54, 0.52)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"ear_{side}", "head", (sx * 0.05, 0.5, 0.83), (sx * 0.13, 0.49, 0.82)),
            Bone(f"upperarm_{side}", "chest", (sx * 0.09, 0.24, 0.52), (sx * 0.09, 0.24, 0.32)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.09, 0.24, 0.32), (sx * 0.09, 0.25, 0.08)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.09, 0.25, 0.08), (sx * 0.09, 0.28, 0.01)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.09, -0.36, 0.56), (sx * 0.09, -0.3, 0.33)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.09, -0.3, 0.33), (sx * 0.09, -0.37, 0.12)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.09, -0.37, 0.12), (sx * 0.09, -0.34, 0.01)),
        ]
    return Skeleton("SK_Sheep", b)


sk = quad()
sk.build()
kit = rig.Kit(sk, "sheep")

WOOL = Mat("wool.sheep", hexc("#d8d0c0"), shine=0.04, tint=(2.1, 2.0, 1.8))
DIRTY = Mat("wool.sheep_soot", hexc("#8a8276"), shine=0.04, tint=(1.3, 1.25, 1.12))
FACE = Mat("paint_black.sheep_face", hexc("#1a1715"), shine=0.12)
HOOF = Mat("tar.hoof", hexc("#141210"), shine=0.2)
EYE = Mat("eye.sheep", hexc("#c8a040"), shine=0.6)
MOUTH = Mat("flesh.sheep_mouth", hexc("#3a2424"), shine=0.3)

body = kit.part("body")

SPINE = along("y", [(-0.5, "pelvis"), (-0.3, "pelvis"), (-0.15, "spine_01"), (0.05, "spine_02"), (0.22, "chest"),
                    (0.34, "neck_01"), (0.42, "neck_02"), (0.5, "head")])


def trunk(p):
    w = SPINE(p)
    if p.y < -0.28 and p.z < 0.58 and abs(p.x) > 0.04:
        k = smoothstep(0.58, 0.44, p.z) * 0.6
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    if 0.16 < p.y < 0.34 and p.z < 0.56 and abs(p.x) > 0.05:
        k = smoothstep(0.56, 0.46, p.z) * 0.5
        s = "r" if p.x > 0 else "l"
        w = {b: v * (1 - k) for b, v in w.items()}
        w[f"upperarm_{s}"] = w.get(f"upperarm_{s}", 0) + k
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def fleece(i, j, a, p):
    """A fleece grown out ragged: lumpy, pulled into locks, heavier on the flanks."""
    q = Vector(p)
    lock = 0.03 * noise3(q, 7, 14.0) + 0.016 * noise3(q, 8, 40.0)
    n = Vector((q.x, 0, q.z - 0.6))
    if n.length > 1e-6:
        n.normalize()
    q += n * lock
    return q


def soot(pts, n):
    """Soot settled on the back and the shoulders; the belly and the breech caked dark."""
    c = sum(pts, Vector()) / len(pts)
    return DIRTY if (n.z > 0.7 and noise3(c, 9, 6.0) > 0.1) or c.z < 0.46 or c.y < -0.48 else WOOL


# (y, half width, top, bottom, squareness)
TRUNK = [(-0.56, 0.06, 0.64, 0.5, 1.0), (-0.5, 0.17, 0.72, 0.42, 0.7), (-0.36, 0.22, 0.77, 0.38, 0.65),
         (-0.2, 0.23, 0.79, 0.36, 0.65), (-0.02, 0.235, 0.8, 0.35, 0.65), (0.14, 0.225, 0.8, 0.37, 0.65),
         (0.26, 0.19, 0.8, 0.42, 0.7), (0.34, 0.13, 0.82, 0.52, 0.8), (0.41, 0.08, 0.85, 0.64, 0.9)]


def finer(secs, n=2):
    out = []
    for a, b in zip(secs, secs[1:]):
        for k in range(n):
            t = k / n
            out.append(tuple(x + (y - x) * t for x, y in zip(a, b)))
    out.append(secs[-1])
    return out


body.sections(finer(TRUNK, 3), 20, WOOL, trunk, cap0=True, cap1=True, shape=fleece, fmat=soot)

# The head: long, narrow, Roman-nosed, the black face; the jaw its own piece so the bleat shows.
HEAD = [(0.42, 0.055, 0.86, 0.73, 0.9), (0.48, 0.06, 0.87, 0.72, 0.85), (0.54, 0.05, 0.84, 0.73, 0.85),
        (0.6, 0.04, 0.8, 0.735, 0.9), (0.65, 0.03, 0.77, 0.74, 0.95)]
body.sections(finer(HEAD, 2), 12, FACE, "head", cap1=True)
body.sections([(0.5, 0.035, 0.735, 0.7, 0.9), (0.58, 0.03, 0.735, 0.71, 0.9), (0.64, 0.022, 0.735, 0.72, 1.0)], 8, FACE, "jaw",
              cap1=True)
body.box((0, 0.6, 0.735), (0.022, 0.05, 0.006), MOUTH, "jaw")
# A topknot of fleece on the poll.
body.blob((0, 0.46, 0.88), (0.06, 0.06, 0.04), 8, 4, WOOL, "head")
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    body.slab([(sx * 0.045, 0.5, 0.835), (sx * 0.15, 0.48, 0.83), (sx * 0.16, 0.51, 0.82), (sx * 0.05, 0.53, 0.82)], 0.012, FACE,
              f"ear_{s}", down=(0, 0, -1))
    body.box((sx * 0.05, 0.53, 0.82), (0.008, 0.012, 0.008), EYE, "head")

# Legs: thin, black, knobbed at the knees; hooves.
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    x = sx * 0.09
    front = along("z", [(0.02, "hand_{s}"), (0.08, "hand_{s}"), (0.12, "lowerarm_{s}"), (0.3, "lowerarm_{s}"), (0.36, "upperarm_{s}"),
                        (0.5, "upperarm_{s}")])
    fw = lambda p, f=front: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x, 0.24, 0.5), (x, 0.24, 0.38), (x, 0.24, 0.32), (x, 0.245, 0.2), (x, 0.25, 0.08), (x, 0.26, 0.03)],
              [0.05, 0.035, 0.03, 0.02, 0.018, 0.02], 8, FACE, fw, ref=(0, 1, 0))
    rear = along("z", [(0.02, "foot_{s}"), (0.1, "foot_{s}"), (0.14, "calf_{s}"), (0.3, "calf_{s}"), (0.36, "thigh_{s}"),
                       (0.55, "thigh_{s}")])
    rw = lambda p, f=rear: {k.format(s=s): v for k, v in f(p).items()}  # noqa: E731
    body.tube([(x, -0.34, 0.52), (x, -0.31, 0.4), (x, -0.3, 0.33), (x, -0.34, 0.22), (x, -0.37, 0.12), (x, -0.355, 0.03)],
              [0.055, 0.04, 0.03, 0.022, 0.018, 0.02], 8, FACE, rw, ref=(0, 1, 0))
    for y0, bone in ((0.27, f"hand_{s}"), (-0.345, f"foot_{s}")):
        body.box((x, y0, 0.015), (0.024, 0.03, 0.015), HOOF, bone, taper=(0.85, 0.8))
body.tube([(0, -0.47, 0.62), (0, -0.51, 0.57), (0, -0.54, 0.52)], [0.04, 0.035, 0.02], 6, DIRTY, "tail_01", ref=(0, 0, 1), cap1=True)

# --------------------------------------------------------------------------------------------------------------
# Clips. +X tips a forward-pointing bone's end up (a head raised) and swings a hanging leg forward.
STAND = {"root@loc": (0, 0, 0), "neck_01": (-6, 0, 0), "head": (-4, 0, 0), "ear_r": (0, 0, -10), "ear_l": (0, 0, 10)}

idle = Clip("idle")
for f, (breath, ear, shift) in enumerate(((0, 0, 0), (1, 0, 0.5), (0, 1, 1), (1, 0, 0.5), (0, 0, 0), (1, 1, -0.5), (0, 0, -1), (1, 0, -0.5))):
    p = over(STAND, root__loc=(0.006 * shift, 0, 0.004 * breath), spine_02=(1.5 * breath, 0, 0), chest=(1 * breath, 0, 2 * shift),
             neck_01=(-6 + 2 * breath, 0, 3 * shift), head=(-4, 0, 4 * shift),
             ear_r=(0, 0, -10 - 30 * ear), tail_01=(0, 0, 8 * shift))
    idle.key(f * 10, p, "CONSTANT" if ear else "BEZIER")
idle.close(80)

shuffle = Clip("shuffle")
STEP = {"upperarm": [(0, 0), (0.15, 18), (0.3, 0), (1, 0)], "lowerarm": [(0, 0), (0.15, -30), (0.3, 0), (1, 0)],
        "thigh": [(0, 0), (0.15, -14), (0.3, 0), (1, 0)], "calf": [(0, 0), (0.15, 30), (0.3, 0), (1, 0)]}


def curve(keys, t):
    t %= 1.0
    for (t0, v0), (t1, v1) in zip(keys, keys[1:]):
        if t0 <= t <= t1:
            u = (t - t0) / max(t1 - t0, 1e-9)
            return v0 + (v1 - v0) * u
    return keys[-1][1]


for f in range(0, 48, 4):
    t = f / 48
    p = dict(STAND)
    for side, off in (("r", 0.0), ("l", 0.5)):
        for joint in ("upperarm", "lowerarm"):
            p[f"{joint}_{side}"] = (curve(STEP[joint], t * 2 + off), 0, 0)
        for joint in ("thigh", "calf"):
            p[f"{joint}_{side}"] = (curve(STEP[joint], t * 2 + off + 0.25), 0, 0)
    turn = 6 * math.sin(2 * math.pi * t)
    p.update({"root@loc": (0, 0.01 * math.sin(4 * math.pi * t), 0.006 * abs(math.sin(4 * math.pi * t))),
              "spine_02": (0, 0, turn * 0.5), "chest": (0, 0, turn), "neck_01": (-10, 0, turn * 2.5),
              "head": (-8, 0, turn * 1.5), "tail_01": (0, 0, -turn * 2)})
    shuffle.key(f, p, "LINEAR")
shuffle.close(48)

bleat = Clip("bleat")
UP = over(STAND, neck_01=(14, 0, 0), neck_02=(8, 0, 0), head=(18, 0, 0), ear_r=(-20, 0, -30), ear_l=(-20, 0, 30), chest=(3, 0, 0))
bleat.key(0, STAND)
bleat.key(10, UP)
bleat.key(14, over(UP, jaw=(-26, 0, 0)), "LINEAR")
for f in (18, 22, 26):
    bleat.key(f, over(UP, jaw=(-22 - 6 * ((f // 4) % 2), 0, 0), head=(18 + 2 * ((f // 4) % 2), 0, 0)), "LINEAR")
bleat.key(32, over(UP, jaw=(-4, 0, 0)))
bleat.key(42, STAND)
bleat.hold(60)
bleat.close(72)

startle = Clip("startle")
BUNCH = over(STAND, root__loc=(0, -0.04, -0.03), pelvis=(-6, 0, 0), spine_02=(6, 0, 0), neck_01=(20, 0, 0), head=(14, 0, 0),
             ear_r=(-40, 0, -20), ear_l=(-40, 0, 20), tail_01=(-20, 0, 0))
BUNCH.update(mirror({"upperarm_r": (-12, 0, 0), "thigh_r": (16, 0, 0), "calf_r": (-20, 0, 0)}))
startle.key(0, STAND, "CONSTANT")
startle.key(2, BUNCH, "CONSTANT")
startle.key(8, over(BUNCH, chest=(0, 0, 8), neck_01=(16, 0, 14)), "LINEAR")
startle.key(14, over(BUNCH, chest=(0, 0, -8), neck_01=(16, 0, -14)), "LINEAR")
startle.key(24, over(STAND, neck_01=(-2, 0, 0)))
startle.close(40)

kit.build()
rig.bake(sk, [idle, shuffle, bleat, startle],
         plant=rig.feet_planter(sk, bones=("hand_l", "hand_r", "foot_l", "foot_r"), clips={"idle", "bleat"}, lowest=0.01))
rig.export(rig.args()[0] if rig.args() else "sheep.glb", kit)
print(f"[dt] sheep clips {[c.name + ':' + str(c.length) for c in (idle, shuffle, bleat, startle)]}")
