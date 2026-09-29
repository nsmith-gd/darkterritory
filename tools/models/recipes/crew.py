"""CREW (GDD §29): the rail worker of tools/blender/crew.py, taken to the fidelity target (ARCHITECTURE §8 note 58).

tools/blender/crew.py stays the crew's source: its skeleton, its weights, its clips and its game mesh (the kit's lofts
and tubes, 1.8 m, SK_Human, variants 0 cap, 1 helmet, 2 cap + scarf, 3 helmet + scarf). This recipe runs it, then:
  * swaps the egg of a head for Lee Perry-Smith's scanned head (CC BY 3.0), decimated, on the head bone, the hats
    over it as before;
  * models a high-resolution copy of every part over the game mesh: the coat's weight hanging in folds below the
    belt, seams down the back and sides, buttons down the front, the sleeves bunched at the elbow and the cuff,
    trousers creased at the knee and gathered over the boots, laced boots with a welt and a cleated sole, gloves with
    their fingers parted and a seam over the knuckles, a cap of stitched panels, a helmet with a rolled rim;
  * dresses the high copy in the texture library's cloth, oilskin, leather and wool at the library's own scale, and
    the scan's own skin;
  * bakes it down (normal, occlusion, colour) into one 1024 atlas on the game mesh, part by part so a cap never
    shadows a helmet it's never worn with, and sooted: soot in the creases, grime rising from the boots.
The chest lamp keeps crew_atlas's lit glass (a pure light, not baked); the shovel keeps the library's own layers.

    python3 tools/models/fetch.py threejs-leeperrysmith && tools/models/build.sh crew
    CREW_PREVIEW=1 tools/models/build.sh crew   # renders the high figure to out/review/crew-high-*.png, no bake
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
BLENDER = os.path.join(os.path.dirname(HERE), "blender")
sys.path.insert(0, BLENDER)
import bmesh  # noqa: E402
import bpy  # noqa: E402
import numpy as np  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import cook  # noqa: E402
import rig  # noqa: E402

# ----------------------------------------------------------------------------------------------------------------
# The crew as tools/blender builds it, its export held back until the game mesh wears the bake.
_export = rig.export
held = {}
rig.export = lambda path, kit: held.update(kit=kit)
CREW = os.path.join(BLENDER, "crew.py")
g = {"__name__": "crew_source", "__file__": CREW}
exec(compile(open(CREW).read(), CREW, "exec"), g)
rig.export = _export
kit, sk = held["kit"], g["sk"]
arm = sk.rig
parts = {o.name: o for o in bpy.data.objects if o.type == "MESH" and o.parent is arm}
# Modelled and baked in the bind pose (the clips' last key is still on the armature).
arm.data.pose_position = "REST"
print("[dt] crew parts", {n: len(o.data.polygons) for n, o in sorted(parts.items())})


def mat_named(o, prefix):
    return {i for i, m in enumerate(o.data.materials) if m is not None and m.name.startswith(prefix)}


# ----------------------------------------------------------------------------------------------------------------
# The head: the scan in place of the egg (the kit's face, hair, nose and ears go; the neck stays under the collar).
body = parts["body"]
doomed = mat_named(body, "crew_atlas.face") | mat_named(body, "crew_atlas.hair")
bm = bmesh.new()
bm.from_mesh(body.data)
bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index in doomed], context="FACES")
bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
bm.to_mesh(body.data)
bm.free()

HEAD_C = g["HEAD_C"]
head = cook.load("threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/LeePerrySmith.glb")
skin_img = os.path.join(cook.SOURCES, "threejs-leeperrysmith", "examples/models/gltf/LeePerrySmith/Map-COL.jpg")
skin = bpy.data.materials.new("scan_skin")
skin.use_nodes = True
tex = skin.node_tree.nodes.new("ShaderNodeTexImage")
tex.image = bpy.data.images.load(skin_img)
skin.node_tree.links.new(tex.outputs["Color"], skin.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
for o in head:
    o.data.materials.clear()
    o.data.materials.append(skin)
cook.fit(head, height=0.46)
cook.rotate(head, 180)
cook.cut(head, lambda c: c.z > 0.2)
lo, hi = cook.bounds(head)
# Crown at the egg's crown, centred over the neck, the face as far forward as the egg's was.
S = 0.93
cook.transform(head, Matrix.Scale(S, 4))
lo, hi = cook.bounds(head)
cook.move(head, (-(lo.x + hi.x) / 2, HEAD_C.y + 0.006 - (lo.y + hi.y) / 2, HEAD_C.z + 0.118 - hi.z))
lo, hi = cook.bounds(head)
print("[dt] head", tuple(round(c, 3) for c in lo), tuple(round(c, 3) for c in hi))


def close_mouth(o):
    """The scan's lips are parted on a cavity (a mouth, teeth, a tongue): everything a few mm behind the lips' own front
    surface, in the band between them, goes, and the slit is capped. A game mesh collapsed from it would otherwise cave
    the lips into the hole."""
    me = o.data
    co = np.array([v.co for v in me.vertices], np.float32)
    tip = co[co[:, 1].argmax()]
    lo_z, hi_z = tip[2] - 0.068, tip[2] - 0.025
    band = (np.abs(co[:, 0]) < 0.034) & (co[:, 2] > lo_z) & (co[:, 2] < hi_z)
    pts = co[band]
    bm = bmesh.new()
    bm.from_mesh(me)
    doomed = []
    for f in bm.faces:
        c = f.calc_center_median()
        if abs(c.x) >= 0.03 or not (lo_z + 0.006 < c.z < hi_z - 0.006):
            continue
        near = pts[(np.abs(pts[:, 0] - c.x) < 0.005) & (np.abs(pts[:, 2] - c.z) < 0.005)]
        if len(near) and c.y < near[:, 1].max() - 0.005:
            doomed.append(f)
    bmesh.ops.delete(bm, geom=doomed, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    rim = [e for e in bm.edges if e.is_boundary and abs(e.verts[0].co.x) < 0.04 and lo_z - 0.01 < e.verts[0].co.z < hi_z + 0.01]
    bmesh.ops.holes_fill(bm, edges=rim, sides=0)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(me)
    bm.free()
    me.update()
    print("[dt] mouth closed:", len(doomed), "faces out")


for o in head:
    close_mouth(o)


# The game mesh's head: the scan decimated, on the head bone, blending into the neck under the collar.
head_low = cook.duplicate(head)
cook.decimate(head_low, 2600)
for o in head_low:
    co = np.array([v.co for v in o.data.vertices], np.float32)
    # The whole face rides the head (a chin on the neck bone shears off it when the clips tip the two apart); only the
    # last of the neck, under the collar, blends.
    w = np.clip((co[:, 2] - 1.545) / 0.02, 0, 1)
    gh, gn = o.vertex_groups.new(name="head"), o.vertex_groups.new(name="neck")
    for vi, k in enumerate(w):
        if k > 0.02:
            gh.add([vi], float(k), "REPLACE")
        if k < 0.98:
            gn.add([vi], float(1 - k), "REPLACE")
bpy.ops.object.select_all(action="DESELECT")
for o in head_low:
    o.select_set(True)
body.select_set(True)
bpy.context.view_layer.objects.active = body
bpy.ops.object.join()

# ----------------------------------------------------------------------------------------------------------------
# The high copy, dressed in the library. Each kit material (by name) wears a library layer at the library's scale
# (repeats a metre), tinted toward the kit's palette; soft ones (cloth, leather over a form) are subdivided smooth,
# hard ones (buckles, the lamp's box, the badge) keep their edges, bevelled.
import make  # noqa: E402

make.USED.clear()
make._mats.clear()
make.LOW.clear()
DRESS = {
    # Cloth at several times the library's repeat: at a person's scale the weave is a texture, not a pattern.
    "crew_atlas.coat": ("coat_oilskin", 7.0, (0.8, 0.8, 0.66), 0.5, 3),
    "crew_atlas.sleeve": ("coat_oilskin", 7.0, (0.8, 0.8, 0.66), 0.5, 3),
    "crew_atlas.trouser": ("wool", 9.0, (0.42, 0.44, 0.5), 0.9, 3),
    "crew_atlas.boot": ("leather", 4.0, (0.36, 0.3, 0.27), 0.55, 2),
    "crew_atlas.gloves": ("leather", 6.0, (0.5, 0.41, 0.34), 0.7, 0),
    "skin.neck": ("skin", 4.0, (0.9, 0.76, 0.68), 0.6, 2),
    "crew_atlas.cap": ("wool", 10.0, (0.3, 0.32, 0.37), 0.9, 2),
    "crew_atlas.helmet": ("paint_olive", 3.0, (0.8, 0.8, 0.7), 0.5, 2),
    "crew_atlas.belt": ("leather", 5.0, (0.42, 0.32, 0.25), 0.5, 0),
    "leather.strap": ("leather", 5.0, (0.42, 0.32, 0.25), 0.5, 0),
    "crew_atlas.satchel": ("coat_oilskin", 9.0, (0.75, 0.68, 0.52), 0.85, 0),
    "crew_atlas.badge": ("brass", 6.0, (1, 1, 1), 0.35, 0),
    "crew_atlas.scarf": ("wool", 10.0, (0.38, 0.3, 0.3), 0.95, 2),
    "iron_plate.crew": ("iron_plate", 4.0, (0.55, 0.55, 0.55), 0.5, 0),
}


def dress(m):
    if m.name.startswith("scan_skin"):
        return m, 0
    key = next(k for k in DRESS if m.name.startswith(k))
    layer, scale, tint, rough, subdiv = DRESS[key]
    return make.lib(layer, scale, tint, rough), subdiv


def high_of(o):
    """The part's high copy, split by material, each piece subdivided (soft) or bevelled (hard) and dressed."""
    out = []
    for mi, m in enumerate(o.data.materials):
        if m is None or m.name.startswith(("crew_atlas.lamp", "scan_skin")):
            continue
        me = o.data.copy()
        bm = bmesh.new()
        bm.from_mesh(me)
        bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index != mi], context="FACES")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        if not bm.faces:
            bm.free()
            continue
        bm.to_mesh(me)
        bm.free()
        h = bpy.data.objects.new(f"{o.name}_{m.name}_high", me)
        bpy.context.scene.collection.objects.link(h)
        h.matrix_world = o.matrix_world
        h.vertex_groups.clear()
        mat, subdiv = dress(m)
        me.materials.clear()
        me.materials.append(mat)
        bpy.context.view_layer.objects.active = h
        if subdiv:
            mod = h.modifiers.new("sub", "SUBSURF")
            mod.levels = mod.render_levels = subdiv
            bpy.ops.object.modifier_apply(modifier=mod.name)
        elif not m.name.startswith("scan_skin"):
            mod = h.modifiers.new("bevel", "BEVEL")
            mod.width, mod.segments, mod.limit_method = 0.0025, 2, "ANGLE"
            bpy.ops.object.modifier_apply(modifier=mod.name)
            mod = h.modifiers.new("sub", "SUBSURF")
            mod.subdivision_type = "SIMPLE"
            mod.levels = mod.render_levels = 2
            bpy.ops.object.modifier_apply(modifier=mod.name)
        for f in me.polygons:
            f.use_smooth = True
        h["dt_kind"] = m.name.split(".")[0] if m.name.startswith("scan_skin") else next(k for k in DRESS if m.name.startswith(k))
        out.append(h)
    return out


def bell(x):
    return np.exp(-x * x)


def smooth01(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def displace(o, fn):
    """Pushes every vertex along its normal by fn(positions Nx3, normals Nx3) metres."""
    me = o.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    no = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("normal", no)
    co, no = co.reshape(-1, 3), no.reshape(-1, 3)
    d = np.asarray(fn(co, no), np.float32)
    me.vertices.foreach_set("co", (co + no * d[:, None]).ravel())
    me.update()


def fine(p, amount=0.0012, scale=70, seed=0):
    return amount * cook.noise_np(p, seed, scale)


def coat(p, n):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    a = np.arctan2(x, y)
    wobble = cook.noise_np(p, 3, 5) * 1.4
    # The skirt's weight: long folds from the belt down, deepening to the hem, fewer across the lapped front.
    hang = smooth01(0.97, 0.56, z) * (0.35 + 0.65 * smooth01(0.1, 0.5, np.abs(a)))
    d = 0.016 * hang * np.sin(a * 7 + wobble)
    # Gathered under the belt; the back dragged across from the shoulder blades; a roll at the collar.
    d += 0.004 * np.sin(a * 26) * bell((z - 1.005) / 0.05)
    d += 0.0035 * np.sin(a * 9 + z * 22 + wobble) * bell((z - 1.25) / 0.12) * (np.abs(a) > 1.6)
    d += 0.004 * bell((z - 1.525) / 0.012)
    # Seams: down the back and both sides.
    for seam in (np.pi / 2, -np.pi / 2, np.pi):
        da = np.abs(np.angle(np.exp(1j * (a - seam))))
        d -= 0.0022 * bell(da * 0.2 / 0.01)
    return d + fine(p)


def sleeve(p, n):
    u, b = np.abs(p[:, 0]), np.arctan2(p[:, 2] - 1.448, p[:, 1])
    wobble = cook.noise_np(p, 5, 9)
    d = 0.006 * np.sin(u * 190 + wobble * 2) * bell((u - 0.47) / 0.05) * (0.6 + 0.4 * wobble)
    d += 0.004 * np.sin(u * 260 + b) * bell((u - 0.64) / 0.03)
    d += 0.005 * np.sin(b * 6 + wobble) * bell((u - 0.24) / 0.05) * (p[:, 2] < 1.45)
    d -= 0.002 * bell(np.abs(np.angle(np.exp(1j * (b + np.pi / 2)))) * 0.07 / 0.008)
    # The cuff turned back: a ridge where it folds.
    d += 0.003 * bell((u - 0.698) / 0.006)
    return d + fine(p)


def trouser(p, n):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    c = np.arctan2(x - np.sign(x) * 0.105, y)
    wobble = cook.noise_np(p, 7, 8)
    d = 0.005 * np.sin(z * 150 + c * 2 + wobble * 2) * bell((z - 0.53) / 0.06)
    d += 0.006 * np.sin(z * 210 + c * 3 + wobble) * bell((z - 0.3) / 0.04)
    d += 0.002 * bell(c / 0.15)
    return d + fine(p, 0.0009, 90)


def boot(p, n):
    z = p[:, 2]
    c = np.arctan2(p[:, 0] - np.sign(p[:, 0]) * 0.105, p[:, 1])
    d = 0.0025 * np.sin(z * 300 + c * 2) * bell((z - 0.13) / 0.025)
    # The welt: a lip where the upper meets the sole.
    d += 0.0025 * bell((z - 0.02) / 0.004)
    return d + fine(p, 0.0006, 120)


def glove(p, n):
    ax, y = np.abs(p[:, 0]), p[:, 1]
    fingers = smooth01(0.82, 0.835, ax)
    d = np.zeros(len(p), np.float32)
    for gy in (-0.022, 0.0, 0.022):
        d -= 0.0045 * bell((y - gy) / 0.004) * fingers
    d += 0.002 * bell((ax - 0.815) / 0.004)
    return d + fine(p, 0.0008, 110)


def cap(p, n):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    a = np.arctan2(x, y - 0.012)
    d = np.zeros(len(p), np.float32)
    crown = z > 1.765
    for k in range(6):
        da = np.abs(np.angle(np.exp(1j * (a - k * np.pi / 3))))
        d -= 0.0018 * bell(da * 0.11 / 0.004) * crown
    d += 0.002 * bell((z - 1.748) / 0.004)
    return d + fine(p, 0.0007, 120)


def helmet(p, n):
    return 0.0025 * cook.noise_np(p, 11, 22) + fine(p, 0.0006, 100)


def scarf(p, n):
    return 0.004 * cook.noise_np(p, 13, 30) + 0.0012 * np.sin(p[:, 2] * 400) + fine(p, 0.001, 90)


SHAPE = {"crew_atlas.coat": coat, "crew_atlas.sleeve": sleeve, "crew_atlas.trouser": trouser, "crew_atlas.boot": boot,
         "crew_atlas.gloves": glove, "crew_atlas.cap": cap, "crew_atlas.helmet": helmet, "crew_atlas.scarf": scarf,
         "crew_atlas.satchel": lambda p, n: fine(p, 0.001, 60), "crew_atlas.belt": lambda p, n: fine(p, 0.0005, 150),
         "leather.strap": lambda p, n: fine(p, 0.0005, 150)}

# Which highs bake onto which game part (a cap never shadows the helmet it isn't worn with).
BAKED = ["body", "hat_cap", "hat_helmet", "scarf"]
highs = {}
for name in BAKED:
    hs = high_of(parts[name])
    for h in hs:
        fn = SHAPE.get(h["dt_kind"])
        if fn is not None:
            displace(h, fn)
    highs[name] = hs
# The scan's own head, full resolution, in the body's group.
highs["body"] += head

from mathutils.bvhtree import BVHTree  # noqa: E402

coat_high = next(h for h in highs["body"] if h["dt_kind"] == "crew_atlas.coat")
tree = BVHTree.FromObject(coat_high, bpy.context.evaluated_depsgraph_get())
BUTTON = make.lib("paint_black", 8.0, (0.6, 0.55, 0.5), 0.4)
for z in (0.8, 0.9, 1.13, 1.23, 1.33, 1.43):
    hit = tree.ray_cast(Vector((0.07, 0.5, z)), Vector((0, -1, 0)))
    if hit[0] is not None:
        at, nrm = hit[0], hit[1]
        highs["body"].append(make.cyl(at - nrm * 0.002, at + nrm * 0.005, 0.011, BUTTON, n=12, bevel=0.002, name="button",
                                      r1=0.009, low=0))
# Laces up the front of each boot, crossing between eyelets.
LACE = make.lib("leather", 8.0, (0.35, 0.3, 0.26), 0.6)
for sx in (-1, 1):
    x = sx * 0.105
    for k in range(5):
        z = 0.125 + k * 0.03
        for s in (-1, 1):
            highs["body"].append(make.cyl((x - s * 0.02, 0.056, z), (x + s * 0.02, 0.056, z + 0.026), 0.0028, LACE, n=6,
                                          bevel=0, name="lace", low=0))
            highs["body"].append(make.torus((x + s * 0.022, 0.055, z), (0, 1, 0), 0.0045, 0.0015, BUTTON, n=8, m=4,
                                            name="eyelet", low=None))
# The helmet's rolled rim.
RIM = make.lib("paint_olive", 2.0, (0.8, 0.8, 0.7), 0.5)
rim = make.torus((0, 0.006, 1.712), (0, 0, 1), 0.153, 0.006, RIM, n=48, m=8, name="rim", low=None)
rim.scale = (1.0, 1.066, 1.0)
bpy.context.view_layer.objects.active = rim
bpy.ops.object.transform_apply(scale=True)
highs["hat_helmet"].append(rim)
print("[dt] crew highs", {k: sum(len(h.data.polygons) for h in v) for k, v in highs.items()})

if os.environ.get("CREW_PREVIEW"):
    out = os.path.join(cook.ROOT, "out", "review")
    os.makedirs(out, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.samples = 16
    scene.cycles.use_denoising = False
    scene.render.resolution_x, scene.render.resolution_y = 500, 700
    scene.world = bpy.data.worlds.new("w")
    scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.8
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.rotation_euler = (0.7, 0.3, 2.6)
    sun.data.energy = 4
    scene.collection.objects.link(sun)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    variant = os.environ.get("CREW_PREVIEW")
    for o in parts.values():
        o.hide_render = True
    for name, hs in highs.items():
        for h in hs:
            h.hide_render = name not in ("body", "hat_helmet" if variant == "helmet" else "hat_cap")
    for view, d, c, dist in (("front", (0.2, 1, 0.1), (0, 0, 0.95), 4.2), ("back", (-0.3, -1, 0.1), (0, 0, 0.95), 4.2),
                             ("head", (0.4, 1, 0.15), (0, 0.02, 1.62), 1.0), ("hand", (0.2, 0.6, 1), (0.75, 0, 1.44), 0.7),
                             ("boot", (0.5, 1, 0.3), (0.1, 0.05, 0.15), 0.9)):
        cam.location = Vector(c) + Vector(d).normalized() * dist
        cam.rotation_euler = (Vector(c) - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = os.path.join(out, f"crew-high-{view}.png")
        bpy.ops.render.render(write_still=True)
    print("[dt] crew preview -> out/review/crew-high-*.png")
    sys.exit(0)

# ----------------------------------------------------------------------------------------------------------------
# One atlas over every baked part: the parts are joined for the unwrap and the bakes (a face's part kept as an
# attribute), then split back into the parts the engine draws per variant. The lamp's glass keeps crew_atlas's lit cell
# (its faces keep their UVs and their material: a pure light). The face and the hands get more of the atlas than their
# area would: they're seen closest.
SIZE = 1024
LAMP = next(m for m in bpy.data.materials if m.name.startswith("crew_atlas.lamp"))
LAMP["dt_library"] = True
keep = {n: {"props": {k: parts[n][k] for k in parts[n].keys()}} for n in BAKED}
for pi, n in enumerate(BAKED):
    o = parts[n]
    attr = o.data.attributes.new("dt_part", "INT", "FACE")
    attr.data.foreach_set("value", [pi] * len(o.data.polygons))
    kinds = []
    for f in o.data.polygons:
        m = o.data.materials[f.material_index]
        kinds.append(2 if m.name.startswith("crew_atlas.lamp") else 1 if m.name.startswith("scan_skin")
                     else 3 if m.name.startswith("crew_atlas.gloves") else 0)
    k = o.data.attributes.new("dt_kind", "INT", "FACE")
    k.data.foreach_set("value", kinds)
bpy.ops.object.select_all(action="DESELECT")
for n in BAKED:
    parts[n].select_set(True)
bpy.context.view_layer.objects.active = parts["body"]
bpy.ops.object.join()
low = parts["body"]
low.name = "crew_joined"
nf = len(low.data.polygons)
part_of = np.empty(nf, np.int32)
low.data.attributes["dt_part"].data.foreach_get("value", part_of)
kind_of = np.empty(nf, np.int32)
low.data.attributes["dt_kind"].data.foreach_get("value", kind_of)
lit = kind_of == 2

low.data.uv_layers.active = low.data.uv_layers.new(name="baked")
bpy.ops.object.select_all(action="DESELECT")
low.select_set(True)
bpy.context.view_layer.objects.active = low
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_mode(type="FACE")
bm = bmesh.from_edit_mesh(low.data)
for f in bm.faces:
    f.select = not lit[f.index] and kind_of[f.index] != 1
bmesh.update_edit_mesh(low.data)
bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.006, area_weight=0.0, scale_to_bounds=True)
# The head in one piece, projected round a cylinder on the neck (the seam down the back, under the collar and the cap),
# in metres, so it lies at the same density as the rest before the boost.
bm = bmesh.from_edit_mesh(low.data)
uvl = bm.loops.layers.uv.active
for f in bm.faces:
    if kind_of[f.index] != 1:
        continue
    f.select = True
    us = []
    for loop in f.loops:
        q = loop.vert.co - HEAD_C
        # 0.5 at the face, 0 and 1 at the back.
        us.append(math.atan2(q.x, q.y) / (2 * math.pi) + 0.5)
    if max(us) - min(us) > 0.5:
        us = [u + 1.0 if u < 0.5 else u for u in us]
    for loop, u in zip(f.loops, us):
        loop[uvl].uv = (u * 0.62, (loop.vert.co.z - 1.5) * 1.0)
bmesh.update_edit_mesh(low.data)
bpy.ops.uv.select_all(action="SELECT")
bpy.ops.uv.average_islands_scale()
bm = bmesh.from_edit_mesh(low.data)
uvl = bm.loops.layers.uv.active
for f in bm.faces:
    k = {1: 3.5, 3: 1.8}.get(int(kind_of[f.index]), 1.0)
    if k != 1.0:
        for loop in f.loops:
            loop[uvl].uv *= k
bmesh.update_edit_mesh(low.data)
bpy.ops.uv.select_all(action="SELECT")
bpy.ops.uv.pack_islands(rotate=True, margin=0.006)
bpy.ops.object.mode_set(mode="OBJECT")
low.data.uv_layers.remove(low.data.uv_layers["UVMap"])
low.data.uv_layers["baked"].name = "UVMap"
# Bake targets: a group's faces bake into the atlas, every other face (the other groups', the lamp's) into a scrap
# image, so each part only ever takes its own high copy.
bake_mat = bpy.data.materials.new("crew_bake")
bake_mat.use_nodes = True
bake_tex = bake_mat.node_tree.nodes.new("ShaderNodeTexImage")
scrap_mat = bpy.data.materials.new("crew_scrap")
scrap_mat.use_nodes = True
scrap_tex = scrap_mat.node_tree.nodes.new("ShaderNodeTexImage")
scrap_tex.image = bpy.data.images.new("crew_scrap", 8, 8)
scrap_mat.node_tree.nodes.active = scrap_tex
low.data.materials.clear()
low.data.materials.append(bake_mat)
low.data.materials.append(scrap_mat)
for flag in ("visible_diffuse", "visible_glossy", "visible_shadow", "visible_transmission", "visible_volume_scatter"):
    setattr(low, flag, False)
parts["shovel"].hide_render = True

# A height mask off the high surface (grime rising from the boots): its z, emitted.
zmat = bpy.data.materials.new("crew_z")
zmat.use_nodes = True
znt = zmat.node_tree
znt.nodes.remove(znt.nodes["Principled BSDF"])
geo = znt.nodes.new("ShaderNodeNewGeometry")
sep = znt.nodes.new("ShaderNodeSeparateXYZ")
div = znt.nodes.new("ShaderNodeMath")
div.operation = "DIVIDE"
div.inputs[1].default_value = 1.8
emit = znt.nodes.new("ShaderNodeEmission")
znt.links.new(geo.outputs["Position"], sep.inputs["Vector"])
znt.links.new(sep.outputs["Z"], div.inputs[0])
znt.links.new(div.outputs[0], emit.inputs["Color"])
znt.links.new(emit.outputs["Emission"], znt.nodes["Material Output"].inputs["Surface"])

scene = bpy.context.scene
scene.render.engine = "CYCLES"
scene.cycles.device = "CPU"
scene.cycles.use_denoising = False
scene.cycles.samples = 24
scene.render.bake.use_selected_to_active = True
scene.render.bake.cage_extrusion = 0.02
scene.render.bake.max_ray_distance = 0.05
scene.render.bake.margin = 3
all_highs = [h for hs in highs.values() for h in hs]
# (A bake clears the whole image, so each group bakes into an image of its own, and its pixels, where the bake wrote
# them (alpha), go into the atlas.)
acc = {"NORMAL": np.tile(np.array([0.5, 0.5, 1.0, 1.0], np.float32), (SIZE, SIZE, 1)),
       "AO": np.ones((SIZE, SIZE, 4), np.float32), "DIFFUSE": np.full((SIZE, SIZE, 4), 0.2, np.float32),
       "EMIT": np.full((SIZE, SIZE, 4), 0.5, np.float32)}
# (Only the colour bake leaves alpha where it didn't write: its coverage is the group's mask for every map.)
masks = {}
# The head bakes as a group of its own, from the scan, with a tight cage: the cloth's high copy is subdivided in from
# its game mesh by a centimetre or two, but a face's cage that deep projects the cheeks onto the nose.
highs["head"] = [h for h in highs["body"] if h in head]
highs["body"] = [h for h in highs["body"] if h not in head]
group_of = np.where(kind_of == 1, len(BAKED), part_of)
CAGES = {"head": (0.006, 0.02)}
for pi, name in enumerate(BAKED + ["head"]):
    low.data.polygons.foreach_set("material_index", np.where((group_of == pi) & ~lit, 0, 1).astype(np.int32))
    low.data.update()
    for h in all_highs:
        h.hide_render = h not in highs[name]
    scene.render.bake.cage_extrusion, scene.render.bake.max_ray_distance = CAGES.get(name, (0.02, 0.05))
    wrote = None
    for kind in ("DIFFUSE", "NORMAL", "AO", "EMIT"):
        if kind == "EMIT":
            for h in highs[name]:
                h.data.materials.clear()
                h.data.materials.append(zmat)
        img = bpy.data.images.new(f"crew_{kind}_{name}", SIZE, SIZE, alpha=True)
        img.generated_color = (0, 0, 0, 0)
        img.colorspace_settings.name = "sRGB" if kind == "DIFFUSE" else "Non-Color"
        bake_tex.image = img
        bake_mat.node_tree.nodes.active = bake_tex
        bpy.ops.object.select_all(action="DESELECT")
        for h in highs[name]:
            h.select_set(True)
        low.select_set(True)
        bpy.context.view_layer.objects.active = low
        if kind == "NORMAL":
            scene.render.bake.normal_space = "TANGENT"
            bpy.ops.object.bake(type="NORMAL")
        elif kind == "DIFFUSE":
            scene.render.bake.use_pass_direct = False
            scene.render.bake.use_pass_indirect = False
            scene.render.bake.use_pass_color = True
            bpy.ops.object.bake(type="DIFFUSE")
        else:
            bpy.ops.object.bake(type=kind)
        px = cook._image_array(img, SIZE)
        if wrote is None:
            wrote = px[..., 3] > 0.5
        acc[kind][wrote] = px[wrote]
        bpy.data.images.remove(img)
    masks[name] = wrote
    if os.environ.get("CREW_DEBUG"):
        print("[dt] bake", name, int(wrote.sum()))
cook.delete(all_highs)
parts["shovel"].hide_render = False
if os.environ.get("DT_BAKE_DEBUG"):
    for kind, arr in acc.items():
        cook._save(arr, os.path.join(cook.ROOT, "out", "review", f"bake-crew_{kind.lower()}.png"))
nimg = bpy.data.images.new("crew_normal", SIZE, SIZE, alpha=False)
nimg.colorspace_settings.name = "Non-Color"
nimg.pixels.foreach_set(acc["NORMAL"][::-1].ravel())

# The colour, darkened by the occlusion and sooted: soot in every crease, the mud and wet of the ballast climbing the
# boots and the coat's hem, the shoulders and cap dulled where the smoke settles on them.
ao = acc["AO"][..., 0]
z = acc["EMIT"][..., 0] * 1.8
base = cook.srgb_to_lin(acc["DIFFUSE"][..., :3])
face = masks["head"]
# (A face takes its occlusion gently: the lips' line, deep in the scan, would soot into an open mouth.)
ao = np.where(face, ao ** 0.35, ao)
base = base * (0.35 + 0.65 * ao)[..., None]
crease = np.clip((1 - ao) * 2.2, 0, 1) * np.where(face, 0.3, 1.0)
soot = np.array([0.018, 0.016, 0.014], np.float32)
base = base * (1 - 0.45 * crease)[..., None] + soot * (0.45 * crease)[..., None]
mud = np.array([0.05, 0.04, 0.03], np.float32)
low_down = np.clip((0.55 - z) / 0.5, 0, 1) ** 1.5 * 0.55
base = base * (1 - low_down)[..., None] + mud * low_down[..., None]
settled = np.clip((z - 1.35) / 0.4, 0, 1) * 0.2
base = base * (1 - settled)[..., None] + soot * settled[..., None]
# The skin: the scan's is a clean, warm studio face. Out here it's sallow with cold and smoke, soot worked into it in
# smudges (the atlas's own noise: the head's one island takes it as blotches across the face).
yy, xx = np.mgrid[0:SIZE, 0:SIZE].astype(np.float32) / SIZE
smudge = np.clip(cook.noise_np(np.stack([xx.ravel() * 9, yy.ravel() * 9, np.zeros(SIZE * SIZE)], 1), 17, 1.0).reshape(SIZE, SIZE)
                 * 0.8 + cook.noise_np(np.stack([xx.ravel() * 31, yy.ravel() * 31, np.ones(SIZE * SIZE)], 1), 19, 1.0).reshape(SIZE, SIZE) * 0.4, 0, 1)
tone = np.array([0.66, 0.58, 0.52], np.float32)
grey = base.mean(-1, keepdims=True)
sallow = (base * 0.55 + grey * 0.45) * tone
sallow = sallow * (1 - 0.55 * smudge)[..., None] + soot * (0.55 * smudge)[..., None]
base = np.where(face[..., None], sallow, base)
bimg = bpy.data.images.new("crew_base", SIZE, SIZE, alpha=True)
rgba = np.ones((SIZE, SIZE, 4), np.float32)
rgba[..., :3] = cook.lin_to_srgb(base)
bimg.pixels.foreach_set(rgba[::-1].ravel())

final = bpy.data.materials.new("crew_final")
final.use_nodes = True
nt = final.node_tree
bsdf = nt.nodes["Principled BSDF"]
t = nt.nodes.new("ShaderNodeTexImage")
t.image = bimg
nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
ntex = nt.nodes.new("ShaderNodeTexImage")
ntex.image = nimg
nmap = nt.nodes.new("ShaderNodeNormalMap")
nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
bsdf.inputs["Roughness"].default_value = 0.75
bsdf.inputs["Metallic"].default_value = 0.0
low.data.materials[0] = final
low.data.materials[1] = LAMP
low.data.polygons.foreach_set("material_index", lit.astype(np.int32))
for f in low.data.polygons:
    f.use_smooth = True
for flag in ("visible_diffuse", "visible_glossy", "visible_shadow", "visible_transmission", "visible_volume_scatter"):
    setattr(low, flag, True)

# Split back into the parts, each with its name and extras (variants), still skinned to the rig.
lows = []
for pi, name in enumerate(BAKED):
    o = low.copy()
    o.data = low.data.copy()
    bpy.context.scene.collection.objects.link(o)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if part_of[f.index] != pi], context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    for a in ("dt_part", "dt_kind"):
        if a in o.data.attributes:
            o.data.attributes.remove(o.data.attributes[a])
    if not any(o.data.polygons[i].material_index == 1 for i in range(len(o.data.polygons))):
        o.data.materials.pop(index=1)
    for k in list(o.keys()):
        del o[k]
    for k, v in keep[name]["props"].items():
        o[k] = v
    o.name = name
    o.data.name = f"crew_{name}"
    lows.append(o)
bpy.data.objects.remove(low, do_unlink=True)
print("[dt] crew parts baked", {o.name: len(o.data.polygons) for o in lows})
layers = cook.bake_layers("crew", lows, family="creature", source_ids=["threejs-leeperrysmith"],
                          made=make.provenance("crew", "the crew's clothes and kit, modelled over tools/blender/crew.py"),
                          size=SIZE)
cook._merge_index("crew", layers)
# (Scraps the bake left: nothing but the rig and its parts may go in the file.)
for o in [o for o in bpy.data.objects if o.type == "MESH" and o.parent is not arm]:
    bpy.data.objects.remove(o, do_unlink=True)
arm.data.pose_position = "POSE"
rig.export(os.path.join(cook.ROOT, "content", "art", "models", "crew.glb"), kit)
