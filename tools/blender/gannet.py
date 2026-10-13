"""THE GANNET (GDD §21 flank; docs/design/creatures/gannet.md §3; ARCHITECTURE §8 note 340): one enormous corrupted gannet.

"The strongest monsters are the ones where you can still tell what they used to be" (§26.5): a northern gannet's long
black-tipped wings, teardrop body and dagger face, then the scale and the wrongness. A 7 m wingspan; landed on a car roof
it stands 2.3 m tall and its wings overhang both edges. The Corruption grew what lets a gannet survive its dive: the beak
is a metre-long spear of serrated bone with a hooked tip and a row of tooth-like spikes along its lower edge, the skull
plated behind it like a helmet; the dive's cushioning air sacs have swollen into huge translucent bladders bulging out of
the throat and breast, lit orange from within by the train's fire (they swell when it folds, throb when it pins); the
black webbed feet have grown into hooked paddles the width of a man's chest. White plumage sooted and mottled brown-black
from living in train smoke, black primaries, the head's yellow wash gone sulphur, a pale staring ringed eye in black skin.
Contamination, not magic: the only light is the fire's, through the sacs.

How it's made (the Look Review: organic, not boxes): the body, the S of the neck, the head, the sacs and the legs are one
skin (rig.fuse: their union voxel-remeshed and QuadriFlowed, smooth-shaded), so the breast swells into the bladders and the
thighs vanish into the belly feathers with no seam; over it, raised bevelled plates on the skull, the spear (flat-shaded,
a curved culmen, the hook, the teeth), and the feathers: cupped, twisted, tapering cards layered out of the wings' trailing
edges (primaries, secondaries, tertials, two rows of coverts), the tail's wedge, the scapulars and the neck's hackles. The
wings' bones arc (a gull-wing at rest) and each feather rides between two of them, so a wing bends in a curve and its
primaries splay apart on the downstroke. Its colour is baked into one atlas by tools/models/recipes/gannet.py; built
alone (this script), it wears the shared tiling textures.

Pivot on the roof between its feet, head forward (-Z in the engine), standing 2.3 m to the top of its plated skull, the
wings spread 7 m in the bind pose. SK_Gannet. A large monster (GDD §27: 8,000-16,000 triangles).

Clips (§31: still, then abrupt): soar, circle, hang, fold, dive, stab, stuck, tearFree, climb, bank, swoop, land, pin,
peckWindup, peck, driven, hit, death. Where the clips put the bird about its origin (CreatureArt.GannetClip picks them by
the sim's GannetMode):
  * flying (soar, circle, hang, climb, bank, swoop, hit, the end of stab, tearFree and driven): the body level over the
    origin, about a metre up (where the sim flies it and enemies.json's body stands);
  * folded (the end of fold, dive, the start of stab, stuck and tearFree): the BEAK TIP at the origin, the bird up the dive
    line behind it, so the sim's dive point is where the spear is, and stuck, the spear is in the planks there;
  * down on a roof (land, pin, peckWindup, peck, the start of driven, death): stood on the origin's floor.

    blender -b --python tools/blender/gannet.py -- content/art/models/gannet.glb
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/blender/gannet.py -- out.glb)
    python tools/models/recipes/gannet.py      # the game's: this, with its colour baked into an atlas
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, along, hexc, mirror, noise3, over, smoothstep  # noqa: E402

rig.reset()

# The brief's numbers (docs/design/creatures/gannet.md §3), printed at the end and pinned by CreatureArtTests.
SPAN, HEIGHT, BEAK_LENGTH = 7.0, 2.3, 1.0

# The head's frame: from the back of the skull along the face to the beak's tip (u), across (x), and up out of the crown (v).
HEAD_O = Vector((0, 0.8, 2.1))
HEAD_D = Vector((0, 1.0, -0.12)).normalized()
HEAD_V = Vector((0, -HEAD_D.z, HEAD_D.y))
BEAK_TIP_U = 1.47


def head_at(u, x=0.0, v=0.0):
    return HEAD_O + HEAD_D * u + Vector((x, 0, 0)) + HEAD_V * v


BEAK_TIP = head_at(BEAK_TIP_U, 0, -0.08)

# The body's frame: the teardrop from the vent forward to the base of the neck, tipped up 24 degrees as it stands.
BODY_O = Vector((0, -0.62, 1.0))
BODY_D = Vector((0, 1.1, 0.5)).normalized()
BODY_V = Vector((0, -BODY_D.z, BODY_D.y))


def body_at(u, x=0.0, v=0.0):
    return BODY_O + BODY_D * u + Vector((x, 0, 0)) + BODY_V * v


# The right wing's joints at rest, root to tip: a gull-wing arc (the arm raised from the shoulder, the wrist forward of it,
# the hand swept back and drooping). The left mirrors it.
WING = [Vector((0.24, 0.44, 1.62)), Vector((0.46, 0.42, 1.7)), Vector((1.15, 0.3, 1.82)), Vector((2.0, 0.44, 1.86)),
        Vector((2.55, 0.36, 1.8)), Vector((3.35, 0.16, 1.64))]
WING_BONES = ["shoulder", "upperarm", "forearm", "hand", "tip"]


def skeleton():
    """SK_Gannet: root, pelvis, spine_01, chest, neck_01..02, head, jaw, the beak's tip (a socket), three sacs (the throat's
    and a breast lobe a side: they scale, swelling and throbbing), a tail of two, and a side each of wing (shoulder,
    upperarm, forearm, hand, tip: the primaries) and leg (thigh, shin, foot: the scaled tarsus, toes: the webbed paddle)."""
    b = [
        Bone("root", None, (0, 0, 0), (0, 0.3, 0)),
        Bone("pelvis", "root", (0, -0.5, 1.05), (0, -0.1, 1.22)),
        Bone("spine_01", "pelvis", (0, -0.1, 1.22), (0, 0.3, 1.4)),
        Bone("chest", "spine_01", (0, 0.3, 1.4), (0, 0.62, 1.6)),
        Bone("neck_01", "chest", (0, 0.58, 1.6), (0, 0.7, 1.86)),
        Bone("neck_02", "neck_01", (0, 0.7, 1.86), (0, 0.84, 2.06)),
        Bone("head", "neck_02", (0, 0.84, 2.06), tuple(head_at(0.45, 0, -0.02))),
        Bone("jaw", "head", tuple(head_at(0.24, 0, -0.1)), tuple(head_at(1.36, 0, -0.12))),
        Bone("beak_tip", "head", tuple(BEAK_TIP), tuple(BEAK_TIP + HEAD_D * 0.1), deform=False),
        Bone("sac_throat", "neck_01", (0, 0.92, 1.86), (0, 0.96, 1.5)),
        Bone("tail_01", "pelvis", (0, -0.5, 1.05), (0, -0.92, 0.9)),
        Bone("tail_02", "tail_01", (0, -0.92, 0.9), (0, -1.38, 0.76)),
    ]
    for side, sx in (("r", 1), ("l", -1)):
        m = Vector((sx, 1, 1))
        wing = [Vector((p.x * m.x, p.y, p.z)) for p in WING]
        parents = ["chest"] + [f"{n}_{side}" for n in WING_BONES[:-1]]
        b += [Bone(f"sac_{side}", "chest", (sx * 0.15, 0.62, 1.48), (sx * 0.17, 0.74, 1.08))]
        b += [Bone(f"{n}_{side}", parents[i], tuple(wing[i]), tuple(wing[i + 1])) for i, n in enumerate(WING_BONES)]
        b += [
            Bone(f"thigh_{side}", "pelvis", (sx * 0.26, -0.15, 1.12), (sx * 0.36, 0.08, 0.85)),
            Bone(f"shin_{side}", f"thigh_{side}", (sx * 0.36, 0.08, 0.85), (sx * 0.4, -0.08, 0.48)),
            Bone(f"foot_{side}", f"shin_{side}", (sx * 0.4, -0.08, 0.48), (sx * 0.42, 0.04, 0.08)),
            Bone(f"toes_{side}", f"foot_{side}", (sx * 0.42, 0.04, 0.08), (sx * 0.42, 0.46, 0.03)),
        ]
    return Skeleton("SK_Gannet", b)


sk = skeleton()
sk.build()
kit = rig.Kit(sk, "gannet")

# --- materials ----------------------------------------------------------------------------------------------
# Each region its own material: built alone they're the shared tiling textures (tinted against the textures' own warmth,
# fleece averaging (0.51, 0.48, 0.43), so the white is white); baked (tools/models/recipes/gannet.py), the names say what
# to paint: off-white plumage mottled sooty brown-black (densest on the back and the coverts), black primaries, the dusky
# trailing edge, the sulphur head, black skin, the pale veined sacs, the scaled black legs.
WHITE = Mat("fleece.gannet_white", hexc("#e4e2dc"), shine=0.08, tint=(1.68, 1.78, 1.96))
BACK = Mat("fleece.gannet_back", hexc("#b8b4ac"), shine=0.07, tint=(1.3, 1.38, 1.52))
BELLY = Mat("fleece.gannet_belly", hexc("#ecebe6"), shine=0.08, tint=(1.78, 1.88, 2.06))
COVERT = Mat("fungal_crust.gannet_covert", hexc("#d4d0c8"), shine=0.06, tint=(2.15, 2.3, 2.75))
FLIGHT = Mat("fleece.gannet_flight", hexc("#dcd8d0"), shine=0.1, tint=(1.6, 1.7, 1.86))
DUSKY = Mat("tar.gannet_dusky", hexc("#3a3430"), shine=0.15, tint=(0.75, 0.7, 0.68))
PRIMARY = Mat("tar.gannet_primary", hexc("#1e1a16"), shine=0.22, tint=(0.4, 0.37, 0.36))
SULPHUR = Mat("fleece.gannet_head", hexc("#d0a838"), shine=0.1, tint=(1.75, 1.4, 0.5))
FACE = Mat("tar.gannet_face", hexc("#141210"), shine=0.35, tint=(0.25, 0.23, 0.22))
PLATE = Mat("mineral_growth.gannet_plate", hexc("#c4aa78"), shine=0.4, tint=(2.3, 1.95, 1.25))
BONE = Mat("concrete_stain.gannet_beak", hexc("#d0c8b0"), shine=0.45, tint=(1.75, 1.62, 1.3))
TOOTH = Mat("skin.gannet_tooth", hexc("#d8ceb8"), shine=0.45, tint=(1.5, 1.42, 1.25))
MOUTH = Mat("flesh.gannet_mouth", hexc("#5a2420"), shine=0.5, tint=(0.75, 0.35, 0.3))
# The eye: pale, staring; lit a breath so it's found in the dark without glowing. (Kept out of the atlas: its own glass.)
EYE = Mat("glass_dirty.gannet_eye", hexc("#d4dee2"), shine=0.9, emissive=0.18, tint=(1.75, 1.9, 1.95))
PUPIL = Mat("tar.gannet_pupil", hexc("#050505"), shine=0.9, tint=(0.1, 0.1, 0.1))
# The sacs: translucent, stretched shiny, the fire's light coming through them (partly emissive).
SAC = Mat("flesh.gannet_sac", hexc("#f0b888"), shine=0.85, emissive=0.42, tint=(1.95, 1.2, 0.72))
LEG = Mat("pine_bark.gannet_leg", hexc("#1e1c1a"), shine=0.4, tint=(0.34, 0.33, 0.32))
WEB = Mat("leather.gannet_web", hexc("#1a1816"), shine=0.3, tint=(0.3, 0.28, 0.27))
TALON = Mat("tar.gannet_talon", hexc("#0e0c0a"), shine=0.6, tint=(0.18, 0.17, 0.16))


def h01(*k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 340, 1.0)


def axial(part, at, secs, sides, mat, bones, shape=None, cap0=False, cap1=False, fmat=None):
    """A loft through cross-sections along a frame's length: `at(u, x, v)` places a point, each section (u, half width, top,
    bottom, squareness) boxes it out the way `sections` does along y (squareness 1: an ellipse)."""
    rings, cents = [], []
    for (u, hw, top, bot, e) in secs:
        vc, hv = (top + bot) / 2, (top - bot) / 2
        ring = []
        for j in range(sides):
            ang = 2 * math.pi * j / sides
            sa, ca = math.sin(ang), math.cos(ang)
            p = at(u, math.copysign(abs(sa) ** e, sa) * hw, vc + math.copysign(abs(ca) ** e, ca) * hv)
            ring.append(Vector(shape(len(rings), j, ang, p)) if shape else p)
        rings.append(ring)
        cents.append(at(u, 0, vc))
    return part.loft(rings, mat, bones, centres=cents, cap0=cap0, cap1=cap1, fmat=fmat)


def finer(secs, n=2):
    out = []
    for a, b in zip(secs, secs[1:]):
        for k in range(n):
            t = k / n
            out.append(tuple(x + (y - x) * t for x, y in zip(a, b)))
    out.append(secs[-1])
    return out


def section_at(secs, u):
    for (u0, *a), (u1, *b) in zip(secs, secs[1:]):
        if u0 <= u <= u1:
            t = (u - u0) / (u1 - u0)
            return tuple(x + (y - x) * t for x, y in zip(a, b))
    return tuple(secs[0][1:]) if u < secs[0][0] else tuple(secs[-1][1:])


def quill(part, base, d, n, length, width, mat, bones, tipmat=None, tipfrac=0.0, cup=0.12, twist=0.0, bend=0.0, narrow=0.6,
          rows=4, sides=1, notch=0.0, seed=0):
    """One feather card: from `base` along `d`, its upper face to `n`. Tapered (broad at a third, to a point), the leading vane
    `narrow` of the trailing one; cupped (its edges `cup` of its width down), bent down along its length (`bend` of it) and
    twisted `twist` radians toward its tip; `notch` narrows its end (a primary's emarginated tip: the fingers). Two faces
    back to back a few millimetres apart, so it reads from above and below. With `tipmat`, the last `tipfrac` is that."""
    d = Vector(d).normalized()
    n0 = Vector(n)
    n0 = (n0 - d * n0.dot(d)).normalized()
    s0 = d.cross(n0).normalized()
    base = Vector(base)
    w_prof = [(0.0, 0.18), (0.3, 0.5), (0.7, 0.42 - 0.2 * notch), (0.9, 0.24 - 0.12 * notch), (1.0, 0.04)]

    def w_at(t):
        for (t0, w0), (t1, w1) in zip(w_prof, w_prof[1:]):
            if t0 <= t <= t1:
                return w0 + (w1 - w0) * (t - t0) / (t1 - t0)
        return 0.0

    ts = [i / rows for i in range(rows + 1)]
    across = [-narrow, -narrow * 0.5, 0.0, 0.5, 1.0] if sides > 1 else [-narrow, 0.0, 1.0]
    grid = []
    for t in ts:
        a = twist * t
        side = s0 * math.cos(a) + n0 * math.sin(a)
        nn = n0 * math.cos(a) - s0 * math.sin(a)
        c = base + d * (length * t) - nn * (bend * length * t * t)
        w = width * w_at(t)
        grid.append([c + side * (w * s) - nn * (cup * w * s * s) for s in across])
    thick = 0.005
    tile = mat.tile() or 1.0

    def uv(p):
        q = p - base
        return (q.dot(s0) / tile, q.dot(d) / tile)
    cut = 1.0 - tipfrac if tipmat is not None else 2.0
    top = [[part.add_v(p, bones) for p in row] for row in grid]
    bot = [[part.add_v(p - n0 * thick, bones) for p in row] for row in grid]
    for i in range(rows):
        m = tipmat if (ts[i] + ts[i + 1]) / 2 > cut else mat
        for j in range(len(across) - 1):
            q = [top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j]]
            part.face(q, [uv(part.v[k]) for k in q], m, None)
            qb = [bot[i][j], bot[i + 1][j], bot[i + 1][j + 1], bot[i][j + 1]]
            part.face(qb, [uv(part.v[k]) for k in qb], m, None)


def wing_point(f, sx=1):
    """A point along the right wing's chain (0 at the shoulder, 1 at the tip's end) and the bones round it, weighted."""
    lens = [(WING[i + 1] - WING[i]).length for i in range(len(WING) - 1)]
    total = sum(lens)
    d = f * total
    for i, L in enumerate(lens):
        if d <= L or i == len(lens) - 1:
            k = min(1.0, d / L)
            p = WING[i] + (WING[i + 1] - WING[i]) * k
            return Vector((p.x * sx, p.y, p.z)), i, k
        d -= L


def between(i, k, s, soft=0.3):
    """Weights for a point `k` along wing bone `i`: its own bone, blended into its neighbours near the joints."""
    names = [f"{n}_{s}" for n in WING_BONES]
    w = {names[i]: 1.0}
    if k < soft and i > 0:
        t = 0.5 * (1 - k / soft)
        w = {names[i]: 1 - t, names[i - 1]: t}
    elif k > 1 - soft and i < len(names) - 1:
        t = 0.5 * (1 - (1 - k) / soft)
        w = {names[i]: 1 - t, names[i + 1]: t}
    return w


# --- the skin: body, neck, head, sacs and legs, one continuous surface (rig.fuse) ---------------------------------------
body = kit.part("body")
# A teardrop: deep and round in the breast, tapering to the vent. (u along it, half width, top, bottom, squareness)
BODY = [(-0.04, 0.06, 0.05, -0.05, 1.0), (0.08, 0.19, 0.15, -0.16, 1.0), (0.28, 0.32, 0.26, -0.3, 0.95), (0.52, 0.42, 0.33, -0.41, 0.92),
        (0.78, 0.49, 0.38, -0.49, 0.92), (1.02, 0.51, 0.4, -0.53, 0.92), (1.22, 0.47, 0.4, -0.5, 0.95), (1.4, 0.37, 0.34, -0.38, 1.0),
        (1.54, 0.22, 0.24, -0.22, 1.0), (1.6, 0.08, 0.1, -0.08, 1.0)]
SPINE = [(0.0, "tail_01"), (0.15, "pelvis"), (0.45, "pelvis"), (0.65, "spine_01"), (0.9, "spine_01"), (1.1, "chest"), (1.6, "chest")]


def body_u(p):
    return (Vector(p) - BODY_O).dot(BODY_D)


def trunk(p):
    """Along the body; the haunches go with the thighs, the shoulders' tops with the wings' roots."""
    p = Vector(p)
    u = body_u(p)
    w = {}
    if u <= SPINE[0][0]:
        w = {SPINE[0][1]: 1.0}
    elif u >= SPINE[-1][0]:
        w = {SPINE[-1][1]: 1.0}
    else:
        for (u0, b0), (u1, b1) in zip(SPINE, SPINE[1:]):
            if u0 <= u <= u1:
                t = (u - u0) / (u1 - u0)
                w[b0] = w.get(b0, 0) + 1 - t
                w[b1] = w.get(b1, 0) + t
                break
    s = "r" if p.x > 0 else "l"
    v = (p - BODY_O).dot(BODY_V)
    if 0.25 < u < 0.75 and v < -0.15 and abs(p.x) > 0.15:
        k = smoothstep(-0.15, -0.4, v) * smoothstep(0.25, 0.4, u) * smoothstep(0.75, 0.6, u) * 0.5
        w = {b: x * (1 - k) for b, x in w.items()}
        w[f"thigh_{s}"] = w.get(f"thigh_{s}", 0) + k
    if 1.0 < u < 1.5 and v > 0.05 and abs(p.x) > 0.2:
        k = smoothstep(0.05, 0.3, v) * smoothstep(0.2, 0.42, abs(p.x)) * 0.5
        w = {b: x * (1 - k) for b, x in w.items()}
        w[f"shoulder_{s}"] = w.get(f"shoulder_{s}", 0) + k
    t = sum(w.values())
    return {b: x / t for b, x in w.items() if x / t > 0.02}


def plumage(i, j, a, p):
    """Soft lumps where the feathers lie in clumps (the fuse smooths them into the skin)."""
    q = Vector(p)
    return q + BODY_V * (0.015 * noise3(q, 11, 6.0)) + Vector((0.012 * noise3(q, 12, 5.0), 0, 0))


def body_mat(pts, n):
    up = n.dot(BODY_V)
    return BACK if up > 0.45 else BELLY if up < -0.3 else WHITE


axial(body, body_at, finer(BODY, 3), 28, WHITE, trunk, shape=plumage, cap0=True, cap1=True, fmat=body_mat)

# The neck: a long S into the skull (forward off the breast, up, and forward again under the head), thick as a gannet's.
NECK = [Vector((0, 0.4, 1.42)), Vector((0, 0.58, 1.56)), Vector((0, 0.65, 1.72)), Vector((0, 0.68, 1.88)), Vector((0, 0.77, 2.02)),
        Vector((0, 0.9, 2.1))]
neck_w = along("z", [(1.5, "chest"), (1.66, "neck_01"), (1.86, "neck_01"), (1.96, "neck_02"), (2.04, "neck_02"), (2.12, "head")])


def neck_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    return SULPHUR if c.z > 1.88 + 0.1 * max(0.0, n.y) else (BACK if n.y < -0.4 else WHITE)


body.tube(NECK, [(0.3, 0.32), (0.25, 0.26), (0.22, 0.22), (0.2, 0.2), (0.19, 0.18), (0.16, 0.15)], 20, WHITE, neck_w,
          ref=(0, -0.6, 0.8), fmat=neck_mat)

# The head: rounded, long, a gannet's; its front runs on under the spear's base.
SKULL = [(-0.02, 0.1, 0.1, -0.1, 1.0), (0.07, 0.15, 0.16, -0.15, 1.0), (0.18, 0.17, 0.19, -0.16, 1.0), (0.3, 0.155, 0.17, -0.14, 1.0),
         (0.4, 0.125, 0.14, -0.12, 1.0), (0.5, 0.1, 0.11, -0.1, 1.0)]


def skull_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    u = (c - HEAD_O).dot(HEAD_D)
    v = (c - HEAD_O).dot(HEAD_V)
    # The black skin: a line from under the eye forward to the gape, and the chin's stripe (the rest sulphur).
    if u > 0.22 and -0.12 < v < 0.0 or u > 0.32 and v < -0.06:
        return FACE
    return SULPHUR


axial(body, head_at, finer(SKULL, 2), 20, SULPHUR, "head", cap0=True, cap1=True, fmat=skull_mat)

# The sacs: the dive's air cushions swollen out of the throat and the breast (in the union, the skin bulges into them).
SAC_THROAT = Vector((0, 0.98, 1.7))
SAC_BREAST = {"r": Vector((0.15, 0.78, 1.28)), "l": Vector((-0.15, 0.78, 1.28))}


def bladder(c, radii, bone, seed):
    c = Vector(c)
    body.blob(tuple(c), radii, 18, 12, SAC, bone, smooth=True,
              shape=lambda i, j, a, th, p: p + (p - c) * (0.05 * noise3(p, 81 + seed, 4.0)) - Vector((0, 0, 0.1 * radii[2] * max(0.0, (c - p).z / radii[2]))))


bladder(SAC_THROAT, (0.21, 0.27, 0.29), "sac_throat", 1)
for s, c in SAC_BREAST.items():
    sx = 1 if s == "r" else -1
    bladder(c, (0.26, 0.26, 0.33), f"sac_{s}", 2 + (sx > 0))
    bladder(c + Vector((sx * 0.19, -0.08, -0.26)), (0.14, 0.14, 0.15), f"sac_{s}", 5 + (sx > 0))
    bladder((sx * 0.15, 0.82, 1.84), (0.09, 0.09, 0.1), "sac_throat", 7 + (sx > 0))

# The legs: the thighs lost in the belly feathers, the shins and the tarsi bare, black and scaled, to the ankle.
for sx, s in ((1, "r"), (-1, "l")):
    leg_w = along("z", [(0.1, f"foot_{s}"), (0.44, f"foot_{s}"), (0.52, f"shin_{s}"), (0.8, f"shin_{s}"), (0.92, f"thigh_{s}")])
    body.tube([(sx * 0.27, -0.12, 1.1), (sx * 0.34, 0.03, 0.9), (sx * 0.37, 0.03, 0.72), (sx * 0.4, -0.06, 0.5), (sx * 0.41, -0.03, 0.3),
               (sx * 0.42, 0.03, 0.1)], [0.17, 0.14, 0.1, 0.08, 0.068, 0.075], 12, LEG,
              lambda p, w=leg_w: {k: v for k, v in w(p).items()}, ref=(0, 1, 0),
              fmat=lambda pts, n: LEG if sum(p.z for p in pts) / len(pts) < 0.66 else WHITE)

# --- the plates: raised, bevelled, a helmet over the crown and the brow, running down onto the spear's base ----------------
plates = kit.part("plates", smooth=False)


def skull_point(u, ang):
    """A point on the skull at `u` along it, `ang` round from the crown (+ to the right), and its way out."""
    hw, top, bot, e = section_at(SKULL, u)
    vc, hv = (top + bot) / 2, (top - bot) / 2
    p = head_at(u, math.sin(ang) * hw, vc + math.cos(ang) * hv)
    n = (Vector((math.sin(ang) / hw, 0, 0)) + HEAD_V * (math.cos(ang) / hv)).normalized()
    return p, n


def plate(u0, u1, a0, a1, thick=0.028, bone="head"):
    """A plate lying on the skull from u0 to u1 and a0 to a1 round it: its foot sunk in the skin, its face raised `thick`
    and inset (a bevel all round), so each one's edge catches the light."""
    corners = [(u0, a0), (u1, a0), (u1, a1), (u0, a1)]
    foot, face = [], []
    cu, ca = (u0 + u1) / 2, (a0 + a1) / 2
    for u, a in corners:
        p, n = skull_point(u, a)
        foot.append(p - n * 0.01)
        q, m = skull_point(cu + (u - cu) * 0.7, ca + (a - ca) * 0.7)
        face.append(q + m * thick)
    fi = [plates.add_v(p, bone) for p in foot]
    ti = [plates.add_v(p, bone) for p in face]
    tile = PLATE.tile() or 1.0
    inside = sum(foot, Vector()) / 4 - skull_point(cu, ca)[1] * 0.05
    plates.face(ti, [(plates.v[k].x / tile, plates.v[k].y / tile) for k in ti], PLATE, False, outward=inside)
    for j in range(4):
        q = [fi[j], fi[(j + 1) % 4], ti[(j + 1) % 4], ti[j]]
        plates.face(q, [(plates.v[k].x / tile, plates.v[k].z / tile) for k in q], PLATE, False, outward=inside)


# The crown: a ridge of plates down the middle, flanked by rows that shrink toward the cheeks; the brow's heavy ridge.
for k, (u0, u1) in enumerate(((-0.04, 0.1), (0.08, 0.22), (0.2, 0.33), (0.31, 0.44))):
    plate(u0, u1, -0.38, 0.38, 0.03 - 0.003 * k)
    for sx in (-1, 1):
        plate(u0 + 0.02, u1, sx * 0.42, sx * 0.95, 0.024 - 0.003 * k)
for sx in (-1, 1):
    plate(0.17, 0.4, sx * 0.95, sx * 1.3, 0.045)      # the brow over the eye
    plate(0.0, 0.17, sx * 1.15, sx * 1.75, 0.022)     # the cheek behind it
# Down onto the spear: the culmen's base plated over (the bone running into the helmet).
for k, (u0, u1) in enumerate(((0.44, 0.56), (0.54, 0.66))):
    plates.slab([head_at(u0, -0.07 + 0.01 * k, 0.12 - 0.02 * k), head_at(u0, 0.07 - 0.01 * k, 0.12 - 0.02 * k),
                 head_at(u1, 0.05 - 0.01 * k, 0.1 - 0.02 * k), head_at(u1, -0.05 + 0.01 * k, 0.1 - 0.02 * k)], 0.03, PLATE, "head", down=-HEAD_V)

# --- the eyes: pale and staring, a black pupil, in a ring of black skin under the brow --------------------------------------
eyes = kit.part("eyes")
for sx in (-1, 1):
    eye_c = head_at(0.27, sx * 0.152, 0.035)
    out = Vector((sx, 0.15, 0.05)).normalized()
    R = rig.Matrix.Rotation(sx * math.pi / 2, 4, "Y")
    eyes.blob(tuple(eye_c - out * 0.004), (0.095, 0.09, 0.026), 14, 3, FACE, "head", rot=R)
    eyes.blob(tuple(eye_c + out * 0.016), (0.066, 0.064, 0.026), 14, 4, EYE, "head", rot=R)
    eyes.blob(tuple(eye_c + out * 0.038), (0.02, 0.022, 0.009), 8, 2, PUPIL, "head", rot=R)

# --- the spear: a metre of serrated bone, the culmen curved, the tip hooked, the lower edge lined with teeth; it gapes ------
beak = kit.part("beak", smooth=False)


def culmen(u, x, v):
    """The upper mandible's line: rising a little off the forehead, then a long gentle fall to the hook, which turns down
    hard over the jaw's tip."""
    rise = 0.02 * math.sin(min(1.0, (u - 0.36) / 0.5) * math.pi) if u > 0.36 else 0.0
    hook = -0.42 * max(0.0, u - 1.3) ** 2 * 4
    return head_at(u, x, v + rise + hook)


# (u, half width, top, bottom, squareness): ridged on top, its cutting edge at v = -0.025.
UPPER = [(0.36, 0.125, 0.13, -0.03, 0.8), (0.48, 0.11, 0.115, -0.026, 0.75), (0.64, 0.092, 0.096, -0.024, 0.72),
         (0.82, 0.075, 0.078, -0.022, 0.72), (1.0, 0.06, 0.062, -0.022, 0.72), (1.16, 0.046, 0.048, -0.022, 0.75),
         (1.28, 0.035, 0.036, -0.026, 0.8), (1.37, 0.026, 0.024, -0.04, 0.85), (1.43, 0.016, 0.01, -0.058, 0.9)]
axial(beak, culmen, finer(UPPER, 2), 10, BONE, "head", cap0=True, cap1="point")
LOWER = [(0.3, 0.12, -0.03, -0.16, 0.8), (0.48, 0.1, -0.03, -0.14, 0.75), (0.7, 0.08, -0.028, -0.118, 0.75),
         (0.92, 0.062, -0.026, -0.096, 0.75), (1.12, 0.046, -0.026, -0.078, 0.78), (1.28, 0.03, -0.028, -0.064, 0.8),
         (1.36, 0.018, -0.03, -0.056, 0.85)]
axial(beak, head_at, finer(LOWER, 2), 10, BONE, "jaw", cap0=True, cap1="point")
# Inside: the palate, the mouth's floor and the tongue; a fold at the gape's corner that stretches as it opens.
for sx in (-1, 1):
    beak.slab([head_at(0.32, sx * 0.075, -0.035), head_at(1.15, sx * 0.03, -0.028), head_at(1.15, 0, -0.03), head_at(0.32, 0, -0.04)],
              0.006, MOUTH, "head", down=(0, 0, -1))
beak.slab([head_at(0.32, -0.07, -0.05), head_at(0.32, 0.07, -0.05), head_at(1.1, 0.025, -0.04), head_at(1.1, -0.025, -0.04)],
          0.012, MOUTH, "jaw", down=(0, 0, 1))
for sx in (-1, 1):
    corner = [head_at(0.22, sx * 0.11, -0.03), head_at(0.4, sx * 0.1, -0.03), head_at(0.4, sx * 0.095, -0.075), head_at(0.22, sx * 0.105, -0.09)]

    def gape_w(p):
        v = (Vector(p) - HEAD_O).dot(HEAD_V)
        k = min(1.0, max(0.0, (-0.035 - v) / 0.05))
        return {"head": 1 - k, "jaw": k} if 0 < k < 1 else ({"head": 1.0} if k <= 0 else {"jaw": 1.0})
    beak.slab(corner, 0.012, MOUTH, gape_w, down=(-sx, 0, 0))

teeth = kit.part("teeth", smooth=False)


def tooth(at, d, length, r, bone, curve=0.25):
    """A spike, raked and curved forward at its point."""
    at, d = Vector(at), Vector(d).normalized()
    fwd = HEAD_D * curve
    teeth.tube([at - d * 0.01, at + d * (length * 0.55), at + (d + fwd).normalized() * length], [r, r * 0.55, 0.002], 4, TOOTH, bone,
               ref=(0, 1, 0), cap1="point")


for sx in (-1, 1):
    for k in range(11):
        u = 0.42 + k * 0.083
        mid = 1 - abs(k - 6) / 6.5
        length = 0.04 + 0.08 * mid + 0.015 * h01(k, sx)
        sec = section_at(LOWER, u)
        at = head_at(u, sx * sec[0] * 0.55, sec[2] + 0.012)
        d = HEAD_V * -1.0 + HEAD_D * 0.5 + Vector((sx * 0.25, 0, 0))
        tooth(at, d, length, 0.016 + 0.007 * mid, "jaw")
    for k in range(10):
        u = 0.45 + k * 0.088
        sec = section_at(UPPER, u)
        at = culmen(u, sx * sec[0] * 0.78, -0.022)
        tooth(at, HEAD_V * -1.0 + HEAD_D * 0.3, 0.03 + 0.014 * h01(k, sx + 4), 0.01, "head", curve=0.1)

# --- the wings: 7 m across, an arc of bone and a fan of layered feathers out of the trailing edge ----------------------------
wings = kit.part("wings")
feathers = kit.part("feathers")
# The arm: the leading edge, a smooth loft along the bones' arc (its root sunk in the shoulder), thick at the shoulder and
# the wrist, tapering to the hand; its chord runs back from the bones.
ARM_F = [0.0, 0.06, 0.12, 0.2, 0.3, 0.38, 0.46, 0.52, 0.58, 0.66, 0.74]
ARM_R = [(0.24, 0.14), (0.27, 0.11), (0.3, 0.095), (0.29, 0.085), (0.26, 0.075), (0.24, 0.07), (0.22, 0.072), (0.2, 0.068), (0.17, 0.055),
         (0.12, 0.042), (0.06, 0.02)]
for sx, s in ((1, "r"), (-1, "l")):
    pts, ws = [], []
    for f in ARM_F:
        p, i, k = wing_point(f, sx)
        pts.append(p + Vector((0, -0.08, -0.01)))
        ws.append(between(i, k, s))

    def arm_w(p, pts=pts, ws=ws):
        # (Each ring takes its centre's weights: the nearest centre along the arm.)
        j = min(range(len(pts)), key=lambda q: (pts[q] - Vector(p)).length)
        return dict(ws[j])
    wings.tube(pts, ARM_R, 12, COVERT, arm_w, ref=(0, 0.25, 1), cap1="point",
               fmat=lambda pts_, n: COVERT if n.z > 0.2 else FLIGHT)

    up = Vector((0, 0, 1))
    # Primaries: ten, out of the hand and the tip, long, black, emarginated toward the end so the tip splits into fingers;
    # each rides between the hand and the tip by how far out it is, so they splay as the tip turns on the downstroke.
    for i in range(10):
        f = i / 9
        root, bi, bk = wing_point(0.72 - 0.15 * f, sx)
        a = math.radians(3 + 4.6 * i)
        d = Vector((sx * math.cos(a), -math.sin(a), -0.1 - 0.04 * f))
        w = {f"tip_{s}": 1 - f, f"hand_{s}": f} if f < 1 else {f"hand_{s}": 1.0}
        quill(feathers, root + Vector((0, -0.04, -0.02 - 0.006 * i)), d, up, 1.04 - 0.035 * i, 0.17 + 0.006 * i, PRIMARY,
              {k: v for k, v in w.items() if v > 0}, cup=0.14, twist=sx * (0.32 - 0.02 * i), bend=0.06, narrow=0.45,
              notch=0.7 if i < 5 else 0.2, sides=2, seed=i + 20 * (sx > 0))
    # Secondaries: along the forearm, pointing back, white, sooted toward their dusky tips.
    for i in range(14):
        f = i / 13
        root, bi, bk = wing_point(0.6 - 0.32 * f, sx)
        a = math.radians(84 + 14 * f + 4 * (h01(i, sx) - 0.5))
        d = Vector((sx * math.cos(a), -math.sin(a), -0.06))
        quill(feathers, root + Vector((0, -0.12, -0.03 - 0.004 * (i % 2))), d, up, 0.6 + 0.05 * h01(i, sx + 2), 0.22, FLIGHT,
              between(bi, bk, s, 0.4), tipmat=DUSKY, tipfrac=0.28, cup=0.1, twist=sx * 0.12, bend=0.04, narrow=0.55,
              seed=40 + i + 20 * (sx > 0))
    # Tertials: on the upper arm, long and pale, lying back over the body's side.
    for i in range(6):
        f = i / 5
        root, bi, bk = wing_point(0.27 - 0.17 * f, sx)
        a = math.radians(98 + 10 * f)
        d = Vector((sx * math.cos(a), -math.sin(a), -0.08))
        quill(feathers, root + Vector((0, -0.12, -0.02)), d, up, 0.56 - 0.06 * f, 0.24, FLIGHT, between(bi, bk, s, 0.4), tipmat=DUSKY,
              tipfrac=0.2, cup=0.1, twist=-sx * 0.1, bend=0.03, seed=70 + i + 20 * (sx > 0))
    # Greater coverts over the flight feathers' roots, then the median coverts over theirs: mottled, sooted.
    for row, (count, f0, f1, L, W, lift, back) in enumerate(((16, 0.72, 0.12, 0.34, 0.2, 0.025, -0.05), (14, 0.66, 0.1, 0.22, 0.17, 0.05, 0.0))):
        for i in range(count):
            f = i / (count - 1)
            root, bi, bk = wing_point(f0 + (f1 - f0) * f, sx)
            a = math.radians(70 + 26 * f)
            d = Vector((sx * math.cos(a), -math.sin(a), -0.05))
            quill(feathers, root + Vector((0, back, lift)), d, up, L + 0.06 * f, W, COVERT, between(bi, bk, s, 0.4), cup=0.12, twist=sx * 0.08,
                  bend=0.08, seed=100 + row * 30 + i + 20 * (sx > 0))
    # Primary coverts over the primaries' roots, dark.
    for i in range(7):
        f = i / 6
        root, bi, bk = wing_point(0.84 - 0.2 * f, sx)
        a = math.radians(14 + 10 * i)
        d = Vector((sx * math.cos(a), -math.sin(a), -0.05))
        quill(feathers, root + Vector((0, -0.02, 0.03)), d, up, 0.36, 0.16, DUSKY, between(bi, bk, s, 0.4), cup=0.1, bend=0.06,
              seed=160 + i + 20 * (sx > 0))

# --- the tail: a wedge of stiff feathers, fanned between its two bones, sooted at the tips --------------------------------
for i in range(12):
    f = i / 11 - 0.5
    root = Vector((f * 0.24, -0.84, 0.94 + 0.004 * abs(i - 5.5)))
    d = Vector((f * 0.75, -1.0, -0.3))
    L = 0.66 - 0.38 * abs(f) ** 1.5
    w = 0.85 - 0.5 * abs(f)
    quill(feathers, root, d, Vector((0, -0.3, 1.0)), L, 0.15, WHITE if abs(f) < 0.3 else BACK, {"tail_02": w, "tail_01": 1 - w},
          tipmat=DUSKY, tipfrac=0.22, cup=0.08, twist=f * 0.4, bend=0.05, seed=180 + i)
for i in range(7):
    f = i / 6 - 0.5
    quill(feathers, (f * 0.3, -0.56, 1.05), Vector((f * 0.5, -1.0, -0.34)), Vector((0, -0.3, 1.0)), 0.44, 0.17, COVERT,
          {"tail_01": 0.7, "pelvis": 0.3}, cup=0.1, bend=0.06, rows=3, seed=200 + i)

# --- scapulars and hackles: feathers lying back over the shoulders and the back, and the ruff down the neck ---------------
for row in range(4):
    for k in range(9):
        a = (k + 0.5 * (row % 2) - 4) * 0.22
        u = 0.55 + row * 0.2
        hw, top, bot, e = section_at(BODY, u)
        vc, hv = (top + bot) / 2, (top - bot) / 2
        p = body_at(u, math.sin(a) * hw, vc + math.cos(a) * hv)
        n = (Vector((math.sin(a) / hw, 0, 0)) + BODY_V * (math.cos(a) / hv)).normalized()
        d = (-BODY_D - n * (-BODY_D).dot(n)).normalized()
        quill(feathers, p - d * 0.04 - n * 0.01, d, n, 0.32 + 0.08 * h01(row, k, 3), 0.22, BACK if h01(row, k) > 0.4 else COVERT, trunk(p),
              cup=0.08, bend=-0.04, rows=2, seed=300 + row * 20 + k)
for k in range(14):
    t = k / 13
    a = (k % 5 - 2) * 0.6
    c = NECK[4] + (NECK[1] - NECK[4]) * t
    n = Vector((math.sin(a), -0.65 * math.cos(a), 0.75 * math.cos(a))).normalized()
    p = c + n * (0.17 + 0.07 * t)
    d = Vector((0, -0.45, -1.0)).normalized()
    d = (d - n * d.dot(n)).normalized()
    quill(feathers, p, d, n, 0.22 + 0.08 * t, 0.16, SULPHUR if t < 0.35 else (BACK if k % 2 else WHITE), neck_w(p), cup=0.06,
          rows=2, seed=500 + k)

# --- the feet: black webbed paddles the width of a chest, the toes knuckled with scutes, the talons hooked ------------------
feet = kit.part("feet")
for sx, s in ((1, "r"), (-1, "l")):
    ankle = Vector((sx * 0.42, 0.04, 0.07))
    tips = []
    for k, (ang, L) in enumerate(((-26, 0.44), (0, 0.5), (26, 0.44), (150, 0.2))):
        a = math.radians(ang) * sx
        d = Vector((math.sin(a), math.cos(a), 0)).normalized()
        if ang == 150:
            d = Vector((-sx * 0.35, -1.0, 0)).normalized()
        bone = f"toes_{s}" if ang != 150 else f"foot_{s}"
        segs = [ankle + d * (L * t) + Vector((0, 0, -0.035 * min(1.0, t * 1.6))) for t in (0.02, 0.2, 0.38, 0.56, 0.74, 0.9, 1.0)]
        # Knuckled: each scute a swelling along the toe.
        radii = [0.055, 0.046, 0.04, 0.043, 0.035, 0.032, 0.026]
        feet.tube(segs, radii, 6, LEG, bone, ref=(0, 0, 1), cap0=True)
        tips.append(segs[-1])
        hook = [segs[-1] - d * 0.01, segs[-1] + d * 0.06 + Vector((0, 0, 0.004)), segs[-1] + d * 0.12 + Vector((0, 0, -0.012)),
                segs[-1] + d * 0.15 + Vector((0, 0, -0.04)), segs[-1] + d * 0.155 + Vector((0, 0, -0.062))]
        hook = [Vector((h.x, h.y, max(h.z, 0.004))) for h in hook]
        feet.tube(hook, [0.028, 0.024, 0.017, 0.009, 0.002], 6, TALON, bone, ref=(0, 0, 1), cap1="point")
    # The webs between the forward toes: leathery, sagging between them.
    for a_, b_ in ((0, 1), (1, 2)):
        # A shallow curved sheet: rows across from one toe to the next, sagging in the middle.
        rows = []
        for t in (0.0, 0.35, 0.7, 1.0):
            pa = ankle + (tips[a_] - ankle) * max(t, 0.02)
            pb = ankle + (tips[b_] - ankle) * max(t, 0.02)
            mid_sag = 0.25 * t
            rows.append([pa + Vector((0, 0, 0.004)), (pa + pb) * 0.5 + (ankle - (pa + pb) * 0.5) * mid_sag + Vector((0, 0, -0.006)),
                         pb + Vector((0, 0, 0.004))])
        top = [[feet.add_v(p, f"toes_{s}") for p in r] for r in rows]
        bot = [[feet.add_v(p - Vector((0, 0, 0.008)), f"toes_{s}") for p in r] for r in rows]
        tile = WEB.tile() or 1.0
        for i in range(3):
            for j in range(2):
                q = [top[i][j], top[i][j + 1], top[i + 1][j + 1], top[i + 1][j]]
                feet.face(q, [(feet.v[k_].x / tile, feet.v[k_].y / tile) for k_ in q], WEB, None, outward=feet.v[q[0]] - Vector((0, 0, 0.5)))
                qb = [bot[i][j], bot[i][j + 1], bot[i + 1][j + 1], bot[i + 1][j]]
                feet.face(qb, [(feet.v[k_].x / tile, feet.v[k_].y / tile) for k_ in qb], WEB, None, outward=feet.v[qb[0]] + Vector((0, 0, 0.5)))

# One skin: the body, the neck, the head, the sacs and the legs grown into one another, no seam where they meet (rig.fuse).
kit.fuse("skin", ["body"], voxel=0.008, faces=3000, lose=0.03)
kit.build()


# --------------------------------------------------------------------------------------------------------------
# Clips. Angles are the armature's axes (rig.rot): +X tips a forward-pointing bone's end up (a head raised) and swings a
# hanging leg forward, and twists a wing about its length leading edge up; +Y lowers the right wing (the left mirrored);
# +Z turns towards -X, sweeping the right wing forward.

# Pitches at rest (degrees up from level, the bones' own): the body's axis, the neck's two bones, the beak.
REST_BODY, REST_NECK1, REST_NECK2, REST_BEAK = 24.4, 65.2, 55.0, -6.8


def chain(body, neck1, neck2, beak, roll=0.0, arch=0.0, turn=0.0):
    """A pose with the body, the neck's two bones and the beak at these pitches (degrees from level, absolute), the body
    rolled `roll` (right wing down +), the chest arched `arch` and the neck turned `turn` (to its left +)."""
    pel = body - REST_BODY
    n1 = neck1 - REST_NECK1 - pel - arch
    n2 = neck2 - REST_NECK2 - pel - arch - n1
    hd = beak - REST_BEAK - pel - arch - n1 - n2
    return {"pelvis": (pel, roll, 0), "chest": (arch, 0, 0), "neck_01": (n1, 0, turn * 0.4), "neck_02": (n2, 0, turn * 0.35),
            "head": (hd, 0, turn * 0.25)}


def at(pose, bone, which="head"):
    return rig.pose_points(sk, pose, [(bone, which)])[0]


def moved(pose, by):
    p = dict(pose)
    x, y, z = p.get("root@loc", (0, 0, 0))
    p["root@loc"] = (x + by[0], y + by[1], z + by[2])
    return p


def tip_at(pose, where):
    """The pose shifted so the beak's tip is at `where` (the folded clips' origin)."""
    return moved(pose, Vector(where) - at(pose, "beak_tip"))


FLY_CENTRE = Vector((0, 0, 1.05))


def flying(pose):
    """The pose shifted so the body's middle is FLY_CENTRE over the origin (where the sim flies it)."""
    return moved(pose, FLY_CENTRE - at(pose, "spine_01"))


def plant(pose, s, ankle, knee=None):
    """A leg stood with its ankle (the toes' root) at `ankle`: the tarsus hung as it is at rest, the toes flat; `knee`, a
    point the knee is drawn toward (out to the side, clear of the belly, for a foot put forward)."""
    foot = sk[f"foot_{s}"]
    heel = Vector(ankle) - (foot.tail - foot.head)
    # (A bird's knee is forward and its shin goes back from it, a hind leg's: it folds the other way from an arm.)
    p = rig.reach(sk, pose, f"thigh_{s}", f"shin_{s}", heel, elbow_axis=0, bend=-1, pole=knee)
    p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", 0, 0, 0)
    p[f"toes_{s}"] = rig.hang(sk, p, f"toes_{s}", 0, 0, 0)
    return p


def legs_to(pose, heel, foot, toes):
    """Both legs posed in `pose`'s body: each heel reached to `heel` (the right's; the left's mirrored) and the tarsus and
    the toes hung at world rotations `foot`, `toes`. Returns the legs' local rotations alone, to lay over any body pitch
    (they're the pelvis's children: relative to it they're the same)."""
    out = {}
    p = dict(pose)
    for s, sx in (("r", 1), ("l", -1)):
        p = rig.reach(sk, p, f"thigh_{s}", f"shin_{s}", Vector((sx * heel[0], heel[1], heel[2])), elbow_axis=0, bend=-1)
        p[f"foot_{s}"] = rig.hang(sk, p, f"foot_{s}", foot[0], sx * foot[1], sx * foot[2])
        p[f"toes_{s}"] = rig.hang(sk, p, f"toes_{s}", toes[0], sx * toes[1], sx * toes[2])
        for b in ("thigh", "shin", "foot", "toes"):
            out[f"{b}_{s}"] = p[f"{b}_{s}"]
    return out


def solve(pose, joints, cost, steps=(16, 8, 4, 2, 1)):
    """Coordinate descent over some joints' angles (bone, axis index) to bring `cost(pose)` down: deterministic."""
    p = dict(pose)
    best = cost(p)
    for step in steps:
        improved = True
        while improved:
            improved = False
            for bone, k in joints:
                for sgn in (1, -1):
                    q = dict(p)
                    v = list(q.get(bone, (0, 0, 0)))
                    v[k] += sgn * step
                    q[bone] = tuple(v)
                    c = cost(q)
                    if c < best - 1e-6:
                        best, p, improved = c, q, True
    return p


def beak_to(pose, target, down=None):
    """The neck and head bent so the beak's tip is at `target` (and, with `down`, the beak pitched about that far down)."""
    target = Vector(target)

    def cost(q):
        c = (at(q, "beak_tip") - target).length
        if down is not None:
            d = (at(q, "beak_tip") - at(q, "head")).normalized()
            c += 0.004 * abs(math.degrees(math.asin(max(-1, min(1, d.z)))) + down)
        return c
    return solve(pose, [("neck_01", 0), ("neck_02", 0), ("head", 0), ("neck_01", 2), ("neck_02", 2)], cost)


def wings(upper=(0, 0, 0), fore=(0, 0, 0), hand=(0, 0, 0), tip=(0, 0, 0), shoulder=(0, 0, 0)):
    """Both wings, the right given and the left its mirror."""
    return mirror({"shoulder_r": shoulder, "upperarm_r": upper, "forearm_r": fore, "hand_r": hand, "tip_r": tip})


def swollen(k, throat=None):
    """The sacs swollen by `k` (1 at rest): the fold's tell, the pin's throb."""
    t = throat if throat is not None else k
    return {"sac_throat@scale": (t, t, t), "sac_r@scale": (k, k, k), "sac_l@scale": (k, k, k)}


def mix(a, b, t):
    """Two poses mixed (angles linearly, a leg's solved rotations by slerp)."""
    out = {}
    for k in set(a) | set(b):
        va, vb = a.get(k, (0, 0, 0)), b.get(k, (0, 0, 0))
        if isinstance(va, rig.Quaternion) or isinstance(vb, rig.Quaternion):
            qa = va if isinstance(va, rig.Quaternion) else rig.rot(*va)
            qb = vb if isinstance(vb, rig.Quaternion) else rig.rot(*vb)
            out[k] = qa.slerp(qb, t)
        else:
            out[k] = tuple(x + (y - x) * t for x, y in zip(va, vb))
    return out


def merge(*poses):
    out = {}
    for p in poses:
        out.update(p)
    return out


# Legs tucked up under the belly in flight (solved in the level body: the heel under the hip, the tarsus forward along the
# belly and the paddle folded back under it: a dark bundle under the white, nothing trailing to stick out of the dive).
TUCK = legs_to(chain(0, 22, 4, -16), (0.3, -0.3, 0.42), (80, 0, 0), (0, 0, 180))
# Legs let down, dangling (climbing off, driven off): the heels under the hips, the paddles hanging, half folded.
DANGLE = legs_to(chain(0, 22, 4, -16), (0.36, -0.2, 0.42), (-20, 0, 0), (-70, 0, 0))
# Wings spread soaring: a slight dihedral, the hands a little swept and the primaries' tips lifted.
SPREAD = wings(upper=(2, -6, 0), fore=(0, 3, 3), hand=(1, 3, -5), tip=(3, -4, -3))
# Wings mantled, stood: the shoulders hunched up, the wrists high and the hands down to the roof each side, a tent over what
# it's got (overhanging both edges of a car).
MANTLE = wings(upper=(18, -42, 26), fore=(-6, 64, 8), hand=(-4, 36, -8), tip=(0, 14, -4))
# The dart: swept right back along the body, the wrists out from its flanks, the primaries trailing past the tail.
# (Swept back in their own plane, joint by joint, into an arrowhead: the wrists out past the flanks, the primaries trailing
# a metre past the tail. The plunge's silhouette, and nothing of the wing laid across the body.)
DART_WINGS = wings(upper=(0, -10, -48), fore=(0, 0, -18), hand=(0, 0, -10), tip=(0, 0, -6))
TAIL_FAN = {"tail_01": (6, 0, 0), "tail_02": (4, 0, 0)}


def flap(t, lift=1.0, sweep=1.0):
    """A wingbeat at `t` (0..1, 0 at the top): the downstroke to 0.55, the upstroke with the hands swept back."""
    a = math.cos(2 * math.pi * t)
    up = max(0.0, -math.sin(2 * math.pi * t))
    down = max(0.0, math.sin(2 * math.pi * t))
    return wings(upper=(-6 * math.sin(2 * math.pi * t), (-12 - 40 * a) * lift, 4 * math.sin(2 * math.pi * t)),
                 fore=(0, -10 * math.cos(2 * math.pi * (t - 0.1)) * lift, 10 * up * sweep),
                 hand=(0, -14 * math.cos(2 * math.pi * (t - 0.18)) * lift, -26 * up * sweep),
                 # (The tip turned forward on the downstroke: its primaries, each between the hand and the tip, splay apart.)
                 tip=(4 * up, -8 * math.cos(2 * math.pi * (t - 0.25)) * lift, -6 * up * sweep + 12 * down * lift))


FLY = merge(chain(0, 22, 4, -16), TUCK, TAIL_FAN)
DEBUG = bool(os.environ.get("GANNET_DEBUG"))


def show(name, pose):
    if DEBUG:
        pts = {b: at(pose, b, w) for b, w in (("beak_tip", "head"), ("spine_01", "head"), ("tip_r", "tail"), ("tip_l", "tail"), ("hand_r", "head"),
                                              ("toes_r", "tail"), ("toes_l", "tail"), ("tail_02", "tail"), ("head", "head"))}
        print(f"[dbg] {name}: " + ", ".join(f"{k}=({v.x:.2f},{v.y:.2f},{v.z:.2f})" for k, v in pts.items()))


# --- soar (2 s): wings spread and still, riding the plume; the head turning, looking down at the roofs ------------------
SOAR = flying(merge(FLY, SPREAD))
show("soar", SOAR)
soar = Clip("soar")
for f, (turn, look) in enumerate(((0, 0), (14, -6), (14, -6), (-10, -10), (-10, -10))):
    p = flying(merge(chain(0, 22, 4, -16 + look, turn=turn), TUCK, TAIL_FAN, SPREAD))
    p["tip_r"] = (3 + (2 if f == 2 else 0), -4, -3)
    soar.key(f * 12, p, "CONSTANT" if f in (1, 3) else "BEZIER")
soar.close(60)

# --- circle (4 s, from and back to the soar): banks into its turn round the train, the head into it, a wingbeat --------
circle = Clip("circle")
circle.key(0, SOAR)
BANKED = flying(merge(chain(0, 22, 4, -20, roll=-26, turn=18), TUCK, SPREAD, {"tail_01": (6, 0, 8), "tail_02": (4, -10, 6)},
                      wings(upper=(0, -10, 0), hand=(2, 6, -8))))
circle.key(20, BANKED)
circle.key(40, merge(BANKED, flap(0.25, 0.6)))
circle.key(52, merge(BANKED, flap(0.75, 0.6)))
circle.key(64, BANKED)
circle.key(96, SOAR)
circle.close(120)
show("circle", BANKED)

# --- hang (1 s): kiting still over a walker, head down, the calls stopped; the wings trembling to hold it there ----------
HANG = flying(merge(chain(-48, -58, -78, -88), TUCK, {"tail_01": (-18, 0, 0), "tail_02": (-12, 0, 0)},
                    wings(upper=(36, -22, 12), fore=(4, 12, 4), hand=(0, 8, -6), tip=(4, 4, 0))))
show("hang", HANG)
hang = Clip("hang")
for f in range(0, 30, 6):
    k = math.sin(2 * math.pi * f / 30)
    hang.key(f, merge(HANG, wings(upper=(36, -22 - 6 * k, 12), fore=(4, 12 + 4 * k, 4), hand=(0, 8 + 6 * k, -6), tip=(4, 4 + 6 * k, 0))), "LINEAR")
hang.close(30)

# --- fold (0.4 s, once): the wings swept back, the sacs swelling, the spear down its line; by the end its tip is the origin
DART = tip_at(merge(chain(-75, -75, -75, -75), TUCK, DART_WINGS, {"tail_01": (0, 0, 0), "tail_02": (0, 0, 0)}, swollen(1.22, 1.28)), (0, 0, 0))
show("dart", DART)
fold = Clip("fold", loop=False)
fold.key(0, HANG)
fold.key(4, merge(mix(HANG, DART, 0.45), wings(upper=(18, -30, -26), fore=(2, 8, -8), hand=(0, 8, -6))), "LINEAR")
fold.key(12, DART)
# --- dive (0.5 s): the dart held, shuddering in the wind of it, the bladders swollen tight --------------------------------
dive = Clip("dive")
for f, k in enumerate((0, 1, -1, 0.5, 0)):
    dive.key(f * 3, merge(DART, wings(upper=(0, -10 + 1.5 * k, -48), fore=(0, 0, -18), hand=(0, -2 * k, -10), tip=(0, 3 * k, -6)),
                          swollen(1.22 + 0.03 * k, 1.28 - 0.03 * k)), "LINEAR")
dive.close(15)

# --- stab (0.5 s, once): the spear in whoever held their line, wrenched out, the wings thrown open: up and away -----------
CLIMB_BODY = merge(chain(18, 40, 16, -10), DANGLE, {"tail_01": (-6, 0, 0), "tail_02": (-4, 0, 0)})
CLIMB_TOP = flying(merge(CLIMB_BODY, flap(0.0)))
stab = Clip("stab", loop=False)
STABBED = tip_at(merge(DART, swollen(1.2)), (0, 0, 0.9))
stab.key(0, STABBED, "CONSTANT")
stab.key(3, moved(merge(STABBED, wings(upper=(0, -24, -40), fore=(0, 6, -12))), (0, -0.05, 0.08)), "LINEAR")
stab.key(8, moved(flying(merge(chain(-20, 0, -20, -40), DANGLE, flap(0.6, 1.2))), (0, 0, 1.0)))
stab.key(15, moved(CLIMB_TOP, (0, 0, 0.6)))

# --- stuck (1.2 s): the spear buried in the roof planks, the body heaving over it, the wings thrashing, the feet scrabbling
STUCK_BODY = merge(chain(-10, -36, -56, -62), {"tail_01": (14, 0, 0), "tail_02": (10, 0, 0)}, swollen(1.15))
def thrash(kr, kl):
    """The wings half spread and arched over it (mantled), beating unevenly: `kr`, `kl` from -1 (down round it) to 1 (up)."""
    def side(k):
        return wings(upper=(10, -16 - 24 * k, 18 - 6 * k), fore=(-4, 36 + 18 * k, 6), hand=(0, 22 + 12 * k, -8), tip=(0, 8 + 8 * k, -4))
    r, l = side(kr), side(kl)
    return {**{b: v for b, v in r.items() if b.endswith("_r")}, **{b: v for b, v in l.items() if b.endswith("_l")}}


STUCK = tip_at(merge(STUCK_BODY, thrash(0, 0)), (0, 0, -0.32))
show("stuck0", STUCK)
for s, x, y in (("r", 0.42, -1.75), ("l", -0.42, -1.6)):
    STUCK = plant(STUCK, s, (x, y, 0.07))
show("stuck", STUCK)
stuck = Clip("stuck")
THRASH = [(0.0, 0.0, 0, 0), (0.7, -0.5, 8, 0.03), (-0.8, 0.8, -6, -0.02), (0.9, -0.7, 10, 0.04), (-0.4, 0.6, -4, 0)]
for f, (kr, kl, wr, lift) in enumerate(THRASH):
    p = merge(STUCK, thrash(kr, kl), {"pelvis": (STUCK["pelvis"][0] + wr * 0.4, wr * 0.8, 0)})
    # (The spear stays in the planks: the body heaves about it.)
    p = tip_at(p, (0, 0, -0.32 + lift))
    # A foot clawing at the planks (the other planted).
    s = "r" if f % 2 else "l"
    p[f"thigh_{s}"] = (p[f"thigh_{s}"][0] + 22, p[f"thigh_{s}"][1], p[f"thigh_{s}"][2])
    p["jaw"] = (-6 - 6 * (f % 2), 0, 0)
    stuck.key(f * 8, p, "CONSTANT" if f in (1, 3) else "LINEAR")
stuck.close(40)

# --- tearFree (0.8 s, once): a heave, the spear wrenched out of the planks, a great downstroke and it's off the roof -------
tear = Clip("tearFree", loop=False)
tear.key(0, STUCK)
HEAVE = moved(merge(STUCK, chain(4, -10, -36, -50), thrash(1, 1)), (0, -0.15, 0.1))
tear.key(8, HEAVE)
OUT = tip_at(merge(STUCK_BODY, chain(14, 20, -10, -30), flap(0.5, 1.1), DANGLE), (0, 0.15, 0.5))
tear.key(13, OUT, "LINEAR")
tear.key(24, moved(CLIMB_TOP, (0, 0, 0.5)))

# --- climb (1 s): heavy wingbeats, labouring up off the train, the legs let down, the head up ------------------------------
climb = Clip("climb")
for f in range(0, 30, 3):
    t = f / 30
    climb.key(f, moved(flying(merge(CLIMB_BODY, flap(t, 1.15))), (0, 0, 0.12 * math.sin(2 * math.pi * (t - 0.3)))), "LINEAR")
climb.close(30)
show("climb", climb.keys[5][1])

# --- bank (1.2 s): wheeling round and coming in low for its mark, rolled hard over, screaming, the bladders pumping ---------
bank = Clip("bank")
BANK_BODY = merge(chain(-8, 14, -4, -22, roll=-44, turn=-16), TUCK, {"tail_01": (0, 0, 10), "tail_02": (0, -14, 8)})
for f in range(0, 36, 4):
    t = f / 36
    p = flying(merge(BANK_BODY, flap(t, 0.8), swollen(1.1 + 0.08 * math.sin(4 * math.pi * t))))
    p["jaw"] = (-30 - 6 * math.sin(6 * math.pi * t), 0, 0)
    bank.key(f, p, "LINEAR")
bank.close(36)
show("bank", bank.keys[0][1])

# --- swoop (0.8 s, once): in on them, the wings up and braking, the feet thrust out, talons open, the scream ----------------
# (The legs solved level, then the body pitched up over them: thrust out ahead and down at whoever it's coming for.)
SWOOP_LEGS = legs_to(chain(0, 22, 4, -16), (0.36, -0.25, 0.39), (55, 0, 0), (60, 0, 0))
SWOOP = flying(merge(chain(15, 30, 0, -34), {"tail_01": (-30, 0, 0), "tail_02": (-20, 0, 0)},
                     wings(upper=(-30, -54, 24), fore=(-6, 24, 12), hand=(0, 18, -6), tip=(0, 8, 0)),
                     SWOOP_LEGS, {"jaw": (-34, 0, 0)}, swollen(1.15)))
show("swoop", SWOOP)
swoop = Clip("swoop", loop=False)
swoop.key(0, merge(bank.keys[0][1]))
swoop.key(10, merge(SWOOP, wings(upper=(-30, -40, 30))))
swoop.key(24, moved(SWOOP, (0, 0.3, -0.4)))

# --- pin (1.5 s): stood on them, one foot down on their chest, the wings mantled over them, the sacs throbbing --------------
# (CreatureArt.GannetPinChest: their chest under the right foot's web, 0.3 m to its right and 0.82 m ahead of its origin;
# their face 0.35 m on from it, where the pecks land: crew_clips.py held_pinned.)
PIN_FOOT = Vector((0.3, 0.72, 0.3))
PIN_HEAD = Vector((0.3, 1.17, 0.15))
PIN_KNEE = Vector((0.9, 0.5, 0.75))
STOOD = merge(chain(34, 76, 40, -32), {"tail_01": (-16, 0, 0), "tail_02": (-6, 0, 0)}, MANTLE)
STOOD = moved(STOOD, (0, 0.3, -0.04))
STOOD = plant(STOOD, "l", (-0.42, 0.12, 0.07))
STOOD = plant(STOOD, "r", PIN_FOOT, PIN_KNEE)
show("pin", STOOD)
pin = Clip("pin")
for f, k in enumerate((0, 1, 0, -0.6)):
    p = merge(STOOD, swollen(1.02 + 0.05 * k, 1.02 + 0.07 * k), {"jaw": (-4 - 4 * max(k, 0), 0, 0)},
              wings(upper=(18, -42 - 2 * k, 26), fore=(-6, 64 + 2 * k, 8), hand=(-4, 36, -8), tip=(0, 14 + 3 * k, -4)))
    p["head"] = (p["head"][0] - 4 * k, 0, 6 * math.sin(f * 1.7))
    pin.key(f * 12, p)
pin.close(48)

# --- land (0.6 s, once): out of the swoop onto them, the weight coming down through the foot, the wings closing to mantle ---
land = Clip("land", loop=False)
land.key(0, moved(SWOOP, (0, -0.3, 0.6)))
land.key(6, moved(merge(STOOD, wings(upper=(-20, -60, 20), fore=(-6, 30, 10), hand=(0, 20, -6))), (0, 0, -0.12)), "LINEAR")
land.key(12, moved(STOOD, (0, 0, -0.05)))
land.key(18, STOOD)

# --- peckWindup (0.9 s, once): the head drawn up and back, the spear raised, a rattle in the throat, the sacs swelling -------
WINDUP = merge(STOOD, chain(26, 85, 70, 30), swollen(1.08, 1.16))
WINDUP = plant(plant(WINDUP, "l", (-0.42, 0.12, 0.07)), "r", PIN_FOOT, PIN_KNEE)
show("windup", WINDUP)
windup = Clip("peckWindup", loop=False)
windup.key(0, STOOD)
for f in range(10, 28, 3):
    windup.key(f, merge(WINDUP, {"jaw": (-3 if f % 2 else -8, 0, 0)}, swollen(1.08 + 0.02 * (f % 2), 1.16)), "CONSTANT" if f > 10 else "BEZIER")
windup.key(27, WINDUP)

# --- peck (0.5 s, once): the strike, at their head (the beat lands at PECK_STRIKE), then the spear drawn back ---------------
STRUCK = beak_to(merge(STOOD, chain(26, 30, -30, -70), swollen(1.05)), PIN_HEAD, down=70)
STRUCK = plant(plant(STRUCK, "l", (-0.42, 0.12, 0.07)), "r", PIN_FOOT, PIN_KNEE)
show("peck", STRUCK)
peck = Clip("peck", loop=False)
peck.key(0, WINDUP)
peck.key(3, STRUCK, "CONSTANT")
peck.key(6, moved(STRUCK, (0, 0, 0.02)))
peck.key(15, STOOD)

# --- driven (0.8 s, once): beaten off, it lurches up off them screaming, the wings hammering down, the legs dangling --------
driven = Clip("driven", loop=False)
driven.key(0, STOOD)
driven.key(5, moved(merge(STOOD, chain(40, 70, 50, 10), wings(upper=(-10, -60, 10), fore=(0, -10, 0)), {"jaw": (-38, 0, 0)}), (0, -0.1, 0.25)))
driven.key(12, moved(flying(merge(chain(36, 60, 30, 0), DANGLE, flap(0.5, 1.4), {"jaw": (-38, 0, 0)})), (0, -0.2, 0.9)))
driven.key(24, moved(CLIMB_TOP, (0, 0, 1.2)))

# --- hit (0.5 s, once): a ball or a blow lands: the head snaps round, a wing jerks in, the jaw wide, too fast -------------
hit = Clip("hit", loop=False)
HIT = flying(merge(FLY, chain(0, 26, 10, -6, roll=14, turn=-40), wings(upper=(0, -24, -20), fore=(0, 20, -10)), {"jaw": (-30, 0, 0)}))
hit.key(0, SOAR, "CONSTANT")
hit.key(2, HIT, "CONSTANT")
hit.key(7, merge(HIT, flap(0.3)), "LINEAR")
hit.key(15, SOAR)

# --- death (1.6 s, once): it goes over, crashing across the roof, the wings crumpling under it, the neck limp ---------------
death = Clip("death", loop=False)
death.key(0, SOAR)
death.key(8, moved(flying(merge(chain(-30, -10, -40, -60, roll=40), wings(upper=(0, -40, -30), fore=(0, 40, 0), hand=(0, 30, 0)), DANGLE,
                                {"jaw": (-30, 0, 0)})), (0.2, 0.3, -0.4)))
SPRAWL = merge(chain(-6, -20, -30, -20, roll=70, turn=50), wings(upper=(0, 30, -10), fore=(0, 10, 20), hand=(0, -10, -10), tip=(0, 10, 0)),
               mirror({"thigh_r": (-40, 0, 0), "shin_r": (-50, 0, 0), "foot_r": (40, 0, 0), "toes_r": (-40, 0, 0)}), {"jaw": (-16, 0, 0)}, swollen(0.8))
SPRAWLED = moved(SPRAWL, Vector((0.4, 0.8, 0.42)) - at(SPRAWL, "spine_01"))
death.key(20, moved(SPRAWLED, (0, -0.3, 0.25)), "LINEAR")
death.key(30, moved(SPRAWLED, (0.05, 0.1, 0.0)))
death.key(48, SPRAWLED)

CLIPS = [soar, circle, hang, fold, dive, stab, stuck, tear, climb, bank, swoop, pin, land, windup, peck, driven, hit, death]
rig.bake(sk, CLIPS)
out = rig.args()[0] if rig.args() else "gannet.glb"
rig.export(out, kit)
# A distance copy (past look.json's creatureLodMetres): it's mostly seen 20 m up. (Not when the recipe runs this to bake
# over it, tools/models/recipes/gannet.py: it exports both itself, from the baked mesh.)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
# The brief's numbers (§3), at rest: 7 m across the wings, 2.3 m to the top of the skull, a metre of beak (CreatureArtTests).
lo = Vector((1e9, 1e9, 1e9))
hi = -lo
for v in (v for p in kit.parts for v in p.v):
    lo = Vector((min(lo.x, v.x), min(lo.y, v.y), min(lo.z, v.z)))
    hi = Vector((max(hi.x, v.x), max(hi.y, v.y), max(hi.z, v.z)))
beak_len = (BEAK_TIP - head_at(0.4)).length
print(f"[dt] gannet: {hi.x - lo.x:.2f} m across (brief {SPAN}), {hi.z:.2f} m tall (brief {HEIGHT}), a {beak_len:.2f} m beak (brief {BEAK_LENGTH}); "
      f"clips {[c.name + ':' + str(c.length) for c in CLIPS]}")
