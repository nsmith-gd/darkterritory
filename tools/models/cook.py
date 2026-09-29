"""The model cook: turns sourced models (tools/models/sources.json, fetched by fetch.py) into the engine's props, run
headless in Blender by tools/models/build.sh. A recipe (tools/models/recipes/<name>.py) imports what it needs,
bashes it into the thing it wants (cuts, bends, grime, parts of one model on another), and calls `finish()`, which:

  * decimates to the recipe's triangle budget and puts the pivot where the engine wants it (floor centre, metres,
    the model facing -Y in Blender, which the exporter turns into the engine's -Z forward);
  * bakes each material's PBR maps into the engine's layer format at 512 (ARCHITECTURE §8 note 55): a diffuse (base
    colour x occlusion, metals darkened as the texture library's converter does), a spec map (R strength, G gloss,
    B emissive mask) and a tangent-space normal map in tools/art's convention (glTF's +Y-up green flipped to y-down);
  * writes them to content/art/textures/models/ with an index (index.models.json) the engine reads beside the library's,
    with each layer's provenance;
  * skins everything rigidly to one root bone (Ballast.Assets reads skinned glTF: a prop is a model with one bone) and
    exports content/art/models/props/<name>.glb.

Deterministic: no randomness but fixed hashes, so a rebuild with the same Blender gives the same files.
"""
from __future__ import annotations

import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCES = os.path.join(ROOT, "intake", "_sources", "models")
TEXTURES = os.path.join(ROOT, "content", "art", "textures", "models")
PROPS = os.path.join(ROOT, "content", "art", "models", "props")
SIZE = 512


def args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def manifest():
    import re
    text = open(os.path.join(HERE, "sources.json")).read()
    return {s["id"]: s for s in json.loads(re.sub(r"^\s*//.*$", "", text, flags=re.M))}


# ----------------------------------------------------------------------------------------------------------------
# Importing

def load(source_id: str, rel: str):
    """Imports one file of a fetched source; returns its mesh objects (transforms applied, parents cleared)."""
    path = os.path.join(SOURCES, source_id, rel)
    if not os.path.exists(path):
        raise FileNotFoundError(f"{path}: run python3 tools/models/fetch.py {source_id}")
    before = set(bpy.data.objects)
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".obj":
        bpy.ops.wm.obj_import(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".ply":
        bpy.ops.wm.ply_import(filepath=path)
    elif ext == ".stl":
        bpy.ops.wm.stl_import(filepath=path)
    else:
        raise ValueError(f"can't import {ext}")
    new = [o for o in bpy.data.objects if o not in before]
    meshes = [o for o in new if o.type == "MESH"]
    for o in meshes:
        o["dt_source"] = source_id
        w = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = w
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    if meshes:
        bpy.context.view_layer.objects.active = meshes[0]
        bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    for o in new:
        if o.type != "MESH":
            bpy.data.objects.remove(o, do_unlink=True)
    return meshes


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    o.name = name
    return o


def bounds(objs):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        for v in o.data.vertices:
            p = o.matrix_world @ v.co
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    return lo, hi


def transform(objs, m: Matrix):
    for o in objs:
        o.data.transform(m)
        o.data.update()


def fit(objs, height=None, size=None, floor=True, centre=True):
    """Scales so the whole thing is `height` tall (or its largest side `size`), then stands it on z = 0 centred."""
    lo, hi = bounds(objs)
    ext = hi - lo
    s = 1.0
    if height is not None:
        s = height / max(ext.z, 1e-9)
    elif size is not None:
        s = size / max(ext.x, ext.y, ext.z, 1e-9)
    c = (lo + hi) / 2
    off = Vector((-c.x if centre else 0, -c.y if centre else 0, -lo.z if floor else -c.z))
    transform(objs, Matrix.Translation(off * s) @ Matrix.Scale(s, 4))


def cube(centre, half, name="cube"):
    """A box mesh (a plinth, a slab), to join into what's baked."""
    bpy.ops.mesh.primitive_cube_add(size=2, location=centre)
    o = bpy.context.view_layer.objects.active
    o.name = name
    o.scale = half
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=True)
    return o


def top_point(objs, fraction=0.03):
    """The centre of the highest few percent of vertices (a figure's head)."""
    pts = [o.matrix_world @ v.co for o in objs for v in o.data.vertices]
    pts.sort(key=lambda p: -p.z)
    k = max(1, int(len(pts) * fraction))
    return sum(pts[:k], Vector()) / k


def rotate(objs, degrees, axis="Z"):
    transform(objs, Matrix.Rotation(math.radians(degrees), 4, axis))


def move(objs, v):
    transform(objs, Matrix.Translation(Vector(v)))


def tris(objs):
    n = 0
    for o in objs:
        o.data.calc_loop_triangles()
        n += len(o.data.loop_triangles)
    return n


def decimate(objs, target):
    """Collapse-decimates every object by the same ratio so the whole comes to about `target` triangles."""
    total = tris(objs)
    if total <= target:
        return
    ratio = target / total
    for o in objs:
        m = o.modifiers.new("decimate", "DECIMATE")
        m.decimate_type = "COLLAPSE"
        m.ratio = ratio
        m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=m.name)


# ----------------------------------------------------------------------------------------------------------------
# Bashing: the operations recipes deform and combine with (all deterministic)

def noise3(p, seed=0, scale=1.0):
    """Cheap deterministic value noise in -1..1 (the same as tools/blender/rig.noise3)."""
    x, y, z = p[0] * scale, p[1] * scale, p[2] * scale
    xi, yi, zi = math.floor(x), math.floor(y), math.floor(z)
    xf, yf, zf = x - xi, y - yi, z - zi

    def h(i, j, k):
        n = (i * 73856093) ^ (j * 19349663) ^ (k * 83492791) ^ (seed * 2654435761)
        n = (n ^ (n >> 13)) * 1274126177
        return ((n ^ (n >> 16)) & 0xFFFF) / 32767.5 - 1.0

    def s(t):
        return t * t * (3 - 2 * t)

    u, v, w = s(xf), s(yf), s(zf)
    acc = 0.0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                acc += h(xi + dx, yi + dy, zi + dz) * (u if dx else 1 - u) * (v if dy else 1 - v) * (w if dz else 1 - w)
    return acc


def deform(objs, fn):
    """Moves every vertex: fn(position Vector) -> new position."""
    for o in objs:
        for v in o.data.vertices:
            v.co = Vector(fn(v.co.copy()))
        o.data.update()


def noise_np(p, seed=0, scale=1.0):
    """Vectorised value noise in -1..1 over an Nx3 array (the masks and lumps over a million-vertex scan)."""
    x = np.asarray(p, np.float64) * scale
    i = np.floor(x).astype(np.int64)
    f = x - i
    f = f * f * (3 - 2 * f)

    def h(ii):
        n = (ii[:, 0] * 73856093) ^ (ii[:, 1] * 19349663) ^ (ii[:, 2] * 83492791) ^ (seed * 2654435761)
        n = (n ^ (n >> 13)) * 1274126177
        return ((n ^ (n >> 16)) & 0xFFFF) / 32767.5 - 1.0

    acc = np.zeros(len(x))
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                acc += h(i + np.array([dx, dy, dz])) * w
    return acc.astype(np.float32)


def lumps(objs, amount, scale, seed=0, along_normal=True):
    """Organic swelling: vertices pushed along their normals by noise (tumours, fungal growth, rot)."""
    for o in objs:
        o.data.calc_normals_split() if hasattr(o.data, "calc_normals_split") else None
        for v in o.data.vertices:
            k = noise3(v.co, seed, scale) * 0.6 + noise3(v.co, seed + 1, scale * 2.7) * 0.4
            v.co += (v.normal if along_normal else Vector((0, 0, 1))) * (k * amount)
        o.data.update()


def cut(objs, keep):
    """Deletes the faces whose centre fails keep(centre): a broken edge, a missing half."""
    for o in objs:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        doomed = [f for f in bm.faces if not keep(f.calc_center_median())]
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        o.data.update()


def _emissive_image(mat):
    if not mat or not mat.use_nodes:
        return None
    bsdf = next((n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    return _linked_image(bsdf.inputs["Emission Color"])[0] if bsdf and "Emission Color" in bsdf.inputs else None


def cut_emissive(objs, threshold=0.3):
    """Deletes the faces its emission map lights (a lantern's glass), so what's inside the cage shows."""
    cache = {}
    for o in objs:
        uv = o.data.uv_layers.active
        if uv is None:
            continue
        bm = bmesh.new()
        bm.from_mesh(o.data)
        layer = bm.loops.layers.uv.active
        doomed = []
        for f in bm.faces:
            mat = o.data.materials[f.material_index] if f.material_index < len(o.data.materials) else None
            img = _emissive_image(mat)
            if img is None:
                continue
            if img.name not in cache:
                px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
                img.pixels.foreach_get(px)
                cache[img.name] = (px.reshape(img.size[1], img.size[0], 4), img.size[0], img.size[1])
            px, w, h = cache[img.name]
            u = sum(l[layer].uv.x for l in f.loops) / len(f.loops)
            v = sum(l[layer].uv.y for l in f.loops) / len(f.loops)
            x, y = int((u % 1) * (w - 1)), int((v % 1) * (h - 1))
            if px[y, x, :3].max() > threshold:
                doomed.append(f)
        bmesh.ops.delete(bm, geom=doomed, context="FACES")
        bm.to_mesh(o.data)
        bm.free()
        o.data.update()


def duplicate(objs):
    out = []
    for o in objs:
        c = o.copy()
        c.data = o.data.copy()
        bpy.context.scene.collection.objects.link(c)
        out.append(c)
    return out


def delete(objs):
    for o in objs:
        bpy.data.objects.remove(o, do_unlink=True)


# ----------------------------------------------------------------------------------------------------------------
# Baking a scan down (the 2008 way: a game mesh wearing the million-triangle original as maps)

def bake_down(objs, name, target, colour=None, size=1024, masks=None, paint=None):
    """Replaces a high-resolution (untextured) scan with a `target`-triangle copy UV-unwrapped and wearing the
    original's detail: a tangent-space normal map and ambient occlusion baked in Cycles from the full mesh, and a
    base colour of `colour` (linear RGB) darkened by that occlusion. `grime(ao, pos_z01) -> multiplier` may stain it
    (tar weeping down, soot in the folds). Returns [low]; the high mesh is deleted."""
    high = join(objs, name + "_high")
    lo, hi = bounds([high])
    extent = (hi - lo).length
    low = duplicate([high])[0]
    low.name = name
    decimate([low], target)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.select_all(action="DESELECT")
    low.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.004, area_weight=0.0, scale_to_bounds=True)
    bpy.ops.object.mode_set(mode="OBJECT")
    for f in low.data.polygons:
        f.use_smooth = True
    # Images to bake into, on a material the low mesh wears.
    nimg = bpy.data.images.new(name + "_normal", size, size, alpha=False, float_buffer=False)
    nimg.colorspace_settings.name = "Non-Color"
    aoimg = bpy.data.images.new(name + "_ao", size, size, alpha=False)
    aoimg.colorspace_settings.name = "Non-Color"
    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    tex = nt.nodes.new("ShaderNodeTexImage")
    low.data.materials.clear()
    low.data.materials.append(mat)
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.device = "CPU"
    scene.cycles.use_denoising = False
    scene.cycles.samples = 16
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.cage_extrusion = extent * 0.012
    scene.render.bake.max_ray_distance = extent * 0.03
    scene.render.bake.margin = 6
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    cimg = None
    bakes = [(nimg, "NORMAL"), (aoimg, "AO")]
    if colour is None:
        cimg = bpy.data.images.new(name + "_colour", size, size, alpha=False)
        bakes.append((cimg, "DIFFUSE"))
    for img, kind in bakes:
        tex.image = img
        nt.nodes.active = tex
        if kind == "NORMAL":
            scene.render.bake.normal_space = "TANGENT"
            bpy.ops.object.bake(type="NORMAL")
        elif kind == "DIFFUSE":
            scene.render.bake.use_pass_direct = False
            scene.render.bake.use_pass_indirect = False
            scene.render.bake.use_pass_color = True
            bpy.ops.object.bake(type="DIFFUSE")
        else:
            bpy.ops.object.bake(type="AO")
    baked = {}
    for mname, fn in sorted((masks or {}).items()):
        # The mask as the high mesh's vertex colour, emitted, baked across onto the low mesh's UVs.
        attr = high.data.color_attributes.new(mname, "FLOAT_COLOR", "POINT")
        co = np.empty(len(high.data.vertices) * 3, np.float32)
        high.data.vertices.foreach_get("co", co)
        m = np.clip(np.asarray(fn(co.reshape(-1, 3)), np.float32), 0, 1)
        attr.data.foreach_set("color", np.repeat(m, 4) * np.tile(np.array([1, 1, 1, 0], np.float32), len(m)) + np.tile(np.array([0, 0, 0, 1], np.float32), len(m)))
        em = bpy.data.materials.new(mname + "_emit")
        em.use_nodes = True
        ent = em.node_tree
        ent.nodes.remove(ent.nodes["Principled BSDF"])
        attr_node = ent.nodes.new("ShaderNodeVertexColor")
        attr_node.layer_name = mname
        emit = ent.nodes.new("ShaderNodeEmission")
        ent.links.new(attr_node.outputs["Color"], emit.inputs["Color"])
        ent.links.new(emit.outputs["Emission"], ent.nodes["Material Output"].inputs["Surface"])
        high.data.materials.clear()
        high.data.materials.append(em)
        mimg = bpy.data.images.new(name + "_" + mname, size, size, alpha=False)
        mimg.colorspace_settings.name = "Non-Color"
        tex.image = mimg
        nt.nodes.active = tex
        bpy.ops.object.bake(type="EMIT")
        baked[mname] = _image_array(mimg, size)[..., 0]
    delete([high])
    # The low mesh's material: colour x occlusion (and grime), the baked normal through a normal map node.
    ao = _image_array(aoimg, size)[..., 0]
    if cimg is not None:
        base = srgb_to_lin(_image_array(cimg, size)[..., :3])
    else:
        base = np.ones((size, size, 3), np.float32) * np.array(colour, np.float32)
    base = base * (0.35 + 0.65 * ao)[..., None]
    if paint is not None:
        base = paint(base, ao, baked)
    bimg = bpy.data.images.new(name + "_base", size, size, alpha=True)
    rgba = np.ones((size, size, 4), np.float32)
    rgba[..., :3] = lin_to_srgb(base)
    bimg.pixels.foreach_set(rgba[::-1].ravel())
    tex.image = bimg
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    ntex = nt.nodes.new("ShaderNodeTexImage")
    ntex.image = nimg
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Roughness"].default_value = 0.8
    bsdf.inputs["Metallic"].default_value = 0.0
    return [low]


# ----------------------------------------------------------------------------------------------------------------
# Materials to layers

def _image_array(img, size=SIZE):
    """An image's pixels, resized to size x size, as float32 HxWx4 in 0..1, top row first. Read straight from the
    image (a copy of a baked, in-memory image comes back blank), then box-filtered or repeated to size."""
    w, h = img.size
    px = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(px)
    a = px.reshape(h, w, 4)[::-1]
    if (w, h) == (size, size):
        return a.copy()
    if w % size == 0 and h % size == 0:
        return a.reshape(size, h // size, size, w // size, 4).mean((1, 3)).astype(np.float32)
    ys = (np.arange(size) * h // size).clip(0, h - 1)
    xs = (np.arange(size) * w // size).clip(0, w - 1)
    return a[ys][:, xs].copy()


def _linked_image(socket):
    """The image feeding a socket (through a Normal Map or Separate Color node), and the channel it comes out of."""
    if socket is None or not socket.is_linked:
        return None, None
    link = socket.links[0]
    node, out = link.from_node, link.from_socket.name
    if node.type == "TEX_IMAGE":
        return node.image, None
    if node.type == "NORMAL_MAP":
        return _linked_image(node.inputs["Color"])[0], "normal"
    if node.type in ("SEPARATE_COLOR", "SEPRGB", "SEPARATE_RGB"):
        img, _ = _linked_image(node.inputs[0])
        return img, {"Red": 0, "Green": 1, "Blue": 2, "R": 0, "G": 1, "B": 2}.get(out, 0)
    return None, None


def _occlusion(mat):
    """glTF's occlusion lives in the importer's settings group: the image and channel feeding it, if any."""
    for n in mat.node_tree.nodes:
        if n.type == "GROUP" and "Occlusion" in n.inputs:
            return _linked_image(n.inputs["Occlusion"])
    return None, None


def srgb_to_lin(x):
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(x):
    x = np.clip(x, 0, 1)
    return np.where(x <= 0.0031308, x * 12.92, 1.055 * np.power(x, 1 / 2.4) - 0.055)


def _save(arr, path):
    """Saves HxWxC float 0..1 (top row first) as an 8-bit PNG."""
    h, w, c = arr.shape
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., :c] = arr
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=True)
    img.pixels.foreach_set(rgba[::-1].ravel())
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def bake_layers(name, objs, grade=None, grime=0.0, family="model", source_ids=()):
    """Every material on `objs` becomes a layer <name>_<i> (its maps written to content/art/textures/models/), and is
    renamed to it, carrying the engine's extras. `grade(diffuse_linear, mask_info) -> diffuse` may push the palette
    (the hand pass); `grime` darkens toward soot in the crevices (occlusion) and low down."""
    os.makedirs(TEXTURES, exist_ok=True)
    mats = []
    for o in objs:
        for m in o.data.materials:
            if m is not None and m not in mats:
                mats.append(m)
    layers = []
    # A material marked dt_library names one of the texture library's layers (brick_soot, stone_block...): it's drawn
    # with that layer and the part's own UVs (metres over the layer's tile), and not baked.
    mats = [m for m in mats if not m.get("dt_library")]
    for i, m in enumerate(mats):
        layer = f"{name}_{i}"
        source_name = m.name
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if m.use_nodes else None
        base_img, _ = _linked_image(bsdf.inputs["Base Color"]) if bsdf else (None, None)
        spec_gloss_normal = None
        if bsdf is None and m.use_nodes:
            # KHR_materials_pbrSpecularGlossiness comes in as Diffuse and Glossy BSDFs: the diffuse's colour is the base,
            # any Normal Map node's image the normal.
            diff = next((n for n in m.node_tree.nodes if n.type == "BSDF_DIFFUSE"), None)
            if diff is not None:
                base_img, _ = _linked_image(diff.inputs["Color"])
            nm = next((n for n in m.node_tree.nodes if n.type == "NORMAL_MAP"), None)
            if nm is not None:
                spec_gloss_normal, _ = _linked_image(nm.inputs["Color"])
        if base_img is not None:
            base = _image_array(base_img)
            albedo = srgb_to_lin(base[..., :3])
            alpha = base[..., 3]
        else:
            c = bsdf.inputs["Base Color"].default_value if bsdf else (0.5, 0.5, 0.5, 1)
            albedo = np.broadcast_to(np.array(c[:3], np.float32), (SIZE, SIZE, 3)).copy()
            alpha = np.ones((SIZE, SIZE), np.float32)
        rough = np.full((SIZE, SIZE), bsdf.inputs["Roughness"].default_value if bsdf else 0.7, np.float32)
        metal = np.full((SIZE, SIZE), bsdf.inputs["Metallic"].default_value if bsdf else 0.0, np.float32)
        if bsdf:
            img, ch = _linked_image(bsdf.inputs["Roughness"])
            if img is not None:
                rough = _image_array(img)[..., ch if isinstance(ch, int) else 1]
            img, ch = _linked_image(bsdf.inputs["Metallic"])
            if img is not None:
                metal = _image_array(img)[..., ch if isinstance(ch, int) else 2]
        ao = np.ones((SIZE, SIZE), np.float32)
        img, ch = _occlusion(m)
        if img is not None:
            ao = _image_array(img)[..., ch if isinstance(ch, int) else 0]
        emissive = np.zeros((SIZE, SIZE), np.float32)
        flame = None
        if bsdf and "Emission Color" in bsdf.inputs:
            img, _ = _linked_image(bsdf.inputs["Emission Color"])
            if img is not None:
                e = _image_array(img)[..., :3]
                emissive = np.clip(e.max(-1) * 1.5, 0, 1)
                flame = srgb_to_lin(e)
        normal = None
        if bsdf or spec_gloss_normal is not None:
            img = spec_gloss_normal if bsdf is None else _linked_image(bsdf.inputs["Normal"])[0]
            if img is not None:
                n = _image_array(img)[..., :3].copy()
                n[..., 1] = 1 - n[..., 1]  # glTF's +Y (up the image) to tools/art's y-down
                normal = n
        # The legacy conversion (as tools/art/texgen/convert.pbr_to_legacy): occlusion into the diffuse, metals
        # darkened, speculars from metalness, gloss from roughness.
        diffuse = albedo * (1 - 0.7 * (1 - ao))[..., None]
        diffuse = diffuse * (1 - 0.55 * metal)[..., None]
        if flame is not None:
            # Where it's a light, it glows the light's colour (the engine draws emissive texels at their diffuse), not
            # whatever the glass was painted in the base colour.
            e = emissive[..., None]
            diffuse = diffuse * (1 - e) + flame / np.maximum(flame.max(-1, keepdims=True), 1e-3) * 0.55 * e
        if grime > 0:
            lum = diffuse.mean(-1)
            crev = np.clip((1 - ao) * 2.5, 0, 1)
            soot = np.array([0.012, 0.011, 0.010], np.float32)
            diffuse = diffuse * (1 - grime * crev)[..., None] + soot * (grime * crev)[..., None] * lum.mean()
        if grade is not None:
            # A grade may take the material's (source) name too, to treat parts differently.
            diffuse = grade(diffuse, source_name) if grade.__code__.co_argcount >= 2 else grade(diffuse)
        base_lum = albedo.mean(-1)
        spec = (0.06 * (1 - metal) + np.clip(lin_to_srgb(base_lum) * 0.9, 0, 1) * metal) * ao
        gloss = np.clip(1 - rough, 0.05, 0.85)
        _save(np.dstack([lin_to_srgb(diffuse), alpha]), os.path.join(TEXTURES, f"{layer}.png"))
        _save(np.dstack([spec, gloss, emissive]), os.path.join(TEXTURES, f"{layer}_s.png"))
        if normal is not None:
            _save(normal, os.path.join(TEXTURES, f"{layer}_n.png"))
        alpha_test = bool((alpha < 0.5).mean() > 0.01)
        mean = [float(x) for x in lin_to_srgb(diffuse.reshape(-1, 3).mean(0))]
        m.name = layer
        if bsdf:
            bsdf.inputs["Base Color"].default_value = (*[c ** 2.2 for c in mean], 1)
        m["dt_shine"] = float(np.clip(spec.mean() * 2, 0, 1))
        m["dt_emissive"] = 0.0
        m["dt_glow"] = float(1.0 if emissive.max() > 0.2 else 0.0)
        layers.append({
            "name": layer,
            "diffuse": f"models/{layer}.png",
            "spec": f"models/{layer}_s.png",
            **({"normal": f"models/{layer}_n.png"} if normal is not None else {}),
            "size": SIZE,
            "tileMetres": None,
            "tiling": False,
            "family": family,
            "alphaTest": alpha_test,
            "sources": [manifest()[s] | {"files": None} for s in source_ids],
        })
    return layers


# ----------------------------------------------------------------------------------------------------------------
# Rigging and export

def rig_and_export(name, objs, sockets=None):
    """One deforming root bone, plus a non-deforming socket bone per `sockets` entry (name -> position): where the
    engine puts a prop's light, or hangs it from."""
    arm = bpy.data.armatures.new("SK_Prop")
    rig = bpy.data.objects.new("SK_Prop", arm)
    bpy.context.scene.collection.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    b = arm.edit_bones.new("root")
    b.head, b.tail = (0, 0, 0), (0, 0.2, 0)
    for sname, at in sorted((sockets or {}).items()):
        s = arm.edit_bones.new(sname)
        s.head, s.tail = Vector(at), Vector(at) + Vector((0, 0.05, 0))
        s.parent = b
        s.use_deform = False
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in objs:
        # Only what the engine reads: one UV layer and the materials (stray colour attributes and extra UV maps from
        # joined parts upset the exporter, which then drops the mesh without a word).
        for a in list(o.data.color_attributes):
            o.data.color_attributes.remove(a)
        # Decimating a join can leave degenerate faces; an invalid mesh exports as nothing.
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        o.data.validate(clean_customdata=True)
        uv = o.data.uv_layers.active
        for layer in list(o.data.uv_layers):
            if uv is not None and layer.name != uv.name:
                o.data.uv_layers.remove(layer)
        g = o.vertex_groups.new(name="root")
        g.add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
        mod = o.modifiers.new("Armature", "ARMATURE")
        mod.object = rig
        o.parent = rig
    os.makedirs(PROPS, exist_ok=True)
    path = os.path.join(PROPS, f"{name}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False, export_animations=False,
        export_def_bones=False, export_extras=True, export_skins=True, export_morph=False, export_texcoords=True,
        export_normals=True, export_tangents=False, export_materials="EXPORT", export_image_format="NONE",
        export_cameras=False, export_lights=False, export_all_influences=False)
    return path


def _merge_index(name, layers):
    path = os.path.join(os.path.dirname(TEXTURES), "index.models.json")
    entries = json.load(open(path)) if os.path.exists(path) else []
    entries = [e for e in entries if not e["name"].startswith(name + "_")] + layers
    entries.sort(key=lambda e: e["name"])
    with open(path, "w") as f:
        json.dump(entries, f, indent=2)
        f.write("\n")


def library_material(layer, shine=0.1):
    """A material that is the texture library's `layer` (content/art/textures/index.json), not a baked one."""
    m = bpy.data.materials.get(layer) or bpy.data.materials.new(layer)
    m["dt_library"] = True
    m["dt_shine"] = shine
    m["dt_emissive"] = 0.0
    m["dt_glow"] = 0.0
    return m


def grid(size_x, size_z, cuts, material, tile, name="grid"):
    """A flat wall panel in the XZ plane facing -Y... +Y (the model's front), subdivided, UV'd in metres over `tile`."""
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=cuts, y_subdivisions=cuts, size=1)
    o = bpy.context.view_layer.objects.active
    o.name = name
    for v in o.data.vertices:
        x, y = v.co.x, v.co.y
        v.co = Vector((x * size_x, 0, y * size_z))
    o.data.materials.append(material)
    uv = o.data.uv_layers.new(name="UVMap")
    for poly in o.data.polygons:
        for li in poly.loop_indices:
            co = o.data.vertices[o.data.loops[li].vertex_index].co
            uv.data[li].uv = (co.x / tile, co.z / tile)
    # Facing +Y (the model's front): the grid is built facing +Z, turned up into the XZ plane.
    bm = bmesh.new()
    bm.from_mesh(o.data)
    for f in bm.faces:
        if f.normal.y > 0:
            continue
        f.normal_flip()
    bm.to_mesh(o.data)
    bm.free()
    return o


def share_materials(objs):
    """Materials that read the same images are one material (the same model imported twice is one layer, not two)."""
    seen = {}
    for o in objs:
        for i, m in enumerate(o.data.materials):
            if m is None or not m.use_nodes:
                continue
            key = tuple(sorted(n.image.name.split(".")[0] for n in m.node_tree.nodes if n.type == "TEX_IMAGE" and n.image))
            if not key:
                continue
            if key in seen:
                o.data.materials[i] = seen[key]
            else:
                seen[key] = m


def finish(name, objs, budget, grade=None, grime=0.0, family="model", sockets=None):
    """Decimate, bake the layers, index them, rig and export. Prints the one [dt] line build.sh keeps."""
    share_materials(objs)
    decimate(objs, budget)
    for o in objs:
        for f in o.data.polygons:
            f.use_smooth = True
    sources = sorted({o.get("dt_source") for o in objs if o.get("dt_source")})
    layers = bake_layers(name, objs, grade=grade, grime=grime, family=family, source_ids=sources)
    _merge_index(name, layers)
    lo, hi = bounds(objs)
    path = rig_and_export(name, objs, sockets)
    size = hi - lo
    print(f"[dt] {name}: {tris(objs)} tris, {len(layers)} layers, {size.x:.2f} x {size.y:.2f} x {size.z:.2f} m, "
          f"from {', '.join(sources)} -> {os.path.relpath(path, ROOT)}")


def weathered(d):
    """Old timber and iron gone grey under soot: desaturated, darkened, a little brown left in it."""
    lum = d.mean(-1, keepdims=True)
    return (lum * 0.7 + d * 0.3) * np.array([0.78, 0.74, 0.68], np.float32)


# ----------------------------------------------------------------------------------------------------------------
# Creatures from scans: rigged on their own pose

def rig_creature(name, meshes, bones, clips, rigid=None, plant=None):
    """Rigs baked-down scan meshes on a skeleton placed on the scan's own pose and exports content/art/models/<name>.glb
    with `clips` (tools/blender/rig Clips). `bones` is [(name, parent, head, tail)], placed where the scan's joints are;
    each vertex is weighted to its nearest bones (inverse distance to the bone's segment, the closest four), except the
    meshes in `rigid` ({mesh: bone}), which ride one bone (eyes on the head). Returns the path."""
    sys.path.insert(0, os.path.join(os.path.dirname(HERE), "blender"))
    import rig as blender_rig
    sk = blender_rig.Skeleton("SK_Human", [blender_rig.Bone(n, p, h, t) for n, p, h, t in bones])
    arm_obj = sk.build()
    names = [b.name for b in sk.bones if b.name != "root"]
    rigid = rigid or {}
    for obj in meshes:
        co = np.empty(len(obj.data.vertices) * 3, np.float32)
        obj.data.vertices.foreach_get("co", co)
        co = co.reshape(-1, 3)
        if obj in rigid:
            g = obj.vertex_groups.new(name=rigid[obj])
            g.add(list(range(len(co))), 1.0, "REPLACE")
        else:
            d = np.empty((len(co), len(names)), np.float32)
            for i, n in enumerate(names):
                b = sk[n]
                a, c = np.array(b.head, np.float32), np.array(b.tail, np.float32)
                ab = c - a
                t = np.clip(((co - a) @ ab) / max(float(ab @ ab), 1e-9), 0, 1)
                d[:, i] = np.linalg.norm(co - (a + t[:, None] * ab), axis=1)
            w = 1.0 / (d + 0.015) ** 6
            top = np.argsort(-w, axis=1)[:, :4]
            groups = {n: obj.vertex_groups.new(name=n) for n in names}
            for vi in range(len(co)):
                ws = w[vi, top[vi]]
                ws = ws / ws.sum()
                for k, bi in enumerate(top[vi]):
                    if ws[k] > 0.03:
                        groups[names[bi]].add([vi], float(ws[k]), "REPLACE")
        for a in list(obj.data.color_attributes):
            obj.data.color_attributes.remove(a)
        bm = bmesh.new()
        bm.from_mesh(obj.data)
        bmesh.ops.dissolve_degenerate(bm, dist=1e-6, edges=bm.edges)
        bm.to_mesh(obj.data)
        bm.free()
        obj.data.validate(clean_customdata=True)
        for f in obj.data.polygons:
            f.use_smooth = True
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = arm_obj
        obj.parent = arm_obj
    blender_rig.bake(sk, clips, plant=plant(sk) if plant else None)
    path = os.path.join(ROOT, "content", "art", "models", f"{name}.glb")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.gltf(
        filepath=path, export_format="GLB", export_yup=True, export_apply=False,
        export_animations=True, export_animation_mode="ACTIONS", export_force_sampling=True, export_frame_step=1,
        export_optimize_animation_size=False, export_anim_slide_to_zero=True, export_def_bones=False,
        export_extras=True, export_skins=True, export_morph=False, export_texcoords=True, export_normals=True,
        export_tangents=False, export_materials="EXPORT", export_image_format="NONE", export_cameras=False,
        export_lights=False, export_all_influences=False, export_reset_pose_bones=True)
    print(f"[dt] {name}: {tris(meshes)} tris, {len(sk.bones)} bones -> {os.path.relpath(path, ROOT)}")
    return path


def eyes_at(points, radius, colour=(0.69, 0.64, 0.5), name="eye"):
    """Small emissive spheres (a creature's eyes: pure light, drawn at full colour)."""
    mat = bpy.data.materials.new(f"{name}.light")
    mat.use_nodes = True
    mat.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*colour, 1)
    mat["dt_library"] = True
    mat["dt_emissive"] = 1.0
    mat["dt_shine"] = 0.0
    mat["dt_glow"] = 0.0
    out = []
    for i, at in enumerate(points):
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=1, radius=radius)
        e = bpy.context.view_layer.objects.active
        # Placed in the mesh itself (like everything cook makes), so a later cook.transform carries it along.
        e.data.transform(Matrix.Translation(Vector(at)))
        e.name = f"{name}_{i}"
        e.data.materials.append(mat)
        out.append(e)
    return out
