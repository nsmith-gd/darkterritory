"""THE WAKER · the Peat Man (GDD §8 "A titan"; docs/design/creatures/wakers.md, wakers-model.md §B; queue #411, ARCHITECTURE
§8 note 679; the director, 13 Oct 2026: chose B, the Peat Man, and from the creature gym: "it needs to be this large
lumbering beast, at least 3 times as large as the current model, it should be a biped titan type thing, the train should be
struggling to outrun it because it has such a large gait, not because it itself is fast").

A man who lay in the peat until it tanned him to leather, grown by the Corruption to the size of a hill. He is shrunk over
long bones: the ribs and the spine stand out, the belly is sunk under the cage, and the rope is still round his neck with its
end hanging. His hair has gone red with the bog, his face is tight on the skull with the eyes gone to wet pits, and his jaw
hangs. His legs are too long (the Corruption "exaggerates whatever allows something to survive": these run trains down),
his arms hang past his knees, and his hands are big enough to close round a car. Peat and moss are still on his back.

He's modelled as a man 2.07 m tall and drawn thirty times that (CreatureArt.WakerScale): 62 m, three and a half times the
greybox stand-in's 17. A car (14.5 m) is the length of his hand and fingers, about 0.48 m here.

How he's made (the Look Review: organic, one skin, not primitives; the Householder's way, note 584): the body is one skin
of smooth volumes (tools/blender/flesh.py) settled onto their field before QuadriFlow, the lips' line opened again (rig.fuse's
`cut`) so the jaw can drop. Over it, finer than its quads: the eyes, the teeth, the rope, the hair, the long fingers and their
nails, the peat and the moss. His colour is baked into one atlas by tools/models/recipes/waker.py.

His rig (SK_Waker, 55 bones): root, pelvis, spine_01..03, neck, head, jaw, a `mouth` socket (where a car goes in: the catch
is fitted to it, CreatureArt.WakerCatch), the rope's hanging end (rope_01, rope_02); clavicles, arms, hands, four three-boned
fingers and a two-boned thumb a hand; thighs, calves, feet, toes. His rest is stood straight, facing +Y (the engine's -Z).

Clips (wakers.md; the Sim's phases: TELEGRAPH rising, COMMIT running, PUNISH holding):
  lie    face down under the land, breathing (6 s loop): the rise's first frame;
  rise   pushing up out of the ground on his arms, a knee under him, up (8 s: wakers.riseSeconds);
  walk   the slow, heavy walk he gets up into (4 s a cycle, 24 m a step: 12 m/s at 30x);
  run    the long bound he runs a train down in (3.4 s a cycle, 1.7 s and 44 m a step: 26 m/s, wakers.topSpeed, at 30x).
         CreatureArt blends the two by his speed and plays them at it, so his feet stay planted (WakerGait);
  grab   down on his knees over the line, both hands on the last car (2.5 s: wakers.liftSeconds);
  lift   holding it under his mouth, drawing it in, his jaw working, till he starts eating (2 s loop);
  eat    kneeling, biting the car off in four bites a car, his jaws shutting on frames 0, 30, 60, 90 (4 s loop: eatSeconds);
  stand  stood short of the walls, out of the guns' reach, swaying (6 s loop);
  roar   the howl at the walls, his jaw hanging (4 s).

    tools/models/build.sh waker   # this, with its colour baked -> content/art/models/waker.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/waker.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, chain, h01, mix, smooth01  # noqa: E402
from mathutils import Matrix, Quaternion, Vector  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, hexc  # noqa: E402

rig.reset()

FINGERS = ("a", "b", "c", "d")
SCALE = 30          # drawn this many times his modelled size (CreatureArt.WakerScale)
ANKLE = 0.08        # the ankle's height over the ground stood
HIP = 1.25          # the hip joints' height stood
WALK_STEP, WALK_FRAMES = 0.8, 120   # a step (m, modelled) and a cycle (two steps, frames): 0.4 m/s modelled, 12 m/s drawn
RUN_STEP, RUN_FRAMES = 1.473, 102   # 0.866 m/s modelled: 26 m/s drawn (enemies.json wakers.topSpeed)
EAT_FRAMES = 120    # enemies.json wakers.eatSeconds, 4 s
HEAD_C = Vector((0, 0.045, 1.93))   # the middle of his skull
LIPS = HEAD_C.z - 0.083             # the lips' line, where the skin is opened for the jaw
HINGE = Vector((0, 0.025, 1.875))   # the jaw's hinge, under the ears
MOUTH = Vector((0, 0.13, LIPS - 0.02))  # where a car goes in (the socket)


def waker_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, 0, HIP + 0.02), (0, 0, HIP + 0.11)),
        Bone("spine_01", "pelvis", (0, 0, HIP + 0.11), (0, -0.01, 1.47)),
        Bone("spine_02", "spine_01", (0, -0.01, 1.47), (0, -0.015, 1.59)),
        Bone("spine_03", "spine_02", (0, -0.015, 1.59), (0, -0.005, 1.7)),
        Bone("neck", "spine_03", (0, 0.0, 1.7), (0, 0.03, 1.82)),
        Bone("head", "neck", (0, 0.03, 1.82), (0, 0.04, 2.07)),
        Bone("jaw", "head", tuple(HINGE), (0, 0.15, LIPS - 0.065)),
        Bone("mouth", "head", tuple(MOUTH), tuple(MOUTH + Vector((0, 0.08, 0))), deform=False),
        # The rope's end, hanging from the knot at his throat down his chest.
        Bone("rope_01", "neck", (0.03, 0.075, 1.72), (0.035, 0.1, 1.6)),
        Bone("rope_02", "rope_01", (0.035, 0.1, 1.6), (0.035, 0.11, 1.47)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"clavicle_{s}", "spine_03", (sx * 0.03, 0.01, 1.67), (sx * 0.19, -0.01, 1.68)),
            Bone(f"upperarm_{s}", f"clavicle_{s}", (sx * 0.2, -0.01, 1.66), (sx * 0.23, -0.02, 1.23)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.23, -0.02, 1.23), (sx * 0.25, 0.02, 0.83)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.25, 0.02, 0.83), (sx * 0.26, 0.04, 0.7)),
            Bone(f"thumb_{s}_01", f"hand_{s}", (sx * 0.232, 0.06, 0.79), (sx * 0.228, 0.105, 0.735)),
            Bone(f"thumb_{s}_02", f"thumb_{s}_01", (sx * 0.228, 0.105, 0.735), (sx * 0.226, 0.125, 0.68)),
            Bone(f"thigh_{s}", "pelvis", (sx * 0.12, 0.0, HIP), (sx * 0.125, 0.02, 0.665)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.125, 0.02, 0.665), (sx * 0.125, -0.01, ANKLE)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.125, -0.01, ANKLE), (sx * 0.13, 0.16, 0.025)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.13, 0.16, 0.025), (sx * 0.13, 0.25, 0.02)),
        ]
        for i, f in enumerate(FINGERS):
            # Four long fingers off the knuckles, three joints each: the hand and fingers a car long.
            dx = (i - 1.5) * 0.021
            k0 = Vector((sx * (0.26 + dx * 0.35), 0.04 + 0.012 * (1 - abs(i - 1.5)) - 0.006 * i, 0.7))
            L = (0.066, 0.05, 0.04) if i in (1, 2) else (0.058, 0.044, 0.036)
            k1 = k0 + Vector((sx * dx * 0.2, 0.006, -L[0]))
            k2 = k1 + Vector((0, 0.008, -L[1]))
            k3 = k2 + Vector((0, 0.01, -L[2]))
            b += [Bone(f"finger_{f}_{s}_01", f"hand_{s}", tuple(k0), tuple(k1)),
                  Bone(f"finger_{f}_{s}_02", f"finger_{f}_{s}_01", tuple(k1), tuple(k2)),
                  Bone(f"finger_{f}_{s}_03", f"finger_{f}_{s}_02", tuple(k2), tuple(k3))]
    return Skeleton("SK_Waker", b)


sk = waker_skeleton()
sk.build()
kit = rig.Kit(sk, "waker")
SKIN_FACES = 4600


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/waker.py),
# the names say what to paint: the leathered skin, the face, the lips and gums, the teeth, the eyes, the nails, the hair,
# the rope, the peat and the moss on him.
SKIN = Mat("skin.waker", hexc("#2c1a10"), shine=0.3)
FACE = Mat("skin.waker_face", hexc("#2c1a10"), shine=0.3)
GUM = Mat("flesh.waker_gum", hexc("#1a0c0a"), shine=0.4)
TOOTH = Mat("skin.waker_tooth", hexc("#5a4630"), shine=0.4)
EYE = Mat("glass_dirty.waker_eye", hexc("#0c0c08"), shine=0.95)
NAIL = Mat("tar.waker_nail", hexc("#120c08"), shine=0.4)
HAIR = Mat("fleece.waker_hair", hexc("#5a2210"), shine=0.15)
ROPE = Mat("wool.waker_rope", hexc("#3a2e1e"), shine=0.05)
PEAT = Mat("fungal_crust.waker_peat", hexc("#1a120c"), shine=0.1)
MOSS = Mat("fungal_crust.waker_moss", hexc("#3a3a18"), shine=0.1)

# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "waker flesh")
SPINE_PTS = [H("pelvis") + Vector((0, 0, -0.08)), H("spine_01"), H("spine_02"), H("spine_03"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "spine_03", "neck", "head"], soft=0.06)
CAGE_C, CAGE_R = Vector((0, -0.005, 1.53)), Vector((0.155, 0.105, 0.16))


def trunk_w(p):
    """Up the spine; the shoulders with the clavicles and the upper arms, the hips with the thighs."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.z > 1.56 and ax > 0.09:
        k = smooth01(0.09, 0.17, ax) * smooth01(1.56, 1.64, p.z)
        w = mix(w, {f"clavicle_{s}": 1.0}, 0.5 * k)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.5 * k * smooth01(0.14, 0.2, ax))
    d = (p - H(f"thigh_{s}")).length
    if d < 0.17 and p.z < HIP + 0.08:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.55 * smooth01(0.17, 0.05, d) * smooth01(0.02, 0.07, ax))
    return w


def head_w(p):
    """The head, but for what's under the lips' line forward of the hinge: the jaw's (and the cheeks between, both)."""
    p = Vector(p)
    w = trunk_w(p)
    if p.z > 1.74 and p.y > HINGE.y - 0.04:
        below = smooth01(LIPS + 0.012, LIPS - 0.008, p.z)
        fwd = smooth01(HINGE.y - 0.03, HINGE.y + 0.03, p.y)
        throat = smooth01(1.76, 1.79, p.z)
        w = mix(w, {"jaw": 1.0}, below * fwd * throat)
    return w


def jaw_w(p):
    return mix(head_w(p), {"jaw": 1.0}, 0.85)


# The hips and the belly: narrow, the hip bones standing, the belly sunk under the cage.
flesh.blob((0, 0.0, HIP - 0.02), (0.15, 0.1, 0.1), 0, SKIN, trunk_w)                                      # the hips
flesh.blob((0, -0.015, HIP + 0.12), (0.105, 0.075, 0.09), 0.06, SKIN, trunk_w)                            # the waist
flesh.carve((0, 0.105, HIP + 0.13), (0.08, 0.04, 0.07), 0.03)                                             # the sunk belly
for sx in (1, -1):
    flesh.blob((sx * 0.11, 0.03, HIP + 0.05), (0.035, 0.035, 0.03), 0.03, SKIN, trunk_w)                  # a hip bone
    flesh.blob((sx * 0.08, -0.07, HIP - 0.04), (0.06, 0.05, 0.07), 0.05, SKIN, trunk_w)                   # a buttock, wasted
# The cage: barrelled and bare, every rib standing out of it, sloping down to the front.
flesh.blob(tuple(CAGE_C), tuple(CAGE_R), 0.07, SKIN, trunk_w)
flesh.blob((0, -0.01, 1.66), (0.18, 0.09, 0.07), 0.06, SKIN, trunk_w)                                     # the chest's top
RIBS = []
for i in range(7):
    z0 = 1.64 - i * 0.034
    q = (z0 - CAGE_C.z) / CAGE_R.z
    if abs(q) >= 0.98:
        continue
    k = math.sqrt(1 - q * q)
    for sx in (1, -1):
        pts = []
        for a in range(168, 18, -15):
            th = math.radians(a)
            z = z0 - 0.055 * (1 - a / 180) ** 1.2
            qq = (z - CAGE_C.z) / CAGE_R.z
            kk = math.sqrt(max(0.05, 1 - qq * qq))
            pts.append(Vector((sx * CAGE_R.x * kk * math.sin(th) * 1.01, CAGE_C.y + CAGE_R.y * kk * math.cos(th) * 1.01, z)))
        RIBS.append(pts)
        flesh.limb(pts, [0.009 + 0.004 * k] * len(pts), 0.008, SKIN, trunk_w, sides=8)
# The spine down his back, each knob of it; the shoulder blades winged out.
for i in range(13):
    z = HIP + 0.04 + i * 0.033
    q = (z - CAGE_C.z) / CAGE_R.z
    y = CAGE_C.y - CAGE_R.y * math.sqrt(max(0.3, 1 - q * q)) - 0.005
    flesh.blob((0, y, z), (0.02, 0.016, 0.014), 0.012, SKIN, trunk_w)
for sx in (1, -1):
    flesh.blob((sx * 0.095, -0.1, 1.6), (0.06, 0.022, 0.08), 0.03, SKIN, trunk_w, rot=R(rz=-sx * 12))      # a shoulder blade
    flesh.blob((sx * 0.2, -0.01, 1.66), (0.058, 0.06, 0.055), 0.04, SKIN, trunk_w)                         # a shoulder
    flesh.limb([Vector((sx * 0.025, 0.07, 1.68)), Vector((sx * 0.1, 0.06, 1.7)), Vector((sx * 0.19, 0.02, 1.7))],
               [0.014, 0.013, 0.015], 0.012, SKIN, trunk_w, sides=8)                                       # a collarbone

# The arms: long, thin, the elbows knobbed, the forearms corded, the hands big.
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([H(f"clavicle_{s}"), sh, el, wr, tip], [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.05)
    flesh.limb([sh + Vector((0, 0, 0.01)), sh.lerp(el, 0.45), el], [0.05, 0.04, 0.034], 0.04, SKIN,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(1.58, 1.66, p[2]) * 0.6))
    flesh.blob(el + Vector((0, -0.01, 0)), (0.034, 0.032, 0.036), 0.02, SKIN, arm, around=12, rings=8)   # the elbow
    flesh.limb([el, el.lerp(wr, 0.35), wr], [0.034, 0.03, 0.021], 0.02, SKIN, arm)
    flesh.blob(wr + Vector((0, 0, -0.004)), (0.026, 0.02, 0.022), 0.012, SKIN, arm, around=10, rings=6)     # the wrist
    flesh.blob(wr.lerp(tip, 0.55), (0.032, 0.017, 0.07), 0.015, SKIN, arm, around=14, rings=7)            # the palm

# The legs: too long, thin, the knees standing out, down to long bony feet.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.05)
    flesh.limb([hip + Vector((0, 0, 0.02)), hip.lerp(kn, 0.4), kn], [0.075, 0.058, 0.045], 0.05, SKIN,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(HIP - 0.08, HIP, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.012, 0)), (0.046, 0.045, 0.05), 0.02, SKIN, leg, around=12, rings=8)     # the knee
    flesh.limb([kn, kn.lerp(an, 0.3), an + Vector((0, 0, 0.03))], [0.045, 0.042, 0.028], 0.02, SKIN, leg)
    flesh.blob(an + Vector((0, -0.02, -0.02)), (0.03, 0.04, 0.03), 0.02, SKIN, leg)                        # the heel
    flesh.blob(an.lerp(ball, 0.5) + Vector((0, 0, -0.03)), (0.042, 0.1, 0.026), 0.025, SKIN, leg)        # the foot
    flesh.blob(ball + Vector((0, 0.03, -0.004)), (0.045, 0.045, 0.02), 0.02, SKIN, leg)                   # its ball

# The neck: long and thin, the cords standing; the head: the skull tight under the leather, the brow and the cheekbones
# standing, the cheeks sunk, the eyes gone to pits, the nose collapsed, the lips shrunk back off the teeth, the jaw hanging.
flesh.limb([H("neck") + Vector((0, -0.005, -0.01)), H("neck").lerp(T("neck"), 0.5), T("neck") + Vector((0, 0.005, 0.02))],
           [0.06, 0.052, 0.055], 0.03, SKIN, trunk_w)
for sx in (1, -1):
    flesh.limb([Vector((sx * 0.05, 0.0, 1.86)), Vector((sx * 0.035, 0.04, 1.77)), Vector((sx * 0.018, 0.07, 1.69))],
               [0.013, 0.014, 0.012], 0.012, SKIN, trunk_w, sides=8)                                       # a cord
flesh.blob(HEAD_C + Vector((0, -0.01, 0.035)), (0.086, 0.104, 0.104), 0.04, FACE, head_w, around=20, rings=12)  # the skull
flesh.blob(HEAD_C + Vector((0, 0.05, -0.03)), (0.062, 0.05, 0.065), 0.04, FACE, head_w)                         # the face
flesh.blob(HEAD_C + Vector((0, 0.088, 0.03)), (0.066, 0.02, 0.02), 0.02, FACE, head_w)                          # the brow
flesh.blob(HEAD_C + Vector((0, 0.08, -0.066)), (0.046, 0.034, 0.018), 0.02, GUM, head_w)                       # the upper jaw
flesh.blob(HEAD_C + Vector((0, 0.108, -0.018)), (0.014, 0.012, 0.022), 0.015, FACE, head_w)                     # the nose
for sx in (1, -1):
    flesh.blob(HEAD_C + Vector((sx * 0.056, 0.07, -0.008)), (0.022, 0.026, 0.016), 0.015, FACE, head_w)       # a cheekbone
    flesh.carve(HEAD_C + Vector((sx * 0.05, 0.084, -0.05)), (0.02, 0.016, 0.03), 0.015)                       # the sunk cheek
    flesh.carve(HEAD_C + Vector((sx * 0.032, 0.094, 0.006)), (0.023, 0.017, 0.017), 0.008)                    # an eye's pit
    flesh.carve(HEAD_C + Vector((sx * 0.007, 0.12, -0.036)), (0.006, 0.008, 0.006), 0.003)                    # a nostril
    flesh.blob(HEAD_C + Vector((sx * 0.084, 0.0, -0.012)), (0.012, 0.022, 0.03), 0.012, FACE, head_w)         # a shrivelled ear
# The jaw: long, the chin bony, hanging a little open even shut.
flesh.blob(HEAD_C + Vector((0, 0.075, -0.115)), (0.05, 0.05, 0.028), 0.03, FACE, jaw_w)                        # the mandible
flesh.blob(HEAD_C + Vector((0, 0.11, -0.128)), (0.026, 0.022, 0.02), 0.02, FACE, jaw_w)                        # the chin
flesh.blob(HEAD_C + Vector((0, 0.085, -0.1)), (0.04, 0.03, 0.012), 0.02, GUM, jaw_w)                           # the lower gum
for sx in (1, -1):
    flesh.limb([HINGE + Vector((sx * 0.06, 0.0, -0.01)), HEAD_C + Vector((sx * 0.052, 0.05, -0.115)),
                HEAD_C + Vector((sx * 0.025, 0.1, -0.13))], [0.016, 0.015, 0.013], 0.015, FACE, jaw_w, sides=8)  # its ramus


def mouth_cut(c):
    """The lips' line, opened again after the fuse: the jaw drops away from it."""
    return abs(c.z - LIPS) < 0.0075 and c.y > HEAD_C.y + 0.07 and abs(c.x) < 0.042


# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the eyes in their pits, the throat behind the lips, the teeth, the rope, the hair, the fingers and
# nails, the toes, and the peat and the moss on his back.
fine = kit.part("fine")
EYES = []
for sx in (1, -1):
    e = HEAD_C + Vector((sx * 0.032, 0.08, 0.004))
    EYES.append(e)
    fine.blob(e, (0.0105, 0.008, 0.009), 10, 6, EYE, "head")
# The dark of his throat, seen through the lips when the jaw drops.
fine.blob(HEAD_C + Vector((0, 0.05, -0.088)), (0.04, 0.045, 0.028), 10, 6, GUM, lambda p: {"head": 0.5, "jaw": 0.5})
TEETH = []
GONE = {("upper", 2), ("upper", 5), ("lower", 1), ("lower", 4)}
for row, z, bone, n in (("upper", LIPS + 0.004, "head", 8), ("lower", LIPS - 0.01, "jaw", 7)):
    for k in range(n):
        if (row, k) in GONE:
            continue
        a = math.radians(-58 + 116 * k / (n - 1))
        p = HEAD_C + Vector((0.037 * math.sin(a), 0.08 + 0.03 * math.cos(a), z - HEAD_C.z))
        TEETH.append(p)
        long = 0.008 + 0.006 * h01(4101, k, 1 if row == "upper" else 2)
        wide = 0.004 + 0.0016 * h01(4103, k, 3 if row == "upper" else 4)
        fine.blob(p + Vector((0, 0, -0.004 if row == "upper" else 0.004)), (wide, 0.0042, long), 6, 4, TOOTH, bone,
                  rot=R(rx=(-8 if row == "upper" else 8) + 26 * (h01(4102, k, z) - 0.5), ry=20 * (h01(4104, k, z) - 0.5),
                        rz=math.degrees(a) * 0.3))
# The noose: a loop of rope round his throat, the knot under his left ear, its end hanging down his chest (rope_01, _02).
neck_c = H("neck").lerp(T("neck"), 0.45)
ring = [neck_c + Vector((0.064 * math.cos(2 * math.pi * k / 16), 0.006 + 0.06 * math.sin(2 * math.pi * k / 16),
                         0.01 * math.sin(2 * math.pi * k / 16 + 1.2))) for k in range(16)]
fine.tube(ring, [0.011] * 16, 6, ROPE, "neck", loop=True, ref=(0, 0, 1))
knot = neck_c + Vector((-0.05, 0.045, -0.01))
fine.blob(knot, (0.02, 0.018, 0.024), 8, 6, ROPE, "neck")
end = [knot + Vector((0.01, 0.01, -0.02)), H("rope_01").lerp(T("rope_01"), 0.5), H("rope_02"), H("rope_02").lerp(T("rope_02"), 0.6),
       T("rope_02") + Vector((0, 0.005, -0.01))]
fine.tube(end, [0.01, 0.01, 0.0095, 0.009, 0.012], 6, ROPE, chain(end, ["neck", "rope_01", "rope_02", "rope_02"], soft=0.02),
          ref=(1, 0, 0), cap0="point", cap1="point")
# The hair: matted, gone red with the bog, in ropes down the back of his skull and onto his neck.
HAIR_TIPS = []
for k in range(16):
    a = math.radians(-150 + 300 * k / 15)
    root = HEAD_C + Vector((0.07 * math.sin(a), -0.01 + 0.055 * math.cos(a) - 0.05, 0.085 - 0.02 * abs(math.sin(a))))
    hang = 0.12 + 0.08 * h01(4110, k)
    pts = [root, root + Vector((0.01 * math.sin(a), -0.035, -0.035)), root + Vector((0.015 * math.sin(a), -0.05, -hang * 0.6)),
           root + Vector((0.02 * math.sin(a), -0.05, -hang))]
    HAIR_TIPS.append(pts[-1])
    fine.tube(pts, [0.009, 0.008, 0.006, 0.002], 5, HAIR, lambda p: {"head": 0.75, "neck": 0.25} if p[2] < 1.88 else {"head": 1.0},
              ref=(0, 0, 1), cap0="point", cap1="point")
# The fingers: long, knuckled, the nails long and black.
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for f in FINGERS:
        k0, k1, k2, k3 = H(f"finger_{f}_{s}_01"), H(f"finger_{f}_{s}_02"), H(f"finger_{f}_{s}_03"), T(f"finger_{f}_{s}_03")
        a = k0 - (k1 - k0).normalized() * 0.02
        bones = chain([a, k1, k2, k3], [f"finger_{f}_{s}_01", f"finger_{f}_{s}_02", f"finger_{f}_{s}_03"], soft=0.012)
        nail = k3 + (k3 - k2).normalized() * 0.012
        TIPS.append(nail)
        fine.tube([a, a.lerp(k1, 0.5), k1, k1.lerp(k2, 0.5), k2, k2.lerp(k3, 0.6), nail],
                  [0.0125, 0.011, 0.0118, 0.0102, 0.0106, 0.0088, 0.004], 6, SKIN, bones, ref=(0, 1, 0), cap0="point",
                  cap1="point", fmat=lambda pts, n, k3=k3: NAIL if (sum(pts, Vector()) / len(pts) - k3).length < 0.014 else SKIN)
    t0, t1, t2 = H(f"thumb_{s}_01"), H(f"thumb_{s}_02"), T(f"thumb_{s}_02")
    a = t0 - (t1 - t0).normalized() * 0.014
    fine.tube([a, t1, t1.lerp(t2, 0.6), t2 + (t2 - t1).normalized() * 0.01], [0.015, 0.0125, 0.01, 0.004], 6, SKIN,
              chain([a, t1, t2], [f"thumb_{s}_01", f"thumb_{s}_02"], soft=0.012), ref=(0, 1, 0), cap0="point", cap1="point",
              fmat=lambda pts, n, t2=t2: NAIL if (sum(pts, Vector()) / len(pts) - t2).length < 0.012 else SKIN)
    # The toes: long and clawed, curled into the ground.
    ball, tt = H(f"toe_{s}"), T(f"toe_{s}")
    for i in range(4):
        dx = sx * (-0.024 + 0.016 * i)
        r0 = ball + Vector((dx, 0.0, 0.01))
        pts = [r0, r0 + Vector((dx * 0.1, 0.04 - 0.006 * i, 0.004)), r0 + Vector((dx * 0.15, 0.075 - 0.012 * i, -0.008))]
        fine.tube(pts, [0.011 - 0.001 * i, 0.009, 0.004], 5, SKIN, f"toe_{s}", ref=(0, 0, 1), cap0="point", cap1="point",
                  fmat=lambda pts, n, e=pts[-1]: NAIL if (sum(pts, Vector()) / len(pts) - e).length < 0.012 else SKIN)
# The peat and the moss still on him: clods on his shoulders and down his back, moss on the clods, a beard of it hanging.
CLODS = []
for k in range(14):
    u = h01(4120, k)
    z = 1.32 + 0.38 * h01(4121, k)
    x = (u - 0.5) * 0.3
    q = (z - CAGE_C.z) / CAGE_R.z
    y = CAGE_C.y - CAGE_R.y * math.sqrt(max(0.25, 1 - q * q)) * (0.85 + 0.2 * (1 - abs(x) / 0.16)) + 0.01
    if k < 4:  # on the shoulders
        x, y, z = (0.17 if k % 2 else -0.17) + 0.03 * (u - 0.5), -0.03 + 0.03 * h01(4122, k), 1.71
    p = Vector((x, y, z))
    CLODS.append(p)
    r = 0.022 + 0.022 * h01(4123, k)
    fine.rock(p, r, PEAT if k % 3 else MOSS, trunk_w, 4130 + k, spike=0.6)
for k in range(8):
    p = CLODS[k] + Vector((0, -0.01, -0.01))
    fine.tube([p, p + Vector((0.004, -0.012, -0.05)), p + Vector((0.008, -0.01, -0.1 - 0.05 * h01(4140, k)))],
              [0.007, 0.005, 0.0015], 4, MOSS, trunk_w, ref=(0, 0, 1), cap0="point", cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest is stood straight; angles in the armature's axes: +X swings a hanging limb forward and tips a spine or
# the head back; the jaw drops with -X; a finger curls with -X.

def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


def curl(k, thumb=0.0, sides=("r", "l")):
    out = {}
    for s in sides:
        for i, f in enumerate(FINGERS):
            out[f"finger_{f}_{s}_01"] = (-k * (0.7 + 0.1 * i), 0, 0)
            out[f"finger_{f}_{s}_02"] = (-k, 0, 0)
            out[f"finger_{f}_{s}_03"] = (-k * 0.8, 0, 0)
        out[f"thumb_{s}_01"] = (-thumb, 0, 0)
        out[f"thumb_{s}_02"] = (-thumb, 0, 0)
    return out


def euler(q):
    return tuple(round(math.degrees(a), 2) for a in q.to_euler("YXZ"))


def mirrored(pose):
    return rig.mirror({k: v for k, v in pose.items() if not rig._split(k)[0].endswith("_l")})


def _frame(t, x):
    t = t.normalized()
    x = (x - t * t.dot(x)).normalized()
    return Matrix((t, x, t.cross(x))).transposed()


def ik2(pose, upper, lower, target, pole, bulge):
    """Exactly where it's wanted (rig.reach's descent trades its reach for its pole, and a titan's foot a few centimetres
    off is metres): the two bones' joint put in the plane of `pole`, bending the way it bends at rest (`bulge`: a knee
    forward, an elbow back), the end of `lower` on `target` (or as near as the limb reaches)."""
    p = dict(pose)
    hip = rig.pose_points(sk, p, [(upper, "head")])[0]
    A, P = Vector(target), Vector(pole)
    t0, c0 = sk[upper].tail - sk[upper].head, sk[lower].tail - sk[lower].head
    L1, L2 = t0.length, c0.length
    a = A - hip
    d = max(abs(L1 - L2) + 1e-4, min(a.length, (L1 + L2) * 0.9995))
    a.normalize()
    w = (P - hip) - a * (P - hip).dot(a)
    if w.length < 1e-6:
        w = Vector(bulge) - a * Vector(bulge).dot(a)
    w.normalize()
    u = (L1 * L1 - L2 * L2 + d * d) / (2 * d)
    K = hip + a * u + w * math.sqrt(max(0.0, L1 * L1 - u * u))
    t1, c1 = K - hip, hip + a * d - K
    x0, x1 = t0.cross(Vector(bulge)), t1.cross(w)
    up_w = _frame(t1, x1) @ _frame(t0, x0).transposed()
    lo_w = _frame(c1, x1) @ _frame(c0, x0).transposed()
    parent = sk[upper].parent
    pw = rig.world_rotation(sk, p, parent).to_matrix() if parent else Matrix.Identity(3)
    p[upper] = (pw.inverted() @ up_w).to_quaternion()
    p[lower] = (up_w.transposed() @ lo_w).to_quaternion()
    return p


def ankle_at(pose, s, target, pole, pitch=0.0):
    """The leg's ankle to `target` (the knee toward `pole`), the foot set at `pitch` degrees to the ground (+ toes down)."""
    p = ik2(pose, f"thigh_{s}", f"calf_{s}", target, pole, (0, 1, 0))
    p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", -pitch, 0, 0)
    return p


def hand_to(pose, s, wrist, pole):
    return ik2(pose, f"upperarm_{s}", f"lowerarm_{s}", wrist, pole, (0, -1, 0))


HANG = curl(14, 6)


def hunch(k, look=0.0, jaw=8.0):
    """Bent over by `k` (0 stood straight .. 1 his run's hunch): the back curved, the head pushed forward and up to see."""
    return {"pelvis": (-18 * k, 0, 0), "spine_01": (-14 * k, 0, 0), "spine_02": (-16 * k, 0, 0), "spine_03": (-12 * k, 0, 0),
            "neck": (30 * k + look, 0, 0), "head": (18 * k + look * 0.5, 0, 0), "jaw": (-jaw, 0, 0),
            "rope_01": (30 * k, 0, 0), "rope_02": (6 * k, 0, 0)}


def _rx(v, deg):
    a = math.radians(deg)
    return Vector((v.x, v.y * math.cos(a) - v.z * math.sin(a), v.y * math.sin(a) + v.z * math.cos(a)))


FOOT_V = H("toe_r") - H("foot_r")          # the ankle to the ball of the foot
TIP_V = T("toe_r") - H("foot_r")           # the ankle to the toes' tips


def rolled(x, y, pitch):
    """The ankle of a foot whose ball is where a flat foot's would be with its ankle at (x, y), its heel up `pitch` degrees."""
    ball = Vector((x + math.copysign(FOOT_V.x, x), y + FOOT_V.y, ANKLE + FOOT_V.z))
    return ball - _rx(Vector((math.copysign(FOOT_V.x, x), FOOT_V.y, FOOT_V.z)), -pitch)


def clear(ankle, sx, pitch, least=0.012):
    """`ankle` raised so a foot at `pitch` keeps its ball and its toes out of the ground."""
    lo = min((ankle + _rx(Vector((sx * v.x, v.y, v.z)), -pitch)).z for v in (FOOT_V, TIP_V))
    return ankle + Vector((0, 0, max(0.0, least - lo)))


def gait(phi, step, stance, hip, bob, low, lean, lift, arm, out=0.125, heavy=1.0):
    """One frame of a stride at `phi` (0..1 a cycle: 0 the right foot comes down, 0.5 the left). Each foot's planted for
    `stance` of the cycle, carried back the whole step's ground (`step` m a step) at the cycle's speed, so whatever plays it
    at the speed it's authored for keeps the feet still on the ground. The hips are lowest `low` into a step, `bob` under
    `hip`; the hunch `lean`; a swinging foot comes `lift` off the ground; the arms swing `arm` degrees, heavy, behind the legs."""
    A = stance * step
    t2 = (2 * phi) % 1.0
    z = hip - bob * math.cos(2 * math.pi * (t2 - low))
    side = 2 * (((phi + 0.25) % 1.0) < 0.5) - 1      # which foot's under him: +1 right, -1 left
    sway = 0.012 * math.sin(2 * math.pi * (phi - 0.08))
    p = with_(HANG, hunch(lean, jaw=8 + 6 * heavy * max(0.0, math.cos(2 * math.pi * (t2 - low)))),
              {"root@loc": (sway, 0, z - HIP), "pelvis": (-10 * lean - 3 * heavy * math.cos(2 * math.pi * (t2 - low)), 4 * side * heavy,
                                                         7 * math.sin(2 * math.pi * phi))})
    p["spine_02"] = (p["spine_02"][0], -2 * side * heavy, -6 * math.sin(2 * math.pi * phi))
    p["spine_03"] = (p["spine_03"][0], -2 * side * heavy, -4 * math.sin(2 * math.pi * phi))
    # The head comes down after each foot does, a beat late.
    nod = 4 * heavy * math.cos(2 * math.pi * (t2 - low - 0.08))
    p["neck"] = (p["neck"][0] + nod, 0, 2 * math.sin(2 * math.pi * phi))
    for s, sx, off in (("r", 1, 0.0), ("l", -1, 0.5)):
        u = (phi + off) % 1.0
        if u < stance:
            # Planted, carried back with the ground; at the end the heel comes up, the foot rolling over its ball.
            k = u / stance
            y = A - 2 * A * k
            pitch = 40 * smooth01(0.7, 1.0, k)
            ankle = rolled(sx * out, y, pitch)
        else:
            k = (u - stance) / (1 - stance)
            e = smooth01(0.0, 1.0, k)
            y = -A + (2 * A + 0.04) * e - 0.04 * smooth01(0.85, 1.0, k)
            zf = ANKLE + lift * math.sin(math.pi * min(1.0, k * 1.1)) ** 0.7
            pitch = 40 * (1 - smooth01(0.0, 0.35, k)) - 18 * smooth01(0.6, 0.95, k)
            ankle = clear(Vector((sx * out, y, zf)), sx, pitch)
        p = ankle_at(p, s, ankle, (sx * 0.14, 0.5 * y + 0.5, 0.65), pitch)
        if u < stance:
            p[f"toe_{s}"] = rig.hang(sk, p, f"toe_{s}", 0, 0, 0)
        # The arm on this side swings against this leg: forward as the leg goes back.
        sw = math.cos(2 * math.pi * (u - 0.5 - 0.06))
        p[f"upperarm_{s}"] = (arm * sw + 6 * lean, -sx * 8, 0)
        p[f"lowerarm_{s}"] = (14 + 10 * lean + arm * 0.45 * max(0.0, sw), 0, 0)
        p[f"hand_{s}"] = (6 - 6 * sw, 0, 0)
    return p


def cycle(name, frames, keys, **kw):
    """A gait keyed on every frame: the joints' rotations eased between keys would carry a planted foot off the ground."""
    c = Clip(name)
    for i in range(keys):
        c.key(round(frames * i / keys), gait(i / keys, **kw), "LINEAR")
    c.close(frames)
    return c


# Walk (4 s a cycle, 12 m/s drawn): the slow, heavy walk: double support, the hips dropping as each foot takes him, the
# long arms hanging and swinging late, the head pushed forward and nodding a beat after each footfall.
walk = cycle("walk", WALK_FRAMES, WALK_FRAMES, step=WALK_STEP, stance=0.6, hip=1.17, bob=0.03, low=0.12, lean=0.75, lift=0.11, arm=16)
# Run (3.4 s a cycle, 26 m/s drawn): the long bound: each foot planted a third of the cycle and carried the length of his
# leg, the knee folding high as it swings through, a short float between, the hips dropping hard as each foot comes down,
# hunched right over, the arms swinging long and heavy.
run = cycle("run", RUN_FRAMES, RUN_FRAMES, step=RUN_STEP, stance=0.353, hip=1.12, bob=0.035, low=0.2, lean=1.0, lift=0.3, arm=34)


def kneel(lean=0.6, sit=0.0, knees=0.17, spread=0.0, jaw=10.0, up=(0.0, 0.0)):
    """Down on his knees astride the line, the shins flat behind him; sat back toward his heels by `sit`; bowed by `lean`.
    `up`: how far each knee (right, left) is lifted off the ground, shuffling him on."""
    hipz = 0.6 - 0.16 * sit
    p = with_(HANG, hunch(lean, look=10 * lean, jaw=jaw), {"root@loc": (0, -0.05 - 0.1 * sit, hipz - HIP)})
    for s, sx, lift in (("r", 1, up[0]), ("l", -1, up[1])):
        h = rig.pose_points(sk, p, [(f"thigh_{s}", "head")])[0]
        L1 = (T(f"thigh_{s}") - H(f"thigh_{s}")).length
        L2 = (T(f"calf_{s}") - H(f"calf_{s}")).length
        kz = 0.05 + 0.06 * lift
        dx = sx * (knees + spread) - h.x
        dy = math.sqrt(max(0.0, L1 * L1 - (h.z - kz) ** 2 - dx * dx))
        knee = Vector((h.x + dx, h.y + dy, kz))
        ankle = knee + Vector((0, -L2 * math.cos(math.radians(2 + 10 * lift)), 0.01 + L2 * math.sin(math.radians(2 + 10 * lift))))
        p = ik2(p, f"thigh_{s}", f"calf_{s}", ankle, knee + Vector((0, 0.3, -0.2)), (0, 1, 0))
        p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", 200, 0, 0)
    return p


def holding(pose, lift=0.0, pull=0.0):
    """Both hands round a car's end under his mouth, `lift` higher and `pull` further in to it."""
    p = dict(pose)
    m = rig.pose_points(sk, p, [("mouth", "head")])[0]
    for s, sx in (("r", 1), ("l", -1)):
        w = m + Vector((sx * 0.07, 0.15 - 0.06 * pull, -0.1 + 0.04 * lift))
        p = hand_to(p, s, w, w + Vector((sx * 0.5, -0.2, -0.3)))
        p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}", 105, 0, -sx * 15)
    p.update(curl(55, 40))
    return p


# Lie (6 s, loop): face down under the land, his arms at his sides and his head turned on its cheek; his back rising and
# falling, slowly.
def flat_legs(p, pole_down=0.3):
    """Both legs laid out straight behind him on the ground, the feet flat on their tops."""
    for s, sx in (("r", 1), ("l", -1)):
        h = rig.pose_points(sk, p, [(f"thigh_{s}", "head")])[0]
        p = ik2(p, f"thigh_{s}", f"calf_{s}", (sx * 0.16, h.y - 1.3, 0.05), (sx * 0.16, h.y - 0.6, -pole_down), (0, 1, 0))
        p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", 200, 0, 0)
    return p


def prone(breath=0.0, reach=0.0):
    p = with_(HANG, {"root": (-90, 0, 0), "root@loc": (0, -HIP - 0.1, 0.05 + 0.01 * breath),
                     "spine_02": (2 * breath, 0, 0), "spine_03": (2 * breath, 0, 0), "neck": (6 * reach, 0, 22 * (1 - reach)),
                     "head": (8 * reach, 0, 22 * (1 - reach)), "jaw": (-6 - 10 * reach, 0, 0)})
    p = flat_legs(p)
    sh = rig.pose_points(sk, p, [("upperarm_r", "head")])[0]
    for s, sx in (("r", 1), ("l", -1)):
        # By his sides; reaching, drawn up to plant either side of his chest.
        side = Vector((sx * 0.3, sh.y - 0.65, 0.04))
        plant = Vector((sx * 0.36, sh.y + 0.05, 0.04))
        w = side.lerp(plant, reach)
        p = hand_to(p, s, w, Vector((sx * 0.6, w.y - 0.2 + 0.4 * reach, 0.5)))
        p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}", 90 * reach, 0, 0)
    return p


lie = Clip("lie")
lie.key(0, prone(0), "BEZIER").key(90, prone(1), "BEZIER").close(180)


def planted_hands(p, y, spread=0.34, out=0.6):
    for s, sx in (("r", 1), ("l", -1)):
        sh = rig.pose_points(sk, p, [(f"upperarm_{s}", "head")])[0]
        p = hand_to(p, s, (sx * spread, y, 0.05), sh + Vector((sx * out, -0.3, 0.4)))
        # The hand flat on the ground, the fingers forward.
        p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}", 90, 0, 0)
    p.update(curl(10, 4))
    return p


# Rise (8 s: wakers.riseSeconds): he pushes himself up out of the ground on his arms, face first, the peat sliding off his
# back (CreatureArt sinks him under the land at the start); comes up on his hands and knees, gets a foot under him, and
# stands, hunched, into his walk.
rise = Clip("rise", loop=False)
rise.key(0, prone(0), "BEZIER")
rise.key(30, prone(0.2, reach=1.0), "BEZIER")
# Pushed up off the ground on his arms, the chest up and the head back, the hips still down, the legs still flat.
PUSH = with_(HANG, {"root": (-62, 0, 0), "root@loc": (0, -1.2, -0.48), "pelvis": (-6, 0, 0), "spine_01": (8, 0, 0),
                    "spine_02": (10, 0, 0), "spine_03": (6, 0, 0), "neck": (24, 0, 0), "head": (18, 0, 0), "jaw": (-30, 0, 0)})
PUSH = planted_hands(flat_legs(PUSH, 0.6), 0.5, out=0.7)
rise.key(80, PUSH, "BEZIER")
# On his hands and knees, then a foot under him.
KNEE = kneel(lean=2.0, sit=0.0, jaw=24)
h = rig.pose_points(sk, KNEE, [("thigh_r", "head")])[0]
KNEE = ankle_at(KNEE, "r", (0.15, h.y + 0.42, ANKLE), (0.18, h.y + 0.8, 0.6), 0)
KNEE = planted_hands(KNEE, 0.62)
rise.key(130, KNEE, "BEZIER")
# Up on his feet, crouched, the knuckles still on the ground.
SQUAT = with_(HANG, hunch(2.0, look=16, jaw=16), {"root@loc": (0, -0.12, 0.74 - HIP)})
for s, sx in (("r", 1), ("l", -1)):
    SQUAT = ankle_at(SQUAT, s, (sx * 0.16, 0.08 if s == "r" else -0.06, ANKLE), (sx * 0.22, 0.6, 0.6), 0)
SQUAT = planted_hands(SQUAT, 0.5, spread=0.3)
rise.key(175, SQUAT, "BEZIER")
rise.key(240, gait(0.0, step=WALK_STEP, stance=0.6, hip=1.17, bob=0.03, low=0.12, lean=0.75, lift=0.11, arm=16), "BEZIER")

# Grab (2.5 s: wakers.liftSeconds): off his last bound he drops to his knees over the line, bowed right down, both hands
# round the last car's end; and lifts it to his mouth (CreatureArt bows him to wherever the car's end is: WakerCatch).
grab = Clip("grab", loop=False)
grab.key(0, run.keys[0][1], "BEZIER")
LUNGE = with_(HANG, hunch(1.8, look=14, jaw=30), {"root@loc": (0, 0.1, 0.78 - HIP)})
LUNGE = ankle_at(LUNGE, "r", (0.15, 0.4, ANKLE), (0.18, 0.8, 0.6), 0)
LUNGE = ankle_at(LUNGE, "l", (-0.16, -0.6, 0.16), (-0.18, 0.0, -0.2), 0)
LUNGE["foot_l"] = rig.hang(sk, LUNGE, "foot_l", 150, 0, 0)
for s, sx in (("r", 1), ("l", -1)):
    LUNGE = hand_to(LUNGE, s, (sx * 0.12, 0.9, 0.25), (sx * 0.6, 0.3, 0.9))
LUNGE.update(curl(20, 10))
grab.key(14, LUNGE, "BEZIER")
grab.key(30, holding(kneel(lean=1.8, sit=0.3, jaw=34), lift=0.0), "BEZIER")
grab.key(75, holding(kneel(lean=1.5, sit=0.5, jaw=20), lift=0.5), "BEZIER")


# Lift (2 s, loop: from his grab till he starts eating, wakers.eatAfter): kneeling over the line, the car's end in both hands
# under his mouth, drawing it in; his jaw working wider, not shutting yet.
def feeding(jaw, lift, pull, chew=0.0, knee=0.0, side=1):
    p = kneel(lean=1.5 - 0.1 * chew, sit=0.5, jaw=jaw, up=(knee, 0.0) if side > 0 else (0.0, knee))
    p["neck"] = (p["neck"][0] + 10 * chew, 0, 3 * chew * side)
    p["head"] = (p["head"][0] + 8 * chew, 0, 4 * chew * side)
    return holding(p, lift=lift, pull=pull)


lift = Clip("lift")
lift.key(0, feeding(jaw=24, lift=0.5, pull=0.2), "BEZIER")
lift.key(30, feeding(jaw=44, lift=0.7, pull=0.5, chew=0.3, side=-1), "BEZIER")
lift.close(60)

# Eat (4 s, loop: wakers.eatSeconds): the car bitten off in four bites, his jaws shutting on frames 0, 30, 60 and 90 (the
# bite's frontier steps a quarter of the car on each, CreatureArt.WakerBite): each time his jaw drops wide, his head comes
# onto the car, the jaws snap shut and he wrenches his head back with the bite, chewing, his knees shuffling him on after
# the train (CreatureArt moves him on down the car as it goes).
eat = Clip("eat")
for n in range(4):
    base, side = 30 * n, (1 if n % 2 else -1)
    eat.key(base, feeding(jaw=4, lift=0.6, pull=0.8, chew=0.9, side=side), "CONSTANT")
    eat.key(base + 8, feeding(jaw=12, lift=0.45, pull=0.5, chew=0.5, side=-side, knee=1.0 if n in (1, 3) else 0.0), "BEZIER")
    eat.key(base + 20, feeding(jaw=46, lift=0.7, pull=0.15, knee=0.4 if n in (1, 3) else 0.0, side=-side), "BEZIER")
    eat.key(base + 27, feeding(jaw=56, lift=0.8, pull=0.25), "LINEAR")
eat.close(EAT_FRAMES)


# Stand (6 s, loop): stood out of the guns' reach, hunched, swaying from foot to foot, his jaw working, the arms hanging.
def stood(sway=0.0, breath=0.0, jaw=10.0):
    p = with_(HANG, hunch(0.85 + 0.05 * breath, look=4, jaw=jaw), {"root@loc": (0.02 * sway, 0, 1.18 - HIP - 0.01 * breath),
                                                                    "pelvis": (-6, 3 * sway, 0)})
    p = ankle_at(p, "r", (0.16, 0.08, ANKLE), (0.3, 1.0, 0.9), 0)
    p = ankle_at(p, "l", (-0.16, -0.12, ANKLE), (-0.3, 1.0, 0.9), 0)
    for s, sx in (("r", 1), ("l", -1)):
        p[f"upperarm_{s}"] = (6 + 3 * sway * sx, -sx * 8, 0)
        p[f"lowerarm_{s}"] = (18, 0, 0)
    return p


stand = Clip("stand")
stand.key(0, stood(0, 0, 10), "BEZIER").key(45, stood(1, 1, 18), "BEZIER").key(90, stood(0, 0, 8), "BEZIER")
stand.key(135, stood(-1, 1, 16), "BEZIER").close(180)

# Roar (4 s): he draws himself up, then throws his head forward and howls at the walls, the jaw hanging as far as it goes,
# the long arms flung back; and sinks back.
roar = Clip("roar", loop=False)
roar.key(0, stood(0, 0, 10), "BEZIER")
UPRIGHT = with_(stood(0, 1, 20), hunch(0.2, look=-10, jaw=20))
UPRIGHT.update({"upperarm_r": (-10, -20, 0), "upperarm_l": (-10, 20, 0)})
roar.key(24, UPRIGHT, "BEZIER")
HOWL = with_(stood(0, 0, 70), hunch(0.9, look=24, jaw=70))
HOWL.update({"upperarm_r": (-40, -40, 0), "upperarm_l": (-40, 40, 0), "lowerarm_r": (10, 0, 0), "lowerarm_l": (10, 0, 0),
             "rope_01": (50, 0, 0)})
HOWL.update(curl(-10, 0))
roar.key(36, HOWL, "CONSTANT")
roar.key(90, with_(HOWL, {"jaw": (-64, 0, 0)}), "BEZIER")
roar.key(120, stood(0, 0, 12), "BEZIER")

CLIPS = [lie, rise, walk, run, grab, lift, eat, stand, roar]

if os.environ.get("DT_POSES"):
    want = [("foot_r", "head"), ("foot_l", "head"), ("toe_r", "tail"), ("hand_r", "tail"), ("mouth", "head"), ("calf_r", "head"),
            ("thigh_r", "head")]
    for clip in CLIPS:
        for frame, pose, _ in clip.keys:
            P = rig.pose_points(sk, pose, want)
            print(f"{clip.name:6s} {frame:3d} " + " ".join(f"{n[0][:6]}=({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})" for n, p in zip(want, P)))
    sys.exit(0)

# One skin (rig.fuse, settled on the flesh's field, then QuadriFlow), the lips opened again: a large monster's budget
# (GDD §27: 8-16k).
kit.fuse("body", ["body"], voxel=0.004, faces=SKIN_FACES, cut=mouth_cut, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] waker", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "waker.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.35)
