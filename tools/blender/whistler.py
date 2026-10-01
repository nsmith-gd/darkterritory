"""THE WHISTLER (GDD v1.2 §21 between the cars, App. A.4 · absence): "Blows your own whistle, then hides in a coupling
gap. Only strikes when the train is stopped. It carries its victim off to a nest."

Not a man (note 131). A long pale thing, four metres of it, segmented like a centipede and thick as a thigh, with twenty
thin legs down its length and a pair of long hooked forelegs at its front. It lives coiled round the drawgear under the
bridge plate between two cars, grub-white where the plates part, the plates the yellowed grey of old ivory, all of it
streaked with the couplers' black grease. It has no eyes and no face. Its front is a smooth blunt wedge, two feelers
twitching off it, and out of the wedge a siphon: a long fleshy tube with a puckered lip at its end and stops along its
top like a flute's, which open and close. That is what whistles. Pulling the cord, it rears straight up out of the gap
three metres, the forelegs hooked over the cord along the eaves. It runs too fast, rippling, with someone held in its
forelegs under its front.

SK_Whistler: root, the body's ten segments (seg_01 at the tail to seg_10 behind the head), the head, the siphon's three,
the feelers, the twenty legs' two bones each (femur out and up from the segment, tibia down to the hook). Faces +Y (the
engine's -Z). Its origin is the gap's (the sim's GapLocal, 0.6 m over the rail): CreatureArt drops it to the rail.
Clips (GDD §31: still, then too fast): fold (coiled under the plate, the front laid on top of the coil looking out,
breathing; once a ripple down the legs), whistle (reared up out of the gap, the forelegs yanking the cord twice, the
siphon swelling, its stops opening), watch (the front lifted at the gap's mouth, swaying and stopping in jerks), run
(straightened out and gone, the body snaking, the legs in waves), hit (struck, once).

    tools/models/build.sh whistler        # this, its high copy and the bake -> content/art/models/whistler.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
N = 10
SEG = 0.4
Z0 = 0.13                       # the body's middle over the ground, laid flat
TAIL = -N * SEG / 2
bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0))]
for k in range(N):
    y0 = TAIL + k * SEG
    bones.append(Bone(f"seg_{k + 1:02d}", "root" if k == 0 else f"seg_{k:02d}", (0, y0, Z0), (0, y0 + SEG, Z0)))
FRONT = Vector((0, TAIL + N * SEG, Z0))
bones.append(Bone("head", f"seg_{N:02d}", tuple(FRONT), tuple(FRONT + Vector((0, 0.26, 0.0)))))
SIPHON = [FRONT + Vector((0, 0.2, 0.02)), FRONT + Vector((0, 0.4, 0.05)), FRONT + Vector((0, 0.58, 0.07)), FRONT + Vector((0, 0.74, 0.08))]
for k in range(3):
    bones.append(Bone(f"siphon_0{k + 1}", "head" if k == 0 else f"siphon_0{k}", tuple(SIPHON[k]), tuple(SIPHON[k + 1])))
LEGS = []
for s, sx in (("r", 1), ("l", -1)):
    bones.append(Bone(f"feeler_{s}", "head", (sx * 0.05, FRONT.y + 0.16, Z0 + 0.06), (sx * 0.32, FRONT.y + 0.5, Z0 + 0.3)))
    for k in range(N):
        seg = f"seg_{k + 1:02d}"
        y = TAIL + (k + 0.55) * SEG
        if k == N - 1:
            # The forelegs: long, raptorial, held folded up in front of it; the hook's what pulls the cord.
            hip, knee, hook = (sx * 0.1, y + 0.05, Z0 + 0.02), (sx * 0.34, y + 0.2, Z0 + 0.34), (sx * 0.22, y + 0.62, Z0 + 0.05)
        else:
            hip, knee, hook = (sx * 0.1, y, Z0 - 0.02), (sx * 0.34, y + 0.02, Z0 + 0.16), (sx * 0.5, y - 0.02, 0.0)
        bones.append(Bone(f"femur_{k + 1:02d}{s}", seg, hip, knee))
        bones.append(Bone(f"tibia_{k + 1:02d}{s}", f"femur_{k + 1:02d}{s}", knee, hook))
        LEGS.append((k + 1, s))
sk = Skeleton("SK_Whistler", bones)
sk.build()
kit = rig.Kit(sk, "whistler")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


FLESH = Mat("skin.whistler", hexc("#b8ab9e"), shine=0.25)
PLATE = Mat("skin.whistler_plate", hexc("#988c7c"), shine=0.4)
LEG = Mat("skin.whistler_leg", hexc("#6c6157"), shine=0.3)
HOOK = Mat("tar.whistler_claw", hexc("#1d1915"), shine=0.5)
LIP = Mat("skin.whistler_lip", hexc("#7c4c45"), shine=0.4)
MOUTH = Mat("tar.whistler_mouth", hexc("#140a0a"), shine=0.6)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


SEGS = [f"seg_{k + 1:02d}" for k in range(N)]

# --- the body: segments, each swelling between pinched joints, an ivory plate over its back and its sides --------------
body = kit.part("body")
pts, radii = [], []
STEPS = 5
pts.append(Vector((0, TAIL - 0.14, Z0 - 0.01)))
radii.append((0.03, 0.025))
for k in range(N):
    for q in range(STEPS):
        f = q / STEPS
        y = TAIL + (k + f) * SEG
        # Thicker through the front third, thinning to the tail.
        g = 0.75 + 0.35 * smooth01(0, 0.7, (k + f) / N)
        swell = 0.82 + 0.18 * math.sin(math.pi * f)
        pts.append(Vector((0, y, Z0)))
        radii.append((0.155 * g * swell, 0.125 * g * swell))
pts.append(FRONT.copy())
radii.append((0.15, 0.12))


def plates(i, j, a, p, fr):
    p = Vector(p)
    side, up, _ = fr
    out = side * math.sin(a) + up * math.cos(a)
    k = (p.y - TAIL) / SEG
    f = k - math.floor(k)
    top = smooth01(-0.35, 0.1, math.cos(a))
    # Each plate's front edge stood up a little over the one before it, its back edge tucked under the next.
    d = top * (0.012 * smooth01(0.0, 0.25, f) - 0.016 * smooth01(0.75, 1.0, f))
    return p + out * (d + 0.003 * noise3(p * 16, 141, 1.0))


def plate_or_flesh(face, n):
    c = sum(face, Vector()) / len(face)
    k = (c.y - TAIL) / SEG
    f = k - math.floor(k)
    under = n.z < -0.25
    joint = f > 0.86 or f < 0.04
    return FLESH if under or joint or c.y < TAIL else PLATE


body.tube(pts, radii, 18, PLATE, (SEGS + ["head"], 5.0), ref=(0, 0, 1), shape=plates, cap0=True, fmat=plate_or_flesh)

# --- the head: a smooth blunt wedge, eyeless; the siphon out of it; the feelers ----------------------------------------
head = kit.part("head")
HC = FRONT + Vector((0, 0.1, -0.005))


def wedge(i, j, a, th, p):
    p = Vector(p)
    d = p - HC
    # Flattened to a shovel at the front, a ridge down the middle of its top.
    s = smooth01(-0.05, 0.16, d.y)
    p.z -= d.z * 0.45 * s
    p.x *= 1.0 - 0.15 * s
    p.z += 0.012 * bell(d.x / 0.025) * (d.z > 0)
    return p + Vector((0, 0, 0.002 * noise3(p * 30, 142, 1.0)))


head.blob(HC, (0.14, 0.18, 0.11), 18, 12, PLATE, {"head": 1.0}, shape=wedge)
# The siphon: a fleshy tube, thick at the root, ending in a puckered lip; the stops along its top.
sph = [SIPHON[0] - Vector((0, 0.06, 0.01))] + [SIPHON[k].lerp(SIPHON[k + 1], f) for k in range(3) for f in (0.0, 0.5)] + [SIPHON[-1]]
sr = [0.05, 0.048, 0.044, 0.04, 0.037, 0.035, 0.034, 0.036]


def siphon_shape(i, j, a, p, fr):
    p = Vector(p)
    side, up, _ = fr
    out = side * math.sin(a) + up * math.cos(a)
    # Wrinkled across like a throat.
    return p + out * 0.003 * math.sin(p.y * 160)


head.tube(sph, sr, 12, FLESH, (["head", "siphon_01", "siphon_02", "siphon_03"], 6.0), ref=(0, 0, 1), shape=siphon_shape)
END = SIPHON[-1]
ring = []
for k in range(14):
    t = 2 * math.pi * k / 14
    ring.append(END + Vector((math.cos(t) * 0.04, 0.004 * math.sin(t * 7), math.sin(t) * 0.04)))
head.tube(ring, [0.012] * 14, 6, LIP, "siphon_03", ref=[Vector((0, 1, 0))] * 14, loop=True)
head.blob(END - Vector((0, 0.008, 0)), (0.03, 0.012, 0.03), 10, 5, MOUTH, "siphon_03")
STOPS = []
for k, f in enumerate((0.2, 0.42, 0.64, 0.84)):
    seg = min(2, int(f * 3))
    c = SIPHON[0].lerp(SIPHON[-1], f)
    r = sr[1 + int(f * 6)] if 1 + int(f * 6) < len(sr) else 0.035
    top = c + Vector((0, 0, r - 0.004))
    head.blob(top, (0.013, 0.016, 0.006), 8, 4, MOUTH, f"siphon_0{seg + 1}")
    ring = [top + Vector((math.cos(2 * math.pi * q / 10) * 0.017, math.sin(2 * math.pi * q / 10) * 0.02, 0.002)) for q in range(10)]
    head.tube(ring, [0.0045] * 10, 4, LIP, f"siphon_0{seg + 1}", ref=[Vector((0, 0, 1))] * 10, loop=True)
for s in ("r", "l"):
    f0, f1 = H(f"feeler_{s}"), T(f"feeler_{s}")
    head.tube([f0, f0.lerp(f1, 0.5) + Vector((0, 0, 0.04)), f1], [0.01, 0.006, 0.0015], 5, LEG, f"feeler_{s}", ref=(0, 0, 1))

# --- the legs: thin, jointed, darker; each tibia ends in a black hook -------------------------------------------------
legs = kit.part("legs")
for k, s in LEGS:
    fe, ti = f"femur_{k:02d}{s}", f"tibia_{k:02d}{s}"
    big = 1.4 if k == N else 1.0
    legs.tube([H(fe), H(fe).lerp(T(fe), 0.5), T(fe)], [0.026 * big, 0.021 * big, 0.018 * big], 6, LEG, fe, ref=(0, 0, 1))
    legs.blob(T(fe), (0.022 * big, 0.022 * big, 0.022 * big), 6, 4, LEG, {fe: 0.5, ti: 0.5})
    hook = T(ti)
    pre = H(ti).lerp(hook, 0.8)
    legs.tube([H(ti), H(ti).lerp(hook, 0.5), pre, hook], [0.017 * big, 0.013 * big, 0.009 * big, 0.002], 6, LEG, ti, ref=(0, 0, 1),
              fmat=lambda pts_, n, pre=pre, hook=hook: HOOK if (sum(pts_, Vector()) / len(pts_) - hook).length < (pre - hook).length * 0.9 else LEG)
    if k == N:
        # The forelegs' inner edge serrated: a row of spines to hold what it carries.
        a, b = H(ti), T(ti)
        for q in range(5):
            c = a.lerp(b, 0.25 + 0.13 * q)
            legs.tube([c, c + Vector((0, 0, -0.04)) + (b - a).normalized() * 0.01], [0.006, 0.0008], 4, HOOK, ti, ref=(0, 1, 0))


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): a segment turned by +Z bends the body ahead of it to its left (-X), by
# +X lifts it. A leg (out along +X on the right) swings forward by +Z, lifts by -Y; mirror() gives the left.
def legs_pose(swing=None, lift=None, tuck=0.0):
    """Every walking leg: `swing(k)` / `lift(k)` (degrees) per segment k (1..N-1), `tuck` 0..1 the legs folded in
    against the body (coiled, held)."""
    p = {}
    for k in range(1, N):
        sw = swing(k) if swing else 0.0
        li = lift(k) if lift else 0.0
        p[f"femur_{k:02d}r"] = (0, -li - 30 * tuck, sw - 50 * tuck)
        p[f"tibia_{k:02d}r"] = (0, 40 * tuck + 0.4 * li, 0)
    return mirror(p)


def centred(p, z=0.0):
    """root@loc set so the body's middle is over the origin (the gap's middle)."""
    p = dict(p)
    p["root@loc"] = (0, 0, 0)
    pts = rig.pose_points(sk, p, [(n, "head") for n in SEGS] + [("head", "tail")])
    cx = sum(q.x for q in pts) / len(pts)
    cy = sum(q.y for q in pts) / len(pts)
    p["root@loc"] = (-cx, -cy, z)
    return p


def lowest(p):
    pts = rig.pose_points(sk, p, [(f"tibia_{k:02d}{s}", "tail") for k, s in LEGS] + [(n, "head") for n in SEGS])
    return min(q.z for q in pts)


def grounded(p):
    """Its lowest hook or segment on the ground."""
    p = dict(p)
    x, y, z = p.get("root@loc", (0, 0, 0))
    lo = lowest(p) - Z0 * 0.0
    p["root@loc"] = (x, y, z - lo)
    return p


def coil(turn0=30.0, grow=2.2, rise=0.0):
    """The body wound round on itself: each segment turned on from the one before, tighter towards the front."""
    return {SEGS[k]: (rise * (k > 5), 0, turn0 + grow * k) for k in range(N)}


FOREFOLD = mirror({f"femur_{N:02d}r": (0, 14, -24), f"tibia_{N:02d}r": (0, 64, 0)})

# Fold (5 s, loop): coiled round the drawgear under the plate, the front laid up over the coil looking out over it, the
# siphon resting along it; breathing (the coil easing and tightening); once, a ripple down the legs from front to tail,
# and the feelers flick.
FOLD_BODY = coil(34, 2.6) | {SEGS[N - 2]: (16, 0, 30), SEGS[N - 1]: (14, 0, 26), "head": (-28, 0, 0), "siphon_01": (-8, 0, 0)}
FOLD = grounded(centred(FOLD_BODY | legs_pose(tuck=0.8) | FOREFOLD))
fold = Clip("fold")
for f, k in ((0, 0.0), (40, 1.0), (80, 0.0), (120, 1.0)):
    fold.key(f, grounded(centred(over(FOLD_BODY | legs_pose(tuck=0.8 - 0.1 * k) | FOREFOLD, seg_05=(0, 0, 34 + 2.6 * 4 - 3 * k)))), "BEZIER")
for f, w in ((126, 0), (128, 1), (130, 2), (132, 3), (134, 4), (136, 5), (140, -9)):
    rip = legs_pose(lift=lambda k, w=w: 35 * bell((N - k - w * 2) / 1.5), tuck=0.8)
    flick = {"feeler_r": (0, -20 if w == 1 else 0, 20 if w == 2 else 0), "feeler_l": (0, 20 if w == 3 else 0, -20 if w == 1 else 0)}
    fold.key(f, FOLD | rip | flick, "CONSTANT")
fold.close(150)

# Whistle (2.5 s, loop for as long as it blows): reared straight up out of the gap, the tail coiled on the rail under it,
# the forelegs up over the cord along the eaves and yanking it down, twice, hard; the siphon pointed up at the sky, its
# root swelling and the stops opening as it blows; the legs down its upright length all stirring.
REAR = coil(40, 6)
REAR = {n: v for n, v in REAR.items() if int(n[-2:]) <= 4} | {"seg_05": (52, 0, 4), "seg_06": (38, 0, 0), "seg_07": (4, 0, 0), "seg_08": (0, 0, 0),
                                                             "seg_09": (-2, 0, 0), "seg_10": (-6, 0, 0), "head": (-10, 0, 0),
                                                             "siphon_01": (30, 0, 0), "siphon_02": (16, 0, 0), "siphon_03": (8, 0, 0)}


def uplegs(ph):
    """Down its upright length the legs hang curled in towards it, stirring out of step, each on its own."""
    return legs_pose(swing=lambda k: (-10 + 22 * math.sin(ph + k * 1.7)) if k >= 5 else 0,
                     lift=lambda k: (-35 + 18 * math.sin(ph * 1.3 + k * 2.3)) if k >= 5 else 0, tuck=0.35)


UPLEGS = uplegs(0.0)


def reach_up(p, pull):
    """The forelegs up over its head, hooked over the cord, pulled down `pull` (0..1)."""
    return p | mirror({f"femur_{N:02d}r": (0, -60 + 40 * pull, 50), f"tibia_{N:02d}r": (0, -40 + 50 * pull, 0)})


whistle = Clip("whistle")
for f, pull, interp in ((0, 0.0, "CONSTANT"), (3, 0.0, "LINEAR"), (10, 1.0, "CONSTANT"), (16, 0.0, "LINEAR"), (22, 1.0, "CONSTANT"),
                        (34, 1.0, "LINEAR"), (44, 0.0, "LINEAR"), (60, 0.0, "CONSTANT")):
    blow = 1.0 if 8 <= f <= 40 else 0.0
    sway = math.sin(f / 75 * 2 * math.pi)
    p = reach_up(over(REAR, seg_07=(4 + 6 * pull, 0, 5 * sway), seg_08=(0, 0, -7 * sway), seg_09=(-2 - 8 * pull, 0, 4 * sway)) |
                 uplegs(f / 75 * 2 * math.pi * 2), pull)
    p = p | {"siphon_01@scale": (1 + 0.35 * blow, 1, 1 + 0.35 * blow), "siphon_03@scale": (1 + 0.2 * blow, 1, 1 + 0.2 * blow),
             "feeler_r": (0, -30 * blow, 10), "feeler_l": (0, 30 * blow, -10)}
    whistle.key(f, grounded(centred(p)), interp)
whistle.close(75)

# Watch (4 s, loop): coiled at the gap's mouth, the front third lifted and held out, swaying a little, stopping dead;
# turning in jerks; the forelegs folded under it ready; the feelers out ahead.
WATCH_BODY = coil(36, 3.0)
WATCH_BODY = {n: v for n, v in WATCH_BODY.items() if int(n[-2:]) <= 7} | {"seg_08": (34, 0, 10), "seg_09": (6, 0, 0), "seg_10": (-10, 0, 0),
                                                                          "head": (-20, 0, 0), "siphon_01": (-6, 0, 0)}
WATCH = grounded(centred(WATCH_BODY | legs_pose(tuck=0.5) | FOREFOLD))
watch = Clip("watch")
for f, turn in ((0, 0), (30, 26), (52, 26), (54, -24), (86, -24), (88, 6), (120, 6)):
    watch.key(f, grounded(centred(over(WATCH_BODY, seg_09=(6, 0, turn * 0.5), seg_10=(-10, 0, turn * 0.6), head=(-20, 0, turn * 0.4)) | legs_pose(tuck=0.5) |
                                  FOREFOLD | {"feeler_r": (0, -10, turn * 0.3), "feeler_l": (0, 10, turn * 0.3)})), "CONSTANT")
watch.close(120)

# Run (0.6 s, loop): straightened out and gone, the body snaking side to side in a wave running back down it, the legs
# rippling in waves, a pair to each wave, too fast; the front held up off the ground, the forelegs clutched shut round
# what it carries.
run = Clip("run")
for f in range(0, 18, 3):
    ph = 2 * math.pi * f / 18
    snake = {SEGS[k]: (0, 0, 12 * math.sin(ph - k * 0.9)) for k in range(N - 2)} | {"seg_09": (16, 0, 0), "seg_10": (-10, 0, 0), "head": (-8, 0, 0)}
    lp = {}
    for k in range(1, N):
        u = ph - k * 1.1
        lp[f"femur_{k:02d}r"] = (0, -30 * max(0.0, math.sin(u)), 30 * math.cos(u))
        lp[f"tibia_{k:02d}r"] = (0, 10 * max(0.0, math.sin(u)), 0)
        lp[f"femur_{k:02d}l"] = (0, 30 * max(0.0, math.sin(u + math.pi)), -30 * math.cos(u + math.pi))
        lp[f"tibia_{k:02d}l"] = (0, -10 * max(0.0, math.sin(u + math.pi)), 0)
    clutch = mirror({f"femur_{N:02d}r": (0, 10, -30), f"tibia_{N:02d}r": (0, 50, 0)})
    run.key(f, grounded(centred(snake | lp | clutch)), "LINEAR")
run.close(18)

# Hit (0.4 s, once): struck, it whips into an S round the blow, the legs all flung out, and snaps back.
hit = Clip("hit", loop=False)
hit.key(0, WATCH, "CONSTANT")
hit.key(2, grounded(centred(over(WATCH_BODY, seg_06=(10, 0, 50), seg_07=(0, 0, -40), seg_08=(40, 0, -30), head=(20, 0, 30)) | legs_pose(lift=lambda k: 40))), "CONSTANT")
hit.key(7, grounded(centred(over(WATCH_BODY, seg_07=(0, 0, 10)) | legs_pose(tuck=0.3) | FOREFOLD)), "LINEAR")
hit.key(12, WATCH, "CONSTANT")

kit.build()
rig.bake(sk, [fold, whistle, watch, run, hit])
for name, p in (("fold", FOLD), ("watch", WATCH), ("whistle", grounded(centred(reach_up(REAR | UPLEGS, 0.0))))):
    pts = rig.pose_points(sk, p, [(b.name, "tail") for b in sk.bones] + [(b.name, "head") for b in sk.bones])
    print(f"[dt] whistler {name} top {max(q.z for q in pts):.2f} low {min(q.z for q in pts):.2f}")
print("[dt] whistler", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "whistler.glb", kit)
