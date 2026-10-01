"""THE GAUNT (GDD v1.2 §21 asleep in villages and yards, App. A.6 · sound (silence)): "A lonely, spindly thing. Wake it
and it follows you home. Every silence makes it angrier, and it hits hard."

A lonely thing, near three metres of it stood up (it never stands up), and the skin on it far too big for what's left
inside. The bones of a starved giant: legs and arms like poles, the knees and elbows knots on them, the ribs a cage, and
over them an ash-grey skin, dry and cracked, gone slack and hanging. It hangs off its belly in an empty sack down over
its hips, in webs under its arms, in folds across its back, slumped round its knees and its ankles like stockings fallen
down. The face is long and mournful and too human: the skin of it has slid, the jowls hanging, the lower lids dragged
down off the eyes so their red rims show; the eyes are big, black and wet, and sad; the mouth is a small round hole, as
if it were about to say "oh". Its hands are huge, the fingers as long as a forearm and the nails horn, for the one thing
it does when the silence gets to it.

The cars weren't built for it (a car's 2.75 m under its roof, the doorway 2.1): aboard it goes down on its knuckles,
and to listen there it squats, folded up small, the knees by its ears (CreatureArt picks by the room; note 110).

SK_Human (rig.human) stretched. Faces +Y (the engine's -Z). Clips (GDD §31: still, then too fast): sleep (the heap,
breathing slowly), stir (the head coming up out of it to look, too slowly, the head tipping over), follow (a slow
stalking lope, head carried low, the hands swinging past its knees), listen (stood over you, leant in, the head
tilted, waiting; the engine leans it in further and tips the head over as its anger climbs), attack (both fists up,
too slowly, and down, too fast), crawl (aboard: the legs folded under, dragging itself on its arms, the head slung
low), squat (aboard: folded up, listening), smash (aboard: the fists from the squat), hit.

    tools/models/build.sh gaunt        # this, its high copy and the bake -> content/art/models/gaunt.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Clip, Mat, Vector, hexc, mirror, noise3, over  # noqa: E402

rig.reset()
# Legs like poles, arms that hang to its shins, a long neck and a long head.
sk = rig.human(height=2.4, leg=1.22, arm=1.6, torso=0.95, neck=2.0, head=1.35, width=0.85, fingers=True, sockets=False)
sk.build()
kit = rig.Kit(sk, "gaunt")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
SHOULDER = H("upperarm_r")
KNEE = H("calf_r").z
HIP = H("thigh_r").z
WAIST = H("spine_01").z
BELLY = H("spine_02").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
# The skull: a long egg, taller than it's deep, the face on its front drawn down to a long chin.
HC = Vector((0.0, 0.02, NECK_TOP + 0.15))
HR = Vector((0.098, 0.118, 0.19))
EYE_U, EYE_W, EYE_R = 0.42, 0.14, 0.03
MOUTH_W = -0.58

SKIN = Mat("skin.gaunt", hexc("#5c5850"), shine=0.12)
FACE = Mat("skin.gaunt_face", hexc("#67635a"), shine=0.14)
RIM = Mat("skin.gaunt_rim", hexc("#5e2b26"), shine=0.5)
EYE = Mat("glass_dirty.gaunt_eye", hexc("#050404"), shine=0.95)
MOUTH = Mat("tar.gaunt_mouth", hexc("#0a0606"), shine=0.3)
NAIL = Mat("tar.gaunt_nail", hexc("#2c261f"), shine=0.4)
FOLD = Mat("skin.gaunt_fold", hexc("#514d46"), shine=0.1)


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


def on_face(u, w, inset):
    """The point on the face at (u, w) (the egg's front, as skull() moves it), `inset` in under the skin."""
    v = math.sqrt(max(0.0, 1 - u * u - w * w))
    surf = HC + Vector((u * HR.x, v * HR.y, w * HR.z))
    return skull(0, 0, 0, 0, surf) - head_normal(surf) * inset


def skull(i, j, a, th, p):
    """The long face: the eyes big in shallow sockets, the brows raised at their inner ends (it's sad), the lids under
    the eyes dragged down into troughs; a long thin nose drooping at its tip; the cheeks slid down into jowls either side
    of a small round mouth, open; the chin long. Behind it the skull's bald, high and lumped."""
    p = Vector(p)
    u, v, w = head_units(p)
    front = smooth01(-0.1, 0.4, v)
    n = head_normal(p)
    off = 0.0
    for eu in (-EYE_U, EYE_U):
        off -= 0.022 * front * bell(math.hypot((u - eu) / 0.3, (w - EYE_W) / 0.24))                          # sockets
        off += 0.01 * front * bell((u - eu * 0.6) / 0.22) * bell((w - EYE_W - 0.3 + 0.35 * (abs(u) - 0.25)) / 0.1)  # brows, sad
        off -= 0.012 * front * bell((u - eu) / 0.22) * bell((w - EYE_W + 0.2) / 0.08)                       # the lids dragged down
        off += 0.008 * front * bell((u - eu * 1.1) / 0.25) * bell((w - EYE_W + 0.4) / 0.12)                 # the bags under them
    off += 0.024 * front * bell(u / 0.09) * smooth01(0.08, -0.05, w) * smooth01(-0.48, -0.28, w)            # the nose's ridge
    off += 0.014 * front * bell(u / 0.15) * bell((w + 0.46) / 0.07)                                          # its tip, drooping
    r = math.hypot(u / 0.27, (w - MOUTH_W) / 0.17)
    off -= 0.02 * front * bell(r / 0.8)                                                                       # the mouth
    off += 0.007 * front * bell((r - 1.0) / 0.3)                                                              # its rim
    off -= 0.012 * bell((abs(u) - 0.9) / 0.15) * bell((w - 0.35) / 0.3)                                     # temples
    off += 0.006 * noise3(p * 18, 7, 1.0) * smooth01(0.0, -0.4, v)                                           # the skull's lumps
    q = p + n * off
    # The cheeks slid down into jowls, hanging out over the jaw either side of the mouth; the chin long.
    if w < -0.15:
        k = smooth01(-0.15, -0.9, w)
        jowl = bell((abs(u) - 0.65) / 0.3) * front
        q.x = HC.x + (q.x - HC.x) * (1 + 0.22 * jowl * k)
        q.z -= 0.05 * k + 0.035 * jowl * smooth01(-0.5, -1.0, w)
    return q


def face_or_skull(points, normal):
    c = sum(points, Vector()) / len(points)
    u, v, w = head_units(c)
    return FACE if v > 0.15 and -1.2 < w < 0.7 and abs(u) < 0.9 else SKIN


# ----------------------------------------------------------------------------------------------------------------
# The head: the skull, the eyes under their heavy lids and over the red of the dragged-down ones, the mouth's hole; the
# neck long, the throat's skin slack and creased, the knuckles of its bones down the back of it.
head = kit.part("head")
head.blob(HC, tuple(HR), 24, 22, SKIN, "head", shape=skull, fmat=face_or_skull)
EYES = {}
for side, eu in (("l", -EYE_U), ("r", EYE_U)):
    sx = -1 if side == "l" else 1
    c = on_face(eu, EYE_W, EYE_R * 0.35)
    EYES[side] = c
    head.blob(c, (EYE_R, EYE_R * 0.8, EYE_R * 0.92), 12, 8, EYE, "head")
    # The upper lid, heavy, down over the top of the eye and drooping to its outer corner: tired, and sad.
    head.blob(c + Vector((0, 0.002, 0)), (EYE_R * 1.12, EYE_R * 0.92, EYE_R * 1.04), 12, 4, FACE, "head", rot=rig.rot(0, sx * 20, 0).to_matrix().to_4x4(), z0=0.05)
    # The lower lid dragged down off the eye: its wet red inside showing in a crescent under it.
    arc = [c + Vector((sx * math.cos(a) * EYE_R * 1.02, 0.005 - 0.006 * math.sin(a) ** 2, -math.sin(a) * EYE_R * 1.05 - 0.012 * math.sin(a) ** 2))
           for a in (math.radians(d) for d in (10, 45, 90, 135, 170))]
    head.tube(arc, [(0.004, 0.003), (0.006, 0.008), (0.007, 0.012), (0.006, 0.008), (0.004, 0.003)], 6, RIM, "head", ref=(0, 1, 0),
              cap0=True, cap1=True)
# The mouth, a small round hole, the lips round it pursed as if to say "oh".
MC = on_face(0.0, MOUTH_W, 0.0)
mouth_n = head_normal(HC + Vector((0, HR.y * 0.8, MOUTH_W * HR.z)))
head.blob(MC - mouth_n * 0.014, (0.022, 0.018, 0.027), 8, 6, MOUTH, "head")
lips = [MC + Vector((math.sin(a) * 0.026, 0.003, math.cos(a) * 0.032)) for a in (2 * math.pi * k / 12 for k in range(12))]
head.tube(lips, [0.009] * 12, 5, FACE, "head", ref=(0, 1, 0), loop=True)
NECK = [Vector((0, -0.02, TOP - 0.03)), Vector((0, -0.0, TOP + (NECK_TOP - TOP) * 0.35)), Vector((0, 0.01, TOP + (NECK_TOP - TOP) * 0.7)),
        Vector((0, 0.02, NECK_TOP + 0.05))]
head.tube(NECK, [(0.05, 0.048), (0.034, 0.036), (0.032, 0.034), (0.04, 0.042)], 10, SKIN, ["spine_03", "neck", "neck", "head"], ref=(0, 1, 0),
          shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (
              0.007 * max(0.0, -math.cos(a)) * max(0.0, math.sin(Vector(p).z * 60)) + 0.004 * bell((abs(math.sin(a)) - 0.75) / 0.12)
              + 0.006 * max(0.0, math.cos(a)) * math.sin(Vector(p).z * 110) ** 2))


# ----------------------------------------------------------------------------------------------------------------
# The body: a cage of ribs over a waist gone to nothing, the hips' crests standing; and the skin hanging off it, an
# empty sack of belly over its hips, creases slumped across its back.
body = kit.part("body")
SPINE = (["pelvis", "spine_01", "spine_02", "spine_03"], 5.0)
TORSO = [(HIP - 0.04, 0.14, 0.09), (HIP + 0.06, 0.12, 0.08), (WAIST, 0.09, 0.07), (BELLY - 0.02, 0.11, 0.08), (BELLY + 0.06, 0.15, 0.11),
         (CHEST + 0.04, 0.165, 0.115), (TOP - 0.08, 0.17, 0.1), (TOP - 0.03, 0.15, 0.085), (TOP, 0.09, 0.065), (TOP + 0.015, 0.06, 0.05)]


def ribs(i, j, a, p, fr):
    """The ribs round the cage, the breastbone's groove, the belly sunk under them, the spine's knuckles down the back."""
    p = Vector(p)
    out = fr[0] * math.sin(a) + fr[1] * math.cos(a)
    front, back = max(0.0, math.cos(a)), max(0.0, -math.cos(a))
    d = 0.0
    if BELLY - 0.04 < p.z < TOP - 0.05:
        d += 0.012 * max(0.0, math.sin((p.z - WAIST) * 48)) ** 2 * (0.4 + 0.6 * abs(math.sin(a)))
    d -= 0.008 * front * bell(math.sin(a) / 0.12) * smooth01(BELLY, BELLY + 0.1, p.z)
    d -= 0.016 * front * bell((p.z - WAIST - 0.03) / 0.07)
    d += 0.01 * back * bell(math.sin(a) / 0.2) * max(0.0, math.sin(p.z * 60))
    # The shoulder blades standing out of its back.
    d += 0.018 * back * bell((abs(math.sin(a)) - 0.5) / 0.2) * bell((p.z - TOP + 0.12) / 0.06)
    # The skin of its back gone slack: creases slumped across it, sagging lowest at the middle of the back.
    slump = p.z + 0.05 * back ** 2
    d += 0.014 * back * math.sin(slump * 42) ** 4 * smooth01(WAIST - 0.05, WAIST + 0.05, p.z) * smooth01(TOP - 0.05, CHEST, p.z)
    return p + out * d


def resampled(secs, step):
    out = []
    for (z0, x0, y0), (z1, x1, y1) in zip(secs, secs[1:]):
        n = max(1, round((z1 - z0) / step))
        out += [(z0 + (z1 - z0) * k / n, x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n) for k in range(n)]
    return out + [secs[-1]]


RIBBED = resampled(TORSO, 0.028)
body.tube([Vector((0, 0.0, z)) for z, _, _ in RIBBED], [(rx, ry) for _, rx, ry in RIBBED], 14, SKIN, SPINE, ref=(0, 1, 0),
          square=0.9, shape=ribs, cap1=True)
for sx in (-1, 1):
    s = "r" if sx > 0 else "l"
    body.tube([Vector((sx * 0.02, 0.07, TOP - 0.03)), Vector((sx * 0.12, 0.055, TOP - 0.01)), Vector((sx * (SHOULDER.x - 0.01), 0.01, SHOULDER.z + 0.01))],
              [0.013, 0.012, 0.017], 6, SKIN, [f"clavicle_{s}"], ref=(0, 0, 1))
    body.blob(Vector((sx * SHOULDER.x, 0.0, SHOULDER.z)), (0.036, 0.04, 0.034), 8, 5, SKIN, [f"upperarm_{s}"])
    body.blob(Vector((sx * 0.115, 0.03, HIP + 0.07)), (0.034, 0.04, 0.034), 8, 4, SKIN, "pelvis")

# The belly's skin: an empty sack hung off the bottom of the ribs, down over the hips to the tops of its thighs, wrinkled
# across, fullest at the bottom where whatever's left in it has settled.
SACK_TOP, SACK_LOW = BELLY + 0.02, HIP - 0.32
sack = []
SN = 14
for i in range(9):
    k = i / 8
    z = SACK_TOP + (SACK_LOW - SACK_TOP) * k
    rx = (0.1 + 0.07 * math.sin(math.pi * min(1.0, k * 1.15))) * (1 + 0.05 * math.sin(k * 30))
    ry = 0.03 + 0.07 * math.sin(math.pi * min(1.0, k * 1.05)) ** 0.8
    cy = 0.06 + 0.05 * k
    if i == 8:
        rx, ry = 0.04, 0.02
    ring = []
    for jj in range(SN):
        a = 2 * math.pi * jj / SN
        sag = 0.03 * k * max(0.0, math.cos(a))
        ring.append(Vector((math.sin(a) * rx, cy + math.cos(a) * ry, z - sag)))
    sack.append(ring)


def sack_weights(p):
    if p.z > WAIST:
        return {"spine_01": 0.7, "spine_02": 0.3}
    if p.z > HIP:
        return {"pelvis": 0.6, "spine_01": 0.4}
    side = smooth01(-0.06, 0.06, p.x)
    k = smooth01(HIP, SACK_LOW, p.z) * 0.5
    out = {"pelvis": 1 - k, "thigh_r": k * side, "thigh_l": k * (1 - side)}
    return {n: w for n, w in out.items() if w > 1e-4}


body.loft(sack, FOLD, sack_weights, centres=[Vector((0, 0.06, r[0].z)) for r in sack], cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# The limbs: poles with knots for joints; webs of skin under the arms; skin slumped round the knees and the ankles; the
# hands huge, the fingers as long as a forearm and the nails horn; the feet long and flat.
limbs = kit.part("limbs")


def jointed(a, b, c, upper, lower, blend=0.04):
    bend = ((b - a).normalized() + (c - b).normalized()).normalized()

    def w(p):
        k = smooth01(-blend, blend, (p - b).dot(bend))
        return {n: v for n, v in ((upper, 1 - k), (lower, k)) if v > 1e-4}
    return w


def sag_ring(centre, radius, drop, bones, thick=0.011, sides=10):
    """A fold of slack skin round a limb (a stocking fallen down), lower at the front."""
    pts = []
    for k in range(sides):
        a = 2 * math.pi * k / sides
        pts.append(centre + Vector((math.sin(a) * radius, math.cos(a) * radius, -drop * max(0.0, math.cos(a)) ** 2)))
    limbs.tube(pts, [thick] * sides, 5, FOLD, bones, ref=(0, 0, 1), loop=True)


for s, sx in (("r", 1), ("l", -1)):
    ua, la, hd = f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"
    sh, e, wr = H(ua), H(la), H(hd)
    limbs.tube([sh + Vector((sx * 0.02, 0, 0)), sh.lerp(e, 0.45), e, e.lerp(wr, 0.35), e.lerp(wr, 0.8), wr],
               [0.036, 0.028, (0.032, 0.034), (0.03, 0.027), (0.025, 0.021), (0.024, 0.02)], 9, SKIN, jointed(sh, e, wr, ua, la), ref=(0, 0, 1))
    limbs.blob(e + Vector((0, -0.012, 0)), (0.03, 0.034, 0.03), 7, 4, SKIN, la)            # the elbow's knot
    limbs.blob(wr, (0.028, 0.026, 0.022), 7, 4, SKIN, hd)
    # The web of skin under the upper arm, from its side to the elbow, hanging, its lower edge ragged.
    web = [sh.lerp(e, k) + Vector((-sx * 0.05 * (1 - k), -0.005, -0.07 - 0.1 * math.sin(math.pi * min(1.0, k * 1.05)))) for k in (0.0, 0.25, 0.5, 0.75, 0.95)]
    limbs.tube(web, [(0.008, 0.02), (0.012, 0.07), (0.013, 0.1), (0.011, 0.08), (0.008, 0.04)], 6, FOLD,
               lambda p, sh=sh, e=e, ua=ua, la=la, s=s: (
                   {"spine_03": 0.6, ua: 0.4} if (p - sh).length < 0.1 else {ua: 1.0} if (p - e).length > 0.12 else {ua: 0.6, la: 0.4}),
               ref=(0, 0, 1), shape=lambda i, j, a, p, fr: Vector(p) - Vector((0, 0, 0.012 * max(0.0, -math.cos(a)) * math.sin(i * 2.7 + j))))
    sag_ring(e.lerp(wr, 0.85), 0.03, 0.025, la, thick=0.009)
    # The hand: the palm broad, five long knuckled fingers' worth in four, the nails horn and hooked.
    k0 = T(hd)
    limbs.tube([wr + Vector((sx * 0.01, 0, 0)), wr.lerp(k0, 0.55), k0], [(0.05, 0.02), (0.07, 0.022), (0.074, 0.02)], 10, SKIN, hd,
               ref=(0, 0, 1))
    for f in range(4):
        spread = (f - 1.5) * 0.036
        base = k0 + Vector((0, spread, 0))
        along = Vector((sx, spread * 1.0, 0)).normalized()
        n = 0.3 * (0.82 + 0.18 * (1 - abs(f - 1.5) / 1.5)) * (0.85 if f == 3 else 1.0)
        pts = [base, base + along * n * 0.38, base + along * n * 0.7, base + along * n]
        w = lambda p, base=base, n=n, s=s: {f"hand_{s}": 1.0} if (p - base).length < n * 0.1 else {f"fingers_{s}": 1.0}
        limbs.tube(pts, [0.017, 0.015, 0.013, 0.01], 6, SKIN, w, ref=(0, 0, 1),
                   shape=lambda i, j, a, p, fr: Vector(p) + (fr[0] * math.sin(a) + fr[1] * math.cos(a)) * (0.004 if i in (1, 2) else 0.0))
        tip = pts[-1]
        limbs.tube([tip - along * 0.02, tip + along * 0.03 - Vector((0, 0, 0.012)), tip + along * 0.05 - Vector((0, 0, 0.04))],
                   [(0.011, 0.007), (0.008, 0.005), (0.002, 0.002)], 5, NAIL, f"fingers_{s}", ref=(0, 0, 1), cap1="point")
    th0, th1 = H(f"thumb_{s}"), T(f"thumb_{s}")
    limbs.tube([th0, th0.lerp(th1, 0.6), th1 + (th1 - th0) * 0.9], [0.018, 0.015, 0.009], 6, SKIN, f"thumb_{s}", ref=(0, 0, 1), cap1="point")

for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}")
    toe = T(f"ball_{s}")
    limbs.tube([hp + Vector((0, 0, 0.03)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.4), an + Vector((0, 0, 0.03))],
               [0.056, 0.038, (0.045, 0.047), (0.033, 0.036), 0.026], 9, SKIN, jointed(hp, kn, an, f"thigh_{s}", f"calf_{s}"), ref=(0, 1, 0))
    limbs.blob(kn + Vector((0, 0.02, 0.01)), (0.042, 0.04, 0.046), 8, 5, SKIN, {f"thigh_{s}": 0.4, f"calf_{s}": 0.6})   # the knee's knot
    sag_ring(kn + Vector((0, 0, -0.1)), 0.046, 0.04, f"calf_{s}", thick=0.013)
    sag_ring(an + Vector((0, 0, 0.1)), 0.034, 0.03, f"calf_{s}", thick=0.011)
    limbs.blob(an, (0.03, 0.032, 0.028), 8, 4, SKIN, f"foot_{s}")
    limbs.tube([an - Vector((0, 0.05, 0)), an.lerp(bl, 0.5), bl], [(0.034, 0.026), (0.044, 0.02), (0.05, 0.018)], 8, SKIN, f"foot_{s}",
               ref=(0, 0, 1), cap0=True)
    for t in range(3):
        spread = (t - 1) * 0.03
        b0 = bl + Vector((spread, 0, 0))
        dirn = Vector((spread * 1.5, 1, -0.1)).normalized()
        tl = 0.09 if t == 1 else 0.075
        limbs.tube([b0, b0 + dirn * tl * 0.6, b0 + dirn * tl], [0.014, 0.012, 0.01], 5, SKIN, f"ball_{s}", ref=(0, 0, 1))
        limbs.tube([b0 + dirn * (tl - 0.01), b0 + dirn * (tl + 0.025) - Vector((0, 0, 0.012))], [(0.01, 0.006), (0.002, 0.002)], 4, NAIL,
                   f"ball_{s}", ref=(0, 0, 1), cap1="point")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/tippy_toesie.py: arms lowered by Y, swung by X (+ forward); a spine tipped forward by -X,
# turned by Z, tipped over sideways by Y; a thigh raised forward by +X, a knee bent by -X. Held and popped (CONSTANT) where
# it's watching: it's still, then it moves, too fast.
BASE = mirror({
    "upperarm_r": (6, 80, 0), "lowerarm_r": (0, 8, 0), "fingers_r": (0, 20, 0), "thumb_r": (0, 6, 0),
    "thigh_r": (8, 0, 0), "calf_r": (-14, 0, 0), "foot_r": (6, 0, 0),
})
# Its stance: stooped, the shoulders rounded, the neck out ahead of them and the head brought up on it to look.
STOOP = over(BASE, spine_01=(-8, 0, 0), spine_02=(-10, 0, 0), spine_03=(-14, 0, 0), neck=(-36, 0, 0), head=(34, 0, 0))


def elbow_out(e):
    return 0.4 if abs(e.x) < 0.17 and e.y > -0.05 else 0.0


def arm_to(pose, side, wrist, curl=0.0, avoid=elbow_out):
    k = 1 if side == "r" else -1
    p = rig.reach(sk, pose, f"upperarm_{side}", f"lowerarm_{side}", wrist, elbow_axis=2, bend=k, avoid=avoid)
    p[f"fingers_{side}"] = (0, (20 + 60 * curl) * k, 0)
    p[f"thumb_{side}"] = (0, (6 + 30 * curl) * k, 0)
    return p


FEET = [(b, e) for b in ("foot_l", "foot_r", "ball_l", "ball_r") for e in ("head", "tail")]
TOPS = [("head", "tail"), ("head", "head"), ("spine_03", "tail"), ("upperarm_r", "head"), ("upperarm_l", "head"), ("hand_r", "tail"), ("hand_l", "tail")]


def ground(pose):
    return min(p.z for p in rig.pose_points(sk, pose, FEET)) - 0.02


def height(pose):
    """How tall it stands in `pose` once its feet are planted (the skull's crown over the back of its head)."""
    pts = rig.pose_points(sk, pose, TOPS)
    return max(p.z for p in pts) + 0.06 - ground(pose)


def shoulder(pose, side):
    return rig.pose_points(sk, pose, [(f"upperarm_{side}", "head")])[0]


def knees(pose, deg):
    out = dict(pose)
    for s in ("l", "r"):
        tx, ty, tz = out.get(f"thigh_{s}", (0, 0, 0))
        cx, cy, cz = out.get(f"calf_{s}", (0, 0, 0))
        fx, fy, fz = out.get(f"foot_{s}", (0, 0, 0))
        out[f"thigh_{s}"], out[f"calf_{s}"], out[f"foot_{s}"] = (tx + deg * 0.55, ty, tz), (cx - deg, cy, cz), (fx + deg * 0.45, fy, fz)
    return out


# Sleep (6 s, loop): the heap. Squatted right down on its heels, folded over its knees, its face in them, its arms
# wrapped over its head and the long fingers hanging down its back; the back rising and falling, slow; once, the
# fingers of a hand open on its skull and close.
HEAP = over(BASE, spine_01=(-50, 0, 0), spine_02=(-34, 0, 0), spine_03=(-26, 0, 0), neck=(-34, 0, 0), head=(-24, 0, 18),
            thigh_r=(124, 0, 12), thigh_l=(124, 0, -12), calf_r=(-152, 0, 0), calf_l=(-152, 0, 0), foot_r=(32, 0, 0), foot_l=(32, 0, 0))
crown = rig.pose_points(sk, HEAP, [("head", "tail")])[0]
HEAP = arm_to(arm_to(HEAP, "r", crown + Vector((0.1, -0.14, 0.02)), curl=0.3), "l", crown + Vector((-0.1, -0.1, 0.04)), curl=0.3)
sleep = Clip("sleep")
for f, k in ((0, 0.0), (45, 1.0), (90, 0.0), (135, 1.0)):
    sleep.key(f, over(HEAP, spine_02=(-34 + 3 * k, 0, 0), spine_03=(-26 + 2.5 * k, 0, 0)), "BEZIER")
sleep.key(150, HEAP | {"fingers_r": (0, 80, 0)}, "CONSTANT")
sleep.key(156, HEAP | {"fingers_r": (0, 38, 0)}, "CONSTANT")
sleep.close(180)

# Stir (4 s, loop while it's stirring): the head comes up out of the heap, slowly, until the eyes are over its arm and
# on you; it holds there; then the head tips right over on its side, all at once, and stays.
PEEK = over(HEAP, spine_01=(-42, 0, 0), spine_02=(-24, 0, 0), spine_03=(-12, 0, 0), neck=(-14, 0, 0), head=(26, 0, 0))
kr, kl = rig.pose_points(sk, PEEK, [("calf_r", "head"), ("calf_l", "head")])
PEEK = arm_to(arm_to(PEEK, "r", kr + Vector((-0.02, 0.1, 0.06)), curl=0.5), "l", kl + Vector((0.02, 0.1, 0.08)), curl=0.5)
for side in ("r", "l"):
    # The hand tipped over its knee, whichever way lets its fingers hang down over it.
    PEEK = min((PEEK | {f"hand_{side}": (rx, ry, 0)} for rx in (-50, 0, 50) for ry in (-60, 0, 60)),
               key=lambda q, side=side: rig.pose_points(sk, q, [(f"fingers_{side}", "tail")])[0].z)
stir = Clip("stir")
stir.key(0, HEAP, "BEZIER")
stir.key(54, PEEK, "CONSTANT")
stir.key(80, over(PEEK, head=(26, 34, 0)), "CONSTANT")
stir.key(112, over(PEEK, head=(26, 30, 0)), "CONSTANT")
stir.key(120, over(PEEK, head=(26, 34, 0)), "CONSTANT")

# Follow (2.4 s, loop): a slow stalking lope, long steps on bent knees, the head carried low out ahead, the long arms
# swinging past its knees; every other step the head turns to you, too fast, and holds.
LOPE = knees(over(STOOP, spine_01=(-12, 0, 0), spine_02=(-12, 0, 0), neck=(-40, 0, 0), head=(40, 0, 0)), 18)
follow = Clip("follow")
for f, k in ((0, 1.0), (18, 0.0), (36, -1.0), (54, 0.0)):
    p = over(LOPE, pelvis=(0, 0, 6 * k), spine_03=(-14, 0, -5 * k), upperarm_r=(6 - 22 * k, 80, 0), upperarm_l=(6 + 22 * k, -80, 0),
             lowerarm_r=(0, 8 + 10 * max(0.0, -k), 0), lowerarm_l=(0, -8 - 10 * max(0.0, k), 0), head=(40, 0, 24 if f == 36 else 0))
    for side, sgn in (("r", 1), ("l", -1)):
        tx, _, _ = p[f"thigh_{side}"]
        cx, _, _ = p[f"calf_{side}"]
        swing = sgn * k
        p[f"thigh_{side}"] = (tx + 26 * swing, 0, 0)
        p[f"calf_{side}"] = (cx - 34 * max(0.0, -swing), 0, 0)
    follow.key(f, p, "CONSTANT" if f == 36 else "BEZIER")
follow.close(72)

# Listen (4 s, loop): stood over you, stooped, the neck out and bent down, the head on one side, the big eyes on you;
# still, waiting for you to say something; now and then the long fingers of a hand ripple. (The engine leans it in and
# tips its head over further as it gets angry.)
LS = over(STOOP, spine_02=(-14, 0, 0), spine_03=(-18, 0, 0), neck=(-44, 0, 0), head=(36, 0, 0))
listen = Clip("listen")
listen.key(0, over(LS, head=(36, 16, 0)), "CONSTANT")
listen.key(66, over(LS, head=(36, 24, 0)), "CONSTANT")
for f, c in ((88, 0.6), (91, -0.2), (94, 0.7), (97, 0.1)):
    listen.key(f, over(LS, head=(36, 24, 0)) | {"fingers_r": (0, 20 + 60 * c, 0)}, "CONSTANT")
listen.close(120)

# Attack (1.6 s, loop): both fists up over its head, slowly, the whole length of it drawn up behind them; then down,
# too fast, the body folding into the blow; and held there, bent over what it hit, the head coming up to look.
RAISE = over(BASE, spine_02=(6, 0, 0), spine_03=(8, 0, 0), neck=(-16, 0, 0), head=(14, 0, 0))
RAISE = arm_to(arm_to(RAISE, "r", shoulder(RAISE, "r") + Vector((-0.12, 0.18, 0.95)), curl=1.0), "l", shoulder(RAISE, "l") + Vector((0.12, 0.18, 0.95)), curl=1.0)
SLAM = knees(over(BASE, spine_01=(-30, 0, 0), spine_02=(-24, 0, 0), spine_03=(-14, 0, 0), neck=(-14, 0, 0), head=(20, 0, 0)), 30)
g = ground(SLAM)
SLAM = arm_to(arm_to(SLAM, "r", Vector((0.12, 0.95, g + 0.5)), curl=1.0), "l", Vector((-0.12, 0.95, g + 0.5)), curl=1.0)
attack = Clip("attack")
attack.key(0, SLAM, "BEZIER")
attack.key(22, RAISE, "LINEAR")
attack.key(28, RAISE, "LINEAR")
attack.key(32, SLAM, "CONSTANT")
attack.key(42, over(SLAM, head=(40, 0, 0)), "CONSTANT")
attack.close(48)

# Crawl (2 s, loop; aboard): down under the roof, the long legs folded under it, dragging itself along on its arms: a
# hand reached out ahead and planted, and the body drawn up to it, then the other; the back humped, the head slung low
# between its shoulders and turned up to look ahead.
CR = over(BASE, spine_01=(-56, 0, 0), spine_02=(-22, 0, 0), spine_03=(-10, 0, 0), neck=(-16, 0, 0), head=(70, 0, 0))
CR = knees(over(CR, thigh_r=(50, 0, 10), thigh_l=(50, 0, -10)), 96)


def knuckle(pose, side, ahead):
    sx = 1 if side == "r" else -1
    sh = shoulder(pose, side)
    return arm_to(pose, side, Vector((sh.x + sx * 0.16, sh.y + ahead, ground(pose) + 0.12)), curl=1.0)


crawl = Clip("crawl")
for f, k in ((0, 1.0), (15, 0.0), (30, -1.0), (45, 0.0)):
    p = over(CR, pelvis=(0, 0, 5 * k), spine_03=(-14, 0, -6 * k))
    for side, sgn in (("r", 1), ("l", -1)):
        tx, _, tz = p[f"thigh_{side}"]
        cx, _, _ = p[f"calf_{side}"]
        swing = sgn * k
        p[f"thigh_{side}"] = (tx + 14 * swing, 0, tz)
        p[f"calf_{side}"] = (cx - 10 * max(0.0, -swing), 0, 0)
    p = knuckle(knuckle(p, "r", 0.42 - 0.22 * k), "l", 0.42 + 0.22 * k)
    crawl.key(f, p, "BEZIER")
crawl.close(60)

# Squat (4 s, loop; aboard): folded up small on its heels, the knees up by its ears, the long arms wrapped round its
# shins and the fingers laced; the head out over its knees towards you, on one side, listening; still.
SQ = over(BASE, spine_01=(-26, 0, 0), spine_02=(-14, 0, 0), spine_03=(-8, 0, 0), neck=(-34, 0, 0), head=(36, 0, 0),
          thigh_r=(118, 0, 16), thigh_l=(118, 0, -16), calf_r=(-150, 0, 0), calf_l=(-150, 0, 0), foot_r=(30, 0, 0), foot_l=(30, 0, 0))
kr, kl = rig.pose_points(sk, SQ, [("calf_r", "head"), ("calf_l", "head")])
SQ = arm_to(arm_to(SQ, "r", kr + Vector((-0.08, 0.14, -0.28)), curl=0.8), "l", kl + Vector((0.08, 0.14, -0.26)), curl=0.8)
squat = Clip("squat")
squat.key(0, over(SQ, head=(36, 20, 0)), "CONSTANT")
squat.key(70, over(SQ, head=(36, 30, 0)), "CONSTANT")
for f, c in ((90, 0.6), (93, -0.1), (96, 0.8), (99, 0.2)):
    squat.key(f, over(SQ, head=(36, 30, 0)) | {"fingers_l": (0, -(20 + 60 * c), 0)}, "CONSTANT")
squat.close(120)

# Smash (1.4 s, loop; aboard): up off its heels on its knees' spring, the fists up as high as the roof lets them, and
# down on whoever's in front of it.
UPS = knees(over(BASE, spine_01=(-24, 0, 0), spine_02=(-14, 0, 0), spine_03=(-8, 0, 0), neck=(-30, 0, 0), head=(30, 0, 0)), 80)
UPS = arm_to(arm_to(UPS, "r", shoulder(UPS, "r") + Vector((-0.1, 0.25, 0.45)), curl=1.0), "l", shoulder(UPS, "l") + Vector((0.1, 0.25, 0.45)), curl=1.0)
DOWN = knees(over(BASE, spine_01=(-44, 0, 0), spine_02=(-26, 0, 0), spine_03=(-12, 0, 0), neck=(-10, 0, 0), head=(36, 0, 0)), 90)
g = ground(DOWN)
DOWN = arm_to(arm_to(DOWN, "r", Vector((0.1, 0.9, g + 0.45)), curl=1.0), "l", Vector((-0.1, 0.9, g + 0.45)), curl=1.0)
smash = Clip("smash")
smash.key(0, DOWN, "BEZIER")
smash.key(18, UPS, "LINEAR")
smash.key(24, UPS, "LINEAR")
smash.key(28, DOWN, "CONSTANT")
smash.close(42)

hit = Clip("hit", loop=False)
hit.key(0, LS, "CONSTANT")
hit.key(2, over(LS, spine_02=(4, 0, 14), spine_03=(2, 0, 10), head=(10, 0, 30)), "CONSTANT")
hit.key(10, over(LS, spine_02=(-18, 0, -4)), "LINEAR")
hit.key(14, LS, "CONSTANT")

# What the cars must fit (CreatureArtTests checks the baked clips against these): standing, it's taller than a car;
# crawling it's under a doorway, squatting and smashing under the roof.
for name, poses in (("listen", [LS]), ("follow", [LOPE]), ("crawl", [CR]), ("squat", [SQ]), ("smash", [UPS, DOWN]), ("attack", [RAISE])):
    print("[dt] gaunt", name, "stands", round(max(height(p) for p in poses), 2), "m")

kit.build()
rig.bake(sk, [sleep, stir, follow, listen, attack, crawl, squat, smash, hit], plant=rig.feet_planter(sk, lowest=0.02))
print("[dt] gaunt", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "gaunt.glb", kit)
