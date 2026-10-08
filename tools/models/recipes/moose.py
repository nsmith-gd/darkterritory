"""THE MOOSE (GDD §21 outside; docs/design/creatures/moose.md §3; ARCHITECTURE §8 note 339): tools/blender/moose.py's
corrupted bull moose with its colour baked into one atlas (the Look Review: organic, not tiled library textures on boxes;
the Gannet's way, tools/models/recipes/gannet.py).

tools/blender/moose.py stays its source: the rig, the fused skin and the rack, the sacs, the clips. This recipe runs it,
unwraps every part into one 2048 atlas (the head and the rack given more of it), and paints the atlas from where each
texel is on the moose at rest, by what it's part of (deterministic, no render: each texel's position, normal and material
are rasterized from the unwrap):
  * the hide a winter-tick "ghost moose's": mottled grey, rubbed bald in pale patches, the last of its dark hair in
    ragged islands, scabbed; veined faintly; crusted in clusters with small pale engorged ticks too many to model (each a
    pale bead with its shadow under it), thickest on the hump, the neck, the shoulders and the rump; darker down the
    legs to black at the hooves, and on the muzzle, the nostrils black;
  * the modelled sacs pale, stretched and glossy, veined dark, the older ones purpled;
  * the rack slag: grey-black, ashy crust in its hollows, rust-brown stains, glassy specks, the tines' tips worn paler;
    velvet peeling off it in bloody patches, and blood run down the palms;
  * the velvet strips wet, dark red, clotted; the hooves black, worn grey at the rims;
  * what's caught in the rack its own colours: the wire rusted, the lamp black and rusted, the cap navy wool, the planks
    weathered grey with pale splintered ends (the insulator's and the lamp's glass, and the eyes, keep their own).

    python tools/models/recipes/moose.py        (Blender as a Python module: pip install "bpy<5")
    tools/models/build.sh moose                 (or with Blender)
"""
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
import bpy  # noqa: E402
import numpy as np  # noqa: E402

import cook  # noqa: E402
import make  # noqa: E402
import overbake  # noqa: E402
from overbake import smooth01  # noqa: E402

S = 2048
kit, g, arm, parts = overbake.hold("moose.py")
print("[dt] moose parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()

# (In the scene's own order: the join keeps the faces in it, and the atlas lines its faces' material names up by it.)
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
FACE, RACK, SAC = 1, 2, 3
# The eyes' wet glass, the insulator's and the lamp's keep their own materials (lit glass).
KEPT = ("glass_dirty.moose_eye", "glass_dirty.moose_insulator", "glass_dirty.moose_lamp_glass")


def kind_of(m):
    if m.name.startswith(KEPT):
        return overbake.Atlas.KEEP
    tail = m.name.split(".")[-1]
    if tail.startswith(("moose_muzzle", "moose_mouth", "moose_ear")):
        return FACE
    if tail.startswith("moose_sac"):
        return SAC
    return RACK if tail.startswith("moose_rack") else 0


atlas = overbake.Atlas("moose", parts, BAKED, kind_of, size=S)
# (The sacs are many and small: they'd take a fifth of the atlas at the hide's density; they're beads, so less of it.)
atlas.unwrap(boosts={FACE: 1.8, RACK: 1.25, SAC: 0.5})

# --- where each texel is ----------------------------------------------------------------------------------------
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

KINDS = sorted(set(atlas.slot_name))
kind_index = {k: i for i, k in enumerate(KINDS)}
face_kind = np.array([kind_index[n] for n in atlas.slot_name], np.int32)

P = np.zeros((S, S, 3), np.float32)
N = np.zeros((S, S, 3), np.float32)
K = np.full((S, S), -1, np.int32)
for t in range(nt):
    loops = tl[t]
    if atlas.keep[tp[t]]:
        continue
    q = uv[loops] * S
    x, y = q[:, 0] - 0.5, (1 - uv[loops][:, 1]) * S - 0.5
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
    K[rows, cols] = face_kind[tp[t]]
# Grown out a few texels past every island's edge, so filtering never reads the empty atlas between them.
for _ in range(4):
    empty = K < 0
    for dy, dx in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        src = np.roll(np.roll(K, dy, 0), dx, 1)
        take = empty & (src >= 0)
        K[take] = src[take]
        P[take] = np.roll(np.roll(P, dy, 0), dx, 1)[take]
        N[take] = np.roll(np.roll(N, dy, 0), dx, 1)[take]
        empty = K < 0
N /= np.maximum(np.linalg.norm(N, axis=-1, keepdims=True), 1e-6)
print("[dt] moose atlas", f"{(K >= 0).mean():.0%} covered by", {k: int((K == i).sum()) for i, k in enumerate(KINDS)})

# --- what colour it is (painted on the covered texels only, as flat arrays) ----------------------------------------
used = K >= 0
p = P[used]
n = N[used]
k = K[used]
X, Y, Z = p[:, 0], p[:, 1], p[:, 2]
AX = np.abs(X)
up = n[:, 2]


def noise(seed, scale, at=None):
    return cook.noise_np(p if at is None else at, seed, scale)


def fbm(seed, scale, octaves=3):
    acc, amp, tot = np.zeros(len(p), np.float32), 1.0, 0.0
    for o in range(octaves):
        acc += amp * noise(seed + o * 17, scale * 2 ** o)
        tot += amp
        amp *= 0.5
    return acc / tot


def is_(*names):
    m = np.zeros(len(k), bool)
    for i, name in enumerate(KINDS):
        if name.split(".")[-1].startswith(names):
            m |= k == i
    return m


def paint(base, colour, amount):
    a = np.clip(amount, 0, 1)[:, None]
    return base * (1 - a) + np.asarray(colour, np.float32) * a


def worley(scale, seed, chunk=400_000):
    """The distance to the nearest of a jittered lattice of points (in cells) and that point's hash (0..1): beads."""
    q = p.astype(np.float64) * scale
    d1 = np.full(len(q), 9.0)
    hid = np.zeros(len(q))
    for s0 in range(0, len(q), chunk):
        qq = q[s0:s0 + chunk]
        cell = np.floor(qq).astype(np.int64)
        best, bh = np.full(len(qq), 9.0), np.zeros(len(qq))
        for dx in (-1, 0, 1):
            for dy in (-1, 0, 1):
                for dz in (-1, 0, 1):
                    cc = cell + np.array([dx, dy, dz])
                    hh = (cc[:, 0] * 73856093) ^ (cc[:, 1] * 19349663) ^ (cc[:, 2] * 83492791) ^ (seed * 2654435761)
                    jit = np.stack([((hh >> s) & 0xFF) / 255.0 for s in (3, 11, 19)], -1)
                    d = np.linalg.norm(qq - (cc + 0.15 + 0.7 * jit), axis=-1)
                    closer = d < best
                    best = np.where(closer, d, best)
                    bh = np.where(closer, ((hh >> 7) & 0xFFFF) / 65535.0, bh)
        d1[s0:s0 + chunk], hid[s0:s0 + chunk] = best, bh
    return d1.astype(np.float32), hid.astype(np.float32)


# The head's frame (the script's): where on the face a texel is.
POLL, ALONG, OVER = (np.array(g[k_], np.float32) for k_ in ("POLL", "ALONG", "OVER"))
hu = (p - POLL) @ ALONG
hv = (p - POLL) @ OVER
on_head = (Y > 1.25) & (hu > -0.15)

base = np.zeros((len(p), 3), np.float32)
rough = np.full(len(p), 0.8, np.float32)

# --- the hide: a ghost moose, grey and patchy, rubbed bald, scabbed, veined, crusted with ticks ---------------------------
skin = is_("moose_hide", "moose_legs", "moose_muzzle", "moose_ear")
grey = np.array((0.125, 0.12, 0.115), np.float32)
pale = np.array((0.27, 0.255, 0.24), np.float32)
bald = np.array((0.26, 0.19, 0.17), np.float32)
hair = np.array((0.032, 0.028, 0.025), np.float32)
patch = fbm(3901, 1.8, 4)
mottle = fbm(3902, 6.0, 3)
grain = noise(3903, 70.0)
base[:] = grey
# Rubbed bald in pale blotches (the ticks' work), the last of its dark winter hair in ragged islands between them.
base = paint(base, pale, smooth01(0.0, 0.25, patch))
base = paint(base, hair, smooth01(-0.12, -0.32, patch) * (0.7 + 0.3 * smooth01(0.4, 1.0, up)))
base = paint(base, bald, smooth01(0.3, 0.5, fbm(3904, 2.6, 2)) * 0.7)
base *= (0.8 + 0.3 * mottle + 0.06 * grain)[:, None]
# The ribs' shadows behind the shoulder, the flank's fold.
ribs = np.clip(np.sin(Y * 30 + 1.1), -1, 0) * smooth01(-0.38, -0.15, Y) * smooth01(0.38, 0.15, Y) * smooth01(0.2, 0.32, AX) * smooth01(2.1, 1.9, Z)
base *= (1 + 0.22 * ribs)[:, None]
# Scabs and raw sores: rust-brown and dark red, ragged.
scab = smooth01(0.62, 0.72, noise(3905, 11.0) * 0.5 + 0.5) * smooth01(0.1, 0.4, fbm(3906, 2.2, 2))
base = paint(base, (0.09, 0.03, 0.02), scab * 0.85)
# Veins under the bald skin.
vein = smooth01(0.93, 0.985, 1 - np.abs(noise(3907, 5.5))) * 0.7 + smooth01(0.95, 0.99, 1 - np.abs(noise(3908, 13.0))) * 0.4
base = paint(base, (0.07, 0.075, 0.1), vein * 0.45 * (0.4 + smooth01(0.0, 0.5, patch)))
# Darker under the belly and between the legs; down the legs to black at the hooves.
base *= (1 - 0.35 * smooth01(-0.3, -0.85, up) * smooth01(1.7, 1.3, Z))[:, None]
legs = smooth01(1.15, 0.55, Z) * (~on_head)
base = paint(base, (0.05, 0.045, 0.04), legs * 0.85)
base = paint(base, (0.012, 0.011, 0.01), smooth01(0.4, 0.16, Z) * 0.9)
# The muzzle dark grey-brown, darkest round the lips; the nostrils black slashes; the eye ringed dark.
muzzle = on_head * smooth01(0.48, 0.71, hu)
base = paint(base, (0.065, 0.055, 0.05), muzzle * 0.85)
lips = on_head * smooth01(0.69, 0.86, hu) * smooth01(-0.02, -0.14, hv)
base = paint(base, (0.025, 0.02, 0.02), lips * 0.8)
for sx in (-1, 1):
    nos = ((hu - 0.98) / 0.06) ** 2 + ((hv + 0.02 - 0.25 * (hu - 0.98)) / 0.026) ** 2 + ((X - sx * 0.09) / 0.07) ** 2
    base = paint(base, (0.008, 0.006, 0.006), smooth01(1.3, 0.7, nos) * on_head)
    eye = ((hu - 0.205) / 0.07) ** 2 + ((hv - 0.06) / 0.05) ** 2 + ((X - sx * 0.2) / 0.07) ** 2
    base = paint(base, (0.03, 0.025, 0.022), smooth01(1.4, 0.6, eye) * on_head)
mouth = is_("moose_mouth")
base[mouth] = (0.05, 0.012, 0.01)
rough[mouth] = 0.35
# The ears: thinner hide, darker, the cup's inside bare and pinkish, the torn edges raw.
ear = is_("moose_ear")
base = paint(base, (0.08, 0.07, 0.065), ear * 0.6)
inside = ear & ((n @ np.array((0, 1, 0), np.float32)) > 0.3)
base = paint(base, (0.22, 0.14, 0.13), inside * 0.6)
# Ticks too many to model: clusters of small pale engorged beads, each lit from above with its shadow under it, thickest on
# the hump, the neck, the shoulders and the rump, few on the legs and the face.
where = smooth01(0.05, 0.45, fbm(3910, 2.4, 2)) * (0.35 + 0.65 * smooth01(1.5, 2.2, Z)) * (1 - smooth01(0.9, 0.6, Z)) * (1 - 0.7 * on_head)
where *= skin & ~ear
d1, hid = worley(22.0, 3911)
r = 0.2 + 0.2 * hid
bead = smooth01(r + 0.05, r - 0.05, d1) * (hid < where)
shade = np.clip(0.55 + 0.6 * up, 0.2, 1.0)
base = paint(base, (0.01, 0.008, 0.007), smooth01(r + 0.16, r + 0.02, d1) * (hid < where) * 0.8)
# (Each bead lit from its top: brighter toward the up side of the cell's middle, the lower rim in shadow.)
tick = np.array((0.42, 0.37, 0.3), np.float32) * (0.75 + 0.3 * hid)[:, None] * shade[:, None] * (1.1 - 0.9 * np.clip(d1 / np.maximum(r, 1e-3), 0, 1))[:, None]
base = base * (1 - bead[:, None]) + tick * bead[:, None]
rough = np.where(bead > 0.5, 0.25, rough)
rough[skin] = np.minimum(rough[skin], 0.75)

# --- the sacs: pale, stretched and glossy, veined; the old ones purpled --------------------------------------------------
sac = is_("moose_sac")
old = is_("moose_sac_old")
base[sac] = np.array((0.36, 0.31, 0.25), np.float32) * (0.85 + 0.2 * noise(3920, 30.0)[sac])[:, None]
base[old] = np.array((0.17, 0.12, 0.13), np.float32) * (0.85 + 0.2 * noise(3921, 30.0)[old])[:, None]
# (Shadowed where they press into the hide, under them.)
base *= (1 - 0.6 * (sac | old) * smooth01(0.1, -0.7, up))[:, None]
sv = smooth01(0.88, 0.97, 1 - np.abs(noise(3922, 40.0)))
base = paint(base, (0.14, 0.03, 0.04), (sac | old) * sv * 0.7)
rough[sac | old] = 0.18

# --- the rack: slag, ashy in its hollows, rust-stained, glassy-specked, its tines' tips worn; velvet and blood on it -------
rack = is_("moose_rack")
slag = np.array((0.05, 0.048, 0.046), np.float32)
ash = np.array((0.2, 0.192, 0.18), np.float32)
base[rack] = slag * (0.75 + 0.5 * fbm(3930, 9.0, 2)[rack])[:, None]
base = paint(base, ash, rack * smooth01(0.05, 0.45, fbm(3931, 5.0, 3)) * 0.75)
base = paint(base, (0.09, 0.045, 0.025), rack * smooth01(0.4, 0.65, fbm(3932, 3.0, 2)) * 0.5)
specks = smooth01(0.82, 0.9, noise(3933, 90.0) * 0.5 + 0.5) * rack
base = paint(base, (0.008, 0.008, 0.009), specks)
rough[rack] = 0.6
rough = np.where(specks > 0.5, 0.15, rough)
tips = rack * smooth01(1.42, 1.58, AX) + rack * smooth01(3.1, 3.3, Z)
base = paint(base, (0.2, 0.19, 0.175), np.clip(tips, 0, 1) * 0.6)
# Velvet still clinging in patches, raw and bloody (more under the palms and down their front edges), and blood run down.
peel = rack * smooth01(0.38, 0.55, fbm(3934, 3.5, 3)) * (0.4 + 0.6 * smooth01(0.2, -0.6, up))
base = paint(base, (0.1, 0.016, 0.012), peel)
base = paint(base, (0.3, 0.12, 0.1), peel * smooth01(0.85, 0.95, 1 - np.abs(noise(3935, 25.0))) * 0.7)
rough = np.where(peel > 0.5, 0.3, rough)
drip_at = p * np.array((14.0, 14.0, 1.6), np.float32)
drips = rack * smooth01(0.62, 0.82, cook.noise_np(drip_at, 3936, 1.0) * 0.5 + 0.5) * smooth01(0.2, 0.5, fbm(3937, 2.0, 2))
base = paint(base, (0.05, 0.007, 0.005), drips * 0.8)

# --- the velvet strips: wet, dark red, clotted; the torn edges pink ------------------------------------------------------
vel = is_("moose_velvet")
base[vel] = np.array((0.13, 0.016, 0.012), np.float32) * (0.8 + 0.3 * noise(3940, 20.0)[vel])[:, None]
base = paint(base, (0.035, 0.004, 0.003), vel * smooth01(0.3, 0.6, noise(3941, 12.0) * 0.5 + 0.5))
base = paint(base, (0.32, 0.13, 0.11), vel * smooth01(0.88, 0.96, 1 - np.abs(noise(3942, 30.0))) * 0.6)
rough[vel] = 0.22

# --- the hooves: black, horn-grained, worn grey at the rims --------------------------------------------------------------
hoof = is_("moose_hoof")
base[hoof] = np.array((0.014, 0.012, 0.011), np.float32) * (0.8 + 0.4 * noise(3950, 60.0, p * np.array((1, 1, 6), np.float32))[hoof])[:, None]
base = paint(base, (0.07, 0.065, 0.06), hoof * smooth01(0.03, 0.0, Z) * 0.8)
rough[hoof] = 0.4

# --- what's caught in the rack ---------------------------------------------------------------------------------------------
wire = is_("moose_wire")
base[wire] = np.array((0.14, 0.055, 0.022), np.float32) * (0.7 + 0.5 * noise(3960, 80.0)[wire])[:, None]
rough[wire] = 0.7
lamp = is_("moose_lamp")
base[lamp] = (0.018, 0.017, 0.016)
base = paint(base, (0.12, 0.05, 0.02), lamp * smooth01(0.3, 0.6, noise(3961, 25.0) * 0.5 + 0.5))
rough[lamp] = 0.5
cap = is_("moose_cap")
base[cap] = np.array((0.025, 0.03, 0.045), np.float32) * (0.85 + 0.3 * noise(3962, 50.0)[cap])[:, None]
base = paint(base, (0.06, 0.012, 0.01), cap * smooth01(0.5, 0.75, noise(3963, 9.0) * 0.5 + 0.5) * 0.7)
rough[cap] = 0.95
wood = is_("moose_wood")
grain_w = noise(3964, 1.0, p * np.array((60, 60, 8), np.float32))
base[wood] = np.array((0.2, 0.18, 0.15), np.float32) * (0.75 + 0.3 * grain_w[wood])[:, None]
base = paint(base, (0.42, 0.36, 0.27), wood * smooth01(0.6, 0.85, noise(3965, 30.0) * 0.5 + 0.5) * 0.6)
rough[wood] = 0.9

full = np.zeros((S, S, 3), np.float32)
full[used] = np.clip(base, 0, 1)
rgh = np.full((S, S), 0.8, np.float32)
rgh[used] = np.clip(rough, 0, 1)

# No normals baked (the shapes are the mesh's own): a flat normal map.
atlas.maps = {"NORMAL": np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), (S, S, 1))}
atlas.finish(full, kit, arm, made=make.provenance("moose", "the Moose, modelled in tools/blender/moose.py, its colour painted here"),
             rough=rgh, lod=0.4)
