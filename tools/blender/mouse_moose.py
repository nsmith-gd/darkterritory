"""THE MOOSE MOUSE (renamed from the Mouse Moose, 23 Oct; note 1015) (GDD §21; the director, 15 Oct 2026: "a mutant rat with little tiny moose antlers"; ARCHITECTURE §8
notes 790, 796; docs/design/creatures/mouse-moose.md §1): the yard's thief that drags a crane's castings off, very slowly.

A rat the size of a dog: about 0.45 m at the shoulder and 0.95 m from its nose to its rump, its tail as long again. Hunched,
the loins arched over heavy haunches, the belly slung low, a hump of muscle over the shoulders (a moose's), the head carried
low on a short thick neck: a broad skull tapering to a long blunt snout, the nose pink and wet, long whiskers, the incisors
long and orange; small black eyes that throw back the lamp red; round thin ears, the left torn. And what the brief names it
by: a little pair of moose antlers, palmate, each a short beam out of the skull to a flat palm with its tines along the
edge, a brow tine forward; the left grown bigger than the right, the right stunted and turned in; the velvet torn and
hanging off them in shreds. Under the throat a moose's bell, a little flap of skin that swings. The tail a bare pink-grey
rope ringed with scales. Mangy (the bake's): a dark grey-brown pelt gone in patches to grey skin and scabs.

How it's made (the Look Review: organic, one skin, not primitives; the Pickers' and the Mourners' way, notes 362, 574):
the haunches, the arched loins, the slung belly, the ribs, the hump, the forelegs to the wrists, the hind legs to the
hocks, the neck and the head to the upper lip are one skin of smooth volumes (tools/blender/flesh.py), settled onto their
field before QuadriFlow; the jaw a second skin (it opens). Finer than their quads: the tail, the hands' fingers and the
hind feet's toes, the ears, the eyes, the whiskers, the incisors, the antlers and their velvet, the bell. Its colour is
baked into one atlas by tools/models/recipes/mouse_moose.py.

Its own rig (SK_MouseMoose, 32 bones): root, pelvis, spine_01, spine_02, neck, head, jaw, the bell, two ears; upper
arms, forearms, hands and fingers; thighs, calves, feet and toes; six bones down the tail. Its rest stands on all fours
facing +Y (the engine's -Z), its feet at the origin's height; the origin between its fore and hind feet. Its teeth go into a
casting 0.7 m ahead of the origin (enemies.json mouseMoose grip), at a casting's lower edge. Clips, one for each of its
modes (MouseMooseMode) and the turns between them (GDD §31: still, then too fast): idle (hunched, sniffing, up on its
haunches to look), creep (the low slinking walk, the head weaving, the sim's 1.2 m/s), bite (the lunge, the teeth in,
the worrying: the sim's 1.5 s), drag (braced back on its haunches, heaving it backwards in jerks), flee (the bolt, the sim's
5 m/s), dive (down its hole head first), hide (down its hole, only the antlers' tips, the eyes and the nose over the
earth, looking round), emerge (up out of it), hit.

    tools/models/build.sh mouse_moose   # this, with its colour baked -> content/art/models/mouse_moose.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/mouse_moose.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from mathutils import Quaternion  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc  # noqa: E402

rig.reset()

TAIL = 6
# The tail's line at rest: out of the rump, down to the ground and lying along it, a lazy curve off to the right.
TAIL_PTS = [(0.0, -0.33, 0.31), (0.008, -0.46, 0.245), (0.03, -0.59, 0.17), (0.06, -0.72, 0.105), (0.095, -0.85, 0.065),
            (0.125, -0.98, 0.045), (0.145, -1.11, 0.035)]
TAIL_R = [0.036, 0.03, 0.023, 0.017, 0.012, 0.008, 0.004]
# The casting it grips (facilities.json castingSize 1.4 x 0.9 x 1.0; enemies.json mouseMoose grip 0.7): its near face 0.7 m
# ahead of the origin; the teeth go in at its lower edge.
GRIP_Y, GRIP_Z = 0.7, 0.22


def mm_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, -0.24, 0.34), (0, -0.08, 0.43)),
        # (The loins arched up over the haunches; the hump over the shoulders, a moose's.)
        Bone("spine_01", "pelvis", (0, -0.08, 0.43), (0, 0.08, 0.455)),
        Bone("spine_02", "spine_01", (0, 0.08, 0.455), (0, 0.22, 0.43)),
        Bone("neck", "spine_02", (0, 0.22, 0.43), (0, 0.33, 0.375)),
        Bone("head", "neck", (0, 0.33, 0.375), (0, 0.6, 0.27)),
        Bone("jaw", "head", (0, 0.375, 0.315), (0, 0.555, 0.255)),
        # The bell: a little flap of skin hung from the throat.
        Bone("bell", "neck", (0, 0.35, 0.29), (0, 0.365, 0.2)),
    ]
    for s, sx in (("r", 1), ("l", -1)):
        b += [
            Bone(f"ear_{s}", "head", (sx * 0.045, 0.365, 0.41), (sx * 0.095, 0.35, 0.47)),
            # The forelegs: short, the elbow tucked back, the wrist forward; a rat's hand, four fingers.
            Bone(f"upperarm_{s}", "spine_02", (sx * 0.075, 0.22, 0.35), (sx * 0.085, 0.185, 0.19)),
            Bone(f"lowerarm_{s}", f"upperarm_{s}", (sx * 0.085, 0.185, 0.19), (sx * 0.085, 0.245, 0.045)),
            Bone(f"hand_{s}", f"lowerarm_{s}", (sx * 0.085, 0.245, 0.045), (sx * 0.09, 0.3, 0.016)),
            Bone(f"fingers_{s}", f"hand_{s}", (sx * 0.09, 0.3, 0.016), (sx * 0.092, 0.34, 0.008)),
            # The hind legs: the thigh in the haunch, the knee forward, the hock back up off the ground, the long foot flat.
            Bone(f"thigh_{s}", "pelvis", (sx * 0.085, -0.22, 0.31), (sx * 0.1, -0.1, 0.17)),
            Bone(f"calf_{s}", f"thigh_{s}", (sx * 0.1, -0.1, 0.17), (sx * 0.1, -0.25, 0.065)),
            Bone(f"foot_{s}", f"calf_{s}", (sx * 0.1, -0.25, 0.065), (sx * 0.1, -0.12, 0.016)),
            Bone(f"toe_{s}", f"foot_{s}", (sx * 0.1, -0.12, 0.016), (sx * 0.1, -0.065, 0.01)),
        ]
    parent = "pelvis"
    for i in range(TAIL):
        b.append(Bone(f"tail_{i + 1:02d}", parent, TAIL_PTS[i], TAIL_PTS[i + 1]))
        parent = f"tail_{i + 1:02d}"
    return Skeleton("SK_MouseMoose", b)


sk = mm_skeleton()
sk.build()
kit = rig.Kit(sk, "mouse_moose")
SKIN_FACES, JAW_FACES = 2100, 160


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/mouse_moose.py),
# the names say what to paint: the mangy pelt, the bare pink skin (hands, feet, nose), the ears, the tail, the mouth, the
# incisors, the claws, the antlers' bone and their velvet. The eyes keep their own lit glass (they throw the lamp back red).
FUR = Mat("skin.mm_fur", hexc("#3a332c"), shine=0.12)
PINK = Mat("skin.mm_pink", hexc("#9a7470"), shine=0.3)
EAR = Mat("skin.mm_ear", hexc("#8a6a66"), shine=0.18)
TAILM = Mat("skin.mm_tail", hexc("#8a7470"), shine=0.3)
MOUTH = Mat("skin.mm_mouth", hexc("#3a1a18"), shine=0.5)
TOOTH = Mat("skin.mm_tooth", hexc("#c88a3a"), shine=0.55)
CLAW = Mat("skin.mm_claw", hexc("#1e1a16"), shine=0.4)
ANTLER = Mat("skin.mm_antler", hexc("#6a5a46"), shine=0.2)
VELVET = Mat("skin.mm_velvet", hexc("#5a3a32"), shine=0.25)
WHISKER = Mat("skin.mm_whisker", hexc("#2a2622"), shine=0.3)
EYE = Mat("glass_dirty.mm_eye", hexc("#3a0806"), shine=0.95, emissive=0.3, tint=(0.9, 0.18, 0.12))

# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the hind feet to the upper lip (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "mouse moose flesh")
SPINE_PTS = [H("pelvis") + Vector((0, -0.08, -0.03)), H("spine_01"), H("spine_02"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "neck", "head"], soft=0.05)
HC = Vector((0, 0.47, 0.3))  # the mouth's middle, between the jaws
JC = Vector((0, 0.46, 0.288))


def trunk_w(p):
    """Along the spine; the shoulders' bulk with the upper arms, the skin over each hip with its thigh, the rump's root
    with the tail's first bone."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if 0.12 < p.y < 0.3 and ax > 0.04 and p.z < 0.4:
        k = smooth01(0.04, 0.09, ax) * bell((p.y - 0.21) / 0.07) * smooth01(0.4, 0.3, p.z)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.5 * k)
    d = (p - H(f"thigh_{s}")).length
    if d < 0.13:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.5 * smooth01(0.13, 0.04, d) * smooth01(0.02, 0.07, ax))
    if p.y < -0.3:
        w = mix(w, {"tail_01": 1.0}, 0.6 * smooth01(-0.3, -0.36, p.y))
    return w


def head_w(p):
    p = Vector(p)
    w = trunk_w(p)
    if p.y > 0.4 and p.z < 0.305:
        # (Below the lip's line the cheeks go a little with the jaw: the mouth's corners stretch as it opens.)
        w = mix(w, {"jaw": 1.0}, 0.5 * smooth01(0.31, 0.29, p.z) * smooth01(0.4, 0.45, p.y))
    return w


# The trunk: the big round haunches and rump, the loins arched up over them, the belly slung low, the ribs' cage, the hump.
flesh.blob((0, -0.2, 0.33), (0.12, 0.15, 0.125), 0, FUR, trunk_w)                                    # the rump
flesh.blob((0, -0.04, 0.38), (0.115, 0.16, 0.1), 0.07, FUR, trunk_w, rot=R(rx=-5))                  # the loins
flesh.blob((0, 0.13, 0.325), (0.104, 0.13, 0.115), 0.07, FUR, trunk_w, rot=R(rx=6))                 # the ribs' cage
flesh.blob((0, 0.0, 0.245), (0.092, 0.17, 0.075), 0.07, FUR, trunk_w)                               # the belly, slung
flesh.blob((0, 0.11, 0.41), (0.08, 0.11, 0.05), 0.06, FUR, trunk_w, rot=R(rx=-8))                   # the hump, the arch's run
for sx in (1, -1):
    flesh.blob((sx * 0.074, -0.19, 0.255), (0.068, 0.125, 0.125), 0.05, FUR, trunk_w, rot=R(rx=-22))  # the haunch, the thigh in it
    flesh.blob((sx * 0.072, 0.2, 0.3), (0.054, 0.075, 0.085), 0.04, FUR, trunk_w)                   # the shoulder, the upper arm in it
# The rump's root of the tail, so the tail grows out of it.
flesh.limb([Vector(TAIL_PTS[0]) + Vector((0, 0.06, 0.02)), Vector(TAIL_PTS[0]), Vector(TAIL_PTS[1])], [0.05, 0.04, 0.032], 0.04, FUR,
           lambda p: mix(trunk_w(p), {"tail_01": 1.0}, smooth01(-0.3, -0.42, p[1])))
# Growths: lumps under the pelt where something's gone wrong (a mutant's), and a sore on the flank.
for c, r in (((0.1, -0.06, 0.36), (0.035, 0.04, 0.03)), ((-0.085, 0.12, 0.38), (0.028, 0.03, 0.024)),
             ((0.06, 0.28, 0.39), (0.022, 0.025, 0.02))):
    flesh.blob(c, r, 0.02, FUR, trunk_w)

# The forelegs: short and thick at the shoulder, thin at the wrist; the hands pink.
HANDS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([sh, el, wr, tip], [f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.025)
    flesh.limb([sh + Vector((0, 0, 0.02)), sh.lerp(el, 0.5), el], [0.056, 0.046, 0.032], 0.04, FUR,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(0.28, 0.36, p[2]) * 0.6))
    flesh.blob(el + Vector((0, -0.01, 0)), (0.026, 0.026, 0.026), 0.012, FUR, arm, around=12, rings=8)      # the elbow
    flesh.limb([el, el.lerp(wr, 0.5), wr], [0.03, 0.023, 0.016], 0.012, FUR, arm)
    palm = wr.lerp(tip, 0.5)
    HANDS[s] = palm
    flesh.blob(palm, (0.024, 0.034, 0.011), 0.012, PINK, arm, rot=R(rx=-25), around=12, rings=6)

# The hind legs: the thigh in the haunch, the knee, the shin back to the hock, the long pink foot flat on the ground.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.03)
    flesh.limb([hip + Vector((0, 0, 0.02)), hip.lerp(kn, 0.5), kn], [0.075, 0.065, 0.04], 0.04, FUR,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(0.24, 0.32, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.008, 0)), (0.03, 0.03, 0.03), 0.012, FUR, leg, around=12, rings=8)        # the knee
    flesh.limb([kn, kn.lerp(an, 0.5), an], [0.034, 0.025, 0.017], 0.012, FUR, leg)
    flesh.blob(an + Vector((0, -0.008, 0)), (0.016, 0.02, 0.018), 0.008, FUR, leg)                       # the hock
    flesh.blob(an.lerp(ball, 0.55) + Vector((0, 0, -0.022)), (0.022, 0.072, 0.012), 0.012, PINK, leg,
               rot=R(rx=-math.degrees(math.atan2(an.z - ball.z, ball.y - an.y)) * 0.55))                 # the foot

# The neck, short and thick, and the head: a broad skull behind the ears tapering to a long blunt snout; the cheeks full
# (a rat's jowls); the nose a pink pad; the eyes small in their orbits; the upper lip split under the nose.
flesh.limb([H("neck") + Vector((0, -0.03, 0.0)), H("neck").lerp(T("neck"), 0.5), T("neck") + Vector((0, 0.015, -0.005))],
           [0.085, 0.072, 0.064], 0.05, FUR, trunk_w)
flesh.blob((0, 0.375, 0.37), (0.068, 0.075, 0.06), 0.04, FUR, head_w, around=18, rings=10)          # the skull
flesh.blob((0, 0.47, 0.32), (0.049, 0.09, 0.043), 0.035, FUR, head_w, rot=R(rx=-16), around=18, rings=8)  # the snout
flesh.blob((0, 0.557, 0.289), (0.028, 0.024, 0.024), 0.015, PINK, head_w)                            # the nose
for sx in (1, -1):
    flesh.blob((sx * 0.042, 0.43, 0.31), (0.03, 0.055, 0.03), 0.025, FUR, head_w)                     # the cheek
    flesh.blob((sx * 0.04, 0.4, 0.375), (0.022, 0.03, 0.018), 0.015, FUR, head_w)                     # the brow
    flesh.carve((sx * 0.043, 0.418, 0.358), (0.014, 0.016, 0.012), 0.008)                           # the orbit
LIP = [flesh.surface(HC + Vector((0, 0, 0.004)), (math.sin(math.radians(a)), math.cos(math.radians(a)), 0), 0.2)
       for a in range(-96, 97, 16)]
LIP = [p - (p - HC).normalized() * 0.003 for p in LIP]
flesh.limb(LIP, [0.005] + [0.006] * (len(LIP) - 2) + [0.005], 0.006, FUR, head_w, sides=8, ref=(0, 0, 1))
flesh.carve((0, 0.47, 0.297), (0.028, 0.07, 0.012), 0.008)                                           # the mouth's roof

# ----------------------------------------------------------------------------------------------------------------
# The jaw: a narrow scoop under the snout, hinged back under the cheeks, its lip rolled, the floor of the mouth hollowed.
jaw = kit.part("jaw")
jflesh = Flesh(jaw, "mouse moose jaw")


def jaw_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    return MOUTH if n.z > 0.5 and c.z > 0.285 and abs(c.x) < 0.025 else FUR


jflesh.blob((0, 0.45, 0.283), (0.03, 0.08, 0.016), 0, FUR, "jaw", rot=R(rx=-14), around=18, rings=8, fmat=jaw_mat)
jflesh.blob((0, 0.515, 0.268), (0.018, 0.02, 0.013), 0.012, FUR, "jaw", fmat=jaw_mat)                 # the chin
LOWLIP = [jflesh.surface(JC, (math.sin(math.radians(a)), math.cos(math.radians(a)), 0), 0.2) for a in range(-90, 91, 18)]
LOWLIP = [p - (p - JC).normalized() * 0.004 + Vector((0, 0, 0.004)) for p in LOWLIP]
jflesh.limb(LOWLIP, [0.004] + [0.005] * (len(LOWLIP) - 2) + [0.004], 0.005, FUR, "jaw", sides=8, ref=(0, 0, 1))
jflesh.carve((0, 0.46, 0.293), (0.022, 0.06, 0.009), 0.006)                                           # the mouth's floor

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin.
fine = kit.part("fine")

# The tail: a bare rope from inside the rump, tapering to a point, ringed (the bake's scales); on its six bones.
tail_bones = chain([Vector(p) for p in TAIL_PTS], [f"tail_{i + 1:02d}" for i in range(TAIL)], soft=0.06)
TP = []
for i in range(len(TAIL_PTS) - 1):
    a, b = Vector(TAIL_PTS[i]), Vector(TAIL_PTS[i + 1])
    TP += [a, a.lerp(b, 0.5)]
TP.append(Vector(TAIL_PTS[-1]))
TR = []
for i in range(len(TAIL_R) - 1):
    TR += [TAIL_R[i], (TAIL_R[i] + TAIL_R[i + 1]) / 2]
TR.append(TAIL_R[-1])
TP[0] = TP[0] + Vector((0, 0.05, 0.02))  # (its root well inside the rump)
fine.tube(TP, TR, 8, TAILM, tail_bones, ref=(0, 0, 1), cap1="point")

# The incisors: two long ones down from under the nose, two up from the chin, chisel-ended, orange.
TEETH_AT = []
for sx in (1, -1):
    a = Vector((sx * 0.0065, 0.548, 0.29))
    b = a + Vector((0, 0.008, -0.028))
    fine.tube([a, a.lerp(b, 0.5), b], [(0.0055, 0.003), (0.005, 0.0028), (0.0045, 0.0018)], 6, TOOTH, "head", ref=(0, 1, 0))
    TEETH_AT.append(a)
    c = Vector((sx * 0.0055, 0.52, 0.268))
    d = c + Vector((0, 0.022, 0.02))
    fine.tube([c, c.lerp(d, 0.5), d], [(0.0048, 0.0028), (0.0045, 0.0026), (0.004, 0.0016)], 6, TOOTH, "jaw", ref=(0, 1, 0))
    TEETH_AT.append(c)

# The eyes: black beads in the orbits, wet; they keep their own lit glass and throw the lamp back red.
EYES = {}
for s, sx in (("r", 1), ("l", -1)):
    c = Vector((sx * 0.049, 0.42, 0.36))
    EYES[s] = c
    fine.blob(c, (0.014, 0.015, 0.013), 10, 6, EYE, "head")

# The whiskers: long and stiff off the sides of the snout, fanned, drooping a little (3 sides each: they're hair).
for s, sx in (("r", 1), ("l", -1)):
    for k in range(6):
        root = Vector((sx * 0.026, 0.51 - 0.006 * (k % 3), 0.302 - 0.006 * (k // 3)))
        ang = math.radians(-35 + 16 * k + 6 * h01(611, k, sx))
        L = 0.11 + 0.06 * h01(612, k, sx)
        d = Vector((sx * math.cos(ang), 0.35 + 0.4 * math.sin(ang), -0.12 + 0.1 * h01(613, k, sx))).normalized()
        mid = root + d * L * 0.5 + Vector((0, 0, -0.006))
        end = root + d * L + Vector((0, 0, -0.018))
        fine.tube([root, mid, end], [0.0012, 0.0008, 0.0003], 3, WHISKER, "head", ref=(0, 0, 1))

# The ears: round, thin and nearly bare, cupped forward, on their own bones; the left one torn, a notch out of its rim.
EAR_OPEN = {}
for s, sx in (("r", 1), ("l", -1)):
    e0, e1 = H(f"ear_{s}"), T(f"ear_{s}")
    c = e0.lerp(e1, 0.65)
    rot = R(rx=-10, rz=-sx * 30)
    M = rot.to_3x3()
    Mi = M.inverted()
    EAR_OPEN[s] = (M @ Vector((0, 1, 0))).normalized()

    def cup(i, j, a, th, p, c=c, M=M, Mi=Mi, torn=(s == "l")):
        q = Mi @ (Vector(p) - c)
        q.y += 0.7 * (q.x * q.x) / 0.04 + 0.2 * max(0.0, -q.z) ** 2 / 0.04
        if torn:
            # The notch: a bite out of the back of the rim.
            k = bell((math.atan2(q.z, -q.x * sx) - 0.9) / 0.25) * smooth01(0.6, 0.95, math.hypot(q.x / 0.04, q.z / 0.045))
            q.x *= 1 - 0.45 * k
            q.z *= 1 - 0.45 * k
        return c + M @ q
    fine.blob(c, (0.04, 0.005, 0.045), 14, 9, EAR, f"ear_{s}", rot=rot, shape=cup)
    fine.tube([e0 + Vector((-sx * 0.01, 0.004, -0.012)), e0.lerp(e1, 0.35)], [0.016, 0.011], 8, EAR, {"head": 0.5, f"ear_{s}": 0.5},
              ref=(0, 0, 1), cap0="point")

# The hands' four fingers, short and pink with dark claws; the hind feet's five long toes.
CLAWS = []
for s, sx in (("r", 1), ("l", -1)):
    fbones = chain([H(f"hand_{s}"), H(f"fingers_{s}"), T(f"fingers_{s}")], [f"hand_{s}", f"fingers_{s}"], soft=0.01)
    base = H(f"fingers_{s}")
    for k in range(4):
        dx = (k - 1.5) * 0.011
        a0 = base + Vector((sx * dx, -0.012, 0.004))
        end = a0 + Vector((sx * dx * 0.6, 0.034 - 0.006 * abs(k - 1.5), -0.006))
        CLAWS.append(end)
        fine.tube([a0, a0.lerp(end, 0.55) + Vector((0, 0, 0.003)), end, end + Vector((0, 0.007, -0.005))], [0.0055, 0.0048, 0.0035, 0.0012], 5,
                  PINK, fbones, ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, n, end=end: CLAW if (sum(pts, Vector()) / len(pts) - end).length < 0.007 else PINK)
    ball = H(f"toe_{s}")
    for k in range(5):
        dx = (k - 2) * 0.01
        a0 = ball + Vector((sx * dx, -0.012, 0.003))
        end = a0 + Vector((sx * dx * 0.7, 0.05 - 0.006 * abs(k - 2), -0.005))
        CLAWS.append(end)
        fine.tube([a0, a0.lerp(end, 0.55) + Vector((0, 0, 0.003)), end, end + Vector((0, 0.008, -0.005))], [0.006, 0.005, 0.0036, 0.0012], 5,
                  PINK, f"toe_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, n, end=end: CLAW if (sum(pts, Vector()) / len(pts) - end).length < 0.008 else PINK)

# The bell: a little flap of loose skin hung from the throat, a moose's, swinging on its bone.
fine.tube([H("bell") + Vector((0, -0.01, 0.025)), H("bell").lerp(T("bell"), 0.45), T("bell"), T("bell") + Vector((0, 0.004, -0.012))],
          [(0.016, 0.024), (0.012, 0.02), (0.009, 0.014), (0.004, 0.006)], 8, FUR, {"bell": 0.85, "neck": 0.15}, ref=(0, 1, 0), cap1="point")

# The antlers: a moose's, big for a rat's head (the director, Look Review, 23 Oct: "their moose antlers need to be bigger";
# note 1015: about twice the span they were, the palms reading from across a yard). Each a short beam out of the skull, out and up and a little back, to a flat palm turned up,
# its tines along the palm's outer edge, and a brow tine forward off the beam. The left grown bigger; the right stunted,
# its palm smaller and curled in. The velvet torn and hanging off both in shreds.
ANTLER_TIPS, VELVET_AT = [], []


def antler(sx, size, curl, n_tines, seed):
    root = Vector((sx * 0.035, 0.405, 0.415))
    out = Vector((sx * 0.7, 0.05, 0.7)).normalized()
    # (The beam short, as a moose's is, so the palm spreads close off the skull: grown with the rest, it was a stalk.)
    knee = root + out * 0.05 * size ** 0.6
    beam_end = knee + Vector((sx * 0.6, -0.1, 0.35)).normalized() * 0.045 * size ** 0.6
    # (The beam and the burr thicken slower than the antler grows: a rat's skull carries them.)
    th = size ** 0.7
    fine.tube([root - out * 0.012, knee, beam_end], [0.014 * th, 0.011 * th, 0.01 * th], 7, ANTLER, "head", ref=(0, 1, 0), cap0="point")
    # The burr at its root, rough.
    fine.blob(root + out * 0.006, (0.017 * th, 0.017 * th, 0.008 * th), 9, 4, ANTLER, "head", rot=Vector((0, 0, 1)).rotation_difference(out).to_matrix())
    # The palm: a flattened oval, broad out and back from the beam's end, tipped up and turned out; `curl` bends it in.
    pc = beam_end + Vector((sx * 0.035, -0.022, 0.012)) * size
    rot = R(rx=12, ry=-sx * (52 + 25 * curl), rz=sx * 18)
    M = rot.to_3x3()
    Mi = M.inverted()

    def palm(i, j, a, th, p, pc=pc, M=M, Mi=Mi):
        q = Mi @ (Vector(p) - pc)
        # Cupped (its hollow up and in), the edge thinned, the stunted one curled in at its outer end.
        q.z += 0.25 * (q.x * q.x + q.y * q.y) / (0.05 * size) + curl * 0.4 * max(0.0, q.x * sx) ** 2 / (0.03 * size)
        return pc + M @ q
    fine.blob(pc, (0.046 * size, 0.027 * size, 0.0055 * size), 14, 6, ANTLER, "head", rot=rot, shape=palm)
    # The tines: short points off the palm's outer and back edge, fanned.
    for k in range(n_tines):
        t = k / max(1, n_tines - 1)
        ang = math.radians(-60 + 120 * t)
        edge = pc + M @ Vector((math.cos(ang) * 0.042 * size, -math.sin(ang) * 0.024 * size, 0))
        d = (M @ Vector((math.cos(ang), -math.sin(ang), 0.55 + 0.3 * h01(seed, k)))).normalized()
        L = (0.034 + 0.02 * h01(seed + 1, k)) * size * (1 - 0.35 * curl)
        tip = edge + d * L
        ANTLER_TIPS.append(tip)
        fine.tube([edge - d * 0.008 * size, edge.lerp(tip, 0.45), tip], [0.0075 * th, 0.005 * th, 0.0009], 5, ANTLER, "head", ref=(0, 0, 1),
                  cap1="point")
    # The brow tine: forward and up off the beam, over the eye.
    b0 = root.lerp(knee, 0.6)
    b1 = b0 + Vector((sx * 0.006, 0.03, 0.012)) * size
    fine.tube([b0, b0.lerp(b1, 0.5), b1], [0.006 * th, 0.0045 * th, 0.0008], 5, ANTLER, "head", ref=(0, 0, 1), cap1="point")
    ANTLER_TIPS.append(b1)
    # The velvet: shreds of it torn and hanging off the beam and the palm's underside, swinging with the head (thin flat strips).
    for k in range(3):
        top = knee.lerp(pc, 0.3 + 0.3 * k) + Vector((0, 0, -0.004))
        L = (0.025 + 0.025 * h01(seed + 5, k)) * size
        lean = Vector((sx * 0.004 * (k - 1), 0.004 * (h01(seed + 6, k) - 0.5), 0))
        pts_ = [top, top + Vector((0, 0, -L * 0.5)) + lean * 0.5, top + Vector((0, 0, -L)) + lean]
        VELVET_AT.append(pts_[-1])
        fine.tube(pts_, [(0.006 * size, 0.0012), (0.005 * size, 0.001), (0.002 * size, 0.0006)], 4, VELVET, "head", ref=(sx, 0, 0), cap1="point")


antler(-1, 3.4, 0.0, 5, 621)    # the left: grown big
antler(1, 2.6, 0.7, 3, 631)     # the right: stunted, curled in


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest stands on all fours; angles in the armature's axes: +X swings a hanging limb forward and tips a
# forward-lying bone (the spine, the head) up; the jaw opens with -X; a tail bone lifts with -X (it lies back along -Y).
# The feet are planted by reach (rig.reach) where the gait puts them; the nose is brought to the casting by a search over
# the neck's and the head's pitch (nose_to).

def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


def tail_pose(lift=0.0, swing=0.0, wave=0.0, phase=0.0):
    """The tail: lifted off the ground by `lift` (degrees at the root, less down it), swung to one side by `swing`, a wave
    running down it by `wave` (degrees) at `phase` (radians)."""
    out = {}
    for i in range(TAIL):
        k = i / (TAIL - 1)
        out[f"tail_{i + 1:02d}"] = (-lift * (1 - 0.7 * k), 0, swing * (1 - 0.4 * k) + wave * math.sin(phase - 1.1 * i))
    return out


def pts(pose, names):
    return rig.pose_points(sk, pose, names)


STAND = with_({"ear_r": (0, 0, 0), "ear_l": (0, 0, 0), "jaw": (-2, 0, 0)}, tail_pose())


def look(yaw=0.0, pitch=0.0, sniff=0.0, ears=0.0):
    """The head turned `yaw`, tipped `pitch` (+ up), the nose working (`sniff`: a twitch of the head), the ears forward (+)
    or back (-)."""
    return {"neck": (pitch * 0.4, 0, yaw * 0.4), "head": (pitch * 0.6 + sniff, 0, yaw * 0.6),
            "ear_r": (-ears * 10, 0, -ears * 12), "ear_l": (-ears * 10, 0, ears * 12)}


def plant_hind(p, s, dy=0.0, lift=0.0, toe=0.0):
    """A hind leg reached so its hock is `dy` along and `lift` up from where it stands at rest, the foot flat (or tipped
    `toe` degrees: up on its toes as it pushes off)."""
    a = H(f"foot_{s}")
    target = Vector((a.x, a.y + dy, a.z + lift))
    p = rig.reach(sk, p, f"thigh_{s}", f"calf_{s}", target, elbow_axis=0, bend=-1, pole=target + Vector((0, 0.35, 0.3)))
    p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", toe, 0, 0)
    p[f"toe_{s}"] = (-toe * 0.6, 0, 0)
    return p


def plant_fore(p, s, dy=0.0, lift=0.0, curl=0.0):
    """A foreleg reached so its wrist is `dy` along and `lift` up from where it stands at rest, the hand flat (or curled
    `curl` degrees, lifted)."""
    w = H(f"hand_{s}")
    target = Vector((w.x, w.y + dy, w.z + lift))
    p = rig.reach(sk, p, f"upperarm_{s}", f"lowerarm_{s}", target, elbow_axis=0, bend=1, pole=target + Vector((0, -0.3, 0.25)))
    p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}", -curl, 0, 0)
    p[f"fingers_{s}"] = (-curl * 0.6, 0, 0)
    return p


def nose_to(p, target, bones=("neck", "head")):
    """The neck's and the head's pitch searched for, so the nose's tip (the head's tail) is at `target`."""
    target = Vector(target)
    p = dict(p)
    cur = {b: list(p.get(b, (0, 0, 0))) for b in bones}

    def cost():
        q = dict(p)
        q.update({b: tuple(v) for b, v in cur.items()})
        return (pts(q, [("head", "tail")])[0] - target).length
    best = cost()
    for step in (16, 8, 4, 2, 1, 0.5):
        improved = True
        while improved:
            improved = False
            for b in bones:
                for sgn in (1, -1):
                    cur[b][0] += sgn * step
                    c = cost()
                    if c < best - 1e-6:
                        best, improved = c, True
                    else:
                        cur[b][0] -= sgn * step
    p.update({b: tuple(round(v, 2) for v in cur[b]) for b in bones})
    return p


def stride(u, duty, S, lift):
    """Where a foot is at `u` (0..1) through its cycle: on the ground for `duty` of it, going back from S/2 to -S/2;
    then up and forward again. -> (dy, lift)."""
    if u < duty:
        return S / 2 - S * u / duty, 0.0
    f = (u - duty) / (1 - duty)
    f2 = f * f * (3 - 2 * f)
    return -S / 2 + S * f2, lift * math.sin(math.pi * f)


# Idle (3 s, loop): hunched, the nose going; the head round to a sound, then down sniffing the ground, then up on its
# haunches to look over (a rat's), the forepaws held to its chest, still; back down. The tail curling slowly behind it.
def standing(extra=None, sit=0.0):
    """Stood (or up on its haunches by `sit` 0..1: the body pitched up off the forelegs, the forepaws held to its chest),
    the hind feet planted, the forefeet planted unless it's sat up."""
    p = with_(STAND, extra or {})
    if sit:
        p.update({"root@loc": (0, -0.05 * sit, -0.02 * sit), "pelvis": (30 * sit, 0, 0), "spine_01": (8 * sit, 0, 0),
                  "spine_02": (4 * sit, 0, 0)})
        p.update({k: v for k, v in (extra or {}).items() if k in ("neck", "head", "ear_r", "ear_l", "jaw")})
        p["neck"] = (p.get("neck", (0, 0, 0))[0] - 22 * sit, 0, p.get("neck", (0, 0, 0))[2])
        p["head"] = (p.get("head", (0, 0, 0))[0] - 16 * sit, 0, p.get("head", (0, 0, 0))[2])
        # The forepaws to its chest: the wrists reached to under the throat, the elbows down at its sides, the hands
        # drooped from them (a rat's begging).
        n = pts(p, [("neck", "head")])[0]
        for s, sx in (("r", 1), ("l", -1)):
            w = Vector((sx * 0.05, n.y + 0.06, n.z - 0.1))
            p = rig.reach(sk, p, f"upperarm_{s}", f"lowerarm_{s}", w, elbow_axis=0, bend=1,
                          pole=w + Vector((sx * 0.08, -0.15, -0.3)))
            p[f"hand_{s}"] = rig.hang(sk, p, f"hand_{s}", -55, 0, 0)
            p[f"fingers_{s}"] = (-35, 0, 0)
    for s in ("r", "l"):
        p = plant_hind(p, s)
        if not sit:
            p = plant_fore(p, s)
    return p


idle = Clip("idle")
for f, pose_, ip in (
        (0, standing(look()), "BEZIER"),
        (8, standing(look(0, 4, 3)), "LINEAR"), (10, standing(look(0, 4, -1)), "LINEAR"), (12, standing(look(0, 4, 3)), "LINEAR"),
        (14, standing(look(32, 6, 0, 1)), "CONSTANT"),                                                    # round to a sound
        (24, standing(with_(look(32, 6, 0, 1), tail_pose(0, 8))), "BEZIER"),
        (34, standing(with_(look(-6, -26, 0, -0.3), tail_pose(0, 14))), "BEZIER"),                       # down, sniffing
        (37, standing(with_(look(-8, -26, 4, -0.3), tail_pose(0, 14))), "LINEAR"),
        (40, standing(with_(look(-4, -28, -2, -0.3), tail_pose(0, 12))), "LINEAR"),
        (43, standing(with_(look(-10, -26, 4, -0.3), tail_pose(0, 10))), "LINEAR"),
        (52, standing(with_(look(10, 6, 0, 1), tail_pose(0, 4)), sit=1.0), "BEZIER"),                    # up on its haunches
        (60, standing(with_(look(-14, 8, 3, 1), tail_pose(0, 0)), sit=1.0), "CONSTANT"),
        (70, standing(with_(look(-14, 8, 0, 1), tail_pose(0, -4)), sit=1.0), "BEZIER"),
        (80, standing(with_(look(0, 2, 0, 0.3), tail_pose(0, -6))), "BEZIER")):
    idle.key(f, pose_, ip)
idle.close(90)

# Creep (0.4 s, loop): the low slinking walk (the sim's 1.2 m/s: a stride of 0.48 m a cycle), the belly near the ground,
# the legs in their diagonal pairs (the right fore with the left hind), the head low and weaving, sniffing; the tail
# dragging behind, swaying.
CREEP_FRAMES, CREEP_S = 12, 1.2 * 12 / 30


def creep_pose(t):
    a = 2 * math.pi * t
    p = with_(STAND, look(8 * math.sin(a * 0.5 + 0.4), -16, 2 * math.sin(4 * a), -0.2),
              {"root@loc": (0, 0, -0.035 + 0.006 * abs(math.cos(a))), "pelvis": (-3, 0, 3 * math.sin(a)),
               "spine_01": (-2, 0, -2 * math.sin(a)), "spine_02": (2, 0, 2 * math.sin(a))},
              tail_pose(0, 4 * math.sin(a * 0.5), 10, a))
    for s, off in (("r", 0.0), ("l", 0.5)):
        dy, lift = stride((t + off) % 1, 0.62, CREEP_S, 0.045)
        p = plant_fore(p, s, dy, lift, curl=40 * lift / 0.045)
        dy, lift = stride((t + off + 0.5) % 1, 0.62, CREEP_S, 0.04)
        p = plant_hind(p, s, dy, lift, toe=20 * lift / 0.04)
    return p


creep = Clip("creep")
for i in range(6):
    creep.key(i * 2, creep_pose(i / 6), "LINEAR")
creep.close(CREEP_FRAMES)


# The grip: the nose to the casting's lower edge, the teeth in, braced back between its forelegs planted ahead and its
# hind legs planted behind.
def gripping(heave=0.0, worry=0.0, step=0.0, jaw=-6.0):
    """Its teeth in the casting (the nose at GRIP_Y + 0.02, GRIP_Z): the body low and stretched between its feet, braced
    back; `heave` 0..1 the yank back, `worry` a shake of the head (degrees), `step` the hind legs' shuffle (-1..1)."""
    p = with_(STAND, {
        "root@loc": (0, 0.1 - 0.035 * heave, -0.06 - 0.012 * heave),
        "pelvis": (-6 - 4 * heave, 0, 0), "spine_01": (2, 0, 0), "spine_02": (4 + 4 * heave, 0, 0),
        "jaw": (jaw, 0, 0), "ear_r": (14, 0, 20), "ear_l": (14, 0, -20), "bell": (10 * heave, 0, 0)},
        tail_pose(4, 0, 8 * heave, 1.5))
    for s, sg in (("r", 1), ("l", -1)):
        p = plant_fore(p, s, 0.1)
        p = plant_hind(p, s, -0.04 + 0.05 * step * sg, 0.02 * max(0.0, step * sg))
    p = nose_to(p, (0, GRIP_Y + 0.02, GRIP_Z))
    p["neck"] = (p["neck"][0], 0, worry * 0.3)
    p["head"] = (p["head"][0], worry * 0.4, worry * 0.5)
    return p


# Bite (1.5 s: the sim's biteSeconds): at the casting, a beat sniffing it; the head drawn back, the jaw coming open, held;
# the lunge in two frames and the teeth in (the snap, held); then it worries it, shaking, and settles into its drag.
bite = Clip("bite", loop=False)
bite.key(0, standing(look(0, -10, 0, 0.3)), "BEZIER")
bite.key(6, standing(look(4, -22, 4, 0.3)), "LINEAR")
bite.key(8, standing(look(-4, -22, -2, 0.3)), "LINEAR")
bite.key(12, standing(with_(look(0, 8, 0, 1), {"jaw": (-30, 0, 0), "root@loc": (0, -0.03, -0.02), "pelvis": (-4, 0, 0)})), "LINEAR")
bite.key(16, standing(with_(look(0, 10, 0, 1), {"jaw": (-36, 0, 0), "root@loc": (0, -0.04, -0.03), "pelvis": (-6, 0, 0)})), "LINEAR")
bite.key(18, gripping(0, 0, jaw=-32), "LINEAR")
bite.key(19, gripping(0, 0), "CONSTANT")                                                        # the teeth in
bite.key(24, gripping(0.2, 0), "CONSTANT")
bite.key(26, gripping(0.5, 14), "LINEAR")                                                        # worrying it
bite.key(29, gripping(0.5, -14), "LINEAR")
bite.key(32, gripping(0.4, 12), "LINEAR")
bite.key(35, gripping(0.3, -8), "LINEAR")
bite.key(45, gripping(0, 0, -1), "BEZIER")

# Drag (0.8 s, loop: a heave every 0.8 s): its teeth in the casting, braced back; a heave, the body yanked back against
# its teeth, the hind legs shuffling a step back after it; a breath; again (the sim drags it on at 0.15 m/s, 0.5 m/s once
# it's out of the hook's reach: CreatureArt plays it faster).
drag = Clip("drag")
drag.key(0, gripping(0, 0, -1), "BEZIER")
drag.key(5, gripping(1, 4, -0.6), "LINEAR")                                                      # the heave
drag.key(8, gripping(0.9, -3, 0.4), "BEZIER")
drag.key(14, gripping(0.3, 2, 1), "BEZIER")                                                      # the step back
drag.key(19, gripping(0.1, -2, 0), "BEZIER")
drag.close(24)


# Flee (0.27 s, loop): the bolt for its hole (the sim's 5 m/s: 1.33 m a bound), a bounding gallop: the forelegs reaching
# together, the hind legs driving past them, the back flexing; flat to the ground, the ears pinned back, the tail out
# straight behind.
def bound(k):
    """k 1: stretched out (forelegs reaching, hind legs driving back); -1 gathered (the hind feet up past the fore)."""
    p = with_(STAND, look(0, -4, 0, -1.6), {"jaw": (-14, 0, 0), "bell": (30 * (0.5 + 0.5 * k), 0, 0)}, tail_pose(-14, 0, 6, k * 3))
    p.update({"root@loc": (0, 0, 0.012 + 0.04 * (1 - abs(k))), "pelvis": (-4 - 6 * k, 0, 0), "spine_01": (10 * k, 0, 0),
              "spine_02": (-4 * k, 0, 0)})
    for s, off in (("r", 0.0), ("l", 0.15)):
        kk = k - off
        p[f"upperarm_{s}"] = (50 * kk, 0, 0)
        p[f"lowerarm_{s}"] = (34 * max(0.0, -kk) + 4, 0, 0)
        p[f"hand_{s}"] = (-22 * kk, 0, 0)
        p[f"thigh_{s}"] = (-44 * kk, 0, 0)
        p[f"calf_{s}"] = (-40 * max(0.0, kk) - 4, 0, 0)
        p[f"foot_{s}"] = (24 * kk, 0, 0)
    return p


flee = Clip("flee")
for f, k in ((0, 1), (2, 0.2), (4, -1), (6, -0.2)):
    flee.key(f, bound(k), "LINEAR")
flee.close(8)

# Hide (3 s, loop): down its hole (at the origin: the sim's hole), up it nose first and steep, the body down in the earth and
# only the head over it: the antlers, the ears laid back, the eyes and the nose; the head turning slowly to look round, a
# twitch, a duck down out of sight (the antlers' tips still showing), and up again.
SUNK_PITCH, SUNK_AT = 80, (0, 0.14, -0.26)


def peering(yaw=0.0, up=0.0, sniff=0.0, down=0.0):
    x, y, z = SUNK_AT
    return with_(STAND, {"root": (SUNK_PITCH, 0, 0), "root@loc": (x, y, z - down),
                         "neck": (-42 + up * 0.4, 0, yaw * 0.4), "head": (-18 + up * 0.6 + sniff, 0, yaw * 0.6),
                         "ear_r": (30, 0, 26), "ear_l": (30, 0, -26), "jaw": (-2, 0, 0),
                         "upperarm_r": (-40, 0, 0), "upperarm_l": (-40, 0, 0), "thigh_r": (30, 0, 0), "thigh_l": (30, 0, 0)},
                 tail_pose(-10, 0))


hide = Clip("hide")
for f, pose_, ip in ((0, peering(), "BEZIER"), (14, peering(28, 2), "BEZIER"), (22, peering(28, 2, 3), "LINEAR"),
                     (24, peering(28, 2, -1), "LINEAR"), (36, peering(-24, 0), "BEZIER"),
                     (44, peering(-24, 0), "CONSTANT"), (46, peering(-10, -2, 0, 0.1), "CONSTANT"),
                     (62, peering(-10, -2, 0, 0.1), "BEZIER"), (70, peering(0, 4, 2), "BEZIER")):
    hide.key(f, pose_, ip)
hide.close(90)


def diving(pitch, at, fore=0.0, kick=0.0):
    """Going down the hole (at the origin) nose first: pitched down `pitch` about `at` (the root's place), the forelegs
    reaching down ahead (`fore`, degrees), the hind legs kicking (`kick`)."""
    return with_(STAND, look(0, -20, 0, -1.6), {"root": (-pitch, 0, 0), "root@loc": at, "jaw": (-6, 0, 0),
                                                "upperarm_r": (fore, 0, 0), "upperarm_l": (fore * 0.9, 0, 0),
                                                "lowerarm_r": (-fore * 0.4, 0, 0), "lowerarm_l": (-fore * 0.4, 0, 0),
                                                "thigh_r": (-kick, 0, 0), "thigh_l": (-kick * 0.6, 0, 0),
                                                "calf_r": (kick * 0.8, 0, 0), "calf_l": (kick * 0.5, 0, 0)},
                 tail_pose(-6, 6))


# Dive (0.5 s): at its hole at a bound: nose down into it, the body after it, the hind legs kicking it in, the tail last;
# turned round in the earth (out of sight), it peers back out.
dive = Clip("dive", loop=False)
dive.key(0, with_(bound(1), {"root@loc": (0, -0.62, 0.03)}), "LINEAR")
dive.key(3, diving(35, (0, -0.48, 0.0), 50, 10), "LINEAR")
dive.key(6, diving(72, (0, -0.4, 0.02), 70, 40), "LINEAR")
dive.key(9, diving(84, (0, -0.4, -0.32), 70, -20), "LINEAR")
dive.key(11, diving(88, (0, -0.4, -0.8), 70, 30), "CONSTANT")
dive.key(12, peering(0, 0, 0, 0.4), "BEZIER")
dive.key(15, peering(), "BEZIER")

# Emerge (0.8 s): up out of its hole: the antlers and the eyes first, a look round, frozen; then out in a scramble, the
# forepaws over the rim and the body hauled up after them.
emerge = Clip("emerge", loop=False)
emerge.key(0, peering(0, 0, 0, 0.3), "LINEAR")
emerge.key(6, peering(0, 4), "LINEAR")
emerge.key(8, peering(26, 4, 2), "CONSTANT")
emerge.key(14, peering(26, 4), "LINEAR")
emerge.key(17, with_(STAND, look(10, 6, 0, 0.4), {"root": (40, 0, 0), "root@loc": (0, -0.1, -0.24), "upperarm_r": (60, 0, 0),
                                                  "upperarm_l": (50, 0, 0), "lowerarm_r": (-30, 0, 0), "lowerarm_l": (-20, 0, 0),
                                                  "thigh_r": (40, 0, 0), "thigh_l": (40, 0, 0)}, tail_pose(-20, 0)), "LINEAR")
emerge.key(20, with_(STAND, look(4, -4, 0, 0.3), {"root": (16, 0, 0), "root@loc": (0, -0.12, -0.08), "upperarm_r": (20, 0, 0),
                                                  "upperarm_l": (30, 0, 0), "thigh_r": (30, 0, 0), "thigh_l": (10, 0, 0)},
                     tail_pose(-10, 0)), "LINEAR")
emerge.key(24, standing(look(0, -10, 0, 0.3)), "BEZIER")

# Hit (0.4 s): a blow: flinched in on itself, the head down and away, a squeal (the jaw wide), the ears flat; it bolts.
hit = Clip("hit", loop=False)
hit.key(0, standing(), "CONSTANT")
hit.key(2, standing(with_(look(-26, -14, 0, -1.6), {"jaw": (-36, 0, 0), "root@loc": (0.03, -0.05, -0.04), "pelvis": (-8, 0, -10),
                                                    "spine_02": (-6, 0, 12)}, tail_pose(10, 20))), "CONSTANT")
hit.key(8, standing(with_(look(-10, -6, 0, -1), {"jaw": (-14, 0, 0)}, tail_pose(0, 8))), "LINEAR")
hit.key(12, standing(), "CONSTANT")

CLIPS = [idle, creep, bite, drag, flee, dive, hide, emerge, hit]

if os.environ.get("DT_POSES"):
    # A quick look at the clips' keys without building the mesh: where the feet, the snout and the hind feet are.
    want = [(f"{b}_{s}", "tail") for b in ("fingers", "toe") for s in ("r", "l")] + [("head", "tail"), ("jaw", "tail"), ("pelvis", "head")]
    want += [("lowerarm_r", "head"), ("neck", "head"), ("spine_02", "head"), ("ear_r", "tail"), ("tail_06", "tail")]
    for clip in CLIPS:
        for frame, pose, _ in clip.keys:
            P = pts(pose, want)
            print(f"{clip.name:7s} {frame:3d} " + " ".join(f"{n[0][:7]}=({p.x:+.2f},{p.y:+.2f},{p.z:+.2f})" for n, p in zip(want, P)))
    sys.exit(0)

# One skin from the hind feet to the upper lip (rig.fuse, settled on the flesh's field, then QuadriFlow), the jaw a second:
# a beast's budget (GDD §27: 2-8k), one at a stop.
kit.fuse("body", ["body"], voxel=0.0024, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.fuse("jaw", ["jaw"], voxel=0.0016, faces=JAW_FACES, lose=0.03, settle=jflesh.settle)
kit.build()
rig.bake(sk, CLIPS)
print("[dt] mouse_moose", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "mouse_moose.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
