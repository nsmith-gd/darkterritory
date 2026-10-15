"""THE MOUSE MOOSE (GDD §21 outside; note 790, docs/design/creatures/mouse-moose.md; note 796): tools/blender/mouse_moose.py's
dog-sized mutant rat with its moose's antlers, its colour and its fine relief baked into one 1024 atlas (the Pickers' way,
tools/models/recipes/picker.py; the eyes kept lit, the Gannet's and the Brakeman's).

tools/blender/mouse_moose.py stays its source: the rig, the fused skin and the jaw and the parts over them, the clips. This
recipe runs it, unwraps every part into one atlas (the head's small parts, the hands, the ears and the antlers given more
of it), and:
  * bakes a high copy's relief into the normal map and its creases into the occlusion: the pelt short, matted and gone in
    mangy patches, scabbed; the tail's scales in rings; the pink hands and feet creased; the ears' thin skin veined; the
    antlers' bone pitted and grooved, the velvet's torn edges;
  * paints the colour from where each texel is on the creature at rest (deterministic, no render): the pelt a sewer rat's,
    dark grey-brown, darker down the spine, the belly a dirty pale grey; mangy, bare grey-pink skin in patches with scabs
    in them, and the growths bald and pinker; the tail pink-grey, ringed, grimed dark toward its tip; the hands, the feet
    and the nose pink, dirty at the edges; the ears thin and pink-grey, veined; the incisors orange (a rat's), browner at
    the roots; the claws dark; the antlers' velvet a mousy brown-grey torn back to raw red where it's ripped and to pale
    bone at the tines' tips, bloodied under the shreds;
  * leaves the eyes their own lit glass, so they throw a lamp back red (the brief: "a little pair of glinting eyes").
Dry and dusty (rough 0.75-0.9); the mouth, the nose and the raw velvet wet.

    tools/models/build.sh mouse_moose        (or with no Blender: pip install "bpy<5", then python tools/models/recipes/mouse_moose.py)
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
from texels import Texels  # noqa: E402

S = 1024  # the roster's small-creature atlas (the Pickers', the Mourners'): every renderer loads every atlas (note 367)
kit, g, arm, parts = overbake.hold("mouse_moose.py")
print("[dt] mouse_moose parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})
make.USED.clear()
make._mats.clear()
make.LOW.clear()

TEETH_AT = np.array([tuple(p) for p in g["TEETH_AT"]], np.float32)
CLAWS = np.array([tuple(p) for p in g["CLAWS"]], np.float32)
TIPS = np.array([tuple(p) for p in g["ANTLER_TIPS"]], np.float32)
VELVET_AT = np.array([tuple(p) for p in g["VELVET_AT"]], np.float32)
TAIL_PTS = np.array(g["TAIL_PTS"], np.float32)


def suffix(name):
    """A kit material's region: "skin.mm_fur" -> "mm_fur" (Blender's ".001" dropped)."""
    return name.split(".")[1]


# The high copy's dress: only its relief and occlusion are baked (the colour is painted below), so one plain grey each,
# subdivided over the skin, kept edged on the teeth and the claws; the eyes keep their own material and aren't in it.
DRESS = {"mm_fur": 3, "mm_pink": 3, "mm_ear": 2, "mm_tail": 2, "mm_mouth": 2, "mm_antler": 2, "mm_velvet": 1, "mm_whisker": 0,
         "mm_tooth": 0, "mm_claw": 0}


def dress(m):
    k = suffix(m.name)
    if k not in DRESS:
        return None
    return make.flat("mouse_moose_high", (0.3, 0.3, 0.3), rough=0.8), DRESS[k]


def ridged(p, seed, scale):
    return 1 - np.abs(cook.noise_np(p, seed, scale))


def mange_of(p):
    """Bare patches where the fur's gone: on the flanks, the haunches and the back of the neck most, the head least."""
    where = 0.6 + 0.4 * smooth01(0.38, 0.3, p[:, 1])
    return smooth01(0.36, 0.68, cook.noise_np(p, 7901, 6.0) * where)


def scabs_of(p):
    """Scabs in the bare patches: raised, crusted spots."""
    return mange_of(p) * smooth01(0.7, 0.9, cook.noise_np(p, 7902, 26.0) * 0.5 + 0.5)


def growths_of(p):
    """The growths under the pelt (tools/blender/mouse_moose.py's three lumps): bald and pinker at their crowns."""
    out = np.zeros(len(p), np.float32)
    for c, r in (((0.1, -0.06, 0.36), 0.04), ((-0.085, 0.12, 0.38), 0.03), ((0.06, 0.28, 0.39), 0.025)):
        out = np.maximum(out, smooth01(r * 1.3, r * 0.6, np.linalg.norm(p - np.array(c, np.float32), axis=1)))
    return out


def tail_along(p):
    """How far down the tail a point is, 0 at the rump to 1 at the tip (by the nearest of its points)."""
    d = np.stack([np.linalg.norm(p - t, axis=1) for t in TAIL_PTS], 1)
    i = np.argmin(d, 1).astype(np.float32)
    return i / (len(TAIL_PTS) - 1)


def fur_shape(p, n):
    # A short matted pelt, the hair lying back along the body in clumps, gone in patches; scabs raised in them; the
    # growths' skin stretched smooth.
    bare = mange_of(p)
    clumps = 0.0007 * np.sin(p[:, 1] * 260 + 4 * cook.noise_np(p, 7911, 18.0)) * (1 - bare)
    hair = 0.0005 * cook.noise_np(p, 7912, 220.0) * (1 - bare)
    crust = 0.0012 * scabs_of(p) * (0.6 + 0.4 * ridged(p, 7913, 90.0))
    return (clumps + hair) * (1 - growths_of(p)) + crust - 0.0004 * bare + fine(p, 0.00015, 600, 7914)


def pink_shape(p, n):
    # The hands' and the feet's soles creased; the knuckles ringed.
    return -0.0005 * ridged(p, 7921, 70.0) ** 6 + 0.0003 * np.sin(np.linalg.norm(p - p.mean(0), axis=1) * 1100) + fine(p, 0.00012, 800, 7922)


def tail_shape(p, n):
    # The scales in rings round the rope, each ring a row of little plates, the hairs between them gone.
    along = np.linalg.norm(p - TAIL_PTS[0], axis=1)
    rings = np.abs(np.sin(along * 420))
    plates = np.abs(np.sin(np.arctan2(p[:, 0] - p[:, 0].mean(), p[:, 2] - p[:, 2].mean()) * 9 + along * 210))
    return -0.0006 * smooth01(0.85, 0.99, rings) - 0.0002 * smooth01(0.85, 0.99, plates) + fine(p, 0.0001, 900, 7931)


def ear_shape(p, n):
    return 0.0004 * smooth01(0.9, 0.97, ridged(p, 7941, 60.0)) + fine(p, 0.0001, 900, 7942)


def antler_shape(p, n):
    # Bone grooved along its length and pitted; the burr knobbed.
    grooves = 0.0006 * smooth01(0.8, 0.97, ridged(p, 7951, 70.0))
    pits = -0.0007 * smooth01(0.6, 0.85, cook.noise_np(p, 7952, 120.0) * 0.5 + 0.5)
    return grooves + pits + fine(p, 0.00015, 700, 7953)


def flat(p, n):
    return 0 * p[:, 0]


SHAPE = {"skin.mm_fur": fur_shape, "skin.mm_pink": pink_shape, "skin.mm_ear": ear_shape, "skin.mm_tail": tail_shape,
         "skin.mm_mouth": lambda p, n: fine(p, 0.0002, 600, 7961), "skin.mm_antler": antler_shape,
         "skin.mm_velvet": lambda p, n: fine(p, 0.0002, 500, 7962), "skin.mm_whisker": flat, "skin.mm_tooth": flat,
         "skin.mm_claw": flat}
BAKED = [o.name for o in bpy.context.scene.collection.objects if o.name in parts]
highs = {name: overbake.high_of(parts[name], dress, SHAPE) for name in BAKED}
print("[dt] mouse_moose highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

FACE, ANTLERS = 1, 2


def kind_of(m):
    k = suffix(m.name)
    if k == "mm_eye":
        return overbake.Atlas.KEEP
    if k in ("mm_antler", "mm_velvet"):
        return ANTLERS
    return FACE if k in ("mm_pink", "mm_ear", "mm_mouth", "mm_tooth", "mm_claw") else 0


atlas = overbake.Atlas("mouse_moose", parts, BAKED, kind_of, size=S)
atlas.unwrap(boosts={FACE: 1.6, ANTLERS: 1.8})
groups = {name: (atlas.part_of == pi, highs[name]) for pi, name in enumerate(BAKED)}
atlas.bake(groups, cages={"body": (0.005, 0.02), "jaw": (0.004, 0.012), "fine": (0.003, 0.01)}, samples=16, height=0.6)

tx = Texels(atlas, S, suffix)
P, N, K = tx.P, tx.N, tx.K
print("[dt] mouse_moose atlas", tx.coverage())
is_, noise, field, paint = tx.is_, tx.noise, tx.field, tx.paint

up = N[..., 2]
fur = is_("mm_fur")
# A sewer rat's pelt: dark grey-brown, grizzled, darker down the spine and the back of the neck, the belly and the throat a
# dirty pale grey; matted into darker clumps.
base = np.broadcast_to(np.array((0.13, 0.115, 0.095), np.float32), (S, S, 3)).copy()
base *= (0.85 + 0.25 * noise(7971, 5.0))[..., None]
base = paint(base, (0.22, 0.2, 0.17), smooth01(0.2, 0.7, noise(7972, 30.0)) * 0.35 * fur)             # grizzled
base = paint(base, (0.06, 0.052, 0.045), smooth01(0.3, 0.85, noise(7973, 11.0)) * 0.4 * fur)           # matted clumps
base = paint(base, (0.055, 0.048, 0.04), smooth01(0.3, 0.9, up) * smooth01(0.07, 0.0, np.abs(P[..., 0])) * 0.55 * fur)
belly = smooth01(-0.15, -0.65, up) * smooth01(0.4, 0.3, P[..., 2])
base = paint(base, (0.3, 0.28, 0.25), belly * 0.6 * fur)
# The mange: bare grey-pink skin in patches, its edges ragged with stubble; scabs dark red-brown in it; the growths bald and
# pinker at their crowns, veined.
bare = field(mange_of)
base = paint(base, (0.36, 0.28, 0.26), bare * 0.75 * fur)
base = paint(base, (0.2, 0.17, 0.15), smooth01(0.0, 0.3, bare) * smooth01(0.6, 0.3, bare) * 0.5 * fur)
scab = field(scabs_of)
base = paint(base, (0.17, 0.06, 0.04), scab * 0.85 * fur)
lump = field(growths_of)
base = paint(base, (0.42, 0.29, 0.27), lump * 0.85 * fur)
vein = field(lambda q: smooth01(0.9, 0.97, ridged(q, 7974, 50.0)))
base = paint(base, (0.3, 0.12, 0.12), vein * lump * 0.6 * fur)
# Grimed from the ground: the legs, the belly's underside and the haunches darker toward the feet, wet-dark.
base = paint(base, (0.05, 0.045, 0.04), smooth01(0.16, 0.04, P[..., 2]) * 0.6 * fur)
# The eye rings dark, the face's fur shorter and browner; the muzzle greyer.
for c in (np.array((0.046, 0.42, 0.36), np.float32), np.array((-0.046, 0.42, 0.36), np.float32)):
    d = np.linalg.norm(P - c, axis=-1)
    base = paint(base, (0.03, 0.025, 0.022), smooth01(0.022, 0.012, d) * fur * 0.85)
base = paint(base, (0.2, 0.18, 0.16), smooth01(0.44, 0.52, P[..., 1]) * smooth01(0.36, 0.3, P[..., 2]) * 0.5 * fur)

# The pink: the hands, the soles, the nose; the colour of a rat's, a grey-pink, darker in the creases and grimed at the edges.
pink = is_("mm_pink")
base = paint(base, (0.5, 0.33, 0.31), pink * (0.9 + 0.15 * noise(7975, 25.0)))
base = paint(base, (0.25, 0.16, 0.14), pink * smooth01(0.3, 0.85, noise(7976, 20.0)) * 0.45)
base = paint(base, (0.1, 0.08, 0.07), pink * smooth01(0.03, 0.005, P[..., 2]) * 0.6)                    # the soles' dirt
nose = pink & (P[..., 1] > 0.53)
base = paint(base, (0.55, 0.3, 0.3), nose * 0.7)

# The tail: grey-pink, ringed with darker scale rows, browner and grimed toward the tip; a scar across it near the rump.
tailm = is_("mm_tail")
along = field(tail_along)
ring = field(lambda q: smooth01(0.8, 0.99, np.abs(np.sin(np.linalg.norm(q - TAIL_PTS[0], axis=1) * 420))))
base = paint(base, (0.42, 0.32, 0.3), tailm * (0.9 + 0.12 * noise(7977, 30.0)))
base = paint(base, (0.2, 0.15, 0.13), ring * tailm * 0.55)
base = paint(base, (0.13, 0.1, 0.085), smooth01(0.3, 1.0, along) * tailm * 0.6)
base = paint(base, (0.08, 0.065, 0.055), smooth01(0.3, 0.9, -up) * tailm * 0.4)                       # dragged in the dirt
base = paint(base, (0.25, 0.08, 0.07), smooth01(0.012, 0.004, np.abs(along - 0.22)) * tailm * 0.7)

# The ears: the backs the pelt's colour, thin; the insides pink-grey, veined red; the torn one's notch raw at its edge.
ears = is_("mm_ear")
opens = np.where(P[..., 0:1] > 0, np.array(tuple(g["EAR_OPEN"]["r"]), np.float32), np.array(tuple(g["EAR_OPEN"]["l"]), np.float32))
inside = smooth01(-0.1, 0.4, (N * opens).sum(-1))
base = paint(base, (0.38, 0.27, 0.26), ears * (0.4 + 0.55 * inside))
base = paint(base, (0.16, 0.12, 0.1), ears * (1 - inside) * 0.5)
ev = field(lambda q: smooth01(0.9, 0.97, ridged(q, 7941, 60.0)))
base = paint(base, (0.3, 0.08, 0.08), ev * ears * inside * 0.7)

# The mouth's insides dark wet red; the incisors orange-yellow (a rat's enamel), browner at the roots and chipped pale at
# the edges; the claws dark horn, paler at the points; the whiskers dark.
mouth = is_("mm_mouth")
base = paint(base, (0.14, 0.03, 0.03), mouth * (0.85 + 0.3 * noise(7978, 40.0)))
teeth = is_("mm_tooth")
root = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TEETH_AT], 0), 0)
base[teeth] = np.array((0.62, 0.36, 0.1), np.float32)
base = paint(base, (0.28, 0.15, 0.06), smooth01(0.008, 0.002, root) * teeth)
base = paint(base, (0.75, 0.6, 0.4), smooth01(0.02, 0.026, root) * teeth * 0.6)
claw = is_("mm_claw")
tip = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in CLAWS], 0), 0)
base[claw] = np.array((0.05, 0.042, 0.035), np.float32)
base = paint(base, (0.25, 0.22, 0.18), smooth01(0.002, 0.007, tip) * claw * 0.6)
base[is_("mm_whisker")] = np.array((0.06, 0.055, 0.05), np.float32)

# The antlers: bone under velvet. The velvet a mousy brown-grey, short-haired, on the beams and the palms' undersides;
# torn back where it's ripped (the shreds hang from the torn edges), raw red at the tear and bloodied; the tines' tips and
# the palms' rims worn through to pale bone. The shreds themselves: wet dark red inside, the velvet's grey-brown outside.
bone = is_("mm_antler")
shred = is_("mm_velvet")
velvet = smooth01(-0.35, 0.1, noise(7981, 9.0) + 0.35 * smooth01(0.0, -0.6, up))
tipd = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in TIPS], 0), 0)
worn = smooth01(0.02, 0.006, tipd)
torn = np.min(np.stack([np.linalg.norm(P - t, axis=-1) for t in VELVET_AT], 0), 0)
base = paint(base, (0.62, 0.55, 0.44), bone * (0.9 + 0.1 * noise(7982, 40.0)))                       # bone
base = paint(base, (0.35, 0.28, 0.2), bone * smooth01(0.5, 0.9, noise(7983, 30.0)) * 0.5)               # stained
base = paint(base, (0.24, 0.2, 0.16), bone * velvet * (1 - worn) * 0.92)                               # the velvet
edge = smooth01(0.3, 0.45, velvet) * smooth01(0.6, 0.45, velvet)
base = paint(base, (0.42, 0.06, 0.05), bone * edge * 0.8)                                             # raw at its torn edge
base = paint(base, (0.3, 0.04, 0.035), bone * smooth01(0.06, 0.0, torn + 0.04 * noise(7984, 30.0)) * (1 - worn) * 0.65)  # bled
base = paint(base, (0.78, 0.72, 0.6), bone * worn * 0.85)                                              # worn through
outer = smooth01(-0.2, 0.3, N[..., 0] * np.sign(P[..., 0]))
base = paint(base, (0.32, 0.05, 0.045), shred * (1 - outer))
base = paint(base, (0.2, 0.16, 0.13), shred * outer * 0.9)
base = np.clip(base, 0, 1)

rough = np.full((S, S), 0.84, np.float32) + 0.06 * noise(7985, 10.0)
rough = np.where(pink | tailm | ears, 0.6, rough)
rough[nose] = 0.3
rough[mouth] = 0.2
rough[teeth] = 0.35
rough[claw] = 0.45
rough = np.where(bone, 0.75 - 0.35 * edge, rough)
rough[shred] = 0.35
rough = np.where(fur, rough - 0.2 * scab, rough)
rough = np.clip(rough, 0.03, 0.95)

ao = atlas.maps["AO"][..., 0]
base = base * (0.35 + 0.65 * ao)[..., None]
k = np.clip((1 - ao) * 2.0, 0, 1) * 0.45
base = base * (1 - k)[..., None] + np.array((0.02, 0.018, 0.016), np.float32) * k[..., None]
atlas.finish(base, kit, arm, made=make.provenance("mouse_moose", "the Mouse Moose, modelled in tools/blender/mouse_moose.py, its colour painted here"),
             rough=rough, lod=0.4)
