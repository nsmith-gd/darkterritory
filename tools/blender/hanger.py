"""THE HANGER (GDD §21, OUTSIDE; the director, 9 Oct 2026; ARCHITECTURE §8 note 586): it lives in a house's roof space,
and its strands hang from the ceiling. "A pale, bloated, spider-like thing that clings upside-down to the rafters; a soft
sac of a body; many thin jointed limbs braced against the beams; a mouth underneath that pays out sticky strands."

Hung upside down from the beams, about 2.3 m across its braced legs: a small hard-ish forebody (the thorax and the head)
and behind it the sac, bloated, soft and sagging away from the beams under its own weight; ten long thin legs, three
bones each, splayed round it, the knees hanging lower than the body and the feet hooked up over the beams; under the
head, facing the floor, a puckered wet mouth between two short hooked fangs, a cluster of small black eyes round it.

How it's made (the Look Review: organic, one skin, not primitives; the Mourners' way, note 362): the sac, the thorax,
the head and the mouth's lips are one skin of smooth volumes (tools/blender/flesh.py), settled onto their field before
QuadriFlow lays the game mesh's quads over it. Over it, finer than its quads and rigid as a spider's: the legs (each
segment a thin sleeve swelling at its knuckle), the fangs, the eyes. Its colour (the veins in the sac, the bands at the
joints, the wet mouth) is baked into one 1024 atlas by tools/models/recipes/hanger.py.

The model's origin is where it hangs from: the underside of the beams it grips, over the middle of it (CreatureArt draws
it with the beams there). Everything is below, but the feet's hooks, which go up over them.

Its own rig (SK_Hanger, 39 bones): root, thorax, abdomen (the sac: it breathes, by scale), head, mouth, two two-boned
palps, and ten legs of three bones (femur, tibia, tarsus): leg_r0..r4, leg_l0..l4 from the front. Its rest is braced on
its legs, facing +Y (the engine's -Z), hanging. Clips (HangerMode; GDD §31: still, then too fast): hidden (folded up
tight against the beams, the sac slowly breathing, a leg twitching now and then), grab (a strand snaps taut: a jerk), haul
(the front legs pulling the strand up hand over hand into the mouth), hold (braced, someone's weight on the line, swaying),
hit (a recoil, the legs flung out), retreat (scuttling backward along the beams, flattened), death (the legs curling in,
letting go).

    tools/models/build.sh hanger        # this, with its colour baked -> content/art/models/hanger.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/hanger.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, chain, mix, smooth01  # noqa: E402
from rig import Bone, Clip, Mat, Quaternion, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

# Its legs, from the front, each a side: which way it splays (degrees from straight ahead, +Y, round toward the side) and
# how far it reaches (the front pair longest: they haul).
AZ = (20, 48, 78, 108, 138)
REACH = (1.18, 1.05, 0.95, 0.98, 1.08)
LEGS = range(len(AZ))
# The forebody's middle, and where the sac hangs behind it.
THORAX = Vector((0.0, 0.1, -0.2))
SAC = Vector((0.0, -0.33, -0.31))
# The mouth, under the head, facing the floor: where its strands come out (CreatureArt.HangerMouth, the engine's (0, -0.37, -0.3)).
MOUTH = Vector((0.0, 0.3, -0.355))


def out_of(s, k):
    """Leg k's way out on side s (+1 right, -1 left), level."""
    a = math.radians(AZ[k])
    return Vector((s * math.sin(a), math.cos(a), 0.0))


def joints(s, k):
    """Leg k's hip, knee, ankle and toe (side s): out from the forebody, the knee hung low, the toe up on the beams."""
    d, L = out_of(s, k), REACH[k]
    hip = THORAX + d * 0.12 + Vector((0, 0, 0.02))
    knee = hip + d * (0.4 * L) + Vector((0, 0, -0.33 * L))
    ankle = hip + d * (0.82 * L) + Vector((0, 0, -0.16 * L))
    toe = hip + d * (1.02 * L)
    toe.z = -0.012
    return hip, knee, ankle, toe


def hanger_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("thorax", "root", (0, -0.05, -0.2), (0, 0.24, -0.2)),
        # The sac, hanging back from the forebody (it breathes: CreatureArtTests lets its scale key).
        Bone("abdomen", "thorax", (0, -0.05, -0.22), (0, -0.62, -0.34)),
        Bone("head", "thorax", (0, 0.2, -0.22), (0, 0.36, -0.27)),
        # The mouth's lips, under the head, pointing at the floor.
        Bone("mouth", "head", (0, 0.29, -0.3), (0, 0.3, -0.37)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        b += [Bone(f"palp_{side}_01", "head", (sx * 0.045, 0.33, -0.3), (sx * 0.07, 0.37, -0.38)),
              Bone(f"palp_{side}_02", f"palp_{side}_01", (sx * 0.07, 0.37, -0.38), (sx * 0.06, 0.39, -0.46))]
        for k in LEGS:
            hip, knee, ankle, toe = joints(sx, k)
            b += [Bone(f"leg_{side}{k}_01", "thorax", tuple(hip), tuple(knee)),
                  Bone(f"leg_{side}{k}_02", f"leg_{side}{k}_01", tuple(knee), tuple(ankle)),
                  Bone(f"leg_{side}{k}_03", f"leg_{side}{k}_02", tuple(ankle), tuple(toe))]
    return Skeleton("SK_Hanger", b)


sk = hanger_skeleton()
sk.build()
kit = rig.Kit(sk, "hanger")
SKIN_FACES = 1500


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/hanger.py),
# the names say what to paint and how to sculpt it: the sac's thin skin, the legs, the wet mouth, the fangs, the eyes.
SKIN = Mat("skin.hanger", hexc("#cfc8b8"), shine=0.3)
LEG = Mat("skin.hanger_leg", hexc("#b9b0a0"), shine=0.25)
LIPS = Mat("skin.hanger_mouth", hexc("#5a2a26"), shine=0.6)
FANG = Mat("skin.hanger_fang", hexc("#3a3028"), shine=0.4)
EYE = Mat("glass_dirty.hanger_eye", hexc("#141212"), shine=0.95)

# ----------------------------------------------------------------------------------------------------------------
# The skin: the forebody and the sac, one surface (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "hanger flesh")


def body_w(p):
    """The forebody to the thorax, the head's end to the head, the sac to the abdomen, blended over the waist."""
    p = Vector(p)
    w = {"thorax": 1.0}
    w = mix(w, {"abdomen": 1.0}, smooth01(0.0, -0.14, p.y))
    w = mix(w, {"head": 1.0}, smooth01(0.2, 0.28, p.y))
    return w


def mouth_w(p):
    return mix(body_w(p), {"mouth": 1.0}, smooth01(0.06, 0.02, (Vector(p) - MOUTH).length))


# The forebody: a low hard-ish carapace, flatter against the beams; the head pushed forward and down off it.
flesh.blob(THORAX, (0.13, 0.16, 0.085), 0, SKIN, body_w, around=20, rings=10)
flesh.blob(THORAX + Vector((0, 0.17, -0.045)), (0.085, 0.09, 0.075), 0.05, SKIN, body_w, around=18, rings=10)   # the head
flesh.blob(THORAX + Vector((0, -0.12, -0.01)), (0.075, 0.07, 0.065), 0.04, SKIN, body_w)                        # the waist
# The sac: bloated and soft, sagging away from the beams under its own weight, heaviest at its back; a groove down its
# middle and the skin drawn into creases where it hangs.
flesh.blob(SAC, (0.25, 0.34, 0.23), 0.06, SKIN, body_w, around=24, rings=12)
flesh.blob(SAC + Vector((0, -0.1, -0.13)), (0.2, 0.25, 0.15), 0.08, SKIN, body_w, around=20, rings=10)       # the sag
flesh.blob(SAC + Vector((0, -0.28, -0.05)), (0.13, 0.11, 0.13), 0.06, SKIN, body_w)                             # its end
for sx in (1, -1):
    # Swellings down its flanks, lumpy, like something packed in it.
    flesh.blob(SAC + Vector((sx * 0.15, 0.06, -0.1)), (0.1, 0.13, 0.11), 0.05, SKIN, body_w)
    flesh.blob(SAC + Vector((sx * 0.12, -0.18, -0.15)), (0.09, 0.1, 0.09), 0.05, SKIN, body_w)
flesh.carve(SAC + Vector((0, 0.0, 0.2)), (0.025, 0.28, 0.05), 0.04)
# The hips: a soft knuckle where each leg grows out of the forebody.
for s, sx in (("r", 1), ("l", -1)):
    for k in LEGS:
        hip = H(f"leg_{s}{k}_01")
        flesh.blob(hip, (0.038, 0.038, 0.034), 0.03, SKIN, lambda p, s=s, k=k: mix(body_w(p), {f"leg_{s}{k}_01": 1.0}, 0.35))
# The mouth: lips puckered round an opening under the head, swollen, pointing at the floor.
flesh.blob(MOUTH + Vector((0, 0, 0.02)), (0.05, 0.045, 0.04), 0.03, LIPS, mouth_w, around=16, rings=8)
flesh.carve(MOUTH + Vector((0, 0, -0.02)), (0.016, 0.014, 0.03), 0.012)

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the legs, the fangs, the palps and the eyes.
legs = kit.part("legs")
TOES = []
for s, sx in (("r", 1), ("l", -1)):
    for k in LEGS:
        hip, knee, ankle, toe = joints(sx, k)
        bones = [f"leg_{s}{k}_01", f"leg_{s}{k}_02", f"leg_{s}{k}_03"]
        w = chain([hip, knee, ankle, toe], bones, soft=0.025)
        thick = 1.0 if k > 0 else 1.12
        # Each segment a thin sleeve: swelling a little past its root, a knuckle at its end; the toe a hooked point up
        # over the beam.
        for seg, (a, b, r0, r1, sides) in enumerate(((hip, knee, 0.026, 0.02, 6), (knee, ankle, 0.019, 0.014, 6), (ankle, toe, 0.013, 0.006, 5))):
            d = (b - a).normalized()
            n = 4 if seg == 0 else 3
            pts, rr = [], []
            for i in range(n + 1):
                t = i / n
                pts.append(a - d * (0.012 if seg else 0.0) + (b - a + d * 0.012) * t)
                r = (r0 + (r1 - r0) * t) * thick
                r *= 0.9 + 0.2 * math.sin(min(1.0, t * 1.3) * math.pi * 0.8)
                rr.append(r)
            if seg == 2:
                rr[-1] = 0.004
            legs.tube(pts, rr, sides, LEG, w, ref=(0, 0, 1), cap0="point", cap1="point")
            if seg < 2:
                # The knuckle.
                legs.blob(b, (0.024 * thick, 0.024 * thick, 0.024 * thick) if seg == 0 else (0.017, 0.017, 0.017), 6, 4, LEG, w)
        # The hook: curled up over the beam's edge.
        d = out_of(sx, k)
        hook = toe + d * 0.035 + Vector((0, 0, 0.02))
        TOES.append(hook)
        legs.tube([toe - d * 0.01, toe + d * 0.02 + Vector((0, 0, 0.006)), hook], [0.006, 0.005, 0.0015], 5, FANG, f"leg_{s}{k}_03",
                  ref=(0, 0, 1), cap1="point")

fine = kit.part("fine")
# The fangs: two short hooked chelicerae either side of the mouth, curled in under it.
for sx in (1, -1):
    root = MOUTH + Vector((sx * 0.04, 0.025, 0.02))
    pts = [root, root + Vector((sx * 0.01, 0.025, -0.04)), root + Vector((-sx * 0.004, 0.03, -0.075)), root + Vector((-sx * 0.02, 0.012, -0.09))]
    fine.tube(pts, [0.016, 0.012, 0.007, 0.002], 6, FANG, "mouth", ref=(0, 1, 0), cap0="point", cap1="point")
# The palps: thin feelers hanging from the head's front, working.
for s, sx in (("r", 1), ("l", -1)):
    a, b, c = H(f"palp_{s}_01"), H(f"palp_{s}_02"), T(f"palp_{s}_02")
    fine.tube([a, a.lerp(b, 0.5), b, b.lerp(c, 0.5), c], [0.012, 0.011, 0.01, 0.008, 0.004], 6, LEG,
              chain([a, b, c], [f"palp_{s}_01", f"palp_{s}_02"], soft=0.015), ref=(0, 1, 0), cap0="point", cap1="point")
# The eyes: a cluster of small black beads round the front of the head, looking down and forward.
EYES = []
for i, (x, y, z, r) in enumerate(((0.025, 0.335, -0.235, 0.014), (0.055, 0.315, -0.225, 0.011), (0.07, 0.29, -0.245, 0.009),
                                  (0.035, 0.34, -0.265, 0.01), (0.06, 0.322, -0.27, 0.008))):
    for sx in (1, -1):
        e = Vector((sx * x, y, z))
        EYES.append(e)
        fine.blob(e, (r, r, r), 8, 5, EYE, "head")

# ----------------------------------------------------------------------------------------------------------------
# Clips. Its rest is braced, hanging. Each leg's joints turn about its own axes (Quaternions in the armature's axes):
# `lift` raises a segment toward the beams (the femur's knee up), `knee` folds the tibia back in toward the body, `curl`
# the tarsus in, `swing` turns the whole leg about the vertical, back (+) or in toward the head (-).
Z = Vector((0, 0, 1))


def leg(s, k, lift=0.0, knee=0.0, curl=0.0, swing=0.0):
    sx = 1 if s == "r" else -1
    axis = out_of(sx, k).cross(Z).normalized()
    q1 = Quaternion(Z, math.radians(-sx * swing)) @ Quaternion(axis, math.radians(lift))
    return {f"leg_{s}{k}_01": q1, f"leg_{s}{k}_02": Quaternion(axis, math.radians(knee)),
            f"leg_{s}{k}_03": Quaternion(axis, math.radians(curl))}


def legs_all(lift=0.0, knee=0.0, curl=0.0, swing=0.0, only=None):
    out = {}
    for s in "rl":
        for k in (only if only is not None else LEGS):
            out.update(leg(s, k, lift, knee, curl, swing))
    return out


def pose(*parts):
    out = {}
    for p in parts:
        out.update(p)
    return out


def breathe(k):
    """The sac swollen (+) or drawn in (-) by k (a fraction of its girth)."""
    return {"abdomen@scale": (1 + k, 1 + k * 0.4, 1 + k)}


def palps(k):
    return {"palp_r_01": (10 * k, 0, 6 * k), "palp_l_01": (10 * k, 0, -6 * k), "palp_r_02": (-12 * k, 0, 0), "palp_l_02": (-12 * k, 0, 0)}


BRACED = pose(breathe(0), palps(0))
# Folded up tight against the beams (hidden): the body pressed up, every knee drawn up level with it and the shins
# folded back in, the toes hooked on close round it; the sac flattened up into the dark.
FOLDED = pose({"root@loc": (0, 0, 0.08), "abdomen": (8, 0, 0), "head": (6, 0, 0)}, breathe(-0.04), palps(0.8),
              legs_all(lift=40, knee=128, curl=-162, swing=0))

# Hidden (6.3 s, loop): folded and still, the sac breathing slowly, unevenly (in, held, out with a catch). Now and then one
# leg comes unhooked, slowly, hangs feeling the air, its tip testing, and is set back, slowly, then gripped in two frames;
# later another, quicker; the palps flick once. Never two at once, never the same beat twice.
def folded(br=0.0, palp=0.8, adjust=()):
    p = pose(FOLDED, breathe(-0.04 + br), palps(palp))
    for s, k, u, test, sw in adjust:
        # u 0 hooked, 1 off the beams and hanging a little; `test` the tip's feeling about; `sw` where it's put back.
        p.update(leg(s, k, lift=40 - 40 * u, knee=128 - 38 * u, curl=-162 + 22 * test * u, swing=sw))
    return p


hidden = Clip("hidden")
hidden.key(0, folded(), "BEZIER")
hidden.key(34, folded(0.035), "BEZIER")                                        # in
hidden.key(40, folded(0.036, adjust=[("r", 1, 0.0, 0, 0)]), "BEZIER")
hidden.key(50, folded(0.03, adjust=[("r", 1, 1.0, 0, 4)]), "BEZIER")          # a leg unhooked, slowly
hidden.key(54, folded(0.03, adjust=[("r", 1, 1.0, 1, 4)]), "BEZIER")          # feeling the air
hidden.key(57, folded(0.03, adjust=[("r", 1, 1.0, -0.4, 6)]), "BEZIER")
hidden.key(61, folded(0.03, adjust=[("r", 1, 1.0, 0.6, 8)]), "BEZIER")
hidden.key(72, folded(0.025, adjust=[("r", 1, 0.25, 0, 10)]), "LINEAR")       # set back, slowly
hidden.key(74, folded(0.02, adjust=[("r", 1, 0.0, -0.3, 10)]), "BEZIER")      # gripped, in two frames
hidden.key(90, folded(0.02, adjust=[("r", 1, 0.0, 0, 10)]), "CONSTANT")       # out: a catch
hidden.key(93, folded(0.015, adjust=[("r", 1, 0.0, 0, 10)]), "BEZIER")
hidden.key(108, folded(0.0, adjust=[("r", 1, 0.0, 0, 10)]), "LINEAR")
hidden.key(110, folded(0.0, palp=1.8, adjust=[("r", 1, 0.0, 0, 10)]), "LINEAR")  # the palps flick
hidden.key(113, folded(0.0, palp=0.8, adjust=[("r", 1, 0.0, 0, 10)]), "BEZIER")
hidden.key(130, folded(0.02, adjust=[("r", 1, 0.0, 0, 10), ("l", 3, 0, 0, 0)]), "LINEAR")
hidden.key(133, folded(0.02, adjust=[("r", 1, 0.0, 0, 10), ("l", 3, 0.5, -0.6, -6)]), "LINEAR")  # another, quick
hidden.key(140, folded(0.02, adjust=[("r", 1, 0.0, 0, 10), ("l", 3, 0.5, -0.6, -6)]), "BEZIER")
hidden.key(146, folded(0.025, adjust=[("r", 1, 0.0, 0, 10), ("l", 3, 0.0, 0, -8)]), "BEZIER")
hidden.key(176, folded(0.01, adjust=[("r", 1, 0.0, 0, 4), ("l", 3, 0.0, 0, -3)]), "BEZIER")
hidden.close(190)

# The front pair's reach down to the strand under the mouth, and their pull up past it (haul, grab, hold).
REACH_DOWN = pose(leg("r", 0, lift=-38, knee=-30, curl=-10, swing=-28), leg("l", 0, lift=-38, knee=-30, curl=-10, swing=-28))
PULL_UP = pose(leg("r", 0, lift=22, knee=52, curl=30, swing=-34), leg("l", 0, lift=22, knee=52, curl=30, swing=-34))

# Grab (0.5 s: CreatureArt.HangerGrabSeconds): the strand snaps taut. From folded tight, in two frames every leg flung down
# and out onto the beams, the body jerked up against them, the sac squeezed, the front legs snatching down at the line;
# then it sags to take the weight, the legs settling one after another.
grab = Clip("grab", loop=False)
grab.key(0, folded(), "LINEAR")
grab.key(2, pose(BRACED, {"root@loc": (0, 0, 0.07), "head": (-14, 0, 0), "abdomen": (10, 0, 0)}, breathe(-0.1), palps(1.5),
                 legs_all(lift=14, knee=20, curl=16), REACH_DOWN), "LINEAR")
grab.key(4, pose(BRACED, {"root@loc": (0, 0, 0.06), "head": (-12, 0, 0), "abdomen": (8, 0, 0)}, breathe(-0.1), palps(1.4),
                 legs_all(lift=12, knee=18, curl=14), REACH_DOWN), "LINEAR")
grab.key(7, pose(BRACED, {"root@loc": (0, 0, -0.04), "head": (12, 0, 0), "abdomen": (-8, 0, 0)}, breathe(0.06), palps(-0.5),
                 legs_all(lift=-6, knee=-8, curl=-6), REACH_DOWN), "BEZIER")
grab.key(11, pose(BRACED, {"root@loc": (0, 0, 0.005), "head": (2, 0, 0), "abdomen": (2, 0, 0)}, breathe(0.0), REACH_DOWN,
                  legs_all(lift=-3, knee=-4, curl=-3, only=(3, 4))), "BEZIER")
grab.key(15, pose(BRACED, {"root@loc": (0, 0, 0.0), "head": (4, 0, 0)}, breathe(0.0), REACH_DOWN), "BEZIER")


def _track(table, f):
    """A value at frame f from [(frame, value, ease)]: eased (smoothstep) or straight between keys."""
    for (f0, v0, e), (f1, v1, _) in zip(table, table[1:]):
        if f0 <= f <= f1:
            t = (f - f0) / max(1, f1 - f0)
            if e:
                t = t * t * (3 - 2 * t)
            return v0 + (v1 - v0) * t
    return table[-1][1]


# Each front leg's reach (1: down at the line, 0: pulled up past the mouth): pulls fast, reaches slow, the two never even;
# the right pulls in four frames, the left in five; a pause; the right twice.
HAUL_R = [(0, 1, 0), (4, 0, 1), (14, 0.2, 1), (23, 1, 1), (26, 1, 0), (29, 0, 1), (38, 0.4, 1), (50, 1, 1)]
HAUL_L = [(0, 0, 1), (6, 0.3, 1), (13, 1, 1), (15, 1, 0), (20, 0, 1), (31, 0.4, 1), (37, 1, 1), (40, 1, 0), (43, 0, 1), (50, 0, 1)]


def haul_at(f):
    out = pose(BRACED)
    tug = 0.0
    for s, table in (("r", HAUL_R), ("l", HAUL_L)):
        k = _track(table, f)
        out.update(leg(s, 0, lift=-38 + 60 * (1 - k), knee=-30 + 82 * (1 - k), curl=-10 + 40 * (1 - k), swing=-28 - 6 * (1 - k)))
        out.update(leg(s, 1, lift=-14 + 20 * (1 - k), knee=-6 + 22 * (1 - k), curl=6 * (1 - k), swing=-14))
        # (How hard it's pulling now: the drop in k over the last frames.)
        tug += max(0.0, _track(table, f - 2) - k) if f >= 2 else 0.0
    tug = min(1.0, tug)
    out.update({"root@loc": (0.0, 0.0, 0.03 * tug - 0.01), "head": (-10 * tug + 3, 0, 4 * (_track(HAUL_R, f) - _track(HAUL_L, f))),
                "abdomen": (6 * tug, 0, 0)})
    out.update(breathe(0.05 * tug - 0.01))
    out.update(palps(1.2 * tug))
    out.update({"mouth@scale": (1 + 0.25 * tug, 1 + 0.25 * tug, 1 + 0.25 * tug)})
    return out


# Haul (1.67 s, loop): pulling the line up into the mouth hand over hand, unevenly: each pull a jerk (the body yanked up
# against the beams, the mouth working), each reach slow.
haul = Clip("haul")
for f in range(0, 50, 2):
    haul.key(f, haul_at(f), "LINEAR")
haul.close(50)

# Hold (2.3 s, loop): someone's weight on the line. Braced hard, the legs spread and set, the front pair clutching the
# line under the mouth; swaying with the weight, jolted when they kick (a two-frame drop), the sac pulsing quick and
# uneven; one leg shifting its grip.
HOLDING = pose(BRACED, legs_all(lift=-8, knee=-10, curl=-8, only=(1, 2, 3, 4)),
               leg("r", 0, lift=-20, knee=10, curl=10, swing=-32), leg("l", 0, lift=-20, knee=10, curl=10, swing=-32))


def holding(sw=0.0, br=0.0, drop=0.0, shift=0.0):
    p = pose(HOLDING, breathe(br), palps(sw + drop), {"thorax": (0, 3 * sw, 0), "head": (5 * abs(sw) + 10 * drop, 0, 4 * sw),
                                                       "abdomen": (0, -3 * sw, 0), "root@loc": (0.01 * sw, 0, -0.04 * drop)})
    if shift:
        p.update(leg("l", 2, lift=-8 + 22 * shift, knee=-10 + 16 * shift, curl=-8 + 20 * shift, swing=4 * shift))
    return p


hold = Clip("hold")
for f, a, ip in ((0, holding(), "BEZIER"), (13, holding(1, 0.05), "BEZIER"), (20, holding(0.4, -0.02), "LINEAR"),
                 (22, holding(0.3, 0.06, drop=1), "BEZIER"), (30, holding(-0.2, 0.0), "BEZIER"), (37, holding(-1, 0.05), "BEZIER"),
                 (44, holding(-0.6, -0.02, shift=1), "BEZIER"), (50, holding(-0.3, 0.04, shift=0.2), "BEZIER"), (56, holding(0, 0.0), "BEZIER")):
    hold.key(f, a, ip)
hold.close(70)

# Hit (0.4 s: CreatureArt.HangerHitSeconds): it recoils up into the beams, twisting, every leg flung out off them, held
# two frames; then back on, the legs finding the beams one side after the other.
hit = Clip("hit", loop=False)
hit.key(0, BRACED, "CONSTANT")
hit.key(2, pose(BRACED, {"root@loc": (0.02, 0, 0.07), "thorax": (0, 8, 14), "head": (-20, 0, 18), "abdomen": (12, 0, -10)},
                breathe(-0.12), palps(2), legs_all(lift=18, knee=-18, curl=-20, swing=6)), "CONSTANT")
hit.key(4, pose(BRACED, {"root@loc": (0.02, 0, 0.06), "thorax": (0, 7, 12), "head": (-16, 0, 16), "abdomen": (10, 0, -8)},
                breathe(-0.1), palps(1.6), legs_all(lift=16, knee=-16, curl=-18, swing=5)), "LINEAR")
hit.key(8, pose(BRACED, {"root@loc": (0, 0, 0.02), "thorax": (0, -3, -4)}, legs_all(lift=4, knee=4),
                {k: v for s in "l" for k, v in legs_all(lift=12, knee=-10, curl=-12).items() if f"_{s}" in k}), "LINEAR")
hit.key(12, BRACED, "CONSTANT")


def scuttle(t):
    """Scuttling backward along the beams at t (0..1): two alternating sets of legs, each stepping off the beams and back
    (a step lets go: the leg drops), flattened up against them."""
    out = pose(BRACED, {"root@loc": (0, 0, 0.05), "abdomen": (6, 0, 0)}, breathe(-0.03))
    for s in "rl":
        for k in LEGS:
            group = (k % 2 == 0) != (s == "r")
            u = (t + (0.5 if group else 0.0)) % 1.0
            if u < 0.5:
                sw, lf = 18 * (2 * u - 0.5), 0.0
            else:
                q = (u - 0.5) * 2
                sw, lf = 18 * (0.5 - q), -26 * math.sin(q * math.pi)
            out.update(leg(s, k, lift=lf + 10, knee=12 - lf * 0.4, curl=8, swing=sw))
    out.update({"head": (0, 0, 6 * math.sin(t * 2 * math.pi))})
    return out


# Retreat (1 s, loop): scuttling back into the dark along the beams in bursts, too fast for its size: a stride and a
# half in eleven frames, stopped dead, flat to the beams, the head snapping round; on again.
retreat = Clip("retreat")
for f in range(0, 12, 2):
    retreat.key(f, scuttle(f / 8), "LINEAR")
retreat.key(12, scuttle(1.5), "LINEAR")
retreat.key(15, pose(scuttle(1.5), {"head": (-6, 0, -14)}), "LINEAR")         # stopped dead, the head round
retreat.key(19, pose(scuttle(1.5), {"head": (-6, 0, -14)}), "LINEAR")
for f in range(20, 30, 2):
    retreat.key(f, scuttle(1.5 + (f - 20) / 20), "LINEAR")
retreat.close(30)

# Death (1.5 s): a jerk, every leg flung; then every leg curls in over the body (a spider's death), one side a beat after
# the other, the sac sagging, and it lets go of the beams and drops, turning over; a last leg twitches.
CURLED = pose(legs_all(lift=-40, knee=78, curl=24), breathe(-0.08), palps(2))
death = Clip("death", loop=False)
death.key(0, BRACED, "CONSTANT")
death.key(3, pose(BRACED, {"root@loc": (0, 0, 0.04), "head": (-16, 0, 0)}, legs_all(lift=12, knee=-10, curl=-20), breathe(-0.1)), "LINEAR")
death.key(9, pose(BRACED, {"root@loc": (0, 0, 0.0), "head": (-6, 0, 0)}, legs_all(lift=12, knee=-10, curl=-20),
                  {k: v for k, v in legs_all(lift=-40, knee=78, curl=24).items() if "_r" in k}, breathe(-0.1)), "BEZIER")
death.key(16, pose(CURLED, {"root@loc": (0, 0.04, -0.25), "root": (20, 0, 10), "head": (14, 0, 0)}), "BEZIER")
death.key(30, pose(CURLED, {"root@loc": (0, 0.1, -0.85), "root": (60, 0, 24), "head": (20, 0, 0)}), "LINEAR")
death.key(38, pose(CURLED, {"root@loc": (0, 0.12, -0.95), "root": (70, 0, 26), "head": (20, 0, 0)}, legs_all(lift=-44, knee=84, curl=30)), "LINEAR")
death.key(40, pose(CURLED, {"root@loc": (0, 0.12, -0.95), "root": (70, 0, 26), "head": (20, 0, 0)}, legs_all(lift=-44, knee=84, curl=30),
                   leg("l", 2, lift=-30, knee=60, curl=40)), "LINEAR")
death.key(45, pose(CURLED, {"root@loc": (0, 0.12, -0.95), "root": (70, 0, 26), "head": (20, 0, 0)}, legs_all(lift=-44, knee=84, curl=30)), "BEZIER")

CLIPS = [hidden, grab, haul, hold, hit, retreat, death]

if os.environ.get("DT_POSES"):
    for clip in CLIPS:
        for frame, pz, _ in clip.keys:
            P = rig.pose_points(sk, pz, [("head", "tail"), ("leg_r0_03", "tail"), ("leg_l0_03", "tail"), ("leg_r2_03", "tail")])
            print(f"{clip.name:8s} {frame:3d} " + " ".join(f"({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})" for p in P))
    sys.exit(0)

# One skin, the forebody and the sac (rig.fuse, settled on the flesh's field, then QuadriFlow); the legs over it. A
# beast's budget (CreatureArtTests: 2-8k).
kit.fuse("body", ["body"], voxel=0.005, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] hanger", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "hanger.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
