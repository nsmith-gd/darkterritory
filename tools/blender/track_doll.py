"""THE TRACK DOLL (GDD v1.2 §21 forward, App. A.2 · sight): "A large porcelain doll standing on the rails. Its white face
shines in the lamp out to 200m." Stop short and it's gone; hit it and it haunts the train: it giggles in the cars, it
stands over the cargo admiring it, and it plays with the controls whenever the cab is left empty.

A Victorian bisque doll grown to 2.15 m and left out by the line: a child's proportions (the head a quarter of its
height, short arms held stiffly off a bell of a skirt), a round glazed face with big glass eyes under heavy lids, a
dark ringlet wig with a fringe, a round lace collar, puffed sleeves, a dress gone grey with soot, the hem ragged, a
petticoat's frill under it, porcelain ball joints at the elbows and knees, and moulded black boots. What's wrong with it
is kept small (GDD §29 "one clear body-language idea", §26.5 contamination, not magic): a crack down through the left
eye, a chip out of the right temple showing the hollow inside, soot run from the eyes like tears, the left eye turned a
little down and out. tools/models/recipes/track_doll.py models its high copy, paints the face and bakes it.

It's jointed, not skinned: every piece is rigid on one bone (the dress's skirt alone follows the thighs a little), so it
moves as a doll moves, at the joints, and holds its poses. GDD §31: unnaturally still when watched, then too-fast
corrections. SK_Human (rig.human) in a doll's proportions.
Clips: stand (on the rail: nothing moves), admire (bent over the cargo to its right, the head snapping between tilts),
giggle (hands over its mouth, shaking), tamper (at the controls, pushing and pulling), cower (cornered: arms over its
face, trembling), hit (struck, once).

    tools/models/build.sh track_doll          # this, its high copy and the bake -> content/art/models/track_doll.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# A 1.8 m template scaled and bent to a doll: short legs and arms, a short neck, a head twice the adult's.
sk = rig.human(height=2.28, leg=0.8, arm=0.82, torso=0.85, neck=0.5, head=2.1, width=0.95, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "track_doll")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
SHOULDER = H("upperarm_r")
KNEE = H("calf_r").z
WAIST = H("spine_01").z
# The head: a glazed egg, rounder than a person's, on the neck's top. HC its centre, HR its radii (x across, y deep,
# z tall). The face's features are placed in its own units: u across (x / rx), w up (z / rz), both -1..1.
HC = Vector((0.0, 0.02, NECK_TOP + 0.25))
HR = Vector((0.212, 0.218, 0.25))
# The glass eyes: centres (u, w), how far they sit in, their radius, and which way each looks (the left one turned a
# little down and out: the only asymmetry a glance catches).
EYE_U, EYE_W, EYE_R = 0.39, -0.04, 0.05
GAZE = {"l": Vector((-0.2, 1.0, -0.2)).normalized(), "r": Vector((0.02, 1.0, -0.03)).normalized()}
CHIP = (0.7, 0.16)  # (u, w) of the chip out of its right temple, beside the eye

PORCELAIN = Mat("skin.porcelain", hexc("#e6dfd2"), shine=0.6)
FACE = Mat("skin.porcelain_face", hexc("#e8e1d4"), shine=0.6)
EYE = Mat("glass_dirty.eye", hexc("#d8d4cc"), shine=0.85)
HAIR = Mat("wool.hair", hexc("#2b2119"), shine=0.2)
RIBBON = Mat("wool.ribbon", hexc("#6c7784"), shine=0.25)
DRESS = Mat("wool.dress", hexc("#b9b1a2"), shine=0.08)
LACE = Mat("wool.lace", hexc("#d8d1c2"), shine=0.08)
BOOT = Mat("leather.boot", hexc("#161312"), shine=0.55)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def head_units(p):
    d = p - HC
    return d.x / HR.x, d.y / HR.y, d.z / HR.z


def head_normal(p):
    d = p - HC
    return Vector((d.x / HR.x ** 2, d.y / HR.y ** 2, d.z / HR.z ** 2)).normalized()


def eye_centre(side):
    """Where an eye sits: on the egg at (±EYE_U, EYE_W), then in by the socket's depth, less how far the glass stands out."""
    u = EYE_U * (-1 if side == "l" else 1)
    v = math.sqrt(max(0.0, 1 - u * u - EYE_W * EYE_W))
    surf = HC + Vector((u * HR.x, v * HR.y, EYE_W * HR.z))
    return surf - head_normal(surf) * 0.03


def sculpt(i, j, a, th, p):
    """The coarse forms of a doll's face on the egg: full cheeks, a small chin, sockets the glass eyes sit in (deeper
    under the eye, so the upper lid comes down over it), a brow, a button of a nose, the chip at the temple."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = max(0.0, v)
    n = head_normal(p)
    off = 0.0
    off += 0.02 * front * bell(math.hypot(abs(u) - 0.5, w + 0.3) / 0.28)            # cheeks
    off += 0.009 * front * bell(u / 0.22) * bell((w + 0.86) / 0.13)                   # the chin's point
    for eu in (-EYE_U, EYE_U):
        off -= 0.024 * front * bell(math.hypot((u - eu) / 0.28, (w - EYE_W + 0.04) / 0.24))   # sockets
        off += 0.006 * front * bell((u - eu) / 0.3) * bell((w - 0.22) / 0.1)                  # brow
    off += 0.014 * front * bell(u / 0.11) * bell((w + 0.27) / 0.11)                  # nose
    off += 0.005 * front * bell(u / 0.15) * bell((w + 0.5) / 0.07)                   # lips
    off -= 0.016 * bell((u - CHIP[0]) / 0.16) * bell((w - CHIP[1]) / 0.14) * smooth01(-0.3, 0.1, v)
    q = p + n * off
    # The jaw narrows under the cheeks to the chin.
    if w < -0.35:
        k = 1 - 0.13 * smooth01(-0.35, -1.0, w)
        q.x = HC.x + (q.x - HC.x) * k
    return q


def face_or_head(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    return FACE if v > 0.3 and -1.05 < w < 0.62 and abs(u) < 0.85 else PORCELAIN


# ----------------------------------------------------------------------------------------------------------------
# The head: the egg, the glass eyes, the neck.
head = kit.part("head")
head.blob(HC, tuple(HR), 28, 20, PORCELAIN, "head", shape=sculpt, fmat=face_or_head)
EYES = {}
for side in ("l", "r"):
    c = eye_centre(side)
    EYES[side] = c
    head.blob(c, (EYE_R, EYE_R, EYE_R), 14, 8, EYE, "head")
head.tube([Vector((0, 0.0, H("neck").z - 0.02)), Vector((0, 0.01, NECK_TOP)), Vector((0, 0.015, NECK_TOP + 0.06))],
          [(0.072, 0.066), (0.068, 0.064), (0.07, 0.066)], 10, PORCELAIN, "neck", ref=(0, 1, 0))


# ----------------------------------------------------------------------------------------------------------------
# The wig: a cap over the crown and the back (the hairline high over the forehead, down to the nape behind), a fringe
# of locks over the brow, ringlets hanging at the sides and behind, a faded bow over the left ear.
hair = kit.part("hair")


def on_egg(theta, phi, lift):
    """A point `lift` metres off the (unsculpted) egg at polar `theta` from the crown and azimuth `phi` from the front."""
    d = Vector((math.sin(theta) * math.sin(phi), math.sin(theta) * math.cos(phi), math.cos(theta)))
    s = HC + Vector((d.x * HR.x, d.y * HR.y, d.z * HR.z))
    return s + head_normal(s) * lift


def hairline(phi):
    """How far down from the crown the wig comes, by azimuth: high over the forehead, over the ears, low at the nape."""
    return math.radians(58 + 64 * (1 - math.cos(phi)) / 2)


AROUND = 22
cap_rings = []
for k in range(1, 10):
    ring = []
    for jj in range(AROUND):
        phi = 2 * math.pi * jj / AROUND
        th = hairline(phi) * k / 9
        # Thicker over the crown and fuller behind, where a wig's hair is piled.
        lift = 0.018 + 0.012 * (1 - k / 9) + 0.02 * max(0.0, -math.cos(phi)) * math.sin(math.pi * k / 9)
        lift += 0.004 * noise3(Vector((phi, k, 0)), 7, 1.7)
        ring.append(on_egg(th, phi, lift))
    cap_rings.append(ring)
# The edge turned under, back into the head, so the hairline has a thickness and no gap shows.
cap_rings.append([on_egg(hairline(2 * math.pi * jj / AROUND), 2 * math.pi * jj / AROUND, -0.012) for jj in range(AROUND)])
# (The first centre is the crown the cap closes to; the rest only need to be inside.)
hair.loft(cap_rings, HAIR, "head", centres=[on_egg(0.0, 0.0, 0.032)] + [HC] * (len(cap_rings) - 1), inside=HC, cap0="point")

# The fringe: cut blunt across the brow, a little uneven: thirteen flat locks overlapping, from under the cap's front.
for k in range(13):
    phi = math.radians(-48 + 8 * k)
    end = math.radians(70 + 2.5 * noise3(Vector((k, 0.3, 0)), 11, 1.3))
    pts = [on_egg(math.radians(52), phi, 0.012), on_egg(math.radians(60), phi, 0.02), on_egg((math.radians(60) + end) / 2, phi, 0.018),
           on_egg(end, phi, 0.015)]
    hair.tube(pts, [(0.034, 0.01), (0.036, 0.011), (0.034, 0.01), (0.03, 0.008)], 6, HAIR, "head", ref=tuple(head_normal(pts[1])),
              cap1=True)

# Ringlets: sausage curls, their spiral a ripple round each (sharpened in the high copy).
RINGLETS = [(95, 100, 0.26), (118, 106, 0.3), (140, 112, 0.32), (162, 118, 0.3), (-95, 100, 0.26), (-118, 106, 0.3),
            (-140, 112, 0.32), (-162, 118, 0.3), (180, 120, 0.28)]
for az, pol, length in RINGLETS:
    phi, th = math.radians(az), math.radians(pol)
    root = on_egg(th - math.radians(8), phi, 0.01)
    out = head_normal(root)
    flat = Vector((out.x, out.y, 0)).normalized()
    pts = [root, root + flat * 0.022 - Vector((0, 0, 0.05))]
    for s in range(1, 5):
        pts.append(pts[1] + flat * 0.004 * s - Vector((0, 0, length * s / 4)))
    radii = [0.03, 0.036, 0.036, 0.034, 0.03, 0.022]

    def curl(i, j, a, p, fr, az=az):
        # The spiral: the radius rippled along the helix.
        return Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * 0.011 * math.sin(a + i * 2.4 + az)
    hair.tube(pts, radii, 8, HAIR, "head", ref=tuple(flat), cap1="point", shape=curl)

# The bow over the left ear, faded to grey-blue: two loops and the knot, two short tails.
bow_at = on_egg(math.radians(62), math.radians(-78), 0.03)
bow_n = head_normal(bow_at)
bow_up = Vector((0, 0, 1)) - bow_n * bow_n.z
bow_up.normalize()
bow_side = bow_n.cross(bow_up)
R = rig.Matrix((bow_side, bow_up, bow_n)).transposed().to_4x4()
for s in (-1, 1):
    hair.blob(bow_at + bow_side * (0.055 * s) + bow_n * 0.008, (0.06, 0.04, 0.02), 10, 5, RIBBON, "head", rot=R)
hair.blob(bow_at + bow_n * 0.012, (0.022, 0.024, 0.018), 8, 4, RIBBON, "head", rot=R)
for s in (-1, 1):
    t0 = bow_at + bow_n * 0.01
    hair.tube([t0, t0 + bow_side * (0.02 * s) - Vector((0, 0, 0.05)), t0 + bow_side * (0.035 * s) - Vector((0, 0, 0.12))],
              [(0.02, 0.006), (0.022, 0.006), (0.018, 0.005)], 5, RIBBON, "head", ref=tuple(bow_n))


# ----------------------------------------------------------------------------------------------------------------
# The dress: bodice, a round lace collar, puffed sleeves, a bell of a skirt with a ragged hem, the petticoat's frill
# under it, a sash round the waist with its bow behind.
dress = kit.part("dress")
SPINE = (["spine_01", "spine_02", "spine_03"], 5.0)
dress.tube([Vector((0, -0.005, WAIST - 0.02)), Vector((0, 0.0, WAIST + 0.12)), Vector((0, 0.0, WAIST + 0.26)),
            Vector((0, -0.005, SHOULDER.z - 0.02)), Vector((0, 0.0, NECK_TOP - 0.03))],
           [(0.17, 0.135), (0.19, 0.15), (0.205, 0.155), (0.2, 0.13), (0.085, 0.075)], 14, DRESS, SPINE,
           ref=(0, 1, 0), square=[0.9, 0.85, 0.85, 0.8, 1.0])

# The collar: from the neck out over the shoulders, scalloped at its edge, with a lip under it.
COL = 26
rings = []
for rr, dz, scallop in ((0.0, 0.0, 0.0), (0.55, -0.025, 0.0), (1.0, -0.06, 1.0), (0.97, -0.068, 1.0)):
    ring = []
    for jj in range(COL):
        a = 2 * math.pi * jj / COL
        ex, ey = 0.09 + rr * 0.15, 0.08 + rr * 0.12
        k = 1 + scallop * 0.022 * abs(math.cos(a * 8))
        ring.append(Vector((math.sin(a) * ex * k, 0.005 + math.cos(a) * ey * k, NECK_TOP - 0.035 + dz)))
    rings.append(ring)
dress.loft(rings, LACE, "spine_03", centres=[Vector((0, 0, NECK_TOP - 0.2))] * 4, inside=Vector((0, 0, NECK_TOP - 0.2)))

# The sleeves: puffed, gathered at the shoulder and the cuff; the upper arm's porcelain shows below them.
for s, sx in (("r", 1), ("l", -1)):
    b = f"upperarm_{s}"
    pts = [Vector((sx * (SHOULDER.x + d), 0, SHOULDER.z)) for d in (-0.04, 0.02, 0.08, 0.15, 0.2)]
    gathers = lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.008 * math.cos(9 * a) * math.sin(math.pi * i / 4))
    dress.tube(pts, [0.07, 0.1, 0.11, 0.09, 0.052], 12, DRESS, b, ref=(0, 0, 1), shape=gathers)
    cuff = [Vector((sx * (SHOULDER.x + d), 0, SHOULDER.z)) for d in (0.19, 0.215)]
    dress.tube(cuff, [0.056, 0.05], 12, LACE, b, ref=(0, 0, 1), cap0=False)


def skirt_weights(p):
    """The skirt on the pelvis at the waist and more and more on the thigh beneath it towards the hem, so a knee that
    comes up pushes the skirt out instead of through it."""
    k = 0.55 * smooth01(WAIST - 0.05, KNEE + 0.1, p.z)
    side = smooth01(-0.08, 0.08, p.x)  # 0 left, 1 right
    out = {"pelvis": 1 - k}
    if k > 0:
        out["thigh_r"] = k * side
        out["thigh_l"] = k * (1 - side)
    return {n: w for n, w in out.items() if w > 1e-4}


SK_N = 32
HEM = KNEE + 0.08
skirt_rings = []
for z, rx, ry, folds in ((WAIST + 0.01, 0.175, 0.14, 0.0), (WAIST - 0.07, 0.24, 0.2, 0.3), (WAIST - 0.2, 0.32, 0.28, 0.6),
                         (WAIST - 0.34, 0.38, 0.34, 0.85), (HEM + 0.04, 0.42, 0.38, 1.0), (HEM, 0.43, 0.39, 1.0)):
    ring = []
    for jj in range(SK_N):
        a = 2 * math.pi * jj / SK_N
        k = 1 + folds * (0.045 * math.sin(7 * a + 0.6) + 0.02 * math.sin(13 * a))
        zz = z
        if z == HEM:
            # Ragged: the hem uneven all round, and torn up in three places.
            zz += 0.022 * noise3(Vector((a * 3, 0, 0)), 31, 1.0)
            for tear in (0.9, 2.6, 4.4):
                zz += 0.07 * bell((a - tear) / 0.09)
        ring.append(Vector((math.sin(a) * rx * k, math.cos(a) * ry * k, zz)))
    skirt_rings.append(ring)
# The hem turned back up inside, so it has an edge.
skirt_rings.append([Vector((p.x * 0.95, p.y * 0.95, p.z + 0.04)) for p in skirt_rings[-1]])
dress.loft(skirt_rings, DRESS, skirt_weights, centres=[Vector((0, 0, r[0].z)) for r in skirt_rings], inside=Vector((0, 0, WAIST - 0.3)))

# The petticoat's frill, below the hem.
fr = []
for z, r, ruffle in ((HEM + 0.05, 0.38, 0.0), (HEM - 0.03, 0.41, 1.0), (HEM - 0.055, 0.405, 1.0)):
    fr.append([Vector((math.sin(a) * r * (1 + ruffle * 0.07 * math.cos(19 * a)), math.cos(a) * r * 0.92 * (1 + ruffle * 0.07 * math.cos(19 * a)), z))
               for a in (2 * math.pi * jj / SK_N for jj in range(SK_N))])
dress.loft(fr, LACE, skirt_weights, centres=[Vector((0, 0, r[0].z)) for r in fr], inside=Vector((0, 0, HEM + 0.2)))

# The sash, and its bow behind.
sash = []
for z, k in ((WAIST + 0.06, 1.05), (WAIST + 0.0, 1.07), (WAIST - 0.05, 1.04)):
    sash.append([Vector((math.sin(a) * 0.18 * k, math.cos(a) * 0.145 * k, z)) for a in (2 * math.pi * jj / 24 for jj in range(24))])
dress.loft(sash, RIBBON, ["pelvis", "spine_01"], centres=[Vector((0, 0, r[0].z)) for r in sash], inside=Vector((0, 0, WAIST)))
back = Vector((0, -0.16, WAIST + 0.01))
for s in (-1, 1):
    dress.blob(back + Vector((0.07 * s, -0.02, 0.01)), (0.075, 0.025, 0.045), 10, 5, RIBBON, "spine_01")
    dress.tube([back, back + Vector((0.04 * s, -0.03, -0.12)), back + Vector((0.07 * s, -0.035, -0.26))],
               [(0.035, 0.008), (0.04, 0.008), (0.035, 0.007)], 5, RIBBON, "pelvis", ref=(0, -1, 0))
dress.blob(back + Vector((0, -0.02, 0.01)), (0.03, 0.022, 0.035), 8, 4, RIBBON, "spine_01")

# The bloomers down the thighs, under the skirt (seen when a knee comes up).
for s, sx in (("r", 1), ("l", -1)):
    dress.tube([H(f"thigh_{s}") + Vector((0, 0, 0.04)), H(f"thigh_{s}").lerp(H(f"calf_{s}"), 0.5), H(f"calf_{s}") + Vector((0, 0, 0.1))],
               [0.11, 0.095, 0.08], 10, LACE, f"thigh_{s}", ref=(0, 1, 0))


# ----------------------------------------------------------------------------------------------------------------
# The limbs: porcelain, ball-jointed: upper arm below the sleeve, the elbow's ball, the forearm to a doll's hand (the
# fingers moulded together, the thumb apart); the knee's ball, the shin, and a moulded boot with a strap.
limbs = kit.part("limbs")
for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    e, wr = H(la), H(hd)
    limbs.tube([Vector((sx * (SHOULDER.x + 0.17), 0, SHOULDER.z)), e - Vector((sx * 0.02, 0, 0))], [0.044, 0.042], 10,
               PORCELAIN, ua, ref=(0, 0, 1))
    limbs.blob(e, (0.05, 0.05, 0.05), 10, 6, PORCELAIN, la)
    limbs.tube([e + Vector((sx * 0.03, 0, 0)), e.lerp(wr, 0.45), wr - Vector((sx * 0.01, 0, 0)), wr + Vector((sx * 0.012, 0, 0))],
               [0.046, (0.047, 0.043), (0.036, 0.033), (0.033, 0.03)], 10, PORCELAIN, la, ref=(0, 0, 1))
    k0 = T(hd)
    # The palm, flat, a little cupped; the fingers as one moulded piece; the thumb.
    limbs.tube([wr + Vector((sx * 0.012, 0, 0)), wr.lerp(k0, 0.5), k0], [(0.034, 0.02), (0.044, 0.022), (0.042, 0.018)], 10,
               PORCELAIN, hd, ref=(0, 0, 1), cap0=False)
    tip = T(f"fingers_{s}")
    limbs.tube([k0, k0.lerp(tip, 0.55), tip], [(0.041, 0.017), (0.036, 0.015), (0.022, 0.011)], 10, PORCELAIN, f"fingers_{s}",
               ref=(0, 0, 1), cap1="point")
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.2], [0.017, 0.015, 0.011], 7, PORCELAIN, f"thumb_{s}",
               ref=(0, 0, 1), cap1="point")

for s, sx in (("r", 1), ("l", -1)):
    kn, an = H(f"calf_{s}"), H(f"foot_{s}")
    limbs.blob(kn, (0.064, 0.064, 0.064), 10, 6, PORCELAIN, f"calf_{s}")
    limbs.tube([kn - Vector((0, 0, 0.04)), kn.lerp(an, 0.3), kn.lerp(an, 0.7), an + Vector((0, 0, 0.1))],
               [0.058, (0.062, 0.064), 0.05, 0.044], 10, PORCELAIN, f"calf_{s}", ref=(0, 1, 0))
    # The boot: shaft, then the foot to the toe, the sole flat on the floor at z = 0.
    x = an.x
    limbs.tube([Vector((x, -0.005, an.z + 0.13)), Vector((x, -0.01, an.z + 0.02))], [0.05, 0.054], 10, BOOT, f"calf_{s}",
               ref=(0, 1, 0), cap0=False)
    toe = T(f"ball_{s}")
    foot = [Vector((x, -0.075, 0.055)), Vector((x, -0.03, 0.06)), Vector((x, 0.06, 0.05)), Vector((x, H(f"ball_{s}").y, 0.04)),
            Vector((x, toe.y + 0.01, 0.03))]
    limbs.tube(foot, [(0.048, 0.055), (0.055, 0.062), (0.052, 0.05), (0.05, 0.04), (0.035, 0.028)], 10, BOOT,
               lambda p, s=s: {f"ball_{s}": 1.0} if p.y > H(f"ball_{s}").y - 0.01 else {f"foot_{s}": 1.0}, ref=(0, 0, 1), square=0.75,
               cap0=True, cap1="point", shape=lambda i, j, a, p, fr: Vector((p.x, p.y, max(0.0, p.z))))
    # The strap over the instep (round under the sole, where it's hidden).
    limbs.tube([Vector((x, 0.0, 0.058)), Vector((x, 0.014, 0.058))], [(0.06, 0.066), (0.06, 0.066)], 10, BOOT, f"foot_{s}",
               ref=(0, 0, 1))


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes (rig.rot): arms lowered by Y, swung by X (+ forward); a spine tipped forward by
# -X and turned by Z (+ to its left). Keys are held and then popped (CONSTANT): a doll doesn't ease, it's moved.
DOLL = mirror({
    "upperarm_r": (4, 74, 0), "lowerarm_r": (0, 0, 5), "hand_r": (0, 6, 0), "fingers_r": (0, 10, 0), "thumb_r": (0, 8, 0),
    "thigh_r": (0, 0, 0), "calf_r": (0, 0, 0), "foot_r": (0, 0, 0),
    "head": (0, 7, 0),
})


def elbow_out(e):
    return 0.4 if abs(e.x) < 0.2 and abs(e.y) < 0.2 else 0.0


def arm_to(pose, side, wrist, fist=0.0):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=elbow_out)
    p[f"fingers_{side}"] = (0, (10 + 70 * fist) * k, 0)
    p[f"thumb_{side}"] = (0, (8 + 25 * fist) * k, 0)
    return p


def face_point(pose, forward=0.1, down=0.12):
    """In front of the face in `pose`: where hands go over a mouth or across the eyes."""
    base, tip = rig.pose_points(sk, pose, [("head", "head"), ("head", "tail")])
    q = rig.world_rotation(sk, pose, "head")
    return base + (tip - base) * 0.4 + q @ Vector((0, forward + HR.y, 0)) - Vector((0, 0, down))


# Stand: on the rail. Nothing moves at all.
stand = Clip("stand")
stand.key(0, DOLL, "CONSTANT")
stand.close(60)

# Admire: bent at the waist over the load to its right, one hand out towards it, the other at its chest; the head
# snaps from one tilt to the next, holds, lurches in closer.
A = over(DOLL, spine_01=(-7, 0, -10), spine_02=(-10, 0, -12), spine_03=(-8, 0, -8), neck=(-6, 0, -4), head=(-12, 16, -18))
A = arm_to(A, "r", (0.62, 0.42, 1.12), fist=0.1)
A = arm_to(A, "l", (0.02, 0.3, 1.45), fist=0.5)
A2 = over(A, head=(-16, -14, -30), neck=(-4, 0, -8))
A3 = arm_to(over(A, spine_02=(-16, 0, -14), spine_03=(-12, 0, -10), head=(-8, 24, -14)), "r", (0.7, 0.5, 1.02), fist=0.0)
admire = Clip("admire")
admire.key(0, A, "CONSTANT")
admire.key(44, A, "CONSTANT")
admire.key(45, A2, "CONSTANT")
admire.key(84, A2, "CONSTANT")
admire.key(85, A, "BEZIER")
admire.key(112, A3, "CONSTANT")
admire.key(138, A3, "CONSTANT")
admire.key(139, A, "CONSTANT")
admire.close(150)

# Giggle: both hands up over its mouth, the shoulders shaking, the head tipping side to side in little jerks.
G = over(DOLL, spine_02=(-6, 0, 0), spine_03=(-8, 0, 0), neck=(-4, 0, 0), head=(-6, 12, 0))
mouth = face_point(G, forward=0.09, down=0.14)
G = arm_to(G, "r", mouth + Vector((0.04, 0, 0)), fist=0.2)
G = arm_to(G, "l", mouth + Vector((-0.04, 0.01, 0.02)), fist=0.2)
giggle = Clip("giggle")
for f in range(0, 48, 4):
    k = (f // 4) % 4
    giggle.key(f, over(G, spine_02=(-6 + (2 if k % 2 else -1), 0, 0), spine_03=(-8 + (3 if k % 2 else 0), 0, 0),
                       head=(-6 + (4 if k % 2 else 0), (12, -4, 16, 0)[k], (0, 4, -3, 2)[k])), "CONSTANT")
giggle.close(48)

# Tamper: at the controls ahead of it, fists on the levers, pushing one and hauling the other in jerks, the head down
# and then up with a snap to see what it's done.
TB = over(DOLL, spine_01=(-5, 0, 0), spine_02=(-6, 0, 0), spine_03=(-4, 0, 0), neck=(-6, 0, 0), head=(-16, 0, 0))


def tamper_pose(r_y, l_y, head_up=0.0):
    p = over(TB, head=(-16 + head_up, 0, 0))
    p = arm_to(p, "r", (0.2, r_y, 1.14), fist=1.0)
    return arm_to(p, "l", (-0.16, l_y, 1.2), fist=1.0)


tamper = Clip("tamper")
tamper.key(0, tamper_pose(0.5, 0.38), "CONSTANT")
tamper.key(6, tamper_pose(0.36, 0.52), "CONSTANT")
tamper.key(12, tamper_pose(0.52, 0.36), "CONSTANT")
tamper.key(18, tamper_pose(0.32, 0.4, head_up=22), "CONSTANT")
tamper.key(30, tamper_pose(0.32, 0.4, head_up=22), "CONSTANT")
tamper.close(36)

# Cower: cornered, it turns half away and folds up, the forearms crossed over its face, knees bent, trembling.
C = over(DOLL, pelvis=(0, 0, 18), spine_01=(-14, 0, 8), spine_02=(-16, 0, 6), spine_03=(-14, 0, 4), neck=(-14, 0, 0),
         head=(-22, 10, 0), thigh_r=(24, 0, 0), calf_r=(-40, 0, 0), foot_r=(16, 0, 0), thigh_l=(18, 0, 0),
         calf_l=(-32, 0, 0), foot_l=(14, 0, 0))
eyes = face_point(C, forward=0.1, down=-0.02)
C = arm_to(C, "r", eyes + Vector((-0.08, 0.02, 0.0)), fist=0.3)
C = arm_to(C, "l", eyes + Vector((0.09, 0.04, 0.06)), fist=0.3)
cower = Clip("cower")
for f in range(0, 24, 2):
    k = 1 if (f // 2) % 2 else -1
    cower.key(f, over(C, spine_03=(-14 + 1.5 * k, 0, 4), head=(-22 - 2 * k, 10 + 2 * k, 0)), "CONSTANT")
cower.close(24)

# Hit: struck, it snaps back and comes forward into the cower again.
hit = Clip("hit", loop=False)
hit.key(0, C, "CONSTANT")
hit.key(2, over(C, spine_02=(-4, 0, 6), spine_03=(-2, 0, 4), neck=(4, 0, 0), head=(6, -18, 12)), "CONSTANT")
hit.key(7, over(C, spine_03=(-8, 0, 4), head=(-10, -8, 6)), "LINEAR")
hit.key(14, C, "BEZIER")

kit.build()
rig.bake(sk, [stand, admire, giggle, tamper, cower, hit],
         plant=rig.feet_planter(sk, clips=["cower", "hit"], lowest=H("ball_r").z))
print("[dt] track_doll", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "track_doll.glb", kit)
