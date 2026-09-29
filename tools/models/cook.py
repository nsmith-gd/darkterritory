"""The model cook: turns sourced models (tools/models/sources.json, fetched by fetch.py) into the engine's props, run
headless in Blender by tools/models/build.sh. A recipe (tools/models/recipes/<name>.py) imports what it needs,
bashes it into the thing it wants (cuts, bends, grime, parts of one model on another), and calls `finish()`, which:

  * decimates to the recipe's triangle budget and puts the pivot where the engine wants it (floor centre, metres,
    the model facing -Y in Blender, which the exporter turns into the engine's -Z forward);
  * bakes each material's PBR maps into the engine's layer format at 512 (ARCHITECTURE §8 note 54): a diffuse (base
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
# Materials to layers

def _image_array(img, size=SIZE):
    """An image's pixels, resized to size x size, as float32 HxWx4 in 0..1, top row first."""
    im = img.copy()
    im.scale(size, size)
    px = np.empty(size * size * 4, np.float32)
    im.pixels.foreach_get(px)
    bpy.data.images.remove(im)
    return px.reshape(size, size, 4)[::-1].copy()


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
    for i, m in enumerate(mats):
        layer = f"{name}_{i}"
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None) if m.use_nodes else None
        base_img, _ = _linked_image(bsdf.inputs["Base Color"]) if bsdf else (None, None)
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
        if bsdf:
            img, _ = _linked_image(bsdf.inputs["Normal"])
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
            diffuse = grade(diffuse)
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


def finish(name, objs, budget, grade=None, grime=0.0, family="model", sockets=None):
    """Decimate, bake the layers, index them, rig and export. Prints the one [dt] line build.sh keeps."""
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
