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

# Hidden (4 s, loop): folded and still, the sac slowly breathing; once, a leg twitches (snapped, held, put back).
hidden = Clip("hidden")
for f, br, tw in ((0, 0.0, 0), (30, 0.035, 0), (48, 0.02, 1), (52, 0.02, 1), (60, 0.0, 0), (90, 0.035, 0)):
    p = pose(FOLDED, breathe(-0.04 + br))
    if tw:
        p.update(leg("r", 1, lift=26, knee=100, curl=-140, swing=8))
    hidden.key(f, p, "CONSTANT" if f in (48, 52) else "BEZIER")
hidden.close(120)

# The front pair's reach down to the strand under the mouth, and their pull up past it (haul, grab, hold).
REACH_DOWN = pose(leg("r", 0, lift=-38, knee=-30, curl=-10, swing=-28), leg("l", 0, lift=-38, knee=-30, curl=-10, swing=-28))
PULL_UP = pose(leg("r", 0, lift=22, knee=52, curl=30, swing=-34), leg("l", 0, lift=22, knee=52, curl=30, swing=-34))

# Grab (0.5 s): the strand snaps taut. A jerk up against the beams, every leg clenching on them, the sac squeezed, the
# front legs snatching down at the line; then braced to take the weight.
grab = Clip("grab", loop=False)
grab.key(0, BRACED, "CONSTANT")
grab.key(2, pose(BRACED, {"root@loc": (0, 0, 0.07), "head": (-14, 0, 0), "abdomen": (10, 0, 0)}, breathe(-0.1), palps(1.5),
                 legs_all(lift=14, knee=20, curl=16), REACH_DOWN), "LINEAR")
grab.key(6, pose(BRACED, {"root@loc": (0, 0, -0.03), "head": (10, 0, 0), "abdomen": (-6, 0, 0)}, breathe(0.05), palps(-0.5),
                 legs_all(lift=-6, knee=-8, curl=-6), REACH_DOWN), "BEZIER")
grab.key(15, pose(BRACED, {"root@loc": (0, 0, 0.0), "head": (4, 0, 0)}, breathe(0.0), REACH_DOWN), "BEZIER")


def haul_pose(t):
    """Hand over hand at t (0..1 of a pull): one front leg pulls up as the other reaches down, the second pair helping;
    the body rocks with each pull and the sac heaves."""
    out = pose(BRACED)
    for s, ph in (("r", 0.0), ("l", 0.5)):
        u = (t + ph) % 1.0
        # Reaching down (k 1) and pulling up (k 0) in turn.
        k = 0.5 + 0.5 * math.cos(u * 2 * math.pi)
        out.update(leg(s, 0, lift=-38 + 60 * (1 - k), knee=-30 + 82 * (1 - k), curl=-10 + 40 * (1 - k), swing=-28 - 6 * (1 - k)))
        out.update(leg(s, 1, lift=-14 + 20 * (1 - k), knee=-6 + 22 * (1 - k), curl=6 * (1 - k), swing=-14))
    rock = math.sin(t * 4 * math.pi)
    out.update({"root@loc": (0.0, 0.0, 0.015 * rock), "head": (8 * rock, 0, 0), "abdomen": (-4 * rock, 0, 0)})
    out.update(breathe(0.03 * rock))
    out.update(palps(0.6 * rock))
    out.update({"mouth@scale": (1 + 0.15 * rock, 1 + 0.15 * rock, 1 + 0.15 * rock)})
    return out


# Haul (1.2 s, loop): pulling the line up hand over hand into the mouth, rocking with each pull.
haul = Clip("haul")
for f in range(0, 36, 6):
    haul.key(f, haul_pose(f / 36), "BEZIER")
haul.close(36)

# Hold (2 s, loop): someone's weight on the line. Braced hard, the legs spread and set, the front pair clutching the
# line under the mouth; the whole of it swaying with the weight, the sac pulsing quick.
HOLDING = pose(BRACED, legs_all(lift=-8, knee=-10, curl=-8, only=(1, 2, 3, 4)),
               leg("r", 0, lift=-20, knee=10, curl=10, swing=-32), leg("l", 0, lift=-20, knee=10, curl=10, swing=-32))
hold = Clip("hold")
for f, sw, br in ((0, 0, 0), (15, 1, 0.05), (30, 0, -0.02), (45, -1, 0.05)):
    hold.key(f, pose(HOLDING, breathe(br), palps(sw), {"thorax": (0, 3 * sw, 0), "head": (5 * abs(sw), 0, 4 * sw),
                                                     "abdomen": (0, -3 * sw, 0), "root@loc": (0.01 * sw, 0, 0)}), "BEZIER")
hold.close(60)

# Hit (0.4 s): it recoils up into the beams, twisting, every leg flung out off them, then back on.
hit = Clip("hit", loop=False)
hit.key(0, BRACED, "CONSTANT")
hit.key(2, pose(BRACED, {"root@loc": (0.02, 0, 0.07), "thorax": (0, 8, 14), "head": (-20, 0, 18), "abdomen": (12, 0, -10)},
                breathe(-0.12), palps(2), legs_all(lift=18, knee=-18, curl=-20, swing=6)), "CONSTANT")
hit.key(8, pose(BRACED, {"root@loc": (0, 0, 0.02), "thorax": (0, -3, -4)}, legs_all(lift=4, knee=4)), "LINEAR")
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


# Retreat (0.6 s, loop): scuttling back into the dark along the beams, too fast for its size.
retreat = Clip("retreat")
for f in range(0, 18, 3):
    retreat.key(f, scuttle(f / 18), "LINEAR")
retreat.close(18)

# Death (1.5 s): every leg curls in over the body (a spider's death), the sac sagging, and it lets go of the beams and
# drops, turning over.
CURLED = pose(legs_all(lift=-50, knee=95, curl=70), breathe(-0.08), palps(2))
death = Clip("death", loop=False)
death.key(0, BRACED, "CONSTANT")
death.key(4, pose(BRACED, {"root@loc": (0, 0, 0.04), "head": (-16, 0, 0)}, legs_all(lift=12, knee=-10, curl=-20), breathe(-0.1)), "LINEAR")
death.key(16, pose(CURLED, {"root@loc": (0, 0.04, -0.25), "root": (20, 0, 10), "head": (14, 0, 0)}), "BEZIER")
death.key(30, pose(CURLED, {"root@loc": (0, 0.1, -0.85), "root": (60, 0, 24), "head": (20, 0, 0)}), "LINEAR")
death.key(45, pose(CURLED, {"root@loc": (0, 0.12, -0.95), "root": (70, 0, 26), "head": (20, 0, 0)}, legs_all(lift=-55, knee=100, curl=80)), "BEZIER")

# One skin, the forebody and the sac (rig.fuse, settled on the flesh's field, then QuadriFlow); the legs over it. A
# beast's budget (CreatureArtTests: 2-8k).
kit.fuse("body", ["body"], voxel=0.005, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, [hidden, grab, haul, hold, hit, retreat, death])
print("[dt] hanger", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "hanger.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
