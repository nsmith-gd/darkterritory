"""THE BRAKEMAN (GDD §21 corrupted humans, App. A.8 · sight; docs/design/creatures/brakeman.md §3; ARCHITECTURE §8 note
364): a dead railwayman still winding the brakes on.

A gaunt old railwayman, stooped and long-armed, in a knee-length coat rotted to rags at the hem; a peaked railway cap with
a brass badge; a grey moustache on a grey, blotched face, the eyes sunk deep under the brow. His right shoulder and arm have
grown into rusted iron: riveted plates down the arm in lames over the shoulder, chain hanging in loops from it, a brake
wheel grown into his back and another strapped to his chest. A brass lamp on a chain at his belt. Long clawed hands, the
knuckles swollen; boots bound in iron straps. Rust-red and soot-black, everything riveted.

How it's made (the Look Review: organic, not boxes; the Gannet's and the Ribbit's way, notes 340, 362): his body is one
skin (tools/blender/flesh.py): the head (the skull, the brow over sunk sockets, the cheekbones over fallen cheeks, the long
nose, the jaw, the moustache's droop and the lank hair under the cap), the neck's cords, the coat over the hunched back
and the chest with its lapels and turned-up collar, the left sleeve torn off at the forearm and the bare arm below it, the
right arm's corrupted flesh, the palms, the trousers and the boots, the cap's crown and band; each a smooth volume both a
solid for the union and a term of a signed-distance field the union's voxels are settled onto, so the arm grows out of the
shoulder and the boot out of the trouser with the crease filled, then QuadriFlowed and smooth-shaded. Over it, apart: the
coat's skirt (a two-sided shell, its hem rotted into tongues), the long fingers and their claws, the eyes; and the hard
parts, flat-shaded: the iron lames and plates, the wrist's cuff, the two brake wheels, the chest's straps, the boot straps,
the chains, the cap's peak and badge, the buttons, the lamp. Its colour (and the rivets, the weave, the pits and the
wrinkles, too fine for the mesh) is baked into one atlas by tools/models/recipes/brakeman.py; built alone (this script), it
wears the shared tiling textures.

SK_Human (rig.human) at 1.85 m, the arms long; and a chain of four off the right hand (the lash: it hangs from the cuff and
swings) and the lamp's bone off the pelvis. T-pose facing +Y (the engine's -Z); pivot on the floor between the feet. A
character (GDD §27: 4,000-10,000 triangles). Clips (§31: still, then abrupt): walk (stooped, quick, the chain swinging and
dragging), wind (bent over a car's brake wheel in front of him, both clawed hands on its rim, cranking it round a
half-turn a beat), jump (a gap, crouched), flee (low and fast), drop (over the side: down onto the ladder, hanging, gone),
climb (up an end ladder behind him and over onto the roof), cornered (backing, crouched, the chain raised), lash (the chain
swung at a face), hit, death (pitching off the roof).

    tools/models/build.sh brakeman        # this, with its colour baked -> content/art/models/brakeman.glb (and .lod1.glb)
    (or, with no Blender: pip install "bpy<5" into a Python 3.11 venv, then python tools/models/recipes/brakeman.py)
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import flesh as fl  # noqa: E402
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Vector, hexc, mirror, noise3, over, swap  # noqa: E402

rig.reset()

# The brief's numbers (brakeman.md §3), printed at the end and pinned by CreatureArtTests: about 1.8 m stooped, 1.9 m stood up
# in his cap.
HEIGHT = 1.85
# The lash: a chain off the right wrist's cuff, four links of bone (hanging plumb at rest).
LASH = [(0.8, 0.0, 1.47)]
for _k in range(4):
    LASH.append((0.8, 0.0, 1.47 - 0.165 * (_k + 1)))
EXTRA = [Bone(f"chain_{k + 1:02d}", "hand_r" if k == 0 else f"chain_{k:02d}", LASH[k], LASH[k + 1]) for k in range(4)]
# The lamp on its chain at his right hip, hung off the belt on an iron hook that holds it out clear of the coat.
LAMP_TOP = Vector((0.27, 0.03, 1.04))
EXTRA.append(Bone("lamp", "pelvis", tuple(LAMP_TOP), tuple(LAMP_TOP + Vector((0, 0, -0.2)))))
sk = rig.human(HEIGHT, arm=1.12, width=0.95, fingers=True, sockets=False, extra=EXTRA)
sk.build()
kit = rig.Kit(sk, "brakeman")
SKIN_FACES, HEAD_FACES = 1650, 650


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


NECK_TOP = H("head").z
HIP = H("thigh_r").z
WAIST = H("spine_01").z
CHEST = H("spine_03").z
TOP = T("spine_03").z
KNEE = H("calf_r").z
HC = Vector((0.0, 0.016, NECK_TOP + 0.1))
HEM = 0.43

# Built alone, each region wears the shared tiling texture its name starts with; baked (tools/models/recipes/brakeman.py),
# the names say what to paint: the coat's rotten wool, the trousers, the boots, the grey skin, the rust-red flesh the iron
# grows out of, the lank hair and the moustache, the iron and the chain, the brass, the claws.
COAT = Mat("wool.brakeman_coat", hexc("#2a231e"), shine=0.05)
COLLAR = Mat("wool.brakeman_collar", hexc("#241e1a"), shine=0.05)
TROUSER = Mat("wool.brakeman_trouser", hexc("#221d1a"), shine=0.04)
BOOT = Mat("leather.brakeman_boot", hexc("#1d1714"), shine=0.3)
CAP = Mat("wool.brakeman_cap", hexc("#1f1b19"), shine=0.06)
PEAK = Mat("leather.brakeman_peak", hexc("#120f0d"), shine=0.5)
SKIN = Mat("skin.brakeman_skin", hexc("#8a8580"), shine=0.25)
RUST = Mat("flesh.brakeman_rust", hexc("#6a3424"), shine=0.3)
HAIR = Mat("fleece.brakeman_hair", hexc("#8f8b84"), shine=0.1)
LIP = Mat("flesh.brakeman_lip", hexc("#4e3a38"), shine=0.3)
EYE = Mat("glass_dirty.brakeman_eye", hexc("#b8b08a"), shine=0.7)
IRON = Mat("rust_heavy.brakeman_iron", hexc("#5c3222"), shine=0.3)
CHAIN = Mat("rust_heavy.brakeman_chain", hexc("#4a2a1c"), shine=0.35)
BRASS = Mat("brass.brakeman_brass", hexc("#7a6236"), shine=0.5)
CLAW = Mat("tar.brakeman_claw", hexc("#16110e"), shine=0.55)
# The lamp's glass, lit a breath (a railwayman's lamp still burning low): its own material, kept out of the atlas.
GLASS = Mat("lamp_lens.brakeman_glass", hexc("#d89a48"), shine=0.9, emissive=0.55)


def bell(x):
    return math.exp(-x * x)


def smooth01(a, b, x):
    t = min(1.0, max(0.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def h01(*k):
    """A fixed hash in 0..1 (no RNG: rebuilds are identical)."""
    return 0.5 + 0.5 * noise3(Vector((k[0] * 1.37 + 0.11, (k[1] if len(k) > 1 else 0) * 2.11 + 0.37, (k[2] if len(k) > 2 else 0) * 0.71 + 0.53)), 364, 1.0)


def R(rx=0.0, ry=0.0, rz=0.0):
    return rig.rot(rx, ry, rz).to_matrix().to_4x4()


def norm(w):
    w = {b: v for b, v in w.items() if v > 0.02}
    t = sum(w.values())
    return {b: v / t for b, v in w.items()}


def mix(a, b, k):
    out = {n: v * (1 - k) for n, v in a.items()}
    for n, v in b.items():
        out[n] = out.get(n, 0) + v * k
    return norm(out)


def chain(joints, bones, soft=0.04):
    """Weights along a limb: each point its nearest length's bone, blended half and half at a joint over `soft` metres."""
    joints = [Vector(j) for j in joints]

    def fn(p):
        p = Vector(p)
        best, bi, bt = 1e9, 0, 0.0
        for i, (a, b) in enumerate(zip(joints, joints[1:])):
            ab = b - a
            t = max(0.0, min(1.0, (p - a).dot(ab) / ab.length_squared))
            d = (a + ab * t - p).length
            if d < best:
                best, bi, bt = d, i, t
        L = (joints[bi + 1] - joints[bi]).length
        w = {bones[bi]: 1.0}
        if bi > 0 and bt * L < soft:
            w = mix(w, {bones[bi - 1]: 1.0}, 0.5 * (1 - bt * L / soft))
        if bi < len(bones) - 1 and (1 - bt) * L < soft:
            w = mix(w, {bones[bi + 1]: 1.0}, 0.5 * (1 - (1 - bt) * L / soft))
        return norm(w)
    return fn


SPINE = rig.along("z", [(HIP - 0.08, "pelvis"), (WAIST, "pelvis"), (WAIST + 0.07, "spine_01"), (CHEST - 0.06, "spine_02"),
                        (CHEST + 0.04, "spine_03"), (TOP + 0.02, "spine_03"), (TOP + 0.07, "neck"), (NECK_TOP, "neck"),
                        (NECK_TOP + 0.03, "head")])
ARM = {s: chain([H(f"clavicle_{s}"), H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")],
                [f"clavicle_{s}", f"upperarm_{s}", f"lowerarm_{s}", f"hand_{s}"], soft=0.05) for s in "lr"}
LEG = {s: chain([H(f"thigh_{s}") + Vector((0, 0, 0.1)), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}"), T(f"ball_{s}")],
                [f"thigh_{s}", f"calf_{s}", f"foot_{s}", f"ball_{s}"], soft=0.05) for s in "lr"}


def body_w(p):
    """The trunk along the spine; past the shoulder's root it goes over to the arm on its side, under the hip to the leg."""
    p = Vector(p)
    s = "r" if p.x > 0 else "l"
    ax = abs(p.x)
    w = SPINE(p)
    if p.z > CHEST - 0.08 and ax > 0.12:
        w = mix(w, ARM[s](p), smooth01(0.13, 0.24, ax) * smooth01(CHEST - 0.08, CHEST + 0.02, p.z))
    if p.z < HIP + 0.02:
        w = mix(w, LEG[s](p), smooth01(HIP + 0.02, HIP - 0.12, p.z) * smooth01(0.0, 0.06, ax))
    return w


def head_w(p):
    p = Vector(p)
    return mix({"neck": 1.0}, {"head": 1.0}, smooth01(NECK_TOP - 0.02, NECK_TOP + 0.04, p.z))


# ----------------------------------------------------------------------------------------------------------------
# The skin: one surface from the cap's crown to the boots' soles (flesh.fuse, settled on `F`).
body = kit.part("body")
F = fl.Flesh(body, "brakeman flesh")

# The head and the neck: a skin of their own (more of the budget for the face; the collar hides where it meets the coat's).
head = kit.part("head")
HF = fl.Flesh(head, "brakeman head")
# The head: long and gaunt, the skull showing; the cap pulled down over it (its crown and band are the skin's too).
HF.blob(HC, (0.074, 0.09, 0.104), 0, SKIN, head_w, around=22, rings=14)                                     # the skull
HF.blob(HC + Vector((0, -0.016, 0.022)), (0.072, 0.084, 0.088), 0.03, SKIN, head_w)                         # its back
HF.blob(HC + Vector((0, 0.034, -0.062)), (0.056, 0.062, 0.052), 0.03, SKIN, head_w)                         # the jaw
HF.blob(HC + Vector((0, 0.074, -0.098)), (0.026, 0.022, 0.024), 0.02, SKIN, head_w)                         # the chin
for sx in (1, -1):
    HF.blob(HC + Vector((sx * 0.046, 0.064, -0.004)), (0.022, 0.02, 0.015), 0.014, SKIN, head_w)            # cheekbones
    HF.carve(HC + Vector((sx * 0.052, 0.08, -0.046)), (0.02, 0.028, 0.026), 0.014)                           # fallen cheeks
    HF.carve(HC + Vector((sx * 0.03, 0.09, 0.012)), (0.018, 0.017, 0.013), 0.008)                            # sunk sockets
    HF.carve(HC + Vector((sx * 0.07, 0.04, 0.03)), (0.012, 0.03, 0.024), 0.012)                              # hollow temples
    HF.blob(HC + Vector((sx * 0.075, -0.006, 0.002)), (0.011, 0.02, 0.03), 0.008, SKIN, head_w, around=10, rings=6)  # ears
# The brow, heavy over the sockets.
HF.limb([HC + Vector((-0.052, 0.078, 0.032)), HC + Vector((0, 0.088, 0.038)), HC + Vector((0.052, 0.078, 0.032))],
       [0.011, 0.013, 0.011], 0.012, SKIN, head_w, sides=10, ref=(0, 1, 0))
# The nose: long, the bridge high, the tip drooped, and its wings.
HF.limb([HC + Vector((0, 0.09, 0.014)), HC + Vector((0, 0.104, -0.018)), HC + Vector((0, 0.112, -0.038))],
       [0.009, 0.011, 0.012], 0.01, SKIN, head_w, sides=10, ref=(1, 0, 0))
for sx in (1, -1):
    HF.blob(HC + Vector((sx * 0.012, 0.102, -0.04)), (0.01, 0.01, 0.008), 0.008, SKIN, head_w, around=8, rings=5)
# The mouth: thin lips pressed shut under the moustache, the corners turned down.
HF.limb([HC + Vector((-0.026, 0.088, -0.074)), HC + Vector((0, 0.097, -0.07)), HC + Vector((0.026, 0.088, -0.074))],
       [0.005, 0.006, 0.005], 0.006, LIP, head_w, sides=8, ref=(0, 0, 1))
# The moustache: a heavy grey brush over the lip, its ends drooping past the mouth's corners.
HF.blob(HC + Vector((0, 0.104, -0.056)), (0.03, 0.012, 0.011), 0.008, HAIR, head_w, around=14, rings=6)
for sx in (1, -1):
    HF.limb([HC + Vector((sx * 0.018, 0.1, -0.058)), HC + Vector((sx * 0.034, 0.09, -0.07)), HC + Vector((sx * 0.038, 0.082, -0.086))],
           [0.009, 0.007, 0.004], 0.006, HAIR, head_w, sides=8, ref=(0, 1, 0))
# The cap: a railwayman's, the crown flat-topped and pulled down to the brow, the band round it (the peak and the badge are
# iron's and brass's: the hard parts).
CAP_Z = HC.z + 0.046
HF.blob(Vector((0, HC.y + 0.006, CAP_Z + 0.03)), (0.088, 0.102, 0.034), 0.012, CAP, head_w, around=24, rings=8)
HF.blob(Vector((0, HC.y + 0.01, CAP_Z + 0.05)), (0.094, 0.108, 0.02), 0.01, CAP, head_w, around=24, rings=6)
BAND = [Vector((math.sin(a) * 0.082, HC.y + math.cos(a) * 0.098, CAP_Z - 0.004 + 0.008 * math.cos(a))) for a in
        [math.radians(d) for d in range(0, 360, 30)]]
HF.limb(BAND + [BAND[0]], [0.016] * (len(BAND) + 1), 0.01, CAP, head_w, sides=8, ref=(0, 0, 1))
# Lank grey hair hanging from under the cap at the sides and the back.
for k in range(9):
    a = math.radians(70 + 220 * k / 8)
    x, y = math.sin(a) * 0.076, HC.y + math.cos(a) * 0.086
    hang = 0.08 + 0.05 * h01(k, 1)
    HF.limb([Vector((x, y, CAP_Z - 0.01)), Vector((x * 1.08, y - 0.006, CAP_Z - 0.01 - hang * 0.55)), Vector((x * 1.06, y - 0.01, CAP_Z - 0.01 - hang))],
           [0.011, 0.008, 0.003], 0.006, HAIR, head_w, sides=6, ref=(0, 0, 1))

# The neck: thin, the cords standing, the apple sharp.
HF.limb([Vector((0, -0.004, TOP - 0.03)), Vector((0, 0.004, (TOP + NECK_TOP) / 2)), Vector((0, 0.012, NECK_TOP + 0.03))],
       [0.04, 0.034, 0.038], 0.02, SKIN, lambda p: mix(SPINE(p), head_w(p), smooth01(TOP + 0.04, NECK_TOP + 0.03, Vector(p).z)))
for sx in (1, -1):
    HF.limb([Vector((sx * 0.026, 0.026, TOP - 0.01)), Vector((sx * 0.02, 0.03, (TOP + NECK_TOP) / 2)), Vector((sx * 0.03, 0.03, NECK_TOP + 0.02))],
           [0.01, 0.009, 0.009], 0.012, SKIN, SPINE, sides=8)
HF.blob(Vector((0, 0.036, (TOP + NECK_TOP) / 2 - 0.004)), (0.011, 0.01, 0.014), 0.01, SKIN, SPINE, around=8, rings=6)

# The trunk in the coat: hunched, the shoulders forward and the upper back rounded, the chest hollow, the coat buttoned
# over it, the collar up round the neck and the lapels open on the shirt's throat. The right shoulder heavy with what's
# grown under the iron.


def coat_mat(pts, n):
    c = sum(pts, Vector()) / len(pts)
    # The shirt's open throat between the lapels, and the right shoulder's coat torn away round the iron's root.
    if c.y > 0.06 and abs(c.x) < 0.045 and c.z > TOP - 0.05:
        return SKIN
    if c.x > 0.12 and c.z > CHEST + 0.02:
        return RUST
    return COAT


F.blob(Vector((0, -0.004, CHEST + 0.02)), (0.165, 0.112, 0.15), 0, COAT, body_w, around=24, rings=14, fmat=coat_mat)   # chest
F.blob(Vector((0, 0.0, WAIST)), (0.152, 0.106, 0.13), 0.06, COAT, body_w, around=22, rings=12, fmat=coat_mat)          # waist
F.blob(Vector((0, -0.006, HIP + 0.04)), (0.165, 0.112, 0.11), 0.06, COAT, body_w, around=22, rings=10)                  # hips
F.blob(Vector((0, -0.066, TOP - 0.05)), (0.135, 0.08, 0.1), 0.06, COAT, body_w, around=20, rings=10)                    # the hunch
F.limb([Vector((-0.2, -0.012, TOP - 0.03)), Vector((0, -0.02, TOP - 0.005)), Vector((0.2, -0.012, TOP - 0.03))], [0.058, 0.064, 0.058],
       0.05, COAT, body_w, sides=14, ref=(0, 0, 1), fmat=coat_mat)                                                       # shoulders
# The right shoulder swollen under the iron, the flesh rust-red where the coat's torn off it.
F.blob(Vector((0.19, -0.012, TOP - 0.02)), (0.092, 0.09, 0.082), 0.05, RUST, body_w, around=18, rings=10)
# The collar, turned up round the neck at the back and sides; the lapels laid open down the chest.
COLLAR_PTS = [Vector((math.sin(a) * 0.085, -0.012 + math.cos(a) * 0.072, TOP + 0.03 - 0.03 * max(0.0, math.cos(a))))
              for a in [math.radians(d) for d in range(-130, 131, 26)]]
F.limb(COLLAR_PTS, [0.02] + [0.028] * (len(COLLAR_PTS) - 2) + [0.02], 0.02, COLLAR, SPINE, sides=8, ref=(0, 0, 1))
for sx in (1, -1):
    F.blob(Vector((sx * 0.07, 0.096, CHEST + 0.05)), (0.045, 0.014, 0.085), 0.025, COLLAR, body_w, rot=R(ry=-sx * 18, rx=8),
           around=14, rings=8)
# The belt over the coat at the waist (its buckle is brass's).
BELT = [Vector((math.sin(a) * 0.158, -0.002 + math.cos(a) * 0.112, WAIST - 0.035)) for a in [math.radians(d) for d in range(0, 360, 24)]]
F.limb(BELT + [BELT[0]], [0.024] * (len(BELT) + 1), 0.008, BOOT, SPINE, sides=6, ref=(0, 0, 1))

# The arms. The left: the coat's sleeve rotted off below the elbow, ragged, and the bare forearm under it starved to the
# bone, the sinews standing; the right: the corrupted flesh under the iron, thick, rust-red, all the way to the hand.
HANDS = {}
for s, sx in (("r", 1), ("l", -1)):
    sh, el, wr, kn = H(f"upperarm_{s}"), H(f"lowerarm_{s}"), H(f"hand_{s}"), T(f"hand_{s}")
    if s == "l":
        F.limb([sh + Vector((sx * -0.03, 0, -0.01)), sh.lerp(el, 0.5), el, el.lerp(wr, 0.42)], [0.06, 0.05, 0.052, 0.054], 0.04, COAT, ARM[s],
               sides=14, ref=(0, 0, 1))
        # The torn cuff: tongues of rotten wool hanging off the sleeve's end (down, in the T-pose).
        for k in range(5):
            a = math.radians(-80 + 160 * k / 4 + 20 * (h01(k, 4) - 0.5))
            o = el.lerp(wr, 0.4) + Vector((0, math.sin(a) * 0.05, -math.cos(a) * 0.05))
            L = 0.04 + 0.04 * h01(k, 5)
            F.blob(o + Vector((sx * (0.012 + L * 0.5), 0, -0.012)), (L * 0.6, 0.016, 0.006), 0.012, COAT, ARM[s], rot=R(rx=math.degrees(a)),
                   around=8, rings=4)
        F.limb([el.lerp(wr, 0.3), el.lerp(wr, 0.7), wr], [0.042, 0.037, 0.033], 0.02, SKIN, ARM[s], sides=12, ref=(0, 0, 1))
        for k, (dy, dz) in enumerate(((0.016, 0.014), (-0.014, 0.012))):
            F.limb([el.lerp(wr, 0.45) + Vector((0, dy, dz)), wr + Vector((sx * -0.02, dy * 0.6, dz * 0.6))], [0.008, 0.006], 0.012, SKIN, ARM[s],
                   sides=6)
        skin = SKIN
    else:
        F.limb([sh + Vector((sx * -0.03, 0, -0.005)), sh.lerp(el, 0.5), el, el.lerp(wr, 0.5), wr], [0.074, 0.062, 0.058, 0.05, 0.034], 0.04,
               RUST, ARM[s], sides=14, ref=(0, 0, 1))
        skin = RUST
    # The palm: long and bony, the knuckles swollen (the fingers are their own, `fingers`).
    F.blob(wr.lerp(kn, 0.5) + Vector((sx * 0.006, 0, 0)), (0.066, 0.05, 0.021), 0.016, skin, f"hand_{s}", around=14, rings=8)
    for f in range(4):
        F.blob(kn + Vector((0, (f - 1.5) * 0.022, 0.006)), (0.013, 0.012, 0.012), 0.008, skin, f"hand_{s}", around=8, rings=5)
    F.blob(wr + Vector((sx * 0.004, 0, 0)), (0.02, 0.028, 0.02), 0.012, skin, ARM[s], around=10, rings=6)
    HANDS[s] = (wr, kn)

# The legs in the trousers, the left one torn at the knee on the corrupted flesh; the boots big and heavy, laced high, the
# soles thick (their iron straps are the hard parts').
for s, sx in (("r", 1), ("l", -1)):
    hp, kn, an, bl, toe = H(f"thigh_{s}"), H(f"calf_{s}"), H(f"foot_{s}"), H(f"ball_{s}"), T(f"ball_{s}")

    def leg_mat(pts, n, s=s, kn=kn):
        c = sum(pts, Vector()) / len(pts)
        return RUST if s == "l" and (c - kn - Vector((0, 0.05, 0))).length < 0.05 else TROUSER

    F.limb([hp + Vector((0, 0, 0.06)), hp.lerp(kn, 0.5), kn, kn.lerp(an, 0.5), an + Vector((0, 0, 0.12))],
           [0.084, 0.066, 0.058, 0.054, 0.05], 0.04, TROUSER, LEG[s], sides=16, ref=(0, 1, 0), fmat=leg_mat)
    F.blob(kn + Vector((0, 0.03, 0)), (0.05, 0.04, 0.05), 0.03, TROUSER, LEG[s], fmat=leg_mat)                 # the knee
    # The boot: its shaft to mid-calf, the foot, the toe cap, the heel, the sole.
    F.limb([an + Vector((0, -0.01, 0.2)), an + Vector((0, -0.006, 0.05))], [0.06, 0.058], 0.02, BOOT, LEG[s], sides=14, ref=(0, 1, 0))
    F.blob(an.lerp(bl, 0.45) + Vector((0, 0, -0.008)), (0.06, 0.115, 0.05), 0.03, BOOT, LEG[s], around=16, rings=8)
    F.blob(bl.lerp(toe, 0.35) + Vector((0, 0, 0.008)), (0.058, 0.064, 0.036), 0.025, BOOT, LEG[s], around=14, rings=8)
    F.blob(an + Vector((0, -0.05, -0.04)), (0.046, 0.04, 0.04), 0.02, BOOT, LEG[s], around=12, rings=6)
    F.blob(an.lerp(toe, 0.5) + Vector((0, -0.01, -0.062)), (0.066, 0.175, 0.016), 0.012, PEAK, LEG[s], around=18, rings=4)

# The roots of the iron: the brake wheel's hub sunk into his back, the flesh rucked up round it.
WHEEL_BACK = Vector((0.11, -0.135, CHEST + 0.0))
F.blob(WHEEL_BACK + Vector((0, 0.03, 0)), (0.07, 0.05, 0.07), 0.04, RUST, SPINE, around=14, rings=8)

# ----------------------------------------------------------------------------------------------------------------
# The coat's skirt: from the waist to the knees, open up the front, heavy wool hanging in folds, the hem rotted into
# ragged tongues. A two-sided shell (outside and lining), on the legs as the crew's is.
skirt = kit.part("skirt")


def skirt_w(p):
    p = Vector(p)
    k = smooth01(WAIST - 0.02, KNEE + 0.1, p.z)
    side = smooth01(-0.06, 0.06, p.x)
    w = {"pelvis": 1 - k}
    if side > 1e-4:
        w["thigh_r"] = k * side
    if side < 1 - 1e-4:
        w["thigh_l"] = k * (1 - side)
    return norm(w)


SK_RINGS = [(WAIST - 0.02, 0.162, 0.118), (HIP + 0.02, 0.18, 0.13), (HIP - 0.12, 0.2, 0.15), (KNEE + 0.16, 0.22, 0.166),
            (KNEE + 0.02, 0.232, 0.176), (HEM + 0.04, 0.24, 0.182)]
N_AROUND = 36
OPEN = math.radians(26)                   # half the front's opening, at the hem (it closes to the waist)


def skirt_ring(z, rx, ry, i, inner):
    ring = []
    last = i == len(SK_RINGS) - 1
    gap = OPEN * smooth01(WAIST, HEM, z)
    for j in range(N_AROUND + 1):
        a = gap + (2 * math.pi - 2 * gap) * j / N_AROUND
        fold = 0.007 * math.sin(a * 9 + 0.6) * smooth01(HIP, HEM, z)
        r = 1 + (fold - (0.006 if inner else 0)) / max(rx, 1e-3)
        p = Vector((math.sin(a) * rx * r, -0.004 + math.cos(a) * ry * r, z))
        if last:
            # Tongues of rot: the hem torn up between them, some long, some short.
            # Rotted into long ragged tongues of different lengths, notches torn up between them, some tongues hanging out.
            tongue = (0.06 + 0.24 * h01(j, 7) ** 1.4) if j % 2 else -(0.02 + 0.1 * h01(j, 9))
            p.z -= tongue
            if j % 2:
                p += Vector((p.x, p.y + 0.004, 0)).normalized() * 0.03 * h01(j, 8)
        ring.append(p)
    return ring


for inner in (False, True):
    rings = [skirt_ring(z, rx, ry, i, inner) for i, (z, rx, ry) in enumerate(SK_RINGS)]
    cents = [Vector((0, -0.004, z)) for z, _, _ in SK_RINGS]
    start = len(skirt.f)
    skirt.loft(rings, COAT, skirt_w, centres=cents, closed=False, uv=lambda i, j, u, v, p: (u * 4.0, v * 1.6))
    if inner:
        # The lining: the same surface a few millimetres in, turned to face the legs (it shows under the hem and up the
        # front's opening).
        skirt.f[start:] = [(idx[::-1], uvs[::-1], m, sm) for idx, uvs, m, sm in skirt.f[start:]]

# ----------------------------------------------------------------------------------------------------------------
# The fingers: long and bony, the joints knotted, each ending in a hooked black claw; the thumbs the same.
fingers = kit.part("fingers")
TIPS = []
for s, sx in (("r", 1), ("l", -1)):
    wr, kn = HANDS[s]
    skin = RUST if s == "r" else SKIN
    for f in range(5):
        thumb = f == 4
        if thumb:
            base = H(f"thumb_{s}") + Vector((0, 0.004, -0.006))
            d = (T(f"thumb_{s}") - H(f"thumb_{s}")).normalized()
            n, r0 = 0.085, 0.012
            bone = f"thumb_{s}"
        else:
            # Splayed in a fan, long, each joint bent further down toward the palm: a claw of a hand.
            fan = (f - 1.5) * 0.3
            base = kn + Vector((0, (f - 1.5) * 0.024, 0.002))
            d = Vector((sx * math.cos(fan), math.sin(fan), -0.05)).normalized()
            n = 0.14 * (0.86 + 0.14 * (1 - abs(f - 1.3) / 1.7))
            r0 = 0.015
            bone = f"fingers_{s}"
        down = Vector((0, 0, -1))
        pts = [base - d * 0.014, base]
        for frac, bend in ((0.4, 12), (0.33, 30), (0.27, 50)):
            dk = (d * math.cos(math.radians(bend)) + down * math.sin(math.radians(bend))).normalized()
            pts.append(pts[-1] + dk * n * frac)
        d = (pts[-1] - pts[-2]).normalized()
        # Knotted at the joints: swollen knuckles, thinner between, tapering to the nail.
        fingers.tube(pts, [r0 * 1.05, r0 * 1.1, r0 * 0.9, r0 * 0.8, r0 * 0.62], 5, skin,
                     (lambda p, base=base, n=n, s=s, bone=bone: {f"hand_{s}": 1.0} if (Vector(p) - base).length < n * 0.12 and bone != f"thumb_{s}" else {bone: 1.0}),
                     ref=(0, 0, 1), cap0=True)
        for k in (1, 2, 3):
            fingers.blob(pts[k], (r0 * 1.32, r0 * 1.32, r0 * 1.22), 5, 3, skin, bone)
        # The claw: horn, hooked on down past the fingertip, long.
        tip = pts[-1]
        c1 = tip + d * 0.035 + Vector((0, 0, -0.006))
        c2 = tip + d * 0.055 + Vector((0, 0, -0.03))
        TIPS.append(c2)
        fingers.tube([tip - d * 0.008, c1, c2], [(r0 * 0.68, r0 * 0.6), (r0 * 0.42, r0 * 0.36), 0.0012], 5, CLAW, bone, ref=(0, 0, 1), cap0=True,
                     cap1="point", smooth=False)

# The eyes: small, sunk deep in their sockets, yellowed and filmed.
eyes = kit.part("eyes")
EYES = {}
for s, sx in (("r", 1), ("l", -1)):
    e = HC + Vector((sx * 0.03, 0.079, 0.01))
    EYES[s] = e
    eyes.blob(e, (0.0115, 0.0105, 0.0095), 8, 6, EYE, "head")

# ----------------------------------------------------------------------------------------------------------------
# The hard parts: iron, chain and brass, flat-shaded over the skin.
iron = kit.part("iron", smooth=False)


def c_plate(part, pts, radii, a0, a1, thick, mat, bones, n=7, ref=(0, 0, 1), flare=0.0):
    """A curved plate round a limb or a shoulder: an arc of `a0`..`a1` (radians about the line's `ref` side) of a tube along
    `pts`, `thick` deep, closed all round (a lame of armour, a riveted plate)."""
    pts = [Vector(p) for p in pts]
    rings = []
    for i, c in enumerate(pts):
        t = pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]
        side, up, _ = rig.frame_from(t, ref)
        r = radii[i] * (1 + flare * (i in (0, len(pts) - 1)))
        outer = [c + (side * math.sin(a) + up * math.cos(a)) * r for a in [a0 + (a1 - a0) * k / (n - 1) for k in range(n)]]
        inner = [c + (side * math.sin(a) + up * math.cos(a)) * (r - thick) for a in [a1 - (a1 - a0) * k / (n - 1) for k in range(n)]]
        rings.append(outer + inner)
    part.loft(rings, mat, bones, centres=pts, cap0=True, cap1=True, smooth=False)


# The right arm's iron: lames over the shoulder, overlapping down onto the upper arm; plates riveted down the arm; the
# cuff at the wrist; all grown through the flesh, ragged where they meet it.
sh, el, wr = H("upperarm_r"), H("lowerarm_r"), H("hand_r")
for k in range(4):
    c = Vector((0.13 + 0.07 * k, -0.012, TOP + 0.018 - 0.022 * k))
    ax = Vector((0, 1, 0))
    c_plate(iron, [c + ax * -0.09, c + ax * 0.0, c + ax * 0.09], [0.1 - 0.008 * k, 0.108 - 0.008 * k, 0.1 - 0.008 * k],
            math.radians(-100), math.radians(100), 0.01, IRON, mix(SPINE(c), ARM["r"](c), smooth01(0.13, 0.28, c.x)),
            n=7, ref=(0.35 * k, 0, 1))
for k in range(3):
    t0, t1 = 0.12 + 0.27 * k, 0.36 + 0.27 * k
    a, b = sh.lerp(el, t0), sh.lerp(el, t1)
    c_plate(iron, [a, a.lerp(b, 0.5), b], [0.074, 0.071, 0.068], math.radians(-150 + 20 * k), math.radians(110 - 15 * k), 0.008, IRON,
            ARM["r"], n=7)
for k in range(3):
    t0, t1 = 0.05 + 0.3 * k, 0.33 + 0.3 * k
    a, b = el.lerp(wr, t0), el.lerp(wr, t1)
    c_plate(iron, [a, a.lerp(b, 0.5), b], [0.062 - 0.008 * k, 0.06 - 0.008 * k, 0.057 - 0.008 * k], math.radians(-140), math.radians(140),
            0.007, IRON, ARM["r"], n=7)
# The cuff: a heavy iron band at the wrist, the lash's chain shackled to it.
iron.tube([wr - Vector((0.05, 0, 0)), wr - Vector((0.012, 0, 0))], [0.05, 0.048], 10, IRON, "lowerarm_r", ref=(0, 0, 1), cap0=True, cap1=True)
iron.tube([wr - Vector((0.05, 0, 0)), wr - Vector((0.043, 0, 0))], [0.056, 0.056], 10, IRON, "lowerarm_r", ref=(0, 0, 1), cap0=True, cap1=True)
def wheel(part, centre, normal, up, radius, spokes, mat, bones, tube=0.016, hub=0.03, segs=14):
    """A brake wheel: its rim, its spokes, its hub (a car's handbrake wheel, as the train kit's)."""
    n = Vector(normal).normalized()
    u = (Vector(up) - n * Vector(up).dot(n)).normalized()
    v = n.cross(u)
    rim = [centre + (u * math.cos(a) + v * math.sin(a)) * radius for a in [2 * math.pi * k / segs for k in range(segs)]]
    part.tube(rim, [tube] * segs, 4, mat, bones, ref=list(n for _ in rim), loop=True)
    for k in range(spokes):
        a = 2 * math.pi * (k + 0.5) / spokes
        d = u * math.cos(a) + v * math.sin(a)
        part.tube([centre + d * hub * 0.8, centre + d * (radius - tube * 0.6)], [tube * 0.6, tube * 0.55], 4, mat, bones, ref=(n.x, n.y, n.z))
    part.tube([centre - n * 0.03, centre + n * 0.03], [hub, hub * 0.9], 7, mat, bones, ref=(u.x, u.y, u.z), cap0=True, cap1=True)


# The brake wheel grown into his back, tipped over his right shoulder; the smaller one strapped to his chest.
wheel(iron, WHEEL_BACK + Vector((0.04, -0.035, 0.02)), Vector((0.2, -1, -0.6)), Vector((0.3, 0, 1)), 0.22, 5, IRON, "spine_03",
      tube=0.019, hub=0.034)
CHEST_WHEEL = Vector((-0.01, 0.13, CHEST - 0.02))
wheel(iron, CHEST_WHEEL, Vector((0, 1, 0.12)), Vector((0, 0, 1)), 0.15, 5, IRON, ["spine_02", "spine_03"], tube=0.014, hub=0.026, segs=12)
# Its straps: iron bands round his chest, riveted to the wheel's rim.
for dz in (0.1, -0.1):
    z = CHEST_WHEEL.z + dz
    STRAP = [Vector((math.sin(a) * 0.172, -0.004 + math.cos(a) * 0.124, z - 0.012 * math.cos(a))) for a in
             [math.radians(d) for d in range(-150, 151, 25)]]
    iron.tube(STRAP, [(0.006, 0.02)] * len(STRAP), 4, IRON, ["spine_02", "spine_03"], ref=[(0, 0, 1)] * len(STRAP))
# The boots' iron straps: three bands round each shaft and one over the instep, a buckle on each.
for s, sx in (("r", 1), ("l", -1)):
    an, bl = H(f"foot_{s}"), H(f"ball_{s}")
    for k, (dz, r) in enumerate(((0.19, 0.069), (0.12, 0.067), (0.05, 0.068))):
        c = an + Vector((0, -0.008, dz))
        ring = [c + Vector((math.sin(a) * r, math.cos(a) * r, 0)) for a in [2 * math.pi * j / 9 for j in range(9)]]
        iron.tube(ring, [(0.007, 0.018)] * 9, 3, IRON, LEG[s], ref=[(0, 0, 1)] * 9, loop=True)
        iron.box(c + Vector((sx * r, 0.0, 0)), (0.008, 0.018, 0.016), IRON, LEG[s])
    c = an.lerp(bl, 0.5) + Vector((0, 0, -0.01))
    arch = [c + Vector((math.sin(a) * 0.062, 0, math.cos(a) * 0.05 + 0.002)) for a in [math.radians(d) for d in range(-90, 91, 30)]]
    iron.tube(arch, [(0.007, 0.018)] * len(arch), 4, IRON, LEG[s], ref=[(0, 1, 0)] * len(arch))
# The cap's peak, stiff out over the eyes, and its brass badge (a wheel's spokes struck in it).
peak = [Vector((math.sin(a) * 0.084, HC.y + 0.02 + math.cos(a) * 0.105, CAP_Z - 0.012 - 0.01 * math.cos(a))) for a in
        [math.radians(d) for d in range(-80, 81, 20)]]
iron.tube(peak, [(0.004, 0.034)] * len(peak), 4, PEAK, "head", ref=[(math.sin(math.radians(d)), math.cos(math.radians(d)), 0.4) for d in range(-80, 81, 20)])
BADGE = Vector((0, HC.y + 0.107, CAP_Z + 0.022))
iron.tube([BADGE - Vector((0, 0.004, 0)), BADGE + Vector((0, 0.004, 0))], [0.016, 0.015], 10, BRASS, "head", ref=(0, 0, 1), cap0=True, cap1=True)
# The buttons, two rows down the coat's front, brass gone dark; the belt's buckle.
for row, x in (("r", 0.062), ("l", -0.062)):
    for k in range(3):
        z = WAIST + 0.03 + k * 0.085
        iron.blob(Vector((x, 0.112 + 0.004 * k, z)), (0.01, 0.005, 0.01), 6, 2, BRASS, ["spine_01", "spine_02", "spine_02"][k], smooth=False)
iron.box(Vector((0, 0.118, WAIST - 0.035)), (0.032, 0.006, 0.026), BRASS, "pelvis")

# ----------------------------------------------------------------------------------------------------------------
# The chains: loops hanging off the shoulder's iron down the arm and round under the back wheel; and the lash, hanging from
# the cuff (its own bones, so it swings).
chains = kit.part("chains", smooth=False)
LINK = 0.06


def links(path, bones_of, size=LINK, wire=0.0075, width=0.036, twist0=0.0):
    """Links along `path` (points), each its own loop, alternately turned a quarter."""
    pts = [Vector(p) for p in path]
    total = sum((b - a).length for a, b in zip(pts, pts[1:]))
    n = max(1, int(total / (size * 0.95)))
    seg = []
    acc = 0.0
    for a, b in zip(pts, pts[1:]):
        seg.append((acc, a, b))
        acc += (b - a).length

    def at(s):
        for a0, a, b in seg:
            L = (b - a).length
            if s <= a0 + L or (a0, a, b) == seg[-1]:
                return a + (b - a) * ((s - a0) / max(L, 1e-9)), (b - a).normalized()
        return pts[-1], (pts[-1] - pts[-2]).normalized()
    for k in range(n):
        c, d = at((k + 0.5) * total / n)
        side, up, _ = rig.frame_from(d, (0, 0, 1) if abs(d.z) < 0.9 else (1, 0, 0))
        a = twist0 + (math.pi / 2) * (k % 2)
        w = side * math.cos(a) + up * math.sin(a)
        loop = [c + d * (math.cos(t) * size * 0.5) + w * (math.sin(t) * width * 0.5) for t in [2 * math.pi * j / 6 for j in range(6)]]
        chains.tube(loop, [wire] * 6, 3, CHAIN, bones_of(c), ref=[w.cross(d)] * 6, loop=True)


# Off the front lame, a loop down over the upper arm and back up to the back wheel's rim; another hanging lower off the
# elbow's plate; one across the chest to the wheel there.
links([Vector((0.16, 0.08, TOP - 0.02)), Vector((0.3, 0.07, CHEST - 0.08)), Vector((0.36, -0.02, CHEST - 0.1)), Vector((0.3, -0.1, CHEST)),
       Vector((0.2, -0.16, CHEST + 0.06))], lambda c: mix(ARM["r"](c), SPINE(c), smooth01(0.33, 0.2, c.x)))
links([sh.lerp(el, 0.7) + Vector((0, 0.06, -0.04)), sh.lerp(el, 0.85) + Vector((0, 0.04, -0.2)), el.lerp(wr, 0.2) + Vector((0, -0.04, -0.22)),
       el.lerp(wr, 0.2) + Vector((0, -0.06, -0.05))], lambda c: ARM["r"](c), twist0=0.6)
# The lash: shackled to the cuff, hanging plumb at rest down its four bones.
LASH_PTS = [Vector(p) for p in LASH]
LASH_W = chain(LASH_PTS, ["chain_01", "chain_02", "chain_03", "chain_04"], soft=0.02)
links([LASH_PTS[0] + Vector((-0.02, 0, -0.02))] + LASH_PTS[1:], LASH_W, size=0.07, wire=0.008, width=0.04)
# The heavy hook at its end, its weight.
end = LASH_PTS[-1]
chains.tube([end + Vector((0, 0, 0.01)), end + Vector((0, 0, -0.05)), end + Vector((0, 0.03, -0.08)), end + Vector((0, 0.05, -0.05))],
            [0.012, 0.011, 0.01, 0.006], 5, CHAIN, "chain_04", ref=(1, 0, 0), cap1="point")

# ----------------------------------------------------------------------------------------------------------------
# The lamp: a railwayman's hand lamp, brass gone brown, on a short chain off the belt at his right hip; its glass lit low.
lamp = kit.part("lamp", smooth=False)
L0 = LAMP_TOP + Vector((0, 0, -0.2))
# The hook off the belt it hangs from, and its chain: four links down to the bail.
lamp.tube([Vector((0.15, 0.03, 1.02)), Vector((0.21, 0.03, 1.045)), LAMP_TOP + Vector((0, 0, 0.012)), LAMP_TOP + Vector((0.004, 0, -0.012))],
          [0.009, 0.008, 0.007, 0.005], 5, IRON, "pelvis", ref=(0, 1, 0))
lamp.tube([L0 + Vector((0, 0, -0.14)), L0 + Vector((0, 0, -0.13)), L0 + Vector((0, 0, -0.02)), L0], [0.05, 0.054, 0.054, 0.046], 10, BRASS, "lamp",
          ref=(0, 1, 0), cap0=True)
lamp.tube([L0, L0 + Vector((0, 0, 0.03)), L0 + Vector((0, 0, 0.045))], [0.046, 0.03, 0.012], 10, BRASS, "lamp", ref=(0, 1, 0), cap1=True)
lamp.blob(L0 + Vector((0, 0.05, -0.075)), (0.03, 0.012, 0.03), 10, 4, GLASS, "lamp", smooth=True)
lamp.tube([L0 + Vector((0, 0.044, -0.075)), L0 + Vector((0, 0.058, -0.075))], [0.036, 0.036], 10, BRASS, "lamp", ref=(0, 0, 1))
bail = [L0 + Vector((-0.04, 0, 0.0)), L0 + Vector((-0.03, 0, 0.05)), L0 + Vector((0, 0, 0.068)), L0 + Vector((0.03, 0, 0.05)), L0 + Vector((0.04, 0, 0.0))]
lamp.tube(bail, [0.004] * 5, 4, BRASS, "lamp", ref=(0, 1, 0))
for k in range(5):
    c = LAMP_TOP + Vector((0, 0, -0.03 - 0.03 * k))
    w = Vector((0.012, 0, 0)) if k % 2 else Vector((0, 0.012, 0))
    lamp.tube([c + Vector((0, 0, 0.02)), c + w, c + Vector((0, 0, -0.02)), c - w], [0.0045] * 4, 3, CHAIN, "lamp", ref=(0, 0, 1), loop=True)

# ----------------------------------------------------------------------------------------------------------------
# Clips. Axes as tools/blender/passenger.py: an arm lowered from the T-pose by +Y (the right; mirror() the left) and swung
# forward by +X; the elbow by Z; the spine forward by -X; a thigh forward by +X, a knee by -X.


def arm_to(pose, s, target, curl=0.6):
    """The arm on side s reaching `target`, from a spread of starts (rig.reach sticks from the T-pose)."""
    sign = 1 if s == "r" else -1
    best = None
    for x in (-60, 0, 60):
        for y in (20, 60, 90):
            start = dict(pose) | {f"upperarm_{s}": (x, y * sign, 0), f"lowerarm_{s}": (0, 0, 0)}
            p = rig.reach(sk, start, f"upperarm_{s}", f"lowerarm_{s}", target, elbow_axis=2, bend=sign)
            tip = rig.pose_points(sk, p, [(f"lowerarm_{s}", "tail")])[0]
            cost = (tip - target).length
            if best is None or cost < best[0]:
                best = (cost, p)
    p = best[1]
    p[f"fingers_{s}"] = (0, sign * (10 + 70 * curl), 0)
    return p


def hang_chain(pose, trail=(0.0, 0.0, 0.0), swing=0.0):
    """The lash hanging from the cuff: plumb, each link trailing a little more (`trail`, a world rotation of the whole: back
    behind him running, out swinging)."""
    p = dict(pose)
    for k in range(4):
        n = f"chain_{k + 1:02d}"
        p[n] = rig.hang(sk, p, n, trail[0] * (0.6 + 0.2 * k) + swing * (k + 1) * 0.6, trail[1], trail[2] * (0.6 + 0.2 * k))
    p["lamp"] = rig.hang(sk, p, "lamp", trail[0] * 0.5 + swing * 0.5, 0, trail[2] * 0.4)
    return p


# Stooped: the back rounded over, the head thrust forward on the neck and up to look, the knees bent, the long arms hanging
# forward of him, the claws curled.
STOOP = mirror({
    "pelvis": (-12, 0, 0), "spine_01": (-14, 0, 0), "spine_02": (-16, 0, 0), "spine_03": (-14, 0, 0), "neck": (30, 0, 0), "head": (22, 0, 0),
    "clavicle_r": (10, 12, 8), "upperarm_r": (28, 42, 0), "lowerarm_r": (0, 0, 34), "hand_r": (0, 14, 0), "fingers_r": (0, 22, 0), "thumb_r": (0, 10, 0),
    "thigh_r": (24, 0, -8), "calf_r": (-38, 0, 0), "foot_r": (14, 0, -8),
})
STOOP = hang_chain(STOOP)


FEET = [(f"{b}_{s}", w) for b in ("foot", "ball") for s in "lr" for w in ("head", "tail")]


def ground(pose, up=0.0):
    """The pose with the root lifted or dropped so the lowest point of his boots is on the floor, then `up` over it."""
    p = dict(pose)
    low = min(q.z for q in rig.pose_points(sk, p, FEET))
    x, y, z = p.get("root@loc", (0, 0, 0))
    p["root@loc"] = (x, y, z + 0.03 - low + up)
    return p


# Walk (0.8 s, loop): quick, short-stepped, stooped, the arms swinging long and loose, the chain swinging and its end
# dragging; the head held still, looking ahead (two 1 m steps at the sim's 2.5 m/s).
W_CONTACT = over(STOOP, pelvis__loc=(0, 0, -0.03), pelvis=(-8, 0, -6), spine_02=(-14, 0, 4), spine_03=(-12, 0, 4),
                 thigh_r=(40, 0, -4), calf_r=(-14, 0, 0), foot_r=(6, 0, -6),
                 thigh_l=(-14, 0, 4), calf_l=(-30, 0, 0), foot_l=(-14, 0, 6),
                 upperarm_r=(2, 42, 0), upperarm_l=(38, -52, 0), lowerarm_l=(0, 0, -30))
W_PASS = over(STOOP, pelvis__loc=(0, 0, 0.01), thigh_r=(14, 0, -4), calf_r=(-24, 0, 0), foot_r=(4, 0, -6),
              thigh_l=(38, 0, 4), calf_l=(-80, 0, 0), foot_l=(16, 0, 6))
walk = Clip("walk")
for f, p, swing in ((0, W_CONTACT, -1), (6, W_PASS, 0)):
    walk.key(f, hang_chain(p, (-14, 0, 0), swing * 12))
for f, p, swing in ((12, W_CONTACT, 1), (18, W_PASS, 0)):
    q = swap(p) | {"neck": p["neck"], "head": p["head"], "pelvis": (p["pelvis"][0], 0, -p["pelvis"][2])}
    walk.key(f, hang_chain(q, (-14, 0, 0), swing * 12))
walk.close(24)

# Wind (1.2 s, loop): bent right over a car's brake wheel in front of him (its rim 0.47 m up, 0.48 m ahead), both clawed
# hands on it, wrenching it round hand over hand: a half-turn a beat, the shoulders throwing into it, a jerk at each
# ratchet's catch. (CreatureArt stands him here, facing the wheel.)
WHEEL_AT = Vector((0, 0.5, 0.47))
WHEEL_R = 0.26
BENT = mirror({
    "pelvis": (-30, 0, 0), "spine_01": (-18, 0, 0), "spine_02": (-14, 0, 0), "spine_03": (-8, 0, 0), "neck": (34, 0, 0), "head": (14, 0, 0),
    "thigh_r": (52, 0, -8), "calf_r": (-66, 0, 0), "foot_r": (16, 0, -6), "clavicle_r": (10, 8, 6),
})
BENT = ground(over(BENT, pelvis__loc=(0, -0.12, 0)))
wind = Clip("wind")
# (A beat: both hands push the rim round a quarter-turn, the ratchet catches, they let go and come back for the next.)
for f, a, lift in ((0, 0.0, 0.0), (12, 0.45, 0.0), (15, 0.45, 0.06), (21, 0.0, 0.06)):
    ang = math.pi * a
    q = over(BENT, spine_02=(-14, 0, 10 * a), spine_03=(-8 + 4 * a, 0, 8 * a))
    for s, off in (("r", -0.5), ("l", math.pi + 0.5)):
        t = ang + off
        grip = WHEEL_AT + Vector((math.cos(t) * WHEEL_R, -math.sin(t) * WHEEL_R, 0.03 + lift))
        q = arm_to(q, s, grip, curl=1.0 - lift * 8)
    wind.key(f, hang_chain(q, (10, 0, 0)), "LINEAR" if f in (12, 15) else "BEZIER")
wind.close(24)

# Flee (0.56 s, loop): low and fast, bent nearly double, long strides, the arms pumping, the chain flying out behind
# (the sim's 5 m/s: two 1.4 m strides).
F_REACH = over(STOOP, pelvis__loc=(0, 0, -0.08), pelvis=(-26, 0, -8), spine_01=(-14, 0, 4), spine_02=(-10, 0, 4), spine_03=(-8, 0, 4),
               neck=(40, 0, 0), head=(20, 0, 0),
               thigh_r=(64, 0, -4), calf_r=(-30, 0, 0), foot_r=(10, 0, -6), thigh_l=(-24, 0, 4), calf_l=(-50, 0, 0), foot_l=(-20, 0, 6),
               upperarm_r=(-20, 40, 0), lowerarm_r=(0, 0, 40), upperarm_l=(50, -42, 0), lowerarm_l=(0, 0, -50))
F_FLY = over(F_REACH, pelvis__loc=(0, 0, 0.02), thigh_r=(30, 0, -4), calf_r=(-60, 0, 0), thigh_l=(20, 0, 4), calf_l=(-100, 0, 0),
             upperarm_r=(14, 42, 0), upperarm_l=(14, -42, 0))
flee = Clip("flee")
for f, p in ((0, F_REACH), (4, F_FLY)):
    flee.key(f, hang_chain(p, (-60, 0, 0)), "LINEAR")
for f, p in ((8, F_REACH), (12, F_FLY)):
    q = swap(p) | {k: p[k] for k in ("neck", "head")} | {"pelvis": (p["pelvis"][0], 0, -p["pelvis"][2])}
    flee.key(f, hang_chain(q, (-60, 0, 0)), "LINEAR")
flee.close(16)

# Jump (0.8 s, once): a gap, crouched: down onto his haunches, sprung, tucked over it with the arms out in front, landing
# low on the far roof two metres on.
CROUCH = over(STOOP, pelvis__loc=(0, 0, -0.28), pelvis=(-34, 0, 0), thigh_r=(86, 0, -6), calf_r=(-110, 0, 0), foot_r=(24, 0, -6),
              thigh_l=(86, 0, 6), calf_l=(-110, 0, 0), foot_l=(24, 0, 6), upperarm_r=(14, 46, 0), upperarm_l=(14, -46, 0))
TUCK = over(CROUCH, pelvis__loc=(0, 1.0, 0.5), pelvis=(-20, 0, 0), thigh_r=(90, 0, -6), calf_r=(-100, 0, 0), thigh_l=(70, 0, 6),
            calf_l=(-90, 0, 0), upperarm_r=(74, 44, 0), lowerarm_r=(0, 0, 8), upperarm_l=(60, -50, 0), lowerarm_l=(0, 0, -20))
jump = Clip("jump", loop=False)
jump.key(0, STOOP, "BEZIER")
jump.key(5, hang_chain(ground(CROUCH), (10, 0, 0)), "LINEAR")
jump.key(9, hang_chain(ground(over(TUCK, pelvis__loc=(0, 0.4, 0)), 0.35), (-40, 0, 0)), "LINEAR")
jump.key(15, hang_chain(ground(TUCK, 0.45), (-50, 0, 0)), "LINEAR")
jump.key(19, hang_chain(ground(over(CROUCH, pelvis__loc=(0, 2.0, 0))), (30, 0, 0)), "LINEAR")
jump.key(24, hang_chain(ground(over(STOOP, pelvis__loc=(0, 2.0, 0))), (10, 0, 0)), "BEZIER")


def at(pose, x, y, z, turn=0.0, tip=0.0, lean=0.0):
    """The pose grounded, then carried to (x, y, z) from where he stood and turned `turn` degrees about the vertical (+: to
    his left), tipped `tip` over sideways (+: to his right) and `lean` forward."""
    p = ground(pose)
    _, _, dz = p["root@loc"]
    p["root@loc"] = (x, y, dz + z)
    p["root"] = (lean, tip, turn)
    return p


# Drop (1.2 s, once): over the side: a step out to the roof's edge, down onto it, a hand on the eave, swung over and down
# the side out of sight (the sim has him gone the moment he's over: CreatureArt plays this where he was).
drop = Clip("drop", loop=False)
drop.key(0, STOOP, "BEZIER")
drop.key(6, hang_chain(at(CROUCH, 0.9, 0.0, 0.0, turn=-70)), "LINEAR")
drop.key(14, hang_chain(at(over(CROUCH, pelvis=(-10, 0, 0)), 1.5, 0.0, -0.9, turn=-90, tip=-20)), "LINEAR")
drop.key(26, hang_chain(at(STOOP, 1.7, 0.0, -2.4, turn=-90, tip=-8)), "LINEAR")
drop.key(36, hang_chain(at(STOOP, 1.75, 0.0, -3.4, turn=-90)), "CONSTANT")

# Climb (2 s, once: the sim's climbSeconds): up the end ladder behind him, facing it, hand over hand, and over the roof's
# end onto it.
climb = Clip("climb", loop=False)
LADDER = over(mirror({"upperarm_r": (60, 20, 0), "lowerarm_r": (0, 0, 60), "thigh_r": (50, 0, 0), "calf_r": (-70, 0, 0), "fingers_r": (0, 70, 0)}),
              spine_01=(6, 0, 0), neck=(-6, 0, 0))
for f, z in ((0, -1.7), (12, -1.2), (24, -0.6)):
    k = (f // 12) % 2
    q = over(LADDER, upperarm_r=(80 if k else 40, 20, 0), upperarm_l=(40 if k else 80, -20, 0),
             thigh_r=(60 if k else 20, 0, 0), thigh_l=(20 if k else 60, 0, 0))
    climb.key(f, hang_chain(at(q, 0, -0.62, z)), "LINEAR")
climb.key(36, hang_chain(at(over(CROUCH, pelvis=(-50, 0, 0), upperarm_r=(70, 40, 0), upperarm_l=(70, -40, 0)), 0, -0.45, 0.0)), "LINEAR")
climb.key(48, hang_chain(at(CROUCH, 0, -0.1, 0.0)), "BEZIER")
climb.key(60, STOOP, "BEZIER")

# Cornered (1.6 s, loop): backing, crouched low, the right arm up with the chain swinging round over his head, the left
# hand clawing out in front, the head turning between the two of them, wheezing.
GUARD = over(CROUCH, pelvis__loc=(0, 0, -0.16), pelvis=(-20, 0, 0), spine_02=(-6, 0, 0), spine_03=(-2, 0, 0), neck=(20, 0, 0),
             thigh_r=(56, 0, -10), calf_r=(-80, 0, 0), thigh_l=(40, 0, 10), calf_l=(-70, 0, 0),
             upperarm_r=(70, 10, 0), lowerarm_r=(0, 0, 70), hand_r=(0, -10, 0), upperarm_l=(50, -40, 0), lowerarm_l=(0, 0, -40),
             fingers_l=(0, -30, 0))
cornered = Clip("cornered")
for f, (turn, lift, sw) in enumerate(((0, 0, 0), (-30, 0.02, 1), (0, 0, 2), (30, 0.02, 3))):
    q = over(GUARD, neck=(20, 0, turn * 0.5), head=(10, 0, turn * 0.7), pelvis__loc=(0, -0.05 * (f % 2), -0.16 + lift),
             spine_03=(-2 + 3 * math.sin(f * 1.6), 0, 0))
    cornered.key(f * 12, hang_chain(q, (0, 0, 0), swing=40 * math.sin(sw * math.pi / 2)), "BEZIER")
cornered.close(48)

# Lash (0.8 s, once): the chain wound back over his shoulder and whipped down and across at a face in front of him; the
# strike at 0.5 s.
WIND_UP = over(GUARD, spine_02=(-4, 0, 30), spine_03=(4, 0, 20), upperarm_r=(30, -20, 40), lowerarm_r=(0, 0, 90), neck=(20, 0, -20))
STRIKE = over(GUARD, pelvis__loc=(0, 0.2, -0.12), spine_02=(-16, 0, -30), spine_03=(-12, 0, -20), upperarm_r=(100, 50, -30),
              lowerarm_r=(0, 0, 10), neck=(24, 0, 10))
lash = Clip("lash", loop=False)
lash.key(0, hang_chain(GUARD), "BEZIER")
lash.key(9, hang_chain(WIND_UP, (-90, 0, -60)), "LINEAR")
lash.key(15, hang_chain(STRIKE, (40, 0, 80)), "CONSTANT")
lash.key(19, hang_chain(STRIKE, (70, 0, 50)), "LINEAR")
lash.key(24, hang_chain(GUARD), "BEZIER")

hit = Clip("hit", loop=False)
hit.key(0, STOOP, "CONSTANT")
hit.key(2, hang_chain(over(STOOP, spine_02=(6, 0, 14), spine_03=(10, 0, 10), neck=(10, 0, 20), head=(10, 0, 20), pelvis__loc=(0, -0.06, 0)),
                      (20, 0, 30)), "CONSTANT")
hit.key(9, hang_chain(over(STOOP, spine_02=(-18, 0, -4))), "LINEAR")
hit.key(14, STOOP, "CONSTANT")

# Death (1.4 s, once): struck the last time, he reels, his knees go, and he pitches over sideways off the roof's edge.
death = Clip("death", loop=False)
death.key(0, STOOP, "CONSTANT")
death.key(4, hang_chain(over(STOOP, spine_02=(10, 0, 20), neck=(10, 0, 30), head=(16, 0, 20), pelvis__loc=(0, -0.08, 0))), "LINEAR")
death.key(14, hang_chain(at(over(CROUCH, upperarm_l=(0, -20, 0), upperarm_r=(0, 30, 0)), 0.5, -0.1, 0.0, tip=30)), "LINEAR")
death.key(26, hang_chain(at(over(STOOP, upperarm_r=(50, 30, 0), upperarm_l=(0, -10, 0)), 1.4, -0.2, -0.6, tip=85, turn=10)), "LINEAR")
death.key(42, hang_chain(at(over(STOOP, upperarm_r=(50, 30, 0), upperarm_l=(0, -10, 0)), 1.9, -0.25, -2.4, tip=120, turn=15)), "LINEAR")

CLIPS = [walk, wind, flee, jump, drop, climb, cornered, lash, hit, death]

# One skin from the cap to the soles (flesh.fuse, settled on the field, then QuadriFlow); the rest over it.
kit.build()
fl.fuse(kit, "body", ["body"], voxel=0.0045, faces=SKIN_FACES, lose=0.03, settle=F.settle, relax=3)
fl.fuse(kit, "head", ["head"], voxel=0.0022, faces=HEAD_FACES, lose=0.03, settle=HF.settle, relax=1)
rig.bake(sk, CLIPS, plant=rig.feet_planter(sk, lowest=0.02, clips=["walk", "flee", "cornered", "lash", "hit"]))
print("[dt] brakeman", {p.name: p.tris() for p in kit.parts}, "bones", len(sk.bones))
out = rig.args()[0] if rig.args() else "brakeman.glb"
rig.export(out, kit)
if __name__ != "overbake_source":
    rig.export_lod(out.replace(".glb", ".lod1.glb"), kit, 0.4)
