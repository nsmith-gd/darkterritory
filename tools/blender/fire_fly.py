"""FIRE FLIES (GDD v1.2 §21 lit cars, App. A.5 · light): "Swarm to the lamps inside cars. Linger long enough and the
car catches fire."

Not flies: moths, big ones, a hand across, come in out of the dark to a lamp the way moths do, and wrong the way moths
are when you look at one close. The body furred the grey of ash; the abdomen a fat segmented grub of a thing, black
plates, and between the plates it's alight, an ember glowing through the joins and a coal at its tail. The wings are
charred paper, tattered, their edges still smouldering, and on each forewing an eye: a human one, pale, almond, the
iris dark, so that settled with its wings folded on the lamp's glass each moth is a pair of eyes looking back at you,
and a lamp they've found is a cluster of them. The head is small, the eyes on it dull red in the light, the antennae
feathered.

SK_FireFly: root, thorax, head, the abdomen's two bones, four wings and six legs. Faces +Y (the engine's -Z), its back
+Z. Clips (§31: still, then too fast): flutter (flying, the slow heavy beat of a big moth, the abdomen pumping), settle
(on the glass, the wings folded back flat over it, crawling a step at a time, the abdomen throbbing; then the wings
flick half open on the eyes, and shut).

    tools/models/build.sh fire_fly        # this, its high copy and the bake -> content/art/models/fire_fly.glb
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import rig  # noqa: E402
from rig import Bone, Clip, Mat, Skeleton, Vector, hexc, mirror, noise3  # noqa: E402

rig.reset()
# The wings flat out to the sides at rest: each from its root on the thorax, its span out along X and its chord along
# Y (the leading edge ahead), swept back towards the tip. (span, chord, sweep at the tip, its root.)
WINGS = {"fore": (0.072, 0.044, 0.022, (0.006, 0.004, 0.0068)), "hind": (0.05, 0.04, 0.01, (0.0055, -0.004, 0.0058))}
# The legs: where each comes off the thorax's underside, its knee, its foot (the right side's; the left mirrored).
LEGS = {"f": ((0.003, 0.009, -0.006), (0.014, 0.02, -0.006), (0.02, 0.028, -0.02)),
        "m": ((0.004, 0.001, -0.007), (0.018, 0.004, -0.007), (0.026, 0.0, -0.022)),
        "b": ((0.003, -0.006, -0.006), (0.016, -0.014, -0.007), (0.022, -0.026, -0.022))}
bones = [Bone("root", None, (0, 0, 0), (0, 0.02, 0)),
         Bone("thorax", "root", (0, -0.009, 0), (0, 0.012, 0)),
         Bone("head", "thorax", (0, 0.012, 0), (0, 0.02, 0)),
         Bone("abdomen_1", "thorax", (0, -0.009, -0.001), (0, -0.028, -0.004)),
         Bone("abdomen_2", "abdomen_1", (0, -0.028, -0.004), (0, -0.048, -0.008))]
for side in (1, -1):
    sfx = "_r" if side > 0 else "_l"
    for name, (span, chord, sweep, (x, y, z)) in WINGS.items():
        bones.append(Bone(f"{name}{sfx}", "thorax", (side * x, y, z), (side * (x + span), y - sweep, z)))
    for name, (a, _, c) in LEGS.items():
        bones.append(Bone(f"leg_{name}{sfx}", "thorax", (side * a[0], a[1], a[2]), (side * c[0], c[1], c[2])))
sk = Skeleton("SK_FireFly", bones)
sk.build()
kit = rig.Kit(sk, "fire_fly")


def H(name):
    return sk[name].head.copy()


def T(name):
    return sk[name].tail.copy()


FUR = Mat("wool.firefly", hexc("#6a6058"), shine=0.04)
WING = Mat("tar.firefly_wing", hexc("#57504a"), shine=0.06)
HIND = Mat("tar.firefly_hindwing", hexc("#57504a"), shine=0.06)     # (the same paper: apart so the bake paints the eyes on the fore only)
PLATE = Mat("tar.firefly", hexc("#1a1512"), shine=0.35)
EMBER = Mat("ember_crack.firefly", hexc("#e8782c"), shine=0.1, glow=0.9)
CORE = Mat("ember_core.firefly", hexc("#ffb454"), emissive=1.0)
RIM = Mat("ember_core.firefly_rim", hexc("#a8401a"), emissive=1.0)
EYE = Mat("eye.firefly", hexc("#300a06"), emissive=1.0)


def bell(x):
    return math.exp(-x * x)


moth = kit.part("moth")

# The thorax: a furred lump, the fur shaggy at its collar and over the wing roots.
moth.blob(Vector((0, 0.002, 0.0005)), (0.0085, 0.0125, 0.0085), 10, 7, FUR, "thorax",
          shape=lambda i, j, a, th, p: Vector(p) * (1 + 0.12 * noise3(Vector(p) * 900, 41, 1.0)))
# The head, tucked under the collar, and its eyes: big, dull, red in the light.
moth.blob(H("head") + Vector((0, 0.003, -0.001)), (0.0055, 0.005, 0.005), 8, 5, FUR, "head")
for side in (1, -1):
    moth.blob(H("head") + Vector((side * 0.0042, 0.004, -0.0005)), (0.0032, 0.0032, 0.0034), 6, 4, EYE, "head")
    # The antennae: feathered, swept up and out and back, a comb of barbs down each.
    base = H("head") + Vector((side * 0.0025, 0.0065, 0.003))
    shaft = [base + Vector((side * 0.006 * f, 0.016 * f - 0.004 * f * f, 0.009 * f)) for f in (0.0, 0.5, 1.0)]
    moth.tube(shaft, [0.0007, 0.0006, 0.0003], 3, PLATE, "head", ref=(0, 0, 1), cap1="point")
    for k in range(1, 5):
        f = k / 5
        root = base + Vector((side * 0.006 * f, 0.016 * f - 0.004 * f * f, 0.009 * f))
        ln = 0.0055 * (1 - 0.6 * f)
        for s2 in (1, -1):
            tip = root + Vector((side * ln * 0.7 * (s2 > 0), -ln * 0.5, ln * 0.4 * s2))
            moth.tube([root, tip], [0.00035, 0.0001], 3, PLATE, "head", ref=(0, 0, 1))

# The abdomen: a fat grub of plates, swollen in the middle, the joins between them alight; a coal at its tail.
SEGS = 6
pts, radii, mats = [], [], []
for k in range(SEGS * 2 + 1):
    f = k / (SEGS * 2)
    p = H("abdomen_1").lerp(T("abdomen_1"), f * 2) if f <= 0.5 else H("abdomen_2").lerp(T("abdomen_2"), (f - 0.5) * 2)
    r = 0.0035 + 0.0055 * math.sin(math.pi * min(1.0, 0.25 + f * 0.95)) ** 0.8
    # Each plate's edge stands proud of the join behind it.
    r *= 1.0 if k % 2 == 0 else 0.86
    pts.append(p)
    radii.append((r, r * 0.88))


def abdomen_weights(p):
    d1, d2 = sk.segment_distance("abdomen_1", p), sk.segment_distance("abdomen_2", p)
    w1 = 1.0 / (d1 + 0.004) ** 4
    w2 = 1.0 / (d2 + 0.004) ** 4
    return {"abdomen_1": w1 / (w1 + w2), "abdomen_2": w2 / (w1 + w2)}


def abdomen_mat(fpts, n):
    # The joins (the rings between plates) glow; a plate's under its edge.
    c = sum(fpts, Vector()) / len(fpts)
    along = (H("abdomen_1") - c).length / ((T("abdomen_2") - H("abdomen_1")).length)
    k = along * SEGS * 2
    return EMBER if abs(k - round(k)) < 0.5 and round(k) % 2 == 1 else PLATE


moth.tube(pts, radii, 8, PLATE, abdomen_weights, ref=(0, 0, 1), fmat=abdomen_mat)
moth.blob(T("abdomen_2") + Vector((0, 0.0012, 0.0002)), (0.0028, 0.0034, 0.0026), 6, 4, CORE, "abdomen_2")


def wing(name, side):
    """A wing as a flat, thin lozenge: narrow at its root, broad out to the tip and swept back, the trailing edge
    tattered; the outer band of faces the smouldering rim."""
    span, chord, sweep, (rx, ry, rz) = WINGS[name]
    bone = f"{name}{'_r' if side > 0 else '_l'}"
    root = Vector((side * rx, ry, rz))
    c = root + Vector((side * span / 2, 0, 0))
    thick = 0.0009
    seed = 61 + (name == "hind") * 7 + (side < 0) * 3

    def shape(i, j, a, th, p):
        p = Vector(p)
        lx, ly, lz = (p.x - c.x) * side, p.y - c.y, p.z - c.z
        # The blob's rings are lat-long: push them out so the faces between the outermost two are a thin band.
        rr = math.hypot(lx / (span / 2), ly / (chord / 2))
        if rr > 1e-6:
            k = rr ** 0.3 / rr
            lx, ly = lx * k, ly * k
        s = (lx / (span / 2) + 1) / 2                       # 0 at the root, 1 at the tip
        width = 0.36 + 0.64 * math.sin(min(1.0, s * 1.3) * math.pi / 2)
        if name == "fore":
            # The forewing's tip hooked back a little (falcate), its leading edge straight.
            ly = ly * width - sweep * s ** 1.6 + (0.25 * chord * s ** 3 if ly < 0 else 0.0)
        else:
            ly = ly * width * 1.1 - sweep * s ** 1.3
        # Tatters: notches bitten out of the trailing edge and the tip.
        if math.cos(a) < 0.3 and s > 0.35:
            notch = sum(bell((s - n) / 0.05) for n in (0.48, 0.71, 0.9))
            ly += chord * 0.22 * notch * (1 if math.cos(a) < 0 else 0.4) * (1 + 0.5 * noise3(Vector((s, side, 0)) * 7, seed, 1.0))
        # The membrane ribbed by its veins, and buckled.
        lz += 0.0006 * math.sin(s * 9 + ly * 300) + 0.0012 * s * s * math.sin(ly * 140 + seed)
        out = Vector((root.x + side * (s * span), c.y + ly, c.z + lz))
        if i == 2:
            OUTLINE.add(_key(out))
        return out

    paper = WING if name == "fore" else HIND
    moth.blob(c, (span / 2, chord / 2, thick), 14, 4, paper, bone, shape=shape,
              fmat=lambda fpts, n: RIM if _on_rim(fpts, root, side, span) else paper)


# The wings' outlines (the blob's middle ring, i = 2 of 4), by where their vertices end up: a face with two corners on
# it is in the outer band.
OUTLINE = set()


def _key(p):
    return (round(p.x, 6), round(p.y, 6), round(p.z, 6))


def _on_rim(fpts, root, side, span):
    """On the outline band, and out past the wing's middle: the root half stays paper."""
    s = sum((p.x - root.x) * side for p in fpts) / len(fpts) / span
    return sum(1 for p in fpts if _key(p) in OUTLINE) >= 2 and s > 0.42


for side in (1, -1):
    wing("hind", side)
    wing("fore", side)

# The legs: thin, jointed, spined, folded under it; each a bone.
for side in (1, -1):
    sfx = "_r" if side > 0 else "_l"
    for name, (a, k, f) in LEGS.items():
        A, K, F = (Vector((side * v[0], v[1], v[2])) for v in (a, k, f))
        moth.tube([A, K, F], [0.0011, 0.0008, 0.0003], 4, PLATE, f"leg_{name}{sfx}", ref=(0, 0, 1), cap1="point")
        # A tuft of fur at the hip.
        moth.blob(A, (0.0018, 0.0018, 0.0015), 5, 3, FUR, "thorax")


# ----------------------------------------------------------------------------------------------------------------
# Clips. Angles in the armature's axes: the right wing (out along +X) beats up by -Y and folds back by -Z; the right
# legs swing forward by +Z and lift by -Y (rig.rot); mirror() gives the left.

def wings(fore, hind=None, tent=0.0):
    """fore, hind: (ry, rz) for the right wings; `tent` rolls each about its span first, the leading edge down (folded
    back, the leading edge is the outer one: a roof over the body)."""
    hind = hind or fore
    return mirror({"fore_r": (-tent, fore[0], fore[1]), "hind_r": (-tent, hind[0], hind[1])})


def legs(f=0.0, m=0.0, b=0.0, lift=0.0, legs_down=0.0):
    out = {"leg_f_r": (-legs_down, lift, f), "leg_m_r": (-legs_down * 0.5, lift, m), "leg_b_r": (legs_down * 0.3, lift, b)}
    return mirror(out)


# Flutter (0.27 s, loop): the heavy slow beat of a big moth, the hindwings a frame behind the fore; it rises on the
# downstroke and sinks on the up; the abdomen pumps against the beat; the legs hang, dangling.
DANGLE = legs(10, 0, -10, lift=18, legs_down=12)
flutter = Clip("flutter")
flutter.key(0, wings((-58, 4), (-44, 0)) | DANGLE | {"abdomen_1": (6, 0, 0), "abdomen_2": (6, 0, 0), "root@loc": (0, 0, -0.003)}, "LINEAR")
flutter.key(2, wings((-10, -4), (-32, 0)) | DANGLE | {"abdomen_1": (0, 0, 0), "abdomen_2": (0, 0, 0), "root@loc": (0, 0, 0.0)}, "LINEAR")
flutter.key(4, wings((40, -10), (26, -6)) | DANGLE | {"abdomen_1": (-8, 0, 0), "abdomen_2": (-10, 0, 0), "root@loc": (0, 0, 0.004)}, "LINEAR")
flutter.key(6, wings((4, -2), (30, -6)) | DANGLE | {"abdomen_1": (-2, 0, 0), "abdomen_2": (-3, 0, 0), "root@loc": (0, 0, 0.002)}, "LINEAR")
flutter.close(8)

# Settle (3.2 s, loop): on the glass, the wings folded back flat and overlapping over the abdomen (the eyes on them
# side by side), dead still but for the abdomen throbbing; a crawl, one leg at a time and then a lurch; a shiver of
# the wings; and the flash: the wings snapped half open, held, the eyes on show, and shut.
FOLD = wings((4, -79), (6, -72), tent=16)
GRIP = legs(6, 0, -6, lift=-4)
settle = Clip("settle")
settle.key(0, FOLD | GRIP | {"abdomen_1@scale": (1, 1, 1)}, "BEZIER")
settle.key(14, FOLD | GRIP | {"abdomen_1@scale": (1.12, 1.0, 1.12), "abdomen_2@scale": (1.08, 1.0, 1.08)}, "BEZIER")
settle.key(28, FOLD | GRIP | {"abdomen_1@scale": (1, 1, 1), "abdomen_2@scale": (1, 1, 1)}, "BEZIER")
# The crawl: a foreleg reached forward, held; then the whole of it lurches after it.
settle.key(34, FOLD | GRIP | {"leg_f_r": (0, -16, 24)}, "CONSTANT")
settle.key(40, FOLD | GRIP | {"leg_f_l": (0, 16, -24), "leg_m_r": (0, -10, 14)}, "CONSTANT")
settle.key(43, FOLD | legs(-10, -8, -18, lift=-4) | {"root@loc": (0, 0.006, 0)}, "LINEAR")
settle.key(46, FOLD | GRIP | {"root@loc": (0, 0.0, 0)}, "BEZIER")
settle.key(58, FOLD | GRIP | {"abdomen_1@scale": (1.12, 1.0, 1.12), "abdomen_2@scale": (1.08, 1.0, 1.08)}, "BEZIER")
# The shiver: the folded wings buzz, three frames, no more.
settle.key(64, wings((0, -77), (2, -70), tent=14) | GRIP, "CONSTANT")
settle.key(65, wings((8, -81), (8, -74), tent=18) | GRIP, "CONSTANT")
settle.key(66, wings((1, -76), (4, -70), tent=14) | GRIP, "CONSTANT")
settle.key(67, FOLD | GRIP, "BEZIER")
# The flash: snapped half open on the eyes, and held, still; then shut, as fast.
settle.key(76, FOLD | GRIP, "CONSTANT")
settle.key(78, wings((-6, -28), (2, -30)) | GRIP | {"abdomen_1": (-6, 0, 0)}, "CONSTANT")
settle.key(90, wings((-6, -28), (2, -30)) | GRIP | {"abdomen_1": (-6, 0, 0)}, "CONSTANT")
settle.key(92, FOLD | GRIP, "BEZIER")
settle.close(96)

kit.build()
rig.bake(sk, [flutter, settle])
print("[dt] fire_fly", {p.name: p.tris() for p in kit.parts}, "total", kit.tris(), "bones", len(sk.bones))
rig.export(rig.args()[0] if rig.args() else "fire_fly.glb", kit)
