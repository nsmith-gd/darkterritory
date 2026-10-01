"""THE CHOIR (GDD v1.2 §21 drawn by noise, App. A.7 · sound): "Small flying ghosts that come for a loud crew. Long
warning, then the swarm. Takes anyone outside, on the roofs, or behind no door. Killable, barely."

Not children (note 133). Each is a bell of membrane the size of a child's chest, drifting in the air the way a jellyfish
drifts in water: a hood of thin grey-blue skin, veined, pulsing, with a frilled skirt round its rim. It has no eyes. On
the front of the hood, where a face would be, it has one mouth, a child's mouth, the lips soft and grey and the small
blunt milk teeth inside, held open in the round O of singing; that's the voice. Under the bell hang three ruffled
curtains of membrane like a surplice's ruff gone to rags, and round them six long tendrils trailing a metre and more.
Seizing someone it settles on their head like a cap, its tendrils wound round their skull and down their neck, and
sings into their ear. (§26: the corruption palette, not neon: no light of its own but the faint cold of its skin.)

SK_Choir: root, the bell, the mouth, the rim's six flaps, the six tendrils' six bones each and the curtains' three.
Faces +Y (the engine's -Z): the mouth's side. Its origin is where the sim has it (seizing, 1.4 m over its catch's feet:
the catch's head is under the bell). Clips (§31: still, then too fast): drift (pulsing slowly, the tendrils trailing in
waves, the mouth singing; once turned on you in a snap), swoop (tipped into the flight, pulsing fast, the tendrils
streamed back), seize (capped on its catch's head, the tendrils wound round it, shuddering, the mouth at their ear),
besiege (pressed to a shut door, the tendrils lashing at it in bursts), hit.

    tools/models/build.sh choir        # this, its high copy and the bake -> content/art/models/choir.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Quaternion, Skeleton, Vector, hexc, noise3  # noqa: E402

rig.reset()
BC = Vector((0, 0, 1.15))          # the bell's middle
BR = (0.34, 0.34, 0.22)             # its radii
RIM_Z = BC.z - 0.1
RIM_R = 0.34
NT, TB, TL = 6, 6, 0.17              # tendrils, bones each, each bone's length
NA, AB, AL = 3, 3, 0.16             # the curtains
# The mouth on the hood's front, a little under its middle, facing out and a little down.
MN = Vector((0, math.cos(math.radians(6)), -math.sin(math.radians(6))))
MC = BC + Vector((0, BR[1] * MN.y, BR[2] * MN.z)) * 0.98

bones = [Bone("root", None, (0, 0, 0), (0, 0.2, 0)), Bone("bell", "root", tuple(BC - Vector((0, 0, 0.1))), tuple(BC + Vector((0, 0, 0.2))))]
bones.append(Bone("mouth", "bell", tuple(MC - MN * 0.03), tuple(MC + MN * 0.1)))
RIMS = []
for k in range(6):
    a = 2 * math.pi * k / 6
    d = Vector((math.sin(a), math.cos(a), 0))
    bones.append(Bone(f"rim_{k + 1}", "bell", tuple(Vector((0, 0, RIM_Z)) + d * 0.12), tuple(Vector((0, 0, RIM_Z - 0.03)) + d * (RIM_R + 0.04))))
    RIMS.append((f"rim_{k + 1}", a))
TENDRILS = []
for k in range(NT):
    a = 2 * math.pi * (k + 0.5) / NT
    root = Vector((math.sin(a) * RIM_R * 0.92, math.cos(a) * RIM_R * 0.92, RIM_Z - 0.02))
    names = []
    for i in range(TB):
        n = f"tendril_{k + 1}_{i + 1}"
        bones.append(Bone(n, "bell" if i == 0 else names[-1], tuple(root - Vector((0, 0, TL * i))), tuple(root - Vector((0, 0, TL * (i + 1))))))
        names.append(n)
    TENDRILS.append((names, a))
CURTAINS = []
for k in range(NA):
    a = 2 * math.pi * k / NA + 0.4
    root = Vector((math.sin(a) * 0.08, math.cos(a) * 0.08, RIM_Z + 0.02))
    names = []
    for i in range(AB):
        n = f"curtain_{k + 1}_{i + 1}"
        bones.append(Bone(n, "bell" if i == 0 else names[-1], tuple(root - Vector((0, 0, AL * i))), tuple(root - Vector((0, 0, AL * (i + 1))))))
        names.append(n)
    CURTAINS.append((names, a))
sk = Skeleton("SK_Choir", bones)
sk.build()
kit = rig.Kit(sk, "choir")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


SKIN = Mat("skin.choir", hexc("#7b858d"), shine=0.35)
FRILL = Mat("skin.choir_frill", hexc("#8d959b"), shine=0.3)
LIP = Mat("skin.choir_face", hexc("#6c6670"), shine=0.4)
HOLE = Mat("tar.choir_hole", hexc("#050404"), shine=0.9)
TEETH = Mat("skin.choir_teeth", hexc("#b4ab98"), shine=0.4)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


RIM_NAMES = [n for n, _ in RIMS]

# --- the bell: a hood of membrane, its underside drawn up into it, veined ridges down it; the mouth's socket in front ---
body = kit.part("bell")


def hood(i, j, a, th, p):
    p = Vector(p)
    d = p - BC
    # A bell's profile: the crown narrower, flaring out to the rim.
    if d.z > 0:
        k = 0.82 + 0.22 * (1 - d.z / BR[2])
        p.x, p.y = BC.x + d.x * k, BC.y + d.y * k
    # The underside drawn up: a shallow cup under the dome, the rim a lip round it.
    if d.z < 0:
        r = math.hypot(d.x, d.y) / BR[0]
        p.z = BC.z - BR[2] * 0.48 * smooth01(0.0, 0.85, r) - 0.03 * (1 - r) + 0.02 * smooth01(0.6, 1.0, r)
    # Ribbed down from the crown like an umbrella's panels, sagging between the ribs towards the rim.
    ang = math.atan2(d.x, d.y)
    p += Vector((d.x, d.y, 0)) * 0.02 * math.cos(ang * 8) * smooth01(0.0, 0.2, -d.z + 0.15)
    # The socket the mouth sits in, pressed in round it.
    m = (p - MC).length
    p -= MN * 0.035 * bell(m / 0.11)
    return p + Vector((0, 0, 0.004 * noise3(p * 14, 151, 1.0)))


body.blob(BC, BR, 22, 12, SKIN, (["bell"] + RIM_NAMES, 4.0), shape=hood)
# The frilled skirt round the rim: a ruffled band of membrane hanging off it.
skirt = []
for k in range(36):
    a = 2 * math.pi * k / 36
    skirt.append(Vector((math.sin(a) * (RIM_R + 0.01), math.cos(a) * (RIM_R + 0.01), RIM_Z - 0.035 + 0.02 * math.sin(a * 9))))
skirt = []
for k in range(48):
    a = 2 * math.pi * k / 48
    # Ruffled: in and out round the rim, and scalloped, longer and shorter.
    r = RIM_R + 0.012 + 0.014 * math.sin(a * 14)
    skirt.append(Vector((math.sin(a) * r, math.cos(a) * r, RIM_Z - 0.05)))
body.tube(skirt, [(0.004, 0.055)] * 48, 4, FRILL, (["bell"] + RIM_NAMES, 6.0), ref=[Vector((0, 0, 1))] * 48, loop=True,
          shape=lambda i, j, a, p, fr: Vector(p) + Vector((0, 0, -0.03)) * (0.5 + 0.5 * math.sin(i * 1.7)) * (math.cos(a) < 0))

# --- the mouth: a child's lips held open in an O, small blunt teeth, the dark of the throat --------------------------
mouth = kit.part("mouth")
side = MN.cross(Vector((0, 0, 1))).normalized()
up = side.cross(MN).normalized()
ring = []
for k in range(16):
    t = 2 * math.pi * k / 16
    # The upper lip's bow, the lower lip fuller.
    r = 0.078 * (1 + 0.06 * math.cos(2 * t)) + 0.006 * (math.sin(t) < 0)
    ring.append(MC + side * math.cos(t) * r + up * math.sin(t) * r * 0.92 + MN * (0.014 + 0.006 * bell(math.sin(t) / 0.3)))
mouth.tube(ring, [(0.013 + 0.008 * max(0.0, -math.sin(2 * math.pi * k / 16)), 0.012 + 0.008 * max(0.0, -math.sin(2 * math.pi * k / 16))) for k in range(16)], 10, LIP, "mouth", ref=[MN] * 16, loop=True)
# The dark inside the O: a lens of black filling the lips' ring, its face just proud of the socket.
mouth.blob(MC + MN * 0.008, (0.074, 0.068, 0.014), 14, 6, HOLE, "mouth", rot=Vector((0, 0, 1)).rotation_difference(MN).to_matrix().to_4x4())
for k in range(12):
    t = 2 * math.pi * (k + 0.5) / 12
    if abs(math.cos(t)) > 0.86:
        continue
    at = MC + side * math.cos(t) * 0.064 + up * math.sin(t) * 0.058 + MN * 0.016
    mouth.blob(at, (0.009, 0.008, 0.012), 6, 4, TEETH, "mouth", rot=Vector((0, 0, 1)).rotation_difference(-up if math.sin(t) > 0 else up).to_matrix().to_4x4())

# --- the tendrils, and the curtains under the bell -------------------------------------------------------------
hang = kit.part("tendrils")
for names, a in TENDRILS:
    pts = [H(names[0])] + [H(n).lerp(T(n), f) for n in names for f in (0.5, 1.0)]
    rr = [0.022 - 0.019 * (i / (len(pts) - 1)) ** 0.7 for i in range(len(pts))]
    hang.tube(pts, rr, 5, SKIN, (names, 6.0), ref=(1, 0, 0), cap1=True,
              shape=lambda i, j, an, p, fr: Vector(p) + (fr[0] * math.sin(an) + fr[1] * math.cos(an)) * 0.003 * math.sin(i * 3.1))
for names, a in CURTAINS:
    pts = [H(names[0])] + [H(n).lerp(T(n), f) for n in names for f in (0.5, 1.0)]
    out = Vector((math.sin(a), math.cos(a), 0))
    rr = [(0.05 * (1 - 0.6 * i / (len(pts) - 1)), 0.008) for i in range(len(pts))]
    hang.tube(pts, rr, 6, FRILL, (names, 6.0), ref=[out] * len(pts),
              shape=lambda i, j, an, p, fr: Vector(p) + fr[1] * 0.02 * math.sin(an * 3 + i * 1.7) * abs(math.sin(an)))


# ----------------------------------------------------------------------------------------------------------------
# Clips. The bell's pulse is its scale (contracted: narrower and taller) and its rim flaps curling in about their
# tangents; the tendrils and curtains are aimed bone by bone along a curve (each bone pointed at the curve where it's a
# bone's length on), so they trail, stream, lash and wind round a head without IK.
def euler_of(q):
    e = q.to_euler("YXZ")
    return (math.degrees(e.x), math.degrees(e.y), math.degrees(e.z))


def done(p):
    return {k: (euler_of(v) if isinstance(v, Quaternion) else v) for k, v in p.items()}


def aim_along(p, names, curve):
    """Each bone of a chain pointed at the dense `curve` (a list of points, from about its root) where it's a bone's
    length from the bone's head."""
    p = dict(p)
    at = 0
    for n in names:
        head, = rig.pose_points(sk, p, [(n, "head")])
        L = (sk[n].tail - sk[n].head).length
        # (On along the curve from where the last bone ended: never back up it.)
        at = next((i for i in range(at, len(curve)) if (curve[i] - head).length >= L), len(curve) - 1)
        target = curve[at]
        rest = (sk[n].tail - sk[n].head).normalized()
        parent = sk[n].parent
        W = rig.world_rotation(sk, p, parent) if parent else Quaternion()
        p[n] = W.inverted() @ rest.rotation_difference((target - head).normalized())
    return p


def curve(p, names, step):
    """A dense curve from a chain's posed root: `step(s)` the offset at arc length s (m) from it."""
    root, = rig.pose_points(sk, p, [(names[0], "head")])
    return [root + step(s * 0.01) for s in range(1, 260)]


def pulse(p, c, rim_curl=0.0):
    """The bell contracted by `c` (0 relaxed .. 1 squeezed), the rim curled in by `rim_curl` degrees more."""
    p = dict(p)
    p["bell@scale"] = (1 - 0.14 * c, 1 + 0.16 * c, 1 - 0.14 * c)
    for n, a in RIMS:
        tangent = Vector((math.cos(a), -math.sin(a), 0))
        p[n] = Quaternion(tangent, math.radians(-(10 + 30 * c + rim_curl)))
    return p


def trail(p, ph, sway=0.06, back=0.0, lift=0.0, curtain=1.0):
    """Every tendril and curtain hung down in a slow wave, `back` streamed behind (-Y) and `lift` raised with it."""
    for k, (names, a) in enumerate(TENDRILS):
        out = Vector((math.sin(a), math.cos(a), 0))

        def step(s, k=k, out=out):
            wave = sway * math.sin(ph - s * 4.5 + k * 1.3) * smooth01(0.0, 0.4, s)
            down = Vector((0, 0, -s)) * (1 - back) + Vector((0, -s, -s * (0.25 - lift))) * back
            return down + out * (0.03 * s + 0.4 * wave) + Vector((math.cos(k * 2.1), 0, 0)) * wave
        p = aim_along(p, names, curve(p, names, step))
    for k, (names, a) in enumerate(CURTAINS):
        def step(s, k=k):
            w = 0.03 * curtain * math.sin(ph * 1.3 - s * 7 + k * 2)
            return Vector((w, -0.6 * back * s + w, -s * (1 - 0.4 * back)))
        p = aim_along(p, names, curve(p, names, step))
    return p


def sing(p, o):
    """The mouth opened `o` (0 a small O .. 1 wide)."""
    return p | {"mouth@scale": (1 + 0.45 * o, 1, 1 + 0.6 * o)}


# Drift (4 s, loop): pulsing slowly (two beats), rising a little on each squeeze and sinking back, the tendrils trailing
# in waves, the mouth singing open and narrower and open; once, between beats, snapped round to face you, and held.
drift = Clip("drift")
for f in range(0, 120, 6):
    ph = 2 * math.pi * f / 60
    c = max(0.0, math.sin(ph)) ** 2
    turn = 0 if f < 84 else (32 if f < 102 else 0)
    p = {"root@loc": (0, 0, 0.05 * c), "root": (0, 0, turn)}
    p = trail(sing(pulse(p, c), 0.5 + 0.4 * math.sin(f / 120 * 2 * math.pi * 3 + 1)), ph)
    drift.key(f, done(p), "CONSTANT" if f in (84, 102) else "BEZIER")
drift.close(120)

# Swoop (1 s, loop): tipped into the flight, the mouth first, pulsing hard and fast, the tendrils and curtains streamed
# back behind it, rippling; the mouth wide.
swoop = Clip("swoop")
for f in range(0, 30, 3):
    ph = 2 * math.pi * f / 15
    c = max(0.0, math.sin(ph)) ** 1.5
    p = {"root": (-38, 0, 0), "root@loc": (0, 0.03 * c, 0)}
    p = trail(sing(pulse(p, c, 8), 0.9), ph * 1.5, sway=0.05, back=0.9, lift=0.15)
    swoop.key(f, done(p), "LINEAR")
swoop.close(30)

# Seize (1.6 s, loop): settled on its catch's head like a cap (their head under the bell: the sim has it 1.4 m over their
# feet), turned so its mouth is at their ear, the rim clamped down, the tendrils wound round their skull and down their
# neck; shuddering in squeezes, the mouth singing wide into their ear.
HEAD = Vector((0, -0.04, 0.27))      # their head's middle, in its frame


def wind(p, squeeze):
    for k, (names, a) in enumerate(TENDRILS):
        a0 = a + math.radians(90)

        def step(s, k=k, a0=a0):
            # In to the skull, round it as it goes down, and on down the neck.
            r = 0.115 + 0.01 * squeeze
            into = smooth01(0.0, 0.2, s)
            ang = a0 + (s - 0.1) / r * 0.85 * (1 if k % 2 else -1)
            z = 0.47 - 0.42 * smooth01(0.0, 0.9, s) - 0.25 * smooth01(0.9, 1.2, s)
            wrap = HEAD + Vector((math.sin(ang) * r, math.cos(ang) * r, z - HEAD.z))
            root, = rig.pose_points(sk, p, [(names[0], "head")])
            return (root.lerp(wrap, into)) - root
        p = aim_along(p, names, curve(p, names, step))
    return p


seize = Clip("seize")
for f, squeeze in ((0, 0.0), (6, 1.0), (10, 0.3), (18, 0.0), (26, 1.0), (30, 0.2), (40, 0.0)):
    p = {"root": (0, 0, 90), "root@loc": (0, 0, -0.5 - 0.02 * squeeze)}
    p = trail(sing(pulse(p, 0.6 * squeeze, 30), 0.7 + 0.3 * squeeze), f / 48 * 2 * math.pi, curtain=0.4)
    p = wind(p, squeeze)
    seize.key(f, done(p), "CONSTANT" if f in (6, 26) else "BEZIER")
seize.close(48)

# Besiege (1.2 s, loop): pressed up to a shut door (ahead of it), the mouth to the crack, the tendrils thrown at the
# door and slapping down it, a few at a time, in uneven bursts; between, the bell squeezing against it.
besiege = Clip("besiege")
for f, lash in ((0, (0, 3)), (3, (1, 4)), (5, ()), (9, (2, 5, 0)), (12, ()), (17, (3,)), (20, (1, 4)), (24, ()), (28, (0, 2, 5))):
    p = {"root": (-20, 0, 0), "root@loc": (0, 0.06, 0)}
    p = sing(pulse(p, 0.8 if lash else 0.2, 10), 1.0 if lash else 0.6)
    p = trail(p, f / 36 * 2 * math.pi, sway=0.03)
    for k in lash:
        names, a = TENDRILS[k]

        def step(s, k=k):
            # Up and over, then flat down the door's face, 0.4 m ahead.
            fwd = min(s, 0.4)
            return Vector((0.08 * math.sin(k * 1.9), fwd + 0.02, 0.15 * math.sin(math.pi * min(1.0, s / 0.4)) - max(0.0, s - 0.4)))
        p = aim_along(p, names, curve(p, names, step))
    besiege.key(f, done(p), "CONSTANT")
besiege.close(36)

# Hit (0.4 s, once): struck, the bell crumples in on itself, the tendrils flung out, and it opens again.
REST = done(trail(sing(pulse({}, 0.0), 0.5), 0.0))
hit = Clip("hit", loop=False)
hit.key(0, REST, "CONSTANT")
hit.key(2, done(trail(sing(pulse({"root": (20, 0, 30), "root@loc": (0, -0.1, 0.05)}, 1.0, 40), 1.0), 1.0, sway=0.2)), "CONSTANT")
hit.key(8, done(trail(sing(pulse({}, 0.3), 0.6), 2.0, sway=0.1)), "LINEAR")
hit.key(12, REST, "CONSTANT")

kit.build()
rig.bake(sk, [drift, swoop, seize, besiege, hit])
print("[dt] choir", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "choir.glb", kit)
