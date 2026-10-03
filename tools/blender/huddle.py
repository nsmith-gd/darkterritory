"""THE HUDDLE (GDD v1.5 §21, App. A.6 · heat): "A flock of small, soft things that follow the crew aboard for the warmth.
They chirp all the time ... petting hushes them. Hit one and the rest bury whoever did it."

One of the flock (CreatureArt draws as many as there are, each on its own beat). Knee-high to nobody: a round soft
thing 0.3 m tall, all fur, the pale dirty beige of a mushroom's gills (§28's fungal palette), the fur shaggy and fine as
mould when you're close. Two big black bead eyes high on its front, wet, catching the lamp; two soft ears that droop;
two little paws held up in front of it and two round feet under it. Its mouth is small and chirps. It's sweet.

Hit one and the rest show what they are: they puff up to half again their size, a crest of quills comes up out of the
fur along their backs, the ears go flat, and the little mouth splits open wide on rows of needle teeth (§26.5: you can
still tell what they used to be; they used to be sweet).

SK_Huddle: root, the body (scaled to puff), the head (the top half: tilts, looks), the jaw, the ears, the paws, the
feet, and the quills (scaled up out of the fur to bristle). Faces +Y (the engine's -Z). Clips: play (about the one
spot, bobbing and chirping, the head tipping to listen), hop (following, a bounce a step), nestle (round a fire, hushed
or warm: settled flat, breathing slow), bristle (TELEGRAPH and COMMIT: puffed, the quills up, the mouth open on its
teeth, shivering), bury (the GRAB, on whoever struck one: clinging, the paws gripping, biting).

    tools/blender/build.sh huddle        # -> content/art/models/huddle.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
BC = Vector((0, 0, 0.135))          # the body's middle
BR = Vector((0.13, 0.12, 0.135))    # its radii (round, a little deeper than it's wide at the bottom)
SPLIT = 0.15                        # where the body's weight goes over to the head
EYE = Vector((0.05, 0.098, 0.18))   # the right eye's middle (the left's mirrored)
MOUTH_AT = Vector((0, 0.108, 0.122))
FINE = 3.0
bones = [Bone("root", None, (0, 0, 0), (0, 0.05, 0)),
         Bone("body", "root", (0, 0, 0), (0, 0, SPLIT)),
         Bone("head", "body", (0, 0, SPLIT), (0, 0, 0.27)),
         Bone("jaw", "head", (0, 0.09, 0.124), (0, 0.13, 0.112)),
         Bone("quills", "body", tuple(BC), tuple(BC + Vector((0, -0.08, 0.04))))]
for s, sx in (("r", 1), ("l", -1)):
    bones += [Bone(f"ear_{s}", "head", (sx * 0.06, -0.01, 0.245), (sx * 0.12, -0.03, 0.28)),
              Bone(f"paw_{s}", "body", (sx * 0.07, 0.085, 0.09), (sx * 0.08, 0.14, 0.085)),
              Bone(f"foot_{s}", "body", (sx * 0.055, 0.04, 0.03), (sx * 0.06, 0.1, 0.012))]
sk = Skeleton("SK_Huddle", bones)
sk.build()
kit = rig.Kit(sk, "huddle")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Soft and pale: wool lightened to a fungus's beige; the belly paler; the eyes black glass; the mouth's inside wet.
FUR = Mat("wool.huddle", hexc("#a9997e"), shine=0.06, tint=(7.0, 6.0, 4.3))
BELLY = Mat("wool.huddle_belly", hexc("#bdb096"), shine=0.06, tint=(7.8, 6.9, 5.0))
EAR = Mat("flesh.huddle_ear", hexc("#a68a7c"), shine=0.2, tint=(1.6, 1.35, 1.25))
EYES = Mat("glass_dirty.huddle_eye", hexc("#040404"), shine=0.95, tint=(0.06, 0.06, 0.06))
MOUTH = Mat("tar.huddle_mouth", hexc("#24090a"), shine=0.7, tint=(0.6, 0.22, 0.22))
TEETH = Mat("skin.huddle_teeth", hexc("#d8d0b8"), shine=0.5, tint=(1.6, 1.7, 1.6))
QUILL = Mat("tar.huddle_quill", hexc("#3d342a"), shine=0.3, tint=(1.0, 0.95, 0.85))
PAD = Mat("flesh.huddle_pad", hexc("#8a6e66"), shine=0.25, tint=(1.3, 1.1, 1.05))


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def split_weights(p):
    """Under the split the body's, over it the head's, soft across it (so it tips its head without a crease)."""
    k = smooth01(SPLIT - 0.04, SPLIT + 0.04, p.z)
    return {n: v for n, v in (("body", 1 - k), ("head", k)) if v > 1e-4}


def unit(p):
    d = p - BC
    return Vector((d.x / BR.x, d.y / BR.y, d.z / BR.z))


def fur(i, j, a, th, p):
    """Round, a little pear-bottomed where it sits; the fur in shaggy tufts (fine on the face, longer on the back and the
    flanks), flattened underneath."""
    p = Vector(p)
    u = unit(p)
    n = Vector((u.x / BR.x, u.y / BR.y, u.z / BR.z)).normalized()
    face = smooth01(0.3, 0.8, u.y) * smooth01(-0.5, 0.0, u.z)
    tuft = 0.5 + 0.5 * noise3(p * 38, 311, 1.0)
    shag = (0.012 + 0.01 * smooth01(0.0, -0.8, u.y)) * tuft ** 2 * (1 - 0.7 * face)
    off = shag + 0.004 * noise3(p * 90, 312, 1.0)
    # The face a little flatter, so the eyes sit on it; the bottom set down flat on the ground.
    off -= 0.012 * smooth01(0.5, 0.95, u.y) * smooth01(-0.3, 0.2, u.z)
    q = p + n * off
    if q.z < 0.012:
        q.z = 0.012 - (0.012 - q.z) * 0.15
    return q


def fur_or_belly(points, normal):
    c = sum(points, Vector()) / len(points)
    u = unit(c)
    return BELLY if u.y > 0.35 and -0.85 < u.z < 0.25 and abs(u.x) < 0.7 else FUR


# ----------------------------------------------------------------------------------------------------------------
# The body: one round soft lump, the belly paler; the quills hidden in the fur of its back.
body = kit.part("body")
body.blob(BC, tuple(BR), 20, 14, FUR, split_weights, shape=fur, fmat=fur_or_belly)
# A crest of quills down the back and over the crown, laid down inside the fur: CreatureArt's bristle scales them out.
for k in range(22):
    t = k / 21
    a = math.radians(-150 + 300 * ((k * 0.618) % 1.0))         # round the back and the flanks
    el = math.radians(20 + 55 * t)
    d = Vector((math.sin(a) * math.cos(el), math.cos(a) * math.cos(el) * 0.85 - 0.25, math.sin(el))).normalized()
    if d.y > 0.35:
        continue                                               # none on its face
    base = BC + Vector((d.x * BR.x, d.y * BR.y, d.z * BR.z)) * 0.55
    tip = BC + Vector((d.x * BR.x, d.y * BR.y, d.z * BR.z)) * (0.86 + 0.06 * math.sin(k * 2.3))
    body.tube([base, base.lerp(tip, 0.5), tip], [0.006, 0.0045, 0.0008], 4, QUILL, "quills", ref=(0, 0, 1), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# The face: two big black beads, wet, set high; under them the small mouth, its lip the jaw's: inside it, wet and dark,
# the teeth, needles, rows of them, hidden while it's shut.
face = kit.part("face")
for sx in (-1, 1):
    c = Vector((sx * EYE.x, EYE.y, EYE.z))
    face.blob(c, (0.03, 0.022, 0.032), 12, 8, EYES, "head")
    # A soft rim of fur round each, so they sit in the face rather than on it.
    rim = [c + Vector((0.034 * math.cos(t), -0.006 - 0.004 * math.sin(t) ** 2, 0.036 * math.sin(t))) for t in (2 * math.pi * k / 10 for k in range(10))]
    face.tube(rim, [0.007] * 10, 5, BELLY, "head", ref=(0, 1, 0), loop=True)
# The mouth: a little muzzle on the face, two soft whisker pads over a small chin (the jaw's); between them the mouth's
# wet dark, shut to a line, and the teeth, rows of needles, hidden behind the pads until it opens.
for sx in (-1, 1):
    face.blob(MOUTH_AT + Vector((sx * 0.019, 0.006, 0.01)), (0.023, 0.017, 0.016), 10, 6, BELLY, "head")
face.blob(MOUTH_AT + Vector((0, 0.004, -0.012)), (0.026, 0.017, 0.013), 10, 5, BELLY, "jaw")
face.blob(MOUTH_AT + Vector((0, -0.002, -0.01)), (0.027, 0.013, 0.022), 10, 6, MOUTH, "head")
for k in range(9):
    x = (k - 4) * 0.0068
    up = MOUTH_AT + Vector((x, 0.012 - 0.006 * abs(k - 4) / 4, 0.002))
    face.tube([up, up + Vector((0, 0.002, -0.016 * (0.7 + 0.3 * (k % 2))))], [0.0024, 0.0004], 3, TEETH, "head", ref=(0, 1, 0), cap1="point")
    dn = MOUTH_AT + Vector((x * 0.85, 0.01 - 0.006 * abs(k - 4) / 4, -0.006))
    face.tube([dn, dn + Vector((0, 0.002, 0.014 * (0.7 + 0.3 * ((k + 1) % 2))))], [0.0022, 0.0004], 3, TEETH, "jaw", ref=(0, 1, 0), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# The ears, soft and drooping, thin enough the lamp shows through pink; the paws, small, three blunt claws each; the
# feet, round pads under it.
limbs = kit.part("limbs")
for s, sx in (("r", 1), ("l", -1)):
    e0, e1 = H(f"ear_{s}"), T(f"ear_{s}")
    tip = e1 + Vector((sx * 0.035, -0.03, -0.065))
    limbs.tube([e0 - Vector((sx * 0.01, 0, 0.01)), e0.lerp(e1, 0.5), e1, tip], [(0.02, 0.03), (0.019, 0.038), (0.016, 0.034), (0.007, 0.014)], 7,
               EAR, f"ear_{s}", ref=(0, 1, 0.3), cap1="point")
    limbs.tube([e0 - Vector((sx * 0.012, 0, 0.012)), e0.lerp(e1, 0.45)], [(0.018, 0.026), (0.015, 0.03)], 7, FUR, f"ear_{s}", ref=(0, 1, 0.3))
    p0, p1 = H(f"paw_{s}"), T(f"paw_{s}")
    limbs.tube([p0, p0.lerp(p1, 0.6), p1], [0.016, 0.017, 0.014], 7, FUR, f"paw_{s}", ref=(0, 0, 1), cap1=True)
    for c in range(3):
        cx = (c - 1) * 0.009
        b0 = p1 + Vector((cx, 0.006, -0.004))
        limbs.tube([b0, b0 + Vector((cx * 0.3, 0.012, -0.008))], [0.003, 0.0006], 3, TEETH, f"paw_{s}", ref=(0, 0, 1), cap1="point")
    f0, f1 = H(f"foot_{s}"), T(f"foot_{s}")
    limbs.blob(f0.lerp(f1, 0.55), (0.024, 0.034, 0.016), 8, 4, FUR, f"foot_{s}")
    limbs.blob(f0.lerp(f1, 0.6) - Vector((0, 0, 0.009)), (0.016, 0.022, 0.006), 7, 3, PAD, f"foot_{s}", z1=0.2)


# ----------------------------------------------------------------------------------------------------------------
# Clips. The root's @loc carries it off the ground (a hop); the body's @scale puffs or squashes it from where it sits;
# the quills' @scale brings them out of the fur. Angles in the armature's axes (rig.rot): the head tips back by +X, over
# to one side by Y, turns by Z; the jaw opens by -X; the ears lift by -X (they lie back along the head) and splay by Z.
REST = {"body@scale": (1, 1, 1), "quills@scale": (1, 1, 1)}
EARS = mirror({"ear_r": (0, 0, 0)})


def pose(**kw):
    return REST | EARS | over({}, **kw)


# Play (1.6 s, loop): about the one spot. A little bob and a squash; the head tipping to one side to listen, then the
# other; two chirps, the mouth opening a crack; the ears flicking.
play = Clip("play")
for f, (bob, tilt, jaw, ear) in enumerate(((0.0, 0, 0, 0), (0.012, 10, 18, -12), (0.0, 14, 0, 0), (0.008, -12, 14, 10), (0.0, -14, 0, 0),
                                           (0.01, 0, 0, -6), (0.0, 4, 0, 0), (0.004, -2, 0, 0))):
    squash = 0.96 if bob == 0 else 1.03
    p = pose(root__loc=(0, 0, bob), head=(4 * (bob > 0), tilt, -tilt * 0.4), jaw=(-jaw, 0, 0),
             ear_r=(ear, 0, 0), ear_l=(-ear * 0.5, 0, 0))
    p["body@scale"] = (squash ** -0.5, squash ** -0.5, squash)
    play.key(f * 6, p, "BEZIER")
play.close(48)

# Hop (0.4 s, loop): following, a bounce a step: crouched, launched (stretched tall), over the top, landed (squashed).
hop = Clip("hop")
for f, (z, sq, pitch) in ((0, (0.0, 0.85, 6)), (3, (0.06, 1.12, -6)), (6, (0.1, 1.04, 0)), (9, (0.05, 1.0, 4))):
    p = pose(root__loc=(0, 0, z), head=(pitch, 0, 0), ear_r=(-20 * (z > 0.05), 0, -8), ear_l=(-20 * (z > 0.05), 0, 8),
             paw_r=(20 * (z > 0), 0, 0), paw_l=(20 * (z > 0), 0, 0), foot_r=(-30 * (z > 0.05), 0, 0), foot_l=(-30 * (z > 0.05), 0, 0))
    p["body@scale"] = (sq ** -0.5, sq ** -0.5, sq)
    hop.key(f, p, "LINEAR")
hop.close(12)

# Nestle (3 s, loop): settled flat round the warmth, the head down and the ears laid back, breathing slow; now and then a
# small chirp in its sleep.
nestle = Clip("nestle")
for f, (br, jaw) in ((0, (0.0, 0)), (30, (1.0, 0)), (45, (0.6, 10)), (48, (0.6, 0)), (60, (0.0, 0))):
    p = pose(root__loc=(0, 0, -0.012), head=(-14, 0, 0), jaw=(-jaw, 0, 0), ear_r=(26, 0, -14), ear_l=(26, 0, 14),
             paw_r=(-30, 0, 0), paw_l=(-30, 0, 0), foot_r=(10, 0, 0), foot_l=(10, 0, 0))
    p["body@scale"] = (1.1 + 0.03 * br, 1.08 + 0.02 * br, 0.86 + 0.04 * br)
    nestle.key(f, p, "BEZIER")
nestle.close(90)

# Bristle (0.6 s, loop): puffed to half again its size, the quills out of the fur, the ears flat back, the mouth split
# open on its teeth; shivering, quick and small.
bristle = Clip("bristle")
for f, (sh, hiss) in enumerate(((0.0, 1.0), (1.0, 0.85), (-1.0, 1.0), (0.6, 0.9), (-0.6, 1.0), (0.0, 0.8))):
    p = pose(root__loc=(0.003 * sh, 0, 0.004 * abs(sh)), head=(10, 2 * sh, 3 * sh), jaw=(-40 * hiss, 0, 0), jaw__loc=(0, 0.006, -0.026 * hiss),
             ear_r=(38, 0, -20), ear_l=(38, 0, 20), paw_r=(-20, 0, -10), paw_l=(-20, 0, 10))
    p["body@scale"] = (1.38 + 0.02 * sh, 1.36, 1.42 - 0.02 * sh)
    p["quills@scale"] = (1.55, 1.55, 1.55)
    bristle.key(f * 3, p, "LINEAR")
bristle.close(18)

# Bury (0.5 s, loop): on whoever struck one, clinging (CreatureArt puts the flock on them, turned to them): the paws up
# and gripping, the feet dug in, biting and biting, the quills half up, the body pumping.
bury = Clip("bury")
for f, (bite, pump) in enumerate(((0.0, 0.0), (1.0, 1.0), (0.2, 0.5), (0.9, 0.8), (0.0, 0.2))):
    p = pose(head=(18 - 14 * bite, 0, 0), jaw=(-6 - 36 * bite, 0, 0), jaw__loc=(0, 0.004, -0.02 * bite), ear_r=(30, 0, -18), ear_l=(30, 0, 18),
             paw_r=(40 - 20 * bite, 0, -14), paw_l=(40 - 20 * bite, 0, 14), foot_r=(-30, 0, 0), foot_l=(-30, 0, 0))
    p["body@scale"] = (1.16 + 0.05 * pump, 1.12, 1.18 - 0.04 * pump)
    p["quills@scale"] = (1.3, 1.3, 1.3)
    bury.key(f * 3, p, "LINEAR")
bury.close(15)

# (Wool's tile is a coat's: on a thing this small its weave would be a knit. Three times as fine, it's a fuzz.)
for part in kit.parts:
    part.f = [(idx, [(u * FINE, v * FINE) for u, v in uvs] if m.startswith("wool.") else uvs, m, sm) for idx, uvs, m, sm in part.f]
kit.build()
rig.bake(sk, [play, hop, nestle, bristle, bury])
print("[dt] huddle", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "huddle.glb", kit)
