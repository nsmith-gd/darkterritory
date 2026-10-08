"""THE BRAKEMAN (GDD §21 corrupted humans, App. A.8; docs/design/creatures/brakeman.md §3; ARCHITECTURE §8 note 364):
tools/blender/brakeman.py's dead railwayman with his colour and his fine relief baked into one 2048 atlas (the Look
Review: organic, not tiled library textures on boxes; the Gannet's and the Ribbit's way).

tools/blender/brakeman.py stays his source: the rig, the fused skin and the parts over it, the clips. This recipe runs it,
unwraps every part into one atlas (the face, the hands and the brass given more of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the coat's wool weave, its folds
    and its moth-eaten rot; the skin's wrinkles, the cords of the neck and the knuckles' creases; the corrupted flesh's
    crusted lumps; the iron pitted, flaked and riveted all over; the chain's links eaten; the boots' cracked leather; the
    hair in strands;
  * paints the colour from where each texel is on him at rest (deterministic, no render: each texel's position, normal and
    material rasterized from the unwrap): the coat soot-black in a patchwork of rust-brown rot, darker and frayed at the
    hem; the trousers the same, worn through at the knee; the boots black, scuffed brown; the cap black wool, the peak
    glossy; the face grey, blotched brown and bruised purple, dark round the sunk eyes, the lips bloodless; the moustache
    and the hair dirty grey; the eyes yellowed and bloodshot round a small dark pupil; the corrupted flesh rust-red,
    crusted orange and cracked black where the iron grows out of it; the iron rust-red streaked orange, black in its pits,
    bare and dark at its edges; the chain darker; the brass tarnished brown and green; the claws black horn.
His lamp's glass keeps its own lit material (a low flame).

    tools/models/build.sh brakeman        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/brakeman.py)
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
from overbake import fine, smooth01  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("brakeman.py")
print("[dt] brakeman parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

EYES = {s: np.array(tuple(v), np.float32) for s, v in g["EYES"].items()}
HC = np.array(tuple(g["HC"]), np.float32)
HEM = float(g["HEM"])
KNEE = float(g["KNEE"])


def suffix(name):
    """A kit material's region: "wool.brakeman_coat" -> "brakeman_coat" (Blender's ".001" dropped)."""
    return name.split(".")[1]


# The high copy's dress: only its relief and occlusion are baked (the colour is painted below), so one plain grey each,
# subdivided over the cloth and the skin, kept edged on the iron, the brass and the claws.
SOFT = {"brakeman_coat": 3, "brakeman_collar": 3, "brakeman_trouser": 3, "brakeman_boot": 2, "brakeman_cap": 2, "brakeman_skin": 3,
        "brakeman_rust": 3, "brakeman_hair": 3, "brakeman_lip": 3, "brakeman_eye": 1, "brakeman_peak": 0, "brakeman_iron": 0,
        "brakeman_chain": 0, "brakeman_brass": 0, "brakeman_claw": 0}


def dress(m):
    k = suffix(m.name)
    if k not in SOFT:
        return None
    return make.flat("brakeman_high", (0.3, 0.3, 0.3), rough=0.5), SOFT[k]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def rivets(p, pitch=0.045, r=0.0055):
    """Rivet heads on a lattice through him (the iron is riveted everywhere it's plated): a dome where a texel is within r of
    a lattice point, every other row offset."""
    q = p / pitch
    q[:, 1] += 0.5 * (np.floor(q[:, 2]) % 2)
    d = np.linalg.norm((q - np.round(q)) * pitch, axis=1)
    return np.clip(1 - (d / r) ** 2, 0, 1) ** 0.5


def wool(p, n):
    # The weave's fine grain, the folds hanging from the shoulders and the belt, and holes eaten in it.
    d = 0.0004 * np.sin(p[:, 2] * 900 + 3 * cook.noise_np(p, 3641, 30.0)) * np.sin(p[:, 0] * 900 + p[:, 1] * 900)
    d += 0.0018 * (ridged(p * np.array([1.0, 1.0, 0.25], np.float32), 3642, 14.0) ** 2 - 0.3)
    d -= 0.004 * smooth01(0.72, 0.86, cook.noise_np(p, 3643, 9.0) * 0.5 + 0.5)
    return d + fine(p, 0.0002, 400, 3644)


def skin(p, n):
    # Wrinkled across the brow and down the cheeks, the cords and the veins standing.
    d = 0.0008 * (ridged(p * np.array([1.0, 1.0, 2.5], np.float32), 3651, 60.0) ** 3)
    d += 0.0006 * smooth01(0.9, 0.97, ridged(p, 3652, 25.0))
    return d + fine(p, 0.00015, 600, 3653)


def rust_flesh(p, n):
    # Crusted, lumped and cracked where the iron grows out of it.
    d = 0.003 * smooth01(0.45, 0.9, cook.noise_np(p, 3661, 30.0) * 0.5 + 0.5) - 0.0025 * smooth01(0.93, 0.99, ridged(p, 3662, 18.0))
    return d + fine(p, 0.0004, 300, 3663)


def iron(p, n):
    # Pitted and flaked, and riveted.
    d = 0.0035 * rivets(p) - 0.0018 * smooth01(0.55, 0.85, cook.noise_np(p, 3671, 40.0) * 0.5 + 0.5)
    d -= 0.001 * smooth01(0.9, 0.97, ridged(p, 3672, 22.0))
    return d + fine(p, 0.0003, 250, 3673)


def chain(p, n):
    return -0.0012 * smooth01(0.5, 0.85, cook.noise_np(p, 3681, 60.0) * 0.5 + 0.5) + fine(p, 0.0002, 300, 3682)


def leather(p, n):
    return 0.0012 * (ridged(p, 3691, 35.0) ** 4) + fine(p, 0.0002, 400, 3692)


def hair(p, n):
    # Lank strands, down the way it hangs.
    return 0.0012 * np.sin(p[:, 0] * 700 + p[:, 1] * 500 + 2 * cook.noise_np(p, 3695, 20.0)) + fine(p, 0.0002, 600, 3696)


def flat0(p, n):
    return 0 * p[:, 0]


SHAPE = {"wool.brakeman_coat": wool, "wool.brakeman_collar": wool, "wool.brakeman_trouser": wool, "wool.brakeman_cap": wool,
         "skin.brakeman_skin": skin, "flesh.brakeman_lip": skin, "flesh.brakeman_rust": rust_flesh, "rust_heavy.brakeman_iron": iron,
         "rust_heavy.brakeman_chain": chain, "leather.brakeman_boot": leather, "leather.brakeman_peak": flat0, "fleece.brakeman_hair": hair,
         "brass.brakeman_brass": flat0, "tar.brakeman_claw": flat0, "glass_dirty.brakeman_eye": flat0}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] brakeman highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE = 1


def kind_of(m):
    k = suffix(m.name)
    if k == "brakeman_glass":
        return overbake.Atlas.KEEP
    return FACE if k in ("brakeman_eye", "brakeman_lip", "brakeman_skin", "brakeman_hair", "brakeman_claw") else 0


atlas = overbake.Atlas("brakeman", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.8})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.008, 0.03), "head": (0.004, 0.015), "skirt": (0.006, 0.02), "fingers": (0.004, 0.012), "eyes": (0.003, 0.01),
                          "iron": (0.005, 0.015), "chains": (0.004, 0.012), "lamp": (0.004, 0.012)}, samples=16, height=2.0)

# --- where each texel is (rasterized from the unwrap at rest) ------------------------------------------------------
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
REGIONS = sorted({suffix(n) for n in atlas.slot_name if n})
region_of = np.array([REGIONS.index(suffix(n)) if n else -1 for n in atlas.slot_name], np.int32)
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
print("[dt] brakeman atlas", f"{(K >= 0).mean():.0%} covered by", {k: int((K == i).sum()) for i, k in enumerate(REGIONS)})

# --- what colour he is -------------------------------------------------------------------------------------------
flat = P.reshape(-1, 3)


def field(fn):
    return fn(flat).reshape(S, S)


def noise(seed, scale):
    return field(lambda q: cook.noise_np(q, seed, scale))


def is_(*names):
    return np.isin(K, [REGIONS.index(n) for n in names if n in REGIONS])


def paint(base, colour, k):
    k = np.clip(k, 0, 1)[..., None]
    return base * (1 - k) + np.array(colour, np.float32) * k


up = N[..., 2]
base = np.full((S, S, 3), 0.03, np.float32)
rough = np.full((S, S), 0.8, np.float32)

# The coat and the trousers: soot-black wool in a patchwork of rust-brown rot (the reference's mottle: squares of it, as if
# patched and patched again and rotted through), greasy at the cuffs and the front, darker and frayed toward the hem.
cloth = is_("brakeman_coat", "brakeman_collar", "brakeman_trouser", "brakeman_cap")
mottle = smooth01(0.35, 0.75, noise(3702, 5.0) * 0.5 + 0.5)
base[cloth] = np.array((0.032, 0.027, 0.024), np.float32)
rot = smooth01(0.58, 0.72, noise(3701, 7.0) * 0.5 + 0.5 + 0.25 * noise(3706, 25.0))
base = paint(base, (0.11, 0.055, 0.032), cloth * rot * (0.45 + 0.4 * mottle))
base = paint(base, (0.06, 0.05, 0.04), cloth * smooth01(0.62, 0.75, noise(3707, 5.0) * 0.5 + 0.5) * 0.5)
base = paint(base, (0.2, 0.1, 0.05), cloth * smooth01(0.82, 0.95, noise(3703, 18.0) * 0.5 + 0.5) * 0.6)
base *= (0.85 + 0.3 * noise(3704, 60.0))[..., None] * cloth[..., None] + (~cloth)[..., None]
hem = smooth01(HEM + 0.25, HEM - 0.05, P[..., 2]) * is_("brakeman_coat")
base = paint(base, (0.012, 0.01, 0.009), hem * 0.6)
rough[cloth] = 0.9
cap = is_("brakeman_cap")
base[cap] = np.array((0.022, 0.022, 0.026), np.float32) * (0.85 + 0.3 * noise(3705, 30.0))[cap][:, None]
trouser = is_("brakeman_trouser")
base = paint(base, (0.09, 0.075, 0.06), trouser * smooth01(0.06, 0.0, np.abs(P[..., 2] - KNEE - 0.02)) * 0.5)
# The boots: black leather, scuffed brown at the toes and the heels, the cracks pale with dried mud; the peak glossy.
boot = is_("brakeman_boot")
base[boot] = np.array((0.025, 0.02, 0.017), np.float32)
base = paint(base, (0.11, 0.07, 0.04), boot * smooth01(0.25, 0.75, noise(3711, 12.0) * 0.5 + 0.5) * smooth01(0.12, 0.0, P[..., 2]))
base = paint(base, (0.12, 0.1, 0.08), boot * smooth01(0.04, 0.0, P[..., 2]) * 0.6)
rough[boot] = 0.55
peak = is_("brakeman_peak")
base[peak] = (0.012, 0.011, 0.01)
rough[peak] = 0.25
# The face and the bare arm: dead grey, blotched brown and bruised purple, the veins dark; darker round the sunk eyes and in
# the hollows of the cheeks; the lips bloodless.
skin = is_("brakeman_skin")
base[skin] = np.array((0.33, 0.29, 0.23), np.float32) * (0.9 + 0.2 * noise(3721, 8.0))[skin][:, None]
base = paint(base, (0.15, 0.115, 0.085), skin * smooth01(0.2, 0.7, noise(3722, 14.0) * 0.5 + 0.5) * 0.6)
base = paint(base, (0.16, 0.11, 0.08), skin * smooth01(0.72, 0.92, noise(3723, 7.0) * 0.5 + 0.5) * 0.3)
# (The bare forearm darker than the face, grimed with soot and rust.)
base = paint(base, (0.1, 0.075, 0.06), skin * smooth01(HC[2] - 0.25, HC[2] - 0.4, P[..., 2]) * 0.55)
base = paint(base, (0.08, 0.06, 0.07), skin * smooth01(0.9, 0.97, field(lambda p: 1 - np.abs(cook.noise_np(p, 3724, 22.0)))) * 0.7)
for e in EYES.values():
    r = np.linalg.norm(P - e, axis=-1)
    base = paint(base, (0.05, 0.035, 0.035), smooth01(0.04, 0.014, r) * skin * 0.6)
for sx in (1, -1):
    cheek = np.linalg.norm((P - (HC + np.array((sx * 0.052, 0.08, -0.046), np.float32))) / np.array((0.03, 0.03, 0.035), np.float32), axis=-1)
    base = paint(base, (0.09, 0.075, 0.07), skin * smooth01(1.2, 0.3, cheek) * 0.35)
lip = is_("brakeman_lip")
base[lip] = (0.17, 0.12, 0.12)
rough[skin | lip] = 0.5
# The moustache and the hair: dirty grey-white, yellowed with smoke, dark at the roots.
hair = is_("brakeman_hair")
base[hair] = np.array((0.33, 0.31, 0.28), np.float32) * (0.85 + 0.3 * noise(3731, 90.0))[hair][:, None]
base = paint(base, (0.22, 0.17, 0.1), hair * 0.25)
rough[hair] = 0.7
# The eyes: yellowed, bloodshot to the rim, a small dark pupil, looking out under the brow.
eye = is_("brakeman_eye")
base[eye] = (0.26, 0.22, 0.13)
for s, e in EYES.items():
    rel = P - e
    dist = np.linalg.norm(rel, axis=-1)
    d = rel / np.maximum(dist, 1e-6)[..., None]
    look = np.array((0.0, 0.97, -0.25), np.float32)
    c = d @ look
    near = eye & (dist < 0.02)
    base = paint(base, (0.32, 0.06, 0.04), near * smooth01(0.4, -0.2, c) * 0.8)
    base = paint(base, (0.12, 0.08, 0.03), near * smooth01(0.78, 0.84, c))
    base = paint(base, (0.01, 0.008, 0.006), near * smooth01(0.92, 0.95, c))
rough[eye] = 0.05
# The corrupted flesh: rust-red, crusted orange in its lumps, cracked black where it splits round the iron.
flesh = is_("brakeman_rust")
base[flesh] = np.array((0.2, 0.06, 0.035), np.float32) * (0.85 + 0.3 * noise(3741, 10.0))[flesh][:, None]
base = paint(base, (0.42, 0.17, 0.06), flesh * smooth01(0.55, 0.85, noise(3742, 30.0) * 0.5 + 0.5) * 0.7)
base = paint(base, (0.02, 0.012, 0.01), flesh * smooth01(0.93, 0.99, field(lambda p: 1 - np.abs(cook.noise_np(p, 3662, 18.0)))))
rough[flesh] = 0.45
# The iron: rust-red streaked orange running down, black in its pits, flaked to dark metal in places; the rivets' heads
# a shade lighter; the chain darker, the same.
metal = is_("brakeman_iron", "brakeman_chain")
streak = noise(3751, 4.0) * 0.6 + 0.4 * field(lambda p: cook.noise_np(p * np.array([3.0, 3.0, 0.4], np.float32), 3752, 6.0))
base[metal] = np.array((0.21, 0.085, 0.045), np.float32) * (0.8 + 0.4 * noise(3753, 20.0))[metal][:, None]
base = paint(base, (0.46, 0.19, 0.06), metal * smooth01(0.2, 0.7, streak) * 0.55)
base = paint(base, (0.03, 0.025, 0.022), metal * smooth01(0.6, 0.85, noise(3671, 40.0) * 0.5 + 0.5) * 0.85)
base = paint(base, (0.07, 0.065, 0.06), metal * smooth01(0.7, 0.9, noise(3754, 9.0) * 0.5 + 0.5) * 0.5)
base = paint(base, (0.32, 0.14, 0.07), is_("brakeman_iron") * field(rivets) * 0.6)
base = paint(base, (0.05, 0.025, 0.018), is_("brakeman_chain") * 0.45)
rough[metal] = 0.75
# The brass: the lamp, the badge, the buttons, the buckle; tarnished brown, green in the creases, rubbed bright on the high spots.
brass = is_("brakeman_brass")
base[brass] = np.array((0.2, 0.13, 0.05), np.float32) * (0.85 + 0.3 * noise(3761, 25.0))[brass][:, None]
base = paint(base, (0.07, 0.11, 0.07), brass * smooth01(0.55, 0.85, noise(3762, 30.0) * 0.5 + 0.5) * 0.6)
rough[brass] = 0.4
# The claws: black horn, a brown sheen, worn pale at the tips.
claw = is_("brakeman_claw")
base[claw] = (0.02, 0.016, 0.013)
base = paint(base, (0.2, 0.17, 0.13), claw * smooth01(0.6, 0.9, noise(3771, 80.0) * 0.5 + 0.5) * 0.2)
rough[claw] = 0.3
# Soot settled on everything facing up; grime in the creases (the bake's occlusion).
base = paint(base, (0.02, 0.018, 0.016), smooth01(0.4, 1.0, up) * 0.25 * ~(eye | brass))
base = np.clip(base, 0, 1)
rough = np.clip(rough, 0.03, 0.95)
ao = atlas.maps["AO"][..., 0]
gentle = eye | brass
ao = np.where(gentle, ao ** 0.35, ao)
base = base * (0.3 + 0.7 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.012, 0.01, 0.009), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("brakeman", "the Brakeman, modelled in tools/blender/brakeman.py, his colour painted here"),
             rough=rough, lod=0.4)
