"""THE CLIMBERS (GDD v1.2 §21 the flank, App. A.4 · movement): "A pack runs alongside the train and mounts any coupling
gap held by fewer players than there are Climbers."

Redone (the director, Look Review, 23 Oct 2026: "I just dont like how this thing looks or animates"; note 1015). Not men
(note 136), and no longer the six-limbed black gecko: a starved thing like a fisher or a marten grown to a man's length,
the kind of animal that climbs, gone wrong. Long and low, its back bowed high over the loins, every knuckle of the spine
and every rib standing under skin gone bald with mange: a sick grey-pink, scabbed, and dark matted fur left in patches
and in a ragged ridge down its spine. Its legs are a weasel's, short in the thigh, but its hands are long-fingered and
nearly a man's, a raccoon's grown wrong, the fingers knuckled, hooked with black claws, for gripping steel; the hind
feet the same, longer. The head is a weasel's skull with the lips gone: the gums bare and the needle teeth showing the
length of the jaw, and small blind eyes the colour of milk that throw the lamp back. A bald rat's tail as long as it is.

It moves the way a mustelid moves, too fast: alongside the train it bounds, the spine bunching and flinging out, the
fore and hind pairs landing in turn, the head held dead level whatever the body does; up a coupling gap it goes hand
over hand like a marten up a trunk; on the roofs it slinks flat, stopping dead to sniff; in the dark of a car it stands
up on its hind legs, the long hands hanging at its chest, and watches.

SK_Climber: root, the body (pelvis, three spine bones), the neck's two, the head and its jaw (`mouth`), the tail's three,
and the four limbs' four bones each (upper, lower, hand, hook: the fingers or toes). Faces +Y (the engine's -Z). Clips
(GDD §31: still, then too fast): run (the bound, beside the train), scrabble (up between the cars facing in: a diagonal
gait, the hands thrown up and hooked on, note 823), walk (slinking along the roofs toward the engine, a stop to sniff),
crouch (up on its hind legs in an unlit car, watching, the head cocking), grab (reared on its victim, the hands
wrapped over their head, the jaws working at their face), hit.

    tools/models/build.sh climber        # this, its high copy and the bake -> content/art/models/climber.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()
BZ = 0.42                                      # the spine's height at the shoulders at rest
LIFT = 0.6 - 0.38                              # what the reared-up clips raise the root by (where the old rig's stood)
# The spine bowed high over the loins, as a weasel's is: up from the hips, down to the shoulders.
SPINE = [Vector((0, -0.5, 0.41)), Vector((0, -0.27, 0.48)), Vector((0, 0.0, 0.47)), Vector((0, 0.22, 0.43)), Vector((0, 0.38, BZ))]
NECK = [SPINE[-1], Vector((0, 0.52, 0.45)), Vector((0, 0.64, 0.48))]
HEAD_TIP = Vector((0, 0.95, 0.44))
TAIL = [SPINE[0], Vector((0, -0.8, 0.36)), Vector((0, -1.1, 0.27)), Vector((0, -1.42, 0.18))]
JAW = Vector((0, 0.69, 0.44))                  # the jaw's hinge, under the back of the skull
JAW_TIP = Vector((0, 0.93, 0.405))
# Each limb (the right's): where it leaves the body, the elbow (the fore's behind and under, the knee ahead), the wrist
# (or the hock), the palm's end (or the sole's), the fingers' tips; and the bone it hangs from.
LIMBS = {"f": ("spine_03", (0.09, 0.3, 0.37), (0.15, 0.2, 0.2), (0.14, 0.4, 0.045), (0.15, 0.5, 0.015), (0.155, 0.62, 0.0)),
         "h": ("pelvis", (0.1, -0.46, 0.38), (0.16, -0.29, 0.22), (0.15, -0.58, 0.1), (0.155, -0.42, 0.015), (0.16, -0.31, 0.0))}
bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
for k, name in enumerate(["pelvis", "spine_01", "spine_02", "spine_03"]):
    bones.append(Bone(name, "root" if k == 0 else bones[-1].name, tuple(SPINE[k]), tuple(SPINE[k + 1])))
bones.append(Bone("neck_01", "spine_03", tuple(NECK[0]), tuple(NECK[1])))
bones.append(Bone("neck_02", "neck_01", tuple(NECK[1]), tuple(NECK[2])))
bones.append(Bone("head", "neck_02", tuple(NECK[2]), tuple(HEAD_TIP)))
bones.append(Bone("mouth", "head", tuple(JAW), tuple(JAW_TIP)))
for k in range(3):
    bones.append(Bone(f"tail_0{k + 1}", "pelvis" if k == 0 else f"tail_0{k}", tuple(TAIL[k]), tuple(TAIL[k + 1])))
for s, sx in (("r", 1), ("l", -1)):
    for limb, (parent, *pts) in LIMBS.items():
        P = [Vector((sx * x, y, z)) for x, y, z in pts]
        for k, part in enumerate(("upper", "lower", "hand", "hook")):
            bones.append(Bone(f"{part}_{limb}{s}", parent, tuple(P[k]), tuple(P[k + 1])))
            parent = f"{part}_{limb}{s}"
sk = Skeleton("SK_Climber", bones)
sk.build()
kit = rig.Kit(sk, "climber")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


# Mange: bare skin a sick grey-pink (the recipe paints the scabs, the veins and the fur's patches); the fur left on it
# near black; the gums bare, dark red; the teeth yellowed; the claws black; the eyes milk.
SKIN = Mat("skin.climber", hexc("#8d7f7a"), shine=0.35)
FUR = Mat("wool.climber_fur", hexc("#1c1815"), shine=0.15)
GUM = Mat("tar.climber_mouth", hexc("#4a1512"), shine=0.6)
TOOTH = Mat("skin.climber_teeth", hexc("#b4a47a"), shine=0.4)
HOOK = Mat("tar.climber_nail", hexc("#0b0a09"), shine=0.6)
EYE = Mat("glass_dirty.climber_eye", hexc("#a8aca6"), shine=0.95, emissive=0.15, tint=(0.85, 0.9, 0.85))


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


BODY_BONES = ["tail_03", "tail_02", "tail_01", "pelvis", "spine_01", "spine_02", "spine_03", "neck_01", "neck_02", "head"]

# --- the body: a weasel's, starved: the chest deep and narrow, the waist pinched, the ribs and the spine standing ------
body = kit.part("body")
path = list(reversed(TAIL[1:])) + SPINE + NECK[1:]
pts = []
for i in range(len(path) - 1):
    n = 4 if path[i].y >= SPINE[0].y - 0.01 else 2
    for f in range(n):
        pts.append(path[i].lerp(path[i + 1], f / n))
pts.append(path[-1])
# (y, half width, half depth, how far the middle hangs under the spine line)
PROFILE = [(-1.42, 0.008, 0.008, 0.0), (-1.1, 0.018, 0.018, 0.0), (-0.8, 0.032, 0.03, 0.0), (-0.6, 0.06, 0.06, 0.02),
           (-0.48, 0.095, 0.095, 0.05), (-0.3, 0.08, 0.085, 0.05), (-0.12, 0.085, 0.1, 0.07), (0.05, 0.1, 0.125, 0.09),
           (0.22, 0.105, 0.13, 0.09), (0.36, 0.09, 0.1, 0.06), (0.5, 0.06, 0.06, 0.02), (0.64, 0.055, 0.055, 0.0)]


def profile(y):
    pr = PROFILE
    if y <= pr[0][0]:
        return pr[0][1:]
    if y >= pr[-1][0]:
        return pr[-1][1:]
    k = next(i for i in range(len(pr) - 1) if pr[i][0] <= y <= pr[i + 1][0])
    t = (y - pr[k][0]) / (pr[k + 1][0] - pr[k][0])
    t = t * t * (3 - 2 * t)
    return tuple(a + (b - a) * t for a, b in zip(pr[k][1:], pr[k + 1][1:]))


def ribs(i, j, a, p, fr):
    """The ribs in hoops down the chest's sides, the belly sunk behind them, the hip bones standing, the spine's knuckles
    in a row down the back; the chest hung under the spine line (a weasel's deep keel)."""
    p = Vector(p)
    side, up, _ = fr
    out = side * math.sin(a) + up * math.cos(a)
    y = p.y
    _, _, sag = profile(y)
    p -= up * sag
    chest = smooth01(-0.15, 0.0, y) * smooth01(0.36, 0.26, y)
    d = 0.011 * max(0.0, math.sin((y + 0.15) * 62)) ** 3 * chest * (1 - abs(math.cos(a)) ** 2)
    d -= 0.012 * smooth01(-0.32, -0.18, y) * smooth01(-0.02, -0.12, y) * max(0.0, -math.cos(a))   # the sunk belly
    d += 0.012 * bell((y + 0.46) / 0.05) * bell((abs(math.sin(a)) - 0.75) / 0.2) * (math.cos(a) > 0)  # hip bones
    d += 0.014 * bell(math.sin(a) / 0.16) * (math.cos(a) > 0) * max(0.0, math.sin(y * 48)) ** 2 * smooth01(-0.6, -0.45, y) * smooth01(0.62, 0.5, y)
    return p + out * (d + 0.002 * noise3(p * 20, 171, 1.0))


radii = [profile(q.y)[:2] for q in pts]
body.tube(pts, radii, 18, SKIN, (BODY_BONES, 5.0), ref=(0, 0, 1), shape=ribs, cap0=True)

# --- the head: a weasel's skull, lipless: the gums bare and the needle teeth the length of both jaws ------------------
head = kit.part("head")
# The skull lofted down its length (y, half width, top, bottom, squareness): the braincase behind, narrowing past the
# eyes to a long snout; the jaw the same under it, narrower, on its own bone.
SKULL = [(0.63, 0.045, 0.51, 0.43, 0.9), (0.7, 0.058, 0.532, 0.44, 0.85), (0.77, 0.056, 0.518, 0.444, 0.85), (0.83, 0.044, 0.496, 0.443, 0.8),
         (0.89, 0.03, 0.476, 0.441, 0.8), (0.95, 0.016, 0.461, 0.439, 0.85)]
JAWS = [(0.69, 0.04, 0.441, 0.41, 0.8), (0.78, 0.036, 0.439, 0.405, 0.8), (0.87, 0.026, 0.437, 0.41, 0.8), (0.935, 0.013, 0.436, 0.419, 0.85)]


def gaunt_skull(i, j, a, p):
    """The sockets sunk either side behind the snout, the cheekbones out under them, the brow's ridge over them."""
    p = Vector(p)
    for ex in (-1, 1):
        e = Vector((ex * 0.042, 0.83, 0.484))
        p -= Vector((ex, 0, 0.3)).normalized() * 0.01 * bell((p - e).length / 0.022)
        p += Vector((ex, 0, 0)) * 0.007 * bell((p - Vector((ex * 0.05, 0.8, 0.455))).length / 0.025)
    return p + Vector((0, 0, 0.0012 * noise3(p * 30, 172, 1.0)))


head.sections(SKULL, 16, SKIN, {"head": 1.0}, axis="y", cap0=True, cap1="point", shape=gaunt_skull)
head.sections(JAWS, 12, SKIN, {"mouth": 1.0}, axis="y", cap0=True, cap1="point")
head.blob(Vector((0, 0.8, 0.438)), (0.022, 0.08, 0.005), 10, 4, GUM, {"mouth": 1.0})   # the tongue, grey-red, in the jaw


def section(secs, y):
    k = next((i for i in range(len(secs) - 1) if secs[i][0] <= y <= secs[i + 1][0]), len(secs) - 2)
    t = (y - secs[k][0]) / (secs[k + 1][0] - secs[k][0])
    return tuple(a + (b - a) * t for a, b in zip(secs[k][1:4], secs[k + 1][1:4]))


# Lipless: the gums bare along both jaws' edges, and the teeth: needles raked back, hung outside the jaw's edge (the
# upper) and up inside it (the lower), the canines long.
for sx in (-1, 1):
    ys = [0.7 + 0.24 * k / 7 for k in range(8)]
    up_edge = [Vector((sx * section(SKULL, y)[0] * 0.92, y, section(SKULL, y)[2] + 0.004)) for y in ys]
    lo_edge = [Vector((sx * section(JAWS, min(y, 0.93))[0] * 0.9, min(y, 0.93), section(JAWS, min(y, 0.93))[1] - 0.002)) for y in ys]
    head.tube(up_edge, [(0.006, 0.006)] * 8, 6, GUM, "head", ref=(0, 0, 1))
    head.tube(lo_edge, [(0.005, 0.005)] * 8, 6, GUM, "mouth", ref=(0, 0, 1))
    for k in range(13):
        y = 0.71 + 0.225 * (k + 0.5) / 13
        canine = k == 10
        hw, _, bot = section(SKULL, y)
        root = Vector((sx * hw * 0.95, y, bot + 0.002))
        L = 0.032 if canine else 0.012 + 0.009 * math.sin(k * 2.3) ** 2
        tip = root + Vector((sx * 0.002, -0.005, -L))
        head.tube([root, root.lerp(tip, 0.5), tip], [0.0034 if canine else 0.0022, 0.0018, 0.0004], 4, TOOTH, "head", ref=(0, 1, 0), cap1="point")
    for k in range(11):
        y = 0.72 + 0.2 * (k + 0.5) / 11
        canine = k == 9
        hw, top, _ = section(JAWS, y)
        root = Vector((sx * hw * 0.8, y, top - 0.002))
        L = 0.022 if canine else 0.007 + 0.005 * math.sin(k * 1.7) ** 2
        tip = root + Vector((sx * -0.002, -0.003, L))
        head.tube([root, root.lerp(tip, 0.5), tip], [0.003 if canine else 0.002, 0.0016, 0.0004], 4, TOOTH, "mouth", ref=(0, 1, 0), cap1="point")
# The eyes: small and sunk in their sockets, blind milk; the ears: little round ones flat to the skull, one torn short.
for ex in (-1, 1):
    head.blob(Vector((ex * 0.038, 0.83, 0.481)), (0.008, 0.009, 0.0075), 8, 6, EYE, "head")
    head.blob(Vector((ex * 0.052, 0.67, 0.522)), (0.007, 0.02, 0.024 if ex > 0 else 0.014), 8, 4, SKIN, "head",
              rot=rig.rot(0, ex * 25, 0).to_matrix().to_4x4())

# --- the fur left on it: a ragged ridge down its spine and matted patches, the tail bald --------------------------------
fur = kit.part("fur")
for k in range(26):
    y = -0.62 + k * 0.045
    seg = "pelvis" if y < SPINE[1].y else "spine_01" if y < SPINE[2].y else "spine_02" if y < SPINE[3].y else "spine_03" if y < SPINE[4].y else "neck_01"
    hw, hd, sag = profile(y)
    # The spine line's height there.
    line = SPINE + NECK[1:]
    zz = next((a.z + (b.z - a.z) * (y - a.y) / (b.y - a.y) for a, b in zip(line, line[1:]) if a.y <= y <= b.y), SPINE[0].z)
    root = Vector((0.012 * math.sin(k * 2.9), y, zz + hd - sag - 0.004))
    L = 0.035 + 0.025 * (0.5 + 0.5 * math.sin(k * 1.9))
    tip = root + Vector((0.01 * math.sin(k * 4.1), -L * 0.8, L * 0.45))
    fur.tube([root, root.lerp(tip, 0.5), tip], [(0.02, 0.004), (0.014, 0.003), 0.001], 4, FUR, seg, ref=(1, 0, 0), cap1="point")

# --- the limbs: a weasel's, short and strong; the hands long-fingered, knuckled, hooked ---------------------------------
limbs = kit.part("limbs")
hands = kit.part("hands")   # the hands and the feet, their fingers and their claws: kept out of the fused skin (rig.fuse)
for s, sx in (("r", 1), ("l", -1)):
    for limb in LIMBS:
        up_, lo, ha, ho = (f"{part}_{limb}{s}" for part in ("upper", "lower", "hand", "hook"))
        fore = limb == "f"
        r0 = 0.05 if fore else 0.062
        sh, el, wr = H(up_), T(up_), T(lo)
        # The upper limb thick with what muscle it has left, the forearm (or shank) a cord with the tendons in it.
        limbs.tube([sh + (sh - el).normalized() * 0.04, sh.lerp(el, 0.4), el, el.lerp(wr, 0.35), el.lerp(wr, 0.75), wr],
                   [r0 * 1.15, r0, r0 * 0.7, r0 * 0.6, r0 * 0.42, r0 * 0.36], 10, SKIN, ([up_, lo, ha], 6.0), ref=(0, 0, 1))
        limbs.blob(el + (el - sh.lerp(wr, 0.5)).normalized() * 0.01, (r0 * 0.6,) * 3, 8, 5, SKIN, {up_: 0.5, lo: 0.5})   # elbow, hock
        # The hand (or the foot): a narrow palm, four long knuckled fingers spread on the steel, a thumb, black claws.
        w, e = H(ha), T(ha)
        fwd = (e - w).normalized()
        across = fwd.cross(Vector((0, 0, 1))).normalized()
        hands.tube([w, w.lerp(e, 0.5), e], [(0.026, 0.014), (0.034, 0.013), (0.036, 0.011)], 10, SKIN, ha, ref=(0, 0, 1))
        flen = 0.105 if fore else 0.085
        for f in range(4):
            o = (f - 1.5) * 0.017
            splay = (f - 1.5) * 0.12
            d = (fwd + across * splay).normalized()
            L = flen * (0.8 + 0.2 * (1 - abs(f - 1.5) / 1.5))
            root = e + across * o
            k1 = root + d * L * 0.4 + Vector((0, 0, 0.012))
            k2 = root + d * L * 0.72 + Vector((0, 0, 0.008))
            tip = root + d * L + Vector((0, 0, -0.008))
            claw = tip + d * 0.018 - Vector((0, 0, 0.012))
            wts = lambda p, root=root, L=L, ha=ha, ho=ho: {ha: 1.0} if (p - root).length < L * 0.15 else {ho: 1.0}
            hands.tube([root, k1, k2, tip], [0.0085, 0.008, 0.0065, 0.005], 6, SKIN, wts, ref=(0, 0, 1),
                       shape=lambda i, j, an, p, fr: Vector(p) + (fr[0] * math.sin(an) + fr[1] * math.cos(an)) * (0.0022 if i in (1, 2) else 0.0))
            hands.tube([tip - d * 0.008 + Vector((0, 0, 0.003)), tip + d * 0.006, claw], [(0.005, 0.003), (0.0035, 0.0025), 0.0006], 4, HOOK, ho,
                       ref=(0, 0, 1), cap1="point")
        inner = -across * sx
        troot = w.lerp(e, 0.4) + inner * 0.03
        ttip = troot + (inner * 0.6 + fwd * 0.8).normalized() * 0.05
        hands.tube([troot, ttip, ttip + fwd * 0.012 - Vector((0, 0, 0.01))], [0.008, 0.006, 0.0008], 5, SKIN, ha, ref=(0, 0, 1),
                   cap1="point")

# One skin from the snout of its back to the tip of its tail, the legs grown out of it (rig.fuse), as the Tippy Toesie's.
kit.fuse("body", ["body", "limbs"], voxel=0.0035, faces=2600)


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): +X tips a bone's far end up, Z turns it (+ to its left). The limbs go
# by IK (rig.reach, from a spread of starts): the wrist where it's wanted, the fore's elbow kept behind and under the
# shoulder and the hind's knee ahead of the hip, as a weasel's are; the hand then laid at a world rotation (rig.hang),
# the fingers curled by their own bone.
LIMBNAMES = [(limb, s) for limb in LIMBS for s in ("r", "l")]


def limb_to(pose, limb, s, wrist, climbing=False):
    up_, lo = f"upper_{limb}{s}", f"lower_{limb}{s}"
    sign = 1 if s == "r" else -1
    shoulder = rig.pose_points(sk, pose, [(up_, "head")])[0]
    axis = rig.pose_points(sk, pose, [("spine_02", "tail")])[0] - rig.pose_points(sk, pose, [("pelvis", "head")])[0]
    axis = axis.normalized()

    def avoid(elbow):
        # Out to its own side a little, never under the body; the fore's elbow behind the shoulder, the hind's knee ahead
        # of the hip (along the body, whichever way it's turned: up a gap the body's stood on end).
        c = max(0.0, shoulder.x * sign + 0.03 - elbow.x * sign) * 4
        along = (elbow - shoulder).dot(axis)
        c += max(0.0, along + 0.02) * 3 if limb == "f" else max(0.0, 0.02 - along) * 3
        if not climbing:
            c += max(0.0, elbow.z - shoulder.z + 0.02) * 3
        return c
    best = None
    for y in (-40, 0, 40):
        for z in (-40, 0, 40):
            for b in (1, -1):
                start = dict(pose) | {up_: (0, y, z), lo: (0, 0, 0)}
                p = rig.reach(sk, start, up_, lo, wrist, elbow_axis=0, bend=b, avoid=avoid)
                tip, elbow = rig.pose_points(sk, p, [(lo, "tail"), (lo, "head")])
                cost = (tip - Vector(wrist)).length + avoid(elbow)
                if best is None or cost < best[0]:
                    best = (cost, p)
    return best[1]


def hands_(p, lay=None, curl=0.0):
    """Each hand laid at a world rotation (default: flat, as it rests); the fingers curled `curl` degrees in."""
    p = dict(p)
    lay = lay or {}
    for limb, s in LIMBNAMES:
        p[f"hand_{limb}{s}"] = rig.hang(sk, p, f"hand_{limb}{s}", *lay.get(limb + s, (0, 0, 0)))
        p[f"hook_{limb}{s}"] = (-curl, 0, 0)
    return p


def euler_of(q):
    e = q.to_euler("YXZ")
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


def done(p):
    return {k: (euler_of(v) if hasattr(v, "to_euler") else v) for k, v in p.items()}


def rest_wrist(limb, s, spread=1.0, dy=0.0, z=0.045):
    x, y, _ = LIMBS[limb][3]
    return (x * spread * (1 if s == "r" else -1), y + dy, z)


def stance(body, wrists, lay=None, curl=0.0, climbing=False):
    p = dict(body)
    for (limb, s), w in wrists.items():
        p = limb_to(p, limb, s, w, climbing)
    return done(hands_(p, lay, curl))


def level_head(body, pitch_sum, turn=0.0):
    """The neck and head turned back against the body's pitch (`pitch_sum`, the spine's degrees summed), so the head
    stays level whatever the spine does: a weasel's head rides its bound like a gimbal's."""
    return body | {"neck_01": (-0.5 * pitch_sum, 0, 0.5 * turn), "neck_02": (-0.3 * pitch_sum, 0, 0.3 * turn), "head": (-0.2 * pitch_sum, 0, 0.2 * turn)}


# Run (0.5 s, loop): the bound. The spine bunches, the hind feet landing up behind the fore, then flings out flat, the fore
# reaching far ahead, and over it goes again, too fast; the fore and the hind pairs a little apart each (a half-bound),
# the head dead level, the tail streaming in a wave behind.
RUN = 15
run = Clip("run")
for f in range(0, RUN, 3):
    u = 2 * math.pi * f / RUN
    bunch = math.cos(u)          # 1 bunched (the back bowed up), -1 flung out
    arch = 16 * bunch
    body = {"root@loc": (0, 0.02 * bunch, -0.02 + 0.07 * max(0.0, math.sin(u + 0.6))),
            "pelvis": (-6 + 0.6 * arch, 0, 0), "spine_01": (0.4 * arch, 0, 0), "spine_02": (-0.5 * arch, 0, 0), "spine_03": (-0.6 * arch, 0, 0),
            "tail_01": (-10 + 12 * math.sin(u - 1.0), 0, 0), "tail_02": (10 * math.sin(u - 1.8), 0, 0), "tail_03": (14 * math.sin(u - 2.6), 0, 0)}
    body = level_head(body, (-6 + 0.6 * arch) + 0.4 * arch - 0.5 * arch - 0.6 * arch)
    body["mouth"] = (-6, 0, 0)
    wr = {}
    for limb, s in LIMBNAMES:
        lag = 0.25 if s == "l" else 0.0
        v = u + lag + (0.0 if limb == "f" else math.pi)
        swing = 0.24 * math.cos(v) * (1 if limb == "f" else 1.1)
        lift = 0.14 * max(0.0, math.sin(v))
        wr[(limb, s)] = rest_wrist(limb, s, 0.95, swing + (0.04 if limb == "f" else -0.02), 0.045 + lift)
    run.key(f, stance(body, wr, curl=20), "LINEAR")
run.close(RUN)

# Scrabble (0.8 s, loop): up between the cars facing in, stood on end up the gap; a marten up a trunk: the diagonal pairs
# (the right fore and the left hind, then the other two) thrown up and hooked on, then dragged down the length of their
# pull as the body's hauled up past them, the back bunching and stretching with each pull, snaking to the side that's
# pulling, the head pressed up, turning, jerking at the top of each pull; the tail hanging and lashing.
UPRIGHT = {"root@loc": (0, 0.1, 0.25 + LIFT), "pelvis": (72, 0, 0), "spine_01": (6, 0, 0), "spine_02": (4, 0, 0), "spine_03": (4, 0, 0),
           "neck_01": (-16, 0, 0), "neck_02": (-12, 0, 0), "head": (-18, 0, 0), "tail_01": (-56, 0, 0), "tail_02": (-20, 0, 0)}
scrabble = Clip("scrabble")
# (The hands out on both cars' ends either side of the gap, as the old rig's were; the hind feet on the buffers.)
GRIP = {"f": (0.4, 0.44, 1.7), "h": (0.38, 0.34, 0.85)}
CLIMB_PHASE = {("f", "r"): 0.0, ("h", "l"): 0.0, ("f", "l"): 0.5, ("h", "r"): 0.5}
REACH_SHARE, PULL = 0.32, 0.15                 # the share of a cycle a hand's off the steel; how far each hand travels each way


def climbing_hand(ph):
    """A hand `ph` (0..1) into its cycle: (up the wall, off it). Thrown up off the steel, then dragged down it on its pull."""
    if ph < REACH_SHARE:
        k = ph / REACH_SHARE
        k = k * k * (3 - 2 * k)
        return -PULL + 2 * PULL * k, 0.08 * math.sin(math.pi * ph / REACH_SHARE)
    k = (ph - REACH_SHARE) / (1 - REACH_SHARE)
    return PULL - 2 * PULL * k, 0.0


SCRABBLE_FRAMES = 24
for f in range(0, SCRABBLE_FRAMES, 2):
    t = f / SCRABBLE_FRAMES
    u = 2 * math.pi * t
    wr = {}
    for limb, s in LIMBNAMES:
        x, y, z = GRIP[limb]
        up, off = climbing_hand((t + CLIMB_PHASE[(limb, s)]) % 1)
        wr[(limb, s)] = (x * (1 if s == "r" else -1), y + off, z + up)
    sway = math.sin(u)
    pull = math.cos(2 * u)       # two pulls a cycle: 1 bunched (hands high), -1 stretched
    heave = 0.04 * math.sin(2 * u)
    snap = 12 * math.copysign(abs(math.sin(2 * u)) ** 0.35, math.sin(2 * u))
    body = UPRIGHT | {"root@loc": (0, 0.1, 0.25 + LIFT + heave), "pelvis": (72 + 3 * pull, 0, 10 * sway),
                      "spine_01": (6 + 8 * pull, 0, -7 * sway), "spine_02": (4 - 6 * pull, 0, -5 * sway), "spine_03": (4, 0, 5 * sway),
                      "neck_02": (-12, 0, 0.5 * snap), "head": (-18 - 5 * math.sin(2 * u), 0, snap), "mouth": (-10 - 8 * max(0.0, math.sin(2 * u)), 0, 0),
                      "tail_01": (-56, 0, -14 * math.sin(u - 0.7)), "tail_02": (-20, 0, -22 * math.sin(u - 1.4)),
                      "tail_03": (0, 0, -26 * math.sin(u - 2.1))}
    scrabble.key(f, stance(body, wr, lay={k + s: (90, 0, 0) for k, s in LIMBNAMES}, curl=40 + 15 * abs(math.sin(2 * u)), climbing=True), "LINEAR")
scrabble.close(SCRABBLE_FRAMES)

# Walk (2.4 s, loop): along the roofs toward the engine, slinking flat, the legs bent under it, one at a time (a hind,
# then the fore on that side), the head low and weaving as if it could smell its way; then stopped dead, the head up,
# a sniff, a snap round to one side, held, and on again.
WALK = 72
walk = Clip("walk")
ORDER = [("h", "r"), ("f", "r"), ("h", "l"), ("f", "l")]
for f in range(0, WALK, 4):
    stepping = f < 48
    ph = f / 48 if stepping else 1.0
    weave = math.sin(2 * math.pi * ph)
    body = {"root@loc": (0, 0, -0.13), "pelvis": (2, 0, 4 * weave), "spine_01": (-2, 0, -2 * weave), "spine_03": (-4, 0, -3 * weave),
            "neck_01": (-14, 0, 18 * weave), "neck_02": (4, 0, 8 * weave), "head": (8, 0, 6 * weave),
            "tail_01": (-8, 0, -10 * weave), "tail_02": (4, 0, -14 * weave), "tail_03": (6, 0, -16 * weave)}
    if not stepping:
        # Stopped dead: the head up, sniffing (it bobs), then snapped round to its right and held.
        k = f - 48
        body |= {"neck_01": (14, 0, 0 if k < 12 else 34), "neck_02": (6 + 4 * (k % 8 < 4), 0, 0 if k < 12 else 18),
                 "head": (-6, 0, 0 if k < 12 else 12), "mouth": (-4 * (k % 8 < 4), 0, 0)}
    wr = {}
    for idx, (limb, s) in enumerate(ORDER):
        local = (ph * 4 - idx) % 4
        if stepping and local < 1:
            dy, lift = 0.2 * (local - 0.5) * 2, 0.09 * math.sin(math.pi * local)
        else:
            dy, lift = 0.2 * (1 - 2 * ((local - 1) % 4) / 3), 0.0
        wr[(limb, s)] = rest_wrist(limb, s, 1.15, dy, 0.045 + lift)
    walk.key(f, stance(body, wr, curl=16), "CONSTANT" if f in (48, 60) else "LINEAR")
walk.close(WALK)

# Crouch (4 s, loop): inside an unlit car, stood up on its hind legs as a stoat stands to look, the back straight up,
# the long hands hanging at its chest, the fingers moving; the head turning slowly, cocking over, and snapping back.
SIT = {"root@loc": (0, 0.0, -0.1 + LIFT), "pelvis": (64, 0, 0), "spine_01": (10, 0, 0), "spine_02": (6, 0, 0), "spine_03": (2, 0, 0),
       "neck_01": (-40, 0, 0), "neck_02": (-24, 0, 0), "tail_01": (-62, 0, 26), "tail_02": (-6, 0, 40), "tail_03": (0, 0, 40)}
SIT_WR = {("f", "r"): (0.1, 0.3, 0.98), ("f", "l"): (-0.1, 0.3, 0.98), ("h", "r"): (0.17, -0.36, 0.045), ("h", "l"): (-0.17, -0.36, 0.045)}
crouch = Clip("crouch")
for f, (turn, cock, fing) in ((0, (0, 0, 30)), (30, (24, 0, 60)), (60, (24, 30, 40)), (90, (24, 30, 70)), (92, (-20, -6, 40))):
    crouch.key(f, stance(SIT | {"head": (-14, cock, turn), "neck_02": (-24, 0, 0.5 * turn)}, SIT_WR,
                         lay={"fr": (-70, 0, 0), "fl": (-70, 0, 0)}, curl=fing), "CONSTANT" if f == 92 else "BEZIER")
crouch.key(110, stance(SIT | {"head": (-14, -6, -20), "neck_02": (-24, 0, -10)}, SIT_WR, lay={"fr": (-70, 0, 0), "fl": (-70, 0, 0)}, curl=40), "BEZIER")
crouch.close(120)

# Grab (1.2 s, loop): reared up on its victim in front of it, the long hands wrapped over their head and gripping, the
# hind feet braced; the jaws at their face, working: biting in, worrying, drawn back and in again, twice a loop, the
# whole of it shuddering; the tail lashing behind.
REAR = {"root@loc": (0, -0.25, 0.1 + LIFT), "pelvis": (44, 0, 0), "spine_01": (10, 0, 0), "spine_02": (6, 0, 0), "spine_03": (-4, 0, 0),
        "neck_01": (-24, 0, 0), "neck_02": (-24, 0, 0), "head": (-26, 0, 0), "tail_01": (-34, 0, 0)}
GRAB_WR = {("f", "r"): (0.14, 0.66, 1.72), ("f", "l"): (-0.14, 0.64, 1.72), ("h", "r"): (0.24, -0.5, 0.045), ("h", "l"): (-0.24, -0.5, 0.045)}
grab = Clip("grab")
GRAB_FRAMES = 36
for f in range(0, GRAB_FRAMES, 2):
    t = f / GRAB_FRAMES
    u = 2 * math.pi * t
    drive = ((1 - math.cos(2 * u)) / 2) ** 0.6       # 0 drawn back, 1 biting in
    worry = 10 * math.sin(4 * u) * drive              # the head shaken as it bites (a terrier's worrying)
    tremor = 0.6 * math.sin(f * 2.7) + 0.4 * math.sin(f * 4.3)
    body = REAR | {"root@loc": (0, -0.25 + 0.07 * drive, 0.1 + LIFT - 0.03 * drive),
                   "pelvis": (44 + 5 * drive, 0, 2 * math.sin(u)), "spine_01": (10 + 2 * drive, 0, -2 * math.sin(u)),
                   "spine_02": (6 + 3 * drive, tremor, 0), "spine_03": (-4 + 4 * drive, 0, 0),
                   "neck_01": (-24 - 4 * drive, 0, 3 * math.sin(u + 0.5)), "neck_02": (-24 - 6 * drive, 0, 0),
                   "head": (-26 - 8 * drive + tremor, worry, 4 * math.sin(u + 1.0)),
                   "mouth": (-44 * (1 - drive) - 8, 0, 0),
                   "tail_01": (-34, 0, 16 * math.sin(u)), "tail_02": (0, 0, 22 * math.sin(u - 0.9)), "tail_03": (0, 0, 26 * math.sin(u - 1.8))}
    wr = dict(GRAB_WR)
    for s_ in ("r", "l"):
        k = 1 if s_ == "r" else -1
        fx, fy, fz = GRAB_WR[("f", s_)]
        hx, hy, hz = GRAB_WR[("h", s_)]
        wr[("f", s_)] = (fx - k * 0.03 * drive, fy + 0.04 * drive, fz - 0.05 * drive)
        shift = max(0.0, math.sin(u + (0 if s_ == "r" else math.pi)))
        wr[("h", s_)] = (hx, hy + 0.05 * shift, hz + 0.05 * shift)
    p = stance(body, wr, lay={"fr": (0, 0, -30), "fl": (0, 0, 30)}, curl=50 + 22 * drive, climbing=True)
    grab.key(f, p, "BEZIER")
grab.close(GRAB_FRAMES)

# Hit (0.4 s, once): struck, it flinches flat and twists round the blow, snapping at it, the tail lashing; and back.
REST = stance({}, {(limb, s): rest_wrist(limb, s) for limb, s in LIMBNAMES})
hit = Clip("hit", loop=False)
hit.key(0, REST, "CONSTANT")
hit.key(2, stance({"root@loc": (0, 0, -0.12), "spine_01": (0, 10, 18), "spine_03": (0, 0, -22), "neck_01": (10, 0, 30), "head": (20, 0, 30),
                   "mouth": (-40, 0, 0), "tail_01": (0, 0, 50)},
                  {(limb, s): rest_wrist(limb, s, 1.15) for limb, s in LIMBNAMES}, curl=40), "CONSTANT")
hit.key(8, stance({"spine_01": (0, 0, -6)}, {(limb, s): rest_wrist(limb, s) for limb, s in LIMBNAMES}), "LINEAR")
hit.key(12, REST, "CONSTANT")

# Up toward GDD §27's budget (a character's, kept light: a pack of them at once).
kit.target_tris = 7000
kit.build()
rig.bake(sk, [run, scrabble, walk, crouch, grab, hit])
print("[dt] climber", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "climber.glb", kit)
