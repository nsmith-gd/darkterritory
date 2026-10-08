"""RIBBITS (GDD v1.2 §21 yards and villages, App. A.6): tools/blender/ribbit.py's toad-rabbit with its colour and its
fine relief baked into one 2048 atlas (the Look Review: organic and wet, not primitives; the Gannet's way, note 340).

tools/blender/ribbit.py stays its source: the rig, the fused skin and the parts over it, the clips. This recipe runs
it, unwraps every part into one atlas (the face, the eyes, the mouth and the hands given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the skin warted all over the back,
    the haunches and the head's top, wrinkled across like a hairless rat's; the belly creased where it hangs; the sac
    in slack folds; the glands pitted; the ears' veins standing in the bare skin;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render: each texel's position,
    normal and material rasterized from the unwrap): grey-white and wet as something that lives under a stone,
    blotched grey-green down the back, the warts darker; the belly, the throat's sac, the palms and the soles paler,
    faintly pink, veined; the ears' bare skin pink and thin, red veins branching up them; the eyes milky, a toad's bar of
    pupil clouded over in each; the gums and the roof of the mouth dark wet red, the tongue paler; the teeth yellowed
    human teeth, brown at the roots; the nails yellow-grey.
Wet all over (rough 0.2-0.3); the eyes glassy.

    tools/models/build.sh ribbit        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/ribbit.py)
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402,F401  (first: as a Python module, Blender's bmesh is only importable after it)
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import bell, fine, smooth01  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("ribbit.py")
print("[dt] ribbit parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = {s: np.array(v, np.float32) for s, v in g["EYES"].items()}
THROAT = np.array(g["THROAT"], np.float32)
HC = np.array(g["HC"], np.float32)
JC = np.array(g["JC"], np.float32)
LIP = np.array([tuple(p) for p in g["LIP"]], np.float32)
LOWLIP = np.array([tuple(p) for p in g["LOWLIP"]], np.float32)
TIPS = np.array([tuple(p) for p in g["TIPS"]], np.float32)
TEETH_AT = np.array([tuple(p) for p in g["TEETH_AT"]], np.float32)


def suffix(name):
    """A kit material's region: "skin.ribbit_belly" -> "ribbit_belly" (Blender's ".001" dropped)."""
    return name.split(".")[1]


# The high copy's dress: only its relief and occlusion are baked (the colour is painted below), so one plain grey each,
# subdivided over the skin's forms, kept edged on the teeth and nails.
DRESS = {"ribbit": 3, "ribbit_belly": 3, "ribbit_ear": 2, "ribbit_eye": 2, "ribbit_gum": 2, "ribbit_tongue": 2,
         "ribbit_teeth": 0, "ribbit_nail": 0}


def dress(m):
    return make.flat("ribbit_high", (0.3, 0.3, 0.3), rough=0.3), DRESS[suffix(m.name)]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def warts_of(p):
    """The small warts, in their hundreds: cells raised on the back, the haunches and the head, few on the belly."""
    top = smooth01(0.22, 0.45, p[:, 2])
    return smooth01(0.58, 0.88, cook.noise_np(p, 1001, 55.0) * 0.5 + 0.5) * (0.25 + 0.75 * top)


def veins_of(p, scale=22.0):
    """A branching net: two scales of ridged noise's crests."""
    return np.maximum(smooth01(0.86, 0.95, ridged(p, 1031, scale)), 0.7 * smooth01(0.9, 0.97, ridged(p, 1032, scale * 2.3)))


def sac_of(p):
    return np.clip(1.4 - np.linalg.norm((p - THROAT) / np.array([0.13, 0.12, 0.08], np.float32), axis=1), 0, 1)


def skin_shape(p, n):
    # Wrinkled like a hairless rat's: folds across the body, deep; warts all over the back; the sac's slack folds.
    d = 0.0024 * warts_of(p) + 0.0014 * (ridged(p * np.array([1.0, 0.35, 1.0], np.float32), 1002, 40.0) ** 3 - 0.3)
    d += 0.0012 * np.sin(p[:, 2] * 150 + 3 * cook.noise_np(p, 1004, 7.0)) * sac_of(p)
    return d + fine(p, 0.00018, 500, 1003)


def belly_shape(p, n):
    # Loose skin, creased across where it hangs; the sac's folds; the palms' and soles' creases.
    d = 0.001 * np.sin(p[:, 1] * 130 + 4 * cook.noise_np(p, 1011, 6.0)) + 0.0013 * np.sin(p[:, 2] * 150 + 3 * cook.noise_np(p, 1004, 7.0)) * sac_of(p)
    return d + 0.0005 * veins_of(p) + fine(p, 0.00012, 500, 1012)


def ear_shape(p, n):
    return 0.0007 * veins_of(p, 18.0) + fine(p, 0.0001, 500, 1021)


SHAPE = {"skin.ribbit_belly": belly_shape, "skin.ribbit_ear": ear_shape, "skin.ribbit_teeth": lambda p, n: 0 * p[:, 0],
         "skin.ribbit_nail": lambda p, n: 0 * p[:, 0], "skin.ribbit": skin_shape}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] ribbit highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    return FACE if suffix(m.name) in ("ribbit_eye", "ribbit_teeth", "ribbit_gum", "ribbit_tongue", "ribbit_nail") else 0


atlas = overbake.Atlas("ribbit", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 2.0})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "jaw": (0.006, 0.02), "head": (0.004, 0.012), "paws": (0.003, 0.01),
                          "ears": (0.004, 0.012)}, samples=16, height=1.0)

# --- where each texel is (the Gannet's way: rasterized from the unwrap at rest) -------------------------------------
low = atlas.low
me = low.data
me.calc_loop_triangles()
nv, nl = len(me.vertices), len(me.loops)
co = np.empty(nv * 3, np.float32)
me.vertices.foreach_get("co", co)
co = co.reshape(-1, 3)
no = np.empty(nv * 3, np.float32)
me.vertices.foreach_get("normal", no)
no = no.reshape(-1, 3)
uv = np.empty(nl * 2, np.float32)
me.uv_layers["UVMap"].data.foreach_get("uv", uv)
uv = uv.reshape(-1, 2)
lv = np.empty(nl, np.int32)
me.loops.foreach_get("vertex_index", lv)
nt = len(me.loop_triangles)
tl = np.empty(nt * 3, np.int32)
me.loop_triangles.foreach_get("loops", tl)
tl = tl.reshape(-1, 3)
tp = np.empty(nt, np.int32)
me.loop_triangles.foreach_get("polygon_index", tp)
REGIONS = sorted({suffix(n) for n in atlas.slot_name})
region_of = np.array([REGIONS.index(suffix(n)) for n in atlas.slot_name], np.int32)
P = np.zeros((S, S, 3), np.float32)
N = np.zeros((S, S, 3), np.float32)
K = np.full((S, S), -1, np.int32)
PART = np.full((S, S), -1, np.int32)
for t in range(nt):
    loops = tl[t]
    if atlas.keep[tp[t]]:
        continue
    x, y = uv[loops][:, 0] * S - 0.5, (1 - uv[loops][:, 1]) * S - 0.5
    x0, x1 = int(max(0, np.floor(x.min()))), int(min(S - 1, np.ceil(x.max())))
    y0, y1 = int(max(0, np.floor(y.min()))), int(min(S - 1, np.ceil(y.max())))
    if x1 < x0 or y1 < y0:
        continue
    gx, gy = np.meshgrid(np.arange(x0, x1 + 1, dtype=np.float32), np.arange(y0, y1 + 1, dtype=np.float32))
    d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
    if abs(d) < 1e-12:
        continue
    a = ((y[1] - y[2]) * (gx - x[2]) + (x[2] - x[1]) * (gy - y[2])) / d
    b = ((y[2] - y[0]) * (gx - x[2]) + (x[0] - x[2]) * (gy - y[2])) / d
    c = 1 - a - b
    inside = (a >= -0.02) & (b >= -0.02) & (c >= -0.02)
    if not inside.any():
        continue
    vi = lv[loops]
    w = np.stack([a, b, c], -1)[inside]
    rows, cols = gy[inside].astype(np.int32), gx[inside].astype(np.int32)
    P[rows, cols] = w @ co[vi]
    N[rows, cols] = w @ no[vi]
    K[rows, cols] = region_of[tp[t]]
    PART[rows, cols] = atlas.part_of[tp[t]]
for _ in range(4):
    empty = K < 0
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        take = empty & (np.roll(np.roll(K, dy, 0), dx, 1) >= 0)
        for arr in (K, PART, P, N):
            arr[take] = np.roll(np.roll(arr, dy, 0), dx, 1)[take]
        empty = K < 0
N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-6)
print("[dt] ribbit atlas", f"{(K >= 0).mean():.0%} covered by", {k: int((K == i).sum()) for i, k in enumerate(REGIONS)})

# --- what colour it is -------------------------------------------------------------------------------------------
flat = P.reshape(-1, 3)


def field(fn):
    return fn(flat).reshape(S, S)


def noise(seed, scale):
    return field(lambda q: cook.noise_np(q, seed, scale))


def is_(*names):
    return np.isin(K, [REGIONS.index(n) for n in names if n in REGIONS])


def part(name):
    return PART == BAKED.index(name)


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


def inside(outline, centre, inset):
    """How far inside a lip's line (in plan, about its centre) each texel is, in metres (negative outside)."""
    rel = P[..., :2] - centre[:2]
    ang = np.arctan2(rel[..., 0], rel[..., 1])
    o = outline[:, :2] - centre[:2]
    oa, orad = np.arctan2(o[:, 0], o[:, 1]), np.linalg.norm(o, axis=1)
    order = np.argsort(oa)
    r = np.interp(ang, oa[order], orad[order], left=0.0, right=0.0)
    return r - inset - np.linalg.norm(rel, axis=-1)


up = N[..., 2]
skin = is_("ribbit", "ribbit_belly")
grey = np.array((0.4, 0.395, 0.38), np.float32)
base = np.broadcast_to(grey, (S, S, 3)).copy()
base *= (0.92 + 0.12 * noise(2901, 3.0))[..., None]
rough = np.full((S, S), 0.24, np.float32)
# Down the back: blotched grey-green, bruised in places; the warts darker, a pale rim round each.
topside = smooth01(-0.1, 0.6, up) * smooth01(0.3, 0.5, P[..., 2])
blotch = smooth01(0.05, 0.55, noise(2902, 5.5)) * topside
base = paint(base, (0.19, 0.205, 0.17), blotch * 0.75 * skin)
base = paint(base, (0.2, 0.15, 0.16), smooth01(0.3, 0.7, noise(2903, 2.5)) * topside * 0.3 * skin)
wart = field(warts_of)
base = paint(base, (0.13, 0.125, 0.11), wart * 0.6 * is_("ribbit"))
rough += 0.18 * wart * is_("ribbit")
# The underside: paler, faintly pink, grading into the flanks; the sac paler still and stretched shiny.
pale = np.maximum(smooth01(0.05, -0.55, up) * smooth01(0.42, 0.25, P[..., 2]), is_("ribbit_belly") * 0.8)
base = paint(base, (0.56, 0.5, 0.48), pale * skin)
sac = field(sac_of)
base = paint(base, (0.62, 0.55, 0.53), sac * skin * smooth01(0.0, -0.4, up))
rough -= 0.08 * sac
# Veins under the thin skin: faint over the belly and the inner limbs, plain on the sac.
vein = field(veins_of)
base = paint(base, (0.3, 0.12, 0.16), vein * skin * (0.25 * pale + 0.55 * sac))
# Darker round the eyes and along the lips, the lips' rims raw.
for e in EYES.values():
    r = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.12, 0.11, 0.1), smooth01(0.075, 0.048, r) * skin * 0.7)
lip = np.maximum(smooth01(0.03, 0.0, np.abs(inside(LIP, HC, 0.0))) * smooth01(0.585, 0.61, P[..., 2]) * smooth01(0.64, 0.62, P[..., 2]),
                 smooth01(0.025, 0.0, np.abs(inside(LOWLIP, JC, 0.0))) * part("jaw"))
base = paint(base, (0.3, 0.2, 0.2), lip * 0.6 * skin)
# The mouth: the roof inside the upper lip and the floor inside the lower, gums at their rims, dark wet red.
# (Only above the jaw's floor: below it, under the jaw, is the throat.)
roof = smooth01(0.0, 0.012, inside(LIP, HC, 0.012)) * smooth01(0.615, 0.6, P[..., 2]) * smooth01(0.588, 0.598, P[..., 2]) * (up < 0.2) * part("body")
floor = smooth01(0.0, 0.01, inside(LOWLIP, JC, 0.006)) * smooth01(0.57, 0.585, P[..., 2]) * (up > 0.2) * part("jaw")
mouth = np.maximum(np.maximum(roof, floor), is_("ribbit_gum") * 1.0)
base = paint(base, (0.17, 0.035, 0.04), mouth)
base = paint(base, (0.06, 0.012, 0.015), mouth * smooth01(0.0, 0.06, np.maximum(inside(LIP, HC, 0.03), inside(LOWLIP, JC, 0.03))))
rough = np.where(mouth > 0.5, 0.15, rough)
# The ears: bare and thin, pink-grey, pinker in their cups, red veins branching up them; darker at the rims.
ear = is_("ribbit_ear")
base[ear] = np.array((0.48, 0.36, 0.35), np.float32)
base = paint(base, (0.62, 0.33, 0.36), ear * smooth01(0.2, -0.6, N[..., 0] * np.sign(P[..., 0])) * 0.7)
base = paint(base, (0.3, 0.05, 0.08), ear * field(lambda q: veins_of(q, 18.0)) * 0.85)
base = paint(base, (0.2, 0.17, 0.16), ear * smooth01(0.3, 0.7, noise(2904, 9.0)) * 0.3)
rough[ear] = 0.3
# The eyes: milky, a grey-green ring of iris, a toad's flat bar of pupil clouded over, looking out and forward.
eye = is_("ribbit_eye")
base[eye] = np.array((0.56, 0.58, 0.53), np.float32)
for s, e in EYES.items():
    rel = P - e
    dist = np.linalg.norm(rel, axis=-1)
    d = rel / np.maximum(dist, 1e-6)[..., None]
    look = np.array([np.sign(e[0]) * 0.75, 0.55, 0.35], np.float32)
    look /= np.linalg.norm(look)
    right = np.cross(look, np.array([0, 0, 1], np.float32))
    right /= np.linalg.norm(right)
    upv = np.cross(right, look)
    u, v, c = d @ right, d @ upv, d @ look
    near = eye & (dist < 0.06)
    iris = near * smooth01(0.8, 0.86, c)
    base = paint(base, (0.32, 0.37, 0.3), iris * 0.6)
    bar = near * (c > 0) * smooth01(1.0, 0.7, (u / 0.42) ** 2 + (v / 0.13) ** 2)
    base = paint(base, (0.07, 0.08, 0.07), bar * 0.75)
    base = paint(base, (0.7, 0.72, 0.68), near * smooth01(0.4, 0.8, noise(2905, 60.0)) * 0.25)
rough[eye] = 0.04
# The teeth: yellowed, browner at the roots; the tongue a paler wet red, the club darker; the nails yellow-grey.
teeth = is_("ribbit_teeth")
base[teeth] = np.array((0.55, 0.48, 0.33), np.float32)
root = np.full((S, S), 1.0, np.float32)
for t in TEETH_AT:
    root = np.minimum(root, np.abs(P[..., 2] - t[2]) + 0.02 * smooth01(0.012, 0.03, np.linalg.norm(P[..., :2] - t[:2], axis=-1)))
base = paint(base, (0.2, 0.11, 0.05), teeth * smooth01(0.008, 0.002, root))
rough[teeth] = 0.35
tongue = is_("ribbit_tongue")
base[tongue] = np.array((0.44, 0.13, 0.15), np.float32)
base = paint(base, (0.3, 0.07, 0.09), tongue * smooth01(0.6, 0.66, P[..., 1]))
rough[tongue] = 0.12
nail = is_("ribbit_nail")
base[nail] = np.array((0.42, 0.37, 0.27), np.float32)
rough[nail] = 0.35
# The fingers and the toes paler, pinker at their tips; the soles and the palms calloused grey.
paws = part("paws") & ~nail
base = paint(base, (0.5, 0.42, 0.4), paws * 0.6)
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.9)

# Darkened by the bake's occlusion, the creases and the folds sooted a little.
ao = atlas.maps["AO"][..., 0]
gentle = eye | teeth
ao = np.where(gentle, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.4
base = base * (1 - k)[..., None] + np.array((0.03, 0.025, 0.025), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("ribbit", "the Ribbits, modelled in tools/blender/ribbit.py, their colour painted here"),
             rough=rough, lod=0.4)
