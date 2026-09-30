"""THE CAR HUGGER (GDD v1.2 §21 rear, App. A.3 · vibration): "Clamps onto the rear car and eats it, shell and loot. Caps
your top speed while attached." Anyone in front of its mouth can be swallowed; it's fought from the rear platform.

It lurks on low ground by the line and comes up onto the last car as it passes: a bloated, eyeless, waxy grey-violet
thing ringed like a maggot, hugging the car's rear end, its head down on the rear platform and its mouth over the end
door (open it and the doorway is its mouth), its bulk sagging off the back of the platform and trailing on the track
(it's what drags the train). Four long arms wrap round
the car's rear corners, the fingers dug into the side walls. The mouth is a lamprey's: a ring of lips, a funnel, three
rings of teeth that turn against each other as it eats (the heavy grinding from the rear that's its tell). What it
has eaten before shows through its hide: rusted plate, rivets, a wheel's flange. tools/models/recipes/car_hugger.py
models its high copy and bakes it.

Frame (the sim's Local when latched): the origin 1.0 m above the rail and 0.4 m behind the rear car's end wall, the
car ahead (+Y here, -Z in the engine), its rear platform's deck 0.1 m above the origin, its sides at x = ±1.5, its roof
at 3.0 above, the rail 1.0 below. SK_Chain: root, a spine of four (tail, belly, chest, head), the mouth (scaled to
open it), three teeth rings (spun about the mouth's axis), and four arms of arm_01, arm_02, hand, grip.
Clips: lurk (on the ground by the line, breathing), latch (up onto the car and clamped, once), feed (eating: heaves,
the teeth grinding, loop), swallow (the mouth wide over someone on the platform, loop), release (it lets go and
slides off, once), hit (struck, once).

    tools/models/build.sh car_hugger          # this, its high copy and the bake -> content/art/models/car_hugger.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, noise3, over  # noqa: E402

rig.reset()
DECK = 0.1       # the rear platform's deck
SIDE = 1.5       # the car's sides
RAIL = -1.0      # the rail's top
WALL = 0.4       # the car's end wall
DOOR_X = -0.45    # the end door's line (content/tuning/train.json interior.doorX): the mouth's over the doorway
MOUTH = Vector((DOOR_X, 0.3, 0.66))   # the mouth's centre: its lower lip on the deck, against the end door
MOUTH_R = 0.52


def shift(y):
    """How far over to the door's side the body is at `y`: all the way at the head, easing back to the car's middle by
    the belly (the arms' grips are the car's corners either way)."""
    t = min(1.0, max(0.0, (y + 1.4) / 1.3))
    return DOOR_X * t * t * (3 - 2 * t)


# The arms: (name, shoulder, elbow, wrist, grip direction). The upper pair round the car's rear corners and along its
# sides at the height of a man's head; the lower pair round under them at the floor's sill. (The shoulders go with
# the body, over to the door's side.)
ARMS = [
    ("a", (0.7 + shift(-0.8), -0.8, 1.35), (1.62, -0.1, 2.2), (1.66, 1.15, 2.72), (-0.1, 0.25, 1.0)),
    ("b", (-0.7 + shift(-0.8), -0.8, 1.35), (-1.62, -0.1, 2.2), (-1.66, 1.15, 2.72), (0.1, 0.25, 1.0)),
    ("c", (0.85 + shift(-1.05), -1.05, 0.55), (1.62, -0.35, 0.4), (1.68, 1.0, 1.05), (-0.25, 1.0, 0.15)),
    ("d", (-0.85 + shift(-1.05), -1.05, 0.55), (-1.62, -0.35, 0.4), (-1.68, 1.0, 1.05), (0.25, 1.0, 0.15)),
]
ROOF = 3.0
TEETH = [(0.2, 0.43, 22, 0.095), (0.06, 0.33, 16, 0.085), (-0.08, 0.22, 11, 0.07)]  # (y, radius, count, length)

bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0)),
         Bone("body_03", "root", (0, -2.3, -0.3), (0, -1.5, 0.2)),
         Bone("body_02", "body_03", (0, -1.5, 0.2), (shift(-0.8), -0.8, 0.6)),
         Bone("body_01", "body_02", (shift(-0.8), -0.8, 0.6), (MOUTH.x, 0.1, 0.66)),
         Bone("body_04", "body_03", (0, -2.3, -0.3), (0, -3.7, -0.8)),
         Bone("mouth", "body_01", (MOUTH.x, 0.1, MOUTH.z), (MOUTH.x, 0.35, MOUTH.z))]
for k in range(3):
    y = TEETH[k][0]
    bones.append(Bone(f"teeth_{k + 1}", "mouth", (MOUTH.x, y - 0.1, MOUTH.z), (MOUTH.x, y + 0.05, MOUTH.z)))
for n, s, e, w, g in ARMS:
    s, e, w, g = Vector(s), Vector(e), Vector(w), Vector(g).normalized()
    k = w + g * 0.24
    parent = "body_02" if n in "ab" else "body_03"
    bones += [Bone(f"arm_{n}_01", parent, s, e), Bone(f"arm_{n}_02", f"arm_{n}_01", e, w),
              Bone(f"hand_{n}", f"arm_{n}_02", w, k), Bone(f"grip_{n}", f"hand_{n}", k, k + g * 0.26)]
sk = Skeleton("SK_Chain", bones)
sk.build()
kit = rig.Kit(sk, "car_hugger")

# Waxy, bruised grey-violet (GDD §28's corruption accents: bruised violet, dead ivory), wet; the mouth's inside
# red-black; the teeth dead ivory; the eaten iron rusted.
HIDE = Mat("flesh.hugger", hexc("#8a7f86"), shine=0.5, tint=(1.1, 1.0, 1.05))
PALE = Mat("flesh.hugger_pale", hexc("#a39a95"), shine=0.5, tint=(1.2, 1.12, 1.08))
GUM = Mat("flesh.hugger_gum", hexc("#5a2a2c"), shine=0.6, tint=(0.9, 0.5, 0.5))
THROAT = Mat("tar.throat", hexc("#120808"), shine=0.6)
TOOTH = Mat("skin.tooth", hexc("#d8ceb8"), shine=0.4)
IRON = Mat("rust_heavy.eaten", hexc("#5a3a2a"), shine=0.2)
BODY = (["body_01", "body_02", "body_03", "body_04"], 4.0)


def bell(x):
    return math.exp(-x * x)


# ----------------------------------------------------------------------------------------------------------------
# The body: from the mouth's rim back over the platform, the shoulders' hump, the belly sagging off the back of the
# platform, the tail dragged on the track.
body = kit.part("body")
SECS = [(0.3, 0.57, MOUTH.z + 0.58, DECK + 0.02, 0.9), (0.02, 0.68, MOUTH.z + 0.74, DECK, 0.85),
        (-0.45, 0.83, 1.62, DECK + 0.02, 0.8), (-0.95, 0.96, 1.68, 0.0, 0.8), (-1.5, 1.06, 1.38, -0.5, 0.8),
        (-2.15, 0.97, 0.8, -0.9, 0.8), (-2.85, 0.74, 0.22, -0.95, 0.8), (-3.45, 0.44, -0.33, -0.96, 0.85),
        (-3.82, 0.16, -0.68, -0.96, 0.9)]


def section_at(y):
    """(half width, top, bottom, squareness) of the body at `y`: a smooth (Catmull-Rom) run through the sections, so
    the hide's rings don't show a kink at every one."""
    pts = sorted(SECS)
    if y <= pts[0][0] or y >= pts[-1][0]:
        return (pts[0] if y <= pts[0][0] else pts[-1])[1:]
    k = next(i for i in range(len(pts) - 1) if pts[i][0] <= y <= pts[i + 1][0])
    p0, p1, p2, p3 = pts[max(k - 1, 0)], pts[k], pts[k + 1], pts[min(k + 2, len(pts) - 1)]
    t = (y - p1[0]) / (p2[0] - p1[0])
    t2, t3 = t * t, t * t * t
    span = p2[0] - p1[0]

    def slope(a, b, u):
        return (b[u] - a[u]) / (b[0] - a[0]) * span if b[0] != a[0] else 0.0

    out = []
    for u in range(1, 5):
        m1, m2 = slope(p0, p2, u), slope(p1, p3, u)
        out.append((2 * t3 - 3 * t2 + 1) * p1[u] + (t3 - 2 * t2 + t) * m1 + (-2 * t3 + 3 * t2) * p2[u] + (t3 - t2) * m2)
    return tuple(out)


def centre_z(y):
    """The body's centre line's height at `y`."""
    _, top, bot, _ = section_at(y)
    return (top + bot) / 2


def on_body(y, angle, lift=0.0):
    """A point on the body's hide at `y`, `angle` round it from the top (degrees, + to its right), `lift` off it, and the
    hide's normal there."""
    hw, top, bot, sq = section_at(y)
    zc, hz = (top + bot) / 2, (top - bot) / 2
    a = math.radians(angle)
    sa, ca = math.sin(a), math.cos(a)
    sa, ca = math.copysign(abs(sa) ** sq, sa), math.copysign(abs(ca) ** sq, ca)
    p = Vector((sa * hw + shift(y), y, zc + ca * hz))
    n = Vector((sa / max(hw, 1e-3), 0, ca / max(hz, 1e-3))).normalized()
    return p + n * lift, n


# Behind the chest it's ringed like a maggot: a crease every 0.3-0.42 m to the tail, some deep, some barely there, the
# hide bulging between them. The rings are laid on the creases, either side of them and between, so each one takes.
RING_FROM, RING_TO = -0.4, -3.55
CREASES, DEPTHS = [RING_FROM], [1.0]
while True:
    k = len(CREASES)
    step = 0.36 * (1 + 0.17 * math.sin(k * 2.3 + 0.5))
    if CREASES[-1] - step < RING_TO:
        break
    CREASES.append(CREASES[-1] - step)
    DEPTHS.append(1.0 + 0.4 * math.sin(k * 1.7 + 1.1))


def ringed(y, q=None):
    """(crease 0..1.4, bulge 0..1) at `y` (a little wobbled round the body by `q`, so no crease is a perfect ring)."""
    if not CREASES[-1] - 0.2 <= y <= RING_FROM:
        return 0.0, 0.0
    if q is not None:
        y += 0.035 * noise3(q, 44, 1.8)
    k = min(range(len(CREASES)), key=lambda i: abs(CREASES[i] - y))
    crease = DEPTHS[k] * bell((y - CREASES[k]) / 0.035)
    lo = next((c for c in CREASES if c < y), None)
    hi = next((c for c in reversed(CREASES) if c >= y), None)
    bulge = math.sin(math.pi * (hi - y) / (hi - lo)) if lo is not None and hi is not None else 0.0
    ends = min(1.0, (RING_FROM - y) / 0.3, (y - RING_TO) / 0.3) if RING_TO < y else 0.0
    return max(0.0, ends) * crease, max(0.0, ends) * bulge


def lumps(i, j, a, p):
    """The hide swollen and slack: lumps (none at the mouth's rim, where the lips sit), the maggot's rings, folds
    hanging from the belly where it sags."""
    q = Vector(p)
    d = q - Vector((0, q.y, centre_z(q.y)))
    out = d.normalized() if d.length > 1e-6 else Vector((0, 0, 1))
    rim = min(1.0, max(0.0, (0.25 - q.y) / 0.3))
    q += out * rim * (0.05 * noise3(q, 41, 2.2) + 0.02 * noise3(q, 42, 6.0))
    crease, bulge = ringed(q.y, q)
    under = max(0.35, min(1.0, 0.7 + out.z))   # shallower underneath, where its weight flattens them
    q += out * d.length * under * (0.02 * bulge - 0.085 * crease)
    # The belly's folds, hanging.
    if -2.6 < q.y < -1.0 and q.z < 0.2:
        q.z -= 0.03 * max(0.0, math.sin(q.y * 9 + noise3(q, 43, 3.0)))
    q.z = max(q.z, RAIL + 0.04)
    q.x += shift(q.y)
    return q


ALONG = [0.3, 0.2, 0.1, 0.02, -0.1, -0.22, -0.34]
for c0, c1 in zip(CREASES, CREASES[1:]):
    ALONG += [c0, c0 - 0.055, (c0 + c1) / 2, c1 + 0.055]
ALONG += [CREASES[-1], CREASES[-1] - 0.055] + [y for y in (-3.62, -3.7, -3.76) if y < CREASES[-1] - 0.1]
FINE = [(y, *section_at(y)) for y in ALONG] + [SECS[-1]]
body.sections(FINE, 32, HIDE, BODY, axis="y", cap1="point", shape=lumps)
# The spine standing through the hide down its back, a knuckle to each ring; humps where the arms come out of it; a
# collar of slack flesh behind the mouth's rim.
for k, (c0, c1) in enumerate(zip(CREASES, CREASES[1:])):
    y = (c0 + c1) / 2   # one to each ring, where it bulges
    at, n = on_body(y, 0, -0.01)
    r = 0.11 * (1 - k / 14)
    R = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
    body.blob(at, (r * 1.1, r * 0.8, r * 0.9), 8, 4, HIDE, BODY, rot=R)
for sx in (-1, 1):
    for y, ang in ((-0.85, 48), (-1.1, 95)):
        at, n = on_body(y, sx * ang, -0.1)
        R = n.to_track_quat("Z", "Y").to_matrix().to_4x4()
        body.blob(at, (0.28, 0.32, 0.2), 10, 5, HIDE, BODY, rot=R)
collar = [MOUTH + Vector((math.sin(2 * math.pi * j / 20) * 0.66, -0.12 - 0.03 * math.sin(j * 2.3), math.cos(2 * math.pi * j / 20) * 0.64))
          for j in range(20)]
body.tube(collar, [(0.1, 0.08)] * 20, 8, HIDE, ["body_01"], ref=(0, 1, 0), loop=True,
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.02 * noise3(Vector(p), 47, 7.0))

# ----------------------------------------------------------------------------------------------------------------
# The mouth: a ring of lips round a funnel of gum, three rings of teeth down it, and the throat.
mouth = kit.part("mouth")
AROUND = 26
ring = [MOUTH + Vector((math.sin(2 * math.pi * j / AROUND) * MOUTH_R, 0.03, math.cos(2 * math.pi * j / AROUND) * MOUTH_R))
        for j in range(AROUND)]
mouth.tube(ring, [(0.085, 0.07)] * AROUND, 8, PALE, "mouth", ref=(0, 1, 0), loop=True,
           shape=lambda i, j, a, p, fr: Vector(p) + fr[0] * 0.012 * math.sin(i * 1.7))


def funnel_ring(y, r, wobble=0.0):
    return [MOUTH + Vector((math.sin(2 * math.pi * j / AROUND) * r * (1 + wobble * math.sin(j * 1.3)), y - MOUTH.y,
                            math.cos(2 * math.pi * j / AROUND) * r * (1 + wobble * math.sin(j * 1.3)))) for j in range(AROUND)]


funnel = [funnel_ring(0.32, MOUTH_R - 0.02), funnel_ring(0.24, 0.46, 0.04), funnel_ring(0.1, 0.36, 0.05),
          funnel_ring(-0.05, 0.25, 0.05), funnel_ring(-0.22, 0.14), funnel_ring(-0.42, 0.07)]
# (Its visible face is the inside: turned towards a point far behind down the axis, the way the funnel narrows.)
mouth.loft(funnel, GUM, "mouth", centres=[MOUTH + Vector((0, r[0].y - MOUTH.y, 0)) for r in funnel],
           inside=MOUTH + Vector((0, -6.0, 0)))
mouth.blob(MOUTH + Vector((0, -0.46, 0)), (0.09, 0.05, 0.09), 8, 4, THROAT, "mouth")
for k, (y, r, count, length) in enumerate(TEETH):
    for j in range(count):
        a = 2 * math.pi * (j + 0.5 * k) / count
        radial = Vector((math.sin(a), 0, math.cos(a)))
        base = MOUTH + Vector((0, y - MOUTH.y, 0)) + radial * r
        # Hooked in towards the throat, a little crooked each.
        tip = base - radial * length * 0.8 - Vector((0, length * 0.55, 0)) + Vector((0, 0, 0.01 * noise3(base, 61 + k, 9.0)))
        mouth.tube([base + radial * 0.01, base.lerp(tip, 0.5), tip], [0.022 + 0.006 * (2 - k), 0.014, 0.004], 5, TOOTH,
                   f"teeth_{k + 1}", ref=(0, 1, 0), cap0=True, cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# The arms: long, thin for their length, the elbows standing out, the hands broad with long fingers dug into the steel.
arms = kit.part("arms")
for n, s, e, w, g in ARMS:
    s, e, w, g = Vector(s), Vector(e), Vector(w), Vector(g).normalized()
    chain = [f"arm_{n}_01", f"arm_{n}_02", f"hand_{n}"]
    upper = n in "ab"
    r0 = 0.19 if upper else 0.17
    # Muscled: the upper arm thick at the shoulder, the forearm swelling below the elbow's knob, the wrist thin.
    path = [s - (e - s).normalized() * 0.2, s, s.lerp(e, 0.35), s.lerp(e, 0.75), e, e.lerp(w, 0.25), e.lerp(w, 0.6), w]
    arms.tube(path, [r0 * 1.35, r0, r0 * 0.95, r0 * 0.72, (r0 * 0.78, r0 * 0.7), r0 * 0.75, r0 * 0.58, (r0 * 0.42, r0 * 0.34)], 12,
              HIDE, (chain, 6.0), ref=(0, 0, 1), cap0=True,
              shape=lambda i, j, a, p, fr, n=n: Vector(p) + fr[0] * 0.02 * noise3(Vector(p), 70 + ord(n), 7.0))
    # The elbow's knob, a bone standing out behind the joint rather than a ball on it.
    arms.blob(e - (w - e).normalized() * 0.03, (0.065, 0.065, 0.065), 8, 5, HIDE, [f"arm_{n}_02"])
    # The hand flat to the car's side, the palm towards it; four long fingers on along the steel (the upper hands' up
    # over the roof's edge and down onto it), their tips dug in.
    wall = Vector((-math.copysign(1, s.x), 0, 0))
    k = w + g * 0.24
    across = g.cross(wall).normalized()
    arms.tube([w, w.lerp(k, 0.45), k], [(0.075, 0.05), (0.11, 0.05), (0.11, 0.042)], 10, PALE, [f"hand_{n}"], ref=tuple(wall), square=0.9)
    for f in range(4):
        o = across * (f - 1.5) * 0.062
        a0 = k + o
        a1 = a0 + g * 0.17
        if upper:
            # Over the edge: up past it, then in across the roof.
            a2 = Vector((a1.x + wall.x * 0.12, a1.y + 0.02, ROOF + 0.06))
            a3 = a2 + wall * 0.14 - Vector((0, 0, 0.04))
        else:
            a2 = a1 + (g * 0.9 + wall * 0.3).normalized() * 0.15
            a3 = a2 + (g * 0.4 + wall * 0.9).normalized() * 0.1
        arms.tube([a0, a1, a2, a3], [0.036, 0.031, 0.025, 0.01], 6, PALE, ([f"hand_{n}", f"grip_{n}"], 6.0), ref=tuple(wall), cap1="point")
    th = w + g * 0.08 - across * 0.14
    arms.tube([th, th + (g - across * 0.6).normalized() * 0.14, th + (g - across * 0.4 + wall * 0.4).normalized() * 0.25],
              [0.036, 0.026, 0.009], 6, PALE, f"grip_{n}", ref=(0, 0, 1), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# What it's eaten: rusted plate sunk in the hump and the flanks, a wheel's flange half out of the belly, rivets.
iron = kit.part("iron")
# (Small enough that the hide's curve round them doesn't leave their edges standing off it like fins.)
PLATES = [(-1.0, 12, (0.2, 0.15), 25), (-1.45, -52, (0.17, 0.21), -35), (-1.95, 95, (0.2, 0.16), 10), (-2.6, -20, (0.22, 0.14), 40)]
for y, ang, (hx, hy), spin in PLATES:
    at, n = on_body(y, ang, -0.01)
    # Pressed into the hide: its plane the hide's, turned about the normal; half sunk.
    R = (n.to_track_quat("Z", "Y") @ rig.rot(0, 0, spin)).to_matrix().to_4x4()
    iron.box(at, (hx, hy, 0.03), IRON, BODY, rot=R, bevel=0.01)
    for s1 in (-1, 1):
        for s2 in (-1, 1):
            loc = Vector((s1 * (hx - 0.045), s2 * (hy - 0.045), 0.035))
            iron.blob(at + R.to_3x3() @ loc, (0.02, 0.02, 0.014), 6, 3, IRON, BODY)
# The flange: an arc of a wheel's rim standing out of its right flank.
fl = [Vector((1.0 + 0.1 * math.cos(t), -1.7 + 0.42 * math.sin(t), -0.25 + 0.42 * math.cos(t))) for t in [math.radians(a) for a in range(-50, 70, 15)]]
iron.tube(fl, [(0.035, 0.07)] * len(fl), 6, IRON, BODY, ref=(1, 0, 0))

# ----------------------------------------------------------------------------------------------------------------
# Clips. The chain's axes (rig.rot): +X tips a bone's far end up, Z turns it (+ to its left). The arms keep their
# hands where they grip (rig.reach per key), so the body heaves between them.
GRIPS = {n: Vector(w) for n, _, _, w, _ in ARMS}


def held(pose, grips=None):
    """`pose` with every arm put back on its grip (or on `grips` where given): the body moves, the hands don't."""
    p = dict(pose)
    for n, *_ in ARMS:
        target = (grips or {}).get(n, GRIPS[n])
        p = rig.reach(sk, p, f"arm_{n}_01", f"arm_{n}_02", target, elbow_axis=0, bend=1)
    return p


def fingers(k):
    return {f"grip_{n}": (-25 * k, 0, 0) for n, *_ in ARMS}


def teeth(turn):
    """The teeth rings turned against each other: the outer one way, the middle the other, the inner twice as fast
    (whole turns at `turn` 1, so a loop that ends there closes)."""
    return {"teeth_1@spin": 360 * turn, "teeth_2@spin": -360 * turn, "teeth_3@spin": 720 * turn}


REST = fingers(1.0)

# Feed: it heaves the whole mouth into the door and back, three uneven chews and a shudder, the teeth turning all
# the while (GDD §31: a monster's too-fast corrections; the heave pops, the release eases).
feed = Clip("feed")
chews = [(0.0, 0.0), (0.07, 0.3), (0.02, 0.1), (0.06, 0.5), (0.0, 0.0), (0.09, 0.8), (0.03, 0.3), (0.0, 0.0)]
for f, (push, k) in enumerate(chews):
    pose = over(REST, root__loc=(0, push, 0.01 * k), body_01=(-3 * k, 0, 2 * math.sin(f)), body_02=(2 * k, 0, 0),
                body_03=(-2 * k, 0, 0), body_04=(4 * k, 0, 3 * math.sin(f * 1.7)))
    pose["mouth@scale"] = (1 - 0.1 * k, 1.0, 1 - 0.12 * k)
    pose |= teeth(f / 8) | fingers(1.0 + 0.3 * k)
    feed.key(f * 6, held(pose), "CONSTANT" if f in (1, 3, 5) else "LINEAR")
last = over(REST)
last["mouth@scale"] = (1.0, 1.0, 1.0)
last |= teeth(1.0) | fingers(1.0)
feed.key(48, held(last), "LINEAR")

# Swallow: the head up off the deck and the mouth dilated wide over whoever's on the platform, gulping.
swallow = Clip("swallow")
for f, k in enumerate((0.0, 1.0, 0.6, 1.0, 0.3, 0.9)):
    pose = over(REST, root__loc=(0, -0.05, 0.08), body_01=(14 + 4 * k, 0, 0), body_02=(-6, 0, 0))
    pose["mouth@scale"] = (1.3 + 0.12 * k, 1.1, 1.35 + 0.12 * k)
    pose |= teeth(f / 6) | fingers(1.3)
    swallow.key(f * 6, held(pose), "CONSTANT" if f % 2 else "BEZIER")
gulp = over(REST, root__loc=(0, -0.05, 0.08), body_01=(14, 0, 0), body_02=(-6, 0, 0))
gulp["mouth@scale"] = (1.3, 1.1, 1.35)
swallow.key(36, held(gulp) | teeth(1.0) | fingers(1.3), "BEZIER")

# Latch: up off the stones with the arms flung wide, onto the car, slapped on and clenched (App. A.3 LATCH; A.1's
# window: it has hold by half a second).
FLUNG = {"a": Vector((2.3, -0.2, 2.4)), "b": Vector((-2.3, -0.2, 2.4)), "c": Vector((2.3, -0.6, 0.2)), "d": Vector((-2.3, -0.6, 0.2))}
low = over(REST, root__loc=(0, -0.9, -0.55), body_01=(-18, 0, 0), body_02=(-10, 0, 0))
low["mouth@scale"] = (0.7, 1.0, 0.7)
latch = Clip("latch", loop=False)
latch.key(0, held(low, FLUNG) | fingers(-0.3), "CONSTANT")
latch.key(5, held(over(low, root__loc=(0, -0.35, -0.2)), FLUNG) | fingers(-0.3), "CONSTANT")
latch.key(9, held(over(REST, root__loc=(0, 0.06, 0.0))) | fingers(0.6), "LINEAR")
latch.key(12, held(over(REST, root__loc=(0, 0.1, 0.02))) | fingers(1.4), "CONSTANT")
latch.key(20, held(REST) | fingers(1.0), "BEZIER")

# Release: the fingers come out of the steel, the arms slide off and it drops back off the platform onto the track.
release = Clip("release", loop=False)
release.key(0, held(REST), "LINEAR")
release.key(6, held(REST) | fingers(-0.4), "CONSTANT")
release.key(12, held(over(REST, root__loc=(0, -0.5, -0.3), body_01=(-12, 0, 0)), FLUNG) | fingers(-0.2), "BEZIER")
release.key(26, held(low, {n: v + Vector((0, -1.5, -1.2)) for n, v in FLUNG.items()}) | fingers(0.2), "BEZIER")

# Hit: struck from the platform: it jerks its head up and back and slams down again.
hit = Clip("hit", loop=False)
hit.key(0, held(REST), "CONSTANT")
hit.key(2, held(over(REST, body_01=(22, 0, 8), body_02=(6, 0, 0), root__loc=(0, -0.12, 0.05))), "CONSTANT")
hit.key(8, held(over(REST, body_01=(-6, 0, -3))), "LINEAR")
hit.key(14, held(REST), "BEZIER")

# Lurk: down in the low ground by the line, off any car: the body lying flat, the arms folded under it, the mouth
# shut, breathing; now and then a hand opens and closes.
DOWN = Vector((0, 0.3, -0.62))
FOLD = {n: v + DOWN for n, v in {"a": Vector((0.9, 0.2, 0.25)), "b": Vector((-0.9, 0.2, 0.25)), "c": Vector((1.0, -0.6, -0.35)),
                                 "d": Vector((-1.0, -0.6, -0.35))}.items()}
flat = over(REST, root__loc=tuple(DOWN), body_01=(-10, 0, 0), body_03=(22, 0, 0), body_04=(-14, 0, 0))
flat["mouth@scale"] = (0.55, 0.9, 0.55)
lurk = Clip("lurk")
for f, k in enumerate((0.0, 1.0, 0.0, 1.0, 0.0)):
    pose = dict(flat)
    pose["body_03@scale"] = (1 + 0.03 * k, 1.0, 1 + 0.04 * k)
    pose |= fingers(0.4 if f != 3 else -0.2)
    lurk.key(f * 20, held(pose, FOLD), "BEZIER" if f != 3 else "CONSTANT")

kit.build()
rig.bake(sk, [lurk, latch, feed, swallow, release, hit])
print("[dt] car_hugger", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(bones))
rig.export(rig.args()[0] if rig.args() else "car_hugger.glb", kit)
