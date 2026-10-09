"""THE MOURNERS (GDD §21, OUTSIDE; the director's brief of 8 Oct 2026; ARCHITECTURE §8 note 362;
docs/design/creatures/mourners.md §3): "a huddle of grey, stooping things with long hooked fingers, picking their way
toward your dead."

About 1.2 m stood up, but they never stand: the torso leant back from the hips as if always hauling on a rope; heavy,
powerful dragging shoulders and thick upper arms out of all proportion to the thin legs; long hooked fingers, twice a
hand's length, the nails curled; the skin ash-pale and dry, cracked like old clay, dusted grey; a small head tucked low
between the shoulders, its face a mourner's veil of loose skin hanging over the eyes.

How it's made (the Look Review: organic, one skin, not primitives; the Ribbit's and the Gannet's way, notes 340, 361):
the pelvis, the starved belly, the leant-back chest, the hump of the shoulders, the arms to the palms, the thin legs to
the feet, the neck and the small head with the brow and the veil's fold are one skin: smooth volumes that are both rig.fuse's
solids and a signed-distance field's terms (tools/blender/flesh.py), settled onto that field before QuadriFlow lays the
game mesh's quads over it (smooth-shaded). Over it, finer than its quads: the veil's hanging folds (they sway), the long
fingers and the thumbs with their curled nails, the toes, the eyes' wet glints under the veil. Its colour (and the
cracks, the ribs and the creases too fine for the mesh) is baked into one atlas by tools/models/recipes/mourner.py.

Its own rig (SK_Mourner, 37 bones): root, pelvis, spine_01, spine_02, neck, head, veil; clavicles, arms, hands, three
two-boned fingers and a thumb a hand; thighs, calves, feet, toes. Its rest is the stoop it waits in, facing +Y (the
engine's -Z). Clips (GDD §31: still, then too fast): wait (hunched, rocking, the head turning in jerks), creep (a slow
bent walk in), startle (a jump back, arms up), drag (both hooked hands in the body's clothes at the sim's hold, leaning
back, heels digging, hauling in tugs), scatter (a darting run off), hit, death.

    tools/models/build.sh mourner        # this, with its colour baked -> content/art/models/mourner.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/mourner.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from flesh import Flesh, R, bell, chain, h01, mix, smooth01  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror  # noqa: E402

rig.reset()

# Where the body it drags is held, in front of it (enemies.json mourners holdAt, holdHeight: the sim's TakeAlong).
HOLD = Vector((0.0, 0.7, 0.5))
FINGERS = ("a", "b", "c")


def mourner_skeleton():
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
        Bone("pelvis", "root", (0, 0.0, 0.5), (0, -0.03, 0.6)),
        Bone("spine_01", "pelvis", (0, -0.03, 0.6), (0, -0.09, 0.74)),
        # (The chest leant back over the hips: the hauler's lean, always.)
        Bone("spine_02", "spine_01", (0, -0.09, 0.74), (0, -0.115, 0.87)),
        Bone("neck", "spine_02", (0, -0.1, 0.87), (0, 0.0, 0.895)),
        Bone("head", "neck", (0, 0.0, 0.895), (0, 0.15, 0.915)),
        # The veil of loose skin, hanging off the brow over the face (it sways).
        Bone("veil", "head", (0, 0.13, 0.93), (0, 0.165, 0.83)),
    ]
    for side, sx in (("l", -1), ("r", 1)):
        b += [
            Bone(f"clavicle_{side}", "spine_02", (sx * 0.03, -0.1, 0.85), (sx * 0.17, -0.08, 0.885)),
            Bone(f"upperarm_{side}", f"clavicle_{side}", (sx * 0.2, -0.065, 0.865), (sx * 0.265, 0.06, 0.62)),
            Bone(f"lowerarm_{side}", f"upperarm_{side}", (sx * 0.265, 0.06, 0.62), (sx * 0.235, 0.235, 0.42)),
            Bone(f"hand_{side}", f"lowerarm_{side}", (sx * 0.235, 0.235, 0.42), (sx * 0.23, 0.315, 0.355)),
            Bone(f"thumb_{side}", f"hand_{side}", (sx * 0.205, 0.27, 0.4), (sx * 0.17, 0.33, 0.37)),
            Bone(f"thigh_{side}", "pelvis", (sx * 0.085, 0.01, 0.49), (sx * 0.11, 0.13, 0.285)),
            Bone(f"calf_{side}", f"thigh_{side}", (sx * 0.11, 0.13, 0.285), (sx * 0.11, -0.01, 0.075)),
            Bone(f"foot_{side}", f"calf_{side}", (sx * 0.11, -0.01, 0.075), (sx * 0.115, 0.1, 0.022)),
            Bone(f"toe_{side}", f"foot_{side}", (sx * 0.115, 0.1, 0.022), (sx * 0.12, 0.16, 0.012)),
        ]
        for i, f in enumerate(FINGERS):
            # Three long fingers off the knuckles, fanned a little, each two bones a hand's length; hooked at rest.
            dx = (i - 1) * 0.03
            k0 = Vector((sx * (0.23 + dx * 0.4) + sx * dx, 0.315, 0.355))
            k1 = k0 + Vector((sx * dx * 0.5, 0.06, -0.085))
            k2 = k1 + Vector((sx * dx * 0.2, -0.035, -0.09))
            b += [Bone(f"finger_{f}_{side}_01", f"hand_{side}", tuple(k0), tuple(k1)),
                  Bone(f"finger_{f}_{side}_02", f"finger_{f}_{side}_01", tuple(k1), tuple(k2))]
    return Skeleton("SK_Mourner", b)


sk = mourner_skeleton()
sk.build()
kit = rig.Kit(sk, "mourner")
SKIN_FACES = 2900


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/mourner.py),
# the names say what to paint and how to sculpt it: the cracked hide, the veil's thin skin, the nails.
SKIN = Mat("skin.mourner", hexc("#a39f97"), shine=0.1)
VEIL = Mat("skin.mourner_veil", hexc("#9c948c"), shine=0.12)
NAIL = Mat("skin.mourner_nail", hexc("#4a443c"), shine=0.3)
EYE = Mat("glass_dirty.mourner_eye", hexc("#1c1a18"), shine=0.95)

# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the feet to the brow (rig.fuse, settled on `flesh`).
body = kit.part("body")
flesh = Flesh(body, "mourner flesh")
SPINE_PTS = [H("pelvis") + Vector((0, 0, -0.06)), H("spine_01"), H("spine_02"), H("neck"), H("head"), T("head")]
SPINE_W = chain(SPINE_PTS, ["pelvis", "spine_01", "spine_02", "neck", "head"], soft=0.05)


def trunk_w(p):
    """Up the leant-back spine; the shoulders' bulk goes with the clavicles and the upper arms, the hips with the thighs."""
    p = Vector(p)
    w = SPINE_W(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    if p.z > 0.76 and ax > 0.09:
        k = smooth01(0.09, 0.2, ax) * smooth01(0.76, 0.84, p.z)
        w = mix(w, {f"clavicle_{s}": 1.0}, 0.55 * k)
        w = mix(w, {f"upperarm_{s}": 1.0}, 0.45 * k * smooth01(0.16, 0.24, ax))
    if p.z < 0.52 and ax > 0.05:
        w = mix(w, {f"thigh_{s}": 1.0}, 0.5 * smooth01(0.05, 0.11, ax) * smooth01(0.52, 0.44, p.z))
    return w


# The trunk: a small pelvis, the waist drawn in and starved, the ribs' cage leant back over it, and on top the heavy
# hump of the shoulders, wider than the hips by half again: all the strength is up there.
flesh.blob((0, 0.0, 0.5), (0.105, 0.085, 0.075), 0, SKIN, trunk_w)                                  # the pelvis
flesh.blob((0, -0.035, 0.62), (0.085, 0.075, 0.075), 0.06, SKIN, trunk_w)                           # the waist
flesh.blob((0, -0.085, 0.755), (0.135, 0.11, 0.115), 0.06, SKIN, trunk_w, rot=R(rx=14))              # the ribs
flesh.blob((0, -0.115, 0.845), (0.16, 0.115, 0.085), 0.06, SKIN, trunk_w, rot=R(rx=10))              # the chest's top
flesh.blob((0, -0.15, 0.905), (0.15, 0.085, 0.075), 0.05, SKIN, trunk_w)                             # the hump behind the head
for sx in (1, -1):
    # The shoulders: great round masses over the arms' roots, the trapezius ridged up between them and the skull.
    flesh.blob((sx * 0.195, -0.075, 0.875), (0.1, 0.11, 0.095), 0.05, SKIN, trunk_w, around=20, rings=10)
    flesh.blob((sx * 0.1, -0.11, 0.93), (0.09, 0.075, 0.05), 0.05, SKIN, trunk_w, rot=R(ry=sx * 18))
    # The shoulder blades, standing proud on the leant-back back.
    flesh.blob((sx * 0.09, -0.19, 0.8), (0.06, 0.03, 0.075), 0.03, SKIN, trunk_w, rot=R(rx=14, rz=sx * 10))
# The belly hollowed under the ribs; the spine's knuckles down the back.
flesh.carve((0, 0.07, 0.66), (0.07, 0.035, 0.05), 0.03)
for k in range(9):
    t = k / 8
    c = Vector((0, -0.04, 0.55)).lerp(Vector((0, -0.13, 0.86)), t)
    top = flesh.surface(c, (0, -1, 0.25), 0.3)
    flesh.blob(top - Vector((0, -0.008, 0)), (0.018, 0.014, 0.016), 0.012, SKIN, trunk_w, around=10, rings=6)

# The arms: thick and corded in the upper arm (dragging arms), the forearm long, the hand's palm narrow.
PALMS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, tip = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    arm = chain([H(f"clavicle_{s}"), sh, el, wr, tip], [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.04)
    flesh.limb([sh + Vector((-sx * 0.01, 0, 0.01)), sh.lerp(el, 0.4), el], [0.085, 0.08, 0.05], 0.04, SKIN,
               lambda p, arm=arm: mix(arm(p), trunk_w(p), smooth01(0.8, 0.9, p[2]) * 0.6))
    # The biceps and the triceps: the dragging muscle, bunched.
    flesh.blob(sh.lerp(el, 0.45) + Vector((sx * 0.012, 0.03, 0)), (0.062, 0.062, 0.1), 0.03, SKIN, arm,
               rot=R(rx=-26, ry=-sx * 6))
    flesh.blob(el + Vector((0, -0.012, 0.0)), (0.038, 0.038, 0.04), 0.02, SKIN, arm, around=12, rings=8)  # the elbow
    flesh.limb([el, el.lerp(wr, 0.3), wr], [0.05, 0.048, 0.03], 0.02, SKIN, arm)
    palm = wr.lerp(tip, 0.55)
    PALMS[s] = palm
    flesh.blob(palm, (0.034, 0.045, 0.017), 0.02, SKIN, arm, rot=R(rx=-40), around=14, rings=6)

# The legs: thin, the knees knobbed, the shins all bone, the feet long and flat; dug in.
for s, sx in (("r", 1), ("l", -1)):
    hip, kn, an, ball, tt = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"toe_{s}"), T(f"toe_{s}")
    leg = chain([hip, kn, an, ball, tt], [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"toe_{s}"], soft=0.04)
    flesh.limb([hip + Vector((0, 0, 0.02)), hip.lerp(kn, 0.5), kn], [0.06, 0.045, 0.034], 0.035, SKIN,
               lambda p, leg=leg: mix(leg(p), trunk_w(p), smooth01(0.44, 0.52, p[2]) * 0.6))
    flesh.blob(kn + Vector((0, 0.012, 0)), (0.035, 0.034, 0.036), 0.015, SKIN, leg, around=12, rings=8)  # the knee
    flesh.limb([kn, kn.lerp(an, 0.4), an], [0.032, 0.026, 0.02], 0.015, SKIN, leg)
    flesh.blob(an + Vector((0, -0.012, -0.01)), (0.022, 0.03, 0.026), 0.012, SKIN, leg)                 # the heel
    flesh.blob(an.lerp(ball, 0.55) + Vector((0, 0, -0.022)), (0.032, 0.07, 0.018), 0.02, SKIN, leg, rot=R(rx=-16))

# The neck, short and thick, and the head: small, round at the back, low between the shoulders and pushed forward; the
# brow heavy, and off it the veil: the loose skin of the face hanging down over the eyes and the mouth in a fold.
HEAD = Vector((0, 0.075, 0.905))
flesh.limb([H("neck"), T("neck") + Vector((0, 0.01, 0))], [0.06, 0.055], 0.04, SKIN, trunk_w)
flesh.blob(HEAD + Vector((0, -0.01, 0.01)), (0.062, 0.075, 0.066), 0.03, SKIN, trunk_w, around=18, rings=10)  # the skull
flesh.blob(HEAD + Vector((0, 0.062, 0.036)), (0.064, 0.03, 0.024), 0.018, SKIN, trunk_w)                     # the brow
flesh.blob(HEAD + Vector((0, 0.07, -0.035)), (0.04, 0.045, 0.035), 0.02, SKIN, trunk_w)                      # the muzzle under it


def veil_w(p):
    p = Vector(p)
    return mix(trunk_w(p), {"veil": 1.0}, smooth01(0.9, 0.85, p.z) * smooth01(HEAD.y + 0.08, HEAD.y + 0.11, p.y))


# The veil: a curtain of the face's own loose skin hanging off the brow in front of the eyes and the mouth, its sides
# folded back round the cheeks; its lower edge hangs in tatters (`fine`).
VEIL_C = HEAD + Vector((0, 0.112, -0.03))
flesh.blob(VEIL_C, (0.062, 0.015, 0.072), 0.015, VEIL, veil_w, rot=R(rx=-12), around=18, rings=10)
for sx in (1, -1):
    flesh.blob(HEAD + Vector((sx * 0.05, 0.09, -0.025)), (0.018, 0.03, 0.06), 0.015, VEIL, veil_w, rot=R(rz=sx * 20))

# ----------------------------------------------------------------------------------------------------------------
# Finer than the skin: the veil's ragged hem, the eyes' glints at its edges, the fingers, the thumbs and the toes.
fine = kit.part("fine")
EYES = []
for sx in (1, -1):
    e = HEAD + Vector((sx * 0.047, 0.068, 0.012))
    EYES.append(e)
    fine.blob(e, (0.011, 0.008, 0.009), 8, 5, EYE, "head")
# The hem: a few broad rags of the same loose skin hanging unevenly off the veil's lower edge, overlapping, twisted,
# one longer than the rest (not a row: they'd read as teeth).
VEIL_AT = []
for i, (x, L, w, twist) in enumerate(((-0.04, 0.035, 0.03, 0.4), (-0.012, 0.07, 0.034, -0.3), (0.02, 0.045, 0.03, 0.5), (0.045, 0.025, 0.024, -0.2))):
    top = flesh.surface(Vector((x, VEIL_C.y - 0.012, VEIL_C.z - 0.03)), (0, 0.35, -1.0), 0.12) + Vector((0, -0.004, 0.02))
    fwd = 0.006 + 0.01 * h01(72, i)
    pts = [top, top + Vector((x * 0.1, fwd * 0.5, -L * 0.45)), top + Vector((x * 0.2, fwd, -L * 0.8)), top + Vector((x * 0.25, fwd * 0.8, -L))]
    VEIL_AT.append(pts[-1])
    refs = [(math.sin(twist * k / 3), math.cos(twist * k / 3), 0) for k in range(4)]
    fine.tube(pts, [(w * 0.55, 0.004), (w * 0.6, 0.0035), (w * 0.45, 0.003), (w * 0.2, 0.002)], 6, VEIL, "veil",
              ref=refs, cap1="point")

# The fingers: twice a hand's length, knuckled, hooked at the ends, the nails long and curled under; the thumb short.
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    for f in FINGERS:
        a, b, c = H(f"finger_{f}_{s}_01"), H(f"finger_{f}_{s}_02"), T(f"finger_{f}_{s}_02")
        a = a - (b - a).normalized() * 0.02
        bones = chain([a, b, c], [f"finger_{f}_{s}_01", f"finger_{f}_{s}_02"], soft=0.015)
        hook = c + (c - b).normalized() * 0.004 + Vector((0, -0.018, -0.006))
        TIPS.append(hook)
        fine.tube([a, a.lerp(b, 0.5), b, b.lerp(c, 0.5), c, hook], [0.0095, 0.0088, 0.0085, 0.0075, 0.0065, 0.003], 6, SKIN, bones,
                  ref=(0, 1, 0), cap0="point",
                  fmat=lambda pts, n, c=c: NAIL if (sum(pts, Vector()) / len(pts) - c).length < 0.016 else SKIN)
        # The nail: a curled hook off the last joint, under.
        fine.tube([c + Vector((0, 0, 0.004)), c + (c - b).normalized() * 0.012, hook + Vector((0, -0.006, -0.008))],
                  [(0.007, 0.004), (0.006, 0.003), (0.001, 0.001)], 5, NAIL, f"finger_{f}_{s}_02", ref=(0, 0, 1), cap1="point")
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    fine.tube([th0 - (th1 - th0) * 0.3, th0.lerp(th1, 0.5), th1, th1 + Vector((-sx * 0.004, 0.0, -0.016))],
              [0.014, 0.012, 0.009, 0.003], 6, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point",
              fmat=lambda pts, n, th1=th1: NAIL if (sum(pts, Vector()) / len(pts) - th1).length < 0.012 else SKIN)
    # Three long toes splayed for grip, clawed.
    ball = H(f"toe_{s}")
    for k in range(3):
        dx = (k - 1) * 0.02
        a0 = ball + Vector((sx * dx, -0.02, 0.002))
        end = a0 + Vector((sx * dx * 0.4, 0.07 - 0.012 * abs(k - 1), -0.006))
        TIPS.append(end)
        fine.tube([a0, a0.lerp(end, 0.55) + Vector((0, 0, 0.006)), end, end + Vector((0, 0.012, -0.008))], [0.011, 0.009, 0.006, 0.002], 5,
                  SKIN, f"toe_{s}", ref=(0, 0, 1), cap1="point",
                  fmat=lambda pts, n, end=end: NAIL if (sum(pts, Vector()) / len(pts) - end).length < 0.012 else SKIN)


# ----------------------------------------------------------------------------------------------------------------
# Clips. The rest is the stoop it waits in; angles in the armature's axes: +X swings a hanging limb forward and tips a
# spine back, a finger curls with -X.

def curl(k, thumb=0.0):
    """The hands' hook: every finger's joints curled by k (degrees), the thumbs by `thumb`."""
    out = {}
    for s in ("r", "l"):
        for i, f in enumerate(FINGERS):
            out[f"finger_{f}_{s}_01"] = (-k * (0.8 + 0.1 * i), 0, 0)
            out[f"finger_{f}_{s}_02"] = (-k * 1.2, 0, 0)
        out[f"thumb_{s}"] = (-thumb, 0, 0)
    return out


def with_(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


STOOP = with_({"pelvis": (0, 0, 0), "veil": (0, 0, 0)}, curl(22, 14))

# Wait (2 s, loop): hunched, rocking on its heels, the head turning in jerks (held, then snapped round: GDD §31).
wait = Clip("wait")
for f, rock, look, tilt in ((0, 0, 0, 0), (10, 1, 0, 0), (14, 1, 28, 6), (24, -1, 28, 6), (28, -1, -22, -4), (40, 1, -22, -4),
                            (44, 1, 4, 8), (52, -1, 4, 8)):
    wait.key(f, with_(STOOP, {
        "pelvis": (2 * rock, 0, 0), "spine_01": (2 * rock, 0, 0), "spine_02": (-1.5 * rock, 0, 0),
        "head": (-4 + tilt, 0, look), "neck": (0, 0, look * 0.25), "veil": (-3 * rock, 0, -look * 0.15),
        "upperarm_r": (3 * rock, 0, 0), "upperarm_l": (3 * rock, 0, 0), "root@loc": (0, -0.01 * rock, 0)}),
        "CONSTANT" if f in (14, 28, 44) else "BEZIER")
wait.close(60)

# Creep (1.2 s, loop): the slow bent walk in, stooped forward over its knees, the arms hanging low and swinging, the
# fingers trailing; each step set down carefully. (The sim's creep is 1.4 m/s: a stride of ~0.84 m a second, two steps.)
creep = Clip("creep")
BENT = with_(STOOP, {"pelvis": (-16, 0, 0), "spine_01": (-8, 0, 0), "spine_02": (-4, 0, 0), "neck": (10, 0, 0), "head": (12, 0, 0),
                     "upperarm_r": (8, 0, 0), "upperarm_l": (8, 0, 0), "lowerarm_r": (6, 0, 0), "lowerarm_l": (6, 0, 0)})
for f, k in ((0, 1), (9, 0), (18, -1), (27, 0)):
    step = {"thigh_r": (24 * k + 6, 0, 0), "thigh_l": (-24 * k + 6, 0, 0),
            "calf_r": (-30 - 18 * max(0, -k), 0, 0), "calf_l": (-30 - 18 * max(0, k), 0, 0),
            "foot_r": (6 * k, 0, 0), "foot_l": (-6 * k, 0, 0),
            "upperarm_r": (8 - 14 * k, 0, 0), "upperarm_l": (8 + 14 * k, 0, 0),
            "spine_02": (-4, 0, 4 * k), "head": (12, 0, -5 * k),
            "root@loc": (0, 0, -0.03 - 0.02 * abs(k))}
    creep.key(f, with_(BENT, step), "BEZIER")
creep.close(36)

# Startle (0.6 s): a jump straight back, the arms flung up over the head and the veil swung, then crouched further off.
startle = Clip("startle", loop=False)
startle.key(0, STOOP, "LINEAR")
startle.key(3, with_(STOOP, {"root@loc": (0, -0.16, 0.14), "pelvis": (12, 0, 0), "spine_01": (10, 0, 0), "spine_02": (8, 0, 0),
                             "neck": (-10, 0, 0), "head": (-14, 0, 0), "veil": (24, 0, 0),
                             "clavicle_r": (0, 0, 0), "upperarm_r": (110, -30, 0), "upperarm_l": (110, 30, 0),
                             "lowerarm_r": (40, 0, 0), "lowerarm_l": (40, 0, 0), "thigh_r": (20, 0, 0), "thigh_l": (14, 0, 0),
                             "calf_r": (-10, 0, 0), "calf_l": (-6, 0, 0), **curl(-10, -10)}), "LINEAR")
startle.key(8, with_(STOOP, {"root@loc": (0, -0.32, -0.04), "pelvis": (-6, 0, 0), "spine_01": (4, 0, 0), "head": (6, 0, 0),
                             "upperarm_r": (70, -20, 0), "upperarm_l": (70, 20, 0), "lowerarm_r": (50, 0, 0), "lowerarm_l": (50, 0, 0),
                             "thigh_r": (34, 0, 0), "thigh_l": (30, 0, 0), "calf_r": (-46, 0, 0), "calf_l": (-42, 0, 0),
                             "foot_r": (14, 0, 0), "foot_l": (12, 0, 0), **curl(30, 20)}), "BEZIER")
startle.key(18, with_(STOOP, {"root@loc": (0, -0.34, 0), "upperarm_r": (20, 0, 0), "upperarm_l": (20, 0, 0), **curl(20, 10)}), "BEZIER")


# Drag (1.6 s, loop): both hands hooked in the body's clothes at the sim's hold (in front of it, low), leant right back,
# heels dug in, hauling in tugs: lean, heave back (a lurch), a shuffle back with the feet, lean again.
def reach_hold(pose, lean):
    p = dict(pose)
    for s, sx in (("r", 1), ("l", -1)):
        target = HOLD + Vector((sx * 0.085, -lean, 0.02))
        p = rig.reach(sk, p, f"upperarm_{s}", f"lowerarm_{s}", target, end=f"hand_{s}", elbow_axis=0, bend=1.0,
                      pole=Vector((sx * 0.4, 0.2, 0.55)))
    return p


HAUL = with_({"pelvis": (14, 0, 0), "spine_01": (10, 0, 0), "spine_02": (4, 0, 0), "neck": (-6, 0, 0), "head": (-2, 0, 0),
              "thigh_r": (34, 0, 0), "thigh_l": (22, 0, 0), "calf_r": (-30, 0, 0), "calf_l": (-36, 0, 0),
              "foot_r": (18, 0, 0), "foot_l": (24, 0, 0), "toe_r": (10, 0, 0), "toe_l": (10, 0, 0), "veil": (8, 0, 0)}, curl(62, 55))
drag = Clip("drag")
for f, k, shuffle in ((0, 0.0, 0), (10, 1.0, 0), (13, 1.3, 0), (20, 0.4, 1), (24, 0.0, 0), (34, 1.0, 0), (37, 1.3, 0), (44, 0.4, -1)):
    pose = with_(HAUL, {"pelvis": (14 + 10 * k, 0, 0), "spine_01": (10 + 6 * k, 0, 0), "spine_02": (4 + 4 * k, 0, 0),
                        "head": (-2 - 8 * k, 0, 0), "veil": (8 + 10 * k, 0, 0), "root@loc": (0, -0.05 * k, -0.04 * k)})
    if shuffle:
        side = "r" if shuffle > 0 else "l"
        pose[f"thigh_{side}"] = (6, 0, 0)
        pose[f"calf_{side}"] = (-56, 0, 0)
    drag.key(f, reach_hold(pose, 0.05 * k), "LINEAR" if f in (10, 34) else "BEZIER")
drag.close(48)

# Scatter (0.4 s, loop): the darting run off, low, the arms flung back, the long fingers trailing.
scatter = Clip("scatter")
RUN = with_(STOOP, {"pelvis": (-24, 0, 0), "spine_01": (-10, 0, 0), "spine_02": (-6, 0, 0), "neck": (12, 0, 0), "head": (16, 0, 0),
                    "upperarm_r": (-40, 0, 0), "upperarm_l": (-40, 0, 0), "lowerarm_r": (20, 0, 0), "lowerarm_l": (20, 0, 0),
                    "veil": (-16, 0, 0)})
for f, k in ((0, 1), (6, -1)):
    scatter.key(f, with_(RUN, {"thigh_r": (40 * k + 10, 0, 0), "thigh_l": (-40 * k + 10, 0, 0),
                               "calf_r": (-20 - 60 * max(0, -k), 0, 0), "calf_l": (-20 - 60 * max(0, k), 0, 0),
                               "upperarm_r": (-40 - 20 * k, 0, 0), "upperarm_l": (-40 + 20 * k, 0, 0),
                               "root@loc": (0, 0, -0.02 + 0.05 * (f == 0))}), "LINEAR")
scatter.close(12)

hit = Clip("hit", loop=False)
hit.key(0, STOOP, "CONSTANT")
hit.key(2, with_(STOOP, {"pelvis": (10, 0, 12), "spine_02": (8, 0, 16), "head": (-12, 0, 24), "veil": (20, 0, -20),
                         "upperarm_r": (40, -20, 0), "upperarm_l": (30, 20, 0), "root@loc": (0, -0.06, 0.02), **curl(-10, -10)}), "CONSTANT")
hit.key(8, with_(STOOP, {"pelvis": (4, 0, -4), "head": (4, 0, -6)}), "LINEAR")
hit.key(12, STOOP, "CONSTANT")

# Death (1 s): it goes over backward, the way it always leant, and lies with its knees up and its hooked hands curled
# in on its chest.
death = Clip("death", loop=False)
death.key(0, STOOP, "CONSTANT")
death.key(3, with_(STOOP, {"pelvis": (20, 0, 6), "spine_02": (10, 0, 10), "head": (-20, 0, 18), "veil": (24, 0, 0),
                           "upperarm_r": (60, -30, 0), "upperarm_l": (50, 30, 0), "root@loc": (0, -0.08, 0.03)}), "LINEAR")
death.key(12, with_(STOOP, {"pelvis": (70, 0, 10), "spine_01": (10, 0, 0), "spine_02": (6, 0, 0), "head": (-10, 0, 30),
                            "veil": (40, 0, 0), "upperarm_r": (60, -20, 0), "upperarm_l": (60, 20, 0),
                            "thigh_r": (40, 0, 0), "thigh_l": (52, 0, 0), "calf_r": (-60, 0, 0), "calf_l": (-70, 0, 0),
                            "root@loc": (0, -0.3, -0.28), **curl(70, 40)}), "LINEAR")
death.key(16, with_(STOOP, {"pelvis": (84, 0, 8), "spine_01": (8, 0, 0), "spine_02": (2, 0, 0), "head": (-14, 0, 34),
                            "veil": (50, 0, 0), "upperarm_r": (80, -10, 0), "upperarm_l": (80, 10, 0), "lowerarm_r": (40, 0, 0),
                            "lowerarm_l": (40, 0, 0), "thigh_r": (50, 0, 0), "thigh_l": (64, 0, 0),
                            "calf_r": (-80, 0, 0), "calf_l": (-90, 0, 0), "root@loc": (0, -0.34, -0.36), **curl(80, 50)}), "BEZIER")
death.key(30, with_(STOOP, {"pelvis": (86, 0, 8), "spine_01": (8, 0, 0), "spine_02": (2, 0, 0), "head": (-16, 0, 40),
                            "veil": (55, 0, 0), "upperarm_r": (86, -14, 0), "upperarm_l": (84, 14, 0), "lowerarm_r": (50, 0, 0),
                            "lowerarm_l": (46, 0, 0), "thigh_r": (44, 0, 0), "thigh_l": (60, 0, 0),
                            "calf_r": (-84, 0, 0), "calf_l": (-92, 0, 0), "root@loc": (0, -0.34, -0.37), **curl(90, 60)}), "BEZIER")

# One skin from the feet to the brow (rig.fuse, settled on the flesh's field, then QuadriFlow): toward a character's
# budget, light enough for six in a ring (CreatureArtTests: 3-7k).
kit.fuse("body", ["body"], voxel=0.0035, faces=SKIN_FACES, lose=0.03, settle=flesh.settle)
kit.build()
rig.bake(sk, [wait, creep, startle, drag, scatter, hit, death])
print("[dt] mourner", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "mourner.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
